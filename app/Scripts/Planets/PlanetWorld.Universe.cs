using FileScape.App.Places;
using FileScape.App.Ui;
using FileScape.App.Visual;
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
/// the navigator, so history and Backspace work as everywhere; Esc or T returns to where you stood.
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

    /// <summary>Far clip in the overview, metres: the largest systems are kilometres across (the surface camera uses <see cref="SurfaceFarMetres"/>).</summary>
    private const float OverviewFarMetres = 30000f;

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

    /// <summary>One wheel notch: in multiplies the distance by 0.85, out by 1.18 (≈ 1/0.85, so in-then-out comes back).</summary>
    private const float WheelZoomIn = 0.85f, WheelZoomOut = 1.18f;

    /// <summary>The closest and farthest the wheel goes, metres.</summary>
    private const float MinZoomMetres = 4f, MaxZoomMetres = 6000f;

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
    private readonly List<(MultiMesh Mesh, List<int> Bodies)> _uniBatches = [];

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
    private float DrawnRadius(UniverseBody b, Vector3 at) => DrawnRadius(b.Radius, at);

    private float DrawnRadius(float radius, Vector3 at)
    {
        var pixelsPerRadian = GetViewport().GetVisibleRect().Size.Y / (2f * MathF.Tan(Mathf.DegToRad(_camera.Fov) / 2f));
        return MathF.Max(radius, _camera.GlobalPosition.DistanceTo(at) * MinDrawnRadiusPixels / pixelsPerRadian);
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
    private MultiMesh? _uniRingMesh, _uniChainMesh, _uniVisitedMesh;
    private MeshInstance3D? _uniHalo;
    private int _uniHere = -1;

    /// <summary>The tree version the overview's layout was made from, and when (app clock).</summary>
    private int _uniLayoutVersion;
    private TimeSpan _uniLayoutAt;

    private Vector3 _uniFocus, _uniFocusTarget;
    private float _uniYaw = StartYawRadians, _uniPitch = StartPitchRadians, _uniDistance, _uniDistanceTarget;
    private int _uniSelected = -1, _uniHovered = -1;
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
            CloseUniverse(returnToSurface: true);
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
        _camera.Far = OverviewFarMetres;
        _warpFlash = OpenFlashLevel; // a soft flash, fading as you pull back from your world
        _s.Hud.SetPlanetsOverview(open: true);
        _titleTime = MathF.Min(_titleTime, 0.3f); // the arrival title fades out of the way (it lingered over the map)
        _audio.Launch();
        _s.Signs.Clear();
        OpenDiscovery(); // PlanetWorld.Discovery.cs (PLANET-76)
        UpdateUniverse(tree, 0f, true);
    }

    /// <summary>
    /// The overview's layout of the tree as it is now. The way to where you stand is always shown, even through a hidden
    /// folder (AppData is hidden on Windows); with <paramref name="keepDrawn"/> every world drawn now is kept too (pinned),
    /// so a relayout only ever adds worlds (PLANET-77): when the budget is full, newly listed folders count as "+N more"
    /// instead of pushing out worlds you were looking at.
    /// </summary>
    private IReadOnlyList<UniverseBody> ComputeUniverse(FsTree tree, bool keepDrawn)
    {
        var onTheWay = new HashSet<int>();
        for (var a = _current!.FolderId; a >= 0 && tree.Contains(a); a = tree[a].ParentId) onTheWay.Add(a);
        var pinned = new HashSet<int>(onTheWay);
        if (keepDrawn)
        {
            foreach (var (b, _) in _uniBodies)
            {
                if (tree.Contains(b.NodeId) && _s.IsVisible(tree[b.NodeId])) pinned.Add(b.NodeId);
            }
        }

        return UniverseLayout.Compute(tree, n => _s.IsVisible(n) || onTheWay.Contains(n.Id), tree.Root.Id, pinned);
    }

    /// <summary>
    /// A system's size for framing, metres: its ring plus a margin (1.3 × the ring radius + the body) or, for a world with
    /// no children, a few body radii (4 ×), so a lone world is not framed as a speck.
    /// </summary>
    private static float SystemSize(UniverseBody b) => MathF.Max(b.Radius * 4f, b.OrbitRadius * 1.3f + b.Radius);

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
        MultiMeshInstance3D Batch(List<int> list, Material mat, Func<UniverseBody, Color> color)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = sphere, InstanceCount = list.Count };
            _uniBatches.Add((mm, list));
            for (var i = 0; i < list.Count; i++) mm.SetInstanceColor(i, color(bodies[list[i]]));
            return new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        }

        var solidMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.8f, RimEnabled = true, Rim = 0.45f, RimTint = 0.6f };
        var ghostMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Back };
        if (solid.Count > 0)
        {
            _universe.AddChild(Batch(solid, solidMat, b =>
            {
                var pal = PlanetArt.Palette(PlanetComposer.ThemeOf(tree, b.NodeId, _s.IsVisible));
                var h = (uint)(b.NodeId * 2654435761u);
                var c = pal.Accent.Lerp(pal.GroundHigh, 0.35f);
                c = Color.FromHsv(Mathf.PosMod(c.H + (h % 100 / 100f - 0.5f) * 0.08f, 1f), Mathf.Clamp(c.S * 1.1f, 0, 1), c.V * 0.95f);
                return b.Discovery == Discovery.Partial ? c.Darkened(0.35f) : c;
            }));
        }

        if (ghosts.Count > 0) _universe.AddChild(Batch(ghosts, ghostMat, _ => new Color(0.7f, 0.72f, 0.95f, 0.22f)));

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

        var lineMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(0.55f, 0.65f, 1f, 0.22f) };
        _uniRingMesh = _uniRingBodies.Count > 0 ? new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = UnitCircleLines(), InstanceCount = _uniRingBodies.Count } : null;
        _uniChainMesh = _uniChains.Count > 0 ? new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = UnitSegmentLine(), InstanceCount = _uniChains.Count } : null;
        foreach (var mm in new[] { _uniRingMesh, _uniChainMesh })
        {
            if (mm != null) _universe.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = lineMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

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

        foreach (var (mm, list) in _uniBatches)
        {
            for (var k = 0; k < list.Count; k++)
            {
                var i = list[k];
                var radius = Mathf.Lerp(_uniMotion[i].RadiusFrom, _uniBodies[i].Body.Radius, e);
                var r = DrawnRadius(radius, _uniBodies[i].At) * AppearScale(i);
                mm.SetInstanceTransform(k, new Transform3D(Basis.Identity.Scaled(new Vector3(r, r, r)), _uniLocal[i]));
            }
        }

        if (_uniRingMesh != null)
        {
            for (var k = 0; k < _uniRingBodies.Count; k++)
            {
                // The ring's plane axes as the layout builds them: a = n × X (or Z when n is nearly along X), c = n × a.
                var i = _uniRingBodies[k];
                var b = _uniBodies[i].Body;
                var n = Gd(b.OrbitNormal);
                var a = n.Cross(MathF.Abs(n.X) < 0.9f ? Vector3.Right : Vector3.Back).Normalized();
                var c = n.Cross(a);
                var r = Mathf.Lerp(_uniMotion[i].OrbitFrom, b.OrbitRadius, e);
                _uniRingMesh.SetInstanceTransform(k, new Transform3D(new Basis(a * r, n * r, c * r), _uniLocal[i]));
            }
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
            }
        }

        if (_uniVisitedMesh != null)
        {
            for (var k = 0; k < _uniVisited.Count; k++)
            {
                var i = _uniVisited[k];
                var r = _uniBodies[i].Body.Radius * AppearScale(i);
                _uniVisitedMesh.SetInstanceTransform(k, new Transform3D(Basis.Identity.Scaled(new Vector3(r, r, r)), _uniLocal[i]));
            }
        }

        if (_uniHalo != null && _uniHere >= 0) _uniHalo.Position = _uniLocal[_uniHere];
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
        _uniRingMesh = _uniChainMesh = _uniVisitedMesh = null;
        _uniHalo = null;
        _uniHere = -1;
        _uniLanding = null;
        _uniFrame = null;
        CloseDiscovery(); // PlanetWorld.Discovery.cs
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
                LandFromUniverse(landing.Id);
                return;
            }
        }

        if (!blockedKeys && _uniLanding == null)
        {
            var right = new Vector3(Mathf.Cos(_uniYaw), 0, -Mathf.Sin(_uniYaw));
            var forward = Vector3.Up.Cross(right);
            _uniYaw -= Axis(InputBindings.TurnLeft, InputBindings.TurnRight) * KeyLookDegreesPerSecond * Mathf.Pi / 180f * dt; // ← → orbit (AD-47)
            var move = right * Axis(InputBindings.Left, InputBindings.Right)
                + forward * Axis(InputBindings.Back, InputBindings.Forward)
                + Vector3.Up * (Axis(InputBindings.Down, InputBindings.Up));
            if (move.LengthSquared() > 0.01f)
            {
                _uniFocusTarget += move.LimitLength() * _uniDistance * KeyPanDistancesPerSecond * dt;
                _uniFrame = null; // the player took the camera: growth no longer reframes it
            }
        }

        // The flash follows the landing's progress (0 → 1 over the flight) and otherwise fades to 0; squaring the level
        // keeps it faint early and brightens it late, so the light seems to rush up as you arrive.
        _warpFlash = Mathf.MoveToward(_warpFlash, _uniLanding is { } ul ? Mathf.Clamp(ul.T / ul.Seconds, 0, 1) : 0, dt * FlashRatePerSecond);
        _flash.Color = FlashColor with { A = Mathf.Clamp(_warpFlash * _warpFlash * FlashPeakOpacity, 0, FlashPeakOpacity) };
        var ease = 1 - Mathf.Exp(-dt * FocusFollowRatePerSecond); // frame-rate independent smoothing (see the constants above)
        _uniFocus = _uniFocus.Lerp(_uniFocusTarget, ease);
        _uniDistance = Mathf.Lerp(_uniDistance, _uniDistanceTarget, 1 - Mathf.Exp(-dt * ZoomFollowRatePerSecond));
        var offset = new Vector3(Mathf.Sin(_uniYaw) * Mathf.Cos(_uniPitch), Mathf.Sin(_uniPitch), Mathf.Cos(_uniYaw) * Mathf.Cos(_uniPitch));
        _camera.GlobalPosition = _uniFocus + offset * _uniDistance;
        _camera.LookAt(_uniFocus, Vector3.Up);
        _camera.Fov = OverviewFovDegrees;
        StepDiscovery(tree); // PlanetWorld.Discovery.cs: ask for more listings, relayout when the tree has grown
        PlaceUniverse(dt);

        var mouse = GetViewport().GetMousePosition();
        _uniHovered = !_orbiting && !_uniPanning && !_s.Hud.BlockingRects.Any(r => r.HasPoint(mouse)) ? PickUniverse(mouse) : -1;
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
        var screenUp = _camera.GlobalBasis.Y;
        var nearby = _uniBodies
            .Select(b => (b, d: b.At.DistanceTo(camPos), look: camFwd.Dot((b.At - camPos).Normalized())))
            .Where(x => x.look > 0.94f && x.d < _uniDistance * 1.6f)
            .OrderBy(x => x.d)
            .Take(10)
            .Select(x => x.b.Body.NodeId)
            .ToHashSet();
        foreach (var (b, at) in _uniBodies)
        {
            var id = b.NodeId;
            var here = id == _current?.FolderId;
            var focus = id == _uniSelected || id == _uniHovered || here;
            if (!focus && b.ParentId >= 0 && !nearby.Contains(id)) continue;
            var n = tree[id];
            var name = (here ? "★ " : "") + tree.DisplayName(id);
            var detail = here ? "you are here" : b.Discovery switch
            {
                Discovery.Unknown => Listing.Describe(Listing.Of(n)),
                Discovery.Partial => "partly listed",
                _ => FolderSummary.Gate(n, Listing.Of(n)),
            };
            if (b.HiddenChildren > 0) detail += $" · +{b.HiddenChildren} more folders";
            if (_s.Visited(id) && !here) detail += " · visited";
            var color = here ? new Color(1f, 0.85f, 0.45f) : b.Discovery == Discovery.Unknown ? new Color(0.75f, 0.78f, 0.95f) : new Color(1f, 0.95f, 0.8f);
            _signs.Add(new SignCandidate(id, at + screenUp * (DrawnRadius(b, at) * 1.15f), name, detail, color, FileIcons.For(n), focus, here ? 30f : 0f, FixedAnchor: true));
        }

        _s.Signs.Layout(_camera, _signs, _s.Hud.BlockingRects);
        var selName = _uniSelected >= 0 && tree.Contains(_uniSelected) ? tree.DisplayName(_uniSelected) : "";
        var (listed, partial, unknown, visitedCount) = UniverseCounts;
        SetPrompt(_uniSelected == _current?.FolderId ? $"★ {selName}: you are here · click a world, then Enter to land there" : $"{selName} · Enter (or double-click) to land there",
            $"{_uniBodies.Count:N0} known folders: {listed:N0} listed (solid) · {partial:N0} partly · {unknown:N0} not listed yet (pale) · {visitedCount:N0} visited (ringed)   —   drag or ← → orbit · middle-drag or WASD pan · wheel zoom · X: pause discovery · Esc or T: back");
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
        foreach (var (b, at) in _uniBodies)
        {
            if (_camera.IsPositionBehind(at)) continue;
            var c = _camera.UnprojectPosition(at);
            var px = c.DistanceTo(_camera.UnprojectPosition(at + right * DrawnRadius(b, at)));
            var d = c.DistanceTo(screen);
            if (d > MathF.Max(px * 1.25f, PickMinPixels) || d >= best) continue;
            best = d;
            id = b.NodeId;
        }

        return id;
    }

    /// <summary>A click this close to a world's centre picks it however small it is drawn, pixels.</summary>
    private const float PickMinPixels = 8f;

    /// <summary>Input in the overview (from Main's unhandled input): keys and clicks. Returns true when consumed.</summary>
    private bool UniverseCommand(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } key when key.IsAction(InputBindings.FlightToggle) || key.Keycode == Key.Escape:
                CloseUniverse(returnToSurface: true);
                return true;
            case InputEventKey { Pressed: true, Echo: false } key when key.Keycode is Key.Enter or Key.KpEnter || key.IsAction(InputBindings.EnterFolder):
                BeginUniverseLanding(_uniSelected);
                return true;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Home, CtrlPressed: false }:
                if (_current != null) SelectInUniverse(_current.FolderId);
                return true;
            case InputEventKey { Pressed: true, Echo: false } f when f.IsAction(InputBindings.Frame):
                if (_uniSelected >= 0) SelectInUniverse(_uniSelected); // F frames the selected world again
                return true;
            case InputEventKey { Pressed: true, Echo: false } b when b.IsAction(InputBindings.Overview):
                return true; // B (the overlook view) means nothing up here
            case InputEventKey { Pressed: true, Echo: false } x when x.IsAction(InputBindings.DiscoveryPause):
                ToggleDiscoveryPause(); // PlanetWorld.Discovery.cs (PLANET-76)
                return true;
            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.N, CtrlPressed: false }:
                RequestSoundToggle(); // PlanetWorld.Sound.cs: the same control as the toolbar's sound button
                return true;
        }

        return false; // Backspace, J, L, Ctrl+L…: the shared commands (a navigation change lands from the overview)
    }

    /// <summary>Selects a world (the shared selection, so the card shows it) and zooms in on its system.</summary>
    public void SelectInUniverse(int id)
    {
        if (!_uniIndex.TryGetValue(id, out var i)) return;
        _uniSelected = id;
        _s.Select(id);
        _audio.Select();
        _uniFocusTarget = UniverseOrigin + _uniMotion[i].To; // where it is headed if a relayout is gliding it
        _uniDistanceTarget = Mathf.Clamp(SystemSize(_uniBodies[i].Body) * SelectFrameSystems, SelectMinMetres, SelectMaxMetres);
        _uniFrame = (id, SelectFrameSystems, SelectMinMetres, SelectMaxMetres);
    }

    /// <summary>The shared selection as the overview last saw it (a change made elsewhere is followed, PLANET-83).</summary>
    private int _uniSharedSeen = -1;

    /// <summary>Enter: fly into the chosen world (≈ 0.75 s), then land on it. An Enter that cannot start says why (PLANET-83).</summary>
    public void BeginUniverseLanding(int id)
    {
        if (_universe == null || _uniLanding != null) return;
        if (id < 0)
        {
            _s.Toast("Click a world first, then Enter to land there.");
            return;
        }

        if (!_uniIndex.TryGetValue(id, out var i))
        {
            // Not drawn (beyond the overview's budget, or hidden): the navigator still goes there, out of the light.
            if (!_s.TravelInto(id)) RefuseLanding(id);
            return;
        }

        if (id == _current?.FolderId)
        {
            CloseUniverse(returnToSurface: true);
            return;
        }

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
        _arriveFromSky = true;
        _warpFlash = 1f;
        var toastBefore = _s.Hud.ToastText;
        var ok = _s.TravelInto(id);
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
                _uniFocusTarget -= (right * mm.ScreenRelative.X - up * mm.ScreenRelative.Y) * _uniDistance * PanDistancesPerPixel;
                _uniFrame = null;
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel when !OverHud(wheel.Position):
                _uniDistanceTarget = Mathf.Clamp(_uniDistanceTarget * (wheel.ButtonIndex == MouseButton.WheelUp ? WheelZoomIn : WheelZoomOut), MinZoomMetres, MaxZoomMetres);
                _uniFrame = null;
                return true;
        }

        return false;
    }

    /// <summary>A left- or right-drag in the overview: orbit round the focus.</summary>
    private void OrbitUniverse(Vector2 screenPixels)
    {
        _uniYaw -= screenPixels.X * OrbitRadiansPerPixel;
        _uniPitch = Mathf.Clamp(_uniPitch + screenPixels.Y * OrbitRadiansPerPixel, -MaxPitchRadians, MaxPitchRadians);
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
