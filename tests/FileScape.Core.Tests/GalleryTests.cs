using System.Buffers.Binary;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using FileScape.Core.Samples;
using FileScape.Core.Scanning;
using FileScape.Core.Shell;

namespace FileScape.Core.Tests;

/// <summary>The gallery sample with its stress cases, written once into an owned temporary directory.</summary>
public sealed class GallerySampleFixture : IDisposable
{
    public GallerySampleFixture()
    {
        Parent = Path.Combine(Path.GetTempPath(), "FileScape-tests", "gallery-" + Guid.NewGuid().ToString("N")[..8]);
        Root = GallerySample.Create(Parent, stress: true);
    }

    public string Parent { get; }

    public string Root { get; }

    public void Dispose()
    {
        if (Directory.Exists(Parent)) Directory.Delete(Parent, recursive: true);
    }
}

/// <summary>
/// The picture gallery prototype (PLANET-96, AD-78): picture sizes read from headers before decoding, the gallery
/// marker, the viewer's picture list and verdicts from metadata, and the mixed sample folder it is played on.
/// </summary>
public sealed class GalleryTests(GallerySampleFixture gallery) : IClassFixture<GallerySampleFixture>
{
    private string P(params string[] parts) => Path.Combine([gallery.Root, .. parts]);

    [Fact]
    public void Header_sizes_match_what_was_written()
    {
        foreach (var (name, w, h) in GallerySample.Viewable)
        {
            ImageHeader.TryReadSize(File.ReadAllBytes(P(name))).Should().Be((w, h), name);
        }

        using var f = File.OpenRead(P(GallerySample.HugeDimensionsName));
        var head = new byte[64];
        f.ReadExactly(head);
        ImageHeader.TryReadSize(head).Should().Be((GallerySample.GiantWidth, GallerySample.GiantHeight), "the giant scan is refused from its header alone");
    }

    [Fact]
    public void Jpeg_and_webp_headers_are_read()
    {
        // A minimal JPEG preamble: SOI, an APP0 segment, then SOF0 (height 300, width 400).
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0x2C, 0x01, 0x90, 0x03];
        ImageHeader.TryReadSize(jpeg).Should().Be((400, 300));
        // DHT (C4) is not a frame header: skipped.
        byte[] dhtFirst = [0xFF, 0xD8, 0xFF, 0xC4, 0x00, 0x03, 0x00, 0xFF, 0xC2, 0x00, 0x11, 0x08, 0x00, 0x10, 0x00, 0x20, 0x03];
        ImageHeader.TryReadSize(dhtFirst).Should().Be((32, 16));
        var webp = new byte[30];
        "RIFF"u8.CopyTo(webp);
        "WEBPVP8X"u8.CopyTo(webp.AsSpan(8));
        webp[24] = 0x7F; webp[25] = 0x07; // width − 1 = 1919
        webp[27] = 0x37; webp[28] = 0x04; // height − 1 = 1079
        ImageHeader.TryReadSize(webp).Should().Be((1920, 1080));
    }

    [Fact]
    public void Damaged_or_unknown_headers_give_null_and_never_throw()
    {
        ImageHeader.TryReadSize([]).Should().BeNull();
        ImageHeader.TryReadSize("hello, world"u8.ToArray()).Should().BeNull();
        ImageHeader.TryReadSize([0xFF, 0xD8, 0xFF]).Should().BeNull();
        ImageHeader.TryReadSize([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0]).Should().BeNull();
        var zero = new byte[24];
        File.ReadAllBytes(P("dawn-over-the-low-hills.png")).AsSpan(0, 16).CopyTo(zero);
        ImageHeader.TryReadSize(zero).Should().BeNull("a zero width is not a picture");
        var rng = new Random(7);
        for (var i = 0; i < 2000; i++)
        {
            var junk = new byte[rng.Next(0, 64)];
            rng.NextBytes(junk);
            if (junk.Length > 2 && i % 2 == 0) { junk[0] = 0xFF; junk[1] = 0xD8; }
            var act = () => ImageHeader.TryReadSize(junk);
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void The_sample_is_a_mixed_gallery_with_its_stress_cases()
    {
        var files = Directory.GetFiles(gallery.Root).Select(Path.GetFileName).ToHashSet();
        files.Should().Contain(GalleryRules.MarkerName).And.Contain("README.md").And.Contain("captions.txt").And.Contain("palette.csv").And.Contain("gallery-chime.wav").And.Contain("old-prints.zip");
        Directory.GetFiles(P("Sketches")).Should().HaveCount(2);
        new FileInfo(P(GallerySample.OversizeName)).Length.Should().BeGreaterThan(GalleryRules.MaxPictureBytes);
        new FileInfo(P(GallerySample.HugeDimensionsName)).Length.Should().BeLessThan(2 * 1024 * 1024, "small on disk, huge decoded");
        // The playable part (no stress files) stays small: it is written into the app's own data folder.
        var playable = GallerySample.Viewable.Sum(v => new FileInfo(P(v.Name)).Length);
        playable.Should().BeLessThan(6L * 1024 * 1024);
        var wav = File.ReadAllBytes(P("gallery-chime.wav"));
        BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)).Should().Be(wav.Length - 44);
    }

    [Fact]
    public void Writing_again_keeps_our_folder_and_refuses_anyone_elses()
    {
        GallerySample.Create(gallery.Parent, stress: true).Should().Be(gallery.Root);
        var other = Path.Combine(Path.GetTempPath(), "FileScape-tests", "gallery-other-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(other, GallerySample.RootName));
            File.WriteAllText(Path.Combine(other, GallerySample.RootName, "mine.txt"), "someone's");
            var act = () => GallerySample.Create(other);
            act.Should().Throw<InvalidOperationException>();
            Directory.GetFiles(Path.Combine(other, GallerySample.RootName)).Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void The_marker_makes_a_gallery_and_the_viewer_steps_through_pictures_only()
    {
        var (tree, _) = ScanRunner.Run(new DirectoryScanner(gallery.Root));
        var root = tree.Nodes.First(n => n.ParentId < 0).Id;
        GalleryRules.IsGallery(tree, root).Should().BeTrue();
        GalleryRules.IsGallery(tree, tree.Find("Sketches").Id).Should().BeFalse("a gallery's subfolders are ordinary unless marked");
        var pictures = GalleryRules.Pictures(tree, root, includeHidden: false).Select(id => tree[id].Name).ToList();
        pictures.Should().Equal(new[] { "aurora-print-large.png", GallerySample.DamagedName, "dawn-over-the-low-hills.png", GallerySample.HugeDimensionsName, "harbour-of-lanterns-wide.png", "lantern-tower-tall.png", "midnight-sunflowers.png", "mist-in-the-blue-valley.png", "postage-stamp.bmp", GallerySample.OversizeName, "two-moons-over-the-orchard.png" });
        GalleryRules.Verdict(tree.Find(GallerySample.OversizeName)).Should().Be(GalleryRules.PictureVerdict.TooLarge);
        GalleryRules.Verdict(tree.Find("dawn-over-the-low-hills.png")).Should().Be(GalleryRules.PictureVerdict.Viewable);
        GalleryRules.IsPicture("A.JPEG").Should().BeTrue();
        GalleryRules.IsPicture("notes.txt").Should().BeFalse();
    }
}

/// <summary>
/// Windows' own thumbnails (PLANET-131, AD-86): <see cref="ShellThumbnails"/> asks the system thumbnail cache and the
/// file type's handler for a picture, fitted to the size asked with its shape kept, opaque, and nothing for a file that
/// has no thumbnail (never an icon instead). Windows only: the shell is what is tested.
/// </summary>
public sealed class ShellThumbnailTests(GallerySampleFixture gallery) : IClassFixture<GallerySampleFixture>
{
    [WindowsFact]
    public void Windows_gives_a_fitted_opaque_thumbnail_of_a_picture_and_nothing_for_other_files()
    {
        foreach (var (name, w, h) in GallerySample.Viewable.Take(3))
        {
            var t = ShellThumbnails.Get(Path.Combine(gallery.Root, name), 96, cacheOnly: false);
            t.Should().NotBeNull(name);
            Math.Max(t!.Width, t.Height).Should().BeLessThanOrEqualTo(96, name);
            ((float)t.Width / t.Height).Should().BeApproximately((float)w / h, 0.05f * w / h, $"{name} keeps its shape");
            t.Rgba.Length.Should().Be(t.Width * t.Height * 4);
            Enumerable.Range(0, t.Width * t.Height).All(i => t.Rgba[i * 4 + 3] == 255).Should().BeTrue("a photo's thumbnail is opaque");
            Enumerable.Range(0, t.Width * t.Height).Any(i => t.Rgba[i * 4] + t.Rgba[i * 4 + 1] + t.Rgba[i * 4 + 2] > 30).Should().BeTrue("not blank");
        }

        ShellThumbnails.Get(Path.Combine(gallery.Root, "captions.txt"), 96, cacheOnly: false).Should().BeNull("a text file has no thumbnail, and never an icon instead");
        ShellThumbnails.Get(Path.Combine(gallery.Root, "no-such-picture.png"), 96, cacheOnly: false).Should().BeNull();
        ShellThumbnails.GetAsync(Path.Combine(gallery.Root, GallerySample.Viewable[0].Name), 256).Result.Should().NotBeNull("the worker threads answer too");
    }
}
