namespace FileScape.Core.Interaction;

/// <summary>
/// The order of names in Places (ORD-01): gates west to east and tier by tier outward, the Destinations list, file
/// rows and vista beacons all use it, so what you see and what is listed agree.
/// <list type="number">
/// <item>Letters compare case-insensitively (ordinal, culture-independent, so the order is the same on every machine).</item>
/// <item>Runs of ASCII digits compare by value: "Folder 2" before "Folder 10", "v9" before "v10". Equal values with
/// different leading zeros: fewer zeros first ("7" before "007").</item>
/// <item>Names still equal then compare ordinally, so Linux names that differ only in case stay distinct and in a fixed
/// order (capitals first: "README" before "Readme" before "readme").</item>
/// </list>
/// The maps keep the tree's own child order (case-insensitive ordinal) for their layouts.
/// </summary>
public sealed class NaturalNameOrder : IComparer<string>
{
    public static readonly NaturalNameOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        var k = CompareNatural(x, y);
        if (k != 0) return k;
        k = StringComparer.OrdinalIgnoreCase.Compare(x, y);
        return k != 0 ? k : string.CompareOrdinal(x, y);
    }

    private static int CompareNatural(string x, string y)
    {
        int i = 0, j = 0;
        var zeros = 0; // tie-break for numbers equal in value: fewer leading zeros first
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int si = i, sj = j;
                while (si < x.Length && x[si] == '0') si++;
                while (sj < y.Length && y[sj] == '0') sj++;
                int ei = si, ej = sj;
                while (ei < x.Length && char.IsAsciiDigit(x[ei])) ei++;
                while (ej < y.Length && char.IsAsciiDigit(y[ej])) ej++;
                // Significant digits: a longer run is a larger number; equal lengths compare digit by digit.
                var len = (ei - si).CompareTo(ej - sj);
                if (len != 0) return len;
                for (int a = si, b = sj; a < ei; a++, b++)
                {
                    if (x[a] != y[b]) return x[a].CompareTo(y[b]);
                }

                if (zeros == 0) zeros = (si - i).CompareTo(sj - j);
                i = ei;
                j = ej;
                continue;
            }

            var cx = char.ToUpperInvariant(x[i]);
            var cy = char.ToUpperInvariant(y[j]);
            if (cx != cy) return cx.CompareTo(cy);
            i++;
            j++;
        }

        var rest = (x.Length - i).CompareTo(y.Length - j);
        return rest != 0 ? rest : zeros;
    }
}
