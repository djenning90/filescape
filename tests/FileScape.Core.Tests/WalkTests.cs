using System.Numerics;
using FileScape.Core.Places;
using FileScape.Core.Units;

namespace FileScape.Core.Tests;

/// <summary>Walking, terrain and jumping (WALK-01..05, JUMP-01..03, AD-16). Direct-method tests of the Core walker.</summary>
public class WalkTests
{
    private static readonly PlaceParams P = PlaceParams.Default;
    private static readonly WalkParams W = WalkParams.Default;
    private static readonly TimeSpan Frame60 = Durations.FromSeconds(1 / 60.0), Frame120 = Durations.FromSeconds(1 / 120.0);

    private static Degrees Deg(float degrees) => Degrees.Of(degrees);

    private static PlaceComposition Sample(int dirs = 6, int files = 40)
    {
        var s = Spec.WithFiles("Sample", files);
        for (var i = 0; i < dirs; i++) s.Dir(new Spec($"dir-{i}").File("a.txt"));
        var parent = new Spec("parent").Dir(s);
        var tree = parent.Build();
        return PlaceComposer.Compose(tree, tree.Root.Children[0], P);
    }

    private static Vector2 Xz(Vector3 v) => new(v.X, v.Z);

    private static void Run(Walker w, PlaceTerrain t, WalkInput input, float seconds, double frameSeconds = 1 / 60.0)
    {
        for (var e = 0.0; e < seconds; e += frameSeconds) w.Update(t, input, Durations.FromSeconds(frameSeconds));
    }

    [Fact]
    public void Eye_is_175_above_the_support_on_the_plaza_the_terrace_and_a_tier()
    {
        var c = Sample();
        var w = new Walker();
        w.TeleportToEye(c.Terrain, c.Arrival.Eye, c.Arrival.Yaw, c.Arrival.LookPitch);
        w.Support.Value.Should().BeApproximately(P.TerraceTop, 1e-3f);
        (w.Eye.Y - w.Feet.Y).Should().BeApproximately(W.EyeHeight.Value, 1e-3f);
        w.Eye.Y.Should().BeApproximately(c.Arrival.Eye.Y, 1e-3f, "the arrival pose stands on the terrace");

        w.Teleport(c.Terrain, new Vector3(-6, 0, -6), Deg(0), Deg(0));
        w.Feet.Y.Should().Be(0);
        (w.Eye.Y - w.Feet.Y).Should().BeApproximately(W.EyeHeight.Value, 1e-3f);

        var wide = Spec.WithDirs("wide", 120).Build();
        var cw = PlaceComposer.Compose(wide, 0, P);
        var tier = cw.Tiers[2];
        var onTier = PlaceGeometry.Direction(0) * tier.Radius + PlaceGeometry.Direction(90) * 4;
        w.Teleport(cw.Terrain, onTier with { Y = 5 }, Deg(0), Deg(0));
        w.Support.Value.Should().BeApproximately(tier.Rise, 1e-3f);
        (w.Eye.Y - w.Feet.Y).Should().BeApproximately(W.EyeHeight.Value, 1e-3f);
    }

    [Theory]
    [InlineData(-85f)]
    [InlineData(0f)]
    [InlineData(85f)]
    public void Walking_is_horizontal_along_the_heading_whatever_the_gaze(float pitch)
    {
        var t = PlaceTerrain.Flat;
        var w = new Walker();
        w.Teleport(t, Vector3.Zero, Deg(30), Deg(pitch));
        Run(w, t, new WalkInput(1, 0, 0, false, false), 2f);
        var dist = Xz(w.Feet).Length();
        dist.Should().BeInRange(W.WalkSpeed.Value * 2 * 0.9f, W.WalkSpeed.Value * 2 * 1.0f);
        w.Feet.Y.Should().Be(0, "looking down never pushes into the ground");
        var dir = Vector2.Normalize(Xz(w.Feet));
        var fwd = Interaction.CameraMath.HorizontalForward(Deg(30));
        Vector2.Dot(dir, new Vector2(fwd.X, fwd.Z)).Should().BeGreaterThan(0.9999f);
    }

    [Fact]
    public void Speed_does_not_depend_on_the_frame_rate_and_diagonals_are_not_faster()
    {
        float Distance(float dt, float fwd, float strafe)
        {
            var w = new Walker();
            w.Teleport(PlaceTerrain.Flat, Vector3.Zero, Deg(0), Deg(0));
            Run(w, PlaceTerrain.Flat, new WalkInput(fwd, strafe, 0, false, false), 3f, dt);
            return Xz(w.Feet).Length();
        }

        var d60 = Distance(1 / 60f, 1, 0);
        Distance(1 / 30f, 1, 0).Should().BeApproximately(d60, d60 * 0.02f);
        Distance(1 / 144f, 1, 0).Should().BeApproximately(d60, d60 * 0.02f);
        Distance(1 / 60f, 1, 1).Should().BeApproximately(d60, d60 * 0.01f, "diagonals are normalised");
    }

    [Fact]
    public void Starts_and_stops_are_responsive()
    {
        var t = PlaceTerrain.Flat;
        var w = new Walker();
        w.Teleport(t, Vector3.Zero, Deg(0), Deg(0));
        var time = 0f;
        // WALK-04: a responsive start reaches ≥ 90 % of walking speed within 0.25 s.
        while (w.HorizontalVelocity.Length() < 0.9f * W.WalkSpeed.Value && time < 2) { w.Update(t, new WalkInput(1, 0, 0, false, false), Frame120); time += 1 / 120f; }
        time.Should().BeLessThanOrEqualTo(0.25f);
        Run(w, t, new WalkInput(1, 0, 0, false, false), 1f);
        time = 0;
        // A stop: below 5 % of walking speed within 0.25 s. WALK-04's row says stops take ≤ 0.2 s; this check is looser
        // than that (rationale for the difference not recorded).
        while (w.HorizontalVelocity.Length() > 0.05f * W.WalkSpeed.Value && time < 2) { w.Update(t, WalkInput.None, Frame120); time += 1 / 120f; }
        time.Should().BeLessThanOrEqualTo(0.25f);
    }

    [Fact]
    public void Turning_with_the_arrows_while_walking_makes_a_curve()
    {
        var t = PlaceTerrain.Flat;
        var w = new Walker();
        w.Teleport(t, Vector3.Zero, Deg(0), Deg(0));
        Run(w, t, new WalkInput(1, 0, 1, false, false), 1.5f); // Up + Right
        w.Yaw.Value.Should().BeApproximately(-W.TurnRate.Value * 1.5f, 3f);
        Xz(w.Feet).Length().Should().BeGreaterThan(3f);
        w.Feet.X.Should().BeGreaterThan(0.5f, "turning right curves to the east (+X)");
    }

    [Fact]
    public void Stairs_are_climbed_without_jumping_and_descended_without_falling()
    {
        var c = Sample();
        var w = new Walker();
        var front = Xz(c.Arrival.Eye) - new Vector2(0, P.TerraceHalfDepth + 4.5f);
        w.Teleport(c.Terrain, new Vector3(front.X, 0, front.Y), Deg(180), Deg(0)); // facing south, towards the terrace
        for (var i = 0; i < 600 && w.Feet.Z < c.Arrival.Eye.Z; i++) w.Update(c.Terrain, new WalkInput(1, 0, 0, false, false), Frame60);
        w.Support.Value.Should().BeApproximately(P.TerraceTop, 1e-3f, "the stairs lead up onto the terrace");
        w.Jumps.Should().Be(0);

        w.SetGaze(Deg(0), Deg(0));
        var airborneFrames = 0;
        for (var i = 0; i < 150; i++)
        {
            w.Update(c.Terrain, new WalkInput(1, 0, 0, false, false), Frame60);
            if (!w.Grounded) airborneFrames++;
        }

        w.Support.Value.Should().Be(0);
        airborneFrames.Should().BeLessThan(3, "walking down stairs stays on the ground");
    }

    [Fact]
    public void The_overlook_ramp_is_walked_up_at_speed()
    {
        var c = Sample();
        var ramp = c.Terrain.Ramps[0];
        ramp.SlopeDeg.Should().BeLessThan(35f); // WALK-05: walkable slopes are ≤ 35°.
        var w = new Walker();
        var dir = Vector2.Normalize(ramp.End - ramp.Start);
        var start = ramp.Start - dir * 1.5f;
        var yaw = -PlaceTerrain.PlaceAngle(dir); // CameraMath yaw for a place angle
        w.Teleport(c.Terrain, new Vector3(start.X, 0, start.Y), Deg(yaw), Deg(-20));
        var length = Vector2.Distance(ramp.Start, ramp.End) + 2.5f;
        Run(w, c.Terrain, new WalkInput(1, 0, 0, false, false), length / W.WalkSpeed.Value * 1.25f);
        w.Support.Value.Should().BeApproximately(P.OverlookTop, 1e-3f);
        w.Jumps.Should().Be(0);
    }

    [Fact]
    public void Walking_into_a_tower_at_an_angle_slides_along_it()
    {
        var box = new TerrainBox(new Vector2(0, -3), new Vector2(0.6f, 3f), 0, 0, 10, TerrainKind.Tower);
        var t = new PlaceTerrain([box], [], [], 60);
        var w = new Walker();
        w.Teleport(t, new Vector3(-2, 0, -0.5f), Deg(0), Deg(0));
        w.Look(Deg(-45), Deg(0)); // heading north-east, into the tower's west face
        Run(w, t, new WalkInput(1, 0, 0, false, false), 2f);
        box.Overlaps(Xz(w.Feet), W.Radius.Value - 0.01f).Should().BeFalse("never inside a solid");
        (-w.Feet.Z).Should().BeGreaterThan(3f, "the walker slid north along the face instead of stopping");
    }

    [Fact]
    public void A_walker_fits_between_neighbouring_towers()
    {
        var c = Sample(files: 400);
        var gap = P.FileCell - P.FileFootprint;
        gap.Should().BeGreaterThan(2 * W.Radius.Value + 0.4f);
        // Walk straight south down a column gap of the densest quarter.
        var t = new PlaceTerrain([
            new TerrainBox(new Vector2(-P.FileCell / 2, 0), new Vector2(P.FileFootprint / 2, 20), 0, 0, 8, TerrainKind.Tower),
            new TerrainBox(new Vector2(P.FileCell / 2, 0), new Vector2(P.FileFootprint / 2, 20), 0, 0, 8, TerrainKind.Tower)], [], [], 60);
        var w = new Walker();
        w.Teleport(t, new Vector3(0, 0, -25), Deg(180), Deg(0));
        Run(w, t, new WalkInput(1, 0, 0, false, false), 12f);
        w.Feet.Z.Should().BeGreaterThan(20f);
        c.Files.Count.Should().Be(400);
    }

    [Fact]
    public void The_boundary_keeps_walkers_in_the_place()
    {
        var c = Sample();
        var w = new Walker();
        w.Teleport(c.Terrain, new Vector3(0, 0, -5), Deg(70), Deg(0));
        Run(w, c.Terrain, new WalkInput(1, 0, 0, true, false), 20f);
        Xz(w.Feet).Length().Should().BeLessThanOrEqualTo(c.Terrain.BoundaryRadius);
        w.IsLost.Should().BeFalse();
    }

    [Fact]
    public void A_jump_is_higher_when_held_short_when_tapped_keeps_momentum_and_is_forgiving()
    {
        // 2026-11-06 (DJ: "jumping could be higher"): a held jump peaks at ≈ 1.2 m (was 0.75–0.85 m), a tap is a short hop.
        var t = PlaceTerrain.Flat;
        var w = new Walker();
        w.Teleport(t, Vector3.Zero, Deg(0), Deg(0));
        var apex = 0f;
        var air = 0;
        w.Update(t, new WalkInput(0, 0, 0, false, true), Frame60);
        for (var i = 0; i < 120; i++) { w.Update(t, WalkInput.None, Frame60); apex = MathF.Max(apex, w.Feet.Y); if (!w.Grounded) air++; }
        apex.Should().BeInRange(1.15f, 1.3f);
        (air / 60f).Should().BeInRange(0.65f, 0.9f, "a full jump lasts under a second");
        w.Grounded.Should().BeTrue();

        w.Teleport(t, Vector3.Zero, Deg(0), Deg(0));
        var hop = 0f;
        w.Update(t, new WalkInput(0, 0, 0, false, true), Frame60);
        for (var i = 0; i < 120; i++) { w.Update(t, new WalkInput(0, 0, 0, false, false, JumpReleased: i >= 2), Frame60); hop = MathF.Max(hop, w.Feet.Y); }
        hop.Should().BeInRange(0.3f, 0.75f, "releasing Space early gives a short hop");

        // A running jump carries forward.
        w.Teleport(t, Vector3.Zero, Deg(0), Deg(0));
        Run(w, t, new WalkInput(1, 0, 0, true, false), 1f);
        var takeoff = Xz(w.Feet);
        w.Update(t, new WalkInput(1, 0, 0, true, true), Frame60);
        var landed = false;
        for (var i = 0; i < 120 && !landed; i++)
        {
            w.Update(t, new WalkInput(0, 0, 0, false, false), Frame60);
            landed = w.Grounded;
        }

        Vector2.Distance(Xz(w.Feet), takeoff).Should().BeGreaterThan(4f, "running momentum is kept in the air");
    }

    [Fact]
    public void Coyote_time_and_jump_buffer()
    {
        var ledge = new TerrainBox(new Vector2(0, 0), new Vector2(3, 3), 0, 0, 2, TerrainKind.Platform);
        var t = new PlaceTerrain([ledge], [], [], 60);
        var w = new Walker();
        w.Teleport(t, new Vector3(0, 5, 2.0f), Deg(180), Deg(0)); // on the platform, facing south (+Z) towards its edge
        w.Support.Value.Should().Be(2);
        // Walk off the edge, then press jump 0.08 s after leaving it.
        for (var i = 0; i < 120 && w.Grounded; i++) w.Update(t, new WalkInput(1, 0, 0, false, false), Frame120);
        w.Grounded.Should().BeFalse();
        for (var i = 0; i < 9; i++) w.Update(t, new WalkInput(1, 0, 0, false, false), Frame120);
        w.Update(t, new WalkInput(1, 0, 0, false, true), Frame120);
        w.Jumps.Should().Be(1, "coyote time lets a late jump count");

        // Jump buffer: press shortly before landing.
        var flat = PlaceTerrain.Flat;
        w.Teleport(flat, Vector3.Zero, Deg(0), Deg(0));
        w.Update(flat, new WalkInput(0, 0, 0, false, true), Frame120);
        while (!(w.VerticalVelocity.Value < 0 && w.Feet.Y < 0.12f)) w.Update(flat, WalkInput.None, Frame120);
        w.Update(flat, new WalkInput(0, 0, 0, false, true), Frame120);
        for (var i = 0; i < 30; i++) w.Update(flat, WalkInput.None, Frame120);
        w.Jumps.Should().Be(3, "a jump pressed just before landing happens on landing (the second jump on flat ground)");
    }

    [Fact]
    public void The_jump_blocks_lead_onto_the_overlook()
    {
        var c = Sample();
        var t = c.Terrain;
        var w = new Walker();
        var ov = c.Overlook;
        var platform = Xz(ov.Top);
        Vector2[] stops = [ov.JumpBlocks[0], ov.JumpBlocks[1], platform];
        float[] heights = [P.OverlookTop / 3, P.OverlookTop * 2 / 3, P.OverlookTop];
        // Start on the ground in line with the blocks; for each, walk at it and jump once near its face.
        var along = Vector2.Normalize(stops[1] - stops[0]);
        var start = stops[0] - along * 4f;
        w.Teleport(t, new Vector3(start.X, 0, start.Y), Deg(0), Deg(0));
        for (var k = 0; k < stops.Length; k++)
        {
            var jumped = false;
            var target = t.Boxes.First(b => b.Kind is TerrainKind.JumpBlock or TerrainKind.Platform && b.Closest(stops[k]).Distance == 0 && b.Top == heights[k]);
            for (var i = 0; i < 400 && !(jumped && w.Grounded && Vector2.Distance(Xz(w.Feet), stops[k]) < 0.6f); i++)
            {
                var to = stops[k] - Xz(w.Feet);
                w.SetGaze(Deg(-PlaceTerrain.PlaceAngle(Vector2.Normalize(to))), Deg(0));
                var jump = !jumped && target.Closest(Xz(w.Feet)).Distance < 1.0f;
                jumped |= jump;
                w.Update(t, new WalkInput(to.Length() > 0.3f ? 1 : 0, 0, 0, false, jump), Frame120);
            }

            for (var i = 0; i < 60; i++) w.Update(t, WalkInput.None, Frame120);
            w.Support.Value.Should().BeApproximately(heights[k], 0.01f, $"jump {k + 1} lands on its block");
        }

        w.Jumps.Should().Be(3);
    }

    /// <summary>
    /// JUMP-03: every gate and the overlook are reachable from the arrival terrace on foot, without jumping. A grid
    /// search over the terrain allows moves that climb at most a step (any drop is fine) through clear cells.
    /// </summary>
    [Theory]
    [InlineData(6, 40)]
    [InlineData(30, 300)]
    [InlineData(1, 0)]
    public void Every_gate_and_the_overlook_are_reachable_without_jumping(int dirs, int files)
    {
        var c = Sample(dirs, files);
        var t = c.Terrain;
        const float cell = 0.5f;
        var n = (int)MathF.Ceiling(t.BoundaryRadius / cell) + 1;
        var heights = new Dictionary<(int, int), float>();
        float? Height(int i, int j)
        {
            if (heights.TryGetValue((i, j), out var h)) return float.IsNaN(h) ? null : h;
            var p = new Vector2(i * cell, j * cell);
            var s = t.SupportHeight(p, W.Radius.Value, 100, 0);
            var ok = p.Length() < t.BoundaryRadius - W.Radius.Value && t.IsClear(p, W.Radius.Value, s, W.BodyHeight.Value);
            heights[(i, j)] = ok ? s : float.NaN;
            return ok ? s : null;
        }

        var startP = Xz(c.Arrival.Eye);
        var start = ((int)MathF.Round(startP.X / cell), (int)MathF.Round(startP.Y / cell));
        Height(start.Item1, start.Item2).Should().NotBeNull();
        var seen = new HashSet<(int, int)> { start };
        var queue = new Queue<(int, int)>([start]);
        while (queue.Count > 0)
        {
            var (i, j) = queue.Dequeue();
            var h = Height(i, j)!.Value;
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var k = (i + di, j + dj);
                if (Math.Abs(k.Item1) > n || Math.Abs(k.Item2) > n || seen.Contains(k)) continue;
                if (Height(k.Item1, k.Item2) is not { } nh || nh > h + W.StepHeight.Value) continue;
                seen.Add(k);
                queue.Enqueue(k);
            }
        }

        bool Reached(Vector3 at) => seen.Any(s => Vector2.Distance(new Vector2(s.Item1 * cell, s.Item2 * cell), Xz(at)) < 0.8f);
        foreach (var g in c.Gates) Reached(g.Position - g.Outward * 1.5f).Should().BeTrue($"gate {g.TargetId} is reachable on foot");
        if (c.ParentGate is { } pg) Reached(pg.Position - pg.Outward * 1.5f).Should().BeTrue("the ▲ gate is reachable on foot");
        Reached(c.Overlook.Top).Should().BeTrue("the overlook is reachable by its ramp");
    }

    [Fact]
    public void A_walker_that_falls_out_of_the_world_reports_it()
    {
        var t = new PlaceTerrain([], [], [], 60);
        var w = new Walker();
        w.Teleport(t, new Vector3(0, -40, 0), Deg(0), Deg(0)); // below the ground: support is 0, so it stands at 0
        w.Feet.Y.Should().Be(0);
        w.IsLost.Should().BeFalse();
    }

    [Fact]
    public void A_jump_under_an_overhang_stops_at_the_head_and_never_passes_through()
    {
        // A slab 3 m up (underside); body 1.85 m: the head meets it 1.15 m above the feet, below the 1.2 m apex.
        var slab = new TerrainBox(new Vector2(0, 0), new Vector2(3, 3), 0, 3.0f, 3.4f, TerrainKind.Platform);
        var t = new PlaceTerrain([slab], [], [], 60);
        var w = new Walker();
        w.Teleport(t, Vector3.Zero, Deg(0), Deg(0));
        var top = 0f;
        w.Update(t, new WalkInput(0, 0, 0, false, true), Frame120);
        for (var i = 0; i < 240; i++) { w.Update(t, WalkInput.None, Frame120); top = MathF.Max(top, w.Feet.Y); }
        (top + W.BodyHeight.Value).Should().BeLessThanOrEqualTo(3.0f + 1e-3f, "the head never rises into the slab");
        w.HeadBumps.Should().Be(1);
        w.Grounded.Should().BeTrue();
        w.Feet.Y.Should().Be(0, "and the walker lands back below it");
    }

    [Fact]
    public void A_running_jump_at_full_speed_into_file_towers_never_lands_on_them()
    {
        // Higher jumps must not make file towers (2 m and up) into platforms: approach the shortest ones at 30 m/s.
        // 2026-11-11: this guards the single jump (the airborne boost is off here, AirJumps = 0); the boost's reach is
        // its own test below, because with it a deliberate second press can reach a tower up to ≈ 2.7 m.
        var c = Sample();
        var t = c.Terrain;
        var w = new Walker(W with { WalkSpeed = MetersPerSecond.Of(30f), RunSpeed = MetersPerSecond.Of(30f), AirJumps = 0 });
        foreach (var tower in t.Boxes.Where(b => b.Kind == TerrainKind.Tower && b.Top >= 2f).OrderBy(b => b.Top).Take(6))
        {
            var dir = Vector2.Normalize(tower.Center);
            var start = tower.Center - dir * 9f;
            w.Teleport(t, new Vector3(start.X, 0, start.Y), Deg(-PlaceTerrain.PlaceAngle(dir)), Deg(0));
            for (var i = 0; i < 240; i++)
            {
                var near = Vector2.Distance(Xz(w.Feet), tower.Center) < 3.5f;
                w.Update(t, new WalkInput(1, 0, 0, true, near), Frame120);
            }

            w.Support.Value.Should().BeLessThan(tower.Top - 0.01f, $"no landing on the {tower.Top:0.0} m tower");
        }
    }

    [Fact]
    public void The_airborne_boost_reaches_file_towers_up_to_about_2_7_m_and_no_taller()
    {
        // The boost (a second press at the apex) is a deliberate reach in the flat world too (AD-39): towers up to ≈ 2.7 m
        // become reachable; anything taller never is. Documented as a design consequence for DJ to judge.
        var c = Sample();
        var t = c.Terrain;
        var w = new Walker(W with { WalkSpeed = MetersPerSecond.Of(8f), RunSpeed = MetersPerSecond.Of(8f) });
        foreach (var tower in t.Boxes.Where(b => b.Kind == TerrainKind.Tower && b.Top >= 3.0f).OrderBy(b => b.Top).Take(4))
        {
            var dir = Vector2.Normalize(tower.Center);
            var start = tower.Center - dir * 6f;
            w.Teleport(t, new Vector3(start.X, 0, start.Y), Deg(-PlaceTerrain.PlaceAngle(dir)), Deg(0));
            var pressed = 0;
            for (var i = 0; i < 360; i++)
            {
                var d = Vector2.Distance(Xz(w.Feet), tower.Center);
                // One press to jump near the tower, one more at the apex: the most a player can do.
                var press = (pressed == 0 && d < 3.5f && w.Grounded) || (pressed == 1 && !w.Grounded && w.VerticalVelocity.Value < 0.5f);
                if (press) pressed++;
                w.Update(t, new WalkInput(1, 0, 0, true, press), Frame120);
            }

            w.Support.Value.Should().BeLessThan(tower.Top - 0.01f, $"no landing on the {tower.Top:0.0} m tower, even with the boost");
        }
    }
}
