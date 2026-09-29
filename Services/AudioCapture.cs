using System;
using System.Collections.Generic;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceStudio.Services;
public sealed record AudioDevice(string Id, string Name, bool Microphone = false)
{
    public override string ToString() => Name;
}
public sealed class AudioCapture : IDisposable
{
    private MMDevice? device;
    private WasapiCapture? capture;
    private readonly Stopwatch clock = new();
    private long nextSample;
    private bool stopping;
    public event Action<long, byte[]>? Audio;
    public event Action<Exception>? Failed;
    public double Peak { get; private set; }
    public long ElapsedSamples => (long)(clock.Elapsed.TotalSeconds * 16000);
    public string? DeviceId => device?.ID;
    public static List<AudioDevice> Devices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var list = new List<AudioDevice> { new("", "系统声音 · 默认播放设备"), new("", "麦克风 · 默认输入设备", true) };
        foreach (var item in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (item) list.Add(new AudioDevice(item.ID, "系统声音 · " + item.FriendlyName));
        }
        foreach (var item in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (item) list.Add(new AudioDevice(item.ID, "麦克风 · " + item.FriendlyName, true));
        }
        return list;
    }
    public static string DefaultId(bool microphone = false)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var current = enumerator.GetDefaultAudioEndpoint(microphone ? DataFlow.Capture : DataFlow.Render, Role.Multimedia);
        return current.ID;
    }
    public void Start(string id, bool microphone = false)
    {
        using var enumerator = new MMDeviceEnumerator();
        device = string.IsNullOrEmpty(id) ? enumerator.GetDefaultAudioEndpoint(microphone ? DataFlow.Capture : DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(id);
        // WASAPI AutoConvertPcm + SrcDefaultQuality performs actual resampling/downmixing.
        capture = microphone ? new WasapiCapture(device) : new WasapiLoopbackCapture(device);
        capture.WaveFormat = new WaveFormat(16000, 16, 1);
        capture.DataAvailable += (_, e) =>
        {
            if (stopping || e.BytesRecorded == 0) return;
            var pcm = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, pcm, 0, pcm.Length);
            int count = pcm.Length / 2;
            long estimatedStart = Math.Max(0, ElapsedSamples - count);
            // WASAPI sends no callbacks when no render streams exist. Preserve these gaps.
            long start = estimatedStart - nextSample > 4800 ? estimatedStart : nextSample;
            nextSample = start + count;
            double peak = 0;
            for (int i = 0; i < pcm.Length; i += 2) peak = Math.Max(peak, Math.Abs((double)BitConverter.ToInt16(pcm, i)) / 32768);
            Peak = peak;
            Audio?.Invoke(start, pcm);
        };
        capture.RecordingStopped += (_, e) =>
        {
            if (!stopping) Failed?.Invoke(e.Exception ?? new InvalidOperationException("音频设备停止了采集。请刷新设备后重新开始。"));
        };
        clock.Start();
        capture.StartRecording();
    }
    public void Dispose()
    {
        stopping = true;
        capture?.Dispose();
        capture = null;
        device?.Dispose();
        device = null;
        clock.Stop();
        Peak = 0;
    }
}
