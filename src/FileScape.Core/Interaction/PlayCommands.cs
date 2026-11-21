namespace FileScape.Core.Interaction;

/// <summary>
/// The one-shot game commands, one per command binding in the app's <c>InputBindings</c> (the held movement keys are not
/// commands). The names match the binding constants so a test can check that every binding has a member here
/// (<c>PlayCommandsTests.Every_command_binding_has_a_command</c>).
/// </summary>
public enum GameCommand
{
    /// <summary>Enter: open the selection with Windows (the overview and the telescope use Enter themselves first).</summary>
    Open,

    /// <summary>Backspace: back in history.</summary>
    HistoryBack,

    /// <summary>Esc: closes popups, then the selection (the panels' Esc is the ownership table's, AD-83).</summary>
    Parent,

    /// <summary>Home: this world's arrival meadow.</summary>
    Home,

    /// <summary>B: the overlook view (planets), the aerial view (Places), the bird's-eye view (maps).</summary>
    Overview,

    /// <summary>Tab: cycles Places → Classic FSN → District. A legacy-view switch.</summary>
    SwitchVis,

    /// <summary>F: face (frame) the selection.</summary>
    Frame,

    Help,
    Quality,
    Stats,

    /// <summary>Ctrl+O: Locations / Open location.</summary>
    OpenFolder,

    Settings,

    /// <summary>G: go to the selected world.</summary>
    EnterFolder,

    /// <summary>U / PgUp: up to the parent.</summary>
    UpPlace,

    /// <summary>Ctrl+Home: back to the folder you started in.</summary>
    RootPlace,

    /// <summary>M: Places ↔ a map. A legacy-view switch.</summary>
    MapToggle,

    /// <summary>T: the overview (planets), flight (Places).</summary>
    FlightToggle,

    /// <summary>H: hidden and system items.</summary>
    HiddenToggle,

    /// <summary>L: Locations.</summary>
    Locations,

    /// <summary>Ctrl+L: go to a typed path.</summary>
    GoToPath,

    /// <summary>K: the colour key.</summary>
    Legend,

    /// <summary>J: Destinations.</summary>
    Destinations,

    /// <summary>P: pin the selection.</summary>
    Pin,

    /// <summary>R: read (the README, or the preview in full).</summary>
    Readme,

    /// <summary>V: Planets ↔ Places. A legacy-view switch.</summary>
    WorldToggle,

    /// <summary>X: pause the overview's discovery.</summary>
    DiscoveryPause,
}

/// <summary>Where play is when a command key arrives (after any panel that owns the keyboard has declined it, AD-83).</summary>
public enum PlayContext
{
    /// <summary>On foot on a planet (FileScape's main view).</summary>
    PlanetWorld,

    /// <summary>The planets' overview (T).</summary>
    Overview,

    /// <summary>Looking through a telescope on the overlook (AD-85).</summary>
    Telescope,

    /// <summary>The legacy flat Places world.</summary>
    Places,

    /// <summary>A legacy whole-tree map (Classic FSN, District).</summary>
    Map,
}

/// <summary>What a command does in a context.</summary>
public enum CommandAnswer
{
    /// <summary>The command acts as usual.</summary>
    Act,

    /// <summary>The context steps back first (the telescope view closes), then the command acts.</summary>
    StepBackThenAct,

    /// <summary>Nothing happens: the key is consumed (it means nothing here).</summary>
    Ignore,

    /// <summary>
    /// Nothing happens, and the first such press in a session says where the earlier views are now (the toolbar's
    /// Legacy views menu): DJ used V, Tab and M for years.
    /// </summary>
    IgnoreLegacySwitch,
}

/// <summary>
/// Which one-shot game commands act in which play context (PLANET-141..143, AD-92). DJ (seventh pass): "Players still
/// reach legacy views accidentally while using Overview, navigating panels, or viewing pictures. Remove the ordinary
/// gameplay shortcuts that switch to legacy views. Keep legacy views available through deliberate menu selection."
/// <para>
/// V, Tab and M switched from the planets (world, overview, telescope) to Places or a map; Tab especially is pressed by
/// reflex in lists. They now do nothing in any planets context: the earlier views are a deliberate choice in the
/// toolbar's Legacy views menu. Inside a legacy view they keep their meaning (V back to Planets, Tab and M between the
/// legacy views), so the menu's round trip stays quick. The telescope consumes the commands that mean nothing at the
/// eyepiece (F, B, X) instead of stepping back for them, and keeps Help, Settings and the key open over the view.
/// </para>
/// The app asks <see cref="Answer"/> in <c>Main.HandleCommand</c> (every shortcut) and <c>PlanetWorld.TelescopeKey</c>
/// (what the telescope's own keys left); <c>InputBindings.CommandOf</c> maps a key event onto a <see cref="GameCommand"/>.
/// Preserve: no planets context ever answers <see cref="CommandAnswer.Act"/> for a legacy-view switch; every context ×
/// command has an answer; the legacy contexts act on everything (they are the reference experiences, unchanged).
/// </summary>
public static class PlayCommands
{
    /// <summary>The commands that switch to (or between) the legacy views.</summary>
    public static readonly IReadOnlyList<GameCommand> LegacySwitches = [GameCommand.WorldToggle, GameCommand.SwitchVis, GameCommand.MapToggle];

    /// <summary>Shown once per session when a legacy-view switch is pressed in the planets.</summary>
    public const string LegacyHint = "V, Tab and M no longer leave the planets: the earlier views are in the toolbar's Legacy views ▾ menu.";

    /// <summary>The commands the telescope steps back for (they take you somewhere else, or open something that covers the view).</summary>
    private static readonly HashSet<GameCommand> TelescopeStepsBackFor =
    [
        GameCommand.Open, GameCommand.HistoryBack, GameCommand.Parent, GameCommand.Home, GameCommand.EnterFolder, GameCommand.UpPlace,
        GameCommand.RootPlace, GameCommand.FlightToggle, GameCommand.Locations, GameCommand.GoToPath, GameCommand.OpenFolder,
        GameCommand.Destinations, GameCommand.Readme,
    ];

    /// <summary>The commands that mean nothing at the eyepiece: F faces the selection (the telescope already aims at one),
    /// B is the overlook view (you are on the overlook), X pauses the overview's discovery.</summary>
    private static readonly HashSet<GameCommand> TelescopeIgnores = [GameCommand.Frame, GameCommand.Overview, GameCommand.DiscoveryPause];

    /// <summary>What <paramref name="command"/> does in <paramref name="context"/> (see the class summary).</summary>
    public static CommandAnswer Answer(PlayContext context, GameCommand command)
    {
        if (context is PlayContext.Places or PlayContext.Map) return CommandAnswer.Act;
        if (LegacySwitches.Contains(command)) return CommandAnswer.IgnoreLegacySwitch;
        if (context != PlayContext.Telescope) return CommandAnswer.Act;
        if (TelescopeIgnores.Contains(command)) return CommandAnswer.Ignore;
        return TelescopeStepsBackFor.Contains(command) ? CommandAnswer.StepBackThenAct : CommandAnswer.Act;
    }

    /// <summary>True for a context in the planets (where the legacy-view switches are inert).</summary>
    public static bool IsPlanets(PlayContext context) => context is PlayContext.PlanetWorld or PlayContext.Overview or PlayContext.Telescope;
}
