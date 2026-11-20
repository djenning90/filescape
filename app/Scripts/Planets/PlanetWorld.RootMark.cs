using FileScape.App.Ui;
using FileScape.Core.Planets;
using FileScape.Core.Scanning;
using FileScape.Core.Shell;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// What a root landmark panel needs from the session beyond <see cref="IWorldSession"/> (PLANET-45, AD-58): starting a
/// terminal through the shell actions (recorded under <c>--test-shell=record</c>), copying text (recorded, not copied,
/// under the test shell), travelling to a path (as Ctrl+L), and this computer's known folders and Windows Terminal.
/// Main implements it (<c>Main.RootMark.cs</c>); a separate interface keeps the shared session contract unchanged.
/// </summary>
public interface IDeveloperSession
{
    /// <summary>Windows Terminal's path when it is installed, else null (looked up once).</summary>
    string? WindowsTerminal { get; }

    /// <summary>Folders Windows names (path → Home, Desktop, Documents, Downloads…); empty for synthetic data.</summary>
    IReadOnlyDictionary<string, string> KnownFolders { get; }

    /// <summary>Starts the terminal (an explicit command); reports the outcome on the HUD. True when it started (or was recorded).</summary>
    bool StartTerminal(TerminalCommand command);

    /// <summary>Puts <paramref name="text"/> on the clipboard (<paramref name="what"/> names it in the toast).</summary>
    void CopyText(string text, string what);

    /// <summary>Travels to a full path, as a typed Ctrl+L path does.</summary>
    void GoToPath(string path);

    /// <summary>Opens File Explorer at a folder (an explicit command; recorded under the test shell); reports the outcome. True when it opened (or was recorded).</summary>
    bool ShowInExplorer(string folder);
}

/// <summary>
/// A filesystem root's landmark as a developer's entry point (PLANET-45, AD-58). The Windows volume's pane gateway and the
/// WSL distribution's terminal monolith are solid (the composer's <see cref="PlanetComposition.RootMark"/>); walking up
/// to one brightens its cue (<see cref="PlanetVisual.RootMarkGlowTarget"/>) and the prompt says "E · Show location
/// actions" (PLANET-57; it was "E · terminal here · copy path · places", which DJ found compressed). E opens a small
/// panel whose rows each say what they do: <b>1</b> open a terminal in this folder (Windows Terminal or PowerShell; for
/// WSL the distribution's own shell), <b>2</b> show it in File Explorer, <b>3</b>/<b>4</b> copy the path in its native
/// form and the other side's, then a few useful places on that filesystem (number keys or click; choosing one travels
/// there). A twirl within reach is the quick route (AD-63): it opens the panel with the terminal highlighted.
/// <para>
/// PLANET-115 (AD-83): the open panel owns the keyboard (<c>Main.KeyboardOwner</c>, <see cref="Core.Interaction.PanelId.LocationActions"/>),
/// however it opened: ↑ ↓ (and Tab, Shift+Tab, Home, End) move the highlight, Enter runs the highlighted row, a number runs
/// its row, E or Esc closes, and nothing else reaches the walk (DJ: at the Ubuntu root the arrows went on walking with the
/// panel open). A click on the world closes it. Nothing runs on walking up, on jumping or twirling, or on opening the
/// panel: only Enter, a number key or a click on a row does anything.
/// </para>
/// <para>
/// The panel acts on the current folder (on a real root, the root; probes may show a root's look on a fixture folder).
/// It is rebuilt each time it opens, so it always describes where you stand.
/// </para>
/// </summary>
public partial class PlanetWorld
{
    // ---- reach and cue (by eye, 2026-11-16)
    /// <summary>E reaches the mark within this distance of its face, metres (a step or two away; the arrival point is farther).</summary>
    private const float RootMarkReachMetres = 2.2f;

    /// <summary>The cue starts to brighten this far from the face, metres, and is full within <see cref="RootMarkReachMetres"/>.</summary>
    private const float RootMarkGlowFromMetres = 7f;

    /// <summary>The panel's border: the cue's colour, a little transparent.</summary>
    private const float RootPanelBorderAlpha = 0.65f;

    private bool _focusRootMark;
    private PanelContainer _rootPanel = null!;
    private Label _rootTitle = null!, _rootPath = null!;
    private VBoxContainer _rootRows = null!;

    /// <summary>The panel's actions in key order (1, 2, …): a label for probes and what choosing it does.</summary>
    private readonly List<(string Label, Action Act)> _rootActions = [];

    /// <summary>The current world's root landmark, if it has one.</summary>
    private PlanetRootMark? RootMark => _current?.Comp.RootMark;

    /// <summary>Standing within reach of the mark (E opens its panel; probes).</summary>
    public bool RootMarkFocused => _focusRootMark;

    public bool RootMarkPanelOpen => _rootPanel.Visible;

    /// <summary>The panel's action labels in key order (probes).</summary>
    public IReadOnlyList<string> RootMarkActions => _rootActions.Select(a => a.Label).ToList();

    /// <summary>The cue's current strength, 0–1 (probes).</summary>
    public float RootMarkGlow => _current?.Visual?.RootMarkGlow ?? 0;

    /// <summary>The mark from where you stand: signed bearing from the camera's heading (degrees, + right) and distance
    /// to its face (metres); (0, −1) without one (probes steer by it).</summary>
    public (float Degrees, float Metres) RootMarkBearing() => RootMark is { } m ? (CameraTurnTo(m.Direction), RootMarkFaceDistance(m)) : (0, -1);

    /// <summary>The walker's distance to the nearest solid's edge, metres (the face, for a walker in front of it).</summary>
    private float RootMarkFaceDistance(PlanetRootMark m)
    {
        var r = _current!.Comp.Shape.Radius;
        return m.Solids.Min(s => PlanetShape.Angle(_walker.Up, s.Direction) * r - s.Radius);
    }

    /// <summary>Builds the panel (hidden). Bottom right, where the workbench's panel sits on a repository (a root has no bench).</summary>
    private void BuildRootMarkPanel(Control root)
    {
        _rootPanel = new PanelContainer { Visible = false };
        _rootPanel.AddThemeStyleboxOverride("panel", PanelStyle());
        _rootPanel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _rootPanel.OffsetLeft = -440; _rootPanel.OffsetRight = -12; _rootPanel.OffsetTop = -380; _rootPanel.OffsetBottom = -66;
        _rootPanel.GrowVertical = Control.GrowDirection.Begin;
        root.AddChild(_rootPanel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        _rootPanel.AddChild(v);
        _rootTitle = MakeLabel(18, new Color(1f, 0.93f, 0.75f), HorizontalAlignment.Left, outline: 0);
        _rootPath = MakeLabel(13, new Color(0.78f, 0.84f, 0.98f), HorizontalAlignment.Left, outline: 0);
        _rootRows = new VBoxContainer();
        _rootRows.AddThemeConstantOverride("separation", 3);
        var hint = new Label { Text = RootPanelHint, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.76f, 0.9f));
        v.AddChild(_rootTitle);
        v.AddChild(_rootPath);
        v.AddChild(_rootRows);
        v.AddChild(hint);
    }

    /// <summary>The panel's own line of keys (PLANET-115).</summary>
    public const string RootPanelHint = "↑ ↓ choose · Enter or its number runs it · click a row · E or Esc closes · nothing runs until you choose it";

    /// <summary>Walking away this far beyond reach closes the panel, metres (it describes the mark you stood at).</summary>
    private const float RootPanelCloseBeyondMetres = 2f;

    /// <summary>
    /// Every focus update (<see cref="UpdateFocus"/>): the cue brightens as you come closer (full while the panel is open),
    /// and the panel closes once you have walked away.
    /// </summary>
    private void UpdateRootMarkGlow()
    {
        _focusRootMark = false;
        if (RootMark is not { } m || _current?.Visual is not { } visual) return;
        var face = RootMarkFaceDistance(m);
        visual.RootMarkGlowTarget = _rootPanel.Visible ? 1 : Mathf.Clamp((RootMarkGlowFromMetres - face) / (RootMarkGlowFromMetres - RootMarkReachMetres), 0, 1);
        if (_rootPanel.Visible && face > RootMarkReachMetres + RootPanelCloseBeyondMetres) _rootPanel.Visible = false;
        if (!_rootPanel.Visible) _rootArmed = -1; // closed (walked away, E, Esc): nothing stays armed
    }

    /// <summary>
    /// From <see cref="UpdateFocus"/> when nothing nearer is in focus (a pad, an object, the overlook): within reach of the
    /// mark it is the focus and sets the prompt (true).
    /// </summary>
    private bool UpdateRootMarkFocus()
    {
        if (RootMark is not { } m || _current == null || _s.Tree is not { } tree || RootMarkFaceDistance(m) > RootMarkReachMetres) return false;
        _focusRootMark = true;
        var wsl = m.Kind == WorldKind.WslRoot;
        var what = $"{(wsl ? "the terminal monolith" : "the pane gateway")} · {PathForms.For(tree.FullPath(_current.FolderId))[0].Text}";
        if (!_rootPanel.Visible)
        {
            // PLANET-57 (DJ: "replace the compressed text with a clear instruction such as E · Show location actions").
            SetPrompt(RootMarkPromptText, $"{what}   ·   or twirl (Space, Space): a terminal or Explorer, then Enter");
        }
        else if (_rootArmed >= 0)
        {
            SetPrompt($"Enter · {RootMarkArmed}   ·   ↑ ↓ choose   ·   E · close", what);
        }
        else
        {
            SetPrompt("↑ ↓ choose · Enter or a number runs it   ·   E · close", what);
        }

        return true;
    }

    /// <summary>The prompt within reach of the mark with its panel closed (probes check it).</summary>
    public const string RootMarkPromptText = "E · Show location actions";

    /// <summary>E at the mark: opens (or closes) its panel. Nothing runs here.</summary>
    private void ToggleRootMarkPanel()
    {
        if (_rootPanel.Visible) { CloseRootMarkPanel(); return; }
        if (RootMark is not { } m || _s.Tree is not { } tree || _current == null) return;
        _audio.Inspect();
        FillRootMarkPanel(tree, m);
        _rootArmed = _rootActions.Count > 0 ? 0 : -1; // PLANET-115: the keyboard starts on the first action (Enter runs it)
        ShowRootQuick();
        _rootPanel.Visible = true;
        RootPanelOpens++;
        GD.Print($"[planets] root mark panel: {string.Join(" | ", _rootActions.Select((a, i) => $"{i + 1} {a.Label}"))}");
    }

    /// <summary>The panel for where you stand: the terminal, the path's forms, the filesystem's places.</summary>
    private void FillRootMarkPanel(Core.Model.FsTree tree, PlanetRootMark m)
    {
        foreach (var c in _rootRows.GetChildren()) c.QueueFree();
        _rootButtons.Clear();
        _rootDoes.Clear();
        _rootDetails.Clear();
        _rootArmed = -1;
        _rootActions.Clear();
        var path = tree.FullPath(_current!.FolderId);
        var wsl = m.Kind == WorldKind.WslRoot;
        var dev = _s as IDeveloperSession;
        _rootTitle.Text = wsl ? $"▮ {Distro(path) ?? tree.DisplayName(_current.FolderId)} · the terminal" : $"▦ {tree.DisplayName(_current.FolderId)} · the gateway";
        var forms = PathForms.For(path);
        _rootPath.Text = forms[0].Text;
        _rootPath.TooltipText = string.Join("\n", forms.Select(f => $"{f.Label}: {f.Text}"));

        // Each action says what it does under its name (PLANET-57: "the panel can then show what each action does").
        _rootQuick.Clear();
        if (_s.IsLive && dev != null && TerminalLaunch.For(path, dev.WindowsTerminal) is { } term)
        {
            var where = term.Program == TerminalLaunch.WslExe ? $"{Distro(path)}'s shell" : term.Program.EndsWith("wt.exe", StringComparison.OrdinalIgnoreCase) ? "Windows Terminal" : "PowerShell";
            _rootQuick.Add(_rootActions.Count);
            AddRootAction(TerminalLabel, $"Opens {where} in {term.StartsIn} (a new window)", term.Explanation, () => dev.StartTerminal(term));
        }
        else
        {
            AddRootNote("No terminal for sample data: open a real folder (Ctrl+O) to start one here.");
        }

        if (_s.IsLive && dev != null)
        {
            _rootQuick.Add(_rootActions.Count);
            AddRootAction(ExplorerLabel, $"Opens File Explorer at {path} (a new window)", path, () => dev.ShowInExplorer(path)); // Windows' form, as Explorer shows it
        }

        foreach (var f in forms)
        {
            var form = f;
            AddRootAction(form == forms[0] ? "Copy path" : $"Copy {form.Label}", $"Puts {form.Text} on the clipboard", $"Copies {form.Text}",
                () => { if (dev != null) dev.CopyText(form.Text, form.Label); else DisplayServer.ClipboardSet(form.Text); });
        }

        var places = !_s.IsLive || dev == null ? []
            : wsl && Distro(path) is { } distro ? RootPlaces.ForWsl(distro, ListAhead.LinuxPath(path) ?? "/")
            : RootPlaces.ForWindows(path, dev.KnownFolders);
        if (places.Count > 0) AddRootNote("Places on this filesystem · choosing one flies you there");
        foreach (var p in places)
        {
            var place = p;
            AddRootAction(place.Label, null, place.Path == RootPlaces.WslHomeMarker ? "your home (looked up when you choose it)" : place.Path, () => TravelToPlace(place, path), place.Detail);
        }

        ShowRootQuick();
    }

    /// <summary>The panel's two outward actions (the twirl's quick route can arm them, AD-63).</summary>
    public const string TerminalLabel = "Open terminal here", ExplorerLabel = "Show in Explorer";

    /// <summary>The distribution of a WSL path, or null.</summary>
    private static string? Distro(string path) => EditorLaunch.Distribution(path);

    /// <summary>
    /// One numbered row: the key and the action, with what it does on a quieter line under it (<paramref name="does"/>),
    /// or a short detail on the same line (a place's path); clicking does what the key does.
    /// </summary>
    private void AddRootAction(string label, string? does, string tooltip, Action act, string? detail = null)
    {
        var index = _rootActions.Count;
        _rootActions.Add((label, act));
        _rootDoes.Add(does ?? detail ?? "");
        _rootDetails.Add(detail);
        var b = new Button { Text = RowText(index, label, detail), FocusMode = Control.FocusModeEnum.None, TooltipText = tooltip, Alignment = HorizontalAlignment.Left, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        b.AddThemeFontSizeOverride("font_size", 14);
        b.Pressed += () => ChooseRootAction(index);
        _rootRows.AddChild(b);
        _rootButtons.Add(b);
        if (does == null) return;
        var l = MakeLabel(12, new Color(0.72f, 0.78f, 0.92f), HorizontalAlignment.Left, outline: 0);
        l.Text = "      " + does; // under the label, past the key number
        l.ClipText = true;
        l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _rootRows.AddChild(l);
    }

    // ---- the twirl's quick route (AD-63)
    /// <summary>The rows a twirl at the mark can arm, in the order it cycles through them (the terminal, Explorer).</summary>
    private readonly List<int> _rootQuick = [];

    /// <summary>The row the twirl has armed (Enter runs it), or −1.</summary>
    private int _rootArmed = -1;

    private readonly List<Button> _rootButtons = [];
    private readonly List<string> _rootDoes = [];
    private readonly List<string?> _rootDetails = [];

    /// <summary>A row's text: its number while a number key reaches it (1–9), the label, and a place's detail.</summary>
    private static string RowText(int index, string label, string? detail)
    {
        var cap = Core.Interaction.PanelOwnership.RowKeyCap(index);
        var key = cap.Length > 0 ? cap : " ";
        return detail != null ? $"{key}   {label}    ·  {detail}" : $"{key}   {label}";
    }

    /// <summary>Times the panel opened, by E or a twirl (probes).</summary>
    public int RootPanelOpens { get; private set; }

    /// <summary>Twirls at the mark that armed a row (probes).</summary>
    public int RootQuickArms { get; private set; }

    /// <summary>The armed row's label, or "" (probes).</summary>
    public string RootMarkArmed => _rootArmed >= 0 && _rootArmed < _rootActions.Count ? _rootActions[_rootArmed].Label : "";

    /// <summary>What each row says it does, in key order (probes).</summary>
    public IReadOnlyList<string> RootMarkDescriptions => _rootDoes;

    /// <summary>The armed row's highlight: a lime border and a "▶" before its key (the walkthrough's colour: "this one").</summary>
    private static readonly Color ArmedColor = Planets.TutorialCue.CueColor;

    /// <summary>
    /// A twirl within reach of the mark (AD-63): the quick route. The twirl opens the panel (if it is closed) with
    /// "Open terminal here" highlighted, and the prompt says Enter runs it. From then on the panel owns the keyboard
    /// (PLANET-115, AD-83): ↑ ↓ move to "Show in Explorer" and the other rows (a second twirl used to; with the keys the
    /// panel's, Space no longer jumps). Nothing starts from the twirl itself: an external program starts only from Enter,
    /// a number key or a click (as AD-57 requires of movement). Away from the mark a twirl is only a twirl; with nothing
    /// to arm (sample data) it opens the panel on its first row. True when the twirl was the mark's.
    /// </summary>
    private bool TwirlAtRootMark()
    {
        if (!_focusRootMark || RootMark is not { } m || _s.Tree is not { } tree || _current == null) return false;
        if (!_rootPanel.Visible)
        {
            FillRootMarkPanel(tree, m);
            _rootPanel.Visible = true;
            RootPanelOpens++;
        }

        if (_rootQuick.Count == 0)
        {
            _rootArmed = _rootActions.Count > 0 ? 0 : -1; // nothing to arm: the keyboard starts on the first row
            ShowRootQuick();
            return true;
        }

        var at = _rootQuick.IndexOf(_rootArmed);
        _rootArmed = _rootQuick[(at + 1) % _rootQuick.Count];
        RootQuickArms++;
        _audio.Select();
        ShowRootQuick();
        GD.Print($"[planets] root mark: twirl armed {_rootArmed + 1} {RootMarkArmed} (Enter runs it)");
        return true;
    }

    /// <summary>Shows which row is highlighted (the one Enter runs): a lime border, "▶" and "Enter" on it.</summary>
    private void ShowRootQuick()
    {
        for (var i = 0; i < _rootButtons.Count; i++)
        {
            var b = _rootButtons[i];
            if (!GodotObject.IsInstanceValid(b)) continue;
            var armed = i == _rootArmed;
            if (armed)
            {
                var box = new StyleBoxFlat { BgColor = new Color(0.1f, 0.16f, 0.08f), BorderColor = ArmedColor, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3 };
                box.SetBorderWidthAll(2);
                box.SetCornerRadiusAll(4);
                b.AddThemeStyleboxOverride("normal", box);
                b.AddThemeColorOverride("font_color", ArmedColor);
                b.Text = $"▶ {RowText(i, _rootActions[i].Label, null)}   ·   Enter";
            }
            else if (b.HasThemeStyleboxOverride("normal"))
            {
                b.RemoveThemeStyleboxOverride("normal");
                b.RemoveThemeColorOverride("font_color");
                b.Text = RowText(i, _rootActions[i].Label, i < _rootDetails.Count ? _rootDetails[i] : null);
            }
        }
    }

    private void AddRootNote(string text)
    {
        var l = MakeLabel(12, new Color(0.85f, 0.8f, 0.6f), HorizontalAlignment.Left, outline: 0);
        l.Text = text;
        _rootRows.AddChild(l);
    }

    /// <summary>Runs the panel's action <paramref name="index"/> (0-based): only ever from a key or a click.</summary>
    public void ChooseRootAction(int index)
    {
        if (!_rootPanel.Visible || index < 0 || index >= _rootActions.Count || _universe != null) return;
        var (label, act) = _rootActions[index];
        GD.Print($"[planets] root mark: {index + 1} {label}");
        _audio.Select();
        _rootArmed = index; // the highlight stays on the chosen row (Enter again repeats it; ↑ ↓ move on)
        ShowRootQuick();
        act();
    }

    /// <summary>A place: the panel closes and the trip goes as a typed path does (home is looked up only now).</summary>
    private void TravelToPlace(RootPlace place, string from)
    {
        if (_s is not IDeveloperSession dev) return;
        var target = place.Path == RootPlaces.WslHomeMarker && Distro(from) is { } distro ? WslHome.Resolve(distro) : place.Path;
        CloseRootMarkPanel();
        dev.GoToPath(target);
    }

    /// <summary>
    /// The panel's own keys while it owns the keyboard (PLANET-115, AD-83; called by <c>Main.KeyboardOwner</c> before the
    /// shared rule): ↑ ↓, Tab and Shift+Tab move the highlight (round the ends), Home and End go to the first and last row,
    /// Enter runs the highlighted row, 1–9 (top row or keypad) run their row, E closes. A held key repeats only the
    /// moves. Everything else is left to the shared rule, which closes the panel on Esc, lets the F keys, Ctrl+L and Ctrl+O
    /// through, and keeps the rest from the game. True when used.
    /// </summary>
    public bool RootActionsKey(InputEventKey k)
    {
        if (!_rootPanel.Visible || !k.Pressed || k.CtrlPressed || k.AltPressed || k.MetaPressed) return false;
        var code = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
        var count = _rootActions.Count;
        var move = code switch
        {
            Key.Up => -1,
            Key.Down => +1,
            Key.Tab => k.ShiftPressed ? -1 : +1,
            _ => 0,
        };
        if (move != 0 || code is Key.Home or Key.End)
        {
            var to = code == Key.Home ? 0 : code == Key.End ? count - 1 : Core.Interaction.PanelOwnership.MoveCursor(_rootArmed, move, count);
            if (to >= 0 && to != _rootArmed)
            {
                _rootArmed = to;
                _audio.Select();
                ShowRootQuick();
            }

            return true;
        }

        if (k.Echo) return false;
        if (code is Key.Enter or Key.KpEnter)
        {
            if (_rootArmed >= 0) ChooseRootAction(_rootArmed);
            return true;
        }

        if (code == Key.E)
        {
            CloseRootMarkPanel();
            return true;
        }

        var digit = code is >= Key.Key1 and <= Key.Key9 ? (int)(code - Key.Key0) : code is >= Key.Kp1 and <= Key.Kp9 ? (int)(code - Key.Kp0) : 0;
        var row = Core.Interaction.PanelOwnership.RowForDigit(digit, count);
        if (row < 0) return false;
        ChooseRootAction(row);
        return true;
    }

    /// <summary>Closes the panel (E, Esc, a click on the world, a place chosen, walking away): nothing stays highlighted.</summary>
    public void CloseRootMarkPanel()
    {
        _rootPanel.Visible = false;
        _rootArmed = -1;
    }

    /// <summary>The probe state's root-mark fields (a JSON fragment ending in a comma).</summary>
    private string RootMarkStateJson()
    {
        if (RootMark is not { } m) return "\"rootMark\":null,";
        var (deg, metres) = RootMarkBearing();
        return System.FormattableString.Invariant($"\"rootMark\":{{\"kind\":\"{m.Kind}\",\"bearing\":{deg:0.#},\"face\":{metres:0.##},\"clearance\":{m.ClearanceMetres:0.##},\"focused\":{(_focusRootMark ? "true" : "false")},\"panel\":{(_rootPanel.Visible ? "true" : "false")},\"glow\":{RootMarkGlow:0.##},\"armed\":\"{RootMarkArmed}\",\"quickArms\":{RootQuickArms},\"opens\":{RootPanelOpens}}},");
    }

    /// <summary>The screen centre of the panel's row <paramref name="index"/> (0-based), or null (probes click it).</summary>
    public Vector2? RootMarkRowCentre(int index)
    {
        if (!_rootPanel.Visible) return null;
        var rows = _rootRows.GetChildren().OfType<Button>().ToList();
        return index >= 0 && index < rows.Count ? rows[index].GetGlobalRect().GetCenter() : null;
    }

    /// <summary>The direction of the mark's face centre, a step in front of it (probes stand there).</summary>
    public NVector3? RootMarkFront(float metres) => RootMark is { } m && _current != null ? PlanetFrame.Move(m.Direction, m.Facing, metres, _current.Comp.Shape.Radius) : null;
}
