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
