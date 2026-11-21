using FileScape.Core.Interaction;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-115..119 (AD-83): the keyboard and mouse ownership table the app reads (<c>Main.KeyboardOwner</c>). Every panel ×
/// every kind of key, the topmost owner, and every panel × a press or click on the world. These carry what the in-app
/// <c>planets-dialog-keys</c> used to establish key by key (3,447 injected strokes): the journey now keeps only the cases
/// that need real event delivery (each control that can hold the focus, each opener).
/// </summary>
public class PanelOwnershipTests
{
    private static readonly PanelId[] All = Enum.GetValues<PanelId>().Where(p => p != PanelId.None).ToArray();

    private static readonly PanelId[] KeyboardPanels =
        [PanelId.Viewer, PanelId.Confirm, PanelId.Welcome, PanelId.Reader, PanelId.Settings, PanelId.Locations, PanelId.Destinations, PanelId.LocationActions, PanelId.TextField];

    private static IEnumerable<PanelKey> EveryKey()
    {
        foreach (var kind in Enum.GetValues<PanelKeyKind>())
        foreach (var echo in new[] { false, true })
        foreach (var modified in new[] { false, true })
            yield return new PanelKey(kind, echo, modified);
    }

    [Fact]
    public void Every_panel_has_exactly_one_rule()
    {
        foreach (var p in All) PanelOwnership.Rules.Count(r => r.Panel == p).Should().Be(1, $"{p} needs one rule (a new panel registers here)");
        PanelOwnership.Rules.Should().OnlyContain(r => r.Why.Length > 20 && r.Focus.Length > 0, "each rule says why and what has the keyboard");
    }

    [Fact]
    public void The_panels_that_take_the_keyboard()
    {
        All.Where(p => PanelOwnership.Rule(p).TakesKeyboard).Should().BeEquivalentTo(KeyboardPanels);
    }

    /// <summary>DJ's report: at the root landmark the location actions opened and the arrows went on walking.</summary>
    [Fact]
    public void The_location_actions_own_the_arrows_letters_and_Enter()
    {
        var owner = PanelOwnership.Owner([PanelId.LocationActions]);
        owner.Should().Be(PanelId.LocationActions);
        PanelOwnership.Route(owner, new PanelKey(PanelKeyKind.Other)).Should().Be(KeyRoute.Consume, "↑ ↓ ← →, W A S D, Space and Enter never reach the walk");
        PanelOwnership.Route(owner, new PanelKey(PanelKeyKind.Escape)).Should().Be(KeyRoute.Close);
        PanelOwnership.Route(owner, new PanelKey(PanelKeyKind.Destinations)).Should().Be(KeyRoute.Consume, "J is a letter here");
        PanelOwnership.Route(owner, new PanelKey(PanelKeyKind.GoToPath, Modified: true)).Should().Be(KeyRoute.Pass, "Ctrl+L opens Open location over it");
    }

    /// <summary>Every keyboard panel × every key: only the deliberate shortcuts pass, Esc closes, repeats do nothing.</summary>
    [Fact]
    public void Only_the_deliberate_shortcuts_pass_an_open_keyboard_panel()
    {
        foreach (var panel in KeyboardPanels)
        foreach (var key in EveryKey())
        {
            var rule = PanelOwnership.Rule(panel);
            var expected = key.Kind == PanelKeyKind.Escape ? (key.Echo ? KeyRoute.Consume : KeyRoute.Close)
                : key.Echo ? KeyRoute.Consume
                : key.Kind == PanelKeyKind.FunctionKey && rule.PassesShortcuts && !key.Modified ? KeyRoute.Pass
                : key.Kind is PanelKeyKind.GoToPath or PanelKeyKind.OpenFolder && rule.PassesShortcuts ? KeyRoute.Pass
                : key.Kind == PanelKeyKind.Destinations && rule.PassesDestinationsKey ? KeyRoute.Pass
                : KeyRoute.Consume;
            PanelOwnership.Route(panel, key).Should().Be(expected, $"{panel}: {key}");
        }

        // The deliberate passes, by name (AD-68): F keys, Ctrl+L and Ctrl+O everywhere but the choices; J from the two navigation panels.
        KeyboardPanels.Where(p => PanelOwnership.Rule(p).PassesShortcuts).Should().BeEquivalentTo([PanelId.Viewer, PanelId.Reader, PanelId.Settings, PanelId.Locations, PanelId.Destinations, PanelId.LocationActions, PanelId.TextField]);
        KeyboardPanels.Where(p => PanelOwnership.Rule(p).PassesDestinationsKey).Should().BeEquivalentTo([PanelId.Locations, PanelId.Destinations]);
        PanelOwnership.Route(PanelId.Confirm, new PanelKey(PanelKeyKind.FunctionKey)).Should().Be(KeyRoute.Consume, "a choice is waiting");
    }

    /// <summary>AD-92: the legacy Places drawer's filter (no panel row) let Enter on an empty list open the selection, PgUp
    /// travel up and the arrows walk. A focused field anywhere now owns them; a registered panel's field stays that panel's.</summary>
    [Fact]
    public void A_focused_text_field_outside_any_panel_owns_its_keys()
    {
        PanelOwnership.Owner([PanelId.TextField]).Should().Be(PanelId.TextField);
        PanelOwnership.Route(PanelId.TextField, new PanelKey(PanelKeyKind.Other)).Should().Be(KeyRoute.Consume, "Enter, PgUp, the arrows and letters are the field's");
        PanelOwnership.Route(PanelId.TextField, new PanelKey(PanelKeyKind.Escape)).Should().Be(KeyRoute.Close, "Esc lets go of the field");
        PanelOwnership.Route(PanelId.TextField, new PanelKey(PanelKeyKind.FunctionKey)).Should().Be(KeyRoute.Pass, "F1 still opens Help");
        PanelOwnership.Owner([PanelId.Destinations, PanelId.TextField], PanelId.Destinations).Should().Be(PanelId.Destinations);
        PanelOwnership.Owner([PanelId.Confirm, PanelId.TextField]).Should().Be(PanelId.Confirm, "a choice still comes first");
        PanelOwnership.OnWorldPress([PanelId.TextField], click: true).Should().Be(WorldPress.Untouched, "a click on the world acts (and takes the focus back)");
    }

    [Fact]
    public void With_no_keyboard_panel_open_every_key_passes()
    {
        foreach (var key in EveryKey()) PanelOwnership.Route(PanelId.None, key).Should().Be(KeyRoute.Pass);
        foreach (var p in All.Except(KeyboardPanels))
        {
            PanelOwnership.Owner([p]).Should().Be(PanelId.None, $"{p} accompanies play");
            foreach (var key in EveryKey()) PanelOwnership.Route(p, key).Should().Be(KeyRoute.Pass);
        }
    }

    [Fact]
    public void The_owner_is_the_modal_panel_then_the_focus_holder_then_the_topmost()
    {
        PanelOwnership.Owner([]).Should().Be(PanelId.None);
        PanelOwnership.Owner([PanelId.Locations, PanelId.Destinations], PanelId.Destinations).Should().Be(PanelId.Destinations, "the list opened last has the focus (PLANET-41)");
        PanelOwnership.Owner([PanelId.Locations, PanelId.Destinations], PanelId.Locations).Should().Be(PanelId.Locations);
        PanelOwnership.Owner([PanelId.Locations, PanelId.Destinations]).Should().Be(PanelId.Locations, "after a click on the world nothing has the focus: the fixed order");
        PanelOwnership.Owner([PanelId.Settings, PanelId.LocationActions]).Should().Be(PanelId.Settings, "F4 over the location actions");
        PanelOwnership.Owner([PanelId.Settings, PanelId.LocationActions], PanelId.Settings).Should().Be(PanelId.Settings);
        PanelOwnership.Owner([PanelId.LocationActions, PanelId.SelectionCard, PanelId.Preview]).Should().Be(PanelId.LocationActions);
        PanelOwnership.Owner([PanelId.Viewer, PanelId.Destinations], PanelId.Destinations).Should().Be(PanelId.Viewer, "the viewer covers everything");
        PanelOwnership.Owner([PanelId.Welcome, PanelId.Settings], PanelId.Settings).Should().Be(PanelId.Welcome, "a choice is needed first");
        PanelOwnership.Owner([PanelId.Destinations], PanelId.Help).Should().Be(PanelId.Destinations, "a focus holder that does not take the keyboard is ignored");
        PanelOwnership.Owner([PanelId.Destinations], PanelId.Locations).Should().Be(PanelId.Destinations, "a focus holder that is not open is ignored");
    }

    /// <summary>DJ: Settings and transient navigation or action panels close on a world click, which then does nothing else.</summary>
    [Fact]
    public void A_world_click_closes_the_transient_panels_and_does_nothing_else()
    {
        foreach (var p in new[] { PanelId.Settings, PanelId.Locations, PanelId.Help, PanelId.LegacyMenu, PanelId.Problems })
        {
            PanelOwnership.OnWorldPress([p], click: false).Should().BeEquivalentTo(new WorldPress(true, [p]), $"{p}: the press itself closes it (no drag, no selection)");
        }

        foreach (var p in new[] { PanelId.Destinations, PanelId.LocationActions, PanelId.Reader })
        {
            PanelOwnership.OnWorldPress([p], click: false).Should().Be(WorldPress.Untouched, $"{p}: a drag from the press still looks around (PLANET-25)");
            PanelOwnership.OnWorldPress([p], click: true).Should().BeEquivalentTo(new WorldPress(true, [p]), $"{p}: the click closes it and selects nothing");
        }

        PanelOwnership.OnWorldPress([PanelId.Settings, PanelId.Destinations, PanelId.SelectionCard], click: true).Should()
            .BeEquivalentTo(new WorldPress(true, [PanelId.Settings, PanelId.Destinations]), "every transient panel goes; the card stays");
        PanelOwnership.OnWorldPress([PanelId.Settings, PanelId.Destinations], click: false).Should()
            .BeEquivalentTo(new WorldPress(true, [PanelId.Settings]), "the press closes Settings and is consumed, so no drag starts; the list waits for a click");
    }

    [Fact]
    public void A_world_click_keeps_a_panel_that_needs_a_choice_and_acts_nowhere()
    {
        foreach (var p in new[] { PanelId.Confirm, PanelId.Welcome })
        foreach (var click in new[] { false, true })
        {
            PanelOwnership.OnWorldPress([p, PanelId.Settings, PanelId.Destinations], click).Should().BeEquivalentTo(new WorldPress(true, []), $"{p}: consumed, nothing closes");
        }
    }

    [Fact]
    public void A_world_click_is_the_worlds_with_only_panels_that_accompany_play()
    {
        PanelOwnership.OnWorldPress([], click: true).Should().Be(WorldPress.Untouched);
        foreach (var p in new[] { PanelId.SelectionCard, PanelId.TutorialCard, PanelId.Preview, PanelId.Bench, PanelId.Viewer })
        foreach (var click in new[] { false, true })
        {
            PanelOwnership.OnWorldPress([p], click).Should().Be(WorldPress.Untouched, $"{p}");
        }
    }

    [Fact]
    public void The_cursor_wraps_and_the_number_keys_reach_the_first_nine_rows()
    {
        PanelOwnership.MoveCursor(0, +1, 5).Should().Be(1);
        PanelOwnership.MoveCursor(4, +1, 5).Should().Be(0);
        PanelOwnership.MoveCursor(0, -1, 5).Should().Be(4);
        PanelOwnership.MoveCursor(-1, +1, 5).Should().Be(0);
        PanelOwnership.MoveCursor(-1, -1, 5).Should().Be(4);
        PanelOwnership.MoveCursor(7, +1, 5).Should().Be(0, "rows rebuilt shorter");
        PanelOwnership.MoveCursor(0, +1, 0).Should().Be(-1);
        PanelOwnership.RowForDigit(1, 5).Should().Be(0);
        PanelOwnership.RowForDigit(5, 5).Should().Be(4);
        PanelOwnership.RowForDigit(6, 5).Should().Be(-1);
        PanelOwnership.RowForDigit(0, 12).Should().Be(-1);
        PanelOwnership.RowKeyCap(0).Should().Be("1");
        PanelOwnership.RowKeyCap(8).Should().Be("9");
        PanelOwnership.RowKeyCap(9).Should().Be("", "no key reaches a tenth row: it shows no number");
    }
}
