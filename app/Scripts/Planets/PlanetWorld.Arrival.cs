using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// The arrival framing (PLANET-42, AD-57): after a landing, a warp or the opening shot the chase camera stands a little
/// higher and further back and looks a little further ahead, so the near horizon drops and more of the world shows
/// over it — its launchers, its groves and, on a repository, the workbench, which now stands on the first ring in
/// front of you (PlanetComposer). The moment you move (walk, jump, a hop) it eases back into the close follow camera
/// and stays there until the next arrival; standing still, or looking around with a drag or ← →, keeps it.
/// <para>
/// PLANET-67 (AD-67): the framing adds its lift to the pitch you choose and scales the distance you choose; it never
/// replaces them, so a vertical drag or the wheel works while it holds (it held the whole time you stood still, and
/// vertical look did nothing). Each arrival starts from the usual pitch, so every landing is framed as designed whatever
/// you looked at before; the wheel's distance is kept.
/// </para>
/// <para>
/// It is a weight on the chase camera's own boom (<see cref="ArrivalWeight"/>, read by <see cref="UpdateCamera"/>),
/// not a separate camera: the landing's blend, the roll cap, the eye-clearance rule and the overlook view all apply to
/// it unchanged, and the flight and the launch are not touched. Its shape scales with the planet's radius (a small world
/// gets a smaller lift, so its far side does not fill the frame).
/// </para>
/// </summary>
public partial class PlanetWorld
{
    // ---- the arrival framing (by eye, 2026-11-16, judged in motion on the Starter Sky, Lantern Workshop, Music Box and the empty moon)

    /// <summary>The arrival's pitch above the traveller, degrees (the walking camera's is <see cref="StartCamPitchDegrees"/>, 18°):
    /// a steeper look down puts the horizon higher in the frame, so the ground beyond the traveller shows.</summary>
    private const float ArrivalPitchDegrees = 29f;

    /// <summary>The arrival's boom length on a large world, metres (walking: <see cref="StartCamDistanceMetres"/>, 7.2 m);
    /// the eye is then ≈ 5 m above the ground instead of ≈ 2.2 m.</summary>
    private const float ArrivalDistanceMetres = 10f;

    /// <summary>The boom never grows past this fraction of the planet's radius (a 16 m world: 9.6 m; the 10 m empty moon: 6 m,
    /// i.e. no longer than the walking boom); larger worlds get the full <see cref="ArrivalDistanceMetres"/>.</summary>
    private const float ArrivalDistancePerRadius = 0.6f;

    /// <summary>The aim point moves this far ahead of the traveller, metres, so the traveller sits lower and the world ahead higher in the frame.</summary>
    private const float ArrivalLookAheadMetres = 2.2f;

    /// <summary>Once you move, the framing eases out at this rate, per second of blend (linear in the weight, eased by
    /// <see cref="Smooth"/>: 1 → 0 in ≈ 0.7 s; the follow rates then glide the camera in behind you).</summary>
    private const float ArrivalReleasePerSecond = 1.4f;

    /// <summary>0..1, the arrival framing's weight (1 just after landing; eased to 0 once you move).</summary>
    private float _arrival01;

    /// <summary>The traveller has moved since the last arrival: the framing is on its way out.</summary>
    private bool _arrivalReleased = true;

    /// <summary>The arrival framing's current weight (probes).</summary>
    public float ArrivalFraming => _arrival01;

    /// <summary>Starts the arrival framing (a landing, a warp's touchdown, the opening shot), from the usual pitch.</summary>
    private void BeginArrivalFraming()
    {
        _arrival01 = 1f;
        _arrivalReleased = false;
        _camPitchDegrees = StartCamPitchDegrees; // the designed arrival shot every time (AD-67); you tilt from there
    }

    /// <summary>Per walking frame: moving (walking, a jump, a hop) releases the framing, which then eases out.</summary>
    private void UpdateArrivalFraming(float dt, bool moved)
    {
        if (moved) _arrivalReleased = true;
        if (_arrivalReleased) _arrival01 = Mathf.MoveToward(_arrival01, 0, dt * ArrivalReleasePerSecond);
    }

    /// <summary>The eased weight the chase camera uses (smoothstep of <see cref="_arrival01"/>).</summary>
    private float ArrivalWeight() => Smooth(_arrival01);

    /// <summary>The arrival boom length on this world, metres: <see cref="ArrivalDistanceMetres"/>, capped by the planet's size.</summary>
    private float ArrivalDistanceHere() =>
        MathF.Max(StartCamDistanceMetres, MathF.Min(ArrivalDistanceMetres, (_current?.Comp.Shape.Radius ?? 0f) * ArrivalDistancePerRadius));

    /// <summary>The workbench on screen now: its deck's centre in viewport units, or null when it is behind the camera, off
    /// screen or hidden by the planet (probes: "you see it when you land").</summary>
    public Vector2? BenchOnScreen()
    {
        if (!OnBenchWorld || _current!.Comp.Overlook is not { } ov) return null;
        var p = Gd(_current.Comp.Surface.PointAt(ov.Direction, ov.Top));
        if (_camera.IsPositionBehind(p)) return null;
        var o = _camera.GlobalPosition;
        var d = (p - o).Normalized();
        if (RayPlanet(o, d, _current.Comp.Shape.Radius - OcclusionInsetMetres) is { } ground && ground < o.DistanceTo(p) - OcclusionSlackMetres - ov.Radius) return null;
        var s = _camera.UnprojectPosition(p);
        var size = GetViewport().GetVisibleRect().Size;
        return s.X >= 0 && s.Y >= 0 && s.X <= size.X && s.Y <= size.Y ? s : null;
    }

    /// <summary>The camera's eye height above the ground under it, metres (probes judge the framing by it).</summary>
    public float CameraEyeHeight()
    {
        if (_current == null) return 0;
        var e = _camera.GlobalPosition;
        return e.Length() - _current.Comp.Shape.GroundRadius(Nv(e.Normalized()));
    }

    /// <summary>The camera's distance from the traveller's middle, metres.</summary>
    public float CameraToTraveller() => _camera.GlobalPosition.DistanceTo(Gd(_walker.Feet) + Gd(_walker.Up) * TravellerMiddleMetres);

    /// <summary>The sign that marks the workbench before it is revealed (PLANET-42): its location is apparent from the
    /// arrival. Pinned over the deck; the cards' own names take over once it is revealed.</summary>
    private void AddBenchMarkerSign()
    {
        if (!OnBenchWorld || _benchRevealed || _current!.Comp.Overlook is not { } ov) return;
        var at = Gd(_current.Comp.Surface.PointAt(ov.Direction, ov.Top + BenchMarkerHeightMetres));
        _signs.Add(new SignCandidate(BenchMarkerSignId, at, "the workbench", "jump on: this project's key files", BenchColor.Lightened(0.2f), IconKind.Code,
            false, BenchMarkerPriority, FixedAnchor: true));
    }

    /// <summary>The bench marker's sign id (a fixed negative id apart from pads, worlds, −100 000 − id, and cards, −200 000 − id).</summary>
    public const int BenchMarkerSignId = -300_000;

    /// <summary>The marker floats this high over the deck, metres (above the traveller's head when standing by it).</summary>
    private const float BenchMarkerHeightMetres = 1.9f;

    /// <summary>The marker's priority among signs: above a pad's ordinary name (rationale: it is the one thing on a
    /// repository DJ asked to see on arrival), below a selected or pointed-at destination.</summary>
    private const float BenchMarkerPriority = 40f;
}
