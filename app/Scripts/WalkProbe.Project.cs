using FileScape.Core.Places;
using FileScape.Core.Scanning;
using Godot;

namespace FileScape.App;

/// <summary>
/// The guided journey of this iteration (LOC-06, PROM-01..03, DEV-01..03), through the injected input path:
/// start at the hub (This computer), open the chooser (This computer first), go by path to an owned fixture's Projects
/// folder, see the project marked, go into it, read its README (inert text), see the editor options (confirmation asked,
/// then cancelled: no editor is launched by the probe), pin and unpin, and return to the hub with Open… → This computer.
/// Run with --root=hub. Only owned fixture folders are listed: the hub lists location roots, never their contents.
/// </summary>
public partial class WalkProbe
{
    private async Task ProjectJourney()
    {
        var tree = App.Tree!;
        Check(App.Navigator.Current == 0 && tree[0].IsHub, $"--root=hub starts at {WorldExplorer.HubName} ({App.Place!.Gates.Count} locations as gates)");
        Check(App.Place.Gates.Count >= 1 && App.Place.Gates.All(g => tree[g.TargetId].ScanState == Core.Model.DirScanState.Unlisted),
            "the hub's locations are gates, and none is listed until entered (no drive is scanned)");
        await Shot("hub-arrival");

        // Open… (Ctrl+O) is the in-app chooser now, with This computer first; Esc closes it.
        await KeyCombo(Godot.Key.O, ctrl: true);
        await Until(() => App.Hud.LocationsVisible, 3);
        Check(App.Hud.LocationsVisible && App.Hud.LocationRowText(0).Contains(WorldExplorer.HubName, StringComparison.Ordinal) && App.Hud.LocationRowText(0).Contains("This PC", StringComparison.Ordinal),
            $"Ctrl+O opens the chooser with “{App.Hud.LocationRowText(0)}” first");
        await Shot("open-chooser");
        await Key(Godot.Key.Escape);

        // Typing "This PC" goes to the hub, never to a path (here: already there).
        await KeyCombo(Godot.Key.L, ctrl: true);
        await TypeText("This PC");
        await Key(Godot.Key.Enter);
        await Frames(5);
        Check(App.Navigator.Current == 0 && App.LastExplorerError == null, "typing “This PC” in Go to path means the hub (no “can't open this location”)");

        // Go to the fixture's Projects folder by path.
        var root = OS.GetCmdlineUserArgs().First(a => a.StartsWith("--project-root=", StringComparison.Ordinal))["--project-root=".Length..];
        await KeyCombo(Godot.Key.L, ctrl: true);
        await TypeText(root);
        await Key(Godot.Key.Enter);
        await Until(() => App.Tree![App.Navigator.Current].Name == "Projects" && App.Place != null && !App.InTransition, 20);
        tree = App.Tree!;
        var projects = App.Navigator.Current;
        Check(tree[projects].Name == "Projects", $"Go to path reaches {tree.FullPath(projects)}");
        var lighthouse = tree[projects].Children.FirstOrDefault(c => tree[c].Name == "lighthouse");
        var notes = tree[projects].Children.FirstOrDefault(c => tree[c].Name == "notes-only");
        await Until(() => App.ProminenceOf(lighthouse).Kind == ProminenceKind.Project, 10);
        var p = App.ProminenceOf(lighthouse);
        Check(p.Kind == ProminenceKind.Project && p.Reason.Contains("Lighthouse.sln", StringComparison.Ordinal), $"lighthouse is marked as a project once listed ahead: {p.Mark} {p.Reason}");
        Check(!App.ProminenceOf(notes).IsProminent, "notes-only (a README, no manifest) is not marked");
        await Frames(10);
        Check(App.Signs.ShownText.Values.Any(t => t.StartsWith("⚙ lighthouse", StringComparison.Ordinal)), $"its gate sign carries the mark ({string.Join(", ", App.Signs.ShownText.Values)})");
        Check(App.PlaceWorld.ProminentGateCount >= 1, $"its gate has a light ({App.PlaceWorld.ProminentGateCount} marked gates)");
        await Shot("projects-marked");

        // Into the project: select its gate, G.
        App.Select(lighthouse);
        await Frames(3);
        await Key(Godot.Key.G);
        await Until(() => App.Navigator.Current == lighthouse && !App.InTransition && App.Place != null, 20);
        Check(App.Navigator.Current == lighthouse && App.Place!.Kind.Kind == PlaceKind.Project, $"in the project place ({App.Place?.Kind.Label}: {string.Join(", ", App.Place?.Kind.Evidence ?? [])})");
        await Shot("project-place");

        // README on request (R): inert text, bounded.
        await Key(Godot.Key.R);
        await Until(() => App.Hud.ReadmeVisible, 5);
        var shown = App.Hud.ReadmeShownText;
        Check(App.Hud.ReadmeVisible && App.LastReadme is { Error: null } && shown.Contains("Lighthouse", StringComparison.Ordinal) && shown.Contains("[color=red]This markup must stay text[/color]", StringComparison.Ordinal) && !shown.Contains("https://example.invalid/logo.png", StringComparison.Ordinal), $"R shows the README ({App.LastReadme?.Path}, {shown.Length} chars): “{shown[..Math.Min(60, shown.Length)].ReplaceLineEndings(" ")}…”");
        await Shot("project-readme");
        await Key(Godot.Key.Escape);
        await Frames(3);
        Check(!App.Hud.ReadmeVisible, "Esc closes the README");

        // The card: the project's prominence, pin, README and editor options (asked first; the probe cancels).
        App.Select(lighthouse);
        await Frames(4);
        var options = App.EditorOptions;
        Check(options.Count >= 1 && options[0].Label == "Open Lighthouse.sln" && options[0].ViaAssociation,
            $"editor options for a Windows project: {string.Join(" | ", options.Select(o => o.Label))}");
        await Shot("project-card");
        if (options.Count > 0)
        {
            App.Hud.ProbeRequestEditor(0);
            await Frames(4);
            Check(App.ConfirmShowing && App.ConfirmText.Contains("Lighthouse.sln", StringComparison.Ordinal), $"choosing it asks first: “{App.ConfirmText.ReplaceLineEndings(" ")}”");
            await Shot("editor-confirmation");
            App.ProbeCancelConfirm();
            await Frames(3);
            Check(!App.ConfirmShowing, "cancelled: nothing was launched");
        }

        await Key(Godot.Key.P);
        await Frames(4);
        Check(App.ProminenceOf(lighthouse).Kind == ProminenceKind.Pinned, "P pins the project (★)");
        await Key(Godot.Key.P);
        await Frames(4);
        Check(App.ProminenceOf(lighthouse).Kind == ProminenceKind.Project, "P again unpins it");

        // Back to the hub with Open… → This computer; history still leads back.
        await KeyCombo(Godot.Key.O, ctrl: true);
        await Until(() => App.Hud.LocationsVisible, 3);
        await Click(App.Hud.LocationRowRect(0).GetCenter());
        await Until(() => App.Navigator.Current == 0 && !App.InTransition, 15);
        Check(App.Navigator.Current == 0, "Open… → This computer returns to the hub");
        await Key(Godot.Key.Backspace);
        await Until(() => App.Navigator.Current == lighthouse && !App.InTransition, 15);
        Check(App.Navigator.Current == lighthouse, "Backspace returns to the project (history kept)");
    }
}
