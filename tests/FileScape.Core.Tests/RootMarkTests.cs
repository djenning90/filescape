using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Movement;
using FileScape.Core.Planets;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-45 (AD-58): a filesystem root's landmark is solid and stands clear of every walking route. The straight walk
/// from the arrival to every launcher never touches it; walking into it stops you; only roots have one; placement is
/// deterministic.
/// </summary>
public class RootMarkTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

    /// <summary>The walker's body radius, metres (SphereWalker's default).</summary>
    private const float BodyMetres = 0.35f;

    /// <summary>Linux roots, Windows drives (empty, small, crowded) and a share: the roots the hub offers.</summary>
    public static IEnumerable<object[]> Roots() =>
    [
        ["ubuntu"], ["debian-small"], ["c-drive"], ["d-empty"], ["crowded-drive"], ["share"],
    ];

    private static (FsTree Tree, int Id) Root(string kind)
    {
        Spec root;
        switch (kind)
        {
            case "ubuntu":
                root = new Spec("Ubuntu-24.04") { Location = LocationKind.Wsl };
                foreach (var n in new[] { "bin", "boot", "dev", "etc", "home", "lib", "lib64", "lost+found", "media", "mnt", "opt", "proc", "root", "run", "sbin", "snap", "srv", "sys", "tmp", "usr", "var" })
                {
                    root = root.Dir(n == "usr" ? Spec.WithFiles(n, 40) : new Spec(n));
                }

                root = root.File("init");
                break;
            case "debian-small":
                root = new Spec("Debian") { Location = LocationKind.Wsl }.Dir(new Spec("home")).Dir(new Spec("etc")).Dir(new Spec("tmp"));
                break;
            case "c-drive":
                root = new Spec(@"C:\") { Location = LocationKind.Drive };
                foreach (var n in new[] { "$Recycle.Bin", "Program Files", "Program Files (x86)", "ProgramData", "Recovery", "System Volume Information", "Users", "Windows", "tools", "src" }) root = root.Dir(new Spec(n));
                root = root.File("pagefile.sys", 8_000_000_000).File("DumpStack.log");
                break;
            case "d-empty":
                root = new Spec(@"D:\") { Location = LocationKind.Drive };
                break;
            case "crowded-drive":
                root = new Spec(@"E:\") { Location = LocationKind.Drive };
                for (var i = 0; i < 40; i++) root = root.Dir(Spec.WithFiles($"folder-{i:D2}", i % 7));
                for (var i = 0; i < 30; i++) root = root.File($"file-{i:D2}.txt", 1000 + i);
                break;
            default:
                root = new Spec(@"\\server\share") { Location = LocationKind.Network }.Dir(new Spec("team")).Dir(new Spec("builds"));
                break;
        }

        var t = new Spec("This computer") { Location = LocationKind.Hub }.Dir(root).Build();
        return (t, t.Root.Children[0]);
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public void A_root_has_its_mark_solid_and_clear_of_every_route(string kind)
    {
        var (t, id) = Root(kind);
        var c = PlanetComposer.Compose(t, id);
        var mark = c.RootMark;
        mark.Should().NotBeNull($"{kind} is a filesystem root");
        mark!.Kind.Should().Be(t[id].Location == LocationKind.Wsl ? WorldKind.WslRoot : WorldKind.WindowsRoot);
        mark.ClearanceMetres.Should().BeGreaterThanOrEqualTo(0, $"{kind}: the mark keeps clear of routes, pads, rings, landmark and overlook");
        foreach (var s in mark.Solids) c.Surface.Obstacles.Should().Contain(s, "every solid of the mark collides");
        mark.Solids.Should().OnlyContain(s => s.Top > new PlanetParams().JumpBoostHeight, "it cannot be climbed or stood on");
        // Its front faces the arrival: the facing tangent points at the pole.
        Vector3.Dot(mark.Facing, PlanetFrame.North(mark.Direction)).Should().BeGreaterThan(0.999f);
        // Nothing else stands in its footprint.
        foreach (var it in c.Items) c.Surface.SurfaceDistance(it.Direction, mark.Direction).Should().BeGreaterThan(RootMarkLayout.Footprint(mark.Kind) + it.Radius, $"{kind}: {it.NodeId} is clear of the mark");
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public void The_straight_walk_to_every_launcher_never_touches_the_mark(string kind)
    {
        var (t, id) = Root(kind);
        var c = PlanetComposer.Compose(t, id);
        var mark = c.RootMark!;
        foreach (var l in c.Launchers)
        {
            var w = new SphereWalker();
            w.Teleport(c.Surface, c.Arrival.Up, c.Arrival.Facing);
            var nearest = float.MaxValue;
            var reached = WalkTo(w, l.Direction, l.PadRadius * 0.6f, 30, () => nearest = MathF.Min(nearest, mark.Solids.Min(s => c.Surface.SurfaceDistance(w.Up, s.Direction) - s.Radius)));
            reached.Should().BeTrue($"{kind}: {l.Name} is reached on foot");
            nearest.Should().BeGreaterThan(BodyMetres, $"{kind}: the walk to {l.Name} passes the mark without touching it");
        }

        // And the walk back to the arrival from the way back's pad, and to the overlook's foot.
        var foot = c.Paths.Single(p => p.TargetId == -1).Points[^2];
        var w2 = new SphereWalker();
        w2.Teleport(c.Surface, c.Arrival.Up, c.Arrival.Facing);
        WalkTo(w2, foot, 0.5f, 30).Should().BeTrue($"{kind}: the overlook's route is open");
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public void Walking_into_the_mark_stops_at_its_face(string kind)
    {
        var (t, id) = Root(kind);
        var c = PlanetComposer.Compose(t, id);
        var mark = c.RootMark!;
        var w = new SphereWalker();
        // Stand 3 m in front of it, facing it, and walk at it for 4 s.
        var start = PlanetFrame.Move(mark.Direction, mark.Facing, 3f, c.Shape.Radius);
        var toward = mark.Direction - start * Vector3.Dot(mark.Direction, start);
        w.Teleport(c.Surface, start, Vector3.Normalize(toward));
        var nearest = float.MaxValue;
        for (var i = 0; i < 240; i++)
        {
            var wish = mark.Direction - w.Up * Vector3.Dot(mark.Direction, w.Up);
            w.Update(new SphereWalkInput(Vector3.Normalize(wish), true, false, false), Frame);
            nearest = MathF.Min(nearest, c.Surface.SurfaceDistance(w.Up, mark.Direction));
        }

        nearest.Should().BeGreaterThan(RootMarkLayout.SolidRadiusMetres + BodyMetres - 0.05f, $"{kind}: the walker does not pass into the mark");
        Vector3.Dot(w.Up - mark.Direction, mark.Facing).Should().BeGreaterThan(0, $"{kind}: still on the arrival's side of it");
        w.Grounded.Should().BeTrue();
    }

    [Fact]
    public void Only_roots_have_a_mark_and_a_root_that_is_a_repository_has_its_workbench_instead()
    {
        var repoRoot = new Spec(@"D:\") { Location = LocationKind.Drive }.Dir(new Spec(".git")).Dir(new Spec("src"));
        var t = new Spec("This computer") { Location = LocationKind.Hub }.Dir(repoRoot.Dir(new Spec("lib"))).Dir(new Spec("Ubuntu") { Location = LocationKind.Wsl }.Dir(new Spec("home").Dir(new Spec("dj")))).Build();
        PlanetComposer.Compose(t, 0).RootMark.Should().BeNull("the hub");
        PlanetComposer.Compose(t, t.Find(@"D:\").Id).RootMark.Should().BeNull("a repository first");
        PlanetComposer.Compose(t, t.Find("home").Id).RootMark.Should().BeNull("a folder");
        PlanetComposer.Compose(t, t.Find("Ubuntu").Id).RootMark!.Kind.Should().Be(WorldKind.WslRoot);
        // A probe can ask for a root's look on any folder (it photographs the look without entering a real root).
        PlanetComposer.Compose(t, t.Find("dj").Id, rootMarkKind: WorldKind.WindowsRoot).RootMark!.Kind.Should().Be(WorldKind.WindowsRoot);
    }

    [Theory]
    [InlineData("debian-small")]
    [InlineData("d-empty")]
    [InlineData("share")]
    public void On_a_root_with_few_folders_it_stands_beside_the_arrival_in_front_deterministically(string kind)
    {
        var (t, id) = Root(kind);
        var c = PlanetComposer.Compose(t, id);
        var a = c.RootMark!;
        a.Should().BeEquivalentTo(PlanetComposer.Compose(t, id).RootMark, "the same tree, the same place");
        var original = MathF.Sqrt(RootMarkLayout.PreferredAheadMetres * RootMarkLayout.PreferredAheadMetres + RootMarkLayout.PreferredLeftMetres * RootMarkLayout.PreferredLeftMetres);
        c.Surface.SurfaceDistance(a.Direction, Vector3.UnitY).Should().BeApproximately(original, 0.05f, "inside the first ring, as before");
        MathF.Abs(PlanetFrame.AzimuthOf(a.Direction)).Should().BeLessThan(MathF.PI * 110 / 180, "in front of you as you land (within 110° of ahead)");
        a.ClearanceMetres.Should().BeGreaterThanOrEqualTo(RootMarkLayout.ComfortMetres - 0.01f, "room to walk round it");
    }

    [Theory]
    [InlineData("ubuntu")]
    [InlineData("c-drive")]
    [InlineData("crowded-drive")]
    public void On_a_crowded_root_it_moves_out_between_rings_rather_than_across_a_route(string kind)
    {
        var (t, id) = Root(kind);
        var c = PlanetComposer.Compose(t, id);
        var a = c.RootMark!;
        c.Surface.SurfaceDistance(a.Direction, Vector3.UnitY).Should().BeGreaterThan(new PlanetParams().RingFirst, "past the first ring, between rings");
        a.ClearanceMetres.Should().BeGreaterThanOrEqualTo(0);
    }

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
}
