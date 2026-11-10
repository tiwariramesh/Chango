using System.Diagnostics;

namespace Chango.Conversion.Media;

public enum VideoCodec { H264, Hevc }

/// <summary>What a video encode is asked for: a constant look (quality), or a bitrate (bits per second).</summary>
public abstract record VideoTarget
{
    /// <summary>
    /// A constant visual quality, on a 0–51 scale where lower is better (x264's CRF scale; each GPU encoder's own
    /// setting is derived from it).
    /// </summary>
    public sealed record Quality(int Level) : VideoTarget;

    /// <summary>An average bitrate, with peaks allowed up to 1.5×.</summary>
    public sealed record Bitrate(long BitsPerSecond) : VideoTarget;
}

/// <summary>
/// Chooses how FFmpeg encodes video: on the graphics chip when there is one that works (NVIDIA NVENC, Intel Quick
/// Sync, AMD AMF; many times faster, nearly as good per byte at these settings), otherwise on the processor with
/// x264/x265 (slower, the best quality per byte). Which encoders work is found once, by asking each to encode three
/// small frames: an encoder FFmpeg lists may still lack the driver to run.
/// </summary>
public static class VideoEncoder
{
    private static readonly object Gate = new();
    private static string? _probedWith;
    private static Dictionary<VideoCodec, string?> _hardware = [];

    /// <summary>The hardware encoder for <paramref name="codec"/> on this PC (e.g. "hevc_qsv"), or null for the processor.</summary>
    public static string? Hardware(string ffmpeg, VideoCodec codec)
    {
        lock (Gate)
        {
            if (_probedWith != ffmpeg)
            {
                _hardware = new()
                {
                    [VideoCodec.H264] = FirstWorking(ffmpeg, "h264_nvenc", "h264_qsv", "h264_amf"),
                    [VideoCodec.Hevc] = FirstWorking(ffmpeg, "hevc_nvenc", "hevc_qsv", "hevc_amf"),
                };
                _probedWith = ffmpeg;
            }
            return _hardware[codec];
        }
    }

    /// <summary>For tests and diagnostics: forget what was found, so the next encode probes again.</summary>
    public static void Reset()
    {
        lock (Gate) _probedWith = null;
    }

    private static string? FirstWorking(string ffmpeg, params string[] encoders) => encoders.FirstOrDefault(e => Works(ffmpeg, e));

    private static bool Works(string ffmpeg, string encoder)
    {
        try
        {
            var start = new ProcessStartInfo(ffmpeg)
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
            };
            foreach (var a in new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=gray:s=320x240:d=0.2",
                         "-frames:v", "3", "-c:v", encoder, "-f", "null", "-" })
                start.ArgumentList.Add(a);
            using var p = Process.Start(start);
            if (p is null) return false;
            var drain = p.StandardError.ReadToEndAsync();
            p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(15_000)) { try { p.Kill(true); } catch { } return false; }
            drain.Wait(1000);
            return p.ExitCode == 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// FFmpeg arguments for the video stream: encoder, rate control and pixel format. <paramref name="tenBit"/> keeps
    /// 10-bit (HDR) video in 10 bits for HEVC; H.264 is always 8-bit 4:2:0, the form every player accepts.
    /// </summary>
    public static string[] Arguments(string? hardware, VideoCodec codec, VideoTarget target, bool tenBit)
    {
        bool hevc = codec == VideoCodec.Hevc;
        bool deep = hevc && tenBit;
        var args = new List<string>();
        switch (hardware?.Split('_')[1])
        {
            case "nvenc":
                args.AddRange(["-c:v", hardware!, "-preset", "p5", "-tune", "hq", "-spatial-aq", "1", "-temporal-aq", "1", "-rc-lookahead", "20"]);
                args.AddRange(target switch
                {
                    VideoTarget.Quality q => ["-rc", "vbr", "-cq", (q.Level + 1).ToString(), "-b:v", "0"],
                    VideoTarget.Bitrate b => ["-rc", "vbr", .. Rate(b)],
                    _ => [],
                });
                args.AddRange(["-pix_fmt", deep ? "p010le" : "yuv420p"]);
                break;
            case "qsv":
                args.AddRange(["-c:v", hardware!, "-preset", "slower"]);
                args.AddRange(target switch
                {
                    // ICQ: Quick Sync's constant quality. Its scale runs a little higher than CRF for the same look.
                    VideoTarget.Quality q => ["-global_quality", (q.Level + 1).ToString()],
                    VideoTarget.Bitrate b => Rate(b),
                    _ => [],
                });
                args.AddRange(["-pix_fmt", deep ? "p010le" : "nv12"]);
                break;
            case "amf":
                args.AddRange(["-c:v", hardware!, "-quality", "quality"]);
                args.AddRange(target switch
                {
                    VideoTarget.Quality q => ["-rc", "cqp", "-qp_i", (q.Level - 1).ToString(), "-qp_p", q.Level.ToString(), "-qp_b", (q.Level + 2).ToString()],
                    VideoTarget.Bitrate b => ["-rc", "vbr_peak", .. Rate(b)],
                    _ => [],
                });
                args.AddRange(["-pix_fmt", deep ? "p010le" : "nv12"]);
                break;
            default:
                args.AddRange(hevc ? ["-c:v", "libx265", "-preset", "fast", "-x265-params", "log-level=error"] : ["-c:v", "libx264", "-preset", "faster"]);
                args.AddRange(target switch
                {
                    VideoTarget.Quality q => ["-crf", q.Level.ToString()],
                    VideoTarget.Bitrate b => Rate(b),
                    _ => [],
                });
                args.AddRange(["-pix_fmt", deep ? "yuv420p10le" : "yuv420p"]);
                break;
        }
        // "hvc1" is how Apple's players (and Windows') recognise HEVC in MP4.
        if (hevc) args.AddRange(["-tag:v", "hvc1"]);
        return [.. args];
    }

    private static string[] Rate(VideoTarget.Bitrate b) =>
        ["-b:v", b.BitsPerSecond.ToString(), "-maxrate", ((long)(b.BitsPerSecond * 1.5)).ToString(), "-bufsize", (b.BitsPerSecond * 2).ToString()];
}
