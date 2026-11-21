using FileScape.Core.Interaction;
using FileScape.Core.Planets;

namespace FileScape.Core.Tests;

/// <summary>
/// The seventh pass's integration fixes (stream F), each through the entry point the app calls: the telescope's lens
/// answers the wheel even when the framed world is so near that the framing sits at the widest lens
/// (<see cref="TelescopeLens"/>, planets-lookouts), the object you just looked at closer stays the focus until you walk
/// (<see cref="ObjectFocus"/>, planets-text), and the preview panel fits a short window (<see cref="PanelFit.BottomGap"/>).
/// </summary>
public class IntegrationFixTests
{
    private const float MinFov = 5f, MaxFov = 50f, MinZoom = 0.35f, MaxZoom = 2.5f;

    [Fact]
    public void The_wheel_narrows_the_lens_when_the_framing_is_wider_than_the_widest_lens()
    {
        // A near world: framing it whole would need 150°, so the lens sits at its widest, 50° (planets-lookouts saw
        // 50° → out 50° → in 50°: the zoom multiplied the unclamped framing, so 0.62 × 150° was still clamped to 50°).
        const float framing = 150f;
        var zoom = 1f;
        for (var i = 0; i < 3; i++) zoom = TelescopeLens.Step(zoom, 1.18f, framing, MinFov, MaxFov, MinZoom, MaxZoom); // out
        TelescopeLens.Fov(framing, zoom, MinFov, MaxFov).Should().Be(MaxFov, "already the widest lens");
        for (var i = 0; i < 6; i++) zoom = TelescopeLens.Step(zoom, 0.85f, framing, MinFov, MaxFov, MinZoom, MaxZoom); // in
        TelescopeLens.Fov(framing, zoom, MinFov, MaxFov).Should().BeLessThan(MaxFov - 10f, "six notches in narrow the lens at once");
    }

    [Fact]
    public void The_lens_is_unchanged_where_the_framing_fits_the_lens()
    {
        TelescopeLens.Fov(30f, 1f, MinFov, MaxFov).Should().Be(30f);
        TelescopeLens.Fov(30f, 1.18f, MinFov, MaxFov).Should().BeApproximately(35.4f, 0.01f);
        TelescopeLens.Fov(30f, 0.85f, MinFov, MaxFov).Should().BeApproximately(25.5f, 0.01f);
        TelescopeLens.Step(1f, 1.18f, 30f, MinFov, MaxFov, MinZoom, MaxZoom).Should().BeApproximately(1.18f, 1e-5f);
        TelescopeLens.Step(2.4f, 1.18f, 10f, MinFov, MaxFov, MinZoom, MaxZoom).Should().Be(MaxZoom, "the zoom keeps its own limits");
    }

    [Fact]
    public void The_object_you_just_looked_at_stays_the_focus_while_in_reach()
    {
        // Standing 1.8 m from the file you hopped to (ahead), with a neighbour 1.2 m away a little to the side (planets-text:
        // the prompt named the neighbour while the preview showed the file and its panel said "Close (E / Esc)").
        FocusCandidate[] near = [new(0, 1.8f, 1f), new(1, 1.2f, 0.9f)];
        ObjectFocus.Best(near, 2.8f, 0.8f).Should().Be(1, "without a held object the nearest, favouring what is ahead, as before");
        ObjectFocus.Best(near, 2.8f, 0.8f, held: 0).Should().Be(0, "the object you looked closer at stays the focus");
        ObjectFocus.Best([new(0, 3.5f, 1f), new(1, 1.2f, 0.9f)], 2.8f, 0.8f, held: 0).Should().Be(1, "out of reach it is not held");
        ObjectFocus.Best([new(0, 2.0f, -0.6f)], 2.8f, 0.8f).Should().Be(-1, "behind you and over 1 m away is not in focus");
        ObjectFocus.Best([], 2.8f, 0.8f, held: 3).Should().Be(-1);
    }

    [Fact]
    public void The_preview_panel_keeps_its_designed_gap_where_there_is_room_and_fits_a_short_window()
    {
        // The planets' preview: 150 units from the top, 180 above the bottom for the prompt, at least 300 tall.
        PanelFit.BottomGap(150f, 180f, 300f, 900f, 16f).Should().Be(180f, "1600 × 900 at 1.0: as designed");
        var h = PanelFit.ViewportUnits(720, 1.5f); // 480 units: the designed gap would leave 150 units, less than its content
        var gap = PanelFit.BottomGap(150f, 180f, 300f, h, 16f);
        (h - gap - 150f).Should().BeGreaterThanOrEqualTo(300f - 0.01f);
        gap.Should().BeGreaterThanOrEqualTo(16f, "never closer to the edge than the margin");
        PanelFit.BottomGap(150f, 180f, 300f, 300f, 16f).Should().Be(16f, "too short for the minimum: the margin, and the content scrolls");
    }
}
