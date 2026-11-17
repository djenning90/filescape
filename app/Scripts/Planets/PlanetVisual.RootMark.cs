using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// A root landmark's interaction cue (PLANET-45, AD-58): rings of light on the ground round the gateway or monolith and a
/// soft light on its face, both following <see cref="RootMarkGlowTarget"/> (0 = far, 1 = within reach of E), which the
/// world sets each frame from where the traveller stands. Only the planet you stand on has them.
/// </summary>
public partial class PlanetVisual
{
    // ---- the cue's look and timing (by eye, 2026-11-16)
    /// <summary>The halo's alpha far away (a faint invitation seen from the arrival) and within reach.</summary>
    private const float HaloIdleAlpha = 0.12f, HaloReachAlpha = 0.85f;

    /// <summary>The face light's energy within reach (0 far away), its range and its place: this far up and in front of the face, metres.</summary>
    private const float FaceLightEnergy = 2.4f, FaceLightRangeMetres = 7f, FaceLightUpMetres = 1.8f, FaceLightFrontMetres = 1.6f;

    /// <summary>How fast the cue follows the target, per second (≈ 0.3 s to settle).</summary>
    private const float GlowRatePerSecond = 10f;

    /// <summary>The halo breathes a little within reach: this fraction of its alpha, at this rate, radians per second.</summary>
    private const float HaloBreath = 0.15f, HaloBreathRadiansPerSecond = 3f;

    private StandardMaterial3D? _haloMaterial;
    private OmniLight3D? _faceLight;
    private Color _cueColor;
    private float _glow;

    /// <summary>How strongly the cue should show, 0–1 (the world sets it; the cue eases towards it).</summary>
    public float RootMarkGlowTarget { get; set; }

    /// <summary>The cue's current strength, 0–1 (probes).</summary>
    public float RootMarkGlow => _glow;

    /// <summary>Builds the halo (at the mark's footprint) and the face light.</summary>
    private void BuildRootMarkCue(PlanetSurface surf, PlanetRootMark mark)
    {
        _cueColor = PlanetArt.RootMarkColor(mark.Kind);
        _haloMaterial = PlanetArt.RootMarkHaloMaterial(_cueColor);
        var radius = RootMarkLayout.Footprint(mark.Kind) + 0.25f; // just outside the stone base
        AddChild(new MeshInstance3D
        {
            Name = "RootMarkHalo",
            Mesh = Shared($"root-halo-{radius:0.00}", () => PlanetArt.RootMarkHalo(radius)),
            MaterialOverride = _haloMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Transform = Stand(surf, mark.Direction, mark.Facing, 1f, sink: 0),
        });
        var front = PlanetFrame.Move(mark.Direction, mark.Facing, FaceLightFrontMetres, surf.Radius);
        _faceLight = new OmniLight3D { Name = "RootMarkLight", LightColor = _cueColor, LightEnergy = 0, OmniRange = FaceLightRangeMetres, Position = PlanetWorld.Gd(surf.PointAt(front, FaceLightUpMetres)) };
        AddChild(_faceLight);
        AnimateRootMarkCue(0);
    }

    /// <summary>Per frame: ease towards the target and show it.</summary>
    private void AnimateRootMarkCue(float dt)
    {
        if (_haloMaterial == null) return;
        _glow = Mathf.Lerp(_glow, Mathf.Clamp(RootMarkGlowTarget, 0, 1), 1 - Mathf.Exp(-GlowRatePerSecond * dt));
        var breath = 1 - HaloBreath * _glow * (0.5f + 0.5f * Mathf.Sin(_time * HaloBreathRadiansPerSecond));
        _haloMaterial.AlbedoColor = _cueColor with { A = Mathf.Lerp(HaloIdleAlpha, HaloReachAlpha, _glow) * breath };
        if (_faceLight != null) _faceLight.LightEnergy = FaceLightEnergy * _glow;
    }
}
