using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Chango.Core;

namespace Chango.Conversion.Subtitles;

/// <summary>One caption: when it shows, and its lines (formatting tags kept as written).</summary>
public sealed record Cue(TimeSpan Start, TimeSpan End, IReadOnlyList<string> Lines);

/// <summary>
/// Subtitles: SRT to WebVTT and back, and either to a plain transcript (TXT). Parsing is forgiving (missing numbers,
/// comma or dot milliseconds, hour-less WebVTT times, a byte-order mark); formatting tags the target can't show are dropped.
/// </summary>
public sealed partial class SubtitleProvider : IOperationProvider
{
    public const string ProviderId = "subtitles";
    public string Id => ProviderId;

    public static bool IsSubtitle(string path) => Path.GetExtension(path).ToLowerInvariant() is ".srt" or ".vtt";

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (mode != OperationMode.Convert || files.IsEmpty || !files.All(IsSubtitle)) yield break;
        var extensions = files.Extensions.ToList();
        if (!extensions.Contains(".srt")) yield return new("convert.subtitles.srt", ProviderId, OperationMode.Convert, "SRT", null, "Convert to %1", "SRT", "Converting to SRT", 1);
        if (!extensions.Contains(".vtt")) yield return new("convert.subtitles.vtt", ProviderId, OperationMode.Convert, "VTT", null, "Convert to %1 (WebVTT)", "VTT", "Converting to VTT", 2);
        yield return new("convert.subtitles.txt", ProviderId, OperationMode.Convert, "TXT", null, "Transcript as %1", "TXT", "Making a transcript", 3);
    }

    public Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct) => Task.Run(() =>
    {
        string ext = "." + operation.Id[(operation.Id.LastIndexOf('.') + 1)..];
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var cues = Parse(File.ReadAllText(file));
                if (cues.Count == 0) throw new InvalidDataException("No captions were found in this file.");
                string text = ext switch { ".srt" => ToSrt(cues), ".vtt" => ToVtt(cues), _ => ToTranscript(cues) };
                string output = SafeOutput.Write(file, context.Output, ext, null, s =>
                {
                    using var w = new StreamWriter(s, new UTF8Encoding(false), leaveOpen: true);
                    w.Write(text);
                }, _ => null, ct);
                progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded, output));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: ex is InvalidDataException ? ex.Message : $"Couldn't read the subtitles: {ex.Message}"));
            }
        }
    }, ct);

    // ------------------------------------------------------------------ reading

    /// <summary>Captions from SRT or WebVTT text (the format is recognised by the timing lines, not the name).</summary>
    public static List<Cue> Parse(string text)
    {
        var cues = new List<Cue>();
        var blocks = Regex.Split(text.TrimStart('﻿').ReplaceLineEndings("\n"), @"\n[ \t]*\n");
        foreach (var block in blocks)
        {
            var lines = block.Split('\n').ToList();
            int timing = lines.FindIndex(l => l.Contains("-->"));
            if (timing < 0) continue; // the WEBVTT header, NOTE, STYLE and REGION blocks
            var m = Timing().Match(lines[timing]);
            if (!m.Success) continue;
            var body = lines.Skip(timing + 1).Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
            cues.Add(new Cue(Time(m.Groups["start"].Value), Time(m.Groups["end"].Value), body));
        }
        return cues;
    }

    private static TimeSpan Time(string t)
    {
        // "01:02:03,450", "01:02:03.450" or (WebVTT) "02:03.450".
        // Whole milliseconds, never floating point: ",450" must stay 450 ms, not 449.
        var parts = t.Replace(',', '.').Split(':');
        var secondParts = parts[^1].Split('.');
        int seconds = int.Parse(secondParts[0], CultureInfo.InvariantCulture);
        int millis = int.Parse(secondParts[1].PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        int minutes = int.Parse(parts[^2], CultureInfo.InvariantCulture);
        int hours = parts.Length > 2 ? int.Parse(parts[^3], CultureInfo.InvariantCulture) : 0;
        return new TimeSpan(0, hours, minutes, seconds, millis);
    }

    // ------------------------------------------------------------------ writing

    public static string ToSrt(IReadOnlyList<Cue> cues)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < cues.Count; i++)
        {
            sb.Append(i + 1).Append("\r\n");
            sb.Append(Stamp(cues[i].Start, ',')).Append(" --> ").Append(Stamp(cues[i].End, ',')).Append("\r\n");
            foreach (var line in cues[i].Lines) sb.Append(KeepTags(VoiceToName(line), "b", "i", "u", "font")).Append("\r\n");
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    public static string ToVtt(IReadOnlyList<Cue> cues)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (var cue in cues)
        {
            sb.Append(Stamp(cue.Start, '.')).Append(" --> ").Append(Stamp(cue.End, '.')).Append('\n');
            foreach (var line in cue.Lines) sb.Append(KeepTags(line, "b", "i", "u", "v", "c", "lang", "ruby", "rt")).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>The words only, one caption per line; captions repeated word for word (common in auto-captions) appear once.</summary>
    public static string ToTranscript(IReadOnlyList<Cue> cues)
    {
        var sb = new StringBuilder();
        string? previous = null;
        foreach (var cue in cues)
        {
            string line = string.Join(" ", cue.Lines.Select(l => KeepTags(VoiceToName(l)).Trim())).Trim();
            line = System.Net.WebUtility.HtmlDecode(line);
            if (line.Length == 0 || line == previous) continue;
            sb.Append(line).Append("\r\n");
            previous = line;
        }
        return sb.ToString();
    }

    private static string Stamp(TimeSpan t, char separator) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{separator}{t.Milliseconds:000}";

    /// <summary>Removes every tag except <paramref name="keep"/> (by name, opening and closing).</summary>
    private static string KeepTags(string line, params string[] keep) =>
        Tag().Replace(line, m => keep.Contains(m.Groups["name"].Value.ToLowerInvariant()) ? m.Value : "");

    /// <summary>WebVTT's speaker tag "&lt;v Anna&gt;Hello" as "Anna: Hello", for formats without speakers.</summary>
    private static string VoiceToName(string line) => Voice().Replace(line, m => m.Groups["name"].Value.Trim() + ": ");

    [GeneratedRegex(@"(?<start>(\d+:)?\d{1,2}:\d{1,2}[.,]\d{1,3})\s*-->\s*(?<end>(\d+:)?\d{1,2}:\d{1,2}[.,]\d{1,3})")]
    private static partial Regex Timing();

    [GeneratedRegex(@"</?(?<name>[a-zA-Z]+)(\.[\w.-]+)?(\s[^>]*)?>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"<v(\.[\w.-]+)?\s+(?<name>[^>]+)>")]
    private static partial Regex Voice();

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        yield return ("Subtitles", "SRT ↔ VTT, and either to a TXT transcript · built in");
    }
}
