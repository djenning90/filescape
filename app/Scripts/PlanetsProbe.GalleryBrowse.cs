using FileScape.App.Planets;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// The gallery's browsing as one experience (PLANET-159..166, AD-94), part of <c>--automate=planets-gallery</c>: what
/// the Core rule (<see cref="GalleryBrowse"/>, <c>GalleryBrowseTests</c>) decides is not repeated here; these steps check
/// that the running app carries it out through injected input — a click on another easel moves the preview, a click on
/// empty ground puts it away and does nothing else, a double-click on an easel opens the large viewer and never Windows,
/// the viewer's right edge steps and its backdrop goes back to the world, Home / End reach the ends, Space does nothing,
/// Esc closes one level at a time, the prompt's sideways cue is there, Ctrl + → steps sideways without turning while
/// → alone turns, and the preview always shows the selection while you walk along the easels.
/// Instrument notes: the points clicked are found from the camera (<see cref="PlanetWorld.PictureOnScreen"/>, empty ground by
/// <see cref="PlanetWorld.Pick"/>); when none is on screen the step is logged as skipped (instrument), not failed.
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>How long the sidestep is held, seconds: longer than the chase camera's swing-in delay (PlanetWorld.SwingInDelaySeconds,
    /// 1.2 s), so the check sees whether a pure sidestep keeps the view (PLANET-165).</summary>
    private const double SidestepHoldSeconds = 1.8;

    private async Task GalleryBrowsing(int dawn)
    {
        var v = P.Viewer;
        P.Approach(dawn); // [direct] as a click in the list: hop beside it and look closer
        await Until(() => P.PreviewOpen && P.PreviewShownId == dawn, 6);
        await Seconds(0.8); // the camera settles behind the traveller
        Check(P.FocusNodeId != dawn || P.PromptDetail.Contains("Ctrl + ← →", StringComparison.Ordinal), $"PLANET-166: beside a picture the prompt teaches the sideways step (\"{P.PromptDetail}\")");
        Check(P.PreviewExpandRect.Size.X > 0, "the preview offers ⤢ View large");

        // ---- a click on another easel: the preview moves to it (it used to stay on the first while the other was selected).
        var shell = App.ShellLog.Count;
        if (P.PictureOnScreen(dawn) is { } other)
        {
            await ClickWorldAt(other.Centre);
            await Until(() => P.PreviewShownId == other.Id, 3);
            Log($"trace: {P.LastClickTrace}");
            Check(P.PreviewOpen && P.PreviewShownId == other.Id && App.Selection.Selected == other.Id && App.ShellLog.Count == shell,
                $"PLANET-160: a click on {Tree[other.Id].Name} moves the preview to it, selected, nothing opened (shown #{P.PreviewShownId})");
            await Shot("browse-click-another-easel");
        }
        else Log("[instrument] no other picture on screen beside the dawn: the click-another step is skipped");

        // ---- a click on empty ground: the preview goes away and the click does nothing else.
        if (EmptyGroundPoint() is { } ground)
        {
            var selected = App.Selection.Selected;
            var feet = P.Walker.Feet;
            await ClickWorldAt(ground);
            await Frames(3);
            Log($"trace: {P.LastClickTrace}");
            Check(!P.PreviewOpen && App.Selection.Selected == selected && Vector3Distance(feet, P.Walker.Feet) < 0.05f,
                $"PLANET-160: a click on empty ground puts the preview away and does nothing else (trace: {P.LastClickTrace})");
        }
        else Log("[instrument] no empty ground on screen: the outside-click step is skipped");

        // ---- a double-click on an easel: the large viewer, in FileScape — never Windows.
        if (P.PictureOnScreen() is { } easel)
        {
            shell = App.ShellLog.Count;
            await DoubleClickAt(easel.Centre);
            await Until(() => P.ViewerOpen, 3);
            Log($"trace: {P.LastClickTrace}");
            Check(P.ViewerOpen && v.Current == easel.Id && App.ShellLog.Count == shell && !App.ModalOpen,
                $"PLANET-161: a double-click on {Tree[easel.Id].Name} views it large in FileScape, Windows not asked (shell +{App.ShellLog.Count - shell})");
        }
        else
        {
            Log("[instrument] no picture on screen: the double-click step opens the viewer with R instead");
            P.Approach(dawn); // [direct]
            await Until(() => P.PreviewOpen, 6);
            await Key(Godot.Key.R);
        }

        await Until(() => v.State != "loading", 5);
        if (!P.ViewerOpen) return;

        // ---- the viewer's mouse: the right edge steps (its › brightens on hover), the backdrop goes back to the world.
        var size = App.GetViewport().GetVisibleRect().Size;
        var (index, count) = v.Place;
        var edge = new Vector2(size.X - 50, size.Y / 2);
        var at = ToWindow(edge);
        Inject(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(2);
        Check(count < 2 || v.HoverZone == ViewerZone.NextEdge, $"the pointer at the right edge is over the › ({v.HoverZone})");
        await Shot("browse-viewer-next-edge");
        if (count >= 2)
        {
            await ClickWorldAt(edge);
            await Frames(2);
            Check(v.Place.Index == index % count + 1 && App.Selection.Selected == v.Current, $"PLANET-162: a click on the right edge steps to the next picture ({index} → {v.Place.Index} of {count}), selected in the world");
            await Key(Godot.Key.End);
            Check(v.Place.Index == count, $"End: the last picture ({v.Place.Index} of {count})");
            await Key(Godot.Key.Home);
            Check(v.Place.Index == 1, $"Home: the first ({v.Place.Index} of {count})");
        }

        var before = v.Current;
        await Key(Godot.Key.Space);
        await Frames(2);
        Check(P.ViewerOpen && v.Current == before && P.Walker.Grounded, "PLANET-163: Space in the viewer does nothing (no step, no jump, still open)");

        var backdrop = v.BackdropPoint();
        var stood = P.Walker.Feet;
        var last = v.Current;
        if (backdrop is { } b)
        {
            await ClickWorldAt(b);
            await Frames(4);
            Check(!P.ViewerOpen && P.PreviewOpen && P.PreviewShownId == last && App.Selection.Selected == last && Vector3Distance(stood, P.Walker.Feet) < 0.05f,
                $"PLANET-162: a click outside the picture goes back to the world where you stood, the preview on {Tree[last].Name}");
            // The press after it is a new click, not the second half of a double-click on whatever is behind.
            Check(App.ShellLog.Count == shell, "and nothing behind the viewer was opened by that click");
        }
        else
        {
            Log("[instrument] the picture fills the stage: no backdrop to click; Esc instead");
            await Key(Godot.Key.Escape);
            await Frames(3);
        }

        // ---- Esc: one level at a time (the viewer is closed now: the preview, then walking).
        Check(P.PreviewOpen, "back on the preview");
        await Key(Godot.Key.Escape);
        await Frames(2);
        Check(!P.PreviewOpen && !P.ViewerOpen && App.KeyboardPanel == Main.KeyPanel.None, "Esc on the preview: walking again, the keys the game's");

        // ---- sideways for arrow-key players (PLANET-165): Ctrl + → steps right without turning; → alone turns.
        // With the dawn's preview open, so walking along the easels shows whether the preview follows (in a gallery) and
        // always shows the selection. The heading is compared with the direction of travel at the end of the hold (a
        // heading measured from north drifts on a small sphere as you cross it; the angle to the motion does not).
        P.Approach(dawn); // [direct]
        await Until(() => P.PreviewOpen && P.PreviewShownId == dawn && !P.Busy, 6);
        await Seconds(0.5);
        var right = P.ViewCamera.GlobalBasis.X;
        var from = P.Walker.Feet;
        Inject(new InputEventKey { Keycode = Godot.Key.Ctrl, PhysicalKeycode = Godot.Key.Ctrl, Pressed = true, CtrlPressed = true });
        Key1(Godot.Key.Right, true, ctrl: true);
        await Seconds(SidestepHoldSeconds - 0.2); // past the camera's 1.2 s swing-in delay: a pure sidestep must not turn into a swing
        var late = P.Walker.Feet;
        await Seconds(0.2);
        var motion = P.Walker.Feet - late;
        var camFwd = -P.ViewCamera.GlobalBasis.Z;
        Key1(Godot.Key.Right, false, ctrl: true);
        Inject(new InputEventKey { Keycode = Godot.Key.Ctrl, PhysicalKeycode = Godot.Key.Ctrl, Pressed = false });
        await Frames(3);
        var along = NVector3.Dot(P.Walker.Feet - from, new NVector3(right.X, right.Y, right.Z));
        var toMotion = motion.LengthSquared() > 1e-6f ? Mathf.RadToDeg(MathF.Acos(Math.Clamp(NVector3.Dot(NVector3.Normalize(motion), NVector3.Normalize(new NVector3(camFwd.X, camFwd.Y, camFwd.Z))), -1f, 1f))) : 0f;
        Check(along > 0.5f && toMotion > 60f, F($"PLANET-165: Ctrl + → steps sideways ({along:0.00} m to the right) and the view stays across the motion after {SidestepHoldSeconds:0.0} s ({toMotion:0}° between the camera and the motion; ≈ 90° is a sidestep, ≈ 0° a swing behind)"));
        Check(!P.PreviewOpen || P.PreviewShownId == App.Selection.Selected, $"walking along the easels the preview shows the selection (open {P.PreviewOpen}; followed to {(P.PreviewFollows >= 0 ? Tree[P.PreviewFollows].Name : "none")})");
        await Shot("browse-after-sidestep");
        var heading = P.CameraHeadingDegrees();
        from = P.Walker.Feet;
        await Hold(0.4, Godot.Key.Right);
        var turned = MathF.Abs(Mathf.Wrap(P.CameraHeadingDegrees() - heading, -180f, 180f));
        Check(turned > 10f && Vector3Distance(from, P.Walker.Feet) < 0.2f, F($"→ alone still turns ({turned:0} °, moved {Vector3Distance(from, P.Walker.Feet):0.00} m)"));
    }

    /// <summary>A point on screen over nothing pickable and no panel (the ground or sky beside the easels), or null.</summary>
    private Vector2? EmptyGroundPoint()
    {
        var size = App.GetViewport().GetVisibleRect().Size;
        for (var fy = 0.82f; fy > 0.3f; fy -= 0.08f)
        for (var fx = 0.5f; fx < 0.95f; fx += 0.07f)
        {
            var pt = new Vector2(size.X * fx, size.Y * fy);
            if (!P.OverHudAt(pt) && P.Pick(pt).Id < 0) return pt;
        }

        return null;
    }
}
