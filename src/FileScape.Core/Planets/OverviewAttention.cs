using FileScape.Core.Model;
using FileScape.Core.Scanning;

namespace FileScape.Core.Planets;

/// <summary>
/// How complete the neighbourhood of the world you are attending to is (PLANET-103, PLANET-107): the laid-out worlds in
/// its subtree down to <see cref="Levels"/> levels below it, how many of those whose listing would show those levels are
/// listed, still waiting for discovery, or cannot be listed (denied, failed, a link, outside discovery's scope), and how
/// many folders there the overview has folded away (more than a ring holds, or the budget). <see cref="Complete"/>: nothing
/// is left for discovery to do there.
/// </summary>
public readonly record struct NeighbourhoodStatus(int Focus, int Levels, int Worlds, int Listed, int Waiting, int Unlistable, int Folded)
{
    /// <summary>Nothing in the neighbourhood is waiting to be listed.</summary>
    public bool Complete => Waiting == 0;
}

/// <summary>
/// The overview's attention (PLANET-101..107, AD-81): which worlds the player is attending to — where you stand, the
/// selected world, the world the camera approaches, and the last few worlds selected or visited — and, from them, how the
/// overview's budget, discovery and status are spent. DJ (sixth pass): "Wherever I focus, I should be able to see and
/// explore the surrounding filesystem … A few levels of the local neighbourhood should unfold reliably, even after I've
/// explored many other branches. Retain useful knowledge while allowing distant detail to give way."
/// <para>
/// Before this (AD-71, AD-77) the budget (1,500 worlds) went breadth-first from the root and every relayout pinned every
/// world already drawn, so once the budget was full nothing new could be laid out: a selected world's listed children
/// stayed folded, discovery (whose candidates are laid-out worlds) had nothing left near it and said "done", and an
/// overview reopened at a deep world spent the budget on the shallow levels and showed that world alone.
/// <c>OverviewAttentionTests</c> reproduces both through the app's old calls.
/// </para>
/// <para>
/// Now the layout spends the budget by attention cost (<see cref="UniverseLayout.Attention"/>), so the neighbourhood of
/// every focus comes first and whatever lies far from all of them gives way; what was laid out last time is retained
/// against small moves (<see cref="Rules.RetainBonus"/>), and each system keeps the room it had while the overview stays
/// open (<see cref="UniverseLayout.Attention.Reserves"/>), so a system whose children give way folds in place and nothing
/// round it moves. Nothing learned is forgotten: the tree keeps every listing, and attention returning to a branch lays it
/// out again without listing it again. Discovery is offered only the laid-out worlds within <see cref="Rules.DiscoveryReach"/>
/// of attention, so it never recurses far from where you look.
/// </para>
/// Engine-free and deterministic for a given tree and sequence of calls; call from the main thread (it reads the tree).
/// The app (<c>PlanetWorld.Universe.cs</c>, <c>PlanetWorld.Discovery.cs</c>) sets the foci and asks for layouts,
/// candidates and the neighbourhood's status.
/// </summary>
public sealed class OverviewAttention
{
    /// <summary>The attention's tuning (attention units: one level of the tree). Defaults are the app's; tests vary them.</summary>
    public sealed record Rules
    {
        /// <summary>
        /// The most worlds the overview lays out. Measured on the Radeon 890M (sixth pass, see AD-81): the drawing of a
        /// laid-out world costs little; the relayout on the main thread and the per-frame placement grow with the count.
        /// </summary>
        public int Budget { get; init; } = DefaultBudget;

        /// <summary>Levels below a focus that make its neighbourhood: its children, grandchildren and great-grandchildren.</summary>
        public int NeighbourhoodLevels { get; init; } = 3;

        /// <summary>
        /// Discovery lists only laid-out worlds whose attention cost is at most this: four levels below the selected world
        /// and where you stand (so five are drawn), three below the approached world, two or less below earlier foci and
        /// two below the root by the background's cost. No recursive discovery far from attention.
        /// </summary>
        public float DiscoveryReach { get; init; } = 4f;

        /// <summary>The approached world's offset: a little behind the selected world and where you stand.</summary>
        public float ApproachedOffset { get; init; } = 1f;

        /// <summary>How many earlier foci (worlds selected or visited before) keep some attention.</summary>
        public int RecentFoci { get; init; } = 4;

        /// <summary>The most recent earlier focus's offset, and how much more each older one gets: what you looked at a
        /// moment ago stays partly unfolded and gives way gradually.</summary>
        public float RecentOffset { get; init; } = 1.5f;

        /// <summary>How much more each older earlier focus gets than the one after it.</summary>
        public float RecentOffsetPerAge { get; init; } = 1f;

        /// <summary>Hysteresis: a laid-out world gives way only to one at least this much nearer attention. Small, so a move
        /// of attention always wins over what was laid out for the last one (a larger bonus kept old neighbourhoods and
        /// left the new one folded, the failure this replaces).</summary>
        public float RetainBonus { get; init; } = 0.5f;

        /// <summary>The background's cost per level below the root (<see cref="UniverseLayout.Attention.BackgroundCostPerLevel"/>).</summary>
        public float BackgroundCostPerLevel { get; init; } = 2f;
    }

    /// <summary>The budget the app uses (<see cref="Rules.Budget"/>; AD-81 has the measurements).</summary>
    public const int DefaultBudget = 6000;

    private readonly Rules _rules;
    private readonly List<int> _recent = [];
    private readonly Dictionary<int, Reserve> _reserves = [];
    private HashSet<int> _laidOut = [];

    public OverviewAttention(Rules? rules = null) => _rules = rules ?? new Rules();

    public Rules Tuning => _rules;

    /// <summary>Where you stand (−1: nowhere).</summary>
    public int Current { get; set; } = -1;

    /// <summary>The selected world (−1: none).</summary>
    public int Selected { get; set; } = -1;

    /// <summary>The world the camera approaches: nearest the point it looks at (−1: none).</summary>
    public int Approached { get; set; } = -1;

    /// <summary>Earlier foci, most recent first (probes).</summary>
    public IReadOnlyList<int> Recent => _recent;

    /// <summary>Folders the budget left out of the last layout (folded into their parents): 0 when everything fits.</summary>
    public int BudgetFolded { get; private set; }

    /// <summary>The worlds in the last layout.</summary>
    public int LaidOut => _laidOut.Count;

    /// <summary>
    /// Attention left <paramref name="previous"/> (the world selected or stood on until now): it becomes the most recent
    /// earlier focus. Call when <see cref="Selected"/> or <see cref="Current"/> changes, with the old value.
    /// </summary>
    public void Note(int previous)
    {
        if (previous < 0) return;
        _recent.Remove(previous);
        _recent.Insert(0, previous);
        if (_recent.Count > _rules.RecentFoci) _recent.RemoveAt(_recent.Count - 1);
    }

    /// <summary>The foci now, with their offsets: where you stand and the selected world, the approached one, the recent ones.</summary>
    public IReadOnlyList<AttentionFocus> Foci()
    {
        var foci = new List<AttentionFocus>(3 + _recent.Count);
        var seen = new HashSet<int>();
        void Add(int id, float offset)
        {
            if (id >= 0 && seen.Add(id)) foci.Add(new AttentionFocus(id, offset));
        }

        Add(Selected, 0f);
        Add(Current, 0f);
        Add(Approached, _rules.ApproachedOffset);
        for (var i = 0; i < _recent.Count; i++) Add(_recent[i], _rules.RecentOffset + _rules.RecentOffsetPerAge * i);
        return foci;
    }

    /// <summary>A signature of the foci (ids and offsets): the app lays out again when it changes and the budget is full.</summary>
    public string Signature() => string.Join(",", Foci().Select(f => f.Id + ":" + f.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private UniverseLayout.Attention AttentionFor(bool keep) => new()
    {
        Foci = Foci(),
        Budget = _rules.Budget,
        BackgroundCostPerLevel = _rules.BackgroundCostPerLevel,
        Retained = keep ? _laidOut : null,
        RetainBonus = _rules.RetainBonus,
        Reserves = _reserves,
    };

    /// <summary>
    /// The overview's layout for the current attention. <paramref name="keep"/> false: a fresh picture (the overview
    /// opening: no retention, no reserved room); true: a relayout while it stays open (what is laid out is retained
    /// against small moves, every system keeps its room). <paramref name="pinned"/>: always laid out (the way to where you stand).
    /// </summary>
    public IReadOnlyList<UniverseBody> Layout(FsTree tree, Func<FsNode, bool>? visible, int root, IReadOnlySet<int>? pinned, bool keep)
    {
        if (!keep)
        {
            _reserves.Clear();
            _laidOut = [];
        }

        var bodies = UniverseLayout.Compute(tree, visible, root, pinned, AttentionFor(keep));
        _laidOut = bodies.Select(b => b.NodeId).ToHashSet();
        BudgetFolded = bodies.Count >= _rules.Budget ? bodies.Sum(b => b.HiddenChildren) : 0;
        return bodies;
    }

    /// <summary>
    /// What the overview would lay out for the current attention, without laying it out (play discovery, PLANET-90):
    /// ids and depths below <paramref name="root"/> in the layout's order. Does not change the retained set.
    /// </summary>
    public IReadOnlyList<(int Id, int Depth)> Selection(FsTree tree, Func<FsNode, bool>? visible, int root, IReadOnlySet<int>? pinned) =>
        UniverseLayout.Selection(tree, visible, root, pinned, AttentionFor(keep: false) with { Reserves = null });

    /// <summary>
    /// The worlds discovery may list now: the laid-out worlds within <see cref="Rules.DiscoveryReach"/> of attention, with
    /// their depth and index in the layout's order (PLANET-104). <paramref name="worlds"/> are (id, depth below the root) in
    /// layout order (a layout's bodies or a <see cref="Selection"/>).
    /// </summary>
    public List<OverviewDiscovery.Candidate> Candidates(FsTree tree, IReadOnlyList<(int Id, int Depth)> worlds, int root)
    {
        var att = AttentionFor(keep: false) with { Reserves = null };
        var list = new List<OverviewDiscovery.Candidate>(worlds.Count);
        for (var i = 0; i < worlds.Count; i++)
        {
            var (id, depth) = worlds[i];
            // Only folders discovery could list need the cost (it walks up the tree).
            if (!tree.Contains(id) || tree[id].ScanState != DirScanState.Unlisted) continue;
            if (UniverseLayout.AttentionCost(tree, id, root, att) <= _rules.DiscoveryReach) list.Add(new OverviewDiscovery.Candidate(id, depth, i));
        }

        return list;
    }

    /// <summary>
    /// Laid-out worlds known only by name that discovery could list but leaves for later because they are beyond its reach
    /// of attention (PLANET-107's "deferred elsewhere"), plus the folders the budget folded away.
    /// </summary>
    public int Deferred(FsTree tree, IReadOnlyList<UniverseBody> bodies, int root, Func<int, bool> inScope)
    {
        var att = AttentionFor(keep: false) with { Reserves = null };
        var deferred = 0;
        foreach (var b in bodies)
        {
            if (!tree.Contains(b.NodeId) || !OverviewDiscovery.Eligible(tree[b.NodeId])) continue;
            if (UniverseLayout.AttentionCost(tree, b.NodeId, root, att) > _rules.DiscoveryReach && inScope(b.NodeId)) deferred++;
        }

        return deferred + BudgetFolded;
    }

    /// <summary>
    /// The neighbourhood of <paramref name="focus"/> in <paramref name="bodies"/> (see <see cref="NeighbourhoodStatus"/>):
    /// the focus and the laid-out worlds up to <see cref="Rules.NeighbourhoodLevels"/> below it. A world at the last level
    /// need not be listed (its children would be a level further); the ones above it must be for the neighbourhood to be
    /// complete. <paramref name="inScope"/>: the app's rule for what discovery may list.
    /// </summary>
    public NeighbourhoodStatus Neighbourhood(FsTree tree, IReadOnlyList<UniverseBody> bodies, int focus, Func<int, bool> inScope)
    {
        var levels = _rules.NeighbourhoodLevels;
        if (!tree.Contains(focus)) return new NeighbourhoodStatus(focus, levels, 0, 0, 0, 0, 0);
        var fd = tree[focus].Depth;
        int worlds = 0, listed = 0, waiting = 0, unlistable = 0, folded = 0;
        foreach (var b in bodies)
        {
            if (!tree.Contains(b.NodeId)) continue;
            var n = tree[b.NodeId];
            var rel = n.Depth - fd;
            if (rel < 0 || rel > levels) continue;
            var a = b.NodeId;
            while (a >= 0 && tree[a].Depth > fd) a = tree[a].ParentId;
            if (a != focus) continue;
            worlds++;
            if (rel == levels) continue; // its listing would show the level beyond the neighbourhood
            folded += b.HiddenChildren;
            if (n.ScanState == DirScanState.Complete) listed++;
            else if (OverviewDiscovery.Eligible(n) && inScope(b.NodeId) && ListAhead.Refusal(tree.FullPath(b.NodeId), ListAheadKind.Background) == null) waiting++;
            else if (n.ScanState != DirScanState.Listing && n.ScanState != DirScanState.Pending) unlistable++;
            else waiting++;
        }

        return new NeighbourhoodStatus(focus, levels, worlds, listed, waiting, unlistable, folded);
    }
}
