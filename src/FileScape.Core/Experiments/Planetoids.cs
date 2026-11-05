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
    private Vector3 PoleEast => Vector3.Normalize(Vector3.Cross(PoleNorth, Pole)) * -1f;

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
        const float e = 0.05f;
        var ahead = ToWorld(chart + new Vector2(0, -e)) - p;
        var north = Vector3.Normalize(ahead - up * Vector3.Dot(ahead, up));
        var east = Vector3.Normalize(Vector3.Cross(north, up)) * -1f;
        return (up, north, east);
    }

    /// <summary>Gravity this body pulls with at a world point (towards its centre, inverse square, m/s²).</summary>
    public Vector3 GravityAt(Vector3 p)
    {
        var d = Center - p;
        var r = MathF.Max(d.Length(), Radius * 0.5f);
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

        var steps = Math.Max(1, (int)MathF.Round(dt.ToSecondsF() * StepsPerSecond));
        var h = dt.ToSecondsF() / steps;
        for (var i = 0; i < steps && Body < 0; i++) FlightStep(h);
    }

    /// <summary>Leaves the body with a world velocity (a launch pad, or a jump strong enough to escape).</summary>
    public void Launch(Vector3 velocity)
    {
        if (Body < 0) return;
        FlightPosition = Position + Up * 0.2f;
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
    /// on body <paramref name="target"/>, found by simulating (bisection on speed). Null if none below 80 m/s.
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
                    if (d.Length() <= bodies[i].Radius && Vector3.Dot(v, d) <= 0 && t > 0.2f) return i;
                }
            }

            return null;
        }

        // Too slow falls back home; too fast flies past. The slowest speed (in 0.5 m/s steps) that arrives is the gentlest arc.
        for (var s = 1f; s <= 80f; s += 0.5f)
        {
            if (Land(s) == target) return s;
        }

        return null;
    }
}
