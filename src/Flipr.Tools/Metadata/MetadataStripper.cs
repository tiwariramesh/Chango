using System.Buffers.Binary;

namespace Flipr.Tools.Metadata;

/// <summary>
/// Removes metadata without re-encoding: the image or audio data is copied byte for byte, only the metadata
/// containers are left out. JPEG keeps its colour profile and, if the photo has one, a minimal EXIF block that holds
/// only the orientation, so it still displays upright.
/// </summary>
public static class MetadataStripper
{
    public static bool CanStrip(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".jpg" or ".jpeg" or ".jpe" or ".jfif" or ".png" or ".mp3" or ".flac";

    /// <summary>Writes a metadata-free copy of <paramref name="source"/> to <paramref name="output"/>.</summary>
    public static void Strip(string source, Stream output)
    {
        byte[] data = File.ReadAllBytes(source);
        byte[] result = Path.GetExtension(source).ToLowerInvariant() switch
        {
            ".png" => Png(data),
            ".mp3" => Mp3(data),
            ".flac" => Flac(data),
            _ => Jpeg(data),
        };
        output.Write(result);
    }

    // ------------------------------------------------------------------ JPEG

    /// <summary>Drops APP1 (EXIF/XMP), APP12/APP13 (IPTC/Photoshop) and comments; keeps JFIF, ICC (APP2) and Adobe (APP14).</summary>
    public static byte[] Jpeg(byte[] d)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) throw new InvalidDataException("This isn't a valid JPEG.");
        int orientation = JpegOrientation(d);
        using var ms = new MemoryStream(d.Length);
        ms.Write(d, 0, 2); // SOI
        int i = 2;
        // The orientation-only EXIF goes straight after JFIF (APP0), or first if there is none.
        bool pendingExif = orientation > 1;
        while (i + 4 <= d.Length)
        {
            if (d[i] != 0xFF) throw new InvalidDataException("This JPEG is damaged.");
            byte marker = d[i + 1];
            if (marker == 0xFF) { i++; continue; } // fill byte
            if (marker == 0xDA) // start of scan: everything from here is image data
            {
                if (pendingExif) ms.Write(OrientationOnlyExif(orientation));
                ms.Write(d, i, d.Length - i);
                return ms.ToArray();
            }
            int length = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(i + 2));
            if (length < 2 || i + 2 + length > d.Length) throw new InvalidDataException("This JPEG is damaged.");
            // APP1 (EXIF/XMP), APP3–APP11, APP12/13 (IPTC, Photoshop), APP15 and comments carry metadata.
            bool drop = marker is 0xE1 or 0xFE or 0xEF || (marker >= 0xE3 && marker <= 0xED);
            if (!drop)
            {
                if (pendingExif && marker != 0xE0) { ms.Write(OrientationOnlyExif(orientation)); pendingExif = false; }
                ms.Write(d, i, 2 + length);
            }
            i += 2 + length;
        }
        throw new InvalidDataException("This JPEG has no image data.");
    }

    /// <summary>
    /// Removes only the GPS position: the EXIF GPS directory and its values are zeroed and unlinked in place (the
    /// file keeps its size and every other tag), and an XMP block that mentions GPS is dropped.
    /// </summary>
    public static byte[] JpegWithoutLocation(byte[] source)
    {
        if (source.Length < 4 || source[0] != 0xFF || source[1] != 0xD8) throw new InvalidDataException("This isn't a valid JPEG.");
        byte[] d = (byte[])source.Clone();
        using var ms = new MemoryStream(d.Length);
        ms.Write(d, 0, 2);
        int i = 2;
        while (i + 4 <= d.Length)
        {
            if (d[i] != 0xFF) throw new InvalidDataException("This JPEG is damaged.");
            byte marker = d[i + 1];
            if (marker == 0xFF) { i++; continue; }
            if (marker == 0xDA) { ms.Write(d, i, d.Length - i); return ms.ToArray(); }
            int length = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(i + 2));
            if (length < 2 || i + 2 + length > d.Length) throw new InvalidDataException("This JPEG is damaged.");
            var payload = d.AsSpan(i + 4, length - 2);
            bool keep = true;
            if (marker == 0xE1 && payload.StartsWith("Exif\0\0"u8)) RemoveGps(d, i + 10, i + 2 + length);
            else if (marker == 0xE1 && payload.StartsWith("http://ns.adobe.com/xap/1.0/"u8) && payload.IndexOf("GPS"u8) >= 0) keep = false;
            if (keep) ms.Write(d, i, 2 + length);
            i += 2 + length;
        }
        throw new InvalidDataException("This JPEG has no image data.");
    }

    /// <summary>Zeroes the GPS IFD (and the values it points to) inside the TIFF block at <paramref name="t"/> and unlinks it from IFD0.</summary>
    private static void RemoveGps(byte[] d, int t, int end)
    {
        if (t + 8 > end) return;
        bool le = d[t] == 'I';
        int U16(int at) => le ? BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at));
        int U32(int at) => (int)(le ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at)));
        void W16(int at, int v) { if (le) BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(at), (ushort)v); else BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(at), (ushort)v); }
        bool In(int at, int size) => at >= t && size >= 0 && at + size <= end;

        int ifd0 = t + U32(t + 4);
        if (!In(ifd0, 2)) return;
        int n = U16(ifd0);
        if (!In(ifd0 + 2, n * 12 + 4)) return;
        for (int e = 0; e < n; e++)
        {
            int entry = ifd0 + 2 + e * 12;
            if (U16(entry) != 0x8825) continue; // GPSInfo pointer
            int gps = t + U32(entry + 8);
            if (In(gps, 2))
            {
                int g = U16(gps);
                if (In(gps + 2, g * 12 + 4))
                {
                    for (int k = 0; k < g; k++)
                    {
                        int ge = gps + 2 + k * 12;
                        int size = TypeSize(U16(ge + 2)) * U32(ge + 4);
                        if (size > 4 && In(t + U32(ge + 8), size)) d.AsSpan(t + U32(ge + 8), size).Clear();
                    }
                    d.AsSpan(gps, 2 + g * 12 + 4).Clear();
                }
            }
            // Unlink: shift the later entries (and the next-IFD pointer) up one slot, then blank the freed slot.
            int after = entry + 12, tail = ifd0 + 2 + n * 12 + 4;
            Buffer.BlockCopy(d, after, d, entry, tail - after);
            d.AsSpan(tail - 12, 12).Clear();
            W16(ifd0, n - 1);
            return;
        }
    }

    private static int TypeSize(int type) => type switch { 3 or 8 => 2, 4 or 9 or 11 => 4, 5 or 10 or 12 => 8, _ => 1 };

    /// <summary>A complete APP1 segment containing an EXIF IFD with just the orientation tag.</summary>
    public static byte[] OrientationOnlyExif(int orientation)
    {
        var b = new byte[2 + 2 + 6 + 8 + 2 + 12 + 4];
        int p = 0;
        b[p++] = 0xFF; b[p++] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(p), (ushort)(b.Length - 2)); p += 2;
        "Exif\0\0"u8.CopyTo(b.AsSpan(p)); p += 6;
        // TIFF header, big-endian ("MM"), IFD0 at offset 8.
        b[p++] = (byte)'M'; b[p++] = (byte)'M'; b[p++] = 0; b[p++] = 42;
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(p), 8); p += 4;
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(p), 1); p += 2;            // one entry
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(p), 0x0112); p += 2;       // Orientation
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(p), 3); p += 2;            // SHORT
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(p), 1); p += 4;            // count
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(p), (ushort)orientation); p += 4; // value, padded
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(p), 0);                    // no next IFD
        return b;
    }

    /// <summary>EXIF orientation (1–8) from the first APP1 Exif segment, or 1.</summary>
    public static int JpegOrientation(byte[] d)
    {
        int i = 2;
        while (i + 4 <= d.Length && d[i] == 0xFF)
        {
            byte marker = d[i + 1];
            if (marker == 0xDA) break;
            int length = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(i + 2));
            if (marker == 0xE1 && length > 16 && d.AsSpan(i + 4, 6).SequenceEqual("Exif\0\0"u8))
            {
                int t = i + 10; // TIFF header
                bool le = d[t] == 'I';
                ushort U16(int at) => le ? BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at));
                uint U32(int at) => le ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at));
                int ifd = t + (int)U32(t + 4), end = i + 2 + length;
                if (ifd + 2 > end) return 1;
                int count = U16(ifd);
                for (int e = 0; e < count && ifd + 2 + e * 12 + 12 <= end; e++)
                {
                    int entry = ifd + 2 + e * 12;
                    if (U16(entry) == 0x0112) { int v = U16(entry + 8); return v is >= 1 and <= 8 ? v : 1; }
                }
                return 1;
            }
            i += 2 + length;
        }
        return 1;
    }

    // ------------------------------------------------------------------ PNG

    private static readonly HashSet<string> PngDrop = ["tEXt", "zTXt", "iTXt", "eXIf", "tIME"];

    /// <summary>Drops text, EXIF and timestamp chunks; keeps colour (iCCP, sRGB, gAMA, cHRM) and pixel-density chunks.</summary>
    public static byte[] Png(byte[] d)
    {
        ReadOnlySpan<byte> sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (d.Length < 8 || !d.AsSpan(0, 8).SequenceEqual(sig)) throw new InvalidDataException("This isn't a valid PNG.");
        using var ms = new MemoryStream(d.Length);
        ms.Write(d, 0, 8);
        int i = 8;
        while (i + 12 <= d.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(i));
            if (length < 0 || i + 12 + length > d.Length) throw new InvalidDataException("This PNG is damaged.");
            string type = System.Text.Encoding.ASCII.GetString(d, i + 4, 4);
            if (!PngDrop.Contains(type)) ms.Write(d, i, 12 + length);
            i += 12 + length;
            if (type == "IEND") break;
        }
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ MP3

    /// <summary>Drops ID3v2 tags at the start (all of them, if repeated) and the ID3v1 tag at the end.</summary>
    public static byte[] Mp3(byte[] d)
    {
        int start = 0;
        while (start + 10 <= d.Length && d[start] == 'I' && d[start + 1] == 'D' && d[start + 2] == '3')
        {
            int size = (d[start + 6] & 0x7F) << 21 | (d[start + 7] & 0x7F) << 14 | (d[start + 8] & 0x7F) << 7 | (d[start + 9] & 0x7F);
            bool footer = (d[start + 5] & 0x10) != 0;
            start += 10 + size + (footer ? 10 : 0);
        }
        int end = d.Length;
        if (end - start >= 128 && d[end - 128] == 'T' && d[end - 127] == 'A' && d[end - 126] == 'G') end -= 128;
        if (start >= end) throw new InvalidDataException("This MP3 has no audio.");
        return d[start..end];
    }

    // ------------------------------------------------------------------ FLAC

    /// <summary>Keeps STREAMINFO, SEEKTABLE and CUESHEET blocks; drops comments (tags), pictures, application and padding.</summary>
    public static byte[] Flac(byte[] d)
    {
        if (d.Length < 8 || d[0] != 'f' || d[1] != 'L' || d[2] != 'a' || d[3] != 'C') throw new InvalidDataException("This isn't a valid FLAC file.");
        var kept = new List<(int Offset, int Length, int Type)>();
        int i = 4;
        bool last = false;
        while (!last && i + 4 <= d.Length)
        {
            last = (d[i] & 0x80) != 0;
            int type = d[i] & 0x7F;
            int length = d[i + 1] << 16 | d[i + 2] << 8 | d[i + 3];
            if (i + 4 + length > d.Length) throw new InvalidDataException("This FLAC file is damaged.");
            if (type is 0 or 3 or 5) kept.Add((i, length, type));
            i += 4 + length;
        }
        if (kept.Count == 0 || kept[0].Type != 0) throw new InvalidDataException("This FLAC file has no stream information.");
        using var ms = new MemoryStream(d.Length);
        ms.Write(d, 0, 4);
        for (int k = 0; k < kept.Count; k++)
        {
            var (offset, length, type) = kept[k];
            ms.WriteByte((byte)((k == kept.Count - 1 ? 0x80 : 0) | type));
            ms.Write(d, offset + 1, 3 + length);
        }
        ms.Write(d, i, d.Length - i); // audio frames
        return ms.ToArray();
    }
}
