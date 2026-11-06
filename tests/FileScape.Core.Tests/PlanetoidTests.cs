using System.Numerics;
using FileScape.Core.Experiments;
using FileScape.Core.Interaction;
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

    /// <summary>PLANET-03: the guided transfer is several times faster than the slowest ballistic arc, and lands walking.</summary>
    [Fact]
    public void A_guided_transfer_is_fast_and_lands_walking_where_aimed()
    {
        var a = Body(Vector3.Zero);
        var c = Body(new Vector3(0, 30, -170), 32f);
        var terrains = new[] { new PlaceTerrain([], [], [], a.Radius * 2.9f), new PlaceTerrain([], [], [], c.Radius * 2.9f) };

        // Before: the experiment's pad speed (the slowest ballistic arc that arrives), flown under gravity.
        var ballistic = new PlanetoidTraveller([a, c], terrains);
        ballistic.PlaceOn(0, new Vector2(0, -18), Degrees.Zero);
        var from = ballistic.Position;
        var dir = Vector3.Normalize(Vector3.Normalize(c.Center - from) + ballistic.Up * 0.6f);
        ballistic.Launch(dir * PlanetoidTraveller.LaunchSpeed([a, c], from, dir, 1)!.Value);
        for (var i = 0; i < 60 * 30 && ballistic.Body != 1; i++) ballistic.Update(WalkInput.None, Frame);
        ballistic.Body.Should().Be(1);
        var before = ballistic.FlightSeconds;

        var t = new PlanetoidTraveller([a, c], terrains);
        t.PlaceOn(0, new Vector2(0, -18), Degrees.Zero);
        t.LaunchGuided(1, new Vector2(0, -18));
        t.Guided.Should().BeTrue();
        var minClearance = float.MaxValue;
        for (var i = 0; i < 60 * 5 && t.Body != 1; i++)
        {
            t.Update(WalkInput.None, Frame);
            if (t.Body < 0) minClearance = MathF.Min(minClearance, MathF.Min((t.Position - a.Center).Length() - a.Radius, (t.Position - c.Center).Length() - c.Radius));
        }

        t.Body.Should().Be(1, "it lands on the target world");
        t.Walker.Grounded.Should().BeTrue();
        new Vector2(t.Walker.Feet.X, t.Walker.Feet.Z).Should().Be(new Vector2(0, -18), "exactly where it was aimed");
        t.FlightSeconds.Should().BeLessThan(2.7f);
        t.FlightSeconds.Should().BeLessThan(before / 2, $"before: {before:0.0} s ballistic");
        minClearance.Should().BeGreaterThan(-0.3f, "the arc does not pass through either world");
    }

    /// <summary>
    /// The chart is not mirrored: at the pole of a body whose pole is +Y and north −Z (a flat place's own frame), chart
    /// east (+x) is world +X, and turning clockwise in the chart (place angles) turns clockwise seen from above.
    /// </summary>
    [Fact]
    public void The_chart_keeps_its_handedness_on_the_sphere()
    {
        var b = new Planetoid(Vector3.Zero, 40f, 6f, Vector3.UnitY, -Vector3.UnitZ);
        var east = b.ToWorld(new Vector2(1, 0)) - b.ToWorld(Vector2.Zero);
        var south = b.ToWorld(new Vector2(0, 1)) - b.ToWorld(Vector2.Zero);
        Vector3.Normalize(east).X.Should().BeApproximately(1f, 1e-3f, "chart +x is east, as in a flat place");
        Vector3.Normalize(south).Z.Should().BeApproximately(1f, 1e-3f, "chart +z is south");
        foreach (var at in new[] { Vector2.Zero, new Vector2(10, -5), new Vector2(-20, 15) })
        {
            var (up, north, e) = b.FrameAt(at);
            Vector3.Dot(Vector3.Cross(e, north), up).Should().BeGreaterThan(0.99f, $"east × north = up at {at} (right-handed, like the place)");
        }

        // A tilted body keeps the same handedness.
        var c = new Planetoid(new Vector3(0, 30, -170), 32f, 6f, Vector3.Normalize(new Vector3(0, 0.6f, 1)), Vector3.Normalize(new Vector3(0, 1, -0.6f)));
        var (u2, n2, e2) = c.FrameAt(new Vector2(3, 4));
        Vector3.Dot(Vector3.Cross(e2, n2), u2).Should().BeGreaterThan(0.99f);
    }

    /// <summary>The distortion stated in docs/planetoid-feasibility.md: distances across shrink by sin φ / φ.</summary>
    [Fact]
    public void Distances_across_shrink_by_sin_phi_over_phi()
    {
        var b = new Planetoid(Vector3.Zero, 40f, 6f, Vector3.UnitY, -Vector3.UnitZ);
        foreach (var s in new[] { 10f, 28f, 40f })
        {
            var phi = s / b.Radius;
            var a = b.ToWorld(new Vector2(0, s));
            var a2 = b.ToWorld(new Vector2(0.01f, s));
            var across = Vector3.Distance(a, a2) / 0.01f;
            across.Should().BeApproximately(MathF.Sin(phi) / phi, 2e-3f, $"at {s} m from the pole");
        }
    }

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
        // An explicit 8 m/s (the geometry is the point here, not the default speed, which changed to 5.5 m/s in KD-28).
        var t = new PlanetoidTraveller([b], [terrain], WalkParams.Default with { WalkSpeed = MetersPerSecond.Of(8f), RunSpeed = MetersPerSecond.Of(16f) });
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
