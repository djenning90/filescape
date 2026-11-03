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

    public bool IsDirectory => Kind == NodeKind.Directory;
    public bool IsRoot => ParentId < 0;

    /// <summary>True for directories whose listing cannot be trusted to be complete.</summary>
    public bool IsListingIncomplete =>
        Kind == NodeKind.Directory && ScanState is not DirScanState.Complete;

    public override string ToString() => $"#{Id} {Kind} {Name}";
}
