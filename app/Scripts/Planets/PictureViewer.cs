using FileScape.Core.Model;
using FileScape.Core.Planets;
using FileScape.Core.Interaction;
using FileScape.Core.Places;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The picture viewer (PLANET-96, AD-78; PLANET-161..163, AD-94): the gallery's "view large" — one picture large over a
/// dimmed world, stepped through the folder's pictures with ← → (also A D, Page Up / Down, Home / End, the mouse's back
/// and forward buttons, a click on the ‹ › edges or the bar's buttons), zoomed with the wheel (about the pointer) or + −,
/// panned by dragging, fitted again with 0 or a double-click, and closed with Esc, R, "✕ Back to the world" or a click on
/// the backdrop outside the picture, back to exactly where you stood. Where a press lands is Core's
/// <see cref="ViewerPointer"/>. Enter or "Open with Windows ↗" hands the picture to Windows (marked as leaving, its own
/// colour) and the viewer stays. Space does nothing here (it is the jump: a habit never steps or closes). It owns the
/// pointer while open (full-window, one of the HUD's blocking rectangles) and, through Main's keyboard ownership
/// (<c>KeyPanel.Viewer</c>), the keyboard.
/// <para>
/// Loading is progressive and bounded (PLANET-134, AD-86): a picture appears at once from Windows' thumbnail
/// (<see cref="QuickPixels"/>, usually the thumbnail cache; or the canvas's own sharp thumbnail), then sharpens when
/// FileScape's decode arrives. Only a file the rules allow (<see cref="GalleryRules.Verdict"/>) is decoded: never a link,
/// never over 24 MiB (Windows' thumbnail is then the picture, and says so); a cloud-only picture is fetched for it, which
/// DJ allowed (2026-11-20), and the caption says it is being fetched; HEIC, GIF and TIFF are shown from Windows'
/// thumbnail. The header's pixel count is checked before decoding (<see cref="Thumbnails.MaxDecodedPixels"/>); the
/// picture is decoded on a worker and shrunk to at most <see cref="ViewerMaxPixels"/>; at most one load runs at a time
/// (stepping quickly only remembers the latest request); at most <see cref="CacheSize"/> decoded and
/// <see cref="QuickCacheSize"/> quick pictures are kept. A picture that cannot be shown at all says why, in the frame.
/// </para>
/// Preserve: nothing is read except on a deliberate open or step (the first picture is the one you asked for); results
/// arriving after you stepped on are kept in the cache but not shown; worker results reach Godot objects on the main
/// thread only (<see cref="Tick"/>).
/// </summary>
public partial class PictureViewer : Control
{
    /// <summary>A picture is decoded to at most this many pixels on its longer side: sharper than the 1600 × 900 window at
    /// fit, room to zoom ×1.3 before pixels grow; ≈ 16 MB a picture with mipmaps (2048 × 1536 × 4 × 4/3).</summary>
    public const int ViewerMaxPixels = 2048;

    /// <summary>Decoded pictures kept (the shown one and the two you came from): back and forth is instant, memory ≤ ≈ 50 MB.</summary>
    public const int CacheSize = 3;

    /// <summary>The quick picture's longer side, pixels: Windows' 1024 px thumbnail cache, sharp enough to read at a glance
    /// in a 1600 × 900 window before the decode arrives (or for good, for a file FileScape does not decode).</summary>
    public const int QuickPixels = 1024;

    /// <summary>Quick pictures kept (≈ 5.6 MB each with mipmaps: ≤ ≈ 45 MB).</summary>
    public const int QuickCacheSize = 8;

    /// <summary>Zoom range, as a factor of the fitted size, and one wheel notch's (or + −'s) step.</summary>
    private const float MaxZoom = 6f, ZoomStep = 1.25f;

    /// <summary>The picture's frame keeps this margin from the window's edges, pixels (viewport units): room for the
    /// caption bar above and the key line below.</summary>
    private const float FrameMarginSide = 40f, FrameMarginTop = 84f, FrameMarginBottom = 64f;

    /// <summary>The dimmed world behind the picture: nearly black, a hint of the world showing through.</summary>
    private static readonly Color Backdrop = new(0.02f, 0.02f, 0.05f, 0.9f);

    /// <summary>The ‹ › step marks at the stage's edges: their glyph size, pixels, and their opacity while the pointer is
    /// over that edge and otherwise (by eye: findable, never over the picture's middle).</summary>
    private const int EdgeMarkPixels = 64;

    private const float EdgeMarkHoverAlpha = 0.95f, EdgeMarkIdleAlpha = 0.28f;

    /// <summary>"Open with Windows ↗"'s colour: a cool sky blue, apart from the viewer's warm gold, so leaving FileScape
    /// never looks like one of its own steps (AD-94).</summary>
    private static readonly Color LeaveColor = new(0.62f, 0.84f, 1f);

    private readonly ColorRect _back;
    private readonly Control _stage;
    private readonly TextureRect _image;
    private readonly Panel _placeholder;
    private readonly Label _title, _meta, _status, _keys, _prevMark, _nextMark;
    private readonly Button _prev, _next;

    /// <summary>A left press on the viewer, not yet a click or a drag: where it began and in which zone (AD-94).</summary>
    private Vector2? _pressAt;

    private ViewerZone _pressZone;

    private FsTree? _tree;
    private IReadOnlyList<int> _pictures = [];
    private int _index = -1;
    private float _zoom = 1f;
    private Vector2 _pan;
    private bool _dragging;

    // Loading: one decode at a time; the latest request waits; a small cache of finished pictures (most recent last).
    private int _loadingId = -1, _wantedId = -1;
    private readonly List<(int Id, ImageTexture Texture, Vector2I Source)> _cache = [];
    private readonly List<(int Id, ImageTexture Texture, string Source)> _quick = []; // Windows' thumbnails, most recent last
    private readonly Dictionary<int, string> _refused = [];
    private readonly Dictionary<int, string> _notes = []; // why a picture is shown from Windows' thumbnail only
    private readonly System.Collections.Concurrent.ConcurrentQueue<(int Id, Thumbnails.Result Result, double Ms, bool Final, string Source, bool Quick)> _done = new();

    /// <summary>The viewer stepped to another picture (the world selects it).</summary>
    public event Action<int>? Stepped;

    /// <summary>The viewer closed (the world brings the preview back on the last picture).</summary>
    public event Action<int>? Closed;

    /// <summary>Enter: open the shown picture with Windows.</summary>
    public event Action<int>? OpenRequested;

    public PictureViewer()
    {
        Name = "PictureViewer";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _back = new ColorRect { Color = Backdrop, MouseFilter = MouseFilterEnum.Ignore };
        _back.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_back);
        _stage = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_stage);
        _placeholder = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        var ph = new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.16f, 1f), BorderColor = new Color(0.95f, 0.8f, 0.5f, 0.35f) };
        ph.SetBorderWidthAll(2);
        ph.SetCornerRadiusAll(6);
        _placeholder.AddThemeStyleboxOverride("panel", ph);
        _stage.AddChild(_placeholder);
        _image = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore, TextureFilter = TextureFilterEnum.LinearWithMipmaps };
        _stage.AddChild(_image);
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        _status.AddThemeFontSizeOverride("font_size", 18);
        _status.AddThemeColorOverride("font_color", new Color(0.9f, 0.88f, 0.8f));
        _stage.AddChild(_status);

        var bar = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        bar.SetAnchorsPreset(LayoutPreset.TopWide);
        bar.OffsetLeft = FrameMarginSide; bar.OffsetRight = -FrameMarginSide; bar.OffsetTop = 18; bar.OffsetBottom = 70;
        bar.AddThemeConstantOverride("separation", 8);
        AddChild(bar);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", 0);
        _title = new Label { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore };
        _title.AddThemeFontSizeOverride("font_size", 20);
        _title.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.75f));
        _meta = new Label { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore };
        _meta.AddThemeFontSizeOverride("font_size", 13);
        _meta.AddThemeColorOverride("font_color", new Color(0.78f, 0.82f, 0.95f));
        text.AddChild(_title);
        text.AddChild(_meta);
        bar.AddChild(text);
        Button B(string label, Action act, string tip)
        {
            var b = new Button { Text = label, FocusMode = FocusModeEnum.None, TooltipText = tip };
            b.Pressed += act;
            bar.AddChild(b);
            return b;
        }

        _prev = B("‹ Previous (←)", () => Step(-1), "The previous picture in this folder (also a click on the left edge).");
        _next = B("Next (→) ›", () => Step(+1), "The next picture in this folder (also a click on the right edge).");
        B("Fit (0)", Fit, "Show the whole picture again.");
        var leave = OpenWithWindowsButton("Open with Windows ↗ (Enter)", "Leaves FileScape: opens the original in the app Windows uses for pictures (Photos, usually). This view stays open here.");
        leave.Pressed += () => { if (Current >= 0) OpenRequested?.Invoke(Current); };
        bar.AddChild(leave);
        B("✕ Back to the world (Esc)", Close, "Back to the world, where you stood (also Esc, R, or a click outside the picture).");

        _prevMark = EdgeMark("‹");
        _nextMark = EdgeMark("›");

        _keys = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _keys.SetAnchorsPreset(LayoutPreset.BottomWide);
        _keys.OffsetTop = -44; _keys.OffsetBottom = -16;
        _keys.AddThemeFontSizeOverride("font_size", 14);
        _keys.AddThemeColorOverride("font_color", new Color(0.75f, 0.8f, 0.92f));
        _keys.Text = KeyLine;
        AddChild(_keys);
    }

    /// <summary>The key line under the picture (AD-94): every way to step, look and leave, in the order people reach for them.</summary>
    public const string KeyLine = "← →  or click ‹ ›  previous / next   ·   wheel  zoom   ·   drag  look around   ·   double-click  zoom / fit   ·   Esc or click outside  back to the world   ·   Enter  open with Windows ↗";

    /// <summary>
    /// The one look for "Open with Windows ↗" (the viewer's bar and the small preview, AD-94): the ↗ and a cool blue
    /// outline mark it as leaving FileScape for another app, apart from the in-game buttons beside it.
    /// </summary>
    public static Button OpenWithWindowsButton(string text, string tooltip)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, TooltipText = tooltip };
        b.AddThemeColorOverride("font_color", LeaveColor);
        b.AddThemeColorOverride("font_hover_color", LeaveColor.Lightened(0.3f));
        var box = new StyleBoxFlat { BgColor = new Color(0.06f, 0.1f, 0.18f, 0.85f), BorderColor = LeaveColor with { A = 0.8f }, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 4, ContentMarginBottom = 4 };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(6);
        b.AddThemeStyleboxOverride("normal", box);
        var hover = (StyleBoxFlat)box.Duplicate();
        hover.BgColor = new Color(0.1f, 0.16f, 0.28f, 0.95f);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        return b;
    }

    /// <summary>A ‹ or › mark at a stage edge (placed in <see cref="Layout"/>; brightened over its edge).</summary>
    private Label EdgeMark(string glyph)
    {
        var l = new Label { Text = glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, EdgeMarkIdleAlpha) };
        l.AddThemeFontSizeOverride("font_size", EdgeMarkPixels);
        l.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.75f));
        l.AddThemeConstantOverride("outline_size", 8);
        l.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.02f, 0.05f, 0.9f));
        AddChild(l);
        return l;
    }

    /// <summary>The zone under <paramref name="p"/> (viewport units): the stage and the drawn picture (or the placeholder's
    /// frame while there is none) as Core's rectangles, for <see cref="ViewerPointer.Zone"/>.</summary>
    public ViewerZone ZoneAt(Vector2 p)
    {
        var stage = new ScreenRect(_stage.Position.X, _stage.Position.Y, _stage.Size.X, _stage.Size.Y);
        var frame = _image.Visible ? FrameRect : new Rect2(_placeholder.Position + _stage.Position, _placeholder.Size);
        return ViewerPointer.Zone(p.X, p.Y, stage, new ScreenRect(frame.Position.X, frame.Position.Y, frame.Size.X, frame.Size.Y), _pictures.Count > 1);
    }

    /// <summary>A point on the backdrop (outside the picture, off the step edges), viewport units, or null when the
    /// picture fills the stage (probes: a 5 % grid of the window, searched only when asked).</summary>
    public Vector2? BackdropPoint()
    {
        var size = GetViewportRect().Size;
        for (var fy = 0.15f; fy < 0.9f; fy += 0.05f)
        {
            for (var fx = 0.15f; fx < 0.9f; fx += 0.05f)
            {
                var p = new Vector2(size.X * fx, size.Y * fy);
                if (ZoneAt(p) == ViewerZone.Backdrop) return p;
            }
        }

        return null;
    }

    /// <summary>The zone the pointer was last over (probes; <see cref="ViewerZone.Chrome"/> before any motion).</summary>
    public ViewerZone HoverZone { get; private set; } = ViewerZone.Chrome;

    /// <summary>Brightens the ‹ or › over which the pointer is (and hides both when the folder has one picture).</summary>
    private void ShowHover(ViewerZone zone)
    {
        HoverZone = zone;
        var canStep = _pictures.Count > 1;
        _prevMark.Visible = _nextMark.Visible = canStep;
        _prevMark.Modulate = new Color(1, 1, 1, zone == ViewerZone.PreviousEdge ? EdgeMarkHoverAlpha : EdgeMarkIdleAlpha);
        _nextMark.Modulate = new Color(1, 1, 1, zone == ViewerZone.NextEdge ? EdgeMarkHoverAlpha : EdgeMarkIdleAlpha);
        MouseDefaultCursorShape = zone is ViewerZone.PreviousEdge or ViewerZone.NextEdge ? CursorShape.PointingHand : zone == ViewerZone.Picture && _zoom > 1.001f ? CursorShape.Drag : CursorShape.Arrow;
    }

    // ------------------------------------------------------------------ what probes and the state file read

    public bool IsOpen => Visible;

    /// <summary>The shown picture's node id (−1 when closed).</summary>
    public int Current => Visible && _index >= 0 && _index < _pictures.Count ? _pictures[_index] : -1;

    /// <summary>Position in the folder's pictures (1-based) and their count.</summary>
    public (int Index, int Count) Place => (_index + 1, _pictures.Count);

    /// <summary>"loading", "shown" or "refused" for the current picture; "shown" also while a quick picture waits to sharpen
    /// (see <see cref="Sharp"/>).</summary>
    public string State => Current < 0 ? "closed" : _image.Texture != null && _image.Visible ? "shown" : _refused.ContainsKey(Current) ? "refused" : "loading";

    /// <summary>The current picture is shown at its final sharpness (decoded, or Windows' thumbnail when that is all there is).</summary>
    public bool Sharp => Current >= 0 && (_cache.Any(c => c.Id == Current) || (_notes.ContainsKey(Current) && _quick.Any(q => q.Id == Current)));

    /// <summary>Where the shown picture came from: "decoded", "windows-cache", "windows", "canvas" or "" (probes, AD-86).</summary>
    public string Source => Current < 0 ? "" : _cache.Any(c => c.Id == Current) ? "decoded" : _quick.FirstOrDefault(q => q.Id == Current).Source ?? "";

    /// <summary>Why the current picture is shown from Windows' thumbnail only ("" when it is decoded or not shown).</summary>
    public string Note => Current >= 0 && _notes.TryGetValue(Current, out var n) ? n : "";

    /// <summary>Each quick picture's time to appear (ms): what AD-86 reports beside <see cref="DecodeMs"/>.</summary>
    public List<double> QuickMs { get; } = [];

    /// <summary>Why the current picture is not shown ("" when it is).</summary>
    public string Refusal => Current >= 0 && _refused.TryGetValue(Current, out var r) ? r : "";

    /// <summary>The shown texture's size in pixels (what was decoded, after shrinking), and the picture's own size.</summary>
    public Vector2I ShownPixels => _image.Texture is { } t && _image.Visible ? new Vector2I(t.GetWidth(), t.GetHeight()) : Vector2I.Zero;

    public Vector2I SourcePixels => _cache.FirstOrDefault(c => c.Id == Current).Source;

    /// <summary>The zoom as a factor of the fitted size.</summary>
    public float Zoom => _zoom;

    /// <summary>Decodes started and finished this session, the most decodes ever running at once, the most pictures ever
    /// cached, and each finished decode's time (ms): the bounds a probe checks.</summary>
    public int DecodesStarted { get; private set; }

    public int DecodesFinished { get; private set; }
    public int MaxConcurrentDecodes { get; private set; }
    public int MaxCached { get; private set; }
    public List<double> DecodeMs { get; } = [];

    /// <summary>The picture frame on screen (viewport units).</summary>
    public Rect2 FrameRect => new(_image.Position + _stage.Position, _image.Size);

    // ------------------------------------------------------------------ open, step, close

    /// <summary>Opens on <paramref name="id"/>, stepping through <paramref name="pictures"/> (the folder's, in order).</summary>
    public void Open(FsTree tree, IReadOnlyList<int> pictures, int id)
    {
        if (!ReferenceEquals(tree, _tree))
        {
            _cache.Clear();
            _quick.Clear();
            _refused.Clear();
            _notes.Clear();
        }

        _tree = tree;
        _pictures = pictures;
        _index = Math.Max(0, pictures.ToList().IndexOf(id));
        Visible = true;
        _pressAt = null;
        _dragging = false;
        ShowPicture(_index);
        ShowHover(ViewerZone.Chrome);
    }

    public void Close()
    {
        if (!Visible) return;
        var last = Current;
        Visible = false;
        _dragging = false;
        _pressAt = null;
        Closed?.Invoke(last);
    }

    /// <summary>Steps <paramref name="delta"/> pictures (wrapping round the folder; <see cref="int.MinValue"/> the first,
    /// <see cref="int.MaxValue"/> the last).</summary>
    public void Step(int delta)
    {
        if (!Visible || _pictures.Count == 0) return;
        var next = GalleryBrowse.Step(_index, delta, _pictures.Count); // wraps round; int.MinValue / MaxValue: Home / End
        if (next == _index) return; // Home on the first, End on the last, or the only picture: nothing to show anew
        _index = next;
        ShowPicture(_index);
        Stepped?.Invoke(Current);
    }

    public void Fit()
    {
        _zoom = 1f;
        _pan = Vector2.Zero;
        Layout();
    }

    /// <summary>Zooms by <paramref name="factor"/> about <paramref name="at"/> (viewport units; null: the frame's centre).</summary>
    public void ZoomBy(float factor, Vector2? at = null)
    {
        var old = _zoom;
        _zoom = Mathf.Clamp(_zoom * factor, 1f, MaxZoom);
        if (_zoom <= 1f) _pan = Vector2.Zero;
        else if (at is { } p)
        {
            // Keep the point under the pointer still: it sits (p − centre − pan) from the picture's centre at the old zoom.
            var centre = _stage.Position + _stage.Size / 2;
            _pan = (p - centre) - (p - centre - _pan) * (_zoom / old);
        }

        Layout();
    }

    private void ShowPicture(int index)
    {
        _zoom = 1f;
        _pan = Vector2.Zero;
        var id = _pictures[index];
        var n = _tree![id];
        _title.Text = n.Name;
        _prev.Disabled = _next.Disabled = _pictures.Count < 2;
        var cached = _cache.FindIndex(c => c.Id == id);
        if (cached >= 0)
        {
            var c = _cache[cached];
            _cache.RemoveAt(cached);
            _cache.Add(c); // most recently used last
            ShowTexture(c.Texture);
        }
        else
        {
            _image.Visible = false;
            var quick = _quick.FindIndex(q => q.Id == id);
            if (quick < 0 && CanvasPicture?.Invoke(id) is { } canvas)
            {
                // The canvas's own sharp thumbnail, if it has one: something to look at in the very first frame.
                _quick.Add((id, ImageTexture.CreateFromImage(canvas), "canvas"));
                quick = _quick.Count - 1;
            }

            if (quick >= 0) ShowTexture(_quick[quick].Texture);
            if (_refused.TryGetValue(id, out var why)) ShowStatus($"This picture can't be shown here: {why}.\nEnter opens it with Windows.");
            else if (GalleryRules.Verdict(n) == GalleryRules.PictureVerdict.Link) Refuse(id, "it is a link, and FileScape does not read through links");
            else if (_notes.ContainsKey(id) && quick >= 0) { } // Windows' thumbnail is all there is: already shown
            else
            {
                if (quick < 0) ShowStatus(GalleryRules.Verdict(n) == GalleryRules.PictureVerdict.Download ? "◌  fetching the picture from your cloud storage…" : "◌  loading the picture…");
                Request(id);
            }
        }

        UpdateMeta();
        Layout();
    }

    private void UpdateMeta()
    {
        if (Current < 0 || _tree == null) return;
        var n = _tree[Current];
        var (i, count) = Place;
        var parts = new List<string> { $"{i} of {count} pictures here", SizeFormat.Short(n.Size) };
        var c = _cache.FirstOrDefault(e => e.Id == Current);
        if (c.Texture != null)
        {
            parts.Add($"{c.Source.X:N0} × {c.Source.Y:N0} px");
            if (c.Texture.GetWidth() < c.Source.X) parts.Add($"shown at {c.Texture.GetWidth():N0} × {c.Texture.GetHeight():N0}");
        }
        else if (_image.Visible && Note.Length > 0) parts.Add($"Windows' thumbnail: {Note}");
        else if (_image.Visible && _loadingId == Current) parts.Add(GalleryRules.Verdict(n) == GalleryRules.PictureVerdict.Download ? "fetching the full picture from your cloud storage…" : "sharpening…");

        if (_zoom > 1.001f) parts.Add($"zoom ×{_zoom:0.0}");
        _meta.Text = string.Join("  ·  ", parts);
    }

    private void ShowTexture(ImageTexture t)
    {
        _image.Texture = t;
        _image.Visible = true;
        _status.Visible = false;
        _placeholder.Visible = false;
    }

    private void ShowStatus(string text)
    {
        _status.Text = text;
        _status.Visible = true;
        _placeholder.Visible = true;
    }

    private void Refuse(int id, string why)
    {
        _refused[id] = why;
        if (id == Current)
        {
            _image.Visible = false;
            ShowStatus($"This picture can't be shown here: {why}.\nEnter opens it with Windows.");
        }
    }

    // ------------------------------------------------------------------ bounded loading

    /// <summary>Asks for <paramref name="id"/>: started now if nothing is decoding, otherwise remembered as the next one
    /// (replacing any earlier wish), so stepping quickly never piles up decodes.</summary>
    private void Request(int id)
    {
        _wantedId = id;
        if (_loadingId >= 0) return;
        StartDecode(id);
    }

    private void StartDecode(int id)
    {
        _wantedId = -1;
        _loadingId = id;
        DecodesStarted++;
        MaxConcurrentDecodes = Math.Max(MaxConcurrentDecodes, 1);
        var tree = _tree!;
        var n = tree[id];
        var path = tree.FullPath(id);
        var verdict = GalleryRules.Verdict(n);
        var decode = verdict is GalleryRules.PictureVerdict.Viewable or GalleryRules.PictureVerdict.Download;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Windows' thumbnail first when nothing is shown yet (asked here, on the main thread, which reads the tree).
        var quick = _quick.Any(q => q.Id == id && q.Source != "canvas") ? null : GalleryThumbnails.RequestAsync(tree, n, QuickPixels, decode: false);
        var why = verdict switch
        {
            GalleryRules.PictureVerdict.TooLarge => $"the file is {SizeFormat.Short(n.Size)}, over the {SizeFormat.Short(GalleryRules.MaxPictureBytes)} FileScape reads",
            GalleryRules.PictureVerdict.WindowsOnly => "FileScape does not decode this kind of picture itself",
            _ => "",
        };
        _ = Task.Run(async () =>
        {
            if (quick != null)
            {
                var a = await quick.ConfigureAwait(false);
                _done.Enqueue((id, new Thumbnails.Result(a.Image, 0, 0, why.Length > 0 ? why : a.Refusal), sw.Elapsed.TotalMilliseconds, !decode, a.Source, true));
            }

            if (decode)
            {
                var r = await Thumbnails.LoadWithReasonAsync(path, ViewerMaxPixels, GalleryRules.MaxPictureBytes).ConfigureAwait(false);
                _done.Enqueue((id, r, sw.Elapsed.TotalMilliseconds, true, "decoded", false));
            }
            else if (quick == null)
            {
                _done.Enqueue((id, new Thumbnails.Result(null, 0, 0, why), 0, true, "none", true));
            }
        });
    }

    /// <summary>The canvas's own picture of a node, if the world has one (set by the world: its sharp thumbnail).</summary>
    public Func<int, Image?>? CanvasPicture { get; set; }

    /// <summary>Per frame (main thread): takes finished decodes, caches them (evicting the oldest past <see cref="CacheSize"/>),
    /// shows the one still wanted, and starts the next request.</summary>
    public void Tick()
    {
        while (_done.TryDequeue(out var d))
        {
            if (d.Quick)
            {
                // Windows' thumbnail: shown at once if nothing better is; final when FileScape will not decode the file.
                if (d.Result.Image is { } qi)
                {
                    QuickMs.Add(d.Ms);
                    var qt = ImageTexture.CreateFromImage(qi);
                    _quick.RemoveAll(q => q.Id == d.Id);
                    _quick.Add((d.Id, qt, d.Source));
                    while (_quick.Count > QuickCacheSize) _quick.RemoveAt(0);
                    if (d.Final) _notes[d.Id] = d.Result.Refusal is { Length: > 0 } note ? note : "the only picture Windows has";
                    if (d.Id == Current && !_cache.Any(c => c.Id == d.Id))
                    {
                        ShowTexture(qt);
                        Layout();
                    }
                }
                else if (d.Final)
                {
                    Refuse(d.Id, d.Result.Refusal is { Length: > 0 } r ? r : "Windows has no thumbnail of it");
                }

                if (d.Final) _loadingId = -1;
                UpdateMeta();
                continue;
            }

            DecodesFinished++;
            DecodeMs.Add(d.Ms);
            _loadingId = -1;
            if (d.Result.Image == null && _quick.Any(q => q.Id == d.Id))
            {
                // The decode was refused (a 63-megapixel scan, damaged data Windows could still read): Windows' thumbnail stays.
                _notes[d.Id] = d.Result.Refusal ?? "it could not be decoded";
                UpdateMeta();
                continue;
            }

            if (d.Result.Image is { } img)
            {
                var tex = ImageTexture.CreateFromImage(img);
                _cache.RemoveAll(c => c.Id == d.Id);
                _cache.Add((d.Id, tex, new Vector2I(d.Result.SourceWidth, d.Result.SourceHeight)));
                while (_cache.Count > CacheSize) _cache.RemoveAt(0);
                MaxCached = Math.Max(MaxCached, _cache.Count);
                if (d.Id == Current)
                {
                    ShowTexture(tex);
                    Layout();
                }
            }
            else
            {
                Refuse(d.Id, d.Result.Refusal ?? "it could not be decoded");
            }

            UpdateMeta();
            GD.Print($"[viewer] decoded {(_tree != null && _tree.Contains(d.Id) ? _tree[d.Id].Name : d.Id.ToString())} in {d.Ms:0} ms: {(d.Result.Image is { } i ? $"{i.GetWidth()}×{i.GetHeight()}" : d.Result.Refusal)}");
        }

        if (_loadingId < 0 && _wantedId >= 0)
        {
            if (_cache.Any(c => c.Id == _wantedId) || _refused.ContainsKey(_wantedId) || _notes.ContainsKey(_wantedId)) _wantedId = -1;
            else StartDecode(_wantedId);
        }
    }

    // ------------------------------------------------------------------ layout and pointer

    public override void _Notification(int what)
    {
        if (what == NotificationResized) Layout();
    }

    /// <summary>Places the stage inside the margins and the picture in it: fitted (aspect kept), then zoomed about the
    /// centre and panned, the pan clamped so the picture never leaves the frame.</summary>
    private void Layout()
    {
        var size = GetViewportRect().Size;
        _stage.Position = new Vector2(FrameMarginSide, FrameMarginTop);
        _stage.Size = new Vector2(MathF.Max(1, size.X - 2 * FrameMarginSide), MathF.Max(1, size.Y - FrameMarginTop - FrameMarginBottom));
        var area = _stage.Size;
        Vector2 fitted;
        if (_image.Texture is { } t && _image.Visible)
        {
            var k = MathF.Min(area.X / t.GetWidth(), area.Y / t.GetHeight());
            fitted = new Vector2(t.GetWidth(), t.GetHeight()) * k;
        }
        else
        {
            fitted = new Vector2(MathF.Min(area.X, area.Y * 4 / 3f), MathF.Min(area.Y, area.X * 3 / 4f)) * 0.6f; // the placeholder's frame
        }

        var shown = fitted * _zoom;
        var slack = (shown - area) / 2;
        _pan = new Vector2(slack.X > 0 ? Mathf.Clamp(_pan.X, -slack.X, slack.X) : 0, slack.Y > 0 ? Mathf.Clamp(_pan.Y, -slack.Y, slack.Y) : 0);
        var pos = (area - shown) / 2 + _pan;
        _image.Position = pos;
        _image.Size = shown;
        _placeholder.Position = (area - fitted) / 2;
        _placeholder.Size = fitted;
        _status.Position = _placeholder.Position + new Vector2(24, 0);
        _status.Size = fitted - new Vector2(48, 0);
        var edge = MathF.Min(ViewerPointer.EdgeWidthPixels, area.X * ViewerPointer.MaxEdgeShare);
        _prevMark.Position = _stage.Position;
        _nextMark.Position = _stage.Position + new Vector2(area.X - edge, 0);
        _prevMark.Size = _nextMark.Size = new Vector2(edge, area.Y);
        UpdateMeta();
    }

    /// <summary>
    /// The viewer's mouse (AD-94; zones from <see cref="ViewerPointer"/>): the wheel zooms about the pointer anywhere; a left
    /// press becomes a drag (a pan) after <see cref="PlanetWorld.DragThresholdPixels"/> of travel, otherwise a click on
    /// release: on the ‹ › edges it steps, on the backdrop it goes back to the world, on the picture nothing; a double-click
    /// zooms ×2 or fits on the picture and steps again on an edge; the mouse's back / forward buttons step. Every button
    /// stays with the viewer, never the camera.
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } w:
                ZoomBy(ZoomStep, w.Position);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } w:
                ZoomBy(1 / ZoomStep, w.Position);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Xbutton1 or MouseButton.Xbutton2, Pressed: true } x:
                Step(x.ButtonIndex == MouseButton.Xbutton1 ? -1 : +1); // the mouse's back / forward buttons
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true } dc:
                var dz = ZoneAt(dc.Position);
                if (dz is ViewerZone.PreviousEdge or ViewerZone.NextEdge) Step(dz == ViewerZone.PreviousEdge ? -1 : +1);
                else if (dz == ViewerZone.Picture)
                {
                    if (_zoom > 1.001f) Fit();
                    else ZoomBy(2f, dc.Position);
                }

                _pressAt = null; // a double-click is done on its press: its release is not a click
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                _pressAt = press.Position;
                _pressZone = ZoneAt(press.Position);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                var wasClick = _pressAt != null && !_dragging;
                _dragging = false;
                _pressAt = null;
                AcceptEvent();
                if (!wasClick) break;
                switch (_pressZone)
                {
                    case ViewerZone.PreviousEdge: Step(-1); break;
                    case ViewerZone.NextEdge: Step(+1); break;
                    case ViewerZone.Backdrop: Close(); break; // the outside click: back to the world, nothing else (AD-83, AD-94)
                }

                break;
            case InputEventMouseMotion m when _pressAt is { } from:
                if (!_dragging && m.Position.DistanceTo(from) >= PlanetWorld.DragThresholdPixels) _dragging = true;
                if (_dragging)
                {
                    _pan += m.Relative;
                    Layout();
                }

                AcceptEvent();
                break;
            case InputEventMouseMotion hover:
                ShowHover(ZoneAt(hover.Position));
                break;
            case InputEventMouseButton:
                AcceptEvent(); // every other button stays with the viewer, never the camera
                break;
        }
    }

    /// <summary>
    /// The viewer's keys (Main gives it the keyboard while open): ← → (and A D, Page Up / Down) step, Home / End the first
    /// and last, + − zoom, 0 fits, Enter opens with Windows (the viewer stays), Esc and R close. Space is not the viewer's:
    /// Main keeps it from the game, so a jump pressed by habit does nothing (AD-94). True when the key was the viewer's;
    /// every other key is kept from the game by Main.
    /// </summary>
    public bool HandleKey(InputEventKey k)
    {
        if (!Visible || !k.Pressed) return false;
        switch (k.Keycode)
        {
            case Godot.Key.Left or Godot.Key.A or Godot.Key.Pageup:
                Step(-1);
                return true;
            case Godot.Key.Right or Godot.Key.D or Godot.Key.Pagedown:
                Step(+1);
                return true;
            case Godot.Key.Home:
                Step(int.MinValue);
                return true;
            case Godot.Key.End:
                Step(int.MaxValue);
                return true;
            case Godot.Key.Equal or Godot.Key.Plus or Godot.Key.KpAdd:
                ZoomBy(ZoomStep);
                return true;
            case Godot.Key.Minus or Godot.Key.KpSubtract:
                ZoomBy(1 / ZoomStep);
                return true;
            case Godot.Key.Key0 or Godot.Key.Kp0:
                Fit();
                return true;
            case Godot.Key.Enter or Godot.Key.KpEnter:
                if (!k.Echo && Current >= 0) OpenRequested?.Invoke(Current);
                return true;
            case Godot.Key.Escape or Godot.Key.R:
                if (!k.Echo) Close();
                return true;
        }

        return false;
    }
}
