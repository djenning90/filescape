using FileScape.Core.Units;

namespace FileScape.Core.Timing;

/// <summary>
/// The game clock (TEST-14, AD-91): the sum of the frames' deltas. Everything the player sees move or expire by time
/// (the world's clock <c>IWorldSession.Now</c>, the HUD's toasts and hints) reads this, never the wall clock, so the
/// game is frame-rate independent and a journey run under Godot's <c>--fixed-fps 60</c> plays out the same however
/// long the machine takes per frame. In ordinary play the deltas are measured from the wall clock, so it follows it.
/// What a change must preserve: each delta is rounded to the nearest tick (<see cref="Durations.FromSeconds"/>), so
/// 60 frames of 1/60 s make one second to within 2 µs and never fall short of it; a non-finite or negative delta does not move the clock.
/// Budgets for work inside one frame and hitch diagnostics are wall-clock measurements and stay on a Stopwatch.
/// </summary>
public sealed class FrameClock
{
    /// <summary>Game time since the clock started.</summary>
    public TimeSpan Now { get; private set; }

    /// <summary>Adds one frame's delta (seconds, as Godot passes it to <c>_Process</c>).</summary>
    public void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0) return;
        Now += Durations.FromSeconds(deltaSeconds);
    }
}

/// <summary>
/// How an in-app journey's waits measure time (TEST-14, AD-91). Under simulated time (<c>--sim-clock</c> with the engine
/// argument <c>--fixed-fps 60</c>, or a movie being written) a probe's <c>Seconds</c> and <c>Hold</c> count game time,
/// so a walk of two seconds is two seconds of the game's movement on a loaded or an idle machine alike, and frames
/// may run faster than real time. A wait's timeout is a safety net for a condition, not an assertion: it passes only
/// when both the game time and the wall clock have reached it. A fast simulation then still gives real background work
/// (a listing, a thumbnail, a delayed folder) at least its real-time budget, and a slow one still gives the game its
/// game time. In real time (an ordinary run) the wall clock alone decides, as before.
/// What a change must preserve: a timeout under simulated time is never shorter than the same timeout in real time on
/// either clock.
/// </summary>
public static class JourneyWait
{
    /// <summary>Whether a wait started <paramref name="simulated"/> game time and <paramref name="wall"/> wall time ago has timed out.</summary>
    public static bool TimedOut(TimeSpan simulated, TimeSpan wall, TimeSpan timeout, bool simulatedTime) =>
        simulatedTime ? simulated >= timeout && wall >= timeout : wall >= timeout;

    /// <summary>
    /// True when a frame's delta (seconds) is the fixed step of <paramref name="framesPerSecond"/>, to the nearest tick:
    /// how a probe confirms that <c>--fixed-fps</c> reached the engine before it trusts game time.
    /// </summary>
    public static bool IsFixedStep(double deltaSeconds, int framesPerSecond) =>
        Durations.FromSeconds(deltaSeconds) == Durations.FromSeconds(1.0 / framesPerSecond);
}
