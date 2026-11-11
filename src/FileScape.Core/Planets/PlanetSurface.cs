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
    private const int HillCount = 7;
    private (Vector3 Centre, float WidthRadians, float Amplitude)[]? _hills;

    public static PlanetShape Sphere(float radius) => new(radius, 0, 0, []);

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
                var width = 0.35f + (float)rng.NextDouble() * 0.45f;
                var amp = HillAmplitude * (0.45f + 0.55f * (float)rng.NextDouble()) * (i % 3 == 2 ? -0.5f : 1f); // some gentle hollows
                hills[i] = (d, width, amp);
            }

            return _hills = hills;
        }
    }

    /// <summary>Terrain height above <see cref="Radius"/> at a unit direction, metres (smooth, |h| ≤ <see cref="HillAmplitude"/>).</summary>
    public float HeightAt(Vector3 up)
    {
        if (HillAmplitude <= 0) return 0;
        var h = 0f;
        foreach (var (c, w, a) in Hills)
        {
            var ang = Angle(up, c) / w;
            h += a * MathF.Exp(-ang * ang);
        }

        var mask = 1f;
        foreach (var (d, r) in FlatSpots)
        {
            var s = Angle(up, d) * Radius;
            mask = MathF.Min(mask, SmoothStep(r, r + 3.5f, s));
        }

        return Math.Clamp(h, -HillAmplitude, HillAmplitude) * mask;
    }

    /// <summary>Distance from the centre to the ground at a unit direction, metres.</summary>
    public float GroundRadius(Vector3 up) => Radius + HeightAt(up);

    /// <summary>Outward surface normal at a unit direction (finite differences on the height field).</summary>
    public Vector3 Normal(Vector3 up)
    {
        if (HillAmplitude <= 0) return up;
        var (a, b) = Tangents(up);
        const float e = 0.01f;
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
    public PlanetSurface(PlanetShape shape, IReadOnlyList<SurfaceObstacle> obstacles)
    {
        Shape = shape;
        Obstacles = obstacles;
    }

    public PlanetShape Shape { get; }
    public IReadOnlyList<SurfaceObstacle> Obstacles { get; }
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
    public float SupportAt(Vector3 up, float bodyRadius, float feetHeight, float allowance)
    {
        var best = 0f;
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
