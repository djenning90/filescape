using FileScape.Core.Model;

namespace FileScape.Core.Planets;

/// <summary>What kind of place a world is, for its music and its look (PLANET-27, AD-48). Separate from
/// <see cref="PlanetTheme"/>, which comes from what the folder holds (pictures, documents…).</summary>
public enum WorldKind
{
    /// <summary>"This computer", or the folder you started from when there is no hub above it: the calm crossroads.</summary>
    Hub,

    /// <summary>A Windows volume root (a drive, a volume without a letter, a share).</summary>
    WindowsRoot,

    /// <summary>A WSL distribution's root (\\wsl.localhost\name).</summary>
    WslRoot,

    /// <summary>A repository root: a <c>.git</c> folder, or a <c>.git</c> file (a worktree or submodule marker), among its entries.</summary>
    Repository,

    /// <summary>
    /// A hazard world (PLANET-34, AD-53): a system-managed location that warrants a caution cue, or the sample hazard. Its
    /// planet is a broken shell over a spinning black hole; the route is safe, the edges are not. Classification is
    /// <c>HazardRules</c>' job (names, platform and path context only); falling is a navigation event and never touches files.
    /// </summary>
    Hazard,

    /// <summary>Any other folder: its feel comes from its <see cref="WorldCharacter.Band"/>.</summary>
    Folder,
}

/// <summary>
/// A world's character: its <see cref="Kind"/>, its depth below the root it belongs to, the depth band (1–4) that sets an
/// ordinary folder's feel, and the repository it is inside (if any), so a repository's child folders can keep a quiet echo
/// of it. Names only: nothing is read from files (FS-01).
/// <para>
/// It sits beside <see cref="PlanetTheme"/> (what the folder holds): the theme keeps the ground, palette and props, and
/// the character adds the sky, the sun, one arrival prop and the music (AD-48, AD-49; <c>PlanetArt.Character.cs</c>,
/// <c>SkySynth</c>). A repository also turns the overlook into the workbench (<see cref="PlanetComposer.Compose"/>, AD-50).
/// </para>
/// </summary>
/// <param name="Kind">What the place is.</param>
/// <param name="Depth">Levels below the root it belongs to (a location root, or the starting folder): 0 at that root.</param>
/// <param name="Band">For folders: 1 just inside a root (depth 1), 2 at depth 2–3, 3 at 4–5, 4 from depth 6 on; 0 for the others.</param>
/// <param name="RepositoryId">The repository this folder is inside (the nearest ancestor that is one), −1 when none; for a repository root, itself.</param>
/// <param name="Worktree">For a repository: its <c>.git</c> is a file (a linked worktree or submodule), not a folder.</param>
public readonly record struct WorldCharacter(WorldKind Kind, int Depth, int Band, int RepositoryId, bool Worktree = false)
{
    /// <summary>The deepest band.</summary>
    public const int MaxBand = 4;

    /// <summary>Inside a repository but not its root (a child folder of a project).</summary>
    public bool InsideRepository => Kind != WorldKind.Repository && RepositoryId >= 0;

    /// <summary>A short name for the character, for the HUD and probes ("Windows volume", "repository", "depth band 2"…).</summary>
    public string Describe() => Kind switch
    {
        WorldKind.Hub => "the hub",
        WorldKind.WindowsRoot => "a Windows volume",
        WorldKind.WslRoot => "a Linux distribution",
        WorldKind.Repository => Worktree ? "a repository (worktree)" : "a repository",
        WorldKind.Hazard => "a broken world (handle with care)",
        _ => BandName(Band) + (InsideRepository ? " · in a project" : ""),
    };

    /// <summary>The bands' names: a deep tree reads as a journey through them.</summary>
    public static string BandName(int band) => band switch
    {
        1 => "the outskirts",
        2 => "the trails",
        3 => "the deep woods",
        _ => "the underglow",
    };

    /// <summary>The band for a depth below the root: 1 → 1, 2–3 → 2, 4–5 → 3, 6+ → 4 (0 for the root itself).</summary>
    /// <remarks>The bands widen with depth (one level, two, two, then the rest), so the feel changes less often the deeper you go; the bands are AD-48's.</remarks>
    public static int BandOf(int depth) => depth <= 0 ? 0 : depth == 1 ? 1 : depth <= 3 ? 2 : depth <= 5 ? 3 : MaxBand;

    /// <summary>
    /// The character of <paramref name="folderId"/>. <paramref name="startId"/> is where the navigator started (its root):
    /// it counts as the hub (unless it is a location root or a repository itself), and depth is measured from it when no
    /// location root lies between. Links take the character of their target when it is in the tree.
    /// </summary>
    public static WorldCharacter Of(FsTree tree, int folderId, int startId = -1)
    {
        if (!tree.Contains(folderId)) return new WorldCharacter(WorldKind.Folder, 0, 1, -1);
        var node = tree[folderId];
        if (node.Kind == NodeKind.Link && node.Link is { Status: LinkStatus.Resolved, TargetId: >= 0 } li && tree.Contains(li.TargetId) && li.TargetId != folderId)
        {
            node = tree[li.TargetId];
            folderId = node.Id;
        }

        var repo = RepositoryMarker(tree, node);
        if (node.IsHub) return new WorldCharacter(WorldKind.Hub, 0, 0, -1);
        // The starting folder (the navigator's root) is your home base, whatever the tree shows above it for context (a
        // path opened directly still has This computer and its drive above it): it is the hub unless it is itself the hub,
        // a location root or a repository, and depth counts from it (2026-11-14 review: deliberate, see AD-48).

        // Depth below the nearest location root or the starting folder; the nearest repository on the way up.
        var depth = 0;
        var repositoryId = repo != null ? folderId : -1;
        var rootFound = node.Location is not LocationKind.None || folderId == startId;
        if (!rootFound)
        {
            foreach (var a in tree.Ancestors(folderId))
            {
                depth++;
                var an = tree[a];
                if (repositoryId < 0 && RepositoryMarker(tree, an) != null) repositoryId = a;
                if (an.Location is not LocationKind.None || an.IsHub || a == startId)
                {
                    rootFound = true;
                    if (an.IsHub) depth--; // the hub's children are the roots
                    break;
                }
            }
        }

        if (repo is { } marker) return new WorldCharacter(WorldKind.Repository, depth, 0, folderId, Worktree: marker.Kind == NodeKind.File);
        if (node.Location is LocationKind.Wsl) return new WorldCharacter(WorldKind.WslRoot, 0, 0, -1);
        if (node.Location is LocationKind.Drive or LocationKind.Volume or LocationKind.Network) return new WorldCharacter(WorldKind.WindowsRoot, 0, 0, -1);
        if (folderId == startId || (!rootFound && node.ParentId < 0)) return new WorldCharacter(WorldKind.Hub, 0, 0, -1);
        return new WorldCharacter(WorldKind.Folder, depth, Math.Max(1, BandOf(depth)), repositoryId);
    }

    /// <summary>The folder's own <c>.git</c> entry (a folder, or a file for a worktree or submodule), or null.</summary>
    public static FsNode? RepositoryMarker(FsTree tree, FsNode folder)
    {
        if (!folder.IsDirectory && folder.Kind != NodeKind.Link) return null;
        foreach (var c in folder.Children)
        {
            var n = tree[c];
            if (n.Name.Equals(".git", StringComparison.Ordinal) && (n.IsDirectory || n.Kind == NodeKind.File)) return n;
        }

        return null;
    }
}
