using FileScape.Core.Interaction;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// One planet's colours ("Lantern Cosmos", PLANET-10). Colours are authored in sRGB; <see cref="PlanetArt"/> converts
/// vertex colours to linear when it bakes them.
/// </summary>
/// <param name="GroundLow">Ground in hollows and low ground.</param>
/// <param name="GroundHigh">Ground on hilltops.</param>
/// <param name="Accent">The planet's signature colour (flower patches, landmark trim, petals).</param>
/// <param name="Path">Lighter worn paths and clearings around flat spots.</param>
/// <param name="SkyTop">Sky straight up.</param>
/// <param name="SkyHorizon">Sky at the horizon (the violet glow band).</param>
/// <param name="Fog">Aerial-perspective tint for distant things.</param>
/// <param name="Sun">The sun's colour.</param>
/// <param name="Ambient">Ambient (sky fill) light colour.</param>
/// <param name="Glow">Lantern and launcher glow colour.</param>
public sealed record PlanetPalette(
    Color GroundLow, Color GroundHigh, Color Accent, Color Path,
    Color SkyTop, Color SkyHorizon, Color Fog, Color Sun, Color Ambient, Color Glow);

/// <summary>The kinds of scenery <see cref="PlanetArt.Decoration"/> draws (each has theme variants).</summary>
public enum DecorationKind
{
    Tree,
    Rock,
    Flower,
    Grass,
    Mushroom,
    Crystal,
    Lamp,
}

/// <summary>
/// A file object or pile mesh. Surface 0 is the body (toon material, vertex colours, glowing lantern parts baked in).
/// <see cref="FaceSurface"/> is the index of a quad with UVs (0,0 top-left … 1,1 bottom-right) on which the app can show a
/// real thumbnail (Image, Video), or −1. <see cref="FaceSize"/> is that quad's width × height in metres (aspect).
/// <see cref="Lantern"/> is the local position of the object's glowing part (for a halo or a light), metres.
/// <see cref="HeightMetres"/> is the object's approximate height at scale 1, metres (1.0 for most; 1.1 for the rocket;
/// 1.6 for a pile).
/// </summary>
public sealed record FileArt(ArrayMesh Mesh, int FaceSurface, Vector2 FaceSize, Vector3 Lantern, float HeightMetres);

/// <summary>
/// The bloom launcher's parts: a pad (walkable top at <see cref="PadTopMetres"/>), a glowing core, and six petal meshes
/// hinged at the pad's inner ring that the app rotates open or closed (<see cref="PetalPose"/>). Local +Y up, origin at
/// the pad's base centre.
/// </summary>
public sealed record LauncherArt(ArrayMesh Pad, ArrayMesh Core, IReadOnlyList<ArrayMesh> Petals, float PadTopMetres, float PadRadiusMetres)
{
    /// <summary>Hinge radius of the petals from the centre, metres.</summary>
    public const float HingeRadiusMetres = 0.62f;

    /// <summary>Petal tilt above the pad when fully closed (a bud) and fully open (a flat bloom), degrees.</summary>
    public const float ClosedTiltDegrees = 74f, OpenTiltDegrees = 6f;

    /// <summary>
    /// Local transform of petal <paramref name="index"/> (0–5) at openness <paramref name="open01"/> (0 = bud, 1 = open,
    /// clamped): the petal is turned round the pad by its share of the circle, offset by half a share (so no petal points
    /// straight along +Z), hinged 2 cm below the walkable top (its root tucks into the pad), then tilted up about its hinge.
    /// </summary>
    public Transform3D PetalPose(int index, float open01)
    {
        var yaw = index * Mathf.Tau / Petals.Count + Mathf.Pi / Petals.Count;
        var tilt = Mathf.DegToRad(Mathf.Lerp(ClosedTiltDegrees, OpenTiltDegrees, Mathf.Clamp(open01, 0, 1)));
        var basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, -tilt);
        var origin = new Basis(Vector3.Up, yaw) * new Vector3(0, PadTopMetres - 0.02f, HingeRadiusMetres);
        return new Transform3D(basis, origin);
    }

    /// <summary>A node with children "Pad", "Core" and "Petal0".."Petal5" (one per petal, at <paramref name="open01"/>); the app finds the petals and the core by these names.</summary>
    public Node3D Instantiate(float open01 = 1f)
    {
        var root = new Node3D { Name = "Launcher" };
        root.AddChild(new MeshInstance3D { Name = "Pad", Mesh = Pad });
        root.AddChild(new MeshInstance3D { Name = "Core", Mesh = Core, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        for (var i = 0; i < Petals.Count; i++)
        {
            root.AddChild(new MeshInstance3D { Name = $"Petal{i}", Mesh = Petals[i], Transform = PetalPose(i, open01) });
        }

        return root;
    }

    /// <summary>Opens or closes the petals of a node made by <see cref="Instantiate"/>.</summary>
    public void SetOpen(Node3D launcher, float open01)
    {
        for (var i = 0; i < Petals.Count; i++)
        {
            if (launcher.GetNodeOrNull<Node3D>($"Petal{i}") is { } p) p.Transform = PetalPose(i, open01);
        }
    }
}

/// <summary>
/// Original procedural art for the Planets experience ("Lantern Cosmos": soft, rounded, toy-like forms, warm toon
/// shading with rim light, deep indigo-violet sky, saturated pastel planets, files glowing like lanterns). Everything is
/// built from code (no imported assets, no third-party artwork). Units are metres; local +Y is up; a prop's front faces
/// −Z and its base is at y = 0. Meshes carry vertex colours; vertex alpha below 1 marks glowing parts (see
/// planet_toon.gdshader). Builders allocate a new mesh per call; callers cache them.
/// </summary>
public static partial class PlanetArt
{
    /// <summary>
    /// The radius the overlook and the workbench meshes are built at, metres: the composer's default platform radius
    /// (<see cref="PlanetParams.OverlookRadius"/>); <see cref="PlanetVisual"/> scales the mesh by the composed radius over
    /// this, so the drawn platform is the walked one.
    /// </summary>
    internal const float OverlookRadiusMetres = 2.2f;

    /// <summary>The overlook mesh's platform top, metres (the composer's default <see cref="PlanetParams.OverlookTop"/>); the telescope stands on it.</summary>
    internal const float OverlookTopMeshMetres = 2.4f;

    // ------------------------------------------------------------------ palettes

    /// <summary>A colour from sRGB channels 0–1 (opaque: alpha 1 also means "not glowing", see <see cref="Glowing"/>).</summary>
    private static Color C(float r, float g, float b) => new(r, g, b);

    /// <summary>The palette of a theme: eleven distinct, harmonious, saturated pastel worlds under one shared night sky.</summary>
    public static PlanetPalette Palette(PlanetTheme theme) => theme switch
    {
        PlanetTheme.Hearth => new(C(0.93f, 0.60f, 0.44f), C(1.00f, 0.80f, 0.56f), C(0.93f, 0.40f, 0.44f), C(1.00f, 0.92f, 0.74f),
            C(0.06f, 0.04f, 0.17f), C(0.32f, 0.17f, 0.40f), C(0.44f, 0.27f, 0.46f), C(1.00f, 0.88f, 0.72f), C(0.47f, 0.37f, 0.63f), C(1.00f, 0.76f, 0.40f)),
        PlanetTheme.Meadow => new(C(0.42f, 0.76f, 0.46f), C(0.74f, 0.90f, 0.50f), C(1.00f, 0.55f, 0.74f), C(0.98f, 0.93f, 0.72f),
            C(0.05f, 0.05f, 0.18f), C(0.22f, 0.20f, 0.46f), C(0.30f, 0.38f, 0.56f), C(1.00f, 0.95f, 0.80f), C(0.40f, 0.45f, 0.66f), C(1.00f, 0.86f, 0.50f)),
        PlanetTheme.Workshop => new(C(0.84f, 0.54f, 0.32f), C(0.96f, 0.74f, 0.42f), C(0.30f, 0.72f, 0.70f), C(0.99f, 0.87f, 0.62f),
            C(0.07f, 0.04f, 0.12f), C(0.34f, 0.18f, 0.30f), C(0.45f, 0.29f, 0.33f), C(1.00f, 0.85f, 0.65f), C(0.48f, 0.38f, 0.52f), C(1.00f, 0.70f, 0.34f)),
        // 2026-11-11, after the first playthrough screenshots: deeper lavender ground with warm paper paths (it read almost white).
        PlanetTheme.Library => new(C(0.60f, 0.46f, 0.74f), C(0.86f, 0.70f, 0.82f), C(0.92f, 0.44f, 0.48f), C(0.99f, 0.90f, 0.76f),
            C(0.05f, 0.04f, 0.17f), C(0.24f, 0.18f, 0.46f), C(0.36f, 0.30f, 0.58f), C(1.00f, 0.93f, 0.85f), C(0.45f, 0.42f, 0.70f), C(1.00f, 0.88f, 0.60f)),
        PlanetTheme.Chime => new(C(0.36f, 0.78f, 0.80f), C(0.64f, 0.93f, 0.88f), C(0.78f, 0.62f, 1.00f), C(0.92f, 0.99f, 0.92f),
            C(0.04f, 0.05f, 0.17f), C(0.16f, 0.22f, 0.46f), C(0.24f, 0.38f, 0.56f), C(0.95f, 0.95f, 1.00f), C(0.38f, 0.48f, 0.72f), C(0.70f, 1.00f, 0.90f)),
        PlanetTheme.Theatre => new(C(0.90f, 0.46f, 0.62f), C(0.99f, 0.70f, 0.74f), C(1.00f, 0.80f, 0.34f), C(1.00f, 0.91f, 0.86f),
            C(0.07f, 0.03f, 0.14f), C(0.32f, 0.12f, 0.36f), C(0.45f, 0.22f, 0.45f), C(1.00f, 0.86f, 0.80f), C(0.50f, 0.36f, 0.62f), C(1.00f, 0.75f, 0.45f)),
        PlanetTheme.Vault => new(C(0.44f, 0.52f, 0.82f), C(0.64f, 0.73f, 0.94f), C(0.98f, 0.78f, 0.36f), C(0.90f, 0.89f, 0.97f),
            C(0.04f, 0.04f, 0.14f), C(0.18f, 0.16f, 0.40f), C(0.28f, 0.28f, 0.52f), C(1.00f, 0.92f, 0.80f), C(0.40f, 0.40f, 0.66f), C(1.00f, 0.82f, 0.45f)),
        PlanetTheme.Crystal => new(C(0.46f, 0.86f, 0.94f), C(0.80f, 0.98f, 1.00f), C(0.96f, 0.50f, 0.88f), C(0.97f, 0.98f, 1.00f),
            C(0.04f, 0.04f, 0.18f), C(0.20f, 0.20f, 0.50f), C(0.32f, 0.36f, 0.62f), C(0.92f, 0.95f, 1.00f), C(0.45f, 0.48f, 0.78f), C(0.60f, 0.90f, 1.00f)),
        PlanetTheme.Wild => new(C(0.54f, 0.76f, 0.34f), C(0.88f, 0.88f, 0.46f), C(1.00f, 0.52f, 0.40f), C(0.96f, 0.88f, 0.68f),
            C(0.05f, 0.05f, 0.15f), C(0.24f, 0.18f, 0.38f), C(0.35f, 0.32f, 0.45f), C(1.00f, 0.90f, 0.72f), C(0.42f, 0.42f, 0.60f), C(1.00f, 0.80f, 0.45f)),
        PlanetTheme.Quiet => new(C(0.70f, 0.68f, 0.80f), C(0.87f, 0.85f, 0.93f), C(0.60f, 0.75f, 0.96f), C(0.96f, 0.95f, 0.99f),
            C(0.03f, 0.03f, 0.11f), C(0.14f, 0.13f, 0.30f), C(0.24f, 0.22f, 0.40f), C(0.90f, 0.90f, 1.00f), C(0.38f, 0.38f, 0.58f), C(0.85f, 0.90f, 1.00f)),
        _ /* Veiled */ => new(C(0.52f, 0.46f, 0.66f), C(0.69f, 0.63f, 0.81f), C(0.80f, 0.72f, 0.96f), C(0.81f, 0.77f, 0.91f),
            C(0.04f, 0.03f, 0.12f), C(0.20f, 0.15f, 0.34f), C(0.46f, 0.41f, 0.62f), C(0.82f, 0.80f, 0.96f), C(0.46f, 0.43f, 0.62f), C(0.80f, 0.72f, 1.00f)),
    };

    /// <summary>The fixed colour of a file category (the same on every planet, so a kind reads at a glance).</summary>
    public static Color CategoryColor(FileCategory c) => c switch
    {
        FileCategory.Image => C(0.98f, 0.52f, 0.58f),
        FileCategory.Document => C(0.36f, 0.56f, 0.90f),
        FileCategory.Code => C(0.90f, 0.68f, 0.30f),
        FileCategory.Audio => C(0.70f, 0.52f, 1.00f),
        FileCategory.Video => C(0.30f, 0.34f, 0.70f),
        FileCategory.Archive => C(0.74f, 0.44f, 0.26f),
        FileCategory.Executable => C(0.96f, 0.36f, 0.36f),
        FileCategory.Data => C(0.40f, 0.88f, 0.96f),
        _ => C(0.66f, 0.62f, 0.80f),
    };

    /// <summary>The lantern colour of a file category (warm for most; tuned so each kind's glow is distinct).</summary>
    public static Color CategoryGlow(FileCategory c) => c switch
    {
        FileCategory.Image => C(1.00f, 0.80f, 0.55f),
        FileCategory.Document => C(1.00f, 0.92f, 0.70f),
        FileCategory.Code => C(0.55f, 1.00f, 0.85f),
        FileCategory.Audio => C(1.00f, 0.90f, 0.45f),
        FileCategory.Video => C(1.00f, 0.82f, 0.40f),
        FileCategory.Archive => C(1.00f, 0.80f, 0.35f),
        FileCategory.Executable => C(0.50f, 0.90f, 1.00f),
        FileCategory.Data => C(0.55f, 0.95f, 1.00f),
        _ => C(0.50f, 1.00f, 0.90f),
    };

    // ------------------------------------------------------------------ materials

    private static ShaderMaterial? _surface, _prop;
    private static readonly Dictionary<Color, ShaderMaterial> Glows = new();
    private static readonly Dictionary<Color, ShaderMaterial> Halos = new();
    private static Shader Load(string name) => GD.Load<Shader>($"res://Shaders/{name}.gdshader");

    /// <summary>The toon material for planet ground (vertex colours, broader speckle). Shared.</summary>
    public static ShaderMaterial SurfaceMaterial()
    {
        if (_surface != null) return _surface;
        _surface = new ShaderMaterial { Shader = Load("planet_toon") };
        // Ground: a stronger, coarser speckle (≈ 1.4 m period) than props get, so wide ground never looks flat-filled.
        _surface.SetShaderParameter("speckle", 0.1f);
        _surface.SetShaderParameter("speckle_per_metre", 0.7f);
        _surface.SetShaderParameter("sky_rim", 0.1f);
        _surface.SetShaderParameter("sun_rim", 0.25f);
        _surface.SetShaderParameter("roughness", 0.95f);
        return _surface;
    }

    /// <summary>The toon material for props, file objects and the traveller (vertex colours, glowing parts). Shared.</summary>
    public static ShaderMaterial PropMaterial()
    {
        if (_prop != null) return _prop;
        _prop = new ShaderMaterial { Shader = Load("planet_toon") };
        // Props: a faint, fine speckle (≈ 0.33 m period) and a glossier finish than the ground.
        _prop.SetShaderParameter("speckle", 0.03f);
        _prop.SetShaderParameter("speckle_per_metre", 3f);
        _prop.SetShaderParameter("roughness", 0.6f);
        return _prop;
    }

    /// <summary>An unlit glowing material of <paramref name="color"/> (lantern cores, the launcher's heart). Cached per colour.</summary>
    public static ShaderMaterial GlowMaterial(Color color, float energy = 2.6f, float pulse = 0f)
    {
        // The cache key packs energy and pulse into one float (pulse in hundreds): distinct for the few (energy < 100,
        // pulse) pairs the app uses, not a general encoding.
        var key = new Color(color.R, color.G, color.B, energy + pulse * 100f);
        if (Glows.TryGetValue(key, out var m)) return m;
        m = new ShaderMaterial { Shader = Load("planet_glow") };
        m.SetShaderParameter("color", color);
        m.SetShaderParameter("energy", energy);
        m.SetShaderParameter("pulse", pulse);
        return Glows[key] = m;
    }

    /// <summary>An additive soft halo material (camera-facing). Cached per colour.</summary>
    public static ShaderMaterial HaloMaterial(Color color, float energy = 0.8f)
    {
        var key = new Color(color.R, color.G, color.B, energy);
        if (Halos.TryGetValue(key, out var m)) return m;
        m = new ShaderMaterial { Shader = Load("planet_halo") };
        m.SetShaderParameter("color", color);
        m.SetShaderParameter("energy", energy);
        return Halos[key] = m;
    }

    /// <summary>A camera-facing soft halo of <paramref name="radiusMetres"/> around a lantern (no shadow).</summary>
    public static MeshInstance3D Halo(Color color, float radiusMetres, float energy = 0.8f) => new()
    {
        Mesh = new QuadMesh { Size = new Vector2(radiusMetres * 2, radiusMetres * 2) },
        MaterialOverride = HaloMaterial(color, energy),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>The sky shader material for a palette (gradient, twinkling stars, nebulae, distant galaxies).</summary>
    public static ShaderMaterial SkyMaterial(PlanetPalette p)
    {
        var m = new ShaderMaterial { Shader = Load("planet_sky") };
        m.SetShaderParameter("top_color", p.SkyTop);
        m.SetShaderParameter("bottom_color", p.SkyTop.Darkened(0.4f));
        m.SetShaderParameter("nebula_a", p.Accent.Lerp(C(0.85f, 0.35f, 0.8f), 0.6f));
        m.SetShaderParameter("nebula_b", C(0.30f, 0.42f, 1.0f).Lerp(p.SkyHorizon, 0.3f));
        m.SetShaderParameter("nebula_strength", 0.7f);
        return m;
    }

    /// <summary>
    /// The environment for a palette: the sky as background, ambient light from the palette (not the sky, so a twinkling
    /// sky never re-lights the scene), soft filmic tone mapping, bloom for lanterns, and light aerial fog that leaves the
    /// sky untouched.
    /// </summary>
    public static Godot.Environment SkyEnvironment(PlanetPalette p) => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Sky,
        Sky = new Sky { SkyMaterial = SkyMaterial(p), RadianceSize = Sky.RadianceSizeEnum.Size32 },
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = p.Ambient,
        AmbientLightEnergy = 1.05f,
        ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
        TonemapExposure = 1.0f,
        GlowEnabled = true,
        GlowIntensity = 0.7f,
        GlowBloom = 0.04f,
        GlowHdrThreshold = 1.1f,
        GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
        // Fog off since DJ's playtest (e7c9777: "sky without the artificial horizon and fog"); the settings below take
        // effect only if it is turned back on.
        FogEnabled = false,
        FogLightColor = p.Fog,
        FogDensity = 0.0011f,
        FogSkyAffect = 0f,
        AdjustmentEnabled = true,
        AdjustmentSaturation = 1.12f,
        AdjustmentContrast = 1.04f,
    };

    /// <summary>The sun for a palette: warm, from high on one side, with soft shadows.</summary>
    public static DirectionalLight3D Sun(PlanetPalette p) => new()
    {
        Name = "Sun",
        LightColor = p.Sun,
        LightEnergy = 1.25f,
        RotationDegrees = new Vector3(-48, 35, 0),
        ShadowEnabled = true,
        ShadowBlur = 1.5f,
        DirectionalShadowMaxDistance = 90f, // metres (rationale not recorded; a short range keeps the shadow map's detail near the walker)
    };

    /// <summary>
    /// A soft atmosphere halo shell for a planet of <paramref name="planetRadiusMetres"/> (centred at the origin), its radius
    /// <paramref name="shellRatio"/> times the planet's: drawn only for worlds seen from outside (see <see cref="PlanetVisual"/>).
    /// </summary>
    public static MeshInstance3D Atmosphere(float planetRadiusMetres, PlanetPalette p, float shellRatio = 1.12f)
    {
        var m = new ShaderMaterial { Shader = Load("planet_atmosphere") };
        m.SetShaderParameter("color", p.Accent.Lerp(p.GroundHigh, 0.4f).Lerp(C(0.8f, 0.7f, 1f), 0.35f));
        m.SetShaderParameter("intensity", 0.45f);
        // Where a sightline grazes the planet it meets the shell at an angle whose sine is 1/shellRatio to the shell's
        // normal, so 1 − cos(view, normal) there is 1 − √(1 − 1/ratio²): the shader is brightest just outside that limb.
        m.SetShaderParameter("limb_f", 1f - Mathf.Sqrt(1f - 1f / (shellRatio * shellRatio)));
        var r = planetRadiusMetres * shellRatio;
        return new MeshInstance3D
        {
            Name = "Atmosphere",
            Mesh = new SphereMesh { Radius = r, Height = r * 2, RadialSegments = 48, Rings = 24 },
            MaterialOverride = m,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    // ------------------------------------------------------------------ planet

    /// <summary>
    /// Icosphere subdivisions that keep edges ≈ 0.6–1.2 m for a planet of <paramref name="radiusMetres"/>: an edge is about
    /// 0.066 × radius at 4 and 0.033 × radius at 5, so worlds up to 15 m use 4 (≤ ≈ 1 m) and larger ones 5.
    /// </summary>
    public static int SubdivisionsFor(float radiusMetres) => radiusMetres <= 15f ? 4 : 5;

    /// <summary>
    /// A planet's ground: a subdivided icosphere whose every vertex sits at <see cref="PlanetShape.GroundRadius"/> (so the
    /// drawn ground is the walked ground; between vertices the chord sags by a few millimetres at most), with normals from
    /// <see cref="PlanetShape.Normal"/>. Coloured by height, slope and soft noise patches of the accent, with lighter
    /// clearings and worn paths around the flat spots and between the first flat spot (the arrival) and the others.
    /// Subdivisions 5 = 20,480 triangles, 4 = 5,120.
    /// </summary>
    public static ArrayMesh PlanetMesh(PlanetShape shape, PlanetPalette palette, int subdivisions = 5) =>
        PlanetMesh(PlanetMeshArrays(shape, palette, subdivisions));

    /// <summary>
    /// The ground's vertex data, computed from plain maths only (no engine objects), so it can be made on a worker thread.
    /// A Hazard World's ground also carries the outline of its broken edges (<see cref="RimOutline"/>; null elsewhere).
    /// </summary>
    public sealed record GroundArrays(Vector3[] Vertices, Vector3[] Normals, Color[] Colors, int[] Indices, RimOutline? Rim = null);

    /// <summary>
    /// A Hazard World's ground is this finely divided (≈ 0.3 m edges at 18–25 m): its bridges are 1.8 m wide and their
    /// edges must read cleanly (AD-59). 81,920 triangles before the cut, of which about a third are kept.
    /// </summary>
    public const int HazardSubdivisions = 6;

    /// <summary>A Hazard World hanging in another's sky is drawn this finely (≈ 1.2 m edges): its islands and widest bridges read from afar.</summary>
    public const int HazardPreviewSubdivisions = 4;

    /// <summary>Uploads ground vertex data as a mesh (main thread).</summary>
    public static ArrayMesh PlanetMesh(GroundArrays g)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = g.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = g.Normals;
        arrays[(int)Mesh.ArrayType.Color] = g.Colors;
        arrays[(int)Mesh.ArrayType.Index] = g.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, SurfaceMaterial());
        return mesh;
    }

    /// <summary>
    /// The ground's vertices (each at <see cref="PlanetShape.GroundRadius"/>, so it matches walking exactly), normals and
    /// colours. Thread-safe: plain maths on value types (2026-11-11: moved off the main thread, where it cost 40–140 ms).
    /// </summary>
    /// <param name="shell">A Hazard World's broken shell (PLANET-34, PLANET-46): only its islands and bridges are kept, and the crust added (<see cref="BreakShell"/>).</param>
    public static GroundArrays PlanetMeshArrays(PlanetShape shape, PlanetPalette palette, int subdivisions = 5, BrokenShell? shell = null)
    {
        var (dirs, tris) = Icosphere(Math.Clamp(subdivisions, 0, 6));
        var verts = new Vector3[dirs.Count];
        var normals = new Vector3[dirs.Count];
        var colors = new Color[dirs.Count];
        var amp = MathF.Max(shape.HillAmplitude, 0.001f);
        var spots = shape.FlatSpots;
        for (var i = 0; i < dirs.Count; i++)
        {
            var d = dirs[i];
            var nd = new NVector3(d.X, d.Y, d.Z);
            var h = shape.HeightAt(nd);
            verts[i] = d * (shape.Radius + h);
            var n = shape.Normal(nd);
            normals[i] = new Vector3(n.X, n.Y, n.Z);
            // Low to high ground: half by height (−amplitude … +amplitude → 0 … 1), half by broad noise (features ≈ 9 m),
            // so colour bands do not simply follow contour lines. The noise offsets only decorrelate the layers and seeds.
            var t = Mathf.Clamp(h / amp * 0.5f + 0.5f, 0, 1);
            var broad = Noise(verts[i] * (1f / 9f) + new Vector3(4.2f, shape.Seed * 0.13f, 1.7f));
            var col = palette.GroundLow.Lerp(palette.GroundHigh, Smooth(0.2f, 0.8f, 0.5f * t + 0.5f * broad));
            // Slopes darken: 1 − cos(tilt) from 0.01 (≈ 8°) to 0.07 (≈ 21°).
            var slope = 1f - NVector3.Dot(n, nd);
            col = col.Lerp(palette.GroundLow.Darkened(0.22f), Smooth(0.01f, 0.07f, slope));
            var p = verts[i];

            // Clearings and paths: lighter around flat spots and along great-circle paths from the arrival spot. Per spot of
            // radius sr (metres along the surface): a faint worn disc (0.35 of the path colour) over it, a brighter ring
            // path (0.6) whose line runs 0.8 m outside its edge (full within ±0.35 m, gone by ±1 m), accent patches damped
            // (to 30 %) inside it and for 3 m beyond, and from the arrival (spot 0) to every other spot a path (0.65; full
            // within ±0.45 m, gone by ±1.2 m).
            var path = 0f;
            var clear = 1f;
            for (var s = 0; s < spots.Count; s++)
            {
                var (sd, sr) = spots[s];
                var dist = PlanetShape.Angle(nd, sd) * shape.Radius;
                path = MathF.Max(path, 0.35f * (1 - Smooth(sr * 0.6f, sr + 0.4f, dist)));
                path = MathF.Max(path, 0.6f * (1 - Smooth(0.35f, 1.0f, MathF.Abs(dist - (sr + 0.8f)))));
                clear = MathF.Min(clear, Smooth(sr, sr + 3f, dist));
                // (Not on a Hazard World: its straight ways would cross open sky; its route is lit along the bridges instead.)
                if (s > 0 && shell == null) path = MathF.Max(path, 0.65f * (1 - Smooth(0.45f, 1.2f, ArcDistance(nd, spots[0].Direction, sd, shape.Radius))));
            }

            // Patches of the accent (flower meadows: features ≈ 6.5 m, where the noise passes 0.6–0.75) away from
            // clearings, and a fine brightness wobble (features ≈ 1.8 m, at most 7 % towards white or black).
            var patch = Noise(p * (1f / 6.5f) + new Vector3(shape.Seed * 0.37f, 1.3f, 2.9f));
            col = col.Lerp(palette.Accent.Lerp(palette.GroundHigh, 0.35f), 0.5f * Smooth(0.6f, 0.75f, patch) * MathF.Max(clear, 0.3f));
            var fine = Noise(p * 0.55f + new Vector3(7.1f, 3.3f, shape.Seed * 0.11f));
            col = col.Lerp(fine > 0.5f ? Colors.White : Colors.Black, MathF.Abs(fine - 0.5f) * 0.14f);
            col = col.Lerp(palette.Path, path);
            colors[i] = Linear(col);
        }

        var ground = new GroundArrays(verts, normals, colors, tris.ToArray());
        return shell != null ? BreakShell(ground, shape, shell, palette) : ground;
    }

    /// <summary>Distance along the surface (metres) from <paramref name="p"/> to the great-circle arc a→b (beyond the ends: to the nearer end).</summary>
    private static float ArcDistance(NVector3 p, NVector3 a, NVector3 b, float radius)
    {
        var n = NVector3.Cross(a, b);
        if (n.LengthSquared() < 1e-8f) return PlanetShape.Angle(p, a) * radius;
        n = NVector3.Normalize(n);
        var proj = p - n * NVector3.Dot(p, n);
        if (proj.LengthSquared() < 1e-8f) return MathF.PI * radius;
        proj = NVector3.Normalize(proj);
        var ab = PlanetShape.Angle(a, b);
        if (PlanetShape.Angle(a, proj) <= ab && PlanetShape.Angle(b, proj) <= ab)
        {
            return MathF.Asin(Math.Clamp(MathF.Abs(NVector3.Dot(p, n)), 0, 1)) * radius;
        }

        return MathF.Min(PlanetShape.Angle(p, a), PlanetShape.Angle(p, b)) * radius;
    }

    /// <summary>
    /// A unit icosphere: the icosahedron's 12 vertices (from the golden ratio), each subdivision splitting every triangle
    /// into four with the edge midpoints pushed out onto the sphere (shared midpoints cached per edge); 20 × 4ⁿ triangles.
    /// </summary>
    private static (List<Vector3> Dirs, List<int> Tris) Icosphere(int subdivisions)
    {
        var t = (1f + MathF.Sqrt(5f)) / 2f;
        var v = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
            new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (var i = 0; i < v.Count; i++) v[i] = v[i].Normalized();
        var f = new List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        for (var s = 0; s < subdivisions; s++)
        {
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out var m)) return m;
                v.Add((v[a] + v[b]).Normalized());
                return cache[key] = v.Count - 1;
            }

            var next = new List<int>(f.Count * 4);
            for (var i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange([a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca]);
            }

            f = next;
        }

        // Godot's front faces wind clockwise seen from outside; make every triangle face outwards.
        for (var i = 0; i < f.Count; i += 3)
        {
            var g = (v[f[i + 1]] - v[f[i]]).Cross(v[f[i + 2]] - v[f[i]]);
            if (g.Dot(v[f[i]] + v[f[i + 1]] + v[f[i + 2]]) > 0) (f[i + 1], f[i + 2]) = (f[i + 2], f[i + 1]);
        }

        return (v, f);
    }

    // ------------------------------------------------------------------ shared bits

    // The materials of the toy world, shared by every builder so props on all planets belong together (sRGB).
    private static readonly Color Stone = C(0.76f, 0.70f, 0.88f);
    private static readonly Color StoneDark = C(0.56f, 0.50f, 0.74f);
    private static readonly Color Wood = C(0.66f, 0.42f, 0.30f);
    private static readonly Color WoodLight = C(0.84f, 0.60f, 0.40f);
    private static readonly Color Brass = C(0.94f, 0.72f, 0.34f);
    private static readonly Color Cream = C(1.00f, 0.95f, 0.84f);
    private static readonly Color Ink = C(0.16f, 0.14f, 0.26f);
    private static readonly Color Leaf = C(0.40f, 0.74f, 0.42f);

    /// <summary>A colour marked as glowing: <paramref name="amount"/> 0..1 of emission (stored in vertex alpha as 1 − amount).</summary>
    internal static Color Glowing(Color c, float amount = 1f) => new(c.R, c.G, c.B, 1f - Mathf.Clamp(amount, 0, 1));

    /// <summary>sRGB to linear for a vertex colour (the shaders work in linear light), keeping alpha (the glow mark) as it is.</summary>
    internal static Color Linear(Color c)
    {
        var l = c.SrgbToLinear();
        return new Color(l.R, l.G, l.B, c.A);
    }

    /// <summary>Smoothstep: 0 below <paramref name="e0"/>, 1 above <paramref name="e1"/>, an S-curve between.</summary>
    private static float Smooth(float e0, float e1, float x)
    {
        var t = Mathf.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// A repeatable pseudo-random value in [0, 1] for an integer lattice point (the corners of <see cref="Noise"/>). Large
    /// odd multipliers (two are xxHash32's primes) scatter neighbouring points, the xor-shifts mix the high bits down, and
    /// the low 24 bits become the float (exact: a float's mantissa holds 24 bits). Wrapping arithmetic is intended.
    /// </summary>
    private static float Hash(int x, int y, int z)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xffffff) / (float)0xffffff;
        }
    }

    /// <summary>Smooth 3D value noise in [0, 1]: <see cref="Hash"/> at the 8 surrounding lattice corners (1 unit apart), blended trilinearly with a smoothstep fade.</summary>
    internal static float Noise(Vector3 p)
    {
        int x = Mathf.FloorToInt(p.X), y = Mathf.FloorToInt(p.Y), z = Mathf.FloorToInt(p.Z);
        float fx = p.X - x, fy = p.Y - y, fz = p.Z - z;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        fz = fz * fz * (3 - 2 * fz);
        float L(float a, float b, float t) => a + (b - a) * t;
        return L(L(L(Hash(x, y, z), Hash(x + 1, y, z), fx), L(Hash(x, y + 1, z), Hash(x + 1, y + 1, z), fx), fy),
            L(L(Hash(x, y, z + 1), Hash(x + 1, y, z + 1), fx), L(Hash(x, y + 1, z + 1), Hash(x + 1, y + 1, z + 1), fx), fy), fz);
    }

    /// <summary>A transform: translate, then Euler rotation in degrees (Godot's YXZ order), then scale.</summary>
    internal static Transform3D T(Vector3 position, Vector3 eulerDegrees = default, float scale = 1f) =>
        new(Basis.FromEuler(eulerDegrees * (Mathf.Pi / 180f)).Scaled(new Vector3(scale, scale, scale)), position);

    /// <summary>As <see cref="T(Vector3, Vector3, float)"/> with a per-axis scale (applied before the rotation).</summary>
    internal static Transform3D T(Vector3 position, Vector3 eulerDegrees, Vector3 scale) =>
        new(Basis.FromEuler(eulerDegrees * (Mathf.Pi / 180f)) * Basis.FromScale(scale), position);

    /// <summary>The round stone plinth a file object or pile stands on (base at y = 0, a slight bulge, top at <paramref name="height"/>).</summary>
    private static void Plinth(MeshBuilder b, float radius = 0.44f, float height = 0.1f)
    {
        b.Lathe([new(0, 0), new(radius, 0), new(radius, 0), new(radius * 1.02f, height * 0.6f), new(radius * 0.94f, height), new(radius * 0.94f, height), new(0, height)],
            (_, i) => i < 3 ? StoneDark : Stone, 20);
    }

    /// <summary>Adds the builder's triangles as a new surface of <paramref name="mesh"/> (a new mesh when null) with the prop toon material unless another is given.</summary>
    private static ArrayMesh Commit(MeshBuilder b, ArrayMesh? mesh = null, Material? material = null)
    {
        mesh ??= new ArrayMesh();
        b.Commit(mesh, material ?? PropMaterial());
        return mesh;
    }

    // ------------------------------------------------------------------ file objects

    /// <summary>
    /// A file object for <paramref name="category"/>: one distinct, approachable silhouette per kind, ≈ 1 m tall at scale
    /// 1, base at y = 0 on a little round plinth (radius 0.44 m, top 0.1 m), local +Y up, front −Z, with a glowing lantern
    /// part. Image (an easel) and Video (a cinema lantern) leave a face quad with UVs as surface 1 for a real thumbnail.
    /// </summary>
    public static FileArt FileObject(FileCategory category)
    {
        var b = new MeshBuilder();
        var face = new MeshBuilder();
        Plinth(b);
        var (lantern, size) = BuildFile(b, face, category, Transform3D.Identity);
        var mesh = Commit(b);
        var faceIndex = -1;
        if (face.Triangles > 0)
        {
            faceIndex = mesh.GetSurfaceCount();
            Commit(face, mesh, FacePlaceholder());
        }

        return new FileArt(mesh, faceIndex, size, lantern, category == FileCategory.Executable ? 1.1f : 1.0f); // the rocket stands taller
    }

    /// <summary>
    /// A pile of files of one kind (a heap or stack version of <see cref="FileObject"/>), ≈ 1.5–1.8 m tall on a wider
    /// plinth (radius 0.95 m). For Image and Video the front item keeps a face quad (surface 1).
    /// </summary>
    public static FileArt Pile(FileCategory category)
    {
        var b = new MeshBuilder();
        var face = new MeshBuilder();
        Plinth(b, 0.95f, 0.14f);
        var lantern = Vector3.Zero;
        var size = Vector2.Zero;
        switch (category)
        {
            case FileCategory.Document:
                // A stack of books beside a lectern.
                (lantern, size) = BuildFile(b, face, category, T(new Vector3(-0.3f, 0.04f, -0.15f), new Vector3(0, 12, 0), 1.1f));
                Books(b, T(new Vector3(0.42f, 0.14f, 0.2f), new Vector3(0, -15, 0)), 6, 0.62f, 1);
                break;
            case FileCategory.Archive:
                BuildFile(b, null, category, T(new Vector3(-0.38f, 0.04f, 0.1f), new Vector3(0, 10, 0), 1.0f));
                BuildFile(b, null, category, T(new Vector3(0.4f, 0.04f, 0.15f), new Vector3(0, -14, 0), 0.95f));
                (lantern, size) = BuildFile(b, face, category, T(new Vector3(0.02f, 0.62f, 0.12f), new Vector3(0, 4, 0), 0.9f));
                break;
            case FileCategory.Data:
                BuildFile(b, null, category, T(new Vector3(0.3f, 0.04f, 0.25f), new Vector3(0, 40, 0), 1.25f));
                BuildFile(b, null, category, T(new Vector3(-0.42f, 0.04f, 0.1f), new Vector3(0, -30, 0), 0.9f));
                (lantern, size) = BuildFile(b, face, category, T(new Vector3(0, 0.04f, -0.3f), new Vector3(0, 10, 0), 1.45f));
                break;
            case FileCategory.Other:
                // A cairn: stones balanced on stones.
                BuildFile(b, null, category, T(new Vector3(-0.32f, 0.04f, 0.2f), new Vector3(0, 30, 0), 1.0f));
                BuildFile(b, null, category, T(new Vector3(0.36f, 0.04f, 0.25f), new Vector3(0, -25, 0), 0.9f));
                (lantern, size) = BuildFile(b, face, category, T(new Vector3(0, 0.62f, 0.1f), new Vector3(0, 0, 0), 0.85f));
                break;
            default:
                // The front item at full size, two smaller ones behind it.
                BuildFile(b, null, category, T(new Vector3(-0.45f, 0.04f, 0.32f), new Vector3(0, 22, 0), 0.85f));
                BuildFile(b, null, category, T(new Vector3(0.45f, 0.04f, 0.36f), new Vector3(0, -18, 0), 0.95f));
                (lantern, size) = BuildFile(b, face, category, T(new Vector3(0, 0.04f, -0.25f), Vector3.Zero, 1.25f));
                break;
        }

        var mesh = Commit(b);
        var faceIndex = -1;
        if (face.Triangles > 0)
        {
            faceIndex = mesh.GetSurfaceCount();
            Commit(face, mesh, FacePlaceholder());
        }

        return new FileArt(mesh, faceIndex, size, lantern, 1.6f);
    }

    private static StandardMaterial3D? _facePlaceholder;

    /// <summary>The face quad's default material (a soft cream card) until the app shows a thumbnail on it.</summary>
    public static StandardMaterial3D FacePlaceholder() => _facePlaceholder ??= new StandardMaterial3D
    {
        AlbedoColor = C(1.0f, 0.95f, 0.86f),
        EmissionEnabled = true,
        Emission = C(1.0f, 0.9f, 0.75f),
        EmissionEnergyMultiplier = 0.25f,
        Roughness = 0.9f,
    };

    /// <summary>Builds a file object (without its plinth) under <paramref name="t"/>; returns the lantern position and face size.</summary>
    private static (Vector3 Lantern, Vector2 FaceSize) BuildFile(MeshBuilder b, MeshBuilder? face, FileCategory category, Transform3D t)
    {
        b.Push(t);
        face?.Push(t);
        var colour = CategoryColor(category);
        var glow = CategoryGlow(category);
        Vector3 lantern;
        var faceSize = Vector2.Zero;
        switch (category)
        {
            case FileCategory.Image:
            {
                // An easel holding a big square canvas (the face), a little lamp clipped on top.
                b.Cylinder(new Vector3(-0.27f, 0.08f, -0.12f), new Vector3(-0.15f, 1.0f, 0.0f), 0.035f, 0.028f, Wood, 6);
                b.Cylinder(new Vector3(0.27f, 0.08f, -0.12f), new Vector3(0.15f, 1.0f, 0.0f), 0.035f, 0.028f, Wood, 6);
                b.Cylinder(new Vector3(0, 0.08f, 0.32f), new Vector3(0, 0.92f, 0.04f), 0.032f, 0.026f, Wood, 6);
                b.RoundBox(new Vector3(0, 0.29f, -0.1f), new Vector3(0.66f, 0.05f, 0.1f), 0.4f, WoodLight);
                b.Push(T(new Vector3(0, 0.63f, -0.08f), new Vector3(-11, 0, 0)));
                const float s = 0.62f, bar = 0.075f;
                b.RoundBox(new Vector3(0, s / 2 + bar / 2 - 0.01f, 0), new Vector3(s + bar * 2, bar, 0.07f), 0.45f, colour);
                b.RoundBox(new Vector3(0, -s / 2 - bar / 2 + 0.01f, 0), new Vector3(s + bar * 2, bar, 0.07f), 0.45f, colour);
                b.RoundBox(new Vector3(-s / 2 - bar / 2 + 0.01f, 0, 0), new Vector3(bar, s, 0.07f), 0.45f, colour);
                b.RoundBox(new Vector3(s / 2 + bar / 2 - 0.01f, 0, 0), new Vector3(bar, s, 0.07f), 0.45f, colour);
                b.Box(new Vector3(0, 0, 0.02f), new Vector3(s, s, 0.03f), Cream.Darkened(0.1f));
                // Lamp on a little arm above the frame.
                b.Cylinder(new Vector3(0, s / 2 + bar, 0), new Vector3(0, s / 2 + bar + 0.12f, -0.06f), 0.015f, 0.015f, Ink, 5);
                b.Ellipsoid(new Vector3(0, s / 2 + bar + 0.14f, -0.08f), new Vector3(0.11f, 0.05f, 0.06f), Brass, 10, 6);
                b.Ellipsoid(new Vector3(0, s / 2 + bar + 0.105f, -0.08f), new Vector3(0.07f, 0.03f, 0.04f), Glowing(glow), 8, 5);
                if (face != null)
                {
                    face.Push(T(new Vector3(0, 0.63f, -0.08f), new Vector3(-11, 0, 0)));
                    face.Quad(new Vector3(0, 0, -0.012f), new Vector3(-s / 2, 0, 0), new Vector3(0, s / 2, 0), Colors.White);
                    face.Pop();
                }

                b.Pop();
                lantern = new Vector3(0, 0.63f + s / 2 + 0.17f, -0.12f);
                faceSize = new Vector2(s, s);
                break;
            }

            case FileCategory.Document:
            {
                // A lectern with an open book whose pages glow softly.
                b.Lathe([new(0, 0.08f), new(0.2f, 0.08f), new(0.2f, 0.08f), new(0.16f, 0.15f), new(0.06f, 0.18f)], (_, _) => Wood, 12);
                b.Cylinder(new Vector3(0, 0.15f, 0), new Vector3(0, 0.74f, 0.04f), 0.06f, 0.05f, Wood, 8);
                b.Push(T(new Vector3(0, 0.8f, 0.02f), new Vector3(-28, 0, 0)));
                b.RoundBox(Vector3.Zero, new Vector3(0.64f, 0.05f, 0.44f), 0.35f, WoodLight);
                b.RoundBox(new Vector3(0, -0.02f, -0.24f), new Vector3(0.66f, 0.05f, 0.05f), 0.5f, Wood);
                // Book: cover, two page blocks in a gentle V, a ribbon.
                b.RoundBox(new Vector3(0, 0.04f, 0), new Vector3(0.62f, 0.03f, 0.42f), 0.3f, colour);
                b.Push(T(new Vector3(-0.15f, 0.07f, 0), new Vector3(0, 0, -7)));
                b.RoundBox(Vector3.Zero, new Vector3(0.29f, 0.045f, 0.38f), 0.3f, Glowing(Cream, 0.35f));
                b.Pop();
                b.Push(T(new Vector3(0.15f, 0.07f, 0), new Vector3(0, 0, 7)));
                b.RoundBox(Vector3.Zero, new Vector3(0.29f, 0.045f, 0.38f), 0.3f, Glowing(Cream, 0.35f));
                b.Pop();
                for (var k = 0; k < 3; k++)
                {
                    b.Box(new Vector3(-0.15f, 0.098f + 0.004f * k, 0.1f - 0.08f * k), new Vector3(0.18f, 0.006f, 0.012f), Ink.Lightened(0.35f));
                    b.Box(new Vector3(0.15f, 0.098f + 0.004f * k, 0.1f - 0.08f * k), new Vector3(0.18f, 0.006f, 0.012f), Ink.Lightened(0.35f));
                }

                b.Box(new Vector3(0.02f, 0.03f, -0.28f), new Vector3(0.04f, 0.01f, 0.18f), C(0.95f, 0.35f, 0.4f));
                b.Pop();
                // A candle lamp on the lectern's corner.
                b.Cylinder(new Vector3(0.3f, 0.86f, 0.12f), new Vector3(0.3f, 0.98f, 0.12f), 0.035f, 0.035f, Cream, 8);
                b.Ellipsoid(new Vector3(0.3f, 1.02f, 0.12f), new Vector3(0.035f, 0.06f, 0.035f), Glowing(glow), 8, 6);
                lantern = new Vector3(0.3f, 1.02f, 0.12f);
                break;
            }

            case FileCategory.Code:
            {
                // A small brass machine with a big gear on its face, a glass lamp dome and a little chimney.
                foreach (var x in new[] { -0.22f, 0.22f })
                foreach (var z in new[] { -0.14f, 0.14f })
                    b.Ellipsoid(new Vector3(x, 0.13f, z), new Vector3(0.06f, 0.05f, 0.06f), Ink.Lightened(0.2f), 8, 5);
                b.RoundBox(new Vector3(0, 0.42f, 0), new Vector3(0.62f, 0.52f, 0.44f), 0.32f, colour);
                b.RoundBox(new Vector3(0, 0.7f, 0), new Vector3(0.66f, 0.06f, 0.48f), 0.4f, colour.Darkened(0.2f));
                b.Push(T(new Vector3(0.04f, 0.42f, -0.24f)));
                b.Gear(0.25f, 0.19f, 0.06f, 10, Brass.Darkened(0.12f), C(0.70f, 0.45f, 0.22f));
                b.Cylinder(new Vector3(0, 0, 0.0f), new Vector3(0, 0, -0.06f), 0.07f, 0.07f, Glowing(glow, 0.8f), 10);
                b.Pop();
                for (var k = 0; k < 3; k++)
                    b.Ellipsoid(new Vector3(-0.24f, 0.56f - 0.1f * k, -0.225f), new Vector3(0.025f, 0.025f, 0.015f), Ink, 6, 4);
                b.Cylinder(new Vector3(0.18f, 0.72f, 0.1f), new Vector3(0.18f, 0.98f, 0.1f), 0.055f, 0.05f, C(0.70f, 0.45f, 0.22f), 8);
                b.Torus(T(new Vector3(0.18f, 0.98f, 0.1f)), 0.055f, 0.02f, Brass, 10, 5);
                b.Lathe([new(0.13f, 0), new(0.13f, 0), new(0.12f, 0.08f), new(0.07f, 0.16f), new(0, 0.18f)], (_, i) => Glowing(glow, 0.85f), 12, origin: new Vector3(-0.14f, 0.72f, -0.02f));
                b.Torus(T(new Vector3(-0.14f, 0.73f, -0.02f)), 0.13f, 0.025f, Brass, 14, 5);
                lantern = new Vector3(-0.14f, 0.8f, -0.02f);
                break;
            }

            case FileCategory.Audio:
            {
                // A bell flower: a curved stem, two leaves, a big bell hanging forward with a glowing heart.
                b.Tube([new(0, 0.08f, 0.1f), new(0.02f, 0.4f, 0.12f), new(0, 0.8f, 0.08f), new(0, 1.0f, -0.05f), new(0, 0.98f, -0.18f)], s => 0.045f - 0.015f * s, _ => Leaf.Darkened(0.1f), 7);
                b.Push(T(new Vector3(-0.14f, 0.3f, 0.1f), new Vector3(0, 0, 50)));
                b.Ellipsoid(new Vector3(0, 0.12f, 0), new Vector3(0.07f, 0.16f, 0.03f), Leaf, 10, 6);
                b.Pop();
                b.Push(T(new Vector3(0.14f, 0.42f, 0.12f), new Vector3(0, 0, -45)));
                b.Ellipsoid(new Vector3(0, 0.12f, 0), new Vector3(0.07f, 0.16f, 0.03f), Leaf, 10, 6);
                b.Pop();
                b.Push(T(new Vector3(0, 0.98f, -0.2f), new Vector3(18, 0, 0)));
                Vector2[] bell = [new(0.0f, 0.02f), new(0.08f, 0.0f), new(0.14f, -0.08f), new(0.17f, -0.2f), new(0.2f, -0.3f), new(0.28f, -0.38f), new(0.3f, -0.4f)];
                b.Lathe(bell, (_, i) => colour.Lightened(0.08f * i / 6f), 16);
                b.Lathe(bell.Select(p => new Vector2(p.X * 0.9f, p.Y - 0.012f)).ToArray(), (_, i) => colour.Lightened(0.35f), 16, inward: true);
                b.Cylinder(new Vector3(0, -0.05f, 0), new Vector3(0, -0.3f, 0), 0.012f, 0.012f, Cream, 4);
                b.Ellipsoid(new Vector3(0, -0.33f, 0), new Vector3(0.07f, 0.07f, 0.07f), Glowing(glow), 10, 7);
                b.Pop();
                lantern = new Vector3(0, 0.67f, -0.3f);
                break;
            }

            case FileCategory.Video:
            {
                // A little cinema lantern: a rounded housing with a screen (the face), two film reels on top, footlights.
                b.Cylinder(new Vector3(0, 0.08f, 0), new Vector3(0, 0.3f, 0), 0.08f, 0.07f, Ink.Lightened(0.2f), 8);
                b.RoundBox(new Vector3(0, 0.56f, 0), new Vector3(0.74f, 0.5f, 0.36f), 0.3f, colour);
                b.RoundBox(new Vector3(0, 0.56f, -0.17f), new Vector3(0.66f, 0.40f, 0.04f), 0.25f, Glowing(glow, 0.9f));
                b.RoundBox(new Vector3(0, 0.56f, -0.18f), new Vector3(0.60f, 0.345f, 0.04f), 0.2f, Ink);
                foreach (var (x, y, r) in new[] { (-0.17f, 0.97f, 0.16f), (0.18f, 0.95f, 0.14f) })
                {
                    b.Cylinder(new Vector3(x - 0.035f, y, 0.02f), new Vector3(x + 0.035f, y, 0.02f), r, r, C(0.82f, 0.80f, 0.92f), 16);
                    for (var k = 0; k < 3; k++)
                    {
                        var a = k * Mathf.Tau / 3 + 0.4f;
                        var o = new Vector3(0, Mathf.Sin(a), Mathf.Cos(a)) * r * 0.5f;
                        b.Cylinder(new Vector3(x - 0.04f, y, 0.02f) + o, new Vector3(x + 0.04f, y, 0.02f) + o, r * 0.22f, r * 0.22f, Ink.Lightened(0.15f), 8);
                    }

                    b.Cylinder(new Vector3(x, y - r * 0.8f, 0.02f), new Vector3(x, 0.8f, 0.02f), 0.02f, 0.02f, Ink, 4);
                }

                for (var k = 0; k < 4; k++) b.Ellipsoid(new Vector3(-0.24f + 0.16f * k, 0.3f, -0.17f), new Vector3(0.035f, 0.035f, 0.035f), Glowing(glow), 8, 5);
                if (face != null) face.Quad(new Vector3(0, 0.56f, -0.205f), new Vector3(-0.28f, 0, 0), new Vector3(0, 0.1575f, 0), Colors.White);
                lantern = new Vector3(0, 0.3f, -0.2f);
                faceSize = new Vector2(0.56f, 0.315f);
                break;
            }

            case FileCategory.Archive:
            {
                // A treasure chest: wooden body and barrel lid, gold bands, a glowing lock and seam.
                b.RoundBox(new Vector3(0, 0.33f, 0), new Vector3(0.74f, 0.44f, 0.5f), 0.22f, colour);
                b.Cylinder(new Vector3(-0.36f, 0.54f, 0), new Vector3(0.36f, 0.54f, 0), 0.25f, 0.25f, colour.Lightened(0.1f), 16);
                foreach (var x in new[] { -0.24f, 0.24f })
                {
                    b.RoundBox(new Vector3(x, 0.33f, 0), new Vector3(0.07f, 0.45f, 0.52f), 0.3f, Brass);
                    b.Cylinder(new Vector3(x - 0.035f, 0.54f, 0), new Vector3(x + 0.035f, 0.54f, 0), 0.262f, 0.262f, Brass, 16);
                }

                b.Box(new Vector3(0, 0.545f, -0.251f), new Vector3(0.68f, 0.022f, 0.01f), Glowing(glow));
                b.RoundBox(new Vector3(0, 0.5f, -0.26f), new Vector3(0.14f, 0.16f, 0.05f), 0.4f, Glowing(Brass, 0.45f));
                b.Ellipsoid(new Vector3(0, 0.5f, -0.29f), new Vector3(0.02f, 0.035f, 0.01f), Ink, 6, 4);
                lantern = new Vector3(0, 0.52f, -0.3f);
                break;
            }

            case FileCategory.Executable:
            {
                // A small rocket on a launch stand: white body, red nose and fins, a glowing porthole and pilot flame.
                for (var k = 0; k < 3; k++)
                {
                    var a = k * Mathf.Tau / 3 + Mathf.Pi / 6;
                    var foot = new Vector3(Mathf.Sin(a) * 0.32f, 0.08f, Mathf.Cos(a) * 0.32f);
                    b.Cylinder(foot, new Vector3(Mathf.Sin(a) * 0.16f, 0.36f, Mathf.Cos(a) * 0.16f), 0.025f, 0.02f, Ink.Lightened(0.25f), 5);
                }

                b.Torus(T(new Vector3(0, 0.34f, 0)), 0.17f, 0.025f, Ink.Lightened(0.3f), 16, 5);
                var white = C(0.97f, 0.96f, 1.0f);
                Vector2[] body = [new(0.0f, 0.22f), new(0.09f, 0.22f), new(0.13f, 0.3f), new(0.17f, 0.42f), new(0.18f, 0.6f), new(0.16f, 0.76f), new(0.16f, 0.76f), new(0.11f, 0.9f), new(0.05f, 1.0f), new(0, 1.04f)];
                b.Lathe(body, (_, i) => i >= 6 ? colour : (i == 4 ? colour.Lightened(0.2f) : white), 16);
                for (var k = 0; k < 3; k++)
                {
                    var yaw = k * 120f + 180f;
                    b.Push(T(Vector3.Zero, new Vector3(0, yaw, 0)));
                    b.Extrude([new(0.14f, 0.26f), new(0.3f, 0.2f), new(0.3f, 0.3f), new(0.16f, 0.5f)], 0.03f, colour, Vector3.Zero, axisX: true);
                    b.Pop();
                }

                b.Cylinder(new Vector3(0, 0.64f, -0.16f), new Vector3(0, 0.64f, -0.185f), 0.06f, 0.06f, Glowing(glow), 12);
                b.Torus(T(new Vector3(0, 0.64f, -0.17f), new Vector3(90, 0, 0)), 0.065f, 0.018f, Brass, 14, 5);
                b.Lathe([new(0.0f, 0.22f), new(0.07f, 0.2f), new(0.0f, 0.06f)], (_, _) => Glowing(C(1f, 0.6f, 0.3f)), 10);
                lantern = new Vector3(0, 0.64f, -0.2f);
                break;
            }

            case FileCategory.Data:
            {
                // A crystal cluster on a small rock, partly glowing.
                b.Ellipsoid(new Vector3(0, 0.1f, 0), new Vector3(0.3f, 0.1f, 0.26f), StoneDark, 12, 6);
                var shards = new (Vector3 At, Vector3 Rot, float H, float R)[]
                {
                    (new(0, 0.1f, 0), new(0, 15, 0), 0.68f, 0.12f), (new(0.14f, 0.12f, 0.04f), new(0, 0, -28), 0.44f, 0.09f),
                    (new(-0.15f, 0.12f, 0.02f), new(5, 0, 30), 0.5f, 0.09f), (new(0.04f, 0.12f, -0.14f), new(-30, 0, -8), 0.36f, 0.075f),
                    (new(-0.05f, 0.12f, 0.15f), new(32, 0, 10), 0.4f, 0.08f),
                };
                var i = 0;
                foreach (var (at, rot, h, r) in shards)
                {
                    b.Push(T(at, rot));
                    var c = i++ % 2 == 0 ? colour : colour.Lerp(C(0.8f, 0.7f, 1f), 0.5f);
                    b.Prism(6, r, h, r * 1.6f, (_, k) => Glowing(c.Lightened(0.06f * k), 0.1f + 0.07f * k));
                    b.Pop();
                }

                lantern = new Vector3(0, 0.6f, 0);
                break;
            }

            default:
            {
                // A round stone with a glowing rune on its face.
                b.Ellipsoid(new Vector3(0, 0.5f, 0), new Vector3(0.4f, 0.42f, 0.33f), colour, 18, 12);
                b.Ellipsoid(new Vector3(0.12f, 0.88f, 0.06f), new Vector3(0.12f, 0.06f, 0.1f), Leaf.Lightened(0.1f), 8, 5);
                var g = Glowing(glow);
                void Stroke(Vector2 a, Vector2 c)
                {
                    var mid = (a + c) / 2;
                    var dy = mid.Y - 0.5f;
                    var z = -0.33f * Mathf.Sqrt(Mathf.Max(0.05f, 1 - mid.X * mid.X / 0.16f - dy * dy / 0.176f)) - 0.004f;
                    var len = (c - a).Length();
                    var ang = Mathf.Atan2(c.Y - a.Y, c.X - a.X);
                    b.Push(new Transform3D(new Basis(Vector3.Back, ang), new Vector3(mid.X, mid.Y, z)));
                    b.RoundBox(Vector3.Zero, new Vector3(len + 0.04f, 0.04f, 0.03f), 0.5f, g);
                    b.Pop();
                }

                Stroke(new Vector2(0, 0.33f), new Vector2(0, 0.72f));
                Stroke(new Vector2(0, 0.6f), new Vector2(-0.12f, 0.7f));
                Stroke(new Vector2(0, 0.6f), new Vector2(0.12f, 0.7f));
                Stroke(new Vector2(0, 0.44f), new Vector2(-0.1f, 0.36f));
                Stroke(new Vector2(0, 0.44f), new Vector2(0.1f, 0.36f));
                lantern = new Vector3(0, 0.55f, -0.36f);
                break;
            }
        }

        face?.Pop();
        b.Pop();
        return (t * lantern, faceSize * t.Basis.Scale.X);
    }

    private static readonly Color[] BookColors =
    [
        C(0.92f, 0.42f, 0.46f), C(0.30f, 0.62f, 0.72f), C(0.96f, 0.76f, 0.36f), C(0.60f, 0.50f, 0.88f), C(0.46f, 0.74f, 0.48f), C(0.95f, 0.58f, 0.36f),
    ];

    /// <summary>
    /// A stack of <paramref name="count"/> books (width ≈ <paramref name="width"/> m), covers and cream page blocks, each
    /// turned a little (±15°); <paramref name="seed"/> picks the sizes and turns and offsets the cover colours.
    /// </summary>
    private static void Books(MeshBuilder b, Transform3D t, int count, float width, int seed)
    {
        b.Push(t);
        var rng = new Random(seed);
        var y = 0f;
        for (var i = 0; i < count; i++)
        {
            var w = width * (0.82f + 0.2f * (float)rng.NextDouble());
            var d = w * 0.72f;
            var h = width * (0.12f + 0.08f * (float)rng.NextDouble());
            var yaw = (float)(rng.NextDouble() * 30 - 15);
            var c = BookColors[(i + seed) % BookColors.Length];
            b.Push(T(new Vector3(0, y, 0), new Vector3(0, yaw, 0)));
            b.RoundBox(new Vector3(0, h * 0.5f, 0), new Vector3(w * 0.96f, h * 0.84f, d * 0.94f), 0.15f, Cream, segments: 8, rings: 6);
            b.RoundBox(new Vector3(0, h * 0.06f, 0), new Vector3(w, h * 0.12f, d), 0.25f, c, segments: 8, rings: 6);
            b.RoundBox(new Vector3(0, h * 0.94f, 0), new Vector3(w, h * 0.12f, d), 0.25f, c, segments: 8, rings: 6);
            b.RoundBox(new Vector3(-w * 0.48f, h * 0.5f, 0), new Vector3(w * 0.06f, h, d), 0.4f, c.Darkened(0.12f), segments: 8, rings: 6);
            b.Pop();
            y += h;
        }

        b.Pop();
    }

    // ------------------------------------------------------------------ launcher, overlook, stepping stone

    /// <summary>
    /// A bloom launcher: a round pad (radius 1.65 m, walkable top 0.32 m at scale 1) with a glowing core and six separate
    /// petals the app rotates open or closed (<see cref="LauncherArt.PetalPose"/>). Base at y = 0. <see cref="PlanetVisual"/>
    /// scales it so the drawn pad matches the composed pad radius (<see cref="LauncherArt.PadRadiusMetres"/>); the core's
    /// glow material is replaced there by the destination's colour. <paramref name="palette"/> null gives a pink-and-gold
    /// launcher, tinted per pad by the app.
    /// </summary>
    public static LauncherArt Launcher(PlanetPalette? palette = null)
    {
        var accent = palette?.Accent ?? C(1.0f, 0.55f, 0.72f);
        var glow = palette?.Glow ?? C(1.0f, 0.82f, 0.45f);
        const float top = 0.32f, radius = 1.65f;
        var pad = new MeshBuilder();
        pad.Lathe([new(0, 0), new(radius - 0.1f, 0), new(radius, 0.1f), new(radius, 0.2f), new(radius - 0.12f, top), new(radius - 0.12f, top), new(0.95f, top), new(0.95f, top), new(0.9f, top - 0.03f), new(0, top - 0.03f)],
            (_, i) => i < 4 ? StoneDark.Lerp(accent, 0.25f) : i < 6 ? Stone.Lerp(accent, 0.15f) : Cream.Lerp(accent, 0.25f), 28);
        pad.Torus(T(new Vector3(0, top + 0.01f, 0)), radius - 0.25f, 0.035f, Glowing(glow, 0.6f), 40, 5);
        for (var k = 0; k < 10; k++)
        {
            var a = k * Mathf.Tau / 10;
            pad.Ellipsoid(new Vector3(Mathf.Sin(a) * 0.5f, top, Mathf.Cos(a) * 0.5f), new Vector3(0.06f, 0.05f, 0.06f), Glowing(glow, 0.7f), 8, 5);
        }

        var padMesh = Commit(pad);
        var core = new MeshBuilder();
        core.Ellipsoid(new Vector3(0, top + 0.08f, 0), new Vector3(0.36f, 0.24f, 0.36f), Colors.White, 18, 10);
        var coreMesh = Commit(core, null, GlowMaterial(glow, 2.4f, 0.25f));
        var petals = new List<ArrayMesh>();
        for (var i = 0; i < 6; i++) // six petals: a flower bud, never a five-pointed star
        {
            var pb = new MeshBuilder();
            Petal(pb, 1.15f, 0.36f, accent.Lerp(Colors.White, 0.25f + 0.04f * i), glow);
            petals.Add(Commit(pb));
        }

        return new LauncherArt(padMesh, coreMesh, petals, top, radius);
    }

    /// <summary>A cupped petal from its hinge (origin) along +Z, curling up at the tip; thin, closed shell.</summary>
    private static void Petal(MeshBuilder b, float length, float halfWidth, Color colour, Color glow)
    {
        const int nu = 9, nv = 6;
        Vector3 P(float u, float v, float off)
        {
            var w = halfWidth * Mathf.Sin(Mathf.Pi * Mathf.Pow(u, 0.75f)) + 0.02f;
            var x = v * w;
            var y = 0.16f * v * v * w / halfWidth + 0.28f * u * u * u + off;
            return new Vector3(x, y, u * length);
        }

        for (var side = 0; side < 2; side++)
        {
            var off = side == 0 ? 0.0f : -0.035f;
            for (var i = 0; i < nu; i++)
            for (var j = 0; j < nv; j++)
            {
                float u0 = i / (float)nu, u1 = (i + 1) / (float)nu, v0 = j / (float)nv * 2 - 1, v1 = (j + 1) / (float)nv * 2 - 1;
                var p00 = P(u0, v0, off);
                var p10 = P(u1, v0, off);
                var p01 = P(u0, v1, off);
                var p11 = P(u1, v1, off);
                Vector3 N(float u, float v)
                {
                    var du = P(Mathf.Min(u + 0.01f, 1), v, off) - P(Mathf.Max(u - 0.01f, 0), v, off);
                    var dv = P(u, Mathf.Min(v + 0.02f, 1), off) - P(u, Mathf.Max(v - 0.02f, -1), off);
                    var n = du.Cross(dv).Normalized();
                    if (n.Y < 0) n = -n;
                    return side == 0 ? n : -n;
                }

                Color Col(float u, float v) => side == 1
                    ? colour.Lerp(Colors.White, 0.15f + 0.2f * u)
                    : Glowing(colour.Lerp(glow, 0.6f * (1 - u)).Lerp(Colors.White, 0.3f * u * (1 - Mathf.Abs(v))), 0.35f * (1 - u) * (1 - u));
                b.Tri(p00, p10, p11, N(u0, v0), N(u1, v0), N(u1, v1), Col(u0, v0), Col(u1, v0), Col(u1, v1));
                b.Tri(p00, p11, p01, N(u0, v0), N(u1, v1), N(u0, v1), Col(u0, v0), Col(u1, v1), Col(u0, v1));
            }
        }
    }

    /// <summary>
    /// The overlook ("inspection station"): a round stone platform 2.4 m high, radius <see cref="OverlookRadiusMetres"/>
    /// (the composer's default <see cref="PlanetParams.OverlookTop"/> and radius: the walker stands on what is drawn), with a
    /// rail (open towards +Z, where stepping stones arrive) and a small brass telescope on a tripod near the −Z edge. Base at
    /// y = 0 (the footing sinks 0.3 m below it).
    /// </summary>
    public static ArrayMesh Overlook(PlanetPalette? palette = null)
    {
        var accent = palette?.Accent ?? C(0.93f, 0.40f, 0.44f);
        var glow = palette?.Glow ?? C(1.0f, 0.8f, 0.45f);
        const float h = OverlookTopMeshMetres, r = OverlookRadiusMetres;
        var b = new MeshBuilder();
        b.Lathe([new(0, -0.3f), new(r * 1.12f, -0.3f), new(r * 1.12f, 0.2f), new(r * 1.0f, 0.5f), new(r * 0.92f, 1.4f), new(r * 0.96f, h - 0.35f), new(r * 0.96f, h - 0.35f), new(r + 0.06f, h - 0.25f), new(r + 0.06f, h - 0.05f), new(r, h), new(r, h), new(0, h)],
            (_, i) => i < 3 ? StoneDark : i < 6 ? Stone : i < 10 ? accent.Lerp(Stone, 0.4f) : Cream.Lerp(Stone, 0.4f), 28);
        b.Torus(T(new Vector3(0, h + 0.005f, 0)), r * 0.55f, 0.03f, Stone.Darkened(0.08f), 28, 4);
        // Rail posts and top rail, open towards +Z (a 70° gap).
        const float railR = r - 0.12f, gap = 35f;
        var posts = 11;
        for (var k = 0; k < posts; k++)
        {
            var a = Mathf.DegToRad(gap + k * (360f - 2 * gap) / (posts - 1));
            var at = new Vector3(Mathf.Sin(a) * railR, h, Mathf.Cos(a) * railR);
            b.Cylinder(at, at + new Vector3(0, 1.0f, 0), 0.05f, 0.04f, WoodLight, 6);
            b.Ellipsoid(at + new Vector3(0, 1.04f, 0), new Vector3(0.07f, 0.07f, 0.07f), k == 0 || k == posts - 1 ? Glowing(glow) : Brass, 8, 5);
        }

        b.TorusArc(T(new Vector3(0, h + 0.98f, 0)), railR, 0.045f, Mathf.DegToRad(gap), Mathf.DegToRad(360 - gap), WoodLight, 40, 6);
        b.TorusArc(T(new Vector3(0, h + 0.5f, 0)), railR, 0.03f, Mathf.DegToRad(gap), Mathf.DegToRad(360 - gap), WoodLight.Darkened(0.1f), 40, 5);
        // Telescope on a tripod, pointing up and out over the −Z edge.
        var tri = new Vector3(0.3f, h, -1.1f);
        for (var k = 0; k < 3; k++)
        {
            var a = k * Mathf.Tau / 3;
            b.Cylinder(tri + new Vector3(Mathf.Sin(a) * 0.35f, 0, Mathf.Cos(a) * 0.35f), tri + new Vector3(0, 1.05f, 0), 0.03f, 0.025f, Ink.Lightened(0.25f), 5);
        }

        b.Push(T(tri + new Vector3(0, 1.12f, 0), new Vector3(28, 0, 0)));
        b.Lathe([new(0, -0.45f), new(0.07f, -0.45f), new(0.07f, -0.45f), new(0.085f, 0.1f), new(0.085f, 0.1f), new(0.11f, 0.12f), new(0.12f, 0.55f), new(0.12f, 0.55f), new(0, 0.55f)],
            (_, i) => i >= 7 ? Glowing(C(0.6f, 0.9f, 1f), 0.8f) : i >= 4 ? Brass : Brass.Darkened(0.2f), 14, axis: new Basis(Vector3.Right, -Mathf.Pi / 2));
        b.Torus(T(new Vector3(0, 0, -0.1f), new Vector3(90, 0, 0)), 0.1f, 0.025f, accent, 14, 5);
        b.Pop();
        return Commit(b);
    }

    /// <summary>
    /// A rounded stepping stone with its top at <paramref name="topMetres"/> (radius 0.72–0.8 m by <paramref name="seed"/>,
    /// a gently wobbly outline; it sinks 0.4 m into the ground). The app scales it by the composed step radius over 0.6 m
    /// (the composer's default <see cref="PlanetParams.StepRadius"/>), so at the default the stone's top is drawn a little
    /// wider than the walkable disc.
    /// </summary>
    public static ArrayMesh SteppingStone(float topMetres = 0.5f, int seed = 0)
    {
        var rng = new Random(seed);
        var r = 0.72f + 0.08f * (float)rng.NextDouble();
        var b = new MeshBuilder();
        var phase = (float)rng.NextDouble() * 6f;
        b.Lathe([new(0, -0.4f), new(r * 1.04f, -0.4f), new(r * 1.06f, topMetres * 0.4f), new(r, topMetres - 0.1f), new(r * 0.86f, topMetres), new(0, topMetres + 0.01f)],
            (_, i) => i >= 4 ? Stone.Lerp(Leaf, 0.25f) : i >= 3 ? Stone : StoneDark, 16,
            radiusScale: (a, _) => 1f + 0.05f * Mathf.Sin(3 * a + phase) + 0.03f * Mathf.Sin(5 * a + phase * 2));
        return Commit(b);
    }

    // ------------------------------------------------------------------ landmarks

    /// <summary>
    /// A planet's landmark (≈ 8–12 m; Quiet's is a little 5 m arch): one distinct silhouette per theme, base at y = 0.
    /// <paramref name="withBell"/> false leaves the bell tower's bell out: the planet hangs it separately
    /// (<see cref="BellTowerBell"/>) so it can swing when rung (PLANET-56).
    /// </summary>
    public static ArrayMesh Landmark(PlanetTheme theme, bool withBell = true)
    {
        var p = Palette(theme);
        var b = new MeshBuilder();
        switch (theme)
        {
            case PlanetTheme.Hearth: Lighthouse(b, p); break;
            case PlanetTheme.Meadow: FlowerTree(b, p); break;
            case PlanetTheme.Workshop: Chimney(b, p); break;
            case PlanetTheme.Library: BookTower(b, p); break;
            case PlanetTheme.Chime:
                BellTowerFrame(b, p);
                if (withBell) Bell(b, p, new Vector3(0, BellTower.BellPivotMeshMetres, 0));
                break;
            case PlanetTheme.Theatre: TentArch(b, p); break;
            case PlanetTheme.Vault: DomedVault(b, p); break;
            case PlanetTheme.Crystal: CrystalSpire(b, p); break;
            case PlanetTheme.Wild: TreeArch(b, p); break;
            case PlanetTheme.Quiet: StoneArch(b, p); break;
            default: Shroud(b, p); break;
        }

        return Commit(b);
    }

    /// <summary>Hearth's landmark: a striped lighthouse with a glowing lamp room (≈ 10.2 m), its door on the −Z front.</summary>
    private static void Lighthouse(MeshBuilder b, PlanetPalette p)
    {
        // Rocks at the foot, a tapered striped tower, a gallery, a glowing lamp room, a red cap and a glowing finial.
        b.Ellipsoid(new Vector3(-1.4f, 0.2f, 0.6f), new Vector3(1.2f, 0.7f, 1.0f), StoneDark, 12, 7);
        b.Ellipsoid(new Vector3(1.3f, 0.1f, 0.9f), new Vector3(1.0f, 0.55f, 0.9f), Stone, 12, 7);
        b.Ellipsoid(new Vector3(0.6f, 0.1f, -1.4f), new Vector3(0.8f, 0.45f, 0.7f), Stone.Darkened(0.05f), 12, 7);
        var prof = new List<Vector2>();
        var cols = new List<Color>();
        const int stripes = 6;
        for (var s = 0; s <= stripes; s++)
        {
            var y = s * 7f / stripes;
            var r = Mathf.Lerp(1.7f, 1.05f, y / 7f);
            if (s > 0) { prof.Add(new Vector2(r, y)); cols.Add(s % 2 == 1 ? Cream : p.Accent); }
            if (s < stripes) { prof.Add(new Vector2(r, y)); cols.Add(s % 2 == 0 ? Cream : p.Accent); }
        }

        b.Lathe(prof, (_, i) => cols[i], 20);
        for (var k = 0; k < 3; k++)
            b.RoundBox(new Vector3(0, 1.8f + k * 1.9f, -Mathf.Lerp(1.62f, 1.08f, (1.8f + k * 1.9f) / 7f)), new Vector3(0.4f, 0.6f, 0.2f), 0.4f, k == 0 ? Glowing(p.Glow, 0.4f) : Ink.Lightened(0.15f));
        b.RoundBox(new Vector3(0, 0.55f, -1.66f), new Vector3(0.75f, 1.1f, 0.2f), 0.35f, Wood);
        b.Lathe([new(0, 6.95f), new(1.5f, 6.95f), new(1.5f, 6.95f), new(1.5f, 7.15f), new(1.5f, 7.15f), new(0, 7.15f)], (_, _) => Ink.Lightened(0.3f), 24);
        for (var k = 0; k < 12; k++)
        {
            var a = k * Mathf.Tau / 12;
            var at = new Vector3(Mathf.Sin(a) * 1.4f, 7.15f, Mathf.Cos(a) * 1.4f);
            b.Cylinder(at, at + new Vector3(0, 0.6f, 0), 0.04f, 0.04f, Ink.Lightened(0.3f), 4);
        }

        b.Torus(T(new Vector3(0, 7.75f, 0)), 1.4f, 0.06f, Ink.Lightened(0.3f), 28, 5);
        b.Lathe([new(0, 7.15f), new(0.82f, 7.15f), new(0.82f, 8.45f), new(0, 8.45f)], (_, _) => Glowing(p.Glow, 0.95f), 16);
        for (var k = 0; k < 6; k++)
        {
            var a = k * Mathf.Tau / 6;
            b.Cylinder(new Vector3(Mathf.Sin(a) * 0.84f, 7.15f, Mathf.Cos(a) * 0.84f), new Vector3(Mathf.Sin(a) * 0.84f, 8.45f, Mathf.Cos(a) * 0.84f), 0.05f, 0.05f, p.Accent.Darkened(0.2f), 4);
        }

        b.Lathe([new(0, 8.4f), new(1.15f, 8.4f), new(1.15f, 8.4f), new(0.9f, 8.75f), new(0.45f, 9.45f), new(0, 9.8f)], (_, _) => p.Accent, 20);
        b.Ellipsoid(new Vector3(0, 10.0f, 0), new Vector3(0.22f, 0.22f, 0.22f), Glowing(p.Glow), 10, 6);
    }

    /// <summary>Meadow's landmark: a giant flower on a curving stem (≈ 9 m), its head tilted towards the −Z front.</summary>
    private static void FlowerTree(MeshBuilder b, PlanetPalette p)
    {
        // A giant flower on a curvy trunk-stem: leaves, a ring of big petals, a glowing centre, tilted towards the front.
        var stem = new Color(0.36f, 0.62f, 0.34f);
        b.Tube([new(0, -0.2f, 0), new(0.5f, 2.2f, 0.3f), new(-0.4f, 4.6f, 0.1f), new(0.1f, 6.8f, -0.2f), new(0, 8.0f, -0.5f)], s => Mathf.Lerp(0.75f, 0.32f, s), s => stem.Lerp(stem.Lightened(0.25f), s), 12);
        foreach (var (y, yaw, len) in new[] { (2.2f, 30f, 2.4f), (3.8f, 200f, 2.1f), (5.4f, 110f, 1.7f) })
        {
            b.Push(T(new Vector3(0, y, 0), new Vector3(0, yaw, 0)));
            b.Push(T(new Vector3(len * 0.5f, 0.35f, 0), new Vector3(0, 0, 22)));
            b.Ellipsoid(Vector3.Zero, new Vector3(len * 0.55f, 0.08f, len * 0.26f), p.GroundLow.Lerp(stem, 0.5f).Lightened(0.1f), 14, 7);
            b.Pop();
            b.Pop();
        }

        b.Push(T(new Vector3(0, 8.2f, -0.6f), new Vector3(-24, 0, 0)));
        for (var k = 0; k < 9; k++)
        {
            var a = k * 360f / 9;
            b.Push(T(Vector3.Zero, new Vector3(0, a, 0)));
            b.Push(T(new Vector3(0, 0.1f, 1.55f), new Vector3(-12, 0, 0)));
            b.Ellipsoid(Vector3.Zero, new Vector3(0.75f, 0.16f, 1.25f), (k % 2 == 0 ? p.Accent : p.Accent.Lightened(0.18f)), 14, 7);
            b.Pop();
            b.Pop();
        }

        for (var k = 0; k < 6; k++)
        {
            var a = k * 360f / 6 + 30;
            b.Push(T(Vector3.Zero, new Vector3(0, a, 0)));
            b.Push(T(new Vector3(0, -0.25f, 0.9f), new Vector3(20, 0, 0)));
            b.Ellipsoid(Vector3.Zero, new Vector3(0.4f, 0.1f, 0.8f), stem.Lightened(0.15f), 10, 5);
            b.Pop();
            b.Pop();
        }

        b.Ellipsoid(new Vector3(0, 0.25f, 0), new Vector3(1.0f, 0.42f, 1.0f), Glowing(p.Glow, 0.55f), 18, 9);
        for (var k = 0; k < 14; k++)
        {
            var a = k * Mathf.Tau / 14;
            b.Ellipsoid(new Vector3(Mathf.Sin(a) * 0.72f, 0.52f, Mathf.Cos(a) * 0.72f), new Vector3(0.1f, 0.1f, 0.1f), Glowing(p.Glow.Lightened(0.2f)), 6, 4);
        }

        b.Pop();
    }

    /// <summary>Workshop's landmark: a brick chimney (crown at 9.35 m, smoke puffs to ≈ 11.9 m) with a brass gear on its −Z face and a boiler.</summary>
    private static void Chimney(MeshBuilder b, PlanetPalette p)
    {
        // A tall brick chimney with dark bands, a flared crown and soft smoke puffs; a big brass gear on its face; a boiler.
        var brick = C(0.84f, 0.42f, 0.34f);
        var prof = new List<Vector2>();
        var cols = new List<Color>();
        for (var s = 0; s <= 8; s++)
        {
            var y = s * 1.1f;
            var r = Mathf.Lerp(1.25f, 0.82f, y / 8.8f);
            prof.Add(new Vector2(r, y));
            cols.Add(s % 3 == 0 ? brick.Darkened(0.25f) : brick.Lightened(0.03f * (s % 2)));
        }

        prof.Add(new Vector2(1.05f, 9.0f)); cols.Add(brick.Darkened(0.3f));
        prof.Add(new Vector2(1.05f, 9.35f)); cols.Add(brick.Darkened(0.3f));
        prof.Add(new Vector2(0.7f, 9.35f)); cols.Add(Ink);
        prof.Add(new Vector2(0.0f, 9.0f)); cols.Add(Ink);
        b.Lathe(prof, (_, i) => cols[i], 10, smooth: false);
        b.Ellipsoid(new Vector3(0.2f, 10.0f, 0.1f), new Vector3(0.8f, 0.6f, 0.8f), Glowing(C(0.95f, 0.92f, 1f), 0.15f), 12, 7);
        b.Ellipsoid(new Vector3(0.9f, 10.8f, 0.3f), new Vector3(0.65f, 0.5f, 0.65f), Glowing(C(0.95f, 0.92f, 1f), 0.15f), 12, 7);
        b.Ellipsoid(new Vector3(1.4f, 11.5f, 0.2f), new Vector3(0.45f, 0.38f, 0.45f), Glowing(C(0.95f, 0.92f, 1f), 0.15f), 10, 6);
        b.Push(T(new Vector3(0, 5.0f, -1.2f)));
        b.Gear(2.3f, 1.85f, 0.45f, 12, Brass, Brass.Darkened(0.25f));
        b.Gear(1.1f, 0.85f, 0.5f, 8, Brass.Darkened(0.12f), Brass.Darkened(0.3f));
        b.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0, -0.4f), 0.35f, 0.35f, Glowing(p.Glow, 0.8f), 14);
        for (var k = 0; k < 6; k++)
        {
            var a = k * Mathf.Tau / 6;
            b.RoundBox(new Vector3(Mathf.Cos(a) * 1.35f, Mathf.Sin(a) * 1.35f, -0.05f), new Vector3(0.75f, 0.28f, 0.3f), 0.4f, Brass.Darkened(0.08f), new Vector3(0, 0, Mathf.RadToDeg(a)));
        }

        b.Pop();
        b.RoundBox(new Vector3(1.9f, 0.9f, 0.4f), new Vector3(1.6f, 1.8f, 1.5f), 0.5f, p.Accent);
        b.Torus(T(new Vector3(1.9f, 1.85f, 0.4f)), 0.55f, 0.1f, Brass, 14, 5);
        b.Ellipsoid(new Vector3(1.9f, 0.9f, -0.36f), new Vector3(0.3f, 0.3f, 0.06f), Glowing(p.Glow), 10, 5);
        b.Tube([new(1.9f, 1.8f, 0.4f), new(1.9f, 3.0f, 0.4f), new(1.4f, 3.6f, 0.3f), new(1.0f, 3.7f, 0.2f)], _ => 0.16f, _ => Brass.Darkened(0.1f), 8);
    }

    /// <summary>Library's landmark: a twisting tower of nine giant books (random sizes from a fixed seed, ≈ 8 m) under a reading lamp (≈ 10 m in all).</summary>
    private static void BookTower(MeshBuilder b, PlanetPalette p)
    {
        // A leaning, twisting tower of giant books with a reading lamp on top.
        var rng = new Random(7);
        var y = 0f;
        for (var i = 0; i < 9; i++)
        {
            var w = 3.1f - i * 0.12f + (float)rng.NextDouble() * 0.3f;
            var h = 0.75f + (float)rng.NextDouble() * 0.3f;
            var yaw = i * 13f + (float)rng.NextDouble() * 14f - 7f;
            var shift = new Vector3(Mathf.Sin(i * 0.6f) * 0.25f, y, 0);
            var c = BookColors[i % BookColors.Length];
            b.Push(T(shift, new Vector3(0, yaw, 0)));
            var d = w * 0.7f;
            b.RoundBox(new Vector3(0.04f, h * 0.5f, 0), new Vector3(w * 0.95f, h * 0.82f, d * 0.93f), 0.12f, Cream, segments: 8, rings: 6);
            b.RoundBox(new Vector3(0, h * 0.06f, 0), new Vector3(w, h * 0.13f, d), 0.2f, c, segments: 8, rings: 6);
            b.RoundBox(new Vector3(0, h * 0.94f, 0), new Vector3(w, h * 0.13f, d), 0.2f, c, segments: 8, rings: 6);
            b.RoundBox(new Vector3(-w * 0.48f, h * 0.5f, 0), new Vector3(w * 0.06f, h, d), 0.35f, c.Darkened(0.15f), segments: 8, rings: 6);
            b.Box(new Vector3(-w * 0.51f, h * 0.5f, 0), new Vector3(0.02f, h * 0.18f, d * 0.8f), Brass);
            if (i % 3 == 1) b.Box(new Vector3(w * 0.1f, h * 0.2f, -d * 0.5f - 0.15f), new Vector3(0.12f, 0.6f, 0.3f), C(0.95f, 0.36f, 0.42f));
            b.Pop();
            y += h;
        }

        // Reading lamp: a curved brass post with a glowing shade.
        b.Cylinder(new Vector3(0.4f, y, 0), new Vector3(0.4f, y + 1.3f, 0), 0.07f, 0.06f, Brass, 8);
        b.Tube([new(0.4f, y + 1.3f, 0), new(0.4f, y + 1.8f, -0.1f), new(0.1f, y + 2.0f, -0.5f)], _ => 0.06f, _ => Brass, 6, capEnds: false);
        b.Lathe([new(0.0f, 0.25f), new(0.25f, 0.2f), new(0.55f, -0.15f), new(0.55f, -0.15f), new(0.0f, -0.05f)], (_, i) => i < 3 ? p.Accent : Glowing(p.Glow), 16, origin: new Vector3(0.1f, y + 1.9f, -0.55f));
        b.Lathe([new(0, y), new(0.45f, y), new(0.45f, y), new(0.45f, y + 0.12f), new(0, y + 0.12f)], (_, _) => Brass.Darkened(0.2f), 14, origin: new Vector3(0.4f, 0, 0));
    }

    /// <summary>
    /// Chime's landmark: an open bell tower with a pyramid roof (≈ 10.2 m); the big bell inside is <see cref="Bell"/>. The
    /// base, pillars and bell are drawn from <see cref="BellTower"/>'s measurements, which are also its collision
    /// (PLANET-56): change them there, not here.
    /// </summary>
    private static void BellTowerFrame(MeshBuilder b, PlanetPalette p)
    {
        // Four posts on a round base, a swooping roof with chimes at its corners.
        const float baseR = BellTower.BaseRadiusMetres, baseTop = BellTower.BaseTopMeshMetres, postTop = BellTower.PillarTopMeshMetres;
        b.Lathe([new(0, 0), new(baseR, 0), new(baseR, baseTop - 0.1f), new(baseR - 0.2f, baseTop), new(0, baseTop)], (_, i) => i < 2 ? StoneDark : Stone, 20);
        var post = p.Accent.Darkened(0.15f);
        foreach (var (x, z) in BellTower.Pillars)
        {
            b.Cylinder(new Vector3(x, baseTop, z), new Vector3(x, postTop, z), BellTower.PillarRadiusMetres, 0.15f, post, 8);
            b.Ellipsoid(new Vector3(x, baseTop + 0.1f, z), new Vector3(BellTower.PillarSolidRadiusMetres, 0.14f, BellTower.PillarSolidRadiusMetres), Brass, 8, 4);
        }

        b.RoundBox(new Vector3(0, 6.5f, -1.5f), new Vector3(3.4f, 0.3f, 0.3f), 0.4f, post.Darkened(0.1f));
        b.RoundBox(new Vector3(0, 6.5f, 1.5f), new Vector3(3.4f, 0.3f, 0.3f), 0.4f, post.Darkened(0.1f));
        b.RoundBox(new Vector3(0, 6.5f, 0), new Vector3(3.4f, 0.3f, 0.3f), 0.4f, post.Darkened(0.1f));
        b.Lathe([new(0, 6.6f), new(3.0f, 6.6f), new(3.1f, 6.85f), new(2.6f, 7.0f), new(1.4f, 7.9f), new(0.5f, 9.3f), new(0, 9.7f)], (_, i) => i < 3 ? p.Accent.Lightened(0.2f) : p.Accent, 4, startAngle: Mathf.Pi / 4, smooth: false);
        b.Ellipsoid(new Vector3(0, 9.9f, 0), new Vector3(0.25f, 0.25f, 0.25f), Glowing(p.Glow), 10, 6);
        foreach (var (x, z) in new[] { (-2.2f, -2.2f), (2.2f, -2.2f), (-2.2f, 2.2f), (2.2f, 2.2f) })
        {
            b.Cylinder(new Vector3(x, 6.75f, z), new Vector3(x, 6.3f, z), 0.02f, 0.02f, Ink, 4);
            b.Lathe([new(0, 0), new(0.1f, -0.05f), new(0.16f, -0.3f), new(0.2f, -0.35f)], (_, _) => Brass, 10, origin: new Vector3(x, 6.3f, z));
            b.Ellipsoid(new Vector3(x, 5.94f, z), new Vector3(0.06f, 0.06f, 0.06f), Glowing(p.Glow), 6, 4);
        }

    }

    /// <summary>
    /// The bell tower's bell with its rope and glowing clapper, hanging from <paramref name="pivot"/> (its mouth
    /// <see cref="BellTower.BellRimRadiusMetres"/> wide, <see cref="BellTower.BellDropMetres"/> below the pivot).
    /// </summary>
    private static void Bell(MeshBuilder b, PlanetPalette p, Vector3 pivot)
    {
        // The profile as first drawn (a 1.3 m mouth, 2.05 m tall), scaled to the measured mouth and drop.
        const float sx = BellTower.BellRimRadiusMetres / 1.3f, sy = BellTower.BellDropMetres / 2.05f;
        Vector2[] bell = [new(0, 0), new(0.35f, -0.05f), new(0.65f, -0.4f), new(0.75f, -1.1f), new(0.9f, -1.6f), new(1.25f, -1.95f), new(1.3f, -2.05f)];
        bell = bell.Select(q => new Vector2(q.X * sx, q.Y * sy)).ToArray();
        b.Lathe(bell, (_, i) => C(1.0f, 0.86f, 0.5f).Lightened(0.04f * i), 20, origin: pivot);
        b.Lathe(bell.Select(q => new Vector2(q.X * 0.88f, q.Y - 0.05f)).ToArray(), (_, _) => C(0.75f, 0.55f, 0.3f), 20, origin: pivot, inward: true);
        // The rope and the glowing clapper, its tip just below the mouth (where a twirl underneath points at it).
        const float clapper = 0.22f, clapperAt = BellTower.BellDropMetres - 0.1f;
        b.Cylinder(pivot + new Vector3(0, -0.05f, 0), pivot + new Vector3(0, -clapperAt + clapper * 0.5f, 0), 0.05f, 0.05f, Ink, 4);
        b.Ellipsoid(pivot + new Vector3(0, -clapperAt, 0), new Vector3(clapper, clapper, clapper), Glowing(p.Glow), 10, 6);
    }

    /// <summary>The bell tower's bell alone, its pivot (where it hangs from) at the origin: the planet swings it when rung (PLANET-56).</summary>
    public static ArrayMesh BellTowerBell(PlanetTheme theme)
    {
        var b = new MeshBuilder();
        Bell(b, Palette(theme), Vector3.Zero);
        return Commit(b);
    }

    /// <summary>Theatre's landmark: a striped tent arch (≈ 10.5 m to the pennant, ≈ 7.4 m across) with a string of bulbs, open along Z.</summary>
    private static void TentArch(MeshBuilder b, PlanetPalette p)
    {
        // A tall striped arch (a pointed tent gate) between two striped posts, a little dome tent with a pennant on top,
        // and a string of glowing bulbs under the arch.
        var stripeA = p.GroundLow.Darkened(0.05f);
        var stripeB = Cream;
        var path = new List<Vector3>();
        const int n = 24;
        for (var i = 0; i <= n; i++)
        {
            var t = i / (float)n;
            var a = Mathf.Pi * t;
            path.Add(new Vector3(-Mathf.Cos(a) * 3.3f, 2.4f + Mathf.Pow(Mathf.Max(0f, Mathf.Sin(a)), 0.8f) * 5.0f, 0));
        }

        for (var s = 0; s < n; s += 2)
            b.Tube(path.GetRange(s, 3), _ => 0.42f, _ => (s / 2) % 2 == 0 ? stripeA : stripeB, 10, capEnds: s == 0 || s == n - 2);
        foreach (var x in new[] { -3.3f, 3.3f })
        {
            var prof = new List<Vector2>();
            var cols = new List<Color>();
            for (var s = 0; s < 6; s++)
            {
                prof.Add(new Vector2(0.48f, s * 0.42f)); cols.Add(s % 2 == 0 ? stripeB : stripeA);
                prof.Add(new Vector2(0.48f, (s + 1) * 0.42f)); cols.Add(s % 2 == 0 ? stripeB : stripeA);
            }

            prof.Insert(0, new Vector2(0, 0)); cols.Insert(0, stripeB);
            b.Lathe(prof, (_, i) => cols[i], 12, origin: new Vector3(x, 0, 0));
            b.Lathe([new(0.0f, 0), new(0.7f, 0), new(0.7f, 0.2f), new(0.55f, 0.3f), new(0, 0.3f)], (_, _) => p.Accent.Darkened(0.3f), 14, origin: new Vector3(x, 0, 0));
        }

        // Dome tent at the crown: vertical stripes (per segment colours), a flag.
        b.Lathe([new(0, 6.8f), new(1.25f, 6.8f), new(1.3f, 7.3f), new(1.0f, 8.1f), new(0.5f, 8.7f), new(0, 9.0f)], (j, _) => j % 2 == 0 ? stripeA : stripeB, 12, smooth: true);
        b.Lathe([new(1.3f, 7.3f), new(1.45f, 7.1f), new(1.45f, 7.1f), new(1.25f, 6.85f)], (j, _) => j % 2 == 0 ? stripeB : stripeA, 12);
        b.Cylinder(new Vector3(0, 8.9f, 0), new Vector3(0, 10.4f, 0), 0.05f, 0.05f, Brass, 5);
        b.Extrude([new(0, 0), new(0.9f, -0.2f), new(0, -0.45f)], 0.04f, C(1f, 0.85f, 0.4f), new Vector3(0.0f, 10.4f, 0), axisX: false);
        b.Ellipsoid(new Vector3(0, 10.45f, 0), new Vector3(0.1f, 0.1f, 0.1f), Glowing(p.Glow), 6, 4);
        for (var k = 1; k < 10; k++)
        {
            var t = k / 10f;
            var x = Mathf.Lerp(-3.0f, 3.0f, t);
            var y = 5.6f - 1.2f * Mathf.Sin(Mathf.Pi * t);
            b.Ellipsoid(new Vector3(x, y, -0.1f), new Vector3(0.12f, 0.14f, 0.12f), Glowing(k % 2 == 0 ? p.Glow : C(1f, 0.6f, 0.7f)), 8, 5);
        }

        b.Tube(Enumerable.Range(0, 11).Select(k => new Vector3(Mathf.Lerp(-3.0f, 3.0f, k / 10f), 5.72f - 1.2f * Mathf.Sin(Mathf.Pi * k / 10f), -0.1f)).ToList(), _ => 0.02f, _ => Ink, 4, capEnds: false);
    }

    /// <summary>Vault's landmark: a domed drum (≈ 8.9 m, radius 3.6 m) with a round door on its −Z front.</summary>
    private static void DomedVault(MeshBuilder b, PlanetPalette p)
    {
        // A round drum with gold bands, a dome, a lantern cupola, and a big round door with a glowing ring on its front.
        var wall = p.GroundHigh.Lerp(Cream, 0.4f);
        b.Lathe([new(0, 0), new(3.6f, 0), new(3.6f, 0.4f), new(3.6f, 0.4f), new(3.2f, 0.5f), new(3.2f, 3.6f), new(3.2f, 3.6f), new(3.4f, 3.75f), new(3.4f, 4.0f), new(3.4f, 4.0f), new(3.15f, 4.0f)],
            (_, i) => i < 3 ? StoneDark : i < 6 ? wall : Brass, 28);
        var dome = new List<Vector2>();
        for (var k = 0; k <= 8; k++)
        {
            var a = k / 8f * Mathf.Pi / 2;
            dome.Add(new Vector2(Mathf.Cos(a) * 3.15f, 4.0f + Mathf.Sin(a) * 3.0f));
        }

        b.Lathe(dome, (j, i) => (j % 4 == 0 ? p.Accent.Lerp(Brass, 0.5f) : p.Accent.Lightened(0.15f)), 24);
        foreach (var y in new[] { 1.4f, 2.6f })
            b.Torus(T(new Vector3(0, y, 0)), 3.22f, 0.08f, Brass, 36, 5);
        b.Lathe([new(0, 6.9f), new(0.6f, 6.9f), new(0.6f, 7.8f), new(0.0f, 7.8f)], (_, _) => Glowing(p.Glow, 0.9f), 10);
        b.Lathe([new(0, 7.75f), new(0.8f, 7.75f), new(0.0f, 8.6f)], (_, _) => Brass, 10);
        b.Ellipsoid(new Vector3(0, 8.75f, 0), new Vector3(0.18f, 0.18f, 0.18f), Glowing(p.Glow), 8, 5);
        b.Push(T(new Vector3(0, 1.9f, -3.18f), new Vector3(90, 0, 0)));
        b.Lathe([new(0, 0.0f), new(1.45f, 0.0f), new(1.45f, -0.18f), new(0, -0.22f)], (_, _) => Brass.Darkened(0.15f), 24);
        b.Torus(T(new Vector3(0, -0.2f, 0)), 1.3f, 0.09f, Glowing(p.Glow, 0.85f), 30, 6);
        for (var k = 0; k < 6; k++)
        {
            var a = k * 60f;
            b.RoundBox(new Vector3(0, -0.24f, 0), new Vector3(2.2f, 0.06f, 0.14f), 0.4f, Brass, new Vector3(0, a, 0));
        }

        b.Ellipsoid(new Vector3(0, -0.28f, 0), new Vector3(0.32f, 0.12f, 0.32f), Glowing(p.Glow), 12, 5);
        b.Pop();
    }

    /// <summary>Crystal's landmark: a tall glowing crystal (≈ 11 m with its bead) in a ring of seven smaller ones.</summary>
    private static void CrystalSpire(MeshBuilder b, PlanetPalette p)
    {
        // A tall central crystal with a ring of tilted smaller ones on a rocky base; colours shift from base to tip.
        b.Ellipsoid(new Vector3(0, 0.2f, 0), new Vector3(2.6f, 0.9f, 2.4f), StoneDark.Lerp(p.GroundLow, 0.3f), 14, 7);
        var cyan = p.GroundHigh.Lerp(C(0.5f, 0.9f, 1f), 0.6f);
        b.Push(T(new Vector3(0, 0.3f, 0), new Vector3(0, 10, 0)));
        b.Prism(6, 0.95f, 7.8f, 2.4f, (_, k) => Glowing(cyan.Lerp(p.Accent, k / 4f), 0.08f + 0.06f * k));
        b.Pop();
        for (var i = 0; i < 7; i++)
        {
            var yaw = i * 360f / 7 + 12;
            var h = 2.4f + (i * 37 % 5) * 0.55f;
            b.Push(T(Vector3.Zero, new Vector3(0, yaw, 0)));
            b.Push(T(new Vector3(0, 0.4f, 1.15f), new Vector3(24 + (i % 3) * 6, 0, 0)));
            b.Prism(6, 0.38f + (i % 2) * 0.12f, h, 0.9f, (_, k) => Glowing((i % 2 == 0 ? p.Accent : cyan).Lightened(0.05f * k), 0.08f + 0.07f * k));
            b.Pop();
            b.Pop();
        }

        b.Ellipsoid(new Vector3(0, 11.0f, 0), new Vector3(0.25f, 0.25f, 0.25f), Glowing(p.Glow), 8, 5);
    }

    /// <summary>Wild's landmark: two trunks arching into one canopy (≈ 10.2 m) hung with glowing fruit, open along Z.</summary>
    private static void TreeArch(MeshBuilder b, PlanetPalette p)
    {
        // Two curving trunks that meet in a puffy shared canopy hung with glowing fruit.
        var bark = C(0.62f, 0.42f, 0.32f);
        b.Tube([new(-2.8f, -0.2f, 0), new(-2.6f, 2.0f, 0.2f), new(-1.8f, 4.6f, 0), new(-0.6f, 6.4f, 0), new(0.2f, 6.9f, 0)], s => Mathf.Lerp(0.6f, 0.3f, s), _ => bark, 10);
        b.Tube([new(2.8f, -0.2f, 0), new(2.7f, 2.2f, -0.2f), new(1.9f, 4.7f, 0), new(0.7f, 6.4f, 0), new(-0.2f, 6.9f, 0)], s => Mathf.Lerp(0.6f, 0.3f, s), _ => bark.Lightened(0.06f), 10);
        var green = p.GroundHigh.Lerp(Leaf, 0.5f);
        var puffs = new (Vector3 C, float R)[]
        {
            (new(0, 7.6f, 0), 2.1f), (new(-2.0f, 6.9f, 0.3f), 1.6f), (new(2.1f, 7.0f, -0.2f), 1.7f), (new(-1.0f, 8.6f, -0.4f), 1.4f),
            (new(1.2f, 8.5f, 0.5f), 1.3f), (new(-3.0f, 5.8f, -0.2f), 1.0f), (new(3.1f, 5.9f, 0.3f), 1.05f), (new(0.2f, 9.3f, 0.1f), 1.0f),
        };
        var i = 0;
        foreach (var (c, r) in puffs)
        {
            b.Ellipsoid(c, new Vector3(r, r * 0.88f, r), (i++ % 2 == 0 ? green : green.Lightened(0.12f)), 14, 9);
        }

        var rng = new Random(3);
        for (var k = 0; k < 9; k++)
        {
            var (c, r) = puffs[k % puffs.Length];
            var d = new Vector3((float)rng.NextDouble() - 0.5f, -0.6f, -0.9f).Normalized();
            b.Ellipsoid(c + d * r * 0.98f, new Vector3(0.18f, 0.18f, 0.18f), Glowing(k % 2 == 0 ? p.Accent : p.Glow, 0.7f), 8, 5);
        }

        b.Tube([new(-1.4f, 5.4f, -0.2f), new(-1.3f, 4.4f, -0.3f), new(-1.35f, 3.6f, -0.3f)], _ => 0.04f, _ => Leaf.Darkened(0.2f), 4);
        b.Ellipsoid(new Vector3(-1.35f, 3.5f, -0.3f), new Vector3(0.16f, 0.2f, 0.16f), Glowing(p.Glow), 8, 5);
    }

    /// <summary>Quiet's landmark: a small arch of stacked stones (≈ 4.9 m: an empty world's modest marker) with one hanging lamp.</summary>
    private static void StoneArch(MeshBuilder b, PlanetPalette p)
    {
        // A little arch of stacked rounded stones with a single lamp hanging from its lintel.
        var rng = new Random(11);
        foreach (var x in new[] { -1.6f, 1.6f })
        {
            var y = 0f;
            for (var k = 0; k < 5; k++)
            {
                var h = 0.7f + (float)rng.NextDouble() * 0.15f;
                var w = 0.9f - k * 0.05f;
                b.RoundBox(new Vector3(x + ((float)rng.NextDouble() - 0.5f) * 0.12f, y + h / 2, 0), new Vector3(w, h, w * 0.9f), 0.55f, (k % 2 == 0 ? Stone : Stone.Lerp(p.GroundHigh, 0.4f)), new Vector3(0, (float)rng.NextDouble() * 20, 0));
                y += h * 0.96f;
            }
        }

        b.RoundBox(new Vector3(0, 4.05f, 0), new Vector3(4.6f, 0.75f, 1.0f), 0.5f, Stone.Lerp(p.GroundHigh, 0.25f), new Vector3(0, 0, 3));
        b.RoundBox(new Vector3(-0.4f, 4.65f, 0), new Vector3(1.4f, 0.5f, 0.8f), 0.6f, Stone, new Vector3(0, 10, 0));
        b.Ellipsoid(new Vector3(1.0f, 4.48f, 0.1f), new Vector3(0.5f, 0.12f, 0.4f), Leaf.Lerp(p.GroundHigh, 0.3f), 10, 5);
        b.Cylinder(new Vector3(0, 3.68f, 0), new Vector3(0, 2.9f, 0), 0.025f, 0.025f, Ink, 4);
        b.Lathe([new(0, 0.12f), new(0.2f, 0.06f), new(0.22f, 0)], (_, _) => Brass, 10, origin: new Vector3(0, 2.8f, 0));
        b.Ellipsoid(new Vector3(0, 2.62f, 0), new Vector3(0.16f, 0.2f, 0.16f), Glowing(p.Glow), 10, 6);
        b.Lathe([new(0.16f, 0.0f), new(0.12f, -0.06f), new(0, -0.08f)], (_, _) => Brass, 10, origin: new Vector3(0, 2.45f, 0));
    }

    /// <summary>Veiled's landmark (and the default): a draped glowing shroud (≈ 10.2 m) ringed by mist bands, for a world whose contents are unknown or unlisted.</summary>
    private static void Shroud(MeshBuilder b, PlanetPalette p)
    {
        // A tall draped shroud with soft folds, ringed by glowing mist bands: the surface is unknown.
        var cloth = p.GroundHigh.Lerp(Cream, 0.3f);
        Vector2[] prof = [new(0, 0), new(3.4f, 0), new(3.2f, 0.6f), new(2.4f, 2.5f), new(1.6f, 5.0f), new(1.1f, 7.0f), new(0.8f, 8.4f), new(0.45f, 9.3f), new(0, 9.7f)];
        b.Lathe(prof, (_, i) => Glowing(cloth.Lerp(p.Accent, i / 10f), 0.12f + 0.03f * i), 28,
            radiusScale: (a, y) => 1f + 0.09f * Mathf.Sin(7 * a + y * 0.4f) * Mathf.Clamp(1.2f - y / 9f, 0, 1));
        foreach (var (y, r) in new[] { (1.2f, 3.4f), (3.6f, 2.6f), (6.2f, 1.8f) })
            b.Torus(T(new Vector3(0, y, 0), new Vector3(8, 0, 4)), r, 0.12f, Glowing(p.Glow.Lerp(Colors.White, 0.3f), 0.55f), 36, 5);
        b.Ellipsoid(new Vector3(0, 9.9f, 0), new Vector3(0.3f, 0.3f, 0.3f), Glowing(p.Glow), 10, 6);
    }

    // ------------------------------------------------------------------ decorations

    /// <summary>Leaf colour per theme: fresh greens that sit well on the theme's ground.</summary>
    private static Color Foliage(PlanetTheme theme, PlanetPalette p) => theme switch
    {
        PlanetTheme.Hearth => C(0.42f, 0.78f, 0.56f),
        PlanetTheme.Meadow => C(0.36f, 0.70f, 0.40f),
        PlanetTheme.Workshop => C(0.48f, 0.72f, 0.42f),
        PlanetTheme.Library => C(0.44f, 0.70f, 0.62f),
        PlanetTheme.Chime => C(0.62f, 0.56f, 0.95f),
        PlanetTheme.Theatre => C(0.98f, 0.62f, 0.52f),
        PlanetTheme.Vault => C(0.38f, 0.66f, 0.70f),
        PlanetTheme.Crystal => C(0.62f, 0.78f, 0.98f),
        PlanetTheme.Wild => C(0.56f, 0.82f, 0.36f),
        PlanetTheme.Quiet => C(0.58f, 0.68f, 0.78f),
        _ => p.GroundHigh.Lerp(C(0.6f, 0.7f, 0.8f), 0.4f),
    };

    /// <summary>
    /// A piece of scenery of <paramref name="kind"/> for <paramref name="theme"/>, varied by <paramref name="seed"/>. Trees:
    /// puffy round (Hearth, Meadow, Wild, Theatre, Chime), cone (Quiet, Crystal, Vault, Veiled, Library) or brass pipe
    /// trees with glowing bulbs (Workshop). Flowers: daisies, or bell flowers on Chime. Lamps: paper lantern posts on
    /// Library, brass gas lamps on Workshop, a glowing globe on a post elsewhere. Base at y = 0, metres.
    /// </summary>
    public static ArrayMesh Decoration(DecorationKind kind, PlanetTheme theme, int seed)
    {
        var p = Palette(theme);
        // One repeatable stream per (seed, kind, theme): 7919 and 131 are primes that keep the three apart (rationale for
        // the particular primes not recorded). Changing the mix changes every planet's scenery.
        var rng = new Random(seed * 7919 + (int)kind * 131 + (int)theme);
        float R(float a, float b2) => a + (b2 - a) * (float)rng.NextDouble();
        var b = new MeshBuilder();
        var green = Foliage(theme, p);
        switch (kind)
        {
            case DecorationKind.Tree when theme == PlanetTheme.Workshop:
            {
                var h = R(2.6f, 3.6f);
                b.Cylinder(new Vector3(0, -0.1f, 0), new Vector3(0, h, 0), 0.16f, 0.12f, Brass.Darkened(0.15f), 8);
                b.Lathe([new(0, 0), new(0.35f, 0), new(0.35f, 0.25f), new(0, 0.25f)], (_, _) => Brass.Darkened(0.3f), 10);
                for (var k = 0; k < 4; k++)
                {
                    var a = k * Mathf.Tau / 4 + R(0, 0.5f);
                    var y = h * (0.45f + 0.15f * k);
                    var o = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                    b.Tube([new Vector3(0, y, 0), o * 0.5f + new Vector3(0, y + 0.1f, 0), o * 0.7f + new Vector3(0, y + 0.55f, 0)], _ => 0.07f, _ => Brass, 6);
                    b.Ellipsoid(o * 0.7f + new Vector3(0, y + 0.7f, 0), new Vector3(0.17f, 0.2f, 0.17f), Glowing(p.Glow), 8, 5);
                }

                b.Gear(0.3f, 0.23f, 0.06f, 8, Brass.Darkened(0.05f), Brass.Darkened(0.3f), new Vector3(0, h * 0.35f, -0.16f));
                b.Ellipsoid(new Vector3(0, h + 0.15f, 0), new Vector3(0.24f, 0.28f, 0.24f), Glowing(p.Glow), 10, 6);
                break;
            }

            case DecorationKind.Tree when theme is PlanetTheme.Quiet or PlanetTheme.Crystal or PlanetTheme.Vault or PlanetTheme.Veiled or PlanetTheme.Library:
            {
                var h = R(2.6f, 4.0f);
                var c = theme == PlanetTheme.Crystal ? p.Accent.Lerp(p.GroundHigh, 0.5f) : theme == PlanetTheme.Library ? green.Lerp(p.Accent, 0.15f) : green.Lerp(p.GroundLow, 0.4f);
                b.Cylinder(new Vector3(0, -0.1f, 0), new Vector3(0, h * 0.35f, 0), 0.16f, 0.12f, Wood, 7);
                for (var k = 0; k < 3; k++)
                {
                    var y0 = h * (0.25f + 0.22f * k);
                    var r = h * (0.36f - 0.08f * k);
                    b.Lathe([new(0, y0), new(r, y0), new(r * 0.95f, y0 + 0.12f), new(r * 0.2f, y0 + h * 0.36f), new(0, y0 + h * 0.4f)], (_, i) => (k % 2 == 0 ? c : c.Lightened(0.08f)).Lightened(0.04f * i), 10);
                }

                if (theme is PlanetTheme.Crystal or PlanetTheme.Veiled) b.Ellipsoid(new Vector3(0, h * 1.02f, 0), new Vector3(0.12f, 0.12f, 0.12f), Glowing(p.Glow), 6, 4);
                break;
            }

            case DecorationKind.Tree:
            {
                var h = R(2.2f, 3.4f);
                var lean = R(-8, 8);
                var top = new Vector3(Mathf.Sin(Mathf.DegToRad(lean)) * h * 0.4f, h, 0);
                b.Tube([new(0, -0.1f, 0), new(0.05f, h * 0.4f, 0), top], s => Mathf.Lerp(0.2f, 0.12f, s), _ => Wood, 7);
                var c = theme switch
                {
                    PlanetTheme.Meadow => rng.Next(3) == 0 ? p.Accent.Lightened(0.15f) : green,
                    _ => green,
                };
                var r = R(0.9f, 1.25f);
                b.Ellipsoid(top + new Vector3(0, 0.3f, 0), new Vector3(r, r * 0.9f, r), c, 14, 9);
                b.Ellipsoid(top + new Vector3(r * 0.65f, -0.1f, 0.2f), new Vector3(r * 0.62f, r * 0.55f, r * 0.62f), c.Lightened(0.1f), 12, 7);
                b.Ellipsoid(top + new Vector3(-r * 0.6f, 0.0f, -0.25f), new Vector3(r * 0.58f, r * 0.52f, r * 0.58f), c.Darkened(0.06f), 12, 7);
                if (rng.Next(2) == 0)
                    for (var k = 0; k < 3; k++)
                    {
                        var a = R(0, Mathf.Tau);
                        b.Ellipsoid(top + new Vector3(Mathf.Sin(a) * r * 0.8f, R(-0.2f, 0.5f), Mathf.Cos(a) * r * 0.8f), new Vector3(0.12f, 0.12f, 0.12f), Glowing(p.Glow, 0.6f), 6, 4);
                    }

                break;
            }

            case DecorationKind.Rock:
            {
                var n = 1 + rng.Next(3);
                for (var k = 0; k < n; k++)
                {
                    var s = R(0.35f, 0.8f) * (k == 0 ? 1.3f : 0.8f);
                    var at = k == 0 ? Vector3.Zero : new Vector3(R(-0.8f, 0.8f), 0, R(-0.8f, 0.8f));
                    b.Push(T(at, new Vector3(R(-10, 10), R(0, 360), R(-10, 10))));
                    b.Ellipsoid(new Vector3(0, s * 0.35f, 0), new Vector3(s, s * 0.75f, s * 0.85f), Stone.Lerp(p.GroundLow, 0.3f).Darkened(R(0, 0.12f)), 10, 7);
                    b.Pop();
                }

                break;
            }

            case DecorationKind.Flower when theme == PlanetTheme.Chime:
            {
                var n = 2 + rng.Next(3);
                for (var k = 0; k < n; k++)
                {
                    var at = new Vector3(R(-0.35f, 0.35f), 0, R(-0.35f, 0.35f));
                    var h = R(0.45f, 0.8f);
                    b.Tube([at, at + new Vector3(0, h, 0), at + new Vector3(0.12f, h + 0.08f, 0)], _ => 0.022f, _ => Leaf.Darkened(0.1f), 5);
                    b.Lathe([new(0, 0.02f), new(0.05f, 0), new(0.08f, -0.08f), new(0.12f, -0.14f)], (_, _) => p.Accent.Lightened(0.1f * k), 10, origin: at + new Vector3(0.14f, h + 0.06f, 0));
                    b.Ellipsoid(at + new Vector3(0.14f, h - 0.06f, 0), new Vector3(0.035f, 0.035f, 0.035f), Glowing(p.Glow), 6, 4);
                }

                break;
            }

            case DecorationKind.Flower:
            {
                var n = 2 + rng.Next(3);
                for (var k = 0; k < n; k++)
                {
                    var at = new Vector3(R(-0.35f, 0.35f), 0, R(-0.35f, 0.35f));
                    var h = R(0.35f, 0.7f);
                    var c = k % 2 == 0 ? p.Accent : p.Accent.Lerp(Colors.White, 0.5f);
                    b.Tube([at, at + new Vector3(0.03f, h * 0.5f, 0), at + new Vector3(0, h, 0)], _ => 0.02f, _ => Leaf.Darkened(0.1f), 5);
                    b.Push(T(at + new Vector3(0, h, 0), new Vector3(-30, R(0, 360), 0)));
                    for (var q = 0; q < 6; q++)
                    {
                        var a = q * Mathf.Tau / 6;
                        b.Ellipsoid(new Vector3(Mathf.Sin(a) * 0.08f, 0, Mathf.Cos(a) * 0.08f), new Vector3(0.06f, 0.02f, 0.06f), c, 8, 4);
                    }

                    b.Ellipsoid(new Vector3(0, 0.02f, 0), new Vector3(0.045f, 0.03f, 0.045f), Glowing(p.Glow, 0.7f), 8, 4);
                    b.Pop();
                }

                break;
            }

            case DecorationKind.Grass:
            {
                var n = 5 + rng.Next(4);
                var c = p.GroundLow.Lerp(Leaf, 0.35f).Darkened(0.08f);
                for (var k = 0; k < n; k++)
                {
                    var at = new Vector3(R(-0.25f, 0.25f), -0.02f, R(-0.25f, 0.25f));
                    var h = R(0.25f, 0.55f);
                    b.Push(T(at, new Vector3(R(-25, 25), R(0, 360), R(-25, 25))));
                    b.Lathe([new(0.05f, 0), new(0.03f, h * 0.5f), new(0, h)], (_, i) => c.Lightened(0.12f * i), 3, smooth: false);
                    b.Pop();
                }

                break;
            }

            case DecorationKind.Mushroom:
            {
                var n = 1 + rng.Next(3);
                for (var k = 0; k < n; k++)
                {
                    var at = k == 0 ? Vector3.Zero : new Vector3(R(-0.5f, 0.5f), 0, R(-0.5f, 0.5f));
                    var s = k == 0 ? R(0.7f, 1.1f) : R(0.35f, 0.6f);
                    b.Push(T(at, new Vector3(R(-8, 8), 0, R(-8, 8)), s));
                    b.Lathe([new(0, 0), new(0.14f, 0), new(0.1f, 0.3f), new(0.12f, 0.5f), new(0, 0.5f)], (_, _) => Cream, 10);
                    b.Lathe([new(0, 0.42f), new(0.42f, 0.42f), new(0.45f, 0.48f), new(0.38f, 0.65f), new(0.2f, 0.78f), new(0, 0.82f)], (_, i) => i < 2 ? Glowing(p.Glow, 0.6f) : p.Accent, 14);
                    for (var q = 0; q < 5; q++)
                    {
                        var a = q * Mathf.Tau / 5 + 0.3f;
                        b.Ellipsoid(new Vector3(Mathf.Sin(a) * 0.27f, 0.69f, Mathf.Cos(a) * 0.27f), new Vector3(0.06f, 0.03f, 0.06f), Cream, 6, 3);
                    }

                    b.Pop();
                }

                break;
            }

            case DecorationKind.Crystal:
            {
                var n = 2 + rng.Next(3);
                var c = theme == PlanetTheme.Crystal ? p.Accent : p.Accent.Lerp(C(0.6f, 0.9f, 1f), 0.5f);
                for (var k = 0; k < n; k++)
                {
                    b.Push(T(new Vector3(R(-0.3f, 0.3f), -0.05f, R(-0.3f, 0.3f)), new Vector3(R(-25, 25), R(0, 360), R(-25, 25))));
                    b.Prism(5, R(0.08f, 0.16f), R(0.3f, 0.8f), 0.18f, (_, i) => Glowing(c.Lightened(0.06f * i), 0.12f + 0.07f * i));
                    b.Pop();
                }

                break;
            }

            default: // Lamp
            {
                if (theme == PlanetTheme.Library)
                {
                    b.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 2.1f, 0), 0.06f, 0.05f, Wood, 6);
                    b.Tube([new(0, 2.0f, 0), new(0.15f, 2.25f, 0), new(0.45f, 2.25f, 0)], _ => 0.035f, _ => Wood, 5);
                    var lc = p.Accent.Lerp(C(1f, 0.7f, 0.5f), 0.5f);
                    b.Cylinder(new Vector3(0.45f, 2.25f, 0), new Vector3(0.45f, 2.1f, 0), 0.01f, 0.01f, Ink, 4);
                    b.Lathe([new(0, 0.0f), new(0.14f, 0.02f), new(0.2f, 0.18f), new(0.2f, 0.32f), new(0.14f, 0.48f), new(0, 0.5f)], (_, i) => Glowing(i % 2 == 0 ? lc : lc.Lightened(0.15f), 0.75f), 10, origin: new Vector3(0.45f, 1.6f, 0));
                    b.Torus(T(new Vector3(0.45f, 1.6f, 0)), 0.14f, 0.02f, Ink, 10, 4);
                    b.Torus(T(new Vector3(0.45f, 2.1f, 0)), 0.14f, 0.02f, Ink, 10, 4);
                }
                else if (theme == PlanetTheme.Workshop)
                {
                    b.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 2.0f, 0), 0.07f, 0.05f, Brass.Darkened(0.2f), 8);
                    b.Lathe([new(0, 0), new(0.22f, 0), new(0.22f, 0.12f), new(0, 0.14f)], (_, _) => Brass.Darkened(0.3f), 10);
                    b.Lathe([new(0, 1.95f), new(0.14f, 1.95f), new(0.2f, 2.3f), new(0.2f, 2.3f), new(0.0f, 2.3f)], (_, i) => Glowing(p.Glow, 0.85f), 6, smooth: false);
                    b.Lathe([new(0, 2.28f), new(0.28f, 2.28f), new(0, 2.55f)], (_, _) => Brass, 6, smooth: false);
                }
                else
                {
                    b.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 1.8f, 0), 0.06f, 0.045f, Ink.Lightened(0.3f), 6);
                    b.Lathe([new(0, 0), new(0.2f, 0), new(0.18f, 0.12f), new(0, 0.14f)], (_, _) => p.Accent.Darkened(0.2f), 10);
                    b.Ellipsoid(new Vector3(0, 2.0f, 0), new Vector3(0.22f, 0.22f, 0.22f), Glowing(p.Glow), 12, 8);
                    b.Lathe([new(0, 2.22f), new(0.18f, 2.17f), new(0, 2.3f)], (_, _) => p.Accent.Darkened(0.1f), 10);
                }

                break;
            }
        }

        return Commit(b);
    }

    /// <summary>Triangle count of a mesh (all surfaces).</summary>
    public static int Triangles(Mesh mesh)
    {
        var n = 0;
        for (var s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            var idx = arrays[(int)Mesh.ArrayType.Index];
            n += idx.VariantType == Variant.Type.Nil || idx.AsInt32Array().Length == 0
                ? arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length / 3
                : idx.AsInt32Array().Length / 3;
        }

        return n;
    }
}

/// <summary>
/// Accumulates triangles (unindexed) with normals, linear vertex colours and UVs under a transform stack, then commits
/// them as one ArrayMesh surface. Every triangle is turned to face along its vertex normals (Godot's front faces wind
/// clockwise), so primitives never need to care about winding.
/// </summary>
internal sealed class MeshBuilder
{
    private readonly List<Vector3> _v = [];
    private readonly List<Vector3> _n = [];
    private readonly List<Color> _c = [];
    private readonly List<Vector2> _uv = [];
    private readonly Stack<Transform3D> _stack = new();
    private Transform3D _t = Transform3D.Identity;
    private Basis _nb = Basis.Identity;

    /// <summary>Triangles added since the last <see cref="Commit"/>.</summary>
    public int Triangles => _v.Count / 3;

    /// <summary>Normals that came out non-finite or zero and were replaced by the face normal (a diagnostic).</summary>
    public static int NonFiniteNormals { get; private set; }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>
    /// Composes <paramref name="t"/> under the current transform for everything added until the matching <see cref="Pop"/>.
    /// Normals use the inverse transpose of the basis, so they stay perpendicular under non-uniform scale (an ellipsoid).
    /// </summary>
    public void Push(Transform3D t)
    {
        _stack.Push(_t);
        _t *= t;
        _nb = _t.Basis.Inverse().Transposed();
    }

    /// <summary>Restores the transform before the last <see cref="Push"/>.</summary>
    public void Pop()
    {
        _t = _stack.Pop();
        _nb = _t.Basis.Inverse().Transposed();
    }

    /// <summary>
    /// One triangle in local coordinates (colours in sRGB, alpha = 1 − glow). Degenerate (squared edge cross product below
    /// 1e-14) or non-finite triangles are dropped; unusable normals are replaced by the face normal (counted in
    /// <see cref="NonFiniteNormals"/>); the winding is swapped when needed so the front face looks along the normals.
    /// </summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Color ca, Color cb, Color cc, Vector2 ua = default, Vector2 ub = default, Vector2 uc = default)
    {
        a = _t * a;
        b = _t * b;
        c = _t * c;
        var g = (b - a).Cross(c - a);
        if (!(g.LengthSquared() >= 1e-14f) || !Finite(a) || !Finite(b) || !Finite(c)) return; // degenerate or non-finite
        na = (_nb * na).Normalized();
        nb = (_nb * nb).Normalized();
        nc = (_nb * nc).Normalized();
        if (!Finite(na) || !Finite(nb) || !Finite(nc) || na.LengthSquared() < 0.5f || nb.LengthSquared() < 0.5f || nc.LengthSquared() < 0.5f)
        {
            na = nb = nc = -g.Normalized(); // a front face's normal (clockwise winding)
            NonFiniteNormals++;
        }
        if (g.Dot(na + nb + nc) > 0)
        {
            (b, c) = (c, b);
            (nb, nc) = (nc, nb);
            (cb, cc) = (cc, cb);
            (ub, uc) = (uc, ub);
        }

        _v.Add(a); _v.Add(b); _v.Add(c);
        _n.Add(na); _n.Add(nb); _n.Add(nc);
        _c.Add(PlanetArt.Linear(ca)); _c.Add(PlanetArt.Linear(cb)); _c.Add(PlanetArt.Linear(cc));
        _uv.Add(ua); _uv.Add(ub); _uv.Add(uc);
    }

    /// <summary>A flat triangle whose normal points along <paramref name="outward"/> (local).</summary>
    public void FlatTri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color col)
    {
        var g = (b - a).Cross(c - a);
        if (g.LengthSquared() < 1e-14f) return;
        var n = g.Normalized();
        if (n.Dot(outward) < 0) n = -n;
        Tri(a, b, c, n, n, n, col, col, col);
    }

    /// <summary>A quad centred at <paramref name="centre"/> spanning ±<paramref name="halfRight"/> (the viewer's right) and ±<paramref name="halfUp"/>, facing right × up, UV (0,0) at the viewer's top-left.</summary>
    public void Quad(Vector3 centre, Vector3 halfRight, Vector3 halfUp, Color col)
    {
        var n = halfRight.Cross(halfUp).Normalized(); // pass the viewer's right: for a face looking along −Z that is −X
        var tl = centre - halfRight + halfUp;
        var tr = centre + halfRight + halfUp;
        var bl = centre - halfRight - halfUp;
        var br = centre + halfRight - halfUp;
        Tri(tl, tr, br, n, n, n, col, col, col, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1));
        Tri(tl, br, bl, n, n, n, col, col, col, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1));
    }

    /// <summary>
    /// A surface of revolution about local Y of a (radius, y) profile. <paramref name="color"/> gets (segment, profile
    /// index). Consecutive duplicate profile points make a hard edge. <paramref name="radiusScale"/> can wobble the radius
    /// by (angle radians, y). <paramref name="inward"/> flips the normals (the inside of a bell).
    /// </summary>
    public void Lathe(IReadOnlyList<Vector2> prof, Func<int, int, Color> color, int segments, bool smooth = true, float startAngle = 0,
        Func<float, float, float>? radiusScale = null, bool inward = false, Vector3 origin = default, Basis? axis = null)
    {
        var pushed = origin != default || axis != null;
        if (pushed) Push(new Transform3D(axis ?? Basis.Identity, origin));
        var n = prof.Count;
        var n2 = new Vector2[n];
        // Normals point to the left of the direction of travel for a profile drawn upwards; a profile drawn downwards
        // (a bell, a cap) is flipped so its normals still face out.
        var downward = prof[n - 1].Y < prof[0].Y;
        for (var i = 0; i < n; i++)
        {
            var prev = i > 0 && prof[i - 1] != prof[i] ? prof[i - 1] : prof[i];
            var next = i < n - 1 && prof[i + 1] != prof[i] ? prof[i + 1] : prof[i];
            var t = next - prev;
            if (t.LengthSquared() < 1e-12f) t = new Vector2(0, 1);
            var nn = new Vector2(t.Y, -t.X).Normalized();
            n2[i] = inward ^ downward ? -nn : nn;
        }

        Vector3 P(int i, float a)
        {
            var r = prof[i].X * (radiusScale?.Invoke(a, prof[i].Y) ?? 1f);
            return new Vector3(Mathf.Cos(a) * r, prof[i].Y, Mathf.Sin(a) * r);
        }

        Vector3 N(int i, float a) => new(Mathf.Cos(a) * n2[i].X, n2[i].Y, Mathf.Sin(a) * n2[i].X);
        for (var j = 0; j < segments; j++)
        {
            var a0 = startAngle + j * Mathf.Tau / segments;
            var a1 = startAngle + (j + 1) * Mathf.Tau / segments;
            var am = (a0 + a1) / 2;
            for (var i = 0; i < n - 1; i++)
            {
                if (prof[i] == prof[i + 1]) continue;
                var c0 = color(j, i);
                var c1 = color(j, i + 1);
                Vector3 p00 = P(i, a0), p01 = P(i, a1), p10 = P(i + 1, a0), p11 = P(i + 1, a1);
                if (smooth)
                {
                    Tri(p00, p10, p11, N(i, a0), N(i + 1, a0), N(i + 1, a1), c0, c1, c1);
                    Tri(p00, p11, p01, N(i, a0), N(i + 1, a1), N(i, a1), c0, c1, c0);
                }
                else
                {
                    var hint = N(i, am) + N(i + 1, am);
                    FlatTri(p00, p10, p11, hint, c0);
                    FlatTri(p00, p11, p01, hint, c0);
                }
            }
        }

        if (pushed) Pop();
    }

    /// <summary>An ellipsoid at <paramref name="c"/> with radii <paramref name="r"/>.</summary>
    public void Ellipsoid(Vector3 c, Vector3 r, Color col, int segments = 14, int rings = 9)
    {
        var prof = new Vector2[rings + 1];
        for (var i = 0; i <= rings; i++)
        {
            var t = Mathf.Pi * i / rings;
            prof[i] = new Vector2(Mathf.Sin(t), -Mathf.Cos(t));
        }

        Push(new Transform3D(Basis.FromScale(r), c));
        Lathe(prof, (_, _) => col, segments);
        Pop();
    }

    /// <summary>A basis whose Y is along <paramref name="y"/> (any X and Z; another reference axis when y is near vertical, where the cross product with up vanishes).</summary>
    private static Basis AlignY(Vector3 y)
    {
        y = y.Normalized();
        var x = Mathf.Abs(y.Dot(Vector3.Up)) < 0.95f ? Vector3.Up.Cross(y).Normalized() : Vector3.Right.Cross(y).Normalized();
        var z = x.Cross(y);
        return new Basis(x, y, z);
    }

    /// <summary>A capped frustum from <paramref name="a"/> (radius <paramref name="ra"/>) to <paramref name="b"/> (radius <paramref name="rb"/>).</summary>
    public void Cylinder(Vector3 a, Vector3 b, float ra, float rb, Color col, int segments = 10, bool caps = true)
    {
        var len = (b - a).Length();
        if (len < 1e-5f) return;
        Vector2[] prof = caps
            ? [new(0, 0), new(ra, 0), new(ra, 0), new(rb, len), new(rb, len), new(0, len)]
            : [new(ra, 0), new(rb, len)];
        Push(new Transform3D(AlignY(b - a), a));
        Lathe(prof, (_, _) => col, segments);
        Pop();
    }

    /// <summary>A tube along a polyline (parallel-transport frames), radius and colour by arc fraction 0..1.</summary>
    public void Tube(IReadOnlyList<Vector3> path, Func<float, float> radius, Func<float, Color> color, int sides = 8, bool capEnds = true)
    {
        if (path.Count < 2) return;
        var total = 0f;
        var s = new float[path.Count];
        for (var i = 1; i < path.Count; i++) s[i] = total += (path[i] - path[i - 1]).Length();
        if (total < 1e-5f) return;
        var tangents = new Vector3[path.Count];
        for (var i = 0; i < path.Count; i++)
        {
            var t = path[Math.Min(i + 1, path.Count - 1)] - path[Math.Max(i - 1, 0)];
            tangents[i] = t.Normalized();
        }

        var normal = Mathf.Abs(tangents[0].Dot(Vector3.Up)) < 0.9f ? tangents[0].Cross(Vector3.Up).Normalized() : tangents[0].Cross(Vector3.Right).Normalized();
        var rings = new Vector3[path.Count][];
        var ringN = new Vector3[path.Count][];
        for (var i = 0; i < path.Count; i++)
        {
            if (i > 0)
            {
                var axis = tangents[i - 1].Cross(tangents[i]);
                if (axis.LengthSquared() > 1e-10f)
                {
                    var ang = Mathf.Acos(Mathf.Clamp(tangents[i - 1].Dot(tangents[i]), -1, 1));
                    normal = normal.Rotated(axis.Normalized(), ang);
                }
            }

            var binormal = tangents[i].Cross(normal).Normalized();
            var r = radius(s[i] / total);
            rings[i] = new Vector3[sides];
            ringN[i] = new Vector3[sides];
            for (var k = 0; k < sides; k++)
            {
                var a = k * Mathf.Tau / sides;
                var d = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                rings[i][k] = path[i] + d * r;
                ringN[i][k] = d;
            }
        }

        for (var i = 0; i < path.Count - 1; i++)
        {
            var c0 = color(s[i] / total);
            var c1 = color(s[i + 1] / total);
            for (var k = 0; k < sides; k++)
            {
                var k1 = (k + 1) % sides;
                Tri(rings[i][k], rings[i + 1][k], rings[i + 1][k1], ringN[i][k], ringN[i + 1][k], ringN[i + 1][k1], c0, c1, c1);
                Tri(rings[i][k], rings[i + 1][k1], rings[i][k1], ringN[i][k], ringN[i + 1][k1], ringN[i][k1], c0, c1, c0);
            }
        }

        if (!capEnds) return;
        foreach (var (i, sign) in new[] { (0, -1f), (path.Count - 1, 1f) })
        {
            var n = tangents[i] * sign;
            var col = color(s[i] / total);
            for (var k = 0; k < sides; k++) Tri(path[i], rings[i][k], rings[i][(k + 1) % sides], n, n, n, col, col, col);
        }
    }

    /// <summary>A torus about local Y under <paramref name="t"/>.</summary>
    public void Torus(Transform3D t, float major, float minor, Color col, int segments = 24, int sides = 6) =>
        TorusArc(t, major, minor, 0, Mathf.Tau, col, segments, sides);

    /// <summary>Part of a torus about local Y from angle <paramref name="a0"/> to <paramref name="a1"/> (radians, from +Z towards +X).</summary>
    public void TorusArc(Transform3D t, float major, float minor, float a0, float a1, Color col, int segments, int sides)
    {
        Push(t);
        for (var j = 0; j < segments; j++)
        {
            var u0 = Mathf.Lerp(a0, a1, j / (float)segments);
            var u1 = Mathf.Lerp(a0, a1, (j + 1) / (float)segments);
            for (var k = 0; k < sides; k++)
            {
                var v0 = k * Mathf.Tau / sides;
                var v1 = (k + 1) * Mathf.Tau / sides;
                (Vector3 P, Vector3 N) At(float u, float v)
                {
                    var dir = new Vector3(Mathf.Sin(u), 0, Mathf.Cos(u));
                    var n = dir * Mathf.Cos(v) + Vector3.Up * Mathf.Sin(v);
                    return (dir * major + n * minor, n);
                }

                var (p00, n00) = At(u0, v0);
                var (p10, n10) = At(u1, v0);
                var (p01, n01) = At(u0, v1);
                var (p11, n11) = At(u1, v1);
                Tri(p00, p10, p11, n00, n10, n11, col, col, col);
                Tri(p00, p11, p01, n00, n11, n01, col, col, col);
            }
        }

        Pop();
    }

    /// <summary>A flat-shaded box.</summary>
    public void Box(Vector3 c, Vector3 size, Color col)
    {
        var h = size / 2;
        Vector3[] axes = [Vector3.Right, Vector3.Up, Vector3.Back];
        for (var k = 0; k < 3; k++)
        {
            var u = axes[(k + 1) % 3] * h[(k + 1) % 3];
            var v = axes[(k + 2) % 3] * h[(k + 2) % 3];
            foreach (var sign in new[] { -1f, 1f })
            {
                var n = axes[k] * sign;
                var fc = c + n * h[k];
                FlatTri(fc - u - v, fc + u - v, fc + u + v, n, col);
                FlatTri(fc - u - v, fc + u + v, fc - u + v, n, col);
            }
        }
    }

    /// <summary>A rounded box (superellipsoid; <paramref name="roundness"/> 0.1 = nearly sharp, 1 = an ellipsoid), optional Euler rotation in degrees.</summary>
    public void RoundBox(Vector3 c, Vector3 size, float roundness, Color col, Vector3 eulerDegrees = default, int segments = 12, int rings = 8)
    {
        var e = Mathf.Clamp(roundness, 0.05f, 1f);
        var r = size / 2;
        float Sp(float x, float p) => Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), p);
        (Vector3, Vector3) At(float eta, float w)
        {
            float ce = Mathf.Cos(eta), se = Mathf.Sin(eta), cw = Mathf.Cos(w), sw = Mathf.Sin(w);
            var p = new Vector3(r.X * Sp(ce, e) * Sp(cw, e), r.Y * Sp(se, e), r.Z * Sp(ce, e) * Sp(sw, e));
            var n = new Vector3(Sp(ce, 2 - e) * Sp(cw, 2 - e) / r.X, Sp(se, 2 - e) / r.Y, Sp(ce, 2 - e) * Sp(sw, 2 - e) / r.Z);
            if (n.LengthSquared() < 1e-12f) n = new Vector3(0, Mathf.Sign(se), 0);
            return (p, n.Normalized());
        }

        Push(new Transform3D(Basis.FromEuler(eulerDegrees * (Mathf.Pi / 180f)), c));
        for (var i = 0; i < rings; i++)
        {
            var e0 = -Mathf.Pi / 2 + Mathf.Pi * i / rings;
            var e1 = -Mathf.Pi / 2 + Mathf.Pi * (i + 1) / rings;
            for (var j = 0; j < segments; j++)
            {
                var w0 = -Mathf.Pi + Mathf.Tau * j / segments;
                var w1 = -Mathf.Pi + Mathf.Tau * (j + 1) / segments;
                var (p00, n00) = At(e0, w0);
                var (p01, n01) = At(e0, w1);
                var (p10, n10) = At(e1, w0);
                var (p11, n11) = At(e1, w1);
                Tri(p00, p10, p11, n00, n10, n11, col, col, col);
                Tri(p00, p11, p01, n00, n11, n01, col, col, col);
            }
        }

        Pop();
    }

    /// <summary>
    /// A flat-shaded prism from a star-shaped outline in the XY plane (or YZ with <paramref name="axisX"/>), extruded by
    /// <paramref name="depth"/> about z = 0 (x = 0), at <paramref name="at"/>.
    /// </summary>
    public void Extrude(IReadOnlyList<Vector2> outline, float depth, Color col, Vector3 at, bool axisX = false, Color? side = null)
    {
        Vector3 P(Vector2 q, float d) => at + (axisX ? new Vector3(d, q.Y, q.X) : new Vector3(q.X, q.Y, d));
        var normal = axisX ? Vector3.Right : Vector3.Back;
        var centre = Vector2.Zero;
        foreach (var q in outline) centre += q;
        centre /= outline.Count;
        var h = depth / 2;
        for (var i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var c = outline[(i + 1) % outline.Count];
            FlatTri(P(centre, h), P(a, h), P(c, h), normal, col);
            FlatTri(P(centre, -h), P(a, -h), P(c, -h), -normal, col);
            var edge = c - a;
            var outward2 = new Vector2(edge.Y, -edge.X);
            if (outward2.Dot((a + c) / 2 - centre) < 0) outward2 = -outward2;
            var outward = axisX ? new Vector3(0, outward2.Y, outward2.X) : new Vector3(outward2.X, outward2.Y, 0);
            FlatTri(P(a, -h), P(c, -h), P(c, h), outward, side ?? col);
            FlatTri(P(a, -h), P(c, h), P(a, h), outward, side ?? col);
        }
    }

    /// <summary>A gear in the XY plane (axis Z) at <paramref name="at"/>: <paramref name="teeth"/> teeth between the root and tip radii.</summary>
    public void Gear(float tipRadius, float rootRadius, float depth, int teeth, Color face, Color side, Vector3 at = default)
    {
        var outline = new List<Vector2>();
        for (var i = 0; i < teeth; i++)
        {
            var w = Mathf.Tau / teeth;
            var a = i * w;
            // One tooth per pitch w: rising flank over the first 12 %, the flat tip to 42 %, falling to the root by 54 %; the
            // rest of the pitch is the gap.
            foreach (var (f, r) in new[] { (0f, rootRadius), (0.12f, tipRadius), (0.42f, tipRadius), (0.54f, rootRadius) })
                outline.Add(new Vector2(Mathf.Cos(a + f * w), Mathf.Sin(a + f * w)) * r);
        }

        Extrude(outline, depth, face, at, side: side);
    }

    /// <summary>A pointed crystal prism of <paramref name="sides"/> sides standing on y = 0 (flat-shaded); colour by (segment, profile index 0–4).</summary>
    public void Prism(int sides, float radius, float height, float tip, Func<int, int, Color> color) =>
        Lathe([new(0, 0), new(radius * 0.8f, 0), new(radius, height * 0.12f), new(radius, height), new(0, height + tip)], color, sides, smooth: false);

    /// <summary>Adds everything accumulated as one new triangle surface of <paramref name="mesh"/> with <paramref name="material"/>, then clears the builder (nothing is added when empty).</summary>
    public void Commit(ArrayMesh mesh, Material material)
    {
        if (_v.Count == 0) return;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _n.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _c.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        _v.Clear();
        _n.Clear();
        _c.Clear();
        _uv.Clear();
    }
}
