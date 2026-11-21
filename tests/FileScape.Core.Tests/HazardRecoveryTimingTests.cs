using System.Numerics;
using FileScape.Core.Movement;
using FileScape.Core.Planets;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-71..72, AD-69: the timing of a Hazard World's two moves, press by press. DJ (2026-11-19): "I couldn't get the
/// late rim catch to work. Once falling, the character seemed completely unresponsive. Pressing Space produced no visible
/// effort." Before AD-69 a press 0–0.12 s after slipping was a coyote jump further out, the catch was open ≈ 0.12–0.23 s
/// (walking) or one frame (running), and a press after that was nothing. These tests enumerate the press time after
/// leaving the rim in 10 ms steps, walking and running, W held on or let go, and say what each press does: the catch, a
/// coyote jump, a last effort, or — after the walker's fall has begun — the app's fall effort (PlanetWorld.Hazard). No
/// press before the fall may do nothing. Then the long jump's twirl window over a leap, and how soon an uncaught slip falls.
/// The printed tables are the source of the player-facing timing in docs/implementation-status.md.
/// </summary>
public class HazardRecoveryTimingTests(ITestOutputHelper output)
{
    /// <summary>A hazard-sized planet, metres (as HazardMovementTests).</summary>
    private const float R = 22f;

    /// <summary>The enumeration's frame and press step, seconds: 10 ms (the walker integrates at 120 Hz inside it).</summary>
    private const float StepSeconds = 0.01f;

    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(StepSeconds);

    /// <summary>How long Space is held for each press in the enumeration, seconds (a quick, deliberate tap).</summary>
    private const float TapSeconds = 0.12f;

    /// <summary>Two round lands joined by a 1.2 m-wide bridge with one gap of <paramref name="gap"/> metres in its middle (as HazardMovementTests).</summary>
    private static (PlanetSurface Surface, ShellSpan Span, float G0, float G1) Gap(float gap, float halfWidth = 1.2f)
    {
        var a = PlanetFrame.FromPolar(0.45f, 0);
        var b = PlanetFrame.FromPolar(0.45f, MathF.PI);
        var lands = new List<ShellLand> { new(a, 3f / R, 1, 0), new(b, 3f / R, 2, 0) };
        var len = PlanetShape.Angle(a, b) * R;
        var g0 = len / 2 - gap / 2;
        var g1 = len / 2 + gap / 2;
        var span = new ShellSpan(a, b, halfWidth, R, 3, [(g0, g1)], gap > 4 ? ShellGapClass.Advanced : ShellGapClass.Plain);
        var shell = new BrokenShell(lands, [a, b], [span], [new ShellLink(0, 1, 0, true)], Vector3.UnitX, R);
        return (new PlanetSurface(PlanetShape.Sphere(R), [], shell), span, g0, g1);
    }

    /// <summary>What one press did.</summary>
    private enum Response
    {
        /// <summary>Nothing at all: the dead press AD-69 removes.</summary>
        Nothing,

        /// <summary>The recovery, and the walker stood on the rim it slipped off.</summary>
        Catch,

        /// <summary>The recovery, but the walker fell anyway (a broken catch).</summary>
        CatchFailed,

        /// <summary>The coyote jump, and it landed across.</summary>
        JumpMade,

        /// <summary>The coyote jump, into the open sky.</summary>
        JumpFell,

        /// <summary>A last effort (a flail and a cry; the fall goes on).</summary>
        Effort,

        /// <summary>The walker's fall had begun before the press: the app answers it with its fall effort (PlanetWorld.Hazard).</summary>
        FallEffort,
    }

    /// <summary>
    /// Walks off the near rim of a <paramref name="gap"/> m gap along the bridge (no jump before it) and presses Space
    /// <paramref name="pressAfter"/> s after the feet leave the rim. <paramref name="holdOn"/>: W stays held after the
    /// slip (else let go). Also returns when the fall began, seconds after the slip (−1: it did not).
    /// </summary>
    private static (Response What, float FallAt) Slip(float gap, bool run, bool holdOn, float pressAfter, HazardMoveParams? hazard = null)
    {
        var (surface, span, g0, g1) = Gap(gap);
        var w = new SphereWalker(hazard == null ? null : SphereWalkParams.Default with { Hazard = hazard });
        var start = span.PointAlong(g0 - 3f);
        w.Teleport(surface, start, span.DirectionAt(start));
        var target = span.PointAlong(g1 + 3f);
        var leftAt = -1f;
        var pressed = false;
        var hold = 0f;
        var what = Response.Nothing;
        for (var i = 0; i < 500; i++)
        {
            var t = i * StepSeconds;
            var wish = leftAt < 0 || holdOn ? Vector3.Normalize(target - w.Up * Vector3.Dot(target, w.Up)) : Vector3.Zero;
            if (!w.Grounded && leftAt < 0) leftAt = t;
            var press = pressAfter >= 0 && !pressed && leftAt >= 0 && t - leftAt >= pressAfter - 1e-4f;
            if (press)
            {
                pressed = true;
                hold = TapSeconds;
            }

            w.Update(new SphereWalkInput(wish, run, press, hold > 0), Frame);
            hold -= StepSeconds;
            if (press || (pressed && what == Response.Nothing))
            {
                what = w.LastEvent.Kind switch
                {
                    JumpEventKind.Recovery => Response.Catch,
                    JumpEventKind.Jump => Response.JumpMade,
                    JumpEventKind.LastEffort => Response.Effort,
                    _ => what,
                };
            }

            if (w.FallCommitted)
            {
                var fallAt = t - leftAt;
                if (!pressed && pressAfter >= 0) return (Response.FallEffort, fallAt);
                return (what switch { Response.Catch => Response.CatchFailed, Response.JumpMade => Response.JumpFell, _ => what }, fallAt);
            }

            if (pressed && w.Grounded && t - leftAt > 0.05f)
            {
                var along = span.Measure(w.Up).AlongMetres;
                return (what == Response.Catch && along > g0 ? Response.CatchFailed : what, -1);
            }
        }

        return (what, -1);
    }

    /// <summary>"0.00–0.39 s catch; 0.40–0.80 s fall effort": consecutive press times with the same response, merged.</summary>
    private static string Ranges(IReadOnlyList<(float At, Response What)> rows)
    {
        var parts = new List<string>();
        for (var i = 0; i < rows.Count;)
        {
            var j = i;
            while (j + 1 < rows.Count && rows[j + 1].What == rows[i].What) j++;
            parts.Add(F($"{rows[i].At:0.00}–{rows[j].At:0.00} s {rows[i].What}"));
            i = j + 1;
        }

        return string.Join("; ", parts);
    }

    /// <summary>Press times 0 to 0.8 s after the slip, 10 ms apart.</summary>
    private static readonly float[] PressTimes = Enumerable.Range(0, 81).Select(k => k * StepSeconds).ToArray();

    [Trait(TestKinds.Trait, TestKinds.Slow)]
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Every_press_after_a_slip_gets_a_response_and_an_early_one_is_the_catch(bool run, bool holdOn)
    {
        const float gap = 9f; // nothing across in reach: the only ground to catch is the rim slipped off
        var noPress = Slip(gap, run, holdOn, -1).FallAt;
        var rows = PressTimes.Select(p => (p, Slip(gap, run, holdOn, p).What)).ToList();
        output.WriteLine(F($"{(run ? "running" : "walking")}, W {(holdOn ? "held on" : "let go")}: an uncaught slip falls {noPress:0.00} s after it; {Ranges(rows)}"));

        noPress.Should().BeGreaterThan(0, "a slip that is not caught falls");
        rows.Should().NotContain(r => r.What == Response.Nothing, "no press before the return is dead");
        rows.Should().NotContain(r => r.What == Response.CatchFailed, "a catch puts the walker back on the rim, W held or not");
        rows.Should().NotContain(r => r.What == Response.JumpFell, "a coyote jump into the open sky is the catch instead");
        var catchUntil = run ? 0.25f : 0.33f; // seconds after the slip: walking the window (0.35 s, AD-75), running the reach closes it
        rows.Where(r => r.p <= catchUntil).Should().OnlyContain(r => r.What == Response.Catch, F($"Space within {catchUntil:0.00} s of slipping catches the rim"));
        rows.Where(r => r.p >= noPress + StepSeconds).Should().OnlyContain(r => r.What == Response.FallEffort, "after that the fall's own effort answers");
        noPress.Should().BeLessThan(run ? 0.35f : 0.45f, "an uncaught slip falls promptly");
    }

    /// <summary>
    /// The catch's window with <paramref name="hazard"/>: the last press time (10 ms steps) that is still a catch, walking
    /// and running with W held and let go, and when an uncaught slip falls (seconds after the slip; the 9 m gap as above).
    /// </summary>
    private static string CatchWindows(HazardMoveParams hazard)
    {
        var parts = new List<string>();
        foreach (var run in new[] { false, true })
        foreach (var holdOn in new[] { true, false })
        {
            var last = PressTimes.Where(p => Slip(9f, run, holdOn, p, hazard).What == Response.Catch).DefaultIfEmpty(-1).Max();
            var fall = Slip(9f, run, holdOn, -1, hazard).FallAt;
            parts.Add(F($"{(run ? "running" : "walking")} W {(holdOn ? "held" : "let go")}: catch to {last:0.00} s, falls at {fall:0.00} s"));
        }

        return string.Join("; ", parts);
    }

    [Trait(TestKinds.Trait, TestKinds.Report)]
    [Fact]
    public void The_tightening_candidates_measured_one_at_a_time()
    {
        // Stream 2 (2026-11-19, AD-75): DJ — "make the recovery challenge only slightly tighter. Change one parameter at a
        // time, compare it with the current build". The same scripted slips (this class's enumeration) with the current
        // values and with each candidate changed alone. The chosen one is HazardMoveParams.Default (AD-75 records why).
        var current = new HazardMoveParams { Window = TimeSpan.FromSeconds(0.4), ReturnReach = Units.Meters.Of(3.2f) };
        output.WriteLine("current (cfee25d): " + CatchWindows(current));
        foreach (var w in new[] { 0.35, 0.3 }) output.WriteLine(F($"Window {w:0.00} s alone: ") + CatchWindows(current with { Window = TimeSpan.FromSeconds(w) }));
        foreach (var r in new[] { 2.9f, 2.6f }) output.WriteLine(F($"ReturnReach {r:0.0} m alone: ") + CatchWindows(current with { ReturnReach = Units.Meters.Of(r) }));
        output.WriteLine("default now: " + CatchWindows(HazardMoveParams.Default));
    }

    [Fact]
    public void A_late_jump_at_a_step_is_still_the_coyote_jump()
    {
        // A step gap (the widest, with its jagged ends): walking off it and pressing in the coyote time still jumps across.
        var gap = HazardTerrain.MaxJumpGapMetres + 2 * ShellSpan.GapJagMetres;
        foreach (var run in new[] { false, true })
        {
            var rows = Enumerable.Range(0, 11).Select(k => (k * StepSeconds, Slip(gap, run, true, k * StepSeconds).What)).ToList();
            output.WriteLine(F($"a {gap:0.0} m step, {(run ? "running" : "walking")}, W held: {Ranges(rows)}"));
            rows.Should().OnlyContain(r => r.What == Response.JumpMade, "a late press at a step it clears is the jump across");
        }
    }

    [Fact]
    public void Each_press_on_the_way_down_after_a_jump_falls_short_is_a_catch_or_an_effort()
    {
        // A plain held jump at a 9 m gap falls short with no ground in reach (≈ 5 m from the take-off, beyond ReturnReach):
        // Space every 0.03 s on the way down until the fall begins, each answered. (Before AD-97 a tapped short hop came down
        // within ReturnReach of the take-off and was caught back onto it with W still held forward; now the rim left is caught
        // back only by steering back towards it: HazardAirIntentTests.)
        var (surface, span, g0, g1) = Gap(9f);
        var w = new SphereWalker();
        var start = span.PointAlong(g0 - 4f);
        w.Teleport(surface, start, span.DirectionAt(start));
        var target = span.PointAlong(g1 + 3f);
        var jumped = false;
        var presses = 0;
        var answered = 0;
        var nextPress = -1f;
        for (var i = 0; i < 400 && !w.FallCommitted; i++)
        {
            var t = i * StepSeconds;
            var wish = Vector3.Normalize(target - w.Up * Vector3.Dot(target, w.Up));
            var press = false;
            if (!jumped && span.Measure(w.Up).AlongMetres >= g0 - 0.3f) press = jumped = true;
            else if (jumped && !w.Grounded && w.VerticalVelocity.Value < 0 && w.Height.Value < 0 && t >= nextPress)
            {
                press = true;
                presses++;
                nextPress = t + 0.03f;
            }

            w.Update(new SphereWalkInput(wish, false, press, press || (jumped && presses == 0)), Frame); // the jump held: a full one, well out
            if (press && presses > 0 && w.LastEvent.Kind is JumpEventKind.LastEffort or JumpEventKind.Recovery) answered++;
        }

        output.WriteLine($"a jump short of a 9 m gap: {presses} presses on the way down, {answered} answered, {w.LastEfforts} last efforts, {w.Recoveries} recoveries");
        presses.Should().BeGreaterThanOrEqualTo(2);
        answered.Should().Be(presses, "every press on the way down is answered");
        w.LastEfforts.Should().Be(presses, "far from any ground, each is a last effort");
        w.FallCommitted.Should().BeTrue();
    }

    [Fact]
    public void A_slip_off_the_side_of_a_narrow_bridge_is_caught_onto_it()
    {
        // Stepping sideways off a 1.2 m bridge (half-width 0.6 m) at walking pace: the catch lands back on it and does not
        // slide off the far side.
        var (surface, span, g0, _) = Gap(9f, halfWidth: 0.6f);
        foreach (var pressAfter in new[] { 0f, 0.1f, 0.2f, 0.3f })
        {
            var w = new SphereWalker();
            var start = span.PointAlong(g0 - 4f);
            var along = span.DirectionAt(start);
            var side = Vector3.Normalize(Vector3.Cross(start, along));
            w.Teleport(surface, start, side);
            var leftAt = -1f;
            var pressed = false;
            var back = false;
            for (var i = 0; i < 300 && !w.FallCommitted; i++)
            {
                var t = i * StepSeconds;
                var wish = Vector3.Normalize(side - w.Up * Vector3.Dot(side, w.Up));
                if (!w.Grounded && leftAt < 0) leftAt = t;
                var press = !pressed && leftAt >= 0 && t - leftAt >= pressAfter - 1e-4f;
                pressed |= press;
                w.Update(new SphereWalkInput(leftAt < 0 ? wish : Vector3.Zero, false, press, press), Frame);
                if (pressed && w.Grounded)
                {
                    // Stand still for half a second: the landing must hold.
                    for (var k = 0; k < 50; k++) w.Update(SphereWalkInput.None, Frame);
                    back = w.Grounded && !surface.Shell!.OverHole(w.Up);
                    break;
                }
            }

            back.Should().BeTrue(F($"Space {pressAfter:0.0} s after stepping off the side of a 1.2 m bridge: back on it"));
        }
    }

    /// <summary>The long jump's twirl window must be at least this wide, seconds, from any board position and however long Space is held.</summary>
    private const float LongJumpWindowSeconds = 0.15f;

    [Trait(TestKinds.Trait, TestKinds.Slow)]
    [Fact]
    public void The_long_jump_twirl_window_over_a_leap()
    {
        // The leap at its widest (with its jagged ends), walking: Space on the take-off board (0.2–0.6 m before the edge,
        // held), and the twirl 0.10–0.80 s after take-off, 10 ms apart. The twirl's window must be wide enough to find
        // and repeat, and around the top of the jump; a plain jump never makes it.
        var gap = HazardTerrain.LeapMaxMetres + ShellSpan.GapJagMetres;
        var windows = new List<(float Hold, float Board, float Seconds, bool PlainMade)>();
        foreach (var hold in new[] { 0.12f, 0.3f, 2f })
        foreach (var board in new[] { 0.2f, 0.4f, 0.6f })
        {
            var made = new List<float>();
            var rows = new List<(float, Response)>();
            for (var k = 10; k <= 80; k++)
            {
                var at = k * StepSeconds;
                var ok = LongJump(gap, board, at, hold);
                if (ok) made.Add(at);
                rows.Add((at, ok ? Response.JumpMade : Response.JumpFell));
            }

            output.WriteLine(F($"leap {gap:0.00} m, Space {board:0.0} m before the edge, each press held {hold:0.00} s: twirl at {Ranges(rows)}"));
            windows.Add((hold, board, made.Count == 0 ? 0 : made.Max() - made.Min() + StepSeconds, LongJump(gap, board, -1, hold)));
        }

        windows.Should().OnlyContain(w => !w.PlainMade, "a plain jump never makes a leap");
        windows.Should().OnlyContain(w => w.Seconds >= LongJumpWindowSeconds, F($"the twirl's window is at least {LongJumpWindowSeconds} s wide, tapped or held"));
    }

    [Fact]
    public void The_in_app_probe_s_long_jumps_are_inside_the_window()
    {
        // PlanetsProbe.LeapCrossing's inputs: Space 0.35 m before the edge, each press held 0.3 s; the twirl late in the
        // window (0.45 s after take-off) makes it, and no twirl does not.
        var gap = HazardTerrain.LeapMaxMetres + ShellSpan.GapJagMetres;
        LongJump(gap, 0.35f, 0.45f, holdSeconds: 0.3f).Should().BeTrue("the late twirl crosses the widest leap");
        LongJump(gap, 0.35f, 0.33f, holdSeconds: 0.3f).Should().BeTrue("and one round the top");
        LongJump(HazardTerrain.LeapMinMetres, 0.35f, -1, holdSeconds: 0.3f).Should().BeFalse("the plain jump falls short of the narrowest");
    }

    /// <summary>Walks at the gap, Space <paramref name="board"/> m before the edge (held <paramref name="holdSeconds"/>, each press), the twirl <paramref name="twirlAt"/> s after take-off (negative: none). True when across.</summary>
    private static bool LongJump(float gap, float board, float twirlAt, float holdSeconds = 2f)
    {
        var (surface, span, g0, g1) = Gap(gap);
        var w = new SphereWalker();
        var start = span.PointAlong(g0 - 6f);
        w.Teleport(surface, start, span.DirectionAt(start));
        var target = span.PointAlong(g1 + 3f);
        bool jumped = false, twirled = false;
        var leftAt = -1f;
        var holdUntil = -1f;
        for (var i = 0; i < 500; i++)
        {
            var t = i * StepSeconds;
            var wish = Vector3.Normalize(target - w.Up * Vector3.Dot(target, w.Up));
            var press = false;
            if (!jumped && span.Measure(w.Up).AlongMetres >= g0 - board) press = jumped = true;
            if (!w.Grounded && leftAt < 0) leftAt = t;
            if (twirlAt >= 0 && leftAt >= 0 && !twirled && t - leftAt >= twirlAt - 1e-4f) press = twirled = true;
            if (press) holdUntil = t + holdSeconds;
            w.Update(new SphereWalkInput(wish, false, press, t < holdUntil), Frame);
            if (w.FallCommitted) return false;
            if (leftAt >= 0 && w.Grounded) return span.Measure(w.Up).AlongMetres > g1;
        }

        return false;
    }

    /// <summary>A display frame at 60 fps, seconds: the real-input replays step the walker as the app does.</summary>
    private const float AppFrameSeconds = 1f / 60;

    /// <summary>What the first and second press of a replayed long jump did, and how it ended.</summary>
    private readonly record struct LongJumpReplay(JumpEventKind First, float FirstAt, JumpEventKind Second, bool Across, bool Fell);

    /// <summary>
    /// The real-input probe's long jump (tools/os-planets-probe.ps1 -Recovery) replayed at 60 fps: stood
    /// <paramref name="standMetres"/> before the edge, W held from rest throughout, the first Space
    /// <paramref name="firstAtMetres"/> before the edge (negative: past it, already over the gap) held
    /// <paramref name="firstHoldSeconds"/>, the second <paramref name="secondAt"/> s after the feet left the ground, held
    /// 0.2 s. What each press did (the walker's event in the press's frame) and whether it crossed or fell.
    /// </summary>
    private static LongJumpReplay ReplayLongJump(float gap, float standMetres, float firstAtMetres, float firstHoldSeconds, float secondAt)
    {
        var (surface, span, g0, g1) = Gap(gap);
        var w = new SphereWalker();
        var start = span.PointAlong(g0 - standMetres);
        w.Teleport(surface, start, span.DirectionAt(start));
        var target = span.PointAlong(g1 + 3f);
        var frame = TimeSpan.FromSeconds(AppFrameSeconds);
        var first = JumpEventKind.None;
        var second = JumpEventKind.None;
        float leftAt = -1, firstAt = -1, holdUntil = -1;
        bool pressed1 = false, pressed2 = false;
        for (var i = 0; i < 600; i++)
        {
            var t = i * AppFrameSeconds;
            var wish = Vector3.Normalize(target - w.Up * Vector3.Dot(target, w.Up));
            var along = span.Measure(w.Up).AlongMetres;
            var press = false;
            if (!pressed1 && along >= g0 - firstAtMetres) { press = pressed1 = true; holdUntil = t + firstHoldSeconds; firstAt = leftAt < 0 ? 0 : t - leftAt; }
            else if (pressed1 && !pressed2 && leftAt >= 0 && t - leftAt >= secondAt - 1e-4f) { press = pressed2 = true; holdUntil = t + 0.2f; }
            w.Update(new SphereWalkInput(wish, false, press, t < holdUntil), frame);
            if (!w.Grounded && leftAt < 0) leftAt = t;
            if (press && !pressed2) first = w.LastEvent.Kind;
            else if (press) second = w.LastEvent.Kind;
            if (w.FallCommitted) return new(first, firstAt, second, false, true);
            if (pressed2 && w.Grounded && t - leftAt > 0.1f && span.Measure(w.Up).AlongMetres > g1) return new(first, firstAt, second, true, false);
        }

        return new(first, firstAt, second, false, false);
    }

    [Fact]
    public void The_real_input_long_jump_replayed_press_by_press()
    {
        // Stream 2 (2026-11-19): with real input on cfee25d the long jump fell twice, its second press recorded as
        // "buffered" 0.367 s after take-off. The probe timed the first press from the state file, written every 0.15 s
        // (≈ 0.9 m apart at walking pace), so it could land anywhere from the board to past the edge. Replayed here at
        // 60 fps from the probe's 3 m stand point: where the first press lands decides what both presses do.
        // Before PLANET-85 (AD-75) every first press just past the edge (0.02–0.12 s after leaving it) was the catch: it
        // carried the walker back, the second press found the twirl spent and did nothing, and W still held walked it off
        // again. Now, off the board heading across, a press in the coyote time is the take-off.
        var coyote = (float)JumpParams.Default.CoyoteTime.TotalSeconds;
        foreach (var gap in new[] { HazardTerrain.LeapMinMetres, HazardTerrain.LeapMaxMetres })
        foreach (var at in new[] { 0.55f, 0.4f, 0.2f, 0.05f, -0.1f, -0.3f, -0.5f, -0.7f })
        {
            var r = ReplayLongJump(gap, 3f, at, 0.13f, 0.367f);
            output.WriteLine(F($"leap {gap:0.0} m, first press {at:+0.00;-0.00} m before the edge ({r.FirstAt:0.000} s after leaving it): {r.First}; second at 0.367 s: {r.Second}; {(r.Across ? "across" : r.Fell ? "fell" : "short, no fall")}"));
            if (r.FirstAt <= coyote) r.Across.Should().BeTrue(F($"a take-off press {r.FirstAt:0.000} s after the board (within the coyote time) and the twirl at 0.367 s cross a {gap:0.0} m leap"));
            else r.First.Should().Be(JumpEventKind.Recovery, "after the coyote time the press is the catch, as before");
        }
    }

    [Fact]
    public void A_slip_off_the_side_of_a_leap_board_is_still_the_catch()
    {
        // PLANET-85's take-off counts only heading across the gap: stepping off the board's side and pressing at once
        // (0–0.1 s) is the catch back onto it, as AD-69 has it.
        var (surface, span, g0, _) = Gap(HazardTerrain.LeapMaxMetres);
        foreach (var pressAfter in new[] { 0f, 0.05f, 0.1f })
        {
            var w = new SphereWalker();
            var start = span.PointAlong(g0 - 0.5f);
            var along = span.DirectionAt(start);
            var side = Vector3.Normalize(Vector3.Cross(start, along));
            w.Teleport(surface, start, side);
            var leftAt = -1f;
            var what = JumpEventKind.None;
            for (var i = 0; i < 300 && what == JumpEventKind.None && !w.FallCommitted; i++)
            {
                var t = i * StepSeconds;
                var wish = Vector3.Normalize(side - w.Up * Vector3.Dot(side, w.Up));
                if (!w.Grounded && leftAt < 0) leftAt = t;
                var press = leftAt >= 0 && t - leftAt >= pressAfter - 1e-4f;
                w.Update(new SphereWalkInput(leftAt < 0 ? wish : Vector3.Zero, false, press, press), Frame);
                if (press) what = w.LastEvent.Kind;
            }

            what.Should().Be(JumpEventKind.Recovery, F($"Space {pressAfter:0.00} s after stepping off the side of a leap's board is the catch"));
        }
    }

    private static string F(FormattableString s) => FormattableString.Invariant(s);
}
