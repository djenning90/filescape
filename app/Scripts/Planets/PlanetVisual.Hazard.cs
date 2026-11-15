using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// A hazard world's extra visuals (PLANET-34, AD-53): the spinning black hole at the centre (<see cref="BlackHoleArt"/>),
/// a glowing line along every broken edge that brightens as you near one (<see cref="SetEdgeWarning"/>), a glow seen down
/// through each hole, and chunks of broken crust tumbling in and above the holes. The shell itself is the ground mesh
/// (<see cref="PlanetArt.BreakShell"/>). A preview (the world hanging in another's sky) gets the black hole and a few
/// chunks, so a broken world reads as one from afar.
/// </summary>
public partial class PlanetVisual
{
    /// <summary>The edge line's glow: always faintly on (energy), up to this much more at an edge (by eye).</summary>
    private const float RimGlowBaseEnergy = 0.35f, RimGlowWarnEnergy = 2.6f;

    /// <summary>The edge line: this wide, metres, lying just inside the edge (on the lip of the rim wall), this far above the ground.</summary>
    private const float RimLineWidthMetres = 0.4f, RimLineLiftMetres = 0.05f;

    private static readonly Color RimGlowColour = new(1f, 0.5f, 0.22f);

    /// <summary>Debris: chunks per hole (full world), their size range (metres across) and how far from the centre they float, as fractions of the planet's radius.</summary>
    private const int DebrisPerHole = 5;

    private const float DebrisMinMetres = 0.5f, DebrisMaxMetres = 1.9f, DebrisInnerRadii = 0.55f, DebrisOuterRadii = 1.22f;

    private ShaderMaterial? _rimGlow;
    private readonly List<(Node3D Node, Vector3 Axis, float RadiansPerSecond)> _debris = [];

    /// <summary>The edge warning's level last set, 0..1 (probes).</summary>
    public float EdgeWarning { get; private set; }

    /// <summary>Builds the hazard visuals; called from the constructor after the ground.</summary>
    private void BuildHazard(BrokenShell shell)
    {
        AddChild(new BlackHoleArt(shell.Radius, _full) { Name = "BlackHole" });
        var rng = new Random(shell.Holes.Count > 0 ? shell.Holes[0].Seed : 1);
        foreach (var hole in shell.Holes)
        {
            var count = _full ? DebrisPerHole : 1;
            for (var i = 0; i < count; i++) AddDebris(hole, shell.Radius, rng);
            AddChild(Plume(hole, shell.Radius));
        }

        if (!_full) return;
        _rimGlow = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/planet_glow.gdshader") };
        _rimGlow.SetShaderParameter("color", RimGlowColour);
        _rimGlow.SetShaderParameter("energy", RimGlowBaseEnergy);
        _rimGlow.SetShaderParameter("pulse", 0.25f);
        _rimGlow.SetShaderParameter("pulse_speed", 3f);
        foreach (var hole in shell.Holes)
        {
            AddChild(RimLine(hole, _comp.Shape));
            // Light leaking up through the hole: a soft halo low in it, seen from the rim.
            var halo = PlanetArt.Halo(new Color(0.95f, 0.55f, 1f), MathF.Sin(hole.RadiusRadians) * shell.Radius * 0.9f, 0.35f);
            halo.Position = Gd(hole.Centre) * (shell.Radius * 0.55f);
            AddChild(halo);
        }
    }

    /// <summary>The glowing edge line for one hole: a ribbon along the edge, from the edge a little way into the hole.</summary>
    private MeshInstance3D RimLine(ShellHole hole, PlanetShape shape)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var perimeter = 2 * MathF.PI * MathF.Sin(hole.RadiusRadians) * shape.Radius;
        var n = Math.Max(96, (int)(perimeter / 0.3f));
        var centre = Gd(hole.Centre);
        var outer = new Vector3[n];
        var inner = new Vector3[n];
        for (var k = 0; k < n; k++)
        {
            var b = hole.BoundaryPoint(Mathf.Tau * k / n);
            var up = Gd(b);
            var inward = (centre - up * centre.Dot(up)).Normalized();
            var p = up * (shape.GroundRadius(b) + RimLineLiftMetres);
            outer[k] = p - inward * (RimLineWidthMetres * 0.25f);
            inner[k] = p + inward * (RimLineWidthMetres * 0.75f) - up * 0.06f;
        }

        for (var k = 0; k < n; k++)
        {
            var k1 = (k + 1) % n;
            foreach (var v in new[] { outer[k], inner[k], inner[k1], outer[k], inner[k1], outer[k1] })
            {
                st.SetNormal(v.Normalized());
                st.AddVertex(v);
            }
        }

        // Both windings, so it shows from above and from the far side of the hole alike.
        var mesh = st.Commit();
        return new MeshInstance3D { Name = "RimGlow", Mesh = mesh, MaterialOverride = _rimGlow, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    /// <summary>
    /// The light rising out of a hole (hazard_plume.gdshader): from inside the shell up to <see cref="PlumeHeightRadii"/>
    /// planet radii above it, about as wide as the hole: as you walk out towards a hole, its light shows above the rim before
    /// the edge does. (2026-11-15 screenshots: it is not visible from the sample's arrival, against the bright ember sky;
    /// the caution banner carries the warning there.)
    /// </summary>
    private static MeshInstance3D Plume(ShellHole hole, float radius)
    {
        var width = 2 * MathF.Sin(hole.RadiusRadians) * radius * PlumeWidthOfHole;
        var height = radius * PlumeHeightRadii;
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/hazard_plume.gdshader") };
        m.SetShaderParameter("energy", PlumeEnergy);
        var up = Gd(hole.Centre);
        var (a, _) = PlanetShape.Tangents(hole.Centre);
        var x = Gd(a);
        var z = x.Cross(up).Normalized();
        return new MeshInstance3D
        {
            Name = "Plume",
            Mesh = new QuadMesh { Size = new Vector2(width, height), CenterOffset = new Vector3(0, height / 2, 0) },
            MaterialOverride = m,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Standing on the hole's axis, from a little inside the shell (so its foot glows in the hole).
            Transform = new Transform3D(new Basis(x, up, z), up * (radius * 0.8f)),
        };
    }

    /// <summary>A plume is this fraction of its hole's width, reaches this many planet radii out from 0.8 of the radius, at this energy (by eye).</summary>
    private const float PlumeWidthOfHole = 0.8f, PlumeHeightRadii = 2.6f, PlumeEnergy = 1.0f;

    /// <summary>One chunk of broken crust floating in or above a hole, tumbling slowly.</summary>
    private void AddDebris(ShellHole hole, float radius, Random rng)
    {
        var (a, b) = PlanetShape.Tangents(hole.Centre);
        var bearing = (float)(rng.NextDouble() * Math.PI * 2);
        var off = (float)rng.NextDouble() * hole.MinRadiusRadians * 0.85f;
        var t = a * MathF.Cos(bearing) + b * MathF.Sin(bearing);
        var dir = NVector3.Normalize(hole.Centre * MathF.Cos(off) + t * MathF.Sin(off));
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

    /// <summary>Sets the edge warning (0 far … 1 at an edge): the edge lines brighten.</summary>
    public void SetEdgeWarning(float nearness01)
    {
        EdgeWarning = nearness01;
        _rimGlow?.SetShaderParameter("energy", RimGlowBaseEnergy + RimGlowWarnEnergy * nearness01 * nearness01);
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
/// The black hole at a hazard world's centre (PLANET-34): a dark core, a photon ring, a fast-spinning accretion disc that
/// reaches about half way to the shell, and (on the world you stand on) an orange light that lights the crust's underside
/// and rim walls from below. Sizes are fractions of the planet's radius, so the same art fits any planet; it spins on its
/// own (<see cref="_Process"/>), whether it is the current world or a preview in the sky.
/// </summary>
public partial class BlackHoleArt : Node3D
{
    /// <summary>The disc reaches this fraction of the planet's radius (inside the crust, which starts ≈ 0.9 of it).</summary>
    private const float DiscOuterRadii = 0.6f;

    /// <summary>The disc's inner edge, in core radii (just outside the photon ring).</summary>
    private const float DiscInnerCores = 1.5f;

    /// <summary>The disc node turns this fast, radians per second, on top of the shader's own swirl (fast: about a turn every 2.5 s).</summary>
    private const float DiscSpinRadiansPerSecond = 2.5f;

    /// <summary>The disc is tilted this much from the planet's equator, degrees, so it never lies exactly edge-on in a side hole.</summary>
    private const float DiscTiltDegrees = 14f;

    private readonly Node3D _disc;

    /// <summary>The core's radius, metres.</summary>
    public float CoreMetres { get; }

    public BlackHoleArt(float planetRadiusMetres, bool full)
    {
        CoreMetres = planetRadiusMetres * BrokenShell.HorizonRadiusFraction;
        AddChild(new MeshInstance3D
        {
            Name = "Core",
            Mesh = new SphereMesh { Radius = CoreMetres, Height = CoreMetres * 2, RadialSegments = 32, Rings = 16 },
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Colors.Black },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        AddChild(PlanetArt.BlackHoleGlow(CoreMetres, 1.7f));
        var tilt = new Node3D { Name = "Tilt", RotationDegrees = new Vector3(DiscTiltDegrees, 0, DiscTiltDegrees * 0.5f) };
        AddChild(tilt);
        _disc = new Node3D { Name = "Spin" };
        tilt.AddChild(_disc);
        _disc.AddChild(PlanetArt.AccretionDisc(CoreMetres * DiscInnerCores, planetRadiusMetres * DiscOuterRadii, full ? 2.3f : 2.8f));
        // A thin second ring, tilted across the first: the disc's light bent over the top of the hole (a hint of lensing).
        var lensed = PlanetArt.AccretionDisc(CoreMetres * 1.15f, CoreMetres * 2.4f, 1.4f);
        lensed.RotationDegrees = new Vector3(80, 0, 0);
        AddChild(lensed);
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

    public override void _Process(double delta) => _disc.RotateY(-DiscSpinRadiansPerSecond * (float)delta);
}
