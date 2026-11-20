using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Places;

namespace FileScape.Core.Planets;

/// <summary>
/// How a departure reached the planetary world (PLANET-120, AD-84): every way of leaving a world calls
/// <see cref="DeparturePlanner.Plan"/> with one of these. The entry changes the plan only where it must (a pad press is
/// on its pad; the overview and a fall leave from the sky); the others say where the request came from, for the
/// inventory in AD-84 and for probes.
/// </summary>
public enum DepartureEntry
{
    /// <summary>Space on a launcher pad (the pad is the button).</summary>
    PadPress,

    /// <summary>G, the card's Go inside, a world double-clicked in the sky, the list's Enter on a world here, a bench/list activation.</summary>
    GoInside,

    /// <summary>U on a world.</summary>
    Parent,

    /// <summary>Backspace on a world (history back).</summary>
    HistoryBack,

    /// <summary>One hop of a multi-hop trip (PLANET-40).</summary>
    TripHop,

    /// <summary>A destination chosen at the telescope (stream 5's survey; it calls the same departure).</summary>
    Telescope,

    /// <summary>
    /// The navigator already moved elsewhere: the Destinations list's deeper rows and Straight there, favourites and
    /// repositories, a link answering, the breadcrumb, Locations, This PC, Go to path (Ctrl+L), Ctrl+Home, Open folder's
    /// new root (Ctrl+O), a walkthrough's "back to the lesson", a trip to a place not below you.
    /// </summary>
    NavigatorChange,

    /// <summary>From the overview: its own dive into the chosen world, a descent out of the light.</summary>
    Overview,

    /// <summary>A fall off a Hazard World's edge: the way back is a descent out of the light.</summary>
    FallWayBack,
}

/// <summary>How a departure leaves: on its pad already, a run to a pad first, with no pad (where you stand), or from the sky.</summary>
public enum DepartureKind
{
    OnPad,
    RunToPad,
    PadLess,
    FromSky,
}

/// <summary>Which launcher a departure leaves from, and why it is the right one (AD-84).</summary>
public enum DeparturePadRole
{
    /// <summary>No pad (pad-less, or from the sky).</summary>
    None,

    /// <summary>The destination's own launcher (a child folder or link, or the link that resolves to it).</summary>
    Own,

    /// <summary>The launcher of the link the navigator went through.</summary>
    Link,

    /// <summary>The way back, to the parent itself.</summary>
    WayBack,

    /// <summary>The launcher of the child folder on the way down to a deeper destination.</summary>
    Toward,

    /// <summary>The way back, for a place that is not below this world: up and over.</summary>
    WayOut,
}

/// <summary>A request to leave the world <see cref="Here"/> for <see cref="Target"/>.</summary>
/// <param name="Tree">The tree the world was composed from.</param>
/// <param name="Here">The composition of the world you stand on (its launchers, radius, hazard).</param>
/// <param name="StandingUp">Where you stand: a unit direction in the world's frame (the walker's up).</param>
/// <param name="Target">The destination folder or link.</param>
/// <param name="Entry">Where the request came from.</param>
/// <param name="ViaLink">The link the navigator went through (<see cref="PlaceChange.ViaLink"/>), or −1.</param>
public sealed record DepartureQuery(FsTree Tree, PlanetComposition Here, Vector3 StandingUp, int Target, DepartureEntry Entry, int ViaLink = -1);

/// <summary>The decision: how to leave, from which pad (null when none), and the run's length.</summary>
/// <param name="RunMetres">Along the ground from where you stand to the pad's centre, metres (0 without a pad).</param>
/// <param name="RunSeconds">The run's planned length, seconds, before any Hazard route is found (0 unless <see cref="DepartureKind.RunToPad"/>).</param>
public sealed record DeparturePlan(DepartureKind Kind, DeparturePadRole Role, PlanetLauncher? Pad, float RunMetres, float RunSeconds);

/// <summary>
/// Chooses the launcher every departure leaves from (PLANET-120..122, AD-84; it extends PLANET-82 / AD-74). DJ
/// (2026-11-20): "Some dialog-based navigation still launches the character from wherever they stand. Find all navigation
/// entry points and route departures through the intended launch-pad behaviour." The app's one departure routine
/// (PlanetWorld.Depart) asks this for every entry point in <see cref="DepartureEntry"/>, so the rule lives in one tested
/// place.
/// <para>
/// The rule, in order: the launcher of the link the navigator went through; the destination's own launcher (or the way back for the parent); else, for a
/// destination below this world, the launcher of the child on the way down to it (<see cref="DeparturePadRole.Toward"/>);
/// else, for a place not below this world, the way back (<see cref="DeparturePadRole.WayOut"/>: up and over); else no
/// pad (a belt moon, or below one; the top of the tree has no way back) and the departure is pad-less. A pad press is
/// always on its pad; the overview and a fall leave from the sky.
/// </para>
/// Preserve: the run's added time never exceeds <see cref="RunMaxSeconds"/> (<see cref="RunRouteMaxSeconds"/> on a
/// Hazard World), so direct navigation stays quick; standing within <see cref="OnPadShare"/> of a pad's radius adds no
/// time; the decision is deterministic and depends only on the query.
/// </summary>
public static class DeparturePlanner
{
    // ---- the run to the pad (PLANET-82; by eye, DJ to judge). A run takes 0.3 s plus a second per 30 m along the ground,
    // at most 0.8 s (1.1 s along a Hazard World's bridges), so the first ring of pads (≈ 9 m from the meadow) is
    // ≈ 0.6 s away and the far side of a world never more than 0.8 s.

    /// <summary>Every run takes at least this long, seconds (a hop onto a pad, not a teleport).</summary>
    public const float RunBaseSeconds = 0.3f;

    /// <summary>The run covers this many metres along the ground per extra second.</summary>
    public const float RunMetresPerSecond = 30f;

    /// <summary>The cap on a run's added time, seconds (DJ: "brisk enough that direct navigation stays useful").</summary>
    public const float RunMaxSeconds = 0.8f;

    /// <summary>The cap along a Hazard World's bridges, seconds: its route can be the long way round.</summary>
    public const float RunRouteMaxSeconds = 1.1f;

    /// <summary>Standing within this share of a pad's radius from its centre counts as on the pad (the wind-up draws you to the centre).</summary>
    public const float OnPadShare = 0.8f;

    /// <summary>Decides how the departure in <paramref name="q"/> leaves (see the class summary).</summary>
    public static DeparturePlan Plan(DepartureQuery q)
    {
        if (q.Entry is DepartureEntry.Overview or DepartureEntry.FallWayBack) return new DeparturePlan(DepartureKind.FromSky, DeparturePadRole.None, null, 0, 0);
        var (pad, role) = PadFor(q);
        if (pad == null) return new DeparturePlan(DepartureKind.PadLess, DeparturePadRole.None, null, 0, 0);
        var metres = PlanetShape.Angle(q.StandingUp, pad.Direction) * q.Here.Shape.Radius;
        if (q.Entry == DepartureEntry.PadPress || metres <= pad.PadRadius * OnPadShare) return new DeparturePlan(DepartureKind.OnPad, role, pad, metres, 0);
        return new DeparturePlan(DepartureKind.RunToPad, role, pad, metres, RunSeconds(metres, q.Here.Hazard != null));
    }

    /// <summary>A run's length for <paramref name="metres"/> along the ground, seconds, within the cap (the Hazard route's when <paramref name="alongRoute"/>).</summary>
    public static float RunSeconds(float metres, bool alongRoute) =>
        Math.Clamp(RunBaseSeconds + metres / RunMetresPerSecond, RunBaseSeconds, alongRoute ? RunRouteMaxSeconds : RunMaxSeconds);

    /// <summary>The launcher to leave from and its role (see the class summary), or (null, None).</summary>
    private static (PlanetLauncher? Pad, DeparturePadRole Role) PadFor(DepartureQuery q)
    {
        var launchers = q.Here.Launchers;
        if (q.ViaLink >= 0 && launchers.FirstOrDefault(l => l.CanLaunch && !l.IsParent && l.TargetId == q.ViaLink) is { } link) return (link, DeparturePadRole.Link);
        // A link the navigator went through elsewhere is where the way leads (it may stand below while its target is anywhere).
        var tree = q.Tree;
        var throughLink = q.ViaLink >= 0 && tree.Contains(q.ViaLink);
        var own = throughLink ? null
            : launchers.FirstOrDefault(l => l.CanLaunch && l.TargetId == q.Target) ?? launchers.FirstOrDefault(l => l.CanLaunch && !l.IsParent && l.LinkTargetId == q.Target && q.Target >= 0);
        if (own != null) return (own, own.IsParent ? DeparturePadRole.WayBack : DeparturePadRole.Own);

        // Not a neighbour: the way there starts down through a child of this world, or up through the way back.
        var here = q.Here.FolderId;
        var via = throughLink ? q.ViaLink : q.Target;
        if (!tree.Contains(via) || !tree.Contains(here) || via == here) return (null, DeparturePadRole.None);
        if (IsBelow(tree, here, via))
        {
            var child = PlaceNavigator.ChildToward(tree, here, via);
            var toward = launchers.FirstOrDefault(l => l.CanLaunch && !l.IsParent && l.TargetId == child);
            return toward != null ? (toward, DeparturePadRole.Toward) : (null, DeparturePadRole.None); // below a belt moon: no pad
        }

        return q.Here.ParentLauncher is { } up ? (up, DeparturePadRole.WayOut) : (null, DeparturePadRole.None);
    }

    /// <summary>True when <paramref name="id"/> is a proper descendant of <paramref name="ancestor"/>.</summary>
    private static bool IsBelow(FsTree tree, int ancestor, int id) => tree.Ancestors(id).Contains(ancestor);
}
