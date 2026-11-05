using System.Numerics;

namespace FileScape.Core.Places;

/// <summary>What a terrain piece is for: solids block walking above step height; supports can be stood on.</summary>
public enum TerrainKind
{
    /// <summary>A file tower (solid; low files can be stepped onto).</summary>
    Tower,
    Landmark,

    /// <summary>A gate pillar (the opening between pillars is walkable).</summary>
    Pillar,

    /// <summary>A raised platform: the arrival terrace, the overlook.</summary>
    Platform,

    /// <summary>One step of a stair.</summary>
    Step,

    /// <summary>A jump block of the optional shortcut (JUMP-02).</summary>
    JumpBlock,

    /// <summary>A railing or wall that keeps walkers on a platform.</summary>
    Rail,
}

/// <summary>
/// An oriented box standing on the ground: <see cref="Center"/> is (x, z), <see cref="HalfExtents"/> are along its own
/// axes, <see cref="AngleDeg"/> rotates it clockwise from north like gates do. It supports anyone above it up to
/// <see cref="Top"/>, and blocks horizontal movement for anyone whose feet are lower than <see cref="Top"/> minus the
/// step height and whose head is above <see cref="Bottom"/>.
/// </summary>
public readonly record struct TerrainBox(Vector2 Center, Vector2 HalfExtents, float AngleDeg, float Bottom, float Top, TerrainKind Kind, int NodeId = -1)
{
    private (Vector2 U, Vector2 V) Axes()
    {
        var a = AngleDeg * MathF.PI / 180f;
        // Local u = the box's "width" axis (tangent), v = its "depth" axis (outward), matching PlaceGeometry.Direction.
        var v = new Vector2(MathF.Sin(a), -MathF.Cos(a));
        var u = new Vector2(-v.Y, v.X);
        return (u, v);
    }

    /// <summary>The closest point of the footprint to <paramref name="p"/> and the distance to it (0 inside).</summary>
    public (Vector2 Closest, float Distance) Closest(Vector2 p)
    {
        var (u, v) = Axes();
        var d = p - Center;
        var lu = Math.Clamp(Vector2.Dot(d, u), -HalfExtents.X, HalfExtents.X);
        var lv = Math.Clamp(Vector2.Dot(d, v), -HalfExtents.Y, HalfExtents.Y);
        var c = Center + u * lu + v * lv;
        return (c, Vector2.Distance(c, p));
    }

    /// <summary>
    /// The shortest push that moves a circle at <paramref name="p"/> with <paramref name="radius"/> out of the footprint,
    /// or zero if they do not overlap.
    /// </summary>
    public Vector2 PushOut(Vector2 p, float radius)
    {
        var (u, v) = Axes();
        var d = p - Center;
        var lu = Vector2.Dot(d, u);
        var lv = Vector2.Dot(d, v);
        if (MathF.Abs(lu) <= HalfExtents.X && MathF.Abs(lv) <= HalfExtents.Y)
        {
            // Centre inside: leave through the nearest face.
            var pu = HalfExtents.X - MathF.Abs(lu);
            var pv = HalfExtents.Y - MathF.Abs(lv);
            return pu < pv
                ? u * ((pu + radius) * MathF.Sign(lu == 0 ? 1 : lu))
                : v * ((pv + radius) * MathF.Sign(lv == 0 ? 1 : lv));
        }

        var (c, dist) = Closest(p);
        if (dist >= radius)
        {
            return Vector2.Zero;
        }

        var n = dist > 1e-6f ? (p - c) / dist : Vector2.UnitX;
        return n * (radius - dist);
    }

    public bool Overlaps(Vector2 p, float radius) => Closest(p).Distance < radius;
}

/// <summary>A straight walkable ramp from <see cref="Start"/> (height <see cref="StartHeight"/>) to <see cref="End"/>.</summary>
public readonly record struct TerrainRamp(Vector2 Start, Vector2 End, float HalfWidth, float StartHeight, float EndHeight)
{
    /// <summary>The height at the closest point of the ramp's strip within <paramref name="radius"/>, or null if outside it.</summary>
    public float? HeightNear(Vector2 p, float radius)
    {
        var axis = End - Start;
        var len = axis.Length();
        var dir = axis / len;
        var side = new Vector2(-dir.Y, dir.X);
        var d = p - Start;
        var along = Vector2.Dot(d, dir);
        var across = Vector2.Dot(d, side);
        if (along < -radius || along > len + radius || MathF.Abs(across) > HalfWidth + radius)
        {
            return null;
        }

        // The highest point of the strip under the walker's disc (towards the high end).
        var high = EndHeight >= StartHeight ? Math.Clamp(along + radius, 0, len) : Math.Clamp(along - radius, 0, len);
        return StartHeight + (EndHeight - StartHeight) * high / len;
    }

    public float SlopeDeg => MathF.Atan2(MathF.Abs(EndHeight - StartHeight), Vector2.Distance(Start, End)) * 180f / MathF.PI;
}

/// <summary>A flat annulus (a raised tier terrace in a wide folder): a support only.</summary>
public readonly record struct TerrainRing(float Inner, float Outer, float Top, float StartDeg, float EndDeg);

/// <summary>
/// Units (docs/units.md): Places world space, metres, Y up; a footprint's (x, z) is a <see cref="Vector2"/> whose Y
/// component is world Z. Angles named "…Deg" are place angles, clockwise from north (−Z). Geometry stays in plain floats
/// here because every value is a length in the same space and the inner loops run per walker sub-step.
/// The walkable ground of one place (AD-16, WALK-05): the plaza floor at height 0 inside <see cref="BoundaryRadius"/>,
/// plus boxes (structures, platforms, steps), ramps and tier rings. Only structures that matter for walking are here;
/// signs, beacons, floor lines and lintels are deliberately absent, so they never snag anyone.
/// </summary>
public sealed class PlaceTerrain
{
    private const float CellSize = 4f;
    private readonly Dictionary<(int, int), List<int>> _grid = [];

    public PlaceTerrain(IReadOnlyList<TerrainBox> boxes, IReadOnlyList<TerrainRamp> ramps, IReadOnlyList<TerrainRing> rings, float boundaryRadius)
    {
        Boxes = boxes;
        Ramps = ramps;
        Rings = rings;
        BoundaryRadius = boundaryRadius;
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            var r = b.HalfExtents.Length();
            for (var x = Cell(b.Center.X - r); x <= Cell(b.Center.X + r); x++)
            for (var z = Cell(b.Center.Y - r); z <= Cell(b.Center.Y + r); z++)
            {
                if (!_grid.TryGetValue((x, z), out var list)) _grid[(x, z)] = list = [];
                list.Add(i);
            }
        }
    }

    public IReadOnlyList<TerrainBox> Boxes { get; }
    public IReadOnlyList<TerrainRamp> Ramps { get; }
    public IReadOnlyList<TerrainRing> Rings { get; }

    /// <summary>Walkers stay inside this radius (WALK-05: nobody falls off the world).</summary>
    public float BoundaryRadius { get; }

    public static readonly PlaceTerrain Flat = new([], [], [], 60f);

    private static int Cell(float v) => (int)MathF.Floor(v / CellSize);

    /// <summary>Indices of boxes whose grid cells touch the disc.</summary>
    public IEnumerable<int> BoxesNear(Vector2 p, float radius)
    {
        var seen = new HashSet<int>();
        for (var x = Cell(p.X - radius); x <= Cell(p.X + radius); x++)
        for (var z = Cell(p.Y - radius); z <= Cell(p.Y + radius); z++)
        {
            if (!_grid.TryGetValue((x, z), out var list)) continue;
            foreach (var i in list)
            {
                if (seen.Add(i)) yield return i;
            }
        }
    }

    /// <summary>
    /// The height of the highest surface under a disc at <paramref name="p"/> that someone with feet at
    /// <paramref name="feet"/> could stand on: surfaces whose underside is above <paramref name="feet"/> plus
    /// <paramref name="reach"/> (lintels, overhangs) are ignored. With <paramref name="maxRise"/>, surfaces whose top is
    /// more than that above the feet are ignored too: for a walker those are walls it touches, not floors. (Without it,
    /// a walker brushing a pillar was snapped onto its top, 7 m up — found at 30 m/s, 2026-11-05 evening.)
    /// </summary>
    public float SupportHeight(Vector2 p, float radius, float feet, float reach, float maxRise = float.PositiveInfinity)
    {
        var h = 0f;
        foreach (var i in BoxesNear(p, radius))
        {
            var b = Boxes[i];
            if (b.Bottom <= feet + reach && b.Top > h && b.Top <= feet + maxRise && b.Overlaps(p, radius)) h = b.Top;
        }

        foreach (var r in Ramps)
        {
            if (r.HeightNear(p, radius) is { } rh && rh > h) h = rh;
        }

        var d = p.Length();
        foreach (var ring in Rings)
        {
            if (ring.Top <= h || d < ring.Inner - radius || d > ring.Outer + radius) continue;
            var a = PlaceAngle(p);
            if (InArc(a, ring.StartDeg, ring.EndDeg)) h = ring.Top;
        }

        return h;
    }

    /// <summary>True if a walker's disc here is clear of every solid (used to validate a landing spot, WALK-07).</summary>
    public bool IsClear(Vector2 p, float radius, float feet, float height)
    {
        if (p.Length() > BoundaryRadius - radius) return false;
        foreach (var i in BoxesNear(p, radius))
        {
            var b = Boxes[i];
            if (b.Overlaps(p, radius) && b.Top > feet + 0.05f && b.Bottom < feet + height) return false;
        }

        return true;
    }

    /// <summary>The angle of a point clockwise from north (-Z), as used throughout the place geometry.</summary>
    public static float PlaceAngle(Vector2 p)
    {
        var a = MathF.Atan2(p.X, -p.Y) * 180f / MathF.PI;
        return a < 0 ? a + 360 : a;
    }

    private static bool InArc(float a, float start, float end)
    {
        // Ring arcs are given in gate angles (-180..180 around north).
        var x = a > 180 ? a - 360 : a;
        return x >= start && x <= end;
    }
}
