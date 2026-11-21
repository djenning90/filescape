using System.Numerics;
using FileScape.Core.Planets;
using FileScape.Core.Scanning;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>
/// The repository workbench jumped onto with the keyboard as the real-input probe's -Route does it (PLANET-29, AD-50):
/// standing a step from its rim, ↑ held, Space after 0.12 s, ↑ let go 0.7 s after Space. Stream R (seventh pass,
/// 2026-11-21): on the candidate e1088c6 the probe once stood touching the rim 7° off the bench's centre and ended off the
/// deck, so the spin that reads the README came from the ground (os -Route: "a spin from the bench opens the README
/// (selected Lantern Workshop)"); the next run on the same build passed. Suspected: AD-97's wall slide (a near head-on push
/// slides along the rim twice as fast as the plain slide). This replay, with the composed world and the default walker at
/// 60 fps from 0.35–0.8 m out and 0–15° off the centre, does not reproduce it with either slide rule: the Core walk is not
/// the cause (the wall slide only brings an angled landing nearer the rim: <see cref="WellInsideMetres"/>). The app then steered by the rendered camera, which trails the heading for ≈ 0.13 s after the probe's last
/// turn (OverlookStepsTests' comparison); it now steers by the heading, as this replay does.
/// <para>
/// What a change must preserve: a jump onto the bench from a step away, ↑ held as the probe holds it, ends standing on the
/// deck, well inside its rim (so the spin from it reads the README).
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

    /// <summary>How long each case is played, seconds (the probe looks 1.4 s after Space).</summary>
    private const float PlaySeconds = 2f;

    /// <summary>
    /// Ending at least this far inside the rim is well on the deck, metres: the app counts up to 0.3 m outside it as on the
    /// bench (StandingOnBench), so 0.3 m inside is that margin again. The wall slide's worst case here is 0.44 m (touching
    /// the rim, 15° off the centre; 0.93 m with the plain slide): AD-97 slides a near head-on push faster along the rim. A
    /// softer slide near head-on (stream R tried the squared and 1.5-power ratio) pinned walkers on the overlook's stones
    /// (OverlookStepsTests), so the slide stays as AD-97 has it.
    /// </summary>
    private const float WellInsideMetres = 0.3f;

    private PlanetComposition Bench()
    {
        var (tree, _) = ScanRunner.Run(new DirectoryScanner(sky.Root));
        var lantern = tree.Nodes.First(n => n.IsDirectory && n.Name.TrimEnd('/', '\\').EndsWith("Lantern Workshop", StringComparison.Ordinal));
        return PlanetComposer.Compose(tree, lantern.Id);
    }

    /// <summary>Where the jump ended: metres inside the rim (negative outside), and standing on the deck (the app's StandingOnBench).</summary>
    private static (float Inside, bool OnDeck) Jump(PlanetComposition comp, float gapMetres, float offsetDegrees, SphereWalkParams? walk = null)
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
        for (var i = 0; i < PlaySeconds / dt; i++)
        {
            var t = i * dt;
            heading = Vector3.Normalize(heading - w.Up * Vector3.Dot(heading, w.Up)); // the camera's heading, carried along
            var press = MathF.Abs(t - RunUpSeconds) < dt / 2;
            var held = t >= RunUpSeconds && t < RunUpSeconds + SpaceHoldSeconds;
            var up = t < RunUpSeconds + UpHoldSeconds;
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
}
