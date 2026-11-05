using System.Diagnostics;
using FileScape.Core.Model;
using FileScape.Core.Scanning;

namespace FileScape.Core.Tests;

/// <summary>
/// The connected namespace on real NTFS junctions (LINK-01..06) and, when a prepared WSL fixture is named in
/// FILESCAPE_WSL_FIXTURE, through \\wsl.localhost (LOC-03). Fixtures live in an owned temp directory.
/// </summary>
public sealed class WindowsWorldTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "filescape-world-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        var vault = Path.Combine(_root, "vault");
        if (Directory.Exists(vault)) Icacls(vault, "/remove:d", "*" + MySid());
        foreach (var l in Directory.EnumerateDirectories(Path.Combine(_root, "arrival")).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0))
        {
            Directory.Delete(l); // removes the junction, never its target
        }

        Directory.Delete(_root, recursive: true);
    }

    private static void Junction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "/d", "/c", "mklink", "/J", link, target }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit(10_000);
        p.ExitCode.Should().Be(0, p.StandardError.ReadToEnd());
    }

    private string Arrival => Path.Combine(_root, "arrival");

    private static string MySid() =>
        OperatingSystem.IsWindows() ? System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value : throw new PlatformNotSupportedException();

    private static void Icacls(string path, string op, string arg)
    {
        var psi = new ProcessStartInfo("icacls.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add(op);
        psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        p.WaitForExit(10_000);
        p.ExitCode.Should().Be(0, $"icacls {op} on {path}");
    }

    /// <summary>Maps (or with null, removes) a temporary drive letter for this user session.</summary>
    private static void Subst(string letter, string? target)
    {
        var psi = new ProcessStartInfo("subst.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(letter);
        psi.ArgumentList.Add(target ?? "/d");
        using var p = Process.Start(psi)!;
        p.WaitForExit(10_000);
        if (target != null) p.ExitCode.Should().Be(0, $"subst {letter} {target}");
    }

    private static string UnusedDriveRoot()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        for (var c = 'Q'; c <= 'Z'; c++) if (!used.Contains(c)) return $"{c}:\\";
        throw new InvalidOperationException("no free drive letter for the test");
    }

    /// <summary>
    /// LINK-06 by actual probing (2026-11-05 correction): a denied target, an unreachable drive and a missing target
    /// each get their own status; Retry recovers once the cause is fixed.
    /// </summary>
    [WindowsFact]
    public void Denied_unavailable_and_broken_targets_are_told_apart_and_retry_recovers()
    {
        Directory.CreateDirectory(Arrival);
        var vault = Path.Combine(_root, "vault");
        Directory.CreateDirectory(vault);
        File.WriteAllText(Path.Combine(vault, "inside.txt"), "x");
        var me = MySid();
        Icacls(vault, "/deny", $"*{me}:(OI)(CI)(RX)");
        Junction(Path.Combine(Arrival, "j-denied"), vault);
        var nowhere = UnusedDriveRoot() + "nowhere";
        // A junction whose target becomes unreachable: made through a temporary subst drive, which is then removed.
        // (mklink /J refuses a target on a drive that does not exist.)
        var letter = UnusedDriveRoot()[..2];
        var substRoot = Path.Combine(_root, "subst-root");
        Directory.CreateDirectory(Path.Combine(substRoot, "inner"));
        try
        {
            Subst(letter, substRoot);
            Junction(Path.Combine(Arrival, "j-nodrive"), letter + "\\inner");
        }
        finally
        {
            Subst(letter, null);
        }

        WorldExplorer.ProbeTarget(letter + "\\inner").Status.Should().Be(LinkStatus.Unavailable, "its drive is gone");
        var gone = Path.Combine(_root, "gone");
        Directory.CreateDirectory(gone);
        Junction(Path.Combine(Arrival, "j-broken"), gone);
        Directory.Delete(gone);
        var notReady = DriveInfo.GetDrives().FirstOrDefault(d => !d.IsReady && d.DriveType == DriveType.Removable);

        // Direct probes of the targets (not through a link).
        WorldExplorer.ProbeTarget(vault).Status.Should().Be(LinkStatus.Denied, "the folder exists but cannot be listed");
        WorldExplorer.ProbeTarget(nowhere).Status.Should().Be(LinkStatus.Unavailable, "its drive does not exist");
        WorldExplorer.ProbeTarget(gone).Status.Should().Be(LinkStatus.Broken, "missing on a reachable volume");
        if (notReady != null) WorldExplorer.ProbeTarget(Path.Combine(notReady.Name, "x")).Should().Be((LinkStatus.Unavailable, "the device is not ready"));

        var (ex, s, t, replies) = Start(Arrival);
        using (ex)
        using (s)
        {
            Pump(s, t, replies, () => replies.ContainsKey(WorldExplorer.ArrivalTicket) && s.Snapshot().Idle && !s.HasPendingBatches);
            var arrival = replies[WorldExplorer.ArrivalTicket].NodeId;
            int Child(string n) => t[arrival].Children.Single(c => t[c].Name == n);
            LinkStatus Follow(string name, out ExplorerReply reply)
            {
                var ticket = ex.FollowLink(Child(name));
                Pump(s, t, replies, () => replies.ContainsKey(ticket));
                Pump(s, t, replies, () => !s.HasPendingBatches);
                reply = replies[ticket];
                return t[Child(name)].Link!.Status;
            }

            Follow("j-denied", out var r1).Should().Be(LinkStatus.Denied);
            r1.NodeId.Should().Be(-1);
            r1.Error.Should().Contain("permissions");
            Follow("j-nodrive", out var r2).Should().Be(LinkStatus.Unavailable);
            r2.Error.Should().Contain("not reachable");
            Follow("j-broken", out var r3).Should().Be(LinkStatus.Broken);

            // Recovery: fix the cause, Retry, follow.
            Icacls(vault, "/remove:d", "*" + me);
            Directory.CreateDirectory(gone);
            ex.Retry(Child("j-denied"));
            ex.Retry(Child("j-broken"));
            Pump(s, t, replies, () => t[Child("j-denied")].Link!.Status == LinkStatus.Resolved && t[Child("j-broken")].Link!.Status == LinkStatus.Resolved);
            Follow("j-denied", out var ok1).Should().Be(LinkStatus.Resolved);
            t.FullPath(ok1.NodeId).Should().BeEquivalentTo(vault);
            Follow("j-broken", out var ok2).Should().Be(LinkStatus.Resolved);
            t.FullPath(ok2.NodeId).Should().BeEquivalentTo(gone);
        }
    }

    private void Build()
    {
        Directory.CreateDirectory(Path.Combine(Arrival, "a"));
        File.WriteAllBytes(Path.Combine(Arrival, "a", "x.txt"), new byte[1000]);
        Directory.CreateDirectory(Path.Combine(_root, "outside", "deep"));
        File.WriteAllBytes(Path.Combine(_root, "outside", "deep", "f.bin"), new byte[7000]);
        Directory.CreateDirectory(Path.Combine(_root, "gone"));
        Junction(Path.Combine(Arrival, "j-outside"), Path.Combine(_root, "outside"));
        Junction(Path.Combine(Arrival, "j-loop"), Arrival);
        Junction(Path.Combine(Arrival, "j-a"), Path.Combine(Arrival, "a"));
        Junction(Path.Combine(Arrival, "j-a-again"), Path.Combine(Arrival, "a"));
        Junction(Path.Combine(Arrival, "j-broken"), Path.Combine(_root, "gone"));
        Directory.Delete(Path.Combine(_root, "gone"));
    }

    private static (WorldExplorer, ScanSession, FsTree, Dictionary<int, ExplorerReply>) Start(string arrival, WorldExplorer.Options? o = null)
    {
        var ex = new WorldExplorer(arrival, o ?? new WorldExplorer.Options { Locations = Locations.Discover().Where(l => l.Kind == LocationKind.Drive).ToList() });
        var s = new ScanSession(ex);
        var t = new FsTree(s.Source);
        s.Start();
        return (ex, s, t, []);
    }

    private static void Pump(ScanSession s, FsTree t, Dictionary<int, ExplorerReply> replies, Func<bool> until, double seconds = 15)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            while (s.TryDequeue(out var b))
            {
                t.Apply(b);
                foreach (var r in b.Replies) replies[r.Ticket] = r;
            }

            if (until()) return;
            Thread.Sleep(5);
        }

        throw new TimeoutException();
    }

    [WindowsFact]
    public void Junctions_are_identified_and_followed_on_demand()
    {
        Build();
        var (ex, s, t, replies) = Start(Arrival);
        using (ex)
        using (s)
        {
            Pump(s, t, replies, () => replies.ContainsKey(WorldExplorer.ArrivalTicket) && s.Snapshot().Idle && !s.HasPendingBatches);
            var arrival = replies[WorldExplorer.ArrivalTicket].NodeId;
            int Child(string n) => t[arrival].Children.Single(c => t[c].Name == n);
            var outside = Child("j-outside");
            t[outside].Kind.Should().Be(NodeKind.Link);
            t[outside].Link!.Kind.Should().Be(LinkKind.Junction, "the reparse tag says junction (LINK-01)");
            t[outside].Link!.Status.Should().Be(LinkStatus.Unresolved, "background traversal never follows links");

            var follow = ex.FollowLink(outside);
            Pump(s, t, replies, () => replies.ContainsKey(follow));
            replies[follow].Error.Should().BeNull();
            t.FullPath(replies[follow].NodeId).Should().BeEquivalentTo(Path.Combine(_root, "outside"));

            var count = t.Count;
            var loop = ex.FollowLink(Child("j-loop"));
            var a1 = ex.FollowLink(Child("j-a"));
            var a2 = ex.FollowLink(Child("j-a-again"));
            Pump(s, t, replies, () => replies.ContainsKey(loop) && replies.ContainsKey(a1) && replies.ContainsKey(a2));
            replies[loop].NodeId.Should().Be(arrival, "a loop junction leads back to the existing ancestor (LINK-05)");
            replies[a1].NodeId.Should().Be(Child("a"));
            replies[a2].NodeId.Should().Be(Child("a"), "two routes, one destination");
            Pump(s, t, replies, () => s.Snapshot().Idle && !s.HasPendingBatches);
            t.Count.Should().Be(count, "no duplicate sub-worlds");

            var broken = ex.FollowLink(Child("j-broken"));
            Pump(s, t, replies, () => replies.ContainsKey(broken));
            replies[broken].NodeId.Should().Be(-1);
            replies[broken].Error.Should().Contain("broken");
        }
    }

    [WindowsFact]
    public void Up_from_the_arrival_root_reaches_its_parent_and_lists_it_on_demand()
    {
        Build();
        var (ex, s, t, replies) = Start(Arrival);
        using (ex)
        using (s)
        {
            Pump(s, t, replies, () => replies.ContainsKey(WorldExplorer.ArrivalTicket));
            var arrival = replies[WorldExplorer.ArrivalTicket].NodeId;
            var parent = t[arrival].ParentId;
            t.FullPath(parent).Should().BeEquivalentTo(_root);
            t[parent].ScanState.Should().Be(DirScanState.Unlisted);
            ex.Visit(parent);
            Pump(s, t, replies, () => t[parent].ScanState == DirScanState.Complete);
            t[parent].Children.Select(c => t[c].Name).Should().BeEquivalentTo(["arrival", "outside"]);
            var drive = t[0].Children.First(c => t.FullPath(c).Equals(Path.GetPathRoot(_root), StringComparison.OrdinalIgnoreCase));
            t[drive].Location.Should().Be(LocationKind.Drive);
        }
    }

    /// <summary>
    /// LOC-03 against a live distribution. Prepare with tools/make-wsl-fixture.sh and set FILESCAPE_WSL_FIXTURE to its
    /// \\wsl.localhost path; skipped (with this reason) otherwise.
    /// </summary>
    [WslFixtureFact]
    public void Wsl_through_the_unc_route_keeps_case_pairs_and_identifies_linux_links()
    {
        var fixture = Environment.GetEnvironmentVariable("FILESCAPE_WSL_FIXTURE")!;

        var (ex, s, t, replies) = Start(fixture, new WorldExplorer.Options());
        using (ex)
        using (s)
        {
            Pump(s, t, replies, () => replies.ContainsKey(WorldExplorer.ArrivalTicket) && s.Snapshot().Idle && !s.HasPendingBatches, 60);
            var arrival = replies[WorldExplorer.ArrivalTicket].NodeId;
            replies[WorldExplorer.ArrivalTicket].Error.Should().BeNull();
            var names = t[arrival].Children.Select(c => t[c].Name).ToList();
            names.Should().Contain("Notes").And.Contain("notes");
            var link = t[arrival].Children.Select(c => t[c]).Single(n => n.Name == "to-real");
            link.Link!.Kind.Should().Be(LinkKind.LinuxSymlink);
            link.Link.Status.Should().Be(LinkStatus.Unsupported);
            t[t.Root.Children.Single(c => t[c].Location == LocationKind.Wsl && fixture.StartsWith(t.FullPath(c), StringComparison.OrdinalIgnoreCase))].Location.Should().Be(LocationKind.Wsl);
        }
    }
}
