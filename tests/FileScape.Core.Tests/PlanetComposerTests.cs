using System.Diagnostics;
using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using FileScape.Core.Places;
using FileScape.Core.Scanning;

namespace FileScape.Core.Tests;

/// <summary>
/// Planet composition (Planets experience): theme, layout budgets, launchers and belt, collision sanity and — the main
/// claim — that everything on a planet can be walked to from the arrival.
/// </summary>
public class PlanetComposerTests
{
    private static readonly PlanetParams P = PlanetParams.Default;

    private static FsTree Modest() =>
        new Spec("Studio")
            .Dir(new Spec("Documents").File("plan.docx", 40_000).File("notes.md", 2_000))
            .Dir(new Spec("Photos").Dir(new Spec("2025").File("a.jpg", 3_000_000)).File("cover.png", 800_000))
            .Dir(new Spec("Code").Dir(new Spec("src").File("main.cs", 9_000)).Dir(new Spec("tests")).File("app.csproj", 1_000))
            .Dir(new Spec("empty-room"))
            .Dir(new Spec("queued") { State = DirScanState.Pending })
            .File("readme.txt", 1_200).File("logo.png", 52_000).File("build.zip", 9_000_000).File("data.json", 300)
            .Build();

    /// <summary>hub ─ C:\ ─ work ─ {app (project), docs ─ notes, to-notes (junction → notes)}.</summary>
    private static (FsTree Tree, int Work, int Notes, int Link) Connected(LinkStatus status = LinkStatus.Resolved)
    {
        var tree = new FsTree(new DataSourceInfo(DataOrigin.LiveFilesystem, "This computer", "test"));
        var b = new ScanBatch();
        void N(int id, int parent, string name, NodeKind kind, DirScanState state = DirScanState.Complete, NodeFlags flags = 0, long size = 0, LinkInfo? link = null, string? loc = null, LocationKind lk = LocationKind.None) =>
            b.NewNodes.Add(new NodeRecord(id, parent, name, kind, flags, 0, size, null, null, null, 0, state, link, loc, lk));
        N(0, -1, "This computer", NodeKind.Directory, loc: "", lk: LocationKind.Hub);
        N(1, 0, @"C:\", NodeKind.Directory, loc: @"C:\", lk: LocationKind.Drive);
        N(2, 1, "work", NodeKind.Directory);
        N(3, 2, "app", NodeKind.Directory);
        N(4, 2, "docs", NodeKind.Directory);
        N(5, 2, "to-notes", NodeKind.Link, DirScanState.NotApplicable, NodeFlags.ReparsePoint | NodeFlags.DirectoryLink,
            link: new LinkInfo(LinkKind.Junction, Reparse.TagMountPoint, @"C:\work\docs\notes", status, @"C:\work\docs\notes", status == LinkStatus.Resolved ? 7 : -1));
        N(6, 2, "linux-link", NodeKind.Link, DirScanState.NotApplicable, NodeFlags.ReparsePoint | NodeFlags.DirectoryLink,
            link: new LinkInfo(LinkKind.LinuxSymlink, 0xA000001D, "/elsewhere"));
        N(7, 4, "notes", NodeKind.Directory);
        N(8, 3, "src", NodeKind.Directory);
        N(9, 3, "App.csproj", NodeKind.File, DirScanState.NotApplicable, size: 2000);
        N(10, 3, "README.md", NodeKind.File, DirScanState.NotApplicable, size: 3000);
        N(11, 3, "Program.cs", NodeKind.File, DirScanState.NotApplicable, size: 400);
        N(12, 7, "today.md", NodeKind.File, DirScanState.NotApplicable, size: 50);
        N(13, 2, "todo.txt", NodeKind.File, DirScanState.NotApplicable, size: 50);
        tree.Apply(b);
        return (tree, 2, 7, 5);
    }

    private static FsTree Mixed(int perKind = 6)
    {
        var s = new Spec("mixed");
        string[] ext = [".txt", ".png", ".cs", ".zip", ".json", ".mp3", ".exe", ".xyz", ".mp4"];
        for (var k = 0; k < ext.Length; k++)
        {
            for (var i = 0; i < perKind + k; i++) s.File($"f{k}-{i:D3}{ext[k]}", 10L << (i % 30));
        }

        return new Spec("top").Dir(s.Dir(new Spec("a")).Dir(new Spec("b"))).Build();
    }

    // ------------------------------------------------------------------ theme

    [Fact]
    public void Theme_follows_what_the_folder_holds()
    {
        var (connected, work, _, _) = Connected();
        PlanetComposer.Compose(connected, 0).Theme.Should().Be(PlanetTheme.Hearth, "the hub is a crossroads");
        PlanetComposer.Compose(connected, 3).Theme.Should().Be(PlanetTheme.Workshop, "app has a .csproj");
        PlanetComposer.Compose(connected, work).Theme.Should().NotBe(PlanetTheme.Veiled);

        PlanetTheme Of(Spec s) => PlanetComposer.Compose(new Spec("top").Dir(s).Build(), 1).Theme;
        Of(new Spec("photos").File("a.png").File("b.jpg").File("c.gif").File("notes.txt")).Should().Be(PlanetTheme.Meadow);
        Of(new Spec("papers").File("a.pdf").File("b.docx").File("c.md").File("x.zip")).Should().Be(PlanetTheme.Library);
        Of(new Spec("node").File("package.json").File("index.js").File("a.png").File("b.png").File("c.png")).Should().Be(PlanetTheme.Workshop, "project evidence wins");
        Of(new Spec("rust").File("Cargo.toml")).Should().Be(PlanetTheme.Workshop);
        Of(new Spec("songs").File("a.mp3").File("b.flac")).Should().Be(PlanetTheme.Chime);
        Of(new Spec("films").File("a.mp4")).Should().Be(PlanetTheme.Theatre);
        Of(new Spec("bin").File("a.exe").File("b.zip").File("c.txt")).Should().Be(PlanetTheme.Vault, "archives and programs together");
        Of(new Spec("db").File("a.json").File("b.csv")).Should().Be(PlanetTheme.Crystal);
        Of(new Spec("mess").File("a.png").File("b.txt").File("c.mp3").File("d.zip").File("e.cs")).Should().Be(PlanetTheme.Wild);
        Of(new Spec("folders").Dir(new Spec("x")).Dir(new Spec("y"))).Should().Be(PlanetTheme.Hearth, "folders only");
        Of(new Spec("nothing")).Should().Be(PlanetTheme.Quiet);
        Of(new Spec("later") { State = DirScanState.Unlisted }).Should().Be(PlanetTheme.Veiled);
        Of(new Spec("queued") { State = DirScanState.Pending }).Should().Be(PlanetTheme.Veiled);
        Of(new Spec("locked") { State = DirScanState.Failed, Flags = NodeFlags.AccessDenied }).Should().Be(PlanetTheme.Veiled);
        Of(new Spec("cloud") { State = DirScanState.NotTraversed, Flags = NodeFlags.CloudNotTraversed }).Should().Be(PlanetTheme.Veiled);
    }

    [Fact]
    public void Quiet_and_veiled_planets_are_small_and_say_what_they_are()
    {
        var tree = new Spec("top").Dir(new Spec("nothing")).Dir(new Spec("later") { State = DirScanState.Unlisted }).Build();
        var quiet = PlanetComposer.Compose(tree, tree.Find("nothing").Id);
        quiet.Shape.Radius.Should().BeApproximately(10f, 0.01f);
        quiet.State.Should().Be(ListingState.Empty);
        quiet.IsIncomplete.Should().BeFalse();
        quiet.Shape.HillAmplitude.Should().BeLessThan(0.5f);
        var veiled = PlanetComposer.Compose(tree, tree.Find("later").Id);
        veiled.Shape.Radius.Should().BeApproximately(12f, 0.01f);
        veiled.IsIncomplete.Should().BeTrue("an unlisted folder is not empty");
        veiled.Summary.Should().Contain("not listed yet");
        veiled.Items.Should().BeEmpty();
        ShouldBeReachable(quiet);
        ShouldBeReachable(veiled);
    }

    // ------------------------------------------------------------------ layout

    [Fact]
    public void A_small_folder_has_its_arrival_landmark_overlook_launchers_and_groves()
    {
        var tree = Modest();
        var c = PlanetComposer.Compose(tree, 0);
        var R = c.Shape.Radius;
        R.Should().BeInRange(16f, 34f);
        c.Shape.HillAmplitude.Should().BeInRange(0.6f, 0.9f);
        c.Arrival.Up.Should().Be(Vector3.UnitY);
        c.Arrival.Facing.Should().Be(-Vector3.UnitZ);
        c.Shape.FlatSpots.Should().Contain(f => f.Direction == Vector3.UnitY && f.Radius >= 3.5f, "the arrival meadow is flat");

        // Landmark straight ahead, 35–45° from the pole; the overlook beside it.
        // 2026-11-12: the landmark stands ahead beyond the launcher rings (at least 40°).
        Deg(PlanetFrame.PolarOf(c.Landmark.Direction)).Should().BeGreaterThanOrEqualTo(39.9f);
        var outerRing = c.Launchers.Max(l => c.Surface.SurfaceDistance(l.Direction, Vector3.UnitY));
        c.Surface.SurfaceDistance(c.Landmark.Direction, Vector3.UnitY).Should().BeGreaterThan(outerRing + P.PadRadius + c.Landmark.Radius);
        foreach (var l in c.Launchers.Where(l => !l.IsParent)) MathF.Abs(MathF.IEEERemainder(PlanetFrame.AzimuthOf(c.Landmark.Direction) - PlanetFrame.AzimuthOf(l.Direction), 2 * MathF.PI)).Should().BeGreaterThan(0.1f, "the landmark stands between pads' bearings, so the walk out to it is clear");
        c.Landmark.Top.Should().BeApproximately(10f, 0.01f);
        var o = c.Overlook;
        o.Top.Should().BeApproximately(2.4f, 0.01f);
        o.Radius.Should().BeApproximately(2.2f, 0.01f);
        c.Surface.SurfaceDistance(o.Direction, c.Landmark.Direction).Should().BeLessThan(8f, "next to the landmark");
        o.Steps.Count.Should().BeInRange(6, 8);
        var heights = new[] { 0f }.Concat(o.Steps.Select(s => s.Top)).Append(o.Top).ToList();
        for (var i = 1; i < heights.Count; i++) (heights[i] - heights[i - 1]).Should().BeInRange(0.01f, 0.4f, "each stone rises at most 0.4 m");
        P.JumpHeight.Should().BeLessThan(o.Top, "a single jump does not reach the overlook");
        P.JumpBoostHeight.Should().BeGreaterThan(o.Top, "a jump and boost does");
        c.OverlookDirection.Should().Be(o.Direction);
        c.Items.Should().NotContain(i => c.Surface.SurfaceDistance(i.Direction, c.OverlookDirection) < c.InspectionRadius + i.Radius, "the inspection station is item-free");

        // Launchers: every child folder (5), in the band, with a body above each; the way back behind the arrival.
        var children = c.Launchers.Where(l => !l.IsParent).ToList();
        children.Select(l => l.TargetId).Should().BeEquivalentTo(tree.Root.Children.Where(id => tree[id].IsDirectory));
        foreach (var l in children)
        {
            c.Surface.SurfaceDistance(l.Direction, Vector3.UnitY).Should().BeApproximately(P.RingFirst, 0.05f, "five folders share the first ring round the arrival");
            l.PadRadius.Should().BeApproximately(1.3f, 0.01f);
            l.PadTop.Should().BeApproximately(0.2f, 0.01f);
            var body = c.SkyBodies[l.SkyBody];
            body.TargetId.Should().Be(l.TargetId);
            body.Launcher.Should().Be(c.Launchers.ToList().IndexOf(l));
            var lift = body.Position.Length() - R;
            lift.Should().BeInRange(17f, 31f);
            var bodyAz = PlanetFrame.AzimuthOf(Vector3.Normalize(body.Position));
            MathF.Abs(MathF.IEEERemainder(bodyAz - PlanetFrame.AzimuthOf(l.Direction), 2 * MathF.PI)).Should().BeLessThan(0.15f, "it hangs on the same bearing as its launcher");
            PlanetFrame.PolarOf(Vector3.Normalize(body.Position)).Should().BeGreaterThan(PlanetFrame.PolarOf(l.Direction), "beyond its pad, round the horizon");
            body.Radius.Should().BeInRange(3f, 8f);
            c.Signs.Should().Contain(s => s.TargetId == l.TargetId && s.Text == l.Name && s.Detail.Length > 0);
        }

        c.SkyBodies.Single(b => b.TargetId == tree.Find("queued").Id).Theme.Should().Be(PlanetTheme.Veiled);
        c.SkyBodies.Single(b => b.TargetId == tree.Find("queued").Id).State.Should().Be(ListingState.Pending);
        c.SkyBodies.Single(b => b.TargetId == tree.Find("empty-room").Id).Theme.Should().Be(PlanetTheme.Quiet);
        c.SkyBodies.Single(b => b.TargetId == tree.Find("Code").Id).Theme.Should().Be(PlanetTheme.Workshop);
        c.ParentLauncher.Should().BeNull("the root has no way back");
        c.OmittedFolders.Should().Be(0);
        c.SkyBodies.Should().NotContain(b => b.InBelt);

        // Files: all four shown individually, one grove per category, names in order within a grove.
        c.Items.Should().HaveCount(4);
        c.Items.Should().OnlyContain(i => i.Kind == PlanetItemKind.File && i.Scale >= 0.6f && i.Scale <= 1.6f);
        c.Groves.Select(g => g.Category).Should().OnlyHaveUniqueItems();
        c.Groves.Select(g => g.Count).Should().BeInDescendingOrder();
        c.Groves.Sum(g => g.Count).Should().Be(4);
        c.Groves.Should().OnlyContain(g => g.Hidden == 0 && g.Label.Contains(" · "));
        c.Groves.Should().HaveCount(c.Signs.Count(s => s.TargetId == -1));
        c.Counts.Should().Be(new PlanetChildCounts(5, 4, 0, 0));
        c.Summary.Should().StartWith("Here: 5 folders · 4 files");
        c.IsTruncated.Should().BeFalse();
        c.Decorations.Count.Should().BeInRange((int)(R * 1.0f), (int)(R * 1.6f) + 1);
        ShouldBeSane(c);
        ShouldBeReachable(c);
    }

    [Fact]
    public void A_child_planet_has_the_way_back_behind_the_arrival_and_returns_beside_the_launcher()
    {
        var tree = Modest();
        var code = tree.Find("Code").Id;
        var c = PlanetComposer.Compose(tree, code);
        var back = c.ParentLauncher!;
        back.TargetId.Should().Be(0);
        c.Surface.SurfaceDistance(back.Direction, Vector3.UnitY).Should().BeApproximately(P.RingFirst, 0.05f, "on the first launcher ring");
        MathF.Abs(MathF.Abs(PlanetFrame.AzimuthOf(back.Direction)) - MathF.PI).Should().BeLessThan(0.01f, "at 6 o'clock: behind the arrival, which faces −Z");
        var parentBody = c.SkyBodies[back.SkyBody];
        parentBody.IsParent.Should().BeTrue();
        parentBody.Position.Z.Should().BeGreaterThan(0);
        parentBody.Position.Y.Should().BeGreaterThan(c.Shape.Radius, "above and behind the arrival");

        // Coming back up from src: beside its launcher, facing away from it.
        var root = PlanetComposer.Compose(tree, 0);
        var launcher = root.LauncherFor(code)!;
        var pose = root.ArrivalFor(code);
        var d = root.Surface.SurfaceDistance(pose.Up, launcher.Direction);
        d.Should().BeInRange(launcher.PadRadius + 0.4f, launcher.PadRadius + 2f);
        Vector3.Dot(pose.Facing, pose.Up).Should().BeApproximately(0, 1e-3f);
        Vector3.Dot(pose.Facing, launcher.Direction - pose.Up).Should().BeLessThan(0, "facing away from the launcher");
        root.Surface.Near(pose.Up, 0.35f).Where(i => root.Surface.Obstacles[i].Top > 0.45f).Should().BeEmpty("the return point is clear");
        root.ArrivalFor(12345).Should().Be(root.Arrival);
        root.ArrivalFor(-1).Should().Be(root.Arrival);
    }

    [Fact]
    public void An_800_file_folder_shows_a_budget_of_items_and_piles_for_the_rest()
    {
        var tree = new Spec("parent").Dir(Spec.WithFiles("many", 800).Dir(new Spec("one")).Dir(new Spec("two"))).Build();
        var many = tree.Find("many").Id;
        var c = PlanetComposer.Compose(tree, many);
        var individual = c.Items.Where(i => i.Kind != PlanetItemKind.Pile).ToList();
        var piles = c.Items.Where(i => i.Kind == PlanetItemKind.Pile).ToList();
        individual.Count.Should().BeLessThanOrEqualTo(P.MaxItems);
        individual.Count.Should().BeGreaterThan(P.MaxItems - 5, "the budget is used");
        (individual.Count + piles.Sum(x => x.HiddenCount)).Should().Be(800, "every visible file is shown or in a pile");
        individual.Select(i => i.NodeId).Should().OnlyHaveUniqueItems();
        c.Groves.Should().HaveCount(9);
        foreach (var (g, gi) in c.Groves.Select((g, gi) => (g, gi)))
        {
            g.Shown.Should().BeGreaterThanOrEqualTo(2);
            var groveItems = c.Items.Where(i => i.Grove == gi).ToList();
            groveItems.Should().OnlyContain(i => i.Category == g.Category);
            groveItems.Count(i => i.Kind == PlanetItemKind.Pile).Should().Be(g.Hidden > 0 ? 1 : 0, "ONE pile per grove that has more");
            groveItems.Where(i => i.Kind == PlanetItemKind.Pile).Sum(i => i.HiddenCount).Should().Be(g.Hidden);
            groveItems.Count(i => i.Kind != PlanetItemKind.Pile).Should().Be(g.Shown);
            var names = groveItems.Where(i => i.NodeId >= 0).Select(i => tree[i.NodeId].Name).ToList();
            names.Should().BeInAscendingOrder(NaturalNameOrder.Instance);
            g.Label.Should().Be($"{PlanetComposer.CategoryLabel(g.Category)} · {g.Count:N0}");
        }

        c.Groves.Sum(g => g.Count).Should().Be(800);
        c.IsTruncated.Should().BeTrue();
        c.Shape.Radius.Should().BeInRange(16f, 34f);
        ShouldBeSane(c);
        ShouldBeReachable(c);
    }

    [Fact]
    public void Composing_an_800_file_folder_is_cheap()
    {
        var tree = new Spec("parent").Dir(Spec.WithFiles("many", 800).Dir(new Spec("one")).Dir(new Spec("two"))).Build();
        var many = tree.Find("many").Id;
        for (var i = 0; i < 5; i++) PlanetComposer.Compose(tree, many);
        const int runs = 20;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < runs; i++) PlanetComposer.Compose(tree, many);
        var perCompose = sw.Elapsed.TotalMilliseconds / runs;
        perCompose.Should().BeLessThan(25, $"composition must stay cheap (target < 5 ms; measured {perCompose:0.00} ms, with slack for a loaded test host)");
    }

    [Fact]
    public void A_240_folder_folder_has_its_launchers_a_belt_and_counts_the_rest()
    {
        var tree = new Spec("top").Dir(Spec.WithDirs("wide", 240).File("a.txt")).Build();
        var wide = tree.Find("wide").Id;
        var c = PlanetComposer.Compose(tree, wide);
        var children = c.Launchers.Where(l => !l.IsParent).ToList();
        children.Should().HaveCount(P.MaxLaunchers);
        children.Select(l => l.Name).Should().BeInAscendingOrder(NaturalNameOrder.Instance, "named in order round the planet");
        var belt = c.SkyBodies.Where(b => b.InBelt).ToList();
        belt.Should().HaveCount(P.MaxBelt);
        belt.Should().OnlyContain(b => b.Launcher == -1 && b.Radius < 2f);
        c.OmittedFolders.Should().Be(240 - P.MaxLaunchers - P.MaxBelt);
        var shown = children.Select(l => l.TargetId).Concat(belt.Select(b => b.TargetId)).ToList();
        shown.Should().OnlyHaveUniqueItems();
        shown.Should().OnlyContain(id => tree[id].ParentId == wide);
        // Ranked by size: the shown launchers are the largest subtrees (all equal here, so by name).
        children.Select(l => l.Name).Should().Equal(Enumerable.Range(0, P.MaxLaunchers).Select(i => $"child-{i:D4}"));
        foreach (var b in belt)
        {
            var r = b.Position.Length();
            r.Should().BeGreaterThan(c.Shape.Radius * 2.2f);
        }

        ShouldBeAClock(c);
        c.Counts.Folders.Should().Be(240);
        c.IsTruncated.Should().BeTrue();
        ShouldBeSane(c);
        ShouldBeReachable(c);
    }

    [Fact]
    public void Prominent_folders_win_the_launchers()
    {
        var tree = new Spec("top").Dir(Spec.WithDirs("wide", 30)).Build();
        var wide = tree.Find("wide").Id;
        var pinned = tree.Find("child-0029").Id;
        var ctx = new ProminenceContext { Pins = new HashSet<string>(PathIdentity.Comparer) { tree.FullPath(pinned) } };
        var c = PlanetComposer.Compose(tree, wide, P with { Prominence = ctx });
        c.LauncherFor(pinned).Should().NotBeNull();
        c.LauncherFor(pinned)!.Prominence!.Kind.Should().Be(ProminenceKind.Pinned);
    }

    [Fact]
    public void Links_get_launchers_marked_as_links()
    {
        var (tree, work, notes, link) = Connected();
        var c = PlanetComposer.Compose(tree, work);
        var l = c.LauncherFor(link)!;
        l.IsLink.Should().BeTrue();
        l.Followable.Should().BeTrue();
        l.CanLaunch.Should().BeTrue();
        l.LinkTargetId.Should().Be(notes);
        l.State.Should().Be(ListingState.Link);
        c.Signs.Single(s => s.TargetId == link).Detail.Should().StartWith("link to notes");
        var linux = c.LauncherFor(6)!;
        linux.IsLink.Should().BeTrue();
        linux.Followable.Should().BeFalse("a Linux symlink cannot be followed through Windows");
        linux.CanLaunch.Should().BeFalse();
        c.SkyBodies[linux.SkyBody].Theme.Should().Be(PlanetTheme.Veiled);
        c.Counts.Links.Should().Be(2);
        c.Counts.Folders.Should().Be(2);

        // Coming back through the link arrives beside the link's launcher.
        c.ArrivalFor(notes).Should().Be(c.ArrivalFor(link));
        c.ArrivalFor(notes).Should().NotBe(c.Arrival);

        // An unresolved link is still a followable launcher, with a veiled body.
        var (tree2, work2, _, link2) = Connected(LinkStatus.Unresolved);
        var c2 = PlanetComposer.Compose(tree2, work2);
        c2.LauncherFor(link2)!.LinkTargetId.Should().Be(-1);
        c2.SkyBodies[c2.LauncherFor(link2)!.SkyBody].Theme.Should().Be(PlanetTheme.Veiled);
        ShouldBeSane(c);
        ShouldBeReachable(c);
    }

    [Fact]
    public void The_visibility_filter_hides_entries_and_counts_them()
    {
        var tree = new Spec("top").Dir(new Spec("box").File(".hidden.png").File("a.txt").File("b.txt").Dir(new Spec(".cache"))).Build();
        var box = tree.Find("box").Id;
        var c = PlanetComposer.Compose(tree, box, visible: n => !n.Name.StartsWith('.'));
        c.Counts.Should().Be(new PlanetChildCounts(0, 2, 0, 2));
        c.Items.Should().HaveCount(2);
        c.Launchers.Where(l => !l.IsParent).Should().BeEmpty();
        c.Theme.Should().Be(PlanetTheme.Library);
    }

    // ------------------------------------------------------------------ sanity and reachability

    public static IEnumerable<object[]> Planets()
    {
        yield return ["modest root"];
        yield return ["modest code"];
        yield return ["mixed"];
        yield return ["mixed many"];
        yield return ["files 1"];
        yield return ["files 65"];
        yield return ["files 150 + 12 dirs"];
        yield return ["dirs 3"];
        yield return ["hub"];
    }

    private static PlanetComposition Make(string name)
    {
        switch (name)
        {
            case "modest root": return PlanetComposer.Compose(Modest(), 0);
            case "modest code": { var t = Modest(); return PlanetComposer.Compose(t, t.Find("Code").Id); }
            case "mixed": { var t = Mixed(); return PlanetComposer.Compose(t, 1); }
            case "mixed many": { var t = Mixed(40); return PlanetComposer.Compose(t, 1); }
            case "files 1": { var t = new Spec("top").Dir(Spec.WithFiles("f", 1)).Build(); return PlanetComposer.Compose(t, 1); }
            case "files 65": { var t = new Spec("top").Dir(Spec.WithFiles("f", 65)).Build(); return PlanetComposer.Compose(t, 1); }
            case "files 150 + 12 dirs":
            {
                var s = Spec.WithFiles("f", 150);
                for (var i = 0; i < 12; i++) s.Dir(new Spec($"d{i}").File("x.png", 1L << (i * 2)));
                var t = new Spec("top").Dir(s).Build();
                return PlanetComposer.Compose(t, 1);
            }
            case "dirs 3": { var t = new Spec("top").Dir(Spec.WithDirs("d", 3)).Build(); return PlanetComposer.Compose(t, 1); }
            default: return PlanetComposer.Compose(Connected().Tree, 0);
        }
    }

    [Theory]
    [MemberData(nameof(Planets))]
    public void Nothing_overlaps_and_everything_is_reachable(string which)
    {
        var c = Make(which);
        ShouldBeSane(c);
        ShouldBeReachable(c);
    }

    /// <summary>No two solids overlap (touching is allowed); no item stands in a pad, the arrival meadow or the overlook.</summary>
    /// <summary>
    /// The launcher clock (2026-11-12): every child launcher on a ring round the arrival (first ring at RingFirst, then
    /// every RingGap), in name order clockwise from straight ahead, ring by ring; the way back at 6 o'clock.
    /// </summary>
    private static void ShouldBeAClock(PlanetComposition c)
    {
        var children = c.Launchers.Where(l => !l.IsParent).ToList();
        var ringOf = children.Select(l => (int)MathF.Round((c.Surface.SurfaceDistance(l.Direction, Vector3.UnitY) - P.RingFirst) / P.RingGap)).ToList();
        for (var i = 0; i < children.Count; i++)
        {
            var d = c.Surface.SurfaceDistance(children[i].Direction, Vector3.UnitY);
            d.Should().BeApproximately(P.RingFirst + ringOf[i] * P.RingGap, 0.05f, $"{children[i].Name} is on a ring");
            if (i > 0)
            {
                ringOf[i].Should().BeGreaterThanOrEqualTo(ringOf[i - 1], "inner ring first");
                // Clockwise from the way back (behind) when there is one, else from straight ahead.
                // Only the first ring holds the way back; each ring starts from its first name's bearing.
                var firstOnRing = children.First(l => (int)MathF.Round((c.Surface.SurfaceDistance(l.Direction, Vector3.UnitY) - P.RingFirst) / P.RingGap) == ringOf[i]);
                var from = c.ParentLauncher != null && ringOf[i] == 0 ? MathF.PI : PlanetFrame.AzimuthOf(firstOnRing.Direction) - 0.001f;
                float Key(PlanetLauncher l) => ((PlanetFrame.AzimuthOf(l.Direction) - from) % (2 * MathF.PI) + 2 * MathF.PI) % (2 * MathF.PI);
                var a0 = Key(children[i - 1]);
                var a1 = Key(children[i]);
                if (ringOf[i] == ringOf[i - 1]) a1.Should().BeGreaterThan(a0, "clockwise in name order");
            }
        }

        children.Select(l => l.Name).Should().BeInAscendingOrder(NaturalNameOrder.Instance);
        if (children.Count > 0 && c.ParentLauncher == null) MathF.Abs(PlanetFrame.AzimuthOf(children[0].Direction)).Should().BeLessThan(0.01f, "the first name straight ahead (12 o'clock)");
    }

    [Fact]
    public void An_ubuntu_like_root_puts_every_folder_on_rings_round_the_arrival_home_included()
    {
        // DJ's playtest (2026-11-12): on the Ubuntu root, home took a while to find. A Linux root's folders (and its
        // usr-merge links) all get a pad within three rings of the arrival, in name order like a clock.
        var names = new[] { "bin", "boot", "dev", "etc", "home", "lib", "lib64", "lost+found", "media", "mnt", "opt", "proc", "root", "run", "sbin", "snap", "srv", "sys", "tmp", "usr", "var", "init" };
        var spec = new Spec("/");
        foreach (var n in names.Where(n => n != "init")) spec = spec.Dir(n == "usr" ? Spec.WithFiles(n, 40) : new Spec(n));
        var tree = spec.File("init").File("swap.img").Build();
        var c = PlanetComposer.Compose(tree, 0);
        var pads = c.Launchers.Where(l => !l.IsParent).ToList();
        pads.Should().HaveCount(21, "every folder has a pad");
        var home = pads.Single(l => l.Name == "home");
        c.Surface.SurfaceDistance(home.Direction, Vector3.UnitY).Should().BeLessThanOrEqualTo(P.RingFirst + 2 * P.RingGap + 0.05f, "home is a short walk from the arrival");
        pads.Max(l => c.Surface.SurfaceDistance(l.Direction, Vector3.UnitY)).Should().BeLessThanOrEqualTo(P.RingFirst + 2 * P.RingGap + 0.05f, "no more than three rings");
        ShouldBeAClock(c);
        ShouldBeSane(c);
        ShouldBeReachable(c);
    }

    [Fact]
    public void On_a_crowded_world_no_two_worlds_share_a_bearing_and_every_world_is_above_the_arrival_horizon()
    {
        var spec = new Spec("top");
        var crowded = new Spec("crowded");
        for (var i = 0; i < 28; i++) crowded = crowded.Dir(Spec.WithFiles($"w{i:D2}", 20 + i * 30));
        var tree = spec.Dir(crowded).Build();
        var c = PlanetComposer.Compose(tree, tree.Find("crowded").Id);
        var pads = c.Launchers.Where(l => !l.IsParent).ToList();
        for (var i = 0; i < pads.Count; i++)
        {
            for (var j = i + 1; j < pads.Count; j++)
            {
                var dAz = MathF.Abs(MathF.IEEERemainder(PlanetFrame.AzimuthOf(pads[i].Direction) - PlanetFrame.AzimuthOf(pads[j].Direction), 2 * MathF.PI));
                dAz.Should().BeGreaterThan(0.03f, $"{pads[i].Name} and {pads[j].Name} on different bearings");
            }
        }

        var eye = Vector3.UnitY * (c.Shape.Radius + 3.4f);
        foreach (var body in c.SkyBodies.Where(b => !b.InBelt && !b.IsParent))
        {
            var to = body.Position - eye;
            var dir = Vector3.Normalize(to);
            // The ray from the arrival eye to the body's centre does not meet the planet.
            var b = Vector3.Dot(eye, dir);
            var disc = b * b - (eye.LengthSquared() - c.Shape.Radius * c.Shape.Radius);
            var hit = disc >= 0 && -b - MathF.Sqrt(disc) > 0 && -b - MathF.Sqrt(disc) < to.Length();
            hit.Should().BeFalse($"{tree[body.TargetId].Name}'s world is seen from the arrival");
        }

        ShouldBeAClock(c);
        ShouldBeSane(c);
        ShouldBeReachable(c);
    }

    [Fact]
    public void The_way_back_is_at_six_o_clock_even_with_no_folders()
    {
        var tree = new Spec("top").Dir(Spec.WithFiles("leaf", 3)).Build();
        var c = PlanetComposer.Compose(tree, tree.Find("leaf").Id);
        var back = c.ParentLauncher!;
        MathF.Abs(MathF.Abs(PlanetFrame.AzimuthOf(back.Direction)) - MathF.PI).Should().BeLessThan(0.01f, "at 6 o'clock");
        c.Surface.SurfaceDistance(back.Direction, Vector3.UnitY).Should().BeApproximately(P.RingFirst, 0.05f);
    }

    private static void ShouldBeSane(PlanetComposition c)
    {
        var obs = c.Surface.Obstacles;
        for (var i = 0; i < obs.Count; i++)
        {
            obs[i].Direction.Length().Should().BeApproximately(1f, 1e-4f);
            for (var j = i + 1; j < obs.Count; j++)
            {
                var d = c.Surface.SurfaceDistance(obs[i].Direction, obs[j].Direction);
                d.Should().BeGreaterThanOrEqualTo(obs[i].Radius + obs[j].Radius - 1e-3f, $"obstacles {i} ({obs[i]}) and {j} ({obs[j]}) overlap on {c.Name}");
            }
        }

        foreach (var it in c.Items)
        {
            c.Surface.SurfaceDistance(it.Direction, Vector3.UnitY).Should().BeGreaterThanOrEqualTo(c.ArrivalRadius + it.Radius, "no item in the arrival meadow");
            c.Surface.SurfaceDistance(it.Direction, c.Overlook.Direction).Should().BeGreaterThan(c.Overlook.Radius + it.Radius, "no item on the overlook");
            foreach (var l in c.Launchers)
            {
                c.Surface.SurfaceDistance(it.Direction, l.Direction).Should().BeGreaterThan(l.PadRadius + it.Radius, "no item in a launcher pad");
            }

            it.Scale.Should().BeInRange(0.6f, 1.6f);
            obs.Should().Contain(o => o.Direction == it.Direction && MathF.Abs(o.Radius - it.Radius) < 1e-5f, "every item is solid");
        }

        foreach (var l in c.Launchers)
        {
            obs.Should().Contain(o => o.Direction == l.Direction && o.Top == l.PadTop, "every pad is a standable solid");
        }

        c.Decorations.Where(d => d.Collides).Should().HaveCount(obs.Count - 2 - c.Overlook.Steps.Count - c.Launchers.Count - c.Items.Count);
        foreach (var d in c.Decorations)
        {
            foreach (var f in c.Shape.FlatSpots)
            {
                c.Surface.SurfaceDistance(d.Direction, f.Direction).Should().BeGreaterThan(f.Radius, "decorations keep off flat spots");
            }
        }
    }

    /// <summary>
    /// A coarse walk: from the arrival, flood over surface sample points ≈ 0.45 m apart with a 0.35 m body; a point can be
    /// stood on at the support the body finds there (a top within 0.45 m of the feet, else the ground) when nothing under
    /// the body rises more than 0.45 m above that support. No jumping. It must reach every launcher pad, a point beside
    /// every item and pile, and the overlook top (so by the steps).
    /// </summary>
    private static void ShouldBeReachable(PlanetComposition c)
    {
        const float spacing = 0.45f;
        const float body = 0.35f;
        const float step = 0.45f;
        var s = c.Surface;
        var R = s.Radius;
        var pts = PlanetComposer.FibonacciSphere((int)(4 * MathF.PI * R * R / (0.866f * spacing * spacing)));
        var nb = PlanetComposer.Neighbours(pts, 1.5f * spacing / R);
        var obs = s.Obstacles;
        var blockers = new int[pts.Length][];
        var supports = new int[pts.Length][];
        var nearB = new List<int>[pts.Length];
        var nearS = new List<int>[pts.Length];
        for (var i = 0; i < pts.Length; i++) { nearB[i] = []; nearS[i] = []; }
        // Bucket by obstacle: only points near each obstacle are tested (cheap for big planets).
        for (var o = 0; o < obs.Count; o++)
        {
            var reach = obs[o].Radius + body;
            var cos = MathF.Cos(reach / R);
            for (var i = 0; i < pts.Length; i++)
            {
                var dot = Vector3.Dot(pts[i], obs[o].Direction);
                if (dot < cos - 1e-3f) continue;
                var d = s.SurfaceDistance(pts[i], obs[o].Direction);
                if (d < obs[o].Radius + body) nearB[i].Add(o);
                if (d < obs[o].Radius + body * 0.6f) nearS[i].Add(o);
            }
        }

        for (var i = 0; i < pts.Length; i++) { blockers[i] = nearB[i].ToArray(); supports[i] = nearS[i].ToArray(); }

        float Support(int i, float feet)
        {
            var best = 0f;
            foreach (var o in supports[i]) if (obs[o].Top <= feet + step && obs[o].Top > best) best = obs[o].Top;
            return best;
        }

        bool Standable(int i, float support)
        {
            foreach (var o in blockers[i]) if (obs[o].Top > support + step) return false;
            return true;
        }

        var reached = new Dictionary<int, HashSet<float>>();
        var queue = new Queue<(int, float)>();
        var start = Enumerable.Range(0, pts.Length).MaxBy(i => pts[i].Y);
        Standable(start, 0).Should().BeTrue("the arrival point is clear");
        reached[start] = [0f];
        queue.Enqueue((start, 0f));
        while (queue.Count > 0)
        {
            var (i, h) = queue.Dequeue();
            foreach (var j in nb[i])
            {
                var sj = Support(j, h);
                if (!Standable(j, sj)) continue;
                if (!reached.TryGetValue(j, out var set)) reached[j] = set = [];
                if (set.Add(sj)) queue.Enqueue((j, sj));
            }
        }

        bool Any(Func<int, float, bool> pred) => reached.Any(kv => kv.Value.Any(h => pred(kv.Key, h)));

        foreach (var l in c.Launchers)
        {
            Any((i, h) => h == l.PadTop && s.SurfaceDistance(pts[i], l.Direction) < l.PadRadius).Should().BeTrue($"launcher pad for #{l.TargetId} is reachable on {c.Name}");
        }

        foreach (var it in c.Items)
        {
            Any((i, _) => s.SurfaceDistance(pts[i], it.Direction) < it.Radius + body + 0.7f).Should().BeTrue($"a point beside item #{it.NodeId} ({it.Kind}) is reachable on {c.Name}");
        }

        Any((i, h) => MathF.Abs(h - c.Overlook.Top) < 1e-4f && s.SurfaceDistance(pts[i], c.Overlook.Direction) < c.Overlook.Radius)
            .Should().BeTrue($"the overlook top is reachable by its steps on {c.Name}");
    }

    // ------------------------------------------------------------------ determinism and signature

    [Fact]
    public void The_same_input_gives_the_same_planet()
    {
        var a = PlanetComposer.Compose(Mixed(20), 1);
        var b = PlanetComposer.Compose(Mixed(20), 1);
        a.Theme.Should().Be(b.Theme);
        a.Shape.Radius.Should().Be(b.Shape.Radius);
        a.Shape.Seed.Should().Be(b.Shape.Seed);
        a.Shape.HillAmplitude.Should().Be(b.Shape.HillAmplitude);
        a.Shape.FlatSpots.Should().Equal(b.Shape.FlatSpots);
        a.Surface.Obstacles.Should().Equal(b.Surface.Obstacles);
        a.Items.Should().Equal(b.Items);
        a.Groves.Should().Equal(b.Groves);
        a.Launchers.Should().Equal(b.Launchers);
        a.SkyBodies.Should().Equal(b.SkyBodies);
        a.Decorations.Should().Equal(b.Decorations);
        a.Signs.Should().Equal(b.Signs);
        a.Signature.Should().Be(b.Signature);
        a.Shape.HeightAt(Vector3.Normalize(new Vector3(1, 0.2f, 0.3f))).Should().Be(b.Shape.HeightAt(Vector3.Normalize(new Vector3(1, 0.2f, 0.3f))));
        a.Shape.HeightAt(Vector3.UnitY).Should().Be(0, "the arrival meadow is flat");
    }

    [Fact]
    public void The_signature_changes_when_a_child_listing_completes()
    {
        var tree = new Spec("top").Dir(new Spec("box").Dir(new Spec("later") { State = DirScanState.Unlisted }).File("a.txt")).Build();
        var box = tree.Find("box").Id;
        var later = tree.Find("later").Id;
        var before = PlanetComposer.Signature(tree, box);
        PlanetComposer.Signature(tree, box).Should().Be(before, "stable while nothing changes");
        PlanetComposer.Compose(tree, box).SkyBodies.Single(b => b.TargetId == later).Theme.Should().Be(PlanetTheme.Veiled);

        var batch = new ScanBatch();
        batch.NewNodes.Add(new NodeRecord(tree.Count, later, "song.mp3", NodeKind.File, 0, 0, 5000, null, null, null, 3, DirScanState.NotApplicable));
        batch.DirectoryUpdates.Add(new DirectoryUpdate(later, DirScanState.Complete));
        tree.Apply(batch);
        PlanetComposer.Signature(tree, box).Should().NotBe(before);
        PlanetComposer.Compose(tree, box).SkyBodies.Single(b => b.TargetId == later).Theme.Should().Be(PlanetTheme.Chime);
    }

    private static float Deg(float radians) => radians * 180f / MathF.PI;
}
