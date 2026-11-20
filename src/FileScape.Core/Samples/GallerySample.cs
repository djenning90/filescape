using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using FileScape.Core.Planets;

namespace FileScape.Core.Samples;

/// <summary>
/// The picture gallery sample (PLANET-96, AD-78): a small <b>mixed</b> folder of real files FileScape writes for itself,
/// so the gallery can be played without anyone's own pictures. It holds original pictures painted by the Starter Sky's
/// painters (<see cref="Pictures"/>) at several sizes and shapes (standard, wide, tall, a large print that must be
/// shrunk, a BMP), files that are not pictures (a README, notes, a table, a sound, an archive), a subfolder of sketches,
/// and the <see cref="GalleryRules.MarkerName"/> marker that makes the folder a gallery. With <c>stress</c> it also writes
/// the cases a gallery must refuse without stalling: a damaged picture, a picture whose header claims more pixels than the
/// app decodes (≈ 63 Mpx; a few hundred kilobytes on disk) and a picture over the preview's 24 MiB read limit.
/// Nothing is downloaded or copied from anywhere; every byte is generated, the same on every run.
/// Preserve: the marker's first line (<see cref="MarkerMagic"/>) is how the writer recognises its own folder; it refuses to
/// write into a folder of that name that it did not make.
/// </summary>
public static class GallerySample
{
    /// <summary>The gallery folder's name under the parent directory.</summary>
    public const string RootName = "Picture Gallery";

    /// <summary>The content version in the marker; bump it when the generated content changes.</summary>
    public const int Version = 1;

    private const string MarkerMagic = "FileScape picture gallery";

    /// <summary>The pictures the viewer can show (in name order), their pixel sizes as written.</summary>
    public static readonly IReadOnlyList<(string Name, int Width, int Height)> Viewable =
    [
        ("aurora-print-large.png", 2560, 1920),
        ("dawn-over-the-low-hills.png", 640, 480),
        ("harbour-of-lanterns-wide.png", 1920, 480),
        ("lantern-tower-tall.png", 640, 960),
        ("midnight-sunflowers.png", 640, 480),
        ("mist-in-the-blue-valley.png", 640, 480),
        ("postage-stamp.bmp", 160, 120),
        ("two-moons-over-the-orchard.png", 640, 480),
    ];

    /// <summary>The stress cases (<c>stress: true</c>): a damaged picture, a picture over the decoded-pixel budget, a picture over the read limit.</summary>
    public const string DamagedName = "damaged-photo.png", HugeDimensionsName = "giant-blank-scan.png", OversizeName = "poster-over-the-size-limit.png";

    /// <summary>The giant scan's size, pixels: 9,000 × 7,000 = 63 Mpx, over the app's 60 Mpx decode budget.</summary>
    public const int GiantWidth = 9000, GiantHeight = 7000;

    /// <summary>The oversized poster's side, pixels: stored uncompressed, 2,900² × 3 bytes ≈ 25.2 MB, over the 24 MiB read limit.</summary>
    public const int OversizeSide = 2900;

    /// <summary>
    /// Writes the gallery under <c>parentDirectory/Picture Gallery</c> (or refreshes our own folder of an older version)
    /// and returns its full path. An existing folder of that name without our marker is refused (nothing is written).
    /// </summary>
    public static string Create(string parentDirectory, bool stress = false)
    {
        var root = Path.Combine(Path.GetFullPath(parentDirectory), RootName);
        if (Directory.Exists(root))
        {
            var version = ReadVersion(root);
            if (version is null) throw new InvalidOperationException($"Refusing to write into {root}: it exists and is not FileScape's gallery sample.");
            if (version == Version && (!stress || File.Exists(Path.Combine(root, OversizeName)))) return root;
        }

        Write(root, stress);
        return root;
    }

    private static int? ReadVersion(string root)
    {
        var marker = Path.Combine(root, GalleryRules.MarkerName);
        if (!File.Exists(marker) || new FileInfo(marker).Length > 4096) return null;
        var text = File.ReadAllText(marker, Encoding.UTF8);
        if (!text.StartsWith(MarkerMagic, StringComparison.Ordinal)) return null;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("version=", StringComparison.Ordinal) && int.TryParse(line[8..].Trim(), out var v)) return v;
        }

        return 0;
    }

    private static void Write(string root, bool stress)
    {
        Directory.CreateDirectory(root);
        var garden = Pictures.Garden;
        Painting(root, "dawn-over-the-low-hills.png", 2, Find(garden, "dawn-over-the-low-hills.png"));
        Painting(root, "midnight-sunflowers.png", 2, Find(garden, "midnight-sunflowers.png"));
        Painting(root, "mist-in-the-blue-valley.png", 2, Find(garden, "mist-in-the-blue-valley.png"));
        Painting(root, "two-moons-over-the-orchard.png", 2, Find(garden, "two-moons-over-the-orchard.png"));
        Painting(root, "aurora-print-large.png", 8, Find(garden, "aurora-above-the-pines.png"));
        // Wide: three scenes side by side; tall: two stacked (each painted at 320 × 240 and scaled ×2).
        var wide = Join([Paint(Find(garden, "lanterns-drifting-at-dusk.png")), Paint(Find(garden, "sunset-on-the-mirror-pond.png")), Paint(Find(garden, "moonrise-at-the-lantern-lake.png"))], across: true);
        Bytes(root, "harbour-of-lanterns-wide.png", Png.Encode(wide.W * 2, wide.H * 2, Scale(wide.Rgb, wide.W, wide.H, 2)));
        var tall = Join([Paint(Find(garden, "starfield-over-the-quiet-moon.png")), Paint(Find(garden, "the-garden-after-rain.png"))], across: false);
        Bytes(root, "lantern-tower-tall.png", Png.Encode(tall.W * 2, tall.H * 2, Scale(tall.Rgb, tall.W, tall.H, 2)));
        var stamp = Paint(Find(Pictures.Pressed, "pressed-violets.png"));
        Bytes(root, "postage-stamp.bmp", Bmp(Pictures.WidthPixels / 2, Pictures.HeightPixels / 2, Half(stamp.Rgb, stamp.W, stamp.H)));

        Text(root, "README.md", Readme);
        Text(root, "captions.txt", "dawn-over-the-low-hills — first light over the hills\nharbour-of-lanterns-wide — three evenings joined into one long view\nlantern-tower-tall — night over day, stacked like a tower\naurora-print-large — a large print: the viewer shrinks it to fit and lets you zoom in\n");
        Text(root, "palette.csv", "name,hex\ndusk violet,#5b3f8c\nlantern gold,#ffc861\nmeadow green,#7dbb6a\nlake blue,#4f86c6\n");
        Bytes(root, "gallery-chime.wav", Chime());
        Bytes(root, "old-prints.zip", [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]); // an empty zip archive
        Painting(Path.Combine(root, "Sketches"), "pressed-cosmos.png", 1, Find(Pictures.Pressed, "pressed-cosmos.png"));
        Painting(Path.Combine(root, "Sketches"), "pressed-buttercups.png", 1, Find(Pictures.Pressed, "pressed-buttercups.png"));

        if (stress)
        {
            // A PNG signature and the start of a header, then noise: no decoder accepts it.
            var damaged = new byte[4096];
            var rng = new SampleRandom(DamagedName);
            for (var i = 0; i < damaged.Length; i++) damaged[i] = (byte)rng.NextUInt64();
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(damaged, 0);
            Bytes(root, DamagedName, damaged);
            WriteFlatPng(Path.Combine(root, HugeDimensionsName), GiantWidth, GiantHeight, CompressionLevel.Fastest);
            WriteNoisePng(Path.Combine(root, OversizeName), OversizeSide, OversizeSide);
        }

        Text(root, GalleryRules.MarkerName, $"{MarkerMagic} sample. A folder holding a file with this name is a gallery in FileScape: its pictures show on their easels as you walk (PLANET-96). Safe to delete.\nversion={Version}\n");
    }

    private const string Readme = """
        # Picture Gallery

        A sample folder FileScape writes for itself: original pictures painted by the app, mixed with files that are not
        pictures, so you can try the gallery without your own photos.

        ## How to look at the pictures

        - Walk among the easels: the pictures appear on them as you come near.
        - Walk up to one and press **E** to look at it.
        - Press **R** (or ⤢) to view it large. **← →** step through the pictures here, the **wheel** zooms, drag to look
          around a zoomed picture, **0** fits it again, and **Esc** brings you back to where you stood.

        The file `.filescape-gallery` is what makes this folder a gallery. Put an empty file with that name in a folder of
        your own and FileScape shows its pictures as you walk; without it, a picture is read only when you press E.
        """;

    // ---------------------------------------------------------------- painting helpers

    /// <summary>An 8-bit RGB picture in memory, rows top to bottom.</summary>
    private sealed record Raster(byte[] Rgb, int W, int H);

    private static Pictures.Painting Find(Pictures.Painting[] set, string name) => set.First(p => p.FileName == name);

    private static Raster Paint(Pictures.Painting p)
    {
        var c = new Canvas(Pictures.WidthPixels, Pictures.HeightPixels);
        var rng = new SampleRandom(p.FileName);
        p.Paint(c, ref rng);
        return new Raster(c.ToRgb(), Pictures.WidthPixels, Pictures.HeightPixels);
    }

    private static void Painting(string root, string name, int scale, Pictures.Painting p)
    {
        var r = Paint(p);
        Bytes(root, name, Png.Encode(r.W * scale, r.H * scale, scale == 1 ? r.Rgb : Scale(r.Rgb, r.W, r.H, scale)));
    }

    private static Raster Join(Raster[] parts, bool across)
    {
        var w = across ? parts.Sum(p => p.W) : parts[0].W;
        var h = across ? parts[0].H : parts.Sum(p => p.H);
        var o = new byte[w * h * 3];
        var x0 = 0;
        var y0 = 0;
        foreach (var p in parts)
        {
            for (var y = 0; y < p.H; y++) Array.Copy(p.Rgb, y * p.W * 3, o, ((y0 + y) * w + x0) * 3, p.W * 3);
            if (across) x0 += p.W;
            else y0 += p.H;
        }

        return new Raster(o, w, h);
    }

    /// <summary>Bilinear enlargement by an integer factor (a smooth print, not blocky pixels).</summary>
    private static byte[] Scale(byte[] rgb, int w, int h, int k)
    {
        int W = w * k, H = h * k;
        var o = new byte[W * H * 3];
        for (var y = 0; y < H; y++)
        {
            var fy = Math.Clamp((y + 0.5f) / k - 0.5f, 0, h - 1);
            var y0 = (int)fy;
            var y1 = Math.Min(y0 + 1, h - 1);
            var ty = fy - y0;
            for (var x = 0; x < W; x++)
            {
                var fx = Math.Clamp((x + 0.5f) / k - 0.5f, 0, w - 1);
                var x0 = (int)fx;
                var x1 = Math.Min(x0 + 1, w - 1);
                var tx = fx - x0;
                for (var c = 0; c < 3; c++)
                {
                    var a = rgb[(y0 * w + x0) * 3 + c] * (1 - tx) + rgb[(y0 * w + x1) * 3 + c] * tx;
                    var b = rgb[(y1 * w + x0) * 3 + c] * (1 - tx) + rgb[(y1 * w + x1) * 3 + c] * tx;
                    o[(y * W + x) * 3 + c] = (byte)Math.Clamp(MathF.Round(a * (1 - ty) + b * ty), 0, 255);
                }
            }
        }

        return o;
    }

    /// <summary>Halves a picture by averaging 2 × 2 blocks.</summary>
    private static byte[] Half(byte[] rgb, int w, int h)
    {
        int W = w / 2, H = h / 2;
        var o = new byte[W * H * 3];
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        for (var c = 0; c < 3; c++)
        {
            var s = rgb[(2 * y * w + 2 * x) * 3 + c] + rgb[(2 * y * w + 2 * x + 1) * 3 + c] + rgb[((2 * y + 1) * w + 2 * x) * 3 + c] + rgb[((2 * y + 1) * w + 2 * x + 1) * 3 + c];
            o[(y * W + x) * 3 + c] = (byte)(s / 4);
        }

        return o;
    }

    /// <summary>A 24-bit bottom-up BMP (BITMAPINFOHEADER), rows padded to 4 bytes.</summary>
    private static byte[] Bmp(int w, int h, byte[] rgb)
    {
        var row = (w * 3 + 3) & ~3;
        var o = new byte[54 + row * h];
        o[0] = (byte)'B';
        o[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(2), o.Length);
        BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(18), w);
        BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(22), h);
        BinaryPrimitives.WriteInt16LittleEndian(o.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(o.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(34), row * h);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var s = (y * w + x) * 3;
            var d = 54 + (h - 1 - y) * row + x * 3;
            o[d] = rgb[s + 2];
            o[d + 1] = rgb[s + 1];
            o[d + 2] = rgb[s];
        }

        return o;
    }

    /// <summary>A soft two-note chime, 0.6 s of 16-bit mono at 22,050 Hz (≈ 26 KB): a sound file the preview can play.</summary>
    private static byte[] Chime()
    {
        const int rate = 22050;
        const float seconds = 0.6f;
        var n = (int)(rate * seconds);
        var pcm = new byte[n * 2];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)rate;
            var second = t > 0.18f ? MathF.Sin(MathF.Tau * 1318.5f * t) * MathF.Exp(-(t - 0.18f) * 7f) : 0f;
            var v = 0.3f * (MathF.Sin(MathF.Tau * 987.8f * t) * MathF.Exp(-t * 7f) + second) * MathF.Min(1f, t * 200f);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(Math.Clamp(v, -1f, 1f) * 32000));
        }

        var o = new byte[44 + pcm.Length];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(o, 0);
        BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(4), 36 + pcm.Length);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(o, 8);
        BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(o.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(o.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(24), rate);
        BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(28), rate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(o.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(o.AsSpan(34), 16);
        Encoding.ASCII.GetBytes("data").CopyTo(o, 36);
        BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(40), pcm.Length);
        pcm.CopyTo(o, 44);
        return o;
    }

    /// <summary>A single-colour RGB PNG of any size, streamed row by row (memory stays one row).</summary>
    private static void WriteFlatPng(string path, int w, int h, CompressionLevel level)
    {
        var row = new byte[1 + w * 3];
        for (var x = 0; x < w; x++)
        {
            row[1 + x * 3] = 0xEE;
            row[2 + x * 3] = 0xEA;
            row[3 + x * 3] = 0xE0;
        }

        WriteStreamedPng(path, w, h, level, (_, r) => row.CopyTo(r, 0));
    }

    /// <summary>A noise PNG stored without compression, so its size on disk is about w × h × 3 bytes.</summary>
    private static void WriteNoisePng(string path, int w, int h)
    {
        var rng = new SampleRandom(path.Length > 0 ? Path.GetFileName(path) : "noise");
        WriteStreamedPng(path, w, h, CompressionLevel.NoCompression, (_, r) =>
        {
            r[0] = 0;
            for (var i = 1; i < r.Length; i++) r[i] = (byte)rng.NextUInt64();
        });
    }

    private delegate void RowWriter(int y, byte[] row);

    private static void WriteStreamedPng(string path, int w, int h, CompressionLevel level, RowWriter rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, level, leaveOpen: true))
        {
            var row = new byte[1 + w * 3];
            for (var y = 0; y < h; y++)
            {
                rows(y, row);
                z.Write(row);
            }
        }

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8;
        ihdr[9] = 2;
        using var f = File.Create(path);
        f.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Png.Chunk(f, "IHDR", ihdr);
        Png.Chunk(f, "IDAT", ms.ToArray());
        Png.Chunk(f, "IEND", []);
    }

    private static void Text(string root, string relative, string content) =>
        Bytes(root, relative, new UTF8Encoding(false).GetBytes(content.Replace("\r\n", "\n", StringComparison.Ordinal)));

    private static void Bytes(string root, string relative, byte[] content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }
}
