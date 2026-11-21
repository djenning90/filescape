using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using FileScape.Core.Model;

namespace FileScape.Core.Scanning;

/// <summary>
/// The connected namespace for live data (AD-17, AD-18, LOC-01..05, LINK-01..08). One tree rooted at a
/// "This computer" hub whose children are location roots (drives, WSL distributions, other roots reached on demand).
/// The chosen folder is ensured as a path (its ancestors become <see cref="DirScanState.Unlisted"/> stubs that know
/// only the child on the way) and is traversed in the background; everything else is listed when visited.
/// </summary>
/// <remarks>
/// Threading: a coordinator thread (<see cref="Run"/>) owns all bookkeeping and assigns node ids, so batches stay in
/// id order (AD-04: the consumer remains the only writer of <see cref="FsTree"/>). Listings, path probes and link
/// resolutions run as worker tasks, several at a time, so one slow distribution or share blocks nothing else
/// (LOC-04). Requests from the UI thread are queued and never block. Links are never followed by background
/// traversal (FS-03); following is a deliberate request.
/// <para>
/// Listing priority, highest first: <c>Urgent</c> (what the user asked for: the place entered, a typed path, Retry),
/// <c>Prefetch</c> (the destinations of a visited place, so its gates show their state, LIST-01; also aerial reveals),
/// <c>Context</c> (a destination's own child folders, bounded per visited place) and <c>Background</c> (the arrival
/// subtree). <see cref="ListAhead"/> refuses system and network folders for everything but deliberate requests, and the
/// node budget (FS-10) keeps a reserve for deliberate requests so the destination stays reachable.
/// </para>
/// <para>
/// The app owns one explorer per connected world (<c>Main.World.cs</c>), posts requests from the UI thread and applies
/// the batches through <see cref="ScanSession"/>; replies to tickets come back in <see cref="ScanBatch.Replies"/>.
/// </para>
/// </remarks>
public sealed class WorldExplorer : IScanSource, IDisposable
{
    /// <summary>The reply ticket of the initial arrival path; tickets from <see cref="GoTo"/> and <see cref="FollowLink"/> start above it.</summary>
    public const int ArrivalTicket = 0;

    /// <summary>The hub node's name (node 0), as shown in the world and accepted by <see cref="LocationRequest.Parse"/>.</summary>
    public const string HubName = "This computer";

    /// <summary>How long the coordinator sleeps between passes while listings, path jobs or link resolutions are in flight, milliseconds (a wake-up from a finished task cuts it short).</summary>
    private const int BusyWaitMilliseconds = 15;

    /// <summary>How long it sleeps when nothing is in flight, milliseconds: a posted request or a finished worker task wakes it at once, so this only sets how often an idle loop re-checks (rationale for 15 and 250 not recorded).</summary>
    private const int IdleWaitMilliseconds = 250;

    /// <summary>The most links one path may pass through before it is called a loop (the same figure as Linux's MAXSYMLINKS; whether it was chosen for that is not recorded).</summary>
    private const int MaxLinkHops = 40;

    /// <summary>The smallest node reserve for deliberate requests, and the share of MaxNodes it is otherwise (1/50 = 2 %); see <see cref="Options.LimitReserve"/>.</summary>
    private const int MinLimitReserve = 64, LimitReserveDivisor = 50;

    /// <summary>
    /// Tuning for one explorer. The defaults are what the app uses; tests and probes shrink the limits to make
    /// budget and ordering cases small.
    /// </summary>
    public sealed record Options
    {
        /// <summary>Location roots for the hub; null discovers drives and WSL distributions.</summary>
        public IReadOnlyList<LocationSeed>? Locations { get; init; }

        /// <summary>Traverse the arrival folder's subtree in the background (off for broad places, LOC-02).</summary>
        public bool? Background { get; init; }

        /// <summary>
        /// The most listings in flight at once (LOC-04): enough that one slow share or sleeping distribution blocks
        /// nothing else, few enough not to flood a disk with parallel enumerations (rationale for 4 not recorded).
        /// </summary>
        public int MaxConcurrent { get; init; } = 4;

        /// <summary>At most this many child folders of a visited place are listed ahead (gate states, vistas).</summary>
        public int PrefetchLimit { get; init; } = 200;

        /// <summary>
        /// LIST-01: at most this many further folders (the child folders of the current place's destinations) are listed
        /// per visited place, one at a time and only when nothing the user is waiting for is queued.
        /// </summary>
        public int ContextLimit { get; init; } = 48;

        /// <summary>The WSL distribution that bare Linux paths (/home/…) map to; see <see cref="Locations.Normalize"/>.</summary>
        public string? DefaultDistribution { get; init; }

        /// <summary>
        /// Extra nodes, beyond the session's MaxNodes, that only deliberate requests (the place you enter, a path you
        /// type, a link you follow) may use, so the destination stays reachable after background listing has used the
        /// budget. Null: max(64, MaxNodes / 50).
        /// </summary>
        public int? LimitReserve { get; init; }

        /// <summary>Test hook, called on a worker just before a directory is enumerated.</summary>
        public Action<string>? BeforeEnumerate { get; init; }
    }

    /// <summary>The coordinator's own view of a folder's listing (finer than <see cref="DirScanState"/>, which is what the UI sees).</summary>
    private enum DirState { Unlisted, Queued, Listing, Listed, Failed }

    /// <summary>Listing priority, highest first (the order matters: <see cref="Enqueue"/> compares with &lt;). See the class remarks.</summary>
    private enum Priority { Urgent, Prefetch, Context, Background }

    /// <summary>
    /// The coordinator's record of one folder node: its path, its children by name (case-sensitive or not, as its volume)
    /// and its listing state, visit and budget bookkeeping. Coordinator thread only.
    /// </summary>
    private sealed class Dir
    {
        public required int Id;
        public required string Path;
        public required bool CaseSensitive;
        public Dictionary<string, int> Children = null!;
        public DirState State;
        public bool Background;
        public bool Visited;
        public bool Prefetched;
        public bool Retry; // listed again on request: the next listing clears the old failure flags
        public int Depth;
        public int Parent = -1;
        public int ContextLeft; // further-context folders this visited place may still have listed (LIST-01)
        public bool Held; // ListAhead refused it; the refusal note has been emitted once
        public Priority Priority = Priority.Background;
    }

    /// <summary>The coordinator's record of one link node: where it lives, what is known of its target, and the request tickets waiting on it.</summary>
    private sealed class LinkEntry
    {
        public required int Id;
        public required int ParentId;
        public required string Path;
        public required bool IsDirectory;
        public required LinkInfo Info;
        public bool Resolving;
        public readonly List<int> Waiting = [];
    }

    /// <summary>One directory entry as a worker read it, before the coordinator gives it an id.</summary>
    private sealed record Entry(string Name, NodeKind Kind, NodeFlags Flags, FileAttributes Attributes, long Size,
        DateTime? Created, DateTime? Modified, DateTime? Accessed, LinkInfo? Link);

    /// <param name="Truncated">The listing stopped at its entry cap (the node budget), so the folder is only partly listed.</param>
    private sealed record ListResult(int DirId, List<Entry> Entries, NodeFlags Failure, string? Error, bool Truncated = false);

    /// <summary>A worker's answer to "is this one name in that folder?" (<see cref="ProbeChild"/>): the entry, or why not.</summary>
    private sealed record ProbeResult(Entry? Entry, string? Error, NodeFlags Failure);

    /// <summary>A worker's answer to "where does this link point?" (<see cref="Resolve"/>): a reachable target path, or a status and reason.</summary>
    private sealed record ResolveResult(string? TargetPath, LinkStatus Status, string? Message);

    /// <summary>
    /// Reaching one path (LOC-01): walks component by component from its root through known nodes, probing for an
    /// unknown name instead of listing the parent, and continuing through links from their targets. Used for the
    /// arrival folder, typed paths and link targets.
    /// </summary>
    private sealed class PathJob
    {
        public required int Ticket;
        public required string Path;
        public string Root = "";
        public List<string> Remaining = []; // components still to walk below Current
        public int Current = -1; // the node reached so far
        public int Hops; // links passed through (see MaxLinkHops)
        public int ViaLink = -1;
        public bool Background;
        public bool Visit = true;
        public Task<ProbeResult>? Probe;
        public bool WaitingForLink;

        /// <summary>For link resolution jobs: the link whose target this ensures.</summary>
        public int ForLink = -1;
    }

    /// <summary>A request posted from the UI thread; the coordinator handles it on its next pass (<see cref="Handle"/>).</summary>
    private abstract record Request;

    /// <summary>From <see cref="GoTo"/>.</summary>
    private sealed record GoToRequest(int Ticket, string Path) : Request;

    /// <summary>From <see cref="Visit"/>.</summary>
    private sealed record VisitRequest(int Id) : Request;

    /// <summary>From <see cref="Reveal"/>.</summary>
    private sealed record RevealRequest(int Id) : Request;

    /// <summary>From <see cref="Expand"/>.</summary>
    private sealed record ExpandRequest(int Id) : Request;

    /// <summary>From <see cref="Discover"/>.</summary>
    private sealed record DiscoverRequest(int Id) : Request;

    /// <summary>From <see cref="FollowLink"/>.</summary>
    private sealed record FollowRequest(int Ticket, int LinkId) : Request;

    /// <summary>From <see cref="Retry"/>: a folder or a link id.</summary>
    private sealed record RetryRequest(int Id) : Request;

    /// <summary>From <see cref="CancelBackground"/>.</summary>
    private sealed record CancelBackgroundRequest : Request;

    /// <summary>A listing running on a worker, with its start timestamp (for the "waiting on the OS" status, FS-09).</summary>
    private sealed class Inflight
    {
        public required Task<ListResult> Task;
        public required Dir Dir;
        public required long Start;
    }

    private readonly string? _arrival;
    private readonly Options _o;
    private readonly ConcurrentQueue<Request> _requests = new();
    private readonly SemaphoreSlim _wake = new(0);
    private int _nextTicket = ArrivalTicket + 1;

    // ---- coordinator-only state
    private readonly Dictionary<int, Dir> _dirs = [];
    private readonly Dictionary<int, LinkEntry> _links = [];
    private readonly Dictionary<string, int> _roots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<Dir> _urgent = new(), _prefetch = new(), _context = new(), _background = new();
    private readonly List<Inflight> _inflight = [];
    private readonly List<PathJob> _jobs = [];
    private readonly List<(Task<ResolveResult> Task, LinkEntry Link)> _resolving = [];
    private ScanContext _ctx = null!;
    private ScanBatch _batch = new();
    private int _nextId;
    private bool _backgroundCancelled;

    public WorldExplorer(string? arrivalPath, Options? options = null)
    {
        _o = options ?? new Options();
        _arrival = arrivalPath == null ? null : Locations.Normalize(arrivalPath, _o.DefaultDistribution, OperatingSystem.IsWindows()) ?? arrivalPath;
    }

    public string? ArrivalPath => _arrival;

    public void Dispose() => _wake.Dispose();

    public DataSourceInfo Describe() =>
        new(DataOrigin.LiveFilesystem, _arrival ?? HubName, "Live filesystem (metadata only), explored on demand");

    // ------------------------------------------------------------------ requests (any thread)

    /// <summary>Reaches a typed or pasted path (LOC-01). The reply carries the node id, or why not.</summary>
    public int GoTo(string path)
    {
        var ticket = Interlocked.Increment(ref _nextTicket);
        Post(new GoToRequest(ticket, path));
        return ticket;
    }

    /// <summary>The user is in this folder: list it now, then its child folders (gate states, vistas).</summary>
    public void Visit(int dirId) => Post(new VisitRequest(dirId));

    /// <summary>
    /// Lists a folder for the aerial world (AERIAL-04) without counting it as visited: queued behind listings the user
    /// is waiting for, never for a location root (no drive or distribution is opened from the air), and only once. The
    /// caller decides which folders may be revealed (the app limits it to the arrival subtree and visited places).
    /// </summary>
    public void Reveal(int dirId) => Post(new RevealRequest(dirId));

    /// <summary>
    /// Lists one folder because the player opened it in the Destinations tree (PLANET-39, AD-56): at the urgent priority
    /// (they are looking at it), but without counting it as visited, so its own child folders are not listed ahead and
    /// no context is spent on it. Only a folder that was never listed (or failed and was retried) is listed; a queued one
    /// moves ahead. Location roots are listed too: opening one in the list is as deliberate as going there.
    /// </summary>
    public void Expand(int dirId) => Post(new ExpandRequest(dirId));

    /// <summary>
    /// Lists a folder for the overview's discovery (PLANET-76, AD-71; the app's scheduler is
    /// <see cref="Planets.OverviewDiscovery"/>): like <see cref="Reveal"/> it is not a visit (no destinations or context
    /// are listed ahead of it), never opens a location root, lists only a folder never listed (one waiting in the
    /// background walk is brought forward of the walk, PLANET-196), and waits behind listings the user is waiting for
    /// (prefetch priority). Unlike Reveal it goes through <see cref="ListAhead"/> as background
    /// listing does, so Linux system filesystems and network shares are refused with their note. Entries and metadata
    /// only, within the node budget's non-reserved part (FS-10).
    /// </summary>
    public void Discover(int dirId) => Post(new DiscoverRequest(dirId));

    /// <summary>Follows a link deliberately (LINK-02). The reply carries the destination id, or why not.</summary>
    public int FollowLink(int linkId)
    {
        var ticket = Interlocked.Increment(ref _nextTicket);
        Post(new FollowRequest(ticket, linkId));
        return ticket;
    }

    /// <summary>Lists a folder again (after a failure) or re-resolves a link.</summary>
    public void Retry(int id) => Post(new RetryRequest(id));

    /// <summary>Stops background traversal; on-demand listing continues (FS-09 for the arrival subtree).</summary>
    public void CancelBackground() => Post(new CancelBackgroundRequest());

    private void Post(Request r)
    {
        _requests.Enqueue(r);
        _wake.Release();
    }

    // ------------------------------------------------------------------ coordinator

    /// <summary>
    /// The coordinator loop: creates the hub and its location roots, starts the arrival path, then repeats one pass
    /// (handle requests, advance path jobs, apply finished resolutions and listings, start new listings, emit one batch)
    /// until cancelled. It sleeps between passes; posted requests and finished worker tasks wake it.
    /// </summary>
    public void Run(ScanContext context, CancellationToken ct)
    {
        _ctx = context;
        var seeds = _o.Locations ?? Locations.Discover();
        var hub = NewNode(-1, HubName, NodeKind.Directory, 0, 0, null, null, null, 0, DirScanState.Complete, null, "", LocationKind.Hub);
        _dirs[hub] = new Dir { Id = hub, Path = "", CaseSensitive = false, State = DirState.Listed, Children = new(StringComparer.OrdinalIgnoreCase) };
        foreach (var seed in seeds)
        {
            AddLocation(seed.Path, seed.Name, seed.Kind, seed.CaseSensitive);
        }

        context.Directories = _dirs.Count;
        if (_arrival != null)
        {
            var background = _o.Background ?? !Locations.IsBroad(_arrival);
            StartJob(new PathJob { Ticket = ArrivalTicket, Path = _arrival, Background = background });
        }

        Flush();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                while (_requests.TryDequeue(out var r)) Handle(r);
                AdvanceJobs();
                CompleteResolutions();
                CompleteListings();
                StartListings();
                Flush();
                ReportProgress();
                _wake.Wait(_inflight.Count + _jobs.Count + _resolving.Count > 0 ? BusyWaitMilliseconds : IdleWaitMilliseconds, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }

        context.Idle = true;
        context.BackgroundActive = false;
        context.SetOperation(null, 0);
    }

    private void Flush()
    {
        if (_batch.IsEmpty) return;
        _ctx.Emit(_batch);
        _batch = new ScanBatch();
    }

    private void ReportProgress()
    {
        _ctx.Pending = _urgent.Count + _prefetch.Count + _context.Count + _background.Count + _inflight.Count;
        _ctx.BackgroundActive = !_backgroundCancelled && (_background.Count > 0 || _inflight.Any(i => i.Dir.Background));
        _ctx.Idle = _ctx.Pending == 0 && _jobs.Count == 0 && _resolving.Count == 0;
        if (_inflight.Count == 0)
        {
            _ctx.SetOperation(null, 0);
        }
        else
        {
            var oldest = _inflight.MinBy(i => i.Start)!;
            _ctx.SetOperation(oldest.Dir.Path, oldest.Start);
        }
    }

    /// <summary>Adds a node to the outgoing batch with the next id, counts it, and registers it under its parent's name map.</summary>
    private int NewNode(int parent, string name, NodeKind kind, NodeFlags flags, long size, DateTime? created, DateTime? modified, DateTime? accessed,
        FileAttributes attributes, DirScanState state, LinkInfo? link, string? locationPath = null, LocationKind location = LocationKind.None)
    {
        var id = _nextId++;
        var depth = parent < 0 ? 0 : _dirs.TryGetValue(parent, out var pdir) ? pdir.Depth + 1 : 1;
        _batch.NewNodes.Add(new NodeRecord(id, parent, name, kind, flags, attributes, size, created, modified, accessed, depth, state, link, locationPath, location));
        _ctx.Nodes++;
        switch (kind)
        {
            case NodeKind.File: _ctx.Files++; _ctx.LogicalBytes += size; break;
            case NodeKind.Link: _ctx.Links++; break;
            case NodeKind.Directory: _ctx.Directories++; break;
        }

        if (parent >= 0 && _dirs.TryGetValue(parent, out var pd)) pd.Children[name] = id;
        return id;
    }

    private int AddLocation(string root, string name, LocationKind kind, bool caseSensitive)
    {
        var id = NewNode(0, name, NodeKind.Directory, 0, 0, null, null, null, 0, DirScanState.Unlisted, null, root, kind);
        _dirs[id] = new Dir { Id = id, Path = root, CaseSensitive = caseSensitive, Children = new(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase), Depth = 1 };
        _roots[root] = id;
        return id;
    }

    private void Handle(Request r)
    {
        switch (r)
        {
            case GoToRequest g:
                StartJob(new PathJob { Ticket = g.Ticket, Path = g.Path });
                break;
            case VisitRequest v when _dirs.TryGetValue(v.Id, out var d):
                MarkVisited(d);
                if (d.State is DirState.Unlisted or DirState.Queued || (d.State == DirState.Failed && d.Retry)) Enqueue(d, Priority.Urgent);
                else if (d.State == DirState.Listed) Prefetch(d);
                // The way back is a destination too: list a parent known only as a stub on the way (LIST-01).
                if (d.Parent > 0 && _dirs.TryGetValue(d.Parent, out var up) && up.State == DirState.Unlisted && !up.Retry) Ahead(up, Priority.Prefetch, ListAheadKind.Destination);
                break;
            case VisitRequest:
                break;
            case RevealRequest rv when rv.Id != 0 && !_roots.ContainsValue(rv.Id) && _dirs.TryGetValue(rv.Id, out var d):
                if (d.State == DirState.Unlisted && !d.Retry) Enqueue(d, Priority.Prefetch);
                break;
            case RevealRequest:
                break;
            case ExpandRequest x when x.Id != 0 && _dirs.TryGetValue(x.Id, out var d):
                if (d.State is DirState.Unlisted or DirState.Queued) Enqueue(d, Priority.Urgent);
                break;
            case ExpandRequest:
                break;
            case DiscoverRequest dr when dr.Id != 0 && !_roots.ContainsValue(dr.Id) && _dirs.TryGetValue(dr.Id, out var d):
                // PLANET-196: a folder waiting in the background walk is brought forward too (before, only a folder never
                // queued was taken, so discovery could not hurry one the walk had queued behind every other).
                if (d.State == DirState.Unlisted && !d.Retry || d.State == DirState.Queued && d.Priority == Priority.Background) Ahead(d, Priority.Prefetch, ListAheadKind.Background);
                break;
            case DiscoverRequest:
                break;
            case FollowRequest f:
                Follow(f.Ticket, f.LinkId);
                break;
            case RetryRequest rr when _dirs.TryGetValue(rr.Id, out var d):
                if (d.State is DirState.Listed or DirState.Failed)
                {
                    d.State = DirState.Unlisted;
                    d.Retry = true;
                }

                Enqueue(d, Priority.Urgent);
                break;
            case RetryRequest rr when _links.TryGetValue(rr.Id, out var l):
                l.Info = l.Info with { Status = LinkStatus.Unresolved, Message = null, TargetId = -1 };
                Follow(-1, l.Id);
                break;
            case CancelBackgroundRequest:
                _backgroundCancelled = true;
                while (_background.TryDequeue(out var bd))
                {
                    if (bd.State != DirState.Queued) continue;
                    bd.State = DirState.Unlisted;
                    _batch.DirectoryUpdates.Add(new DirectoryUpdate(bd.Id, DirScanState.Cancelled, NodeFlags.Incomplete));
                }

                break;
        }
    }

    // ------------------------------------------------------------------ listing

    /// <summary>
    /// Queues a folder for listing at <paramref name="p"/>. A queued folder is only re-queued for Urgent (so a request
    /// jumps ahead; the stale queue entry is skipped by <see cref="Next"/>), and the UI is told "queued" except for
    /// background traversal, which stays quiet.
    /// </summary>
    private void Enqueue(Dir d, Priority p)
    {
        if (d.State is DirState.Listing or DirState.Listed) return;
        // A queued folder moves up only to Urgent, or out of the background walk into a higher queue (PLANET-196).
        var fromWalk = d.State == DirState.Queued && d.Priority == Priority.Background && p < Priority.Background;
        if (d.State == DirState.Queued && p != Priority.Urgent && !fromWalk) return;
        if ((d.State != DirState.Queued || fromWalk) && p != Priority.Background)
        {
            _batch.DirectoryUpdates.Add(new DirectoryUpdate(d.Id, DirScanState.Pending));
        }

        d.State = DirState.Queued;
        if (p < d.Priority || d.Priority == Priority.Background) d.Priority = p;
        // Observable effect: Priority.Context falls to the _ arm, so context folders wait in _background, not _context,
        // and the idle-time gate in Next() does not hold them back (they still come after Urgent and Prefetch). Whether
        // that is intended is not recorded; LIST-01's tests check the context budget, not this ordering.
        (p switch { Priority.Urgent => _urgent, Priority.Prefetch => _prefetch, _ => _background }).Enqueue(d);
    }

    private void Prefetch(Dir d)
    {
        if (d.Prefetched || d.Id == 0) return; // the hub's locations are listed only when entered (no waking distributions)
        d.Prefetched = true;
        var n = 0;
        foreach (var id in d.Children.Values.OrderBy(i => i))
        {
            if (n >= _o.PrefetchLimit) break;
            if (_dirs.TryGetValue(id, out var c) && c.State == DirState.Unlisted && !c.Retry && Ahead(c, Priority.Prefetch, ListAheadKind.Destination))
            {
                n++;
            }
        }
    }

    /// <summary>A visited place gets its own budget of further context once (LIST-01).</summary>
    private void MarkVisited(Dir d)
    {
        if (!d.Visited) d.ContextLeft = _o.ContextLimit;
        d.Visited = true;
    }

    /// <summary>Queues a folder to be listed ahead of the user unless <see cref="ListAhead"/> refuses it (then says why, once).</summary>
    private bool Ahead(Dir c, Priority p, ListAheadKind kind)
    {
        if (ListAhead.Refusal(c.Path, kind) is { } why)
        {
            if (!c.Held)
            {
                c.Held = true;
                _batch.DirectoryUpdates.Add(new DirectoryUpdate(c.Id, DirScanState.Unlisted, Error: why));
            }

            return false;
        }

        Enqueue(c, p);
        return true;
    }

    /// <summary>
    /// LIST-01: after a destination of a visited place is listed, its own child folders become further context, within
    /// the visited place's budget, so the next step's gates and the horizon show real states instead of "not listed yet".
    /// </summary>
    private void QueueContext(Dir listed)
    {
        if (listed.Priority != Priority.Prefetch || listed.Parent <= 0 || !_dirs.TryGetValue(listed.Parent, out var visited) || !visited.Visited) return;
        foreach (var id in listed.Children.Values.OrderBy(i => i))
        {
            if (visited.ContextLeft <= 0) return;
            if (_dirs.TryGetValue(id, out var c) && c.State == DirState.Unlisted && !c.Retry && Ahead(c, Priority.Context, ListAheadKind.Context))
            {
                visited.ContextLeft--;
            }
        }
    }

    private int Reserve => _o.LimitReserve ?? Math.Max(MinLimitReserve, _ctx.MaxNodes / LimitReserveDivisor);

    /// <summary>
    /// Entries a listing of this priority may still add to the tree (FS-10, 2026-11-05). The tree never exceeds
    /// MaxNodes (plus <see cref="Reserve"/> for deliberate requests): the budget is checked when a listing is dispatched
    /// (its worker stops enumerating there) and again when it is applied. Transient buffering is bounded by
    /// MaxConcurrent × the remaining budget — the limit bounds the tree, it is not an exact memory ceiling.
    /// </summary>
    private int Budget(Priority p) =>
        Math.Max(0, (p == Priority.Urgent ? _ctx.MaxNodes + Reserve : _ctx.MaxNodes) - _ctx.Nodes);

    /// <summary>Dispatches queued folders to workers, highest priority first, up to MaxConcurrent, each capped by the remaining budget.</summary>
    private void StartListings()
    {
        while (_inflight.Count < _o.MaxConcurrent && Next() is { } d)
        {
            var cap = Budget(d.Priority);
            if (cap == 0)
            {
                // The budget is used up: background and prefetched folders stay unlisted, honestly marked.
                _ctx.LimitReached = true;
                d.State = DirState.Unlisted;
                _batch.DirectoryUpdates.Add(new DirectoryUpdate(d.Id, DirScanState.NotTraversed, NodeFlags.Incomplete,
                    $"Not listed: FileScape's entry limit ({_ctx.MaxNodes:N0}) was reached."));
                continue;
            }

            d.State = DirState.Listing;
            if (d.Priority != Priority.Background) _batch.DirectoryUpdates.Add(new DirectoryUpdate(d.Id, DirScanState.Listing));
            var path = d.Path;
            var hook = _o.BeforeEnumerate;
            var id = d.Id;
            var task = Task.Run(() => List(id, path, hook, cap));
            task.ContinueWith(_ => _wake.Release(), TaskScheduler.Default);
            _inflight.Add(new Inflight { Task = task, Dir = d, Start = Stopwatch.GetTimestamp() });
        }
    }

    private Dir? Next()
    {
        // Further context waits for idle time: nothing the user is waiting for queued or in flight, one at a time.
        var contextAllowed = _inflight.All(i => i.Dir.Priority is Priority.Background) && !HasQueued(_urgent) && !HasQueued(_prefetch);
        foreach (var q in new[] { _urgent, _prefetch, _context, _background })
        {
            if (q == _context && !contextAllowed) continue;
            while (q.TryDequeue(out var d))
            {
                if (d.State == DirState.Queued) return d;
            }
        }

        return null;
    }

    private static bool HasQueued(Queue<Dir> q) => q.Any(d => d.State == DirState.Queued);

    private void CompleteListings()
    {
        for (var i = _inflight.Count - 1; i >= 0; i--)
        {
            var f = _inflight[i];
            if (!f.Task.IsCompleted) continue;
            _inflight.RemoveAt(i);
            var r = f.Task.IsFaulted
                ? new ListResult(f.Dir.Id, [], NodeFlags.EnumerationError, f.Task.Exception?.InnerException?.Message)
                : f.Task.Result;
            Apply(f.Dir, r);
        }
    }

    /// <summary>
    /// Applies a finished listing: new entries become nodes (names already known from a path stub are merged, not
    /// duplicated), the budget is re-checked, and the folder's final state and flags are emitted. A listed visited place
    /// then lists its destinations ahead, and a listed destination queues its context.
    /// </summary>
    private void Apply(Dir d, ListResult r)
    {
        var hadChildren = d.Children.Count > 0;
        var created = 0;
        var room = Budget(d.Priority); // other listings may have used budget since this one was dispatched
        var truncated = r.Truncated;
        var kept = 0;
        foreach (var e in r.Entries)
        {
            if (d.Children.ContainsKey(e.Name)) continue; // already known (an ancestor stub's child): merge
            if (created >= room)
            {
                truncated = true;
                break;
            }

            AddEntry(d, e);
            created++;
        }

        kept = d.Children.Count;

        var state = DirScanState.Complete;
        var add = NodeFlags.None;
        var error = r.Error;
        if (truncated)
        {
            // Only part of the folder fits the budget: say so, never call it complete (FS-08, FS-10).
            _ctx.LimitReached = true;
            add |= NodeFlags.Incomplete;
            error = $"Only {kept:N0} entries are listed: FileScape's entry limit ({_ctx.MaxNodes:N0}) was reached. Start fresh here (Locations) to explore from this folder with a new budget.";
            LargestTruncatedListing = Math.Max(LargestTruncatedListing, r.Entries.Count);
        }

        if (r.Failure != NodeFlags.None)
        {
            state = r.Entries.Count == 0 ? DirScanState.Failed : DirScanState.Complete;
            add = r.Failure | (r.Entries.Count == 0 ? 0 : NodeFlags.Incomplete);
            _ctx.Problems++;
        }

        d.State = r.Failure != NodeFlags.None && r.Entries.Count == 0 ? DirState.Failed : DirState.Listed;
        var clear = d.Retry ? NodeFlags.AccessDenied | NodeFlags.EnumerationError | NodeFlags.Vanished | NodeFlags.Unavailable | NodeFlags.Incomplete : 0;
        d.Retry = false;
        _batch.DirectoryUpdates.Add(new DirectoryUpdate(d.Id, state, add, error, clear, SortChildren: hadChildren && created > 0));
        if (d.Visited) Prefetch(d);
        QueueContext(d);
    }

    /// <summary>Adds one listed or probed entry under its parent; a subfolder of a background folder is queued for background traversal unless refused.</summary>
    private void AddEntry(Dir parent, Entry e)
    {
        var state = DirScanState.NotApplicable;
        var queueBackground = false;
        if (e.Kind == NodeKind.Directory)
        {
            state = (e.Flags & NodeFlags.CloudNotTraversed) != 0 ? DirScanState.NotTraversed : DirScanState.Unlisted;
            if (state == DirScanState.Unlisted && parent.Background && !_backgroundCancelled
                && ListAhead.Refusal(Path.Join(parent.Path, e.Name), ListAheadKind.Background) == null)
            {
                state = DirScanState.Pending;
                queueBackground = true;
            }
        }

        var id = NewNode(parent.Id, e.Name, e.Kind, e.Flags, e.Size, e.Created, e.Modified, e.Accessed, e.Attributes, state, e.Link);
        var path = Path.Join(parent.Path, e.Name);
        if (e.Kind == NodeKind.Directory)
        {
            var d = new Dir
            {
                Id = id,
                Path = path,
                CaseSensitive = parent.CaseSensitive,
                Children = new(parent.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase),
                Background = queueBackground,
                Parent = parent.Id,
                State = state == DirScanState.NotTraversed ? DirState.Listed : DirState.Unlisted,
                Depth = parent.Depth + 1,
            };
            _dirs[id] = d;
            if (queueBackground)
            {
                d.State = DirState.Queued;
                _background.Enqueue(d);
            }
        }
        else if (e.Kind == NodeKind.Link && e.Link != null)
        {
            _links[id] = new LinkEntry { Id = id, ParentId = parent.Id, Path = path, IsDirectory = (e.Flags & NodeFlags.DirectoryLink) != 0, Info = e.Link };
        }
    }

    private static readonly EnumerationOptions ListOptions = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
        BufferSize = 64 * 1024, // bytes, as DirectoryScanner's Options
    };

    /// <summary>The most entries a truncated listing kept (tests check that buffering respected the budget).</summary>
    public int LargestTruncatedListing { get; private set; }

    /// <summary>
    /// Worker: one directory listing, metadata only; reparse entries classified by tag. It stops after
    /// <paramref name="cap"/> entries (plus one, to know the folder is larger), so a huge folder is never buffered
    /// beyond the node budget. A truncated listing keeps the first entries in the OS's enumeration order.
    /// </summary>
    private static ListResult List(int id, string path, Action<string>? hook, int cap)
    {
        var entries = new List<Entry>();
        var truncated = false;
        try
        {
            hook?.Invoke(path);
            foreach (var e in new FileSystemEnumerable<Entry>(path, ToEntry, ListOptions))
            {
                if (entries.Count >= cap)
                {
                    truncated = true;
                    break;
                }

                entries.Add(Classify(path, e));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ListResult(id, entries, NodeFlags.AccessDenied, ex.Message);
        }
        catch (DirectoryNotFoundException ex)
        {
            return new ListResult(id, entries, RootReachable(path) ? NodeFlags.Vanished : NodeFlags.Unavailable, ex.Message);
        }
        catch (IOException ex)
        {
            return new ListResult(id, entries, RootReachable(path) ? NodeFlags.EnumerationError : NodeFlags.Unavailable, ex.Message);
        }

        entries.Sort(static (a, b) =>
        {
            var k = Order(a.Kind).CompareTo(Order(b.Kind));
            if (k != 0) return k;
            k = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            return k != 0 ? k : StringComparer.Ordinal.Compare(a.Name, b.Name);
        });
        return new ListResult(id, entries, NodeFlags.None, null, truncated);
    }

    private static int Order(NodeKind k) => k switch { NodeKind.Directory => 0, NodeKind.Link => 1, _ => 2 };

    /// <summary>
    /// Whether the path's root (drive, share, distribution) answers at all: tells "this folder is gone" (Vanished,
    /// Broken) from "its whole location is unavailable right now".
    /// </summary>
    private static bool RootReachable(string path)
    {
        try
        {
            var (root, _) = Locations.Split(path);
            return Directory.Exists(root.EndsWith('\\') || root == "/" ? root : root + "\\");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Entry ToEntry(ref FileSystemEntry e)
    {
        var attrs = e.Attributes;
        var flags = DirectoryScanner.FlagsFor(attrs);
        var kind = e.IsDirectory ? NodeKind.Directory : NodeKind.File;
        if (kind == NodeKind.Directory && !DirectoryScanner.ShouldEnumerateDirectory(attrs)) flags |= NodeFlags.CloudNotTraversed;
        return new Entry(e.FileName.ToString(), kind, flags, attrs, kind == NodeKind.File ? e.Length : 0,
            Time(e.CreationTimeUtc), Time(e.LastWriteTimeUtc), Time(e.LastAccessTimeUtc), null);
    }

    // As DirectoryScanner.SafeTime: the FILETIME epoch (1601) means "no time".
    private static DateTime? Time(DateTimeOffset t) => t.UtcDateTime.Year <= 1601 ? null : t.UtcDateTime;

    /// <summary>Reparse entries become what their tag says (LINK-01); others are unchanged.</summary>
    private static Entry Classify(string dir, Entry e)
    {
        if ((e.Attributes & FileAttributes.ReparsePoint) == 0) return e;
        var full = Path.Join(dir, e.Name);
        var isDir = e.Kind == NodeKind.Directory;
        var c = Reparse.Classify(isDir, Reparse.ReadTag(full), () => Reparse.ReadTarget(full, isDir));
        var flags = (e.Flags & ~NodeFlags.CloudNotTraversed) | c.ExtraFlags;
        if (c.Kind == NodeKind.Directory && !DirectoryScanner.ShouldEnumerateDirectory(e.Attributes)) flags |= NodeFlags.CloudNotTraversed;
        return e with { Kind = c.Kind, Flags = flags, Size = c.Kind == NodeKind.File ? e.Size : 0, Link = c.Link };
    }

    // ------------------------------------------------------------------ paths (LOC-01, AD-17)

    /// <summary>Normalises a job's path, starts it at its root (adding the root as a location if it is new) and registers it; an unusable path is answered at once.</summary>
    private void StartJob(PathJob job)
    {
        var normalized = Locations.Normalize(job.Path, _o.DefaultDistribution, OperatingSystem.IsWindows());
        if (normalized == null || !(normalized.StartsWith('/') || normalized.StartsWith(@"\\", StringComparison.Ordinal) || (normalized.Length >= 2 && normalized[1] == ':')))
        {
            Reply(job, -1, $"“{job.Path}” is not a full path FileScape understands (try C:\\folder, \\\\wsl.localhost\\Distro\\path or /home/you).");
            return;
        }

        var (root, parts) = Locations.Split(normalized);
        job.Root = root;
        job.Remaining = parts;
        if (!_roots.TryGetValue(root, out var rootId))
        {
            rootId = AddLocation(root, Locations.NameOf(root), Locations.KindOf(root), Locations.IsCaseSensitive(root));
        }

        job.Current = rootId;
        _jobs.Add(job);
    }

    /// <summary>Moves every path job on: applies a finished probe (the found entry becomes a stub under its parent) and steps on.</summary>
    private void AdvanceJobs()
    {
        for (var i = _jobs.Count - 1; i >= 0; i--)
        {
            var job = _jobs[i];
            if (job.WaitingForLink) continue;
            if (job.Probe is { } probe)
            {
                if (!probe.IsCompleted) continue;
                job.Probe = null;
                var r = probe.IsFaulted ? new ProbeResult(null, probe.Exception?.InnerException?.Message, NodeFlags.EnumerationError) : probe.Result;
                if (r.Entry == null)
                {
                    _jobs.RemoveAt(i);
                    Reply(job, -1, r.Failure == NodeFlags.Unavailable
                        ? $"“{_dirs[job.Current].Path}” is not reachable right now ({r.Error})."
                        : $"“{Path.Join(_dirs[job.Current].Path, job.Remaining[0])}” {(r.Failure == NodeFlags.AccessDenied ? "cannot be reached: access denied" : "does not exist")}.");
                    continue;
                }

                AddEntry(_dirs[job.Current], r.Entry);
            }

            if (Step(job)) _jobs.RemoveAt(i);
        }
    }

    /// <summary>Walks a job as far as known nodes allow. Returns true when the job is finished.</summary>
    private bool Step(PathJob job)
    {
        while (true)
        {
            if (job.Remaining.Count == 0)
            {
                Arrived(job, job.Current);
                return true;
            }

            var dir = _dirs[job.Current];
            var name = job.Remaining[0];
            if (dir.Children.TryGetValue(name, out var childId))
            {
                if (_dirs.ContainsKey(childId))
                {
                    job.Current = childId;
                    job.Remaining.RemoveAt(0);
                    continue;
                }

                if (_links.TryGetValue(childId, out var link))
                {
                    // A path through a link continues from the link's target (LINK-03: entry path ≠ resolved path).
                    if (++job.Hops > MaxLinkHops)
                    {
                        Reply(job, -1, "Too many links in a row (a loop).");
                        return true;
                    }

                    job.Remaining.RemoveAt(0);
                    if (link.Info.Status == LinkStatus.Resolved && link.Info.TargetPath != null)
                    {
                        Restart(job, link.Info.TargetPath);
                        continue;
                    }

                    if (!link.Info.CanFollow)
                    {
                        Reply(job, -1, link.Info.Message ?? $"“{name}” cannot be followed.");
                        return true;
                    }

                    job.WaitingForLink = true;
                    StartResolve(link);
                    _pendingThroughLink.Add((job, link.Id));
                    return false;
                }

                // A file: only valid as the last component.
                if (job.Remaining.Count == 1)
                {
                    Arrived(job, childId);
                    return true;
                }

                Reply(job, -1, $"“{name}” is a file, not a folder.");
                return true;
            }

            // Unknown child: probe for it (a stub is created without listing the parent).
            var parentPath = dir.Path;
            var caseSensitive = dir.CaseSensitive;
            job.Probe = Task.Run(() => ProbeChild(parentPath, name, caseSensitive));
            job.Probe.ContinueWith(_ => _wake.Release(), TaskScheduler.Default);
            return false;
        }
    }

    /// <summary>Path jobs paused at a link in the middle of their path, until the link's target is known.</summary>
    private readonly List<(PathJob Job, int LinkId)> _pendingThroughLink = [];

    /// <summary>Continues a job from a link's target: the target's components, then whatever was left after the link.</summary>
    private void Restart(PathJob job, string newPath)
    {
        var (root, parts) = Locations.Split(newPath);
        parts.AddRange(job.Remaining);
        job.Remaining = parts;
        job.Root = root;
        if (!_roots.TryGetValue(root, out var rootId))
        {
            rootId = AddLocation(root, Locations.NameOf(root), Locations.KindOf(root), Locations.IsCaseSensitive(root));
        }

        job.Current = rootId;
    }

    /// <summary>
    /// A job reached its node: a link-resolution job records the link as resolved and answers its waiters; otherwise
    /// the folder is marked visited (and for the arrival, background), listed now if unlisted, and the ticket answered.
    /// </summary>
    private void Arrived(PathJob job, int nodeId)
    {
        if (job.ForLink >= 0)
        {
            var link = _links[job.ForLink];
            link.Info = link.Info with { Status = LinkStatus.Resolved, TargetId = nodeId };
            _batch.LinkUpdates.Add(new LinkUpdate(link.Id, link.Info));
            FinishLinkWaiters(link, nodeId, null);
            return;
        }

        if (_dirs.TryGetValue(nodeId, out var d))
        {
            if (job.Background && !_backgroundCancelled)
            {
                d.Background = true;
            }

            if (job.Visit) MarkVisited(d);
            if (d.State is DirState.Unlisted) Enqueue(d, Priority.Urgent);
            else if (d.State == DirState.Listed && d.Visited) Prefetch(d);
        }

        Reply(job, nodeId, null);
    }

    private void Reply(PathJob job, int nodeId, string? error)
    {
        if (job.Ticket < 0) return;
        _batch.Replies.Add(new ExplorerReply(job.Ticket, nodeId, error, job.ViaLink));
    }

    /// <summary>Worker: finds one entry in a folder without emitting the rest (real name casing, attributes, kind).</summary>
    private static ProbeResult ProbeChild(string parent, string name, bool caseSensitive)
    {
        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        try
        {
            var en = new FileSystemEnumerable<Entry>(parent, ToEntry, ListOptions)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => e.FileName.Equals(name, cmp),
            };
            foreach (var e in en) return new ProbeResult(Classify(parent, e), null, 0);
            return new ProbeResult(null, "not found", NodeFlags.Vanished);
        }
        catch (UnauthorizedAccessException)
        {
            // The folder cannot be listed but may still be traversable: fall back to the name as given.
            return StatFallback(parent, name);
        }
        catch (DirectoryNotFoundException ex)
        {
            return new ProbeResult(null, ex.Message, RootReachable(parent) ? NodeFlags.Vanished : NodeFlags.Unavailable);
        }
        catch (IOException ex)
        {
            return new ProbeResult(null, ex.Message, RootReachable(parent) ? NodeFlags.EnumerationError : NodeFlags.Unavailable);
        }
    }

    /// <summary>When the parent cannot be listed (access denied), reads the named entry directly: a traverse-only folder still lets you through.</summary>
    private static ProbeResult StatFallback(string parent, string name)
    {
        var full = Path.Join(parent, name);
        try
        {
            var info = new DirectoryInfo(full);
            if (!info.Exists)
            {
                var file = new FileInfo(full);
                if (!file.Exists) return new ProbeResult(null, "access denied", NodeFlags.AccessDenied);
                var fe = new Entry(name, NodeKind.File, DirectoryScanner.FlagsFor(file.Attributes), file.Attributes, file.Length, file.CreationTimeUtc, file.LastWriteTimeUtc, file.LastAccessTimeUtc, null);
                return new ProbeResult(Classify(parent, fe), null, 0);
            }

            var de = new Entry(name, NodeKind.Directory, DirectoryScanner.FlagsFor(info.Attributes), info.Attributes, 0, info.CreationTimeUtc, info.LastWriteTimeUtc, info.LastAccessTimeUtc, null);
            return new ProbeResult(Classify(parent, de), null, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ProbeResult(null, ex.Message, NodeFlags.AccessDenied);
        }
    }

    // ------------------------------------------------------------------ links (AD-18)

    /// <summary>
    /// A deliberate follow (LINK-02): a resolved link answers at once with its target (listed if needed); an
    /// unfollowable one answers why; otherwise the ticket waits while the link is resolved. Ticket −1: no reply wanted.
    /// </summary>
    private void Follow(int ticket, int linkId)
    {
        if (!_links.TryGetValue(linkId, out var link))
        {
            if (ticket >= 0) _batch.Replies.Add(new ExplorerReply(ticket, -1, "That is not a link FileScape can follow.", linkId));
            return;
        }

        if (link.Info.Status == LinkStatus.Resolved && link.Info.TargetId >= 0)
        {
            if (_dirs.TryGetValue(link.Info.TargetId, out var d))
            {
                MarkVisited(d);
                if (d.State == DirState.Unlisted) Enqueue(d, Priority.Urgent);
            }

            if (ticket >= 0) _batch.Replies.Add(new ExplorerReply(ticket, link.Info.TargetId, null, linkId));
            return;
        }

        if (!link.Info.CanFollow)
        {
            if (ticket >= 0) _batch.Replies.Add(new ExplorerReply(ticket, -1, Explain(link.Info), linkId));
            return;
        }

        if (ticket >= 0) link.Waiting.Add(ticket);
        StartResolve(link);
    }

    /// <summary>Starts resolving a link on a worker, once (later callers join the one resolution).</summary>
    private void StartResolve(LinkEntry link)
    {
        if (link.Resolving) return;
        link.Resolving = true;
        var path = link.Path;
        var info = link.Info;
        var isDir = link.IsDirectory;
        var task = Task.Run(() => Resolve(path, isDir, info));
        task.ContinueWith(_ => _wake.Release(), TaskScheduler.Default);
        _resolving.Add((task, link));
    }

    /// <summary>Applies finished link resolutions, then resumes (or fails) the path jobs that were paused at those links.</summary>
    private void CompleteResolutions()
    {
        for (var i = _resolving.Count - 1; i >= 0; i--)
        {
            var (task, link) = _resolving[i];
            if (!task.IsCompleted) continue;
            _resolving.RemoveAt(i);
            link.Resolving = false;
            var r = task.IsFaulted ? new ResolveResult(null, LinkStatus.Unavailable, task.Exception?.InnerException?.Message) : task.Result;
            if (r.TargetPath == null)
            {
                link.Info = link.Info with { Status = r.Status, Message = r.Message };
                _batch.LinkUpdates.Add(new LinkUpdate(link.Id, link.Info));
                FinishLinkWaiters(link, -1, Explain(link.Info));
                continue;
            }

            link.Info = link.Info with { TargetPath = r.TargetPath };
            // Ensure the target as a path in the same tree: an ancestor or a known folder is reused (LINK-05).
            var job = new PathJob { Ticket = -1, Path = r.TargetPath, ForLink = link.Id, Visit = true };
            StartJob(job);
        }

        // Paths that were waiting for a link in their middle continue from its target, or fail with its reason.
        for (var i = _pendingThroughLink.Count - 1; i >= 0; i--)
        {
            var (job, linkId) = _pendingThroughLink[i];
            var link = _links[linkId];
            if (link.Resolving) continue;
            _pendingThroughLink.RemoveAt(i);
            if (link.Info.TargetPath != null)
            {
                job.WaitingForLink = false;
                Restart(job, link.Info.TargetPath);
            }
            else
            {
                _jobs.Remove(job);
                Reply(job, -1, Explain(link.Info));
            }
        }
    }

    private void FinishLinkWaiters(LinkEntry link, int targetId, string? error)
    {
        foreach (var t in link.Waiting)
        {
            _batch.Replies.Add(new ExplorerReply(t, targetId, error, link.Id));
        }

        link.Waiting.Clear();
    }

    /// <summary>The user-facing sentence for a link that cannot be followed: its own message if it has one, else one per status.</summary>
    public static string Explain(LinkInfo info) => info.Message ?? info.Status switch
    {
        LinkStatus.Broken => $"This {info.KindName} is broken: its target {Quote(info.TargetPath ?? info.TargetText)}does not exist.",
        LinkStatus.Denied => $"This {info.KindName} could not be read (access denied).",
        LinkStatus.Unavailable => $"The target of this {info.KindName} {Quote(info.TargetPath ?? info.TargetText)}is not reachable right now (a drive, share or distribution that is not available).",
        LinkStatus.Unsupported => $"FileScape cannot follow this {info.KindName}.",
        _ => $"A {info.KindName}.",
    };

    private static string Quote(string? s) => s == null ? "" : $"“{s}” ";

    /// <summary>
    /// Worker: works out where a link points and what is actually there (LINK-06), by probing rather than by
    /// Directory.Exists/File.Exists (which answer "false" for missing, denied and unreachable alike):
    /// read the link → read the target's attributes → for a folder, try to read its first entry.
    /// </summary>
    private static ResolveResult Resolve(string linkPath, bool isDir, LinkInfo info)
    {
        string? text;
        try
        {
            text = info.TargetText ?? (isDir ? new DirectoryInfo(linkPath).LinkTarget : new FileInfo(linkPath).LinkTarget);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var (status, why) = ClassifyFailure(ex, linkPath);
            return new ResolveResult(null, status == LinkStatus.Broken ? LinkStatus.Unavailable : status, $"The {info.KindName} itself could not be read: {why}");
        }

        if (string.IsNullOrEmpty(text))
        {
            return new ResolveResult(null, LinkStatus.Unsupported, $"Windows did not report where this {info.KindName} points.");
        }

        string target;
        try
        {
            var parent = Path.GetDirectoryName(linkPath) ?? linkPath;
            target = Path.IsPathFullyQualified(text) ? text : Path.GetFullPath(Path.Combine(parent, text));
            // Strip NT and Win32 device prefixes so the target compares equal to paths the user would type:
            // \??\X (a junction's raw target) always, \\?\C:\… only when it is a drive path (index 5 is the colon).
            if (target.StartsWith(@"\??\", StringComparison.Ordinal)) target = target[4..];
            if (target.StartsWith(@"\\?\", StringComparison.Ordinal) && target.Length > 6 && target[5] == ':') target = target[4..];
            target = Path.TrimEndingDirectorySeparator(target);
            if (target.Length == 2 && target[1] == ':') target += "\\";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new ResolveResult(null, LinkStatus.Broken, $"The target “{text}” is not a valid path.");
        }

        var probe = ProbeTarget(target);
        return probe.Status == LinkStatus.Resolved
            ? new ResolveResult(target, LinkStatus.Resolved, null)
            : new ResolveResult(null, probe.Status, probe.Status switch
            {
                LinkStatus.Broken => $"This {info.KindName} is broken: its target “{target}” does not exist.",
                LinkStatus.Denied => $"The target “{target}” exists but cannot be opened with your permissions ({probe.Why}).",
                _ => $"The target “{target}” is not reachable right now ({probe.Why}).",
            });
    }

    /// <summary>
    /// What is actually at a path: Resolved (exists, and a folder's first entry can be read), Broken (missing on a
    /// reachable volume), Denied (access refused), or Unavailable (device not ready, network path or volume missing).
    /// </summary>
    public static (LinkStatus Status, string? Why) ProbeTarget(string target)
    {
        FileAttributes attrs;
        try
        {
            attrs = File.GetAttributes(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ClassifyFailure(ex, target);
        }

        if ((attrs & FileAttributes.Directory) == 0) return (LinkStatus.Resolved, null);
        try
        {
            // One entry is enough to know the folder can be listed; nothing else is read.
            using var e = new FileSystemEnumerable<bool>(target, static (ref FileSystemEntry _) => true, ListOptions).GetEnumerator();
            e.MoveNext();
            return (LinkStatus.Resolved, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var (status, why) = ClassifyFailure(ex, target);
            return (status == LinkStatus.Broken ? LinkStatus.Unavailable : status, why);
        }
    }

    // Win32 error codes (winerror.h) carried in HResult as 0x8007xxxx.
    private const int ErrorFileNotFound = 2, ErrorPathNotFound = 3, ErrorAccessDenied = 5, ErrorInvalidDrive = 15, ErrorNotReady = 21,
        ErrorBadNetPath = 53, ErrorNetNameDeleted = 64, ErrorBadNetName = 67, ErrorUnexpectedNetError = 59, ErrorBadDevice = 1200, ErrorNoSuchDevice = 433;

    /// <summary>Maps an OS failure on <paramref name="path"/> to a link status, from the actual error, not a guess.</summary>
    public static (LinkStatus Status, string Why) ClassifyFailure(Exception ex, string path)
    {
        if (ex is UnauthorizedAccessException) return (LinkStatus.Denied, "access denied");
        var code = ex.HResult & 0xFFFF;
        var facilityWin32 = (ex.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000);
        if (facilityWin32)
        {
            switch (code)
            {
                case ErrorAccessDenied: return (LinkStatus.Denied, "access denied");
                case ErrorNotReady: return (LinkStatus.Unavailable, "the device is not ready");
                case ErrorInvalidDrive or ErrorBadDevice or ErrorNoSuchDevice: return (LinkStatus.Unavailable, "no such drive or device");
                case ErrorBadNetPath or ErrorBadNetName or ErrorNetNameDeleted or ErrorUnexpectedNetError: return (LinkStatus.Unavailable, "the network location is not reachable");
                case ErrorFileNotFound or ErrorPathNotFound:
                    return RootReachable(path) ? (LinkStatus.Broken, "it does not exist") : (LinkStatus.Unavailable, "its drive, share or distribution is not available");
            }
        }

        if (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return RootReachable(path) ? (LinkStatus.Broken, "it does not exist") : (LinkStatus.Unavailable, "its drive, share or distribution is not available");
        }

        return (LinkStatus.Unavailable, ex.Message);
    }
}
