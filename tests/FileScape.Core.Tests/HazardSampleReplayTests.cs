using System.Numerics;
using FileScape.Core.Movement;
using FileScape.Core.Planets;
using FileScape.Core.Scanning;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>
/// TEST-09 (AD-88), the first journey assertions moved into Core: the planets-hazard journey's timed presses replayed on the
/// Starter Sky's own Hazard Worlds. The sample is written and scanned as the app does (SampleSky, DirectoryScanner), Event
/// Horizon and Debris Field are composed by PlanetComposer (the composition the app walks on), and the default
/// <see cref="SphereWalker"/> (PlanetWorld's) is stepped at 60 fps with the probe's inputs: W held towards the far side,
/// Space pressed and held for the probe's frame counts, each press read on the frame after the probe decides it.
/// <para>
/// What moved: <c>PlanetsProbe.RecoverySlips</c>' mid-window and late catches and <c>LeapCrossing</c>'s late twirl and late
/// take-off. The journey keeps one of each through the running app (the early catch; the apex twirl and the plain jump
/// that falls short, whose fall returns to Event Horizon), so the boundary between keys, PlanetWorld and the walker is
/// still exercised there. Equivalence was shown by one injected regression caught by both (AD-88).
/// </para>
/// What a change must preserve: the probe's numbers (stand points, press times, hold frames) stay those of
/// <c>app/Scripts/PlanetsProbe.Hazard.cs</c>; a change there is made here too.
/// </summary>
public sealed class HazardSampleReplayTests(SampleSkyFixture sky, ITestOutputHelper output) : IClassFixture<SampleSkyFixture>
{
    /// <summary>The app's display frame at 60 fps, seconds (what the journey runs at).</summary>
    private const float FrameSeconds = 1f / 60;

    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(FrameSeconds);

    /// <summary>The probe's take-off press comes this far before the leap's edge, metres (LeapCrossing: <c>along &gt;= g0 - 0.35f</c>).</summary>
    private const float BoardMetres = 0.35f;

    /// <summary>Frames the probe holds each Space press in LeapCrossing (18) and the late take-off press (8).</summary>
    private const int HoldFrames = 18, LateTakeOffHoldFrames = 8;

    /// <summary>The probe decides a timed press half a frame early, seconds (the press is read on the next frame).</summary>
    private const float HalfFrameSeconds = 0.008f;

    /// <summary>
    /// Frames between the probe seeing the walker's state and the app reading the key it injects: two, measured from the
    /// journey's press log on 70b6ffc (a twirl decided at 0.45 s logged at 0.483 s, the late take-off at 0.05 s logged at
    /// 0.083 s, the board press 0.35 m before the edge logged 0.21 m before it).
    /// </summary>
    private const int InputLagFrames = 2;

    /// <summary>The probe's keys per frame (Space pressed this frame, Space held, W held), delivered <see cref="InputLagFrames"/> late.</summary>
    private sealed class LaggedKeys
    {
        private readonly Queue<(bool Pressed, bool Held, bool Walk)> _q = new(Enumerable.Repeat((false, false, true), InputLagFrames));

        public (bool Pressed, bool Held, bool Walk) Next() => _q.Dequeue();

        public void Send(bool pressed, bool held, bool walk = true) => _q.Enqueue((pressed, held, walk));
    }

    private PlanetComposition Compose(string name)
    {
        var (tree, _) = ScanRunner.Run(new DirectoryScanner(sky.Root));
        return PlanetComposer.Compose(tree, tree.Nodes.First(n => n.Name == name).Id);
    }

    /// <summary>The probe's leap modes (PlanetsProbe.LeapTwirl): the twirl at the top, late (0.45 s), none; the take-off press late (0.05 s, the twirl at 0.37 s).</summary>
    public enum Leap { Apex, Late, Plain, LateTakeOff }

    /// <summary>What a replayed leap did: across, a fall, the take-off press's and the twirl's walker events, and the apex twirls.</summary>
    private readonly record struct LeapResult(bool Across, bool Fell, JumpEventKind TakeOff, JumpEventKind Twirl, int ApexTwirls, float TwirlAt);

    /// <summary>
    /// <c>PlanetsProbe.LeapCrossing</c> on Debris Field's leap: stood 3 m before the gap on its bridge facing across, W held,
    /// Space on the board (or <c>0.05 s</c> after leaving it) and the twirl as <paramref name="mode"/> says.
    /// </summary>
    private static LeapResult ReplayLeap(PlanetComposition comp, Leap mode)
    {
        var shell = comp.Surface.Shell!;
        var link = shell.Links.First(l => l.Advanced);
        var s = shell.Spans[link.Span];
        var (g0, g1) = s.Gaps[0];
        var w = new SphereWalker();
        var start = s.PointAlong(MathF.Max(0.5f, g0 - 3f));
        w.Teleport(comp.Surface, start, s.DirectionAt(start));
        var far = s.PointAlong(g1 + 3f);
        bool jumped = false, twirled = false;
        int holdFrames = 0, apex0 = w.ApexTwirls, presses = 0;
        float air = 0, twirlAt = -1;
        JumpEventKind takeOff = JumpEventKind.None, twirl = JumpEventKind.None;
        var keys = new LaggedKeys();
        for (var frame = 0; frame < 360; frame++)
        {
            // The app's frame: the key read (pressed this frame, held), the walker stepped, the air time counted.
            var (pressed, held, _) = keys.Next();
            var wish = Vector3.Normalize(far - w.Up * Vector3.Dot(far, w.Up));
            w.Update(new SphereWalkInput(wish, false, pressed, held), Frame);
            if (pressed && presses++ == 0) takeOff = w.LastEvent.Kind;
            else if (pressed) twirl = w.LastEvent.Kind;
            air = w.Grounded ? 0 : air + FrameSeconds;
            if (w.FallCommitted) return new(false, true, takeOff, twirl, w.ApexTwirls - apex0, twirlAt);
            var along = s.Measure(w.Up).AlongMetres;
            if (jumped && w.Grounded && along > g1) return new(true, false, takeOff, twirl, w.ApexTwirls - apex0, twirlAt);

            // The probe's frame: what it presses next (it sees the walker now; the app reads the key later).
            var airborne = jumped && !w.Grounded;
            var twirlNow = mode switch
            {
                Leap.Apex => holdFrames == 0 && MathF.Abs(w.VerticalVelocity.Value) < 1.5f,
                Leap.Late => air >= 0.45f - HalfFrameSeconds,
                Leap.LateTakeOff => air >= 0.37f - HalfFrameSeconds,
                _ => false,
            };
            var takeOffNow = mode == Leap.LateTakeOff ? !w.Grounded && air >= 0.05f - HalfFrameSeconds : w.Grounded && along >= g0 - BoardMetres;
            var press = false;
            if (!jumped && takeOffNow)
            {
                jumped = press = true;
                holdFrames = mode == Leap.LateTakeOff ? LateTakeOffHoldFrames : HoldFrames;
            }
            else if (airborne && !twirled && twirlNow)
            {
                twirled = press = true;
                twirlAt = air;
                holdFrames = HoldFrames;
            }

            if (holdFrames > 0) holdFrames--; // as the probe: decremented in the press's own frame too

            keys.Send(press, holdFrames > 0);
        }

        return new(false, false, takeOff, twirl, w.ApexTwirls - apex0, twirlAt);
    }

    [Trait(TestKinds.Trait, TestKinds.Slow)]
    [Theory]
    [InlineData(Leap.Apex)]
    [InlineData(Leap.Late)]
    [InlineData(Leap.LateTakeOff)]
    [InlineData(Leap.Plain)]
    public void The_hazard_journey_s_long_jumps_on_debris_field(Leap mode)
    {
        var comp = Compose("Debris Field");
        var s = comp.Surface.Shell!.Spans[comp.Surface.Shell.Links.First(l => l.Advanced).Span];
        var r = ReplayLeap(comp, mode);
        output.WriteLine(FormattableString.Invariant($"leap {s.Gaps[0].ToMetres - s.Gaps[0].FromMetres:0.00} m on a bridge of {s.LengthMetres:0.0} m, {mode}: take-off {r.TakeOff}, twirl {r.Twirl} at {r.TwirlAt:0.00} s, {r.ApexTwirls} apex twirl(s), {(r.Across ? "across" : r.Fell ? "fell" : "short, no fall")}"));
        switch (mode)
        {
            case Leap.Apex:
                r.Across.Should().BeTrue("the twirl at the top of the jump crosses the leap");
                r.ApexTwirls.Should().Be(1, "and it is the apex twirl");
                break;
            case Leap.Late:
                r.Across.Should().BeTrue("the twirl late in its window (0.45 s after take-off) still crosses");
                break;
            case Leap.LateTakeOff:
                r.TakeOff.Should().Be(JumpEventKind.Jump, "a take-off press 0.05 s past the board is the long jump's take-off (the coyote jump, PLANET-85)");
                r.Twirl.Should().Be(JumpEventKind.AirJump, "the twirl at 0.37 s is the twirl");
                r.ApexTwirls.Should().Be(1, "at the top of the jump");
                r.Across.Should().BeTrue("and it crosses");
                break;
            default:
                r.Fell.Should().BeTrue("the same jump without the twirl falls short");
                break;
        }
    }

    /// <summary>
    /// <c>PlanetsProbe.RecoverySlip</c> on Event Horizon: stood 1.3 m from the rim the probe finds (25° left of the arrival,
    /// towards the jet's opening) facing it, W walks off; Space <paramref name="pressAfter"/> s after the feet leave the rim
    /// (W held on or let go then), held 12 frames. True when caught: one recovery, back on the ground over solid shell.
    /// </summary>
    private static (bool Caught, int Recoveries, bool Fell) ReplaySlip(PlanetComposition comp, float pressAfter, bool holdOn)
    {
        var shell = comp.Surface.Shell!;
        var r = comp.Shape.Radius;
        var edge = Enumerable.Range(1, 400).Select(i => PlanetFrame.FromPolar(i * 0.025f / r, -25f * MathF.PI / 180f)).First(shell.OverHole);
        var inward = Vector3.Normalize(Vector3.UnitY - edge * edge.Y);
        var stand = PlanetFrame.Move(edge, inward, 1.3f, r);
        var toward = -Vector3.Normalize(inward - stand * Vector3.Dot(inward, stand));
        var w = new SphereWalker();
        w.Teleport(comp.Surface, stand, toward);
        bool off = false, pressed = false, walkHeld = true;
        int pressFrames = 0;
        float air = 0;
        var keys = new LaggedKeys();
        for (var frame = 0; frame < 300; frame++)
        {
            var (press, held, walk) = keys.Next();
            var wish = walk ? Vector3.Normalize(toward - w.Up * Vector3.Dot(toward, w.Up)) : Vector3.Zero;
            w.Update(new SphereWalkInput(wish, false, press, held), Frame);
            air = w.Grounded ? 0 : air + FrameSeconds;
            if (w.FallCommitted) return (false, w.Recoveries, true);

            // The probe's frame: W let go as the feet leave (mid-window), Space at the press time, held 12 frames.
            if (!w.Grounded && !off)
            {
                off = true;
                if (!holdOn) walkHeld = false;
            }

            var pressNow = false;
            if (off && !pressed && air >= pressAfter - HalfFrameSeconds) pressed = pressNow = true;
            if (pressed && pressFrames > 3 && w.Grounded) break;
            if (pressed) pressFrames++;
            keys.Send(pressNow, pressed && pressFrames <= 12, walkHeld);
        }

        // The probe lets go of both keys and waits 20 frames before its check: the walker settles (or falls).
        for (var frame = 0; frame < 20; frame++)
        {
            w.Update(new SphereWalkInput(Vector3.Zero, false, false, false), Frame);
            if (w.FallCommitted) return (false, w.Recoveries, true);
        }

        return (w.Recoveries == 1 && w.Grounded && !shell.OverHole(w.Up), w.Recoveries, false);
    }

    [Trait(TestKinds.Trait, TestKinds.Slow)]
    [Theory]
    [InlineData(0.03f, true, "early")]
    [InlineData(0.2f, false, "mid-window")]
    [InlineData(-1f, true, "late")]
    public void The_hazard_journey_s_rim_catches_on_event_horizon(float pressAfter, bool holdOn, string what)
    {
        var comp = Compose("Event Horizon");
        // "late": 0.05 s before the catch's window closes, as the probe computes it.
        if (pressAfter < 0) pressAfter = (float)SphereWalkParams.Default.Hazard.Window.TotalSeconds - 0.05f;
        var (caught, recoveries, fell) = ReplaySlip(comp, pressAfter, holdOn);
        output.WriteLine(FormattableString.Invariant($"{what}: Space {pressAfter:0.00} s after the slip, W {(holdOn ? "held on" : "let go")}: {recoveries} recovery, {(fell ? "fell" : caught ? "back on the rim" : "not caught")}"));
        caught.Should().BeTrue($"a slip off Event Horizon's rim is caught by Space {what} ({pressAfter:0.00} s after it)");
    }
}
