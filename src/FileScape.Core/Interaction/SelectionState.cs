using FileScape.Core.Model;

namespace FileScape.Core.Interaction;

/// <summary>
/// The user's position in the hierarchy: the selected node, the hovered node, and the navigation history. It refers
/// to node ids only, never to scene objects, so it survives visualization switches and re-layout (AD-03, NAV-06).
/// </summary>
public sealed class SelectionState
{
    private readonly List<int> _history = [];

    public int Selected { get; private set; } = -1;
    public int Hovered { get; set; } = -1;

    public event Action<int>? SelectionChanged;

    public IReadOnlyList<int> History => _history;

    /// <summary>Selects a node. If <paramref name="recordHistory"/> is true, the previous selection can be returned to with Back.</summary>
    public void Select(int id, bool recordHistory = true)
    {
        if (id == Selected)
        {
            return;
        }

        if (recordHistory && Selected >= 0)
        {
            _history.Add(Selected);
            if (_history.Count > 200)
            {
                _history.RemoveAt(0); // bounded so long sessions don't grow without limit; only the oldest steps are lost
            }
        }

        Selected = id;
        SelectionChanged?.Invoke(id);
    }

    /// <summary>Returns to the previous selection. Returns false if the history is empty.</summary>
    public bool Back()
    {
        if (_history.Count == 0)
        {
            return false;
        }

        var previous = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        Selected = previous;
        SelectionChanged?.Invoke(previous);
        return true;
    }

    /// <summary>Selects the parent directory of the current selection. Returns false at the root or with nothing selected.</summary>
    public bool Parent(FsTree tree)
    {
        if (Selected < 0 || !tree.Contains(Selected) || tree[Selected].IsRoot)
        {
            return false;
        }

        Select(tree[Selected].ParentId);
        return true;
    }

    public void Reset()
    {
        _history.Clear();
        Selected = -1;
        Hovered = -1;
        SelectionChanged?.Invoke(-1);
    }
}
