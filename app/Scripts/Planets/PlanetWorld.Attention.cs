using FileScape.Core.Model;
using FileScape.Core.Planets;

namespace FileScape.App.Planets;

/// <summary>
/// The overview follows attention (PLANET-101..107, AD-81): this part keeps the session's <see cref="OverviewAttention"/>
/// up to date — where you stand, the selected world, the approached world and the worlds attended to before — and asks
/// it for the layout (<see cref="ComputeUniverse"/>), discovery's candidates and the indicator's words. DJ (sixth pass):
/// "Wherever I focus, I should be able to see and explore the surrounding filesystem."
/// <para>
/// When the budget is full a move of attention lays the overview out again (at most every
/// <see cref="AttentionRelayoutSeconds"/>), so the new neighbourhood unfolds and distant detail folds in place; when
/// everything fits, attention changes nothing in the picture and no relayout is made. The attention object lives as
/// long as the world (the session), so the worlds you looked at in an earlier overview keep some attention in the next.
/// </para>
/// Main thread only.
/// </summary>
public partial class PlanetWorld
{
    /// <summary>The overview's budget of laid-out worlds: <see cref="OverviewAttention.DefaultBudget"/>, or
    /// --test-overview-budget=N (the measurement instrument).</summary>
    public int OverviewBudget { get; init; } = OverviewAttention.DefaultBudget;

    private OverviewAttention? _attentionState;

    /// <summary>The session's attention (made on first use, with <see cref="OverviewBudget"/>).</summary>
    public OverviewAttention Attention => _attentionState ??= new OverviewAttention(new OverviewAttention.Rules { Budget = OverviewBudget });

    /// <summary>
    /// The least time between two relayouts made because attention moved, seconds: a click relays out at once after a
    /// quiet moment, while the approached world, which changes as the camera glides, cannot relay out every frame.
    /// </summary>
    private const float AttentionRelayoutSeconds = 0.3f;

    /// <summary>The foci the last layout was made for (see <see cref="OverviewAttention.Signature"/>).</summary>
    private string _uniAttentionSignature = "";

    /// <summary>Where you stand, for attention: a new world makes the old one a recent focus.</summary>
    private void NoteCurrentForAttention()
    {
        if (_current == null) return;
        var a = Attention;
        if (a.Current == _current.FolderId) return;
        a.Note(a.Current);
        a.Current = _current.FolderId;
    }

    /// <summary>The selected world, for attention: the world it leaves becomes a recent focus.</summary>
    private void NoteSelectionForAttention(int id)
    {
        var a = Attention;
        if (a.Selected == id) return;
        a.Note(a.Selected);
        a.Selected = id;
    }

    /// <summary>
    /// Attention moved since the last layout and the budget had to fold something away then: lay out again so the new
    /// neighbourhood unfolds. (When everything fits, the picture does not depend on attention.)
    /// </summary>
    private bool AttentionWantsRelayout()
    {
        var a = Attention;
        a.Approached = _uniApproached;
        return a.BudgetFolded > 0 && a.Signature() != _uniAttentionSignature && (_s.Now - _uniLayoutAt).TotalSeconds >= AttentionRelayoutSeconds;
    }

    /// <summary>The world whose neighbourhood the indicator describes: the selected one, else where you stand.</summary>
    private int NeighbourhoodFocus() => _uniSelected >= 0 ? _uniSelected : _current?.FolderId ?? -1;

    /// <summary>The neighbourhood's state now and the discovery deferred elsewhere (the indicator, the probes).</summary>
    public (NeighbourhoodStatus Near, int Deferred) AttentionStatus()
    {
        if (_s.Tree is not { } tree || _uniBodies.Count == 0) return (default, 0);
        var bodies = _uniBodies.Select(b => b.Body).ToList();
        var near = Attention.Neighbourhood(tree, bodies, NeighbourhoodFocus(), _s.MayListAhead, _s.IsVisible); // PLANET-197: folders the layout has not caught up with wait
        var deferred = Attention.Deferred(tree, bodies, tree.Root.Id, _s.MayListAhead);
        return (near, deferred);
    }

    /// <summary>The attention part of the probe state file (counts only, never names).</summary>
    private string AttentionStateJson()
    {
        if (_universe == null) return "";
        var (near, deferred) = AttentionStatus();
        return System.FormattableString.Invariant(
            $"\"attention\":{{\"budget\":{Attention.Tuning.Budget},\"laidOut\":{Attention.LaidOut},\"budgetFolded\":{Attention.BudgetFolded},\"focus\":{near.Focus},\"nearWorlds\":{near.Worlds},\"nearListed\":{near.Listed},\"nearWaiting\":{near.Waiting},\"nearComplete\":{(near.Complete ? "true" : "false")},\"deferred\":{deferred}}},");
    }

    // ---------------------------------------------------------------- instruments (probes only)

    /// <summary>A fresh attention with another budget (the bodies-against-frame-time measurement, PLANET-108). Close the
    /// overview first; the next one opens with this budget.</summary>
    public void ResetAttentionForProbe(int budget) => _attentionState = new OverviewAttention(new OverviewAttention.Rules { Budget = budget });

    /// <summary>Lays the open overview out again now, as discovery's relayout does (keeping what is drawn), and returns
    /// how long the layout and the scene rebuild took on the main thread, milliseconds (probes).</summary>
    public (double LayoutMs, double SceneMs) RelayoutNowForProbe()
    {
        if (_universe == null || _s.Tree is not { } tree) return (0, 0);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var bodies = ComputeUniverse(tree, keepDrawn: true);
        var layout = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        ApplyUniverseLayout(tree, bodies, animate: true);
        return (layout, sw.Elapsed.TotalMilliseconds);
    }
}
