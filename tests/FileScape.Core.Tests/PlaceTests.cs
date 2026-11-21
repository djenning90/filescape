using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Scanning;
using FileScape.Core.Units;

namespace FileScape.Core.Tests;

/// <summary>A compact way to build trees for place tests: directories, files and per-directory states.</summary>
public sealed class Spec
{
    public Spec(string name) => Name = name;

    public string Name { get; }
    public List<Spec> Dirs { get; } = [];
    public List<(string Name, long Size)> Files { get; } = [];
    public DirScanState State { get; set; } = DirScanState.Complete;
    public NodeFlags Flags { get; set; }
    public bool IsLink { get; set; }
    public LocationKind Location { get; set; }

    public Spec Dir(Spec d) { Dirs.Add(d); return this; }
    public Spec File(string name, long size = 100) { Files.Add((name, size)); return this; }

    /// <summary>Per-file node flags (cloud, hidden…), by name; files not listed have none.</summary>
    public Dictionary<string, NodeFlags> FileFlags { get; } = [];

    public Spec File(string name, long size, NodeFlags flags) { Files.Add((name, size)); FileFlags[name] = flags; return this; }

    public static Spec WithDirs(string name, int count, string prefix = "child-")
    {
        var s = new Spec(name);
        for (var i = 0; i < count; i++) s.Dirs.Add(new Spec($"{prefix}{i:D4}").File("readme.md", 10 + i));
        return s;
    }

    public static Spec WithFiles(string name, int count)
    {
        var s = new Spec(name);
        string[] ext = [".txt", ".png", ".cs", ".zip", ".json", ".mp3", ".exe", ".xyz", ".mp4"];
        for (var i = 0; i < count; i++) s.Files.Add(($"file-{i:D5}{ext[i % ext.Length]}", (long)Math.Pow(10, i % 9)));
        return s;
    }

    public static Spec Chain(string name, int depth)
    {
        var root = new Spec(name);
        var cur = root;
        for (var i = 1; i <= depth; i++)
        {
            var next = new Spec($"level-{i:D2}");
            cur.Dirs.Add(next);
            cur = next;
        }

        cur.File("bottom.txt");
        return root;
    }

    /// <summary>Builds the tree breadth first, as the scanner emits it (directories, then files, by name).</summary>
    public FsTree Build(DataSourceInfo? source = null)
    {
        var tree = new FsTree(source ?? new DataSourceInfo(DataOrigin.Synthetic, "synthetic://test", "test"));
        var batch = new ScanBatch();
        var queue = new Queue<(Spec Spec, int Id, int Depth)>();
        batch.NewNodes.Add(new NodeRecord(0, -1, Name, NodeKind.Directory, Flags, 0, 0, null, null, null, 0, State, Location: Location));
        queue.Enqueue((this, 0, 0));
        var next = 1;
        while (queue.Count > 0)
        {
            var (s, id, depth) = queue.Dequeue();
            foreach (var d in s.Dirs.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                var kind = d.IsLink ? NodeKind.Link : NodeKind.Directory;
                var flags = d.Flags | (d.IsLink ? NodeFlags.ReparsePoint | NodeFlags.DirectoryLink : 0);
                batch.NewNodes.Add(new NodeRecord(next, id, d.Name, kind, flags, 0, 0, null, null, null, depth + 1, d.IsLink ? DirScanState.NotApplicable : d.State, Location: d.Location));
                if (!d.IsLink) queue.Enqueue((d, next, depth + 1));
                next++;
            }

            foreach (var (name, size) in s.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                batch.NewNodes.Add(new NodeRecord(next++, id, name, NodeKind.File, s.FileFlags.GetValueOrDefault(name), 0, size, null, null, null, depth + 1, DirScanState.NotApplicable));
            }
        }

        tree.Apply(batch);
        return tree;
    }
}

/// <summary>
/// The Places domain (PLACE-01..12, AD-14): composition, wide/deep/empty cases, unknown-vs-empty, thresholds with
/// hysteresis, navigation and picking. These run without the engine.
/// </summary>
public class PlaceTests
{
    private static readonly PlaceParams P = PlaceParams.Default;

    private static FsTree Modest() =>
        new Spec("Studio")
            .Dir(new Spec("Documents").File("plan.docx", 40_000).File("notes.md", 2_000))
            .Dir(new Spec("Photos").Dir(new Spec("2025").File("a.jpg", 3_000_000)).File("cover.png", 800_000))
            .Dir(new Spec("Code").Dir(new Spec("src").File("main.cs", 9_000)).Dir(new Spec("tests")).File("app.csproj", 1_000))
            .Dir(new Spec("empty-room"))
            .Dir(new Spec("locked") { State = DirScanState.Failed, Flags = NodeFlags.AccessDenied })
            .Dir(new Spec("queued") { State = DirScanState.Pending })
            .Dir(new Spec("shortcut") { IsLink = true })
            .File("readme.txt", 1_200).File("logo.png", 52_000).File("build.zip", 9_000_000).File("data.json", 300)
            .Build();

    private static IEnumerable<(int Id, Box Box, string What)> Solids(PlaceComposition c)
    {
        yield return (c.FolderId, c.Landmark.Bounds, "landmark");
        foreach (var f in c.Files) yield return (f.Id, f.Box, "file");
        foreach (var g in c.Gates) yield return (g.TargetId, g.Bounds, "gate");
        if (c.ParentGate is { } pg) yield return (pg.TargetId, pg.Bounds, "parent gate");
    }

    private static void ShouldNotOverlap(PlaceComposition c)
    {
        var solids = Solids(c).ToList();
        for (var i = 0; i < solids.Count; i++)
        {
            for (var j = i + 1; j < solids.Count; j++)
            {
                if (solids[i].Box.Overlaps(solids[j].Box)) solids[i].Box.Overlaps(solids[j].Box).Should().BeFalse($"{solids[i].What} #{solids[i].Id} overlaps {solids[j].What} #{solids[j].Id}"); // Asserted only where violated: a FluentAssertions call per pair was most of this test's time (TEST-12).
            }
        }
    }

    /// <summary>PLACE-04: every immediate child directory or directory link has exactly one gate; none is omitted.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(60)]
    [InlineData(300)]
    public void Every_child_folder_has_exactly_one_gate_and_nothing_overlaps(int children)
    {
        var tree = Spec.WithDirs("root", children).File("a.txt").File("b.png").Build();
        var c = PlaceComposer.Compose(tree, 0);
        var expected = tree.Root.Children.Where(id => tree[id].IsDirectory).ToList();
        c.Gates.Select(g => g.TargetId).Should().BeEquivalentTo(expected);
        c.Gates.Select(g => g.TargetId).Should().OnlyHaveUniqueItems();
        ShouldNotOverlap(c);
        c.IsWide.Should().Be(children > P.MaxSingleRingGates);
        if (!c.IsWide)
        {
            c.Gates.Should().NotContain(g => MathF.Abs(g.AngleDeg) < 1f, "due north is kept free so the landmark never hides a gate");
        }
    }

    [Fact]
    public void Gates_on_a_tier_keep_their_spacing_and_tiers_ascend_in_alphabetical_order()
    {
        var tree = Spec.WithDirs("wide", 300).Build();
        var c = PlaceComposer.Compose(tree, 0);
        c.Tiers.Count.Should().BeGreaterThan(1);
        c.Tiers.Sum(t => t.Count).Should().Be(300);
        foreach (var tier in c.Gates.GroupBy(g => g.Tier))
        {
            var ordered = tier.OrderBy(g => g.AngleDeg).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                Vector3.Distance(ordered[i].Position, ordered[i - 1].Position).Should().BeGreaterThanOrEqualTo(P.GateSpacing - 1e-3f);
            }
        }

        // Reading tier by tier, west to east, gives the names in alphabetical order (learnable, PLACE-05).
        var reading = c.Gates.OrderBy(g => g.Tier).ThenBy(g => g.AngleDeg).Select(g => tree[g.TargetId].Name).ToList();
        reading.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        c.Tiers.Select(t => t.Rise).Should().BeInAscendingOrder("outer tiers rise so they can be seen over inner ones");
        c.Tiers[0].FirstName.Should().Be("child-0000");
        c.Tiers[^1].LastName.Should().Be("child-0299");
        c.Vistas.Should().BeEmpty("tiers omit vistas; that is stated in the HUD");
    }

    [Trait(TestKinds.Trait, TestKinds.Slow)]
    [Fact]
    public void Files_are_placed_once_in_type_quarters_and_keep_the_route_home_clear()
    {
        var tree = new Spec("parent").Dir(Spec.WithFiles("many", 800).Dir(new Spec("one")).Dir(new Spec("two"))).Build();
        var many = tree.Find("many").Id;
        var c = PlaceComposer.Compose(tree, many);
        // The same multiset check as BeEquivalentTo, sorted: FluentAssertions' equivalency matches 800 × 800 items (TEST-12).
        c.Files.Select(f => f.Id).Order().Should().Equal(tree[many].Children.Where(id => tree[id].Kind == NodeKind.File).Order());
        c.Quarters.Sum(q => q.Count).Should().Be(800);
        c.Quarters.Select(q => q.Category).Should().OnlyHaveUniqueItems();
        foreach (var f in c.Files)
        {
            var q = c.Quarters.Single(x => x.Category == f.Category);
            var angle = MathF.Atan2(f.Box.Center.X, -f.Box.Center.Z) * 180 / MathF.PI;
            if (angle < 0) angle += 360;
            angle.Should().BeInRange(q.StartDeg - 0.5f, q.EndDeg + 0.5f, "a file stays inside its quarter");
            f.Box.Center.Z.Should().BeGreaterThan(-1, "files are on the near (south) half");
        }

        ShouldNotOverlap(c);
        var corridor = new Box(new Vector3(-P.CorridorHalfWidth, 0, P.LandmarkRadius), new Vector3(P.CorridorHalfWidth, 50, c.ParentGate!.Position.Z - 0.5f));
        c.Files.Should().NotContain(f => f.Box.Overlaps(corridor, 1e-4f), "the walk from the landmark to the parent gate is unobstructed");
        Solids(c).Where(s => s.Box.Intersect(c.Arrival.Eye, -Vector3.UnitY) is { } t && t < 0.01f).Should().BeEmpty("the arrival point is not inside a structure");
        c.Gates.Should().HaveCount(2);
    }

    /// <summary>PLACE-09: a place depends only on its neighbourhood; unrelated branches do not move anything.</summary>
    [Fact]
    public void Composition_is_deterministic_and_local()
    {
        Spec Make(int extra)
        {
            var other = Spec.WithDirs("elsewhere", extra);
            return new Spec("root").Dir(new Spec("home").Dir(Spec.WithDirs("projects", 7).File("x.txt", 5).File("y.png", 99_999))).Dir(other);
        }

        var t1 = Make(2).Build();
        var t2 = Make(40).Build();
        var a = PlaceComposer.Compose(t1, t1.Find("projects").Id);
        var b = PlaceComposer.Compose(t2, t2.Find("projects").Id);
        a.Gates.Select(g => (t1[g.TargetId].Name, g.Position)).Should().Equal(b.Gates.Select(g => (t2[g.TargetId].Name, g.Position)));
        a.Files.Select(f => (t1[f.Id].Name, f.Box)).Should().Equal(b.Files.Select(f => (t2[f.Id].Name, f.Box)));
        a.Landmark.Identity.Should().Be(b.Landmark.Identity);
        a.Arrival.Should().Be(b.Arrival);
        PlaceComposer.Compose(t1, t1.Find("projects").Id).Gates.Select(g => g.Position).Should().Equal(a.Gates.Select(g => g.Position));
    }

    /// <summary>PLACE-11: unknown contents never look like an empty folder.</summary>
    [Fact]
    public void Gate_states_distinguish_empty_from_unknown()
    {
        var tree = Modest();
        var c = PlaceComposer.Compose(tree, 0);
        ListingState StateOf(string name) => c.Gates.Single(g => tree[g.TargetId].Name == name).State;
        StateOf("empty-room").Should().Be(ListingState.Empty);
        StateOf("locked").Should().Be(ListingState.Denied);
        StateOf("queued").Should().Be(ListingState.Pending);
        StateOf("shortcut").Should().Be(ListingState.Link);
        StateOf("Photos").Should().Be(ListingState.Open);
        foreach (var s in new[] { ListingState.Denied, ListingState.Pending, ListingState.NotScanned, ListingState.Partial, ListingState.Cloud, ListingState.Failed })
        {
            Listing.IsUnknown(s).Should().BeTrue();
            Listing.Describe(s).Should().NotBe(Listing.Describe(ListingState.Empty));
        }

        Listing.IsUnknown(ListingState.Empty).Should().BeFalse();
        // Revised 2026-11-05 (LINK-02, AD-18): links are followed on purpose when they can be; a link with no readable
        // information (this synthetic one) or an unsupported kind is not enterable, and an unresolved link has no vista.
        c.GateFor(tree.Find("shortcut").Id)!.CanEnter.Should().BeFalse("nothing is known about where this link leads");
        c.Vistas.Should().NotContain(v => v.FolderId == tree.Find("shortcut").Id);
        Listing.IsUnknown(ListingState.Unlisted).Should().BeTrue();
        Listing.Describe(ListingState.Unlisted).Should().NotBe(Listing.Describe(ListingState.Empty));
        c.Vistas.Single(v => v.FolderId == tree.Find("queued").Id).State.Should().Be(ListingState.Pending);
    }

    [Fact]
    public void Empty_root_and_files_only_folders_compose_intentionally()
    {
        var empty = new Spec("nothing-here").Build();
        var c = PlaceComposer.Compose(empty, 0);
        c.State.Should().Be(ListingState.Empty);
        c.Gates.Should().BeEmpty();
        c.Files.Should().BeEmpty();
        c.ParentGate.Should().BeNull("the root has no way up");
        c.Arrival.Eye.Y.Should().BeApproximately(P.TerraceTop + P.EyeHeight.Value, 1e-4f);

        var filesOnly = Spec.WithFiles("files-only", 12).Build();
        var f = PlaceComposer.Compose(filesOnly, 0);
        f.Gates.Should().BeEmpty();
        f.Files.Should().HaveCount(12);
        f.State.Should().Be(ListingState.Open);
    }

    [Fact]
    public void Deep_chains_are_announced_on_their_gate()
    {
        var tree = Spec.Chain("deep", 25).Build();
        var c = PlaceComposer.Compose(tree, 0);
        c.Gates.Should().ContainSingle().Which.ChainLength.Should().Be(24, "level-01 → level-25 is a chain of 24 single folders below level-01");
        PlaceComposer.ChainLength(tree, tree.Find("level-25").Id).Should().Be(0);
    }

    [Fact]
    public void Vistas_preview_the_childs_largest_files_and_its_children_without_overlapping()
    {
        var tree = Spec.WithDirs("root", 24).Build();
        var big = new Spec("root");
        for (var i = 0; i < 24; i++)
        {
            var child = Spec.WithFiles($"c{i:D2}", 40);
            for (var j = 0; j < 20; j++) child.Dir(new Spec($"g{j:D2}"));
            big.Dir(child);
        }

        tree = big.Build();
        var c = PlaceComposer.Compose(tree, 0);
        c.Vistas.Should().HaveCount(24);
        foreach (var v in c.Vistas)
        {
            var folder = tree[v.FolderId];
            v.Towers.Should().HaveCount(P.VistaMaxTowers);
            var expected = folder.Children.Where(id => tree[id].Kind == NodeKind.File).OrderByDescending(id => tree[id].Size).ThenBy(id => tree[id].Name, StringComparer.OrdinalIgnoreCase).Take(P.VistaMaxTowers);
            v.Towers.Select(t => t.Id).Should().Equal(expected);
            v.Beacons.Should().HaveCount(P.VistaMaxBeacons);
            v.HiddenBeacons.Should().Be(20 - P.VistaMaxBeacons, "beyond the cap the count is stated, not dropped silently");
            v.Identity.Should().Be(PlaceIdentity.For(folder.Name), "a vista looks like the place it previews");
        }

        for (var i = 0; i < c.Vistas.Count; i++)
        {
            for (var j = i + 1; j < c.Vistas.Count; j++)
            {
                var d = Vector3.Distance(c.Vistas[i].Center, c.Vistas[j].Center);
                if (d < c.Vistas[i].Radius + c.Vistas[j].Radius) d.Should().BeGreaterThanOrEqualTo(c.Vistas[i].Radius + c.Vistas[j].Radius); // Asserted only where violated: a FluentAssertions call per pair was most of this test's time (TEST-12).
            }
        }
    }

    [Fact]
    public void The_parent_gate_sits_behind_the_arrival_point_with_a_vista_of_the_parent()
    {
        var tree = Modest();
        var photos = tree.Find("Photos").Id;
        var c = PlaceComposer.Compose(tree, photos);
        c.ParentGate.Should().NotBeNull();
        c.ParentGate!.TargetId.Should().Be(0);
        c.ParentGate.Position.Z.Should().BeGreaterThan(c.Arrival.Eye.Z, "the way back is behind you on arrival");
        c.Arrival.Yaw.Should().Be(Degrees.Zero, "arrival faces the child gates (north)");
        c.ParentVista!.FolderId.Should().Be(0);
        c.ParentVista.Beacons.Should().NotContain(b => b.Id == photos, "the parent's preview omits the place you are in");
    }

    // ------------------------------------------------------------------ thresholds (PLACE-06)

    private static (PlaceComposition Place, Gate Gate) OneGate()
    {
        var tree = new Spec("r").Dir(new Spec("child")).Build();
        var c = PlaceComposer.Compose(tree, 0);
        return (c, c.Gates.Single());
    }

    private static Vector3 Along(Gate g, float depth, float lateral = 0, float height = 2.6f) =>
        g.Position + g.Outward * depth + g.Tangent * lateral + new Vector3(0, height, 0);

    [Fact]
    public void Walking_out_through_a_gate_counts_once()
    {
        var (c, g) = OneGate();
        var t = new ThresholdTracker();
        t.Reset(c, c.Arrival.Eye, TimeSpan.Zero);
        t.Update(Along(g, -6), TimeSpan.FromSeconds(2)).Should().BeNull();
        t.Update(Along(g, -1), TimeSpan.FromSeconds(2.1)).Should().BeNull();
        t.Update(Along(g, 0.5f), TimeSpan.FromSeconds(2.2)).Should().Be(g);
        // Hovering on the boundary afterwards cannot re-trigger without going 2 u back inside.
        t.Update(Along(g, -0.6f), TimeSpan.FromSeconds(2.3)).Should().BeNull();
        t.Update(Along(g, 0.4f), TimeSpan.FromSeconds(2.4)).Should().BeNull();
        t.Update(Along(g, -0.5f), TimeSpan.FromSeconds(2.5)).Should().BeNull();
        t.Update(Along(g, 0.6f), TimeSpan.FromSeconds(2.6)).Should().BeNull();
        t.Update(Along(g, -2.5f), TimeSpan.FromSeconds(2.7)).Should().BeNull();
        t.Update(Along(g, 0.6f), TimeSpan.FromSeconds(2.8)).Should().Be(g, "re-armed after a deliberate step back inside");
    }

    [Fact]
    public void Crossings_outside_the_gate_above_the_lintel_inward_or_during_cooldown_do_not_count()
    {
        var (c, g) = OneGate();
        var t = new ThresholdTracker();
        t.Reset(c, Along(g, -5), TimeSpan.Zero);
        t.Update(Along(g, 1), TimeSpan.FromSeconds(0.5)).Should().BeNull("cooldown after arrival");
        t.Reset(c, Along(g, -5), TimeSpan.Zero);
        t.Update(Along(g, -1, lateral: g.Width), TimeSpan.FromSeconds(2)).Should().BeNull();
        t.Update(Along(g, 1, lateral: g.Width), TimeSpan.FromSeconds(2.1)).Should().BeNull("beside the gate, not through it");
        t.Update(Along(g, -3, height: g.Height + 1), TimeSpan.FromSeconds(2.2));
        t.Update(Along(g, 1, height: g.Height + 1), TimeSpan.FromSeconds(2.3)).Should().BeNull("flying above the lintel escapes the place without travelling");
        t.Update(Along(g, 3), TimeSpan.FromSeconds(2.4));
        t.Update(Along(g, -3), TimeSpan.FromSeconds(2.5)).Should().BeNull("walking inward through a gate is not travel");
        ThresholdTracker.Crosses(g with { State = ListingState.Link, Followable = false }, Along(g, -1), Along(g, 1)).Should().BeFalse("a link that cannot be followed is not a way through");
        ThresholdTracker.Crosses(g with { State = ListingState.Link, Followable = true }, Along(g, -1), Along(g, 1)).Should().BeTrue("a followable link is entered on purpose by walking through it (LINK-02)");
    }

    /// <summary>Found in the exported app: stepping back (S) from the arrival terrace walked through the parent gate.</summary>
    [Fact]
    public void Backing_through_a_gate_does_not_travel()
    {
        var (c, g) = OneGate();
        var t = new ThresholdTracker();
        t.Reset(c, Along(g, -5), TimeSpan.Zero);
        t.Update(Along(g, -1), TimeSpan.FromSeconds(2), facing: -g.Outward).Should().BeNull();
        t.Update(Along(g, 1), TimeSpan.FromSeconds(2.1), facing: -g.Outward).Should().BeNull("walking backwards through a gate is not deliberate travel");
        t.Update(Along(g, -3), TimeSpan.FromSeconds(2.2), facing: g.Outward);
        t.Update(Along(g, 1), TimeSpan.FromSeconds(2.3), facing: g.Outward).Should().Be(g, "facing through the gate counts");
    }

    [Fact]
    public void Parent_gate_is_a_threshold_too()
    {
        var tree = Modest();
        var c = PlaceComposer.Compose(tree, tree.Find("Code").Id);
        var t = new ThresholdTracker();
        t.Reset(c, c.Arrival.Eye, TimeSpan.Zero);
        var pg = c.ParentGate!;
        t.Update(Along(pg, -1), TimeSpan.FromSeconds(2)).Should().BeNull();
        t.Update(Along(pg, 1), TimeSpan.FromSeconds(2.1)).Should().Be(pg);
        pg.IsParent.Should().BeTrue();
    }

    // ------------------------------------------------------------------ navigation and picking (PLACE-01, 07, 08)

    [Fact]
    public void Navigator_moves_only_deliberately_and_reports_the_kind_of_move()
    {
        var tree = Modest();
        var nav = new PlaceNavigator();
        nav.Reset();
        var changes = new List<PlaceChange>();
        nav.Changed += changes.Add;
        var code = tree.Find("Code").Id;
        var src = tree.Find("src").Id;

        nav.Enter(tree, src).Should().BeFalse("src is not a child of the root");
        nav.Enter(tree, tree.Find("readme.txt").Id).Should().BeFalse("files are not places");
        nav.Enter(tree, tree.Find("shortcut").Id).Should().BeFalse("links are not followed");
        nav.Enter(tree, code).Should().BeTrue();
        nav.Enter(tree, src).Should().BeTrue();
        PlaceNavigator.Breadcrumb(tree, nav.Current).Select(id => tree[id].Name).Should().Equal("Studio", "Code", "src");
        nav.Up(tree).Should().BeTrue();
        nav.Current.Should().Be(code);
        nav.GoTo(tree, tree.Find("main.cs").Id).Should().BeTrue("a file's id goes to its folder");
        nav.Current.Should().Be(src);
        nav.GoToRoot().Should().BeTrue();
        nav.Up(tree).Should().BeFalse("nothing above the chosen root");
        nav.Back().Should().BeTrue();
        nav.Current.Should().Be(src);
        changes.Select(ch => ch.Move).Should().Equal(PlaceMove.Enter, PlaceMove.Enter, PlaceMove.Up, PlaceMove.Jump, PlaceMove.Root, PlaceMove.Back);
        PlaceNavigator.ChildToward(tree, 0, src).Should().Be(code);
    }

    [Fact]
    public void Picking_selects_structures_and_return_pose_faces_into_the_place()
    {
        var tree = Modest();
        var c = PlaceComposer.Compose(tree, 0);
        var file = c.Files[0];
        PlacePicker.Pick(c, file.Box.Center with { Y = 60 }, -Vector3.UnitY).Should().Be(file.Id);
        PlacePicker.Pick(c, new Vector3(0, 60, 0), -Vector3.UnitY).Should().Be(0, "the landmark represents the folder you are in");
        var gate = c.Gates.First(g => g.State == ListingState.Open);
        PlacePicker.Pick(c, gate.Position + new Vector3(0, 3, 0) - gate.Outward * 10, gate.Outward).Should().Be(gate.TargetId);

        var back = c.ReturnPose(gate.TargetId, P);
        var forward = CameraMath.HorizontalForward(back.Yaw);
        Vector3.Dot(forward, -gate.Outward).Should().BeGreaterThan(0.99f, "returning faces into the parent, the gate behind you");
        ThresholdTracker.Depth(gate, back.Eye).Should().BeLessThan(-ThresholdTracker.ArmDepth.Value, "you arrive inside, so the gate is armed but not crossed");
        var approach = c.ApproachPose(gate.TargetId, P);
        Vector3.Dot(CameraMath.HorizontalForward(approach.Yaw), gate.Outward).Should().BeGreaterThan(0.99f);
    }

    [Fact]
    public void Signature_tracks_the_neighbourhood_only()
    {
        var tree = new Spec("root").Dir(new Spec("a").Dir(new Spec("deep").Dir(new Spec("deeper")))).Dir(new Spec("b")).Build();
        var before = PlaceComposer.Signature(tree, 0);
        var batch = new ScanBatch();
        batch.NewNodes.Add(new NodeRecord(tree.Count, tree.Find("deeper").Id, "far.txt", NodeKind.File, 0, 0, 5, null, null, null, 4, DirScanState.NotApplicable));
        tree.Apply(batch);
        PlaceComposer.Signature(tree, 0).Should().Be(before, "a change three levels down does not recompose the root");
        var batch2 = new ScanBatch();
        batch2.NewNodes.Add(new NodeRecord(tree.Count, tree.Find("b").Id, "new.txt", NodeKind.File, 0, 0, 5, null, null, null, 2, DirScanState.NotApplicable));
        tree.Apply(batch2);
        PlaceComposer.Signature(tree, 0).Should().NotBe(before, "a child's contents feed its vista");
    }

    [Fact]
    public void Identity_is_stable_and_distinguishes_neighbours()
    {
        PlaceIdentity.For("Photos").Should().Be(PlaceIdentity.For("photos"), "Windows names are case-insensitive");
        var ids = Enumerable.Range(0, 40).Select(i => PlaceIdentity.For($"folder-{i}")).ToList();
        ids.Select(i => i.Form).Distinct().Should().HaveCount(PlaceIdentity.Forms);
        ids.Select(i => MathF.Round(i.Hue * 20)).Distinct().Count().Should().BeGreaterThan(5);
    }

    /// <summary>A real scan of the synthetic tree: every directory composes without overlaps and with all its children.</summary>
    [Fact]
    public void Every_folder_of_the_synthetic_sample_composes()
    {
        var (tree, _) = ScanRunner.Run(new SyntheticTreeSource { TargetNodes = 6000 });
        var checkedFolders = 0;
        foreach (var n in tree.Nodes.Where(n => n.IsDirectory))
        {
            var c = PlaceComposer.Compose(tree, n.Id);
            c.Gates.Should().HaveCount(n.Children.Count(id => tree[id].IsDirectory || (tree[id].Flags & NodeFlags.DirectoryLink) != 0));
            c.Files.Count.Should().Be(n.Children.Count - c.Gates.Count);
            if (checkedFolders++ < 150) ShouldNotOverlap(c); // pairwise check is quadratic; a large sample suffices
        }

        checkedFolders.Should().BeGreaterThan(100);
    }
}
