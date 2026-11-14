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
    public event Action? OpenLocationRequested;
    public event Action? DemoRequested;
    public event Action? CancelRequested;
    /// <summary>0 = Places, 1 = Classic FSN, 2 = District (AD-15).</summary>
    public event Action<int>? ExperienceRequested;
    public event Action? ResetRequested;
    public event Action? OverviewRequested;
    public event Action? QualityToggled;
    public event Action? OpenRequested;
    public event Action? RevealRequested;
    public event Action? ParentRequested;
    public event Action? FlyToRequested;
    public event Action? CopyPathRequested;
    public event Action? HelpToggled;
    public event Action? FlightToggled;

    /// <summary>The × on the inspector: the user is done with the selection (Places hides the card).</summary>
    public event Action? InspectorClosed;

    private Control _root = null!;
    private Label _sourceBadge = null!;
    private PanelContainer _sourcePanel = null!;
    private Label _statusLabel = null!;
    private ProgressBar _progress = null!;
    private Button _cancel = null!;
    private Button _quality = null!;
    private readonly Button[] _visButtons = new Button[4];
    private Button _homeButton = null!, _overviewButton = null!, _flightButton = null!;
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
            foreach (var c in new Control[] { _topBar, _inspector, _legendPanel, _statusPanel, _help, _settingsPanel, _placePanel, _destPanel, _locPanel, _problemPanel, _notePanel, _flyPanel, _ascentPanel, _hintPanel, _gateChip })
            {
                if (c.Visible)
                {
                    yield return c.GetGlobalRect();
                }
            }
        }
    }

    private PanelContainer _topBar = null!;
    private Button _keyButton = null!;

    /// <summary>The key (legend) is an on-demand panel in Places (DJ: it hid a quarter of the view); the maps show it by default.</summary>
    private bool _legendInPlaces, _legendInMaps = true;

    private bool _placesMode = true;
    private bool _scanning;
    public bool LegendVisible => _legendPanel.Visible;
    public Rect2 KeyButtonRect => _keyButton.GetGlobalRect();

    /// <summary>Shows or hides the key for the current experience (K, the Key button). Remembered per experience for this session.</summary>
    public void ToggleLegend()
    {
        if (_placesMode) _legendInPlaces = !_legendInPlaces; else _legendInMaps = !_legendInMaps;
        ApplyLegendVisibility();
    }

    private void ApplyLegendVisibility()
    {
        _legendPanel.Visible = _placesMode ? _legendInPlaces : _legendInMaps;
        _keyButton.SetPressedNoSignal(_legendPanel.Visible);
    }
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
        // Opaque enough that bright in-world signs behind a panel do not read through it (VISI-01, 2026-11-05: 0.86 → 0.93).
        var panel = Panel(new Color(0.035f, 0.045f, 0.075f, 0.93f), new Color(0.35f, 0.5f, 0.85f, 0.35f));
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
        BuildHintAndFlyChip();
        BuildPlaces();
        BuildGateChip();
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
        row.AddChild(MakeButton("Open…", () => OpenLocationRequested?.Invoke(), "Open a location (Ctrl+O): This computer (all drives and WSL distributions), a drive, a path, or browse for a folder."));
        row.AddChild(MakeButton("Sample data", () => DemoRequested?.Invoke(), "Load the deterministic synthetic tree (not your files)."));
        _cancel = MakeButton("Cancel scan", () => CancelRequested?.Invoke(), "Stop scanning. The part scanned so far stays explorable.");
        _cancel.Disabled = true;
        _cancel.Visible = false; // shown only while a scan runs: the top bar must fit at 125 % UI scale
        row.AddChild(_cancel);
        row.AddChild(new VSeparator());
        var group = new ButtonGroup();
        // The planetary world (AD-39) comes first: it is the new experience, easy to find; Places stays beside it to compare.
        _visButtons[3] = new Button
        {
            Text = "✦ Planets (V)",
            ToggleMode = true,
            ButtonGroup = group,
            TooltipText = "Planets: each folder is a small world you can run round. Its folders hang in the sky above launchers; files stand in groves. Same folder, selection and history as Places (V switches).",
            FocusMode = Control.FocusModeEnum.None,
        };
        _visButtons[3].AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.55f));
        _visButtons[3].AddThemeColorOverride("font_pressed_color", new Color(1f, 0.93f, 0.7f));
        _visButtons[3].Pressed += () => ExperienceRequested?.Invoke(3);
        row.AddChild(_visButtons[3]);
        string[] names = ["Places", "Classic FSN", "District"];
        string[] tips =
        [
            "Places: be inside a folder — files around you, child folders as gates you can walk through (Tab / M).",
            "Classic FSN: whole-tree reference map, FSN-inspired (Tab). The selection is preserved.",
            "District: whole-tree reference map, futuristic city (Tab). The selection is preserved.",
        ];
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            _visButtons[index] = new Button
            {
                Text = names[i],
                ToggleMode = true,
                ButtonGroup = group,
                TooltipText = tips[i],
                FocusMode = Control.FocusModeEnum.None,
            };
            _visButtons[i].Pressed += () => ExperienceRequested?.Invoke(index);
            row.AddChild(_visButtons[i]);
            if (i == 0) row.AddChild(new Label { Text = " maps:", VerticalAlignment = VerticalAlignment.Center });
        }

        row.AddChild(new VSeparator());
        _homeButton = MakeButton("Home", () => ResetRequested?.Invoke(), "Back to the arrival point (Home).");
        row.AddChild(_homeButton);
        _overviewButton = MakeButton("Overview", () => OverviewRequested?.Invoke(), "Bird's-eye view of the whole tree (B).");
        row.AddChild(_overviewButton);
        _flightButton = MakeButton("✈ Flight (T)", () => FlightToggled?.Invoke(), FlightTooltip);
        _flightButton.ToggleMode = true;
        row.AddChild(_flightButton);
        // Quality lives in Settings (F2 still toggles it): the top bar must fit at 125 % UI scale with the Key button.
        _quality = MakeButton("Quality: High", () => QualityToggled?.Invoke(), "Toggle effects (F2). Low disables volumetric fog, SSR and SSAO.");
        _keyButton = MakeButton("Key (K)", () => ToggleLegend(), "What colours, heights and gate looks mean. Hidden by default in Places so the world stays in view.");
        _keyButton.ToggleMode = true;
        row.AddChild(_keyButton);
        BuildSettings(row);
        var help = MakeButton("Help (F1)", () => HelpToggled?.Invoke(), "Show or hide the controls (F1).");
        help.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.6f));
        row.AddChild(help);

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _sourcePanel = new PanelContainer();
        // The badge may be cut short (full text in its tooltip) so the top bar never grows past the window at 125 % UI scale.
        _sourceBadge = new Label { Text = "No data", VerticalAlignment = VerticalAlignment.Center, ClipText = true, CustomMinimumSize = new Vector2(90, 0), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
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
        _inspectorIcon = new TextureRect { CustomMinimumSize = new Vector2(26, 26), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Visible = false };
        head.AddChild(_inspectorIcon);
        _inspectorSwatch = new ColorRect { CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        head.AddChild(_inspectorSwatch);
        _inspectorTitle = new Label { Text = "Nothing selected", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _inspectorTitle.AddThemeFontSizeOverride("font_size", 19);
        head.AddChild(_inspectorTitle);
        _inspectorClose = MakeButton("×", () => InspectorClosed?.Invoke(), "Close (Esc). Nothing moves; click something to inspect it again.");
        _inspectorClose.Flat = true;
        _inspectorClose.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        head.AddChild(_inspectorClose);
        v.AddChild(head);

        // Places: a compact card (a one-line brief and the actions); the full details open on demand (DJ: panels on demand).
        _inspectorBrief = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        _inspectorBrief.AddThemeFontSizeOverride("font_size", 14);
        _inspectorBrief.AddThemeColorOverride("font_color", new Color(0.78f, 0.85f, 0.97f));
        v.AddChild(_inspectorBrief);
        _inspectorDetails = MakeButton("Details ▸", () => { _inspectorExpanded = !_inspectorExpanded; ApplyInspectorMode(); }, "Show or hide the full details: sizes, dates, flags, link target, why a folder could not be listed.");
        _inspectorDetails.Flat = true;
        _inspectorDetails.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _inspectorDetails.Visible = false;
        v.AddChild(_inspectorDetails);
        BuildProjectCard(v, _inspectorBrief.GetIndex());

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

    private TextureRect _inspectorIcon = null!;
    private Label _inspectorBrief = null!;
    private Button _inspectorDetails = null!, _inspectorClose = null!;
    private bool _inspectorCompact, _inspectorExpanded, _inspectorWanted = true;

    public bool InspectorVisible => _inspector.Visible;
    public Rect2 InspectorRect => _inspector.GetGlobalRect();
    public Rect2 InspectorDetailsRect => _inspectorDetails.GetGlobalRect();
    public bool InspectorExpanded => _inspectorExpanded;
    public string InspectorTitle => _inspectorTitle.Text;
    public string InspectorBrief => _inspectorBrief.Text;

    /// <summary>
    /// Places: the inspector is a card shown only while something is selected, sized to its content, with the full
    /// details behind "Details ▸" (VIEW-02). The maps keep the full-height panel.
    /// </summary>
    public void SetInspectorCard(bool compact, bool visible, string brief, Texture2D? icon)
    {
        _inspectorCompact = compact;
        _inspectorWanted = visible;
        _inspectorBrief.Text = brief;
        _inspectorIcon.Texture = icon;
        _inspectorIcon.Visible = icon != null;
        _inspectorSwatch.Visible = icon == null;
        ApplyInspectorMode();
    }

    private void ApplyInspectorMode()
    {
        _inspector.Visible = _inspectorWanted;
        _inspectorClose.Visible = _inspectorCompact;
        _inspectorBrief.Visible = _inspectorCompact && _inspectorBrief.Text.Length > 0;
        _inspectorDetails.Visible = _inspectorCompact;
        _inspectorDetails.Text = _inspectorExpanded ? "Details ▾" : "Details ▸";
        _inspectorBody.Visible = !_inspectorCompact || _inspectorExpanded;
        _inspectorBody.FitContent = _inspectorCompact;
        _inspectorBody.SizeFlagsVertical = _inspectorCompact ? Control.SizeFlags.Fill : Control.SizeFlags.ExpandFill;
        _inspector.SetAnchorsPreset(_inspectorCompact ? Control.LayoutPreset.TopRight : Control.LayoutPreset.RightWide, keepOffsets: false);
        _inspector.GrowHorizontal = Control.GrowDirection.Begin;
        _inspector.OffsetLeft = -380; _inspector.OffsetRight = -10; _inspector.OffsetTop = 70;
        if (!_inspectorCompact) _inspector.OffsetBottom = -10;
        else _inspector.Size = _inspector.Size with { Y = 0 }; // shrink to content (recomputed in _Process)
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
                "[b]✦ Planets[/b] (V switches with Places; same folder, selection and history)\n" +
                "  Each folder is a small world; its folders hang in the sky above flower launchers; files stand in groves.\n" +
                "  [b]W A S D[/b] (or ↑ ↓) run, Shift faster · [b]drag[/b] (left or right mouse) or [b]← →[/b] look around, also while running · wheel zoom\n" +
                "  [b]Space[/b] jump, again in the air to twirl higher · on a launcher: Space flies there · [b]T[/b] overview of every known folder\n" +
                "  [b]E[/b] look closer (preview) · [b]Enter[/b] open with Windows · [b]Backspace[/b] back · U up · G fly to the selected world\n" +
                "  [b]J[/b] this world's list (every folder and file, filter by typing) · Ctrl+L go to a path · N sound\n\n" +
                "[b]Places[/b] (the flat world: you are [i]inside[/i] a folder, on foot)\n" +
                "Files stand around you as towers, grouped by type, each with its type's icon. Each child folder is a [b]gate[/b], named above it;\n" +
                "    [b]walk through a gate[/b] to go in; the [b]▲ Up[/b] gate behind you goes back. Going up arrives facing the next ▲ gate.\n" +
                "[b]Walking[/b]   [b]↑ / ↓[/b] walk forward / back    [b]← / →[/b] turn    [b]W A S D[/b] walk and step sideways    hold [b]Shift[/b] run\n" +
                "    hold [b]right mouse[/b] (or left-drag) to look; letting go levels the view (on foot only)    [b]Space[/b] jump: hold for a high jump, tap for a hop\n" +
                "[b]Flight[/b]   [b]T[/b]: rise 16 m and look down across the place; [b]T[/b] again lands you safely on foot (the view levels)\n" +
                "    [b]W A S D[/b] fly level (looking down never makes you dive)    [b]E[/b] / [b]Space[/b] up    [b]Q[/b] / [b]C[/b] down    [b]wheel[/b] speed (faster the higher you are)\n" +
                "    the view stays where you leave it    [b]B[/b] aerial view: this place, its parent and its child places from above\n" +
                "[b]The aerial world[/b]   Above 14 m every folder you know is an island: roads lead to child folders, cyan arcs are links,\n" +
                "    a warm ring marks places you have visited, a gold beacon where you are. Fly over one and press [b]T[/b] to land there,\n" +
                "    or click it and press [b]G[/b] to fly there. Places near you inside the folder you opened are listed as you fly;\n" +
                "    others stay \"not listed yet\" until you go there (nothing scans a drive). Distances between places are symbolic.\n" +
                "[b]Panels on demand[/b]   [b]J[/b] Destinations (type to filter, ↑↓ Enter)    [b]K[/b] key (colours and heights)    [b]F4[/b] settings    [b]Esc[/b] closes\n" +
                "[b]G[/b]  go into the selected folder or link      [b]U[/b] / [b]PgUp[/b]  up to the parent      [b]Backspace[/b]  back (also back through a link)\n" +
                "[b]Home[/b]  this folder's arrival point      [b]Ctrl+Home[/b]  back to the arrival root      [b]M[/b]  map      [b]Tab[/b]  next view\n" +
                "[b]L[/b] or [b]Ctrl+O[/b]  open a location: [b]⌂ This computer[/b] (This PC: every drive and WSL distribution), a drive, Browse…      [b]Ctrl+L[/b]  type a path (\"This PC\" works)\n" +
                "[b]H[/b]  show / hide hidden and system items      Gates marked [b]⌂[/b] home, [b]◆[/b] Downloads, Pictures…, [b]⚙[/b] projects, [b]★[/b] your pins (the card says why)\n" +
                "[b]P[/b]  pin or unpin the selected folder (or the one you are in)      [b]R[/b]  read this project's README (as text; nothing is run or fetched)\n" +
                "    A project's card offers the editors on this computer (a Windows solution's own app, VS Code, VS Code for WSL); FileScape asks first.\n" +
                "    Gates show [b]queued[/b] or [b]listing now…[/b] while nearby places are listed ahead; /proc, /sys, /dev, /run and /mnt are never listed ahead.\n" +
                "Selecting something never moves you: [b]click[/b] inspects (a card with Details ▸), travel is always deliberate.\n\n" +
                "[b]Moving around in the maps (Classic FSN, District)[/b]\n" +
                "[b]W A S D[/b] or [b]arrow keys[/b]  move      [b]Q / E[/b]  down / up      hold [b]Shift[/b]  faster\n" +
                "[b]Hold right mouse[/b]  fly: look around with the mouse while W A S D / arrows move you\n" +
                "    where you look; the [b]wheel[/b] changes flight speed; release to stop (or [b]Esc[/b])\n" +
                "[b]Left-drag[/b]  orbit around the view centre      [b]Middle-drag[/b]  pan\n" +
                "[b]Wheel[/b]  zoom in / out (while flying: flight speed)\n" +
                "[b]Home[/b]  back to the start view      [b]B[/b]  bird's-eye overview\n\n" +
                "[b]Exploring[/b]\n" +
                "[b]Click[/b]  select and inspect      [b]Double-click[/b] or [b]F[/b]  fly to the selection\n" +
                "[b]Esc[/b]  parent directory (in Places: back to the folder itself)      [b]Backspace[/b]  previous selection (Places: previous place)\n" +
                "[b]Enter[/b]  open with Windows (asks first for programs, links and cloud files)\n" +
                "[b]Tab[/b]  cycle Places / Classic FSN / District (the selection stays)\n" +
                "[b]F1[/b]  this help      [b]F2[/b]  quality      [b]F3[/b]  performance stats      [b]F4[/b]  settings      [b]K[/b]  key\n\n" +
                "Right-drag used to pan; pan is now on the middle button.\n" +
                "Nothing is opened or run unless you press Open. FileScape only reads metadata.",
        };
        _help.AddChild(text);
        _helpText = text;
    }

    private RichTextLabel _helpText = null!;

    /// <summary>Shows which build is running at the end of Help (delivery traceability).</summary>
    public void SetBuildIdentifier(string id) => _helpText.Text += $"\n\n[color=#7f8fab]Build: {id}[/color]";

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
        var nowS = Time.GetTicksMsec() / 1000.0;
        if (_hintPanel.Visible && nowS - _hintShownAt > 60) DismissHint();
        _flyLabel.AddThemeColorOverride("font_color", nowS < _flyHighlightUntil ? new Color(1f, 0.92f, 0.5f) : new Color(0.75f, 0.95f, 1f));
        if (_toastPanel.Visible && Time.GetTicksMsec() / 1000.0 > _toastUntil)
        {
            _toastPanel.Visible = false;
        }

        // The left column stacks: the place panel grows (connection, problems), Destinations and chips follow it, and
        // Destinations stops above the legend; the hint sits between the legend and the inspector (VISI-01). All in
        // viewport units, so it holds at any UI scale (checked at 1.0 and 1.25).
        // Places: the status strip shows only while something is being scanned; its text is the place bar's tooltip.
        _statusPanel.Visible = !_placesMode || _scanning;
        _placePanel.OffsetTop = _statusPanel.Visible ? 112 : 70;
        if (_inspector.Visible && _inspectorCompact)
        {
            // The card is as tall as its content, never taller than the window allows (it scrolls then).
            var vpH = GetViewport().GetVisibleRect().Size.Y;
            var body = _inspectorBody.Visible ? _inspectorBody.GetContentHeight() : 0;
            _inspectorBody.CustomMinimumSize = new Vector2(0, Mathf.Min(body, vpH * 0.55f));
            _inspectorBody.ScrollActive = body > vpH * 0.55f;
            _inspector.Size = _inspector.Size with { Y = 0 };
        }

        if (_placePanel.Visible)
        {
            _crumbScroll.ScrollHorizontal = 1 << 30; // clamped to the end: the current folder stays visible
            var bottom = _placePanel.Position.Y + _placePanel.Size.Y;
            _destPanel.OffsetTop = bottom + 8;
            var legendTop = _legendPanel.Visible ? _legendPanel.Position.Y : GetViewport().GetVisibleRect().Size.Y - 10;
            // The drawer is on demand, so when open it may use most of the column (wide folders have hundreds of entries).
            _destPanel.OffsetBottom = Mathf.Max(_destPanel.OffsetTop + 160, Mathf.Min(_destPanel.OffsetTop + 520, legendTop - 8));
            // The place bar is only as wide as it needs, up to the inspector's column.
            var vpW = GetViewport().GetVisibleRect().Size.X;
            _placePanel.OffsetRight = -Mathf.Max(390, vpW - 10 - 1000);
            _flyPanel.OffsetTop = bottom + 8;
            if (_ascentPanel.Visible && _hintPanel.Visible) DismissHint(); // one bottom line at a time
            _notePanel.OffsetTop = bottom + 96;
            _problemPanel.OffsetTop = bottom + 8;
        }

        if (_hintPanel.Visible)
        {
            var vp = GetViewport().GetVisibleRect().Size;
            var left = _legendPanel.Visible ? _legendPanel.Position.X + _legendPanel.Size.X + 10 : 10;
            var right = _inspector.Visible ? _inspector.Position.X - 10 : vp.X - 10;
            var room = Mathf.Max(200, right - left);
            _hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _hintLabel.CustomMinimumSize = new Vector2(Mathf.Min(room - 24, _hintLabel.GetThemeFont("font").GetStringSize(_hintLabel.Text, HorizontalAlignment.Left, -1, 14).X + 4), 0);
            _hintPanel.Size = _hintPanel.GetCombinedMinimumSize();
            _hintPanel.Position = new Vector2(left + (room - _hintPanel.Size.X) / 2, vp.Y - _hintPanel.Size.Y - 14);        }
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
        _cancel.Visible = scanning;
        _scanning = scanning;
        _placePanel.TooltipText = text;
    }

    /// <summary>Shows which experience is active and adapts the toolbar wording (AD-15).</summary>
    public void SetExperience(int experience)
    {
        for (var i = 0; i < _visButtons.Length; i++) _visButtons[i].SetPressedNoSignal(i == experience);
        var planets = experience == 3;
        var places = experience == 0 || planets;
        _homeButton.TooltipText = places ? "Back to this folder's arrival point (Home). Ctrl+Home: back to the chosen root." : "Recover the home view: root and its first row of children (Home).";
        _overviewButton.Text = places ? "Ascend (B)" : "Overview (B)";
        _overviewButton.TooltipText = places ? "Rise above this place to see its neighbourhood. The whole filesystem is on the maps (M)." : "Bird's-eye view of the whole tree (B).";
        _placePanel.Visible = places;
        _placesMode = places;
        _destPanel.Visible = places && _destOpen;
        ApplyLegendVisibility();
        if (!places) SetInspectorCard(false, true, "", null);
        if (!places) SetAscentChip(null);
        _hintLabel.Text = planets ? PlanetsHintText : places ? PlacesHintText : HintText;
        _homeButton.Visible = true;
        _overviewButton.Visible = !planets;
        _flightButton.Visible = true;
        if (planets) SetPlanetsOverview(open: false);
    }

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

    public void SetLegend(string colorMeaning, IReadOnlyList<(Color Color, string Label)> entries, string heightMeaning, bool markers = true)
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
        if (!markers)
        {
            return; // Places states its gate colours in the entries themselves
        }

        var markerGrid = new GridContainer { Columns = 2 };
        markerGrid.AddThemeConstantOverride("h_separation", 14);
        markerGrid.AddChild(Swatch(Visual.Palette.Problem, "access denied / error"));
        markerGrid.AddChild(Swatch(Visual.Palette.Vanished, "vanished during scan"));
        markerGrid.AddChild(Swatch(Visual.Palette.LinkCyan, "link (not followed)"));
        markerGrid.AddChild(Swatch(Visual.Palette.Cloud, "cloud placeholder"));
        markerGrid.AddChild(Swatch(Visual.Palette.Incomplete, "not scanned / partial"));
        markerGrid.AddChild(Swatch(new Color(0.35f, 0.35f, 0.4f), "hidden / system (dimmed)"));
        _legendBox.AddChild(markerGrid);
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

    public void ToggleHelp()
    {
        _help.Visible = !_help.Visible;
        if (_help.Visible) DismissHint();
    }

    public bool HelpVisible => _help.Visible;

    /// <summary>The on-screen rectangle of a visualization button (for input automation that clicks the real toolbar).</summary>
    public Rect2 VisualizationButtonRect(int mapIndex) => _visButtons[mapIndex + 1].GetGlobalRect();

    public Rect2 ExperienceButtonRect(int experience) => _visButtons[experience].GetGlobalRect();

    // ------------------------------------------------------------ hint and flight status

    private PanelContainer _hintPanel = null!;
    private Label _hintLabel = null!;
    private PanelContainer _flyPanel = null!;
    private Label _flyLabel = null!;
    private double _hintShownAt;
    private bool _movedOnce, _flewOnce;
    private double _flyHighlightUntil;

    /// <summary>The first-run hint. It is dismissed after the user has both moved and flown, after 60 s, or when Help opens.</summary>
    public const string PlanetsHintText = "WASD run · drag or ← → look · Space jump (again: twirl) · on a launcher: Space to fly · E look closer · Enter open · Backspace back · T overview · J list · N sound · F1 help";

    public const string HintText = "W / ↑ walk  ·  Shift run  ·  Space jump  ·  hold right mouse to look  ·  F4 settings (look direction, speed)  ·  F1 help";

    private void BuildHintAndFlyChip()
    {
        _hintPanel = new PanelContainer();
        _hintPanel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _hintPanel.GrowHorizontal = Control.GrowDirection.Both;
        _hintPanel.GrowVertical = Control.GrowDirection.Begin;
        _hintPanel.OffsetBottom = -14;
        _hintPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _hintLabel = new Label { Text = HintText, HorizontalAlignment = HorizontalAlignment.Center };
        _hintLabel.AddThemeFontSizeOverride("font_size", 14);
        _hintLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.94f, 1f));
        _hintPanel.AddChild(_hintLabel);
        _root.AddChild(_hintPanel);
        _hintShownAt = Time.GetTicksMsec() / 1000.0;

        _flyPanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _flyPanel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _flyPanel.GrowHorizontal = Control.GrowDirection.Both;
        _flyPanel.OffsetTop = 196; // below the Places breadcrumb panel
        _flyLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _flyLabel.AddThemeFontSizeOverride("font_size", 14);
        _flyPanel.AddChild(_flyLabel);
        _root.AddChild(_flyPanel);
    }

    public bool HintVisible => _hintPanel.Visible;
    public bool FlyChipVisible => _flyPanel.Visible;
    public string FlyChipText => _flyLabel.Text;

    public void DismissHint() => _hintPanel.Visible = false;

    /// <summary>Records that the user moved or flew. The hint goes once they have done both.</summary>
    public void NoteInteraction(bool flew)
    {
        if (flew) _flewOnce = true; else _movedOnce = true;
        if (_movedOnce && _flewOnce) DismissHint();
    }

    public void SetFlyStatus(bool flying, float speed, bool highlight, bool places = false, bool landing = false, float? altitude = null, bool alongGaze = false)
    {
        _flyPanel.Visible = flying;
        // The flight reminder (FLY-03): what moves you level, what moves you up and down, how to land.
        var move = alongGaze ? "W where you look" : "W A S D level";
        _flyLabel.Text = places
            ? landing ? "LANDING…" : $"✈ {(altitude is { } h ? $"{h:0} m up · " : "")}{speed:0} m/s  ·  {move}  ·  E/Space up · Q/C down  ·  B aerial view  ·  T land"
            : $"FLYING  ·  speed {speed:0.#} u/s (wheel to change, Shift ×3)  ·  release right mouse or Esc to stop";
        if (highlight) _flyHighlightUntil = Time.GetTicksMsec() / 1000.0 + 1.2;
    }

    /// <summary>The Flight toggle: shown in Places only, pressed while flying.</summary>
    public void SetFlightMode(bool places, bool flying, bool landing)
    {
        _flightButton.Visible = places;
        _flightButton.SetPressedNoSignal(flying);
        _flightButton.Text = flying ? (landing ? "✈ Landing…" : "✈ Land (T)") : "✈ Flight (T)";
        _flightButton.TooltipText = FlightTooltip; // the planets reuse this button for the overview
    }

    private const string FlightTooltip = "Equip flight: rise out of the place and fly freely (T). Press again to land safely on foot.";

    /// <summary>On the planets the flight button is the overview (2026-11-11).</summary>
    public void SetPlanetsOverview(bool open)
    {
        _flightButton.Visible = true;
        _flightButton.SetPressedNoSignal(open);
        _flightButton.Text = open ? "✧ Back down (T)" : "✧ Overview (T)";
        _flightButton.TooltipText = "The overview: zoom out to every folder FileScape knows, in 3D. Click a world, Enter to land there; T or Esc to come back down.";
    }

    public bool FlightButtonPressed => _flightButton.ButtonPressed;
    public Rect2 FlightButtonRect => _flightButton.GetGlobalRect();

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
