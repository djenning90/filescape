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
    public bool ProblemsVisible => _problemPanel.Visible;
    public string ConnectionText => _connection.Visible ? _connection.Text : "";
    public string NoteText => _notePanel.Visible ? _note.Text : "";
    public int LocationRowCount => _locList.ItemCount;
    public LineEdit PathEdit => _pathEdit;
    public string ProblemButtonText => _problemButton.Visible ? _problemButton.Text : "";
    public int ProblemCount => _problemList.ItemCount;

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
        var title = new Label { Text = "Locations" };
        title.AddThemeFontSizeOverride("font_size", 18);
        v.AddChild(title);
        _locList = new ItemList { CustomMinimumSize = new Vector2(600, 220), SelectMode = ItemList.SelectModeEnum.Single };
        _locList.AddThemeFontSizeOverride("font_size", 14);
        _locList.ItemActivated += i => LocationChosen?.Invoke((int)_locList.GetItemMetadata((int)i));
        _locList.ItemClicked += (i, _, button) => { if (button == (long)MouseButton.Left) LocationChosen?.Invoke((int)_locList.GetItemMetadata((int)i)); };
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
        row.AddChild(MakeButton("Go", () => PathSubmitted?.Invoke(_pathEdit.Text), "Go to this path."));
        row.AddChild(MakeButton("Close (Esc)", HideLocations, "Close without going anywhere."));
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

    public void ShowLocations(IReadOnlyList<(int Id, string Text, string Tooltip)> rows, bool focusPath)
    {
        _locList.Clear();
        foreach (var (id, text, tip) in rows)
        {
            var i = _locList.AddItem(text);
            _locList.SetItemMetadata(i, id);
            _locList.SetItemTooltip(i, tip);
        }

        if (rows.Count == 0) _locList.AddItem("(sample data: type a path below to explore your own folders)", selectable: false);
        _locPanel.Visible = true;
        if (focusPath || rows.Count == 0) _pathEdit.GrabFocus();
        else _locList.GrabFocus();
    }

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

    /// <summary>Esc peels popups first (Locations, problem list). Returns true if one was closed.</summary>
    public bool ClosePopups()
    {
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

    private void ToggleProblems() => _problemPanel.Visible = !_problemPanel.Visible && _problemList.ItemCount > 0;

    public void SetWorldActions(bool canRetry, string? overrideText)
    {
        _retryButton.Visible = canRetry;
        _overrideButton.Visible = overrideText != null;
        if (overrideText != null) _overrideButton.Text = overrideText;
    }
}
