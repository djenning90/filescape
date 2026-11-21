using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using FileScape.Core.Shell;
using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-selection-actions --root=sample|&lt;fixture&gt; --vis=planets --test-shell=record</c> (PLANET-87,
/// AD-76): the selection card's actions audited as a matrix — Open, Show in Explorer, Parent, Fly to and Copy path, each
/// by its button (a click injected at the button's centre through the real input path) and by the shortcut its label
/// shows (plus the shortcut the label showed before 2026-11-19: Enter, Esc, F), for six kinds of target: a file on this
/// world, a child folder (a world in the sky), the world you stand on, the location root (a drive or a WSL
/// distribution), the hub ("This computer"), and a world selected in the overview. The test shell records Open and Show in
/// Explorer and the clipboard (nothing is started, the person's clipboard is untouched).
/// <para>
/// Each cell logs one <c>cell</c> line: the label, enabled or not, the tooltip, and what happened — a shell request, a
/// clipboard entry, a selection change, the camera turning, the overview framing, a landing, a toast, or nothing. It is
/// judged by four rules: an enabled action does something visible (an effect or a message); a disabled one says why in
/// its tooltip (starting "Unavailable:"); a shortcut a label shows does what the button does; and nothing asks Windows for
/// a path that is not one (the hub). Selections are set [direct] (the selection a click makes).
/// </para>
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>What a cell can change, read before and after an action.</summary>
    private sealed record ActionSnap(int Shell, int Clip, int Sel, float Yaw, int UniFocus, float UniMetres, int Folder, bool Overview, string Toast, bool Confirm);

    private ActionSnap SnapActions() => new(App.ShellLog.Count, App.ClipboardLog.Count, App.Selection.Selected, P.CameraHeadingDegrees(),
        P.UniverseOpen ? P.UniverseFraming.Focus : -1, P.UniverseOpen ? P.UniverseFraming.Metres : 0f, App.Navigator.Current, P.UniverseOpen, App.Hud.ShownToast, App.ConfirmShowing);

    /// <summary>The effect between two snapshots, as one comparable word plus detail ("none" when nothing changed).</summary>
    private string Effect(ActionSnap a, ActionSnap b)
    {
        var parts = new List<string>();
        if (b.Shell > a.Shell) parts.Add("shell:" + App.ShellLog[^1]);
        if (b.Clip > a.Clip) parts.Add("clip:" + App.ClipboardLog[^1]);
        if (b.Confirm && !a.Confirm) parts.Add("confirm");
        if (b.Folder != a.Folder || (a.Overview && !b.Overview)) parts.Add(F($"travel:{(b.Overview ? "overview" : "surface")}:{NodeName(b.Folder)}"));
        else if (b.Sel != a.Sel) parts.Add("select:" + NodeName(b.Sel));
        if (!a.Overview && !b.Overview && MathF.Abs(Mathf.Wrap(b.Yaw - a.Yaw, -180f, 180f)) > 2f) parts.Add("camera");
        if (a.Overview && b.Overview && (b.UniFocus != a.UniFocus || MathF.Abs(b.UniMetres - a.UniMetres) > ReframeFraction * a.UniMetres))
            parts.Add("frame:" + NodeName(b.UniFocus));
        if (b.Toast != "" && b.Toast != a.Toast) parts.Add("toast:" + b.Toast);
        return parts.Count == 0 ? "none" : string.Join(" | ", parts);
    }

    private string NodeName(int id) => id < 0 ? "(none)" : Tree.Contains(id) ? Tree.DisplayName(id) : $"#{id}";

    /// <summary>The key a label shows in brackets ("Open (Enter)" → Enter), or null.</summary>
    private static Godot.Key? ShownKey(string label)
    {
        var open = label.LastIndexOf('(');
        if (open < 0 || !label.EndsWith(')')) return null;
        return label[(open + 1)..^1] switch { "Enter" => Godot.Key.Enter, "Esc" => Godot.Key.Escape, "F" => Godot.Key.F, "G" => Godot.Key.G, _ => null };
    }

    private async Task SelectionActionsRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5);
        var here = App.Navigator.Current;
        var file = P.Composition!.Items.Select(i => i.NodeId)
            .FirstOrDefault(id => Tree.Contains(id) && Tree[id].Kind == NodeKind.File && !LaunchRisk.RunsCode(Tree[id].Name), -1);
        var folder = Tree[here].Children.FirstOrDefault(c => Tree[c].IsDirectory && P.ChildWorldIds.Contains(c), -1);
        var root = here;
        while (Tree[root].ParentId >= 0 && !Tree[Tree[root].ParentId].IsHub) root = Tree[root].ParentId;
        var hub = Tree[root].ParentId >= 0 ? Tree[root].ParentId : -1;
        Log($"targets: file {NodeName(file)}, folder {NodeName(folder)}, here {NodeName(here)}, root {Tree.FullPath(root)}, hub {NodeName(hub)}");
        Check(file >= 0 && folder >= 0 && hub >= 0, "the world has a file, a child world and a hub above its location root");

        foreach (var (kind, id) in new[] { ("file", file), ("folder", folder), ("here", here), ("root", root), ("hub", hub) })
        {
            if (id < 0) continue;
            foreach (var action in new[] { "open", "reveal", "copy", "fly", "parent" }) await Cell(kind, id, action, overview: false);
        }

        await Shot("selection-card-surface");

        // The overview: a world selected there; Open is last (its old shortcut, Enter, lands).
        P.ToggleUniverse(); // [direct] T
        App.Hud.SetPlanetsOverview(P.UniverseOpen);
        await Until(() => P.UniverseOpen && P.UniverseSettled, 10);
        await Seconds(1.0);
        foreach (var action in new[] { "reveal", "copy", "fly", "parent", "open" }) await Cell("overview", folder, action, overview: true);

        // Parent in the overview moves the card to the containing world: F and Enter must then act on that world,
        // not on the world selected before (the overview's own selection and the card's must stay one).
        if (!P.UniverseOpen) { P.ToggleUniverse(); await Until(() => P.UniverseOpen && P.UniverseSettled, 10); await Seconds(0.8); }
        P.SelectInUniverse(folder); // [direct] a click on the world
        await Seconds(0.8);
        await PressButton("parent");
        await Seconds(0.6);
        var parentSel = App.Selection.Selected;
        await Key(Godot.Key.F);
        await Seconds(0.6);
        Check(parentSel == Tree[folder].ParentId && P.UniverseFraming.Focus == parentSel,
            $"overview: after Parent the card shows {NodeName(parentSel)} and F frames it (framed {NodeName(P.UniverseFraming.Focus)})");
        await Shot("selection-card-overview");
    }

    /// <summary>Clicks one of the card's action buttons by name (its centre); false when it is hidden.</summary>
    private async Task<bool> PressButton(string name)
    {
        var b = App.Hud.SelectionActionButtons.First(x => x.Name == name);
        if (!b.Visible) return false;
        await Click(b.Rect.GetCenter());
        return true;
    }

    /// <summary>Selects the target again as a click would, closes what a previous cell left, and clears the toast.</summary>
    /// <summary>Wheel notches out before "Zoom to (F)" is tried, so bringing the view back shows (PLANET-87/93).</summary>
    private const int WheelOutNotches = 4;

    /// <summary>A change of the overview's distance larger than this fraction counts as a reframe.</summary>
    private const float ReframeFraction = 0.25f;

    /// <summary>Waits (at most <paramref name="timeout"/> s) until the overview's distance changes by under 1 % in 0.2 s.</summary>
    private async Task UntilFramingSteady(double timeout = 5)
    {
        var timedOut = Deadline(timeout);
        while (!timedOut())
        {
            var a = P.UniverseFraming.Metres;
            await Seconds(0.2);
            if (MathF.Abs(P.UniverseFraming.Metres - a) < 0.01f * MathF.Max(a, 1f)) return;
        }
    }

    private async Task ResetCell(int id, bool overview, int frameElsewhere = -1)
    {
        if (App.ConfirmShowing) App.ProbeCancelConfirm();
        if (overview && !P.UniverseOpen) { P.ToggleUniverse(); App.Hud.SetPlanetsOverview(true); await Until(() => P.UniverseOpen && P.UniverseSettled, 10); await Seconds(0.8); }
        if (overview && frameElsewhere >= 0)
        {
            // The overview follows the card's selection (PLANET-83), so "framed elsewhere" is now: the card's world framed,
            // then the view wheeled out from it, as a player does before asking to be brought back (PLANET-93).
            P.SelectInUniverse(id); // [direct] a click on the world
            await Seconds(0.8);
            var at = App.GetViewport().GetVisibleRect().Size / 2;
            for (var i = 0; i < WheelOutNotches; i++) { Mouse(MouseButton.WheelDown, true, at); Mouse(MouseButton.WheelDown, false, at); await Frames(3); }
            await Seconds(0.8);
        }
        else if (overview) P.SelectInUniverse(id); // [direct] a click on the world
        if (overview) await UntilFramingSteady(); // a glide left by the cell before (the wheel-out) must not leak into this one
        else App.Selection.Select(id); // [direct] the selection a click makes
        App.Hud.Toast("", null, 0);
        await Frames(3);
        if (!overview) P.FaceCameraAlong(PlanetShape.Tangents(P.Walker.Up).A); // [direct] a known heading, so turning shows
        await Frames(2);
    }

    /// <summary>One action on one target, by button and by the shortcuts (the one its label shows, and its old one).</summary>
    private async Task Cell(string kind, int id, string action, bool overview)
    {
        // Zoom to (F) in the overview is seen only if the overview is framed on another world first.
        var elsewhere = overview && action == "fly" ? App.Navigator.Current : -1;
        await ResetCell(id, overview, elsewhere);
        var b = App.Hud.SelectionActionButtons.First(x => x.Name == action);
        var where = $"{kind} {NodeName(id)}";
        var before = SnapActions();
        var pressed = await PressButton(action);
        await Seconds(0.5);
        var buttonEffect = pressed ? Effect(before, SnapActions()) : "hidden";
        Log($"cell {where} · {action} button \"{b.Text}\" {(b.Disabled ? "disabled" : "enabled")} · tooltip \"{b.Tooltip}\" → {buttonEffect}");
        if (b.Disabled) Check(buttonEffect == "none" && b.Tooltip.StartsWith("Unavailable:", StringComparison.Ordinal), $"{where}: {action} is disabled and says why (\"{b.Tooltip}\")");
        else if (pressed) Check(buttonEffect != "none", $"{where}: the enabled {action} button does something visible ({buttonEffect})");
        // The hub has no filesystem path (its FullPath is empty): nothing may hand Windows an empty or made-up path for it.
        if (kind == "hub" && buttonEffect.StartsWith("shell:", StringComparison.Ordinal))
            Check(buttonEffect.StartsWith($"shell:open {SelectionActions.ThisPcShellPath}", StringComparison.Ordinal), $"{where}: {action} asks Windows only for This PC ({SelectionActions.ThisPcShellPath}), never for a path the hub does not have ({buttonEffect})");
        if (kind == "hub" && buttonEffect.StartsWith("clip:", StringComparison.Ordinal))
            Check(false, $"{where}: copy path copies a path, and the hub has none ({buttonEffect})");

        var keys = new List<Godot.Key>();
        if (ShownKey(b.Text) is { } shown) keys.Add(shown);
        var old = action switch { "open" => Godot.Key.Enter, "parent" => Godot.Key.Escape, "fly" => Godot.Key.F, _ => (Godot.Key?)null };
        if (old is { } o && !keys.Contains(o)) keys.Add(o);
        foreach (var key in keys)
        {
            await ResetCell(id, overview, elsewhere);
            var label = App.Hud.SelectionActionButtons.First(x => x.Name == action);
            var claimed = ShownKey(label.Text) == key;
            var s0 = SnapActions();
            await Key(key);
            await Seconds(0.5);
            var keyEffect = Effect(s0, SnapActions());
            if (overview && !keyEffect.Contains("travel:")) { await Seconds(2.5); keyEffect = Effect(s0, SnapActions()); } // a landing takes ≈ 2 s (its fly-in reframes first)
            Log($"cell {where} · {action} key {key} ({(claimed ? "shown on the button" : "not shown")}) → {keyEffect}");
            if (claimed && !label.Disabled)
                Check(Kind(keyEffect) == Kind(buttonEffect), $"{where}: {key}, shown on \"{label.Text}\", does what the button does (key: {keyEffect}; button: {buttonEffect})");
            if (claimed && label.Disabled)
                Check(!keyEffect.StartsWith("shell:", StringComparison.Ordinal) && !keyEffect.StartsWith("clip:", StringComparison.Ordinal), $"{where}: {key} does not run the disabled {action} ({keyEffect})");
        }
    }

    /// <summary>
    /// An effect without the detail that may differ between a key and its button, for comparing them: a shell request keeps
    /// its verb and path ("shell:open C:\x"), a toast is only "toast"; a selection, travel or framing keeps its target
    /// (Esc's "select:(none)" is not the button's "select:Starter Sky").
    /// </summary>
    private static string Kind(string effect) => string.Join(" | ", effect.Split(" | ").Where(p => !p.StartsWith("toast:", StringComparison.Ordinal) || effect == p).Select(p => p.StartsWith("toast:", StringComparison.Ordinal) ? "toast" : p));
}
