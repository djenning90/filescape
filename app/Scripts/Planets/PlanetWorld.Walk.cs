using FileScape.App.Places;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// Walking on a planet and the camera that follows (PLANET-11, AD-39). Movement is camera-relative (W away from the
/// camera, A/D to the sides), the traveller turns to face where it runs, and the chase camera follows by itself: it
/// swings in behind while you run, keeps its own up close to the local up with a capped roll rate (research E4: no
/// sudden, unexpected camera motion), never goes under the ground, and rises over the planet on the overlook. Right-drag
/// turns it (and the mouse wheel sets its distance); nothing else is required to play.
/// </summary>
public partial class PlanetWorld
{
    /// <summary>The camera's up turns towards the local up at most this fast (research E4: 90°/s cap, eased).</summary>
    public const float CameraRollCapDegreesPerSecond = 90f;

    private Godot.Vector3 _camPos, _camUp = Godot.Vector3.Up, _camFwd = Godot.Vector3.Forward, _camLook;
    private float _camPitchDegrees = 18f, _camDistance = 7.2f, _sinceOrbitSeconds = 99f;
    private float _overlook01;
    private bool _jumpQueued;
    private float _camBlend, _camBlendTotal = 0.6f;
    private Transform3D _camBlendFrom;

    /// <summary>
    /// The opening (research: introduce a destination by showing it): the camera starts high over the arrival meadow
    /// with the world and its neighbours in view, then glides down behind the traveller. You can move at once; any key
    /// or the mouse shortens it.
    /// </summary>
    public void PlayEstablishingShot(float seconds = 2.6f)
    {
        if (_current == null) return;
        var up = Gd(_walker.Up);
        var f = Gd(_walker.Facing);
        var r = _current.Comp.Shape.Radius;
        var eye = Gd(_walker.Feet) + up * (r * 1.5f + 10f) - f * (r * 1.1f + 8f);
        var look = Gd(_walker.Feet) + f * (r * 0.5f);
        _camBlendFrom = new Transform3D(Basis.LookingAt((look - eye).Normalized(), up), eye);
        _camera.GlobalTransform = _camBlendFrom;
        _camBlend = _camBlendTotal = seconds;
        EstablishingShots++;
    }

    public int EstablishingShots { get; private set; }

    /// <summary>A key or a click during the opening: finish the glide quickly (never cut).</summary>
    private void HurryBlend()
    {
        if (_camBlend > 0.45f && _camBlendTotal > 1f)
        {
            var k = 1 - _camBlend / _camBlendTotal;
            _camBlendTotal = 0.45f / MathF.Max(0.05f, 1 - k);
            _camBlend = 0.45f;
        }
    }
    private Godot.Vector3 _avatarUp = Godot.Vector3.Up;
    private float _maxRollRateDegreesPerSecond;
    private int _lastFootsteps;

    public float MaxCameraRollRateDegreesPerSecond => _maxRollRateDegreesPerSecond;
    public bool OnOverlook => _overlook01 > 0.5f;

    /// <summary>The overlook's camera on request (B): rise and look out over the planet, or come back down.</summary>
    private bool _overlookRequested;

    public void ToggleOverlookView() => _overlookRequested = !_overlookRequested;

    private void UpdateWalking(float dt, bool blockedKeys)
    {
        if (_current == null) return;
        if (_hop != null)
        {
            _overlook01 = Mathf.MoveToward(_overlook01, 0, dt * 1.6f);
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

        UpdateBench(dt, ev.Kind);
        if (_walker.Footsteps != _lastFootsteps)
        {
            _lastFootsteps = _walker.Footsteps;
            _audio.Step(Mathf.Clamp(_walker.Velocity.Length() / 11f, 0.2f, 1f));
            NoteFootstepNear();
        }

        PoseAvatar(dt);
        var onTop = _current.Comp.Overlook is { } ov && _walker.Support.Value > ov.Top - 0.3f && PlanetShape.Angle(_walker.Up, ov.Direction) * _current.Comp.Shape.Radius < ov.Radius + 0.3f;
        // The workbench keeps the chase camera: its cards rise round you and are read close up (the overlook view made them tiny).
        var wantOverlook = (onTop && !OnBenchWorld) || _overlookRequested;
        if (onTop && !_overlookNoted && !OnBenchWorld)
        {
            _overlookNoted = true;
            _audio.Inspect();
            ShowHint("From the overlook every destination is named. The telescope (E) lists everything here.");
        }

        _overlook01 = Mathf.MoveToward(_overlook01, wantOverlook ? 1 : 0, dt * 1.6f);
        UpdateCamera(dt, lookAt: null, moving: _walker.Velocity.Length() > 0.8f);
        UpdateFocus();
    }

    private bool _overlookNoted;

    private float _keyLook;

    /// <summary>
    /// ← → turn the camera (AD-47, as they turn the view in Places): standing still they look around; with W/S held the
    /// walk curves, because movement follows the camera. Eased in over ≈ 0.12 s so a tap nudges and a hold sweeps.
    /// </summary>
    private void KeyboardLook(float dt, float axis)
    {
        _keyLook = Mathf.MoveToward(_keyLook, axis, dt / 0.12f);
        if (MathF.Abs(_keyLook) < 1e-3f) return;
        var up = Gd(_walker.Up);
        var fast = Godot.Input.IsActionPressed(InputBindings.Fast) ? 1.6f : 1f;
        _camFwd = _camFwd.Rotated(up, -_keyLook * fast * Mathf.DegToRad(KeyLookDegreesPerSecond) * dt).Normalized();
        _sinceOrbitSeconds = 0;
    }

    private static float Axis(string negative, string positive) =>
        (Godot.Input.IsActionPressed(positive) ? 1f : 0f) - (Godot.Input.IsActionPressed(negative) ? 1f : 0f);

    /// <summary>The traveller's pose from the walker: feet on the ground, up along the local up, facing where it runs.</summary>
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
        if (moving && _sinceOrbitSeconds > 1.2f && !_orbiting)
        {
            // Swing in behind a running traveller (not when it runs towards the camera: that would spin the view).
            var ang = f.SignedAngleTo(facing, up);
            if (MathF.Abs(ang) < Mathf.DegToRad(135)) f = f.Rotated(up, ang * (1 - MathF.Exp(-1.4f * dt)));
        }

        _camFwd = f;
        // Up: eased towards the local up and capped (90°/s), so walking round a small world never rolls the view suddenly.
        var upAngle = _camUp.AngleTo(up);
        if (upAngle > 1e-5f)
        {
            var step = MathF.Min(upAngle * (1 - MathF.Exp(-6f * dt)), Mathf.DegToRad(CameraRollCapDegreesPerSecond) * dt);
            var axis = _camUp.Cross(up);
            if (axis.LengthSquared() > 1e-10f) _camUp = _camUp.Rotated(axis.Normalized(), step).Normalized();
            else _camUp = up;
            if (dt > 0) _maxRollRateDegreesPerSecond = MathF.Max(_maxRollRateDegreesPerSecond, Mathf.RadToDeg(step) / dt);
        }

        var pitch = Mathf.DegToRad(Mathf.Lerp(_camPitchDegrees, 34f, _overlook01));
        var dist = Mathf.Lerp(_camDistance, 15f, _overlook01) + extraBack;
        var target = feet + up * (1.15f + extraUp * 0.5f) + facing * 0.5f;
        var desired = target - f * (MathF.Cos(pitch) * dist) + up * (MathF.Sin(pitch) * dist + extraUp);
        // Never under the ground: keep the eye at least 0.9 m above the surface below it.
        var dir = desired.Normalized();
        var minR = _current.Comp.Shape.GroundRadius(Nv(dir)) + 0.9f;
        if (desired.Length() < minR) desired = dir * minR;
        _camPos = _camPos.Lerp(desired, 1 - MathF.Exp(-7.5f * dt));
        var look = target;
        if (lookAt is { } la)
        {
            // Wind-up: look past the traveller towards where it is going; it stays low in the frame, the world above it.
            var toward = (la - target).Normalized();
            look = target + toward * 5f;
        }
        // The point looked at is smoothed too, so the view never turns in one frame (launch, landing, a hop's end).
        _camLook = _camLook.Lerp(look, 1 - MathF.Exp(-12f * dt));
        look = _camPos + KeepInFrame(_camPos, _camLook - _camPos, feet + up * 0.6f, 18f);
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

        _camera.Fov = Mathf.Lerp(_camera.Fov, 62f, 1 - MathF.Exp(-4f * dt));
    }

    /// <summary>
    /// The camera in flight: it travels with the traveller (an offset from it, eased towards behind-and-above along the
    /// direction of travel, scaled with it), so the traveller stays steady on screen however fast it goes, and it turns
    /// to frame the target as it nears.
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

        var back = warp ? 12f : 9f;
        var desired = -fwd * (back * scale) + up * (3.2f * scale);
        if (warp) desired = tr.Built ? tr.ToUp * 9f + Gd(tr.LandFacing) * 6f : -tr.FromUp * 6f - _camFwd * 9f;
        _camOffset = _camOffset.Lerp(desired, 1 - MathF.Exp(-3.5f * dt));
        _camPos = pos + _camOffset;
        var lookWant = fwd * (6f * scale);
        if (tr.Target != null) lookWant = lookWant.Lerp(tr.Target.Node.GlobalPosition - pos, Smooth(u) * 0.5f);
        _lookOffset = _lookOffset.Lerp(lookWant, 1 - MathF.Exp(-4f * dt));
        _camLook = pos + _lookOffset;
        _camUp = _camUp.Slerp(up, 1 - MathF.Exp(-5f * dt)).Normalized();
        var lookDir = _camLook - _camPos;
        if (lookDir.LengthSquared() < 1e-6f) lookDir = fwd;
        lookDir = KeepInFrame(_camPos, lookDir, pos + up * (0.6f * scale), 16f);
        if (MathF.Abs(lookDir.Normalized().Dot(_camUp)) > 0.995f) _camUp = side.Normalized();
        _camera.LookAtFromPosition(_camPos, _camPos + lookDir, _camUp);
        _camera.Fov = 62f + 12f * Mathf.Sin(Mathf.Clamp(u, 0, 1) * Mathf.Pi);
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

    private bool _flightCamStarted;
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
        var target = feet + up * 1.15f;
        _camPos = target - _camFwd * (MathF.Cos(pitch) * _camDistance) + up * (MathF.Sin(pitch) * _camDistance);
        _camera.LookAtFromPosition(_camPos, target, _camUp);
        _camLook = target;
        _camBlend = 0;
        _overlook01 = 0;
        PoseAvatar(0);
    }

    // ------------------------------------------------------------------ hops: short in-planet arcs (Home, approach)

    private sealed class Hop
    {
        public NVector3 FromUp, ToUp, Facing;
        public float T, Seconds, Height;
        public int Inspect = -1;
    }

    private Hop? _hop;

    /// <summary>A short hop across the planet to stand at <paramref name="up"/> facing <paramref name="facing"/> (then inspect).</summary>
    private void StartHop(NVector3 up, NVector3 facing, int inspect)
    {
        if (_current == null) return;
        var dist = PlanetShape.Angle(_walker.Up, up) * _current.Comp.Shape.Radius;
        _hop = new Hop { FromUp = _walker.Up, ToUp = up, Facing = facing, Seconds = Mathf.Clamp(0.45f + dist / 40f, 0.45f, 1.2f), Height = Mathf.Clamp(dist * 0.25f, 1.2f, 9f), Inspect = inspect };
        _audio.Jump();
        _avatar.Jump();
    }

    private void UpdateHop(float dt)
    {
        var h = _hop!;
        h.T += dt;
        var u = Mathf.Clamp(h.T / h.Seconds, 0, 1);
        var dir = Slerp(h.FromUp, h.ToUp, Smooth(u));
        var surf = _current!.Comp.Surface;
        var height = 4 * h.Height * u * (1 - u);
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
        _avatar.Drive(dt, 6f, false, (1 - 2 * u) * 6f);
        _walker.Teleport(surf, dir, facing);
        UpdateCamera(dt, lookAt: null, moving: true);
        if (u >= 1)
        {
            _walker.Teleport(surf, h.ToUp, h.Facing);
            _avatar.Land(4f);
            _audio.Land(4f);
            var inspect = h.Inspect;
            _hop = null;
            if (inspect >= 0) InspectNode(inspect);
        }
    }

    private static NVector3 Slerp(NVector3 a, NVector3 b, float t)
    {
        var ang = PlanetShape.Angle(a, b);
        if (ang < 1e-5f) return b;
        var s = MathF.Sin(ang);
        return NVector3.Normalize(a * (MathF.Sin((1 - t) * ang) / s) + b * (MathF.Sin(t * ang) / s));
    }
}
