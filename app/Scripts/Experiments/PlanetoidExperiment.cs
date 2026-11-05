using FileScape.Core.Experiments;
using FileScape.Core.Places;
using FileScape.Core.Units;
using Godot;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Experiments;

/// <summary>
/// EXPERIMENT (PLANET-02; --experiment=planetoids): two small original worlds and a traveller who walks on them with the
/// unchanged FileScape walker (run in each body's chart, <see cref="PlanetoidTraveller"/>) and flies between them from a
/// launch pad. It exists to test the feasibility claims in docs/planetoid-feasibility.md; it is not part of FileScape's
/// views and shows no files. W/S walk, A/D or ←/→ turn, Space jump (on the glowing pad: launch), Esc quits.
/// With --automate=planetoids it walks, launches, lands, takes screenshots and quits.
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
        AddBody(new Planetoid(NVector3.Zero, 40f, 6f, NVector3.UnitY, -NVector3.UnitZ), new Color(0.1f, 0.38f, 0.36f), new Color(0.04f, 0.18f, 0.22f), new Color(0.3f, 1f, 0.85f), ring: false);
        AddBody(new Planetoid(new NVector3(0, 30, -170), 32f, 6f, NVector3.Normalize(new NVector3(0, 0.6f, 1)), NVector3.Normalize(new NVector3(0, 1, -0.6f))), new Color(0.45f, 0.16f, 0.22f), new Color(0.22f, 0.07f, 0.14f), new Color(1f, 0.55f, 0.6f), ring: true);
        _traveller = new PlanetoidTraveller(_bodies, _terrains);
        _traveller.PlaceOn(0, new NVector2(0, 14), Degrees.Zero);

        // A launch pad on each world, aimed at the other; its speed comes from simulating the arc (Core).
        for (var i = 0; i < 2; i++)
        {
            var b = _bodies[i];
            var other = _bodies[1 - i];
            var chart = new NVector2(0, -18);
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

    private void AddBody(Planetoid b, Color a, Color c, Color glow, bool ring)
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

        // A tiny place on each world, composed in its flat chart and carried onto the sphere: a landmark at the pole and
        // a ring of eight gate-like arches 12 m out (they are obstacles for the walker in the chart).
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

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode == Key.Escape) GetTree().Quit();
            if (k.Keycode == Key.Space) _jumpQueued = true;
        }
    }

    public override void _Process(double delta)
    {
        var dt = Durations.FromSeconds(delta);
        var fwd = (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up) ? 1 : 0) - (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down) ? 1 : 0);
        var turn = (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) ? 1 : 0) - (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) ? 1 : 0);
        var jump = _jumpQueued;
        _jumpQueued = false;
        if (jump && _traveller.Body >= 0 && OnPad() is { } v)
        {
            _traveller.Launch(v);
            jump = false;
        }

        _traveller.Update(new WalkInput(fwd, 0, turn, Input.IsKeyPressed(Key.Shift), jump, !Input.IsKeyPressed(Key.Space)), dt);
        var feet = Gd(_traveller.Position);
        var up = Gd(_traveller.Up);
        var forward = Gd(_traveller.Forward);
        _avatar.Transform = new Transform3D(BasisFrom(up, forward), feet + up * 0.9f);
        // A chase camera: behind and above, so the curvature and the other world are in view.
        var want = feet + up * 4.5f - forward * 9f;
        _camPos = _camPos.Lerp(want, 1 - Mathf.Exp(-6f * (float)delta));
        _camera.LookAtFromPosition(_camPos, feet + up * 1.4f + forward * 5f, up);
        var where = _traveller.Body switch { 0 => "on the teal world", 1 => "on the coral world", _ => "flying between worlds" };
        _label.Text = $"EXPERIMENT · planetoid traversal (not the FileScape view) · {where}\nW/S walk · A/D turn · Space jump · Space on the glowing pad: fly to the other world · Esc quit";
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
        Log($"start: body {_traveller.Body}, feet {_traveller.Position}");
        Hold(Key.W, true);
        await Seconds(2.0);
        Hold(Key.W, false);
        var r = (_traveller.Position - _bodies[0].Center).Length();
        Check(_traveller.Body == 0 && MathF.Abs(r - _bodies[0].Radius) < 0.05f, $"walking on the teal world keeps the feet on its surface (r = {r:0.00} m)");
        await Shot("01-walking-on-a-small-world");
        // Turn round and walk onto the pad (the experiment's own input, injected as key presses).
        Hold(Key.Left, true);
        await Seconds(1.5);
        Hold(Key.Left, false);
        NVector2[] route = [new(5, -2), new(4, -12), _pads[0].Chart]; // round the landmark, through the gap in the arches
        var leg = 0;
        for (var i = 0; i < 900 && OnPad() == null; i++)
        {
            // Steer along the route in the chart (a direct helper: the experiment has no pathfinding).
            var feet = new NVector2(_traveller.Walker.Feet.X, _traveller.Walker.Feet.Z);
            if (leg < route.Length - 1 && NVector2.Distance(feet, route[leg]) < 1.2f) leg++;
            var to = route[leg] - feet;
            _traveller.Walker.SetGaze(Degrees.Of(-PlaceTerrain.PlaceAngle(NVector2.Normalize(to))), Degrees.Zero);
            Hold(Key.W, true);
            await Frames(1);
        }

        Hold(Key.W, false);
        Check(OnPad() != null, "reached the launch pad on foot");
        _jumpQueued = true;
        await Seconds(1.0);
        Check(_traveller.Body == -1, "Space on the pad launches you towards the other world");
        await Shot("02-flying-between-worlds");
        for (var i = 0; i < 1200 && _traveller.Body != 1; i++) await Frames(1);
        await Seconds(1.5);
        var r2 = (_traveller.Position - _bodies[1].Center).Length();
        Check(_traveller.Body == 1 && _traveller.Walker.Grounded && MathF.Abs(r2 - _bodies[1].Radius) < 0.05f, $"you land walking on the coral world (r = {r2:0.00} m, transfers {_traveller.Transfers})");
        Hold(Key.W, true);
        await Seconds(2.0);
        Hold(Key.W, false);
        await Shot("03-walking-on-the-other-world");
        Log($"done failures={_failures}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(_out, "planetoids.log"), _log.ToString());
        GetTree().Quit(_failures);
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
