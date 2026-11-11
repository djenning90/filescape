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
/// </summary>
public partial class PlanetWorld : Node3D
{
    private readonly IWorldSession _s;
    private readonly SphereWalker _walker = new();
    private Camera3D _camera = null!;
    private AvatarRig _avatar = null!;
    private SkyAudio _audio = null!;
    private readonly Dictionary<PlanetTheme, Godot.Environment> _environments = [];
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
    public bool Busy => _phase != Phase.Walking;

    public SphereWalker Walker => _walker;
    public PlanetComposition? Composition => _current?.Comp;
    public Camera3D Camera => _camera;
    public SkyAudio Audio => _audio;
    public AvatarRig Avatar => _avatar;
    public bool Active => _active;

    public override void _Ready()
    {
        _camera = new Camera3D { Fov = 62, Near = 0.08f, Far = 4000, Current = false };
        AddChild(_camera);
        _avatar = new AvatarRig { Name = "Traveller" };
        AddChild(_avatar);
        _audio = new SkyAudio { Name = "SkyAudio" };
        AddChild(_audio);
        BuildOverlay();
        BuildTravelFx();
    }

    // ------------------------------------------------------------------ activation (the shared session switches worlds)

    /// <summary>Shows the planetary world at the session's current folder (arriving next to the selected file if it is here).</summary>
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
            BuildCurrent(tree, _s.Navigator.Current, arriveFrom: -1);
            var beside = ArriveBesideSelection(tree);
            SnapCamera();
            if (!beside) PlayEstablishingShot();
            ShowTitleCard(tree, _s.Navigator.Current, frame ? "Planets" : null);
        }
    }

    public void Deactivate()
    {
        if (!_active) return;
        _active = false;
        Visible = false;
        _camera.Current = false;
        _audio.SetActive(false);
        EndOrbit();
        ClosePreview(); // stops a sound preview and restores the music
        _contentsPanel.Visible = false;
        _phase = Phase.Walking;
        _transfer = null;
        _hop = null;
        _warpFlash = 0;
        _s.Signs.Clear();
        _overlay.Visible = false;
        if (_worldEnv != null && _savedEnvironment != null) _worldEnv.Environment = _savedEnvironment;
        if (_sun != null)
        {
            _sun.Visible = true;
            (_sun.ShadowBlur, _sun.ShadowNormalBias, _sun.DirectionalShadowMaxDistance) = _savedShadow;
        }
    }

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
        if (_current != null) ApplyAtmosphere(_current.Comp.Theme);
    }

    // ------------------------------------------------------------------ per frame

    public void Tick(double delta, bool blockedKeys)
    {
        if (!_active) return;
        var dt = (float)delta;
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
            // The folder changed while this world was not travelling (a new tree, a reset): arrive there directly.
            var first = _current == null;
            BuildCurrent(tree, _s.Navigator.Current, arriveFrom: -1);
            SnapCamera();
            if (first) PlayEstablishingShot(); // the first world of a session is introduced
            ShowTitleCard(tree, _s.Navigator.Current, null);
        }

        if (_orbiting && (blockedKeys || !Godot.Input.IsMouseButtonPressed(MouseButton.Right))) EndOrbit(); // a dialog, focus loss or a lost release
        MaybeRecompose(tree);
        ApplyPendingRecompose(tree);
        switch (_phase)
        {
            case Phase.Walking:
                UpdateWalking(dt, blockedKeys);
                break;
            default:
                UpdateTravel(dt, blockedKeys);
                break;
        }

        UpdateOverlay(dt);
        UpdateSigns(tree);
        UpdateThumbnails();
        AnimateScene(dt);
    }

    private FsTree? _tree;

    private void ResetForTree(FsTree tree)
    {
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
        CloseOverlays();
    }

    /// <summary>A listing that arrives or grows changes what the planet shows: recompose (throttled), keep where you stand.</summary>
    private void MaybeRecompose(FsTree tree)
    {
        if (_current == null || Busy) return;
        var now = _s.Now;
        if (now - _lastSignatureCheck < TimeSpan.FromSeconds(0.35)) return;
        _lastSignatureCheck = now;
        var sig = PlanetComposer.Signature(tree, _current.FolderId);
        if (sig == _signature) return;
        // The new planet's ground is made on a worker; the swap happens when it is ready (no hitch while you walk).
        _signature = sig;
        var comp = Compose(tree, _current.FolderId);
        _pendingRecompose = (comp, GroundAsync(comp, full: true));
    }

    private (PlanetComposition Comp, Task<PlanetArt.GroundArrays> Ground)? _pendingRecompose;

    /// <summary>Something the signature does not see changed (hidden entries shown or not): compose again.</summary>
    public void Refresh()
    {
        _signature = "";
        _lastSignatureCheck = TimeSpan.Zero;
    }

    private void ApplyPendingRecompose(FsTree tree)
    {
        if (_pendingRecompose is not { } pending || _current == null || Busy) return;
        if (!pending.Ground.IsCompleted) return;
        _pendingRecompose = null;
        if (pending.Comp.FolderId != _current.FolderId) return;
        BuildCurrent(tree, _current.FolderId, arriveFrom: -2, comp: pending.Comp);
        _walker.ChangeSurface(_current.Comp.Surface); // keeps speed and any jump in progress
        UpdateDestinations();
    }

    // ------------------------------------------------------------------ building planets

    private PlanetComposition Compose(FsTree tree, int folderId) =>
        PlanetComposer.Compose(tree, folderId, PlanetParams.Default, _s.IsVisible);

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
        ApplyAtmosphere(comp.Theme);
        _audio.SetTheme(comp.Theme);
        if (arriveFrom != -2)
        {
            var (up, facing) = arriveFrom >= 0 ? comp.ArrivalFor(arriveFrom) : comp.Arrival;
            _walker.Teleport(comp.Surface, up, facing);
        }

        UpdateDestinations();
    }

    /// <summary>Built planet visuals kept for a while after you leave (going back and forth reuses them; LRU, keyed by what they show).</summary>
    private readonly List<(string Key, PlanetVisual Visual)> _visualCache = [];
    private const int VisualCacheSize = 10;

    public double LastBuildMs { get; private set; }

    private static string VisualKey(Body b, bool full) => $"{b.FolderId}:{b.Comp.Signature}:{(full ? 1 : 0)}";

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
        var arrays = ground?.Result ?? PlanetArt.PlanetMeshArrays(body.Comp.Shape, PlanetArt.Palette(body.Comp.Theme), 3);
        var built = new PlanetVisual(tree, body.Comp, _quality, full, arrays) { Name = key };
        LastBuildMs = sw.Elapsed.TotalMilliseconds;
        if (full) GD.Print($"[planets] built {tree.DisplayName(body.FolderId)} in full: {LastBuildMs:0.0} ms on the main thread (ground {waited}; {built.BuildTimes})");
        return built;
    }

    /// <summary>Ground vertex data made on worker threads, by planet and what it shows (prefetched for every world in the sky).</summary>
    private readonly Dictionary<string, Task<PlanetArt.GroundArrays>> _groundTasks = [];
    private readonly Queue<string> _groundOrder = new();

    private Task<PlanetArt.GroundArrays> GroundAsync(PlanetComposition comp, bool full)
    {
        var key = $"{comp.FolderId}:{comp.Signature}:{(full ? 1 : 0)}";
        if (_groundTasks.TryGetValue(key, out var task)) return task;
        var shape = comp.Shape;
        var palette = PlanetArt.Palette(comp.Theme);
        var subdivisions = full ? PlanetArt.SubdivisionsFor(shape.Radius) : 3;
        task = Task.Run(() => PlanetArt.PlanetMeshArrays(shape, palette, subdivisions));
        _groundTasks[key] = task;
        _groundOrder.Enqueue(key);
        while (_groundOrder.Count > 40) _groundTasks.Remove(_groundOrder.Dequeue());
        return task;
    }

    private void BuildFull(FsTree tree, Body body)
    {
        if (body.Full && body.Visual != null) return;
        if (body.Visual != null) Release(body);
        body.Visual = TakeOrBuild(tree, body, full: true);
        body.Node.AddChild(body.Visual);
        body.Full = true;
        body.AutoThumbnails = InStarterSky(tree, body.FolderId);
    }

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
        _visualCache.Add((VisualKey(b, b.Full), b.Visual));
        while (_visualCache.Count > VisualCacheSize)
        {
            _visualCache[0].Visual.QueueFree();
            _visualCache.RemoveAt(0);
        }

        b.Visual = null;
    }

    private void FreeBody(Body b)
    {
        Release(b);
        if (GodotObject.IsInstanceValid(b.Node)) b.Node.QueueFree();
    }

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
        var arrivalUp = (toParent + t).Normalized();
        var back = (toParent - arrivalUp * toParent.Dot(arrivalUp)).Normalized();
        var x = arrivalUp.Cross(back).Normalized();
        var basis = new Basis(x, arrivalUp, back); // the child's +Y = its arrival meadow, +Z = behind you on arrival
        var scale = MathF.Max(0.02f, sky.Radius / MathF.Max(1f, child.Shape.Radius));
        return new Transform3D(basis.Scaled(new Godot.Vector3(scale, scale, scale)), c);
    }

    /// <summary>A parent we have no sky position for (it does not list us yet): large and behind the arrival meadow.</summary>
    private static Transform3D DefaultParentPlacement(PlanetComposition current, PlanetComposition parent)
    {
        var dir = Gd(NVector3.Normalize(current.Arrival.Up * 0.55f - current.Arrival.Facing)) ;
        var scale = 2.2f;
        var dist = current.Shape.Radius + parent.Shape.Radius * scale + 60f;
        return new Transform3D(Basis.Identity.Scaled(new Godot.Vector3(scale, scale, scale)), dir * dist);
    }

    private static (Godot.Vector3 A, Godot.Vector3 B) Tangents(Godot.Vector3 up)
    {
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
            StandFacing(launcher.Direction, 1.4f);
            return true;
        }

        return false;
    }

    /// <summary>Stands the traveller a short walk from a point on the surface, facing it.</summary>
    private void StandFacing(NVector3 target, float size)
    {
        if (_current == null) return;
        var surf = _current.Comp.Surface;
        var up = target;
        var (a, _) = PlanetShape.Tangents(up);
        // Step back along the line from the arrival meadow, so you face it with the meadow behind you.
        var away = _current.Comp.Arrival.Up - up * NVector3.Dot(_current.Comp.Arrival.Up, up);
        away = away.LengthSquared() > 1e-6f ? NVector3.Normalize(away) : a;
        var standOff = (1.6f + size * 0.6f) / surf.Radius;
        var stand = NVector3.Normalize(up * MathF.Cos(standOff) + away * MathF.Sin(standOff));
        var facing = NVector3.Normalize(target - stand * NVector3.Dot(target, stand));
        _walker.Teleport(surf, stand, facing);
    }

    // ------------------------------------------------------------------ atmosphere

    private void ApplyAtmosphere(PlanetTheme theme)
    {
        if (_worldEnv == null) return;
        var pal = PlanetArt.Palette(theme);
        if (!_environments.TryGetValue(theme, out var env))
        {
            _environments[theme] = env = PlanetArt.SkyEnvironment(pal);
        }

        _worldEnv.Environment = env;
        if (_sun != null)
        {
            _sun.Visible = true;
            _sun.LightColor = pal.Sun;
            _sun.LightEnergy = 1.15f;
            _sun.RotationDegrees = new Godot.Vector3(-38, 35, 0);
            _sun.ShadowEnabled = _quality == Quality.High;
            // A small world needs a short, fine shadow range (the flat world's 160 m made blocky shadows on the ground).
            _sun.DirectionalShadowMaxDistance = 48;
            _sun.ShadowBlur = 1.6f;
            _sun.ShadowNormalBias = 1.4f;
        }
    }

    public static Godot.Vector3 Gd(NVector3 v) => new(v.X, v.Y, v.Z);
    public static NVector3 Nv(Godot.Vector3 v) => new(v.X, v.Y, v.Z);
}
