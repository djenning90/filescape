namespace FileScape.Core.Interaction;

/// <summary>
/// The controls sheet behind F1 (PLANET-154): its content, written once in <see cref="UiText"/> markup — headings for
/// each part, key caps for every key, quiet notes — and rendered by the HUD's Help panel. It lives here, apart from the
/// panel's layout, so a change to the controls is a change to this text alone (streams that remove or add a key edit
/// only their lines), and so its formatting is tested in Core.
/// Preserve: every key the game answers to in that context is named; nothing here is literal file text (it is all
/// authored), so it may use the markup freely; the sheet still reads as plain text (<see cref="UiText.Plain"/>).
/// </summary>
public static class HelpSheet
{
    /// <summary>The Planets part: FileScape's main view (AD-51).</summary>
    public const string Planets =
        "# ✦ Planets\n" +
        "FileScape's main view. New here? Press *Learn to play* above for a few minutes of guided play.\n" +
        "Each folder is a small world; its folders hang in the sky above flower launchers; files stand in groves.\n" +
        "\n" +
        "## Moving\n" +
        "- {W A S D} (or {↑} {↓}) run, {Shift} faster · drag (left or right mouse) or {←} {→} look around, also while running · wheel: zoom\n" +
        "- {Space} jump, again in the air to twirl higher · on a launcher {Space} flies there\n" +
        "- {F} face the selection; {F} again turns back to your earlier view ~(same world, within 20 s, if you have not walked off or looked away)~\n" +
        "\n" +
        "## Looking and going\n" +
        "- {E} look closer (preview) · {Enter} open with Windows · {Backspace} back · {U} up · {G} fly to the selected world\n" +
        "- {J} this world's list (every folder and file; type to filter) · {Ctrl+L} go to a path · {N} sound\n" +
        "- Every journey leaves from a launcher: you hop to the pad that leads there (toward a deeper folder, or the way back for somewhere else; with no pad, a ring of light at your feet), then take off · {Esc} before take-off stays\n" +
        "\n" +
        "## The overview ({T})\n" +
        "- Click a world to orbit it · drag or {←} {→} orbit · wheel zooms toward the pointer · middle-drag or {W A S D} pan\n" +
        "- {[} {]} the next world round its ring · {Z} up a level · {F} frame it · {Shift} faster ~(pan ×3, ← → ×1.6, a wheel notch counts twice)~\n" +
        "- {Enter} lands there · {Esc} or {T} flies back down to where you stood ~(press again to hurry)~\n" +
        "\n" +
        "## Landmarks\n" +
        "- *The telescope* on the overlook: {E} (or a twirl) looks through it · {←} {→} world to world · {Enter} flies there · {T} the overview · {Esc} steps back\n" +
        "- *Hazard Worlds* (system folders; Event Horizon in the sample): islands of rock over a black hole. Follow the lit paths, jump the gaps; step off and you fall back up to the folder above. {J}, {U}, {Backspace} and every file action work as anywhere.";

    /// <summary>The legacy views' part: the earlier experiences, kept working (AD-15, AD-51).</summary>
    public const string Legacy =
        "# Legacy views\n" +
        "The toolbar's *Legacy views* menu (or {V} and {Tab}): the earlier experiences, kept working.\n" +
        "\n" +
        "## Places ~(the flat world: you are inside a folder, on foot)~\n" +
        "Files stand around you as towers, grouped by type, each with its type's icon. Each child folder is a *gate*, named above it; *walk through a gate* to go in; the *▲ Up* gate behind you goes back. Going up arrives facing the next ▲ gate.\n" +
        "- *Walking*  {↑} / {↓} walk forward / back · {←} / {→} turn · {W A S D} walk and step sideways · hold {Shift} run\n" +
        "- hold right mouse (or left-drag) to look; letting go levels the view (on foot only) · {Space} jump: hold for a high jump, tap for a hop\n" +
        "- *Flight*  {T}: rise 16 m and look down across the place; {T} again lands you safely on foot (the view levels)\n" +
        "- {W A S D} fly level (looking down never makes you dive) · {E} / {Space} up · {Q} / {C} down · wheel: speed (faster the higher you are)\n" +
        "- the view stays where you leave it · {B} aerial view: this place, its parent and its child places from above\n" +
        "- *The aerial world*  Above 14 m every folder you know is an island: roads lead to child folders, cyan arcs are links, a warm ring marks places you have visited, a gold beacon where you are. Fly over one and press {T} to land there, or click it and press {G} to fly there. Places near you inside the folder you opened are listed as you fly; others stay \"not listed yet\" until you go there (nothing scans a drive). Distances between places are symbolic.\n" +
        "- *Panels on demand*  {J} Destinations (type to filter, {↑} {↓} {Enter}) · {K} key (colours and heights) · {F4} settings · {Esc} closes\n" +
        "- {G} go into the selected folder or link · {U} / {PgUp} up to the parent · {Backspace} back (also back through a link)\n" +
        "- {Home} this folder's arrival point · {Ctrl+Home} back to the arrival root · {M} map · {Tab} next view\n" +
        "- {L} or {Ctrl+O} open a location: ⌂ This computer (This PC: every drive and WSL distribution), a drive, Browse… · {Ctrl+L} type a path (\"This PC\" works)\n" +
        "- {H} show / hide hidden and system items · gates marked ⌂ home, ◆ Downloads, Pictures…, ⚙ projects, ★ your pins (the card says why)\n" +
        "- {P} pin or unpin the selected folder (or the one you are in) · {R} read this project's README (as text; nothing is run or fetched)\n" +
        "- A project's card offers the editors on this computer (a Windows solution's own app, VS Code, VS Code for WSL); FileScape asks first.\n" +
        "- Gates show *queued* or *listing now…* while nearby places are listed ahead; /proc, /sys, /dev, /run and /mnt are never listed ahead.\n" +
        "Selecting something never moves you: click inspects (a card with Details ▸), travel is always deliberate.\n" +
        "\n" +
        "## Moving around in the maps ~(Classic FSN, District)~\n" +
        "- {W A S D} or arrow keys move · {Q} / {E} down / up · hold {Shift} faster\n" +
        "- Hold right mouse to fly: look around with the mouse while {W A S D} / arrows move you where you look; the wheel changes flight speed; release to stop (or {Esc})\n" +
        "- Left-drag orbits around the view centre · middle-drag pans · wheel zooms in / out (while flying: flight speed)\n" +
        "- {Home} back to the start view · {B} bird's-eye overview\n" +
        "\n" +
        "## Exploring\n" +
        "- Click: select and inspect · double-click or {F}: fly to the selection\n" +
        "- {Esc} parent directory (in Places: back to the folder itself) · {Backspace} previous selection (Places: previous place)\n" +
        "- {Enter} open with Windows (asks first for programs, links and cloud files)\n" +
        "- {Tab} cycle Places / Classic FSN / District (the selection stays)\n" +
        "- {F1} this help · {F2} quality · {F3} performance stats · {F4} settings · {K} key\n" +
        "\n" +
        "~Right-drag used to pan; pan is now on the middle button.~\n" +
        "~Nothing is opened or run unless you press Open. FileScape only reads metadata.~";

    /// <summary>The whole sheet, as markup.</summary>
    public static string Markup => Planets + "\n\n" + Legacy;

    /// <summary>The sheet as BBCode for the Help panel, with the running build named at its end (traceability).</summary>
    public static string Bbcode(string? buildId) =>
        UiText.ToBbcode(Markup + (buildId != null ? "\n\n~Build: " + UiText.Lit(buildId) + "~" : ""), UiTextStyle.Panel);
}
