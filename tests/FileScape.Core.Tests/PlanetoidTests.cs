using System.Numerics;
using FileScape.Core.Experiments;
using FileScape.Core.Places;
using FileScape.Core.Units;

namespace FileScape.Core.Tests;

/// <summary>
/// The planetoid experiment (PLANET-02): the chart that lets the existing flat walker run on a sphere, and travel from
/// one body to another. These test the feasibility claims in docs/planetoid-feasibility.md, not product behaviour.
/// </summary>
public class PlanetoidTests
{
    private static readonly TimeSpan Frame = Durations.FromSeconds(1 / 60.0);

    private static Planetoid Body(Vector3 centre, float radius = 40f) => new(centre, radius, 6f, Vector3.UnitY, -Vector3.UnitZ);

    [Fact]
    public void The_chart_maps_flat_place_coordinates_onto_the_sphere_and_back()
    {
        var b = Body(new Vector3(10, -5, 3));
        foreach (var c in new[] { new Vector2(0, 0), new Vector2(12, -7), new Vector2(-30, 25), new Vector2(60, 40), new Vector2(0, 100) })
        {
            var w = b.ToWorld(c, 1.75f);
            var (back, h) = b.ToChart(w);
            Vector2.Distance(back, c).Should().BeLessThan(0.01f);
            h.Should().BeApproximately(1.75f, 1e-3f);
            // Distance from the pole along the surface is preserved (azimuthal equidistant).
            var angle = MathF.Acos(Vector3.Dot(Vector3.Normalize(w - b.Center), b.Pole));
            (angle * b.Radius).Should().BeApproximately(c.Length(), 0.02f);
        }

        var (up, north, east) = b.FrameAt(new Vector2(20, -10));
        Vector3.Dot(up, north).Should().BeApproximately(0, 1e-3f);
        Vector3.Dot(up, east).Should().BeApproximately(0, 1e-3f);
        Vector3.Dot(north, east).Should().BeApproximately(0, 1e-2f);
    }

    [Fact]
    public void The_unchanged_walker_walks_round_a_body_with_its_feet_on_the_surface()
    {
        var b = Body(Vector3.Zero);
        var terrain = new PlaceTerrain([], [], [], b.Radius * 2.9f); // walkable to ≈ 166° from the pole
        var t = new PlanetoidTraveller([b], [terrain]);
        t.PlaceOn(0, Vector2.Zero, Degrees.Zero);
        for (var i = 0; i < 60 * 8; i++) t.Update(new WalkInput(1, 0, 0, false, false), Frame);
        var p = t.Position;
        p.Length().Should().BeApproximately(b.Radius, 0.01f, "the feet stay on the surface");
        // 8 s at 8 m/s along the chart's north = 64 m along a great circle from the pole.
        var angle = MathF.Acos(Vector3.Dot(Vector3.Normalize(p), b.Pole));
        (angle * b.Radius).Should().BeApproximately(64f, 1.5f);
        Vector3.Dot(t.Forward, t.Up).Should().BeApproximately(0, 1e-2f, "you look along the surface, not into it");
    }

    [Fact]
    public void A_launch_carries_you_to_the_other_body_and_you_land_walking()
    {
        var a = Body(Vector3.Zero);
        var c = Body(new Vector3(0, 30, -170), 32f);
        var terrains = new[] { new PlaceTerrain([], [], [], a.Radius * 2.9f), new PlaceTerrain([], [], [], c.Radius * 2.9f) };
        var t = new PlanetoidTraveller([a, c], terrains);
        t.PlaceOn(0, new Vector2(0, -20), Degrees.Zero);
        var from = t.Position;
        var dir = Vector3.Normalize(Vector3.Normalize(c.Center - from) + t.Up * 0.6f);
        var speed = PlanetoidTraveller.LaunchSpeed([a, c], from, dir, 1);
        speed.Should().NotBeNull("some launch speed reaches the other body");
        t.Launch(dir * speed!.Value);
        for (var i = 0; i < 60 * 20 && t.Body != 1; i++) t.Update(WalkInput.None, Frame);
        t.Body.Should().Be(1);
        t.Transfers.Should().Be(1);
        for (var i = 0; i < 60; i++) t.Update(WalkInput.None, Frame);
        t.Walker.Grounded.Should().BeTrue();
        Vector3.Distance(t.Position, c.Center).Should().BeApproximately(c.Radius, 0.01f);
    }
}
