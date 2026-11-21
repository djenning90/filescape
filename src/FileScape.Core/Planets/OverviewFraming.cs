using System.Numerics;

namespace FileScape.Core.Planets;

/// <summary>
/// How far the overview camera may zoom out, from what is in view (PLANET-192, AD-98). DJ (seventh pass): "allow zooming
/// far enough out to understand the selected system and its neighbors … Derive useful framing limits from the visible
/// system and current focus." Before, the wheel stopped at a fixed 6,000 m whatever the universe; at the Ubuntu root,
/// whose ring of top-level folders and their systems is kilometres across, that cut off the neighbourhood.
/// <para>
/// It owns the sizes the overview frames by and the wheel's limits; <c>PlanetWorld.Universe.cs</c> draws, smooths and
/// clamps with them. The farthest zoom is <see cref="NeighbourhoodSystems"/> × the focus's neighbourhood (its own system,
/// or the system it orbits in, which holds its siblings: its neighbours), never less than <see cref="FloorMetres"/> (the
/// old fixed limit, so no universe is more constrained than before) and never more than <see cref="UniverseSystems"/> ×
/// the whole universe's extent (or <see cref="CeilingMetres"/>), so you can always see the selection among its
/// neighbours but not lose the map in empty space.
/// </para>
/// <para>What a change must preserve: the wheel never jumps the camera (a limit that shrinks below the current distance
/// stops zooming out there; it does not pull the camera in: <see cref="WheelDistance"/>); the plain notch is
/// <see cref="OverviewControls.WheelDistanceFactor"/>'s; the far clip always contains the universe from the farthest zoom.</para>
/// </summary>
public static class OverviewFraming
{
    /// <summary>The closest the wheel goes, metres (unchanged from the 2026-11-11 tuning).</summary>
    public const float MinMetres = 4f;

    /// <summary>The farthest zoom is never less than this, metres: the fixed limit before PLANET-192.</summary>
    public const float FloorMetres = 6000f;

    /// <summary>
    /// The farthest zoom: this many neighbourhood sizes from the focus. With the 62° lens a system of size S (its ring plus a
    /// margin) fills about 2·S/(2·d·tan 31°) of the view's height at distance d: at 3 × it fills ≈ 55 %, the whole
    /// neighbourhood in view with room round it.
    /// </summary>
    public const float NeighbourhoodSystems = 3f;

    /// <summary>The farthest zoom is never more than this many universe extents (the whole map then fills ≈ 55 % of the view).</summary>
    public const float UniverseSystems = 3f;

    /// <summary>An absolute ceiling, metres: the vast fixture's laid-out universe is hundreds of kilometres across (AD-81).</summary>
    public const float CeilingMetres = 2_000_000f;

    /// <summary>The far clip is at least this, metres (the overview's before PLANET-192).</summary>
    public const float FarFloorMetres = 30000f;

    /// <summary>
    /// A system's size for framing, metres: its ring plus a margin (1.3 × the ring radius + the body) or, for a world with
    /// no children, a few body radii (4 ×), so a lone world is not framed as a speck. (Moved from the app, unchanged.)
    /// </summary>
    public static float SystemSize(UniverseBody b) => MathF.Max(b.Radius * 4f, b.OrbitRadius * 1.3f + b.Radius);

    /// <summary>
    /// The size of <paramref name="focus"/>'s neighbourhood, metres from the focus: its own system, or the system of
    /// <paramref name="parent"/> it orbits in (its siblings are its neighbours) measured from the focus, whichever is larger.
    /// </summary>
    public static float NeighbourhoodSize(UniverseBody focus, UniverseBody? parent)
    {
        var own = SystemSize(focus);
        if (parent is not { } p) return own;
        return MathF.Max(own, Vector3.Distance(focus.Position, p.Position) + SystemSize(p));
    }

    /// <summary>
    /// The universe's extent seen from <paramref name="centre"/>, metres: the farthest body's distance plus its radius
    /// (0 with no bodies).
    /// </summary>
    public static float Extent(IEnumerable<UniverseBody> bodies, Vector3 centre)
    {
        var far = 0f;
        foreach (var b in bodies) far = MathF.Max(far, Vector3.Distance(b.Position, centre) + b.Radius);
        return far;
    }

    /// <summary>
    /// The farthest the wheel may take the camera from the focus, metres: <see cref="NeighbourhoodSystems"/> ×
    /// <paramref name="neighbourhood"/>, at least <see cref="FloorMetres"/>, at most <see cref="UniverseSystems"/> ×
    /// (<paramref name="universeExtent"/> + <paramref name="focusFromCentre"/>, so a focus at the edge can still see the
    /// whole map) and <see cref="CeilingMetres"/>.
    /// </summary>
    public static float MaxDistance(float neighbourhood, float universeExtent, float focusFromCentre = 0f)
    {
        var ceiling = MathF.Min(CeilingMetres, MathF.Max(FloorMetres, UniverseSystems * (universeExtent + focusFromCentre)));
        return Math.Clamp(NeighbourhoodSystems * neighbourhood, FloorMetres, ceiling);
    }

    /// <summary>
    /// One wheel notch: <paramref name="current"/> (the distance target) × <paramref name="factor"/>, within
    /// [<see cref="MinMetres"/>, <paramref name="max"/>]; a limit already passed (the focus changed to a smaller
    /// neighbourhood) stops a zoom out where it is and never pulls the camera in.
    /// </summary>
    public static float WheelDistance(float current, float factor, float max)
    {
        var next = current * factor;
        if (factor > 1f) return current >= max ? current : MathF.Min(next, max); // out: stop at the limit, or where it already is past it
        return MathF.Max(next, MathF.Min(current, MinMetres)); // in: stop at the closest
    }

    /// <summary>The far clip for the camera at <paramref name="distance"/> from a universe of <paramref name="extent"/>, metres: all of it, with room.</summary>
    public static float FarClip(float distance, float extent) => MathF.Max(FarFloorMetres, 1.1f * (distance + 2f * extent));
}
