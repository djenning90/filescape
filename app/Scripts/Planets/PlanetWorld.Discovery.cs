using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The overview's discovery (PLANET-76..78, AD-71): while the overview (T) is open it lists the worlds it draws that are
/// known only by name, breadth-first and the selected or zoomed-toward world's subtree first
/// (<see cref="OverviewDiscovery"/> decides; the session's explorer lists, through <see cref="Core.Scanning.WorldExplorer.Discover"/>),
/// and the overview grows as the listings arrive (<see cref="ApplyUniverseLayout"/> glides it, so nothing jumps). A
/// small indicator above the prompt says it is going on and has a Pause button; X does the same. What is learned is the
/// session's tree, so it stays after the overview closes and is what a visit would have listed (a later visit of a
/// discovered folder lists nothing again). Nothing is listed while the overview is closed, and nothing at all without a
/// live explorer (synthetic data).
/// <para>
/// Main thread only (it reads the tree and the scene). The scheduler's bounds are in <see cref="OverviewDiscovery.Limits"/>;
/// the pacing of steps and relayouts is here.
/// </para>
/// </summary>
public partial class PlanetWorld
{
    private readonly OverviewDiscovery _discovery = new();
    private PanelContainer _discoveryPanel = null!;
    private Label _discoveryLabel = null!;
    private Button _discoveryButton = null!;
    private bool _discoveryBlockingAdded;
    private TimeSpan _lastDiscoveryStep;

    /// <summary>How often the scheduler is asked for more, seconds: often enough for its rate (10 a second), cheap (one
    /// pass over the drawn worlds).</summary>
    private static readonly TimeSpan DiscoveryStepInterval = TimeSpan.FromSeconds(0.1);

    /// <summary>
    /// The least time between two relayouts while the overview is open, seconds: each costs a layout of the tree and a
    /// rebuild of the batches on the main thread (a few ms at 1,500 worlds, measured by the discovery journey), and a
    /// glide (<see cref="UniverseGrowSeconds"/>) should mostly finish before the next one starts.
    /// </summary>
    private const float UniverseRelayoutSeconds = 1.5f;

    /// <summary>The longest and the most recent relayout on the main thread, milliseconds (probes).</summary>
    public double DiscoveryRelayoutMsMax { get; private set; }
    public double DiscoveryRelayoutMsLast { get; private set; }

    /// <summary>Relayouts made while the overview was open (probes).</summary>
    public int DiscoveryRelayouts { get; private set; }

    public OverviewDiscovery DiscoveryScheduler => _discovery;

    /// <summary>Discovery is available: a live explorer behind the session.</summary>
    public bool DiscoveryAvailable => _s.Explorer != null;

    /// <summary>The indicator's text and its button's rectangle (probes, the real-input probe's state file).</summary>
    public string DiscoveryText => _discoveryLabel.Text;
    public Rect2 DiscoveryButtonRect => _discoveryButton.GetGlobalRect();
    public bool DiscoveryIndicatorShown => _discoveryPanel.Visible;

    /// <summary>The indicator: a small panel above the prompt (bottom centre), one line and a button.</summary>
    private void BuildDiscoveryPanel(Control root)
    {
        _discoveryPanel = new PanelContainer { Visible = false };
        var style = PanelStyle();
        style.ContentMarginTop = style.ContentMarginBottom = 4;
        _discoveryPanel.AddThemeStyleboxOverride("panel", style);
        _discoveryPanel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        // Viewport units: above the prompt box (whose top is 150 above the bottom edge), centred, 520 wide.
        _discoveryPanel.OffsetLeft = -260; _discoveryPanel.OffsetRight = 260; _discoveryPanel.OffsetTop = -196; _discoveryPanel.OffsetBottom = -158;
        _discoveryPanel.GrowVertical = Control.GrowDirection.Begin;
        root.AddChild(_discoveryPanel);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _discoveryPanel.AddChild(row);
        _discoveryLabel = MakeLabel(14, new Color(0.82f, 0.9f, 1f), HorizontalAlignment.Left, outline: 0);
        _discoveryLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _discoveryLabel.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_discoveryLabel);
        // No keyboard focus: a focused button would take the overview's keys (WASD, Enter) away from it.
        _discoveryButton = new Button { Text = "Pause", FocusMode = Control.FocusModeEnum.None, TooltipText = "Pause or resume listing more folders for the overview (X)" };
        _discoveryButton.Pressed += ToggleDiscoveryPause;
        row.AddChild(_discoveryButton);
    }

    /// <summary>The overview opened: discovery resumes where it was (paused stays paused).</summary>
    private void OpenDiscovery()
    {
        if (!_discoveryBlockingAdded)
        {
            _s.Hud.AddBlocking(_discoveryPanel); // names and clicks leave the indicator alone
            _discoveryBlockingAdded = true;
        }

        _lastDiscoveryStep = TimeSpan.Zero;
        foreach (var (b, _) in _uniBodies) _uniEverDrawn.Add(b.NodeId);
        DiscoveryRelayouts = 0;
        _discoveryPanel.Visible = DiscoveryAvailable;
        UpdateDiscoveryIndicator();
    }

    /// <summary>The overview closed: nothing more is asked for (listings in flight still arrive in the tree).</summary>
    private void CloseDiscovery() => _discoveryPanel.Visible = false;

    /// <summary>X or the indicator's button: pause or resume.</summary>
    public void ToggleDiscoveryPause()
    {
        if (_universe == null || !DiscoveryAvailable) return;
        _discovery.Paused = !_discovery.Paused;
        _audio.Select();
        UpdateDiscoveryIndicator();
    }

    /// <summary>
    /// Once a frame in the overview: every <see cref="DiscoveryStepInterval"/> asks the scheduler which drawn worlds to
    /// list and posts them to the explorer; when the tree has changed (anything: discovery, a visit's listings, the
    /// background traversal) and the last relayout is <see cref="UniverseRelayoutSeconds"/> old, lays the overview out
    /// again and glides into it. Not while landing (the flight aims at a fixed point).
    /// </summary>
    private void StepDiscovery(FsTree tree)
    {
        var now = _s.Now;
        if (_s.Explorer is { } explorer && now - _lastDiscoveryStep >= DiscoveryStepInterval)
        {
            _lastDiscoveryStep = now;
            var drawn = new List<OverviewDiscovery.Candidate>(_uniBodies.Count);
            for (var i = 0; i < _uniBodies.Count; i++) drawn.Add(new OverviewDiscovery.Candidate(_uniBodies[i].Body.NodeId, _uniBodies[i].Body.Depth, i));
            var focus = new List<int>(2) { _uniSelected };
            if (ZoomedToward() is >= 0 and var toward && toward != _uniSelected) focus.Add(toward);
            foreach (var id in _discovery.Step(tree, drawn, focus, now, _s.MayListAhead)) explorer.Discover(id);
        }

        if (tree.Version != _uniLayoutVersion && _uniLanding == null && (now - _uniLayoutAt).TotalSeconds >= UniverseRelayoutSeconds)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var bodies = ComputeUniverse(tree, keepDrawn: true);
            foreach (var b in bodies) _uniEverDrawn.Add(b.NodeId);
            if (bodies.Count > 0 && !SameLayout(bodies))
            {
                ApplyUniverseLayout(tree, bodies, animate: true);
                DiscoveryRelayouts++;
            }
            else
            {
                _uniLayoutVersion = tree.Version;
                _uniLayoutAt = now;
            }

            DiscoveryRelayoutMsLast = sw.Elapsed.TotalMilliseconds;
            DiscoveryRelayoutMsMax = Math.Max(DiscoveryRelayoutMsMax, DiscoveryRelayoutMsLast);
        }

        UpdateDiscoveryIndicator();
    }

    /// <summary>The layout has not changed what is drawn (same worlds, places, sizes and states): nothing to rebuild.</summary>
    private bool SameLayout(IReadOnlyList<UniverseBody> bodies)
    {
        if (bodies.Count != _uniBodies.Count) return false;
        for (var i = 0; i < bodies.Count; i++)
        {
            var (a, b) = (bodies[i], _uniBodies[i].Body);
            if (a.NodeId != b.NodeId || a.Position != b.Position || a.Discovery != b.Discovery || a.HiddenChildren != b.HiddenChildren || a.Radius != b.Radius || a.OrbitRadius != b.OrbitRadius) return false;
        }

        return true;
    }

    /// <summary>The world the camera is zoomed toward: the drawn world nearest the point it looks at (−1 when none).</summary>
    private int ZoomedToward()
    {
        var best = -1;
        var bestD = float.MaxValue;
        foreach (var (b, at) in _uniBodies)
        {
            var d = at.DistanceSquaredTo(_uniFocusTarget);
            if (d < bestD)
            {
                bestD = d;
                best = b.NodeId;
            }
        }

        return best;
    }

    /// <summary>A spinner for the indicator while discovery runs (one step every quarter second).</summary>
    private static readonly string[] DiscoverySpinner = ["◐", "◓", "◑", "◒"];

    /// <summary>The spinner's step time, seconds.</summary>
    private const double DiscoverySpinnerSeconds = 0.25;

    private void UpdateDiscoveryIndicator()
    {
        if (!_discoveryPanel.Visible) return;
        var worlds = _uniBodies.Count;
        var (listed, partial, _, _) = UniverseCounts;
        var spin = DiscoverySpinner[(int)(_s.Now.TotalSeconds / DiscoverySpinnerSeconds) % DiscoverySpinner.Length];
        _discoveryLabel.Text = _discovery.State switch
        {
            OverviewDiscoveryState.Paused => $"Discovery paused · {worlds:N0} worlds, {listed + partial:N0} listed · X resumes",
            OverviewDiscoveryState.LimitReached => $"Discovery stopped at {_discovery.Bounds.MaxTreeNodes:N0} known entries · {worlds:N0} worlds",
            OverviewDiscoveryState.Complete when worlds >= UniverseLayout.MaxBodies => $"Discovery done: the overview's {worlds:N0} worlds are as listed as they will be",
            OverviewDiscoveryState.Complete => $"Discovery done · {worlds:N0} worlds, {listed + partial:N0} listed",
            _ => $"{spin} Discovering… {worlds:N0} worlds, {listed + partial:N0} listed · X pauses",
        };
        _discoveryButton.Text = _discovery.Paused ? "Resume" : "Pause";
        _discoveryButton.Disabled = !_discovery.Paused && _discovery.State is OverviewDiscoveryState.Complete or OverviewDiscoveryState.LimitReached;
    }

    /// <summary>
    /// Worlds drawn per level below <paramref name="root"/> (0 = the root itself), and how many of each are listed: what
    /// the discovery journey measures at 0, 10 and 30 s. Only worlds inside <paramref name="root"/>'s subtree count.
    /// </summary>
    public SortedDictionary<int, (int Drawn, int Listed)> UniverseLevels(int root)
    {
        var levels = new SortedDictionary<int, (int, int)>();
        var tree = _s.Tree;
        if (tree == null || !tree.Contains(root)) return levels;
        var rootDepth = tree[root].Depth;
        foreach (var (b, _) in _uniBodies)
        {
            if (!tree.Contains(b.NodeId)) continue;
            var inside = false;
            for (var a = b.NodeId; a >= 0; a = tree[a].ParentId)
            {
                if (a != root) continue;
                inside = true;
                break;
            }

            if (!inside) continue;
            var level = tree[b.NodeId].Depth - rootDepth;
            var (d, l) = levels.GetValueOrDefault(level);
            levels[level] = (d + 1, l + (b.Discovery == Discovery.Unknown ? 0 : 1));
        }

        return levels;
    }

    /// <summary>A world's place in the overview now (universe frame, metres) or null (probes: how far worlds move).</summary>
    public Vector3? UniverseLocal(int id) => _uniIndex.TryGetValue(id, out var i) ? _uniLocal[i] : null;

    /// <summary>
    /// A drawn world a real click would select (the real-input probe): the largest on screen other than the selected one,
    /// clear of the edges and the HUD (60 px at the sides, 160 at the top, 120 at the bottom, viewport pixels), that
    /// picking at its centre returns; null when there is none.
    /// </summary>
    public (int Id, Vector2 Centre)? UniverseClickable()
    {
        if (_universe == null) return null;
        var size = GetViewport().GetVisibleRect().Size;
        foreach (var (id, c, _) in UniverseOnScreen().OrderByDescending(w => w.Pixels))
        {
            if (id == _uniSelected || c.X < 60 || c.Y < 160 || c.X > size.X - 60 || c.Y > size.Y - 120 || OverHud(c) || PickUniverse(c) != id) continue;
            return (id, c);
        }

        return null;
    }

    /// <summary>Over a HUD panel or one of the planets' own panels (probes: is a world clickable there).</summary>
    public bool OverHudAt(Vector2 p) => OverHud(p);

    /// <summary>Every world the overview has drawn while open this session (probes: was a folder ever a world).</summary>
    private readonly HashSet<int> _uniEverDrawn = [];

    public bool UniverseKnowsEver(int id) => _uniEverDrawn.Contains(id);

    /// <summary>The overview's drawn worlds, in layout order (probes).</summary>
    public IEnumerable<int> UniverseIds => _uniBodies.Select(b => b.Body.NodeId);

    /// <summary>The point the overview camera is heading to look at (global), its yaw and pitch (probes: nothing resets them).</summary>
    public (Vector3 Focus, float Yaw, float Pitch) UniverseCameraAim => (_uniFocusTarget, _uniYaw, _uniPitch);

    /// <summary>The overview's growth glide is settled (no world moving).</summary>
    public bool UniverseSettled => _uniGrowT >= UniverseGrowSeconds;

    /// <summary>The discovery part of the probe state file (PLANET-76; counts only, never names).</summary>
    private string DiscoveryStateJson()
    {
        if (_universe == null) return "";
        var levels = _s.Tree is { } t && t.Contains(_s.Navigator.Root) ? UniverseLevels(_s.Navigator.Root) : [];
        var deepest = levels.Count > 0 ? levels.Keys.Max() : -1;
        var deepestListed = levels.Where(l => l.Value.Listed > 0).Select(l => l.Key).DefaultIfEmpty(-1).Max();
        return System.FormattableString.Invariant(
            $"\"discovery\":{{\"state\":\"{_discovery.State}\",\"paused\":{(_discovery.Paused ? "true" : "false")},\"worlds\":{_uniBodies.Count},\"requested\":{_discovery.Requested},\"listed\":{_discovery.Listed},\"failed\":{_discovery.Failed},\"inFlight\":{_discovery.InFlight},\"frontier\":{_discovery.Frontier},\"levels\":{deepest + 1},\"deepestListed\":{deepestListed},\"relayouts\":{DiscoveryRelayouts},\"relayoutMsMax\":{DiscoveryRelayoutMsMax:0.0},\"button\":[{DiscoveryButtonRect.GetCenter().X:0},{DiscoveryButtonRect.GetCenter().Y:0}],\"indicator\":{(DiscoveryIndicatorShown ? "true" : "false")},\"selected\":{_uniSelected},\"clickable\":{(UniverseClickable() is { } uc ? $"[{uc.Id},{uc.Centre.X:0},{uc.Centre.Y:0}]" : "null")}}},");
    }
}
