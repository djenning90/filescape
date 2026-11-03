using Godot;

namespace FileScape.App;

/// <summary>
/// An orbit camera around a target point, with exponential smoothing towards desired values (NAV-01..05).
/// Interaction code changes the desired state only. Process() eases the actual state towards it, so travel and
/// recovery are animated, and any input during a flight simply retargets it.
/// </summary>
public partial class CameraRig : Node3D
{
    public const float MinPitch = 4f, MaxPitch = 88f, MinDistance = 1.2f, MaxDistance = 25_000f;

    private Vector3 _target, _desiredTarget;
    private float _yaw, _desiredYaw;
    private float _pitch = 32, _desiredPitch = 32;
    private float _distance = 60, _desiredDistance = 60;

    public Camera3D Camera { get; private set; } = null!;

    /// <summary>The rate at which the camera approaches the desired state, per second.</summary>
    public float Sharpness { get; set; } = 7f;

    public Vector3 Target => _target;
    public Vector3 DesiredTarget => _desiredTarget;
    public float Distance => _distance;
    public float Yaw => _yaw;
    public float Pitch => _pitch;
    public bool IsSettled =>
        _target.DistanceTo(_desiredTarget) < 0.01f * Mathf.Max(1, _distance) &&
        Mathf.Abs(_distance - _desiredDistance) < 0.01f * _distance &&
        Mathf.Abs(_yaw - _desiredYaw) < 0.05f && Mathf.Abs(_pitch - _desiredPitch) < 0.05f;

    public override void _Ready()
    {
        Camera = new Camera3D { Fov = 55, Near = 0.15f, Far = 30_000f, Current = true };
        AddChild(Camera);
        Apply();
    }

    public override void _Process(double delta)
    {
        var k = 1f - Mathf.Exp(-Sharpness * (float)delta);
        _target = _target.Lerp(_desiredTarget, k);
        _distance = Mathf.Exp(Mathf.Lerp(Mathf.Log(_distance), Mathf.Log(_desiredDistance), k));
        _yaw = Mathf.Lerp(_yaw, _desiredYaw, k);
        _pitch = Mathf.Lerp(_pitch, _desiredPitch, k);
        Apply();
    }

    private void Apply()
    {
        var yaw = Mathf.DegToRad(_yaw);
        var pitch = Mathf.DegToRad(_pitch);
        var offset = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)) * _distance;
        var pos = _target + offset;
        if (pos.Y < 0.4f)
        {
            pos.Y = 0.4f; // never look from under the ground
        }

        Camera.GlobalPosition = pos;
        Camera.LookAt(_target, Vector3.Up);
    }

    public void Orbit(float dYaw, float dPitch)
    {
        _desiredYaw += dYaw;
        _desiredPitch = Mathf.Clamp(_desiredPitch + dPitch, MinPitch, MaxPitch);
    }

    public void Zoom(float factor) =>
        _desiredDistance = Mathf.Clamp(_desiredDistance * factor, MinDistance, MaxDistance);

    /// <summary>Pans in the camera's screen plane by a pixel delta, scaled so the content under the cursor follows it.</summary>
    public void PanPixels(Vector2 pixels, float viewportHeight)
    {
        var worldPerPixel = 2f * _distance * Mathf.Tan(Mathf.DegToRad(Camera.Fov / 2)) / Mathf.Max(1, viewportHeight);
        var basis = Camera.GlobalTransform.Basis;
        var move = (-basis.X * pixels.X + basis.Y * pixels.Y) * worldPerPixel;
        _desiredTarget += move;
        _desiredTarget.Y = Mathf.Max(_desiredTarget.Y, 0);
    }

    /// <summary>Moves the target over the ground relative to the current heading (WASD/QE).</summary>
    public void MoveLocal(Vector3 input, float dt)
    {
        var yaw = Mathf.DegToRad(_yaw);
        var forward = new Vector3(-Mathf.Sin(yaw), 0, -Mathf.Cos(yaw));
        var right = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
        var speed = Mathf.Max(4f, _distance * 0.9f);
        _desiredTarget += (forward * -input.Z + right * input.X + Vector3.Up * input.Y) * speed * dt;
        _desiredTarget.Y = Mathf.Max(_desiredTarget.Y, 0);
    }

    /// <summary>Flies to frame an AABB (approach/select-and-travel, NAV-02).</summary>
    public void Frame(Aabb box, float? pitch = null, float? yaw = null, float padding = 1.25f)
    {
        if (pitch.HasValue) _desiredPitch = Mathf.Clamp(pitch.Value, MinPitch, MaxPitch);
        if (yaw.HasValue) _desiredYaw = yaw.Value;
        _desiredTarget = box.GetCenter();

        // Fit the box's projected extents, not its bounding sphere. FSN-style trees are much wider than deep, so
        // sphere fitting would leave them a thin sliver on the horizon.
        var yawR = Mathf.DegToRad(_desiredYaw);
        var pitchR = Mathf.DegToRad(_desiredPitch);
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
    }

    /// <summary>Shifts the whole camera, current and desired, without animation. It keeps the view anchored when re-layout moves the content.</summary>
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
        _pitch = _desiredPitch;
        Apply();
    }

    public void SetDesired(Vector3 target, float distance, float yaw, float pitch)
    {
        _desiredTarget = target;
        _desiredDistance = Mathf.Clamp(distance, MinDistance, MaxDistance);
        _desiredYaw = yaw;
        _desiredPitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
    }
}
