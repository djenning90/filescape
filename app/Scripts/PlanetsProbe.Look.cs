using FileScape.App.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-look --root=sample [--prefs=PATH]</c> (PLANET-67/68, AD-67): vertical camera look on the Starter
/// Sky. DJ (playtest of <c>bcde9b33b338</c>): left- and right-drag turn the camera sideways, but dragging up or down
/// seems locked out. This drags vertically through the same input path as a mouse (press, relative motion, release) in
/// every place the chase camera has its own framing — standing on arrival (the opening shot), after walking, while
/// walking, just after a landing, the overlook view (B), a Hazard World (the hazard view's pitch cap), and the overview
/// (T) — and measures what the player sees: the view's elevation (<see cref="PlanetWorld.ViewPitchDegrees"/>) before the
/// drag, once the camera has settled after it, and again a while later (nothing may level it back). It also sweeps the
/// pitch in small drags from one end to the other (no dead band), checks the wheel still zooms during the arrival
/// framing, and that the arrival framing itself is unchanged until you look. The saved look settings are logged; run it
/// with each <c>--prefs</c> variant (the planets camera does not use the Places reverse settings, AD-67).
/// A change must keep: every drag judged from the camera actually drawn, not from the chosen value alone.
/// <para>
/// PLANET-84 (AD-75, 2026-11-19): DJ — "After I look sharply up or down, beginning to walk should smoothly bring the camera
/// back to its usual walking perspective. Let an active mouse drag take control immediately. Preserve my heading and the
/// useful arrival framing." <see cref="ReturnToLevel"/> looks sharply down and up standing still, then walks, and judges
/// the return frame by frame (the usual walking view reached, no jump between frames, the heading kept); a drag held
/// while walking keeps the camera where the drag puts it until it is let go; in the overlook view the walk returns to
/// the overlook's own perspective. Standing still, a look still stays put (the checks above).
/// </para>
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>A drag's travel in the single-drag checks, window pixels (at 0.15°/px: 30° of chosen pitch).</summary>
    private const float LookDragPixels = 200f;

    /// <summary>The view must tilt at least this much for a <see cref="LookDragPixels"/> drag, degrees (about a third of what the boom turns: the camera keeps aiming at the traveller, so the view turns less than the boom).</summary>
    private const float LookMinResponseDegrees = 8f;

    /// <summary>After the drag the camera may drift back at most this much in <see cref="LookHoldSeconds"/>, degrees (levelling or a framing undoing the look would show here).</summary>
    private const float LookMaxDriftDegrees = 1.5f;

    /// <summary>How long the camera is given to settle after a drag, seconds (its follow rates close 63 % in ≤ 0.13 s; the overlook blend takes ≈ 0.6 s).</summary>
    private const double LookSettleSeconds = 1.0;

    /// <summary>How long the look is then watched for a drift back, seconds.</summary>
    private const double LookHoldSeconds = 2.0;

    /// <summary>The sweep's step, window pixels (9° of chosen pitch), and the least each step must tilt the view, degrees (no dead band).</summary>
    private const float LookSweepStepPixels = 60f, LookSweepMinStepDegrees = 1.0f;

    private async Task LookRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Until(() => P.CameraSettled, 8);
        await Seconds(3.0); // the opening shot glides down into the arrival framing
        var prefs = App.Preferences;
        Log($"saved look settings: reverse left/right {prefs.ReverseLookHorizontal}, reverse up/down {prefs.ReverseLookVertical}, level on release {prefs.LevelViewOnRelease} (file: {App.PreferencesPath ?? "none"})");
        var root = App.Navigator.Current;

        // ---- standing on arrival (the opening shot): the arrival framing holds while you stand still
        Check(P.ArrivalFraming > 0.99f, F($"standing after the opening shot: the arrival framing is on ({P.ArrivalFraming:0.00})"));
        var arrivalView = P.ViewPitchDegrees;
        Log(F($"arrival view before any drag: {arrivalView:+0.0;-0.0}° (chosen pitch {P.ChosenPitchDegrees:0.0}°, boom {P.BoomPitchDegrees:0.0}°)"));
        Check(MathF.Abs(P.BoomPitchDegrees - PlanetWorld.ArrivalPitch) < 0.5f, F($"the arrival perspective is unchanged until you look: boom {P.BoomPitchDegrees:0.0}° (arrival {PlanetWorld.ArrivalPitch:0}°)"));
        await Shot("look-arrival-before");
        await VerticalLook("standing on arrival", MouseButton.Right, LookDragPixels);
        await Shot("look-arrival-down");
        await VerticalLook("standing on arrival", MouseButton.Left, -2 * LookDragPixels);
        await Shot("look-arrival-up");
        Check(P.ArrivalFraming > 0.99f, F($"looking round keeps the arrival framing (distance and look-ahead): {P.ArrivalFraming:0.00}"));
        await WheelDistance("standing on arrival");

        // ---- after walking (the framing released)
        await Hold(1.2, Godot.Key.W);
        await Until(() => P.ArrivalFraming <= 0 && P.Walker.Velocity.Length() < 0.1f, 4);
        Check(P.ArrivalFraming <= 0, F($"after a short walk the arrival framing is released ({P.ArrivalFraming:0.00})"));
        await VerticalLook("after walking", MouseButton.Right, LookDragPixels);
        await VerticalLook("after walking", MouseButton.Left, -LookDragPixels);
        await Sweep("after walking");

        // ---- PLANET-84: walking brings a sharp look back to the usual perspective; a held drag wins while it is held
        await ReturnToLevel();

        // ---- the overlook view (B), then back
        await Key(Godot.Key.B);
        await Until(() => P.OverlookView01 >= 1f, 3);
        Check(P.OverlookView01 >= 1f, "B: the overlook view");
        await VerticalLook("in the overlook view", MouseButton.Right, LookDragPixels);
        await VerticalLook("in the overlook view", MouseButton.Left, -LookDragPixels);
        await WalkBack("in the overlook view", PlanetWorld.StartPitch);
        Check(P.OverlookView01 >= 1f && MathF.Abs(P.BoomPitchDegrees - PlanetWorld.OverlookPitch) < 1f,
            F($"in the overlook view: the walk keeps the overlook view ({P.OverlookView01:0.00}) at its own pitch, boom {P.BoomPitchDegrees:0.0}° (overlook {PlanetWorld.OverlookPitch:0}°)"));
        await Key(Godot.Key.B);
        await Until(() => P.OverlookView01 <= 0f, 3);

        // ---- just after a landing (tilted well down before the flight: the landing is framed as designed all the same)
        await DragBy(MouseButton.Right, new Vector2(0, LookDragPixels));
        await Seconds(LookSettleSeconds);
        var tiltedBefore = P.ChosenPitchDegrees;
        var target = Child(root, "Constellations");
        if (target < 0) target = Tree[root].Children.First(c => Tree[c].IsDirectory);
        App.Selection.Select(target); // [direct] the selection a click makes
        await Frames(2);
        await Fly(Godot.Key.G, target, Tree.DisplayName(target));
        await Until(() => P.CameraSettled, 4);
        await Seconds(1.0);
        Check(P.ArrivalFraming > 0.99f, F($"landed on {Tree.DisplayName(target)}: the arrival framing is on ({P.ArrivalFraming:0.00})"));
        Check(MathF.Abs(P.BoomPitchDegrees - PlanetWorld.ArrivalPitch) < 0.5f && MathF.Abs(tiltedBefore - PlanetWorld.StartPitch) > 20f,
            F($"the landing is framed at the arrival's pitch, {P.BoomPitchDegrees:0.0}°, though the pitch was {tiltedBefore:0.0}° before the flight (usual {PlanetWorld.StartPitch:0}°)"));
        await VerticalLook("just after a landing", MouseButton.Left, LookDragPixels);
        await Shot("look-landed-down");
        await VerticalLook("just after a landing", MouseButton.Right, -LookDragPixels);
        await WheelDistance("just after a landing");
        await Key(Godot.Key.Backspace);
        await Until(() => App.Navigator.Current == root && !P.Busy, 15);
        await WaitSettled();

        // ---- a Hazard World: the hazard view's pitch and its cap
        var eh = Child(root, "Event Horizon");
        if (Check(eh >= 0, "the Starter Sky has Event Horizon"))
        {
            App.Selection.Select(eh); // [direct]
            await Frames(2);
            await Fly(Godot.Key.G, eh, "Event Horizon");
            await Until(() => P.CameraSettled, 4);
            await Seconds(2.0); // the hazard arrival look turns to the opening
            await VerticalLook("on a Hazard World (arrival)", MouseButton.Right, LookDragPixels);
            await VerticalLook("on a Hazard World (arrival)", MouseButton.Left, -LookDragPixels);
            await Sweep("on a Hazard World");
            await Key(Godot.Key.Backspace);
            await Until(() => App.Navigator.Current == root && !P.Busy, 15);
            await WaitSettled();
        }

        // ---- the overview (T)
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 5);
        await Seconds(2.5);
        foreach (var (button, dy) in new[] { (MouseButton.Right, LookDragPixels / 2), (MouseButton.Left, -LookDragPixels) })
        {
            var uni0 = P.UniversePitchDegrees;
            var v0 = P.ViewPitchDegrees;
            await DragBy(button, new Vector2(0, dy));
            await Seconds(LookSettleSeconds);
            var uni1 = P.UniversePitchDegrees;
            var v1 = P.ViewPitchDegrees;
            await Seconds(LookHoldSeconds);
            var v2 = P.ViewPitchDegrees;
            var down = dy > 0;
            Check((down ? v0 - v1 : v1 - v0) >= LookMinResponseDegrees && MathF.Abs(v2 - v1) <= LookMaxDriftDegrees,
                F($"in the overview a {button}-drag {(down ? "down" : "up")} tilts the view {(down ? "down" : "up")}: {v0:+0.0;-0.0}° → {v1:+0.0;-0.0}° → {v2:+0.0;-0.0}° (orbit elevation {uni0:0.0}° → {uni1:0.0}°)"));
        }

        await Shot("look-overview");
        await Key(Godot.Key.Escape);
        await Until(() => !P.UniverseOpen && !P.Busy, 10);
    }

    /// <summary>A sharp look in <see cref="ReturnToLevel"/>, window pixels (at 0.15°/px: 60° of chosen pitch, to an end stop).</summary>
    private const float SharpLookPixels = 400f;

    /// <summary>The walk in <see cref="WalkBack"/> lasts this long, seconds (the return's time constant is 0.4 s).</summary>
    private const double ReturnWalkSeconds = 2.0;

    /// <summary>After <see cref="ReturnWalkSeconds"/> of walking the chosen pitch must be within this of the usual, degrees.</summary>
    private const float ReturnToleranceDegrees = 1.0f;

    /// <summary>The drawn view may move at most this much between two frames during the return, degrees (a glide, not a cut: at 60 fps the return's fastest step is ≈ 2°).</summary>
    private const float ReturnMaxFrameStepDegrees = 3.0f;

    /// <summary>Walking forward from where the camera looks, the heading may turn at most this much, degrees.</summary>
    private const float ReturnMaxHeadingDegrees = 3.0f;

    /// <summary>
    /// PLANET-84: standing still, a sharp look down (then, separately, up) stays put; then W: the chosen pitch glides back
    /// to the usual within <see cref="ReturnWalkSeconds"/>, the drawn view never jumps between frames, and the heading is
    /// kept. Then a drag held down while walking: nothing eases while the button is down; once let go, the walk brings the
    /// view back after <c>PitchReturnDelaySeconds</c>.
    /// </summary>
    private async Task ReturnToLevel()
    {
        // The usual walking view, measured while walking at the usual pitch.
        HoldKey(Godot.Key.W, true);
        await Seconds(1.5);
        var usualView = P.ViewPitchDegrees;
        HoldKey(Godot.Key.W, false);
        await Until(() => P.Walker.Velocity.Length() < 0.1f, 3);
        Log(F($"the usual walking view: {usualView:+0.0;-0.0}° (chosen pitch {P.ChosenPitchDegrees:0.0}°)"));

        foreach (var (what, dy) in new[] { ("down", SharpLookPixels), ("up", -SharpLookPixels) })
        {
            await DragBy(MouseButton.Right, new Vector2(0, dy));
            await Seconds(LookSettleSeconds);
            var tilted = P.ViewPitchDegrees;
            var chosen = P.ChosenPitchDegrees;
            await Seconds(LookHoldSeconds);
            Check(MathF.Abs(P.ViewPitchDegrees - tilted) <= LookMaxDriftDegrees && MathF.Abs(tilted - usualView) >= 15f,
                F($"standing, a sharp look {what} stays put: {usualView:+0.0;-0.0}° → {tilted:+0.0;-0.0}° → {P.ViewPitchDegrees:+0.0;-0.0}° {LookHoldSeconds:0} s later (chosen {chosen:0.0}°)"));
            await Shot($"look-sharp-{what}");
            await WalkBack($"after a sharp look {what}", PlanetWorld.StartPitch, usualView);
        }

        // A drag held while walking: the drag owns the camera until it is let go.
        HoldKey(Godot.Key.W, true);
        await Seconds(0.8);
        var start = ToWindow(App.GetViewport().GetVisibleRect().Size / 2 + new Vector2(0, 80));
        Mouse(MouseButton.Right, true, start);
        await Frames(2);
        var at = start;
        for (var i = 0; i < 10; i++)
        {
            var d = new Vector2(0, LookDragPixels / 10);
            at += d;
            Inject(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = d, ScreenRelative = d, ButtonMask = MouseButtonMask.Right });
            await Frames(1);
        }

        await Seconds(0.3);
        var held = P.ChosenPitchDegrees;
        var heldView = P.ViewPitchDegrees;
        await Seconds(1.5);
        var heldLater = P.ChosenPitchDegrees;
        Check(MathF.Abs(heldLater - held) < 0.1f && MathF.Abs(held - PlanetWorld.StartPitch) > 20f,
            F($"while walking, a drag held down keeps the camera where it put it: chosen {held:0.0}° → {heldLater:0.0}° 1.5 s later, button still down (view {heldView:+0.0;-0.0}°)"));
        Mouse(MouseButton.Right, false, start);
        await Seconds(ReturnWalkSeconds + 0.5);
        var released = P.ChosenPitchDegrees;
        HoldKey(Godot.Key.W, false);
        Check(MathF.Abs(released - PlanetWorld.StartPitch) <= ReturnToleranceDegrees,
            F($"once the drag is let go, the walk brings the pitch back: {heldLater:0.0}° → {released:0.0}° (usual {PlanetWorld.StartPitch:0}°)"));
        await Until(() => P.Walker.Velocity.Length() < 0.1f, 3);
    }

    /// <summary>
    /// Walks forward for <see cref="ReturnWalkSeconds"/> and judges the return to <paramref name="usualChosen"/> (the
    /// usual chosen pitch; in the overlook view its framing is added on top), frame by frame: no jump in the drawn view,
    /// the heading kept, and — with <paramref name="usualView"/> — the usual walking view reached.
    /// </summary>
    private async Task WalkBack(string where, float usualChosen, float? usualView = null)
    {
        var yaw0 = P.CameraHeadingDegrees();
        var chosen0 = P.ChosenPitchDegrees;
        var view0 = P.ViewPitchDegrees;
        var last = view0;
        var maxStep = 0f;
        var frames = 0;
        HoldKey(Godot.Key.W, true);
        var t0 = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - t0 < ReturnWalkSeconds * 1000)
        {
            await Frames(1);
            var v = P.ViewPitchDegrees;
            maxStep = MathF.Max(maxStep, MathF.Abs(v - last));
            last = v;
            frames++;
        }

        var chosen1 = P.ChosenPitchDegrees;
        var view1 = P.ViewPitchDegrees;
        var yaw1 = P.CameraHeadingDegrees();
        HoldKey(Godot.Key.W, false);
        var turned = MathF.Abs(Mathf.Wrap(yaw1 - yaw0, -180f, 180f));
        var reached = usualView is not { } uv || MathF.Abs(view1 - uv) <= 2f;
        Check(MathF.Abs(chosen1 - usualChosen) <= ReturnToleranceDegrees && reached && maxStep <= ReturnMaxFrameStepDegrees && turned <= ReturnMaxHeadingDegrees,
            F($"{where}: walking {ReturnWalkSeconds:0.0} s brings the camera back smoothly: chosen {chosen0:0.0}° → {chosen1:0.0}° (usual {usualChosen:0}°), view {view0:+0.0;-0.0}° → {view1:+0.0;-0.0}°{(usualView is { } u ? F($" (usual walking {u:+0.0;-0.0}°)") : "")}, largest step {maxStep:0.00}° a frame over {frames} frames, heading turned {turned:0.0}°"));
        await Until(() => P.Walker.Velocity.Length() < 0.1f, 3);
    }

    /// <summary>
    /// One vertical drag (positive <paramref name="dy"/> = down) and its judgement: the view tilts the same way (a drag
    /// down raises the camera, so the view looks further down; up lowers it, so the view rises), by at least
    /// <see cref="LookMinResponseDegrees"/> per <see cref="LookDragPixels"/>, and stays there.
    /// </summary>
    private async Task VerticalLook(string where, MouseButton button, float dy)
    {
        await Seconds(LookSettleSeconds);
        var before = P.ViewPitchDegrees;
        var chosen = P.ChosenPitchDegrees;
        await DragBy(button, new Vector2(0, dy));
        await Seconds(LookSettleSeconds);
        var after = P.ViewPitchDegrees;
        var chosenAfter = P.ChosenPitchDegrees;
        await Seconds(LookHoldSeconds);
        var later = P.ViewPitchDegrees;
        var down = dy > 0;
        var want = LookMinResponseDegrees * MathF.Abs(dy) / LookDragPixels;
        // A drag that runs into the chosen pitch's end stop cannot tilt further: judge by what the chosen pitch moved.
        var moved = MathF.Abs(chosenAfter - chosen) / (MathF.Abs(dy) * 0.15f);
        if (moved < 0.99f) want *= moved;
        var tilt = down ? before - after : after - before;
        Check(tilt >= want && MathF.Abs(later - after) <= LookMaxDriftDegrees,
            F($"{where}: a {button}-drag {(down ? "down" : "up")} of {MathF.Abs(dy):0} px tilts the view {(down ? "down" : "up")} {tilt:0.0}° (≥ {want:0.0}°): {before:+0.0;-0.0}° → {after:+0.0;-0.0}°, {later:+0.0;-0.0}° {LookHoldSeconds:0} s later (chosen pitch {chosen:0.0}° → {chosenAfter:0.0}°, boom {P.BoomPitchDegrees:0.0}°)"));
    }

    /// <summary>
    /// The whole range in small drags: up to the lowest pitch, then down step by step to the highest; every step that
    /// moves the chosen pitch must tilt the view by at least <see cref="LookSweepMinStepDegrees"/> (no band where the
    /// drag does nothing), and the view must cover a useful range.
    /// </summary>
    private async Task Sweep(string where)
    {
        await DragBy(MouseButton.Right, new Vector2(0, -1000)); // to the lowest pitch (the view's highest)
        await Seconds(LookSettleSeconds);
        var top = P.ViewPitchDegrees;
        var steps = new List<string>();
        var dead = 0;
        var last = top;
        for (var i = 0; i < 14; i++)
        {
            var chosen = P.ChosenPitchDegrees;
            await DragBy(MouseButton.Right, new Vector2(0, LookSweepStepPixels));
            await Seconds(0.7);
            var v = P.ViewPitchDegrees;
            var movedChosen = P.ChosenPitchDegrees - chosen;
            if (movedChosen < 0.5f) break; // at the end stop
            steps.Add(F($"{v:+0.0;-0.0}"));
            if (last - v < LookSweepMinStepDegrees * movedChosen / (LookSweepStepPixels * 0.15f)) dead++;
            last = v;
        }

        Check(dead == 0 && top - last >= 30f, F($"{where}: the sweep tilts the view at every step, {top:+0.0;-0.0}° … {last:+0.0;-0.0}° ({string.Join(", ", steps)}; {dead} dead steps)"));
        await DragBy(MouseButton.Right, new Vector2(0, -(P.ChosenPitchDegrees - PlanetWorld.StartPitch) / 0.15f)); // back to the usual pitch
        await Seconds(LookSettleSeconds);
    }

    /// <summary>Three wheel notches out, then three in: the camera's real distance from the traveller follows (the arrival framing must not hold it either).</summary>
    private async Task WheelDistance(string where)
    {
        float Distance() => P.Camera.GlobalPosition.DistanceTo(P.Avatar.GlobalPosition);
        var at = ToWindow(App.GetViewport().GetVisibleRect().Size / 2);
        await Seconds(LookSettleSeconds);
        var d0 = Distance();
        for (var i = 0; i < 3; i++) { Mouse(MouseButton.WheelDown, true, at); Mouse(MouseButton.WheelDown, false, at); await Frames(3); }
        await Seconds(LookSettleSeconds);
        var d1 = Distance();
        for (var i = 0; i < 3; i++) { Mouse(MouseButton.WheelUp, true, at); Mouse(MouseButton.WheelUp, false, at); await Frames(3); }
        await Seconds(LookSettleSeconds);
        var d2 = Distance();
        Check(d1 > d0 * 1.15f && MathF.Abs(d2 - d0) < d0 * 0.08f, F($"{where}: the wheel zooms the camera out and back: {d0:0.0} m → {d1:0.0} m → {d2:0.0} m"));
    }

    /// <summary>
    /// A drag by <paramref name="pixels"/> (window pixels) with the left or right button from a point in the world's
    /// view, as a mouse sends it: press, absolute-and-relative motion in 10 steps, release. A left press only becomes a
    /// look after <see cref="PlanetWorld.DragThresholdPixels"/>, so its positions are real cursor positions.
    /// </summary>
    private async Task DragBy(MouseButton button, Vector2 pixels, int steps = 10)
    {
        var start = ToWindow(App.GetViewport().GetVisibleRect().Size / 2 + new Vector2(0, 80));
        Mouse(button, true, start);
        await Frames(2);
        var at = start;
        var mask = (MouseButtonMask)(1 << ((int)button - 1));
        for (var i = 0; i < steps; i++)
        {
            var d = pixels / steps;
            at += d;
            Inject(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = d, ScreenRelative = d, ButtonMask = mask });
            await Frames(1);
        }

        Mouse(button, false, start);
        await Frames(2);
    }
}
