using FileScape.App.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-tutorial</c> (PLANET-33): plays the whole walkthrough on the Starter Sky with injected input,
/// as a player would — opening it from Help, left-drag look, walking to each target, jumping, launching, the workbench
/// (jump on, E), U, the J list, Backspace, the overview round trip — plus a wrong turn recovered with "Back to the
/// lesson" and a skipped step. Each step must advance only when its action happens. Real Windows input is checked
/// separately by <c>tools/os-planets-probe.ps1 -Tutorial</c>.
/// </summary>
public partial class PlanetsProbe
{
    private async Task TutorialRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5);
        var root = App.Navigator.Current;

        // Reopen from Help (F1, then the Learn to play button).
        await Key(Godot.Key.F1);
        await Frames(3);
        await Click(App.Hud.LearnToPlayButtonRect.GetCenter());
        await Until(() => App.TutorialStep == 0, 10);
        Check(App.TutorialStep == 0 && App.TutorialTitle.Contains("Look around"), $"Help → Learn to play starts the walkthrough: {App.TutorialTitle}");
        await Shot("tutorial-1-look");

        // 1 · Look around: nothing advances until the camera really turns.
        await Seconds(0.8);
        Check(App.TutorialStep == 0, "the step waits for the action (no turn yet)");
        await LeftDrag(new Vector2(500, 0));
        await LeftDrag(new Vector2(500, 0));
        await Until(() => App.TutorialStep == 1, 4);
        Check(App.TutorialStep == 1, $"left-drag look done → {App.TutorialTitle}");

        // 1 · Walk to the tower.
        await Shot("tutorial-2-walk");
        var lm = P.Composition!.Landmark;
        await WalkToward(lm.Direction, lm.Radius + 2.5f, 12);
        await Until(() => App.TutorialStep == 2, 4);
        Check(App.TutorialStep == 2, F($"walked to the landmark ({P.DistanceTo(lm.Direction):0.0} m) → {App.TutorialTitle}"));

        // 2 · Jump.
        await Key(Godot.Key.Space);
        await Until(() => App.TutorialStep == 3, 4);
        Check(App.TutorialStep == 3, $"jumped → {App.TutorialTitle}");

        // A wrong turn: fly somewhere else, then "Back to the lesson".
        var musicBox = Child(root, "Music Box");
        App.Selection.Select(musicBox); // [direct] as a click on its world
        await Frames(2);
        await Fly(Godot.Key.G, musicBox, "Music Box (a wrong turn)");
        await Until(() => App.TutorialOffRoute, 4);
        Check(App.TutorialOffRoute && App.TutorialStep == 3, "a wrong turn: the card offers Back to the lesson and keeps the step");
        await Shot("tutorial-wrong-turn");
        await Click(App.LessonButtonRect("back").GetCenter());
        await Until(() => App.Navigator.Current == root && !P.Busy, 12);
        await Seconds(1);
        Check(App.Navigator.Current == root && App.TutorialStep == 3, "Back to the lesson returns to the Starter Sky, same step");

        // 2 · Fly to Lantern Workshop: walk onto its pad, press Space.
        var workshop = Child(root, "Lantern Workshop");
        var pad = P.Composition!.Launchers.First(l => l.TargetId == workshop);
        await WalkToward(pad.Direction, 0.6f, 25);
        await Fly(Godot.Key.Space, workshop, "Lantern Workshop");
        await Until(() => App.TutorialStep == 4, 4);
        Check(App.TutorialStep == 4, $"launched into Lantern Workshop → {App.TutorialTitle}");
        await Shot("tutorial-4-bench");

        // 3 · Jump onto the workbench, then E.
        var ov = P.Composition!.Overlook;
        await WalkToward(ov.Direction, ov.Radius + 1.0f, 20);
        HoldKey(Godot.Key.W, true);
        await Frames(4);
        await Key(Godot.Key.Space);
        await Until(() => P.BenchRevealed, 3);
        HoldKey(Godot.Key.W, false);
        await Until(() => App.TutorialStep == 5, 4);
        Check(App.TutorialStep == 5, $"jumped onto the workbench → {App.TutorialTitle}");
        await Until(() => P.Walker.Grounded, 2);
        await Frames(10);
        await Key(Godot.Key.E);
        await Until(() => App.TutorialStep == 6, 5);
        Check(App.TutorialStep == 6, $"E read the README → {App.TutorialTitle}");
        await Shot("tutorial-5-readme");
        await Key(Godot.Key.Escape);
        await Frames(5);

        // 4 · U, J → Music Box, U, Backspace.
        await Key(Godot.Key.U);
        await Until(() => App.Navigator.Current == root && !P.Busy, 12);
        await Until(() => App.TutorialStep == 7, 4);
        Check(App.TutorialStep == 7, $"U went up → {App.TutorialTitle}");
        await Key(Godot.Key.J);
        await Frames(4);
        await TypeText("music");
        await Frames(3);
        await Key(Godot.Key.Down);
        await Frames(2);
        await Key(Godot.Key.Enter);
        await Until(() => App.Navigator.Current == musicBox && !P.Busy, 12);
        await Until(() => App.TutorialStep == 8, 4);
        Check(App.TutorialStep == 8, $"J list → Music Box → {App.TutorialTitle}");
        await Key(Godot.Key.U);
        await Until(() => App.Navigator.Current == root && !P.Busy, 12);
        await Until(() => App.TutorialStep == 9, 4);
        Check(App.TutorialStep == 9, $"U → the Starter Sky → {App.TutorialTitle}");
        await Key(Godot.Key.Backspace);
        await Until(() => App.Navigator.Current == musicBox && !P.Busy, 12);
        await Until(() => App.TutorialStep == 10, 4);
        Check(App.TutorialStep == 10, $"Backspace → Music Box (history, not up) → {App.TutorialTitle}");

        // 5 · The overview: open, orbit, Esc back to the same spot.
        await Seconds(1);
        await Key(Godot.Key.T);
        await Until(() => App.TutorialStep == 11, 5);
        Check(App.TutorialStep == 11, $"T opened the overview → {App.TutorialTitle}");
        await Seconds(1);
        await LeftDrag(new Vector2(300, 0));
        await Until(() => App.TutorialStep == 12, 4);
        Check(App.TutorialStep == 12, $"orbited the overview → {App.TutorialTitle}");
        await Shot("tutorial-overview");
        await Key(Godot.Key.Escape);
        await Until(() => App.TutorialTitle == "done", 8);
        Check(App.TutorialTitle == "done", "Esc back where you stood: the walkthrough is done and offers This computer");
        await Shot("tutorial-done");

        // Replay, skip a step, and leave mid-lesson.
        await Click(App.LessonButtonRect("restart").GetCenter());
        await Until(() => App.TutorialStep == 0, 15);
        Check(App.TutorialStep == 0, "Start over replays from the first step");
        await Click(App.LessonButtonRect("skip").GetCenter());
        await Frames(3);
        Check(App.TutorialStep == 1, $"Skip step goes on: {App.TutorialTitle}");
        await Click(App.LessonButtonRect("leave").GetCenter());
        await Frames(3);
        Check(!App.TutorialActive, "Leave closes the walkthrough mid-lesson");
    }

    /// <summary>A left-drag in the middle of the view (the walkthrough's look control).</summary>
    private async Task LeftDrag(Vector2 pixels, int steps = 10)
    {
        var centre = ToWindow(App.GetViewport().GetVisibleRect().Size / 2 + new Vector2(0, 80));
        Mouse(MouseButton.Left, true, centre);
        await Frames(2);
        for (var i = 0; i < steps; i++) { Motion(pixels / steps); await Frames(1); }
        Mouse(MouseButton.Left, false, centre);
        await Frames(3);
    }

    /// <summary>
    /// Walks towards a surface direction as a player would: turn the camera with ← → until it faces the spot, run with W,
    /// stop within <paramref name="stopMetres"/> (a jump over small obstacles when stuck).
    /// </summary>
    private async Task WalkToward(NVector3 dir, float stopMetres, double timeoutSeconds)
    {
        var start = Time.GetTicksMsec();
        var lastDist = float.MaxValue;
        var stuck = 0;
        while ((Time.GetTicksMsec() - start) / 1000.0 < timeoutSeconds)
        {
            var d = P.DistanceTo(dir);
            if (d <= stopMetres) break;
            var bearing = P.CameraTurnTo(dir);
            if (MathF.Abs(bearing) > 12)
            {
                var key = bearing > 0 ? Godot.Key.Right : Godot.Key.Left;
                HoldKey(key, true);
                await Seconds(Math.Min(0.4, MathF.Abs(bearing) / 110.0));
                HoldKey(key, false);
                continue;
            }

            HoldKey(Godot.Key.W, true);
            await Seconds(d > 4 ? 0.3 : 0.12);
            HoldKey(Godot.Key.W, false);
            stuck = d > lastDist - 0.05f ? stuck + 1 : 0;
            lastDist = d;
            if (stuck >= 3)
            {
                HoldKey(Godot.Key.W, true);
                await Key(Godot.Key.Space);
                await Seconds(0.5);
                HoldKey(Godot.Key.W, false);
                stuck = 0;
            }
        }

        HoldKey(Godot.Key.W, false);
        await Frames(3);
    }
}
