using System.Numerics;

namespace FileScape.Core.Planets;

/// <summary>
/// How a planet looks and feels, chosen from what its folder holds (PLANET-10). Identity of a place stays in the tree;
/// this is only a visual and audio treatment (AD-20: identity, layout and style are separate).
/// </summary>
public enum PlanetTheme
{
    /// <summary>The hub ("This computer") or a folder of folders: a cosy crossroads world.</summary>
    Hearth,

    /// <summary>Mostly images: a meadow of picture flowers.</summary>
    Meadow,

    /// <summary>A project or mostly code: a warm brass workshop.</summary>
    Workshop,

    /// <summary>Mostly documents: a library world of lecterns and paper.</summary>
    Library,

    /// <summary>Mostly audio: a world of bell flowers.</summary>
    Chime,

    /// <summary>Mostly video: a lantern theatre world.</summary>
    Theatre,

    /// <summary>Mostly archives or executables: a vault of chests and machines.</summary>
    Vault,

    /// <summary>Mostly data files: a crystal world.</summary>
    Crystal,

    /// <summary>A mix with no dominant kind: a wild garden.</summary>
    Wild,

    /// <summary>Listed and empty: a small quiet moon.</summary>
    Quiet,

    /// <summary>Not listed (yet), denied, unavailable or failed: a veiled world whose surface is unknown.</summary>
    Veiled,
}

/// <summary>
/// The solid shape of a planet in its own frame (centre at the origin, metres): a sphere of <see cref="Radius"/> with
/// gentle deterministic hills, flattened around <see cref="FlatSpots"/> (where launchers, the arrival meadow and file
/// groves stand) so footing there is clear. Walking, collision and the rendered mesh all use <see cref="HeightAt"/>,
/// so what you see is what you stand on.
/// </summary>
/// <param name="Radius">Base radius, metres.</param>
/// <param name="Seed">Chooses the hills (deterministic per folder).</param>
/// <param name="HillAmplitude">Tallest hill, metres (0 = a perfect sphere).</param>
/// <param name="FlatSpots">Unit directions and surface radii (metres) kept flat.</param>
public sealed record PlanetShape(float Radius, int Seed, float HillAmplitude, IReadOnlyList<(Vector3 Direction, float Radius)> FlatSpots)
{
    // ---- the hills: HillCount Gaussian bumps a·exp(−(angle/width)²) at random directions from Seed
    private const int HillCount = 7;

    /// <summary>A hill's width (its 1/e angular radius) is this plus up to <see cref="HillWidthSpreadRadians"/>: ≈ 20°…46°, broad swells rather than peaks.</summary>
    private const float HillWidthMinRadians = 0.35f;

    private const float HillWidthSpreadRadians = 0.45f;

    /// <summary>A hill's height is this fraction of <see cref="HillAmplitude"/> plus up to <see cref="HillAmplitudeSpreadFraction"/>.</summary>
    private const float HillAmplitudeMinFraction = 0.45f;

    private const float HillAmplitudeSpreadFraction = 0.55f;

    /// <summary>Every third hill (i % 3 == 2) is a hollow, half as deep as a hill would be high: gentle dips among the swells.</summary>
    private const float HollowFactor = -0.5f;

    /// <summary>
    /// Hills are ignored beyond this many widths from their centre: exp(−3.2²) = exp(−10.24) ≈ 4·10⁻⁵ of their height,
    /// under 0.05 mm for the tallest. Larger is exact to more digits and slower.
    /// </summary>
    private const float HillCutoffWidths = 3.2f;

    private (Vector3 Centre, float WidthRadians, float Amplitude)[]? _hills;

    /// <summary>A perfect sphere with no hills (tests and placeholders).</summary>
    public static PlanetShape Sphere(float radius) => new(radius, 0, 0, []);

    /// <summary>The hills, drawn once from <see cref="Seed"/> (the draw order is part of every planet's look: keep it).</summary>
    private (Vector3 Centre, float WidthRadians, float Amplitude)[] Hills
    {
        get
        {
            if (_hills != null) return _hills;
            var rng = new Random(Seed);
            var hills = new (Vector3, float, float)[HillAmplitude > 0 ? HillCount : 0];
            for (var i = 0; i < hills.Length; i++)
            {
                var d = RandomDirection(rng);
                var width = HillWidthMinRadians + (float)rng.NextDouble() * HillWidthSpreadRadians;
                var amp = HillAmplitude * (HillAmplitudeMinFraction + HillAmplitudeSpreadFraction * (float)rng.NextDouble()) * (i % 3 == 2 ? HollowFactor : 1f); // some gentle hollows
                hills[i] = (d, width, amp);
            }

            return _hills = hills;
        }
    }

    /// <summary>Terrain height above <see cref="Radius"/> at a unit direction, metres (smooth, |h| ≤ <see cref="HillAmplitude"/>).</summary>
    /// <remarks>
    /// Evaluated per vertex and per walker step, so the cheap test comes first: a hill or flat spot whose influence cannot
    /// reach this direction is skipped by one dot product against a precomputed cosine (2026-11-11: ground meshes took
    /// 60–140 ms to build before this).
    /// </remarks>
    public float HeightAt(Vector3 up)
    {
        if (HillAmplitude <= 0) return 0;
        var cache = Cache;
        var h = 0f;
        foreach (var (c, w, a, cosCut) in cache.Hills)
        {
            var d = Vector3.Dot(up, c);
            if (d < cosCut) continue; // beyond HillCutoffWidths (3.2) widths: exp(−10) is nothing
            var ang = MathF.Acos(Math.Clamp(d, -1f, 1f)) / w;
            h += a * MathF.Exp(-ang * ang);
        }

        if (h == 0) return 0;
        var mask = 1f;
        foreach (var (dir, r, cosInner, cosOuter) in cache.Flats)
        {
            var d = Vector3.Dot(up, dir);
            if (d < cosOuter) continue;
            if (d >= cosInner) return 0; // inside a flat spot
            var sMetres = MathF.Acos(Math.Clamp(d, -1f, 1f)) * Radius;
            mask = MathF.Min(mask, SmoothStep(r, r + FlatFadeMetres, sMetres));
        }

        return Math.Clamp(h, -HillAmplitude, HillAmplitude) * mask;
    }

    /// <summary>How far beyond a flat spot's radius the hills fade back in, metres.</summary>
    public const float FlatFadeMetres = 3.5f;

    /// <summary>The hills and flat spots with their precomputed cut-off cosines, and the flat-spot list they were built from.</summary>
    private sealed record ShapeCache(object FlatSource, (Vector3 C, float W, float A, float CosCut)[] Hills, (Vector3 Dir, float R, float CosInner, float CosOuter)[] Flats);

    private ShapeCache? _cache;

    /// <summary>Hills and flat spots with their cut-off cosines, rebuilt if <see cref="FlatSpots"/> is replaced (a record <c>with</c> copies fields).</summary>
    private ShapeCache Cache
    {
        get
        {
            if (_cache != null && ReferenceEquals(_cache.FlatSource, FlatSpots)) return _cache;
            var hills = Hills.Select(hh => (hh.Centre, hh.WidthRadians, hh.Amplitude, MathF.Cos(MathF.Min(MathF.PI, hh.WidthRadians * HillCutoffWidths)))).ToArray();
            var flats = FlatSpots.Select(f => (f.Direction, f.Radius, MathF.Cos(MathF.Min(MathF.PI, f.Radius / Radius)), MathF.Cos(MathF.Min(MathF.PI, (f.Radius + FlatFadeMetres) / Radius)))).ToArray();
            return _cache = new ShapeCache(FlatSpots, hills, flats);
        }
    }

    /// <summary>Distance from the centre to the ground at a unit direction, metres.</summary>
    public float GroundRadius(Vector3 up) => Radius + HeightAt(up);

    /// <summary>Outward surface normal at a unit direction (finite differences on the height field).</summary>
    public Vector3 Normal(Vector3 up)
    {
        if (HillAmplitude <= 0) return up;
        var (a, b) = Tangents(up);
        const float e = 0.01f; // ≈ 0.01 rad steps (0.1–0.35 m on these planets): small against a hill's width, large against float error
        var p = up * GroundRadius(up);
        var pa = Vector3.Normalize(up + a * e);
        var pb = Vector3.Normalize(up + b * e);
        var n = Vector3.Cross(pa * GroundRadius(pa) - p, pb * GroundRadius(pb) - p);
        n = Vector3.Normalize(n);
        return Vector3.Dot(n, up) < 0 ? -n : n;
    }

    /// <summary>Angle between two unit vectors, radians (robust near 0 and π).</summary>
    public static float Angle(Vector3 a, Vector3 b) => MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b));

    /// <summary>Two unit tangents perpendicular to <paramref name="up"/> and each other (any stable choice).</summary>
    public static (Vector3 A, Vector3 B) Tangents(Vector3 up)
    {
        // Y as the reference axis unless up is within ≈ 26° of it (|up.Y| ≥ 0.9), where the cross product gets short and
        // its direction unstable; then X.
        var refAxis = MathF.Abs(up.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var a = Vector3.Normalize(Vector3.Cross(refAxis, up));
        return (a, Vector3.Cross(up, a));
    }

    /// <summary>A deterministic, uniformly distributed unit direction.</summary>
    public static Vector3 RandomDirection(Random rng)
    {
        var z = (float)(rng.NextDouble() * 2 - 1);
        var t = (float)(rng.NextDouble() * Math.PI * 2);
        var s = MathF.Sqrt(1 - z * z);
        return new Vector3(s * MathF.Cos(t), z, s * MathF.Sin(t));
    }

    private static float SmoothStep(float e0, float e1, float x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>
/// A round solid standing on a planet's ground: a cylinder about the local up at <see cref="Direction"/>, from the
/// ground to <see cref="Top"/> above it. It blocks walking unless its top is low enough to step onto; its top can be
/// stood on (stepping stones, the overlook, launcher pads, a file object's plinth).
/// </summary>
/// <param name="Direction">Unit direction of its axis from the planet centre.</param>
/// <param name="Radius">Footprint radius measured along the surface, metres.</param>
/// <param name="Top">Height of its top above the local ground, metres.</param>
/// <param name="Id">The tree node it stands for, or −1 (scenery, steps).</param>
public readonly record struct SurfaceObstacle(Vector3 Direction, float Radius, float Top, int Id = -1);

/// <summary>
/// The walkable world of one planet: its <see cref="PlanetShape"/> and the solids on it (AD-39). Everything is in the
/// planet's frame (centre at the origin). Directions are unit vectors; distances along the surface are measured at the
/// base radius (angle × <see cref="PlanetShape.Radius"/>), which is how footprints are defined, so collision matches
/// what is drawn anywhere on the sphere (no chart, no seam, no distortion: unlike the 2026-11-06 experiment's chart,
/// this walks the whole planet).
/// </summary>
public sealed class PlanetSurface
{
    /// <summary>
    /// The height <see cref="SupportAt"/> reports over a hole in a <see cref="Shell"/>, metres: no ground at all. Far below
    /// anything a fall reaches before the app takes over (17 m/s² for 3 s is ≈ 77 m), and finite, so the walker's
    /// arithmetic stays ordinary.
    /// </summary>
    public const float NoGroundMetres = -1000f;

    /// <summary>
    /// On a broken shell, feet this far below the ground (metres) have fallen past the rim: there is no ground for them
    /// anywhere (see <see cref="SupportAt"/>), and the app treats the fall as committed. 0.6 m takes ≈ 0.27 s to fall at
    /// 17 m/s², so the coyote time (0.12 s) and a twirl just after stepping off can still save you.
    /// </summary>
    public const float FallThroughMetres = 0.6f;

    public PlanetSurface(PlanetShape shape, IReadOnlyList<SurfaceObstacle> obstacles, BrokenShell? shell = null)
    {
        Shape = shape;
        Obstacles = obstacles;
        Shell = shell is { Holes.Count: > 0 } ? shell : null;
    }

    public PlanetShape Shape { get; }
    public IReadOnlyList<SurfaceObstacle> Obstacles { get; }

    /// <summary>A hazard world's broken shell (PLANET-34): where there is no ground. Null for a whole planet.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BrokenShell? Shell { get; }

    public float Radius => Shape.Radius;

    /// <summary>Distance along the surface between two unit directions, metres (at the base radius).</summary>
    public float SurfaceDistance(Vector3 a, Vector3 b) => PlanetShape.Angle(a, b) * Shape.Radius;

    /// <summary>Indices of obstacles whose footprint comes within <paramref name="metres"/> of <paramref name="up"/>.</summary>
    public IEnumerable<int> Near(Vector3 up, float metres)
    {
        for (var i = 0; i < Obstacles.Count; i++)
        {
            if (SurfaceDistance(up, Obstacles[i].Direction) < Obstacles[i].Radius + metres) yield return i;
        }
    }

    /// <summary>
    /// The height above the local ground a walker of <paramref name="bodyRadius"/> stands at: the highest top under its
    /// disc that is no higher than <paramref name="feetHeight"/> + <paramref name="allowance"/> (0 = the ground).
    /// </summary>
    /// <remarks>Over a hole in the <see cref="Shell"/> (the walker's centre past the edge) the ground is
    /// <see cref="NoGroundMetres"/>: walking on, the walker leaves the ground and falls, with nothing to land on.</remarks>
    public float SupportAt(Vector3 up, float bodyRadius, float feetHeight, float allowance)
    {
        // Fallen more than FallThroughMetres below the rim, the walker is inside the crust's thickness: drifting back over
        // solid ground it must not pop up through it (2026-11-15: a fall that skimmed back under the rim landed on top).
        // Above that it may still land: skimming a tooth's tip, or a quick jump back, is a near miss, not a fall.
        if (Shell != null && (feetHeight < -FallThroughMetres || Shell.OverHole(up))) return NoGroundMetres;
        var best = 0f;
        // A top holds the walker while its footprint reaches within 0.6 body radii of the walker's centre: you stand on
        // a ledge until most of your disc is past its edge, then step or fall off (rationale for 0.6 not recorded).
        foreach (var i in Near(up, bodyRadius * 0.6f))
        {
            var o = Obstacles[i];
            if (o.Top <= feetHeight + allowance && o.Top > best) best = o.Top;
        }

        return best;
    }

    /// <summary>The world position (planet frame) of a point <paramref name="height"/> above the ground at <paramref name="up"/>.</summary>
    public Vector3 PointAt(Vector3 up, float height = 0) => up * (Shape.GroundRadius(up) + height);
}
