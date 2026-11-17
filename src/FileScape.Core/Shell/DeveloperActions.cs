using FileScape.Core.Interaction;
using FileScape.Core.Scanning;
using Microsoft.Win32;

namespace FileScape.Core.Shell;

/// <summary>
/// One way to open a terminal in a folder (PLANET-45, AD-58), described before anything runs: the program, its separate
/// arguments (joined with <see cref="WindowsShellActions.CommandLine"/> when started), and where it starts, as the
/// terminal will show it.
/// </summary>
/// <param name="Label">The button's text ("Open terminal here").</param>
/// <param name="Program">The program started (wt.exe, powershell.exe, wsl.exe).</param>
/// <param name="Args">Its arguments, each one separate (quoting is the shell action's job).</param>
/// <param name="Explanation">What it will do, for the button's tooltip.</param>
/// <param name="StartsIn">The directory the shell starts in, in that shell's own form (C:\… or /home/…).</param>
public sealed record TerminalCommand(string Label, string Program, IReadOnlyList<string> Args, string Explanation, string StartsIn);

/// <summary>
/// Opening a terminal in a folder, platform-aware (PLANET-45, AD-58): a Windows folder opens in Windows Terminal
/// (<c>wt.exe -d</c>) when it is installed, otherwise in Windows PowerShell; a WSL folder opens its own distribution's
/// shell through <c>wsl.exe -d &lt;distro&gt; --cd &lt;linux path&gt;</c> (hosted by whatever the default terminal is).
/// Only an explicit player command starts one (the landmark panel's button or key), never walking.
/// <para>
/// Preserve: the directory is always an argument of its own (never pasted into a command string), except PowerShell's
/// <c>Set-Location</c>, which quotes it as a single-quoted literal (<see cref="SetLocation"/>). The Windows-only tests
/// start these very argument lists (with a command that prints the directory instead of staying open) in fixture
/// folders whose names have spaces and an apostrophe, and check the directory the shell reports.
/// </para>
/// </summary>
public static class TerminalLaunch
{
    /// <summary>The WSL launcher (System32, on PATH).</summary>
    public const string WslExe = "wsl.exe";

    /// <summary>Windows PowerShell 5.1, present on every Windows 10/11 (on PATH).</summary>
    public const string PowerShellExe = "powershell.exe";

    /// <summary>
    /// The terminal for <paramref name="path"/> (a folder): WSL paths through <see cref="WslArgs"/>, drive and UNC paths
    /// through Windows Terminal when <paramref name="windowsTerminal"/> (its path) is known, else PowerShell; null for
    /// anything else (synthetic data, a POSIX path on Linux).
    /// </summary>
    public static TerminalCommand? For(string path, string? windowsTerminal)
    {
        switch (EditorLaunch.PlatformOf(path))
        {
            case ProjectPlatform.Wsl when EditorLaunch.Distribution(path) is { } distro && ListAhead.LinuxPath(path) is { } linux:
                return new TerminalCommand("Open terminal here", WslExe, WslArgs(distro, linux),
                    $"Opens a {distro} shell in {linux} (wsl.exe -d {distro} --cd …).", linux);
            case ProjectPlatform.Windows:
                var dir = WindowsDirectory(path);
                return windowsTerminal != null
                    ? new TerminalCommand("Open terminal here", windowsTerminal, WindowsTerminalArgs(dir), $"Opens Windows Terminal in {dir}.", dir)
                    : new TerminalCommand("Open terminal here", PowerShellExe, PowerShellArgs(dir, keepOpen: true), $"Opens Windows PowerShell in {dir} (Windows Terminal is not installed).", dir);
            default:
                return null;
        }
    }

    /// <summary><c>-d &lt;distro&gt; --cd &lt;linux path&gt;</c>, then <paramref name="then"/> (a command to run there instead of
    /// the login shell; the tests pass <c>pwd</c>).</summary>
    public static IReadOnlyList<string> WslArgs(string distro, string linuxPath, IReadOnlyList<string>? then = null) =>
        ["-d", distro, "--cd", linuxPath, .. then ?? []];

    /// <summary>
    /// <c>-d &lt;dir&gt;</c>, Windows Terminal's starting directory. Windows Terminal splits its command line at <c>;</c>
    /// (a new tab), so a semicolon in the path is escaped as <c>\;</c>, its documented escape.
    /// </summary>
    public static IReadOnlyList<string> WindowsTerminalArgs(string dir) => ["-d", dir.Replace(";", "\\;")];

    /// <summary>
    /// Windows PowerShell started in <paramref name="dir"/>: <c>-NoExit</c> keeps it open for the person; the tests use
    /// <paramref name="keepOpen"/> false with <paramref name="then"/> printing the location, so the same
    /// <see cref="SetLocation"/> is what they check.
    /// </summary>
    public static IReadOnlyList<string> PowerShellArgs(string dir, bool keepOpen, string? then = null) =>
        ["-NoLogo", keepOpen ? "-NoExit" : "-NoProfile", "-Command", SetLocation(dir) + (then != null ? "; " + then : "")];

    /// <summary><c>Set-Location -LiteralPath '…'</c>: a single-quoted literal (an apostrophe doubled), so no character of the
    /// path is a wildcard, a variable or a command.</summary>
    public static string SetLocation(string dir) => $"Set-Location -LiteralPath '{dir.Replace("'", "''")}'";

    /// <summary>A drive root keeps its separator (<c>C:\</c>; <c>C:</c> alone would mean "the current directory on C:").</summary>
    private static string WindowsDirectory(string path) => path.Length == 2 && path[1] == ':' ? path + "\\" : path;

    /// <summary>Windows Terminal's execution alias for this user, if it is installed (<c>%LOCALAPPDATA%\Microsoft\WindowsApps\wt.exe</c>); null otherwise.</summary>
    public static string? FindWindowsTerminal()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local)) return null;
        var alias = Path.Combine(local, "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(alias) ? alias : null;
    }
}

/// <summary>One spelling of a path to copy (PLANET-45): what it is for and the text.</summary>
public sealed record PathForm(string Label, string Text);

/// <summary>
/// The forms of a folder's path a developer pastes (PLANET-45, AD-58): the native one first (<c>C:\…</c> for Windows,
/// <c>/home/…</c> for a WSL folder), then the other side's (<c>\\wsl.localhost\Distro\…</c> for a WSL folder, <c>/mnt/c/…</c>
/// for a Windows drive). A network share has only its own.
/// </summary>
public static class PathForms
{
    public static IReadOnlyList<PathForm> For(string path)
    {
        switch (EditorLaunch.PlatformOf(path))
        {
            case ProjectPlatform.Wsl when ListAhead.LinuxPath(path) is { } linux:
                var unc = PathIdentity.Normalize(path);
                if (unc.Count(c => c == '\\') == 3) unc += "\\"; // \\wsl.localhost\Distro → \\wsl.localhost\Distro\ (the root)
                return [new PathForm("Linux path", linux), new PathForm("Windows path to it", unc)];
            case ProjectPlatform.Windows:
                var native = path.Length == 2 && path[1] == ':' ? path + "\\" : path;
                return MountPath(native) is { } mnt ? [new PathForm("Windows path", native), new PathForm("From WSL", mnt)] : [new PathForm("Windows path", native)];
            default:
                return [new PathForm("Path", path)];
        }
    }

    /// <summary>The WSL mount path of a drive path (<c>C:\a b</c> → <c>/mnt/c/a b</c>, <c>C:\</c> → <c>/mnt/c</c>); null for UNC paths.</summary>
    public static string? MountPath(string windowsPath)
    {
        if (windowsPath.Length < 2 || windowsPath[1] != ':' || !char.IsAsciiLetter(windowsPath[0])) return null;
        var rest = windowsPath[2..].Replace('\\', '/').TrimEnd('/');
        return "/mnt/" + char.ToLowerInvariant(windowsPath[0]) + rest;
    }
}

/// <summary>A useful place on a filesystem, offered by its root's landmark (PLANET-45): a label, the folder (a Windows-side path FileScape can travel to) and a short detail.</summary>
public sealed record RootPlace(string Label, string Path, string Detail);

/// <summary>
/// A few useful places on a root's filesystem (PLANET-45, AD-58), each a trip, not a listing: on a WSL distribution your
/// home (<c>~</c>), <c>/tmp</c>, <c>/mnt/c</c> (offered as the Windows drive itself, which FileScape lists natively rather
/// than through WSL's file server) and the root; on Windows the drive's root and the folders Windows names (Home,
/// Desktop, Documents, Downloads), from the same known-folder map that ranks prominent folders. Nothing is listed here.
/// </summary>
public static class RootPlaces
{
    /// <summary>A WSL distribution's places. <paramref name="currentLinux"/> is where you are (its own entry is left out).</summary>
    public static IReadOnlyList<RootPlace> ForWsl(string distro, string currentLinux)
    {
        var root = Locations.WslPrefix + distro;
        var list = new List<RootPlace>
        {
            new("~  home", WslHomeMarker, $"your Linux home in {distro}"),
            new("/tmp", root + @"\tmp", "temporary files"),
            new("/mnt/c", @"C:\", "the Windows C: drive (opened as C:\\)"),
            new("/  the root", root + "\\", $"the top of {distro}"),
        };
        return list.Where(p => p.Path == WslHomeMarker || ListAhead.LinuxPath(p.Path) != currentLinux || !p.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Stands for "this distribution's home" in <see cref="ForWsl"/>: the app resolves it only when chosen
    /// (<see cref="WslHome.Resolve"/>), so opening the panel reads nothing.
    /// </summary>
    public const string WslHomeMarker = "~";

    /// <summary>Windows places: the drive's root (unless you are there) and the named folders present in <paramref name="knownFolders"/> (path → name).</summary>
    public static IReadOnlyList<RootPlace> ForWindows(string current, IReadOnlyDictionary<string, string> knownFolders)
    {
        var list = new List<RootPlace>();
        if (current.Length >= 2 && current[1] == ':')
        {
            var drive = current[..2] + "\\";
            if (!PathIdentity.Same(drive, current)) list.Add(new RootPlace(drive, drive, "the top of this drive"));
        }

        foreach (var name in new[] { "Home", "Desktop", "Documents", "Downloads" })
        {
            var hit = knownFolders.FirstOrDefault(kv => kv.Value == name);
            if (hit.Key is { } p && !PathIdentity.Same(p, current)) list.Add(new RootPlace(name, p, name == "Home" ? "your user profile" : $"your {name} folder"));
        }

        return list;
    }
}

/// <summary>
/// A WSL distribution's default user's home (PLANET-45): the user WSL logs in as (<c>DefaultUid</c> in the Lxss registry
/// key, as <see cref="Locations.WslDistributions"/> reads names) looked up in the distribution's <c>/etc/passwd</c>, read
/// only when the person chooses "~" and bounded in size. Falls back to <c>/home</c>.
/// </summary>
public static class WslHome
{
    /// <summary>At most this much of /etc/passwd is read, bytes (64 KiB: thousands of accounts).</summary>
    public const int MaxPasswdBytes = 64 * 1024;

    /// <summary>The home directory of <paramref name="uid"/> in passwd text (<c>name:x:uid:gid:gecos:home:shell</c>), or null.</summary>
    public static string? FromPasswd(string passwd, int uid)
    {
        foreach (var raw in passwd.Split('\n'))
        {
            var f = raw.TrimEnd('\r').Split(':');
            if (f.Length >= 7 && int.TryParse(f[2], out var u) && u == uid && f[5].StartsWith('/')) return f[5];
        }

        return null;
    }

    /// <summary>The distribution's default user id from the registry (Windows only), or null when it is not recorded.</summary>
    public static int? DefaultUid(string distro)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
            if (key == null) return null;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var d = key.OpenSubKey(sub);
                if (d?.GetValue("DistributionName") is string n && n.Equals(distro, StringComparison.OrdinalIgnoreCase) && d.GetValue("DefaultUid") is int uid) return uid;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return null;
    }

    /// <summary>The Windows-side path of the distribution's default user's home (<c>\\wsl.localhost\Distro\home\dj</c>), or its <c>/home</c>.</summary>
    public static string Resolve(string distro)
    {
        var root = Locations.WslPrefix + distro;
        var fallback = root + @"\home";
        if (DefaultUid(distro) is not { } uid) return fallback;
        try
        {
            using var f = new FileStream(root + @"\etc\passwd", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[Math.Min(MaxPasswdBytes, (int)Math.Min(f.Length, int.MaxValue))];
            var n = 0;
            while (n < buf.Length && f.Read(buf, n, buf.Length - n) is var r and > 0) n += r;
            var home = FromPasswd(System.Text.Encoding.UTF8.GetString(buf, 0, n), uid);
            return home == null ? fallback : root + home.Replace('/', '\\');
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return fallback;
        }
    }
}
