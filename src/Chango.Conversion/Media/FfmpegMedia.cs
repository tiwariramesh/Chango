using Chango.Core;

namespace Chango.Conversion.Media;

/// <summary>
/// The video and audio work <see cref="MediaFoundationProvider"/> offers, done by FFmpeg whenever it's here (it ships
/// with Chango). Compared with Windows' own transcoder:
/// <list type="bullet">
/// <item><b>Faster:</b> decoding and encoding on the graphics chip when it can (NVENC, Quick Sync, AMF), x264/x265
/// on the processor otherwise; streams that are already right are copied, not re-encoded (Remove audio, MP4 from
/// H.264/HEVC, audio already in AAC).</item>
/// <item><b>Better per byte:</b> a constant visual quality instead of a fixed bitrate table, and an HEVC option.</item>
/// <item><b>Keeps the metadata:</b> recording date, GPS location, camera, title, artist, album and cover art.</item>
/// </list>
/// Windows' transcoder remains for WMV and WMA, Trim and Normalize, and for any PC without FFmpeg.
/// </summary>
internal static class FfmpegMedia
{
    /// <summary>Box presets (long × short side) and their keys; "half" and "hevc" keep the resolution.</summary>
    private static readonly Dictionary<string, (int Long, int Short)> Boxes = new()
    {
        ["720"] = (1280, 720), ["1080"] = (1920, 1080), ["480"] = (854, 480),
    };

    /// <summary>Compression: x264's CRF 23 look (H.264), 26 for HEVC: visually close to the source at a fraction of the size.</summary>
    public const int H264Quality = 23, HevcQuality = 26;

    public static bool Handles(string operationId) =>
        operationId.StartsWith("tool.video.compress.", StringComparison.Ordinal) || operationId == "tool.video.mute"
        || operationId is "convert.media.mp4" or "convert.media.mp3" or "convert.media.m4a" or "convert.media.wav" or "convert.media.flac"
        || operationId.StartsWith("tool.audio.compress.", StringComparison.Ordinal) || operationId is "tool.audio.mono" or "tool.audio.stereo";

    public static FileOutcome Run(string ffmpeg, string operationId, string file, OperationContext context, CancellationToken ct)
    {
        try
        {
            if (operationId.StartsWith("tool.video.compress.", StringComparison.Ordinal))
                return CompressVideo(ffmpeg, operationId["tool.video.compress.".Length..], file, context, ct);
            if (operationId == "tool.video.mute") return Mute(ffmpeg, file, context, ct);
            if (operationId == "convert.media.mp4") return ToMp4(ffmpeg, file, context, ct);
            return Audio(ffmpeg, operationId, file, context, ct);
        }
        catch (AlreadySmallException)
        {
            return new FileOutcome(file, OutcomeStatus.Skipped, Message: "This video is already as small as it can get");
        }
    }

    /// <summary>Thrown from inside the write when the result came out no smaller: SafeOutput then discards it.</summary>
    private sealed class AlreadySmallException : Exception;

    // ------------------------------------------------------------------ video

    private static FileOutcome CompressVideo(string ffmpeg, string key, string file, OperationContext context, CancellationToken ct)
    {
        var probe = MediaProbe.Read(ffmpeg, file);
        long size = new FileInfo(file).Length;
        var codec = key == "hevc" ? VideoCodec.Hevc : VideoCodec.H264;
        VideoTarget target = key == "half"
            ? new VideoTarget.Bitrate(Math.Max(300_000, probe.EstimatedVideoBitRate(size) / 2))
            : new VideoTarget.Quality(codec == VideoCodec.Hevc ? HevcQuality : H264Quality);

        string output = SafeOutput.WriteToPath(file, context.Output, ".mp4", true, temp =>
        {
            Encode(ffmpeg, file, temp, codec, target, probe, Boxes.TryGetValue(key, out var box) ? box : null, keepAudio: true, context, ct);
            if (new FileInfo(temp).Length >= size) throw new AlreadySmallException();
        }, temp => MediaFoundationProvider.ValidateContainer(temp, ".mp4"), ct, hideWhileWriting: false, tempKeepsExtension: true);
        return new FileOutcome(file, OutcomeStatus.Succeeded, output);
    }

    /// <summary>
    /// Re-encodes the first video stream (and, unless <paramref name="keepAudio"/> is false, the audio) into an MP4,
    /// on the graphics chip when it can and on the processor if the chip refuses.
    /// </summary>
    private static void Encode(string ffmpeg, string file, string temp, VideoCodec codec, VideoTarget target, MediaProbe probe,
        (int Long, int Short)? box, bool keepAudio, OperationContext context, CancellationToken ct)
    {
        string[] Args(string? gpu) =>
        [
            "-map", "0:v:0", .. (keepAudio ? new[] { "-map", "0:a?" } : ["-an"]),
            .. VideoEncoder.Arguments(gpu, codec, target, probe.TenBit),
            "-vf", Filters(codec, probe, box),
            .. (keepAudio ? AudioForMp4(probe) : []),
            "-movflags", "+faststart+use_metadata_tags",
        ];

        string? hardware = VideoEncoder.Hardware(ffmpeg, codec);
        try
        {
            FfmpegProvider.Run(ffmpeg, file, temp, Args(hardware), true, context.ReportFileFraction, ct, ["-hwaccel", "auto"]);
        }
        catch (IOException) when (hardware is not null && !ct.IsCancellationRequested)
        {
            FfmpegProvider.Run(ffmpeg, file, temp, Args(null), true, context.ReportFileFraction, ct);
        }
    }

    /// <summary>
    /// Scaling into the box (never up, even sizes for 4:2:0, the right way round for portrait video; FFmpeg has
    /// already turned the picture upright), and HDR mapped to normal range when the result is 8-bit H.264, so it
    /// doesn't look washed out.
    /// </summary>
    private static string Filters(VideoCodec codec, MediaProbe probe, (int Long, int Short)? box)
    {
        var chain = new List<string>();
        if (probe.Hdr && codec == VideoCodec.H264)
            chain.Add("zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv");
        chain.Add(box is { } b
            ? $"scale=w='min(iw,if(gt(iw,ih),{b.Long},{b.Short}))':h='min(ih,if(gt(iw,ih),{b.Short},{b.Long}))':force_original_aspect_ratio=decrease:force_divisible_by=2:flags=lanczos"
            : "scale=trunc(iw/2)*2:trunc(ih/2)*2");
        return string.Join(',', chain);
    }

    /// <summary>AAC at up to 192 kbps is copied as it is (no loss, no time); anything else becomes AAC 128 kbps.</summary>
    private static string[] AudioForMp4(MediaProbe probe) =>
        probe.AudioCodec == "aac" && probe.AudioBitRate is > 0 and <= 192_000 ? ["-c:a", "copy"] : ["-c:a", "aac", "-b:a", "128k"];

    /// <summary>
    /// Remove audio: the video (and subtitles) copied as they are, in the same container: instant and lossless.
    /// Containers that won't take the copy get an H.264 MP4 instead.
    /// </summary>
    private static FileOutcome Mute(string ffmpeg, string file, OperationContext context, CancellationToken ct)
    {
        string ext = Path.GetExtension(file).ToLowerInvariant();
        try
        {
            string copied = SafeOutput.WriteToPath(file, context.Output, ext, true,
                temp => FfmpegProvider.Run(ffmpeg, file, temp, ["-map", "0", "-map", "-0:a", "-dn", "-c", "copy", .. Mp4Flags(ext)], true, context.ReportFileFraction, ct),
                temp => MediaFoundationProvider.ValidateContainer(temp, ext), ct, hideWhileWriting: false, tempKeepsExtension: true);
            return new FileOutcome(file, OutcomeStatus.Succeeded, copied);
        }
        catch (IOException) when (!ct.IsCancellationRequested) { /* re-encode below */ }
        var probe = MediaProbe.Read(ffmpeg, file);
        string output = SafeOutput.WriteToPath(file, context.Output, ".mp4", true,
            temp => Encode(ffmpeg, file, temp, VideoCodec.H264, new VideoTarget.Quality(FfmpegProvider.ConvertQuality), probe, null, keepAudio: false, context, ct),
            temp => MediaFoundationProvider.ValidateContainer(temp, ".mp4"), ct, hideWhileWriting: false, tempKeepsExtension: true);
        return new FileOutcome(file, OutcomeStatus.Succeeded, output);
    }

    /// <summary>
    /// To MP4: H.264 or HEVC video with AAC, MP3 or no audio is re-packaged without re-encoding (instant, nothing
    /// lost); anything else is encoded at conversion quality.
    /// </summary>
    private static FileOutcome ToMp4(string ffmpeg, string file, OperationContext context, CancellationToken ct)
    {
        var probe = MediaProbe.Read(ffmpeg, file);
        bool copyable = probe.VideoCodec is "h264" or "hevc" && (!probe.HasAudio || probe.AudioCodec is "aac" or "mp3");
        if (copyable)
        {
            try
            {
                string[] tag = probe.VideoCodec == "hevc" ? ["-tag:v", "hvc1"] : [];
                string remuxed = SafeOutput.WriteToPath(file, context.Output, ".mp4", false,
                    temp => FfmpegProvider.Run(ffmpeg, file, temp, ["-map", "0:v:0", "-map", "0:a?", "-c", "copy", .. tag, .. Mp4Flags(".mp4")], true, context.ReportFileFraction, ct),
                    temp => MediaFoundationProvider.ValidateContainer(temp, ".mp4"), ct, hideWhileWriting: false, tempKeepsExtension: true);
                return new FileOutcome(file, OutcomeStatus.Succeeded, remuxed);
            }
            catch (IOException) when (!ct.IsCancellationRequested) { /* encode below */ }
        }
        string output = SafeOutput.WriteToPath(file, context.Output, ".mp4", false,
            temp => Encode(ffmpeg, file, temp, VideoCodec.H264, new VideoTarget.Quality(FfmpegProvider.ConvertQuality), probe, null, keepAudio: true, context, ct),
            temp => MediaFoundationProvider.ValidateContainer(temp, ".mp4"), ct, hideWhileWriting: false, tempKeepsExtension: true);
        return new FileOutcome(file, OutcomeStatus.Succeeded, output);
    }

    private static string[] Mp4Flags(string extension) =>
        extension is ".mp4" or ".m4v" or ".mov" or ".m4a" ? ["-movflags", "+faststart+use_metadata_tags"] : [];

    // ------------------------------------------------------------------ audio

    /// <summary>
    /// Audio conversions and tools, keeping tags (title, artist, album …) and cover art. AAC audio going into M4A is
    /// copied, not re-encoded.
    /// </summary>
    private static FileOutcome Audio(string ffmpeg, string operationId, string file, OperationContext context, CancellationToken ct)
    {
        bool fromVideo = FfmpegProvider.IsVideo(file);
        string extension;
        bool numbered;
        int? kbps = null, channels = null;
        switch (operationId)
        {
            case var id when id.StartsWith("convert.media.", StringComparison.Ordinal):
                extension = "." + id["convert.media.".Length..];
                numbered = false;
                break;
            case var id when id.StartsWith("tool.audio.compress.", StringComparison.Ordinal):
                kbps = int.Parse(id["tool.audio.compress.".Length..]);
                extension = SameKind(file, lossyOnly: true);
                numbered = true;
                break;
            default:
                channels = operationId == "tool.audio.mono" ? 1 : 2;
                extension = SameKind(file, lossyOnly: false);
                numbered = true;
                break;
        }
        if (!numbered && extension == Path.GetExtension(file).ToLowerInvariant())
            return new FileOutcome(file, OutcomeStatus.Skipped, Message: $"Already {extension.TrimStart('.').ToUpperInvariant()}");

        var probe = MediaProbe.Read(ffmpeg, file);
        if (!probe.HasAudio) return new FileOutcome(file, OutcomeStatus.Failed, Message: "This file has no sound to extract.");
        bool copy = kbps is null && channels is null && extension == ".m4a" && probe.AudioCodec == "aac";
        string[] codec = copy ? ["-c:a", "copy"] : extension switch
        {
            ".mp3" => ["-c:a", "libmp3lame", .. (kbps is { } k ? new[] { "-b:a", $"{k}k" } : ["-q:a", "2"]), "-id3v2_version", "3"],
            ".m4a" => ["-c:a", "aac", "-b:a", $"{kbps ?? 192}k"],
            ".wav" => ["-c:a", "pcm_s16le"],
            ".flac" => ["-c:a", "flac"],
            _ => throw new NotSupportedException(extension),
        };
        // Cover art travels with the tags (it's a picture "stream" in the file), except from a video or into WAV.
        bool cover = !fromVideo && extension is ".mp3" or ".m4a" or ".flac";
        string[] args =
        [
            "-map", "0:a:0", .. (cover ? new[] { "-map", "0:v?", "-c:v", "copy", "-disposition:v", "attached_pic" } : ["-vn"]),
            .. codec, .. (channels is { } n ? new[] { "-ac", n.ToString() } : []), .. Mp4Flags(extension),
        ];
        string output = SafeOutput.WriteToPath(file, context.Output, extension, numbered,
            temp => FfmpegProvider.Run(ffmpeg, file, temp, args, true, context.ReportFileFraction, ct),
            temp => MediaFoundationProvider.ValidateContainer(temp, extension), ct, hideWhileWriting: false, tempKeepsExtension: true);
        return new FileOutcome(file, OutcomeStatus.Succeeded, output);
    }

    /// <summary>The source's own audio format where it makes sense, otherwise M4A (as Windows' path does).</summary>
    private static string SameKind(string file, bool lossyOnly) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".mp3" => ".mp3",
        ".wav" when !lossyOnly => ".wav",
        ".flac" when !lossyOnly => ".flac",
        _ => ".m4a",
    };
}
