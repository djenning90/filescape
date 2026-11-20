using System.Numerics;

namespace FileScape.Core.Planets;

/// <summary>
/// One piece of solid ground on a Hazard World's shell (PLANET-34, PLANET-46, PLANET-63; AD-53, AD-59, AD-65): a jagged cap
/// round <see cref="Centre"/> — the arrival, a launcher's pad, the landmark and overlook, a file plateau, an outcrop, a
/// fragment broken off a bigger island, or a lip grown towards a neighbour. Its edge is <see cref="BoundaryRadians"/>, an
/// angle from the centre that varies with the bearing round it: the base angle <see cref="RadiusRadians"/> shaped by broad
/// lobes and three sets of sharp teeth (triangle waves), each with a phase and a strength (0.45–1 of its most) drawn from
/// <see cref="Seed"/>, all scaled by <see cref="Roughness"/>, so no two islands share an outline (DJ, 2026-11-17: "the
/// broken ground still looks a little like a graph of pads and bridges"). A land is star-shaped about its centre (every
/// point on the straight way from the centre to a point inside is inside), which the route relies on.
/// <para>
/// Planet frame (docs/units.md): directions are unit vectors from the planet centre, angles radians. The bearing θ round
/// the centre is measured in the tangent frame <see cref="PlanetShape.Tangents"/> gives the centre (θ = 0 along its A).
/// The walker, the app's mesh and the tests all read <see cref="BrokenShell"/>, which reads this, so what you see is
/// where you fall. What a change must preserve: the edge stays within <see cref="MinRadiusRadians"/> and
/// <see cref="MaxRadiusRadians"/> (the terrain sizes lands by them), and the same seed gives the same outline.
/// </para>
/// </summary>
/// <param name="centre">Unit direction of the land's centre (also the route's waypoint on it, unless the shell says otherwise).</param>
/// <param name="radiusRadians">The base angular radius; the edge stays within ±<see cref="WobbleFor"/> × <see cref="Roughness"/> of it.</param>
/// <param name="seed">Chooses the edge's phases and strengths (deterministic per world and land).</param>
/// <param name="roughness">Scales the edge's lobes and teeth, 0..1 (1: the full strength).</param>
public sealed class ShellLand(Vector3 centre, float radiusRadians, int seed, float roughness = 1f, IReadOnlyList<ShellSpur>? spurs = null)
{
    /// <summary>Spurs grown out of the rim towards a neighbour (HazardTerrain: a gap that looked jumpable becomes a hop).</summary>
    public IReadOnlyList<ShellSpur> Spurs { get; } = spurs ?? [];

    private readonly float _spurMaxRadians = (spurs ?? []).Select(s => s.ExtraRadians).DefaultIfEmpty(0).Max();

    /// <summary>The same land with one more spur.</summary>
    public ShellLand WithSpur(ShellSpur spur) => new(Centre, RadiusRadians, Seed, Roughness, [.. Spurs, spur]);

    /// <summary>Unit direction of the land's centre.</summary>
    public Vector3 Centre { get; } = centre;

    /// <summary>The base angular radius, radians.</summary>
    public float RadiusRadians { get; } = radiusRadians;

    /// <summary>Chooses the edge's phases and strengths.</summary>
    public int Seed { get; } = seed;

    /// <summary>Scales the edge's lobes and teeth, 0..1.</summary>
    public float Roughness { get; } = Math.Clamp(roughness, 0f, 1f);

    // ---- the edge profile: r(θ) = RadiusRadians × (1 + Roughness × Σ strength × amplitude × wave(frequency θ + phase)).
    // Lobes are sines (broad bites out of the rim and bulges), teeth are triangle waves (sharp points: the "broken" look).
    // Amplitudes by eye: 2026-11-16 (PLANET-46) islands of 4–9 m read as broken rock with teeth of ≈ 0.2 m; 2026-11-17
    // (PLANET-63) stronger low lobes and a third, finer set of teeth, each at a seeded strength, so outlines differ.

    /// <summary>Broad lobes: 2, 3, 5 and 7 per turn, at most these fractions of the base radius.</summary>
    private static readonly (int Frequency, float Amplitude)[] Lobes = [(2, 0.09f), (3, 0.07f), (5, 0.045f), (7, 0.03f)];

    /// <summary>Teeth: 11, 17 and 29 per turn (odd and coprime with the lobes, so the pattern never repeats round the rim).</summary>
    private static readonly (int Frequency, float Amplitude)[] Teeth = [(11, 0.035f), (17, 0.02f), (29, 0.015f)];

    /// <summary>Each lobe and tooth set is drawn at this much of its amplitude at least (and at most all of it).</summary>
    private const float LeastStrength = 0.45f;

    /// <summary>The largest relative excursion any land's edge makes from its base radius at full roughness (every amplitude at full strength: 0.305).</summary>
    public const float Wobble = 0.09f + 0.07f + 0.045f + 0.03f + 0.035f + 0.02f + 0.015f;

    /// <summary>One phase per lobe and tooth set, radians, then one strength each (0.45..1), drawn from the seed.</summary>
    private readonly float[] _phases = MakePhases(seed);

    private readonly float _wobble = WobbleOf(MakePhases(seed));

    private readonly (Vector3 A, Vector3 B) _tangents = PlanetShape.Tangents(centre);

    private static float[] MakePhases(int seed)
    {
        var rng = new Random(seed);
        var n = Lobes.Length + Teeth.Length;
        var p = new float[2 * n];
        for (var i = 0; i < n; i++) p[i] = (float)(rng.NextDouble() * 2 * Math.PI);
        for (var i = 0; i < n; i++) p[n + i] = LeastStrength + (1 - LeastStrength) * (float)rng.NextDouble();
        return p;
    }

    private static float WobbleOf(float[] p)
    {
        var n = Lobes.Length + Teeth.Length;
        var w = 0f;
        for (var i = 0; i < Lobes.Length; i++) w += Lobes[i].Amplitude * p[n + i];
        for (var i = 0; i < Teeth.Length; i++) w += Teeth[i].Amplitude * p[n + Lobes.Length + i];
        return w;
    }

    /// <summary>The largest relative excursion of the edge of the land made from <paramref name="seed"/> at full roughness (≤ <see cref="Wobble"/>): lands are sized with it before they are made.</summary>
    public static float WobbleFor(int seed) => WobbleOf(MakePhases(seed));

    /// <summary>The farthest the edge reaches from the centre, radians.</summary>
    public float MaxRadiusRadians => RadiusRadians * (1 + _wobble * Roughness) + _spurMaxRadians;

    /// <summary>The nearest the edge comes to the centre, radians: whatever stands within it is on solid ground.</summary>
    public float MinRadiusRadians => RadiusRadians * (1 - _wobble * Roughness);

    /// <summary>The edge's angle from the centre at bearing <paramref name="thetaRadians"/>, radians.</summary>
    public float BoundaryRadians(float thetaRadians)
    {
        var ph = _phases;
        var n = Lobes.Length + Teeth.Length;
        var f = 0f;
        for (var i = 0; i < Lobes.Length; i++) f += ph[n + i] * Lobes[i].Amplitude * MathF.Sin(Lobes[i].Frequency * thetaRadians + ph[i]);
        for (var i = 0; i < Teeth.Length; i++) f += ph[n + Lobes.Length + i] * Teeth[i].Amplitude * Triangle(Teeth[i].Frequency * thetaRadians + ph[Lobes.Length + i]);
        var r = RadiusRadians * (1 + Roughness * f);
        foreach (var sp in Spurs) r += sp.ExtraRadians * ShellSpur.Bump(MathF.IEEERemainder(thetaRadians - sp.Bearing, 2 * MathF.PI) / sp.HalfWidth);
        return r;
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
        // The cheap tests first, on the cosine (no trigonometry): beyond the farthest tooth, or within the nearest.
        var c = Vector3.Dot(up, Centre);
        if (c < _cosMax) return false;
        if (c > _cosMin) return true;
        var d = PlanetShape.Angle(up, Centre);
        if (d >= MaxRadiusRadians) return false;
        if (d < MinRadiusRadians) return true;
        return d < BoundaryRadians(BearingOf(up));
    }

    // The cosines of the farthest and nearest reach, a hair outside each so the angle test decides at the boundary.
    private float _cosMax => _cosMaxCache ??= MathF.Cos(MathF.Min(MathF.PI, MaxRadiusRadians)) - 1e-6f;
    private float _cosMin => _cosMinCache ??= MathF.Cos(MathF.Max(0, MinRadiusRadians)) + 1e-6f;
    private float? _cosMaxCache, _cosMinCache;

    /// <summary>The point of the edge at bearing <paramref name="thetaRadians"/>: a unit direction.</summary>
    public Vector3 BoundaryPoint(float thetaRadians)
    {
        var r = BoundaryRadians(thetaRadians);
        var t = _tangents.A * MathF.Cos(thetaRadians) + _tangents.B * MathF.Sin(thetaRadians);
        return Vector3.Normalize(Centre * MathF.Cos(r) + t * MathF.Sin(r));
    }
}

/// <summary>
/// A spur grown out of a land's rim (PLANET-61, PLANET-63, AD-65): where two pieces of ground faced each other across open
/// sky too wide to hop and too narrow to read as impassable, each grows a tongue of rock towards the other until the gap
/// between them is a plain hop. <see cref="Bearing"/> is the bearing round the land's centre (radians),
/// <see cref="ExtraRadians"/> how much further the rim reaches there and <see cref="HalfWidth"/> the spur's half-width in
/// bearing (radians). The profile (<see cref="Bump"/>) is flat across its middle and falls off smoothly, so the tip is
/// broad enough to land on and the outline stays star-shaped. A bridge's spurs are <see cref="SpanSpur"/>.
/// </summary>
public readonly record struct ShellSpur(float Bearing, float ExtraRadians, float HalfWidth)
{
    /// <summary>The spur's flat middle: full height within this fraction of its half-width.</summary>
    private const float FlatFraction = 0.4f;

    /// <summary>The spur's profile at <paramref name="x"/> half-widths from its middle: 1 across the flat middle, a cosine fall to 0 at ±1.</summary>
    public static float Bump(float x)
    {
        var a = MathF.Abs(x);
        if (a >= 1) return 0;
        if (a <= FlatFraction) return 1;
        return 0.5f * (1 + MathF.Cos(MathF.PI * (a - FlatFraction) / (1 - FlatFraction)));
    }
}

/// <summary>
/// A spur grown out of a bridge's side (see <see cref="ShellSpur"/>): centred <see cref="AlongMetres"/> along it, on the
/// left (going A → B) when <see cref="Left"/>, widening that side by up to <see cref="ExtraMetres"/> over
/// ±<see cref="HalfLengthMetres"/> along it, metres.
/// </summary>
public readonly record struct SpanSpur(float AlongMetres, bool Left, float ExtraMetres, float HalfLengthMetres);

/// <summary>
/// What a jump gap asks of you (PLANET-61, AD-65). DJ (2026-11-17): "distinguish gaps a normal jump can clear, gaps that
/// reward an advanced move, and gaps that are there to steer me toward a bridge. Make that distinction readable from the
/// geometry and landing area." The third kind is not a gap on a bridge but the open sky between pieces of ground
/// (<see cref="HazardTerrain.SteeringMinMetres"/>).
/// </summary>
public enum ShellGapClass
{
    /// <summary>A step: well within a plain jump at walking pace, with a wide landing lip beyond it (<see cref="HazardTerrain.MaxJumpGapMetres"/>).</summary>
    Plain,

    /// <summary>
    /// A leap: beyond any plain jump at walking pace, cleared by a twirl at the top of the jump (or a running jump), with a
    /// marked take-off board and a wide landing ledge; only on an optional bridge (a shortcut), never on the only way.
    /// </summary>
    Advanced,
}

/// <summary>
/// A bridge or ridge of solid ground on a Hazard World (PLANET-46, PLANET-61, PLANET-63; AD-59, AD-65): a strip along the
/// great-circle arc from <see cref="A"/> to <see cref="B"/> (two waypoints, each inside a land), at least
/// <see cref="HalfWidthMetres"/> each side of its centre line, with ragged edges that only ever widen it (by up to
/// <see cref="RagMetres"/>), and with <see cref="Gaps"/>: stretches cut across it that you jump. A gap's ends are jagged
/// (they only ever lengthen it, by up to <see cref="GapJagMetres"/> at each end), and the bridge widens into a ledge for
/// <see cref="LedgeLengthMetres"/> on both sides of it (by <see cref="LedgeMetres"/>, more for an advanced gap): room to
/// take off and a generous place to land. Drops on both sides; the route walks its centre line.
/// <para>Planet frame, metres along the surface at the planet's base radius. What a change must preserve: the strip is at
/// least twice <see cref="HalfWidthMetres"/> wide outside its gaps, and a gap is at least its nominal length everywhere
/// across the bridge and at most that plus twice <see cref="GapJagMetres"/> (HazardWorldTests).</para>
/// </summary>
public sealed class ShellSpan
{
    /// <summary>The ragged edges stick out from the strip's guaranteed half-width by up to this much, metres (by eye; 0.3 m before PLANET-63).</summary>
    public const float RagMetres = 0.45f;

    /// <summary>The rag's three wavelengths along the bridge, metres (rationale: irregular at walking pace; by eye).</summary>
    private const float RagLongMetres = 3.1f, RagMidMetres = 1.3f, RagShortMetres = 0.55f;

    /// <summary>A gap's ends are jagged: each end of a plain gap reaches up to this much further across part of the bridge, metres (an advanced gap: half).</summary>
    public const float GapJagMetres = 0.2f;

    /// <summary>The jag's wavelength across the bridge, metres (two or three points across a 2 m bridge).</summary>
    private const float GapJagWavelengthMetres = 0.9f;

    /// <summary>Either side of a plain gap the bridge widens by this much each side, metres: a lip to land on.</summary>
    public const float LedgeMetres = 0.6f;

    /// <summary>Either side of an advanced gap it widens by this much each side, metres: the marked take-off board and a wide landing ledge.</summary>
    public const float AdvancedLedgeMetres = 1.0f;

    /// <summary>The ledge reaches this far from the gap's end along the bridge, metres, then tapers over <see cref="LedgeTaperMetres"/>.</summary>
    public const float LedgeLengthMetres = 1.8f, LedgeTaperMetres = 0.8f;

    private readonly Vector3 _normal, _tangent, _mid;
    private readonly float _radius, _boundRadians, _ledgeMetres, _jagMetres;
    private readonly float[] _phases;

    public ShellSpan(Vector3 a, Vector3 b, float halfWidthMetres, float radius, int seed, IReadOnlyList<(float FromMetres, float ToMetres)>? gaps = null,
        ShellGapClass gapClass = ShellGapClass.Plain, IReadOnlyList<SpanSpur>? spurs = null)
    {
        Spurs = spurs ?? [];
        A = a;
        B = b;
        HalfWidthMetres = halfWidthMetres;
        Seed = seed;
        Gaps = gaps ?? [];
        GapClass = gapClass;
        _radius = radius;
        _ledgeMetres = Gaps.Count == 0 ? 0 : gapClass == ShellGapClass.Advanced ? AdvancedLedgeMetres : LedgeMetres;
        _jagMetres = gapClass == ShellGapClass.Advanced ? GapJagMetres / 2 : GapJagMetres;
        var n = Vector3.Cross(a, b);
        _normal = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : PlanetShape.Tangents(a).A;
        _tangent = Vector3.Cross(_normal, a); // at A, along the arc towards B
        LengthMetres = PlanetShape.Angle(a, b) * radius;
        var mid = a + b;
        _mid = mid.LengthSquared() > 1e-8f ? Vector3.Normalize(mid) : a;
        _boundRadians = (LengthMetres / 2 + halfWidthMetres + RagMetres + _ledgeMetres + Spurs.Select(sp => sp.ExtraMetres).DefaultIfEmpty(0).Max() + 0.01f) / radius;
        var rng = new Random(seed);
        _phases = new float[10];
        for (var i = 0; i < _phases.Length; i++) _phases[i] = (float)(rng.NextDouble() * 6.283);
    }

    /// <summary>The waypoints at its ends (unit directions).</summary>
    public Vector3 A { get; }
    public Vector3 B { get; }

    /// <summary>The guaranteed half-width, metres: the strip is at least twice this wide everywhere outside a gap.</summary>
    public float HalfWidthMetres { get; }

    public int Seed { get; }

    /// <summary>The stretches cut across the bridge (jump gaps), metres along it from <see cref="A"/>: nominal, the shortest across it.</summary>
    public IReadOnlyList<(float FromMetres, float ToMetres)> Gaps { get; }

    /// <summary>What its gaps ask of you (meaningful only when it has one).</summary>
    public ShellGapClass GapClass { get; }

    /// <summary>Spurs grown out of its sides towards a neighbour (HazardTerrain).</summary>
    public IReadOnlyList<SpanSpur> Spurs { get; }

    /// <summary>The same bridge with one more spur.</summary>
    public ShellSpan WithSpur(SpanSpur spur) => new(A, B, HalfWidthMetres, _radius, Seed, Gaps, GapClass, [.. Spurs, spur]);

    /// <summary>The same bridge without its gaps (a gap that other ground covers is closed again).</summary>
    public ShellSpan Whole() => new(A, B, HalfWidthMetres, _radius, Seed, null, ShellGapClass.Plain, Spurs);

    /// <summary>Its length from waypoint to waypoint, metres.</summary>
    public float LengthMetres { get; }

    /// <summary>A cheap bound: the bridge lies within this angle of <see cref="Mid"/>, radians.</summary>
    public float BoundRadians => _boundRadians;

    /// <summary>The cosine of <see cref="BoundRadians"/> (a hair lower): a direction whose dot product with <see cref="Mid"/> is below it is off the bridge.</summary>
    public float CosBound => _cosBound ??= MathF.Cos(MathF.Min(MathF.PI, _boundRadians)) - 1e-6f;

    private float? _cosBound;

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

    /// <summary>The point <paramref name="sideMetres"/> to the left (negative: right) of the centre line at <paramref name="alongMetres"/> (a unit direction).</summary>
    public Vector3 PointAt(float alongMetres, float sideMetres)
    {
        var p = PointAlong(alongMetres);
        var s = sideMetres / _radius;
        return Vector3.Normalize(p * MathF.Cos(s) + _normal * MathF.Sin(s));
    }

    /// <summary>The unit tangent along the bridge (A → B) at <paramref name="up"/> (on or near it).</summary>
    public Vector3 DirectionAt(Vector3 up)
    {
        var t = Vector3.Cross(_normal, up);
        return t.LengthSquared() > 1e-10f ? Vector3.Normalize(t) : _tangent;
    }

    /// <summary>
    /// The half-width at <paramref name="alongMetres"/> on one side (left when <paramref name="left"/>), metres: the
    /// guaranteed half-width, plus the rag (0..<see cref="RagMetres"/>), plus the ledge near a gap.
    /// </summary>
    public float HalfWidthAt(float alongMetres, bool left)
    {
        var o = left ? 0 : 3;
        var w = 1f + 0.45f * ShellLand.Triangle(alongMetres * 2 * MathF.PI / RagLongMetres + _phases[o])
            + 0.35f * ShellLand.Triangle(alongMetres * 2 * MathF.PI / RagMidMetres + _phases[o + 1])
            + 0.2f * ShellLand.Triangle(alongMetres * 2 * MathF.PI / RagShortMetres + _phases[o + 2]);
        var spur = 0f;
        foreach (var sp in Spurs)
        {
            if (sp.Left == left) spur = MathF.Max(spur, sp.ExtraMetres * ShellSpur.Bump((alongMetres - sp.AlongMetres) / sp.HalfLengthMetres));
        }

        return HalfWidthMetres + RagMetres * 0.5f * w + LedgeAt(alongMetres) + spur;
    }

    /// <summary>How much the bridge widens at <paramref name="alongMetres"/> for a ledge beside a gap, metres (each side).</summary>
    public float LedgeAt(float alongMetres)
    {
        if (_ledgeMetres <= 0) return 0;
        var best = 0f;
        foreach (var (g0, g1) in Gaps)
        {
            var d = alongMetres < g0 ? g0 - alongMetres : alongMetres > g1 ? alongMetres - g1 : 0;
            var k = d <= LedgeLengthMetres ? 1 : 1 - Math.Clamp((d - LedgeLengthMetres) / LedgeTaperMetres, 0, 1);
            best = MathF.Max(best, k);
        }

        return _ledgeMetres * best;
    }

    /// <summary>How far an end of gap <paramref name="gap"/> (its start when <paramref name="start"/>) reaches beyond its nominal place at <paramref name="sideMetres"/> across the bridge, metres (0..the jag).</summary>
    public float GapJagAt(int gap, bool start, float sideMetres)
    {
        if (_jagMetres <= 0) return 0;
        var ph = _phases[6 + (start ? 0 : 1) + 2 * (gap % 2)];
        return _jagMetres * 0.5f * (1 + ShellLand.Triangle(sideMetres * 2 * MathF.PI / GapJagWavelengthMetres + ph));
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
        for (var i = 0; i < Gaps.Count; i++)
        {
            var (g0, g1) = Gaps[i];
            inside = MathF.Min(inside, MathF.Max(g0 - GapJagAt(i, true, side) - along, along - g1 - GapJagAt(i, false, side)));
        }

        return inside;
    }
}

/// <summary>
/// A link of a Hazard World's route (AD-59, AD-65): bridge <see cref="Span"/> joins the waypoints of lands <see cref="From"/>
/// and <see cref="To"/>; <see cref="Jump"/> when it has a gap to jump; <see cref="Advanced"/> when that gap is a leap
/// (<see cref="ShellGapClass.Advanced"/>): an optional shortcut the route never takes.
/// </summary>
public readonly record struct ShellLink(int From, int To, int Span, bool Jump, bool Advanced = false);

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
        _routeSpans = links.Select(l => l.Span).ToHashSet();
        _linked = new bool[lands.Count];
        if (lands.Count > 0) _linked[0] = true;
        foreach (var l in links)
        {
            if (l.From >= 0 && l.From < _linked.Length) _linked[l.From] = true;
            if (l.To >= 0 && l.To < _linked.Length) _linked[l.To] = true;
        }
    }

    /// <summary>
    /// The opening straight ahead of the arrival (AD-59): from this far from the arrival pole to this far, metres, within
    /// this azimuth of straight ahead, degrees; the black hole's window, kept open sky but for the ridge. Zero when unknown.
    /// </summary>
    public (float NearMetres, float FarMetres, float HalfDegrees) FrontOpening { get; init; }

    /// <summary>True when <paramref name="up"/> (with <paramref name="reachMetres"/> round it) lies in <see cref="FrontOpening"/>.</summary>
    public bool InFrontOpening(Vector3 up, float reachMetres = 0)
    {
        var (near, far, half) = FrontOpening;
        if (far <= 0) return false;
        var polar = PlanetFrame.PolarOf(up) * Radius;
        if (polar + reachMetres < near || polar - reachMetres > far) return false;
        var az = MathF.Abs(MathF.IEEERemainder(PlanetFrame.AzimuthOf(up), 2 * MathF.PI)) * 180f / MathF.PI;
        var ring = MathF.Max(1f, MathF.Sin(PlanetFrame.PolarOf(up)) * Radius);
        return az - reachMetres / ring * 180f / MathF.PI < half;
    }

    /// <summary>The same shell with other lands, bridges and links (the waypoints, jet, radius and opening kept).</summary>
    public BrokenShell With(IReadOnlyList<ShellLand>? lands = null, IReadOnlyList<ShellSpan>? spans = null, IReadOnlyList<ShellLink>? links = null) =>
        new(lands ?? Lands, Waypoints, spans ?? Spans, links ?? Links, JetAxis, Radius) { FrontOpening = FrontOpening };

    /// <summary>True when bridge <paramref name="span"/> is on the route (a link uses it): a rock arch is not, and carries no lit stripe.</summary>
    public bool OnRoute(int span) => _routeSpans.Contains(span);

    private readonly HashSet<int> _routeSpans;

    /// <summary>Per land: on the route (the arrival, or an end of a bridge). A fragment is not: it is reached by a hop.</summary>
    private readonly bool[] _linked;

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
    /// <summary>
    /// Is <paramref name="up"/> on a leap's take-off board, heading across its gap (PLANET-85, AD-75)? True on an
    /// advanced span within <paramref name="boardMetres"/> before either end of a gap no longer than
    /// <paramref name="maxGapMetres"/> (a gap the long jump clears, not an unjumpable stretch), on the bridge or its ledge,
    /// with <paramref name="heading"/> (a tangent; any length) within <paramref name="maxDegrees"/> of straight across
    /// the gap. The walker asks it of the ground it last stood on, so a press just after running off the board is the
    /// long jump's take-off, not the catch.
    /// </summary>
    public bool LeapTakeOff(Vector3 up, Vector3 heading, float boardMetres, float maxGapMetres, float maxDegrees)
    {
        if (heading.LengthSquared() < 1e-8f) return false;
        var h = Vector3.Normalize(heading);
        var cos = MathF.Cos(maxDegrees * MathF.PI / 180f);
        foreach (var s in Spans)
        {
            if (s.GapClass != ShellGapClass.Advanced || s.Gaps.Count == 0 || Vector3.Dot(up, s.Mid) < s.CosBound) continue;
            var (along, side) = s.Measure(up);
            if (MathF.Abs(side) > s.HalfWidthMetres + ShellSpan.AdvancedLedgeMetres + ShellSpan.RagMetres) continue;
            var across = Vector3.Dot(h, s.DirectionAt(up)); // + towards B
            foreach (var (g0, g1) in s.Gaps)
            {
                if (g1 - g0 > maxGapMetres) continue;
                if (along >= g0 - boardMetres && along <= g0 && across >= cos) return true;
                if (along <= g1 + boardMetres && along >= g1 && across <= -cos) return true;
            }
        }

        return false;
    }

    public bool OverHole(Vector3 up)
    {
        foreach (var l in Lands)
        {
            if (l.Contains(up)) return false;
        }

        foreach (var s in Spans)
        {
            if (Vector3.Dot(up, s.Mid) < s.CosBound) continue;
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

    /// <summary>Bearings searched round a point for <see cref="NearestGround"/> (every 11.25°: at 1.6 m, 0.3 m apart, about one edge chord).</summary>
    private const int GroundSearchBearings = 32;

    /// <summary>Distance step along each bearing for <see cref="NearestGround"/>, metres.</summary>
    private const float GroundSearchStepMetres = 0.1f;

    /// <summary>
    /// The nearest solid ground to <paramref name="up"/> (a point over open sky) within <paramref name="maxMetres"/>, for the
    /// recovery twirl (PLANET-62, AD-65): the unit direction of the first solid point found along the best bearing and its
    /// distance, metres; null when there is none in reach. Ground ahead along <paramref name="heading"/> (a tangent, or
    /// zero) counts as up to a third nearer, so a jump that falls short reaches for the far rim before the near one.
    /// </summary>
    public (Vector3 Point, float Metres)? NearestGround(Vector3 up, float maxMetres, Vector3 heading)
    {
        var (a, b) = PlanetShape.Tangents(up);
        var h = heading - up * Vector3.Dot(heading, up);
        var hasHeading = h.LengthSquared() > 1e-6f;
        if (hasHeading) h = Vector3.Normalize(h);
        (Vector3 Point, float Metres)? best = null;
        var bestScore = float.MaxValue;
        for (var i = 0; i < GroundSearchBearings; i++)
        {
            var th = 2 * MathF.PI * i / GroundSearchBearings;
            var dir = a * MathF.Cos(th) + b * MathF.Sin(th);
            var weight = hasHeading ? 1f - Vector3.Dot(dir, h) / 3f : 1f; // ahead 2/3, beside 1, behind 4/3
            for (var d = GroundSearchStepMetres; d <= maxMetres + 1e-4f; d += GroundSearchStepMetres)
            {
                if (d * weight >= bestScore) break;
                var p = PlanetFrame.Move(up, dir, d, Radius);
                if (OverHole(p)) continue;
                bestScore = d * weight;
                best = (p, d);
                break;
            }
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
                if (l.Advanced) continue; // a leap is a shortcut for those who can make it, never the route (AD-65)
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
            if (_linked[i] && Lands[i].Contains(up)) list.Add((i, PlanetShape.Angle(up, Waypoints[i]) * Radius, false));
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
                if (_linked[i] && PlanetShape.Angle(up, Waypoints[i]) < PlanetShape.Angle(up, Waypoints[best])) best = i;
            }

            list.Add((best, PlanetShape.Angle(up, Waypoints[best]) * Radius, false));
        }

        return list;
    }
}
