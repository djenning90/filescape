using FileScape.Core.Planets;

namespace FileScape.Core.Tests;

/// <summary>
/// Which pictures load next on a gallery world, at which sharpness, and what is let go (PLANET-131, PLANET-133, AD-86):
/// nearest and visible first, a small picture everywhere before a sharp one near you, never more in flight, shown or
/// sharp than the budget allows, failures never retried; and the session's byte-bounded cache. Written before the code.
/// </summary>
public sealed class ThumbnailPlanTests
{
    private static readonly ThumbBudget B = new() { MaxInFlight = 2, MaxShown = 6, MaxLarge = 2, LargeWithinMetres = 8, LargeReleaseMetres = 12 };

    private static ThumbSlot S(int id, float d, bool inView = true, ThumbTier shown = ThumbTier.None, ThumbTier loading = ThumbTier.None, ThumbTier failed = ThumbTier.None) =>
        new(id, d, inView, shown, loading, failed);

    [Fact]
    public void Visible_pictures_load_first_nearest_first()
    {
        var next = ThumbnailPlan.Next([S(1, 30), S(2, 1, inView: false), S(3, 10), S(4, 2)], B);
        next.Should().Equal((4, ThumbTier.Small), (3, ThumbTier.Small));
    }

    [Fact]
    public void Never_more_in_flight_than_the_budget()
    {
        ThumbnailPlan.Next([S(1, 1, loading: ThumbTier.Small), S(2, 2), S(3, 3)], B).Should().Equal((2, ThumbTier.Small));
        ThumbnailPlan.Next([S(1, 1, loading: ThumbTier.Small), S(2, 2, loading: ThumbTier.Large), S(3, 3)], B).Should().BeEmpty();
    }

    [Fact]
    public void A_near_picture_sharpens_once_it_shows_small_and_at_most_MaxLarge_are_sharp()
    {
        var slots = new[] { S(1, 3, shown: ThumbTier.Small), S(2, 5, shown: ThumbTier.Small), S(3, 6, shown: ThumbTier.Small), S(4, 20, shown: ThumbTier.Small) };
        ThumbnailPlan.Next(slots, B).Should().Equal((1, ThumbTier.Large), (2, ThumbTier.Large));
        slots[0] = S(1, 3, shown: ThumbTier.Large);
        slots[1] = S(2, 5, loading: ThumbTier.Large, shown: ThumbTier.Small);
        ThumbnailPlan.Next(slots, B).Should().BeEmpty("two sharp ones (one loading) is the budget");
        // A visible picture still waiting for its small one comes before a sharper one beside you.
        ThumbnailPlan.Next([S(1, 3, shown: ThumbTier.Small), S(2, 15)], B with { MaxInFlight = 1 }).Should().Equal((2, ThumbTier.Small));
        // Behind you or far away: never sharpened.
        ThumbnailPlan.Next([S(1, 3, inView: false, shown: ThumbTier.Small), S(2, 9, shown: ThumbTier.Small)], B).Should().BeEmpty();
    }

    [Fact]
    public void Failures_are_not_retried_and_a_failed_sharp_one_stays_small()
    {
        ThumbnailPlan.Next([S(1, 1, failed: ThumbTier.Small), S(2, 2, shown: ThumbTier.Small, failed: ThumbTier.Large)], B).Should().BeEmpty();
    }

    [Fact]
    public void Shown_pictures_are_bounded()
    {
        var slots = Enumerable.Range(0, 5).Select(i => S(i, i, shown: ThumbTier.Small)).Append(S(10, 40)).Append(S(11, 41)).ToArray();
        ThumbnailPlan.Next(slots, B with { LargeWithinMetres = -1 }).Should().Equal((10, ThumbTier.Small));
    }

    [Fact]
    public void Sharp_pictures_left_behind_are_released_with_hysteresis()
    {
        ThumbnailPlan.Release([S(1, 10, shown: ThumbTier.Large), S(2, 13, shown: ThumbTier.Large), S(3, 30, shown: ThumbTier.Small)], B).Should().Equal(2);
        ThumbnailPlan.Release([S(1, 2, shown: ThumbTier.Large), S(2, 3, shown: ThumbTier.Large), S(3, 4, shown: ThumbTier.Large)], B).Should().Equal(3);
    }

    [Fact]
    public void Walking_through_three_hundred_pictures_stays_within_every_bound()
    {
        // A line of 300 pictures 2 m apart; the walker moves 0.5 m a tick; a request finishes the tick after it starts.
        var budget = new ThumbBudget();
        var shown = new ThumbTier[300];
        var loading = new ThumbTier[300];
        var requests = 0;
        int peakShown = 0, peakLarge = 0, peakFlight = 0;
        for (var tick = 0; tick < 1400; tick++)
        {
            var at = tick * 0.5f;
            for (var i = 0; i < 300; i++) if (loading[i] != ThumbTier.None) { shown[i] = loading[i]; loading[i] = ThumbTier.None; }
            ThumbSlot Slot(int i) => new(i, MathF.Abs(i * 2f - at), i * 2f >= at - 1, shown[i], loading[i], ThumbTier.None);
            foreach (var r in ThumbnailPlan.Release(Enumerable.Range(0, 300).Select(Slot).ToList(), budget)) shown[r] = ThumbTier.Small;
            // Far pictures give their texture back once the budget is reached (as the app does on another planet).
            foreach (var r in ThumbnailPlan.Evict(Enumerable.Range(0, 300).Select(Slot).ToList(), budget)) shown[r] = ThumbTier.None;
            var next = ThumbnailPlan.Next(Enumerable.Range(0, 300).Select(Slot).ToList(), budget);
            foreach (var (id, tier) in next) { loading[id] = tier; requests++; }
            peakFlight = Math.Max(peakFlight, loading.Count(l => l != ThumbTier.None));
            peakShown = Math.Max(peakShown, Enumerable.Range(0, 300).Count(i => shown[i] != ThumbTier.None || loading[i] != ThumbTier.None));
            peakLarge = Math.Max(peakLarge, Enumerable.Range(0, 300).Count(i => shown[i] == ThumbTier.Large || loading[i] == ThumbTier.Large));
        }

        peakFlight.Should().BeLessThanOrEqualTo(budget.MaxInFlight);
        peakShown.Should().BeLessThanOrEqualTo(budget.MaxShown);
        peakLarge.Should().BeLessThanOrEqualTo(budget.MaxLarge);
        shown.Count(s => s != ThumbTier.None).Should().BeGreaterThan(0);
        requests.Should().BeLessThan(300 * 3, "each picture is asked for a small and at most a few sharp versions, not every tick");
    }

    [Fact]
    public void The_cache_keeps_the_most_recently_used_within_its_bytes()
    {
        var c = new ByteBudgetCache<string, string>(100);
        c.Put("a", "A", 40);
        c.Put("b", "B", 40);
        c.TryGet("a", out _).Should().BeTrue(); // a is now the most recent
        c.Put("c", "C", 40);
        c.TryGet("b", out _).Should().BeFalse("the least recently used went");
        c.TryGet("a", out var a).Should().BeTrue();
        a.Should().Be("A");
        c.Bytes.Should().Be(80);
        c.Put("huge", "H", 101);
        c.TryGet("huge", out _).Should().BeFalse("larger than the whole budget: not kept");
        c.Put("a", "A2", 10);
        c.Bytes.Should().Be(50, "replacing an entry replaces its cost");
        c.Count.Should().Be(2);
    }
}
