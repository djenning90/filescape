using System.Globalization;

namespace FileScape.Core.Units;

// Lightweight physical quantities (docs/units.md). Each is a readonly struct around one float, so it is as cheap as
// the float it replaces (no allocation, inlined operators). Only dimensionally meaningful operations exist:
//   Meters ± Meters, Meters × ratio, Meters / Meters → ratio, Meters / TimeSpan → MetersPerSecond,
//   MetersPerSecond × TimeSpan → Meters, MetersPerSecondSquared × TimeSpan → MetersPerSecond,
//   Degrees ± Degrees, DegreesPerSecond × TimeSpan → Degrees, PerSecond → smoothing factors and time constants.
// There is no implicit conversion back to float: use .Value (or a named conversion such as ToRadians) at engine
// and math boundaries, where the unit is then stated by the receiving name.
//
// Durations are System.TimeSpan (already a value type with no implicit conversion to a number).
//
// Values: quantities are signed where the physics is (a vertical velocity, a displacement, a yaw). Constructors do
// not reject NaN or infinity — frame-loop arithmetic must stay branch-free — so configuration objects validate their
// own values (WalkParams.Validate) and IsFinite is available for checks.

/// <summary>A length in metres (Places world space: 1 world unit = 1 m). Signed: may be a displacement.</summary>
public readonly record struct Meters(float Value) : IComparable<Meters>
{
    public static readonly Meters Zero = default;

    public static Meters Of(float meters) => new(meters);

    public bool IsFinite => float.IsFinite(Value);

    public static Meters operator +(Meters a, Meters b) => new(a.Value + b.Value);
    public static Meters operator -(Meters a, Meters b) => new(a.Value - b.Value);
    public static Meters operator -(Meters a) => new(-a.Value);
    public static Meters operator *(Meters a, float ratio) => new(a.Value * ratio);
    public static Meters operator *(float ratio, Meters a) => new(a.Value * ratio);
    public static Meters operator /(Meters a, float divisor) => new(a.Value / divisor);

    /// <summary>The dimensionless ratio of two lengths.</summary>
    public static float operator /(Meters a, Meters b) => a.Value / b.Value;

    public static MetersPerSecond operator /(Meters distance, TimeSpan duration) => new(distance.Value / (float)duration.TotalSeconds);

    public static bool operator <(Meters a, Meters b) => a.Value < b.Value;
    public static bool operator >(Meters a, Meters b) => a.Value > b.Value;
    public static bool operator <=(Meters a, Meters b) => a.Value <= b.Value;
    public static bool operator >=(Meters a, Meters b) => a.Value >= b.Value;

    public int CompareTo(Meters other) => Value.CompareTo(other.Value);

    public static Meters Min(Meters a, Meters b) => a.Value <= b.Value ? a : b;
    public static Meters Max(Meters a, Meters b) => a.Value >= b.Value ? a : b;
    public Meters Abs() => new(MathF.Abs(Value));

    public override string ToString() => Value.ToString("0.###", CultureInfo.InvariantCulture) + " m";
}

/// <summary>A speed (a magnitude, or a signed component such as a vertical velocity) in metres per second.</summary>
public readonly record struct MetersPerSecond(float Value) : IComparable<MetersPerSecond>
{
    public static readonly MetersPerSecond Zero = default;

    public static MetersPerSecond Of(float metersPerSecond) => new(metersPerSecond);

    public bool IsFinite => float.IsFinite(Value);

    public static MetersPerSecond operator +(MetersPerSecond a, MetersPerSecond b) => new(a.Value + b.Value);
    public static MetersPerSecond operator -(MetersPerSecond a, MetersPerSecond b) => new(a.Value - b.Value);
    public static MetersPerSecond operator -(MetersPerSecond a) => new(-a.Value);
    public static MetersPerSecond operator *(MetersPerSecond a, float ratio) => new(a.Value * ratio);
    public static MetersPerSecond operator *(float ratio, MetersPerSecond a) => new(a.Value * ratio);
    public static float operator /(MetersPerSecond a, MetersPerSecond b) => a.Value / b.Value;

    public static Meters operator *(MetersPerSecond speed, TimeSpan duration) => new(speed.Value * (float)duration.TotalSeconds);
    public static Meters operator *(TimeSpan duration, MetersPerSecond speed) => speed * duration;
    public static MetersPerSecondSquared operator /(MetersPerSecond speed, TimeSpan duration) => new(speed.Value / (float)duration.TotalSeconds);

    public static bool operator <(MetersPerSecond a, MetersPerSecond b) => a.Value < b.Value;
    public static bool operator >(MetersPerSecond a, MetersPerSecond b) => a.Value > b.Value;
    public static bool operator <=(MetersPerSecond a, MetersPerSecond b) => a.Value <= b.Value;
    public static bool operator >=(MetersPerSecond a, MetersPerSecond b) => a.Value >= b.Value;

    public int CompareTo(MetersPerSecond other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("0.###", CultureInfo.InvariantCulture) + " m/s";
}

/// <summary>An acceleration in metres per second squared (gravity is a positive magnitude acting downwards).</summary>
public readonly record struct MetersPerSecondSquared(float Value)
{
    public static MetersPerSecondSquared Of(float metersPerSecondSquared) => new(metersPerSecondSquared);

    public bool IsFinite => float.IsFinite(Value);

    public static MetersPerSecond operator *(MetersPerSecondSquared a, TimeSpan duration) => new(a.Value * (float)duration.TotalSeconds);

    public override string ToString() => Value.ToString("0.###", CultureInfo.InvariantCulture) + " m/s²";
}

/// <summary>
/// An angle in degrees. Sign conventions are those of the API that uses it (camera yaw: positive turns left; place
/// angles: clockwise from north) — see docs/units.md. Degrees are not normalised automatically: a yaw accumulates
/// turns, and <see cref="Wrapped"/> gives the equivalent in (-180, 180] when a short-way difference is needed.
/// </summary>
public readonly record struct Degrees(float Value) : IComparable<Degrees>
{
    private const float RadiansPerDegree = MathF.PI / 180f;

    public static readonly Degrees Zero = default;

    public static Degrees Of(float degrees) => new(degrees);

    public static Degrees FromRadians(float radians) => new(radians / RadiansPerDegree);

    /// <summary>The angle in radians, for trigonometry (System.MathF and Godot rotations take radians).</summary>
    public float ToRadians() => Value * RadiansPerDegree;

    public bool IsFinite => float.IsFinite(Value);

    /// <summary>The same direction expressed in (-180, 180].</summary>
    public Degrees Wrapped()
    {
        var v = Value % 360f;
        if (v > 180f) v -= 360f;
        else if (v <= -180f) v += 360f;
        return new(v);
    }

    public Degrees Clamp(Degrees min, Degrees max) => new(Math.Clamp(Value, min.Value, max.Value));

    public static Degrees Min(Degrees a, Degrees b) => a.Value <= b.Value ? a : b;
    public static Degrees Max(Degrees a, Degrees b) => a.Value >= b.Value ? a : b;

    public static Degrees operator +(Degrees a, Degrees b) => new(a.Value + b.Value);
    public static Degrees operator -(Degrees a, Degrees b) => new(a.Value - b.Value);
    public static Degrees operator -(Degrees a) => new(-a.Value);
    public static Degrees operator *(Degrees a, float ratio) => new(a.Value * ratio);
    public static Degrees operator *(float ratio, Degrees a) => new(a.Value * ratio);
    public static DegreesPerSecond operator /(Degrees angle, TimeSpan duration) => new(angle.Value / (float)duration.TotalSeconds);

    public static bool operator <(Degrees a, Degrees b) => a.Value < b.Value;
    public static bool operator >(Degrees a, Degrees b) => a.Value > b.Value;
    public static bool operator <=(Degrees a, Degrees b) => a.Value <= b.Value;
    public static bool operator >=(Degrees a, Degrees b) => a.Value >= b.Value;

    public int CompareTo(Degrees other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("0.###", CultureInfo.InvariantCulture) + "°";
}

/// <summary>An angular speed in degrees per second (e.g. how fast the arrow keys turn the heading).</summary>
public readonly record struct DegreesPerSecond(float Value)
{
    public static DegreesPerSecond Of(float degreesPerSecond) => new(degreesPerSecond);

    public static Degrees operator *(DegreesPerSecond rate, TimeSpan duration) => new(rate.Value * (float)duration.TotalSeconds);
    public static Degrees operator *(TimeSpan duration, DegreesPerSecond rate) => rate * duration;
    public static DegreesPerSecond operator *(DegreesPerSecond rate, float ratio) => new(rate.Value * ratio);

    public override string ToString() => Value.ToString("0.###", CultureInfo.InvariantCulture) + "°/s";
}

/// <summary>
/// A rate in inverse seconds, used for exponential smoothing: each frame a value moves the fraction
/// <c>1 − e^(−rate·Δt)</c> of the way to its target (<see cref="SmoothingFactor"/>), which is frame-rate independent.
/// After one <see cref="TimeConstant"/> (1/rate) ≈ 63 % of a step change is done, after 2.3 time constants 90 %, after
/// 4.6 time constants 99 %. A higher rate responds faster; zero never moves.
/// </summary>
public readonly record struct PerSecond(float Value)
{
    public static PerSecond Of(float perSecond) => new(perSecond);

    public bool IsFinite => float.IsFinite(Value);

    /// <summary>The fraction (0..1) of the remaining difference to close in <paramref name="dt"/>.</summary>
    public float SmoothingFactor(TimeSpan dt) => 1f - MathF.Exp(-Value * (float)dt.TotalSeconds);

    /// <summary>The fraction for a step given in seconds (for inner loops that already work in float seconds).</summary>
    public float SmoothingFactor(float dtSeconds) => 1f - MathF.Exp(-Value * dtSeconds);

    /// <summary>1/rate: the time to close ≈ 63 % of a step change.</summary>
    public TimeSpan TimeConstant => TimeSpan.FromSeconds(1.0 / Value);

    public override string ToString() => Value.ToString("0.###", CultureInfo.InvariantCulture) + "/s";
}

/// <summary>Creating durations from computed seconds.</summary>
public static class Durations
{
    /// <summary>
    /// A duration from seconds, rounded to the nearest 100 ns tick. Use this for computed values such as a frame's delta:
    /// TimeSpan.FromSeconds truncates (1/60 s becomes 166,666 ticks), and the deficit accumulates — a 120 Hz integrator
    /// fed 60 Hz frames then lags one step behind (found by the golden behaviour test, 2026-11-05).
    /// </summary>
    public static TimeSpan FromSeconds(double seconds) => TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
}

/// <summary>Explicit duration helpers at math boundaries.</summary>
public static class DurationExtensions
{
    /// <summary>The duration in seconds as a float, for inner-loop arithmetic whose names then say "Seconds".</summary>
    public static float ToSecondsF(this TimeSpan duration) => (float)duration.TotalSeconds;
}
