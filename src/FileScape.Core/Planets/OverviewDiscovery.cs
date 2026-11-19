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

    /// <summary>Paused by the player: nothing new is asked for (listings already asked for still arrive).</summary>
    public bool Paused { get; set; }

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
    public IReadOnlyList<int> Step(FsTree tree, IReadOnlyList<Candidate> drawn, IReadOnlyCollection<int> focus, TimeSpan now, Func<int, bool> inScope)
    {
        Settle(tree, now);
        var elapsed = _lastStep is { } last ? Math.Max(0, (now - last).TotalSeconds) : 0;
        _lastStep = now;
        _tokens = Math.Min(_limits.BurstListings, _tokens + elapsed * _limits.ListingsPerSecond);

        var frontier = new List<(int Id, int Rank, int Depth, int Order)>();
        var focusDepth = new Dictionary<int, int>();
        foreach (var f in focus)
        {
            if (tree.Contains(f)) focusDepth[f] = tree[f].Depth;
        }

        foreach (var c in drawn)
        {
            if (!tree.Contains(c.Id) || _tried.Contains(c.Id) || _inflight.ContainsKey(c.Id) || !Eligible(tree[c.Id])) continue;
            // Below a focus: ranked first, breadth-first from that focus (its own depth subtracted).
            var rank = 1;
            var depth = c.Depth;
            if (focusDepth.Count > 0)
            {
                for (var a = c.Id; a >= 0; a = tree[a].ParentId)
                {
                    if (!focusDepth.TryGetValue(a, out var fd)) continue;
                    rank = 0;
                    depth = tree[c.Id].Depth - fd;
                    break;
                }
            }

            frontier.Add((c.Id, rank, depth, c.Order));
        }

        Frontier = frontier.Count;
        var picked = new List<int>();
        if (Paused)
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
            if (_inflight.Count >= _limits.MaxInFlight || _tokens < 1) break;
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
    /// with a note (a refusal from <see cref="ListAhead"/>, the entry limit) is not asked for again.
    /// </summary>
    public static bool Eligible(FsNode n) =>
        n.Kind == NodeKind.Directory
        && n.ScanState == DirScanState.Unlisted
        && n.Location == LocationKind.None
        && n.ParentId >= 0
        && n.Error == null
        && (n.Flags & (NodeFlags.AccessDenied | NodeFlags.EnumerationError | NodeFlags.Unavailable | NodeFlags.Vanished | NodeFlags.CloudNotTraversed | NodeFlags.CloudPlaceholder)) == 0;

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
