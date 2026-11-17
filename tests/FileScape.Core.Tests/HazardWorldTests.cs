using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-34, AD-53: which folders are Hazard Worlds (platform and path context, look-alikes that are not). PLANET-46,
/// AD-59: the shell is islands round the destinations joined by narrow bridges, about a third of the sphere: every
/// destination's content keeps its margin, the route from the arrival reaches every launcher (the way back without a
/// jump), bridges are at least the walkable width, the real walker clears every gap from a range of take-off points,
/// falls happen only off the shell, and the opening ahead of the arrival (with the black hole's jet) is open sky.
/// </summary>
public class HazardWorldTests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ classification

    /// <summary>A computer with the real system folders and every look-alike the rules must not catch.</summary>
    private static FsTree Computer()
    {
        var c = new Spec("C:") { Location = LocationKind.Drive }
            .Dir(new Spec("Windows").Dir(new Spec("System32").File("kernel32.dll")))
            .Dir(new Spec("Program Files").Dir(new Spec("App")))
            .Dir(new Spec("Program Files (x86)"))
            .Dir(new Spec("ProgramData"))
            .Dir(new Spec("$Recycle.Bin"))
            .Dir(new Spec("System Volume Information"))
            .Dir(new Spec("Recovery"))
            .Dir(new Spec("Users").Dir(new Spec("dj").Dir(new Spec("projects").Dir(new Spec("Windows").File("notes.md")).Dir(new Spec("Program Files")))));
        var d = new Spec("D:") { Location = LocationKind.Drive }
            .Dir(new Spec("windows")) // a volume root's windows, in any case, is still the OS's (an old install)
            .Dir(new Spec("projects").Dir(new Spec("Windows").Dir(new Spec(".git"))).Dir(new Spec("proc")));
        var ubuntu = new Spec("Ubuntu") { Location = LocationKind.Wsl }
            .Dir(new Spec("proc")).Dir(new Spec("sys")).Dir(new Spec("dev")).Dir(new Spec("boot")).Dir(new Spec("run"))
            .Dir(new Spec("Proc")) // Linux names are case-sensitive: not /proc
            .Dir(new Spec("etc"))
            .Dir(new Spec("home").Dir(new Spec("dj").Dir(new Spec("repo").Dir(new Spec(".git")).Dir(new Spec("proc")).Dir(new Spec("sys")))));
        var share = new Spec(@"\\server\share") { Location = LocationKind.Network }.Dir(new Spec("Windows")).Dir(new Spec("ProgramData"));
        var volume = new Spec(@"\\?\Volume{1234}\") { Location = LocationKind.Volume }.Dir(new Spec("Recovery"));
        var posix = new Spec("/") { Location = LocationKind.Volume }.Dir(new Spec("proc")).Dir(new Spec("Windows"));
        return new Spec("This computer") { Location = LocationKind.Hub }.Dir(c).Dir(d).Dir(ubuntu).Dir(share).Dir(volume).Dir(posix).Build();
    }

    /// <summary>The folder at a path of names from the tree root (the first is the root's child).</summary>
    private static int At(FsTree t, params string[] names)
    {
        var id = 0;
        foreach (var name in names) id = t[id].Children.First(c => t[c].Name == name);
        return id;
    }

    [Fact]
    public void Every_windows_system_folder_directly_under_a_volume_root_is_a_hazard()
    {
        var t = Computer();
        foreach (var name in HazardRules.WindowsNames.Select(n => n.Name))
        {
            var reason = HazardRules.Classify(t, At(t, "C:", name));
            reason.Should().NotBeNull(name);
            reason!.Rule.Should().Be(HazardRule.WindowsSystem);
            reason.Why.Should().StartWith(name);
            WorldCharacter.Of(t, At(t, "C:", name), 0).Kind.Should().Be(WorldKind.Hazard, name);
        }

        HazardRules.Classify(t, At(t, "D:", "windows"))!.Name.Should().Be("Windows", "Windows names are case-insensitive");
        HazardRules.IsHazard(t, At(t, @"\\?\Volume{1234}\", "Recovery")).Should().BeTrue("a volume without a letter is a volume root too");
    }

    [Fact]
    public void Every_linux_system_folder_directly_under_a_distribution_root_is_a_hazard()
    {
        var t = Computer();
        foreach (var name in HazardRules.LinuxNames.Select(n => n.Name))
        {
            HazardRules.Classify(t, At(t, "Ubuntu", name))!.Rule.Should().Be(HazardRule.LinuxSystem, name);
        }

        HazardRules.Classify(t, At(t, "/", "proc"))!.Rule.Should().Be(HazardRule.LinuxSystem, "the POSIX root is a Linux root");
    }

    [Fact]
    public void Look_alikes_are_ordinary_folders()
    {
        var t = Computer();
        string[][] ordinary =
        [
            ["C:", "Users", "dj", "projects", "Windows"], // a project folder called Windows
            ["C:", "Users", "dj", "projects", "Program Files"],
            ["C:", "Windows", "System32"], // only the system folder itself; what is inside is an ordinary world
            ["C:", "Program Files", "App"],
            ["D:", "projects", "Windows"], // a repository called Windows
            ["D:", "projects", "proc"], // a Linux name on a Windows drive
            ["Ubuntu", "Proc"], // Linux names are case-sensitive
            ["Ubuntu", "etc"],
            ["Ubuntu", "home", "dj", "repo", "proc"], // a repository's proc
            ["Ubuntu", "home", "dj", "repo", "sys"],
            [@"\\server\share", "Windows"], // a network share's top folders are someone's shared folders
            [@"\\server\share", "ProgramData"],
            ["/", "Windows"], // the POSIX root is not a Windows volume
        ];
        foreach (var path in ordinary)
        {
            var id = At(t, path);
            HazardRules.Classify(t, id).Should().BeNull(string.Join("/", path));
            WorldCharacter.Of(t, id, 0).Kind.Should().NotBe(WorldKind.Hazard, string.Join("/", path));
        }

        WorldCharacter.Of(t, At(t, "D:", "projects", "Windows"), 0).Kind.Should().Be(WorldKind.Repository);
        // Roots are never hazards themselves.
        foreach (var root in new[] { "C:", "D:", "Ubuntu", "/" }) HazardRules.IsHazard(t, At(t, root)).Should().BeFalse(root);
        HazardRules.IsHazard(t, 0).Should().BeFalse("the hub");
    }

    [Fact]
    public void A_path_opened_directly_at_a_drive_or_distribution_root_keeps_its_context()
    {
        var c = new Spec("C:\\").Dir(new Spec("Windows")).Dir(new Spec("src").Dir(new Spec("Windows")))
            .Build(new DataSourceInfo(DataOrigin.LiveFilesystem, @"C:\", "test"));
        HazardRules.IsHazard(c, At(c, "Windows")).Should().BeTrue();
        HazardRules.IsHazard(c, At(c, "src", "Windows")).Should().BeFalse();
        var wsl = new Spec(@"\\wsl.localhost\Ubuntu").Dir(new Spec("proc")).Dir(new Spec("home").Dir(new Spec("proc")))
            .Build(new DataSourceInfo(DataOrigin.LiveFilesystem, @"\\wsl.localhost\Ubuntu", "test"));
        HazardRules.IsHazard(wsl, At(wsl, "proc")).Should().BeTrue();
        HazardRules.IsHazard(wsl, At(wsl, "home", "proc")).Should().BeFalse();
        var elsewhere = new Spec(@"D:\projects").Dir(new Spec("Windows")).Build(new DataSourceInfo(DataOrigin.LiveFilesystem, @"D:\projects", "test"));
        HazardRules.IsHazard(elsewhere, At(elsewhere, "Windows")).Should().BeFalse("D:\\projects is not a volume root");
    }

    [Fact]
    public void The_sample_marker_counts_only_inside_a_fileScape_sample()
    {
        var sky = new Spec("Starter Sky").File(HazardRules.SampleRootMarkerName)
            .Dir(new Spec("Event Horizon").File(HazardRules.SampleMarkerName).File("README.txt").Dir(new Spec("Accretion Disc")))
            .Dir(new Spec("Garden Photos").File("a.png"));
        var t = new Spec("home").Dir(sky).Dir(new Spec("mine").File(HazardRules.SampleMarkerName)).Dir(new Spec("dir-marker").Dir(new Spec(HazardRules.SampleMarkerName))).Build();
        var eh = HazardRules.Classify(t, t.Find("Event Horizon").Id);
        eh!.Rule.Should().Be(HazardRule.Sample);
        WorldCharacter.Of(t, t.Find("Event Horizon").Id, t.Find("Starter Sky").Id).Kind.Should().Be(WorldKind.Hazard);
        HazardRules.IsHazard(t, t.Find("mine").Id).Should().BeFalse("a marker outside a sample means nothing: no folder becomes a hazard by accident");
        HazardRules.IsHazard(t, t.Find("dir-marker").Id).Should().BeFalse("the marker is a file");
        HazardRules.IsHazard(t, t.Find("Accretion Disc").Id).Should().BeFalse("inside the hazard is an ordinary world");
        HazardRules.IsHazard(t, t.Find("Starter Sky").Id).Should().BeFalse("the sample root itself is not inside a sample");
        HazardRules.IsHazard(t, t.Find("Garden Photos").Id).Should().BeFalse();
    }

    [Fact]
    public void Hazard_is_the_starting_folder_too_and_wins_over_a_repository()
    {
        var t = new Spec("C:") { Location = LocationKind.Drive }.Dir(new Spec("ProgramData").Dir(new Spec(".git"))).Build();
        var id = At(t, "ProgramData");
        WorldCharacter.Of(t, id, id).Kind.Should().Be(WorldKind.Hazard, "being system-managed matters more than being where you started");
        WorldCharacter.Of(t, id, 0).Kind.Should().Be(WorldKind.Hazard);
        WorldCharacter.Of(t, id, 0).Describe().Should().Contain("Hazard World");
    }


    // ------------------------------------------------------------------ the shell: islands and bridges (PLANET-46, AD-59)

    /// <summary>Hazard worlds of every shape the composer meets: the sample, a sparse and a crowded one, files only, nothing at all, and the sample's crowded Debris Field.</summary>
    public static TheoryData<string> Worlds() => new() { "sample", "few", "crowded", "files", "empty", "debris" };

    private static (FsTree Tree, int Id) Hazard(string kind)
    {
        var sky = new Spec("Starter Sky").File(HazardRules.SampleRootMarkerName);
        Spec eh;
        switch (kind)
        {
            case "sample":
                eh = new Spec("Event Horizon").File(HazardRules.SampleMarkerName).File("README.txt", 900).File("caution.txt", 300).File("orbit-log.csv", 200)
                    .Dir(new Spec("Accretion Disc").File("spin-notes.md")).Dir(new Spec("Photon Sphere").File("postcard.txt"));
                break;
            case "few":
                eh = Spec.WithDirs("Event Horizon", 4).File(HazardRules.SampleMarkerName);
                break;
            case "crowded":
                eh = Spec.WithDirs("Event Horizon", 40).File(HazardRules.SampleMarkerName);
                for (var i = 0; i < 30; i++) eh.File($"f{i:D2}{(i % 3 == 0 ? ".png" : i % 3 == 1 ? ".dll" : ".txt")}", 1000 * i + 10);
                break;
            case "files":
                eh = Spec.WithFiles("Event Horizon", 120).File(HazardRules.SampleMarkerName);
                break;
            case "debris":
                eh = new Spec("Debris Field").File(HazardRules.SampleMarkerName).File("manifest.txt", 400);
                foreach (var name in Samples.SampleSky.DebrisFieldWorlds) eh.Dir(new Spec(name).File("log.txt", 80));
                break;
            default:
                eh = new Spec("Event Horizon").File(HazardRules.SampleMarkerName);
                break;
        }

        var t = new Spec("home").Dir(sky.Dir(eh).Dir(new Spec("Garden"))).Build();
        return (t, t.Find(kind == "debris" ? "Debris Field" : "Event Horizon").Id);
    }

    /// <summary>Points round a disc of <paramref name="metres"/> about <paramref name="centre"/> (and its centre).</summary>
    private static IEnumerable<Vector3> Disc(Vector3 centre, float metres, float radius, int count = 24)
    {
        yield return centre;
        var (a, b) = PlanetShape.Tangents(centre);
        for (var i = 0; i < count; i++)
        {
            var th = 2 * MathF.PI * i / count;
            yield return PlanetFrame.Move(centre, a * MathF.Cos(th) + b * MathF.Sin(th), metres, radius);
        }
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void A_hazard_world_is_islands_and_bridges_round_its_destinations(string kind)
    {
        var (t, id) = Hazard(kind);
        var c = PlanetComposer.Compose(t, id);
        c.Hazard.Should().NotBeNull();
        var shell = c.Surface.Shell!;
        shell.Should().NotBeNull();
        var r = c.Shape.Radius;
        var walkable = shell.WalkableFraction();
        output.WriteLine(F($"{kind}: radius {r:0.0} m, {shell.Lands.Count} lands, {shell.Spans.Count} bridges, {shell.Links.Count(l => l.Jump)} jumps, walkable {walkable:P0}, pads {c.Launchers.Count}, belt {c.SkyBodies.Count(s => s.InBelt)}, items {c.Items.Count}"));
        // Most of the sphere is open: DJ, 2026-11-16, "something closer to a third of the surface being walkable".
        walkable.Should().BeInRange(0.25f, 0.55f, $"{kind}: {walkable:P0} of the shell is ground");

        // Every destination's content stands on solid ground with the margin.
        const float margin = HazardTerrain.EdgeMarginMetres - 0.05f;
        foreach (var p in Disc(c.Arrival.Up, c.ArrivalRadius, r)) shell.EdgeMetres(p).Should().BeGreaterThan(margin, "the arrival meadow");
        var p0 = PlanetParams.Default;
        foreach (var l in c.Launchers)
        {
            foreach (var p in Disc(l.Direction, l.PadRadius + PlanetComposer.ReturnStandOff + 0.35f, r)) shell.EdgeMetres(p).Should().BeGreaterThan(margin, $"{l.Name}'s launcher and its return stand point");
            if (!l.IsParent) shell.OverHole(c.ArrivalFor(l.TargetId).Up).Should().BeFalse("coming back from a child, you stand on solid ground");
        }

        foreach (var p in Disc(c.Landmark.Direction, c.Landmark.Radius, r)) shell.EdgeMetres(p).Should().BeGreaterThan(margin, "the landmark");
        foreach (var p in Disc(c.Overlook.Direction, c.Overlook.Radius, r)) shell.EdgeMetres(p).Should().BeGreaterThan(margin, "the overlook");
        foreach (var s in c.Overlook.Steps) shell.EdgeMetres(s.Direction).Should().BeGreaterThan(s.Radius, "the overlook's stones");
        foreach (var it in c.Items) shell.EdgeMetres(it.Direction).Should().BeGreaterThan(it.Radius + 0.95f, "files stand back from the edges");
        foreach (var d in c.Decorations.Where(d => d.Collides)) shell.OverHole(d.Direction).Should().BeFalse("scenery stands on ground");

        // Straight ahead of the arrival the ground falls away on both sides of the ridge, and the jet's openings are open sky.
        foreach (var exit in new[] { shell.JetAxis, -shell.JetAxis })
        {
            foreach (var p in Disc(exit, HazardTerrain.JetClearMetres - 0.3f, r)) shell.OverHole(p).Should().BeTrue($"{kind}: the jet's opening is open sky");
        }

        // Straight ahead the ground ends a few steps from the arrival (the jet's opening); the ridge runs ahead-right to the
        // overlook's foot, with open sky on both sides of its middle.
        var drop = Enumerable.Range(1, 60).Select(i => PlanetFrame.FromPolar(i * 0.25f / r, 0)).First(shell.OverHole);
        (PlanetFrame.PolarOf(drop) * r).Should().BeLessThan(6.5f, $"{kind}: the drop straight ahead is close");
        var foot = shell.Waypoints.OrderBy(w => PlanetShape.Angle(w, c.Overlook.Direction)).First();
        (PlanetFrame.AzimuthOf(foot) * 180 / MathF.PI).Should().BeApproximately(32f, 1f, "the ridge runs ahead-right");
        var ridge = shell.Spans.First(s => (s.A == Vector3.UnitY && s.B == foot) || (s.B == Vector3.UnitY && s.A == foot));
        var middle = ridge.PointAlong(ridge.LengthMetres * 0.45f);
        var across = Vector3.Cross(ridge.DirectionAt(middle), middle);
        foreach (var sign in new[] { -1f, 1f }) shell.OverHole(PlanetFrame.Move(middle, across * sign, ridge.HalfWidthMetres + ShellSpan.RagMetres + 1.5f, r)).Should().BeTrue($"{kind}: a drop beside the ridge");

        // Too many folders for the ground: at most HazardMaxPads pads; the rest are moons in the belt (click and G, or the list).
        var children = t[id].Children.Count(ch => t[ch].IsDirectory);
        var pads = c.Launchers.Count(l => !l.IsParent);
        pads.Should().BeLessThanOrEqualTo(10);
        (pads + c.SkyBodies.Count(s => s.InBelt) + c.OmittedFolders).Should().Be(children, "every folder is a pad, a moon or counted");
    }

    [Fact]
    public void A_crowded_hazard_world_puts_its_extra_folders_in_the_belt()
    {
        var (t, id) = Hazard("crowded");
        var c = PlanetComposer.Compose(t, id);
        c.Launchers.Count(l => !l.IsParent).Should().BeInRange(6, 10);
        c.SkyBodies.Count(s => s.InBelt).Should().BeGreaterThan(20);
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void Every_destination_is_reached_from_the_arrival_along_the_route(string kind)
    {
        var (t, id) = Hazard(kind);
        var c = PlanetComposer.Compose(t, id);
        var shell = c.Surface.Shell!;
        var r = c.Shape.Radius;
        var targets = c.Launchers.Select(l => (l.Name, l.Direction, l.IsParent)).ToList();
        targets.Add(("the overlook's foot", shell.Waypoints.OrderBy(w => PlanetShape.Angle(w, c.Overlook.Direction)).First(), false));
        foreach (var (name, target, isParent) in targets)
        {
            var route = shell.Route(c.Arrival.Up, target);
            route[^1].Point.Should().Be(target);
            if (isParent) route.Should().OnlyContain(s => !s.Jump, "the way back is a walk");
            var from = c.Arrival.Up;
            foreach (var step in route)
            {
                // Along each leg: on solid ground at least a half bridge in from the edge, except across a jump gap, which
                // is short and lies between two stretches of ground.
                var len = PlanetShape.Angle(from, step.Point) * r;
                var open = 0f;
                var longestOpen = 0f;
                for (var k = 0; k <= Math.Max(1, (int)(len / 0.1f)); k++)
                {
                    var p = Slerp(from, step.Point, k / MathF.Max(1, len / 0.1f));
                    var e = shell.EdgeMetres(p);
                    if (e <= 0) { open += 0.1f; longestOpen = MathF.Max(longestOpen, open); continue; }
                    open = 0;
                    if (!step.Jump) e.Should().BeGreaterThan(HazardTerrain.MinBridgeWidthMetres / 2 - 0.05f, $"{kind}: the way to {name} keeps to the middle of the ground");
                }

                if (step.Jump) longestOpen.Should().BeInRange(HazardTerrain.MinJumpGapMetres - 0.15f, HazardTerrain.MaxJumpGapMetres + 0.15f, $"{kind}: the gap on the way to {name}");
                else longestOpen.Should().Be(0, $"{kind}: no open sky on a walking leg to {name}");
                from = step.Point;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void Bridges_are_at_least_the_walkable_width_and_some_have_a_gap_to_jump(string kind)
    {
        var (t, id) = Hazard(kind);
        var shell = PlanetComposer.Compose(t, id).Surface.Shell!;
        foreach (var s in shell.Spans)
        {
            (2 * s.HalfWidthMetres).Should().BeGreaterThanOrEqualTo(HazardTerrain.MinBridgeWidthMetres);
            for (var along = 0f; along <= s.LengthMetres; along += 0.2f)
            {
                if (s.Gaps.Any(g => along > g.FromMetres - 0.05f && along < g.ToMetres + 0.05f)) continue;
                var p = s.PointAlong(along);
                var side = Vector3.Cross(s.DirectionAt(p), p);
                foreach (var sign in new[] { -1f, 1f })
                {
                    var q = PlanetFrame.Move(p, side * sign, HazardTerrain.MinBridgeWidthMetres / 2 - 0.02f, shell.Radius);
                    shell.OverHole(q).Should().BeFalse($"{kind}: the bridge is {HazardTerrain.MinBridgeWidthMetres} m wide at {along:0.0} m");
                }
            }

            foreach (var (g0, g1) in s.Gaps)
            {
                (g1 - g0).Should().BeInRange(HazardTerrain.MinJumpGapMetres, HazardTerrain.MaxJumpGapMetres);
                shell.OverHole(s.PointAlong((g0 + g1) / 2)).Should().BeTrue("a gap is open sky");
            }
        }

        shell.Links.Count(l => l.Jump).Should().BeInRange(1, 3, $"{kind}: a few gaps worth lining up");
    }

    // ------------------------------------------------------------------ walking, jumping and falling (the real walker)

    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

    /// <summary>
    /// Follows a route as a player would: heads straight for each waypoint in turn and, on a leg with a gap, jumps (Space
    /// held for 0.3 s) once the edge is <paramref name="pressBeforeMetres"/> ahead. Returns whether it got there without
    /// committing to a fall, and the lowest the feet went.
    /// </summary>
    private static (bool Reached, float Lowest, int Jumps) FollowRoute(SphereWalker w, BrokenShell shell, IReadOnlyList<RouteStep> route, float pressBeforeMetres, bool run, float seconds = 60)
    {
        var lowest = 0f;
        var jumps = 0;
        var holdFrames = 0;
        var frames = 0;
        foreach (var step in route)
        {
            var last = step.Point == route[^1].Point;
            while (frames++ < seconds * 60)
            {
                var dist = w.Surface.SurfaceDistance(w.Up, step.Point);
                if (dist < (last ? 0.4f : 0.6f) && w.Grounded) break;
                var wish = step.Point - w.Up * Vector3.Dot(step.Point, w.Up);
                wish = wish.LengthSquared() > 1e-10f ? Vector3.Normalize(wish) : Vector3.Zero;
                var press = false;
                if (step.Jump && w.Grounded && holdFrames == 0)
                {
                    var ahead = PlanetFrame.Move(w.Up, wish, pressBeforeMetres, shell.Radius);
                    if (shell.OverHole(ahead)) { press = true; holdFrames = 18; jumps++; }
                }

                var held = holdFrames > 0;
                if (holdFrames > 0) holdFrames--;
                w.Update(new SphereWalkInput(wish, run, press, held), Frame);
                lowest = MathF.Min(lowest, w.Height.Value);
                if (w.Height.Value < -PlanetSurface.FallThroughMetres) return (false, lowest, jumps);
            }
        }

        return (w.Surface.SurfaceDistance(w.Up, route[^1].Point) < 0.6f, lowest, jumps);
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void The_walker_follows_the_route_to_every_launcher_jumping_the_gaps(string kind)
    {
        var (t, id) = Hazard(kind);
        var c = PlanetComposer.Compose(t, id);
        var shell = c.Surface.Shell!;
        var jumped = 0;
        foreach (var l in c.Launchers)
        {
            foreach (var run in new[] { false, true })
            {
                var w = new SphereWalker();
                w.Teleport(c.Surface, c.Arrival.Up, c.Arrival.Facing);
                var route = shell.Route(c.Arrival.Up, l.Direction);
                var (reached, lowest, jumps) = FollowRoute(w, shell, route, 0.7f, run);
                reached.Should().BeTrue($"{kind}: {l.Name} is reached {(run ? "running" : "walking")} along the route");
                lowest.Should().BeGreaterThan(-PlanetSurface.FallThroughMetres, $"{kind}: never through the rim on the way to {l.Name}");
                w.Grounded.Should().BeTrue();
                jumps.Should().Be(route.Count(s => s.Jump), "one jump per gap");
                jumped += jumps;
            }
        }

        output.WriteLine($"{kind}: {jumped} jumps along the routes to {c.Launchers.Count} launchers");
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void Every_gap_is_cleared_with_margin_whenever_you_press_jump_near_its_edge(string kind)
    {
        var (t, id) = Hazard(kind);
        var c = PlanetComposer.Compose(t, id);
        var shell = c.Surface.Shell!;
        foreach (var link in shell.Links.Where(l => l.Jump))
        {
            var s = shell.Spans[link.Span];
            var (g0, g1) = s.Gaps[0];
            // Both ways across, walking and running, pressing Space from just before the edge to 1.3 m before it.
            foreach (var (from, to) in new[] { (s.PointAlong(MathF.Max(0, g0 - 4f)), s.PointAlong(MathF.Min(s.LengthMetres, g1 + 3f))), (s.PointAlong(MathF.Min(s.LengthMetres, g1 + 4f)), s.PointAlong(MathF.Max(0, g0 - 3f))) })
            {
                foreach (var press in new[] { 0.25f, 0.7f, 1.3f })
                {
                    foreach (var run in new[] { false, true })
                    {
                        var w = new SphereWalker();
                        w.Teleport(c.Surface, from, Vector3.Normalize(to - from * Vector3.Dot(to, from)));
                        var (reached, lowest, _) = FollowRoute(w, shell, [new RouteStep(to, true)], press, run, 8);
                        reached.Should().BeTrue($"{kind}: across the {g1 - g0:0.00} m gap, Space {press} m before the edge, {(run ? "running" : "walking")}");
                        lowest.Should().BeGreaterThan(-PlanetSurface.FallThroughMetres);
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void Falls_happen_only_off_the_shell(string kind)
    {
        var (t, id) = Hazard(kind);
        var c = PlanetComposer.Compose(t, id);
        var shell = c.Surface.Shell!;
        var falls = 0;
        for (var bearing = 0; bearing < 360; bearing += 15)
        {
            var w = new SphereWalker();
            w.Teleport(c.Surface, c.Arrival.Up, PlanetFrame.Facing(c.Arrival.Up, MathF.PI + bearing * MathF.PI / 180f));
            var committedAt = -1;
            for (var i = 0; i < 60 * 25; i++)
            {
                // Keep heading the same way (parallel transport keeps Facing; the wish follows it).
                w.Update(new SphereWalkInput(w.Facing, true, false, false), Frame);
                if (committedAt >= 0)
                {
                    if (i - committedAt > 60) break;
                    continue;
                }

                // Skimming a tooth's tip dips a few centimetres and lands again; on solid ground nothing sinks further.
                if (!shell.OverHole(w.Up)) w.Height.Value.Should().BeGreaterThan(-PlanetSurface.FallThroughMetres, $"{kind}, bearing {bearing}: solid ground holds");
                if (w.Height.Value < -PlanetSurface.FallThroughMetres)
                {
                    committedAt = i;
                    shell.EdgeMetres(w.Up).Should().BeLessThan(1.5f, "a fall starts at an edge");
                }
            }

            if (committedAt < 0) continue;
            falls++;
            w.Grounded.Should().BeFalse($"{kind}, bearing {bearing}: past the edge there is nothing to stand on");
            w.Height.Value.Should().BeLessThan(-3f, "a second after stepping off, the walker is well into the fall");
            w.Support.Value.Should().Be(PlanetSurface.NoGroundMetres);
        }

        falls.Should().BeGreaterThan(12, "most straight walks from the arrival end in open sky");
    }

    [Fact]
    public void Ordinary_worlds_have_no_shell()
    {
        var t = new Spec("home").Dir(new Spec("Windows").File("a.txt")).Dir(new Spec("proc")).Build();
        foreach (var n in t.Nodes.Where(n => n.IsDirectory))
        {
            var c = PlanetComposer.Compose(t, n.Id);
            c.Hazard.Should().BeNull();
            c.Surface.Shell.Should().BeNull();
        }
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void Composition_is_deterministic(string kind)
    {
        var (t, id) = Hazard(kind);
        var a = PlanetComposer.Compose(t, id).Surface.Shell!;
        var b = PlanetComposer.Compose(t, id).Surface.Shell!;
        a.Lands.Select(l => (l.Centre, l.RadiusRadians, l.Seed)).Should().Equal(b.Lands.Select(l => (l.Centre, l.RadiusRadians, l.Seed)));
        a.Spans.Select(s => (s.A, s.B, s.HalfWidthMetres, s.Gaps.Count)).Should().Equal(b.Spans.Select(s => (s.A, s.B, s.HalfWidthMetres, s.Gaps.Count)));
        a.JetAxis.Should().Be(b.JetAxis);
    }

    [Fact]
    public void A_land_edge_is_jagged_and_stays_within_its_bounds()
    {
        var h = new ShellLand(Vector3.Normalize(new Vector3(0.3f, -1, 0.2f)), 0.3f, 42);
        var samples = Enumerable.Range(0, 720).Select(i => h.BoundaryRadians(i * MathF.PI / 360f)).ToArray();
        samples.Should().OnlyContain(r => r >= h.MinRadiusRadians - 1e-5f && r <= h.MaxRadiusRadians + 1e-5f);
        (samples.Max() - samples.Min()).Should().BeGreaterThan(0.12f * h.RadiusRadians, "the rim is visibly broken, not a circle");
        // Sharp teeth: the slope changes sign many times round the rim.
        var turns = 0;
        for (var i = 2; i < samples.Length; i++) if (MathF.Sign(samples[i] - samples[i - 1]) != MathF.Sign(samples[i - 1] - samples[i - 2])) turns++;
        turns.Should().BeGreaterThan(20);
        // The boundary point is on the edge: just inside is ground, just outside is not.
        for (var i = 0; i < 36; i++)
        {
            var th = i * MathF.PI / 18f;
            var e = h.BoundaryPoint(th);
            h.InsideRadians(e).Should().BeApproximately(0, 1e-3f);
            h.Contains(Vector3.Normalize(Vector3.Lerp(e, h.Centre, 0.02f))).Should().BeTrue();
            h.Contains(Vector3.Normalize(Vector3.Lerp(e, h.Centre, -0.02f))).Should().BeFalse();
        }
    }

    [Fact]
    public void Standing_still_at_an_edge_holds_until_the_centre_is_past_it()
    {
        var (t, id) = Hazard("sample");
        var c = PlanetComposer.Compose(t, id);
        var shell = c.Surface.Shell!;
        // The arrival island's edge straight left (inside the opening, so no bridge joins there).
        var edge = Enumerable.Range(1, 400).Select(i => PlanetFrame.FromPolar(i * 0.025f / c.Shape.Radius, -25f * MathF.PI / 180f)).First(shell.OverHole);
        var inward = Vector3.Normalize(Vector3.UnitY - edge * edge.Y);
        var solid = PlanetFrame.Move(edge, inward, 0.25f, c.Shape.Radius);
        var w = new SphereWalker();
        w.Teleport(c.Surface, solid, -inward);
        for (var i = 0; i < 60; i++) w.Update(SphereWalkInput.None, Frame);
        w.Grounded.Should().BeTrue("25 cm from the edge is still ground");
        w.Teleport(c.Surface, PlanetFrame.Move(edge, -inward, 0.2f, c.Shape.Radius), -inward);
        w.Height.Value.Should().Be(0, "teleported over open sky, the walker is put at the rim's level, not 1 km down");
        for (var i = 0; i < 30; i++) w.Update(SphereWalkInput.None, Frame);
        w.Grounded.Should().BeFalse();
        w.Height.Value.Should().BeLessThan(-0.5f);
    }

    [Fact]
    public void Arc_clear_tells_a_safe_hop_from_one_across_open_sky()
    {
        var (t, id) = Hazard("sample");
        var c = PlanetComposer.Compose(t, id);
        var shell = c.Surface.Shell!;
        var r = c.Shape.Radius;
        shell.ArcClear(PlanetFrame.FromPolar(12f / r, -40f * MathF.PI / 180f), PlanetFrame.FromPolar(12f / r, 40f * MathF.PI / 180f), 1f).Should().BeFalse("across the opening ahead");
        shell.ArcClear(Vector3.UnitY, PlanetFrame.FromPolar(2f / r, 1f), 1f).Should().BeTrue("on the arrival's island");
    }

    private static string F(FormattableString s) => FormattableString.Invariant(s);

    private static Vector3 Slerp(Vector3 a, Vector3 b, float t)
    {
        var ang = PlanetShape.Angle(a, b);
        if (ang < 1e-5f) return b;
        var s = MathF.Sin(ang);
        return Vector3.Normalize(a * (MathF.Sin((1 - t) * ang) / s) + b * (MathF.Sin(t * ang) / s));
    }
}
