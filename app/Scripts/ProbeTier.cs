using System.Collections.Concurrent;
using Godot;

namespace FileScape.App;

/// <summary>
/// TEST-01 (AD-29): verification tiers for the in-app probes. <c>--tier=functional</c> runs every assertion but
/// leaves out measurement-only segments (frame-time recordings), logging each one it leaves out, so a functional run
/// is visibly not a performance run. Screenshots are encoded and written on worker threads in every tier; a probe
/// waits for them before it quits.
/// </summary>
public static class ProbeTier
{
    /// <summary>True for <c>--tier=functional</c>: measurement-only segments are left out (<see cref="Measure"/>).</summary>
    public static readonly bool Functional = OS.GetCmdlineUserArgs().Any(a => a == "--tier=functional");

    private static readonly ConcurrentBag<Task> Saves = [];

    /// <summary>Saves the captured image off the main thread (PNG encoding was 0.2–0.5 s of frame stall per shot).</summary>
    public static void SaveAsync(Image img, string path) => Saves.Add(Task.Run(() => img.SavePng(path)));

    /// <summary>
    /// Blocks until every screenshot is on disk, or <see cref="SaveTimeout"/> passes (a stuck save must not keep the
    /// probe from quitting; the evidence would then be short a picture, which the reviewer sees).
    /// </summary>
    public static void WaitForSaves() => Task.WaitAll([.. Saves], SaveTimeout);

    /// <summary>The longest a finishing probe waits for its screenshots (generous: a save takes well under a second).</summary>
    private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(30);

    /// <summary>True when the measurement should run; otherwise logs that it was left out and why.</summary>
    public static bool Measure(string label, Action<string> log)
    {
        if (!Functional) return true;
        log($"SKIP perf {label}: measurement only, left out of the functional tier (run the release tier for timings)");
        return false;
    }
}
