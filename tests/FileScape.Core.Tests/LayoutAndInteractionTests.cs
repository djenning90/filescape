using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Scanning;
using FileScape.Core.Shell;

namespace FileScape.Core.Tests;

/// <summary>Layout geometry (VIS-01..06), picking (NAV-02), selection (NAV-06), and classification (VIS-04).</summary>
public class LayoutAndInteractionTests
{
    private static readonly Lazy<FsTree> Tree = new(() => ScanRunner.Run(new SyntheticTreeSource { TargetNodes = 6000 }).Tree);

    public static TheoryData<string> Styles => new() { "classic", "district" };

    private static LayoutParams ParamsFor(string style) => style == "classic" ? LayoutParams.Classic : LayoutParams.District;

    public static TheoryData<string> AllLayouts => new() { "classic", "district", "rows" };

    private static LayoutParams ParamsForAny(string style) => style == "rows" ? LayoutParams.Classic with { Arrangement = Arrangement.Rows } : ParamsFor(style);

    /// <summary>VIS-05: subtrees must never overlap; otherwise picking and reading the hierarchy become ambiguous.</summary>
    [Theory]
    [MemberData(nameof(AllLayouts))]
    public void Sibling_subtrees_never_overlap(string style)
    {
        var tree = Tree.Value;
        var layout = TreeLayout.Compute(tree, ParamsForAny(style));
        foreach (var dir in tree.Nodes.Where(n => n.IsDirectory))
        {
            var subdirs = dir.Children.Where(c => tree[c].IsDirectory).ToList();
            for (var i = 0; i < subdirs.Count; i++)
            {
                for (var j = i + 1; j < subdirs.Count; j++)
                {
                    var a = Flatten(layout.SubtreeBounds[subdirs[i]]);
                    var b = Flatten(layout.SubtreeBounds[subdirs[j]]);
                    a.Overlaps(b, 1e-3f).Should().BeFalse($"subtrees {tree[subdirs[i]].Name} and {tree[subdirs[j]].Name} under {dir.Name} must not overlap");
                }

                // A child subtree must not overlap its parent's own pedestal either.
                Flatten(layout.SubtreeBounds[subdirs[i]]).Overlaps(Flatten(layout.NodeBoxes[dir.Id]), 1e-3f).Should().BeFalse();
            }
        }
    }

    /// <summary>Compares footprints in the ground plane (XZ). Height does not matter for overlap.</summary>
    private static Box Flatten(Box b) => new(b.Min with { Y = 0 }, b.Max with { Y = 1 });

    /// <summary>
    /// AD-08: the fishbone arrangement keeps real-shaped trees two-dimensional, so a single overview can show them.
    /// The historical rows arrangement is kept for comparison and is expected to be much wider.
    /// </summary>
    [Fact]
    public void Fishbone_is_far_more_compact_than_historical_rows()
    {
        var tree = Tree.Value;
        var fish = TreeLayout.Compute(tree, LayoutParams.Classic).Bounds.Size;
        var rows = TreeLayout.Compute(tree, LayoutParams.Classic with { Arrangement = Arrangement.Rows }).Bounds.Size;
        var fishAspect = MathF.Max(fish.X, fish.Z) / MathF.Min(fish.X, fish.Z);
        var rowsAspect = rows.X / rows.Z;
        fishAspect.Should().BeLessThan(6f, $"fishbone bounds {fish.X:0}×{fish.Z:0}");
        rowsAspect.Should().BeGreaterThan(fishAspect, $"rows bounds {rows.X:0}×{rows.Z:0}");
        (fish.X * fish.Z).Should().BeLessThan(rows.X * rows.Z, "fishbone should use less ground area");
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Files_stand_on_their_own_pedestal_without_overlapping(string style)
    {
        var tree = Tree.Value;
        var layout = TreeLayout.Compute(tree, ParamsFor(style));
        foreach (var dir in tree.Nodes.Where(n => n.IsDirectory))
        {
            var ped = layout.NodeBoxes[dir.Id];
            var leaves = dir.Children.Where(c => !tree[c].IsDirectory).Select(c => layout.NodeBoxes[c]).ToList();
            foreach (var leaf in leaves)
            {
                leaf.Min.Y.Should().BeApproximately(ped.Max.Y, 1e-4f);
                leaf.Min.X.Should().BeGreaterThanOrEqualTo(ped.Min.X);
                leaf.Max.X.Should().BeLessThanOrEqualTo(ped.Max.X);
                leaf.Min.Z.Should().BeGreaterThanOrEqualTo(ped.Min.Z);
                leaf.Max.Z.Should().BeLessThanOrEqualTo(ped.Max.Z);
            }

            if (dir.Name == "wide-2000-files")
            {
                // Exhaustively check one large grid for overlaps.
                for (var i = 0; i < leaves.Count; i++)
                {
                    for (var j = i + 1; j < Math.Min(leaves.Count, i + 50); j++)
                    {
                        leaves[i].Overlaps(leaves[j]).Should().BeFalse();
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllLayouts))]
    public void Children_recede_behind_their_parent(string style)
    {
        var tree = Tree.Value;
        var layout = TreeLayout.Compute(tree, ParamsForAny(style));
        foreach (var n in tree.Nodes.Where(n => n.IsDirectory && !n.IsRoot))
        {
            layout.NodeBoxes[n.Id].Max.Z.Should().BeLessThan(layout.NodeBoxes[n.ParentId].Min.Z);
        }
    }

    [Fact]
    public void Layout_is_deterministic()
    {
        var a = TreeLayout.Compute(Tree.Value, LayoutParams.Classic);
        var b = TreeLayout.Compute(Tree.Value, LayoutParams.Classic);
        a.NodeBoxes.Should().Equal(b.NodeBoxes);
    }

    [Fact]
    public void Height_increases_with_size_and_zero_byte_files_remain_visible()
    {
        var s = SizeScale.Classic;
        s.Height(0).Should().BeGreaterThan(0);
        s.Height(1).Should().BeGreaterThan(s.Height(0));
        s.Height(1_000).Should().BeLessThan(s.Height(1_000_000));
        (s.Height(10_000_000) - s.Height(1_000_000)).Should().BeApproximately(s.PerDecade, 0.01f, "each ×10 adds PerDecade");
        s.IsCapped(long.MaxValue).Should().BeTrue();
        s.Describe(long.MaxValue).Should().Contain("CAPPED");
        s.Describe(0).Should().Contain("zero-byte");
    }

    [Fact]
    public void Connectors_join_every_directory_to_its_parent()
    {
        var tree = Tree.Value;
        var layout = TreeLayout.Compute(tree, LayoutParams.District);
        layout.Connectors.Should().HaveCount(tree.Nodes.Count(n => n.IsDirectory) - 1);
        layout.Connectors.Should().OnlyContain(c => (c.Points.Length == 2 || c.Points.Length == 4 || c.Points.Length == 6) && c.Points.All(p => p.Y < 0.1f));
        foreach (var c in layout.Connectors)
        {
            // Routes start at the parent's back edge and end at the child's front edge, and every segment is axis-aligned.
            c.Points[0].Z.Should().BeApproximately(layout.NodeBoxes[c.ParentId].Min.Z, 1e-3f);
            c.Points[^1].Z.Should().BeApproximately(layout.NodeBoxes[c.ChildId].Max.Z, 1e-3f);
            for (var i = 1; i < c.Points.Length; i++)
            {
                var d = c.Points[i] - c.Points[i - 1];
                (MathF.Abs(d.X) < 1e-4f || MathF.Abs(d.Z) < 1e-4f).Should().BeTrue("streets are orthogonal");
            }
        }
    }

    [Fact]
    public void Picking_straight_down_hits_the_file_under_the_ray_and_misses_empty_space()
    {
        var tree = Tree.Value;
        var layout = TreeLayout.Compute(tree, LayoutParams.Classic);
        var file = tree.Find("hundred-mib.iso");
        var box = layout.NodeBoxes[file.Id];

        Picker.Pick(tree, layout, box.Center with { Y = 100 }, -Vector3.UnitY).Should().Be(file.Id);
        Picker.Pick(tree, layout, new Vector3(box.Center.X, 100, 5000), -Vector3.UnitY).Should().Be(-1);
    }

    [Fact]
    public void Picking_a_zero_byte_tile_works()
    {
        var tree = Tree.Value;
        var layout = TreeLayout.Compute(tree, LayoutParams.Classic);
        var zero = tree[tree.Find("zero-byte-200").Children[17]];
        var c = layout.NodeBoxes[zero.Id].Center;
        Picker.Pick(tree, layout, c with { Y = 50 }, -Vector3.UnitY).Should().Be(zero.Id);
    }

    [Fact]
    public void Picking_returns_the_nearest_of_overlapping_candidates_along_the_ray()
    {
        var tree = Tree.Value;
        var layout = TreeLayout.Compute(tree, LayoutParams.Classic);
        var dir = tree.Find("size-outliers");
        var ped = layout.NodeBoxes[dir.Id];

        // A horizontal ray through the pedestal's front face, below the files, hits the pedestal first.
        var origin = new Vector3(ped.Center.X, ped.Max.Y * 0.5f, ped.Max.Z + 50);
        Picker.Pick(tree, layout, origin, -Vector3.UnitZ).Should().Be(dir.Id);
    }

    [Fact]
    public void Selection_survives_switching_layouts_and_supports_back_and_parent()
    {
        var tree = Tree.Value;
        var selection = new SelectionState();
        var file = tree.Find("four-gib.mkv");
        selection.Select(file.Id);

        var classic = TreeLayout.Compute(tree, LayoutParams.Classic);
        var district = TreeLayout.Compute(tree, LayoutParams.District);

        selection.Selected.Should().Be(file.Id, "selection refers to node identity, not scene objects");
        classic.NodeBoxes[selection.Selected].Should().NotBe(district.NodeBoxes[selection.Selected]);
        Picker.Pick(tree, district, district.NodeBoxes[file.Id].Center with { Y = 200 }, -Vector3.UnitY).Should().Be(file.Id);

        selection.Parent(tree).Should().BeTrue();
        selection.Selected.Should().Be(file.ParentId);
        selection.Back().Should().BeTrue();
        selection.Selected.Should().Be(file.Id);
        selection.Back().Should().BeFalse("only one step was recorded before back");
    }

    [Fact]
    public void Parent_at_root_is_refused()
    {
        var selection = new SelectionState();
        selection.Select(0);
        selection.Parent(Tree.Value).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 0)]
    [InlineData(8, 1)]
    [InlineData(31, 2)]
    [InlineData(100, 4)]
    [InlineData(365, 5)]
    [InlineData(366, 6)]
    public void Age_bands_follow_fsn_defaults(int daysOld, int band)
    {
        var now = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        AgeBands.Band(now.AddDays(-daysOld), now).Should().Be(band);
    }

    [Theory]
    [InlineData("setup.exe", true)]
    [InlineData("SCRIPT.PS1", true)]
    [InlineData("shortcut.lnk", true)]
    [InlineData("trailing.exe.", true)]
    [InlineData("notes.txt", false)]
    [InlineData("photo.jpg", false)]
    public void Launch_risk_classifies_code_running_extensions(string name, bool risky)
    {
        LaunchRisk.RunsCode(name).Should().Be(risky);
    }

    [Fact]
    public void Mock_shell_never_claims_success()
    {
        var r = new MockShellActions().Open("synthetic://x");
        r.Succeeded.Should().BeFalse();
        r.WasMock.Should().BeTrue();
        r.Message.Should().StartWith("MOCK");
    }

    /// <summary>
    /// AD-08: a directory with hundreds of children (a package cache, a photo dump) must not become a single
    /// endless spine. Multi-spine fishbone keeps the block roughly square.
    /// </summary>
    [Fact]
    public void Directory_with_hundreds_of_subdirectories_stays_roughly_square()
    {
        var tree = new FsTree(new DataSourceInfo(DataOrigin.Synthetic, "synthetic://many", "many"));
        var batch = new ScanBatch();
        batch.NewNodes.Add(new NodeRecord(0, -1, "root", NodeKind.Directory, 0, 0, 0, null, null, null, 0, DirScanState.Complete));
        for (var i = 1; i <= 300; i++)
        {
            batch.NewNodes.Add(new NodeRecord(i, 0, $"pkg-{i:D3}", NodeKind.Directory, 0, 0, 0, null, null, null, 1, DirScanState.Complete));
        }

        tree.Apply(batch);
        foreach (var style in new[] { LayoutParams.Classic, LayoutParams.District })
        {
            var b = TreeLayout.Compute(tree, style).Bounds.Size;
            (MathF.Max(b.X, b.Z) / MathF.Min(b.X, b.Z)).Should().BeLessThan(3f, $"bounds {b.X:0}×{b.Z:0}");
        }

        var layout = TreeLayout.Compute(tree, LayoutParams.Classic);
        for (var i = 1; i <= 300; i++)
        {
            for (var j = i + 1; j <= 300; j++)
            {
                layout.NodeBoxes[i].Overlaps(layout.NodeBoxes[j]).Should().BeFalse();
            }
        }
    }
}
