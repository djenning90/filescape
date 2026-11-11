using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The planetary world's side of the traveller: one-shot events (jump, twirl, landing, launch) are collected between
/// frames and handed to <see cref="Animate"/> with the next pose, so callers say what happened, not how to show it.
/// </summary>
public partial class AvatarRig
{
    private bool _pendingJump, _pendingTwirl, _pendingLand, _pendingLaunch;

    public void Jump() => _pendingJump = true;
    public void Twirl() => _pendingTwirl = true;
    public void Land(float impact) => _pendingLand = true;
    public void Launch() => _pendingLaunch = true;

    /// <summary>One frame of the traveller on foot (or hopping): its motion, and any events since the last frame.</summary>
    public void Drive(float dt, float speed, bool grounded, float verticalSpeed, float windUp01 = 0)
    {
        Animate(dt, new AvatarState(speed, grounded, verticalSpeed, _pendingJump, _pendingTwirl, _pendingLand, windUp01, Flying: false, Twirl: false));
        _pendingJump = _pendingTwirl = _pendingLand = _pendingLaunch = false;
    }

    /// <summary>One frame in flight: streamlined along the direction of travel.</summary>
    public void SetFlying(Vector3 position, Vector3 up, Vector3 forward, float progress01, float dt = 1f / 60)
    {
        // In flight the body leans into the direction of travel: up is the local up, facing is the travel direction flattened.
        var f = forward - up * forward.Dot(up);
        if (f.LengthSquared() < 1e-5f) f = up.Cross(Vector3.Right).LengthSquared() > 1e-4f ? up.Cross(Vector3.Right) : up.Cross(Vector3.Forward);
        SetPose(position, up, f.Normalized());
        Animate(dt, new AvatarState(9f, false, 0f, false, false, false, 0f, Flying: true, Twirl: _pendingLaunch));
        _pendingLaunch = false;
    }
}
