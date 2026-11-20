using System.Diagnostics;
using FileScape.App.Places;
using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Scanning;
using FileScape.Core.Shell;
using FileScape.Core.Units;
using Godot;
using GVector3 = Godot.Vector3;

namespace FileScape.App;

/// <summary>
/// The application controller: the one node that owns the shared session — the current tree and its scan session,
/// the navigator (current folder and history), the selection, the shell actions, the HUD and the probes' state file —
/// and routes input and HUD intents to whichever experience is active (AD-15). The data flow is one-way: scan → tree →
/// layout or composition → scene; the selection refers only to node ids, so it survives re-layout, recomposition and a
/// change of experience (docs/architecture.md).
/// <para>
/// FileScape needs one owner for this because every experience (Places, the Classic and District maps, Planets) shows
/// the same tree and the same "where you are": switching (Tab, M, V) must keep the folder, the selection and the
/// history. Scan batches are applied only here, on the main thread, within a per-frame budget (AD-04).
/// </para>
/// <para>
/// The partial files, by concern:
/// <list type="bullet">
/// <item><c>Main.cs</c>: start-up and command line, the scene, scanning, the frame loop, the maps' camera and picking,
/// input routing (<see cref="_Input"/> sees releases first; <see cref="_UnhandledInput"/> takes presses no control
/// consumed, AD-12), deliberate shell actions, HUD content, shutdown (SHUT-01).</item>
/// <item><c>Main.Places.cs</c>: the Places experience — switching experiences, composing a place, travel through
/// gates and the threshold transition between places, signs and the gate chip.</item>
/// <item><c>Main.Walk.cs</c>: locomotion in Places — walking via the Core <see cref="Walker"/>, flight, landing,
/// guided glides and mouse look.</item>
/// <item><c>Main.Aerial.cs</c>: the aerial world seen when flying high, and travel between places from the air.</item>
/// <item><c>Main.World.cs</c>: the connected world — the live <see cref="WorldExplorer"/>, following links, Go to path
/// and Locations, the hidden/system filter.</item>
/// <item><c>Main.Projects.cs</c>: prominence (pins, known folders, projects), README and editor actions.</item>
/// <item><c>Main.Planets.cs</c>: the planetary world on the shared session (<see cref="IWorldSession"/>).</item>
/// <item><c>Main.Settings.cs</c>: the user's preferences.</item>
/// </list>
/// </para>
/// <para>
/// Assumptions to keep: only this class mutates the tree (single writer, AD-04); nothing here may block on the
/// filesystem (listings and README reads run off the main thread); and a held key or captured mouse must always be
/// released on focus loss, dialogs and lost releases (<see cref="ReleaseAllInput"/>).
/// </para>
/// </summary>
public partial class Main : Node3D
{
    /// <summary>Scan batches are applied for at most this long per frame, so a fast scan never stalls rendering (AD-04).</summary>
    private static readonly TimeSpan BatchBudget = TimeSpan.FromMilliseconds(6);

    // Refresh intervals of the per-frame bookkeeping (app clock). Labels track the camera closely; the status line,
    // state file and stats overlay need not change every frame.
    private static readonly TimeSpan LabelRefreshInterval = TimeSpan.FromSeconds(0.08), StatusRefreshInterval = TimeSpan.FromSeconds(0.1),
        StateWriteInterval = TimeSpan.FromSeconds(0.15), StatsRefreshInterval = TimeSpan.FromSeconds(0.25), ProcessMemoryInterval = TimeSpan.FromSeconds(1);

    private readonly Visualization[] _visualizations = [new ClassicVisualization(), new DistrictVisualization()];
    private readonly SelectionState _selection = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private CameraRig _rig = null!;
    private WorldEnvironment _world = null!;
    private DirectionalLight3D _sun = null!;
    private MeshInstance3D _ground = null!;
    private Hud _hud = null!;
    private LabelLayer _labels = null!;

    /// <summary>Screen-space destination names (LABEL-01): gates while walking, places from the air.</summary>
    private SignLayer _signs = null!;

    public SignLayer Signs => _signs;
    private MeshInstance3D _selectionBox = null!, _hoverBox = null!, _beam = null!;
    private SpotLight3D _spot = null!;
    private ConfirmationDialog _confirm = null!;
    private FileDialog _folderDialog = null!;
    private Action? _pendingConfirm;

    private FsTree? _tree;
    private ScanSession? _session;
    private LayoutResult? _layout;
    private IShellActions _shell = new MockShellActions();
    private int _visIndex;
    private Quality _quality = Quality.High;
    private int _layoutTreeVersion = -1;
    private TimeSpan _lastLayoutAt = TimeSpan.FromSeconds(-10);

    /// <summary>
    /// Map re-layout during a scan is throttled to this interval, adapted to about 8× the last layout+build cost
    /// (0.25–3 s), so re-layout takes at most ≈ 1/8 of the frame time (AD-04).
    /// </summary>
    private TimeSpan _layoutInterval = TimeSpan.FromSeconds(MinRelayoutSeconds);

    /// <summary>
    /// The adaptive re-layout cadence (AD-04): wait this many times the last layout+build cost between re-layouts while
    /// scanning, so re-layout takes about 1/8 of the time. Higher: steadier frames, a map that fills in more jerkily.
    /// </summary>
    private const int RelayoutCostMultiple = 8;

    /// <summary>The re-layout cadence's limits, seconds: no faster than 4 a second, never stale for longer than 3 s.</summary>
    private const double MinRelayoutSeconds = 0.25, MaxRelayoutSeconds = 3.0;

    /// <summary>A frame gap longer than this, milliseconds, is recorded as a hitch for outside probes (TEST-03).</summary>
    private const int HitchThresholdMs = 50;

    /// <summary>The planets' breadcrumb and counts follow the listing at most this often.</summary>
    private static readonly TimeSpan PlanetHudRefreshInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>A listing that has taken longer than this, seconds, is named in the status line ("waiting on Windows").</summary>
    private const double SlowListingNoticeSeconds = 1.0;

    private bool _needsInitialFrame;
    private bool _scanFinishedReported;

    private Vector2 _pressPosition;
    private MouseButton _pressButton = MouseButton.None;
    private bool _dragged;
    private bool _hoverDirty;
    private TimeSpan _lastLabelRefresh, _lastStatusRefresh, _lastStatsRefresh, _lastProcessMemoryAt;
    private long _processPrivateBytes;

    public PerfRecorder Perf { get; } = new();
    public double LastLayoutMs { get; private set; }
    public double LastBuildMs { get; private set; }
    public int InstanceCount { get; private set; }
    public FsTree? Tree => _tree;
    public LayoutResult? Layout => _layout;
    public ScanSession? Session => _session;
    public SelectionState Selection => _selection;
    public CameraRig Rig => _rig;
    public Hud Hud => _hud;
    public Visualization ActiveVisualization => _visualizations[_visIndex];
    public int VisualizationIndex => _visIndex;
    public Quality Quality => _quality;
    public string? LastShellMessage { get; private set; }

    /// <summary>The last shell action's result (the reader's status line says whether it worked).</summary>
    public ShellResult? LastShellResult { get; private set; }
    public int LabelsShown => _labels.LastShown;
    public IReadOnlyList<Rect2> LabelRects => _labels.ShownRects;

    private bool _labelsVisible = true;

    /// <summary>Hides the 2D name labels (instrument use: measuring the 3D scene only).</summary>
    public void SetLabelsVisible(bool visible)
    {
        _labelsVisible = visible;
        if (!visible) _labels.HideAll();
    }

    // Scene parts exposed for investigations (the flicker probe toggles them one at a time).
    internal WorldEnvironment World => _world;
    internal DirectionalLight3D Sun => _sun;
    internal SpotLight3D SelectionSpot => _spot;
    internal MeshInstance3D SelectionBeam => _beam;
    internal MeshInstance3D Ground => _ground;

    /// <summary>True when an isolated experiment runs instead of FileScape (nothing else is built).</summary>
    private bool _experimentOnly;

    /// <summary>
    /// Start-up: reads the command line, builds the scene, loads preferences, picks the experience and the starting
    /// data (hub, Starter Sky, a folder, or the synthetic sample) and attaches the probe or automation the command line
    /// names. An isolated experiment (<c>--experiment</c>) builds only itself and returns early.
    /// </summary>
    public override void _Ready()
    {
        ConsoleLaunch.Detect();
        InputBindings.Register();
        var args = CommandLine.Parse(OS.GetCmdlineUserArgs());
        // 96 dpi is Windows' 100 % scale; the UI follows the screen's scaling between 100 % and 300 % unless --ui-scale says otherwise.
        var dpiScale = Mathf.Clamp(DisplayServer.ScreenGetDpi() / 96f, 1f, 3f);
        GetWindow().ContentScaleFactor = args.UiScale ?? dpiScale;
        GetWindow().Title = "FileScape";
        if (args.WindowSize is { } ws)
        {
            DisplayServer.WindowSetSize(ws);
        }

        _stateFile = args.StateFile;
        if (args.Experiment == "planetoids")
        {
            // An isolated experiment (PLANET-02): its own scene; nothing of FileScape is built or scanned.
            _experimentOnly = true;
            AddChild(new Experiments.PlanetoidExperiment(args.Automation == "planetoids", args.OutputDirectory ?? OS.GetUserDataDir()));
            return;
        }

        if (args.Experiment == "planet-gallery") { _experimentOnly = true; AddChild(new Planets.PlanetGallery(args.Automation == "planet-gallery", args.OutputDirectory ?? OS.GetUserDataDir())); return; }

        FileScape.Core.Planets.PlanetComposer.ClassicReliefForComparison = args.TestClassicRelief;
        _testListDelayMs = args.TestListDelayMs ?? 0;
        _testListDelayMatch = args.TestListDelayMatch;
        _testBroad = args.TestBroad;
        _recordShell = args.TestShellRecord;
        if (args.SpaciousLayout) _placeParams = PlaceParams.Spacious;
        if (args.TestMaxIslands is { } mi) _worldLayoutParams = WorldLayoutParams.Default with { MaxIslands = mi };
        BuildScene();
        LoadPreferences(args);
        BuildTutorialUi();
        _quality = args.Quality ?? Quality.High;
        _visIndex = args.Visualization is 1 ? 1 : 0;
        // The reference-view evidence scripts predate Places and expect a map unless --vis says otherwise (AD-15).
        var legacyScript = args.Automation is "journey" or "perf" or "scanperf" or "live" or "qualitystress" or "flicker" or "navigation";
        // A person's launch (no automation) opens Planets at This computer (AD-51, PLANET-32) unless they chose a mode or a
        // folder; explicit --vis and --root always win. Automated journeys keep their historical defaults (Places on a
        // synthetic tree, maps for the evidence scripts) so their expectations stay valid.
        var personLaunch = args.Automation == null;
        _automationIgnoresFocus = !personLaunch;
        _experience = args.Experience ?? (legacyScript ? Experience.Classic : personLaunch ? Experience.Planets : Experience.Places);
        if (_experience != Experience.Places) _visIndex = _experience == Experience.Classic ? 0 : 1;
        ApplyExperience(frame: false);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);

        if ((LocationRequest.Parse(args.Root) is { IsHub: true } || (args.Root == null && personLaunch && args.SyntheticNodes == null)) && !args.BoundedTree)
        {
            StartLive(null); // --root=hub or --root="This PC": start at the hub (LOC-06)
        }
        else if (args.Root is "sample" or "starter-sky")
        {
            if (TryStarterSky() is { } sky) StartLive(sky); // the curated sample worlds (real files the app writes for itself)
            else StartScan(new SyntheticTreeSource());
        }
        else if (args.Root is { } root)
        {
            if (args.BoundedTree) StartBoundedTree(root, confirmBroad: false);
            else StartLive(root);
        }
        else
        {
            StartScan(new SyntheticTreeSource { TargetNodes = args.SyntheticNodes ?? 12_000, NodesPerSecond = args.SyntheticRate ?? 0 });
        }

        MaybeOfferWelcome(args);
        GD.Print($"[build] {BuildInfo.Identifier}");
        _hud.SetBuildIdentifier(BuildInfo.Identifier);
        if (args.Automation == "smoke")
        {
            AddChild(new SmokeProbe(this, args.OutputDirectory ?? OS.GetUserDataDir()));
        }
        else if (args.Automation is { } walkMode && walkMode.StartsWith("walk", StringComparison.Ordinal))
        {
            AddChild(new WalkProbe(this, args.OutputDirectory ?? OS.GetUserDataDir(), walkMode));
        }
        else if (args.Automation is "places" or "places-scan" or "places-tour" or "places-debug")
        {
            AddChild(new PlacesProbe(this, args.OutputDirectory ?? OS.GetUserDataDir(), args.Automation));
        }
        else if (args.Automation is { } planetsMode && planetsMode.StartsWith("planets-", StringComparison.Ordinal))
        {
            AddChild(new PlanetsProbe(this, args.OutputDirectory ?? OS.GetUserDataDir(), planetsMode));
        }
        else if (args.Automation == "navigation")
        {
            AddChild(new NavigationProbe(this, args.OutputDirectory ?? OS.GetUserDataDir()));
        }
        else if (args.Automation is { } script)
        {
            var automation = new Automation(this, script, args.OutputDirectory ?? OS.GetUserDataDir(), args);
            AddChild(automation);
        }

        // SHUT-01 instrument: 4 s lets the start-up finish and the first place appear before the held state begins.
        if (args.TestHold is { } hold) GetTree().CreateTimer(4.0).Timeout += () => EnterTestHold(hold);
    }

    private int _testListDelayMs;
    private string? _testListDelayMatch;
    private bool _testBroad; // --test-broad (PLANET-76 instrument): no background listing of the arrival folder
    private bool _recordShell;

    /// <summary>Opens the Starter Sky in Planets (the toolbar's Sample world, the walkthrough), or the synthetic tree if it cannot be written.</summary>
    public void OpenStarterSky()
    {
        if (_experience != Experience.Planets && (_tree == null || !_tree.Source.IsLive))
        {
            SetExperience(Experience.Planets); // from a synthetic tree, switching to Planets opens the Starter Sky itself
            return;
        }

        if (TryStarterSky() is { } sky)
        {
            if (_experience != Experience.Planets) SetExperience(Experience.Planets);
            StartLive(sky);
        }
        else
        {
            StartScan(new SyntheticTreeSource());
        }
    }

    /// <summary>
    /// Key presses that reached the game's shortcuts (<see cref="_UnhandledInput"/> on the planets), newest last, at most
    /// <see cref="UnhandledKeyLogLength"/> (probes, PLANET-51: while you type in a text field none may arrive here).
    /// </summary>
    public readonly List<string> UnhandledKeys = [];

    /// <summary>The unhandled-key log keeps this many entries (enough for one probe step's typing).</summary>
    private const int UnhandledKeyLogLength = 64;

    private void NoteUnhandledKey(InputEventKey k)
    {
        if (k.Keycode is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta) return; // held modifiers, never a shortcut on their own
        if (UnhandledKeys.Count >= UnhandledKeyLogLength) UnhandledKeys.RemoveAt(0);
        UnhandledKeys.Add(OS.GetKeycodeString(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode) + (k.Echo ? "(echo)" : ""));
        UnhandledKeyCount++;
    }

    /// <summary>Every key press that reached the game's shortcuts so far (the log above keeps only the newest).</summary>
    public int UnhandledKeyCount { get; private set; }

    /// <summary>What the recording test shell was asked to do (probes; empty with the real shell).</summary>
    public IReadOnlyList<string> ShellLog => (_shell as FileScape.Core.Shell.RecordingShellActions)?.Log ?? [];

    /// <summary>Test instrument (PLANET-43): under <c>--test-shell=record</c>, the next Open fails with this Win32 error code.</summary>
    public bool ProbeFailNextOpen(int win32Error)
    {
        if (_shell is not FileScape.Core.Shell.RecordingShellActions rec) return false;
        rec.FailNextOpenWith = win32Error;
        return true;
    }
    private bool _closing;

    /// <summary>
    /// SHUT-01 (AD-28): orderly shutdown. Stops accepting input and per-frame work, cancels scanning and discovery
    /// (the explorer's coordinator stops before its next unit of work), and never waits for outstanding filesystem
    /// calls: listings in flight run on background threads and are abandoned, so a stalled share cannot hold the exit.
    /// </summary>
    private void BeginShutdown(string why)
    {
        if (_closing) return;
        _closing = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var progress = _session?.Snapshot();
        _session?.Dispose();
        if (progress is { } p)
        {
            GD.Print($"[exit] scanning cancelled ({p.State}, {p.PendingDirectories} folders pending{(p.CurrentPath != null ? $", abandoned listing of {p.CurrentPath}" : "")})");
        }

        ConsoleLaunch.ReportExit(why == "window closed" ? 0 : -1, why, sw.Elapsed);
    }

    /// <summary>SHUT-01 instrument: enters a state and stays there, for an outside close-and-time probe.</summary>
    private void EnterTestHold(string hold)
    {
        GD.Print($"[test-hold] {hold}");
        switch (hold)
        {
            case "dialog": ShowFolderDialog(); break;
            case "fly": TakeOff(); break;
            case "reveal" when _tree != null && _nav.Current >= 0:
                GD.Print($"[test-hold] {_shell.RevealInExplorer(_tree.FullPath(_nav.Current)).Message}");
                break;
        }
    }

    /// <summary>
    /// Builds the nodes every experience shares: environment, sun, ground, the two maps' roots, the camera rig, the
    /// maps' selection and hover highlights, the label and sign layers and the HUD (wired to its intents), then each
    /// experience's own scene. Dialogs are added deferred, once the HUD's UI root is in the tree.
    /// </summary>
    private void BuildScene()
    {
        _world = new WorldEnvironment();
        AddChild(_world);
        _sun = new DirectionalLight3D { ShadowEnabled = true };
        _sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        AddChild(_sun);
        // A 40 km ground plane, far wider than any layout, so its edge is never in view; set 1 mm below y = 0 so it never
        // z-fights with bases and tiles that sit exactly on the ground.
        _ground = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(40_000, 40_000) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new GVector3(0, -0.001f, 0),
        };
        AddChild(_ground);

        foreach (var v in _visualizations)
        {
            AddChild(v.Root);
            v.Root.Visible = false;
        }

        _rig = new CameraRig();
        AddChild(_rig);

        var highlight = GD.Load<Shader>("res://Shaders/highlight.gdshader");
        _selectionBox = new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = new ShaderMaterial { Shader = highlight }, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _hoverBox = new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = new ShaderMaterial { Shader = highlight }, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        // Hover is the quieter of the two highlights: no pulse, and faces only faintly tinted (3 %), so the edges carry it.
        ((ShaderMaterial)_hoverBox.MaterialOverride).SetShaderParameter("pulse", 0f);
        ((ShaderMaterial)_hoverBox.MaterialOverride).SetShaderParameter("face_alpha", 0.03f);
        // The maps' selection marker from afar: a thin (0.12 m radius) unit-high light column scaled to height in
        // UpdateHighlights, and an unshadowed spot from above (34 m reach, 20° cone) that lights the selected box.
        _beam = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 1, RadialSegments = 12, Rings = 1 },
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/beam.gdshader") },
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _spot = new SpotLight3D { SpotRange = 34, SpotAngle = 20, LightEnergy = 9, LightSpecular = 0.15f, ShadowEnabled = false, Visible = false, RotationDegrees = new GVector3(-90, 0, 0) };
        AddChild(_selectionBox);
        AddChild(_hoverBox);
        AddChild(_beam);
        AddChild(_spot);

        var labelCanvas = new CanvasLayer { Layer = 0 };
        _labels = new LabelLayer();
        labelCanvas.AddChild(_labels);
        _signs = new SignLayer();
        labelCanvas.AddChild(_signs);
        AddChild(labelCanvas);
        _hud = new Hud { Layer = 1 };
        AddChild(_hud);

        _hud.OpenFolderRequested += ShowFolderDialog;
        _hud.OpenLocationRequested += ShowLocations;
        _hud.PinToggled += TogglePin;
        _hud.ReadmeRequested += ShowReadme;
        ConnectReader(); // Main.Reader: the reading view's links and Open with Windows
        _hud.EditorRequested += RequestEditor;
        // "Sample world" opens the bundled Starter Sky (real sample files FileScape writes for itself) in Planets; the
        // synthetic tree remains for the legacy maps and automation (--synthetic=N).
        _hud.DemoRequested += OpenStarterSky;
        _hud.CancelRequested += CancelScan;
        _hud.ExperienceRequested += i => SetExperience((Experience)i);
        _hud.ResetRequested += () => ResetView();
        _hud.OverviewRequested += () => Overview();
        _hud.QualityToggled += ToggleQuality;
        _hud.OpenRequested += RequestOpen;
        _hud.RevealRequested += RequestReveal;
        _hud.ParentRequested += CardParent; // Main.SelectionActions (PLANET-87)
        _hud.FlyToRequested += CardFly;
        _hud.CopyPathRequested += CopyPath;
        _hud.HelpToggled += () => _hud.ToggleHelp();
        _hud.InspectorClosed += () => _selection.Select(-1);
        _hud.FlightToggled += ToggleFlight;
        _selection.SelectionChanged += _ => UpdateInspector();

        BuildPlaceScene();
        BuildAerialScene();
        BuildPlanets();
        CallDeferred(MethodName.BuildDialogs);
    }

    private void BuildDialogs()
    {
        _confirm = new ConfirmationDialog { Title = "Please confirm", OkButtonText = "Continue", Exclusive = true };
        _confirm.Confirmed += () => { var a = _pendingConfirm; _pendingConfirm = null; a?.Invoke(); };
        _confirm.Canceled += () => _pendingConfirm = null;
        _hud.UiRoot.AddChild(_confirm);
        _folderDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Title = "Choose a folder to explore (metadata only)",
        };
        _folderDialog.DirSelected += OnFolderChosen;
        _hud.UiRoot.AddChild(_folderDialog);
    }

    // ---------------------------------------------------------------- scanning

    /// <summary>Starts a new scan, replacing the current tree. Any running scan is cancelled first.</summary>
    public void StartScan(IScanSource source)
    {
        _session?.Dispose();
        _session = new ScanSession(source);
        _tree = new FsTree(_session.Source);
        _shell = _tree.Source.IsLive ? (_recordShell ? new RecordingShellActions() : new WindowsShellActions()) : new MockShellActions();
        _layout = null;
        _layoutTreeVersion = -1;
        _lastLayoutAt = TimeSpan.FromSeconds(-10);
        _needsInitialFrame = true;
        _scanFinishedReported = false;
        _selection.Reset();
        _nav.Reset(0);
        _place = null;
        _transition = null;
        _hud.SetTransition(0, null);
        _placeWorld.Clear();
        foreach (var v in _visualizations)
        {
            v.Clear();
        }

        _hud.SetSource(_tree.Source.IsLive ? $"LIVE · {MiddleTruncate(_tree.Source.RootDisplayPath, 48)}" : "SYNTHETIC SAMPLE DATA — not your files", _tree.Source.IsLive,
            _tree.Source.IsLive ? $"Live Windows filesystem, metadata only: {_tree.Source.RootDisplayPath}" : _tree.Source.Description);
        _session.Start();
        OnSourceStarted(source);
        UpdateInspector();
    }

    private static string MiddleTruncate(string s, int max) =>
        s.Length <= max ? s : string.Concat(s.AsSpan(0, max / 2 - 1), "…", s.AsSpan(s.Length - (max - max / 2)));

    public void CancelScan()
    {
        if (_explorer != null)
        {
            _explorer.CancelBackground();
            _hud.Toast("Background listing of the arrival folder stopped. Places are still listed when you visit them.");
            return;
        }

        if (_session is { IsFinished: false })
        {
            _session.Cancel();
            _hud.Toast("Cancel requested. Stopping after the current directory; everything scanned so far stays explorable.");
        }
    }

    private void ShowFolderDialog()
    {
        ReleaseAllInput();
        _folderDialog?.Popup();
    }

    private void OnFolderChosen(string path)
    {
        if (LocationRequest.Parse(path) is { IsHub: true })
        {
            GoToHub();
            return;
        }

        var full = System.IO.Path.GetFullPath(path);
        if (_explorer != null)
        {
            // Already exploring the live filesystem: go there in the same world and make it the arrival root.
            var ticket = _explorer.GoTo(full);
            _pendingGoTo[ticket] = full;
            _pendingNewRoot = ticket;
            _hud.SetTransitionNote($"Finding {full}…");
            return;
        }

        // Broad places (drive roots, profiles, cloud folders) need no confirmation any more: they are listed on demand
        // and never traversed in the background (LOC-02, Locations.IsBroad), which supersedes FS-12's confirmation.
        StartLive(full);
    }

    // ---------------------------------------------------------------- frame loop

    /// <summary>
    /// The frame loop: applies scan batches within <see cref="BatchBudget"/>, then runs the active experience — Planets
    /// hands the frame to <see cref="PlanetWorld"/>; Places recomposes, advances any transition, moves the user and checks
    /// gate crossings; the maps re-layout (throttled while scanning). The shared bookkeeping runs last (<see cref="FinishFrame"/>).
    /// </summary>
    public override void _Process(double delta)
    {
        if (_closing) return; // SHUT-01: no new work once closing has begun
        if (_experimentOnly) return;
        RefreshCardOnViewChange();
        Perf.BeginFrame(delta, _session is { IsFinished: false } && !(_explorer != null && _session.Snapshot().Idle));
        var now = _clock.Elapsed; // the app clock (time since start)
        if (_now > TimeSpan.Zero && (now - _now).TotalMilliseconds > HitchThresholdMs)
        {
            // TEST-03 diagnostics: the last long frame (the gap since the previous frame began), for outside probes.
            _lastHitchMs = (now - _now).TotalMilliseconds;
            _lastHitchAtMs = now.TotalMilliseconds;
        }

        _now = _clock.Elapsed;
        DrainBatches();
        if (_experience == Experience.Planets)
        {
            // The planetary world (AD-39) owns its camera, movement and signs; the session, HUD and state file stay shared.
            var pointerBlocked = !_windowFocused || ModalOpen;
            var blockedKeys = pointerBlocked || GetViewport().GuiGetFocusOwner() != null || KeyboardPanel != KeyPanel.None; // AD-68
            _planets.Tick(delta, blockedKeys, pointerBlocked);
            TickTutorial((float)delta);
            // The breadcrumb and counts follow the listing (found 2026-11-11: opening the planets on a fresh root left
            // "You are in" empty, because the HUD was set before the root was listed). At most twice a second.
            if (_tree is { Count: > 0 } && _tree.Version != _planetHudTreeVersion && _now - _planetHudAt >= PlanetHudRefreshInterval)
            {
                _planetHudTreeVersion = _tree.Version;
                _planetHudAt = _now;
                UpdatePlaceHud();
            }

            FinishFrame(now);
            return;
        }

        var places = _experience == Experience.Places;
        if (places)
        {
            MaybeRecompose(_now);
            UpdateTransition(delta);
        }
        else
        {
            MaybeRelayout(now);
        }

        HandleKeyboardMovement(Durations.FromSeconds(delta));
        if (places) CheckThresholds();

        if (places && _place != null && _tree != null)
        {
            if (_hoverDirty)
            {
                _hoverDirty = false;
                UpdateHover();
            }

            UpdateAerial();
            UpdatePlaceFrame();
            if (_labelsVisible && now - _lastLabelRefresh > LabelRefreshInterval)
            {
                _lastLabelRefresh = now;
                if (_transition == null) _labels.RefreshPlace(_rig.Camera, _tree, _place, _selection, _hud.BlockingRects, worldSigns: _placeWorld.VisibleSignRects(_rig.Camera).Concat(_signs.ShownRects));
                else _labels.HideAll();
            }
        }
        else if (!places && _layout != null && _tree != null)
        {
            if (_hoverDirty)
            {
                _hoverDirty = false;
                UpdateHover();
            }

            UpdateHighlights();
            UpdateHereMarker();
            if (_labelsVisible && now - _lastLabelRefresh > LabelRefreshInterval)
            {
                _lastLabelRefresh = now;
                _labels.Refresh(_rig.Camera, _tree, _layout, _selection, _hud.BlockingRects);
            }
        }

        FinishFrame(now);
    }

    /// <summary>The per-frame bookkeeping every experience shares: status line, state file, stats.</summary>
    private void FinishFrame(TimeSpan now)
    {
        if (now - _lastStatusRefresh > StatusRefreshInterval)
        {
            _lastStatusRefresh = now;
            UpdateStatus();
        }

        if (_stateFile != null && now - _lastStateWrite > StateWriteInterval)
        {
            _lastStateWrite = now;
            WriteState();
        }

        if (now - _lastStatsRefresh > StatsRefreshInterval)
        {
            _lastStatsRefresh = now;
            UpdateStats(now);
        }
    }

    private long _planetHudTreeVersion = -1;
    private TimeSpan _planetHudAt = TimeSpan.Zero;

    /// <summary>
    /// Applies queued scan batches to the tree, oldest first, until the frame's <see cref="BatchBudget"/> is spent (the
    /// rest wait for the next frame), and hands each batch's explorer replies on. Only here does the tree change (AD-04).
    /// </summary>
    private void DrainBatches()
    {
        if (_session == null || _tree == null)
        {
            return;
        }

        var start = _clock.Elapsed;
        while (_clock.Elapsed - start < BatchBudget && _session.TryDequeue(out var batch))
        {
            _tree.Apply(batch);
            HandleReplies(batch);
        }
    }

    /// <summary>Maps only: re-layout when the tree changed, but during a scan no more often than <see cref="_layoutInterval"/>.</summary>
    private void MaybeRelayout(TimeSpan now)
    {
        if (_tree == null || _tree.Count == 0 || _tree.Version == _layoutTreeVersion)
        {
            return;
        }

        var scanning = _session is { IsFinished: false };
        if (scanning && now - _lastLayoutAt < _layoutInterval && _layout != null)
        {
            return;
        }

        Relayout();
        _lastLayoutAt = now;
    }

    /// <summary>Recomputes the layout for the active visualization and rebuilds its instances, keeping the anchor node fixed on screen.</summary>
    public void Relayout()
    {
        if (_tree == null || _tree.Count == 0)
        {
            return;
        }

        var anchor = _selection.Selected >= 0 ? _selection.Selected : 0;
        GVector3? before = _layout != null && _layout.Contains(anchor) ? Visualization.Gd(_layout.NodeBoxes[anchor].Center) : null;

        var sw = Stopwatch.StartNew();
        var vis = ActiveVisualization;
        _layout = TreeLayout.Compute(_tree, vis.LayoutParams);
        LastLayoutMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        InstanceCount = vis.Rebuild(_tree, _layout);
        LastBuildMs = sw.Elapsed.TotalMilliseconds;
        _layoutTreeVersion = _tree.Version;
        Perf.MarkRelayout(LastLayoutMs + LastBuildMs);

        // Adaptive cadence: re-layout costs at most about 1/8 of the time during a scan.
        _layoutInterval = TimeSpan.FromSeconds(Math.Clamp((LastLayoutMs + LastBuildMs) * RelayoutCostMultiple / 1000.0, MinRelayoutSeconds, MaxRelayoutSeconds));

        if (_needsInitialFrame && _experience != Experience.Places)
        {
            _needsInitialFrame = false;
            ResetView(snap: true);
        }
        else if (before is { } b)
        {
            _rig.ShiftInstantly(Visualization.Gd(_layout.NodeBoxes[anchor].Center) - b);
        }

        UpdateInspector();
    }

    // ---------------------------------------------------------------- visualization

    /// <summary>Switches between High and Low quality (the toolbar button and its key).</summary>
    public void ToggleQuality()
    {
        _quality = _quality == Quality.High ? Quality.Low : Quality.High;
        SetQuality(_quality);
    }

    /// <summary>Applies a quality level to the active experience's lighting and atmosphere and to the viewport's anti-aliasing.</summary>
    public void SetQuality(Quality q)
    {
        _quality = q;
        if (_experience == Experience.Planets) _planets.ApplyQuality(_quality);
        else if (_experience == Experience.Places) PlaceAtmosphere.Configure(_world, _sun, _ground, _quality);
        else ActiveVisualization.ConfigureEnvironment(_world, _sun, _ground, _quality);
        ApplyQualityToViewport();
        _hud.SetQuality(_quality.ToString());
    }

    /// <summary>High: 4× MSAA; Low: the cheaper FXAA post-pass instead.</summary>
    private void ApplyQualityToViewport()
    {
        var vp = GetViewport();
        vp.Msaa3D = _quality == Quality.High ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Disabled;
        vp.ScreenSpaceAA = _quality == Quality.High ? Viewport.ScreenSpaceAAEnum.Disabled : Viewport.ScreenSpaceAAEnum.Fxaa;
    }

    // ---------------------------------------------------------------- camera / navigation
    // Map framing (Classic and District). Distances are in file cells (LayoutParams.FileCell) so the two maps, whose
    // cells differ in size, frame alike; the padding is the share of the box's size the view leaves around it.

    /// <summary>Home: FSN's oblique view from the front, 30° above the horizon, with a 5 % margin.</summary>
    private static readonly Degrees HomeElevation = Degrees.Of(30);

    private const float HomePadding = 1.05f;

    /// <summary>Home frames at most 60 cells either side of the root, so a root with hundreds of children stays readable.</summary>
    private const float HomeMaxHalfWidthCells = 60f;

    /// <summary>Overview (NAV-05): steeply from above (62°), the whole tree with a 2 % margin.</summary>
    private static readonly Degrees OverviewElevation = Degrees.Of(62);

    private const float OverviewPadding = 1.02f;

    /// <summary>Framing a selection: never tighter than 9 cells square, from 25…55° above (the current pitch, clamped), 15 % margin.</summary>
    private const float SelectionMinSizeCells = 9f, SelectionPadding = 1.15f;

    private static readonly Degrees SelectionMinElevation = Degrees.Of(25), SelectionMaxElevation = Degrees.Of(55);

    /// <summary>
    /// The recovery view (Home, NAV-04): the root pedestal and the first row of its children, seen from the front at
    /// FSN's oblique angle, with deeper levels receding into the haze. This is predictable, and close enough to read
    /// labels. <see cref="Overview"/> shows the whole tree instead.
    /// </summary>
    public void ResetView(bool snap = false)
    {
        if (_experience == Experience.Planets)
        {
            _planets.ReturnToArrival();
            return;
        }

        if (_experience == Experience.Places)
        {
            if (_place != null && _transition == null) ArriveAt(_place.Arrival, snap);
            return;
        }

        if (_layout == null || _tree == null || _layout.Count == 0)
        {
            return;
        }

        var box = _layout.NodeBoxes[0];
        foreach (var c in _tree.Root.Children)
        {
            if (_layout.Contains(c)) box = box.Union(_layout.NodeBoxes[c]);
        }

        // Keep the home view a readable size even when the root has hundreds of children.
        var maxHalfWidth = HomeMaxHalfWidthCells * ActiveVisualization.LayoutParams.FileCell;
        if (box.Size.X > maxHalfWidth * 2)
        {
            box = new Box(box.Min with { X = MathF.Max(box.Min.X, -maxHalfWidth) }, box.Max with { X = MathF.Min(box.Max.X, maxHalfWidth) });
        }

        _rig.Frame(new Aabb(Visualization.Gd(box.Min), Visualization.Gd(box.Size)), elevation: HomeElevation, yaw: Degrees.Zero, padding: HomePadding);
        if (snap)
        {
            _rig.Snap();
        }
    }

    /// <summary>A high bird's-eye view that fits the whole tree's width (NAV-05).</summary>
    public void Overview(bool snap = false)
    {
        if (_experience == Experience.Places)
        {
            AerialView(); // B: this place and its neighbours from above (AERIAL-06)
            if (snap) _rig.Snap();
            return;
        }

        if (_layout == null || _layout.Count == 0)
        {
            return;
        }

        var b = _layout.Bounds;
        _rig.Frame(new Aabb(Visualization.Gd(b.Min), Visualization.Gd(b.Size)), elevation: OverviewElevation, yaw: Degrees.Zero, padding: OverviewPadding);
        if (snap)
        {
            _rig.Snap();
        }
    }

    /// <summary>F / double-click: frame the selection (eased). See <see cref="FlyToSelection(bool)"/>.</summary>
    public void FlyToSelection() => FlyToSelection(false);

    /// <summary>
    /// Frames the selection: in Places, within the place (<see cref="FrameInPlace"/>); on a map, a folder with its files
    /// or a file with a little of its neighbourhood, never tighter than <see cref="SelectionMinSizeCells"/>.
    /// </summary>
    public void FlyToSelection(bool snap)
    {
        if (_experience == Experience.Places)
        {
            FrameInPlace();
            if (snap) _rig.Snap();
            return;
        }

        if (_layout == null || _tree == null || _selection.Selected < 0)
        {
            return;
        }

        var id = _selection.Selected;
        if (!_layout.Contains(id))
        {
            _hud.Toast("That item has just been scanned and is not placed yet. It appears after the next layout update.");
            return;
        }

        var box = _layout.NodeBoxes[id];
        if (_tree[id].IsDirectory)
        {
            foreach (var c in _tree[id].Children)
            {
                if (!_tree[c].IsDirectory && _layout.Contains(c)) box = box.Union(_layout.NodeBoxes[c]);
            }
        }
        else
        {
            // Frame a file with some of its neighbourhood, so it stays in context (2.5 u each side, 0.5 u above and below).
            var grow = new System.Numerics.Vector3(2.5f, 0.5f, 2.5f);
            box = new Box(box.Min - grow, box.Max + grow);
        }

        // Never frame tighter than a small neighbourhood, so an empty directory or a tiny file stays in context.
        var minSize = SelectionMinSizeCells * ActiveVisualization.LayoutParams.FileCell;
        var centre = box.Center;
        var half = System.Numerics.Vector3.Max(box.Size * 0.5f, new System.Numerics.Vector3(minSize * 0.5f, 0, minSize * 0.5f));
        box = new Box(centre - half, centre + half);
        _rig.Frame(new Aabb(Visualization.Gd(box.Min), Visualization.Gd(box.Size)), elevation: _rig.Pitch.Clamp(SelectionMinElevation, SelectionMaxElevation), padding: SelectionPadding);
        if (snap)
        {
            _rig.Snap();
        }
    }

    /// <summary>Selects a node by id (ignored if the tree does not hold it), and frames it when <paramref name="fly"/>.</summary>
    public void Select(int id, bool fly = false)
    {
        if (_tree == null || !_tree.Contains(id))
        {
            return;
        }

        _selection.Select(id);
        if (fly)
        {
            FlyToSelection();
        }
    }

    /// <summary>Re-picks what is under the pointer (after it moved); nothing is hovered over a HUD panel.</summary>
    private void UpdateHover()
    {
        var mouse = GetViewport().GetMousePosition();
        if (_hud.BlockingRects.Any(r => r.HasPoint(mouse)))
        {
            _selection.Hovered = -1;
            return;
        }

        _selection.Hovered = PickAt(mouse);
    }

    /// <summary>The node under a screen point (viewport units) in the active experience, or −1.</summary>
    public int PickAt(Vector2 screen)
    {
        if (_experience == Experience.Places)
        {
            return PickPlace(screen);
        }

        if (_tree == null || _layout == null)
        {
            return -1;
        }

        var origin = _rig.Camera.ProjectRayOrigin(screen);
        var dir = _rig.Camera.ProjectRayNormal(screen);
        return Picker.Pick(_tree, _layout, Visualization.Num(origin), Visualization.Num(dir));
    }

    /// <summary>
    /// The maps' selection and hover boxes, and the selection's beam and spot. The beam is shown only from beyond 18 cells
    /// (up close it would obstruct); it is 80 u tall in District, whose buildings are taller, and 40 u in Classic.
    /// </summary>
    private void UpdateHighlights()
    {
        PlaceHighlight(_selectionBox, _selection.Selected, 1.0f);
        PlaceHighlight(_hoverBox, _selection.Hovered != _selection.Selected ? _selection.Hovered : -1, 0.6f);
        var sel = _selection.Selected;
        var visible = sel >= 0 && _layout != null && sel < _layout.Count;
        // The beam marks the selection from afar. Up close it would only obstruct, so it is hidden there.
        _beam.Visible = visible && _rig.Distance > 18f * ActiveVisualization.LayoutParams.FileCell;
        _spot.Visible = visible;
        if (visible)
        {
            var b = _layout!.NodeBoxes[sel];
            var top = new GVector3(b.Center.X, b.Max.Y, b.Center.Z);
            var height = ActiveVisualization is DistrictVisualization ? 80f : 40f;
            _beam.Position = top + new GVector3(0, height / 2, 0);
            _beam.Scale = new GVector3(1, height, 1);
            _spot.Position = top + new GVector3(0, 14, 0); // 14 u above the top, well inside the spot's 34 u reach
        }
    }

    /// <summary>
    /// Fits a highlight box around a node: 4 % larger than its box plus a margin of 0.12 u × <paramref name="grow"/>
    /// (1 for the selection, 0.6 for hover), so the outline sits just outside the node's faces.
    /// </summary>
    private void PlaceHighlight(MeshInstance3D mesh, int id, float grow)
    {
        if (id < 0 || _layout == null || id >= _layout.Count)
        {
            mesh.Visible = false;
            return;
        }

        var b = _layout.NodeBoxes[id];
        var size = Visualization.Gd(b.Size) * 1.04f + GVector3.One * 0.12f * grow;
        mesh.Position = Visualization.Gd(b.Center);
        mesh.Scale = size;
        ((ShaderMaterial)mesh.MaterialOverride).SetShaderParameter("box_size", size);
        mesh.Visible = true;
    }

    // ---------------------------------------------------------------- input
    //
    // Input design (NAV-07..NAV-11, docs/decisions.md AD-12):
    //  * Actions come from InputBindings, never raw keycodes, so aliases and future remapping live in one place.
    //  * Presses count only when they reach _UnhandledInput, that is, when no UI control consumed them. A focused
    //    control that owns the arrow keys therefore keeps them. Releases are observed in _Input, before the GUI, so
    //    a key or button can never stay "held" because a control swallowed its release.
    //  * Focus loss, dialogs, and lost button releases clear held keys, end drags, and release mouse capture.

    private readonly HeldActions _held = new();
    private string? _stateFile;
    private long _stateSeq;
    private double _lastHitchMs, _lastHitchAtMs, _lookBeganMs = -1;
    private TimeSpan _lastStateWrite;
    private bool _windowFocused = true;

    /// <summary>The OS window's real focus. Under --automate input ignores its loss (<see cref="_windowFocused"/> stays true):
    /// injected probe input must not depend on what a person does elsewhere on the machine (KD-31). A person's launch
    /// never has --automate, so for a player focus loss still releases everything and pauses input.</summary>
    private bool _osFocused = true;
    private bool _automationIgnoresFocus;
    private Vector2 _flyStartMouse;
    private int _consumedEscapes;

    /// <summary>A press becomes a drag (orbit, pan or look) once the pointer has moved this far, viewport units; less is a click.</summary>
    private const int DragThreshold = 4;

    /// <summary>One wheel notch on a map: in multiplies the camera distance by 0.87, out by 1.15 (≈ 1/0.87, so in-then-out comes back).</summary>
    private const float MapWheelZoomIn = 0.87f, MapWheelZoomOut = 1.15f;

    /// <summary>A left-button drag in a place turns 1.4× as far per pixel as right-button look capture (rationale not recorded).</summary>
    private const float PlaceDragLookMultiplier = 1.4f;

    /// <summary>True while a modal dialog is open; scene navigation is paused and held keys are cleared.</summary>
    public bool ModalOpen => _welcomeOpen || (_confirm?.Visible ?? false) || (_folderDialog?.Visible ?? false) || (_hud?.LocationsVisible ?? false) || (_hud?.SettingsVisible ?? false);

    public bool WindowFocused => _windowFocused;

    /// <summary>The longest frame time seen since the last state write, milliseconds (diagnostics for probes).</summary>
    public double LastHitchMs => _lastHitchMs;
    public bool OsWindowFocused => _osFocused;

    /// <summary>Test instrument: a probe that checks what focus loss does sets this, so the app honours it under --automate.</summary>
    public bool HonourFocusLoss { get; set; }
    public bool IsDragging => _pressButton != MouseButton.None;
    public IEnumerable<string> HeldActionNames => InputBindings.MovementActions.Where(_held.IsHeld);

    /// <summary>
    /// Sees every event before the GUI. Here go the things no control may swallow: button and key releases (so nothing
    /// stays held), and, while the scene owns the mouse (map flight or Places look capture), motion, the wheel and the
    /// Escape that ends it. Planets routes everything to <see cref="PlanetWorld.Input"/>. Presses are left for
    /// <see cref="_UnhandledInput"/>.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (_closing) return;
        if (_experimentOnly) return;
        // Esc for an open keyboard panel is taken here, before the GUI: with the real mouse resting on the toolbar button
        // that opened the panel, its tooltip is up and the GUI spends the first key press dismissing it, so Esc only
        // closed the panel the second time (os-planets-probe -Dialogs, 2026-11-19; AD-68). PanelOwnsKey does what
        // _UnhandledInput would: a trip's Esc first, then the panel's own Esc.
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape } && KeyboardPanel != KeyPanel.None && PanelOwnsKey(@event)) return;
        if (_experience == Experience.Planets)
        {
            _planets.PointerBlocked = !_windowFocused || ModalOpen;
            if (_planets.Input(@event)) GetViewport().SetInputAsHandled();
            return;
        }

        // Releases first, whoever has focus.
        if (@event is InputEventKey { Pressed: false } up)
        {
            foreach (var a in InputBindings.MovementActions)
            {
                if (up.IsAction(a)) _held.Release(a, InputBindings.KeyId(up));
            }
        }
        else if (@event is InputEventMouseButton { Pressed: false } mbUp)
        {
            if (mbUp.ButtonIndex == MouseButton.Right && _rig.IsFlying)
            {
                EndFly();
            }

            if (mbUp.ButtonIndex == MouseButton.Right && _placeLook)
            {
                EndPlaceLook();
            }

            if (mbUp.ButtonIndex == _pressButton && mbUp.ButtonIndex != MouseButton.Left)
            {
                _pressButton = MouseButton.None;
            }
        }

        if (_placeLook)
        {
            // Places look capture (right mouse held, WALK-03): motion turns the heading and gaze; Esc releases.
            switch (@event)
            {
                case InputEventMouseMotion motion:
                    PlaceLook(motion.ScreenRelative); // physical pixels: look speed must not depend on the UI scale
                    GetViewport().SetInputAsHandled();
                    break;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel when _locomotion == Locomotion.Flying:
                    StepPlaceFlightSpeed((wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1) * WheelNotches(wheel));
                    GetViewport().SetInputAsHandled();
                    break;
                case InputEventKey { Pressed: true, Echo: false } esc when esc.IsAction(InputBindings.Parent):
                    _consumedEscapes++;
                    EndPlaceLook();
                    GetViewport().SetInputAsHandled();
                    break;
            }

            return;
        }

        if (!_rig.IsFlying)
        {
            return;
        }

        // While flying, the scene owns the mouse: motion looks, the wheel sets speed, and Escape only ends flight.
        switch (@event)
        {
            case InputEventMouseMotion motion:
                if (_transition == null) _rig.Look(motion.ScreenRelative);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                // Factor carries multi-notch and fractional (high-resolution wheel, touchpad) scrolling.
                _rig.StepFlySpeed((wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1) * WheelNotches(wheel));
                _hud.SetFlyStatus(true, _rig.FlySpeedWorldUnitsPerSecond, highlight: true);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false } esc when esc.IsAction(InputBindings.Parent):
                _consumedEscapes++;
                EndFly();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>
    /// Presses that no UI control consumed (AD-12): mouse buttons and drags for the scene, held movement keys, and
    /// one-shot commands (<see cref="HandleCommand"/>). On the planets the world sees them first and the shared commands
    /// get what it leaves.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_experimentOnly) return;
        if (PanelOwnsKey(@event)) return; // AD-68 (Main.KeyboardOwner): a trip's Esc, then an open panel owns the keyboard
        if (_experience == Experience.Planets)
        {
            if (@event is InputEventKey { Pressed: true } seen) NoteUnhandledKey(seen);
            if (ProbeShortcutsInert && @event is InputEventKey { Keycode: not Key.Escape }) return; // instrument (PLANET-69): log, do not act
            if (_planets.UnhandledInput(@event)) return;
            if (@event is InputEventMouseButton { Pressed: true }) GetViewport().GuiReleaseFocus();
            if (@event is InputEventKey { Pressed: true, Echo: false } pk) HandleCommand(pk);
            return;
        }

        switch (@event)
        {
            case InputEventMouseButton mb:
                HandleMouseButton(mb);
                break;
            case InputEventMouseMotion mm:
                HandleMouseMotion(mm);
                break;
            case InputEventKey { Pressed: true } key:
                foreach (var a in InputBindings.MovementActions)
                {
                    if (key.IsAction(a)) _held.Press(a, InputBindings.KeyId(key));
                }

                if (!key.Echo)
                {
                    if (key.IsAction(InputBindings.Jump)) _jumpQueued = true;
                    HandleCommand(key);
                }

                break;
        }
    }

    /// <summary>
    /// Mouse buttons in the scene: the wheel (map zoom, or flight speed in Places), right-button flight or look capture,
    /// double-click (fly to, or travel in Places), and the start and end of a left/middle drag; a left press released
    /// without dragging selects what is under it.
    /// </summary>
    private void HandleMouseButton(InputEventMouseButton mb)
    {
        if (mb.Pressed)
        {
            // Clicking the scene takes keyboard focus away from any UI control, so movement keys apply to the scene again.
            GetViewport().GuiReleaseFocus();
        }

        if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            if (_experience == Experience.Places)
            {
                // On foot the wheel does nothing (no zoom: you are standing somewhere); in flight it sets the speed.
                if (mb.Pressed && _locomotion == Locomotion.Flying) StepPlaceFlightSpeed((mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1) * WheelNotches(mb));
                return;
            }

            if (mb.Pressed && !_rig.IsFlying) _rig.Zoom(Mathf.Pow(mb.ButtonIndex == MouseButton.WheelUp ? MapWheelZoomIn : MapWheelZoomOut, WheelNotches(mb)));
            return;
        }

        if (mb.Pressed && mb.IsAction(InputBindings.FlyLook))
        {
            if (_experience == Experience.Places) BeginPlaceLook(mb.Position);
            else BeginFly(mb.Position);
            return;
        }

        if (mb.Pressed)
        {
            if (mb.DoubleClick && mb.ButtonIndex == MouseButton.Left)
            {
                var hit = PickAt(mb.Position);
                if (hit >= 0 && _experience == Experience.Places && _tree != null)
                {
                    // Places: double-clicking a gate, vista or beacon travels there; the landmark returns home.
                    Select(hit);
                    if (hit == _nav.Current) ResetView();
                    else if (_tree[hit].IsDirectory || _tree[hit].Kind == NodeKind.Link) TravelInto(hit);
                    else FlyToSelection();
                }
                else if (hit >= 0)
                {
                    Select(hit, fly: true);
                }

                _pressButton = MouseButton.None;
                return;
            }

            if (mb.IsAction(InputBindings.Orbit) || mb.IsAction(InputBindings.Pan))
            {
                _pressPosition = mb.Position;
                _pressButton = mb.ButtonIndex;
                _dragged = false;
            }

            return;
        }

        if (mb.ButtonIndex == _pressButton)
        {
            if (!_dragged && mb.ButtonIndex == MouseButton.Left)
            {
                var hit = PickAt(mb.Position);
                if (hit >= 0) Select(hit);
            }

            EndDragLook();

            _pressButton = MouseButton.None;
        }
    }

    /// <summary>Pointer motion: marks hover for re-picking, and once past <see cref="DragThreshold"/> orbits or pans a map, or looks in a place.</summary>
    private void HandleMouseMotion(InputEventMouseMotion mm)
    {
        _hoverDirty = true;
        if (_pressButton == MouseButton.None)
        {
            return;
        }

        if (!_dragged && mm.Position.DistanceTo(_pressPosition) > DragThreshold)
        {
            _dragged = true;
        }

        if (!_dragged)
        {
            return;
        }

        if (_experience == Experience.Places)
        {
            // In a place you stand somewhere: dragging turns your head (no orbit, no pan).
            if (_pressButton == MouseButton.Left) PlaceLook(mm.ScreenRelative, CameraRig.LookDegreesPerPixel * PlaceDragLookMultiplier);
            return;
        }

        if (_pressButton == MouseButton.Left)
        {
            _rig.Orbit(Degrees.Of(-mm.ScreenRelative.X * CameraRig.OrbitYawDegreesPerPixel), Degrees.Of(-mm.ScreenRelative.Y * CameraRig.OrbitPitchDegreesPerPixel));
        }
        else if (_pressButton == MouseButton.Middle)
        {
            _rig.PanPixels(mm.Relative, GetViewport().GetVisibleRect().Size.Y); // viewport units: the content follows the cursor
        }
    }

    /// <summary>
    /// One-shot key commands, by action (never raw keycodes). While a place change or a planet launch is in progress only
    /// Help works, so travel happens one step at a time. On the planets some keys mean something else first
    /// (<see cref="HandlePlanetCommand"/>).
    /// </summary>
    private void HandleCommand(InputEventKey key)
    {
        ShortcutCommandCount++;
        var planets = _experience == Experience.Planets;
        var places = _experience == Experience.Places || planets;
        if (_transition != null && !key.IsAction(InputBindings.Help)) return; // travel in progress: one place change at a time
        if (planets && _planets.Busy && !key.IsAction(InputBindings.Help)) return; // a launch in progress: one at a time
        if (key.IsAction(InputBindings.WorldToggle)) { ToggleWorld(); return; }
        if (planets && HandlePlanetCommand(key)) return;
        if (key.IsAction(InputBindings.Open)) RequestOpen();
        else if (key.IsAction(InputBindings.HistoryBack)) { if (places) _nav.Back(); else if (_selection.Back()) FlyToSelection(); }
        else if (key.IsAction(InputBindings.Parent)) HandleEscape();
        else if (key.IsAction(InputBindings.RootPlace)) { if (places) TravelRoot(); else ResetView(); }
        else if (key.IsAction(InputBindings.Home)) ResetView();
        else if (key.IsAction(InputBindings.Overview)) Overview();
        else if (key.IsAction(InputBindings.SwitchVis)) SetExperience(_experience == Experience.Planets ? Experience.Places : (Experience)(((int)_experience + 1) % 3));
        else if (key.IsAction(InputBindings.EnterFolder)) EnterSelection();
        else if (key.IsAction(InputBindings.UpPlace)) { if (places) TravelUp(); }
        else if (key.IsAction(InputBindings.MapToggle)) ToggleMap();
        else if (key.IsAction(InputBindings.Frame)) FlyToSelection();
        else if (key.IsAction(InputBindings.Help)) _hud.ToggleHelp();
        else if (key.IsAction(InputBindings.Legend)) _hud.ToggleLegend();
        else if (key.IsAction(InputBindings.Destinations)) { if (places) _hud.ToggleDestinations(focusFilter: true); }
        else if (key.IsAction(InputBindings.Pin)) { if (places) TogglePin(); }
        else if (key.IsAction(InputBindings.Readme)) { if (places) ShowReadme(); }
        else if (key.IsAction(InputBindings.Quality)) ToggleQuality();
        else if (key.IsAction(InputBindings.Stats)) _hud.SetStats("", !_hud.StatsVisible);
        else if (key.IsAction(InputBindings.Settings)) _hud.ToggleSettings();
        else if (key.IsAction(InputBindings.OpenFolder)) ShowLocations(); // the in-app chooser first (LOC-06); Browse… opens the picker
        else if (key.IsAction(InputBindings.FlightToggle)) ToggleFlight();
        else if (key.IsAction(InputBindings.HiddenToggle)) ToggleHiddenVisibility();
        else if (key.IsAction(InputBindings.GoToPath)) ShowGoToPath();
        else if (key.IsAction(InputBindings.Locations)) ShowLocations();
    }

    /// <summary>Escape peels back one layer at a time: help overlay, then parent directory. Flight is handled in _Input.</summary>
    private void HandleEscape()
    {
        if (_hud.ClosePopups())
        {
            return;
        }

        if (_hud.HelpVisible)
        {
            _hud.ToggleHelp();
            return;
        }

        if (_experience == Experience.Planets && _planets.CloseOverlay()) return;
        if (_experience is Experience.Places or Experience.Planets)
        {
            // In a place, Esc closes the inspector card (clears the selection); it never moves you.
            if (_hud.DestinationsOpen) _hud.ToggleDestinations(focusFilter: false);
            else if (_selection.Selected >= 0) _selection.Select(-1);
            return;
        }

        if (_tree != null && _selection.Parent(_tree)) FlyToSelection();
    }

    /// <summary>
    /// The notches in one wheel event. Godot reports 0 for ordinary single-notch events from some devices, so 0 counts as
    /// one notch; otherwise the factor is kept within 0.05…10 notches per event.
    /// </summary>
    private static float WheelNotches(InputEventMouseButton e) => e.Factor > 0 ? Mathf.Clamp(e.Factor, 0.05f, 10f) : 1f;

    private void BeginFly(Vector2 mousePosition)
    {
        if (ModalOpen || !_windowFocused)
        {
            return;
        }

        _flyStartMouse = mousePosition;
        _pressButton = MouseButton.None;
        _rig.EnterFly();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _hud.SetFlyStatus(true, _rig.FlySpeedWorldUnitsPerSecond, highlight: false);
        _hud.NoteInteraction(flew: true);
    }

    /// <summary>Ends flight without moving the camera. It restores the cursor where flight began and re-pivots on what is under the screen centre.</summary>
    public void EndFly()
    {
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            if (_flyStartMouse != Vector2.Zero) Input.WarpMouse(_flyStartMouse);
        }

        if (!_rig.IsFlying)
        {
            return;
        }

        _rig.ExitFly();
        _hud.SetFlyStatus(false, _rig.FlySpeedWorldUnitsPerSecond, highlight: false);
        RepivotOnView();
    }

    /// <summary>
    /// After flight, orbit around whatever is under the screen centre: the picked surface, else the ground ahead (only if
    /// the view points at least slightly down, so a near-horizontal ray does not pivot kilometres away). A pivot nearer
    /// than 2 u or beyond 3000 u is ignored and the rig keeps its own.
    /// </summary>
    private void RepivotOnView()
    {
        var cam = _rig.Camera;
        var centre = GetViewport().GetVisibleRect().Size / 2;
        var origin = cam.ProjectRayOrigin(centre);
        var dir = cam.ProjectRayNormal(centre);
        float? t = null;
        if (_tree != null && _layout != null && Picker.Pick(_tree, _layout, Visualization.Num(origin), Visualization.Num(dir)) is var hit && hit >= 0)
        {
            t = _layout.NodeBoxes[hit].Intersect(Visualization.Num(origin), Visualization.Num(dir));
        }

        if (t == null && dir.Y < -0.05f)
        {
            t = -origin.Y / dir.Y;
        }

        if (t is { } d && d > 2f && d < 3000f)
        {
            _rig.SetPivotDistanceKeepingPose(d);
        }
    }

    /// <summary>A snapshot of navigation state for external probes and automation logs.</summary>
    public string StateJson()
    {
        var p = _rig.Camera.GlobalPosition;
        var t = _rig.Target;
        var up = _rig.Camera.GlobalTransform.Basis.Y;
        return
            System.FormattableString.Invariant($"{{\"seq\":{_stateSeq},\"appMs\":{_clock.Elapsed.TotalMilliseconds:0},\"frameMs\":{_now.TotalMilliseconds:0},\"lookBeganMs\":{_lookBeganMs:0},\"lastHitchMs\":{_lastHitchMs:0},\"lastHitchAtMs\":{_lastHitchAtMs:0},\"mode\":\"{_rig.Mode}\",\"mouseMode\":\"{Input.MouseMode}\",\"focused\":{(_windowFocused ? "true" : "false")},\"modal\":{(ModalOpen ? "true" : "false")},\"dragging\":\"{_pressButton}\",") +
            System.FormattableString.Invariant($"\"pos\":[{p.X:0.###},{p.Y:0.###},{p.Z:0.###}],\"target\":[{t.X:0.###},{t.Y:0.###},{t.Z:0.###}],\"yaw\":{_rig.Yaw.Value:0.###},\"lookPitch\":{_rig.LookPitch.Value:0.###},\"up\":{up.Y:0.####},") +
            System.FormattableString.Invariant($"\"distance\":{_rig.Distance:0.###},\"flySpeed\":{_rig.FlySpeedWorldUnitsPerSecond:0.###},\"held\":[{string.Join(",", HeldActionNames.Select(h => $"\"{h}\""))}],") +
            System.FormattableString.Invariant($"\"selected\":{_selection.Selected},\"vis\":{_visIndex},\"hint\":{(_hud.HintVisible ? "true" : "false")},\"flyChip\":{(_hud.FlyChipVisible ? "true" : "false")},\"consumedEscapes\":{_consumedEscapes},") +
            StateJsonTutorial() +
            System.FormattableString.Invariant($"\"experience\":\"{_experience}\",\"current\":{_nav.Current},\"currentName\":\"{JsonEscape(_tree != null && _tree.Count > 0 ? _tree.DisplayName(_nav.Current) : "")}\",\"transition\":{(_transition != null ? "true" : "false")},\"transitions\":{_transitions},\"gates\":{_place?.Gates.Count ?? -1},\"selectedName\":\"{JsonEscape(_tree != null && _selection.Selected >= 0 ? _tree[_selection.Selected].Name : "")}\",\"lastShell\":\"{JsonEscape(LastShellMessage ?? "")}\",") +
            System.FormattableString.Invariant($"\"locomotion\":\"{_locomotion}\",\"grounded\":{(_walker.Grounded ? "true" : "false")},\"support\":{_walker.Support.Value:0.###},\"feetY\":{_walker.Feet.Y:0.###},\"lookCaptured\":{(_placeLook ? "true" : "false")},\"jumps\":{_walker.Jumps},\"relocations\":{_relocations},\"landing\":{(_landing != null ? "true" : "false")},\"guided\":{(_guided ? "true" : "false")},\"showHidden\":{(_showHidden ? "true" : "false")},\"locationsOpen\":{(_hud.LocationsVisible ? "true" : "false")},\"via\":{_nav.CurrentVia},\"currentPath\":\"{JsonEscape(_tree != null && _tree.Count > 0 ? _tree.FullPath(_nav.Current) : "")}\",") +
            StateJsonNavigation() + StateJsonInput() + StateJsonSettings() + _planets.StateJson() + "}";
    }

    /// <summary>Escapes a string for the state file's JSON: backslash, quote and the control characters (a trip panel's
    /// text has line breaks; unescaped, the state file stopped parsing, 2026-11-17).</summary>
    private static string JsonEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    private void WriteState()
    {
        try
        {
            // seq and appMs let an outside probe tell a fresh snapshot from one written before its input (TEST-03).
            _stateSeq++;
            System.IO.File.WriteAllText(_stateFile!, StateJson());
        }
        catch (System.IO.IOException)
        {
            // A reader may hold the file briefly; the next write catches up.
        }
    }

    /// <summary>Clears every transient input state. Called on focus loss and when a modal dialog opens.</summary>
    private void ReleaseAllInput()
    {
        EndFly();
        EndPlaceLook();
        _jumpQueued = false;
        _held.Clear();
        EndDragLook();
        _pressButton = MouseButton.None;
        _dragged = false;
    }

    /// <summary>A left-button drag in a place is a look: when it ends, the view returns to level like a right-button look (CTRL-04).</summary>
    private void EndDragLook()
    {
        if (_experience == Experience.Places && _pressButton == MouseButton.Left && _dragged) LevelAfterLook();
    }

    /// <summary>
    /// Per frame: turns held movement keys into motion — the Places walker or flight, else map flight or ground moves.
    /// When the keyboard belongs elsewhere (focus lost, a dialog, a focused control) every held state is released; during
    /// a place transition movement pauses but held keys are kept, so walking resumes on arrival (PLACE-16). Also catches
    /// button releases the OS never delivered.
    /// </summary>
    private void HandleKeyboardMovement(TimeSpan dt)
    {
        // A focused UI control (or an open dialog) owns the keyboard; navigation resumes when it lets go.
        var blocked = !_windowFocused || ModalOpen || GetViewport().GuiGetFocusOwner() != null || KeyboardPanel != KeyPanel.None || _transition != null;
        if (_transition != null && _windowFocused && !ModalOpen)
        {
            return; // travel in progress: movement pauses, flight (if any) continues afterwards
        }

        if (blocked)
        {
            if (_held.Any || _rig.IsFlying || _placeLook || _pressButton != MouseButton.None) ReleaseAllInput();
            // A walker keeps falling and landing while the keyboard is elsewhere; it just gets no input.
            if (_experience == Experience.Places) UpdatePlaceLocomotion(dt, blocked: true);
            return;
        }

        if (_experience == Experience.Places)
        {
            if (_placeLook && !Input.IsMouseButtonPressed(MouseButton.Right)) EndPlaceLook();
            if (_pressButton != MouseButton.None && !Input.IsMouseButtonPressed(_pressButton))
            {
                EndDragLook(); // a release the OS did not deliver still ends the look
                _pressButton = MouseButton.None;
            }

            UpdatePlaceLocomotion(dt, blocked: false);
            return;
        }

        // Lost button releases (for example a release outside the window that the OS did not deliver) end the drag or flight.
        if (_rig.IsFlying && !Input.IsMouseButtonPressed(MouseButton.Right)) EndFly();
        if (_pressButton != MouseButton.None && !Input.IsMouseButtonPressed(_pressButton)) _pressButton = MouseButton.None;

        // In the maps the arrow keys keep moving sideways (NAV-07); only Places turns with them (WALK-03).
        var input = new GVector3(
            Mathf.Clamp(_held.Axis(InputBindings.Left, InputBindings.Right) + _held.Axis(InputBindings.TurnLeft, InputBindings.TurnRight), -1, 1),
            _held.Axis(InputBindings.Down, InputBindings.Up),
            _held.Axis(InputBindings.Back, InputBindings.Forward));
        var fast = _held.IsHeld(InputBindings.Fast);
        if (_rig.IsFlying)
        {
            _rig.FlyMove(input, fast, dt);
        }
        else if (input != GVector3.Zero)
        {
            _rig.MoveGround(input, fast, dt);
            _hud.NoteInteraction(flew: false);
        }
    }

    // ---------------------------------------------------------------- deliberate actions

    /// <summary>Opens the selection with Windows. It asks first for code-running files, links, and cloud placeholders (FILE-01..03).</summary>
    public void RequestOpen()
    {
        if (_tree == null || _selection.Selected < 0)
        {
            if (_tree != null) _hud.Toast("Nothing is selected to open: click a file or folder first."); // PLANET-43: never a silent no-op
            return;
        }

        var node = _tree[_selection.Selected];
        if (node.IsHub)
        {
            // The hub has no filesystem path (PLANET-87): Open shows This PC, which is what it stands for.
            var r = _shell.Open(SelectionActions.ThisPcShellPath);
            ReportShell(r.Succeeded && !r.WasMock && !_recordShell ? r with { Message = "This PC opened in File Explorer." } : r);
            return;
        }

        var path = _tree.FullPath(node.Id);
        if (!_tree.Source.IsLive)
        {
            ReportShell(_shell.Open(path));
            return;
        }

        string? reason = null;
        if (node.Kind == NodeKind.File && LaunchRisk.RunsCode(node.Name))
        {
            reason = $"“{node.Name}” can run a program or script.\nWindows will execute it with your permissions.";
        }
        else if (node.Kind == NodeKind.Link)
        {
            reason = $"“{node.Name}” is a link. FileScape did not follow it; Windows will open its target, which may be elsewhere.";
        }
        else if ((node.Flags & NodeFlags.CloudPlaceholder) != 0)
        {
            reason = $"“{node.Name}” is a cloud placeholder. Opening it may download it from the provider.";
        }

        if (reason != null)
        {
            Confirm(reason + "\n\nOpen it with Windows?", () => ReportShell(_shell.Open(path)));
        }
        else
        {
            ReportShell(_shell.Open(path));
        }
    }

    /// <summary>Shows the selection in Windows Explorer (or the mock shell's simulation for sample data).</summary>
    public void RequestReveal()
    {
        if (CardSelection("show in Explorer") is not { } node) return;
        var a = CardActionsFor(node).Reveal;
        if (!a.Enabled) { ToastUnavailable(a); return; } // the hub, synthetic data (PLANET-87)

        ReportShell(_shell.RevealInExplorer(_tree!.FullPath(node.Id)));
    }

    private void CopyPath()
    {
        if (CardSelection("copy the path of") is not { } node) return;
        var a = CardActionsFor(node).Copy;
        if (!a.Enabled) { ToastUnavailable(a); return; } // the hub has no path (PLANET-87)

        // Through the session's one copy path (Main.RootMark): recorded, not copied, under --test-shell=record or on
        // synthetic data, so a probe never overwrites the person's clipboard.
        ((FileScape.App.Planets.IDeveloperSession)this).CopyText(_tree!.FullPath(node.Id), "Path");
    }

    /// <summary>Logs a shell action's outcome and toasts it: amber when simulated (sample data), green on success (6 s),
    /// red on failure (<see cref="ShellFailureToastSeconds"/>, long enough to read why: PLANET-43).</summary>
    private void ReportShell(ShellResult r)
    {
        LastShellMessage = r.Message;
        LastShellResult = r;
        GD.Print($"[shell] succeeded={r.Succeeded} mock={r.WasMock} {r.Message}");
        var failed = !r.Succeeded && !r.WasMock;
        _hud.Toast(r.Message, r.WasMock ? new Color(1f, 0.85f, 0.45f) : r.Succeeded ? new Color(0.6f, 1f, 0.7f) : new Color(1f, 0.5f, 0.45f), failed ? ShellFailureToastSeconds : 6);
    }

    /// <summary>A refused open or reveal stays on screen this long, seconds (its reason is a sentence to read).</summary>
    private const double ShellFailureToastSeconds = 10;

    /// <summary>Probe access: whether a confirmation is showing, and its text; cancelling it runs nothing.</summary>
    public bool ConfirmShowing => _confirm?.Visible == true;
    public string ConfirmText => _confirm?.DialogText ?? "";

    public void ProbeCancelConfirm()
    {
        _pendingConfirm = null;
        _confirm.Hide();
    }

    /// <summary>Asks before a consequential action; <paramref name="onOk"/> runs only on Continue. Held input is released first.</summary>
    public void Confirm(string text, Action onOk)
    {
        ReleaseAllInput();
        _pendingConfirm = onOk;
        _confirm.DialogText = text;
        _confirm.PopupCentered(new Vector2I(560, 200)); // minimum size, viewport units
    }

    // ---------------------------------------------------------------- HUD content

    /// <summary>The inspector card's one-line brief in Places (VIEW-02): what it is, how big, and its state; details on demand.</summary>
    private string Brief(FsNode node)
    {
        var tree = _tree!;
        if (node.Kind == NodeKind.Link) return $"Link · {PlaceText.LinkShort(tree, node)}";
        if (node.IsDirectory)
        {
            var state = Listing.Of(node);
            return Listing.IsUnknown(state) && state != ListingState.Partial ? $"Folder · {Listing.Describe(state)}" : "Folder · " + FolderSummary.Gate(node, state);
        }

        var modified = node.ModifiedUtc is { } m ? $" · modified {m.ToLocalTime():yyyy-MM-dd}" : "";
        return $"{PlaceText.CategoryName(FileCategories.Of(node.Name)).TrimEnd('s')} file · {SizeFormat.Short(node.Size)}{modified}";
    }

    /// <summary>
    /// Refreshes the inspector for the selection: in the walking worlds a compact card (or none when nothing is
    /// selected), on a map the full panel; and which actions (open, reveal, parent, go inside, retry) apply.
    /// </summary>
    private void UpdateInspector()
    {
        var worldCard = _experience is Experience.Places or Experience.Planets;
        if (worldCard && (_tree == null || _tree.Count == 0 || _selection.Selected < 0))
        {
            // Places: nothing selected → no card; the place bar and the plaque say where you are (VIEW-02).
            _hud.SetInspectorCard(compact: true, visible: false, "", null);
            _hud.SetSelectionActions(false, false, false);
            _hud.SetEnterAction(false, "Go inside (G)");
            return;
        }

        if (_tree == null || _tree.Count == 0 || _selection.Selected < 0)
        {
            _hud.SetEnterAction(false, "Go inside (G)");
            _hud.SetInspector("Nothing selected",
                "[color=#9fb0cc]Click a pedestal or block to inspect it. Double-click to fly to it.\n\nPress [b]F1[/b] for controls, [b]Home[/b] to reset the view.[/color]",
                new Color(0, 0, 0, 0));
            _hud.SetSelectionActions(false, false, false);
            return;
        }

        var node = _tree[_selection.Selected];
        _hud.SetInspector(_tree.DisplayName(node.Id), Inspector.Describe(node, _tree, ActiveVisualization), ActiveVisualization.ColorFor(node, _tree));
        if (worldCard)
        {
            var current = node.Id == _nav.Current ? _place != null && KindOf(node.Id) is { } k && (k.Kind != FileScape.Core.Places.PlaceKind.Ordinary || k.Overridden) ? $"You are here · {k.Label}: {string.Join(", ", k.Evidence)}\n" : "You are here\n" : "";
            _hud.SetInspectorCard(compact: true, visible: true, current + Brief(node), FileIcons.Get(FileIcons.For(node)));
            UpdateCardExtras(node);
        }
        else
        {
            _hud.SetInspectorCard(compact: false, visible: true, "", null);
        }

        ApplyCardActions(node); // Main.SelectionActions (PLANET-87): labels, enabled and why not, for this view
        var places = worldCard;
        _hud.SetWorldActions(_explorer != null && NeedsRetry(node), null);
        var enterable = (node.IsDirectory || (node.Kind == NodeKind.Link && node.Link is { CanFollow: true })) && node.Id != _nav.Current;
        _hud.SetEnterAction(enterable || (!places && !node.IsDirectory),
            places ? (node.Id == _nav.Current ? "You are here" : node.Kind == NodeKind.Link ? "Follow link (G)" : "Go inside (G)") : "Go here in Places (G)");
        if (places) _hud.HighlightDestination(node.Id);
    }

    private static bool NeedsRetry(FsNode n) =>
        (n.Kind == NodeKind.Directory && (n.ScanState is DirScanState.Failed or DirScanState.Cancelled || (n.Flags & (NodeFlags.AccessDenied | NodeFlags.Unavailable | NodeFlags.EnumerationError)) != 0)) ||
        (n.Kind == NodeKind.Link && n.Link is { Status: LinkStatus.Broken or LinkStatus.Denied or LinkStatus.Unavailable });

    /// <summary>
    /// The status line from the scan session's snapshot: on-demand exploration (what is being listed, and what Windows is
    /// slow on), or a bounded scan's progress and outcome. The <c>[scan]</c> log line is printed once per scan (probes parse it).
    /// </summary>
    private void UpdateStatus()
    {
        if (_session == null || _tree == null)
        {
            return;
        }

        var p = _session.Snapshot();
        if (_explorer != null && p.State == ScanState.Running)
        {
            // On-demand exploration (AD-17): the source never "completes"; say what it is doing.
            var listed = $"{p.Nodes:N0} entries listed · {p.Directories:N0} folders · {p.Files:N0} files · {p.Links:N0} links · {SizeFormat.Short(p.LogicalBytes)}";
            var bg = p.BackgroundActive ? $" · listing the arrival folder in the background ({p.PendingDirectories:N0} queued)" : "";
            var waiting = p.CurrentPath != null && p.CurrentOperationDuration.TotalSeconds > SlowListingNoticeSeconds
                ? $" · waiting on Windows for {p.CurrentOperationDuration.TotalSeconds:0.0}s: {p.CurrentPath}"
                : "";
            var problemsText = p.Problems > 0 ? $" · {p.Problems:N0} folders could not be listed" : "";
            if (p.LimitReached) problemsText += $" · ENTRY LIMIT ({p.MaxNodes:N0}) reached: some folders are partly listed — Locations › Start fresh here";
            var head = p.Idle && waiting.Length == 0 ? "Exploring on demand" : "Listing…";
            _hud.SetStatus($"{head} · {listed}{problemsText}{bg}{waiting}", p.Idle ? 1 : 0.5, p.BackgroundActive, p.Problems > 0 ? new Color(1f, 0.85f, 0.6f) : new Color(0.7f, 0.95f, 0.8f));
            return;
        }

        var counts = $"{p.Nodes:N0} entries · {p.Directories:N0} dirs · {p.Files:N0} files · {p.Links:N0} links · {SizeFormat.Short(p.LogicalBytes)} logical";
        var problems = p.Problems > 0 ? $" · {p.Problems:N0} problem dirs" : "";
        var done = Math.Max(0, p.Directories - p.PendingDirectories);
        var frac = p.Directories == 0 ? 0 : done / (double)p.Directories;
        var source = _tree.Source.IsLive ? "" : "Synthetic ";
        switch (p.State)
        {
            case ScanState.Running:
                var op = p.CurrentPath != null && p.CurrentOperationDuration.TotalSeconds > SlowListingNoticeSeconds
                    ? $"  ·  waiting on Windows for {p.CurrentOperationDuration.TotalSeconds:0.0}s: {p.CurrentPath}"
                    : "";
                _hud.SetStatus($"{source}Scanning… {counts}{problems} · {p.PendingDirectories:N0} dirs queued · {p.Elapsed.TotalSeconds:0.0}s{op}", frac, true);
                break;
            case ScanState.Cancelling:
                var wait = p.CurrentPath != null ? $" Waiting for Windows to finish listing {p.CurrentPath} ({p.CurrentOperationDuration.TotalSeconds:0.0}s); the app is responsive." : "";
                _hud.SetStatus($"Cancelling…{wait} {counts}", frac, true, new Color(1f, 0.85f, 0.5f));
                break;
            default:
                var summary = p.State switch
                {
                    ScanState.Completed => $"{source}Scan complete in {p.Elapsed.TotalSeconds:0.00}s",
                    ScanState.Cancelled => $"Scan CANCELLED after {p.Elapsed.TotalSeconds:0.00}s (stopped {p.CancelAcknowledgedAfter?.TotalMilliseconds:0} ms after the request) — partial tree; unscanned directories are marked",
                    ScanState.LimitReached => $"Scan stopped at the {ScanSession.DefaultMaxNodes:N0}-entry limit — partial tree; unscanned directories are marked",
                    _ => $"Scan FAILED: {p.FailureMessage} — partial tree shown",
                };
                var tint = p.State == ScanState.Completed && p.Problems == 0 ? new Color(0.7f, 0.95f, 0.8f) : new Color(1f, 0.82f, 0.5f);
                _hud.SetStatus($"{summary} · {counts}{problems}", 1, false, tint);
                if (!_scanFinishedReported)
                {
                    _scanFinishedReported = true;
                    GD.Print($"[scan] {p.State} nodes={p.Nodes} dirs={p.Directories} files={p.Files} links={p.Links} bytes={p.LogicalBytes} problems={p.Problems} elapsed_ms={p.Elapsed.TotalMilliseconds:0} cancel_ack_ms={p.CancelAcknowledgedAfter?.TotalMilliseconds:0}");
                }

                break;
        }
    }

    /// <summary>The stats overlay (when shown): frame times over the last 120 frames, scene and layout costs, memory and GPU.</summary>
    private void UpdateStats(TimeSpan now)
    {
        if (!_hud.StatsVisible)
        {
            return;
        }

        if (now - _lastProcessMemoryAt > ProcessMemoryInterval)
        {
            _lastProcessMemoryAt = now;
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            _processPrivateBytes = proc.PrivateMemorySize64;
        }

        var s = Perf.Summary(120);
        var gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
        var draw = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var vmem = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024 * 1024);
        _hud.SetStats(
            $"{Engine.GetFramesPerSecond():0} fps · frame avg {s.Mean:0.0} ms · p95 {s.P95:0.0} · max {s.Max:0.0} · GPU {gpu:0.0} ms\n" +
            $"nodes {_tree?.Count:N0} · instances {InstanceCount:N0} · draw calls {draw:0} · labels {_labels.LastShown}\n" +
            $"last layout {LastLayoutMs:0.0} ms · build {LastBuildMs:0.0} ms · relayout every ≥{_layoutInterval.TotalSeconds:0.00}s while scanning\n" +
            $"managed {GC.GetTotalMemory(false) / (1024 * 1024):N0} MB · process private {_processPrivateBytes / (1024 * 1024):N0} MB · video {vmem:0} MB\n" +
            $"{RenderingServer.GetVideoAdapterName()} · {RenderingServer.GetVideoAdapterApiVersion()} · {_quality} · {ActiveVisualization.Name}" +
            // FOLLOW-2 (2026-11-17): a gap a person hears with this count unchanged is not FileScape's (the device or its driver).
            (_experience == Experience.Planets ? $"\naudio: {_planets.Audio.AudibleSkips} gaps since start · queued {_planets.Audio.QueuedSeconds * 1000:0} ms · audio thread's longest wake {_planets.Audio.LoopGapMaxMs:0} ms" : ""),
            true);
    }

    /// <summary>Window close and quit begin the orderly shutdown; losing focus releases every held input (so Alt-Tab leaves nothing stuck).</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            BeginShutdown("window closed");
        }
        else if (what == NotificationPredelete)
        {
            BeginShutdown("quit");
        }
        else if (what is (int)NotificationApplicationFocusOut or (int)NotificationWMWindowFocusOut)
        {
            // Alt-Tab and similar: release capture and forget held keys, so nothing is stuck on return.
            _osFocused = false;
            if (_automationIgnoresFocus && !HonourFocusLoss) return;
            _windowFocused = false;
            if (_rig != null) ReleaseAllInput();
        }
        else if (what is (int)NotificationApplicationFocusIn or (int)NotificationWMWindowFocusIn)
        {
            _osFocused = true;
            _windowFocused = true;
        }
    }
}
