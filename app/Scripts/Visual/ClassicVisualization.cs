using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using Godot;
using GEnvironment = Godot.Environment;

namespace FileScape.App.Visual;

/// <summary>
/// The FSN-inspired view (VIS-01): steel pedestals on an indigo grid landscape, file blocks coloured by age
/// using FSN's seven bands, and luminous wires from each directory to its children. Dusk sky, distance fog, and
/// soft shadows give depth without hiding structure.
/// </summary>
public sealed class ClassicVisualization : Visualization
{
    private static readonly Color PedestalColor = new(0.42f, 0.5f, 0.64f);
    private static readonly Color RootPedestalColor = new(0.62f, 0.68f, 0.82f);
    private static readonly Color WireColor = new(0.75f, 0.88f, 1f);

    /// <summary>A wire's cross-section, layout units: 0.08 wide, 0.05 tall (thin beside the pedestals; rationale for the exact size not recorded).</summary>
    private const float WireWidth = 0.08f, WireHeight = 0.05f;

    private readonly BoxMesh _box = new() { Size = Vector3.One };
    private readonly ShaderMaterial _blockMaterial;
    private readonly ShaderMaterial _pedestalMaterial;
    private readonly StandardMaterial3D _wireMaterial;
    private readonly StandardMaterial3D _markerMaterial;

    public ClassicVisualization()
    {
        var shader = GD.Load<Shader>("res://Shaders/block.gdshader");
        _blockMaterial = new ShaderMaterial { Shader = shader };
        _pedestalMaterial = new ShaderMaterial { Shader = shader };
        _pedestalMaterial.SetShaderParameter("edge_color", new Color(0.75f, 0.85f, 1f));
        _pedestalMaterial.SetShaderParameter("edge_strength", 0.35f);
        _pedestalMaterial.SetShaderParameter("edge_width", 0.06f);
        _pedestalMaterial.SetShaderParameter("metallic_value", 0.45f);
        _pedestalMaterial.SetShaderParameter("roughness_value", 0.32f);
        _pedestalMaterial.SetShaderParameter("emission_strength", 0.03f);
        _wireMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(1.6f, 1.7f, 2.0f), // above 1: unshaded and past the glow threshold, so the wires bloom
        };
        _markerMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(1.8f, 1.8f, 1.8f),
        };
    }

    public override string Name => "Classic FSN";
    public override LayoutParams LayoutParams => LayoutParams.Classic;
    public override string ColorMeaning => "Colour = age since last modified (FSN's seven bands)";
    public override Color SelectionColor => new(1f, 0.93f, 0.55f);
    public override Color HoverColor => new(0.6f, 0.85f, 1f);

    public override IReadOnlyList<(Color Color, string Label)> Legend =>
        Palette.Age.Select((c, i) => (c, AgeBands.Labels[i])).ToList();

    public override Color ColorFor(FsNode node, FsTree tree) => node.Kind switch
    {
        NodeKind.Directory => node.IsRoot ? RootPedestalColor : PedestalColor,
        NodeKind.Link => Palette.LinkCyan * 0.6f,
        _ => Palette.Age[AgeBands.Band(node.ModifiedUtc, tree.Source.AgeReferenceUtc)],
    };

    /// <summary>
    /// The dusk look: an indigo sky, distance fog with aerial perspective, a cool sun with soft shadows (wider on High),
    /// and the grid ground. Distances here are layout units, not metres (docs/units.md). The values are an art direction
    /// tuned by eye (rationale not recorded).
    /// </summary>
    public override void ConfigureEnvironment(WorldEnvironment world, DirectionalLight3D sun, MeshInstance3D ground, Quality quality)
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.015f, 0.02f, 0.07f),
            SkyHorizonColor = new Color(0.16f, 0.2f, 0.42f),
            SkyCurve = 0.12f,
            GroundBottomColor = new Color(0.01f, 0.012f, 0.03f),
            GroundHorizonColor = new Color(0.12f, 0.15f, 0.32f),
            SunAngleMax = 20f,
            SkyEnergyMultiplier = 1.0f,
        };
        var high = quality == Quality.High;
        world.Environment = new GEnvironment
        {
            BackgroundMode = GEnvironment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = GEnvironment.AmbientSource.Sky,
            AmbientLightEnergy = 0.9f,
            TonemapMode = GEnvironment.ToneMapper.Agx,
            TonemapExposure = 1.05f,
            GlowEnabled = true,
            GlowIntensity = 0.55f,
            GlowBloom = 0.05f,
            GlowBlendMode = GEnvironment.GlowBlendModeEnum.Softlight,
            FogEnabled = true,
            FogLightColor = new Color(0.12f, 0.16f, 0.34f),
            FogDensity = 0.0022f,
            FogSkyAffect = 0.35f,
            FogAerialPerspective = 0.3f,
            SsaoEnabled = high,
            SsaoRadius = 1.4f,
            SsaoIntensity = 1.6f,
            SsrEnabled = false,
            VolumetricFogEnabled = false,
        };
        // The sun is shared with Places, which changes these; restore the map defaults explicitly.
        sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightAndSky;
        sun.LightSpecular = 1f;
        sun.LightColor = new Color(0.86f, 0.9f, 1f);
        sun.LightEnergy = 1.1f;
        sun.RotationDegrees = new Vector3(-48, -32, 0);
        sun.ShadowEnabled = true;
        sun.DirectionalShadowMaxDistance = high ? 260 : 140;
        ground.MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/ground_grid.gdshader") };
    }

    /// <summary>Directories as pedestals, files as blocks (one box each, from the layout), state markers, and a wire along every connector.</summary>
    protected override int Build(FsTree tree, LayoutResult layout, List<int> order)
    {
        var pedestals = new InstanceBatch();
        var blocks = new InstanceBatch();
        var wires = new InstanceBatch();
        var markers = new InstanceBatch();
        foreach (var id in order)
        {
            var n = tree[id];
            var box = layout.NodeBoxes[id];
            var style = StyleBits.For(n);
            if (n.IsDirectory)
            {
                pedestals.AddBox(Gd(box.Center), Gd(box.Size), ColorFor(n, tree), style);
            }
            else
            {
                blocks.AddBox(Gd(box.Center), Gd(box.Size), ColorFor(n, tree), style);
            }

            if (MarkerColor(n) is { } mc)
            {
                AddMarker(markers, box, mc);
            }
        }

        foreach (var c in layout.Connectors)
        {
            for (var i = 1; i < c.Points.Length; i++)
            {
                wires.AddSegment(Gd(c.Points[i - 1]), Gd(c.Points[i]), WireWidth, WireHeight, WireColor, 0);
            }
        }

        Emit("pedestals", pedestals, _box, _pedestalMaterial);
        Emit("blocks", blocks, _box, _blockMaterial);
        Emit("wires", wires, _box, _wireMaterial, castShadows: false);
        Emit("markers", markers, _box, _markerMaterial, castShadows: false);
        return pedestals.Count + blocks.Count + wires.Count + markers.Count;
    }
}
