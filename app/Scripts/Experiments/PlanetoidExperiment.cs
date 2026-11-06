using FileScape.App.Places;
using FileScape.App.Visual;
using FileScape.Core.Experiments;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Scanning;
using FileScape.Core.Units;
using Godot;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Experiments;

/// <summary>
/// EXPERIMENT (PLANET-02; --experiment=planetoids): two small original worlds and a traveller who walks on them with the
/// unchanged FileScape walker (run in each body's chart, <see cref="PlanetoidTraveller"/>) and flies between them from a
/// launch pad. It exists to test the feasibility claims in docs/planetoid-feasibility.md; it is not part of FileScape's
/// views. W/S walk, A/D or ←/→ turn, Space jump (on the glowing pad: launch), Esc quits.
/// <para>
/// 2026-11-06 (PLANET-03/04): the teal world carries a real composed folder-place — an original small folder, "Orchard",
/// composed by the unchanged <see cref="PlaceComposer"/> (compact layout) — drawn on the sphere through the chart, with
/// gate names, file towers coloured by type, and picking (click: the ray is marched in world space and tested in the
/// chart). V toggles first person; right-drag looks. The pad launches a fast guided transfer
/// (<see cref="PlanetoidTraveller.LaunchGuided"/>) instead of the slow ballistic arc.
/// </para>
/// With --automate=planetoids it walks, jumps, looks, picks, launches, lands, takes screenshots and quits.
/// </summary>
public partial class PlanetoidExperiment : Node3D
{
    private readonly bool _automate;
    private readonly string _out;
    private readonly List<Planetoid> _bodies = [];
    private readonly List<PlaceTerrain> _terrains = [];
    private readonly List<(int Body, NVector2 Chart, float Radius, NVector3 LaunchVelocity)> _pads = [];
    private PlanetoidTraveller _traveller = null!;
    private Camera3D _camera = null!;
    private MeshInstance3D _avatar = null!;
    private Label _label = null!;
    private Vector3 _camPos;
    private bool _jumpQueued;
    private readonly System.Text.StringBuilder _log = new();
    private int _failures;
    private FsTree _orchard = null!;
    private PlaceComposition _place = null!;
    private bool _firstPerson;
    private float _lookPitch;
    private float _chasePitch = 20f;
    private float _landKick;
    private int _lastTransfers;
    private string _picked = "";
    private float _launchFov;

    /// <summary>The teal world's radius: large enough that the composed place stays within ≈ 35° of the pole.</summary>
    private const float TealRadius = 48f;

    public PlanetoidExperiment(bool automate, string outDir)
    {
        _automate = automate;
        _out = outDir;
        Name = "PlanetoidExperiment";
    }

    public override void _Ready()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial { SkyTopColor = new Color(0.02f, 0.02f, 0.07f), SkyHorizonColor = new Color(0.12f, 0.08f, 0.25f), GroundBottomColor = new Color(0.02f, 0.02f, 0.05f), GroundHorizonColor = new Color(0.1f, 0.07f, 0.2f) } },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.6f,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            GlowEnabled = true,
            GlowIntensity = 0.8f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, 30, 0), LightEnergy = 1.1f, LightColor = new Color(1f, 0.92f, 0.85f), ShadowEnabled = true });

        // Two original worlds: a teal garden world, and a smaller coral one with a ring.
        _orchard = Orchard();
        _place = PlaceComposer.Compose(_orchard, 0, PlaceParams.Compact);
        AddBody(new Planetoid(NVector3.Zero, TealRadius, 6f, NVector3.UnitY, -NVector3.UnitZ), new Color(0.1f, 0.38f, 0.36f), new Color(0.04f, 0.18f, 0.22f), new Color(0.3f, 1f, 0.85f), ring: false, place: _place);
        AddBody(new Planetoid(new NVector3(0, 30, -190), 32f, 6f, NVector3.Normalize(new NVector3(0, 0.6f, 1)), NVector3.Normalize(new NVector3(0, 1, -0.6f))), new Color(0.45f, 0.16f, 0.22f), new Color(0.22f, 0.07f, 0.14f), new Color(1f, 0.55f, 0.6f), ring: true);
        _traveller = new PlanetoidTraveller(_bodies, _terrains);
        var arrival = _place.Arrival;
        _traveller.PlaceOn(0, new NVector2(arrival.Eye.X, arrival.Eye.Z), Degrees.Of(arrival.Yaw.Value));
        BuildPlace(_bodies[0], _place);

        // A launch pad on each world, aimed at the other; its speed comes from simulating the arc (Core).
        for (var i = 0; i < 2; i++)
        {
            var b = _bodies[i];
            var other = _bodies[1 - i];
            // On the teal world the pad is behind the arrival terrace, in the clear corridor; on the coral world, by its pole.
            var chart = i == 0 ? new NVector2(0, 12.5f) : new NVector2(0, -18);
            var at = b.ToWorld(chart);
            var up = NVector3.Normalize(at - b.Center);
            var dir = NVector3.Normalize(NVector3.Normalize(other.Center - at) + up * 0.6f);
            var speed = PlanetoidTraveller.LaunchSpeed(_bodies, at, dir, 1 - i) ?? 30f;
            _pads.Add((i, chart, 2.2f, dir * speed));
            var (u, n, e) = b.FrameAt(chart);
            var pad = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 2.2f, BottomRadius = 2.4f, Height = 0.25f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.15f), EmissionEnabled = true, Emission = new Color(1f, 0.9f, 0.4f), EmissionEnergyMultiplier = 2.2f },
            };
            pad.Transform = new Transform3D(new Basis(Gd(e), Gd(u), Gd(-n)), Gd(at + u * 0.1f));
            AddChild(pad);
        }

        _avatar = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.95f, 0.8f), EmissionEnabled = true, Emission = new Color(1f, 0.85f, 0.5f), EmissionEnergyMultiplier = 0.6f },
        };
        AddChild(_avatar);
        _camera = new Camera3D { Fov = 60, Near = 0.1f, Far = 5000, Current = true };
        AddChild(_camera);
        var layer = new CanvasLayer();
        AddChild(layer);
        _label = new Label { Position = new Vector2(16, 12) };
        _label.AddThemeFontSizeOverride("font_size", 16);
        _label.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.6f));
        _label.AddThemeConstantOverride("outline_size", 5);
        _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        layer.AddChild(_label);
        _camPos = Gd(_traveller.Position) + new Vector3(0, 6, 10);
        if (_automate) _ = Script();
    }

    private void AddBody(Planetoid b, Color a, Color c, Color glow, bool ring, PlaceComposition? place = null)
    {
        _bodies.Add(b);
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/planetoid.gdshader") };
        mat.SetShaderParameter("base_a", a);
        mat.SetShaderParameter("base_b", c);
        mat.SetShaderParameter("glow", glow);
        mat.SetShaderParameter("radius", b.Radius);
        AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = b.Radius, Height = b.Radius * 2, RadialSegments = 96, Rings = 48 }, MaterialOverride = mat, Position = Gd(b.Center) });
        if (ring)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = b.Radius * 1.5f, OuterRadius = b.Radius * 1.62f, Rings = 96 },
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = glow * 1.4f },
                Position = Gd(b.Center),
                RotationDegrees = new Vector3(20, 0, 12),
            });
        }

        if (place != null)
        {
            // A real composed folder-place: its terrain is the walker's, unchanged, in this body's chart.
            _terrains.Add(place.Terrain);
            return;
        }

        // A tiny place on the other world, composed in its flat chart and carried onto the sphere: a landmark at the pole
        // and a ring of seven gate-like arches 12 m out (they are obstacles for the walker in the chart).
        var boxes = new List<TerrainBox>();
        var meshes = new List<(NVector2 Chart, Vector3 Size, Color Color)>();
        meshes.Add((NVector2.Zero, new Vector3(1.2f, 9f, 1.2f), glow));
        boxes.Add(new TerrainBox(NVector2.Zero, new NVector2(0.6f, 0.6f), 0, 0, 9, TerrainKind.Landmark));
        for (var k = 0; k < 8; k++)
        {
            var dir = PlaceGeometry.Direction(k * 45f);
            var centre = new NVector2(dir.X, dir.Z) * 12f;
            if (k == 0) continue; // the gap towards the pad (north)
            meshes.Add((centre, new Vector3(0.6f, 4f, 0.6f), glow * 0.8f));
            boxes.Add(new TerrainBox(centre, new NVector2(0.3f, 0.3f), 0, 0, 4, TerrainKind.Pillar));
        }

        _terrains.Add(new PlaceTerrain(boxes, [], [], b.Radius * 2.9f));
        foreach (var (chart, size, color) in meshes)
        {
            var (u, n, e) = b.FrameAt(chart);
            var at = b.ToWorld(chart, size.Y / 2);
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.12f), EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 1.2f },
                Transform = new Transform3D(new Basis(Gd(e), Gd(u), Gd(-n)), Gd(at)),
            });
        }
    }

    /// <summary>
    /// "Orchard": an original small folder (five folders, a dozen files of several types, a README) built in memory, so
    /// the experiment needs no real files.
    /// </summary>
    private static FsTree Orchard()
    {
        var tree = new FsTree(new DataSourceInfo(DataOrigin.Synthetic, "Orchard", "planetoid experiment (in memory)"));
        var batch = new ScanBatch();
        var next = 0;
        int Add(int parent, string name, NodeKind kind, long size, int depth)
        {
            var id = next++;
            batch.NewNodes.Add(new NodeRecord(id, parent, name, kind, 0, 0, size, null, null, null, depth,
                kind == NodeKind.Directory ? DirScanState.Complete : DirScanState.NotApplicable, null, null, LocationKind.None));
            return id;
        }

        var root = Add(-1, "Orchard", NodeKind.Directory, 0, 0);
        foreach (var (dir, files) in new[] { ("Blossom", 3), ("Harvest", 6), ("Saplings", 2), ("Seeds", 4), ("Tools", 1) })
        {
            var d = Add(root, dir, NodeKind.Directory, 0, 1);
            for (var i = 0; i < files; i++) Add(d, $"{dir.ToLowerInvariant()}-{i + 1}.txt", NodeKind.File, 1000L * (i + 1), 2);
        }

        foreach (var (name, size) in new[] { ("README.md", 4_000L), ("plan.txt", 12_000L), ("map.png", 900_000L), ("rows.png", 400_000L), ("song.mp3", 4_800_000L),
                     ("yield.csv", 60_000L), ("notes.md", 9_000L), ("sketch.svg", 30_000L), ("tour.mp4", 40_000_000L), ("tools.zip", 2_000_000L), ("seeds.json", 3_000L), ("pump.cs", 7_000L) })
        {
            Add(root, name, NodeKind.File, size, 1);
        }

        tree.Apply(batch);
        return tree;
    }

    /// <summary>
    /// Draws the composed place on the sphere: every terrain box (towers, pillars, landmark, terrace, overlook, steps)
    /// becomes a box standing on the surface at its chart position, with its footprint in the local tangent frame, so
    /// what you see is what the walker collides with (to the chart's accuracy: within ≈ 35° of the pole the footprint
    /// on the sphere is 94–100 % of the chart's across, exact along). Gates carry their folder names.
    /// </summary>
    private void BuildPlace(Planetoid b, PlaceComposition place)
    {
        var mats = new Dictionary<Color, StandardMaterial3D>();
        StandardMaterial3D Mat(Color c, float glow)
        {
            if (!mats.TryGetValue(c, out var m))
            {
                m = new StandardMaterial3D { AlbedoColor = c.Darkened(0.55f), EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = glow };
                mats[c] = m;
            }

            return m;
        }

        foreach (var box in place.Terrain.Boxes)
        {
            var color = box.Kind switch
            {
                TerrainKind.Tower when box.NodeId >= 0 => Palette.Category[FileCategories.Of(_orchard[box.NodeId].Name)],
                TerrainKind.Pillar when box.NodeId >= 0 => PlacePalette.Accent(PlaceIdentity.For(_orchard[box.NodeId].Name)),
                TerrainKind.Landmark => new Color(0.4f, 1f, 0.9f),
                _ => new Color(0.42f, 0.5f, 0.62f),
            };
            var glow = box.Kind is TerrainKind.Tower or TerrainKind.Landmark ? 0.9f : box.Kind == TerrainKind.Pillar ? 1.4f : 0.35f;
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One }, MaterialOverride = Mat(color, glow), Transform = OnSurface(b, box) });
        }

        foreach (var g in place.Gates)
        {
            var chart = new NVector2(g.Position.X, g.Position.Z);
            var (u, _, _) = b.FrameAt(chart);
            AddChild(new Label3D
            {
                Text = _orchard[g.TargetId].Name,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 64,
                PixelSize = 0.012f,
                OutlineSize = 12,
                Modulate = PlacePalette.Accent(PlaceIdentity.For(_orchard[g.TargetId].Name)).Lightened(0.4f),
                Position = Gd(b.ToWorld(chart, g.Height + 1.4f)),
                NoDepthTest = false,
            });
            _ = u;
        }
    }

    /// <summary>A terrain box's transform on the sphere: its chart footprint in the local tangent frame, its height along up.</summary>
    private static Transform3D OnSurface(Planetoid b, TerrainBox box)
    {
        var (up, north, east) = b.FrameAt(box.Center);
        // Box axes in the chart (place angles clockwise from north; x east, z south), rotated by the box's angle.
        var a = box.AngleDeg * MathF.PI / 180f;
        NVector3 ToWorldDir(float x, float z) => east * x + north * -z;
        var ax = ToWorldDir(MathF.Cos(a), MathF.Sin(a));
        var az = ToWorldDir(-MathF.Sin(a), MathF.Cos(a));
        var basis = new Basis(Gd(ax) * box.HalfExtents.X * 2, Gd(up) * (box.Top - box.Bottom), Gd(az) * box.HalfExtents.Y * 2);
        return new Transform3D(basis, Gd(b.ToWorld(box.Center, (box.Top + box.Bottom) / 2)));
    }

    /// <summary>
    /// PLANET-04 picking: a world ray is not a straight line in the chart, so it is marched in world space (0.15 m steps)
    /// and each point is tested against the terrain boxes in the body's chart. Returns the node picked (or −1) and what.
    /// </summary>
    public (int NodeId, TerrainKind Kind, float Distance) Pick(Vector3 origin, Vector3 dir, float maxDistance = 90f)
    {
        var b = _bodies[0];
        for (var t = 0.3f; t < maxDistance; t += 0.15f)
        {
            var p = origin + dir * t;
            var (chart, h) = b.ToChart(new NVector3(p.X, p.Y, p.Z));
            if (h < -0.5f) return (-1, TerrainKind.Platform, t); // into the ground
            foreach (var box in _place.Terrain.Boxes)
            {
                if (h >= box.Bottom && h <= box.Top && box.Overlaps(chart, 0.02f)) return (box.NodeId, box.Kind, t);
            }
        }

        return (-1, TerrainKind.Platform, maxDistance);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode == Key.Escape) GetTree().Quit();
            if (k.Keycode == Key.Space) _jumpQueued = true;
            if (k.Keycode == Key.V) _firstPerson = !_firstPerson;
        }

        if (@event is InputEventMouseMotion m && (m.ButtonMask & MouseButtonMask.Right) != 0)
        {
            // Right-drag looks: yaw turns the walker; pitch tilts the first-person view or raises the chase camera.
            _traveller.Walker.Look(Degrees.Of(-m.Relative.X * 0.25f), Degrees.Zero);
            if (_firstPerson) _lookPitch = Mathf.Clamp(_lookPitch - m.Relative.Y * 0.2f, -80f, 80f);
            else _chasePitch = Mathf.Clamp(_chasePitch + m.Relative.Y * 0.2f, -5f, 70f);
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click && _traveller.Body == 0)
        {
            var hit = Pick(_camera.ProjectRayOrigin(click.Position), _camera.ProjectRayNormal(click.Position));
            _picked = hit.NodeId >= 0 ? $"picked: {_orchard[hit.NodeId].Name} ({hit.Kind.ToString().ToLowerInvariant()}, {hit.Distance:0} m)" : $"picked: {hit.Kind.ToString().ToLowerInvariant()}";
        }
    }

    public override void _Process(double delta)
    {
        var dt = Durations.FromSeconds(delta);
        var fwd = (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up) ? 1 : 0) - (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down) ? 1 : 0);
        var turn = (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) ? 1 : 0) - (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) ? 1 : 0);
        var jump = _jumpQueued;
        _jumpQueued = false;
        if (jump && _traveller.Body >= 0 && OnPad() is { } pad)
        {
            // PLANET-03: a fast guided transfer to the other world's pad (the slow ballistic arc took ≈ 9–10 s).
            var target = 1 - _traveller.Body;
            _traveller.LaunchGuided(target, _pads[target].Chart);
            _launchFov = 1f;
            jump = false;
            _ = pad;
        }

        _traveller.Update(new WalkInput(fwd, 0, turn, Input.IsKeyPressed(Key.Shift), jump, !Input.IsKeyPressed(Key.Space)), dt);
        var feet = Gd(_traveller.Position);
        var up = Gd(_traveller.Up);
        var forward = Gd(_traveller.Forward);
        _avatar.Transform = new Transform3D(BasisFrom(up, forward), feet + up * 0.9f);
        // A chase camera: behind and above, so the curvature and the other world are in view.
        if (_traveller.Transfers != _lastTransfers)
        {
            _lastTransfers = _traveller.Transfers;
            _landKick = 1f; // a decisive landing: the camera dips and settles (no shake, no cut)
        }

        _landKick = Mathf.Max(0, _landKick - (float)delta * 2.2f);
        _launchFov = Mathf.Max(0, _launchFov - (float)delta * 1.6f);
        var dip = up * (-0.8f * Mathf.Sin(_landKick * Mathf.Pi));
        if (_firstPerson && _traveller.Body >= 0)
        {
            var right = forward.Cross(up).Normalized();
            var look = forward.Rotated(right, Mathf.DegToRad(_lookPitch));
            _camPos = feet + up * 1.75f + dip;
            _camera.LookAtFromPosition(_camPos, _camPos + look, up);
        }
        else
        {
            // A chase camera: behind and above. In a transfer it pulls back and widens a little with speed, so the arc and
            // the target world read; it never shakes or cuts.
            var flying = _traveller.Body < 0;
            var back = flying ? 15f : 9f;
            var elev = Mathf.DegToRad(flying ? 28f : _chasePitch);
            var want = feet + up * (back * Mathf.Sin(elev) + 1.5f) - forward * (back * Mathf.Cos(elev)) + dip;
            _camPos = _camPos.Lerp(want, 1 - Mathf.Exp((flying ? -9f : -6f) * (float)delta));
            _camera.LookAtFromPosition(_camPos, feet + up * 1.4f + forward * (flying ? 9f : 5f), up);
        }

        var progress = _traveller.Guided ? _traveller.GuidedProgress : 0f;
        _camera.Fov = 60f + 12f * Mathf.Sin(progress * Mathf.Pi) + 6f * _launchFov;
        var where = _traveller.Body switch { 0 => "on the teal world · the folder-place “Orchard”", 1 => "on the coral world", _ => $"flying between worlds ({_traveller.FlightSeconds:0.0} s)" };
        _label.Text = $"EXPERIMENT · planetoid traversal (not the FileScape view) · {where}\nW/S walk · A/D turn · right-drag look · V first person · click pick · Space jump · Space on the glowing pad: fly to the other world · Esc quit" +
                      (_picked.Length > 0 ? $"\n{_picked}" : "");
    }

    private NVector3? OnPad()
    {
        var feet = new NVector2(_traveller.Walker.Feet.X, _traveller.Walker.Feet.Z);
        foreach (var p in _pads)
        {
            if (p.Body == _traveller.Body && NVector2.Distance(feet, p.Chart) < p.Radius) return p.LaunchVelocity;
        }

        return null;
    }

    private static Basis BasisFrom(Vector3 up, Vector3 forward)
    {
        var right = forward.Cross(up).Normalized();
        var f = up.Cross(right).Normalized();
        return new Basis(right, up, -f);
    }

    private static Vector3 Gd(NVector3 v) => new(v.X, v.Y, v.Z);

    // ------------------------------------------------------------------ automation (--automate=planetoids)

    private async Task Script()
    {
        await Frames(30);
        var b0 = _bodies[0];
        Log($"start: body {_traveller.Body}, chart feet {_traveller.Walker.Feet}, place Orchard with {_place.Gates.Count} gates, {_place.Files.Count} files, boundary {_place.Terrain.BoundaryRadius:0.0} m (≈ {_place.Terrain.BoundaryRadius / b0.Radius * 180 / MathF.PI:0}° of the {b0.Radius:0} m world)");
        await Shot("01-curved-place-arrival");

        // Walking: the unchanged walker in the chart; the feet stay on the sphere and never inside a structure.
        var start = _traveller.Walker.Feet;
        _traveller.Walker.SetGaze(Degrees.Of(-30f), Degrees.Zero); // towards the east gates, past the landmark
        Hold(Key.W, true);
        await Seconds(1.6);
        Hold(Key.W, false);
        var r = (_traveller.Position - b0.Center).Length() - _traveller.Walker.Feet.Y;
        var feet2 = new NVector2(_traveller.Walker.Feet.X, _traveller.Walker.Feet.Z);
        var inside = _place.Terrain.Boxes.Any(x => x.Kind is TerrainKind.Tower or TerrainKind.Pillar or TerrainKind.Landmark && x.Overlaps(feet2, 0.3f) && _traveller.Walker.Feet.Y < x.Top - 0.1f);
        Check(_traveller.Body == 0 && MathF.Abs(r - b0.Radius) < 0.05f && !inside && NVector3.Distance(start, _traveller.Walker.Feet) > 3f,
            $"walking in the curved place: {NVector3.Distance(start, _traveller.Walker.Feet):0.0} m, feet on the sphere (r = {r:0.00} m), never inside a structure");

        // Jumping.
        var y0 = _traveller.Walker.Feet.Y;
        var apex = y0;
        Hold(Key.Space, true);
        _jumpQueued = true;
        for (var i = 0; i < 90; i++) { await Frames(1); apex = MathF.Max(apex, _traveller.Walker.Feet.Y); }
        Hold(Key.Space, false);
        await Seconds(0.8);
        Check(apex - y0 > 0.8f && _traveller.Walker.Grounded && _traveller.Walker.Jumps >= 1, $"jumping on the sphere: apex {apex - y0:0.00} m above the start, landed");

        // Looking (first person): pitch is relative to the local up, and the view never rolls. Face a gate first (the walk
        // ends at the landmark, which would fill the view).
        var gate = _place.Gates[_place.Gates.Count / 2];
        var toGate = new NVector2(gate.Position.X, gate.Position.Z) - new NVector2(_traveller.Walker.Feet.X, _traveller.Walker.Feet.Z);
        _traveller.Walker.SetGaze(Degrees.Of(-PlaceTerrain.PlaceAngle(NVector2.Normalize(toGate)) + 35f), Degrees.Zero);
        _firstPerson = true;
        _lookPitch = 30f;
        await Frames(3);
        var up = Gd(_traveller.Up);
        var fwd = -_camera.GlobalBasis.Z;
        var right = _camera.GlobalBasis.X;
        Check(MathF.Abs(fwd.Dot(up) - MathF.Sin(MathF.PI / 6)) < 0.03f && MathF.Abs(right.Dot(up)) < 0.02f,
            $"looking up 30° in first person: view·up = {fwd.Dot(up):0.000} (sin 30° = 0.5), roll {right.Dot(up):0.000}");
        _lookPitch = -10f;
        await Shot("02-first-person-in-the-curved-place");

        // Picking: a gate pillar and a file tower, by marching the camera ray and testing in the chart.
        var picks = new List<string>();
        foreach (var kind in new[] { TerrainKind.Pillar, TerrainKind.Tower })
        {
            var cam = _camera.GlobalPosition;
            var ok = false;
            foreach (var box in _place.Terrain.Boxes.Where(x => x.Kind == kind && x.NodeId >= 0).OrderBy(x => NVector2.Distance(x.Center, feet2)))
            {
                var target = Gd(b0.ToWorld(box.Center, (box.Top + box.Bottom) / 2));
                var hit = Pick(cam, (target - cam).Normalized());
                if (hit.NodeId != box.NodeId) continue;
                picks.Add($"{kind.ToString().ToLowerInvariant()} {_orchard[hit.NodeId].Name} at {hit.Distance:0.0} m");
                _picked = $"picked: {_orchard[hit.NodeId].Name}";
                ok = true;
                break;
            }

            Check(ok, $"picking a {kind.ToString().ToLowerInvariant()} on the sphere by its world ray: {picks.LastOrDefault() ?? "none hit"}");
        }

        _firstPerson = false;
        await Shot("03-picked-in-the-curved-place");

        // Back to the pad (behind the arrival terrace) and launch.
        for (var i = 0; i < 900 && OnPad() == null; i++)
        {
            var feet = new NVector2(_traveller.Walker.Feet.X, _traveller.Walker.Feet.Z);
            var to = _pads[0].Chart - feet;
            var way = to.Length() > 7f && MathF.Abs(feet.X) > 2f ? new NVector2(0, feet.Y) - feet : to; // into the corridor first
            _traveller.Walker.SetGaze(Degrees.Of(-PlaceTerrain.PlaceAngle(NVector2.Normalize(way))), Degrees.Zero);
            Hold(Key.W, true);
            await Frames(1);
        }

        Hold(Key.W, false);
        Check(OnPad() != null, "reached the launch pad on foot");

        // Before: the slowest ballistic arc that arrives (the previous pad), simulated with the same bodies.
        var before = BallisticSeconds(0);
        _jumpQueued = true;
        await Frames(2);
        Check(_traveller.Body == -1 && _traveller.Guided, "Space on the pad launches a guided transfer");
        await Seconds(0.9);
        await Shot("04-transfer-mid-arc");
        for (var i = 0; i < 600 && _traveller.Body != 1; i++) await Frames(1);
        var after = _traveller.FlightSeconds;
        await Seconds(0.6);
        await Shot("05-landed-on-the-coral-world");
        var r2 = (_traveller.Position - _bodies[1].Center).Length();
        Check(_traveller.Body == 1 && _traveller.Walker.Grounded && MathF.Abs(r2 - _bodies[1].Radius) < 0.05f, $"you land walking on the coral world (r = {r2:0.00} m, transfers {_traveller.Transfers})");
        Check(after < 2.8f && after < before / 2, $"transfer: {after:0.00} s guided against {before:0.00} s ballistic before");
        Log($"transfer-timing before={before:0.00}s after={after:0.00}s");
        Hold(Key.W, true);
        await Seconds(1.5);
        Hold(Key.W, false);
        Log($"done failures={_failures}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(_out, "planetoids.log"), _log.ToString());
        GetTree().Quit(_failures);
    }

    /// <summary>The previous transfer: the experiment's slowest arriving ballistic launch from a pad, flown under gravity (seconds).</summary>
    private float BallisticSeconds(int from)
    {
        var t = new PlanetoidTraveller(_bodies, _terrains);
        t.PlaceOn(from, _pads[from].Chart, Degrees.Zero);
        var at = t.Position;
        var dir = NVector3.Normalize(NVector3.Normalize(_bodies[1 - from].Center - at) + t.Up * 0.6f);
        var speed = PlanetoidTraveller.LaunchSpeed(_bodies, at, dir, 1 - from) ?? 30f;
        t.Launch(dir * speed);
        for (var i = 0; i < 120 * 40 && t.Body != 1 - from; i++) t.Update(WalkInput.None, Durations.FromSeconds(1.0 / 120));
        return t.Body == 1 - from ? t.FlightSeconds : float.NaN;
    }

    private static void Hold(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });

    private async Task Frames(int n)
    {
        for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    private async Task Shot(string name)
    {
        await Frames(3);
        System.IO.Directory.CreateDirectory(_out);
        var path = System.IO.Path.Combine(_out, name + ".png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        Log($"screenshot {path}");
    }

    private void Check(bool ok, string what)
    {
        if (!ok) _failures++;
        Log((ok ? "PASS " : "FAIL ") + what);
    }

    private void Log(string line)
    {
        GD.Print("[planetoids] " + line);
        _log.AppendLine(line);
    }
}
