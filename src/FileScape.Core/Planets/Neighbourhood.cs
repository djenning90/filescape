using FileScape.Core.Model;

namespace FileScape.Core.Planets;

/// <summary>A world on show and what its shown look was made from: the full <see cref="PlanetComposition.Signature"/>
/// for the world you stand on, the <see cref="PlanetComposition.PreviewSignature"/> for a world seen in the sky.</summary>
public readonly record struct ShownWorld(int FolderId, string Signature, bool InSky)
{
    /// <summary>The world you stand on (everything on its surface shows, the parent's pad and world included).</summary>
    public static ShownWorld Standing(PlanetComposition c) => new(c.FolderId, c.Signature, InSky: false);

    /// <summary>A world hanging in the sky (a child or the parent): only its look from outside shows.</summary>
    public static ShownWorld Sky(PlanetComposition c) => new(c.FolderId, c.PreviewSignature, InSky: true);
}

/// <summary>
/// Which of the worlds on show need composing again (PLANET-174, AD-96). The planets show the world you stand on, its
/// child worlds in the sky and its parent; each was composed from the tree as it was then. Background discovery keeps
/// listing below you, and before the seventh pass every change to the current world's signature re-composed and rebuilt
/// <i>all</i> its neighbours on the main thread (475–1,141 ms on the Ubuntu root's 21 children, every 4–5 s). This
/// answers the narrower question the app now asks: which shown worlds look different in the tree now. Only those are
/// composed again (off the main thread, <see cref="BackgroundComposer"/>); the rest keep their bodies and visuals.
/// <para>
/// Preserve: it reads the tree only (call it where the tree is not being changed: the main thread); a world in the sky is
/// judged by its preview signature (a sibling's discovery changes the parent's counts, which only the surface shows,
/// <see cref="PlanetComposer.PreviewSignature"/>); a world whose signature is unchanged is never reported (the composer is
/// a pure function of what the signature covers); the order given is kept (the caller's priority: the current world
/// first); each id at most once; ids the tree does not contain are skipped.
/// </para>
/// </summary>
public static class Neighbourhood
{
    /// <summary>The worlds among <paramref name="shown"/> whose signature in <paramref name="tree"/> now differs, in order, each once.</summary>
    public static IReadOnlyList<int> Stale(FsTree tree, IEnumerable<ShownWorld> shown)
    {
        var stale = new List<int>();
        var seen = new HashSet<int>();
        foreach (var w in shown)
        {
            if (!seen.Add(w.FolderId) || !tree.Contains(w.FolderId) || !tree[w.FolderId].IsDirectory) continue;
            var now = w.InSky ? PlanetComposer.PreviewSignature(tree, w.FolderId) : PlanetComposer.Signature(tree, w.FolderId);
            if (now != w.Signature) stale.Add(w.FolderId);
        }

        return stale;
    }
}
