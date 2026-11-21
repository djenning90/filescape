namespace FileScape.Core.Planets;

/// <summary>
/// Compositions kept by folder (PLANET-175, AD-96), so the worlds you go back to, and the neighbours of a world you
/// land on, are not composed again on the main thread. Holds the latest composition made for each folder, with the
/// variant it was made for (a probe's forced root look, or none), least recently used dropped first.
/// <para>
/// Two lookups: <see cref="TryGetExact"/> (made from exactly this signature: what the tree shows now) and
/// <see cref="Latest"/> (whatever was made last, possibly from an older listing: a landing shows it at once and asks
/// for a fresh one in the background, instead of stalling the touchdown to compose). Preserve: a composition is only ever
/// returned for the folder and variant it was made for; <see cref="Clear"/> when something the signature does not see
/// changes (hidden entries shown or not, a new tree); bounded by <see cref="Capacity"/>. Not thread-safe: the main
/// thread owns it (background results are put in when they are collected).
/// </para>
/// </summary>
public sealed class CompositionCache
{
    private readonly Dictionary<int, LinkedListNode<Entry>> _byFolder = [];
    private readonly LinkedList<Entry> _order = new(); // most recently used first

    private sealed record Entry(int FolderId, string Variant, PlanetComposition Composition);

    /// <param name="capacity">Folders kept at most (at least 1).</param>
    public CompositionCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count => _byFolder.Count;

    /// <summary>Keeps <paramref name="composition"/> as its folder's latest (replacing an older one).</summary>
    public void Put(PlanetComposition composition, string variant = "")
    {
        if (_byFolder.Remove(composition.FolderId, out var old)) _order.Remove(old);
        _byFolder[composition.FolderId] = _order.AddFirst(new Entry(composition.FolderId, variant, composition));
        while (_byFolder.Count > Capacity)
        {
            var last = _order.Last!;
            _order.RemoveLast();
            _byFolder.Remove(last.Value.FolderId);
        }
    }

    /// <summary>The folder's composition made from exactly <paramref name="signature"/> for <paramref name="variant"/>, if kept.</summary>
    public bool TryGetExact(int folderId, string signature, string variant, out PlanetComposition composition)
    {
        composition = null!;
        if (!_byFolder.TryGetValue(folderId, out var node) || node.Value.Variant != variant || node.Value.Composition.Signature != signature) return false;
        Touch(node);
        composition = node.Value.Composition;
        return true;
    }

    /// <summary>The folder's latest composition for <paramref name="variant"/>, whatever listing it was made from, or null.</summary>
    public PlanetComposition? Latest(int folderId, string variant)
    {
        if (!_byFolder.TryGetValue(folderId, out var node) || node.Value.Variant != variant) return null;
        Touch(node);
        return node.Value.Composition;
    }

    public void Clear()
    {
        _byFolder.Clear();
        _order.Clear();
    }

    private void Touch(LinkedListNode<Entry> node)
    {
        _order.Remove(node);
        _order.AddFirst(node);
    }
}
