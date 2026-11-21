using System.Text;
using FileScape.Core.Scanning;

namespace FileScape.Core.Model;

/// <summary>Whether the data shown is the live filesystem or generated sample data (requirement UX-06).</summary>
public enum DataOrigin
{
    LiveFilesystem,
    Synthetic,
}

/// <summary>Describes where a tree's data came from. The UI shows this permanently.</summary>
public sealed record DataSourceInfo(DataOrigin Origin, string RootDisplayPath, string Description)
{
    public bool IsLive => Origin == DataOrigin.LiveFilesystem;

    /// <summary>
    /// The "now" that file ages are measured against. Live scans use the scan's start time. Synthetic data uses
    /// a fixed date, so its colours are reproducible.
    /// </summary>
    public DateTime AgeReferenceUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// The node store for one scan. Nodes arrive in <see cref="ScanBatch"/>es in strictly increasing id order and
/// are applied on the consuming thread only (the Godot main thread in the app). The type is not thread-safe
/// by design: the scanner never touches it (AD-04). One exception, explicit: a worker may read it while it holds a
/// <see cref="BeginBackgroundRead"/> lease (PLANET-176, AD-96: planets composed off the main thread), and
/// <see cref="Apply"/> refuses to change the tree while any lease is held, so the consumer must defer batches then
/// (<see cref="BackgroundReaders"/>).
/// <para>
/// Nodes are only ever appended, never removed or renumbered, so an id stays valid for the tree's lifetime and parents
/// always have smaller ids than their children. Aggregates (sizes, counts, unsettled directories) are updated up the
/// ancestor chain as each node arrives, so labels and layouts read them in O(1) instead of walking subtrees. A fresh
/// start (a new world, Locations) builds a new tree rather than clearing this one.
/// </para>
/// </summary>
public sealed class FsTree
{
    private readonly List<FsNode> _nodes = [];

    public FsTree(DataSourceInfo source)
    {
        Source = source;
    }

    public DataSourceInfo Source { get; }
    public int Count => _nodes.Count;
    public FsNode Root => _nodes[0];
    public IReadOnlyList<FsNode> Nodes => _nodes;

    /// <summary>Increments on every applied batch, so consumers can tell whether layout is stale.</summary>
    public int Version { get; private set; }

    /// <summary>Nodes currently carrying a problem flag (<see cref="IsProblem"/>), for the status line; kept up to date as retries clear them.</summary>
    public int FlaggedProblemCount { get; private set; }

    public FsNode this[int id] => _nodes[id];

    private int _backgroundReaders;

    /// <summary>Workers reading the tree now (leases from <see cref="BeginBackgroundRead"/>); while above 0, <see cref="Apply"/> throws.</summary>
    public int BackgroundReaders => Volatile.Read(ref _backgroundReaders);

    /// <summary>
    /// A lease for reading the tree on another thread (taken on the consuming thread before the worker starts, ended
    /// by disposing it, from any thread, once). While held, the tree does not change: <see cref="Apply"/> throws.
    /// </summary>
    public IDisposable BeginBackgroundRead()
    {
        Interlocked.Increment(ref _backgroundReaders);
        return new ReadLease(this);
    }

    private sealed class ReadLease(FsTree tree) : IDisposable
    {
        private int _ended;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0) Interlocked.Decrement(ref tree._backgroundReaders);
        }
    }

    public bool Contains(int id) => id >= 0 && id < _nodes.Count;

    /// <summary>
    /// Applies one scanner batch: appends new nodes, then applies directory state updates, then link updates, and bumps
    /// <see cref="Version"/>. Throws if a new node's id is not the next one (a source bug: ids must arrive in order).
    /// </summary>
    public void Apply(ScanBatch batch)
    {
        if (BackgroundReaders > 0) throw new InvalidOperationException("The tree is being read on a worker (BeginBackgroundRead); apply batches after the lease ends.");
        foreach (var record in batch.NewNodes)
        {
            if (record.Id != _nodes.Count)
            {
                throw new InvalidOperationException(
                    $"Scan batch out of order: expected id {_nodes.Count}, got {record.Id}.");
            }

            var node = record.ToNode();
            _nodes.Add(node);
            if (node.ParentId >= 0)
            {
                var parent = _nodes[node.ParentId];
                parent.Children.Add(node.Id);
                AddToAggregates(parent, node);
            }

            if (node.Kind == NodeKind.Directory && !node.IsSettled) AddUnsettled(node, 1);

            if (IsProblem(node.Flags))
            {
                FlaggedProblemCount++;
            }
        }

        foreach (var update in batch.DirectoryUpdates)
        {
            var dir = _nodes[update.Id];
            var wasProblem = IsProblem(dir.Flags);
            var wasSettled = dir.IsSettled;
            dir.ScanState = update.State;
            dir.Flags = (dir.Flags & ~update.ClearFlags) | update.AddFlags;
            if ((update.ClearFlags & (NodeFlags.AccessDenied | NodeFlags.EnumerationError | NodeFlags.Vanished | NodeFlags.Unavailable)) != 0) dir.Error = null;
            dir.Error = update.Error ?? dir.Error;
            if (wasSettled != dir.IsSettled) AddUnsettled(dir, wasSettled ? 1 : -1);
            if (!wasProblem && IsProblem(dir.Flags))
            {
                FlaggedProblemCount++;
            }
            else if (wasProblem && !IsProblem(dir.Flags))
            {
                FlaggedProblemCount--;
            }

            if (update.SortChildren)
            {
                // Children that were known before the listing (an ancestor stub's one child) are merged; restore the
                // deterministic order: directories, links, files; case-insensitive, then ordinal (Linux case pairs).
                dir.Children.Sort((a, b) =>
                {
                    var na = _nodes[a];
                    var nb = _nodes[b];
                    var k = KindOrder(na.Kind).CompareTo(KindOrder(nb.Kind));
                    if (k != 0) return k;
                    k = StringComparer.OrdinalIgnoreCase.Compare(na.Name, nb.Name);
                    return k != 0 ? k : StringComparer.Ordinal.Compare(na.Name, nb.Name);
                });
            }
        }

        foreach (var lu in batch.LinkUpdates)
        {
            _nodes[lu.Id].Link = lu.Link;
        }

        Version++;
    }

    /// <summary>Adds <paramref name="delta"/> to the unsettled-directory count of <paramref name="dir"/> and every ancestor.</summary>
    private void AddUnsettled(FsNode dir, int delta)
    {
        for (var current = dir; ; current = _nodes[current.ParentId])
        {
            current.SubtreeUnsettledDirs += delta;
            if (current.ParentId < 0) break;
        }
    }

    /// <summary>Counts a new child in its parent's direct totals and in the subtree totals of the parent and every ancestor.</summary>
    private void AddToAggregates(FsNode parent, FsNode child)
    {
        switch (child.Kind)
        {
            case NodeKind.File:
                parent.DirectFileCount++;
                parent.DirectFileBytes += child.Size;
                break;
            case NodeKind.Directory:
                parent.DirectDirCount++;
                break;
            case NodeKind.Link:
                parent.DirectLinkCount++;
                break;
        }

        if ((child.Flags & (NodeFlags.Hidden | NodeFlags.System)) != 0) parent.DirectHiddenCount++;

        var current = parent;
        while (true)
        {
            if (child.Kind == NodeKind.File)
            {
                current.SubtreeFileCount++;
                current.SubtreeBytes += child.Size;
            }
            else if (child.Kind == NodeKind.Directory)
            {
                current.SubtreeDirCount++;
            }
            else if (child.Kind == NodeKind.Link)
            {
                current.SubtreeLinkCount++;
            }

            if (current.ParentId < 0)
            {
                break;
            }

            current = _nodes[current.ParentId];
        }
    }

    private static int KindOrder(NodeKind k) => k switch { NodeKind.Directory => 0, NodeKind.Link => 1, _ => 2 };

    /// <summary>Flags that indicate missing or unreliable information. Hidden/system/link are facts, not problems.</summary>
    public static bool IsProblem(NodeFlags flags) =>
        (flags & (NodeFlags.AccessDenied | NodeFlags.Vanished | NodeFlags.EnumerationError | NodeFlags.Unavailable)) != 0;

    /// <summary>
    /// Returns the node's full path. For live data this is a real Windows path built from the root path and the
    /// names. For synthetic data it uses the synthetic:// display root.
    /// </summary>
    public string FullPath(int id)
    {
        var node = _nodes[id];
        if (node.LocationPath != null)
        {
            return node.LocationPath;
        }

        if (node.IsRoot)
        {
            return Source.RootDisplayPath;
        }

        var parts = new List<string>();
        var current = node;
        for (; !current.IsRoot && current.LocationPath == null; current = _nodes[current.ParentId])
        {
            parts.Add(current.Name);
        }

        var basePath = current.LocationPath ?? Source.RootDisplayPath;
        var sb = new StringBuilder(basePath);
        // POSIX locations use '/', Windows (drives, UNC, \\wsl.localhost) use '\\'; synthetic data uses '/'.
        var separator = basePath.StartsWith('/') || !Source.IsLive ? '/' : '\\';
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            if (sb.Length > 0 && sb[^1] != separator)
            {
                sb.Append(separator);
            }

            sb.Append(parts[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// A short display name: the last path segment for the root (whose Name holds the full root path), otherwise
    /// the entry name. A drive root keeps its full form ("C:\").
    /// </summary>
    public string DisplayName(int id)
    {
        var node = _nodes[id];
        if (!node.IsRoot || node.IsHub)
        {
            return node.Name;
        }

        var trimmed = node.Name.TrimEnd('\\', '/');
        var cut = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        return cut >= 0 && cut < trimmed.Length - 1 ? trimmed[(cut + 1)..] : node.Name;
    }

    /// <summary>Lists ancestors, nearest first, excluding the node itself.</summary>
    public IEnumerable<int> Ancestors(int id)
    {
        for (var p = _nodes[id].ParentId; p >= 0; p = _nodes[p].ParentId)
        {
            yield return p;
        }
    }
}
