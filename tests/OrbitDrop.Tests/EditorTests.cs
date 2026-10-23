using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitDrop.Conversion;
using OrbitDrop.UI.Editor;

namespace OrbitDrop.Tests;

public class EditorTests
{
    private static BitmapSource Gradient(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            px[i] = (byte)(x * 255 / w); px[i + 1] = (byte)(y * 255 / h); px[i + 2] = 128; px[i + 3] = 255;
        }
        return EditRenderer.FromPixels(px, w, h);
    }

    private static T Sta<T>(Func<T> f)
    {
        T result = default!;
        StaWorker.RunAsync(() => result = f()).GetAwaiter().GetResult();
        return result;
    }

    private static (byte B, byte G, byte R) At(BitmapSource b, int x, int y)
    {
        var px = new byte[4];
        b.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return (px[0], px[1], px[2]);
    }

    [Fact]
    public void Crop_sets_the_output_size()
    {
        var result = Sta(() => EditRenderer.Export(Gradient(200, 100), new Adjustments(), [], new Int32Rect(10, 20, 50, 40)));
        Assert.Equal((50, 40), (result.PixelWidth, result.PixelHeight));
    }

    [Fact]
    public void Black_box_redaction_is_solid_black_and_marks_change_pixels()
    {
        var marks = new List<Mark> { new RedactMark(new Rect(10, 10, 30, 30), RedactKind.Black), new BoxMark(Colors.Red, 4, new Rect(100, 20, 60, 40)) };
        var result = Sta(() => EditRenderer.Export(Gradient(200, 100), new Adjustments(), marks, null));
        Assert.Equal((0, 0, 0), At(result, 25, 25));
        var edge = At(result, 100, 40);
        Assert.True(edge.R > 200 && edge.G < 60, $"expected the red box outline, got {edge}");
    }

    [Fact]
    public void Pixelate_makes_blocks_of_one_colour()
    {
        var marks = new List<Mark> { new RedactMark(new Rect(0, 0, 120, 120), RedactKind.Pixelate) };
        var result = Sta(() => EditRenderer.Export(Gradient(120, 120), new Adjustments(), marks, null));
        // Block size is long side / 60 = 2 px minimum 4 → 4 px blocks: (0,0) and (3,3) share a block.
        Assert.Equal(At(result, 0, 0), At(result, 3, 3));
        Assert.NotEqual(At(result, 0, 0), At(result, 100, 100));
    }

    [Fact]
    public void Brightness_brightens()
    {
        var src = Gradient(50, 50);
        var result = Sta(() => EditRenderer.Export(src, new Adjustments(Brightness: 0.5), [], null));
        var before = At(src, 25, 25);
        var after = At(result, 25, 25);
        Assert.True(after.R > before.R && after.G > before.G);
    }
}
