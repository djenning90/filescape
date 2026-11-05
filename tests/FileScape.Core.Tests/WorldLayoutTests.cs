using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Places;

namespace FileScape.Core.Tests;

/// <summary>
/// The aerial world layout (AERIAL-02, AD-26): every known folder is an island; islands never overlap; a place's child
/// folders lie ahead of it (its north) in natural order west → east, like its gates; its parent lies behind it (south);
/// and discovering folders deeper down does not move the places around them.
/// </summary>
public class WorldLayoutTests
{
    private static Spec Sample(bool deeper = false)
    {
        var docs = new Spec("Documents").File("a.txt").Dir(new Spec("Leg 10").File("x")).Dir(new Spec("Leg 2")).Dir(new Spec("Leg 1"));
        var photos = Spec.WithFiles("Photos", 40).Dir(new Spec("2024").File("p.jpg")).Dir(new Spec("2025").File("q.jpg"));
        if (deeper) photos.Dirs[0].Dir(new Spec("raw").File("r.cr2")).Dir(new Spec("edited").File("e.jpg"));
        var wide = Spec.WithDirs("Wide", 70);
        var chain = Spec.Chain("Chain", 6);
        return new Spec("root").Dir(docs).Dir(photos).Dir(wide).Dir(chain).Dir(Spec.WithFiles("Music", 300));
    }

    private static Dictionary<string, WorldIsland> ByPath(FsTree t, WorldMap m) => m.Islands.Values.ToDictionary(i => t.FullPath(i.Id), i => i);

    [Fact]
    public void Every_folder_is_an_island_and_none_overlap()
    {
        var t = Sample().Build();
        var m = WorldLayout.Compute(t);
        m.Islands.Count.Should().Be(t.Nodes.Count(n => n.IsDirectory));
        var list = m.Islands.Values.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            for (var j = i + 1; j < list.Count; j++)
            {
                var d = Vector2.Distance(list[i].Center, list[j].Center);
                d.Should().BeGreaterThanOrEqualTo(list[i].Radius + list[j].Radius, $"{t.FullPath(list[i].Id)} and {t.FullPath(list[j].Id)} must not overlap");
            }
        }
    }

    [Fact]
    public void Children_lie_ahead_in_gate_order_and_the_parent_lies_behind()
    {
        var t = Sample().Build();
        var m = WorldLayout.Compute(t);
        foreach (var parent in m.Islands.Values.Where(i => i.ParentId >= 0))
        {
            var frame = m.FrameOf(parent.Id);
            var kids = m.Islands.Values.Where(i => i.ParentId == parent.Id).ToList();
            // Ahead: north of the place in its own frame (local z < 0)...
            foreach (var k in kids) frame.ToLocal(k.Center).Y.Should().BeLessThan(0, $"{t[k.Id].Name} lies ahead of {t[parent.Id].Name}");
            // ...in natural order from west (left) to east (right), like the gates (ORD-01).
            // (an angular order: a wide folder's children are in rows, so a later row's x is wider for the same angle)
            var byName = kids.OrderBy(k => t[k.Id].Name, Interaction.NaturalNameOrder.Instance).Select(k => { var l = frame.ToLocal(k.Center); return MathF.Round(MathF.Atan2(l.X, -l.Y), 3); }).ToList();
            byName.Should().BeInAscendingOrder($"children of {t[parent.Id].Name} run west → east in name order");
            // The parent is straight behind (the ▲ gate is south in every place).
            var back = frame.ToLocal(m.Islands[parent.ParentId].Center);
            back.Y.Should().BeGreaterThan(0);
            MathF.Abs(back.X).Should().BeLessThan(back.Y * 0.01f + 0.01f, "the parent is due south");
        }
    }

    [Fact]
    public void Frames_round_trip_and_angles_convert()
    {
        var f = new PlaceFrame(new Vector2(120, -40), 37f);
        var p = new Vector2(-3.5f, 88f);
        Vector2.Distance(f.ToWorld(f.ToLocal(p)), p).Should().BeLessThan(1e-3f);
        f.ToLocal(f.Center).Length().Should().BeLessThan(1e-4f);
        var north = new Vector2(PlaceGeometry.Direction(37f).X, PlaceGeometry.Direction(37f).Z);
        var ahead = f.ToLocal(f.Center + north * 10);
        ahead.X.Should().BeApproximately(0, 1e-3f);
        ahead.Y.Should().BeApproximately(-10, 1e-3f, "the frame's north is local −Z");
        f.AngleToWorld(f.AngleToLocal(200f)).Should().BeApproximately(200f, 1e-4f);
    }

    [Fact]
    public void Discovering_folders_deeper_down_does_not_move_their_neighbours()
    {
        var a = Sample().Build();
        var b = Sample(deeper: true).Build();
        var ma = ByPath(a, WorldLayout.Compute(a));
        var mb = ByPath(b, WorldLayout.Compute(b));
        // Two new folders under Photos/2024: Photos' own subtree may re-arrange; every other place stays put.
        foreach (var (path, island) in ma)
        {
            if (path.Contains("Photos", StringComparison.Ordinal)) continue;
            Vector2.Distance(mb[path].Center, island.Center).Should().BeLessThan(0.01f, $"{path} keeps its place");
        }

        mb.Keys.Count(k => k.EndsWith("raw", StringComparison.Ordinal) || k.EndsWith("edited", StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public void A_size_limit_drops_the_deepest_folders_and_says_how_many()
    {
        var t = Sample().Build();
        var m = WorldLayout.Compute(t, parameters: WorldLayoutParams.Default with { MaxIslands = 20 });
        m.Islands.Count.Should().Be(20);
        m.Truncated.Should().Be(t.Nodes.Count(n => n.IsDirectory) - 20);
        m.Islands.Values.Sum(i => i.HiddenChildren).Should().BeGreaterThan(0, "the parents of dropped folders say so");
        m.Islands.Values.Max(i => i.Depth).Should().BeLessThanOrEqualTo(2, "breadth first: the nearest levels are kept");
    }

    [Fact]
    public void Unknown_folders_are_small_veiled_islands_and_large_folders_get_room()
    {
        var unknown = new Spec("not-yet") { State = DirScanState.Pending };
        var t = new Spec("root").Dir(unknown).Dir(Spec.WithDirs("Wide", 70)).Build();
        var m = WorldLayout.Compute(t);
        m.Islands[t.Find("not-yet").Id].Radius.Should().Be(WorldLayoutParams.Default.UnknownRadius);
        m.Islands[t.Find("Wide").Id].Radius.Should().BeGreaterThan(50f, "70 gates need tiers: a bigger place");
    }

    [Fact]
    public void Layout_is_deterministic_and_fast_for_thousands_of_folders()
    {
        var big = new Spec("root");
        for (var i = 0; i < 40; i++)
        {
            var d = new Spec($"d{i}");
            for (var j = 0; j < 30; j++) d.Dir(new Spec($"e{j}").Dir(new Spec("f")));
            big.Dir(d);
        }

        var t = big.Build();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var m1 = WorldLayout.Compute(t);
        var ms = sw.Elapsed.TotalMilliseconds;
        var m2 = WorldLayout.Compute(t);
        m1.Islands.Count.Should().Be(2441);
        m1.Islands.Values.All(i => m2.Islands[i.Id] == i).Should().BeTrue();
        ms.Should().BeLessThan(150, "laid out on the main thread when the explored world changes (throttled)");
    }
}
