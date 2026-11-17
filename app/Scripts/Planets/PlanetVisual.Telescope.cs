using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The overlook telescope's light (PLANET-55, DJ 2026-11-17: "give me a visible cue on approach that E at the telescope
/// opens the current world's file and destination list"): a soft cyan light on the telescope that follows
/// <see cref="TelescopeCueTarget"/> (0 = far, 1 = at the stones or on the platform), which the world sets each frame
/// from where you stand. The words — "E · Telescope · this world's files and destinations" — are a pinned sign over
/// the telescope in the sign layer (<c>PlanetWorld.Bell.cs</c>), which keeps clear of the HUD. The overlook's own view
/// is unchanged. Not on a repository, whose overlook is the workbench.
/// </summary>
public partial class PlanetVisual
{
    // ---- by eye, 2026-11-17
    /// <summary>Where the telescope stands on the overlook mesh (its tripod's top), mesh metres: see <c>PlanetArt.Overlook</c>.</summary>
    private static readonly Vector3 TelescopeMeshPoint = new(0.3f, PlanetArt.OverlookTopMeshMetres + 1.12f, -1.1f);

    /// <summary>The light on the telescope at full cue, its energy and range in metres.</summary>
    private const float TelescopeLightEnergy = 2.2f, TelescopeLightRangeMetres = 3.5f;

    private OmniLight3D? _telescopeLight;
    private float _telescopeCue;

    /// <summary>How strongly the telescope's cue should show, 0–1 (the world sets it; the cue eases towards it at the root mark's rate).</summary>
    public float TelescopeCueTarget { get; set; }

    /// <summary>The cue's current strength, 0–1 (the world shows the sign above a threshold; probes).</summary>
    public float TelescopeCue => _telescopeCue;

    /// <summary>The telescope's position in this node's frame, or null without one.</summary>
    public Vector3? TelescopePoint { get; private set; }

    /// <summary>The light on the telescope, placed with the overlook's transform <paramref name="overlook"/>.</summary>
    private void BuildTelescopeCue(Transform3D overlook)
    {
        TelescopePoint = overlook * TelescopeMeshPoint;
        _telescopeLight = new OmniLight3D { Name = "TelescopeLight", LightColor = new Color(0.65f, 0.9f, 1f), LightEnergy = 0, OmniRange = TelescopeLightRangeMetres, Position = TelescopePoint.Value };
        AddChild(_telescopeLight);
    }

    /// <summary>Per frame: ease towards the target and light the telescope.</summary>
    private void AnimateTelescopeCue(float dt)
    {
        if (_telescopeLight == null) return;
        _telescopeCue = Mathf.Lerp(_telescopeCue, Mathf.Clamp(TelescopeCueTarget, 0, 1), 1 - Mathf.Exp(-GlowRatePerSecond * dt));
        _telescopeLight.LightEnergy = TelescopeLightEnergy * _telescopeCue;
    }
}
