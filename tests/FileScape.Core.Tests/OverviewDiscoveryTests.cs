using System.Diagnostics;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using FileScape.Core.Scanning;

namespace FileScape.Core.Tests;

/// <summary>
/// The overview's discovery scheduler (PLANET-76, AD-71): breadth-first order, the focus's subtree first, its bounds
/// (in flight, rate, tree size, timeout), and what it never asks for (links, location roots, failed or denied folders,
/// refusals). A simulated filesystem answers requests after a set delay, so order and bounds are exact; one POSIX test
/// runs it against the real explorer with a symlink loop and an unreadable folder.
/// </summary>
public sealed class OverviewDiscoveryTests
{
    /// <summary>
    /// A filesystem of folders only, listed on request: the tree starts with its root listed (its children known by name),
    /// and <see cref="Answer"/> applies a requested folder's listing (children as unlisted folders) as the explorer would.
    /// </summary>
    private sealed class SimFs
    {
        public readonly FsTree Tree = new(new DataSourceInfo(DataOrigin.Synthetic, "synthetic://sim", "sim"));
        private readonly Dictionary<int, string> _path = [];
        private readonly Func<string, IEnumerable<string>> _children;
        public readonly List<string> ListedOrder = [];

        public SimFs(Func<string, IEnumerable<string>> children)
        {
            _children = children;
            var b = new ScanBatch();
            b.NewNodes.Add(new NodeRecord(0, -1, "root", NodeKind.Directory, 0, 0, 0, null, null, null, 0, DirScanState.Unlisted));
            Tree.Apply(b);
            _path[0] = "";
            Answer(0);
            ListedOrder.Clear();
        }

        public string PathOf(int id) => _path[id];

        public void Answer(int id, DirScanState state = DirScanState.Complete, NodeFlags flags = 0)
        {
            var b = new ScanBatch();
            var next = Tree.Count;
            if (state == DirScanState.Complete)
            {
                foreach (var name in _children(_path[id]).OrderBy(n => n, StringComparer.Ordinal))
                {
                    var cid = next++;
                    b.NewNodes.Add(new NodeRecord(cid, id, name, NodeKind.Directory, 0, 0, 0, null, null, null, Tree[id].Depth + 1, DirScanState.Unlisted));
                    _path[cid] = _path[id] + "/" + name;
                }
            }

            b.DirectoryUpdates.Add(new DirectoryUpdate(id, state, flags));
            Tree.Apply(b);
            ListedOrder.Add(_path[id]);
        }

        /// <summary>The overview's drawn worlds now (the real layout, so candidates and order are what the app passes).</summary>
        public List<OverviewDiscovery.Candidate> Drawn() =>
            UniverseLayout.Compute(Tree).Select((b, i) => new OverviewDiscovery.Candidate(b.NodeId, b.Depth, i)).ToList();
    }

    /// <summary>Every folder has <paramref name="fanout"/> children down to <paramref name="depth"/> levels (names a, b, c…).</summary>
    private static Func<string, IEnumerable<string>> Uniform(int fanout, int depth) =>
        p => p.Count(c => c == '/') >= depth ? [] : Enumerable.Range(0, fanout).Select(i => ((char)('a' + i)).ToString());

    private static int DepthOf(string path) => path.Count(c => c == '/');

    /// <summary>Runs the scheduler for <paramref name="seconds"/> of simulated time in 0.1 s steps; each request is answered after <paramref name="latency"/>.</summary>
    private static double Run(SimFs fs, OverviewDiscovery d, double seconds, double latency = 0.05, Func<IReadOnlyCollection<int>>? focus = null, Action<TimeSpan>? each = null, double start = 0)
    {
        var pending = new List<(int Id, TimeSpan Due)>();
        var t = start;
        for (var k = 0; k <= (int)Math.Round(seconds * 10); k++)
        {
            t = start + k * 0.1;
            var now = TimeSpan.FromSeconds(t);
            foreach (var p in pending.Where(p => p.Due <= now).ToList())
            {
                pending.Remove(p);
                fs.Answer(p.Id);
            }

            foreach (var id in d.Step(fs.Tree, fs.Drawn(), focus?.Invoke() ?? [], now, _ => true)) pending.Add((id, now + TimeSpan.FromSeconds(latency)));
            each?.Invoke(now);
        }

        return t + 0.1;
    }

    [Fact]
    public void Folders_are_listed_breadth_first_so_the_surroundings_fill_in_before_any_branch_goes_deep()
    {
        var fs = new SimFs(Uniform(3, 6));
        var d = new OverviewDiscovery(new OverviewDiscovery.Limits { ListingsPerSecond = 1000, BurstListings = 1000, MaxInFlight = 1000 });
        Run(fs, d, 6);
        var depths = fs.ListedOrder.Select(DepthOf).ToList();
        depths.Should().BeInAscendingOrder("a level is listed completely before the next one starts");
        fs.ListedOrder.Count(p => DepthOf(p) == 3).Should().Be(27, "every folder of the first levels is listed");
        // Within a level, the layout's order (names) decides: a's children before b's.
        fs.ListedOrder.Where(p => DepthOf(p) == 2).Should().Equal("/a/a", "/a/b", "/a/c", "/b/a", "/b/b", "/b/c", "/c/a", "/c/b", "/c/c");
    }

    [Fact]
    public void The_selected_worlds_subtree_goes_first_breadth_first_below_it()
    {
        var fs = new SimFs(Uniform(3, 5));
        var d = new OverviewDiscovery(new OverviewDiscovery.Limits { ListingsPerSecond = 2, BurstListings = 1, MaxInFlight = 1 });
        // Level 1 (a, b, c) first, as nothing is selected; then select c.
        var t = Run(fs, d, 1.4);
        fs.ListedOrder.Should().Equal("/a", "/b", "/c");
        var c = fs.Tree[0].Children.Single(i => fs.Tree[i].Name == "c");
        var before = fs.ListedOrder.Count;
        Run(fs, d, 6, focus: () => [c], start: t);
        var after = fs.ListedOrder.Skip(before).Take(12).ToList();
        after.Should().OnlyContain(p => p.StartsWith("/c/", StringComparison.Ordinal), "the selected world's subtree is listed before the rest");
        after.Select(DepthOf).Should().BeInAscendingOrder("breadth-first below the selected world too");
    }

    [Fact]
    public void Requests_in_flight_and_the_rate_stay_within_their_bounds()
    {
        var fs = new SimFs(Uniform(4, 6));
        var limits = new OverviewDiscovery.Limits { MaxInFlight = 3, ListingsPerSecond = 10, BurstListings = 3 };
        var d = new OverviewDiscovery(limits);
        var maxInFlight = 0;
        // Slow listings (0.5 s each): the in-flight bound is what holds them back.
        Run(fs, d, 5, latency: 0.5, each: _ => maxInFlight = Math.Max(maxInFlight, d.InFlight));
        maxInFlight.Should().BeLessThanOrEqualTo(3);
        d.Requested.Should().BeGreaterThan(20, "it keeps going while listings are slow");
        // Fast listings: the rate holds it back (10 a second plus the burst).
        var fs2 = new SimFs(Uniform(4, 6));
        var d2 = new OverviewDiscovery(limits);
        Run(fs2, d2, 5, latency: 0.01);
        d2.Requested.Should().BeLessThanOrEqualTo(10 * 5 + 3);
        d2.Requested.Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void Discovery_stops_at_the_tree_size_limit_and_while_paused()
    {
        var fs = new SimFs(Uniform(5, 6));
        var d = new OverviewDiscovery(new OverviewDiscovery.Limits { MaxTreeNodes = 60, ListingsPerSecond = 100, BurstListings = 100, MaxInFlight = 100 });
        var t = Run(fs, d, 3);
        d.State.Should().Be(OverviewDiscoveryState.LimitReached);
        fs.Tree.Count.Should().BeLessThan(60 + 5 * 100, "nothing is asked for past the limit (what was in flight still arrives)");
        var count = fs.Tree.Count;
        Run(fs, d, 3, start: t);
        fs.Tree.Count.Should().Be(count);

        var fs2 = new SimFs(Uniform(3, 4));
        var paused = new OverviewDiscovery { Paused = true };
        var t2 = Run(fs2, paused, 3);
        paused.Requested.Should().Be(0);
        paused.State.Should().Be(OverviewDiscoveryState.Paused);
        paused.Paused = false;
        Run(fs2, paused, 3, start: t2);
        paused.Requested.Should().BeGreaterThan(0, "resuming carries on");
    }

    [Fact]
    public void Pausing_says_so_at_once_not_at_the_next_step()
    {
        // AD-77: the "X pauses" check read the indicator ≈ 80 ms after X; the state changed only at the next step (every
        // 0.1 s), so it failed whenever that step fell after the read. The state now follows the flag at once.
        var fs = new SimFs(Uniform(3, 4));
        var d = new OverviewDiscovery();
        d.Step(fs.Tree, fs.Drawn(), [], TimeSpan.Zero, _ => true);
        d.State.Should().Be(OverviewDiscoveryState.Discovering);
        d.Paused = true;
        d.State.Should().Be(OverviewDiscoveryState.Paused, "no step has run since X");
        d.Paused = false;
        d.State.Should().Be(OverviewDiscoveryState.Discovering, "resuming says so at once too");
    }

    [Fact]
    public void During_play_the_current_world_goes_first_then_its_parent_at_the_quieter_pace()
    {
        // PLANET-90: focus in priority order (the current world, then its parent), the play pace (2 a second, 2 in flight).
        var fs = new SimFs(Uniform(3, 5));
        var limits = new OverviewDiscovery.Limits();
        var d = new OverviewDiscovery(limits);
        // List levels 1 and 2 first (as a visit would), so there is a frontier under both b and a/…
        var t = Run(fs, new OverviewDiscovery(new OverviewDiscovery.Limits { ListingsPerSecond = 1000, BurstListings = 1000, MaxInFlight = 1000 }), 0.1);
        var a = fs.Tree[0].Children.Single(i => fs.Tree[i].Name == "a");
        var ab = fs.Tree[a].Children.Single(i => fs.Tree[i].Name == "b");
        var pending = new List<(int Id, TimeSpan Due)>();
        var before = fs.ListedOrder.Count;
        var maxInFlight = 0;
        for (var k = 0; k < 100; k++)
        {
            var now = TimeSpan.FromSeconds(t + k * 0.1);
            foreach (var p in pending.Where(p => p.Due <= now).ToList())
            {
                pending.Remove(p);
                fs.Answer(p.Id);
            }

            foreach (var id in d.Step(fs.Tree, fs.Drawn(), [ab, a, 0], now, _ => true, OverviewDiscovery.Pace.Play, focusInOrder: true))
                pending.Add((id, now + TimeSpan.FromSeconds(0.6)));
            maxInFlight = Math.Max(maxInFlight, d.InFlight);
        }

        var listed = fs.ListedOrder.Skip(before).ToList();
        maxInFlight.Should().BeLessThanOrEqualTo(limits.PlayMaxInFlight);
        d.Requested.Should().BeLessThanOrEqualTo((int)(limits.PlayListingsPerSecond * 10) + limits.BurstListings, "the play pace");
        // a/b's own subtree (1 + 3 + 9 folders) first; a slot left while its next level is in flight goes to its parent's
        // other folders (its neighbours), never yet to the rest of the tree.
        listed.Should().HaveCountGreaterThanOrEqualTo(13);
        listed[0].Should().Be("/a/b", "the current world first");
        listed.Take(13).Count(p => p.StartsWith("/a/b", StringComparison.Ordinal)).Should().BeGreaterThanOrEqualTo(9, "then mostly its own subtree");
        listed.Should().OnlyContain(p => p.StartsWith("/a/", StringComparison.Ordinal), "its neighbours before the rest of the tree");
    }

    [Fact]
    public void Everything_drawn_is_listed_then_discovery_is_complete()
    {
        var fs = new SimFs(Uniform(2, 3));
        var d = new OverviewDiscovery();
        Run(fs, d, 10);
        d.State.Should().Be(OverviewDiscoveryState.Complete);
        d.Listed.Should().Be(2 + 4 + 8, "every folder below the root, once");
        fs.ListedOrder.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Links_location_roots_and_failed_or_denied_folders_are_never_asked_for()
    {
        var spec = new Spec("root")
            .Dir(new Spec("plain") { State = DirScanState.Unlisted })
            .Dir(new Spec("link") { IsLink = true })
            .Dir(new Spec("denied") { State = DirScanState.Failed, Flags = NodeFlags.AccessDenied })
            .Dir(new Spec("denied-unlisted") { State = DirScanState.Unlisted, Flags = NodeFlags.AccessDenied })
            .Dir(new Spec("cloud") { State = DirScanState.NotTraversed, Flags = NodeFlags.CloudNotTraversed })
            .Dir(new Spec("drive") { State = DirScanState.Unlisted, Location = LocationKind.Drive })
            .Dir(new Spec("pending") { State = DirScanState.Pending });
        var tree = spec.Build();
        var candidates = tree.Nodes.Select((n, i) => new OverviewDiscovery.Candidate(n.Id, n.Depth, i)).ToList();
        var d = new OverviewDiscovery();
        var asked = d.Step(tree, candidates, [], TimeSpan.Zero, _ => true);
        // "pending" was left out before PLANET-196: a folder queued by the background walk could not be brought forward.
        asked.Select(id => tree[id].Name).Should().BeEquivalentTo(["plain", "pending"]);
        OverviewDiscovery.Eligible(tree.Find("link")).Should().BeFalse("a link is not a folder: it is shown, never followed");
    }

    /// <summary>
    /// PLANET-196: a drawn world the background walk has queued (pending in the tree) is asked for like an unlisted one,
    /// so the explorer brings it forward of the walk (<c>WorldExplorerTests.Discover_brings_a_folder_waiting_in_the_background_walk_forward</c>);
    /// it counts as listed when the walk's listing lands, and the counts of a census tell what is known from what is listed.
    /// </summary>
    [Fact]
    public void A_folder_waiting_in_the_background_walk_is_asked_for_and_the_census_counts_it_as_waiting()
    {
        var tree = new Spec("root")
            .Dir(new Spec("walked") { State = DirScanState.Complete })
            .Dir(new Spec("queued") { State = DirScanState.Pending })
            .Dir(new Spec("named") { State = DirScanState.Unlisted })
            .Dir(new Spec("denied") { State = DirScanState.Failed, Flags = NodeFlags.AccessDenied })
            .Build();
        var census = OverviewDiscovery.Census(tree);
        census.Known.Should().Be(5, "every folder the tree holds, the root included");
        census.Listed.Should().Be(2, "the root and \"walked\"");
        census.Waiting.Should().Be(2, "\"queued\" and \"named\": known by name only");
        census.Failed.Should().Be(1);
        var candidates = tree.Nodes.Where(n => n.IsDirectory).Select((n, i) => new OverviewDiscovery.Candidate(n.Id, n.Depth, i)).ToList();
        var d = new OverviewDiscovery();
        d.Step(tree, candidates, [], TimeSpan.Zero, _ => true).Select(id => tree[id].Name).Should().BeEquivalentTo(["queued", "named"]);
        var b = new ScanBatch();
        b.DirectoryUpdates.Add(new DirectoryUpdate(tree.Find("queued").Id, DirScanState.Complete));
        tree.Apply(b);
        d.Step(tree, candidates, [], TimeSpan.FromSeconds(0.1), _ => true);
        d.Listed.Should().Be(1, "the walk's own listing answers the request");
        var after = OverviewDiscovery.Census(tree);
        after.Listed.Should().Be(census.Listed + 1);
        OverviewDiscovery.Regression(census, after).Should().BeNull("progress only grows");
        OverviewDiscovery.Regression(after, census).Should().NotBeNull("a census with fewer listed folders than before is a regression");
    }

    [Fact]
    public void A_refused_folder_is_counted_and_not_asked_for_again()
    {
        var fs = new SimFs(Uniform(2, 2));
        var d = new OverviewDiscovery();
        var a = fs.Tree[0].Children[0];
        var asked = d.Step(fs.Tree, fs.Drawn(), [], TimeSpan.Zero, id => id != a);
        asked.Should().NotContain(a);
        d.Refused.Should().Be(1);
        d.Step(fs.Tree, fs.Drawn(), [], TimeSpan.FromSeconds(1), _ => true).Should().NotContain(a, "each folder is considered once");
    }

    [Fact]
    public void A_request_that_is_never_answered_frees_its_slot_after_the_timeout()
    {
        var fs = new SimFs(Uniform(3, 1));
        var d = new OverviewDiscovery(new OverviewDiscovery.Limits { MaxInFlight = 1, RequestTimeout = TimeSpan.FromSeconds(2) });
        var first = d.Step(fs.Tree, fs.Drawn(), [], TimeSpan.Zero, _ => true);
        first.Should().HaveCount(1);
        d.Step(fs.Tree, fs.Drawn(), [], TimeSpan.FromSeconds(1), _ => true).Should().BeEmpty("its slot is still taken");
        var next = d.Step(fs.Tree, fs.Drawn(), [], TimeSpan.FromSeconds(2.5), _ => true);
        d.TimedOut.Should().Be(1);
        next.Should().HaveCount(1).And.NotContain(first[0]);
    }

    [Fact]
    public void A_failed_listing_counts_as_failed_and_is_not_retried()
    {
        var fs = new SimFs(Uniform(2, 2));
        var d = new OverviewDiscovery(new OverviewDiscovery.Limits { MaxInFlight = 1 });
        var first = d.Step(fs.Tree, fs.Drawn(), [], TimeSpan.Zero, _ => true).Single();
        fs.Answer(first, DirScanState.Failed, NodeFlags.AccessDenied);
        d.Step(fs.Tree, fs.Drawn(), [], TimeSpan.FromSeconds(1), _ => true).Should().NotContain(first);
        d.Failed.Should().Be(1);
    }

    /// <summary>
    /// PLANET-77: the overview pins every world it draws when it lays out again, so discovery only ever adds worlds, even
    /// when the budget (1,500) fills: a newly listed shallow folder does not push out deeper worlds already shown.
    /// </summary>
    [Fact]
    public void Relaying_out_with_what_is_drawn_pinned_only_adds_worlds()
    {
        var fs = new SimFs(Uniform(3, 8)); // 3^8 = 6,561 at the bottom: well past the overview's budget
        var d = new OverviewDiscovery(new OverviewDiscovery.Limits { ListingsPerSecond = 200, BurstListings = 50, MaxInFlight = 50 });
        var drawn = new HashSet<int>(UniverseLayout.Compute(fs.Tree).Select(b => b.NodeId));
        var pending = new List<int>();
        for (var step = 0; step < 200; step++)
        {
            foreach (var id in pending) fs.Answer(id);
            var bodies = UniverseLayout.Compute(fs.Tree, null, 0, drawn);
            bodies.Select(b => b.NodeId).Should().Contain(drawn, "a world once drawn stays drawn");
            drawn = bodies.Select(b => b.NodeId).ToHashSet();
            pending = d.Step(fs.Tree, bodies.Select((b, i) => new OverviewDiscovery.Candidate(b.NodeId, b.Depth, i)).ToList(), [], TimeSpan.FromSeconds(step * 0.1), _ => true).ToList();
        }

        drawn.Count.Should().BeInRange(UniverseLayout.MaxBodies - UniverseLayout.MaxChildren, UniverseLayout.MaxBodies + UniverseLayout.MaxChildren);
        d.State.Should().Be(OverviewDiscoveryState.Complete, "once the budget is full and every drawn world is listed, there is nothing left to discover");
    }

    /// <summary>
    /// The scheduler against the real explorer on a POSIX tree (Background off, as on a broad place): a deep tree, a
    /// symlink loop, an unreadable folder and a folder outside the arrival. Discovery reaches the deep levels without a
    /// visit, lists each folder once, never goes through the link, reports the unreadable folder as denied and lists
    /// nothing outside the arrival folder.
    /// </summary>
    [PosixFact]
    public void Against_the_real_explorer_links_are_not_followed_and_denied_folders_are_reported()
    {
        if (OperatingSystem.IsWindows()) return; // [PosixFact] skips it there; this tells the analyser too
        var root = Path.Combine(Path.GetTempPath(), "filescape-discovery-" + Guid.NewGuid().ToString("N")[..8]);
        var arrival = Path.Combine(root, "arrival");
        var deep = arrival;
        for (var i = 1; i <= 7; i++)
        {
            deep = Path.Combine(deep, $"level-{i}");
            Directory.CreateDirectory(deep);
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(deep)!, $"side-{i}"));
            File.WriteAllText(Path.Combine(deep, "file.txt"), "never read");
        }

        Directory.CreateSymbolicLink(Path.Combine(arrival, "level-1", "loop"), "..");
        // Three levels down: the visit lists the arrival's folders and their children (destinations, context), discovery the rest.
        var locked = Path.Combine(arrival, "level-1", "level-2", "locked");
        Directory.CreateDirectory(Path.Combine(locked, "inside"));
        Directory.CreateDirectory(Path.Combine(root, "outside", "beyond"));
        File.SetUnixFileMode(locked, UnixFileMode.None);
        var canDeny = Environment.UserName != "root";
        try
        {
            var explorer = new WorldExplorer(arrival, new WorldExplorer.Options { Locations = [new LocationSeed("/", "/", LocationKind.Volume, true)], Background = false });
            using var session = new ScanSession(explorer);
            var tree = new FsTree(session.Source);
            session.Start();
            var d = new OverviewDiscovery(new OverviewDiscovery.Limits { ListingsPerSecond = 50, BurstListings = 5 });
            int arrivalId = -1;
            var sw = Stopwatch.StartNew();
            bool Inside(int id)
            {
                foreach (var a in tree.Ancestors(id)) if (a == arrivalId) return true;
                return false;
            }

            var visited = false;
            while (sw.Elapsed < TimeSpan.FromSeconds(20))
            {
                while (session.TryDequeue(out var b))
                {
                    tree.Apply(b);
                    foreach (var r in b.Replies) if (r.Ticket == WorldExplorer.ArrivalTicket) arrivalId = r.NodeId;
                }

                if (arrivalId >= 0)
                {
                    if (!visited) explorer.Visit(arrivalId); // where you stand is listed by the visit, as in the app
                    visited = true;
                    var drawn = UniverseLayout.Compute(tree, null, tree.Root.Id).Select((b, i) => new OverviewDiscovery.Candidate(b.NodeId, b.Depth, i)).ToList();
                    foreach (var id in d.Step(tree, drawn, [], sw.Elapsed, Inside)) explorer.Discover(id);
                    if (d.State == OverviewDiscoveryState.Complete && session.Snapshot().Idle && !session.HasPendingBatches) break;
                }

                Thread.Sleep(5);
            }

            d.State.Should().Be(OverviewDiscoveryState.Complete);
            var levels = tree.Nodes.Where(n => n.IsDirectory && n.ScanState == DirScanState.Complete && Inside(n.Id)).Max(n => n.Depth - tree[arrivalId].Depth);
            levels.Should().BeGreaterThanOrEqualTo(6, "discovery reaches the deep levels without visiting them");
            var loop = tree.Find("loop");
            loop.Kind.Should().Be(NodeKind.Link);
            loop.Children.Should().BeEmpty("a link is never followed by discovery");
            tree.Nodes.Should().NotContain(n => n.Name == "beyond", "nothing outside the arrival folder is listed");
            if (canDeny)
            {
                var l = tree.Find("locked");
                l.ScanState.Should().Be(DirScanState.Failed);
                (l.Flags & NodeFlags.AccessDenied).Should().Be(NodeFlags.AccessDenied);
                d.Failed.Should().BeGreaterThanOrEqualTo(1);
            }

            d.Listed.Should().Be(d.Requested - d.Failed - d.TimedOut);
            session.Dispose();
            session.WaitForExit(TimeSpan.FromSeconds(5));
            explorer.Dispose();
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.Delete(Path.Combine(arrival, "level-1", "loop"));
            Directory.Delete(root, recursive: true);
        }
    }
}
