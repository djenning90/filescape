using FileScape.App.Planets;
using FileScape.Core.Model;
using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-dialog-keys --root=sample --vis=planets --test-shell=record</c> (PLANET-69/70, AD-68): every
/// panel that takes the keyboard on the planets — Destinations, Locations, Open location (Ctrl+L and the Open… button),
/// Settings and the reading view — opened through its shortcut and through its toolbar entry, then typed into at once
/// with every letter, the digits, Shift capitals, Space where text is edited, Tab, Shift+Tab, Backspace, Delete, the
/// Page, Home/End and arrow keys and the chords a person types in a field (Ctrl+V, Ctrl+Z, Ctrl+Tab, Ctrl+B); then the
/// same after moving the keyboard into the panel's results; then Esc. Each key is judged on its own: it must not reach
/// the game's shortcuts (<see cref="Main.UnhandledKeys"/>), hand a command to <c>HandleCommand</c>
/// (<see cref="Main.ShortcutCommandCount"/>), switch the experience (<see cref="Main.ExperienceChangeCount"/>; Planets to
/// Places is the "legacy view" of DJ's report), move you, the traveller or the overview, or close the panel.
/// <para>
/// Three passes. <b>Enumerate</b> (the instrument <see cref="Main.ProbeShortcutsInert"/> on): every panel/key pair that
/// reaches the shortcuts is logged and dropped, so one run lists all the leaks without the first one leaving the planets
/// (the before-fix evidence). <b>Live</b>: the same cases with the shortcuts acting, judged on the counters and the state
/// (a leak that changed the experience is logged and undone so the run goes on). <b>After travel</b>: Enter in the
/// Destinations filter goes to Lantern Workshop, the panels are reopened there (shortcut and toolbar), Enter in Open
/// location's path field comes home, the reader and Locations are reopened, and Destinations and Locations are tried in
/// the overview (T).
/// </para>
/// Preserve: input goes only through the real event path (key code, physical code and character together, as a
/// keyboard sends them); the panels' own keys (Esc, J closing Destinations, R closing the reader, Enter committing) are
/// not in the typed set, they are checked by name.
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>One key stroke: its key, the modifiers held and the character it types (0 for none).</summary>
    private sealed record Stroke(string Name, Godot.Key Key, bool Shift = false, bool Ctrl = false, char Ch = '\0');

    /// <summary>One way of opening a panel, and what the panel offers the keyboard.</summary>
    private sealed record DialogCase(string Panel, string Opener, Func<Task> Open, Func<bool> IsOpen, bool EditsText, bool HasResults, string OwnKeys);

    /// <summary>Leaks found, "panel / opener / phase: key → what happened", across the run.</summary>
    private readonly List<string> _dialogLeaks = [];

    private int _dialogKeysTried;

    private static IEnumerable<Stroke> Letters(string ownKeys) =>
        "abcdefghijklmnopqrstuvwxyz".Where(c => !ownKeys.Contains(c)).Select(c => new Stroke(c.ToString(), (Godot.Key)char.ToUpperInvariant(c), Ch: c));

    /// <summary>The typed set for a panel: see the class summary.</summary>
    private static List<Stroke> DialogStrokes(DialogCase c, bool inResults)
    {
        var s = Letters(c.OwnKeys).ToList();
        s.AddRange("0123456789".Select(d => new Stroke(d.ToString(), Godot.Key.Key0 + (d - '0'), Ch: d)));
        s.Add(new Stroke("Shift+V", Godot.Key.V, Shift: true, Ch: 'V'));
        s.Add(new Stroke("Shift+Q", Godot.Key.Q, Shift: true, Ch: 'Q'));
        if (c.EditsText && !inResults) s.Add(new Stroke("Space", Godot.Key.Space, Ch: ' '));
        s.AddRange(
        [
            new("Tab", Godot.Key.Tab), new("Shift+Tab", Godot.Key.Tab, Shift: true), new("Backspace", Godot.Key.Backspace), new("Delete", Godot.Key.Delete),
            new("PageDown", Godot.Key.Pagedown), new("PageUp", Godot.Key.Pageup), new("Home", Godot.Key.Home), new("End", Godot.Key.End),
            new("Up", Godot.Key.Up), new("Down", Godot.Key.Down), new("Left", Godot.Key.Left), new("Right", Godot.Key.Right),
            new("Ctrl+V", Godot.Key.V, Ctrl: true), new("Ctrl+Z", Godot.Key.Z, Ctrl: true), new("Ctrl+Tab", Godot.Key.Tab, Ctrl: true), new("Ctrl+B", Godot.Key.B, Ctrl: true),
        ]);
        return s;
    }

    /// <summary>Presses and releases one stroke as a keyboard sends it (modifier keys down first, up last).</summary>
    private async Task Strike(Stroke s)
    {
        if (s.Ctrl) Inject(new InputEventKey { Keycode = Godot.Key.Ctrl, PhysicalKeycode = Godot.Key.Ctrl, Pressed = true, CtrlPressed = true });
        if (s.Shift) Inject(new InputEventKey { Keycode = Godot.Key.Shift, PhysicalKeycode = Godot.Key.Shift, Pressed = true, ShiftPressed = true, CtrlPressed = s.Ctrl });
        Inject(new InputEventKey { Keycode = s.Key, PhysicalKeycode = s.Key, Pressed = true, ShiftPressed = s.Shift, CtrlPressed = s.Ctrl, Unicode = s.Ch });
        await Frames(1);
        Inject(new InputEventKey { Keycode = s.Key, PhysicalKeycode = s.Key, Pressed = false, ShiftPressed = s.Shift, CtrlPressed = s.Ctrl });
        if (s.Shift) Inject(new InputEventKey { Keycode = Godot.Key.Shift, PhysicalKeycode = Godot.Key.Shift, Pressed = false, CtrlPressed = s.Ctrl });
        if (s.Ctrl) Inject(new InputEventKey { Keycode = Godot.Key.Ctrl, PhysicalKeycode = Godot.Key.Ctrl, Pressed = false });
        await Frames(1);
    }

    /// <summary>What a stray key would change, apart from the panels themselves.</summary>
    private string GameState() =>
        $"at {App.Navigator.Current} · {App.Experience} · overview {P.UniverseOpen} · busy {P.Busy} · legend {App.Hud.LegendVisible} · help {App.Hud.HelpVisible} · stats {App.Hud.StatsVisible} · {App.Hud.HiddenButtonText} · jumps {P.Walker.Jumps}";

    private bool AnyPanelOpen => P.ListOpen || App.Hud.LocationsVisible || App.Hud.SettingsVisible || App.Hud.ReadmeVisible || P.PreviewOpen;

    private string KeyboardAt => App.GetViewport().GuiGetFocusOwner() switch
    {
        null => "nothing",
        LineEdit l when l == App.Hud.PathEdit => "the path field",
        LineEdit => "the filter",
        Godot.Tree => "the results",
        ItemList when App.Hud.LocationListFocused => "the location rows",
        var c => c.GetType().Name,
    };

    /// <summary>Esc until nothing is open (at most five presses), then a moment for the world to settle.</summary>
    private async Task CloseAllPanels()
    {
        for (var i = 0; i < 5 && AnyPanelOpen; i++)
        {
            await Key(Godot.Key.Escape);
            await Frames(2);
        }

        await Until(() => !P.Busy, 20);
    }

    /// <summary>Moves the keyboard into the panel's results with keys (Destinations: an empty filter then ↓; Locations: Tab from the path field).</summary>
    private async Task<bool> IntoResults(DialogCase c)
    {
        if (c.Panel == "Destinations")
        {
            for (var i = 0; i < 40 && P.DestinationFilterText.Length > 0; i++) await Strike(new Stroke("Backspace", Godot.Key.Backspace));
            if (!P.DestinationTreeFocused) await Strike(new Stroke("Down", Godot.Key.Down));
            return P.DestinationTreeFocused;
        }

        for (var i = 0; i < 3 && !App.Hud.LocationListFocused; i++) await Strike(new Stroke("Tab", Godot.Key.Tab));
        return App.Hud.LocationListFocused;
    }

    /// <summary>
    /// Opens the panel, types the set at once (one key at a time, each judged on its own), then again from the results
    /// where the panel has them, then Esc. Returns the leaks this case found.
    /// </summary>
    private async Task<int> TypeIntoPanel(DialogCase c, string pass)
    {
        await CloseAllPanels();
        await c.Open();
        await Until(c.IsOpen, 8);
        await Frames(3);
        var tag = $"{c.Panel} via {c.Opener} ({pass})";
        if (!c.IsOpen())
        {
            Log($"{tag}: not open yet (toast “{App.Hud.ShownToast}”); once more");
            await CloseAllPanels();
            await c.Open();
            await Until(c.IsOpen, 10);
            await Frames(3);
        }

        if (!Check(c.IsOpen(), $"{tag}: open (keyboard at {KeyboardAt}; toast “{App.Hud.ShownToast}”)")) return 1;
        var found = 0;
        var feet = P.Walker.Feet;
        var state = GameState();
        foreach (var inResults in c.HasResults ? new[] { false, true } : new[] { false })
        {
            var phase = inResults ? "in the results" : "typed at once";
            if (inResults && !Check(await IntoResults(c), $"{tag}: the keyboard moves into the results with keys (at {KeyboardAt})")) continue;
            foreach (var s in DialogStrokes(c, inResults))
            {
                if (inResults && !(c.Panel == "Destinations" ? P.DestinationTreeFocused : App.Hud.LocationListFocused)) await IntoResults(c);
                var unhandled = App.UnhandledKeyCount;
                var commands = App.ShortcutCommandCount;
                var views = App.ExperienceChangeCount;
                var at = KeyboardAt;
                App.ProbeShortcutsInert = pass == "enumerate"; // only while the key itself is typed: the openers are shortcuts too
                await Strike(s);
                App.ProbeShortcutsInert = false;
                _dialogKeysTried++;
                var what = new List<string>();
                var reached = LeakedSince(unhandled);
                if (reached.Count > 0) what.Add($"reached the shortcuts [{string.Join(",", reached)}]");
                if (App.ShortcutCommandCount != commands) what.Add($"{App.ShortcutCommandCount - commands} command(s)");
                if (App.ExperienceChangeCount != views) what.Add($"view changed to {App.Experience}");
                if (GameState() != state) what.Add($"state {state} → {GameState()}");
                if ((P.Walker.Feet - feet).Length() > 0.05f) what.Add(F($"moved {(P.Walker.Feet - feet).Length():0.00} m"));
                if (!c.IsOpen()) what.Add("closed the panel");
                if (what.Count == 0) continue;
                found++;
                var line = $"{c.Panel} / {c.Opener} / {phase} / keyboard at {at}: {s.Name} → {string.Join("; ", what)}";
                _dialogLeaks.Add($"{pass}: {line}");
                Log("LEAK " + pass + ": " + line);
                // Undo what a leak changed, so the next key is judged from the same place.
                if (App.Experience != Experience.Planets) { App.SetExperience(Experience.Planets); await WaitSettled(); }
                if (P.UniverseOpen && !state.Contains("overview True")) { await Key(Godot.Key.T); await Until(() => !P.UniverseOpen && !P.Busy, 10); }
                await Until(() => !P.Busy, 20);
                feet = P.Walker.Feet;
                state = GameState();
                if (!c.IsOpen())
                {
                    await CloseAllPanels();
                    await c.Open();
                    await Until(c.IsOpen, 8);
                    await Frames(3);
                    if (inResults) await IntoResults(c);
                }
            }
        }

        Check(found == 0, $"{tag}: no typed key reached a shortcut, changed the view or moved anything ({found} leak(s); see LEAK lines)");
        var commandsBeforeEsc = App.ShortcutCommandCount;
        await Key(Godot.Key.Escape);
        await Frames(3);
        if (c.Panel == "Reader" && P.PreviewOpen) { await Key(Godot.Key.Escape); await Frames(3); } // the Expand route returns to the preview first
        Check(!c.IsOpen() && GameState() == state, $"{tag}: Esc closes it and nothing else ({GameState()}; commands {App.ShortcutCommandCount - commandsBeforeEsc})");
        return found;
    }

    private List<DialogCase> DialogCases(int readme)
    {
        var cases = new List<DialogCase>
        {
            new("Destinations", "J", async () => await Key(Godot.Key.J), () => P.ListOpen, true, true, "j"),
            new("Destinations", "☰ Destinations", async () => await Click(App.Hud.DestinationsButtonRect.GetCenter()), () => P.ListOpen, true, true, "j"),
            new("Locations", "L", async () => await Key(Godot.Key.L), () => App.Hud.LocationsVisible, false, true, ""),
            new("Locations", "Locations (L) button", async () => await Click(App.Hud.LocationsButtonRect.GetCenter()), () => App.Hud.LocationsVisible, false, true, ""),
            new("Open location", "Ctrl+L", async () => await KeyCombo(Godot.Key.L, true), () => App.Hud.LocationsVisible, true, true, ""),
            new("Open location", "Ctrl+O", async () => await KeyCombo(Godot.Key.O, true), () => App.Hud.LocationsVisible, false, true, ""),
            new("Open location", "Open… button", async () => await Click(App.Hud.OpenLocationButtonRect.GetCenter()), () => App.Hud.LocationsVisible, false, true, ""),
            new("Settings", "F4", async () => await Key(Godot.Key.F4), () => App.Hud.SettingsVisible, false, false, ""),
            new("Settings", "⚙ Settings button", async () => await Click(App.Hud.SettingsButtonRect.GetCenter()), () => App.Hud.SettingsVisible, false, false, ""),
        };
        if (readme >= 0)
        {
            // R reads the selected folder's README, else this one's: Esc first lets go of a world a panel left selected.
            cases.Add(new("Reader", "R", async () => { await Key(Godot.Key.Escape); await Frames(2); await Key(Godot.Key.R); await Until(() => App.Hud.ReadmeVisible, 6); }, () => App.Hud.ReadmeVisible, false, false, "r"));
            cases.Add(new("Reader", "⤢ Expand on the preview", async () =>
            {
                P.Approach(readme); // [direct] as Enter on README.md in the list: hop there and look closer
                await Until(() => P.PreviewOpen && P.PreviewExpandable && !P.Busy, 10);
                await Seconds(0.4);
                await Click(P.PreviewExpandRect.GetCenter());
                await Until(() => App.Hud.ReadmeVisible, 6);
            }, () => App.Hud.ReadmeVisible, false, false, "r"));
        }

        return cases;
    }

    private async Task<int> AllDialogs(string pass, IEnumerable<DialogCase> cases)
    {
        var n = 0;
        foreach (var c in cases) n += await TypeIntoPanel(c, pass);
        await CloseAllPanels();
        return n;
    }

    private async Task DialogKeysRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5);
        var root = App.Navigator.Current;
        var workshop = Child(root, "Lantern Workshop");
        var readme = Child(root, "README.md");
        Check(workshop >= 0 && readme >= 0, "the Starter Sky has Lantern Workshop and README.md");

        // ---- pass 1: enumerate (the shortcuts log and drop what the typed keys bring them)
        var enumerated = await AllDialogs("enumerate", DialogCases(readme));
        Check(enumerated == 0, $"enumerate: no panel/key pair reaches the game's shortcuts ({enumerated} leak(s) in {_dialogKeysTried} keys)");

        // ---- pass 2: live, at home
        var live = await AllDialogs("live", DialogCases(readme));
        Check(live == 0 && App.Experience == Experience.Planets, $"live: typing in every panel changed nothing ({live} leak(s)); still in {App.Experience}");
        await Shot("dialogs-live");

        // ---- Enter in the Destinations filter travels; then every panel again there (shortcut and toolbar)
        await CloseAllPanels();
        await Key(Godot.Key.J);
        await TypeKeys("Lantern");
        await Key(Godot.Key.Enter);
        await Until(() => P.Busy, 3);
        await Until(() => !P.Busy && App.Navigator.Current == workshop, 30);
        await Seconds(1.5);
        Check(App.Navigator.Current == workshop && !P.ListOpen, $"Enter in the Destinations filter goes to Lantern Workshop ({Tree[App.Navigator.Current].Name})");
        var away = await AllDialogs("after travel", DialogCases(-1));
        Check(away == 0, $"after travel: reopened panels own the keyboard too ({away} leak(s))");

        // ---- Enter in Open location's path field comes home
        await CloseAllPanels();
        await KeyCombo(Godot.Key.L, true);
        await Until(() => App.Hud.LocationsVisible && App.Hud.PathEdit.HasFocus(), 3);
        await TypeText(Tree.FullPath(root));
        await Key(Godot.Key.Enter);
        await Until(() => !App.Hud.LocationsVisible && !P.Busy && App.Navigator.Current == root, 30);
        await Seconds(1.5);
        Check(App.Navigator.Current == root && !App.Hud.LocationsVisible, $"Enter in Open location's path field goes there and closes it ({Tree[App.Navigator.Current].Name})");
        var back = await AllDialogs("home again", DialogCases(readme).Where(c => c.Panel is "Reader" or "Locations"));
        Check(back == 0, $"home again: the reader and Locations own the keyboard ({back} leak(s))");

        // ---- in the overview
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 3);
        await Seconds(1);
        var up = await AllDialogs("overview", DialogCases(-1).Where(c => c.Opener is "J" or "L" or "Ctrl+L"));
        Check(up == 0 && P.UniverseOpen, $"overview: Destinations and Locations own the keyboard there too ({up} leak(s)); the overview stays open");
        await Key(Godot.Key.T);
        await Until(() => !P.UniverseOpen && !P.Busy, 10);

        Log($"dialog keys: {_dialogKeysTried} keys tried, {_dialogLeaks.Count} leak(s)");
        foreach (var l in _dialogLeaks) Log("  " + l);
    }
}
