using FileScape.App.Places;
using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The overview (PLANET-19, 2026-11-12; DJ: "a Universe Sandbox–style overview"): T (or the toolbar) zooms out from
/// where you stand to the whole known folder hierarchy in 3D, laid out as nested star systems
/// (<see cref="UniverseLayout"/>): a folder's subfolders orbit it on one tilted ring. Listed folders are solid worlds,
/// partly listed ones dimmer, folders known only by name are pale ghosts; folders you have visited this session wear a
/// ring; where you stand has a gold beacon. Right-drag orbits, middle-drag or WASD pans, the wheel zooms; click a world
/// to select it (the shared selection: its card shows it) and zoom in on it; Enter or a double-click lands there, through
/// the navigator, so history and Backspace work as everywhere; Esc or T returns to where you stood.
/// </summary>
public partial class PlanetWorld
{
    /// <summary>Far from every planet (the overview is drawn in the same scene, out of their way).</summary>
    private static readonly Vector3 UniverseOrigin = new(0, 40000, 0);

    private Node3D? _universe;
    private readonly List<(UniverseBody Body, Vector3 At)> _uniBodies = [];
    private readonly Dictionary<int, int> _uniIndex = [];
    private Vector3 _uniFocus, _uniFocusTarget;
    private float _uniYaw = 0.6f, _uniPitch = 0.55f, _uniDistance, _uniDistanceTarget;
    private int _uniSelected = -1, _uniHovered = -1;
    private bool _uniPanning;
    private (int Id, float T)? _uniLanding;
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
        var bodies = UniverseLayout.Compute(tree, n => _s.IsVisible(n) || onTheWay.Contains(n.Id), tree.Root.Id);
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
        _uniDistance = here.Body.Radius * 3f;
        _uniDistanceTarget = Mathf.Clamp(SystemSize(here.Body) * 2.6f, 30f, 3000f);
        _camera.Far = 30000f;
        _warpFlash = 0.75f; // a soft flash, fading as you pull back from your world
        _s.Hud.SetPlanetsOverview(open: true);
        _audio.Launch();
        _s.Signs.Clear();
        UpdateUniverse(tree, 0f, true);
    }

    private static float SystemSize(UniverseBody b) => MathF.Max(b.Radius * 4f, b.OrbitRadius * 1.3f + b.Radius);

    private void BuildUniverseScene(FsTree tree, IReadOnlyList<UniverseBody> bodies)
    {
        var sphere = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 24, Rings = 12 };
        var solid = bodies.Where(b => b.Discovery != Discovery.Unknown).ToList();
        var ghosts = bodies.Where(b => b.Discovery == Discovery.Unknown).ToList();
        MultiMeshInstance3D Batch(List<UniverseBody> list, Material mat, Func<UniverseBody, Color> color)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = sphere, InstanceCount = list.Count };
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
        _uniSelectRing?.QueueFree();
        _uniSelectRing = null;
        _uniBodies.Clear();
        _uniIndex.Clear();
        _uniLanding = null;
        _camera.Far = 4000f;
        _s.Signs.Clear();
        _s.Hud.SetPlanetsOverview(open: false);
        SetPrompt("");
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
            _uniLanding = (landing.Id, t);
            if (t >= 0.75f)
            {
                LandFromUniverse(landing.Id);
                return;
            }
        }

        if (!blockedKeys && _uniLanding == null)
        {
            var right = new Vector3(Mathf.Cos(_uniYaw), 0, -Mathf.Sin(_uniYaw));
            var forward = Vector3.Up.Cross(right);
            var move = right * (Axis(InputBindings.Left, InputBindings.Right) + Axis(InputBindings.TurnLeft, InputBindings.TurnRight))
                + forward * Axis(InputBindings.Back, InputBindings.Forward)
                + Vector3.Up * (Axis(InputBindings.Down, InputBindings.Up));
            if (move.LengthSquared() > 0.01f) _uniFocusTarget += move.LimitLength() * _uniDistance * 0.9f * dt;
        }

        _warpFlash = Mathf.MoveToward(_warpFlash, _uniLanding != null ? Mathf.Clamp(_uniLanding.Value.T / 0.75f, 0, 1) : 0, dt * 1.6f);
        _flash.Color = new Color(1f, 0.95f, 0.86f, Mathf.Clamp(_warpFlash * _warpFlash * 0.85f, 0, 0.85f));
        var ease = 1 - Mathf.Exp(-dt * 6f);
        _uniFocus = _uniFocus.Lerp(_uniFocusTarget, ease);
        _uniDistance = Mathf.Lerp(_uniDistance, _uniDistanceTarget, 1 - Mathf.Exp(-dt * 3.5f));
        var offset = new Vector3(Mathf.Sin(_uniYaw) * Mathf.Cos(_uniPitch), Mathf.Sin(_uniPitch), Mathf.Cos(_uniYaw) * Mathf.Cos(_uniPitch));
        _camera.GlobalPosition = _uniFocus + offset * _uniDistance;
        _camera.LookAt(_uniFocus, Vector3.Up);
        _camera.Fov = 62f;

        var mouse = GetViewport().GetMousePosition();
        _uniHovered = !_orbiting && !_uniPanning && !_s.Hud.BlockingRects.Any(r => r.HasPoint(mouse)) ? PickUniverse(mouse) : -1;
        if (_uniSelectRing != null)
        {
            var sel = _uniIndex.TryGetValue(_uniSelected, out var si) ? _uniBodies[si] : default;
            _uniSelectRing.Visible = si >= 0 && _uniSelected >= 0;
            if (_uniSelectRing.Visible)
            {
                var toCam = (_camera.GlobalPosition - sel.At).Normalized();
                var r = sel.Body.Radius;
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
            _signs.Add(new SignCandidate(id, at + screenUp * (b.Radius * 1.15f), name, detail, color, FileIcons.For(n), focus, here ? 30f : 0f, FixedAnchor: true));
        }

        _s.Signs.Layout(_camera, _signs, _s.Hud.BlockingRects);
        var selName = _uniSelected >= 0 && tree.Contains(_uniSelected) ? tree.DisplayName(_uniSelected) : "";
        var (listed, partial, unknown, visitedCount) = UniverseCounts;
        SetPrompt(_uniSelected == _current?.FolderId ? $"★ {selName}: you are here · click a world, then Enter to land there" : $"{selName} · Enter (or double-click) to land there",
            $"{_uniBodies.Count:N0} known folders: {listed:N0} listed (solid) · {partial:N0} partly · {unknown:N0} not listed yet (pale) · {visitedCount:N0} visited (ringed)   —   right-drag orbit · middle-drag or WASD pan · wheel zoom · Esc or T: back");
    }

    private int PickUniverse(Vector2 screen)
    {
        var eye = _camera.ProjectRayOrigin(screen);
        var ray = _camera.ProjectRayNormal(screen);
        var best = float.PositiveInfinity;
        var id = -1;
        foreach (var (b, at) in _uniBodies)
        {
            // A little larger than the body, so small far worlds can be clicked.
            var r = MathF.Max(b.Radius * 1.25f, eye.DistanceTo(at) * 0.012f);
            if (RayPlanet(eye - at, ray, r) is not { } hit || hit >= best) continue;
            best = hit;
            id = b.NodeId;
        }

        return id;
    }

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
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Home }:
                if (_current != null) SelectInUniverse(_current.FolderId);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                var id = PickUniverse(click.Position);
                if (id < 0) return true;
                SelectInUniverse(id);
                if (click.DoubleClick) BeginUniverseLanding(id);
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
        _uniDistanceTarget = Mathf.Clamp(SystemSize(_uniBodies[i].Body) * 2.4f, 12f, 3000f);
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

        _uniLanding = (id, 0f);
        _uniFocusTarget = _uniBodies[i].At;
        _uniDistanceTarget = _uniBodies[i].Body.Radius * 1.6f;
        _audio.WindUp(0.75f);
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
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mb:
                _uniPanning = mb.Pressed;
                return true;
            case InputEventMouseMotion mm when _uniPanning:
                var right = _camera.GlobalBasis.X;
                var up = _camera.GlobalBasis.Y;
                _uniFocusTarget -= (right * mm.ScreenRelative.X - up * mm.ScreenRelative.Y) * _uniDistance * 0.0016f;
                return true;
            case InputEventMouseMotion mm when _orbiting:
                _uniYaw -= mm.ScreenRelative.X * 0.005f;
                _uniPitch = Mathf.Clamp(_uniPitch + mm.ScreenRelative.Y * 0.005f, -1.45f, 1.45f);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                _uniDistanceTarget = Mathf.Clamp(_uniDistanceTarget * (wheel.ButtonIndex == MouseButton.WheelUp ? 0.85f : 1.18f), 4f, 6000f);
                return true;
        }

        return false;
    }

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
