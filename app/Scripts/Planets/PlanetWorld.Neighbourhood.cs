using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The worlds around you without stalls (PLANET-174..177, AD-96): the children in the sky and the parent are composed off
/// the main thread, kept for the way back, and built into the scene a few milliseconds a frame.
/// <para>
/// Why: the desktop reproduction on the Ubuntu root (seventh pass) found that every change background discovery made to
/// the current world re-composed and rebuilt all 21 worlds in its sky on the main thread (475–1,141 ms, every 4–5 s
/// while walking), and that a landing back on the root did the same twice (0.9–1.2 s, then 0.6–0.9 s when the recompose
/// followed). Now: a world already in the sky keeps its body and visual unless its look changed
/// (<see cref="Neighbourhood.Stale"/>, by <see cref="PlanetComposer.PreviewSignature"/>); a changed or missing composition
/// is made by <see cref="BackgroundComposer"/> (which holds the tree still meanwhile: Main defers scan batches) and kept
/// in <see cref="CompositionCache"/>; previews and swaps are built by <see cref="FrameSliceQueue"/> within
/// <see cref="NeighbourSliceBudget"/> a frame; and a launch asks for its destination's neighbours before it lands
/// (<see cref="PrefetchNeighbourhood"/>), so touchdown finds them ready.
/// </para>
/// Preserve: nothing here changes what a world looks like (the same pure compositions, made elsewhere and later); travel
/// is never disturbed (no swap while <see cref="Busy"/>: the flight's target, its frame and the landing stay as they
/// are, and a world prepared in full for a launch is not swapped); a world with a probe's forced look is composed on the
/// main thread as before (<see cref="RootMarkFor"/>); every queue is bounded and dropped with the tree (ResetForTree).
/// </summary>
public partial class PlanetWorld
{
    /// <summary>Main-thread time a frame may start neighbourhood work in, milliseconds (FrameSliceQueue: at least one item
    /// runs, so a frame costs at most this plus one preview, ≈ 1–3 ms with its ground made on a worker). By measurement:
    /// 4 ms keeps a 60 Hz frame inside its 16.7 ms with the walk's own ≈ 2 ms, and lands 21 previews in about 10 frames.</summary>
    private static readonly TimeSpan NeighbourSliceBudget = TimeSpan.FromMilliseconds(4);

    /// <summary>Folders one background job composes at most. A job holds the tree still (scan batches wait), so it is kept
    /// short: on the Ubuntu root the costliest child composes in ≈ 100–200 ms, the median in under 1 ms.</summary>
    private const int MaxComposeJobFolders = 6;

    /// <summary>Compositions kept by folder (<see cref="CompositionCache"/>): a few neighbourhoods' worth (a world, its
    /// parent and up to ≈ 30 worlds in its sky), so going down and back up composes nothing again.</summary>
    private const int CompositionCacheFolders = 128;

    /// <summary>Folders a launch's prefetch asks for at most (its destination's child folders and parent).</summary>
    private const int PrefetchFoldersMax = 40;

    private readonly CompositionCache _compositions = new(CompositionCacheFolders);
    private readonly BackgroundComposer _composer = new();
    private readonly List<int> _composeWanted = [];
    private readonly FrameSliceQueue _slices = new();
    private static readonly System.Diagnostics.Stopwatch SliceClock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>The destination whose neighbourhood was last prefetched (one prefetch per launch).</summary>
    private int _prefetchedFor = -1;

    /// <summary>Counts <see cref="ResetNeighbourhood"/> calls; a job started under an older count is dropped when collected
    /// (made for another tree, or under the old hidden-entries rule).</summary>
    private int _neighbourGeneration, _jobGeneration;

    /// <summary>Folders waiting to be composed in the background (the frame log, probes).</summary>
    public int NeighbourComposeWaiting => _composeWanted.Count;

    public bool NeighbourComposerBusy => _composer.Busy;

    /// <summary>Main-thread work waiting for a frame's slice (previews, swaps, prebuilt previews).</summary>
    public int NeighbourSlicesWaiting => _slices.Count;

    /// <summary>Neighbours composed on the main thread because no composition was kept (probes: the regression guard
    /// expects none while walking; a landing far from anything seen may need a few).</summary>
    public int MainThreadNeighbourComposes { get; private set; }

    /// <summary>Worlds in the sky swapped for a recomposed look, and background jobs and composes (probes, the log).</summary>
    public int NeighbourSwaps { get; private set; }

    public (int Jobs, int Composed, int Failures, double LongestJobMs) BackgroundComposes => (_composer.Jobs, _composer.Composed, _composer.Failures, _composer.LongestJobMs);

    /// <summary>A probe's forced look is composed on the main thread with it; every other folder may be composed anywhere.</summary>
    private string VariantOf(int folderId) => RootMarkFor(folderId)?.ToString() ?? "";

    /// <summary>The world shown at this folder as a neighbour (a child in the sky or the parent), or null.</summary>
    private Body? Neighbour(int folderId) =>
        _children.TryGetValue(folderId, out var b) ? b : _parent is { } p && p.FolderId == folderId ? p : null;

    private bool IsNeighbour(Body body) => Neighbour(body.FolderId) == body;

    /// <summary>What the sky shows now, for <see cref="Neighbourhood.Stale"/>.</summary>
    private IEnumerable<ShownWorld> NeighboursShown() =>
        _children.Values.Append(_parent).OfType<Body>().Select(b => ShownWorld.Sky(b.Comp));

    /// <summary>Asks for <paramref name="folderId"/> to be composed in the background (once; <paramref name="first"/>: ahead of the rest).</summary>
    private void RequestCompose(int folderId, bool first = false)
    {
        if (VariantOf(folderId).Length > 0) return; // composed on the main thread where it is shown
        var at = _composeWanted.IndexOf(folderId);
        if (at >= 0 && !(first && at > 0)) return;
        if (at >= 0) _composeWanted.RemoveAt(at);
        if (first) _composeWanted.Insert(0, folderId);
        else _composeWanted.Add(folderId);
    }

    /// <summary>
    /// The composition to show a neighbour with: the kept one if its look is current; else the kept one, with a fresh one
    /// asked for (swapped in when made); else composed now on the main thread (counted: only a world never seen).
    /// </summary>
    private PlanetComposition NeighbourComposition(FsTree tree, int folderId)
    {
        var variant = VariantOf(folderId);
        if (_compositions.Latest(folderId, variant) is { } kept)
        {
            if (kept.PreviewSignature != PlanetComposer.PreviewSignature(tree, folderId)) RequestCompose(folderId);
            return kept;
        }

        MainThreadNeighbourComposes++;
        var made = Compose(tree, folderId);
        _compositions.Put(made, variant);
        return made;
    }

    /// <summary>The current world's composition: kept if made from exactly this listing, else composed now (and kept).</summary>
    private PlanetComposition CurrentComposition(FsTree tree, int folderId)
    {
        var variant = VariantOf(folderId);
        if (_compositions.TryGetExact(folderId, PlanetComposer.Signature(tree, folderId), variant, out var kept)) return kept;
        var made = Compose(tree, folderId);
        _compositions.Put(made, variant);
        return made;
    }

    /// <summary>A new world in the sky: its kept (or new) composition, its visual from the cache or built in a later slice.</summary>
    private Body NewNeighbour(FsTree tree, int folderId, string nodeName)
    {
        var body = new Body { FolderId = folderId, Comp = NeighbourComposition(tree, folderId), Node = new Node3D { Name = nodeName } };
        AddChild(body.Node);
        GroundAsync(body.Comp, full: true); // prefetched: ready by the time you stand on its launcher
        if (TakeCachedVisual(body, full: false) is { } cached)
        {
            body.Visual = cached;
            body.Node.AddChild(cached);
            body.Full = false;
        }
        else
        {
            GroundAsync(body.Comp, full: false);
            _slices.Enqueue($"preview:{folderId}", () => BuildPreviewSlice(body));
        }

        return body;
    }

    /// <summary>One slice: a world in the sky gets its preview once its ground (made on a worker) is ready.</summary>
    private SliceResult BuildPreviewSlice(Body body)
    {
        if (_s.Tree is not { } tree || !IsNeighbour(body) || body.Visual != null) return SliceResult.Done;
        if (!GroundAsync(body.Comp, full: false).IsCompleted) return SliceResult.NotReady;
        BuildPreview(tree, body);
        return SliceResult.Done;
    }

    /// <summary>One slice: a world in the sky whose look changed takes its new composition (never during travel).</summary>
    private SliceResult SwapNeighbourSlice(int folderId)
    {
        if (_s.Tree is not { } tree || Neighbour(folderId) is not { } body || _current == null) return SliceResult.Done;
        if (Busy) return SliceResult.NotReady; // the flight's target and frame stay as they are until you land
        if (body.Full) return SliceResult.Done; // prepared for a launch: recomposed once you stand on it
        if (_compositions.Latest(folderId, VariantOf(folderId)) is not { } comp) return SliceResult.Done;
        if (comp.PreviewSignature == body.Comp.PreviewSignature)
        {
            body.Comp = comp; // the same look from a fresher listing: nothing to rebuild
            return SliceResult.Done;
        }

        if (!GroundAsync(comp, full: false).IsCompleted) return SliceResult.NotReady;
        Release(body); // the old look goes to the cache
        body.Comp = comp;
        BuildPreview(tree, body);
        body.Node.Transform = PlacementOf(body);
        GroundAsync(comp, full: true);
        NeighbourSwaps++;
        FrameDiag.Note($"swap #{folderId}");
        return SliceResult.Done;
    }

    /// <summary>One slice: a preview for a world a launch is heading towards, built into the cache before touchdown.</summary>
    private SliceResult PrebuildPreviewSlice(int folderId)
    {
        if (_s.Tree is not { } tree || !ReferenceEquals(tree, _tree)) return SliceResult.Done;
        if (_compositions.Latest(folderId, VariantOf(folderId)) is not { } comp) return _composeWanted.Contains(folderId) || _composer.Busy ? SliceResult.NotReady : SliceResult.Done;
        var key = VisualKeyOf(folderId, comp, full: false);
        if (_visualCache.Exists(e => e.Key == key) || Neighbour(folderId) is { Visual: not null }) return SliceResult.Done;
        if (!GroundAsync(comp, full: false).IsCompleted) return SliceResult.NotReady;
        var visual = BuildVisual(tree, folderId, comp, full: false);
        CacheVisual(key, visual);
        return SliceResult.Done;
    }

    /// <summary>
    /// A launch towards <paramref name="targetId"/>: its child folders and parent are composed in the background now and
    /// their previews built into the cache during the flight, so the landing finds its sky ready (no compose at touchdown).
    /// </summary>
    private void PrefetchNeighbourhood(FsTree tree, int targetId)
    {
        if (!tree.Contains(targetId) || _prefetchedFor == targetId) return;
        _prefetchedFor = targetId;
        var t = tree[targetId];
        var ids = t.Children.Where(c => tree[c].IsDirectory && _s.IsVisible(tree[c])).Take(PrefetchFoldersMax).ToList();
        if (t.ParentId >= 0 && tree.Contains(t.ParentId)) ids.Add(t.ParentId);
        foreach (var id in ids)
        {
            if (id == _current?.FolderId || Neighbour(id) != null) continue; // already shown (and kept across the landing)
            if (_compositions.Latest(id, VariantOf(id)) is not { } kept || kept.PreviewSignature != PlanetComposer.PreviewSignature(tree, id)) RequestCompose(id);
            _slices.Enqueue($"prebuild:{id}", () => PrebuildPreviewSlice(id));
        }

        RequestCompose(targetId); // its own fresh listing, for the recompose after touchdown
    }

    /// <summary>
    /// Once a frame (PlanetWorld.Tick, before the recompose check): collects a finished background job, starts the next,
    /// prefetches for a launch under way, and runs the frame's slices.
    /// </summary>
    private void PumpNeighbourhood(FsTree tree)
    {
        if (_composer.TryCollect(out var job))
        {
            FrameDiag.Note(FormattableString.Invariant($"composed {job.Compositions.Count} in {job.Elapsed.TotalMilliseconds:0}ms (worker)"));
            if (ReferenceEquals(job.Tree, tree) && _jobGeneration == _neighbourGeneration)
            {
                foreach (var c in job.Compositions) OnComposed(c);
            }
        }

        if (_transfer is { } tr && (tr.Target?.FolderId ?? tr.Change?.To ?? tr.LaunchTarget) is var target and >= 0) PrefetchNeighbourhood(tree, target);
        else if (_transfer == null && !Busy) _prefetchedFor = -1; // landed: the next launch there prefetches afresh
        if (!_composer.Busy && _composeWanted.Count > 0)
        {
            var ids = _composeWanted.Take(MaxComposeJobFolders).Where(tree.Contains).ToList();
            _composeWanted.RemoveRange(0, Math.Min(MaxComposeJobFolders, _composeWanted.Count));
            // Captured now, on the main thread: the worker reads only the tree (held still) and these.
            var parameters = ComposeParams;
            Func<FsNode, bool> visible = _s.IsVisible; // a pure rule over the node and one setting
            if (_composer.TryStart(tree, ids, id => PlanetComposer.Compose(tree, id, parameters, visible))) _jobGeneration = _neighbourGeneration;
        }

        _slices.Run(NeighbourSliceBudget, () => SliceClock.Elapsed);
    }

    /// <summary>A background composition arrived: kept; for the world you stand on it becomes the pending recompose, for a
    /// world in the sky whose look changed a swap is queued.</summary>
    private void OnComposed(PlanetComposition comp)
    {
        _compositions.Put(comp);
        var id = comp.FolderId;
        if (_current != null && id == _current.FolderId)
        {
            // Its ground is made on a worker too; ApplyPendingRecompose swaps it in once that is ready (as before AD-96).
            if (comp.Signature != _current.Comp.Signature) _pendingRecompose = (comp, GroundAsync(comp, full: true));
            return;
        }

        if (Neighbour(id) is { } body && body.Comp.Signature != comp.Signature) _slices.Enqueue($"swap:{id}", () => SwapNeighbourSlice(id));
    }

    /// <summary>Where a neighbour hangs in the current world's frame: a child at its sky position, the parent behind.</summary>
    private Transform3D PlacementOf(Body body)
    {
        var current = _current!.Comp;
        if (_parent == body)
        {
            var mine = body.Comp.SkyBodies.FirstOrDefault(k => k.TargetId == current.FolderId && !k.InBelt);
            return mine != null ? ChildPlacement(body.Comp, mine, current).AffineInverse() : DefaultParentPlacement(current, body.Comp);
        }

        var sky = current.SkyBodies.FirstOrDefault(s => s.TargetId == body.FolderId && !s.InBelt && !s.IsParent);
        return sky != null ? ChildPlacement(current, sky, body.Comp) : body.Node.Transform;
    }

    /// <summary>Drops every queue and kept composition (a new tree, or hidden entries shown or not).</summary>
    private void ResetNeighbourhood()
    {
        _compositions.Clear();
        _composeWanted.Clear();
        _slices.Clear();
        _prefetchedFor = -1;
        _neighbourGeneration++;
        // A job in flight finishes on its own; its results are for the old tree and are dropped when collected.
    }
}
