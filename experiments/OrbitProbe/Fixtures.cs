using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitProbe;

/// <summary>Creates the test folder used by the manual test sheet. Never overwrites existing files.</summary>
public static class Fixtures
{
    public const string DefaultFolder = @"C:\OrbitTest";

    public static List<string> Create(string folder)
    {
        Directory.CreateDirectory(folder);
        var created = new List<string>();

        void Image(string name, int w, int h, bool alpha, Color a, Color b)
        {
            string path = Path.Combine(folder, name);
            if (File.Exists(path)) return;
            var bmp = Gradient(w, h, alpha, a, b);
            BitmapEncoder enc = Path.GetExtension(name).ToLowerInvariant() switch
            {
                ".png" => new PngBitmapEncoder(),
                ".bmp" => new BmpBitmapEncoder(),
                _ => new JpegBitmapEncoder { QualityLevel = 90 },
            };
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = new FileStream(path, FileMode.CreateNew);
            enc.Save(fs);
            created.Add(path);
        }

        Image("a.jpg", 1280, 960, false, Color.FromRgb(255, 140, 40), Color.FromRgb(90, 60, 200));
        Image("b.png", 800, 600, true, Color.FromRgb(20, 160, 120), Color.FromRgb(240, 220, 40));
        Image("ü nicode 名前.jpg", 640, 480, false, Color.FromRgb(200, 40, 80), Color.FromRgb(40, 120, 220));
        Image("d.bmp", 320, 240, false, Color.FromRgb(60, 60, 60), Color.FromRgb(220, 220, 220));
        // Collision bait: an unrelated a.png that must never be overwritten by "a.jpg → PNG".
        Image("a.png", 64, 64, false, Color.FromRgb(0, 0, 0), Color.FromRgb(255, 255, 255));

        string pdf = Path.Combine(folder, "c.pdf");
        if (!File.Exists(pdf)) { File.WriteAllBytes(pdf, MinimalPdf()); created.Add(pdf); }

        string txt = Path.Combine(folder, "notes.txt");
        if (!File.Exists(txt)) { File.WriteAllText(txt, "Orbit Probe test file.\r\n"); created.Add(txt); }

        string longDir = Path.Combine(folder, new string('L', 120), new string('M', 120));
        string longFile = Path.Combine(longDir, "long path image.jpg");
        if (!File.Exists(longFile))
        {
            Directory.CreateDirectory(longDir);
            var bmp = Gradient(200, 150, false, Colors.Teal, Colors.Orange);
            var enc = new JpegBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = new FileStream(longFile, FileMode.CreateNew);
            enc.Save(fs);
            created.Add(longFile);
        }
        return created;
    }

    private static BitmapSource Gradient(int w, int h, bool alpha, Color a, Color b)
    {
        int stride = w * 4;
        var px = new byte[stride * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double t = (double)x / (w - 1);
            int i = y * stride + x * 4;
            px[i] = (byte)(a.B + (b.B - a.B) * t);
            px[i + 1] = (byte)(a.G + (b.G - a.G) * t);
            px[i + 2] = (byte)(a.R + (b.R - a.R) * t);
            // Transparent circle in the middle when alpha is requested (tests JPG flattening).
            double dx = x - w / 2.0, dy = y - h / 2.0;
            px[i + 3] = alpha && dx * dx + dy * dy < (h / 4.0) * (h / 4.0) ? (byte)0 : (byte)255;
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
        bmp.Freeze();
        return bmp;
    }

    private static byte[] MinimalPdf()
    {
        // Single-page PDF with correct xref offsets.
        var objs = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            "<< /Length 42 >>\nstream\nBT /F1 24 Tf 72 760 Td (Orbit Probe) Tj ET\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objs.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n");
        }
        int xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objs.Length + 1}\n0000000000 65535 f \n");
        foreach (int o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objs.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
