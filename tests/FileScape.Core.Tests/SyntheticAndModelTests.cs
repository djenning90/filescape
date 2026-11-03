using FileScape.Core.Model;
using FileScape.Core.Scanning;

namespace FileScape.Core.Tests;

/// <summary>Synthetic data and tree model behaviour (FS-11, UX-06, AD-04).</summary>
public class SyntheticAndModelTests
{
    [Fact]
    public void Same_seed_produces_identical_trees()
    {
        var (a, _) = ScanRunner.Run(new SyntheticTreeSource { Seed = 7, TargetNodes = 3000 });
        var (b, _) = ScanRunner.Run(new SyntheticTreeSource { Seed = 7, TargetNodes = 3000 });

        a.Count.Should().Be(b.Count);
        a.Nodes.Select(n => (n.ParentId, n.Name, n.Kind, n.Size, n.ModifiedUtc))
            .Should().Equal(b.Nodes.Select(n => (n.ParentId, n.Name, n.Kind, n.Size, n.ModifiedUtc)));
    }

    [Fact]
    public void Different_seeds_differ()
    {
        // Above the ~2,330 fixed edge-case nodes, so the seeded random fill contributes.
        var (a, _) = ScanRunner.Run(new SyntheticTreeSource { Seed = 1, TargetNodes = 4000 });
        var (b, _) = ScanRunner.Run(new SyntheticTreeSource { Seed = 2, TargetNodes = 4000 });
        a.Nodes.Select(n => n.Name).Should().NotEqual(b.Nodes.Select(n => n.Name));
    }

    [Fact]
    public void Node_count_matches_target_and_every_node_is_marked_synthetic()
    {
        var (tree, progress) = ScanRunner.Run(new SyntheticTreeSource { TargetNodes = 5000 });

        tree.Count.Should().Be(5000);
        progress.Nodes.Should().Be(5000);
        progress.State.Should().Be(ScanState.Completed);
        tree.Nodes.Should().OnlyContain(n => (n.Flags & NodeFlags.Synthetic) != 0);
        tree.Source.Origin.Should().Be(DataOrigin.Synthetic);
        tree.FullPath(tree.Find("hundred-mib.iso").Id).Should().StartWith("synthetic://");
    }

    [Fact]
    public void Contains_required_edge_cases()
    {
        var (tree, _) = ScanRunner.Run(new SyntheticTreeSource { TargetNodes = 4000 });

        tree.Find("wide-2000-files").Children.Should().HaveCount(2000);
        tree.Find("zero-byte-200").Children.Select(c => tree[c].Size).Should().AllBeEquivalentTo(0L).And.HaveCount(200);
        tree.Find("level-40").Depth.Should().Be(41);
        tree.Find("emoji-🚀-launch.log").Kind.Should().Be(NodeKind.File);
        tree.Find("combining-éclair.txt").Should().NotBeNull();
        tree.Find("two-tib-simulated.img").Size.Should().Be(2L << 40);
        tree.Find("junction-to-root").Kind.Should().Be(NodeKind.Link);
        tree.Find("access-denied").ScanState.Should().Be(DirScanState.Failed);
        tree.Find("access-denied").Flags.Should().HaveFlag(NodeFlags.AccessDenied);
        tree.Find("cloud-folder").ScanState.Should().Be(DirScanState.NotTraversed);
        tree.Find("hidden-settings.ini").Flags.Should().HaveFlag(NodeFlags.Hidden);
        var longFile = tree.Find("deeply-nested-file-whose-full-path-exceeds-the-classic-260-character-windows-limit.txt");
        tree.FullPath(longFile.Id).Length.Should().BeGreaterThan(260);
    }

    [Fact]
    public void Aggregates_sum_subtree_files_and_bytes()
    {
        var (tree, progress) = ScanRunner.Run(new SyntheticTreeSource { TargetNodes = 3000 });

        tree.Root.SubtreeFileCount.Should().Be(tree.Nodes.Count(n => n.Kind == NodeKind.File));
        tree.Root.SubtreeBytes.Should().Be(tree.Nodes.Where(n => n.Kind == NodeKind.File).Sum(n => n.Size));
        tree.Root.SubtreeBytes.Should().Be(progress.LogicalBytes);
        tree.Root.SubtreeDirCount.Should().Be(tree.Nodes.Count(n => n.IsDirectory) - 1);

        var outliers = tree.Find("size-outliers");
        outliers.DirectFileCount.Should().Be(6);
        outliers.DirectFileBytes.Should().Be(outliers.Children.Sum(c => tree[c].Size));
    }

    [Fact]
    public void Parents_precede_children_so_ids_are_breadth_first()
    {
        var (tree, _) = ScanRunner.Run(new SyntheticTreeSource { TargetNodes = 3000 });
        tree.Nodes.Skip(1).Should().OnlyContain(n => n.ParentId < n.Id);
        tree.Nodes.Skip(1).Should().OnlyContain(n => n.Depth == tree[n.ParentId].Depth + 1);
    }

    [Fact]
    public void Out_of_order_batch_is_rejected_rather_than_silently_corrupting_ids()
    {
        var tree = new FsTree(new DataSourceInfo(DataOrigin.Synthetic, "synthetic://t", "t"));
        var batch = new ScanBatch();
        batch.NewNodes.Add(new NodeRecord(5, -1, "root", NodeKind.Directory, 0, 0, 0, null, null, null, 0, DirScanState.Pending));
        var act = () => tree.Apply(batch);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cancelling_a_throttled_synthetic_scan_is_acknowledged_quickly_and_marks_pending_dirs()
    {
        using var session = new ScanSession(new SyntheticTreeSource { TargetNodes = 50_000, NodesPerSecond = 5_000 });
        var tree = new FsTree(session.Source);
        session.Start();
        Thread.Sleep(400);
        session.Cancel();
        session.State.Should().BeOneOf(ScanState.Cancelling, ScanState.Cancelled);
        session.WaitForExit(TimeSpan.FromSeconds(2)).Should().BeTrue();
        while (session.TryDequeue(out var b))
        {
            tree.Apply(b);
        }

        var progress = session.Snapshot();
        progress.State.Should().Be(ScanState.Cancelled);
        progress.CancelAcknowledgedAfter.Should().NotBeNull();
        progress.CancelAcknowledgedAfter!.Value.Should().BeLessThan(TimeSpan.FromMilliseconds(250));
        tree.Count.Should().BeLessThan(50_000);
        tree.Nodes.Should().Contain(n => n.ScanState == DirScanState.Cancelled);
        tree.Nodes.Should().NotContain(n => n.ScanState == DirScanState.Pending, "every unfinished directory must be marked, not left pending");
    }

    [Fact]
    public void Node_limit_stops_the_scan_visibly()
    {
        var (tree, progress) = ScanRunner.Run(new DirectoryScannerOverSynthetic(), maxNodes: 50);
        progress.State.Should().Be(ScanState.LimitReached);
        tree.Count.Should().BeLessThanOrEqualTo(50);
        tree.Nodes.Should().Contain(n => (n.Flags & NodeFlags.Incomplete) != 0);
    }

    /// <summary>Runs the real DirectoryScanner over a small temporary tree on any OS, to test the node-limit path.</summary>
    private sealed class DirectoryScannerOverSynthetic : IScanSource
    {
        private readonly string _root;
        private readonly DirectoryScanner _inner;

        public DirectoryScannerOverSynthetic()
        {
            _root = Path.Combine(Path.GetTempPath(), "FileScape-fixtures", "limit-" + Guid.NewGuid().ToString("N")[..8]);
            for (var d = 0; d < 10; d++)
            {
                var dir = Directory.CreateDirectory(Path.Combine(_root, $"d{d}"));
                for (var f = 0; f < 10; f++)
                {
                    File.WriteAllText(Path.Combine(dir.FullName, $"f{f}.txt"), "x");
                }
            }

            _inner = new DirectoryScanner(_root);
        }

        public DataSourceInfo Describe() => _inner.Describe();

        public void Run(ScanContext context, CancellationToken ct)
        {
            try
            {
                _inner.Run(context, ct);
            }
            finally
            {
                Directory.Delete(_root, true);
            }
        }
    }
}
