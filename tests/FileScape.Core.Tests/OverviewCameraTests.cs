using System.Numerics;
using FileScape.Core.Planets;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// The overview camera's controls and transitions (PLANET-110..112, AD-82): what Shift does to each control, Esc's way
/// back to where you stand (a zoom-and-pan path in the overview, a hand-over onto the real planet that keeps your world's
/// size and place on screen, a glide down behind the traveller), and resuming where you stood after a legacy view.
/// </summary>
public sealed class OverviewCameraTests
{
    // ---------------------------------------------------------------- Shift (PLANET-110)

    [Fact]
    public void Without_Shift_every_control_keeps_its_tuning()
    {
        foreach (var m in Enum.GetValues<OverviewMove>()) OverviewControls.SpeedFactor(m, fast: false).Should().Be(1f, m.ToString());
        OverviewControls.WheelDistanceFactor(zoomIn: true, fast: false).Should().Be(0.85f);
        OverviewControls.WheelDistanceFactor(zoomIn: false, fast: false).Should().Be(1.18f);
        OverviewControls.WheelTowardShareFor(fast: false).Should().Be(0.3f);
    }

    [Fact]
    public void Shift_accelerates_travel_most_turning_like_walking_and_leaves_drag_orbit_direct()
    {
        OverviewControls.SpeedFactor(OverviewMove.KeyPan, fast: true).Should().Be(3f);
        OverviewControls.SpeedFactor(OverviewMove.DragPan, fast: true).Should().Be(3f);
        OverviewControls.SpeedFactor(OverviewMove.KeyOrbit, fast: true).Should().Be(1.6f);
        OverviewControls.SpeedFactor(OverviewMove.DragOrbit, fast: true).Should().Be(1f);
    }

    [Fact]
    public void A_Shift_wheel_notch_is_two_notches_and_in_then_out_still_comes_back()
    {
        var inFast = OverviewControls.WheelDistanceFactor(zoomIn: true, fast: true);
        var outFast = OverviewControls.WheelDistanceFactor(zoomIn: false, fast: true);
        inFast.Should().BeApproximately(0.85f * 0.85f, 1e-6f);
        outFast.Should().BeApproximately(1.18f * 1.18f, 1e-6f);
        (inFast * outFast).Should().BeInRange(0.99f, 1.02f);
        // The slide toward the pointer: two notches' worth (1 − 0.7²).
        OverviewControls.WheelTowardShareFor(fast: true).Should().BeApproximately(1 - 0.7f * 0.7f, 1e-6f);
    }

    // ---------------------------------------------------------------- the zoom-and-pan path (PLANET-111)

    [Fact]
    public void The_path_starts_and_ends_exactly_on_its_views()
    {
        var from = new OverviewView(new Vector3(400, 30, -250), 900f);
        var to = new OverviewView(new Vector3(-20, 5, 60), 6f);
        var p = new ZoomPanPath(from, to);
        p.Length.Should().BeGreaterThan(0);
        Close(p.At(0), from);
        Close(p.At(p.Length), to);
        Close(p.At(-1), from);
        Close(p.At(p.Length + 5), to);
    }

    [Fact]
    public void A_pure_zoom_moves_the_distance_geometrically_and_the_focus_not_at_all()
    {
        var c = new Vector3(5, 6, 7);
        var p = new ZoomPanPath(new OverviewView(c, 1000f), new OverviewView(c, 10f));
        p.Length.Should().BeApproximately(MathF.Log(100f) / ZoomPanPath.Rho, 1e-3f);
        var mid = p.At(p.Length / 2);
        mid.Focus.Should().Be(c);
        mid.DistanceMetres.Should().BeApproximately(100f, 0.5f); // the geometric mean
    }

    [Fact]
    public void Panning_far_compared_with_the_view_pulls_back_first_and_never_jumps()
    {
        var from = new OverviewView(Vector3.Zero, 20f);
        var to = new OverviewView(new Vector3(2000, 0, 0), 20f);
        var p = new ZoomPanPath(from, to);
        var steps = 400;
        var prev = p.At(0);
        var highest = 0f;
        for (var i = 1; i <= steps; i++)
        {
            var v = p.At(p.Length * i / steps);
            highest = MathF.Max(highest, v.DistanceMetres);
            // Continuity: no step moves the focus by more than a few view sizes or the distance by more than a few percent.
            Vector3.Distance(v.Focus, prev.Focus).Should().BeLessThan(MathF.Max(v.DistanceMetres, prev.DistanceMetres) * 0.2f);
            (v.DistanceMetres / prev.DistanceMetres).Should().BeInRange(0.95f, 1.05f);
            // The focus only moves forward along the line.
            v.Focus.X.Should().BeGreaterThanOrEqualTo(prev.Focus.X - 1e-3f);
            prev = v;
        }

        highest.Should().BeGreaterThan(200f, "a long pan zooms out to keep both ends in view");
    }

    [Fact]
    public void Identical_views_make_an_empty_path()
    {
        var v = new OverviewView(new Vector3(1, 2, 3), 50f);
        var p = new ZoomPanPath(v, v);
        p.Length.Should().Be(0);
        Close(p.At(0), v);
    }

    [Theory]
    [InlineData(6000f, 6f)]
    [InlineData(40f, 6f)]
    [InlineData(6.5f, 6f)]
    public void The_way_back_takes_a_bounded_time_whatever_the_zoom(float fromDistance, float toDistance)
    {
        var p = new ZoomPanPath(new OverviewView(new Vector3(300, 0, 0), fromDistance), new OverviewView(Vector3.Zero, toDistance));
        var d = OverviewReturn.Duration(p).TotalSeconds;
        d.Should().BeInRange(OverviewReturn.MinSeconds - 1e-6, OverviewReturn.MaxSeconds + 1e-6);
    }

    [Fact]
    public void The_overview_part_starts_and_ends_at_rest_and_its_light_rises_late()
    {
        OverviewReturn.Ease(0).Should().Be(0);
        OverviewReturn.Ease(1).Should().Be(1);
        const float h = 1e-3f;
        (OverviewReturn.Ease(h) / h).Should().BeLessThan(0.01f, "no jolt as Esc starts the move");
        ((1 - OverviewReturn.Ease(1 - h)) / h).Should().BeLessThan(0.01f, "at rest at the hand-over");
        OverviewReturn.Ease(0.5f).Should().BeApproximately(0.5f, 1e-6f);
        OverviewReturn.FlashLevel(0).Should().Be(0);
        OverviewReturn.FlashLevel(OverviewReturn.FlashFromProgress).Should().Be(0);
        OverviewReturn.FlashLevel(1).Should().BeApproximately(OverviewReturn.FlashPeakLevel, 1e-6f);
        for (var u = 0f; u < 1f; u += 0.05f) OverviewReturn.FlashLevel(u + 0.05f).Should().BeGreaterThanOrEqualTo(OverviewReturn.FlashLevel(u));
    }

    // ---------------------------------------------------------------- the hand-over onto the planet (PLANET-111)

    [Theory]
    [InlineData(30f, 0f)]
    [InlineData(60f, 1f)]
    [InlineData(14f, 2f)]
    public void At_the_hand_over_your_world_keeps_its_size_and_place_on_screen(float planetRadius, float axisSeed)
    {
        // The overview's last frame: CloseRadii body radii from your world's centre, looking at it.
        const float bodyRadius = 2f;
        var overviewApparent = OverviewReturn.ApparentRadius(bodyRadius, bodyRadius * OverviewReturn.CloseRadii);
        var (up, facing) = Frame(axisSeed);
        var (eye, forward, screenUp) = OverviewReturn.SurfaceStart(up, facing, planetRadius);
        // The real world is as big on screen…
        OverviewReturn.ApparentRadius(planetRadius, eye.Length()).Should().BeApproximately(overviewApparent, 1e-4f);
        // …and centred, as the overview's world is (the overview camera looks at its focus).
        Vector3.Dot(forward, Vector3.Normalize(-eye)).Should().BeApproximately(1f, 1e-5f);
        // A proper camera basis: unit, perpendicular, screen up toward the traveller's facing.
        forward.Length().Should().BeApproximately(1f, 1e-5f);
        screenUp.Length().Should().BeApproximately(1f, 1e-5f);
        Vector3.Dot(forward, screenUp).Should().BeApproximately(0f, 1e-5f);
        Vector3.Dot(screenUp, facing).Should().BeGreaterThan(0.5f);
        // Above and behind the traveller.
        Vector3.Dot(Vector3.Normalize(eye), up).Should().BeApproximately(MathF.Cos(OverviewReturn.BehindFromVerticalDegrees * MathF.PI / 180f), 1e-4f);
        Vector3.Dot(eye, facing).Should().BeLessThan(0);
    }

    [Theory]
    [InlineData(14f)]
    [InlineData(30f)]
    [InlineData(80f)]
    public void The_glide_down_from_the_hand_over_stays_above_the_ground_and_ends_behind_the_traveller(float planetRadius)
    {
        var (up, facing) = Frame(0.4f);
        var (eye, _, _) = OverviewReturn.SurfaceStart(up, facing, planetRadius);
        // The walking camera: 7.2 m back at 18° from a point 1.1 m over the feet (PlanetWorld.Walk's usual chase view).
        var feet = up * planetRadius;
        var pitch = 18f * MathF.PI / 180f;
        var walkingEye = feet + up * 1.1f - facing * (MathF.Cos(pitch) * 7.2f) + up * (MathF.Sin(pitch) * 7.2f);
        // The blend moves the eye along the straight line between the two (Transform3D.InterpolateWith, smoothstep in time).
        for (var i = 0; i <= 200; i++)
        {
            var k = i / 200f;
            var p = Vector3.Lerp(eye, walkingEye, k);
            p.Length().Should().BeGreaterThan(planetRadius + 1f, $"at {k:0.00} of the glide the eye is above the ground");
        }

        // It comes down from behind: the eye never passes in front of the traveller.
        for (var i = 0; i <= 20; i++) Vector3.Dot(Vector3.Lerp(eye, walkingEye, i / 20f) - feet, facing).Should().BeLessThan(0.01f);
    }

    [Fact]
    public void The_two_parts_meet_at_rest_so_the_hand_over_has_no_jump_in_speed()
    {
        // The overview part's speed at its end and the planet part's (the same smoothstep) at its start are both zero.
        const float h = 1e-3f;
        ((1 - OverviewReturn.Ease(1 - h)) / h).Should().BeLessThan(0.01f);
        (OverviewReturn.Ease(h) / h).Should().BeLessThan(0.01f);
    }

    // ---------------------------------------------------------------- resuming after a legacy view (PLANET-112)

    [Fact]
    public void Resuming_needs_the_same_tree_folder_and_selection()
    {
        var tree = new Spec("r").Dir(new Spec("a")).Dir(new Spec("b")).Build();
        var a = tree.Root.Children[0];
        var b = tree.Root.Children[1];
        var saved = new SurfaceResume(tree, a, -1, Vector3.UnitY, -Vector3.UnitZ, -Vector3.UnitZ, 25f, 9f);
        SurfaceResume.Decide(saved, tree, a, -1).Should().Be(SurfaceResumeDecision.Resume);
        SurfaceResume.Decide(null, tree, a, -1).Should().Be(SurfaceResumeDecision.NothingSaved);
        SurfaceResume.Decide(saved, tree, b, -1).Should().Be(SurfaceResumeDecision.MovedElsewhere);
        SurfaceResume.Decide(saved, tree, a, b).Should().Be(SurfaceResumeDecision.SelectionChanged);
        var rescan = new Spec("r").Dir(new Spec("a")).Dir(new Spec("b")).Build();
        SurfaceResume.Decide(saved, rescan, a, -1).Should().Be(SurfaceResumeDecision.NewTree);
        SurfaceResume.Decide(saved, null, a, -1).Should().Be(SurfaceResumeDecision.NewTree);
    }

    /// <summary>AD-92 (PLANET-144) replaced "only a traveller standing on the ground" (a jump, a twirl, a Hazard World and a
    /// fall were not kept, so you arrived afresh): now only a departure, a hop or a trip is not (SurfaceResumeTests).</summary>
    [Fact]
    public void Only_a_traveller_on_the_way_somewhere_is_not_kept()
    {
        SurfaceResume.MayCapture(travelling: false).Should().BeTrue();
        SurfaceResume.MayCapture(travelling: true).Should().BeFalse();
    }

    // ---------------------------------------------------------------- Enter in the overview (PLANET-83, PLANET-114)

    [Fact]
    public void A_row_chosen_in_the_list_is_what_the_overviews_Enter_lands_on()
    {
        // The fifth pass's second silent failure: a row clicked in the list, then Enter acted on "you are here".
        const int here = 1, row = 7;
        bool Drawn(int id) => id is 1 or 7 or 9;
        var target = OverviewEnter.Target(cardSelection: row, overviewSelected: here, Drawn);
        target.Should().Be(row);
        OverviewEnter.Decide(target, here, Drawn, landing: false).Should().Be(OverviewEnterAction.FlyInto);
        // A card selection the overview does not draw (a file, a hidden folder) leaves the overview's own choice.
        OverviewEnter.Target(cardSelection: 42, overviewSelected: 9, Drawn).Should().Be(9);
        OverviewEnter.Target(cardSelection: -1, overviewSelected: 9, Drawn).Should().Be(9);
    }

    [Fact]
    public void Every_Enter_in_the_overview_lands_goes_back_down_or_says_why()
    {
        bool Drawn(int id) => id is 1 or 7;
        OverviewEnter.Decide(7, here: 1, Drawn, landing: false).Should().Be(OverviewEnterAction.FlyInto);
        OverviewEnter.Decide(1, here: 1, Drawn, landing: false).Should().Be(OverviewEnterAction.BackDown);
        OverviewEnter.Decide(30, here: 1, Drawn, landing: false).Should().Be(OverviewEnterAction.TravelOutOfLight);
        OverviewEnter.Decide(-1, here: 1, Drawn, landing: false).Should().Be(OverviewEnterAction.SayChooseFirst);
        OverviewEnter.Decide(7, here: 1, Drawn, landing: true).Should().Be(OverviewEnterAction.Wait);
    }

    [Fact]
    public void With_the_overview_open_a_commit_lands_through_it_never_as_a_surface_trip()
    {
        // The fifth pass's first silent failure: a folder two levels down chosen in the list started a trip whose hops
        // never ran under the overview (wind-up heard, nothing happened).
        OverviewEnter.CommitLandsThroughOverview(overviewOpen: true).Should().BeTrue();
        OverviewEnter.CommitLandsThroughOverview(overviewOpen: false).Should().BeFalse();
    }

    // ---------------------------------------------------------------- helpers

    private static void Close(OverviewView a, OverviewView b)
    {
        Vector3.Distance(a.Focus, b.Focus).Should().BeLessThan(1e-2f);
        a.DistanceMetres.Should().BeApproximately(b.DistanceMetres, b.DistanceMetres * 1e-4f);
    }

    /// <summary>A unit up and a unit facing perpendicular to it, turned by <paramref name="seed"/> so the tests are not axis-aligned.</summary>
    private static (Vector3 Up, Vector3 Facing) Frame(float seed)
    {
        var q = Quaternion.CreateFromYawPitchRoll(seed * 0.7f, seed * 0.4f, seed * 0.2f);
        return (Vector3.Transform(Vector3.UnitY, q), Vector3.Transform(-Vector3.UnitZ, q));
    }
}
