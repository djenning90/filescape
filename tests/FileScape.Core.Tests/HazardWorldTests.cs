using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Planets;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-34, AD-53: which folders are hazard worlds (platform and path context, look-alikes that are not), and the broken
/// shell: the route is solid with a forgiving margin, falls happen only off the shell, and the straight way from the
/// arrival to anything on the planet never crosses an edge (so lists and hops do not either).
/// </summary>
public class HazardWorldTests
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
        WorldCharacter.Of(t, id, 0).Describe().Should().Contain("broken");
    }

    // ------------------------------------------------------------------ the broken shell

    /// <summary>Hazard worlds of every shape the composer meets: the sample, a sparse and a crowded system folder, files only, nothing at all.</summary>
    public static TheoryData<string> Worlds() => new() { "sample", "few", "crowded", "files", "empty" };

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
            default:
                eh = new Spec("Event Horizon").File(HazardRules.SampleMarkerName);
                break;
        }

        var t = new Spec("home").Dir(sky.Dir(eh).Dir(new Spec("Garden"))).Build();
        return (t, t.Find("Event Horizon").Id);
    }

    /// <summary>Metres from <paramref name="up"/> to the nearest hole's farthest tooth (a lower bound on the distance to any edge).</summary>
    private static float Clearance(BrokenShell shell, Vector3 up) =>
        shell.Holes.Min(h => (PlanetShape.Angle(up, h.Centre) - h.MaxRadiusRadians) * shell.Radius);

    [Theory]
    [MemberData(nameof(Worlds))]
    public void A_hazard_world_is_broken_but_its_route_keeps_a_forgiving_margin(string kind)
    {
        var (t, id) = Hazard(kind);
        var c = PlanetComposer.Compose(t, id);
        c.Hazard.Should().NotBeNull();
        var shell = c.Surface.Shell!;
        shell.Should().NotBeNull();
        shell.Holes.Should().HaveCountGreaterThanOrEqualTo(2, "a main breach and at least one side breach");
        // A sizeable part of the sphere is gone (Monte Carlo over a fixed Fibonacci set).
        var points = PlanetComposer.FibonacciSphere(4000);
        var open = points.Count(shell.OverHole) / (float)points.Length;
        open.Should().BeInRange(0.08f, 0.45f, $"{kind}: {open:P0} of the shell is open");
        const float margin = BrokenShell.SafeMarginMetres - 0.01f;
        var p = PlanetParams.Default;
        Clearance(shell, c.Arrival.Up).Should().BeGreaterThan(c.ArrivalRadius + margin);
        foreach (var l in c.Launchers)
        {
            Clearance(shell, l.Direction).Should().BeGreaterThan(p.LauncherClearRadius + margin, l.Name);
            // The walk to it from the arrival (a child's drawn route, the way back's straight walk).
            for (var k = 0; k <= 40; k++)
            {
                var a = Slerp(Vector3.UnitY, l.Direction, k / 40f);
                Clearance(shell, a).Should().BeGreaterThan(p.PathHalfWidth + margin, $"{l.Name} route at {k}/40");
            }

            var (back, _) = c.ArrivalFor(l.TargetId);
            if (!l.IsParent) shell.OverHole(back).Should().BeFalse("coming back from a child, you stand on solid ground");
        }

        // The rings are walking routes too.
        foreach (var polar in c.Launchers.Select(l => PlanetFrame.PolarOf(l.Direction)).Distinct())
        {
            for (var az = 0; az < 360; az += 3)
            {
                Clearance(shell, PlanetFrame.FromPolar(polar, az * MathF.PI / 180f)).Should().BeGreaterThan(p.PathHalfWidth + margin);
            }
        }

        Clearance(shell, c.Landmark.Direction).Should().BeGreaterThan(c.Landmark.Radius + margin);
        Clearance(shell, c.Overlook.Direction).Should().BeGreaterThan(c.Overlook.Radius + margin);
        foreach (var it in c.Items)
        {
            Clearance(shell, it.Direction).Should().BeGreaterThan(it.Radius + margin, "files stand back from the edges");
            shell.ArcClear(Vector3.UnitY, it.Direction, 1.4f).Should().BeTrue("the straight way from the arrival to a file crosses no hole (hops route through it)");
        }

        foreach (var d in c.Decorations.Where(d => d.Collides)) shell.OverHole(d.Direction).Should().BeFalse();
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

    [Fact]
    public void Composition_is_deterministic()
    {
        var (t, id) = Hazard("sample");
        var a = PlanetComposer.Compose(t, id).Surface.Shell!;
        var b = PlanetComposer.Compose(t, id).Surface.Shell!;
        a.Holes.Select(h => (h.Centre, h.RadiusRadians, h.Seed)).Should().Equal(b.Holes.Select(h => (h.Centre, h.RadiusRadians, h.Seed)));
    }

    [Fact]
    public void The_edge_profile_is_jagged_and_stays_within_its_bounds()
    {
        var h = new ShellHole(Vector3.Normalize(new Vector3(0.3f, -1, 0.2f)), 0.5f, 42);
        var samples = Enumerable.Range(0, 720).Select(i => h.BoundaryRadians(i * MathF.PI / 360f)).ToArray();
        samples.Should().OnlyContain(r => r >= h.MinRadiusRadians - 1e-5f && r <= h.MaxRadiusRadians + 1e-5f);
        (samples.Max() - samples.Min()).Should().BeGreaterThan(0.12f * h.RadiusRadians, "the rim is visibly broken, not a circle");
        // Sharp teeth: the slope changes sign many times round the rim.
        var turns = 0;
        for (var i = 2; i < samples.Length; i++) if (MathF.Sign(samples[i] - samples[i - 1]) != MathF.Sign(samples[i - 1] - samples[i - 2])) turns++;
        turns.Should().BeGreaterThan(20);
        // The boundary point is on the edge: just inside is a hole, just outside is ground.
        for (var i = 0; i < 36; i++)
        {
            var th = i * MathF.PI / 18f;
            var e = h.BoundaryPoint(th);
            h.OutsideRadians(e).Should().BeApproximately(0, 1e-3f);
            h.Contains(Vector3.Normalize(Vector3.Lerp(e, h.Centre, 0.02f))).Should().BeTrue();
            h.Contains(Vector3.Normalize(Vector3.Lerp(e, h.Centre, -0.02f))).Should().BeFalse();
        }
    }

    // ------------------------------------------------------------------ walking and falling

    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

    /// <summary>Walks towards <paramref name="target"/> (a unit direction) for up to <paramref name="seconds"/>; returns whether it got within <paramref name="within"/> metres.</summary>
    private static bool WalkTo(SphereWalker w, Vector3 target, float within, float seconds, Action? eachFrame = null)
    {
        for (var i = 0; i < seconds * 60; i++)
        {
            if (w.Surface.SurfaceDistance(w.Up, target) < within) return true;
            var wish = target - w.Up * Vector3.Dot(target, w.Up);
            w.Update(new SphereWalkInput(wish.LengthSquared() > 1e-8f ? Vector3.Normalize(wish) : Vector3.Zero, true, false, false), Frame);
            eachFrame?.Invoke();
        }

        return w.Surface.SurfaceDistance(w.Up, target) < within;
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void Walking_the_route_to_every_launcher_never_falls(string kind)
    {
        var (t, id) = Hazard(kind);
        var c = PlanetComposer.Compose(t, id);
        foreach (var l in c.Launchers)
        {
            var w = new SphereWalker();
            w.Teleport(c.Surface, c.Arrival.Up, c.Arrival.Facing);
            var lowest = 0f;
            var reached = WalkTo(w, l.Direction, l.PadRadius * 0.6f, 30, () => lowest = MathF.Min(lowest, w.Height.Value));
            reached.Should().BeTrue($"{kind}: {l.Name} is reached on foot");
            lowest.Should().BeGreaterThan(-0.05f, $"{kind}: never below the ground on the way to {l.Name}");
            w.Grounded.Should().BeTrue();
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

        falls.Should().BeGreaterThan(0, "some straight walks end over a hole");
    }

    [Fact]
    public void Standing_still_at_an_edge_holds_until_the_centre_is_past_it()
    {
        var (t, id) = Hazard("sample");
        var c = PlanetComposer.Compose(t, id);
        var h = c.Surface.Shell!.Holes[0];
        var edge = h.BoundaryPoint(0.3f);
        var outward = edge - h.Centre * Vector3.Dot(edge, h.Centre);
        var away = Vector3.Normalize(edge - h.Centre);
        var solid = PlanetFrame.Move(edge, away, 0.2f, c.Shape.Radius);
        var w = new SphereWalker();
        w.Teleport(c.Surface, solid, outward);
        for (var i = 0; i < 60; i++) w.Update(SphereWalkInput.None, Frame);
        w.Grounded.Should().BeTrue("20 cm from the edge is still ground");
        w.Teleport(c.Surface, PlanetFrame.Move(edge, -away, 0.2f, c.Shape.Radius), outward);
        w.Height.Value.Should().Be(0, "teleported over a hole, the walker is put at the rim's level, not 1 km down");
        for (var i = 0; i < 30; i++) w.Update(SphereWalkInput.None, Frame);
        w.Grounded.Should().BeFalse();
        w.Height.Value.Should().BeLessThan(-0.5f);
    }

    [Fact]
    public void Arc_clear_tells_a_safe_hop_from_one_across_a_hole()
    {
        var (t, id) = Hazard("sample");
        var shell = PlanetComposer.Compose(t, id).Surface.Shell!;
        var h = shell.Holes[0];
        var tangent = PlanetShape.Tangents(h.Centre).A;
        var a = PlanetFrame.Move(h.Centre, tangent, (h.MaxRadiusRadians * shell.Radius) + 2f, shell.Radius);
        var b = PlanetFrame.Move(h.Centre, -tangent, (h.MaxRadiusRadians * shell.Radius) + 2f, shell.Radius);
        shell.ArcClear(a, b, 1f).Should().BeFalse("straight across the hole");
        shell.ArcClear(Vector3.UnitY, PlanetFrame.FromPolar(0.5f, 1f), 1f).Should().BeTrue("near the arrival");
    }

    private static Vector3 Slerp(Vector3 a, Vector3 b, float t)
    {
        var ang = PlanetShape.Angle(a, b);
        if (ang < 1e-5f) return b;
        var s = MathF.Sin(ang);
        return Vector3.Normalize(a * (MathF.Sin((1 - t) * ang) / s) + b * (MathF.Sin(t * ang) / s));
    }
}
