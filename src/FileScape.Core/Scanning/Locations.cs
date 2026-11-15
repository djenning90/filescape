using FileScape.Core.Model;
using Microsoft.Win32;

namespace FileScape.Core.Scanning;

/// <summary>A location root offered by the hub (LOC-01): a drive, a WSL distribution, or another root.</summary>
public sealed record LocationSeed(string Name, string Path, LocationKind Kind, bool CaseSensitive, string? Detail = null);

/// <summary>
/// Discovery of locations and path handling for the connected namespace (AD-17, LOC-01..03). Discovery reads only
/// cheap system information (logical drives, the WSL registry); nothing is listed until it is visited.
/// </summary>
public static class Locations
{
    /// <summary>The canonical prefix of a WSL path (<c>\\wsl$\</c> is rewritten to this, so one folder has one spelling).</summary>
    public const string WslPrefix = @"\\wsl.localhost\";

    /// <summary>Drives and registered WSL distributions on Windows; "/" elsewhere.</summary>
    public static IReadOnlyList<LocationSeed> Discover()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [new LocationSeed("/", "/", LocationKind.Volume, CaseSensitive: true)];
        }

        var list = new List<LocationSeed>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                // DriveType is cheap; IsReady/VolumeLabel can block on network or optical drives, so they are not read here.
                list.Add(new LocationSeed(d.Name, d.Name, LocationKind.Drive, false, d.DriveType switch
                {
                    DriveType.Fixed => "local drive",
                    DriveType.Removable => "removable drive",
                    DriveType.Network => "network drive",
                    DriveType.CDRom => "optical drive",
                    _ => "drive",
                }));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        foreach (var name in WslDistributions())
        {
            list.Add(new LocationSeed(name, WslPrefix + name, LocationKind.Wsl, CaseSensitive: true, "WSL · Linux"));
        }

        return list;
    }

    /// <summary>Registered WSL distribution names (HKCU\…\Lxss), without starting WSL.</summary>
    public static IReadOnlyList<string> WslDistributions()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var names = new List<string>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
            if (key == null) return names;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var d = key.OpenSubKey(sub);
                if (d?.GetValue("DistributionName") is string n && !string.IsNullOrWhiteSpace(n)) names.Add(n);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <summary>The default WSL distribution's name, used for pasted Linux paths such as /home/user.</summary>
    public static string? DefaultWslDistribution()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
            if (key?.GetValue("DefaultDistribution") is string guid)
            {
                using var d = key.OpenSubKey(guid);
                if (d?.GetValue("DistributionName") is string n) return n;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return WslDistributions() is [var first, ..] ? first : null;
    }

    /// <summary>
    /// Normalises a typed or pasted path (LOC-01): quotes and file:// URLs are removed; \\wsl$\ becomes
    /// \\wsl.localhost\; on Windows, /mnt/c/… becomes C:\… and other Linux paths map to the default distribution;
    /// "C:" becomes "C:\". Returns null for empty input or a Linux path without a distribution.
    /// </summary>
    public static string? Normalize(string input, string? defaultDistribution, bool windows)
    {
        var s = input.Trim().Trim('"', '\'').Trim();
        if (s.Length == 0) return null;
        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(s, UriKind.Absolute, out var uri))
        {
            s = uri.IsUnc ? @"\\" + uri.Host + uri.AbsolutePath.Replace('/', '\\') : Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        }

        if (!windows)
        {
            return s.Length > 1 ? s.TrimEnd('/') : s;
        }

        if (s.StartsWith('/') || s.StartsWith('~'))
        {
            // "/mnt/c" or "/mnt/c/…" (index 5 is the drive letter) is WSL's view of a Windows drive: use the drive itself.
            if (s.Length >= 6 && s.StartsWith("/mnt/", StringComparison.Ordinal) && char.IsAsciiLetter(s[5]) && (s.Length == 6 || s[6] == '/'))
            {
                return char.ToUpperInvariant(s[5]) + @":\" + s[Math.Min(s.Length, 7)..].Replace('/', '\\').TrimEnd('\\');
            }

            if (defaultDistribution == null || s.StartsWith('~')) return null;
            var linux = s.TrimEnd('/');
            return WslPrefix + defaultDistribution + linux.Replace('/', '\\');
        }

        s = s.Replace('/', '\\');
        if (s.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase)) s = WslPrefix + s[7..]; // 7 = length of \\wsl$\
        if (s.Length == 2 && s[1] == ':') s += '\\';
        if (s.Length > 3) s = s.TrimEnd('\\');
        // Collapse "." and ".." without touching the filesystem.
        try
        {
            if (OperatingSystem.IsWindows() && s.Length >= 2 && (s[1] == ':' || s.StartsWith(@"\\", StringComparison.Ordinal))) s = Path.GetFullPath(s);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        // Drop a trailing separator, except on a volume root (\\?\Volume{…}\: four separators), whose canonical form keeps it.
        if (s.Length > 3 && !(s.StartsWith(@"\\?\", StringComparison.Ordinal) && s.EndsWith('\\') && s.Count(c => c == '\\') == 4)) s = s.TrimEnd('\\');
        return s;
    }

    /// <summary>
    /// Splits an absolute, normalised path into its root (C:\, \\server\share, \\?\Volume{…}\, /) and its components.
    /// </summary>
    public static (string Root, List<string> Parts) Split(string path)
    {
        string root;
        if (path.StartsWith('/'))
        {
            root = "/";
        }
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            var end = path.IndexOf('\\', 4);
            root = end < 0 ? path + "\\" : path[..(end + 1)];
        }
        else if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var server = path.IndexOf('\\', 2);
            var share = server < 0 ? -1 : path.IndexOf('\\', server + 1);
            root = share < 0 ? path : path[..share];
        }
        else if (path.Length >= 2 && path[1] == ':')
        {
            root = char.ToUpperInvariant(path[0]) + @":\";
        }
        else
        {
            throw new ArgumentException($"Not an absolute path: {path}", nameof(path));
        }

        var rest = path.Length > root.Length ? path[root.Length..] : "";
        var parts = rest.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).ToList();
        // Canonical root forms: "C:\", "\\server\share" (no trailing separator), "\\?\Volume{…}\", "/".
        return (root, parts);
    }

    /// <summary>What kind of location a root (from <see cref="Split"/>) is: a drive, a WSL distribution, a network share or a volume.</summary>
    public static LocationKind KindOf(string root) =>
        root == "/" ? LocationKind.Volume
        : root.StartsWith(WslPrefix, StringComparison.OrdinalIgnoreCase) ? LocationKind.Wsl
        : root.StartsWith(@"\\?\", StringComparison.Ordinal) ? LocationKind.Volume
        : root.StartsWith(@"\\", StringComparison.Ordinal) ? LocationKind.Network
        : LocationKind.Drive;

    /// <summary>Linux file systems are case-sensitive; Windows volumes are treated as case-insensitive (AD-17).</summary>
    public static bool IsCaseSensitive(string root) => root == "/" || root.StartsWith(WslPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>A display name for a root that is not a known location (a network share, a volume).</summary>
    public static string NameOf(string root) =>
        root.StartsWith(WslPrefix, StringComparison.OrdinalIgnoreCase) ? root[WslPrefix.Length..] : root;

    /// <summary>
    /// Broad places are explored on demand only, never traversed in the background (LOC-02, FS-12): location roots, a
    /// user profile, and anything inside cloud-synced folders.
    /// </summary>
    public static bool IsBroad(string path)
    {
        var (root, parts) = Split(path);
        if (parts.Count == 0) return true;
        if (KindOf(root) == LocationKind.Wsl && parts.Count <= 1) return true; // /home etc.
        if (!OperatingSystem.IsWindows()) return parts.Count <= 1;
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile) && (trimmed.Equals(Path.TrimEndingDirectorySeparator(profile), StringComparison.OrdinalIgnoreCase)
                                               || trimmed.Equals(Path.GetDirectoryName(profile), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var v in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var od = Environment.GetEnvironmentVariable(v);
            if (!string.IsNullOrEmpty(od) && trimmed.StartsWith(Path.TrimEndingDirectorySeparator(od), StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
