using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using OrbitProbe.Native;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace OrbitProbe;

/// <summary>
/// Headless checks for everything that does NOT need a human drag: conversions, naming, collisions,
/// atomic output, failure cleanup, source integrity, menu rules and CF_HDROP parsing.
/// Run: OrbitProbe.exe --selftest [folder]. Results go to selftest-result.txt in that folder.
/// This is engineering verification, not acceptance evidence for the drag interaction.
/// </summary>
public static class SelfTest
{
    public static async Task<int> RunAsync(string dir)
    {
        var lines = new List<string>();
        int failures = 0;
        void Check(bool ok, string name)
        {
            lines.Add($"{(ok ? "PASS" : "FAIL")}  {name}");
            if (ok) ProbeLog.Pass(name); else { ProbeLog.Fail(name); failures++; }
        }

        try
        {
            Fixtures.Create(dir);
            var originals = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .ToDictionary(f => f, SafeOutput.Sha256);
            string P(string name) => Path.Combine(dir, name);

            // --- menu rules ---
            var conv = Operations.ItemsFor(WheelMode.Convert, [P("a.jpg")]);
            Check(conv.Count == 4 && conv.All(i => i.Label != "JPG"), "Convert menu for a JPG offers 4 targets and hides JPG");
            var mixed = Operations.ItemsFor(WheelMode.Convert, [P("a.jpg"), P("b.png")]);
            Check(mixed.Count == 5, "Mixed JPG+PNG selection keeps all 5 targets (per-file skip instead)");
            Check(Operations.ItemsFor(WheelMode.Convert, [P("a.jpg"), P("c.pdf")]).Count == 0, "Image+PDF selection offers no conversions (intersection rule)");
            Check(Operations.ItemsFor(WheelMode.Actions, [P("c.pdf")]).Count == 3, "Actions offered for any file");

            // --- conversion + collision-safe naming ---
            var toPng = await Operations.ExecuteAsync(conv.First(i => i.Label == "PNG"), [P("a.jpg")]);
            Check(toPng[0].Output == P("a (2).png"), $"a.jpg → PNG avoids existing a.png (got '{Path.GetFileName(toPng[0].Output ?? toPng[0].Error)}')");
            Check(Dims(P("a (2).png")) == (1280, 960), "Converted PNG decodes with original dimensions");

            foreach (var item in conv)
            {
                var r = await Operations.ExecuteAsync(item, [P("ü nicode 名前.jpg")]);
                Check(r[0].Error is null && r[0].Output is not null && File.Exists(r[0].Output!) && Dims(r[0].Output!) == (640, 480),
                    $"Unicode name → {item.Label}: {Path.GetFileName(r[0].Output ?? r[0].Error)}");
            }

            var bmpToGif = await Operations.ExecuteAsync(new WheelItem("convert.gif", "GIF", "", ""), [P("d.bmp")]);
            Check(bmpToGif[0].Output is { } g && Dims(g) == (320, 240), "BMP → GIF (palettised by encoder)");

            // --- transparency flattened onto white for JPG ---
            var pngToJpg = await Operations.ExecuteAsync(new WheelItem("convert.jpg", "JPG", "", ""), [P("b.png")]);
            Check(pngToJpg[0].Output is { } j && CenterPixelIsWhite(j), "Transparent PNG → JPG flattens onto white, not black");

            // --- batch with per-file skip ---
            var batch = await Operations.ExecuteAsync(new WheelItem("convert.jpg", "JPG", "", ""), [P("a.jpg"), P("d.bmp")]);
            Check(batch[0].Error?.Contains("already JPG") == true && batch[1].Output is not null,
                "Batch: file already in target format is skipped, the other still converts");

            // --- tools ---
            var rot1 = await Operations.ExecuteAsync(new WheelItem("tool.rotate-cw", "", "", ""), [P("a.jpg")]);
            var rot2 = await Operations.ExecuteAsync(new WheelItem("tool.rotate-cw", "", "", ""), [P("a.jpg")]);
            Check(rot1[0].Output == P("a (rotated).jpg") && Dims(rot1[0].Output!) == (960, 1280), "Rotate → 'a (rotated).jpg' with swapped dimensions");
            Check(rot2[0].Output == P("a (rotated) (2).jpg"), "Second rotate → 'a (rotated) (2).jpg' (no overwrite)");
            var gray = await Operations.ExecuteAsync(new WheelItem("tool.grayscale", "", "", ""), [P("b.png")]);
            Check(gray[0].Output == P("b (grayscale).png"), "Grayscale → 'b (grayscale).png'");

            // --- long path ---
            string longFile = Directory.EnumerateFiles(dir, "long path image.jpg", SearchOption.AllDirectories).First();
            var lp = await Operations.ExecuteAsync(new WheelItem("convert.png", "PNG", "", ""), [longFile]);
            Check(lp[0].Output is not null && lp[0].Output!.Length > 260, $"Long path (>260 chars) conversion ({lp[0].Output?.Length ?? 0} chars)");

            // --- failure leaves nothing behind ---
            int before = Directory.GetFiles(dir).Length;
            bool threw = false;
            try { SafeOutput.WriteBeside(P("a.jpg"), ".png", "x", s => s.WriteByte(1), _ => "forced validation failure"); }
            catch (IOException) { threw = true; }
            Check(threw && Directory.GetFiles(dir).Length == before, "Failed validation → exception, no output, no temp file");
            Check(!SafeOutput.LeftoverTemps(dir).Any(), "No leftover .flipr-*.tmp files");

            // --- originals untouched ---
            bool intact = originals.All(kv => File.Exists(kv.Key) && SafeOutput.Sha256(kv.Key) == kv.Value);
            Check(intact, $"All {originals.Count} original files byte-identical (SHA-256), including collision bait a.png");

            // --- CF_HDROP parsing + DROPDESCRIPTION through the COM IDataObject interface (in-process) ---
            var dataObject = new DataObject(DataFormats.FileDrop, new[] { P("a.jpg"), P("ü nicode 名前.jpg") });
            var com = (ComIDataObject)dataObject;
            var read = DataObjectReader.ReadFileDrop(com);
            Check(read.Length == 2 && read[1] == P("ü nicode 名前.jpg"), "CF_HDROP parsed via COM IDataObject (Unicode intact)");
            Check(DataObjectReader.ListFormats(com).Contains("CF_HDROP"), "EnumFormatEtc lists CF_HDROP");
            Check(DataObjectReader.TrySetDropDescription(com, Ole.DROPIMAGE_COPY, "Convert to %1", "PNG"),
                "DROPDESCRIPTION accepted by a data object via SetData");
        }
        catch (Exception ex)
        {
            Check(false, $"Self-test crashed: {ex}");
        }

        lines.Add($"{(failures == 0 ? "ALL PASSED" : $"{failures} FAILURE(S)")}");
        File.WriteAllLines(Path.Combine(dir, "selftest-result.txt"), lines);
        return failures;
    }

    private static (int, int) Dims(string path)
    {
        var b = Operations.Load(path);
        return (b.PixelWidth, b.PixelHeight);
    }

    private static bool CenterPixelIsWhite(string path)
    {
        BitmapSource b = Operations.Load(path);
        var px = new byte[4];
        b.CopyPixels(new Int32Rect(b.PixelWidth / 2, b.PixelHeight / 2, 1, 1), px, 4, 0);
        return px[0] > 240 && px[1] > 240 && px[2] > 240;
    }
}
