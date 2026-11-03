using Godot;

namespace FileScape.App.Ui;

/// <summary>
/// The 2D interface: a top bar, a scan status strip, the inspector, the legend, a help line, the stats overlay, and
/// toasts. It is built in code, so the scene file stays trivial and the layout is reviewable as source. The HUD only
/// raises intent events; Main decides what they do.
/// </summary>
public partial class Hud : CanvasLayer
{
    public event Action? OpenFolderRequested;
    public event Action? DemoRequested;
    public event Action? CancelRequested;
    public event Action<int>? VisualizationRequested;
    public event Action? ResetRequested;
    public event Action? OverviewRequested;
    public event Action? QualityToggled;
    public event Action? OpenRequested;
    public event Action? RevealRequested;
    public event Action? ParentRequested;
    public event Action? FlyToRequested;
    public event Action? CopyPathRequested;
    public event Action? HelpToggled;

    private Control _root = null!;
    private Label _sourceBadge = null!;
    private PanelContainer _sourcePanel = null!;
    private Label _statusLabel = null!;
    private ProgressBar _progress = null!;
    private Button _cancel = null!;
    private Button _quality = null!;
    private readonly Button[] _visButtons = new Button[2];
    private PanelContainer _inspector = null!;
    private RichTextLabel _inspectorBody = null!;
    private Label _inspectorTitle = null!;
    private ColorRect _inspectorSwatch = null!;
    private Button _openButton = null!;
    private Button _revealButton = null!;
    private Button _parentButton = null!;
    private VBoxContainer _legendBox = null!;
    private Label _stats = null!;
    private Label _toast = null!;
    private PanelContainer _toastPanel = null!;
    private double _toastUntil;
    private PanelContainer _help = null!;

    /// <summary>The screen rectangles occupied by the UI, so 3D input and labels can avoid them.</summary>
    public IEnumerable<Rect2> BlockingRects
    {
        get
        {
            foreach (var c in new Control[] { _topBar, _inspector, _legendPanel, _statusPanel, _help })
            {
                if (c.Visible)
                {
                    yield return c.GetGlobalRect();
                }
            }
        }
    }

    private PanelContainer _topBar = null!;
    private PanelContainer _legendPanel = null!;
    private PanelContainer _statusPanel = null!;

    public static Theme BuildTheme()
    {
        var font = new SystemFont
        {
            // Segoe UI covers Latin/Greek/Cyrillic/Hebrew. The others supply CJK, Korean, Indic and emoji, so
            // Unicode names render rather than as tofu (FS-06).
            FontNames = ["Segoe UI", "Segoe UI Emoji", "Segoe UI Symbol", "Yu Gothic UI", "Microsoft YaHei UI", "Malgun Gothic", "Nirmala UI", "Ebrima"],
            Antialiasing = TextServer.FontAntialiasing.Lcd,
            SubpixelPositioning = TextServer.SubpixelPositioning.Auto,
            AllowSystemFallback = true,
        };
        var theme = new Theme { DefaultFont = font, DefaultFontSize = 15 };
        var panel = Panel(new Color(0.035f, 0.045f, 0.075f, 0.86f), new Color(0.35f, 0.5f, 0.85f, 0.35f));
        theme.SetStylebox("panel", "PanelContainer", panel);
        var btn = Panel(new Color(0.1f, 0.13f, 0.2f, 0.95f), new Color(0.4f, 0.55f, 0.9f, 0.45f), 4, 8, 5);
        var btnHover = Panel(new Color(0.16f, 0.21f, 0.33f, 1f), new Color(0.55f, 0.75f, 1f, 0.8f), 4, 8, 5);
        var btnPressed = Panel(new Color(0.22f, 0.32f, 0.55f, 1f), new Color(0.7f, 0.85f, 1f, 0.9f), 4, 8, 5);
        var btnDisabled = Panel(new Color(0.07f, 0.08f, 0.11f, 0.8f), new Color(0.25f, 0.3f, 0.4f, 0.3f), 4, 8, 5);
        theme.SetStylebox("normal", "Button", btn);
        theme.SetStylebox("hover", "Button", btnHover);
        theme.SetStylebox("pressed", "Button", btnPressed);
        theme.SetStylebox("hover_pressed", "Button", btnPressed);
        theme.SetStylebox("disabled", "Button", btnDisabled);
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetColor("font_color", "Button", new Color(0.86f, 0.9f, 1f));
        theme.SetColor("font_disabled_color", "Button", new Color(0.45f, 0.5f, 0.6f));
        theme.SetColor("font_color", "Label", new Color(0.86f, 0.9f, 1f));
        theme.SetColor("default_color", "RichTextLabel", new Color(0.86f, 0.9f, 1f));
        theme.SetStylebox("background", "ProgressBar", Panel(new Color(0.08f, 0.1f, 0.16f, 1f), new Color(0, 0, 0, 0), 3, 0, 0));
        theme.SetStylebox("fill", "ProgressBar", Panel(new Color(0.3f, 0.6f, 1f, 1f), new Color(0, 0, 0, 0), 3, 0, 0));
        return theme;
    }

    private static StyleBoxFlat Panel(Color bg, Color border, int radius = 7, int padX = 12, int padY = 9)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ContentMarginLeft = padX,
            ContentMarginRight = padX,
            ContentMarginTop = padY,
            ContentMarginBottom = padY,
        };
        sb.SetBorderWidthAll(border.A > 0 ? 1 : 0);
        sb.SetCornerRadiusAll(radius);
        return sb;
    }

    public override void _Ready()
    {
        _root = new Control { Theme = BuildTheme(), MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        BuildTopBar();
        BuildStatus();
        BuildInspector();
        BuildLegend();
        BuildHelp();
        BuildStatsAndToast();
    }

    private void BuildTopBar()
    {
        _topBar = new PanelContainer();
        _topBar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _topBar.OffsetLeft = 10; _topBar.OffsetRight = -10; _topBar.OffsetTop = 10;
        _root.AddChild(_topBar);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _topBar.AddChild(row);

        var title = new Label { Text = "FileScape", VerticalAlignment = VerticalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color(0.75f, 0.88f, 1f));
        row.AddChild(title);
        row.AddChild(new VSeparator());
        row.AddChild(MakeButton("Open folder…", () => OpenFolderRequested?.Invoke(), "Choose a folder to scan (metadata only)."));
        row.AddChild(MakeButton("Sample data", () => DemoRequested?.Invoke(), "Load the deterministic synthetic tree (not your files)."));
        _cancel = MakeButton("Cancel scan", () => CancelRequested?.Invoke(), "Stop scanning. The part scanned so far stays explorable.");
        _cancel.Disabled = true;
        row.AddChild(_cancel);
        row.AddChild(new VSeparator());
        var group = new ButtonGroup();
        for (var i = 0; i < 2; i++)
        {
            var index = i;
            _visButtons[i] = new Button
            {
                Text = i == 0 ? "Classic FSN" : "District",
                ToggleMode = true,
                ButtonGroup = group,
                TooltipText = "Switch visualization (Tab). The selection is preserved.",
                FocusMode = Control.FocusModeEnum.None,
            };
            _visButtons[i].Pressed += () => VisualizationRequested?.Invoke(index);
            row.AddChild(_visButtons[i]);
        }

        row.AddChild(new VSeparator());
        row.AddChild(MakeButton("Reset view", () => ResetRequested?.Invoke(), "Recover the home view: root and its first row of children (Home)."));
        row.AddChild(MakeButton("Overview", () => OverviewRequested?.Invoke(), "Bird's-eye view of the whole tree (B)."));
        _quality = MakeButton("Quality: High", () => QualityToggled?.Invoke(), "Toggle effects (F2). Low disables volumetric fog, SSR and SSAO.");
        row.AddChild(_quality);
        row.AddChild(MakeButton("Help", () => HelpToggled?.Invoke(), "Show or hide controls (F1)."));

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _sourcePanel = new PanelContainer();
        _sourceBadge = new Label { Text = "No data", VerticalAlignment = VerticalAlignment.Center };
        _sourceBadge.AddThemeFontSizeOverride("font_size", 14);
        _sourcePanel.AddChild(_sourceBadge);
        row.AddChild(_sourcePanel);
    }

    private void BuildStatus()
    {
        _statusPanel = new PanelContainer();
        _statusPanel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _statusPanel.OffsetLeft = 10; _statusPanel.OffsetRight = -390; _statusPanel.OffsetTop = 70;
        _root.AddChild(_statusPanel);
        var v = new VBoxContainer();
        _statusPanel.AddChild(v);
        _statusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.Off, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _statusLabel.AddThemeFontSizeOverride("font_size", 13);
        v.AddChild(_statusLabel);
        _progress = new ProgressBar { CustomMinimumSize = new Vector2(0, 6), ShowPercentage = false, MaxValue = 1 };
        v.AddChild(_progress);
    }

    private void BuildInspector()
    {
        _inspector = new PanelContainer { CustomMinimumSize = new Vector2(370, 0) };
        _inspector.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        _inspector.OffsetLeft = -380; _inspector.OffsetRight = -10; _inspector.OffsetTop = 70; _inspector.OffsetBottom = -10;
        _root.AddChild(_inspector);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        _inspector.AddChild(v);

        var head = new HBoxContainer();
        _inspectorSwatch = new ColorRect { CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        head.AddChild(_inspectorSwatch);
        _inspectorTitle = new Label { Text = "Nothing selected", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _inspectorTitle.AddThemeFontSizeOverride("font_size", 19);
        head.AddChild(_inspectorTitle);
        v.AddChild(head);

        _inspectorBody = new RichTextLabel
        {
            BbcodeEnabled = true,
            SelectionEnabled = true,
            ScrollActive = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Text = "Click a pedestal or block to inspect it.",
        };
        _inspectorBody.AddThemeFontSizeOverride("normal_font_size", 14);
        _inspectorBody.AddThemeFontSizeOverride("bold_font_size", 14);
        _inspectorBody.AddThemeFontSizeOverride("mono_font_size", 13);
        v.AddChild(_inspectorBody);

        var buttons = new GridContainer { Columns = 2 };
        buttons.AddThemeConstantOverride("h_separation", 6);
        buttons.AddThemeConstantOverride("v_separation", 6);
        _openButton = MakeButton("Open (Enter)", () => OpenRequested?.Invoke(), "Open with the Windows-associated application. Asks first for programs and scripts.");
        _revealButton = MakeButton("Show in Explorer", () => RevealRequested?.Invoke(), "Open Explorer with this item selected.");
        _parentButton = MakeButton("Parent (Esc)", () => ParentRequested?.Invoke(), "Select the containing directory.");
        var fly = MakeButton("Fly to (F)", () => FlyToRequested?.Invoke(), "Bring the selection into view.");
        var copy = MakeButton("Copy path", () => CopyPathRequested?.Invoke(), "Copy the full path to the clipboard.");
        foreach (var b in new[] { _openButton, _revealButton, _parentButton, fly, copy })
        {
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            buttons.AddChild(b);
        }

        v.AddChild(buttons);
        SetSelectionActions(false, false, false);
    }

    private void BuildLegend()
    {
        _legendPanel = new PanelContainer();
        _legendPanel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _legendPanel.GrowVertical = Control.GrowDirection.Begin;
        _legendPanel.OffsetLeft = 10; _legendPanel.OffsetBottom = -10;
        _root.AddChild(_legendPanel);
        _legendBox = new VBoxContainer();
        _legendBox.AddThemeConstantOverride("separation", 2);
        _legendPanel.AddChild(_legendBox);
    }

    private void BuildHelp()
    {
        _help = new PanelContainer { Visible = false };
        _help.SetAnchorsPreset(Control.LayoutPreset.Center);
        _help.GrowHorizontal = Control.GrowDirection.Both;
        _help.GrowVertical = Control.GrowDirection.Both;
        _root.AddChild(_help);
        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            CustomMinimumSize = new Vector2(560, 0),
            Text =
                "[b]Controls[/b]\n" +
                "[b]Click[/b] select and inspect      [b]Double-click[/b] fly to\n" +
                "[b]Left-drag[/b] orbit      [b]Right/middle-drag[/b] pan      [b]Wheel[/b] zoom\n" +
                "[b]W A S D[/b] move      [b]Q / E[/b] down / up      [b]Shift[/b] faster\n" +
                "[b]Enter[/b] open with Windows (asks first for programs)\n" +
                "[b]Backspace[/b] back      [b]Esc[/b] parent directory      [b]F[/b] fly to selection\n" +
                "[b]Home[/b] reset view      [b]B[/b] bird's-eye overview      [b]Tab[/b] switch visualization\n" +
                "[b]F1[/b] help      [b]F2[/b] quality      [b]F3[/b] performance stats\n\n" +
                "Nothing is opened or run unless you press Open. FileScape only reads metadata.",
        };
        _help.AddChild(text);
    }

    private void BuildStatsAndToast()
    {
        _stats = new Label { Visible = false };
        _stats.AddThemeFontSizeOverride("font_size", 12);
        _stats.AddThemeColorOverride("font_color", new Color(0.6f, 1f, 0.75f));
        _stats.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _stats.AddThemeConstantOverride("outline_size", 4);
        _stats.Position = new Vector2(14, 120);
        _root.AddChild(_stats);

        _toastPanel = new PanelContainer { Visible = false };
        _toastPanel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _toastPanel.GrowHorizontal = Control.GrowDirection.Both;
        _toastPanel.GrowVertical = Control.GrowDirection.Begin;
        _toastPanel.OffsetBottom = -60;
        _root.AddChild(_toastPanel);
        _toast = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(520, 0), HorizontalAlignment = HorizontalAlignment.Center };
        _toastPanel.AddChild(_toast);
    }

    private static Button MakeButton(string text, Action onPress, string tooltip)
    {
        var b = new Button { Text = text, TooltipText = tooltip, FocusMode = Control.FocusModeEnum.None };
        b.Pressed += onPress;
        return b;
    }

    public override void _Process(double delta)
    {
        if (_toastPanel.Visible && Time.GetTicksMsec() / 1000.0 > _toastUntil)
        {
            _toastPanel.Visible = false;
        }
    }

    public void SetSource(string text, bool live, string tooltip)
    {
        _sourceBadge.Text = text;
        _sourcePanel.TooltipText = tooltip;
        var sb = (StyleBoxFlat)_root.Theme.GetStylebox("panel", "PanelContainer").Duplicate();
        sb.BgColor = live ? new Color(0.03f, 0.22f, 0.12f, 0.92f) : new Color(0.3f, 0.2f, 0.02f, 0.92f);
        sb.BorderColor = live ? new Color(0.3f, 1f, 0.55f, 0.8f) : new Color(1f, 0.75f, 0.2f, 0.8f);
        sb.ContentMarginTop = 4; sb.ContentMarginBottom = 4;
        _sourcePanel.AddThemeStyleboxOverride("panel", sb);
        _sourceBadge.AddThemeColorOverride("font_color", live ? new Color(0.7f, 1f, 0.8f) : new Color(1f, 0.88f, 0.55f));
    }

    public void SetStatus(string text, double progress01, bool scanning, Color? tint = null)
    {
        _statusLabel.Text = text;
        _statusLabel.AddThemeColorOverride("font_color", tint ?? new Color(0.8f, 0.86f, 0.95f));
        _progress.Value = progress01;
        _progress.Visible = scanning;
        _cancel.Disabled = !scanning;
    }

    public void SetVisualization(int index) => _visButtons[index].SetPressedNoSignal(true);

    public void SetQuality(string text) => _quality.Text = "Quality: " + text;

    public void SetInspector(string title, string bbcode, Color swatch)
    {
        _inspectorTitle.Text = title;
        _inspectorBody.Text = bbcode;
        _inspectorSwatch.Color = swatch;
    }

    public void SetSelectionActions(bool canOpen, bool canReveal, bool canParent)
    {
        _openButton.Disabled = !canOpen;
        _revealButton.Disabled = !canReveal;
        _parentButton.Disabled = !canParent;
    }

    public void SetLegend(string colorMeaning, IReadOnlyList<(Color Color, string Label)> entries, string heightMeaning)
    {
        foreach (var c in _legendBox.GetChildren())
        {
            c.QueueFree();
        }

        var title = new Label { Text = colorMeaning };
        title.AddThemeFontSizeOverride("font_size", 13);
        _legendBox.AddChild(title);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 14);
        foreach (var (color, label) in entries)
        {
            grid.AddChild(Swatch(color, label));
        }

        _legendBox.AddChild(grid);
        var h = new Label { Text = heightMeaning, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(330, 0) };
        h.AddThemeFontSizeOverride("font_size", 12);
        h.AddThemeColorOverride("font_color", new Color(0.7f, 0.76f, 0.88f));
        _legendBox.AddChild(h);
        var markers = new GridContainer { Columns = 2 };
        markers.AddThemeConstantOverride("h_separation", 14);
        markers.AddChild(Swatch(Visual.Palette.Problem, "access denied / error"));
        markers.AddChild(Swatch(Visual.Palette.Vanished, "vanished during scan"));
        markers.AddChild(Swatch(Visual.Palette.LinkCyan, "link (not followed)"));
        markers.AddChild(Swatch(Visual.Palette.Cloud, "cloud placeholder"));
        markers.AddChild(Swatch(Visual.Palette.Incomplete, "not scanned / partial"));
        markers.AddChild(Swatch(new Color(0.35f, 0.35f, 0.4f), "hidden / system (dimmed)"));
        _legendBox.AddChild(markers);
    }

    private static Control Swatch(Color color, string label)
    {
        var row = new HBoxContainer();
        row.AddChild(new ColorRect { Color = color, CustomMinimumSize = new Vector2(11, 11), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        var l = new Label { Text = label };
        l.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(l);
        return row;
    }

    public void SetStats(string text, bool visible)
    {
        _stats.Visible = visible;
        _stats.Text = text;
    }

    public bool StatsVisible => _stats.Visible;

    public void ToggleHelp() => _help.Visible = !_help.Visible;

    public void Toast(string text, Color? color = null, double seconds = 4.5)
    {
        _toast.Text = text;
        _toast.AddThemeColorOverride("font_color", color ?? new Color(0.9f, 0.95f, 1f));
        _toastPanel.Visible = true;
        _toastUntil = Time.GetTicksMsec() / 1000.0 + seconds;
    }

    public Theme UiTheme => _root.Theme;
    public Control UiRoot => _root;
}
