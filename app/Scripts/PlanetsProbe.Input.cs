using FileScape.App.Planets;
using FileScape.Core.Model;
using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-destinations-keys --root=sample --vis=planets --test-shell=record</c> (PLANET-51, AD-62): the
/// Destinations list from the keyboard, with real key events (key code, physical code and character together, as a
/// keyboard sends them; Shift for capitals). After J, after the ☰ button and after reopening on another world: the
/// filter has the keyboard, the first letters typed (every one a shortcut: L, N, T, E, R…) land in it, and none reaches
/// the game's shortcuts (<see cref="Main.UnhandledKeys"/>, which leaves out held modifiers such as Shift). Then the route
/// without the mouse: ↓ or Tab into the results, Backspace edits the filter from there (it reached History back before),
/// Home / PageUp / End move within the results (PageUp flew up a level before), → / ← open and close a folder, Enter
/// goes. A click on the world while the list is open, then typing, still types into the filter.
/// <para>
/// The defects were reproduced first with the same instrument (2026-11-17, <c>docs/evidence/2026-11-17-input/
/// repro-destinations-keys.log</c>): typed letters after J reached the filter in every case tried (J, ☰, after travel,
/// in the overview, during a flight, the pointer resting over the list); keys in the results did not — Backspace, Home,
/// PageUp and Tab reached the shortcuts, and PageUp flew up out of the Starter Sky with the list open.
/// </para>
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>Types with real key events (key code, physical code and character), Shift for capitals, '\b' as Backspace.</summary>
    private async Task TypeKeys(string text, int framesBetween = 1)
    {
        foreach (var ch in text)
        {
            var key = ch switch { ' ' => Godot.Key.Space, '\b' => Godot.Key.Backspace, _ when char.IsDigit(ch) => Godot.Key.Key0 + (ch - '0'), _ => (Godot.Key)char.ToUpperInvariant(ch) };
            var shift = char.IsUpper(ch);
            if (shift) Inject(new InputEventKey { Keycode = Godot.Key.Shift, PhysicalKeycode = Godot.Key.Shift, Pressed = true, ShiftPressed = true });
            Inject(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true, ShiftPressed = shift, Unicode = ch == '\b' ? 0 : ch });
            await Frames(framesBetween);
            Inject(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false, ShiftPressed = shift });
            if (shift) Inject(new InputEventKey { Keycode = Godot.Key.Shift, PhysicalKeycode = Godot.Key.Shift, Pressed = false });
            await Frames(framesBetween);
        }
    }

    /// <summary>What the game's shortcuts were sent since <paramref name="count"/>.</summary>
    private List<string> LeakedSince(int count)
    {
        var n = App.UnhandledKeyCount - count;
        return App.UnhandledKeys.Skip(Math.Max(0, App.UnhandledKeys.Count - n)).ToList();
    }

    /// <summary>The state a stray shortcut would change: where you are, the panels, the overview, the experience.</summary>
    private string WorldState() => $"{App.Navigator.Current}|{App.Experience}|{P.UniverseOpen}|{App.Hud.LegendVisible}|{App.Hud.LocationsVisible}|{App.Hud.HelpVisible}|{P.PreviewOpen}|{P.ListOpen}";

    private string FocusName => App.GetViewport().GuiGetFocusOwner() switch { null => "nothing", LineEdit => "the filter", Godot.Tree => "the results", var c => c.GetType().Name };

    /// <summary>Opens the list (<paramref name="open"/>), types <paramref name="typed"/> at once, and checks where it went.</summary>
    private async Task TypeAfterOpening(string how, Func<Task> open, string typed)
    {
        if (P.ListOpen) { await Key(Godot.Key.Escape); await Frames(3); }
        await open();
        Check(P.ListOpen && P.DestinationFilterFocused && P.DestinationFilterEditing, $"{how}: the list is open and the filter has the keyboard, editing (focus: {FocusName})");
        var state = WorldState();
        var count = App.UnhandledKeyCount;
        await TypeKeys(typed);
        await Frames(2);
        var leaked = LeakedSince(count);
        Check(P.DestinationFilterText == typed, $"{how}: “{typed}” typed at once lands in the filter (“{P.DestinationFilterText}”)");
        Check(leaked.Count == 0 && WorldState() == state, $"{how}: no shortcut fired (keys that reached the game: [{string.Join(",", leaked)}]; state {(WorldState() == state ? "unchanged" : $"changed {state} → {WorldState()}")})");
    }

    private async Task DestinationsKeysRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5);
        var root = App.Navigator.Current;
        var workshop = Child(root, "Lantern Workshop");
        var lwd = Child(root, "Long Way Down");
        var meadow = Child(lwd, "Meadow Gate");
        Check(workshop >= 0 && meadow >= 0, "the Starter Sky has Lantern Workshop and the Long Way Down");

        // ---- J, then typing at once (the next frame): every letter of "Lantern" is a shortcut somewhere.
        await TypeAfterOpening("J", async () => { Key1(Godot.Key.J, true); await Frames(1); Key1(Godot.Key.J, false); await Frames(1); }, "Lantern");
        await Shot("keys-typed-after-j");

        // ---- into the results and back, without the mouse
        var count = App.UnhandledKeyCount;
        await Key(Godot.Key.Down);
        Check(P.DestinationTreeFocused && P.SelectedDestinationKey == Key0(workshop), $"↓ moves into the results, on the best match ({P.SelectedDestinationKey}, focus: {FocusName})");
        await TypeKeys("\b");
        Check(P.DestinationFilterText == "Lanter" && P.DestinationFilterFocused && App.Navigator.Current == root, $"Backspace in the results edits the filter (“{P.DestinationFilterText}”, focus: {FocusName}), it does not go back");
        await Key(Godot.Key.Tab);
        Check(P.DestinationTreeFocused, $"Tab from the filter also moves into the results (focus: {FocusName})");
        await Key(Godot.Key.Tab);
        Check(P.DestinationFilterFocused, $"Tab in the results returns to the filter (focus: {FocusName})");
        await TypeKeys("\b\b\b\b\b\b");
        Check(P.DestinationFilterText.Length == 0, $"the filter is empty again (“{P.DestinationFilterText}”)");
        await Key(Godot.Key.Down);
        var first = P.SelectedDestinationKey;
        var state = WorldState();
        await Key(Godot.Key.End);
        var last = P.SelectedDestinationKey;
        await Key(Godot.Key.Pageup);
        var paged = P.SelectedDestinationKey;
        await Key(Godot.Key.Home);
        Check(last != first && P.SelectedDestinationKey == first && paged != last, $"End, PageUp and Home move within the results ({first} → {last} → {paged} → {P.SelectedDestinationKey})");
        Check(WorldState() == state && !P.Busy, $"…and never reach the game (PageUp flew up a level before; state {(WorldState() == state ? "unchanged" : "changed")})");
        var leaked = LeakedSince(count);
        Check(leaked.Count == 0, $"no key reached the shortcuts on the way ([{string.Join(",", leaked)}])");

        // ---- open and close a folder, then go
        await Key(Godot.Key.Tab);
        await TypeKeys("long way");
        await Key(Godot.Key.Down);
        Check(P.SelectedDestinationKey == Key0(lwd), $"typing “long way” and ↓ chooses the Long Way Down ({P.SelectedDestinationKey})");
        await Key(Godot.Key.Right);
        await Until(() => RowFor(lwd)?.Expanded == true, 2);
        await Frames(2);
        Check(RowFor(lwd)?.Expanded == true, "→ opens it in place");
        await Key(Godot.Key.Right);
        await Frames(2);
        Check(P.SelectedDestinationKey == Key0(meadow), $"→ again steps into it: Meadow Gate ({P.SelectedDestinationKey})");
        await Key(Godot.Key.Left);
        await Frames(2);
        await Key(Godot.Key.Left);
        await Until(() => RowFor(lwd)?.Expanded == false, 2);
        Check(P.SelectedDestinationKey == Key0(lwd) && RowFor(lwd)?.Expanded == false, "← steps back up, ← again closes it");
        await Shot("keys-results");
        await Key(Godot.Key.Tab);
        await TypeKeys("\b\b\b\b\b\b\b\bLantern");
        Log($"before Enter: filter “{P.DestinationFilterText}”, selected {P.SelectedDestinationKey}, focus {FocusName}, rows {string.Join(" | ", P.DestinationRows.Take(8).Select(r => r.Key + ":" + r.Name))}");
        await Key(Godot.Key.Enter);
        await Until(() => P.Busy, 3);
        Check(!P.ListOpen, "Enter from the filter goes to the best match (the list steps aside)");
        await Until(() => !P.Busy && App.Navigator.Current == workshop, 30);
        await Seconds(1.5);
        Check(App.Navigator.Current == workshop, $"…and lands on Lantern Workshop ({Tree[App.Navigator.Current].Name})");

        // ---- reopened after travel: J and the ☰ button
        await TypeAfterOpening("J after travel", async () => await Key(Godot.Key.J), "Notes");
        await TypeAfterOpening("☰ Destinations after travel", async () => await Click(App.Hud.DestinationsButtonRect.GetCenter()), "Readme");
        await Shot("keys-after-travel");

        // ---- the ☰ button back home, and a click on the world while the list is open
        await Key(Godot.Key.Escape);
        await Frames(3);
        await Key(Godot.Key.Backspace); // History back: home to the Starter Sky (the list is closed, so Backspace is the game's again)
        await Until(() => !P.Busy && App.Navigator.Current == root, 30);
        await Seconds(1);
        Check(App.Navigator.Current == root, "with the list closed Backspace is History back again (home)");
        await TypeAfterOpening("☰ Destinations", async () => await Click(App.Hud.DestinationsButtonRect.GetCenter()), "Music");
        // PLANET-117 (AD-83, 2026-11-20): a click on the world closes the list and does nothing else (it used to keep the list
        // and only take the keyboard from the filter); a drag beside it still looks around, and the list keeps the keyboard.
        var selectedBefore = App.Selection.Selected;
        await ClickWorldAt(new Vector2(900, 250));
        await Frames(3);
        Check(!P.ListOpen && App.Selection.Selected == selectedBefore && !P.Busy, $"a click on the world closes the list and does nothing else (selection {App.Selection.Selected}, focus: {FocusName})");
        await TypeAfterOpening("☰ Destinations again", async () => await Click(App.Hud.DestinationsButtonRect.GetCenter()), "Music");
        await DragCamera(new Vector2(240, 0));
        await Frames(3);
        count = App.UnhandledKeyCount;
        state = WorldState();
        await TypeKeys(" Box");
        Check(P.ListOpen && P.DestinationFilterText == "Music Box" && P.DestinationFilterFocused && WorldState() == state, $"after a drag beside the open list, typing goes into its filter (“{P.DestinationFilterText}”, focus: {FocusName})");
        leaked = LeakedSince(count);
        Check(leaked.Count == 0, $"…and fires no shortcut ([{string.Join(",", leaked)}])");
        await Key(Godot.Key.Escape);
        await Frames(3);
        Check(!P.ListOpen, "Esc closes the list");

        // ---- in the overview
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 3);
        await Seconds(1);
        await TypeAfterOpening("J in the overview", async () => await Key(Godot.Key.J), "Garden");
        await Key(Godot.Key.Escape);
        await Frames(3);
        await Key(Godot.Key.T);
        await Until(() => !P.UniverseOpen && !P.Busy, 10);
        Log($"keys that reached the game this run (newest {App.UnhandledKeys.Count}): {string.Join(",", App.UnhandledKeys)}");
    }
}
