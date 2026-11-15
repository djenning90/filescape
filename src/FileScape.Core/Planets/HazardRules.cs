using System.Text.RegularExpressions;
using FileScape.Core.Model;

namespace FileScape.Core.Planets;

/// <summary>Which of <see cref="HazardRules"/>' rules made a folder a hazard world.</summary>
public enum HazardRule
{
    /// <summary>A system-managed folder directly under a Windows volume root (C:\Windows, C:\Program Files…).</summary>
    WindowsSystem,

    /// <summary>A kernel or runtime folder directly under a Linux root (a WSL distribution's / or a POSIX "/").</summary>
    LinuxSystem,

    /// <summary>The sample hazard: a folder marked <see cref="HazardRules.SampleMarkerName"/> inside a FileScape sample.</summary>
    Sample,
}

/// <summary>Why a folder is a hazard world: the rule, the matched name, and one plain sentence for the HUD and probes.</summary>
/// <param name="Rule">The rule that matched.</param>
/// <param name="Name">The folder's name as matched (the rule's spelling, e.g. "Program Files").</param>
/// <param name="Why">What the place is and why it gets a caution cue, in one sentence.</param>
public sealed record HazardReason(HazardRule Rule, string Name, string Why);

/// <summary>
/// Decides which folders are hazard worlds (PLANET-34, AD-53): a small, explainable set of system-managed locations,
/// matched by <b>platform and path context</b>, never by a name alone, plus the sample hazard in the Starter Sky.
/// <para>
/// The rules (the table and its reasons are in docs/decisions.md AD-53):
/// <list type="bullet">
/// <item><b>Windows</b>: <see cref="WindowsNames"/>, compared case-insensitively (Windows names are), and only for a folder
/// whose parent is a Windows volume root: a <see cref="LocationKind.Drive"/> or <see cref="LocationKind.Volume"/> location
/// (not "/", not a network share), or a tree root whose path is a drive root such as <c>C:\</c>. D:\projects\Windows is an
/// ordinary folder.</item>
/// <item><b>Linux</b>: <see cref="LinuxNames"/>, compared case-sensitively (Linux names are), and only for a folder whose
/// parent is a Linux root: a <see cref="LocationKind.Wsl"/> distribution (\\wsl.localhost\name) or the POSIX root "/".
/// A repository's "proc" folder is an ordinary folder.</item>
/// <item><b>Sample</b>: a folder holding a file named <see cref="SampleMarkerName"/>, inside a FileScape sample (an
/// ancestor holds the sample marker <c>.filescape-sample</c>). The marker means nothing anywhere else, so no folder
/// becomes a hazard by accident.</item>
/// </list>
/// Only the matched folder is a hazard; the folders inside it are ordinary worlds (C:\Windows\System32 is not broken).
/// </para>
/// Names only: nothing is read from disk (FS-01), so classification is free and the same on every machine. Being a hazard
/// changes only how the world looks and what happens when you step off an edge (a navigation event); it never changes
/// file controls, permissions or what any action does.
/// </summary>
public static class HazardRules
{
    /// <summary>The marker file that makes a folder inside a FileScape sample a hazard (the Starter Sky's Event Horizon).</summary>
    public const string SampleMarkerName = ".filescape-hazard";

    /// <summary>The sample's own marker (the same name as <c>SampleSky.MarkerName</c>; Core's planets do not depend on the sample writer).</summary>
    public const string SampleRootMarkerName = ".filescape-sample";

    /// <summary>System-managed folders directly under a Windows volume root, with why each warrants a caution cue.</summary>
    public static readonly IReadOnlyList<(string Name, string Why)> WindowsNames =
    [
        ("Windows", "the operating system's own files; changing them can stop Windows from starting"),
        ("Program Files", "installed 64-bit programs, managed by their installers"),
        ("Program Files (x86)", "installed 32-bit programs, managed by their installers"),
        ("ProgramData", "machine-wide program data and settings shared by every user"),
        ("$Recycle.Bin", "the Recycle Bin's hidden per-user stores"),
        ("System Volume Information", "restore points, shadow copies and indexing data, kept by Windows"),
        ("Recovery", "the Windows Recovery Environment, used to repair a PC that will not start"),
    ];

    /// <summary>Kernel and runtime folders directly under a Linux root, with why each warrants a caution cue.</summary>
    public static readonly IReadOnlyList<(string Name, string Why)> LinuxNames =
    [
        ("proc", "the kernel's live view of processes and settings; its files are not files on disk"),
        ("sys", "the kernel's live view of devices and drivers"),
        ("dev", "device files: reading or writing them talks to hardware"),
        ("boot", "the kernel and boot loader the system starts from"),
        ("run", "runtime state of running services (sockets, locks), rebuilt at every start"),
    ];

    /// <summary>A Windows drive root path: a letter, a colon and an optional separator (C:, C:\, c:/).</summary>
    private static readonly Regex DriveRoot = new(@"^[A-Za-z]:[\\/]?$", RegexOptions.CultureInvariant);

    /// <summary>A WSL distribution root path: \\wsl.localhost\name or \\wsl$\name, with an optional trailing separator.</summary>
    private static readonly Regex WslRoot = new(@"^\\\\wsl(\.localhost|\$)\\[^\\]+\\?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// The reason <paramref name="folderId"/> is a hazard world, or null. A directory link is classified by its own
    /// place, not its target (the composer only composes directories). Cheap: one parent lookup, and for the sample rule
    /// a walk up the ancestors.
    /// </summary>
    public static HazardReason? Classify(FsTree tree, int folderId)
    {
        if (!tree.Contains(folderId)) return null;
        var node = tree[folderId];
        if (!node.IsDirectory || node.IsHub || node.Location is not LocationKind.None) return null;
        if (node.ParentId >= 0 && tree.Contains(node.ParentId))
        {
            var parent = tree[node.ParentId];
            if (IsWindowsVolumeRoot(tree, parent))
            {
                foreach (var (name, why) in WindowsNames)
                {
                    if (node.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return new HazardReason(HazardRule.WindowsSystem, name, $"{name}: {why}.");
                }
            }
            else if (IsLinuxRoot(tree, parent))
            {
                foreach (var (name, why) in LinuxNames)
                {
                    if (node.Name.Equals(name, StringComparison.Ordinal)) return new HazardReason(HazardRule.LinuxSystem, name, $"/{name}: {why}.");
                }
            }
        }

        if (HasFile(tree, node, SampleMarkerName) && InsideSample(tree, folderId))
        {
            return new HazardReason(HazardRule.Sample, node.Name, "the Starter Sky's sample hazard: a broken world to practise falling off, safely.");
        }

        return null;
    }

    /// <summary>True when <paramref name="folderId"/> is a hazard world (<see cref="Classify"/> is not null).</summary>
    public static bool IsHazard(FsTree tree, int folderId) => Classify(tree, folderId) != null;

    /// <summary>
    /// A Windows volume root: a drive or a volume location (not the POSIX "/", which is also a Volume location), or a tree
    /// root opened by a drive path. A network share is not: its top folders are someone's shared folders, not an
    /// installed system.
    /// </summary>
    private static bool IsWindowsVolumeRoot(FsTree tree, FsNode n)
    {
        if (n.Location is LocationKind.Drive) return true;
        if (n.Location is LocationKind.Volume) return !IsPosixRoot(n);
        return n.Location is LocationKind.None && n.IsRoot && DriveRoot.IsMatch(tree.FullPath(n.Id));
    }

    /// <summary>A Linux root: a WSL distribution location, the POSIX root "/", or a tree root opened by a WSL distribution path.</summary>
    private static bool IsLinuxRoot(FsTree tree, FsNode n)
    {
        if (n.Location is LocationKind.Wsl) return true;
        if (n.Location is LocationKind.Volume) return IsPosixRoot(n);
        if (n.Location is not LocationKind.None || !n.IsRoot) return false;
        var path = tree.FullPath(n.Id);
        return path == "/" || WslRoot.IsMatch(path);
    }

    /// <summary>The POSIX root location ("/", as <c>Locations.Discover</c> offers it off Windows): its path, or its name, is "/".</summary>
    private static bool IsPosixRoot(FsNode n) => (n.LocationPath ?? n.Name) == "/";

    /// <summary>The folder lists a file of exactly this name (ordinal: the markers are FileScape's own spelling).</summary>
    private static bool HasFile(FsTree tree, FsNode folder, string name)
    {
        foreach (var c in folder.Children)
        {
            var n = tree[c];
            if (n.Kind == NodeKind.File && n.Name.Equals(name, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>A proper ancestor of the folder holds the sample marker: the folder is inside a FileScape sample.</summary>
    private static bool InsideSample(FsTree tree, int folderId)
    {
        foreach (var a in tree.Ancestors(folderId))
        {
            if (HasFile(tree, tree[a], SampleRootMarkerName)) return true;
        }

        return false;
    }
}
