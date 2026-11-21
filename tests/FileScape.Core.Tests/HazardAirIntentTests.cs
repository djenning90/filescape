using System.Numerics;
using FileScape.Core.Movement;
using FileScape.Core.Planets;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-181..184 (AD-97): a jump's intent over a Hazard World's open sky. DJ (2026-11-20): "During a long jump, the
/// character sometimes turns toward the ledge they left. A forward jump and boost should preserve forward intent. Steering
/// back toward an edge should allow recovery … with a bounded number of boosts and visible effort after recovery becomes
/// impossible." The desktop owner's suspect (report-desktop item 3): a second Space falling below the rim was the catch,
/// and with no ground within reach ahead the catch aimed back at the ledge left (within 3.2 m), carried the walker there and
/// turned the body to face it.
/// <para>
/// Every case steps the default <see cref="SphereWalker"/> (PlanetWorld's) at 60 fps with the real-input lag
/// (<see cref="InputLagFrames"/>, as HazardSampleReplayTests measured it) over a bridge with one gap: walking at it, Space
/// on the take-off board held 0.3 s (the probe's press), a second Space at a time after take-off held 0.2 s, with W held
/// on, nothing held, or steering back towards the rim left (S, or the camera turned round and W). Gap widths 3–5 m (where
/// the old catch reached back) and the widest leap.
/// </para>
/// What a change must preserve: forward intent (W held or nothing held after a forward jump) never ends back on the ledge
/// left and never turns the body to face it; steering back can catch the rim left; every press over open sky is answered.
/// </summary>
public sealed class HazardAirIntentTests(ITestOutputHelper output)
{
    /// <summary>A hazard-sized planet, metres (as HazardMovementTests).</summary>
    private const float R = 22f;

    /// <summary>The app's display frame at 60 fps, seconds.</summary>
    private const float FrameSeconds = 1f / 60;

    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(FrameSeconds);

    /// <summary>Frames between deciding a key and the app reading it (HazardSampleReplayTests.InputLagFrames: measured, 70b6ffc).</summary>
    private const int InputLagFrames = 2;

    /// <summary>The take-off press comes this far before the edge, metres (the probe's board press).</summary>
    private const float BoardMetres = 0.3f;

    /// <summary>The take-off press is held this long, seconds (the probe's 18 frames); the second press 0.2 s.</summary>
    private const float TakeOffHoldSeconds = 0.3f, SecondHoldSeconds = 0.2f;

    /// <summary>Steering back starts this long after take-off, seconds (a player who sees the jump falling short).</summary>
    private const float SteerBackAfterSeconds = 0.15f;

    /// <summary>The body counts as turned towards the ledge left when its facing is more than this from straight across, degrees.</summary>
    private const float TurnedBackDegrees = 90f;

    /// <summary>Two round lands joined by a 1.2 m-wide bridge with one gap of <paramref name="gap"/> metres in its middle (as HazardRecoveryTimingTests).</summary>
    private static (PlanetSurface Surface, ShellSpan Span, float G0, float G1) Gap(float gap)
    {
        var a = PlanetFrame.FromPolar(0.45f, 0);
        var b = PlanetFrame.FromPolar(0.45f, MathF.PI);
        var lands = new List<ShellLand> { new(a, 3f / R, 1, 0), new(b, 3f / R, 2, 0) };
        var len = PlanetShape.Angle(a, b) * R;
        var g0 = len / 2 - gap / 2;
        var g1 = len / 2 + gap / 2;
        var span = new ShellSpan(a, b, 1.2f, R, 3, [(g0, g1)], gap > 4 ? ShellGapClass.Advanced : ShellGapClass.Plain);
        var shell = new BrokenShell(lands, [a, b], [span], [new ShellLink(0, 1, 0, true)], Vector3.UnitX, R);
        return (new PlanetSurface(PlanetShape.Sphere(R), [], shell), span, g0, g1);
    }

    /// <summary>What the player steers after take-off.</summary>
    public enum Steer
    {
        /// <summary>W held on towards the far side.</summary>
        Forward,

        /// <summary>Nothing held after take-off.</summary>
        Nothing,

        /// <summary>Steering back towards the rim left, from <see cref="SteerBackAfterSeconds"/> after take-off.</summary>
        Back,
    }

    /// <summary>How a jump ended.</summary>
    public enum End { Across, BackOnLedge, Fell, Hanging }

    /// <summary>
    /// One replayed jump: the second press's event (null: the jump ended before the app read it), how it ended, the most the
    /// body turned from straight across (degrees) before landing or falling.
    /// </summary>
    private readonly record struct Jump(JumpEventKind? Second, End End, float MaxTurnDegrees, int Recoveries, int Efforts, bool SecondOverHole);

    /// <summary>
    /// Walks at a <paramref name="gap"/> m gap from 4 m before it, Space <see cref="BoardMetres"/> before the edge, a second
    /// Space <paramref name="secondAt"/> s after take-off (negative: none), steering as <paramref name="steer"/> says.
    /// </summary>
    private static Jump Replay(float gap, float secondAt, Steer steer)
    {
        var (surface, span, g0, g1) = Gap(gap);
        var w = new SphereWalker();
        var start = span.PointAlong(g0 - 4f);
        w.Teleport(surface, start, span.DirectionAt(start));
        var keys = new Queue<(bool Pressed, bool Held, Steer? Wish)>(Enumerable.Repeat((false, false, (Steer?)Steer.Forward), InputLagFrames));
        bool jumped = false, second = false;
        float takeOffAt = -1, holdUntil = -1, maxTurn = 0;
        JumpEventKind? secondKind = null;
        var secondOverHole = false;
        for (var i = 0; i < 60 * 5; i++)
        {
            var t = i * FrameSeconds;
            var (pressed, held, wishKind) = keys.Dequeue();
            var across = span.DirectionAt(w.Up);
            var wish = wishKind switch
            {
                Steer.Forward => across, // straight across, so the wish never turns as the walker passes a target point
                Steer.Back => -across,
                _ => Vector3.Zero,
            };
            var overHole = surface.Shell!.OverHole(w.Up);
            w.Update(new SphereWalkInput(wish, false, pressed, held), Frame);
            if (pressed && jumped && second && secondKind == null) (secondKind, secondOverHole) = (w.LastEvent.Kind, overHole);
            if (takeOffAt < 0 && !w.Grounded) takeOffAt = t;
            if (takeOffAt >= 0)
            {
                var facingAcross = Vector3.Dot(w.Facing, span.DirectionAt(w.Up));
                maxTurn = MathF.Max(maxTurn, MathF.Acos(Math.Clamp(facingAcross, -1, 1)) * 180 / MathF.PI);
            }

            if (w.FallCommitted) return new(secondKind, End.Fell, maxTurn, w.Recoveries, w.LastEfforts, secondOverHole);
            if (takeOffAt >= 0 && w.Grounded && t - takeOffAt > 0.05f)
            {
                var along = span.Measure(w.Up).AlongMetres;
                return new(secondKind, along > g1 ? End.Across : End.BackOnLedge, maxTurn, w.Recoveries, w.LastEfforts, secondOverHole);
            }

            // The player's frame: the keys read InputLagFrames later.
            var press = false;
            if (!jumped && w.Grounded && span.Measure(w.Up).AlongMetres >= g0 - BoardMetres)
            {
                press = jumped = true;
                holdUntil = t + TakeOffHoldSeconds;
            }
            else if (jumped && !second && secondAt >= 0 && takeOffAt >= 0 && t - takeOffAt >= secondAt - 1e-4f)
            {
                press = second = true;
                holdUntil = t + SecondHoldSeconds;
            }

            Steer? next = takeOffAt < 0 ? Steer.Forward
                : steer == Steer.Back && t - takeOffAt < SteerBackAfterSeconds ? Steer.Forward
                : steer;
            keys.Enqueue((press, t < holdUntil, next));
        }

        return new(secondKind, End.Hanging, maxTurn, w.Recoveries, w.LastEfforts, secondOverHole);
    }

    /// <summary>Gap widths, metres: 3–5 m (the old catch reached back across these) and the widest leap with its jagged ends.</summary>
    public static readonly float[] Gaps = [3f, 3.5f, 4f, 4.5f, 5f, HazardTerrain.LeapMaxMetres + ShellSpan.GapJagMetres];

    /// <summary>Second-press times after take-off, seconds: rising, the top, falling and below the rim.</summary>
    private static readonly float[] SecondTimes = Enumerable.Range(0, 17).Select(k => 0.2f + k * 0.05f).ToArray();

    public static IEnumerable<object[]> GapCases() => Gaps.Select(g => new object[] { g });

    [Theory]
    [MemberData(nameof(GapCases))]
    public void A_forward_jump_keeps_its_forward_intent(float gap)
    {
        var bad = new List<string>();
        foreach (var steer in new[] { Steer.Forward, Steer.Nothing })
        {
            var rows = SecondTimes.Select(at => (at, r: Replay(gap, at, steer))).ToList();
            output.WriteLine(F($"{gap:0.0} m, {steer}: ") + string.Join("; ", rows.Select(x => F($"{x.at:0.00} s {x.r.Second}→{x.r.End} turn {x.r.MaxTurnDegrees:0}°"))));
            foreach (var (at, r) in rows)
            {
                if (r.End == End.BackOnLedge) bad.Add(F($"{gap:0.0} m {steer}, second Space at {at:0.00} s: carried back onto the ledge left ({r.Second})"));
                if (r.MaxTurnDegrees > TurnedBackDegrees) bad.Add(F($"{gap:0.0} m {steer}, second Space at {at:0.00} s: turned {r.MaxTurnDegrees:0}° towards the ledge left ({r.Second})"));
                if (r.Second == JumpEventKind.None && r.SecondOverHole) bad.Add(F($"{gap:0.0} m {steer}, second Space at {at:0.00} s: no response"));
            }
        }

        bad.Should().BeEmpty("a forward jump and boost keep forward intent, and every press over open sky is answered");
    }

    [Theory]
    [MemberData(nameof(GapCases))]
    public void Steering_back_towards_the_rim_can_catch_it(float gap)
    {
        var rows = SecondTimes.Select(at => (at, r: Replay(gap, at, Steer.Back))).ToList();
        output.WriteLine(F($"{gap:0.0} m, steering back: ") + string.Join("; ", rows.Select(x => F($"{x.at:0.00} s {x.r.Second}→{x.r.End}"))));
        rows.Should().Contain(x => x.r.End == End.BackOnLedge && x.r.Second == JumpEventKind.Recovery,
            "a player who steers back towards the rim left and presses Space on the way down is caught back onto it");
        rows.Should().NotContain(x => x.r.Second == JumpEventKind.None && x.r.SecondOverHole, "every press over open sky is answered");
        rows.Should().NotContain(x => x.r.Second == JumpEventKind.Recovery && x.r.End == End.Across, "the catch goes where the player steers, not on across");
    }

    [Theory]
    [MemberData(nameof(GapCases))]
    public void At_most_one_boost_per_airtime_and_then_each_press_is_an_effort(float gap)
    {
        // Space every 0.1 s from 0.2 s after take-off until the jump ends, W held: the first press is the twirl or the
        // catch, every later one over open sky a visible effort (never silence), and no second lift.
        var (surface, span, g0, g1) = Gap(gap);
        var w = new SphereWalker();
        var start = span.PointAlong(g0 - 4f);
        w.Teleport(surface, start, span.DirectionAt(start));
        float takeOffAt = -1, nextPress = -1;
        var jumped = false;
        var answers = new List<(float At, JumpEventKind Kind, bool OverHole)>();
        for (var i = 0; i < 60 * 5 && !w.FallCommitted; i++)
        {
            var t = i * FrameSeconds;
            var press = false;
            if (!jumped && w.Grounded && span.Measure(w.Up).AlongMetres >= g0 - BoardMetres) press = jumped = true;
            else if (takeOffAt >= 0 && t - takeOffAt >= 0.2f && t >= nextPress) { press = true; nextPress = t + 0.1f; }
            var overHole = surface.Shell!.OverHole(w.Up);
            w.Update(new SphereWalkInput(span.DirectionAt(w.Up), false, press, press), Frame);
            if (takeOffAt < 0 && !w.Grounded) takeOffAt = t;
            if (press && takeOffAt >= 0 && t > takeOffAt) answers.Add((t - takeOffAt, w.LastEvent.Kind, overHole));
            if (takeOffAt >= 0 && t - takeOffAt > 0.05f && w.Grounded) break;
        }

        output.WriteLine(F($"{gap:0.0} m: ") + string.Join("; ", answers.Select(a => F($"{a.At:0.00} s {a.Kind}{(a.OverHole ? "" : " (over ground)")}"))));
        var boosts = answers.Count(a => a.Kind is JumpEventKind.AirJump or JumpEventKind.Recovery);
        boosts.Should().BeLessThanOrEqualTo(1, "one boost per airtime: the twirl or the catch");
        answers.Where(a => a.OverHole).Should().OnlyContain(a => a.Kind != JumpEventKind.None, "every press over open sky is answered: a boost, or an effort once none is left");
        answers.SkipWhile(a => a.Kind is not (JumpEventKind.AirJump or JumpEventKind.Recovery)).Skip(1).Where(a => a.OverHole)
            .Should().OnlyContain(a => a.Kind == JumpEventKind.LastEffort, "after the boost, each press is a last effort");
    }

    private static string F(FormattableString s) => FormattableString.Invariant(s);
}
