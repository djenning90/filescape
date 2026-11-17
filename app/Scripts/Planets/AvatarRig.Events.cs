using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The planetary world's side of the traveller: one-shot events (jump, twirl, landing, launch) are collected between
/// frames and handed to <see cref="Animate"/> with the next pose, so callers say what happened, not how to show it.
/// </summary>
public partial class AvatarRig
{
    private bool _pendingJump, _pendingTwirl, _pendingLand, _pendingLaunch, _pendingFlail;

    /// <summary>Left the ground: a stretch on the next <see cref="Drive"/>.</summary>
    public void Jump() => _pendingJump = true;

    /// <summary>An air jump: the twirl.</summary>
    public void Twirl() => _pendingTwirl = true;

    /// <summary>A last effort on a Hazard World (PLANET-62): a flail on the next frame, on foot or falling.</summary>
    public void Flail() => _pendingFlail = true;

    /// <summary>Touched down: a squash. <paramref name="impact"/> is not used; the squash's strength comes from the
    /// smoothed vertical speed <see cref="Drive"/> has been given.</summary>
    public void Land(float impact) => _pendingLand = true;

    /// <summary>A launch: a twirl at the start of the flight (on the next <see cref="SetFlying"/>).</summary>
    public void Launch() => _pendingLaunch = true;

    /// <summary>One frame of the traveller on foot (or hopping): its motion, and any events since the last frame.</summary>
    public void Drive(float dt, float speed, bool grounded, float verticalSpeed, float windUp01 = 0)
    {
        Animate(dt, new AvatarState(speed, grounded, verticalSpeed, _pendingJump, _pendingTwirl, _pendingLand, windUp01, Flying: false, Twirl: false, Flail: _pendingFlail));
        _pendingJump = _pendingTwirl = _pendingLand = _pendingLaunch = _pendingFlail = false;
    }

    /// <summary>One frame in flight: streamlined along the direction of travel.</summary>
    public void SetFlying(Vector3 position, Vector3 up, Vector3 forward, float progress01, float scale = 1f, float dt = 1f / 60)
    {
        // In flight the body leans into the direction of travel: up is the local up, facing is the travel direction flattened.
        var f = forward - up * forward.Dot(up);
        if (f.LengthSquared() < 1e-5f) f = up.Cross(Vector3.Right).LengthSquared() > 1e-4f ? up.Cross(Vector3.Right) : up.Cross(Vector3.Forward);
        SetPose(position, up, f.Normalized());
        Scale = new Vector3(scale, scale, scale); // after SetPose, which sets the whole transform (it used to undo the flight's scale)
        // The speed (9 m/s) only matters to the running amount, which flight zeroes; the dt default assumes 60 Hz.
        Animate(dt, new AvatarState(9f, false, 0f, false, false, false, 0f, Flying: true, Twirl: _pendingLaunch, Flail: _pendingFlail));
        _pendingLaunch = _pendingFlail = false;
    }
}
