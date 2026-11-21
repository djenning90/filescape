using FileScape.App.Places;
using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The overview (PLANET-19, 2026-11-11; DJ: "a Universe Sandbox–style overview"): T (or the toolbar) zooms out from
/// where you stand to the whole known folder hierarchy in 3D, laid out as nested star systems
/// (<see cref="UniverseLayout"/>): a folder's subfolders orbit it on one tilted ring. Listed folders are solid worlds,
/// partly listed ones dimmer, folders known only by name are pale ghosts; folders you have visited this session wear a
/// ring; where you stand has a gold beacon. Left- or right-drag (or ← →) orbits, middle-drag or WASD pans, the wheel zooms; click a world
/// to select it (the shared selection: its card shows it) and zoom in on it; Enter or a double-click lands there, through
/// the navigator, so history and Backspace work as everywhere; Esc or T flies back down to where you stood (PLANET-111,
/// <see cref="BeginUniverseReturn"/>). Shift speeds up pan, keyboard orbit and the wheel (PLANET-110, <see cref="OverviewControls"/>).
/// </summary>
public partial class PlanetWorld
{
    /// <summary>Far from every planet (the overview is drawn in the same scene, out of their way).</summary>
    private static readonly Vector3 UniverseOrigin = new(0, 40000, 0);

    // ---- the overview camera (tuning: DJ's playtest 2026-11-11; values chosen by eye, not measured)
    // Smoothing: each frame the camera closes a fraction 1 − e^(−rate·dt) of the gap to its target. That is the exact
    // per-frame step of exponential decay, so the motion is the same at 30, 60 or 144 fps (a fixed "lerp 10 % a frame"
    // would be twice as fast at 120 fps as at 60). The gap shrinks to 1/e (37 %) after 1/rate seconds: a higher rate
    // feels tighter and snappier, a lower one floatier.

    /// <summary>How fast the look-at point follows a click, a pan or a landing, per second (1/e of the gap left after ≈ 0.17 s).</summary>
    private const float FocusFollowRatePerSecond = 6f;

    /// <summary>How fast the camera's distance follows a zoom, per second (≈ 0.29 s): slower than the focus, so a select
    /// reads as "turn to it, then come closer" rather than one lurch.</summary>
    private const float ZoomFollowRatePerSecond = 3.5f;

    /// <summary>The overview's vertical field of view, degrees: the same as the walking camera's, so opening and leaving the
    /// overview never change the lens (a separate choice: the two need not stay equal).</summary>
    private const float OverviewFovDegrees = 62f;

    // Far clip in the overview: OverviewFraming.FarClip (PLANET-192), at least 30 km and always the whole universe from where
    // the camera is (the surface camera uses SurfaceFarMetres).

    /// <summary>Far clip back on a planet, metres (restored when the overview closes).</summary>
    private const float SurfaceFarMetres = 4000f;

    /// <summary>The orbit's starting angles, radians: a three-quarter view from above (yaw ≈ 34°, pitch ≈ 32°).</summary>
    private const float StartYawRadians = 0.6f, StartPitchRadians = 0.55f;

    /// <summary>Drag sensitivity of the orbit, radians per screen pixel (a 360 px drag turns ≈ 103°).</summary>
    private const float OrbitRadiansPerPixel = 0.005f;

    /// <summary>The orbit stops just short of straight down or up (±83°): at ±90° the look-at would flip.</summary>
    private const float MaxPitchRadians = 1.45f;

    /// <summary>Middle-drag pan, in units of the camera distance per pixel: the world under the cursor moves about with it at any zoom.</summary>
    private const float PanDistancesPerPixel = 0.0016f;

    /// <summary>WASD pan speed, camera distances per second: crossing the view takes about the same time at any zoom.</summary>
    private const float KeyPanDistancesPerSecond = 0.9f;

    // One wheel notch's zoom and what Shift does to every control: OverviewControls (Core, PLANET-110).

    // The closest and farthest the wheel goes: OverviewFraming (PLANET-192, AD-98). The farthest is derived from the focus's
    // neighbourhood and the universe's extent (UniverseMaxZoomMetres), never less than the 6,000 m it was fixed at before.

    /// <summary>The universe's extent from its root, metres, as last laid out (<see cref="OverviewFraming.Extent"/>).</summary>
    private float _uniExtent;

    /// <summary>
    /// How far the wheel may zoom out now, metres: from the focus (the selected world, else where you stand) far enough to
    /// see its neighbourhood (<see cref="OverviewFraming.MaxDistance"/>).
    /// </summary>
    public float UniverseMaxZoomMetres()
    {
        var focus = _uniSelected >= 0 && _uniIndex.ContainsKey(_uniSelected) ? _uniSelected : _current?.FolderId ?? -1;
        if (!_uniIndex.TryGetValue(focus, out var i) || _uniBodies.Count == 0) return OverviewFraming.FloorMetres;
        var b = _uniBodies[i].Body;
        UniverseBody? parent = _uniIndex.TryGetValue(b.ParentId, out var pi) ? _uniBodies[pi].Body : null;
        return OverviewFraming.MaxDistance(OverviewFraming.NeighbourhoodSize(b, parent), _uniExtent, System.Numerics.Vector3.Distance(b.Position, _uniBodies[0].Body.Position));
    }

    /// <summary>The universe's extent from its root, metres (probes: the wheel reaches <see cref="UniverseMaxZoomMetres"/>).</summary>
    public float UniverseExtentMetres => _uniExtent;

    // ---- framing a world: distance = a multiple of its system's size (SystemSize), within limits
    /// <summary>Opening: start this many body radii from where you stand, then pull back (the zoom out is the transition).</summary>
    private const float OpenStartRadii = 3f;

    /// <summary>Opening: settle at this many system sizes from where you stand, within 30…3000 m.</summary>
    private const float OpenFrameSystems = 2.6f, OpenMinMetres = 30f, OpenMaxMetres = 3000f;

    /// <summary>Selecting: a little closer than opening, so the chosen system fills more of the view, within 12…3000 m.</summary>
    private const float SelectFrameSystems = 2.4f, SelectMinMetres = 12f, SelectMaxMetres = 3000f;

    /// <summary>Landing: fly in to this many body radii, so the world nearly fills the view as the light comes up.</summary>
    private const float LandingCloseRadii = 1.6f;

    /// <summary>The landing flight, seconds before beat alignment (then moved by at most ±0.09 s onto the music's sixteenth grid).</summary>
    private const float LandingSeconds = 0.75f, LandingBeatShiftSeconds = 0.09f;

    // ---- the white flash (the "warp" light the overview opens out of and lands into)
    /// <summary>The flash colour: a warm white, so it reads as light rather than a blank screen.</summary>
    private static readonly Color FlashColor = new(1f, 0.95f, 0.86f, 0f);

    /// <summary>The flash's peak opacity (and its cap): never fully opaque, so the world stays faintly visible through it.</summary>
    private const float FlashPeakOpacity = 0.85f;

    /// <summary>How fast the flash level may change, per second: it rises with the landing's progress and fades in ≈ 0.6 s.</summary>
    private const float FlashRatePerSecond = 1.6f;

    /// <summary>The flash level on opening: a soft flash (≈ 48 % opacity after the squaring) that fades as you pull back.</summary>
    private const float OpenFlashLevel = 0.75f;

    private Node3D? _universe;

    /// <summary>The drawn bodies' batches and the indices (into <see cref="_uniBodies"/>) in each, in instance order
    /// (re-placed and re-scaled every frame, see DrawnRadius and <see cref="PlaceUniverse"/>).</summary>
    /// <para>PLANET-108 (AD-81): each batch draws only the worlds shown now (<see cref="LiveMultiMesh"/>), with each world's
    /// colour and custom data kept here per batch entry; one engine call per laid-out world, every frame, was most of a
    /// frame at tens of thousands of worlds while a few hundred were drawn.</para>
    private readonly List<(LiveMultiMesh Mesh, List<int> Bodies, Color[] Base, Color[] Custom)> _uniBatches = [];

    /// <summary>A world, ring or haze whose fade is below this is not drawn at all (it would be under a pixel).</summary>
    private const float LiveFadeMin = 0.002f;

    /// <summary>
    /// The smallest a world is drawn in the overview, pixels of radius (FOLLOW-1, 2026-11-17): a world is 1.2–4 m across
    /// but its children's rings are hundreds of metres, so framing a grown system (the Starter Sky's is ≈ 480 m) left every
    /// world 2–5 px wide and the overview looked empty. Far worlds keep this apparent size, like markers in Universe
    /// Sandbox; close up they are their real size. 6 px radius ≈ a 12 px disc: clearly a world, still smaller than its name.
    /// </summary>
    private const float MinDrawnRadiusPixels = 6f;

    /// <summary>
    /// A body's drawn radius now, metres: its real radius, or at least <see cref="MinDrawnRadiusPixels"/> on screen at its
    /// distance from the camera (pixels per radian = view height / (2 tan(fov/2))). A world that has just been discovered
    /// grows into it (<see cref="AppearScale"/>, applied by <see cref="PlaceUniverse"/>).
    /// </summary>
    private float DrawnRadius(UniverseBody b, Vector3 at) => DrawnRadius(b.Radius, at, _uniIndex.TryGetValue(b.NodeId, out var i) ? MinPixelsAt(i) : MinDrawnRadiusPixels);

    /// <param name="minPixels">The least drawn radius in pixels: <see cref="MinDrawnRadiusPixels"/>, or less for worlds on a
    /// crowded ring (PlanetWorld.Detail.cs, PLANET-91).</param>
    private float DrawnRadius(float radius, Vector3 at, float minPixels = MinDrawnRadiusPixels)
    {
        var pixelsPerRadian = GetViewport().GetVisibleRect().Size.Y / (2f * MathF.Tan(Mathf.DegToRad(_camera.Fov) / 2f));
        return MathF.Max(radius, _camera.GlobalPosition.DistanceTo(at) * minPixels / pixelsPerRadian);
    }

    private readonly List<(UniverseBody Body, Vector3 At)> _uniBodies = [];
    private readonly Dictionary<int, int> _uniIndex = [];

    // ---- incremental growth while the overview is open (PLANET-77, AD-71): a relayout never cuts. Every body glides from
    // where it is drawn to its new place over UniverseGrowSeconds; a newly discovered world grows out of its parent; and
    // the whole picture is shifted so the anchor (the selected world, else the root) keeps its place, so the camera,
    // which looks at the anchor, sees its own system unfold rather than the map jump.
    /// <summary>Each body's motion for the current growth step, in the universe node's frame (metres): from, to, and its
    /// radius, ring radius and appear scale at the start (the end values are the layout's).</summary>
    private readonly List<(Vector3 From, Vector3 To, float RadiusFrom, float OrbitFrom, float AppearFrom)> _uniMotion = [];

    /// <summary>Each body's place this frame (universe frame), from <see cref="_uniMotion"/>.</summary>
    private readonly List<Vector3> _uniLocal = [];

    /// <summary>Seconds into the current growth step (≥ <see cref="UniverseGrowSeconds"/>: settled).</summary>
    private float _uniGrowT = float.MaxValue;

    /// <summary>
    /// How long a relayout's glide lasts, seconds, eased in and out: long enough to follow a world to its new place, short
    /// enough to be over before the next relayout (<see cref="UniverseRelayoutSeconds"/>). By eye; DJ to judge.
    /// </summary>
    private const float UniverseGrowSeconds = 1.2f;

    /// <summary>The offset added to every layout position (metres, layout frame) so the anchor stays where it was drawn.</summary>
    private System.Numerics.Vector3 _uniShift;

    /// <summary>Systems drawn with a ring (two or more drawn children) and chains (a parent with one drawn child), by body index.</summary>
    private readonly List<int> _uniRingBodies = [];
    private readonly List<(int Parent, int Child)> _uniChains = [];
    private readonly List<int> _uniVisited = [];
    private MultiMesh? _uniChainMesh, _uniVisitedMesh;

    /// <summary>The rings, only those of shown open systems drawn (PLANET-108).</summary>
    private LiveMultiMesh? _uniRingMesh;
    private MeshInstance3D? _uniHalo;
    private int _uniHere = -1;

    /// <summary>The tree version the overview's layout was made from, and when (app clock).</summary>
    private int _uniLayoutVersion;
    private TimeSpan _uniLayoutAt;

    private Vector3 _uniFocus, _uniFocusTarget;
    private float _uniYaw = StartYawRadians, _uniPitch = StartPitchRadians, _uniDistance, _uniDistanceTarget;
    private int _uniSelected = -1, _uniHovered = -1;

    /// <summary>The pointer is over a name (PLANET-194): the hovered world is the named one, kept at its own size and rank.</summary>
    private bool _uniHoverOnName;
    private bool _uniPanning;
    private (int Id, float T, float Seconds)? _uniLanding;
    private MeshInstance3D? _uniSelectRing;
    private bool _arriveFromSky;

    /// <summary>
    /// The system the camera was last framed on by opening or selecting (its world, and the multiple of its size and the
    /// limits used), until the player zooms or pans. While it holds, a relayout that grows that system eases the distance
    /// out with it (PLANET-77), so a discovered ring stays in view; the camera's angles and focus are never reset.
    /// </summary>
    private (int Id, float Systems, float MinMetres, float MaxMetres)? _uniFrame;

    public bool UniverseOpen => _universe != null;
    public int UniverseBodyCount => _uniBodies.Count;
    public int UniverseSelected => _uniSelected;
    public (int Listed, int Partial, int Unknown, int Visited) UniverseCounts { get; private set; }

    /// <summary>T: zoom out to the overview, or back to where you stood.</summary>
    public void ToggleUniverse()
    {
        if (_universe != null)
        {
            BeginUniverseReturn();
            return;
        }

        OpenUniverse();
    }

    private void OpenUniverse()
    {
        if (Busy || _hop != null || _current == null || _s.Tree is not { } tree || tree.Count == 0) return;
        EndOrbit();
        CloseOverlays();
        _jumpQueued = false;
        // Attention (PlanetWorld.Attention.cs, PLANET-101): where you stand and the world selected before opening (in the
        // list or the sky) are what the budget unfolds first, however deep they are.
        NoteCurrentForAttention();
        var sharedBefore = _s.Selection.Selected;
        NoteSelectionForAttention(sharedBefore >= 0 && tree.Contains(sharedBefore) && tree[sharedBefore].IsDirectory ? sharedBefore : _current.FolderId);
        _uniApproached = -1;
        Attention.Approached = -1;
        var bodies = ComputeUniverse(tree, keepDrawn: false);
        if (bodies.Count == 0) return;
        _universe = new Node3D { Name = "Universe", Position = UniverseOrigin };
        AddChild(_universe);
        _uniShift = System.Numerics.Vector3.Zero;
        ApplyUniverseLayout(tree, bodies, animate: false);
        // Start close to where you stand and pull back: the zoom out is the transition.
        var here = _uniIndex.TryGetValue(_current.FolderId, out var hi) ? _uniBodies[hi] : _uniBodies[0];
        _uniSelected = here.Body.NodeId;
        // PLANET-83: the selection is shared — a world chosen in the list or the sky before opening is the one Enter lands
        // on (before, Enter here acted on "you are here" and glided back down while the card showed the chosen world).
        // The opening still pulls back from where you stand.
        var shared = _s.Selection.Selected;
        if (shared >= 0 && shared != _current.FolderId && _uniIndex.ContainsKey(shared)) _uniSelected = shared;
        _uniSharedSeen = shared;
        _uniFocus = _uniFocusTarget = here.At;
        _uniDistance = here.Body.Radius * OpenStartRadii;
        _uniDistanceTarget = Mathf.Clamp(SystemSize(here.Body) * OpenFrameSystems, OpenMinMetres, OpenMaxMetres);
        _uniFrame = (here.Body.NodeId, OpenFrameSystems, OpenMinMetres, OpenMaxMetres);
        _camera.Far = OverviewFraming.FarClip(_uniDistanceTarget, _uniExtent); // and every frame after (PlaceUniverseCamera)
        _warpFlash = OpenFlashLevel; // a soft flash, fading as you pull back from your world
        _s.Hud.SetPlanetsOverview(open: true);
        _titleTime = MathF.Min(_titleTime, 0.3f); // the arrival title fades out of the way (it lingered over the map)
        _audio.Launch();
        _s.Signs.Clear();
        OpenDiscovery(); // PlanetWorld.Discovery.cs (PLANET-76)
        ShowUniverseHint(); // PlanetWorld.UniverseNav.cs (PLANET-93): the controls, briefly
        UpdateUniverse(tree, 0f, true);
    }

    /// <summary>
    /// The overview's layout of the tree as it is now, spent on attention (PLANET-101, AD-81: <see cref="OverviewAttention"/>).
    /// The way to where you stand is always shown, even through a hidden folder (AppData is hidden on Windows). With
    /// <paramref name="keepDrawn"/> (a relayout while open) what is drawn is retained against small moves of attention and
    /// every system keeps its room, so a relayout adds worlds and folds distant ones in place without moving their
    /// neighbours (PLANET-105); before AD-81 every drawn world was pinned, so once the budget was full nothing near a new
    /// selection could be added.
    /// </summary>
    private IReadOnlyList<UniverseBody> ComputeUniverse(FsTree tree, bool keepDrawn)
    {
        var onTheWay = new HashSet<int>();
        for (var a = _current!.FolderId; a >= 0 && tree.Contains(a); a = tree[a].ParentId) onTheWay.Add(a);
        NoteCurrentForAttention();
        var bodies = Attention.Layout(tree, n => _s.IsVisible(n) || onTheWay.Contains(n.Id), tree.Root.Id, onTheWay, keepDrawn);
        _uniAttentionSignature = Attention.Signature();
        return bodies;
    }

    /// <summary>A system's size for framing, metres (<see cref="OverviewFraming.SystemSize"/>: Core since PLANET-192, unchanged).</summary>
    private static float SystemSize(UniverseBody b) => OverviewFraming.SystemSize(b);

    /// <summary>
    /// Takes a layout into the scene. The first one (<paramref name="animate"/> false) is drawn where it lies. Later ones
    /// (discovery, a listing that arrived) glide: each body from where it is drawn now, a new one from its parent, all
    /// shifted so the anchor — the selected world if it is still shown, else the root — stays where it was headed.
    /// </summary>
    private void ApplyUniverseLayout(FsTree tree, IReadOnlyList<UniverseBody> bodies, bool animate)
    {
        var oldIndex = new Dictionary<int, int>(_uniIndex);
        var oldBodies = _uniBodies.Select(b => b.Body).ToList();
        var oldLocal = new List<Vector3>(_uniLocal);
        var oldMotion = new List<(Vector3 From, Vector3 To, float RadiusFrom, float OrbitFrom, float AppearFrom)>(_uniMotion);
        var oldProgress = GrowEase();
        var newIndex = new Dictionary<int, int>(bodies.Count);
        for (var i = 0; i < bodies.Count; i++) newIndex[bodies[i].NodeId] = i;
        if (animate)
        {
            var anchor = _uniSelected >= 0 && oldIndex.ContainsKey(_uniSelected) && newIndex.ContainsKey(_uniSelected) ? _uniSelected
                : oldIndex.ContainsKey(bodies[0].NodeId) ? bodies[0].NodeId : -1;
            if (anchor >= 0)
            {
                // Where the anchor was headed stays where it is headed: the new layout is shifted onto it.
                var was = oldMotion[oldIndex[anchor]].To;
                var now = Gd(bodies[newIndex[anchor]].Position);
                var shift = was - now;
                _uniShift = new System.Numerics.Vector3(shift.X, shift.Y, shift.Z);
            }
        }

        _uniBodies.Clear();
        _uniIndex.Clear();
        _uniMotion.Clear();
        _uniLocal.Clear();
        _uniExtent = bodies.Count > 0 ? OverviewFraming.Extent(bodies, bodies[0].Position) : 0f; // PLANET-192: the wheel's reach and the far clip
        foreach (var b in bodies)
        {
            var to = Gd(b.Position + _uniShift);
            var motion = (From: to, To: to, RadiusFrom: b.Radius, OrbitFrom: b.OrbitRadius, AppearFrom: 1f);
            if (animate)
            {
                if (oldIndex.TryGetValue(b.NodeId, out var oi))
                {
                    // From where it is drawn now (mid-glide too), with the size it has now.
                    var om = oldMotion[oi];
                    var ob = oldBodies[oi];
                    motion = (oldLocal[oi], to, Mathf.Lerp(om.RadiusFrom, ob.Radius, oldProgress), Mathf.Lerp(om.OrbitFrom, ob.OrbitRadius, oldProgress), Mathf.Lerp(om.AppearFrom, 1f, oldProgress));
                }
                else
                {
                    // New: out of its parent (where the parent is drawn now, or will be).
                    var from = oldIndex.TryGetValue(b.ParentId, out var pi) ? oldLocal[pi] : newIndex.TryGetValue(b.ParentId, out var np) ? Gd(bodies[np].Position + _uniShift) : to;
                    motion = (from, to, b.Radius, 0f, 0f);
                }
            }

            _uniIndex[b.NodeId] = _uniBodies.Count;
            _uniBodies.Add((b, UniverseOrigin + motion.From));
            _uniMotion.Add(motion);
            _uniLocal.Add(motion.From);
        }

        if (animate && _uniFrame is { } frame && newIndex.TryGetValue(frame.Id, out var fi))
        {
            // Keep the framed system framed as it grows (not a reset: only the distance follows, at the zoom's own ease).
            _uniDistanceTarget = Mathf.Clamp(SystemSize(bodies[fi]) * frame.Systems, frame.MinMetres, frame.MaxMetres);
            _uniFocusTarget = UniverseOrigin + _uniMotion[fi].To;
        }

        RemapDetail(oldIndex, bodies, first: !animate); // PlanetWorld.Detail.cs (PLANET-91): fades carried by node id
        _uniGrowT = animate ? 0f : float.MaxValue;
        _uniLayoutVersion = tree.Version;
        _uniLayoutAt = _s.Now;
        BuildUniverseScene(tree, bodies);
        PlaceUniverse(0f);
    }

    /// <summary>The current growth step's progress, 0…1, eased in and out (smoothstep); 1 when settled.</summary>
    private float GrowEase()
    {
        if (_uniGrowT >= UniverseGrowSeconds) return 1f;
        var u = Mathf.Clamp(_uniGrowT / UniverseGrowSeconds, 0f, 1f);
        return u * u * (3f - 2f * u);
    }

    /// <summary>A body's appear scale now: 1 for a settled world, growing from 0 for a newly discovered one.</summary>
    private float AppearScale(int i) => Mathf.Lerp(_uniMotion[i].AppearFrom, 1f, GrowEase());

    /// <summary>
    /// Builds the overview's scene objects for a layout: one batch of solid worlds and one of ghosts (colours fixed per
    /// layout), the orbit rings and chains as instances of a unit circle and a unit segment, the visited rings, the beacon
    /// where you stand. Their transforms are set every frame by <see cref="PlaceUniverse"/>, so a relayout glides.
    /// </summary>
    private void BuildUniverseScene(FsTree tree, IReadOnlyList<UniverseBody> bodies)
    {
        foreach (var child in _universe!.GetChildren()) child.QueueFree();
        var sphere = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 24, Rings = 12 };
        var solid = new List<int>();
        var ghosts = new List<int>();
        for (var i = 0; i < bodies.Count; i++) (bodies[i].Discovery == Discovery.Unknown ? ghosts : solid).Add(i);
        _uniBatches.Clear();
        MultiMeshInstance3D Batch(List<int> list, Material mat, Func<UniverseBody, (Color Base, Color Custom)> look, bool custom)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = custom, Mesh = sphere };
            var baseColors = new Color[list.Count];
            var customs = new Color[list.Count];
            for (var i = 0; i < list.Count; i++) (baseColors[i], customs[i]) = look(bodies[list[i]]);
            _uniBatches.Add((new LiveMultiMesh(mm), list, baseColors, customs));

            return new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        }

        // Solid worlds: one shared material, each world's look per instance (PlanetWorld.Detail.cs, PLANET-92).
        var solidMat = SolidWorldMaterial();
        var ghostMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Back };
        if (solid.Count > 0)
        {
            _universe.AddChild(Batch(solid, solidMat, b => WorldLookOf(tree, b), custom: OverviewLooksVaried));
        }

        if (ghosts.Count > 0) _universe.AddChild(Batch(ghosts, ghostMat, _ => (new Color(0.7f, 0.72f, 0.95f, 0.22f), default), custom: false));

        // Orbits: one faint ring per system with two or more children, in its tilted plane; a folder with a single subfolder
        // is a chain, not a system: a short straight link, not a ring across the sky.
        var childCount = new Dictionary<int, int>();
        foreach (var b in bodies)
        {
            if (b.ParentId >= 0) childCount[b.ParentId] = childCount.GetValueOrDefault(b.ParentId) + 1;
        }

        _uniRingBodies.Clear();
        _uniChains.Clear();
        for (var i = 0; i < bodies.Count; i++)
        {
            var b = bodies[i];
            if (b.OrbitRadius > 0 && childCount.GetValueOrDefault(b.NodeId) >= 2) _uniRingBodies.Add(i);
            if (b.ParentId >= 0 && _uniIndex.TryGetValue(b.ParentId, out var pi) && childCount.GetValueOrDefault(b.ParentId) == 1) _uniChains.Add((pi, i));
        }

        // Per-instance colours (white, alpha = the level of detail's fade) multiply the line colour: a folded system's ring fades out.
        var lineMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(0.55f, 0.65f, 1f, 0.22f), VertexColorUseAsAlbedo = true };
        _uniRingMesh = _uniRingBodies.Count > 0 ? new LiveMultiMesh(new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = UnitCircleLines() }) : null;
        _uniChainMesh = _uniChains.Count > 0 ? new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = UnitSegmentLine(), InstanceCount = _uniChains.Count } : null;
        foreach (var mm in new[] { _uniRingMesh?.Mesh, _uniChainMesh })
        {
            if (mm != null) _universe.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = lineMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        BuildHaze(bodies, childCount); // PlanetWorld.Detail.cs (PLANET-91): folded systems' haze

        // Visited this session: a ring round the world. Where you stand: a gold beacon.
        _uniVisited.Clear();
        for (var i = 0; i < bodies.Count; i++)
        {
            if (_s.Visited(bodies[i].NodeId)) _uniVisited.Add(i);
        }

        _uniVisitedMesh = null;
        if (_uniVisited.Count > 0)
        {
            var torus = new TorusMesh { InnerRadius = 1.35f, OuterRadius = 1.5f, Rings = 32, RingSegments = 4 };
            _uniVisitedMesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = torus, InstanceCount = _uniVisited.Count };
            _universe.AddChild(new MultiMeshInstance3D { Multimesh = _uniVisitedMesh, MaterialOverride = PlanetArt.GlowMaterial(new Color(0.75f, 0.95f, 1f), 1.4f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        _uniHalo = null;
        _uniHere = _uniIndex.TryGetValue(_current!.FolderId, out var ci) ? ci : -1;
        if (_uniHere >= 0)
        {
            _uniHalo = PlanetArt.Halo(new Color(1f, 0.82f, 0.4f), bodies[ci].Radius * 3.2f, 1.4f);
            _universe.AddChild(_uniHalo);
        }

        if (_uniSelectRing == null)
        {
            _uniSelectRing = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 1.6f, OuterRadius = 1.75f, Rings = 48, RingSegments = 4 },
                MaterialOverride = PlanetArt.GlowMaterial(new Color(1f, 0.92f, 0.55f), 2.2f, pulse: 1f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_uniSelectRing);
        }

        UniverseCounts = (bodies.Count(b => b.Discovery == Discovery.Listed), bodies.Count(b => b.Discovery == Discovery.Partial), bodies.Count(b => b.Discovery == Discovery.Unknown), _uniVisited.Count);
    }

    /// <summary>Segments of the unit circle drawn for every orbit (scaled, tilted and placed per system).</summary>
    private const int OrbitSegments = 64;

    /// <summary>The unit circle in the XZ plane as line segments: a ring instance maps X and Z onto its plane's axes.</summary>
    private static ArrayMesh UnitCircleLines()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Lines);
        for (var i = 0; i < OrbitSegments; i++)
        {
            var t0 = Mathf.Tau * i / OrbitSegments;
            var t1 = Mathf.Tau * (i + 1) / OrbitSegments;
            st.AddVertex(new Vector3(Mathf.Cos(t0), 0, Mathf.Sin(t0)));
            st.AddVertex(new Vector3(Mathf.Cos(t1), 0, Mathf.Sin(t1)));
        }

        return st.Commit();
    }

    /// <summary>A unit segment along +Z: a chain instance maps Z onto parent → child.</summary>
    private static ArrayMesh UnitSegmentLine()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Lines);
        st.AddVertex(Vector3.Zero);
        st.AddVertex(Vector3.Back);
        return st.Commit();
    }

    /// <summary>
    /// Advances the growth glide by <paramref name="dt"/> and sets every drawn transform from it: bodies (at their drawn
    /// radius, see DrawnRadius), rings, chains, visited rings and the beacon (≤ 1,500 bodies plus their rings a frame).
    /// </summary>
    private void PlaceUniverse(float dt)
    {
        if (_universe == null) return;
        if (_uniGrowT < UniverseGrowSeconds) _uniGrowT += dt;
        var e = GrowEase();
        for (var i = 0; i < _uniMotion.Count; i++)
        {
            var m = _uniMotion[i];
            var p = e >= 1f ? m.To : m.From.Lerp(m.To, e);
            _uniLocal[i] = p;
            _uniBodies[i] = (_uniBodies[i].Body, UniverseOrigin + p);
        }

        UpdateUniverseDetail(dt); // PlanetWorld.Detail.cs (PLANET-91): which systems are open at this distance
        var cam = _camera.GlobalPosition;
        var pixelsPerRadian = GetViewport().GetVisibleRect().Size.Y / (2f * MathF.Tan(Mathf.DegToRad(_camera.Fov) / 2f));
        foreach (var (live, list, baseColors, customs) in _uniBatches)
        {
            live.Begin();
            for (var k = 0; k < list.Count; k++)
            {
                var i = list[k];
                var show = ShowScale(i) * AppearScale(i);
                if (show < LiveFadeMin) continue; // folded away: not drawn at all
                var radius = Mathf.Lerp(_uniMotion[i].RadiusFrom, _uniBodies[i].Body.Radius, e);
                // DrawnRadius, with the view's scale worked out once a frame.
                var r = MathF.Max(radius, cam.DistanceTo(_uniBodies[i].At) * MinPixelsAt(i) / pixelsPerRadian) * show;
                var o = live.Next();
                var buf = live.Buffer;
                LiveMultiMesh.WriteTransform(buf, o, new Vector3(r, 0, 0), new Vector3(0, r, 0), new Vector3(0, 0, r), _uniLocal[i]);
                LiveMultiMesh.WriteColor(buf, o + 12, baseColors[k]);
                if (live.Stride > 16) LiveMultiMesh.WriteColor(buf, o + 16, customs[k]);
            }

            live.End();
        }

        if (_uniRingMesh != null)
        {
            _uniRingMesh.Begin();
            for (var k = 0; k < _uniRingBodies.Count; k++)
            {
                // The ring's plane axes as the layout builds them: a = n × X (or Z when n is nearly along X), c = n × a.
                var i = _uniRingBodies[k];
                var fade = ShowScale(i) * OpenScale(i);
                if (fade < LiveFadeMin) continue; // a folded or hidden system's ring is not drawn
                var b = _uniBodies[i].Body;
                var n = Gd(b.OrbitNormal);
                var a = n.Cross(MathF.Abs(n.X) < 0.9f ? Vector3.Right : Vector3.Back).Normalized();
                var c = n.Cross(a);
                var r = Mathf.Lerp(_uniMotion[i].OrbitFrom, b.OrbitRadius, e);
                var o = _uniRingMesh.Next();
                LiveMultiMesh.WriteTransform(_uniRingMesh.Buffer, o, a * r, n * r, c * r, _uniLocal[i]);
                LiveMultiMesh.WriteColor(_uniRingMesh.Buffer, o + 12, new Color(1, 1, 1, fade));
            }

            _uniRingMesh.End();
        }

        if (_uniChainMesh != null)
        {
            for (var k = 0; k < _uniChains.Count; k++)
            {
                var (pi, ci) = _uniChains[k];
                var d = _uniLocal[ci] - _uniLocal[pi];
                var dir = d.LengthSquared() > 1e-8f ? d.Normalized() : Vector3.Back;
                var u = dir.Cross(MathF.Abs(dir.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
                var v = dir.Cross(u);
                // Z carries the segment; X and Y only keep the basis invertible (the segment has no width).
                _uniChainMesh.SetInstanceTransform(k, new Transform3D(new Basis(u, v, d.LengthSquared() > 1e-8f ? d : dir * 1e-4f), _uniLocal[pi]));
                _uniChainMesh.SetInstanceColor(k, new Color(1, 1, 1, ShowScale(ci)));
            }
        }

        if (_uniVisitedMesh != null)
        {
            for (var k = 0; k < _uniVisited.Count; k++)
            {
                var i = _uniVisited[k];
                var r = _uniBodies[i].Body.Radius * AppearScale(i) * ShowScale(i);
                _uniVisitedMesh.SetInstanceTransform(k, new Transform3D(Basis.Identity.Scaled(new Vector3(r, r, r)), _uniLocal[i]));
            }
        }

        if (_uniHalo != null && _uniHere >= 0) _uniHalo.Position = _uniLocal[_uniHere];
        PlaceHaze(); // PlanetWorld.Detail.cs
    }

    /// <summary>Leaves the overview; <paramref name="returnToSurface"/> glides the camera back down to where you stood.
    /// What discovery learned stays in the session's tree (PLANET-78).</summary>
    private void CloseUniverse(bool returnToSurface)
    {
        if (_universe == null) return;
        var from = _camera.GlobalTransform;
        EndOrbit();
        _uniPanning = false;
        _universe.QueueFree();
        _universe = null;
        _uniBatches.Clear();
        _uniSelectRing?.QueueFree();
        _uniSelectRing = null;
        _uniBodies.Clear();
        _uniIndex.Clear();
        _uniMotion.Clear();
        _uniLocal.Clear();
        _uniRingBodies.Clear();
        _uniChains.Clear();
        _uniVisited.Clear();
        _uniRingMesh = null;
        _uniChainMesh = _uniVisitedMesh = null;
        _uniHalo = null;
        _uniHere = -1;
        _uniLanding = null;
        _uniReturn = null;
        _uniFrame = null;
        CloseDiscovery(); // PlanetWorld.Discovery.cs
        HideUniverseHint(); // PlanetWorld.UniverseNav.cs
        _camera.Far = SurfaceFarMetres;
        _s.Signs.Clear();
        _s.Hud.SetPlanetsOverview(open: false);
        SetPrompt("");
        _warpFlash = 0; // the overview's veil never outlives it
        _flash.Color = FlashColor;
        if (returnToSurface)
        {
            _camBlendFrom = from;
            _camBlend = _camBlendTotal = 1.1f; // the way back down is a glide, never a cut
            _audio.Arrive();
        }
    }

    private void UpdateUniverse(FsTree tree, float dt, bool blockedKeys)
    {
        // A selection made elsewhere while the overview is open (a row of the Destinations list) is the overview's too
        // (PLANET-83), so Enter lands where the card says.
        var shared = _s.Selection.Selected;
        if (shared != _uniSharedSeen)
        {
            _uniSharedSeen = shared;
            if (shared >= 0 && shared != _uniSelected && _uniLanding == null && _uniIndex.ContainsKey(shared)) SelectInUniverse(shared);
        }

        if (_uniLanding is { } landing)
        {
            // Flying into the chosen world before landing on it.
            var t = landing.T + dt;
            _uniLanding = (landing.Id, t, landing.Seconds);
            if (t >= landing.Seconds)
            {
                _audio.Launch(); // on the beat, as every launch
                LandFromUniverse(landing.Id); // travel: OverviewLanding
                return;
            }
        }

        if (_uniReturn is { } back)
        {
            // Esc's way back (PLANET-111): along the zoom-and-pan path to your world, then down onto the planet.
            var t = back.T + dt;
            _uniReturn = back with { T = t };
            var progress = Mathf.Clamp(t / back.Seconds, 0f, 1f);
            var view = back.Path.At(back.Path.Length * OverviewReturn.Ease(progress));
            _uniFocus = _uniFocusTarget = Gd(view.Focus);
            _uniDistance = _uniDistanceTarget = view.DistanceMetres;
            if (progress >= 1f)
            {
                PlaceUniverseCamera();
                FinishUniverseReturn();
                return;
            }
        }

        if (!blockedKeys && _uniLanding == null && _uniReturn == null)
        {
            var right = new Vector3(Mathf.Cos(_uniYaw), 0, -Mathf.Sin(_uniYaw));
            var forward = Vector3.Up.Cross(right);
            var fast = Godot.Input.IsActionPressed(InputBindings.Fast); // Shift (PLANET-110): per-control factors in OverviewControls
            _uniYaw -= Axis(InputBindings.TurnLeft, InputBindings.TurnRight) * KeyLookDegreesPerSecond * Mathf.Pi / 180f * dt
                * OverviewControls.SpeedFactor(OverviewMove.KeyOrbit, fast); // ← → orbit (AD-47)
            var move = right * Axis(InputBindings.Left, InputBindings.Right)
                + forward * Axis(InputBindings.Back, InputBindings.Forward)
                + Vector3.Up * (Axis(InputBindings.Down, InputBindings.Up));
            if (move.LengthSquared() > 0.01f)
            {
                _uniFocusTarget += move.LimitLength() * _uniDistance * KeyPanDistancesPerSecond * OverviewControls.SpeedFactor(OverviewMove.KeyPan, fast) * dt;
                _uniFrame = null; // the player took the camera: growth no longer reframes it
            }
        }

        // The flash follows the landing's progress (0 → 1 over the flight) and otherwise fades to 0; squaring the level
        // keeps it faint early and brightens it late, so the light seems to rush up as you arrive.
        _warpFlash = _uniReturn is { } rf
            ? Mathf.Max(OverviewReturn.FlashLevel(Mathf.Clamp(rf.T / rf.Seconds, 0f, 1f)), Mathf.MoveToward(_warpFlash, 0, dt * FlashRatePerSecond)) // the opening's flash may still be fading
            : Mathf.MoveToward(_warpFlash, _uniLanding is { } ul ? Mathf.Clamp(ul.T / ul.Seconds, 0, 1) : 0, dt * FlashRatePerSecond);
        _flash.Color = FlashColor with { A = Mathf.Clamp(_warpFlash * _warpFlash * FlashPeakOpacity, 0, FlashPeakOpacity) };
        var ease = 1 - Mathf.Exp(-dt * FocusFollowRatePerSecond); // frame-rate independent smoothing (see the constants above)
        _uniFocus = _uniFocus.Lerp(_uniFocusTarget, ease);
        _uniDistance = Mathf.Lerp(_uniDistance, _uniDistanceTarget, 1 - Mathf.Exp(-dt * ZoomFollowRatePerSecond));
        PlaceUniverseCamera();
        if (_uniReturn == null) StepDiscovery(tree); // PlanetWorld.Discovery.cs: ask for more listings, relayout when the tree has grown (not on the way back: nothing may move under it)
        PlaceUniverse(dt);

        var mouse = GetViewport().GetMousePosition();
        // PLANET-194: over a name, the world it names is the one pointed at (not the world under the name); it is not promoted
        // or enlarged for that (its name would move away from under the pointer).
        var overName = !_orbiting && !_uniPanning && SignTarget(mouse) is { } nm && _uniIndex.ContainsKey(nm) ? nm : -1;
        _uniHoverOnName = overName >= 0;
        _uniHovered = !_orbiting && !_uniPanning && !_s.Hud.BlockingRects.Any(r => r.HasPoint(mouse)) ? (overName >= 0 && PickUniverseDisc(mouse) is var disc && (disc < 0 || disc == overName) ? overName : PickUniverse(mouse)) : -1; // a world's disc beats another's name (as Click)
        if (_uniSelectRing != null)
        {
            var sel = _uniIndex.TryGetValue(_uniSelected, out var si) ? _uniBodies[si] : default;
            _uniSelectRing.Visible = si >= 0 && _uniSelected >= 0;
            if (_uniSelectRing.Visible)
            {
                var toCam = (_camera.GlobalPosition - sel.At).Normalized();
                var r = DrawnRadius(sel.Body, sel.At);
                var basis = Basis.LookingAt(toCam, Vector3.Up).Rotated(Basis.LookingAt(toCam, Vector3.Up).X, Mathf.Pi / 2);
                _uniSelectRing.GlobalTransform = new Transform3D(basis.Scaled(new Vector3(r, r, r)), sel.At);
            }
        }

        // Names: where you stand, the selected and pointed-at worlds, the top, and a few worlds near the middle of the view.
        _signs.Clear();
        var camPos = _camera.GlobalPosition;
        var camFwd = -_camera.GlobalBasis.Z;
        var pixelsPerRadian = GetViewport().GetVisibleRect().Size.Y / (2f * MathF.Tan(Mathf.DegToRad(_camera.Fov) / 2f)); // a world's drawn radius on screen
        // Only drawn worlds are measured (PLANET-108: at tens of thousands laid out, most are folded away).
        var nearby = _uniBodies
            .Select((b, i) => (b, i))
            .Where(x => Drawn(x.i))
            .Select(x => (x.b, x.i, d: x.b.At.DistanceTo(camPos), look: camFwd.Dot((x.b.At - camPos).Normalized())))
            .Where(x => x.look > 0.94f && x.d < _uniDistance * 1.6f)
            .OrderBy(x => x.d)
            .Take(10)
            .Select(x => x.b.Body.NodeId)
            .ToHashSet();
        // The biggest folded systems in view are named too, with what they hold (PLANET-91: a clear indication).
        foreach (var id in _uniBodies
                     .Select((b, i) => (b, i))
                     .Where(x => Drawn(x.i) && FoldedAt(x.i) >= FoldedLabelMin)
                     .Select(x => (x.b, x.i, look: camFwd.Dot((x.b.At - camPos).Normalized())))
                     .Where(x => x.look > 0.85f)
                     .OrderByDescending(x => FoldedAt(x.i))
                     .Take(FoldedLabels)
                     .Select(x => x.b.Body.NodeId)) nearby.Add(id);
        for (var bi = 0; bi < _uniBodies.Count; bi++)
        {
            var (b, at) = _uniBodies[bi];
            if (!Drawn(bi)) continue;
            var id = b.NodeId;
            var here = id == _current?.FolderId;
            var focus = id == _uniSelected || id == _uniHovered && !_uniHoverOnName || here;
            if (!focus && b.ParentId >= 0 && !nearby.Contains(id)) continue;
            var n = tree[id];
            var name = (here ? "★ " : "") + tree.DisplayName(id);
            var detail = here ? "you are here" : b.Discovery switch
            {
                Discovery.Unknown => Listing.Describe(Listing.Of(n)),
                Discovery.Partial => "partly listed",
                _ => FolderSummary.Gate(n, Listing.Of(n)),
            };
            var folded = FoldedAt(bi);
            if (folded > b.HiddenChildren) detail += $" · +{folded:N0} worlds inside";
            else if (b.HiddenChildren > 0) detail += $" · +{b.HiddenChildren} more folders";
            if (_s.Visited(id) && !here) detail += " · visited";
            var color = here ? new Color(1f, 0.85f, 0.45f) : b.Discovery == Discovery.Unknown ? new Color(0.75f, 0.78f, 0.95f) : new Color(1f, 0.95f, 0.8f);
            // PLANET-193 (AD-98): placed round the world by Core's LabelPlacement, by priority: the selection, the pointed-at
            // world, where you stand, the root, the largest folded systems, then the nearest (before: pinned straight above
            // each world, stacked on long stems that crossed other worlds and names).
            var tier = id == _uniSelected ? 0 : id == _uniHovered && !_uniHoverOnName ? 1 : here ? 2 : b.ParentId < 0 ? 3 : FoldedAt(bi) >= FoldedLabelMin ? 4 : 5;
            var pixels = DrawnRadius(b, at) / MathF.Max(1e-3f, camPos.DistanceTo(at)) * pixelsPerRadian;
            _signs.Add(new SignCandidate(id, at, name, detail, color, FileIcons.For(n), focus, here ? 30f : 0f, Around: true, AnchorPixels: pixels, Tier: tier));
        }

        _s.Signs.Layout(_camera, _signs, _s.Hud.BlockingRects);
        var selName = _uniSelected >= 0 && tree.Contains(_uniSelected) ? tree.DisplayName(_uniSelected) : "";
        var (listed, partial, unknown, visitedCount) = UniverseCounts;
        var shown = PromptName(selName);
        SetPrompt(_uniSelected == _current?.FolderId ? $"★ {shown}: you are here · click a world, then {K("Enter")} to land there" : $"{shown} · {K("Enter")} (or double-click) to land there",
            $"{_uniBodies.Count:N0} known folders: {listed:N0} listed (solid) · {partial:N0} partly · {unknown:N0} not listed yet (pale) · {visitedCount:N0} visited (ringed)   —   {K("Shift")}: faster · {K("X")}: pause discovery · {K("Esc")} or {K("T")}: back down · {K("F1")}: every control");
        UpdateUniverseHint(dt); // PlanetWorld.UniverseNav.cs
    }

    /// <summary>The overview camera from its orbit: <see cref="_uniDistance"/> from <see cref="_uniFocus"/> at the orbit's angles.</summary>
    private void PlaceUniverseCamera()
    {
        var offset = new Vector3(Mathf.Sin(_uniYaw) * Mathf.Cos(_uniPitch), Mathf.Sin(_uniPitch), Mathf.Cos(_uniYaw) * Mathf.Cos(_uniPitch));
        // Looking along −offset rather than LookAt(focus): far out in a vast overview (hundreds of km from the origin)
        // Godot's LookAt judges a few metres "the same position" (relative tolerance) and fails (AD-81, the vast fixture).
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(-offset, Vector3.Up), _uniFocus + offset * _uniDistance);
        _camera.Fov = OverviewFovDegrees;
        _camera.Far = OverviewFraming.FarClip(_uniDistance, _uniExtent); // PLANET-192: the whole universe from the farthest zoom
    }

    /// <summary>
    /// The world under the pointer: of the worlds whose drawn disc (a little enlarged, and at least
    /// <see cref="PickMinPixels"/>) contains the point, the one whose centre is nearest it on screen. Drawn at a minimum
    /// size (FOLLOW-1) neighbouring worlds overlap, and the nearest along the ray was often not the one clicked.
    /// </summary>
    private int PickUniverse(Vector2 screen)
    {
        var best = float.PositiveInfinity;
        var id = -1;
        var right = _camera.GlobalBasis.X;
        for (var i = 0; i < _uniBodies.Count; i++)
        {
            var (b, at) = _uniBodies[i];
            if (!Drawn(i) || _camera.IsPositionBehind(at)) continue; // a folded world is not there to click (PLANET-91)
            var c = _camera.UnprojectPosition(at);
            var px = c.DistanceTo(_camera.UnprojectPosition(at + right * DrawnRadius(b, at)));
            var d = c.DistanceTo(screen);
            if (d > MathF.Max(px * 1.25f, PickMinPixels) || d >= best) continue;
            best = d;
            id = b.NodeId;
        }

        return id;
    }

    /// <summary>
    /// The world whose drawn disc itself (not enlarged, no minimum) contains the point, nearest centre first, or −1: what
    /// the pointer is visibly on, which wins over another world's name lying over it (<see cref="Click"/>, the hover).
    /// </summary>
    private int PickUniverseDisc(Vector2 screen)
    {
        var best = float.PositiveInfinity;
        var id = -1;
        var right = _camera.GlobalBasis.X;
        for (var i = 0; i < _uniBodies.Count; i++)
        {
            var (b, at) = _uniBodies[i];
            if (!Drawn(i) || _camera.IsPositionBehind(at)) continue;
            var c = _camera.UnprojectPosition(at);
            var px = c.DistanceTo(_camera.UnprojectPosition(at + right * DrawnRadius(b, at)));
            var d = c.DistanceTo(screen);
            if (d > px || d >= best) continue;
            best = d;
            id = b.NodeId;
        }

        return id;
    }

    /// <summary>A click this close to a world's centre picks it however small it is drawn, pixels.</summary>
    private const float PickMinPixels = 8f;

    /// <summary>Esc's way back in progress (PLANET-111): the path, seconds into it, and its length in seconds.</summary>
    private (ZoomPanPath Path, float T, float Seconds)? _uniReturn;

    /// <summary>On the way back down from the overview (probes).</summary>
    public bool UniverseReturning => _uniReturn != null;

    /// <summary>
    /// Esc or T in the overview (PLANET-111, AD-82): fly back down to where you stand. The camera follows a zoom-and-pan
    /// path (<see cref="ZoomPanPath"/>) from wherever it is to <see cref="OverviewReturn.CloseRadii"/> radii from your
    /// world, the map still drawn and a soft light rising at the end; then <see cref="FinishUniverseReturn"/> hands it to
    /// the planet. A second Esc or T hurries it (keeping the progress made, <see cref="OverviewReturn.HurriedSeconds"/>).
    /// Without a drawn world for where you stand (it always is: the way there is pinned) it falls back to the old glide.
    /// </summary>
    private void BeginUniverseReturn()
    {
        if (_universe == null) return;
        if (_uniReturn is { } going)
        {
            // Hurry: the remaining share of the move takes HurriedSeconds, from the same progress (never a jump).
            var p = Mathf.Clamp(going.T / going.Seconds, 0f, 0.999f);
            var total = MathF.Min(going.Seconds, OverviewReturn.HurriedSeconds / (1f - p));
            _uniReturn = going with { T = p * total, Seconds = total };
            return;
        }

        if (_uniLanding != null) return; // already flying into another world
        if (_uniHere < 0 || _current == null)
        {
            CloseUniverse(returnToSurface: true);
            return;
        }

        EndOrbit();
        _uniPanning = false;
        _uniFrame = null;
        var here = _uniBodies[_uniHere];
        var to = new OverviewView(Nv(here.At), here.Body.Radius * OverviewReturn.CloseRadii);
        var path = new ZoomPanPath(new OverviewView(Nv(_uniFocus), _uniDistance), to);
        _uniReturn = (path, 0f, (float)OverviewReturn.Duration(path).TotalSeconds);
        UniverseReturns++;
    }

    /// <summary>
    /// The hand-over (PLANET-111): the overview closes and the camera starts on the planet where the overview left it —
    /// as many planet radii from its centre as it was (drawn) body radii from your world, looking at the centre, above and
    /// behind the traveller (<see cref="OverviewReturn.SurfaceStart"/>) — then glides into the walking camera over
    /// <see cref="OverviewReturn.SurfaceBlendSeconds"/> (the same blend as an arrival's establishing shot). The soft light
    /// carries over and fades. Nothing about where you stand changes.
    /// </summary>
    private void FinishUniverseReturn()
    {
        var here = _uniBodies[_uniHere];
        var drawn = MathF.Max(DrawnRadius(here.Body, here.At), 1e-3f);
        var radii = _camera.GlobalPosition.DistanceTo(here.At) / drawn;
        var offCentre = Mathf.RadToDeg((-_camera.GlobalBasis.Z).AngleTo(here.At - _camera.GlobalPosition));
        var flash = _warpFlash;
        CloseUniverse(returnToSurface: false);
        if (_current == null) return;
        var (eye, forward, screenUp) = OverviewReturn.SurfaceStart(_walker.Up, _walker.Facing, _current.Comp.Shape.Radius, radii);
        _camBlendFrom = new Transform3D(Basis.LookingAt(Gd(forward), Gd(screenUp)), Gd(eye));
        _camera.GlobalTransform = _camBlendFrom;
        _camera.Fov = OverviewFovDegrees;
        _camBlend = _camBlendTotal = OverviewReturn.SurfaceBlendSeconds;
        _warpFlash = flash;
        _flash.Color = FlashColor with { A = Mathf.Clamp(_warpFlash * _warpFlash * FlashPeakOpacity, 0, FlashPeakOpacity) };
        _audio.Arrive();
        LastReturnHandOver = (radii, eye.Length() / _current.Comp.Shape.Radius, offCentre);
    }

    /// <summary>
    /// One frame of the way back as the player sees your world (probes, PLANET-111): whether the overview is drawn, the
    /// camera's distance from your world's centre in its radii (the drawn body's in the overview, the planet's after), how
    /// far the centre is off the view's axis (degrees), and the light's opacity. Continuous through the hand-over.
    /// </summary>
    public (bool Overview, float DistanceRadii, float OffCentreDegrees, float Flash) ReturnTraceSample()
    {
        var eye = _camera.GlobalPosition;
        var fwd = -_camera.GlobalBasis.Z;
        if (_universe != null && _uniHere >= 0)
        {
            var h = _uniBodies[_uniHere];
            var to = h.At - eye;
            return (true, to.Length() / MathF.Max(DrawnRadius(h.Body, h.At), 1e-3f), Mathf.RadToDeg(fwd.AngleTo(to)), _flash.Color.A);
        }

        if (_current == null) return default;
        return (false, eye.Length() / _current.Comp.Shape.Radius, Mathf.RadToDeg(fwd.AngleTo(-eye)), _flash.Color.A);
    }

    /// <summary>Ways back begun with Esc or T (probes).</summary>
    public int UniverseReturns { get; private set; }

    /// <summary>The last hand-over (probes): the camera's distance from your world in drawn body radii in the overview's
    /// last frame and in planet radii in the planet's first, and how far your world was off the view's centre, degrees.</summary>
    public (float OverviewRadii, float SurfaceRadii, float OffCentreDegrees) LastReturnHandOver { get; private set; }

    /// <summary>Input in the overview (from Main's unhandled input): keys and clicks. Returns true when consumed.</summary>
    private bool UniverseCommand(InputEvent e)
    {
        if (_uniReturn != null && e is InputEventKey { Pressed: true } k
            && (k.Keycode is Key.Enter or Key.KpEnter || (k.Keycode == Key.Home && !k.CtrlPressed) || k.IsAction(InputBindings.EnterFolder) || k.IsAction(InputBindings.Frame)
                || k.PhysicalKeycode is Key.Bracketleft or Key.Bracketright or Key.Z))
        {
            // On the way back down the overview's own moves wait (Esc or T again hurries it). The shared commands still
            // act: Backspace, the list or a path change the navigator, which lands out of the light as from the overview.
            return true;
        }

        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } key when key.IsAction(InputBindings.FlightToggle) || key.Keycode == Key.Escape:
                BeginUniverseReturn();
                return true;
            case InputEventKey { Pressed: true, Echo: false } key when key.Keycode is Key.Enter or Key.KpEnter || key.IsAction(InputBindings.EnterFolder):
                BeginUniverseLanding(UniverseCardSelection()); // travel: OverviewLanding
                return true;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Home, CtrlPressed: false }:
                if (_current != null) SelectInUniverse(_current.FolderId);
                return true;
            case InputEventKey { Pressed: true, Echo: false } f when f.IsAction(InputBindings.Frame):
                ZoomToSelection(); // F frames the selected world again (the card's "Zoom to (F)")
                return true;
            case InputEventKey { Pressed: true, Echo: false } b when b.IsAction(InputBindings.Overview):
                return true; // B (the overlook view) means nothing up here
            case InputEventKey { Pressed: true, Echo: false } x when x.IsAction(InputBindings.DiscoveryPause):
                ToggleDiscoveryPause(); // PlanetWorld.Discovery.cs (PLANET-76)
                return true;
            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.N, CtrlPressed: false }:
                RequestSoundToggle(); // PlanetWorld.Sound.cs: the same control as the toolbar's sound button
                return true;
            case InputEventKey { Pressed: true, PhysicalKeycode: Key.Bracketleft or Key.Bracketright, CtrlPressed: false } ring:
                StepRoundRing(ring.PhysicalKeycode == Key.Bracketright ? 1 : -1); // PlanetWorld.UniverseNav.cs (PLANET-93)
                return true;
            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Z, CtrlPressed: false }:
                StepUpLevel(); // PlanetWorld.UniverseNav.cs (PLANET-93)
                return true;
        }

        return false; // Backspace, J, L, Ctrl+L…: the shared commands (a navigation change lands from the overview)
    }

    /// <summary>
    /// The world the overview's keys act on: the card's selection when the overview draws it, else the overview's own
    /// (PLANET-87: the card's Parent, or a pick in a list, moves the shared selection; F and Enter must follow it, not
    /// the world clicked before).
    /// </summary>
    private int UniverseCardSelection() => OverviewEnter.Target(_s.Selection.Selected, _uniSelected, _uniIndex.ContainsKey);

    /// <summary>F and the card's "Zoom to (F)" in the overview: frame the selected world. False when it is not drawn.</summary>
    public bool ZoomToSelection()
    {
        var id = UniverseCardSelection();
        if (id < 0 || !_uniIndex.ContainsKey(id)) return false;
        SelectInUniverse(id);
        return true;
    }

    /// <summary>Selects a world (the shared selection, so the card shows it) and zooms in on its system.</summary>
    public void SelectInUniverse(int id)
    {
        if (_uniReturn != null || !_uniIndex.TryGetValue(id, out var i)) return;
        _uniSelected = id;
        NoteSelectionForAttention(id); // PlanetWorld.Attention.cs: its neighbourhood unfolds at the next relayout
        _s.Select(id);
        _audio.Select();
        _uniFocusTarget = UniverseOrigin + _uniMotion[i].To; // where it is headed if a relayout is gliding it
        _uniDistanceTarget = Mathf.Clamp(SystemSize(_uniBodies[i].Body) * SelectFrameSystems, SelectMinMetres, SelectMaxMetres);
        _uniFrame = (id, SelectFrameSystems, SelectMinMetres, SelectMaxMetres);
        NoteUniverseControl(UniverseControl.Select);
    }

    /// <summary>The shared selection as the overview last saw it (a change made elsewhere is followed, PLANET-83).</summary>
    private int _uniSharedSeen = -1;

    /// <summary>
    /// Enter (and every commit made while the overview is open): fly into the chosen world (≈ 0.75 s), then land on it. An
    /// Enter that cannot start says why (PLANET-83). The decision is <see cref="OverviewEnter.Decide"/> (Core, unit-tested).
    /// A commit during Esc's way back down takes over from it (PLANET-114: you changed your mind; the camera flies on from
    /// where the way back had got to).
    /// </summary>
    public void BeginUniverseLanding(int id)
    {
        if (_universe == null) return;
        var action = OverviewEnter.Decide(id, _current?.FolderId ?? -1, _uniIndex.ContainsKey, _uniLanding != null);
        if (_uniReturn != null && action != OverviewEnterAction.BackDown && action != OverviewEnterAction.Wait) _uniReturn = null;
        switch (action)
        {
            case OverviewEnterAction.Wait:
                return;
            case OverviewEnterAction.SayChooseFirst:
                _s.Toast("Click a world first, then Enter to land there.");
                return;
            case OverviewEnterAction.TravelOutOfLight:
                // Not drawn (beyond the overview's budget, or hidden): the navigator still goes there, out of the light.
                if (!_s.TravelInto(id)) RefuseLanding(id); // travel: OverviewLanding
                return;
            case OverviewEnterAction.BackDown:
                BeginUniverseReturn(); // where you stand: the way back down, as Esc (a second one hurries it)
                return;
        }

        var i = _uniIndex[id];
        var seconds = _audio.AlignToSixteenth(LandingSeconds, maxShiftSeconds: LandingBeatShiftSeconds); // ends on the beat (AD-42)
        _uniLanding = (id, 0f, seconds);
        _uniFocusTarget = UniverseOrigin + _uniMotion[i].To;
        _uniDistanceTarget = _uniBodies[i].Body.Radius * LandingCloseRadii;
        _uniFrame = null;
        _audio.WindUp(seconds);
    }

    /// <summary>
    /// Lands on a world chosen in the overview: the navigator goes there (history kept, Backspace returns), and you come
    /// down onto it out of the light, as from a warp.
    /// </summary>
    private void LandFromUniverse(int id)
    {
        CloseUniverse(returnToSurface: false);
        _arriveFromSky = true; // travel: OverviewLanding
        _warpFlash = 1f;
        var toastBefore = _s.Hud.ToastText;
        var ok = _s.TravelInto(id); // travel: OverviewLanding
        if (!ok || _phase == Phase.Walking && _transfer == null)
        {
            // Refused (a link that can't be followed) or waiting on a link: come back down where you stood.
            _arriveFromSky = _arriveFromSky && ok;
            if (!ok)
            {
                _warpFlash = 0;
                _audio.Deny();
                SnapCamera();
                if (_s.Hud.ToastText == toastBefore) RefuseLanding(id); // the session did not say why: say it here
            }
        }
    }

    /// <summary>An accepted landing that could not go: says so, with the world's name and why (PLANET-83: never silence).</summary>
    private void RefuseLanding(int id)
    {
        var tree = _s.Tree;
        var name = tree != null && tree.Contains(id) ? tree.DisplayName(id) : "That world";
        var why = tree == null || !tree.Contains(id) ? "it is no longer in the tree"
            : !tree[id].IsDirectory && tree[id].Kind != Core.Model.NodeKind.Link ? "it is a file, not a world"
            : Busy ? "another journey is still under way"
            : "it can't be reached from here";
        _audio.Deny();
        _s.Toast($"Can't land on {name}: {why}.");
        LandingRefusals++;
    }

    /// <summary>Landings from the overview refused with a message (probes).</summary>
    public int LandingRefusals { get; private set; }

    // Overview input from Main._Input (drag and wheel), so the GUI never sees them first.
    private bool UniverseInput(InputEvent e)
    {
        if (_uniReturn != null && e is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.Middle } or InputEventMouseMotion { ButtonMask: MouseButtonMask.Middle })
        {
            return true; // the way back owns the camera (PLANET-111)
        }

        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: false }:
                var wasPanning = _uniPanning;
                _uniPanning = false;
                return wasPanning;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: true } mb when !OverHud(mb.Position):
                _uniPanning = true;
                return true;
            case InputEventMouseMotion mm when _uniPanning:
                var right = _camera.GlobalBasis.X;
                var up = _camera.GlobalBasis.Y;
                _uniFocusTarget -= (right * mm.ScreenRelative.X - up * mm.ScreenRelative.Y) * _uniDistance * PanDistancesPerPixel
                    * OverviewControls.SpeedFactor(OverviewMove.DragPan, mm.ShiftPressed);
                _uniFrame = null;
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel when !OverHud(wheel.Position):
                // Shift: a notch counts as two (PLANET-110).
                // PLANET-192: out as far as the focus's neighbourhood needs (OverviewFraming), never pulled in by a smaller limit.
                _uniDistanceTarget = OverviewFraming.WheelDistance(_uniDistanceTarget, OverviewControls.WheelDistanceFactor(wheel.ButtonIndex == MouseButton.WheelUp, wheel.ShiftPressed), UniverseMaxZoomMetres());
                if (wheel.ButtonIndex == MouseButton.WheelUp) ZoomTowardPointer(wheel.Position, wheel.ShiftPressed); // PlanetWorld.UniverseNav.cs (PLANET-93)
                _uniFrame = null;
                NoteUniverseControl(UniverseControl.Wheel);
                return true;
        }

        return false;
    }

    /// <summary>A left- or right-drag in the overview: orbit round the focus.</summary>
    private void OrbitUniverse(Vector2 screenPixels)
    {
        if (_uniReturn != null) return; // Esc's way back owns the camera (PLANET-111)
        // A drag orbit is direct manipulation: Shift leaves it as it is (OverviewControls.FastDragOrbitFactor).
        var k = OrbitRadiansPerPixel * OverviewControls.SpeedFactor(OverviewMove.DragOrbit, Godot.Input.IsActionPressed(InputBindings.Fast));
        _uniYaw -= screenPixels.X * k;
        _uniPitch = Mathf.Clamp(_uniPitch + screenPixels.Y * k, -MaxPitchRadians, MaxPitchRadians);
    }

    /// <summary>Over a HUD panel or the planet's own list or preview: the GUI gets the event (scrolling a list, say).</summary>
    private bool OverHud(Vector2 p) => _s.Hud.BlockingRects.Any(r => r.HasPoint(p)) || OverOverlay(p);

    // Distant worlds: the folders around the neighbourhood (siblings of the parent's folders, children of the worlds in
    // the sky) drawn far off and small, so the space beyond reads as more of the same tree; never labelled.
    private Node3D? _distantWorlds;

    private void BuildDistantWorlds(FsTree tree, int parentId)
    {
        _distantWorlds?.QueueFree();
        _distantWorlds = null;
        if (_current == null) return;
        var relatives = _children.Keys.SelectMany(id => tree[id].Children);
        if (parentId >= 0 && tree.Contains(parentId)) relatives = relatives.Concat(tree[parentId].Children);
        var ids = relatives.Where(id => tree.Contains(id) && id != _current.FolderId && !_children.ContainsKey(id) && tree[id].IsDirectory && _s.IsVisible(tree[id])).Distinct().Take(120).ToArray();
        if (ids.Length == 0) return;
        _distantWorlds = new Node3D { Name = "DistantWorlds" };
        AddChild(_distantWorlds);
        var directions = PlanetComposer.FibonacciSphere(ids.Length + 7);
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 12, Rings = 6 }, InstanceCount = ids.Length };
        for (var i = 0; i < ids.Length; i++)
        {
            var h = (uint)(ids[i] * 2654435761u);
            var distance = 320f + h % 9 * 70f;            // far off, at many depths
            var size = 2.2f + h % 5 * 0.6f;               // small: their apparent size says "far"
            var d = Gd(directions[i + 7]);
            if (d.Dot(Vector3.Up) > 0.2f) d = (d - Vector3.Up * 0.6f).Normalized(); // mostly round the horizon of space, not overhead
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(size, size, size)), d * distance));
            var c = PlanetArt.Palette(PlanetComposer.ThemeOf(tree, ids[i], _s.IsVisible)).GroundHigh;
            mm.SetInstanceColor(i, c.Darkened(0.25f));
        }

        _distantWorlds.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f, RimEnabled = true, Rim = 0.5f },
        });
    }

    public int DistantWorldCount => _distantWorlds?.GetChild<MultiMeshInstance3D>(0)?.Multimesh?.InstanceCount ?? 0;
}
