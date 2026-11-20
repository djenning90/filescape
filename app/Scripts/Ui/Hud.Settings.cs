using System.Globalization;
using FileScape.Core.Interaction;
using FileScape.Core.Units;
using Godot;

namespace FileScape.App.Ui;

/// <summary>
/// Settings (CTRL-01..04): mouse-look direction and levelling and walking speeds, saved for next time. Opened from the ⚙ Settings
/// button in the top bar or F4; Esc closes it.
/// </summary>
public partial class Hud
{
    public event Action<Preferences>? PreferencesChanged;
    public event Action? SettingsToggled;

    private PanelContainer _settingsPanel = null!;
    private CheckBox _reverseH = null!, _reverseV = null!, _levelView = null!, _flyGaze = null!;
    private HSlider _walkSlider = null!, _runSlider = null!;
    private Label _walkValue = null!, _runValue = null!, _settingsNote = null!;
    private Button _settingsButton = null!;
    /// <summary>The preferences as last shown: edits start from them, so fields this panel does not show (pins) are kept.</summary>
    private Preferences _shownPrefs = Preferences.Default;

    /// <summary>True while <see cref="SetPreferences"/> fills the controls, so their change events do not echo back as edits.</summary>
    private bool _settingsSync;

    /// <summary>The speed sliders move in steps of 0.5 m/s (within Preferences.MinSpeed…MaxSpeed).</summary>
    private const double SpeedStepMetresPerSecond = 0.5;

    public bool SettingsVisible => _settingsPanel.Visible;
    public Rect2 SettingsButtonRect => _settingsButton.GetGlobalRect();
    public Rect2 ReverseHorizontalRect => _reverseH.GetGlobalRect();
    public Rect2 ReverseVerticalRect => _reverseV.GetGlobalRect();
    public bool ReverseHorizontalChecked => _reverseH.ButtonPressed;
    public bool ReverseVerticalChecked => _reverseV.ButtonPressed;
    public Rect2 LevelViewRect => _levelView.GetGlobalRect();
    public Rect2 FlyGazeRect => _flyGaze.GetGlobalRect();
    public string WalkSpeedText => _walkValue.Text;

    /// <summary>Adds the ⚙ button to the top bar (after Key) and builds the centred, initially hidden panel; it also adopts the Quality button.</summary>
    private void BuildSettings(HBoxContainer topRow)
    {
        _settingsButton = MakeButton("⚙ Settings", () => SettingsToggled?.Invoke(), "Mouse-look direction and walking speed (F4). Saved for next time.");
        topRow.AddChild(_settingsButton);
        topRow.MoveChild(_settingsButton, _keyButton.GetIndex() + 1);

        _settingsPanel = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(560, 0) };
        _settingsPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _settingsPanel.GrowHorizontal = Control.GrowDirection.Both;
        _settingsPanel.GrowVertical = Control.GrowDirection.Both;
        _root.AddChild(_settingsPanel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        _settingsPanel.AddChild(v);
        var title = new Label { Text = "Settings" };
        title.AddThemeFontSizeOverride("font_size", 18);
        v.AddChild(title);

        Label Section(string text)
        {
            var l = new Label { Text = text };
            l.AddThemeFontSizeOverride("font_size", 14);
            l.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.6f));
            v.AddChild(l);
            return l;
        }

        Section("Mouse look in Places (hold right mouse, or drag with the left button)");
        _reverseH = new CheckBox { Text = "Reverse left / right — moving the mouse right turns you left", FocusMode = Control.FocusModeEnum.All };
        _reverseV = new CheckBox { Text = "Reverse up / down — moving the mouse down looks up", FocusMode = Control.FocusModeEnum.All };
        _reverseH.TooltipText = "Off: the usual first-person direction (mouse right turns right). On: as if dragging the scene.";
        _reverseV.TooltipText = "Off: the usual direction (mouse down looks down). On: mouse down looks up.";
        _reverseH.Toggled += _ => EmitPreferences();
        _reverseV.Toggled += _ => EmitPreferences();
        _levelView = new CheckBox { Text = "On foot: level the view when you let go (in flight your view stays where you leave it)", FocusMode = Control.FocusModeEnum.All };
        _levelView.TooltipText = "On: releasing the mouse button eases your view back to level in about a third of a second; the direction you turned to is kept. Off: the view stays where you left it.";
        _levelView.Toggled += _ => EmitPreferences();
        v.AddChild(_reverseH);
        v.AddChild(_reverseV);
        v.AddChild(_levelView);
        _flyGaze = new CheckBox { Text = "Flight: W flies where you look (off: W/S/A/D stay level; E/Space up, Q/C down)", FocusMode = Control.FocusModeEnum.All };
        _flyGaze.TooltipText = "Off (default): looking down from above never makes you dive; height changes only with E/Space and Q/C. On: forward follows your gaze, as before.";
        _flyGaze.Toggled += _ => EmitPreferences();
        v.AddChild(_flyGaze);
        var mapNote = new Label { Text = "The arrow keys always turn the way they point. The maps keep their own mouse controls (orbit, fly), unchanged.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        mapNote.AddThemeFontSizeOverride("font_size", 12);
        mapNote.AddThemeColorOverride("font_color", new Color(0.7f, 0.78f, 0.92f));
        v.AddChild(mapNote);

        Section("Speed on foot in Places (metres per second)");
        (HSlider, Label) SpeedRow(string label, string tooltip)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var name = new Label { Text = label, CustomMinimumSize = new Vector2(150, 0), TooltipText = tooltip, MouseFilter = Control.MouseFilterEnum.Pass };
            row.AddChild(name);
            var slider = new HSlider
            {
                MinValue = Preferences.MinSpeed.Value,
                MaxValue = Preferences.MaxSpeed.Value,
                Step = SpeedStepMetresPerSecond,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                FocusMode = Control.FocusModeEnum.All,
                TooltipText = tooltip,
            };
            row.AddChild(slider);
            var value = new Label { CustomMinimumSize = new Vector2(80, 0), HorizontalAlignment = HorizontalAlignment.Right };
            row.AddChild(value);
            v.AddChild(row);
            slider.ValueChanged += _ => EmitPreferences();
            return (slider, value);
        }

        (_walkSlider, _walkValue) = SpeedRow("Walk (W)", "Walking speed. Default 8 m/s (before 2026-11-05 evening: 4.2 m/s).");
        (_runSlider, _runValue) = SpeedRow("Run (hold Shift)", "Running speed, never below walking. Default 16 m/s (before: 8.4 m/s).");

        Section("Display");
        var qrow = new HBoxContainer();
        qrow.AddThemeConstantOverride("separation", 10);
        qrow.AddChild(_quality);
        var qnote = new Label { Text = "F2 toggles it anywhere. Low turns off volumetric fog, reflections and ambient occlusion.", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        qnote.AddThemeFontSizeOverride("font_size", 12);
        qnote.AddThemeColorOverride("font_color", new Color(0.7f, 0.78f, 0.92f));
        qrow.AddChild(qnote);
        v.AddChild(qrow);

        _settingsNote = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _settingsNote.AddThemeFontSizeOverride("font_size", 12);
        _settingsNote.AddThemeColorOverride("font_color", new Color(0.7f, 0.78f, 0.92f));
        v.AddChild(_settingsNote);
        var buttons = new HBoxContainer();
        buttons.AddChild(MakeButton("Reset to defaults", () => PreferencesChanged?.Invoke(Preferences.Default with { Pins = _shownPrefs.Pins }), "Reverse both look directions, level the view on release; walk 5.5 m/s, run 11 m/s. Pins are kept."));
        buttons.AddChild(MakeButton("Suggested speeds", () => PreferencesChanged?.Invoke(_shownPrefs with { WalkSpeed = Preferences.Default.WalkSpeed, RunSpeed = Preferences.Default.RunSpeed }), "Walk 5.5 m/s, run 11 m/s: comfortable in the compact places (the old defaults, 8 / 16 m/s, suited the larger places)."));
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        buttons.AddChild(MakeButton("Close (Esc)", () => _settingsPanel.Visible = false, "Close. Changes are already saved."));
        v.AddChild(buttons);
        foreach (var b in buttons.GetChildren().OfType<Button>()) b.FocusMode = Control.FocusModeEnum.All; // Tab reaches them (AD-83)
    }

    /// <summary>Shows the current preferences without raising <see cref="PreferencesChanged"/>; <paramref name="savedTo"/> null = not saved (automation).</summary>
    public void SetPreferences(Preferences p, string? savedTo)
    {
        _settingsSync = true;
        _shownPrefs = p;
        _reverseH.ButtonPressed = p.ReverseLookHorizontal;
        _reverseV.ButtonPressed = p.ReverseLookVertical;
        _levelView.ButtonPressed = p.LevelViewOnRelease;
        _flyGaze.ButtonPressed = p.FlyAlongGaze;
        _walkSlider.Value = p.WalkSpeed.Value;
        _runSlider.Value = p.RunSpeed.Value;
        _walkValue.Text = string.Create(CultureInfo.InvariantCulture, $"{p.WalkSpeed.Value:0.0} m/s");
        _runValue.Text = string.Create(CultureInfo.InvariantCulture, $"{p.RunSpeed.Value:0.0} m/s");
        _settingsNote.Text = savedTo != null ? $"Saved automatically to {savedTo}" : "Not saved: this is a test run (--prefs not given).";
        _settingsSync = false;
    }

    /// <summary>Raises <see cref="PreferencesChanged"/> with every control's current value; Main saves and applies them (the panel never saves itself).</summary>
    private void EmitPreferences()
    {
        if (_settingsSync) return;
        PreferencesChanged?.Invoke(_shownPrefs with
        {
            ReverseLookHorizontal = _reverseH.ButtonPressed,
            ReverseLookVertical = _reverseV.ButtonPressed,
            LevelViewOnRelease = _levelView.ButtonPressed,
            FlyAlongGaze = _flyGaze.ButtonPressed,
            WalkSpeed = MetersPerSecond.Of((float)_walkSlider.Value),
            RunSpeed = MetersPerSecond.Of((float)_runSlider.Value),
        });
    }

    /// <summary>Opens or closes the panel (F4, the ⚙ button); opening brings it to the front, nearly opaque, and dismisses the first-run hint.</summary>
    public void ToggleSettings()
    {
        _settingsPanel.Visible = !_settingsPanel.Visible;
        if (!_settingsPanel.Visible) return;
        // Drawn above every other HUD panel (it was added before them, so they used to cover parts of it), and opaque.
        _settingsPanel.MoveToFront();
        var sb = (StyleBoxFlat)_root.Theme.GetStylebox("panel", "PanelContainer").Duplicate();
        sb.BgColor = new Color(sb.BgColor, 0.98f);
        sb.ContentMarginLeft = sb.ContentMarginRight = 18;
        sb.ContentMarginTop = sb.ContentMarginBottom = 14;
        _settingsPanel.AddThemeStyleboxOverride("panel", sb);
        DismissHint();
        FocusSettings();
    }

    public void HideSettings() => _settingsPanel.Visible = false;

    /// <summary>
    /// AD-83 (PLANET-116): the open panel's first setting takes the keyboard, so Tab, Shift+Tab and ↑ ↓ move through the
    /// settings, Space or Enter toggles a box or presses a button and ← → move a slider; a panel closing over it gives the
    /// keyboard back here. Its controls take the focus for this (they were mouse-only, so keys reached nothing in it).
    /// </summary>
    public void FocusSettings()
    {
        if (!_settingsPanel.Visible || GetViewport().GuiGetFocusOwner() is { } f && _settingsPanel.IsAncestorOf(f)) return;
        _reverseH.GrabFocus();
    }
}
