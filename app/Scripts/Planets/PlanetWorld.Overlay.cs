using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// The planetary world's own screen layer: a title card that introduces each world on arrival (its name and what it
/// holds), one prompt line that says what the next key does, the preview panel (picture, text, sound), the planet's
/// list (every folder and file here, filterable), names in the sky (the shared sign layer), and the travel effects
/// (the trail you fly, the landing ring, the warp's light). Text is the last resort (research): the prompt is one line
/// and only appears when there is something to do.
/// </summary>
public partial class PlanetWorld
{
    private CanvasLayer _overlay = null!;
    private Label _title = null!, _titleDetail = null!, _prompt = null!, _promptDetail = null!, _hint = null!;
    private Control _titleBox = null!, _promptBox = null!;
    private float _titleTime, _hintTime;
    private PanelContainer _previewPanel = null!, _contentsPanel = null!;
    private Label _previewTitle = null!, _previewMeta = null!;
    private TextureRect _previewImage = null!;
    private RichTextLabel _previewText = null!;
    private LineEdit _contentsFilter = null!;
    private ItemList _contentsList = null!;
    private Label _contentsTitle = null!;
    private readonly List<int> _contentsIds = [];
    private FileCategory? _contentsCategory;
    private ColorRect _flash = null!;
    private float _warpFlash;
    private AudioStreamPlayer _previewPlayer = null!;

    public bool PreviewOpen => _previewPanel.Visible;
    public bool ListOpen => _contentsPanel.Visible;
    public string TitleText => _title.Text;
    public bool PreviewHasImage => _previewImage.Visible && _previewImage.Texture != null;
    public bool PreviewHasText => _previewText.Visible && _previewText.Text.Length > 0;
    public int ListCount => _contentsIds.Count;

    private void BuildOverlay()
    {
        _overlay = new CanvasLayer { Layer = 1, Visible = false };
        AddChild(_overlay);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(root);

        _flash = new ColorRect { Color = new Color(1f, 0.95f, 0.85f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_flash);

        // Title card: top centre, below the toolbar and the place bar.
        var tb = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        tb.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        tb.OffsetLeft = -420; tb.OffsetRight = 420; tb.OffsetTop = 150; tb.OffsetBottom = 240;
        _title = MakeLabel(34, new Color(1f, 0.95f, 0.82f), HorizontalAlignment.Center, outline: 10);
        _titleDetail = MakeLabel(16, new Color(0.85f, 0.88f, 1f), HorizontalAlignment.Center, outline: 6);
        tb.AddChild(_title);
        tb.AddChild(_titleDetail);
        root.AddChild(tb);
        _titleBox = tb;

        // The prompt: bottom centre, one line plus a quiet second line.
        var pb = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        pb.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        pb.OffsetLeft = -460; pb.OffsetRight = 460; pb.OffsetTop = -150; pb.OffsetBottom = -64;
        _prompt = MakeLabel(21, new Color(1f, 0.93f, 0.7f), HorizontalAlignment.Center, outline: 8);
        _promptDetail = MakeLabel(14, new Color(0.82f, 0.86f, 0.98f), HorizontalAlignment.Center, outline: 5);
        _hint = MakeLabel(15, new Color(0.8f, 0.95f, 1f), HorizontalAlignment.Center, outline: 5);
        pb.AddChild(_hint);
        pb.AddChild(_prompt);
        pb.AddChild(_promptDetail);
        root.AddChild(pb);
        _promptBox = pb;

        BuildPreviewPanel(root);
        BuildContentsPanel(root);
        _previewPlayer = new AudioStreamPlayer { VolumeDb = -4 };
        AddChild(_previewPlayer);
    }

    private static Label MakeLabel(int size, Color color, HorizontalAlignment align, int outline)
    {
        var l = new Label { HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.Off, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeConstantOverride("outline_size", outline);
        l.AddThemeColorOverride("font_outline_color", new Color(0.04f, 0.03f, 0.1f, 0.9f));
        return l;
    }

    private static StyleBoxFlat PanelStyle()
    {
        var s = new StyleBoxFlat { BgColor = new Color(0.07f, 0.06f, 0.14f, 0.92f), BorderColor = new Color(0.95f, 0.8f, 0.5f, 0.55f), ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10 };
        s.SetBorderWidthAll(1);
        s.SetCornerRadiusAll(12);
        return s;
    }

    private void BuildPreviewPanel(Control root)
    {
        _previewPanel = new PanelContainer { Visible = false };
        _previewPanel.AddThemeStyleboxOverride("panel", PanelStyle());
        _previewPanel.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        _previewPanel.OffsetLeft = 12; _previewPanel.OffsetRight = 392; _previewPanel.OffsetTop = 150; _previewPanel.OffsetBottom = -180;
        root.AddChild(_previewPanel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        _previewPanel.AddChild(v);
        _previewTitle = MakeLabel(18, new Color(1f, 0.93f, 0.75f), HorizontalAlignment.Left, outline: 0);
        _previewMeta = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _previewMeta.AddThemeFontSizeOverride("font_size", 13);
        _previewMeta.AddThemeColorOverride("font_color", new Color(0.78f, 0.82f, 0.95f));
        _previewImage = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(340, 240), Visible = false };
        _previewText = new RichTextLabel { BbcodeEnabled = false, ScrollActive = true, SelectionEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, Visible = false, CustomMinimumSize = new Vector2(340, 200) };
        _previewText.AddThemeFontSizeOverride("normal_font_size", 13);
        var close = new Button { Text = "Close (E / Esc)", FocusMode = Control.FocusModeEnum.None };
        close.Pressed += ClosePreview;
        var open = new Button { Text = "Open with Windows (Enter)", FocusMode = Control.FocusModeEnum.None };
        open.Pressed += () => _s.OpenSelection();
        var row = new HBoxContainer();
        row.AddChild(open);
        row.AddChild(close);
        v.AddChild(_previewTitle);
        v.AddChild(_previewMeta);
        v.AddChild(_previewImage);
        v.AddChild(_previewText);
        v.AddChild(row);
    }

    private void BuildContentsPanel(Control root)
    {
        _contentsPanel = new PanelContainer { Visible = false };
        _contentsPanel.AddThemeStyleboxOverride("panel", PanelStyle());
        _contentsPanel.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        _contentsPanel.OffsetLeft = 12; _contentsPanel.OffsetRight = 452; _contentsPanel.OffsetTop = 150; _contentsPanel.OffsetBottom = -70;
        root.AddChild(_contentsPanel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        _contentsPanel.AddChild(v);
        _contentsTitle = MakeLabel(17, new Color(1f, 0.93f, 0.75f), HorizontalAlignment.Left, outline: 0);
        _contentsFilter = new LineEdit { PlaceholderText = "Type to filter by name…", ClearButtonEnabled = true };
        _contentsFilter.TextChanged += _ => FillContents();
        _contentsFilter.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) { _contentsPanel.Visible = false; _contentsFilter.ReleaseFocus(); _contentsFilter.AcceptEvent(); }
            if (e is InputEventKey { Pressed: true, Keycode: Key.Down } && _contentsList.ItemCount > 0) { _contentsList.GrabFocus(); _contentsList.Select(0); _contentsFilter.AcceptEvent(); }
        };
        _contentsList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Single, FixedIconSize = new Vector2I(18, 18) };
        _contentsList.ItemSelected += i => { if (i >= 0 && i < _contentsIds.Count) { _s.Select(_contentsIds[(int)i]); _audio.Select(); } };
        _contentsList.ItemActivated += i =>
        {
            if (i < 0 || i >= _contentsIds.Count) return;
            var id = _contentsIds[(int)i];
            _contentsPanel.Visible = false;
            _contentsList.ReleaseFocus();
            Activate(id, sky: false);
        };
        var hint = new Label { Text = "Click: select (the card shows it) · double-click or Enter: go there (a world) or stand by it (a file) · Esc closes", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.76f, 0.9f));
        v.AddChild(_contentsTitle);
        v.AddChild(_contentsFilter);
        v.AddChild(_contentsList);
        v.AddChild(hint);
    }

    private bool OverOverlay(Vector2 p) =>
        (_previewPanel.Visible && _previewPanel.GetGlobalRect().HasPoint(p)) || (_contentsPanel.Visible && _contentsPanel.GetGlobalRect().HasPoint(p));

    // ------------------------------------------------------------------ title card, prompt, hint

    /// <summary>The destination introduced on arrival: its name, its kind of world and what it holds (fades after ≈ 3 s).</summary>
    private void ShowTitleCard(FsTree tree, int folderId, string? lead)
    {
        if (!tree.Contains(folderId) || _current == null) return;
        _title.Text = tree.DisplayName(folderId);
        var c = _current.Comp;
        var world = PlanetText.WorldName(c.Theme);
        var parts = new List<string> { world, _s.Summary(folderId) };
        if (c.OmittedFiles > 0) parts.Add($"{c.ShownFiles:N0} files stand here, {c.OmittedFiles:N0} more in piles (J lists them all)");
        if (c.State is ListingState.Partial) parts.Add("partly listed");
        if (Listing.IsUnknown(c.State)) parts.Add(Listing.Describe(c.State));
        _titleDetail.Text = (lead != null ? lead + " · " : "") + string.Join("  ·  ", parts.Where(p => p.Length > 0));
        _titleTime = 3.6f;
    }

    private string _promptKey = "";

    private void SetPrompt(string text, string detail = "")
    {
        if (_promptKey == text + detail) return;
        _promptKey = text + detail;
        _prompt.Text = text;
        _promptDetail.Text = detail;
    }

    private void ShowHint(string text)
    {
        _hint.Text = text;
        _hintTime = 5f;
    }

    private void UpdateOverlay(float dt)
    {
        _titleTime = MathF.Max(0, _titleTime - dt);
        _titleBox.Modulate = new Color(1, 1, 1, Mathf.Clamp(_titleTime / 0.8f, 0, 1));
        _hintTime = MathF.Max(0, _hintTime - dt);
        _hint.Modulate = new Color(1, 1, 1, Mathf.Clamp(_hintTime / 0.8f, 0, 1));
        _promptBox.Visible = !Busy;
        _flash.Color = new Color(1f, 0.95f, 0.86f, Mathf.Clamp(_warpFlash * _warpFlash * 0.85f, 0, 0.85f));
        if (Busy && _transfer?.Change is { } ch && _s.Tree is { } tree && tree.Contains(ch.To))
        {
            _title.Text = tree.DisplayName(ch.To);
            _titleDetail.Text = ch.Move switch { PlaceMove.Up => "up to", PlaceMove.Back => "back to", PlaceMove.Link => "through the link", _ => "flying to" };
            _titleTime = MathF.Max(_titleTime, 1.2f);
        }
    }

    // ------------------------------------------------------------------ preview

    private const long MaxImageBytes = 24L * 1024 * 1024, MaxTextBytes = 4L * 1024 * 1024, MaxAudioBytes = 16L * 1024 * 1024;

    private void ShowPreview(FsTree tree, FsNode n)
    {
        _contentsPanel.Visible = false;
        _previewTitle.Text = n.Name;
        var cat = FileCategories.Of(n.Name);
        var modified = n.ModifiedUtc is { } m ? m.ToLocalTime().ToString("d MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "unknown";
        _previewMeta.Text = $"{PlaceText.CategoryName(cat)} · {SizeFormat.Short(n.Size)} ({SizeFormat.Exact(n.Size)}) · modified {modified}\n{tree.FullPath(n.Id)}";
        _previewImage.Visible = false;
        _previewImage.Texture = null;
        _previewText.Visible = false;
        _previewText.Text = "";
        _previewPlayer.Stop();
        _previewPanel.Visible = true;
        if (!_s.IsLive || n.Kind != NodeKind.File) return; // sample data has no contents; links and folders are not previewed
        var path = tree.FullPath(n.Id);
        var ext = System.IO.Path.GetExtension(n.Name).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" && n.Size <= MaxImageBytes)
        {
            _previewImage.Visible = true;
            var id = n.Id;
            _ = Thumbnails.LoadAsync(path, 512).ContinueWith(t => { if (t.Result is { } img) CallDeferred(MethodName.ApplyPreviewImage, img, id); });
        }
        else if (ext is ".wav" or ".ogg" or ".mp3" && n.Size <= MaxAudioBytes)
        {
            PlayPreview(path, ext);
        }
        else if (PlanetText.LooksLikeText(n.Name) && n.Size <= MaxTextBytes)
        {
            _previewText.Visible = true;
            _previewText.Text = PlanetText.ReadPreview(path, 6000);
        }
    }

    private void ApplyPreviewImage(Image img, int id)
    {
        if (!_previewPanel.Visible || _s.Selection.Selected != id) return;
        _previewImage.Texture = ImageTexture.CreateFromImage(img);
    }

    /// <summary>A few seconds of a sound file, the music ducked underneath (read-only; nothing is written).</summary>
    private void PlayPreview(string path, string ext)
    {
        try
        {
            AudioStream? stream = ext switch
            {
                ".wav" => AudioStreamWav.LoadFromFile(path),
                ".ogg" => AudioStreamOggVorbis.LoadFromFile(path),
                ".mp3" => new AudioStreamMP3 { Data = System.IO.File.ReadAllBytes(path) },
                _ => null,
            };
            if (stream == null) return;
            _previewPlayer.Stream = stream;
            _previewPlayer.Play();
            _audio.Duck(true);
            _previewText.Visible = true;
            _previewText.Text = "♪ playing a preview…  (E or Esc stops it)";
        }
        catch (Exception e)
        {
            _previewText.Visible = true;
            _previewText.Text = "This sound could not be played here: " + e.Message;
        }
    }

    private void ClosePreview()
    {
        if (!_previewPanel.Visible) return;
        _previewPanel.Visible = false;
        _previewPlayer.Stop();
        _audio.Duck(false);
    }

    // ------------------------------------------------------------------ the planet's list (J, a pile, the telescope)

    public void ToggleContents()
    {
        if (_contentsPanel.Visible) _contentsPanel.Visible = false;
        else OpenContents(null);
    }

    private void OpenContents(FileCategory? category)
    {
        _contentsCategory = category;
        _contentsFilter.Text = "";
        ClosePreview();
        _contentsPanel.Visible = true;
        FillContents();
        _contentsFilter.GrabFocus();
    }

    private void FillContents()
    {
        var tree = _s.Tree;
        if (tree == null || _current == null) return;
        var folder = tree[_current.FolderId];
        var filter = _contentsFilter.Text.Trim();
        _contentsIds.Clear();
        _contentsList.Clear();
        var all = folder.Children.Select(c => tree[c]).Where(_s.IsVisible).ToList();
        var rows = all
            .Where(n => _contentsCategory == null || (n.Kind == NodeKind.File && FileCategories.Of(n.Name) == _contentsCategory))
            .Where(n => filter.Length == 0 || n.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.IsDirectory || n.Kind == NodeKind.Link ? 0 : 1).ThenBy(n => n.Name, NaturalNameOrder.Instance)
            .Take(5000)
            .ToList();
        foreach (var n in rows)
        {
            var detail = n.IsDirectory ? _s.Summary(n.Id) : n.Kind == NodeKind.Link ? PlaceText.LinkShort(tree, n) : SizeFormat.Short(n.Size);
            var i = _contentsList.AddItem($"{n.Name}    {detail}", FileIcons.Get(FileIcons.For(n)));
            _contentsList.SetItemTooltip(i, tree.FullPath(n.Id));
            _contentsIds.Add(n.Id);
        }

        var what = _contentsCategory is { } c ? PlaceText.CategoryName(c).ToLowerInvariant() : "worlds and files";
        _contentsTitle.Text = $"{tree.DisplayName(_current.FolderId)} · {rows.Count:N0} {what}" + (rows.Count >= 5000 ? " (first 5,000: type to filter)" : "") + (folder.IsListingIncomplete ? " · partly listed" : "");
    }

    // ------------------------------------------------------------------ names in the sky (the shared sign layer)

    private TimeSpan _lastSigns;
    private readonly List<SignCandidate> _signs = [];

    private void UpdateSigns(FsTree tree)
    {
        if (_current == null) return;
        if (Busy)
        {
            _s.Signs.Clear();
            return;
        }

        if (_s.Now - _lastSigns < TimeSpan.FromSeconds(0.05)) return;
        _lastSigns = _s.Now;
        _signs.Clear();
        var camPos = _camera.GlobalPosition;
        var camFwd = -_camera.GlobalBasis.Z;
        void AddWorld(Body b, bool isParent)
        {
            if (!tree.Contains(b.FolderId)) return;
            var scale = b.Node.Transform.Basis.Scale.X;
            var centre = b.Node.GlobalPosition;
            var top = centre + _camUp * (b.Comp.Shape.Radius * scale * 1.08f);
            var to = (centre - camPos).Normalized();
            var focus = to.Dot(camFwd) > 0.97f || OnOverlook || _s.Selection.Selected == b.FolderId;
            var detail = isParent ? "the way back" : FolderSummary.Gate(tree[b.FolderId], Listing.Of(tree[b.FolderId]));
            var color = PlanetArt.Palette(b.Comp.Theme).Accent.Lightened(0.35f);
            _signs.Add(new SignCandidate(b.FolderId, top, tree.DisplayName(b.FolderId), detail, color, isParent ? IconKind.Up : FileIcons.For(tree[b.FolderId]), focus, focus ? 2f : 0f));
        }

        foreach (var b in _children.Values) AddWorld(b, false);
        if (_parent != null) AddWorld(_parent, true);
        if (OnOverlook || _overlookRequested)
        {
            foreach (var g in _current.Comp.Groves)
            {
                _signs.Add(new SignCandidate(-1000 - _signs.Count, Gd(_current.Comp.Surface.PointAt(g.CentreDirection, 2.6f)), g.Label, "", PlanetArt.CategoryColor(g.Category), FileIcons.For(g.Category), false));
            }
        }

        if (_focusItem is { NodeId: >= 0 } fi && tree.Contains(fi.NodeId))
        {
            var n = tree[fi.NodeId];
            _signs.Add(new SignCandidate(n.Id, Gd(_current.Comp.Surface.PointAt(fi.Direction, 1.25f * fi.Scale + 0.3f)), n.Name, SizeFormat.Short(n.Size), PlanetArt.CategoryColor(fi.Category), FileIcons.For(n), true, 3f));
        }

        var blocked = _s.Hud.BlockingRects.ToList();
        if (_previewPanel.Visible) blocked.Add(_previewPanel.GetGlobalRect());
        if (_contentsPanel.Visible) blocked.Add(_contentsPanel.GetGlobalRect());
        blocked.Add(_promptBox.GetGlobalRect());
        _s.Signs.Layout(_camera, _signs, blocked);
    }

    // ------------------------------------------------------------------ thumbnails on the picture easels

    private void UpdateThumbnails()
    {
        if (_current?.Visual is not { } v || !_s.IsLive || _s.Tree is not { } tree) return;
        v.PumpThumbnails(tree, maxConcurrent: 2);
    }

    // ------------------------------------------------------------------ travel effects

    private FlightTrail _flightTrail = null!;
    private LandingBurst _landFx = null!;

    private void BuildTravelFx()
    {
        _flightTrail = new FlightTrail();
        AddChild(_flightTrail);
        _landFx = new LandingBurst();
        AddChild(_landFx);
    }

    private void AnimateScene(float dt)
    {
        _current?.Visual?.Animate(dt, Nv(_camera.GlobalPosition), _s.Selection.Selected, FocusNodeId);
        foreach (var c in _children.Values) c.Visual?.Animate(dt, Nv(_camera.GlobalPosition), -1, -1);
        _parent?.Visual?.Animate(dt, Nv(_camera.GlobalPosition), -1, -1);
        _flightTrail.Tick(dt);
        _landFx.Tick(dt);
    }
}
