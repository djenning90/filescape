using System.Numerics;

namespace FileScape.Core.Layout;

/// <summary>An axis-aligned box in layout space. Y is up. The hierarchy recedes towards -Z.</summary>
public readonly record struct Box(Vector3 Min, Vector3 Max)
{
    public static readonly Box Empty = new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

    public bool IsEmpty => Min.X > Max.X;
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;

    public static Box FromBase(Vector3 baseCenter, float width, float height, float depth) =>
        new(new Vector3(baseCenter.X - width / 2, baseCenter.Y, baseCenter.Z - depth / 2),
            new Vector3(baseCenter.X + width / 2, baseCenter.Y + height, baseCenter.Z + depth / 2));

    public Box Union(Box other) => IsEmpty ? other : other.IsEmpty ? this
        : new Box(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

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
