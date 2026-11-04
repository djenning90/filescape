using System.Globalization;

namespace FileScape.Core.Layout;

/// <summary>
/// Maps a logical byte size to a visual height (requirement VIS-03).
/// <para>
/// height = <see cref="Base"/> + <see cref="PerDecade"/> · log10(1 + bytes), times the visualization's multiplier,
/// capped at <see cref="Cap"/>.
/// </para>
/// <para>
/// Why log rather than FSN's square-root "exaggerated" mode: the scale is absolute rather than normalised to the
/// largest file. Heights therefore do not change while a scan is still discovering files, and the very wide range
/// of real sizes (0 B to TB) stays readable. Each ten-fold increase in size adds the same height. The inspector
/// always shows the exact byte count. See docs/decisions.md AD-07.
/// </para>
/// </summary>
public sealed record SizeScale(float Base, float PerDecade, float Multiplier, float Cap)
{
    public static readonly SizeScale Classic = new(0.12f, 0.42f, 1f, 6f);
    public static readonly SizeScale District = new(0.25f, 0.42f, 2.4f, 14f);

    /// <summary>Places (PLACE-03): towers taller than eye level (≈ 3 u for 1 KiB, 5.3 u for 1 MiB, 7.5 u for 1 GiB).</summary>
    public static readonly SizeScale Place = new(0.4f, 0.42f, 1.8f, 12f);

    /// <summary>The height given to zero-byte files: a thin tile that is still visible and pickable.</summary>
    public float ZeroByteHeight => 0.05f * Multiplier;

    public float UncappedHeight(long bytes) =>
        bytes <= 0 ? ZeroByteHeight : (Base + PerDecade * MathF.Log10(1f + bytes)) * Multiplier;

    public float Height(long bytes) => MathF.Min(UncappedHeight(bytes), Cap);

    public bool IsCapped(long bytes) => UncappedHeight(bytes) > Cap;

    /// <summary>An explanation for the inspector and legend.</summary>
    public string Describe(long bytes)
    {
        var h = Height(bytes);
        var text = bytes <= 0
            ? $"Height {h:0.00} u: zero-byte tile"
            : $"Height {h:0.00} u: log scale, each ×10 in size adds {PerDecade * Multiplier:0.00} u";
        return IsCapped(bytes)
            ? text + string.Create(CultureInfo.InvariantCulture, $"; CAPPED at {Cap:0.#} u (uncapped {UncappedHeight(bytes):0.00} u)")
            : text;
    }
}
