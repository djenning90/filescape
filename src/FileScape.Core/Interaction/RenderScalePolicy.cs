namespace FileScape.Core.Interaction;

/// <summary>
/// How much of a very large window the 3D world is rendered at (PLANET-180, AD-96). Measured on the seventh pass's
/// machine (AMD Radeon 890M) standing on the Ubuntu root fullscreen at 3840×2160: the renderer's GPU time was 18.2 ms as
/// shipped (4× MSAA, glow, shadows, the sky shader), so frames missed 60 Hz (walking p50 19.6 ms); 4× MSAA was the largest
/// single cost (−7.4 ms off), then the sky shader (−3.9), glow (−2.7), shadows (−0.7); rendering at 0.75 scale cut 6.8 ms
/// and at 0.5 cut 11.4. Windowed at 1600×900 the same scene takes ≈ 7.5 ms, comfortably inside a frame.
/// <para>
/// The rule: up to <see cref="FullScalePixels"/> (2560×1440) the 3D world is drawn at full resolution, so the look at
/// 1600×900, 1920×1080 and 2560×1440 is unchanged; above it, at the scale that brings the drawn pixels back to that
/// budget (the GUI and text stay at full resolution, and the app upscales with FSR 1, which sharpens), never below
/// <see cref="MinScale"/>. At 3840×2160 that is 0.67 (≈ 2560×1440 drawn). An explicit scale (<c>--render-scale=</c>)
/// wins. Preserve: 1 for every window at or under the budget; monotonic in the window's area; within [MinScale, 1].
/// </para>
/// </summary>
public static class RenderScalePolicy
{
    /// <summary>The most pixels drawn at full scale: 2560 × 1440 (≈ 3.7 million).</summary>
    public const long FullScalePixels = 2560L * 1440L;

    /// <summary>The smallest automatic scale (two thirds: a 4K window draws ≈ 1440p; lower starts to look soft).</summary>
    public const float MinScale = 2f / 3f;

    /// <summary>The 3D render scale for a window of <paramref name="width"/> × <paramref name="height"/> pixels, or
    /// <paramref name="requested"/> (clamped to 0.25..1) when one was asked for.</summary>
    public static float ScaleFor(int width, int height, float? requested = null)
    {
        if (requested is { } r) return Math.Clamp(r, 0.25f, 1f);
        var pixels = (long)Math.Max(1, width) * Math.Max(1, height);
        if (pixels <= FullScalePixels) return 1f;
        return MathF.Max(MinScale, MathF.Sqrt(FullScalePixels / (float)pixels));
    }
}
