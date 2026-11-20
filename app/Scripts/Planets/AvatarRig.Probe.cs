using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// What the traveller's drawn pose is, for probes (PLANET-81: the landing orientation trace). Read-only views of the
/// procedural animation's state after the last <see cref="Animate"/>; nothing here changes the pose.
/// </summary>
public partial class AvatarRig
{
    /// <summary>The drawn body's axis, world space: the direction from the feet up through the body after the lean and
    /// twirl (the rig's up when standing upright).</summary>
    public Vector3 BodyAxis => _lean.GlobalBasis.Y.Normalized();

    /// <summary>The rig's own up and facing, world space (what <see cref="SetPose"/> was given last).</summary>
    public Vector3 RigUp => _up;

    public Vector3 RigFacing => _facing;

    /// <summary>The lean the last frame drew, degrees about the rig's right axis: positive forward (towards the facing).</summary>
    public float LeanDegrees => _leanDegrees;

    /// <summary>The flight pose's amount, 0 (on foot) to 1 (streamlined, nearly prone).</summary>
    public float FlightPose01 => _fly;

    /// <summary>The squash and stretch, dimensionless: 0 at rest, negative squashed, positive stretched.</summary>
    public float SquashAmount => _squashAmount;

    /// <summary>The head's and the lower foot's height above the rig's origin along its up, metres (standing: ≈ 0.83 and 0).</summary>
    public float HeadHeightMetres => (_head.GlobalPosition - GlobalPosition).Dot(_up) / Mathf.Max(Scale.X, 1e-4f);

    public float LowestFootMetres => Mathf.Min((_footL.GlobalPosition - GlobalPosition).Dot(_up), (_footR.GlobalPosition - GlobalPosition).Dot(_up)) / Mathf.Max(Scale.X, 1e-4f);

    /// <summary>The landing squash events handed to <see cref="Animate"/> so far (probes: a touchdown must give one).</summary>
    public int LandingsAnimated { get; private set; }
}
