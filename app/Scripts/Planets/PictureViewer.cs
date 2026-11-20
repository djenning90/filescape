using FileScape.Core.Model;
using FileScape.Core.Planets;
using FileScape.Core.Interaction;
using FileScape.Core.Places;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The picture viewer (PLANET-96, AD-78): the gallery prototype's "inspect" — one picture large over a dimmed world,
/// stepped through the folder's pictures with ← →, zoomed with the wheel (about the pointer) or + −, panned by dragging,
/// fitted again with 0 or a double-click, and closed with Esc, R or ✕ back to exactly where you stood. It owns the
/// pointer while open (full-window, one of the HUD's blocking rectangles) and, through Main's keyboard ownership
/// (<c>KeyPanel.Viewer</c>), the keyboard.
/// <para>
/// Loading is bounded the way the preview is: only a file the folder's rules allow (<see cref="GalleryRules.Verdict"/>:
/// never a cloud placeholder or a link, never over 24 MiB) is read; the header's pixel count is checked before decoding
/// (<see cref="Thumbnails.MaxDecodedPixels"/>); the picture is decoded on a worker and shrunk to at most
/// <see cref="ViewerMaxPixels"/> on its longer side; at most one decode runs at a time (stepping quickly only remembers the
/// latest request); and at most <see cref="CacheSize"/> decoded pictures are kept. While a picture loads, its frame shows a
/// placeholder; a picture that cannot be shown says why, in the frame, and the others stay reachable.
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

    /// <summary>Zoom range, as a factor of the fitted size, and one wheel notch's (or + −'s) step.</summary>
    private const float MaxZoom = 6f, ZoomStep = 1.25f;

    /// <summary>The picture's frame keeps this margin from the window's edges, pixels (viewport units): room for the
    /// caption bar above and the key line below.</summary>
    private const float FrameMarginSide = 40f, FrameMarginTop = 84f, FrameMarginBottom = 64f;

    /// <summary>The dimmed world behind the picture: nearly black, a hint of the world showing through.</summary>
    private static readonly Color Backdrop = new(0.02f, 0.02f, 0.05f, 0.9f);

    private readonly ColorRect _back;
    private readonly Control _stage;
    private readonly TextureRect _image;
    private readonly Panel _placeholder;
    private readonly Label _title, _meta, _status, _keys;
    private readonly Button _prev, _next;

    private FsTree? _tree;
    private IReadOnlyList<int> _pictures = [];
    private int _index = -1;
    private float _zoom = 1f;
    private Vector2 _pan;
    private bool _dragging;

    // Loading: one decode at a time; the latest request waits; a small cache of finished pictures (most recent last).
    private int _loadingId = -1, _wantedId = -1;
    private readonly List<(int Id, ImageTexture Texture, Vector2I Source)> _cache = [];
    private readonly Dictionary<int, string> _refused = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<(int Id, Thumbnails.Result Result, double Ms)> _done = new();

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

        _prev = B("‹ Previous (←)", () => Step(-1), "The previous picture in this folder.");
        _next = B("Next (→) ›", () => Step(+1), "The next picture in this folder.");
        B("Fit (0)", Fit, "Show the whole picture again.");
        B("Open with Windows (Enter)", () => { if (Current >= 0) OpenRequested?.Invoke(Current); }, "Open this picture with the app Windows uses for it.");
        B("✕ Close (Esc)", Close, "Back to the world, where you stood.");

        _keys = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _keys.SetAnchorsPreset(LayoutPreset.BottomWide);
        _keys.OffsetTop = -44; _keys.OffsetBottom = -16;
        _keys.AddThemeFontSizeOverride("font_size", 14);
        _keys.AddThemeColorOverride("font_color", new Color(0.75f, 0.8f, 0.92f));
        _keys.Text = "← →  step through the pictures here   ·   wheel or + −  zoom   ·   drag  look around   ·   0  fit   ·   Esc  back to the world";
        AddChild(_keys);
    }

    // ------------------------------------------------------------------ what probes and the state file read

    public bool IsOpen => Visible;

    /// <summary>The shown picture's node id (−1 when closed).</summary>
    public int Current => Visible && _index >= 0 && _index < _pictures.Count ? _pictures[_index] : -1;

    /// <summary>Position in the folder's pictures (1-based) and their count.</summary>
    public (int Index, int Count) Place => (_index + 1, _pictures.Count);

    /// <summary>"loading", "shown" or "refused" for the current picture.</summary>
    public string State => Current < 0 ? "closed" : _refused.ContainsKey(Current) ? "refused" : _image.Texture != null && _image.Visible ? "shown" : "loading";

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
            _refused.Clear();
        }

        _tree = tree;
        _pictures = pictures;
        _index = Math.Max(0, pictures.ToList().IndexOf(id));
        Visible = true;
        ShowPicture(_index);
    }

    public void Close()
    {
        if (!Visible) return;
        var last = Current;
        Visible = false;
        _dragging = false;
        Closed?.Invoke(last);
    }

    /// <summary>Steps <paramref name="delta"/> pictures (wrapping round the folder).</summary>
    public void Step(int delta)
    {
        if (!Visible || _pictures.Count == 0) return;
        _index = ((_index + delta) % _pictures.Count + _pictures.Count) % _pictures.Count;
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
            if (_refused.TryGetValue(id, out var why)) ShowStatus($"This picture can't be shown here: {why}.\nEnter opens it with Windows.");
            else if (GalleryRules.Verdict(n) is var verdict && verdict != GalleryRules.PictureVerdict.Viewable) Refuse(id, verdict switch
            {
                GalleryRules.PictureVerdict.Link => "it is a link, and FileScape does not read through links",
                _ => $"it is {SizeFormat.Short(n.Size)}, over the {SizeFormat.Short(GalleryRules.MaxPictureBytes)} a preview reads",
            });
            else
            {
                ShowStatus("◌  loading the picture…");
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
        var path = _tree!.FullPath(id);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _ = Thumbnails.LoadWithReasonAsync(path, ViewerMaxPixels, GalleryRules.MaxPictureBytes).ContinueWith(t => _done.Enqueue((id, t.Result, sw.Elapsed.TotalMilliseconds)));
    }

    /// <summary>Per frame (main thread): takes finished decodes, caches them (evicting the oldest past <see cref="CacheSize"/>),
    /// shows the one still wanted, and starts the next request.</summary>
    public void Tick()
    {
        while (_done.TryDequeue(out var d))
        {
            DecodesFinished++;
            DecodeMs.Add(d.Ms);
            _loadingId = -1;
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
            if (_cache.Any(c => c.Id == _wantedId) || _refused.ContainsKey(_wantedId)) _wantedId = -1;
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
        UpdateMeta();
    }

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
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true } dc:
                if (_zoom > 1.001f) Fit();
                else ZoomBy(2f, dc.Position);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                _dragging = b.Pressed;
                AcceptEvent();
                break;
            case InputEventMouseMotion m when _dragging:
                _pan += m.Relative;
                Layout();
                AcceptEvent();
                break;
            case InputEventMouseButton:
                AcceptEvent(); // every other button stays with the viewer, never the camera
                break;
        }
    }

    /// <summary>
    /// The viewer's keys (Main gives it the keyboard while open): ← → (and A D) step, + − zoom, 0 fits, Enter opens with
    /// Windows, Esc and R close. True when the key was the viewer's; every other key is kept from the game by Main.
    /// </summary>
    public bool HandleKey(InputEventKey k)
    {
        if (!Visible || !k.Pressed) return false;
        switch (k.Keycode)
        {
            case Godot.Key.Left or Godot.Key.A:
                Step(-1);
                return true;
            case Godot.Key.Right or Godot.Key.D:
                Step(+1);
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
