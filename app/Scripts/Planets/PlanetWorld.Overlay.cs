using FileScape.App.Places;
using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Shell;
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
/// <para>
/// Layout offsets here are viewport units (docs/units.md), anchored to the screen's edges so they hold at any window
/// size; the HUD (toolbar, place bar, selection card) is Main's and is laid out elsewhere, so these leave room for it.
/// Worker-thread results (previews) come back through <see cref="Deliver"/> and run on the main thread in <see cref="Tick"/>.
/// </para>
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
    /// <summary>The full-screen warp light (its colour and cap are <see cref="FlashColor"/> and <see cref="FlashPeakOpacity"/>).</summary>
    private ColorRect _flash = null!;

    /// <summary>The flash's level, 0..1 (its opacity is the square of it times the peak).</summary>
    private float _warpFlash;
    private AudioStreamPlayer _previewPlayer = null!;

    public bool PreviewOpen => _previewPanel.Visible;
    public string TitleText => _title.Text;
    public bool PreviewHasImage => _previewImage.Visible && _previewImage.Texture != null;
    public bool PreviewHasText => _previewText.Visible && _previewText.Text.Length > 0;

    // ---- how long the screen texts stay, seconds (by eye; rationale not recorded)
    /// <summary>The arrival title card: fully shown for 2.8 s, then fading over <see cref="TitleFadeSeconds"/> (gone at 3.6 s).</summary>
    private const float TitleCardSeconds = 3.6f, TitleFadeSeconds = 0.8f;

    /// <summary>A hint (the overlook's, the bench's): 5 s, the last <see cref="HintFadeSeconds"/> fading.</summary>
    private const float HintSeconds = 5f, HintFadeSeconds = 0.8f;

    /// <summary>While travelling the title shows the destination for at least this long, seconds (kept topped up each frame).</summary>
    private const float TravelTitleSeconds = 1.2f;

    /// <summary>A flash left over while walking (none should be) fades at this rate, level per second (gone in ≤ 0.5 s).</summary>
    private const float StrayFlashFadePerSecond = 2f;

    /// <summary>Builds the screen layer once (<see cref="_Ready"/>): everything starts hidden and is shown as needed.</summary>
    private void BuildOverlay()
    {
        _overlay = new CanvasLayer { Layer = 1, Visible = false };
        AddChild(_overlay);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(root);

        // Transparent to start; its colour is set every frame (UpdateOverlay, UpdateUniverse) to FlashColor at the flash's opacity.
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
        BuildContentsPanel(root); // PlanetWorld.Destinations
        BuildTripPanel(root); // PlanetWorld.Trip
        BuildBenchPanel(root);
        BuildRootMarkPanel(root); // PLANET-45 (PlanetWorld.RootMark.cs)
        _previewPlayer = new AudioStreamPlayer { VolumeDb = -4 };
        AddChild(_previewPlayer);
    }

    /// <summary>A one-line label for the screen layer: <paramref name="size"/> in font pixels, a dark outline of
    /// <paramref name="outline"/> pixels so it reads over any sky, trimmed with an ellipsis rather than wrapped.</summary>
    private static Label MakeLabel(int size, Color color, HorizontalAlignment align, int outline)
    {
        var l = new Label { HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.Off, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeConstantOverride("outline_size", outline);
        l.AddThemeColorOverride("font_outline_color", new Color(0.04f, 0.03f, 0.1f, 0.9f));
        return l;
    }

    /// <summary>The planets' panel look: deep blue-violet, nearly opaque, a thin warm-gold border, rounded corners.</summary>
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
        // The title row: the name, and in the upper-right corner the Expand icon (PLANET-52; PlanetWorld.Reading).
        var titleRow = new HBoxContainer();
        _previewTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _previewTitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        titleRow.AddChild(_previewTitle);
        titleRow.AddChild(MakeExpandButton());
        _previewText.MetaClicked += meta => OnPreviewLink(meta.AsString()); // PlanetWorld.Reading: links in the excerpt
        _previewText.MetaUnderlined = false;
        v.AddChild(titleRow);
        v.AddChild(_previewMeta);
        v.AddChild(_previewImage);
        v.AddChild(_previewText);
        v.AddChild(row);
    }

    /// <summary>The point is over one of the planet's own open panels (preview, list, bench).</summary>
    private bool OverOverlay(Vector2 p) =>
        (_previewPanel.Visible && _previewPanel.GetGlobalRect().HasPoint(p)) || (_contentsPanel.Visible && _contentsPanel.GetGlobalRect().HasPoint(p)) || (_benchPanel.Visible && _benchPanel.GetGlobalRect().HasPoint(p))
        || (_rootPanel.Visible && _rootPanel.GetGlobalRect().HasPoint(p));

    // ------------------------------------------------------------------ title card, prompt, hint

    /// <summary>The destination introduced on arrival: its name, its kind of world and what it holds (fades after ≈ 3 s).</summary>
    private void ShowTitleCard(FsTree tree, int folderId, string? lead)
    {
        if (!tree.Contains(folderId) || _current == null) return;
        _title.Text = tree.DisplayName(folderId);
        var c = _current.Comp;
        var world = PlanetText.WorldName(c.Theme);
        var parts = new List<string> { world, _s.Summary(folderId) };
        var piled = c.Items.Where(i => i.Kind == PlanetItemKind.Pile).Sum(i => i.HiddenCount);
        if (piled > 0) parts.Add($"{c.Items.Count(i => i.Kind != PlanetItemKind.Pile):N0} stand here, {piled:N0} more in piles (J lists them all)");
        if (c.State is ListingState.Partial) parts.Add("partly listed");
        if (Listing.IsUnknown(c.State)) parts.Add(Listing.Describe(c.State));
        _titleDetail.Text = (lead != null ? lead + " · " : "") + string.Join("  ·  ", parts.Where(p => p.Length > 0));
        _titleTime = TitleCardSeconds;
    }

    private string _promptKey = "";

    /// <summary>Sets the prompt line and its quiet second line; unchanged text is not re-set (it is called every frame).</summary>
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
        _hintTime = HintSeconds;
    }

    /// <summary>
    /// The arrival title counts down and fades. Also called in the overview (whose frames skip <see cref="UpdateOverlay"/>):
    /// opening it shortens the title to 0.3 s, but without this the title stayed frozen over the map (seen in the
    /// walkthrough's zoom step, 2026-11-17).
    /// </summary>
    private void FadeTitle(float dt)
    {
        _titleTime = MathF.Max(0, _titleTime - dt);
        _titleBox.Modulate = new Color(1, 1, 1, Mathf.Clamp(_titleTime / TitleFadeSeconds, 0, 1));
    }

    /// <summary>Per frame on a planet: fades the title and hint, hides the prompt while travelling, draws the flash, and
    /// during travel titles the destination with how you are going there.</summary>
    private void UpdateOverlay(float dt)
    {
        FadeTitle(dt);
        _hintTime = MathF.Max(0, _hintTime - dt);
        _hint.Modulate = new Color(1, 1, 1, Mathf.Clamp(_hintTime / HintFadeSeconds, 0, 1));
        _promptBox.Visible = !Busy;
        if (_phase == Phase.Walking && _transfer == null) _warpFlash = Mathf.MoveToward(_warpFlash, 0, dt * StrayFlashFadePerSecond); // a stray veil fades
        // The same flash curve as the overview's (squared level, capped; see FlashPeakOpacity).
        _flash.Color = FlashColor with { A = Mathf.Clamp(_warpFlash * _warpFlash * FlashPeakOpacity, 0, FlashPeakOpacity) };
        if (Busy && _transfer?.Change is { } ch && _s.Tree is { } tree && tree.Contains(ch.To))
        {
            _title.Text = tree.DisplayName(ch.To);
            _titleDetail.Text = ch.Move switch { PlaceMove.Up => "up to", PlaceMove.Back => "back to", PlaceMove.Link => "through the link", _ => "flying to" };
            _titleTime = MathF.Max(_titleTime, TravelTitleSeconds);
        }
    }

    // ------------------------------------------------------------------ preview

    /// <summary>The largest files previewed, bytes: 24 MiB for a picture, 4 MiB for text, 12 MiB for a sound (bigger ones
    /// say there is no preview; Enter still opens them). Keeps a preview's read and decode bounded (rationale for the
    /// exact sizes not recorded).</summary>
    private const long MaxImageBytes = 24L * 1024 * 1024, MaxTextBytes = 4L * 1024 * 1024, MaxAudioBytes = 12L * 1024 * 1024;

    /// <summary>A picture preview is decoded to at most this many pixels on its longer side.</summary>
    private const int PreviewImagePixels = 512;

    /// <summary>A text preview reads at most this many bytes from the start of the file.</summary>
    private const int PreviewTextBytes = 6000;

    /// <summary>
    /// Shows a file's card in the preview panel and starts its preview, if it has one, on a worker. A file's contents
    /// are read only here, on a deliberate E (FILE-02, DEV-02); never for sample data, folders, links, cloud placeholders
    /// or reparse points. A result that comes back after you have moved on is dropped (it checks the selection).
    /// </summary>
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
        PreviewPlain("");
        _previewPlayer.Stop();
        _previewPanel.Visible = true;
        if (!_s.IsLive || n.Kind != NodeKind.File) return; // sample data has no contents; links and folders are not previewed
        if ((n.Flags & (NodeFlags.CloudPlaceholder | NodeFlags.ReparsePoint)) != 0)
        {
            // Reading a cloud placeholder can make its provider download it: never for a preview (Open asks first).
            _previewText.Visible = true;
            PreviewPlain((n.Flags & NodeFlags.CloudPlaceholder) != 0
                ? "A cloud file that may not be stored on this PC: FileScape does not read it for a preview, so nothing is downloaded. Open (Enter) asks first."
                : "A reparse point (a link or a special file): FileScape does not read through it for a preview.");
            return;
        }

        var path = tree.FullPath(n.Id);
        var ext = System.IO.Path.GetExtension(n.Name).ToLowerInvariant();
        var id = n.Id;
        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" && n.Size <= MaxImageBytes)
        {
            _previewImage.Visible = true;
            _ = Thumbnails.LoadAsync(path, PreviewImagePixels).ContinueWith(t => Deliver(() =>
            {
                if (t.Result is { } img) ApplyPreviewImage(img, id);
                else PreviewSays(id, "This picture could not be shown here (Enter opens it with Windows).");
            }));
        }
        else if (ext is ".wav" or ".ogg" or ".mp3" && n.Size <= MaxAudioBytes)
        {
            _previewText.Visible = true;
            PreviewPlain("♪ loading a preview…");
            _ = Task.Run(() => Thumbnails.ReadBounded(path, MaxAudioBytes)).ContinueWith(t => Deliver(() => PlayPreview(id, t.Result, ext)));
        }
        else if (PlanetText.LooksLikeText(n.Name) && n.Size <= MaxTextBytes)
        {
            _previewText.Visible = true;
            PreviewPlain("…");
            // Markdown is rendered, other text shown plain (PlanetWorld.Reading, PLANET-44); a failed read says why, plainly.
            var name = n.Name;
            _ = Task.Run(() => Readme.Read(path, PreviewTextBytes)).ContinueWith(t => Deliver(() =>
            {
                var r = t.Result;
                if (r.Error != null) PreviewSays(id, r.Error);
                else PreviewText(id, name, r.Text, excerpt: r.Truncated);
            }));
        }
        else
        {
            _previewText.Visible = true;
            PreviewPlain("No preview for this kind of file here. Enter opens it with Windows.");
        }
    }

    // Results of reads made on worker threads come back to the main thread here (and only while this world exists).
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _deliveries = new();

    private void Deliver(Action a) => _deliveries.Enqueue(a);

    private void RunDeliveries()
    {
        while (_deliveries.TryDequeue(out var a)) a();
    }

    /// <summary>Shows a line in the preview's text area, if the preview is still open on <paramref name="id"/>.</summary>
    private void PreviewSays(int id, string text)
    {
        if (!_previewPanel.Visible || _s.Selection.Selected != id) return;
        _previewText.Visible = true;
        PreviewPlain(text);
    }

    /// <summary>A decoded picture: shown on its easel in the world, and in the panel if it is still open on it.</summary>
    private void ApplyPreviewImage(Image img, int id)
    {
        _previewImage.Visible = true;
        _current?.Visual?.ShowThumbnail(id, img); // the easel shows the picture you looked at
        if (!_previewPanel.Visible || _s.Selection.Selected != id) return;
        _previewImage.Texture = ImageTexture.CreateFromImage(img);
    }

    /// <summary>A few seconds of a sound file (read on a worker, bounded and shared), the music ducked underneath.</summary>
    private void PlayPreview(int id, byte[]? bytes, string ext)
    {
        if (!_previewPanel.Visible || _s.Selection.Selected != id) return;
        if (bytes == null)
        {
            PreviewSays(id, "This sound could not be read here (Enter opens it with Windows).");
            return;
        }

        try
        {
            AudioStream? stream = ext switch
            {
                ".wav" => AudioStreamWav.LoadFromBuffer(bytes),
                ".ogg" => AudioStreamOggVorbis.LoadFromBuffer(bytes),
                ".mp3" => new AudioStreamMP3 { Data = bytes },
                _ => null,
            };
            if (stream == null) return;
            _previewPlayer.Stream = stream;
            _previewPlayer.Play();
            _audio.Duck(true); // the music drops under the preview; ClosePreview restores it
            PreviewSays(id, "♪ playing a preview…  (E or Esc stops it)");
        }
        catch (Exception e)
        {
            PreviewSays(id, "This sound could not be played here: " + e.Message);
        }
    }

    /// <summary>Closes the preview, stops a sound preview and restores the music.</summary>
    private void ClosePreview()
    {
        if (!_previewPanel.Visible) return;
        _previewPanel.Visible = false;
        _previewPlayer.Stop();
        _audio.Duck(false);
    }

    // ------------------------------------------------------------------ names in the sky (the shared sign layer)

    private int _hovered = -1;

    /// <summary>What the mouse points at this frame (a node id, or −1).</summary>
    public int HoveredNodeId => _hovered;
    private readonly List<SignCandidate> _signs = [];

    // ---- names (AD-45; by eye unless stated)
    /// <summary>A pad's name is pinned this high above the pad, metres (probes check the same anchor: <see cref="PadAnchor"/>).</summary>
    private const float PadNameHeightMetres = 1.35f;

    /// <summary>Pads further than this from the camera are not named unless they are in focus, metres (AD-45).</summary>
    private const float PadNameReachMetres = 46f;

    /// <summary>A world's name sits just above its top: this many of its drawn radii above its centre.</summary>
    private const float WorldNameLiftRadii = 1.06f;

    /// <summary>
    /// "Hidden by the planet" tests a sphere this much smaller than the planet's radius, metres, so the ground's own
    /// bumps and what stands on it are not counted as hiding things.
    /// </summary>
    private const float OcclusionInsetMetres = 0.3f;

    /// <summary>The ground must be hit at least this much nearer than the thing itself, metres, to hide it (so a ray that
    /// grazes the ground right at the anchor does not).</summary>
    private const float OcclusionSlackMetres = 0.5f;

    /// <summary>The "looked at" boost starts within ≈ 12° of the view's centre (cos 12° ≈ 0.978, AD-45) and reaches 1 at the
    /// centre (the 0.022 below is 1 − 0.978).</summary>
    private const float LookedCosine = 0.978f;

    // Sign ids: a node's own id for pads and objects; offset key spaces for the other kinds (worlds −100 000 − id, groves
    // −1000 − n, bench cards −200 000 − id), so signs of different kinds never share an id even when they name the same node.

    /// <summary>
    /// Names on screen (2026-11-11, after DJ's playtest), every frame so they stay attached as the camera turns:
    /// <list type="bullet">
    /// <item>every launcher pad within reach is named, pinned above its pad with a stem (which pad is plain);</item>
    /// <item>a world in the sky is named when you look at it, point at it or select it, pinned above the body;</item>
    /// <item>nothing is named when the planet stands between the camera and it (a pad over the horizon, a world behind
    /// the planet);</item>
    /// <item>when names overlap, the selected or pointed-at one wins, then the one nearest the middle of the view,
    /// then the nearest.</item>
    /// </list>
    /// </summary>
    private void UpdateSigns(FsTree tree)
    {
        if (_current == null) return;
        if (Busy)
        {
            _s.Signs.Clear();
            return;
        }

        _signs.Clear();
        var camPos = _camera.GlobalPosition;
        var camFwd = -_camera.GlobalBasis.Z;
        var screenUp = _camera.GlobalBasis.Y;
        var mouse = GetViewport().GetMousePosition();
        _hovered = -1;
        if (!_s.Hud.BlockingRects.Any(r => r.HasPoint(mouse)) && !OverOverlay(mouse) && !_orbiting)
        {
            var picked = Pick(mouse);
            if (picked.Id >= 0 && tree.Contains(picked.Id)) _hovered = picked.Id;
            else if (PickBenchTile(mouse) is >= 0 and var tileId) _hovered = tileId;
        }

        var selected = _s.Selection.Selected;
        // Looking at it: within ≈ 12° of the middle of the view gives a boost that grows towards the centre.
        float Looked(Godot.Vector3 at) => MathF.Max(0, (camFwd.Dot((at - camPos).Normalized()) - LookedCosine) / 0.022f);

        // Launcher pads: the ring round the arrival, named on the ground.
        var surf = _current.Comp.Surface;
        var padNamed = new HashSet<int>();
        var groundTest = _current.Comp.Shape.Radius - OcclusionInsetMetres;
        foreach (var l in _current.Comp.Launchers)
        {
            if (!tree.Contains(l.TargetId)) continue;
            var anchor = Gd(surf.PointAt(l.Direction, PadNameHeightMetres));
            var d = camPos.DistanceTo(anchor);
            var standing = _focusLauncher?.TargetId == l.TargetId;
            var focus = standing || selected == l.TargetId || _hovered == l.TargetId;
            if (d > PadNameReachMetres && !focus) continue;
            var n = tree[l.TargetId];
            var detail = l.IsParent ? "the way back" : l.IsLink ? PlaceText.LinkShort(tree, n) : FolderSummary.Gate(n, Listing.Of(n));
            var color = l.IsParent ? new Color(0.8f, 0.86f, 1f) /* the way back: a pale sky blue */ : PlanetArt.Palette(SkyTheme(l)).Accent.Lightened(0.35f);
            var mark = ProminenceMark(l.TargetId);
            var padHidden = RayPlanet(camPos, (anchor - camPos).Normalized(), groundTest) is { } ph && ph < d - OcclusionSlackMetres;
            // Counted as named only if it is on screen, 8 px inside the edges (else the world in the sky must carry the name).
            if (!padHidden && !_camera.IsPositionBehind(anchor) && GetViewport().GetVisibleRect().Grow(-8).HasPoint(_camera.UnprojectPosition(anchor))) padNamed.Add(l.TargetId);
            _signs.Add(new SignCandidate(l.TargetId, anchor, mark + tree.DisplayName(l.TargetId), detail, color,
                // Priority (higher wins an overlap): up to 40 for being looked at, plus 6 for a prominent folder.
                l.IsParent ? IconKind.Up : FileIcons.For(n), focus, 40f * Looked(anchor) + (mark.Length > 0 ? 6f : 0f), FixedAnchor: true));
        }

        // Worlds in the sky: named when looked at, pointed at or selected (and all of them from the overlook).
        void AddWorld(Body b, bool isParent)
        {
            if (!tree.Contains(b.FolderId)) return;
            var scale = b.Node.Transform.Basis.Scale.X;
            var centre = b.Node.GlobalPosition;
            var top = centre + screenUp * (b.Comp.Shape.Radius * scale * WorldNameLiftRadii);
            // A world high in the view: its top is off screen (or under the top 60 px), so the name is pinned on the world itself.
            if (!_camera.IsPositionBehind(top) && _camera.UnprojectPosition(top).Y < 60) top = centre;
            var looked = Looked(centre);
            var focus = selected == b.FolderId || _hovered == b.FolderId;
            if (!focus && looked <= 0 && !OnOverlook) return;
            if (padNamed.Contains(b.FolderId)) return; // its pad is named on screen already: one name per destination
            var detail = isParent ? "the way back" : FolderSummary.Gate(tree[b.FolderId], Listing.Of(tree[b.FolderId]));
            var color = PlanetArt.Palette(b.Comp.Theme).Accent.Lightened(0.35f);
            // A world's look boost (60) is larger than a pad's (40); rationale not recorded.
            _signs.Add(new SignCandidate(-100_000 - b.FolderId, top, tree.DisplayName(b.FolderId), detail, color, isParent ? IconKind.Up : FileIcons.For(tree[b.FolderId]), focus, 60f * looked, FixedAnchor: true));
        }

        foreach (var b in _children.Values) AddWorld(b, false);
        if (_parent != null) AddWorld(_parent, true);

        // Whatever else the mouse points at (a moon in the ring, an object) is named too.
        if (_hovered >= 0 && _hovered != _focusItem?.NodeId && !_children.ContainsKey(_hovered) && _parent?.FolderId != _hovered && _current.Comp.Launchers.All(l => l.TargetId != _hovered))
        {
            var hn = tree[_hovered];
            Godot.Vector3? anchor = null;
            if (_current.Comp.SkyBodies.FirstOrDefault(k => k.InBelt && k.TargetId == _hovered) is { } moon) anchor = Gd(moon.Position) + screenUp * (moon.Radius + 0.4f);
            else if (_current.Comp.Items.FirstOrDefault(i => i.NodeId == _hovered) is { } hi) anchor = Gd(surf.PointAt(hi.Direction, 1.25f * hi.Scale + 0.3f));
            if (anchor is { } a)
            {
                var detail = hn.IsDirectory ? FolderSummary.Gate(hn, Listing.Of(hn)) + " · double-click: fly there" : SizeFormat.Short(hn.Size) + " · double-click: go to it";
                _signs.Add(new SignCandidate(hn.Id, a, hn.Name, detail, new Color(1f, 0.95f, 0.75f) /* warm white */, FileIcons.For(hn), true, 8f, FixedAnchor: true));
            }
        }

        if (OnOverlook || _overlookRequested)
        {
            foreach (var g in _current.Comp.Groves)
            {
                // A grove's name floats 2.6 m over its centre, with no priority boost (it gives way to any other name).
                _signs.Add(new SignCandidate(-1000 - _signs.Count, Gd(surf.PointAt(g.CentreDirection, 2.6f)), g.Label, "", PlanetArt.CategoryColor(g.Category), FileIcons.For(g.Category), false, FixedAnchor: true));
            }
        }

        if (_focusItem is { NodeId: >= 0 } fi && tree.Contains(fi.NodeId))
        {
            var n = tree[fi.NodeId];
            // Above the object: 1.25 × its scale plus 0.3 m (the same anchor as a pointed-at object and the bench's trails).
            _signs.Add(new SignCandidate(n.Id, Gd(surf.PointAt(fi.Direction, 1.25f * fi.Scale + 0.3f)), n.Name, SizeFormat.Short(n.Size), PlanetArt.CategoryColor(fi.Category), FileIcons.For(n), true, 3f, FixedAnchor: true));
        }

        AddBenchSigns(tree);
        AddTelescopeSign(); // PLANET-55 (PlanetWorld.Bell.cs)
        var blocked = _s.Hud.BlockingRects.ToList();
        if (_benchPanel.Visible) blocked.Add(_benchPanel.GetGlobalRect());
        if (_previewPanel.Visible) blocked.Add(_previewPanel.GetGlobalRect());
        if (_contentsPanel.Visible) blocked.Add(_contentsPanel.GetGlobalRect());
        blocked.Add(_promptBox.GetGlobalRect());
        // Hidden by the planet: the line from the camera to the thing meets the ground first.
        var groundR = _current.Comp.Shape.Radius - OcclusionInsetMetres;
        _s.Signs.Layout(_camera, _signs, blocked, (sign, distance) =>
            RayPlanet(camPos, (sign.Anchor - camPos).Normalized(), groundR) is { } hit && hit < distance - OcclusionSlackMetres);
        SignsOccludedTest = groundR;
    }

    /// <summary>The ground radius used for the occlusion test (probes).</summary>
    public float SignsOccludedTest { get; private set; }

    /// <summary>The theme of the world a launcher leads to (its sky body's), for the name's colour.</summary>
    private PlanetTheme SkyTheme(PlanetLauncher l) =>
        _current != null && l.SkyBody >= 0 && l.SkyBody < _current.Comp.SkyBodies.Count ? _current.Comp.SkyBodies[l.SkyBody].Theme : PlanetTheme.Veiled;

    /// <summary>The session's prominence mark for a folder, with a trailing space ("" when it is not prominent).</summary>
    private string ProminenceMark(int id)
    {
        var p = _s.ProminenceOf(id);
        return p.IsProminent ? p.Mark + " " : "";
    }

    // ------------------------------------------------------------------ thumbnails on the picture easels

    /// <summary>Lets the planet's easels load pictures, at most two loading at once; by themselves only in the Starter Sky (FILE-02/DEV-02).</summary>
    private void UpdateThumbnails()
    {
        if (_current?.Visual is not { } v || !_s.IsLive || _s.Tree is not { } tree) return;
        v.PumpThumbnails(tree, maxConcurrent: 2, automatic: _current.AutoThumbnails);
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

    /// <summary>Animates the planets in view (only the current one reacts to the selection and focus) and the travel effects.</summary>
    private void AnimateScene(float dt)
    {
        _current?.Visual?.Animate(dt, Nv(_camera.GlobalPosition), _s.Selection.Selected, FocusNodeId);
        foreach (var c in _children.Values) c.Visual?.Animate(dt, Nv(_camera.GlobalPosition), -1, -1);
        _parent?.Visual?.Animate(dt, Nv(_camera.GlobalPosition), -1, -1);
        _flightTrail.Tick(dt);
        _landFx.Tick(dt);
    }
}
