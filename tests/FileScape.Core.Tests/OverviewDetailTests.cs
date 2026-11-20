using FileScape.Core.Model;
using FileScape.Core.Planets;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// The overview's level of detail (PLANET-91, AD-77): what is drawn is decided separately from what is known. A system
/// opens when its ring is big enough on screen to hold its children apart, or when it is on the way to the selected or
/// approached world; distant branches open later; a folded system counts what it holds; nothing in the layout moves.
/// </summary>
public sealed class OverviewDetailTests
{
    /// <summary>A uniform tree: <paramref name="fanout"/> listed folders per folder, <paramref name="depth"/> levels.</summary>
    private static FsTree Uniform(int fanout, int depth)
    {
        Spec Make(string name, int level)
        {
            var s = new Spec(name);
            if (level < depth)
            {
                for (var i = 0; i < fanout; i++) s.Dir(Make($"{name}{(char)('a' + i)}", level + 1));
            }

            return s;
        }

        return Make("r", 0).Build();
    }

    private static (bool[] Shown, bool[] Expanded, int[] Folded) Run(IReadOnlyList<UniverseBody> bodies, float ppm, IReadOnlyCollection<int> focus, bool[]? was = null, Func<int, float>? ppmOf = null)
    {
        var scale = Enumerable.Range(0, bodies.Count).Select(i => ppmOf?.Invoke(i) ?? ppm).ToArray();
        var shown = new bool[bodies.Count];
        var expanded = new bool[bodies.Count];
        var folded = new int[bodies.Count];
        OverviewDetail.Compute(bodies, scale, focus, was, shown, expanded, folded);
        return (shown, expanded, folded);
    }

    [Fact]
    public void Seen_from_far_only_the_root_system_opens_and_everything_below_is_counted()
    {
        var tree = Uniform(4, 4);
        var bodies = UniverseLayout.Compute(tree);
        var (shown, expanded, folded) = Run(bodies, 0.001f, []);
        shown.Count(x => x).Should().Be(1 + 4, "the root (the way in) and its children");
        expanded[0].Should().BeTrue();
        // Each child folds its own 4 + 16 + 64 descendants.
        for (var i = 1; i <= 4; i++) folded[i].Should().Be(4 + 16 + 64);
        (shown.Count(x => x) + folded.Sum()).Should().Be(bodies.Count, "what is not drawn is counted, nothing lost");
    }

    [Fact]
    public void Close_up_everything_opens_and_depth_shown_grows_as_the_view_comes_closer()
    {
        var tree = Uniform(4, 4);
        var bodies = UniverseLayout.Compute(tree);
        var counts = new[] { 0.01f, 0.1f, 0.5f, 2f, 20f }.Select(p => Run(bodies, p, []).Shown.Count(x => x)).ToList();
        counts.Should().BeInAscendingOrder("closer shows more");
        counts[^1].Should().Be(bodies.Count, "close enough, everything is drawn");
    }

    [Fact]
    public void The_way_to_the_selected_world_is_open_and_the_selected_system_opens_sooner_than_its_neighbours()
    {
        var tree = Uniform(4, 4);
        var bodies = UniverseLayout.Compute(tree);
        var deep = bodies.First(b => b.Depth == 3);
        var (shown, expanded, _) = Run(bodies, 0.001f, [deep.NodeId]);
        var index = bodies.Select((b, i) => (b.NodeId, i)).ToDictionary(x => x.NodeId, x => x.i);
        shown[index[deep.NodeId]].Should().BeTrue("the selected world is drawn, however far");
        for (var a = deep.ParentId; a >= 0; a = tree[a].ParentId) expanded[index[a]].Should().BeTrue("every system on the way to it is open");

        // At a scale where an ordinary level-1 system just fails to open, the selected one (lower bar) opens.
        var sel = bodies.First(b => b.Depth == 1);
        var other = bodies.Last(b => b.Depth == 1);
        var bar = MathF.Max(new OverviewDetail.Rules().ExpandRingPixels, 4 * new OverviewDetail.Rules().ChildSpacingPixels / (2 * MathF.PI));
        var ppm = bar * 0.9f / other.OrbitRadius;
        var r = Run(bodies, ppm, []);
        r.Expanded[index[other.NodeId]].Should().BeFalse("an ordinary system below its bar stays folded");
        // sel is on the focus path itself (open); its children are below the focus and open with the lower bar.
        var child = bodies.First(b => b.ParentId == sel.NodeId);
        var childBar = MathF.Max(new OverviewDetail.Rules().ExpandRingPixels, 4 * new OverviewDetail.Rules().ChildSpacingPixels / (2 * MathF.PI));
        var ppmChild = childBar * 0.8f / child.OrbitRadius;
        Run(bodies, ppmChild, [sel.NodeId]).Expanded[index[child.NodeId]].Should().BeTrue("below the selected world the bar is lower");
        Run(bodies, ppmChild, []).Expanded[index[child.NodeId]].Should().BeFalse("without the selection the same system stays folded");
    }

    [Fact]
    public void Distant_branches_show_shallower_detail_than_the_focus_branch()
    {
        var tree = Uniform(3, 5);
        var bodies = UniverseLayout.Compute(tree);
        var focus = bodies.First(b => b.Depth == 2);
        // At every scale the focus's own branch shows at least as deep as a branch away from its path, and deeper at some.
        bool Under(UniverseBody b, int a)
        {
            for (var x = b.NodeId; x >= 0; x = tree[x].ParentId)
            {
                if (x == a) return true;
            }

            return false;
        }

        var level1OfFocus = tree[focus.NodeId].ParentId;
        var farBranch = bodies.First(b => b.Depth == 1 && b.NodeId != level1OfFocus).NodeId;
        var deeperSomewhere = false;
        for (var ppm = 0.01f; ppm < 100f; ppm *= 1.5f)
        {
            var (shown, _, _) = Run(bodies, ppm, [focus.NodeId]);
            int Deepest(int a) => bodies.Where((b, i) => shown[i] && Under(b, a)).Select(b => b.Depth).DefaultIfEmpty(0).Max();
            Deepest(focus.NodeId).Should().BeGreaterThanOrEqualTo(Deepest(farBranch), $"at {ppm} px/m");
            deeperSomewhere |= Deepest(focus.NodeId) > Deepest(farBranch);
        }

        deeperSomewhere.Should().BeTrue("detail falls away from the focus");
    }

    [Fact]
    public void An_open_system_folds_again_only_well_below_its_bar()
    {
        var tree = Uniform(4, 2);
        var bodies = UniverseLayout.Compute(tree);
        var child = bodies.First(b => b.Depth == 1);
        var i = bodies.ToList().IndexOf(child);
        // A level-1 system with no selection is one level off the way in (the root): its bar carries one branch penalty.
        var rules = new OverviewDetail.Rules();
        var bar = MathF.Max(rules.ExpandRingPixels, 4 * rules.ChildSpacingPixels / (2 * MathF.PI)) * (1 + rules.BranchPenaltyPerLevel);
        var ppm = bar * 0.9f / child.OrbitRadius;
        var was = new bool[bodies.Count];
        Run(bodies, ppm, [], was).Expanded[i].Should().BeFalse("below its bar it does not open");
        was[i] = true;
        Run(bodies, ppm, [], was).Expanded[i].Should().BeTrue("once open it stays open until well below the bar");
        Run(bodies, bar * 0.7f / child.OrbitRadius, [], was).Expanded[i].Should().BeFalse();
    }

    [Fact]
    public void Folded_counts_include_what_the_layout_budget_left_out()
    {
        // 70 children: the layout draws 64 and counts 6 as hidden.
        var tree = Spec.WithDirs("r", 70).Build();
        var bodies = UniverseLayout.Compute(tree);
        bodies[0].HiddenChildren.Should().Be(6);
        var (_, expanded, folded) = Run(bodies, 100f, []);
        expanded[0].Should().BeTrue();
        folded[0].Should().Be(6, "an open system still says what the budget left out");
    }
}
