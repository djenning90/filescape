using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-overview-discovery --root=&lt;Deep Atlas&gt; --test-broad</c> (PLANET-76..78, 2026-11-19): DJ asked
/// that the overview discover more of the filesystem by itself. This opens the overview (T) on the deep fixture
/// (FileScape.Fixtures <c>create-deep</c>, or <c>tools/make-wsl-fixture.sh --deep</c> through \\wsl.localhost) with the
/// arrival folder treated as a broad place, so nothing but discovery lists it, and measures the worlds and levels drawn
/// and listed at 0, 10 and 30 s without visiting anything. Meanwhile it checks that nothing jumps (each world's
/// per-frame screen motion, the selected world's drift), that the camera is never reset, that selecting a world sends
/// discovery into its subtree first, that zooming, pausing (X and the button), landing on a discovered world and coming
/// back all work during discovery, that what was learned is still there when the overview opens again, and that links,
/// the outside folder, the denied folder and the hidden folder are treated as ordinary exploration treats them. Frame
/// times while discovery runs are judged outside the functional tier.
/// <para>
/// <c>--counts-only</c> (a real drive root, <c>--root=C:\</c>): the same measurements at 0, 10 and 30 s, zoom, pause and
/// resume, but no screenshots, no landing, and only counts in the log (never a folder name); before quitting it pauses
/// discovery and waits until nothing is in flight, so no path is printed on exit.
/// </para>
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>A frame in which some drawn world moves more than this share of the screen height is a jump (a relayout
    /// that cut instead of gliding); a glide moves the farthest world a few per cent a frame.</summary>
    private const float DiscoveryJumpShare = 0.10f;

    /// <summary>The selected (anchor) world may drift at most this share of the screen height a frame, and over a whole
    /// wait, while discovery relayouts round it.</summary>
    private const float AnchorFrameShare = 0.01f, AnchorWaitShare = 0.03f;

    /// <summary>The most drawn worlds whose screen motion is tracked each frame (projection cost in the probe).</summary>
    private const int DiscoveryTrackedWorlds = 400;

    private readonly List<double> _discoveryFrameMs = [];
    private readonly Dictionary<int, Vector2> _discoveryLastScreen = [];
    private bool _discoveryTracking;
    private float _discoveryMaxJump, _discoveryAnchorMaxStep;
    private string _discoveryJumpWhat = "";
    private int _discoveryFrameIndex, _discoveryRelayoutsSeen;
    private int _discoveryAnchor = -1;
    private Vector2? _discoveryAnchorLast;

    /// <summary>From <see cref="_Process"/>: frame times while the overview is open, and every tracked world's motion.</summary>
    private void TrackDiscoveryFrame(double delta)
    {
        if (!P.UniverseOpen) return;
        _discoveryFrameMs.Add(delta * 1000);
        if (!_discoveryTracking) return;
        var h = App.GetViewport().GetVisibleRect().Size.Y;
        _discoveryFrameIndex++;
        var relayout = P.DiscoveryRelayouts != _discoveryRelayoutsSeen;
        _discoveryRelayoutsSeen = P.DiscoveryRelayouts;
        var cam = P.Camera.GlobalPosition;
        var (camNow, _) = P.UniverseZoom;
        var seen = new HashSet<int>();
        var vp = App.GetViewport().GetVisibleRect();
        foreach (var id in P.UniverseIds.Take(DiscoveryTrackedWorlds))
        {
            // Only what is on screen: far outside the view a projection magnifies the smallest motion (the first runs
            // "jumped" 48 % at (-1892, 2414) in a 1600 x 900 view: an instrument defect, not a layout jump).
            if (P.UniverseScreen(id) is not { } s || !vp.HasPoint(s)) continue;
            seen.Add(id);
            if (_discoveryLastScreen.TryGetValue(id, out var was) && s.DistanceTo(was) / h > _discoveryMaxJump)
            {
                _discoveryMaxJump = s.DistanceTo(was) / h;
                var at = P.UniverseLocal(id) is { } l ? l + new Vector3(0, 40000, 0) : Vector3.Zero;
                _discoveryJumpWhat = F($"frame {_discoveryFrameIndex}{(relayout ? " (a relayout frame)" : "")}: ({was.X:0},{was.Y:0}) → ({s.X:0},{s.Y:0}), {cam.DistanceTo(at):0.0} m from the camera (camera {camNow:0} m from its focus), level {Tree[id].Depth}");
            }
            _discoveryLastScreen[id] = s;
        }

        foreach (var gone in _discoveryLastScreen.Keys.Where(k => !seen.Contains(k)).ToList()) _discoveryLastScreen.Remove(gone);
        if (_discoveryAnchor >= 0 && P.UniverseScreen(_discoveryAnchor) is { } a)
        {
            if (_discoveryAnchorLast is { } la) _discoveryAnchorMaxStep = MathF.Max(_discoveryAnchorMaxStep, a.DistanceTo(la) / h);
            _discoveryAnchorLast = a;
        }
    }

    private void StartTracking(int anchor)
    {
        _discoveryLastScreen.Clear();
        _discoveryMaxJump = 0;
        _discoveryAnchorMaxStep = 0;
        _discoveryAnchor = anchor;
        _discoveryAnchorLast = null;
        _discoveryTracking = true;
    }

    /// <summary>What the overview shows below the arrival folder at one moment.</summary>
    private sealed record DiscoverySnapshot(double Seconds, int Worlds, int Inside, int DeepestDrawn, int DeepestListed, string PerLevel, int Requested, int Listed, int Failed, int TreeEntries);

    private DiscoverySnapshot Snap(int root, double seconds)
    {
        var levels = P.UniverseLevels(root);
        var d = P.DiscoveryScheduler;
        return new DiscoverySnapshot(seconds, P.UniverseBodyCount, levels.Values.Sum(v => v.Drawn), levels.Count > 0 ? levels.Keys.Max() : -1,
            levels.Where(l => l.Value.Listed > 0).Select(l => l.Key).DefaultIfEmpty(-1).Max(),
            string.Join(" ", levels.Select(l => F($"{l.Key}:{l.Value.Drawn}/{l.Value.Listed}"))), d.Requested, d.Listed, d.Failed, Tree.Count);
    }

    private void LogSnap(DiscoverySnapshot s) =>
        Log(F($"  at {s.Seconds:0} s: {s.Worlds} worlds drawn ({s.Inside} below the arrival folder), levels drawn 0..{s.DeepestDrawn}, deepest listed {s.DeepestListed}; per level drawn/listed {s.PerLevel}; discovery asked {s.Requested}, listed {s.Listed}, failed {s.Failed}; tree {s.TreeEntries} entries; relayouts {P.DiscoveryRelayouts}, longest {P.DiscoveryRelayoutMsMax:0.0} ms"));

    /// <summary>Waits until <paramref name="t"/> seconds after <paramref name="since"/> (wall clock), tracking as it goes.</summary>
    private async Task WaitUntilSecond(System.Diagnostics.Stopwatch since, double t)
    {
        while (since.Elapsed.TotalSeconds < t) await Frames(1);
    }

    private async Task OverviewDiscoveryRun()
    {
        var countsOnly = System.Environment.GetCommandLineArgs().Concat(OS.GetCmdlineUserArgs()).Any(a => a == "--counts-only");
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 40);
        await WaitSettled();
        await Seconds(2); // the visit's own listings (destinations, context) arrive first: they are the 0 s picture
        var root = App.Navigator.Current;
        Check(P.DiscoveryAvailable, "discovery is available (a live explorer)");
        if (!countsOnly) Log($"arrival: {Tree.FullPath(root)}");
        Log(F($"limits: {P.DiscoveryScheduler.Bounds.MaxInFlight} in flight, {P.DiscoveryScheduler.Bounds.ListingsPerSecond} listings/s (burst {P.DiscoveryScheduler.Bounds.BurstListings}), stop at {P.DiscoveryScheduler.Bounds.MaxTreeNodes} entries, request timeout {P.DiscoveryScheduler.Bounds.RequestTimeout.TotalSeconds} s; overview lays out ≤ {P.Attention.Tuning.Budget} worlds, ≤ {UniverseLayout.MaxChildren} per system"));

        // ---- open, then wait without touching anything
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 5);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var s0 = Snap(root, 0);
        LogSnap(s0);
        LogDetail("at 0 s"); // PlanetsProbe.Detail.cs (PLANET-91: drawn, folded, overlap)
        Check(P.DiscoveryIndicatorShown && P.DiscoveryText.Contains("Discover", StringComparison.Ordinal), F($"a small indicator says discovery is going on: \"{(countsOnly ? P.DiscoveryText.Split('·')[0].Trim() : P.DiscoveryText)}\""));
        if (!countsOnly) await Shot("discovery-0s");
        await Seconds(2.5); // the opening pull-back settles before motion is judged
        var aim0 = P.UniverseCameraAim;
        StartTracking(P.UniverseSelected);
        var anchorAt = P.UniverseScreen(P.UniverseSelected);
        await WaitUntilSecond(clock, 10);
        _discoveryTracking = false;
        var s10 = Snap(root, 10);
        LogSnap(s10);
        LogDetail("at 10 s");
        if (!countsOnly) await Shot("discovery-10s");
        var aim10 = P.UniverseCameraAim;
        var anchorNow = P.UniverseScreen(P.UniverseSelected);
        var h = App.GetViewport().GetVisibleRect().Size.Y;
        var drift = anchorAt is { } a0 && anchorNow is { } a1 ? a0.DistanceTo(a1) / h : float.NaN;
        Check(s10.Worlds > s0.Worlds, F($"waiting reveals more worlds: {s0.Worlds} at 0 s → {s10.Worlds} at 10 s"));
        if (!countsOnly) Check(s10.DeepestDrawn >= s0.DeepestDrawn + 2, F($"…and more levels without a visit: levels drawn 0..{s0.DeepestDrawn} → 0..{s10.DeepestDrawn}, deepest listed {s0.DeepestListed} → {s10.DeepestListed}"));
        else Check(s10.DeepestDrawn >= s0.DeepestDrawn + 1 || s10.Worlds >= P.Attention.Tuning.Budget, F($"…and deeper: levels drawn 0..{s0.DeepestDrawn} → 0..{s10.DeepestDrawn} (or the overview's budget is full)"));
        Check(_discoveryMaxJump < DiscoveryJumpShare, F($"no world jumps while the overview grows: largest per-frame move {_discoveryMaxJump * 100:0.0} % of the screen height ({P.DiscoveryRelayouts} relayouts glided); {_discoveryJumpWhat}"));
        Check(_discoveryAnchorMaxStep < AnchorFrameShare && (float.IsNaN(drift) || drift < AnchorWaitShare), F($"the selected world keeps its place on screen: ≤ {_discoveryAnchorMaxStep * 100:0.00} % a frame, {drift * 100:0.0} % over the wait"));
        Check(MathF.Abs(aim10.Yaw - aim0.Yaw) < 1e-4f && MathF.Abs(aim10.Pitch - aim0.Pitch) < 1e-4f && aim10.Focus.DistanceTo(aim0.Focus) < 0.5f, F($"the camera is never reset: yaw {aim0.Yaw:0.000} → {aim10.Yaw:0.000}, pitch {aim0.Pitch:0.000} → {aim10.Pitch:0.000}, focus moved {aim10.Focus.DistanceTo(aim0.Focus):0.00} m"));

        // ---- select a world: its subtree goes first
        var chosen = countsOnly ? LargestUnlistedRich(root) : Child(root, "West");
        if (Check(chosen >= 0 && P.UniverseKnows(chosen), "a world below the arrival folder to select"))
        {
            var before = CompleteUnder(root);
            if (P.UniverseScreen(chosen) is { } at && !P.OverHudAt(at)) await Click(at / GetWindow().ContentScaleFactor);
            else
            {
                P.SelectInUniverse(chosen); // [direct] it was off screen or under a panel
                Log("  [direct] selected (it was not clickable on screen)");
            }

            await Frames(5);
            Check(P.UniverseSelected == chosen, countsOnly ? "a click selects a world during discovery" : $"a click selects {Tree.DisplayName(chosen)} during discovery");
            await Seconds(6);
            var fresh = CompleteUnder(root).Except(before).ToList();
            var inside = fresh.Count(id => IsUnder(id, chosen));
            Check(fresh.Count > 0 && inside >= fresh.Count * 3 / 4, F($"the selected world's subtree goes first: {inside} of the {fresh.Count} folders listed in the next 6 s are inside it"));
            // Zoom toward it with the wheel.
            var (_, z0) = P.UniverseZoom;
            await Wheel(up: true, 3);
            await Seconds(1);
            var (_, z1) = P.UniverseZoom;
            Check(z1 < z0 * 0.8f, F($"the wheel zooms during discovery: {z0:0} → {z1:0} m"));
        }

        // ---- 30 s
        await WaitUntilSecond(clock, 30);
        var s30 = Snap(root, 30);
        LogSnap(s30);
        LogDetail("at 30 s");
        if (!countsOnly) await Shot("discovery-30s");
        Check(s30.Worlds >= s10.Worlds && s30.DeepestListed >= s10.DeepestListed, F($"it keeps going: {s10.Worlds} → {s30.Worlds} worlds, deepest listed {s10.DeepestListed} → {s30.DeepestListed}"));
        if (!countsOnly) Check(s30.DeepestListed >= s0.DeepestListed + 2, F($"multiple additional levels listed without visiting them: {s0.DeepestListed} → {s30.DeepestListed}"));

        // ---- pause and resume (X, then the button)
        await Key(Godot.Key.X);
        await Frames(3);
        var requested = P.DiscoveryScheduler.Requested;
        Check(P.DiscoveryScheduler.Paused && P.DiscoveryText.Contains("paused", StringComparison.OrdinalIgnoreCase), F($"X pauses discovery (the indicator says so): paused {P.DiscoveryScheduler.Paused}, \"{P.DiscoveryText}\", focus {GetViewport().GuiGetFocusOwner()?.Name ?? "none"}"));
        await Seconds(2);
        Check(P.DiscoveryScheduler.Requested == requested, F($"nothing more is asked for while paused ({requested} asked)"));
        await Click(P.DiscoveryButtonRect.GetCenter() / GetWindow().ContentScaleFactor);
        await Frames(3);
        Check(!P.DiscoveryScheduler.Paused, "the indicator's button resumes it");

        if (!countsOnly)
        {
            // ---- land on a discovered world (listed by discovery, never visited), come back
            var target = DiscoveredToLand(root);
            if (Check(target >= 0, "a world discovered (listed) without a visit, to land on"))
            {
                var children = Tree[target].Children.Count;
                Check(Tree[target].ScanState == DirScanState.Complete, F($"{Tree.DisplayName(target)} (level {Tree[target].Depth - Tree[root].Depth}) was listed by discovery before any visit: {children} entries"));
                P.SelectInUniverse(target); // [direct] a deep world, possibly behind its neighbours on screen
                await Frames(3);
                await Key(Godot.Key.Enter);
                await Until(() => App.Navigator.Current == target && !P.Busy && !P.UniverseOpen, 15);
                await Seconds(1.5);
                await Shot("discovery-landed");
                Check(App.Navigator.Current == target && !P.UniverseOpen && P.Composition != null, $"Enter lands on {Tree.DisplayName(App.Navigator.Current)} during discovery");
                Check(Tree[target].Children.Count == children, F($"the visit lists nothing again: still {Tree[target].Children.Count} entries"));
                await Key(Godot.Key.Backspace);
                await Until(() => App.Navigator.Current == root && !P.Busy, 15);
                await Seconds(1.5);
                Check(App.Navigator.Current == root, $"Backspace returns to {Tree.DisplayName(App.Navigator.Current)}");
            }
        }
        else
        {
            await Key(Godot.Key.Escape);
            await Until(() => !P.UniverseOpen && !P.Busy, 10);
            await Seconds(1);
        }

        // ---- open again: what was learned is kept
        var known = Tree.Count;
        var asked = P.DiscoveryScheduler.Requested;
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 5);
        await Seconds(1);
        var again = Snap(root, clock.Elapsed.TotalSeconds);
        LogSnap(again);
        Check(again.Worlds >= s30.Worlds && Tree.Count >= known && P.DiscoveryScheduler.Requested >= asked, F($"opening the overview again keeps what was learned: {again.Worlds} worlds (was {s30.Worlds}), {Tree.Count} entries known"));

        // ---- responsiveness while discovery ran
        var ms = _discoveryFrameMs.OrderBy(x => x).ToList();
        if (ms.Count >= 10)
        {
            var line = F($"perf overview with discovery: {ms.Count} frames, mean {ms.Average():0.0} ms, p95 {ms[(int)(ms.Count * 0.95)]:0.0} ms, p99 {ms[(int)(ms.Count * 0.99)]:0.0} ms, max {ms[^1]:0.0} ms; longest relayout {P.DiscoveryRelayoutMsMax:0.0} ms");
            if (ProbeTier.Functional) Log("SKIP " + line + " (functional tier: not a measurement)");
            else Check(ms[(int)(ms.Count * 0.95)] < FrameP95LimitMs, line);
        }

        // ---- quiet before leaving: pause, let what is in flight arrive (no path is printed on exit)
        if (!P.DiscoveryScheduler.Paused) await Key(Godot.Key.X);
        await Until(() => P.DiscoveryScheduler.InFlight == 0, 25);
        await Seconds(2);
        await Key(Godot.Key.Escape);
        await Until(() => !P.UniverseOpen && !P.Busy, 10);

        if (!countsOnly) CheckOrdinaryExplorationRules(root);
    }

    /// <summary>Links, the outside folder, the denied and the hidden folder: as ordinary exploration treats them.</summary>
    private void CheckOrdinaryExplorationRules(int root)
    {
        var link = Tree.Nodes.FirstOrDefault(n => n.Name == "back-to-atlas");
        if (link != null) Check(link.Kind == NodeKind.Link && link.Children.Count == 0, $"the link back to the root is shown, never followed ({link.Kind}, {link.Children.Count} entries below it)");
        else Log("  the link back to the root was not reached");
        Check(!Tree.Nodes.Any(n => n.Name == "beyond.txt"), "nothing outside the arrival folder was listed (the folder a link points to stays unlisted)");
        var sealedDir = Tree.Nodes.FirstOrDefault(n => n.Name == "Sealed");
        if (sealedDir == null) Log("  the denied folder was not reached");
        else Check(sealedDir.ScanState == DirScanState.Failed && (sealedDir.Flags & NodeFlags.AccessDenied) != 0 || sealedDir.ScanState == DirScanState.Unlisted,
            $"the denied folder is reported as denied, not retried ({sealedDir.ScanState}, {sealedDir.Flags})");
        // A hidden folder is not a world in the overview, so discovery never asks for it (a visit's own context listing,
        // two levels down, may list it as ordinary exploration does).
        var hidden = Tree.Nodes.FirstOrDefault(n => n.Name == "Hidden Grove");
        if (hidden != null) Check(!P.UniverseKnowsEver(hidden.Id), $"a hidden folder is never drawn, so discovery never asks for it ({hidden.ScanState})");
        Log(F($"  discovery totals: asked {P.DiscoveryScheduler.Requested}, listed {P.DiscoveryScheduler.Listed}, failed {P.DiscoveryScheduler.Failed}, timed out {P.DiscoveryScheduler.TimedOut}, refused {P.DiscoveryScheduler.Refused}"));
    }

    private bool IsUnder(int id, int ancestor)
    {
        for (var a = id; a >= 0; a = Tree[a].ParentId) if (a == ancestor) return true;
        return false;
    }

    /// <summary>Folders below <paramref name="root"/> that are listed now.</summary>
    private HashSet<int> CompleteUnder(int root) =>
        Tree.Nodes.Where(n => n.IsDirectory && n.ScanState == DirScanState.Complete && n.Id != root && IsUnder(n.Id, root)).Select(n => n.Id).ToHashSet();

    /// <summary>For a drive root (counts only): the drawn level-1 world with the most drawn worlds still to list below it.</summary>
    private int LargestUnlistedRich(int root)
    {
        var drawn = P.UniverseIds.ToHashSet();
        return Tree[root].Children.Where(c => drawn.Contains(c) && Tree[c].IsDirectory)
            .Select(c => (c, n: drawn.Count(d => d != c && IsUnder(d, c) && Tree[d].ScanState == DirScanState.Unlisted)))
            .OrderByDescending(x => x.n).Select(x => x.c).DefaultIfEmpty(-1).First();
    }

    /// <summary>A drawn world at least three levels down that discovery listed and nobody visited.</summary>
    private int DiscoveredToLand(int root) =>
        P.UniverseIds.Where(id => Tree[id].IsDirectory && Tree[id].ScanState == DirScanState.Complete && !App.VisitedPlaces.Contains(id)
                                  && Tree[id].Depth - Tree[root].Depth >= 3 && IsUnder(id, root))
            .OrderByDescending(id => Tree[id].Depth).DefaultIfEmpty(-1).First();
}
