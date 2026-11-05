using System.Numerics;
using FileScape.Core.Interaction;

namespace FileScape.Core.Places;

/// <summary>Walking and jumping parameters (WALK-01..05, JUMP-01). 1 u = 1 m. Provisional until DJ tries them (KD-04).</summary>
public sealed record WalkParams
{
    public float EyeHeight { get; init; } = 1.75f;

    /// <summary>The walker's head clearance: structures whose underside is higher than this above the feet never block.</summary>
    public float BodyHeight { get; init; } = 1.85f;

    /// <summary>Body radius: also how close the eye gets to a wall (0.35 m keeps tower faces from filling the view).</summary>
    public float Radius { get; init; } = 0.35f;
    public float StepHeight { get; init; } = 0.45f;
    public float WalkSpeed { get; init; } = 4.2f;
    public float RunSpeed { get; init; } = 8.4f;

    /// <summary>Exponential approach rate of horizontal velocity on the ground (per second): ≈ 90 % of speed in 0.16 s.</summary>
    public float GroundResponse { get; init; } = 14f;

    /// <summary>The same in the air: momentum is mostly kept, with some steering (JUMP-01).</summary>
    public float AirResponse { get; init; } = 2.2f;

    public float Gravity { get; init; } = 17f;

    /// <summary>Take-off speed; apex = v² / 2g ≈ 0.83 m.</summary>
    public float JumpSpeed { get; init; } = 5.3f;

    /// <summary>A jump still works this long after walking off a ledge.</summary>
    public float CoyoteTime { get; init; } = 0.12f;

    /// <summary>A jump pressed this long before landing happens on landing.</summary>
    public float JumpBuffer { get; init; } = 0.15f;

    /// <summary>While airborne, a ledge up to this far above the feet can still be moved onto (a forgiving landing).</summary>
    public float AirLedgeTolerance { get; init; } = 0.3f;

    /// <summary>Arrow-key turn rate, degrees per second.</summary>
    public float TurnRate { get; init; } = 120f;

    /// <summary>Walking down: the walker stays on the ground over drops up to this (stairs, ramps) instead of falling.</summary>
    public float SnapDown { get; init; } = 0.5f;

    /// <summary>The camera's rate of following upward steps (per second); it never lags by more than <see cref="MaxEyeLag"/>.</summary>
    public float EyeSmoothing { get; init; } = 18f;

    public float MaxEyeLag { get; init; } = 0.35f;

    /// <summary>Below this the walker is considered lost and is returned to safety (WALK-07).</summary>
    public float FallLimit { get; init; } = -30f;

    public float SubStep { get; init; } = 1f / 120f;

    public static readonly WalkParams Default = new();
}

/// <summary>One frame of walking input. Axes are -1..1; <see cref="JumpPressed"/> is an edge (pressed this frame).</summary>
public readonly record struct WalkInput(float Forward, float Strafe, float Turn, bool Run, bool JumpPressed)
{
    public static readonly WalkInput None = default;
}

/// <summary>
/// A kinematic first-person walker (AD-16): a vertical disc with a body height, moving over a
/// <see cref="PlaceTerrain"/>. Heading (yaw) and gaze (look pitch) are separate: movement is always horizontal along
/// the heading, whatever the gaze (WALK-02). Integration uses fixed sub-steps, so behaviour does not depend on the
/// frame rate (WALK-04). Collision slides along solids; small steps are climbed and walkers stay grounded walking
/// down stairs and ramps (WALK-05).
/// </summary>
public sealed class Walker
{
    private readonly WalkParams _p;
    private float _accumulator;
    private float _sinceGrounded = 999f;
    private float _sinceJumpPressed = 999f;
    private float _eyeY;

    public Walker(WalkParams? parameters = null)
    {
        _p = parameters ?? WalkParams.Default;
    }

    public WalkParams Params => _p;

    /// <summary>The feet position (x, support height, z).</summary>
    public Vector3 Feet { get; private set; }

    /// <summary>Heading in CameraMath convention: 0 looks -Z, positive turns left.</summary>
    public float Yaw { get; private set; }

    public float LookPitch { get; private set; }
    public Vector2 HorizontalVelocity { get; private set; }
    public float VerticalVelocity { get; private set; }
    public bool Grounded { get; private set; }

    /// <summary>The height of the surface currently under the walker.</summary>
    public float Support { get; private set; }

    public int Jumps { get; private set; }
    public int Landings { get; private set; }

    /// <summary>Set when the walker fell below <see cref="WalkParams.FallLimit"/>; the owner should relocate it.</summary>
    public bool IsLost { get; private set; }

    /// <summary>The camera eye: feet plus eye height, with upward steps smoothed (no snapping, no shake).</summary>
    public Vector3 Eye => new(Feet.X, _eyeY, Feet.Z);

    /// <summary>Places the walker standing on whatever supports it at (x, z), as low as <paramref name="feet"/>.Y allows.</summary>
    public void Teleport(PlaceTerrain terrain, Vector3 feet, float yaw, float lookPitch)
    {
        var p = new Vector2(feet.X, feet.Z);
        var s = terrain.SupportHeight(p, _p.Radius, feet.Y, _p.StepHeight);
        Feet = new Vector3(feet.X, s, feet.Z);
        Support = s;
        Yaw = yaw;
        LookPitch = CameraMath.ClampLookPitch(lookPitch);
        HorizontalVelocity = Vector2.Zero;
        VerticalVelocity = 0;
        Grounded = true;
        IsLost = false;
        _sinceGrounded = 0;
        _sinceJumpPressed = 999f;
        _eyeY = s + _p.EyeHeight;
        _accumulator = 0;
    }

    /// <summary>Places the walker so that its eye is at <paramref name="eye"/>, standing on the support below it.</summary>
    public void TeleportToEye(PlaceTerrain terrain, Vector3 eye, float yaw, float lookPitch) =>
        Teleport(terrain, eye - new Vector3(0, _p.EyeHeight, 0), yaw, lookPitch);

    /// <summary>Mouse look: horizontal changes the heading, vertical the gaze (WALK-03).</summary>
    public void Look(float dYaw, float dLookPitch)
    {
        Yaw += dYaw;
        LookPitch = CameraMath.ClampLookPitch(LookPitch + dLookPitch);
    }

    public void SetGaze(float yaw, float lookPitch)
    {
        Yaw = yaw;
        LookPitch = CameraMath.ClampLookPitch(lookPitch);
    }

    public void Update(PlaceTerrain terrain, WalkInput input, float dt)
    {
        if (input.JumpPressed) _sinceJumpPressed = 0;
        _accumulator += Math.Clamp(dt, 0, 0.25f);
        var step = _p.SubStep;
        while (_accumulator >= step)
        {
            _accumulator -= step;
            Step(terrain, input, step);
        }
    }

    private void Step(PlaceTerrain terrain, WalkInput input, float dt)
    {
        _sinceJumpPressed += dt;
        _sinceGrounded = Grounded ? 0 : _sinceGrounded + dt;
        Yaw += -input.Turn * _p.TurnRate * dt;

        // Desired horizontal velocity along the heading (never along the gaze, WALK-02); diagonals normalised.
        var fwd = CameraMath.HorizontalForward(Yaw);
        var right = CameraMath.Right(Yaw);
        var wish = new Vector2(fwd.X, fwd.Z) * input.Forward + new Vector2(right.X, right.Z) * input.Strafe;
        if (wish.LengthSquared() > 1) wish = Vector2.Normalize(wish);
        var speed = input.Run ? _p.RunSpeed : _p.WalkSpeed;
        var target = wish * speed;
        var rate = Grounded ? _p.GroundResponse : _p.AirResponse;
        if (!Grounded && wish == Vector2.Zero) rate = 0.3f; // in the air, letting go keeps momentum
        HorizontalVelocity = Vector2.Lerp(HorizontalVelocity, target, 1 - MathF.Exp(-rate * dt));
        if (Grounded && wish == Vector2.Zero && HorizontalVelocity.LengthSquared() < 0.0004f) HorizontalVelocity = Vector2.Zero;

        // Jump: on the ground or shortly after leaving it, pressed now or shortly before.
        if (_sinceJumpPressed <= _p.JumpBuffer && _sinceGrounded <= _p.CoyoteTime && VerticalVelocity <= 0.01f)
        {
            VerticalVelocity = _p.JumpSpeed;
            Grounded = false;
            _sinceGrounded = _p.CoyoteTime + 1;
            _sinceJumpPressed = 999f;
            Jumps++;
        }

        var feet = Feet;
        var pos = new Vector2(feet.X, feet.Z);

        // Horizontal move with slide-along collision.
        var allowance = Grounded ? _p.StepHeight : _p.AirLedgeTolerance;
        var moved = pos + HorizontalVelocity * dt;
        for (var iter = 0; iter < 4; iter++)
        {
            var push = Vector2.Zero;
            foreach (var i in terrain.BoxesNear(moved, _p.Radius))
            {
                var b = terrain.Boxes[i];
                if (b.Top <= feet.Y + allowance || b.Bottom >= feet.Y + _p.BodyHeight) continue;
                var po = b.PushOut(moved, _p.Radius);
                if (po.LengthSquared() > push.LengthSquared()) push = po;
            }

            var len = moved.Length();
            var limit = terrain.BoundaryRadius - _p.Radius;
            if (len > limit && push == Vector2.Zero)
            {
                push = -moved / len * (len - limit);
            }

            if (push == Vector2.Zero) break;
            moved += push;
            var n = Vector2.Normalize(push);
            var into = Vector2.Dot(HorizontalVelocity, n);
            if (into < 0) HorizontalVelocity -= n * into; // slide: drop the component into the obstacle
        }

        // A ramp is a wall where its surface is higher than the walker can step (its sides, or its high end from
        // below): slide along the ramp's axis if that is free, otherwise stay.
        foreach (var r in terrain.Ramps)
        {
            if (r.HeightNear(moved, _p.Radius) is not { } rh || rh <= feet.Y + allowance) continue;
            var axis = Vector2.Normalize(r.End - r.Start);
            var slide = pos + axis * Vector2.Dot(moved - pos, axis);
            moved = r.HeightNear(slide, _p.Radius) is { } sh && sh > feet.Y + allowance ? pos : slide;
            HorizontalVelocity = axis * Vector2.Dot(HorizontalVelocity, axis);
        }

        pos = moved;

        // Vertical: gravity, landing, stepping up and staying on the ground walking down.
        var support = terrain.SupportHeight(pos, _p.Radius, feet.Y, Grounded ? _p.StepHeight : _p.AirLedgeTolerance);
        var y = feet.Y;
        if (Grounded)
        {
            if (support >= y - _p.SnapDown && support <= y + _p.StepHeight)
            {
                y = support; // flat, step up, or down a stair/ramp
                VerticalVelocity = 0;
            }
            else
            {
                Grounded = false; // walked off an edge
            }
        }

        if (!Grounded)
        {
            VerticalVelocity -= _p.Gravity * dt;
            y += VerticalVelocity * dt;
            if (y <= support && VerticalVelocity <= 0)
            {
                y = support;
                VerticalVelocity = 0;
                Grounded = true;
                Landings++;
            }
        }

        Support = support;
        Feet = new Vector3(pos.X, y, pos.Y);
        if (y < _p.FallLimit) IsLost = true;

        // The eye follows upward steps smoothly; downward and airborne motion are followed exactly enough to feel direct.
        var targetEye = y + _p.EyeHeight;
        _eyeY = targetEye > _eyeY && Grounded
            ? MathF.Max(targetEye - _p.MaxEyeLag, _eyeY + (targetEye - _eyeY) * (1 - MathF.Exp(-_p.EyeSmoothing * dt)))
            : targetEye;
    }
}
