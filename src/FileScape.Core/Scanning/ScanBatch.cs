using FileScape.Core.Model;

namespace FileScape.Core.Scanning;

/// <summary>An immutable description of a node as produced by a scan source, on the scan thread.</summary>
public sealed record NodeRecord(
    int Id,
    int ParentId,
    string Name,
    NodeKind Kind,
    NodeFlags Flags,
    FileAttributes Attributes,
    long Size,
    DateTime? CreatedUtc,
    DateTime? ModifiedUtc,
    DateTime? AccessedUtc,
    int Depth,
    DirScanState InitialState,
    LinkInfo? Link = null,
    string? LocationPath = null,
    LocationKind Location = LocationKind.None)
{
    internal FsNode ToNode() => new()
    {
        Id = Id,
        ParentId = ParentId,
        Name = Name,
        Kind = Kind,
        Flags = Flags,
        Attributes = Attributes,
        Size = Size,
        CreatedUtc = CreatedUtc,
        ModifiedUtc = ModifiedUtc,
        AccessedUtc = AccessedUtc,
        Depth = Depth,
        ScanState = InitialState,
        Link = Link,
        LocationPath = LocationPath,
        Location = Location,
    };
}

/// <summary>A change to a directory that has already been emitted: its scan finished, failed, or was skipped.</summary>
public sealed record DirectoryUpdate(int Id, DirScanState State, NodeFlags AddFlags = NodeFlags.None, string? Error = null,
    NodeFlags ClearFlags = NodeFlags.None, bool SortChildren = false);

/// <summary>A link's resolution changed (AD-18).</summary>
public sealed record LinkUpdate(int Id, LinkInfo Link);

/// <summary>The answer to a request (a path to reach, a link to follow): the node reached, or why not.</summary>
public sealed record ExplorerReply(int Ticket, int NodeId, string? Error, int ViaLink = -1);

/// <summary>
/// The unit of transfer from the scan thread to the consumer. New nodes must be in increasing id order across
/// batches. A directory update may only refer to nodes already emitted, in this batch or an earlier one.
/// </summary>
public sealed class ScanBatch
{
    public List<NodeRecord> NewNodes { get; } = [];
    public List<DirectoryUpdate> DirectoryUpdates { get; } = [];
    public List<LinkUpdate> LinkUpdates { get; } = [];
    public List<ExplorerReply> Replies { get; } = [];
    public bool IsEmpty => NewNodes.Count == 0 && DirectoryUpdates.Count == 0 && LinkUpdates.Count == 0 && Replies.Count == 0;
}
