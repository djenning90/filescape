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

    /// <summary>The drawn bodies' batches and the bodies in each, in instance order (re-scaled every frame, see DrawnRadius).</summary>
    private readonly List<(MultiMesh Mesh, List<UniverseBody> Bodies)> _uniBatches = [];

    /// <summary>
    /// The smallest a world is drawn in the overview, pixels of radius (FOLLOW-1, 2026-11-17): a world is 1.2–4 m across
    /// but its children's rings are hundreds of metres, so framing a grown system (the Starter Sky's is ≈ 480 m) left every
    /// world 2–5 px wide and the overview looked empty. Far worlds keep this apparent size, like markers in Universe
    /// Sandbox; close up they are their real size. 6 px radius ≈ a 12 px disc: clearly a world, still smaller than its name.
    /// </summary>
    private const float MinDrawnRadiusPixels = 6f;

    /// <summary>
    /// A body's drawn radius now, metres: its real radius, or at least <see cref="MinDrawnRadiusPixels"/> on screen at its
    /// distance from the camera (pixels per radian = view height / (2 tan(fov/2))).
    /// </summary>
    private float DrawnRadius(UniverseBody b, Vector3 at)
    {
        var pixelsPerRadian = GetViewport().GetVisibleRect().Size.Y / (2f * MathF.Tan(Mathf.DegToRad(_camera.Fov) / 2f));
        return MathF.Max(b.Radius, _camera.GlobalPosition.DistanceTo(at) * MinDrawnRadiusPixels / pixelsPerRadian);
    }

    /// <summary>Re-scales every drawn body to <see cref="DrawnRadius"/> (≤ 1,500 instance transforms a frame).</summary>
    private void RescaleUniverseBodies()
    {
        foreach (var (mm, list) in _uniBatches)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var b = list[i];
                var r = DrawnRadius(b, UniverseOrigin + Gd(b.Position));
                mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(r, r, r)), Gd(b.Position)));
            }
        }
    }
    private readonly List<(UniverseBody Body, Vector3 At)> _uniBodies = [];
    private readonly Dictionary<int, int> _uniIndex = [];
    private Vector3 _uniFocus, _uniFocusTarget;
    private float _uniYaw = StartYawRadians, _uniPitch = StartPitchRadians, _uniDistance, _uniDistanceTarget;
    private int _uniSelected = -1, _uniHovered = -1;
    private bool _uniPanning;
    private (int Id, float T, float Seconds)? _uniLanding;
    private MeshInstance3D? _uniSelectRing;
    private bool _arriveFromSky;

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
        // The way to where you stand is always shown, even through a hidden folder (AppData is hidden on Windows).
        var onTheWay = new HashSet<int>();
        for (var a = _current.FolderId; a >= 0 && tree.Contains(a); a = tree[a].ParentId) onTheWay.Add(a);
        var bodies = UniverseLayout.Compute(tree, n => _s.IsVisible(n) || onTheWay.Contains(n.Id), tree.Root.Id, onTheWay);
        if (bodies.Count == 0) return;
        _universe = new Node3D { Name = "Universe", Position = UniverseOrigin };
        AddChild(_universe);
        _uniBodies.Clear();
        _uniIndex.Clear();
        foreach (var b in bodies)
        {
            _uniIndex[b.NodeId] = _uniBodies.Count;
            _uniBodies.Add((b, UniverseOrigin + Gd(b.Position)));
        }

        BuildUniverseScene(tree, bodies);
        // Start close to where you stand and pull back: the zoom out is the transition.
        var here = _uniIndex.TryGetValue(_current.FolderId, out var hi) ? _uniBodies[hi] : _uniBodies[0];
        _uniSelected = here.Body.NodeId;
        _uniFocus = _uniFocusTarget = here.At;
        _uniDistance = here.Body.Radius * OpenStartRadii;
        _uniDistanceTarget = Mathf.Clamp(SystemSize(here.Body) * OpenFrameSystems, OpenMinMetres, OpenMaxMetres);
        _camera.Far = OverviewFarMetres;
        _warpFlash = OpenFlashLevel; // a soft flash, fading as you pull back from your world
        _s.Hud.SetPlanetsOverview(open: true);
        _titleTime = MathF.Min(_titleTime, 0.3f); // the arrival title fades out of the way (it lingered over the map)
        _audio.Launch();
        _s.Signs.Clear();
        UpdateUniverse(tree, 0f, true);
    }

    /// <summary>
    /// A system's size for framing, metres: its ring plus a margin (1.3 × the ring radius + the body) or, for a world with
    /// no children, a few body radii (4 ×), so a lone world is not framed as a speck.
    /// </summary>
    private static float SystemSize(UniverseBody b) => MathF.Max(b.Radius * 4f, b.OrbitRadius * 1.3f + b.Radius);

    private void BuildUniverseScene(FsTree tree, IReadOnlyList<UniverseBody> bodies)
    {
        var sphere = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 24, Rings = 12 };
        var solid = bodies.Where(b => b.Discovery != Discovery.Unknown).ToList();
        var ghosts = bodies.Where(b => b.Discovery == Discovery.Unknown).ToList();
        _uniBatches.Clear();
        MultiMeshInstance3D Batch(List<UniverseBody> list, Material mat, Func<UniverseBody, Color> color)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = sphere, InstanceCount = list.Count };
            _uniBatches.Add((mm, list));
            for (var i = 0; i < list.Count; i++)
            {
                var r = list[i].Radius;
                mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(r, r, r)), Gd(list[i].Position)));
                mm.SetInstanceColor(i, color(list[i]));
            }

            return new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        }

        var solidMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.8f, RimEnabled = true, Rim = 0.45f, RimTint = 0.6f };
        var ghostMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Back };
        if (solid.Count > 0)
        {
            _universe!.AddChild(Batch(solid, solidMat, b =>
            {
                var pal = PlanetArt.Palette(PlanetComposer.ThemeOf(tree, b.NodeId, _s.IsVisible));
                var h = (uint)(b.NodeId * 2654435761u);
                var c = pal.Accent.Lerp(pal.GroundHigh, 0.35f);
                c = Color.FromHsv(Mathf.PosMod(c.H + (h % 100 / 100f - 0.5f) * 0.08f, 1f), Mathf.Clamp(c.S * 1.1f, 0, 1), c.V * 0.95f);
                return b.Discovery == Discovery.Partial ? c.Darkened(0.35f) : c;
            }));
        }

        if (ghosts.Count > 0) _universe!.AddChild(Batch(ghosts, ghostMat, _ => new Color(0.7f, 0.72f, 0.95f, 0.22f)));

        // Orbits: one faint ring per system with children, in its tilted plane.
        var lines = new SurfaceTool();
        lines.Begin(Mesh.PrimitiveType.Lines);
        var any = false;
        var byId = bodies.ToDictionary(b => b.NodeId);
        // A folder with a single subfolder is a chain, not a system: a short straight link, not a ring across the sky.
        foreach (var b in bodies.Where(b => b.ParentId >= 0 && byId.ContainsKey(b.ParentId) && bodies.Count(o => o.ParentId == b.ParentId) == 1))
        {
            lines.AddVertex(Gd(byId[b.ParentId].Position));
            lines.AddVertex(Gd(b.Position));
            any = true;
        }

        foreach (var b in bodies.Where(b => b.OrbitRadius > 0 && bodies.Count(o => o.ParentId == b.NodeId) >= 2))
        {
            var n = Gd(b.OrbitNormal);
            var a = n.Cross(MathF.Abs(n.X) < 0.9f ? Vector3.Right : Vector3.Back).Normalized();
            var c = n.Cross(a);
            const int seg = 64;
            for (var i = 0; i < seg; i++)
            {
                var t0 = Mathf.Tau * i / seg;
                var t1 = Mathf.Tau * (i + 1) / seg;
                lines.AddVertex(Gd(b.Position) + (a * Mathf.Cos(t0) + c * Mathf.Sin(t0)) * b.OrbitRadius);
                lines.AddVertex(Gd(b.Position) + (a * Mathf.Cos(t1) + c * Mathf.Sin(t1)) * b.OrbitRadius);
            }

            any = true;
        }

        if (any)
        {
            _universe!.AddChild(new MeshInstance3D
            {
                Mesh = lines.Commit(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(0.55f, 0.65f, 1f, 0.22f) },
            });
        }

        // Visited this session: a ring round the world. Where you stand: a gold beacon.
        var visited = bodies.Where(b => _s.Visited(b.NodeId)).ToList();
        if (visited.Count > 0)
        {
            var torus = new TorusMesh { InnerRadius = 1.35f, OuterRadius = 1.5f, Rings = 32, RingSegments = 4 };
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = torus, InstanceCount = visited.Count };
            for (var i = 0; i < visited.Count; i++)
            {
                var r = visited[i].Radius;
                mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(r, r, r)), Gd(visited[i].Position)));
            }

            _universe!.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = PlanetArt.GlowMaterial(new Color(0.75f, 0.95f, 1f), 1.4f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        if (_uniIndex.TryGetValue(_current!.FolderId, out var ci))
        {
            var here = _uniBodies[ci].Body;
            var halo = PlanetArt.Halo(new Color(1f, 0.82f, 0.4f), here.Radius * 3.2f, 1.4f);
            halo.Position = Gd(here.Position);
            _universe!.AddChild(halo);
        }

        _uniSelectRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 1.6f, OuterRadius = 1.75f, Rings = 48, RingSegments = 4 },
            MaterialOverride = PlanetArt.GlowMaterial(new Color(1f, 0.92f, 0.55f), 2.2f, pulse: 1f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_uniSelectRing);
        UniverseCounts = (bodies.Count(b => b.Discovery == Discovery.Listed), bodies.Count(b => b.Discovery == Discovery.Partial), bodies.Count(b => b.Discovery == Discovery.Unknown), visited.Count);
    }

    /// <summary>Leaves the overview; <paramref name="returnToSurface"/> glides the camera back down to where you stood.</summary>
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
        _uniLanding = null;
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
            if (move.LengthSquared() > 0.01f) _uniFocusTarget += move.LimitLength() * _uniDistance * KeyPanDistancesPerSecond * dt;
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
        RescaleUniverseBodies();

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
            $"{_uniBodies.Count:N0} known folders: {listed:N0} listed (solid) · {partial:N0} partly · {unknown:N0} not listed yet (pale) · {visitedCount:N0} visited (ringed)   —   drag or ← → orbit · middle-drag or WASD pan · wheel zoom · Esc or T: back");
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
        _uniFocusTarget = _uniBodies[i].At;
        _uniDistanceTarget = Mathf.Clamp(SystemSize(_uniBodies[i].Body) * SelectFrameSystems, SelectMinMetres, SelectMaxMetres);
    }

    /// <summary>Enter: fly into the chosen world (≈ 0.75 s), then land on it.</summary>
    public void BeginUniverseLanding(int id)
    {
        if (_universe == null || id < 0 || _uniLanding != null || !_uniIndex.TryGetValue(id, out var i)) return;
        if (id == _current?.FolderId)
        {
            CloseUniverse(returnToSurface: true);
            return;
        }

        var seconds = _audio.AlignToSixteenth(LandingSeconds, maxShiftSeconds: LandingBeatShiftSeconds); // ends on the beat (AD-42)
        _uniLanding = (id, 0f, seconds);
        _uniFocusTarget = _uniBodies[i].At;
        _uniDistanceTarget = _uniBodies[i].Body.Radius * LandingCloseRadii;
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
            }
        }
    }

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
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel when !OverHud(wheel.Position):
                _uniDistanceTarget = Mathf.Clamp(_uniDistanceTarget * (wheel.ButtonIndex == MouseButton.WheelUp ? WheelZoomIn : WheelZoomOut), MinZoomMetres, MaxZoomMetres);
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
