using System.Globalization;
using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Units;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>
/// Traversal timing and continuous upward travel (WALK-10, PLACE-16), measured with the real walker on composed places.
/// "Before" uses the 2026-11-05 afternoon tuning (walk 4.2, run 8.4 m/s; arriving after Up in front of the child's gate
/// facing the landmark) so the report can compare; "after" uses the defaults.
/// </summary>
public class TraversalTests(ITestOutputHelper output)
{
    /// <summary>The walking parameters before DJ's movement feedback (KD-20), kept for comparison only.</summary>
    /// <remarks>
    /// The jump is pinned too (2026-11-06: the default jump became higher, with an apex hang and short hops), so the golden
    /// walk keeps the trajectory it was captured with; the new jump has its own tests in <c>JumpFeelTests</c>.
    /// </remarks>
    internal static readonly WalkParams Before = WalkParams.Default with
    {
        WalkSpeed = MetersPerSecond.Of(4.2f), RunSpeed = MetersPerSecond.Of(8.4f),
        JumpSpeed = MetersPerSecond.Of(5.3f), ApexHangSpeed = MetersPerSecond.Of(0f), ShortHopGravityScale = 1f, FallGravityScale = 1f,
    };

    /// <summary>
    /// The speeds the spacious layout had when it was replaced (8 / 16 m/s). The tests below this keep checking the
    /// 2026-11-05 improvements in that layout; <see cref="Compact_places_shorten_journeys_at_comfortable_speeds"/> compares
    /// the compact layout with it (KD-28).
    /// </summary>
    internal static readonly WalkParams Spacious8 = WalkParams.Default with { WalkSpeed = MetersPerSecond.Of(8f), RunSpeed = MetersPerSecond.Of(16f) };

    /// <summary>Threshold transition time the app adds per place change (out + in), before and after this pass.</summary>
    private static readonly TimeSpan TransitionBefore = TimeSpan.FromSeconds(0.42 + 0.62), TransitionAfter = TimeSpan.FromSeconds(0.30 + 0.45);

    /// <summary>A typical folder (files, eight subfolders), a wide one (tiers) and a chain of typical folders for repeated Up.</summary>
    internal static (FsTree Tree, int Typical, int Wide, int[] Chain) Places()
    {
        Spec Typical(string name, Spec? inner = null)
        {
            var s = Spec.WithFiles(name, 30);
            for (var i = 0; i < 8; i++) s.Dir(i == 3 && inner != null ? inner : new Spec($"{name}-sub-{i}").File("a.txt"));
            return s;
        }

        var deepest = Typical("level-5");
        var level4 = Typical("level-4", deepest);
        var level3 = Typical("level-3", level4);
        var level2 = Typical("level-2", level3);
        var level1 = Typical("level-1", level2);
        var wide = Spec.WithDirs("wide", 70);
        for (var i = 0; i < 20; i++) wide.File($"w-{i}.txt", 1000 * (i + 1));
        var tree = new Spec("top").Dir(level1).Dir(wide).Build();
        int Id(string n) => tree.Find(n).Id;
        return (tree, Id("level-1"), Id("wide"), [Id("level-1"), Id("level-2"), Id("level-3"), Id("level-4"), Id("level-5")]);
    }

    private static string S(TimeSpan t) => t.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

    [Fact]
    public void Arrival_to_child_gates_takes_about_half_as_long_as_before()
    {
        var (tree, typical, wide, _) = Places();
        foreach (var (label, id) in new[] { ("typical (8 gates)", typical), ("wide (70 gates, tiers)", wide) })
        {
            var place = PlaceComposer.Compose(tree, id, PlaceParams.Spacious);
            var near = place.Gates.MinBy(g => Vector3.Distance(g.Position, place.Arrival.Eye))!;
            var far = place.Gates.MaxBy(g => Vector3.Distance(g.Position, place.Arrival.Eye))!;
            foreach (var (which, gate) in new[] { ("nearest", near), ("farthest", far) })
            {
                var route = PlaceRoutes.ArrivalToGate(place, gate);
                var walkBefore = PlaceRoutes.FollowWaypoints(place, place.Arrival, route, gate, Before, run: false);
                var runBefore = PlaceRoutes.FollowWaypoints(place, place.Arrival, route, gate, Before, run: true);
                var walkAfter = PlaceRoutes.FollowWaypoints(place, place.Arrival, route, gate, Spacious8, run: false);
                var runAfter = PlaceRoutes.FollowWaypoints(place, place.Arrival, route, gate, Spacious8, run: true);
                output.WriteLine($"{label}, {which} gate ({walkAfter.Distance:0} m): walk {S(walkBefore.Time)} → {S(walkAfter.Time)}, run {S(runBefore.Time)} → {S(runAfter.Time)}");
                foreach (var r in new[] { walkBefore, runBefore, walkAfter, runAfter }) r.Crossed.Should().BeTrue($"{label} {which}: the route reaches the gate");
                // WALK-10: "about half as long". Speeds went 4.2 → 8 m/s walking and 8.4 → 16 m/s running (time × ≈ 0.53 on
                // a straight line); 0.6 is the accepted bound, leaving room above that ratio (its exact value not recorded).
                walkAfter.Time.TotalSeconds.Should().BeLessThan(walkBefore.Time.TotalSeconds * 0.6);
                runAfter.Time.TotalSeconds.Should().BeLessThan(runBefore.Time.TotalSeconds * 0.6);
            }
        }
    }

    [Fact]
    public void Going_up_arrives_facing_the_next_way_up_and_straight_ahead_reaches_it()
    {
        var (tree, _, wide, chain) = Places();
        var p = PlaceParams.Spacious;
        // From each level up to its parent, and from the wide folder's gates (near, middle, far tiers) to the top.
        var cases = new List<(int Parent, int Child, string Label)>();
        for (var i = chain.Length - 1; i > 0; i--) cases.Add((chain[i - 1], chain[i], $"{tree[chain[i]].Name} → {tree[chain[i - 1]].Name}"));
        var widePlace = PlaceComposer.Compose(tree, wide, PlaceParams.Spacious);
        foreach (var g in new[] { widePlace.Gates[0], widePlace.Gates[widePlace.Gates.Count / 2], widePlace.Gates[^1] })
        {
            cases.Add((wide, g.TargetId, $"{tree[g.TargetId].Name} → wide (tier {g.Tier + 1})"));
        }

        foreach (var (parent, child, label) in cases)
        {
            var place = PlaceComposer.Compose(tree, parent, PlaceParams.Spacious);
            var route = PlaceRoutes.ContinueUp(place, child, p, Spacious8);
            route.Verified.Should().BeTrue($"{label}: straight ahead must reach the ▲ gate");
            var walk = PlaceRoutes.WalkStraight(place, route.Pose, place.ParentGate!, Spacious8, run: false);
            walk.Crossed.Should().BeTrue($"{label}: walking (not only running) reaches it too");
            walk.MaxDeviation.Should().BeLessThan(0.3f, $"{label}: nothing pushes the walker off the line");
            // Before: arriving in front of the child's gate facing the landmark; reaching the ▲ gate meant walking round it.
            var old = place.ReturnPose(child, p);
            var oldRoute = new List<Vector2> { new(MathF.Sign(old.Eye.X == 0 ? 1 : old.Eye.X) * 2.6f, -3.5f), new(MathF.Sign(old.Eye.X == 0 ? 1 : old.Eye.X) * 2.6f, 3.5f), new(0, place.ParentGate!.Position.Z - 3), new(0, place.ParentGate.Position.Z + 3) };
            var before = PlaceRoutes.FollowWaypoints(place, old, oldRoute, place.ParentGate, Before, run: false);
            var straightBefore = PlaceRoutes.WalkStraight(place, old, place.ParentGate, Before, run: false);
            straightBefore.Crossed.Should().BeFalse($"{label}: before, holding W from the return pose did not lead up");
            output.WriteLine($"{label}: arrive {route.StandOff:0.0} m from the child's gate; W alone → ▲ in {S(walk.Time)} walking ({walk.Distance:0} m); before: W alone never got there, steering round the landmark took {S(before.Time)}");
        }
    }

    [Fact]
    public void At_the_top_going_up_faces_into_the_place_and_no_sign_stands_in_the_avenue()
    {
        var (tree, typical, _, _) = Places();
        var top = PlaceComposer.Compose(tree, 0, PlaceParams.Spacious);
        top.ParentGate.Should().BeNull();
        var route = PlaceRoutes.ContinueUp(top, typical, PlaceParams.Default, Spacious8);
        route.Verified.Should().BeFalse("there is no way further up");
        route.Pose.Should().Be(top.ReturnPose(typical, PlaceParams.Default), "at the top (the hub, a sample root) Up arrives in front of the child's gate, facing in");

        foreach (var id in new[] { typical, 0 })
        {
            var place = PlaceComposer.Compose(tree, id, PlaceParams.Spacious);
            foreach (var q in place.Quarters)
            {
                (q.SignPosition.Z <= 0 || MathF.Abs(q.SignPosition.X) >= PlaceParams.Default.CorridorHalfWidth + 1.2f - 1e-3f).Should().BeTrue($"the {q.Category} sign at {q.SignPosition} is out of the avenue to the ▲ gate");
            }
        }
    }

    [Fact]
    public void Five_successive_ups_with_W_held_take_seconds_not_a_minute()
    {
        var (tree, _, _, chain) = Places();
        var p = PlaceParams.Spacious;
        var after = TimeSpan.Zero;
        var before = TimeSpan.Zero;
        for (var i = chain.Length - 1; i > 0; i--)
        {
            var place = PlaceComposer.Compose(tree, chain[i - 1], PlaceParams.Spacious);
            var route = PlaceRoutes.ContinueUp(place, chain[i], p, Spacious8);
            var walk = PlaceRoutes.WalkStraight(place, route.Pose, place.ParentGate!, Spacious8, run: false);
            walk.Crossed.Should().BeTrue();
            after += walk.Time + TransitionAfter;
            var old = place.ReturnPose(chain[i], p);
            var side = old.Eye.X < 0 ? -1f : 1f;
            var oldRoute = new List<Vector2> { new(side * 2.6f, -3.5f), new(side * 2.6f, 3.5f), new(0, place.ParentGate!.Position.Z - 3), new(0, place.ParentGate.Position.Z + 3) };
            var walked = PlaceRoutes.FollowWaypoints(place, old, oldRoute, place.ParentGate, Before, run: false);
            walked.Crossed.Should().BeTrue();
            before += walked.Time + TransitionBefore + TimeSpan.FromSeconds(1.5); // + turning round (≈180° at 120°/s)
        }

        output.WriteLine($"4 successive Ups on foot (walking, W held): before ≈ {S(before)} (with turning round), after {S(after)}");
        after.TotalSeconds.Should().BeLessThan(before.TotalSeconds * 0.5);
    }

    /// <summary>
    /// PLACE-14 / KD-28: the compact layout with its comfortable default speeds (5.5 / 11 m/s) against the spacious layout
    /// at the speeds it needed (8 / 16 m/s): arrival to the nearest and farthest gates, and repeated Up, all on foot.
    /// </summary>
    [Fact]
    public void Compact_places_shorten_journeys_at_comfortable_speeds()
    {
        var (tree, typical, wide, chain) = Places();
        foreach (var (label, id) in new[] { ("typical (8 gates)", typical), ("wide (70 gates, tiers)", wide) })
        {
            var sp = PlaceComposer.Compose(tree, id, PlaceParams.Spacious);
            var cp = PlaceComposer.Compose(tree, id, PlaceParams.Compact);
            foreach (var which in new[] { "nearest", "farthest" })
            {
                Gate Pick(PlaceComposition c) => which == "nearest" ? c.Gates.MinBy(g => Vector3.Distance(g.Position, c.Arrival.Eye))! : c.Gates.MaxBy(g => Vector3.Distance(g.Position, c.Arrival.Eye))!;
                var gs = Pick(sp);
                var gc = Pick(cp);
                var before = PlaceRoutes.FollowWaypoints(sp, sp.Arrival, PlaceRoutes.ArrivalToGate(sp, gs), gs, Spacious8, run: false);
                var after = PlaceRoutes.FollowWaypoints(cp, cp.Arrival, PlaceRoutes.ArrivalToGate(cp, gc), gc, WalkParams.Default, run: false);
                var afterRun = PlaceRoutes.FollowWaypoints(cp, cp.Arrival, PlaceRoutes.ArrivalToGate(cp, gc), gc, WalkParams.Default, run: true);
                output.WriteLine($"{label}, {which} gate: spacious {before.Distance:0} m at 8 m/s {S(before.Time)}; compact {after.Distance:0} m walking {S(after.Time)}, running {S(afterRun.Time)}");
                after.Crossed.Should().BeTrue($"{label} {which}: the compact route reaches the gate");
                afterRun.Crossed.Should().BeTrue();
                after.Distance.Should().BeLessThan(before.Distance * 0.75f, "the walk itself is shorter");
                // Acceptance (PLACE-14): walking at the comfortable 5.5 m/s takes no more than 10 % longer than the spacious
                // layout needed at 8 m/s. Measured: typical gates 5.3–5.4 s against 6.0–6.1 s; the farthest tier of a
                // 70-gate place 13.0 s against 12.3 s (running: 6.8 s).
                after.Time.Should().BeLessThan(before.Time * 1.10, "walking more slowly still gets there about as soon");
            }
        }

        TimeSpan Ups(PlaceParams p, WalkParams w)
        {
            var total = TimeSpan.Zero;
            for (var i = chain.Length - 1; i > 0; i--)
            {
                var place = PlaceComposer.Compose(tree, chain[i - 1], p);
                var route = PlaceRoutes.ContinueUp(place, chain[i], p, w);
                var walk = PlaceRoutes.WalkStraight(place, route.Pose, place.ParentGate!, w, run: false);
                walk.Crossed.Should().BeTrue("straight ahead from each arrival reaches the way up");
                total += walk.Time + TransitionAfter;
            }

            return total;
        }

        var upsBefore = Ups(PlaceParams.Spacious, Spacious8);
        var upsAfter = Ups(PlaceParams.Compact, WalkParams.Default);
        output.WriteLine($"4 successive Ups walking: spacious at 8 m/s {S(upsBefore)}, compact at 5.5 m/s {S(upsAfter)}");
        // PLACE-14: repeated Ups in the compact layout at the slower comfortable speed take at most 5 % longer than the
        // spacious layout needed at 8 m/s.
        upsAfter.Should().BeLessThan(upsBefore * 1.05);
    }
}
