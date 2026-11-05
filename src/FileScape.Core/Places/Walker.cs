using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Units;

namespace FileScape.Core.Places;

/// <summary>
/// Walking and jumping parameters (WALK-01..05, JUMP-01; AD-16). Places world space: 1 world unit = 1 m, Y up
/// (docs/units.md). These are provisional until DJ has tried them (KD-20); each note says what changing it does.
/// </summary>
public sealed record WalkParams
{
    /// <summary>Eye above the supporting surface. 1.75 m is an adult's eye height; it sets how big everything feels.</summary>
    public Meters EyeHeight { get; init; } = Meters.Of(1.75f);

    /// <summary>
    /// Head clearance above the feet: structures whose underside is higher never block (lintels, overhangs). Lower it
    /// and low beams stop blocking; raise it and more overhangs become walls.
    /// </summary>
    public Meters BodyHeight { get; init; } = Meters.Of(1.85f);

    /// <summary>
    /// Body radius (a vertical disc). It is also the closest the eye gets to a wall: 0.35 m keeps tower faces from filling
    /// the view while still fitting the 1.15 m alleys between towers (PlaceParams.FileCell − FileFootprint).
    /// </summary>
    public Meters Radius { get; init; } = Meters.Of(0.35f);

    /// <summary>The highest ledge climbed without jumping (stairs, tier terraces at 0.42 m). Raise it and low blocks stop needing a jump.</summary>
    public Meters StepHeight { get; init; } = Meters.Of(0.45f);

    public MetersPerSecond WalkSpeed { get; init; } = MetersPerSecond.Of(4.2f);

    /// <summary>Speed while Shift is held.</summary>
    public MetersPerSecond RunSpeed { get; init; } = MetersPerSecond.Of(8.4f);

    /// <summary>
    /// How quickly horizontal velocity follows the input on the ground (exponential smoothing, <see cref="PerSecond"/>):
    /// 14/s reaches 90 % of the target speed in ≈ 0.16 s and stops as quickly. Higher feels snappier, lower floatier.
    /// </summary>
    public PerSecond GroundResponse { get; init; } = PerSecond.Of(14f);

    /// <summary>The same in the air: 2.2/s keeps most of the momentum while allowing some steering (JUMP-01).</summary>
    public PerSecond AirResponse { get; init; } = PerSecond.Of(2.2f);

    /// <summary>In the air with no input: momentum is almost kept (a time constant of ≈ 3.3 s).</summary>
    public PerSecond AirCoast { get; init; } = PerSecond.Of(0.3f);

    /// <summary>Downward acceleration (a positive magnitude). With <see cref="JumpSpeed"/> it sets the jump's height and air time.</summary>
    public MetersPerSecondSquared Gravity { get; init; } = MetersPerSecondSquared.Of(17f);

    /// <summary>Upward take-off speed: apex = v² / 2g ≈ 0.83 m, air time = 2v/g ≈ 0.62 s on the flat.</summary>
    public MetersPerSecond JumpSpeed { get; init; } = MetersPerSecond.Of(5.3f);

    /// <summary>A jump still works this long after walking off a ledge (forgiving timing).</summary>
    public TimeSpan CoyoteTime { get; init; } = TimeSpan.FromSeconds(0.12);

    /// <summary>A jump pressed this long before landing happens on landing (forgiving timing).</summary>
    public TimeSpan JumpBuffer { get; init; } = TimeSpan.FromSeconds(0.15);

    /// <summary>While airborne, a ledge up to this far above the feet can still be moved onto (a forgiving landing).</summary>
    public Meters AirLedgeTolerance { get; init; } = Meters.Of(0.3f);

    /// <summary>Arrow-key turn rate (heading). 120°/s is a full turn in 3 s.</summary>
    public DegreesPerSecond TurnRate { get; init; } = DegreesPerSecond.Of(120f);

    /// <summary>Walking down, the walker stays on the ground over drops up to this (stairs, ramps) instead of falling.</summary>
    public Meters SnapDown { get; init; } = Meters.Of(0.5f);

    /// <summary>
    /// How quickly the camera follows an upward step (exponential smoothing). Steps are climbed instantly by the body;
    /// the eye catches up so stairs do not jolt the view, never lagging more than <see cref="MaxEyeLag"/>.
    /// </summary>
    public PerSecond EyeSmoothing { get; init; } = PerSecond.Of(18f);

    public Meters MaxEyeLag { get; init; } = Meters.Of(0.35f);

    /// <summary>Feet below this world height mean the walker has left the world; the owner relocates it (WALK-07).</summary>
    public Meters FallLimit { get; init; } = Meters.Of(-30f);

    /// <summary>
    /// Fixed integration frequency. Behaviour does not depend on the frame rate because every frame is integrated in
    /// these steps (WALK-04); 120 Hz keeps a running walker (8.4 m/s) moving 7 cm per step, well under its radius.
    /// </summary>
    public int SubStepsPerSecond { get; init; } = 120;

    /// <summary>The longest frame integrated at once; a longer stall (a hitch, a breakpoint) is dropped, not replayed.</summary>
    public TimeSpan MaxFrame { get; init; } = TimeSpan.FromSeconds(0.25);

    public static readonly WalkParams Default = new();

    /// <summary>Rejects values that would break the integration (non-finite, non-positive sizes or rates).</summary>
    public WalkParams Validate()
    {
        static void Positive(float v, string name)
        {
            if (!float.IsFinite(v) || v <= 0) throw new ArgumentOutOfRangeException(name, v, "must be finite and positive");
        }

        Positive(EyeHeight.Value, nameof(EyeHeight));
        Positive(BodyHeight.Value, nameof(BodyHeight));
        Positive(Radius.Value, nameof(Radius));
        Positive(Gravity.Value, nameof(Gravity));
        Positive(SubStepsPerSecond, nameof(SubStepsPerSecond));
        if (!StepHeight.IsFinite || StepHeight < Meters.Zero) throw new ArgumentOutOfRangeException(nameof(StepHeight));
        if (!FallLimit.IsFinite) throw new ArgumentOutOfRangeException(nameof(FallLimit));
        return this;
    }
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

    /// <summary>Frame time not yet integrated, in seconds (always less than one sub-step).</summary>
    private float _accumulatorSeconds;

    /// <summary>Time since the walker last stood on something, in seconds (drives coyote time).</summary>
    private float _sinceGroundedSeconds = 999f;

    /// <summary>Time since Space was last pressed, in seconds (drives the jump buffer); 999 = not recently.</summary>
    private float _sinceJumpPressedSeconds = 999f;

    /// <summary>The smoothed eye height (world Y, metres) that the camera uses; it trails upward steps.</summary>
    private float _eyeY;

    public Walker(WalkParams? parameters = null)
    {
        _p = (parameters ?? WalkParams.Default).Validate();
    }

    public WalkParams Params => _p;

    /// <summary>The feet position in world space, metres: (x, height of the support, z).</summary>
    public Vector3 Feet { get; private set; }

    /// <summary>Heading (CameraMath convention: 0 looks −Z, positive turns left). Movement follows the heading.</summary>
    public Degrees Yaw { get; private set; }

    /// <summary>Gaze up/down (positive up), clamped to ±85°. It never affects where walking goes (WALK-02).</summary>
    public Degrees LookPitch { get; private set; }

    /// <summary>Horizontal velocity in world space, metres per second, as (x, z).</summary>
    public Vector2 HorizontalVelocity { get; private set; }

    /// <summary>Vertical velocity, positive upwards.</summary>
    public MetersPerSecond VerticalVelocity { get; private set; }

    public bool Grounded { get; private set; }

    /// <summary>The world height of the surface currently under the walker.</summary>
    public Meters Support { get; private set; }

    public int Jumps { get; private set; }
    public int Landings { get; private set; }

    /// <summary>Set when the feet fell below <see cref="WalkParams.FallLimit"/>; the owner should relocate the walker.</summary>
    public bool IsLost { get; private set; }

    /// <summary>The camera eye in world space (metres): feet plus eye height, with upward steps smoothed (no snapping, no shake).</summary>
    public Vector3 Eye => new(Feet.X, _eyeY, Feet.Z);

    /// <summary>Places the walker standing on whatever supports it at (x, z), as low as <paramref name="feet"/>.Y allows.</summary>
    public void Teleport(PlaceTerrain terrain, Vector3 feet, Degrees yaw, Degrees lookPitch)
    {
        var p = new Vector2(feet.X, feet.Z);
        var s = terrain.SupportHeight(p, _p.Radius.Value, feet.Y, _p.StepHeight.Value);
        Feet = new Vector3(feet.X, s, feet.Z);
        Support = Meters.Of(s);
        Yaw = yaw;
        LookPitch = CameraMath.ClampLookPitch(lookPitch);
        HorizontalVelocity = Vector2.Zero;
        VerticalVelocity = MetersPerSecond.Zero;
        Grounded = true;
        IsLost = false;
        _sinceGroundedSeconds = 0;
        _sinceJumpPressedSeconds = 999f;
        _eyeY = s + _p.EyeHeight.Value;
        _accumulatorSeconds = 0;
    }

    /// <summary>Places the walker so that its eye is at <paramref name="eye"/>, standing on the support below it.</summary>
    public void TeleportToEye(PlaceTerrain terrain, Vector3 eye, Degrees yaw, Degrees lookPitch) =>
        Teleport(terrain, eye - new Vector3(0, _p.EyeHeight.Value, 0), yaw, lookPitch);

    /// <summary>Mouse look: horizontal changes the heading, vertical the gaze (WALK-03).</summary>
    public void Look(Degrees deltaYaw, Degrees deltaLookPitch)
    {
        Yaw += deltaYaw;
        LookPitch = CameraMath.ClampLookPitch(LookPitch + deltaLookPitch);
    }

    public void SetGaze(Degrees yaw, Degrees lookPitch)
    {
        Yaw = yaw;
        LookPitch = CameraMath.ClampLookPitch(lookPitch);
    }

    /// <summary>Integrates one frame of <paramref name="frame"/> length in fixed sub-steps.</summary>
    public void Update(PlaceTerrain terrain, WalkInput input, TimeSpan frame)
    {
        if (input.JumpPressed) _sinceJumpPressedSeconds = 0;
        _accumulatorSeconds += Math.Clamp(frame.ToSecondsF(), 0, _p.MaxFrame.ToSecondsF());
        var stepSeconds = 1f / _p.SubStepsPerSecond;
        while (_accumulatorSeconds >= stepSeconds)
        {
            _accumulatorSeconds -= stepSeconds;
            Step(terrain, input, stepSeconds);
        }
    }

    /// <summary>One fixed step. Plain floats inside, all in metres, seconds and degrees (named), for speed and clarity.</summary>
    private void Step(PlaceTerrain terrain, WalkInput input, float dtSeconds)
    {
        _sinceJumpPressedSeconds += dtSeconds;
        _sinceGroundedSeconds = Grounded ? 0 : _sinceGroundedSeconds + dtSeconds;
        Yaw += Degrees.Of(-input.Turn * _p.TurnRate.Value * dtSeconds);

        // Desired horizontal velocity along the heading (never along the gaze, WALK-02); diagonals normalised.
        var fwd = CameraMath.HorizontalForward(Yaw);
        var right = CameraMath.Right(Yaw);
        var wish = new Vector2(fwd.X, fwd.Z) * input.Forward + new Vector2(right.X, right.Z) * input.Strafe;
        if (wish.LengthSquared() > 1) wish = Vector2.Normalize(wish);
        var speedMetersPerSecond = (input.Run ? _p.RunSpeed : _p.WalkSpeed).Value;
        var targetVelocity = wish * speedMetersPerSecond;
        var response = Grounded ? _p.GroundResponse : _p.AirResponse;
        if (!Grounded && wish == Vector2.Zero) response = _p.AirCoast; // in the air, letting go keeps momentum
        HorizontalVelocity = Vector2.Lerp(HorizontalVelocity, targetVelocity, response.SmoothingFactor(dtSeconds));
        if (Grounded && wish == Vector2.Zero && HorizontalVelocity.LengthSquared() < 0.0004f) HorizontalVelocity = Vector2.Zero; // below 2 cm/s: stopped

        // Jump: on the ground or shortly after leaving it, pressed now or shortly before.
        var verticalMetersPerSecond = VerticalVelocity.Value;
        if (_sinceJumpPressedSeconds <= _p.JumpBuffer.ToSecondsF() && _sinceGroundedSeconds <= _p.CoyoteTime.ToSecondsF() && verticalMetersPerSecond <= 0.01f)
        {
            verticalMetersPerSecond = _p.JumpSpeed.Value;
            Grounded = false;
            _sinceGroundedSeconds = _p.CoyoteTime.ToSecondsF() + 1;
            _sinceJumpPressedSeconds = 999f;
            Jumps++;
        }

        var feet = Feet;
        var pos = new Vector2(feet.X, feet.Z);
        var radius = _p.Radius.Value;

        // Horizontal move with slide-along collision. A solid blocks if its top is above what can be stepped onto
        // (or, airborne, reached by the ledge tolerance) and its underside is below the head.
        var allowance = (Grounded ? _p.StepHeight : _p.AirLedgeTolerance).Value;
        var moved = pos + HorizontalVelocity * dtSeconds;
        for (var iter = 0; iter < 4; iter++)
        {
            var push = Vector2.Zero;
            foreach (var i in terrain.BoxesNear(moved, radius))
            {
                var b = terrain.Boxes[i];
                if (b.Top <= feet.Y + allowance || b.Bottom >= feet.Y + _p.BodyHeight.Value) continue;
                var po = b.PushOut(moved, radius);
                if (po.LengthSquared() > push.LengthSquared()) push = po;
            }

            var len = moved.Length();
            var limit = terrain.BoundaryRadius - radius;
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
            if (r.HeightNear(moved, radius) is not { } rh || rh <= feet.Y + allowance) continue;
            var axis = Vector2.Normalize(r.End - r.Start);
            var slide = pos + axis * Vector2.Dot(moved - pos, axis);
            moved = r.HeightNear(slide, radius) is { } sh && sh > feet.Y + allowance ? pos : slide;
            HorizontalVelocity = axis * Vector2.Dot(HorizontalVelocity, axis);
        }

        pos = moved;

        // Vertical: gravity, landing, stepping up and staying on the ground walking down.
        var support = terrain.SupportHeight(pos, radius, feet.Y, allowance);
        var y = feet.Y;
        if (Grounded)
        {
            if (support >= y - _p.SnapDown.Value && support <= y + _p.StepHeight.Value)
            {
                y = support; // flat, step up, or down a stair/ramp
                verticalMetersPerSecond = 0;
            }
            else
            {
                Grounded = false; // walked off an edge
            }
        }

        if (!Grounded)
        {
            verticalMetersPerSecond -= _p.Gravity.Value * dtSeconds;
            y += verticalMetersPerSecond * dtSeconds;
            if (y <= support && verticalMetersPerSecond <= 0)
            {
                y = support;
                verticalMetersPerSecond = 0;
                Grounded = true;
                Landings++;
            }
        }

        VerticalVelocity = MetersPerSecond.Of(verticalMetersPerSecond);
        Support = Meters.Of(support);
        Feet = new Vector3(pos.X, y, pos.Y);
        if (y < _p.FallLimit.Value) IsLost = true;

        // The eye follows upward steps smoothly; downward and airborne motion are followed exactly enough to feel direct.
        var targetEye = y + _p.EyeHeight.Value;
        _eyeY = targetEye > _eyeY && Grounded
            ? MathF.Max(targetEye - _p.MaxEyeLag.Value, _eyeY + (targetEye - _eyeY) * _p.EyeSmoothing.SmoothingFactor(dtSeconds))
            : targetEye;
    }
}
