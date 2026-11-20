using System.Globalization;

namespace FileScape.Core.Planets;

/// <summary>
/// The words of the overview's discovery indicator (PLANET-107, AD-81): two short lines, the first about the neighbourhood
/// you are attending to (still being listed, or complete), the second about everything else (how much is left for later,
/// far from your attention, and how many worlds the overview holds). DJ (sixth pass): "The status said discovery was done
/// at 1,500 worlds, but its message was clipped behind the Pause button … Distinguish completion of a local neighborhood
/// from deferred discovery elsewhere." The old single line ("Discovery done: the overview's 1,500 worlds are as listed as
/// they will be") was both too long for its panel and wrong in kind: the budget had stopped discovery, not finished it.
/// Engine-free (<c>OverviewStatusTests</c>); the app shows the lines beside its Pause button and wraps nothing.
/// </summary>
public static class OverviewStatus
{
    /// <summary>A name in the indicator is cut to this many characters (with an ellipsis), so the first line fits its
    /// panel beside the button at UI scale 1.25 (≈ 60 characters at 14 px in 640 px).</summary>
    public const int MaxNameChars = 24;

    /// <summary>
    /// The indicator's two lines. <paramref name="name"/>: the neighbourhood's world (the selected one, else where you
    /// stand); <paramref name="spinner"/>: a one-character spinner while discovery runs.
    /// </summary>
    public static (string Near, string Elsewhere) Text(OverviewDiscoveryState state, NeighbourhoodStatus near, string name, int deferredElsewhere, int worlds, int maxTreeNodes, string spinner)
    {
        var who = Short(name);
        var first = state switch
        {
            OverviewDiscoveryState.Paused => $"Discovery paused · round {who}: {Progress(near)} · X resumes",
            OverviewDiscoveryState.LimitReached => string.Create(CultureInfo.InvariantCulture, $"Discovery stopped at {maxTreeNodes:N0} known entries"),
            _ when near.Complete => $"✓ Round {who}: complete, {near.Levels} levels{Unlistable(near)}",
            _ => $"{spinner} Discovering round {who}: {Progress(near)} · X pauses",
        };
        var second = deferredElsewhere > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{deferredElsewhere:N0} farther folders wait until you look there · {worlds:N0} worlds")
            : state == OverviewDiscoveryState.Discovering
                ? string.Create(CultureInfo.InvariantCulture, $"Listing what you can see · {worlds:N0} worlds")
                : string.Create(CultureInfo.InvariantCulture, $"Nothing left to list in view · {worlds:N0} worlds");
        return (first, second);
    }

    private static string Progress(NeighbourhoodStatus n) =>
        string.Create(CultureInfo.InvariantCulture, $"{n.Listed:N0} of {n.Listed + n.Waiting:N0} folders listed");

    private static string Unlistable(NeighbourhoodStatus n) =>
        n.Unlistable > 0 ? string.Create(CultureInfo.InvariantCulture, $" ({n.Unlistable:N0} can't be listed)") : "";

    /// <summary>A name cut to <see cref="MaxNameChars"/> characters (by text elements, so an emoji is never split).</summary>
    public static string Short(string name)
    {
        var info = new StringInfo(name);
        return info.LengthInTextElements <= MaxNameChars ? name : info.SubstringByTextElements(0, MaxNameChars - 1) + "…";
    }
}
