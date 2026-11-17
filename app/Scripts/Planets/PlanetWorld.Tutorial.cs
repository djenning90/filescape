using FileScape.App.Visual;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// Where the current walkthrough step wants you to go (PLANET-38): a surface direction on the current planet, how high
/// the spot is (a pad, the bench's deck), how far from its centre counts as being there, how high the screen chevron
/// floats over it, and what to do once you are there (the pill's words, e.g. "Press Space to fly"). Metres throughout.
/// </summary>
public sealed record TutorialTarget(NVector3 Direction, string Action, float LiftMetres, float ReachMetres, float MarkerAboveMetres);

/// <summary>
/// The walkthrough's hooks into the planet (PLANET-33, AD-54; the cues PLANET-38, AD-55). It draws the current step's
/// target in a visual language of its own, lime and moving, which nothing else in the planets uses and which has no
/// sign-style label:
/// <list type="bullet">
/// <item>in the world: a tall lime column of light rising from the spot and a band of light round it on the ground
/// marking where to stand, with ripples that close in on it;</item>
/// <item>on screen (<see cref="Planets.TutorialCue"/>): a bobbing chevron pinned over the spot when it is in view; the same at
/// the horizon, with "over the horizon", when the planet's curve hides it; an arrow at the screen's edge, pointing the
/// way to turn, when it is out of view or behind you;</item>
/// <item>as you approach (within <see cref="ApproachMetres"/> of the spot's edge) the ring brightens, the ripples speed
/// up, the chevron grows and comes down, and a lime pill says what to do there; standing on it, the pill stays.</item>
/// </list>
/// It also answers the few read-only questions the lessons ask (how far you are from a spot) and makes room for the
/// lesson card. The lessons themselves (what to say, when a step is done, the text hint) live in <c>Main.Tutorial.cs</c>;
/// this file only draws and measures. A change must keep: the cue hidden whenever there is no target (between steps,
/// travelling, in the overview, after Leave), the column and ring never casting shadows or taking clicks, and
/// <see cref="TutorialCue"/> (the state the probes read) describing exactly what is drawn.
/// </summary>
public partial class PlanetWorld
{
    private Node3D? _beacon;
    private MeshInstance3D? _beaconRing, _beaconRipple;
    private StandardMaterial3D? _coreMat, _glowMat, _ringMat, _rippleMat;
    private TutorialTarget? _beaconTarget;
    private float _beaconBuiltReach = -1;
    private float _beaconTime;
    private TutorialCue? _cueView;
    private int _edgeSide = 1;

    /// <summary>This frame's screen cue (what <see cref="Planets.TutorialCue"/> draws), for probes and the state file.</summary>
    public TutorialCueState TutorialCue { get; private set; } = TutorialCueState.Hidden;

    /// <summary>The current target (null when none), for probes.</summary>
    public TutorialTarget? TutorialTargetNow => _beaconTarget;

    /// <summary>How tall the column of light stands above the spot, metres: on the 20–30 m sample worlds its top shows
    /// over the curve from much of the planet, and it clears the tower.</summary>
    private const float BeaconHeightMetres = 16f;

    /// <summary>The column's bright core and its soft glow, radii in metres.</summary>
    private const float CoreRadiusMetres = 0.14f, GlowRadiusMetres = 0.65f;

    /// <summary>The ground ring is a short band of light this tall, metres: knee-high, so it marks the spot without walling
    /// in the traveller (0.8 m read as a glass jar round the pad). Plus the curve's drop under it, so it meets the ground all round.</summary>
    private const float RingBandMetres = 0.45f;

    /// <summary>Within this distance of the spot's edge, metres, the cue changes to "nearly there": brighter ring, faster
    /// ripples, a bigger, lower chevron and the action pill. Under a second's run (by eye).</summary>
    private const float ApproachMetres = 6f;

    /// <summary>A ripple closes from this many times the ring's radius onto the ring, then starts again.</summary>
    private const float RippleStartScale = 1.9f;

    /// <summary>How long one ripple takes to close in, seconds: calm far away, eager when near.</summary>
    private const float RippleFarSeconds = 1.6f, RippleNearSeconds = 0.8f;

    /// <summary>The column's core breathes between these opacities at <see cref="ColumnPulseHz"/> (cycles per second).</summary>
    private const float ColumnMinAlpha = 0.45f, ColumnMaxAlpha = 0.85f, ColumnPulseHz = 1f;

    /// <summary>The column's soft outer glow, opacity (steady).</summary>
    private const float ColumnGlowAlpha = 0.35f;

    /// <summary>Insets for the screen cue, viewport units: the edge arrow stays this far inside the left and right edges,
    /// and between <see cref="CueTopInset"/> (under the toolbar and place bar; lower when the lesson card is there) and
    /// <see cref="CueBottomInset"/> above the bottom (over the prompt line).</summary>
    private const float CueSideInset = 70f, CueTopInset = 170f, CueBottomInset = 180f;

    /// <summary>"Over the horizon" pins the chevron at a point this far along the ground's tangent towards the target from
    /// the camera, metres (far enough that it projects near the horizon of a small world); the edge arrow uses the same
    /// distance straight ahead to find the horizon's height on screen.</summary>
    private const float HorizonPinMetres = 30f;

    /// <summary>Beyond this bearing, degrees, the target counts as behind you: the edge arrow keeps the side it had
    /// rather than flip as the bearing crosses ±180°.</summary>
    private const float BehindKeepSideDegrees = 165f;

    /// <summary>The chevron floats at least this high over the spot when you are near, metres (and at 60 % of the far height).</summary>
    private const float NearMarkerMinMetres = 1.4f;

    /// <summary>
    /// Shows the walkthrough's target on the current planet (null hides the column, the ring and the screen cue).
    /// Called every frame by the walkthrough; the meshes are built once and the ring is reshaped only when the reach changes.
    /// </summary>
    public void SetTutorialTarget(TutorialTarget? target)
    {
        _beaconTarget = target;
        if (target == null || _current == null)
        {
            ClearBeacon();
            return;
        }

        if (_beacon == null) BuildBeacon();
        if (MathF.Abs(_beaconBuiltReach - target.ReachMetres) > 0.01f) ShapeRing(target.ReachMetres);
        PlaceBeacon();
    }

    private void ClearBeacon()
    {
        _beaconTarget = null;
        _beacon?.QueueFree();
        _beacon = null;
        _beaconRing = _beaconRipple = null;
        _beaconBuiltReach = -1;
        TutorialCue = TutorialCueState.Hidden;
        _cueView?.Show(TutorialCue, 0);
    }

    /// <summary>An unshaded, additive, double-sided glow of colour <paramref name="c"/> (its alpha scales the light).</summary>
    private static StandardMaterial3D Glow(Color c, Texture2D texture) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        AlbedoColor = c,
        AlbedoTexture = texture,
    };

    /// <summary>An open tube (no caps) for the column and the ring.</summary>
    private static MeshInstance3D Tube(float radius, float height, Material mat) => new()
    {
        Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = 48, Rings = 1, CapTop = false, CapBottom = false },
        MaterialOverride = mat,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    private void BuildBeacon()
    {
        var lime = Planets.TutorialCue.CueColor;
        _beacon = new Node3D { Name = "TutorialBeacon" };
        AddChild(_beacon);
        // Bright at the bottom, fading to nothing at the top (a CylinderMesh's V runs from its top, 0, to its bottom, 1).
        var fade = new GradientTexture2D
        {
            Gradient = new Gradient { Colors = [new Color(1, 1, 1, 0), new Color(1, 1, 1, 1)], Offsets = [0f, 1f] },
            FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1), Width = 4, Height = 64,
        };
        _coreMat = Glow(lime, fade);
        var core = Tube(CoreRadiusMetres, BeaconHeightMetres, _coreMat);
        core.Position = new Godot.Vector3(0, BeaconHeightMetres / 2, 0);
        _glowMat = Glow(lime with { A = ColumnGlowAlpha }, fade);
        var glow = Tube(GlowRadiusMetres, BeaconHeightMetres, _glowMat);
        glow.Position = core.Position;
        _ringMat = Glow(lime, fade);
        _rippleMat = Glow(lime, fade);
        _beaconRing = Tube(1, 1, _ringMat);
        _beaconRipple = Tube(1, 1, _rippleMat);
        _beacon.AddChild(core);
        _beacon.AddChild(glow);
        _beacon.AddChild(_beaconRing);
        _beacon.AddChild(_beaconRipple);
        _beaconBuiltReach = -1;
    }

    /// <summary>Sizes the ground ring for a reach (metres): a band of light that meets the ground all round despite the
    /// planet's curve (it reaches down by the curve's drop at that distance).</summary>
    private void ShapeRing(float reach)
    {
        if (_beaconRing == null || _beaconRipple == null || _current == null) return;
        var r = _current.Comp.Shape.Radius;
        var drop = reach < r ? r - MathF.Sqrt(r * r - reach * reach) : reach;
        var height = RingBandMetres + drop;
        _beaconRing.Mesh = new CylinderMesh { TopRadius = reach, BottomRadius = reach, Height = height, RadialSegments = 64, Rings = 1, CapTop = false, CapBottom = false };
        _beaconRing.Position = new Godot.Vector3(0, height / 2 - drop - 0.05f, 0);
        _beaconRipple.Mesh = new CylinderMesh { TopRadius = reach, BottomRadius = reach, Height = height * 0.5f, RadialSegments = 64, Rings = 1, CapTop = false, CapBottom = false };
        _beaconRipple.Position = new Godot.Vector3(0, height * 0.25f - drop - 0.05f, 0);
        _beaconBuiltReach = reach;
    }

    /// <summary>Re-stands the beacon on the current planet (after a rebuild or a move of the target).</summary>
    private void PlaceBeacon()
    {
        if (_beacon == null || _beaconTarget is not { } t || _current == null) return;
        _beacon.Transform = PlanetVisual.Stand(_current.Comp.Surface, t.Direction, PlanetFrame.North(t.Direction), 1f, height: t.LiftMetres, sink: 0f);
    }

    /// <summary>
    /// Per frame: animates the column and ring by how close you are, then works out the screen cue — pinned over the
    /// spot, at the horizon, or at the edge — and hands it to <see cref="Planets.TutorialCue"/>. Hidden while travelling.
    /// </summary>
    private void AnimateBeacon(float dt)
    {
        _beaconTime += dt;
        if (_beacon == null || _beaconTarget is not { } t || _current == null || _phase != Phase.Walking)
        {
            if (_beacon != null) _beacon.Visible = _phase == Phase.Walking;
            TutorialCue = TutorialCueState.Hidden;
            _cueView?.Show(TutorialCue, dt);
            return;
        }

        _beacon.Visible = true;
        var edgeDistance = DistanceTo(t.Direction) - t.ReachMetres;
        var phase = edgeDistance <= 0 ? TutorialCuePhase.Here : edgeDistance <= ApproachMetres ? TutorialCuePhase.Near : TutorialCuePhase.Far;
        var near = phase != TutorialCuePhase.Far;
        var lime = Planets.TutorialCue.CueColor;

        var breathe = 0.5f + 0.5f * Mathf.Sin(_beaconTime * Mathf.Tau * ColumnPulseHz);
        // The column is for finding the spot from afar: it fades as you close in (gone when you stand there), so it
        // never fills the view from close by; the ring and the pill take over.
        var columnFade = Mathf.Clamp(edgeDistance / ApproachMetres, 0f, 1f);
        _coreMat!.AlbedoColor = lime with { A = Mathf.Lerp(ColumnMinAlpha, ColumnMaxAlpha, breathe) * columnFade };
        _glowMat!.AlbedoColor = lime with { A = ColumnGlowAlpha * columnFade };
        _ringMat!.AlbedoColor = lime with { A = phase == TutorialCuePhase.Here ? 1f : near ? 0.75f + 0.25f * breathe : 0.55f };
        var rippleSeconds = near ? RippleNearSeconds : RippleFarSeconds;
        var u = _beaconTime % rippleSeconds / rippleSeconds;
        var s = Mathf.Lerp(RippleStartScale, 1f, u);
        _beaconRipple!.Scale = new Godot.Vector3(s, 1, s);
        _rippleMat!.AlbedoColor = lime with { A = phase == TutorialCuePhase.Here ? 0f : MathF.Sin(u * MathF.PI) * (near ? 0.9f : 0.6f) };

        TutorialCue = ComputeCue(t, phase, MathF.Max(0, edgeDistance));
        EnsureCueView();
        _cueView!.Show(TutorialCue, dt);
    }

    /// <summary>Adds the screen cue to the planets' own screen layer once, just above the warp flash and below every panel
    /// (the preview, the list and the bench panel cover it).</summary>
    private void EnsureCueView()
    {
        if (_cueView != null) return;
        _cueView = new TutorialCue { Name = "TutorialCue" };
        var root = _titleBox.GetParent<Control>();
        root.AddChild(_cueView);
        root.MoveChild(_cueView, _flash.GetIndex() + 1);
    }

    /// <summary>
    /// Decides the screen cue: the spot in view and not behind the planet's curve → a chevron pinned over it; ahead but
    /// behind the curve → a chevron at the horizon in its direction; otherwise → an arrow at the left or right edge, the
    /// way to turn (the side is kept while the target is nearly straight behind, so it does not flip back and forth).
    /// </summary>
    private TutorialCueState ComputeCue(TutorialTarget t, TutorialCuePhase phase, float distance)
    {
        var view = GetViewport().GetVisibleRect().Size;
        var top = CueTopInset;
        foreach (var r in _s.Hud.BlockingRects)
        {
            // Keep clear under a panel that spans the top centre (the lesson card): 64 units below it, room for the
            // chevron's distance label above it (40 clipped the label once the chevron waited at the top, 2026-11-17).
            if (r.Position.Y < view.Y * 0.4f && r.Position.X < view.X * 0.5f && r.End.X > view.X * 0.5f) top = MathF.Max(top, r.End.Y + 64f);
        }

        var inner = new Rect2(CueSideInset, top, view.X - 2 * CueSideInset, view.Y - CueBottomInset - top);
        var bearing = CameraTurnTo(t.Direction);
        var up = _beacon!.GlobalBasis.Y.Normalized();
        var spot = _beacon.GlobalPosition;
        var cam = _camera.GlobalPosition;
        var markerHeight = phase == TutorialCuePhase.Far ? t.MarkerAboveMetres : MathF.Max(NearMarkerMinMetres, t.MarkerAboveMetres * 0.6f);
        var marker = spot + up * markerHeight;
        var groundR = _current!.Comp.Shape.Radius - OcclusionInsetMetres;
        bool Hidden(Godot.Vector3 at) => RayPlanet(cam, (at - cam).Normalized(), groundR) is { } hit && hit < cam.DistanceTo(at) - OcclusionSlackMetres;
        // The chevron pins over the spot whenever the point it hangs from is in sight, even if the ground there is
        // already behind the curve (then it says "over the horizon"); only when that point is hidden too does it wait
        // at the horizon.
        var spotHidden = Hidden(spot);
        var hidden = Hidden(marker);

        if (!_camera.IsPositionBehind(marker) && (!hidden || phase == TutorialCuePhase.Here))
        {
            var p = _camera.UnprojectPosition(marker);
            if (inner.HasPoint(p)) return new TutorialCueState(TutorialCueMode.OnScreen, p, 90, distance, bearing, phase, t.Action, spotHidden && phase == TutorialCuePhase.Far);
            // In view but above the area the lesson card leaves free (a tall target seen from the higher arrival camera,
            // PLANET-42): the chevron waits at the top of that area straight below it (2026-11-17).
            if (p.X >= inner.Position.X && p.X <= inner.End.X && p.Y >= 0 && p.Y < inner.Position.Y)
                return new TutorialCueState(TutorialCueMode.Horizon, new Vector2(p.X, inner.Position.Y), 90, distance, bearing, phase, t.Action, spotHidden && phase == TutorialCuePhase.Far);
        }

        if (MathF.Abs(bearing) < BehindKeepSideDegrees) _edgeSide = bearing >= 0 ? 1 : -1;
        if (hidden)
        {
            // Over the horizon: a point far along the ground towards the target, at the camera's height.
            var walkerUp = Gd(_walker.Up);
            var to = spot - Gd(_walker.Feet);
            to -= walkerUp * to.Dot(walkerUp);
            if (to.LengthSquared() > 1e-6f)
            {
                var pin = cam + to.Normalized() * HorizonPinMetres;
                if (!_camera.IsPositionBehind(pin))
                {
                    var p = _camera.UnprojectPosition(pin);
                    // From a camera that looks well down (the arrival framing, PLANET-42, or the Hazard World view) the
                    // horizon is above the area left free by the lesson card: the chevron then waits at the top of that
                    // area, in the target's direction, rather than turning into a side arrow while you face it (2026-11-17).
                    if (p.X >= inner.Position.X && p.X <= inner.End.X && p.Y < inner.Position.Y) p.Y = inner.Position.Y;
                    if (inner.HasPoint(p)) return new TutorialCueState(TutorialCueMode.Horizon, p, 90, distance, bearing, phase, t.Action, true);
                }
            }
        }

        // Out of view: an arrow at the side to turn towards, level with the horizon straight ahead (kept inside the area).
        var level = _camera.UnprojectPosition(cam - _camera.GlobalBasis.Z * HorizonPinMetres).Y;
        var y = Mathf.Clamp(float.IsFinite(level) ? level : view.Y * 0.5f, inner.Position.Y + 50f, inner.End.Y - 70f);
        var x = _edgeSide > 0 ? inner.End.X : inner.Position.X;
        return new TutorialCueState(TutorialCueMode.Edge, new Vector2(x, y), _edgeSide > 0 ? 0 : 180, distance, bearing, phase, t.Action, hidden);
    }

    /// <summary>Surface distance (metres) from where you stand to a direction on this planet, or +∞ when there is no planet.</summary>
    public float DistanceTo(NVector3 surfaceDirection) =>
        _current == null ? float.PositiveInfinity : PlanetShape.Angle(_walker.Up, surfaceDirection) * _current.Comp.Shape.Radius;

    /// <summary>The arrival title's normal top, viewport units, and the gap it keeps under the lesson card.</summary>
    private const float TitleTop = 150f, TitleUnderCardGap = 14f;

    /// <summary>
    /// While a lesson card shows at the top centre, the arrival title moves down under it (its top
    /// <see cref="TitleUnderCardGap"/> below the card's bottom, <paramref name="cardBottom"/> in viewport units; 0 = no
    /// card) so neither covers the other, whatever the card's height (a hint line or a wrapped instruction adds to it).
    /// </summary>
    public void MakeRoomForLesson(float cardBottom)
    {
        _titleBox.OffsetTop = cardBottom > 0 ? MathF.Max(TitleTop, cardBottom + TitleUnderCardGap) : TitleTop;
        _titleBox.OffsetBottom = _titleBox.OffsetTop + 90;
    }

    /// <summary>A little celebration where you stand when a lesson step is done: the inspect chime and a burst of light (lime, the walkthrough's colour).</summary>
    public void CelebrateLessonStep()
    {
        _audio.Inspect();
        _landFx.Burst(Gd(_walker.Feet), Gd(_walker.Up), Planets.TutorialCue.CueColor);
    }

    /// <summary>The overview's orbit angle, radians (the walkthrough checks that you looked round it).</summary>
    public float UniverseYaw => _uniYaw;
}
