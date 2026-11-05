using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Units;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Scanning;

namespace FileScape.Core.Tests;

/// <summary>
/// Camera navigation math (NAV-07..NAV-10), alias handling (NAV-08), stale-layout consistency during scans
/// (VIS-10, review finding 1), and the cloud-root policy (FS-13, review finding 3).
/// </summary>
public class NavigationAndConsistencyTests
{
    private const float Eps = 1e-4f;

    [Fact]
    public void Diagonal_movement_is_not_faster_than_straight_movement()
    {
        var straight = CameraMath.GroundDelta(new Vector3(0, 0, 1), Degrees.Of(30), 10, TimeSpan.FromSeconds(0.5)).Length();
        var diagonal = CameraMath.GroundDelta(new Vector3(1, 0, 1), Degrees.Of(30), 10, TimeSpan.FromSeconds(0.5)).Length();
        var threeWay = CameraMath.FlyDelta(new Vector3(1, 1, 1), Degrees.Of(30), Degrees.Of(-20), 10, TimeSpan.FromSeconds(0.5)).Length();
        diagonal.Should().BeApproximately(straight, Eps);
        threeWay.Should().BeApproximately(straight, Eps);
        straight.Should().BeApproximately(5f, Eps, "speed × dt, independent of frame rate");
    }

    [Fact]
    public void Ground_movement_ignores_pitch_and_fly_movement_follows_the_view()
    {
        var ground = CameraMath.GroundDelta(new Vector3(0, 0, 1), Degrees.Zero, 1, TimeSpan.FromSeconds(1));
        ground.Should().Be(new Vector3(0, 0, -1) with { X = ground.X });
        ground.X.Should().BeApproximately(0, Eps);

        var fly = CameraMath.FlyDelta(new Vector3(0, 0, 1), Degrees.Zero, Degrees.Of(-30), 1, TimeSpan.FromSeconds(1));
        fly.Y.Should().BeApproximately(-0.5f, Eps, "looking 30° down, forward descends");
        fly.Z.Should().BeApproximately(-MathF.Cos(MathF.PI / 6), Eps);

        var strafe = CameraMath.FlyDelta(new Vector3(1, 0, 0), Degrees.Of(90), Degrees.Of(-30), 1, TimeSpan.FromSeconds(1));
        strafe.Y.Should().BeApproximately(0, Eps, "strafe stays horizontal");
        var up = CameraMath.FlyDelta(new Vector3(0, 1, 0), Degrees.Of(45), Degrees.Of(-30), 1, TimeSpan.FromSeconds(1));
        up.Should().Be(Vector3.UnitY, "Q/E are world-vertical");
    }

    [Fact]
    public void Right_is_perpendicular_to_forward_and_horizontal()
    {
        foreach (var yaw in new[] { 0f, 37f, 90f, 200f, -75f }.Select(Degrees.Of))
        {
            Vector3.Dot(CameraMath.Right(yaw), CameraMath.HorizontalForward(yaw)).Should().BeApproximately(0, Eps);
            CameraMath.Right(yaw).Y.Should().Be(0);
        }
    }

    [Fact]
    public void Pitch_is_limited_short_of_vertical_so_the_view_never_flips()
    {
        CameraMath.ClampLookPitch(Degrees.Of(400)).Should().Be(CameraMath.MaxLookPitch);
        CameraMath.ClampLookPitch(Degrees.Of(-400)).Should().Be(CameraMath.MinLookPitch);
        CameraMath.MaxLookPitch.Should().BeLessThan(Degrees.Of(90));
        CameraMath.Forward(Degrees.Zero, CameraMath.MaxLookPitch).Y.Should().BeLessThan(1f);
    }

    [Fact]
    public void Fly_speed_steps_with_the_wheel_within_bounds_and_scales_with_distance()
    {
        CameraMath.StepFlySpeed(10, 1).Should().BeApproximately(12.5f, Eps);
        CameraMath.StepFlySpeed(10, -1).Should().BeApproximately(8f, Eps);
        CameraMath.StepFlySpeed(10, 1000).Should().Be(CameraMath.MaxFlySpeedWorldUnitsPerSecond);
        CameraMath.StepFlySpeed(10, -1000).Should().Be(CameraMath.MinFlySpeedWorldUnitsPerSecond);
        CameraMath.StepFlySpeed(10, 2).Should().BeApproximately(CameraMath.StepFlySpeed(CameraMath.StepFlySpeed(10, 1), 1), Eps,
            "one event worth two notches (fast or high-resolution wheel) equals two single notches");
        CameraMath.StepFlySpeed(10, 0.5f).Should().BeInRange(10.5f, 11.5f, "fractional touchpad scrolling adjusts proportionally");
        CameraMath.InitialFlySpeed(10).Should().BeLessThan(CameraMath.InitialFlySpeed(300), "close inspection starts slower than long-distance travel");
    }

    /// <summary>NAV-09: switching between orbit and fly must not move or rotate the camera.</summary>
    [Theory]
    [InlineData(0f, -30f, 40f)]
    [InlineData(123f, 20f, 7.5f)]
    [InlineData(-60f, -84f, 900f)]
    public void Pose_round_trips_between_orbit_and_fly_exactly(float yawDegrees, float pitchDegrees, float distance)
    {
        var target = new Vector3(12, 3, -40);
        var (yaw, pitch) = (Degrees.Of(yawDegrees), Degrees.Of(pitchDegrees));
        var position = CameraMath.OrbitPosition(target, yaw, pitch, distance);
        var back = CameraMath.TargetFromPose(position, yaw, pitch, distance);
        Vector3.Distance(back, target).Should().BeLessThan(1e-3f * MathF.Max(1, distance));
    }

    [Fact]
    public void Orbit_pitch_limit_keeps_the_camera_above_ground()
    {
        var limit = CameraMath.MaxOrbitLookPitchAboveGround(targetHeight: 0, distanceWorldUnits: 50, minCameraHeight: 0.4f);
        var pos = CameraMath.OrbitPosition(new Vector3(0, 0, 0), Degrees.Zero, limit, 50);
        pos.Y.Should().BeApproximately(0.4f, 1e-3f);
        limit.Should().BeLessThan(Degrees.Zero, "with the target on the ground, the camera must look down");
        CameraMath.MaxOrbitLookPitchAboveGround(targetHeight: 100, distanceWorldUnits: 10, minCameraHeight: 0.4f)
            .Should().Be(CameraMath.MaxLookPitch, "a high target allows looking up");
    }

    /// <summary>NAV-08: aliases (W and Up) must not double movement; releasing one alias keeps the action held.</summary>
    [Fact]
    public void Aliases_do_not_double_and_release_is_tracked_per_key()
    {
        var held = new HeldActions();
        held.Press("nav_forward", 'W');
        held.Press("nav_forward", 4194320); // Up arrow
        held.Axis("nav_back", "nav_forward").Should().Be(1f);
        held.Release("nav_forward", 4194320);
        held.IsHeld("nav_forward").Should().BeTrue("W is still down");
        held.Release("nav_forward", 'W');
        held.IsHeld("nav_forward").Should().BeFalse();

        held.Press("nav_forward", 'W');
        held.Press("nav_back", 'S');
        held.Axis("nav_back", "nav_forward").Should().Be(0f, "opposites cancel");
        held.Clear();
        held.Any.Should().BeFalse("focus loss clears everything, so nothing can stay stuck");
        held.Release("nav_left", 'A'); // a release whose press the UI consumed must be harmless
    }

    /// <summary>
    /// Review finding 1: during a scan the tree grows while the displayed layout is kept. Picking and label
    /// planning must only use nodes the layout knows about, and must not throw.
    /// </summary>
    [Fact]
    public void Picking_and_labels_stay_consistent_when_the_tree_outgrows_the_displayed_layout()
    {
        var source = new SyntheticTreeSource { TargetNodes = 4000 };
        using var session = new ScanSession(source);
        var tree = new FsTree(session.Source);
        session.Start();
        session.WaitForExit(TimeSpan.FromSeconds(30)).Should().BeTrue();
        var batches = new List<ScanBatch>();
        while (session.TryDequeue(out var b))
        {
            batches.Add(b);
        }

        // Apply roughly half, lay out, then apply the rest without re-layout: the interval Main deliberately allows.
        var half = batches.Count / 2;
        foreach (var b in batches.Take(half)) tree.Apply(b);
        var stale = TreeLayout.Compute(tree, LayoutParams.Classic);
        foreach (var b in batches.Skip(half)) tree.Apply(b);
        tree.Count.Should().BeGreaterThan(stale.Count, "the test must actually exercise the stale interval");

        var selection = new SelectionState();
        var newest = tree.Count - 1;
        selection.Select(newest);              // selected node has no geometry yet
        selection.Hovered = stale.Count - 1;

        var candidates = LabelPlanner.Candidates(tree, stale, selection, new Vector3(0, 50, 50));
        candidates.Should().NotBeEmpty();
        candidates.Should().OnlyContain(c => stale.Contains(c.Id));
        candidates.Should().NotContain(c => c.Id == newest);

        // Every ray over the displayed area resolves to a node that has geometry, or to nothing.
        var bounds = stale.Bounds;
        for (var x = bounds.Min.X; x < bounds.Max.X; x += (bounds.Max.X - bounds.Min.X) / 25)
        {
            for (var z = bounds.Min.Z; z < bounds.Max.Z; z += (bounds.Max.Z - bounds.Min.Z) / 25)
            {
                var hit = Picker.Pick(tree, stale, new Vector3(x, 100, z), -Vector3.UnitY);
                (hit == -1 || stale.Contains(hit)).Should().BeTrue();
            }
        }

        // Picking still works during the interval (previously it returned -1 whenever the counts differed).
        var someFile = Enumerable.Range(0, stale.Count).First(i => tree[i].Kind == NodeKind.File);
        Picker.Pick(tree, stale, stale.NodeBoxes[someFile].Center with { Y = 100 }, -Vector3.UnitY).Should().Be(someFile);
    }

    /// <summary>Review finding 3 / FS-13: a recall-on-data-access root is shown but not enumerated, like descendants.</summary>
    [Fact]
    public void Cloud_recall_root_is_visibly_left_unenumerated()
    {
        var root = Path.Combine(Path.GetTempPath(), "FileScape-fixtures", "cloudroot-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "would-be-listed"));
        try
        {
            var probed = false;
            var scanner = new DirectoryScanner(root)
            {
                RootAttributesProbe = _ =>
                {
                    probed = true;
                    return FileAttributes.Directory | DirectoryScanner.RecallOnDataAccess;
                },
            };
            var (tree, progress) = ScanRunner.Run(scanner);

            probed.Should().BeTrue();
            progress.State.Should().Be(ScanState.Completed);
            tree.Count.Should().Be(1, "the root's children must not be enumerated");
            tree.Root.ScanState.Should().Be(DirScanState.NotTraversed);
            tree.Root.Flags.Should().HaveFlag(NodeFlags.CloudNotTraversed).And.HaveFlag(NodeFlags.CloudPlaceholder);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Enumeration_policy_is_the_same_function_for_root_and_children()
    {
        DirectoryScanner.ShouldEnumerateDirectory(FileAttributes.Directory).Should().BeTrue();
        DirectoryScanner.ShouldEnumerateDirectory(FileAttributes.Directory | DirectoryScanner.RecallOnDataAccess).Should().BeFalse();
        DirectoryScanner.ShouldEnumerateDirectory(FileAttributes.Directory | DirectoryScanner.RecallOnOpen).Should().BeTrue(
            "RECALL_ON_OPEN concerns opening file content, which a metadata scan never does; it is still flagged as a cloud placeholder");
    }
}
