using Godot;

namespace FileScape.App.Ui;

/// <summary>
/// The Places parts of the HUD (PLACE-02, 04, 06, 07, 10): the "You are in" breadcrumb with Up and Root, the
/// destination list (every child folder, so none is out of reach), the ascent chip that explains what ascent
/// shows, and the threshold transition (fade and caption).
/// </summary>
public partial class Hud
{
    public const string PlacesHintText = "↑↓ walk · ←→ turn · WASD · hold right mouse to look · Space jump · Shift run · T flight · walk through a gate to enter · F1 help";

    public event Action<int>? BreadcrumbRequested;
    public event Action? UpRequested;
    public event Action? RootRequested;
    public event Action<int>? DestinationSelected;
    public event Action<int>? DestinationActivated;
    public event Action? EnterRequested;

    private PanelContainer _placePanel = null!;
    private HBoxContainer _crumbs = null!;
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
        _crumbs = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipContents = true };
        _crumbs.AddThemeConstantOverride("separation", 0);
        row.AddChild(_crumbs);
        _upButton = MakeButton("▲ Up (U)", () => UpRequested?.Invoke(), "Go up to the parent folder (U or PgUp), arriving in front of the gate you came from.");
        _rootButton = MakeButton("⌂ Root", () => RootRequested?.Invoke(), "Back to the folder you chose to explore (Ctrl+Home).");
        row.AddChild(_upButton);
        row.AddChild(_rootButton);
        _placeSummary = new Label();
        _placeSummary.AddThemeFontSizeOverride("font_size", 13);
        _placeSummary.AddThemeColorOverride("font_color", new Color(0.72f, 0.8f, 0.94f));
        v.AddChild(_placeSummary);

        _destPanel = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        _destPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _destPanel.OffsetLeft = 10; _destPanel.OffsetTop = 190; _destPanel.OffsetRight = 320; _destPanel.OffsetBottom = 470;
        _root.AddChild(_destPanel);
        var dv = new VBoxContainer();
        _destPanel.AddChild(dv);
        _destTitle = new Label { Text = "Destinations" };
        _destTitle.AddThemeFontSizeOverride("font_size", 14);
        dv.AddChild(_destTitle);
        // FocusMode None: the list must never take the arrow keys away from walking (NAV-08).
        _destList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None, SelectMode = ItemList.SelectModeEnum.Single };
        _destList.AddThemeFontSizeOverride("font_size", 13);
        // ItemClicked fires on every click; ItemSelected would not fire for an item that is already highlighted.
        _destList.ItemClicked += (i, _, button) => { if (button == (long)MouseButton.Left) DestinationSelected?.Invoke((int)_destList.GetItemMetadata((int)i)); };
        _destList.ItemActivated += i => DestinationActivated?.Invoke((int)_destList.GetItemMetadata((int)i));
        dv.AddChild(_destList);
        var help = new Label { Text = "Click: look at it · double-click: go", HorizontalAlignment = HorizontalAlignment.Center };
        help.AddThemeFontSizeOverride("font_size", 11);
        help.AddThemeColorOverride("font_color", new Color(0.6f, 0.68f, 0.82f));
        dv.AddChild(help);

        _ascentPanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _ascentPanel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _ascentPanel.GrowHorizontal = Control.GrowDirection.Both;
        _ascentPanel.OffsetTop = 240;
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

        _placeSummary.Text = summary;
        _upButton.Disabled = !canUp;
        _rootButton.Disabled = atRoot;
    }

    public void SetDestinations(IReadOnlyList<(int Id, string Text, Color Color, string Tooltip)> items, string title)
    {
        _destList.Clear();
        foreach (var (id, text, color, tip) in items)
        {
            var i = _destList.AddItem(text);
            _destList.SetItemMetadata(i, id);
            _destList.SetItemCustomFgColor(i, color);
            _destList.SetItemTooltip(i, tip);
        }

        _destTitle.Text = title;
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
