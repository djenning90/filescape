using System.Numerics;
using FileScape.Core.Places;
using FileScape.Core.Units;

namespace FileScape.Core.Experiments;

/// <summary>
/// EXPERIMENT (PLANET-02, not the product): a small spherical body a folder-place could become. World space is metres,
/// Y up only as a convention for the whole scene; on a body, "up" is away from its centre.
/// </summary>
/// <param name="Center">World position of the body's centre.</param>
/// <param name="Radius">Surface radius, metres.</param>
/// <param name="SurfaceGravity">Acceleration at the surface, m/s² (falls off with the inverse square of the distance).</param>
/// <param name="Pole">Unit vector from the centre to the body's pole: where a place's landmark would stand (chart origin).</param>
/// <param name="PoleNorth">Unit tangent at the pole: the place's north (chart −Z).</param>
public sealed record Planetoid(Vector3 Center, float Radius, float SurfaceGravity, Vector3 Pole, Vector3 PoleNorth)
{
    /// <summary>
    /// The chart's east at the pole: north × up, so (east, up, north) has the place's own handedness (x east, z south,
    /// y up: east × north = up). Until 2026-11-06 this had the opposite sign, which mirrored every chart on the sphere
    /// (round trips still agreed, so the tests did not notice); fixed with
    /// <c>PlanetoidTests.The_chart_keeps_its_handedness_on_the_sphere</c>.
    /// </summary>
    private Vector3 PoleEast => Vector3.Normalize(Vector3.Cross(PoleNorth, Pole));

    /// <summary>
    /// Chart → world: the azimuthal-equidistant map from the pole. A place's flat local coordinates (x east, z south, y
    /// up, metres) become a point on (or <paramref name="height"/> above) the sphere: distance from the pole along the
    /// surface is preserved exactly; distances across shrink by sin(φ)/φ (≈ 0.64 at a quarter turn). This is what lets
    /// the existing flat composer, terrain and walker run unchanged on a curved body.
    /// </summary>
    public Vector3 ToWorld(Vector2 chart, float height = 0)
    {
        var s = chart.Length();
        var phi = s / Radius;
        var t = s < 1e-5f ? PoleNorth : Vector3.Normalize(PoleEast * (chart.X / s) + PoleNorth * (-chart.Y / s));
        var dir = Pole * MathF.Cos(phi) + t * MathF.Sin(phi);
        return Center + dir * (Radius + height);
    }

    /// <summary>World → chart (and height above the surface). Undefined only exactly at the antipode.</summary>
    public (Vector2 Chart, float Height) ToChart(Vector3 world)
    {
        var d = world - Center;
        var len = d.Length();
        var u = d / len;
        var cos = Math.Clamp(Vector3.Dot(u, Pole), -1f, 1f);
        var phi = MathF.Acos(cos);
        var tangent = u - Pole * cos;
        var tl = tangent.Length();
        if (tl < 1e-6f) return (Vector2.Zero, len - Radius);
        tangent /= tl;
        var s = phi * Radius;
        return (new Vector2(Vector3.Dot(tangent, PoleEast) * s, -Vector3.Dot(tangent, PoleNorth) * s), len - Radius);
    }

    /// <summary>The local frame at a chart point: up (away from the centre), and the chart's north and east carried onto the sphere.</summary>
    public (Vector3 Up, Vector3 North, Vector3 East) FrameAt(Vector2 chart)
    {
        var p = ToWorld(chart);
        var up = Vector3.Normalize(p - Center);
        const float e = 0.05f; // a 5 cm chart step north, to read the chart's north off the sphere by finite difference
        var ahead = ToWorld(chart + new Vector2(0, -e)) - p;
        var north = Vector3.Normalize(ahead - up * Vector3.Dot(ahead, up));
        var east = Vector3.Normalize(Vector3.Cross(north, up));
        return (up, north, east);
    }

    /// <summary>Gravity this body pulls with at a world point (towards its centre, inverse square, m/s²).</summary>
    public Vector3 GravityAt(Vector3 p)
    {
        var d = Center - p;
        var r = MathF.Max(d.Length(), Radius * 0.5f); // never nearer than half the radius: no singularity at the centre
        return d / r * SurfaceGravity * (Radius * Radius) / (r * r);
    }
}

/// <summary>
/// EXPERIMENT (PLANET-02): walking on small bodies and travelling between them. On a body the unchanged
/// <see cref="Walker"/> moves in the body's chart (a flat <see cref="PlaceTerrain"/>), so steps, collision, jumping and
/// the forgiving timing all carry over; the chart pose is mapped onto the sphere for the camera. A launch leaves the
/// chart for free flight under every body's gravity; touching a body again lands in its chart.
/// </summary>
public sealed class PlanetoidTraveller
{
    /// <summary>Free-flight integration frequency (as the walker's).</summary>
    public const int StepsPerSecond = 120;

    /// <summary>A guided transfer starts this far above the feet, metres.</summary>
    private const float GuidedDepartureLift = 0.2f;

    /// <summary>A free launch starts this far above the feet, metres, so the first flight step is not already a touchdown.</summary>
    private const float LaunchDepartureLift = 0.2f;

    /// <summary>The guided arc's control point is raised by this fraction of the straight distance (a quadratic Bézier peaks at half of it).</summary>
    private const float GuidedArcRise = 0.42f;

    // ---- guided transfer time: GuidedBaseSeconds + distance / GuidedMetresPerSecond, clamped (PLANET-03: ≤ 2.7 s)
    private const float GuidedBaseSeconds = 0.9f;
    private const float GuidedMetresPerSecond = 140f;
    private const float GuidedMinSeconds = 1.4f;
    private const float GuidedMaxSeconds = 2.6f;

    // ---- LaunchSpeed's search: whole and half m/s from the slowest up, landings counted only after the departure
    private const float LaunchSearchMinSpeed = 1f;
    private const float LaunchSearchMaxSpeed = 80f;
    private const float LaunchSearchSpeedStep = 0.5f;

    /// <summary>A touchdown within this long of the start is the departure body still underfoot, not a landing, seconds.</summary>
    private const float LaunchSearchDepartureSeconds = 0.2f;

    private readonly IReadOnlyList<Planetoid> _bodies;
    private readonly IReadOnlyList<PlaceTerrain> _terrains;
    private readonly Walker _walker;

    public PlanetoidTraveller(IReadOnlyList<Planetoid> bodies, IReadOnlyList<PlaceTerrain> terrains, WalkParams? walk = null)
    {
        _bodies = bodies;
        _terrains = terrains;
        _walker = new Walker(walk ?? WalkParams.Default);
    }

    /// <summary>The body the traveller stands on, or −1 while in free flight between bodies.</summary>
    public int Body { get; private set; }

    public Walker Walker => _walker;
    public Vector3 FlightPosition { get; private set; }
    public Vector3 FlightVelocity { get; private set; }
    public int Transfers { get; private set; }

    /// <summary>Seconds since the last launch (while flying), or the duration of the last completed transfer.</summary>
    public float FlightSeconds { get; private set; }

    /// <summary>True during a guided transfer (<see cref="LaunchGuided"/>), as opposed to free flight under gravity.</summary>
    public bool Guided => _arc != null;

    /// <summary>0 at departure, 1 at touchdown, during a guided transfer.</summary>
    public float GuidedProgress => _arc == null ? 0 : Math.Clamp(_arcT / _arc.Duration, 0, 1);

    /// <summary>A guided transfer: a quadratic Bézier From → Control → To over Duration seconds, landing at TargetChart on Target.</summary>
    private sealed record Arc(Vector3 From, Vector3 Control, Vector3 To, int Target, Vector2 TargetChart, float Duration);

    private Arc? _arc;
    private float _arcT;

    /// <summary>
    /// PLANET-03: an energetic, readable transfer instead of the literal slow ballistic arc. The traveller follows a
    /// quadratic arc from here to <paramref name="targetChart"/> on <paramref name="target"/>, raised away from both
    /// bodies, in <paramref name="seconds"/> (default: 1.4–2.6 s by distance). Parametrised linearly, the arc's speed is
    /// highest at departure and arrival and lowest at the top: a burst off the pad, a moment of hang to see where you
    /// are going, and a decisive landing. Touchdown lands walking in the target's chart, facing along the arc.
    /// </summary>
    public void LaunchGuided(int target, Vector2 targetChart, float? seconds = null)
    {
        if (Body < 0) return;
        var from = Position + Up * GuidedDepartureLift;
        var to = _bodies[target].ToWorld(targetChart);
        var dist = Vector3.Distance(from, to);
        var lift = Vector3.Normalize(Up + Vector3.Normalize(to - _bodies[target].Center));
        if (!float.IsFinite(lift.X)) lift = Up;
        var control = (from + to) / 2 + lift * dist * GuidedArcRise;
        var duration = seconds ?? Math.Clamp(GuidedBaseSeconds + dist / GuidedMetresPerSecond, GuidedMinSeconds, GuidedMaxSeconds);
        _arc = new Arc(from, control, to, target, targetChart, duration);
        _arcT = 0;
        FlightSeconds = 0;
        FlightPosition = from;
        FlightVelocity = (control - from) * 2 / duration;
        Body = -1;
    }

    /// <summary>Stands the traveller on <paramref name="body"/> at a chart point, walking, facing <paramref name="heading"/> (a camera yaw).</summary>
    public void PlaceOn(int body, Vector2 chart, Degrees heading)
    {
        Body = body;
        _walker.Teleport(_terrains[body], new Vector3(chart.X, 0, chart.Y), heading, Degrees.Zero);
    }

    /// <summary>World position of the feet (on a body) or of the traveller (in flight).</summary>
    public Vector3 Position => Body >= 0 ? _bodies[Body].ToWorld(new Vector2(_walker.Feet.X, _walker.Feet.Z), _walker.Feet.Y) : FlightPosition;

    /// <summary>Local up at the traveller: away from the body stood on, or against the strongest pull in flight.</summary>
    public Vector3 Up
    {
        get
        {
            if (Body >= 0) return Vector3.Normalize(Position - _bodies[Body].Center);
            var g = Vector3.Zero;
            foreach (var b in _bodies) g += b.GravityAt(FlightPosition);
            return g.LengthSquared() > 1e-8f ? -Vector3.Normalize(g) : Vector3.UnitY;
        }
    }

    /// <summary>The walking heading as a world direction on the sphere (the camera looks along it).</summary>
    public Vector3 Forward
    {
        get
        {
            if (Body < 0) return FlightVelocity.LengthSquared() > 1e-4f ? Vector3.Normalize(FlightVelocity) : Vector3.UnitZ;
            var chart = new Vector2(_walker.Feet.X, _walker.Feet.Z);
            var (_, north, east) = _bodies[Body].FrameAt(chart);
            var f = Interaction.CameraMath.HorizontalForward(_walker.Yaw);
            return Vector3.Normalize(east * f.X + north * -f.Z);
        }
    }

    public void Update(WalkInput input, TimeSpan dt)
    {
        if (Body >= 0)
        {
            _walker.Update(_terrains[Body], input, dt);
            return;
        }

        FlightSeconds += dt.ToSecondsF();
        if (_arc is { } arc)
        {
            _arcT += dt.ToSecondsF();
            var u = Math.Clamp(_arcT / arc.Duration, 0, 1);
            var a = Vector3.Lerp(arc.From, arc.Control, u);
            var b = Vector3.Lerp(arc.Control, arc.To, u);
            FlightPosition = Vector3.Lerp(a, b, u);
            FlightVelocity = (b - a) * 2 / arc.Duration;
            if (u >= 1)
            {
                var body = _bodies[arc.Target];
                var (_, north, east) = body.FrameAt(arc.TargetChart);
                var along = new Vector2(Vector3.Dot(FlightVelocity, east), -Vector3.Dot(FlightVelocity, north));
                var heading = along.LengthSquared() > 1e-4f ? Degrees.Of(-PlaceTerrain.PlaceAngle(Vector2.Normalize(along))) : _walker.Yaw;
                Body = arc.Target;
                _walker.Teleport(_terrains[arc.Target], new Vector3(arc.TargetChart.X, 0, arc.TargetChart.Y), heading, Degrees.Zero);
                Transfers++;
                _arc = null;
            }

            return;
        }

        var steps = Math.Max(1, (int)MathF.Round(dt.ToSecondsF() * StepsPerSecond));
        var h = dt.ToSecondsF() / steps;
        for (var i = 0; i < steps && Body < 0; i++) FlightStep(h);
    }

    /// <summary>Leaves the body with a world velocity (a launch pad, or a jump strong enough to escape).</summary>
    public void Launch(Vector3 velocity)
    {
        if (Body < 0) return;
        FlightPosition = Position + Up * LaunchDepartureLift;
        FlightVelocity = velocity;
        Body = -1;
    }

    private void FlightStep(float h)
    {
        var g = Vector3.Zero;
        foreach (var b in _bodies) g += b.GravityAt(FlightPosition);
        FlightVelocity += g * h;
        FlightPosition += FlightVelocity * h;
        for (var i = 0; i < _bodies.Count; i++)
        {
            var b = _bodies[i];
            var d = FlightPosition - b.Center;
            if (d.Length() > b.Radius || Vector3.Dot(FlightVelocity, d) > 0) continue;
            // Touch down: into this body's chart, keeping the horizontal part of the motion as walking momentum.
            var (chart, _) = b.ToChart(FlightPosition);
            var (_, north, east) = b.FrameAt(chart);
            var along = new Vector2(Vector3.Dot(FlightVelocity, east), -Vector3.Dot(FlightVelocity, north));
            var heading = along.LengthSquared() > 1e-4f ? Degrees.Of(-PlaceTerrain.PlaceAngle(Vector2.Normalize(along))) : _walker.Yaw;
            Body = i;
            _walker.Teleport(_terrains[i], new Vector3(chart.X, 0, chart.Y), heading, Degrees.Zero);
            Transfers++;
            return;
        }
    }

    /// <summary>
    /// The launch speed (along <paramref name="direction"/>) that carries a traveller from <paramref name="from"/> to land
    /// on body <paramref name="target"/>, found by simulating each candidate speed from 1 m/s upwards in 0.5 m/s steps
    /// (a scan, not a bisection: landing is not monotonic in speed). Null if none up to 80 m/s.
    /// </summary>
    public static float? LaunchSpeed(IReadOnlyList<Planetoid> bodies, Vector3 from, Vector3 direction, int target, float maxSeconds = 20f)
    {
        int? Land(float speed)
        {
            var p = from;
            var v = Vector3.Normalize(direction) * speed;
            const float h = 1f / StepsPerSecond;
            for (var t = 0f; t < maxSeconds; t += h)
            {
                var g = Vector3.Zero;
                foreach (var b in bodies) g += b.GravityAt(p);
                v += g * h;
                p += v * h;
                for (var i = 0; i < bodies.Count; i++)
                {
                    var d = p - bodies[i].Center;
                    if (d.Length() <= bodies[i].Radius && Vector3.Dot(v, d) <= 0 && t > LaunchSearchDepartureSeconds) return i;
                }
            }

            return null;
        }

        // Too slow falls back home; too fast flies past. The slowest speed (in 0.5 m/s steps) that arrives is the gentlest arc.
        for (var s = LaunchSearchMinSpeed; s <= LaunchSearchMaxSpeed; s += LaunchSearchSpeedStep)
        {
            if (Land(s) == target) return s;
        }

        return null;
    }
}
