using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// File and folder actions on a planet (PLANET-43, AD-57): one rule for the three things you can point at — a card over
/// the workbench, a file or folder standing on the world (its object or its launcher pad), and a world in the sky.
/// <list type="bullet">
/// <item><b>Click</b>: select it (the selection card shows its actions: Open, Show in Explorer, Go inside).</item>
/// <item><b>Double-click</b>: a folder or link (a card, a pad, a world) — go there; a file (a card or its object) — the
/// deliberate Open, exactly as Enter or the card's Open button (<see cref="IWorldSession.OpenSelection"/>: the same
/// confirmations for code, links and cloud files, and a message saying what happened, failures included).</item>
/// <item><b>Enter</b>: open the selection (shared with every view, Main.RequestOpen).</item>
/// </list>
/// Before 2026-11-16 a double-click on a file hopped the traveller to it and previewed it (DJ: "the character jumped and
/// beeped instead of opening the solution"), while a folder card navigated: the same gesture meant two things.
/// <para>
/// The other half of the file is the click trace: every click records how it was resolved (which hit test answered,
/// what was done), printed as <c>[planets] click …</c> and kept in <see cref="LastClickTrace"/> for probes.
/// </para>
/// </summary>
public partial class PlanetWorld
{
    /// <summary>How the last click on the world was resolved: hit test → dispatch → action (probes and the log).</summary>
    public string LastClickTrace { get; private set; } = "";

    /// <summary>Records one click's resolution (main thread).</summary>
    private void TraceClick(string trace)
    {
        LastClickTrace = trace;
        GD.Print($"[planets] click {trace}");
    }

    /// <summary>The trace's name for what a click on <paramref name="id"/> does (its kind, and select / go / open).</summary>
    private string ActionWord(int id, bool doubleClick)
    {
        if (_s.Tree is not { } tree || !tree.Contains(id)) return "unknown node";
        var n = tree[id];
        var kind = n.IsDirectory ? "folder" : n.Kind == NodeKind.Link ? "link" : "file";
        return $"{kind} {n.Name}: {(doubleClick ? (n.IsDirectory || n.Kind == NodeKind.Link ? "go there" : "open (the deliberate Open)") : "select")}";
    }

    /// <summary>
    /// A double-click on a card, an object, a pad or a world (and Enter or a double-click in the bench's list): a folder
    /// or link goes there (launching from its pad's bearing when it has one; a world in the sky is always a folder); a
    /// file is selected and opened through the session's one Open path, never directly.
    /// </summary>
    private void DoubleClickOn(int id, bool sky)
    {
        var tree = _s.Tree;
        if (tree == null || !tree.Contains(id)) return;
        _s.Select(id);
        var n = tree[id];
        if (sky || n.IsDirectory || n.Kind == NodeKind.Link)
        {
            LaunchToSelection(); // travel: WorldDoubleClick
            return;
        }

        OpenFile(id);
    }

    /// <summary>
    /// Opens a file as Enter does (<see cref="IWorldSession.OpenSelection"/>): the session asks first for files that run
    /// code, links and cloud placeholders, records instead of launching under the test shell, and says what happened —
    /// in red when Windows refused (no app for the type, the file gone, access denied). The open sound plays only when
    /// the request goes out; the session's message is the answer.
    /// </summary>
    private void OpenFile(int id)
    {
        if (_s.Tree is not { } tree || !tree.Contains(id) || tree[id].IsDirectory) return;
        _s.Select(id);
        _audio.Open();
        _s.OpenSelection();
    }

    /// <summary>Twirls that previewed a file or listed a pile (probes).</summary>
    public int TwirlPreviews { get; private set; }

    /// <summary>
    /// The twirl's contextual action (PLANET-43, AD-57): a twirl (the air jump) with a file in focus — the object the prompt
    /// names, "or twirl (Space, Space) to preview" — selects it and shows its preview, as E does, without turning you or
    /// moving you (you are in the air); with a pile in focus it lists that pile. It never opens anything: a twirl is
    /// movement, and an external program must never start from movement. The workbench's spin (README) is handled first
    /// by <see cref="UpdateBench"/>. Away from files a twirl is only a twirl.
    /// </summary>
    private void TwirlNearFile()
    {
        if (_focusItem is not { } it || _s.Tree is not { } tree) return;
        if (it.Kind == PlanetItemKind.Pile)
        {
            OpenContents(it.Category);
            _audio.Inspect();
            TwirlPreviews++;
            return;
        }

        if (it.NodeId < 0 || !tree.Contains(it.NodeId) || tree[it.NodeId].IsDirectory) return;
        if (_previewPanel.Visible && _s.Selection.Selected == it.NodeId) return; // already showing it: a second twirl changes nothing
        _s.Select(it.NodeId);
        _audio.Inspect();
        ShowPreview(tree, tree[it.NodeId]);
        TwirlPreviews++;
        GD.Print($"[planets] twirl by {tree[it.NodeId].Name}: preview");
    }

    /// <summary>Where a standing object's pick sphere is centred (the same point <see cref="Pick"/> tests), or null.</summary>
    public Godot.Vector3? ItemCentre(int nodeId)
    {
        if (_current?.Comp.Items.FirstOrDefault(i => i.NodeId == nodeId) is not { } it) return null;
        return Gd(_current.Comp.Surface.PointAt(it.Direction, 0.55f * it.Scale));
    }

    /// <summary>Where a card over the workbench is now (its pick centre), or null when it has not risen.</summary>
    public Godot.Vector3? BenchTileCentre(int nodeId)
    {
        foreach (var (id, tile) in _tiles)
        {
            if (id == nodeId) return tile.GlobalPosition;
        }

        return null;
    }
}
