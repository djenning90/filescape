namespace FileScape.Core.Interaction;

/// <summary>What one step of a <see cref="FrameSliceQueue"/> item did this frame.</summary>
public enum SliceResult
{
    /// <summary>The item finished (it leaves the queue).</summary>
    Done,

    /// <summary>The item cannot run yet (a worker's result is not ready, travel is under way): try again next frame.
    /// A wait costs no budget and does not count as the frame's step.</summary>
    NotReady,
}

/// <summary>
/// Main-thread work spread over frames within a time budget (PLANET-176, AD-96): building a sky world's preview,
/// swapping in a recomposed neighbour. Before the seventh pass such work ran all at once in one frame (a landing on the
/// Ubuntu root built 21 previews in one 0.9–1.2 s frame); queued here, each frame runs ready items in order until the
/// frame's budget is spent, so the cost is a few milliseconds a frame instead of a visible stall.
/// <para>
/// Rules a change must keep (tested in FrameSliceQueueTests): items run in the order queued (first come, first shown);
/// queuing a key already waiting replaces that item in its place (the newer work supersedes the older: a world
/// recomposed twice is swapped once); each frame runs at least one ready item, however long it takes (so the queue
/// always drains), and starts no further item once <c>budget</c> is spent (an item is never interrupted: the budget bounds
/// when items start, so a frame costs at most the budget plus one item); each item is offered at most once per frame; an
/// item that is not ready keeps its place. Not thread-safe: the main thread owns it.
/// </para>
/// </summary>
public sealed class FrameSliceQueue
{
    private readonly List<(string Key, Func<SliceResult> Step)> _items = [];

    public int Count => _items.Count;

    /// <summary>Keys waiting, in order (probes, the frame log).</summary>
    public IEnumerable<string> Keys => _items.Select(i => i.Key);

    /// <summary>Items finished since the queue was made (probes).</summary>
    public int Completed { get; private set; }

    /// <summary>Queues <paramref name="step"/> under <paramref name="key"/>, replacing a waiting item with that key in its place.</summary>
    public void Enqueue(string key, Func<SliceResult> step)
    {
        var i = _items.FindIndex(x => x.Key == key);
        if (i >= 0) _items[i] = (key, step);
        else _items.Add((key, step));
    }

    public bool Contains(string key) => _items.Exists(x => x.Key == key);

    public void Remove(string key) => _items.RemoveAll(x => x.Key == key);

    public void Clear() => _items.Clear();

    /// <summary>
    /// One frame's work: offers each item once, in order, running ready ones until <paramref name="budget"/> has passed
    /// on <paramref name="clock"/> (at least one ready item runs). Returns how many finished.
    /// </summary>
    public int Run(TimeSpan budget, Func<TimeSpan> clock)
    {
        if (_items.Count == 0) return 0;
        var start = clock();
        var done = 0;
        var snapshot = _items.ToArray(); // an item may queue more work; that waits for the next frame
        foreach (var item in snapshot)
        {
            if (done > 0 && clock() - start >= budget) break;
            var at = _items.FindIndex(x => x.Key == item.Key && x.Step == item.Step);
            if (at < 0) continue; // replaced or removed by an earlier item this frame
            if (item.Step() == SliceResult.NotReady) continue;
            done++;
            Completed++;
            at = _items.FindIndex(x => x.Key == item.Key && x.Step == item.Step);
            if (at >= 0) _items.RemoveAt(at);
        }

        return done;
    }
}
