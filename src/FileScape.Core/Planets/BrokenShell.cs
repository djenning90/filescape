using System.Numerics;

namespace FileScape.Core.Planets;

/// <summary>
/// One piece of solid ground on a Hazard World's shell (PLANET-34, PLANET-46, AD-53, AD-59): a jagged cap round
/// <see cref="Centre"/> — the arrival, a launcher's pad, the landmark and overlook, a file plateau or an outcrop. Its edge is
/// <see cref="BoundaryRadians"/>, an angle from the centre that varies with the bearing round it: the base angle
/// <see cref="RadiusRadians"/> shaped by a few broad lobes and two sets of sharp teeth (triangle waves), with phases from
/// <see cref="Seed"/> and amplitudes scaled by <see cref="Roughness"/>. A land is star-shaped about its centre (every point
/// on the straight way from the centre to a point inside is inside), which the route relies on.
/// <para>
/// Planet frame (docs/units.md): directions are unit vectors from the planet centre, angles radians. The bearing θ round
/// the centre is measured in the tangent frame <see cref="PlanetShape.Tangents"/> gives the centre (θ = 0 along its A).
/// The walker, the app's mesh and the tests all read <see cref="BrokenShell"/>, which reads this, so what you see is
/// where you fall.
/// </para>
/// </summary>
/// <param name="centre">Unit direction of the land's centre (also the route's waypoint on it, unless the shell says otherwise).</param>
/// <param name="radiusRadians">The base angular radius; the edge stays within ±<see cref="Wobble"/> × <see cref="Roughness"/> of it.</param>
/// <param name="seed">Chooses the edge's phases (deterministic per world and land).</param>
/// <param name="roughness">Scales the edge's lobes and teeth, 0..1 (1: the full <see cref="Wobble"/>).</param>
public sealed class ShellLand(Vector3 centre, float radiusRadians, int seed, float roughness = 1f)
{
    /// <summary>Unit direction of the land's centre.</summary>
    public Vector3 Centre { get; } = centre;

    /// <summary>The base angular radius, radians.</summary>
    public float RadiusRadians { get; } = radiusRadians;

    /// <summary>Chooses the edge's phases.</summary>
    public int Seed { get; } = seed;

    /// <summary>Scales the edge's lobes and teeth, 0..1.</summary>
    public float Roughness { get; } = Math.Clamp(roughness, 0f, 1f);

    // ---- the edge profile: r(θ) = RadiusRadians × (1 + Roughness × Σ amplitude × wave(frequency θ + phase)). Lobes are
    // sines (broad bites out of the rim), teeth are triangle waves (sharp points: the "broken" look). Their amplitudes sum
    // to Wobble. Chosen by eye (2026-11-16, PLANET-46: islands of 4–9 m read as broken rock with teeth of ≈ 0.2 m).

    /// <summary>Broad lobes: 2, 3 and 5 per turn, at these fractions of the base radius.</summary>
    private static readonly (int Frequency, float Amplitude)[] Lobes = [(2, 0.07f), (3, 0.05f), (5, 0.03f)];

    /// <summary>Teeth: 11 large and 23 small per turn (odd and coprime with the lobes, so the pattern never repeats round the rim).</summary>
    private static readonly (int Frequency, float Amplitude)[] Teeth = [(11, 0.03f), (23, 0.02f)];

    /// <summary>The largest relative excursion of the edge from the base radius at full roughness (the sum of every amplitude: 0.20).</summary>
    public const float Wobble = 0.07f + 0.05f + 0.03f + 0.03f + 0.02f;

    /// <summary>One phase per lobe and tooth set, radians, drawn from the seed.</summary>
    private readonly float[] _phases = MakePhases(seed);

    private readonly (Vector3 A, Vector3 B) _tangents = PlanetShape.Tangents(centre);

    private static float[] MakePhases(int seed)
    {
        var rng = new Random(seed);
        var p = new float[Lobes.Length + Teeth.Length];
        for (var i = 0; i < p.Length; i++) p[i] = (float)(rng.NextDouble() * 2 * Math.PI);
        return p;
    }

    /// <summary>The farthest the edge reaches from the centre, radians.</summary>
    public float MaxRadiusRadians => RadiusRadians * (1 + Wobble * Roughness);

    /// <summary>The nearest the edge comes to the centre, radians: whatever stands within it is on solid ground.</summary>
    public float MinRadiusRadians => RadiusRadians * (1 - Wobble * Roughness);

    /// <summary>The edge's angle from the centre at bearing <paramref name="thetaRadians"/>, radians.</summary>
    public float BoundaryRadians(float thetaRadians)
    {
        var ph = _phases;
        var f = 0f;
        for (var i = 0; i < Lobes.Length; i++) f += Lobes[i].Amplitude * MathF.Sin(Lobes[i].Frequency * thetaRadians + ph[i]);
        for (var i = 0; i < Teeth.Length; i++) f += Teeth[i].Amplitude * Triangle(Teeth[i].Frequency * thetaRadians + ph[Lobes.Length + i]);
        return RadiusRadians * (1 + Roughness * f);
    }

    /// <summary>A triangle wave of period 2π in [−1, 1] (−1 at 0, 1 at π): straight flanks and sharp points.</summary>
    internal static float Triangle(float x)
    {
        var t = x / (2 * MathF.PI);
        t -= MathF.Floor(t);
        return 4 * MathF.Abs(t - 0.5f) - 1;
    }

    /// <summary>The bearing of <paramref name="up"/> round the centre, radians (0 for the centre itself).</summary>
    public float BearingOf(Vector3 up) => MathF.Atan2(Vector3.Dot(up, _tangents.B), Vector3.Dot(up, _tangents.A));

    /// <summary>
    /// How far <paramref name="up"/> is inside the edge, radians, measured along its bearing from the centre: positive on
    /// this land, negative off it. Along a jagged rim this radial measure can differ a little from the true distance.
    /// </summary>
    public float InsideRadians(Vector3 up)
    {
        var d = PlanetShape.Angle(up, Centre);
        return BoundaryRadians(BearingOf(up)) - d;
    }

    /// <summary>True when <paramref name="up"/> is on this land.</summary>
    public bool Contains(Vector3 up)
    {
        var d = PlanetShape.Angle(up, Centre);
        if (d >= MaxRadiusRadians) return false; // the cheap tests first
        if (d < MinRadiusRadians) return true;
        return d < BoundaryRadians(BearingOf(up));
    }

    /// <summary>The point of the edge at bearing <paramref name="thetaRadians"/>: a unit direction.</summary>
    public Vector3 BoundaryPoint(float thetaRadians)
    {
        var r = BoundaryRadians(thetaRadians);
        var t = _tangents.A * MathF.Cos(thetaRadians) + _tangents.B * MathF.Sin(thetaRadians);
        return Vector3.Normalize(Centre * MathF.Cos(r) + t * MathF.Sin(r));
    }
}

/// <summary>
/// A bridge or ridge of solid ground on a Hazard World (PLANET-46, AD-59): a strip along the great-circle arc from
/// <see cref="A"/> to <see cref="B"/> (two waypoints, each inside a land), at least <see cref="HalfWidthMetres"/> each side
/// of its centre line, with ragged edges that only ever widen it (by up to <see cref="RagMetres"/>), and with
/// <see cref="Gaps"/>: stretches cut clean across it that you jump. Drops on both sides; the route walks its centre line.
/// <para>Planet frame, metres along the surface at the planet's base radius.</para>
/// </summary>
public sealed class ShellSpan
{
    /// <summary>The ragged edges stick out from the strip's guaranteed half-width by up to this much, metres (by eye).</summary>
    public const float RagMetres = 0.3f;

    /// <summary>The rag's two wavelengths along the bridge, metres (rationale: irregular at walking pace; by eye).</summary>
    private const float RagLongMetres = 1.9f, RagShortMetres = 0.7f;

    private readonly Vector3 _normal, _tangent, _mid;
    private readonly float _radius, _boundRadians;
    private readonly float[] _phases;

    public ShellSpan(Vector3 a, Vector3 b, float halfWidthMetres, float radius, int seed, IReadOnlyList<(float FromMetres, float ToMetres)>? gaps = null)
    {
        A = a;
        B = b;
        HalfWidthMetres = halfWidthMetres;
        Seed = seed;
        Gaps = gaps ?? [];
        _radius = radius;
        var n = Vector3.Cross(a, b);
        _normal = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : PlanetShape.Tangents(a).A;
        _tangent = Vector3.Cross(_normal, a); // at A, along the arc towards B
        LengthMetres = PlanetShape.Angle(a, b) * radius;
        var mid = a + b;
        _mid = mid.LengthSquared() > 1e-8f ? Vector3.Normalize(mid) : a;
        _boundRadians = (LengthMetres / 2 + halfWidthMetres + RagMetres + 0.01f) / radius;
        var rng = new Random(seed);
        _phases = [(float)(rng.NextDouble() * 6.283), (float)(rng.NextDouble() * 6.283), (float)(rng.NextDouble() * 6.283), (float)(rng.NextDouble() * 6.283)];
    }

    /// <summary>The waypoints at its ends (unit directions).</summary>
    public Vector3 A { get; }
    public Vector3 B { get; }

    /// <summary>The guaranteed half-width, metres: the strip is at least twice this wide everywhere outside a gap.</summary>
    public float HalfWidthMetres { get; }

    public int Seed { get; }

    /// <summary>The stretches cut across the bridge (jump gaps), metres along it from <see cref="A"/>.</summary>
    public IReadOnlyList<(float FromMetres, float ToMetres)> Gaps { get; }

    /// <summary>Its length from waypoint to waypoint, metres.</summary>
    public float LengthMetres { get; }

    /// <summary>A cheap bound: the bridge lies within this angle of <see cref="Mid"/>, radians.</summary>
    public float BoundRadians => _boundRadians;

    /// <summary>The middle of its arc (unit direction).</summary>
    public Vector3 Mid => _mid;

    /// <summary>
    /// Where <paramref name="up"/> is relative to the bridge: metres along it from <see cref="A"/> (negative before A, beyond
    /// <see cref="LengthMetres"/> after B) and metres to the side of the centre line (positive to the left going A → B).
    /// </summary>
    public (float AlongMetres, float SideMetres) Measure(Vector3 up)
    {
        var side = MathF.Asin(Math.Clamp(Vector3.Dot(up, _normal), -1f, 1f));
        var q = up - _normal * Vector3.Dot(up, _normal);
        var along = MathF.Atan2(Vector3.Dot(q, _tangent), Vector3.Dot(q, A));
        return (along * _radius, side * _radius);
    }

    /// <summary>The point on the centre line <paramref name="alongMetres"/> from <see cref="A"/> (a unit direction).</summary>
    public Vector3 PointAlong(float alongMetres)
    {
        var a = alongMetres / _radius;
        return Vector3.Normalize(A * MathF.Cos(a) + _tangent * MathF.Sin(a));
    }

    /// <summary>The unit tangent along the bridge (A → B) at <paramref name="up"/> (on or near it).</summary>
    public Vector3 DirectionAt(Vector3 up)
    {
        var t = Vector3.Cross(_normal, up);
        return t.LengthSquared() > 1e-10f ? Vector3.Normalize(t) : _tangent;
    }

    /// <summary>The half-width at <paramref name="alongMetres"/> on one side (left when <paramref name="left"/>), metres: the guaranteed half-width plus the rag (0..<see cref="RagMetres"/>).</summary>
    public float HalfWidthAt(float alongMetres, bool left)
    {
        var p0 = left ? _phases[0] : _phases[2];
        var p1 = left ? _phases[1] : _phases[3];
        var w = 1f + 0.6f * ShellLand.Triangle(alongMetres * 2 * MathF.PI / RagLongMetres + p0) + 0.4f * ShellLand.Triangle(alongMetres * 2 * MathF.PI / RagShortMetres + p1);
        return HalfWidthMetres + RagMetres * 0.5f * w;
    }

    /// <summary>
    /// How far <paramref name="up"/> is inside the bridge, metres: positive on it, negative off it (to the side, past an
    /// end, or in a gap). Past its ends the strip is rounded off (its ends lie inside lands anyway).
    /// </summary>
    public float InsideMetres(Vector3 up)
    {
        var (along, side) = Measure(up);
        float inside;
        if (along < 0) inside = HalfWidthMetres - PlanetShape.Angle(up, A) * _radius;
        else if (along > LengthMetres) inside = HalfWidthMetres - PlanetShape.Angle(up, B) * _radius;
        else inside = HalfWidthAt(along, side > 0) - MathF.Abs(side);
        foreach (var (g0, g1) in Gaps) inside = MathF.Min(inside, MathF.Max(g0 - along, along - g1));
        return inside;
    }
}

/// <summary>
/// A link of a Hazard World's route (AD-59): bridge <see cref="Span"/> joins the waypoints of lands <see cref="From"/> and
/// <see cref="To"/>; <see cref="Jump"/> when it has a gap to jump.
/// </summary>
public readonly record struct ShellLink(int From, int To, int Span, bool Jump);

/// <summary>One step of a route: the next waypoint to walk to, and whether the way to it crosses a jump gap.</summary>
public readonly record struct RouteStep(Vector3 Point, bool Jump);

/// <summary>
/// A Hazard World's broken shell (PLANET-34, PLANET-46; AD-53 amended by AD-59): what is left of the planet's ground over
/// a black hole at the centre. Solid ground is the union of <see cref="Lands"/> (jagged islands carrying the arrival, the
/// launchers, the landmark, the files and outcrops) and <see cref="Spans"/> (narrow bridges between them, some with a gap
/// to jump); everything else is open: walking off it, you fall. <see cref="PlanetSurface"/> carries it (the walker finds no
/// ground off it), the app draws the ground, rims and glow from <see cref="EdgeMetres"/>, and hops and probes follow the
/// route (<see cref="Route"/>). <c>HazardTerrain.Design</c> makes it around the destinations, so every launcher and the
/// way back are reachable from the arrival.
/// <para>
/// Planet frame, metres; the shell's thickness is drawn (<see cref="CrustMetres"/>), not walked: falling starts as soon as
/// there is no ground under the walker's centre. Immutable: safe to read from the app's ground worker.
/// </para>
/// </summary>
public sealed class BrokenShell
{
    /// <summary>The crust's drawn thickness, metres: the depth of the broken rim and of the shell's underside.</summary>
    public const float CrustMetres = 1.6f;

    /// <summary>The black hole's event horizon (the dark core) as a fraction of the planet's radius.</summary>
    public const float HorizonRadiusFraction = 0.1f;

    /// <summary>A crossed gap adds this much to a route's length when choosing between routes, metres: walking is preferred to jumping.</summary>
    private const float JumpPenaltyMetres = 8f;

    public BrokenShell(IReadOnlyList<ShellLand> lands, IReadOnlyList<Vector3> waypoints, IReadOnlyList<ShellSpan> spans,
        IReadOnlyList<ShellLink> links, Vector3 jetAxis, float radius)
    {
        if (waypoints.Count != lands.Count) throw new ArgumentException("one waypoint per land", nameof(waypoints));
        Lands = lands;
        Waypoints = waypoints;
        Spans = spans;
        Links = links;
        JetAxis = jetAxis;
        Radius = radius;
    }

    /// <summary>The islands of solid ground. Land 0 carries the arrival.</summary>
    public IReadOnlyList<ShellLand> Lands { get; }

    /// <summary>The route's waypoint on each land (same index): where its bridges meet.</summary>
    public IReadOnlyList<Vector3> Waypoints { get; }

    /// <summary>The bridges and ridges.</summary>
    public IReadOnlyList<ShellSpan> Spans { get; }

    /// <summary>The route: which bridge joins which lands.</summary>
    public IReadOnlyList<ShellLink> Links { get; }

    /// <summary>The black hole's spin axis (unit, planet frame): its jets leave along ±this through open sky, and its disc lies across it.</summary>
    public Vector3 JetAxis { get; }

    /// <summary>The planet's base radius, metres (for distances along the surface).</summary>
    public float Radius { get; }

    /// <summary>True when <paramref name="up"/> is over open space: there is no ground there.</summary>
    public bool OverHole(Vector3 up)
    {
        foreach (var l in Lands)
        {
            if (l.Contains(up)) return false;
        }

        foreach (var s in Spans)
        {
            if (PlanetShape.Angle(up, s.Mid) > s.BoundRadians) continue;
            if (s.InsideMetres(up) > 0) return false;
        }

        return true;
    }

    /// <summary>
    /// Metres along the surface from <paramref name="up"/> to the nearest edge: positive on solid ground (how far inside),
    /// negative over open space (how far out), measured per land along its bearing and per bridge across it. Where pieces
    /// overlap, the value inside is a lower bound (the warning comes a little early); outside it is the distance to the
    /// nearest piece. For the edge warning, the art and the probes.
    /// </summary>
    public float EdgeMetres(Vector3 up)
    {
        var best = float.NegativeInfinity;
        foreach (var l in Lands)
        {
            // A land whose farthest tooth is further than −best away cannot do better.
            var d = PlanetShape.Angle(up, l.Centre);
            if ((l.MaxRadiusRadians - d) * Radius <= best) continue;
            best = MathF.Max(best, l.InsideRadians(up) * Radius);
        }

        foreach (var s in Spans)
        {
            var d = PlanetShape.Angle(up, s.Mid);
            if ((s.BoundRadians - d) * Radius <= best) continue;
            best = MathF.Max(best, s.InsideMetres(up));
        }

        return best;
    }

    /// <summary>
    /// True when the great-circle arc a→b keeps at least <paramref name="clearanceMetres"/> inside solid ground all the way
    /// (sampled every half metre, so an opening narrower than that could slip through).
    /// </summary>
    public bool ArcClear(Vector3 a, Vector3 b, float clearanceMetres)
    {
        var angle = PlanetShape.Angle(a, b);
        var steps = Math.Max(1, (int)MathF.Ceiling(angle * Radius / 0.5f));
        var s = MathF.Sin(angle);
        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            var p = s < 1e-5f ? a : Vector3.Normalize(a * (MathF.Sin((1 - t) * angle) / s) + b * (MathF.Sin(t * angle) / s));
            if (EdgeMetres(p) < clearanceMetres) return false;
        }

        return true;
    }

    /// <summary>
    /// The share of the sphere that is solid ground (0..1), counted over <paramref name="samples"/> evenly spread points.
    /// </summary>
    public float WalkableFraction(int samples = 4000)
    {
        var points = PlanetComposer.FibonacciSphere(samples);
        var solid = 0;
        foreach (var p in points)
        {
            if (!OverHole(p)) solid++;
        }

        return solid / (float)points.Length;
    }

    /// <summary>
    /// The way to walk from <paramref name="from"/> to <paramref name="to"/> (both on solid ground) along the route: the
    /// waypoints to head for in turn (not including <paramref name="from"/>; the last is <paramref name="to"/>), each with
    /// whether the way to it crosses a jump gap. Walking straight to each in turn keeps to solid ground: inside a land the
    /// way runs through its waypoint, between lands along the bridges' centre lines. Prefers walking to jumping
    /// (<see cref="JumpPenaltyMetres"/>). From or to a point off the shell, the nearest waypoint stands in for it.
    /// </summary>
    public IReadOnlyList<RouteStep> Route(Vector3 from, Vector3 to)
    {
        var starts = Entries(from);
        var goals = Entries(to);
        var n = Lands.Count;
        // Same land (and not on a bridge): via its waypoint, which is safe in a star-shaped land.
        foreach (var (node, _, jumpS) in starts)
        {
            foreach (var (g, _, jumpG) in goals)
            {
                if (node == g && !jumpS && !jumpG && Lands[node].Contains(from) && Lands[node].Contains(to))
                {
                    return ArcClear(from, to, 0.6f) ? [new RouteStep(to, false)] : [new RouteStep(Waypoints[node], false), new RouteStep(to, false)];
                }
            }
        }

        // Dijkstra over the lands from every entry point.
        var dist = new float[n];
        var prev = new int[n];
        var prevLink = new int[n];
        var startJump = new bool[n];
        Array.Fill(dist, float.PositiveInfinity);
        Array.Fill(prev, -1);
        Array.Fill(prevLink, -1);
        var queue = new PriorityQueue<int, float>();
        foreach (var (node, cost, jump) in starts)
        {
            if (cost < dist[node]) { dist[node] = cost; startJump[node] = jump; queue.Enqueue(node, cost); }
        }

        while (queue.TryDequeue(out var u, out var du))
        {
            if (du > dist[u]) continue;
            for (var k = 0; k < Links.Count; k++)
            {
                var l = Links[k];
                if (l.From != u && l.To != u) continue;
                var v = l.From == u ? l.To : l.From;
                var dv = du + Spans[l.Span].LengthMetres + (l.Jump ? JumpPenaltyMetres : 0);
                if (dv < dist[v]) { dist[v] = dv; prev[v] = u; prevLink[v] = k; queue.Enqueue(v, dv); }
            }
        }

        var bestGoal = -1;
        var bestCost = float.PositiveInfinity;
        var bestGoalJump = false;
        foreach (var (g, cost, jump) in goals)
        {
            if (dist[g] + cost < bestCost) { bestCost = dist[g] + cost; bestGoal = g; bestGoalJump = jump; }
        }

        if (bestGoal < 0) return [new RouteStep(to, false)];
        var chain = new List<int>();
        for (var v = bestGoal; v >= 0; v = prev[v]) chain.Add(v);
        chain.Reverse();
        var steps = new List<RouteStep> { new(Waypoints[chain[0]], startJump[chain[0]]) };
        for (var i = 1; i < chain.Count; i++) steps.Add(new RouteStep(Waypoints[chain[i]], Links[prevLink[chain[i]]].Jump));
        steps.Add(new RouteStep(to, bestGoalJump));
        // Drop a first waypoint we are already standing on.
        if (steps.Count > 1 && PlanetShape.Angle(from, steps[0].Point) * Radius < 0.3f && !steps[0].Jump) steps.RemoveAt(0);
        return steps;
    }

    /// <summary>
    /// The route's entry points for a position: the land it stands on (to its waypoint), or the two ends of the bridge it
    /// stands on (with whether a gap lies between); off the shell, the nearest waypoint. Costs are metres.
    /// </summary>
    private List<(int Node, float Cost, bool Jump)> Entries(Vector3 up)
    {
        var list = new List<(int, float, bool)>();
        for (var i = 0; i < Lands.Count; i++)
        {
            if (Lands[i].Contains(up)) list.Add((i, PlanetShape.Angle(up, Waypoints[i]) * Radius, false));
        }

        foreach (var l in Links)
        {
            var s = Spans[l.Span];
            if (PlanetShape.Angle(up, s.Mid) > s.BoundRadians || s.InsideMetres(up) <= 0) continue;
            var (along, _) = s.Measure(up);
            var gapBefore = s.Gaps.Any(g => g.ToMetres <= along);
            var gapAfter = s.Gaps.Any(g => g.FromMetres >= along);
            list.Add((l.From, along + (gapBefore ? JumpPenaltyMetres : 0), gapBefore));
            list.Add((l.To, s.LengthMetres - along + (gapAfter ? JumpPenaltyMetres : 0), gapAfter));
        }

        if (list.Count == 0 && Lands.Count > 0)
        {
            var best = 0;
            for (var i = 1; i < Waypoints.Count; i++)
            {
                if (PlanetShape.Angle(up, Waypoints[i]) < PlanetShape.Angle(up, Waypoints[best])) best = i;
            }

            list.Add((best, PlanetShape.Angle(up, Waypoints[best]) * Radius, false));
        }

        return list;
    }
}
