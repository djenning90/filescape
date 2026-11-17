using FileScape.Core.Model;
using FileScape.Core.Places;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-39..41 (AD-56): the Destinations tree (rows, states before and after expanding, filtering, bounds), trip
/// planning and its history rule, the repository index and path lookup. No engine.
/// </summary>
public class DestinationTreeTests
{
    private static FsTree Sky() =>
        new Spec("Sky")
            .Dir(new Spec("Garden").Dir(new Spec("Pressed").File("a.png")).File("b.png"))
            .Dir(Spec.Chain("Long Way Down", 5))
            .Dir(new Spec("Quiet Moon"))
            .Dir(new Spec("Music").File("1.wav").File("2.wav"))
            .Dir(new Spec("locked") { State = DirScanState.Failed, Flags = NodeFlags.AccessDenied })
            .Dir(new Spec("far") { State = DirScanState.Unlisted })
            .Dir(new Spec("queued") { State = DirScanState.Listing })
            .Dir(new Spec("Workshop").Dir(new Spec(".git")).Dir(new Spec("src")).File("README.md"))
            .Dir(new Spec("shortcut") { IsLink = true })
            .File("notes.txt").File("map.png")
            .Build();

    private static DestinationList Build(FsTree t, params string[] expanded) =>
        DestinationTree.Build(t, new DestinationQuery { Current = 0, Expanded = new HashSet<string>(expanded) });

    private static DestinationRow Row(DestinationList l, string name) => l.Rows.Single(r => r.Name == name);

    [Fact]
    public void Each_folder_says_what_is_behind_it_before_it_is_opened()
    {
        var l = Build(Sky());
        Row(l, "Quiet Moon").Detail.Should().Be("empty");
        Row(l, "Quiet Moon").CanExpand.Should().BeFalse("an empty folder has nothing to open");
        Row(l, "Music").Detail.Should().Be("2 files");
        Row(l, "Music").CanExpand.Should().BeFalse("no folders inside: nothing to go further down to");
        Row(l, "locked").Detail.Should().Be("access denied");
        Row(l, "locked").Warning.Should().BeTrue();
        Row(l, "far").Detail.Should().Be("not listed yet");
        Row(l, "far").CanExpand.Should().BeTrue("it may hold folders: opening it lists it");
        Row(l, "queued").Detail.Should().Be("listing…");
        Row(l, "Garden").Detail.Should().Be("1 folder · 1 file");
        Row(l, "shortcut").Kind.Should().Be(DestinationRowKind.Link);
        Row(l, "shortcut").CanExpand.Should().BeFalse("a link is followed on purpose, never opened from the list");
    }

    [Fact]
    public void Expanding_shows_a_folders_own_folders_one_level_deeper_and_keeps_going()
    {
        var t = Sky();
        var lwd = t.Find("Long Way Down").Id;
        var l1 = t.Find("level-01").Id;
        var l = Build(t, $"f{lwd}", $"f{l1}");
        var names = l.Rows.Where(r => r.Kind == DestinationRowKind.Folder).Select(r => (r.Name, r.Depth)).ToList();
        names.Should().ContainInOrder(("Long Way Down", 0), ("level-01", 1), ("level-02", 2));
        Row(l, "Long Way Down").Expanded.Should().BeTrue();
        Row(l, "level-02").Expanded.Should().BeFalse("only what the player opened is open");
    }

    [Fact]
    public void An_unlisted_folder_opened_is_asked_for_and_shown_as_listing_and_nothing_else_is()
    {
        var t = Sky();
        var far = t.Find("far").Id;
        var l = Build(t, $"f{far}");
        l.NeedsListing.Should().Equal(far);
        var i = l.Rows.ToList().FindIndex(r => r.Name == "far");
        l.Rows[i + 1].Kind.Should().Be(DestinationRowKind.Note);
        l.Rows[i + 1].Name.Should().Be("listing…");
        Build(t).NeedsListing.Should().BeEmpty("a closed folder is never listed for the list");
    }

    [Fact]
    public void A_branch_with_no_folders_says_why_empty_denied_or_files_only()
    {
        var t = Sky();
        var garden = t.Find("Garden").Id;
        var pressed = t.Find("Pressed").Id;
        var l = Build(t, $"f{garden}", $"f{pressed}");
        // Pressed holds only a file; it cannot be opened (no folders), so its row says "1 file" instead.
        Row(l, "Pressed").Detail.Should().Be("1 file");
        Row(l, "Pressed").Expanded.Should().BeFalse();
    }

    [Fact]
    public void Files_are_a_group_closed_when_there_are_worlds_and_open_when_there_are_none()
    {
        var t = Sky();
        var l = Build(t);
        Row(l, "Files here").Expanded.Should().BeFalse();
        l.Rows.Should().NotContain(r => r.Kind == DestinationRowKind.File);
        l.HereCount.Should().Be(11, "9 worlds (8 folders and a link) and 2 files: every entry here can be reached");
        var music = DestinationTree.Build(t, new DestinationQuery { Current = t.Find("Music").Id });
        Row(music, "Files here").Expanded.Should().BeTrue("a folder of files only shows them");
        music.Rows.Count(r => r.Kind == DestinationRowKind.File).Should().Be(2);
        Row(music, "Back up to Sky").Kind.Should().Be(DestinationRowKind.WayBack);
    }

    [Fact]
    public void The_filter_keeps_matches_and_the_branches_that_lead_to_them()
    {
        var t = Sky();
        var lwd = t.Find("Long Way Down").Id;
        var l1 = t.Find("level-01").Id;
        var l = DestinationTree.Build(t, new DestinationQuery { Current = 0, Filter = "level-02", Expanded = new HashSet<string> { $"f{lwd}", $"f{l1}" } });
        l.Rows.Select(r => r.Name).Should().Equal("Long Way Down", "level-01", "level-02");
        var found = DestinationTree.Build(t, new DestinationQuery { Current = 0, Filter = "long way", Expanded = new HashSet<string> { $"f{lwd}" } });
        found.Rows.Select(r => r.Name).Should().Equal(["Long Way Down", "level-01"], "a match opened shows what is inside it, not only more matches");
        var deep = DestinationTree.Build(t, new DestinationQuery { Current = 0, Filter = "level-04" });
        deep.Rows.Select(r => r.Name).Should().Equal(["Long Way Down", "level-01", "level-02", "level-03", "level-04"], "typing finds a folder deep down among what is listed, and opens the way to it");
        deep.NeedsListing.Should().BeEmpty("searching never lists anything");
        var files = DestinationTree.Build(t, new DestinationQuery { Current = 0, Filter = "map" });
        files.Rows.Should().Contain(r => r.Kind == DestinationRowKind.File && r.Name == "map.png", "the files group opens to show a match");
        files.HereCount.Should().Be(1);
    }

    [Fact]
    public void A_wide_branch_is_bounded_and_says_how_many_more()
    {
        var t = new Spec("Root").Dir(Spec.WithDirs("wide", 600)).Build();
        var wide = t.Find("wide").Id;
        var l = DestinationTree.Build(t, new DestinationQuery { Current = 0, Expanded = new HashSet<string> { $"f{wide}" } });
        l.Rows.Count(r => r.Depth == 1 && r.Kind == DestinationRowKind.Folder).Should().Be(250);
        l.Rows.Should().Contain(r => r.Kind == DestinationRowKind.Note && r.Name.Contains("350 more"));
    }

    [Fact]
    public void Favourites_list_pins_then_repositories_and_mark_where_you_are()
    {
        var t = Sky();
        var ws = t.Find("Workshop").Id;
        var favs = new List<FavouritePlace>
        {
            new(FavouriteKind.Pinned, @"D:\work\notes", "notes", -1),
            new(FavouriteKind.Repository, t.FullPath(ws), "Workshop", ws),
        };
        var l = DestinationTree.Build(t, new DestinationQuery { Current = ws, Favourites = favs });
        var section = l.Rows.ToList().FindIndex(r => r.Kind == DestinationRowKind.Section);
        l.Rows[section].Expanded.Should().BeTrue();
        l.Rows[section + 1].Detail.Should().StartWith("pinned");
        l.Rows[section + 2].IsHere.Should().BeTrue();
        l.Rows[section + 2].IsRepository.Should().BeTrue();
        var closed = DestinationTree.Build(t, new DestinationQuery { Current = 0, Favourites = favs, Expanded = new HashSet<string> { "!fav" } });
        closed.Rows.Should().NotContain(r => r.Kind == DestinationRowKind.Favourite);
    }

    [Fact]
    public void A_trip_is_planned_one_level_at_a_time_and_only_downwards()
    {
        var t = Sky();
        var lwd = t.Find("Long Way Down").Id;
        var deep = t.Find("level-04").Id;
        var hops = TripPlan.Hops(t, 0, deep)!;
        hops.Select(h => t[h].Name).Should().Equal("Long Way Down", "level-01", "level-02", "level-03", "level-04");
        TripPlan.Hops(t, lwd, 0).Should().BeNull("a trip goes down; up is U");
        TripPlan.Hops(t, deep, deep).Should().BeNull();
        TripPlan.Unavailable(t, 0, lwd).Should().BeNull();
        TripPlan.Unavailable(t, lwd, deep).Should().NotBeNull("level-04 is not a child of Long Way Down");
    }

    [Fact]
    public void A_whole_trip_is_one_step_back()
    {
        var t = Sky();
        var nav = new PlaceNavigator();
        nav.Reset(0);
        var music = t.Find("Music").Id;
        nav.Enter(t, music).Should().BeTrue();
        nav.Up(t).Should().BeTrue();
        var hops = TripPlan.Hops(t, 0, t.Find("level-03").Id)!;
        foreach (var h in hops) nav.Enter(t, h).Should().BeTrue();
        nav.MergeLastMoves(hops.Count);
        nav.Back().Should().BeTrue();
        nav.Current.Should().Be(0, "Backspace after the trip returns to where it started");
        nav.Back().Should().BeTrue();
        nav.Current.Should().Be(music, "and the history before the trip is intact");
    }

    [Fact]
    public void Repositories_are_found_incrementally_and_paths_resolve_without_listing()
    {
        var t = Sky();
        var idx = new RepositoryIndex();
        idx.Update(t, 3).Should().BeFalse("the first three nodes hold no .git");
        idx.CaughtUp.Should().BeFalse();
        while (!idx.CaughtUp) idx.Update(t, 3);
        idx.Repositories.Should().Equal(t.Find("Workshop").Id);
        var src = t.Find("src").Id;
        PathLookup.Find(t, t.FullPath(src)).Should().Be(src);
        PathLookup.Find(t, t.FullPath(src).ToUpperInvariant()).Should().Be(src, "Windows names differ only in case");
        PathLookup.Find(t, t.FullPath(src) + @"\nothing").Should().Be(-1);
    }
}
