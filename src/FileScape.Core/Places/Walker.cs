using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Movement;
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

    /// <summary>
    /// Walking speed (W): 5.5 m/s, a brisk jog. Until 2026-11-06 it was 8 m/s because places were large (the nearest
    /// child gate ≈ 45 m from the arrival point; DJ found 4.2 m/s too slow there, KD-20). The compact layout (PLACE-14)
    /// brings the nearest gate to ≈ 18 m, so ordinary walking can be comfortable again (KD-28). The user can change it
    /// (Settings: Interaction.Preferences, 2–30 m/s); saved speeds are never overwritten.
    /// </summary>
    public MetersPerSecond WalkSpeed { get; init; } = MetersPerSecond.Of(5.5f);

    /// <summary>Speed while Shift is held: twice the walking speed, for purposeful running (was 16 m/s).</summary>
    public MetersPerSecond RunSpeed { get; init; } = MetersPerSecond.Of(11f);

    /// <summary>The defaults before the compact layout (8 / 16 m/s): a saved file with exactly these is offered the new ones.</summary>
    public static readonly (float Walk, float Run) PreviousDefaultSpeeds = (8f, 16f);

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

    /// <summary>
    /// Upward take-off speed. 6.4 m/s (was 5.3, apex 0.83 m; DJ: "jumping could be higher", 2026-11-06): with the apex
    /// hang below, a held jump peaks at ≈ 1.22 m and lasts ≈ 0.78 s on the flat — over a 0.8 m block with room to spare,
    /// still below every lintel and tower roof that matters (a jump never lands on a file tower: see WalkTests).
    /// </summary>
    public MetersPerSecond JumpSpeed { get; init; } = MetersPerSecond.Of(6.4f);

    /// <summary>
    /// Near the top of a held jump (vertical speed below this), gravity is reduced by <see cref="ApexGravityScale"/>: a
    /// brief hang that makes the arc feel floaty and easier to steer onto a ledge. Zero disables it.
    /// </summary>
    public MetersPerSecond ApexHangSpeed { get; init; } = MetersPerSecond.Of(1.2f);

    public float ApexGravityScale { get; init; } = 0.65f;

    /// <summary>Rising with Space already released: gravity × this, so a tap is a short hop and a hold a full jump.</summary>
    public float ShortHopGravityScale { get; init; } = 2.2f;

    /// <summary>Falling: gravity × this, so landings come a little sooner than the rise (snappier, less floaty on the way down).</summary>
    public float FallGravityScale { get; init; } = 1.15f;

    /// <summary>A jump still works this long after walking off a ledge (forgiving timing).</summary>
    public TimeSpan CoyoteTime { get; init; } = TimeSpan.FromSeconds(0.12);

    /// <summary>A jump pressed this long before landing happens on landing (forgiving timing).</summary>
    public TimeSpan JumpBuffer { get; init; } = TimeSpan.FromSeconds(0.15);

    /// <summary>
    /// Airborne boosts per airtime (the double jump, <see cref="JumpParams.AirJumps"/>); re-armed on landing. 0 disables
    /// it (the behaviour before 2026-11-11).
    /// </summary>
    public int AirJumps { get; init; } = JumpParams.Default.AirJumps;

    /// <summary>The vertical speed a boost sets (<see cref="JumpParams.AirJumpSpeed"/>): jump + boost at the apex peaks ≈ 2.7 m.</summary>
    public MetersPerSecond AirJumpSpeed { get; init; } = JumpParams.Default.AirJumpSpeed;

    /// <summary>Falling within this of the support, a press is kept for the landing (<see cref="JumpParams.AirJumpGroundClearance"/>).</summary>
    public Meters AirJumpGroundClearance { get; init; } = JumpParams.Default.AirJumpGroundClearance;

    /// <summary>Speed a boost adds along the movement input (<see cref="JumpParams.AirJumpNudge"/>).</summary>
    public MetersPerSecond AirJumpNudge { get; init; } = JumpParams.Default.AirJumpNudge;

    /// <summary>The vertical parameters as the shared <see cref="JumpModel"/> takes them.</summary>
    public JumpParams Jump => new()
    {
        Gravity = Gravity,
        JumpSpeed = JumpSpeed,
        ApexHangSpeed = ApexHangSpeed,
        ApexGravityScale = ApexGravityScale,
        ShortHopGravityScale = ShortHopGravityScale,
        FallGravityScale = FallGravityScale,
        CoyoteTime = CoyoteTime,
        JumpBuffer = JumpBuffer,
        AirJumps = AirJumps,
        AirJumpSpeed = AirJumpSpeed,
        AirJumpGroundClearance = AirJumpGroundClearance,
        AirJumpNudge = AirJumpNudge,
    };

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
    /// these steps (WALK-04); 120 Hz keeps a running walker (16 m/s) moving 13 cm per step and the fastest allowed (30 m/s) 25 cm, under its 0.35 m radius, so it cannot pass through a 0.55 m gate pillar.
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
        Positive(WalkSpeed.Value, nameof(WalkSpeed));
        Positive(RunSpeed.Value, nameof(RunSpeed));
        Positive(SubStepsPerSecond, nameof(SubStepsPerSecond));
        if (!StepHeight.IsFinite || StepHeight < Meters.Zero) throw new ArgumentOutOfRangeException(nameof(StepHeight));
        if (!FallLimit.IsFinite) throw new ArgumentOutOfRangeException(nameof(FallLimit));
        Jump.Validate();
        return this;
    }
}

/// <summary>
/// One frame of walking input. Axes are -1..1; <see cref="JumpPressed"/> is an edge (pressed this frame);
/// <see cref="JumpReleased"/> says Space is up (a rising jump becomes a short hop). The default is a held jump.
/// </summary>
public readonly record struct WalkInput(float Forward, float Strafe, float Turn, bool Run, bool JumpPressed, bool JumpReleased = false)
{
    public static readonly WalkInput None = default;
}

/// <summary>
/// A kinematic first-person walker (AD-16): a vertical disc with a body height, moving over a
/// <see cref="PlaceTerrain"/>. Heading (yaw) and gaze (look pitch) are separate: movement is always horizontal along
/// the heading, whatever the gaze (WALK-02). Integration uses fixed sub-steps, so behaviour does not depend on the
/// frame rate (WALK-04). Collision slides along solids; small steps are climbed and walkers stay grounded walking
/// down stairs and ramps (WALK-05). The vertical motion (jump, arc, double jump, landing) is the shared
/// <see cref="JumpModel"/>.
/// </summary>
public sealed class Walker
{
    private WalkParams _p;

    /// <summary>Frame time not yet integrated, in seconds (always less than one sub-step).</summary>
    private float _accumulatorSeconds;

    /// <summary>Take-off, arc, boost and landing (shared with the sphere walker).</summary>
    private readonly JumpModel _jump;

    /// <summary>The smoothed eye height (world Y, metres) that the camera uses; it trails upward steps.</summary>
    private float _eyeY;

    // Durations converted once to float seconds for the per-sub-step arithmetic (TimeSpan→float costs a division).
    private readonly float _stepSeconds, _maxFrameSeconds;

    public Walker(WalkParams? parameters = null)
    {
        _p = (parameters ?? WalkParams.Default).Validate();
        _stepSeconds = 1f / _p.SubStepsPerSecond;
        _jump = new JumpModel(_p.Jump);
        _maxFrameSeconds = _p.MaxFrame.ToSecondsF();
    }

    public WalkParams Params => _p;

    /// <summary>Changes the walking and running speeds (the user's preference, CTRL-02); everything else is kept.</summary>
    public void SetSpeeds(MetersPerSecond walk, MetersPerSecond run) => _p = (_p with { WalkSpeed = walk, RunSpeed = run }).Validate();

    /// <summary>Times the head met an overhang while rising (checks: head clearance).</summary>
    public int HeadBumps { get; private set; }

    /// <summary>The feet position in world space, metres: (x, height of the support, z).</summary>
    public Vector3 Feet { get; private set; }

    /// <summary>Heading (CameraMath convention: 0 looks −Z, positive turns left). Movement follows the heading.</summary>
    public Degrees Yaw { get; private set; }

    /// <summary>Gaze up/down (positive up), clamped to ±85°. It never affects where walking goes (WALK-02).</summary>
    public Degrees LookPitch { get; private set; }

    /// <summary>Horizontal velocity in world space, metres per second, as (x, z).</summary>
    public Vector2 HorizontalVelocity { get; private set; }

    /// <summary>Vertical velocity, positive upwards.</summary>
    public MetersPerSecond VerticalVelocity => _jump.VerticalVelocity;

    public bool Grounded => _jump.Grounded;

    /// <summary>The world height of the surface currently under the walker.</summary>
    public Meters Support { get; private set; }

    /// <summary>Take-offs from the ground (including coyote and buffered jumps).</summary>
    public int Jumps => _jump.Jumps;

    /// <summary>Airborne boosts (double jumps).</summary>
    public int AirJumps => _jump.AirJumps;

    public int Landings => _jump.Landings;

    /// <summary>
    /// The latest vertical event of the last <see cref="Update"/> (Jump, AirJump with its twirl, Land with its impact
    /// speed), or None: a frame-level view of the sub-steps, for animation and sound.
    /// </summary>
    public JumpEvent LastEvent { get; private set; }

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
        _jump.Reset();
        LastEvent = JumpEvent.None;
        IsLost = false;
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
        if (input.JumpPressed) _jump.Press();
        LastEvent = JumpEvent.None;
        _accumulatorSeconds += Math.Clamp(frame.ToSecondsF(), 0, _maxFrameSeconds);
        while (_accumulatorSeconds >= _stepSeconds)
        {
            _accumulatorSeconds -= _stepSeconds;
            Step(terrain, input, _stepSeconds);
        }
    }

    /// <summary>One fixed step. Plain floats inside, all in metres, seconds and degrees (named), for speed and clarity.</summary>
    private void Step(PlaceTerrain terrain, WalkInput input, float dtSeconds)
    {
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

        // Jump: on the ground or shortly after leaving it, pressed now or shortly before; airborne, the one boost.
        var feet = Feet;
        var e = _jump.BeginStep(dtSeconds, feet.Y - Support.Value);
        if (e.Kind == JumpEventKind.AirJump && wish != Vector2.Zero)
        {
            // The twirl's small forward lean, never beyond the target speed.
            var dir = Vector2.Normalize(wish);
            var along = Vector2.Dot(HorizontalVelocity, dir);
            var add = MathF.Min(_p.AirJumpNudge.Value, MathF.Max(0, speedMetersPerSecond - along));
            HorizontalVelocity += dir * add;
        }

        if (e.Kind != JumpEventKind.None) LastEvent = e;

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
        // Only what can be stepped (or, airborne, reached) onto supports: a solid the disc merely touches is a wall.
        var support = terrain.SupportHeight(pos, radius, feet.Y, allowance, maxRise: allowance);
        var y = feet.Y;
        if (Grounded)
        {
            if (support >= y - _p.SnapDown.Value && support <= y + _p.StepHeight.Value)
            {
                y = support; // flat, step up, or down a stair/ramp
                _jump.StayOnGround();
            }
            else
            {
                _jump.LeaveGround(); // walked off an edge
            }
        }

        if (!Grounded)
        {
            // The arc (JUMP-01, JumpModel): full gravity while rising with Space held, a hang near the apex, a short hop
            // when Space is released early, and slightly stronger gravity on the way down.
            var rise = _jump.IntegrateRiseMeters(dtSeconds, jumpHeld: !input.JumpReleased);
            if (rise > 0)
            {
                // Head clearance: a solid whose underside is above the head stops the rise (no passing up through it).
                var head = y + _p.BodyHeight.Value;
                foreach (var i in terrain.BoxesNear(pos, radius))
                {
                    var b = terrain.Boxes[i];
                    if (b.Bottom >= head - 0.01f && b.Bottom < head + rise && b.Overlaps(pos, radius))
                    {
                        rise = MathF.Max(0, b.Bottom - head);
                        _jump.StopRise();
                        HeadBumps++;
                    }
                }
            }

            y += rise;
            if (y <= support && _jump.VerticalVelocity.Value <= 0)
            {
                y = support;
                LastEvent = _jump.Land();
            }
        }

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
