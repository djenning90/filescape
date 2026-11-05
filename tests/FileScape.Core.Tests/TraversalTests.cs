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
    internal static readonly WalkParams Before = WalkParams.Default with { WalkSpeed = MetersPerSecond.Of(4.2f), RunSpeed = MetersPerSecond.Of(8.4f) };

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
            var place = PlaceComposer.Compose(tree, id);
            var near = place.Gates.MinBy(g => Vector3.Distance(g.Position, place.Arrival.Eye))!;
            var far = place.Gates.MaxBy(g => Vector3.Distance(g.Position, place.Arrival.Eye))!;
            foreach (var (which, gate) in new[] { ("nearest", near), ("farthest", far) })
            {
                var route = PlaceRoutes.ArrivalToGate(place, gate);
                var walkBefore = PlaceRoutes.FollowWaypoints(place, place.Arrival, route, gate, Before, run: false);
                var runBefore = PlaceRoutes.FollowWaypoints(place, place.Arrival, route, gate, Before, run: true);
                var walkAfter = PlaceRoutes.FollowWaypoints(place, place.Arrival, route, gate, WalkParams.Default, run: false);
                var runAfter = PlaceRoutes.FollowWaypoints(place, place.Arrival, route, gate, WalkParams.Default, run: true);
                output.WriteLine($"{label}, {which} gate ({walkAfter.Distance:0} m): walk {S(walkBefore.Time)} → {S(walkAfter.Time)}, run {S(runBefore.Time)} → {S(runAfter.Time)}");
                foreach (var r in new[] { walkBefore, runBefore, walkAfter, runAfter }) r.Crossed.Should().BeTrue($"{label} {which}: the route reaches the gate");
                walkAfter.Time.TotalSeconds.Should().BeLessThan(walkBefore.Time.TotalSeconds * 0.6);
                runAfter.Time.TotalSeconds.Should().BeLessThan(runBefore.Time.TotalSeconds * 0.6);
            }
        }
    }

    [Fact]
    public void Going_up_arrives_facing_the_next_way_up_and_straight_ahead_reaches_it()
    {
        var (tree, _, wide, chain) = Places();
        var p = PlaceParams.Default;
        // From each level up to its parent, and from the wide folder's gates (near, middle, far tiers) to the top.
        var cases = new List<(int Parent, int Child, string Label)>();
        for (var i = chain.Length - 1; i > 0; i--) cases.Add((chain[i - 1], chain[i], $"{tree[chain[i]].Name} → {tree[chain[i - 1]].Name}"));
        var widePlace = PlaceComposer.Compose(tree, wide);
        foreach (var g in new[] { widePlace.Gates[0], widePlace.Gates[widePlace.Gates.Count / 2], widePlace.Gates[^1] })
        {
            cases.Add((wide, g.TargetId, $"{tree[g.TargetId].Name} → wide (tier {g.Tier + 1})"));
        }

        foreach (var (parent, child, label) in cases)
        {
            var place = PlaceComposer.Compose(tree, parent);
            var route = PlaceRoutes.ContinueUp(place, child, p, WalkParams.Default);
            route.Verified.Should().BeTrue($"{label}: straight ahead must reach the ▲ gate");
            var walk = PlaceRoutes.WalkStraight(place, route.Pose, place.ParentGate!, WalkParams.Default, run: false);
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
        var top = PlaceComposer.Compose(tree, 0);
        top.ParentGate.Should().BeNull();
        var route = PlaceRoutes.ContinueUp(top, typical, PlaceParams.Default, WalkParams.Default);
        route.Verified.Should().BeFalse("there is no way further up");
        route.Pose.Should().Be(top.ReturnPose(typical, PlaceParams.Default), "at the top (the hub, a sample root) Up arrives in front of the child's gate, facing in");

        foreach (var id in new[] { typical, 0 })
        {
            var place = PlaceComposer.Compose(tree, id);
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
        var p = PlaceParams.Default;
        var after = TimeSpan.Zero;
        var before = TimeSpan.Zero;
        for (var i = chain.Length - 1; i > 0; i--)
        {
            var place = PlaceComposer.Compose(tree, chain[i - 1]);
            var route = PlaceRoutes.ContinueUp(place, chain[i], p, WalkParams.Default);
            var walk = PlaceRoutes.WalkStraight(place, route.Pose, place.ParentGate!, WalkParams.Default, run: false);
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
}
