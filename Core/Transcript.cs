using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VoiceStudio.Core;
public enum CaptionMode { Original, Translation, Bilingual }
public sealed record Caption(int SegmentId, long StartMs, long EndMs, string Text, string Translation = "", string TranslationError = "")
{
    public string TimeLabel => TimeSpan.FromMilliseconds(StartMs).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    public string TranslationDisplay => Translation.Length > 0 && Translation != Text ? Translation : TranslationError;
}
public static class Transcript
{
    public static string CaptionText(Caption line, CaptionMode mode)
    {
        static string Clean(string text) => text.Replace("\r", "").Replace("\n", " ").Trim();
        string original = Clean(line.Text), translated = Clean(line.Translation);
        if (mode == CaptionMode.Original || translated.Length == 0 || translated == original) return original;
        return mode == CaptionMode.Translation ? translated : original + Environment.NewLine + translated;
    }
    public static string Srt(IEnumerable<Caption> captions, CaptionMode mode = CaptionMode.Original)
    {
        var result = new StringBuilder();
        int index = 0;
        foreach (var line in captions.OrderBy(c => c.StartMs).ThenBy(c => c.SegmentId))
        {
            if (string.IsNullOrWhiteSpace(line.Text)) continue;
            result.AppendLine((++index).ToString(CultureInfo.InvariantCulture));
            result.AppendLine($"{Timestamp(line.StartMs)} --> {Timestamp(Math.Max(line.StartMs + 1, line.EndMs))}");
            result.AppendLine(CaptionText(line, mode));
            result.AppendLine();
        }
        return result.ToString();
    }
    private static string Timestamp(long ms)
    {
        ms = Math.Max(0, ms);
        return $"{ms / 3600000:00}:{ms / 60000 % 60:00}:{ms / 1000 % 60:00},{ms % 1000:000}";
    }
}
