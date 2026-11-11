using FileScape.Core.Units;

namespace FileScape.Core.Movement;

/// <summary>
/// The vertical half of walking: take-off, the arc and landing, shared by the flat <see cref="Places.Walker"/> and the
/// <see cref="Planets.SphereWalker"/> (one model, two surfaces). "Up" is world +Y for the first and the local up of the
/// planet for the second; heights and speeds are metres and metres per second along it, positive upwards.
/// Provisional until DJ has tried them (KD-20); each note says what changing it does.
/// </summary>
public sealed record JumpParams
{
    /// <summary>Downward acceleration (a positive magnitude). With <see cref="JumpSpeed"/> it sets the jump's height and air time.</summary>
    public MetersPerSecondSquared Gravity { get; init; } = MetersPerSecondSquared.Of(17f);

    /// <summary>Upward take-off speed: with the apex hang, a held jump peaks at ≈ 1.22 m and lasts ≈ 0.78 s on the flat.</summary>
    public MetersPerSecond JumpSpeed { get; init; } = MetersPerSecond.Of(6.4f);

    /// <summary>
    /// Near the top of a held jump (vertical speed below this), gravity is reduced by <see cref="ApexGravityScale"/>: a
    /// brief hang that makes the arc feel floaty and easier to steer onto a ledge. Zero disables it.
    /// </summary>
    public MetersPerSecond ApexHangSpeed { get; init; } = MetersPerSecond.Of(1.2f);

    /// <summary>Gravity multiplier (dimensionless) during the apex hang.</summary>
    public float ApexGravityScale { get; init; } = 0.65f;

    /// <summary>Rising with Space already released: gravity × this, so a tap is a short hop and a hold a full jump.</summary>
    public float ShortHopGravityScale { get; init; } = 2.2f;

    /// <summary>Falling: gravity × this, so landings come a little sooner than the rise.</summary>
    public float FallGravityScale { get; init; } = 1.15f;

    /// <summary>A jump still works this long after walking off a ledge (forgiving timing).</summary>
    public TimeSpan CoyoteTime { get; init; } = TimeSpan.FromSeconds(0.12);

    /// <summary>A jump pressed this long before landing happens on landing (forgiving timing).</summary>
    public TimeSpan JumpBuffer { get; init; } = TimeSpan.FromSeconds(0.15);

    /// <summary>
    /// Deliberate airborne boosts per airtime (the double jump), re-armed on landing. 0 disables it; 1 is the design
    /// (one boost, never a flight).
    /// </summary>
    public int AirJumps { get; init; } = 1;

    /// <summary>
    /// The vertical speed a boost sets (it replaces the current one, so its timing hardly matters). 7.1 m/s: a boost at
    /// the apex of a held jump peaks ≈ 2.7 m above the take-off, against ≈ 1.2 m for the single jump.
    /// </summary>
    public MetersPerSecond AirJumpSpeed { get; init; } = MetersPerSecond.Of(7.1f);

    /// <summary>
    /// Falling within this of the support below, a press is kept for the landing (the jump buffer) instead of spending
    /// the boost: a press meant as "jump again as I land" is not mistaken for a double jump.
    /// </summary>
    public Meters AirJumpGroundClearance { get; init; } = Meters.Of(0.35f);

    /// <summary>
    /// A boost adds this much speed along the movement input (if there is any, and never beyond the target speed): the
    /// small forward lean of the twirl. Applied by the walkers, which own horizontal motion.
    /// </summary>
    public MetersPerSecond AirJumpNudge { get; init; } = MetersPerSecond.Of(1.0f);

    public static readonly JumpParams Default = new();

    /// <summary>Rejects values that would break the integration.</summary>
    public JumpParams Validate()
    {
        static void NonNegative(float v, string name)
        {
            if (!float.IsFinite(v) || v < 0) throw new ArgumentOutOfRangeException(name, v, "must be finite and not negative");
        }

        if (!float.IsFinite(Gravity.Value) || Gravity.Value <= 0) throw new ArgumentOutOfRangeException(nameof(Gravity), Gravity.Value, "must be finite and positive");
        NonNegative(JumpSpeed.Value, nameof(JumpSpeed));
        NonNegative(ApexHangSpeed.Value, nameof(ApexHangSpeed));
        NonNegative(ApexGravityScale, nameof(ApexGravityScale));
        NonNegative(ShortHopGravityScale, nameof(ShortHopGravityScale));
        NonNegative(FallGravityScale, nameof(FallGravityScale));
        NonNegative(AirJumpSpeed.Value, nameof(AirJumpSpeed));
        NonNegative(AirJumpGroundClearance.Value, nameof(AirJumpGroundClearance));
        NonNegative(AirJumpNudge.Value, nameof(AirJumpNudge));
        if (AirJumps < 0) throw new ArgumentOutOfRangeException(nameof(AirJumps), AirJumps, "must not be negative");
        if (CoyoteTime < TimeSpan.Zero || JumpBuffer < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(CoyoteTime), "durations must not be negative");
        return this;
    }
}

/// <summary>What happened to the vertical motion in a step (or, on a walker, in its latest frame).</summary>
public enum JumpEventKind
{
    None,

    /// <summary>A take-off from the ground (or within coyote time of leaving it).</summary>
    Jump,

    /// <summary>The airborne boost (double jump): consumers play a twirl and a sound.</summary>
    AirJump,

    /// <summary>The feet met a support while falling.</summary>
    Land,
}

/// <param name="Kind">What happened.</param>
/// <param name="ImpactSpeed">For <see cref="JumpEventKind.Land"/>: the downward speed at touch-down (a positive magnitude); otherwise zero.</param>
public readonly record struct JumpEvent(JumpEventKind Kind, MetersPerSecond ImpactSpeed = default)
{
    public static readonly JumpEvent None = default;
}

/// <summary>
/// The vertical state machine: coyote time, jump buffer, held vs released (short hop), apex hang, fall gravity scale
/// and one forgiving airborne boost (the double jump). It knows nothing of terrain: the walker tells it when it stands,
/// walks off, bumps its head or lands, and moves the feet by what <see cref="IntegrateRiseMeters"/> returns.
/// <para>
/// One fixed sub-step is: <see cref="BeginStep"/> (timers, take-off, boost), then either <see cref="StayOnGround"/> or
/// <see cref="LeaveGround"/>, and while airborne <see cref="IntegrateRiseMeters"/> and possibly <see cref="StopRise"/>
/// or <see cref="Land"/>. <see cref="Press"/> registers a Space press (an edge) once per frame, before its sub-steps.
/// </para>
/// </summary>
public sealed class JumpModel
{
    private const float NotRecentlySeconds = 999f;

    private readonly JumpParams _p;

    // Durations converted once to float seconds for the per-sub-step arithmetic.
    private readonly float _coyoteSeconds, _jumpBufferSeconds;

    /// <summary>Time since the walker last stood on something, in seconds (drives coyote time).</summary>
    private float _sinceGroundedSeconds = NotRecentlySeconds;

    /// <summary>Time since Space was last pressed and not yet used, in seconds (drives the buffer and the boost).</summary>
    private float _sinceJumpPressedSeconds = NotRecentlySeconds;

    /// <summary>Vertical speed, metres per second, positive upwards.</summary>
    private float _verticalMetersPerSecond;

    public JumpModel(JumpParams? parameters = null)
    {
        _p = (parameters ?? JumpParams.Default).Validate();
        _coyoteSeconds = _p.CoyoteTime.ToSecondsF();
        _jumpBufferSeconds = _p.JumpBuffer.ToSecondsF();
        AirJumpsLeft = _p.AirJumps;
    }

    public JumpParams Params => _p;

    public bool Grounded { get; private set; } = true;

    /// <summary>Vertical speed along the local up, positive upwards.</summary>
    public MetersPerSecond VerticalVelocity => MetersPerSecond.Of(_verticalMetersPerSecond);

    /// <summary>Take-offs from the ground (including coyote and buffered jumps).</summary>
    public int Jumps { get; private set; }

    /// <summary>Airborne boosts (double jumps).</summary>
    public int AirJumps { get; private set; }

    public int Landings { get; private set; }

    /// <summary>Boosts still available in this airtime (re-armed on landing).</summary>
    public int AirJumpsLeft { get; private set; }

    /// <summary>The event of the latest sub-step (None, Jump, AirJump or Land).</summary>
    public JumpEvent LastEvent { get; private set; }

    /// <summary>Starts over standing (a teleport): no motion, no pending press, the boost armed. Counters are kept.</summary>
    public void Reset()
    {
        Grounded = true;
        _verticalMetersPerSecond = 0;
        _sinceGroundedSeconds = 0;
        _sinceJumpPressedSeconds = NotRecentlySeconds;
        AirJumpsLeft = _p.AirJumps;
        LastEvent = JumpEvent.None;
    }

    /// <summary>Space was pressed (an edge) this frame.</summary>
    public void Press() => _sinceJumpPressedSeconds = 0;

    /// <summary>
    /// Starts one sub-step: advances the timers and takes off if a press is pending — from the ground (or within coyote
    /// time), or, airborne, as the boost. <paramref name="clearanceMeters"/> is the feet's height above the support
    /// below (it keeps a press just before landing for the buffer). Returns (and records) the step's event.
    /// </summary>
    public JumpEvent BeginStep(float dtSeconds, float clearanceMeters)
    {
        LastEvent = JumpEvent.None;
        _sinceJumpPressedSeconds += dtSeconds;
        _sinceGroundedSeconds = Grounded ? 0 : _sinceGroundedSeconds + dtSeconds;
        if (_sinceJumpPressedSeconds > _jumpBufferSeconds) return LastEvent;

        if (_sinceGroundedSeconds <= _coyoteSeconds && _verticalMetersPerSecond <= 0.01f)
        {
            _verticalMetersPerSecond = _p.JumpSpeed.Value;
            Grounded = false;
            _sinceGroundedSeconds = _coyoteSeconds + 1;
            _sinceJumpPressedSeconds = NotRecentlySeconds;
            Jumps++;
            return LastEvent = new JumpEvent(JumpEventKind.Jump);
        }

        // A press that the landing will reach within the jump buffer stays a (buffered) ground jump: the boost must not
        // take over the forgiving buffer (2026-11-11 review: at 7–9 m/s a fixed 0.35 m left only the last 0.05 s).
        var landingWithinBuffer = -_verticalMetersPerSecond * _jumpBufferSeconds;
        var aboutToLand = _verticalMetersPerSecond < 0 && clearanceMeters < MathF.Max(_p.AirJumpGroundClearance.Value, landingWithinBuffer);
        if (!Grounded && AirJumpsLeft > 0 && !aboutToLand)
        {
            _verticalMetersPerSecond = _p.AirJumpSpeed.Value;
            _sinceJumpPressedSeconds = NotRecentlySeconds;
            AirJumpsLeft--;
            AirJumps++;
            return LastEvent = new JumpEvent(JumpEventKind.AirJump);
        }

        return LastEvent;
    }

    /// <summary>Grounded and supported this step: no vertical motion.</summary>
    public void StayOnGround() => _verticalMetersPerSecond = 0;

    /// <summary>The ground fell away (walked off an edge): airborne, coyote time starts.</summary>
    public void LeaveGround() => Grounded = false;

    /// <summary>
    /// Airborne: applies this step's gravity and returns the vertical displacement in metres (positive up). The arc:
    /// full gravity while rising with Space held, a hang near the apex, a short hop when Space is released early, and
    /// slightly stronger gravity on the way down.
    /// </summary>
    public float IntegrateRiseMeters(float dtSeconds, bool jumpHeld)
    {
        var g = _p.Gravity.Value;
        if (_verticalMetersPerSecond > 0 && !jumpHeld) g *= _p.ShortHopGravityScale;
        else if (MathF.Abs(_verticalMetersPerSecond) < _p.ApexHangSpeed.Value && jumpHeld) g *= _p.ApexGravityScale;
        else if (_verticalMetersPerSecond < 0) g *= _p.FallGravityScale;
        _verticalMetersPerSecond -= g * dtSeconds;
        return _verticalMetersPerSecond * dtSeconds;
    }

    /// <summary>The head met something while rising: the rise stops.</summary>
    public void StopRise() => _verticalMetersPerSecond = 0;

    /// <summary>The feet met a support while falling: grounded, the boost re-armed. Returns (and records) the Land event.</summary>
    public JumpEvent Land()
    {
        var impact = MetersPerSecond.Of(MathF.Max(0, -_verticalMetersPerSecond));
        _verticalMetersPerSecond = 0;
        Grounded = true;
        AirJumpsLeft = _p.AirJumps;
        Landings++;
        return LastEvent = new JumpEvent(JumpEventKind.Land, impact);
    }
}
