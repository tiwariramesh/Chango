using System.Globalization;
using System.Text.RegularExpressions;
using Flipr.Core;

namespace Flipr.Conversion.Media;

/// <summary>
/// The formats Windows can't make by itself (MKV, WebM, MOV, AVI, OGG, Opus, AIFF), and anything from files Windows
/// can't read (FLV, MPEG, OGG, Opus, AIFF …), through <b>FFmpeg</b> when it's installed (winget install Gyan.FFmpeg).
/// FFmpeg isn't bundled: without it these options simply don't appear. Never duplicates what
/// <see cref="MediaFoundationProvider"/> already offers for a file.
/// </summary>
public sealed partial class FfmpegProvider : IOperationProvider
{
    public const string ProviderId = "media.ffmpeg";
    public string Id => ProviderId;

    private static readonly AppFinder Ffmpeg = new(() => ExternalTools.Find("ffmpeg.exe", @"ffmpeg\bin\ffmpeg.exe", @"scoop\shims\ffmpeg.exe",
        WingetPackage(), @"C:\ProgramData\chocolatey\bin\ffmpeg.exe", @"C:\ffmpeg\bin\ffmpeg.exe"));

    /// <summary>The ffmpeg.exe in use, or null when FFmpeg isn't installed.</summary>
    public static string? ExecutablePath => Ffmpeg.Value;
    public static bool Available => ExecutablePath is not null;

    /// <summary>Read by FFmpeg only (Windows has no decoder for them).</summary>
    private static readonly HashSet<string> OnlyFfmpegVideo = new(StringComparer.OrdinalIgnoreCase)
        { ".flv", ".f4v", ".mpg", ".mpeg", ".vob", ".ogv", ".mxf", ".asf", ".divx" };
    private static readonly HashSet<string> OnlyFfmpegAudio = new(StringComparer.OrdinalIgnoreCase)
        { ".ogg", ".oga", ".opus", ".aif", ".aiff", ".aifc", ".amr", ".ac3", ".wv", ".ape", ".mka", ".caf", ".au" };

    private sealed record Target(string Label, string Extension, bool IsVideo, string[] Codec);

    // Encoders chosen for the widest playback support at good quality.
    private static readonly string[] H264 = ["-c:v", "libx264", "-crf", "20", "-preset", "medium", "-pix_fmt", "yuv420p",
        "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"];

    private static readonly Target Mkv = new("MKV", ".mkv", true, ["-map", "0:v?", "-map", "0:a?", "-map", "0:s?", "-c", "copy"]),
        Webm = new("WEBM", ".webm", true, ["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-c:a", "libopus", "-b:a", "128k"]),
        Mov = new("MOV", ".mov", true, H264),
        Avi = new("AVI", ".avi", true, ["-c:v", "mpeg4", "-vtag", "xvid", "-q:v", "3", "-c:a", "libmp3lame", "-q:a", "3"]),
        Mp4 = new("MP4", ".mp4", true, H264),
        Ogg = new("OGG", ".ogg", false, ["-c:a", "libvorbis", "-q:a", "5"]),
        Opus = new("OPUS", ".opus", false, ["-c:a", "libopus", "-b:a", "128k"]),
        Aiff = new("AIFF", ".aiff", false, ["-c:a", "pcm_s16be"]),
        Mp3 = new("MP3", ".mp3", false, ["-c:a", "libmp3lame", "-q:a", "2"]),
        M4a = new("M4A", ".m4a", false, ["-c:a", "aac", "-b:a", "192k"]),
        Wav = new("WAV", ".wav", false, ["-c:a", "pcm_s16le"]),
        Flac = new("FLAC", ".flac", false, ["-c:a", "flac"]);

    /// <summary>Re-encoding fallback for MKV when the streams can't simply be copied into it.</summary>
    private static readonly string[] MkvReencode = ["-map", "0:v?", "-map", "0:a?", "-c:v", "libx264", "-crf", "20", "-preset", "medium", "-c:a", "aac", "-b:a", "192k"];

    private static readonly Target[] Extra = [Mkv, Webm, Mov, Avi, Ogg, Opus, Aiff];   // what only FFmpeg makes
    private static readonly Target[] Standard = [Mp4, Mp3, M4a, Wav, Flac];            // for files only FFmpeg reads
    private static readonly Target[] All = [.. Standard, .. Extra];

    private static readonly OperationGroup More = new("media.more", "More", null); // the wheel marks group slices with "+"

    public static bool IsVideo(string path) => MediaFoundationProvider.IsVideo(path) || OnlyFfmpegVideo.Contains(Path.GetExtension(path));
    public static bool IsAudio(string path) => MediaFoundationProvider.IsAudio(path) || OnlyFfmpegAudio.Contains(Path.GetExtension(path));
    private static bool WindowsReads(string path) => MediaFoundationProvider.IsVideo(path) || MediaFoundationProvider.IsAudio(path);

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (mode != OperationMode.Convert || files.IsEmpty || !files.All(f => IsVideo(f) || IsAudio(f)) || !Available) yield break;
        bool allVideo = files.All(IsVideo);
        bool windowsReadsAll = files.All(WindowsReads);
        var extensions = files.Extensions.ToList();

        // Video can become video or audio; audio only audio. Windows' own formats stay with Media Foundation.
        var targets = (windowsReadsAll ? Extra : All).Where(t => allVideo || !t.IsVideo)
            .Where(t => !(extensions.Count == 1 && (extensions[0] == t.Extension || (t == Aiff && extensions[0] is ".aif" or ".aifc"))));
        int order = 40;
        foreach (var t in targets)
        {
            bool extract = allVideo && !t.IsVideo;
            // Video files already have a full wheel: the extra formats share one "More" segment there.
            var group = allVideo && windowsReadsAll ? More : null;
            yield return new($"convert.ffmpeg{t.Extension}", ProviderId, OperationMode.Convert, t.Label, null,
                extract ? "Extract audio as %1" : "Convert to %1", t.Label, extract ? $"Extracting audio as {t.Label}" : $"Converting to {t.Label}",
                order++, Group: group);
        }
    }

    public async Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct)
    {
        var target = All.Single(t => operation.Id == $"convert.ffmpeg{t.Extension}");
        string ffmpeg = Ffmpeg.Value ?? throw new NotSupportedException("FFmpeg isn't installed.");
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(file)) throw new FileNotFoundException(null, file);
                string output = await Task.Run(() => SafeOutput.WriteToPath(file, context.Output, target.Extension, null, temp =>
                {
                    try { Run(ffmpeg, file, temp, target.Codec, target.IsVideo, context.ReportFileFraction, ct); }
                    catch (IOException) when (target == Mkv && !ct.IsCancellationRequested)
                    {
                        Run(ffmpeg, file, temp, MkvReencode, true, context.ReportFileFraction, ct);
                    }
                }, _ => null, ct, hideWhileWriting: false, tempKeepsExtension: true), ct); // FFmpeg can't replace a hidden file
                progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded, output));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: Friendly(ex))); }
        }
    }

    /// <summary>One FFmpeg run, reporting progress from its "-progress" stream against the duration it prints.</summary>
    private static void Run(string ffmpeg, string input, string output, string[] codec, bool video, Action<double>? fraction, CancellationToken ct)
    {
        TimeSpan? duration = null;
        var errors = new List<string>();
        string[] args = ["-hide_banner", "-nostdin", "-y", "-i", input, .. (video ? [] : new[] { "-vn" }), "-map_metadata", "0", .. codec,
            "-progress", "pipe:1", "-nostats", output];
        try
        {
            ExternalTools.RunAsync(ffmpeg, args, TimeSpan.FromHours(6), ct,
                onOutput: line =>
                {
                    if (duration is { TotalMilliseconds: > 0 } d && line.StartsWith("out_time_us=", StringComparison.Ordinal)
                        && long.TryParse(line.AsSpan(12), out long us))
                        fraction?.Invoke(Math.Clamp(us / 1000.0 / d.TotalMilliseconds, 0, 1));
                },
                onError: line =>
                {
                    if (duration is null && DurationLine().Match(line) is { Success: true } m
                        && TimeSpan.TryParse(m.Groups["t"].Value, CultureInfo.InvariantCulture, out var t)) duration = t;
                    if (ErrorLine().IsMatch(line)) lock (errors) errors.Add(line.Trim());
                }).GetAwaiter().GetResult();
        }
        catch (IOException) when (errors.Count > 0)
        {
            throw new IOException("FFmpeg couldn't convert it: " + errors[^1]);
        }
    }

    private static string? WingetPackage()
    {
        string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        try
        {
            return Directory.Exists(packages)
                ? Directory.EnumerateDirectories(packages, "*FFmpeg*").SelectMany(d => Directory.EnumerateFiles(d, "ffmpeg.exe", SearchOption.AllDirectories)).FirstOrDefault()
                : null;
        }
        catch { return null; }
    }

    private static string Friendly(Exception ex) => ex switch
    {
        FileNotFoundException => "The file no longer exists.",
        UnauthorizedAccessException => "Windows denied access (read-only folder or file in use).",
        TimeoutException => "The conversion took too long and was stopped.",
        _ => ex.Message,
    };

    [GeneratedRegex(@"Duration:\s*(?<t>\d+:\d{2}:\d{2}(\.\d+)?)")]
    private static partial Regex DurationLine();

    [GeneratedRegex(@"(?i)error|invalid|unknown|not supported|could not|unable|no such|does not contain|matches no streams")]
    private static partial Regex ErrorLine();

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        yield return ("Video and audio (FFmpeg)", Available
            ? "MKV, WebM, MOV, AVI, OGG, Opus, AIFF, and FLV, MPEG, OGG, AIFF … to MP4, MP3, M4A, WAV, FLAC · using FFmpeg"
            : "Install FFmpeg (winget install Gyan.FFmpeg) for MKV, WebM, MOV, AVI, OGG, Opus and AIFF");
    }
}
