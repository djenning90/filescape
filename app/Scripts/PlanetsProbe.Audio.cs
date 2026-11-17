using System.Diagnostics;
using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-audio-gaps</c> (FOLLOW-2, 2026-11-17): DJ hears gaps when the app opens and while moving,
/// dragging or resizing the window. Measures, phase by phase, the generator's underruns (<c>SkyAudio.Skips</c>, each an
/// audible gap), the least audio left queued (<c>QueueLowSeconds</c>; 0 = it ran dry) and the longest gap between frames:
/// startup (everything since playback began), steady play, a camera drag, rapid travel, main-loop stalls of
/// 60…500 ms (what the Windows move/resize loop does to the frame callback; the real move and resize are played by
/// <c>tools/os-planets-probe.ps1 -Audio</c>), and mute then restore. The checks hold the fixed behaviour: no gaps.
/// </summary>
public partial class PlanetsProbe
{
    private double _maxFrameGapMs;
    private readonly Stopwatch _frameClock = new();

    /// <summary>The stalls simulated on the main thread, milliseconds.</summary>
    private static readonly int[] StallMs = [60, 120, 250, 500];

    private async Task AudioGaps()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 40);
        await WaitSettled();
        await Seconds(3);
        var a = P.Audio;
        Log(F($"  startup: {a.Skips} skips since playback began, {a.AudibleSkips} after the first sound; app's last hitch {App.LastHitchMs:0} ms"));
        foreach (var line in a.SkipLog) Log("    skip " + line);
        Check(a.AudibleSkips == 0, $"startup: no gaps from the first sound to the settled world ({a.AudibleSkips} after it; {a.Skips - a.AudibleSkips} empty blocks as playback started)");

        await Phase("steady play, 10 s standing", () => Seconds(10));
        await Phase("a camera drag, 4 s", async () => { for (var i = 0; i < 8; i++) await DragCamera(new Vector2(240, 0)); });
        var root = App.Navigator.Current;
        var kids = P.Composition!.Launchers.Where(l => !l.IsParent && Tree.Contains(l.TargetId)).Select(l => l.TargetId).Take(3).ToList();
        await Phase("rapid travel (three hops out and back)", async () =>
        {
            foreach (var k in kids)
            {
                App.Selection.Select(k); // [direct] as a click on its world
                await Frames(2);
                await Key(Godot.Key.G);
                await Until(() => App.Navigator.Current == k && !P.Busy, 15);
                await Key(Godot.Key.Backspace);
                await Until(() => App.Navigator.Current == root && !P.Busy, 15);
            }
        });
        foreach (var ms in StallMs)
        {
            await Phase($"a {ms} ms stall of the main loop", async () =>
            {
                await Frames(5);
                OS.DelayMsec(ms); // [direct] the frame callback stops, as in the Windows move/resize loop
                await Seconds(1);
            });
        }

        await Phase("mute, 2 s, sound back", async () =>
        {
            await Key(Godot.Key.N);
            await Seconds(2);
            await Key(Godot.Key.N);
            await Seconds(2);
        });
    }

    /// <summary>One measured phase: skips added, the least queued, the longest frame gap; a gap fails the check.</summary>
    private async Task Phase(string what, Func<Task> act)
    {
        var a = P.Audio;
        var skips = a.Skips;
        a.ResetQueueLow();
        _maxFrameGapMs = 0;
        _frameClock.Restart();
        await act();
        var added = a.Skips - skips;
        Log(F($"  {what}: +{added} skips, least queued {(a.QueueLowSeconds == double.MaxValue ? 0 : a.QueueLowSeconds * 1000):0} ms, longest frame gap {_maxFrameGapMs:0} ms"));
        Check(added == 0, $"{what}: no gaps (+{added} skips)");
    }

    /// <summary>Called every frame by the probe: the longest time between frames in the current phase.</summary>
    private void TrackFrameGap()
    {
        if (!_frameClock.IsRunning) return;
        _maxFrameGapMs = Math.Max(_maxFrameGapMs, _frameClock.Elapsed.TotalMilliseconds);
        _frameClock.Restart();
    }
}
