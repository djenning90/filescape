using System.Numerics;
using FileScape.Core.Planets;
using FileScape.Core.Scanning;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-185..186 (AD-97): the overlook's curved stepping stones (the way up to the telescope) climbed with forward and a
/// turn held together. DJ (2026-11-20): "Play the curved telescope steps with simultaneous forward movement and turning.
/// Investigate getting trapped against the side wall." Each case composes Starter Sky worlds as the app does and steps the
/// default <see cref="SphereWalker"/> at the app's frame rates with the app's keyboard camera: ↑ held (the wish is the
/// camera's forward, PlanetWorld.Walk), ← or → held turning the camera at <see cref="KeyTurnDegreesPerSecond"/>
/// (× <see cref="FastTurnFactor"/> with Shift, which also runs) after its <see cref="KeyEaseInSeconds"/> ease-in, or at a
/// fraction of that (a player tapping the key), from the foot of the first stone facing along the ring.
/// <para>
/// Found (2026-11-20): with the plain slide (speed × cos of the angle into the wall) the platform's curved wall turned the
/// walker's heading slower than the held key turned the view, so the input swung head-on into the wall, the walker slowed
/// to a stop against it on the fourth stone, facing it, then walked back down. The old rule is
/// <c>WallSlideFullDegrees = 0</c>, kept here as the comparison that shows the trap.
/// </para>
/// What a change must preserve: the keyboard-turn constants mirror the app's (PlanetWorld.Interact KeyLookDegreesPerSecond,
/// PlanetWorld.Walk KeyLookEaseInSeconds and KeyLookFastFactor).
/// </summary>
public sealed class OverlookStepsTests(SampleSkyFixture sky, ITestOutputHelper output) : IClassFixture<SampleSkyFixture>
{
    /// <summary>The app's keyboard turn, degrees per second (PlanetWorld.KeyLookDegreesPerSecond).</summary>
    private const float KeyTurnDegreesPerSecond = 110f;

    /// <summary>Shift turns faster by this factor (PlanetWorld.KeyLookFastFactor).</summary>
    private const float FastTurnFactor = 1.6f;

    /// <summary>The key turn eases in over this, seconds (PlanetWorld.KeyLookEaseInSeconds).</summary>
    private const float KeyEaseInSeconds = 0.12f;

    /// <summary>How long each climb is played, seconds (the seven stones take ≈ 1.5 s at walking pace).</summary>
    private const float ClimbSeconds = 6f;

    /// <summary>Slower than this fraction of the speed asked for, with ↑ held, on a stone, against the wall, the walker counts as pinned.</summary>
    private const float PinnedSpeedFraction = 0.5f;

    /// <summary>Within this of touching the platform's wall, metres, the walker counts as against it.</summary>
    private const float WallContactMetres = 0.05f;

    /// <summary>Pinned against the wall longer than this in one climb, seconds, is a trap.</summary>
    private const float TrapSeconds = 0.25f;

    /// <summary>What a climb did: up on the platform (and when), seconds pinned against the platform's wall, the highest support reached (metres), and whether it ended on the ground beside the steps.</summary>
    private readonly record struct Climb(bool Up, float UpAt, float Pinned, float Highest, bool FellOff)
    {
        public override string ToString() => FormattableString.Invariant($"{(Up ? $"up at {UpAt:0.00} s" : $"not up (highest {Highest:0.0} m{(FellOff ? ", off the steps" : "")})")}, pinned {Pinned:0.00} s");
    }

    /// <summary>The Starter Sky's world, two of its folders' worlds and a Hazard World's (Event Horizon: its overlook on a rock island): the overlook is the same complex on every world, only the planet's size and relief differ.</summary>
    private List<(string Name, PlanetComposition Comp)> Worlds()
    {
        var (tree, _) = ScanRunner.Run(new DirectoryScanner(sky.Root));
        var dirs = tree.Nodes.Where(n => n.IsDirectory).ToList();
        var pick = dirs.Take(3).Concat(dirs.Where(n => n.Name == "Event Horizon")).Distinct();
        return pick.Select(n => (Path.GetFileName(n.Name.TrimEnd('/', '\\')), PlanetComposer.Compose(tree, n.Id))).Where(w => w.Item2.Overlook.Steps.Count > 0).ToList();
    }

    /// <summary>
    /// Climbs <paramref name="comp"/>'s steps: ↑ held throughout, the turn key that first swings the view towards the
    /// platform (<paramref name="inward"/>) or away from it held at <paramref name="turnFraction"/> of its rate
    /// (<paramref name="fast"/>: Shift held), at <paramref name="fps"/> frames a second, with <paramref name="walk"/>'s walker.
    /// </summary>
    private static Climb Play(PlanetComposition comp, float turnFraction, bool inward, bool fast, int fps, SphereWalkParams? walk = null)
    {
        var ov = comp.Overlook;
        var r = comp.Shape.Radius;
        var s0 = ov.Steps[0];
        var s1 = ov.Steps[1];
        var along = Vector3.Normalize(s1.Direction - s0.Direction - s0.Direction * Vector3.Dot(s1.Direction - s0.Direction, s0.Direction));
        var start = PlanetFrame.Move(s0.Direction, -along, s0.Radius + 0.35f + 0.5f, r);
        var w = new SphereWalker(walk);
        w.Teleport(comp.Surface, start, along);
        var cam = w.Facing;
        var frame = TimeSpan.FromSeconds(1.0 / fps);
        var dt = 1f / fps;
        var keyLook = 0f;
        float pinned = 0, highest = 0, upAt = -1;
        var wallMetres = ov.Radius + w.Params.BodyRadius.Value;
        var asked = (fast ? w.Params.RunSpeed : w.Params.WalkSpeed).Value;
        // The key held is the one that first swings the view towards the platform (inward) or away; it stays held.
        var startToCentre = ov.Direction - w.Up * Vector3.Dot(ov.Direction, w.Up);
        var key = (Vector3.Dot(Vector3.Cross(w.Up, cam), startToCentre) > 0 ? 1f : -1f) * (inward ? 1 : -1);
        for (var i = 0; i < ClimbSeconds * fps; i++)
        {
            var up = w.Up;
            cam = Vector3.Normalize(cam - up * Vector3.Dot(cam, up)); // the camera's heading carried onto the new tangent plane
            keyLook = MathF.Min(1, keyLook + dt / KeyEaseInSeconds);
            var turn = key * keyLook * turnFraction * KeyTurnDegreesPerSecond * (fast ? FastTurnFactor : 1) * dt * MathF.PI / 180f;
            cam = cam * MathF.Cos(turn) + Vector3.Cross(up, cam) * MathF.Sin(turn);
            w.Update(new SphereWalkInput(cam, fast, false, false), frame);
            highest = MathF.Max(highest, w.Support.Value);
            if (w.Grounded && w.Support.Value >= ov.Top - 1e-3f)
            {
                upAt = (i + 1) * dt;
                break;
            }

            var fromCentre = PlanetShape.Angle(w.Up, ov.Direction) * r;
            // On a stone only: walking into the platform's bare side from the ground is a head-on push, and that may stop you.
            if (w.Support.Value > 0 && w.Velocity.Length() < asked * PinnedSpeedFraction && fromCentre < wallMetres + WallContactMetres) pinned += dt;
        }

        var off = upAt < 0 && w.Grounded && w.Support.Value < 1e-3f && highest > 0;
        return new Climb(upAt >= 0, upAt, pinned, highest, off);
    }

    /// <summary>Turn rates as a fraction of the key's: none, a tapped key (a third, two thirds) and the key held.</summary>
    private static readonly float[] TurnFractions = [0f, 0.33f, 0.66f, 1f];

    /// <summary>The app's frame rates: 30, 60 and 144 fps.</summary>
    private static readonly int[] FrameRates = [30, 60, 144];

    [Fact]
    public void Forward_and_a_turn_held_never_pin_the_walker_against_the_platform()
    {
        var worlds = Worlds();
        worlds.Count.Should().BeGreaterThanOrEqualTo(3);
        var pinned = new List<string>();
        foreach (var (name, comp) in worlds)
        foreach (var fast in new[] { false, true })
        foreach (var inward in new[] { true, false })
        foreach (var f in TurnFractions)
        foreach (var fps in FrameRates)
        {
            var c = Play(comp, f, inward, fast, fps);
            var what = FormattableString.Invariant($"{name} ({comp.Shape.Radius:0.0} m), {(fast ? "running" : "walking")}, turn {(inward ? "towards the platform" : "away")} ×{f:0.00}, {fps} fps: {c}");
            if (fps == 60 && inward) output.WriteLine(what);
            if (c.Pinned > TrapSeconds) pinned.Add(what);
        }

        pinned.Should().BeEmpty("forward and a turn held never pin the walker against the platform's wall");
    }

    [Fact]
    public void Walking_up_with_forward_and_the_turn_key_held_reaches_the_platform()
    {
        // DJ's case: ↑ and ← (or →, whichever the curve needs) held together, walking, at every frame rate.
        foreach (var (name, comp) in Worlds())
        foreach (var fps in FrameRates)
        {
            var c = Play(comp, 1f, inward: true, fast: false, fps);
            output.WriteLine(FormattableString.Invariant($"{name}, walking, the key held, {fps} fps: {c}"));
            c.Up.Should().BeTrue($"walking up {name}'s steps with ↑ and the turn key held reaches the platform at {fps} fps");
            c.Pinned.Should().BeLessThanOrEqualTo(TrapSeconds);
        }
    }

    [Fact]
    public void The_plain_slide_pinned_the_walker_against_the_wall()
    {
        // The comparison (the old rule, WallSlideFullDegrees 0: speed × cos of the angle): the same climb as DJ's is held
        // against the wall and never gets up. This is what the two tests above guard against.
        var (name, comp) = Worlds()[0];
        var old = Play(comp, 1f, inward: true, fast: false, 60, SphereWalkParams.Default with { WallSlideFullDegrees = 0 });
        var now = Play(comp, 1f, inward: true, fast: false, 60);
        output.WriteLine($"{name}, walking, the key held, 60 fps: the plain slide {old}; now {now}");
        old.Up.Should().BeFalse("the plain slide stopped the walker against the wall");
        old.Pinned.Should().BeGreaterThan(TrapSeconds);
        now.Up.Should().BeTrue();
    }
}
