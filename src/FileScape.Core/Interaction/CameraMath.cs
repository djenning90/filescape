using System.Numerics;

namespace FileScape.Core.Interaction;

/// <summary>
/// Engine-independent camera navigation math (NAV-01, NAV-07..NAV-10), kept here so it can be unit-tested.
/// <para>
/// Conventions: Y is up. <c>yaw</c> is in degrees. At yaw 0 the camera looks along -Z. <c>lookPitch</c> is in
/// degrees and is positive when looking up. There is never any roll. Movement input is (x = right, y = up,
/// z = forward), each component in -1..1.
/// </para>
/// </summary>
public static class CameraMath
{
    /// <summary>The look-pitch limits. Close to vertical, but never past it, so the view cannot flip.</summary>
    public const float MinLookPitch = -85f, MaxLookPitch = 85f;

    public const float MinFlySpeed = 0.5f, MaxFlySpeed = 3000f;

    /// <summary>The speed change per wheel notch while flying.</summary>
    public const float WheelSpeedStep = 1.25f;

    public static Vector3 Forward(float yawDeg, float lookPitchDeg)
    {
        var y = yawDeg * MathF.PI / 180f;
        var p = lookPitchDeg * MathF.PI / 180f;
        return new Vector3(-MathF.Sin(y) * MathF.Cos(p), MathF.Sin(p), -MathF.Cos(y) * MathF.Cos(p));
    }

    public static Vector3 HorizontalForward(float yawDeg) => Forward(yawDeg, 0);

    public static Vector3 Right(float yawDeg)
    {
        var y = yawDeg * MathF.PI / 180f;
        return new Vector3(MathF.Cos(y), 0, -MathF.Sin(y));
    }

    /// <summary>Limits combined input to unit length, so diagonals are not faster than straight moves.</summary>
    public static Vector3 ClampInput(Vector3 input) => input.LengthSquared() > 1f ? Vector3.Normalize(input) : input;

    /// <summary>Orbit-mode keyboard movement: heading-relative over the ground plane, plus world-vertical (Q/E).</summary>
    public static Vector3 GroundDelta(Vector3 input, float yawDeg, float speed, float dt) =>
        Compose(input, HorizontalForward(yawDeg), Right(yawDeg), speed, dt);

    /// <summary>Fly-mode movement: forward/back along the view direction (pitch included), horizontal strafe, world-vertical Q/E.</summary>
    public static Vector3 FlyDelta(Vector3 input, float yawDeg, float lookPitchDeg, float speed, float dt) =>
        Compose(input, Forward(yawDeg, lookPitchDeg), Right(yawDeg), speed, dt);

    /// <summary>
    /// Combines the axes in world space, then normalizes. A pitched forward vector and world up are not
    /// orthogonal, so normalizing the input alone would make some combinations slower or faster than a straight
    /// move. The world speed is speed × min(1, |input|).
    /// </summary>
    private static Vector3 Compose(Vector3 input, Vector3 forward, Vector3 right, float speed, float dt)
    {
        var world = forward * input.Z + right * input.X + Vector3.UnitY * input.Y;
        var len = world.Length();
        if (len < 1e-6f)
        {
            return Vector3.Zero;
        }

        return world / len * MathF.Min(1f, input.Length()) * speed * dt;
    }

    public static float ClampLookPitch(float pitch) => Math.Clamp(pitch, MinLookPitch, MaxLookPitch);

    /// <summary>
    /// Adjusts fly speed by wheel notches (positive = faster), within useful bounds. Notches may be fractional:
    /// high-resolution wheels and touchpads report partial notches, and fast spins report several in one event.
    /// </summary>
    public static float StepFlySpeed(float speed, float notches) =>
        Math.Clamp(speed * MathF.Pow(WheelSpeedStep, notches), MinFlySpeed, MaxFlySpeed);

    /// <summary>The starting fly speed, scaled to how far the camera is from what it was looking at (useful at any scale).</summary>
    public static float InitialFlySpeed(float orbitDistance) => Math.Clamp(orbitDistance * 0.6f, 2f, 400f);

    /// <summary>The camera position in orbit mode: on a sphere around the target, opposite the look direction.</summary>
    public static Vector3 OrbitPosition(Vector3 target, float yawDeg, float lookPitchDeg, float distance) =>
        target - Forward(yawDeg, lookPitchDeg) * distance;

    /// <summary>The orbit target that keeps a given camera pose exactly unchanged, used for continuous fly → orbit transitions.</summary>
    public static Vector3 TargetFromPose(Vector3 position, float yawDeg, float lookPitchDeg, float distance) =>
        position + Forward(yawDeg, lookPitchDeg) * distance;

    /// <summary>
    /// The highest look pitch (least downward) an orbit may use before the camera would pass below
    /// <paramref name="minCameraHeight"/>. This prevents the ground clamp from moving the camera off its sphere.
    /// </summary>
    public static float MaxOrbitLookPitchAboveGround(float targetHeight, float distance, float minCameraHeight)
    {
        // camera.y = target.y - sin(p)·d ≥ h  ⇒  sin(p) ≤ (target.y - h) / d
        var s = (targetHeight - minCameraHeight) / MathF.Max(distance, 1e-3f);
        return s >= 1 ? MaxLookPitch : s <= -1 ? MinLookPitch : MathF.Min(MaxLookPitch, MathF.Asin(s) * 180f / MathF.PI);
    }
}

/// <summary>
/// Tracks which navigation actions are held, from individual key press and release events. An action stays held
/// while any of its bound keys is down, so aliases (W and Up arrow) never double the speed. Clear() recovers from
/// focus loss, so no key can stay stuck. The app feeds only events that no UI control consumed (NAV-08).
/// </summary>
public sealed class HeldActions
{
    private readonly Dictionary<string, HashSet<long>> _held = new(StringComparer.Ordinal);

    public void Press(string action, long key)
    {
        if (!_held.TryGetValue(action, out var keys))
        {
            keys = [];
            _held[action] = keys;
        }

        keys.Add(key);
    }

    /// <summary>Releasing a key that was never registered as pressed (because the UI consumed the press) is harmless.</summary>
    public void Release(string action, long key)
    {
        if (_held.TryGetValue(action, out var keys))
        {
            keys.Remove(key);
        }
    }

    public bool IsHeld(string action) => _held.TryGetValue(action, out var keys) && keys.Count > 0;

    public float Axis(string negative, string positive) => (IsHeld(positive) ? 1f : 0f) - (IsHeld(negative) ? 1f : 0f);

    public void Clear() => _held.Clear();

    public bool Any => _held.Values.Any(k => k.Count > 0);
}
