using FileScape.Core.Interaction;

namespace FileScape.Core.Tests;

/// <summary>Text-heavy panels fit every representative window and UI scale (PLANET-155): the designed size where there is
/// room, the window less its margins where there is not.</summary>
public class PanelFitTests
{
    /// <summary>The designed widths (viewport units) of the surfaces that are fitted: Help, the prompt, the telescope
    /// panel, the title card.</summary>
    private static readonly float[] Designed = [1100f, 920f, 900f, 840f];

    [Fact]
    public void Every_fitted_surface_stays_inside_every_representative_window()
    {
        const float margin = 16f;
        foreach (var (w, h) in PanelFit.RepresentativeWindows)
        foreach (var scale in PanelFit.RepresentativeScales)
        {
            var vw = PanelFit.ViewportUnits(w, scale);
            var vh = PanelFit.ViewportUnits(h, scale);
            foreach (var d in Designed)
            {
                var fitted = PanelFit.Width(d, vw, margin);
                fitted.Should().BeLessThanOrEqualTo(vw - 2 * margin, $"{d} in {w}×{h} at {scale}");
                fitted.Should().Be(MathF.Min(d, vw - 2 * margin), "it is never narrower than it needs to be");
            }

            var (fw, fh) = PanelFit.Size(1100f, 600f, vw, vh, margin);
            (fw <= vw - 2 * margin && fh <= vh - 2 * margin).Should().BeTrue($"Help in {w}×{h} at {scale}");
        }
    }

    [Fact]
    public void The_small_cases_that_used_to_overflow_now_shrink()
    {
        // 1280 × 720 at UI 1.25 and 1.5: 1024 and 853 units wide, less than Help's 1100 (it was cut off at both sides).
        PanelFit.Width(1100f, PanelFit.ViewportUnits(1280, 1.25f), 16f).Should().Be(992f);
        PanelFit.Width(1100f, PanelFit.ViewportUnits(1280, 1.5f), 16f).Should().BeApproximately(821.3f, 0.1f);
        PanelFit.Width(1100f, 1920f, 16f).Should().Be(1100f);
        PanelFit.Width(500f, 10f, 16f).Should().Be(0f, "never negative");
    }
}
