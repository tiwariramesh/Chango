using Flipr.Core;
using Flipr.Tools;

namespace Flipr.Tests;

public class FileActionTests
{
    private static List<FileOutcome> Run(string id, OperationContext ctx, params string[] items)
    {
        var provider = new FileActionsProvider();
        var files = new FileSet(items);
        var op = new OperationDescriptor(id, FileActionsProvider.ProviderId, OperationMode.Actions, "x", null, "%1", "x", "x", 0);
        var c = new Collector();
        provider.ExecuteAsync(op, files, ctx, c, CancellationToken.None).GetAwaiter().GetResult();
        return c.Outcomes;
    }

    private static string To(string prefix, string folder) => $"{prefix}{FileActionsProvider.FolderSeparator}{folder}";

    [Fact]
    public void Copy_to_never_overwrites_and_keeps_the_original()
    {
        using var t = new TempFolder();
        string src = t.Image("photo.png", 16, 16);
        string dest = Directory.CreateDirectory(t.File("out")).FullName;
        var first = Assert.Single(Run(To(FileActionsProvider.CopyToPrefix, dest), Ctx.Default(), src));
        var second = Assert.Single(Run(To(FileActionsProvider.CopyToPrefix, dest), Ctx.Default(), src));
        Assert.Equal(Path.Combine(dest, "photo.png"), first.Output);
        Assert.Equal(Path.Combine(dest, "photo (2).png"), second.Output);
        Assert.True(File.Exists(src));
        Assert.Equal(SafeOutput.Sha256(src), SafeOutput.Sha256(second.Output!));
        Assert.Empty(SafeOutput.LeftoverTemps(dest));
        Assert.False(File.GetAttributes(first.Output!).HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public void Folders_copy_recursively_and_move()
    {
        using var t = new TempFolder();
        string folder = Directory.CreateDirectory(t.File("album")).FullName;
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "sub", "note.txt"), "hi");
        string dest = Directory.CreateDirectory(t.File("dest")).FullName;

        var copy = Assert.Single(Run(To(FileActionsProvider.CopyToPrefix, dest), Ctx.Default(), folder));
        Assert.Equal("hi", File.ReadAllText(Path.Combine(copy.Output!, "sub", "note.txt")));
        Assert.True(Directory.Exists(folder));

        var moved = Assert.Single(Run(To(FileActionsProvider.MoveToPrefix, dest), Ctx.Default(), folder));
        Assert.Equal(Path.Combine(dest, "album (2)"), moved.Output);
        Assert.False(Directory.Exists(folder));
        Assert.Empty(SafeOutput.LeftoverTemps(dest));
    }

    [Fact]
    public void Moving_into_the_same_folder_is_skipped_and_a_folder_cannot_go_inside_itself()
    {
        using var t = new TempFolder();
        string src = t.Image("a.png", 8, 8);
        Assert.Equal(OutcomeStatus.Skipped, Assert.Single(Run(To(FileActionsProvider.MoveToPrefix, t.Path), Ctx.Default(), src)).Status);

        string folder = Directory.CreateDirectory(t.File("box")).FullName;
        string inner = Directory.CreateDirectory(Path.Combine(folder, "inner")).FullName;
        Assert.Equal(OutcomeStatus.Failed, Assert.Single(Run(To(FileActionsProvider.CopyToPrefix, inner), Ctx.Default(), folder)).Status);
    }

    [Fact]
    public void Choose_uses_the_picked_folder_from_input()
    {
        using var t = new TempFolder();
        string src = t.Image("b.png", 8, 8);
        string dest = Directory.CreateDirectory(t.File("picked")).FullName;
        var o = Assert.Single(Run(FileActionsProvider.CopyToPrefix + FileActionsProvider.ChooseSuffix, Ctx.Default() with { Input = dest }, src));
        Assert.Equal(Path.Combine(dest, "b.png"), o.Output);
    }

    [Fact]
    public void Copy_to_offers_choose_first_then_recent_and_common_folders()
    {
        using var t = new TempFolder();
        var provider = new FileActionsProvider(() => [t.Path]);
        var copyTo = provider.GetOperations(new FileSet([t.File("x.txt")]), OperationMode.Actions)
            .Where(o => o.Group?.Id == "actions.copy-to").ToList();
        Assert.Equal("Choose…", copyTo[0].Label);
        Assert.True(copyTo[0].NeedsInput);
        Assert.Equal(To(FileActionsProvider.CopyToPrefix, t.Path), copyTo[1].Id);
        Assert.Contains(copyTo, o => o.Label == "Desktop");
    }
}
