using System.Buffers.Binary;

namespace Flipr.Conversion.Media;

/// <summary>Peak measurement and gain for 16-bit PCM WAV data (as written by Media Foundation).</summary>
public static class WavPcm
{
    /// <summary>The byte range of the "data" chunk, after checking the file is 16-bit PCM.</summary>
    public static (int Offset, int Length) DataChunk(byte[] wav)
    {
        if (wav.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(wav) != 0x52494646 /* RIFF */)
            throw new InvalidDataException("Not a WAV file.");
        int i = 12;
        bool pcm16 = false;
        while (i + 8 <= wav.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(wav, i, 4);
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(i + 4));
            int body = i + 8;
            if (id == "fmt ")
            {
                ushort format = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body));
                ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 14));
                pcm16 = (format == 1 || format == 0xFFFE) && bits == 16;
            }
            else if (id == "data")
            {
                if (!pcm16) throw new InvalidDataException("Expected 16-bit PCM audio.");
                return (body, Math.Min(size, wav.Length - body) & ~1);
            }
            i = body + size + (size & 1); // chunks are word-aligned
        }
        throw new InvalidDataException("The WAV file has no audio data.");
    }

    /// <summary>Gain that brings the loudest sample to <paramref name="peak"/> (0–1). Silence → 1.</summary>
    public static double NormalizeGain(byte[] wav, double peak)
    {
        var (offset, length) = DataChunk(wav);
        int max = 0;
        for (int i = offset; i < offset + length; i += 2)
            max = Math.Max(max, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(i))));
        return max == 0 ? 1 : peak * 32767.0 / max;
    }

    public static void ApplyGain(byte[] wav, double gain)
    {
        var (offset, length) = DataChunk(wav);
        for (int i = offset; i < offset + length; i += 2)
        {
            int v = (int)Math.Round(BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(i)) * gain);
            BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(i), (short)Math.Clamp(v, short.MinValue, short.MaxValue));
        }
    }
}
