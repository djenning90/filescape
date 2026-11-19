using Godot;

namespace FileScape.App;

/// <summary>
/// Keyboard ownership by open panels (PLANET-69/70, AD-68): the one place that decides whether a key press that no
/// control consumed may reach the game's shortcuts. While a panel that takes the keyboard is open — the reading view,
/// Settings, Locations / Open location, or the planets' Destinations list (and its pile/telescope form) — the topmost
/// one gets the key first (its own handler: <see cref="ReaderKey"/>, <see cref="Planets.PlanetWorld.ListKey"/>), Esc
/// closes it (<see cref="HandleEscape"/>, never the overview or a gameplay layer behind it), and only these still pass
/// to the game: the F keys, Ctrl+L and Ctrl+O exactly (they open the navigation panels themselves), and J from the
/// Destinations results (it closes the list). Every other press, echo or chord — letters, digits, Tab, Backspace, Page
/// and arrow keys, Ctrl+V — stops here, wherever the GUI focus is (the path field, the rows, nothing after a click on
/// the world), so typing and panel navigation can never travel, jump, toggle a panel or switch to a legacy view.
/// <para>
/// Why one rule at the dispatch point and not per panel: the leaks DJ hit came from keys a control did not use (Godot's
/// ItemList leaves Backspace, a Button-only panel leaves every letter, a click on the world leaves focus nowhere), and each
/// panel had its own partial guard (the reader's, the list's) or none (Settings, Locations). A key reaches the game's
/// shortcuts only through <see cref="_UnhandledInput"/>, so guarding there covers every panel and every focus owner.
/// </para>
/// Also here: the instrument counters that let a probe see a stray key (<see cref="ShortcutCommandCount"/>,
/// <see cref="ExperienceChangeCount"/>, <see cref="PanelConsumedKeyCount"/>, <see cref="ProbeShortcutsInert"/>).
/// Preserve: a trip's Esc stops the trip first, whatever is open; the panels' own keys (typing, Enter, results
/// navigation, R closing the reader) are unchanged; with no keyboard panel open nothing here changes what a key does;
/// a new text-entry or navigation panel is added to <see cref="KeyboardPanel"/>, not given its own guard.
/// </summary>
public partial class Main
{
    /// <summary>The panels that take the keyboard while open, topmost first in <see cref="KeyboardPanel"/>.</summary>
    public enum KeyPanel { None, Reader, Settings, Locations, Destinations }

    /// <summary>The open panel that owns the keyboard (the topmost when several are open), or <see cref="KeyPanel.None"/>.</summary>
    public KeyPanel KeyboardPanel =>
        _hud == null ? KeyPanel.None
        : _hud.ReadmeVisible ? KeyPanel.Reader
        : _hud.SettingsVisible ? KeyPanel.Settings
        : _hud.LocationsVisible ? KeyPanel.Locations
        : _experience == Experience.Planets && _planets.ListOpen ? KeyPanel.Destinations
        : KeyPanel.None;

    /// <summary>
    /// First thing in <see cref="_UnhandledInput"/> for every event: true when the key is done with — used by a trip or
    /// the topmost panel, or kept from the game because a panel owns the keyboard (see the class summary).
    /// </summary>
    private bool PanelOwnsKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k) return false;
        if (_experience == Experience.Planets && _planets.TripKey(k)) return true; // a trip's Esc stops it (PLANET-40)
        var panel = KeyboardPanel;
        if (panel == KeyPanel.None) return false;
        if (panel == KeyPanel.Reader && ReaderKey(k)) return true;
        if (panel == KeyPanel.Destinations && _planets.ListKey(k)) return true;
        if (k.Keycode == Key.Escape)
        {
            if (!k.Echo) HandleEscape(); // the panel's own Esc: it closes, in the overview too (the overview stays)
            GetViewport().SetInputAsHandled();
            return true;
        }

        if (PassesOpenPanel(k, panel)) return false;
        PanelConsumedKeyCount++;
        GetViewport().SetInputAsHandled();
        return true;
    }

    /// <summary>The keys that still reach the game while <paramref name="panel"/> owns the keyboard (class summary).</summary>
    private static bool PassesOpenPanel(InputEventKey k, KeyPanel panel)
    {
        if (k.Echo) return false;
        if (k.Keycode is >= Key.F1 and <= Key.F12 && !k.CtrlPressed && !k.AltPressed && !k.MetaPressed) return true;
        if (k.IsAction(InputBindings.GoToPath, exactMatch: true) || k.IsAction(InputBindings.OpenFolder, exactMatch: true)) return true;
        return panel == KeyPanel.Destinations && k.IsAction(InputBindings.Destinations, exactMatch: true);
    }

    /// <summary>Key presses kept from the game because a panel owned the keyboard (probes and the state file).</summary>
    public int PanelConsumedKeyCount { get; private set; }

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
