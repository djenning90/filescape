using FileScape.Core.Timing;

namespace FileScape.Core.Tests;

/// <summary>
/// The clocks a journey runs on (TEST-14..16, AD-91). <see cref="FrameClock"/> is the game's clock: the sum of the
/// frames' deltas, so under Godot's <c>--fixed-fps 60</c> one frame is exactly 1/60 s of game time however long the
/// machine takes to draw it, and in ordinary play it follows the wall clock. <see cref="JourneyWait"/> is the rule a
/// probe's waits use: <c>Seconds</c>/<c>Hold</c> count game time, and a wait's timeout (a safety net, not an assertion)
/// passes only when both clocks have reached it, so a fast simulation never cuts short a wait for real background work
/// and a slow one never cuts short game time.
/// </summary>
public class JourneyClockTests
{
    [Fact]
    public void Sixty_fixed_frames_are_one_second_to_the_microsecond()
    {
        var clock = new FrameClock();
        for (var i = 0; i < 60; i++) clock.Advance(1.0 / 60);
        // Each 1/60 s frame rounds to the nearest tick (166,667), never truncates (166,666): no deficit accumulates.
        (clock.Now - TimeSpan.FromSeconds(1)).Duration().Should().BeLessThan(TimeSpan.FromMicroseconds(5));
        clock.Now.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_minute_of_fixed_frames_drifts_by_less_than_a_millisecond()
    {
        var clock = new FrameClock();
        for (var i = 0; i < 3600; i++) clock.Advance(1.0 / 60);
        (clock.Now - TimeSpan.FromMinutes(1)).Duration().Should().BeLessThan(TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void A_negative_or_non_finite_delta_does_not_move_the_clock()
    {
        var clock = new FrameClock();
        clock.Advance(0.5);
        clock.Advance(-1);
        clock.Advance(double.NaN);
        clock.Advance(double.PositiveInfinity);
        clock.Now.Should().Be(TimeSpan.FromSeconds(0.5));
    }

    [Fact]
    public void Real_time_waits_time_out_by_the_wall_clock_alone()
    {
        JourneyWait.TimedOut(simulated: TimeSpan.Zero, wall: TimeSpan.FromSeconds(5), timeout: TimeSpan.FromSeconds(5), simulatedTime: false)
            .Should().BeTrue();
        JourneyWait.TimedOut(simulated: TimeSpan.FromSeconds(9), wall: TimeSpan.FromSeconds(4), timeout: TimeSpan.FromSeconds(5), simulatedTime: false)
            .Should().BeFalse();
    }

    [Fact]
    public void A_fast_simulation_keeps_waiting_for_real_background_work()
    {
        // Frames drawn four times faster than real time: 20 s of game time have passed but only 5 s of a listing's.
        JourneyWait.TimedOut(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), simulatedTime: true)
            .Should().BeFalse("the safety net must give real work at least its real-time budget");
        JourneyWait.TimedOut(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), simulatedTime: true)
            .Should().BeTrue();
    }

    [Fact]
    public void A_slow_simulation_keeps_waiting_for_game_time()
    {
        // A loaded machine (or a movie being written): 10 s of wall clock but only 3 s of game time.
        JourneyWait.TimedOut(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), simulatedTime: true)
            .Should().BeFalse("a walk given 5 s of game time must get them however slowly the frames come");
    }

    [Theory]
    [InlineData(1.0 / 60, true)]
    [InlineData(0.01666667, true)] // within half a tick of 1/60 s
    [InlineData(0.0167, false)]
    [InlineData(0.033, false)]
    public void A_fixed_step_is_recognised_to_the_tick(double delta, bool fixedStep)
    {
        JourneyWait.IsFixedStep(delta, framesPerSecond: 60).Should().Be(fixedStep);
    }
}
