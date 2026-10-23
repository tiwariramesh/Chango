namespace Flipr.Conversion.Media;

/// <summary>
/// WPF's GIF encoder writes frames but no timing and no loop instruction (it ignores frame metadata), so the
/// animation would flash through once. This rewrites the block stream: a NETSCAPE2.0 "loop forever" extension after
/// the global colour table, and a Graphic Control Extension with the frame delay before every image.
/// </summary>
public static class GifLoop
{
    private static readonly byte[] LoopExtension =
        [0x21, 0xFF, 0x0B, .. "NETSCAPE2.0"u8.ToArray(), 0x03, 0x01, 0x00, 0x00, 0x00];

    /// <param name="delayCentiseconds">Frame delay in hundredths of a second.</param>
    /// <param name="offsets">
    /// For delta-encoded animations: where each frame's image sits on the canvas. Frames then stack ("do not dispose"),
    /// so each one only needs the region that changed.
    /// </param>
    public static byte[] AddTimingAndLoop(byte[] gif, int delayCentiseconds, IReadOnlyList<(int X, int Y)>? offsets = null)
    {
        int frame = 0;
        if (gif.Length < 13 || gif[0] != 'G' || gif[1] != 'I' || gif[2] != 'F') throw new InvalidDataException("Not a GIF.");
        using var output = new MemoryStream(gif.Length + 1024);
        int at = 13; // header (6) + logical screen descriptor (7)
        if ((gif[10] & 0x80) != 0) at += 3 * (1 << ((gif[10] & 0x07) + 1)); // global colour table
        output.Write("GIF89a"u8);  // extensions need 89a
        output.Write(gif, 6, at - 6);
        output.Write(LoopExtension);

        byte disposal = offsets is null ? (byte)0x08 /* restore to background */ : (byte)0x04 /* leave in place */;
        byte[] control = [0x21, 0xF9, 0x04, disposal, (byte)delayCentiseconds, (byte)(delayCentiseconds >> 8), 0x00, 0x00];
        int i = at;
        while (i < gif.Length)
        {
            byte block = gif[i];
            if (block == 0x3B) { output.WriteByte(0x3B); break; } // trailer
            if (block == 0x21) // extension: copy, except existing graphic-control and loop blocks (we write our own)
            {
                byte label = gif[i + 1];
                int end = SkipSubBlocks(gif, i + 2);
                bool ours = label == 0xF9 || (label == 0xFF && gif.AsSpan(i + 3, Math.Min(11, gif.Length - i - 3)).SequenceEqual("NETSCAPE2.0"u8));
                if (!ours) output.Write(gif, i, end - i);
                i = end;
            }
            else if (block == 0x2C) // image: descriptor (10) + optional local colour table + LZW size + data
            {
                int j = i + 10;
                byte packed = gif[i + 9];
                if ((packed & 0x80) != 0) j += 3 * (1 << ((packed & 0x07) + 1));
                j = SkipSubBlocks(gif, j + 1);
                output.Write(control);
                if (offsets is not null && frame < offsets.Count)
                {
                    var (x, y) = offsets[frame];
                    gif[i + 1] = (byte)x; gif[i + 2] = (byte)(x >> 8);
                    gif[i + 3] = (byte)y; gif[i + 4] = (byte)(y >> 8);
                }
                frame++;
                output.Write(gif, i, j - i);
                i = j;
            }
            else throw new InvalidDataException("Unexpected data in the GIF.");
        }
        if (output.Length == 0 || output.GetBuffer()[output.Length - 1] != 0x3B) output.WriteByte(0x3B);
        return output.ToArray();
    }

    /// <summary>Index just past a chain of data sub-blocks (each length-prefixed, ending with a zero-length block).</summary>
    private static int SkipSubBlocks(byte[] gif, int i)
    {
        while (i < gif.Length)
        {
            int size = gif[i];
            i += 1 + size;
            if (size == 0) break;
        }
        return Math.Min(i, gif.Length);
    }
}
