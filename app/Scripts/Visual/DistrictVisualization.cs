using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using Godot;
using GEnvironment = Godot.Environment;

namespace FileScape.App.Visual;

/// <summary>
/// The futuristic district (VIS-02). Directories become low plazas with glowing kerbs, and files become glass towers
/// whose lit windows take the file-type colour. Parent-child links are streets at ground level with slow pulses
/// running from parent to child. The ground is wet asphalt under a night sky with volumetric haze (High quality).
/// The mapping is deliberately different from Classic: tower height is log(size) at 2.4× scale, and colour is file
/// type rather than age.
/// </summary>
public sealed class DistrictVisualization : Visualization
{
    private static readonly Color PlazaColor = new(0.05f, 0.055f, 0.08f);
    private static readonly Color StreetColor = new(0.35f, 0.85f, 1f);

    private readonly BoxMesh _box = new() { Size = Vector3.One };
    private readonly ShaderMaterial _towerMaterial;
    private readonly ShaderMaterial _plazaMaterial;
    private readonly ShaderMaterial _streetMaterial;
    private readonly StandardMaterial3D _markerMaterial;

    public DistrictVisualization()
    {
        _towerMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/tower.gdshader") };
        _plazaMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/block.gdshader") };
        _plazaMaterial.SetShaderParameter("edge_color", new Color(0.55f, 0.35f, 1f));
        _plazaMaterial.SetShaderParameter("edge_strength", 1.0f);
        _plazaMaterial.SetShaderParameter("edge_width", 0.12f);
        _plazaMaterial.SetShaderParameter("emission_strength", 0.0f);
        _plazaMaterial.SetShaderParameter("roughness_value", 0.25f);
        _plazaMaterial.SetShaderParameter("metallic_value", 0.3f);
        _plazaMaterial.SetShaderParameter("top_lift", 0.0f);
        _streetMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/street.gdshader") };
        _markerMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(2.2f, 2.2f, 2.2f),
        };
    }

    public override string Name => "District";
    public override LayoutParams LayoutParams => LayoutParams.District;
    public override string ColorMeaning => "Window light = file type (from extension)";
    public override Color SelectionColor => new(0.3f, 1f, 0.85f);
    public override Color HoverColor => new(0.9f, 0.55f, 1f);

    public override IReadOnlyList<(Color Color, string Label)> Legend =>
        Palette.Category.Select(kv => (kv.Value, kv.Key.ToString())).ToList();

    public override Color ColorFor(FsNode node, FsTree tree) => node.Kind switch
    {
        NodeKind.Directory => PlazaColor,
        NodeKind.Link => Palette.LinkCyan,
        _ => Palette.Category[FileCategories.Of(node.Name)],
    };

    public override void ConfigureEnvironment(WorldEnvironment world, DirectionalLight3D sun, MeshInstance3D ground, Quality quality)
    {
        var high = quality == Quality.High;
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.004f, 0.004f, 0.012f),
            SkyHorizonColor = new Color(0.16f, 0.05f, 0.22f),
            SkyCurve = 0.08f,
            GroundBottomColor = new Color(0.0f, 0.0f, 0.0f),
            GroundHorizonColor = new Color(0.1f, 0.03f, 0.14f),
            SunAngleMax = 5f,
            SkyEnergyMultiplier = 0.9f,
        };
        world.Environment = new GEnvironment
        {
            BackgroundMode = GEnvironment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = GEnvironment.AmbientSource.Color,
            AmbientLightColor = new Color(0.08f, 0.07f, 0.16f),
            AmbientLightEnergy = 0.6f,
            TonemapMode = GEnvironment.ToneMapper.Agx,
            TonemapExposure = 1.15f,
            GlowEnabled = true,
            GlowIntensity = 0.9f,
            GlowStrength = 1.05f,
            GlowBloom = 0.12f,
            GlowHdrThreshold = 0.9f,
            GlowBlendMode = GEnvironment.GlowBlendModeEnum.Screen,
            FogEnabled = !high,
            FogLightColor = new Color(0.1f, 0.05f, 0.16f),
            FogDensity = 0.004f,
            FogSkyAffect = 0.2f,
            VolumetricFogEnabled = high,
            VolumetricFogDensity = 0.012f,
            VolumetricFogAlbedo = new Color(0.55f, 0.5f, 0.8f),
            VolumetricFogEmission = new Color(0.04f, 0.02f, 0.07f),
            VolumetricFogEmissionEnergy = 1.0f,
            VolumetricFogLength = 220f,
            VolumetricFogGIInject = 0.6f,
            SsrEnabled = high,
            SsrMaxSteps = 48,
            SsrFadeIn = 0.15f,
            SsrFadeOut = 2.0f,
            SsaoEnabled = high,
            SsaoRadius = 1.2f,
            SsaoIntensity = 1.2f,
        };
        // VIS-11: the sun's specular highlight on near-mirror puddles switched on and off as the camera moved
        // (measured: blob-scale flashes halve with specular 0 or puddle roughness 0.25). Keep a faint sheen and
        // slightly rougher puddles; reflections of the lit towers (SSR) are unaffected.
        sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightAndSky;
        sun.LightSpecular = 0.1f;
        sun.LightColor = new Color(0.55f, 0.6f, 1f);
        sun.LightEnergy = 0.25f;
        sun.RotationDegrees = new Vector3(-35, 140, 0);
        sun.ShadowEnabled = high;
        sun.DirectionalShadowMaxDistance = 220;
        var wet = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/ground_wet.gdshader") };
        wet.SetShaderParameter("puddle_roughness", 0.12f);
        ground.MaterialOverride = wet;
    }

    protected override int Build(FsTree tree, LayoutResult layout, List<int> order)
    {
        var plazas = new InstanceBatch();
        var towers = new InstanceBatch();
        var streets = new InstanceBatch();
        var markers = new InstanceBatch();
        foreach (var id in order)
        {
            var n = tree[id];
            var box = layout.NodeBoxes[id];
            var style = StyleBits.For(n);
            if (n.IsDirectory)
            {
                plazas.AddBox(Gd(box.Center), Gd(box.Size), PlazaColor, style);
            }
            else
            {
                towers.AddBox(Gd(box.Center), Gd(box.Size), ColorFor(n, tree), style);
            }

            if (MarkerColor(n) is { } mc)
            {
                AddMarker(markers, box, mc);
            }
        }

        foreach (var c in layout.Connectors)
        {
            var along = 0f;
            for (var i = 1; i < c.Points.Length; i++)
            {
                var a = Gd(c.Points[i - 1]);
                var b = Gd(c.Points[i]);
                streets.AddSegment(a, b, 0.7f, 0.03f, StreetColor, along);
                along += a.DistanceTo(b);
            }
        }

        Emit("plazas", plazas, _box, _plazaMaterial);
        Emit("towers", towers, _box, _towerMaterial);
        Emit("streets", streets, _box, _streetMaterial, castShadows: false);
        Emit("markers", markers, _box, _markerMaterial, castShadows: false);
        return plazas.Count + towers.Count + streets.Count + markers.Count;
    }
}
