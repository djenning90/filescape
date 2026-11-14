using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace FileScape.Core.Samples;

/// <summary>
/// "Starter Sky": a small, original, curated folder of real files (pictures, music-box tunes, letters, a tiny C#
/// project, an archive of logs, many constellation folders and an empty moon) written for the first impression of the
/// planetary world. Every file is real content, so open and preview work; everything is generated deterministically
/// from code (no assets, no network).
/// <para>Safety: the folder is recognised by a marker file (<see cref="MarkerName"/>) holding <see cref="Version"/>.
/// <see cref="Create"/> never deletes anything, and never writes into an existing "Starter Sky" that lacks the marker.</para>
/// </summary>
public static class SampleSky
{
    /// <summary>The sample folder's name under the parent directory.</summary>
    public const string RootName = "Starter Sky";

    /// <summary>The marker file inside the sample root; its text names the sample and its <see cref="Version"/>.</summary>
    public const string MarkerName = ".filescape-sample";

    /// <summary>The content version written into the marker. Bump it when the generated content changes.</summary>
    public const int Version = 3; // 2 (2026-11-14): Lantern Workshop is a repository (.git); the Long Way Down. 3: waymark notes name the right bands

    private const string MarkerMagic = "FileScape sample folder";
    private const string JunctionName = "Shortcut to Garden";

    /// <summary>
    /// Writes the sample under <c>parentDirectory/Starter Sky</c> and returns that root path (full path).
    /// <list type="bullet">
    /// <item>No root yet: the tree is written into a hidden staging folder beside it, marked, then renamed into place,
    /// so an interrupted run never leaves a half-written "Starter Sky".</item>
    /// <item>Root with a marker of this <see cref="Version"/>: returns at once without writing anything.</item>
    /// <item>Root with a marker of another version: our own folder, refreshed in place (sample files rewritten,
    /// nothing deleted, other files left alone) and re-marked.</item>
    /// <item>Root (file or folder) without the marker: throws <see cref="InvalidOperationException"/> and changes nothing.</item>
    /// </list>
    /// On Windows it also tries to add a directory junction "Shortcut to Garden" → "Garden Photos"; a failure is ignored.
    /// </summary>
    public static string Create(string parentDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(parentDirectory);
        var parent = Path.GetFullPath(parentDirectory);
        var root = Path.Combine(parent, RootName);

        if (File.Exists(root))
        {
            throw new InvalidOperationException($"Refusing to write the sample: {root} is an existing file.");
        }

        if (Directory.Exists(root))
        {
            var existing = ReadMarkerVersion(root);
            if (existing is null)
            {
                throw new InvalidOperationException($"Refusing to write into {root}: it exists and is not a FileScape sample (no {MarkerName}).");
            }

            if (existing == Version)
            {
                return root;
            }

            WriteTree(root);
            WriteMarker(root);
            TryLinkGarden(root);
            return root;
        }

        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, "." + RootName + ".partial-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(staging);
            WriteTree(staging);
            WriteMarker(staging);
            Directory.Move(staging, root);
        }
        catch (IOException) when (Directory.Exists(root) && ReadMarkerVersion(root) == Version)
        {
            // Another writer finished first: theirs is identical.
            TryDeleteStaging(staging);
            return root;
        }
        catch
        {
            TryDeleteStaging(staging);
            throw;
        }

        TryLinkGarden(root);
        return root;
    }

    /// <summary>True when <paramref name="path"/> is a directory holding a sample marker (of any version).</summary>
    public static bool IsSample(string path)
    {
        try
        {
            return !string.IsNullOrEmpty(path) && ReadMarkerVersion(path) is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The marker's version, 0 for a marker without a readable version, null when there is no marker.</summary>
    private static int? ReadMarkerVersion(string root)
    {
        var marker = Path.Combine(root, MarkerName);
        if (!File.Exists(marker))
        {
            return null;
        }

        var info = new FileInfo(marker);
        if (info.Length > 4096)
        {
            return null;
        }

        var text = File.ReadAllText(marker, Encoding.UTF8);
        if (!text.StartsWith(MarkerMagic, StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("version=", StringComparison.Ordinal) &&
                int.TryParse(t["version=".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }
        }

        return 0;
    }

    private static void WriteMarker(string root) =>
        File.WriteAllText(Path.Combine(root, MarkerName),
            $"{MarkerMagic} \"{RootName}\", written by FileScape as a friendly first sky. Safe to delete.\nversion={Version.ToString(CultureInfo.InvariantCulture)}\n",
            new UTF8Encoding(false));

    private static void TryDeleteStaging(string staging)
    {
        try
        {
            // Only our own freshly created staging folder; it never holds links (the junction is made after the rename).
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryLinkGarden(string root)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var link = Path.Combine(root, JunctionName);
        var target = Path.Combine(root, "Garden Photos");
        if (Directory.Exists(link) || File.Exists(link) || !Directory.Exists(target))
        {
            return;
        }

        try
        {
            // mklink /J needs no elevation or Developer Mode (as FileScape.Fixtures does).
            var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add("mklink");
            psi.ArgumentList.Add("/J");
            psi.ArgumentList.Add(link);
            psi.ArgumentList.Add(target);
            using var p = Process.Start(psi);
            if (p is not null && !p.WaitForExit(10_000))
            {
                p.Kill();
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // Skipped quietly: the shortcut is a nicety.
        }
    }

    // ---------------------------------------------------------------- the tree

    private static void WriteTree(string root)
    {
        Text(root, "README.md", SampleTexts.RootReadme);
        Text(root, "notes.txt", SampleTexts.RootNotes);
        Bytes(root, "sky-map.png", Pictures.Render(Pictures.SkyMap));

        foreach (var p in Pictures.Garden)
        {
            Bytes(root, Path.Combine("Garden Photos", p.FileName), Pictures.Render(p));
        }

        foreach (var p in Pictures.Pressed)
        {
            Bytes(root, Path.Combine("Garden Photos", "Pressed Flowers", p.FileName), Pictures.Render(p));
        }

        foreach (var (rel, text) in SampleTexts.Workshop)
        {
            Text(root, Path.Combine("Lantern Workshop", rel), text);
        }

        foreach (var tune in MusicBox.Tunes)
        {
            Bytes(root, Path.Combine("Music Box", tune.FileName), MusicBox.Render(tune));
        }

        Text(root, Path.Combine("Music Box", "playlist.m3u"), MusicBox.Playlist());
        Text(root, Path.Combine("Music Box", "liner-notes.txt"), SampleTexts.LinerNotes);

        foreach (var (name, text) in SampleTexts.Letters)
        {
            Text(root, Path.Combine("Letters", name), text);
        }

        foreach (var (name, text) in SampleTexts.Drafts)
        {
            Text(root, Path.Combine("Letters", "Drafts", name), text);
        }

        foreach (var (name, text) in StarArchive.Files())
        {
            Text(root, Path.Combine("Star Archive", name), text);
        }

        foreach (var (name, text) in StarArchive.YearFiles())
        {
            Text(root, Path.Combine("Star Archive", "2025", name), text);
        }

        for (var i = 0; i < SampleTexts.Constellations.Length; i++)
        {
            foreach (var (name, text) in SampleTexts.ConstellationFiles(i))
            {
                Text(root, Path.Combine("Constellations", SampleTexts.Constellations[i].Name, name), text);
            }
        }

        Directory.CreateDirectory(Path.Combine(root, "Quiet Moon"));

        // Lantern Workshop is a repository (PLANET-27): an empty git repository's own files, so git itself would accept it.
        // Written only when there is no .git yet: a refresh never overwrites a repository someone has started using.
        var git = Path.Combine(root, "Lantern Workshop", ".git");
        if (!Directory.Exists(git) && !File.Exists(git)) WriteGitSkeleton(root);

        // The Long Way Down: six levels, one per step of the journey, so every depth band can be visited (AD-48).
        var path = "Long Way Down";
        foreach (var (name, note) in SampleTexts.LongWayDown)
        {
            path = Path.Combine(path, name);
            Text(root, Path.Combine(path, "waymark.txt"), note);
        }
    }

    private static void WriteGitSkeleton(string root)
    {
        Text(root, Path.Combine("Lantern Workshop", ".git", "HEAD"), "ref: refs/heads/main\n");
        Text(root, Path.Combine("Lantern Workshop", ".git", "config"), "[core]\n\trepositoryformatversion = 0\n\tfilemode = false\n\tbare = false\n");
        Text(root, Path.Combine("Lantern Workshop", ".git", "description"), "Lantern, the Starter Sky's tiny sample project.\n");
        Directory.CreateDirectory(Path.Combine(root, "Lantern Workshop", ".git", "objects"));
        Directory.CreateDirectory(Path.Combine(root, "Lantern Workshop", ".git", "refs", "heads"));
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

/// <summary>A small deterministic generator (SplitMix64), so content never depends on the runtime's Random.</summary>
internal struct SampleRandom
{
    private ulong _state;

    public SampleRandom(string seed)
    {
        // FNV-1a over the UTF-16 code units: stable across runs and platforms (unlike string.GetHashCode).
        var h = 14695981039346656037UL;
        foreach (var c in seed)
        {
            h = (h ^ c) * 1099511628211UL;
        }

        _state = h;
    }

    public ulong NextUInt64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public float Range(float min, float max) => min + (float)NextDouble() * (max - min);

    /// <summary>Uniform integer in [min, maxExclusive).</summary>
    public int Next(int min, int maxExclusive) => min + (int)(NextUInt64() % (ulong)(maxExclusive - min));

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(0, items.Count)];
}

// ==================================================================== pictures

/// <summary>A float RGB canvas (0..1 per channel) in pixel space: x right, y down, pixel centres at +0.5.</summary>
internal sealed class Canvas
{
    public readonly int WidthPixels;
    public readonly int HeightPixels;
    public readonly Vector3[] Px;

    public Canvas(int widthPixels, int heightPixels)
    {
        WidthPixels = widthPixels;
        HeightPixels = heightPixels;
        Px = new Vector3[widthPixels * heightPixels];
    }

    public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

    public static float Smooth(float edge0, float edge1, float v)
    {
        var t = Clamp01((v - edge0) / (edge1 - edge0));
        return t * t * (3 - 2 * t);
    }

    public void Blend(int x, int y, Vector3 c, float alpha)
    {
        if ((uint)x >= (uint)WidthPixels || (uint)y >= (uint)HeightPixels || alpha <= 0) return;
        ref var p = ref Px[y * WidthPixels + x];
        p = Vector3.Lerp(p, c, alpha > 1 ? 1 : alpha);
    }

    public void Add(int x, int y, Vector3 c)
    {
        if ((uint)x >= (uint)WidthPixels || (uint)y >= (uint)HeightPixels) return;
        Px[y * WidthPixels + x] += c;
    }

    public static Vector3 Sample((float At, Vector3 Color)[] stops, float t)
    {
        if (t <= stops[0].At) return stops[0].Color;
        for (var i = 1; i < stops.Length; i++)
        {
            if (t <= stops[i].At)
            {
                var u = (t - stops[i - 1].At) / (stops[i].At - stops[i - 1].At);
                return Vector3.Lerp(stops[i - 1].Color, stops[i].Color, Smooth(0, 1, u));
            }
        }

        return stops[^1].Color;
    }

    /// <summary>A vertical gradient over the whole canvas; stop positions are fractions of the height.</summary>
    public void Gradient(params (float At, Vector3 Color)[] stops)
    {
        for (var y = 0; y < HeightPixels; y++)
        {
            var c = Sample(stops, (y + 0.5f) / HeightPixels);
            for (var x = 0; x < WidthPixels; x++) Px[y * WidthPixels + x] = c;
        }
    }

    /// <summary>Blends <paramref name="color"/> over the pixels of a box by a per-pixel coverage (0..1).</summary>
    public void Shape(float minX, float minY, float maxX, float maxY, Func<float, float, float> coverage, Vector3 color, float alpha = 1)
    {
        var x0 = Math.Max(0, (int)MathF.Floor(minX));
        var y0 = Math.Max(0, (int)MathF.Floor(minY));
        var x1 = Math.Min(WidthPixels - 1, (int)MathF.Ceiling(maxX));
        var y1 = Math.Min(HeightPixels - 1, (int)MathF.Ceiling(maxY));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var cov = coverage(x + 0.5f, y + 0.5f);
                if (cov > 0) Blend(x, y, color, cov * alpha);
            }
        }
    }

    public void Disc(float cx, float cy, float radiusPixels, Vector3 color, float alpha = 1) =>
        Shape(cx - radiusPixels - 1, cy - radiusPixels - 1, cx + radiusPixels + 1, cy + radiusPixels + 1,
            (x, y) => Clamp01(radiusPixels - MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) + 0.5f), color, alpha);

    /// <summary>A lit crescent: the disc minus a shadow disc offset by (<paramref name="shadowDxPixels"/>, <paramref name="shadowDyPixels"/>).</summary>
    public void Crescent(float cx, float cy, float radiusPixels, float shadowDxPixels, float shadowDyPixels, Vector3 color) =>
        Shape(cx - radiusPixels - 1, cy - radiusPixels - 1, cx + radiusPixels + 1, cy + radiusPixels + 1, (x, y) =>
        {
            var lit = Clamp01(radiusPixels - MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) + 0.5f);
            var sx = cx + shadowDxPixels;
            var sy = cy + shadowDyPixels;
            var shadow = Clamp01(radiusPixels - MathF.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy)) + 0.5f);
            return lit * (1 - shadow);
        }, color);

    public void Ellipse(float cx, float cy, float rxPixels, float ryPixels, float angleDegrees, Vector3 color, float alpha = 1)
    {
        var a = angleDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(a);
        var sin = MathF.Sin(a);
        var r = Math.Max(rxPixels, ryPixels) + 1;
        var minR = Math.Min(rxPixels, ryPixels);
        Shape(cx - r, cy - r, cx + r, cy + r, (x, y) =>
        {
            var dx = x - cx;
            var dy = y - cy;
            var u = (dx * cos + dy * sin) / rxPixels;
            var v = (-dx * sin + dy * cos) / ryPixels;
            return Clamp01((1 - MathF.Sqrt(u * u + v * v)) * minR + 0.5f);
        }, color, alpha);
    }

    public void Line(float ax, float ay, float bx, float by, float widthPixels, Vector3 color, float alpha = 1)
    {
        var half = widthPixels / 2;
        var abx = bx - ax;
        var aby = by - ay;
        var len2 = Math.Max(1e-6f, abx * abx + aby * aby);
        Shape(Math.Min(ax, bx) - half - 1, Math.Min(ay, by) - half - 1, Math.Max(ax, bx) + half + 1, Math.Max(ay, by) + half + 1, (x, y) =>
        {
            var t = Clamp01(((x - ax) * abx + (y - ay) * aby) / len2);
            var dx = x - (ax + t * abx);
            var dy = y - (ay + t * aby);
            return Clamp01(half - MathF.Sqrt(dx * dx + dy * dy) + 0.5f);
        }, color, alpha);
    }

    public void Triangle(float ax, float ay, float bx, float by, float cx, float cy, Vector3 color, float alpha = 1)
    {
        var sign = MathF.Sign((bx - ax) * (cy - ay) - (by - ay) * (cx - ax));
        static float Edge(float px, float py, float qx, float qy, float x, float y, float s)
        {
            var ex = qx - px;
            var ey = qy - py;
            return s * (ex * (y - py) - ey * (x - px)) / MathF.Sqrt(ex * ex + ey * ey);
        }

        Shape(Math.Min(ax, Math.Min(bx, cx)) - 1, Math.Min(ay, Math.Min(by, cy)) - 1, Math.Max(ax, Math.Max(bx, cx)) + 1, Math.Max(ay, Math.Max(by, cy)) + 1,
            (x, y) => Clamp01(Math.Min(Edge(ax, ay, bx, by, x, y, sign), Math.Min(Edge(bx, by, cx, cy, x, y, sign), Edge(cx, cy, ax, ay, x, y, sign))) + 0.5f),
            color, alpha);
    }

    /// <summary>Adds a soft light: <paramref name="color"/> × strength × e^(−(d/r)²).</summary>
    public void Glow(float cx, float cy, float radiusPixels, Vector3 color, float strength)
    {
        var reach = radiusPixels * 2.6f;
        var x0 = Math.Max(0, (int)(cx - reach));
        var x1 = Math.Min(WidthPixels - 1, (int)(cx + reach));
        var y0 = Math.Max(0, (int)(cy - reach));
        var y1 = Math.Min(HeightPixels - 1, (int)(cy + reach));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var dx = (x + 0.5f - cx) / radiusPixels;
                var dy = (y + 0.5f - cy) / radiusPixels;
                Add(x, y, color * (strength * MathF.Exp(-(dx * dx + dy * dy))));
            }
        }
    }

    /// <summary>Fills below a ridge line top(x) with a vertical blend from <paramref name="top"/> to <paramref name="bottom"/>.</summary>
    public void Ridge(Func<float, float> topY, Vector3 top, Vector3 bottom, float alpha = 1)
    {
        for (var x = 0; x < WidthPixels; x++)
        {
            var t = topY(x + 0.5f);
            var start = Math.Max(0, (int)MathF.Floor(t));
            for (var y = start; y < HeightPixels; y++)
            {
                var cov = Clamp01(y + 1 - t);
                var depth = Clamp01((y - t) / Math.Max(1, HeightPixels - t));
                Blend(x, y, Vector3.Lerp(top, bottom, depth), cov * alpha);
            }
        }
    }

    /// <summary>Rolling hills: a base line plus three sines of random phase, in pixels.</summary>
    public Func<float, float> Hills(ref SampleRandom rng, float baseY, float amplitudePixels, float wavelengthFraction = 0.6f)
    {
        var w = WidthPixels;
        var f = new float[3];
        var ph = new float[3];
        for (var i = 0; i < 3; i++)
        {
            f[i] = 2 * MathF.PI / (w * wavelengthFraction * rng.Range(0.7f, 1.3f) / (1 + i * 1.7f));
            ph[i] = rng.Range(0, 2 * MathF.PI);
        }

        return x => baseY - amplitudePixels * (MathF.Sin(x * f[0] + ph[0]) + 0.45f * MathF.Sin(x * f[1] + ph[1]) + 0.18f * MathF.Sin(x * f[2] + ph[2]));
    }

    public void Stars(ref SampleRandom rng, int count, float maxY, Vector3 tint)
    {
        for (var i = 0; i < count; i++)
        {
            var x = rng.Range(0, WidthPixels);
            var y = rng.Range(0, maxY);
            var b = MathF.Pow(rng.Range(0.05f, 1f), 2.2f);
            var ix = (int)x;
            var iy = (int)y;
            var c = tint * (0.25f + 0.9f * b);
            Add(ix, iy, c);
            if (b > 0.35f)
            {
                var s = c * 0.35f;
                Add(ix + 1, iy, s);
                Add(ix - 1, iy, s);
                Add(ix, iy + 1, s);
                Add(ix, iy - 1, s);
            }

            if (b > 0.75f) Glow(x, y, 2.5f, tint, 0.25f);
        }
    }

    /// <summary>Curtains of light rising from a wavy base line; colours blend from <paramref name="low"/> to <paramref name="high"/>.</summary>
    public void Aurora(ref SampleRandom rng, float topY, float baseY, Vector3 low, Vector3 high, float strength)
    {
        var p1 = rng.Range(0, 6.28f);
        var p2 = rng.Range(0, 6.28f);
        var p3 = rng.Range(0, 6.28f);
        for (var x = 0; x < WidthPixels; x++)
        {
            var b = baseY + 16 * MathF.Sin(x * 0.018f + p1) + 7 * MathF.Sin(x * 0.051f + p2);
            var band = MathF.Pow(0.5f + 0.5f * MathF.Sin(x * 0.011f + p3), 1.5f) * (0.75f + 0.25f * MathF.Sin(x * 0.37f + p2));
            for (var y = (int)Math.Max(0, topY); y < Math.Min(HeightPixels, b + 6); y++)
            {
                var up = (b - y) / (b - topY);
                var foot = Smooth(-6, 2, b - y);
                var i = MathF.Exp(-Math.Max(0, up) * 2.6f) * foot * band * strength;
                Add(x, y, Vector3.Lerp(low, high, Clamp01(up * 1.3f)) * i);
            }
        }
    }

    /// <summary>A still-water reflection of everything above <paramref name="horizonY"/>, tinted, with gentle ripples.</summary>
    public void Mirror(float horizonY, Vector3 tint, float tintAmount, float ripplePixels)
    {
        var copy = (Vector3[])Px.Clone();
        var h = (int)horizonY;
        for (var y = h; y < HeightPixels; y++)
        {
            var depth = (y - horizonY) / Math.Max(1, HeightPixels - horizonY);
            var sy = (int)Math.Clamp(2 * horizonY - y - 1, 0, h - 1);
            var shift = ripplePixels * (0.3f + depth) * MathF.Sin(y * 0.9f) * MathF.Sin(y * 0.13f + 1.3f);
            for (var x = 0; x < WidthPixels; x++)
            {
                var sx = Math.Clamp((int)(x + shift), 0, WidthPixels - 1);
                var c = copy[sy * WidthPixels + sx];
                Px[y * WidthPixels + x] = Vector3.Lerp(c * (0.92f - 0.25f * depth), tint, tintAmount + 0.2f * depth);
            }
        }

        for (var x = 0; x < WidthPixels; x++) Blend(x, h, Vector3.One, 0.12f);
    }

    /// <summary>A soft cloud: a union of puffs with a flattish base, lit from above.</summary>
    public void Cloud(ref SampleRandom rng, float cx, float cy, float scalePixels, Vector3 light, Vector3 shade, float alpha = 1)
    {
        var n = rng.Next(4, 7);
        var puffs = new (float X, float Y, float R)[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)(n - 1) - 0.5f;
            puffs[i] = (cx + t * scalePixels * 2.2f, cy - rng.Range(0, scalePixels * 0.45f) * (1 - MathF.Abs(t) * 1.4f), scalePixels * rng.Range(0.42f, 0.7f) * (1 - MathF.Abs(t) * 0.7f));
        }

        var r = scalePixels * 2.2f;
        var x0 = Math.Max(0, (int)(cx - r));
        var y0 = Math.Max(0, (int)(cy - r));
        var x1 = Math.Min(WidthPixels - 1, (int)(cx + r));
        var y1 = Math.Min(HeightPixels - 1, (int)(cy + scalePixels * 0.6f));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                float cov = 0;
                foreach (var (px, py, pr) in puffs)
                {
                    var d = MathF.Sqrt((x + 0.5f - px) * (x + 0.5f - px) + (y + 0.5f - py) * (y + 0.5f - py));
                    cov = Math.Max(cov, Clamp01((pr - d) / 1.8f + 0.5f));
                }

                cov *= Smooth(cy + scalePixels * 0.45f, cy + scalePixels * 0.2f, y + 0.5f);
                if (cov <= 0) continue;
                var lit = Clamp01((cy + scalePixels * 0.4f - y) / (scalePixels * 1.2f));
                Blend(x, y, Vector3.Lerp(shade, light, lit), cov * alpha);
            }
        }
    }

    /// <summary>A flower head: petals arranged round a centre.</summary>
    public void Flower(float cx, float cy, float radiusPixels, int petals, Vector3 petal, Vector3 centre, float turnDegrees = 0, float petalWidth = 0.42f)
    {
        for (var i = 0; i < petals; i++)
        {
            var a = turnDegrees + i * 360f / petals;
            var rad = a * MathF.PI / 180;
            var shade = Vector3.Lerp(petal, petal * 0.82f, (i % 2) * 0.5f);
            Ellipse(cx + MathF.Cos(rad) * radiusPixels * 0.55f, cy + MathF.Sin(rad) * radiusPixels * 0.55f, radiusPixels * 0.5f, radiusPixels * petalWidth, a, shade);
        }

        Disc(cx, cy, radiusPixels * 0.3f, centre);
        Disc(cx - radiusPixels * 0.08f, cy - radiusPixels * 0.08f, radiusPixels * 0.12f, Vector3.Lerp(centre, Vector3.One, 0.35f));
    }

    public void PineTree(float x, float baseY, float heightPixels, Vector3 color, Vector3? snow = null)
    {
        var w = heightPixels * 0.3f;
        Line(x, baseY, x, baseY - heightPixels * 0.2f, Math.Max(1, heightPixels * 0.07f), color * 0.8f);
        for (var tier = 0; tier < 3; tier++)
        {
            var bottom = baseY - heightPixels * (0.12f + tier * 0.26f);
            var top = bottom - heightPixels * 0.42f;
            var half = w * (1 - tier * 0.25f);
            Triangle(x - half, bottom, x + half, bottom, x, top, color);
            if (snow is { } s) Triangle(x - half * 0.4f, top + heightPixels * 0.17f, x + half * 0.4f, top + heightPixels * 0.17f, x, top, s, 0.9f);
        }
    }

    /// <summary>A small paper lantern with its glow.</summary>
    public void Lantern(float x, float y, float sizePixels, Vector3 color, float glow = 0.55f)
    {
        Glow(x, y, sizePixels * 2.8f, color, glow * 0.5f);
        Ellipse(x, y, sizePixels * 0.75f, sizePixels, 0, color);
        Ellipse(x - sizePixels * 0.15f, y - sizePixels * 0.2f, sizePixels * 0.3f, sizePixels * 0.45f, 0, Vector3.Lerp(color, Vector3.One, 0.55f));
        Line(x - sizePixels * 0.4f, y - sizePixels, x + sizePixels * 0.4f, y - sizePixels, Math.Max(1, sizePixels * 0.25f), color * 0.5f);
    }

    public void Vignette(float strength)
    {
        for (var y = 0; y < HeightPixels; y++)
        {
            for (var x = 0; x < WidthPixels; x++)
            {
                var dx = (x + 0.5f) / WidthPixels - 0.5f;
                var dy = (y + 0.5f) / HeightPixels - 0.5f;
                Px[y * WidthPixels + x] *= 1 - strength * (dx * dx + dy * dy) * 2;
            }
        }
    }

    /// <summary>Smooth paper grain: bilinear value noise on a 20-pixel grid, ±<paramref name="amount"/>.</summary>
    public void Paper(ref SampleRandom rng, float amount)
    {
        const int cell = 20;
        var gw = WidthPixels / cell + 2;
        var gh = HeightPixels / cell + 2;
        var g = new float[gw * gh];
        for (var i = 0; i < g.Length; i++) g[i] = rng.Range(-1, 1);
        for (var y = 0; y < HeightPixels; y++)
        {
            for (var x = 0; x < WidthPixels; x++)
            {
                var fx = x / (float)cell;
                var fy = y / (float)cell;
                int ix = (int)fx, iy = (int)fy;
                float tx = Smooth(0, 1, fx - ix), ty = Smooth(0, 1, fy - iy);
                var top = g[iy * gw + ix] * (1 - tx) + g[iy * gw + ix + 1] * tx;
                var bot = g[(iy + 1) * gw + ix] * (1 - tx) + g[(iy + 1) * gw + ix + 1] * tx;
                Px[y * WidthPixels + x] *= 1 + amount * (top * (1 - ty) + bot * ty);
            }
        }
    }

    /// <summary>8-bit RGB rows, top to bottom.</summary>
    public byte[] ToRgb()
    {
        var bytes = new byte[Px.Length * 3];
        for (var i = 0; i < Px.Length; i++)
        {
            var p = Px[i];
            bytes[i * 3] = (byte)MathF.Round(Clamp01(p.X) * 255);
            bytes[i * 3 + 1] = (byte)MathF.Round(Clamp01(p.Y) * 255);
            bytes[i * 3 + 2] = (byte)MathF.Round(Clamp01(p.Z) * 255);
        }

        return bytes;
    }
}

/// <summary>A minimal PNG writer: 8-bit truecolour (colour type 2), one IDAT, per-row adaptive filters, zlib via System.IO.Compression.</summary>
internal static class Png
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(int widthPixels, int heightPixels, byte[] rgb)
    {
        var stride = widthPixels * 3;
        var raw = new byte[heightPixels * (1 + stride)];
        for (var y = 0; y < heightPixels; y++)
        {
            var best = 0;
            var bestScore = long.MaxValue;
            for (var filter = 0; filter <= 4; filter++)
            {
                long score = 0;
                for (var i = 0; i < stride; i++)
                {
                    var v = (byte)(rgb[y * stride + i] - Predict(rgb, stride, y, i, filter));
                    score += v < 128 ? v : 256 - v;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = filter;
                }
            }

            var o = y * (1 + stride);
            raw[o] = (byte)best;
            for (var i = 0; i < stride; i++) raw[o + 1 + i] = (byte)(rgb[y * stride + i] - Predict(rgb, stride, y, i, best));
        }

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                z.Write(raw);
            }

            compressed = ms.ToArray();
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, widthPixels);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), heightPixels);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // colour type 2: truecolour RGB
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", compressed);
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static int Predict(byte[] rgb, int stride, int y, int i, int filter)
    {
        int a = i >= 3 ? rgb[y * stride + i - 3] : 0;
        int b = y > 0 ? rgb[(y - 1) * stride + i] : 0;
        int c = i >= 3 && y > 0 ? rgb[(y - 1) * stride + i - 3] : 0;
        switch (filter)
        {
            case 1: return a;
            case 2: return b;
            case 3: return (a + b) >> 1;
            case 4:
                var p = a + b - c;
                int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
            default: return 0;
        }
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        s.Write(word);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var b in typeBytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(word, crc ^ 0xFFFFFFFFu);
        s.Write(word);
    }

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }

        return t;
    }
}

/// <summary>The procedurally painted pictures: 320 × 240 pixels each, painted from a seed derived from the file name.</summary>
internal static class Pictures
{
    public const int WidthPixels = 320;
    public const int HeightPixels = 240;

    public delegate void Painter(Canvas c, ref SampleRandom rng);

    public sealed record Painting(string FileName, Painter Paint);

    public static byte[] Render(Painting p)
    {
        var c = new Canvas(WidthPixels, HeightPixels);
        var rng = new SampleRandom(p.FileName);
        p.Paint(c, ref rng);
        return Png.Encode(WidthPixels, HeightPixels, c.ToRgb());
    }

    private static Vector3 C(uint rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    private static readonly Vector3 White = Vector3.One;

    public static readonly Painting SkyMap = new("sky-map.png", (Canvas c, ref SampleRandom r) =>
    {
        c.Gradient((0, C(0x0C0B26)), (1, C(0x241C4E)));
        c.Glow(90, 70, 60, C(0x6A3C8E), 0.35f);
        c.Glow(250, 180, 70, C(0x2E5C8E), 0.3f);
        c.Stars(ref r, 160, 240, C(0xE8E4FF));
        const float hx = 160, hy = 122;
        for (var ring = 1; ring <= 3; ring++)
        {
            var rad = ring * 34f;
            for (var a = 0; a < 360; a += 4)
            {
                var t = a * MathF.PI / 180;
                c.Disc(hx + MathF.Cos(t) * rad * 1.25f, hy + MathF.Sin(t) * rad * 0.8f, 0.6f, C(0x8E86C8), 0.6f);
            }
        }

        (float Angle, float Ring, uint Color, string Name)[] worlds =
        [
            (200, 2, 0x9FE08A, "garden"), (250, 3, 0xF2B66A, "workshop"), (300, 2, 0xF59AC0, "music"),
            (345, 3, 0xC8A27A, "letters"), (30, 2, 0x8ED8E8, "archive"), (95, 3, 0xB8A8F0, "constellations"), (140, 2, 0xD8D4E8, "quiet"),
        ];
        foreach (var (angle, ring, color, _) in worlds)
        {
            var t = angle * MathF.PI / 180;
            var x = hx + MathF.Cos(t) * ring * 34 * 1.25f;
            var y = hy + MathF.Sin(t) * ring * 34 * 0.8f;
            for (var s = 0.15f; s < 0.85f; s += 0.07f) c.Disc(hx + (x - hx) * s, hy + (y - hy) * s, 0.9f, C(0xFFE6A8), 0.7f);
            var size = ring == 2 ? 9f : 7f;
            c.Glow(x, y, size * 1.6f, C(color), 0.35f);
            c.Disc(x, y, size, C(color));
            c.Disc(x - size * 0.3f, y - size * 0.3f, size * 0.35f, Vector3.Lerp(C(color), White, 0.5f), 0.8f);
        }

        c.Glow(hx, hy, 30, C(0xFFC870), 0.45f);
        c.Disc(hx, hy, 15, C(0xFFB862));
        c.Disc(hx - 5, hy - 5, 5, C(0xFFE8B8), 0.9f);
        c.Ellipse(hx, hy, 25, 5, -12, C(0xFFD9A0), 0.55f);
        c.Vignette(0.35f);
    });

    public static readonly Painting[] Garden =
    [
        new("dawn-over-the-low-hills.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x2B2D6E)), (0.42f, C(0xC86B9A)), (0.7f, C(0xFFB37A)), (1, C(0xFFD9A0)));
            c.Glow(212, 150, 70, C(0xFFB070), 0.55f);
            c.Disc(212, 150, 22, C(0xFFF1C8));
            c.Cloud(ref r, 80, 70, 22, C(0xF7B8C8), C(0xB0709A), 0.8f);
            uint[] layers = [0xC89AC0, 0x9C6FA6, 0x6E4A86, 0x45305E];
            for (var i = 0; i < layers.Length; i++)
            {
                var col = C(layers[i]);
                c.Ridge(c.Hills(ref r, 150 + i * 22, 9 - i, 0.7f + i * 0.1f), col, col * 0.8f);
            }

            for (var i = 0; i < 4; i++)
            {
                var bx = 60 + i * 18 + r.Range(-4, 4);
                var by = 45 + r.Range(-8, 8);
                c.Line(bx - 4, by - 2, bx, by, 1.1f, C(0x3A2A50));
                c.Line(bx, by, bx + 4, by - 2, 1.1f, C(0x3A2A50));
            }

            c.Vignette(0.3f);
        }),
        new("moonrise-at-the-lantern-lake.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x0E1033)), (0.35f, C(0x2A2466)), (0.6f, C(0x5B4A8C)));
            c.Stars(ref r, 140, 120, C(0xF0ECFF));
            c.Glow(90, 70, 34, C(0xC8C0F0), 0.5f);
            c.Disc(90, 70, 18, C(0xF4EED8));
            c.Disc(84, 66, 4, C(0xDCD4C0));
            c.Disc(96, 76, 3, C(0xDCD4C0));
            c.Ridge(c.Hills(ref r, 128, 9), C(0x2B2550), C(0x1E1A3E));
            c.Mirror(140, C(0x1A1844), 0.25f, 2.2f);
            for (var i = 0; i < 9; i++)
            {
                var x = 30 + i * 32 + r.Range(-10, 10);
                var y = r.Range(60, 125);
                var s = r.Range(3, 5.5f);
                c.Lantern(x, y, s, C(0xFFB25A));
                c.Glow(x, 280 - y, s * 2.4f, C(0xFFB25A), 0.18f);
                c.Ellipse(x, 280 - y, s * 0.7f, s * 0.4f, 0, C(0xFFB25A), 0.35f);
            }

            c.Ridge(c.Hills(ref r, 228, 5, 0.4f), C(0x120F26), C(0x0A0818));
            for (var i = 0; i < 26; i++)
            {
                var x = r.Range(0, 320);
                c.Line(x, 236, x + r.Range(-3, 3), 236 - r.Range(10, 26), 1.2f, C(0x0E0B20));
            }

            c.Vignette(0.4f);
        }),
        new("aurora-above-the-pines.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x050A1E)), (0.5f, C(0x0B2340)), (1, C(0x13365A)));
            c.Stars(ref r, 180, 170, C(0xE6F0FF));
            c.Aurora(ref r, 10, 120, C(0x4CF2A0), C(0x9A6CF0), 0.9f);
            c.Ridge(c.Hills(ref r, 160, 16, 0.9f), C(0x14304E), C(0x0E2238));
            c.Ridge(c.Hills(ref r, 212, 4), C(0x1E3A5A), C(0x2A4A6E));
            for (var i = 0; i < 22; i++)
            {
                var x = i * 15.5f + r.Range(-5, 5);
                c.PineTree(x, 214 + r.Range(-3, 5), r.Range(26, 54), C(0x071322));
            }

            c.Vignette(0.35f);
        }),
        new("noon-meadow-with-poppies.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x5FA8E8)), (0.5f, C(0xBFE3FF)), (0.65f, C(0xE8F6FF)));
            c.Disc(262, 40, 15, C(0xFFFBE6));
            c.Glow(262, 40, 40, C(0xFFF6C8), 0.45f);
            c.Cloud(ref r, 70, 52, 20, White, C(0xC8DCF0));
            c.Cloud(ref r, 170, 80, 14, White, C(0xC8DCF0));
            c.Cloud(ref r, 300, 100, 12, White, C(0xC8DCF0), 0.9f);
            c.Ridge(c.Hills(ref r, 150, 10), C(0xA8D898), C(0x9BD18A));
            c.Ridge(c.Hills(ref r, 172, 10), C(0x7DC46A), C(0x6DBB5E));
            c.Ridge(c.Hills(ref r, 200, 6, 1.2f), C(0x5AAE50), C(0x3E8A3E));
            var flowers = new (float X, float Y)[60];
            for (var i = 0; i < flowers.Length; i++) flowers[i] = (r.Range(0, 320), r.Range(196, 238));
            Array.Sort(flowers, (a, b) => a.Y.CompareTo(b.Y));
            foreach (var (x, y) in flowers)
            {
                var s = 2 + (y - 190) * 0.13f;
                c.Line(x, y, x + 0.5f, y + s * 1.8f, Math.Max(1, s * 0.25f), C(0x2E7A32));
                c.Flower(x, y, s, 5, C(0xE8483A), C(0x2A1A12), r.Range(0, 72), 0.5f);
            }

            c.Vignette(0.2f);
        }),
        new("the-long-violet-evening.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x2A1748)), (0.4f, C(0x6B3A8E)), (0.7f, C(0xE0748A)), (1, C(0xFFB36B)));
            c.Glow(110, 172, 80, C(0xFF9A6A), 0.45f);
            c.Disc(110, 172, 30, C(0xFFD38A));
            for (var i = 0; i < 5; i++) c.Ellipse(r.Range(20, 300), r.Range(70, 140), r.Range(30, 60), r.Range(2, 3.5f), r.Range(-3, 3), C(0xF59AAE), 0.45f);
            c.Stars(ref r, 30, 60, C(0xFFE8F0));
            uint[] layers = [0x9A5A9E, 0x7A4486, 0x56306E, 0x3A2052];
            for (var i = 0; i < layers.Length; i++) c.Ridge(c.Hills(ref r, 168 + i * 16, 8), C(layers[i]), C(layers[i]) * 0.85f);
            c.Vignette(0.3f);
        }),
        new("starfield-over-the-quiet-moon.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x03040F)), (0.5f, C(0x0B0E2A)), (1, C(0x1C1846)));
            for (var y = 0; y < 240; y++)
            {
                for (var x = 0; x < 320; x++)
                {
                    var d = (x * 0.6f - y + 40) / 26f;
                    c.Add(x, y, C(0x5A4A9A) * (0.32f * MathF.Exp(-d * d)));
                }
            }

            c.Stars(ref r, 420, 240, C(0xE8E8FF));
            for (var i = 0; i < 160; i++)
            {
                var t = r.Range(0, 1);
                var x = t * 320;
                var y = x * 0.6f + 40 + r.Range(-18, 18);
                c.Add((int)x, (int)y, C(0xCFC8FF) * r.Range(0.2f, 0.7f));
            }

            c.Glow(242, 80, 46, C(0xF8E8C0), 0.3f);
            c.Crescent(242, 80, 28, -11, -6, C(0xFFF2D0));
            c.Disc(160, 560, 360, C(0x2E2A55));
            c.Disc(160, 562, 356, C(0x3A3566), 0.6f);
            c.Ellipse(90, 222, 14, 4, 0, C(0x24204A));
            c.Ellipse(210, 214, 10, 3, 0, C(0x24204A));
            c.Ellipse(250, 230, 18, 5, 0, C(0x24204A));
            c.Line(140, 205, 140, 190, 1.4f, C(0x1A1636));
            c.Lantern(140, 186, 3.5f, C(0xFFC066), 0.8f);
            c.Vignette(0.4f);
        }),
        new("mist-in-the-blue-valley.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x9DB8E0)), (0.6f, C(0xD4E2F2)), (1, C(0xE8EEF6)));
            c.Disc(230, 70, 20, C(0xFFF8EC), 0.7f);
            c.Glow(230, 70, 40, C(0xFFF0D8), 0.25f);
            uint[] layers = [0xB4C6E0, 0x98AED2, 0x7C96C0, 0x617EAC, 0x476694, 0x2D4A6E];
            for (var i = 0; i < layers.Length; i++)
            {
                c.Ridge(c.Hills(ref r, 105 + i * 22, 10 + i * 2, 0.5f + i * 0.08f), C(layers[i]), C(layers[i]) * 0.94f);
                if (i == layers.Length - 1) break;

                // Mist pools in the valleys: it thickens downwards, and the next, nearer ridge hides its lower part.
                var mistY = 118 + i * 22;
                for (var y = (int)mistY - 28; y < 240; y++)
                {
                    var a = 0.42f * Canvas.Smooth(mistY - 28, mistY + 6, y) * (1 - i * 0.1f);
                    for (var x = 0; x < 320; x++) c.Blend(x, y, C(0xEEF2FA), a);
                }
            }

            c.Vignette(0.2f);
        }),
        new("sunset-on-the-mirror-pond.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x3B1E54)), (0.3f, C(0xC2456A)), (0.5f, C(0xFF9A4A)), (0.6f, C(0xFFC870)));
            c.Glow(160, 126, 60, C(0xFFB050), 0.6f);
            c.Disc(160, 126, 20, C(0xFFE8B0));
            c.Cloud(ref r, 70, 60, 16, C(0xFFB0A0), C(0xA0406A), 0.8f);
            c.Cloud(ref r, 250, 44, 18, C(0xFFB0A0), C(0xA0406A), 0.8f);
            c.Ridge(c.Hills(ref r, 134, 5, 0.5f), C(0x6B2C55), C(0x5A2448));
            c.Mirror(142, C(0x3A1A44), 0.15f, 2.5f);
            for (var i = 0; i < 9; i++)
            {
                var x = r.Range(10, 310);
                var y = r.Range(170, 232);
                var s = 3 + (y - 160) * 0.1f;
                c.Ellipse(x, y, s * 1.6f, s * 0.6f, 0, C(0x3E7A4A));
                if (i % 3 == 0) c.Flower(x + s * 0.4f, y - s * 0.4f, s * 0.9f, 6, C(0xF8B0CC), C(0xFFE08A), 15);
            }

            for (var i = 0; i < 18; i++)
            {
                var x = r.Range(0, 60) + (i % 2 == 0 ? 0 : 260);
                c.Line(x, 240, x + r.Range(-4, 4), 240 - r.Range(18, 44), 1.4f, C(0x24102A));
            }

            c.Vignette(0.35f);
        }),
        new("tulip-rows-at-first-light.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x9CB8E0)), (0.45f, C(0xF6D7B0)), (0.6f, C(0xFCE9C8)));
            c.Glow(60, 112, 50, C(0xFFE0A8), 0.5f);
            c.Disc(60, 112, 14, C(0xFFF4DC));
            c.Ridge(c.Hills(ref r, 120, 6), C(0xA8C890), C(0x98BC80));
            c.Ridge(c.Hills(ref r, 132, 3, 1.4f), C(0x7FB06A), C(0x5E9A50));
            uint[] colours = [0xF07EA8, 0xFFD25A, 0xE8483A, 0xFFF4F0, 0xB07EE0, 0xFF9A5A];
            for (var row = 0; row < 7; row++)
            {
                var y = 138 + row * row * 2.1f + row * 4;
                var s = 1.6f + row * 1.25f;
                var col = C(colours[row % colours.Length]);
                var gap = s * 2.8f;
                for (var x = -gap + r.Range(0, gap); x < 330; x += gap)
                {
                    c.Line(x, y, x, y + s * 2.2f, Math.Max(1, s * 0.22f), C(0x3E7A3A));
                    c.Ellipse(x, y, s * 0.62f, s * 0.8f, 0, col);
                    c.Triangle(x - s * 0.62f, y - s * 0.1f, x - s * 0.2f, y - s * 0.3f, x - s * 0.5f, y - s * 1.1f, col);
                    c.Triangle(x + s * 0.62f, y - s * 0.1f, x + s * 0.2f, y - s * 0.3f, x + s * 0.5f, y - s * 1.1f, col);
                    c.Triangle(x - s * 0.25f, y - s * 0.3f, x + s * 0.25f, y - s * 0.3f, x, y - s * 1.15f, col * 0.9f);
                }
            }

            c.Vignette(0.2f);
        }),
        new("two-moons-over-the-orchard.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x10304A)), (0.45f, C(0x2F6A7A)), (0.75f, C(0x8CC7B8)));
            c.Stars(ref r, 60, 90, C(0xE0FFF4));
            c.Glow(90, 60, 36, C(0xF2F0DC), 0.35f);
            c.Disc(90, 60, 20, C(0xF2F0DC));
            c.Glow(132, 44, 18, C(0xF8D8E6), 0.3f);
            c.Disc(132, 44, 9, C(0xF8D8E6));
            c.Ridge(c.Hills(ref r, 150, 8), C(0x4A8A7A), C(0x3A7468));
            c.Ridge(c.Hills(ref r, 185, 5, 1.2f), C(0x3E7A5E), C(0x2A5A44));
            for (var row = 0; row < 3; row++)
            {
                var y = 160 + row * 26;
                var s = 9 + row * 5;
                for (var x = 20 - row * 8 + r.Range(0, 10); x < 330; x += s * 3.2f)
                {
                    c.Line(x, y + s * 0.4f, x, y + s * 1.6f, s * 0.22f, C(0x3A2A20));
                    c.Disc(x, y, s, C(0x2D5A4A));
                    c.Disc(x - s * 0.3f, y - s * 0.35f, s * 0.55f, C(0x3E7462));
                    for (var f = 0; f < 4; f++) c.Disc(x + r.Range(-s * 0.7f, s * 0.7f), y + r.Range(-s * 0.6f, s * 0.6f), Math.Max(0.8f, s * 0.12f), C(0xFFA65A));
                }
            }

            c.Vignette(0.3f);
        }),
        new("clouds-like-sleeping-sheep.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x7EC1F2)), (0.7f, C(0xCFEBFF)));
            for (var i = 0; i < 7; i++)
            {
                var x = 30 + i * 44 + r.Range(-10, 10);
                var y = 40 + (i % 3) * 34 + r.Range(-6, 6);
                var s = r.Range(11, 16);
                c.Cloud(ref r, x, y, s, White, C(0xD0E0F0));
                var hx = x + s * 1.35f;
                var hy = y + s * 0.05f;
                c.Ellipse(hx, hy, s * 0.42f, s * 0.34f, 10, C(0x5A5466));
                c.Line(hx - s * 0.2f, hy - s * 0.02f, hx - s * 0.02f, hy + s * 0.04f, 1, C(0xF0ECF6));
                c.Line(x - s * 0.6f, y + s * 0.42f, x - s * 0.6f, y + s * 0.62f, 1.6f, C(0x5A5466), 0.7f);
                c.Line(x + s * 0.6f, y + s * 0.42f, x + s * 0.6f, y + s * 0.62f, 1.6f, C(0x5A5466), 0.7f);
            }

            c.Ridge(x => 175 + 30 * MathF.Pow((x - 160) / 200f, 2) * 4 - 22, C(0x9ED88A), C(0x7CC46A));
            c.Ridge(c.Hills(ref r, 214, 5), C(0x6BBF5F), C(0x4FA048));
            for (var i = 0; i < 70; i++) c.Disc(r.Range(0, 320), r.Range(200, 240), r.Range(0.8f, 1.8f), i % 2 == 0 ? White : C(0xFFE27A));
            c.Vignette(0.15f);
        }),
        new("the-garden-after-rain.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x8FA3B8)), (0.5f, C(0xD8E2E0)), (0.75f, C(0xF2EEDD)));
            uint[] bands = [0xE85A5A, 0xF29A4A, 0xF2D25A, 0x7CC86A, 0x5AA8E0, 0x6A6AD0, 0x9A6AC8];
            for (var i = 0; i < bands.Length; i++)
            {
                var rad = 190 - i * 5f;
                var col = C(bands[i]);
                c.Shape(0, 0, 320, 240, (x, y) =>
                {
                    var d = MathF.Sqrt((x - 210) * (x - 210) + (y - 250) * (y - 250));
                    return Canvas.Clamp01(2.5f - MathF.Abs(d - rad) + 0.5f) * Canvas.Smooth(240, 120, y);
                }, col, 0.32f);
            }

            c.Cloud(ref r, 60, 40, 22, C(0xE8ECF2), C(0x98A6B8), 0.9f);
            c.Ridge(c.Hills(ref r, 160, 8), C(0x9AC490), C(0x86B47C));
            c.Ridge(c.Hills(ref r, 190, 6), C(0x6EA862), C(0x4E8A48));
            uint[] petals = [0xF27EA0, 0xFFD25A, 0xA88AE8, 0xFFFFFF, 0xF2A05A];
            for (var i = 0; i < 26; i++)
            {
                var x = r.Range(0, 320);
                var y = r.Range(186, 236);
                var s = 2.5f + (y - 180) * 0.12f;
                c.Line(x, y, x, y + s * 2.5f, Math.Max(1, s * 0.2f), C(0x2E6A30));
                c.Ellipse(x + s * 0.7f, y + s * 1.4f, s * 0.6f, s * 0.25f, 30, C(0x3E8A3A));
                c.Flower(x, y, s, 6, C(petals[i % petals.Length]), C(0xFFE07A), r.Range(0, 60));
                c.Disc(x + s * 0.5f, y - s * 0.4f, Math.Max(0.7f, s * 0.15f), White, 0.8f);
            }

            c.Vignette(0.2f);
        }),
        new("lanterns-drifting-at-dusk.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x1B1440)), (0.35f, C(0x4A2A6E)), (0.6f, C(0xB0577E)), (0.7f, C(0xF0A070)));
            c.Stars(ref r, 70, 80, C(0xFFF0F8));
            c.Ridge(c.Hills(ref r, 160, 6), C(0x3A2050), C(0x2A1840));
            c.Mirror(168, C(0x2A1A48), 0.2f, 2);
            for (var i = 0; i < 34; i++)
            {
                var t = r.Range(0, 1);
                var y = 20 + t * 140;
                var x = r.Range(0, 320);
                var s = 1.4f + t * 4.2f;
                c.Lantern(x, y, s, Vector3.Lerp(C(0xFFC066), C(0xFF8A5A), r.Range(0, 1)), 0.5f + t * 0.3f);
                if (t > 0.6f) c.Glow(x, 336 - y, s * 2, C(0xFFB060), 0.2f);
            }

            c.Vignette(0.4f);
        }),
        new("snowy-hills-under-a-pink-sky.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0xF6B8C8)), (0.5f, C(0xFCDCE0)), (0.8f, C(0xFFF0EA)));
            c.Disc(90, 80, 18, C(0xFFF8F0), 0.85f);
            c.Glow(90, 80, 40, C(0xFFE8E0), 0.3f);
            c.Ridge(c.Hills(ref r, 140, 14), C(0xE6DCF0), C(0xD8CCE8));
            c.Ridge(c.Hills(ref r, 172, 10), C(0xF4EEF6), C(0xE4DAEC));
            for (var i = 0; i < 14; i++)
            {
                var x = r.Range(0, 320);
                c.PineTree(x, 186 + r.Range(-6, 6), r.Range(22, 40), C(0x2E5A6A), White);
            }

            c.Ridge(c.Hills(ref r, 208, 6, 1.1f), White, C(0xEAE2F2));
            for (var i = 0; i < 90; i++) c.Disc(r.Range(0, 320), r.Range(0, 240), r.Range(0.6f, 1.5f), White, 0.85f);
            c.Vignette(0.15f);
        }),
        new("aurora-over-the-still-lake.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x040818)), (0.6f, C(0x0A1F3A)));
            c.Stars(ref r, 200, 140, C(0xE8F0FF));
            c.Aurora(ref r, 6, 100, C(0x5AF2B0), C(0xF06AB8), 1.0f);
            c.Ridge(c.Hills(ref r, 138, 12, 0.5f), C(0x0B1A2E), C(0x0A1626));
            c.Mirror(150, C(0x061020), 0.2f, 1.8f);
            c.Ridge(c.Hills(ref r, 226, 4), C(0x050C18), C(0x03070F));
            for (var i = 0; i < 8; i++) c.PineTree(r.Range(0, 320), 230, r.Range(18, 30), C(0x03070F));
            c.Vignette(0.4f);
        }),
        new("midnight-sunflowers.png", (Canvas c, ref SampleRandom r) =>
        {
            c.Gradient((0, C(0x0A1030)), (0.5f, C(0x1F2A5C)), (1, C(0x3A3C78)));
            c.Stars(ref r, 160, 130, C(0xF0F0FF));
            c.Glow(250, 54, 40, C(0xE8E8FF), 0.35f);
            c.Disc(250, 54, 17, C(0xF6F4E8));
            c.Ridge(c.Hills(ref r, 150, 8), C(0x202A58), C(0x1A224A));
            c.Ridge(c.Hills(ref r, 190, 6), C(0x1A3A3A), C(0x10282A));
            var heads = new (float X, float Y, float S)[8];
            for (var i = 0; i < heads.Length; i++) heads[i] = (20 + i * 40 + r.Range(-8, 8), r.Range(130, 190), 0);
            Array.Sort(heads, (a, b) => a.Y.CompareTo(b.Y));
            foreach (var (x, y, _) in heads)
            {
                var s = 9 + (y - 120) * 0.17f;
                c.Line(x, y, x + r.Range(-4, 4), 240, s * 0.18f, C(0x1E4A2A));
                c.Ellipse(x + s * 0.7f, y + s * 2.2f, s * 0.8f, s * 0.35f, 25, C(0x245A30));
                c.Ellipse(x - s * 0.7f, y + s * 3.0f, s * 0.8f, s * 0.35f, -25, C(0x245A30));
                c.Flower(x, y, s * 1.5f, 14, C(0xE8B83A), C(0x4A2E18), r.Range(0, 25), 0.22f);
                for (var d = 0; d < 9; d++) c.Disc(x + r.Range(-s * 0.3f, s * 0.3f), y + r.Range(-s * 0.3f, s * 0.3f), 0.8f, C(0x2A1A0E));
            }

            c.Vignette(0.4f);
        }),
    ];

    private static void PaperBackground(Canvas c, ref SampleRandom r, uint tone)
    {
        c.Gradient((0, C(tone)), (1, C(tone) * 0.94f));
        c.Paper(ref r, 0.018f);
        c.Vignette(0.12f);
    }

    private static void Tape(Canvas c, float x, float y, float angleDegrees) =>
        c.Ellipse(x, y, 22, 6, angleDegrees, C(0xF4E6B8), 0.55f);

    public static readonly Painting[] Pressed =
    [
        new("pressed-forget-me-nots.png", (Canvas c, ref SampleRandom r) =>
        {
            PaperBackground(c, ref r, 0xF3EBDD);
            for (var sprig = 0; sprig < 3; sprig++)
            {
                var bx = 90 + sprig * 70f;
                var by = 220f;
                var tx = bx + r.Range(-30, 30);
                var ty = 50 + r.Range(0, 30);
                c.Line(bx, by, tx, ty, 1.6f, C(0x8FA978));
                for (var l = 0; l < 3; l++)
                {
                    var t = 0.3f + l * 0.2f;
                    c.Ellipse(bx + (tx - bx) * t + 8, by + (ty - by) * t, 10, 3.5f, -30 + l * 10, C(0x9AB083));
                }

                for (var f = 0; f < 7; f++)
                {
                    var fx = tx + r.Range(-18, 18);
                    var fy = ty + r.Range(-14, 22);
                    c.Line(tx, ty + 10, fx, fy, 0.9f, C(0x8FA978));
                    c.Flower(fx, fy, r.Range(6, 9), 5, C(0x7BA7E0), C(0xF6D66A), r.Range(0, 72), 0.48f);
                }
            }

            Tape(c, 40, 30, -20);
            Tape(c, 285, 214, -20);
        }),
        new("pressed-cosmos.png", (Canvas c, ref SampleRandom r) =>
        {
            PaperBackground(c, ref r, 0xF5EDE4);
            (float X, float Y, float S, uint Col)[] blooms = [(100, 80, 26, 0xE89AC0), (210, 120, 30, 0xD06A9E), (140, 170, 20, 0xF4C8DC)];
            foreach (var (x, y, s, col) in blooms)
            {
                c.Line(x, y, x + r.Range(-30, 30), 236, 1.6f, C(0x7E9A6A));
                for (var leaf = 0; leaf < 5; leaf++)
                {
                    var ly = y + 40 + leaf * 18;
                    c.Line(x, ly, x + r.Range(-26, 26), ly - r.Range(4, 14), 0.9f, C(0x8FAA78));
                }

                c.Flower(x, y, s, 8, C(col), C(0xE8B84A), r.Range(0, 45), 0.32f);
                for (var v = 0; v < 8; v++)
                {
                    var a = v * MathF.PI / 4 + 0.2f;
                    c.Line(x + MathF.Cos(a) * s * 0.3f, y + MathF.Sin(a) * s * 0.3f, x + MathF.Cos(a) * s * 0.85f, y + MathF.Sin(a) * s * 0.85f, 0.7f, C(col) * 0.8f, 0.6f);
                }
            }

            Tape(c, 280, 30, 25);
        }),
        new("pressed-violets.png", (Canvas c, ref SampleRandom r) =>
        {
            PaperBackground(c, ref r, 0xEEE9DF);
            for (var i = 0; i < 6; i++)
            {
                var x = 50 + i * 45 + r.Range(-10, 10);
                var y = 70 + (i % 2) * 70 + r.Range(-10, 10);
                var footX = x + r.Range(-20, 20);
                c.Line(x, y + 8, footX, 232, 1.2f, C(0x86A070));
                var t = r.Range(0.35f, 0.7f);
                var sx = x + (footX - x) * t;
                var sy = y + 8 + (232 - y - 8) * t;
                var side = i % 2 == 0 ? -1 : 1;
                var lx = sx + side * 16;
                var ly = sy - 10;
                c.Line(sx, sy, lx, ly + 4, 1, C(0x86A070));
                c.Disc(lx - 5, ly, 8, C(0x7E9E6E));
                c.Disc(lx + 5, ly, 8, C(0x7E9E6E));
                c.Triangle(lx - 12, ly + 2, lx + 12, ly + 2, lx, ly + 18, C(0x7E9E6E));
                var s = r.Range(10, 14);
                c.Ellipse(x - s * 0.35f, y - s * 0.55f, s * 0.38f, s * 0.55f, -15, C(0x6A4A98));
                c.Ellipse(x + s * 0.35f, y - s * 0.55f, s * 0.38f, s * 0.55f, 15, C(0x6A4A98));
                c.Ellipse(x - s * 0.55f, y + s * 0.05f, s * 0.5f, s * 0.36f, 20, C(0x7A5AA8));
                c.Ellipse(x + s * 0.55f, y + s * 0.05f, s * 0.5f, s * 0.36f, -20, C(0x7A5AA8));
                c.Ellipse(x, y + s * 0.45f, s * 0.42f, s * 0.5f, 0, C(0x8A6AB8));
                c.Disc(x, y + s * 0.05f, s * 0.14f, C(0xF2D25A));
            }

            Tape(c, 160, 16, 2);
        }),
        new("pressed-buttercups.png", (Canvas c, ref SampleRandom r) =>
        {
            PaperBackground(c, ref r, 0xF4EEE2);
            for (var i = 0; i < 11; i++)
            {
                var x = r.Range(40, 280);
                var y = r.Range(36, 180);
                c.Line(x, y, x + r.Range(-40, 40), 236, 1.1f, C(0x8EA672));
                c.Flower(x, y, r.Range(9, 15), 5, C(0xF2C64A), C(0xD89A2A), r.Range(0, 72), 0.52f);
            }

            for (var leaf = 0; leaf < 6; leaf++)
            {
                var x = r.Range(40, 280);
                var y = r.Range(170, 225);
                for (var lobe = -1; lobe <= 1; lobe++) c.Ellipse(x + lobe * 7, y - MathF.Abs(lobe) * 2, 7, 4.5f, lobe * 35, C(0x88A26A));
            }

            Tape(c, 36, 214, 30);
            Tape(c, 284, 26, 30);
        }),
    ];
}

// ==================================================================== music box

/// <summary>
/// Gentle original music-box tunes: a seeded walk over a pentatonic scale, played by additive bell tones (a tine's
/// partials, each with its own exponential decay) over a soft bass note each bar, with a short echo. 22050 Hz mono 16-bit PCM.
/// </summary>
internal static class MusicBox
{
    public const int SampleRateHertz = 22050;

    private static readonly int[] MajorPentatonic = [0, 2, 4, 7, 9];
    private static readonly int[] MinorPentatonic = [0, 3, 5, 7, 10];

    public sealed record Tune(string FileName, string Title, int RootMidi, int[] Scale, double BeatsPerMinute, int BeatsPerBar, double DurationSeconds);

    public static readonly Tune[] Tunes =
    [
        new("lullaby-for-a-small-planet.wav", "Lullaby for a Small Planet", 72, MajorPentatonic, 84, 4, 5.2),
        new("the-lamplighters-waltz.wav", "The Lamplighter's Waltz", 74, MajorPentatonic, 132, 3, 4.6),
        new("rain-on-the-greenhouse.wav", "Rain on the Greenhouse", 77, MajorPentatonic, 120, 4, 3.8),
        new("orbit-of-the-paper-moon.wav", "Orbit of the Paper Moon", 69, MinorPentatonic, 96, 4, 5.6),
        new("seeds-in-the-wind.wav", "Seeds in the Wind", 79, MajorPentatonic, 140, 4, 3.4),
        new("goodnight-lanterns.wav", "Goodnight, Lanterns", 67, MinorPentatonic, 76, 3, 5.9),
    ];

    private readonly record struct Note(double StartSeconds, int Midi, float Velocity);

    private static List<Note> Compose(Tune t)
    {
        var rng = new SampleRandom(t.FileName);
        var beatSeconds = 60.0 / t.BeatsPerMinute;
        var lastStartSeconds = t.DurationSeconds - 1.3;
        var notes = new List<Note>();
        var degree = rng.Next(3, 7); // index over two octaves of the scale (0..10)
        int[] steps = [-2, -1, -1, 1, 1, 2, -1, 1, 3, -3, 0];
        var beat = 0;
        double time = 0.05;
        while (time < lastStartSeconds - beatSeconds * 0.5)
        {
            var downbeat = beat % t.BeatsPerBar == 0;
            if (downbeat)
            {
                var bassDegree = (beat / t.BeatsPerBar) % 2 == 0 ? 0 : 3;
                notes.Add(new(time, Pitch(t, bassDegree) - 12, 0.35f));
            }
            else if (t.BeatsPerBar == 3)
            {
                notes.Add(new(time, Pitch(t, 2) - 12, 0.16f));
            }

            var eighths = rng.NextDouble() < 0.6;
            for (var half = 0; half < (eighths ? 2 : 1); half++)
            {
                degree = Math.Clamp(degree + rng.Pick(steps), 0, 10);
                notes.Add(new(time + half * beatSeconds / 2, Pitch(t, degree), half == 0 ? (downbeat ? 0.9f : 0.75f) : 0.6f));
            }

            time += beatSeconds;
            beat++;
        }

        // Home: the tonic with its octave below, left to ring out.
        notes.Add(new(Math.Max(time, lastStartSeconds - 0.2), Pitch(t, 5), 0.85f));
        notes.Add(new(Math.Max(time, lastStartSeconds - 0.2) + 0.02, Pitch(t, 0), 0.4f));
        return notes;
    }

    private static int Pitch(Tune t, int degree) => t.RootMidi - 12 + 12 * (degree / 5) + t.Scale[degree % 5];

    public static byte[] Render(Tune t)
    {
        var count = (int)Math.Round(t.DurationSeconds * SampleRateHertz);
        var mix = new float[count];
        (float Ratio, float Amp, float DecaySeconds)[] partials = [(1f, 1f, 1.5f), (2f, 0.32f, 0.7f), (3.01f, 0.1f, 0.35f), (5.4f, 0.08f, 0.16f), (8.93f, 0.04f, 0.07f)];
        foreach (var n in Compose(t))
        {
            var f = 440.0 * Math.Pow(2, (n.Midi - 69) / 12.0);
            var brightness = (float)Math.Pow(440 / f, 0.35);
            var start = (int)(n.StartSeconds * SampleRateHertz);
            var lengthSamples = Math.Min(count - start, (int)(2.6 * SampleRateHertz));
            foreach (var (ratio, amp, decaySeconds) in partials)
            {
                var pf = f * ratio;
                if (pf > SampleRateHertz * 0.45) continue;
                var w = 2 * Math.PI * pf / SampleRateHertz;
                var decayPerSample = Math.Exp(-1.0 / (decaySeconds * brightness * SampleRateHertz));
                double env = amp * n.Velocity;
                for (var i = 0; i < lengthSamples; i++)
                {
                    var attack = Math.Min(1.0, i / (0.004 * SampleRateHertz));
                    mix[start + i] += (float)(Math.Sin(w * i) * env * attack);
                    env *= decayPerSample;
                }
            }
        }

        // A short, soft echo: the room of a small wooden box.
        var delay = (int)(0.11 * SampleRateHertz);
        for (var i = delay; i < count; i++) mix[i] += mix[i - delay] * 0.22f;

        var peak = 1e-6f;
        foreach (var v in mix) peak = Math.Max(peak, Math.Abs(v));
        var gain = 0.6f / peak;
        var fadeSamples = (int)(0.35 * SampleRateHertz);
        var pcm = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            var fade = Math.Min(1f, (count - 1 - i) / (float)fadeSamples);
            var s = (short)Math.Round(Math.Clamp(mix[i] * gain * fade, -1f, 1f) * 32767);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), s);
        }

        return Wav(pcm, SampleRateHertz);
    }

    /// <summary>A canonical 44-byte-header RIFF/WAVE file: PCM, mono, 16-bit.</summary>
    private static byte[] Wav(byte[] pcm, int sampleRateHertz)
    {
        var bytes = new byte[44 + pcm.Length];
        var s = bytes.AsSpan();
        Encoding.ASCII.GetBytes("RIFF").CopyTo(s);
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], 36 + pcm.Length);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(s[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(s[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(s[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(s[22..], 1); // mono
        BinaryPrimitives.WriteInt32LittleEndian(s[24..], sampleRateHertz);
        BinaryPrimitives.WriteInt32LittleEndian(s[28..], sampleRateHertz * 2); // bytes per second
        BinaryPrimitives.WriteInt16LittleEndian(s[32..], 2); // block align
        BinaryPrimitives.WriteInt16LittleEndian(s[34..], 16); // bits per sample
        Encoding.ASCII.GetBytes("data").CopyTo(s[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(s[40..], pcm.Length);
        pcm.CopyTo(s[44..]);
        return bytes;
    }

    public static string Playlist()
    {
        var sb = new StringBuilder("#EXTM3U\n#PLAYLIST:Music Box\n");
        foreach (var t in Tunes)
        {
            sb.Append(CultureInfo.InvariantCulture, $"#EXTINF:{(int)Math.Round(t.DurationSeconds)},{t.Title}\n{t.FileName}\n");
        }

        return sb.ToString();
    }
}

// ==================================================================== star archive

/// <summary>The Star Archive: many small plausible observatory files (.log, .csv, .json), 200-900 bytes each.</summary>
internal static class StarArchive
{
    public const int FileCount = 1500;
    public const int YearFileCount = 30;

    private static readonly string[] Objects =
    [
        "The Kettle α", "The Kettle β", "Paper Crane γ", "Lantern Moth α", "Sleeping Fox δ", "The Teacup ε", "Quiet Owl α",
        "Bell Flower β", "The Rowboat γ", "Comet Hare α", "Two Acorns β", "The Weathervane α", "Thimble γ", "Garden Gate δ",
    ];

    private static readonly string[] Events =
    [
        "dome opened, sky clear", "dome closed, dew forming", "focus adjusted by {0} steps", "guide star acquired: {1}",
        "cloud passing, exposure paused", "exposure resumed", "filter wheel moved to {2}", "tea break (kettle on)",
        "frame {3} saved", "seeing improved to {4} arcsec", "wind {5} m/s from the west", "lantern dimmed for dark adaptation",
        "meteor noted near {1}", "calibration frames taken", "humidity {6}%", "telescope parked",
    ];

    private static readonly string[] Filters = ["clear", "rose", "amber", "violet", "deep blue"];

    public static IEnumerable<(string Name, string Text)> Files()
    {
        for (var i = 1; i <= FileCount; i++)
        {
            var kind = (i - 1) % 3;
            var index = (i - 1) / 3 + 1;
            var tag = index.ToString("D4", CultureInfo.InvariantCulture);
            yield return kind switch
            {
                0 => ($"log-{tag}.log", Log($"log-{tag}", new DateTime(2024, 1, 1).AddDays(index * 0.73))),
                1 => ($"readings-{tag}.csv", Csv($"readings-{tag}", new DateTime(2024, 1, 1).AddDays(index * 0.73))),
                _ => ($"survey-{tag}.json", Json($"survey-{tag}", new DateTime(2024, 1, 1).AddDays(index * 0.73), index)),
            };
        }
    }

    public static IEnumerable<(string Name, string Text)> YearFiles()
    {
        for (var m = 1; m <= 12; m++)
        {
            var name = $"readings-2025-{m:D2}.csv";
            yield return (name, Csv(name, new DateTime(2025, m, 3)));
        }

        for (var n = 1; n <= YearFileCount - 12; n++)
        {
            var name = $"night-watch-2025-{n:D2}.log";
            yield return (name, Log(name, new DateTime(2025, 1, 1).AddDays(n * 19.5)));
        }
    }

    private static int Utf8(string s) => Encoding.UTF8.GetByteCount(s);

    /// <summary>A file's size budget in bytes.</summary>
    private static int Target(ref SampleRandom rng) => rng.Next(340, 901);

    private static string Stamp(DateTime t) => t.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Log(string seed, DateTime night)
    {
        var rng = new SampleRandom(seed);
        var target = Target(ref rng);
        var sb = new StringBuilder();
        var t = night.Date.AddHours(20.5 + rng.NextDouble());
        while (true)
        {
            var level = rng.NextDouble() < 0.12 ? "NOTE" : "INFO";
            var ev = string.Format(CultureInfo.InvariantCulture, rng.Pick(Events),
                rng.Next(3, 40), rng.Pick(Objects), rng.Pick(Filters), rng.Next(100, 999), Math.Round(rng.Range(0.8f, 3.2f), 1), rng.Next(1, 9), rng.Next(35, 92));
            var line = $"{Stamp(t)} {level,-4} observatory: {ev}\n";
            if (Utf8(sb.ToString()) + Utf8(line) > target) break;
            sb.Append(line);
            t = t.AddSeconds(rng.Next(40, 1400));
        }

        return sb.ToString();
    }

    private static string Csv(string seed, DateTime night)
    {
        var rng = new SampleRandom(seed);
        var target = Target(ref rng);
        var sb = new StringBuilder("time_utc,object,magnitude,seeing_arcsec,temperature_c\n");
        var t = night.Date.AddHours(21 + rng.NextDouble());
        while (true)
        {
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{Stamp(t)},{rng.Pick(Objects)},{rng.Range(1.2f, 6.4f):0.00},{rng.Range(0.8f, 3.4f):0.0},{rng.Range(-4f, 16f):0.0}\n");
            if (Utf8(sb.ToString()) + Utf8(line) > target) break;
            sb.Append(line);
            t = t.AddMinutes(rng.Next(3, 45));
        }

        return sb.ToString();
    }

    private static string Json(string seed, DateTime night, int index)
    {
        var rng = new SampleRandom(seed);
        var target = Target(ref rng);
        var head = string.Create(CultureInfo.InvariantCulture,
            $"{{\n  \"survey\": \"SA-{index:D4}\",\n  \"night\": \"{night:yyyy-MM-dd}\",\n  \"observer\": \"the night shift\",\n  \"objects\": [\n");
        const string tail = "\n  ]\n}\n";
        var items = new List<string>();
        var length = Utf8(head) + Utf8(tail);
        while (true)
        {
            var item = string.Create(CultureInfo.InvariantCulture,
                $"    {{ \"name\": \"{rng.Pick(Objects)}\", \"ra_deg\": {rng.Range(0, 360):0.00}, \"dec_deg\": {rng.Range(-60, 80):0.00}, \"magnitude\": {rng.Range(1.2f, 6.4f):0.00} }}");
            var extra = Utf8(item) + (items.Count > 0 ? 2 : 0);
            if (items.Count > 0 && length + extra > target) break;
            items.Add(item);
            length += extra;
        }

        return head + string.Join(",\n", items) + tail;
    }
}

// ==================================================================== texts

/// <summary>Every written word of the sample: original, warm and whimsical; no real people.</summary>
internal static class SampleTexts
{
    public const string RootReadme = """
        # Starter Sky

        Welcome! FileScape made this little sky so you have somewhere friendly to wander first.
        Every folder here is a small world: walk all the way round it, step onto a bloom launcher and press Space to fly to the next.
        The pictures, music-box tunes, letters and the tiny lantern project are real files, so open them, preview them, keep them.
        When you are ready, point FileScape at your own folders. You can delete this sky at any time; nothing depends on it.

        """;

    public const string RootNotes = """
        Notes for a first walk
        ======================

        - Move with the arrow keys or WASD. The camera follows on its own.
        - Stand on a bloom launcher and press Space: a little wind-up, then off you go.
        - Walk up to a glowing file and press E to look at it more closely.
        - Garden Photos is a meadow of pictures. Music Box rings. Letters is a quiet library.
        - The Star Archive holds far too many small files to show one by one; see how it copes.
        - Quiet Moon is empty on purpose. Somebody has to keep the silence.

        """;

    public const string LinerNotes = """
        Music Box - liner notes
        =======================

        Six small tunes for a tin music box that never existed.

        Each one is a walk over a five-note scale, played by bell tones that ring and fade
        the way a comb of steel tines does when the drum turns past. Under every bar there
        is a soft low note, and a short echo, as if the box sat on a wooden table.

          1. Lullaby for a Small Planet     C major pentatonic, slow
          2. The Lamplighter's Waltz        D, in three
          3. Rain on the Greenhouse         F, quick and light
          4. Orbit of the Paper Moon        A minor pentatonic
          5. Seeds in the Wind              G, quickest of all
          6. Goodnight, Lanterns            G minor pentatonic, in three

        All six were made by FileScape from numbers alone. You may play them, keep them,
        or hum them to a plant. They will not mind.

        """;

    /// <summary>The Long Way Down: six nested folders, each with a waymark note (names and notes original).</summary>
    public static readonly (string Name, string Note)[] LongWayDown =
    [
        ("Meadow Gate", "Waymark 1 — the trails begin (the Long Way Down itself was the outskirts, one ring). Amber light and a quicker step: two rings.\n"),
        ("Fern Trail", "Waymark 2 — still the trails. The tune you heard at the gate comes back, a little changed.\n"),
        ("Old Bridge", "Waymark 3 — the deep woods. Violet sky, three rings.\n"),
        ("Deep Woods", "Waymark 4 — the woods go on. Listen for the gate's tune again.\n"),
        ("Hollow Oak", "Waymark 5 — the underglow. Cyan light, four rings.\n"),
        ("Glowing Spring", "Waymark 6 — still the underglow, as deep as this sample goes. Backspace takes you home the way you came.\n"),
    ];

    public static readonly (string Path, string Text)[] Workshop =
    [
        ("README.md", """
            # Lantern

            A tiny C# library that models a lantern: a wick, some oil and the glow they make together.
            It is part of FileScape's Starter Sky, a sample you can open, read and build.

            ## Build

            ```
            dotnet build Lantern.sln
            ```

            ## Test

            The tests in `tests/` use xUnit. They are left out of the plain build so it needs no packages;
            to run them (this restores xUnit from NuGet):

            ```
            dotnet test Lantern.csproj -p:WithTests=true
            ```

            ## Layout

            - `src/Lantern.cs` - the lantern: light it, burn it, snuff it.
            - `src/Wick.cs` - wicks and how fast they drink oil.
            - `src/Glow.cs` - how bright and what colour.
            - `tests/LanternTests.cs` - a few small checks.
            - `docs/design.md` - why it is shaped this way.

            """),
        ("Lantern.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RootNamespace>LanternWorkshop</RootNamespace>
              </PropertyGroup>
              <!-- The plain build is the library alone; `-p:WithTests=true` adds the xUnit tests. -->
              <ItemGroup Condition="'$(WithTests)' != 'true'">
                <Compile Remove="tests/**" />
              </ItemGroup>
              <PropertyGroup Condition="'$(WithTests)' == 'true'">
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup Condition="'$(WithTests)' == 'true'">
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
              </ItemGroup>
            </Project>

            """),
        ("Lantern.sln", """

            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            VisualStudioVersion = 17.0.31903.59
            MinimumVisualStudioVersion = 10.0.40219.1
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Lantern", "Lantern.csproj", "{6C1E2A3B-4D5E-4F60-8A71-92B3C4D5E6F7}"
            EndProject
            Global
            	GlobalSection(SolutionConfigurationPlatforms) = preSolution
            		Debug|Any CPU = Debug|Any CPU
            		Release|Any CPU = Release|Any CPU
            	EndGlobalSection
            	GlobalSection(ProjectConfigurationPlatforms) = postSolution
            		{6C1E2A3B-4D5E-4F60-8A71-92B3C4D5E6F7}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		{6C1E2A3B-4D5E-4F60-8A71-92B3C4D5E6F7}.Debug|Any CPU.Build.0 = Debug|Any CPU
            		{6C1E2A3B-4D5E-4F60-8A71-92B3C4D5E6F7}.Release|Any CPU.ActiveCfg = Release|Any CPU
            		{6C1E2A3B-4D5E-4F60-8A71-92B3C4D5E6F7}.Release|Any CPU.Build.0 = Release|Any CPU
            	EndGlobalSection
            EndGlobal

            """),
        ("src/Lantern.cs", """
            namespace LanternWorkshop;

            /// <summary>A small oil lantern. Light it, let it burn for a while, and read its glow.</summary>
            public sealed class Lantern
            {
                public Lantern(Wick wick, double oilMillilitres)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(oilMillilitres);
                    Wick = wick;
                    OilMillilitres = oilMillilitres;
                }

                public Wick Wick { get; }

                public double OilMillilitres { get; private set; }

                public bool IsLit { get; private set; }

                /// <summary>What the lantern gives off right now.</summary>
                public Glow Glow => IsLit ? Glow.Of(Wick) : Glow.Dark;

                public void Light()
                {
                    if (OilMillilitres <= 0)
                    {
                        throw new InvalidOperationException("There is no oil: the wick only smokes.");
                    }

                    IsLit = true;
                }

                public void Snuff() => IsLit = false;

                /// <summary>Burns for <paramref name="duration"/>; the flame goes out by itself when the oil runs dry.</summary>
                public void Burn(TimeSpan duration)
                {
                    if (!IsLit)
                    {
                        return;
                    }

                    var used = Wick.MillilitresPerHour * duration.TotalHours;
                    OilMillilitres = Math.Max(0, OilMillilitres - used);
                    if (OilMillilitres == 0)
                    {
                        IsLit = false;
                    }
                }

                public void Refill(double millilitres)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(millilitres);
                    OilMillilitres += millilitres;
                }
            }

            """),
        ("src/Wick.cs", """
            namespace LanternWorkshop;

            public enum WickMaterial
            {
                Cotton,
                Linen,
                Hemp,
            }

            /// <summary>A wick: longer wicks burn brighter and thirstier.</summary>
            public sealed record Wick(double LengthMillimetres, WickMaterial Material)
            {
                /// <summary>How much oil the wick drinks, in millilitres per hour.</summary>
                public double MillilitresPerHour => LengthMillimetres * Material switch
                {
                    WickMaterial.Cotton => 0.50,
                    WickMaterial.Linen => 0.42,
                    WickMaterial.Hemp => 0.36,
                    _ => 0.50,
                };
            }

            """),
        ("src/Glow.cs", """
            namespace LanternWorkshop;

            /// <summary>How a lantern looks from across the garden.</summary>
            public readonly record struct Glow(double Lumens, string Colour)
            {
                public static Glow Dark { get; } = new(0, "none");

                public static Glow Of(Wick wick)
                {
                    var lumens = Math.Round(wick.LengthMillimetres * 1.8, 1);
                    var colour = wick.Material switch
                    {
                        WickMaterial.Linen => "honey",
                        WickMaterial.Hemp => "amber",
                        _ => "warm white",
                    };
                    return new Glow(lumens, colour);
                }

                public string Describe() => Lumens <= 0 ? "dark" : $"{Lumens} lm, {Colour}";
            }

            """),
        ("tests/LanternTests.cs", """
            using LanternWorkshop;
            using Xunit;

            namespace LanternWorkshop.Tests;

            public class LanternTests
            {
                [Fact]
                public void A_new_lantern_is_dark()
                {
                    var lantern = new Lantern(new Wick(10, WickMaterial.Cotton), 50);
                    Assert.False(lantern.IsLit);
                    Assert.Equal(Glow.Dark, lantern.Glow);
                }

                [Fact]
                public void A_lit_lantern_glows_by_its_wick()
                {
                    var lantern = new Lantern(new Wick(10, WickMaterial.Hemp), 50);
                    lantern.Light();
                    Assert.Equal("18 lm, amber", lantern.Glow.Describe());
                }

                [Fact]
                public void Burning_uses_oil_and_the_flame_goes_out_when_it_is_gone()
                {
                    var lantern = new Lantern(new Wick(10, WickMaterial.Cotton), 10);
                    lantern.Light();
                    lantern.Burn(TimeSpan.FromHours(1));
                    Assert.Equal(5, lantern.OilMillilitres, precision: 6);
                    lantern.Burn(TimeSpan.FromHours(3));
                    Assert.Equal(0, lantern.OilMillilitres);
                    Assert.False(lantern.IsLit);
                }

                [Fact]
                public void An_empty_lantern_will_not_light()
                {
                    var lantern = new Lantern(new Wick(8, WickMaterial.Linen), 0);
                    Assert.Throws<InvalidOperationException>(lantern.Light);
                }
            }

            """),
        ("docs/design.md", """
            # Lantern - design notes

            ## What it is for

            Lantern is a toy model, small enough to read in one sitting. It exists so the Starter Sky
            has a real project in it: a solution, a project file, sources, tests and docs.

            ## Decisions

            1. **Oil is the only resource.** A lantern burns while it is lit and has oil. When the oil
               runs out it goes dark by itself; it never goes below zero.
            2. **The wick decides the glow.** Brightness follows the wick's length; colour follows its
               material. Cotton is warm white, linen is honey, hemp is amber.
            3. **Time is a `TimeSpan`.** `Burn` takes a duration, never a bare number of hours.
            4. **No clocks inside.** Nothing reads the time of day, so tests are exact.

            ## Ideas for later

            - Glass shades that tint the glow (rose, sea green, violet).
            - A draught that makes the flame flicker.
            - A lamplighter who walks a round of lanterns at dusk.

            """),
        (".gitignore", """
            bin/
            obj/
            .vs/
            *.user
            TestResults/

            """),
    ];

    public static readonly (string Name, string Text)[] Letters =
    [
        ("to-the-lamplighter.md", """
            # To the Lamplighter

            Dear Lamplighter,

            Thank you for lighting the lantern at the end of our lane every evening, even the windy ones.
            The moths have started to wait for you; so, if I am honest, have I.

            I left a jar of plum jam on the post. It is not payment, it is a thank-you.

            With warm regards,
            the house with the blue gate

            """),
        ("a-note-left-on-the-kettle.txt", """
            Gone to the small world with the bell on it. Back before the second moon sets.
            The seedlings want water at noon, not before. Do not let the cat sit in the tulips,
            whatever she tells you.

            Tea is in the round tin.

            """),
        ("postcard-from-the-violet-moon.md", """
            # Postcard from the Violet Moon

            Arrived safely! The launch was brisk, the landing soft, and the view on the way was
            everything you said: our little world shrinking to a marble while this one grew to a garden.

            You can walk all the way round in an afternoon. I did it twice.
            The grass here is lilac and hums when the wind goes over it.

            Wish you were here (and that you had packed my scarf).

            """),
        ("on-planting-moonbeans.md", """
            # On Planting Moonbeans

            1. Sow them at dusk, never at noon: they sulk in bright light.
            2. One bean per hand-width. They like company, but not crowding.
            3. Water them with rainwater that has sat out under the stars for a night.
            4. When the first pale leaf opens, hum something gentle to it. Any tune will do.
            5. Harvest when the pods glow faintly. If they glow brightly, you have waited too long,
               and you will have to read by them instead.

            """),
        ("thank-you-for-the-seeds.txt", """
            Dear neighbour across the sky,

            The seeds arrived tucked inside the paper crane, which flew the last part of the way
            on its own and landed on the windowsill. I have never had post do that before.

            They are sprouting already. I have named the tallest one Persistence.

            Gratefully,
            the gardener on the green planet

            """),
        ("directions-to-the-small-world-with-the-bell.txt", """
            How to get to the small world with the bell
            -------------------------------------------

            Start at the hearth. Walk past the tall lantern until the path curls round to the far side.
            You will see a launcher shaped like a primrose. Stand in the middle of it.
            Look up: the bell world hangs right above you, gold and a little lopsided.
            Press Space and let the primrose do the rest.

            When you land, follow the sound. You cannot get lost; the whole planet rings.

            """),
        ("the-lantern-that-would-not-dim.md", """
            # The Lantern That Would Not Dim

            There was once a lantern that refused to go out. Wind tried. Rain tried.
            The night itself leaned close and blew.

            "Why do you keep shining?" asked the night.

            "Because somebody might be walking home," said the lantern,
            "and it is a long way round a small world in the dark."

            The night thought about that, and after a while it sat down beside the lantern
            and kept it company until morning.

            """),
        ("letter-from-a-traveller-between-worlds.md", """
            # Letter from a Traveller Between Worlds

            Dear friend,

            I have now hopped between eleven small worlds, and I have learned three things:

            - Every world looks bigger once you are standing on it.
            - You can always see where you are going next, if you remember to look up.
            - It is polite to wave at the world you just left.

            I am writing this on a planet made mostly of bookshelves. Somebody keeps the dust off
            them, but I have not met them yet. I suspect owls.

            Until the next launch,
            a wanderer with a very full notebook

            """),
        ("list-of-things-to-bring.txt", """
            Things to bring on the next journey
            ===================================

            [x] the warm scarf (this time)
            [x] a lantern, with a spare wick
            [x] seeds for the meadow world
            [ ] something to trade at the bell market
            [ ] a song to hum on the launchers
            [ ] patience, for the quiet moon

            """),
        ("reply-about-the-greenhouse.md", """
            # Re: the greenhouse

            Yes, you may borrow the greenhouse for the winter. The door sticks, so lift it a little
            as you push. The tomatoes on the left are spoken for; the ones on the right are anyone's.

            When it rains, the glass roof plays a tune. I have tried to write it down,
            but it never comes out the same twice. Perhaps that is the point.

            """),
    ];

    public static readonly (string Name, string Text)[] Drafts =
    [
        ("unfinished-letter-to-the-comet.md", """
            # To the Comet (draft)

            Dear Comet,

            You pass by every so often and never stay for tea. I understand that you are busy,
            and that your tail would not fit in the kitchen, but

            """),
        ("draft-garden-plan.txt", """
            Garden plan (not final)

            north bed: tulips? or the singing grass
            south bed: moonbeans, if they forgive me for last year
            by the gate: something that glows, so visitors find the latch
            ask the lamplighter about the corner that never gets sun

            """),
        ("half-a-poem-about-wicks.txt", """
            A wick is a thread that has made up its mind
            to drink the dark slowly and leave light behind.
            It asks for so little: some oil and a spark,
            and then it

            (finish this when the right word turns up)

            """),
    ];

    public static readonly (string Name, string Legend)[] Constellations =
    [
        ("The Kettle", "A round pot of seven stars whose spout points at the morning. When it rises, someone somewhere is making tea."),
        ("Paper Crane", "Folded from the faint stars of the west; it flies a little further along the sky each night."),
        ("The Lamplighter", "A tall figure carrying a pole with one bright star at its tip."),
        ("Sleeping Fox", "A curl of reddish stars with its tail over its nose. Never point at it; it is resting."),
        ("The Teacup", "Small, chipped and well loved: four stars and a handle."),
        ("Lantern Moth", "Two wide wings of dim stars around one warm one."),
        ("The Wheelbarrow", "Tipped forward, as if about to spill a load of stardust into the garden."),
        ("Snail on a Leaf", "The slowest constellation: it seems to move only when you are not looking."),
        ("The Open Book", "Two pages of stars; old stargazers said you could read tomorrow's weather in it."),
        ("Little Ladder", "Five rungs, leaning against nothing at all."),
        ("The Watering Can", "Its rose sprinkles a trail of faint stars over the Kettle."),
        ("Kite String", "A long thin line of stars with a bright diamond at the end, always tugging north."),
        ("The Hedgehog", "A tight ball of many small stars. Best seen in autumn."),
        ("Two Acorns", "A pair of bright stars that set together, every time."),
        ("The Spinning Top", "Three stars on a point, said to wobble on clear windy nights."),
        ("Bell Flower", "A drooping cup of blue stars. In summer it is said to ring."),
        ("The Rowboat", "It drifts low along the horizon with two oar stars."),
        ("Knitted Scarf", "A long zigzag of stars someone started and never finished."),
        ("The Weathervane", "It turns with the seasons and always shows where the wind will come from."),
        ("Sugar Bowl", "A neat little cluster beside the Teacup, of course."),
        ("The Long Bridge", "A slow arc of stars from one side of the sky to the other."),
        ("Candle Stub", "Short and steady, with a flickering star for a flame."),
        ("The Postbox", "A square of four stars with a slot of darkness. Letters to the sky go here."),
        ("Quiet Owl", "Two wide golden eyes; the rest of it is very hard to see."),
        ("The Umbrella", "It opens wide in the rainy months, so they say."),
        ("Cocoa Mug", "A warm cluster that rises in winter and sets in spring."),
        ("The Pinwheel", "Four arms of stars around a hub, turning ever so slowly."),
        ("Seed Packet", "A small rectangle of stars that scatters a few new ones every year."),
        ("The Harp Fern", "Strings of faint stars hanging from a curled frond."),
        ("Button Tin", "A jumble of bright round stars of every colour."),
        ("The Sleepy Whale", "The largest constellation; its spout is a smudge of nebula."),
        ("Thimble", "One tight bright knot of stars, easy to lose and easy to find again."),
        ("The Garden Gate", "Two posts and a crossbar; travellers say it opens onto the summer sky."),
        ("Pocket Watch", "A ring of twelve faint stars with a single bright hand."),
        ("The Comet Hare", "A leaping shape with a streak behind it, as if it had just stolen a comet's tail."),
        ("Paper Boat", "A small triangle of stars floating on the edge of the Long Bridge."),
    ];

    private static readonly string[] Seasons = ["spring", "summer", "autumn", "winter"];
    private static readonly string[] Directions = ["north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west"];

    /// <summary>One to three small text files for constellation <paramref name="index"/>.</summary>
    public static IEnumerable<(string Name, string Text)> ConstellationFiles(int index)
    {
        var (name, legend) = Constellations[index];
        var rng = new SampleRandom(name);
        yield return ("about.txt", $"{name}\n{new string('-', name.Length)}\n\n{legend}\n");
        if (index % 3 >= 1)
        {
            yield return ("how-to-find.txt", string.Create(CultureInfo.InvariantCulture,
                $"How to find {name}\n\nBest seen in {Seasons[index % 4]}, low in the {rng.Pick(Directions)} after dusk.\nBrightest star: about magnitude {rng.Range(0.8f, 3.5f):0.0}.\nStars that make the shape: {rng.Next(4, 13)}.\n"));
        }

        if (index % 3 == 2)
        {
            yield return ("sightings.md", string.Create(CultureInfo.InvariantCulture,
                $"# Sightings of {name}\n\n- {2024 + index % 2}-{1 + index % 12:D2}-{1 + rng.Next(0, 28):D2}: clear, seen with the naked eye.\n- a misty night: only the brightest star showed.\n- with a lantern dimmed: {rng.Next(2, 9)} more faint stars than usual.\n"));
        }
    }
}
