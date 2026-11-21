using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using FileScape.Core.Scanning;

namespace FileScape.Core.Tests;

/// <summary>
/// The seventh pass's stall fix (PLANET-174..177, AD-96): background discovery changes the world you stand on, and only
/// the worlds whose listing changed are composed again (<see cref="Neighbourhood.Stale"/>), off the main thread
/// (<see cref="BackgroundComposer"/>, under the tree's read lease), kept for the way back (<see cref="CompositionCache"/>),
/// and built into the scene in frame-budgeted slices (<see cref="FrameSliceQueue"/>). Fast and deterministic.
/// </summary>
public class NeighbourhoodSchedulingTests
{
    /// <summary>A root shaped like the Ubuntu root: 21 child folders, each listed with three files.</summary>
    private const int RootLikeChildren = 21;

    private static FsTree RootLike(out int[] children)
    {
        var tree = new FsTree(new DataSourceInfo(DataOrigin.LiveFilesystem, "/", "test"));
        var b = new ScanBatch();
        var id = 0;
        b.NewNodes.Add(Dir(id++, -1, "/"));
        children = new int[RootLikeChildren];
        for (var c = 0; c < RootLikeChildren; c++)
        {
            var dir = id++;
            children[c] = dir;
            b.NewNodes.Add(Dir(dir, 0, $"d{c:00}"));
            for (var f = 0; f < 3; f++) b.NewNodes.Add(File(id++, dir, $"f{f}.txt"));
        }

        tree.Apply(b);
        return tree;
    }

    private static NodeRecord Dir(int id, int parent, string name, DirScanState state = DirScanState.Complete) =>
        new(id, parent, name, NodeKind.Directory, 0, 0, 0, null, null, null, 0, state, null, null, LocationKind.None);

    private static NodeRecord File(int id, int parent, string name) =>
        new(id, parent, name, NodeKind.File, 0, 0, 1_000, null, null, null, 0, DirScanState.NotApplicable, null, null, LocationKind.None);

    /// <summary>What background discovery does: one more entry listed in one child folder.</summary>
    private static void DiscoverIn(FsTree tree, int folder, bool asFolder = false)
    {
        var b = new ScanBatch();
        b.NewNodes.Add(asFolder ? Dir(tree.Count, folder, "found", DirScanState.Pending) : File(tree.Count, folder, "found.txt"));
        tree.Apply(b);
    }

    /// <summary>What the app shows: the world you stand on (<paramref name="current"/>), then its children in the sky.</summary>
    private static List<ShownWorld> Shown(FsTree tree, int current, IEnumerable<int> sky) =>
        sky.Select(id => ShownWorld.Sky(PlanetComposer.Compose(tree, id))).Prepend(ShownWorld.Standing(PlanetComposer.Compose(tree, current))).ToList();

    [Fact]
    public void A_discovery_update_in_one_child_recomposes_that_child_and_the_world_you_stand_on_not_its_siblings()
    {
        var tree = RootLike(out var children);
        var shownFull = children.ToDictionary(c => c, c => PlanetComposer.Signature(tree, c));
        var shown = Shown(tree, 0, children); // the current world first, then its children
        DiscoverIn(tree, children[5]);
        Neighbourhood.Stale(tree, shown).Should().Equal([0, children[5]],
            "only the root (its child's file count changed) and the child that grew differ; 20 siblings keep their worlds");
        // Before the fix every child counted as changed: a child's full signature mixes its parent's subtree count.
        children.Where(c => c != children[5]).Should().OnlyContain(c => PlanetComposer.Signature(tree, c) != shownFull[c],
            "the full signature does change (the parent's counts), which is why the sky is judged by the preview signature");
    }

    [Fact]
    public void A_sibling_whose_preview_signature_is_unchanged_looks_the_same_from_outside()
    {
        var tree = RootLike(out var children);
        var before = PlanetComposer.Compose(tree, children[1]);
        DiscoverIn(tree, children[5]);
        DiscoverIn(tree, children[5], asFolder: true);
        var after = PlanetComposer.Compose(tree, children[1]);
        after.PreviewSignature.Should().Be(before.PreviewSignature);
        after.Signature.Should().NotBe(before.Signature);
        // What a preview draws (PlanetVisual with full: false; its ground from PlanetArt.PlanetMeshArrays) is the same.
        after.Shape.Radius.Should().Be(before.Shape.Radius);
        after.Shape.Seed.Should().Be(before.Shape.Seed);
        after.Shape.HillAmplitude.Should().Be(before.Shape.HillAmplitude);
        after.Shape.Relief.Should().Be(before.Shape.Relief);
        after.Shape.FlatSpots.Should().Equal(before.Shape.FlatSpots);
        after.Theme.Should().Be(before.Theme);
        after.Landmark.Should().BeEquivalentTo(before.Landmark);
        (after.Surface.Shell == null).Should().Be(before.Surface.Shell == null);
    }

    [Fact]
    public void A_folder_found_deeper_down_recomposes_only_its_ancestors_on_show()
    {
        var tree = RootLike(out var children);
        DiscoverIn(tree, children[2], asFolder: true);
        var shown = Shown(tree, 0, children);
        DiscoverIn(tree, tree.Count - 1); // a file inside the grandchild: the child's subtree grew, the root's too
        Neighbourhood.Stale(tree, shown).Should().Equal([0, children[2]]);
    }

    [Fact]
    public void Nothing_changed_recomposes_nothing_and_unknown_ids_are_skipped()
    {
        var tree = RootLike(out var children);
        var shown = Shown(tree, 0, children).Append(new ShownWorld(9_999, "stale", InSky: true)).ToList();
        Neighbourhood.Stale(tree, shown).Should().BeEmpty();
    }

    [Fact]
    public void The_background_composer_holds_the_tree_still_until_its_worker_has_finished_reading()
    {
        var tree = RootLike(out var children);
        using var gate = new ManualResetEventSlim(false);
        var composer = new BackgroundComposer();
        composer.TryStart(tree, [children[0], children[1]], id => { gate.Wait(); return PlanetComposer.Compose(tree, id); }).Should().BeTrue();
        composer.Busy.Should().BeTrue();
        tree.BackgroundReaders.Should().Be(1, "the lease is taken before the worker starts");
        composer.TryStart(tree, [children[2]], id => PlanetComposer.Compose(tree, id)).Should().BeFalse("one job at a time");
        var apply = () => DiscoverIn(tree, children[3]);
        apply.Should().Throw<InvalidOperationException>("the main thread must defer batches while a worker reads the tree");
        composer.TryCollect(out _).Should().BeFalse("still composing");

        gate.Set();
        composer.WaitForJob(TimeSpan.FromSeconds(30)).Should().BeTrue();
        composer.TryCollect(out var job).Should().BeTrue();
        tree.BackgroundReaders.Should().Be(0, "the lease ends before the results can be collected");
        job.Compositions.Select(c => c.FolderId).Should().Equal(children[0], children[1]);
        job.Compositions[0].Signature.Should().Be(PlanetComposer.Compose(tree, children[0]).Signature, "the same pure composition as on the main thread");
        composer.Busy.Should().BeFalse();
        apply.Should().NotThrow();
    }

    [Fact]
    public void A_compose_that_throws_is_counted_and_left_out_and_the_lease_still_ends()
    {
        var tree = RootLike(out var children);
        var composer = new BackgroundComposer();
        composer.TryStart(tree, [children[0], children[1]], id => id == children[0] ? throw new InvalidOperationException("boom") : PlanetComposer.Compose(tree, id));
        composer.WaitForJob(TimeSpan.FromSeconds(30)).Should().BeTrue();
        composer.TryCollect(out var job).Should().BeTrue();
        job.Compositions.Select(c => c.FolderId).Should().Equal(children[1]);
        composer.Failures.Should().Be(1);
        tree.BackgroundReaders.Should().Be(0);
    }

    [Fact]
    public void The_composition_cache_answers_exactly_or_with_the_latest_and_drops_the_least_recently_used()
    {
        var tree = RootLike(out var children);
        var cache = new CompositionCache(capacity: 2);
        var a = PlanetComposer.Compose(tree, children[0]);
        var b = PlanetComposer.Compose(tree, children[1]);
        cache.Put(a);
        cache.Put(b);
        cache.TryGetExact(children[0], a.Signature, "", out var hit).Should().BeTrue();
        hit.Should().BeSameAs(a);
        cache.TryGetExact(children[0], a.Signature, "WslRoot", out _).Should().BeFalse("another variant is another composition");

        DiscoverIn(tree, children[0]);
        var now = PlanetComposer.Signature(tree, children[0]);
        cache.TryGetExact(children[0], now, "", out _).Should().BeFalse("made from an older listing");
        cache.Latest(children[0], "").Should().BeSameAs(a, "a landing may show it at once and ask for a fresh one");

        cache.Put(PlanetComposer.Compose(tree, children[2])); // children[1] is the least recently used
        cache.Count.Should().Be(2);
        cache.Latest(children[1], "").Should().BeNull();
        cache.Latest(children[0], "").Should().NotBeNull();
    }

    /// <summary>A clock a test advances by hand: each step "takes" the time it adds.</summary>
    private sealed class FakeClock
    {
        public TimeSpan Now;
        public TimeSpan Read() => Now;
    }

    [Fact]
    public void The_slice_queue_runs_items_in_order_until_the_frame_budget_is_spent_and_at_least_one()
    {
        var clock = new FakeClock();
        var q = new FrameSliceQueue();
        var ran = new List<string>();
        foreach (var (key, ms) in new[] { ("a", 3.0), ("b", 3.0), ("c", 3.0), ("d", 20.0), ("e", 1.0) })
        {
            q.Enqueue(key, () => { ran.Add(key); clock.Now += TimeSpan.FromMilliseconds(ms); return SliceResult.Done; });
        }

        var budget = TimeSpan.FromMilliseconds(5);
        q.Run(budget, clock.Read).Should().Be(2, "a (3 ms) then b starts at 3 ms < 5 ms; c would start at 6 ms");
        q.Run(budget, clock.Read).Should().Be(2, "c, then d starts at 3 ms and runs to its end (items are never interrupted)");
        q.Run(budget, clock.Read).Should().Be(1);
        ran.Should().Equal("a", "b", "c", "d", "e");
        q.Count.Should().Be(0);
        q.Run(budget, clock.Read).Should().Be(0);

        // An item longer than the whole budget still runs: the queue always drains.
        q.Enqueue("long", () => { clock.Now += TimeSpan.FromMilliseconds(50); return SliceResult.Done; });
        q.Enqueue("next", () => SliceResult.Done);
        q.Run(budget, clock.Read).Should().Be(1);
        q.Keys.Should().Equal("next");
    }

    [Fact]
    public void A_waiting_item_keeps_its_place_costs_nothing_and_a_newer_one_with_its_key_replaces_it()
    {
        var clock = new FakeClock();
        var q = new FrameSliceQueue();
        var ready = false;
        var ran = new List<string>();
        q.Enqueue("swap:5", () => ready ? Done("old 5") : SliceResult.NotReady);
        q.Enqueue("preview:7", () => Done("7"));
        q.Run(TimeSpan.FromMilliseconds(5), clock.Read).Should().Be(1, "5 is not ready; 7 runs");
        q.Keys.Should().Equal("swap:5");

        q.Enqueue("swap:5", () => Done("new 5")); // recomposed again before the first swap ran
        q.Count.Should().Be(1);
        q.Run(TimeSpan.FromMilliseconds(5), clock.Read);
        ran.Should().Equal("7", "new 5");
        q.Completed.Should().Be(2);

        SliceResult Done(string what)
        {
            ran.Add(what);
            return SliceResult.Done;
        }
    }

    [Fact]
    public void Work_queued_by_an_item_waits_for_the_next_frame()
    {
        var clock = new FakeClock();
        var q = new FrameSliceQueue();
        var runs = 0;
        q.Enqueue("a", () =>
        {
            q.Enqueue("b", () => { runs++; return SliceResult.Done; });
            return SliceResult.Done;
        });
        q.Run(TimeSpan.FromSeconds(1), clock.Read).Should().Be(1);
        runs.Should().Be(0);
        q.Run(TimeSpan.FromSeconds(1), clock.Read).Should().Be(1);
        runs.Should().Be(1);
    }
}

/// <summary>The automatic 3D render scale for very large windows (PLANET-180, AD-96).</summary>
public class NeighbourhoodSchedulingRenderScaleTests
{
    [Theory]
    [InlineData(1600, 900, 1f)]
    [InlineData(1920, 1080, 1f)]
    [InlineData(2560, 1440, 1f)]
    [InlineData(3840, 2160, 0.6667f)]
    [InlineData(7680, 4320, 0.6667f)]
    public void Windows_up_to_1440p_draw_at_full_scale_and_larger_ones_come_back_to_that_budget(int w, int h, float expected) =>
        RenderScalePolicy.ScaleFor(w, h).Should().BeApproximately(expected, 0.001f);

    [Fact]
    public void The_scale_falls_steadily_with_the_window_and_an_explicit_one_wins()
    {
        RenderScalePolicy.ScaleFor(3440, 1440).Should().BeApproximately(MathF.Sqrt(2560f / 3440f), 1e-4f);
        RenderScalePolicy.ScaleFor(3000, 1700).Should().BeGreaterThan(RenderScalePolicy.ScaleFor(3840, 2160) - 1e-6f);
        RenderScalePolicy.ScaleFor(3840, 2160, requested: 1f).Should().Be(1f);
        RenderScalePolicy.ScaleFor(1600, 900, requested: 0.1f).Should().Be(0.25f);
    }
}
