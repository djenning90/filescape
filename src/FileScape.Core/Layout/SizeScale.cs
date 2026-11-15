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
    /// <summary>The Classic FSN map (layout units, AD-07): ×1, capped at 6 u (the cap values are not explained in AD-07).</summary>
    public static readonly SizeScale Classic = new(0.12f, 0.42f, 1f, 6f);

    /// <summary>The District map (layout units, AD-07): a higher base and ×2.4 for its larger blocks (1.1 u footprints on 1.55 u cells), capped at 14 u.</summary>
    public static readonly SizeScale District = new(0.25f, 0.42f, 2.4f, 14f);

    /// <summary>Places (PLACE-03): towers taller than eye level (≈ 3 u for 1 KiB, 5.3 u for 1 MiB, 7.5 u for 1 GiB).</summary>
    public static readonly SizeScale Place = new(0.4f, 0.42f, 1.8f, 12f);

    /// <summary>A zero-byte file's height before <see cref="Multiplier"/>: well below <see cref="Base"/>, so empty reads as empty.</summary>
    private const float ZeroByteBaseHeight = 0.05f;

    /// <summary>The height given to zero-byte files: a thin tile that is still visible and pickable.</summary>
    public float ZeroByteHeight => ZeroByteBaseHeight * Multiplier;

    /// <summary>The mapped height before the cap: zero-byte files get <see cref="ZeroByteHeight"/>, not the log formula's base.</summary>
    public float UncappedHeight(long bytes) =>
        bytes <= 0 ? ZeroByteHeight : (Base + PerDecade * MathF.Log10(1f + bytes)) * Multiplier;

    /// <summary>The drawn height: <see cref="UncappedHeight"/> limited to <see cref="Cap"/>.</summary>
    public float Height(long bytes) => MathF.Min(UncappedHeight(bytes), Cap);

    /// <summary>True when the cap shortened this file (the inspector says so: a capped height is not comparable).</summary>
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
