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

    /// <summary>Footfalls: one every <see cref="SphereWalkParams.Stride"/> walked on the ground (for footstep sounds).</summary>
    public int Footsteps { get; private set; }

    /// <summary>The latest vertical event of the last <see cref="Update"/> (Jump, AirJump, Land with impact speed) or None.</summary>
    public JumpEvent LastEvent { get; private set; }

    /// <summary>The feet in the planet frame, metres.</summary>
    public Vector3 Feet => Surface.PointAt(Up, Height.Value);

    /// <summary>
    /// Places the walker standing at <paramref name="up"/> (any non-zero vector) on <paramref name="surface"/>, on the
    /// highest top under its disc (or the ground), facing <paramref name="facing"/> projected onto the tangent plane.
    /// </summary>
    /// <summary>
    /// Swaps the planet under the walker (the same planet recomposed: a listing arrived) without disturbing it: position,
    /// height, facing, velocity and the jump in progress are kept; the next step stands it on the new support or pushes it
    /// out of a new solid. Teleporting instead stopped the walker and cancelled jumps on every recompose.
    /// </summary>
    public void ChangeSurface(PlanetSurface surface) => _surface = surface;

    public void Teleport(PlanetSurface surface, Vector3 up, Vector3 facing)
    {
        _surface = surface;
        Up = up.LengthSquared() > 0 ? Vector3.Normalize(up) : Vector3.UnitY;
        var f = facing - Up * Vector3.Dot(facing, Up);
        Facing = f.LengthSquared() > 1e-8f ? Vector3.Normalize(f) : PlanetShape.Tangents(Up).A;
        var s = surface.SupportAt(Up, _p.BodyRadius.Value, float.MaxValue, 0);
        Height = Meters.Of(s);
        Support = Meters.Of(s);
        Velocity = Vector3.Zero;
        _jump.Reset();
        LastEvent = JumpEvent.None;
        _accumulatorSeconds = 0;
        _strideMeters = 0;
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

    /// <summary>One fixed step. Plain floats inside: metres, seconds, radians (named).</summary>
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
        velocity = Vector3.Lerp(velocity, wish * speedMetersPerSecond, response.SmoothingFactor(dtSeconds));
        if (grounded && wish.LengthSquared() < 1e-8f && velocity.LengthSquared() < 0.0004f) velocity = Vector3.Zero;

        // Jump along the local up; airborne, the one boost (with the twirl's small lean along the input).
        var e = _jump.BeginStep(dtSeconds, heightMeters - Support.Value);
        if (e.Kind == JumpEventKind.AirJump && wish.LengthSquared() > 1e-8f)
        {
            var dir = Vector3.Normalize(wish);
            var add = MathF.Min(_p.Jump.AirJumpNudge.Value, MathF.Max(0, speedMetersPerSecond - Vector3.Dot(velocity, dir)));
            velocity += dir * add;
        }

        if (e.Kind != JumpEventKind.None) LastEvent = e;
        grounded = _jump.Grounded;
        var allowanceMeters = (grounded ? _p.StepHeight : _p.AirLedgeTolerance).Value;

        // Move along the sphere, keeping the feet's distance from the centre (the ground then decides what happens).
        var feetRadiusMeters = shape.GroundRadius(up) + heightMeters;
        var speed = velocity.Length();
        var movedMeters = 0f;
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
        for (var iter = 0; iter < 4; iter++)
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
            _strideMeters = MathF.Min(_strideMeters, _p.Stride.Value * 0.5f); // the first step after landing comes soon
        }

        // Turn the body towards its motion (or the input while nearly still).
        var faceTarget = velocity.LengthSquared() > 0.01f ? velocity : wish;
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
