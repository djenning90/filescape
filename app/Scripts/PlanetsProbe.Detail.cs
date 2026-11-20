using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App;

/// <summary>
/// The fifth playtest follow-up's journeys (PLANET-90..93, AD-77), each runnable on a fixture or, with
/// <c>--counts-only</c>, on a real root (<c>C:\</c>, <c>\\wsl.localhost\Ubuntu-24.04\</c>): only counts and timings are
/// logged then, never a folder name, and no screenshot is taken.
/// <list type="bullet">
/// <item><c>--automate=planets-play-discovery</c>: walks round the arrival world for 30 s with the overview closed, logging
/// frame times (p50/p95/max) and what background discovery listed at 10 and 30 s, then opens the overview and counts
/// what it shows. Run it with and without <c>--test-play-discovery=off</c> (and <c>--no-vsync</c> for frame cost).</item>
/// <item><c>--automate=planets-overview-navigate</c>: opens the overview, lets discovery fill it for 12 s, measures what
/// is drawn (worlds, folded, overlapping disc pairs, draw calls, frame times), then plays the navigation: the controls
/// hint, [ and ] round a ring, Z up a level, the wheel toward a world under the pointer, F; and logs a fingerprint of the
/// worlds' looks (equal between runs on the same tree: the looks are stable). <c>--test-overview-detail=off</c> and
/// <c>--test-overview-look=plain</c> give the before picture in the same build.</item>
/// </list>
/// </summary>
public partial class PlanetsProbe
{
    private readonly List<double> _playFrameMs = [];
    private bool _playTracking;

    /// <summary>From <see cref="_Process"/> (via <see cref="TrackDiscoveryFrame"/>'s caller): frame times while walking.</summary>
    private void TrackPlayFrame(double delta)
    {
        if (_playTracking && !P.UniverseOpen) _playFrameMs.Add(delta * 1000);
    }

    /// <summary>--no-vsync (an app argument): frame times then show the work done, not the 60 Hz wait (cost comparisons).</summary>
    private static void MaybeNoVsync()
    {
        if (!System.Environment.GetCommandLineArgs().Concat(OS.GetCmdlineUserArgs()).Any(a => a == "--no-vsync")) return;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
    }

    private static string FrameStats(List<double> raw)
    {
        if (raw.Count < 10) return "too few frames";
        var ms = raw.OrderBy(x => x).ToList();
        return F($"{ms.Count} frames, p50 {ms[ms.Count / 2]:0.0} ms, p95 {ms[(int)(ms.Count * 0.95)]:0.0} ms, p99 {ms[(int)(ms.Count * 0.99)]:0.0} ms, max {ms[^1]:0.0} ms");
    }

    /// <summary>Folders below <paramref name="root"/> listed now, and the deepest level listed (0 = the root).</summary>
    private (int Listed, int Deepest) ListedBelow(int root)
    {
        var listed = 0;
        var deepest = 0;
        var rootDepth = Tree[root].Depth;
        foreach (var n in Tree.Nodes)
        {
            if (!n.IsDirectory || n.ScanState != DirScanState.Complete || n.Id == root || !IsUnder(n.Id, root)) continue;
            listed++;
            deepest = Math.Max(deepest, n.Depth - rootDepth);
        }

        return (listed, deepest);
    }

    private async Task PlayDiscoveryRun()
    {
        var countsOnly = System.Environment.GetCommandLineArgs().Concat(OS.GetCmdlineUserArgs()).Any(a => a == "--counts-only");
        MaybeNoVsync();
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 40);
        await WaitSettled();
        await Seconds(2);
        var root = App.Navigator.Current;
        var on = P.PlayDiscoveryEnabled;
        Log($"play discovery {(on ? "on" : "off (--test-play-discovery=off)")}; pace {P.DiscoveryScheduler.Bounds.PlayListingsPerSecond} listings/s, {P.DiscoveryScheduler.Bounds.PlayMaxInFlight} in flight");
        var (l0, d0) = ListedBelow(root);
        Log(F($"  at 0 s: {l0} folders listed below the arrival, deepest level {d0}; tree {Tree.Count} entries"));
        var swaps0 = P.RecomposeSwaps;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _playFrameMs.Clear();
        _playTracking = true;
        // Walk in a wide circle (W with the arrow turning), in short legs so the walker meets the ground's variety.
        var at10 = (Listed: 0, Deepest: 0);
        var asked10 = 0;
        while (clock.Elapsed.TotalSeconds < 30)
        {
            await Hold(2.0, Godot.Key.W, Godot.Key.Right);
            await Hold(1.0, Godot.Key.W);
            if (clock.Elapsed.TotalSeconds >= 10 && at10.Listed == 0)
            {
                at10 = ListedBelow(root);
                asked10 = P.PlayDiscoveryRequested;
                Log(F($"  at {clock.Elapsed.TotalSeconds:0} s: {at10.Listed} folders listed below the arrival, deepest level {at10.Deepest}; play discovery asked {asked10}; tree {Tree.Count} entries"));
            }
        }

        _playTracking = false;
        var (l30, d30) = ListedBelow(root);
        Log(F($"  at {clock.Elapsed.TotalSeconds:0} s: {l30} folders listed below the arrival, deepest level {d30}; play discovery asked {P.PlayDiscoveryRequested}; tree {Tree.Count} entries; candidate rebuilds {P.PlayDiscoveryRebuilds}, longest {P.PlayDiscoveryRebuildMsMax:0.00} ms"));
        Log("perf walking with play discovery " + (on ? "on" : "off") + ": " + FrameStats(_playFrameMs) + F($"; frames over 25 ms: {_playFrameMs.Count(x => x > 25)}; the planet recomposed {P.RecomposeSwaps - swaps0} times (longest swap {P.RecomposeSwapMsMax:0.0} ms)"));
        if (on) Check(P.PlayDiscoveryRequested > 0 && l30 > l0, F($"background discovery lists while you play: {l0} → {l30} folders listed, {P.PlayDiscoveryRequested} asked"));
        else Check(P.PlayDiscoveryRequested == 0, "with --test-play-discovery=off nothing is asked for while you play");
        Check(P.DiscoveryScheduler.InFlight <= P.DiscoveryScheduler.Bounds.PlayMaxInFlight, F($"within the play bound: {P.DiscoveryScheduler.InFlight} in flight"));
        var ms = _playFrameMs.OrderBy(x => x).ToList();
        if (ms.Count >= 10)
        {
            var line = F($"walking frame time while discovering: p95 {ms[(int)(ms.Count * 0.95)]:0.0} ms");
            if (ProbeTier.Functional) Log("SKIP " + line + " (functional tier: not a measurement)");
            else Check(ms[(int)(ms.Count * 0.95)] < FrameP95LimitMs, line);
        }

        // What the overview shows after half a minute of play, before any of its own discovery.
        P.DiscoveryScheduler.Paused = true; // [direct] measure what play found, not the overview's own growth
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 5);
        await Seconds(1);
        var levels = P.UniverseLevels(root);
        Log(F($"  the overview after 30 s of play: {P.UniverseBodyCount} worlds, levels 0..{(levels.Count > 0 ? levels.Keys.Max() : -1)}, deepest listed {levels.Where(l => l.Value.Listed > 0).Select(l => l.Key).DefaultIfEmpty(-1).Max()}"));
        if (!countsOnly) await Shot("overview-after-play");
        await Until(() => P.DiscoveryScheduler.InFlight == 0, 25);
        await Key(Godot.Key.Escape);
        await Until(() => !P.UniverseOpen && !P.Busy, 10);
    }

    /// <summary>A fingerprint of the looks of the listed worlds drawn (by path, so it compares between runs; never logged as names).</summary>
    private string LookFingerprint()
    {
        var h = 1469598103934665603UL;
        var n = 0;
        foreach (var id in P.UniverseIds.Where(i => Tree[i].ScanState == DirScanState.Complete).OrderBy(i => Tree.FullPath(i), StringComparer.Ordinal).Take(200))
        {
            if (P.UniverseLook(id) is not { } look) continue;
            foreach (var ch in Tree.FullPath(id)) { h ^= ch; h *= 1099511628211UL; }
            foreach (var v in new[] { look.Base.R, look.Base.G, look.Base.B, look.Custom.R, look.Custom.G, look.Custom.B, look.Custom.A })
            {
                h ^= (ulong)(int)MathF.Round(v * 1000);
                h *= 1099511628211UL;
            }

            n++;
        }

        return F($"{n} worlds, fingerprint {h:x16}");
    }

    /// <summary>What the overview draws now: worlds drawn, folded, overlapping disc pairs and worlds, draw calls.</summary>
    private void LogDetail(string when)
    {
        var (drawn, folded) = P.UniverseDetailCounts();
        var (onScreen, pairs, worlds) = P.UniverseOverlap();
        var calls = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        Log(F($"  {when}: {P.UniverseBodyCount} worlds laid out, {drawn} drawn, {folded} folded into drawn systems; on screen {onScreen}, overlapping disc pairs {pairs}, worlds overlapping another {worlds} ({(onScreen > 0 ? 100.0 * worlds / onScreen : 0):0} %); draw calls {calls}; detail step ≤ {P.DetailMsMax:0.00} ms"));
    }

    private async Task OverviewNavigateRun()
    {
        var countsOnly = System.Environment.GetCommandLineArgs().Concat(OS.GetCmdlineUserArgs()).Any(a => a == "--counts-only");
        MaybeNoVsync();
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 40);
        await WaitSettled();
        await Seconds(2);
        var root = App.Navigator.Current;
        Log($"detail {(P.OverviewDetailEnabled ? "on" : "off (--test-overview-detail=off)")}, looks {(P.OverviewLooksVaried ? "varied" : "plain (--test-overview-look=plain)")}");
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 5);
        await Frames(3);
        Check(P.UniverseHintShown && P.UniverseHintText.Contains("[ ]", StringComparison.Ordinal), "the overview teaches its controls briefly in the view");
        if (!countsOnly) await Shot("nav-open");
        // Discovery fills the overview: 12 s, or --nav-fill-seconds=N (≈ 40 s fills C:\ toward the 1,500-world budget, for
        // the draw-cost measurement).
        var fill = System.Environment.GetCommandLineArgs().Concat(OS.GetCmdlineUserArgs()).FirstOrDefault(a => a.StartsWith("--nav-fill-seconds=", StringComparison.Ordinal)) is { } fa
            ? double.Parse(fa["--nav-fill-seconds=".Length..], System.Globalization.CultureInfo.InvariantCulture) : 12;
        await Seconds(fill);
        _discoveryFrameMs.Clear();
        await Seconds(4); // frame times of the settled, full overview
        LogDetail(F($"after {fill + 4:0} s"));
        Log("perf overview: " + FrameStats(_discoveryFrameMs));
        Log("looks: " + LookFingerprint());
        if (!countsOnly) await Shot("nav-full");
        Check(!P.UniverseHintShown, "the hint has stepped aside by now");
        if (P.OverviewDetailEnabled)
        {
            var (_, folded) = P.UniverseDetailCounts();
            Check(folded > 0 || P.UniverseBodyCount < 60, F($"far systems are folded and counted, not drawn: {folded} worlds folded"));
        }

        // ---- round a ring with ] and [ : each a selection, the orbit's centre following it
        var start = P.UniverseSelected;
        var firstChild = P.UniverseIds.FirstOrDefault(id => Tree[id].ParentId == root && Tree[id].IsDirectory && P.UniverseKnows(id), -1);
        if (Check(firstChild >= 0, "a world on the arrival's ring"))
        {
            P.SelectInUniverse(firstChild); // [direct] a known starting point on the ring
            await Seconds(1);
            var visited = new List<int> { P.UniverseSelected };
            for (var k = 0; k < 3; k++)
            {
                await Key(Godot.Key.Bracketright);
                await Seconds(0.8);
                visited.Add(P.UniverseSelected);
            }

            var siblings = visited.All(id => Tree[id].ParentId == root);
            Check(visited.Distinct().Count() == visited.Count || P.UniverseIds.Count(id => Tree[id].ParentId == root) < 4, F($"] steps round the ring, a new world each press ({visited.Distinct().Count()} distinct of {visited.Count})"));
            Check(siblings, "…staying on the same ring");
            var aim = P.UniverseCameraAim;
            var sel = P.UniverseLocal(P.UniverseSelected) is { } l ? l + new Vector3(0, 40000, 0) : Vector3.Zero;
            Check(aim.Focus.DistanceTo(sel) < 1f, F($"the orbit's centre is on the selected world ({aim.Focus.DistanceTo(sel):0.0} m)"));
            Check(P.UniverseDrawn(P.UniverseSelected), "the selected world is drawn (its way is always open)");
            await Key(Godot.Key.Bracketleft);
            await Seconds(0.8);
            Check(P.UniverseSelected == visited[^2], "[ steps back");
            if (!countsOnly) await Shot("nav-ring");
            await Key(Godot.Key.Z);
            await Seconds(1);
            Check(P.UniverseSelected == root, "Z goes up a level to the system it orbits");
        }

        // ---- the wheel toward a world under the pointer
        _discoveryFrameMs.Clear();
        var target = P.UniverseClickable();
        if (Check(target != null, "a world under the pointer to zoom toward") && target is { } t)
        {
            var at = P.UniverseLocal(t.Id) is { } l ? l + new Vector3(0, 40000, 0) : Vector3.Zero;
            var before = P.UniverseCameraAim.Focus.DistanceTo(at);
            var (_, z0) = P.UniverseZoom;
            Inject(new InputEventMouseMotion { Position = ToWindow(t.Centre), GlobalPosition = ToWindow(t.Centre) });
            await Frames(2);
            for (var k = 0; k < 5; k++)
            {
                // Each notch at the world's current place on screen (it slides toward the centre as the view follows).
                var c = P.UniverseScreen(t.Id) ?? t.Centre;
                Mouse(MouseButton.WheelUp, true, ToWindow(c));
                Mouse(MouseButton.WheelUp, false, ToWindow(c));
                await Seconds(0.35);
            }

            await Seconds(1.2);
            var after = P.UniverseCameraAim.Focus.DistanceTo(at);
            var (_, z1) = P.UniverseZoom;
            Check(after < before * 0.5f && z1 < z0 * 0.6f, F($"the wheel zooms toward the world under the pointer: centre {before:0} → {after:0} m from it, distance {z0:0} → {z1:0} m"));
            Check(P.UniverseApproached == t.Id || after < 0.3f * before, "…which becomes the approached world");
            LogDetail("zoomed toward a world");
            if (!countsOnly) await Shot("nav-zoomed");
            await Key(Godot.Key.F);
            await Seconds(1);
        }

        Log("perf navigating: " + FrameStats(_discoveryFrameMs));
        if (!P.DiscoveryScheduler.Paused) await Key(Godot.Key.X);
        await Until(() => P.DiscoveryScheduler.InFlight == 0, 25);
        await Seconds(1);
        await Key(Godot.Key.Escape);
        await Until(() => !P.UniverseOpen && !P.Busy, 10);
        _ = start;
    }
}
