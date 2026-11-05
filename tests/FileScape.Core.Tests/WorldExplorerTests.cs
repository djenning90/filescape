using System.Diagnostics;
using FileScape.Core.Model;
using FileScape.Core.Scanning;

namespace FileScape.Core.Tests;

/// <summary>
/// The connected namespace (AD-17, AD-18; LINK-01..08, LOC-01..04) on a real POSIX directory with real symbolic
/// links. Runs where symlinks can be created without privileges (Linux/WSL); Windows junction cases are in
/// WindowsWorldTests.
/// </summary>
public sealed class WorldExplorerTests : IDisposable
{
    private readonly string _root;

    public WorldExplorerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "filescape-world-" + Guid.NewGuid().ToString("N")[..8]);
        if (OperatingSystem.IsWindows()) return;
        var arrival = Path.Combine(_root, "arrival");
        Directory.CreateDirectory(Path.Combine(arrival, "a"));
        Directory.CreateDirectory(Path.Combine(arrival, "b", "inner"));
        File.WriteAllBytes(Path.Combine(arrival, "a", "x.txt"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(arrival, "b", "inner", "y.bin"), new byte[500]);
        File.WriteAllText(Path.Combine(arrival, "Readme"), "1");
        File.WriteAllText(Path.Combine(arrival, "README"), "22");
        Directory.CreateDirectory(Path.Combine(_root, "outside", "deep"));
        File.WriteAllBytes(Path.Combine(_root, "outside", "deep", "f.bin"), new byte[7000]);
        Directory.CreateSymbolicLink(Path.Combine(arrival, "link-to-outside"), "../outside");
        Directory.CreateSymbolicLink(Path.Combine(arrival, "loop"), ".");
        Directory.CreateSymbolicLink(Path.Combine(arrival, "link-to-a"), "a");
        Directory.CreateSymbolicLink(Path.Combine(arrival, "second-link-to-a"), "./a");
        Directory.CreateSymbolicLink(Path.Combine(arrival, "broken"), "../missing");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        // Remove links first so nothing can recurse through them.
        foreach (var l in Directory.EnumerateFileSystemEntries(Path.Combine(_root, "arrival")).Where(p => new FileInfo(p).LinkTarget != null))
        {
            File.Delete(l);
        }

        Directory.Delete(_root, recursive: true);
    }

    private string Arrival => Path.Combine(_root, "arrival");

    private sealed class Harness : IDisposable
    {
        public readonly WorldExplorer Explorer;
        public readonly ScanSession Session;
        public readonly FsTree Tree;
        public readonly Dictionary<int, ExplorerReply> Replies = [];

        public Harness(string arrival, WorldExplorer.Options? options = null, int maxNodes = ScanSession.DefaultMaxNodes)
        {
            Explorer = new WorldExplorer(arrival, options ?? new WorldExplorer.Options { Locations = [new LocationSeed("/", "/", LocationKind.Volume, true)] });
            Session = new ScanSession(Explorer, maxNodes);
            Tree = new FsTree(Session.Source);
            Session.Start();
        }

        public void Pump(Func<bool> until, double seconds = 10)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                while (Session.TryDequeue(out var b))
                {
                    Tree.Apply(b);
                    foreach (var r in b.Replies) Replies[r.Ticket] = r;
                }

                if (until()) return;
                Thread.Sleep(5);
            }

            throw new TimeoutException("condition not reached");
        }

        public ExplorerReply Await(int ticket)
        {
            Pump(() => Replies.ContainsKey(ticket));
            return Replies[ticket];
        }

        public bool Idle => Session.Snapshot().Idle && !Session.HasPendingBatches;

        public int Child(int parent, string name) => Tree[parent].Children.Single(c => Tree[c].Name == name);

        public void Dispose()
        {
            Session.Dispose();
            Session.WaitForExit(TimeSpan.FromSeconds(5));
            Explorer.Dispose();
        }
    }

    private static bool Posix => !OperatingSystem.IsWindows();

    [PosixFact]
    public void The_arrival_folder_is_reached_under_the_hub_with_unlisted_ancestors()
    {
        using var h = new Harness(Arrival);
        var reply = h.Await(WorldExplorer.ArrivalTicket);
        reply.Error.Should().BeNull();
        var arrival = reply.NodeId;
        h.Tree.FullPath(arrival).Should().Be(Arrival);
        h.Tree.Root.IsHub.Should().BeTrue();
        var parent = h.Tree[h.Tree[arrival].ParentId];
        parent.ScanState.Should().Be(DirScanState.Unlisted, "ancestors are known by name only until visited");
        parent.Children.Should().ContainSingle();
        h.Pump(() => h.Idle);
        h.Tree[arrival].ScanState.Should().Be(DirScanState.Complete);
    }

    [PosixFact]
    public void Background_traversal_lists_the_arrival_subtree_but_never_follows_links()
    {
        using var h = new Harness(Arrival);
        var arrival = h.Await(WorldExplorer.ArrivalTicket).NodeId;
        h.Pump(() => h.Idle && h.Tree[arrival].ScanState == DirScanState.Complete);
        var inner = h.Child(h.Child(arrival, "b"), "inner");
        h.Tree[inner].ScanState.Should().Be(DirScanState.Complete, "the arrival subtree is traversed in the background");
        var link = h.Tree[h.Child(arrival, "link-to-outside")];
        link.Kind.Should().Be(NodeKind.Link);
        link.Link!.Kind.Should().Be(LinkKind.SymbolicLink);
        link.Link.Status.Should().Be(LinkStatus.Unresolved, "background traversal never follows links");
        h.Tree.Nodes.Should().NotContain(n => n.Name == "deep", "nothing outside the arrival subtree was listed");
    }

    [PosixFact]
    public void Following_a_link_outside_the_root_reaches_its_physical_location()
    {
        using var h = new Harness(Arrival);
        var arrival = h.Await(WorldExplorer.ArrivalTicket).NodeId;
        h.Pump(() => h.Idle);
        var linkId = h.Child(arrival, "link-to-outside");
        var reply = h.Await(h.Explorer.FollowLink(linkId));
        reply.Error.Should().BeNull();
        reply.ViaLink.Should().Be(linkId);
        h.Tree.FullPath(reply.NodeId).Should().Be(Path.Combine(_root, "outside"), "the destination is shown at its real location");
        h.Tree[reply.NodeId].ParentId.Should().Be(h.Tree[arrival].ParentId, "Up goes to the physical parent, not back through the link");
        h.Pump(() => h.Tree[linkId].Link!.Status == LinkStatus.Resolved);
        h.Tree[linkId].Link!.TargetId.Should().Be(reply.NodeId);
    }

    [PosixFact]
    public void A_link_back_to_an_ancestor_and_two_links_to_one_target_reuse_existing_nodes()
    {
        using var h = new Harness(Arrival);
        var arrival = h.Await(WorldExplorer.ArrivalTicket).NodeId;
        h.Pump(() => h.Idle);
        var before = h.Tree.Count;
        h.Await(h.Explorer.FollowLink(h.Child(arrival, "loop"))).NodeId.Should().Be(arrival, "a loop leads to the existing ancestor");
        var a = h.Child(arrival, "a");
        h.Await(h.Explorer.FollowLink(h.Child(arrival, "link-to-a"))).NodeId.Should().Be(a);
        h.Await(h.Explorer.FollowLink(h.Child(arrival, "second-link-to-a"))).NodeId.Should().Be(a, "two routes, one destination");
        h.Pump(() => h.Idle);
        h.Tree.Count.Should().Be(before, "no duplicate sub-worlds were created");
    }

    [PosixFact]
    public void A_broken_link_says_so()
    {
        using var h = new Harness(Arrival);
        var arrival = h.Await(WorldExplorer.ArrivalTicket).NodeId;
        h.Pump(() => h.Idle);
        var broken = h.Child(arrival, "broken");
        var reply = h.Await(h.Explorer.FollowLink(broken));
        reply.NodeId.Should().Be(-1);
        reply.Error.Should().Contain("broken");
        h.Pump(() => h.Tree[broken].Link!.Status != LinkStatus.Unresolved);
        h.Tree[broken].Link!.Status.Should().Be(LinkStatus.Broken);
    }

    [PosixFact]
    public void A_path_through_a_link_arrives_at_the_physical_folder()
    {
        using var h = new Harness(Arrival);
        h.Await(WorldExplorer.ArrivalTicket);
        var reply = h.Await(h.Explorer.GoTo(Path.Combine(Arrival, "link-to-outside", "deep")));
        reply.Error.Should().BeNull();
        h.Tree.FullPath(reply.NodeId).Should().Be(Path.Combine(_root, "outside", "deep"));
    }

    [PosixFact]
    public void Totals_never_include_link_targets()
    {
        using var h = new Harness(Arrival);
        var arrival = h.Await(WorldExplorer.ArrivalTicket).NodeId;
        h.Pump(() => h.Idle);
        var bytes = h.Tree[arrival].SubtreeBytes;
        bytes.Should().Be(1000 + 500 + 1 + 2);
        h.Await(h.Explorer.FollowLink(h.Child(arrival, "link-to-outside")));
        var outside = h.Tree.Nodes.Single(n => n.Name == "outside").Id;
        h.Explorer.Visit(outside);
        h.Pump(() => h.Idle && h.Tree[outside].ScanState == DirScanState.Complete);
        h.Tree[arrival].SubtreeBytes.Should().Be(bytes, "the linked folder's contents are not counted again");
    }

    [PosixFact]
    public void Linux_names_that_differ_only_in_case_are_two_entries()
    {
        using var h = new Harness(Arrival);
        var arrival = h.Await(WorldExplorer.ArrivalTicket).NodeId;
        h.Pump(() => h.Idle);
        var names = h.Tree[arrival].Children.Select(c => h.Tree[c].Name).ToList();
        names.Should().Contain("Readme").And.Contain("README");
        h.Tree[h.Child(arrival, "README")].Size.Should().Be(2);
    }

    [PosixFact]
    public void Visiting_an_unlisted_ancestor_merges_the_child_it_already_knew()
    {
        using var h = new Harness(Arrival);
        var arrival = h.Await(WorldExplorer.ArrivalTicket).NodeId;
        var parent = h.Tree[arrival].ParentId;
        h.Explorer.Visit(parent);
        h.Pump(() => h.Idle && h.Tree[parent].ScanState == DirScanState.Complete);
        var names = h.Tree[parent].Children.Select(c => h.Tree[c].Name).ToList();
        names.Should().Equal("arrival", "outside");
        h.Tree[parent].Children.Count(c => h.Tree[c].Name == "arrival").Should().Be(1, "the known child is not duplicated");
    }

    [PosixFact]
    public void A_slow_listing_does_not_block_another_place()
    {
        using var gate = new ManualResetEventSlim(false);
        var slow = Path.Combine(Arrival, "b");
        using var h = new Harness(Arrival, new WorldExplorer.Options
        {
            Locations = [new LocationSeed("/", "/", LocationKind.Volume, true)],
            Background = false,
            BeforeEnumerate = p => { if (p == slow) gate.Wait(TimeSpan.FromSeconds(20)); },
        });
        var arrival = h.Await(WorldExplorer.ArrivalTicket).NodeId;
        h.Pump(() => h.Tree[arrival].ScanState == DirScanState.Complete);
        var b = h.Child(arrival, "b");
        var a = h.Child(arrival, "a");
        h.Explorer.Visit(b);
        h.Explorer.Visit(a);
        h.Pump(() => h.Tree[a].ScanState == DirScanState.Complete, 5);
        h.Tree[b].ScanState.Should().NotBe(DirScanState.Complete, "b is still blocked");
        h.Session.Snapshot().CurrentPath.Should().Be(slow, "the blocked OS call is reported");
        gate.Set();
        h.Pump(() => h.Tree[b].ScanState == DirScanState.Complete, 5);
    }

    [PosixFact]
    public void Cancelling_the_background_marks_queued_folders_and_keeps_on_demand_listing()
    {
        using var gate = new ManualResetEventSlim(false);
        using var h = new Harness(Arrival, new WorldExplorer.Options
        {
            Locations = [new LocationSeed("/", "/", LocationKind.Volume, true)],
            MaxConcurrent = 1,
            BeforeEnumerate = p => { if (p.EndsWith(Path.DirectorySeparatorChar + "a", StringComparison.Ordinal)) gate.Wait(TimeSpan.FromSeconds(20)); },
        });
        var arrival = h.Await(WorldExplorer.ArrivalTicket).NodeId;
        h.Pump(() => h.Tree[arrival].ScanState == DirScanState.Complete);
        h.Explorer.CancelBackground();
        gate.Set();
        var b = h.Child(arrival, "b");
        h.Pump(() => h.Idle);
        h.Tree[b].ScanState.Should().Be(DirScanState.Cancelled);
        h.Explorer.Visit(b);
        h.Pump(() => h.Tree[b].ScanState == DirScanState.Complete);
    }

    /// <summary>FS-08/FS-10 (2026-11-05 correction): a folder cut by the entry limit is partial, never "complete".</summary>
    [PosixFact]
    public void The_entry_limit_marks_folders_partial_bounds_buffering_and_keeps_requested_places_reachable()
    {
        var big = Path.Combine(Arrival, "big");
        Directory.CreateDirectory(big);
        for (var i = 0; i < 300; i++) File.WriteAllText(Path.Combine(big, $"f{i:D3}.txt"), "x");
        using var h = new Harness(Arrival, new WorldExplorer.Options { Locations = [new LocationSeed("/", "/", LocationKind.Volume, true)], LimitReserve = 10 }, maxNodes: 40);
        var arrival = h.Await(WorldExplorer.ArrivalTicket).NodeId;
        h.Pump(() => h.Idle);
        var bigId = h.Child(arrival, "big");
        var reply = h.Await(h.Explorer.GoTo(big));
        reply.NodeId.Should().Be(bigId);
        h.Pump(() => h.Idle && h.Tree[bigId].ScanState != DirScanState.Pending);
        var node = h.Tree[bigId];
        node.Children.Count.Should().BeInRange(1, 49);
        node.Flags.Should().HaveFlag(NodeFlags.Incomplete);
        Places.Listing.Of(node).Should().Be(Places.ListingState.Partial, "a cut listing reads 'partly listed', not complete");
        node.Error.Should().Contain("entry limit").And.Contain("Start fresh");
        h.Tree.Count.Should().BeLessThanOrEqualTo(40 + 10 + 8, "the tree stays within the budget plus the reserve (and a few path stubs)");
        h.Explorer.LargestTruncatedListing.Should().BeLessThanOrEqualTo(50, "a 300-entry folder was not buffered whole");
        var p = h.Session.Snapshot();
        p.LimitReached.Should().BeTrue();
        p.MaxNodes.Should().Be(40);

        // After the budget is spent, a requested destination is still reached (its path is ensured).
        var deep = h.Await(h.Explorer.GoTo(Path.Combine(_root, "outside", "deep")));
        deep.Error.Should().BeNull();
        h.Tree.FullPath(deep.NodeId).Should().Be(Path.Combine(_root, "outside", "deep"));
    }

    [Fact]
    public void Paths_are_normalised()
    {
        Locations.Normalize(@"\\wsl$\Ubuntu\home\me\", null, windows: true).Should().Be(@"\\wsl.localhost\Ubuntu\home\me");
        Locations.Normalize("/mnt/c/Users/Public", null, windows: true).Should().Be(@"C:\Users\Public");
        Locations.Normalize("/home/me/src", "Ubuntu-24.04", windows: true).Should().Be(@"\\wsl.localhost\Ubuntu-24.04\home\me\src");
        Locations.Normalize("\"C:\\Program Files\\\"", null, windows: true).Should().Be(@"C:\Program Files");
        Locations.Normalize("d:", null, windows: true).Should().Be(@"d:\");
        Locations.Normalize("/home/x", null, windows: true).Should().BeNull("no distribution to map a Linux path to");
        var (root, parts) = Locations.Split(@"\\wsl.localhost\Ubuntu-24.04\home\me");
        root.Should().Be(@"\\wsl.localhost\Ubuntu-24.04");
        parts.Should().Equal("home", "me");
        Locations.Split(@"C:\Users").Root.Should().Be(@"C:\");
        Locations.IsCaseSensitive(@"\\wsl.localhost\Ubuntu-24.04").Should().BeTrue();
        Locations.IsCaseSensitive(@"C:\").Should().BeFalse();
    }

    [Fact]
    public void Reparse_tags_are_classified_by_kind()
    {
        Reparse.Classify(true, Reparse.TagMountPoint, () => @"C:\Users\x\Documents").Link!.Kind.Should().Be(LinkKind.Junction);
        Reparse.Classify(true, Reparse.TagMountPoint, () => @"\\?\Volume{1234}\").Link!.Kind.Should().Be(LinkKind.MountedVolume);
        Reparse.Classify(true, Reparse.TagSymlink, () => "a").Link!.Kind.Should().Be(LinkKind.SymbolicLink);
        var lx = Reparse.Classify(false, Reparse.TagLxSymlink, () => null);
        lx.Link!.Kind.Should().Be(LinkKind.LinuxSymlink);
        lx.Link.Status.Should().Be(LinkStatus.Unsupported);
        var cloud = Reparse.Classify(true, 0x9000701A, () => null);
        cloud.Kind.Should().Be(NodeKind.Directory, "a OneDrive folder is a folder, not a link");
        cloud.ExtraFlags.Should().HaveFlag(NodeFlags.CloudPlaceholder);
        Reparse.Classify(false, Reparse.TagAppExecLink, () => null).ExtraFlags.Should().HaveFlag(NodeFlags.AppAlias);
        Reparse.Classify(false, Reparse.TagLxFifo, () => null).ExtraFlags.Should().HaveFlag(NodeFlags.LinuxSpecial);
        Reparse.Classify(true, 0x80000018, () => null).Link!.Kind.Should().Be(LinkKind.Other);
    }
}
