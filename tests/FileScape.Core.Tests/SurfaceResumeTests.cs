using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Planets;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-144..145 (AD-92): where a resume after a legacy view stands you (<see cref="SurfaceResume.Place"/>, called by
/// <c>PlanetWorld.Resume</c>). DJ: "Restore the previous world, standing position, and camera when returning from a legacy
/// view. Handle an unsafe saved position with a nearby safe placement." Before AD-92 a jump, a twirl, a fall and any Hazard
/// World were not kept at all (you arrived at the meadow with the establishing shot); now they are, and these cases say
/// where you then stand: where you were when that is safe, else the nearest safe ground, else the arrival.
/// </summary>
public class SurfaceResumeTests
{
    private const float R = 22f;
    private const float Body = 0.35f;
    private static readonly FsTree Tree = new Spec("r").Dir(new Spec("a")).Build();

    private static SurfaceResume Saved(Vector3 up, float support = 0) =>
        new(Tree, Tree.Root.Children[0], -1, Vector3.Normalize(up), PlanetShape.Tangents(Vector3.Normalize(up)).A, -Vector3.UnitZ, 20f, 8f) { SupportMetres = support };

    /// <summary>Two round islands 3 m in radius joined by a 2 m wide bridge with a 2 m gap in it (a Hazard World in small).</summary>
    private static (PlanetSurface Surface, Vector3 A, Vector3 B, ShellSpan Span) Islands()
    {
        var a = PlanetFrame.FromPolar(0.45f, 0);
        var b = PlanetFrame.FromPolar(0.45f, MathF.PI);
        var lands = new List<ShellLand> { new(a, 3f / R, 1, 0), new(b, 3f / R, 2, 0) };
        var len = PlanetShape.Angle(a, b) * R;
        var span = new ShellSpan(a, b, 1.0f, R, 3, [(len / 2 - 1, len / 2 + 1)], ShellGapClass.Plain);
        var shell = new BrokenShell(lands, [a, b], [span], [new ShellLink(0, 1, 0, true)], Vector3.UnitX, R);
        return (new PlanetSurface(PlanetShape.Sphere(R), [], shell), a, b, span);
    }

    [Fact]
    public void On_a_whole_planet_with_nothing_new_you_stand_where_you_stood()
    {
        var surface = new PlanetSurface(PlanetShape.Sphere(R), []);
        var up = Vector3.Normalize(new Vector3(0.3f, 0.9f, 0.2f));
        var p = SurfaceResume.Place(Saved(up), surface, Vector3.UnitY, Body);
        p.Stand.Should().Be(ResumeStand.AsSaved);
        p.MovedMetres.Should().Be(0);
        (p.Up - up).Length().Should().BeLessThan(1e-6f);
    }

    [Fact]
    public void Standing_on_a_platform_resumes_on_it_but_a_new_tower_where_you_stood_moves_you_beside_it()
    {
        var up = Vector3.Normalize(new Vector3(0.1f, 1f, 0.05f));
        var platform = new PlanetSurface(PlanetShape.Sphere(R), [new SurfaceObstacle(up, 2f, 1.2f)]);
        SurfaceResume.Place(Saved(up, support: 1.2f), platform, Vector3.UnitY, Body).Stand.Should().Be(ResumeStand.AsSaved, "you stood on its top");

        var tower = new PlanetSurface(PlanetShape.Sphere(R), [new SurfaceObstacle(up, 1f, 3f, Id: 7)]);
        var p = SurfaceResume.Place(Saved(up, support: 0), tower, Vector3.UnitY, Body);
        p.Stand.Should().Be(ResumeStand.Nearby);
        p.Why.Should().Contain("stands there");
        tower.SurfaceDistance(p.Up, up).Should().BeGreaterThan(1f).And.BeLessThan(1f + Body * 0.6f + SafeStanding.SearchStepMetres + 0.01f, "just outside the tower's footprint");
        tower.SupportAt(p.Up, Body, float.MaxValue, 0).Should().Be(0);
    }

    [Fact]
    public void Mid_air_over_an_island_comes_down_where_you_were()
    {
        var (surface, a, _, _) = Islands();
        SurfaceResume.Place(Saved(a), surface, a, Body).Stand.Should().Be(ResumeStand.AsSaved, "a jump over solid ground lands there");
    }

    [Fact]
    public void Over_a_gap_or_at_the_rim_you_are_put_on_the_nearest_safe_ground()
    {
        var (surface, a, b, span) = Islands();
        var gap = span.PointAlong(PlanetShape.Angle(a, b) * R / 2);
        surface.Shell!.OverHole(gap).Should().BeTrue();
        var p = SurfaceResume.Place(Saved(gap), surface, a, Body);
        p.Stand.Should().Be(ResumeStand.Nearby);
        p.Why.Should().Be("over open space");
        surface.Shell.OverHole(p.Up).Should().BeFalse();
        surface.Shell.EdgeMetres(p.Up).Should().BeGreaterThanOrEqualTo(SafeStanding.PlaceClearMetres);
        p.MovedMetres.Should().BeLessThan(SafeStanding.SearchMetres);

        // At the island's rim (inside by less than a body's width), on the side away from the bridge: moved inward, a little.
        var tangent = -Vector3.Normalize(b - a * Vector3.Dot(a, b));
        var rim = Vector3.Normalize(a * MathF.Cos(2.8f / R) + tangent * MathF.Sin(2.8f / R));
        surface.Shell.OverHole(rim).Should().BeFalse();
        surface.Shell.EdgeMetres(rim).Should().BeLessThan(SafeStanding.StandClearMetres);
        var q = SurfaceResume.Place(Saved(rim), surface, a, Body);
        q.Stand.Should().Be(ResumeStand.Nearby);
        q.Why.Should().Be("at the rim");
        surface.Shell.EdgeMetres(q.Up).Should().BeGreaterThanOrEqualTo(SafeStanding.PlaceClearMetres);
        q.MovedMetres.Should().BeLessThan(2.5f, "the island's safe middle is a metre or two in");
    }

    [Fact]
    public void Standing_on_the_bridge_itself_resumes_there()
    {
        var (surface, a, b, span) = Islands();
        var onBridge = span.PointAlong(PlanetShape.Angle(a, b) * R / 2 - 2.5f);
        surface.Shell!.OverHole(onBridge).Should().BeFalse();
        surface.Shell.EdgeMetres(onBridge).Should().BeGreaterThan(SafeStanding.StandClearMetres);
        SurfaceResume.Place(Saved(onBridge), surface, a, Body).Stand.Should().Be(ResumeStand.AsSaved, "a 2 m bridge's centre line is safe to stand on");
    }

    [Fact]
    public void Far_from_any_ground_you_arrive_at_the_meadow()
    {
        var (surface, a, _, _) = Islands();
        var far = Vector3.Normalize(-a + new Vector3(0, 0.01f, 0)); // the other side of the world: nothing within reach
        var p = SurfaceResume.Place(Saved(far), surface, a, Body);
        p.Stand.Should().Be(ResumeStand.Arrival);
        (p.Up - a).Length().Should().BeLessThan(1e-5f);
        p.Why.Should().Contain("no safe ground near");
    }

    [Fact]
    public void The_search_is_deterministic()
    {
        var (surface, a, b, span) = Islands();
        var gap = span.PointAlong(PlanetShape.Angle(a, b) * R / 2);
        SurfaceResume.Place(Saved(gap), surface, a, Body).Should().Be(SurfaceResume.Place(Saved(gap), surface, a, Body));
    }
}
