using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// The overview's miniature worlds' looks (PLANET-92, AD-77): varied and recognisable from what a folder holds, stable
/// for the same folder whatever order it was listed in (never from node ids), and no longer one pink for every folder
/// of folders.
/// </summary>
public sealed class OverviewWorldLookTests
{
    private static int Find(FsTree tree, string name) => tree.Nodes.Single(n => n.Name == name).Id;

    /// <summary>Hue distance round the wheel (0…0.5).</summary>
    private static float HueGap(Vector3 a, Vector3 b)
    {
        var d = MathF.Abs(OverviewWorldLook.HueOf(a) - OverviewWorldLook.HueOf(b));
        return MathF.Min(d, 1 - d);
    }

    [Fact]
    public void A_folders_look_is_the_same_however_its_tree_was_listed()
    {
        // The same folders, in the second tree after an extra folder listed first: different node ids, the same names.
        var names = Enumerable.Range(0, 12).Select(i => $"folder {i}").ToList();
        FsTree Build(bool extra)
        {
            var root = new Spec("root");
            if (extra) root.Dir(new Spec("0 first").Dir(new Spec("x")).Dir(new Spec("y")).File("z.txt"));
            foreach (var n in names) root.Dir(new Spec(n).File("a.png").File("b.txt").Dir(new Spec("inner")));
            return root.Build();
        }

        var t1 = Build(extra: false);
        var t2 = Build(extra: true);
        foreach (var n in names)
        {
            Find(t1, n).Should().NotBe(Find(t2, n), "the test needs different ids");
            OverviewWorldLook.Of(t1, Find(t1, n)).Should().Be(OverviewWorldLook.Of(t2, Find(t2, n)), n);
        }
    }

    [Fact]
    public void Folders_of_folders_are_varied_and_not_one_pink()
    {
        var root = new Spec("root");
        for (var i = 0; i < 40; i++) root.Dir(new Spec($"system {i:D2}").Dir(new Spec("a")).Dir(new Spec("b")));
        var tree = root.Build();
        var looks = tree[0].Children.Select(c => OverviewWorldLook.Of(tree, c)).ToList();
        // Hue in twelfths of the wheel: no twelfth holds more than a third of them, and at least five are used.
        var buckets = looks.GroupBy(l => (int)(OverviewWorldLook.HueOf(l.Base) * 12) % 12).ToList();
        buckets.Count.Should().BeGreaterThanOrEqualTo(5);
        buckets.Max(g => g.Count()).Should().BeLessThanOrEqualTo(looks.Count / 3);
        looks.Select(l => l.Base).Distinct().Count().Should().Be(looks.Count, "every world its own shade");
        // The old Hearth pink: hue ≈ 0.02 (≈ 7°, a salmon) ± 0.04 of jitter, saturation ≈ 0.5, bright. Every such world
        // was in that region; now at most the rust family's share is near it.
        bool Pinkish(Vector3 c)
        {
            var h = OverviewWorldLook.HueOf(c);
            return (h > 0.96f || h < 0.07f) && Saturation(c) > 0.35f && MathF.Max(c.X, MathF.Max(c.Y, c.Z)) > 0.85f;
        }

        looks.Count(l => Pinkish(l.Base)).Should().BeLessThanOrEqualTo(looks.Count / 5);
    }

    [Fact]
    public void What_a_folder_holds_picks_its_colour_family()
    {
        var root = new Spec("root")
            .Dir(new Spec("Photos").File("1.jpg").File("2.jpg").File("3.png").File("4.jpg"))
            .Dir(new Spec("Source").File("a.cs").File("b.cs").File("c.cs").File("d.cs"))
            .Dir(new Spec("Papers").File("a.pdf").File("b.docx").File("c.pdf").File("d.pdf"))
            .Dir(new Spec("Mixed").File("a.jpg").File("b.cs").File("c.pdf").File("d.mp3"));
        var tree = root.Build();
        var photos = OverviewWorldLook.Of(tree, Find(tree, "Photos"));
        var source = OverviewWorldLook.Of(tree, Find(tree, "Source"));
        var papers = OverviewWorldLook.Of(tree, Find(tree, "Papers"));
        var mixed = OverviewWorldLook.Of(tree, Find(tree, "Mixed"));
        HueGap(photos.Base, source.Base).Should().BeGreaterThan(0.1f);
        HueGap(source.Base, papers.Base).Should().BeGreaterThan(0.1f);
        HueGap(photos.Base, papers.Base).Should().BeGreaterThan(0.1f);
        OverviewWorldLook.HueOf(source.Base).Should().BeInRange(0.04f, 0.16f, "code is amber");
        OverviewWorldLook.HueOf(papers.Base).Should().BeInRange(0.54f, 0.66f, "documents are blue");
        photos.Pattern.Should().Be(WorldPattern.Spots, "one kind of file: spotted");
        mixed.Pattern.Should().Be(WorldPattern.Swirl, "many kinds: swirled");
    }

    [Fact]
    public void Patterns_vary_and_phases_spread()
    {
        var root = new Spec("root");
        for (var i = 0; i < 30; i++)
        {
            var d = new Spec($"d{i}");
            for (var j = 0; j < i % 4 * 20; j++) d.Dir(new Spec($"c{j}"));
            if (i % 3 == 0) d.File("x.png");
            root.Dir(d);
        }

        var tree = root.Build();
        var looks = tree[0].Children.Select(c => OverviewWorldLook.Of(tree, c)).ToList();
        looks.Select(l => l.Pattern).Distinct().Count().Should().BeGreaterThanOrEqualTo(3);
        looks.Select(l => (int)(l.Phase * 4)).Distinct().Count().Should().Be(4, "phases spread over the range");
    }

    private static float Saturation(Vector3 c)
    {
        var max = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        var min = MathF.Min(c.X, MathF.Min(c.Y, c.Z));
        return max <= 0 ? 0 : (max - min) / max;
    }
}
