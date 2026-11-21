namespace FileScape.Core.Planets;

/// <summary>
/// The telescope's lens (PLANET-188, AD-98; fixed in the seventh pass's integration): the field of view it draws for the
/// framed world and how the wheel or W/S change it. The framing (the angle that shows the world whole with sky round it)
/// is clamped to the lens's range first and the zoom applies to that, so a world near enough to need more than the widest
/// lens still narrows at the first notch in (planets-lookouts saw 50° → out 50° → in 50°: the zoom multiplied the
/// unclamped framing). The zoom itself is kept where it changes what you see, so notches out past the widest lens are not
/// banked and then spent before a notch in shows.
/// Preserve: where the framing is within the lens's range the field of view is framing × zoom, as before; the zoom stays
/// within its own limits; the result stays within the lens's range.
/// </summary>
public static class TelescopeLens
{
    /// <summary>The field of view, degrees, for <paramref name="framingDegrees"/> at <paramref name="zoom"/> (1: framed whole).</summary>
    public static float Fov(float framingDegrees, float zoom, float minFovDegrees, float maxFovDegrees) =>
        Math.Clamp(Math.Clamp(framingDegrees, minFovDegrees, maxFovDegrees) * zoom, minFovDegrees, maxFovDegrees);

    /// <summary>
    /// The zoom after multiplying it by <paramref name="factor"/> (a wheel notch, or a held key's share of a second),
    /// within its own limits and within the range where it still changes the lens for this framing.
    /// </summary>
    public static float Step(float zoom, float factor, float framingDegrees, float minFovDegrees, float maxFovDegrees, float minZoom, float maxZoom)
    {
        var framing = Math.Clamp(framingDegrees, minFovDegrees, maxFovDegrees);
        var lo = Math.Max(minZoom, minFovDegrees / framing);
        var hi = Math.Max(lo, Math.Min(maxZoom, maxFovDegrees / framing));
        return Math.Clamp(zoom * factor, lo, hi);
    }
}
