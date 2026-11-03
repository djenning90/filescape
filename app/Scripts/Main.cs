using System.Diagnostics;
using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Scanning;
using FileScape.Core.Shell;
using Godot;
using GVector3 = Godot.Vector3;

namespace FileScape.App;

/// <summary>
/// The application controller. It owns the current tree, scan session, layout, active visualization, and the
/// selection, and it routes input and HUD intents. The data flow is one-way: scan → tree → layout → visualization.
/// The selection refers only to node ids (docs/architecture.md).
/// </summary>
public partial class Main : Node3D
{
    private const double BatchBudgetMs = 6.0;

    private readonly Visualization[] _visualizations = [new ClassicVisualization(), new DistrictVisualization()];
    private readonly SelectionState _selection = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private CameraRig _rig = null!;
    private WorldEnvironment _world = null!;
    private DirectionalLight3D _sun = null!;
    private MeshInstance3D _ground = null!;
    private Hud _hud = null!;
    private LabelLayer _labels = null!;
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
    private double _lastLayoutAt = -10;
    private double _layoutInterval = 0.25;
    private bool _needsInitialFrame;
    private bool _scanFinishedReported;

    private Vector2 _pressPosition;
    private MouseButton _pressButton = MouseButton.None;
    private bool _dragged;
    private bool _hoverDirty;
    private double _lastLabelRefresh, _lastStatusRefresh, _lastStatsRefresh, _lastProcessMemoryAt;
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
    public int LabelsShown => _labels.LastShown;

    public override void _Ready()
    {
        InputBindings.Register();
        var args = CommandLine.Parse(OS.GetCmdlineUserArgs());
        var dpiScale = Mathf.Clamp(DisplayServer.ScreenGetDpi() / 96f, 1f, 3f);
        GetWindow().ContentScaleFactor = args.UiScale ?? dpiScale;
        GetWindow().Title = "FileScape";
        if (args.WindowSize is { } ws)
        {
            DisplayServer.WindowSetSize(ws);
        }

        _stateFile = args.StateFile;
        BuildScene();
        _quality = args.Quality ?? Quality.High;
        _visIndex = args.Visualization ?? 0;
        ApplyVisualization(frameSelection: false);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);

        if (args.Root is { } root)
        {
            StartScan(new DirectoryScanner(root));
        }
        else
        {
            StartScan(new SyntheticTreeSource { TargetNodes = args.SyntheticNodes ?? 12_000, NodesPerSecond = args.SyntheticRate ?? 0 });
        }

        if (args.Automation == "navigation")
        {
            AddChild(new NavigationProbe(this, args.OutputDirectory ?? OS.GetUserDataDir()));
        }
        else if (args.Automation is { } script)
        {
            var automation = new Automation(this, script, args.OutputDirectory ?? OS.GetUserDataDir(), args);
            AddChild(automation);
        }
    }

    private void BuildScene()
    {
        _world = new WorldEnvironment();
        AddChild(_world);
        _sun = new DirectionalLight3D { ShadowEnabled = true };
        _sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        AddChild(_sun);
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
        ((ShaderMaterial)_hoverBox.MaterialOverride).SetShaderParameter("pulse", 0f);
        ((ShaderMaterial)_hoverBox.MaterialOverride).SetShaderParameter("face_alpha", 0.03f);
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
        AddChild(labelCanvas);
        _hud = new Hud { Layer = 1 };
        AddChild(_hud);

        _hud.OpenFolderRequested += ShowFolderDialog;
        _hud.DemoRequested += () => StartScan(new SyntheticTreeSource());
        _hud.CancelRequested += CancelScan;
        _hud.VisualizationRequested += SetVisualization;
        _hud.ResetRequested += () => ResetView();
        _hud.OverviewRequested += () => Overview();
        _hud.QualityToggled += ToggleQuality;
        _hud.OpenRequested += RequestOpen;
        _hud.RevealRequested += RequestReveal;
        _hud.ParentRequested += () => { if (_tree != null && _selection.Parent(_tree)) FlyToSelection(); };
        _hud.FlyToRequested += FlyToSelection;
        _hud.CopyPathRequested += CopyPath;
        _hud.HelpToggled += () => _hud.ToggleHelp();
        _selection.SelectionChanged += _ => UpdateInspector();

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
        _shell = _tree.Source.IsLive ? new WindowsShellActions() : new MockShellActions();
        _layout = null;
        _layoutTreeVersion = -1;
        _lastLayoutAt = -10;
        _needsInitialFrame = true;
        _scanFinishedReported = false;
        _selection.Reset();
        foreach (var v in _visualizations)
        {
            v.Clear();
        }

        _hud.SetSource(_tree.Source.IsLive ? $"LIVE · {MiddleTruncate(_tree.Source.RootDisplayPath, 48)}" : "SYNTHETIC SAMPLE DATA — not your files", _tree.Source.IsLive,
            _tree.Source.IsLive ? $"Live Windows filesystem, metadata only: {_tree.Source.RootDisplayPath}" : _tree.Source.Description);
        _session.Start();
        UpdateInspector();
    }

    private static string MiddleTruncate(string s, int max) =>
        s.Length <= max ? s : string.Concat(s.AsSpan(0, max / 2 - 1), "…", s.AsSpan(s.Length - (max - max / 2)));

    public void CancelScan()
    {
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
        var full = System.IO.Path.GetFullPath(path);
        var reason = BroadRootReason(full);
        if (reason != null)
        {
            Confirm($"{reason}\n\nScanning reads metadata only, but a large tree can take a long time and use a lot of memory (the scan stops at {ScanSession.DefaultMaxNodes:N0} entries). Cloud-backed folders may contact their provider.\n\nScan {full} anyway?",
                () => StartScan(new DirectoryScanner(full)));
            return;
        }

        StartScan(new DirectoryScanner(full));
    }

    /// <summary>Returns a warning if the chosen root is a whole drive, a user profile, or a cloud-synced folder (FS-12).</summary>
    private static string? BroadRootReason(string full)
    {
        var trimmed = System.IO.Path.TrimEndingDirectorySeparator(full);
        if (System.IO.Path.GetPathRoot(full) is { } pr && System.IO.Path.TrimEndingDirectorySeparator(pr).Equals(trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return "This is the root of a drive or network share.";
        }

        var profile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (trimmed.Equals(System.IO.Path.TrimEndingDirectorySeparator(profile), StringComparison.OrdinalIgnoreCase))
        {
            return "This is your whole user profile.";
        }

        foreach (var v in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var od = System.Environment.GetEnvironmentVariable(v);
            if (!string.IsNullOrEmpty(od) && trimmed.StartsWith(System.IO.Path.TrimEndingDirectorySeparator(od), StringComparison.OrdinalIgnoreCase))
            {
                return "This folder is inside OneDrive (cloud-synced).";
            }
        }

        return full.StartsWith(@"\\", StringComparison.Ordinal) ? "This is a network path." : null;
    }

    // ---------------------------------------------------------------- frame loop

    public override void _Process(double delta)
    {
        Perf.BeginFrame(delta, _session is { IsFinished: false });
        var now = _clock.Elapsed.TotalSeconds;
        DrainBatches();
        MaybeRelayout(now);
        HandleKeyboardMovement((float)delta);

        if (_layout != null && _tree != null)
        {
            if (_hoverDirty)
            {
                _hoverDirty = false;
                UpdateHover();
            }

            UpdateHighlights();
            if (now - _lastLabelRefresh > 0.08)
            {
                _lastLabelRefresh = now;
                _labels.Refresh(_rig.Camera, _tree, _layout, _selection, _hud.BlockingRects);
            }
        }

        if (now - _lastStatusRefresh > 0.1)
        {
            _lastStatusRefresh = now;
            UpdateStatus();
        }

        if (_stateFile != null && now - _lastStateWrite > 0.15)
        {
            _lastStateWrite = now;
            WriteState();
        }

        if (now - _lastStatsRefresh > 0.25)
        {
            _lastStatsRefresh = now;
            UpdateStats(now);
        }
    }

    private void DrainBatches()
    {
        if (_session == null || _tree == null)
        {
            return;
        }

        var start = _clock.Elapsed.TotalMilliseconds;
        while (_clock.Elapsed.TotalMilliseconds - start < BatchBudgetMs && _session.TryDequeue(out var batch))
        {
            _tree.Apply(batch);
        }
    }

    private void MaybeRelayout(double now)
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
        _layoutInterval = Math.Clamp((LastLayoutMs + LastBuildMs) * 8 / 1000.0, 0.25, 3.0);

        if (_needsInitialFrame)
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

    public void SetVisualization(int index)
    {
        if (index == _visIndex)
        {
            return;
        }

        _visIndex = index;
        ApplyVisualization(frameSelection: true);
    }

    private void ApplyVisualization(bool frameSelection)
    {
        for (var i = 0; i < _visualizations.Length; i++)
        {
            _visualizations[i].Root.Visible = i == _visIndex;
        }

        var vis = ActiveVisualization;
        vis.ConfigureEnvironment(_world, _sun, _ground, _quality);
        ApplyQualityToViewport();
        _hud.SetVisualization(_visIndex);
        _hud.SetQuality(_quality.ToString());
        var scale = vis.LayoutParams.Scale;
        _hud.SetLegend(vis.ColorMeaning, vis.Legend,
            $"Height = log10 of logical size: each ×10 in size adds {scale.PerDecade * scale.Multiplier:0.00} u; zero-byte files are thin tiles; capped at {scale.Cap:0} u. Exact bytes are in the inspector.");
        ((ShaderMaterial)_selectionBox.MaterialOverride).SetShaderParameter("color", vis.SelectionColor);
        ((ShaderMaterial)_hoverBox.MaterialOverride).SetShaderParameter("color", vis.HoverColor with { A = 0.7f });
        ((ShaderMaterial)_beam.MaterialOverride).SetShaderParameter("color", vis.SelectionColor with { A = 0.35f });
        _spot.LightColor = vis.SelectionColor;

        if (_tree != null && _tree.Count > 0)
        {
            Relayout();
            if (frameSelection)
            {
                if (_selection.Selected >= 0) FlyToSelection(snap: false);
                else ResetView();
            }
        }
    }

    public void ToggleQuality()
    {
        _quality = _quality == Quality.High ? Quality.Low : Quality.High;
        SetQuality(_quality);
    }

    public void SetQuality(Quality q)
    {
        _quality = q;
        ActiveVisualization.ConfigureEnvironment(_world, _sun, _ground, _quality);
        ApplyQualityToViewport();
        _hud.SetQuality(_quality.ToString());
    }

    private void ApplyQualityToViewport()
    {
        var vp = GetViewport();
        vp.Msaa3D = _quality == Quality.High ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Disabled;
        vp.ScreenSpaceAA = _quality == Quality.High ? Viewport.ScreenSpaceAAEnum.Disabled : Viewport.ScreenSpaceAAEnum.Fxaa;
    }

    // ---------------------------------------------------------------- camera / navigation

    /// <summary>
    /// The recovery view (Home, NAV-04): the root pedestal and the first row of its children, seen from the front at
    /// FSN's oblique angle, with deeper levels receding into the haze. This is predictable, and close enough to read
    /// labels. <see cref="Overview"/> shows the whole tree instead.
    /// </summary>
    public void ResetView(bool snap = false)
    {
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
        var maxHalfWidth = 60f * ActiveVisualization.LayoutParams.FileCell;
        if (box.Size.X > maxHalfWidth * 2)
        {
            box = new Box(box.Min with { X = MathF.Max(box.Min.X, -maxHalfWidth) }, box.Max with { X = MathF.Min(box.Max.X, maxHalfWidth) });
        }

        _rig.Frame(new Aabb(Visualization.Gd(box.Min), Visualization.Gd(box.Size)), pitch: 30, yaw: 0, padding: 1.05f);
        if (snap)
        {
            _rig.Snap();
        }
    }

    /// <summary>A high bird's-eye view that fits the whole tree's width (NAV-05).</summary>
    public void Overview(bool snap = false)
    {
        if (_layout == null || _layout.Count == 0)
        {
            return;
        }

        var b = _layout.Bounds;
        _rig.Frame(new Aabb(Visualization.Gd(b.Min), Visualization.Gd(b.Size)), pitch: 62, yaw: 0, padding: 1.02f);
        if (snap)
        {
            _rig.Snap();
        }
    }

    public void FlyToSelection() => FlyToSelection(false);

    public void FlyToSelection(bool snap)
    {
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
            // Frame a file with some of its neighbourhood, so it stays in context.
            var grow = new System.Numerics.Vector3(2.5f, 0.5f, 2.5f);
            box = new Box(box.Min - grow, box.Max + grow);
        }

        // Never frame tighter than a small neighbourhood, so an empty directory or a tiny file stays in context.
        var minSize = 9f * ActiveVisualization.LayoutParams.FileCell;
        var centre = box.Center;
        var half = System.Numerics.Vector3.Max(box.Size * 0.5f, new System.Numerics.Vector3(minSize * 0.5f, 0, minSize * 0.5f));
        box = new Box(centre - half, centre + half);
        _rig.Frame(new Aabb(Visualization.Gd(box.Min), Visualization.Gd(box.Size)), pitch: Mathf.Clamp(_rig.Pitch, 25, 55), padding: 1.15f);
        if (snap)
        {
            _rig.Snap();
        }
    }

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

    public int PickAt(Vector2 screen)
    {
        if (_tree == null || _layout == null)
        {
            return -1;
        }

        var origin = _rig.Camera.ProjectRayOrigin(screen);
        var dir = _rig.Camera.ProjectRayNormal(screen);
        return Picker.Pick(_tree, _layout, Visualization.Num(origin), Visualization.Num(dir));
    }

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
            _spot.Position = top + new GVector3(0, 14, 0);
        }
    }

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
    private double _lastStateWrite;
    private bool _windowFocused = true;
    private Vector2 _flyStartMouse;
    private int _consumedEscapes;

    /// <summary>True while a modal dialog is open; scene navigation is paused and held keys are cleared.</summary>
    public bool ModalOpen => (_confirm?.Visible ?? false) || (_folderDialog?.Visible ?? false);

    public bool WindowFocused => _windowFocused;
    public bool IsDragging => _pressButton != MouseButton.None;
    public IEnumerable<string> HeldActionNames => InputBindings.MovementActions.Where(_held.IsHeld);

    public override void _Input(InputEvent @event)
    {
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

            if (mbUp.ButtonIndex == _pressButton && mbUp.ButtonIndex != MouseButton.Left)
            {
                _pressButton = MouseButton.None;
            }
        }

        if (!_rig.IsFlying)
        {
            return;
        }

        // While flying, the scene owns the mouse: motion looks, the wheel sets speed, and Escape only ends flight.
        switch (@event)
        {
            case InputEventMouseMotion motion:
                _rig.Look(motion.Relative);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                // Factor carries multi-notch and fractional (high-resolution wheel, touchpad) scrolling.
                _rig.StepFlySpeed((wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1) * WheelNotches(wheel));
                _hud.SetFlyStatus(true, _rig.FlySpeed, highlight: true);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false } esc when esc.IsAction(InputBindings.Parent):
                _consumedEscapes++;
                EndFly();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
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
                    HandleCommand(key);
                }

                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        if (mb.Pressed)
        {
            // Clicking the scene takes keyboard focus away from any UI control, so movement keys apply to the scene again.
            GetViewport().GuiReleaseFocus();
        }

        if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            if (mb.Pressed && !_rig.IsFlying) _rig.Zoom(Mathf.Pow(mb.ButtonIndex == MouseButton.WheelUp ? 0.87f : 1.15f, WheelNotches(mb)));
            return;
        }

        if (mb.Pressed && mb.IsAction(InputBindings.FlyLook))
        {
            BeginFly(mb.Position);
            return;
        }

        if (mb.Pressed)
        {
            if (mb.DoubleClick && mb.ButtonIndex == MouseButton.Left)
            {
                var hit = PickAt(mb.Position);
                if (hit >= 0) Select(hit, fly: true);
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

            _pressButton = MouseButton.None;
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mm)
    {
        _hoverDirty = true;
        if (_pressButton == MouseButton.None)
        {
            return;
        }

        if (!_dragged && mm.Position.DistanceTo(_pressPosition) > 4)
        {
            _dragged = true;
        }

        if (!_dragged)
        {
            return;
        }

        if (_pressButton == MouseButton.Left)
        {
            _rig.Orbit(-mm.Relative.X * CameraRig.OrbitYawSensitivity, -mm.Relative.Y * CameraRig.OrbitPitchSensitivity);
        }
        else if (_pressButton == MouseButton.Middle)
        {
            _rig.PanPixels(mm.Relative, GetViewport().GetVisibleRect().Size.Y);
        }
    }

    private void HandleCommand(InputEventKey key)
    {
        if (key.IsAction(InputBindings.Open)) RequestOpen();
        else if (key.IsAction(InputBindings.HistoryBack)) { if (_selection.Back()) FlyToSelection(); }
        else if (key.IsAction(InputBindings.Parent)) HandleEscape();
        else if (key.IsAction(InputBindings.Home)) ResetView();
        else if (key.IsAction(InputBindings.Overview)) Overview();
        else if (key.IsAction(InputBindings.SwitchVis)) SetVisualization((_visIndex + 1) % _visualizations.Length);
        else if (key.IsAction(InputBindings.Frame)) FlyToSelection();
        else if (key.IsAction(InputBindings.Help)) _hud.ToggleHelp();
        else if (key.IsAction(InputBindings.Quality)) ToggleQuality();
        else if (key.IsAction(InputBindings.Stats)) _hud.SetStats("", !_hud.StatsVisible);
        else if (key.IsAction(InputBindings.OpenFolder)) ShowFolderDialog();
    }

    /// <summary>Escape peels back one layer at a time: help overlay, then parent directory. Flight is handled in _Input.</summary>
    private void HandleEscape()
    {
        if (_hud.HelpVisible)
        {
            _hud.ToggleHelp();
            return;
        }

        if (_tree != null && _selection.Parent(_tree)) FlyToSelection();
    }

    /// <summary>Godot reports 0 for ordinary single-notch events from some devices, so 0 counts as one notch.</summary>
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
        _hud.SetFlyStatus(true, _rig.FlySpeed, highlight: false);
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
        _hud.SetFlyStatus(false, _rig.FlySpeed, highlight: false);
        RepivotOnView();
    }

    /// <summary>After flight, orbit around whatever is under the screen centre: the picked surface, else the ground ahead.</summary>
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
            System.FormattableString.Invariant($"{{\"mode\":\"{_rig.Mode}\",\"mouseMode\":\"{Input.MouseMode}\",\"focused\":{(_windowFocused ? "true" : "false")},\"modal\":{(ModalOpen ? "true" : "false")},\"dragging\":\"{_pressButton}\",") +
            System.FormattableString.Invariant($"\"pos\":[{p.X:0.###},{p.Y:0.###},{p.Z:0.###}],\"target\":[{t.X:0.###},{t.Y:0.###},{t.Z:0.###}],\"yaw\":{_rig.Yaw:0.###},\"lookPitch\":{_rig.LookPitch:0.###},\"up\":{up.Y:0.####},") +
            System.FormattableString.Invariant($"\"distance\":{_rig.Distance:0.###},\"flySpeed\":{_rig.FlySpeed:0.###},\"held\":[{string.Join(",", HeldActionNames.Select(h => $"\"{h}\""))}],") +
            System.FormattableString.Invariant($"\"selected\":{_selection.Selected},\"vis\":{_visIndex},\"hint\":{(_hud.HintVisible ? "true" : "false")},\"flyChip\":{(_hud.FlyChipVisible ? "true" : "false")},\"consumedEscapes\":{_consumedEscapes}}}");
    }

    private void WriteState()
    {
        try
        {
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
        _held.Clear();
        _pressButton = MouseButton.None;
        _dragged = false;
    }

    private void HandleKeyboardMovement(float dt)
    {
        // A focused UI control (or an open dialog) owns the keyboard; navigation resumes when it lets go.
        var blocked = !_windowFocused || ModalOpen || GetViewport().GuiGetFocusOwner() != null;
        if (blocked)
        {
            if (_held.Any || _rig.IsFlying || _pressButton != MouseButton.None) ReleaseAllInput();
            return;
        }

        // Lost button releases (for example a release outside the window that the OS did not deliver) end the drag or flight.
        if (_rig.IsFlying && !Input.IsMouseButtonPressed(MouseButton.Right)) EndFly();
        if (_pressButton != MouseButton.None && !Input.IsMouseButtonPressed(_pressButton)) _pressButton = MouseButton.None;

        var input = new GVector3(
            _held.Axis(InputBindings.Left, InputBindings.Right),
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
            return;
        }

        var node = _tree[_selection.Selected];
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

    public void RequestReveal()
    {
        if (_tree == null || _selection.Selected < 0)
        {
            return;
        }

        ReportShell(_shell.RevealInExplorer(_tree.FullPath(_selection.Selected)));
    }

    private void CopyPath()
    {
        if (_tree == null || _selection.Selected < 0)
        {
            return;
        }

        DisplayServer.ClipboardSet(_tree.FullPath(_selection.Selected));
        _hud.Toast("Path copied to the clipboard.");
    }

    private void ReportShell(ShellResult r)
    {
        LastShellMessage = r.Message;
        GD.Print($"[shell] succeeded={r.Succeeded} mock={r.WasMock} {r.Message}");
        _hud.Toast(r.Message, r.WasMock ? new Color(1f, 0.85f, 0.45f) : r.Succeeded ? new Color(0.6f, 1f, 0.7f) : new Color(1f, 0.5f, 0.45f), 6);
    }

    public void Confirm(string text, Action onOk)
    {
        ReleaseAllInput();
        _pendingConfirm = onOk;
        _confirm.DialogText = text;
        _confirm.PopupCentered(new Vector2I(560, 200));
    }

    // ---------------------------------------------------------------- HUD content

    private void UpdateInspector()
    {
        if (_tree == null || _tree.Count == 0 || _selection.Selected < 0)
        {
            _hud.SetInspector("Nothing selected",
                "[color=#9fb0cc]Click a pedestal or block to inspect it. Double-click to fly to it.\n\nPress [b]F1[/b] for controls, [b]Home[/b] to reset the view.[/color]",
                new Color(0, 0, 0, 0));
            _hud.SetSelectionActions(false, false, false);
            return;
        }

        var node = _tree[_selection.Selected];
        _hud.SetInspector(_tree.DisplayName(node.Id), Inspector.Describe(node, _tree, ActiveVisualization), ActiveVisualization.ColorFor(node, _tree));
        _hud.SetSelectionActions(canOpen: true, canReveal: _tree.Source.IsLive, canParent: !node.IsRoot);
    }

    private void UpdateStatus()
    {
        if (_session == null || _tree == null)
        {
            return;
        }

        var p = _session.Snapshot();
        var counts = $"{p.Nodes:N0} entries · {p.Directories:N0} dirs · {p.Files:N0} files · {p.Links:N0} links · {SizeFormat.Short(p.LogicalBytes)} logical";
        var problems = p.Problems > 0 ? $" · {p.Problems:N0} problem dirs" : "";
        var done = Math.Max(0, p.Directories - p.PendingDirectories);
        var frac = p.Directories == 0 ? 0 : done / (double)p.Directories;
        var source = _tree.Source.IsLive ? "" : "Synthetic ";
        switch (p.State)
        {
            case ScanState.Running:
                var op = p.CurrentPath != null && p.CurrentOperationDuration.TotalSeconds > 1.0
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

    private void UpdateStats(double now)
    {
        if (!_hud.StatsVisible)
        {
            return;
        }

        if (now - _lastProcessMemoryAt > 1)
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
            $"last layout {LastLayoutMs:0.0} ms · build {LastBuildMs:0.0} ms · relayout every ≥{_layoutInterval:0.00}s while scanning\n" +
            $"managed {GC.GetTotalMemory(false) / (1024 * 1024):N0} MB · process private {_processPrivateBytes / (1024 * 1024):N0} MB · video {vmem:0} MB\n" +
            $"{RenderingServer.GetVideoAdapterName()} · {RenderingServer.GetVideoAdapterApiVersion()} · {_quality} · {ActiveVisualization.Name}",
            true);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete)
        {
            _session?.Dispose();
        }
        else if (what is (int)NotificationApplicationFocusOut or (int)NotificationWMWindowFocusOut)
        {
            // Alt-Tab and similar: release capture and forget held keys, so nothing is stuck on return.
            _windowFocused = false;
            if (_rig != null) ReleaseAllInput();
        }
        else if (what is (int)NotificationApplicationFocusIn or (int)NotificationWMWindowFocusIn)
        {
            _windowFocused = true;
        }
    }
}
