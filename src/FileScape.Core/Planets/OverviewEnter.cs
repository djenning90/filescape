namespace FileScape.Core.Planets;

/// <summary>
/// What Enter (and every other way of committing to a world) does while the overview is open (PLANET-83, PLANET-114,
/// AD-82). The fifth pass found two silent failures of DJ's route (Destinations → a world → Enter in the overview): a
/// row clicked in the list, then the overview's Enter, acted on the overview's own "you are here" and glided back down;
/// and a folder two levels down committed in the list started a trip whose hops never ran under the overview. Both were
/// decisions, now made here and unit-tested (<c>OverviewCameraTests</c>), so the in-app journey
/// (<c>planets-overview-enter</c>) no longer repeats every variant three times to catch them.
/// <list type="bullet">
/// <item><see cref="Target"/>: the card's selection when the overview draws it, else the overview's own.</item>
/// <item><see cref="Decide"/>: fly into a drawn world; travel out of the light to one the overview does not draw; go back
/// down for where you stand; say why when nothing is chosen; wait while another landing flies. On Esc's way back down
/// (PLANET-111) a commit takes over from it: you changed your mind.</item>
/// <item><see cref="CommitLandsThroughOverview"/>: with the overview open, any commit (the list's Enter, a trip, G, a
/// launch) lands through the overview, never as a surface launch or trip (those run on the surface, which the overview
/// does not tick).</item>
/// </list>
/// <c>PlanetWorld.Universe.cs</c> (BeginUniverseLanding, UniverseCardSelection) and the commit paths call these.
/// </summary>
public static class OverviewEnter
{
    /// <summary>The world Enter acts on: <paramref name="cardSelection"/> (the shared selection) when the overview draws it,
    /// else <paramref name="overviewSelected"/> (−1: none).</summary>
    public static int Target(int cardSelection, int overviewSelected, Func<int, bool> drawn) =>
        cardSelection >= 0 && drawn(cardSelection) ? cardSelection : overviewSelected;

    /// <summary>What a commit to <paramref name="id"/> does in the overview.</summary>
    /// <param name="id">The chosen world (−1: none).</param>
    /// <param name="here">Where you stand.</param>
    /// <param name="drawn">Whether the overview draws the world.</param>
    /// <param name="landing">A landing is already flying.</param>
    public static OverviewEnterAction Decide(int id, int here, Func<int, bool> drawn, bool landing) =>
        landing ? OverviewEnterAction.Wait
        : id < 0 ? OverviewEnterAction.SayChooseFirst
        : !drawn(id) ? OverviewEnterAction.TravelOutOfLight
        : id == here ? OverviewEnterAction.BackDown
        : OverviewEnterAction.FlyInto;

    /// <summary>With the overview open, a commit lands through it (never a surface launch or trip).</summary>
    public static bool CommitLandsThroughOverview(bool overviewOpen) => overviewOpen;
}

/// <summary>The outcome of <see cref="OverviewEnter.Decide"/>.</summary>
public enum OverviewEnterAction
{
    /// <summary>Fly into the world (≈ 0.75 s), then land on it out of the light.</summary>
    FlyInto,

    /// <summary>Where you stand: Esc's way back down.</summary>
    BackDown,

    /// <summary>Not drawn (beyond the budget, hidden): the navigator goes there, out of the light; a refusal says why.</summary>
    TravelOutOfLight,

    /// <summary>Nothing chosen: a toast says to click a world first.</summary>
    SayChooseFirst,

    /// <summary>Another landing is flying: this one waits (is ignored).</summary>
    Wait,
}
