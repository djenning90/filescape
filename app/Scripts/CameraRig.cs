using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Units;
using Godot;

namespace FileScape.App;

public enum CameraMode
{
    /// <summary>Orbit around a target point, with eased framing (inspection).</summary>
    Orbit,

    /// <summary>Free flight with mouse-look while the right button is held (travel).</summary>
    Fly,
}

/// <summary>
/// The camera (NAV-01..05, NAV-07..10). One pose representation serves both modes: a target point, a yaw, a look pitch
/// and a distance; the camera sits at target − forward·distance and never rolls.
/// <para>
/// <b>Current and desired pose.</b> The rig keeps two copies of the pose. The <i>current</i> pose is what is drawn;
/// the <i>desired</i> pose is where a framing request (Home, F, Destinations) wants the camera to be. In orbit mode the
/// current pose eases towards the desired one every frame by exponential smoothing (<see cref="Easing"/>): a framing
/// move starts fast and settles gently, and it is independent of the frame rate. Direct manipulation (fly, walking via
/// <see cref="SetPose"/>, look) sets both copies, so nothing drifts afterwards.
/// </para>
/// <para>
/// Units (docs/units.md): positions and distances are <b>world units</b> — metres in Places, abstract layout units in the
/// maps — so they stay Godot floats/vectors named accordingly; angles are <see cref="Degrees"/> in the CameraMath
/// convention (yaw 0 looks −Z, positive turns left; pitch positive looks up). Mouse input is in screen pixels.
/// </para>
/// </summary>
public partial class CameraRig : Node3D
{
    /// <summary>Orbit distance limits (world units): never inside the target, never beyond the far plane.</summary>
    public const float MinDistanceWorldUnits = 1.2f, MaxDistanceWorldUnits = 25_000f;

    /// <summary>A safety floor for the eye (world units above Y = 0); pitch limits normally keep the camera higher.</summary>
    public const float MinCameraHeightWorldUnits = 0.4f;

    /// <summary>
    /// Mouse sensitivities in degrees per screen pixel (physical pixels: InputEventMouseMotion.ScreenRelative, so the
    /// UI scale does not change them). Look: 0.12°/px, a 750 px sweep turns 90°. Orbit drags are a little faster.
    /// </summary>
    public const float LookDegreesPerPixel = 0.12f, OrbitYawDegreesPerPixel = 0.28f, OrbitPitchDegreesPerPixel = 0.22f;

    // Current pose (drawn) and desired pose (eased towards in orbit mode). Targets are world positions; distances are
    // world units along the view ray.
    private Vector3 _target, _desiredTarget;
    private Degrees _yaw, _desiredYaw;
    private Degrees _lookPitch = Degrees.Of(-32), _desiredLookPitch = Degrees.Of(-32);
    private float _distanceWorldUnits = 60, _desiredDistanceWorldUnits = 60;

    /// <summary>Map-fly velocity, world units per second, eased towards the input (see <see cref="FlyMove"/>).</summary>
    private Vector3 _flyVelocity;

    /// <summary>The wheel-chosen flight speed relative to the distance-based starting speed (dimensionless).</summary>
    private float _flySpeedMultiplier = 1f;

    public Camera3D Camera { get; private set; } = null!;
    public CameraMode Mode { get; private set; } = CameraMode.Orbit;
    public bool IsFlying => Mode == CameraMode.Fly;

    /// <summary>How quickly orbit mode closes the gap to the desired pose: 7/s settles 90 % of a move in ≈ 0.33 s.</summary>
    public PerSecond Easing { get; set; } = PerSecond.Of(7f);

    /// <summary>Map flight speed, world units per second (wheel-adjustable).</summary>
    public float FlySpeedWorldUnitsPerSecond { get; private set; } = 10f;

    public Vector3 Target => _target;
    public Vector3 DesiredTarget => _desiredTarget;

    /// <summary>The orbit distance from the target to the camera, world units.</summary>
    public float Distance => _distanceWorldUnits;

    public Degrees Yaw => _yaw;
    public Degrees LookPitch => _lookPitch;

    /// <summary>The camera's elevation above its target (positive = looking down), as used by framing.</summary>
    public Degrees Pitch => -_lookPitch;

    /// <summary>True once the current pose is within 1 % (distance) and 0.05° (angles) of the desired pose.</summary>
    public bool IsSettled =>
        _target.DistanceTo(_desiredTarget) < 0.01f * Mathf.Max(1, _distanceWorldUnits) &&
        Mathf.Abs(_distanceWorldUnits - _desiredDistanceWorldUnits) < 0.01f * _distanceWorldUnits &&
        Mathf.Abs((_yaw - _desiredYaw).Value) < 0.05f && Mathf.Abs((_lookPitch - _desiredLookPitch).Value) < 0.05f;

    public override void _Ready()
    {
        // Field of view in degrees (vertical); near/far planes in world units.
        Camera = new Camera3D { Fov = 55, Near = 0.15f, Far = 30_000f, Current = true };
        AddChild(Camera);
        Apply();
    }

    public override void _Process(double delta)
    {
        if (Mode == CameraMode.Orbit)
        {
            // Exponential smoothing: close the fraction k of the remaining gap this frame (frame-rate independent).
            // Distance eases in log space, so zooming feels uniform at any scale.
            var k = Easing.SmoothingFactor(Durations.FromSeconds(delta));
            _target = _target.Lerp(_desiredTarget, k);
            _distanceWorldUnits = Mathf.Exp(Mathf.Lerp(Mathf.Log(_distanceWorldUnits), Mathf.Log(_desiredDistanceWorldUnits), k));
            _yaw = Degrees.Of(Mathf.Lerp(_yaw.Value, _desiredYaw.Value, k));
            _lookPitch = Degrees.Of(Mathf.Lerp(_lookPitch.Value, _desiredLookPitch.Value, k));
        }

        Apply();
    }

    /// <summary>The eye position in world space.</summary>
    public Vector3 CameraPosition => Visualization.Gd(CameraMath.OrbitPosition(Visualization.Num(_target), _yaw, _lookPitch, _distanceWorldUnits));

    private void Apply()
    {
        var pos = CameraPosition;
        if (pos.Y < MinCameraHeightWorldUnits)
        {
            pos.Y = MinCameraHeightWorldUnits; // safety net only; pitch limits normally keep the camera above ground
        }

        // Godot boundary: Euler angles in radians (X = pitch, Y = yaw, no roll).
        Camera.GlobalTransform = new Transform3D(Basis.FromEuler(new Vector3(_lookPitch.ToRadians(), _yaw.ToRadians(), 0)), pos);
    }

    // ------------------------------------------------------------ orbit mode

    public void Orbit(Degrees deltaYaw, Degrees deltaLookPitch)
    {
        _desiredYaw += deltaYaw;
        _desiredLookPitch = ClampOrbitPitch(_desiredLookPitch + deltaLookPitch, _desiredTarget, _desiredDistanceWorldUnits);
    }

    private static Degrees ClampOrbitPitch(Degrees lookPitch, Vector3 target, float distanceWorldUnits) =>
        lookPitch.Clamp(CameraMath.MinLookPitch,
            CameraMath.MaxOrbitLookPitchAboveGround(target.Y, distanceWorldUnits, MinCameraHeightWorldUnits + 0.05f));

    /// <summary>Multiplies the orbit distance (dimensionless factor: below 1 zooms in).</summary>
    public void Zoom(float factor)
    {
        _desiredDistanceWorldUnits = Mathf.Clamp(_desiredDistanceWorldUnits * factor, MinDistanceWorldUnits, MaxDistanceWorldUnits);
        _desiredLookPitch = ClampOrbitPitch(_desiredLookPitch, _desiredTarget, _desiredDistanceWorldUnits);
    }

    /// <summary>
    /// Pans in the camera's screen plane by a delta in viewport units (UI-scaled pixels), scaled so the content under the
    /// cursor follows it: one viewport unit covers 2·d·tan(fov/2)/height world units at the target's distance d.
    /// </summary>
    public void PanPixels(Vector2 viewportUnits, float viewportHeight)
    {
        var worldUnitsPerViewportUnit = 2f * _distanceWorldUnits * Mathf.Tan(Mathf.DegToRad(Camera.Fov / 2)) / Mathf.Max(1, viewportHeight);
        var basis = Camera.GlobalTransform.Basis;
        _desiredTarget += (-basis.X * viewportUnits.X + basis.Y * viewportUnits.Y) * worldUnitsPerViewportUnit;
        _desiredTarget.Y = Mathf.Max(_desiredTarget.Y, 0);
        _desiredLookPitch = ClampOrbitPitch(_desiredLookPitch, _desiredTarget, _desiredDistanceWorldUnits);
    }

    /// <summary>
    /// Keyboard movement outside fly mode: heading-relative over the ground, plus world-vertical (Q/E). It applies to
    /// the current and the desired pose alike, so it responds immediately and leaves any framing in progress
    /// consistent. Speed (world units per second) is max(4, 0.9 × orbit distance) — useful close up and in overviews —
    /// ×3 with Shift.
    /// </summary>
    public void MoveGround(Vector3 input, bool fast, TimeSpan dt)
    {
        var speedWorldUnitsPerSecond = Mathf.Max(4f, _distanceWorldUnits * 0.9f) * (fast ? 3f : 1f);
        var delta = Visualization.Gd(CameraMath.GroundDelta(Visualization.Num(input), _yaw, speedWorldUnitsPerSecond, dt));
        _target += delta;
        _desiredTarget += delta;
        _target.Y = Mathf.Max(_target.Y, 0);
        _desiredTarget.Y = Mathf.Max(_desiredTarget.Y, 0);
        _desiredLookPitch = ClampOrbitPitch(_desiredLookPitch, _desiredTarget, _desiredDistanceWorldUnits);
        _lookPitch = ClampOrbitPitch(_lookPitch, _target, _distanceWorldUnits);
    }

    /// <summary>
    /// Flies to frame an AABB (world space) (approach/select-and-travel, NAV-02). <paramref name="elevation"/> is the
    /// angle above the target (positive = looking down); <paramref name="padding"/> is a dimensionless margin factor.
    /// </summary>
    public void Frame(Aabb box, Degrees? elevation = null, Degrees? yaw = null, float padding = 1.25f)
    {
        ExitFly();
        if (elevation.HasValue) _desiredLookPitch = -elevation.Value.Clamp(Degrees.Of(4), Degrees.Of(88));
        if (yaw.HasValue) _desiredYaw = yaw.Value;
        _desiredTarget = box.GetCenter();

        // Fit the box's projected extents rather than its bounding sphere: wide trees otherwise become a sliver.
        var yawR = _desiredYaw.ToRadians();
        var pitchR = (-_desiredLookPitch).ToRadians();
        var right = new Vector3(Mathf.Cos(yawR), 0, -Mathf.Sin(yawR));
        var forward = new Vector3(-Mathf.Sin(yawR), 0, -Mathf.Cos(yawR));
        var halfW = 0.5f * (Mathf.Abs(right.X) * box.Size.X + Mathf.Abs(right.Z) * box.Size.Z);
        var depth = Mathf.Abs(forward.X) * box.Size.X + Mathf.Abs(forward.Z) * box.Size.Z;
        var halfH = 0.5f * (depth * Mathf.Sin(pitchR) + box.Size.Y * Mathf.Cos(pitchR));
        var vfov = Mathf.DegToRad(Camera.Fov) * 0.5f;
        var aspect = GetViewport().GetVisibleRect().Size.Aspect();
        var hfov = Mathf.Atan(Mathf.Tan(vfov) * aspect);
        var fit = Mathf.Max(halfW / Mathf.Tan(hfov), halfH / Mathf.Tan(vfov)) + depth * 0.5f * Mathf.Cos(pitchR);
        _desiredDistanceWorldUnits = Mathf.Clamp(Mathf.Max(fit * padding, 2.5f), MinDistanceWorldUnits * 2, MaxDistanceWorldUnits);
        _desiredLookPitch = ClampOrbitPitch(_desiredLookPitch, _desiredTarget, _desiredDistanceWorldUnits);
    }

    // ------------------------------------------------------------ fly mode

    /// <summary>
    /// Starts flight from exactly the current pose. Any framing animation in progress stops where it is, so the
    /// camera cannot snap back to an old orbit target later.
    /// </summary>
    public void EnterFly()
    {
        if (IsFlying)
        {
            return;
        }

        FreezeDesired();
        Mode = CameraMode.Fly;
        _flyVelocity = Vector3.Zero;
        FlySpeedWorldUnitsPerSecond = Mathf.Clamp(CameraMath.InitialFlySpeed(_distanceWorldUnits) * _flySpeedMultiplier,
            CameraMath.MinFlySpeedWorldUnitsPerSecond, CameraMath.MaxFlySpeedWorldUnitsPerSecond);
    }

    /// <summary>Ends flight, keeping the exact pose. Orbiting then pivots around the point the camera was looking at.</summary>
    public void ExitFly()
    {
        if (!IsFlying)
        {
            return;
        }

        Mode = CameraMode.Orbit;
        _flyVelocity = Vector3.Zero;
        FreezeDesired();
    }

    /// <summary>
    /// Re-pivots the orbit at a new distance (world units) along the current view ray, without moving the camera. Main
    /// uses this after flight, so orbiting rotates around the surface under the screen centre.
    /// </summary>
    public void SetPivotDistanceKeepingPose(float distanceWorldUnits)
    {
        var pos = Visualization.Num(CameraPosition);
        _distanceWorldUnits = Mathf.Clamp(distanceWorldUnits, MinDistanceWorldUnits, MaxDistanceWorldUnits);
        _target = Visualization.Gd(CameraMath.TargetFromPose(pos, _yaw, _lookPitch, _distanceWorldUnits));
        FreezeDesired();
    }

    /// <summary>Mouse look from a delta in physical screen pixels (x right, y down): right turns right, down looks down.</summary>
    public void Look(Vector2 screenPixels)
    {
        var pos = Visualization.Num(CameraPosition);
        _yaw -= Degrees.Of(screenPixels.X * LookDegreesPerPixel);
        _lookPitch = CameraMath.ClampLookPitch(_lookPitch - Degrees.Of(screenPixels.Y * LookDegreesPerPixel));
        _target = Visualization.Gd(CameraMath.TargetFromPose(pos, _yaw, _lookPitch, _distanceWorldUnits));
        FreezeDesired();
    }

    /// <summary>How quickly map-fly velocity follows the input: 14/s (time constant ≈ 70 ms) — responsive, not abrupt.</summary>
    public static readonly PerSecond FlyResponse = PerSecond.Of(14f);

    /// <summary>Moves in map flight: the velocity (world units per second) eases towards the input, then integrates.</summary>
    public void FlyMove(Vector3 input, bool fast, TimeSpan dt)
    {
        var speedWorldUnitsPerSecond = FlySpeedWorldUnitsPerSecond * (fast ? 3f : 1f);
        var wantedVelocity = Visualization.Gd(CameraMath.FlyVelocity(Visualization.Num(input), _yaw, _lookPitch, speedWorldUnitsPerSecond));
        _flyVelocity = _flyVelocity.Lerp(wantedVelocity, FlyResponse.SmoothingFactor(dt));
        if (_flyVelocity.LengthSquared() < 1e-8f)
        {
            return;
        }

        var pos = CameraPosition + _flyVelocity * dt.ToSecondsF();
        pos.Y = Mathf.Max(pos.Y, MinCameraHeightWorldUnits);
        _target = Visualization.Gd(CameraMath.TargetFromPose(Visualization.Num(pos), _yaw, _lookPitch, _distanceWorldUnits));
        FreezeDesired();
    }

    /// <summary>Adjusts flight speed with the wheel. The multiplier persists, so the next flight starts at a similar relative speed.</summary>
    public void StepFlySpeed(float notches)
    {
        var baseSpeed = CameraMath.InitialFlySpeed(_distanceWorldUnits);
        FlySpeedWorldUnitsPerSecond = CameraMath.StepFlySpeed(FlySpeedWorldUnitsPerSecond, notches);
        _flySpeedMultiplier = Mathf.Clamp(FlySpeedWorldUnitsPerSecond / baseSpeed, 0.02f, 50f);
    }

    // ------------------------------------------------------------ shared

    /// <summary>
    /// Places the camera exactly at an eye position (world) with a yaw and look pitch, pivoting
    /// <paramref name="pivotDistanceWorldUnits"/> ahead. It works in both modes and does not end flight, so a place
    /// transition can carry a flying user (PLACE-07). Walking and Places flight drive the camera through this each frame.
    /// </summary>
    public void SetPose(Vector3 eye, Degrees yaw, Degrees lookPitch, float pivotDistanceWorldUnits)
    {
        _yaw = yaw;
        _lookPitch = CameraMath.ClampLookPitch(lookPitch);
        _distanceWorldUnits = Mathf.Clamp(pivotDistanceWorldUnits, MinDistanceWorldUnits, MaxDistanceWorldUnits);
        _target = Visualization.Gd(CameraMath.TargetFromPose(Visualization.Num(eye), _yaw, _lookPitch, _distanceWorldUnits));
        _flyVelocity = Vector3.Zero;
        FreezeDesired();
        Apply();
    }

    /// <summary>Eases (orbit mode) towards an eye pose; used for Home/approach in Places.</summary>
    public void GlideToPose(Vector3 eye, Degrees yaw, Degrees lookPitch, float pivotDistanceWorldUnits)
    {
        ExitFly();
        _desiredYaw = NearestAngle(_yaw, yaw);
        _desiredLookPitch = CameraMath.ClampLookPitch(lookPitch);
        _desiredDistanceWorldUnits = Mathf.Clamp(pivotDistanceWorldUnits, MinDistanceWorldUnits, MaxDistanceWorldUnits);
        _desiredTarget = Visualization.Gd(CameraMath.TargetFromPose(Visualization.Num(eye), _desiredYaw, _desiredLookPitch, _desiredDistanceWorldUnits));
    }

    /// <summary>The equivalent of <paramref name="angle"/> closest to <paramref name="current"/>, so turns take the short way round.</summary>
    public static Degrees NearestAngle(Degrees current, Degrees angle) => current + (angle - current).Wrapped();

    private void FreezeDesired()
    {
        _desiredTarget = _target;
        _desiredDistanceWorldUnits = _distanceWorldUnits;
        _desiredYaw = _yaw;
        _desiredLookPitch = _lookPitch;
    }

    /// <summary>Shifts the whole camera (a world-space displacement) without animation. It keeps the view anchored when re-layout moves the content.</summary>
    public void ShiftInstantly(Vector3 displacement)
    {
        _target += displacement;
        _desiredTarget += displacement;
        Apply();
    }

    /// <summary>Jumps to the desired state without easing (automation, first frame).</summary>
    public void Snap()
    {
        _target = _desiredTarget;
        _distanceWorldUnits = _desiredDistanceWorldUnits;
        _yaw = _desiredYaw;
        _lookPitch = _desiredLookPitch;
        Apply();
    }

    /// <summary>Sets the desired orbit pose. <paramref name="elevation"/> is the angle above the target (positive = looking down).</summary>
    public void SetDesired(Vector3 target, float distanceWorldUnits, Degrees yaw, Degrees elevation)
    {
        ExitFly();
        _desiredTarget = target;
        _desiredDistanceWorldUnits = Mathf.Clamp(distanceWorldUnits, MinDistanceWorldUnits, MaxDistanceWorldUnits);
        _desiredYaw = yaw;
        _desiredLookPitch = ClampOrbitPitch(-elevation, _desiredTarget, _desiredDistanceWorldUnits);
    }
}
