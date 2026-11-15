using FileScape.Core.Interaction;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// EXPERIMENT (--experiment=planet-gallery): an isolated scene showing the Planets art ("Lantern Cosmos") — the sky, a
/// large Hearth showcase planet carrying every file object and pile, the bloom launcher, the overlook, stepping stones
/// and the traveller animating (running, jumping with a twirl, winding up and flying); and one small planet per other
/// theme hanging in the sky with its landmark, decorations, a launcher and a few files. Nothing of FileScape is built or
/// scanned. Interactively it cycles its camera shots every few seconds (Esc quits). With --automate=planet-gallery
/// --out=DIR it writes screenshots and planet-gallery.log (triangle counts) to DIR and quits with exit code 0.
/// </summary>
public partial class PlanetGallery : Node3D
{
    private readonly bool _automate;
    private readonly string _out;
    private readonly System.Text.StringBuilder _log = new();
    private Camera3D _camera = null!;
    private AvatarRig _avatar = null!;
    private PlanetShape _showcase = null!;

    /// <summary>The showcase planet's radius, metres (the satellite worlds are 9–13 m).</summary>
    private const float ShowcaseRadius = 34f;
    private LauncherArt _launcherArt = null!;
    private Node3D _launcher = null!;
    private readonly List<(PlanetTheme Theme, Vector3 Centre, Basis Basis, float Radius)> _satellites = [];
    private readonly List<(string Name, Action Frame)> _shots = [];
    private float _clockSeconds;
    private int _shot = -1;
    private float _shotSeconds;
    private Action? _cameraFollow; // re-aims the camera every frame for shots that follow the traveller

    /// <summary>Interactive mode: each camera shot is held this long, seconds, before the next.</summary>
    private const float ShotHoldSeconds = 5f;

    /// <summary>Interactive mode: after running this long, seconds, the traveller tries a trick (alternately a jump and a wind-up, by the clock in steps of the same length).</summary>
    private const float RunBeforeTrickSeconds = 2.5f;

    /// <summary>What the traveller is demonstrating: running a circle, a jump with an air jump, winding up on the launcher, the flight, or standing still for a portrait.</summary>
    private enum Demo { Run, Jump, WindUp, Fly, Idle }
    private Demo _mode = Demo.Run;
    private float _modeSeconds;
    private float _runAngle;
    private bool _airJumped;
    private float _height, _verticalSpeed;
    private Vector3 _flyFrom, _flyCtrl, _flyTo;
    /// <summary>The traveller runs a 2.6 m circle at 5.5 m/s (a lap ≈ 3 s), between the rows and the launcher.</summary>
    private const float RunSpeedMetersPerSecond = 5.5f, RunRadiusMetres = 2.6f;

    /// <summary>Map positions on the showcase (x east, z south, metres from the pole): the run circle's centre and the launcher.</summary>
    private static readonly Vector2 RunCentre = new(-0.5f, -5.2f);

    private static readonly Vector2 LauncherAt = new(-6.5f, -9f);

    /// <summary>The demo jump: take-off and air-jump speeds (m/s), the vertical speed below which the air jump fires, and gravity (m/s², the walker's 17).</summary>
    private const float JumpTakeOffMetresPerSecond = 6.5f, AirJumpMetresPerSecond = 5.5f, AirJumpBelowMetresPerSecond = 1.0f, GravityMetresPerSecondSquared = 17f;

    /// <summary>The wind-up lasts 0.9 s (petals close to 40 % open), then the flight starts after 1 s; the flight takes 2 s.</summary>
    private const float WindUpSeconds = 0.9f, WindUpHoldSeconds = 1.0f, FlightSeconds = 2.0f;

    /// <summary>The flight: an ease-out power on its progress, its length towards the third satellite (metres) and how high its arc bulges (metres).</summary>
    private const float FlightEasePower = 1.6f, FlightLengthMetres = 42f, FlightArcLiftMetres = 16f;

    /// <summary>The traveller's feet on the launcher, metres above the ground (the pad's walkable top).</summary>
    private const float PadTopMetres = 0.32f;

    public PlanetGallery(bool automate, string outDir)
    {
        _automate = automate;
        _out = outDir;
        Name = "PlanetGallery";
    }

    public override void _Ready()
    {
        var hearth = PlanetArt.Palette(PlanetTheme.Hearth);
        AddChild(new WorldEnvironment { Environment = PlanetArt.SkyEnvironment(hearth) });
        var sun = PlanetArt.Sun(hearth);
        sun.RotationDegrees = new Vector3(-42, 205, 0); // light from behind the cameras (file fronts face −Z)
        AddChild(sun);
        _camera = new Camera3D { Fov = 55, Near = 0.05f, Far = 4000, Current = true };
        AddChild(_camera);

        BuildShowcase(hearth);
        BuildSatellites();
        _avatar = new AvatarRig();
        AddChild(_avatar);
        DriveAvatar(0.016f);
        DefineShots();
        LogTriangles();
        if (_automate) _ = Script();
        else NextShot();
    }

    // ------------------------------------------------------------------ the showcase planet

    /// <summary>
    /// The direction from the showcase's centre to map point (x east, z south, metres): an azimuthal-equidistant map from
    /// the pole, so a map distance is the distance walked along the ground.
    /// </summary>
    private Vector3 ShowcaseDir(float x, float z)
    {
        var d = Mathf.Sqrt(x * x + z * z);
        if (d < 1e-4f) return Vector3.Up;
        var a = d / ShowcaseRadius;
        return new Vector3(Mathf.Sin(a) * x / d, Mathf.Cos(a), Mathf.Sin(a) * z / d);
    }

    private static NVector3 N(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>World point <paramref name="height"/> above the showcase ground at map (x east, z south), metres.</summary>
    private Vector3 OnShowcase(float x, float z, float height = 0)
    {
        var d = ShowcaseDir(x, z);
        return d * (_showcase.GroundRadius(N(d)) + height);
    }

    /// <summary>A frame on a sphere at <paramref name="up"/>: local −Z towards the map north (−Z), turned by a place angle (clockwise, degrees).</summary>
    private static Basis Frame(Vector3 up, float yawDegrees)
    {
        var refAxis = Mathf.Abs(up.Dot(Vector3.Back)) < 0.95f ? Vector3.Back : Vector3.Right;
        var z = (refAxis - up * refAxis.Dot(up)).Normalized();
        var x = up.Cross(z).Normalized();
        return new Basis(up, -Mathf.DegToRad(yawDegrees)) * new Basis(x, up, z);
    }

    /// <summary>A prop's transform standing on the showcase at map (x, z), turned by a place angle, raised by <paramref name="height"/> metres.</summary>
    private Transform3D PlaceOnShowcase(float x, float z, float yawDegrees = 0, float height = 0)
    {
        var d = ShowcaseDir(x, z);
        return new Transform3D(Frame(d, yawDegrees), OnShowcase(x, z, height));
    }

    /// <summary>The Hearth showcase: every file object in a row (fronts north), every pile behind, the launcher, overlook, stepping stones, landmark and seeded scenery clear of the display.</summary>
    private void BuildShowcase(PlanetPalette p)
    {
        var flats = new List<(NVector3, float)> { (NVector3.UnitY, 15f), (N(new Vector3(0.7f, 0.2f, 0.7f).Normalized()), 4f), (N(new Vector3(-0.6f, -0.3f, 0.75f).Normalized()), 4f) };
        _showcase = new PlanetShape(ShowcaseRadius, 5, 2.2f, flats);
        AddChild(new MeshInstance3D { Name = "Showcase", Mesh = PlanetArt.PlanetMesh(_showcase, p, 5) });
        AddChild(PlanetArt.Atmosphere(ShowcaseRadius, p));

        // Row of file objects (fronts face north, −Z), and a row of piles behind them.
        var cats = Enum.GetValues<FileCategory>();
        var thumbs = new Dictionary<FileCategory, Texture2D> { [FileCategory.Image] = Thumbnail(false), [FileCategory.Video] = Thumbnail(true) };
        for (var i = 0; i < cats.Length; i++)
        {
            var art = PlanetArt.FileObject(cats[i]);
            Spawn(art, PlaceOnShowcase(-8f + i * 2f, 0f), thumbs);
            var pile = PlanetArt.Pile(cats[i]);
            Spawn(pile, PlaceOnShowcase(-10.4f + i * 2.6f, 5.0f), thumbs);
        }

        _launcherArt = PlanetArt.Launcher(p);
        _launcher = _launcherArt.Instantiate(1f);
        _launcher.Transform = PlaceOnShowcase(LauncherAt.X, LauncherAt.Y, 20);
        AddChild(_launcher);
        AddChild(new MeshInstance3D { Mesh = PlanetArt.Overlook(p), Transform = PlaceOnShowcase(6f, -10f, 0) });
        var stones = new[] { (3.4f, -4.6f, 0.6f), (4.4f, -5.7f, 1.25f), (5.2f, -6.8f, 1.85f) };
        for (var i = 0; i < stones.Length; i++)
            AddChild(new MeshInstance3D { Mesh = PlanetArt.SteppingStone(stones[i].Item3, i), Transform = PlaceOnShowcase(stones[i].Item1, stones[i].Item2) });
        AddChild(new MeshInstance3D { Mesh = PlanetArt.Landmark(PlanetTheme.Hearth), Transform = PlaceOnShowcase(0, -19f, 0) });

        // Scenery around the display, clear of the rows.
        var rng = new Random(4);
        var kinds = new[] { DecorationKind.Tree, DecorationKind.Tree, DecorationKind.Rock, DecorationKind.Flower, DecorationKind.Flower, DecorationKind.Grass, DecorationKind.Grass, DecorationKind.Mushroom, DecorationKind.Lamp, DecorationKind.Crystal };
        var cache = new Dictionary<(DecorationKind, int), ArrayMesh>();
        for (var k = 0; k < 70; k++)
        {
            var a = (float)rng.NextDouble() * Mathf.Tau;
            var r = 11f + (float)rng.NextDouble() * 12f;
            var x = Mathf.Cos(a) * r;
            var z = Mathf.Sin(a) * r;
            if (Mathf.Abs(z) < 8.5f && Mathf.Abs(x) < 13.5f) continue;
            if (new Vector2(x, z - -19f).Length() < 4.5f) continue;
            var kind = kinds[rng.Next(kinds.Length)];
            var key = (kind, rng.Next(3));
            if (!cache.TryGetValue(key, out var mesh)) cache[key] = mesh = PlanetArt.Decoration(kind, PlanetTheme.Hearth, key.Item2);
            AddChild(new MeshInstance3D { Mesh = mesh, Transform = PlaceOnShowcase(x, z, (float)rng.NextDouble() * 360f) });
        }

        // Lamps along the front of the rows.
        foreach (var x in new[] { -11f, 11f })
            AddChild(new MeshInstance3D { Mesh = PlanetArt.Decoration(DecorationKind.Lamp, PlanetTheme.Hearth, 0), Transform = PlaceOnShowcase(x, 1.5f) });
    }

    /// <summary>Places a file object or pile with a halo on its lantern; a face shows a stand-in picture (the video still on a wide face such as Video's, the landscape otherwise).</summary>
    private void Spawn(FileArt art, Transform3D at, Dictionary<FileCategory, Texture2D>? thumbs, Node3D? parent = null)
    {
        var mi = new MeshInstance3D { Mesh = art.Mesh, Transform = at };
        (parent ?? this).AddChild(mi);
        if (art.FaceSurface >= 0 && thumbs != null)
        {
            var tex = art.FaceSize.X > art.FaceSize.Y * 1.3f ? thumbs[FileCategory.Video] : thumbs[FileCategory.Image];
            mi.SetSurfaceOverrideMaterial(art.FaceSurface, new StandardMaterial3D
            {
                AlbedoTexture = tex,
                EmissionEnabled = true,
                EmissionTexture = tex,
                EmissionEnergyMultiplier = 0.35f,
                Roughness = 0.9f,
            });
        }

        var halo = PlanetArt.Halo(new Color(1f, 0.82f, 0.5f), 0.45f, 0.45f);
        halo.Position = art.Lantern;
        mi.AddChild(halo);
    }

    /// <summary>A small procedural picture standing in for a real thumbnail (a sunset landscape, or a stage with a play mark).</summary>
    private static ImageTexture Thumbnail(bool video)
    {
        const int w = 128, h = 128;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var u = x / (float)w;
            var v = y / (float)h;
            Color c;
            if (!video)
            {
                c = new Color(0.98f, 0.62f, 0.55f).Lerp(new Color(0.42f, 0.36f, 0.78f), 1 - v * 1.4f);
                if (new Vector2(u - 0.68f, v - 0.42f).Length() < 0.11f) c = new Color(1f, 0.9f, 0.6f);
                var hill = 0.62f + 0.08f * Mathf.Sin(u * 7f) + 0.04f * Mathf.Sin(u * 17f + 1);
                if (v > hill) c = new Color(0.42f, 0.7f, 0.46f).Lerp(new Color(0.25f, 0.48f, 0.4f), (v - hill) * 3);
                var hill2 = 0.78f + 0.05f * Mathf.Sin(u * 5f + 2);
                if (v > hill2) c = new Color(0.62f, 0.82f, 0.42f);
            }
            else
            {
                c = new Color(0.2f, 0.16f, 0.38f).Lerp(new Color(0.95f, 0.45f, 0.55f), Mathf.Clamp(1.2f - new Vector2(u - 0.5f, v - 0.45f).Length() * 2.2f, 0, 1));
                var px = u - 0.44f;
                var py = Mathf.Abs(v - 0.5f);
                if (px > 0 && px < 0.2f && py < (0.2f - px) * 0.6f) c = new Color(1f, 0.95f, 0.85f);
            }

            img.SetPixel(x, y, c);
        }

        return ImageTexture.CreateFromImage(img);
    }

    // ------------------------------------------------------------------ satellites (one per other theme)

    /// <summary>
    /// One small planet per other theme, in a loose ring round the showcase (36° apart, alternately 78 and 100 m out, in
    /// three height tiers), each tilted ≈ 39° from vertical towards the showcase, so its pole (with the landmark) leans towards the cameras.
    /// </summary>
    private void BuildSatellites()
    {
        var themes = Enum.GetValues<PlanetTheme>().Where(t => t != PlanetTheme.Hearth).ToArray();
        var cache = new Dictionary<(DecorationKind, PlanetTheme, int), ArrayMesh>();
        for (var i = 0; i < themes.Length; i++)
        {
            var theme = themes[i];
            var p = PlanetArt.Palette(theme);
            var radius = theme == PlanetTheme.Quiet ? 9f : 10f + (i * 3 % 4);
            var az = Mathf.DegToRad(i * 36f + 8f);
            var dist = 78f + (i % 2) * 22f;
            var centre = new Vector3(Mathf.Sin(az) * dist, ShowcaseRadius + 14f + (i % 3) * 13f, Mathf.Cos(az) * dist);
            var toShowcase = new Vector3(-centre.X, 0, -centre.Z).Normalized();
            var up = (Vector3.Up * 0.55f + toShowcase * 0.45f).Normalized();
            var basis = Frame(up, 0);
            _satellites.Add((theme, centre, basis, radius));

            var poleSpot = (NVector3.UnitY, 3.4f);
            var groves = new List<(NVector3, float)> { poleSpot };
            for (var g = 0; g < 3; g++)
            {
                var a = Mathf.DegToRad(g * 120f + 60f);
                var d = new Vector3(Mathf.Sin(a) * Mathf.Sin(1.0f), Mathf.Cos(1.0f), Mathf.Cos(a) * Mathf.Sin(1.0f));
                groves.Add((N(d), 2.6f));
            }

            var shape = new PlanetShape(radius, 100 + i, theme is PlanetTheme.Quiet or PlanetTheme.Veiled ? 0.6f : 1.3f, groves);
            var node = new Node3D { Name = $"Planet{theme}", Transform = new Transform3D(basis, centre) };
            AddChild(node);
            node.AddChild(new MeshInstance3D { Mesh = PlanetArt.PlanetMesh(shape, p, PlanetArt.SubdivisionsFor(radius)) });
            node.AddChild(PlanetArt.Atmosphere(radius, p));
            Transform3D At(Vector3 dir, float yaw, float height = 0)
            {
                dir = dir.Normalized();
                return new Transform3D(Frame(dir, yaw), dir * (shape.GroundRadius(N(dir)) + height));
            }

            Vector3 G(int g) => new(groves[g].Item1.X, groves[g].Item1.Y, groves[g].Item1.Z);
            Vector3 Offset(Vector3 dir, float east, float north)
            {
                var f = Frame(dir.Normalized(), 0);
                return (dir.Normalized() * radius + f.X * east - f.Z * north).Normalized();
            }

            if (theme != PlanetTheme.Veiled)
                node.AddChild(new MeshInstance3D { Mesh = PlanetArt.Landmark(theme), Transform = At(Vector3.Up, 15) });
            else
                node.AddChild(new MeshInstance3D { Mesh = PlanetArt.Landmark(theme), Transform = At(Vector3.Up, 0) });

            if (theme is not (PlanetTheme.Quiet or PlanetTheme.Veiled))
            {
                var cats = GroveFor(theme);
                for (var k = 0; k < cats.Length; k++)
                    Spawn(PlanetArt.FileObject(cats[k]), At(Offset(G(1), (k - 1) * 1.5f, 0), 0), null, node);
                Spawn(PlanetArt.Pile(cats[0]), At(G(3), 30), null, node);
            }

            var launcher = PlanetArt.Launcher(p).Instantiate(0.85f);
            launcher.Transform = At(G(2), 0);
            node.AddChild(launcher);

            var rng = new Random(31 * i + 7);
            var kinds = DecorationsFor(theme);
            for (var k = 0; k < 26; k++)
            {
                var d = PlanetShape.RandomDirection(rng);
                var dir = new Vector3(d.X, d.Y, d.Z);
                if (groves.Any(g => PlanetShape.Angle(d, g.Item1) * radius < g.Item2 + 1.6f)) continue;
                var kind = kinds[rng.Next(kinds.Length)];
                var key = (kind, theme, rng.Next(3));
                if (!cache.TryGetValue(key, out var mesh)) cache[key] = mesh = PlanetArt.Decoration(kind, theme, key.Item3);
                node.AddChild(new MeshInstance3D { Mesh = mesh, Transform = At(dir, (float)rng.NextDouble() * 360f, -0.05f) });
            }
        }
    }

    private static FileCategory[] GroveFor(PlanetTheme t) => t switch
    {
        PlanetTheme.Meadow => [FileCategory.Image, FileCategory.Image, FileCategory.Image],
        PlanetTheme.Workshop => [FileCategory.Code, FileCategory.Data, FileCategory.Code],
        PlanetTheme.Library => [FileCategory.Document, FileCategory.Document, FileCategory.Other],
        PlanetTheme.Chime => [FileCategory.Audio, FileCategory.Audio, FileCategory.Audio],
        PlanetTheme.Theatre => [FileCategory.Video, FileCategory.Video, FileCategory.Image],
        PlanetTheme.Vault => [FileCategory.Archive, FileCategory.Executable, FileCategory.Archive],
        PlanetTheme.Crystal => [FileCategory.Data, FileCategory.Data, FileCategory.Data],
        _ => [FileCategory.Image, FileCategory.Code, FileCategory.Audio],
    };

    private static DecorationKind[] DecorationsFor(PlanetTheme t) => t switch
    {
        PlanetTheme.Crystal => [DecorationKind.Crystal, DecorationKind.Crystal, DecorationKind.Tree, DecorationKind.Rock, DecorationKind.Grass],
        PlanetTheme.Quiet => [DecorationKind.Rock, DecorationKind.Rock, DecorationKind.Tree, DecorationKind.Grass, DecorationKind.Lamp],
        PlanetTheme.Veiled => [DecorationKind.Rock, DecorationKind.Tree, DecorationKind.Crystal, DecorationKind.Grass],
        PlanetTheme.Workshop => [DecorationKind.Tree, DecorationKind.Tree, DecorationKind.Lamp, DecorationKind.Rock, DecorationKind.Grass, DecorationKind.Mushroom],
        PlanetTheme.Library => [DecorationKind.Tree, DecorationKind.Lamp, DecorationKind.Lamp, DecorationKind.Flower, DecorationKind.Grass],
        PlanetTheme.Chime => [DecorationKind.Flower, DecorationKind.Flower, DecorationKind.Tree, DecorationKind.Grass, DecorationKind.Lamp],
        _ => [DecorationKind.Tree, DecorationKind.Tree, DecorationKind.Flower, DecorationKind.Flower, DecorationKind.Grass, DecorationKind.Rock, DecorationKind.Mushroom, DecorationKind.Lamp],
    };

    // ------------------------------------------------------------------ the avatar demo

    /// <summary>Starts a demo: resets the jump state; a jump takes off at once; a flight plans its quadratic Bézier from the pad towards the third satellite.</summary>
    private void SetMode(Demo mode)
    {
        _mode = mode;
        _modeSeconds = 0;
        _airJumped = false;
        _height = 0;
        _verticalSpeed = 0;
        if (mode == Demo.Jump) _verticalSpeed = JumpTakeOffMetresPerSecond;
        if (mode == Demo.Fly)
        {
            _flyFrom = OnShowcase(LauncherAt.X, LauncherAt.Y, PadTopMetres);
            var target = _satellites[2].Centre;
            _flyTo = _flyFrom + (target - _flyFrom).Normalized() * FlightLengthMetres;
            _flyCtrl = (_flyFrom + _flyTo) / 2 + _flyFrom.Normalized() * FlightArcLiftMetres;
        }
    }

    /// <summary>
    /// Poses and animates the traveller for the current demo (a scripted stand-in for the walker: no physics, so the shots
    /// are repeatable). Interactively a wind-up leads to a flight and a flight back to running; automated, the script
    /// switches modes itself.
    /// </summary>
    private void DriveAvatar(float dt)
    {
        _modeSeconds += dt;
        var state = new AvatarState();
        Vector3 pos, up, facing;
        void Circle(float height)
        {
            _runAngle += dt * RunSpeedMetersPerSecond / RunRadiusMetres;
            var x = RunCentre.X + Mathf.Cos(_runAngle) * RunRadiusMetres;
            var z = RunCentre.Y + Mathf.Sin(_runAngle) * RunRadiusMetres;
            var ahead = new Vector2(-Mathf.Sin(_runAngle), Mathf.Cos(_runAngle));
            pos = OnShowcase(x, z, height);
            up = pos.Normalized();
            facing = OnShowcase(x + ahead.X * 0.1f, z + ahead.Y * 0.1f, height) - pos;
        }

        pos = up = facing = Vector3.Zero;
        switch (_mode)
        {
            case Demo.Run:
                Circle(0);
                state = new AvatarState(RunSpeedMetersPerSecond);
                break;
            case Demo.Jump:
            {
                const float g = GravityMetresPerSecondSquared;
                var jumped = _modeSeconds <= dt;
                var airJump = false;
                if (!_airJumped && _verticalSpeed < AirJumpBelowMetresPerSecond) // near the apex: the air jump (with its twirl)
                {
                    _airJumped = true;
                    _verticalSpeed = AirJumpMetresPerSecond;
                    airJump = true;
                }

                _verticalSpeed -= g * dt;
                _height += _verticalSpeed * dt;
                var landed = false;
                if (_height <= 0 && !jumped)
                {
                    _height = 0;
                    landed = true;
                }

                Circle(_height);
                state = new AvatarState(RunSpeedMetersPerSecond, landed, _verticalSpeed, jumped, airJump, landed);
                if (landed) _mode = Demo.Run;
                break;
            }

            case Demo.WindUp:
            {
                pos = OnShowcase(LauncherAt.X, LauncherAt.Y, PadTopMetres);
                up = pos.Normalized();
                facing = _satellites[2].Centre - pos;
                var w = Mathf.Clamp(_modeSeconds / WindUpSeconds, 0, 1);
                _launcherArt.SetOpen(_launcher, 1 - 0.6f * w);
                state = new AvatarState(0, true, 0, WindUp01: w);
                if (_modeSeconds > WindUpHoldSeconds && !_automate) SetMode(Demo.Fly);
                break;
            }

            case Demo.Fly:
            {
                var t = Mathf.Clamp(_modeSeconds / FlightSeconds, 0, 1);
                var s = 1 - Mathf.Pow(1 - t, FlightEasePower); // fast off the pad, slowing towards the end
                Vector3 B(float u) => _flyFrom.Lerp(_flyCtrl, u).Lerp(_flyCtrl.Lerp(_flyTo, u), u);
                pos = B(s);
                var tangent = (B(Mathf.Min(s + 0.01f, 1)) - B(Mathf.Max(s - 0.01f, 0))).Normalized();
                var planetUp = pos.Normalized();
                up = (planetUp - tangent * planetUp.Dot(tangent)).Normalized();
                facing = tangent;
                _launcherArt.SetOpen(_launcher, Mathf.Min(1, 0.4f + t * 2)); // the petals spring back open over the first 30 % of the flight
                state = new AvatarState(0, false, 0, Flying: true);
                if (t >= 1 && !_automate) SetMode(Demo.Run);
                break;
            }

            default:
                pos = OnShowcase(-1.5f, -2.6f);
                up = pos.Normalized();
                facing = OnShowcase(-1.5f, -5f) - pos;
                break;
        }

        _avatar.SetPose(pos, up, facing);
        _avatar.Animate(dt, state);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _clockSeconds += dt;
        DriveAvatar(dt);
        _cameraFollow?.Invoke();
        if (_automate) return;
        _shotSeconds += dt;
        if (_shotSeconds > ShotHoldSeconds) NextShot();
        if (_mode == Demo.Run && _modeSeconds > RunBeforeTrickSeconds) SetMode(((int)(_clockSeconds / RunBeforeTrickSeconds)) % 2 == 0 ? Demo.Jump : Demo.WindUp);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } k)
        {
            if (k.Keycode == Key.Escape) GetTree().Quit();
            if (k.Keycode is Key.Space or Key.Right) NextShot();
        }
    }

    private void NextShot()
    {
        _shot = (_shot + 1) % _shots.Count;
        _shotSeconds = 0;
        _cameraFollow = null;
        _shots[_shot].Frame();
    }

    // ------------------------------------------------------------------ camera shots

    private void Look(Vector3 from, Vector3 to, Vector3? up = null)
    {
        _camera.LookAtFromPosition(from, to, up ?? from.Normalized());
    }

    /// <summary>A shot that follows the traveller from behind, above and to the side (metres in its frame), looking at its chest (0.8 m up).</summary>
    private void Chase(float behindMetres, float aboveMetres, float sideMetres = 0)
    {
        _cameraFollow = () =>
        {
            var b = _avatar.GlobalBasis;
            var up = b.Y.Normalized();
            var back = b.Z.Normalized();
            var at = _avatar.GlobalPosition + up * 0.8f;
            Look(at + back * behindMetres + up * aboveMetres + b.X.Normalized() * sideMetres, at, up);
        };
        _cameraFollow();
    }

    /// <summary>The named camera shots, in order (their names are the screenshot file names).</summary>
    private void DefineShots()
    {
        _shots.Add(("01-wide", () => Look(new Vector3(40, 150, 205), new Vector3(0, 40, 0), Vector3.Up)));
        _shots.Add(("02-from-the-surface", () => Look(OnShowcase(2, 12, 2.2f), OnShowcase(-2, -14, 7f))));
        _shots.Add(("03-files-left", () => Look(OnShowcase(-5f, -3.6f, 1.5f), OnShowcase(-5f, 0, 0.55f))));
        _shots.Add(("04-files-right", () => Look(OnShowcase(5f, -3.6f, 1.5f), OnShowcase(5f, 0, 0.55f))));
        _shots.Add(("05-piles", () => Look(OnShowcase(0, -5.5f, 4.2f), OnShowcase(0, 3.5f, 0.6f))));
        _shots.Add(("06-launcher-overlook", () => Look(OnShowcase(0.5f, -1.0f, 3.0f), OnShowcase(0, -9f, 1.2f))));
        _shots.Add(("07-avatar-run", () => { SetMode(Demo.Run); Chase(6.5f, 2.6f); }));
        _shots.Add(("08-avatar-portrait", () =>
        {
            SetMode(Demo.Idle);
            _cameraFollow = () =>
            {
                var b = _avatar.GlobalBasis;
                var up = b.Y.Normalized();
                var at = _avatar.GlobalPosition + up * 0.62f;
                Look(at - b.Z.Normalized() * 2.4f + b.X.Normalized() * 0.9f + up * 0.35f, at, up);
            };
        }));
    }

    // ------------------------------------------------------------------ automation (--automate=planet-gallery)

    /// <summary>The automated run: every shot, the trick shots, two contact sheets, then the log and exit code 0.</summary>
    private async Task Script()
    {
        await Frames(45); // settle before the first shot (count chosen by eye; rationale not recorded)
        foreach (var (name, frame) in _shots)
        {
            _cameraFollow = null;
            frame();
            await Seconds(name.Contains("avatar") ? 1.4 : 0.4);
            await Shot(name);
        }

        // The jump with its air-jump twirl, seen from the side.
        SetMode(Demo.Jump);
        Chase(0.8f, 1.4f, 5.2f);
        await Seconds(0.47);
        await Shot("09-avatar-twirl");
        await Seconds(1.0);

        // Wind-up on the launcher, then the flight.
        SetMode(Demo.WindUp);
        _cameraFollow = () =>
        {
            var b = _avatar.GlobalBasis;
            var up = b.Y.Normalized();
            var at = _avatar.GlobalPosition + up * 0.6f;
            Look(at - b.Z.Normalized() * 3.4f + b.X.Normalized() * 2.2f + up * 0.9f, at, up);
        };
        await Seconds(0.8);
        await Shot("10-avatar-windup");
        SetMode(Demo.Fly);
        Chase(5.0f, 3.4f, 1.8f);
        await Seconds(0.9);
        await Shot("11-avatar-flight");
        _cameraFollow = null;
        SetMode(Demo.Run);

        // Contact sheets: each satellite whole, then its landmark close up.
        await Sheet("12-planets-sheet", s =>
        {
            var view = (s.Basis.Y * 0.75f + (new Vector3(40, 150, 205) - s.Centre).Normalized() * 0.65f).Normalized();
            Look(s.Centre + view * s.Radius * 3.4f, s.Centre + s.Basis.Y * s.Radius * 0.25f, s.Basis.Y);
        });
        await Sheet("13-landmarks-sheet", s =>
        {
            var pole = s.Centre + s.Basis.Y * s.Radius;
            var front = -s.Basis.Z;
            Look(pole + front * 15f + s.Basis.Y * 5f + s.Basis.X * 3f, pole + s.Basis.Y * 4.2f, s.Basis.Y);
        });

        Log("done");
        System.IO.Directory.CreateDirectory(_out);
        System.IO.File.WriteAllText(System.IO.Path.Combine(_out, "planet-gallery.log"), _log.ToString());
        GetTree().Quit(0);
    }

    /// <summary>A contact sheet: each satellite framed by <paramref name="frame"/>, captured and shrunk into a 4-column grid of 400 × 225 tiles.</summary>
    private async Task Sheet(string name, Action<(PlanetTheme Theme, Vector3 Centre, Basis Basis, float Radius)> frame)
    {
        const int cols = 4, tileW = 400, tileH = 225;
        var rows = (_satellites.Count + cols - 1) / cols;
        var sheet = Image.CreateEmpty(cols * tileW, rows * tileH, false, Image.Format.Rgba8);
        sheet.Fill(new Color(0.05f, 0.04f, 0.1f));
        for (var i = 0; i < _satellites.Count; i++)
        {
            frame(_satellites[i]);
            await Frames(4);
            var img = GetViewport().GetTexture().GetImage();
            img.Convert(Image.Format.Rgba8);
            img.Resize(tileW, tileH, Image.Interpolation.Lanczos);
            sheet.BlitRect(img, new Rect2I(0, 0, tileW, tileH), new Vector2I(i % cols * tileW, i / cols * tileH));
        }

        System.IO.Directory.CreateDirectory(_out);
        var path = System.IO.Path.Combine(_out, name + ".png");
        sheet.SavePng(path);
        Log($"screenshot {path} ({string.Join(", ", _satellites.Select(s => s.Theme))})");
    }

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

    private void Log(string line)
    {
        GD.Print("[planet-gallery] " + line);
        _log.AppendLine(line);
    }

    /// <summary>Logs the triangle count of every piece of art (to watch the polygon budget), and how many normals the builder had to repair.</summary>
    private void LogTriangles()
    {
        Log($"triangles: showcase planet (r {ShowcaseRadius} m, subdivisions 5) {PlanetArt.Triangles(PlanetArt.PlanetMesh(_showcase, PlanetArt.Palette(PlanetTheme.Hearth), 5))}");
        Log($"triangles: small planet (r 10 m, subdivisions {PlanetArt.SubdivisionsFor(10)}) {PlanetArt.Triangles(PlanetArt.PlanetMesh(PlanetShape.Sphere(10), PlanetArt.Palette(PlanetTheme.Meadow), PlanetArt.SubdivisionsFor(10)))}");
        foreach (var c in Enum.GetValues<FileCategory>())
            Log($"triangles: file {c} {PlanetArt.Triangles(PlanetArt.FileObject(c).Mesh)}, pile {PlanetArt.Triangles(PlanetArt.Pile(c).Mesh)}");
        var l = PlanetArt.Launcher();
        Log($"triangles: launcher pad {PlanetArt.Triangles(l.Pad)}, core {PlanetArt.Triangles(l.Core)}, petals 5 x {PlanetArt.Triangles(l.Petals[0])}");
        Log($"triangles: overlook {PlanetArt.Triangles(PlanetArt.Overlook())}, stepping stone {PlanetArt.Triangles(PlanetArt.SteppingStone())}");
        foreach (var t in Enum.GetValues<PlanetTheme>())
            Log($"triangles: landmark {t} {PlanetArt.Triangles(PlanetArt.Landmark(t))}");
        foreach (var k in Enum.GetValues<DecorationKind>())
            Log($"triangles: decoration {k} (Hearth/Workshop/Library/Chime) {string.Join("/", new[] { PlanetTheme.Hearth, PlanetTheme.Workshop, PlanetTheme.Library, PlanetTheme.Chime }.Select(t => PlanetArt.Triangles(PlanetArt.Decoration(k, t, 1))))}");
        var avatar = 0;
        foreach (var mi in _avatar.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            if (mi.Mesh is ArrayMesh am) avatar += PlanetArt.Triangles(am);
        Log($"triangles: avatar {avatar} + scarf ribbon 40 (dynamic)");
        Log($"normals replaced (non-finite or zero): {MeshBuilder.NonFiniteNormals}");
    }
}
