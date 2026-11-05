using Godot;

namespace FileScape.App.Ui;

/// <summary>
/// The Places parts of the HUD (PLACE-02, 04, 06, 07, 10): the "You are in" breadcrumb with Up and Root, the
/// destination list (every child folder, so none is out of reach), the ascent chip that explains what ascent
/// shows, and the threshold transition (fade and caption).
/// </summary>
public partial class Hud
{
    public const string PlacesHintText = "↑↓ walk · ←→ turn · WASD · hold right mouse to look · Space jump · Shift run · T fly · J destinations · K key · F1 help";

    public event Action<int>? BreadcrumbRequested;
    public event Action? UpRequested;
    public event Action? RootRequested;
    public event Action<int>? DestinationSelected;
    public event Action<int>? DestinationActivated;
    public event Action? EnterRequested;

    private PanelContainer _placePanel = null!;
    private HBoxContainer _crumbs = null!;
    private ScrollContainer _crumbScroll = null!;
    private Label _placeSummary = null!;
    private Button _upButton = null!, _rootButton = null!;
    private PanelContainer _destPanel = null!;
    private Label _destTitle = null!;
    private ItemList _destList = null!;
    private PanelContainer _ascentPanel = null!;
    private Label _ascentLabel = null!;
    private ColorRect _fade = null!;
    private Label _caption = null!;
    private Button _enterButton = null!;

    /// <summary>The breadcrumb as shown, root first (checked by automation).</summary>
    public List<string> BreadcrumbNames { get; } = [];

    public int DestinationCount => _destList.ItemCount;

    private void BuildPlaces()
    {
        _placePanel = new PanelContainer();
        _placePanel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _placePanel.OffsetLeft = 10; _placePanel.OffsetRight = -390; _placePanel.OffsetTop = 112;
        _root.AddChild(_placePanel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 2);
        _placePanel.AddChild(v);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        v.AddChild(row);
        var here = new Label { Text = "You are in ", VerticalAlignment = VerticalAlignment.Center };
        here.AddThemeColorOverride("font_color", new Color(0.65f, 0.75f, 0.9f));
        row.AddChild(here);
        // The breadcrumb scrolls (without a bar) rather than widening the panel, so the place bar never runs into the
        // inspector at higher UI scales; it is kept scrolled to its end, the current folder.
        _crumbScroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        row.AddChild(_crumbScroll);
        _crumbs = new HBoxContainer();
        _crumbs.AddThemeConstantOverride("separation", 0);
        _crumbScroll.AddChild(_crumbs);
        _upButton = MakeButton("▲ Up (U)", () => UpRequested?.Invoke(), "Go up to the parent folder (U or PgUp), arriving in front of the gate you came from.");
        _rootButton = MakeButton("⌂ Root", () => RootRequested?.Invoke(), "Back to the folder you chose to explore (Ctrl+Home).");
        row.AddChild(_upButton);
        row.AddChild(_rootButton);
        _destButton = MakeButton("☰ Destinations (J)", () => ToggleDestinations(focusFilter: false), "Every folder here, in gate order (A→Z, west → east), with filtering. Opens and closes on demand (J).");
        _destButton.ToggleMode = true;
        row.AddChild(_destButton);
        // One line: what is here · what is in the whole subtree (SUM-01/02); the full wording is in the tooltip and on the plaque.
        _placeSummary = new Label { ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = Control.MouseFilterEnum.Pass };
        _placeSummary.AddThemeFontSizeOverride("font_size", 13);
        _placeSummary.AddThemeColorOverride("font_color", new Color(0.72f, 0.8f, 0.94f));
        v.AddChild(_placeSummary);

        _destPanel = new PanelContainer { CustomMinimumSize = new Vector2(330, 0), Visible = false };
        _destPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _destPanel.OffsetLeft = 10; _destPanel.OffsetTop = 190; _destPanel.OffsetRight = 350; _destPanel.OffsetBottom = 470;
        _root.AddChild(_destPanel);
        var dv = new VBoxContainer();
        _destPanel.AddChild(dv);
        var titleRow = new HBoxContainer();
        _destTitle = new Label { Text = "Destinations", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _destTitle.AddThemeFontSizeOverride("font_size", 14);
        titleRow.AddChild(_destTitle);
        var close = MakeButton("×", () => ToggleDestinations(focusFilter: false), "Close (J or Esc). Your place in the list is kept.");
        close.Flat = true;
        titleRow.AddChild(close);
        dv.AddChild(titleRow);
        // Keyboard access (VIEW-03): J opens the drawer with this field focused; type to filter, ↑/↓ to choose, Enter to go.
        _destFilter = new LineEdit { PlaceholderText = "Filter by name · ↑↓ choose · Enter go · Esc close", ClearButtonEnabled = true };
        _destFilter.AddThemeFontSizeOverride("font_size", 13);
        _destFilter.TextChanged += _ => ApplyDestinationFilter();
        _destFilter.GuiInput += OnDestinationFilterKey;
        dv.AddChild(_destFilter);
        // FocusMode None: the list must never take the arrow keys away from walking (NAV-08).
        _destList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None, SelectMode = ItemList.SelectModeEnum.Single };
        _destList.AddThemeFontSizeOverride("font_size", 13);
        // ItemClicked fires on every click; ItemSelected would not fire for an item that is already highlighted.
        // Picking a row with the mouse hands the keyboard back to walking (the filter lets go; Esc then closes the drawer).
        _destList.ItemClicked += (i, _, button) =>
        {
            if (button != (long)MouseButton.Left) return;
            _destFilter.ReleaseFocus();
            DestinationSelected?.Invoke((int)_destList.GetItemMetadata((int)i));
        };
        _destList.ItemActivated += i => DestinationActivated?.Invoke((int)_destList.GetItemMetadata((int)i));
        dv.AddChild(_destList);
        var help = new Label { Text = "Click: look at it · double-click: go", HorizontalAlignment = HorizontalAlignment.Center };
        help.AddThemeFontSizeOverride("font_size", 11);
        help.AddThemeColorOverride("font_color", new Color(0.6f, 0.68f, 0.82f));
        dv.AddChild(help);

        // The aerial chip sits at the bottom (the top already holds the toolbar, place bar and flight chip).
        _ascentPanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _ascentPanel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _ascentPanel.GrowHorizontal = Control.GrowDirection.Both;
        _ascentPanel.GrowVertical = Control.GrowDirection.Begin;
        _ascentPanel.OffsetBottom = -14;
        _ascentLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _ascentLabel.AddThemeFontSizeOverride("font_size", 14);
        _ascentLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.92f, 1f));
        _ascentPanel.AddChild(_ascentLabel);
        _root.AddChild(_ascentPanel);

        _fade = new ColorRect { Color = new Color(0.008f, 0.01f, 0.03f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_fade);
        _caption = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _caption.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _caption.AddThemeFontSizeOverride("font_size", 30);
        _caption.AddThemeColorOverride("font_color", new Color(0.88f, 0.94f, 1f));
        _caption.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        _caption.AddThemeConstantOverride("outline_size", 6);
        _root.AddChild(_caption);
        // The fade must sit under the toolbar and inspector so they stay readable during a transition.
        _root.MoveChild(_fade, 0);
        _root.MoveChild(_caption, 1);

        _enterButton = MakeButton("Go inside (G)", () => EnterRequested?.Invoke(), "Travel into this folder: in Places through its gate, from a map straight into its place.");
        _enterButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _openButton.GetParent().AddChild(_enterButton);
        BuildWorld(row, v);
    }

    /// <summary>Sets the "You are in" breadcrumb. Long paths keep the root, an ellipsis with the hidden depth, and the last levels.</summary>
    public void SetPlace(IReadOnlyList<(int Id, string Name)> path, string summary, bool canUp, bool atRoot)
    {
        foreach (var c in _crumbs.GetChildren()) c.QueueFree();
        BreadcrumbNames.Clear();
        BreadcrumbNames.AddRange(path.Select(p => p.Name));
        const int keepTail = 4;
        for (var i = 0; i < path.Count; i++)
        {
            if (path.Count > keepTail + 2 && i == 1)
            {
                var skipped = path.Count - keepTail - 1;
                _crumbs.AddChild(new Label { Text = $" › …{skipped} levels… ", VerticalAlignment = VerticalAlignment.Center, TooltipText = string.Join(" › ", path.Skip(1).Take(skipped).Select(p => p.Name)), MouseFilter = Control.MouseFilterEnum.Stop });
                i = path.Count - keepTail - 1;
                continue;
            }

            if (i > 0) _crumbs.AddChild(new Label { Text = " › ", VerticalAlignment = VerticalAlignment.Center });
            var (id, name) = path[i];
            var last = i == path.Count - 1;
            var b = new Button { Text = name, Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = last ? "You are here" : $"Go to {name}", Disabled = last };
            b.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1f));
            b.AddThemeColorOverride("font_disabled_color", new Color(1f, 0.95f, 0.7f));
            b.Pressed += () => BreadcrumbRequested?.Invoke(id);
            _crumbs.AddChild(b);
        }

        _placeSummary.Text = summary.Replace("\n", "  ·  ");
        _placeSummary.TooltipText = summary;
        _upButton.Disabled = !canUp;
        _rootButton.Disabled = atRoot;
    }

    private Button _destButton = null!;
    private LineEdit _destFilter = null!;
    private bool _destOpen;
    private IReadOnlyList<(int Id, string Text, Color Color, string Tooltip, Texture2D? Icon)> _destItems = [];

    public bool DestinationsOpen => _destOpen;
    public Rect2 DestinationsButtonRect => _destButton.GetGlobalRect();
    public LineEdit DestinationFilter => _destFilter;

    /// <summary>Opens or closes the Destinations drawer (J, the ☰ button). Contents, filter and scroll are kept while closed.</summary>
    public void ToggleDestinations(bool focusFilter)
    {
        _destOpen = !_destOpen;
        _destPanel.Visible = _placesMode && _destOpen;
        _destButton.SetPressedNoSignal(_destOpen);
        if (_destOpen && focusFilter) _destFilter.GrabFocus();
        if (!_destOpen) _destFilter.ReleaseFocus();
        if (_destOpen) DismissHint();
    }

    private void OnDestinationFilterKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k) return;
        var count = _destList.ItemCount;
        var cur = _destList.GetSelectedItems() is { Length: > 0 } sel ? sel[0] : -1;
        switch (k.Keycode)
        {
            case Key.Down when count > 0:
                _destList.Select(Math.Min(count - 1, cur + 1));
                _destList.EnsureCurrentIsVisible();
                _destFilter.AcceptEvent();
                break;
            case Key.Up when count > 0:
                _destList.Select(Math.Max(0, cur - 1));
                _destList.EnsureCurrentIsVisible();
                _destFilter.AcceptEvent();
                break;
            case Key.Enter or Key.KpEnter when count > 0:
                var pick = cur >= 0 ? cur : 0;
                var id = (int)_destList.GetItemMetadata(pick);
                if (id >= 0) DestinationActivated?.Invoke(id);
                ToggleDestinations(focusFilter: false);
                _destFilter.AcceptEvent();
                break;
            case Key.Escape:
                ToggleDestinations(focusFilter: false);
                _destFilter.AcceptEvent();
                break;
        }
    }

    public void SetDestinations(IReadOnlyList<(int Id, string Text, Color Color, string Tooltip, Texture2D? Icon)> items, string title)
    {
        _destItems = items;
        _destTitle.Text = title;
        _destButton.Text = "☰ Destinations (J)";
        ApplyDestinationFilter();
    }

    private void ApplyDestinationFilter()
    {
        var filter = _destFilter.Text.Trim();
        _destList.Clear();
        foreach (var (id, text, color, tip, icon) in _destItems)
        {
            if (filter.Length > 0 && id >= 0 && !text.Contains(filter, StringComparison.CurrentCultureIgnoreCase)) continue;
            var i = icon != null ? _destList.AddItem(text, icon) : _destList.AddItem(text);
            _destList.SetItemMetadata(i, id);
            _destList.SetItemCustomFgColor(i, color);
            _destList.SetItemTooltip(i, tip);
        }
    }

    public string HintLabelText => _hintLabel.Text;

    /// <summary>Scrolls the destination list so an item is visible (wide folders have hundreds).</summary>
    public void EnsureDestinationVisible(int index)
    {
        _destList.Select(index);
        _destList.EnsureCurrentIsVisible();
    }

    public void HighlightDestination(int id)
    {
        for (var i = 0; i < _destList.ItemCount; i++)
        {
            if ((int)_destList.GetItemMetadata(i) == id)
            {
                _destList.Select(i);
                _destList.EnsureCurrentIsVisible();
                return;
            }
        }

        _destList.DeselectAll();
    }

    /// <summary>The text of the destination list items (for checks).</summary>
    public IEnumerable<int> DestinationIds()
    {
        for (var i = 0; i < _destList.ItemCount; i++) yield return (int)_destList.GetItemMetadata(i);
    }

    /// <summary>An item's on-screen rectangle (item rects are in content coordinates, so the scroll offset is removed).</summary>
    public Rect2 DestinationItemRect(int index)
    {
        var r = _destList.GetItemRect(index);
        var scroll = (float)_destList.GetVScrollBar().Value;
        return new Rect2(_destList.GetGlobalRect().Position + r.Position - new Vector2(0, scroll), r.Size);
    }

    public void SetAscentChip(string? text)
    {
        _ascentPanel.Visible = text != null;
        if (text != null) _ascentLabel.Text = text;
    }

    public bool AscentChipVisible => _ascentPanel.Visible;

    public void SetTransition(float fade, string? caption)
    {
        _fade.Visible = fade > 0.001f;
        _fade.Color = _fade.Color with { A = Mathf.Clamp(fade, 0, 1) * 0.94f };
        _caption.Visible = caption != null && fade > 0.15f;
        if (caption != null) _caption.Text = caption;
        _caption.Modulate = new Color(1, 1, 1, Mathf.Clamp(fade * 1.4f - 0.2f, 0, 1));
    }

    public void SetEnterAction(bool enabled, string text)
    {
        _enterButton.Disabled = !enabled;
        _enterButton.Text = text;
    }
}

/// <summary>
/// The gate chip (PLACE-17): a compact screen-space name for the gate you are walking up to, or the gate under the
/// cursor or selected, so a nearby entrance is identifiable even when its signs are out of view or hidden.
/// </summary>
public partial class Hud
{
    private PanelContainer _gateChip = null!;
    private Label _gateChipTitle = null!, _gateChipDetail = null!;

    public bool GateChipVisible => _gateChip.Visible;
    public string GateChipText => _gateChip.Visible ? _gateChipTitle.Text + " | " + _gateChipDetail.Text : "";
    public Rect2 GateChipRect => _gateChip.GetGlobalRect();

    private void BuildGateChip()
    {
        _gateChip = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_gateChip);
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 0);
        _gateChip.AddChild(v);
        _gateChipTitle = new Label { HorizontalAlignment = HorizontalAlignment.Center, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _gateChipTitle.AddThemeFontSizeOverride("font_size", 17);
        v.AddChild(_gateChipTitle);
        _gateChipDetail = new Label { HorizontalAlignment = HorizontalAlignment.Center, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _gateChipDetail.AddThemeFontSizeOverride("font_size", 13);
        _gateChipDetail.AddThemeColorOverride("font_color", new Color(0.78f, 0.85f, 0.97f));
        v.AddChild(_gateChipDetail);
    }

    /// <summary>Shows the chip (null hides it). <paramref name="title"/> is the full name with a short lead (“Ahead:”).</summary>
    public void SetGateChip(string? title, string detail, Color color)
    {
        if (title == null)
        {
            _gateChip.Visible = false;
            return;
        }

        _gateChipTitle.Text = title;
        _gateChipTitle.AddThemeColorOverride("font_color", color);
        _gateChipDetail.Text = detail;
        _gateChip.Visible = true;
        PlaceGateChip();
    }

    /// <summary>Centred between the legend and the inspector, in the lower third of the scene (viewport units).</summary>
    private void PlaceGateChip()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        var left = _legendPanel.Visible ? _legendPanel.Position.X + _legendPanel.Size.X + 10 : 10;
        if (_destPanel.Visible) left = Mathf.Max(left, _destPanel.Position.X + _destPanel.Size.X + 10);
        var right = _inspector.Visible ? _inspector.Position.X - 10 : vp.X - 10;
        var room = Mathf.Max(220, right - left);
        var width = Mathf.Min(room, 560);
        _gateChipTitle.CustomMinimumSize = new Vector2(width - 24, 0);
        _gateChip.Size = new Vector2(width, 0);
        _gateChip.Size = _gateChip.GetCombinedMinimumSize() with { X = width };
        var bottom = _hintPanel.Visible ? _hintPanel.Position.Y - 8 : vp.Y - 70;
        if (_toastPanel.Visible) bottom = Mathf.Min(bottom, _toastPanel.Position.Y - 8);
        _gateChip.Position = new Vector2(left + (room - width) / 2, Mathf.Min(vp.Y * 0.72f, bottom - _gateChip.Size.Y));
    }
}
