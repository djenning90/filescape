using System.Numerics;
using FileScape.Core.Movement;
using FileScape.Core.Units;

namespace FileScape.Core.Planets;

/// <summary>
/// Walking and jumping on a planet (<see cref="SphereWalker"/>). Planet frame: centre at the origin, metres. The
/// vertical motion is the shared <see cref="JumpParams"/>; the rest mirrors the flat walker's feel (WalkParams), a
/// little brisker because planets are small and round. Provisional until DJ has tried them.
/// </summary>
public sealed record SphereWalkParams
{
    /// <summary>Walking speed: 6 m/s, a brisk jog (a lap of a 20 m planet in ≈ 21 s).</summary>
    public MetersPerSecond WalkSpeed { get; init; } = MetersPerSecond.Of(6f);

    /// <summary>Speed while Shift is held.</summary>
    public MetersPerSecond RunSpeed { get; init; } = MetersPerSecond.Of(11f);

    /// <summary>How quickly velocity follows the input on the ground: 14/s reaches 90 % in ≈ 0.16 s.</summary>
    public PerSecond GroundResponse { get; init; } = PerSecond.Of(14f);

    /// <summary>The same in the air: most of the momentum is kept while allowing some steering.</summary>
    public PerSecond AirResponse { get; init; } = PerSecond.Of(2.2f);

    /// <summary>In the air with no input: momentum is almost kept.</summary>
    public PerSecond AirCoast { get; init; } = PerSecond.Of(0.3f);

    /// <summary>Body radius (a disc on the surface, measured along it).</summary>
    public Meters BodyRadius { get; init; } = Meters.Of(0.35f);

    /// <summary>The highest top climbed without jumping (an obstacle top this close above the feet is a step, not a wall).</summary>
    public Meters StepHeight { get; init; } = Meters.Of(0.45f);

    /// <summary>Walking down, the walker stays on the ground over drops up to this instead of falling.</summary>
    public Meters SnapDown { get; init; } = Meters.Of(0.5f);

    /// <summary>While airborne, a top up to this far above the feet can still be moved onto (a forgiving landing).</summary>
    public Meters AirLedgeTolerance { get; init; } = Meters.Of(0.3f);

    /// <summary>How fast the body turns to face its motion: 720°/s turns about in a quarter second.</summary>
    public DegreesPerSecond FacingTurnRate { get; init; } = DegreesPerSecond.Of(720f);

    /// <summary>Distance walked on the ground per footstep (one <see cref="SphereWalker.Footsteps"/> count).</summary>
    public Meters Stride { get; init; } = Meters.Of(0.85f);

    /// <summary>Fixed integration frequency (frame-rate independence): at 11 m/s a step moves 9 cm, well under the body radius.</summary>
    public int SubStepsPerSecond { get; init; } = 120;

    /// <summary>The longest frame integrated at once; a longer stall (a hitch, a breakpoint) is dropped, not replayed.</summary>
    public TimeSpan MaxFrame { get; init; } = TimeSpan.FromSeconds(0.25);

    /// <summary>Take-off, arc, double jump and landing (the flat walker's defaults).</summary>
    public JumpParams Jump { get; init; } = JumpParams.Default;

    /// <summary>The twirl's timing, the recovery twirl and the last effort on a broken shell (PLANET-62, AD-65); nothing else reads them.</summary>
    public HazardMoveParams Hazard { get; init; } = HazardMoveParams.Default;

    public static readonly SphereWalkParams Default = new();

    /// <summary>Rejects values that would break the integration (non-finite, non-positive sizes or rates).</summary>
    public SphereWalkParams Validate()
    {
        static void Positive(float v, string name)
        {
            if (!float.IsFinite(v) || v <= 0) throw new ArgumentOutOfRangeException(name, v, "must be finite and positive");
        }

        static void NonNegative(float v, string name)
        {
            if (!float.IsFinite(v) || v < 0) throw new ArgumentOutOfRangeException(name, v, "must be finite and not negative");
        }

        Positive(WalkSpeed.Value, nameof(WalkSpeed));
        Positive(RunSpeed.Value, nameof(RunSpeed));
        Positive(BodyRadius.Value, nameof(BodyRadius));
        Positive(FacingTurnRate.Value, nameof(FacingTurnRate));
        Positive(Stride.Value, nameof(Stride));
        Positive(SubStepsPerSecond, nameof(SubStepsPerSecond));
        NonNegative(GroundResponse.Value, nameof(GroundResponse));
        NonNegative(AirResponse.Value, nameof(AirResponse));
        NonNegative(AirCoast.Value, nameof(AirCoast));
        NonNegative(StepHeight.Value, nameof(StepHeight));
        NonNegative(SnapDown.Value, nameof(SnapDown));
        NonNegative(AirLedgeTolerance.Value, nameof(AirLedgeTolerance));
        if (MaxFrame <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(MaxFrame));
        Jump.Validate();
        Hazard.Validate();
        return this;
    }
}

/// <summary>
/// How the twirl works over a Hazard World's open sky (PLANET-62, AD-65; retimed by PLANET-71, AD-69). DJ (2026-11-17):
/// "When I barely miss an edge and start to fall, let a well-timed twirl give me a brief lift and a chance to catch the
/// rim. If I'm too late or too far away, let the attempt read as a last effort before the fall." DJ (2026-11-19): "I
/// couldn't get the late rim catch to work. Once falling, the character seemed completely unresponsive … Adjust the timing
/// and reach so a player can discover and repeat them. During a failed recovery attempt, give the character a visible and
/// audible effort, even if too late." So, over open sky only:
/// <list type="bullet">
/// <item>the long jump: a twirl at the top of the jump (vertical speed within ±<see cref="ApexSpeed"/>) leans forward
/// harder (<see cref="ApexNudge"/> instead of the twirl's usual 1 m/s, up to the run speed): the timed twirl that carries a
/// leap;</item>
/// <item>the rim catch: Space on the way down (faster than <see cref="LateSpeed"/>, or with the feet below a rim's level),
/// or Space in the coyote time of walking off when that coyote jump would only drop into the open sky, with the twirl
/// still unused, within <see cref="Window"/> of the feet passing the rim's level, no deeper than <see cref="MaxDepth"/>,
/// and ground in reach (the rim last stood on within <see cref="ReturnReach"/>, or any ground within <see cref="Reach"/>):
/// a lift to just above the rim (<see cref="Clearance"/>, at least <see cref="MinLift"/>) and a carry that lands
/// <see cref="Inset"/> inside that ground, holding its course (no air steering) until it lands;</item>
/// <item>every other press on the way down over open sky is a last effort: a flail and a cry, no lift, as often as Space
/// is pressed (no dead press), until the fall takes over (the app answers each press during the fall too).</item>
/// </list>
/// One recovery per airtime, and the twirl or the catch, not both. Only a <see cref="SphereWalker"/> on a
/// <see cref="BrokenShell"/> uses these, and only over open sky: whole planets and solid ground keep the twirl as it was
/// (the behaviour baseline's trajectories are unchanged). Values by trial against the real walker (HazardMovementTests,
/// HazardRecoveryTimingTests), 2026-11-17 and 2026-11-19.
/// </summary>
public sealed record HazardMoveParams
{
    /// <summary>
    /// A twirl with the vertical speed within ± this, m/s, is "at the top": ≈ 0.2 s either side of it, up to where a
    /// falling press becomes the catch (<see cref="LateSpeed"/>). 4 m/s (2026-11-19, AD-69; was 3, which left a tapped
    /// long jump a 0.1 s window from the far end of the board).
    /// </summary>
    public MetersPerSecond ApexSpeed { get; init; } = MetersPerSecond.Of(4.0f);

    /// <summary>A twirl at the top leans forward by up to this, m/s (along the input, never beyond the run speed).</summary>
    public MetersPerSecond ApexNudge { get; init; } = MetersPerSecond.Of(2.2f);

    /// <summary>Falling faster than this, m/s, a twirl over open sky is the catch (the recovery or a last effort), not a fresh jump (≈ 0.2 s past the top of a held jump).</summary>
    public MetersPerSecond LateSpeed { get; init; } = MetersPerSecond.Of(4.0f);

    /// <summary>The recovery lifts at least this fast, m/s (from above the rim's level the lift would otherwise be nothing).</summary>
    public MetersPerSecond MinLift { get; init; } = MetersPerSecond.Of(3.0f);

    /// <summary>
    /// The press must come within this long of the feet passing below the rim's level, seconds. Walking off, that is from
    /// the moment the feet leave the rim: "Space within 0.4 s of slipping". 0.4 s (2026-11-19, was 0.3): DJ asked for a
    /// window a player can find and repeat; in the app only ≈ 0.12–0.23 s of the old one was ever reachable (AD-69).
    /// </summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(0.4);

    /// <summary>
    /// No deeper than this below the rim's level, metres. Walking off, the feet are ≈ 1.6 m down 0.4 s after the slip, so
    /// <see cref="Window"/> closes the catch first; after a jump that fell short (falling faster) this closes it sooner.
    /// 1.8 m (2026-11-19, was 1.1, which closed the catch ≈ 0.34 s after a slip).
    /// </summary>
    public Meters MaxDepth { get; init; } = Meters.Of(1.8f);

    /// <summary>
    /// Any ground within this of the walker's centre, measured along the surface, metres, can be caught: the far rim of a
    /// gap barely missed, a rim beside you. 1.5 m (2026-11-17, was 1.1). Kept: 1.8 m let a plain jump saved by the catch
    /// clear a leap (HazardTerrain.LeapMinMetres). The rim you slipped off has its own, longer <see cref="ReturnReach"/>.
    /// </summary>
    public Meters Reach { get; init; } = Meters.Of(1.5f);

    /// <summary>
    /// The rim last stood on can be caught from up to this far, metres along the surface from where the feet last had
    /// support to the walker's centre (2026-11-19, AD-69). Walking off (6 m/s) the walker is ≈ 2.4 m out when
    /// <see cref="Window"/> closes; running (11 m/s) it is out of reach ≈ 0.3 s after the slip. A jump that falls short of a
    /// leap took off ≥ 4 m back, so this never carries one across (HazardMovementTests).
    /// </summary>
    public Meters ReturnReach { get; init; } = Meters.Of(3.2f);

    /// <summary>The lift takes the feet this far above the rim's level at its top, metres (just enough to step back on).</summary>
    public Meters Clearance { get; init; } = Meters.Of(0.35f);

    /// <summary>The carry aims this far inside the ground found, metres, so the walker lands on it rather than on its edge (a 1.2 m bridge's middle is 0.6 m in).</summary>
    public Meters Inset { get; init; } = Meters.Of(0.6f);

    /// <summary>
    /// The carry is never faster than this, metres per second (a lunge, not a dash: the run speed is 11). Below it, its speed
    /// is the way to its aim over the lift's air time, so it lands at the aim rather than past it (2026-11-19, was 6.5 m/s
    /// over a fixed 0.4 s, which overshot a narrow bridge once the reach grew).
    /// </summary>
    public MetersPerSecond MaxCarrySpeed { get; init; } = MetersPerSecond.Of(9f);

    /// <summary>
    /// While the feet are more than this below the rim's level, metres, the carry waits rather than move in under the
    /// ground (the rock is drawn ≈ 0.6 m thick): the lift rises first and the carry swings over the rim.
    /// </summary>
    public Meters CarryUnderRim { get; init; } = Meters.Of(0.15f);

    public static readonly HazardMoveParams Default = new();

    /// <summary>Rejects values that would break the move (non-finite or negative).</summary>
    public HazardMoveParams Validate()
    {
        static void NonNegative(float v, string name)
        {
            if (!float.IsFinite(v) || v < 0) throw new ArgumentOutOfRangeException(name, v, "must be finite and not negative");
        }

        if (Window < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(Window));
        NonNegative(MaxDepth.Value, nameof(MaxDepth));
        NonNegative(Reach.Value, nameof(Reach));
        NonNegative(ReturnReach.Value, nameof(ReturnReach));
        NonNegative(Clearance.Value, nameof(Clearance));
        NonNegative(Inset.Value, nameof(Inset));
        if (!float.IsFinite(MaxCarrySpeed.Value) || MaxCarrySpeed.Value <= 0) throw new ArgumentOutOfRangeException(nameof(MaxCarrySpeed), MaxCarrySpeed.Value, "must be finite and positive");
        NonNegative(CarryUnderRim.Value, nameof(CarryUnderRim));
        NonNegative(ApexSpeed.Value, nameof(ApexSpeed));
        NonNegative(ApexNudge.Value, nameof(ApexNudge));
        NonNegative(LateSpeed.Value, nameof(LateSpeed));
        NonNegative(MinLift.Value, nameof(MinLift));
        return this;
    }
}

/// <summary>
/// One frame of planet walking input.
/// </summary>
/// <param name="Wish">Desired direction in the planet frame (any vector, e.g. the camera's forward × stick): projected onto the tangent plane, length clamped to 1 (shorter is slower).</param>
/// <param name="Run">Shift held.</param>
/// <param name="JumpPressed">Space pressed this frame (an edge).</param>
/// <param name="JumpHeld">Space is down (a rising jump with Space released becomes a short hop).</param>
public readonly record struct SphereWalkInput(Vector3 Wish, bool Run, bool JumpPressed, bool JumpHeld)
{
    public static readonly SphereWalkInput None = default;
}

/// <summary>
/// A kinematic walker on a <see cref="PlanetSurface"/> (centre at the origin), all the way round: no chart, no seam,
/// no pole. Its place is a unit direction <see cref="Up"/> and a <see cref="Height"/> above the ground there; it moves
/// by rotating <see cref="Up"/> about <c>Up × velocity</c>, and the same rotation parallel-transports
/// <see cref="Velocity"/> and <see cref="Facing"/>, so they stay tangent and continuous anywhere (at ±Y and the
/// antipodes too: nothing refers to a fixed axis). Ground follows <see cref="PlanetShape.HeightAt"/>; obstacles are
/// circles on the surface that block and slide, or are stepped / landed onto when low enough. Vertical motion is the
/// shared <see cref="JumpModel"/>. Fixed sub-steps make it frame-rate independent.
/// </summary>
public sealed class SphereWalker
{
    /// <summary>Accumulated time within this much of a whole sub-step counts as one, absorbing frame-length rounding (100 ns ticks).</summary>
    private const float StepToleranceSeconds = 1e-4f;

    /// <summary>
    /// On the ground with no input, a velocity below this (squared, m²/s²: 0.02 m/s) is zeroed, so the exponential
    /// slow-down ends in a stop instead of a creep that never quite finishes.
    /// </summary>
    private const float RestSpeedSquared = 0.0004f;

    /// <summary>Push-out passes per sub-step (rationale for 4 not recorded); an overlap left after the last is met again next sub-step.</summary>
    private const int PushOutIterations = 4;

    /// <summary>Above this speed (squared, m²/s²: 0.1 m/s) the body turns to face its motion; slower, it turns to the input.</summary>
    private const float FaceMotionSpeedSquared = 0.01f;

    /// <summary>In the air the stride counter is held at no more than this fraction of a stride, so the first footstep after landing comes soon.</summary>
    private const float LandingStrideFraction = 0.5f;

    private readonly SphereWalkParams _p;
    private readonly JumpModel _jump;
    private readonly float _stepSeconds, _maxFrameSeconds;

    /// <summary>Frame time not yet integrated, in seconds.</summary>
    private float _accumulatorSeconds;

    /// <summary>Ground distance walked since the last footstep, metres.</summary>
    private float _strideMeters;

    private PlanetSurface? _surface;

    public SphereWalker(SphereWalkParams? parameters = null)
    {
        _p = (parameters ?? SphereWalkParams.Default).Validate();
        _jump = new JumpModel(_p.Jump);
        _stepSeconds = 1f / _p.SubStepsPerSecond;
        _recoveryWindowSeconds = _p.Hazard.Window.ToSecondsF();
        _maxFrameSeconds = _p.MaxFrame.ToSecondsF();
    }

    public SphereWalkParams Params => _p;

    /// <summary>The planet walked on (set by <see cref="Teleport"/>).</summary>
    public PlanetSurface Surface => _surface ?? throw new InvalidOperationException("Teleport the walker onto a planet first.");

    /// <summary>Unit direction from the planet centre to the walker: the local up.</summary>
    public Vector3 Up { get; private set; } = Vector3.UnitY;

    /// <summary>Feet above the local ground radius (includes standing on an obstacle top).</summary>
    public Meters Height { get; private set; }

    /// <summary>Unit tangent the body faces; it turns smoothly towards the motion.</summary>
    public Vector3 Facing { get; private set; } = -Vector3.UnitZ;

    /// <summary>Tangential velocity, metres per second (planet frame).</summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>Speed along the local up, positive upwards.</summary>
    public MetersPerSecond VerticalVelocity => _jump.VerticalVelocity;

    public bool Grounded => _jump.Grounded;

    /// <summary>Height above the local ground of what is under the walker (0 = the ground, else an obstacle top).</summary>
    public Meters Support { get; private set; }

    public int Jumps => _jump.Jumps;
    public int AirJumps => _jump.AirJumps;
    public int Landings => _jump.Landings;

    // ---- the recovery twirl on a broken shell (PLANET-62, AD-65; HazardMoveParams). On a whole planet none of this runs.

    /// <summary>Recovery twirls made (Space below a rim, in time and in reach: a lift back towards the ground).</summary>
    public int Recoveries { get; private set; }

    /// <summary>Twirls at the top of a jump over a broken shell's open sky (the timed twirl that leans harder).</summary>
    public int ApexTwirls { get; private set; }

    /// <summary>Last efforts made (Space on the way down over open sky with no catch to make: too late, too far, or already spent; no lift). Every such press counts.</summary>
    public int LastEfforts { get; private set; }

    /// <summary>Seconds since the feet went below a rim's level over open sky in this airtime, or −1 when they are not below one.</summary>
    public float BelowRimSeconds => _belowRimSeconds;

    /// <summary>
    /// A press now could still be a recovery: below a rim, within its window and depth, the twirl unused, none made yet
    /// this airtime, and (once deeper than <see cref="PlanetSurface.FallThroughMetres"/>, where it decides when the fall
    /// starts) ground in reach.
    /// </summary>
    public bool RecoveryOpen => !_recoverySpent && _jump.AirJumpsLeft > 0 && _belowRimSeconds >= 0 && _belowRimSeconds <= _recoveryWindowSeconds
        && -Height.Value <= _p.Hazard.MaxDepth.Value && _catchInReach;

    /// <summary>The recovery's lift and carry are under way (set by the recovery, cleared on landing or a teleport): no air steering, and no fall while it rises.</summary>
    public bool Carrying => _carrying;

    /// <summary>
    /// On a broken shell, the walker has fallen past saving: more than <see cref="PlanetSurface.FallThroughMetres"/> below
    /// the rim, no recovery still open and no recovery lift still rising. The app starts the fall here (PlanetWorld.Hazard,
    /// since AD-69; before, it started at the depth alone, ≈ 0.23 s after a slip, and cut the catch's window short). Never
    /// true on a whole planet.
    /// </summary>
    public bool FallCommitted => _surface?.Shell != null && !Grounded && Height.Value < -PlanetSurface.FallThroughMetres
        && !RecoveryOpen && !(_carrying && _jump.VerticalVelocity.Value > 0);

    private float _belowRimSeconds = -1;
    private bool _recoverySpent, _carrying;

    /// <summary>Whether a catch would find ground now (kept up to date only while the feet are deeper than FallThroughMetres and the window is open; true otherwise).</summary>
    private bool _catchInReach = true;

    /// <summary>Where the feet last had support (unit up): the rim a slip is caught back onto (<see cref="HazardMoveParams.ReturnReach"/>).</summary>
    private Vector3 _lastGroundUp = Vector3.UnitY;

    private readonly float _recoveryWindowSeconds;

    /// <summary>Footfalls: one every <see cref="SphereWalkParams.Stride"/> walked on the ground (for footstep sounds).</summary>
    public int Footsteps { get; private set; }

    /// <summary>The latest vertical event of the last <see cref="Update"/> (Jump, AirJump, Land with impact speed) or None.</summary>
    public JumpEvent LastEvent { get; private set; }

    /// <summary>The feet in the planet frame, metres.</summary>
    public Vector3 Feet => Surface.PointAt(Up, Height.Value);

    /// <summary>
    /// Swaps the planet under the walker (the same planet recomposed: a listing arrived) without disturbing it: position,
    /// height, facing, velocity and the jump in progress are kept; the next step stands it on the new support or pushes it
    /// out of a new solid. Teleporting instead stopped the walker and cancelled jumps on every recompose.
    /// </summary>
    public void ChangeSurface(PlanetSurface surface) => _surface = surface;

    /// <summary>
    /// Places the walker standing at <paramref name="up"/> (any non-zero vector) on <paramref name="surface"/>, on the
    /// highest top under its disc (or the ground), facing <paramref name="facing"/> projected onto the tangent plane.
    /// Stops it: no velocity, no jump in progress, no pending press, and the sub-step and stride accumulators cleared
    /// (arrivals and warps; a recompose uses <see cref="ChangeSurface"/> instead).
    /// </summary>
    public void Teleport(PlanetSurface surface, Vector3 up, Vector3 facing)
    {
        _surface = surface;
        Up = up.LengthSquared() > 0 ? Vector3.Normalize(up) : Vector3.UnitY;
        var f = facing - Up * Vector3.Dot(facing, Up);
        Facing = f.LengthSquared() > 1e-8f ? Vector3.Normalize(f) : PlanetShape.Tangents(Up).A;
        // Over a hole (a hop's arc across a broken world) it stands at the rim's level, not on "no ground" 1 km down; the
        // next step finds nothing under it and it falls. On whole planets s is never negative, so this changes nothing.
        var s = MathF.Max(0, surface.SupportAt(Up, _p.BodyRadius.Value, float.MaxValue, 0));
        Height = Meters.Of(s);
        Support = Meters.Of(s);
        Velocity = Vector3.Zero;
        _jump.Reset();
        LastEvent = JumpEvent.None;
        _accumulatorSeconds = 0;
        _strideMeters = 0;
        _belowRimSeconds = -1;
        _recoverySpent = false;
        _carrying = false;
        _catchInReach = true;
        _lastGroundUp = Up;
    }

    /// <summary>Integrates one frame of <paramref name="frame"/> length in fixed sub-steps.</summary>
    public void Update(SphereWalkInput input, TimeSpan frame)
    {
        var surface = Surface;
        if (input.JumpPressed) _jump.Press();
        LastEvent = JumpEvent.None;
        _accumulatorSeconds += Math.Clamp(frame.ToSecondsF(), 0, _maxFrameSeconds);
        while (_accumulatorSeconds >= _stepSeconds - StepToleranceSeconds)
        {
            _accumulatorSeconds -= _stepSeconds;
            Step(surface, input, _stepSeconds);
        }
    }

    /// <summary>
    /// One fixed step. Plain floats inside: metres, seconds, radians (named). In order: steer the tangential velocity
    /// towards the input (exponential smoothing, <see cref="PerSecond.SmoothingFactor"/>); take off or boost; move along
    /// the sphere at constant feet radius; push out of solids (sliding); settle on the support or fly the arc and land;
    /// count footsteps; turn the body; re-orthonormalise. The order matters: the push-out uses the height the move left,
    /// and landing uses the support found after the push-out.
    /// </summary>
    private void Step(PlanetSurface surface, SphereWalkInput input, float dtSeconds)
    {
        var shape = surface.Shape;
        var up = Up;
        var velocity = Velocity;
        var facing = Facing;
        var heightMeters = Height.Value;
        var bodyRadiusMeters = _p.BodyRadius.Value;

        // Desired tangential velocity.
        var wish = input.Wish - up * Vector3.Dot(input.Wish, up);
        if (!float.IsFinite(wish.X + wish.Y + wish.Z)) wish = Vector3.Zero;
        if (wish.LengthSquared() > 1) wish = Vector3.Normalize(wish);
        var speedMetersPerSecond = (input.Run ? _p.RunSpeed : _p.WalkSpeed).Value;
        var grounded = _jump.Grounded;
        var response = grounded ? _p.GroundResponse : _p.AirResponse;
        if (!grounded && wish.LengthSquared() < 1e-8f) response = _p.AirCoast;
        // The recovery's carry holds its course: steering on (W still held from the walk off) would take it back out.
        if (!_carrying) velocity = Vector3.Lerp(velocity, wish * speedMetersPerSecond, response.SmoothingFactor(dtSeconds));
        if (grounded && wish.LengthSquared() < 1e-8f && velocity.LengthSquared() < RestSpeedSquared) velocity = Vector3.Zero;

        // Jump along the local up; airborne, the one boost (with the twirl's small lean along the input).
        // On a broken shell, a press below a rim is the recovery twirl or a last effort instead of a twirl.
        var special = surface.Shell is { } shell ? RecoveryPress(shell, up, ref velocity, heightMeters, dtSeconds) : JumpEvent.None;
        var verticalBeforeMetersPerSecond = _jump.VerticalVelocity.Value;
        var e = _jump.BeginStep(dtSeconds, heightMeters - Support.Value);
        if (special.Kind != JumpEventKind.None) e = special;
        if (e.Kind == JumpEventKind.AirJump && wish.LengthSquared() > 1e-8f)
        {
            var dir = Vector3.Normalize(wish);
            // Over a broken shell's open sky, a twirl at the top of the jump leans harder, up to the run speed (AD-65).
            var apex = surface.Shell is { } open && MathF.Abs(verticalBeforeMetersPerSecond) <= _p.Hazard.ApexSpeed.Value && open.OverHole(up);
            var nudge = apex ? _p.Hazard.ApexNudge.Value : _p.Jump.AirJumpNudge.Value;
            var cap = apex ? _p.RunSpeed.Value : speedMetersPerSecond;
            var add = MathF.Min(nudge, MathF.Max(0, cap - Vector3.Dot(velocity, dir)));
            velocity += dir * add;
            if (apex)
            {
                ApexTwirls++;
                _jump.HoldRise(); // AD-69: the timed twirl carries a tap as far as a hold
            }
        }

        if (e.Kind != JumpEventKind.None) LastEvent = e;
        grounded = _jump.Grounded;
        var allowanceMeters = (grounded ? _p.StepHeight : _p.AirLedgeTolerance).Value;

        // Move along the sphere, keeping the feet's distance from the centre (the ground then decides what happens).
        var feetRadiusMeters = shape.GroundRadius(up) + heightMeters;
        var speed = velocity.Length();
        var movedMeters = 0f;
        // The carry waits while the feet are still deep under the rim's level and the next move would take them in under the ground.
        if (_carrying && surface.Shell is { } carryShell && heightMeters < -_p.Hazard.CarryUnderRim.Value && speed > 1e-6f
            && !carryShell.OverHole(PlanetFrame.Move(up, velocity / speed, speed * dtSeconds + bodyRadiusMeters, shape.Radius)))
        {
            speed = 0;
        }

        if (speed > 1e-6f)
        {
            var angleRadians = speed * dtSeconds / feetRadiusMeters;
            var axis = Vector3.Cross(up, velocity / speed);
            if (axis.LengthSquared() > 1e-12f)
            {
                Transport(ref up, ref velocity, ref facing, Vector3.Normalize(axis), angleRadians);
                movedMeters = speed * dtSeconds;
            }
        }

        heightMeters = feetRadiusMeters - shape.GroundRadius(up);

        // Obstacles: a disc of the body radius is pushed out along the great circle and slides, unless the top can be
        // stepped (grounded) or reached (airborne) onto.
        // Each pass resolves only the deepest overlap and then looks again, so several overlapping solids are settled one
        // by one.
        for (var iter = 0; iter < PushOutIterations; iter++)
        {
            var bestOverlapMeters = 0f;
            var bestAway = Vector3.Zero;
            foreach (var i in surface.Near(up, bodyRadiusMeters))
            {
                var o = surface.Obstacles[i];
                if (o.Top <= heightMeters + allowanceMeters) continue;
                var overlapMeters = o.Radius + bodyRadiusMeters - surface.SurfaceDistance(up, o.Direction);
                if (overlapMeters <= bestOverlapMeters) continue;
                var away = up * Vector3.Dot(up, o.Direction) - o.Direction; // tangent at up, pointing away from the obstacle
                if (away.LengthSquared() < 1e-12f) away = -facing; // exactly at its centre: back out the way we came
                bestOverlapMeters = overlapMeters;
                bestAway = Vector3.Normalize(away);
            }

            if (bestOverlapMeters <= 0) break;
            var into = Vector3.Dot(velocity, bestAway);
            if (into < 0) velocity -= bestAway * into; // slide: drop the component into the obstacle
            var pushFeetRadiusMeters = shape.GroundRadius(up) + heightMeters;
            Transport(ref up, ref velocity, ref facing, Vector3.Normalize(Vector3.Cross(up, bestAway)), bestOverlapMeters / shape.Radius);
            heightMeters = pushFeetRadiusMeters - shape.GroundRadius(up);
        }

        // Vertical: support, staying on the ground (hills, steps down), walking off, the arc and landing.
        var supportMeters = surface.SupportAt(up, bodyRadiusMeters, heightMeters, allowanceMeters);
        if (grounded)
        {
            if (supportMeters >= heightMeters - _p.SnapDown.Value && supportMeters <= heightMeters + _p.StepHeight.Value)
            {
                heightMeters = supportMeters;
                _jump.StayOnGround();
            }
            else
            {
                _jump.LeaveGround();
            }
        }

        if (!_jump.Grounded)
        {
            heightMeters += _jump.IntegrateRiseMeters(dtSeconds, input.JumpHeld);
            if (heightMeters <= supportMeters && _jump.VerticalVelocity.Value <= 0)
            {
                heightMeters = supportMeters;
                LastEvent = _jump.Land();
                if (_carrying)
                {
                    // The catch sticks its landing: the carry's speed would slide the walker off a narrow bridge's far side.
                    _carrying = false;
                    velocity = Vector3.Zero;
                }
            }
        }

        // Footsteps on the ground.
        if (_jump.Grounded)
        {
            _strideMeters += movedMeters;
            var strideMeters = _p.Stride.Value;
            while (_strideMeters >= strideMeters)
            {
                _strideMeters -= strideMeters;
                Footsteps++;
            }
        }
        else
        {
            _strideMeters = MathF.Min(_strideMeters, _p.Stride.Value * LandingStrideFraction); // the first step after landing comes soon
        }

        // Turn the body towards its motion (or the input while nearly still).
        var faceTarget = velocity.LengthSquared() > FaceMotionSpeedSquared ? velocity : wish;
        if (faceTarget.LengthSquared() > 1e-8f)
        {
            faceTarget = Vector3.Normalize(faceTarget);
            var angleRadians = MathF.Atan2(Vector3.Dot(up, Vector3.Cross(facing, faceTarget)), Vector3.Dot(facing, faceTarget));
            var maxRadians = Degrees.Of(_p.FacingTurnRate.Value * dtSeconds).ToRadians();
            var turnRadians = Math.Clamp(angleRadians, -maxRadians, maxRadians);
            facing = facing * MathF.Cos(turnRadians) + Vector3.Cross(up, facing) * MathF.Sin(turnRadians);
        }

        // Keep the frame exact: unit up, tangent velocity and facing.
        up = Vector3.Normalize(up);
        velocity -= up * Vector3.Dot(velocity, up);
        facing -= up * Vector3.Dot(facing, up);
        facing = facing.LengthSquared() > 1e-12f ? Vector3.Normalize(facing) : PlanetShape.Tangents(up).A;

        Up = up;
        Velocity = velocity;
        Facing = facing;
        Height = Meters.Of(heightMeters);
        Support = Meters.Of(supportMeters);

        // Below a rim's level over open sky: the recovery's window runs (a whole planet has no rim, so it never starts).
        if (surface.Shell is { } brokenShell && !_jump.Grounded && heightMeters < 0 && brokenShell.OverHole(up))
        {
            _belowRimSeconds = _belowRimSeconds < 0 ? 0 : _belowRimSeconds + dtSeconds;
            // Deep enough for the fall to start: it waits only while a catch would still find ground (so a run off, out of
            // reach at once, falls as promptly as before). Shallower, nothing is decided and the search is skipped.
            _catchInReach = heightMeters >= -PlanetSurface.FallThroughMetres || _recoverySpent || _jump.AirJumpsLeft == 0
                || _belowRimSeconds > _recoveryWindowSeconds || CatchAim(brokenShell, up, velocity) != null;
        }
        else
        {
            _belowRimSeconds = -1;
            _catchInReach = true;
            if (_jump.Grounded)
            {
                _recoverySpent = false;
                _lastGroundUp = up;
            }
        }
    }

    /// <summary>
    /// A press over open sky on the way down (HazardMoveParams; AD-65, AD-69). Airborne with a press pending, and:
    /// <list type="bullet">
    /// <item>in the coyote time of walking off, the coyote jump stands unless it would only drop into the open sky
    /// (<see cref="CoyoteJumpLands"/>): then the press is the catch (AD-69: Space 0–0.12 s after a slip used to jump the
    /// walker further out);</item>
    /// <item>otherwise over open sky, falling below the rim's level or faster than <see cref="HazardMoveParams.LateSpeed"/>.</item>
    /// </list>
    /// With the twirl unused, no recovery yet, in time (<see cref="HazardMoveParams.Window"/>, <see cref="HazardMoveParams.MaxDepth"/>)
    /// and ground in reach (<see cref="CatchAim"/>), the recovery: a lift to <see cref="HazardMoveParams.Clearance"/> above
    /// the rim (full gravity, so a tap and a hold alike) and a carry onto the ground, replacing the tangential velocity
    /// and holding its course until it lands. Otherwise a last effort: no lift, the twirl spent, and the next press is
    /// another one (never a dead press). Returns None (the ordinary jump and twirl rules) otherwise.
    /// </summary>
    private JumpEvent RecoveryPress(BrokenShell shell, Vector3 up, ref Vector3 velocity, float heightMeters, float dtSeconds)
    {
        if (_jump.Grounded || !_jump.PressPending || _carrying) return JumpEvent.None;
        var rp = _p.Hazard;
        if (!shell.OverHole(up)) return JumpEvent.None; // over ground: the jump, the buffered jump or the twirl
        if (_jump.CanTakeOff(dtSeconds))
        {
            if (CoyoteJumpLands(shell, up, velocity, heightMeters)) return JumpEvent.None; // a late jump that makes it: the coyote jump
        }
        else
        {
            var vy = _jump.VerticalVelocity.Value;
            if (!(heightMeters < 0 && vy < 0) && vy >= -rp.LateSpeed.Value) return JumpEvent.None; // rising or at the top: the twirl, if left
        }

        var depthMeters = MathF.Max(0, -heightMeters);
        var inTime = !_recoverySpent && _jump.AirJumpsLeft > 0 && depthMeters <= rp.MaxDepth.Value && _belowRimSeconds <= _recoveryWindowSeconds;
        var aim = inTime ? CatchAim(shell, up, velocity) : null;
        _recoverySpent = true;
        if (aim is not { } found)
        {
            LastEfforts++;
            return _jump.SpendPress(JumpEventKind.LastEffort, 0);
        }

        // The lift: v² = 2 g h takes the feet from the depth to the clearance above the rim (the apex hang adds a little).
        var liftMetersPerSecond = MathF.Max(rp.MinLift.Value, MathF.Sqrt(2 * _p.Jump.Gravity.Value * MathF.Max(0, rp.Clearance.Value - heightMeters)));
        // The carry: onto the ground, the way there over the lift's air time (so it lands at the aim, not past it).
        var airSeconds = MathF.Max(dtSeconds, AirSeconds(heightMeters, liftMetersPerSecond, riseHeld: true));
        velocity = found.Direction * MathF.Min(rp.MaxCarrySpeed.Value, found.Metres / airSeconds);
        _carrying = true;
        Recoveries++;
        return _jump.SpendPress(JumpEventKind.Recovery, liftMetersPerSecond);
    }

    /// <summary>
    /// Where a catch from <paramref name="up"/> would carry the walker: towards any ground within
    /// <see cref="HazardMoveParams.Reach"/> (ahead along <paramref name="heading"/> preferred: <see cref="BrokenShell.NearestGround"/>),
    /// else back to the rim last stood on if within <see cref="HazardMoveParams.ReturnReach"/>; the tangent direction and
    /// the carry's length to <see cref="HazardMoveParams.Inset"/> inside that ground, metres. Null when nothing is in reach.
    /// </summary>
    private (Vector3 Direction, float Metres)? CatchAim(BrokenShell shell, Vector3 up, Vector3 heading)
    {
        var rp = _p.Hazard;
        if (shell.NearestGround(up, rp.Reach.Value, heading) is { } near)
        {
            var t = near.Point - up * Vector3.Dot(near.Point, up);
            if (t.LengthSquared() > 1e-10f) return (Vector3.Normalize(t), near.Metres + rp.Inset.Value);
        }

        var backMetres = PlanetShape.Angle(up, _lastGroundUp) * shell.Radius;
        if (backMetres > rp.ReturnReach.Value) return null;
        var back = _lastGroundUp - up * Vector3.Dot(_lastGroundUp, up);
        return back.LengthSquared() > 1e-10f ? (Vector3.Normalize(back), backMetres + rp.Inset.Value) : null;
    }

    /// <summary>
    /// A coyote jump counts as making it only with ground this far short of where a held one would come down, metres: the
    /// prediction is a held, unsteered jump, and a tap (a short hop) or air drag lands metres shorter (a running tap at a
    /// 9 m gap predicted to land fell 2 m short, 2026-11-19).
    /// </summary>
    private const float CoyoteLandMarginMetres = 1.0f;

    /// <summary>
    /// Would a coyote jump now (a held jump from <paramref name="heightMeters"/> at the current tangential velocity, not
    /// steered) come down on ground, with <see cref="CoyoteLandMarginMetres"/> to spare? Used only over open sky within the
    /// coyote time (AD-69): a late jump at a gap it clears stays a jump; one that would drop into the open sky is a slip.
    /// </summary>
    private bool CoyoteJumpLands(BrokenShell shell, Vector3 up, Vector3 velocity, float heightMeters)
    {
        var seconds = AirSeconds(heightMeters, _p.Jump.JumpSpeed.Value, riseHeld: true);
        var speed = velocity.Length();
        var metres = speed * seconds - CoyoteLandMarginMetres;
        if (speed < 1e-4f || metres <= 0) return false; // (nearly) straight up and back down into the same open sky
        return !shell.OverHole(PlanetFrame.Move(up, velocity / speed, metres, shell.Radius));
    }

    /// <summary>Simulation step for <see cref="AirSeconds"/>, seconds (240 Hz: finer than the walker's own steps, cheap enough for a press).</summary>
    private const float AirPredictStepSeconds = 1f / 240;

    /// <summary>Longest air time <see cref="AirSeconds"/> looks ahead, seconds (a held jump lasts ≈ 0.8 s).</summary>
    private const float AirPredictMaxSeconds = 3f;

    /// <summary>
    /// How long from now until the feet, rising at <paramref name="upMetersPerSecond"/> from <paramref name="heightMeters"/>,
    /// come back down to the rim's level (height 0), seconds, by the shared jump's gravity law (held rise with its apex
    /// hang when <paramref name="riseHeld"/>, the fall at its scale).
    /// </summary>
    private float AirSeconds(float heightMeters, float upMetersPerSecond, bool riseHeld)
    {
        var j = _p.Jump;
        var h = heightMeters;
        var v = upMetersPerSecond;
        var t = 0f;
        while (t < AirPredictMaxSeconds && !(v < 0 && h <= 0))
        {
            var g = j.Gravity.Value;
            if (v > 0 && !riseHeld) g *= j.ShortHopGravityScale;
            else if (MathF.Abs(v) < j.ApexHangSpeed.Value && riseHeld) g *= j.ApexGravityScale;
            else if (v < 0) g *= j.FallGravityScale;
            v -= g * AirPredictStepSeconds;
            h += v * AirPredictStepSeconds;
            t += AirPredictStepSeconds;
        }

        return t;
    }

    /// <summary>Rotates the walker's frame (position direction, velocity, facing) about a unit axis through the centre.</summary>
    private static void Transport(ref Vector3 up, ref Vector3 velocity, ref Vector3 facing, Vector3 axis, float angleRadians)
    {
        var cos = MathF.Cos(angleRadians);
        var sin = MathF.Sin(angleRadians);
        up = Rotate(up, axis, cos, sin);
        velocity = Rotate(velocity, axis, cos, sin);
        facing = Rotate(facing, axis, cos, sin);
    }

    /// <summary>Rodrigues' rotation of <paramref name="v"/> about the unit <paramref name="axis"/> (given the angle's cosine and sine).</summary>
    private static Vector3 Rotate(Vector3 v, Vector3 axis, float cos, float sin) =>
        v * cos + Vector3.Cross(axis, v) * sin + axis * (Vector3.Dot(axis, v) * (1 - cos));
}
