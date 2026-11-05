using System.Numerics;
using FileScape.Core.Units;

namespace FileScape.Core.Interaction;

/// <summary>
/// Engine-independent camera navigation math (NAV-01, NAV-07..NAV-10), kept here so it can be unit-tested.
/// <para>
/// Conventions (docs/units.md): world space, Y up, right-handed; at yaw 0 the camera looks along −Z ("north" in
/// Places), and a positive yaw turns it left (counter-clockwise seen from above). Look pitch is positive looking up.
/// There is never any roll. Movement input is (x = right, y = up, z = forward), each component in −1..1.
/// </para>
/// <para>
/// Distances and speeds here are in <b>world units</b>: metres in Places, abstract layout units in the Classic FSN and
/// District maps. They are therefore plain floats named "…WorldUnits", not <see cref="Meters"/>.
/// </para>
/// </summary>
public static class CameraMath
{
    /// <summary>The look-pitch limits: close to vertical, never past it, so the view cannot flip over.</summary>
    public static readonly Degrees MinLookPitch = Degrees.Of(-85f), MaxLookPitch = Degrees.Of(85f);

    /// <summary>Flight speed limits, world units per second (orbit-fly in the maps; Places flight uses the same clamp).</summary>
    public const float MinFlySpeedWorldUnitsPerSecond = 0.5f, MaxFlySpeedWorldUnitsPerSecond = 3000f;

    /// <summary>Flight speed is multiplied by this per wheel notch (dimensionless): 3 notches ≈ ×1.95.</summary>
    public const float WheelSpeedFactorPerNotch = 1.25f;

    /// <summary>The unit view direction for a yaw and look pitch.</summary>
    public static Vector3 Forward(Degrees yaw, Degrees lookPitch)
    {
        var y = yaw.ToRadians();
        var p = lookPitch.ToRadians();
        return new Vector3(-MathF.Sin(y) * MathF.Cos(p), MathF.Sin(p), -MathF.Cos(y) * MathF.Cos(p));
    }

    /// <summary>The unit heading on the ground plane (pitch ignored): walking and ground movement use this.</summary>
    public static Vector3 HorizontalForward(Degrees yaw) => Forward(yaw, Degrees.Zero);

    /// <summary>The unit vector to the camera's right on the ground plane.</summary>
    public static Vector3 Right(Degrees yaw)
    {
        var y = yaw.ToRadians();
        return new Vector3(MathF.Cos(y), 0, -MathF.Sin(y));
    }

    /// <summary>Limits combined input to unit length, so diagonals are not faster than straight moves.</summary>
    public static Vector3 ClampInput(Vector3 input) => input.LengthSquared() > 1f ? Vector3.Normalize(input) : input;

    /// <summary>
    /// Orbit-mode keyboard velocity (world units per second): heading-relative over the ground plane, plus world
    /// vertical (Q/E).
    /// </summary>
    public static Vector3 GroundVelocity(Vector3 input, Degrees yaw, float speedWorldUnitsPerSecond) =>
        Compose(input, HorizontalForward(yaw), Right(yaw), speedWorldUnitsPerSecond);

    /// <summary>The displacement (world units) of <see cref="GroundVelocity"/> over <paramref name="dt"/>.</summary>
    public static Vector3 GroundDelta(Vector3 input, Degrees yaw, float speedWorldUnitsPerSecond, TimeSpan dt) =>
        GroundVelocity(input, yaw, speedWorldUnitsPerSecond) * dt.ToSecondsF();

    /// <summary>
    /// Fly-mode velocity (world units per second): forward/back along the view direction (pitch included), horizontal
    /// strafe, world-vertical Q/E.
    /// </summary>
    public static Vector3 FlyVelocity(Vector3 input, Degrees yaw, Degrees lookPitch, float speedWorldUnitsPerSecond) =>
        Compose(input, Forward(yaw, lookPitch), Right(yaw), speedWorldUnitsPerSecond);

    /// <summary>The displacement (world units) of <see cref="FlyVelocity"/> over <paramref name="dt"/>.</summary>
    public static Vector3 FlyDelta(Vector3 input, Degrees yaw, Degrees lookPitch, float speedWorldUnitsPerSecond, TimeSpan dt) =>
        FlyVelocity(input, yaw, lookPitch, speedWorldUnitsPerSecond) * dt.ToSecondsF();

    /// <summary>
    /// Combines the axes in world space, then normalizes. A pitched forward vector and world up are not
    /// orthogonal, so normalizing the input alone would make some combinations slower or faster than a straight
    /// move. The resulting speed is speed × min(1, |input|).
    /// </summary>
    private static Vector3 Compose(Vector3 input, Vector3 forward, Vector3 right, float speed)
    {
        var world = forward * input.Z + right * input.X + Vector3.UnitY * input.Y;
        var len = world.Length();
        if (len < 1e-6f)
        {
            return Vector3.Zero;
        }

        return world / len * MathF.Min(1f, input.Length()) * speed;
    }

    public static Degrees ClampLookPitch(Degrees pitch) => pitch.Clamp(MinLookPitch, MaxLookPitch);

    /// <summary>
    /// Adjusts fly speed (world units per second) by wheel notches (positive = faster), within useful bounds. Notches
    /// may be fractional: high-resolution wheels and touchpads report partial notches, and fast spins several at once.
    /// </summary>
    public static float StepFlySpeed(float speedWorldUnitsPerSecond, float notches) =>
        Math.Clamp(speedWorldUnitsPerSecond * MathF.Pow(WheelSpeedFactorPerNotch, notches), MinFlySpeedWorldUnitsPerSecond, MaxFlySpeedWorldUnitsPerSecond);

    /// <summary>
    /// The starting fly speed (world units per second) for an orbit distance (world units): 0.6 per second of the
    /// distance, so crossing what you were looking at takes about 1.7 s at any scale; clamped to 2..400.
    /// </summary>
    public static float InitialFlySpeed(float orbitDistanceWorldUnits) => Math.Clamp(orbitDistanceWorldUnits * 0.6f, 2f, 400f);

    /// <summary>The camera position in orbit mode: on a sphere of <paramref name="distanceWorldUnits"/> around the target, opposite the look direction.</summary>
    public static Vector3 OrbitPosition(Vector3 target, Degrees yaw, Degrees lookPitch, float distanceWorldUnits) =>
        target - Forward(yaw, lookPitch) * distanceWorldUnits;

    /// <summary>The orbit target that keeps a given camera pose exactly unchanged, used for continuous fly → orbit transitions.</summary>
    public static Vector3 TargetFromPose(Vector3 position, Degrees yaw, Degrees lookPitch, float distanceWorldUnits) =>
        position + Forward(yaw, lookPitch) * distanceWorldUnits;

    /// <summary>
    /// The highest look pitch (least downward) an orbit may use before the camera would pass below
    /// <paramref name="minCameraHeight"/> (world units). This prevents the ground clamp from moving the camera off its sphere.
    /// </summary>
    public static Degrees MaxOrbitLookPitchAboveGround(float targetHeight, float distanceWorldUnits, float minCameraHeight)
    {
        // camera.y = target.y − sin(p)·d ≥ h  ⇒  sin(p) ≤ (target.y − h) / d
        var s = (targetHeight - minCameraHeight) / MathF.Max(distanceWorldUnits, 1e-3f);
        return s >= 1 ? MaxLookPitch : s <= -1 ? MinLookPitch : Degrees.Min(MaxLookPitch, Degrees.FromRadians(MathF.Asin(s)));
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
