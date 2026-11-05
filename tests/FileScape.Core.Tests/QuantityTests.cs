using FileScape.Core.Places;
using FileScape.Core.Units;

namespace FileScape.Core.Tests;

/// <summary>The physical quantity types (docs/units.md): conversions, dimensionally correct operations, edge values.</summary>
public class QuantityTests
{
    [Fact]
    public void Distance_over_time_is_speed_and_speed_over_time_is_distance()
    {
        var speed = Meters.Of(10) / TimeSpan.FromSeconds(4);
        speed.Should().Be(MetersPerSecond.Of(2.5f));
        (MetersPerSecond.Of(4.2f) * TimeSpan.FromSeconds(2)).Value.Should().BeApproximately(8.4f, 1e-6f);
        (TimeSpan.FromSeconds(2) * MetersPerSecond.Of(4.2f)).Value.Should().BeApproximately(8.4f, 1e-6f);
        (MetersPerSecondSquared.Of(17) * TimeSpan.FromSeconds(0.5)).Should().Be(MetersPerSecond.Of(8.5f));
        (MetersPerSecond.Of(10) / TimeSpan.FromSeconds(2)).Should().Be(MetersPerSecondSquared.Of(5));
        (Meters.Of(3) / Meters.Of(4)).Should().Be(0.75f, "a ratio of lengths has no unit");
    }

    [Fact]
    public void Compatible_quantities_add_and_scale()
    {
        (Meters.Of(1.6f) + Meters.Of(1.75f)).Value.Should().BeApproximately(3.35f, 1e-6f);
        (Meters.Of(1) - Meters.Of(3)).Should().Be(Meters.Of(-2), "lengths can be signed displacements");
        (Meters.Of(2) * 3).Should().Be(Meters.Of(6));
        (-MetersPerSecond.Of(5.3f)).Value.Should().Be(-5.3f, "a vertical velocity is signed (down is negative)");
        Meters.Max(Meters.Of(1), Meters.Of(2)).Should().Be(Meters.Of(2));
        (Meters.Of(1) < Meters.Of(2)).Should().BeTrue();
    }

    [Fact]
    public void Degrees_convert_to_radians_explicitly_and_wrap_on_request()
    {
        Degrees.Of(180).ToRadians().Should().BeApproximately(MathF.PI, 1e-6f);
        Degrees.FromRadians(MathF.PI / 2).Value.Should().BeApproximately(90, 1e-4f);
        Degrees.Of(370).Wrapped().Value.Should().BeApproximately(10, 1e-4f);
        Degrees.Of(-190).Wrapped().Value.Should().BeApproximately(170, 1e-4f);
        Degrees.Of(180).Wrapped().Should().Be(Degrees.Of(180));
        Degrees.Of(-180).Wrapped().Should().Be(Degrees.Of(180), "(-180, 180]");
        Degrees.Of(725).Value.Should().Be(725, "a yaw accumulates turns until asked to wrap");
        (DegreesPerSecond.Of(120) * TimeSpan.FromSeconds(1.5)).Should().Be(Degrees.Of(180));
        Degrees.Of(400).Clamp(Degrees.Of(-85), Degrees.Of(85)).Should().Be(Degrees.Of(85));
    }

    [Fact]
    public void A_smoothing_rate_gives_a_frame_rate_independent_fraction()
    {
        var rate = PerSecond.Of(14);
        rate.SmoothingFactor(TimeSpan.Zero).Should().Be(0);
        rate.SmoothingFactor(TimeSpan.FromSeconds(1 / 14.0)).Should().BeApproximately(1 - 1 / MathF.E, 1e-5f, "one time constant closes 63 %");
        rate.TimeConstant.TotalSeconds.Should().BeApproximately(1 / 14.0, 1e-7);
        // Two frames of 1/120 s close the same fraction as one frame of 1/60 s.
        var two = 1 - (1 - rate.SmoothingFactor(1 / 120f)) * (1 - rate.SmoothingFactor(1 / 120f));
        two.Should().BeApproximately(rate.SmoothingFactor(1 / 60f), 1e-6f);
        PerSecond.Of(0).SmoothingFactor(TimeSpan.FromSeconds(10)).Should().Be(0, "a zero rate never moves");
    }

    [Fact]
    public void Computed_seconds_round_to_the_nearest_tick()
    {
        TimeSpan.FromSeconds(1 / 60.0).Ticks.Should().Be(166_666, "the framework truncates — the trap Durations avoids");
        Durations.FromSeconds(1 / 60.0).Ticks.Should().Be(166_667);
        Durations.FromSeconds(1 / 120.0).Ticks.Should().Be(83_333);
        Durations.FromSeconds(0.25).Should().Be(TimeSpan.FromMilliseconds(250));
        Durations.FromSeconds(-0.5).Should().Be(TimeSpan.FromSeconds(-0.5));
    }

    [Fact]
    public void Non_finite_values_are_detectable_and_rejected_by_configuration()
    {
        Meters.Of(float.NaN).IsFinite.Should().BeFalse();
        MetersPerSecond.Of(float.PositiveInfinity).IsFinite.Should().BeFalse();
        Degrees.Of(30).IsFinite.Should().BeTrue();
        var bad = () => new Walker(new WalkParams { EyeHeight = Meters.Of(float.NaN) });
        bad.Should().Throw<ArgumentOutOfRangeException>();
        var zeroRadius = () => new Walker(new WalkParams { Radius = Meters.Zero });
        zeroRadius.Should().Throw<ArgumentOutOfRangeException>();
        var negativeStep = () => new Walker(new WalkParams { StepHeight = Meters.Of(-0.1f) });
        negativeStep.Should().Throw<ArgumentOutOfRangeException>();
        var deepFall = () => new Walker(new WalkParams { FallLimit = Meters.Of(-1000) });
        deepFall.Should().NotThrow("a negative fall limit is a legitimate world height");
    }

    [Fact]
    public void Quantities_print_with_their_unit()
    {
        Meters.Of(1.75f).ToString().Should().Be("1.75 m");
        MetersPerSecond.Of(4.2f).ToString().Should().Be("4.2 m/s");
        Degrees.Of(-85).ToString().Should().Be("-85°");
        PerSecond.Of(14).ToString().Should().Be("14/s");
    }
}
