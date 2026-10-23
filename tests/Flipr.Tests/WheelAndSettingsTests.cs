using System.Windows;
using Flipr.Infrastructure;
using Flipr.UI.Wheel;
using Flipr.Windows.DragDrop;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Flipr.Tests;

public class WheelGeometryTests
{
    // Points 100 units from the centre, inside the ring (66–140).
    private static int At(double angleDeg, int count, double r = 100) =>
        WheelGeometry.HitTest(r * Math.Cos(angleDeg * Math.PI / 180), r * Math.Sin(angleDeg * Math.PI / 180), count);

    [Theory]
    [InlineData(-90, 0)]   // 12 o'clock → first
    [InlineData(0, 1)]     // 3 o'clock
    [InlineData(90, 2)]    // 6 o'clock
    [InlineData(180, 3)]   // 9 o'clock
    [InlineData(-40, 1)]   // just past the 12/3 boundary (-45°)
    [InlineData(-50, 0)]   // just before it
    [InlineData(-140, 3)]  // just past the 9/12 boundary (-135°)
    public void Four_segments_map_clockwise_from_the_top(double angle, int expected) => Assert.Equal(expected, At(angle, 4));

    [Fact]
    public void Five_segments_cover_the_full_circle_once()
    {
        var hits = Enumerable.Range(0, 360).Select(a => At(a - 90, 5)).ToList();
        Assert.Equal([0, 1, 2, 3, 4], hits.Distinct().Order());
        Assert.Equal(0, hits[0]);
    }

    [Fact]
    public void Centre_is_the_hub_and_beyond_the_disc_is_outside()
    {
        Assert.Equal(WheelGeometry.HitHub, WheelGeometry.HitTest(0, 0, 4));
        Assert.Equal(WheelGeometry.HitHub, WheelGeometry.HitTest(40, 30, 4));
        Assert.Equal(WheelGeometry.HitOutside, WheelGeometry.HitTest(160, 0, 4));
    }

    [Fact]
    public void Rim_beyond_the_ring_still_counts_as_the_segment()
    {
        Assert.Equal(1, At(0, 4, r: 146));
    }

    [Fact]
    public void Empty_wheel_never_returns_a_segment() => Assert.Equal(WheelGeometry.HitHub, At(0, 0));

    [Fact]
    public void Wide_label_at_eight_o_clock_does_not_fit_but_a_wrapped_one_does()
    {
        // Label centre of segment 3 of 3 (150°): a one-line "Show in folder" (~76 wide) reaches into the hub.
        double a = 150 * Math.PI / 180, cx = WheelGeometry.LabelR * Math.Cos(a), cy = WheelGeometry.LabelR * Math.Sin(a);
        Assert.False(WheelGeometry.LabelFits(cx, cy, 76, 15));
        Assert.True(WheelGeometry.LabelFits(cx, cy, 42, 30));
    }

    [Fact]
    public void Label_must_stay_inside_the_disc() => Assert.False(WheelGeometry.LabelFits(0, -WheelGeometry.LabelR, 40, 80));
}

public class SettingsStoreTests
{
    [Fact]
    public void Round_trips_all_values()
    {
        using var t = new TempFolder();
        var store = new SettingsStore(t.File("settings.json"));
        store.Save(new AppSettings { Theme = AppTheme.Dark, JpegQuality = 75, TriggerKey = TriggerKey.Ctrl, WheelSize = WheelSize.Large });
        var loaded = store.Load();
        Assert.Equal((AppTheme.Dark, 75, TriggerKey.Ctrl, WheelSize.Large), (loaded.Theme, loaded.JpegQuality, loaded.TriggerKey, loaded.WheelSize));
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        using var t = new TempFolder();
        var s = new SettingsStore(t.File("none.json")).Load();
        Assert.Equal(AppTheme.System, s.Theme);
        Assert.Equal(TriggerKey.Shift, s.TriggerKey);
    }

    [Fact]
    public void Corrupt_file_is_backed_up_and_defaults_are_used()
    {
        using var t = new TempFolder();
        File.WriteAllText(t.File("settings.json"), "{ this is not json");
        var s = new SettingsStore(t.File("settings.json")).Load();
        Assert.Equal(AppTheme.System, s.Theme);
        Assert.Single(Directory.GetFiles(t.Path, "settings.json.corrupt-*"));
    }

    [Fact]
    public void Hand_edited_out_of_range_values_are_normalised()
    {
        using var t = new TempFolder();
        File.WriteAllText(t.File("settings.json"), """{ "jpegQuality": 7, "outputLocation": "folder" }""");
        var s = new SettingsStore(t.File("settings.json")).Load();
        Assert.Equal(50, s.JpegQuality);
        Assert.Equal(Core.OutputLocation.BesideOriginal, s.OutputLocation); // folder chosen but no folder set
    }
}

public class DataObjectTests
{
    [Fact]
    public async Task Reads_unicode_paths_from_CF_HDROP_through_the_COM_interface()
    {
        string[] files = [@"C:\x\a.jpg", @"C:\x\ü nicode 名前.png"];
        string[]? read = null;
        await Conversion.StaWorker.RunAsync(() =>
        {
            var com = (ComIDataObject)new DataObject(DataFormats.FileDrop, files);
            read = DataObjectReader.ReadFileDrop(com);
        });
        Assert.Equal(files, read);
    }
}

public class AccentTests
{
    private static System.Windows.Media.Color C(string hex) => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
    private static readonly System.Windows.Media.Color DarkSegment = C("#2F2F38"), LightSegment = C("#FFFFFF");

    [Fact]
    public void Dark_mode_uses_the_lighter_shade_of_a_dark_accent()
    {
        // A deep blue accent: the base colour is too dark for the dark wheel, Windows' Light2 shade isn't.
        var shades = new[] { C("#99C9FF"), C("#4A8FE0"), C("#003E92"), C("#CDE6FF") }; // Light2, Light1, Accent, Light3
        Assert.Equal(C("#99C9FF"), Flipr.UI.ThemeManager.PickAccent(shades, DarkSegment));
    }

    [Fact]
    public void Light_mode_uses_the_darker_shade()
    {
        var shades = new[] { C("#005FB8"), C("#0078D4"), C("#003E92"), C("#001A68") }; // Dark1, Accent, Dark2, Dark3
        Assert.Equal(C("#005FB8"), Flipr.UI.ThemeManager.PickAccent(shades, LightSegment));
    }

    [Fact]
    public void Falls_back_only_when_no_shade_is_readable()
    {
        var murky = new[] { C("#3A3A44"), C("#34343E"), C("#2B2B33") };
        Assert.Null(Flipr.UI.ThemeManager.PickAccent(murky, DarkSegment));
        Assert.Null(Flipr.UI.ThemeManager.PickAccent([], DarkSegment));
    }
}
