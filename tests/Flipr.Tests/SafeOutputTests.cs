using Flipr.Core;

namespace Flipr.Tests;

public class SafeOutputTests
{
    private static string Write(string source, OutputOptions? options = null, string? ext = ".png", string? suffix = null,
        Func<string, string?>? validate = null) =>
        SafeOutput.Write(source, options ?? new OutputOptions(), ext, suffix, s => s.Write([1, 2, 3]), validate ?? (_ => null));

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
        Assert.Equal(t.File("photo (2).png"), Write(t.File("photo.jpg")));
        Assert.Equal(t.File("photo (3).png"), Write(t.File("photo.jpg")));
        Assert.Equal("bait", File.ReadAllText(t.File("photo.png")));
    }

    [Fact]
    public void Tool_suffix_is_applied_before_collision_numbering()
    {
        using var t = new TempFolder();
        File.WriteAllText(t.File("a.jpg"), "x");
        Assert.Equal(t.File("a (rotated).jpg"), Write(t.File("a.jpg"), ext: null, suffix: "rotated"));
        Assert.Equal(t.File("a (rotated) (2).jpg"), Write(t.File("a.jpg"), ext: null, suffix: "rotated"));
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
            SafeOutput.Write(t.File("a.jpg"), new OutputOptions(), ".png", null, _ => throw new InvalidOperationException(), _ => null));
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
            SafeOutput.Write(t.File("a.jpg"), new OutputOptions(), ".png", null, s => { s.WriteByte(1); cts.Cancel(); }, _ => null, cts.Token));
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
