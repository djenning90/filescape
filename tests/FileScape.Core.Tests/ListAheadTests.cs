using System.Diagnostics;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Scanning;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// LIST-01..03: nearby places make progress without a crawl. A fixture that starts genuinely unlisted (no background
/// traversal), bounded further context, refusals for Linux system filesystems, and distinct queued/listing states.
/// </summary>
public sealed class ListAheadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "filescape-ahead-" + Guid.NewGuid().ToString("N")[..8]);

    public ListAheadTests()
    {
        // arrival / p1..p3 / c1..c3 / g1 / deep : four levels below the arrival point.
        foreach (var p in new[] { "p1", "p2", "p3" })
        foreach (var c in new[] { "c1", "c2", "c3" })
        {
            Directory.CreateDirectory(Path.Combine(_root, "arrival", p, c, "g1", "deep"));
            File.WriteAllText(Path.Combine(_root, "arrival", p, c, "note.txt"), "x");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("/proc")]
    [InlineData("/proc/1/task")]
    [InlineData("/sys/class")]
    [InlineData("/dev")]
    [InlineData("/run/user")]
    [InlineData("/mnt/c")]
    [InlineData(@"\\wsl.localhost\Ubuntu-24.04\proc")]
    [InlineData(@"\\wsl$\Debian\sys\kernel")]
    [InlineData(@"\\wsl.localhost\Ubuntu-24.04\mnt\c\Users")]
    public void Linux_system_filesystems_and_mount_aliases_are_never_listed_ahead(string path)
    {
        foreach (var kind in Enum.GetValues<ListAheadKind>())
        {
            ListAhead.Refusal(path, kind).Should().StartWith("Not listed ahead:");
        }
    }

    [Theory]
    [InlineData("/home")]
    [InlineData("/home/someone/src")]
    [InlineData("/procedures")]
    [InlineData(@"\\wsl.localhost\Ubuntu-24.04\home")]
    [InlineData(@"\\wsl.localhost\Ubuntu-24.04")]
    [InlineData(@"C:\Users\Public")]
    public void Ordinary_folders_may_be_listed_ahead(string path) =>
        ListAhead.Refusal(path, ListAheadKind.Context).Should().BeNull();

    [Fact]
    public void A_network_share_is_a_destination_but_never_further_context()
    {
        ListAhead.Refusal(@"\\server\share\team", ListAheadKind.Destination).Should().BeNull();
        ListAhead.Refusal(@"\\server\share\team", ListAheadKind.Context).Should().Contain("network share");
        ListAhead.Refusal(@"\\server\share\team", ListAheadKind.Background).Should().Contain("network share");
    }

    [PosixFact]
    public void Nearby_places_start_unlisted_and_get_listed_with_bounded_context()
    {
        using var h = new Harness(Path.Combine(_root, "arrival"), new WorldExplorer.Options
        {
            Locations = [new LocationSeed("/", "/", LocationKind.Volume, true)],
            Background = false,
            ContextLimit = 4,
        });
        var arrival = h.Await(WorldExplorer.ArrivalTicket);
        h.Explorer.Visit(arrival);
        List<int> Dirs(IEnumerable<int> ids) => ids.SelectMany(i => h.Tree[i].Children).Where(c => h.Tree[c].IsDirectory).ToList();
        h.Pump(() => h.Tree[arrival].ScanState == DirScanState.Complete && Dirs([arrival]) is { Count: 3 } p && p.All(x => h.Tree[x].ScanState == DirScanState.Complete));
        var ps = Dirs([arrival]);
        h.Pump(() => Dirs(ps).Count(c => h.Tree[c].ScanState == DirScanState.Complete) >= 4);
        Thread.Sleep(300);
        h.Pump(() => h.Idle);
        var cs = Dirs(ps);
        cs.Should().HaveCount(9);
        cs.Concat(ps).Should().OnlyContain(c => h.FirstState[c] == DirScanState.Unlisted, "every place below the arrival started unlisted (a cold tree, not a warmed one)");
        cs.Count(c => h.Tree[c].ScanState == DirScanState.Complete).Should().Be(4, "further context is bounded (ContextLimit 4)");
        cs.Count(c => h.Tree[c].ScanState == DirScanState.Unlisted).Should().Be(5);
        var gs = cs.SelectMany(c => h.Tree[c].Children).Where(g => h.Tree[g].IsDirectory).ToList();
        gs.Should().OnlyContain(g => h.Tree[g].ScanState == DirScanState.Unlisted, "context goes one level beyond the destinations, never further");

        // Moving on: the new place's destinations come first; its context budget is its own.
        var p1 = ps.First(p => Dirs([p]).Any(c => h.Tree[c].ScanState == DirScanState.Unlisted));
        h.Explorer.Visit(p1);
        h.Pump(() => Dirs([p1]).All(c => h.Tree[c].ScanState == DirScanState.Complete));
    }

    [PosixFact]
    public void Visiting_a_place_lists_the_way_back_when_it_was_only_a_stub()
    {
        var deep = Path.Combine(_root, "arrival", "p2", "c2");
        using var h = new Harness(deep, new WorldExplorer.Options
        {
            Locations = [new LocationSeed("/", "/", LocationKind.Volume, true)],
            Background = false,
            ContextLimit = 0,
        });
        var here = h.Await(WorldExplorer.ArrivalTicket);
        var parent = h.Tree[here].ParentId;
        h.Tree[parent].Children.Should().HaveCount(1, "the parent is a stub that knows only the way here");
        h.Explorer.Visit(here);
        h.Pump(() => h.Tree[parent].ScanState == DirScanState.Complete);
        h.Tree[parent].Children.Should().HaveCount(3);
    }

    [PosixFact]
    public void Entering_proc_lists_it_once_and_never_its_process_folders()
    {
        if (!Directory.Exists("/proc/self")) return; // not Linux
        using var h = new Harness("/proc", new WorldExplorer.Options
        {
            Locations = [new LocationSeed("/", "/", LocationKind.Volume, true)],
            ContextLimit = 48,
        });
        var proc = h.Await(WorldExplorer.ArrivalTicket);
        h.Explorer.Visit(proc);
        h.Pump(() => h.Tree[proc].ScanState == DirScanState.Complete);
        Thread.Sleep(300);
        h.Pump(() => h.Idle, 20);
        var dirs = h.Tree[proc].Children.Where(c => h.Tree[c].IsDirectory).ToList();
        dirs.Should().NotBeEmpty();
        dirs.Should().OnlyContain(d => h.Tree[d].ScanState == DirScanState.Unlisted, "nothing in /proc is listed ahead, even with a context budget");
        h.Tree[dirs[0]].Error.Should().Contain("/proc is a Linux system filesystem");
        Listing.Of(h.Tree[dirs[0]]).Should().Be(ListingState.Unlisted);
    }

    [PosixFact]
    public void A_listing_in_progress_is_distinct_from_queued()
    {
        using var gate = new ManualResetEventSlim(false);
        var slow = Path.Combine(_root, "arrival", "p3");
        using var h = new Harness(Path.Combine(_root, "arrival"), new WorldExplorer.Options
        {
            Locations = [new LocationSeed("/", "/", LocationKind.Volume, true)],
            Background = false,
            MaxConcurrent = 1,
            BeforeEnumerate = p => { if (p == slow) gate.Wait(TimeSpan.FromSeconds(20)); },
        });
        try
        {
            var arrival = h.Await(WorldExplorer.ArrivalTicket);
            h.Pump(() => h.Tree[arrival].ScanState == DirScanState.Complete);
            var p3 = h.Tree[arrival].Children.Single(c => h.Tree[c].Name == "p3");
            h.Explorer.Visit(p3); // urgent: p3 is dispatched (and blocks), its parent's other places wait
            h.Pump(() => h.Tree[p3].ScanState == DirScanState.Listing, 5);
            Listing.Of(h.Tree[p3]).Should().Be(ListingState.Listing);
            Listing.Describe(ListingState.Listing).Should().Be("listing now…");
            Listing.Describe(ListingState.Pending).Should().Be("queued");
        }
        finally
        {
            gate.Set();
        }

        h.Pump(() => h.Idle);
    }

    /// <summary>
    /// A WorldExplorer behind a real ScanSession, pumped on the test thread as the app pumps it on its main thread.
    /// Records each node's state when it first appeared (<see cref="FirstState"/>), so a test can tell a folder that
    /// started unlisted from one that was listed before it was seen.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        public readonly WorldExplorer Explorer;
        public readonly ScanSession Session;
        public readonly FsTree Tree;
        private readonly Dictionary<int, ExplorerReply> _replies = [];
        public readonly Dictionary<int, DirScanState> FirstState = [];

        public Harness(string arrival, WorldExplorer.Options options)
        {
            Explorer = new WorldExplorer(arrival, options);
            Session = new ScanSession(Explorer);
            Tree = new FsTree(Session.Source);
            Session.Start();
        }

        public bool Idle => Session.Snapshot().Idle && !Session.HasPendingBatches;

        public void Pump(Func<bool> until, double seconds = 10)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                while (Session.TryDequeue(out var b))
                {
                    Tree.Apply(b);
                    foreach (var n in b.NewNodes) FirstState.TryAdd(n.Id, n.InitialState);
                    foreach (var r in b.Replies) _replies[r.Ticket] = r;
                }

                if (until()) return;
                Thread.Sleep(5);
            }

            throw new TimeoutException("condition not reached");
        }

        public int Await(int ticket)
        {
            Pump(() => _replies.ContainsKey(ticket));
            return _replies[ticket].NodeId;
        }

        public void Dispose()
        {
            Session.Dispose();
            Session.WaitForExit(TimeSpan.FromSeconds(5));
            Explorer.Dispose();
        }
    }
}
