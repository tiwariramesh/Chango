using OrbitDrop.Core;
using OrbitDrop.UI.Settings;

namespace OrbitDrop.App;

/// <summary>
/// Lists every option OrbitDrop offers, per kind of file, by asking the live registry about sample files. So the
/// Wheel page in Settings always matches the real wheel, including options added later.
/// </summary>
internal static class WheelCatalogBuilder
{
    private sealed record Sample(string Title, string Glyph, string[][] FileSets);

    private static readonly Sample[] Samples =
    [
        new("Photos", "", [["photo.jpg"], ["photo-1.jpg", "photo-2.png"]]),
        new("Video", "", [["clip.mp4"], ["clip-1.mp4", "clip-2.mp4"]]),
        new("Audio", "", [["song.mp3"]]),
        new("PDF", "", [["document.pdf"], ["document-1.pdf", "document-2.pdf"]]),
        new("Documents", "", [["report.docx"], ["report.doc"], ["sheet.xls"], ["slides.ppt"], ["drawing.cdr"]]),
        new("Archives","", [["archive.zip"]]),
        new("Any file", "", [["notes.txt"]]),
    ];

    public static IReadOnlyList<WheelCategory> Build(OperationRegistry registry)
    {
        // Some providers check that files exist (metadata), so the samples are real, empty files.
        string dir = Path.Combine(Path.GetTempPath(), "OrbitDrop", "wheel-catalog");
        Directory.CreateDirectory(dir);
        string Real(string name)
        {
            string p = Path.Combine(dir, name);
            if (!File.Exists(p)) File.WriteAllBytes(p, []);
            return p;
        }

        // Each kind of file gets its whole wheel (as Settings → Wheel shows it); an option shared by several kinds is
        // one setting, so hiding it in one place hides it everywhere.
        var result = new List<WheelCategory>();
        foreach (var sample in Samples)
        {
            var modes = Entries(registry, sample, Real);
            if (modes.Count > 0) result.Add(new WheelCategory(sample.Title, sample.Glyph, modes));
        }
        return result;
    }

    private static List<WheelModeSection> Entries(OperationRegistry registry, Sample sample, Func<string, string> real)
    {
        var modes = new List<WheelModeSection>();
        foreach (var mode in Enum.GetValues<OperationMode>())
        {
            var flat = sample.FileSets.SelectMany(set => registry.Query(new FileSet(set.Select(real)), mode))
                .GroupBy(o => o.Id).Select(g => g.First()).OrderBy(o => o.Order).ToList();
            var entries = new List<WheelEntry>();
            var seenGroups = new HashSet<string>();
            foreach (var op in flat)
            {
                if (op.Group is { } g)
                {
                    if (!seenGroups.Add(g.Id)) continue;
                    var members = flat.Where(o => o.Group?.Id == g.Id && WheelFilter.OptionKey(o) is not null)
                        .Select(o => new WheelChoice(WheelFilter.OptionKey(o)!, o.Label, o.Glyph)).ToList();
                    entries.Add(new WheelEntry(new WheelChoice(WheelFilter.GroupKey(g), g.Label, g.Glyph), members));
                }
                else if (WheelFilter.OptionKey(op) is { } key)
                    entries.Add(new WheelEntry(new WheelChoice(key, op.Label, op.Glyph), []));
            }
            if (entries.Count > 0) modes.Add(new WheelModeSection(mode, entries));
        }
        return modes;
    }

}
