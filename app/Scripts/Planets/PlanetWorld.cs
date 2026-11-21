using FileScape.App.Places;
using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// The planetary world (PLANET-10.., AD-39): every folder is a small planet you can run all the way round, its child
/// folders hang in the sky above bloom launchers, its files stand in groves by kind, and travel is a launch with a
/// visible wind-up, a brisk arc and a landing that never cuts. It runs on the shared session (<see cref="IWorldSession"/>):
/// the current folder, the selection, the history and every filesystem action belong to the session, so switching to
/// Places (V) keeps them.
/// <para>
/// What varies here, and only here: the <b>surface and gravity</b> (<see cref="PlanetSurface"/> and
/// <see cref="SphereWalker"/>: local up is away from the planet's centre, anywhere on it); the <b>camera frame</b> (a
/// chase camera whose up follows the local up with a capped roll rate); <b>placement and travel</b>
/// (<see cref="PlanetComposer"/>, launches between bodies with a floating origin and nested scale); and the <b>visual
/// and audio treatment</b> (<see cref="PlanetArt"/>, <see cref="AvatarRig"/>, <see cref="SkyAudio"/>).
/// </para>
/// Frames: the current planet's centre is the world origin (a floating origin). A child body hangs at its composed
/// sky position, drawn at a smaller scale (its preview radius over its real radius), so a whole neighbourhood fits in
/// view; its parent is drawn at the inverse of that placement (larger, behind you). A transfer flies into the target's
/// placement and, on touchdown, re-expresses everything in the target's frame, so nothing on screen jumps.
/// <para>
/// Main owns it: it calls <see cref="Activate(WorldEnvironment, DirectionalLight3D, bool)"/> and <see cref="Deactivate"/> when the session switches worlds, feeds it
/// input (<see cref="Input"/> before the GUI, <see cref="UnhandledInput"/> after) and calls <see cref="Tick"/> once a
/// frame. One class, split by concern across partial files:
/// </para>
/// <list type="bullet">
/// <item><b>PlanetWorld.cs</b> (this file): activation, the per-frame dispatch, building planets (full and preview
/// visuals, their cache, ground made on workers), where neighbours hang (<see cref="ChildPlacement"/>), the atmosphere.</item>
/// <item><b>.Walk</b>: walking, the chase camera, the flight camera, the establishing shot, hops across the planet.</item>
/// <item><b>.Travel</b>: launches (wind-up, release, flight, landing), warps, and the navigator's changes.</item>
/// <item><b>.Interact</b>: mouse and key input (drag-to-look, click on release), picking, the focus and its prompt,
/// inspect and approach, the Destinations drawer, the probe state.</item>
/// <item><b>.Overlay</b>: the screen layer (title card, prompt, hint, preview, the planet's list, the warp flash),
/// names in the sky, the travel effects.</item>
/// <item><b>.Universe</b>: the overview (T): every known folder as nested star systems, its camera and landing.
/// <b>.Discovery</b>: what the open overview lists by itself, its indicator and pause (PLANET-76..78).</item>
/// <item><b>.Bench</b>: the repository workbench (PLANET-29). <b>.Routes</b>: remembered routes. <b>.Probe</b>: what
/// probes read.</item>
/// <item><b>.Bell</b>: the bell tower and the telescope (PLANET-55/56/73); <b>.Telescope</b>: looking through it (AD-85). <b>.Whimsy</b>: the lighthouse and the
/// scenery that answers a twirl (PLANET-74/75, AD-70).</item>
/// </list>
/// Assumptions to keep: only one place change at a time (<see cref="Busy"/>); everything shown is in the current
/// planet's frame, so anything kept across a landing must be re-expressed in the new frame (<c>Land</c>); and node ids
/// belong to one tree, so a new tree resets everything (<c>ResetForTree</c>).
/// </summary>
public partial class PlanetWorld : Node3D
{
    private readonly IWorldSession _s;
    private readonly SphereWalker _walker = new();
    private Camera3D _camera = null!;
    private AvatarRig _avatar = null!;
    private SkyAudio _audio = null!;
    private readonly Dictionary<(PlanetTheme, WorldKind, int, bool), Godot.Environment> _environments = [];
    private WorldCharacter _character;
    private readonly Dictionary<int, int> _arrivals = [];

    /// <summary>The current world's character (PLANET-27).</summary>
    public WorldCharacter Character => _character;

    /// <summary>How many times a folder's world has been arrived at in this session (the music develops with it).</summary>
    public int Arrivals(int folderId) => _arrivals.GetValueOrDefault(folderId);
    private WorldEnvironment? _worldEnv;
    private DirectionalLight3D? _sun;
    private Godot.Environment? _savedEnvironment;
    private bool _active;
    private Quality _quality = Quality.High;

    /// <summary>A planet in the scene: its composition, its node, and whether it is built in full or as a preview.</summary>
    private sealed class Body
    {
        public required int FolderId;
        public required PlanetComposition Comp;
        public required Node3D Node;
        public bool Full;
        public PlanetVisual? Visual;

        /// <summary>Easels load their pictures by themselves only inside the app's own Starter Sky (FILE-02/DEV-02).</summary>
        public bool AutoThumbnails;
    }

    private Body? _current;
    private Body? _parent;
    private readonly Dictionary<int, Body> _children = [];
    private TimeSpan _lastSignatureCheck;
    private string _signature = "";

    public PlanetWorld(IWorldSession session)
    {
        _s = session;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>True while a launch (wind-up, flight or landing) is in progress: one place change at a time.</summary>
    public bool Busy => _phase != Phase.Walking || _trip != null; // a trip (PlanetWorld.Trip) is busy between its hops too

    /// <summary>Times the safety net closed the overview over a travel that had begun under it (probes: 0 when every path lands through the overview).</summary>
    public int TravelUnderOverviewClosed { get; private set; }

    public SphereWalker Walker => _walker;
    public PlanetComposition? Composition => _current?.Comp;
    public Camera3D Camera => _camera;
    public SkyAudio Audio => _audio;
    public AvatarRig Avatar => _avatar;
    public bool Active => _active;

    /// <summary>The surface camera's near clip, metres (8 cm; rationale not recorded). The far clip is
    /// <see cref="SurfaceFarMetres"/>, the field of view <see cref="WalkingFovDegrees"/>.</summary>
    private const float SurfaceNearMetres = 0.08f;

    public override void _Ready()
    {
        _camera = new Camera3D { Fov = WalkingFovDegrees, Near = SurfaceNearMetres, Far = SurfaceFarMetres, Current = false };
        AddChild(_camera);
        _avatar = new AvatarRig { Name = "Traveller" };
        AddChild(_avatar);
        _audio = new SkyAudio { Name = "SkyAudio" };
        AddChild(_audio);
        BuildOverlay();
        BuildTravelFx();
    }

    // ------------------------------------------------------------------ activation (the shared session switches worlds)

    /// <summary>
    /// Shows the planetary world at the session's current folder: back where you stood if you left it for a legacy view
    /// and nothing has moved meanwhile (PLANET-112, <see cref="SurfaceResume"/>), else arriving next to the selected file
    /// if it is here, else at the arrival with the establishing shot.
    /// </summary>
    public void Activate(WorldEnvironment world, DirectionalLight3D sun, bool frame)
    {
        _worldEnv = world;
        _sun = sun;
        _savedEnvironment = world.Environment;
        _savedShadow = (sun.ShadowBlur, sun.ShadowNormalBias, sun.DirectionalShadowMaxDistance);
        _active = true;
        Visible = true;
        _camera.Current = true;
        _audio.SetActive(true);
        _phase = Phase.Walking;
        _transfer = null;
        if (_s.Tree is { Count: > 0 } tree && !_s.AwaitingArrival)
        {
            if (!ReferenceEquals(tree, _tree)) ResetForTree(tree);
            LastResume = SurfaceResume.Decide(_resume, tree, _s.Navigator.Current, _s.Selection.Selected);
            if (LastResume == SurfaceResumeDecision.Resume && _resume is { } r)
            {
                // Rebuilt where you stand (−2: not an arrival, so no arrival count, chord or caution), then put back.
                BuildCurrent(tree, _s.Navigator.Current, arriveFrom: -2);
                Resume(r);
                ShowTitleCard(tree, _s.Navigator.Current, frame ? "Planets" : null);
                if (r.InOverview) OpenUniverse(); // you chose the legacy view from the overview: it opens again over where you stood (AD-92)
                _resume = null;
                return;
            }

            BuildCurrent(tree, _s.Navigator.Current, arriveFrom: -1);
            var beside = ArriveBesideSelection(tree);
            SnapCamera();
            if (!beside) PlayEstablishingShot();
            ShowTitleCard(tree, _s.Navigator.Current, frame ? "Planets" : null);
        }

        _resume = null;
    }

    /// <summary>Where you stood when a legacy view was shown (PLANET-112): kept by <see cref="Deactivate"/>, used once by
    /// <see cref="Activate(WorldEnvironment, DirectionalLight3D, bool)"/>.</summary>
    private SurfaceResume? _resume;

    /// <summary>What the last activation decided about resuming (probes, the log).</summary>
    public SurfaceResumeDecision LastResume { get; private set; } = SurfaceResumeDecision.NothingSaved;

    /// <summary>
    /// Keeps where you stand and how the camera looks (PLANET-112; PLANET-144, AD-92), unless you are on your way somewhere
    /// (a departure, a hop, a trip, the overview's dive). In the air, mid-fall off a Hazard World's rim, on a Hazard World
    /// and under the overview it is kept: <see cref="Resume"/> puts you on safe ground (Core's <see cref="SurfaceResume.Place"/>).
    /// A Hazard World resumed is rebuilt without an arrival (no caution card again; the route and its beads are the world's own).
    /// </summary>
    private void CaptureResume()
    {
        _resume = null;
        if (_current == null || _s.Tree is not { } tree) return;
        var travelling = _phase is not (Phase.Walking or Phase.Falling) || _trip != null || _hop != null || _transfer != null || _uniLanding != null;
        if (!SurfaceResume.MayCapture(travelling)) return;
        _resume = new SurfaceResume(tree, _current.FolderId, _s.Selection.Selected, _walker.Up, _walker.Facing, Nv(_camFwd), _camPitchDegrees, _camDistance)
        {
            ArrivalFraming = _arrival01,
            ArrivalReleased = _arrivalReleased,
            SupportMetres = _walker.Support.Value,
            InOverview = _universe != null && _uniReturn == null, // on the way back down it counts as on the surface
        };
    }

    /// <summary>Where the last resume stood you (Core's <see cref="ResumePlacement"/>: as saved, nearby, the arrival), for probes and the log.</summary>
    public ResumePlacement LastResumePlacement { get; private set; }

    /// <summary>
    /// Stands the traveller where <paramref name="r"/> says, or on the nearest safe ground if that spot is no longer safe
    /// (<see cref="SurfaceResume.Place"/>: over a gap, at a rim, where something now stands), and puts the camera back as it
    /// was (no establishing shot).
    /// </summary>
    private void Resume(SurfaceResume r)
    {
        if (_current == null) return;
        var place = SurfaceResume.Place(r, _current.Comp.Surface, _current.Comp.Arrival.Up, _walker.Params.BodyRadius.Value);
        LastResumePlacement = place;
        if (place.Stand != ResumeStand.AsSaved) GD.Print($"[planets] resumed {place.Stand} ({place.Why}): moved {place.MovedMetres:0.0} m");
        _walker.Teleport(_current.Comp.Surface, place.Up, place.Stand == ResumeStand.Arrival ? _current.Comp.Arrival.Facing : r.Facing);
        _camPitchDegrees = r.CameraPitchDegrees;
        _camDistance = r.CameraDistanceMetres;
        _arrival01 = r.ArrivalFraming;
        _arrivalReleased = r.ArrivalReleased;
        SnapCamera(Gd(r.CameraForward));
        Resumes++;
    }

    /// <summary>Activations that resumed where you stood (probes).</summary>
    public int Resumes { get; private set; }

    /// <summary>
    /// Hides the planetary world and gives back what it borrowed: the shared environment and the sun's shadow settings
    /// (saved by <see cref="Activate(WorldEnvironment, DirectionalLight3D, bool)"/>), the mouse, the music. Any travel in progress is dropped; on the next
    /// activation the world is built wherever the navigator then is — where you stood, if nothing moved (PLANET-112).
    /// </summary>
    public void Deactivate()
    {
        if (!_active) return;
        CaptureResume(); // before anything below resets the walk (PLANET-112)
        CloseUniverse(returnToSurface: false);
        _active = false;
        Visible = false;
        _camera.Current = false;
        _audio.SetActive(false);
        EndOrbit();
        ClosePreview(); // stops a sound preview and restores the music
        _contentsPanel.Visible = false;
        if (_trip != null) EndTrip("stopped"); // where it got to (PlanetWorld.Trip)
        _phase = Phase.Walking;
        _transfer = null;
        _hop = null;
        _warpFlash = 0;
        EndHazard();
        _s.Signs.Clear();
        _overlay.Visible = false;
        if (_worldEnv != null && _savedEnvironment != null) _worldEnv.Environment = _savedEnvironment;
        if (_sun != null)
        {
            _sun.Visible = true;
            (_sun.ShadowBlur, _sun.ShadowNormalBias, _sun.DirectionalShadowMaxDistance) = _savedShadow;
        }
    }

    /// <summary>The sun's shadow settings as the other worlds had them (overwritten by <see cref="Activate(WorldEnvironment, DirectionalLight3D, bool)"/> before use;
    /// the initial values only matter if Deactivate ran first, and are the flat world's).</summary>
    private (float Blur, float NormalBias, float MaxDistance) _savedShadow = (1f, 1f, 160f);

    public override void _ExitTree()
    {
        // Detached visuals in the cache are not in the tree: free them, and the shared meshes, so nothing leaks at exit.
        foreach (var (_, v) in _visualCache) v.Free();
        _visualCache.Clear();
        PlanetVisual.ClearSharedMeshes();
    }

    public void ApplyQuality(Quality q)
    {
        _quality = q;
        if (_current != null) ApplyAtmosphere(_current.Comp.Theme, _character);
    }

    // ------------------------------------------------------------------ per frame

    /// <summary>
    /// One frame, called by Main: worker results first (previews), then whichever of the overview, walking or travel is
    /// running, then the screen layer, names, thumbnails and animation. While the session waits for an arrival nothing
    /// is built (showing the wrong place would be worse than showing none).
    /// </summary>
    /// <param name="blockedKeys">Keys go elsewhere (a dialog, focus elsewhere, a GUI control with keyboard focus).</param>
    /// <param name="pointerBlocked">The window lost focus or a dialog is open: drags end. GUI keyboard focus alone does
    /// not end a drag (PLANET-25: a list's filter ended an orbit the frame it started).</param>
    public void Tick(double delta, bool blockedKeys, bool pointerBlocked)
    {
        if (!_active) return;
        _pointerBlocked = pointerBlocked;
        var dt = (float)delta;
        // PlanetWorld.Sky: the slow turn and the occasional shooting star, every frame (also while a flight waits for its
        // destination's listing, and in the overview, which shows no shooting star).
        UpdateSky(dt);
        RunDeliveries();
        _overlay.Visible = true;
        var tree = _s.Tree;
        if (tree == null || tree.Count == 0 || _s.AwaitingArrival)
        {
            SetPrompt(_s.AwaitingArrival ? "Arriving…" : "");
            return;
        }

        if (!ReferenceEquals(tree, _tree))
        {
            // A new tree (Open…, the Starter Sky, a rescan): node ids start again, so nothing built for the old one is kept.
            ResetForTree(tree);
        }

        if (_current == null || (_current.FolderId != _s.Navigator.Current && !Busy))
        {
            CloseUniverse(returnToSurface: false);
            // The folder changed while this world was not travelling (a new tree, a reset): arrive there directly.
            var first = _current == null;
            BuildCurrent(tree, _s.Navigator.Current, arriveFrom: -1);
            SnapCamera();
            if (first) PlayEstablishingShot(); // the first world of a session is introduced
            ShowTitleCard(tree, _s.Navigator.Current, null);
        }

        if (_orbiting && (pointerBlocked || !Godot.Input.IsMouseButtonPressed(_orbitButton))) EndOrbit(); // a dialog, focus loss or a lost release
        if (_leftPress != null && (pointerBlocked || !Godot.Input.IsMouseButtonPressed(MouseButton.Left)))
        {
            _leftPress = null;
            _lastLeftWasDrag = true; // dropped without a click: the next press is not the second of a double-click
        }

        if (blockedKeys || _universe != null || _phase != Phase.Walking || _hop != null) _keyLook = 0; // no turning on after the keys stop being read
        UpdateMusicExperience(dt); // AD-79: the overlook's and the overview's own music (PlanetWorld.Music.cs)
        // AD-85: the telescope's view never outlives a departure, a hop or the overview (each started elsewhere closes it here).
        if (_scope != null && (_phase != Phase.Walking || _hop != null || _universe != null)) CloseTelescopeView(glide: false);
        if (_universe != null && (_phase != Phase.Walking || _hop != null))
        {
            // PLANET-83 safety net: travel on the surface never runs under the overview (nothing ticks it), so one that has
            // begun anyway is shown, not left waiting under the map (the overview glides back down; the travel goes on).
            GD.PrintErr($"[planets] travel ({_phase}) began under the overview: closing it so the travel runs");
            CloseUniverse(returnToSurface: true);
            _s.Hud.SetPlanetsOverview(false);
            TravelUnderOverviewClosed++;
        }

        if (_universe != null)
        {
            UpdateUniverse(tree, dt, blockedKeys);
            FadeTitle(dt); // the arrival title makes way for the map (PlanetWorld.Overlay)
            return;
        }

        StepPlayDiscovery(tree); // PlanetWorld.Discovery.cs (PLANET-90): background discovery at the play pace
        MaybeRecompose(tree);
        ApplyPendingRecompose(tree);
        switch (_phase)
        {
            case Phase.Walking when _scope != null:
                UpdateTelescopeView(dt, blockedKeys); // AD-85 (PlanetWorld.Telescope.cs): the traveller stands still
                break;
            case Phase.Walking:
                UpdateWalking(dt, blockedKeys);
                break;
            default:
                UpdateTravel(dt, blockedKeys);
                break;
        }

        UpdateOverlay(dt);
        UpdateTrip(dt); // PlanetWorld.Trip
        UpdateDestinationsPanel(); // PlanetWorld.Destinations
        UpdateHazardScreen(dt);
        UpdateSigns(tree);
        UpdateThumbnails(dt);
        AnimateBeacon(dt);
        AnimateScene(dt);
    }

    /// <summary>The tree everything built here belongs to (compared by reference: a rescan is a new tree).</summary>
    private FsTree? _tree;

    /// <summary>
    /// A new tree: node ids start again, so every body, cached visual, prefetched ground, remembered route and pending
    /// recompose made for the old tree is dropped (a stale id would show or launch to the wrong folder).
    /// </summary>
    private void ResetForTree(FsTree tree)
    {
        CloseUniverse(returnToSurface: false);
        _discovery.Reset(); // ids start again (PlanetWorld.Discovery)
        _uniEverDrawn.Clear();
        _tree = tree;
        var bodies = new List<Body>();
        if (_current != null) bodies.Add(_current);
        if (_parent != null) bodies.Add(_parent);
        bodies.AddRange(_children.Values);
        foreach (var b in bodies.Distinct())
        {
            b.Visual?.QueueFree();
            b.Visual = null;
            if (GodotObject.IsInstanceValid(b.Node)) b.Node.QueueFree();
        }

        _current = null;
        _parent = null;
        _children.Clear();
        foreach (var (_, v) in _visualCache) v.QueueFree();
        _visualCache.Clear();
        _groundTasks.Clear();
        _groundOrder.Clear();
        _inSample.Clear();
        _flownRoutes.Clear();
        _pendingRecompose = null;
        _phase = Phase.Walking;
        _transfer = null;
        _hop = null;
        _signature = "";
        EndHazard();
        CloseOverlays();
    }

    /// <summary>How often the current folder's signature is compared with what is shown (rationale for 0.35 s not
    /// recorded: a growing listing is picked up within about a third of a second, and the signature is not computed every frame).</summary>
    private static readonly TimeSpan RecomposeCheckInterval = TimeSpan.FromSeconds(0.35);

    /// <summary>
    /// While background discovery is listing during play (PLANET-90), the least time between two recompositions of the
    /// world you stand on, seconds. Discovery lists your own world's subtree first, and every listing below it changes
    /// its children's sizes and so its signature: without this the planet was rebuilt every 0.35 s, each swap a long
    /// frame (deep fixture, no vsync: walking p99 9.1 ms off, 27.8 ms on; with the gap p99 5.6 ms on and off, 7 swaps of ≤ 35 ms in 30 s). With it the planet still grows with what is found,
    /// in steps; the first change after a quiet spell is still taken at once.
    /// </summary>
    private static readonly TimeSpan RecomposeGapWhileDiscovering = TimeSpan.FromSeconds(4);

    private TimeSpan? _lastRecomposeSwap;

    /// <summary>Recompositions swapped in this session and the longest swap, milliseconds (probes).</summary>
    public int RecomposeSwaps { get; private set; }
    public double RecomposeSwapMsMax { get; private set; }

    /// <summary>A listing that arrives or grows changes what the planet shows: recompose (throttled), keep where you stand.</summary>
    private void MaybeRecompose(FsTree tree)
    {
        if (_current == null || Busy) return;
        var now = _s.Now;
        if (now - _lastSignatureCheck < RecomposeCheckInterval) return;
        // Background discovery at work: recompose at most every RecomposeGapWhileDiscovering (see there).
        if (PlayDiscoveryEnabled && _discovery.State == OverviewDiscoveryState.Discovering && _lastRecomposeSwap is { } lastSwap && now - lastSwap < RecomposeGapWhileDiscovering) return;
        _lastSignatureCheck = now;
        var sig = PlanetComposer.Signature(tree, _current.FolderId);
        if (sig == _signature) return;
        // The new planet's ground is made on a worker; the swap happens when it is ready (no hitch while you walk).
        _signature = sig;
        var comp = Compose(tree, _current.FolderId);
        _pendingRecompose = (comp, GroundAsync(comp, full: true));
    }

    /// <summary>A recomposition waiting for its ground mesh (made on a worker); swapped in by <see cref="ApplyPendingRecompose"/>.</summary>
    private (PlanetComposition Comp, Task<PlanetArt.GroundArrays> Ground)? _pendingRecompose;

    /// <summary>Something the signature does not see changed (hidden entries shown or not): compose again.</summary>
    public void Refresh()
    {
        _signature = "";
        _lastSignatureCheck = TimeSpan.Zero;
    }

    /// <summary>
    /// Swaps in a recomposed planet once its ground is ready, keeping the walker where it is and its motion (never during
    /// travel; dropped if you have left that folder meanwhile).
    /// </summary>
    private void ApplyPendingRecompose(FsTree tree)
    {
        if (_pendingRecompose is not { } pending || _current == null || Busy) return;
        if (!pending.Ground.IsCompleted) return;
        _pendingRecompose = null;
        if (pending.Comp.FolderId != _current.FolderId) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        BuildCurrent(tree, _current.FolderId, arriveFrom: -2, comp: pending.Comp);
        _walker.ChangeSurface(_current.Comp.Surface); // keeps speed and any jump in progress
        UpdateDestinations();
        RecomposeSwaps++;
        RecomposeSwapMsMax = Math.Max(RecomposeSwapMsMax, sw.Elapsed.TotalMilliseconds);
        _lastRecomposeSwap = _s.Now;
    }

    // ------------------------------------------------------------------ building planets

    /// <summary>The composer's parameters: the defaults, with this PC's picture folders for a gallery's character
    /// (PLANET-130; asked of Windows once, paths only; none for synthetic data).</summary>
    private PlanetParams ComposeParams => _composeParams ??= PlanetParams.Default with { Libraries = _s.IsLive ? PictureLibraries.FromSystem() : PictureLibraries.Empty };

    private PlanetParams? _composeParams;

    private PlanetComposition Compose(FsTree tree, int folderId) =>
        PlanetComposer.Compose(tree, folderId, ComposeParams, _s.IsVisible,
            // A probe's root look on the current folder stands that root's landmark too (PLANET-45).
            rootMarkKind: ProbeCharacter is { Kind: WorldKind.WindowsRoot or WorldKind.WslRoot } forced && folderId == _current?.FolderId ? forced.Kind
            : ProbeRootLook is { } look && folderId == _s.Navigator.Root ? look : null);

    /// <summary>
    /// Makes <paramref name="folderId"/> the current planet at the origin with its children and parent around it.
    /// <paramref name="arriveFrom"/>: −1 the default arrival, −2 keep the walker (a recompose), else the folder you came from.
    /// <paramref name="reuse"/> is a body already built for this folder (the one you just landed on); <paramref name="keep"/>
    /// the planet you left, kept in full as the new parent or child (its placement is the same transform it already has).
    /// </summary>
    private void BuildCurrent(FsTree tree, int folderId, int arriveFrom, Body? reuse = null, Body? keep = null, PlanetComposition? comp = null)
    {
        comp ??= reuse?.Comp ?? Compose(tree, folderId);
        _pendingRecompose = null;
        _signature = comp.Signature; // what is shown (a body composed at launch may be older than the tree at landing)
        var old = new List<Body>();
        if (_current != null) old.Add(_current);
        if (_parent != null) old.Add(_parent);
        old.AddRange(_children.Values);
        foreach (var b in old.Distinct())
        {
            if (b != reuse && b != keep) FreeBody(b);
        }

        _children.Clear();
        _parent = null;
        if (reuse is { Full: true })
        {
            _current = reuse;
        }
        else
        {
            if (reuse != null) FreeBody(reuse);
            _current = new Body { FolderId = folderId, Comp = comp, Node = new Node3D { Name = $"Planet-{folderId}" } };
            AddChild(_current.Node);
            BuildFull(tree, _current);
        }

        _current.Node.Transform = Transform3D.Identity;
        BuildNeighbours(tree, keep);
        UpdateRouteBeads();
        var character = CharacterOf(folderId);
        ApplyAtmosphere(comp.Theme, character);
        if (arriveFrom != -2) _arrivals[folderId] = _arrivals.GetValueOrDefault(folderId) + 1;
        _audio.SetWorld(comp.Theme, character, Math.Max(1, Arrivals(folderId))); // before Arrive(): the arrival chord sounds in the new key (AD-49)
        if (arriveFrom != -2)
        {
            var (up, facing) = arriveFrom >= 0 ? comp.ArrivalFor(arriveFrom) : comp.Arrival;
            _walker.Teleport(comp.Surface, up, facing);
        }

        UpdateDestinations();
        ResetBench(tree, recompose: arriveFrom == -2);
        OnHazardWorldBuilt(arriveFrom);
    }

    /// <summary>Built planet visuals kept for a while after you leave (going back and forth reuses them; LRU, keyed by what they show).</summary>
    private readonly List<(string Key, PlanetVisual Visual)> _visualCache = [];

    /// <summary>Detached visuals kept at most; the oldest is freed first (rationale for 10 not recorded).</summary>
    private const int VisualCacheSize = 10;

    /// <summary>Main-thread time of the last visual build that was not a cache hit, milliseconds (probes, the build log).</summary>
    public double LastBuildMs { get; private set; }

    /// <summary>The ground mesh of a preview (a world hanging in the sky): subdivision level 3, small enough to make inline.</summary>
    private const int PreviewSubdivisions = 3;

    /// <summary>Prefetched ground meshes kept at most, oldest dropped first (a dropped one is simply made again when
    /// needed; rationale for 40 not recorded).</summary>
    private const int GroundTaskCacheSize = 40;

    /// <summary>A visual's identity: the folder, what it shows (its signature), full or preview, and its character, so a
    /// cached visual is reused only if it would look exactly the same.</summary>
    private string VisualKey(Body b, bool full) => $"{b.FolderId}:{b.Comp.Signature}:{(full ? 1 : 0)}:{CharacterOf(b.FolderId)}";

    /// <summary>The character of a folder's world (PLANET-27): what kind of place it is, from the tree and where you started.</summary>
    public WorldCharacter CharacterOf(int folderId) =>
        ProbeCharacter is { } forced && folderId == _current?.FolderId ? forced
        : ProbeRootLook is { } look && folderId == _s.Navigator.Root ? new WorldCharacter(look, 0, 0, -1)
        : _s.Tree is { } t && t.Contains(folderId) ? WorldCharacter.Of(t, folderId, _s.Navigator.Root) : new WorldCharacter(WorldKind.Folder, 1, 1, -1);

    /// <summary>Probes only: show the current world with this character (to photograph a Windows volume without entering one).</summary>
    public WorldCharacter? ProbeCharacter { get; set; }

    /// <summary>
    /// Instrument (<c>--test-root-look</c>, PLANET-119): the launch folder (the navigator's root) is shown with this root's
    /// look and landmark wherever you come back to it, so a real-input probe can use a root's location actions on a fixture.
    /// </summary>
    public WorldKind? ProbeRootLook { get; set; }

    /// <summary>
    /// A visual for <paramref name="body"/>: from the cache if one shows exactly this, else built now on the main thread
    /// (a full build waits for its ground mesh if the worker has not finished; the log says whether it waited).
    /// </summary>
    private PlanetVisual TakeOrBuild(FsTree tree, Body body, bool full)
    {
        var key = VisualKey(body, full);
        var i = _visualCache.FindIndex(e => e.Key == key);
        if (i >= 0)
        {
            var v = _visualCache[i].Visual;
            _visualCache.RemoveAt(i);
            return v;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        // A preview's ground is small (subdivision 3): made right here rather than queued behind full-size prefetches.
        var ground = full ? GroundAsync(body.Comp, full) : null;
        var waited = ground == null ? "inline" : ground.IsCompleted ? "ready" : "waited";
        var arrays = ground?.Result ?? PlanetArt.PlanetMeshArrays(body.Comp.Shape, PlanetArt.Palette(body.Comp.Theme),
            body.Comp.Surface.Shell != null ? PlanetArt.HazardPreviewSubdivisions : PreviewSubdivisions, body.Comp.Surface.Shell);
        var built = new PlanetVisual(tree, body.Comp, _quality, full, arrays, CharacterOf(body.FolderId)) { Name = key, CacheKey = key };
        LastBuildMs = sw.Elapsed.TotalMilliseconds;
        if (full) GD.Print($"[planets] built {tree.DisplayName(body.FolderId)} in full: {LastBuildMs:0.0} ms on the main thread (ground {waited}; {built.BuildTimes}){(built.HazardStats.Length > 0 ? $"; Hazard World ground {arrays.Indices.Length / 3} triangles, {built.HazardStats}" : "")}");
        return built;
    }

    /// <summary>Ground vertex data made on worker threads, by planet and what it shows (prefetched for every world in the sky).</summary>
    private readonly Dictionary<string, Task<PlanetArt.GroundArrays>> _groundTasks = [];
    private readonly Queue<string> _groundOrder = new();

    /// <summary>
    /// The ground mesh data for a composition, made on a worker thread (started once per folder, signature and detail;
    /// later calls return the same task). The worker only reads the shape and palette captured here, never the scene.
    /// </summary>
    private Task<PlanetArt.GroundArrays> GroundAsync(PlanetComposition comp, bool full)
    {
        var key = $"{comp.FolderId}:{comp.Signature}:{(full ? 1 : 0)}";
        if (_groundTasks.TryGetValue(key, out var task)) return task;
        var shape = comp.Shape;
        var palette = PlanetArt.Palette(comp.Theme);
        var shell = comp.Surface.Shell; // immutable: safe to read on the worker
        var subdivisions = shell != null ? (full ? PlanetArt.HazardSubdivisions : PlanetArt.HazardPreviewSubdivisions) : full ? PlanetArt.SubdivisionsFor(shape.Radius) : PreviewSubdivisions;
        task = Task.Run(() => PlanetArt.PlanetMeshArrays(shape, palette, subdivisions, shell));
        _groundTasks[key] = task;
        _groundOrder.Enqueue(key);
        while (_groundOrder.Count > GroundTaskCacheSize) _groundTasks.Remove(_groundOrder.Dequeue());
        return task;
    }

    /// <summary>Gives a body its full visual (objects, launchers, landmark), replacing a preview if it had one.</summary>
    private void BuildFull(FsTree tree, Body body)
    {
        if (body.Full && body.Visual != null) return;
        if (body.Visual != null) Release(body);
        body.Visual = TakeOrBuild(tree, body, full: true);
        body.Node.AddChild(body.Visual);
        body.Full = true;
        // The app's own sample, or a gallery world (PLANET-130: by its marker, its contents, a picture library such as a
        // redirected Pictures folder, or standing inside a gallery; DJ allowed pictures to be read as you walk there).
        body.AutoThumbnails = InStarterSky(tree, body.FolderId) || (_s.IsLive && body.Comp.Media.IsGallery);
    }

    /// <summary>Gives a body its preview visual (how a world looks hanging in the sky).</summary>
    private void BuildPreview(FsTree tree, Body body)
    {
        body.Visual = TakeOrBuild(tree, body, full: false);
        body.Node.AddChild(body.Visual);
        body.Full = false;
    }

    /// <summary>Detaches a body's visual into the cache (or frees it when the cache is full).</summary>
    private void Release(Body b)
    {
        if (b.Visual == null || !GodotObject.IsInstanceValid(b.Visual)) return;
        b.Visual.GetParent()?.RemoveChild(b.Visual);
        _visualCache.Add((b.Visual.CacheKey.Length > 0 ? b.Visual.CacheKey : VisualKey(b, b.Full), b.Visual));
        while (_visualCache.Count > VisualCacheSize)
        {
            _visualCache[0].Visual.QueueFree();
            _visualCache.RemoveAt(0);
        }

        b.Visual = null;
    }

    /// <summary>Removes a body from the scene; its visual goes to the cache first.</summary>
    private void FreeBody(Body b)
    {
        Release(b);
        if (GodotObject.IsInstanceValid(b.Node)) b.Node.QueueFree();
    }

    /// <summary>Folder id → inside the Starter Sky (an answer may cost a check on disk, so it is remembered until the tree changes).</summary>
    private readonly Dictionary<int, bool> _inSample = [];

    /// <summary>True when the folder is inside the app's own Starter Sky (a marked folder FileScape wrote itself).</summary>
    private bool InStarterSky(FsTree tree, int folderId)
    {
        if (!_s.IsLive) return false;
        if (_inSample.TryGetValue(folderId, out var known)) return known;
        var result = false;
        for (var id = folderId; id >= 0 && tree.Contains(id); id = tree[id].ParentId)
        {
            if (tree[id].Name != FileScape.Core.Samples.SampleSky.RootName) continue;
            try { result = FileScape.Core.Samples.SampleSky.IsSample(tree.FullPath(id)); }
            catch (Exception) { result = false; }
            break;
        }

        return _inSample[folderId] = result;
    }

    /// <summary>
    /// Standing on a launcher pad, the world it leads to is built in full ahead of the launch (the cost is paid while you
    /// stand, not in the release frame), so the flight starts smoothly.
    /// </summary>
    private void Prepare(int targetId)
    {
        if (_s.Tree is not { } tree) return;
        if (_children.TryGetValue(targetId, out var b) && !b.Full) BuildFull(tree, b);
        else if (_parent != null && _parent.FolderId == targetId && !_parent.Full) BuildFull(tree, _parent);
    }

    /// <summary>Children as previews at their sky placements; the parent at the inverse of this planet's placement in it.</summary>
    private void BuildNeighbours(FsTree tree, Body? keep)
    {
        if (_current == null) return;
        foreach (var sky in _current.Comp.SkyBodies)
        {
            if (sky.InBelt || sky.IsParent || sky.TargetId < 0 || !tree.Contains(sky.TargetId)) continue;
            if (!tree[sky.TargetId].IsDirectory) continue;
            Body body;
            if (keep != null && keep.FolderId == sky.TargetId)
            {
                body = keep; // the planet you came up from, still in full detail, where it already is
            }
            else
            {
                body = new Body { FolderId = sky.TargetId, Comp = Compose(tree, sky.TargetId), Node = new Node3D { Name = $"Child-{sky.TargetId}" } };
                AddChild(body.Node);
                GroundAsync(body.Comp, full: true); // prefetched: ready by the time you stand on its launcher
                BuildPreview(tree, body);
            }

            body.Node.Transform = ChildPlacement(_current.Comp, sky, body.Comp);
            _children[sky.TargetId] = body;
        }

        var parentId = tree[_current.FolderId].ParentId;
        if (parentId >= 0 && tree.Contains(parentId) && tree[parentId].IsDirectory)
        {
            Body body;
            if (keep != null && keep.FolderId == parentId)
            {
                body = keep; // the planet you came down from, in full detail
            }
            else
            {
                body = new Body { FolderId = parentId, Comp = Compose(tree, parentId), Node = new Node3D { Name = $"Parent-{parentId}" } };
                AddChild(body.Node);
                GroundAsync(body.Comp, full: true);
                BuildPreview(tree, body);
            }

            var pcomp = body.Comp;
            var mine = pcomp.SkyBodies.FirstOrDefault(k => k.TargetId == _current.FolderId && !k.InBelt);
            body.Node.Transform = mine != null
                ? ChildPlacement(pcomp, mine, _current.Comp).AffineInverse()
                : DefaultParentPlacement(_current.Comp, pcomp);
            _parent = body;
        }

        BuildDistantWorlds(tree, parentId);
    }

    /// <summary>
    /// Where a child planet hangs in its parent's frame: at its composed sky position, at the scale that makes its real
    /// radius look like its preview radius, turned so its arrival meadow faces back towards the parent at 45° (so on
    /// arrival the parent hangs above the horizon behind you, not straight overhead).
    /// </summary>
    public static Transform3D ChildPlacement(PlanetComposition parent, SkyBody sky, PlanetComposition child)
    {
        var c = Gd(sky.Position);
        var toParent = (-c).Normalized();
        var (t, _) = Tangents(toParent);
        var arrivalUp = (toParent + t).Normalized(); // toParent and t are perpendicular unit vectors: their sum is 45° from each
        // The rest of the basis is fixed by those two directions alone, so the same two compositions always give the same
        // placement: going down and coming back up agree (AD-41). Keep it a pure function of its arguments.
        var back = (toParent - arrivalUp * toParent.Dot(arrivalUp)).Normalized();
        var x = arrivalUp.Cross(back).Normalized();
        var basis = new Basis(x, arrivalUp, back); // the child's +Y = its arrival meadow, +Z = behind you on arrival
        // Drawn scale = preview radius ÷ real radius, floored at 1/50 so a very large child never shrinks to nothing (and the
        // real radius at 1 m, so a degenerate shape cannot divide by zero). Rationale for 0.02 not recorded.
        var scale = MathF.Max(0.02f, sky.Radius / MathF.Max(1f, child.Shape.Radius));
        return new Transform3D(basis.Scaled(new Godot.Vector3(scale, scale, scale)), c);
    }

    /// <summary>
    /// A parent we have no sky position for (it does not list us yet): large and behind the arrival meadow. The values
    /// are a stand-in chosen by eye (rationale not recorded); once the parent lists this folder,
    /// <see cref="ChildPlacement"/> takes over.
    /// </summary>
    private static Transform3D DefaultParentPlacement(PlanetComposition current, PlanetComposition parent)
    {
        // Behind you as you arrive and a little up: 0.55 up per 1 back is ≈ 29° above the meadow's horizon.
        var dir = Gd(NVector3.Normalize(current.Arrival.Up * 0.55f - current.Arrival.Facing)) ;
        var scale = 2.2f; // drawn at 2.2 × its real size: it looms
        var dist = current.Shape.Radius + parent.Shape.Radius * scale + 60f; // surfaces 60 m apart, whatever the radii
        return new Transform3D(Basis.Identity.Scaled(new Godot.Vector3(scale, scale, scale)), dir * dist);
    }

    /// <summary>Two unit tangents perpendicular to <paramref name="up"/> (and to each other), the same for the same input.</summary>
    private static (Godot.Vector3 A, Godot.Vector3 B) Tangents(Godot.Vector3 up)
    {
        // A cross product with an axis nearly parallel to up is tiny and its direction unstable, so switch reference axis
        // when up is within ≈ 26° of ±Y (|cos| ≥ 0.9). The switch decides where children hang: keep it as it is.
        var refAxis = MathF.Abs(up.Y) < 0.9f ? Godot.Vector3.Up : Godot.Vector3.Right;
        var a = refAxis.Cross(up).Normalized();
        return (a, up.Cross(a));
    }

    /// <summary>The arrival when switching worlds: next to the selected file or folder if it is on this planet.</summary>
    private bool ArriveBesideSelection(FsTree tree)
    {
        if (_current == null) return false;
        var sel = _s.Selection.Selected;
        if (sel < 0 || !tree.Contains(sel)) return false;
        var item = _current.Comp.Items.FirstOrDefault(i => i.NodeId == sel);
        if (item != null)
        {
            StandFacing(item.Direction, item.Scale);
            return true;
        }

        var launcher = _current.Comp.Launchers.FirstOrDefault(l => l.TargetId == sel);
        if (launcher != null)
        {
            // Sized as a 1.4 m object: you stand ≈ 2.4 m from the pad's centre, just off the pad (on it, Space would launch).
            StandFacing(launcher.Direction, 1.4f);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Stands the traveller a short walk from a point on the surface, facing it: 1.6 m plus 0.6 × <paramref name="size"/>
    /// away along the ground (so a bigger object gets more room), on the side towards the arrival meadow.
    /// </summary>
    private void StandFacing(NVector3 target, float size)
    {
        if (_current == null) return;
        var surf = _current.Comp.Surface;
        var up = target;
        var (a, _) = PlanetShape.Tangents(up);
        // Step back along the line from the arrival meadow, so you face it with the meadow behind you.
        var away = _current.Comp.Arrival.Up - up * NVector3.Dot(_current.Comp.Arrival.Up, up);
        away = away.LengthSquared() > 1e-6f ? NVector3.Normalize(away) : a;
        var standOff = (1.6f + size * 0.6f) / surf.Radius; // metres along the ground ÷ radius = the angle, radians
        var stand = NVector3.Normalize(up * MathF.Cos(standOff) + away * MathF.Sin(standOff));
        var facing = NVector3.Normalize(target - stand * NVector3.Dot(target, stand));
        _walker.Teleport(surf, stand, facing);
    }

    // ------------------------------------------------------------------ atmosphere

    // The sun on a planet (by eye; rationale not recorded unless stated). Every planet gets the same sun direction and
    // strength; only its colour varies, half the theme's and half the character's.
    private const float SunEnergy = 1.15f;
    private static readonly Godot.Vector3 SunRotationDegrees = new(-38, 35, 0);
    private const float SunColourCharacterShare = 0.5f;

    /// <summary>Shadow range, metres: a small world needs a short, fine range (the flat world's 160 m made blocky
    /// shadows on the ground).</summary>
    private const float PlanetShadowMaxDistanceMetres = 48f;
    private const float PlanetShadowBlur = 1.6f, PlanetShadowNormalBias = 1.4f;

    /// <summary>
    /// The sky and the sun for a world: the environment for its (theme, character) pair, made once and kept, and the
    /// shared sun recoloured and given the planets' shadow settings (<see cref="Deactivate"/> restores the others').
    /// Also records the character as the current one.
    /// </summary>
    private void ApplyAtmosphere(PlanetTheme theme, WorldCharacter character = default)
    {
        if (_worldEnv == null) return;
        var pal = PlanetArt.Palette(theme);
        var key = (theme, character.Kind, character.Band, character.InsideRepository);
        if (!_environments.TryGetValue(key, out var env))
        {
            _environments[key] = env = PlanetArt.SkyEnvironment(pal, character);
        }

        _character = character;

        _worldEnv.Environment = env;
        if (_sun != null)
        {
            _sun.Visible = true;
            _sun.LightColor = pal.Sun.Lerp(PlanetArt.Look(character).Sun, SunColourCharacterShare);
            _sun.LightEnergy = SunEnergy;
            _sun.RotationDegrees = SunRotationDegrees;
            _sun.ShadowEnabled = _quality == Quality.High;
            _sun.DirectionalShadowMaxDistance = PlanetShadowMaxDistanceMetres;
            _sun.ShadowBlur = PlanetShadowBlur;
            _sun.ShadowNormalBias = PlanetShadowNormalBias;
        }
    }

    /// <summary>Core's <see cref="NVector3"/> (System.Numerics) as Godot's vector: same axes, same values.</summary>
    public static Godot.Vector3 Gd(NVector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Godot's vector as Core's <see cref="NVector3"/>.</summary>
    public static NVector3 Nv(Godot.Vector3 v) => new(v.X, v.Y, v.Z);
}
