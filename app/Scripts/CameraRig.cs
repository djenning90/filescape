using FileScape.App.Visual;
using FileScape.Core.Interaction;
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
/// The camera (NAV-01..05, NAV-07..10). There is one pose representation for both modes: a target point, yaw,
/// look pitch, and distance. The camera sits at target − forward·distance and never rolls. Orbit mode eases the
/// actual pose towards a desired pose. Fly mode moves the camera and its target together with no easing, so
/// entering or leaving flight changes nothing on screen (no jump, no rotation, no snap back). The math lives in
/// <see cref="CameraMath"/> (Core) and is unit-tested there.
/// </summary>
public partial class CameraRig : Node3D
{
    public const float MinDistance = 1.2f, MaxDistance = 25_000f, MinCameraHeight = 0.4f;

    /// <summary>Degrees per pixel of mouse movement.</summary>
    public const float LookSensitivity = 0.12f, OrbitYawSensitivity = 0.28f, OrbitPitchSensitivity = 0.22f;

    private Vector3 _target, _desiredTarget;
    private float _yaw, _desiredYaw;
    private float _lookPitch = -32, _desiredLookPitch = -32;
    private float _distance = 60, _desiredDistance = 60;
    private Vector3 _flyVelocity;
    private float _flySpeedMultiplier = 1f;

    public Camera3D Camera { get; private set; } = null!;
    public CameraMode Mode { get; private set; } = CameraMode.Orbit;
    public bool IsFlying => Mode == CameraMode.Fly;

    /// <summary>The rate at which orbit mode approaches the desired pose, per second.</summary>
    public float Sharpness { get; set; } = 7f;

    public float FlySpeed { get; private set; } = 10f;
    public Vector3 Target => _target;
    public Vector3 DesiredTarget => _desiredTarget;
    public float Distance => _distance;
    public float Yaw => _yaw;
    public float LookPitch => _lookPitch;

    /// <summary>The camera's elevation above its target in degrees (positive = looking down), as used by framing.</summary>
    public float Pitch => -_lookPitch;

    public bool IsSettled =>
        _target.DistanceTo(_desiredTarget) < 0.01f * Mathf.Max(1, _distance) &&
        Mathf.Abs(_distance - _desiredDistance) < 0.01f * _distance &&
        Mathf.Abs(_yaw - _desiredYaw) < 0.05f && Mathf.Abs(_lookPitch - _desiredLookPitch) < 0.05f;

    public override void _Ready()
    {
        Camera = new Camera3D { Fov = 55, Near = 0.15f, Far = 30_000f, Current = true };
        AddChild(Camera);
        Apply();
    }

    public override void _Process(double delta)
    {
        if (Mode == CameraMode.Orbit)
        {
            var k = 1f - Mathf.Exp(-Sharpness * (float)delta);
            _target = _target.Lerp(_desiredTarget, k);
            _distance = Mathf.Exp(Mathf.Lerp(Mathf.Log(_distance), Mathf.Log(_desiredDistance), k));
            _yaw = Mathf.Lerp(_yaw, _desiredYaw, k);
            _lookPitch = Mathf.Lerp(_lookPitch, _desiredLookPitch, k);
        }

        Apply();
    }

    public Vector3 CameraPosition => Visualization.Gd(CameraMath.OrbitPosition(Visualization.Num(_target), _yaw, _lookPitch, _distance));

    private void Apply()
    {
        var pos = CameraPosition;
        if (pos.Y < MinCameraHeight)
        {
            pos.Y = MinCameraHeight; // safety net only; pitch limits normally keep the camera above ground
        }

        Camera.GlobalTransform = new Transform3D(
            Basis.FromEuler(new Vector3(Mathf.DegToRad(_lookPitch), Mathf.DegToRad(_yaw), 0)), pos);
    }

    // ------------------------------------------------------------ orbit mode

    public void Orbit(float dYaw, float dLookPitch)
    {
        _desiredYaw += dYaw;
        _desiredLookPitch = ClampOrbitPitch(_desiredLookPitch + dLookPitch, _desiredTarget, _desiredDistance);
    }

    private static float ClampOrbitPitch(float lookPitch, Vector3 target, float distance) =>
        Mathf.Clamp(lookPitch, CameraMath.MinLookPitch,
            CameraMath.MaxOrbitLookPitchAboveGround(target.Y, distance, MinCameraHeight + 0.05f));

    public void Zoom(float factor)
    {
        _desiredDistance = Mathf.Clamp(_desiredDistance * factor, MinDistance, MaxDistance);
        _desiredLookPitch = ClampOrbitPitch(_desiredLookPitch, _desiredTarget, _desiredDistance);
    }

    /// <summary>Pans in the camera's screen plane by a pixel delta, scaled so the content under the cursor follows it.</summary>
    public void PanPixels(Vector2 pixels, float viewportHeight)
    {
        var worldPerPixel = 2f * _distance * Mathf.Tan(Mathf.DegToRad(Camera.Fov / 2)) / Mathf.Max(1, viewportHeight);
        var basis = Camera.GlobalTransform.Basis;
        _desiredTarget += (-basis.X * pixels.X + basis.Y * pixels.Y) * worldPerPixel;
        _desiredTarget.Y = Mathf.Max(_desiredTarget.Y, 0);
        _desiredLookPitch = ClampOrbitPitch(_desiredLookPitch, _desiredTarget, _desiredDistance);
    }

    /// <summary>
    /// Keyboard movement outside fly mode: heading-relative over the ground, plus world-vertical (Q/E). It applies to
    /// the current and the desired pose alike, so it responds immediately and leaves any framing in progress
    /// consistent. The speed scales with the viewing distance, so it suits both close inspection and overviews.
    /// </summary>
    public void MoveGround(Vector3 input, bool fast, float dt)
    {
        var speed = Mathf.Max(4f, _distance * 0.9f) * (fast ? 3f : 1f);
        var delta = Visualization.Gd(CameraMath.GroundDelta(Visualization.Num(input), _yaw, speed, dt));
        _target += delta;
        _desiredTarget += delta;
        _target.Y = Mathf.Max(_target.Y, 0);
        _desiredTarget.Y = Mathf.Max(_desiredTarget.Y, 0);
        _desiredLookPitch = ClampOrbitPitch(_desiredLookPitch, _desiredTarget, _desiredDistance);
        _lookPitch = ClampOrbitPitch(_lookPitch, _target, _distance);
    }

    /// <summary>Flies to frame an AABB (approach/select-and-travel, NAV-02). <paramref name="pitch"/> is elevation in degrees.</summary>
    public void Frame(Aabb box, float? pitch = null, float? yaw = null, float padding = 1.25f)
    {
        ExitFly();
        if (pitch.HasValue) _desiredLookPitch = -Mathf.Clamp(pitch.Value, 4, 88);
        if (yaw.HasValue) _desiredYaw = yaw.Value;
        _desiredTarget = box.GetCenter();

        // Fit the box's projected extents rather than its bounding sphere: wide trees otherwise become a sliver.
        var yawR = Mathf.DegToRad(_desiredYaw);
        var pitchR = Mathf.DegToRad(-_desiredLookPitch);
        var right = new Vector3(Mathf.Cos(yawR), 0, -Mathf.Sin(yawR));
        var forward = new Vector3(-Mathf.Sin(yawR), 0, -Mathf.Cos(yawR));
        var halfW = 0.5f * (Mathf.Abs(right.X) * box.Size.X + Mathf.Abs(right.Z) * box.Size.Z);
        var depth = Mathf.Abs(forward.X) * box.Size.X + Mathf.Abs(forward.Z) * box.Size.Z;
        var halfH = 0.5f * (depth * Mathf.Sin(pitchR) + box.Size.Y * Mathf.Cos(pitchR));
        var vfov = Mathf.DegToRad(Camera.Fov) * 0.5f;
        var aspect = GetViewport().GetVisibleRect().Size.Aspect();
        var hfov = Mathf.Atan(Mathf.Tan(vfov) * aspect);
        var fit = Mathf.Max(halfW / Mathf.Tan(hfov), halfH / Mathf.Tan(vfov)) + depth * 0.5f * Mathf.Cos(pitchR);
        _desiredDistance = Mathf.Clamp(Mathf.Max(fit * padding, 2.5f), MinDistance * 2, MaxDistance);
        _desiredLookPitch = ClampOrbitPitch(_desiredLookPitch, _desiredTarget, _desiredDistance);
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
        FlySpeed = Mathf.Clamp(CameraMath.InitialFlySpeed(_distance) * _flySpeedMultiplier, CameraMath.MinFlySpeed, CameraMath.MaxFlySpeed);
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
    /// Re-pivots the orbit at a new distance along the current view ray, without moving the camera. Main uses
    /// this after flight, so orbiting rotates around the surface under the screen centre.
    /// </summary>
    public void SetPivotDistanceKeepingPose(float distance)
    {
        var pos = Visualization.Num(CameraPosition);
        _distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
        _target = Visualization.Gd(CameraMath.TargetFromPose(pos, _yaw, _lookPitch, _distance));
        FreezeDesired();
    }

    public void Look(Vector2 relativePixels)
    {
        var pos = Visualization.Num(CameraPosition);
        _yaw -= relativePixels.X * LookSensitivity;
        _lookPitch = CameraMath.ClampLookPitch(_lookPitch - relativePixels.Y * LookSensitivity);
        _target = Visualization.Gd(CameraMath.TargetFromPose(pos, _yaw, _lookPitch, _distance));
        FreezeDesired();
    }

    /// <summary>Moves in flight. Velocity eases quickly (time constant ≈ 70 ms), so starts and stops feel responsive, not abrupt.</summary>
    public void FlyMove(Vector3 input, bool fast, float dt)
    {
        var speed = FlySpeed * (fast ? 3f : 1f);
        var wanted = Visualization.Gd(CameraMath.FlyDelta(Visualization.Num(input), _yaw, _lookPitch, speed, 1f));
        _flyVelocity = _flyVelocity.Lerp(wanted, 1f - Mathf.Exp(-14f * dt));
        if (_flyVelocity.LengthSquared() < 1e-8f)
        {
            return;
        }

        var pos = CameraPosition + _flyVelocity * dt;
        pos.Y = Mathf.Max(pos.Y, MinCameraHeight);
        _target = Visualization.Gd(CameraMath.TargetFromPose(Visualization.Num(pos), _yaw, _lookPitch, _distance));
        FreezeDesired();
    }

    /// <summary>Adjusts flight speed with the wheel. The multiplier persists, so the next flight starts at a similar relative speed.</summary>
    public void StepFlySpeed(float notches)
    {
        var baseSpeed = CameraMath.InitialFlySpeed(_distance);
        FlySpeed = CameraMath.StepFlySpeed(FlySpeed, notches);
        _flySpeedMultiplier = Mathf.Clamp(FlySpeed / baseSpeed, 0.02f, 50f);
    }

    // ------------------------------------------------------------ shared

    /// <summary>
    /// Places the camera exactly at an eye position with a yaw and look pitch, pivoting <paramref name="pivotDistance"/>
    /// ahead. It works in both modes and does not end flight, so a place transition can carry a flying user (PLACE-07).
    /// </summary>
    public void SetPose(Vector3 eye, float yaw, float lookPitch, float pivotDistance)
    {
        _yaw = yaw;
        _lookPitch = CameraMath.ClampLookPitch(lookPitch);
        _distance = Mathf.Clamp(pivotDistance, MinDistance, MaxDistance);
        _target = Visualization.Gd(CameraMath.TargetFromPose(Visualization.Num(eye), _yaw, _lookPitch, _distance));
        _flyVelocity = Vector3.Zero;
        FreezeDesired();
        Apply();
    }

    /// <summary>Eases (orbit mode) towards an eye pose; used for Home/approach in Places.</summary>
    public void GlideToPose(Vector3 eye, float yaw, float lookPitch, float pivotDistance)
    {
        ExitFly();
        _desiredYaw = NearestAngle(_yaw, yaw);
        _desiredLookPitch = CameraMath.ClampLookPitch(lookPitch);
        _desiredDistance = Mathf.Clamp(pivotDistance, MinDistance, MaxDistance);
        _desiredTarget = Visualization.Gd(CameraMath.TargetFromPose(Visualization.Num(eye), _desiredYaw, _desiredLookPitch, _desiredDistance));
    }

    /// <summary>The equivalent of <paramref name="angle"/> closest to <paramref name="current"/>, so turns take the short way round.</summary>
    public static float NearestAngle(float current, float angle) => current + Mathf.Wrap(angle - current, -180f, 180f);

    private void FreezeDesired()
    {
        _desiredTarget = _target;
        _desiredDistance = _distance;
        _desiredYaw = _yaw;
        _desiredLookPitch = _lookPitch;
    }

    /// <summary>Shifts the whole camera without animation. It keeps the view anchored when re-layout moves the content.</summary>
    public void ShiftInstantly(Vector3 delta)
    {
        _target += delta;
        _desiredTarget += delta;
        Apply();
    }

    /// <summary>Jumps to the desired state without easing (automation, first frame).</summary>
    public void Snap()
    {
        _target = _desiredTarget;
        _distance = _desiredDistance;
        _yaw = _desiredYaw;
        _lookPitch = _desiredLookPitch;
        Apply();
    }

    /// <summary>Sets the desired orbit pose. <paramref name="pitch"/> is elevation in degrees (positive = looking down).</summary>
    public void SetDesired(Vector3 target, float distance, float yaw, float pitch)
    {
        ExitFly();
        _desiredTarget = target;
        _desiredDistance = Mathf.Clamp(distance, MinDistance, MaxDistance);
        _desiredYaw = yaw;
        _desiredLookPitch = ClampOrbitPitch(-pitch, _desiredTarget, _desiredDistance);
    }
}
