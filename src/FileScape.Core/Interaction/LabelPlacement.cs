using System.Numerics;

namespace FileScape.Core.Interaction;

/// <summary>A screen rectangle for label placement, viewport units (x right, y down).</summary>
public readonly record struct LabelRect(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;

    /// <summary>The two rectangles share area (touching edges do not count).</summary>
    public bool Intersects(LabelRect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

    /// <summary><paramref name="o"/> lies wholly inside this one.</summary>
    public bool Encloses(LabelRect o) => o.X >= X && o.Y >= Y && o.Right <= Right && o.Bottom <= Bottom;

    /// <summary>Grown by <paramref name="by"/> on every side.</summary>
    public LabelRect Grow(float by) => new(X - by, Y - by, W + 2 * by, H + 2 * by);

    /// <summary>The point of the rectangle nearest <paramref name="p"/> (p itself when inside).</summary>
    public Vector2 Nearest(Vector2 p) => new(Math.Clamp(p.X, X, Right), Math.Clamp(p.Y, Y, Bottom));

    /// <summary>The circle at <paramref name="c"/> of radius <paramref name="r"/> reaches into the rectangle.</summary>
    public bool TouchesDisc(Vector2 c, float r) => Vector2.DistanceSquared(Nearest(c), c) < r * r;
}

/// <summary>
/// One label to place: its id, the body it names (its centre on screen and its drawn radius, pixels), its measured size,
/// its priority tier (lower first: the selection, the hovered world, "you are here", then the rest) and its rank within
/// the tier (lower first: nearer the camera).
/// </summary>
/// <param name="Preferred">The slot the label had last frame (<see cref="PlacedLabel.Slot"/>; −1 none): tried first, so a
/// name stays on the same side of its world while the camera moves instead of hopping between equal slots.</param>
public readonly record struct LabelRequest(int Id, Vector2 Anchor, float AnchorRadius, Vector2 Size, int Tier, float Rank, int Preferred = -1);

/// <summary>
/// A placed label: its rectangle and its leader, from <see cref="LeaderFrom"/> on the body's rim (where the dot is drawn)
/// to <see cref="LeaderTo"/> on the label's edge. <see cref="HasLeader"/> is false when the label sits against the body.
/// <see cref="Slot"/> is the ring × 8 + direction it took (pass it back as <see cref="LabelRequest.Preferred"/>).
/// </summary>
public readonly record struct PlacedLabel(int Id, LabelRect Rect, Vector2 LeaderFrom, Vector2 LeaderTo, int Tier, int Slot = 0)
{
    public bool HasLeader => Vector2.Distance(LeaderFrom, LeaderTo) > LabelPlacement.MinLeaderPixels;
}

/// <summary>
/// Places the overview's names round the worlds they name (PLANET-193, AD-98). DJ (seventh pass): "With about ten labels
/// among hundreds of bodies, leader lines cross behind other labels and their endpoints become ambiguous … clear priority
/// for selection, hover, and 'You are here'." Before, every name was pinned straight above its world and, when that slot
/// was taken, stacked higher on a longer vertical stem, so stems ran up through other worlds and behind other names.
/// <para>
/// Greedy by priority (<see cref="LabelRequest.Tier"/>, then <see cref="LabelRequest.Rank"/>): each label tries
/// positions round its body, nearest first — rings at <see cref="GapPixels"/> + k × <see cref="RingStepPixels"/> beyond
/// the body's drawn radius, and on each ring eight directions, above-right first (names read above their worlds) — and
/// takes the first that is inside the window, clear of the HUD, of every placed label, of every requested world's disc,
/// whose leader crosses no placed label or leader and passes over no other requested world, and across which no placed
/// leader runs. A label with no such place is left out (the world keeps its dot; the card and the list name it), and at
/// most <see cref="Options.MaxLabels"/> are placed, so the most important always win. The leader starts on the body's rim
/// (the app draws a dot there), so it is plain which world a name belongs to.
/// </para>
/// <para>Engine-free and deterministic for the same input (<c>OverviewLabelPlacementTests</c>); the app measures the text, projects
/// the worlds and draws (<c>SignLayer</c>). What a change must preserve: no two placed labels overlap, no leader crosses
/// another or runs under a label, a higher tier is never displaced by a lower one, and the selection is always tried first.</para>
/// </summary>
public static class LabelPlacement
{
    /// <summary>A label's nearest edge is this far beyond its world's drawn rim on the first ring, pixels.</summary>
    public const float GapPixels = 6f;

    /// <summary>Each further ring is this much farther out, pixels (a leader longer than three rings reads as detached).</summary>
    public const float RingStepPixels = 22f;

    /// <summary>Rings tried per label: the leader is at most <see cref="GapPixels"/> + (Rings − 1) × <see cref="RingStepPixels"/> long (≈ 72 px).</summary>
    public const int Rings = 4;

    /// <summary>Labels are kept at least this far apart, pixels (so two boxes never touch).</summary>
    public const float SpacingPixels = 3f;

    /// <summary>A leader shorter than this is not drawn (the label touches its world), pixels.</summary>
    public const float MinLeaderPixels = 2.5f;

    /// <summary>The bounds of one placement.</summary>
    public sealed record Options
    {
        /// <summary>At most this many labels at once: about ten names read; more bury the view (PLANET-193).</summary>
        public int MaxLabels { get; init; } = 12;
    }

    /// <summary>The directions tried on each ring, in order of preference (screen: y down). Above-right first, as names were.</summary>
    private static readonly Vector2[] Directions =
    [
        Vector2.Normalize(new(1, -1)), Vector2.Normalize(new(-1, -1)), new(0, -1), new(1, 0), new(-1, 0),
        Vector2.Normalize(new(1, 1)), Vector2.Normalize(new(-1, 1)), new(0, 1),
    ];

    /// <summary>
    /// Places <paramref name="requests"/> inside <paramref name="viewport"/>, clear of <paramref name="blocked"/> (HUD
    /// panels). Returns the placed labels in placement order (most important first).
    /// </summary>
    public static IReadOnlyList<PlacedLabel> Place(IReadOnlyList<LabelRequest> requests, LabelRect viewport, IReadOnlyList<LabelRect> blocked, Options? options = null)
    {
        var o = options ?? new Options();
        var ordered = requests.OrderBy(r => r.Tier).ThenBy(r => r.Rank).ThenBy(r => r.Id).ToList();
        var placed = new List<PlacedLabel>();
        foreach (var r in ordered)
        {
            if (placed.Count >= o.MaxLabels) break;
            if (Try(r, ordered, placed, viewport, blocked) is { } p) placed.Add(p);
        }

        return placed;
    }

    /// <summary>The first free position for <paramref name="r"/>, or null.</summary>
    private static PlacedLabel? Try(LabelRequest r, List<LabelRequest> all, List<PlacedLabel> placed, LabelRect viewport, IReadOnlyList<LabelRect> blocked)
    {
        if (r.Preferred is >= 0 and < Rings * 8 && At(r.Preferred) is { } kept) return kept; // last frame's slot, if still free
        for (var slot = 0; slot < Rings * Directions.Length; slot++)
        {
            if (slot != r.Preferred && At(slot) is { } p) return p;
        }

        return null;

        PlacedLabel? At(int slot)
        {
            var u = Directions[slot % Directions.Length];
            var reach = r.AnchorRadius + GapPixels + slot / Directions.Length * RingStepPixels;
            var rect = Around(r.Anchor + u * reach, u, r.Size);
            var from = r.Anchor + u * r.AnchorRadius;
            var to = rect.Nearest(r.Anchor);
            return Free(r, rect, from, to, all, placed, viewport, blocked) ? new PlacedLabel(r.Id, rect, from, to, r.Tier, slot) : null;
        }
    }

    /// <summary>A rectangle of <paramref name="size"/> whose edge nearest the body touches <paramref name="at"/>, lying away from it along <paramref name="u"/>.</summary>
    private static LabelRect Around(Vector2 at, Vector2 u, Vector2 size)
    {
        const float Straight = 0.2f; // |component| under this counts as straight along the other axis
        var x = u.X > Straight ? at.X : u.X < -Straight ? at.X - size.X : at.X - size.X / 2;
        var y = u.Y > Straight ? at.Y : u.Y < -Straight ? at.Y - size.Y : at.Y - size.Y / 2;
        return new LabelRect(x, y, size.X, size.Y);
    }

    private static bool Free(LabelRequest r, LabelRect rect, Vector2 from, Vector2 to, List<LabelRequest> all, List<PlacedLabel> placed, LabelRect viewport, IReadOnlyList<LabelRect> blocked)
    {
        if (!viewport.Encloses(rect)) return false;
        foreach (var b in blocked)
        {
            if (b.Intersects(rect)) return false;
        }

        var spaced = rect.Grow(SpacingPixels);
        var leader = Vector2.Distance(from, to) > MinLeaderPixels;
        foreach (var p in placed)
        {
            if (p.Rect.Intersects(spaced)) return false;
            if (leader && SegmentHitsRect(from, to, p.Rect)) return false; // a leader never runs under another name
            if (p.HasLeader && SegmentHitsRect(p.LeaderFrom, p.LeaderTo, rect)) return false; // nor does a placed one under this
            if (leader && p.HasLeader && SegmentsCross(from, to, p.LeaderFrom, p.LeaderTo)) return false;
        }

        // Clear of every world that asked for a name (its disc), and the leader passes over none but its own.
        foreach (var q in all)
        {
            if (rect.TouchesDisc(q.Anchor, q.AnchorRadius + SpacingPixels)) return false;
            if (leader && q.Id != r.Id && SegmentNearPoint(from, to, q.Anchor, q.AnchorRadius)) return false;
        }

        return true;
    }

    /// <summary>
    /// The layout's faults (probes and tests): pairs of labels that overlap, leaders that cross, leaders that run under a
    /// label other than their own. All zero for a layout from <see cref="Place"/>.
    /// </summary>
    public static (int Overlaps, int Crossings, int UnderLabels) Faults(IReadOnlyList<PlacedLabel> labels)
    {
        int overlaps = 0, crossings = 0, under = 0;
        for (var i = 0; i < labels.Count; i++)
        {
            for (var j = 0; j < labels.Count; j++)
            {
                if (i == j) continue;
                var (a, b) = (labels[i], labels[j]);
                if (j > i && a.Rect.Intersects(b.Rect)) overlaps++;
                if (j > i && a.HasLeader && b.HasLeader && SegmentsCross(a.LeaderFrom, a.LeaderTo, b.LeaderFrom, b.LeaderTo)) crossings++;
                if (a.HasLeader && SegmentHitsRect(a.LeaderFrom, a.LeaderTo, b.Rect)) under++;
            }
        }

        return (overlaps, crossings, under);
    }

    /// <summary>The segment p→q passes within <paramref name="radius"/> of <paramref name="c"/>.</summary>
    private static bool SegmentNearPoint(Vector2 p, Vector2 q, Vector2 c, float radius)
    {
        var d = q - p;
        var t = Math.Clamp(Vector2.Dot(c - p, d) / MathF.Max(1e-6f, d.LengthSquared()), 0f, 1f);
        return Vector2.DistanceSquared(p + d * t, c) < radius * radius;
    }

    /// <summary>The segment p→q enters the rectangle's interior (Liang–Barsky).</summary>
    public static bool SegmentHitsRect(Vector2 p, Vector2 q, LabelRect r)
    {
        var d = q - p;
        float t0 = 0, t1 = 1;
        return Clip(-d.X, p.X - r.X, ref t0, ref t1) && Clip(d.X, r.Right - p.X, ref t0, ref t1)
            && Clip(-d.Y, p.Y - r.Y, ref t0, ref t1) && Clip(d.Y, r.Bottom - p.Y, ref t0, ref t1) && t1 - t0 > 1e-4f
            && Inside(p + d * ((t0 + t1) / 2), r);

        static bool Inside(Vector2 m, LabelRect r) => m.X > r.X && m.X < r.Right && m.Y > r.Y && m.Y < r.Bottom;

        static bool Clip(float den, float num, ref float t0, ref float t1)
        {
            if (MathF.Abs(den) < 1e-9f) return num >= 0;
            var t = num / den;
            if (den < 0)
            {
                if (t > t1) return false;
                if (t > t0) t0 = t;
            }
            else
            {
                if (t < t0) return false;
                if (t < t1) t1 = t;
            }

            return true;
        }
    }

    /// <summary>The segments a→b and c→d cross at a point inside both (shared ends and parallel touches do not count).</summary>
    public static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;
        var r = b - a;
        var s = d - c;
        var den = Cross(r, s);
        if (MathF.Abs(den) < 1e-9f) return false;
        var t = Cross(c - a, s) / den;
        var u = Cross(c - a, r) / den;
        const float Ends = 1e-3f;
        return t > Ends && t < 1 - Ends && u > Ends && u < 1 - Ends;
    }
}
