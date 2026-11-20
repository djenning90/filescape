using FileScape.Core.Interaction;
using FileScape.Core.Model;
using Godot;

namespace FileScape.App;

/// <summary>
/// The selection card's actions in every view (PLANET-87, AD-76): which of Open, Show in Explorer, Parent, Fly to and
/// Copy path apply to the selection, with labels and tooltips from <see cref="SelectionActions.For"/>, and what the
/// buttons and their shortcuts do. One handler per action serves the button and the key, so a label's shortcut does what
/// its button does; an action that cannot run says why (its tooltip, and a toast when its key is pressed anyway).
/// <para>
/// Preserve: the hub ("This computer", no filesystem path) is never handed to Windows as a path — Open shows This PC,
/// the rest are disabled; on the planets F faces (surface) or zooms (overview), and the overview acts on the card's
/// selection; the legacy maps keep "Parent (Esc)" and "Fly to (F)" as they were.
/// </para>
/// </summary>
public partial class Main
{
    /// <summary>The view the card is in now.</summary>
    private CardView CurrentCardView => _experience switch
    {
        Experience.Planets => _planets.UniverseOpen ? CardView.PlanetOverview : CardView.PlanetSurface,
        Experience.Places => CardView.Places,
        _ => CardView.Map,
    };

    /// <summary>The card's description of a node: its kind, name, and where it can be shown from here.</summary>
    private CardTargetInfo CardTargetFor(FsNode node)
    {
        var tree = _tree!;
        var kind = node.IsHub ? CardTarget.Hub
            : node.Kind == NodeKind.Link ? CardTarget.Link
            : node.IsDirectory ? (node.LocationPath != null || (node.ParentId >= 0 && tree[node.ParentId].IsHub) ? CardTarget.LocationRoot : CardTarget.Folder)
            : CardTarget.File;
        var view = CurrentCardView;
        var inView = view switch
        {
            CardView.PlanetSurface => _planets.CanFace(node.Id),
            CardView.PlanetOverview => _planets.UniverseKnows(node.Id),
            _ => true,
        };
        var standing = view == CardView.PlanetSurface && node.Id == _nav.Current;
        return new CardTargetInfo(kind, tree.DisplayName(node.Id), tree.Source.IsLive, node.IsRoot, standing, inView);
    }

    /// <summary>The selected node, or null (with a toast naming what to do) when nothing is selected.</summary>
    private FsNode? CardSelection(string verb)
    {
        if (_tree == null || _selection.Selected < 0 || !_tree.Contains(_selection.Selected))
        {
            if (_tree != null) _hud.Toast($"Nothing is selected to {verb}: click a file, folder or world first.");
            return null;
        }

        return _tree[_selection.Selected];
    }

    /// <summary>The card's actions for this node, now (also what a key checks before it acts).</summary>
    private CardActions CardActionsFor(FsNode node) => SelectionActions.For(CurrentCardView, CardTargetFor(node));

    /// <summary>Applies the card's actions for the selection (UpdateInspector, and when the overview opens or closes).</summary>
    private void ApplyCardActions(FsNode node) => _hud.SetCardActions(CardActionsFor(node));

    /// <summary>Says why an action is unavailable, as its tooltip does (for a key pressed anyway).</summary>
    private void ToastUnavailable(CardAction a) =>
        _hud.Toast(a.Tooltip.StartsWith(SelectionActions.Unavailable, StringComparison.Ordinal) ? a.Tooltip[SelectionActions.Unavailable.Length..] : a.Tooltip, new Color(1f, 0.8f, 0.5f));

    private (CardView View, object? World, int Current) _cardContext;

    /// <summary>
    /// Per frame: when the view changes under an unchanged selection (the overview opens or closes, a world is built
    /// after a landing), the card's labels and what can be faced change with it, so it is refreshed then.
    /// </summary>
    private void RefreshCardOnViewChange()
    {
        var context = (CurrentCardView, _experience == Experience.Planets ? _planets.Composition : null, _nav.Current);
        if (context == _cardContext) return;
        _cardContext = context;
        if (_tree != null && _selection.Selected >= 0) UpdateInspector();
    }

    /// <summary>The card's Parent: select the containing folder; on a map also frame it; in the overview zoom to it.</summary>
    private void CardParent()
    {
        if (CardSelection("find the parent of") is not { } node) return;
        var a = CardActionsFor(node).Parent;
        if (!a.Enabled) { ToastUnavailable(a); return; }
        if (!_selection.Parent(_tree!)) return;
        if (_experience == Experience.Planets && _planets.UniverseOpen) _planets.ZoomToSelection();
        else if (_experience != Experience.Planets) FlyToSelection();
    }

    /// <summary>
    /// The card's Fly to and the F key: on a planet's surface turn to face the selection, in the overview zoom to it,
    /// elsewhere frame it as before. When it cannot, say why.
    /// </summary>
    private void CardFly()
    {
        if (_experience != Experience.Planets) { FlyToSelection(); return; }
        if (CardSelection("face") is not { } node) return;
        var a = CardActionsFor(node).Fly;
        if (!a.Enabled) { ToastUnavailable(a); return; }
        var done = _planets.UniverseOpen ? _planets.ZoomToSelection() : _planets.FaceSelection();
        if (!done) ToastUnavailable(a with { Tooltip = SelectionActions.Unavailable + $"{_tree!.DisplayName(node.Id)} cannot be shown from here." });
    }
}
