using System.IO.Enumeration;
using FileScape.Core.Model;

namespace FileScape.Core.Scanning;

/// <summary>
/// Scans a real directory tree, breadth first, reading metadata only. Shallow levels appear first, which suits
/// incremental display.
/// <para>
/// This is the whole-tree reference mode (the "bounded tree scan" behind the Classic FSN and District maps): one chosen
/// folder listed completely in the background, up to <see cref="ScanContext.MaxNodes"/>. The connected world uses
/// <see cref="WorldExplorer"/> instead, which lists on demand; it reuses this class's attribute policy
/// (<see cref="FlagsFor"/>, <see cref="ShouldEnumerateDirectory"/>) so both modes classify entries identically.
/// </para>
/// </summary>
/// <remarks>
/// Policies (docs/requirements.md FS-01 to FS-10):
/// <list type="bullet">
/// <item>Each directory is enumerated once with <see cref="FileSystemEnumerable{TResult}"/> without recursion.
/// .NET's built-in recursion has no reparse-point check, so FileScape recurses itself.</item>
/// <item>Reparse points become <see cref="NodeKind.Link"/> leaves and are never followed. That prevents cycles
/// and stops mount points or junctions crossing onto other volumes.</item>
/// <item>Hidden and system entries are included and flagged. The .NET defaults would skip them silently.</item>
/// <item>Access-denied, vanished, and failed directories are recorded and the scan continues.</item>
/// <item>Directories marked RECALL_ON_DATA_ACCESS are not enumerated, because Microsoft documents that enumerating
/// them can fetch from the remote store. Files are never opened. Even so, FileScape does not claim that metadata
/// enumeration never triggers provider activity.</item>
/// <item>Size is the logical length from the directory entry. Allocated size is not queried during the scan.</item>
/// </list>
/// </remarks>
public sealed class DirectoryScanner : IScanSource
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
        BufferSize = 64 * 1024, // bytes for the OS directory-entry buffer: fewer kernel round trips per big folder than the default buffer (rationale for 64 KiB not recorded)
    };

    private readonly string _rootPath;

    public DirectoryScanner(string rootPath)
    {
        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (_rootPath.EndsWith(':'))
        {
            _rootPath += Path.DirectorySeparatorChar; // "C:" → "C:\" so the path stays absolute
        }
    }

    /// <summary>A test hook, called on the scan thread just before a directory is enumerated, inside the reported OS-operation window.</summary>
    public Action<string>? BeforeEnumerateDirectory { get; init; }

    /// <summary>
    /// A test hook that replaces how the root's attributes are read. Real cloud-placeholder attributes cannot be
    /// created without a cloud provider, so tests use this to exercise the root policy.
    /// </summary>
    public Func<string, FileAttributes>? RootAttributesProbe { get; init; }

    /// <summary>
    /// Policy FS-13, applied identically to the root and to every descendant: a directory marked
    /// RECALL_ON_DATA_ACCESS is shown but not enumerated, because Microsoft documents that enumerating it can fetch
    /// from the remote store. There is no override in this prototype.
    /// </summary>
    public static bool ShouldEnumerateDirectory(FileAttributes attributes) => (attributes & RecallOnDataAccess) == 0;

    /// <summary>
    /// The number of entries per emitted batch for very large directories: a huge folder arrives in slices, so the
    /// consumer can apply one slice per frame instead of one enormous batch.
    /// </summary>
    public int MaxBatchEntries { get; init; } = 4096;

    public DataSourceInfo Describe() =>
        new(DataOrigin.LiveFilesystem, _rootPath, "Live Windows filesystem (metadata only)");

    /// <summary>
    /// Emits the root, then lists directories in breadth-first order until the queue empties, the node limit is reached
    /// (the rest are marked <see cref="DirScanState.NotTraversed"/>) or cancellation (the rest are marked
    /// <see cref="DirScanState.Cancelled"/>). Ids are assigned in emission order, as <see cref="ScanBatch"/> requires.
    /// </summary>
    public void Run(ScanContext context, CancellationToken ct)
    {
        var rootBatch = new ScanBatch();
        var rootInfo = new DirectoryInfo(_rootPath);
        var rootFlags = NodeFlags.None;
        FileAttributes rootAttrs = 0;
        DateTime? created = null, modified = null, accessed = null;
        try
        {
            rootAttrs = RootAttributesProbe?.Invoke(_rootPath) ?? rootInfo.Attributes;
            if ((int)rootAttrs == -1)
            {
                // .NET reports a missing path as all-bits-set attributes rather than throwing. Treat it as "no
                // attributes", so enumeration fails visibly (Vanished) instead of looking like a cloud placeholder.
                rootAttrs = 0;
            }

            rootFlags = FlagsFor(rootAttrs) & ~(NodeFlags.ReparsePoint | NodeFlags.DirectoryLink);
            created = rootInfo.CreationTimeUtc;
            modified = rootInfo.LastWriteTimeUtc;
            accessed = rootInfo.LastAccessTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            rootFlags |= NodeFlags.EnumerationError;
        }

        // The user chose the root explicitly, so it is traversed even if it is itself a link. The cloud policy
        // still applies to it, exactly as to its descendants.
        var enumerateRoot = (rootFlags & NodeFlags.EnumerationError) != 0 || ShouldEnumerateDirectory(rootAttrs);
        if (!enumerateRoot)
        {
            rootFlags |= NodeFlags.CloudNotTraversed;
        }

        rootBatch.NewNodes.Add(new NodeRecord(0, -1, _rootPath, NodeKind.Directory, rootFlags, rootAttrs, 0,
            created, modified, accessed, 0, enumerateRoot ? DirScanState.Pending : DirScanState.NotTraversed));
        context.Nodes = 1;
        context.Directories = 1;
        context.Pending = enumerateRoot ? 1 : 0;
        context.Emit(rootBatch);
        if (!enumerateRoot)
        {
            return;
        }

        var queue = new Queue<(int Id, string Path, int Depth)>();
        queue.Enqueue((0, _rootPath, 0));
        var nextId = 1;

        while (queue.Count > 0)
        {
            if (ct.IsCancellationRequested)
            {
                MarkRemaining(context, queue, DirScanState.Cancelled);
                return;
            }

            var (dirId, dirPath, depth) = queue.Dequeue();
            context.Pending = queue.Count + 1;

            if (context.Nodes >= context.MaxNodes)
            {
                context.LimitReached = true;
                // Put it back so MarkRemaining marks it too: it was never listed.
                queue.Enqueue((dirId, dirPath, depth));
                MarkRemaining(context, queue, DirScanState.NotTraversed);
                return;
            }

            var (entries, failureFlags, error) = Enumerate(context, dirPath, BeforeEnumerateDirectory, ct);

            // Deterministic order: directories, then links, then files, each ordinal case-insensitive.
            entries.Sort(static (a, b) =>
            {
                var k = KindOrder(a.Kind).CompareTo(KindOrder(b.Kind));
                return k != 0 ? k : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            });

            var batch = new ScanBatch();
            var truncatedByLimit = false;
            foreach (var e in entries)
            {
                if (context.Nodes >= context.MaxNodes)
                {
                    truncatedByLimit = true;
                    context.LimitReached = true;
                    break;
                }

                var id = nextId++;
                var state = DirScanState.NotApplicable;
                if (e.Kind == NodeKind.Directory)
                {
                    if ((e.Flags & NodeFlags.CloudNotTraversed) != 0)
                    {
                        state = DirScanState.NotTraversed;
                    }
                    else
                    {
                        state = DirScanState.Pending;
                        queue.Enqueue((id, Path.Join(dirPath, e.Name), depth + 1));
                    }
                }

                batch.NewNodes.Add(new NodeRecord(id, dirId, e.Name, e.Kind, e.Flags, e.Attributes, e.Size,
                    e.Created, e.Modified, e.Accessed, depth + 1, state));
                context.Nodes++;
                switch (e.Kind)
                {
                    case NodeKind.Directory: context.Directories++; break;
                    case NodeKind.File: context.Files++; context.LogicalBytes += e.Size; break;
                    case NodeKind.Link: context.Links++; break;
                }

                if (batch.NewNodes.Count >= MaxBatchEntries)
                {
                    context.Emit(batch);
                    batch = new ScanBatch();
                }
            }

            var finalState = DirScanState.Complete;
            var addFlags = failureFlags;
            if (failureFlags != NodeFlags.None)
            {
                // A failure part-way through may still have yielded entries. Keep them and mark the directory partial.
                finalState = entries.Count == 0 ? DirScanState.Failed : DirScanState.Complete;
                addFlags |= entries.Count == 0 ? NodeFlags.None : NodeFlags.Incomplete;
                context.Problems++;
            }

            if (truncatedByLimit)
            {
                addFlags |= NodeFlags.Incomplete;
                finalState = DirScanState.NotTraversed;
            }

            if (ct.IsCancellationRequested && failureFlags == NodeFlags.None && error == "cancelled")
            {
                finalState = DirScanState.Cancelled;
                addFlags |= NodeFlags.Incomplete;
            }

            batch.DirectoryUpdates.Add(new DirectoryUpdate(dirId, finalState, addFlags, error == "cancelled" ? null : error));
            context.Emit(batch);

            if (truncatedByLimit)
            {
                MarkRemaining(context, queue, DirScanState.NotTraversed);
                return;
            }
        }

        context.Pending = 0;
    }

    private static int KindOrder(NodeKind kind) => kind switch
    {
        NodeKind.Directory => 0,
        NodeKind.Link => 1,
        _ => 2,
    };

    private static void MarkRemaining(ScanContext context, Queue<(int Id, string Path, int Depth)> queue, DirScanState state)
    {
        var batch = new ScanBatch();
        while (queue.Count > 0)
        {
            var (id, _, _) = queue.Dequeue();
            batch.DirectoryUpdates.Add(new DirectoryUpdate(id, state, NodeFlags.Incomplete));
        }

        context.Pending = 0;
        context.Emit(batch);
    }

    /// <summary>One directory entry as read from the OS, before it is given an id.</summary>
    private readonly record struct Entry(
        string Name, NodeKind Kind, NodeFlags Flags, FileAttributes Attributes, long Size,
        DateTime? Created, DateTime? Modified, DateTime? Accessed);

    /// <summary>
    /// Lists one directory inside a reported OS-operation window. Returns what was read even on failure (a partial
    /// listing is kept), the failure as a flag and message, or the error text "cancelled" when it stopped early on request.
    /// </summary>
    private static (List<Entry> Entries, NodeFlags FailureFlags, string? Error) Enumerate(
        ScanContext context, string dirPath, Action<string>? beforeEnumerate, CancellationToken ct)
    {
        var entries = new List<Entry>();
        context.BeginOperation(dirPath);
        try
        {
            // Runs inside the operation window, so a test hook that blocks looks exactly like a slow OS call.
            beforeEnumerate?.Invoke(dirPath);
            var enumerable = new FileSystemEnumerable<Entry>(dirPath, ToEntry, Options);
            foreach (var entry in enumerable)
            {
                entries.Add(entry);
                if (ct.IsCancellationRequested)
                {
                    return (entries, NodeFlags.None, "cancelled");
                }
            }

            return (entries, NodeFlags.None, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            return (entries, NodeFlags.AccessDenied, ex.Message);
        }
        catch (DirectoryNotFoundException ex)
        {
            return (entries, NodeFlags.Vanished, ex.Message);
        }
        catch (IOException ex)
        {
            return (entries, NodeFlags.EnumerationError, ex.Message);
        }
        finally
        {
            context.EndOperation();
        }
    }

    private static Entry ToEntry(ref FileSystemEntry e)
    {
        var attrs = e.Attributes;
        var flags = FlagsFor(attrs);
        NodeKind kind;
        if ((attrs & FileAttributes.ReparsePoint) != 0)
        {
            kind = NodeKind.Link;
            if (e.IsDirectory)
            {
                flags |= NodeFlags.DirectoryLink;
            }
        }
        else if (e.IsDirectory)
        {
            kind = NodeKind.Directory;
            if (!ShouldEnumerateDirectory(attrs))
            {
                flags |= NodeFlags.CloudNotTraversed;
            }
        }
        else
        {
            kind = NodeKind.File;
        }

        return new Entry(
            e.FileName.ToString(),
            kind,
            flags,
            attrs,
            kind == NodeKind.File ? e.Length : 0,
            SafeTime(e.CreationTimeUtc),
            SafeTime(e.LastWriteTimeUtc),
            SafeTime(e.LastAccessTimeUtc));
    }

    // Windows reports "no time" as the FILETIME epoch (1601-01-01); show that as unknown rather than as a real date.
    private static DateTime? SafeTime(DateTimeOffset t) =>
        t.UtcDateTime.Year <= 1601 ? null : t.UtcDateTime;

    // Values from the Windows SDK (winnt.h). .NET's FileAttributes enum lacks the cloud attributes.
    public const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    public const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    public const FileAttributes Pinned = (FileAttributes)0x00080000;
    public const FileAttributes Unpinned = (FileAttributes)0x00100000;

    /// <summary>The node flags an entry's attributes imply; any recall or offline attribute marks a cloud placeholder.</summary>
    internal static NodeFlags FlagsFor(FileAttributes attrs)
    {
        var f = NodeFlags.None;
        if ((attrs & FileAttributes.Hidden) != 0) f |= NodeFlags.Hidden;
        if ((attrs & FileAttributes.System) != 0) f |= NodeFlags.System;
        if ((attrs & FileAttributes.ReadOnly) != 0) f |= NodeFlags.ReadOnly;
        if ((attrs & FileAttributes.ReparsePoint) != 0) f |= NodeFlags.ReparsePoint;
        if ((attrs & FileAttributes.SparseFile) != 0) f |= NodeFlags.Sparse;
        if ((attrs & FileAttributes.Compressed) != 0) f |= NodeFlags.Compressed;
        if ((attrs & FileAttributes.Encrypted) != 0) f |= NodeFlags.Encrypted;
        if ((attrs & (RecallOnOpen | RecallOnDataAccess | FileAttributes.Offline)) != 0) f |= NodeFlags.CloudPlaceholder;
        return f;
    }
}
