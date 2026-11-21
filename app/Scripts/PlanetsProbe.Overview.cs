using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-overview-routes</c> (FOLLOW-1, 2026-11-17): DJ saw a blank overview (T) in the sample world on
/// the installed build, where an earlier probe had counted 62 sample worlds. This plays the ordinary routes into the
/// sample — launched straight into it (<c>--root=sample</c>), or switched to it with Sample world from This computer
/// (<c>--root=hub</c>) or from a live folder (<c>--root=&lt;fixture&gt;</c>) — then opens the overview and checks what is
/// <i>drawn</i>: bodies inside the view and at least a few pixels wide (<c>PlanetWorld.UniverseOnScreen</c>), plus a
/// screenshot. Then it lands on a drawn world, comes back through history, and checks that T then Esc returns to the
/// exact standing spot. Started on the sample it also opens the overview from the walkthrough's overview step.
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>The fewest bodies an overview of the sample must draw (its own system has ten worlds and the way back).</summary>
    private const int OverviewMinDrawn = 4;

    /// <summary>A world reads as a world at this apparent diameter or more, pixels.</summary>
    private const float OverviewVisiblePixels = 8f;

    private async Task OverviewRoutes()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 40);
        await WaitSettled();
        await Seconds(3);
        var startedOn = Tree.DisplayName(App.Navigator.Current);
        var sample = Tree.DisplayName(App.Navigator.Current) == "Starter Sky";
        if (!sample)
        {
            Log($"started on {startedOn}: Sample world from the toolbar");
            App.OpenStarterSky(); // [direct] what the toolbar's Sample world button calls
            await Until(() => App.Tree is { Count: > 1 } && P.Composition != null && Tree.DisplayName(App.Navigator.Current) == "Starter Sky" && !P.Busy, 40);
            await WaitSettled();
            await Seconds(3);
        }

        var root = App.Navigator.Current;
        Check(Tree.DisplayName(root) == "Starter Sky", $"in the Starter Sky (started on {startedOn}; path {Tree.FullPath(root)})");
        await OverviewOpenAndLook($"from {(sample ? "a launch into the sample" : startedOn)}");
        await OverviewNamesAndReach(root);

        // Land on a drawn world, come back through history.
        // Only a world inside the sample: an ancestor (the folders above it on this PC) is never landed on by a probe.
        bool InSample(int id) { for (var a = id; a >= 0 && Tree.Contains(a); a = Tree[a].ParentId) if (a == root) return true; return false; }
        var drawn = P.UniverseOnScreen().Where(b => b.Id != root && Tree.Contains(b.Id) && InSample(b.Id)).OrderByDescending(b => b.Pixels).ToList();
        if (Check(drawn.Count > 0, "a world other than this one is drawn to click"))
        {
            var target = drawn[0];
            await Click(target.At);
            await Frames(10);
            Check(P.UniverseSelected == target.Id, $"a click selects {Tree.DisplayName(target.Id)} ({target.Pixels:0} px wide)");
            await Key(Godot.Key.Enter);
            await Until(() => App.Navigator.Current == target.Id && !P.Busy && !P.UniverseOpen, 15);
            await Seconds(1.5);
            await Shot("overview-landed");
            Check(App.Navigator.Current == target.Id && !P.UniverseOpen, $"Enter lands on {Tree.DisplayName(App.Navigator.Current)}");
            await Key(Godot.Key.Backspace);
            await Until(() => App.Navigator.Current == root && !P.Busy, 15);
            await Seconds(1.5);
            Check(App.Navigator.Current == root, $"Backspace returns to {Tree.DisplayName(App.Navigator.Current)}");
        }

        // T, then Esc: back on the exact spot.
        await WaitSettled();
        var feet = P.Walker.Feet;
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 5);
        await Seconds(2);
        await Key(Godot.Key.Escape);
        await Until(() => !P.UniverseOpen && !P.Busy, 10);
        await Seconds(1);
        var moved = (P.Walker.Feet - feet).Length();
        Check(!P.UniverseOpen && App.Navigator.Current == root && moved < 0.25f, F($"Esc comes back down to where you stood ({moved:0.00} m away)"));

        if (sample)
        {
            // The walkthrough's overview step (its T).
            await Key(Godot.Key.F1);
            await Frames(3);
            await Click(App.Hud.LearnToPlayButtonRect.GetCenter());
            await Until(() => App.TutorialStep == 0, 10);
            for (var i = 0; i < 14 && App.TutorialStep < 10; i++)
            {
                await Click(App.LessonButtonRect("skip").GetCenter());
                await Frames(4);
            }

            await Until(() => !P.Busy, 15);
            Check(App.TutorialStep == 10, $"the walkthrough's overview step: {App.TutorialTitle}");
            await OverviewOpenAndLook("from the walkthrough");
            await Key(Godot.Key.Escape);
            await Until(() => !P.UniverseOpen && !P.Busy, 10);
            await Click(App.LessonButtonRect("leave").GetCenter());
        }
    }

    /// <summary>
    /// Seventh pass (PLANET-192..194) in the open overview: the names are placed round their worlds without faults (no
    /// overlaps, no crossing leaders, none under a name), at most <see cref="Ui.SignLayer.MaxAroundLabels"/>, the selection
    /// and where you stand among them; a click on a name focuses the world it names and a double-click on one never lands;
    /// the wheel zooms out to the derived reach (<see cref="Planets.PlanetWorld.UniverseMaxZoomMetres"/>, never under 6 km).
    /// Ends with where you stand selected again and the camera back in.
    /// </summary>
    private async Task OverviewNamesAndReach(int root)
    {
        await Frames(4);
        var placed = App.Signs.AroundPlaced;
        var faults = Core.Interaction.LabelPlacement.Faults(placed);
        Log($"  names: {placed.Count} placed of {App.Signs.InView} asked in view; overlaps {faults.Overlaps}, crossing leaders {faults.Crossings}, leaders under a name {faults.UnderLabels}");
        Check(faults == (0, 0, 0) && placed.Count <= Ui.SignLayer.MaxAroundLabels, $"the overview's names do not overlap and no leader crosses another or runs under a name ({placed.Count} names)");
        Check(App.Signs.ShownIds.Contains(P.UniverseSelected), $"the selected world ({Tree.DisplayName(P.UniverseSelected)}) is named first");
        await Shot("overview-names");
        var other = placed.Where(p => p.Id != P.UniverseSelected && Tree.Contains(p.Id)).Select(p => p.Id).FirstOrDefault(-1);
        if (other >= 0 && App.Signs.ShownRectById.TryGetValue(other, out var rect))
        {
            var focuses = P.UniverseLabelFocuses;
            await Click(rect.GetCenter());
            await Frames(10);
            Check(P.UniverseSelected == other && P.UniverseLabelFocuses == focuses + 1 && P.UniverseOpen, $"a click on the name {Tree.DisplayName(other)} focuses that world (not one under the name)");
            await Seconds(1.2);
            if (App.Signs.ShownRectById.TryGetValue(other, out var again))
            {
                await DoubleClickAt(again.GetCenter());
                await Frames(10);
                Check(P.UniverseOpen && !P.Busy && P.UniverseSelected == other && P.UniverseLabelFocuses >= focuses + 2, "a double-click on its name focuses it and stays in the overview (landing is Enter or a double-click on the world)");
            }
        }
        else Log("(no second name to click in this view)");

        // The wheel's reach (PLANET-192): out until it stops.
        var max = P.UniverseMaxZoomMetres();
        var centre = P.GetViewport().GetVisibleRect().Size / 2;
        await WheelAt(centre, down: true, 60);
        await Seconds(1.5);
        var reached = P.UniverseZoom.Target;
        Log(F($"  the wheel's reach: {reached:0} m (limit {max:0} m; the universe is {P.UniverseExtentMetres:0} m from its root)"));
        Check(reached >= max * 0.98f && max >= Core.Planets.OverviewFraming.FloorMetres, F($"the wheel zooms out to the derived reach ({reached:0} of {max:0} m, never under 6 km)"));
        await Shot("overview-reach");
        P.SelectInUniverse(App.Navigator.Current); // [direct] where you stand chosen again, the camera back in (as a click on it)
        await Seconds(1.5);
    }

    /// <summary>T, a moment for the pull-back, then what is drawn: a count, sizes, a screenshot.</summary>
    private async Task OverviewOpenAndLook(string how)
    {
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 5);
        await Seconds(2.5); // the pull-back settles
        var drawn = P.UniverseOnScreen();
        var (metres, focus) = P.UniverseFraming;
        await Shot("overview-" + how.Replace(' ', '-'));
        Log(F($"  overview {how}: {P.UniverseBodyCount} bodies laid out, {drawn.Count} drawn (largest {(drawn.Count > 0 ? drawn.Max(d => d.Pixels) : 0):0} px), camera {metres:0} m from {(Tree.Contains(focus) ? Tree.DisplayName(focus) : "?")}"));
        // Worlds must read as worlds, not specks: at least OverviewMinDrawn of them 8 px or more across (DJ's blank overview
        // had ten "in view" at 2–5 px).
        var visible = drawn.Count(d => d.Pixels >= OverviewVisiblePixels);
        Check(visible >= OverviewMinDrawn, $"the overview {how} draws worlds you can see: {visible} at ≥ {OverviewVisiblePixels:0} px of {drawn.Count} in view ({P.UniverseBodyCount} laid out)");
    }
}
