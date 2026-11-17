using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// A Hazard World's behaviour (PLANET-34, PLANET-46; AD-53, AD-59): the caution cue on arrival, the edge warning as you
/// near a broken edge (with rock crumbling off it), the fall, and hops that keep to the route. Core decides where there is no ground (<see cref="BrokenShell"/>); once the walker has dropped
/// <see cref="PlanetSurface.FallThroughMetres"/> below the rim the fall is committed and this part takes over.
/// <para>
/// <b>The fall</b> (<see cref="Phase.Falling"/>, so the world is <see cref="Busy"/>): ≈ 2.6 s along a path that starts with
/// the momentum you stepped off with, drops towards the centre, flattens into the black hole's disc and winds round it
/// faster and faster (<see cref="FallPoint"/>, in the black hole's own frame: its spin axis is <see cref="BrokenShell.JetAxis"/>); the camera follows you down through the hole, speed lines and a violet
/// flash build, the traveller tumbles and shrinks. At the flash's peak the world asks the session to go up
/// (<c>TravelUp</c>: ordinary navigation, so history, selection and listing behave as for U or Backspace), and the
/// navigator's change arrives as a warp descent out of the light onto the parent, beside this world's launcher
/// (<see cref="ArrivalFrom"/>), ready to step on again. A navigation change made during the fall (Backspace, the list) is
/// taken as the fall's destination instead. Nothing here reads or writes a file.
/// </para>
/// Frames: the fall's path is in the hazard world's frame (its centre is the origin, as everything is while you are on it);
/// the return builds the parent at the origin under the flash, as a warp does.
/// </summary>
public partial class PlanetWorld
{
    // ---- the fall (by eye, 2026-11-15)
    /// <summary>The fall lasts this long from stepping off to the flash's peak, seconds (the brief: a short sequence, ≈ 2–3 s).</summary>
    private const float FallSeconds = 2.6f;

    /// <summary>Turns round the black hole over the fall (the spiral's sweep): two and a quarter.</summary>
    private const float FallTurns = 2.25f;

    /// <summary>The fall ends this many core radii from the centre (just outside the photon ring, inside the flash).</summary>
    private const float FallEndCoreRadii = 1.8f;

    /// <summary>The stepping-off momentum fades with this rate, per second (its time constant ≈ 0.4 s), and is gone by half way.</summary>
    private const float FallMomentumRatePerSecond = 2.5f;

    /// <summary>The flash builds from this fraction of the fall to full at its end; the return happens at full.</summary>
    private const float FallFlashFrom = 0.72f;

    /// <summary>The camera rides this far behind and above the traveller along the fall, metres, and eases towards that at this rate per second.</summary>
    private const float FallCamBackMetres = 4.2f, FallCamUpMetres = 1.6f, FallCamFollowRatePerSecond = 5f, FallLookFollowRatePerSecond = 8f;

    /// <summary>The field of view widens to this by the end of the fall, degrees (speed).</summary>
    private const float FallFovDegrees = 86f;

    /// <summary>If the session has not answered the fall's TravelUp in this long, seconds, you are set back on this world's arrival instead.</summary>
    private const float FallReturnTimeoutSeconds = 6f;

    /// <summary>The violet flash's fade after the return (level per second: gone in ≈ 0.7 s, under the warp's own descent).</summary>
    private const float FallFlashFadePerSecond = 1.4f;

    // ---- the edge warning
    /// <summary>The warning starts this far from an edge, metres, and is full this close (the walker's centre past it is a fall).</summary>
    private const float EdgeWarnFromMetres = 4.5f, EdgeWarnFullMetres = 0.4f;

    /// <summary>How fast the warning level follows the distance, per second (≈ 0.12 s).</summary>
    private const float EdgeWarnRatePerSecond = 8f;

    /// <summary>
    /// At an edge (warning 1) the chase camera rises by this pitch, degrees, pulls back this far and aims this far ahead,
    /// metres (<see cref="UpdateCamera"/>): from the usual 18° behind you cannot see over the rim of a small round world
    /// into the hole (2026-11-15, first screenshots). Scaled by the warning level, so it eases in as you approach.
    /// </summary>
    private const float EdgePeekPitchDegrees = 26f, EdgePeekBackMetres = 2.5f, EdgePeekAheadMetres = 3f;

    /// <summary>
    /// On a Hazard World the walking camera rides this much higher (pitch, degrees) and further back (metres), and the
    /// total pitch with the edge peek stops at the max: the islands, the bridges' edges and the open sky between them read
    /// at walking pace, and the opening ahead of the arrival with the black hole's jet is in view as you land (AD-59; by
    /// eye, 2026-11-16: at the usual 18° the openings beyond 6 m sat in a thin band on the horizon).
    /// </summary>
    private const float HazardViewPitchDegrees = 16f, HazardViewBackMetres = 2.2f, HazardViewMaxPitchDegrees = 62f;

    /// <summary>The hazard view eases in and out at this rate per second (≈ 0.5 s), so leaving or entering by a launch never jumps.</summary>
    private const float HazardViewRatePerSecond = 2f;

    private float _hazardView;

    /// <summary>How much of the hazard view applies now (0 off a Hazard World … 1 on one), eased over <paramref name="dt"/>; <paramref name="snap"/> jumps straight to it.</summary>
    private float HazardViewAmount(float dt, bool snap = false)
    {
        var want = Shell != null ? 1f : 0f;
        _hazardView = snap ? want : Mathf.MoveToward(_hazardView, want, dt * HazardViewRatePerSecond);
        return _hazardView;
    }

    /// <summary>
    /// Landing at a Hazard World's arrival, the chase camera turns towards the opening ahead and the black hole's jet for
    /// this long, seconds, at this rate per second (≈ 0.3 s time constant), unless you look round yourself (AD-59: the view
    /// after a flight faces wherever the flight came from, so the opening was often out of shot).
    /// </summary>
    private const float ArrivalLookSeconds = 1.8f, ArrivalLookRatePerSecond = 3.5f;

    private float _arrivalLook;

    /// <summary>The camera's heading <paramref name="f"/> turned towards the jet's opening while the arrival look lasts (walking frames only).</summary>
    private Godot.Vector3 HazardArrivalLook(Godot.Vector3 f, Godot.Vector3 up, float dt)
    {
        if (_arrivalLook <= 0 || Shell is not { } shell || _phase != Phase.Walking || _transfer != null) return f;
        if (_orbiting || _keyLook != 0) { _arrivalLook = 0; return f; } // the player looks round: theirs from now on
        _arrivalLook -= dt;
        var exit = Gd(shell.JetAxis);
        var want = exit - up * exit.Dot(up);
        if (want.LengthSquared() < 1e-6f) return f;
        var ang = f.SignedAngleTo(want.Normalized(), up);
        return f.Rotated(up, ang * (1 - MathF.Exp(-ArrivalLookRatePerSecond * dt))).Normalized();
    }

    /// <summary>The edge hint is shown once per visit, when the warning first passes this level.</summary>
    private const float EdgeHintLevel = 0.6f;

    // ---- the caution cue
    /// <summary>The caution banner shows this long on arrival, seconds, fading over the last <see cref="CautionFadeSeconds"/>.</summary>
    private const float CautionSeconds = 6.5f, CautionFadeSeconds = 1.2f;

    private static readonly Color CautionColour = new(1f, 0.72f, 0.3f);
    private static readonly Color FallFlashColour = new(0.93f, 0.84f, 1f);

    /// <summary>A fall in progress: its start (planet frame) and the spiral's parameters, all fixed at the moment of stepping off.</summary>
    private sealed class FallState
    {
        public int FolderId;
        public float T;

        /// <summary>The start: distance from the centre (metres), elevation above the disc plane and azimuth in it (radians).</summary>
        public float D0, Elev0, Phi0;

        /// <summary>The walker's velocity at stepping off (tangential plus vertical), metres per second.</summary>
        public Vector3 V0;

        /// <summary>Where the spiral ends, metres from the centre.</summary>
        public float DEnd;

        /// <summary>The black hole's frame (planet frame columns; Y its spin axis): the spiral is computed in it.</summary>
        public Basis Frame = Basis.Identity;

        /// <summary>Set once the session has been asked to go up.</summary>
        public bool Asked;
        public float AskedFor;
    }

    private FallState? _fall;

    /// <summary>The fall has handed over to the return (a warp onto the parent): when it lands, the return is complete.</summary>
    private bool _fallReturning;
    private int _fallReturnFrom = -1;
    private float _edgeNearness, _fallFlash, _cautionTime, _fallLines;
    private bool _edgeHintShown;

    private Control? _hazardLayer;
    private ColorRect _hazardScreen = null!, _fallFlashRect = null!;
    private ShaderMaterial _hazardScreenMaterial = null!;
    private Label _caution = null!, _cautionDetail = null!;

    // ---- for probes
    /// <summary>A fall is playing (the spiral; not yet the return).</summary>
    public bool Falling => _fall != null && _phase == Phase.Falling;

    /// <summary>The fall's progress 0..1, or −1 when not falling.</summary>
    public float FallProgress => Falling ? Mathf.Clamp(_fall!.T / FallSeconds, 0, 1) : -1;

    public int FallsStarted { get; private set; }
    public int FallReturns { get; private set; }

    /// <summary>The nearest the last fall came to the centre, metres, and how far round it turned, in turns (probes: it is a spiral).</summary>
    public float LastFallClosestMetres { get; private set; }
    public float LastFallTurns { get; private set; }

    /// <summary>The edge warning level 0..1 (smoothed), and the raw distance to the nearest edge, metres (+∞ on a whole world).</summary>
    public float EdgeNearness => _edgeNearness;
    public float EdgeMetresHere => _current?.Comp.Surface.Shell?.EdgeMetres(_walker.Up) ?? float.PositiveInfinity;

    /// <summary>The caution banner is showing (and its text).</summary>
    public bool CautionShown => _cautionTime > 0;
    public string CautionText => _hazardLayer == null ? "" : _caution.Text + " · " + _cautionDetail.Text;

    /// <summary>Rock is crumbling off the edge nearest the traveller (probes).</summary>
    public bool CrumbsFalling => _current?.Visual?.CrumbsEmitting ?? false;

    /// <summary>The current world's broken shell, if it is a Hazard World.</summary>
    public BrokenShell? Shell => _current?.Comp.Surface.Shell;

    // ------------------------------------------------------------------ the screen layer

    /// <summary>Builds the hazard screen layer on first use: the warning/speed-line layer, the violet flash and the caution banner.</summary>
    private void EnsureHazardLayer()
    {
        if (_hazardLayer != null) return;
        _hazardLayer = new Control { Name = "Hazard", MouseFilter = Control.MouseFilterEnum.Ignore };
        _hazardLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(_hazardLayer);
        _hazardScreenMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/hazard_screen.gdshader") };
        _hazardScreen = new ColorRect { Material = _hazardScreenMaterial, MouseFilter = Control.MouseFilterEnum.Ignore, Color = Colors.White };
        _hazardScreen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hazardLayer.AddChild(_hazardScreen);
        _fallFlashRect = new ColorRect { Color = FallFlashColour with { A = 0 }, MouseFilter = Control.MouseFilterEnum.Ignore };
        _fallFlashRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hazardLayer.AddChild(_fallFlashRect);
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        box.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        box.OffsetLeft = -460; box.OffsetRight = 460; box.OffsetTop = 252; box.OffsetBottom = 336; // just under the title card
        _caution = MakeLabel(19, CautionColour, HorizontalAlignment.Center, outline: 8);
        _cautionDetail = MakeLabel(15, new Color(1f, 0.9f, 0.78f), HorizontalAlignment.Center, outline: 5);
        var route = MakeLabel(15, new Color(1f, 0.9f, 0.78f), HorizontalAlignment.Center, outline: 5);
        route.Text = "Islands of rock, narrow bridges, a gap or two to jump: follow the lit paths. J lists everything, U goes up.";
        box.AddChild(_caution);
        box.AddChild(_cautionDetail);
        box.AddChild(route);
        _hazardLayer.AddChild(box);
        box.Modulate = new Color(1, 1, 1, 0);
    }

    /// <summary>
    /// Every frame (from <see cref="Tick"/>, in any phase): fades the caution banner, the flash and the speed lines, draws
    /// the warning vignette, and notices the end of a fall's return (the warp has landed on the parent).
    /// </summary>
    private void UpdateHazardScreen(float dt)
    {
        if (_fallReturning && _phase == Phase.Walking && _transfer == null)
        {
            _fallReturning = false;
            _fall = null;
            FallReturns++;
            _audio.FallReturn();
            Mark("fall-landed");
            if (_s.Tree is { } tree && tree.Contains(_fallReturnFrom) && _current != null)
            {
                ShowHint($"Back in {tree.DisplayName(_current.FolderId)}, beside {tree.DisplayName(_fallReturnFrom)}'s launcher: step on and press Space to try again.");
            }
        }

        if (_hazardLayer == null) return;
        _cautionTime = MathF.Max(0, _cautionTime - dt);
        ((Control)_caution.GetParent()).Modulate = new Color(1, 1, 1, Mathf.Clamp(_cautionTime / CautionFadeSeconds, 0, 1));
        if (_phase != Phase.Falling)
        {
            _fallFlash = Mathf.MoveToward(_fallFlash, 0, dt * FallFlashFadePerSecond);
            _fallLines = Mathf.MoveToward(_fallLines, 0, dt * 2f);
        }

        var warn = _phase == Phase.Walking && Shell != null ? _edgeNearness : 0f;
        _hazardScreenMaterial.SetShaderParameter("vignette", warn * warn);
        _hazardScreenMaterial.SetShaderParameter("lines", _fallLines);
        _hazardScreen.Visible = warn > 0.001f || _fallLines > 0.001f;
        _fallFlashRect.Color = FallFlashColour with { A = Mathf.Clamp(_fallFlash, 0, 1) };
    }

    // ------------------------------------------------------------------ arrival and the edge warning

    /// <summary>
    /// A world was built (from <see cref="BuildCurrent"/>): on arriving at a hazard world, the caution cue (the banner says
    /// what the place is and that the route is safe) and a fresh edge hint; elsewhere the warning is cleared. A recompose
    /// (<paramref name="arriveFrom"/> −2) changes nothing.
    /// </summary>
    private void OnHazardWorldBuilt(int arriveFrom)
    {
        if (arriveFrom == -2 || _current == null) return;
        _edgeHintShown = false;
        _edgeNearness = 0;
        _current.Visual?.SetEdgeWarning(0);
        if (_current.Comp.Hazard is not { } hz) return;
        // Landing at the arrival (not beside a launcher, coming back from a child): the camera turns to the opening ahead.
        _arrivalLook = _current.Comp.LauncherFor(arriveFrom) == null ? ArrivalLookSeconds : 0;
        EnsureHazardLayer();
        _caution.Text = "⚠  Caution · a Hazard World";
        _cautionDetail.Text = char.ToUpperInvariant(hz.Why[0]) + hz.Why[1..];
        _cautionTime = CautionSeconds;
    }

    /// <summary>
    /// Each walking frame on a hazard world: how near the nearest edge is (smoothed), for the sound
    /// (<see cref="SkyAudio.HazardWarning"/>, every frame), the edge lines' glow, the vignette, and a one-time hint.
    /// Leaving a hazard world sends one last 0 so the warning sound stops.
    /// </summary>
    private void UpdateEdgeWarning(float dt)
    {
        if (Shell is not { } shell)
        {
            if (_edgeNearness > 0) { _edgeNearness = 0; _audio.HazardWarning(0); }
            return;
        }

        var e = shell.EdgeMetres(_walker.Up);
        var target = 1 - Mathf.Clamp((e - EdgeWarnFullMetres) / (EdgeWarnFromMetres - EdgeWarnFullMetres), 0, 1);
        _edgeNearness = Mathf.Lerp(_edgeNearness, target, 1 - MathF.Exp(-EdgeWarnRatePerSecond * dt));
        _audio.HazardWarning(_edgeNearness);
        _current!.Visual?.SetEdgeWarning(_edgeNearness, Gd(_walker.Feet));
        if (_edgeNearness > EdgeHintLevel && !_edgeHintShown && _s.Tree is { } tree)
        {
            _edgeHintShown = true;
            var parent = tree[_current.FolderId].ParentId;
            ShowHint(parent >= 0 ? $"The ground ends here. Step off and you fall, round the black hole and back up to {tree.DisplayName(parent)}." : "The ground ends here.");
        }
    }

    // ------------------------------------------------------------------ the fall

    /// <summary>
    /// After the walker's step: if it has fallen through the rim (Core's commitment depth), the fall begins. True when it
    /// did (the walking frame stops there).
    /// </summary>
    private bool BeginFallIfThrough()
    {
        if (_current == null || Shell == null || _walker.Grounded || _walker.Height.Value >= -PlanetSurface.FallThroughMetres) return false;
        // The spiral is computed in the black hole's frame (its disc lies across the jet axis), from where and how fast you
        // stepped off, both turned into that frame.
        var frame = BlackHoleArt.AxisBasis(Shell.JetAxis);
        var p0 = frame.Inverse() * Gd(_walker.Feet);
        var d0 = p0.Length();
        var v0 = frame.Inverse() * (Gd(_walker.Velocity) + Gd(_walker.Up) * _walker.VerticalVelocity.Value);
        var horizontal = new Vector2(p0.X, p0.Z).Length();
        // The azimuth round the disc's axis (+Y); straight below the axis it is undefined, so take the way you were going.
        var phi = horizontal > 0.05f * d0 ? MathF.Atan2(p0.Z, p0.X) : MathF.Atan2(v0.Z, v0.X);
        _fall = new FallState
        {
            FolderId = _current.FolderId,
            D0 = d0,
            Elev0 = MathF.Asin(Mathf.Clamp(p0.Y / d0, -1, 1)),
            Phi0 = phi,
            V0 = v0,
            DEnd = _current.Comp.Shape.Radius * BrokenShell.HorizonRadiusFraction * FallEndCoreRadii,
            Frame = frame,
        };
        _phase = Phase.Falling;
        _hop = null;
        _transfer = null;
        FallsStarted++;
        LastFallClosestMetres = d0;
        LastFallTurns = 0;
        EnsureHazardLayer();
        CloseOverlays();
        _audio.FallStart();
        _audio.HazardWarning(0);
        _edgeNearness = 0;
        _flightCamStarted = false;
        _flightTrail.Begin(Gd(_walker.Feet));
        Mark("fall");
        return true;
    }

    /// <summary>
    /// Where the fall has taken the traveller at progress <paramref name="u"/> (0..1), in the black hole's frame (Y its spin axis; <see cref="FallState.Frame"/> turns it into the planet frame): the distance from the
    /// centre shrinks with u^1.8 (slow, then accelerating), the elevation above the disc flattens out between 5 % and
    /// 80 %, and the azimuth winds on by <see cref="FallTurns"/> × u^2.2 (faster and faster), plus the stepping-off momentum,
    /// fading. At u = 0 it is exactly where you fell from, moving as you were, so the hand-over has no kink.
    /// </summary>
    private static Vector3 FallPoint(FallState f, float u)
    {
        var t = u * FallSeconds;
        var d = Mathf.Lerp(f.D0, f.DEnd, MathF.Pow(u, 1.8f));
        var elev = f.Elev0 * (1 - Smooth(Mathf.Clamp((u - 0.05f) / 0.75f, 0, 1)));
        var phi = f.Phi0 + Mathf.Tau * FallTurns * MathF.Pow(u, 2.2f);
        var p = new Vector3(MathF.Cos(elev) * MathF.Cos(phi), MathF.Sin(elev), MathF.Cos(elev) * MathF.Sin(phi)) * d;
        var drift = (1 - MathF.Exp(-FallMomentumRatePerSecond * t)) / FallMomentumRatePerSecond; // ∫ e^(−k t) dt: the momentum's distance so far, seconds × (m/s)
        return p + f.V0 * drift * (1 - Smooth(Mathf.Clamp(u / 0.5f, 0, 1)));
    }

    /// <summary>One frame of the fall (Phase.Falling): the path, the traveller, the trail, the sound, the camera, the screen; the return at the end.</summary>
    private void UpdateFall(float dt)
    {
        var f = _fall;
        if (f == null || _current == null || _current.FolderId != f.FolderId)
        {
            _phase = Phase.Walking;
            _fall = null;
            return;
        }

        f.T += dt;
        if (f.Asked)
        {
            // Waiting for the session's answer under the full flash (it normally answers at once, as a warp).
            f.AskedFor += dt;
            _fallFlash = 1;
            if (f.AskedFor > FallReturnTimeoutSeconds) SetDownAfterFall("The way back did not answer; you are back on solid ground.");
            return;
        }

        var u = Mathf.Clamp(f.T / FallSeconds, 0, 1);
        var pos = f.Frame * FallPoint(f, u);
        var ahead = f.Frame * FallPoint(f, MathF.Min(1, u + 0.01f)) - pos;
        var fwd = ahead.LengthSquared() > 1e-8f ? ahead.Normalized() : -pos.Normalized();
        var outward = pos.LengthSquared() > 1e-6f ? pos.Normalized() : Vector3.Up;
        LastFallClosestMetres = MathF.Min(LastFallClosestMetres, pos.Length());
        LastFallTurns = FallTurns * MathF.Pow(u, 2.2f);

        // The traveller: tumbling about its direction of travel, faster as it goes, and stretched thin at the end.
        var side = fwd.Cross(outward);
        var bodyUp = side.LengthSquared() > 1e-6f ? fwd.Cross(side.Normalized()).Normalized() : outward;
        bodyUp = bodyUp.Rotated(fwd, Mathf.Tau * 1.5f * u * u);
        var scale = 1 - 0.45f * Smooth(Mathf.Clamp((u - 0.7f) / 0.3f, 0, 1));
        _avatar.SetFlying(pos, bodyUp, fwd, u, scale, dt);
        _flightTrail.Add(pos, scale);
        _audio.Fall(u);

        // The camera: behind and above along the path, looking past the traveller towards the black hole; its up turns from
        // the ground's up to the disc's axis, so by the end you look across the spinning disc.
        var camUpWant = outward.Slerp(f.Frame.Y.Normalized(), Smooth(u)).Normalized();
        var desired = pos - fwd * FallCamBackMetres + camUpWant * FallCamUpMetres;
        _camPos = _camPos.Lerp(desired, 1 - MathF.Exp(-FallCamFollowRatePerSecond * dt));
        var look = pos.Lerp(Vector3.Zero, 0.25f + 0.35f * u);
        _camLook = _camLook.Lerp(look, 1 - MathF.Exp(-FallLookFollowRatePerSecond * dt));
        _camUp = _camUp.Slerp(camUpWant, 1 - MathF.Exp(-4f * dt)).Normalized();
        var lookDir = KeepInFrame(_camPos, _camLook - _camPos, pos, KeepInFrameFlightDegrees);
        if (MathF.Abs(lookDir.Normalized().Dot(_camUp)) > 0.995f) _camUp = side.LengthSquared() > 1e-6f ? side.Normalized() : _camUp;
        _camera.LookAtFromPosition(_camPos, _camPos + lookDir, _camUp);
        _camera.Fov = Mathf.Lerp(WalkingFovDegrees, FallFovDegrees, Smooth(u));

        _fallLines = Smooth(Mathf.Clamp((u - 0.1f) / 0.5f, 0, 1));
        _fallFlash = Smooth(Mathf.Clamp((u - FallFlashFrom) / (1 - FallFlashFrom), 0, 1));
        if (u >= 1) AskToGoUp(f);
    }

    /// <summary>
    /// The fall's end, at the flash's peak: ask the session to go up (ordinary navigation). Its answer comes back through
    /// <see cref="OnNavChanged"/>, which (see <see cref="OnNavChangedWhileFalling"/>) makes it a descent out of the light.
    /// At the top of the tree there is nowhere to go up to, so you are set down on this world's arrival instead.
    /// </summary>
    private void AskToGoUp(FallState f)
    {
        f.Asked = true;
        _fallFlash = 1;
        Mark("fall-up");
        if (_s.Tree is not { } tree || tree[f.FolderId].ParentId < 0 || !_s.TravelUp())
        {
            SetDownAfterFall(null);
        }
    }

    /// <summary>
    /// Called by <see cref="OnNavChanged"/> when the navigator changes during a fall (the fall's own TravelUp, or a key such
    /// as Backspace pressed while falling): the change becomes a warp that starts at the top of its rise, so the new world
    /// is built under the flash and you come down onto it beside the launcher of the world you fell from.
    /// </summary>
    private void OnNavChangedWhileFalling()
    {
        if (_fall == null) return;
        _fallReturning = true;
        _fallReturnFrom = _fall.FolderId;
        _fallFlash = 1;
        _fallLines = 0;
        _flightTrail.End();
        _arriveFromSky = true; // OnNavChanged's warp branch: built at once, a descent out of the light
        _warpFlash = 1f;
        _camera.Fov = WalkingFovDegrees;
        Mark("fall-return");
    }

    /// <summary>The fall could not go up: stand on this world's arrival meadow again, under a fading flash.</summary>
    private void SetDownAfterFall(string? why)
    {
        if (_current == null) return;
        _fall = null;
        _fallReturning = false;
        _phase = Phase.Walking;
        _flightTrail.End();
        _walker.Teleport(_current.Comp.Surface, _current.Comp.Arrival.Up, _current.Comp.Arrival.Facing);
        _avatar.Scale = Vector3.One;
        _camera.Fov = WalkingFovDegrees;
        SnapCamera();
        _audio.FallReturn();
        FallReturns++;
        if (why != null) _s.Toast(why);
        Mark("fall-set-down");
    }

    /// <summary>Drops any fall and warning state (the world is hidden, or a new tree arrives).</summary>
    private void EndHazard()
    {
        _fall = null;
        _fallReturning = false;
        _fallFlash = 0;
        _fallLines = 0;
        _cautionTime = 0;
        if (_edgeNearness > 0) _audio.HazardWarning(0);
        _edgeNearness = 0;
        if (_hazardLayer != null)
        {
            _fallFlashRect.Color = FallFlashColour with { A = 0 };
            _hazardScreen.Visible = false;
            ((Control)_caution.GetParent()).Modulate = new Color(1, 1, 1, 0);
        }
    }

    // ------------------------------------------------------------------ hops on a Hazard World

    /// <summary>
    /// The way a hop should go from <paramref name="from"/> to <paramref name="to"/> on a Hazard World: null for the
    /// straight arc (when it keeps over solid ground), or the route's waypoints after <paramref name="from"/>, ending at
    /// <paramref name="to"/> (<see cref="BrokenShell.Route"/>: along the bridges' centre lines). Approach, Home and the
    /// list's hops use it, so keyboard and list navigation keep to the route; a hop is airborne, so a gap on the way is
    /// simply hopped over.
    /// </summary>
    private NVector3[]? HopRoute(NVector3 from, NVector3 to)
    {
        if (Shell is not { } shell || _current == null) return null;
        const float clearanceMetres = 0.6f; // a body and a little
        if (shell.ArcClear(from, to, clearanceMetres)) return null;
        var route = shell.Route(from, to).Select(s => s.Point).ToArray();
        return route.Length > 1 ? route : null;
    }
}
