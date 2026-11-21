namespace FileScape.Core.Interaction;

/// <summary>
/// Every panel FileScape shows over the world, as the keyboard and mouse ownership rule knows it (PLANET-115..119, AD-83).
/// The order of the keyboard panels here is not significant; <see cref="PanelOwnership.Rules"/> gives the topmost order.
/// </summary>
public enum PanelId
{
    None,

    /// <summary>The planets' picture viewer (PLANET-96): full screen, always topmost, its own keys first.</summary>
    Viewer,

    /// <summary>A confirmation before a consequential action (Godot's exclusive dialog; it takes its keys itself).</summary>
    Confirm,

    /// <summary>The first-run welcome: three choices, one of them is needed.</summary>
    Welcome,

    /// <summary>The reading view (R, ⤢ Expand).</summary>
    Reader,

    /// <summary>Settings (F4, ⚙).</summary>
    Settings,

    /// <summary>Locations / Open location (L, Ctrl+L, Ctrl+O, Open…).</summary>
    Locations,

    /// <summary>The planets' Destinations list (J, ☰), also in its pile and telescope forms.</summary>
    Destinations,

    /// <summary>A root landmark's location actions (E or jump + twirl at the terminal monolith or the pane gateway).</summary>
    LocationActions,

    /// <summary>The help sheet (F1).</summary>
    Help,

    /// <summary>The Legacy views menu under its toolbar button.</summary>
    LegacyMenu,

    /// <summary>The "needs attention" list under the place bar.</summary>
    Problems,

    /// <summary>The selection card: it describes what you selected while you go on playing.</summary>
    SelectionCard,

    /// <summary>The walkthrough's lesson card: it guides play, so play goes on under it.</summary>
    TutorialCard,

    /// <summary>The small preview (E on a file): closes when you walk away or press E again.</summary>
    Preview,

    /// <summary>The repository workbench's list: shown while you stand at the bench.</summary>
    Bench,

    /// <summary>
    /// A text field holding the keyboard outside every panel above (the legacy Places drawer's filter today; any field a
    /// later panel forgets to register): typing, Enter, the arrows and Page keys are the field's, never the game's (AD-92).
    /// </summary>
    TextField,
}

/// <summary>What a press of a mouse button on the world (not on any panel) does while a panel is open.</summary>
public enum OutsideClick
{
    /// <summary>Nothing: the press acts on the world as usual (panels that accompany play).</summary>
    Ignore,

    /// <summary>The press closes the panel and does nothing else (a centred panel: Settings, Locations, Help, menus).</summary>
    DismissOnPress,

    /// <summary>
    /// A left click (released without a drag) closes the panel and does nothing else; a drag still looks around with the
    /// panel open (PLANET-25: a panel beside the world must not take the camera away).
    /// </summary>
    DismissOnClick,

    /// <summary>The panel needs a choice: the press is consumed and the panel stays.</summary>
    Keep,
}

/// <summary>Where a key press goes once the owning panel's own handler has declined it.</summary>
public enum KeyRoute
{
    /// <summary>On to the game's shortcuts.</summary>
    Pass,

    /// <summary>The owning panel closes (Esc).</summary>
    Close,

    /// <summary>Kept from the game: nothing happens.</summary>
    Consume,
}

/// <summary>The kinds of key press the rule tells apart (the app maps an engine key event onto one).</summary>
public enum PanelKeyKind
{
    /// <summary>Any other key: letters, digits, Space, Tab, Backspace, Enter, Page, Home/End, arrows, other chords.</summary>
    Other,

    Escape,

    /// <summary>F1–F12.</summary>
    FunctionKey,

    /// <summary>Exactly the "go to path" chord (Ctrl+L): it opens Open location.</summary>
    GoToPath,

    /// <summary>Exactly the "open folder" chord (Ctrl+O): it opens Locations.</summary>
    OpenFolder,

    /// <summary>Exactly the Destinations key (J, no modifiers).</summary>
    Destinations,
}

/// <summary>One key press as the rule sees it: its kind, whether it is a held key's repeat, and whether Ctrl, Alt or the
/// Windows key is held (Shift does not count: it makes capitals).</summary>
public readonly record struct PanelKey(PanelKeyKind Kind, bool Echo = false, bool Modified = false);

/// <summary>
/// One panel's ownership: whether it takes the keyboard while open, what it gives the keyboard on opening, what a
/// press on the world does, which game keys still pass, and why.
/// </summary>
/// <param name="TakesKeyboard">While open (and topmost) every key it does not use is kept from the game.</param>
/// <param name="Modal">It is above any panel holding the GUI focus (a choice is needed, or it covers the screen).</param>
/// <param name="Focus">What has the keyboard as it opens (what Tab, the arrows and Enter act on first).</param>
/// <param name="PassesShortcuts">The F keys, Ctrl+L and Ctrl+O still reach the game (they open the navigation panels).</param>
/// <param name="PassesDestinationsKey">J still reaches the game (from Destinations it closes the list; from Locations it opens it beside them, PLANET-41).</param>
public sealed record PanelRule(PanelId Panel, bool TakesKeyboard, bool Modal, OutsideClick OutsideClick, string Focus, bool PassesShortcuts, bool PassesDestinationsKey, string Why);

/// <summary>What a press on the world does: whether it is consumed, and which panels it closes.</summary>
public sealed record WorldPress(bool Consumed, IReadOnlyList<PanelId> Close)
{
    public static readonly WorldPress Untouched = new(false, []);
}

/// <summary>
/// The keyboard and mouse ownership policy for every panel (PLANET-115..119, AD-83; it extends AD-68): the one table the
/// app reads (<c>Main.KeyboardOwner</c>) to decide which open panel owns the keyboard, where each key it leaves goes,
/// and what a click on the world does while panels are open.
/// <para>
/// Why a table in Core: DJ found a panel (a root landmark's location actions) that opened without the keyboard, so the
/// arrows went on walking. Each panel's answer used to be spread over the dispatch code and the panel itself; here every
/// panel has one row, a test checks every panel × key and every click, and a new panel cannot be added without a row.
/// </para>
/// Preserve: Esc closes only the owning panel; a held key's repeat never passes or closes; with no keyboard panel open
/// nothing here changes what a key does; the order of <see cref="Rules"/> is the topmost order for panels nobody focused.
/// </summary>
public static class PanelOwnership
{
    /// <summary>Every panel's rule, keyboard panels in topmost order first (after a click on the world nothing has the focus).</summary>
    public static readonly IReadOnlyList<PanelRule> Rules =
    [
        new(PanelId.Viewer, true, true, OutsideClick.Ignore, "the picture", true, false,
            "Full screen above everything (PLANET-96): ← → step, Esc/R close; there is no world to click outside it."),
        new(PanelId.Confirm, true, true, OutsideClick.Keep, "Continue", false, false,
            "A consequential action waits on the answer: a click elsewhere must not answer it or act behind it."),
        new(PanelId.Welcome, true, true, OutsideClick.Keep, "Learn to play", false, false,
            "Three choices on the first launch; ← → or Tab choose, Enter answers, Esc is \"Explore on my own\"."),
        new(PanelId.Reader, true, false, OutsideClick.DismissOnClick, "the text", true, false,
            "A document beside the world: its text scrolls with the keys; a click on the world puts it away."),
        new(PanelId.Settings, true, false, OutsideClick.DismissOnPress, "the first setting", true, false,
            "A centred panel of settings, saved as they change: Tab and the arrows move, Space toggles, a world click closes it."),
        new(PanelId.Locations, true, false, OutsideClick.DismissOnPress, "the rows (or the path field for Ctrl+L)", true, true,
            "A navigation panel: typing, the rows and Enter go there; a world click closes it without going anywhere."),
        new(PanelId.Destinations, true, false, OutsideClick.DismissOnClick, "the filter", true, true,
            "A navigation list beside the world (and its pile and telescope forms): typing filters, ↓ and Tab reach the rows."),
        new(PanelId.LocationActions, true, false, OutsideClick.DismissOnClick, "the first action", true, false,
            "An action panel opened at a root landmark (E, or jump + twirl): ↑ ↓ choose, Enter or a number runs, E/Esc close."),
        new(PanelId.TextField, true, false, OutsideClick.Ignore, "the field", true, false,
            "A text field with the keyboard and no panel of its own: typing, Enter, the arrows and Page keys edit it; Esc lets go; a click on the world takes the focus back."),
        new(PanelId.Help, false, false, OutsideClick.DismissOnPress, "nothing (the game keeps the keys)", true, false,
            "A sheet to read while you try the keys it lists; a click on the world puts it away."),
        new(PanelId.LegacyMenu, false, false, OutsideClick.DismissOnPress, "nothing", true, false,
            "A drop-down menu: a click anywhere else closes it."),
        new(PanelId.Problems, false, false, OutsideClick.DismissOnPress, "nothing", true, false,
            "A drop-down list of folders that need attention: a click anywhere else closes it."),
        new(PanelId.SelectionCard, false, false, OutsideClick.Ignore, "nothing", true, false,
            "It describes the selection while you play: a click on the world selects something else (its keys are the game's)."),
        new(PanelId.TutorialCard, false, false, OutsideClick.Ignore, "nothing", true, false,
            "The walkthrough guides play, so the keys and the world stay the game's."),
        new(PanelId.Preview, false, false, OutsideClick.Ignore, "nothing", true, false,
            "A look at what you stand by: it closes when you walk away or press E; a click selects as usual."),
        new(PanelId.Bench, false, false, OutsideClick.Ignore, "nothing", true, false,
            "The workbench's list while you stand at the bench: walking off is how you leave it."),
    ];

    /// <summary>The rule for one panel (every panel but <see cref="PanelId.None"/> has one).</summary>
    public static PanelRule Rule(PanelId panel) =>
        Rules.FirstOrDefault(r => r.Panel == panel) ?? throw new ArgumentOutOfRangeException(nameof(panel), panel, "no ownership rule for this panel");

    /// <summary>
    /// The open panel that owns the keyboard, or <see cref="PanelId.None"/>: a modal panel (in rule order), else the keyboard
    /// panel holding the GUI focus (<paramref name="focusHolder"/>: Locations and Destinations can be open together,
    /// the one opened last has the focus, PLANET-41), else the first open keyboard panel in rule order.
    /// </summary>
    public static PanelId Owner(IReadOnlyCollection<PanelId> open, PanelId focusHolder = PanelId.None)
    {
        foreach (var r in Rules)
        {
            if (r.TakesKeyboard && r.Modal && open.Contains(r.Panel)) return r.Panel;
        }

        if (focusHolder != PanelId.None && open.Contains(focusHolder) && Rule(focusHolder).TakesKeyboard) return focusHolder;
        foreach (var r in Rules)
        {
            if (r.TakesKeyboard && open.Contains(r.Panel)) return r.Panel;
        }

        return PanelId.None;
    }

    /// <summary>
    /// Where a key goes while <paramref name="owner"/> owns the keyboard, after its own handler declined it: Esc closes it
    /// (a repeat does nothing), the shortcuts it lets through pass, everything else is kept from the game.
    /// <see cref="PanelId.None"/> (or a panel that does not take the keyboard) passes everything.
    /// </summary>
    public static KeyRoute Route(PanelId owner, PanelKey key)
    {
        if (owner == PanelId.None || !Rule(owner).TakesKeyboard) return KeyRoute.Pass;
        if (key.Kind == PanelKeyKind.Escape) return key.Echo ? KeyRoute.Consume : KeyRoute.Close;
        if (key.Echo) return KeyRoute.Consume;
        var rule = Rule(owner);
        var passes = key.Kind switch
        {
            PanelKeyKind.FunctionKey => rule.PassesShortcuts && !key.Modified,
            PanelKeyKind.GoToPath or PanelKeyKind.OpenFolder => rule.PassesShortcuts,
            PanelKeyKind.Destinations => rule.PassesDestinationsKey,
            _ => false,
        };
        return passes ? KeyRoute.Pass : KeyRoute.Consume;
    }

    /// <summary>
    /// A mouse press on the world (over no panel) while <paramref name="open"/> are open. <paramref name="click"/>: the
    /// press is a left click released without a drag (it is asked again on release for <see cref="OutsideClick.DismissOnClick"/>
    /// panels); false for the press itself. A panel that needs a choice keeps the press and closes nothing; otherwise the
    /// panels that dismiss on this kind of press close, and the press is consumed if any did.
    /// </summary>
    public static WorldPress OnWorldPress(IReadOnlyCollection<PanelId> open, bool click)
    {
        if (open.Count == 0) return WorldPress.Untouched;
        if (Rules.Any(r => r.OutsideClick == OutsideClick.Keep && open.Contains(r.Panel))) return new WorldPress(true, []);
        var close = Rules.Where(r => open.Contains(r.Panel) && (r.OutsideClick == OutsideClick.DismissOnPress || (click && r.OutsideClick == OutsideClick.DismissOnClick)))
            .Select(r => r.Panel).ToList();
        return close.Count > 0 ? new WorldPress(true, close) : WorldPress.Untouched;
    }

    /// <summary>
    /// The highlighted row after ↑ (−1), ↓ (+1), Home or End in a panel of <paramref name="count"/> rows: ↑ ↓ wrap round,
    /// a cursor outside the rows starts at the first (↓) or the last (↑). −1 when there are no rows.
    /// </summary>
    public static int MoveCursor(int current, int delta, int count)
    {
        if (count <= 0) return -1;
        if (current < 0 || current >= count) return delta < 0 ? count - 1 : 0;
        return ((current + delta) % count + count) % count;
    }

    /// <summary>The row a number key chooses (1–9 → 0–8) in a panel of <paramref name="count"/> rows, or −1.</summary>
    public static int RowForDigit(int digit, int count) => digit >= 1 && digit <= 9 && digit <= count ? digit - 1 : -1;

    /// <summary>The key cap shown before row <paramref name="index"/> (0-based): its number while a number key reaches it, else "".</summary>
    public static string RowKeyCap(int index) => index is >= 0 and < 9 ? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
}
