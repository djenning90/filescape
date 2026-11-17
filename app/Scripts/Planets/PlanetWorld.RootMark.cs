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
}

/// <summary>
/// A filesystem root's landmark as a developer's entry point (PLANET-45, AD-58). The Windows volume's pane gateway and the
/// WSL distribution's terminal monolith are solid (the composer's <see cref="PlanetComposition.RootMark"/>); walking up
/// to one brightens its cue (<see cref="PlanetVisual.RootMarkGlowTarget"/>) and the prompt says "E · terminal here · copy
/// path · places". E opens a small panel: <b>1</b> open a terminal in this folder (Windows Terminal or PowerShell; for WSL
/// the distribution's own shell), <b>2</b>/<b>3</b> copy the path in its native form and the other side's, then a few
/// useful places on that filesystem (number keys or click; choosing one travels there). Nothing runs on walking up or
/// on opening the panel: only a button or its key does anything.
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
        var hint = new Label { Text = "Press a number or click · E or Esc closes · nothing runs until you choose it", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.76f, 0.9f));
        v.AddChild(_rootTitle);
        v.AddChild(_rootPath);
        v.AddChild(_rootRows);
        v.AddChild(hint);
    }

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
        SetPrompt(_rootPanel.Visible ? "number keys · choose   ·   E · close" : "E · terminal here · copy path · places",
            $"{(wsl ? "the terminal monolith" : "the pane gateway")} · {PathForms.For(tree.FullPath(_current.FolderId))[0].Text}");
        return true;
    }

    /// <summary>E at the mark: opens (or closes) its panel. Nothing runs here.</summary>
    private void ToggleRootMarkPanel()
    {
        if (_rootPanel.Visible) { _rootPanel.Visible = false; return; }
        if (RootMark is not { } m || _s.Tree is not { } tree || _current == null) return;
        _audio.Inspect();
        FillRootMarkPanel(tree, m);
        _rootPanel.Visible = true;
        GD.Print($"[planets] root mark panel: {string.Join(" | ", _rootActions.Select((a, i) => $"{i + 1} {a.Label}"))}");
    }

    /// <summary>The panel for where you stand: the terminal, the path's forms, the filesystem's places.</summary>
    private void FillRootMarkPanel(Core.Model.FsTree tree, PlanetRootMark m)
    {
        foreach (var c in _rootRows.GetChildren()) c.QueueFree();
        _rootActions.Clear();
        var path = tree.FullPath(_current!.FolderId);
        var wsl = m.Kind == WorldKind.WslRoot;
        var dev = _s as IDeveloperSession;
        _rootTitle.Text = wsl ? $"▮ {Distro(path) ?? tree.DisplayName(_current.FolderId)} · the terminal" : $"▦ {tree.DisplayName(_current.FolderId)} · the gateway";
        var forms = PathForms.For(path);
        _rootPath.Text = forms[0].Text;
        _rootPath.TooltipText = string.Join("\n", forms.Select(f => $"{f.Label}: {f.Text}"));

        if (_s.IsLive && dev != null && TerminalLaunch.For(path, dev.WindowsTerminal) is { } term)
        {
            var where = term.Program == TerminalLaunch.WslExe ? $"{Distro(path)} shell" : term.Program.EndsWith("wt.exe", StringComparison.OrdinalIgnoreCase) ? "Windows Terminal" : "PowerShell";
            AddRootAction("Open terminal here", $"{where} · {term.StartsIn}", term.Explanation, () => dev.StartTerminal(term));
        }
        else
        {
            AddRootNote("No terminal for sample data: open a real folder (Ctrl+O) to start one here.");
        }

        foreach (var f in forms)
        {
            var form = f;
            AddRootAction(form == forms[0] ? "Copy path" : $"Copy {form.Label}", form.Text, $"Copies {form.Text}",
                () => { if (dev != null) dev.CopyText(form.Text, form.Label); else DisplayServer.ClipboardSet(form.Text); });
        }

        var places = !_s.IsLive || dev == null ? []
            : wsl && Distro(path) is { } distro ? RootPlaces.ForWsl(distro, ListAhead.LinuxPath(path) ?? "/")
            : RootPlaces.ForWindows(path, dev.KnownFolders);
        if (places.Count > 0) AddRootNote("Places on this filesystem");
        foreach (var p in places)
        {
            var place = p;
            AddRootAction(place.Label, place.Detail, place.Path == RootPlaces.WslHomeMarker ? "your home (looked up when you choose it)" : place.Path, () => TravelToPlace(place, path));
        }
    }

    /// <summary>The distribution of a WSL path, or null.</summary>
    private static string? Distro(string path) => EditorLaunch.Distribution(path);

    /// <summary>One numbered row: the key, the action and a detail on the right; clicking does what the key does.</summary>
    private void AddRootAction(string label, string detail, string tooltip, Action act)
    {
        var index = _rootActions.Count;
        _rootActions.Add((label, act));
        var b = new Button { Text = $"{index + 1}   {label}    ·  {detail}", FocusMode = Control.FocusModeEnum.None, TooltipText = tooltip, Alignment = HorizontalAlignment.Left, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        b.AddThemeFontSizeOverride("font_size", 14);
        b.Pressed += () => ChooseRootAction(index);
        _rootRows.AddChild(b);
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
        act();
    }

    /// <summary>A place: the panel closes and the trip goes as a typed path does (home is looked up only now).</summary>
    private void TravelToPlace(RootPlace place, string from)
    {
        if (_s is not IDeveloperSession dev) return;
        var target = place.Path == RootPlaces.WslHomeMarker && Distro(from) is { } distro ? WslHome.Resolve(distro) : place.Path;
        _rootPanel.Visible = false;
        dev.GoToPath(target);
    }

    /// <summary>Number keys while the panel is open choose its rows (1–9, top row or keypad); true when consumed.</summary>
    private bool RootMarkKey(InputEvent e)
    {
        if (!_rootPanel.Visible || e is not InputEventKey { Pressed: true, Echo: false } k) return false;
        var code = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
        var n = code is >= Key.Key1 and <= Key.Key9 ? (int)(code - Key.Key1) : code is >= Key.Kp1 and <= Key.Kp9 ? (int)(code - Key.Kp1) : -1;
        if (n < 0) return false;
        ChooseRootAction(n);
        return true;
    }

    /// <summary>The probe state's root-mark fields (a JSON fragment ending in a comma).</summary>
    private string RootMarkStateJson()
    {
        if (RootMark is not { } m) return "\"rootMark\":null,";
        var (deg, metres) = RootMarkBearing();
        return System.FormattableString.Invariant($"\"rootMark\":{{\"kind\":\"{m.Kind}\",\"bearing\":{deg:0.#},\"face\":{metres:0.##},\"clearance\":{m.ClearanceMetres:0.##},\"focused\":{(_focusRootMark ? "true" : "false")},\"panel\":{(_rootPanel.Visible ? "true" : "false")},\"glow\":{RootMarkGlow:0.##}}},");
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
