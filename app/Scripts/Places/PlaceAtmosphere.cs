using FileScape.App.Visual;
using Godot;
using GEnvironment = Godot.Environment;

namespace FileScape.App.Places;

/// <summary>
/// The Places atmosphere (PLACE-14): a deep blue-violet night, haze that lets vistas fade into the distance, wet
/// ground with reflections, and glow on emissive edges. It deliberately draws no sun disc and keeps the moonlight's
/// specular low, because near-mirror surfaces lit by a strong light produced on/off highlights during motion
/// (VIS-11 investigation, docs/implementation-status.md).
/// </summary>
public static class PlaceAtmosphere
{
    // ---- haze on the ground and high up (SetAerial blends between them). Densities are per metre: Godot's fog keeps
    // about e^(−density·distance) of what lies behind it.
    /// <summary>Volumetric fog on the ground (High quality): was 0.011, thinned so the far horizon (≈ 100–140 m) shows as haze, not nothing.</summary>
    private const float GroundVolumetricFogPerMetre = 0.0078f;

    /// <summary>Depth fog on the ground (Low quality; HORIZON-03): thinner than before (0.006), so the silhouettes beyond the vistas read as shapes.</summary>
    private const float GroundFogPerMetre = 0.0045f;

    /// <summary>How far the volumetric fog is computed on the ground, metres (beyond it there is none).</summary>
    private const float GroundVolumetricFogLengthMetres = 240f;

    /// <summary>High up: ≈ 70 % of a place 400 m away still shows through the volumetric fog, and the fog reaches 900 m.</summary>
    private const float AerialVolumetricFogPerMetre = 0.0009f, AerialFogPerMetre = 0.0005f, AerialVolumetricFogLengthMetres = 900f;

    /// <summary>Above this climb (aerial blend), High quality drops screen-space reflections and occlusion.</summary>
    private const float AerialEffectsOffBlend = 0.5f;

    /// <summary>
    /// Sets up the night: sky, ambient light, tone mapping, glow, fog (depth fog on Low, volumetric on High), the moonlight
    /// and the wet ground material. Called when Places becomes the experience and when the quality changes.
    /// The colours and effect strengths are an art direction tuned by eye (PLACE-14); only the fog has a recorded history.
    /// </summary>
    public static void Configure(WorldEnvironment world, DirectionalLight3D sun, MeshInstance3D ground, Quality quality)
    {
        var high = quality == Quality.High;
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.006f, 0.008f, 0.025f),
            SkyHorizonColor = new Color(0.1f, 0.07f, 0.24f),
            SkyCurve = 0.11f,
            GroundBottomColor = new Color(0, 0, 0),
            GroundHorizonColor = new Color(0.07f, 0.05f, 0.16f),
            SunAngleMax = 0.01f,
            SkyEnergyMultiplier = 1.0f,
        };
        world.Environment = new GEnvironment
        {
            BackgroundMode = GEnvironment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = GEnvironment.AmbientSource.Color,
            AmbientLightColor = new Color(0.09f, 0.09f, 0.2f),
            AmbientLightEnergy = 0.7f,
            ReflectedLightSource = GEnvironment.ReflectionSource.Sky,
            TonemapMode = GEnvironment.ToneMapper.Agx,
            TonemapExposure = 1.2f,
            GlowEnabled = true,
            GlowIntensity = 0.85f,
            GlowStrength = 1.0f,
            GlowBloom = 0.08f,
            GlowHdrThreshold = 1.0f,
            GlowBlendMode = GEnvironment.GlowBlendModeEnum.Screen,
            FogEnabled = !high,
            FogLightColor = new Color(0.08f, 0.06f, 0.16f),
            FogDensity = GroundFogPerMetre,
            FogSkyAffect = 0.3f,
            VolumetricFogEnabled = high,
            VolumetricFogDensity = GroundVolumetricFogPerMetre,
            VolumetricFogAlbedo = new Color(0.5f, 0.5f, 0.78f),
            VolumetricFogEmission = new Color(0.035f, 0.025f, 0.07f),
            VolumetricFogEmissionEnergy = 1.0f,
            VolumetricFogLength = GroundVolumetricFogLengthMetres,
            VolumetricFogGIInject = 0.5f,
            SsrEnabled = high,
            SsrMaxSteps = 56,
            SsrFadeIn = 0.15f,
            SsrFadeOut = 2.0f,
            SsaoEnabled = high,
            SsaoRadius = 1.2f,
            SsaoIntensity = 1.1f,
        };
        sun.LightColor = new Color(0.55f, 0.62f, 1f);
        sun.LightEnergy = 0.22f;
        sun.LightSpecular = 0.1f;
        sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly;
        sun.RotationDegrees = new Vector3(-40, 150, 0); // the moon 40° up (rationale for its bearing not recorded)
        sun.ShadowEnabled = high;
        sun.DirectionalShadowMaxDistance = 160; // metres from the camera: no shadows beyond (rationale for 160 not recorded)
        var groundMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/ground_wet.gdshader") };
        groundMat.SetShaderParameter("puddle_roughness", 0.12f);
        ground.MaterialOverride = groundMat;
    }

    /// <summary>
    /// Thins the haze as you climb (AERIAL-01): at eye level it fades the vistas into the distance
    /// (<see cref="GroundVolumetricFogPerMetre"/>: ≈ 15 % left after 240 m; it was 0.011/m, ≈ 7 %, when this was written),
    /// which would hide the aerial world's places hundreds of metres away. <paramref name="blend"/> 0 = on the ground,
    /// 1 = high up (<see cref="AerialVolumetricFogPerMetre"/>: about 70 % of a place 400 m away still shows). Each value
    /// is a straight lerp between the two; Main calls it each frame while the aerial world is shown, and with 0 on leaving it.
    /// </summary>
    public static void SetAerial(WorldEnvironment world, float blend, Quality quality)
    {
        var env = world.Environment;
        if (env == null) return;
        var b = Mathf.Clamp(blend, 0, 1);
        // High up, screen-space reflections and occlusion add little and cost GPU time over the wide view (measured).
        var high = quality == Quality.High;
        env.SsrEnabled = high && b < AerialEffectsOffBlend;
        env.SsaoEnabled = high && b < AerialEffectsOffBlend;
        env.VolumetricFogDensity = Mathf.Lerp(GroundVolumetricFogPerMetre, AerialVolumetricFogPerMetre, b);
        env.FogDensity = Mathf.Lerp(GroundFogPerMetre, AerialFogPerMetre, b);
        env.VolumetricFogLength = Mathf.Lerp(GroundVolumetricFogLengthMetres, AerialVolumetricFogLengthMetres, b);
    }
}
