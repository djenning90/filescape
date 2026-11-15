using System.Numerics;

namespace FileScape.Core.Layout;

/// <summary>
/// An axis-aligned box: the solid of a pedestal, file block, tower or gate, and the unit of bounds, picking and camera
/// framing in both the maps (<see cref="TreeLayout"/>, layout units, the hierarchy receding towards −Z) and Places
/// (metres). Y is up. Immutable; every operation returns a new box.
/// </summary>
public readonly record struct Box(Vector3 Min, Vector3 Max)
{
    /// <summary>
    /// The inverted box (Min = +max, Max = −max): it contains nothing, and <see cref="Union"/> with it returns the other
    /// box, so bounds can be accumulated starting from it.
    /// </summary>
    public static readonly Box Empty = new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

    public bool IsEmpty => Min.X > Max.X;
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;

    public static Box FromBase(Vector3 baseCenter, float width, float height, float depth) =>
        new(new Vector3(baseCenter.X - width / 2, baseCenter.Y, baseCenter.Z - depth / 2),
            new Vector3(baseCenter.X + width / 2, baseCenter.Y + height, baseCenter.Z + depth / 2));

    public Box Union(Box other) => IsEmpty ? other : other.IsEmpty ? this
        : new Box(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    /// <summary>
    /// True when the two interiors overlap by more than <paramref name="epsilon"/> on every axis: boxes that merely touch
    /// (neighbouring blocks sharing a face, a block standing on its pedestal) do not count, even after float rounding.
    /// </summary>
    public bool Overlaps(Box o, float epsilon = 1e-4f) =>
        Min.X < o.Max.X - epsilon && Max.X > o.Min.X + epsilon &&
        Min.Y < o.Max.Y - epsilon && Max.Y > o.Min.Y + epsilon &&
        Min.Z < o.Max.Z - epsilon && Max.Z > o.Min.Z + epsilon;

    /// <summary>A slab test. Returns the entry distance along a unit-length direction, or null if the ray misses.</summary>
    public float? Intersect(Vector3 origin, Vector3 direction)
    {
        var tMin = 0f;
        var tMax = float.MaxValue;
        for (var axis = 0; axis < 3; axis++)
        {
            var o = origin[axis];
            var d = direction[axis];
            var lo = Min[axis];
            var hi = Max[axis];
            if (MathF.Abs(d) < 1e-9f)
            {
                // Parallel to this pair of planes (1/d would overflow): the ray hits only if it starts between them.
                if (o < lo || o > hi)
                {
                    return null;
                }

                continue;
            }

            var inv = 1f / d;
            var t1 = (lo - o) * inv;
            var t2 = (hi - o) * inv;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax)
            {
                return null;
            }
        }

        return tMin;
    }
}
