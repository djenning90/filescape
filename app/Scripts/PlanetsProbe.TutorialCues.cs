using System.Text.Json;
using FileScape.App.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-tutorial-cues --root=sample --vis=planets --test-shell=record</c> (PLANET-38, AD-55): the
/// walkthrough's target cues, played with injected input as a newcomer would. The first four steps with the cue checked
/// at each: none while looking (key caps instead), the tower's beacon pinned on screen, turning away on purpose until
/// the beacon is behind you (the edge arrow on the side to turn towards, the card's text saying so), the other side,
/// facing it again, the cue changing as you approach (near, then here, with the action pill); the launcher's beacon
/// hidden by the planet's curve (the chevron at the horizon); then all thirteen steps to the end, checking that the cue
/// follows each lesson (the bench's beacon after the flight, none for the key-only steps) and that it clears on Leave.
/// It reads the state the real-input probe reads (<c>Main.StateJson</c>'s <c>cue</c>), so both see the same thing.
/// Screenshots at every phase, to be looked at: the cue must not cover the view.
/// </summary>
public partial class PlanetsProbe
{
    private TutorialCueState Cue => P.TutorialCue;

    private async Task TutorialCuesRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5);
        var root = App.Navigator.Current;
        var view = App.GetViewport().GetVisibleRect().Size;

        await Key(Godot.Key.F1);
        await Frames(3);
        await Click(App.Hud.LearnToPlayButtonRect.GetCenter());
        await Until(() => App.TutorialStep == 0, 10);
        await Frames(5);
        Check(App.TutorialStep == 0 && Cue.Mode == TutorialCueMode.None && App.TutorialKeys == "drag+← →",
            $"step 1 (look): no place to go, so no beacon; key caps instead: '{App.TutorialKeys}', cue {Cue.Mode}");
        await Shot("cues-1-look-keycaps");
        await LeftDrag(new Vector2(500, 0));
        await LeftDrag(new Vector2(500, 0));
        await Until(() => App.TutorialStep == 1, 4);
        Check(App.TutorialStep == 1, $"looked round → {App.TutorialTitle}");

        // ---- step 2: the tower.
        var lm = P.Composition!.Landmark;
        await Until(() => Cue.Mode != TutorialCueMode.None, 3);
        Check(Cue.Mode != TutorialCueMode.None && P.TutorialTargetNow is { } t2 && NVector3.Distance(t2.Direction, lm.Direction) < 1e-4f,
            F($"step 2 shows a cue for the tower at once: {Cue.Mode} at ({Cue.Anchor.X:0},{Cue.Anchor.Y:0}), {Cue.DistanceMetres:0.0} m"));
        await Shot("cues-2-tower-start");

        // Turn away on purpose until the tower is behind you.
        await TurnUntil(lm.Direction, b => MathF.Abs(b) > 150f, Godot.Key.Left);
        await Frames(4);
        var behind = Cue;
        var rightSide = behind.BearingDegrees >= 0;
        Check(behind.Mode == TutorialCueMode.Edge && MathF.Abs(behind.BearingDegrees) > 150f,
            F($"turned away (bearing {behind.BearingDegrees:0}°): the edge arrow shows ({behind.Mode})"));
        Check(rightSide ? behind.Anchor.X > view.X * 0.85f && behind.AngleDegrees == 0 : behind.Anchor.X < view.X * 0.15f && MathF.Abs(behind.AngleDegrees) == 180,
            F($"  on the side to turn towards and pointing that way: x {behind.Anchor.X:0} of {view.X:0}, angle {behind.AngleDegrees:0}° for bearing {behind.BearingDegrees:0}°"));
        var cardBottom = App.LessonCardRect.End.Y;
        Check(behind.Anchor.Y - 50f > cardBottom, F($"  the arrow is clear of the lesson card (arrow top {behind.Anchor.Y - 50f:0} > card bottom {cardBottom:0})"));
        Check(App.LessonHint.Contains("behind you") && App.LessonHint.Contains(rightSide ? "right" : "left"), $"  the card's text backup agrees: '{App.LessonHint}'");
        CheckStateCue("edge", rightSide ? 0 : 180);
        await Shot("cues-3-tower-behind");

        // Round to the other side (the arrow swaps sides), then face it.
        // Turning left moves a target behind-right round to the left (and vice versa).
        await TurnUntil(lm.Direction, b => MathF.Abs(b) is > 60f and < 110f && (b >= 0) != rightSide, rightSide ? Godot.Key.Left : Godot.Key.Right, 6);
        await Frames(4);
        Check(Cue.Mode == TutorialCueMode.Edge && (Cue.BearingDegrees >= 0 ? Cue.AngleDegrees == 0 && Cue.Anchor.X > view.X * 0.85f : MathF.Abs(Cue.AngleDegrees) == 180 && Cue.Anchor.X < view.X * 0.15f),
            F($"to the side (bearing {Cue.BearingDegrees:0}°): edge arrow at x {Cue.Anchor.X:0}, angle {Cue.AngleDegrees:0}°"));
        Check(!App.LessonHint.Contains("behind") && App.LessonHint.Length > 0, $"  the text says which side: '{App.LessonHint}'");
        await Shot("cues-4-tower-side");
        await TurnUntil(lm.Direction, b => MathF.Abs(b) < 6f, Godot.Key.Left);
        await Frames(4);
        // From across the planet the tower is behind the curve (the chevron waits at the horizon); either way the cue is
        // in front, in its direction.
        Check(Cue.Mode is TutorialCueMode.OnScreen or TutorialCueMode.Horizon && MathF.Abs(Cue.Anchor.X - view.X / 2) < view.X * 0.25f && Cue.AngleDegrees == 90,
            F($"facing it: the chevron is ahead, in its direction ({Cue.Mode} at {Cue.Anchor.X:0},{Cue.Anchor.Y:0}; over the horizon {Cue.OverHorizon}; phase {Cue.Phase}, {Cue.DistanceMetres:0.0} m)"));
        Check(App.LessonHint.Length == 0, "  no turn hint while it is in view");
        CheckStateCue(Cue.Mode == TutorialCueMode.Horizon ? "horizon" : "onscreen", 90);
        var farY = Cue.Anchor.Y;
        await Shot("cues-5-tower-ahead");

        // Walk on until the tower's chevron pins over it (over the brow), still far.
        await WalkUntil(lm.Direction, () => Cue.Mode == TutorialCueMode.OnScreen || App.TutorialStep != 1, 10);
        Check(Cue.Mode == TutorialCueMode.OnScreen && App.TutorialStep == 1,
            F($"over the brow the chevron pins over the tower ({Cue.Mode}, {Cue.Phase}, {Cue.DistanceMetres:0.0} m, at {Cue.Anchor.X:0},{Cue.Anchor.Y:0})"));
        CheckStateCue("onscreen", 90);
        await Shot("cues-6-tower-onscreen");

        // Approach: the cue changes when near (before the step completes).
        if (Cue.Phase == TutorialCuePhase.Far)
        {
            await WalkUntil(lm.Direction, () => Cue.Phase != TutorialCuePhase.Far || App.TutorialStep != 1, 12);
            // Since walking eases the camera back to its usual pitch (PLANET-84), the low walking view can see the ring's centre
            // just past the planet's brow: the cue is then pinned at the horizon over the tower (the screenshot shows the chevron
            // on the tower and the ring in view), which is as good as on screen for a player walking up to it.
            var shown = Cue.Mode == TutorialCueMode.OnScreen
                || (Cue.Mode == TutorialCueMode.Horizon && Cue.Anchor.X > 0 && Cue.Anchor.X < view.X && Cue.Anchor.Y > 0 && Cue.Anchor.Y < view.Y);
            Check(App.TutorialStep == 1 && Cue.Phase == TutorialCuePhase.Near && shown && Cue.Action.Length > 0,
                F($"approaching: the cue says near ({Cue.Phase}, mode {Cue.Mode}, step {App.TutorialStep}, {Cue.DistanceMetres:0.0} m from the ring; chevron at y {Cue.Anchor.Y:0}, was {farY:0}; pill '{Cue.Action}')"));
            await Shot("cues-7-tower-near");
        }
        else
        {
            Log($"(already near the tower: {Cue.Phase})");
        }

        await AwaitFocus();
        await WalkToward(lm.Direction, lm.Radius + 2.0f, 12);
        await Until(() => App.TutorialStep == 2, 4);
        Check(App.TutorialStep == 2, F($"into the ring → {App.TutorialTitle}"));
        await Frames(3);
        Check(Cue.Mode == TutorialCueMode.None, $"between steps the cue clears ({Cue.Mode})");
        await Until(() => App.TutorialStep == 2 && App.TutorialKeys == "Space", 3);
        await Frames(3);
        Check(Cue.Mode == TutorialCueMode.None && App.TutorialKeys == "Space", $"step 3 (jump): no beacon, key cap '{App.TutorialKeys}'");

        await AwaitFocus();
        await Key(Godot.Key.Space);
        await Until(() => App.TutorialStep == 3, 4);
        Check(App.TutorialStep == 3, $"jumped → {App.TutorialTitle}");

        // ---- step 4: the launcher; the cue moves to it.
        var workshop = Child(root, "Lantern Workshop");
        var pad = P.Composition!.Launchers.First(l => l.TargetId == workshop);
        await Until(() => Cue.Mode != TutorialCueMode.None, 3);
        Check(P.TutorialTargetNow is { } tp && NVector3.Distance(tp.Direction, pad.Direction) < 1e-4f && Cue.Action.Contains("Space"),
            $"the lesson advanced and the cue followed: now the Lantern Workshop launcher ('{Cue.Action}')");
        await Shot("cues-8-pad-start");

        // Hidden by the planet's curve: face the pad from far enough away that the ground hides it.
        var horizonSeen = false;
        for (var i = 0; i < 8 && !horizonSeen; i++)
        {
            await TurnUntil(pad.Direction, b => MathF.Abs(b) < 5f, Godot.Key.Left);
            await Frames(4);
            if (Cue.Mode == TutorialCueMode.Horizon) { horizonSeen = true; break; }
            Log(F($"  facing the pad {Cue.DistanceMetres:0.0} m away: {Cue.Mode}; walking away from it"));
            await TurnUntil(pad.Direction, b => MathF.Abs(b) > 170f, Godot.Key.Left);
            HoldKey(Godot.Key.W, true);
            await Seconds(0.7);
            HoldKey(Godot.Key.W, false);
            await Frames(3);
        }

        Check(horizonSeen && Cue.Anchor.X > 70 && Cue.Anchor.X < view.X - 70,
            F($"behind the curve ({Cue.DistanceMetres:0.0} m away): the chevron waits at the horizon in its direction ({Cue.Mode} at {Cue.Anchor.X:0},{Cue.Anchor.Y:0})"));
        CheckStateCue("horizon", 90);
        await Shot("cues-9-pad-horizon");

        await WalkUntil(pad.Direction, () => Cue.Mode == TutorialCueMode.OnScreen, 15);
        Check(Cue.Mode == TutorialCueMode.OnScreen, F($"over the brow the chevron pins onto the pad ({Cue.Phase}, {Cue.DistanceMetres:0.0} m)"));
        await Shot("cues-10-pad-onscreen");
        await AwaitFocus();
        await WalkToward(pad.Direction, 0.6f, 25);
        await Frames(4);
        Check(Cue.Phase == TutorialCuePhase.Here && Cue.Action.Contains("Space") && App.TutorialStep == 3,
            $"on the pad: phase {Cue.Phase}, the pill says '{Cue.Action}' ({Cue.Mode})");
        await Shot("cues-11-pad-here");
        await AwaitFocus();
        await Fly(Godot.Key.Space, workshop, "Lantern Workshop");
        await Until(() => App.TutorialStep == 4, 4);
        Check(App.TutorialStep == 4, $"step 5 of 14 reached (the first four done) → {App.TutorialTitle}");

        // ---- step 5: the bench; the cue moved to this world.
        var ov = P.Composition!.Overlook;
        await Until(() => Cue.Mode != TutorialCueMode.None, 4);
        Check(P.TutorialTargetNow is { } tb && NVector3.Distance(tb.Direction, ov.Direction) < 1e-4f && Cue.Mode != TutorialCueMode.None,
            $"after the flight the cue is the workbench on the new world ({Cue.Mode}, '{Cue.Action}')");
        await Shot("cues-12-bench");
        await AwaitFocus();
        await WalkToward(ov.Direction, ov.Radius + 1.0f, 20);
        HoldKey(Godot.Key.W, true);
        await Frames(4);
        await AwaitFocus();
        await Key(Godot.Key.Space);
        await Until(() => P.BenchRevealed, 3);
        HoldKey(Godot.Key.W, false);
        await Until(() => App.TutorialStep == 5, 4);
        Check(App.TutorialStep == 5, $"jumped onto the bench → {App.TutorialTitle}");
        await Until(() => P.Walker.Grounded, 2);
        await Until(() => Cue.Mode != TutorialCueMode.None, 3);
        Check(Cue.Action.Contains("Press E") && Cue.Phase == TutorialCuePhase.Here, $"on the bench: the pill says '{Cue.Action}' ({Cue.Phase}, {Cue.Mode})");
        await Shot("cues-13-bench-press-e");
        await Frames(10);
        await AwaitFocus();
        await Key(Godot.Key.E);
        await Until(() => App.TutorialStep == 6, 5);
        Check(App.TutorialStep == 6, $"E read the README → {App.TutorialTitle}");
        await AwaitFocus();
        await Key(Godot.Key.Escape);
        await Frames(5);

        // ---- steps 7–14: no place to go; key caps, no beacon.
        async Task KeyStep(int step, string keys, Func<Task> act, Func<bool> after, string what)
        {
            await Until(() => App.TutorialStep == step, 4);
            await Frames(3);
            Check(App.TutorialStep == step && App.TutorialKeys == keys && Cue.Mode == TutorialCueMode.None,
                $"step {step + 1}: key caps '{App.TutorialKeys}', no beacon ({Cue.Mode})");
            await AwaitFocus();
            await act();
            await Until(after, 12);
            await Until(() => App.TutorialStep == step + 1 || App.TutorialTitle == "done", 5);
            Check(App.TutorialStep == step + 1 || App.TutorialTitle == "done", $"  {what} → {App.TutorialTitle}");
        }

        var musicBox = Child(root, "Music Box");
        await KeyStep(6, "U", () => Key(Godot.Key.U), () => App.Navigator.Current == root && !P.Busy, "U went up");
        await Shot("cues-14-keycap-j");
        await KeyStep(7, "J+↓+Enter", async () =>
        {
            await Key(Godot.Key.J);
            await Frames(4);
            await TypeText("music");
            await Frames(3);
            await Key(Godot.Key.Down);
            await Frames(2);
            await Key(Godot.Key.Enter);
        }, () => App.Navigator.Current == musicBox && !P.Busy, "J list → Music Box");
        await KeyStep(8, "U", () => Key(Godot.Key.U), () => App.Navigator.Current == root && !P.Busy, "U → the Starter Sky");
        await KeyStep(9, "Backspace", () => Key(Godot.Key.Backspace), () => App.Navigator.Current == musicBox && !P.Busy, "Backspace → Music Box");
        await Seconds(1);
        await KeyStep(10, "T", () => Key(Godot.Key.T), () => P.UniverseOpen, "T opened the overview");
        await Seconds(1);
        await KeyStep(11, "drag", async () =>
        {
            var yaw0 = P.UniverseYaw;
            // Until the step's "✓" shows (it stays on step 12 for the celebration).
            for (var i = 0; i < 3 && App.TutorialStep == 11 && !App.LessonHint.StartsWith('✓'); i++)
            {
                await LeftDrag(new Vector2(300, 0));
                await Frames(10);
                Log(F($"  orbit drag {i + 1}: overview yaw moved {P.UniverseYaw - yaw0:0.00} rad"));
            }
        }, () => true, "orbited");
        await KeyStep(12, "wheel ↓", async () =>
        {
            await Seconds(1.2); // the opening pull-back settles
            for (var i = 0; i < 30 && App.TutorialStep == 12 && !App.LessonHint.StartsWith('✓'); i++) { await Wheel(up: false, 1); await Seconds(0.35); }
        }, () => true, "zoomed out with the wheel");
        await KeyStep(13, "Esc", () => Key(Godot.Key.Escape), () => !P.UniverseOpen && !P.Busy, "Esc back where you stood");
        await Until(() => App.TutorialTitle == "done", 8);
        Check(App.TutorialTitle == "done" && Cue.Mode == TutorialCueMode.None, $"all 14 steps done; no cue left behind ({Cue.Mode})");
        await Shot("cues-15-done");

        // ---- Leave clears the cue mid-lesson.
        await Click(App.LessonButtonRect("restart").GetCenter());
        await Until(() => App.TutorialStep == 0, 15);
        await Click(App.LessonButtonRect("skip").GetCenter());
        await Until(() => App.TutorialStep == 1 && Cue.Mode != TutorialCueMode.None, 4);
        Check(App.TutorialStep == 1 && Cue.Mode != TutorialCueMode.None, $"Start over, skip to the tower: the cue is back ({Cue.Mode})");
        await Shot("cues-16-before-leave");
        await Click(App.LessonButtonRect("leave").GetCenter());
        await Frames(4);
        Check(!App.TutorialActive && Cue.Mode == TutorialCueMode.None && P.TutorialTargetNow == null && !HasBeaconNode(),
            $"Leave clears the cue: tutorial {App.TutorialActive}, cue {Cue.Mode}, beacon {(HasBeaconNode() ? "still there" : "gone")}");
        CheckStateCue("none", null);
        await Shot("cues-17-after-leave");
    }

    private bool HasBeaconNode() => P.FindChild("TutorialBeacon", recursive: false, owned: false) is Node n && GodotObject.IsInstanceValid(n) && !n.IsQueuedForDeletion();

    /// <summary>The state file's <c>cue</c> (what the real-input probe reads) agrees with what is drawn.</summary>
    private void CheckStateCue(string mode, float? angle)
    {
        using var doc = JsonDocument.Parse(App.StateJson());
        var cue = doc.RootElement.GetProperty("cue");
        var m = cue.GetProperty("mode").GetString();
        var a = cue.GetProperty("angle").GetSingle();
        Check(m == mode && (angle == null || MathF.Abs(a - angle.Value) < 0.5f),
            F($"  state file: cue mode '{m}', angle {a:0}, at [{cue.GetProperty("x").GetSingle():0},{cue.GetProperty("y").GetSingle():0}], phase {cue.GetProperty("phase").GetString()}, keys '{cue.GetProperty("keys").GetString()}'"));
    }

    /// <summary>Turns the camera with an arrow key (as a player would) until <paramref name="done"/> holds for the bearing to a spot.</summary>
    private async Task TurnUntil(NVector3 dir, Func<float, bool> done, Godot.Key preferred, double timeoutSeconds = 8)
    {
        var start = Time.GetTicksMsec();
        // The camera's turn eases on after a key is let go, so a stop inside the window can drift out of it: wait for it
        // to settle and aim again (up to three passes; full-tier run 2026-11-17 overshot from −99° to past +60°).
        for (var pass = 0; pass < 3; pass++)
        {
            await TurnPass(dir, done, preferred, start, timeoutSeconds);
            await Frames(20);
            if (done(P.CameraTurnTo(dir))) break;
        }
    }

    private async Task TurnPass(NVector3 dir, Func<float, bool> done, Godot.Key preferred, ulong start, double timeoutSeconds)
    {
        while (!done(P.CameraTurnTo(dir)) && (Time.GetTicksMsec() - start) / 1000.0 < timeoutSeconds)
        {
            var b = P.CameraTurnTo(dir);
            // Fine aim: towards the bearing; otherwise keep turning the preferred way.
            // Facing a target (done at bearing 0): always turn towards it, so an overshoot is corrected rather than carried
            // round the whole circle the preferred way; other goals (turn it behind, to a side) keep the preferred way.
            var key = done(0) ? (b > 0 ? Godot.Key.Right : Godot.Key.Left) : preferred;
            Log(F($"    turn: bearing {b:0.#}°, {key}"));
            await AwaitFocus();
            HoldKey(key, true);
            await Frames(1);
            HoldKey(key, false);
        }

        await Frames(2);
    }

    /// <summary>Walks towards a spot (turning to face it first) until <paramref name="stop"/> holds or the timeout passes.</summary>
    private async Task WalkUntil(NVector3 dir, Func<bool> stop, double timeoutSeconds)
    {
        var start = Time.GetTicksMsec();
        while (!stop() && (Time.GetTicksMsec() - start) / 1000.0 < timeoutSeconds)
        {
            var b = P.CameraTurnTo(dir);
            if (MathF.Abs(b) > 10f)
            {
                var k = b > 0 ? Godot.Key.Right : Godot.Key.Left;
                await AwaitFocus();
                HoldKey(k, true);
                await Frames(2);
                HoldKey(k, false);
                continue;
            }

            await AwaitFocus();
            HoldKey(Godot.Key.W, true);
            await Frames(2);
            HoldKey(Godot.Key.W, false);
        }

        HoldKey(Godot.Key.W, false);
        await Frames(2);
    }

    /// <summary>
    /// Waits (up to a minute) while the window is not focused: injected input is ignored then (KD-31), and on a machine
    /// shared with other windows focus can leave for a while. A wait is logged so a failure beside it reads as the
    /// environment, not the product.
    /// </summary>
    private async Task AwaitFocus()
    {
        if (App.WindowFocused) return; // always under --automate now (Main ignores focus loss for injected input)
        var start = Time.GetTicksMsec();
        await Until(() => App.WindowFocused, 60);
        Log(F($"  (waited {(Time.GetTicksMsec() - start) / 1000.0:0.0} s for the window's focus: {(App.WindowFocused ? "back" : "still away")})"));
    }
}
