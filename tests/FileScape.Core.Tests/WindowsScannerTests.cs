using FileScape.Core.Model;
using FileScape.Core.Scanning;
using FileScape.Core.Shell;
using FileScape.Fixtures;

namespace FileScape.Core.Tests;

/// <summary>
/// The real scanner against the on-disk fixture, on Windows (FS-01..FS-10). Every test creates its own fixture
/// under %TEMP%\FileScape-fixtures and deletes it afterwards.
/// </summary>
public sealed class WindowsScannerTests : IDisposable
{
    private readonly List<string> _fixtures = [];

    private FixtureManifest NewFixture(bool vanishing = true)
    {
        var m = FixtureBuilder.Create(includeVanishing: vanishing);
        _fixtures.Add(m.Root);
        return m;
    }

    public void Dispose()
    {
        foreach (var f in _fixtures)
        {
            FixtureBuilder.Delete(f);
        }
    }

    [WindowsFact]
    public void Scan_counts_match_the_fixture_manifest()
    {
        var m = NewFixture(vanishing: false);
        var (tree, progress) = ScanRunner.Run(new DirectoryScanner(m.Root));

        progress.State.Should().Be(ScanState.Completed);
        m.Skipped.Should().BeEmpty("every Windows edge case should be creatable on this machine; skipped: " + string.Join("; ", m.Skipped));
        tree.Nodes.Count(n => n.IsDirectory).Should().Be(m.ExpectedDirectories);
        tree.Nodes.Count(n => n.Kind == NodeKind.File).Should().Be(m.ExpectedFiles);
        tree.Nodes.Count(n => n.Kind == NodeKind.Link).Should().Be(m.ExpectedLinks);
        tree.Root.SubtreeBytes.Should().Be(m.ExpectedLogicalBytes);
        tree.Source.Origin.Should().Be(DataOrigin.LiveFilesystem);
    }

    [WindowsFact]
    public void Junction_loop_and_symlinks_are_shown_but_not_traversed()
    {
        var m = NewFixture(vanishing: false);
        var (tree, _) = ScanRunner.Run(new DirectoryScanner(m.Root));

        var junction = tree.Find(Path.GetFileName(m.JunctionLoop!));
        junction.Kind.Should().Be(NodeKind.Link);
        junction.Flags.Should().HaveFlag(NodeFlags.ReparsePoint).And.HaveFlag(NodeFlags.DirectoryLink);
        junction.Children.Should().BeEmpty();

        var dirLink = tree.Find(Path.GetFileName(m.DirectorySymlink!));
        dirLink.Kind.Should().Be(NodeKind.Link);
        dirLink.Children.Should().BeEmpty();
        tree.Find(Path.GetFileName(m.FileSymlink!)).Kind.Should().Be(NodeKind.Link);

        // A followed loop would repeat the fixture's contents; each name must occur exactly once.
        tree.Nodes.Count(n => n.Name == "wide-600-files").Should().Be(1);
    }

    [WindowsFact]
    public void Hidden_and_system_entries_are_included_and_flagged()
    {
        var m = NewFixture(vanishing: false);
        var (tree, _) = ScanRunner.Run(new DirectoryScanner(m.Root));

        tree.Find("hidden-file.txt").Flags.Should().HaveFlag(NodeFlags.Hidden);
        var sys = tree.Find("system-file.dat");
        sys.Flags.Should().HaveFlag(NodeFlags.System).And.HaveFlag(NodeFlags.Hidden);
        tree.Find("read-only.txt").Flags.Should().HaveFlag(NodeFlags.ReadOnly);
        tree.Find(FixtureBuilder.MarkerName).Should().NotBeNull();
    }

    [WindowsFact]
    public void Access_denied_directory_is_flagged_and_siblings_are_still_scanned()
    {
        var m = NewFixture(vanishing: false);
        m.DeniedDirectory.Should().NotBeNull();
        var (tree, progress) = ScanRunner.Run(new DirectoryScanner(m.Root));

        var denied = tree.Find("access-denied");
        denied.ScanState.Should().Be(DirScanState.Failed);
        denied.Flags.Should().HaveFlag(NodeFlags.AccessDenied);
        denied.Error.Should().NotBeNullOrWhiteSpace();
        tree.FindOrNull("secret.txt").Should().BeNull();
        tree.Find("hidden-file.txt").Should().NotBeNull("siblings of the denied directory must still be listed");
        progress.Problems.Should().BeGreaterThanOrEqualTo(1);
        progress.State.Should().Be(ScanState.Completed);
    }

    [WindowsFact]
    public void Directory_deleted_mid_scan_is_flagged_vanished_without_failing_the_scan()
    {
        var m = NewFixture(vanishing: true);
        var scanner = new DirectoryScanner(m.Root)
        {
            // Deletes the directory after its parent has listed it, before it is enumerated.
            BeforeEnumerateDirectory = path =>
            {
                if (path.EndsWith("vanishing", StringComparison.Ordinal) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            },
        };
        var (tree, progress) = ScanRunner.Run(scanner);

        var vanished = tree.Find("vanishing");
        vanished.Flags.Should().HaveFlag(NodeFlags.Vanished);
        vanished.ScanState.Should().Be(DirScanState.Failed);
        progress.State.Should().Be(ScanState.Completed);
    }

    [WindowsFact]
    public void Unicode_names_and_long_paths_round_trip_exactly()
    {
        var m = NewFixture(vanishing: false);
        var (tree, _) = ScanRunner.Run(new DirectoryScanner(m.Root));

        foreach (var name in m.UnicodeNames)
        {
            tree.Find(name).Kind.Should().Be(NodeKind.File);
        }

        var longFile = tree.Find(Path.GetFileName(m.LongPathFile!));
        tree.FullPath(longFile.Id).Should().Be(m.LongPathFile);
        tree.FullPath(longFile.Id).Length.Should().BeGreaterThan(300);
        File.Exists(tree.FullPath(longFile.Id)).Should().BeTrue("the reconstructed path must be usable with Windows APIs");
    }

    [WindowsFact]
    public void Sparse_file_reports_logical_size_and_zero_byte_files_are_zero()
    {
        var m = NewFixture(vanishing: false);
        var (tree, _) = ScanRunner.Run(new DirectoryScanner(m.Root));

        var sparse = tree.Find("sparse-10-gib.img");
        sparse.Size.Should().Be(10L << 30);
        sparse.Flags.Should().HaveFlag(NodeFlags.Sparse);
        tree.Find("zero-byte").Children.Select(c => tree[c].Size).Should().AllBeEquivalentTo(0L);
    }

    /// <summary>FS-07: hard links are counted once per name, so totals can double count. This is labelled, not hidden.</summary>
    [WindowsFact]
    public void Hard_linked_file_is_counted_under_each_name()
    {
        var m = NewFixture(vanishing: false);
        m.HardLink.Should().NotBeNull();
        var (tree, _) = ScanRunner.Run(new DirectoryScanner(m.Root));
        tree.Find("hard-link-to-one-mib.bin").Size.Should().Be(1 << 20);
        tree.Find("one-mib.bin").Size.Should().Be(1 << 20);
        var outliers = tree.Find("size-outliers");
        outliers.DirectFileBytes.Should().Be(1 + (1 << 20) + (1 << 20) + (10L << 30), "both names of the hard-linked file are counted");
    }

    [WindowsFact]
    public void Missing_root_produces_a_failed_root_not_an_exception()
    {
        var missing = Path.Combine(Path.GetTempPath(), "FileScape-fixtures", "does-not-exist-" + Guid.NewGuid().ToString("N"));
        var (tree, progress) = ScanRunner.Run(new DirectoryScanner(missing));
        progress.State.Should().Be(ScanState.Completed);
        tree.Root.ScanState.Should().Be(DirScanState.Failed);
        tree.Root.Flags.Should().HaveFlag(NodeFlags.Vanished);
    }

    [WindowsFact]
    public void Cancellation_stops_a_real_scan_promptly()
    {
        var m = NewFixture(vanishing: false);
        using var session = new ScanSession(new DirectoryScanner(m.Root) { BeforeEnumerateDirectory = _ => Thread.Sleep(20) });
        session.Start();
        Thread.Sleep(150);
        session.Cancel();
        session.WaitForExit(TimeSpan.FromSeconds(2)).Should().BeTrue();
        var p = session.Snapshot();
        p.State.Should().Be(ScanState.Cancelled);
        p.CancelAcknowledgedAfter!.Value.Should().BeLessThan(TimeSpan.FromMilliseconds(250));
    }

    [WindowsFact]
    public void Opening_a_missing_file_reports_an_error_without_ui_or_exception()
    {
        var r = new WindowsShellActions().Open(Path.Combine(Path.GetTempPath(), "FileScape-fixtures", "missing-" + Guid.NewGuid().ToString("N") + ".txt"));
        r.Succeeded.Should().BeFalse();
        r.WasMock.Should().BeFalse();
        r.Message.Should().Contain("error");
    }
}
