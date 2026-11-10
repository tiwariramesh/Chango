using System.Diagnostics;
using Chango.Conversion;
using Chango.Conversion.Media;
using Chango.Core;

namespace Chango.Tests;

/// <summary>Video and audio through FFmpeg: smaller results that keep the recording date, location and tags.</summary>
public class FfmpegMediaTests
{
    private const string Recorded = "2019-05-04T10:30:00";
    private const string Location = "+27.7000+085.3000/";

    private static string Ffmpeg => FfmpegProvider.ExecutablePath!;

    private static void Make(params string[] args)
    {
        var start = new ProcessStartInfo(Ffmpeg) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-y", "-loglevel", "error" }.Concat(args)) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, err);
    }

    /// <summary>A 4-second 1280×720 phone-like clip with a recording date, a GPS location and a title.</summary>
    private static string Clip(TempFolder t, string name = "clip.mp4")
    {
        string path = t.File(name);
        Make("-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=4", "-f", "lavfi", "-i", "sine=f=440:d=4",
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", "12", "-c:a", "aac", "-b:a", "256k", "-pix_fmt", "yuv420p",
            "-metadata", $"creation_time={Recorded}Z", "-metadata", "title=Trip", "-metadata", $"com.apple.quicktime.location.ISO6709={Location}",
            "-movflags", "+use_metadata_tags", path);
        return path;
    }

    private static string Report(string file)
    {
        var start = new ProcessStartInfo(Ffmpeg) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true };
        foreach (var a in new[] { "-hide_banner", "-i", file }) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        string report = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return report;
    }

    private static void AssertKeepsMetadata(string file)
    {
        string report = Report(file);
        Assert.Contains(Recorded, report);
        Assert.Contains(Location, report);
        Assert.Contains("Trip", report);
    }

    private static FileOutcome Run(string id, string file)
    {
        var provider = new MediaFoundationProvider();
        var files = new FileSet([file]);
        var op = provider.GetOperations(files, OperationMode.Tools).Concat(provider.GetOperations(files, OperationMode.Convert)).Single(d => d.Id == id);
        var c = new Collector();
        provider.ExecuteAsync(op, files, Ctx.Default(), c, CancellationToken.None).GetAwaiter().GetResult();
        return Assert.Single(c.Outcomes);
    }

    [FfmpegFact]
    public void The_probe_reads_size_codec_and_audio()
    {
        using var t = new TempFolder();
        var probe = MediaProbe.Read(Ffmpeg, Clip(t));
        Assert.Equal((1280, 720), (probe.Width, probe.Height));
        Assert.Equal("h264", probe.VideoCodec);
        Assert.Equal("aac", probe.AudioCodec);
        Assert.InRange(probe.Duration.TotalSeconds, 3.9, 4.1);
        Assert.False(probe.TenBit);
    }

    [Fact]
    public void The_probe_turns_rotated_video_upright_and_spots_hdr()
    {
        var probe = MediaProbe.Parse("""
              Duration: 00:00:10.50, start: 0.000000, bitrate: 21000 kb/s
              Stream #0:0[0x1](und): Video: hevc (Main 10) (hvc1 / 0x31637668), yuv420p10le(tv, bt2020nc/bt2020/arib-std-b67), 1920x1080, 20000 kb/s, 30 fps (default)
                  Side data:
                    displaymatrix: rotation of -90.00 degrees
              Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 44100 Hz, stereo, fltp, 160 kb/s (default)
            """);
        Assert.Equal((1080, 1920), (probe.Width, probe.Height));
        Assert.True(probe.TenBit);
        Assert.True(probe.Hdr);
        Assert.Equal(20_000_000, probe.VideoBitRate);
        Assert.Equal(160_000, probe.AudioBitRate);
    }

    [FfmpegFact]
    public void Compressing_shrinks_the_video_and_keeps_date_location_and_title()
    {
        using var t = new TempFolder();
        string src = Clip(t);
        var o = Run("tool.video.compress.720", src);
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        Assert.Equal(t.File("clip_1.mp4"), o.Output);
        Assert.True(new FileInfo(o.Output!).Length < new FileInfo(src).Length);
        AssertKeepsMetadata(o.Output!);
    }

    [FfmpegFact]
    public void Smallest_uses_hevc_and_keeps_the_metadata()
    {
        using var t = new TempFolder();
        string src = Clip(t);
        var o = Run("tool.video.compress.hevc", src);
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        Assert.Equal("hevc", MediaProbe.Read(Ffmpeg, o.Output!).VideoCodec);
        AssertKeepsMetadata(o.Output!);
    }

    [FfmpegFact]
    public void Half_size_targets_half_the_bitrate()
    {
        using var t = new TempFolder();
        string src = Clip(t);
        var o = Run("tool.video.compress.half", src);
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        Assert.InRange(new FileInfo(o.Output!).Length, new FileInfo(src).Length / 4, new FileInfo(src).Length * 7 / 10);
    }

    [FfmpegFact]
    public void Remove_audio_copies_the_video_untouched()
    {
        using var t = new TempFolder();
        string src = Clip(t);
        var o = Run("tool.video.mute", src);
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        var probe = MediaProbe.Read(Ffmpeg, o.Output!);
        Assert.False(probe.HasAudio);
        Assert.Equal("h264", probe.VideoCodec);
        AssertKeepsMetadata(o.Output!);
    }

    [FfmpegFact]
    public void Mp4_from_mov_is_repackaged_not_reencoded()
    {
        using var t = new TempFolder();
        string mp4 = Clip(t);
        string mov = t.File("clip.mov");
        Make("-i", mp4, "-c", "copy", "-map_metadata", "0", "-movflags", "+use_metadata_tags", mov);
        var o = Run("convert.media.mp4", mov);
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        // A copy keeps the stream byte for byte, so the size barely moves.
        Assert.InRange(new FileInfo(o.Output!).Length, new FileInfo(mov).Length * 9 / 10, new FileInfo(mov).Length * 11 / 10);
        AssertKeepsMetadata(o.Output!);
    }

    [FfmpegFact]
    public void Audio_tools_keep_title_and_artist()
    {
        using var t = new TempFolder();
        string mp3 = t.File("song.mp3");
        Make("-f", "lavfi", "-i", "sine=f=330:d=3", "-c:a", "libmp3lame", "-b:a", "256k", "-metadata", "title=Song", "-metadata", "artist=Ramesh",
            "-id3v2_version", "3", mp3);
        var o = Run("tool.audio.compress.64", mp3);
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        Assert.Equal(t.File("song_1.mp3"), o.Output);
        string report = Report(o.Output!);
        Assert.Contains("Song", report);
        Assert.Contains("Ramesh", report);
        var m4a = Run("convert.media.m4a", mp3);
        Assert.Contains("Ramesh", Report(m4a.Output!));
    }

    [FfmpegFact]
    public void The_encoder_choice_works_on_this_pc()
    {
        // Whatever was found (a graphics chip, or none), encoding with it must succeed.
        VideoEncoder.Reset();
        using var t = new TempFolder();
        string src = Clip(t);
        foreach (var codec in new[] { VideoCodec.H264, VideoCodec.Hevc })
        {
            string? gpu = VideoEncoder.Hardware(Ffmpeg, codec);
            string output = t.File($"out-{codec}.mp4");
            FfmpegProvider.Run(Ffmpeg, src, output, [.. VideoEncoder.Arguments(gpu, codec, new VideoTarget.Quality(26), false), "-an"], true, null, CancellationToken.None);
            Assert.True(new FileInfo(output).Length > 1000, $"{codec} with {gpu ?? "the processor"}");
        }
    }
}
