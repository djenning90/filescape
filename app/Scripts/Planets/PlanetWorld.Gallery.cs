using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The picture gallery prototype on a planet (PLANET-96, AD-78): the picture viewer's place in the world. R (or the
/// preview's ⤢) on a picture's preview opens <see cref="PictureViewer"/> on it, stepping through the pictures of the
/// folder you stand in; each step selects that picture in the world (its easel shows it and the selection ring moves),
/// Enter opens it with Windows, and closing brings back the small preview on the last picture you looked at, where you
/// stood. A folder marked as a gallery (<see cref="GalleryRules.IsGallery"/>) also lets its easels load their pictures as
/// you walk, nearest first (<see cref="PlanetVisual.PumpPictures"/>).
/// Preserve: the viewer opens only from a deliberate R or click on a picture's preview, never by itself; it is closed
/// without a preview when you travel (<see cref="CloseViewerForTravel"/>).
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
        _viewer.Stepped += id => _s.Selection.Select(id);
        // The canvas's thumbnail shows in the viewer's first frame, before Windows' larger one (PLANET-134).
        _viewer.CanvasPicture = id => _s.Tree is { } t && t.Contains(id)
            ? GalleryThumbnails.Cached(t, t[id], PlanetVisual.Budget.LargePixels) ?? GalleryThumbnails.Cached(t, t[id], PlanetVisual.Budget.SmallPixels)
            : null;
        _viewer.OpenRequested += id =>
        {
            _s.Selection.Select(id);
            _s.OpenSelection();
        };
        _viewer.Closed += id =>
        {
            if (_viewerClosingForTravel || _s.Tree is not { } tree || !tree.Contains(id)) return;
            _s.Selection.Select(id);
            ShowPreview(tree, tree[id]); // the small preview comes back on the picture you looked at last
        };
    }

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
    /// R or ⤢ on a picture's preview: opens the viewer on it, stepping through this folder's pictures (as the world shows
    /// them: hidden ones only when shown). False when the preview is not of a picture.
    /// </summary>
    public bool OpenViewer()
    {
        if (!_previewPanel.Visible || !_s.IsLive || _s.Tree is not { } tree || _current == null) return false;
        var id = _s.Selection.Selected;
        if (!tree.Contains(id) || tree[id] is not { Kind: NodeKind.File } n || !GalleryRules.IsPicture(n.Name)) return false;
        var pictures = GalleryRules.Pictures(tree, n.ParentId, includeHidden: true).Where(p => _s.IsVisible(tree[p])).ToList();
        if (!pictures.Contains(id)) pictures = [id];
        ClosePreview();
        _viewer.Open(tree, pictures, id);
        _audio.Inspect();
        GD.Print($"[planets] picture viewer: {n.Name} ({pictures.Count} pictures here)");
        return true;
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
