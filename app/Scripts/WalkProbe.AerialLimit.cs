using FileScape.Core.Places;
using Godot;

namespace FileScape.App;

/// <summary>
/// AERIAL-07 in the exported app (--automate=walk-aerial-limit --test-max-islands=N --root=&lt;Wayfarer fixture&gt;): with a
/// deliberately small aerial display budget, go by path to a deep place that breadth-first order alone would leave out,
/// rise into the aerial view and land, over this place and over open space. With a budget large enough for the way
/// down, the current place, its ancestry and its folders are shown; with a smaller one, the aerial view says it is
/// unavailable here and landing recovers in the place. Owned fixture only.
/// </summary>
public partial class WalkProbe
{
    private async Task AerialLimitJourney()
    {
        var limit = App.WorldLayoutParameters.MaxIslands;
        Log($"aerial display budget: {limit} places (--test-max-islands)");
        var deepPath = Tree.FullPath(App.Navigator.Current) + @"\Journeys\Coast\Harbour";
        await KeyCombo(Godot.Key.L, ctrl: true);
        await TypeText(deepPath);
        await Key(Godot.Key.Enter);
        await Until(() => App.Tree![App.Navigator.Current].Name == "Harbour" && App.Place != null && !App.InTransition, 20);
        var here = App.Navigator.Current;
        Check(Tree[here].Name == "Harbour", $"Go to path reaches {Tree.FullPath(here)}");
        await Seconds(1.5); // list-ahead fills in the neighbourhood: more folders compete for the budget

        var ancestry = Tree.Ancestors(here).ToList();
        var needed = ancestry.Count + 1;
        var bfs = WorldLayout.Compute(Tree, parameters: App.WorldLayoutParameters);
        Check(!bfs.Contains(here), $"premise: breadth-first alone keeps {bfs.Islands.Count} of {Tree.Nodes.Count(n => n.IsDirectory)} known folders and leaves Harbour out (depth {ancestry.Count})");

        // Rise into the aerial view: T, then (set-up) high above the place, then B frames the neighbourhood.
        await Key(Godot.Key.T);
        await Seconds(1.5);
        App.ProbeFlyAt(App.Rig.CameraPosition + new Vector3(0, 90, 0)); // [direct] well above the aerial altitude
        await Frames(6);
        await Key(Godot.Key.B);
        await Until(() => !App.GuidedGlide, 6);
        await Frames(10);
        var map = App.WorldMap!;
        Log($"aerial world: {map.Islands.Count} places, {map.Truncated} left out, focus shown {map.FocusShown}; chip: {App.Hud.AscentChipText}");
        Check(map.Islands.Count <= limit, $"the display budget holds ({map.Islands.Count} ≤ {limit})");

        if (needed <= limit)
        {
            Check(map.FocusShown && map.Focus == here, "the current place is in the aerial world");
            Check(ancestry.All(map.Contains), $"so is the whole way down to it ({ancestry.Count} places)");
            var kids = Tree[here].Children.Where(c => Tree[c].IsDirectory).ToList();
            Check(kids.Count == 0 || kids.Any(map.Contains), $"and its own folders ({kids.Count(map.Contains)} of {kids.Count})");
            Check(!App.AerialUnavailableHere && App.Aerial.Visible && App.Hud.AscentChipText.StartsWith("Aerial view ·", StringComparison.Ordinal), "the aerial world is drawn around you");
            await Shot("aerial-limit-focus-kept");

            // Landing over open space far from every place: the nearest place (the path that indexed the current island).
            var far = App.Rig.CameraPosition + new Vector3(0, 40, 0) + App.Rig.Camera.GlobalTransform.Basis.Z * 900f;
            App.ProbeFlyAt(far); // [direct] far out over nothing
            await Frames(6);
            await Key(Godot.Key.T);
            await Until(() => !App.InTransition && App.Locomotion == Locomotion.Walking && !App.LandingInProgress && !App.GuidedGlide, 15);
            await Seconds(0.8);
            Check(App.Locomotion == Locomotion.Walking && W.Grounded && App.Place != null, $"T far over open space lands you safely, in {Tree.DisplayName(App.Navigator.Current)} ({App.LastLandingNote ?? "-"})");
        }
        else
        {
            Check(!map.FocusShown, $"the way down needs {needed} places, more than the budget: the current place is reported missing, not faked");
            Check(App.AerialUnavailableHere && !App.Aerial.Visible, "no aerial world is drawn in a made-up frame");
            Check(App.Hud.AscentChipText.Contains("unavailable here", StringComparison.Ordinal), $"the chip says why: “{App.Hud.AscentChipText}”");
            await Shot("aerial-limit-unavailable");
            await Click(App.GetViewport().GetVisibleRect().Size * new Vector2(0.5f, 0.6f));
            await Frames(3);
            Check(App.Navigator.Current == here, "clicking where islands would be does nothing unexpected");

            var far = App.Rig.CameraPosition + App.Rig.Camera.GlobalTransform.Basis.Z * 600f;
            App.ProbeFlyAt(far); // [direct] over open space, where landing used to look up the current island
            await Frames(6);
            await Key(Godot.Key.T);
            await Until(() => !App.InTransition && App.Locomotion == Locomotion.Walking && !App.LandingInProgress && !App.GuidedGlide, 15);
            await Seconds(0.8);
            Check(App.Navigator.Current == here && App.Locomotion == Locomotion.Walking && W.Grounded && App.Place != null,
                $"T recovers: you land in Harbour, on foot ({App.LastLandingNote ?? "-"})");
            Check(App.LastAerialRecovery != null, $"the recovery is logged: {App.LastAerialRecovery}");
        }

        // Back to normal: Backspace returns to where you came from, and the aerial world works there.
        await Key(Godot.Key.Backspace);
        await WaitPlace();
        await Key(Godot.Key.T);
        await Seconds(1.5);
        App.ProbeFlyAt(App.Rig.CameraPosition + new Vector3(0, 90, 0)); // [direct]
        await Frames(10);
        var m2 = App.WorldMap!;
        Check(m2.FocusShown || (Tree.Ancestors(App.Navigator.Current).Count() + 1 > limit && App.AerialUnavailableHere),
            $"after going back to {Tree.DisplayName(App.Navigator.Current)} the aerial view follows ({m2.Islands.Count} places, focus shown {m2.FocusShown})");
        await Key(Godot.Key.T);
        await Until(() => App.Locomotion == Locomotion.Walking && !App.LandingInProgress && !App.GuidedGlide && !App.InTransition, 15);
        Check(App.Locomotion == Locomotion.Walking && App.Place != null, "and you land again on foot");
    }
}
