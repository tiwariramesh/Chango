using Chango.Conversion.Imaging;
using Chango.Core;
using Chango.Tools;

namespace Chango.Tests;

public class RegistryTests
{
    private static readonly OperationRegistry Registry =
        new([new ImageConversionProvider(), new ImageToolsProvider(), new FileActionsProvider()]);

    private static FileSet Files(params string[] names) => new(names.Select(n => Path.Combine(@"C:\x", n)));

    [Fact]
    public void Single_jpg_offers_every_other_format_in_order()
    {
        var labels = Registry.Query(Files("a.jpg"), OperationMode.Convert).Select(d => d.Label);
        Assert.Equal(["PNG", "WEBP", "BMP", "TIFF", "GIF", "JFIF"], labels);
    }

    [Fact]
    public void Mixed_image_selection_keeps_all_targets_and_skips_per_file()
    {
        Assert.Equal(7, Registry.Query(Files("a.jpg", "b.png"), OperationMode.Convert).Count);
    }

    [Fact]
    public void Only_options_valid_for_every_file_are_offered()
    {
        Assert.Empty(Registry.Query(Files("a.jpg", "c.pdf"), OperationMode.Convert));
        Assert.Empty(Registry.Query(Files("a.jpg", "c.pdf"), OperationMode.Tools));
        Assert.Equal(["Copy path", "Show in folder", "Copy to", "Move to", "Float", "Share", "Terminal"],
            OperationMenu.TopLevel(Registry.Query(Files("a.jpg", "c.pdf"), OperationMode.Actions)).Select(d => d.Label));
    }

    [Fact]
    public void Extensions_are_case_insensitive_and_aliases_map()
    {
        var labels = Registry.Query(Files("A.JPEG"), OperationMode.Convert).Select(d => d.Label);
        Assert.DoesNotContain("JPG", labels);
    }

    [Fact]
    public void Top_level_tools_have_glyphs_and_formats_do_not()
    {
        // Inside a group, size presets ("50%", "1 MB") are text like formats; the segments you see first all have icons.
        Assert.All(OperationMenu.TopLevel(Registry.Query(Files("a.png"), OperationMode.Tools)), d => Assert.NotNull(d.Glyph));
        Assert.All(Registry.Query(Files("a.png"), OperationMode.Convert), d => Assert.Null(d.Glyph));
    }

    [Fact]
    public void Empty_selection_offers_nothing()
    {
        foreach (var mode in Enum.GetValues<OperationMode>()) Assert.Empty(Registry.Query(FileSet.Empty, mode));
    }

    [Fact]
    public void Every_descriptor_resolves_to_its_provider()
    {
        foreach (var mode in Enum.GetValues<OperationMode>())
        foreach (var d in Registry.Query(Files("a.png"), mode))
            Assert.Equal(d.ProviderId, Registry.ProviderFor(d).Id);
    }

    [Fact]
    public void FileSet_deduplicates_case_insensitively()
    {
        Assert.Single(new FileSet([@"C:\x\a.png", @"C:\X\A.PNG"]));
    }
}
