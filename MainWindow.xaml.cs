using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using VoiceStudio.Core;
using VoiceStudio.Services;
using Forms = System.Windows.Forms;

namespace VoiceStudio;
public partial class MainWindow : Window
{
    private readonly ObservableCollection<Caption> captions = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch sessionClock = new();
    private OverlayWindow? overlay;
    private Engine? engine;
    private AudioCapture? capture;
    private Forms.NotifyIcon? tray;
    private Process? downloader;
    private bool running, busy, downloading, dirty, quitting, initialized;
    private int ticks;
    private long lastAudioMs;
    private string selectedDevice = "";
    private bool selectedMicrophone;
    private int lastCaptionId;
    private int overlayCaptionId;
    private string overlayOriginal = "", overlayTranslation = "";
    private string sessionTranslationTarget = "off";
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    public MainWindow()
    {
        InitializeComponent();
        CaptionList.ItemsSource = captions;
        Loaded += OnLoaded;
        Closing += OnClosing;
        timer.Tick += Tick;
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        overlay = new OverlayWindow();
        RefreshDevices();
        RefreshScreens();
        LoadSettings();
        initialized = true;
        UpdateModel();
        UpdateTranslation();
        ApplyOverlayStyle();
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
        if (!(RegisterHotKey(handle, 1, 0x4003, 0x53) & RegisterHotKey(handle, 2, 0x4003, 0x48) & RegisterHotKey(handle, 3, 0x4003, 0x4C)))
            StatusText.Text = "部分快捷键被其他程序占用，请使用窗口或托盘操作。";
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Voice Studio · 实时字幕", Visible = true };
        tray.DoubleClick += (_, _) => RestoreWindow();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 Voice Studio", null, (_, _) => RestoreWindow());
        menu.Items.Add("开始 / 停止字幕", null, async (_, _) => await ToggleAsync());
        menu.Items.Add("显示 / 隐藏字幕", null, (_, _) => ToggleOverlay());
        menu.Items.Add("退出", null, (_, _) => Close());
        tray.ContextMenuStrip = menu;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        timer.Start();
    }
    private void RestoreWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x312)
        {
            handled = true;
            switch (wParam.ToInt32())
            {
                case 1: _ = ToggleAsync(); break;
                case 2: ToggleOverlay(); break;
                case 3: LockOverlayBox.IsChecked = !LockOverlayBox.IsChecked; ApplyOverlayStyle(); break;
            }
        }
        if (msg == 0x007E) { RefreshScreens(); overlay?.PlaceOnScreen(ScreenBox.SelectedIndex); }
        return IntPtr.Zero;
    }
    private string Model => (ModelBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "base";
    private string RecognitionLanguage => (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";
    private string TranslationTarget => (TranslationBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "zh";
    private string TranslationBackend => (TranslationBackendBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "qwen";
    private bool TranslationInstalled => TranslationBackend == "qwen" ? Paths.QwenReady : Paths.TranslationReady;
    private string ComputeDevice => (ComputeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";
    private CaptionMode DisplayMode => (CaptionMode)Math.Clamp(DisplayBox.SelectedIndex, 0, 2);
    private void RefreshDevices()
    {
        try { DeviceBox.ItemsSource = AudioCapture.Devices(); DeviceBox.SelectedIndex = 0; }
        catch (Exception ex) { StatusText.Text = "无法读取播放设备：" + ex.Message; }
    }
    private void RefreshScreens()
    {
        int previous = ScreenBox.SelectedIndex;
        ScreenBox.ItemsSource = Forms.Screen.AllScreens.Select((s, i) => $"显示器 {i + 1}{(s.Primary ? " · 主屏幕" : "")}").ToArray();
        ScreenBox.SelectedIndex = Math.Clamp(previous, 0, Forms.Screen.AllScreens.Length - 1);
    }
    private void RefreshDevices_Click(object sender, RoutedEventArgs e) => RefreshDevices();
    private void Model_Changed(object sender, SelectionChangedEventArgs e) { if (initialized) UpdateModel(); }
    private void UpdateModel()
    {
        bool installed = Paths.ModelReady(Model);
        string size = Model switch { "tiny" => "80 MB", "base" => "150 MB", "small" => "500 MB", "medium" => "1.5 GB", "large-v3-turbo" => "1.6 GB", "large-v3" => "3.1 GB", "sensevoice-small" => "240 MB", _ => "未知" };
        ModelStatus.Text = installed ? $"{Model} 已就绪" : $"{Model} 尚未安装 · 约 {size}";
        ModelStatus.ToolTip = Path.Combine(Paths.Models, Model);
        DownloadButton.Content = installed ? "模型已安装" : "下载模型";
        DownloadButton.IsEnabled = !installed && !running && !busy;
    }
    private void UpdateTranslation()
    {
        bool installed = TranslationInstalled;
        TranslationStatus.Text = TranslationBackend == "qwen"
            ? (installed ? "Qwen3 4B 已就绪 · 中日英直译" : "需要 Qwen3 4B · 模型约 2.5 GB，另含引擎")
            : (installed ? "中日英翻译包已安装 · 日中经英语中转" : "需要中日英翻译包 · 日中经英语中转");
        TranslationDownloadButton.Content = installed ? "翻译模型已安装" : TranslationBackend == "qwen" ? "下载 Qwen 模型与引擎" : "下载中日英翻译包";
        TranslationDownloadButton.IsEnabled = !installed && !running && !busy && !downloading;
    }
    private void Translation_Changed(object sender, SelectionChangedEventArgs e) { if (initialized) UpdateTranslation(); }
    private void Display_Changed(object sender, SelectionChangedEventArgs e) { if (initialized) ApplyOverlayStyle(); }
    private async void Start_Click(object sender, RoutedEventArgs e) => await ToggleAsync();
    private async Task ToggleAsync()
    {
        if (busy || downloading || quitting) return;
        if (running) { await StopAsync(); return; }
        if (!Paths.ModelReady(Model)) { StatusText.Text = "请先点击右侧「下载模型」，完成后即可开始。"; return; }
        if (TranslationTarget != "off" && !TranslationInstalled) { StatusText.Text = "请先下载所选翻译模型，或暂时选择「关闭翻译」。"; return; }
        if (DeviceBox.SelectedItem is not AudioDevice device) { StatusText.Text = "没有可用的播放设备，请连接耳机或扬声器后刷新。"; return; }
        if (dirty && MessageBox.Show(this, "开始新会话会清除未导出的字幕，继续吗？", "新会话", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        busy = true;
        UpdateControls();
        captions.Clear(); dirty = false; lastCaptionId = overlayCaptionId = 0; UpdateCount();
        overlayOriginal = overlayTranslation = "";
        sessionTranslationTarget = TranslationTarget;
        StatusText.Text = "正在启动本机识别…";
        PreviewText.Text = "正在加载模型，请稍候…";
        try
        {
            var activeEngine = new Engine();
            engine = activeEngine;
            activeEngine.Event += message => Dispatcher.Invoke(() => { if (engine == activeEngine) HandleEvent(message); });
            activeEngine.Failed += error => Dispatcher.BeginInvoke(async () => { if (engine == activeEngine && running && !busy) await StopAsync(error); });
            await activeEngine.StartAsync(Model, RecognitionLanguage, sessionTranslationTarget, ComputeDevice, TranslationBackend);
            selectedDevice = device.Id;
            selectedMicrophone = device.Microphone;
            capture = new AudioCapture();
            capture.Audio += (position, audio) => { lastAudioMs = sessionClock.ElapsedMilliseconds; activeEngine.Audio(position, audio); };
            capture.Failed += ex => Dispatcher.BeginInvoke(async () => { if (running && !busy) await StopAsync("采集已停止：" + ex.Message); });
            sessionClock.Restart();
            capture.Start(selectedDevice, selectedMicrophone);
            running = true;
            lastAudioMs = 0;
            PreviewText.Text = "等待语音…";
            StatusText.Text = selectedMicrophone ? "正在聆听 · 麦克风输入，说话时音量条会变化" : "正在聆听 · 电脑播放声音（如需识别自己说话，请选择麦克风）";
            if (ShowOverlayBox.IsChecked == true) ShowOverlay();
            overlay?.SetText("等待语音…");
        }
        catch (Exception ex)
        {
            await CleanupAsync();
            StatusText.Text = ex is UnauthorizedAccessException && device.Microphone
                ? "无法访问麦克风：请在 Windows 设置中允许桌面应用访问麦克风，并确认设备未被独占。"
                : "无法开始：" + ex.Message;
            PreviewText.Text = "启动失败，请检查下方提示。";
        }
        finally { busy = false; UpdateControls(); }
    }
    private async Task StopAsync(string? error = null)
    {
        if (busy) return;
        busy = true; running = false;
        UpdateControls();
        capture?.Dispose(); capture = null;
        sessionClock.Stop();
        StatusText.Text = error ?? "正在整理最后一段字幕…";
        try { if (error == null && engine != null) await engine.FinishAsync(); }
        catch (Exception ex) { error = "最后一段未完成：" + ex.Message; }
        finally
        {
            await CleanupAsync();
            busy = false; UpdateControls();
            PreviewText.Text = error == null ? "本次采集已结束，可以导出字幕。" : "采集已停止，已有字幕仍可导出。";
            StatusText.Text = error ?? $"已停止 · 共 {captions.Count} 条字幕";
            overlay?.SetText(error == null ? "已停止" : "采集已停止，请查看主窗口");
        }
    }
    private async Task CleanupAsync()
    {
        capture?.Dispose(); capture = null;
        var old = engine; engine = null;
        if (old != null) await old.DisposeAsync();
        running = false; sessionClock.Stop(); LevelMeter.Value = 0;
    }
    private void HandleEvent(JsonElement message)
    {
        string? type = message.GetProperty("type").GetString();
        if (type == "status") { StatusText.Text = message.GetProperty("message").GetString(); return; }
        if (type == "ready")
        {
            ModelStatus.Text = $"{Model} · {message.GetProperty("device").GetString()} / {message.GetProperty("computeType").GetString()}";
            if (message.TryGetProperty("warning", out var warning) && !string.IsNullOrEmpty(warning.GetString())) ModelStatus.Text += "\n" + warning.GetString();
            if (message.TryGetProperty("translationDevice", out var translationDevice) && translationDevice.GetString() != "off") ModelStatus.Text += "\n翻译设备：" + translationDevice.GetString();
            return;
        }
        if (type is "translation" or "translation_partial")
        {
            if (message.TryGetProperty("inferenceMs", out var translationMs))
                TranslationStatus.Text = $"翻译 {translationMs.GetInt32()} ms · 排队 {message.GetProperty("waitMs").GetInt32()} ms\n{(TranslationBackend == "qwen" ? "Qwen 直译" : "日中经英语中转")} · 临时译文会修订";
        }
        if (type == "translation_partial")
        {
            int translatedId = message.GetProperty("segmentId").GetInt32();
            string source = message.GetProperty("sourceText").GetString() ?? "";
            if (translatedId > lastCaptionId && translatedId == overlayCaptionId && source == overlayOriginal)
            {
                overlayTranslation = message.GetProperty("text").GetString() ?? "";
                overlay?.SetCaption(source, overlayTranslation);
                PreviewText.Text = source + "\n" + overlayTranslation + "（临时译文）";
            }
            return;
        }
        if (type is "translation" or "translation_error")
        {
            int translatedId = message.GetProperty("segmentId").GetInt32();
            var existing = captions.FirstOrDefault(c => c.SegmentId == translatedId);
            if (existing == null) return;
            var updated = type == "translation"
                ? existing with { Translation = message.GetProperty("text").GetString() ?? "", TranslationError = "" }
                : existing with { TranslationError = "翻译未完成：" + message.GetProperty("message").GetString() };
            captions[captions.IndexOf(existing)] = updated;
            dirty = true;
            if (translatedId == overlayCaptionId)
            {
                overlayTranslation = updated.Translation.Length > 0 ? updated.Translation : updated.Text;
                overlay?.SetCaption(updated.Text, overlayTranslation);
            }
            if (type == "translation_error") TranslationStatus.Text = updated.TranslationError;
            return;
        }
        if (type is not ("partial" or "final")) return;
        string text = message.GetProperty("text").GetString() ?? "";
        int id = message.GetProperty("segmentId").GetInt32();
        if (id <= lastCaptionId) return;
        if (type == "final")
        {
            lastCaptionId = id;
            if (!string.IsNullOrWhiteSpace(text))
            {
                var caption = new Caption(id, message.GetProperty("startMs").GetInt64(), message.GetProperty("endMs").GetInt64(), text);
                captions.Add(caption); dirty = true; UpdateCount(); CaptionList.ScrollIntoView(caption);
            }
            PreviewText.Text = running ? "正在聆听下一句…" : "正在整理字幕…";
        }
        else PreviewText.Text = string.IsNullOrEmpty(text) ? "正在识别…" : text;
        if (id != overlayCaptionId || text != overlayOriginal) overlayTranslation = "";
        overlayCaptionId = id;
        overlayOriginal = text;
        if (string.IsNullOrEmpty(text)) overlay?.SetText("等待语音…");
        else overlay?.SetCaption(text, overlayTranslation);
        int queued = message.GetProperty("queued").GetInt32();
        StatusText.Text = queued > 0 ? $"识别中 · 有 {queued} 段排队，可选择更快的模型" : $"正在聆听 · 本次推理 {message.GetProperty("inferenceMs").GetInt32()} ms";
        if (captions.Count >= 10000 && running && !busy) _ = StopAsync("已达到 10000 条会话上限，请导出后开始新会话。");
    }
    private void Tick(object? sender, EventArgs e)
    {
        ElapsedText.Text = sessionClock.Elapsed.ToString(@"hh\:mm\:ss");
        if (!running || busy || capture == null) return;
        bool quiet = sessionClock.ElapsedMilliseconds - lastAudioMs > 700;
        LevelMeter.Value = quiet ? 0 : capture.Peak;
        if (++ticks % 4 != 0) return;
        engine?.Tick(capture.ElapsedSamples);
        overlay?.ClampToScreen();
        if (quiet) StatusText.Text = selectedMicrophone ? "等待麦克风声音 · 请检查输入设备与 Windows 麦克风权限" : "等待电脑播放声音 · 自己说话需选择麦克风";
        if (selectedDevice.Length == 0)
        {
            try { if (capture.DeviceId != AudioCapture.DefaultId(selectedMicrophone)) _ = StopAsync("默认音频设备已改变，请重新开始以连接新设备。"); }
            catch (Exception) { _ = StopAsync("音频设备已断开，请连接设备并刷新。"); }
        }
    }
    private void UpdateControls()
    {
        StartButton.Content = busy ? "处理中…" : running ? "停止字幕" : "开始字幕";
        StartButton.IsEnabled = !busy && !downloading;
        ModelBox.IsEnabled = LanguageBox.IsEnabled = DeviceBox.IsEnabled = RefreshButton.IsEnabled = TranslationBox.IsEnabled = TranslationBackendBox.IsEnabled = ComputeBox.IsEnabled = !busy && !running && !downloading;
        ExportSrtButton.IsEnabled = ExportTextButton.IsEnabled = DeleteButton.IsEnabled = !running && !busy && captions.Count > 0;
        if (!downloading) { if (!running) UpdateModel(); UpdateTranslation(); }
    }
    private void UpdateCount() { CountText.Text = $"{captions.Count} 条"; EmptyText.Visibility = captions.Count == 0 ? Visibility.Visible : Visibility.Collapsed; }
    private void ShowOverlay() { overlay?.Show(); overlay?.PlaceOnScreen(ScreenBox.SelectedIndex); ApplyOverlayStyle(); }
    private void ToggleOverlay() { ShowOverlayBox.IsChecked = !ShowOverlayBox.IsChecked; if (ShowOverlayBox.IsChecked == true) ShowOverlay(); else overlay?.Hide(); }
    private void OverlayOptions_Changed(object sender, RoutedEventArgs e) { if (ShowOverlayBox.IsChecked == true) ShowOverlay(); else overlay?.Hide(); ApplyOverlayStyle(); }
    private void OverlayStyle_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (initialized) ApplyOverlayStyle(); }
    private void ApplyOverlayStyle() { overlay?.SetStyle(FontSlider.Value, OpacitySlider.Value); overlay?.SetLocked(LockOverlayBox.IsChecked == true); overlay?.SetMode(sessionTranslationTarget == "off" ? CaptionMode.Original : DisplayMode); }
    private void Screen_Changed(object sender, SelectionChangedEventArgs e) { if (initialized) overlay?.PlaceOnScreen(ScreenBox.SelectedIndex); }
    private async void Download_Click(object sender, RoutedEventArgs e) => await DownloadAsync(false);
    private async void TranslationDownload_Click(object sender, RoutedEventArgs e) => await DownloadAsync(true);
    private async Task DownloadAsync(bool translation)
    {
        if (downloading) { try { downloader?.Kill(true); } catch (InvalidOperationException) { } return; }
        downloading = true; UpdateControls(); DownloadButton.IsEnabled = false; TranslationDownloadButton.IsEnabled = false;
        var downloadButton = translation ? TranslationDownloadButton : DownloadButton;
        var downloadStatus = translation ? TranslationStatus : ModelStatus;
        downloadButton.Content = "取消下载"; downloadButton.IsEnabled = true;
        StatusText.Text = "正在下载模型…";
        DownloadProgress.Visibility = Visibility.Visible; DownloadProgress.IsIndeterminate = true;
        try
        {
            var start = Paths.EngineStart();
            if (translation) start.ArgumentList.Add(TranslationBackend == "qwen" ? "--download-qwen" : "--download-translation");
            else { start.ArgumentList.Add("--download-model"); start.ArgumentList.Add(Model); }
            start.ArgumentList.Add("--model-root"); start.ArgumentList.Add(Paths.Models);
            downloader = Process.Start(start) ?? throw new InvalidOperationException("下载进程无法启动");
            var stderr = downloader.StandardError.ReadToEndAsync();
            string? line;
            while ((line = await downloader.StandardOutput.ReadLineAsync()) != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var data = doc.RootElement;
                    switch (data.GetProperty("type").GetString())
                    {
                        case "progress":
                            double total = data.GetProperty("total").GetDouble(), current = data.GetProperty("current").GetDouble();
                            DownloadProgress.IsIndeterminate = total <= 0;
                            if (total > 0) DownloadProgress.Value = current / total * 100;
                            downloadStatus.Text = data.GetProperty("unit").GetString() == "B" ? $"下载中 {current / 1048576:F1} / {total / 1048576:F1} MB" : $"下载文件 {current} / {total}";
                            break;
                        case "status": downloadStatus.Text = data.GetProperty("message").GetString(); break;
                        case "error": StatusText.Text = "下载失败：" + data.GetProperty("message").GetString(); break;
                    }
                }
                catch (JsonException) { }
            }
            await downloader.WaitForExitAsync();
            if (downloader.ExitCode == 0) StatusText.Text = "模型已安装，可离线开始字幕。";
            else if (!StatusText.Text.StartsWith("下载失败")) StatusText.Text = "下载已取消或失败，可以重试。" + (await stderr).Trim().Split('\n').LastOrDefault();
        }
        catch (Exception ex) { StatusText.Text = "下载失败：" + ex.Message; }
        finally { downloader?.Dispose(); downloader = null; downloading = false; DownloadProgress.Visibility = Visibility.Collapsed; UpdateControls(); }
    }
    private void ExportSrt_Click(object sender, RoutedEventArgs e) => Export(true);
    private void ExportText_Click(object sender, RoutedEventArgs e) => Export(false);
    private void Export(bool srt)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "VoiceStudio-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), DefaultExt = srt ? ".srt" : ".txt", Filter = srt ? "SubRip 字幕|*.srt" : "纯文本|*.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, srt ? Transcript.Srt(captions, DisplayMode) : string.Join(Environment.NewLine + Environment.NewLine, captions.Select(c => Transcript.CaptionText(c, DisplayMode))), new UTF8Encoding(true)); dirty = false; StatusText.Text = "已导出：" + dialog.FileName; }
        catch (Exception ex) { StatusText.Text = "导出失败：" + ex.Message; }
    }
    private void Delete_Click(object sender, RoutedEventArgs e) { if (CaptionList.SelectedItem is Caption line) { captions.Remove(line); dirty = true; UpdateCount(); UpdateControls(); } }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (quitting) return;
        e.Cancel = true;
        if (busy) { StatusText.Text = "请等待当前操作完成后再退出。"; return; }
        if ((dirty || running || downloading) && MessageBox.Show(this, "退出会停止采集/下载，并丢弃未导出的记录。确认退出？", "退出 Voice Studio", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        quitting = true; timer.Stop();
        try { downloader?.Kill(true); } catch (InvalidOperationException) { }
        await CleanupAsync();
        SaveSettings();
        var handle = new WindowInteropHelper(this).Handle;
        for (int i = 1; i <= 3; i++) UnregisterHotKey(handle, i);
        tray?.Dispose(); overlay?.Close(); Close();
    }
    private void SaveSettings()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(Paths.Settings)!); var source = DeviceBox.SelectedItem as AudioDevice; File.WriteAllText(Paths.Settings, JsonSerializer.Serialize(new { modelIndex = ModelBox.SelectedIndex, languageIndex = LanguageBox.SelectedIndex, translationIndex = TranslationBox.SelectedIndex, translationBackendIndex = TranslationBackendBox.SelectedIndex, displayIndex = DisplayBox.SelectedIndex, computeIndex = ComputeBox.SelectedIndex, font = FontSlider.Value, opacity = OpacitySlider.Value, locked = LockOverlayBox.IsChecked == true, sourceId = source?.Id ?? "", microphone = source?.Microphone ?? false })); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(Paths.Settings)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(Paths.Settings)); var s = doc.RootElement;
            ModelBox.SelectedIndex = Math.Clamp(s.GetProperty("modelIndex").GetInt32(), 0, ModelBox.Items.Count - 1);
            LanguageBox.SelectedIndex = Math.Clamp(s.GetProperty("languageIndex").GetInt32(), 0, LanguageBox.Items.Count - 1);
            if (s.TryGetProperty("translationIndex", out var translation)) TranslationBox.SelectedIndex = Math.Clamp(translation.GetInt32(), 0, TranslationBox.Items.Count - 1);
            if (s.TryGetProperty("translationBackendIndex", out var backend)) TranslationBackendBox.SelectedIndex = Math.Clamp(backend.GetInt32(), 0, TranslationBackendBox.Items.Count - 1);
            if (s.TryGetProperty("displayIndex", out var display)) DisplayBox.SelectedIndex = Math.Clamp(display.GetInt32(), 0, 2);
            if (s.TryGetProperty("computeIndex", out var compute)) ComputeBox.SelectedIndex = Math.Clamp(compute.GetInt32(), 0, 2);
            FontSlider.Value = s.GetProperty("font").GetDouble(); OpacitySlider.Value = s.GetProperty("opacity").GetDouble(); LockOverlayBox.IsChecked = s.GetProperty("locked").GetBoolean();
            if (s.TryGetProperty("microphone", out var mic) && s.TryGetProperty("sourceId", out var sourceId))
                DeviceBox.SelectedItem = DeviceBox.Items.Cast<AudioDevice>().FirstOrDefault(d => d.Id == sourceId.GetString() && d.Microphone == mic.GetBoolean()) ?? DeviceBox.Items[0];
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
    }
}
