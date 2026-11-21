namespace FileScape.Core.Interaction;

/// <summary>
/// Fitting interface panels to the window (PLANET-155): a panel or a line of screen text is drawn at its designed size
/// where the window has room, and shrinks to the window, keeping a margin, where it does not. Sizes are viewport units
/// (docs/units.md: window pixels divided by the UI scale, because the window's content scale is the UI scale), so a
/// 1280 × 720 window at UI 1.5 has 853 × 480 units, less than Help's designed 1100 × 540.
/// Preserve: a fitted size never exceeds the designed one, never leaves less than the margin, and is never negative.
/// </summary>
public static class PanelFit
{
    /// <summary>The window sizes (pixels) and UI scales FileScape is checked at (the brief's representative set).</summary>
    public static readonly (int Width, int Height)[] RepresentativeWindows = [(1280, 720), (1600, 900), (1920, 1080), (2560, 1440)];

    /// <summary>The UI scales checked with each window (Settings offers 1.0–1.5 among others).</summary>
    public static readonly float[] RepresentativeScales = [1.0f, 1.25f, 1.5f];

    /// <summary>The viewport's extent in units for a window extent in pixels at <paramref name="uiScale"/>.</summary>
    public static float ViewportUnits(int windowPixels, float uiScale) => windowPixels / uiScale;

    /// <summary>The width (or height) to draw: <paramref name="designed"/>, or the viewport less a margin each side.</summary>
    public static float Width(float designed, float viewport, float margin) => MathF.Max(0, MathF.Min(designed, viewport - 2 * margin));

    /// <summary>Both extents (see <see cref="Width"/>).</summary>
    public static (float Width, float Height) Size(float designedWidth, float designedHeight, float viewportWidth, float viewportHeight, float margin) =>
        (Width(designedWidth, viewportWidth, margin), Width(designedHeight, viewportHeight, margin));

    /// <summary>
    /// The gap to leave under a panel anchored <paramref name="top"/> units from the top and stretched down: the designed
    /// gap where the panel still gets <paramref name="minHeight"/>, less in a short window (down to the margin), so the
    /// panel's content fits instead of running off the bottom (the planets' preview at 1280 × 720, UI 1.5: 480 units tall,
    /// its content ran to 551; integration of the seventh pass).
    /// </summary>
    public static float BottomGap(float top, float designedGap, float minHeight, float viewportHeight, float margin) =>
        Math.Clamp(viewportHeight - top - minHeight, margin, Math.Max(margin, designedGap));
}
