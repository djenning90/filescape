using System.Numerics;
using FileScape.Core.Movement;
using FileScape.Core.Places;
using FileScape.Core.Units;

namespace FileScape.Core.Tests;

/// <summary>The shared vertical model (JumpModel) on its own, and the flat walker's double jump.</summary>
public class JumpModelTests
{
    private const float StepSeconds = 1f / 120;

    /// <summary>A body on a ground of adjustable height, stepped the way the walkers step the model.</summary>
    private sealed class Column(JumpParams? p = null)
    {
        public JumpModel M { get; } = new(p);
        public float HeightMeters;
        public float GroundMeters;
        public float ApexMeters;

        public JumpEvent Step(bool press = false, bool held = true)
        {
            if (press) M.Press();
            var e = M.BeginStep(StepSeconds, HeightMeters - GroundMeters);
            if (M.Grounded)
            {
                if (GroundMeters >= HeightMeters - 0.5f && GroundMeters <= HeightMeters + 0.45f)
                {
                    HeightMeters = GroundMeters;
                    M.StayOnGround();
                }
                else
                {
                    M.LeaveGround();
                }
            }

            if (!M.Grounded)
            {
                HeightMeters += M.IntegrateRiseMeters(StepSeconds, held);
                if (HeightMeters <= GroundMeters && M.VerticalVelocity.Value <= 0) { HeightMeters = GroundMeters; e = M.Land(); }
            }

            ApexMeters = MathF.Max(ApexMeters, HeightMeters);
            return e;
        }

        public void Run(int steps, bool held = true) { for (var i = 0; i < steps; i++) Step(held: held); }

        public void RunUntilLanded(int maxSteps = 600) { for (var i = 0; i < maxSteps && !(M.Grounded && i > 0); i++) Step(); }
    }

    [Fact]
    public void A_held_jump_peaks_at_about_1_2_m_and_a_tap_is_a_short_hop()
    {
        var held = new Column();
        held.Step(press: true).Kind.Should().Be(JumpEventKind.Jump);
        held.RunUntilLanded();
        held.ApexMeters.Should().BeInRange(1.15f, 1.3f);
        held.M.Landings.Should().Be(1);

        var tap = new Column();
        tap.Step(press: true);
        for (var i = 0; i < 600 && !tap.M.Grounded; i++) tap.Step(held: i < 4);
        tap.ApexMeters.Should().BeInRange(0.3f, 0.75f, "releasing Space early gives a short hop");
    }

    [Fact]
    public void Jump_plus_boost_at_the_apex_peaks_between_2_5_and_3_m()
    {
        var c = new Column();
        c.Step(press: true);
        while (c.M.VerticalVelocity.Value > 0) c.Step();
        c.Step(press: true).Kind.Should().Be(JumpEventKind.AirJump);
        c.M.VerticalVelocity.Value.Should().BeApproximately(c.M.Params.AirJumpSpeed.Value - c.M.Params.Gravity.Value * StepSeconds, 0.05f, "the boost replaces the vertical speed");
        c.RunUntilLanded();
        c.ApexMeters.Should().BeInRange(2.5f, 3.0f);
        c.M.Jumps.Should().Be(1);
        c.M.AirJumps.Should().Be(1);
    }

    [Fact]
    public void The_boost_replaces_the_vertical_speed_so_its_timing_hardly_matters()
    {
        // Pressed early in the rise or late in the fall, the boost sets the same speed (replace, not add).
        foreach (var delaySteps in new[] { 6, 40, 70 })
        {
            var c = new Column();
            c.Step(press: true);
            c.Run(delaySteps);
            c.M.Grounded.Should().BeFalse();
            c.Step(press: true).Kind.Should().Be(JumpEventKind.AirJump, $"after {delaySteps} steps");
            c.M.VerticalVelocity.Value.Should().BeApproximately(7.1f, 0.2f);
        }
    }

    [Fact]
    public void Only_one_boost_per_airtime_and_it_is_re_armed_on_landing()
    {
        var c = new Column();
        c.Step(press: true);
        c.Run(20);
        c.Step(press: true).Kind.Should().Be(JumpEventKind.AirJump);
        for (var i = 0; i < 10; i++) { c.Run(8); c.Step(press: true).Kind.Should().NotBe(JumpEventKind.AirJump, "only one boost per airtime"); }
        c.M.AirJumps.Should().Be(1);
        c.M.AirJumpsLeft.Should().Be(0);
        c.RunUntilLanded();
        c.M.Grounded.Should().BeTrue();
        c.M.AirJumpsLeft.Should().Be(1, "landing re-arms the boost");
        c.Run(30);

        c.Step(press: true).Kind.Should().Be(JumpEventKind.Jump);
        c.Run(20);
        c.Step(press: true).Kind.Should().Be(JumpEventKind.AirJump);
        c.M.AirJumps.Should().Be(2);
        c.M.Jumps.Should().Be(2);
    }

    [Fact]
    public void Coyote_time_jumps_from_the_ground_and_after_it_the_press_is_the_boost()
    {
        // Within coyote time of walking off a 2 m ledge: a normal jump.
        var c = new Column { HeightMeters = 2, GroundMeters = 2 };
        c.Run(5);
        c.GroundMeters = 0; // walked off
        c.Run(9);           // 0.075 s
        c.M.Grounded.Should().BeFalse();
        c.Step(press: true).Kind.Should().Be(JumpEventKind.Jump, "coyote time lets a late jump count");
        c.M.AirJumps.Should().Be(0);

        // After coyote time (0.12 s): the press is the airborne boost.
        var d = new Column { HeightMeters = 2, GroundMeters = 2 };
        d.Run(5);
        d.GroundMeters = 0;
        d.Run(20);          // 0.17 s, still well above the ground
        d.Step(press: true).Kind.Should().Be(JumpEventKind.AirJump, "walking off a ledge leaves the boost available");
        d.M.Jumps.Should().Be(0);
        d.M.VerticalVelocity.Value.Should().BeGreaterThan(6.5f);
        d.RunUntilLanded();
        d.M.Landings.Should().Be(1);
    }

    [Fact]
    public void A_press_within_the_jump_buffer_of_a_fast_landing_is_buffered_not_the_boost()
    {
        // 2026-11-11 review: falling fast (a drop, a long fall), a fixed 0.35 m clearance left only the last ≈ 0.05 s for
        // the buffer, so a press 0.1 s before landing became the boost. It must stay a ground jump on landing.
        var c = new Column { HeightMeters = 3.0f };
        c.M.LeaveGround();
        // Fall until about 0.1 s from the ground at the current speed, then press.
        while (!(c.M.VerticalVelocity.Value < 0 && c.HeightMeters + c.M.VerticalVelocity.Value * 0.1f <= 0)) c.Step();
        c.M.VerticalVelocity.Value.Should().BeLessThan(-5f, "a fast landing");
        c.Step(press: true).Kind.Should().Be(JumpEventKind.None);
        var events = new List<JumpEventKind>();
        for (var i = 0; i < 40; i++) events.Add(c.Step().Kind);
        events.Should().ContainInOrder(JumpEventKind.Land, JumpEventKind.Jump);
        c.M.AirJumps.Should().Be(0, "the press was kept for the landing, not spent in the air");
    }

    [Fact]
    public void A_press_just_before_landing_is_buffered_not_spent_as_the_boost()
    {
        var c = new Column();
        c.Step(press: true);
        while (!(c.M.VerticalVelocity.Value < 0 && c.HeightMeters < 0.12f)) c.Step();
        c.Step(press: true).Kind.Should().Be(JumpEventKind.None, "a press this close to the ground waits for the landing");
        var events = new List<JumpEventKind>();
        for (var i = 0; i < 30; i++) events.Add(c.Step().Kind);
        events.Should().ContainInOrder(JumpEventKind.Land, JumpEventKind.Jump);
        c.M.Jumps.Should().Be(2, "the buffered press jumps on landing");
        c.M.AirJumps.Should().Be(0);
    }

    [Fact]
    public void Disabled_air_jumps_ignore_airborne_presses()
    {
        var c = new Column(new JumpParams { AirJumps = 0 });
        c.Step(press: true);
        c.Run(20);
        c.Step(press: true).Kind.Should().Be(JumpEventKind.None);
        c.RunUntilLanded();
        c.ApexMeters.Should().BeInRange(1.15f, 1.3f);
        c.M.AirJumps.Should().Be(0);
    }

    [Fact]
    public void Landing_reports_the_impact_speed()
    {
        var c = new Column();
        c.Step(press: true);
        JumpEvent land = default;
        for (var i = 0; i < 600 && land.Kind != JumpEventKind.Land; i++) land = c.Step();
        land.Kind.Should().Be(JumpEventKind.Land);
        land.ImpactSpeed.Value.Should().BeInRange(5f, 8f);
    }

    // --- The flat walker's double jump ---

    private static readonly TimeSpan Frame60 = Durations.FromSeconds(1 / 60.0);

    /// <summary>Runs at a 2.2 m platform from 2 m away (its edge at z = −2, facing −Z), jumps 1 m in and optionally boosts at the apex.</summary>
    private static Walker RunAtPlatform(bool boost, WalkParams? p = null)
    {
        var platform = new TerrainBox(new Vector2(0, -4), new Vector2(2, 2), 0, 0, 2.2f, TerrainKind.Platform);
        var t = new PlaceTerrain([platform], [], [], 60);
        var w = new Walker(p);
        w.Teleport(t, Vector3.Zero, Degrees.Zero, Degrees.Zero);
        bool jumped = false, boosted = false;
        for (var i = 0; i < 240; i++)
        {
            var press = false;
            if (!jumped && w.Feet.Z <= -1f) { press = true; jumped = true; }
            else if (boost && jumped && !boosted && !w.Grounded && w.VerticalVelocity.Value <= 0) { press = true; boosted = true; }
            var onTop = w.Grounded && w.Feet.Y > 2f; // stop once up there (the platform is 4 m deep)
            w.Update(t, new WalkInput(onTop ? 0 : 1, 0, 0, true, press), Frame60);
        }

        return w;
    }

    [Fact]
    public void A_2_2_m_platform_is_out_of_reach_of_a_single_jump_and_reached_with_the_boost()
    {
        var single = RunAtPlatform(boost: false);
        single.Jumps.Should().Be(1);
        single.Feet.Y.Should().Be(0, "a single jump (≈ 1.2 m + 0.3 m ledge tolerance) cannot reach 2.2 m");
        single.Feet.Z.Should().BeGreaterThan(-2f + single.Params.Radius.Value - 0.01f, "the platform side stops it");

        var boosted = RunAtPlatform(boost: true);
        boosted.AirJumps.Should().Be(1);
        boosted.Grounded.Should().BeTrue();
        boosted.Feet.Y.Should().BeApproximately(2.2f, 1e-4f, "jump + boost lands on top");

        var disabled = RunAtPlatform(boost: true, new WalkParams { AirJumps = 0 });
        disabled.AirJumps.Should().Be(0);
        disabled.Feet.Y.Should().Be(0, "with air jumps off the second press does nothing");
    }

    [Fact]
    public void The_flat_walker_reports_the_frame_events()
    {
        var t = PlaceTerrain.Flat;
        var w = new Walker();
        w.Teleport(t, Vector3.Zero, Degrees.Zero, Degrees.Zero);
        w.Update(t, new WalkInput(0, 0, 0, false, true), Frame60);
        w.LastEvent.Kind.Should().Be(JumpEventKind.Jump);
        w.Update(t, WalkInput.None, Frame60);
        w.LastEvent.Kind.Should().Be(JumpEventKind.None);
        for (var i = 0; i < 10; i++) w.Update(t, WalkInput.None, Frame60);
        w.Update(t, new WalkInput(1, 0, 0, false, true), Frame60);
        w.LastEvent.Kind.Should().Be(JumpEventKind.AirJump);
        var sawLand = false;
        for (var i = 0; i < 200 && !sawLand; i++) { w.Update(t, WalkInput.None, Frame60); sawLand = w.LastEvent.Kind == JumpEventKind.Land; }
        sawLand.Should().BeTrue();
        w.LastEvent.ImpactSpeed.Value.Should().BeGreaterThan(5f);
    }
}
