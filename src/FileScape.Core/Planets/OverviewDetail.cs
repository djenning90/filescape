namespace FileScape.Core.Planets;

/// <summary>
/// How much of the laid-out overview is drawn (PLANET-91, AD-77): the level of detail, kept separate from what discovery
/// knows. DJ (fifth playtest follow-up): "the current breadth and depth can overwhelm the view. At the Ubuntu root,
/// worlds overlap until they form solid shapes." Every world is drawn at least a few pixels wide (FOLLOW-1), so a deep
/// system seen from afar packed hundreds of discs into the space of its ring.
/// <para>
/// The rule, per laid-out system (a world with children in the layout): it is <b>expanded</b> (its children drawn) when
/// its ring is large enough on screen to hold them apart — at least <see cref="Rules.ExpandRingPixels"/> of radius, and
/// its two closest children at least <see cref="Rules.ChildSpacingPixels"/> apart — or when it is on the way to a focus (the
/// selected world, the world approached, where you stand; always expanded, so you see where you are). Below the selected or
/// approached world the size bar (not the spacing) is lower (<see cref="Rules.FocusFactor"/>: those systems open sooner); away from the focus it rises with
/// every level a branch leaves the focus's path (<see cref="Rules.BranchPenaltyPerLevel"/>), so distant branches show
/// shallower detail. A system once expanded stays so until its ring falls below <see cref="Rules.CollapseShare"/> of the
/// bar (no flicker at the edge). A world is <b>shown</b> when its parent is shown and expanded. A shown system that is
/// not expanded is <b>folded</b>: the count of its laid-out descendants (plus the folders the layout's budget left out
/// below it) is what its indicator says.
/// </para>
/// <para>
/// It never moves, adds or removes a world from the layout, and never changes what discovery lists: the layout and the
/// tree are untouched, only drawing changes, so expanding and folding cannot make anything jump. Bodies must be in
/// the layout's order (every parent before its children), as <see cref="UniverseLayout.Compute"/> returns them.
/// Engine-free (<c>OverviewDetailTests</c>); the app passes each body's on-screen scale.
/// </para>
/// </summary>
public static class OverviewDetail
{
    /// <summary>The rule's tuning (screen pixels are viewport pixels). Defaults are the app's; tests vary them.</summary>
    public sealed record Rules
    {
        /// <summary>A system's ring must be at least this radius on screen to open: below it, its children (each at least
        /// 12 px across) would sit on top of their parent. Higher folds more.</summary>
        public float ExpandRingPixels { get; init; } = 40f;

        /// <summary>The least on-screen distance between the two closest children of an open ring, pixels: a 12 px disc and a gap, so neighbours on the ring stay
        /// apart (a 64-world ring opens at ≈ 160 px of radius).</summary>
        public float ChildSpacingPixels { get; init; } = 16f;

        /// <summary>Below a focus (the selected or approached world) the size bar is this share: such systems open sooner.
        /// The spacing bar is never lowered, so children never open on top of each other.</summary>
        public float FocusFactor { get; init; } = 0.6f;

        /// <summary>The bar rises by this share for each level a branch is away from the focus's path: distant
        /// branches show shallower detail.</summary>
        public float BranchPenaltyPerLevel { get; init; } = 0.5f;

        /// <summary>An expanded system folds again only below this share of its bar (hysteresis against flicker).</summary>
        public float CollapseShare { get; init; } = 0.8f;
    }

    /// <summary>
    /// Decides, for every body, whether it is shown, expanded and how many worlds are folded inside it.
    /// <paramref name="pixelsPerMetre"/> is each body's ring's on-screen scale at its distance (0 when unknown: never opens
    /// by size; the app folds in how much the ring is foreshortened); <paramref name="focus"/> node ids whose path from the
    /// root is always open and whose subtrees open sooner (the selected and approached worlds);
    /// <paramref name="pathOnly"/> node ids whose path is always open but whose subtrees get no lower bar (where you
    /// stand: on a drive root everything is below it, and a lower bar everywhere would draw everything);
    /// <paramref name="wasExpanded"/> last decision (same order and length, or null). Results are written into
    /// <paramref name="shown"/>, <paramref name="expanded"/> and <paramref name="folded"/> (each the bodies' length).
    /// </summary>
    /// <para><paramref name="layout"/>: what depends only on the layout (<see cref="Prepare"/>), made once per layout
    /// rather than every decision (PLANET-108: at tens of thousands of worlds this was most of the decision's cost).</para>
    public static void Compute(IReadOnlyList<UniverseBody> bodies, ReadOnlySpan<float> pixelsPerMetre, IReadOnlyCollection<int> focus,
        bool[]? wasExpanded, bool[] shown, bool[] expanded, int[] folded, Rules? rules = null, IReadOnlyCollection<int>? pathOnly = null, Prepared? layout = null)
    {
        rules ??= new Rules();
        var n = bodies.Count;
        if (n == 0) return;
        layout ??= Prepare(bodies);
        var (index, parent, childCount, minChord) = (layout.Index, layout.Parent, layout.ChildCount, layout.MinChord);

        // The focus path (a focus and its ancestors) and the focus subtrees.
        var onPath = new bool[n];
        var isFocus = new bool[n];
        foreach (var f in focus)
        {
            if (!index.TryGetValue(f, out var fi)) continue;
            isFocus[fi] = true;
            for (var a = fi; a >= 0 && !onPath[a]; a = parent[a]) onPath[a] = true;
        }

        foreach (var f in pathOnly ?? [])
        {
            if (!index.TryGetValue(f, out var fi)) continue;
            for (var a = fi; a >= 0 && !onPath[a]; a = parent[a]) onPath[a] = true;
        }

        if (!onPath.Any(x => x)) onPath[0] = true; // no focus: the root's own system is the way in
        var inFocus = new bool[n];
        var branch = new int[n];
        for (var i = 0; i < n; i++)
        {
            var p = parent[i];
            inFocus[i] = isFocus[i] || p >= 0 && inFocus[p];
            branch[i] = onPath[i] || inFocus[i] || p < 0 ? 0 : branch[p] + 1;
        }

        // Shown and expanded, top-down (parents come first).
        for (var i = 0; i < n; i++)
        {
            var p = parent[i];
            shown[i] = p < 0 || shown[p] && expanded[p];
            expanded[i] = false;
            if (!shown[i] || childCount[i] == 0) continue;
            if (onPath[i])
            {
                expanded[i] = true;
                continue;
            }

            // The focus lowers only the size bar, never the spacing: below it a ring still opens only when its children
            // fit round it apart (lowering both drew a drive root's systems on top of each other, 80 % overlapping).
            // Spacing: the closest two children on the ring (chord c) must be ChildSpacingPixels apart on screen, i.e. the ring
            // radius R must reach ChildSpacingPixels × R / c. For k evenly spaced children that is ≈ k × spacing / 2π; the
            // layout gives each child an arc in proportion to its system, so small worlds beside each other sit closer
            // (averaging over the ring left half the worlds drawn on C:\ overlapping).
            var spacing = minChord[i] > 0 ? rules.ChildSpacingPixels * bodies[i].OrbitRadius / minChord[i] : 0f;
            var bar = MathF.Max(rules.ExpandRingPixels * (inFocus[i] ? rules.FocusFactor : 1f), MathF.Max(spacing, childCount[i] * rules.ChildSpacingPixels / (2 * MathF.PI)));
            bar *= 1 + rules.BranchPenaltyPerLevel * branch[i];
            if (wasExpanded != null && i < wasExpanded.Length && wasExpanded[i]) bar *= rules.CollapseShare;
            var ppm = i < pixelsPerMetre.Length ? pixelsPerMetre[i] : 0f;
            expanded[i] = bodies[i].OrbitRadius * ppm >= bar;
        }

        // Folded counts, bottom-up: every laid-out descendant and every folder the layout's budget left out below.
        var below = new int[n];
        for (var i = n - 1; i >= 0; i--)
        {
            below[i] += bodies[i].HiddenChildren;
            if (parent[i] >= 0) below[parent[i]] += 1 + below[i];
        }

        for (var i = 0; i < n; i++) folded[i] = !shown[i] ? 0 : expanded[i] ? bodies[i].HiddenChildren : below[i];
    }

    /// <summary>What the rule needs from a layout that does not change until the next layout: each body's index by node id,
    /// its parent's index (−1 for none), its children's count and its closest children's distance (metres).</summary>
    public sealed record Prepared(Dictionary<int, int> Index, int[] Parent, int[] ChildCount, float[] MinChord);

    /// <summary>Works out <see cref="Prepared"/> for a layout (O(n + Σ k²)).</summary>
    public static Prepared Prepare(IReadOnlyList<UniverseBody> bodies)
    {
        var n = bodies.Count;
        var index = new Dictionary<int, int>(n);
        for (var i = 0; i < n; i++) index[bodies[i].NodeId] = i;
        var parent = new int[n];
        var childCount = new int[n];
        for (var i = 0; i < n; i++)
        {
            parent[i] = bodies[i].ParentId >= 0 && index.TryGetValue(bodies[i].ParentId, out var p) ? p : -1;
            if (parent[i] >= 0) childCount[parent[i]]++;
        }

        return new Prepared(index, parent, childCount, ClosestChildChords(bodies, parent)); // the chords decide whether a ring's children fit apart
    }

    /// <summary>
    /// For each body, the distance between its two closest children in the layout (metres; 0 with fewer than two): how
    /// far apart a ring's children can be drawn. The layout gives each child an arc in proportion to its whole system, so
    /// small worlds side by side are much closer than an even share of the ring. <paramref name="parent"/> is each body's
    /// parent index (−1 for none), or null to work it out. O(Σ k²) over the systems (k ≤ 64): call per layout or per
    /// detail step, not per frame.
    /// </summary>
    public static float[] ClosestChildChords(IReadOnlyList<UniverseBody> bodies, int[]? parent = null)
    {
        var n = bodies.Count;
        if (parent == null)
        {
            var index = new Dictionary<int, int>(n);
            for (var i = 0; i < n; i++) index[bodies[i].NodeId] = i;
            parent = new int[n];
            for (var i = 0; i < n; i++) parent[i] = bodies[i].ParentId >= 0 && index.TryGetValue(bodies[i].ParentId, out var p) ? p : -1;
        }

        var kids = new Dictionary<int, List<int>>();
        for (var i = 0; i < n; i++)
        {
            if (parent[i] < 0) continue;
            if (!kids.TryGetValue(parent[i], out var list)) kids[parent[i]] = list = [];
            list.Add(i);
        }

        var chord = new float[n];
        foreach (var (p, list) in kids)
        {
            if (list.Count < 2) continue;
            var best = float.MaxValue;
            for (var a = 0; a < list.Count; a++)
            {
                for (var b = a + 1; b < list.Count; b++) best = MathF.Min(best, System.Numerics.Vector3.Distance(bodies[list[a]].Position, bodies[list[b]].Position));
            }

            chord[p] = best;
        }

        return chord;
    }
}
