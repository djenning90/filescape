using System.Numerics;
using FileScape.Core.Planets;
using FileScape.Core.Scanning;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>
/// The repository workbench jumped onto with the keyboard as the real-input probe's -Route does it (PLANET-29, AD-50):
/// standing a step from its rim, ↑ held, Space after 0.12 s, ↑ let go 0.7 s after Space. Stream R (seventh pass,
/// 2026-11-21): on the candidate e1088c6 the probe stood touching the rim 7–9° off the bench's centre and the jump ended
/// off the deck, so the spin that reads the README came from the ground (os -Route: "a spin from the bench opens the
/// README (selected Lantern Workshop)"; 1 run in 3; trace-route-bench.csv: the body faced 80° off the camera, the camera
/// swung 19° towards it, the jump went along the rim). Cause (AD-97, PLANET-185): the probe's last ↑ pulse pressed
/// nearly head-on into the rim, the guided wall slide carried the walker along it at twice the plain slide's speed, the
/// body turned to that slide, and the app's camera swung in behind the body, so ↑ steered along the rim.
/// <see cref="After_pressing_against_the_rim_a_jump_still_ends_on_the_deck"/> replays that approach (the camera's swing-in
/// modelled on PlanetWorld.Walk's) and failed first (AD-97's slide alone: off the deck from 4° off the centre); a push
/// within 15° of head-on now slides at the plain speed and the body keeps facing the input
/// (<see cref="SphereWalkParams.WallSlideHeadOnDegrees"/>).
/// <para>
/// What a change must preserve: a jump onto the bench from a step away, ↑ held as the probe holds it, ends standing on the
/// deck, well inside its rim (so the spin from it reads the README), also right after pressing against the rim.
/// </para>
/// </summary>
public sealed class BenchJumpTests(SampleSkyFixture sky, ITestOutputHelper output) : IClassFixture<SampleSkyFixture>
{
    /// <summary>The app's display frame at 60 fps.</summary>
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

    /// <summary>↑ alone before Space, seconds (the probe's 120 ms).</summary>
    private const float RunUpSeconds = 0.12f;

    /// <summary>Space is held this long, seconds (the probe's 90 ms).</summary>
    private const float SpaceHoldSeconds = 0.09f;

    /// <summary>↑ is let go this long after Space, seconds (the probe's 700 ms).</summary>
    private const float UpHoldSeconds = 0.7f;

    /// <summary>The probe's last ↑ pulse before the jump, seconds (Hold-Key W 120 ms + 60 ms a metre), and the pause after it (its 120 ms sleep and a state read).</summary>
    private const float PulseSeconds = 0.15f, PauseSeconds = 0.15f;

    /// <summary>The app's walking camera swings in behind a body moving faster than this, m/s, at this rate per second, within this of behind, degrees (PlanetWorld.Walk: 0.8 m/s, SwingInRatePerSecond, SwingInMaxDegrees).</summary>
    private const float SwingInMovingMetresPerSecond = 0.8f, SwingInRatePerSecond = 1.4f, SwingInMaxDegrees = 135f;

    /// <summary>AD-97's wall slide alone (no plain slide near head-on, the body turning to the slide): the candidate e1088c6's walker.</summary>
    private static readonly SphereWalkParams Ad97Slide = SphereWalkParams.Default with { WallSlideHeadOnFromDegrees = 89.9f, WallSlideHeadOnDegrees = 90f };

    /// <summary>How long each case is played, seconds (the probe looks 1.4 s after Space).</summary>
    private const float PlaySeconds = 2f;

    /// <summary>
    /// Ending at least this far inside the rim is well on the deck, metres: the app counts up to 0.3 m outside it as on the
    /// bench (StandingOnBench), so 0.3 m inside is that margin again. A slide softened over the whole range (stream R tried
    /// the squared and 1.5-power ratio, and the plain slide from 72°) pinned walkers on the overlook's stones
    /// (OverlookStepsTests), so only pushes within 15° of head-on slide at the plain speed.
    /// </summary>
    private const float WellInsideMetres = 0.3f;

    private PlanetComposition Bench()
    {
        var (tree, _) = ScanRunner.Run(new DirectoryScanner(sky.Root));
        var lantern = tree.Nodes.First(n => n.IsDirectory && n.Name.TrimEnd('/', '\\').EndsWith("Lantern Workshop", StringComparison.Ordinal));
        return PlanetComposer.Compose(tree, lantern.Id);
    }

    /// <summary>Where the jump ended: metres inside the rim (negative outside), and standing on the deck (the app's StandingOnBench).</summary>
    private static (float Inside, bool OnDeck) Jump(PlanetComposition comp, float gapMetres, float offsetDegrees, SphereWalkParams? walk = null, bool probeApproach = false)
    {
        var ov = comp.Overlook;
        var r = comp.Shape.Radius;
        var pole = comp.Arrival.Up; // the walk comes from the arrival, as the bench's route does (PlanetComposer)
        var outward = Vector3.Normalize(pole - ov.Direction * Vector3.Dot(pole, ov.Direction));
        var start = PlanetFrame.Move(ov.Direction, outward, ov.Radius + gapMetres, r);
        var toCentre = Vector3.Normalize(ov.Direction - start * Vector3.Dot(ov.Direction, start));
        var turn = offsetDegrees * MathF.PI / 180f;
        var heading = toCentre * MathF.Cos(turn) + Vector3.Cross(start, toCentre) * MathF.Sin(turn);
        var w = new SphereWalker(walk);
        w.Teleport(comp.Surface, start, heading);
        var dt = (float)Frame.TotalSeconds;
        // The probe's approach (probeApproach): its last ↑ pulse towards the bench (PulseSeconds) ends pressed against the
        // rim, then a pause (PauseSeconds) before the jump's ↑; the camera swings in behind the moving body as the app's does.
        var lead = probeApproach ? PulseSeconds + PauseSeconds : 0f;
        for (var i = 0; i < (PlaySeconds + lead) / dt; i++)
        {
            var t = i * dt - lead;
            heading = Vector3.Normalize(heading - w.Up * Vector3.Dot(heading, w.Up)); // the camera's heading, carried along
            if (probeApproach && w.Velocity.Length() > SwingInMovingMetresPerSecond)
            {
                var ang = MathF.Atan2(Vector3.Dot(Vector3.Cross(heading, w.Facing), w.Up), Vector3.Dot(heading, w.Facing));
                if (MathF.Abs(ang) < SwingInMaxDegrees * MathF.PI / 180f)
                {
                    var step = ang * (1 - MathF.Exp(-SwingInRatePerSecond * dt));
                    heading = Vector3.Normalize(heading * MathF.Cos(step) + Vector3.Cross(w.Up, heading) * MathF.Sin(step));
                }
            }

            var press = MathF.Abs(t - RunUpSeconds) < dt / 2;
            var held = t >= RunUpSeconds && t < RunUpSeconds + SpaceHoldSeconds;
            var up = t < -PauseSeconds || (t >= 0 && t < RunUpSeconds + UpHoldSeconds);
            w.Update(new SphereWalkInput(up ? heading : Vector3.Zero, false, press, held), Frame);
        }

        var inside = ov.Radius - PlanetShape.Angle(w.Up, ov.Direction) * r;
        return (inside, w.Grounded && w.Support.Value > ov.Top - 0.3f && inside > -0.3f);
    }

    [Fact]
    public void A_jump_from_a_step_away_ends_well_on_the_deck()
    {
        var comp = Bench();
        var bad = new List<string>();
        foreach (var gap in new[] { 0.35f, 0.5f, 0.8f })
        foreach (var offset in new[] { 0f, 4f, 7f, 10f, 15f })
        {
            var now = Jump(comp, gap, offset);
            var plain = Jump(comp, gap, offset, SphereWalkParams.Default with { WallSlideFullDegrees = 0 });
            var row = FormattableString.Invariant($"{gap:0.00} m out, {offset:0}° off the centre: ends {now.Inside:0.00} m inside, on the deck {now.OnDeck} (the plain slide: {plain.Inside:0.00} m, {plain.OnDeck})");
            output.WriteLine(row);
            if (!now.OnDeck || now.Inside < WellInsideMetres) bad.Add(row);
        }

        bad.Should().BeEmpty("a jump onto the workbench from a step away, ↑ held as the probe holds it, ends well on the deck");
    }

    [Fact]
    public void After_pressing_against_the_rim_a_jump_still_ends_on_the_deck()
    {
        // The probe's own approach: its last ↑ pulse ends pressed against the rim a few degrees off the centre, the body
        // turns to the slide along the rim, the camera swings in behind the body, and ↑ then follows the camera.
        var comp = Bench();
        var bad = new List<string>();
        foreach (var offset in new[] { 0f, 4f, 7f, 9f, 10f, 11f, 12f })
        {
            var now = Jump(comp, 0.35f, offset, probeApproach: true);
            var ad97 = Jump(comp, 0.35f, offset, Ad97Slide, probeApproach: true);
            var row = FormattableString.Invariant($"touching the rim, {offset:0}° off the centre, after the probe's approach: ends {now.Inside:0.00} m inside, on the deck {now.OnDeck} (AD-97's slide alone: {ad97.Inside:0.00} m, {ad97.OnDeck})");
            output.WriteLine(row);
            // Judged up to 9° (the probe met 5.9–9.3°; it stops steering within 12°); from 10° the landing nears the rim (reported).
            if (offset <= 9f && (!now.OnDeck || now.Inside < WellInsideMetres)) bad.Add(row);
        }

        bad.Should().BeEmpty("pressing against the rim never turns the jump away from the bench");
    }
}
