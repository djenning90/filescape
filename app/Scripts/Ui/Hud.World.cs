using Godot;

namespace FileScape.App.Ui;

/// <summary>
/// HUD parts for the connected world: the Locations hub and Go-to-path field (LOC-01), the hidden/system toggle
/// (VISI-04), the connection chip after following a link (LINK-03), a short note while a link or path is resolved,
/// the problem summary (VISI-03), and Retry / identity override actions in the inspector.
/// </summary>
public partial class Hud
{
    public event Action<int>? LocationChosen;
    public event Action<string>? PathSubmitted;
    public event Action? LocationsRequested;
    public event Action? HiddenToggled;
    public event Action? RetryRequested;
    public event Action? OrdinaryOverrideToggled;
    public event Action<int>? ProblemChosen;
    public event Action? StartFreshRequested;
    public event Action? ScanTreeRequested;
    public event Action<string>? ScanPathAsTreeRequested;

    private Label _modeLabel = null!;
    private string _mode = "";

    public string ModeText => _mode;

    /// <summary>Names the mode in Locations and in the source badge tooltip (connected world vs bounded tree scan).</summary>
    public void SetMode(string mode, bool live)
    {
        _mode = mode;
        _modeLabel.Text = $"Mode: {mode}";
        if (live) _sourcePanel.TooltipText = $"{mode}. " + _sourcePanel.TooltipText;
    }

    private PanelContainer _locPanel = null!;
    private ItemList _locList = null!;
    private LineEdit _pathEdit = null!;
    private Button _locButton = null!, _hiddenButton = null!, _problemButton = null!;
    private Button _retryButton = null!, _overrideButton = null!;
    private Label _connection = null!;
    private PanelContainer _notePanel = null!;
    private Label _note = null!;
    private PanelContainer _problemPanel = null!;
    private ItemList _problemList = null!;

    public bool LocationsVisible => _locPanel.Visible;

    /// <summary>The place bar's Locations button on screen (the real-input probe opens the panel with it).</summary>
    public Rect2 LocationsButtonRect => _locButton.GetGlobalRect();

    public string LocationRowText(int index) => index < _locList.ItemCount ? _locList.GetItemText(index) : "";
    public bool ProblemsVisible => _problemPanel.Visible;
    public string ConnectionText => _connection.Visible ? _connection.Text : "";
    public string NoteText => _notePanel.Visible ? _note.Text : "";
    public int LocationRowCount => _locList.ItemCount;
    public LineEdit PathEdit => _pathEdit;
    public string ProblemButtonText => _problemButton.Visible ? _problemButton.Text : "";
    public int ProblemCount => _problemList.ItemCount;

    /// <summary>
    /// Adds the Locations and Hidden buttons to the place bar's row, the connection line and problem button under it, and
    /// builds the note, the Locations dialog, the problem list and the inspector's Retry / override buttons (all hidden).
    /// Called from <see cref="BuildPlaces"/>, so the place bar exists.
    /// </summary>
    private void BuildWorld(HBoxContainer placeRow, VBoxContainer placeColumn)
    {
        _locButton = MakeButton("Locations (L)", () => LocationsRequested?.Invoke(), "Drives and WSL distributions; type or paste any path (Ctrl+L).");
        placeRow.AddChild(_locButton);
        _hiddenButton = MakeButton("Hidden: off (H)", () => HiddenToggled?.Invoke(), "Show or hide entries with the Windows hidden or system attribute. Names starting with a dot are always shown. Totals always include hidden entries.");
        _hiddenButton.ToggleMode = true;
        placeRow.AddChild(_hiddenButton);

        _connection = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _connection.AddThemeFontSizeOverride("font_size", 13);
        _connection.AddThemeColorOverride("font_color", new Color(0.55f, 0.95f, 0.95f));
        placeColumn.AddChild(_connection);

        _problemButton = MakeButton("", () => ToggleProblems(), "Folders here that could not be listed, and links that cannot be followed. Click for the list.");
        _problemButton.Visible = false;
        _problemButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _problemButton.Flat = true;
        _problemButton.AddThemeColorOverride("font_color", new Color(1f, 0.78f, 0.5f));
        placeColumn.AddChild(_problemButton);

        _notePanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _notePanel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _notePanel.GrowHorizontal = Control.GrowDirection.Both;
        _notePanel.OffsetTop = 270;
        _note = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _note.AddThemeFontSizeOverride("font_size", 15);
        _notePanel.AddChild(_note);
        _root.AddChild(_notePanel);

        // Locations popup.
        _locPanel = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(620, 0) };
        _locPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _locPanel.GrowHorizontal = Control.GrowDirection.Both;
        _locPanel.GrowVertical = Control.GrowDirection.Both;
        _root.AddChild(_locPanel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        _locPanel.AddChild(v);
        var title = new Label { Text = "Open a location" };
        title.AddThemeFontSizeOverride("font_size", 18);
        v.AddChild(title);
        _locList = new ItemList { CustomMinimumSize = new Vector2(600, 220), SelectMode = ItemList.SelectModeEnum.Single };
        _locList.AddThemeFontSizeOverride("font_size", 14);
        // The row for where you already are is disabled (PLANET-41): choosing it does nothing.
        _locList.ItemActivated += i => { if (!_locList.IsItemDisabled((int)i)) LocationChosen?.Invoke((int)_locList.GetItemMetadata((int)i)); };
        _locList.ItemClicked += (i, _, button) => { if (button == (long)MouseButton.Left && !_locList.IsItemDisabled((int)i)) LocationChosen?.Invoke((int)_locList.GetItemMetadata((int)i)); };
        _locList.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                HideLocations();
                _locList.AcceptEvent();
            }
        };
        v.AddChild(_locList);
        var hint = new Label { Text = "Go to a path: C:\\folder · \\\\wsl.localhost\\Ubuntu\\home\\you · \\\\wsl$\\… · /home/you · /mnt/c/…", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.78f, 0.92f));
        v.AddChild(hint);
        var row = new HBoxContainer();
        v.AddChild(row);
        _pathEdit = new LineEdit { PlaceholderText = "Type or paste a path, then Enter", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _pathEdit.TextSubmitted += t => PathSubmitted?.Invoke(t);
        row.AddChild(_pathEdit);
        row.AddChild(MakeButton("Go", () => PathSubmitted?.Invoke(_pathEdit.Text), "Go to this path in the connected world (listed when you arrive)."));
        row.AddChild(MakeButton("Scan as tree", () => ScanPathAsTreeRequested?.Invoke(_pathEdit.Text), "Bounded tree scan (whole-tree reference): scan this folder completely, links not followed, so the maps show its whole tree."));
        row.AddChild(MakeButton("Browse…", () => { HideLocations(); OpenFolderRequested?.Invoke(); }, "Choose a folder with the Windows folder picker. (This PC cannot be picked there: it is not a folder. Choose ⌂ This computer above instead.)"));
        row.AddChild(MakeButton("Close (Esc)", HideLocations, "Close without going anywhere."));
        var modes = new HBoxContainer();
        v.AddChild(modes);
        _modeLabel = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _modeLabel.AddThemeFontSizeOverride("font_size", 12);
        _modeLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.78f, 0.92f));
        modes.AddChild(_modeLabel);
        modes.AddChild(MakeButton("Scan this folder as a tree", () => ScanTreeRequested?.Invoke(), "Bounded tree scan of the folder you are in: the Classic FSN and District maps then show its whole tree (the original reference behaviour)."));
        modes.AddChild(MakeButton("Start fresh here", () => StartFreshRequested?.Invoke(), "Explore the connected world again from the folder you are in, with a new entry budget (use it when the entry limit has been reached)."));
        _locPanel.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                HideLocations();
                _locPanel.AcceptEvent();
            }
        };
        _pathEdit.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                HideLocations();
                _pathEdit.AcceptEvent();
            }
        };

        // Problem list popup.
        _problemPanel = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(460, 0) };
        _problemPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _problemPanel.OffsetLeft = 330; _problemPanel.OffsetTop = 190;
        _root.AddChild(_problemPanel);
        var pv = new VBoxContainer();
        _problemPanel.AddChild(pv);
        var pt = new Label { Text = "Needs attention here (details in the inspector)" };
        pt.AddThemeFontSizeOverride("font_size", 14);
        pv.AddChild(pt);
        _problemList = new ItemList { CustomMinimumSize = new Vector2(440, 160), FocusMode = Control.FocusModeEnum.None };
        _problemList.AddThemeFontSizeOverride("font_size", 13);
        _problemList.ItemClicked += (i, _, button) => { if (button == (long)MouseButton.Left) ProblemChosen?.Invoke((int)_problemList.GetItemMetadata((int)i)); };
        pv.AddChild(_problemList);
        pv.AddChild(MakeButton("Close", () => _problemPanel.Visible = false, "Close the list."));

        _retryButton = MakeButton("Retry", () => RetryRequested?.Invoke(), "List this folder again, or resolve this link again.");
        _overrideButton = MakeButton("Not a project", () => OrdinaryOverrideToggled?.Invoke(), "Show this folder as an ordinary folder instead of a software project (or back).");
        _retryButton.Visible = _overrideButton.Visible = false;
        _openButton.GetParent().AddChild(_retryButton);
        _openButton.GetParent().AddChild(_overrideButton);
    }

    public void SetLocationsAvailable(bool live)
    {
        _locButton.TooltipText = live
            ? "Drives and WSL distributions; type or paste any path (Ctrl+L)."
            : "Sample data has no locations: type or paste a path to explore your own folders (Ctrl+L).";
    }

    /// <summary>
    /// Opens the Locations dialog with these rows (drives, distributions, This computer). The path field gets the keyboard
    /// when <paramref name="focusPath"/> (Ctrl+L) or when there are no rows; otherwise the list does. The row whose id is
    /// <paramref name="here"/> is where you already are: marked "● you are here", in the highlight colour, and disabled,
    /// so it cannot be chosen as a trip to nowhere (PLANET-41).
    /// </summary>
    public void ShowLocations(IReadOnlyList<(int Id, string Text, string Tooltip)> rows, bool focusPath, int here = int.MinValue)
    {
        _locList.Clear();
        foreach (var (id, text, tip) in rows)
        {
            var isHere = id == here;
            var i = _locList.AddItem(isHere ? $"●  {text}   ·  you are here" : text);
            _locList.SetItemMetadata(i, id);
            _locList.SetItemTooltip(i, isHere ? "You are here already." : tip);
            if (isHere)
            {
                _locList.SetItemDisabled(i, true);
                _locList.SetItemCustomFgColor(i, LocationHereColor);
            }
        }

        if (rows.Count == 0) _locList.AddItem("(no locations in this mode — type a path: Go explores the connected world, Scan as tree makes a bounded tree)", selectable: false);
        _locPanel.Visible = true;
        if (focusPath || rows.Count == 0)
        {
            _pathEdit.GrabFocus();
            _pathEdit.SelectAll(); // like an address bar: typing replaces the last path (found by the project journey)
        }
        else _locList.GrabFocus();
    }

    /// <summary>The "you are here" row's colour: the breadcrumb's current-folder yellow.</summary>
    private static readonly Color LocationHereColor = new(1f, 0.95f, 0.7f);

    /// <summary>Whether the Locations row at <paramref name="index"/> is disabled (where you already are; probes).</summary>
    public bool LocationRowDisabled(int index) => index < _locList.ItemCount && _locList.IsItemDisabled(index);

    public void HideLocations()
    {
        _locPanel.Visible = false;
        _pathEdit.ReleaseFocus();
        _locList.ReleaseFocus();
    }

    public Rect2 LocationRowRect(int index)
    {
        var r = _locList.GetItemRect(index);
        return new Rect2(_locList.GetGlobalRect().Position + r.Position, r.Size);
    }

    /// <summary>The row showing location <paramref name="id"/>, or −1 (the placeholder row has no metadata, hence the Nil check).</summary>
    public int LocationRowIndex(int id)
    {
        for (var i = 0; i < _locList.ItemCount; i++)
        {
            if (_locList.GetItemMetadata(i).VariantType != Variant.Type.Nil && (int)_locList.GetItemMetadata(i) == id) return i;
        }

        return -1;
    }

    public void SetHiddenToggle(bool showing)
    {
        _hiddenButton.SetPressedNoSignal(showing);
        _hiddenButton.Text = showing ? "Hidden: shown (H)" : "Hidden: off (H)";
    }

    /// <summary>The Hidden button names how many entries here are hidden (when some are and they are not shown).</summary>
    public void SetHiddenCount(int hidden, bool showing)
    {
        _hiddenButton.Text = showing ? "Hidden: shown (H)" : hidden > 0 ? $"{hidden} hidden (H)" : "Hidden: off (H)";
    }

    public string HiddenButtonText => _hiddenButton.Text;

    public void SetConnection(string? text)
    {
        _connection.Visible = text != null;
        if (text != null) _connection.Text = text;
    }

    /// <summary>A short note under the place bar while a link or path is being resolved ("Finding …"); null hides it.</summary>
    public void SetTransitionNote(string? text)
    {
        _notePanel.Visible = text != null;
        if (text != null) _note.Text = text;
    }

    public void SetProblems(IReadOnlyList<(int Id, string Text)> problems)
    {
        _problemList.Clear();
        foreach (var (id, text) in problems)
        {
            var i = _problemList.AddItem(text);
            _problemList.SetItemMetadata(i, id);
        }

        _problemButton.Visible = problems.Count > 0;
        _problemButton.Text = $"⚠ {problems.Count} here need{(problems.Count == 1 ? "s" : "")} attention ▸";
        if (problems.Count == 0) _problemPanel.Visible = false;
    }

    /// <summary>Esc peels popups first, one per press: README, Settings, Locations, then the problem list. Returns true if one was closed.</summary>
    public bool ClosePopups()
    {
        if (_readmePanel.Visible)
        {
            HideReadme();
            return true;
        }

        if (_settingsPanel.Visible)
        {
            HideSettings();
            return true;
        }

        if (_locPanel.Visible)
        {
            HideLocations();
            return true;
        }

        if (_problemPanel.Visible)
        {
            _problemPanel.Visible = false;
            return true;
        }

        return false;
    }

    /// <summary>Closes the problem list (a destination was committed: it would cover the flight).</summary>
    public void HideProblemList() => _problemPanel.Visible = false;

    private void ToggleProblems() => _problemPanel.Visible = !_problemPanel.Visible && _problemList.ItemCount > 0;

    public void SetWorldActions(bool canRetry, string? overrideText)
    {
        _retryButton.Visible = canRetry;
        _overrideButton.Visible = overrideText != null;
        if (overrideText != null) _overrideButton.Text = overrideText;
    }
}
