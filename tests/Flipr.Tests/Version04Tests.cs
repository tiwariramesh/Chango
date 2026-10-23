using Flipr.Conversion;
using Flipr.Conversion.Imaging;
using Flipr.Core;
using Flipr.Infrastructure;
using Flipr.Tools;
using Flipr.Windows.Shell;

namespace Flipr.Tests;

/// <summary>0.4: Undo, "Put my last choice first", size estimates and clipboard images.</summary>
public class Version04Tests
{
    private static OperationDescriptor Op(string id, OperationMode mode = OperationMode.Convert, OperationGroup? group = null) =>
        new(id, "test", mode, id, null, "%1", id, id, 1, Group: group);

    // ------------------------------------------------------------------ Undo

    [Fact]
    public void Undo_takes_back_only_what_the_job_made()
    {
        using var t = new TempFolder();
        string original = t.Image("photo.jpg", 20, 20);
        string made = t.Image("photo.png", 20, 20);
        string older = t.File("older.png");
        File.WriteAllText(older, "x");
        File.SetCreationTimeUtc(older, DateTime.UtcNow.AddHours(-1));
        var started = DateTime.UtcNow.AddSeconds(-1);

        var outcomes = new[]
        {
            new FileOutcome(original, OutcomeStatus.Succeeded, made),
            new FileOutcome(original, OutcomeStatus.Succeeded, original),   // an "output" that is the original: never
            new FileOutcome(original, OutcomeStatus.Succeeded, older),      // older than the job: never
            new FileOutcome(original, OutcomeStatus.Failed),
        };
        Assert.Equal([made], Undo.Targets(Op("convert.image.png"), [original], outcomes, started));
    }

    [Fact]
    public void Undo_never_touches_moved_files()
    {
        using var t = new TempFolder();
        string moved = t.Image("moved.jpg", 10, 10);
        var outcomes = new[] { new FileOutcome(t.File("was-here.jpg"), OutcomeStatus.Succeeded, moved) };
        Assert.Empty(Undo.Targets(Op(@"action.move-to|C:\Somewhere", OperationMode.Actions), [t.File("was-here.jpg")], outcomes, DateTime.UtcNow.AddMinutes(-1)));
        Assert.False(Undo.IsUndoable(Op("action.move-to.choose", OperationMode.Actions)));
        Assert.True(Undo.IsUndoable(Op("action.copy-to.choose", OperationMode.Actions)));
    }

    [LocalOnlyFact] // a build server may have no Recycle Bin, and Windows would then ask before deleting for good
    public void Undo_sends_files_to_the_Recycle_Bin_not_away_for_good()
    {
        using var t = new TempFolder();
        string file = t.File("Flipr test file (safe to delete).txt");
        File.WriteAllText(file, "Flipr's Undo test put this in the Recycle Bin.");
        Assert.Equal(1, RecycleBin.Send([file]));
        Assert.False(File.Exists(file));
        Assert.Equal(0, RecycleBin.Send([t.File("does-not-exist.txt")]));
    }

    // ------------------------------------------------------------------ Put my last choice first

    [Fact]
    public void The_last_choice_moves_to_the_top_and_groups_stay_together()
    {
        var compress = new OperationGroup("image.compress", "Compress", null);
        var options = new[] { Op("a"), Op("b"), Op("c.1", group: compress), Op("c.2", group: compress), Op("d") };
        Assert.Equal(["d", "a", "b", "c.1", "c.2"], WheelMemory.Apply(options, "d").Select(o => o.Id));
        Assert.Equal(["c.1", "c.2", "a", "b", "d"], WheelMemory.Apply(options, WheelFilter.GroupKey(compress)).Select(o => o.Id));
        Assert.Same(options, WheelMemory.Apply(options, "gone")); // a forgotten option changes nothing
        Assert.Same(options, WheelMemory.Apply(options, null));
    }

    [Fact]
    public void Memory_is_per_mode_and_kind_of_file()
    {
        var png = new FileSet([@"C:\x\a.PNG"]);
        var mixed = new FileSet([@"C:\x\b.jpg", @"C:\x\a.png"]);
        Assert.Equal("Convert|.png", WheelMemory.KeyFor(OperationMode.Convert, png));
        Assert.Equal("Tools|.jpg,.png", WheelMemory.KeyFor(OperationMode.Tools, mixed));
    }

    [Fact]
    public void Wheel_extras_are_off_until_turned_on_and_memory_stays_small()
    {
        var s = new AppSettings();
        Assert.False(s.LastUsedFirst);
        Assert.False(s.ShowSizeEstimates);
        Assert.False(s.KeepOpenOnCentre);
        Assert.True(s.OfferUndo);
        for (int i = 0; i < 500; i++) s.LastUsed[$"Convert|.x{i}"] = "convert.image.png";
        s.Normalize();
        Assert.True(s.LastUsed.Count <= AppSettings.MaxLastUsed);
        var copy = s.Clone();
        copy.LastUsed["new"] = "x";
        Assert.False(s.LastUsed.ContainsKey("new")); // a clone's memory is its own
    }

    // ------------------------------------------------------------------ size estimates

    [Fact]
    public async Task Estimates_are_sensible()
    {
        using var t = new TempFolder();
        string png = t.Image("shot.png", 1600, 1000);
        long size = new FileInfo(png).Length;
        Dictionary<string, long> formats = [];
        await StaWorker.RunAsync(() => formats = SizeEstimate.Formats(png, 92));
        Assert.DoesNotContain("PNG", formats.Keys);         // not its own format
        Assert.True(formats["JPG"] < size, $"JPG {formats["JPG"]} vs PNG {size}");
        Assert.True(formats["BMP"] > formats["JPG"]);

        string jpg = t.Image("photo.jpg", 800, 600);
        await StaWorker.RunAsync(() => formats = SizeEstimate.Formats(jpg, 92));
        Assert.Equal(new FileInfo(jpg).Length, formats["JFIF"]); // a rename: same bytes

        var tools = new ImageToolsProvider().EstimateSizes(png, Ctx.Default()).ToDictionary(e => e.OperationId, e => e.Bytes);
        Assert.Equal(size / 2, tools["tool.image.compress.half"]);
        Assert.Equal(Math.Min(size, 1_000_000), tools["tool.image.compress.1mb"]);
        Assert.True(tools["tool.image.resize.800"] < size);
        Assert.Equal(new FileInfo(png).Length, SizeEstimate.Resized(size, 1.0));
    }

    // ------------------------------------------------------------------ clipboard images

    [Fact]
    public async Task A_copied_image_can_be_saved_as_png_byte_for_byte()
    {
        using var captures = new TempFolder();
        using var target = new TempFolder();
        ClipboardCaptures.Folder = captures.Path;
        try
        {
            string capture = captures.Image("Copied image 2026-10-22 10.00.00.png", 64, 40);
            var provider = new ImageConversionProvider();
            var labels = provider.GetOperations(new FileSet([capture]), OperationMode.Convert).Select(o => o.Label).ToList();
            Assert.Contains("PNG", labels); // offered, unlike for ordinary PNGs

            var op = provider.GetOperations(new FileSet([capture]), OperationMode.Convert).Single(o => o.Label == "PNG");
            var c = new Collector();
            var context = Ctx.Default() with { Output = new OutputOptions(OutputLocation.Folder, target.Path) };
            await provider.ExecuteAsync(op, new FileSet([capture]), context, c, CancellationToken.None);
            var outcome = Assert.Single(c.Outcomes);
            Assert.Equal(target.File("Copied image 2026-10-22 10.00.00.png"), outcome.Output);
            Assert.Equal(SafeOutput.Sha256(capture), SafeOutput.Sha256(outcome.Output!));

            // An ordinary PNG still doesn't offer PNG.
            string plain = target.Image("plain.png", 10, 10);
            Assert.DoesNotContain("PNG", provider.GetOperations(new FileSet([plain]), OperationMode.Convert).Select(o => o.Label));
        }
        finally { ClipboardCaptures.Folder = null; }
    }
}

/// <summary>Skipped on build servers (GitHub Actions sets CI=true).</summary>
public sealed class LocalOnlyFactAttribute : FactAttribute
{
    public LocalOnlyFactAttribute() { if (Environment.GetEnvironmentVariable("CI") == "true") Skip = "Runs on real PCs only"; }
}
