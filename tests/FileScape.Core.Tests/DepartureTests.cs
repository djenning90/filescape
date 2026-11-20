using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Planets;
using FileScape.Core.Scanning;

namespace FileScape.Core.Tests;

/// <summary>
/// Which launcher every departure leaves from (PLANET-120..122, AD-84): one test per entry point in AD-84's inventory,
/// each building the query the app builds for it (PlanetWorld.Depart → <see cref="DeparturePlanner.Plan"/>). Written
/// before the rule was changed: the entry points DJ saw launching "from wherever they stand" (a destination deeper down
/// or elsewhere, chosen in a dialog) failed against the earlier rule (the destination's own pad only).
/// </summary>
public class DepartureTests
{
    /// <summary>
    /// top ─ here ─ {alpha, beta ─ beta-1 ─ beta-2, gamma (repository), mid ─ m1 ─ m2, belt-*}, top ─ elsewhere ─ far.
    /// With <paramref name="maxLaunchers"/> pads, the lowest ranked of here's folders become belt moons.
    /// </summary>
    private static (FsTree Tree, PlanetComposition Here, Ids Ids) World(int maxLaunchers = 28)
    {
        var here = new Spec("here")
            .Dir(new Spec("alpha").File("a.txt"))
            .Dir(new Spec("beta").Dir(new Spec("beta-1").Dir(new Spec("beta-2").File("deep.txt"))))
            .Dir(new Spec("gamma").File("g.csproj", 900))
            .Dir(new Spec("mid").Dir(new Spec("m1").Dir(new Spec("m2"))))
            .File("here.txt");
        for (var i = 0; i < 4; i++) here.Dir(new Spec($"zz-belt-{i}").Dir(new Spec($"below-belt-{i}")));
        var tree = new Spec("top").Dir(here).Dir(new Spec("elsewhere").Dir(new Spec("far").File("f.txt"))).Build();
        var ids = new Ids(tree);
        var comp = PlanetComposer.Compose(tree, ids["here"], PlanetParams.Default with { MaxLaunchers = maxLaunchers });
        return (tree, comp, ids);
    }

    private sealed class Ids(FsTree tree)
    {
        public int this[string name] => tree.Find(name).Id;
    }

    private static DeparturePlan Plan(FsTree tree, PlanetComposition here, Vector3 standing, int target, DepartureEntry entry, int via = -1) =>
        DeparturePlanner.Plan(new DepartureQuery(tree, here, standing, target, entry, via));

    /// <summary>Standing on the arrival meadow (the usual place to be when a dialog is used).</summary>
    private static Vector3 Meadow(PlanetComposition c) => c.Arrival.Up;

    private static void ShouldRunTo(DeparturePlan p, PlanetLauncher? pad, DeparturePadRole role, string why)
    {
        pad.Should().NotBeNull(why);
        p.Pad.Should().Be(pad, why);
        p.Role.Should().Be(role, why);
        p.Kind.Should().Be(DepartureKind.RunToPad, why);
        p.RunSeconds.Should().BeInRange(DeparturePlanner.RunBaseSeconds, DeparturePlanner.RunMaxSeconds, "the run is brisk and capped");
    }

    // ------------------------------------------------------------------ entry points that had their pad before (AD-74)

    [Fact]
    public void Pad_press_leaves_from_the_pad_with_no_time_added()
    {
        var (t, c, ids) = World();
        var pad = c.LauncherFor(ids["alpha"])!;
        var p = Plan(t, c, pad.Direction, ids["alpha"], DepartureEntry.PadPress);
        p.Kind.Should().Be(DepartureKind.OnPad);
        p.Pad.Should().Be(pad);
        p.RunSeconds.Should().Be(0);
    }

    [Fact]
    public void Go_inside_runs_to_the_worlds_own_pad()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["beta"], DepartureEntry.GoInside), c.LauncherFor(ids["beta"]), DeparturePadRole.Own, "G on a world here");
    }

    [Fact]
    public void Go_inside_standing_on_the_pad_adds_nothing()
    {
        var (t, c, ids) = World();
        var pad = c.LauncherFor(ids["beta"])!;
        var p = Plan(t, c, pad.Direction, ids["beta"], DepartureEntry.GoInside);
        p.Kind.Should().Be(DepartureKind.OnPad);
        p.RunSeconds.Should().Be(0);
    }

    [Fact]
    public void Parent_runs_to_the_way_back()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["top"], DepartureEntry.Parent), c.ParentLauncher, DeparturePadRole.WayBack, "U");
    }

    [Fact]
    public void History_back_to_a_world_here_runs_to_its_pad()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["alpha"], DepartureEntry.HistoryBack), c.LauncherFor(ids["alpha"]), DeparturePadRole.Own, "Backspace to a child");
    }

    [Fact]
    public void Trip_hop_runs_to_the_next_worlds_pad()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["mid"], DepartureEntry.TripHop), c.LauncherFor(ids["mid"]), DeparturePadRole.Own, "a trip's hop");
    }

    [Fact]
    public void Destinations_world_here_committed_runs_to_its_pad()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["gamma"], DepartureEntry.NavigatorChange), c.LauncherFor(ids["gamma"]), DeparturePadRole.Own, "a world here chosen in a dialog");
    }

    // ------------------------------------------------------------------ the entry points that launched where you stood (DJ, 2026-11-20)

    [Fact]
    public void Destinations_deeper_row_runs_to_the_pad_on_the_way_down()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["beta-2"], DepartureEntry.NavigatorChange), c.LauncherFor(ids["beta"]), DeparturePadRole.Toward, "a folder two levels down (Straight there)");
    }

    [Fact]
    public void Favourite_elsewhere_runs_to_the_way_back()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["far"], DepartureEntry.NavigatorChange), c.ParentLauncher, DeparturePadRole.WayOut, "a pinned folder in another branch");
    }

    [Fact]
    public void Repository_below_runs_toward_it()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["m2"], DepartureEntry.NavigatorChange), c.LauncherFor(ids["mid"]), DeparturePadRole.Toward, "a repository below this world");
    }

    [Fact]
    public void History_back_to_a_place_elsewhere_runs_to_the_way_back()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["far"], DepartureEntry.HistoryBack), c.ParentLauncher, DeparturePadRole.WayOut, "Backspace to a place in another branch");
    }

    [Fact]
    public void History_back_to_a_place_below_runs_toward_it()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["beta-1"], DepartureEntry.HistoryBack), c.LauncherFor(ids["beta"]), DeparturePadRole.Toward, "Backspace after a trip");
    }

    [Fact]
    public void Breadcrumb_or_Ctrl_Home_to_an_ancestor_runs_to_the_way_back()
    {
        // The way back leads up; a grandparent (or the arrival root) is reached up and over.
        var tree = new Spec("root").Dir(new Spec("top").Dir(new Spec("here").Dir(new Spec("kid")))).Build();
        var here = tree.Find("here").Id;
        var comp = PlanetComposer.Compose(tree, here);
        ShouldRunTo(Plan(tree, comp, Meadow(comp), tree.Find("root").Id, DepartureEntry.NavigatorChange), comp.ParentLauncher, DeparturePadRole.WayOut, "the breadcrumb's root");
    }

    [Fact]
    public void Locations_or_go_to_path_elsewhere_runs_to_the_way_back()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["elsewhere"], DepartureEntry.NavigatorChange), c.ParentLauncher, DeparturePadRole.WayOut, "Ctrl+L to a sibling");
    }

    [Fact]
    public void Open_folder_new_root_below_runs_toward_it()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["beta-1"], DepartureEntry.NavigatorChange), c.LauncherFor(ids["beta"]), DeparturePadRole.Toward, "Ctrl+O on a folder below");
    }

    [Fact]
    public void Walkthrough_back_to_the_lesson_runs_to_the_way_back()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["top"], DepartureEntry.NavigatorChange), c.ParentLauncher, DeparturePadRole.WayBack, "the lesson is the parent");
        ShouldRunTo(Plan(t, c, Meadow(c), ids["elsewhere"], DepartureEntry.NavigatorChange), c.ParentLauncher, DeparturePadRole.WayOut, "the lesson is elsewhere");
    }

    [Fact]
    public void Telescope_destination_below_runs_toward_it()
    {
        var (t, c, ids) = World();
        ShouldRunTo(Plan(t, c, Meadow(c), ids["m1"], DepartureEntry.Telescope), c.LauncherFor(ids["mid"]), DeparturePadRole.Toward, "a world chosen at the telescope");
    }

    [Fact]
    public void A_link_here_leaves_from_its_launcher_and_a_link_below_from_the_pad_toward_it()
    {
        var (tree, work, link, deepLink, target) = Links();
        var c = PlanetComposer.Compose(tree, work);
        var linkPad = c.LauncherFor(link)!;
        ShouldRunTo(Plan(tree, c, Meadow(c), target, DepartureEntry.NavigatorChange, via: link), linkPad, DeparturePadRole.Link, "the navigator went through the link here");
        ShouldRunTo(Plan(tree, c, Meadow(c), link, DepartureEntry.GoInside), linkPad, DeparturePadRole.Own, "G on the link");
        // A link in a folder below (a deeper Destinations row): leave toward the folder that holds it.
        ShouldRunTo(Plan(tree, c, Meadow(c), target, DepartureEntry.NavigatorChange, via: deepLink), c.LauncherFor(tree[deepLink].ParentId), DeparturePadRole.Toward, "a link two levels down");
    }

    /// <summary>work ─ {docs ─ notes, to-notes (junction → notes), app ─ deeper-link (junction → notes)}.</summary>
    private static (FsTree Tree, int Work, int Link, int DeepLink, int Target) Links()
    {
        var tree = new FsTree(new DataSourceInfo(DataOrigin.LiveFilesystem, "This computer", "test"));
        var b = new ScanBatch();
        void N(int id, int parent, string name, NodeKind kind, LinkInfo? link = null, NodeFlags flags = 0) =>
            b.NewNodes.Add(new NodeRecord(id, parent, name, kind, flags, 0, 0, null, null, null, 0, kind == NodeKind.Link ? DirScanState.NotApplicable : DirScanState.Complete, link, null, LocationKind.None));
        var junction = new LinkInfo(LinkKind.Junction, Reparse.TagMountPoint, @"C:\work\docs\notes", LinkStatus.Resolved, @"C:\work\docs\notes", 3);
        N(0, -1, "C:", NodeKind.Directory);
        N(1, 0, "work", NodeKind.Directory);
        N(2, 1, "docs", NodeKind.Directory);
        N(3, 2, "notes", NodeKind.Directory);
        N(4, 1, "to-notes", NodeKind.Link, junction, NodeFlags.ReparsePoint | NodeFlags.DirectoryLink);
        N(5, 1, "app", NodeKind.Directory);
        N(6, 5, "deeper-link", NodeKind.Link, junction, NodeFlags.ReparsePoint | NodeFlags.DirectoryLink);
        tree.Apply(b);
        return (tree, 1, 4, 6, 3);
    }

    // ------------------------------------------------------------------ destinations without a pad, and the sky

    [Fact]
    public void A_belt_moon_and_what_is_below_it_have_no_pad()
    {
        var (t, c, ids) = World(maxLaunchers: 4);
        c.SkyBodies.Should().Contain(k => k.InBelt && k.TargetId == ids["zz-belt-0"], "the fixture's last folders are belt moons");
        Plan(t, c, Meadow(c), ids["zz-belt-0"], DepartureEntry.GoInside).Kind.Should().Be(DepartureKind.PadLess);
        Plan(t, c, Meadow(c), ids["below-belt-0"], DepartureEntry.NavigatorChange).Kind.Should().Be(DepartureKind.PadLess);
    }

    [Fact]
    public void The_top_of_the_tree_has_no_way_back_so_a_place_not_below_is_pad_less()
    {
        var tree = new Spec("top").Dir(new Spec("a").Dir(new Spec("a1"))).Build();
        var c = PlanetComposer.Compose(tree, 0);
        c.ParentLauncher.Should().BeNull();
        Plan(tree, c, Meadow(c), tree.Find("a1").Id, DepartureEntry.NavigatorChange).Role.Should().Be(DeparturePadRole.Toward);
        // The only place not below the top is the top itself: nothing leaves for it.
        Plan(tree, c, Meadow(c), 0, DepartureEntry.NavigatorChange).Kind.Should().Be(DepartureKind.PadLess);
    }

    [Fact]
    public void The_overview_and_a_fall_leave_from_the_sky()
    {
        var (t, c, ids) = World();
        Plan(t, c, Meadow(c), ids["alpha"], DepartureEntry.Overview).Kind.Should().Be(DepartureKind.FromSky);
        Plan(t, c, Meadow(c), ids["top"], DepartureEntry.FallWayBack).Kind.Should().Be(DepartureKind.FromSky);
    }

    // ------------------------------------------------------------------ the cap (quick direct navigation)

    [Fact]
    public void The_run_is_capped_even_from_the_far_side_of_the_world()
    {
        var (t, c, ids) = World();
        var pad = c.LauncherFor(ids["beta"])!;
        var p = Plan(t, c, -pad.Direction, ids["beta"], DepartureEntry.GoInside);
        p.RunMetres.Should().BeGreaterThan(c.Shape.Radius * 3f, "half way round the world");
        p.RunSeconds.Should().Be(DeparturePlanner.RunMaxSeconds);
        DeparturePlanner.RunSeconds(0, false).Should().Be(DeparturePlanner.RunBaseSeconds);
        DeparturePlanner.RunSeconds(9, false).Should().BeApproximately(0.6f, 1e-4f, "the first ring is ≈ 0.6 s away");
        DeparturePlanner.RunSeconds(1000, true).Should().Be(DeparturePlanner.RunRouteMaxSeconds);
    }

    [Fact]
    public void A_hazard_worlds_run_is_capped_at_its_route_limit()
    {
        var c = new Spec("C:") { Location = LocationKind.Drive }.Dir(new Spec("Windows").Dir(new Spec("System32").File("k.dll")).Dir(new Spec("Fonts"))).Build();
        var win = c.Find("Windows").Id;
        var comp = PlanetComposer.Compose(c, win);
        comp.Hazard.Should().NotBeNull("Windows is a Hazard World");
        var pad = comp.ParentLauncher!;
        var p = Plan(c, comp, -pad.Direction, c.Root.Id, DepartureEntry.Parent);
        p.Kind.Should().Be(DepartureKind.RunToPad);
        p.RunSeconds.Should().BeLessThanOrEqualTo(DeparturePlanner.RunRouteMaxSeconds);
    }
}
