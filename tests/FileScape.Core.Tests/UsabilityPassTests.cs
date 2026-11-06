using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Scanning;
using FileScape.Core.Units;

namespace FileScape.Core.Tests;

/// <summary>
/// DJ's hands-on feedback pass (2026-11-05 evening): look direction preferences (CTRL-01..03), folder summaries
/// (SUM-01..04) and natural name order (ORD-01).
/// </summary>
public class UsabilityPassTests
{
    // ------------------------------------------------------------------ CTRL: look direction and speed preferences

    [Fact]
    public void Look_direction_reverses_each_axis_independently_and_nothing_else()
    {
        var right = new Vector2(100, 0);
        var down = new Vector2(0, 100);
        var usual = Preferences.Default with { ReverseLookHorizontal = false, ReverseLookVertical = false };
        // Usual first-person direction (the behaviour before this pass): mouse right turns right (yaw decreases,
        // positive yaw = left); mouse down looks down.
        usual.LookDelta(right, 0.12f).Yaw.Value.Should().BeApproximately(-12f, 1e-4f);
        usual.LookDelta(down, 0.12f).LookPitch.Value.Should().BeApproximately(-12f, 1e-4f);
        usual.LookDelta(right, 0.12f).LookPitch.Should().Be(Degrees.Zero);

        var h = usual with { ReverseLookHorizontal = true };
        h.LookDelta(right, 0.12f).Yaw.Value.Should().BeApproximately(12f, 1e-4f, "reversed: mouse right turns left");
        h.LookDelta(down, 0.12f).LookPitch.Value.Should().BeApproximately(-12f, 1e-4f, "vertical untouched");

        var v = usual with { ReverseLookVertical = true };
        v.LookDelta(down, 0.12f).LookPitch.Value.Should().BeApproximately(12f, 1e-4f, "reversed: mouse down looks up");
        v.LookDelta(right, 0.12f).Yaw.Value.Should().BeApproximately(-12f, 1e-4f, "horizontal untouched");

        // This iteration's default: both reversed relative to the previous Places behaviour (DJ's request).
        Preferences.Default.ReverseLookHorizontal.Should().BeTrue();
        Preferences.Default.ReverseLookVertical.Should().BeTrue();
        Preferences.Default.LookDelta(right, 0.12f).Yaw.Value.Should().BeApproximately(12f, 1e-4f);
        Preferences.Default.LookDelta(down, 0.12f).LookPitch.Value.Should().BeApproximately(12f, 1e-4f);
    }

    [Fact]
    public void Preferences_round_trip_through_json_and_bad_values_are_clamped_not_fatal()
    {
        var p = new Preferences { ReverseLookHorizontal = false, ReverseLookVertical = true, WalkSpeed = MetersPerSecond.Of(6.5f), RunSpeed = MetersPerSecond.Of(12f) };
        var back = Preferences.FromJson(p.ToJson(), out var problem);
        problem.Should().BeNull();
        back.Should().Be(p);

        Preferences.FromJson("{\"walkSpeedMetersPerSecond\": 500, \"runSpeedMetersPerSecond\": 1}", out _).Should().Match<Preferences>(x =>
            x.WalkSpeed == Preferences.MaxSpeed && x.RunSpeed == Preferences.MaxSpeed, "too fast is clamped and running is never slower than walking");
        Preferences.FromJson("{\"reverseLookHorizontal\": false}", out _).Should().Be(Preferences.Default with { ReverseLookHorizontal = false }, "missing fields keep their defaults");
        Preferences.FromJson("not json", out var bad).Should().Be(Preferences.Default);
        bad.Should().NotBeNull();

        var dir = Path.Combine(Path.GetTempPath(), "FileScape-tests", "prefs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "preferences.json");
            Preferences.Load(path, out var missing).Should().Be(Preferences.Default);
            missing.Should().BeNull("a missing file is simply the defaults");
            p.Save(path);
            Preferences.Load(path, out _).Should().Be(p);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Default_speeds_are_twice_as_fast_and_the_fastest_allowed_cannot_pass_a_pillar()
    {
        // Changed 2026-11-06 (KD-28, PLACE-14): 8 / 16 m/s became 5.5 / 11 m/s with the compact places, whose nearest
        // gate is ≈ 18 m away instead of ≈ 45 m. The old pair is kept as the comparison value; running stays twice walking.
        WalkParams.Default.WalkSpeed.Should().Be(MetersPerSecond.Of(5.5f));
        WalkParams.Default.RunSpeed.Should().Be(MetersPerSecond.Of(11f));
        WalkParams.PreviousDefaultSpeeds.Should().Be((8f, 16f));
        // One 120 Hz sub-step at the fastest allowed speed moves less than the body radius.
        (Preferences.MaxSpeed.Value / WalkParams.Default.SubStepsPerSecond).Should().BeLessThan(WalkParams.Default.Radius.Value);

        // Running at the maximum straight at a gate pillar: the walker stops at it, never inside or beyond it.
        var tree = new Spec("p").Dir(new Spec("one").File("a.txt")).Build();
        var place = PlaceComposer.Compose(tree, 0);
        var gate = place.Gates[0];
        var pillar = place.Terrain.Boxes.First(b => b.Kind == TerrainKind.Pillar && b.NodeId == gate.TargetId);
        var start = new Vector3(pillar.Center.X, 0, pillar.Center.Y) - gate.Outward * 12f;
        var w = new Walker(WalkParams.Default with { WalkSpeed = Preferences.MaxSpeed, RunSpeed = Preferences.MaxSpeed });
        w.Teleport(place.Terrain, start, PlaceGeometry.YawFor(gate.AngleDeg), Degrees.Zero);
        for (var i = 0; i < 120; i++)
        {
            w.Update(place.Terrain, new WalkInput(1, 0, 0, true, false), Durations.FromSeconds(1 / 60.0));
            pillar.Overlaps(new Vector2(w.Feet.X, w.Feet.Z), w.Params.Radius.Value - 0.01f).Should().BeFalse("never inside the pillar");
        }

        ThresholdTracker.Depth(gate, w.Feet).Should().BeLessThan(0, "stopped in front of the pillar, not through it");
    }

    [Fact]
    public void Space_jumps_and_jumping_does_not_make_the_walker_faster()
    {
        // DJ described Space as accelerating. Measured: horizontal distance over 4 s on the flat, with and without
        // jumping every time the walker lands, walking and running.
        foreach (var run in new[] { false, true })
        {
            float Distance(bool jump)
            {
                var w = new Walker();
                w.Teleport(PlaceTerrain.Flat, Vector3.Zero, Degrees.Zero, Degrees.Zero);
                for (var i = 0; i < 240; i++) w.Update(PlaceTerrain.Flat, new WalkInput(1, 0, 0, run, jump && w.Grounded), Durations.FromSeconds(1 / 60.0));
                if (jump) w.Jumps.Should().BeGreaterThan(3);
                return new Vector2(w.Feet.X, w.Feet.Z).Length();
            }

            var flat = Distance(false);
            var hopping = Distance(true);
            hopping.Should().BeLessThanOrEqualTo(flat * 1.001f, $"run={run}: jumping never adds speed ({hopping:0.0} m vs {flat:0.0} m)");
        }
    }

    // ------------------------------------------------------------------ SUM: folder summaries

    private static FsTree Mixed() =>
        new Spec("root")
            .Dir(new Spec("only-folders").Dir(new Spec("a").File("x.bin", 1000)).Dir(new Spec("b").File("y.bin", 2000)))
            .Dir(new Spec("empty"))
            .Dir(new Spec("partial").Dir(new Spec("listed").File("z.txt", 500)).Dir(new Spec("not-yet") { State = DirScanState.Unlisted }))
            .Dir(new Spec("unknown") { State = DirScanState.Unlisted })
            .Dir(new Spec("with-hidden").Dir(new Spec(".cache")).Dir(new Spec("secret") { Flags = NodeFlags.Hidden }).File("v.txt", 10))
            .Dir(new Spec("with-link").Dir(new Spec("shortcut") { IsLink = true }).File("w.txt", 20))
            .Dir(new Spec("drive-gone").Dir(new Spec("unavailable") { State = DirScanState.Failed, Flags = NodeFlags.Unavailable }).File("q.txt", 30))
            .Build();

    [Fact]
    public void Summaries_count_folders_and_files_and_say_whether_totals_are_complete()
    {
        var t = Mixed();
        FsNode F(string n) => t.Find(n);

        FolderSummary.Gate(F("only-folders"), Listing.Of(F("only-folders"))).Should().Be("2 folders · 0 files · 2.93 KiB in all");
        FolderSummary.Place(F("only-folders"), ListingState.Open).Should().Be("Here: 2 folders · 0 files\nIn all: 2 folders · 2 files · 2.93 KiB");
        FolderSummary.Gate(F("empty"), Listing.Of(F("empty"))).Should().Be("empty");
        FolderSummary.Place(F("empty"), ListingState.Empty).Should().Be("Here: empty");

        // Partial: one child is not listed yet, so the total is a lower bound and says so.
        F("partial").SubtreeUnsettledDirs.Should().Be(1);
        FolderSummary.TotalsComplete(F("partial")).Should().BeFalse();
        FolderSummary.Gate(F("partial"), Listing.Of(F("partial"))).Should().Be("2 folders · 0 files · ≥ 500 B so far", "the counts are what is here; the size is the subtree, a lower bound");
        FolderSummary.Place(F("partial"), ListingState.Open).Should().Contain("Found so far: 2 folders · 1 file · ≥ 500 B (1 folder not fully listed yet)");

        // Not listed at all: no zeros, the state instead.
        FolderSummary.Here(F("unknown")).Should().BeNull();
        FolderSummary.Gate(F("unknown"), Listing.Of(F("unknown"))).Should().Be("not listed yet");
        FolderSummary.Place(F("unknown"), ListingState.Unlisted).Should().Be("Here: not listed yet");

        // Hidden entries are counted (and said so); links are counted as links, never followed into the totals.
        F("with-hidden").DirectHiddenCount.Should().Be(1);
        FolderSummary.Place(F("with-hidden"), ListingState.Open).Should().StartWith("Here: 2 folders · 1 file (incl. 1 hidden)");
        FolderSummary.Gate(F("with-link"), Listing.Of(F("with-link"))).Should().Be("0 folders · 1 file · 1 link · 20 B in all");
        F("root").SubtreeLinkCount.Should().Be(1);

        // An unavailable child makes the parent's total partial.
        FolderSummary.Gate(F("drive-gone"), Listing.Of(F("drive-gone"))).Should().Be("1 folder · 1 file · ≥ 30 B so far");

        // A folder with folders below but no sizes known yet never shows "0 B".
        var deferred = new Spec("top").Dir(new Spec("d").Dir(new Spec("x") { State = DirScanState.Unlisted })).Build();
        FolderSummary.Gate(deferred.Find("d"), Listing.Of(deferred.Find("d"))).Should().Be("1 folder · 0 files · size not known yet");
        FolderSummary.Inspector(deferred.Find("d")).Should().Contain(r => r.Label == "Found so far" && r.Value.Contains("1 folder not fully listed yet"));
    }

    [Fact]
    public void Completeness_is_maintained_incrementally_as_folders_are_listed()
    {
        var t = new Spec("top").Dir(new Spec("a") { State = DirScanState.Unlisted }).Dir(new Spec("b") { State = DirScanState.Pending }).Build();
        var top = t.Root;
        top.SubtreeUnsettledDirs.Should().Be(2);
        var a = t.Find("a").Id;
        var batch = new ScanBatch();
        batch.NewNodes.Add(new NodeRecord(t.Count, a, "inside.txt", NodeKind.File, 0, 0, 4096, null, null, null, 2, DirScanState.NotApplicable));
        batch.DirectoryUpdates.Add(new DirectoryUpdate(a, DirScanState.Complete));
        t.Apply(batch);
        top.SubtreeUnsettledDirs.Should().Be(1, "a is listed now");
        var b = t.Find("b").Id;
        var batch2 = new ScanBatch();
        batch2.DirectoryUpdates.Add(new DirectoryUpdate(b, DirScanState.Complete, NodeFlags.AccessDenied));
        t.Apply(batch2);
        top.SubtreeUnsettledDirs.Should().Be(1, "denied is not complete");
        var batch3 = new ScanBatch();
        batch3.DirectoryUpdates.Add(new DirectoryUpdate(b, DirScanState.Complete, ClearFlags: NodeFlags.AccessDenied));
        t.Apply(batch3);
        top.SubtreeUnsettledDirs.Should().Be(0);
        FolderSummary.TotalsComplete(top).Should().BeTrue();
        FolderSummary.Gate(t.Find("a"), ListingState.Open).Should().Be("0 folders · 1 file · 4.00 KiB in all");
    }

    // ------------------------------------------------------------------ ORD: natural name order

    [Fact]
    public void Names_order_naturally_and_case_variants_stay_distinct()
    {
        string[] names = ["Folder 10", "folder 2", "Folder 1", "v10", "v9", "007", "7", "README", "readme", "Readme", "alpha", "Alpha-2", "Alpha-10", "b", "a10b", "a2b"];
        var sorted = names.OrderBy(n => n, NaturalNameOrder.Instance).ToArray();
        sorted.Should().Equal("7", "007", "a2b", "a10b", "alpha", "Alpha-2", "Alpha-10", "b", "Folder 1", "folder 2", "Folder 10", "README", "Readme", "readme", "v9", "v10");
        NaturalNameOrder.Instance.Compare("README", "readme").Should().BeLessThan(0, "case variants keep a fixed order, never equal");
        NaturalNameOrder.Instance.Compare("x99999999999999999999999", "x100000000000000000000000").Should().BeLessThan(0, "long digit runs do not overflow");
    }

    [Fact]
    public void Gates_follow_the_natural_order_west_to_east()
    {
        var s = new Spec("p");
        foreach (var n in new[] { "Folder 10", "Folder 2", "Folder 1", "folder 3" }) s.Dir(new Spec(n));
        var tree = s.Build();
        var place = PlaceComposer.Compose(tree, 0);
        place.Gates.OrderBy(g => g.AngleDeg).Select(g => tree[g.TargetId].Name).Should().Equal("Folder 1", "Folder 2", "folder 3", "Folder 10");
    }
}

/// <summary>CTRL-04: letting go after a mouse look eases the view back to level.</summary>
public class LookLevelingTests
{
    [Theory]
    [InlineData(40f)]
    [InlineData(-55f)]
    [InlineData(3f)]
    public void Returns_to_level_smoothly_and_finishes_in_the_stated_time(float startDegrees)
    {
        var l = new FileScape.Core.Interaction.LookLeveling(FileScape.Core.Units.Degrees.Of(startDegrees));
        var dt = TimeSpan.FromSeconds(1.0 / 60);
        var previous = startDegrees;
        var frames = 0;
        var largestStep = 0f;
        while (!l.Done)
        {
            var p = l.Step(dt).Value;
            frames++;
            MathF.Sign(p).Should().NotBe(-MathF.Sign(startDegrees), "it never overshoots past level");
            MathF.Abs(p).Should().BeLessThanOrEqualTo(MathF.Abs(previous) + 1e-4f, "it only moves toward level");
            largestStep = MathF.Max(largestStep, MathF.Abs(previous - p));
            previous = p;
            frames.Should().BeLessThan(100);
        }

        previous.Should().Be(0f, "it ends exactly level");
        (frames * dt).Should().BeCloseTo(FileScape.Core.Interaction.LookLeveling.Duration, TimeSpan.FromSeconds(1.0 / 60));
        // Smoothstep peaks at 1.5× the average rate: no jump in any single frame.
        largestStep.Should().BeLessThan(MathF.Abs(startDegrees) * 1.5f / 21f + 0.01f);
    }

    [Fact]
    public void Levelling_is_on_by_default_and_saved()
    {
        FileScape.Core.Interaction.Preferences.Default.LevelViewOnRelease.Should().BeTrue();
        var off = FileScape.Core.Interaction.Preferences.Default with { LevelViewOnRelease = false };
        FileScape.Core.Interaction.Preferences.FromJson(off.ToJson(), out var problem).LevelViewOnRelease.Should().BeFalse();
        problem.Should().BeNull();
        FileScape.Core.Interaction.Preferences.FromJson("{\"version\":1}", out _).LevelViewOnRelease.Should().BeTrue("older files without the field get the default");
    }
}
