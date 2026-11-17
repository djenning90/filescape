using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// A Hazard World's extra visuals (PLANET-34, PLANET-46; AD-53, AD-59): the spinning black hole at the centre with its jet
/// (<see cref="BlackHoleArt"/>), a glowing line along every broken edge that brightens as you near one
/// (<see cref="SetEdgeWarning"/>), rock crumbling off the edge nearest you, and chunks of broken crust tumbling in the open
/// sky. The shell itself is the ground mesh (<see cref="PlanetArt.BreakShell"/>), whose outline the edge line follows. A
/// preview (the world hanging in another's sky) gets the black hole, its jet and a few chunks, so a Hazard World reads as
/// one from afar.
/// </summary>
public partial class PlanetVisual
{
    /// <summary>The edge line's glow: always faintly on (energy), up to this much more at an edge (by eye).</summary>
    private const float RimGlowBaseEnergy = 0.55f, RimGlowWarnEnergy = 2.6f;

    private static readonly Color RimGlowColour = new(1f, 0.5f, 0.22f);

    /// <summary>Debris: chunks in the open sky (full world, preview), their size range (metres across) and how far from the centre they float, as fractions of the planet's radius.</summary>
    private const int DebrisFull = 26, DebrisPreview = 6;

    private const float DebrisMinMetres = 0.5f, DebrisMaxMetres = 1.9f, DebrisInnerRadii = 0.55f, DebrisOuterRadii = 1.18f;

    /// <summary>Crumbs falling off the edge nearest you: at most this many at once, each this big (metres), falling for this long (seconds), emitted while the warning is above this level.</summary>
    private const int CrumbAmount = 40;

    private const float CrumbMinMetres = 0.05f, CrumbMaxMetres = 0.16f, CrumbLifetimeSeconds = 2.2f, CrumbFromWarning = 0.15f;

    private ShaderMaterial? _rimGlow;
    private CpuParticles3D? _crumbs;
    private PlanetArt.RimOutline? _rim;
    private readonly List<(Node3D Node, Vector3 Axis, float RadiansPerSecond)> _debris = [];

    /// <summary>Rock crumbs are falling off the edge nearest the traveller (probes).</summary>
    public bool CrumbsEmitting => _crumbs?.Emitting ?? false;

    /// <summary>The outline's size (diagnostics; empty elsewhere).</summary>
    public string HazardStats { get; private set; } = "";

    /// <summary>The edge warning's level last set, 0..1 (probes).</summary>
    public float EdgeWarning { get; private set; }

    /// <summary>Builds the Hazard World's visuals; called from the constructor after the ground (whose outline <paramref name="rim"/> is).</summary>
    private void BuildHazard(BrokenShell shell, PlanetArt.RimOutline? rim)
    {
        _rim = rim;
        AddChild(new BlackHoleArt(shell, _full) { Name = "BlackHole" });
        var rng = new Random(shell.Lands.Count > 0 ? shell.Lands[0].Seed : 1);
        var count = _full ? DebrisFull : DebrisPreview;
        for (var i = 0; i < count; i++) AddDebris(shell, rng);
        if (!_full) return;
        _rimGlow = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/planet_glow.gdshader") };
        _rimGlow.SetShaderParameter("color", RimGlowColour);
        _rimGlow.SetShaderParameter("energy", RimGlowBaseEnergy);
        _rimGlow.SetShaderParameter("pulse", 0.25f);
        _rimGlow.SetShaderParameter("pulse_speed", 3f);
        if (rim != null && rim.Segments.Length > 0) AddChild(RimLine(rim));
        HazardStats = rim == null ? "" : $"outline {rim.Points.Length} points, {rim.Segments.Length / 2} segments";
        _crumbs = Crumbs();
        AddChild(_crumbs);
    }

    /// <summary>The glowing edge line: the ribbon along the whole outline (made on the ground worker: <see cref="PlanetArt.RimOutline"/>), uploaded here.</summary>
    private MeshInstance3D RimLine(PlanetArt.RimOutline rim)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = rim.GlowVertices;
        arrays[(int)Mesh.ArrayType.Normal] = rim.GlowNormals;
        arrays[(int)Mesh.ArrayType.Index] = rim.GlowIndices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return new MeshInstance3D { Name = "RimGlow", Mesh = mesh, MaterialOverride = _rimGlow, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    /// <summary>The crumbs: small rock bits dropping off the edge nearest the traveller (placed each frame by <see cref="SetEdgeWarning"/>), in the edge's own frame (local Y up).</summary>
    private static CpuParticles3D Crumbs()
    {
        var mesh = new BoxMesh { Size = Vector3.One };
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.32f, 0.36f), Roughness = 1f };
        mesh.Material = mat;
        return new CpuParticles3D
        {
            Name = "Crumbs",
            Mesh = mesh,
            Amount = CrumbAmount,
            Lifetime = CrumbLifetimeSeconds,
            Emitting = false,
            LocalCoords = true,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(1.2f, 0.05f, 0.25f),
            Direction = new Vector3(0, -1, 0),
            Spread = 25f,
            InitialVelocityMin = 0.2f,
            InitialVelocityMax = 0.9f,
            Gravity = new Vector3(0, -5f, 0),
            AngularVelocityMin = -180f,
            AngularVelocityMax = 180f,
            ScaleAmountMin = CrumbMinMetres,
            ScaleAmountMax = CrumbMaxMetres,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>One chunk of broken crust floating in the open sky (inside or above the shell), tumbling slowly.</summary>
    private void AddDebris(BrokenShell shell, Random rng)
    {
        var radius = shell.Radius;
        NVector3 dir = default;
        for (var tries = 0; tries < 40; tries++)
        {
            dir = NVector3.Normalize(new NVector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1));
            if (shell.EdgeMetres(dir) < -2f) break; // over open sky, clear of the rims
        }

        var r = radius * (DebrisInnerRadii + (DebrisOuterRadii - DebrisInnerRadii) * (float)rng.NextDouble());
        var size = DebrisMinMetres + (DebrisMaxMetres - DebrisMinMetres) * (float)rng.NextDouble();
        var seed = rng.Next(1 << 20);
        var key = $"debris-{seed % 6}";
        var node = new MeshInstance3D
        {
            Name = "Debris",
            Mesh = Shared(key, () => PlanetArt.DebrisChunk(seed % 6)),
            Position = Gd(dir) * r,
            Rotation = new Vector3((float)rng.NextDouble() * 6.3f, (float)rng.NextDouble() * 6.3f, (float)rng.NextDouble() * 6.3f),
            Scale = Vector3.One * size,
        };
        AddChild(node);
        var axis = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).Normalized();
        _debris.Add((node, axis, 0.15f + 0.4f * (float)rng.NextDouble())); // a slow tumble: one turn in 10–40 s
    }

    /// <summary>
    /// Sets the edge warning (0 far … 1 at an edge) for a traveller standing at <paramref name="feet"/> (planet frame): the
    /// edge lines brighten, and rock crumbs fall off the nearest piece of edge while the warning is up.
    /// </summary>
    public void SetEdgeWarning(float nearness01, Vector3? feet = null)
    {
        EdgeWarning = nearness01;
        _rimGlow?.SetShaderParameter("energy", RimGlowBaseEnergy + RimGlowWarnEnergy * nearness01 * nearness01);
        if (_crumbs == null) return;
        var emit = nearness01 > CrumbFromWarning && feet is { } f && _rim is { Points.Length: > 0 };
        if (emit)
        {
            // The nearest point of the outline (a few thousand points: a linear scan is a few microseconds).
            var best = 0;
            var bestD = float.MaxValue;
            for (var i = 0; i < _rim!.Points.Length; i++)
            {
                var d = _rim.Points[i].DistanceSquaredTo(feet!.Value);
                if (d < bestD) { bestD = d; best = i; }
            }

            var p = _rim.Points[best];
            var up = p.Normalized();
            var o = _rim.Out[best].LengthSquared() > 0.5f ? _rim.Out[best] : up.Cross(Vector3.Right).Normalized();
            var x = up.Cross(o).Normalized(); // along the edge
            _crumbs.Transform = new Transform3D(new Basis(x, up, o), p + o * 0.15f - up * 0.1f);
        }

        if (_crumbs.Emitting != emit) _crumbs.Emitting = emit;
        _crumbs.SpeedScale = 0.7f + 0.6f * nearness01;
    }

    /// <summary>The debris tumbles (called from <see cref="Animate"/>' caller each frame through <see cref="_Process"/>).</summary>
    public override void _Process(double delta)
    {
        if (_debris.Count == 0) return;
        var dt = (float)delta;
        foreach (var (node, axis, rate) in _debris)
        {
            if (IsInstanceValid(node)) node.Rotate(axis, rate * dt);
        }
    }

    private static Vector3 Gd(NVector3 v) => new(v.X, v.Y, v.Z);
}

/// <summary>
/// The black hole at a Hazard World's centre (PLANET-34, PLANET-46): a dark core, a photon ring, a fast-spinning accretion
/// disc that reaches about half way to the shell, and a jet each way along its spin axis (<see cref="BrokenShell.JetAxis"/>)
/// out through the open sky: the one ahead of the arrival climbs out of the opening in front of you as you land (AD-59),
/// the other is drawn only where the shell is open, too. On the world you stand on, an orange light lights the crust's
/// underside and rim walls from below. Sizes are fractions of the planet's radius, so the same art fits any planet; the
/// node's local Y is the spin axis (the fall spirals round the same axis), and it spins on its own (<see cref="_Process"/>).
/// </summary>
public partial class BlackHoleArt : Node3D
{
    /// <summary>The disc reaches this fraction of the planet's radius (inside the crust, which starts ≈ 0.9 of it).</summary>
    private const float DiscOuterRadii = 0.6f;

    /// <summary>The disc's inner edge, in core radii (just outside the photon ring).</summary>
    private const float DiscInnerCores = 1.5f;

    /// <summary>The disc node turns this fast, radians per second, on top of the shader's own swirl (fast: about a turn every 2.5 s).</summary>
    private const float DiscSpinRadiansPerSecond = 2.5f;

    /// <summary>A jet reaches this many planet radii from the centre, is this wide (metres per planet radius), at this energy (by eye, 2026-11-16).</summary>
    private const float JetLengthRadii = 3.2f, JetWidthPerRadius = 0.3f, JetEnergy = 2.4f, PreviewJetEnergy = 2.6f;

    /// <summary>The opening's glow: a soft halo this far out along the jet (planet radii, just under the shell), this big (planet radii), at this energy (by eye).</summary>
    private const float OpeningGlowAtRadii = 0.72f, OpeningGlowRadii = 0.32f, OpeningGlowEnergy = 0.7f;

    private readonly Node3D _disc;

    /// <summary>The core's radius, metres.</summary>
    public float CoreMetres { get; }

    public BlackHoleArt(BrokenShell shell, bool full)
    {
        var planetRadiusMetres = shell.Radius;
        CoreMetres = planetRadiusMetres * BrokenShell.HorizonRadiusFraction;
        Basis = AxisBasis(shell.JetAxis);
        AddChild(new MeshInstance3D
        {
            Name = "Core",
            Mesh = new SphereMesh { Radius = CoreMetres, Height = CoreMetres * 2, RadialSegments = 32, Rings = 16 },
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Colors.Black },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        AddChild(PlanetArt.BlackHoleGlow(CoreMetres, 1.7f));
        _disc = new Node3D { Name = "Spin" };
        AddChild(_disc);
        _disc.AddChild(PlanetArt.AccretionDisc(CoreMetres * DiscInnerCores, planetRadiusMetres * DiscOuterRadii, full ? 2.3f : 2.8f));
        // A thin second ring, tilted across the first: the disc's light bent over the top of the hole (a hint of lensing).
        var lensed = PlanetArt.AccretionDisc(CoreMetres * 1.15f, CoreMetres * 2.4f, 1.4f);
        lensed.RotationDegrees = new Vector3(80, 0, 0);
        AddChild(lensed);
        // The jets: along +Y (out through the opening ahead of the arrival) and −Y where the shell is open there too.
        AddChild(Jet(planetRadiusMetres, full ? JetEnergy : PreviewJetEnergy, flip: false));
        var glow = PlanetArt.Halo(new Color(0.95f, 0.6f, 1f), planetRadiusMetres * OpeningGlowRadii, OpeningGlowEnergy);
        glow.Position = Vector3.Up * (planetRadiusMetres * OpeningGlowAtRadii); // local: along the spin axis, light welling up through the opening
        AddChild(glow);
        if (shell.OverHole(-shell.JetAxis)) AddChild(Jet(planetRadiusMetres, full ? JetEnergy : PreviewJetEnergy, flip: true));
        if (full)
        {
            AddChild(new OmniLight3D
            {
                Name = "DiscLight",
                LightColor = new Color(1f, 0.6f, 0.35f),
                LightEnergy = 5f,
                OmniRange = planetRadiusMetres * 0.97f, // stops short of the ground's outer face (at the base radius and above)
                OmniAttenuation = 0.8f,
                ShadowEnabled = false,
            });
        }
    }

    /// <summary>A basis whose Y is <paramref name="axis"/> (the planet-frame spin axis), X and Z any perpendicular pair.</summary>
    public static Basis AxisBasis(NVector3 axis)
    {
        var y = new Vector3(axis.X, axis.Y, axis.Z).Normalized();
        var x = (MathF.Abs(y.Dot(Vector3.Right)) < 0.9f ? Vector3.Right : Vector3.Forward).Cross(y).Normalized();
        var z = x.Cross(y).Normalized();
        return new Basis(x, y, z);
    }

    /// <summary>One jet: a camera-facing beam (hazard_jet.gdshader) from the core along local +Y (or −Y).</summary>
    private static MeshInstance3D Jet(float radius, float energy, bool flip)
    {
        var length = radius * JetLengthRadii;
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/hazard_jet.gdshader") };
        m.SetShaderParameter("energy", energy);
        return new MeshInstance3D
        {
            Name = flip ? "JetBack" : "Jet",
            Mesh = new QuadMesh { Size = new Vector2(radius * JetWidthPerRadius, length), CenterOffset = new Vector3(0, length / 2, 0) },
            MaterialOverride = m,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // A wide cull margin: the quad turns to face the camera in the shader, so its own bounds understate it.
            ExtraCullMargin = radius * JetWidthPerRadius,
            RotationDegrees = flip ? new Vector3(180, 0, 0) : Vector3.Zero,
        };
    }

    public override void _Process(double delta) => _disc.RotateY(-DiscSpinRadiansPerSecond * (float)delta);
}
