using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Chango.Conversion.Media;

/// <summary>
/// What FFmpeg reports about a file's streams ("ffmpeg -i file" with no output): enough to choose encoder settings.
/// Sizes are as displayed, after any rotation the file asks for.
/// </summary>
public sealed partial record MediaProbe(
    TimeSpan Duration,
    long BitRate,
    bool HasVideo,
    string? VideoCodec,
    int Width,
    int Height,
    long VideoBitRate,
    bool TenBit,
    bool Hdr,
    bool HasAudio,
    string? AudioCodec,
    long AudioBitRate)
{
    public static MediaProbe Read(string ffmpeg, string file)
    {
        var start = new ProcessStartInfo(ffmpeg)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-i", file }) start.ArgumentList.Add(a);
        using var p = Process.Start(start) ?? throw new IOException("FFmpeg didn't start.");
        var output = p.StandardOutput.ReadToEndAsync();
        string report = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(30_000)) { try { p.Kill(true); } catch { } }
        output.Wait(1000);
        return Parse(report);
    }

    /// <summary>Parses FFmpeg's input report (exposed for tests).</summary>
    public static MediaProbe Parse(string report)
    {
        TimeSpan duration = TimeSpan.Zero;
        long bitrate = 0;
        if (DurationLine().Match(report) is { Success: true } d)
        {
            TimeSpan.TryParse(d.Groups["t"].Value, CultureInfo.InvariantCulture, out duration);
            if (d.Groups["b"].Success) bitrate = long.Parse(d.Groups["b"].Value, CultureInfo.InvariantCulture) * 1000;
        }

        var video = VideoLine().Match(report);
        int w = 0, h = 0;
        long vbr = 0;
        string? vcodec = video.Success ? video.Groups["vc"].Value : null;
        bool tenBit = false, hdr = false;
        if (video.Success)
        {
            string line = video.Value;
            if (SizeIn().Match(line) is { Success: true } s) (w, h) = (int.Parse(s.Groups["w"].Value), int.Parse(s.Groups["h"].Value));
            if (KbpsIn().Match(line) is { Success: true } k) vbr = long.Parse(k.Groups["k"].Value, CultureInfo.InvariantCulture) * 1000;
            tenBit = line.Contains("p10", StringComparison.Ordinal) || line.Contains("p12", StringComparison.Ordinal);
            hdr = line.Contains("smpte2084", StringComparison.Ordinal) || line.Contains("arib-std-b67", StringComparison.Ordinal);
            if (Rotation().Match(report) is { Success: true } r
                && double.TryParse(r.Groups["deg"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double deg)
                && Math.Abs(Math.Abs(deg) % 180 - 90) < 1)
                (w, h) = (h, w);
        }

        var audio = AudioLine().Match(report);
        string? codec = null;
        long abr = 0;
        if (audio.Success)
        {
            codec = audio.Groups["codec"].Value;
            if (KbpsIn().Match(audio.Value) is { Success: true } k) abr = long.Parse(k.Groups["k"].Value, CultureInfo.InvariantCulture) * 1000;
        }
        return new MediaProbe(duration, bitrate, video.Success, vcodec, w, h, vbr, tenBit, hdr, audio.Success, codec, abr);
    }

    /// <summary>The video's own bitrate: as reported, or the whole file's less the audio, or size ÷ length.</summary>
    public long EstimatedVideoBitRate(long fileBytes)
    {
        if (VideoBitRate > 0) return VideoBitRate;
        long total = BitRate > 0 ? BitRate : Duration.TotalSeconds > 0 ? (long)(fileBytes * 8 / Duration.TotalSeconds) : 0;
        return Math.Max(0, total - AudioBitRate);
    }

    [GeneratedRegex(@"Duration:\s*(?<t>\d+:\d{2}:\d{2}(\.\d+)?)(.*?bitrate:\s*(?<b>\d+)\s*kb/s)?")]
    private static partial Regex DurationLine();

    // Attached cover pictures are "Video" streams too; the real one isn't marked (attached pic).
    [GeneratedRegex(@"Stream #\d+:\d+[^\n]*?: Video: (?![^\n]*\(attached pic\))(?<vc>\w+)[^\n]*")]
    private static partial Regex VideoLine();

    [GeneratedRegex(@"Stream #\d+:\d+[^\n]*?: Audio: (?<codec>\w+)[^\n]*")]
    private static partial Regex AudioLine();

    [GeneratedRegex(@", (?<w>\d{2,5})x(?<h>\d{2,5})[\s,\[]")]
    private static partial Regex SizeIn();

    [GeneratedRegex(@"(?<k>\d+) kb/s")]
    private static partial Regex KbpsIn();

    [GeneratedRegex(@"rotation of (?<deg>-?\d+(\.\d+)?) degrees")]
    private static partial Regex Rotation();
}
