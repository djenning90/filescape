using FileScape.App.Places;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// Walking on a planet and the camera that follows (PLANET-11, AD-39). Movement is camera-relative (W away from the
/// camera, A/D to the sides), the traveller turns to face where it runs, and the chase camera follows by itself: it
/// swings in behind while you run, keeps its own up close to the local up with a capped roll rate (research E4: no
/// sudden, unexpected camera motion), never goes under the ground, and rises over the planet on the overlook. A left- or
/// right-drag turns it (AD-47; the mouse wheel sets its distance, ← → turn it too); nothing else is required to play.
/// <para>
/// The camera keeps its own state (<c>_camPos</c>, <c>_camLook</c>, <c>_camUp</c>, <c>_camFwd</c>) and eases it towards a
/// desired pose each frame, so every change of target (a wind-up, a landing, a hop's end) is a glide, never a cut.
/// That state is in the current planet's frame: a landing re-expresses it in the new frame (<c>Land</c>), and anything
/// added to it must be too. Smoothing uses 1 − e^(−rate·dt) (frame-rate independent; see the overview camera's
/// constants and docs/units.md): a higher rate feels tighter, a lower one floatier; 1/rate is the time to close 63 %.
/// </para>
/// </summary>
public partial class PlanetWorld
{
    /// <summary>The camera's up turns towards the local up at most this fast (research E4: 90°/s cap, eased).</summary>
    public const float CameraRollCapDegreesPerSecond = 90f;

    // ---- the chase camera (tuning by eye, 2026-11-10/11; rationale not recorded unless stated)

    /// <summary>The walking camera's vertical field of view, degrees (the overview uses its own, <see cref="OverviewFovDegrees"/>).</summary>
    private const float WalkingFovDegrees = 62f;

    /// <summary>How fast the field of view returns to <see cref="WalkingFovDegrees"/> after a flight widened it, per second (≈ 0.25 s).</summary>
    private const float FovRecoverRatePerSecond = 4f;

    /// <summary>The camera's starting pitch above the traveller, degrees, and its starting distance, metres (the wheel changes the distance).</summary>
    private const float StartCamPitchDegrees = 18f, StartCamDistanceMetres = 7.2f;

    /// <summary>On the overlook the camera rises to this pitch, degrees, and pulls back to this distance, metres, to look out over the planet.</summary>
    private const float OverlookPitchDegrees = 34f, OverlookDistanceMetres = 15f;

    /// <summary>How fast the overlook view comes and goes, per second of blend (0 → 1 in ≈ 0.6 s; linear, not eased).</summary>
    private const float OverlookBlendPerSecond = 1.6f;

    /// <summary>The point the camera aims at: this high above the feet, metres (about the traveller's head) and this far ahead.</summary>
    private const float LookHeightMetres = 1.15f, LookAheadMetres = 0.5f;

    /// <summary>The eye never comes closer than this to the ground below it, metres.</summary>
    private const float MinEyeClearanceMetres = 0.9f;

    /// <summary>How fast the camera's position follows its desired place, per second (≈ 0.13 s).</summary>
    private const float CamFollowRatePerSecond = 7.5f;

    /// <summary>How fast the point looked at follows, per second (≈ 0.08 s): quicker than the position, so the view
    /// turns to a new subject before the camera has moved.</summary>
    private const float LookFollowRatePerSecond = 12f;

    /// <summary>How fast the camera's up eases towards the local up, per second (before the <see cref="CameraRollCapDegreesPerSecond"/> cap).</summary>
    private const float RollEaseRatePerSecond = 6f;

    /// <summary>After a drag or a key turn the camera is left where you put it this long, seconds, before it swings in behind you again.</summary>
    private const float SwingInDelaySeconds = 1.2f;

    /// <summary>How fast it swings in behind a running traveller, per second (≈ 0.7 s: a slow drift, not a snap).</summary>
    private const float SwingInRatePerSecond = 1.4f;

    /// <summary>It only swings in when the traveller runs within this angle of away from the camera, degrees; running
    /// towards the camera does not spin the view round.</summary>
    private const float SwingInMaxDegrees = 135f;

    /// <summary>The traveller's middle, metres above the feet (it is ≈ 1.1 m tall): kept within <see cref="KeepInFrameWalkingDegrees"/> of the view's centre.</summary>
    private const float TravellerMiddleMetres = 0.6f;

    /// <summary>Walking: the traveller never strays further than this from the centre of the view, degrees.</summary>
    private const float KeepInFrameWalkingDegrees = 18f;

    /// <summary>Wind-up: the camera pulls back and up by at most these, metres (growing with the wind-up), and looks this
    /// far past the traveller towards the destination.</summary>
    private const float WindUpPullBackMetres = 1.5f, WindUpRiseMetres = 0.6f, WindUpLookPastMetres = 5f;

    // ---- the flight camera
    /// <summary>In flight the camera rides this far behind and above the traveller, metres at the traveller's scale.</summary>
    private const float FlightCamBackMetres = 9f, FlightCamUpMetres = 3.2f;

    /// <summary>How fast the flight camera's offset follows, per second (≈ 0.29 s): loose enough that turns feel like flight.</summary>
    private const float FlightCamFollowRatePerSecond = 3.5f;

    /// <summary>In flight it looks this far ahead along the direction of travel, metres at the traveller's scale.</summary>
    private const float FlightLookAheadMetres = 6f;

    /// <summary>How fast the flight camera's look point and its up follow, per second.</summary>
    private const float FlightLookFollowRatePerSecond = 4f, FlightUpFollowRatePerSecond = 5f;

    /// <summary>Flying: the traveller never strays further than this from the centre of the view, degrees.</summary>
    private const float KeepInFrameFlightDegrees = 16f;

    /// <summary>The field of view widens by up to this mid-flight, degrees (a sense of speed), and narrows again for the landing.</summary>
    private const float FlightFovWidenDegrees = 12f;

    // ---- the camera's own state, in the current planet's frame
    private Godot.Vector3 _camPos, _camUp = Godot.Vector3.Up, _camFwd = Godot.Vector3.Forward, _camLook;
    private float _camPitchDegrees = StartCamPitchDegrees, _camDistance = StartCamDistanceMetres;

    /// <summary>Seconds since you last turned the camera yourself (starts "long ago", so the camera may swing in at once).</summary>
    private float _sinceOrbitSeconds = 99f;

    /// <summary>0 = the chase view, 1 = the overlook view (blended between).</summary>
    private float _overlook01;
    private bool _jumpQueued;

    /// <summary>How long a landing's blend from the flight camera to the walking one takes, seconds (research E5: the
    /// camera reaches its walking pose within 0.6 s of touchdown).</summary>
    private const float LandingBlendSeconds = 0.6f;

    /// <summary>A blend into the walking camera in progress: seconds left, its whole length, and the pose it started from.</summary>
    private float _camBlend, _camBlendTotal = LandingBlendSeconds;
    private Transform3D _camBlendFrom;

    /// <summary>
    /// The opening (research: introduce a destination by showing it): the camera starts high over the arrival meadow
    /// with the world and its neighbours in view, then glides down behind the traveller. You can move at once; any key
    /// or the mouse shortens it.
    /// <para>
    /// It works by setting the blend's start pose: <see cref="UpdateCamera"/> then glides from it into the walking camera
    /// over <paramref name="seconds"/> (the same mechanism as a landing's blend).
    /// </para>
    /// </summary>
    public void PlayEstablishingShot(float seconds = 2.6f)
    {
        if (_current == null) return;
        var up = Gd(_walker.Up);
        var f = Gd(_walker.Facing);
        var r = _current.Comp.Shape.Radius;
        // In planet radii plus a margin, so a small world and a large one are framed alike: 1.5 r + 10 m up and
        // 1.1 r + 8 m back, looking at a point half a radius ahead (the meadow and the sky beyond it). Chosen by eye.
        var eye = Gd(_walker.Feet) + up * (r * 1.5f + 10f) - f * (r * 1.1f + 8f);
        var look = Gd(_walker.Feet) + f * (r * 0.5f);
        _camBlendFrom = new Transform3D(Basis.LookingAt((look - eye).Normalized(), up), eye);
        _camera.GlobalTransform = _camBlendFrom;
        _camBlend = _camBlendTotal = seconds;
        EstablishingShots++;
    }

    public int EstablishingShots { get; private set; }

    /// <summary>A hurried opening finishes in this long, seconds.</summary>
    private const float HurriedBlendSeconds = 0.45f;

    /// <summary>
    /// A key or a click during the opening: finish the glide quickly (never cut). Only a long blend (over 1 s: the
    /// establishing shot, not a landing's 0.6 s) is hurried, and only with more than <see cref="HurriedBlendSeconds"/>
    /// left. The progress reached so far is kept: the total is rescaled so the remaining (1 − k) of the glide takes
    /// <see cref="HurriedBlendSeconds"/>, so the camera does not jump back or forward along it.
    /// </summary>
    private void HurryBlend()
    {
        if (_camBlend > HurriedBlendSeconds && _camBlendTotal > 1f)
        {
            var k = 1 - _camBlend / _camBlendTotal;
            _camBlendTotal = HurriedBlendSeconds / MathF.Max(0.05f, 1 - k); // 0.05: never divide by (nearly) zero
            _camBlend = HurriedBlendSeconds;
        }
    }
    private Godot.Vector3 _avatarUp = Godot.Vector3.Up;
    private float _maxRollRateDegreesPerSecond;
    private int _lastFootsteps;

    /// <summary>The fastest the camera's up has turned this session, degrees per second (probes check the E4 cap).</summary>
    public float MaxCameraRollRateDegreesPerSecond => _maxRollRateDegreesPerSecond;

    /// <summary>The overlook view is more than half blended in (the names of every destination then show).</summary>
    public bool OnOverlook => _overlook01 > 0.5f;

    /// <summary>The overlook's camera on request (B): rise and look out over the planet, or come back down.</summary>
    private bool _overlookRequested;

    public void ToggleOverlookView() => _overlookRequested = !_overlookRequested;

    /// <summary>
    /// One walking frame: read the keys (unless they go elsewhere), launch from a pad or move the walker, turn its events
    /// into sound and animation, then the camera and the focus. A hop in progress takes over instead.
    /// </summary>
    private void UpdateWalking(float dt, bool blockedKeys)
    {
        if (_current == null) return;
        if (_hop != null)
        {
            _overlook01 = Mathf.MoveToward(_overlook01, 0, dt * OverlookBlendPerSecond);
            UpdateHop(dt);
            return;
        }

        var fwd = 0f;
        var strafe = 0f;
        var run = false;
        var jumpHeld = false;
        if (!blockedKeys)
        {
            fwd = Axis(InputBindings.Back, InputBindings.Forward);
            strafe = Axis(InputBindings.Left, InputBindings.Right);
            KeyboardLook(dt, Axis(InputBindings.TurnLeft, InputBindings.TurnRight));
            run = Godot.Input.IsActionPressed(InputBindings.Fast);
            jumpHeld = Godot.Input.IsActionPressed(InputBindings.Jump);
        }

        var jump = _jumpQueued && !blockedKeys;
        _jumpQueued = false;
        var up = Gd(_walker.Up);
        // Camera-relative: forward is where the camera looks, flattened onto the ground here.
        var camF = (-_camera.GlobalBasis.Z);
        camF = (camF - up * camF.Dot(up));
        if (camF.LengthSquared() < 1e-4f) camF = _camFwd;
        camF = camF.Normalized();
        var camR = camF.Cross(up).Normalized();
        var wish = camF * fwd + camR * strafe;

        // Space on a launcher pad launches (the pad is the button); elsewhere it jumps, and again in the air it twirls higher.
        if (jump && _walker.Grounded && FocusLauncher() is { } pad)
        {
            BeginLaunch(pad.TargetId, pad.Direction, fromPad: true);
            return;
        }

        if (jump) CloseOverlaysOnMove();
        if (wish.LengthSquared() > 0.01f) CloseOverlaysOnMove();
        _walker.Update(new SphereWalkInput(Nv(wish), run, jump, jumpHeld), Core.Units.Durations.FromSeconds(dt));
        var ev = _walker.LastEvent;
        switch (ev.Kind)
        {
            case Core.Movement.JumpEventKind.Jump:
                _audio.Jump();
                _avatar.Jump();
                break;
            case Core.Movement.JumpEventKind.AirJump:
                _audio.AirJump();
                _avatar.Twirl();
                break;
            case Core.Movement.JumpEventKind.Land:
                _audio.Land(ev.ImpactSpeed.Value);
                _avatar.Land(ev.ImpactSpeed.Value);
                break;
        }

        if (BeginFallIfThrough()) return; // through a broken world's rim: the fall takes over (PlanetWorld.Hazard)
        UpdateBench(dt, ev.Kind);
        if (_walker.Footsteps != _lastFootsteps)
        {
            _lastFootsteps = _walker.Footsteps;
            _audio.Step(Mathf.Clamp(_walker.Velocity.Length() / 11f, 0.2f, 1f)); // loudness: full at 11 m/s and above, never below 0.2
            NoteFootstepNear();
        }

        PoseAvatar(dt);
        // On the overlook's top: standing within 0.3 m of its height and of its edge (a little slack, so its rim counts).
        var onTop = _current.Comp.Overlook is { } ov && _walker.Support.Value > ov.Top - 0.3f && PlanetShape.Angle(_walker.Up, ov.Direction) * _current.Comp.Shape.Radius < ov.Radius + 0.3f;
        // The workbench keeps the chase camera: its cards rise round you and are read close up (the overlook view made them tiny).
        var wantOverlook = (onTop && !OnBenchWorld) || _overlookRequested;
        if (onTop && !_overlookNoted && !OnBenchWorld)
        {
            _overlookNoted = true;
            _audio.Inspect();
            ShowHint("From the overlook every destination is named. The telescope (E) lists everything here.");
        }

        _overlook01 = Mathf.MoveToward(_overlook01, wantOverlook ? 1 : 0, dt * OverlookBlendPerSecond);
        UpdateCamera(dt, lookAt: null, moving: _walker.Velocity.Length() > 0.8f); // above 0.8 m/s counts as moving (the camera may swing in)
        UpdateFocus();
        UpdateEdgeWarning(dt);
    }

    /// <summary>The overlook's hint has been shown (once per PlanetWorld, i.e. once per run: it is never reset).</summary>
    private bool _overlookNoted;

    /// <summary>The ← → turn, eased: −1 (full left) to 1 (full right).</summary>
    private float _keyLook;

    /// <summary>← → ease in over this long, seconds (AD-47): a tap nudges, a hold sweeps.</summary>
    private const float KeyLookEaseInSeconds = 0.12f;

    /// <summary>Shift turns this much faster (AD-47).</summary>
    private const float KeyLookFastFactor = 1.6f;

    /// <summary>
    /// ← → turn the camera (AD-47, as they turn the view in Places): standing still they look around; with W/S held the
    /// walk curves, because movement follows the camera. Eased in over ≈ 0.12 s so a tap nudges and a hold sweeps.
    /// </summary>
    private void KeyboardLook(float dt, float axis)
    {
        _keyLook = Mathf.MoveToward(_keyLook, axis, dt / KeyLookEaseInSeconds);
        if (MathF.Abs(_keyLook) < 1e-3f) return;
        var up = Gd(_walker.Up);
        var fast = Godot.Input.IsActionPressed(InputBindings.Fast) ? KeyLookFastFactor : 1f;
        _camFwd = _camFwd.Rotated(up, -_keyLook * fast * Mathf.DegToRad(KeyLookDegreesPerSecond) * dt).Normalized();
        _sinceOrbitSeconds = 0;
    }

    /// <summary>A key pair as an axis: −1, 0 or 1 (both held cancel).</summary>
    private static float Axis(string negative, string positive) =>
        (Godot.Input.IsActionPressed(positive) ? 1f : 0f) - (Godot.Input.IsActionPressed(negative) ? 1f : 0f);

    /// <summary>
    /// The traveller's pose from the walker: feet on the ground, up along the local up, facing where it runs.
    /// <paramref name="hover"/> (waiting on a pad for a link to answer) floats it 0.6 m up, bobbing ±0.15 m about
    /// 0.8 times a second (5 rad/s), so a pending launch visibly waits rather than looking stuck.
    /// </summary>
    private void PoseAvatar(float dt, float windUp = 0, bool hover = false)
    {
        var up = Gd(_walker.Up);
        var feet = Gd(_walker.Feet);
        if (hover) feet += up * (0.6f + 0.15f * Mathf.Sin((float)_s.Now.TotalSeconds * 5));
        _avatarUp = up;
        _avatar.Scale = Godot.Vector3.One;
        _avatar.SetPose(feet, up, Gd(_walker.Facing));
        _avatar.Drive(dt, _walker.Velocity.Length(), _walker.Grounded && !hover, _walker.VerticalVelocity.Value, windUp);
    }

    /// <summary>
    /// The chase camera. <paramref name="lookAt"/> turns it towards a world point (the target during a wind-up);
    /// <paramref name="extraBack"/>/<paramref name="extraUp"/> pull it out (wind-up). Its up follows the local up eased and capped.
    /// <para>
    /// Each frame: carry the heading onto the tangent plane here; maybe swing in behind (<paramref name="moving"/>, not
    /// while you are turning it yourself); turn the up (eased, then capped at <see cref="CameraRollCapDegreesPerSecond"/>);
    /// place the desired eye on a pitch-and-distance boom (lifted out of the ground if needed); ease position and look
    /// point; keep the traveller in frame; then either finish a blend (landing, establishing shot) or look directly.
    /// Records the fastest roll seen for the E4 probe.
    /// </para>
    /// </summary>
    private void UpdateCamera(float dt, Godot.Vector3? lookAt, float extraBack = 0, float extraUp = 0, bool moving = false)
    {
        if (_current == null) return;
        var up = Gd(_walker.Up);
        var feet = Gd(_walker.Feet);
        var facing = Gd(_walker.Facing);
        // Carry the camera's heading onto the new tangent plane (parallel transport: no spin at any pole).
        var f = _camFwd - up * _camFwd.Dot(up);
        if (f.LengthSquared() < 1e-4f) f = facing;
        f = f.Normalized();
        _sinceOrbitSeconds += dt;
        if (moving && _sinceOrbitSeconds > SwingInDelaySeconds && !_orbiting)
        {
            // Swing in behind a running traveller (not when it runs towards the camera: that would spin the view).
            var ang = f.SignedAngleTo(facing, up);
            if (MathF.Abs(ang) < Mathf.DegToRad(SwingInMaxDegrees)) f = f.Rotated(up, ang * (1 - MathF.Exp(-SwingInRatePerSecond * dt)));
        }

        _camFwd = f;
        // Up: eased towards the local up and capped (90°/s), so walking round a small world never rolls the view suddenly.
        // The ease alone would turn fast after a big change of up; the cap bounds that, and the ease keeps small changes soft.
        var upAngle = _camUp.AngleTo(up);
        if (upAngle > 1e-5f)
        {
            var step = MathF.Min(upAngle * (1 - MathF.Exp(-RollEaseRatePerSecond * dt)), Mathf.DegToRad(CameraRollCapDegreesPerSecond) * dt);
            var axis = _camUp.Cross(up);
            if (axis.LengthSquared() > 1e-10f) _camUp = _camUp.Rotated(axis.Normalized(), step).Normalized();
            else _camUp = up;
            if (dt > 0) _maxRollRateDegreesPerSecond = MathF.Max(_maxRollRateDegreesPerSecond, Mathf.RadToDeg(step) / dt);
        }

        // Near a broken edge (PlanetWorld.Hazard) the camera rises and looks ahead, so you see down into the hole before
        // you step off it; elsewhere _edgeNearness is 0 and this adds nothing.
        var peek = _edgeNearness;
        var pitch = Mathf.DegToRad(Mathf.Lerp(_camPitchDegrees, OverlookPitchDegrees, _overlook01) + EdgePeekPitchDegrees * peek);
        var dist = Mathf.Lerp(_camDistance, OverlookDistanceMetres, _overlook01) + extraBack + EdgePeekBackMetres * peek;
        // The aim point rises by half the wind-up's lift (the eye rises by all of it), so the view tilts down a little.
        var target = feet + up * (LookHeightMetres + extraUp * 0.5f) + facing * (LookAheadMetres + EdgePeekAheadMetres * peek);
        var desired = target - f * (MathF.Cos(pitch) * dist) + up * (MathF.Sin(pitch) * dist + extraUp);
        // Never under the ground: keep the eye at least 0.9 m above the surface below it (the planet's centre is the origin,
        // so pushing the eye out along its own direction lifts it straight up).
        var dir = desired.Normalized();
        var minR = _current.Comp.Shape.GroundRadius(Nv(dir)) + MinEyeClearanceMetres;
        if (desired.Length() < minR) desired = dir * minR;
        _camPos = _camPos.Lerp(desired, 1 - MathF.Exp(-CamFollowRatePerSecond * dt));
        var look = target;
        if (lookAt is { } la)
        {
            // Wind-up: look past the traveller towards where it is going; it stays low in the frame, the world above it.
            var toward = (la - target).Normalized();
            look = target + toward * WindUpLookPastMetres;
        }
        // The point looked at is smoothed too, so the view never turns in one frame (launch, landing, a hop's end).
        _camLook = _camLook.Lerp(look, 1 - MathF.Exp(-LookFollowRatePerSecond * dt));
        look = _camPos + KeepInFrame(_camPos, _camLook - _camPos, feet + up * TravellerMiddleMetres, KeepInFrameWalkingDegrees);
        if (_camBlend > 0)
        {
            // Just landed: blend from the flight camera to the walking one (no cut, research E5).
            _camBlend = MathF.Max(0, _camBlend - dt);
            var k = Smooth(1 - _camBlend / _camBlendTotal);
            var walking = new Transform3D(Basis.LookingAt((look - _camPos).Normalized(), _camUp), _camPos);
            _camera.GlobalTransform = _camBlendFrom.InterpolateWith(walking, k);
        }
        else
        {
            _camera.LookAtFromPosition(_camPos, look, _camUp);
        }

        _camera.Fov = Mathf.Lerp(_camera.Fov, WalkingFovDegrees, 1 - MathF.Exp(-FovRecoverRatePerSecond * dt));
    }

    /// <summary>
    /// The camera in flight: it travels with the traveller (an offset from it, eased towards behind-and-above along the
    /// direction of travel, scaled with it), so the traveller stays steady on screen however fast it goes, and it turns
    /// to frame the target as it nears.
    /// <para>
    /// The offsets start from wherever the walking camera was (the first frame of a flight or warp), so take-off does not
    /// cut; <paramref name="scale"/> is the traveller's current scale, so the framing shrinks or grows with it on the way
    /// into a smaller or larger world. <paramref name="u"/> is the flight's progress 0..1 (a warp passes its own).
    /// </para>
    /// </summary>
    private void FlightCamera(Godot.Vector3 pos, Godot.Vector3 up, Godot.Vector3 fwd, float scale, float u, Transfer tr, float dt, bool warp = false)
    {
        var side = fwd.Cross(up);
        if (side.LengthSquared() < 1e-4f) side = _camera.GlobalBasis.X;
        if (!_flightCamStarted)
        {
            _flightCamStarted = true;
            _camOffset = _camPos - pos;
            _lookOffset = _camLook - pos;
        }

        // (The warp's 12 m has no effect: a warp replaces the whole offset on the next line.)
        var back = warp ? 12f : FlightCamBackMetres;
        var desired = -fwd * (back * scale) + up * (FlightCamUpMetres * scale);
        // A warp frames its own shot: rising, 6 m below and 9 m behind (you shoot up away from the camera); descending,
        // 9 m above and 6 m out along the landing facing (you come down past it onto the new world). By eye.
        if (warp) desired = tr.Built ? tr.ToUp * 9f + Gd(tr.LandFacing) * 6f : -tr.FromUp * 6f - _camFwd * 9f;
        _camOffset = _camOffset.Lerp(desired, 1 - MathF.Exp(-FlightCamFollowRatePerSecond * dt));
        _camPos = pos + _camOffset;
        var lookWant = fwd * (FlightLookAheadMetres * scale);
        // As the flight goes on, the look point swings up to halfway towards the target's centre, so it comes into view.
        if (tr.Target != null) lookWant = lookWant.Lerp(tr.Target.Node.GlobalPosition - pos, Smooth(u) * 0.5f);
        _lookOffset = _lookOffset.Lerp(lookWant, 1 - MathF.Exp(-FlightLookFollowRatePerSecond * dt));
        _camLook = pos + _lookOffset;
        _camUp = _camUp.Slerp(up, 1 - MathF.Exp(-FlightUpFollowRatePerSecond * dt)).Normalized();
        var lookDir = _camLook - _camPos;
        if (lookDir.LengthSquared() < 1e-6f) lookDir = fwd;
        lookDir = KeepInFrame(_camPos, lookDir, pos + up * (TravellerMiddleMetres * scale), KeepInFrameFlightDegrees);
        // Looking (almost) along the up vector leaves LookAt without a defined roll (within ≈ 5.7°): use the side instead.
        if (MathF.Abs(lookDir.Normalized().Dot(_camUp)) > 0.995f) _camUp = side.Normalized();
        _camera.LookAtFromPosition(_camPos, _camPos + lookDir, _camUp);
        _camera.Fov = WalkingFovDegrees + FlightFovWidenDegrees * Mathf.Sin(Mathf.Clamp(u, 0, 1) * Mathf.Pi); // widest mid-flight
        _camFwd = fwd;
    }

    /// <summary>
    /// Turns a view direction just enough that <paramref name="subject"/> stays within <paramref name="maxDegrees"/> of
    /// the centre (the traveller never leaves the frame, whatever the camera is easing towards).
    /// </summary>
    private static Godot.Vector3 KeepInFrame(Godot.Vector3 camPos, Godot.Vector3 lookDir, Godot.Vector3 subject, float maxDegrees)
    {
        var d = lookDir.Normalized();
        var to = subject - camPos;
        if (to.LengthSquared() < 1e-6f) return d;
        to = to.Normalized();
        var ang = d.AngleTo(to);
        var max = Mathf.DegToRad(maxDegrees);
        if (ang <= max) return d;
        var axis = d.Cross(to);
        if (axis.LengthSquared() < 1e-10f) return d;
        return d.Rotated(axis.Normalized(), ang - max).Normalized();
    }

    /// <summary>The flight camera has taken its offsets from the walking camera (reset at each take-off).</summary>
    private bool _flightCamStarted;

    /// <summary>The flight camera's eye and look point, relative to the traveller (they ride along with it).</summary>
    private Godot.Vector3 _camOffset, _lookOffset;

    /// <summary>Puts the camera straight into its walking pose (arrival, switching worlds).</summary>
    private void SnapCamera()
    {
        if (_current == null) return;
        _camUp = Gd(_walker.Up);
        _camFwd = Gd(_walker.Facing);
        var up = _camUp;
        var feet = Gd(_walker.Feet);
        var pitch = Mathf.DegToRad(_camPitchDegrees);
        var target = feet + up * LookHeightMetres;
        _camPos = target - _camFwd * (MathF.Cos(pitch) * _camDistance) + up * (MathF.Sin(pitch) * _camDistance);
        _camera.LookAtFromPosition(_camPos, target, _camUp);
        _camLook = target;
        _camBlend = 0;
        _overlook01 = 0;
        PoseAvatar(0);
    }

    // ------------------------------------------------------------------ hops: short in-planet arcs (Home, approach)

    /// <summary>A hop in progress: from and to (surface directions), the facing to land with, elapsed and total seconds,
    /// the arc's peak height (metres) and what to inspect on landing (−1: nothing).</summary>
    private sealed class Hop
    {
        public NVector3 FromUp, ToUp, Facing;
        public float T, Seconds, Height;
        public int Inspect = -1;

        /// <summary>On a broken world, a point the hop passes over to go round a hole rather than across it (<see cref="HopVia"/>), else null.</summary>
        public NVector3? Via;

        /// <summary>With <see cref="Via"/>: the first leg's share of the whole way (0..1).</summary>
        public float FirstLeg;
    }

    private Hop? _hop;

    // A hop's length and height grow with the distance along the ground, within limits (by eye; rationale not recorded):
    // 0.45 s plus a second per 40 m, at most 1.2 s; a peak of a quarter of the distance, 1.2–9 m.
    private const float HopBaseSeconds = 0.45f, HopMetresPerSecond = 40f, HopMaxSeconds = 1.2f;
    private const float HopHeightPerMetre = 0.25f, HopMinHeightMetres = 1.2f, HopMaxHeightMetres = 9f;

    /// <summary>A short hop across the planet to stand at <paramref name="up"/> facing <paramref name="facing"/> (then inspect).</summary>
    private void StartHop(NVector3 up, NVector3 facing, int inspect)
    {
        if (_current == null) return;
        var via = HopVia(_walker.Up, up);
        var first = via is { } v ? PlanetShape.Angle(_walker.Up, v) : 0f;
        var angle = via is { } w ? first + PlanetShape.Angle(w, up) : PlanetShape.Angle(_walker.Up, up);
        var dist = angle * _current.Comp.Shape.Radius;
        _hop = new Hop
        {
            FromUp = _walker.Up, ToUp = up, Facing = facing,
            Seconds = Mathf.Clamp(HopBaseSeconds + dist / HopMetresPerSecond, HopBaseSeconds, HopMaxSeconds),
            Height = Mathf.Clamp(dist * HopHeightPerMetre, HopMinHeightMetres, HopMaxHeightMetres),
            Inspect = inspect,
            Via = via,
            FirstLeg = angle > 1e-6f ? first / angle : 0f,
        };
        _audio.Jump();
        _avatar.Jump();
    }

    /// <summary>
    /// One frame of a hop: the traveller moves along the great circle (eased) on a parabola that peaks at
    /// <see cref="Hop.Height"/> halfway, and the walker is teleported with it (so it lands exactly at the end).
    /// </summary>
    private void UpdateHop(float dt)
    {
        var h = _hop!;
        h.T += dt;
        var u = Mathf.Clamp(h.T / h.Seconds, 0, 1);
        var s = Smooth(u);
        // Round a hole: two great-circle legs through Via, at the same pace as one straight arc.
        var dir = h.Via is not { } via ? Slerp(h.FromUp, h.ToUp, s)
            : s < h.FirstLeg ? Slerp(h.FromUp, via, s / h.FirstLeg) : Slerp(via, h.ToUp, (s - h.FirstLeg) / MathF.Max(1e-6f, 1 - h.FirstLeg));
        var surf = _current!.Comp.Surface;
        var height = 4 * h.Height * u * (1 - u); // 4u(1 − u): 0 at both ends, 1 at u = ½
        var facing = h.ToUp - dir * NVector3.Dot(h.ToUp, dir);
        facing = facing.LengthSquared() > 1e-5f ? NVector3.Normalize(facing) : h.Facing;
        var feet = Gd(surf.PointAt(dir, height));
        // The camera rides along rigidly (the same rotation as the traveller's up), so a hop across the planet never
        // leaves the view rolled and catching up (the roll cap is for walking, not for a deliberate jump elsewhere).
        var prevUp = Gd(_walker.Up);
        var nowUp = Gd(dir);
        var axis = prevUp.Cross(nowUp);
        if (axis.LengthSquared() > 1e-12f)
        {
            var q = new Quaternion(axis.Normalized(), prevUp.AngleTo(nowUp));
            _camPos = q * _camPos;
            _camLook = q * _camLook;
            _camUp = (q * _camUp).Normalized();
            _camFwd = (q * _camFwd).Normalized();
        }

        _avatar.SetPose(feet, Gd(dir), Gd(facing));
        _avatar.Drive(dt, 6f, false, (1 - 2 * u) * 6f); // animated as a 6 m/s airborne run, rising then falling at up to 6 m/s
        _walker.Teleport(surf, dir, facing);
        UpdateCamera(dt, lookAt: null, moving: true);
        if (u >= 1)
        {
            _walker.Teleport(surf, h.ToUp, h.Facing);
            _avatar.Land(4f);
            _audio.Land(4f); // a soft landing: impact as at 4 m/s (a flight lands at 8, a warp at 9)
            var inspect = h.Inspect;
            _hop = null;
            if (inspect >= 0) InspectNode(inspect);
        }
    }

    /// <summary>Spherical interpolation between two unit vectors (constant angular speed along the great circle).</summary>
    private static NVector3 Slerp(NVector3 a, NVector3 b, float t)
    {
        var ang = PlanetShape.Angle(a, b);
        if (ang < 1e-5f) return b;
        var s = MathF.Sin(ang);
        return NVector3.Normalize(a * (MathF.Sin((1 - t) * ang) / s) + b * (MathF.Sin(t * ang) / s));
    }
}
