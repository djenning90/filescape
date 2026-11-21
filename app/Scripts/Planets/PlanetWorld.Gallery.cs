using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The picture gallery on a planet (PLANET-96, AD-78; PLANET-159..164, AD-94): the picture viewer's place in the world
/// and the one browsing rule every gesture on a file goes through (<see cref="GalleryBrowse"/>). A click or E on a
/// picture looks at it (its small preview); a double-click, R, the preview's "⤢ View large" or a click on the preview's
/// picture opens <see cref="PictureViewer"/> on it, stepping through the pictures of the folder you stand in; each step
/// selects that picture in the world (its easel shows it and the selection ring moves); closing brings back the small
/// preview on the last picture you looked at, where you stood. Windows opens a picture only on Enter or "Open with
/// Windows ↗". The preview always shows the selection: a click on another file moves it there, a click on empty ground
/// puts it away; in a gallery, walking on with a picture's preview open moves it to the picture in focus. A gallery
/// (<see cref="MediaWorld"/>, AD-86) also lets its easels load their pictures as you walk, nearest first
/// (<see cref="PlanetVisual.PumpPictures"/>).
/// Preserve: the viewer opens only from a deliberate gesture on a picture, never by itself; it is closed without a
/// preview when you travel (<see cref="CloseViewerForTravel"/>); this file carries out the rule's effects and decides
/// nothing of its own.
/// </summary>
public partial class PlanetWorld
{
    private PictureViewer _viewer = null!;

    /// <summary>The viewer sits on its own screen layer above the planet's overlay and the HUD's bars, so the picture has the
    /// whole window (the HUD's own layer is below this).</summary>
    private const int ViewerCanvasLayer = 6;

    /// <summary>Set while the viewer closes because of a trip: no preview comes back.</summary>
    private bool _viewerClosingForTravel;

    public PictureViewer Viewer => _viewer;

    public bool ViewerOpen => _viewer.IsOpen;

    /// <summary>The planet you stand on is a gallery (its canvases fill as you walk; PLANET-130).</summary>
    public bool OnGallery => _current != null && _s.IsLive && _current.Comp.Media.IsGallery;

    private void BuildPictureViewer()
    {
        var layer = new CanvasLayer { Layer = ViewerCanvasLayer };
        AddChild(layer);
        _viewer = new PictureViewer();
        layer.AddChild(_viewer);
        _s.Hud.AddBlocking(_viewer); // clicks and the wheel over it are the viewer's, never the camera's
        _viewer.Stepped += id => ApplyBrowse(GalleryBrowse.Stepped(BrowseState.Viewing(id), id), id, quiet: true); // the viewer has its own feel; no world sound per step
        // The canvas's thumbnail shows in the viewer's first frame, before Windows' larger one (PLANET-134).
        _viewer.CanvasPicture = id => _s.Tree is { } t && t.Contains(id)
            ? GalleryThumbnails.Cached(t, t[id], PlanetVisual.Budget.LargePixels) ?? GalleryThumbnails.Cached(t, t[id], PlanetVisual.Budget.SmallPixels)
            : null;
        _viewer.OpenRequested += id =>
        {
            // Enter or "Open with Windows ↗" in the viewer: the picture shown, handed to Windows; the viewer stays (AD-94).
            _s.Select(id);
            ApplyBrowse(GalleryBrowse.InViewer(BrowseNow, BrowseGesture.OpenWithWindows, id), id);
        };
        _viewer.Closed += id =>
        {
            ForgetPress(); // a click on the backdrop closed it: the next press on the world is not the second half of a double-click
            if (_viewerClosingForTravel || _s.Tree is not { } tree || !tree.Contains(id)) return;
            // The small preview comes back on the picture you looked at last (the rule's CloseViewer is already done).
            ApplyBrowse(GalleryBrowse.InViewer(BrowseState.Viewing(id), BrowseGesture.Close, id), id, skip: BrowseEffect.CloseViewer, quiet: true);
        };
    }

    /// <summary>The focus prompt's second line beside a picture in a gallery (AD-94): the sideways step, arrows included,
    /// so an arrow-key player finds it where it is needed (PLANET-166).</summary>
    private const string GallerySideCue = "   ·   {Ctrl} + {←} {→} or {A} {D} · step sideways along the easels";

    /// <summary>The same line while the preview is open: walking on moves the preview along (browsing by walking).</summary>
    private const string GalleryWalkCue = "   ·   walk on: the next easel shows here ({Ctrl} + {←} {→} or {A} {D} step sideways)";

    /// <summary>The arrival card's words on how to look in a gallery (after "a picture gallery of N pictures").</summary>
    private const string GalleryArrivalCue = "click or E on a picture to look, double-click or R to view it large";

    /// <summary>The arrival card's account of a gallery: how many pictures, and why when it is not plain from them.</summary>
    private static string GalleryWhy(MediaVerdict m) =>
        $" of {m.Pictures:N0} picture{(m.Pictures == 1 ? "" : "s")}" + m.Reason switch
        {
            MediaReason.PictureLibrary => " in your Pictures",
            MediaReason.InsideGallery => " inside a gallery",
            _ => "",
        };

    /// <summary>The viewer's keys, while it is open (Main gives it the keyboard).</summary>
    public bool ViewerKey(InputEventKey k) => _viewer.HandleKey(k);

    /// <summary>
    /// R, ⤢ "View large" or a click on the preview's picture: opens the viewer on the previewed picture, if the rule
    /// says so (<see cref="BrowseGesture.Expand"/>). False when the preview is not of a picture (R then means the README).
    /// </summary>
    public bool OpenViewer()
    {
        if (!_previewPanel.Visible || _s.Tree is not { } tree || !tree.Contains(_previewShownId)) return false;
        var id = _previewShownId;
        var step = GalleryBrowse.On(BrowseNow, BrowseGesture.Expand, BrowseTargetOf(id, sky: false), id);
        if (!step.Has(BrowseEffect.OpenViewer)) return false;
        ApplyBrowse(step, id);
        return true;
    }

    /// <summary>
    /// The viewer on <paramref name="id"/>, stepping through this folder's pictures (as the world shows them: hidden ones
    /// only when shown). False when it is not a picture on a live folder.
    /// </summary>
    private bool OpenViewerOn(int id)
    {
        if (!_s.IsLive || _s.Tree is not { } tree || _current == null || !tree.Contains(id)) return false;
        if (tree[id] is not { Kind: NodeKind.File } n || !GalleryRules.IsPicture(n.Name)) return false;
        var pictures = GalleryRules.Pictures(tree, n.ParentId, includeHidden: true).Where(p => _s.IsVisible(tree[p])).ToList();
        if (!pictures.Contains(id)) pictures = [id];
        _viewer.Open(tree, pictures, id);
        _audio.Inspect();
        GD.Print($"[planets] picture viewer: {n.Name} ({pictures.Count} pictures here)");
        return true;
    }

    // ------------------------------------------------------------------ the browsing rule's state and effects (AD-94)

    /// <summary>The file the small preview shows (−1 when it has never shown one); the selection while it is open.</summary>
    private int _previewShownId = -1;

    /// <summary>The file the small preview shows while it is open, else −1 (probes).</summary>
    public int PreviewShownId => _previewPanel.Visible ? _previewShownId : -1;

    /// <summary>How far the previewed file was when its preview opened, metres along the ground to its edge (∞ when it
    /// does not stand on this world): the rule keeps the preview while you walk towards it.</summary>
    private float _previewOpenedDistance = float.PositiveInfinity;

    /// <summary>Where browsing stands now, read from what is shown (the viewer, the preview, or neither).</summary>
    private BrowseState BrowseNow =>
        _viewer.IsOpen ? BrowseState.Viewing(_viewer.Current)
        : _previewPanel.Visible && _previewShownId >= 0 ? BrowseState.Previewing(_previewShownId)
        : BrowseState.Walking;

    /// <summary>What the rule calls a node: a picture (only on a live folder, where it can be shown), another file, a
    /// folder or link (<paramref name="sky"/>: a world in the sky), or nothing (−1).</summary>
    private BrowseTarget BrowseTargetOf(int id, bool sky)
    {
        if (id < 0 || _s.Tree is not { } tree || !tree.Contains(id)) return BrowseTarget.Nothing;
        var n = tree[id];
        if (sky || n.IsDirectory || n.Kind == NodeKind.Link) return BrowseTarget.Place;
        return _s.IsLive && GalleryRules.IsPicture(n.Name) ? BrowseTarget.Picture : BrowseTarget.File;
    }

    /// <summary>How far a standing file is, metres along the ground to its edge (the focus rule's measure), or ∞.</summary>
    private float GroundDistanceTo(int id)
    {
        if (_current?.Comp.Items.FirstOrDefault(i => i.NodeId == id) is not { } it) return float.PositiveInfinity;
        return PlanetShape.Angle(_walker.Up, it.Direction) * _current.Comp.Shape.Radius - 0.3f * it.Scale;
    }

    /// <summary>
    /// Carries out one answer of the rule on <paramref name="id"/>, in order (<paramref name="skip"/>: an effect already
    /// done by the caller). A look plays the inspect sound, a selection alone the select sound; <paramref name="quiet"/>
    /// (browsing by walking) plays neither. True when the gesture was used up (<see cref="BrowseEffect.Consume"/>).
    /// </summary>
    private bool ApplyBrowse(BrowseStep step, int id, BrowseEffect? skip = null, bool quiet = false)
    {
        var tree = _s.Tree;
        var looks = step.Has(BrowseEffect.ShowPreview) || step.Has(BrowseEffect.OpenViewer);
        foreach (var e in step.Effects)
        {
            if (e == skip) continue;
            switch (e)
            {
                case BrowseEffect.Select:
                    _s.Select(id);
                    if (!looks && !quiet) _audio.Select();
                    break;
                case BrowseEffect.ShowPreview when tree != null && tree.Contains(id):
                    ShowPreview(tree, tree[id]);
                    if (!quiet) _audio.Inspect();
                    break;
                case BrowseEffect.ClosePreview:
                    ClosePreview();
                    break;
                case BrowseEffect.OpenViewer:
                    OpenViewerOn(id);
                    break;
                case BrowseEffect.CloseViewer:
                    _viewer.Close();
                    break;
                case BrowseEffect.OpenWithWindows:
                    OpenFile(id); // the session's one Open path, with its confirmations
                    break;
                case BrowseEffect.GoThere:
                    LaunchToSelection();
                    break;
            }
        }

        return step.Has(BrowseEffect.Consume);
    }

    // ------------------------------------------------------------------ for probes (os-planets-probe.ps1 -Gallery)

    /// <summary>
    /// A picture standing here whose pick centre is on screen, clear of the edges and the HUD, and answers a click there
    /// (<paramref name="except"/> left out), nearest the screen's middle; null when none is. Viewport units (with
    /// <c>--ui-scale=1</c> the window's client pixels).
    /// </summary>
    public (int Id, Vector2 Centre)? PictureOnScreen(int except = -1)
    {
        if (_current == null || _s.Tree is not { } tree) return null;
        var size = GetViewport().GetVisibleRect().Size;
        (int, Vector2)? best = null;
        var bestD = float.MaxValue;
        foreach (var it in _current.Comp.Items)
        {
            if (it.NodeId < 0 || it.NodeId == except || !tree.Contains(it.NodeId) || !GalleryRules.IsPicture(tree[it.NodeId].Name)) continue;
            var c3 = Gd(_current.Comp.Surface.PointAt(it.Direction, 0.55f * it.Scale));
            if (_camera.IsPositionBehind(c3)) continue;
            var c = _camera.UnprojectPosition(c3);
            // Clear of the screen's edges and the HUD bars (the same margins as ClickableWorld).
            if (c.X < 60 || c.Y < 160 || c.X > size.X - 60 || c.Y > size.Y - 120 || OverHud(c) || Pick(c).Id != it.NodeId) continue;
            var d = c.DistanceTo(size / 2);
            if (d < bestD) { bestD = d; best = (it.NodeId, c); }
        }

        return best;
    }

    /// <summary>The gallery's part of the state file (os-planets-probe.ps1 -Gallery): what is shown where, and a picture to click.</summary>
    private string GalleryStateJson()
    {
        var (index, count) = _viewer.Place;
        var pic = _viewer.IsOpen ? null : PictureOnScreen(_previewShownId);
        var back = _viewer.IsOpen ? _viewer.BackdropPoint() : null;
        // The angle between where the camera looks and where the traveller moves, degrees on the ground (−1 standing):
        // ≈ 90 while sidestepping with the view kept, ≈ 0 once the camera has swung in behind (PLANET-165).
        var up = Gd(_walker.Up);
        var vel = Gd(_walker.Velocity) - up * Gd(_walker.Velocity).Dot(up);
        var look = _camFwd - up * _camFwd.Dot(up);
        var camToMotion = vel.Length() > 0.8f && look.LengthSquared() > 1e-6f ? Mathf.RadToDeg(look.Normalized().AngleTo(vel.Normalized())) : -1f;
        return System.FormattableString.Invariant(
            $"\"gallery\":{{\"onGallery\":{(OnGallery ? "true" : "false")},\"viewer\":{(_viewer.IsOpen ? "true" : "false")},\"viewerCurrent\":{_viewer.Current},\"viewerIndex\":{index},\"viewerCount\":{count},\"hover\":\"{_viewer.HoverZone}\",\"previewShown\":{PreviewShownId},\"follows\":{PreviewFollows},\"pictureOnScreen\":{(pic is { } p ? $"[{p.Id},{p.Centre.X:0},{p.Centre.Y:0}]" : "null")},\"backdrop\":{(back is { } b ? $"[{b.X:0},{b.Y:0}]" : "null")},\"camToMotion\":{camToMotion:0.#}}},");
    }

    /// <summary>Closes the viewer for a trip (no preview comes back).</summary>
    private void CloseViewerForTravel()
    {
        if (!_viewer.IsOpen) return;
        _viewerClosingForTravel = true;
        _viewer.Close();
        _viewerClosingForTravel = false;
    }
}
