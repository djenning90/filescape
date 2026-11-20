using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Scanning;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-137..140 (AD-87): Open and Show in folder are distinct for every kind of thing the card can hold — files,
/// folders, location roots (drive, WSL distribution, network share, volume), links (junctions, symbolic links, Linux
/// links, shortcuts), items that cannot be reached (gone, access denied, an unreachable share, an online-only cloud
/// placeholder, a Linux pipe) and the hub — through <see cref="SelectionActions.Describe"/>, the entry point the app
/// calls with its tree.
/// </summary>
public class SelectionActionsKindsTests
{
    /// <summary>
    /// hub ─ C:\ ─ work ─ {notes.txt, photos, to-docs (junction), to-gone (broken symlink), Report.lnk, big.pptx (cloud),
    /// gone.txt (vanished), locked (denied), cloud-folder (online-only)}; hub ─ \\wsl.localhost\Ubuntu ─ etc ─ {fifo
    /// (Linux special), lx-link (Linux symlink)}; hub ─ \\server\share (unreachable); hub ─ \\?\Volume{…}\ (no letter).
    /// </summary>
    private static (FsTree Tree, Dictionary<string, int> Id) World(bool live = true)
    {
        var tree = new FsTree(new DataSourceInfo(live ? DataOrigin.LiveFilesystem : DataOrigin.Synthetic, "This computer", "test"));
        var b = new ScanBatch();
        var ids = new Dictionary<string, int>();
        void N(string key, int parent, string name, NodeKind kind, DirScanState state = DirScanState.Complete, NodeFlags flags = 0, LinkInfo? link = null, string? loc = null, LocationKind lk = LocationKind.None)
        {
            ids[key] = ids.Count;
            b.NewNodes.Add(new NodeRecord(ids[key], parent, name, kind, flags, 0, 0, null, null, null, 0, kind == NodeKind.Directory ? state : DirScanState.NotApplicable, link, loc, lk));
        }

        N("hub", -1, "This computer", NodeKind.Directory, loc: "", lk: LocationKind.Hub);
        N("drive", 0, @"C:\", NodeKind.Directory, loc: @"C:\", lk: LocationKind.Drive);
        N("work", ids["drive"], "work", NodeKind.Directory);
        N("file", ids["work"], "notes.txt", NodeKind.File);
        N("folder", ids["work"], "photos", NodeKind.Directory);
        N("junction", ids["work"], "to-docs", NodeKind.Link, flags: NodeFlags.ReparsePoint | NodeFlags.DirectoryLink,
            link: new LinkInfo(LinkKind.Junction, Reparse.TagMountPoint, @"C:\docs", LinkStatus.Resolved, @"C:\docs"));
        N("broken", ids["work"], "to-gone", NodeKind.Link, flags: NodeFlags.ReparsePoint,
            link: new LinkInfo(LinkKind.SymbolicLink, Reparse.TagSymlink, @"C:\gone", LinkStatus.Broken));
        N("shortcut", ids["work"], "Report.lnk", NodeKind.File);
        N("cloud", ids["work"], "big.pptx", NodeKind.File, flags: NodeFlags.CloudPlaceholder);
        N("gone", ids["work"], "gone.txt", NodeKind.File, flags: NodeFlags.Vanished);
        N("denied", ids["work"], "locked", NodeKind.Directory, DirScanState.Failed, NodeFlags.AccessDenied);
        N("cloudFolder", ids["work"], "cloud-folder", NodeKind.Directory, DirScanState.NotTraversed, NodeFlags.CloudPlaceholder | NodeFlags.CloudNotTraversed);
        N("wsl", 0, "Ubuntu", NodeKind.Directory, loc: @"\\wsl.localhost\Ubuntu", lk: LocationKind.Wsl);
        N("etc", ids["wsl"], "etc", NodeKind.Directory);
        N("fifo", ids["etc"], "fifo", NodeKind.File, flags: NodeFlags.LinuxSpecial);
        N("lxlink", ids["etc"], "lx-link", NodeKind.Link, flags: NodeFlags.ReparsePoint,
            link: new LinkInfo(LinkKind.LinuxSymlink, 0xA000001D, "../usr", LinkStatus.Unsupported));
        N("share", 0, @"\\server\share", NodeKind.Directory, DirScanState.Failed, NodeFlags.Unavailable, loc: @"\\server\share", lk: LocationKind.Network);
        N("volume", 0, @"\\?\Volume{0000}\", NodeKind.Directory, loc: @"\\?\Volume{0000}\", lk: LocationKind.Volume);
        tree.Apply(b);
        return (tree, ids);
    }

    private static readonly CardView[] Views = [CardView.Map, CardView.Places, CardView.PlanetSurface, CardView.PlanetOverview];

    private static CardActions On(string key, CardView view = CardView.PlanetSurface, bool live = true)
    {
        var (tree, id) = World(live);
        return SelectionActions.For(view, SelectionActions.Describe(tree, id[key]));
    }

    [Fact]
    public void Describe_classifies_what_the_app_selects()
    {
        var (tree, id) = World();
        SelectionActions.Describe(tree, id["hub"]).Kind.Should().Be(CardTarget.Hub);
        SelectionActions.Describe(tree, id["drive"]).Kind.Should().Be(CardTarget.LocationRoot);
        SelectionActions.Describe(tree, id["drive"]).Location.Should().Be(LocationKind.Drive);
        SelectionActions.Describe(tree, id["wsl"]).Location.Should().Be(LocationKind.Wsl);
        SelectionActions.Describe(tree, id["folder"]).Kind.Should().Be(CardTarget.Folder);
        SelectionActions.Describe(tree, id["file"]).Kind.Should().Be(CardTarget.File);
        SelectionActions.Describe(tree, id["junction"]).Kind.Should().Be(CardTarget.Link);
        SelectionActions.Describe(tree, id["shortcut"]).Shortcut.Should().BeTrue();
        SelectionActions.Describe(tree, id["file"]).Shortcut.Should().BeFalse();
        SelectionActions.Describe(tree, id["gone"]).Availability.Should().Be(CardAvailability.Missing);
        SelectionActions.Describe(tree, id["denied"]).Availability.Should().Be(CardAvailability.Denied);
        SelectionActions.Describe(tree, id["share"]).Availability.Should().Be(CardAvailability.Unreachable);
        SelectionActions.Describe(tree, id["file"]).Availability.Should().Be(CardAvailability.Available);
        SelectionActions.Describe(tree, id["hub"]).IsTop.Should().BeTrue();
        SelectionActions.Describe(tree, id["drive"]).IsTop.Should().BeFalse("a drive's parent is the hub");
        var t = SelectionActions.Describe(tree, id["file"], standingOn: true, inView: false);
        t.StandingOn.Should().BeTrue();
        t.InView.Should().BeFalse();
        t.Live.Should().BeTrue();
        t.Name.Should().Be("notes.txt");
    }

    [Fact]
    public void Open_on_a_folder_opens_it_and_Show_reveals_it_in_its_containing_folder()
    {
        var a = On("folder");
        a.Open.Should().Be(new CardAction(true, "Open folder (Enter)", "Open photos in File Explorer, showing what is inside it."));
        a.Reveal.Should().Be(new CardAction(true, "Show in folder", "Open the folder that contains photos in File Explorer, with photos selected."));

        var f = On("file");
        f.Open.Should().Be(new CardAction(true, "Open (Enter)", "Open notes.txt with the app Windows uses for .txt files. Asks first for programs and scripts."));
        f.Reveal.Should().Be(new CardAction(true, "Show in folder", "Open the folder that contains notes.txt in File Explorer, with notes.txt selected."));
    }

    [Fact]
    public void A_location_root_is_shown_where_Windows_lists_it_or_says_why_not()
    {
        var drive = On("drive");
        drive.Open.Should().Be(new CardAction(true, "Open drive (Enter)", @"Open C:\ in File Explorer, showing what is inside it."));
        drive.Reveal.Should().Be(new CardAction(true, "Show in This PC", @"Open This PC in File Explorer, with C:\ selected."));
        var wsl = On("wsl");
        wsl.Open.Label.Should().Be("Open folder (Enter)");
        wsl.Reveal.Enabled.Should().BeFalse("File Explorer opens Linux but does not select a distribution there (measured 2026-11-20)");
        wsl.Reveal.Tooltip.Should().Contain("does not select").And.Contain("Open folder");
        var volume = On("volume");
        volume.Open.Enabled.Should().BeTrue();
        volume.Reveal.Enabled.Should().BeFalse();
        volume.Reveal.Tooltip.Should().Contain("no drive letter").And.Contain("Open");
        var share = On("share");
        share.Open.Enabled.Should().BeFalse();
        share.Open.Tooltip.Should().Contain("could not be reached");
        share.Reveal.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Links_open_their_target_and_are_shown_as_themselves()
    {
        var j = On("junction");
        j.Open.Should().Be(new CardAction(true, "Open target (Enter)", @"Open the junction's target (C:\docs) with Windows. Asks first: FileScape did not follow it."));
        j.Reveal.Should().Be(new CardAction(true, "Show in folder", "Open the folder that contains to-docs in File Explorer, with the junction itself selected (not its target)."));
        var broken = On("broken");
        broken.Open.Enabled.Should().BeFalse();
        broken.Open.Tooltip.Should().Be(@"Unavailable: the symbolic link's target (C:\gone) does not exist. Show in folder selects the link itself.");
        broken.Reveal.Enabled.Should().BeTrue("the link itself is there");
        var lx = On("lxlink");
        lx.Open.Enabled.Should().BeFalse();
        lx.Open.Tooltip.Should().Contain("Windows cannot follow");
        lx.Reveal.Enabled.Should().BeTrue();
        var lnk = On("shortcut");
        lnk.Open.Should().Be(new CardAction(true, "Open (Enter)", "Open the shortcut: Windows starts what Report.lnk points to. Asks first."));
        lnk.Reveal.Tooltip.Should().Be("Open the folder that contains Report.lnk in File Explorer, with the shortcut itself selected (not its target).");
    }

    [Fact]
    public void Unavailable_items_say_why_and_what_still_works()
    {
        var gone = On("gone");
        gone.Open.Should().Be(new CardAction(false, "Open (Enter)", "Unavailable: gone.txt is no longer there (moved, renamed or deleted since FileScape listed it). Parent selects its folder."));
        gone.Reveal.Enabled.Should().BeFalse();
        gone.Reveal.Tooltip.Should().Contain("no longer there");
        gone.Copy.Enabled.Should().BeTrue("the path is still text to copy");

        var denied = On("denied");
        denied.Open.Enabled.Should().BeFalse("File Explorer would offer to change its permissions");
        denied.Open.Tooltip.Should().Contain("permissions").And.Contain("Show in folder");
        denied.Reveal.Enabled.Should().BeTrue("its containing folder can be shown with it selected");

        var cloud = On("cloud");
        cloud.Open.Enabled.Should().BeTrue();
        cloud.Open.Tooltip.Should().Contain("online-only").And.Contain("download");
        cloud.Reveal.Enabled.Should().BeTrue("showing it does not download it");
        On("cloudFolder").Open.Tooltip.Should().Contain("online-only");

        var fifo = On("fifo");
        fifo.Open.Enabled.Should().BeFalse();
        fifo.Open.Tooltip.Should().Contain("Linux").And.Contain("Show in folder");
        fifo.Reveal.Enabled.Should().BeTrue();
    }

    [Fact]
    public void The_hub_and_synthetic_data_keep_their_rules()
    {
        var hub = On("hub");
        hub.Open.Should().Be(new CardAction(true, "Open This PC (Enter)", "Open This PC in File Explorer."));
        hub.Reveal.Enabled.Should().BeFalse();
        hub.Copy.Enabled.Should().BeFalse();
        var synthetic = On("folder", live: false);
        synthetic.Reveal.Enabled.Should().BeFalse();
        synthetic.Reveal.Tooltip.Should().Contain("synthetic");
    }

    [Fact]
    public void Every_kind_in_every_view_has_distinct_open_and_show_and_says_why_when_off()
    {
        var (tree, id) = World();
        foreach (var view in Views)
        foreach (var (key, node) in id)
        {
            var a = SelectionActions.For(view, SelectionActions.Describe(tree, node));
            a.Open.Label.Should().NotBe(a.Reveal.Label, key);
            a.Open.Label.Contains("(Enter)").Should().Be(view != CardView.PlanetOverview, $"{key} in {view}");
            foreach (var act in new[] { a.Open, a.Reveal })
            {
                if (!act.Enabled) act.Tooltip.Should().StartWith(SelectionActions.Unavailable, $"{key} in {view}");
                else act.Tooltip.Should().NotStartWith(SelectionActions.Unavailable, $"{key} in {view}");
            }
        }
    }
}
