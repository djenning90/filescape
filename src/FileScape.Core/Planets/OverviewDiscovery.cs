using FileScape.Core.Model;
using FileScape.Core.Scanning;

namespace FileScape.Core.Planets;

/// <summary>Where the overview's discovery stands (PLANET-76), for its indicator and the probes.</summary>
public enum OverviewDiscoveryState
{
    /// <summary>Folders are being listed, or are about to be (a listing is in flight or one was just asked for).</summary>
    Discovering,

    /// <summary>The player paused it (X or the indicator's button); nothing new is asked for, listings in flight finish.</summary>
    Paused,

    /// <summary>Every world the overview draws is listed (or could not be): nothing is left to discover there.</summary>
    Complete,

    /// <summary>The tree reached <see cref="OverviewDiscovery.Limits.MaxTreeNodes"/>: discovery stops, visits still list.</summary>
    LimitReached,
}

/// <summary>
/// The tree's folders by state (<see cref="OverviewDiscovery.Census"/>): known (every folder), listed (its contents are
/// in the tree), waiting (known by name, or queued: discovery may list it) and failed (denied, unavailable). Known minus
/// the other three are folders deliberately not listed (cloud, links' stubs, the entry limit, a cancelled walk).
/// </summary>
public readonly record struct DiscoveryCensus(int Known, int Listed, int Waiting, int Failed)
{
    /// <summary>The census as a JSON object (the probe state file; counts only, never names).</summary>
    public string Json => $"{{\"known\":{Known},\"listed\":{Listed},\"waiting\":{Waiting},\"failed\":{Failed}}}";
}

/// <summary>
/// The overview's discovery scheduler (PLANET-76, AD-71): while the overview is open it decides which of the worlds it
/// draws are listed next, so the known hierarchy grows outward without visiting anything. Engine-free and deterministic
/// for a given tree and clock, so its order and bounds are unit-tested (<c>OverviewDiscoveryTests</c>).
/// <para>
/// Order: the frontier is every drawn world still known only by name. Worlds in the subtree of a focus (the selected
/// world, the world the camera is zoomed toward) go first, breadth-first below that focus; then everything else,
/// breadth-first by depth, then in the layout's own order (names), so the surroundings fill in a level at a time before
/// any branch goes deep. Only worlds the layout draws are candidates, so discovery never lists what the overview could
/// not show (<see cref="UniverseLayout.MaxBodies"/>, <see cref="UniverseLayout.MaxChildren"/> bound it).
/// </para>
/// <para>
/// Bounds (<see cref="Limits"/>): at most <c>MaxInFlight</c> listings asked for and not yet answered, a token bucket of
/// <c>ListingsPerSecond</c> (burst <c>BurstListings</c>), and nothing once the tree holds <c>MaxTreeNodes</c> entries. A
/// request whose folder has not changed state after <c>RequestTimeout</c> frees its slot (the explorer may have queued
/// it behind what the user is waiting for) and is not asked for again.
/// </para>
/// <para>
/// What is never asked for, consistent with ordinary exploration: links (they are not folders in the tree, so they are
/// never followed into loops), the hub and location roots (no drive or distribution is opened from the overview),
/// folders that failed (access denied, unavailable: shown as such, listed again only by Retry), cloud folders that are
/// not traversed, anything <see cref="ListAhead"/> refuses (Linux system filesystems, network shares), and anything
/// the caller's scope rule excludes. Each folder is asked for once per tree. It reads entries and metadata only: the
/// explorer it feeds (<see cref="WorldExplorer.Discover"/>) never opens a file.
/// </para>
/// Call <see cref="Step"/> from the main thread (it reads the tree, which only the main thread writes).
/// </summary>
public sealed class OverviewDiscovery
{
    /// <summary>The scheduler's bounds. Defaults are what the app uses (AD-71); tests shrink them.</summary>
    public sealed record Limits
    {
        /// <summary>
        /// Listings asked for and not yet answered: few enough that a visit's own listings (the explorer runs four at a
        /// time) are never stuck behind discovery, enough that one slow folder does not stall it.
        /// </summary>
        public int MaxInFlight { get; init; } = 3;

        /// <summary>
        /// New listings per second, sustained. Each one adds entries to the tree on the main thread and may change the
        /// overview's layout, so this paces the growth the player watches (≈ 300 folders in 30 s) rather than the disk.
        /// </summary>
        public double ListingsPerSecond { get; init; } = 10;

        /// <summary>Listings that may be asked for at once after a pause (the token bucket's size).</summary>
        public int BurstListings { get; init; } = 3;

        /// <summary>
        /// Discovery stops once the tree holds this many entries (files, folders and links): a fifth of the session's
        /// <see cref="ScanSession.DefaultMaxNodes"/>, so what you visit next always has room (memory: ≈ 1 KB per entry).
        /// </summary>
        public int MaxTreeNodes { get; init; } = 200_000;

        /// <summary>A request with no change of state after this long frees its slot and is not asked for again.</summary>
        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(20);

        /// <summary>
        /// New listings per second while you play (the overview closed, PLANET-90): slower than the overview's pace,
        /// so the tree grows quietly in the background and the walking world's frame time is untouched (each listing
        /// lands on the main thread). ≈ 120 folders a minute.
        /// </summary>
        public double PlayListingsPerSecond { get; init; } = 2;

        /// <summary>Listings in flight while you play: two, so two of the explorer's four workers are always free for
        /// what the visit itself asks for (destinations, previews, a landing).</summary>
        public int PlayMaxInFlight { get; init; } = 2;
    }

    /// <summary>Which pace a step runs at: the overview's (watching it grow) or the quieter one during play (PLANET-90).</summary>
    public enum Pace
    {
        /// <summary>The overview is open: <see cref="Limits.ListingsPerSecond"/>, <see cref="Limits.MaxInFlight"/>.</summary>
        Overview,

        /// <summary>Ordinary play: <see cref="Limits.PlayListingsPerSecond"/>, <see cref="Limits.PlayMaxInFlight"/>.</summary>
        Play,
    }

    /// <summary>One drawn world the scheduler may list: its node, its depth below the overview's root, and its index in
    /// the layout's breadth-first order (which is name order within a level).</summary>
    public readonly record struct Candidate(int Id, int Depth, int Order);

    private readonly Limits _limits;
    private readonly Dictionary<int, TimeSpan> _inflight = [];
    private readonly HashSet<int> _tried = [];
    private double _tokens;
    private TimeSpan? _lastStep;

    public OverviewDiscovery(Limits? limits = null)
    {
        _limits = limits ?? new Limits();
        _tokens = _limits.BurstListings;
    }

    public Limits Bounds => _limits;

    /// <summary>
    /// Paused by the player: nothing new is asked for (listings already asked for still arrive). Setting it updates
    /// <see cref="State"/> at once, so the indicator says "paused" in the frame X is pressed; before 2026-11-19 (fifth
    /// follow-up) the state changed only at the next <see cref="Step"/> (up to 0.1 s later), and the discovery journey's
    /// "X pauses" check, read ≈ 80 ms after the key, failed whenever the step fell after it (AD-77).
    /// </summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (value) State = OverviewDiscoveryState.Paused;
            else if (State == OverviewDiscoveryState.Paused) State = InFlight > 0 || Frontier > 0 ? OverviewDiscoveryState.Discovering : OverviewDiscoveryState.Complete;
        }
    }

    private bool _paused;

    public OverviewDiscoveryState State { get; private set; } = OverviewDiscoveryState.Complete;

    /// <summary>Listings asked for so far, and how they ended: listed, failed (denied, unavailable…), timed out.</summary>
    public int Requested { get; private set; }

    public int Listed { get; private set; }

    public int Failed { get; private set; }

    public int TimedOut { get; private set; }

    /// <summary>Drawn worlds not asked for because <see cref="ListAhead"/> or the scope refused them.</summary>
    public int Refused { get; private set; }

    /// <summary>Listings asked for and not yet answered (≤ <see cref="Limits.MaxInFlight"/>).</summary>
    public int InFlight => _inflight.Count;

    /// <summary>Drawn worlds still waiting to be listed at the last step.</summary>
    public int Frontier { get; private set; }

    /// <summary>
    /// One scheduling step: settles answered requests, refills the rate budget, and returns the folders to ask the
    /// explorer for now (best first). <paramref name="drawn"/> is the overview's current bodies; <paramref name="focus"/>
    /// the worlds whose subtrees go first (the selected one, the one zoomed toward; ids not in the tree are ignored);
    /// <paramref name="inScope"/> the caller's rule for what may be listed ahead (the app: inside the arrival folder or
    /// below a place visited this session).
    /// </summary>
    /// <para>
    /// <paramref name="pace"/> picks the rate and the in-flight bound (<see cref="Pace"/>). With
    /// <paramref name="focusInOrder"/> the focus list is a priority order (the current world, then its parent, then its
    /// grandparent: PLANET-90's "what I'm visiting first"): a world ranks by the first focus it lies below; without it
    /// every focus ranks the same (the overview's selected and zoomed-toward worlds, AD-71).
    /// </para>
    public IReadOnlyList<int> Step(FsTree tree, IReadOnlyList<Candidate> drawn, IReadOnlyCollection<int> focus, TimeSpan now, Func<int, bool> inScope,
        Pace pace = Pace.Overview, bool focusInOrder = false)
    {
        Settle(tree, now);
        var elapsed = _lastStep is { } last ? Math.Max(0, (now - last).TotalSeconds) : 0;
        _lastStep = now;
        var rate = pace == Pace.Play ? _limits.PlayListingsPerSecond : _limits.ListingsPerSecond;
        var maxInFlight = pace == Pace.Play ? _limits.PlayMaxInFlight : _limits.MaxInFlight;
        _tokens = Math.Min(_limits.BurstListings, _tokens + elapsed * rate);

        var frontier = new List<(int Id, int Rank, int Depth, int Order)>();
        var focusDepth = new Dictionary<int, (int Depth, int Rank)>();
        var k = 0;
        foreach (var f in focus)
        {
            if (tree.Contains(f) && !focusDepth.ContainsKey(f)) focusDepth[f] = (tree[f].Depth, focusInOrder ? k : 0);
            k++;
        }

        var outside = focusInOrder ? focusDepth.Count : 1;

        foreach (var c in drawn)
        {
            if (!tree.Contains(c.Id) || _tried.Contains(c.Id) || _inflight.ContainsKey(c.Id) || !Eligible(tree[c.Id])) continue;
            // Below a focus: ranked first, breadth-first from that focus (its own depth subtracted). The nearest focus
            // above it decides (in priority order, the current world's subtree before its parent's).
            var rank = outside;
            var depth = c.Depth;
            if (focusDepth.Count > 0)
            {
                for (var a = c.Id; a >= 0; a = tree[a].ParentId)
                {
                    if (!focusDepth.TryGetValue(a, out var fd)) continue;
                    if (fd.Rank > rank) continue;
                    rank = fd.Rank;
                    depth = tree[c.Id].Depth - fd.Depth;
                    if (!focusInOrder) break;
                }
            }

            frontier.Add((c.Id, rank, depth, c.Order));
        }

        Frontier = frontier.Count;
        var picked = new List<int>();
        if (_paused)
        {
            State = OverviewDiscoveryState.Paused;
            return picked;
        }

        if (tree.Count >= _limits.MaxTreeNodes)
        {
            State = OverviewDiscoveryState.LimitReached;
            return picked;
        }

        frontier.Sort((x, y) => x.Rank != y.Rank ? x.Rank.CompareTo(y.Rank) : x.Depth != y.Depth ? x.Depth.CompareTo(y.Depth) : x.Order.CompareTo(y.Order));
        foreach (var (id, _, _, _) in frontier)
        {
            if (_inflight.Count >= maxInFlight || _tokens < 1) break;
            _tried.Add(id);
            // Checked only for the folders about to be asked for: a path is built per check.
            if (!inScope(id) || ListAhead.Refusal(tree.FullPath(id), ListAheadKind.Background) != null)
            {
                Refused++;
                Frontier--;
                continue;
            }

            _inflight[id] = now;
            _tokens -= 1;
            Requested++;
            Frontier--;
            picked.Add(id);
        }

        State = _inflight.Count > 0 || Frontier > 0 ? OverviewDiscoveryState.Discovering : OverviewDiscoveryState.Complete;
        return picked;
    }

    /// <summary>Forgets everything (a new tree: ids start again).</summary>
    public void Reset()
    {
        _inflight.Clear();
        _tried.Clear();
        _tokens = _limits.BurstListings;
        _lastStep = null;
        Requested = Listed = Failed = TimedOut = Refused = Frontier = 0;
        State = OverviewDiscoveryState.Complete;
    }

    /// <summary>
    /// A folder the overview may list ahead: a plain folder known only by name. Location roots and the hub are opened only
    /// by going there; a failed, denied, unavailable or cloud folder keeps its state until the player retries it; a folder
    /// with a note (a refusal from <see cref="ListAhead"/>, the entry limit) is not asked for again. A folder the
    /// background walk has queued (pending) is eligible too (PLANET-196): asking for it brings it forward of the walk.
    /// </summary>
    public static bool Eligible(FsNode n) =>
        n.Kind == NodeKind.Directory
        && n.ScanState is DirScanState.Unlisted or DirScanState.Pending
        && n.Location == LocationKind.None
        && n.ParentId >= 0
        && n.Error == null
        && (n.Flags & (NodeFlags.AccessDenied | NodeFlags.EnumerationError | NodeFlags.Unavailable | NodeFlags.Vanished | NodeFlags.CloudNotTraversed | NodeFlags.CloudPlaceholder)) == 0;

    /// <summary>
    /// What the tree knows (PLANET-195, AD-98): its folders by state, counted the same whether the overview is open or not,
    /// so "newly discovered" (more folders listed or known) can be told from "newly displayed" (more of them drawn by
    /// the overview's budget). <paramref name="within"/> limits the count to some folders (a neighbourhood).
    /// </summary>
    public static DiscoveryCensus Census(FsTree tree, Func<FsNode, bool>? within = null)
    {
        int known = 0, listed = 0, waiting = 0, failed = 0;
        foreach (var n in tree.Nodes)
        {
            if (n.Kind != NodeKind.Directory || within != null && !within(n)) continue;
            known++;
            switch (n.ScanState)
            {
                case DirScanState.Complete: listed++; break;
                case DirScanState.Unlisted or DirScanState.Pending or DirScanState.Listing: waiting++; break;
                case DirScanState.Failed: failed++; break;
            }
        }

        return new DiscoveryCensus(known, listed, waiting, failed);
    }

    /// <summary>
    /// Why <paramref name="after"/> is not progress from <paramref name="before"/> (fewer folders known or listed: the
    /// session's tree only ever learns), or null when it is (probes check it across opening the overview).
    /// </summary>
    public static string? Regression(DiscoveryCensus before, DiscoveryCensus after) =>
        after.Known < before.Known ? $"folders known fell from {before.Known} to {after.Known}"
        : after.Listed < before.Listed ? $"folders listed fell from {before.Listed} to {after.Listed}"
        : null;

    /// <summary>Requests whose folder changed state are answered; requests older than the timeout give up their slot.</summary>
    private void Settle(FsTree tree, TimeSpan now)
    {
        List<int>? done = null;
        foreach (var (id, asked) in _inflight)
        {
            if (!tree.Contains(id))
            {
                (done ??= []).Add(id);
                continue;
            }

            var s = tree[id].ScanState;
            if (s is DirScanState.Unlisted or DirScanState.Pending or DirScanState.Listing)
            {
                if (now - asked < _limits.RequestTimeout) continue;
                TimedOut++;
            }
            else if (s == DirScanState.Complete)
            {
                Listed++;
            }
            else
            {
                Failed++;
            }

            (done ??= []).Add(id);
        }

        if (done == null) return;
        foreach (var id in done) _inflight.Remove(id);
    }
}
