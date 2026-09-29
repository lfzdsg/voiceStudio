using System.Text;
using NAudio.Wave;
using VoiceStudio.Core;
using VoiceStudio.Services;

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }

var srt = Transcript.Srt(new[] { new Caption(2, 3600001, 3600500, "你好"), new Caption(1, 0, 1000, "Hello\nworld"), new Caption(3, 0, 0, " ") });
Check(srt.StartsWith("1" + Environment.NewLine + "00:00:00,000 --> 00:00:01,000"), "SRT sorted and numbered");
Check(srt.Contains("01:00:00,001 --> 01:00:00,500"), "SRT hour boundary");
Check(srt.Contains("Hello world"), "SRT multiline sanitized");
var bilingual = new Caption(1, 0, 1000, "Hello", "你好");
Check(Transcript.CaptionText(bilingual, CaptionMode.Bilingual) == "Hello" + Environment.NewLine + "你好", "Bilingual export preserves two lines");
Check(Transcript.CaptionText(bilingual, CaptionMode.Translation) == "你好", "Translation-only export");
Check(Transcript.CaptionText(bilingual with { Translation = "", TranslationError = "Failed" }, CaptionMode.Translation) == "Hello", "Failed translation retains original in export");
Check(Transcript.CaptionText(bilingual with { Translation = "Hello" }, CaptionMode.Bilingual) == "Hello", "Same-language captions are not duplicated");
using (var memory = new MemoryStream(PipeProtocol.Json(new { text = "中文" })))
    Check((await PipeProtocol.ReadJsonAsync(memory, default)).GetProperty("text").GetString() == "中文", "Pipe Unicode round trip");
try { await PipeProtocol.ReadJsonAsync(new MemoryStream(new byte[] { 255, 255, 255, 127 }), default); throw new Exception("invalid frame accepted"); }
catch (InvalidDataException) { Console.WriteLine("PASS Invalid pipe frame rejected"); }

if (args.Contains("--devices"))
{
    var devices = AudioCapture.Devices();
    foreach (var d in devices) Console.WriteLine("DEVICE " + d.Name);
    Check(devices.Any(d => !d.Microphone && d.Id.Length > 0), "Windows playback device enumeration");
}

if (args.Length > 1 && args[0] == "--recognize")
{
    var results = new List<Caption>();
    var translations = new Dictionary<int, string>();
    var clock = new System.Diagnostics.Stopwatch();
    long? firstTextMs = null, firstTranslationMs = null;
    bool earlyTranslation = false;
    string target = args.Length > 5 ? args[5] : "off";
    await using var engine = new Engine(args.Length > 3 ? args[3] : null);
    engine.Event += e =>
    {
        var type = e.GetProperty("type").GetString();
        if (type == "ready") Console.WriteLine("READY " + e.GetRawText());
        if ((type is "partial" or "final") && !string.IsNullOrWhiteSpace(e.GetProperty("text").GetString()) && firstTextMs == null)
        {
            firstTextMs = clock.ElapsedMilliseconds;
            Console.WriteLine("FIRST TEXT MS " + firstTextMs);
        }
        if (type is "translation_partial" or "translation")
        {
            if (firstTranslationMs == null)
            {
                firstTranslationMs = clock.ElapsedMilliseconds;
                Console.WriteLine("FIRST TRANSLATION MS " + firstTranslationMs);
            }
            if (type == "translation_partial") earlyTranslation |= !results.Any(c => c.SegmentId == e.GetProperty("segmentId").GetInt32());
        }
        if (e.GetProperty("type").GetString() == "translation")
        {
            translations[e.GetProperty("segmentId").GetInt32()] = e.GetProperty("text").GetString()!;
            Console.WriteLine("TRANSLATION " + e.GetProperty("text").GetString());
        }
        if (e.GetProperty("type").GetString() == "translation_error") Console.Error.WriteLine("TRANSLATION ERROR " + e.GetProperty("message").GetString());
        if (e.GetProperty("type").GetString() == "final")
        {
            string text = e.GetProperty("text").GetString()!;
            Console.WriteLine("FINAL " + text);
            if (text.Length > 0) results.Add(new Caption(e.GetProperty("segmentId").GetInt32(), e.GetProperty("startMs").GetInt64(), e.GetProperty("endMs").GetInt64(), text));
        }
    };
    engine.Failed += message => Console.Error.WriteLine("ENGINE ERROR " + message);
    await engine.StartAsync(args.Length > 4 ? args[4] : "base", args.Length > 2 ? args[2] : "en", target, args.Contains("--gpu") ? "cuda" : "cpu", args.Contains("--qwen") ? "qwen" : "argos");
    clock.Start();
    using var reader = new WaveFileReader(args[1]);
    Check(reader.WaveFormat.SampleRate == 16000 && reader.WaveFormat.Channels == 1 && reader.WaveFormat.BitsPerSample == 16, "fixture PCM16 16k mono");
    var buffer = new byte[3200];
    long offset = 0;
    int count;
    while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
    {
        engine.Audio(offset, buffer[..count]);
        offset += count / 2;
        await Task.Delay(100); // real-time input, exercises partials and queue pressure
    }
    await engine.FinishAsync();
    Check(results.Count > 0, "real ASR produced final captions and flushed tail");
    if (args.Contains("--latency")) Check(earlyTranslation, "preview translation arrived before sentence completion");
    if (target != "off") Check(results.All(c => translations.TryGetValue(c.SegmentId, out var t) && !string.IsNullOrWhiteSpace(t) && !t.Contains('▁')), "every final caption translated before completion");
    Check(results.Zip(results.Skip(1), (a, b) => a.EndMs <= b.StartMs).All(x => x), "caption times are non-overlapping");
    string transcript = string.Join(" ", results.Select(c => c.Text));
    if ((args.Length > 2 ? args[2] : "en") == "en") Check(transcript.Contains("caption", StringComparison.OrdinalIgnoreCase), "recognized expected spoken content");
    File.WriteAllText(Path.Combine(Paths.Root, ".runtime", "recognition-test.srt"), Transcript.Srt(results), Encoding.UTF8);
}
if (args.Length > 1 && args[0] == "--loopback")
{
    long samples = 0;
    double peak = 0;
    Exception? failure = null;
    using var capture = new AudioCapture();
    capture.Audio += (_, bytes) => { Interlocked.Add(ref samples, bytes.Length / 2); peak = Math.Max(peak, capture.Peak); };
    capture.Failed += e => failure = e;
    capture.Start("");
    using var reader = new WaveFileReader(args[1]);
    using var playback = new WasapiOut();
    playback.Init(reader); playback.Play();
    await Task.Delay(reader.TotalTime + TimeSpan.FromSeconds(1));
    capture.Dispose();
    Check(failure == null, "WASAPI capture completed without error");
    Check(samples > 16000 && peak > 0.001, "WASAPI captured real loopback PCM");
    Console.WriteLine($"Captured {samples} samples, peak {peak:F3}");
}
if (args.Contains("--microphone"))
{
    long samples = 0;
    Exception? failure = null;
    using var mic = new AudioCapture();
    mic.Audio += (_, bytes) => Interlocked.Add(ref samples, bytes.Length / 2);
    mic.Failed += e => failure = e;
    mic.Start("", microphone: true);
    await Task.Delay(1500);
    mic.Dispose();
    Check(failure == null && samples > 16000, "microphone produces PCM16 frames (audio not saved)");
}
