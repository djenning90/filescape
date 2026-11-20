using FileScape.Core.Model;
using FileScape.Core.Planets;
using FileScape.Core.Scanning;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// The overview follows attention at any depth (PLANET-101..109, AD-81; DJ, sixth pass: "after exploring toward
/// …\filescape\app, selecting folders stopped revealing their children … reopening Overview sometimes showed only that
/// folder … discovery was done at 1,500 worlds"). A vast simulated tree — twelve wide branches already explored and a
/// spine fourteen levels deep — is driven the way the app drives it: lay out, ask discovery, answer its listings, lay out
/// again; then the player selects and visits deep worlds.
/// </summary>
public sealed class OverviewAttentionTests
{
    /// <summary>A filesystem of folders only, listed on request (as <c>OverviewDiscoveryTests</c>'s, with any shape).</summary>
    private sealed class SimFs
    {
        public readonly FsTree Tree = new(new DataSourceInfo(DataOrigin.Synthetic, "synthetic://vast", "vast"));
        private readonly Dictionary<int, string> _path = [];
        private readonly Func<string, IEnumerable<string>> _children;
        private readonly Dictionary<string, int> _byPath = [];

        public SimFs(Func<string, IEnumerable<string>> children)
        {
            _children = children;
            var b = new ScanBatch();
            b.NewNodes.Add(new NodeRecord(0, -1, "root", NodeKind.Directory, 0, 0, 0, null, null, null, 0, DirScanState.Unlisted));
            Tree.Apply(b);
            _path[0] = "";
            _byPath[""] = 0;
            Answer(0);
        }

        public string PathOf(int id) => _path[id];

        public int Id(string path) => _byPath[path];

        public void Answer(int id)
        {
            if (Tree[id].ScanState == DirScanState.Complete) return;
            var b = new ScanBatch();
            var next = Tree.Count;
            foreach (var name in _children(_path[id]).OrderBy(n => n, StringComparer.Ordinal))
            {
                var cid = next++;
                b.NewNodes.Add(new NodeRecord(cid, id, name, NodeKind.Directory, 0, 0, 0, null, null, null, Tree[id].Depth + 1, DirScanState.Unlisted));
                _path[cid] = _path[id] + "/" + name;
                _byPath[_path[cid]] = cid;
            }

            b.DirectoryUpdates.Add(new DirectoryUpdate(id, DirScanState.Complete, 0));
            Tree.Apply(b);
        }

        /// <summary>A visit: the folder and its children listed (the explorer's destinations and context).</summary>
        public void Visit(string path)
        {
            var id = Id(path);
            Answer(id);
            foreach (var c in Tree[id].Children.ToList()) Answer(c);
        }
    }

    /// <summary>
    /// Twelve branches b00…b11, each six wide for four levels (≈ 3,100 folders: more than the old budget by breadth alone),
    /// and /route: a spine s1…s14 where every spine folder also holds four side folders of three leaves each.
    /// </summary>
    private static IEnumerable<string> Vast(string p)
    {
        var depth = p.Count(c => c == '/');
        if (depth == 0) return Enumerable.Range(0, 12).Select(i => $"b{i:D2}").Append("route");
        if (p.StartsWith("/b", StringComparison.Ordinal)) return depth >= 4 ? [] : Enumerable.Range(0, 6).Select(i => $"x{i}");
        var last = p[(p.LastIndexOf('/') + 1)..];
        if (last == "route" || last.StartsWith('s'))
        {
            var level = last == "route" ? 0 : int.Parse(last[1..], System.Globalization.CultureInfo.InvariantCulture);
            var side = Enumerable.Range(0, 4).Select(i => $"k{i}");
            return level >= 14 ? side : side.Append($"s{level + 1}");
        }

        return last.StartsWith('k') ? Enumerable.Range(0, 3).Select(i => $"m{i}") : [];
    }

    /// <summary>The spine folder <paramref name="level"/> levels below /route (0: /route itself).</summary>
    private static string Spine(int level) => "/route" + string.Concat(Enumerable.Range(1, level).Select(i => $"/s{i}"));

    /// <summary>The old budget, so the simulated tree (≈ 3,400 folders) overflows it as DJ's did.</summary>
    private static readonly OverviewAttention.Rules Small = new() { Budget = 1500 };

    /// <summary>
    /// The app's overview loop (<c>PlanetWorld.ComputeUniverse</c> and <c>StepDiscovery</c>): every 0.1 s the explorer's
    /// answers arrive, the overview lays out again with attention (retaining what it drew), and discovery is offered the
    /// laid-out worlds within attention's reach, the selected world first.
    /// </summary>
    private sealed class Loop(SimFs fs, OverviewAttention.Rules? rules = null)
    {
        public readonly OverviewAttention Attention = new(rules ?? Small);
        public readonly OverviewDiscovery Discovery = new();
        private readonly List<(int Id, TimeSpan Due)> _pending = [];
        public readonly List<int> Asked = [];
        public IReadOnlyList<UniverseBody> Bodies { get; private set; } = [];
        public TimeSpan Now { get; private set; }
        private int _version = -1;
        private string _signature = "";
        private TimeSpan _laidAt;

        public HashSet<int> PathTo(int id)
        {
            var s = new HashSet<int>();
            for (var a = id; a >= 0; a = fs.Tree[a].ParentId) s.Add(a);
            return s;
        }

        /// <summary>T: a fresh picture.</summary>
        public void Open()
        {
            Bodies = Attention.Layout(fs.Tree, null, 0, PathTo(Math.Max(0, Attention.Current)), keep: false);
            (_version, _signature, _laidAt) = (fs.Tree.Version, Attention.Signature(), Now);
        }

        /// <summary>A click: the selection moves (the world it leaves becomes a recent focus).</summary>
        public void Select(int id)
        {
            Attention.Note(Attention.Selected);
            Attention.Selected = id;
        }

        public void Run(double seconds)
        {
            var end = Now + TimeSpan.FromSeconds(seconds);
            while (Now < end)
            {
                Now += TimeSpan.FromSeconds(0.1);
                foreach (var p in _pending.Where(p => p.Due <= Now).ToList())
                {
                    _pending.Remove(p);
                    fs.Answer(p.Id);
                }

                // The app's cadence: a grown tree at most every 1.5 s, a move of attention (when the budget is full) every 0.3 s.
                var since = (Now - _laidAt).TotalSeconds;
                if (fs.Tree.Version != _version && since >= 1.5 || Attention.BudgetFolded > 0 && Attention.Signature() != _signature && since >= 0.3)
                {
                    Bodies = Attention.Layout(fs.Tree, null, 0, PathTo(Math.Max(0, Attention.Current)), keep: true);
                    (_version, _signature, _laidAt) = (fs.Tree.Version, Attention.Signature(), Now);
                }

                var worlds = Bodies.Select(b => (b.NodeId, b.Depth)).ToList();
                var focus = new List<int> { Attention.Selected };
                foreach (var id in Discovery.Step(fs.Tree, Attention.Candidates(fs.Tree, worlds, 0), focus, Now, _ => true))
                {
                    _pending.Add((id, Now + TimeSpan.FromSeconds(0.05)));
                    Asked.Add(id);
                }
            }
        }

        public int Kids(int id) => Bodies.Count(b => b.ParentId == id);

        public NeighbourhoodStatus Near(int id) => Attention.Neighbourhood(fs.Tree, Bodies, id, _ => true);
    }

    /// <summary>Opens the overview at the root and explores the twelve wide branches, each selected in turn (the budget fills).</summary>
    private static (SimFs Fs, Loop Loop) Explored()
    {
        var fs = new SimFs(Vast);
        var loop = new Loop(fs);
        loop.Attention.Current = 0;
        loop.Open();
        foreach (var b in fs.Tree[0].Children.Where(c => fs.Tree[c].Name.StartsWith('b')).ToList())
        {
            loop.Select(b);
            loop.Run(4);
        }

        loop.Bodies.Count.Should().Be(Small.Budget, "the explored branches fill the budget");
        return (fs, loop);
    }

    /// <summary>PLANET-101: reproduced first through the old calls (it failed on the base: "found 1", the first level only).</summary>
    [Fact]
    public void After_exploring_wide_branches_every_selected_world_down_a_deep_spine_reveals_its_children()
    {
        var (fs, loop) = Explored();
        for (var level = 0; level <= 13; level++)
        {
            var id = fs.Id(Spine(level));
            loop.Bodies.Should().Contain(b => b.NodeId == id, $"spine level {level} is drawn, so it can be selected");
            loop.Select(id);
            loop.Run(3);
            loop.Kids(id).Should().Be(5, $"spine level {level} reveals its folders once selected");
        }

        loop.Bodies.Count.Should().BeLessThanOrEqualTo(Small.Budget);
    }

    /// <summary>PLANET-102: reproduced first through the old calls (on the base the visited world was drawn alone: "found 0").</summary>
    [Fact]
    public void Reopening_the_overview_at_a_deep_visited_world_shows_its_children()
    {
        var (fs, _) = Explored();
        for (var level = 0; level <= 10; level++) fs.Visit(Spine(level));
        var here = fs.Id(Spine(10));
        var reopened = new Loop(fs);
        reopened.Attention.Current = here;
        reopened.Attention.Selected = here;
        reopened.Open();
        reopened.Kids(here).Should().Be(5, "the world you stand on shows its folders");
        foreach (var k in fs.Tree[here].Children) reopened.Kids(k).Should().Be(fs.Tree[k].Children.Count, "and theirs, which the visit listed");
    }

    /// <summary>PLANET-103, PLANET-107: three levels unfold below the selected world and its neighbourhood says it is
    /// complete, while far branches are left for later and said to be.</summary>
    [Fact]
    public void A_deep_neighbourhood_unfolds_three_levels_and_is_complete_while_far_branches_wait()
    {
        var (fs, loop) = Explored();
        foreach (var level in Enumerable.Range(0, 10)) fs.Answer(fs.Id(Spine(level))); // the way there, as a walk down lists it
        var s9 = fs.Id(Spine(9));
        loop.Select(s9);
        loop.Run(8);
        var near = loop.Near(s9);
        near.Complete.Should().BeTrue($"nothing is waiting round the selected world ({near})");
        // Below s9: s10 and k0..k3 (5); s11, s10's sides and k0..k3's leaves (5 + 12 = 17); s12, s11's sides and s10's sides' leaves (17).
        var rel = loop.Bodies.Where(b => IsUnder(fs, b.NodeId, s9)).GroupBy(b => fs.Tree[b.NodeId].Depth - fs.Tree[s9].Depth).ToDictionary(g => g.Key, g => g.Count());
        rel.GetValueOrDefault(1).Should().Be(5);
        rel.GetValueOrDefault(2).Should().Be(17);
        rel.GetValueOrDefault(3).Should().Be(17);
        var deferred = loop.Attention.Deferred(fs.Tree, loop.Bodies, 0, _ => true);
        deferred.Should().BeGreaterThan(0, "far branches still hold folders known only by name, left for when you look there");
        var (first, second) = OverviewStatus.Text(loop.Discovery.State, near, "s9", deferred, loop.Bodies.Count, 200_000, "◐");
        first.Should().Contain("complete").And.Contain("s9");
        second.Should().Contain("wait until you look there");
    }

    /// <summary>PLANET-104: discovery stays near attention: nothing is listed whose attention cost is beyond its reach.</summary>
    [Fact]
    public void Discovery_never_recurses_far_from_attention()
    {
        var fs = new SimFs(Vast);
        var loop = new Loop(fs);
        var s12 = -1;
        foreach (var level in Enumerable.Range(0, 13)) fs.Answer(s12 = fs.Id(Spine(level)));
        loop.Attention.Current = s12; // landed there
        loop.Open();
        loop.Select(s12);
        loop.Run(60);
        // Far branches: the background reaches two levels below the root (cost 2 a level, reach 4), never deeper.
        loop.Asked.Where(id => fs.PathOf(id).StartsWith("/b", StringComparison.Ordinal)).Select(id => fs.Tree[id].Depth).DefaultIfEmpty(0).Max().Should().BeLessThanOrEqualTo(2);
        loop.Asked.Should().Contain(fs.Tree[s12].Children, "while the selected world's own folders are listed");
    }

    /// <summary>
    /// PLANET-105, PLANET-106: when attention moves far away the old neighbourhood's detail gives way (folded, not
    /// refused); its system keeps its place and room, so its worlds do not move round it; nothing learned is forgotten,
    /// and returning lays it out again at once without listing anything again.
    /// </summary>
    [Fact]
    public void Distant_detail_gives_way_in_place_and_returns_without_listing_again()
    {
        var (fs, loop) = Explored();
        // /b03 selected: its whole system (6 + 36 + 216 worlds, three levels) is laid out and listed.
        var x = fs.Id("/b03");
        loop.Select(x);
        loop.Run(8);
        var b03 = x;
        Dictionary<int, System.Numerics.Vector3> Around(IReadOnlyList<UniverseBody> bodies)
        {
            var at = bodies.ToDictionary(b => b.NodeId, b => b.Position);
            return fs.Tree[b03].Children.Where(at.ContainsKey).ToDictionary(c => c, c => at[c] - at[b03]);
        }

        var before = Around(loop.Bodies);
        var beforeRing = loop.Bodies.Single(b => b.NodeId == b03).OrbitRadius;
        var underX = loop.Bodies.Count(b => IsUnder(fs, b.NodeId, x) && b.NodeId != x);
        underX.Should().Be(6 + 36 + 216, "the selected world's three levels below are drawn");
        // Attention goes far away: down the spine to level 12, standing there.
        var s = 0;
        foreach (var level in Enumerable.Range(0, 13)) fs.Answer(s = fs.Id(Spine(level)));
        loop.Select(s);
        loop.Attention.Note(loop.Attention.Current);
        loop.Attention.Current = s;
        loop.Run(4);
        // …and on to other far branches, until /b03 is no longer among the recent foci.
        foreach (var other in new[] { "/b05/x1", "/b07/x3", "/b09/x4", "/b11/x0", "/b01/x5" })
        {
            loop.Select(fs.Id(other));
            loop.Run(4);
        }
        loop.Bodies.Count(b => IsUnder(fs, b.NodeId, x) && b.NodeId != x).Should().BeLessThan(underX, "distant detail gave way");
        loop.Bodies.Should().Contain(b => b.NodeId == b03, "the branch itself is still there, folded");
        // Its worlds keep their places round it: within float slack of the layout's metres-scale sums (without the
        // reserved room the folded system shrank and its neighbours slid metres along the ring).
        var ring = loop.Bodies.Single(b => b.NodeId == b03).OrbitRadius;
        var moved = Around(loop.Bodies).Where(kv => before.ContainsKey(kv.Key)).Select(kv => (kv.Value - before[kv.Key]).Length()).DefaultIfEmpty(0).Max();
        moved.Should().BeLessThan(ring * 1e-3f, $"the worlds round /b03 keep their places (ring {ring:0.0} m, was {beforeRing:0.0} m)");

        // Return: laid out again from what is known, and nothing there is listed again.
        var listedBefore = fs.Tree.Nodes.Where(n => n.ScanState == DirScanState.Complete && IsUnder(fs, n.Id, x)).Select(n => n.Id).ToHashSet();
        var asked = loop.Asked.Count;
        loop.Select(x);
        loop.Run(0.1);
        loop.Bodies.Count(b => IsUnder(fs, b.NodeId, x) && b.NodeId != x).Should().Be(underX, "the neighbourhood is back at the next layout");
        loop.Run(3);
        loop.Asked.Skip(asked).Should().NotIntersectWith(listedBefore, "nothing listed before is listed again");
        loop.Asked.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// PLANET-106: the layout's order does not follow attention. The app steps round a ring in it ([ and ], PLANET-93);
    /// in the costs' order the selected world moved to the front of its ring at every selection, and [ after ] did not
    /// step back (found by <c>planets-overview-navigate</c> on the staged build).
    /// </summary>
    [Fact]
    public void The_layouts_order_is_breadth_first_by_name_whatever_is_selected()
    {
        var fs = new SimFs(Vast);
        foreach (var b in fs.Tree[0].Children.ToList()) fs.Answer(b);
        var ring = fs.Tree[0].Children.OrderBy(c => fs.Tree[c].Name, StringComparer.Ordinal).ToList();
        foreach (var selected in new[] { ring[0], ring[5], ring[^1] })
        {
            var a = new OverviewAttention(Small) { Current = 0, Selected = selected };
            var bodies = a.Layout(fs.Tree, null, 0, null, keep: false);
            bodies.Where(b => b.ParentId == 0).Select(b => b.NodeId).Should().Equal(ring, $"the root's ring in name order with {fs.PathOf(selected)} selected");
            bodies.Select(b => b.Depth).Should().BeInAscendingOrder("breadth-first");
        }
    }

    /// <summary>PLANET-107: the indicator's words.</summary>
    [Fact]
    public void The_status_tells_a_complete_neighbourhood_from_discovery_deferred_elsewhere()
    {
        var complete = new NeighbourhoodStatus(1, 3, 40, 12, 0, 0, 0);
        var busy = new NeighbourhoodStatus(1, 3, 40, 5, 7, 0, 0);
        var (a1, a2) = OverviewStatus.Text(OverviewDiscoveryState.Discovering, complete, "app", 312, 4000, 200_000, "◐");
        a1.Should().Be("✓ Round app: complete, 3 levels");
        a2.Should().Be("312 farther folders wait until you look there · 4,000 worlds");
        var (b1, _) = OverviewStatus.Text(OverviewDiscoveryState.Discovering, busy, "app", 0, 4000, 200_000, "◐");
        b1.Should().Be("◐ Discovering round app: 5 of 12 folders listed · X pauses");
        var (c1, c2) = OverviewStatus.Text(OverviewDiscoveryState.Complete, complete, "app", 0, 1500, 200_000, "◐");
        c1.Should().StartWith("✓ Round app: complete");
        c2.Should().Be("Nothing left to list in view · 1,500 worlds");
        (c1 + c2).Should().NotContain("as listed as they will be", "the budget no longer reads as discovery's end");
        OverviewStatus.Text(OverviewDiscoveryState.Paused, busy, "app", 0, 10, 200_000, "◐").Near.Should().Contain("paused").And.Contain("X resumes");
        OverviewStatus.Short(new string('a', 40)).Should().HaveLength(OverviewStatus.MaxNameChars);
        OverviewStatus.Short("filescape").Should().Be("filescape");
    }

    private static bool IsUnder(SimFs fs, int id, int ancestor)
    {
        for (var a = id; a >= 0; a = fs.Tree[a].ParentId) if (a == ancestor) return true;
        return false;
    }
}
