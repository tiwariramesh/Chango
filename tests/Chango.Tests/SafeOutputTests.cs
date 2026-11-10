using Chango.Core;

namespace Chango.Tests;

public class SafeOutputTests
{
    private static string Write(string source, OutputOptions? options = null, string? ext = ".png", bool numbered = false,
        Func<string, string?>? validate = null) =>
        SafeOutput.Write(source, options ?? new OutputOptions(), ext, numbered, s => s.Write([1, 2, 3]), validate ?? (_ => null));

    [Fact]
    public void Writes_beside_the_original_with_new_extension()
    {
        using var t = new TempFolder();
        File.WriteAllText(t.File("photo.jpg"), "x");
        Assert.Equal(t.File("photo.png"), Write(t.File("photo.jpg")));
    }

    [Fact]
    public void Never_overwrites_existing_files_and_numbers_collisions()
    {
        using var t = new TempFolder();
        File.WriteAllText(t.File("photo.jpg"), "x");
        File.WriteAllText(t.File("photo.png"), "bait");
        Assert.Equal(t.File("photo_1.png"), Write(t.File("photo.jpg")));
        Assert.Equal(t.File("photo_2.png"), Write(t.File("photo.jpg")));
        Assert.Equal("bait", File.ReadAllText(t.File("photo.png")));
    }

    [Fact]
    public void Tool_results_are_numbered_after_the_source()
    {
        using var t = new TempFolder();
        File.WriteAllText(t.File("abc.jpg"), "x");
        string first = Write(t.File("abc.jpg"), ext: null, numbered: true);
        Assert.Equal(t.File("abc_1.jpg"), first);
        // Compressing the result again continues the count instead of stacking suffixes.
        Assert.Equal(t.File("abc_2.jpg"), Write(first, ext: null, numbered: true));
        // The original once more: _1 and _2 are taken.
        Assert.Equal(t.File("abc_3.jpg"), Write(t.File("abc.jpg"), ext: null, numbered: true));
        // A tool that changes the format numbers within the new extension.
        Assert.Equal(t.File("abc_1.png"), Write(t.File("abc.jpg"), ext: ".png", numbered: true));
    }

    [Theory]
    [InlineData("abc", "abc_1")]
    [InlineData("abc_1", "abc_2")]
    [InlineData("abc_9", "abc_10")]
    [InlineData("abc_99", "abc_100")]
    [InlineData("IMG_1234", "IMG_1234_1")]   // a camera number, not Chango's
    [InlineData("DSC_0001", "DSC_0001_1")]   // leading zero: a camera number
    [InlineData("abc_0", "abc_0_1")]
    [InlineData("_1", "_1_1")]              // nothing before the number
    [InlineData("my_photo", "my_photo_1")]
    public void Only_small_numbers_count_as_Chango_numbers(string stem, string expected) =>
        Assert.Equal(expected + ".jpg", SafeOutput.CandidateNames(stem, ".jpg", numbered: true).First());

    [Fact]
    public void Results_keep_the_original_dates()
    {
        using var t = new TempFolder();
        string source = t.File("old.jpg");
        File.WriteAllText(source, "x");
        var created = new DateTime(2019, 5, 4, 10, 30, 0, DateTimeKind.Utc);
        var modified = new DateTime(2020, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        File.SetCreationTimeUtc(source, created);
        File.SetLastWriteTimeUtc(source, modified);

        string output = Write(source);
        Assert.Equal(created, File.GetCreationTimeUtc(output));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(output));

        string today = Write(source, new OutputOptions(KeepSourceDates: false), ext: ".bmp");
        Assert.True(File.GetLastWriteTimeUtc(today) > DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public void Page_folders_can_keep_the_original_dates()
    {
        using var t = new TempFolder();
        string source = t.File("doc.pdf");
        File.WriteAllText(source, "x");
        var modified = new DateTime(2018, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, modified);
        string folder = SafeOutput.WriteFolder(source, new OutputOptions(), "doc (PNG)",
            dir => File.WriteAllText(Path.Combine(dir, "Page 1.png"), "p"), dateContents: true);
        Assert.Equal(modified, File.GetLastWriteTimeUtc(Path.Combine(folder, "Page 1.png")));
    }

    [Fact]
    public void Copies_are_named_like_File_Explorer_names_them()
    {
        using var t = new TempFolder();
        File.WriteAllText(t.File("a.txt"), "x");
        string copy() => SafeOutput.WriteToPath(t.File("a.txt"), new OutputOptions(), null, false,
            temp => File.Copy(t.File("a.txt"), temp, overwrite: true), _ => null, hideWhileWriting: false, copyNames: true);
        Assert.Equal(t.File("a (2).txt"), copy());
        Assert.Equal(t.File("a (3).txt"), copy());
    }

    [Fact]
    public void Undo_recognises_results_that_carry_older_dates()
    {
        using var t = new TempFolder();
        string source = t.File("old.jpg");
        File.WriteAllText(source, "x");
        File.SetCreationTimeUtc(source, DateTime.UtcNow.AddYears(-3));
        var started = DateTime.UtcNow.AddSeconds(-1);
        string output = Write(source);
        var op = new OperationDescriptor("convert.image.png", "test", OperationMode.Convert, "PNG", null, "%1", "PNG", "PNG", 1);
        Assert.Equal([output], Undo.Targets(op, [source], [new FileOutcome(source, OutcomeStatus.Succeeded, output)], started));

        // A different file put at that path is not Chango's, whatever its name.
        File.Delete(output);
        File.WriteAllText(output, "someone else's");
        File.SetCreationTimeUtc(output, DateTime.UtcNow.AddYears(-3));
        Assert.Empty(Undo.Targets(op, [source], [new FileOutcome(source, OutcomeStatus.Succeeded, output)], started));
    }

    [Fact]
    public void Failed_validation_leaves_no_output_and_no_temp_file()
    {
        using var t = new TempFolder();
        File.WriteAllText(t.File("a.jpg"), "x");
        Assert.Throws<IOException>(() => Write(t.File("a.jpg"), validate: _ => "broken"));
        Assert.Equal([t.File("a.jpg")], Directory.GetFiles(t.Path));
    }

    [Fact]
    public void Exception_while_writing_leaves_nothing_behind()
    {
        using var t = new TempFolder();
        File.WriteAllText(t.File("a.jpg"), "x");
        Assert.Throws<InvalidOperationException>(() =>
            SafeOutput.Write(t.File("a.jpg"), new OutputOptions(), ".png", false, _ => throw new InvalidOperationException(), _ => null));
        Assert.Empty(SafeOutput.LeftoverTemps(t.Path));
        Assert.Single(Directory.GetFiles(t.Path));
    }

    [Fact]
    public void Cancellation_after_writing_discards_the_temp_file()
    {
        using var t = new TempFolder();
        File.WriteAllText(t.File("a.jpg"), "x");
        using var cts = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            SafeOutput.Write(t.File("a.jpg"), new OutputOptions(), ".png", false, s => { s.WriteByte(1); cts.Cancel(); }, _ => null, cts.Token));
        Assert.Single(Directory.GetFiles(t.Path));
    }

    [Fact]
    public void Folder_option_writes_into_the_chosen_folder()
    {
        using var t = new TempFolder();
        File.WriteAllText(t.File("a.jpg"), "x");
        string target = t.File("out");
        Assert.Equal(Path.Combine(target, "a.png"), Write(t.File("a.jpg"), new OutputOptions(OutputLocation.Folder, target)));
    }

    [Fact]
    public void Original_is_byte_identical_afterwards()
    {
        using var t = new TempFolder();
        File.WriteAllBytes(t.File("a.jpg"), [9, 8, 7]);
        string before = SafeOutput.Sha256(t.File("a.jpg"));
        Write(t.File("a.jpg"));
        Assert.Equal(before, SafeOutput.Sha256(t.File("a.jpg")));
    }
}
