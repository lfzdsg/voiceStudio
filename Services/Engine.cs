using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using VoiceStudio.Core;

namespace VoiceStudio.Services;
public static class Paths
{
    public static string Root { get; } = FindRoot();
    public static string Models => Path.Combine(Root, "models");
    public static string Settings => Path.Combine(Root, ".runtime", "settings.json");
    private static string FindRoot()
    {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "engine", "voice-worker", "voice-worker.exe"))) return AppContext.BaseDirectory;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "VoiceStudio.csproj"))) return dir.FullName;
        return AppContext.BaseDirectory;
    }
    public static ProcessStartInfo EngineStart(string? rootPath = null)
    {
        string root = rootPath ?? Root;
        var exe = Path.Combine(root, "engine", "voice-worker", "voice-worker.exe");
        var python = Path.Combine(root, ".venv", "Scripts", "python.exe");
        var start = new ProcessStartInfo
        {
            FileName = File.Exists(exe) ? exe : python,
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8, StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        if (!File.Exists(exe))
        {
            if (!File.Exists(python)) throw new InvalidOperationException("识别环境尚未安装。请先运行 scripts/setup.ps1，再重新打开程序。");
            start.ArgumentList.Add("-u");
            start.ArgumentList.Add(Path.Combine(root, "worker", "main.py"));
        }
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["HF_HUB_DISABLE_SYMLINKS_WARNING"] = "1";
        return start;
    }
    public static bool ModelReady(string model) => File.Exists(Path.Combine(Models, model, ".ready")) && File.Exists(Path.Combine(Models, model, model == "sensevoice-small" ? "model.int8.onnx" : "model.bin"));
    public static bool TranslationReady => System.Linq.Enumerable.All(new[] { "en_zh", "zh_en", "en_ja", "ja_en" }, pair => File.Exists(Path.Combine(Models, "translation", pair, ".ready")));
    public static bool QwenReady => File.Exists(Path.Combine(Models, "qwen3-4b-instruct", ".ready")) && File.Exists(Path.Combine(Models, "qwen3-4b-instruct", "Qwen3-4B-Instruct-2507-Q4_K_M.gguf")) && File.Exists(Path.Combine(Root, "engine", "llama", ".ready")) && File.Exists(Path.Combine(Root, "engine", "llama", "llama-server.exe"));
}

public sealed class Engine : IAsyncDisposable
{
    private readonly NamedPipeServerStream pipe;
    private readonly string root;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<byte[]> output = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(100) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? readTask, writeTask;
    private Process? process;
    private int disposed;
    private int failed;
    private bool finishing;
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public event Action<JsonElement>? Event;
    public event Action<string>? Failed;

    public Engine(string? rootPath = null)
    {
        root = rootPath == null ? Paths.Root : Path.GetFullPath(rootPath);
        pipe = new NamedPipeServerStream("VoiceStudio-" + SessionId, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }
    public async Task StartAsync(string model, string language, string translationTarget = "off", string device = "cpu", string translationBackend = "argos")
    {
        var start = Paths.EngineStart(root);
        start.ArgumentList.Add("--pipe"); start.ArgumentList.Add("VoiceStudio-" + SessionId);
        process = new Process { StartInfo = start };
        process.Start();
        var errors = process.StandardError.ReadToEndAsync();
        _ = WatchExit(process, errors);
        _ = process.StandardOutput.ReadToEndAsync();
        using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        connectionTimeout.CancelAfter(TimeSpan.FromSeconds(25));
        var connected = pipe.WaitForConnectionAsync(connectionTimeout.Token);
        if (await Task.WhenAny(connected, ready.Task) == ready.Task) await ready.Task;
        await connected;
        readTask = ReadLoop();
        writeTask = WriteLoop();
        Send(PipeProtocol.Json(new { type = "start", protocol = 1, sessionId = SessionId, modelPath = Path.Combine(root, "models", model), language, translationTarget, device, translationBackend }));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(90), lifetime.Token);
    }
    public void Audio(long offset, byte[] data) => Send(PipeProtocol.Audio(offset, data));
    public void Tick(long position) => Send(PipeProtocol.Json(new { type = "tick", position }));
    private void Send(byte[] message)
    {
        if (disposed != 0) return;
        if (!output.Writer.TryWrite(message)) ReportFailure("音频发送缓冲已满，采集已停止。请降低模型大小后重试。");
    }
    private async Task WriteLoop()
    {
        try
        {
            await foreach (var packet in output.Reader.ReadAllAsync(lifetime.Token)) await pipe.WriteAsync(packet, lifetime.Token);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
            if (disposed == 0) ReportFailure("识别连接中断：" + e.Message);
        }
    }
    private async Task ReadLoop()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var message = await PipeProtocol.ReadJsonAsync(pipe, lifetime.Token);
                if (message.GetProperty("sessionId").GetString() != SessionId) continue;
                var type = message.GetProperty("type").GetString();
                if (type == "ready") ready.TrySetResult();
                if (type == "error") { ReportFailure(message.GetProperty("message").GetString() ?? "识别失败"); return; }
                Event?.Invoke(message);
                if (type == "done") { done.TrySetResult(); return; }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or JsonException)
        {
            if (disposed == 0 && !done.Task.IsCompleted) ReportFailure("识别连接中断：" + e.Message);
        }
    }
    private void ReportFailure(string message)
    {
        if (Interlocked.Exchange(ref failed, 1) != 0) return;
        ready.TrySetException(new InvalidOperationException(message));
        done.TrySetException(new InvalidOperationException(message));
        Failed?.Invoke(message);
    }
    private async Task WatchExit(Process child, Task<string> errors)
    {
        try
        {
            await child.WaitForExitAsync();
            string detail = (await errors).Trim();
            if (detail.Length > 600) detail = detail[^600..];
            if (!finishing && disposed == 0) ReportFailure("识别进程已退出。" + detail);
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { }
    }
    public async Task FinishAsync()
    {
        finishing = true;
        Send(PipeProtocol.Json(new { type = "stop" }));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        output.Writer.TryComplete();
        pipe.Dispose();
        if (process != null)
        {
            try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } } catch (InvalidOperationException) { }
            process.Dispose();
        }
        if (readTask != null) await readTask;
        if (writeTask != null) await writeTask;
        lifetime.Dispose();
    }
}
