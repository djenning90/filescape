using Godot;

namespace FileScape.App;

/// <summary>
/// Named input actions and their default bindings (NAV-08). Every key or button the app reacts to is declared
/// here once, so aliases (W and Up arrow) and future remapping have a single source of truth. Movement keys use
/// physical key positions, so WASD stays in place on non-QWERTY layouts. There is no keybinding editor yet.
/// </summary>
public static class InputBindings
{
    // Continuous movement (held).
    public const string Forward = "nav_forward", Back = "nav_back", Left = "nav_left", Right = "nav_right";
    public const string Up = "nav_up", Down = "nav_down", Fast = "nav_fast";

    // Walking (WALK-03): the arrow keys turn in Places; in the maps they still move sideways (aliases of A/D).
    public const string TurnLeft = "nav_turn_left", TurnRight = "nav_turn_right", Jump = "nav_jump";

    // Mouse modes.
    public const string FlyLook = "nav_fly_look", Pan = "nav_pan", Orbit = "nav_orbit_select";

    // Commands (pressed once).
    public const string Open = "cmd_open", HistoryBack = "cmd_history_back", Parent = "cmd_parent_or_release";
    public const string Home = "cmd_home", Overview = "cmd_overview", SwitchVis = "cmd_switch_visualization";
    public const string Frame = "cmd_frame_selection", Help = "cmd_help", Quality = "cmd_quality", Stats = "cmd_stats";
    public const string OpenFolder = "cmd_open_folder", Settings = "cmd_settings";

    // Places (PLACE-06..10, AD-15).
    public const string EnterFolder = "cmd_enter_folder", UpPlace = "cmd_up_place", RootPlace = "cmd_root_place";
    public const string MapToggle = "cmd_map_toggle";

    // Walking world (WALK-06, VISI-04, LOC-01).
    public const string FlightToggle = "cmd_flight", HiddenToggle = "cmd_hidden", Locations = "cmd_locations", GoToPath = "cmd_go_to_path";

    // On-demand panels (VIEW-01/03): the key (legend) and the Destinations drawer.
    public const string Legend = "cmd_legend", Destinations = "cmd_destinations";

    public static readonly string[] MovementActions = [Forward, Back, Left, Right, TurnLeft, TurnRight, Up, Down, Fast, Jump];

    /// <summary>Human-readable bindings for Help.</summary>
    public static readonly (string Action, string Keys)[] Describe =
    [
        (Forward, "W / Up arrow"), (Back, "S / Down arrow"), (Left, "A"), (Right, "D"),
        (TurnLeft, "Left arrow (maps: move left)"), (TurnRight, "Right arrow (maps: move right)"),
        (Up, "E (flight: up)"), (Down, "Q (flight: down)"), (Fast, "Shift"), (Jump, "Space (flight: up)"),
    ];

    public static void Register()
    {
        Physical(Forward, Key.W); Logical(Forward, Key.Up);
        Physical(Back, Key.S); Logical(Back, Key.Down);
        Physical(Left, Key.A);
        Physical(Right, Key.D);
        Logical(TurnLeft, Key.Left);
        Logical(TurnRight, Key.Right);
        Physical(Jump, Key.Space);
        Physical(Up, Key.E);
        Physical(Down, Key.Q);
        Logical(Fast, Key.Shift);

        Mouse(FlyLook, MouseButton.Right);
        Mouse(Pan, MouseButton.Middle);
        Mouse(Orbit, MouseButton.Left);

        Logical(Open, Key.Enter); Logical(Open, Key.KpEnter);
        Logical(HistoryBack, Key.Backspace);
        Logical(Parent, Key.Escape);
        Logical(Home, Key.Home);
        Physical(Overview, Key.B);
        Logical(SwitchVis, Key.Tab);
        Physical(Frame, Key.F);
        Logical(Help, Key.F1);
        Logical(Quality, Key.F2);
        Logical(Stats, Key.F3);
        Logical(Settings, Key.F4);
        AddEvent(OpenFolder, new InputEventKey { Keycode = Key.O, CtrlPressed = true });
        Physical(EnterFolder, Key.G);
        Physical(UpPlace, Key.U); Logical(UpPlace, Key.Pageup);
        AddEvent(RootPlace, new InputEventKey { Keycode = Key.Home, CtrlPressed = true });
        Physical(MapToggle, Key.M);
        Physical(FlightToggle, Key.T);
        Physical(HiddenToggle, Key.H);
        AddEvent(GoToPath, new InputEventKey { Keycode = Key.L, CtrlPressed = true });
        Physical(Locations, Key.L);
        Physical(Legend, Key.K);
        Physical(Destinations, Key.J);
    }

    /// <summary>A stable per-key identity for alias tracking: same key, same id, whichever field the event carries.</summary>
    public static long KeyId(InputEvent e) => e switch
    {
        InputEventKey k => (long)(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode),
        InputEventMouseButton m => 1_000_000L + (long)m.ButtonIndex,
        _ => -1,
    };

    private static void Physical(string action, Key key) => AddEvent(action, new InputEventKey { PhysicalKeycode = key });

    private static void Logical(string action, Key key) => AddEvent(action, new InputEventKey { Keycode = key });

    private static void Mouse(string action, MouseButton button) => AddEvent(action, new InputEventMouseButton { ButtonIndex = button });

    private static void AddEvent(string action, InputEvent e)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action, 0.2f);
        }

        InputMap.ActionAddEvent(action, e);
    }
}
