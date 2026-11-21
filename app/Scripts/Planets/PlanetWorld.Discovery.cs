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
/// discovered folder lists nothing again). While the overview is closed it goes on more quietly at the play pace (PLANET-90, below), and nothing at all is listed without a
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

    /// <summary>
    /// The indicator: a small panel above the prompt (bottom centre): two short lines (PLANET-107: the neighbourhood you
    /// attend to, then everything else) and the Pause button in its own column. Before AD-81 it was one 520-wide line whose
    /// longest message ("Discovery done: the overview's 1,500 worlds are as listed as they will be") ran under the button.
    /// </summary>
    private void BuildDiscoveryPanel(Control root)
    {
        _discoveryPanel = new PanelContainer { Visible = false };
        var style = PanelStyle();
        style.ContentMarginTop = style.ContentMarginBottom = 5;
        _discoveryPanel.AddThemeStyleboxOverride("panel", style);
        _discoveryPanel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        // Viewport units: above the prompt box (whose top is 150 above the bottom edge), centred.
        _discoveryPanel.OffsetLeft = -DiscoveryPanelWidth / 2; _discoveryPanel.OffsetRight = DiscoveryPanelWidth / 2;
        _discoveryPanel.OffsetTop = -158 - DiscoveryPanelHeight; _discoveryPanel.OffsetBottom = -158;
        _discoveryPanel.GrowVertical = Control.GrowDirection.Begin;
        root.AddChild(_discoveryPanel);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        _discoveryPanel.AddChild(row);
        var lines = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        lines.AddThemeConstantOverride("separation", 0);
        row.AddChild(lines);
        _discoveryLabel = MakeLabel(14, new Color(0.82f, 0.9f, 1f), HorizontalAlignment.Left, outline: 0);
        _discoveryLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        lines.AddChild(_discoveryLabel);
        _discoveryElsewhere = MakeLabel(12, new Color(0.66f, 0.72f, 0.86f), HorizontalAlignment.Left, outline: 0);
        _discoveryElsewhere.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        lines.AddChild(_discoveryElsewhere);
        // No keyboard focus: a focused button would take the overview's keys (WASD, Enter) away from it.
        _discoveryButton = new Button { Text = "Pause", FocusMode = Control.FocusModeEnum.None, TooltipText = "Pause or resume listing more folders for the overview (X)", CustomMinimumSize = new Vector2(DiscoveryButtonWidth, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _discoveryButton.Pressed += ToggleDiscoveryPause;
        row.AddChild(_discoveryButton);
    }

    /// <summary>The indicator's size, viewport units: wide enough for <see cref="OverviewStatus"/>'s longest first line
    /// (a 24-character name) beside the button, and two lines high.</summary>
    private const float DiscoveryPanelWidth = 680f, DiscoveryPanelHeight = 50f;

    /// <summary>The Pause/Resume button's width, viewport units: the same for both words, so the lines never shift.</summary>
    private const float DiscoveryButtonWidth = 84f;

    private Label _discoveryElsewhere = null!;

    /// <summary>The indicator's second line (probes).</summary>
    public string DiscoveryElsewhereText => _discoveryElsewhere.Text;

    /// <summary>
    /// The indicator's lines fit (probes, PLANET-107): each line's text is no wider than its label, and neither label
    /// reaches the button. With the detail of the first failure, or null.
    /// </summary>
    public string? DiscoveryIndicatorOverflow()
    {
        var button = _discoveryButton.GetGlobalRect();
        foreach (var l in new[] { _discoveryLabel, _discoveryElsewhere })
        {
            var font = l.GetThemeFont("font");
            var size = l.GetThemeFontSize("font_size");
            var w = font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, size).X;
            var r = l.GetGlobalRect();
            if (w > r.Size.X + 0.5f) return System.FormattableString.Invariant($"\"{l.Text}\" is {w:0} wide in a {r.Size.X:0} label");
            if (r.Intersects(button)) return System.FormattableString.Invariant($"the label ({r.Position.X:0}..{r.End.X:0}) reaches the button ({button.Position.X:0}..{button.End.X:0})");
        }

        return null;
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
        _discoveryStatusAt = TimeSpan.MinValue;
        foreach (var (b, _) in _uniBodies) _uniEverDrawn.Add(b.NodeId);
        DiscoveryRelayouts = 0;
        _discoveryPanel.Visible = DiscoveryAvailable;
        UpdateDiscoveryIndicator();
    }

    /// <summary>The overview closed: nothing more is asked for (listings in flight still arrive in the tree). The
    /// selected world becomes a recent focus: while you play, attention is where you stand.</summary>
    private void CloseDiscovery()
    {
        _discoveryPanel.Visible = false;
        NoteSelectionForAttention(-1);
    }

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
            // The laid-out worlds within attention's reach (PLANET-104): never recursive discovery far from where you look.
            var worlds = new List<(int Id, int Depth)>(_uniBodies.Count);
            foreach (var (b, _) in _uniBodies) worlds.Add((b.NodeId, b.Depth));
            var drawn = Attention.Candidates(tree, worlds, tree.Root.Id);
            var focus = new List<int>(2) { _uniSelected };
            if (ZoomedToward() is >= 0 and var toward && toward != _uniSelected) focus.Add(toward);
            foreach (var id in _discovery.Step(tree, drawn, focus, now, _s.MayListAhead)) explorer.Discover(id);
        }

        if (_uniLanding == null && (tree.Version != _uniLayoutVersion && (now - _uniLayoutAt).TotalSeconds >= UniverseRelayoutSeconds || AttentionWantsRelayout()))
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

    // ---- background discovery during ordinary play (PLANET-90, AD-77)
    // DJ (fifth playtest follow-up): "Allow background folder discovery during ordinary play as well as Overview. Reuse
    // the listing scheduler, prioritize what I'm visiting, and keep the game responsive." The same scheduler, bounds and
    // explorer request as the overview's, at its quieter play pace (OverviewDiscovery.Limits.PlayListingsPerSecond, two
    // in flight), and the same candidates: the worlds the overview would draw (UniverseLayout.Selection, without laying
    // them out). The current world's subtree goes first, then its parent's (its neighbours), then its grandparent's, then
    // the rest breadth-first. Only while walking (never during a flight or a landing, whose frames matter most), never
    // while paused (X in the overview pauses both), and never without a live explorer.

    /// <summary>False with --test-play-discovery=off (the before/after instrument): nothing is listed while you play.</summary>
    public bool PlayDiscoveryEnabled { get; init; } = true;

    /// <summary>How often play discovery asks the scheduler, seconds: its pace is 2 a second, so four steps a second
    /// keep it on pace at a quarter of the overview's checks.</summary>
    private static readonly TimeSpan PlayDiscoveryStepInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>
    /// The least time between two rebuilds of play discovery's candidates, seconds. A rebuild is the overview's selection
    /// pass over the tree (sorting each system's names), ≈ 1–3 ms at 1,500 worlds on the main thread, so it waits for
    /// the tree to have changed and for this long since the last one.
    /// </summary>
    private static readonly TimeSpan PlayCandidateInterval = TimeSpan.FromSeconds(2);

    private List<OverviewDiscovery.Candidate> _playCandidates = [];
    private int _playCandidatesVersion = -1;
    private TimeSpan _playCandidatesAt;
    private int _playCandidatesFolder = -1;

    /// <summary>The longest candidate rebuild while playing, milliseconds, and how many (probes).</summary>
    public double PlayDiscoveryRebuildMsMax { get; private set; }
    public int PlayDiscoveryRebuilds { get; private set; }

    /// <summary>Listings play discovery has asked for this session (probes).</summary>
    public int PlayDiscoveryRequested { get; private set; }

    /// <summary>Once a frame while the overview is closed: background discovery at the play pace (see above).</summary>
    private void StepPlayDiscovery(FsTree tree)
    {
        if (!PlayDiscoveryEnabled || _s.Explorer is not { } explorer || _current == null || Busy || _hop != null || _phase != Phase.Walking) return;
        var now = _s.Now;
        if (now - _lastDiscoveryStep < PlayDiscoveryStepInterval) return;
        _lastDiscoveryStep = now;
        var folder = _current.FolderId;
        if (_playCandidatesFolder != folder || tree.Version != _playCandidatesVersion && now - _playCandidatesAt >= PlayCandidateInterval)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // The overview's own selection (ComputeUniverse with nothing drawn yet): the way to where you stand pinned.
            var onTheWay = new HashSet<int>();
            for (var a = folder; a >= 0 && tree.Contains(a); a = tree[a].ParentId) onTheWay.Add(a);
            // Attention while you play is where you stand (and what you attended to before): PLANET-104's reach applies too.
            NoteCurrentForAttention();
            var selection = Attention.Selection(tree, n => _s.IsVisible(n) || onTheWay.Contains(n.Id), tree.Root.Id, onTheWay);
            _playCandidates = Attention.Candidates(tree, selection, tree.Root.Id);
            _playCandidatesVersion = tree.Version;
            _playCandidatesAt = now;
            _playCandidatesFolder = folder;
            PlayDiscoveryRebuilds++;
            PlayDiscoveryRebuildMsMax = Math.Max(PlayDiscoveryRebuildMsMax, sw.Elapsed.TotalMilliseconds);
        }

        // What you are visiting first: this world's subtree, then its parent's, then its grandparent's (the focus order).
        var focus = new List<int>(3);
        for (var a = folder; a >= 0 && focus.Count < 3 && tree.Contains(a); a = tree[a].ParentId) focus.Add(a);
        foreach (var id in _discovery.Step(tree, _playCandidates, focus, now, _s.MayListAhead, OverviewDiscovery.Pace.Play, focusInOrder: true))
        {
            explorer.Discover(id);
            PlayDiscoveryRequested++;
        }
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

    /// <summary>How often the indicator's neighbourhood and deferred counts are worked out, seconds: each is a pass over
    /// the laid-out worlds (with a walk up the tree for each), so not every frame.</summary>
    private const double DiscoveryStatusSeconds = 0.25;

    private TimeSpan _discoveryStatusAt = TimeSpan.MinValue;
    private (NeighbourhoodStatus Near, int Deferred) _discoveryStatus;

    private void UpdateDiscoveryIndicator()
    {
        if (!_discoveryPanel.Visible) return;
        var now = _s.Now;
        if (_discoveryStatusAt == TimeSpan.MinValue || (now - _discoveryStatusAt).TotalSeconds >= DiscoveryStatusSeconds || now < _discoveryStatusAt)
        {
            _discoveryStatus = AttentionStatus(); // PlanetWorld.Attention.cs
            _discoveryStatusAt = now;
        }

        var spin = DiscoverySpinner[(int)(now.TotalSeconds / DiscoverySpinnerSeconds) % DiscoverySpinner.Length];
        var focus = NeighbourhoodFocus();
        var name = _s.Tree is { } t && t.Contains(focus) ? t.DisplayName(focus) : "here";
        var (near, elsewhere) = OverviewStatus.Text(_discovery.State, _discoveryStatus.Near, name, _discoveryStatus.Deferred, _uniBodies.Count, _discovery.Bounds.MaxTreeNodes, spin);
        _discoveryLabel.Text = near;
        _discoveryElsewhere.Text = elsewhere;
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

    private int _censusVersion = -1;
    private DiscoveryCensus _census, _censusHere;

    /// <summary>
    /// The tree's folders by state, everywhere and within where you stand (its own folders, one level down: the local
    /// neighbourhood), recounted when the tree changes (PLANET-195; probes).
    /// </summary>
    public (DiscoveryCensus All, DiscoveryCensus Here) Census
    {
        get
        {
            if (_s.Tree is not { } tree) return default;
            if (tree.Version != _censusVersion || _current?.FolderId is not { } here || _censusHereFolder != here)
            {
                var folder = _current?.FolderId ?? -1;
                _census = OverviewDiscovery.Census(tree);
                _censusHere = OverviewDiscovery.Census(tree, n => n.ParentId == folder);
                _censusVersion = tree.Version;
                _censusHereFolder = folder;
            }

            return (_census, _censusHere);
        }
    }

    private int _censusHereFolder = -1;

    /// <summary>
    /// The census part of the probe state file, written whether or not the overview is open (PLANET-195: the desktop
    /// owner found no discovery counts before it opened): the tree's folders by state, the local neighbourhood's, play
    /// discovery's requests and the scheduler's counts, and the overview's drawn worlds and zoom when it is open. Counts
    /// only, never names.
    /// </summary>
    private string CensusStateJson()
    {
        var (all, here) = Census;
        var open = _universe != null;
        return System.FormattableString.Invariant(
            $"\"census\":{{\"tree\":{all.Json},\"here\":{here.Json},\"playRequested\":{PlayDiscoveryRequested},\"requested\":{_discovery.Requested},\"listed\":{_discovery.Listed},\"failed\":{_discovery.Failed},\"timedOut\":{_discovery.TimedOut},\"refused\":{_discovery.Refused},\"inFlight\":{_discovery.InFlight},\"state\":\"{_discovery.State}\",\"overviewOpen\":{(open ? "true" : "false")},\"drawn\":{(open ? _uniBodies.Count : 0)},\"zoom\":{(open ? _uniDistanceTarget : 0):0.#},\"maxZoom\":{(open ? UniverseMaxZoomMetres() : 0):0.#},\"extent\":{(open ? _uniExtent : 0):0.#}}},");
    }

    /// <summary>The discovery part of the probe state file (PLANET-76; counts only, never names).</summary>
    private string DiscoveryStateJson()
    {
        if (_universe == null) return "";
        var levels = _s.Tree is { } t && t.Contains(_s.Navigator.Root) ? UniverseLevels(_s.Navigator.Root) : [];
        var deepest = levels.Count > 0 ? levels.Keys.Max() : -1;
        var deepestListed = levels.Where(l => l.Value.Listed > 0).Select(l => l.Key).DefaultIfEmpty(-1).Max();
        return System.FormattableString.Invariant(
            $"\"discovery\":{{\"state\":\"{_discovery.State}\",\"paused\":{(_discovery.Paused ? "true" : "false")},\"worlds\":{_uniBodies.Count},\"requested\":{_discovery.Requested},\"listed\":{_discovery.Listed},\"failed\":{_discovery.Failed},\"inFlight\":{_discovery.InFlight},\"frontier\":{_discovery.Frontier},\"levels\":{deepest + 1},\"deepestListed\":{deepestListed},\"relayouts\":{DiscoveryRelayouts},\"relayoutMsMax\":{DiscoveryRelayoutMsMax:0.0},\"button\":[{DiscoveryButtonRect.GetCenter().X:0},{DiscoveryButtonRect.GetCenter().Y:0}],\"indicator\":{(DiscoveryIndicatorShown ? "true" : "false")},\"selected\":{_uniSelected},\"clickable\":{(UniverseClickable() is { } uc ? $"[{uc.Id},{uc.Centre.X:0},{uc.Centre.Y:0}]" : "null")}}},") + AttentionStateJson();
    }
}
