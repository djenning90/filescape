using System.Diagnostics;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-stalls</c> (PLANET-178..180, AD-96): the seventh pass's stall routes, played with injected input
/// (never real OS input) and measured by wall clock per frame, so a fix is judged on the frame-time distribution the
/// player sees. Runs on a fixture or, with <c>--counts-only</c>, on a real root: only counts and timings are logged, never
/// a name, and no screenshot is taken. Pair it with <c>--diag-frames=PATH</c> to attribute the long frames
/// (<c>tools/frame-diag-summary.py</c>); the probe labels each route as the frame log's segment.
/// <list type="bullet">
/// <item><c>--stalls-route=walk,land,overview,gpu</c> (any order; default <c>walk,land</c>): <b>walk</b> holds W (a slight
/// turn every few seconds) for <c>--stalls-walk-seconds</c> (default 60) while background discovery lists; <b>land</b>
/// flies to up to three worlds in the sky and back up (G with the world selected, then U: six landings, each followed
/// by 1.5 s of standing, where the second stall used to come); <b>overview</b> opens the overview for 4 s and closes it;
/// <b>gallery</b> walks 20 s among the easels then lands on up to two worlds in its sky and back; <b>gpu</b> stands still
/// and measures the renderer's GPU time with each costly feature off in turn (MSAA, glow, shadows, the sky shader, render
/// scale) to name the dominant GPU cost.</item>
/// <item><c>--stalls-fullscreen</c> plays it in exclusive-less fullscreen (the window covers its screen).</item>
/// <item>The functional check (<c>--tier=functional</c>, AD-96's regression guard): with discovery listing during the walk,
/// no frame longer than <c>--stalls-limit-ms</c> (default 250 ms, a wide budget). Everything else is measurement.</item>
/// </list>
/// Preserve: a route never sends input while the window is unfocused (ProbeBase re-presses held keys after focus
/// returns); the frame list is per route, so one route's stall is never blamed on another.
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>The functional check's budget: no frame longer than this while walking during discovery, milliseconds.
    /// Wide on purpose (a stall DJ saw was 400–1,100 ms; an ordinary frame is 16.7): it catches the regression, not jitter.</summary>
    private const double DefaultStallLimitMs = 250;

    /// <summary>The thresholds the report counts frames over, milliseconds: two 60 Hz frames, a visible hitch, a stall.</summary>
    private static readonly double[] StallThresholdsMs = [33, 100, 300];

    private readonly List<(double AtS, double Ms, string Segment)> _stallFrames = [];
    private string _stallSegment = "";
    private long _stallLastTicks;
    private readonly Stopwatch _stallClock = Stopwatch.StartNew();

    /// <summary>From <see cref="_Process"/>: the wall-clock gap since the last frame, under the current route's label.</summary>
    private void TrackStallFrame()
    {
        var t = Stopwatch.GetTimestamp();
        if (_stallLastTicks != 0 && _stallSegment.Length > 0)
        {
            _stallFrames.Add((_stallClock.Elapsed.TotalSeconds, (t - _stallLastTicks) * 1000.0 / Stopwatch.Frequency, _stallSegment));
        }

        _stallLastTicks = t;
    }

    private void StallSegment(string name)
    {
        _stallSegment = name;
        FrameDiag.Segment = name;
    }

    private async Task StallsRun()
    {
        var routes = (UserArg("stalls-route") ?? "walk,land").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var walkSeconds = double.Parse(UserArg("stalls-walk-seconds") ?? "60", System.Globalization.CultureInfo.InvariantCulture);
        var limitMs = double.Parse(UserArg("stalls-limit-ms") ?? DefaultStallLimitMs.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        MaybeNoVsync();
        if (OS.GetCmdlineUserArgs().Contains("--stalls-fullscreen"))
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            await Seconds(2);
        }

        RenderingServer.ViewportSetMeasureRenderTime(App.GetViewport().GetViewportRid(), true);
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 60);
        await WaitSettled();
        await Seconds(2);
        Log(F($"stalls: routes {string.Join(",", routes)}; window {DisplayServer.WindowGetSize()}; vsync {DisplayServer.WindowGetVsyncMode()}; tree {Tree.Count} entries; {P.Composition!.SkyBodies.Count} bodies in the sky"));
        foreach (var route in routes)
        {
            switch (route)
            {
                case "walk": await StallsWalk("walk", walkSeconds, limitMs); break;
                case "land": await StallsLandings("land", 3); break;
                case "overview": await StallsOverview(); break;
                case "gallery":
                    await StallsWalk("gallery-walk", 20, limitMs: null);
                    await StallsLandings("gallery-land", 2);
                    break;
                case "gpu": await StallsGpu(); break;
                default: Check(false, $"unknown stalls route '{route}'"); break;
            }
        }

        StallSegment("");
        StallReport("all", _stallFrames.Select(f => f.Ms).ToList());
    }

    /// <summary>Walks (W, a 0.4 s turn every 4 s) for <paramref name="seconds"/>; checks the stall budget if discovery listed meanwhile.</summary>
    private async Task StallsWalk(string segment, double seconds, double? limitMs)
    {
        var asked0 = P.PlayDiscoveryRequested;
        var swaps0 = P.RecomposeSwaps;
        var from = _stallFrames.Count;
        StallSegment(segment);
        var clock = Stopwatch.StartNew();
        var turn = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            await Hold(4.0, Godot.Key.W);
            await Hold(0.4, Godot.Key.W, turn++ % 2 == 0 ? Godot.Key.Right : Godot.Key.Left);
        }

        StallSegment("");
        var frames = _stallFrames.Skip(from).Where(f => f.Segment == segment).ToList();
        var asked = P.PlayDiscoveryRequested - asked0;
        StallReport(segment, frames.Select(f => f.Ms).ToList(), F($"; discovery asked {asked} listings; {P.RecomposeSwaps - swaps0} recompositions swapped in"));
        if (limitMs is not { } limit) return;
        var worst = frames.Count > 0 ? frames.Max(f => f.Ms) : 0;
        if (asked == 0) Log(F($"SKIP the stall budget ({limit:0} ms): discovery listed nothing during the walk (nothing left to list?), so the walk did not exercise it"));
        else Check(worst < limit, F($"no frame over {limit:0} ms while walking {seconds:0} s during discovery ({asked} listings asked): longest {worst:0.0} ms"));
    }

    /// <summary>Up to <paramref name="worlds"/> worlds in the sky: G to each (selected), stand 1.5 s, U back, stand 1.5 s.</summary>
    private async Task StallsLandings(string segment, int worlds)
    {
        var root = App.Navigator.Current;
        var comp = P.Composition!;
        var targets = comp.Launchers.Where(l => !l.IsParent && l.CanLaunch && P.HasWorldInSky(l.TargetId)).Select(l => l.TargetId).Distinct().Take(worlds).ToList();
        if (targets.Count == 0)
        {
            Log($"SKIP {segment}: no world in the sky to land on");
            return;
        }

        var landed = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            StallSegment(F($"{segment}-down-{i}"));
            App.Select(targets[i]); // [direct] the selection (a click on its world does the same)
            await Frames(2);
            await Key(Godot.Key.G);
            await Until(() => App.Navigator.Current == targets[i] && !P.Busy, 20);
            if (App.Navigator.Current == targets[i]) landed++;
            await Seconds(1.5);
            StallSegment(F($"{segment}-up-{i}"));
            await Key(Godot.Key.U);
            await Until(() => App.Navigator.Current == root && !P.Busy, 20);
            if (App.Navigator.Current == root) landed++;
            await Seconds(1.5);
        }

        StallSegment("");
        Check(landed == targets.Count * 2, F($"{segment}: {landed} of {targets.Count * 2} landings arrived"));
        var frames = _stallFrames.Where(f => f.Segment.StartsWith(segment + "-", StringComparison.Ordinal)).ToList();
        StallReport(segment, frames.Select(f => f.Ms).ToList());
        foreach (var g in frames.GroupBy(f => f.Segment)) Log(F($"  {g.Key}: max {g.Max(f => f.Ms):0.0} ms, frames over 100 ms {g.Count(f => f.Ms > 100)}"));
    }

    private async Task StallsOverview()
    {
        StallSegment("overview-open");
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 5);
        await Seconds(4);
        StallSegment("overview-close");
        await Key(Godot.Key.Escape);
        await Until(() => !P.UniverseOpen && !P.Busy, 10);
        await Seconds(1);
        StallSegment("");
        foreach (var seg in new[] { "overview-open", "overview-close" }) StallReport(seg, _stallFrames.Where(f => f.Segment == seg).Select(f => f.Ms).ToList());
    }

    /// <summary>The renderer's GPU time standing still, with each costly feature off in turn (then restored).</summary>
    private async Task StallsGpu()
    {
        StallSegment("gpu");
        var vp = App.GetViewport();
        var env = App.GetTree().Root.FindChildren("*", nameof(WorldEnvironment), true, false).OfType<WorldEnvironment>().FirstOrDefault()?.Environment;
        var sun = App.GetTree().Root.FindChildren("*", nameof(DirectionalLight3D), true, false).OfType<DirectionalLight3D>().FirstOrDefault(l => l.Visible);
        var msaa = vp.Msaa3D;
        var scale = vp.Scaling3DScale;
        var variants = new List<(string Name, Action On, Action Off)>
        {
            ("as shipped", () => { }, () => { }),
            ("msaa off", () => vp.Msaa3D = Viewport.Msaa.Disabled, () => vp.Msaa3D = msaa),
            ("glow off", () => { if (env != null) env.GlowEnabled = false; }, () => { if (env != null) env.GlowEnabled = true; }),
            ("shadows off", () => { if (sun != null) sun.ShadowEnabled = false; }, () => { if (sun != null) sun.ShadowEnabled = true; }),
            ("sky shader off", () => { if (env != null) env.BackgroundMode = Godot.Environment.BGMode.Color; }, () => { if (env != null) env.BackgroundMode = Godot.Environment.BGMode.Sky; }),
            ("render scale 0.75", () => vp.Scaling3DScale = 0.75f, () => vp.Scaling3DScale = scale),
            ("render scale 0.5", () => vp.Scaling3DScale = 0.5f, () => vp.Scaling3DScale = scale),
            ("msaa off + scale 0.75", () => { vp.Msaa3D = Viewport.Msaa.Disabled; vp.Scaling3DScale = 0.75f; }, () => { vp.Msaa3D = msaa; vp.Scaling3DScale = scale; }),
        };
        Log(F($"gpu: window {DisplayServer.WindowGetSize()}, msaa {msaa}, render scale {scale:0.00}, glow {env?.GlowEnabled}, shadows {sun?.ShadowEnabled}"));
        foreach (var (name, on, off) in variants)
        {
            on();
            await Frames(20); // let the change take effect (pipelines, buffers)
            var gpu = new List<double>();
            var wall = new List<double>();
            for (var i = 0; i < 120; i++)
            {
                var before = _stallFrames.Count;
                await Frames(1);
                gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp.GetViewportRid()));
                if (_stallFrames.Count > before) wall.Add(_stallFrames[^1].Ms);
            }

            off();
            gpu.Sort();
            wall.Sort();
            Log(F($"  gpu {name}: GPU median {gpu[gpu.Count / 2]:0.00} ms (p90 {gpu[(int)(gpu.Count * 0.9)]:0.00}); frame median {(wall.Count > 0 ? wall[wall.Count / 2] : 0):0.0} ms"));
        }

        StallSegment("");
    }

    /// <summary>Logs one route's distribution: p50/p95/p99/max and the counts over 33/100/300 ms.</summary>
    private void StallReport(string what, List<double> raw, string extra = "")
    {
        if (raw.Count < 10)
        {
            Log($"stalls {what}: too few frames ({raw.Count})");
            return;
        }

        var ms = raw.OrderBy(x => x).ToList();
        double P(double q) => ms[Math.Min(ms.Count - 1, (int)(ms.Count * q))];
        var over = string.Join(", ", StallThresholdsMs.Select(t => F($">{t:0} ms {raw.Count(x => x > t)}")));
        Log(F($"stalls {what}: {ms.Count} frames, p50 {P(0.5):0.0} ms, p95 {P(0.95):0.0}, p99 {P(0.99):0.0}, max {ms[^1]:0.0}; {over}{extra}"));
    }
}
