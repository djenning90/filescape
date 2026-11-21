using System.Diagnostics;
using FileScape.Core.Model;

namespace FileScape.Core.Planets;

/// <summary>
/// Composes planets on a worker thread (PLANET-176, AD-96). <see cref="PlanetComposer.Compose"/> is engine-free and reads
/// only the tree, so it can run off the main thread, which then only collects the results and builds their visuals in
/// slices (<see cref="Interaction.FrameSliceQueue"/>). Before the seventh pass every neighbour was composed on the main
/// thread (357–617 ms of the Ubuntu root's 475–1,141 ms stalls).
/// <para>
/// One job at a time: <see cref="TryStart"/> takes the tree's read lease (<see cref="FsTree.BeginBackgroundRead"/>) on the
/// calling (main) thread before the worker starts and the worker ends it when it has finished reading, before its
/// results can be collected; while it is held the main thread must not apply scan batches (Main defers them; the tree
/// would throw). Keep jobs short (the caller passes a few folders at a time) so batches are deferred briefly.
/// A compose that throws is counted in <see cref="Failures"/> and left out (the caller composes that folder itself if it
/// still needs it). Results come back in the order asked. Call <see cref="TryStart"/> and <see cref="TryCollect"/> from
/// the main thread only.
/// </para>
/// </summary>
public sealed class BackgroundComposer
{
    private Task<Job>? _task;

    /// <summary>A finished job: the tree it read, what it made in the order asked, and its worker time.</summary>
    public sealed record Job(FsTree Tree, IReadOnlyList<PlanetComposition> Compositions, TimeSpan Elapsed);

    /// <summary>A job has been started and not yet collected.</summary>
    public bool Busy => _task != null;

    /// <summary>Jobs started, folders composed, composes that threw (probes, the frame log).</summary>
    public int Jobs { get; private set; }

    public int Composed { get; private set; }

    public int Failures { get; private set; }

    /// <summary>The longest job's worker time, milliseconds.</summary>
    public double LongestJobMs { get; private set; }

    /// <summary>
    /// Starts composing <paramref name="folderIds"/> of <paramref name="tree"/> with <paramref name="compose"/> on a worker,
    /// unless a job is already in flight (false). <paramref name="compose"/> must read only the tree and values captured
    /// before the call (never the scene or the main thread's mutable state).
    /// </summary>
    public bool TryStart(FsTree tree, IReadOnlyList<int> folderIds, Func<int, PlanetComposition> compose)
    {
        if (_task != null || folderIds.Count == 0) return false;
        var ids = folderIds.ToArray();
        var lease = tree.BeginBackgroundRead();
        Jobs++;
        _task = Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var made = new List<PlanetComposition>(ids.Length);
            var failed = 0;
            try
            {
                foreach (var id in ids)
                {
                    try { made.Add(compose(id)); }
                    catch (Exception) { failed++; } // counted; the caller composes it itself if still needed
                }
            }
            finally
            {
                lease.Dispose(); // the tree may change again from here
            }

            Interlocked.Add(ref _pendingFailures, failed);
            return new Job(tree, made, sw.Elapsed);
        });
        return true;
    }

    private int _pendingFailures;

    /// <summary>The finished job, once (false while it runs or when none was started).</summary>
    public bool TryCollect(out Job job)
    {
        job = null!;
        if (_task is not { IsCompleted: true } t) return false;
        _task = null;
        job = t.Result; // the worker catches every compose's exception, so the task cannot fault
        Composed += job.Compositions.Count;
        Failures += Interlocked.Exchange(ref _pendingFailures, 0);
        LongestJobMs = Math.Max(LongestJobMs, job.Elapsed.TotalMilliseconds);
        return true;
    }

    /// <summary>Waits for the job in flight (tests; never the game's main thread).</summary>
    public bool WaitForJob(TimeSpan timeout) => _task == null || _task.Wait(timeout);
}
