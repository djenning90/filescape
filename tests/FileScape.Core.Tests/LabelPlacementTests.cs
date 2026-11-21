using System.Numerics;
using FileScape.Core.Interaction;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-193 (AD-98): the overview's names placed round their worlds through <see cref="LabelPlacement.Place"/> (what the
/// app's sign layer calls each frame in the overview): near their bodies on short leaders, never overlapping, no leader
/// crossing another or running under a name, the selection, the hovered world and "you are here" first, a bounded count;
/// on dense synthetic layouts like the 2026-11-21 screenshots (about ten names among hundreds of worlds in a cluster).
/// </summary>
public sealed class LabelPlacementTests
{
    private static readonly LabelRect Window = new(0, 0, 1600, 900);
    private static readonly LabelRect[] Hud = [new(0, 0, 1600, 60), new(500, 760, 600, 140)]; // a top bar and the prompt

    /// <summary>A cluster of <paramref name="bodies"/> worlds round the middle (seeded), the first <paramref name="named"/> asking for names.</summary>
    private static List<LabelRequest> Cluster(int seed, int bodies, int named, float spread = 260f)
    {
        var rng = new Random(seed);
        var list = new List<LabelRequest>();
        for (var i = 0; i < bodies; i++)
        {
            var a = rng.NextDouble() * Math.PI * 2;
            var d = Math.Sqrt(rng.NextDouble()) * spread;
            var at = new Vector2(800 + (float)(Math.Cos(a) * d), 430 + (float)(Math.Sin(a) * d * 0.6));
            if (i >= named) continue;
            var tier = i switch { 0 => 0, 1 => 1, 2 => 2, _ => 5 };
            list.Add(new LabelRequest(i, at, 6f + (float)rng.NextDouble() * 4f, new Vector2(80 + rng.Next(70), i < 3 ? 34 : 20), tier, i));
        }

        return list;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void On_a_dense_cluster_no_names_overlap_and_no_leader_crosses_another_or_runs_under_a_name(int seed)
    {
        var requests = Cluster(seed, 400, 18);
        var placed = LabelPlacement.Place(requests, Window, Hud);
        placed.Count.Should().BeInRange(6, new LabelPlacement.Options().MaxLabels, "about ten names fit, and never more than the bound");
        LabelPlacement.Faults(placed).Should().Be((0, 0, 0));
        foreach (var p in placed)
        {
            Window.Encloses(p.Rect).Should().BeTrue();
            Hud.Should().NotContain(h => h.Intersects(p.Rect), "names stay off the HUD");
            var r = requests.Single(q => q.Id == p.Id);
            Vector2.Distance(p.LeaderFrom, r.Anchor).Should().BeApproximately(r.AnchorRadius, 0.01f, "the leader starts on the world's rim, where its dot is");
            Vector2.Distance(p.LeaderFrom, p.LeaderTo).Should().BeLessThanOrEqualTo(LabelPlacement.GapPixels + (LabelPlacement.Rings - 1) * LabelPlacement.RingStepPixels + 1f, "short leaders: a name stays near its world");
            requests.Where(q => q.Id != p.Id).Should().NotContain(q => p.Rect.TouchesDisc(q.Anchor, q.AnchorRadius), "a name covers no other named world");
        }
    }

    [Fact]
    public void The_selection_the_hovered_world_and_you_are_here_come_first()
    {
        var requests = Cluster(7, 300, 30, spread: 160f);
        var placed = LabelPlacement.Place(requests, Window, Hud);
        placed.Take(3).Select(p => p.Tier).Should().Equal([0, 1, 2], "placed in priority order");
        placed.Select(p => p.Id).Should().Contain([0, 1, 2]);
        placed.Count.Should().Be(new LabelPlacement.Options().MaxLabels, "30 asked for in a tight cluster: the bound holds");
        placed.Select(p => p.Tier).Should().BeInAscendingOrder();
    }

    [Fact]
    public void A_lone_world_gets_its_name_right_beside_it_above_right()
    {
        var r = new LabelRequest(1, new Vector2(800, 450), 8f, new Vector2(100, 20), 0, 0);
        var p = LabelPlacement.Place([r], Window, []).Single();
        p.Rect.X.Should().BeGreaterThan(800, "above-right first");
        p.Rect.Bottom.Should().BeLessThan(450);
        Vector2.Distance(p.LeaderFrom, p.LeaderTo).Should().BeApproximately(LabelPlacement.GapPixels, 0.01f, "on the first ring: a short leader");
    }

    [Fact]
    public void Near_an_edge_or_a_panel_it_takes_another_side()
    {
        var corner = new LabelRequest(1, new Vector2(1590, 80), 6f, new Vector2(120, 20), 0, 0);
        var p = LabelPlacement.Place([corner], Window, Hud).Single();
        Window.Encloses(p.Rect).Should().BeTrue();
        p.Rect.Right.Should().BeLessThanOrEqualTo(1590, "the right of the window is too near: it goes left");
        p.Rect.Y.Should().BeGreaterThanOrEqualTo(60, "and clear of the top bar");
    }

    [Fact]
    public void A_name_keeps_last_frames_side_of_its_world_while_the_view_moves()
    {
        var first = LabelPlacement.Place(Cluster(3, 300, 14), Window, Hud);
        // The camera turns a little: every world moves 4 px to the right; each name asks for its last slot.
        var slots = first.ToDictionary(p => p.Id, p => p.Slot);
        var moved = Cluster(3, 300, 14).Select(r => r with { Anchor = r.Anchor + new Vector2(4, 0), Preferred = slots.GetValueOrDefault(r.Id, -1) }).ToList();
        var second = LabelPlacement.Place(moved, Window, Hud);
        var kept = second.Count(p => slots.TryGetValue(p.Id, out var s) && s == p.Slot);
        kept.Should().BeGreaterThanOrEqualTo(first.Count - 1, "names do not hop to another side when nothing forces them");
        LabelPlacement.Faults(second).Should().Be((0, 0, 0), "and the kept slots are still checked");
    }

    [Fact]
    public void The_same_input_gives_the_same_layout()
    {
        var a = LabelPlacement.Place(Cluster(11, 300, 16), Window, Hud);
        var b = LabelPlacement.Place(Cluster(11, 300, 16), Window, Hud);
        a.Should().Equal(b);
    }

    [Fact]
    public void The_fault_check_sees_overlaps_and_crossings()
    {
        var x = new PlacedLabel(1, new LabelRect(100, 100, 50, 20), new Vector2(90, 150), new Vector2(160, 90), 0);
        var y = new PlacedLabel(2, new LabelRect(120, 110, 50, 20), new Vector2(90, 90), new Vector2(160, 150), 0);
        var (overlaps, crossings, under) = LabelPlacement.Faults([x, y]);
        overlaps.Should().Be(1);
        crossings.Should().Be(1);
        under.Should().BeGreaterThan(0);
        LabelPlacement.SegmentsCross(new(0, 0), new(10, 0), new(10, 0), new(20, 5)).Should().BeFalse("a shared end is not a crossing");
    }
}
