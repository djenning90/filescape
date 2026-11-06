namespace FileScape.Core.Scanning;

/// <summary>How far ahead of the user a folder would be listed.</summary>
public enum ListAheadKind
{
    /// <summary>A destination of the place you are in (a gate, or the way back): listed so its gate shows its state.</summary>
    Destination,

    /// <summary>Further context (a destination's own child folders), listed only when nothing else waits.</summary>
    Context,

    /// <summary>Background traversal of the arrival folder's subtree.</summary>
    Background,
}

/// <summary>
/// LIST-02 (AD-31): which folders FileScape may list before the user goes there. Deliberate requests (entering a place,
/// a typed path, a followed link, Retry) are never refused here; this only bounds what is listed <em>ahead</em>, so idle
/// listing cannot become a crawl of the whole computer. Refused folders stay "not listed yet" with the reason as their
/// note, and are listed one level when entered.
/// </summary>
public static class ListAhead
{
    private static readonly (string Path, string Why)[] LinuxSpecial =
    [
        ("/proc", "a Linux system filesystem (live process and kernel data)"),
        ("/sys", "a Linux system filesystem (live kernel and device data)"),
        ("/dev", "Linux device files"),
        ("/run", "Linux runtime state"),
        ("/mnt", "mounted aliases of other locations (/mnt/c is drive C:)"),
        ("/media", "mounted removable media"),
        ("/snap", "mounted snap packages"),
        ("/lost+found", "filesystem recovery space"),
    ];

    /// <summary>Null when the folder may be listed ahead; otherwise why not (shown as its note).</summary>
    public static string? Refusal(string path, ListAheadKind kind)
    {
        if (LinuxPath(path) is { } linux)
        {
            foreach (var (p, why) in LinuxSpecial)
            {
                if (linux == p || linux.StartsWith(p + "/", StringComparison.Ordinal))
                {
                    return $"Not listed ahead: {p} is {why}. Enter it to list it.";
                }
            }

            return null;
        }

        if (kind != ListAheadKind.Destination && IsNetworkShare(path))
        {
            return "Not listed ahead: a network share is listed only as far as you go, so a slow server cannot hold anything up.";
        }

        return null;
    }

    /// <summary>The Linux path of a WSL (<c>\\wsl.localhost\Distro\…</c>, <c>\\wsl$\Distro\…</c>) or POSIX path; null otherwise.</summary>
    public static string? LinuxPath(string path)
    {
        if (path.StartsWith('/')) return path.Length > 1 ? path.TrimEnd('/') : "/";
        foreach (var prefix in new[] { @"\\wsl.localhost\", @"\\wsl$\" })
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = path[prefix.Length..];
            var slash = rest.IndexOf('\\');
            return slash < 0 ? "/" : "/" + rest[(slash + 1)..].Replace('\\', '/').TrimEnd('/');
        }

        return null;
    }

    private static bool IsNetworkShare(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal) && LinuxPath(path) == null;
}
