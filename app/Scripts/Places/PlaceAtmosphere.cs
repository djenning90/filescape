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
            FogDensity = 0.006f,
            FogSkyAffect = 0.3f,
            VolumetricFogEnabled = high,
            VolumetricFogDensity = 0.011f,
            VolumetricFogAlbedo = new Color(0.5f, 0.5f, 0.78f),
            VolumetricFogEmission = new Color(0.035f, 0.025f, 0.07f),
            VolumetricFogEmissionEnergy = 1.0f,
            VolumetricFogLength = 240f,
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
        sun.RotationDegrees = new Vector3(-40, 150, 0);
        sun.ShadowEnabled = high;
        sun.DirectionalShadowMaxDistance = 160;
        var groundMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/ground_wet.gdshader") };
        groundMat.SetShaderParameter("puddle_roughness", 0.12f);
        ground.MaterialOverride = groundMat;
    }

    /// <summary>
    /// Thins the haze as you climb (AERIAL-01): at eye level it fades the vistas into the distance (0.011/m volumetric,
    /// ≈ 7 % left after 240 m), which would hide the aerial world's places hundreds of metres away. <paramref name="blend"/>
    /// 0 = on the ground, 1 = high up (≈ 0.0009/m: about 70 % of a place 400 m away still shows).
    /// </summary>
    public static void SetAerial(WorldEnvironment world, float blend, Quality quality)
    {
        var env = world.Environment;
        if (env == null) return;
        var b = Mathf.Clamp(blend, 0, 1);
        // High up, screen-space reflections and occlusion add little and cost GPU time over the wide view (measured).
        var high = quality == Quality.High;
        env.SsrEnabled = high && b < 0.5f;
        env.SsaoEnabled = high && b < 0.5f;
        env.VolumetricFogDensity = Mathf.Lerp(0.011f, 0.0009f, b);
        env.FogDensity = Mathf.Lerp(0.006f, 0.0005f, b);
        env.VolumetricFogLength = Mathf.Lerp(240f, 900f, b);
    }
}
