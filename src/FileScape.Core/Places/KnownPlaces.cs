using FileScape.Core.Model;

namespace FileScape.Core.Places;

/// <summary>
/// The repositories FileScape has seen so far (PLANET-41): every folder with a <c>.git</c> entry (a folder, or a file
/// for a worktree or submodule — the same rule as <see cref="Planets.WorldCharacter.RepositoryMarker"/>) among the
/// nodes already in the tree. It never lists anything: it reads what scanning has found, incrementally.
/// <para>
/// Nodes are only ever appended to an <see cref="FsTree"/> (ids grow), so the index remembers how far it has read and
/// looks only at new nodes, at most <c>budget</c> per call: the app calls it once a frame while the list is open, so a
/// tree of millions of entries is indexed over a few frames and the list stays responsive while discovery continues.
/// A different tree (a new scan) starts the index again.
/// </para>
/// </summary>
public sealed class RepositoryIndex
{
    private FsTree? _tree;
    private int _next;
    private readonly List<int> _repos = [];
    private readonly HashSet<int> _set = [];

    /// <summary>Repository folders in the order they were found.</summary>
    public IReadOnlyList<int> Repositories => _repos;

    public IReadOnlySet<int> Set => _set;

    /// <summary>The whole tree has been read (until it grows again).</summary>
    public bool CaughtUp => _tree != null && _next >= _tree.Count;

    /// <summary>Reads up to <paramref name="budget"/> new nodes; true when a repository was found.</summary>
    public bool Update(FsTree tree, int budget)
    {
        if (!ReferenceEquals(tree, _tree))
        {
            _tree = tree;
            _next = 0;
            _repos.Clear();
            _set.Clear();
        }

        var end = Math.Min(tree.Count, _next + Math.Max(1, budget));
        var found = false;
        for (var i = _next; i < end; i++)
        {
            var n = tree[i];
            if (n.ParentId < 0 || !n.Name.Equals(".git", StringComparison.Ordinal) || !(n.IsDirectory || n.Kind == NodeKind.File)) continue;
            if (_set.Add(n.ParentId))
            {
                _repos.Add(n.ParentId);
                found = true;
            }
        }

        _next = end;
        return found;
    }
}

/// <summary>Finds a folder already in the tree by its full path (a pin from an earlier session), without listing anything.</summary>
public static class PathLookup
{
    /// <summary>
    /// The node for <paramref name="path"/>, or −1 when it is not in the tree (yet). It starts from the location roots
    /// (or the tree's root when there is no hub) and descends name by name: an exact name first, then one that differs
    /// only in case (Windows names; a case-sensitive WSL folder with both spellings resolves to the exact one).
    /// </summary>
    public static int Find(FsTree tree, string path)
    {
        if (tree.Count == 0 || string.IsNullOrWhiteSpace(path)) return -1;
        var want = path.TrimEnd('\\', '/');
        IEnumerable<int> starts = tree.Root.IsHub ? tree.Root.Children : [0];
        foreach (var s in starts)
        {
            var sp = tree.FullPath(s).TrimEnd('\\', '/');
            if (sp.Length == 0) continue;
            if (want.Equals(sp, StringComparison.OrdinalIgnoreCase)) return s;
            if (!want.StartsWith(sp, StringComparison.OrdinalIgnoreCase) || want.Length <= sp.Length || want[sp.Length] is not ('\\' or '/')) continue;
            var cur = s;
            foreach (var part in want[(sp.Length + 1)..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            {
                var kids = tree[cur].Children;
                var next = kids.FirstOrDefault(c => tree[c].Name.Equals(part, StringComparison.Ordinal), -1);
                if (next < 0) next = kids.FirstOrDefault(c => tree[c].Name.Equals(part, StringComparison.OrdinalIgnoreCase), -1);
                if (next < 0) { cur = -1; break; }
                cur = next;
            }

            if (cur >= 0) return cur;
        }

        return -1;
    }
}
