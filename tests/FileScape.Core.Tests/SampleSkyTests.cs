using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FileScape.Core.Samples;

namespace FileScape.Core.Tests;

/// <summary>One Starter Sky written into an owned temporary directory, shared by the tests that only read it.</summary>
public sealed class SampleSkyFixture : IDisposable
{
    public SampleSkyFixture()
    {
        Parent = Path.Combine(Path.GetTempPath(), "FileScape-tests", "sky-" + Guid.NewGuid().ToString("N")[..8]);
        Root = SampleSky.Create(Parent);
    }

    public string Parent { get; }

    public string Root { get; }

    public void Dispose()
    {
        // Linux/WSL never gets the Windows-only junction; on Windows, remove it first so nothing follows it.
        var link = Path.Combine(Root, "Shortcut to Garden");
        if (Directory.Exists(link)) Directory.Delete(link, recursive: false);
        if (Directory.Exists(Parent)) Directory.Delete(Parent, recursive: true);
    }
}

/// <summary>The curated first-impression sample (Planets): structure, real file formats, size budget and safety.</summary>
public sealed class SampleSkyTests(SampleSkyFixture sky) : IClassFixture<SampleSkyFixture>
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private string P(params string[] parts) => Path.Combine([sky.Root, .. parts]);

    private static string[] Files(string dir) => Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    private static string[] Dirs(string dir) => Directory.GetDirectories(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void Create_returns_the_root_under_the_parent_and_marks_it()
    {
        sky.Root.Should().Be(Path.Combine(Path.GetFullPath(sky.Parent), SampleSky.RootName));
        SampleSky.IsSample(sky.Root).Should().BeTrue();
        File.ReadAllText(P(SampleSky.MarkerName)).Should().Contain($"version={SampleSky.Version}");
        SampleSky.IsSample(sky.Parent).Should().BeFalse();
        SampleSky.IsSample(Path.Combine(sky.Parent, "does-not-exist")).Should().BeFalse();
        SampleSky.IsSample("").Should().BeFalse();
        Directory.GetDirectories(sky.Parent).Should().ContainSingle("the staging folder is renamed into place, never left behind");
    }

    [Fact]
    public void The_tree_has_the_curated_structure_and_counts()
    {
        Files(sky.Root).Should().BeEquivalentTo(SampleSky.MarkerName, "README.md", "notes.txt", "sky-map.png");
        var expectedDirs = new[] { "Constellations", "Garden Photos", "Lantern Workshop", "Letters", "Music Box", "Quiet Moon", "Star Archive" };
        Dirs(sky.Root).Except(["Shortcut to Garden"]).Should().BeEquivalentTo(expectedDirs);

        File.ReadAllLines(P("README.md")).Count(l => l.Length > 0 && !l.StartsWith('#')).Should().BeInRange(3, 6);

        Files(P("Garden Photos")).Should().HaveCount(16).And.OnlyContain(n => n.EndsWith(".png"));
        Dirs(P("Garden Photos")).Should().Equal("Pressed Flowers");
        Files(P("Garden Photos", "Pressed Flowers")).Should().HaveCount(4).And.OnlyContain(n => n.EndsWith(".png"));
        Files(P("Garden Photos")).Should().Contain("dawn-over-the-low-hills.png");

        Directory.GetFiles(P("Lantern Workshop"), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(P("Lantern Workshop"), f).Replace('\\', '/'))
            .Should().BeEquivalentTo("README.md", "Lantern.csproj", "Lantern.sln", "src/Lantern.cs", "src/Wick.cs", "src/Glow.cs",
                "tests/LanternTests.cs", "docs/design.md", ".gitignore");

        Files(P("Music Box")).Where(n => n.EndsWith(".wav")).Should().HaveCount(6);
        Files(P("Music Box")).Should().Contain(["playlist.m3u", "liner-notes.txt"]).And.HaveCount(8);
        var playlist = File.ReadAllLines(P("Music Box", "playlist.m3u"));
        playlist[0].Should().Be("#EXTM3U");
        playlist.Where(l => l.EndsWith(".wav")).Should().BeEquivalentTo(Files(P("Music Box")).Where(n => n.EndsWith(".wav")));

        Files(P("Letters")).Should().HaveCount(10).And.OnlyContain(n => n.EndsWith(".md") || n.EndsWith(".txt"));
        Files(P("Letters", "Drafts")).Should().HaveCount(3);

        var archive = Files(P("Star Archive"));
        archive.Should().HaveCount(1500);
        archive.Select(Path.GetExtension).Distinct().Should().BeEquivalentTo(".log", ".csv", ".json");
        archive.Should().Contain(["log-0001.log", "readings-0001.csv"]);
        Dirs(P("Star Archive")).Should().Equal("2025");
        Files(P("Star Archive", "2025")).Should().HaveCount(30);

        var constellations = Dirs(P("Constellations"));
        constellations.Should().HaveCount(36).And.Contain(["The Kettle", "Paper Crane"]);
        foreach (var c in constellations)
        {
            Directory.GetFiles(P("Constellations", c)).Length.Should().BeInRange(1, 3, c);
            Directory.GetDirectories(P("Constellations", c)).Should().BeEmpty();
        }

        Directory.EnumerateFileSystemEntries(P("Quiet Moon")).Should().BeEmpty();
    }

    [Fact]
    public void Star_archive_files_are_200_to_900_bytes_of_plausible_text()
    {
        foreach (var f in Directory.GetFiles(P("Star Archive"), "*", SearchOption.AllDirectories))
        {
            new FileInfo(f).Length.Should().BeInRange(200, 900, f);
        }

        File.ReadAllText(P("Star Archive", "readings-0001.csv")).Should().StartWith("time_utc,object,magnitude");
        File.ReadAllText(P("Star Archive", "log-0001.log")).Should().Contain("observatory:");
        foreach (var json in Directory.GetFiles(P("Star Archive"), "*.json").Take(50))
        {
            var act = () => System.Text.Json.JsonDocument.Parse(File.ReadAllText(json)).Dispose();
            act.Should().NotThrow(json);
        }
    }

    [Fact]
    public void Every_png_is_a_valid_320_by_240_truecolour_image_with_real_content()
    {
        var pngs = Directory.GetFiles(sky.Root, "*.png", SearchOption.AllDirectories);
        pngs.Should().HaveCount(21);
        foreach (var png in pngs)
        {
            var bytes = File.ReadAllBytes(png);
            bytes.AsSpan(0, 8).ToArray().Should().Equal(PngSignature, png);
            var offset = 8;
            int? width = null, height = null;
            using var idat = new MemoryStream();
            var chunkTypes = new List<string>();
            while (offset < bytes.Length)
            {
                var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
                var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
                var data = bytes.AsSpan(offset + 8, length);
                var crc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + length));
                Crc32(bytes.AsSpan(offset + 4, 4 + length)).Should().Be(crc, $"{png}: {type} CRC");
                chunkTypes.Add(type);
                if (type == "IHDR")
                {
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    data[8].Should().Be(8, "bit depth");
                    data[9].Should().Be(2, "colour type 2 (RGB)");
                    data[10..13].ToArray().Should().Equal([0, 0, 0], "deflate, adaptive filtering, no interlace");
                }
                else if (type == "IDAT")
                {
                    idat.Write(data);
                }

                offset += 12 + length;
            }

            offset.Should().Be(bytes.Length);
            chunkTypes[0].Should().Be("IHDR");
            chunkTypes[^1].Should().Be("IEND");
            width.Should().Be(320);
            height.Should().Be(240);

            idat.Position = 0;
            using var z = new ZLibStream(idat, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            z.CopyTo(raw);
            var scanlines = raw.ToArray();
            scanlines.Length.Should().Be(240 * (1 + 320 * 3), png);
            var rgb = Unfilter(scanlines, 320, 240);
            var colours = new HashSet<int>();
            for (var i = 0; i < rgb.Length; i += 3) colours.Add(rgb[i] << 16 | rgb[i + 1] << 8 | rgb[i + 2]);
            colours.Count.Should().BeGreaterThan(400, $"{png} should be a painted picture, not a flat fill");
        }
    }

    [Fact]
    public void Every_wav_is_22050_hz_mono_16_bit_pcm_of_3_to_6_seconds()
    {
        var wavs = Directory.GetFiles(P("Music Box"), "*.wav");
        wavs.Should().HaveCount(6);
        foreach (var wav in wavs)
        {
            var b = File.ReadAllBytes(wav);
            Encoding.ASCII.GetString(b, 0, 4).Should().Be("RIFF");
            BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(4)).Should().Be(b.Length - 8);
            Encoding.ASCII.GetString(b, 8, 8).Should().Be("WAVEfmt ");
            BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(16)).Should().Be(16);
            BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(20)).Should().Be(1, "PCM");
            BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(22)).Should().Be(1, "mono");
            var sampleRateHertz = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(24));
            sampleRateHertz.Should().Be(22050);
            BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(28)).Should().Be(22050 * 2);
            BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(32)).Should().Be(2);
            BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(34)).Should().Be(16);
            Encoding.ASCII.GetString(b, 36, 4).Should().Be("data");
            var dataBytes = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(40));
            dataBytes.Should().Be(b.Length - 44);
            var durationSeconds = dataBytes / 2.0 / sampleRateHertz;
            durationSeconds.Should().BeInRange(3, 6, wav);

            var peak = 0;
            for (var i = 44; i < b.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(i))));
            peak.Should().BeInRange(8000, 32767, "audible but never clipped hard");
            Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(b.Length - 2))).Should().BeLessThan(200, "the tune fades out without a click");
        }
    }

    [Fact]
    public void Total_size_is_under_12_megabytes()
    {
        var totalBytes = Directory.GetFiles(sky.Root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        totalBytes.Should().BeLessThan(12L * 1024 * 1024);
    }

    [Fact]
    public void A_second_call_returns_at_once_without_writing()
    {
        var before = Snapshot(sky.Root);
        SampleSky.Create(sky.Parent).Should().Be(sky.Root);
        Snapshot(sky.Root).Should().Equal(before);
    }

    [Fact]
    public void Content_is_deterministic()
    {
        var other = Path.Combine(Path.GetTempPath(), "FileScape-tests", "sky-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var root = SampleSky.Create(other);
            Hashes(root).Should().Equal(Hashes(sky.Root));
        }
        finally
        {
            var link = Path.Combine(other, SampleSky.RootName, "Shortcut to Garden");
            if (Directory.Exists(link)) Directory.Delete(link, recursive: false);
            if (Directory.Exists(other)) Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void An_existing_unmarked_starter_sky_is_refused_with_an_exception_and_left_untouched()
    {
        var parent = Path.Combine(Path.GetTempPath(), "FileScape-tests", "sky-own-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var mine = Directory.CreateDirectory(Path.Combine(parent, SampleSky.RootName)).FullName;
            File.WriteAllText(Path.Combine(mine, "README.md"), "my own readme");
            var before = Snapshot(mine);

            var act = () => SampleSky.Create(parent);

            act.Should().Throw<InvalidOperationException>().WithMessage("*not a FileScape sample*");
            Snapshot(mine).Should().Equal(before);
            File.ReadAllText(Path.Combine(mine, "README.md")).Should().Be("my own readme");
            Directory.GetFileSystemEntries(parent).Should().ContainSingle("no staging folder is created or left behind");
            SampleSky.IsSample(mine).Should().BeFalse();

            // A marker written by someone else (not ours) is not a sample either.
            File.WriteAllText(Path.Combine(mine, SampleSky.MarkerName), "something else entirely");
            SampleSky.IsSample(mine).Should().BeFalse();
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void An_existing_file_named_starter_sky_is_refused()
    {
        var parent = Path.Combine(Path.GetTempPath(), "FileScape-tests", "sky-file-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(parent);
            File.WriteAllText(Path.Combine(parent, SampleSky.RootName), "a file");
            var act = () => SampleSky.Create(parent);
            act.Should().Throw<InvalidOperationException>();
            File.ReadAllText(Path.Combine(parent, SampleSky.RootName)).Should().Be("a file");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void An_older_sample_is_refreshed_in_place_and_keeps_files_it_did_not_write()
    {
        var parent = Path.Combine(Path.GetTempPath(), "FileScape-tests", "sky-old-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var old = Directory.CreateDirectory(Path.Combine(parent, SampleSky.RootName)).FullName;
            File.WriteAllText(Path.Combine(old, SampleSky.MarkerName), "FileScape sample folder \"Starter Sky\". Safe to delete.\nversion=0\n");
            File.WriteAllText(Path.Combine(old, "my-note.txt"), "keep me");

            SampleSky.Create(parent).Should().Be(old);

            File.ReadAllText(Path.Combine(old, "my-note.txt")).Should().Be("keep me");
            File.ReadAllText(Path.Combine(old, SampleSky.MarkerName)).Should().Contain($"version={SampleSky.Version}");
            Files(Path.Combine(old, "Garden Photos")).Should().HaveCount(16);
        }
        finally
        {
            var link = Path.Combine(parent, SampleSky.RootName, "Shortcut to Garden");
            if (Directory.Exists(link)) Directory.Delete(link, recursive: false);
            Directory.Delete(parent, recursive: true);
        }
    }

    [WindowsFact]
    public void On_windows_a_junction_leads_to_the_garden()
    {
        var link = P("Shortcut to Garden");
        Directory.Exists(link).Should().BeTrue();
        File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint).Should().BeTrue();
        Files(link).Should().HaveCount(16);
    }

    private static List<(string Path, long Bytes, DateTime WrittenUtc)> Snapshot(string root) =>
        Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Where(e => !e.Contains("Shortcut to Garden", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(e => (e, File.Exists(e) ? new FileInfo(e).Length : -1, File.GetLastWriteTimeUtc(e)))
            .ToList();

    private static List<(string Path, string Sha256)> Hashes(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("Shortcut to Garden", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(root, f), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))))
            .OrderBy(t => t.Item1, StringComparer.Ordinal)
            .ToList();

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>Reverses PNG filters 0-4 (3 bytes per pixel) to recover the RGB rows.</summary>
    private static byte[] Unfilter(byte[] scanlines, int widthPixels, int heightPixels)
    {
        var stride = widthPixels * 3;
        var rgb = new byte[stride * heightPixels];
        for (var y = 0; y < heightPixels; y++)
        {
            var filter = scanlines[y * (stride + 1)];
            filter.Should().BeLessThanOrEqualTo(4);
            for (var i = 0; i < stride; i++)
            {
                int a = i >= 3 ? rgb[y * stride + i - 3] : 0;
                int b = y > 0 ? rgb[(y - 1) * stride + i] : 0;
                int c = i >= 3 && y > 0 ? rgb[(y - 1) * stride + i - 3] : 0;
                var p = a + b - c;
                int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                var predictor = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => pa <= pb && pa <= pc ? a : pb <= pc ? b : c,
                    _ => 0,
                };
                rgb[y * stride + i] = (byte)(scanlines[y * (stride + 1) + 1 + i] + predictor);
            }
        }

        return rgb;
    }
}
