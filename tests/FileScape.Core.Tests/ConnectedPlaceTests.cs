using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Scanning;

namespace FileScape.Core.Tests;

/// <summary>Places over the connected namespace: link travel and return, Up beyond the root, identities, visibility.</summary>
public class ConnectedPlaceTests
{
    /// <summary>hub ─ C:\ ─ work ─ {app (project), docs ─ notes}, plus a junction work\to-notes → docs\notes.</summary>
    private static (FsTree Tree, int Work, int App, int Docs, int Notes, int Link) World(LinkStatus status = LinkStatus.Resolved)
    {
        var tree = new FsTree(new DataSourceInfo(DataOrigin.LiveFilesystem, "This computer", "test"));
        var b = new ScanBatch();
        void N(int id, int parent, string name, NodeKind kind, DirScanState state = DirScanState.Complete, NodeFlags flags = 0, long size = 0, LinkInfo? link = null, string? loc = null, LocationKind lk = LocationKind.None) =>
            b.NewNodes.Add(new NodeRecord(id, parent, name, kind, flags, 0, size, null, null, null, 0, state, link, loc, lk));
        N(0, -1, "This computer", NodeKind.Directory, loc: "", lk: LocationKind.Hub);
        N(1, 0, @"C:\", NodeKind.Directory, loc: @"C:\", lk: LocationKind.Drive);
        N(2, 1, "work", NodeKind.Directory);
        N(3, 2, "app", NodeKind.Directory);
        N(4, 2, "docs", NodeKind.Directory);
        N(5, 2, "to-notes", NodeKind.Link, DirScanState.NotApplicable, NodeFlags.ReparsePoint | NodeFlags.DirectoryLink,
            link: new LinkInfo(LinkKind.Junction, Reparse.TagMountPoint, @"C:\work\docs\notes", status, @"C:\work\docs\notes", status == LinkStatus.Resolved ? 7 : -1));
        N(6, 2, ".hidden-dir", NodeKind.Directory, flags: NodeFlags.Hidden);
        N(7, 4, "notes", NodeKind.Directory);
        N(8, 3, "src", NodeKind.Directory);
        N(9, 3, ".gitignore", NodeKind.File, DirScanState.NotApplicable, size: 10);
        N(10, 3, "App.csproj", NodeKind.File, DirScanState.NotApplicable, size: 2000);
        N(11, 3, "README.md", NodeKind.File, DirScanState.NotApplicable, size: 3000);
        N(12, 3, "Program.cs", NodeKind.File, DirScanState.NotApplicable, size: 400);
        N(13, 7, "today.md", NodeKind.File, DirScanState.NotApplicable, size: 50);
        tree.Apply(b);
        return (tree, 2, 3, 4, 7, 5);
    }

    [Fact]
    public void Following_a_link_and_coming_back_returns_to_the_link_with_up_going_to_the_physical_parent()
    {
        var (tree, work, _, docs, notes, link) = World();
        var nav = new PlaceNavigator();
        nav.Reset(work);
        var changes = new List<PlaceChange>();
        nav.Changed += changes.Add;
        nav.FollowLink(tree, link, notes).Should().BeTrue();
        nav.Current.Should().Be(notes);
        nav.CurrentVia.Should().Be(link);
        changes[^1].Move.Should().Be(PlaceMove.Link);

        nav.Back().Should().BeTrue();
        nav.Current.Should().Be(work);
        changes[^1].ViaLink.Should().Be(link, "the return arrives in front of the link it went through");

        nav.FollowLink(tree, link, notes);
        nav.Up(tree).Should().BeTrue();
        nav.Current.Should().Be(docs, "Up is the physical parent, not the place the link was in (LINK-03)");
    }

    [Fact]
    public void Up_continues_beyond_the_arrival_root_to_the_hub()
    {
        var (tree, work, _, _, _, _) = World();
        var nav = new PlaceNavigator();
        nav.Reset(work);
        nav.Up(tree).Should().BeTrue();
        nav.Current.Should().Be(1);
        nav.Up(tree).Should().BeTrue();
        nav.Current.Should().Be(0);
        nav.Up(tree).Should().BeFalse("the hub is the top");
        nav.Root.Should().Be(work, "the arrival root is unchanged");
    }

    [Fact]
    public void A_resolved_link_gate_can_be_entered_and_previews_its_destination()
    {
        var (tree, work, _, _, notes, link) = World();
        var c = PlaceComposer.Compose(tree, work);
        var g = c.GateFor(link)!;
        g.IsLink.Should().BeTrue();
        g.CanEnter.Should().BeTrue();
        c.Vistas.Should().Contain(v => v.FolderId == notes, "a resolved link shows where it leads");

        var (broken, w2, _, _, _, l2) = World(LinkStatus.Broken);
        PlaceComposer.Compose(broken, w2).GateFor(l2)!.CanEnter.Should().BeFalse();
    }

    [Fact]
    public void Hidden_entries_are_filtered_from_the_place_and_counted()
    {
        var (tree, work, _, _, _, _) = World();
        var all = PlaceComposer.Compose(tree, work);
        var visible = PlaceComposer.Compose(tree, work, visible: n => (n.Flags & (NodeFlags.Hidden | NodeFlags.System)) == 0);
        all.Gates.Count.Should().Be(visible.Gates.Count + 1);
        visible.HiddenCount.Should().Be(1);
        visible.Gates.Should().NotContain(g => tree[g.TargetId].Name == ".hidden-dir");
        tree[work].SubtreeDirCount.Should().Be(5, "totals are unaffected by visibility");
    }

    [Fact]
    public void A_software_project_is_recognised_from_a_manifest_and_a_readme_with_its_evidence()
    {
        var (tree, work, app, docs, _, _) = World();
        var k = PlaceKinds.Classify(tree, app);
        k.Kind.Should().Be(PlaceKind.Project);
        k.Evidence.Should().Contain("manifest App.csproj").And.Contain("readme README.md");
        k.CoreFiles.Select(id => tree[id].Name).Should().Equal("README.md", "App.csproj");
        PlaceKinds.Classify(tree, app, treatAsOrdinary: true).Kind.Should().Be(PlaceKind.Ordinary, "the user can turn it off");
        PlaceKinds.Classify(tree, docs).Kind.Should().Be(PlaceKind.Ordinary, "no manifest, no project");
        PlaceKinds.Classify(tree, work).Kind.Should().Be(PlaceKind.Ordinary);
        PlaceKinds.Classify(tree, 0).Kind.Should().Be(PlaceKind.Hub);
        PlaceKinds.Classify(tree, 1).Kind.Should().Be(PlaceKind.LocationRoot);
    }

    [Fact]
    public void A_manifest_alone_or_a_readme_alone_is_not_enough()
    {
        var s = new Spec("lonely").File("package.json");
        var t = s.Build();
        PlaceKinds.Classify(t, 0).Kind.Should().Be(PlaceKind.Ordinary);
        var r = new Spec("notes").File("README.md").File("todo.txt").Build();
        PlaceKinds.Classify(r, 0).Kind.Should().Be(PlaceKind.Ordinary);
        var g = new Spec("tool").Dir(new Spec(".git")).File("Cargo.toml").Build();
        PlaceKinds.Classify(g, 0).Kind.Should().Be(PlaceKind.Project, "a manifest plus version control is strong evidence");
    }

    [Fact]
    public void A_project_core_stands_by_the_avenue_clear_of_the_corridor()
    {
        var (tree, _, app, _, _, _) = World();
        var kind = PlaceKinds.Classify(tree, app);
        var c = PlaceComposer.Compose(tree, app, kind: kind);
        var core = c.Files.Where(f => f.Core).ToList();
        core.Select(f => tree[f.Id].Name).Should().BeEquivalentTo(["README.md", "App.csproj"]);
        foreach (var f in core)
        {
            MathF.Abs(f.Box.Center.X).Should().BeGreaterThan(PlaceParams.Default.CorridorHalfWidth + PlaceParams.Default.FileFootprint / 2 - 0.01f);
            f.Box.Center.Z.Should().BeGreaterThan(0, "south of the landmark, on the way in");
        }

        c.Kind.Kind.Should().Be(PlaceKind.Project);
        c.Files.Count.Should().Be(4, "every file is still present exactly once");
    }
}
