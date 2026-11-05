namespace FileScape.Core.Model;

/// <summary>What a filesystem entry is, as far as FileScape's traversal policy is concerned.</summary>
public enum NodeKind : byte
{
    Directory,
    File,

    /// <summary>
    /// A reparse point (junction, symbolic link, mount point, or another tag). FileScape shows it but never
    /// traverses it, which rules out cycles and volume crossings (requirement FS-03).
    /// </summary>
    Link,
}

/// <summary>Facts and problems attached to a node. Several can apply at once.</summary>
[Flags]
public enum NodeFlags : uint
{
    None = 0,
    Hidden = 1 << 0,
    System = 1 << 1,
    ReadOnly = 1 << 2,

    /// <summary>The entry is a reparse point. Its target is not followed.</summary>
    ReparsePoint = 1 << 3,

    /// <summary>The reparse point is a directory link (junction, directory symlink, or mount point).</summary>
    DirectoryLink = 1 << 4,

    /// <summary>The cloud-provider attributes RECALL_ON_OPEN, RECALL_ON_DATA_ACCESS, or OFFLINE are set.</summary>
    CloudPlaceholder = 1 << 5,

    /// <summary>
    /// A directory that was not enumerated because enumerating it could make the provider fetch remote data
    /// (RECALL_ON_DATA_ACCESS).
    /// </summary>
    CloudNotTraversed = 1 << 6,

    Sparse = 1 << 7,
    Compressed = 1 << 8,
    Encrypted = 1 << 9,

    /// <summary>Listing the directory's children was refused (access denied).</summary>
    AccessDenied = 1 << 10,

    /// <summary>The entry disappeared between being listed and being enumerated.</summary>
    Vanished = 1 << 11,

    /// <summary>Some other OS error occurred while enumerating. See <see cref="FsNode.Error"/>.</summary>
    EnumerationError = 1 << 12,

    /// <summary>The directory's children are missing or partial (cancellation, node limit, or an error part-way).</summary>
    Incomplete = 1 << 13,

    /// <summary>The data came from a synthetic generator, not the filesystem.</summary>
    Synthetic = 1 << 14,

    /// <summary>A Linux FIFO, socket or device seen through WSL: shown as a file, never opened.</summary>
    LinuxSpecial = 1 << 15,

    /// <summary>An app execution alias (a WindowsApps launcher): a file that runs a program.</summary>
    AppAlias = 1 << 16,

    /// <summary>The volume, share or distribution holding this location could not be reached.</summary>
    Unavailable = 1 << 17,
}

/// <summary>What kind of location root a node is (AD-17). Most nodes are <see cref="None"/>.</summary>
public enum LocationKind : byte
{
    None,

    /// <summary>"This computer": the top of the connected namespace; its children are location roots.</summary>
    Hub,

    /// <summary>A Windows drive (C:\).</summary>
    Drive,

    /// <summary>A WSL distribution through the supported UNC route (\\wsl.localhost\name).</summary>
    Wsl,

    /// <summary>A network share or another UNC root.</summary>
    Network,

    /// <summary>A volume without a drive letter (\\?\Volume{…}\) or another root.</summary>
    Volume,
}

/// <summary>The actual kind of a reparse point (LINK-01), from its reparse tag rather than assumed.</summary>
public enum LinkKind : byte
{
    Junction,
    SymbolicLink,
    MountedVolume,

    /// <summary>A Linux symbolic link seen through \\wsl.localhost (tag 0xA000001D): identified, not followable via Windows.</summary>
    LinuxSymlink,

    /// <summary>A reparse point with another, provider-specific tag.</summary>
    Other,
}

/// <summary>Where a link stands (LINK-06). Unresolved links are connections, not errors.</summary>
public enum LinkStatus : byte
{
    Unresolved,
    Resolved,

    /// <summary>The target does not exist (its volume does).</summary>
    Broken,

    /// <summary>The link or its target could not be read (access denied).</summary>
    Denied,

    /// <summary>The target's volume, share or distribution is not reachable.</summary>
    Unavailable,

    /// <summary>FileScape cannot follow this kind (Linux links through Windows, unknown providers).</summary>
    Unsupported,
}

/// <summary>A link's identity and, once resolved, its destination in the same tree (AD-18).</summary>
public sealed record LinkInfo(LinkKind Kind, uint Tag, string? TargetText, LinkStatus Status = LinkStatus.Unresolved,
    string? TargetPath = null, int TargetId = -1, string? Message = null)
{
    public bool CanFollow => Status is LinkStatus.Unresolved or LinkStatus.Resolved && Kind is not (LinkKind.LinuxSymlink or LinkKind.Other);

    public string KindName => Kind switch
    {
        LinkKind.Junction => "junction",
        LinkKind.SymbolicLink => "symbolic link",
        LinkKind.MountedVolume => "mounted volume",
        LinkKind.LinuxSymlink => "Linux symbolic link",
        _ => $"reparse point (tag 0x{Tag:X8})",
    };
}

/// <summary>Where a directory stands in the scan.</summary>
public enum DirScanState : byte
{
    /// <summary>The node is a file or link, so the state does not apply.</summary>
    NotApplicable,

    /// <summary>Queued but not yet enumerated.</summary>
    Pending,
    Complete,

    /// <summary>Enumeration failed. Flags and <see cref="FsNode.Error"/> say why.</summary>
    Failed,

    /// <summary>Deliberately not traversed (a cloud placeholder directory, a directory link, or the node limit).</summary>
    NotTraversed,

    /// <summary>The scan was cancelled before this directory was enumerated.</summary>
    Cancelled,

    /// <summary>Known to exist but not listed yet: it is listed when visited (on-demand exploration, AD-17).</summary>
    Unlisted,
}

/// <summary>
/// One filesystem entry. A node is identified by <see cref="Id"/>, a dense index into its <see cref="FsTree"/>.
/// The id is stable for the tree's lifetime, so selection and navigation history refer to ids, never to
/// scene objects (architecture decision AD-03).
/// </summary>
public sealed class FsNode
{
    public required int Id { get; init; }

    /// <summary>The parent's id. The root's value is -1.</summary>
    public required int ParentId { get; init; }

    public required string Name { get; init; }
    public required NodeKind Kind { get; init; }
    public NodeFlags Flags { get; internal set; }

    /// <summary>The raw Windows attribute bits as enumerated (0 for synthetic data).</summary>
    public FileAttributes Attributes { get; init; }

    /// <summary>The logical file length in bytes, as reported by directory enumeration. Directories and links are 0.</summary>
    public long Size { get; init; }

    public DateTime? CreatedUtc { get; init; }
    public DateTime? ModifiedUtc { get; init; }
    public DateTime? AccessedUtc { get; init; }

    public int Depth { get; init; }
    public DirScanState ScanState { get; internal set; }

    /// <summary>For links: kind, target and resolution (AD-18). Also set for app aliases (files) to explain them.</summary>
    public LinkInfo? Link { get; internal set; }

    /// <summary>For location roots (and the hub, ""): the absolute path this node stands for. Paths of descendants build on it.</summary>
    public string? LocationPath { get; init; }

    public LocationKind Location { get; init; }

    /// <summary>A human-readable OS error for failed or partial enumeration.</summary>
    public string? Error { get; internal set; }

    /// <summary>Child ids in deterministic display order. Always empty for files and links.</summary>
    public List<int> Children { get; } = [];

    // Aggregates are maintained incrementally by FsTree.Apply and cover what has been scanned so far.
    // Hard-linked files count once per name, so totals can double count (FS-07).
    public long DirectFileBytes { get; internal set; }
    public int DirectFileCount { get; internal set; }
    public int DirectDirCount { get; internal set; }
    public long SubtreeBytes { get; internal set; }
    public int SubtreeFileCount { get; internal set; }
    public int SubtreeDirCount { get; internal set; }

    /// <summary>Direct child links (junctions, symlinks, …): shown, never followed into the totals.</summary>
    public int DirectLinkCount { get; internal set; }

    /// <summary>Links anywhere in the subtree (their destinations are not counted, so nothing is counted twice).</summary>
    public int SubtreeLinkCount { get; internal set; }

    /// <summary>Direct entries with the Windows hidden or system attribute. The counts above include them.</summary>
    public int DirectHiddenCount { get; internal set; }

    /// <summary>
    /// Directories in this subtree, this one included, whose own listing is not complete (not listed yet, pending,
    /// partial, denied, failed, unavailable, cloud, beyond the entry limit). Zero means the subtree totals are complete;
    /// otherwise they are what has been found so far — a lower bound (SUM-02). Maintained incrementally, so no label
    /// ever walks the tree to find out.
    /// </summary>
    public int SubtreeUnsettledDirs { get; internal set; }

    /// <summary>True when this directory's own listing is complete and trustworthy (see <see cref="SubtreeUnsettledDirs"/>).</summary>
    public bool IsSettled => Kind == NodeKind.Directory && ScanState == DirScanState.Complete && (Flags & UnsettledFlags) == 0;

    internal const NodeFlags UnsettledFlags = NodeFlags.Incomplete | NodeFlags.AccessDenied | NodeFlags.EnumerationError | NodeFlags.Vanished | NodeFlags.Unavailable | NodeFlags.CloudNotTraversed;

    public bool IsDirectory => Kind == NodeKind.Directory;
    public bool IsHub => Location == LocationKind.Hub;
    public bool IsRoot => ParentId < 0;

    /// <summary>True for directories whose listing cannot be trusted to be complete.</summary>
    public bool IsListingIncomplete =>
        Kind == NodeKind.Directory && ScanState is not DirScanState.Complete;

    public override string ToString() => $"#{Id} {Kind} {Name}";
}
