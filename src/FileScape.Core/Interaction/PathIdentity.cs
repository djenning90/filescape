using FileScape.Core.Scanning;

namespace FileScape.Core.Interaction;

/// <summary>
/// PROM-05 (AD-36): when two full paths name the same folder, for pins and other path-keyed identities. Platform-aware:
/// <list type="bullet">
/// <item>Linux paths are case-sensitive: <c>/home/dj/Notes</c> and <c>/home/dj/notes</c> are different folders. That holds
/// both for POSIX paths and for WSL paths seen from Windows (<c>\\wsl.localhost\Distro\…</c>), whose Linux part is
/// compared exactly. The two WSL routes (<c>\\wsl.localhost\</c> and the older <c>\\wsl$\</c>) and the distribution name
/// (which WSL matches without case) are aliases.</item>
/// <item>Windows paths (drives, network shares) are compared without case, as Windows does by default.</item>
/// </list>
/// A trailing separator is not part of the identity (except for a root such as <c>C:\</c> or <c>/</c>). Per-directory
/// case sensitivity on NTFS (<c>fsutil file setCaseSensitiveInfo</c>) is not detected: such a folder is compared as
/// Windows usually does.
/// </summary>
public static class PathIdentity
{
    /// <summary>The canonical WSL route (the same spelling as <see cref="Locations.WslPrefix"/>).</summary>
    public const string WslPrefix = @"\\wsl.localhost\";
    private const string WslLegacyPrefix = @"\\wsl$\";

    /// <summary>Compares paths by <see cref="Key"/>.</summary>
    public static readonly StringComparer Comparer = new KeyComparer();

    /// <summary>
    /// The path in its canonical spelling, for storing: a WSL path through <c>\\wsl.localhost\</c>, without a trailing
    /// separator. Case is kept as given (the distribution name too), so what is stored is what the user saw.
    /// </summary>
    public static string Normalize(string path)
    {
        var p = path;
        if (p.StartsWith(WslLegacyPrefix, StringComparison.OrdinalIgnoreCase)) p = WslPrefix + p[WslLegacyPrefix.Length..];
        else if (p.StartsWith(WslPrefix, StringComparison.OrdinalIgnoreCase) && !p.StartsWith(WslPrefix, StringComparison.Ordinal)) p = WslPrefix + p[WslPrefix.Length..];
        return TrimSeparator(p);
    }

    /// <summary>A string equal for two paths exactly when they name the same folder under the rules above.</summary>
    /// <remarks>
    /// The "posix:", "wsl:" and "win:" prefixes keep the three families apart, so a Windows path can never collide with
    /// a Linux one that happens to upper-case to the same text. Keys are in-memory only; nothing stores them.
    /// </remarks>
    public static string Key(string path)
    {
        var p = Normalize(path);
        if (p.StartsWith('/')) return "posix:" + p;
        if (p.StartsWith(WslPrefix, StringComparison.Ordinal))
        {
            var rest = p[WslPrefix.Length..];
            var slash = rest.IndexOf('\\');
            var distro = slash < 0 ? rest : rest[..slash];
            return "wsl:" + distro.ToUpperInvariant() + ":" + (ListAhead.LinuxPath(p) ?? "/");
        }

        return "win:" + p.ToUpperInvariant();
    }

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> name the same folder (equal <see cref="Key"/>s).</summary>
    public static bool Same(string a, string b) => string.Equals(Key(a), Key(b), StringComparison.Ordinal);

    private static string TrimSeparator(string p)
    {
        if (p.Length <= 1) return p;
        var t = p.TrimEnd('\\', '/');
        if (t.Length == 0) return p[..1]; // "/" or "\"
        if (t.Length == 2 && t[1] == ':') return t + "\\"; // a drive root keeps its separator: C:\
        return t;
    }

    /// <summary>Equality, ordering and hashing by <see cref="Key"/>, for path-keyed dictionaries and sets.</summary>
    private sealed class KeyComparer : StringComparer
    {
        public override int Compare(string? x, string? y) =>
            x == null || y == null ? string.CompareOrdinal(x, y) : string.CompareOrdinal(Key(x), Key(y));

        public override bool Equals(string? x, string? y) =>
            x == null || y == null ? ReferenceEquals(x, y) : string.Equals(Key(x), Key(y), StringComparison.Ordinal);

        public override int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(Key(obj));
    }
}
