using System.Globalization;
using System.Text;
using FileScape.Core.Scanning;
using FileScape.Core.Units;
using Godot;

namespace FileScape.App;

/// <summary>
/// Navigation verification through the application input path (--automate=navigation). Every control is driven
/// by synthetic InputEvents passed to <see cref="Input.ParseInputEvent"/>. They travel through the same pipeline
/// as real input: InputMap actions, GUI controls, then Main._Input / _UnhandledInput. The probe only reads state
/// to judge results. Exceptions, each labelled in the log: opening a confirmation dialog and starting the stale-layout
/// scan are direct method calls (set-up), and focus loss is a simulated Godot notification. Real OS-level Alt-Tab
/// and SendInput are covered by tools/os-input-probe.ps1.
/// <para>
/// What it proves, for the overview camera (CameraRig): ground movement is heading-relative and the same speed for
/// arrows, WASD, both together and diagonals; Shift is ×3; flight starts and ends without a jump, mouse-look stops at
/// ±85° without flipping or rolling; the wheel zooms in orbit and changes speed in flight; Escape releases flight
/// before it navigates; a focused text field or a modal dialog owns the keys; focus loss forgets held keys and drags;
/// and labels and commands keep working while the layout lags the tree during a scan.
/// </para>
/// <para>
/// Timing: speeds are measured on the wall clock over a held interval, so they are rates, not distances, and the
/// tolerances are ratios between two measurements taken the same way (frame pacing affects both alike). This probe
/// predates <see cref="ProbeBase"/> and keeps its own copies of the injection helpers.
/// </para>
/// </summary>
public partial class NavigationProbe : Node
{
    private readonly Main _main;
    private readonly string _out;
    private readonly StringBuilder _log = new();
    private int _failures;

    public NavigationProbe(Main main, string outDir)
    {
        _main = main;
        _out = outDir.Replace('\\', '/');
    }

    public override async void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute(_out);
        try
        {
            await Run();
        }
        catch (Exception ex)
        {
            Check(false, $"probe threw {ex}");
        }

        Log($"done failures={_failures}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(_out, "navigation.log"), _log.ToString());
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private CameraRig Rig => _main.Rig;

    /// <summary>
    /// Two speeds measured by the same method count as equal within 8 % (absorbs frame-pacing jitter over a 0.3…0.6 s
    /// hold; a doubled or √2-faster speed is far outside it).
    /// </summary>
    private const double SpeedRatioTolerance = 0.08;

    /// <summary>Two unit directions count as the same when their dot product is within 0.02 of 1 (≈ 11°).</summary>
    private const double DirectionDotTolerance = 0.02;

    /// <summary>
    /// The journey. Waits for the scan and its final layout (no timeout: the synthetic source always finishes), then
    /// runs the sections in order; each section leaves the camera in a known state for the next (reset, released).
    /// </summary>
    private async Task Run()
    {
        while (_main.Session is { IsFinished: false } || _main.Layout == null || _main.Layout.TreeVersion != _main.Tree!.Version)
        {
            await Frames(1);
        }

        _main.ResetView(snap: true);
        await Frames(10);
        Log($"start: {_main.StateJson()}");
        Check(_main.Hud.HintVisible, "first-run hint is visible: " + Ui.Hud.HintText);

        // ---------------------------------------------------- orbit-mode keyboard (heading-relative ground movement)
        var w = await GroundMove([Key.W], 0.5);
        var up = await GroundMove([Key.Up], 0.5);
        var both = await GroundMove([Key.W, Key.Up], 0.5);
        var diag = await GroundMove([Key.W, Key.D], 0.5);
        var right = await GroundMove([Key.Right], 0.5);
        var dKey = await GroundMove([Key.D], 0.5);
        var shift = await GroundMove([Key.Shift, Key.W], 0.5);
        var fwd = Forward();
        Log(F($"ground speeds u/s: W={w.Speed:0.00} Up={up.Speed:0.00} W+Up={both.Speed:0.00} W+D={diag.Speed:0.00} Right={right.Speed:0.00} D={dKey.Speed:0.00} Shift+W={shift.Speed:0.00}"));
        Check(w.Speed > 1, "W moves the view in orbit mode");
        Check(Near(up.Speed / w.Speed, 1, SpeedRatioTolerance), F($"Up arrow = W speed (ratio {up.Speed / w.Speed:0.000})"));
        Check(Near(both.Speed / w.Speed, 1, SpeedRatioTolerance), F($"W+Up together does not double speed (ratio {both.Speed / w.Speed:0.000})"));
        Check(Near(diag.Speed / w.Speed, 1, SpeedRatioTolerance), F($"diagonal W+D is not faster (ratio {diag.Speed / w.Speed:0.000})"));
        Check(Near(Dot(w.Dir, fwd), 1, DirectionDotTolerance), "W moves along the horizontal heading");
        Check(Near(Dot(diag.Dir, fwd), 0.7071f, 0.05), F($"W+D moves at 45° (dot {Dot(diag.Dir, fwd):0.000})"));
        Check(Near(Dot(right.Dir, dKey.Dir), 1, DirectionDotTolerance) && Near(right.Speed / dKey.Speed, 1, SpeedRatioTolerance), "Right arrow = D");
        // ×3 is CameraRig's fast multiplier; the wider absolute tolerance allows about the same 8 % on the larger ratio.
        Check(Near(shift.Speed / w.Speed, 3, 0.25), F($"Shift accelerates ×3 (ratio {shift.Speed / w.Speed:0.00})"));
        var eUp = await GroundMove([Key.E], 0.4);
        var qDown = await GroundMove([Key.Q], 0.2);
        Check(eUp.Delta.Y > 0.5 && Mathf.Abs(eUp.Delta.X) + Mathf.Abs(eUp.Delta.Z) < 0.05, F($"E raises the view (Δy {eUp.Delta.Y:0.00})"));
        Check(qDown.Delta.Y < -0.5, F($"Q lowers the view (Δy {qDown.Delta.Y:0.00})"));
        var wheelDist = Rig.Distance;
        await Wheel(true, 2);
        await Frames(60);
        Check(Rig.Distance < wheelDist * 0.9f, F($"wheel zooms in orbit mode ({wheelDist:0.0} → {Rig.Distance:0.0})"));

        // ---------------------------------------------------- enter flight: continuity
        _main.ResetView(snap: true);
        await Frames(30);
        var before = Rig.Camera.GlobalTransform;
        var centre = _main.GetViewport().GetVisibleRect().Size / 2;
        Mouse(MouseButton.Right, true, centre);
        await Frames(3);
        var after = Rig.Camera.GlobalTransform;
        Check(Rig.IsFlying && Input.MouseMode == Input.MouseModeEnum.Captured, "holding right mouse enters fly mode and captures the mouse");
        Check(_main.Hud.FlyChipVisible && _main.Hud.FlyChipText.Contains("speed", StringComparison.Ordinal), "fly status shows speed: " + _main.Hud.FlyChipText);
        Check(Same(before, after), F($"entering flight does not move or rotate the camera (Δpos {before.Origin.DistanceTo(after.Origin):0.0000})"));

        // ---------------------------------------------------- mouse look and pitch limits
        var yaw0 = Rig.Yaw;
        Motion(new Vector2(100, 0));
        await Frames(2);
        Check(Near(Rig.Yaw.Value, yaw0.Value - 100 * CameraRig.LookDegreesPerPixel, 0.01f), F($"mouse-look yaw: 100 px → {(Rig.Yaw - yaw0).Value:0.00}°"));
        Motion(new Vector2(0, -20000));
        await Frames(2);
        var upY = Rig.Camera.GlobalTransform.Basis.Y.Y;
        // ±85° is CameraMath's look-pitch limit; a camera up vector with y > 0.05 has not tipped over the vertical.
        Check(Near(Rig.LookPitch.Value, 85, 0.01f) && upY > 0.05f, F($"look up stops at {Rig.LookPitch.Value:0.0}° without flipping (camera up.y {upY:0.000})"));
        Motion(new Vector2(0, 40000));
        await Frames(2);
        Check(Near(Rig.LookPitch.Value, -85, 0.01f) && Rig.Camera.GlobalTransform.Basis.Y.Y > 0.05f, F($"look down stops at {Rig.LookPitch.Value:0.0}° without flipping"));
        Check(Mathf.Abs(Rig.Camera.GlobalTransform.Basis.X.Y) < 1e-4f, "no roll: camera right vector stays horizontal");
        // Climb well clear of the ground first: the speed series below flies downward at −30°, and reaching the
        // ground floor would turn the remaining motion into a horizontal slide and distort the speed ratios.
        Motion(new Vector2(0, (0 - Rig.LookPitch.Value) / -CameraRig.LookDegreesPerPixel));
        await Frames(2);
        await FlyMove([Key.Shift, Key.E], 2.5);
        Motion(new Vector2(0, (-30 - Rig.LookPitch.Value) / -CameraRig.LookDegreesPerPixel));
        await Frames(2);
        var altitude = Rig.Camera.GlobalPosition.Y;
        Log(F($"look pitch set to {Rig.LookPitch:0.0}°, altitude {altitude:0} u"));

        // ---------------------------------------------------- fly movement
        var fw = await FlyMove([Key.W], 0.6);
        var fup = await FlyMove([Key.Up], 0.6);
        var fboth = await FlyMove([Key.W, Key.Up], 0.6);
        var fdiag = await FlyMove([Key.W, Key.A], 0.6);
        var fleft = await FlyMove([Key.Left], 0.6);
        var fshift = await FlyMove([Key.Shift, Key.W], 0.6);
        var look = ToGd(FileScape.Core.Interaction.CameraMath.Forward(Rig.Yaw, Rig.LookPitch));
        Check(Rig.Camera.GlobalPosition.Y > CameraRig.MinCameraHeightWorldUnits + 1, F($"instrument: the speed series stayed above ground (altitude {Rig.Camera.GlobalPosition.Y:0} u)"));
        Log(F($"fly speeds u/s (speed setting {Rig.FlySpeedWorldUnitsPerSecond:0.0}): W={fw.Speed:0.00} Up={fup.Speed:0.00} W+Up={fboth.Speed:0.00} W+A={fdiag.Speed:0.00} Left={fleft.Speed:0.00} Shift+W={fshift.Speed:0.00}"));
        Check(Near(Dot(fw.Dir, look), 1, DirectionDotTolerance) && fw.Delta.Y < 0, F($"W flies along the view direction, descending when looking down (dot {Dot(fw.Dir, look):0.000})"));
        Check(Near(fup.Speed / fw.Speed, 1, SpeedRatioTolerance), F($"Up arrow = W in flight (ratio {fup.Speed / fw.Speed:0.000})"));
        Check(Near(fboth.Speed / fw.Speed, 1, SpeedRatioTolerance), F($"W+Up does not double flight speed (ratio {fboth.Speed / fw.Speed:0.000})"));
        Check(Near(fdiag.Speed / fw.Speed, 1, SpeedRatioTolerance), F($"diagonal flight is not faster (ratio {fdiag.Speed / fw.Speed:0.000})"));
        Check(Mathf.Abs(fleft.Delta.Y) < 0.01f, "strafe stays level");
        Check(Near(fshift.Speed / fw.Speed, 3, 0.3), F($"Shift ×3 in flight (ratio {fshift.Speed / fw.Speed:0.00})"));
        var fe = await FlyMove([Key.E], 0.4);
        var fq = await FlyMove([Key.Q], 0.2);
        Check(fe.Delta.Y > 0 && Mathf.Abs(fe.Delta.X) + Mathf.Abs(fe.Delta.Z) < 0.01f * fe.Delta.Length() + 1e-3f, "E ascends in world space while flying");
        Check(fq.Delta.Y < 0, "Q descends while flying");
        var s0 = Rig.FlySpeedWorldUnitsPerSecond;
        await Wheel(true, 3);
        // Three notches of CameraMath.WheelSpeedFactorPerNotch (×1.25 each): 1.25³ = 1.953.
        Check(Near(Rig.FlySpeedWorldUnitsPerSecond / s0, 1.953f, 0.02f), F($"wheel up ×3 raises flight speed {s0:0.0} → {Rig.FlySpeedWorldUnitsPerSecond:0.0} u/s"));
        Check(_main.Hud.FlyChipText.Contains(Rig.FlySpeedWorldUnitsPerSecond.ToString("0.#", CultureInfo.CurrentCulture), StringComparison.Ordinal), "flight speed shown while it changes");
        await Wheel(false, 3);
        Check(Near(Rig.FlySpeedWorldUnitsPerSecond, s0, 0.01f * s0), "wheel down restores the speed");
        var dist = Rig.Distance;
        Check(Near(Rig.Distance, dist, 1e-3f), "wheel in flight does not zoom");

        // ---------------------------------------------------- release: continuity, no snap back
        var preRelease = Rig.Camera.GlobalTransform;
        Mouse(MouseButton.Right, false, centre);
        await Frames(3);
        var postRelease = Rig.Camera.GlobalTransform;
        Check(!Rig.IsFlying && Input.MouseMode == Input.MouseModeEnum.Visible && !_main.Hud.FlyChipVisible, "releasing right mouse ends flight and restores the cursor");
        Check(Same(preRelease, postRelease), F($"leaving flight does not move the camera (Δpos {preRelease.Origin.DistanceTo(postRelease.Origin):0.0000})"));
        await Frames(90);
        Check(Same(postRelease, Rig.Camera.GlobalTransform), "no snap back to the old orbit target afterwards");
        var pivot = Rig.Target;
        Mouse(MouseButton.Left, true, centre);
        Motion(new Vector2(60, 0), MouseButtonMask.Left, centre + new Vector2(60, 0));
        await Frames(2);
        Mouse(MouseButton.Left, false, centre + new Vector2(60, 0));
        await Frames(60);
        Check(Rig.Target.DistanceTo(pivot) < 1e-3f && !Same(postRelease, Rig.Camera.GlobalTransform), "left-drag orbits around the post-flight pivot");

        // ---------------------------------------------------- Escape: release first, then parent
        var tree = _main.Tree!;
        var file = tree.Nodes.First(n => n.Name == "hundred-mib.iso");
        _main.Select(file.Id, fly: true);
        await Settle();
        Mouse(MouseButton.Right, true, centre);
        await Frames(2);
        Key1(Key.Escape, true); await Frames(2); Key1(Key.Escape, false); await Frames(2);
        Check(!Rig.IsFlying && Input.MouseMode == Input.MouseModeEnum.Visible, "Escape ends flight and releases capture");
        Check(_main.Selection.Selected == file.Id, "that Escape did not also navigate to the parent");
        Mouse(MouseButton.Right, false, centre);
        await Frames(2);
        Key1(Key.Escape, true); await Frames(2); Key1(Key.Escape, false); await Frames(2);
        Check(_main.Selection.Selected == file.ParentId, "the next Escape navigates to the parent directory");

        // ---------------------------------------------------- selection and visualization switching after flight
        _main.Select(file.Id, fly: true);
        await Settle();
        Mouse(MouseButton.Right, true, centre);
        Motion(new Vector2(150, 40));
        var flown = await FlyMove([Key.W], 0.4);
        Mouse(MouseButton.Right, false, centre);
        await Frames(2);
        Key1(Key.Tab, true); await Frames(2); Key1(Key.Tab, false);
        await Settle();
        Check(_main.VisualizationIndex == 1 && _main.Selection.Selected == file.Id, F($"after flying {flown.Delta.Length():0.0} u, Tab switches to District and keeps the selection"));
        Key1(Key.F, true); await Frames(2); Key1(Key.F, false);
        await Settle();
        var box = _main.Layout!.NodeBoxes[file.Id];
        var toSel = Visual.Visualization.Gd(box.Center) - Rig.Camera.GlobalPosition;
        Check(Rig.Camera.GlobalTransform.Basis.Z.Dot(-toSel.Normalized()) > 0.9f, "F frames the selection again");

        // ---------------------------------------------------- toolbar click, then keyboard
        var btn = _main.Hud.VisualizationButtonRect(0);
        Mouse(MouseButton.Left, true, btn.GetCenter());
        await Frames(2);
        Mouse(MouseButton.Left, false, btn.GetCenter());
        await Settle();
        Check(_main.VisualizationIndex == 0, "clicking the Classic FSN toolbar button switches visualization");
        var afterToolbar = await GroundMove([Key.Up], 0.3);
        Check(afterToolbar.Speed > 1, "arrow keys still move the view right after a toolbar click");

        // ---------------------------------------------------- a focused UI control owns the arrow keys
        var edit = new LineEdit { Position = new Vector2(20, 300), Size = new Vector2(200, 30) };
        _main.Hud.UiRoot.AddChild(edit);
        await Frames(2);
        edit.GrabFocus();
        await Frames(2);
        var typed = await GroundMove([Key.Up, Key.W], 0.3);
        Check(typed.Delta.Length() < 1e-3f, "while a text field has focus, W and Up do not move the camera");
        Check(edit.Text.Contains('w', StringComparison.OrdinalIgnoreCase), F($"the text field received the typed key instead (text '{edit.Text}')"));
        Mouse(MouseButton.Left, true, centre);
        await Frames(1);
        Mouse(MouseButton.Left, false, centre);
        await Frames(3);
        var refocused = await GroundMove([Key.W], 0.3);
        Check(_main.GetViewport().GuiGetFocusOwner() == null && refocused.Speed > 1, "clicking the scene returns the keys to navigation");
        edit.QueueFree();

        // ---------------------------------------------------- a modal dialog pauses navigation (dialog opened directly: set-up)
        _main.Confirm("Navigation probe: this dialog must block scene movement.", () => { });
        await Frames(5);
        var blocked = await GroundMove([Key.Up], 0.3);
        var blockedW = await GroundMove([Key.W], 0.3);
        Check(blocked.Delta.Length() < 1e-3f && blockedW.Delta.Length() < 1e-3f, "with a dialog open, arrows and W do not move the camera");
        var selBefore = _main.Selection.Selected;
        Key1(Key.Escape, true); await Frames(2); Key1(Key.Escape, false); await Frames(4);
        Check(!_main.ModalOpen && _main.Selection.Selected == selBefore, "Escape closes the dialog without navigating to the parent");
        var afterDialog = await GroundMove([Key.W], 0.3);
        Check(afterDialog.Speed > 1, "movement works again once the dialog is closed");

        // ---------------------------------------------------- focus loss while flying with W held (simulated notification)
        Mouse(MouseButton.Right, true, centre);
        await Frames(2);
        Key1(Key.W, true);
        await Frames(10);
        GetTree().Root.PropagateNotification((int)NotificationApplicationFocusOut);
        await Frames(3);
        Check(!Rig.IsFlying && Input.MouseMode == Input.MouseModeEnum.Visible && !_main.HeldActionNames.Any(), "focus loss ends flight, releases capture, and forgets held keys");
        var p0 = Rig.Camera.GlobalPosition;
        GetTree().Root.PropagateNotification((int)NotificationApplicationFocusIn);
        await Frames(30);
        Check(Rig.Camera.GlobalPosition.DistanceTo(p0) < 1e-3f, "nothing moves after focus returns until a key is pressed again");
        Key1(Key.W, false);
        Mouse(MouseButton.Right, false, centre);
        await Frames(2);
        Mouse(MouseButton.Left, true, centre);
        Motion(new Vector2(30, 0), MouseButtonMask.Left, centre + new Vector2(30, 0));
        await Frames(3); // ParseInputEvent buffers until the next frame; notifications are immediate
        Check(_main.IsDragging, "instrument: the drag is active before focus is lost");
        GetTree().Root.PropagateNotification((int)NotificationApplicationFocusOut);
        GetTree().Root.PropagateNotification((int)NotificationApplicationFocusIn);
        await Settle(); // let the orbit easing from the short drag finish before taking the reference pose
        var t0 = Rig.Camera.GlobalTransform;
        Motion(new Vector2(200, 0), 0, centre + new Vector2(230, 0));
        await Frames(30);
        var t1 = Rig.Camera.GlobalTransform;
        // A stuck drag would orbit 200 px × 0.28°/px ≈ 56°. The 0.01 basis tolerance (≈0.6°) only absorbs the
        // tail of orbit easing that IsSettled still allows.
        Check(!_main.IsDragging && t0.Basis.Z.DistanceTo(t1.Basis.Z) < 0.01f, F($"a drag interrupted by focus loss does not keep orbiting with no button held (dragging={_main.IsDragging}, Δpos {t0.Origin.DistanceTo(t1.Origin):0.0000}, Δyaw-basis {t0.Basis.Z.DistanceTo(t1.Basis.Z):0.00000}, settled={Rig.IsSettled})"));
        Mouse(MouseButton.Left, false, centre);
        await Frames(2);

        // ---------------------------------------------------- Home recovery after flying away
        Mouse(MouseButton.Right, true, centre);
        Motion(new Vector2(700, 300));
        await FlyMove([Key.Shift, Key.W], 1.0);
        Mouse(MouseButton.Right, false, centre);
        await Frames(2);
        Key1(Key.Home, true); await Frames(2); Key1(Key.Home, false);
        await Settle();
        var root = _main.Layout!.NodeBoxes[0];
        var rootScreen = Rig.Camera.UnprojectPosition(new Vector3(root.Center.X, root.Max.Y, root.Center.Z));
        Check(!Rig.Camera.IsPositionBehind(new Vector3(root.Center.X, root.Max.Y, root.Center.Z)) && _main.GetViewport().GetVisibleRect().HasPoint(rootScreen), "Home recovers the start view after flying away");

        // ---------------------------------------------------- stale layout during a scan (review finding 1)
        _main.StartScan(new SyntheticTreeSource { TargetNodes = 60_000, NodesPerSecond = 15_000 }); // set-up: direct call
        // At 15 000 nodes/s the 60 000-node scan takes about 4 s, with relayouts trailing it; 20 s is the cap. The
        // commands are injected every 20th stale frame, and more than 20 stale frames prove the case was exercised.
        var intervalFrames = 0;
        var labelFramesDuringInterval = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (_main.Session is { IsFinished: false } && sw.Elapsed.TotalSeconds < 20)
        {
            if (_main.Layout != null && _main.Tree!.Count > _main.Layout.Count)
            {
                intervalFrames++;
                if (intervalFrames % 20 == 1)
                {
                    // Commands that consume layout geometry, injected while the tree is ahead of the layout.
                    _main.Select(_main.Tree.Count - 1);
                    Key1(Key.F, true); Key1(Key.F, false);
                    Key1(Key.Home, true); Key1(Key.Home, false);
                    Key1(Key.B, true); Key1(Key.B, false);
                    Mouse(MouseButton.Left, true, centre); Mouse(MouseButton.Left, false, centre);
                }

                if (_main.LabelsShown > 0) labelFramesDuringInterval++;
            }

            await Frames(1);
        }

        Log($"stale-layout interval: {intervalFrames} frames with tree ahead of layout; labels drawn in {labelFramesDuringInterval} of them");
        Check(intervalFrames > 20, "the scan really exercised the stale-layout interval");
        Check(labelFramesDuringInterval > 0, "labels kept rendering during the interval");
        Log($"end: {_main.StateJson()}");
    }

    // ------------------------------------------------------------ input injection

    private static void Inject(InputEvent e) => Input.ParseInputEvent(e);

    /// <summary>One key event (logical and physical code; Shift also sets its modifier flag; letters carry their character).</summary>
    private static void Key1(Key key, bool pressed) =>
        Inject(new InputEventKey
        {
            Keycode = key,
            PhysicalKeycode = key,
            Pressed = pressed,
            ShiftPressed = key == Key.Shift && pressed,
            Unicode = pressed && key is >= Key.A and <= Key.Z ? char.ToLowerInvariant((char)key) : 0,
        });

    private void Mouse(MouseButton button, bool pressed, Vector2 position)
    {
        var mask = pressed ? (MouseButtonMask)(1 << ((int)button - 1)) : 0;
        Inject(new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, Position = position, GlobalPosition = position, ButtonMask = mask });
    }

    private static void Motion(Vector2 relative, MouseButtonMask mask = 0, Vector2? position = null)
    {
        var pos = position ?? Vector2.Zero;
        Inject(new InputEventMouseMotion { Relative = relative, ScreenRelative = relative, Position = pos, GlobalPosition = pos, ButtonMask = mask });
    }

    /// <summary>Wheel notches at the view centre, two frames apart (each notch is a press and a release, as Godot reports a wheel).</summary>
    private async Task Wheel(bool up, int notches)
    {
        var at = _main.GetViewport().GetVisibleRect().Size / 2;
        for (var i = 0; i < notches; i++)
        {
            Mouse(up ? MouseButton.WheelUp : MouseButton.WheelDown, true, at);
            Mouse(up ? MouseButton.WheelUp : MouseButton.WheelDown, false, at);
            await Frames(2);
        }
    }

    /// <summary>A measured movement: displacement (world units) over a wall-clock interval (seconds).</summary>
    private readonly record struct Move(Vector3 Delta, double Seconds)
    {
        public float Speed => Seconds > 0 ? Delta.Length() / (float)Seconds : 0;
        public Vector3 Dir => Delta.LengthSquared() > 0 ? Delta.Normalized() : Vector3.Zero;
    }

    /// <summary>Holds keys for a duration and measures how far the orbit target moved (orbit mode has no velocity ramp).</summary>
    private async Task<Move> GroundMove(Key[] keys, double seconds)
    {
        await Frames(2);
        var start = Rig.Target;
        foreach (var k in keys) Key1(k, true);
        await Frames(1);
        var t0 = Time.GetTicksUsec();
        var p0 = Rig.Target;
        while ((Time.GetTicksUsec() - t0) / 1e6 < seconds) await Frames(1);
        var p1 = Rig.Target;
        var t1 = Time.GetTicksUsec();
        foreach (var k in keys) Key1(k, false);
        await Frames(3);
        _ = start;
        return new Move(p1 - p0, (t1 - t0) / 1e6);
    }

    /// <summary>
    /// Holds keys in flight, measuring the steady-state part (after the ~70 ms velocity ramp: 0.35 s of lead-in leaves
    /// a wide margin), then waits 0.4 s after release for the coast-down to end before the next measurement.
    /// </summary>
    private async Task<Move> FlyMove(Key[] keys, double seconds)
    {
        foreach (var k in keys) Key1(k, true);
        var ramp = Time.GetTicksUsec();
        while ((Time.GetTicksUsec() - ramp) / 1e6 < 0.35) await Frames(1);
        var t0 = Time.GetTicksUsec();
        var p0 = Rig.Camera.GlobalPosition;
        while ((Time.GetTicksUsec() - t0) / 1e6 < seconds) await Frames(1);
        var p1 = Rig.Camera.GlobalPosition;
        var t1 = Time.GetTicksUsec();
        foreach (var k in keys) Key1(k, false);
        var stop = Time.GetTicksUsec();
        while ((Time.GetTicksUsec() - stop) / 1e6 < 0.4) await Frames(1);
        return new Move(p1 - p0, (t1 - t0) / 1e6);
    }

    // ------------------------------------------------------------ helpers

    private Vector3 Forward() => ToGd(FileScape.Core.Interaction.CameraMath.HorizontalForward(Rig.Yaw));

    private static Vector3 ToGd(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static float Dot(Vector3 a, Vector3 b) => a.Dot(b);

    private static bool Near(float a, float b, double tol) => Mathf.Abs(a - b) <= (float)tol;

    /// <summary>
    /// The same camera pose, for the "no jump" checks: positions within 1e-3 world units (or 1e-5 of the distance from
    /// the origin when that is larger, for float precision far out) and basis vectors within 1e-4.
    /// </summary>
    private static bool Same(Transform3D a, Transform3D b) =>
        a.Origin.DistanceTo(b.Origin) < 1e-3f * Mathf.Max(1, a.Origin.Length() * 0.01f) &&
        a.Basis.X.DistanceTo(b.Basis.X) < 1e-4f && a.Basis.Y.DistanceTo(b.Basis.Y) < 1e-4f && a.Basis.Z.DistanceTo(b.Basis.Z) < 1e-4f;

    /// <summary>Waits for the rig to settle (at most 300 frames, so a rig that never settles fails a check rather than hangs), then 5 more.</summary>
    private async Task Settle()
    {
        for (var i = 0; i < 300 && !Rig.IsSettled; i++) await Frames(1);
        await Frames(5);
    }

    private async Task Frames(int n)
    {
        for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    private void Check(bool ok, string what)
    {
        if (!ok) _failures++;
        Log($"{(ok ? "PASS" : "FAIL")} {what}");
    }

    private void Log(string line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss.fff}] {line}";
        GD.Print("[nav] " + stamped);
        _log.AppendLine(stamped);
    }
}
