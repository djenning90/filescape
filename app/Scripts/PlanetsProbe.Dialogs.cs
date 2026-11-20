using FileScape.App.Planets;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-dialog-keys --root=sample --vis=planets --test-shell=record</c> (PLANET-69/70, AD-68;
/// PLANET-115..119, AD-83): every panel that takes the keyboard on the planets, opened the ways a person opens it, then
/// typed into through the real event path, clicked outside, and closed.
/// <para>
/// What it keeps, and why (AD-83, 2026-11-20): Core's <c>PanelOwnershipTests</c> now carry the policy — every panel ×
/// every kind of key (only the F keys, Ctrl+L, Ctrl+O and, from the two navigation panels, J pass; Esc closes the owner;
/// repeats do nothing), the topmost owner, and every panel × a press or click on the world. This journey keeps what needs
/// real event delivery: that each opener (shortcut, toolbar button, gesture) leaves the keyboard somewhere the rule sees,
/// and that each kind of control that can hold the focus (a filter, a tree, an item list, a path field, rich text, check
/// boxes, a twirl's panel with no focus at all) leaves nothing to the game. So it types a representative set per panel —
/// the letters bound to shortcuts, two digits, Tab, Backspace, Delete, Page, Home and the arrows, Ctrl+V and Ctrl+Tab —
/// where it used to type every letter and digit three times over (an "enumerate" pass, then live, then after travel:
/// 3,447 keys, 172 s).
/// </para>
/// <para>
/// Then the new cases: the location actions at a root landmark (the sample shown with a root's look, a probe-only
/// override) opened with E and with a jump and twirl — they own the keyboard at once (the arrows and W move the
/// highlight, never the traveller), Enter runs the highlighted row (recorded), Esc closes; Settings' keys move through
/// its settings; a click on a world in the sky with each transient panel open closes the panel and selects nothing, and
/// the keyboard is the game's again; a drag beside the Destinations list still looks around (PLANET-25); the welcome
/// keeps a click elsewhere; a panel closing over another hands the keyboard back to it; all of it again after travel and
/// in the overview.
/// </para>
/// Preserve: input goes only through the real event path (key code, physical code and character together, as a keyboard
/// sends them; clicks as mouse events at window pixels); the panels' own keys (Esc, J closing Destinations, R closing the
/// reader, Enter committing) are not in the typed set, they are checked by name.
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

    /// <summary>
    /// The letters typed into every panel: the ones bound to a game shortcut on the planets (V switches to a legacy view,
    /// T the overview, B the overlook, G travel, U up, H hidden files, K the key, N sound, X discovery, Q, W, A, S, D move,
    /// R the reader, E look closer, L Locations, J Destinations). The rule treats every other letter the same way
    /// (<c>PanelOwnershipTests</c>), so typing all 26 added nothing but time.
    /// </summary>
    private const string ShortcutLetters = "vtbguhknxqwasdrelj";

    private static IEnumerable<Stroke> Letters(string ownKeys) =>
        ShortcutLetters.Where(c => !ownKeys.Contains(c)).Select(c => new Stroke(c.ToString(), (Godot.Key)char.ToUpperInvariant(c), Ch: c));

    /// <summary>The typed set for a panel: see the class summary.</summary>
    private static List<Stroke> DialogStrokes(DialogCase c, bool inResults)
    {
        var s = Letters(c.OwnKeys).ToList();
        s.AddRange("15".Select(d => new Stroke(d.ToString(), Godot.Key.Key0 + (d - '0'), Ch: d)));
        s.Add(new Stroke("Shift+V", Godot.Key.V, Shift: true, Ch: 'V'));
        if (c.EditsText && !inResults) s.Add(new Stroke("Space", Godot.Key.Space, Ch: ' '));
        s.AddRange(
        [
            new("Tab", Godot.Key.Tab), new("Shift+Tab", Godot.Key.Tab, Shift: true), new("Backspace", Godot.Key.Backspace), new("Delete", Godot.Key.Delete),
            new("PageDown", Godot.Key.Pagedown), new("Home", Godot.Key.Home),
            new("Up", Godot.Key.Up), new("Down", Godot.Key.Down), new("Left", Godot.Key.Left), new("Right", Godot.Key.Right),
            new("Ctrl+V", Godot.Key.V, Ctrl: true), new("Ctrl+Tab", Godot.Key.Tab, Ctrl: true),
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

    private bool AnyPanelOpen => P.ListOpen || App.Hud.LocationsVisible || App.Hud.SettingsVisible || App.Hud.ReadmeVisible || P.PreviewOpen || P.RootMarkPanelOpen || App.WelcomeOpen;

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

        if (!Check(c.IsOpen(), $"{tag}: open (keyboard at {KeyboardAt}, owner {App.KeyboardPanel}; toast “{App.Hud.ShownToast}”)")) return 1;
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
                await Strike(s);
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
        Check(!c.IsOpen() && GameState() == state && App.KeyboardPanel == PanelId.None && App.GetViewport().GuiGetFocusOwner() == null,
            $"{tag}: Esc closes it and nothing else; the keyboard is the game's again ({GameState()}; commands {App.ShortcutCommandCount - commandsBeforeEsc}; owner {App.KeyboardPanel}, focus {KeyboardAt})");
        return found;
    }

    private List<DialogCase> DialogCases(int readme)
    {
        var cases = new List<DialogCase>
        {
            new("Destinations", "J", async () => await Key(Godot.Key.J), () => P.ListOpen, true, true, "j"),
            new("Destinations", "☰ Destinations", async () => await Click(App.Hud.DestinationsButtonRect.GetCenter()), () => P.ListOpen, true, true, "j"),
            // J from the Locations rows opens Destinations beside them (PLANET-41): a panel key, checked by planets-navigation.
            new("Locations", "L", async () => await Key(Godot.Key.L), () => App.Hud.LocationsVisible, false, true, "j"),
            new("Locations", "Locations (L) button", async () => await Click(App.Hud.LocationsButtonRect.GetCenter()), () => App.Hud.LocationsVisible, false, true, "j"),
            new("Open location", "Ctrl+L", async () => await KeyCombo(Godot.Key.L, true), () => App.Hud.LocationsVisible, true, true, "j"),
            new("Open location", "Ctrl+O", async () => await KeyCombo(Godot.Key.O, true), () => App.Hud.LocationsVisible, false, true, "j"),
            new("Open location", "Open… button", async () => await Click(App.Hud.OpenLocationButtonRect.GetCenter()), () => App.Hud.LocationsVisible, false, true, "j"),
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

    // ------------------------------------------------------------------ the location actions (PLANET-115)

    /// <summary>[instrument: override] The home world shown with a Windows root's look, standing just in front of its landmark, facing it.</summary>
    private async Task<bool> StandAtRootMark()
    {
        await CloseAllPanels();
        if (P.Composition?.RootMark == null) await StandIn(WorldKind.WindowsRoot);
        if (P.Composition?.RootMark is not { } m || P.RootMarkFront(1.6f) is not { } front) return false;
        P.Walker.Teleport(P.Composition.Surface, front, -m.Facing); // [direct] two steps in front of the gateway
        P.FaceCameraAlong(-m.Facing);
        await Until(() => P.RootMarkFocused, 3);
        await Frames(4);
        return P.RootMarkFocused;
    }

    /// <summary>
    /// The location actions opened by <paramref name="opener"/> own the keyboard at once: held arrows and W move the
    /// highlight, never the traveller, letters and Tab reach no shortcut, Enter runs the highlighted row (recorded, not
    /// copied), Esc closes it and the keyboard is the game's again.
    /// </summary>
    private async Task LocationActionsCase(string opener, Func<Task> open, string where)
    {
        var tag = $"Location actions via {opener} ({where})";
        if (!Check(await StandAtRootMark(), $"{tag}: [instrument: override] standing at the gateway (prompt \"{P.PromptText}\")")) return;
        await open();
        await Until(() => P.RootMarkPanelOpen && P.Walker.Grounded, 4);
        await Frames(3);
        if (!Check(P.RootMarkPanelOpen && App.KeyboardPanel == PanelId.LocationActions && P.RootMarkArmed.Length > 0,
                $"{tag}: open, owning the keyboard at once on \"{P.RootMarkArmed}\" (owner {App.KeyboardPanel}; rows {string.Join(" | ", P.RootMarkActions)})")) return;
        var feet = P.Walker.Feet;
        var yaw = P.CameraHeadingDegrees();
        var state = GameState();
        var unhandled = App.UnhandledKeyCount;
        var commands = App.ShortcutCommandCount;
        var shell = App.ShellLog.Count;
        var clip = App.ClipboardLog.Count;
        await Hold(0.5, Godot.Key.Up);
        await Hold(0.5, Godot.Key.W);
        await Hold(0.4, Godot.Key.Left);
        await Hold(0.3, Godot.Key.Space);
        foreach (var s in new Stroke[] { new("t", Godot.Key.T, Ch: 't'), new("v", Godot.Key.V, Ch: 'v'), new("j", Godot.Key.J, Ch: 'j'), new("Tab", Godot.Key.Tab), new("Backspace", Godot.Key.Backspace), new("Ctrl+V", Godot.Key.V, Ctrl: true) })
        {
            await Strike(s);
            _dialogKeysTried++;
        }

        await Frames(4);
        var moved = (P.Walker.Feet - feet).Length();
        var turned = MathF.Abs(Mathf.Wrap(P.CameraHeadingDegrees() - yaw, -180f, 180f));
        Check(moved < 0.05f && turned < 0.5f && GameState() == state && App.UnhandledKeyCount == unhandled && App.ShortcutCommandCount == commands && P.RootMarkPanelOpen && App.ShellLog.Count == shell && App.ClipboardLog.Count == clip,
            F($"{tag}: ↑, W, ←, Space held, then T V J Tab Backspace Ctrl+V: nothing moved ({moved:0.00} m, turned {turned:0.0}°), reached the game ({App.UnhandledKeyCount - unhandled} key(s), {App.ShortcutCommandCount - commands} command(s)) or ran; still open"));
        // Enter runs the highlighted row: on sample data the rows copy the path (recorded, never put on the clipboard).
        var chosen = P.RootMarkArmed;
        await Key(Godot.Key.Enter);
        await Frames(3);
        Check(App.ClipboardLog.Count + App.ShellLog.Count == clip + shell + 1 && P.RootMarkPanelOpen, $"{tag}: Enter runs the highlighted row \"{chosen}\" (recorded: {(App.ClipboardLog.Count > clip ? App.ClipboardLog[^1] : App.ShellLog.Count > shell ? App.ShellLog[^1] : "nothing")})");
        await Key(Godot.Key.Escape);
        await Frames(3);
        Check(!P.RootMarkPanelOpen && App.KeyboardPanel == PanelId.None, $"{tag}: Esc closes it (owner {App.KeyboardPanel})");
        await KeysAreTheGamesAgain(tag);
    }

    /// <summary>
    /// After a panel closed: S walks again (the keyboard is the game's, nothing holds the GUI focus); S, back from the
    /// camera, so a landmark in front does not stop the step. In the overview the walk is not the keys' (only the focus).
    /// </summary>
    private async Task KeysAreTheGamesAgain(string tag)
    {
        if (P.UniverseOpen)
        {
            Check(App.GetViewport().GuiGetFocusOwner() == null && App.KeyboardPanel == PanelId.None, $"{tag}: the keyboard is the overview's again (focus {KeyboardAt})");
            return;
        }

        await Until(() => !P.Busy && P.Walker.Grounded, 5);
        var feet = P.Walker.Feet;
        await Hold(0.35, Godot.Key.S);
        await Frames(6);
        var walked = (P.Walker.Feet - feet).Length();
        Check(walked > 0.3f && App.GetViewport().GuiGetFocusOwner() == null, F($"{tag}: then S walks again ({walked:0.00} m; focus {KeyboardAt})"));
    }

    // ------------------------------------------------------------------ a click on the world (PLANET-117)

    /// <summary>A point on the world a click would act on (a world in the sky, else the sky above the horizon), clear of every panel.</summary>
    private Vector2 WorldPoint()
    {
        if (P.ClickableWorld() is { } w) return w.Centre;
        var size = App.GetViewport().GetVisibleRect().Size;
        foreach (var p in new[] { new Vector2(0.5f, 0.3f), new Vector2(0.25f, 0.3f), new Vector2(0.75f, 0.3f), new Vector2(0.5f, 0.55f) })
        {
            var at = p * size;
            if (!P.OverHudAt(at)) return at;
        }

        return size * new Vector2(0.5f, 0.3f);
    }

    /// <summary>
    /// With the panel open, a click on the world closes it and does nothing else (no selection, no travel, no view
    /// change); the keyboard is the game's again. For <paramref name="keep"/> panels the click is kept and the panel stays.
    /// </summary>
    private async Task ClickOutside(string panel, Func<Task> open, Func<bool> isOpen, bool keep = false)
    {
        await CloseAllPanels();
        App.Selection.Select(-1);
        await Frames(2);
        await open();
        await Until(isOpen, 6);
        await Frames(3);
        if (!Check(isOpen(), $"click outside {panel}: open")) return;
        var at = WorldPoint();
        var selected = App.Selection.Selected;
        var dismissals = App.WorldPressDismissals;
        var state = GameState();
        await Click(at);
        await Frames(4);
        if (keep)
        {
            Check(isOpen() && App.Selection.Selected == selected && GameState() == state, F($"click outside {panel} at ({at.X:0},{at.Y:0}): kept (it needs a choice), nothing selected, nothing else changed"));
            return;
        }

        Check(!isOpen() && App.Selection.Selected == selected && GameState() == state && App.WorldPressDismissals == dismissals + 1 && App.KeyboardPanel == PanelId.None,
            F($"click outside {panel} at ({at.X:0},{at.Y:0}): it closes and the click does nothing else (selection {App.Selection.Selected}, was {selected}; {GameState()}; owner {App.KeyboardPanel})"));
        await KeysAreTheGamesAgain($"click outside {panel}");
        // The next click acts as usual (it is not the second half of a double-click: nothing travels).
        at = WorldPoint();
        await Click(at);
        await Frames(4);
        Check(!P.Busy, F($"click outside {panel}: the next click on the world is a single click (no travel; selection {App.Selection.Selected})"));
        App.Selection.Select(-1);
    }

    /// <summary>A right-drag beside the open Destinations list still turns the camera, and the list stays (PLANET-25).</summary>
    private async Task DragBesideList()
    {
        await CloseAllPanels();
        await Key(Godot.Key.J);
        await Until(() => P.ListOpen, 4);
        var yaw = P.CameraHeadingDegrees();
        await DragCamera(new Vector2(240, 0));
        await Frames(4);
        var turned = MathF.Abs(Mathf.Wrap(P.CameraHeadingDegrees() - yaw, -180f, 180f));
        Check(turned > 20 && P.ListOpen, F($"a right-drag with the list open looks around ({turned:0.0}°) and the list stays (PLANET-25)"));
        await CloseAllPanels();
    }

    // ------------------------------------------------------------------ Settings, the welcome, a panel closing over another

    /// <summary>Settings' keys: the first setting has the keyboard on opening, Tab moves on inside it, Space toggles the focused box (then back).</summary>
    private async Task SettingsKeys()
    {
        await CloseAllPanels();
        await Key(Godot.Key.F4);
        await Until(() => App.Hud.SettingsVisible, 4);
        await Frames(2);
        var first = App.GetViewport().GuiGetFocusOwner();
        Check(first is CheckBox && App.Hud.SettingsHolds(first), $"Settings: the first setting has the keyboard on opening ({KeyboardAt})");
        var before = App.Hud.ReverseHorizontalChecked;
        await Strike(new Stroke("Space", Godot.Key.Space, Ch: ' '));
        var toggled = App.Hud.ReverseHorizontalChecked;
        await Strike(new Stroke("Space", Godot.Key.Space, Ch: ' '));
        await Strike(new Stroke("Tab", Godot.Key.Tab));
        var next = App.GetViewport().GuiGetFocusOwner();
        Check(toggled != before && App.Hud.ReverseHorizontalChecked == before && next != null && next != first && App.Hud.SettingsHolds(next) && P.Walker.Jumps == _jumpsAtStart,
            $"Settings: Space toggles the focused setting (and back), Tab moves to the next ({next?.GetType().Name}); Space did not jump");
        await CloseAllPanels();
    }

    private int _jumpsAtStart;

    /// <summary>The welcome (shown as a first launch shows it): ← → move between its answers, letters reach nothing, a click elsewhere is kept, Esc is "Explore on my own".</summary>
    private async Task WelcomeKeys()
    {
        await CloseAllPanels();
        App.ProbeShowWelcome(); // [instrument] a person's first launch shows it; automation never does
        await Frames(3);
        var first = App.GetViewport().GuiGetFocusOwner();
        await Strike(new Stroke("Right", Godot.Key.Right));
        var second = App.GetViewport().GuiGetFocusOwner();
        var unhandled = App.UnhandledKeyCount;
        var state = GameState();
        foreach (var s in new Stroke[] { new("t", Godot.Key.T, Ch: 't'), new("v", Godot.Key.V, Ch: 'v'), new("w", Godot.Key.W, Ch: 'w') }) await Strike(s);
        Check(App.KeyboardPanel == PanelId.Welcome && first is Button && second is Button && second != first && App.UnhandledKeyCount == unhandled && GameState() == state && App.WelcomeOpen,
            $"the welcome owns the keyboard: its first answer has it, → moves to the next ({(second as Button)?.Text}), T V W reach nothing");
        await Click(WorldPoint());
        await Frames(3);
        Check(App.WelcomeOpen && GameState() == state, "the welcome keeps a click on the world (it needs an answer)");
        await Key(Godot.Key.Escape);
        await Frames(3);
        Check(!App.WelcomeOpen && App.KeyboardPanel == PanelId.None, "Esc on the welcome is \"Explore on my own\"");
    }

    /// <summary>F4 over an open panel, then Esc: Settings closes and the panel underneath has the keyboard again.</summary>
    private async Task PanelClosingOverAnother()
    {
        await CloseAllPanels();
        await Key(Godot.Key.J);
        await Until(() => P.ListOpen, 4);
        await Key(Godot.Key.F4);
        await Until(() => App.Hud.SettingsVisible, 4);
        Check(App.KeyboardPanel == PanelId.Settings && P.ListOpen, $"F4 over Destinations: Settings owns the keyboard ({App.KeyboardPanel})");
        await Key(Godot.Key.Escape);
        await Frames(3);
        var owner = App.KeyboardPanel;
        await TypeKeys("lan");
        await Frames(2);
        Check(!App.Hud.SettingsVisible && P.ListOpen && owner == PanelId.Destinations && P.DestinationFilterText == "lan",
            $"Esc closes only Settings; the list has the keyboard again: typing lands in its filter (“{P.DestinationFilterText}”, owner {owner})");
        await CloseAllPanels();

        if (!await StandAtRootMark()) return;
        await Key(Godot.Key.E);
        await Until(() => P.RootMarkPanelOpen, 3);
        await KeyCombo(Godot.Key.L, true);
        await Until(() => App.Hud.LocationsVisible, 4);
        Check(App.KeyboardPanel == PanelId.Locations && P.RootMarkPanelOpen, $"Ctrl+L over the location actions: Open location owns the keyboard ({App.KeyboardPanel})");
        await Key(Godot.Key.Escape);
        await Frames(3);
        var feet = P.Walker.Feet;
        var before = P.RootMarkArmed;
        await Key(Godot.Key.Down);
        await Hold(0.3, Godot.Key.W);
        Check(!App.Hud.LocationsVisible && P.RootMarkPanelOpen && App.KeyboardPanel == PanelId.LocationActions && (P.Walker.Feet - feet).Length() < 0.05f && (P.RootMarkActions.Count < 2 || P.RootMarkArmed != before),
            $"Esc closes only Open location; the location actions have the keys again (↓: \"{before}\" → \"{P.RootMarkArmed}\"; W moved nothing)");
        await CloseAllPanels();
    }

    private async Task DialogKeysRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5);
        _jumpsAtStart = P.Walker.Jumps;
        var root = App.Navigator.Current;
        var workshop = Child(root, "Lantern Workshop");
        var readme = Child(root, "README.md");
        Check(workshop >= 0 && readme >= 0, "the Starter Sky has Lantern Workshop and README.md");

        // ---- every panel, by shortcut and by toolbar (the representative typed set)
        var live = await AllDialogs("home", DialogCases(readme));
        Check(live == 0 && App.Experience == Experience.Planets, $"home: typing in every panel changed nothing ({live} leak(s)); still in {App.Experience}");
        await Shot("dialogs-live");
        await SettingsKeys();
        await WelcomeKeys();

        // ---- a click on the world with each transient panel open (PLANET-117); a drag beside the list still looks
        await ClickOutside("Settings", async () => await Key(Godot.Key.F4), () => App.Hud.SettingsVisible);
        await ClickOutside("Locations", async () => await Key(Godot.Key.L), () => App.Hud.LocationsVisible);
        await ClickOutside("Destinations", async () => await Key(Godot.Key.J), () => P.ListOpen);
        await ClickOutside("the reader", async () => { await Key(Godot.Key.Escape); await Frames(2); await Key(Godot.Key.R); }, () => App.Hud.ReadmeVisible);
        await ClickOutside("Help", async () => await Key(Godot.Key.F1), () => App.Hud.HelpVisible);
        await DragBesideList();

        // ---- the location actions at a root landmark (PLANET-115): by E, by a jump and twirl, and a world click
        await LocationActionsCase("E", async () => await Key(Godot.Key.E), "home");
        await LocationActionsCase("a jump and twirl", async () => await JumpAndTwirl(), "home");
        if (await StandAtRootMark()) await ClickOutside("the location actions", async () => await Key(Godot.Key.E), () => P.RootMarkPanelOpen);
        await PanelClosingOverAnother();
        P.ProbeCharacter = null;
        P.ProbeRebuild();
        await Seconds(1);

        // ---- Enter in the Destinations filter travels; then each panel again there (its shortcut)
        await CloseAllPanels();
        await Key(Godot.Key.J);
        await TypeKeys("Lantern");
        await Key(Godot.Key.Enter);
        await Until(() => P.Busy, 3);
        await Until(() => !P.Busy && App.Navigator.Current == workshop, 30);
        await Seconds(1.5);
        Check(App.Navigator.Current == workshop && !P.ListOpen, $"Enter in the Destinations filter goes to Lantern Workshop ({Tree[App.Navigator.Current].Name})");
        var away = await AllDialogs("after travel", DialogCases(-1).Where(c => c.Opener is "J" or "L" or "Ctrl+L" or "F4"));
        Check(away == 0, $"after travel: reopened panels own the keyboard too ({away} leak(s))");
        await ClickOutside("Destinations after travel", async () => await Key(Godot.Key.J), () => P.ListOpen);

        // ---- Enter in Open location's path field comes home
        await CloseAllPanels();
        await KeyCombo(Godot.Key.L, true);
        await Until(() => App.Hud.LocationsVisible && App.Hud.PathEdit.HasFocus(), 3);
        await TypeText(Tree.FullPath(root));
        await Key(Godot.Key.Enter);
        await Until(() => !App.Hud.LocationsVisible && !P.Busy && App.Navigator.Current == root, 30);
        await Seconds(1.5);
        Check(App.Navigator.Current == root && !App.Hud.LocationsVisible, $"Enter in Open location's path field goes there and closes it ({Tree[App.Navigator.Current].Name})");
        var back = await AllDialogs("home again", DialogCases(readme).Where(c => c.Opener is "R" or "L"));
        Check(back == 0, $"home again: the reader and Locations own the keyboard ({back} leak(s))");

        // ---- in the overview
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 3);
        await Seconds(1);
        var up = await AllDialogs("overview", DialogCases(-1).Where(c => c.Opener is "J" or "L"));
        Check(up == 0 && P.UniverseOpen, $"overview: Destinations and Locations own the keyboard there too ({up} leak(s)); the overview stays open");
        await ClickOutside("Settings in the overview", async () => await Key(Godot.Key.F4), () => App.Hud.SettingsVisible);
        Check(P.UniverseOpen, "the overview stays open through a world click that closed Settings");
        await Key(Godot.Key.T);
        await Until(() => !P.UniverseOpen && !P.Busy, 10);

        Log($"dialog keys: {_dialogKeysTried} keys tried, {_dialogLeaks.Count} leak(s)");
        foreach (var l in _dialogLeaks) Log("  " + l);
    }
}
