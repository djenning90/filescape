using System.Numerics;

namespace FileScape.Core.Planets;

/// <summary>
/// One hole broken out of a hazard world's shell (PLANET-34, AD-53): a jagged cap round <see cref="Centre"/>. Its edge is
/// <see cref="BoundaryRadians"/>, an angle from the centre that varies with the bearing round it: the base angle
/// <see cref="RadiusRadians"/> shaped by a few broad lobes and two sets of sharp teeth (triangle waves), with phases from
/// <see cref="Seed"/>. The walker (<see cref="PlanetSurface.SupportAt"/>), the app's mesh (<c>PlanetArt.Hazard</c>) and
/// the tests all read this one function, so what you see is where you fall.
/// <para>
/// Planet frame (docs/units.md): directions are unit vectors from the planet centre, angles radians. The bearing θ round
/// the centre is measured in the tangent frame <see cref="PlanetShape.Tangents"/> gives the centre (θ = 0 along its A).
/// </para>
/// </summary>
/// <param name="Centre">Unit direction of the hole's centre.</param>
/// <param name="RadiusRadians">The base angular radius; the edge stays within ±<see cref="Wobble"/> of it.</param>
/// <param name="Seed">Chooses the edge's phases (deterministic per world and hole).</param>
public sealed class ShellHole(Vector3 centre, float radiusRadians, int seed)
{
    /// <summary>Unit direction of the hole's centre.</summary>
    public Vector3 Centre { get; } = centre;

    /// <summary>The base angular radius, radians.</summary>
    public float RadiusRadians { get; } = radiusRadians;

    /// <summary>Chooses the edge's phases.</summary>
    public int Seed { get; } = seed;

    // ---- the edge profile: r(θ) = RadiusRadians × (1 + Σ amplitude × wave(frequency θ + phase)). Lobes are sines (broad
    // bites out of the rim), teeth are triangle waves (sharp points: the "broken" look). Their amplitudes sum to Wobble, so
    // the edge stays between (1 − Wobble) and (1 + Wobble) times the base radius. Chosen by eye (2026-11-15).

    /// <summary>Broad lobes: 2, 3 and 5 per turn, at these fractions of the base radius.</summary>
    private static readonly (int Frequency, float Amplitude)[] Lobes = [(2, 0.10f), (3, 0.08f), (5, 0.05f)];

    /// <summary>Teeth: 13 large and 29 small per turn (odd and coprime with the lobes, so the pattern never repeats round the rim).</summary>
    private static readonly (int Frequency, float Amplitude)[] Teeth = [(13, 0.07f), (29, 0.03f)];

    /// <summary>The largest relative excursion of the edge from the base radius (the sum of every amplitude: 0.33).</summary>
    public const float Wobble = 0.10f + 0.08f + 0.05f + 0.07f + 0.03f;

    /// <summary>One phase per lobe and tooth set, radians, drawn from the seed (a class, not a record: equality by
    /// value would compare this array by reference).</summary>
    private readonly float[] _phases = MakePhases(seed);

    private static float[] MakePhases(int seed)
    {
        var rng = new Random(seed);
        var p = new float[Lobes.Length + Teeth.Length];
        for (var i = 0; i < p.Length; i++) p[i] = (float)(rng.NextDouble() * 2 * Math.PI);
        return p;
    }

    /// <summary>The largest angle from the centre the edge reaches, radians: every safety margin is measured from this.</summary>
    public float MaxRadiusRadians => RadiusRadians * (1 + Wobble);

    /// <summary>The smallest angle from the centre the edge comes to, radians.</summary>
    public float MinRadiusRadians => RadiusRadians * (1 - Wobble);

    /// <summary>The edge's angle from the centre at bearing <paramref name="thetaRadians"/>, radians.</summary>
    public float BoundaryRadians(float thetaRadians)
    {
        var ph = _phases;
        var f = 1f;
        for (var i = 0; i < Lobes.Length; i++) f += Lobes[i].Amplitude * MathF.Sin(Lobes[i].Frequency * thetaRadians + ph[i]);
        for (var i = 0; i < Teeth.Length; i++) f += Teeth[i].Amplitude * Triangle(Teeth[i].Frequency * thetaRadians + ph[Lobes.Length + i]);
        return RadiusRadians * f;
    }

    /// <summary>A triangle wave of period 2π in [−1, 1] (−1 at 0, 1 at π): straight flanks and sharp points.</summary>
    private static float Triangle(float x)
    {
        var t = x / (2 * MathF.PI);
        t -= MathF.Floor(t);
        return 4 * MathF.Abs(t - 0.5f) - 1;
    }

    /// <summary>The bearing of <paramref name="up"/> round the centre, radians (0 for the centre itself).</summary>
    public float BearingOf(Vector3 up)
    {
        var (a, b) = PlanetShape.Tangents(Centre);
        return MathF.Atan2(Vector3.Dot(up, b), Vector3.Dot(up, a));
    }

    /// <summary>
    /// How far <paramref name="up"/> is outside the edge, radians, measured along its bearing from the centre: positive on
    /// solid ground, negative over the hole. Along a jagged rim this is the radial distance to the edge, which near a
    /// tooth can be more than the true distance; margins use <see cref="MaxRadiusRadians"/> instead.
    /// </summary>
    public float OutsideRadians(Vector3 up) => PlanetShape.Angle(up, Centre) - BoundaryRadians(BearingOf(up));

    /// <summary>True when <paramref name="up"/> is over the hole (no ground).</summary>
    public bool Contains(Vector3 up)
    {
        var d = PlanetShape.Angle(up, Centre);
        if (d >= MaxRadiusRadians) return false; // the cheap test first: beyond the farthest tooth
        if (d < MinRadiusRadians) return true;
        return d < BoundaryRadians(BearingOf(up));
    }

    /// <summary>The point of the edge at bearing <paramref name="thetaRadians"/>: a unit direction.</summary>
    public Vector3 BoundaryPoint(float thetaRadians)
    {
        var (a, b) = PlanetShape.Tangents(Centre);
        var r = BoundaryRadians(thetaRadians);
        var t = a * MathF.Cos(thetaRadians) + b * MathF.Sin(thetaRadians);
        return Vector3.Normalize(Centre * MathF.Cos(r) + t * MathF.Sin(r));
    }
}

/// <summary>
/// A hazard world's broken shell (PLANET-34, AD-53): the planet's ground with <see cref="Holes"/> broken out of it, over a
/// black hole at the centre. <see cref="PlanetSurface"/> carries it, so the walker finds no ground over a hole and falls;
/// the app draws the rim, the crust and the black hole from it; <see cref="Carve"/> makes it so that the arrival, every
/// launcher, the routes between them and the rings keep at least <see cref="SafeMarginMetres"/> of solid ground to the
/// nearest edge (the regular route is forgiving; the danger is off it).
/// <para>Planet frame, metres; the shell's thickness is drawn (<see cref="CrustMetres"/>), not walked: falling starts
/// as soon as there is no ground under the walker's centre.</para>
/// </summary>
/// <param name="Holes">The holes (they never overlap: <see cref="IsthmusMetres"/> of ground between any two).</param>
/// <param name="Radius">The planet's base radius, metres (for distances along the surface).</param>
public sealed record BrokenShell(IReadOnlyList<ShellHole> Holes, float Radius)
{
    /// <summary>The least solid ground between an edge's farthest tooth and the arrival meadow, a launcher's clear area, a route or a ring, metres (PLANET-34: a forgiving route).</summary>
    public const float SafeMarginMetres = 3f;

    /// <summary>The least ground between two holes' farthest teeth, metres: a walkable bridge (wider than a body, 0.7 m, with room to spare).</summary>
    public const float IsthmusMetres = 2.5f;

    /// <summary>The crust's drawn thickness, metres: the depth of the broken rim and of the shell's underside.</summary>
    public const float CrustMetres = 1.6f;

    /// <summary>The black hole's event horizon (the dark core) as a fraction of the planet's radius.</summary>
    public const float HorizonRadiusFraction = 0.1f;

    /// <summary>
    /// The main breach: centred this far from the arrival pole, degrees (near the antipode, so it is the farthest thing from
    /// where you land), turned about the pole by the world's seed, with this base radius, radians (≈ 36°: about a tenth of
    /// the sphere before the teeth).
    /// </summary>
    private const float MainBreachPolarDeg = 168f, MainBreachRadians = 0.62f;

    /// <summary>
    /// Side breaches: up to <see cref="MaxSideBreaches"/>, centred this far from the pole, degrees (past the equator,
    /// below the launcher rings), with this base radius, radians (≈ 17°).
    /// </summary>
    private const float SideBreachPolarDeg = 100f, SideBreachRadians = 0.3f;

    private const int MaxSideBreaches = 3;

    /// <summary>A hole that does not fit shrinks by this factor and tries again, down to <see cref="MinHoleRadians"/> (≈ 8°), below which it is left out.</summary>
    private const float ShrinkFactor = 0.88f, MinHoleRadians = 0.14f;

    /// <summary>True when <paramref name="up"/> is over a hole: there is no ground there.</summary>
    public bool OverHole(Vector3 up)
    {
        foreach (var h in Holes)
        {
            if (h.Contains(up)) return true;
        }

        return false;
    }

    /// <summary>
    /// Metres along the surface from <paramref name="up"/> to the nearest edge, along that hole's bearing (see
    /// <see cref="ShellHole.OutsideRadians"/>): positive on solid ground, negative over a hole; +∞ with no holes. For the
    /// edge warning and the probes, not for safety margins (those use the holes' farthest teeth).
    /// </summary>
    public float EdgeMetres(Vector3 up)
    {
        var best = float.PositiveInfinity;
        foreach (var h in Holes)
        {
            // Beyond the farthest tooth plus the current best, this hole cannot be nearer.
            var d = PlanetShape.Angle(up, h.Centre);
            if ((d - h.MaxRadiusRadians) * Radius >= best) continue;
            best = MathF.Min(best, h.OutsideRadians(up) * Radius);
        }

        return best;
    }

    /// <summary>
    /// True when the great-circle arc a→b keeps at least <paramref name="clearanceMetres"/> from every edge (sampled every
    /// half metre, so a gap narrower than that could slip through; holes are metres across). Hops use it to go round
    /// holes rather than across them.
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
    /// Breaks a shell for a planet of <paramref name="radius"/> metres, keeping clear of what the route needs: discs
    /// (direction, radius in metres: the meadow, launchers' clear areas, the landmark and the overlook), routes (great-circle
    /// arcs with a half-width) and rings (a polar angle with a half-width), each by <see cref="SafeMarginMetres"/> from the
    /// holes' farthest teeth.
    /// <para>
    /// The main breach near the antipode is tried first, then side breaches on bearings spread round the planet (ahead
    /// first, so the overlook looks towards one); each that does not fit shrinks (×<see cref="ShrinkFactor"/>) until it
    /// does or is too small to keep. Deterministic: everything comes from <paramref name="seed"/>.
    /// </para>
    /// </summary>
    public static BrokenShell Carve(float radius, int seed,
        IReadOnlyList<(Vector3 Direction, float RadiusMetres)> discs,
        IReadOnlyList<(Vector3 A, Vector3 B, float HalfWidthMetres)> routes,
        IReadOnlyList<(float PolarRadians, float HalfWidthMetres)> rings)
    {
        var holes = new List<ShellHole>();
        var rng = new Random(seed ^ 0x51A7E);

        bool Fits(Vector3 c, float maxRadians)
        {
            var reach = maxRadians * radius + SafeMarginMetres;
            foreach (var (d, r) in discs)
            {
                if (PlanetShape.Angle(c, d) * radius < reach + r) return false;
            }

            foreach (var (a, b, w) in routes)
            {
                if (PlanetFrame.DistanceToArc(c, a, b, radius) < reach + w) return false;
            }

            var polar = PlanetFrame.PolarOf(c);
            foreach (var (p, w) in rings)
            {
                if (MathF.Abs(polar - p) * radius < reach + w) return false;
            }

            foreach (var h in holes)
            {
                if (PlanetShape.Angle(c, h.Centre) * radius < (maxRadians + h.MaxRadiusRadians) * radius + IsthmusMetres) return false;
            }

            return true;
        }

        void TryAdd(Vector3 centre, float startRadians)
        {
            var holeSeed = rng.Next();
            for (var r = startRadians; r >= MinHoleRadians; r *= ShrinkFactor)
            {
                if (!Fits(centre, r * (1 + ShellHole.Wobble))) continue;
                holes.Add(new ShellHole(centre, r, holeSeed));
                return;
            }
        }

        var turn = (float)(rng.NextDouble() * 2 * Math.PI);
        TryAdd(PlanetFrame.FromPolar(MainBreachPolarDeg * MathF.PI / 180f, turn), MainBreachRadians);
        // Side breaches: ahead (beyond the landmark, seen from the overlook), then round to the sides and behind; each
        // nudged by up to ±15° so worlds differ.
        float[] bearingsDeg = [0f, 125f, 235f, 60f, 180f, 300f];
        var added = 0;
        foreach (var b in bearingsDeg)
        {
            if (added >= MaxSideBreaches) break;
            var az = (b + ((float)rng.NextDouble() - 0.5f) * 30f) * MathF.PI / 180f;
            var before = holes.Count;
            TryAdd(PlanetFrame.FromPolar(SideBreachPolarDeg * MathF.PI / 180f, az), SideBreachRadians);
            if (holes.Count > before) added++;
        }

        return new BrokenShell(holes, radius);
    }
}
