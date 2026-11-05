using System.Collections.Concurrent;
using System.Diagnostics;
using FileScape.Core.Model;

namespace FileScape.Core.Scanning;

public enum ScanState
{
    Running,

    /// <summary>Cancellation was requested. The source is finishing its current OS call.</summary>
    Cancelling,
    Completed,
    Cancelled,

    /// <summary>The scan stopped at the node limit. The tree shows the part scanned so far.</summary>
    LimitReached,

    /// <summary>The source threw an unexpected exception. The partial tree remains usable.</summary>
    Failed,
}

/// <summary>A point-in-time copy of scan progress, safe to read from any thread.</summary>
public sealed record ScanProgress(
    ScanState State,
    int Nodes,
    int Directories,
    int Files,
    int Links,
    long LogicalBytes,
    int Problems,
    int PendingDirectories,
    TimeSpan Elapsed,
    string? CurrentPath,
    TimeSpan CurrentOperationDuration,
    TimeSpan? CancelAcknowledgedAfter,
    string? FailureMessage,
    bool Idle = false,
    bool BackgroundActive = false);

/// <summary>Something that can produce a tree as a stream of batches. Implementations run on a background thread.</summary>
public interface IScanSource
{
    DataSourceInfo Describe();

    /// <summary>Produces all batches. Must return promptly after <paramref name="ct"/> is cancelled.</summary>
    void Run(ScanContext context, CancellationToken ct);
}

/// <summary>
/// The scan thread's handle on the session: emits batches, records counters, and marks which blocking OS call
/// is in progress, so the UI can tell "waiting on the OS" from "the application is stuck" (FS-09).
/// </summary>
public sealed class ScanContext
{
    private readonly ScanSession _session;

    internal ScanContext(ScanSession session, int maxNodes)
    {
        _session = session;
        MaxNodes = maxNodes;
    }

    /// <summary>The scan stops cleanly at this many nodes. It protects memory and is reported to the user (FS-10).</summary>
    public int MaxNodes { get; }

    public int Directories;
    public int Files;
    public int Links;
    public long LogicalBytes;
    public int Problems;
    public int Pending;
    public int Nodes;
    public bool LimitReached;

    /// <summary>On-demand sources (AD-17): nothing is queued or in flight; the source waits for requests.</summary>
    public volatile bool Idle;

    /// <summary>On-demand sources: background traversal of the arrival subtree is still running.</summary>
    public volatile bool BackgroundActive;

    public void Emit(ScanBatch batch)
    {
        if (!batch.IsEmpty)
        {
            _session.Enqueue(batch);
        }
    }

    public void BeginOperation(string path) => _session.BeginOperation(path);
    public void EndOperation() => _session.EndOperation();

    /// <summary>Reports the oldest of several concurrent OS operations (null: none), with its start timestamp.</summary>
    public void SetOperation(string? path, long startTimestamp) => _session.SetOperation(path, startTimestamp);
}

/// <summary>
/// Runs an <see cref="IScanSource"/> on a dedicated background thread and hands its batches to a consumer, which
/// drains them with <see cref="TryDequeue"/> on its own thread. Cancel() returns immediately. The session records
/// when the source actually stopped.
/// </summary>
public sealed class ScanSession : IDisposable
{
    public const int DefaultMaxNodes = 1_000_000;

    private readonly IScanSource _source;
    private readonly ConcurrentQueue<ScanBatch> _batches = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _clock = new();
    private readonly ScanContext _context;
    private Thread? _thread;
    private volatile ScanState _state = ScanState.Running;
    private volatile string? _currentPath;
    private long _operationStartTicks;
    private long _cancelRequestedTicks = -1;
    private long _stoppedTicks = -1;
    private string? _failure;

    public ScanSession(IScanSource source, int maxNodes = DefaultMaxNodes)
    {
        _source = source;
        _context = new ScanContext(this, maxNodes);
        Source = source.Describe();
    }

    public DataSourceInfo Source { get; }
    public ScanState State => _state;
    public bool IsFinished => _state is ScanState.Completed or ScanState.Cancelled or ScanState.LimitReached or ScanState.Failed;

    public void Start()
    {
        if (_thread != null)
        {
            throw new InvalidOperationException("Scan already started.");
        }

        _clock.Start();
        _thread = new Thread(RunSource)
        {
            IsBackground = true,
            Name = "FileScape scan",
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
    }

    /// <summary>Requests cancellation without blocking. The source stops before its next unit of work.</summary>
    public void Cancel()
    {
        if (IsFinished || _cts.IsCancellationRequested)
        {
            return;
        }

        Interlocked.CompareExchange(ref _cancelRequestedTicks, _clock.ElapsedTicks, -1);
        _state = ScanState.Cancelling;
        _cts.Cancel();
    }

    public bool TryDequeue(out ScanBatch batch) => _batches.TryDequeue(out batch!);

    /// <summary>True while batches are waiting to be applied by the consumer.</summary>
    public bool HasPendingBatches => !_batches.IsEmpty;

    /// <summary>Waits for the source thread to stop. Intended for tests and tools, not the UI thread.</summary>
    public bool WaitForExit(TimeSpan timeout) => _thread?.Join(timeout) ?? true;

    public ScanProgress Snapshot()
    {
        var ctx = _context;
        var opStart = Interlocked.Read(ref _operationStartTicks);
        var path = _currentPath;
        var opDuration = path != null && opStart > 0
            ? Stopwatch.GetElapsedTime(opStart)
            : TimeSpan.Zero;
        TimeSpan? ack = null;
        var cancelTicks = Interlocked.Read(ref _cancelRequestedTicks);
        var stopTicks = Interlocked.Read(ref _stoppedTicks);
        if (cancelTicks >= 0 && stopTicks >= 0)
        {
            ack = TimeSpan.FromSeconds((stopTicks - cancelTicks) / (double)Stopwatch.Frequency);
        }

        return new ScanProgress(
            _state,
            Volatile.Read(ref ctx.Nodes),
            Volatile.Read(ref ctx.Directories),
            Volatile.Read(ref ctx.Files),
            Volatile.Read(ref ctx.Links),
            Interlocked.Read(ref ctx.LogicalBytes),
            Volatile.Read(ref ctx.Problems),
            Volatile.Read(ref ctx.Pending),
            _clock.Elapsed,
            path,
            opDuration,
            ack,
            _failure,
            ctx.Idle,
            ctx.BackgroundActive);
    }

    internal void Enqueue(ScanBatch batch) => _batches.Enqueue(batch);

    internal void BeginOperation(string path)
    {
        Interlocked.Exchange(ref _operationStartTicks, Stopwatch.GetTimestamp());
        _currentPath = path;
    }

    internal void EndOperation() => _currentPath = null;

    internal void SetOperation(string? path, long startTimestamp)
    {
        if (path != null) Interlocked.Exchange(ref _operationStartTicks, startTimestamp);
        _currentPath = path;
    }

    private void RunSource()
    {
        try
        {
            _source.Run(_context, _cts.Token);
            _state = _cts.IsCancellationRequested ? ScanState.Cancelled
                : _context.LimitReached ? ScanState.LimitReached
                : ScanState.Completed;
        }
        catch (OperationCanceledException)
        {
            _state = ScanState.Cancelled;
        }
        catch (Exception ex)
        {
            _failure = $"{ex.GetType().Name}: {ex.Message}";
            _state = ScanState.Failed;
        }
        finally
        {
            _currentPath = null;
            Interlocked.Exchange(ref _stoppedTicks, _clock.ElapsedTicks);
            _clock.Stop();
        }
    }

    public void Dispose()
    {
        Cancel();
        _cts.Dispose();
    }
}
