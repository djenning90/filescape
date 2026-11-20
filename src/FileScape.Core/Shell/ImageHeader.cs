using System.Buffers.Binary;

namespace FileScape.Core.Shell;

/// <summary>
/// A picture's size in pixels read from its header bytes, before anything is decoded (PLANET-96, AD-78). Decoding a
/// small file can still need a huge buffer (a 10,000 × 8,000 PNG of one colour is a few kilobytes on disk and 320 MB
/// decoded), so the app asks this first and refuses a picture over its pixel budget without decoding it. Knows PNG
/// (IHDR), JPEG (the first SOF marker), BMP (BITMAPINFOHEADER and later; the OS/2 core header) and WebP (VP8, VP8L,
/// VP8X). Pure and engine-free: it only looks at the bytes it is given.
/// Preserve: never throws; an unknown format, a truncated or a contradictory header gives null (the caller then treats
/// the file as having no preview, as for a decoding error).
/// </summary>
public static class ImageHeader
{
    /// <summary>The bytes worth reading for <see cref="TryReadSize"/>: enough for PNG, BMP and WebP, and for a JPEG whose
    /// frame header follows typical EXIF and ICC segments (a JPEG with a larger preamble gives null and is decoded as before
    /// only if the caller allows that).</summary>
    public const int ProbeBytes = 256 * 1024;

    /// <summary>The picture's width and height in pixels, or null when the header is not one this knows or is damaged.</summary>
    public static (int Width, int Height)? TryReadSize(ReadOnlySpan<byte> b)
    {
        try
        {
            return Png(b) ?? Jpeg(b) ?? Bmp(b) ?? WebP(b);
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static (int, int)? Valid(long w, long h) => w > 0 && h > 0 && w <= int.MaxValue && h <= int.MaxValue ? ((int)w, (int)h) : null;

    private static (int, int)? Png(ReadOnlySpan<byte> b)
    {
        ReadOnlySpan<byte> sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (b.Length < 24 || !b[..8].SequenceEqual(sig) || b[12] != 'I' || b[13] != 'H' || b[14] != 'D' || b[15] != 'R') return null;
        return Valid(BinaryPrimitives.ReadUInt32BigEndian(b[16..]), BinaryPrimitives.ReadUInt32BigEndian(b[20..]));
    }

    private static (int, int)? Jpeg(ReadOnlySpan<byte> b)
    {
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return null;
        var i = 2;
        while (i + 4 <= b.Length)
        {
            if (b[i] != 0xFF) return null;
            var marker = b[i + 1];
            if (marker == 0xFF) { i++; continue; } // fill bytes
            if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7) { i += 2; continue; } // markers without a length
            var length = BinaryPrimitives.ReadUInt16BigEndian(b[(i + 2)..]);
            if (length < 2) return null;
            // SOF0..SOF15 except DHT (C4), JPG (C8) and DAC (CC) carry the frame size: height then width.
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                if (i + 9 > b.Length) return null;
                return Valid(BinaryPrimitives.ReadUInt16BigEndian(b[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(b[(i + 5)..]));
            }

            if (marker == 0xDA) return null; // the scan started without a frame header
            i += 2 + length;
        }

        return null;
    }

    private static (int, int)? Bmp(ReadOnlySpan<byte> b)
    {
        if (b.Length < 26 || b[0] != 'B' || b[1] != 'M') return null;
        var header = BinaryPrimitives.ReadUInt32LittleEndian(b[14..]);
        if (header == 12) return Valid(BinaryPrimitives.ReadUInt16LittleEndian(b[18..]), BinaryPrimitives.ReadUInt16LittleEndian(b[20..]));
        if (header < 40) return null;
        // A negative height is a top-down bitmap: the size is its magnitude.
        return Valid(Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(b[18..])), Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(b[22..])));
    }

    private static (int, int)? WebP(ReadOnlySpan<byte> b)
    {
        if (b.Length < 30 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'E' || b[10] != 'B' || b[11] != 'P') return null;
        var chunk = System.Text.Encoding.ASCII.GetString(b.Slice(12, 4));
        var d = b[20..];
        switch (chunk)
        {
            case "VP8 ":
                // A key frame: 3 bytes of frame tag, the start code 9D 01 2A, then 14-bit width and height.
                if (d[3] != 0x9D || d[4] != 0x01 || d[5] != 0x2A) return null;
                return Valid(BinaryPrimitives.ReadUInt16LittleEndian(d[6..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(d[8..]) & 0x3FFF);
            case "VP8L":
                if (d[0] != 0x2F) return null;
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(d[1..]);
                return Valid((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
            case "VP8X":
                return Valid((d[4] | d[5] << 8 | d[6] << 16) + 1L, (d[7] | d[8] << 8 | d[9] << 16) + 1L);
            default:
                return null;
        }
    }
}
