using FileScape.Core.Interaction;
using Godot;

namespace FileScape.App;

/// <summary>
/// Keyboard and mouse ownership by open panels (PLANET-69/70, AD-68; PLANET-115..119, AD-83): the one place that decides
/// whether a key press that no control consumed may reach the game's shortcuts, and what a press on the world does while
/// panels are open. The policy is Core's table (<see cref="PanelOwnership"/>: which panels take the keyboard, which one is
/// topmost, which keys still pass, which panels a world click closes); this file only tells it what is open, maps a key
/// event onto it and carries out its answer.
/// <para>
/// Keys: while a keyboard panel is open — the picture viewer, a confirmation, the welcome, the reading view, Settings,
/// Locations / Open location, the planets' Destinations list (and its pile/telescope form), a root landmark's location
/// actions, and since AD-92 any focused text field no panel owns — the owner (<see cref="KeyboardPanel"/>) gets the key first through its own handler (<see cref="OwnKey"/>),
/// then the table routes what it left: Esc closes the owner (only it: never the overview or a gameplay layer behind it),
/// the F keys, Ctrl+L and Ctrl+O pass where the table says (they open the navigation panels), J passes from Destinations
/// and Locations (PLANET-41), and every other press, echo or chord stops here, wherever the GUI focus is, so typing and
/// panel navigation can never travel, jump, toggle a panel or switch to a legacy view.
/// </para>
/// <para>
/// Focus: a panel that opens establishes its focus (its own code: the filter, the rows, the text, the first setting, the
/// first action); when a panel closes and another stays open, <see cref="RestoreFocus"/> gives the one left the keyboard
/// again; when none stays, the GUI focus is released so the keys are the game's at once.
/// </para>
/// <para>
/// Mouse: a press on the world (over no panel) goes to <see cref="DismissOnWorldPress"/> before anything else sees it
/// (Main._Input): Settings, Locations, Help and the menus close on the press and the press does nothing else (no drag,
/// no selection); Destinations, the location actions and the reader close on a click released without a drag, which
/// then selects nothing (<see cref="DismissOnWorldClick"/>, asked by PlanetWorld and the legacy views on release), so a
/// drag still looks around beside them (PLANET-25); a confirmation or the welcome keeps the press and stays.
/// </para>
/// <para>
/// Why one rule at the dispatch point and not per panel: the leaks DJ hit came from keys a control did not use (Godot's
/// ItemList leaves Backspace, a Button-only panel leaves every letter, a click on the world leaves focus nowhere), and a
/// panel that never told the rule it was open (the location actions: the arrows went on walking). A key reaches the
/// game's shortcuts only through <see cref="_UnhandledInput"/>, and movement keys are read only when no panel owns the
/// keyboard (Main._Process), so guarding here covers every panel and every focus owner.
/// </para>
/// Also here: the instrument counters that let a probe see a stray key or click (<see cref="ShortcutCommandCount"/>,
/// <see cref="ExperienceChangeCount"/>, <see cref="PanelConsumedKeyCount"/>, <see cref="WorldPressDismissals"/>,
/// <see cref="ProbeShortcutsInert"/>).
/// Preserve: a trip's Esc stops the trip first, whatever is open; the panels' own keys (typing, Enter, results
/// navigation, R closing the reader) are unchanged; with no keyboard panel open nothing here changes what a key does; a
/// new panel gets a row in <see cref="PanelOwnership.Rules"/> and a line in <see cref="OpenPanels"/> (and, if it has
/// keys of its own, in <see cref="OwnKey"/>), not its own guard.
/// </summary>
public partial class Main
{
    /// <summary>The AD-68 names of the keyboard panels, kept for callers (probes): the values are Core's <see cref="PanelId"/>.</summary>
    public static class KeyPanel
    {
        public const PanelId None = PanelId.None, Viewer = PanelId.Viewer, Reader = PanelId.Reader, Settings = PanelId.Settings,
            Locations = PanelId.Locations, Destinations = PanelId.Destinations, LocationActions = PanelId.LocationActions;
    }

    /// <summary>Every panel open now, as the ownership table names them (keyboard panels and those that accompany play).</summary>
    public List<PanelId> OpenPanels()
    {
        var open = new List<PanelId>();
        if (_hud == null) return open;
        var planets = _experience == Experience.Planets;
        void Add(bool shown, PanelId p) { if (shown) open.Add(p); }
        Add(planets && _planets.ViewerOpen, PanelId.Viewer); // PLANET-96: the picture viewer covers everything
        Add(_confirm?.Visible == true, PanelId.Confirm);
        Add(_welcomeOpen, PanelId.Welcome);
        Add(_hud.ReadmeVisible, PanelId.Reader);
        Add(_hud.SettingsVisible, PanelId.Settings);
        Add(_hud.LocationsVisible, PanelId.Locations);
        Add(planets && _planets.ListOpen, PanelId.Destinations);
        Add(planets && _planets.RootMarkPanelOpen, PanelId.LocationActions);
        Add(_hud.HelpVisible, PanelId.Help);
        Add(_hud.LegacyMenuOpen, PanelId.LegacyMenu);
        Add(_hud.ProblemsVisible, PanelId.Problems);
        Add(_hud.InspectorVisible, PanelId.SelectionCard);
        Add(_lessonCard?.Visible == true, PanelId.TutorialCard);
        Add(planets && _planets.PreviewOpen, PanelId.Preview);
        Add(planets && _planets.BenchPanelOpen, PanelId.Bench);
        // AD-92: a text field with the focus that no panel above owns (the legacy Places drawer's filter) owns its keys too.
        Add(GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit && FocusPanel(GetViewport().GuiGetFocusOwner()) == PanelId.None, PanelId.TextField);
        return open;
    }

    /// <summary>
    /// The open panel that owns the keyboard, or <see cref="PanelId.None"/> (<see cref="PanelOwnership.Owner"/>): a modal
    /// one, else the one holding the GUI focus (Locations and Destinations can be open together, PLANET-41: the one you
    /// opened last has the focus), else the topmost in the table's order (after a click on the world nothing has the focus).
    /// </summary>
    public PanelId KeyboardPanel
    {
        get
        {
            if (_hud == null) return PanelId.None;
            var open = OpenPanels();
            return open.Count == 0 ? PanelId.None : PanelOwnership.Owner(open, GetViewport().GuiGetFocusOwner() is { } f ? FocusHolder(f) : PanelId.None);
        }
    }

    /// <summary>The registered panel holding <paramref name="f"/>, or <see cref="PanelId.None"/> (then a focused text field is <see cref="PanelId.TextField"/>).</summary>
    private PanelId FocusPanel(Control f) =>
        _hud.ReaderHolds(f) ? PanelId.Reader
        : _hud.SettingsHolds(f) ? PanelId.Settings
        : _hud.LocationsHolds(f) ? PanelId.Locations
        : _experience == Experience.Planets && _planets.ListHolds(f) ? PanelId.Destinations
        : PanelId.None;

    /// <summary>The panel holding the GUI focus (a registered one, else a bare focused text field), or <see cref="PanelId.None"/>.</summary>
    private PanelId FocusHolder(Control f) => FocusPanel(f) is var p && p != PanelId.None ? p : f is LineEdit or TextEdit ? PanelId.TextField : PanelId.None;

    /// <summary>A key event as the table sees it: Esc, an F key, the navigation chords and J by their exact bindings, or any other.</summary>
    private static PanelKey ToPanelKey(InputEventKey k) => new(
        k.Keycode == Key.Escape ? PanelKeyKind.Escape
        : k.Keycode is >= Key.F1 and <= Key.F12 ? PanelKeyKind.FunctionKey
        : k.IsAction(InputBindings.GoToPath, exactMatch: true) ? PanelKeyKind.GoToPath
        : k.IsAction(InputBindings.OpenFolder, exactMatch: true) ? PanelKeyKind.OpenFolder
        : k.IsAction(InputBindings.Destinations, exactMatch: true) ? PanelKeyKind.Destinations
        : PanelKeyKind.Other,
        k.Echo, k.CtrlPressed || k.AltPressed || k.MetaPressed);

    /// <summary>
    /// First thing in <see cref="_UnhandledInput"/> for every event: true when the key is done with — used by a trip or
    /// the owning panel, or kept from the game because a panel owns the keyboard (see the class summary).
    /// </summary>
    private bool PanelOwnsKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k) return false;
        if (_experience == Experience.Planets && _planets.TripKey(k)) return true; // a trip's Esc stops it (PLANET-40)
        var panel = KeyboardPanel;
        if (panel == PanelId.None) return false;
        if (OwnKey(panel, k))
        {
            GetViewport().SetInputAsHandled();
            AfterPanelChange();
            return true;
        }

        switch (PanelOwnership.Route(panel, ToPanelKey(k)))
        {
            case KeyRoute.Pass:
                return false;
            case KeyRoute.Close:
                ClosePanel(panel); // the panel's own Esc: it closes, in the overview too (the overview stays)
                AfterPanelChange();
                GetViewport().SetInputAsHandled();
                return true;
            default:
                PanelConsumedKeyCount++;
                GetViewport().SetInputAsHandled();
                return true;
        }
    }

    /// <summary>The owning panel's own keys, before the table's rule (true when it used the key).</summary>
    private bool OwnKey(PanelId panel, InputEventKey k) => panel switch
    {
        PanelId.Reader => ReaderKey(k),
        PanelId.Viewer => _planets.ViewerKey(k),
        PanelId.Destinations => _planets.ListKey(k),
        PanelId.LocationActions => _planets.RootActionsKey(k), // PLANET-115: ↑ ↓ Tab Home End, Enter, 1–9, E
        PanelId.Welcome => WelcomeKey(k),
        _ => false,
    };

    /// <summary>Closes one panel, as its own Close button or Esc does.</summary>
    private void ClosePanel(PanelId panel)
    {
        switch (panel)
        {
            case PanelId.Viewer: _planets.CloseOverlay(); break; // the viewer is the planets' topmost overlay
            case PanelId.Confirm: ProbeCancelConfirm(); break; // cancelling runs nothing
            case PanelId.Welcome: CloseWelcome(); break; // "Explore on my own"
            case PanelId.Reader: _hud.HideReadme(); break;
            case PanelId.Settings: _hud.HideSettings(); break;
            case PanelId.Locations: _hud.HideLocations(); break;
            case PanelId.Destinations: _planets.CloseContents(); break;
            case PanelId.LocationActions: _planets.CloseRootMarkPanel(); break;
            case PanelId.Help: if (_hud.HelpVisible) _hud.ToggleHelp(); break;
            case PanelId.LegacyMenu or PanelId.Problems: _hud.CloseMenus(); break;
            case PanelId.TextField:
                // The legacy Places drawer's filter is the drawer's: its Esc closes the drawer, as it did before AD-92 and
                // as the field's own placeholder says (explore-ui1.0, seventh-pass integration). Any other bare field lets go.
                if (_hud.DestinationsOpen && GetViewport().GuiGetFocusOwner() == _hud.DestinationFilter) _hud.ToggleDestinations(focusFilter: false);
                else GetViewport().GuiReleaseFocus();
                break;
        }
    }

    /// <summary>
    /// After a panel closed or used a key: the keyboard panel left open gets its focus back (a panel closing over another
    /// left it nowhere); with none left the GUI focus is released, or the toolbar button that opened the panel keeps it and
    /// every shortcut stays blocked (R, J did nothing after ⚙ then Esc; os-planets-probe -Dialogs, 2026-11-19).
    /// </summary>
    private void AfterPanelChange()
    {
        var owner = KeyboardPanel;
        if (owner == PanelId.None) GetViewport().GuiReleaseFocus();
        else RestoreFocus(owner);
    }

    /// <summary>Gives <paramref name="panel"/> its useful focus again when the GUI focus is outside it (AD-83).</summary>
    private void RestoreFocus(PanelId panel)
    {
        switch (panel)
        {
            case PanelId.Reader: _hud.FocusReader(); break;
            case PanelId.Settings: _hud.FocusSettings(); break;
            case PanelId.Locations: _hud.FocusLocations(); break;
            case PanelId.Destinations: _planets.FocusListIfLost(); break;
            case PanelId.LocationActions: if (GetViewport().GuiGetFocusOwner() != null) GetViewport().GuiReleaseFocus(); break; // its keys come through the rule
        }
    }

    /// <summary>
    /// A mouse press anywhere, from <see cref="_Input"/> before the GUI and the world see it (AD-83). Over a panel or the
    /// HUD it is the GUI's; on the world, the panels that close on a press close and the press is consumed (nothing else
    /// happens with it: no drag, no selection, no travel); a panel that needs a choice keeps it. True when consumed.
    /// </summary>
    private bool DismissOnWorldPress(InputEventMouseButton press)
    {
        if (!press.Pressed || press.ButtonIndex is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle) || !_windowFocused) return false;
        var open = OpenPanels();
        if (open.Count == 0 || open.Contains(PanelId.Viewer) || PointerOverUi(press.Position)) return false; // the viewer takes its own clicks
        var r = PanelOwnership.OnWorldPress(open, click: false);
        if (!r.Consumed) return false;
        foreach (var p in r.Close) ClosePanel(p);
        if (r.Close.Count > 0) WorldPressDismissals++;
        AfterPanelChange();
        GetViewport().SetInputAsHandled();
        if (_experience == Experience.Planets) _planets.ForgetPress();
        else _pressButton = MouseButton.None;
        _nextPressNotDouble = true;
        return true;
    }

    /// <summary>
    /// A left click on the world released without a drag (asked by PlanetWorld and the legacy views before they select
    /// anything): the panels that close on a click close (Destinations, the location actions, the reader). True when it
    /// closed one: the click then does nothing else.
    /// </summary>
    public bool DismissOnWorldClick()
    {
        var open = OpenPanels();
        if (open.Count == 0 || open.Contains(PanelId.Viewer)) return false;
        var r = PanelOwnership.OnWorldPress(open, click: true);
        if (r.Close.Count == 0) return false;
        foreach (var p in r.Close) ClosePanel(p);
        WorldPressDismissals++;
        AfterPanelChange();
        _nextPressNotDouble = true;
        return true;
    }

    /// <summary>The point (viewport units) is over the HUD or one of the planets' own panels, not the world.</summary>
    private bool PointerOverUi(Vector2 p) =>
        _hud.BlockingRects.Any(r => r.HasPoint(p)) || (_experience == Experience.Planets && _planets.OverHudAt(p));

    /// <summary>A press consumed to close a panel: the legacy views must not read the next press as a double-click.</summary>
    private bool _nextPressNotDouble;

    /// <summary>
    /// The welcome's keys (it needs one of its three answers): ← → and Tab move between them, Enter or Space answers.
    /// Its buttons take the focus for this; Esc is "Explore on my own" (the table's Close).
    /// </summary>
    private bool WelcomeKey(InputEventKey k)
    {
        if (!_welcomeOpen) return false;
        if (GetViewport().GuiGetFocusOwner() is not { } f || !_welcome.IsAncestorOf(f)) FocusWelcome();
        return false; // the focused button takes its keys through the GUI; the table keeps the rest
    }

    /// <summary>Key presses kept from the game because a panel owned the keyboard (probes and the state file).</summary>
    public int PanelConsumedKeyCount { get; private set; }

    /// <summary>Presses or clicks on the world that closed panels and did nothing else (probes, AD-83).</summary>
    public int WorldPressDismissals { get; private set; }

    /// <summary>Key presses handed to the one-shot commands so far (probes: none may arrive while a panel owns the keyboard).</summary>
    public int ShortcutCommandCount { get; private set; }

    /// <summary>Switches of experience so far (probes: typing in a panel must never change the view).</summary>
    public int ExperienceChangeCount { get; private set; }

    /// <summary>
    /// Test instrument (PLANET-69): while true, a key press that reaches the planets' shortcuts is logged
    /// (<see cref="UnhandledKeys"/>) and then dropped instead of acting, Esc excepted (it still closes the panel), so one run
    /// can list every panel/key pair that leaks without the first leak (Tab, V) leaving the planets.
    /// </summary>
    public bool ProbeShortcutsInert { get; set; }
}
