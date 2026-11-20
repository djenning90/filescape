using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App;

/// <summary>
/// The sixth pass's overview journeys (PLANET-101..109, AD-81), on the vast fixture (FileScape.Fixtures <c>create-vast</c>)
/// or, with <c>--counts-only</c>, on a real root along a route given by <c>--route=a/b/c</c> (only counts are logged then,
/// never a folder name, and no screenshot is taken).
/// <list type="bullet">
/// <item><c>--automate=planets-overview-attention --test-broad</c>: DJ's failure, played. Opens the overview, crosses
/// distant branches (three regions far apart, and a sector in each), descends a deep route twelve passages down (14 levels
/// below the arrival, beyond where DJ's overview stopped), lands there, opens the overview again, goes two passages deeper,
/// returns (Backspace) and goes back to the first branch. At every focus it checks that the neighbourhood's children are
/// drawn and that discovery completes the neighbourhood, and it checks the indicator's lines fit beside its button at UI
/// scales 1.0 and 1.25. Selections are made directly ([direct]): this checks what attention does with a selection; clicking
/// and the camera are other journeys'.</item>
/// <item><c>--automate=planets-overview-bodies</c> (no <c>--test-broad</c>: the visit's background traversal lists the whole
/// fixture): the renderer's cost against the number of laid-out worlds (PLANET-108): for each budget in
/// <c>--budgets=1500,5000,20000,46000</c>, the overview opened, orbiting for 4 s with every world drawn
/// (the level of detail off) and with the level of detail on; frame times (run with <c>--no-vsync</c>), draw calls, and the
/// main-thread cost of a relayout.</item>
/// </list>
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>The longest a focus's neighbourhood may take to be listed by discovery, seconds (≈ 60 folders at 10/s,
    /// with slack for a shared machine).</summary>
    private const double NeighbourhoodTimeoutSeconds = 25;

    private bool CountsOnly => System.Environment.GetCommandLineArgs().Concat(OS.GetCmdlineUserArgs()).Any(a => a == "--counts-only");

    private static string? UserArg(string name) =>
        System.Environment.GetCommandLineArgs().Concat(OS.GetCmdlineUserArgs()).Where(a => a.StartsWith("--" + name + "=", StringComparison.Ordinal)).Select(a => a[(name.Length + 3)..]).LastOrDefault();

    /// <summary>The folder at a '/'-separated route below <paramref name="from"/> (names case-insensitive), or −1.</summary>
    private int Route(int from, string route)
    {
        var at = from;
        foreach (var part in route.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (at < 0) return -1;
            at = Child(at, part);
        }

        return at;
    }

    /// <summary>Folder children of a world the overview would show (visible folders).</summary>
    private List<int> FolderKids(int id) => Tree[id].Children.Where(c => Tree[c].IsDirectory && App.IsVisibleInPlaceForProbe(Tree[c])).ToList();

    /// <summary>
    /// Selects <paramref name="id"/> and waits until its neighbourhood is complete (or the timeout); then checks its
    /// children are drawn and the indicator says so and fits. Returns whether the neighbourhood completed.
    /// </summary>
    private async Task<bool> AttendTo(int id, string what, bool select = true)
    {
        if (select)
        {
            P.SelectInUniverse(id); // [direct]
            await Frames(3);
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var firstKids = -1.0;
        NeighbourhoodStatus near = default;
        while (clock.Elapsed.TotalSeconds < NeighbourhoodTimeoutSeconds)
        {
            var kids = FolderKids(id);
            if (firstKids < 0 && kids.Count > 0 && kids.Take(UniverseLayout.MaxChildren).All(P.UniverseDrawn)) firstKids = clock.Elapsed.TotalSeconds;
            near = P.AttentionStatus().Near;
            if (near.Focus == id && near.Complete && firstKids >= 0) break;
            await Seconds(0.25);
        }

        var all = FolderKids(id);
        var drawn = all.Count(P.UniverseDrawn);
        var laid = all.Count(P.UniverseKnows);
        var grand = all.SelectMany(FolderKids).Count(P.UniverseKnows);
        var label = CountsOnly ? what : $"{what} ({Tree.DisplayName(id)}, level {Tree[id].Depth - Tree[App.Navigator.Root].Depth})";
        Check(all.Count == 0 || drawn >= Math.Min(all.Count, UniverseLayout.MaxChildren),
            F($"{label}: its folders are drawn: {drawn} of {all.Count} drawn ({laid} laid out, {grand} grandchildren laid out){(firstKids >= 0 ? $", all within {firstKids:0.0} s" : "")}"));
        Check(near.Focus == id && near.Complete, F($"{label}: the neighbourhood is complete within {clock.Elapsed.TotalSeconds:0.0} s: {near.Worlds} worlds over {near.Levels} levels, {near.Listed} listed, {near.Waiting} waiting, {near.Unlistable} can't be listed, {near.Folded} folded"));
        // The indicator works its counts out every 0.25 s (PlanetWorld.Discovery.cs), so it may say so a moment after the
        // probe's own count does (the first run read it in the same frame and failed 10 of 41 times: an instrument defect).
        if (near.Complete) await Until(() => P.DiscoveryText.Contains("complete", StringComparison.Ordinal), 1.5);
        var overflow = P.DiscoveryIndicatorOverflow();
        Check(overflow == null && P.DiscoveryText.Contains("complete", StringComparison.Ordinal),
            CountsOnly ? F($"{what}: the indicator says the neighbourhood is complete and fits{(overflow != null ? ": overflow" : "")}") : $"{what}: the indicator says \"{P.DiscoveryText}\" / \"{P.DiscoveryElsewhereText}\"{(overflow != null ? " — " + overflow : "")}");
        Log(F($"  attention: {P.UniverseBodyCount} worlds laid out (budget {P.Attention.Tuning.Budget}, {P.Attention.BudgetFolded} folded by it), recent foci {P.Attention.Recent.Count}; discovery asked {P.DiscoveryScheduler.Requested}, listed {P.DiscoveryScheduler.Listed}; relayouts {P.DiscoveryRelayouts}, longest {P.DiscoveryRelayoutMsMax:0.0} ms"));
        return near.Complete;
    }

    private async Task OverviewAttentionRun()
    {
        var countsOnly = CountsOnly;
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 40);
        await WaitSettled();
        await Seconds(2);
        var root = App.Navigator.Current;
        Check(P.DiscoveryAvailable, "discovery is available (a live explorer)");
        Log(F($"budget {P.Attention.Tuning.Budget} worlds, neighbourhood {P.Attention.Tuning.NeighbourhoodLevels} levels, discovery reach {P.Attention.Tuning.DiscoveryReach}"));
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 5);
        await Seconds(2);
        _discoveryFrameMs.Clear();
        if (!await AttendTo(root, "the arrival", select: false) && !countsOnly) await Shot("attention-arrival-incomplete");
        if (!countsOnly) await Shot("attention-0-arrival");

        // ---- distant branches, each and a sector in it (fixture), or the real route's steps (counts only)
        var routeArg = UserArg("route");
        if (countsOnly)
        {
            if (!Check(routeArg != null, "a --route to follow on the real root")) return;
            var at = root;
            var step = 0;
            foreach (var part in routeArg!.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = Child(at, part);
                if (!Check(next >= 0 && P.UniverseKnows(next), F($"route step {step + 1} is laid out, so it can be selected"))) return;
                at = next;
                step++;
                await AttendTo(at, F($"route step {step} (level {Tree[at].Depth - Tree[root].Depth})"));
            }

            await QuietAndLeave();
            return;
        }

        var regions = new[] { "Region 01", "Region 16", "Region 08" };
        var firstSector = -1;
        foreach (var r in regions)
        {
            var region = Child(root, r);
            if (!Check(region >= 0 && P.UniverseKnows(region), $"{r} is laid out")) continue;
            await AttendTo(region, r);
            var sector = Route(region, "Sector C/Cluster 4");
            if (Check(sector >= 0 && P.UniverseKnows(sector), $"{r}/Sector C/Cluster 4 is laid out after attending to {r}"))
            {
                await AttendTo(sector, $"{r}/Sector C/Cluster 4");
                if (firstSector < 0) firstSector = sector;
            }
        }

        await Shot("attention-1-branches");

        // ---- down a deep route, passage by passage, well beyond the old failing depth
        var route = Route(root, "Region 09/Deep Route");
        if (!Check(route >= 0 && P.UniverseKnows(route), "Region 09/Deep Route is laid out")) return;
        await AttendTo(route, "Region 09/Deep Route");
        var passage = route;
        for (var p = 1; p <= 12; p++)
        {
            var next = Child(passage, $"Passage {p:D2}");
            if (!Check(next >= 0 && P.UniverseKnows(next), $"Passage {p:D2} is laid out once its parent was attended to")) return;
            passage = next;
            await AttendTo(passage, $"Passage {p:D2}");
        }

        await Shot("attention-2-passage-12");

        // ---- land there, open the overview again: where you stand shows its folders at once (DJ: "only that folder")
        await Key(Godot.Key.Enter);
        await Until(() => App.Navigator.Current == passage && !P.Busy && !P.UniverseOpen, 20);
        await Seconds(1.5);
        Check(App.Navigator.Current == passage, $"Enter lands on {Tree.DisplayName(passage)}");
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 5);
        await Seconds(1);
        var hereKids = FolderKids(passage);
        Check(hereKids.Count > 0 && hereKids.All(P.UniverseKnows), F($"the overview opened again at level {Tree[passage].Depth - Tree[root].Depth} shows where you stand with its folders: {hereKids.Count(P.UniverseKnows)} of {hereKids.Count} laid out at once"));
        await Shot("attention-3-reopened-deep");
        await AttendTo(passage, "Passage 12, reopened", select: false);
        for (var p = 13; p <= 14; p++)
        {
            var next = Child(passage, $"Passage {p:D2}");
            if (!Check(next >= 0 && P.UniverseKnows(next), $"Passage {p:D2} is laid out")) return;
            passage = next;
            await AttendTo(passage, $"Passage {p:D2}");
        }

        await Shot("attention-4-passage-14");

        // ---- return: Backspace to the arrival, then the first branch again
        await Key(Godot.Key.Escape);
        await Until(() => !P.UniverseOpen && !P.Busy, 10);
        await Seconds(1);
        await Key(Godot.Key.Backspace);
        await Until(() => App.Navigator.Current == root && !P.Busy, 20);
        await Seconds(1.5);
        Check(App.Navigator.Current == root, $"Backspace returns to {Tree.DisplayName(App.Navigator.Current)}");
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 5);
        await Seconds(1.5);
        if (firstSector >= 0)
        {
            if (Check(P.UniverseKnows(firstSector) || P.UniverseKnows(Tree[firstSector].ParentId), "the first branch is still in the overview"))
            {
                if (!P.UniverseKnows(firstSector))
                {
                    await AttendTo(Tree[firstSector].ParentId, "Region 01/Sector C, on return");
                }

                var asked = P.DiscoveryScheduler.Requested;
                P.SelectInUniverse(firstSector); // [direct]
                await Seconds(1.0);
                var kids = FolderKids(firstSector);
                Check(kids.All(P.UniverseKnows), F($"returning to Region 01/Sector C/Cluster 4: its {kids.Count} folders are laid out again within 1 s, from what was learned"));
                await AttendTo(firstSector, "Region 01/Sector C/Cluster 4, on return", select: false);
                Log(F($"  discovery asked {P.DiscoveryScheduler.Requested - asked} more while returning (each folder is asked for once)"));
            }
        }

        await Shot("attention-5-returned");

        // ---- the indicator at UI 1.0 and 1.25 (the panel scales with the UI)
        var scale0 = GetWindow().ContentScaleFactor;
        foreach (var scale in new[] { 1.0f, 1.25f })
        {
            GetWindow().ContentScaleFactor = scale;
            await Frames(6);
            var overflow = P.DiscoveryIndicatorOverflow();
            Check(overflow == null, $"at UI scale {scale:0.00} the indicator's lines fit beside its button: \"{P.DiscoveryText}\" / \"{P.DiscoveryElsewhereText}\"{(overflow != null ? " — " + overflow : "")}");
            await Shot(F($"attention-status-{scale:0.00}"));
        }

        GetWindow().ContentScaleFactor = scale0;
        await Frames(3);
        var ms = _discoveryFrameMs.OrderBy(x => x).ToList();
        if (ms.Count >= 10)
        {
            var line = F($"perf overview while attention moved: {ms.Count} frames, p50 {ms[ms.Count / 2]:0.0} ms, p95 {ms[(int)(ms.Count * 0.95)]:0.0} ms, p99 {ms[(int)(ms.Count * 0.99)]:0.0} ms, max {ms[^1]:0.0} ms; longest relayout {P.DiscoveryRelayoutMsMax:0.0} ms");
            if (ProbeTier.Functional) Log("SKIP " + line + " (functional tier: not a measurement)");
            else Check(ms[(int)(ms.Count * 0.95)] < FrameP95LimitMs, line);
        }

        await QuietAndLeave();
    }

    /// <summary>Pauses discovery, lets what is in flight arrive (no path is printed on exit), and closes the overview.</summary>
    private async Task QuietAndLeave()
    {
        if (!P.DiscoveryScheduler.Paused) await Key(Godot.Key.X);
        await Until(() => P.DiscoveryScheduler.InFlight == 0, 25);
        await Seconds(1);
        await Key(Godot.Key.Escape);
        await Until(() => !P.UniverseOpen && !P.Busy, 10);
    }

    // ---------------------------------------------------------------- bodies against frame time (PLANET-108)

    private async Task OverviewBodiesRun()
    {
        MaybeNoVsync(); // PlanetsProbe.Detail.cs
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 40);
        await WaitSettled();
        // The visit's background traversal lists the fixture: wait until the tree stops growing.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var last = Tree.Count;
        var still = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 300 && still.Elapsed.TotalSeconds < 6)
        {
            await Seconds(1);
            if (Tree.Count != last) still.Restart();
            last = Tree.Count;
        }

        var folders = Tree.Nodes.Count(n => n.IsDirectory);
        Log(F($"tree {Tree.Count} entries, {folders} folders, after {clock.Elapsed.TotalSeconds:0} s"));
        P.DiscoveryScheduler.Paused = true; // [direct] measure drawing, not growth
        var budgets = (UserArg("budgets") ?? "1500,5000,20000,46000").Split(',').Select(b => int.Parse(b, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var detail0 = P.OverviewDetailEnabled;
        foreach (var budget in budgets)
        {
            P.ResetAttentionForProbe(budget);
            await Key(Godot.Key.T);
            await Until(() => P.UniverseOpen, 10);
            await Seconds(3); // the opening pull-back
            foreach (var detail in new[] { false, true })
            {
                P.OverviewDetailEnabled = detail;
                await Seconds(0.5);
                var frames = new List<double>();
                var draws = new List<long>();
                var t0 = Time.GetTicksUsec();
                var start = t0;
                Key1(Godot.Key.Left, true);
                while ((Time.GetTicksUsec() - start) / 1e6 < 4)
                {
                    await Frames(1);
                    var t = Time.GetTicksUsec();
                    frames.Add((t - t0) / 1000.0);
                    t0 = t;
                    draws.Add((long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame));
                }

                Key1(Godot.Key.Left, false);
                var (drawn, folded) = P.UniverseDetailCounts();
                Log(F($"perf bodies {P.UniverseBodyCount} (budget {budget}), detail {(detail ? "on" : "off")}: {drawn} drawn, {folded} folded; {FrameStats(frames)}; draw calls p50 {draws.OrderBy(x => x).ElementAt(draws.Count / 2)}"));
            }

            var relayouts = new List<(double Layout, double Scene)>();
            for (var i = 0; i < 3; i++)
            {
                relayouts.Add(P.RelayoutNowForProbe());
                await Seconds(1.4); // let the glide finish
            }

            Log(F($"perf relayout at {P.UniverseBodyCount} bodies: layout {relayouts.Min(r => r.Layout):0.0}–{relayouts.Max(r => r.Layout):0.0} ms, scene rebuild {relayouts.Min(r => r.Scene):0.0}–{relayouts.Max(r => r.Scene):0.0} ms; detail decision longest {P.DetailMsMax:0.0} ms"));
            if (budget == budgets[^1] || budget == P.Attention.Tuning.Budget) await Shot(F($"bodies-{budget}"));
            await Key(Godot.Key.Escape);
            await Until(() => !P.UniverseOpen && !P.Busy, 10);
            await Seconds(1);
        }

        P.OverviewDetailEnabled = detail0;
        Check(true, "measured (the numbers are the result)");
    }
}
