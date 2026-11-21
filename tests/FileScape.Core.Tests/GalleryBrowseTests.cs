using FileScape.Core.Movement;
using FileScape.Core.Planets;

namespace FileScape.Core.Tests;

/// <summary>
/// The gallery's browsing rule (PLANET-159..164, AD-94) driven through the calls PlanetWorld and PictureViewer make:
/// look (a click or E on a picture), view large (a double-click, R, ⤢), step, back to the preview, walk on; Windows
/// only on Enter / "Open with Windows ↗"; the preview always shows the selection. Also the viewer's mouse zones and the
/// arrow-key sidestep (PLANET-165).
/// </summary>
public class GalleryBrowseTests
{
    private const int Dawn = 11, Harbour = 12, Notes = 20, Folder = 30;

    private static BrowseStep On(BrowseState s, BrowseGesture g, BrowseTarget t, int id) => GalleryBrowse.On(s, g, t, id);

    [Fact]
    public void A_click_on_a_picture_looks_at_it_from_walking()
    {
        var r = On(BrowseState.Walking, BrowseGesture.Click, BrowseTarget.Picture, Dawn);
        r.Next.Should().Be(BrowseState.Previewing(Dawn));
        r.Effects.Should().Equal(BrowseEffect.Select, BrowseEffect.ShowPreview);
    }

    [Fact]
    public void A_click_on_another_picture_moves_the_open_preview_to_it()
    {
        // The defect this replaces: the click selected the harbour while the preview went on showing the dawn.
        var r = On(BrowseState.Previewing(Dawn), BrowseGesture.Click, BrowseTarget.Picture, Harbour);
        r.Next.Should().Be(BrowseState.Previewing(Harbour));
        r.Effects.Should().Equal(BrowseEffect.Select, BrowseEffect.ShowPreview);
    }

    [Fact]
    public void The_preview_follows_the_selection_to_any_file_and_closes_for_a_place()
    {
        On(BrowseState.Previewing(Dawn), BrowseGesture.Click, BrowseTarget.File, Notes).Next.Should().Be(BrowseState.Previewing(Notes));
        var place = On(BrowseState.Previewing(Dawn), BrowseGesture.Click, BrowseTarget.Place, Folder);
        place.Next.Should().Be(BrowseState.Walking);
        place.Effects.Should().Equal(BrowseEffect.Select, BrowseEffect.ClosePreview);
        // Walking, a click on a document only selects it (the card), as before: nothing is read.
        On(BrowseState.Walking, BrowseGesture.Click, BrowseTarget.File, Notes).Effects.Should().Equal(BrowseEffect.Select);
    }

    [Fact]
    public void A_click_on_empty_ground_puts_the_preview_away_and_does_nothing_else()
    {
        var r = On(BrowseState.Previewing(Dawn), BrowseGesture.Click, BrowseTarget.Nothing, -1);
        r.Next.Should().Be(BrowseState.Walking);
        r.Effects.Should().Equal(BrowseEffect.ClosePreview, BrowseEffect.Consume);
        On(BrowseState.Walking, BrowseGesture.Click, BrowseTarget.Nothing, -1).Effects.Should().BeEmpty();
    }

    [Fact]
    public void A_double_click_on_a_picture_views_it_large_and_never_opens_Windows()
    {
        foreach (var from in new[] { BrowseState.Walking, BrowseState.Previewing(Dawn), BrowseState.Previewing(Harbour) })
        {
            var r = On(from, BrowseGesture.DoubleClick, BrowseTarget.Picture, Dawn);
            r.Next.Should().Be(BrowseState.Viewing(Dawn));
            r.Has(BrowseEffect.OpenViewer).Should().BeTrue();
            r.Has(BrowseEffect.OpenWithWindows).Should().BeFalse($"from {from}");
        }
    }

    [Fact]
    public void A_double_click_on_any_other_file_is_still_the_deliberate_Open_and_on_a_place_goes_there()
    {
        On(BrowseState.Walking, BrowseGesture.DoubleClick, BrowseTarget.File, Notes).Effects.Should().Equal(BrowseEffect.Select, BrowseEffect.OpenWithWindows);
        On(BrowseState.Previewing(Dawn), BrowseGesture.DoubleClick, BrowseTarget.Place, Folder).Effects.Should().Equal(BrowseEffect.Select, BrowseEffect.ClosePreview, BrowseEffect.GoThere);
    }

    [Fact]
    public void Windows_opens_only_on_the_deliberate_open_gesture()
    {
        var states = new[] { BrowseState.Walking, BrowseState.Previewing(Dawn), BrowseState.Viewing(Dawn) };
        var gestures = Enum.GetValues<BrowseGesture>().Where(g => g is not (BrowseGesture.OpenWithWindows or BrowseGesture.DoubleClick));
        foreach (var s in states)
        foreach (var g in gestures)
        foreach (var t in Enum.GetValues<BrowseTarget>())
        {
            On(s, g, t, Dawn).Has(BrowseEffect.OpenWithWindows).Should().BeFalse($"{g} on {t} from {s}");
        }

        On(BrowseState.Previewing(Dawn), BrowseGesture.OpenWithWindows, BrowseTarget.Picture, Dawn).Effects.Should().Equal(BrowseEffect.OpenWithWindows);
        var inViewer = GalleryBrowse.InViewer(BrowseState.Viewing(Dawn), BrowseGesture.OpenWithWindows, Harbour);
        inViewer.Effects.Should().Equal(BrowseEffect.OpenWithWindows);
        inViewer.Next.Mode.Should().Be(BrowseMode.Viewer, "the viewer stays open behind Windows' app");
    }

    [Fact]
    public void E_toggles_the_preview_of_the_thing_in_focus()
    {
        On(BrowseState.Walking, BrowseGesture.Inspect, BrowseTarget.Picture, Dawn).Next.Should().Be(BrowseState.Previewing(Dawn));
        On(BrowseState.Previewing(Dawn), BrowseGesture.Inspect, BrowseTarget.Picture, Dawn).Next.Should().Be(BrowseState.Walking);
        On(BrowseState.Previewing(Dawn), BrowseGesture.Inspect, BrowseTarget.Picture, Harbour).Next.Should().Be(BrowseState.Previewing(Harbour));
        On(BrowseState.Walking, BrowseGesture.Inspect, BrowseTarget.Place, Folder).Effects.Should().BeEmpty("a pad's E is the world's own");
    }

    [Fact]
    public void Expand_views_the_previewed_picture_large_and_leaves_text_to_the_reader()
    {
        var r = On(BrowseState.Previewing(Dawn), BrowseGesture.Expand, BrowseTarget.Picture, Dawn);
        r.Next.Should().Be(BrowseState.Viewing(Dawn));
        r.Effects.Should().Equal(BrowseEffect.ClosePreview, BrowseEffect.OpenViewer);
        On(BrowseState.Previewing(Notes), BrowseGesture.Expand, BrowseTarget.File, Notes).Effects.Should().BeEmpty();
        On(BrowseState.Walking, BrowseGesture.Expand, BrowseTarget.Picture, Dawn).Effects.Should().BeEmpty("R walking is the README's (unchanged)");
    }

    [Fact]
    public void Stepping_selects_and_closing_comes_back_to_the_preview_on_the_last_picture()
    {
        var s = On(BrowseState.Previewing(Dawn), BrowseGesture.Expand, BrowseTarget.Picture, Dawn).Next;
        var stepped = GalleryBrowse.Stepped(s, Harbour);
        stepped.Next.Should().Be(BrowseState.Viewing(Harbour));
        stepped.Effects.Should().Equal(BrowseEffect.Select);
        foreach (var close in new[] { BrowseGesture.Close, BrowseGesture.Expand })
        {
            var back = GalleryBrowse.InViewer(stepped.Next, close, Harbour);
            back.Next.Should().Be(BrowseState.Previewing(Harbour));
            back.Effects.Should().Equal(BrowseEffect.CloseViewer, BrowseEffect.Select, BrowseEffect.ShowPreview);
        }

        // Clicks on the picture belong to the viewer (zoom, pan): nothing changes.
        GalleryBrowse.InViewer(stepped.Next, BrowseGesture.Click, Harbour).Effects.Should().BeEmpty();
        GalleryBrowse.Stepped(BrowseState.Walking, Harbour).Effects.Should().BeEmpty();
    }

    [Fact]
    public void Esc_closes_one_level_at_a_time_and_travel_closes_everything()
    {
        GalleryBrowse.InViewer(BrowseState.Viewing(Dawn), BrowseGesture.Close, Dawn).Next.Should().Be(BrowseState.Previewing(Dawn));
        On(BrowseState.Previewing(Dawn), BrowseGesture.Close, BrowseTarget.Picture, Dawn).Next.Should().Be(BrowseState.Walking);
        On(BrowseState.Walking, BrowseGesture.Close, BrowseTarget.Nothing, -1).Effects.Should().BeEmpty("Esc walking is the world's");
        var t = On(BrowseState.Viewing(Dawn), BrowseGesture.Travel, BrowseTarget.Nothing, -1);
        t.Next.Should().Be(BrowseState.Walking);
        t.Effects.Should().Equal(BrowseEffect.CloseViewer, BrowseEffect.ClosePreview);
        t.Has(BrowseEffect.ShowPreview).Should().BeFalse("no preview comes back after a trip");
    }

    [Fact]
    public void The_preview_never_shows_anything_but_the_selection()
    {
        // Every answer that leaves a preview open on something selects that thing (or it was already shown).
        var ids = new[] { Dawn, Harbour, Notes, Folder, -1 };
        var states = new List<BrowseState> { BrowseState.Walking };
        states.AddRange(ids.Where(i => i >= 0).Select(BrowseState.Previewing));
        foreach (var s in states)
        foreach (var g in Enum.GetValues<BrowseGesture>())
        foreach (var t in Enum.GetValues<BrowseTarget>())
        foreach (var id in ids)
        {
            var r = On(s, g, t, id);
            if (r.Next.Mode == BrowseMode.Preview && r.Next.Shown != s.Shown)
            {
                r.Effects.Should().ContainInOrder(BrowseEffect.Select, BrowseEffect.ShowPreview);
            }
        }
    }

    [Fact]
    public void Walking_along_a_gallery_moves_the_preview_to_the_picture_in_focus()
    {
        var w = new GalleryBrowse.Walk(Harbour, FocusIsPicture: true, OnGallery: true, ShownDistanceMetres: 2f, OpenedDistanceMetres: 1f, ReachMetres: 2.8f);
        var r = GalleryBrowse.OnWalk(BrowseState.Previewing(Dawn), w);
        r.Next.Should().Be(BrowseState.Previewing(Harbour));
        r.Effects.Should().Equal(BrowseEffect.Select, BrowseEffect.ShowPreview);
    }

    [Fact]
    public void Outside_a_gallery_walking_by_a_picture_reads_nothing_and_walking_off_closes()
    {
        var near = new GalleryBrowse.Walk(Harbour, true, OnGallery: false, ShownDistanceMetres: 2f, OpenedDistanceMetres: 1f, ReachMetres: 2.8f);
        GalleryBrowse.OnWalk(BrowseState.Previewing(Dawn), near).Effects.Should().BeEmpty("still within reach of the dawn: it stays");
        var off = near with { ShownDistanceMetres = 2.8f + GalleryBrowse.PreviewKeepSlackMetres + 0.1f };
        GalleryBrowse.OnWalk(BrowseState.Previewing(Dawn), off).Effects.Should().Equal(BrowseEffect.ClosePreview);
    }

    [Fact]
    public void Walking_up_to_a_picture_clicked_from_afar_keeps_its_preview()
    {
        // Clicked from 9 m; walking towards it (focus nothing yet) keeps it; backing away past where you clicked closes it.
        var towards = new GalleryBrowse.Walk(-1, false, true, ShownDistanceMetres: 6f, OpenedDistanceMetres: 9f, ReachMetres: 2.8f);
        GalleryBrowse.OnWalk(BrowseState.Previewing(Dawn), towards).Effects.Should().BeEmpty();
        GalleryBrowse.OnWalk(BrowseState.Previewing(Dawn), towards with { ShownDistanceMetres = 10f }).Effects.Should().Equal(BrowseEffect.ClosePreview);
        GalleryBrowse.OnWalk(BrowseState.Walking, towards).Effects.Should().BeEmpty();
    }

    [Fact]
    public void Steps_wrap_round_the_folder_and_Home_End_reach_its_ends()
    {
        GalleryBrowse.Step(0, -1, 5).Should().Be(4);
        GalleryBrowse.Step(4, +1, 5).Should().Be(0);
        GalleryBrowse.Step(2, int.MinValue, 5).Should().Be(0);
        GalleryBrowse.Step(2, int.MaxValue, 5).Should().Be(4);
        GalleryBrowse.Step(-1, +1, 5).Should().Be(1);
        GalleryBrowse.Step(0, +1, 0).Should().Be(-1);
        GalleryBrowse.Step(0, +1, 1).Should().Be(0);
    }

    private static readonly ScreenRect Stage = new(40, 84, 1520, 752);

    [Fact]
    public void The_viewer_steps_at_its_side_edges_and_goes_back_to_the_world_from_the_backdrop()
    {
        var picture = new ScreenRect(400, 84, 800, 752); // a tall picture fitted in the middle
        ViewerPointer.Zone(60, 400, Stage, picture, canStep: true).Should().Be(ViewerZone.PreviousEdge);
        ViewerPointer.Zone(10, 400, Stage, picture, canStep: true).Should().Be(ViewerZone.PreviousEdge, "the margin beside the ‹ still steps");
        ViewerPointer.Zone(1540, 400, Stage, picture, canStep: true).Should().Be(ViewerZone.NextEdge);
        ViewerPointer.Zone(300, 400, Stage, picture, canStep: true).Should().Be(ViewerZone.Backdrop);
        ViewerPointer.Zone(800, 400, Stage, picture, canStep: true).Should().Be(ViewerZone.Picture);
        ViewerPointer.Zone(800, 40, Stage, picture, canStep: true).Should().Be(ViewerZone.Chrome);
        ViewerPointer.Zone(800, 870, Stage, picture, canStep: true).Should().Be(ViewerZone.Chrome);
    }

    [Fact]
    public void With_one_picture_the_edges_are_backdrop_and_a_narrow_stage_keeps_its_middle_draggable()
    {
        var wide = new ScreenRect(40, 84, 1520, 752);
        ViewerPointer.Zone(60, 400, Stage, wide, canStep: false).Should().Be(ViewerZone.Picture);
        ViewerPointer.Zone(10, 400, Stage, wide, canStep: false).Should().Be(ViewerZone.Backdrop);
        var narrow = new ScreenRect(0, 0, 300, 300);
        ViewerPointer.Zone(51, 150, narrow, narrow, canStep: true).Should().Be(ViewerZone.Picture, "each edge is at most a sixth of a narrow stage");
        ViewerPointer.Zone(49, 150, narrow, narrow, canStep: true).Should().Be(ViewerZone.PreviousEdge);
    }

    [Fact]
    public void Ctrl_makes_the_arrows_step_sideways_and_nothing_changes_without_it()
    {
        ArrowSidestep.Resolve(0, -1, sidestepHeld: false).Should().Be((0f, -1f));
        ArrowSidestep.Resolve(1, 0, sidestepHeld: false).Should().Be((1f, 0f));
        ArrowSidestep.Resolve(0, -1, sidestepHeld: true).Should().Be((-1f, 0f));
        ArrowSidestep.Resolve(0, 1, sidestepHeld: true).Should().Be((1f, 0f));
        ArrowSidestep.Resolve(1, 1, sidestepHeld: true).Should().Be((1f, 0f), "an arrow and D the same way do not step faster");
        ArrowSidestep.Resolve(-1, 1, sidestepHeld: true).Should().Be((0f, 0f), "opposite ways cancel");
        ArrowSidestep.Resolve(0, 0, sidestepHeld: true).Should().Be((0f, 0f));
    }
}
