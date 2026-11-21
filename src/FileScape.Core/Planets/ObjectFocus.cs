namespace FileScape.Core.Planets;

/// <summary>One object on the surface as the focus sees it: its index, its distance to its edge (metres) and how far it
/// is ahead (the cosine between your facing and the way to it: 1 straight ahead, −1 behind).</summary>
public readonly record struct FocusCandidate(int Index, float DistanceMetres, float Ahead);

/// <summary>
/// Which object on a planet's surface is in focus — what E acts on and what the prompt names (PLANET-43; the rule moved
/// here from PlanetWorld.UpdateFocus in the seventh pass's integration so it can be tested): the nearest in reach,
/// favouring what is ahead, never something behind you more than 1 m away; and, new (PLANET-198), the object you just
/// looked at closer (a hop from the list, E, a twirl) stays the focus while it is in reach and you have not walked on, so
/// the prompt and the preview's "Close (E / Esc)" speak of the same file (planets-text: after a hop to a file the prompt
/// offered E on a neighbour standing nearer). The app releases the hold when you walk or the preview moves on.
/// Preserve: without a held object the choice is exactly the old loop's (first best score wins, ties keep the earlier).
/// </summary>
public static class ObjectFocus
{
    /// <summary>Behind you means a cosine below this (over ≈ 102° from your facing).</summary>
    private const float BehindCosine = -0.2f;

    /// <summary>Something behind you is still in focus within this distance, metres.</summary>
    private const float BehindReachMetres = 1.0f;

    /// <summary>
    /// The index of the object in focus, or −1: <paramref name="held"/> when it is among the candidates and in reach,
    /// else the lowest score (distance − ahead × <paramref name="aheadBonusMetres"/>) below <paramref name="reachMetres"/>.
    /// </summary>
    public static int Best(IEnumerable<FocusCandidate> candidates, float reachMetres, float aheadBonusMetres, int held = -1)
    {
        var best = reachMetres;
        var pick = -1;
        foreach (var c in candidates)
        {
            if (c.DistanceMetres > reachMetres) continue;
            if (c.Ahead < BehindCosine && c.DistanceMetres > BehindReachMetres) continue;
            if (held >= 0 && c.Index == held) return held;
            var score = c.DistanceMetres - c.Ahead * aheadBonusMetres;
            if (score < best)
            {
                best = score;
                pick = c.Index;
            }
        }

        return pick;
    }
}
