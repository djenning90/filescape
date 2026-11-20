namespace FileScape.Core.Planets;

/// <summary>How sharp a gallery picture is on its canvas: not yet, a small thumbnail, or a sharp one (PLANET-133).</summary>
public enum ThumbTier : byte
{
    None,
    Small,
    Large,
}

/// <summary>
/// One picture on a gallery world as the planner sees it: its distance from the camera (metres), whether it is in the
/// camera's view, the tier it shows, the tier being loaded (None when nothing is in flight) and the highest tier that
/// failed (None when nothing has).
/// </summary>
public readonly record struct ThumbSlot(int Id, float DistanceMetres, bool InView, ThumbTier Shown, ThumbTier Loading, ThumbTier Failed);

/// <summary>
/// The bounds on a gallery world's pictures (PLANET-131, AD-86). Defaults: 2 requests in flight (the shell's two worker
/// threads); at most 64 pictures with a texture on one world (the planet's own item budget, so every shown easel can fill;
/// ≈ 22 MB of 256 px textures with mipmaps); at most 8 sharp (768 px, ≈ 3 MB each with mipmaps: ≈ 25 MB); a picture is
/// sharpened within 8 m while in view and goes back to small beyond 12 m (the gap keeps it from flickering between the
/// two as you walk to and fro); a far texture is given back only for a picture at least 6 m nearer.
/// </summary>
public sealed record ThumbBudget
{
    public int MaxInFlight { get; init; } = 2;

    public int MaxShown { get; init; } = 64;

    public int MaxLarge { get; init; } = 8;

    /// <summary>A small thumbnail's longer side, pixels: Windows caches 256 px thumbnails, so these are usually a cache read.</summary>
    public int SmallPixels { get; init; } = 256;

    /// <summary>A sharp thumbnail's longer side, pixels: a 1.24 m canvas fills ≈ 700 px of a 1600 × 900 window from 1.5 m.</summary>
    public int LargePixels { get; init; } = 768;

    public float LargeWithinMetres { get; init; } = 8f;

    public float LargeReleaseMetres { get; init; } = 12f;

    /// <summary>A shown far picture gives its texture to a waiting one only when that one is at least this much nearer, metres.</summary>
    public float EvictMarginMetres { get; init; } = 6f;
}

/// <summary>
/// The gallery's loading order (PLANET-131, PLANET-133, AD-86), pure and deterministic so it is tested without the
/// engine; <c>PlanetVisual</c> asks it a few times a second and does what it says. <see cref="Next"/>: what to start
/// now, best first — a small thumbnail for each picture in view (nearest first), then a sharp one for the nearest in view
/// within reach, then small ones behind you — never more than the budget's in-flight, shown or sharp counts, never a
/// tier that already failed. <see cref="Release"/>: sharp pictures to drop back to small (left behind, or over the count).
/// <see cref="Evict"/>: far textures to give up so nearer waiting pictures can show when the shown budget is full.
/// Preserve: every list it returns is bounded by the budget; the same slots give the same answer; distances in metres.
/// </summary>
public static class ThumbnailPlan
{
    private static bool Busy(ThumbSlot s) => s.Loading != ThumbTier.None;

    private static bool Holds(ThumbSlot s) => s.Shown != ThumbTier.None || s.Loading != ThumbTier.None;

    private static bool WantsSmall(ThumbSlot s) => s.Shown == ThumbTier.None && !Busy(s) && s.Failed == ThumbTier.None;

    private static bool WantsLarge(ThumbSlot s, ThumbBudget b) =>
        s.Shown == ThumbTier.Small && !Busy(s) && s.Failed < ThumbTier.Large && s.InView && s.DistanceMetres <= b.LargeWithinMetres;

    public static List<(int Id, ThumbTier Tier)> Next(IReadOnlyList<ThumbSlot> slots, ThumbBudget b)
    {
        var result = new List<(int, ThumbTier)>();
        var free = b.MaxInFlight - slots.Count(Busy);
        if (free <= 0) return result;
        var holding = slots.Count(Holds);
        var large = slots.Count(s => s.Shown == ThumbTier.Large || s.Loading == ThumbTier.Large);
        // Classes: 0 = small in view, 1 = sharp near, 2 = small out of view; nearest first within each.
        var wanted = new List<(int Class, float Distance, int Id, ThumbTier Tier)>();
        foreach (var s in slots)
        {
            if (WantsSmall(s)) wanted.Add((s.InView ? 0 : 2, s.DistanceMetres, s.Id, ThumbTier.Small));
            else if (WantsLarge(s, b)) wanted.Add((1, s.DistanceMetres, s.Id, ThumbTier.Large));
        }

        foreach (var w in wanted.OrderBy(w => w.Class).ThenBy(w => w.Distance).ThenBy(w => w.Id))
        {
            if (free == 0) break;
            if (w.Tier == ThumbTier.Small)
            {
                if (holding >= b.MaxShown) continue;
                holding++;
            }
            else
            {
                if (large >= b.MaxLarge) continue;
                large++;
            }

            result.Add((w.Id, w.Tier));
            free--;
        }

        return result;
    }

    public static List<int> Release(IReadOnlyList<ThumbSlot> slots, ThumbBudget b)
    {
        var sharp = slots.Where(s => s.Shown == ThumbTier.Large && !Busy(s)).OrderBy(s => s.DistanceMetres).ThenBy(s => s.Id).ToList();
        var result = sharp.Where(s => s.DistanceMetres > b.LargeReleaseMetres).Select(s => s.Id).ToList();
        var kept = sharp.Where(s => s.DistanceMetres <= b.LargeReleaseMetres).ToList();
        var loadingLarge = slots.Count(s => s.Loading == ThumbTier.Large);
        for (var i = Math.Max(0, b.MaxLarge - loadingLarge); i < kept.Count; i++) result.Add(kept[i].Id);
        return result;
    }

    public static List<int> Evict(IReadOnlyList<ThumbSlot> slots, ThumbBudget b)
    {
        var result = new List<int>();
        if (slots.Count(Holds) < b.MaxShown) return result;
        var waiting = slots.Where(s => WantsSmall(s) && s.InView).OrderBy(s => s.DistanceMetres).ThenBy(s => s.Id).Take(b.MaxInFlight).ToList();
        var far = slots.Where(s => s.Shown != ThumbTier.None && !Busy(s)).OrderByDescending(s => s.DistanceMetres).ThenBy(s => s.Id).ToList();
        for (var i = 0; i < waiting.Count && i < far.Count; i++)
        {
            if (far[i].DistanceMetres - waiting[i].DistanceMetres < b.EvictMarginMetres) break;
            result.Add(far[i].Id);
        }

        return result;
    }
}

/// <summary>
/// A least-recently-used cache bounded by bytes (PLANET-131): the session's decoded thumbnails, so a world you come back
/// to fills at once and the viewer's steps back are instant. An entry larger than the whole budget is not kept.
/// Preserve: <see cref="Bytes"/> never exceeds the budget. Not thread-safe: use from one thread (the app's main thread).
/// </summary>
public sealed class ByteBudgetCache<TKey, TValue>(long maxBytes) where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value, long Bytes)>> _map = [];
    private readonly LinkedList<(TKey Key, TValue Value, long Bytes)> _order = new(); // most recent first

    public long MaxBytes => maxBytes;

    public long Bytes { get; private set; }

    public int Count => _map.Count;

    public void Put(TKey key, TValue value, long bytes)
    {
        Remove(key);
        if (bytes > maxBytes) return;
        _map[key] = _order.AddFirst((key, value, bytes));
        Bytes += bytes;
        while (Bytes > maxBytes && _order.Last is { } last) Remove(last.Value.Key);
    }

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Remove(TKey key)
    {
        if (!_map.Remove(key, out var node)) return;
        _order.Remove(node);
        Bytes -= node.Value.Bytes;
    }
}
