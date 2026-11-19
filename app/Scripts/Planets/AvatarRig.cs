using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// What the traveller is doing this frame, for <see cref="AvatarRig.Animate"/>. Events (<see cref="Jumped"/>,
/// <see cref="AirJumped"/>, <see cref="Landed"/>) are true for the one frame they happen.
/// </summary>
/// <param name="SpeedMetersPerSecond">Ground speed along the surface, m/s.</param>
/// <param name="Grounded">Feet on the ground (or a top).</param>
/// <param name="VerticalSpeedMetersPerSecond">Speed along the local up, m/s (up is positive).</param>
/// <param name="Jumped">Left the ground this frame.</param>
/// <param name="AirJumped">Jumped again in the air this frame (plays the twirl).</param>
/// <param name="Landed">Touched down this frame.</param>
/// <param name="WindUp01">Launcher wind-up, 0 (none) to 1 (about to launch): a crouch that deepens.</param>
/// <param name="Flying">Travelling between planets: the streamlined pose.</param>
/// <param name="Twirl">Ask for a twirl (as on an air jump) without the event.</param>
/// <param name="Flail">A last effort this frame (PLANET-62): arms thrown up, legs kicking, a lean back — the reach that misses.</param>
public readonly record struct AvatarState(
    float SpeedMetersPerSecond = 0,
    bool Grounded = true,
    float VerticalSpeedMetersPerSecond = 0,
    bool Jumped = false,
    bool AirJumped = false,
    bool Landed = false,
    float WindUp01 = 0,
    bool Flying = false,
    bool Twirl = false,
    bool Flail = false);

/// <summary>
/// The traveller (original character, "Lantern Cosmos"): a small rounded figure ≈ 1.1 m tall — a cloak-like body, a
/// round head with a simple friendly face, a little lantern on an antenna that glows and sways, a scarf ribbon that
/// trails behind with spring physics, two little feet and two floating mitts. Origin at the feet; local +Y up, front −Z.
/// Animation is procedural: a running bob with swinging feet, squash and stretch on jumping and landing, a full twirl
/// on an air jump, a crouch during a launcher wind-up, and a streamlined flying pose with the scarf streaming.
/// Readable from a chase camera 6–8 m away: big head, contrasting cloak and scarf, a bright lantern.
/// <para>
/// It only shows: <see cref="PlanetWorld"/> moves it (<see cref="SetPose"/>, or <c>SetFlying</c> in flight) and says what
/// happened (AvatarRig.Events.cs); nothing here affects movement or collision. The node tree is Lean (tilt and twirl,
/// pivoting at the body's middle) → Squash (scale about the feet) → Bob (the running bob), so each effect composes with
/// the others; the scarf is top-level (world space), simulated as a rope.
/// </para>
/// All dimensions are metres in the rig's frame; the shapes in <c>Build</c> are modelled by eye. The animation's
/// smoothed amounts ease with 1 − e^(−rate·dt), rates per second (docs/units.md); the springs are mass-spring-dampers
/// stepped with semi-implicit Euler (stiffness per s², damping per s), so their feel depends on both numbers together.
/// </summary>
public partial class AvatarRig : Node3D
{
    /// <summary>Standing height without the antenna, metres.</summary>
    public const float HeightMetres = 1.1f;

    private static readonly Color Cloak = new(0.30f, 0.36f, 0.86f);
    private static readonly Color CloakTrim = new(0.98f, 0.86f, 0.52f);
    private static readonly Color Skin = new(1.00f, 0.93f, 0.84f);
    private static readonly Color ScarfColor = new(1.00f, 0.46f, 0.36f);
    private static readonly Color LanternColor = new(1.00f, 0.80f, 0.42f);
    private static readonly Color Dark = new(0.14f, 0.12f, 0.24f);

    private readonly Node3D _lean = new() { Name = "Lean" };
    private readonly Node3D _squash = new() { Name = "Squash" };
    private readonly Node3D _bob = new() { Name = "Bob" };
    private readonly Node3D _head = new() { Name = "Head" };
    private readonly Node3D _antenna = new() { Name = "Antenna" };
    private readonly Node3D _eyes = new() { Name = "Eyes" };
    private readonly Node3D _footL = new() { Name = "FootL" }, _footR = new() { Name = "FootR" };
    private readonly Node3D _handL = new() { Name = "HandL" }, _handR = new() { Name = "HandR" };
    private readonly MeshInstance3D _scarf = new() { Name = "Scarf", TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    private readonly ImmediateMesh _scarfMesh = new();
    private OmniLight3D _light = null!;
    private MeshInstance3D _lanternCore = null!;

    // Pose and motion
    private Vector3 _up = Vector3.Up, _facing = Vector3.Forward;
    private Vector3 _lastPos;
    private Vector3 _velocity;
    private bool _havePose;

    // Animation state (seconds and dimensionless amounts)
    private float _timeSeconds;
    private float _phase;              // stride phase, radians
    private float _run;                // 0..1
    private float _fly;                // 0..1
    private float _wind;               // 0..1 (smoothed)
    private float _air;                // 0..1 (smoothed "not grounded")
    private float _squashAmount;       // + = stretched
    private float _squashVelocity;     // per second
    private float _twirlSeconds = -1;  // time since the twirl began, or −1
    private float _sway, _swayVelocity; // antenna sway, radians and radians per second
    private float _blinkSeconds = 2.5f;
    private float _vertical;           // smoothed vertical speed, m/s

    private const float TwirlDurationSeconds = 0.5f;

    // ---- the last effort (PLANET-62, AD-65; by eye): a flail this long, arms waving this fast (rad/s, ≈ 3 Hz), leaning back this far (degrees)
    // PLANET-72, AD-69 (2026-11-19, DJ: "pressing Space produced no visible effort"): leaning back 26° (was 18°) and a
    // hop of the body this high, metres, over the flail's first half (a sine hump; the pose only, the walker's path is
    // unchanged), so the effort reads from the chase and fall cameras.
    private const float FlailDurationSeconds = 0.7f, FlailWaveRadiansPerSecond = 19f, FlailLeanDegrees = 26f, FlailHopMetres = 0.22f;

    private float _flailSeconds = -1;  // time since the last effort began, or −1

    // ---- animation tuning (by eye; rationale not recorded)
    /// <summary>How fast the run, flight, wind-up and airborne amounts follow the state, per second.</summary>
    private const float RunEaseRate = 10f, FlyEaseRate = 6f, WindEaseRate = 14f, AirEaseRate = 12f, VerticalEaseRate = 12f;

    /// <summary>The run amount reaches 1 at this ground speed, m/s.</summary>
    private const float FullRunMetresPerSecond = 5f;

    /// <summary>Stride rate: one cycle per 1.3 m/s of speed, at most 4.2 cycles a second (fast running stays readable).</summary>
    private const float StrideMetresPerSecondPerCycle = 1.3f, MaxStrideCyclesPerSecond = 4.2f;

    /// <summary>The squash spring: stiff and underdamped (ζ ≈ 0.4), so a jump or landing overshoots once and settles.</summary>
    private const float SquashStiffness = 260f, SquashDamping = 13f;

    /// <summary>The antenna's sway spring: softer and less damped than the squash, so it wobbles after starts and stops.</summary>
    private const float SwayStiffness = 90f, SwayDamping = 6f;

    /// <summary>Movements this large in one frame are teleports (a landing's re-framing, a warp), not motion, metres.</summary>
    private const float TeleportMetres = 4f;
    private const int ScarfPoints = 11;
    private const float ScarfSegmentMetres = 0.085f;
    private readonly Vector3[] _rope = new Vector3[ScarfPoints];
    private readonly Vector3[] _ropePrev = new Vector3[ScarfPoints];
    private bool _ropeReady;

    public AvatarRig()
    {
        Name = "AvatarRig";
        Build();
    }

    /// <summary>Builds the figure's meshes and node tree once (shapes by eye, metres, origin at the feet, front −Z).</summary>
    private void Build()
    {
        AddChild(_lean);
        _lean.AddChild(_squash);
        _squash.AddChild(_bob);

        // Body: a soft bell-shaped cloak with a trim, a neck ring of scarf.
        var body = new MeshBuilder();
        body.Lathe([new(0, 0.1f), new(0.31f, 0.1f), new(0.33f, 0.15f), new(0.32f, 0.26f), new(0.27f, 0.42f), new(0.2f, 0.55f), new(0.12f, 0.6f), new(0, 0.61f)],
            (_, i) => i < 1 ? Cloak.Darkened(0.35f) : i < 2 ? CloakTrim : Cloak.Lightened(0.04f * (i - 2)), 18);
        body.Torus(PlanetArt.T(new Vector3(0, 0.16f, 0)), 0.315f, 0.035f, CloakTrim, 24, 5);
        body.Torus(PlanetArt.T(new Vector3(0, 0.58f, 0)), 0.15f, 0.07f, ScarfColor, 18, 7);
        body.Ellipsoid(new Vector3(0, 0.36f, -0.27f), new Vector3(0.05f, 0.05f, 0.03f), CloakTrim, 8, 5);
        _bob.AddChild(Instance(body, "Body"));

        // Head: a big round head, face on −Z.
        _head.Position = new Vector3(0, 0.83f, 0);
        _bob.AddChild(_head);
        var head = new MeshBuilder();
        head.Ellipsoid(Vector3.Zero, new Vector3(0.28f, 0.26f, 0.27f), Skin, 20, 12);
        head.Ellipsoid(new Vector3(-0.16f, -0.06f, -0.2f), new Vector3(0.055f, 0.035f, 0.03f), new Color(1f, 0.62f, 0.62f), 8, 5);
        head.Ellipsoid(new Vector3(0.16f, -0.06f, -0.2f), new Vector3(0.055f, 0.035f, 0.03f), new Color(1f, 0.62f, 0.62f), 8, 5);
        head.TorusArc(PlanetArt.T(new Vector3(0, -0.07f, -0.235f), new Vector3(90, 0, 0)), 0.04f, 0.011f, Mathf.Pi * 0.6f, Mathf.Pi * 1.4f, Dark, 8, 4);
        // A little hood cap with a rim, in the cloak colour.
        head.Lathe([new(0.0f, 0.27f), new(0.12f, 0.255f), new(0.22f, 0.19f), new(0.27f, 0.1f), new(0.29f, 0.06f), new(0.29f, 0.06f), new(0.25f, 0.05f)], (_, i) => i < 5 ? Cloak.Lightened(0.08f) : CloakTrim, 18,
            axis: new Basis(Vector3.Right, Mathf.DegToRad(14)));
        _head.AddChild(Instance(head, "HeadMesh"));
        _head.AddChild(_eyes);
        var eyes = new MeshBuilder();
        foreach (var x in new[] { -0.095f, 0.095f })
        {
            eyes.Ellipsoid(new Vector3(x, 0.0f, -0.235f), new Vector3(0.045f, 0.068f, 0.03f), Dark, 10, 7);
            eyes.Ellipsoid(new Vector3(x + 0.014f, 0.026f, -0.262f), new Vector3(0.014f, 0.016f, 0.008f), PlanetArt.Glowing(Colors.White, 0.4f), 6, 4);
        }

        _eyes.AddChild(Instance(eyes, "EyesMesh"));

        // Antenna with the lantern (pivot at the top of the head; sways).
        _antenna.Position = new Vector3(0, 0.25f, 0.02f);
        _head.AddChild(_antenna);
        var antenna = new MeshBuilder();
        antenna.Tube([Vector3.Zero, new(0, 0.1f, -0.02f), new(0, 0.2f, -0.08f), new(0, 0.25f, -0.17f)], _ => 0.014f, _ => Dark.Lightened(0.15f), 5);
        antenna.Lathe([new(0, 0.0f), new(0.05f, 0.0f), new(0.045f, -0.03f), new(0, -0.04f)], (_, _) => CloakTrim, 10, origin: new Vector3(0, 0.25f, -0.17f));
        antenna.Lathe([new(0, 0.0f), new(0.04f, 0.0f), new(0.04f, 0.0f), new(0.035f, -0.11f), new(0.035f, -0.11f), new(0, -0.12f)], (_, _) => CloakTrim, 10, origin: new Vector3(0, 0.22f, -0.17f));
        _antenna.AddChild(Instance(antenna, "AntennaMesh"));
        var core = new MeshBuilder();
        core.Ellipsoid(Vector3.Zero, new Vector3(0.055f, 0.065f, 0.055f), Colors.White, 12, 8);
        _lanternCore = new MeshInstance3D { Name = "Lantern", Mesh = new ArrayMesh(), Position = new Vector3(0, 0.155f, -0.17f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        core.Commit((ArrayMesh)_lanternCore.Mesh, PlanetArt.GlowMaterial(LanternColor, 3.2f, 0.12f));
        _antenna.AddChild(_lanternCore);
        var halo = PlanetArt.Halo(LanternColor, 0.32f, 0.55f);
        halo.Position = _lanternCore.Position;
        _antenna.AddChild(halo);
        _light = new OmniLight3D { LightColor = LanternColor, LightEnergy = 0.7f, OmniRange = 2.8f, Position = _lanternCore.Position, ShadowEnabled = false };
        _antenna.AddChild(_light);

        // Mitts and feet.
        foreach (var (node, x) in new[] { (_handL, -0.36f), (_handR, 0.36f) })
        {
            node.Position = new Vector3(x, 0.36f, 0);
            _bob.AddChild(node);
            var m = new MeshBuilder();
            m.Ellipsoid(Vector3.Zero, new Vector3(0.075f, 0.07f, 0.075f), Skin, 10, 7);
            node.AddChild(Instance(m, "Mitt"));
        }

        foreach (var (node, x) in new[] { (_footL, -0.13f), (_footR, 0.13f) })
        {
            node.Position = new Vector3(x, 0, 0);
            _lean.AddChild(node);
            var m = new MeshBuilder();
            m.Ellipsoid(new Vector3(0, 0.065f, -0.03f), new Vector3(0.095f, 0.07f, 0.13f), Dark.Lightened(0.12f), 12, 7);
            m.Ellipsoid(new Vector3(0, 0.03f, -0.03f), new Vector3(0.1f, 0.03f, 0.135f), CloakTrim.Darkened(0.2f), 12, 5);
            node.AddChild(Instance(m, "Foot"));
        }

        _scarf.Mesh = _scarfMesh;
        AddChild(_scarf);
    }

    /// <summary>A mesh instance of the built shapes with the shared prop material.</summary>
    private static MeshInstance3D Instance(MeshBuilder b, string name)
    {
        var mesh = new ArrayMesh();
        b.Commit(mesh, PlanetArt.PropMaterial());
        return new MeshInstance3D { Name = name, Mesh = mesh };
    }

    /// <summary>Places the traveller: feet at <paramref name="position"/>, standing along <paramref name="up"/>, looking along <paramref name="facing"/> (projected onto the ground plane).</summary>
    public void SetPose(Vector3 position, Vector3 up, Vector3 facing)
    {
        up = up.LengthSquared() > 1e-8f ? up.Normalized() : Vector3.Up;
        var f = facing - up * facing.Dot(up);
        if (f.LengthSquared() < 1e-8f) f = _facing - up * _facing.Dot(up);
        if (f.LengthSquared() < 1e-8f) f = up.Cross(Vector3.Right);
        f = f.Normalized();
        _up = up;
        _facing = f;
        var z = -f;
        var x = up.Cross(z).Normalized();
        GlobalTransform = new Transform3D(new Basis(x, up, z), position);
        if (!_havePose)
        {
            _lastPos = position;
            _havePose = true;
        }
    }

    /// <summary>Advances the procedural animation by <paramref name="deltaSeconds"/> for <paramref name="state"/> (call after <see cref="SetPose"/>).</summary>
    public void Animate(float deltaSeconds, AvatarState state)
    {
        var dt = Mathf.Clamp(deltaSeconds, 0f, 0.1f); // a long frame (a hitch) is taken as 0.1 s, which keeps the squash spring's step stable (ω·dt ≈ 1.6 < 2)
        if (dt <= 0) return;
        _timeSeconds += dt;
        var pos = GlobalPosition;
        var newVelocity = (pos - _lastPos) / dt;
        if ((pos - _lastPos).Length() > TeleportMetres) newVelocity = Vector3.Zero; // a teleport, not motion
        var accel = (newVelocity - _velocity) / dt;
        _velocity = newVelocity;
        _lastPos = pos;

        float Ease(float current, float target, float ratePerSecond) => current + (target - current) * (1 - Mathf.Exp(-ratePerSecond * dt));
        var speed = Mathf.Max(0, state.SpeedMetersPerSecond);
        _run = Ease(_run, state.Grounded && !state.Flying ? Mathf.Clamp(speed / FullRunMetresPerSecond, 0, 1) : 0, RunEaseRate);
        _fly = Ease(_fly, state.Flying ? 1 : 0, FlyEaseRate);
        _wind = Ease(_wind, Mathf.Clamp(state.WindUp01, 0, 1), WindEaseRate);
        _air = Ease(_air, state.Grounded || state.Flying ? 0 : 1, AirEaseRate);
        _vertical = Ease(_vertical, state.VerticalSpeedMetersPerSecond, VerticalEaseRate);

        // Stride: cycles per second grow with speed and saturate, so fast running stays readable.
        var cyclesPerSecond = Mathf.Min(speed / StrideMetresPerSecondPerCycle, MaxStrideCyclesPerSecond);
        _phase += Mathf.Tau * cyclesPerSecond * dt;

        // Squash and stretch: an underdamped spring towards a target; events kick it (a jump stretches, a landing squashes,
        // harder the faster it lands, up to twice as hard). The target crouches with the wind-up and stretches in flight.
        if (state.Jumped) _squashVelocity += 5.5f;
        if (state.Landed) _squashVelocity -= 3.5f + Mathf.Clamp(Mathf.Abs(_vertical) * 0.35f, 0, 3.5f);
        if (state.AirJumped) _squashVelocity += 4f;
        var target = -0.3f * _wind + 0.16f * _fly + _air * Mathf.Clamp(_vertical * 0.025f, -0.08f, 0.14f);
        _squashVelocity += (-SquashStiffness * (_squashAmount - target) - SquashDamping * _squashVelocity) * dt;
        _squashAmount = Mathf.Clamp(_squashAmount + _squashVelocity * dt, -0.45f, 0.5f);

        if ((state.AirJumped || state.Twirl) && _twirlSeconds < 0) _twirlSeconds = 0;
        var twirl = 0f;
        if (_twirlSeconds >= 0)
        {
            _twirlSeconds += dt;
            var t = Mathf.Clamp(_twirlSeconds / TwirlDurationSeconds, 0, 1);
            twirl = 1 - Mathf.Pow(1 - t, 3);
            if (t >= 1) _twirlSeconds = -1;
        }

        var twirling = _twirlSeconds >= 0 ? 1f : 0f;

        // The last effort: arms up and waving, legs kicking, leaning back, fading out over its last third.
        if (state.Flail) _flailSeconds = 0;
        var flail = 0f;
        if (_flailSeconds >= 0)
        {
            _flailSeconds += dt;
            var t = _flailSeconds / FlailDurationSeconds;
            flail = t >= 1 ? 0 : Mathf.Clamp((1 - t) * 3f, 0, 1) * Mathf.Clamp(t * 8f, 0, 1);
            if (t >= 1) _flailSeconds = -1;
        }

        var wave = Mathf.Sin(_timeSeconds * FlailWaveRadiansPerSecond);

        // Lean and spin (pivot about the body centre so the flight pose rotates around the middle, not the feet).
        var forwardSpeed = Mathf.Clamp(speed / 8f, 0, 1);
        // Degrees: 6–14° forward when running, 68° in flight (nearly prone), 8° back when winding up.
        var leanDeg = _run * (6f + 8f * forwardSpeed) + _fly * 68f + _wind * -8f - flail * FlailLeanDegrees;
        var shake = _wind * Mathf.Sin(_timeSeconds * 55f) * 2.5f; // a fast ±2.5° tremble while winding up (55 rad/s ≈ 9 Hz)
        var spin = new Basis(Vector3.Up, -twirl * Mathf.Tau);
        var tilt = new Basis(Vector3.Right, -Mathf.DegToRad(leanDeg)) * new Basis(Vector3.Back, Mathf.DegToRad(shake + Mathf.Sin(_phase) * 3f * _run));
        var pivot = new Vector3(0, 0.5f, 0);
        var rot = spin * tilt;
        _lean.Transform = new Transform3D(rot, pivot - rot * pivot);

        // Squash about the feet (volume preserving), plus breathing.
        var s = 1 + _squashAmount + 0.012f * Mathf.Sin(_timeSeconds * 2.4f) * (1 - _run);
        var side = 1 / Mathf.Sqrt(Mathf.Max(s, 0.3f));
        _squash.Scale = new Vector3(side, s, side);

        // Running bob: lowest at each foot contact.
        var bob = _run * 0.055f * Mathf.Abs(Mathf.Sin(_phase)) - _run * 0.02f;
        var flailT = _flailSeconds >= 0 ? _flailSeconds / FlailDurationSeconds : 1f;
        var hop = flailT < 0.5f ? FlailHopMetres * Mathf.Sin(Mathf.Pi * flailT * 2f) : 0f; // the effort's hop (first half of the flail)
        _bob.Position = new Vector3(0, bob + hop, 0);
        _head.Rotation = new Vector3(Mathf.DegToRad(-leanDeg * 0.35f * (1 - _fly) + _fly * -40f + 4f * Mathf.Sin(_phase * 2) * _run), 0, 0);

        // Feet: swing along the stride on the ground, tuck in the air, together and trailing in flight.
        var swing = Mathf.Sin(_phase) * 0.17f * _run;
        var liftL = Mathf.Max(0, Mathf.Cos(_phase)) * 0.1f * _run;
        var liftR = Mathf.Max(0, -Mathf.Cos(_phase)) * 0.1f * _run;
        var tuck = _air * 0.12f;
        _footL.Position = new Vector3(-0.13f + 0.05f * _fly, liftL + tuck + 0.02f * _wind, -swing + 0.05f * _air + 0.15f * _fly + 0.12f * flail * wave).Lerp(new Vector3(-0.06f, 0.06f, 0.2f), _fly);
        _footR.Position = new Vector3(0.13f - 0.05f * _fly, liftR + tuck * 0.6f + 0.02f * _wind, swing - 0.03f * _air + 0.15f * _fly - 0.12f * flail * wave).Lerp(new Vector3(0.06f, 0.06f, 0.2f), _fly);
        _footL.Rotation = new Vector3(Mathf.DegToRad(-swing * 120f + _fly * 60f), 0, 0);
        _footR.Rotation = new Vector3(Mathf.DegToRad(swing * 120f + _fly * 60f), 0, 0);

        // Mitts: swing opposite the feet; up when rising, out when twirling, raised and back during wind-up, swept back in flight.
        var rise = _air * Mathf.Clamp(_vertical / 6f, -1, 1);
        var handSwing = Mathf.Sin(_phase) * 0.14f * _run;
        var handL = new Vector3(-0.36f - 0.1f * twirling - 0.05f * _air, 0.36f + 0.16f * rise + 0.12f * _wind + 0.08f * twirling, handSwing + 0.1f * _wind);
        var handR = new Vector3(0.36f + 0.1f * twirling + 0.05f * _air, 0.36f + 0.16f * rise + 0.12f * _wind + 0.08f * twirling, -handSwing + 0.1f * _wind);
        // The last effort: both mitts thrown up overhead, waving out of step.
        // (after the flight pose, so it shows in a fall too).
        _handL.Position = handL.Lerp(new Vector3(-0.3f, 0.32f, 0.25f), _fly).Lerp(new Vector3(-0.3f - 0.08f * wave, 0.95f + 0.1f * wave, -0.1f), flail);
        _handR.Position = handR.Lerp(new Vector3(0.3f, 0.32f, 0.25f), _fly).Lerp(new Vector3(0.3f - 0.08f * wave, 0.95f - 0.1f * wave, -0.1f), flail);

        // Blink now and then: the eyes close to 12 % for the last 0.12 s before each blink, then the next comes 2.2–4.7 s
        // later. sin(t · 12.9898) is a cheap, repeatable pseudo-random number (the constant is the one familiar from a common shader hash).
        _blinkSeconds -= dt;
        var blink = _blinkSeconds < 0.12f ? 0.12f : 1f;
        if (_blinkSeconds < 0) _blinkSeconds = 2.2f + (Mathf.Sin(_timeSeconds * 12.9898f) * 0.5f + 0.5f) * 2.5f;
        _eyes.Scale = new Vector3(1, Mathf.Lerp(_eyes.Scale.Y, blink, 1 - Mathf.Exp(-40f * dt)), 1);

        // Antenna sway: a spring driven by acceleration along the facing (it lags behind starts and swings on stops).
        var forwardAccel = accel.Dot(_facing);
        var swayTarget = Mathf.Clamp(-forwardAccel * 0.012f, -0.6f, 0.6f) - 0.25f * _fly + 0.12f * _run;
        _swayVelocity += (-SwayStiffness * (_sway - swayTarget) - SwayDamping * _swayVelocity) * dt;
        _sway += _swayVelocity * dt;
        _antenna.Rotation = new Vector3(_sway, 0, 0.08f * Mathf.Sin(_timeSeconds * 1.7f));

        // Lantern: brighter with the wind-up and in flight.
        _light.LightEnergy = 0.7f + 1.2f * _wind + 0.5f * _fly;
        _lanternCore.Scale = Vector3.One * (1 + 0.35f * _wind);

        UpdateScarf(dt);
    }

    /// <summary>Scarf ribbon: Verlet rope from the back of the neck with drag, light gravity and a breeze; drawn as a two-sided strip.</summary>
    private void UpdateScarf(float dt)
    {
        var anchor = _bob.GlobalTransform * new Vector3(0.05f, 0.6f, 0.14f);
        var back = GlobalBasis.Z.Normalized();
        if (!_ropeReady || (_rope[0] - anchor).Length() > 3f)
        {
            for (var i = 0; i < ScarfPoints; i++)
            {
                _rope[i] = anchor + back * (i * ScarfSegmentMetres) - _up * (i * 0.02f);
                _ropePrev[i] = _rope[i];
            }

            _ropeReady = true;
        }

        const int steps = 2; // Verlet sub-steps per frame (rationale for two not recorded)
        var h = dt / steps;
        var breeze = back * (0.9f + 0.6f * Mathf.Sin(_timeSeconds * 3.1f)) + GlobalBasis.X * 0.4f * Mathf.Sin(_timeSeconds * 4.3f);
        var gravity = -_up * 3.5f; // light (m/s²): the ribbon floats rather than hangs
        var damping = Mathf.Exp(-9f * h); // air drag: the ribbon streams behind when the traveller moves fast
        for (var step = 0; step < steps; step++)
        {
            _rope[0] = anchor;
            _ropePrev[0] = anchor;
            for (var i = 1; i < ScarfPoints; i++)
            {
                var v = (_rope[i] - _ropePrev[i]) * damping;
                _ropePrev[i] = _rope[i];
                // Flutter grows with speed, so a streaming scarf waves instead of trailing as a thin line.
                var flutter = (GlobalBasis.X * Mathf.Sin(_timeSeconds * 9f + i * 0.9f) + _up * 0.6f * Mathf.Cos(_timeSeconds * 7f + i * 0.7f))
                              * (Mathf.Min(_velocity.Length(), 20f) * 0.9f * i / (ScarfPoints - 1f));
                _rope[i] += v + (gravity + breeze * 2f + flutter) * h * h;
            }

            // Keep the ribbon out of the body, then make it inextensible by following the leader: each point sits exactly
            // one segment from the previous one (stable however fast the anchor moves; a launch must not stretch it).
            var bodyCentre = _bob.GlobalTransform * new Vector3(0, 0.45f, 0);
            for (var i = 1; i < ScarfPoints; i++)
            {
                var off = _rope[i] - bodyCentre;
                var dist = off.Length();
                if (dist < 0.36f && dist > 1e-5f) _rope[i] = bodyCentre + off / dist * 0.36f;
                var d = _rope[i] - _rope[i - 1];
                var len = d.Length();
                var target = _rope[i - 1] + (len > 1e-6f ? d / len : back) * ScarfSegmentMetres;
                _ropePrev[i] += (target - _rope[i]) * 0.85f; // most of the correction is not new velocity
                _rope[i] = target;
            }
        }

        _scarfMesh.ClearSurfaces();
        _scarfMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, PlanetArt.PropMaterial());
        var colA = PlanetArt.Linear(ScarfColor);
        var colB = PlanetArt.Linear(ScarfColor.Lightened(0.25f));
        for (var i = 0; i < ScarfPoints - 1; i++)
        {
            var a = _rope[i];
            var b = _rope[i + 1];
            var dir = (b - a).Normalized();
            var side = dir.Cross(_up);
            if (side.LengthSquared() < 1e-6f) side = GlobalBasis.X;
            side = side.Normalized();
            var wA = 0.065f * (1 - 0.35f * i / (ScarfPoints - 1f));
            var wB = 0.065f * (1 - 0.35f * (i + 1) / (ScarfPoints - 1f));
            var c = i % 3 == 2 ? colB : colA;
            Vector3 a0 = a - side * wA, a1 = a + side * wA, b0 = b - side * wB, b1 = b + side * wB;
            // Both faces of the ribbon: each triangle in both windings, each with the normal of its own front side.
            EmitTri(a0, b0, b1, c);
            EmitTri(a0, b1, b0, c);
            EmitTri(a0, b1, a1, c);
            EmitTri(a0, a1, b1, c);
        }

        _scarfMesh.SurfaceEnd();
    }

    /// <summary>One triangle of the scarf strip with its own front-face normal (degenerate ones are skipped).</summary>
    private void EmitTri(Vector3 p0, Vector3 p1, Vector3 p2, Color c)
    {
        // Godot's front faces wind clockwise, so the front normal is −(p1 − p0) × (p2 − p0).
        var n = -(p1 - p0).Cross(p2 - p0);
        if (n.LengthSquared() < 1e-12f) return;
        n = n.Normalized();
        foreach (var p in new[] { p0, p1, p2 })
        {
            _scarfMesh.SurfaceSetNormal(n);
            _scarfMesh.SurfaceSetColor(c);
            _scarfMesh.SurfaceAddVertex(p);
        }
    }
}
