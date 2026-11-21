using System.Numerics;
using FileScape.Core.Movement;
using FileScape.Core.Planets;
using FileScape.Core.Units;

namespace FileScape.Core.Tests;

/// <summary>
/// Edge validation of the planet walker (SphereWalker): long laps, the poles and antipodes, collision anywhere on the
/// sphere, steps, the double jump onto a high top, hills and frame-rate independence (PLANET-11).
/// </summary>
public class SphereWalkerTests
{
    private static readonly TimeSpan Frame120 = Durations.FromSeconds(1 / 120.0);

    /// <summary>The unit direction <paramref name="surfaceMeters"/> along the surface from <paramref name="centre"/>, at an azimuth (degrees) in its tangent frame.</summary>
    private static Vector3 Offset(Vector3 centre, float azimuthDegrees, float surfaceMeters, float planetRadiusMeters)
    {
        var (a, b) = PlanetShape.Tangents(centre);
        var az = Degrees.Of(azimuthDegrees).ToRadians();
        var t = a * MathF.Cos(az) + b * MathF.Sin(az);
        var ang = surfaceMeters / planetRadiusMeters;
        return Vector3.Normalize(centre * MathF.Cos(ang) + t * MathF.Sin(ang));
    }

    /// <summary>The tangent direction at <paramref name="up"/> towards <paramref name="target"/> (zero if there).</summary>
    private static Vector3 Toward(Vector3 up, Vector3 target)
    {
        var t = target - up * Vector3.Dot(up, target);
        return t.LengthSquared() > 1e-12f ? Vector3.Normalize(t) : Vector3.Zero;
    }

    [Trait(TestKinds.Trait, TestKinds.Slow)]
    [Theory]
    [InlineData(0f, 1f, 0f, 1f, 0f, 0f)]  // from exactly +Y: through −Y (its antipode)
    [InlineData(0f, -1f, 0f, 0f, 0f, 1f)] // from exactly −Y: through +Y
    [InlineData(1f, 0f, 0f, 0f, 1f, 0f)]  // from +X towards +Y: through +Y, −X (the antipode) and −Y
    public void Ten_great_circle_laps_stay_on_the_surface_with_a_continuous_tangent_facing(float ux, float uy, float uz, float fx, float fy, float fz)
    {
        const float r = 20f;
        var surface = new PlanetSurface(PlanetShape.Sphere(r), []);
        var start = new Vector3(ux, uy, uz);
        var w = new SphereWalker();
        w.Teleport(surface, start, new Vector3(fx, fy, fz));
        var normal = Vector3.Normalize(Vector3.Cross(start, w.Facing)); // the great circle's plane
        var travelledRadians = 0f;
        var prevUp = w.Up;
        var prevFacing = w.Facing;
        float closestToPlusY = 9, closestToMinusY = 9, closestToAntipode = 9;
        var steps = 0;
        while (travelledRadians < 10 * 2 * MathF.PI)
        {
            w.Update(new SphereWalkInput(w.Facing, Run: true, false, false), Frame120);
            steps++;
            MathF.Abs(w.Up.Length() - 1).Should().BeLessThan(1e-4f, $"|Up| at step {steps}");
            MathF.Abs(w.Feet.Length() - r).Should().BeLessThan(1e-3f, $"feet on the surface at step {steps}");
            w.Grounded.Should().BeTrue();
            MathF.Abs(Vector3.Dot(w.Facing, w.Up)).Should().BeLessThan(1e-4f, "facing stays tangent");
            MathF.Abs(w.Facing.Length() - 1).Should().BeLessThan(1e-4f);
            Vector3.Dot(prevFacing, w.Facing).Should().BeGreaterThan(0.99f, $"no flip between consecutive steps (step {steps}, up {w.Up})");
            MathF.Abs(Vector3.Dot(w.Up, normal)).Should().BeLessThan(2e-3f, "walking straight stays on the great circle");
            travelledRadians += PlanetShape.Angle(prevUp, w.Up);
            closestToPlusY = MathF.Min(closestToPlusY, PlanetShape.Angle(w.Up, Vector3.UnitY) * r);
            closestToMinusY = MathF.Min(closestToMinusY, PlanetShape.Angle(w.Up, -Vector3.UnitY) * r);
            closestToAntipode = MathF.Min(closestToAntipode, PlanetShape.Angle(w.Up, -start) * r);
            prevUp = w.Up;
            prevFacing = w.Facing;
            steps.Should().BeLessThan(40_000);
        }

        closestToPlusY.Should().BeLessThan(0.1f, "the laps pass over +Y");
        closestToMinusY.Should().BeLessThan(0.1f, "the laps pass over −Y");
        closestToAntipode.Should().BeLessThan(0.1f, "the laps pass over the start's antipode");
        (travelledRadians * r / (steps / 120f)).Should().BeApproximately(11f, 0.1f, "running speed is the surface speed");
    }

    /// <summary>Walks past a tall obstacle at <paramref name="d"/> (offset 0.4 m sideways) and returns the end pose in the obstacle's frame.</summary>
    private static (Vector3 Up, Vector3 Facing, float ClosestMeters) PassObstacle(Vector3 d, Vector3 t)
    {
        const float r = 20f;
        var b = Vector3.Cross(d, t);
        var surface = new PlanetSurface(PlanetShape.Sphere(r), [new SurfaceObstacle(d, 1.0f, 2.0f)]);
        float a = 3f / r, lat = 0.4f / r;
        var start = Vector3.Normalize(d * MathF.Cos(a) * MathF.Cos(lat) - t * MathF.Sin(a) * MathF.Cos(lat) + b * MathF.Sin(lat));
        var w = new SphereWalker();
        w.Teleport(surface, start, t);
        var closest = 99f;
        for (var i = 0; i < 180; i++)
        {
            w.Update(new SphereWalkInput(t, false, false, false), Frame120);
            closest = MathF.Min(closest, surface.SurfaceDistance(w.Up, d));
        }

        Vector3 Local(Vector3 v) => new(Vector3.Dot(v, d), Vector3.Dot(v, t), Vector3.Dot(v, b));
        return (Local(w.Up), Local(w.Facing), closest);
    }

    [Fact]
    public void Collision_push_out_and_sliding_are_the_same_in_six_directions_including_the_poles()
    {
        (Vector3 D, Vector3 T)[] frames =
        [
            (Vector3.UnitY, Vector3.UnitX), (-Vector3.UnitY, Vector3.UnitZ),
            (Vector3.UnitX, Vector3.UnitZ), (-Vector3.UnitX, Vector3.UnitY),
            (Vector3.UnitZ, Vector3.UnitY), (-Vector3.UnitZ, Vector3.UnitX),
        ];
        var results = frames.Select(f => PassObstacle(f.D, f.T)).ToArray();
        var first = results[0];
        first.ClosestMeters.Should().BeGreaterThan(1.35f - 2e-3f, "the body's disc never enters the obstacle");
        first.ClosestMeters.Should().BeLessThan(1.36f, "it touched the obstacle and slid along it");
        first.Up.Y.Should().BeGreaterThan(0.05f, "it slid round and carried on past");
        foreach (var (res, i) in results.Select((x, i) => (x, i)))
        {
            Vector3.Distance(res.Up, first.Up).Should().BeLessThan(1e-4f, $"direction {frames[i].D}: same end position (to 2 mm)");
            Vector3.Distance(res.Facing, first.Facing).Should().BeLessThan(1e-3f, $"direction {frames[i].D}: same facing");
            res.ClosestMeters.Should().BeApproximately(first.ClosestMeters, 2e-3f);
        }
    }

    [Fact]
    public void A_spiral_of_0_35_m_steps_climbs_to_a_2_4_m_top_without_jumping()
    {
        const float r = 20f;
        var c = Vector3.Normalize(new Vector3(0.3f, 0.8f, -0.5f));
        var obstacles = new List<SurfaceObstacle> { new(c, 1.0f, 2.4f) };
        for (var k = 1; k <= 6; k++) obstacles.Add(new SurfaceObstacle(Offset(c, (k - 1) * 50f, 2.0f, r), 0.9f, 0.35f * k));
        var surface = new PlanetSurface(PlanetShape.Sphere(r), obstacles);
        var targets = obstacles.Skip(1).Select(o => o.Direction).Append(c).ToList();
        var w = new SphereWalker();
        w.Teleport(surface, Offset(c, -70f, 3.2f, r), Vector3.Zero);
        w.Height.Value.Should().Be(0);
        var next = 0;
        var highest = 0f;
        for (var i = 0; i < 120 * 20 && next < targets.Count; i++)
        {
            w.Update(new SphereWalkInput(Toward(w.Up, targets[next]), false, false, false), Frame120);
            (w.Height.Value >= highest - 1e-4f || !w.Grounded).Should().BeTrue("climbing never drops back a step");
            highest = MathF.Max(highest, w.Height.Value);
            if (surface.SurfaceDistance(w.Up, targets[next]) < 0.25f) next++;
        }

        next.Should().Be(targets.Count, "every step and the top were reached");
        for (var i = 0; i < 60; i++) w.Update(SphereWalkInput.None, Frame120);
        w.Grounded.Should().BeTrue();
        w.Height.Value.Should().BeApproximately(2.4f, 1e-4f);
        w.Support.Value.Should().BeApproximately(2.4f, 1e-4f);
        w.Jumps.Should().Be(0);
        (w.Feet.Length() - r).Should().BeApproximately(2.4f, 1e-3f);
    }

    /// <summary>Runs at a 2.2 m top (radius 1.5 m) from 2 m before its edge, jumps 1 m in and optionally boosts at the apex.</summary>
    private static SphereWalker RunAtTop(bool boost, Vector3 centre)
    {
        const float r = 20f;
        var surface = new PlanetSurface(PlanetShape.Sphere(r), [new SurfaceObstacle(centre, 1.5f, 2.2f)]);
        var w = new SphereWalker();
        var start = Offset(centre, 30f, 3.5f, r);
        w.Teleport(surface, start, Toward(start, centre));
        bool jumped = false, boosted = false;
        for (var i = 0; i < 240; i++)
        {
            var press = false;
            var edgeMeters = surface.SurfaceDistance(w.Up, centre) - 1.5f;
            if (!jumped && edgeMeters <= 1f) { press = true; jumped = true; }
            else if (boost && jumped && !boosted && !w.Grounded && w.VerticalVelocity.Value <= 0) { press = true; boosted = true; }
            var onTop = w.Grounded && w.Height.Value > 2f;
            w.Update(new SphereWalkInput(onTop ? Vector3.Zero : Toward(w.Up, centre), true, press, true), Durations.FromSeconds(1 / 60.0));
            if (press && boosted && boost) w.LastEvent.Kind.Should().Be(JumpEventKind.AirJump);
        }

        return w;
    }

    [Fact]
    public void Jump_plus_boost_lands_on_a_2_2_m_top_that_a_single_jump_cannot_reach()
    {
        foreach (var centre in new[] { Vector3.UnitY, Vector3.Normalize(new Vector3(-0.4f, 0.2f, 0.9f)) })
        {
            var single = RunAtTop(boost: false, centre);
            single.Jumps.Should().Be(1);
            single.Grounded.Should().BeTrue();
            single.Height.Value.Should().Be(0, "a single jump cannot reach 2.2 m");

            var both = RunAtTop(boost: true, centre);
            both.Jumps.Should().Be(1);
            both.AirJumps.Should().Be(1);
            both.Grounded.Should().BeTrue();
            both.Height.Value.Should().BeApproximately(2.2f, 1e-4f, "jump + boost lands on top");
            both.Landings.Should().Be(1);
        }
    }

    [Fact]
    public void Walking_over_hills_stays_grounded_with_the_feet_on_the_ground()
    {
        const float r = 20f;
        var shape = new PlanetShape(r, 7, 1.5f, []);
        var surface = new PlanetSurface(shape, []);
        foreach (var run in new[] { false, true })
        {
            var w = new SphereWalker();
            w.Teleport(surface, new Vector3(0.2f, 1, 0.1f), Vector3.UnitX);
            float lo = 99, hi = -99, walkedMeters = 0;
            var prev = w.Feet;
            for (var i = 0; i < 120 * 30; i++)
            {
                w.Update(new SphereWalkInput(w.Facing, run, false, false), Frame120);
                w.Grounded.Should().BeTrue($"step {i}");
                w.Height.Value.Should().BeApproximately(0, 1e-5f);
                (w.Feet.Length() - shape.GroundRadius(w.Up)).Should().BeApproximately(0, 1e-3f);
                lo = MathF.Min(lo, shape.HeightAt(w.Up));
                hi = MathF.Max(hi, shape.HeightAt(w.Up));
                walkedMeters += Vector3.Distance(prev, w.Feet);
                prev = w.Feet;
            }

            (hi - lo).Should().BeGreaterThan(0.5f, "the path crossed real hills");
            w.Landings.Should().Be(0);
            ((float)w.Footsteps).Should().BeApproximately(walkedMeters / 0.85f, walkedMeters / 0.85f * 0.05f + 2, "a footstep every ≈ 0.85 m");
        }
    }

    /// <summary>A scripted walk (piecewise-constant input on 1/12 s boundaries, shared by 60 and 144 Hz frames) and its end position.</summary>
    private static Vector3 ScriptedWalk(double frameSeconds)
    {
        const float r = 18f;
        var shape = new PlanetShape(r, 3, 1.2f, [(Vector3.UnitY, 3f)]);
        var surface = new PlanetSurface(shape, [new SurfaceObstacle(Offset(Vector3.UnitY, 90f, 6f, r), 1.2f, 3f), new SurfaceObstacle(Offset(Vector3.UnitY, 20f, 9f, r), 0.8f, 0.4f)]);
        var w = new SphereWalker();
        w.Teleport(surface, Vector3.UnitY, Vector3.UnitX);
        Vector3[] wishes = [new(1, 0, 0.2f), new(0.6f, 0, 1), new(-0.3f, 0.5f, 1), new(1, 0, -0.5f), new(0.2f, 0, 0.9f), new(-1, 0.2f, 0.3f), new(0.5f, 0, 0.5f), new(0, 0, 0)];
        var frames = (int)Math.Round(5.0 / frameSeconds);
        for (var f = 0; f < frames; f++)
        {
            var tick = (long)Math.Round(f * frameSeconds * 720); // time in 1/720 s, shared by 60 and 144 Hz frame boundaries
            var segment = (int)Math.Min(wishes.Length - 1, tick / 360);
            var press = tick is 720 or 900; // a jump at 1 s and a boost at 1.25 s
            w.Update(new SphereWalkInput(wishes[segment], Run: tick is >= 1440 and < 2160, press, JumpHeld: tick < 1080), Durations.FromSeconds(frameSeconds));
        }

        w.Jumps.Should().Be(1);
        w.AirJumps.Should().Be(1);
        return w.Feet;
    }

    [Fact]
    public void Behaviour_does_not_depend_on_the_frame_rate()
    {
        var at60 = ScriptedWalk(1 / 60.0);
        var at144 = ScriptedWalk(1 / 144.0);
        Vector3.Distance(at60, at144).Should().BeLessThan(0.01f, "60 and 144 Hz frames end at the same place (within 1 cm)");
        Vector3.Distance(at60, Vector3.UnitY * 18f).Should().BeGreaterThan(3f, "the script really moved");
    }

    [Fact]
    public void Stalls_longer_than_the_frame_limit_are_dropped()
    {
        var surface = new PlanetSurface(PlanetShape.Sphere(20), []);
        var w = new SphereWalker();
        w.Teleport(surface, Vector3.UnitZ, Vector3.UnitX);
        for (var i = 0; i < 60; i++) w.Update(new SphereWalkInput(Vector3.UnitX, false, false, false), Frame120);
        var before = w.Up;
        w.Update(new SphereWalkInput(Vector3.UnitX, false, false, false), TimeSpan.FromSeconds(5));
        (PlanetShape.Angle(before, w.Up) * 20).Should().BeLessThan(6f * 0.25f + 0.05f, "a 5 s hitch integrates at most 0.25 s");
    }

    [Fact]
    public void Teleport_stands_on_a_top_and_faces_along_the_surface()
    {
        var up = Vector3.Normalize(new Vector3(1, 1, 0));
        var surface = new PlanetSurface(PlanetShape.Sphere(15), [new SurfaceObstacle(up, 1f, 0.6f)]);
        var w = new SphereWalker();
        w.Teleport(surface, up * 3, Vector3.UnitY);
        w.Up.Length().Should().BeApproximately(1, 1e-6f);
        w.Height.Value.Should().BeApproximately(0.6f, 1e-6f);
        w.Grounded.Should().BeTrue();
        MathF.Abs(Vector3.Dot(w.Facing, w.Up)).Should().BeLessThan(1e-6f);
        w.Facing.Y.Should().BeGreaterThan(0.5f);
        w.Teleport(surface, Vector3.UnitY, Vector3.UnitY); // facing along up: any tangent
        MathF.Abs(Vector3.Dot(w.Facing, w.Up)).Should().BeLessThan(1e-6f);
        w.Facing.Length().Should().BeApproximately(1, 1e-5f);
    }

    [Fact]
    public void The_body_turns_smoothly_to_face_its_motion()
    {
        var surface = new PlanetSurface(PlanetShape.Sphere(20), []);
        var w = new SphereWalker();
        w.Teleport(surface, Vector3.UnitY, Vector3.UnitX);
        var wish = -Vector3.UnitX; // straight behind
        var maxTurnDegrees = 0f;
        var prev = w.Facing;
        for (var i = 0; i < 120; i++)
        {
            w.Update(new SphereWalkInput(wish, false, false, false), Frame120);
            maxTurnDegrees = MathF.Max(maxTurnDegrees, Degrees.FromRadians(PlanetShape.Angle(prev, w.Facing)).Value);
            prev = w.Facing;
        }

        maxTurnDegrees.Should().BeLessThanOrEqualTo(720f / 120 + 0.01f, "never faster than the turn rate");
        Vector3.Dot(w.Facing, Toward(w.Up, wish)).Should().BeGreaterThan(0.999f, "facing the motion after a second");
    }
}
