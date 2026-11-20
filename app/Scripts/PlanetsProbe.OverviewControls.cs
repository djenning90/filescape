using FileScape.Core.Planets;
using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-overview-controls --root=sample</c> (PLANET-110..112, AD-82; sixth pass, stream 2): the
/// overview's controls and transitions with injected input, on the Starter Sky.
/// <list type="bullet">
/// <item><b>Shift</b>: the same key pan held with and without Shift (the focus's travel per second, in camera distances),
/// a wheel notch out with and without Shift (the distance target's factor). The rates themselves are unit-tested
/// (<c>OverviewCameraTests</c>); this checks that the keys and the wheel reach them.</item>
/// <item><b>Esc's way back</b>: after zooming out and panning away, Esc; every frame until the walking camera has settled,
/// your world's distance in its radii, how far it is off the view's centre and the light's opacity. Checked: the overview
/// part's length, no frame that jumps (as rates, so a slow frame is not a jump: the distance changes by at most
/// <see cref="MaxZoomEFoldsPerSecond"/> e-folds a second, the off-centre angle by at most
/// <see cref="MaxOffCentreDegreesPerSecond"/>; a cut is one frame's change, an unbounded rate), and the hand-over continuous (the
/// distance within 2 %, off-centre within 1°), the light never more than ≈ 30 %, you where you stood. Then a second Esc
/// hurries it. Screenshots are taken in a separate pass so they do not disturb the trace.</item>
/// <item><b>Discoverable</b>: the hint names Shift and Esc when the overview opens (F1 lists every control).</item>
/// <item><b>The legacy round trip</b>: walk, turn the camera, V to Places and back, M to a map and V back: where you
/// stood, the heading, the chosen pitch and distance kept; no establishing shot.</item>
/// </list>
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>A key pan's travel with Shift over without must be within this of <see cref="OverviewControls.FastPanFactor"/>
    /// (frame times vary between the two holds).</summary>
    private const float ShiftRatioTolerance = 0.6f;

    /// <summary>The fastest the way back may change your world's distance, e-folds per second: the zoom-and-pan path at
    /// its fastest (a 500× zoom with a pan in 1.8 s measured 7.6/s at its peak) with a margin.</summary>
    private const float MaxZoomEFoldsPerSecond = 10f;

    /// <summary>The fastest your world may move off the view's centre, degrees per second (the glide down turns ≈ 70° in 1.5 s, peaking near 70°/s).</summary>
    private const float MaxOffCentreDegreesPerSecond = 240f;

    private async Task OverviewControlsRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 40);
        await WaitSettled();
        await Seconds(1.5);
        var root = App.Navigator.Current;
        var feet = P.Walker.Feet;

        // ---- Shift on the key pan and the wheel
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen && P.UniverseSettled, 8);
        Check(P.UniverseHintShown && P.UniverseHintText.Contains("Shift", StringComparison.Ordinal) && P.UniverseHintText.Contains("Esc", StringComparison.Ordinal),
            "the overview's hint names Shift and Esc (PLANET-113)");
        await Seconds(2.5); // the opening's pull-back settles (the zoom follows at 3.5/s)
        var plain = await PanRate(fast: false);
        var fast = await PanRate(fast: true);
        Log(F($"key pan: {plain:0.000} camera distances/s plain, {fast:0.000} with Shift (×{fast / MathF.Max(plain, 1e-6f):0.00})"));
        Check(plain > 0.3f && MathF.Abs(fast / plain - OverviewControls.FastPanFactor) < ShiftRatioTolerance,
            F($"Shift makes the key pan ×{OverviewControls.FastPanFactor:0} faster (measured ×{fast / MathF.Max(plain, 1e-6f):0.00})"));
        var empty = ToWindow(App.GetViewport().GetVisibleRect().Size * new Vector2(0.12f, 0.8f));
        var w0 = P.UniverseZoom.Target;
        Wheel(MouseButton.WheelDown, empty, shift: false);
        await Frames(3);
        var w1 = P.UniverseZoom.Target;
        Wheel(MouseButton.WheelDown, empty, shift: true);
        await Frames(3);
        var w2 = P.UniverseZoom.Target;
        Log(F($"wheel out: {w0:0.0} → {w1:0.0} m (×{w1 / w0:0.000}) → with Shift {w2:0.0} m (×{w2 / w1:0.000})"));
        Check(MathF.Abs(w1 / w0 - OverviewControls.WheelDistanceFactor(false, false)) < 1e-3f && MathF.Abs(w2 / w1 - OverviewControls.WheelDistanceFactor(false, true)) < 1e-3f,
            "a wheel notch out multiplies the distance by 1.18; with Shift it counts as two notches");

        // ---- Esc's way back, traced frame by frame (away from your world first, so it is a real journey)
        for (var k = 0; k < 3; k++) { Wheel(MouseButton.WheelDown, empty, shift: true); await Frames(2); }
        await Hold(0.5, Godot.Key.Shift, Godot.Key.D);
        await Seconds(1.5);
        var trace = await TraceReturn(hurryAfterSeconds: -1);
        var overviewFrames = trace.Where(x => x.Sample.Overview).ToList();
        var surfaceFrames = trace.Where(x => !x.Sample.Overview).ToList();
        if (Check(overviewFrames.Count > 5 && surfaceFrames.Count > 5, $"the way back traced: {overviewFrames.Count} overview frames, {surfaceFrames.Count} on the planet"))
        {
            var overviewSeconds = overviewFrames[^1].At - trace[0].At;
            Check(overviewSeconds >= OverviewReturn.MinSeconds - 0.05 && overviewSeconds <= OverviewReturn.MaxSeconds + 0.15,
                F($"the overview part lasts {overviewSeconds:0.00} s ({OverviewReturn.MinSeconds:0.0}–{OverviewReturn.MaxSeconds:0.0} s)"));
            var last = overviewFrames[^1].Sample;
            var first = surfaceFrames[0].Sample;
            var (ovR, surfR, _) = P.LastReturnHandOver;
            Log(F($"hand-over: {last.DistanceRadii:0.000} radii, {last.OffCentreDegrees:0.00}° off centre in the overview → {first.DistanceRadii:0.000} radii, {first.OffCentreDegrees:0.00}° on the planet (recorded {ovR:0.000} → {surfR:0.000})"));
            Check(MathF.Abs(first.DistanceRadii / last.DistanceRadii - 1f) < 0.02f && MathF.Abs(first.OffCentreDegrees - last.OffCentreDegrees) < 1f,
                F($"the hand-over keeps your world's size and place on screen ({last.DistanceRadii:0.00} → {first.DistanceRadii:0.00} radii, {last.OffCentreDegrees:0.0}° → {first.OffCentreDegrees:0.0}° off centre)"));
            var worstDistance = 0f;
            var worstAngle = 0f;
            var worstStep = 0f;
            for (var i = 1; i < trace.Count; i++)
            {
                var a = trace[i - 1].Sample;
                var b = trace[i].Sample;
                var dt = (float)(trace[i].At - trace[i - 1].At);
                if (a.DistanceRadii <= 0 || b.DistanceRadii <= 0 || dt <= 0) continue;
                var step = MathF.Abs(MathF.Log(b.DistanceRadii / a.DistanceRadii));
                worstStep = MathF.Max(worstStep, step);
                worstDistance = MathF.Max(worstDistance, step / dt);
                worstAngle = MathF.Max(worstAngle, MathF.Abs(b.OffCentreDegrees - a.OffCentreDegrees) / dt);
            }

            Check(worstDistance <= MaxZoomEFoldsPerSecond && worstAngle <= MaxOffCentreDegreesPerSecond,
                F($"no frame of the way back jumps: distance at most {worstDistance:0.0} e-folds/s (largest frame {(MathF.Exp(worstStep) - 1) * 100:0.0} %), off centre at most {worstAngle:0}°/s"));
            var flashMax = trace.Max(x => x.Sample.Flash);
            Check(flashMax <= 0.3f, F($"the light stays soft (at most {flashMax * 100:0} % opaque)"));
            var settled = trace[^1].At - surfaceFrames[0].At;
            Log(F($"on the planet the glide settled in {settled:0.00} s (blend {OverviewReturn.SurfaceBlendSeconds:0.0} s)"));
        }

        Check(!P.UniverseOpen && App.Navigator.Current == root && (P.Walker.Feet - feet).Length() < 0.05f, "back where you stood");

        // A second Esc hurries the way back.
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen && P.UniverseSettled, 8);
        await Seconds(1.5);
        for (var k = 0; k < 4; k++) { Wheel(MouseButton.WheelDown, empty, shift: true); await Frames(2); }
        await Seconds(1.0);
        var hurried = await TraceReturn(hurryAfterSeconds: 0.2);
        var hurriedOverview = hurried.Where(x => x.Sample.Overview).ToList();
        var took = hurriedOverview.Count > 0 ? hurriedOverview[^1].At - hurried[0].At : 0;
        Check(took > 0 && took < 0.2 + OverviewReturn.HurriedSeconds + 0.15, F($"a second Esc hurries the way back: the overview part took {took:0.00} s"));

        // Pictures of the way back for DJ (a separate pass: a screenshot stalls a frame).
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen && P.UniverseSettled, 8);
        await Seconds(1.5);
        for (var k = 0; k < 3; k++) { Wheel(MouseButton.WheelDown, empty, shift: true); await Frames(2); }
        await Seconds(1.0);
        await Shot("return-0-overview");
        await Key(Godot.Key.Escape);
        await Seconds(0.7);
        await Shot("return-1-flying-home");
        await Until(() => !P.UniverseOpen, 3);
        await Frames(2);
        await Shot("return-2-hand-over");
        await Seconds(0.5);
        await Shot("return-3-coming-down");
        await Until(() => P.CameraSettled, 4);
        await Shot("return-4-settled");

        // ---- the legacy round trip (PLANET-112)
        await LegacyRoundTrip(Godot.Key.V, "V to Places and V back");
        await LegacyRoundTrip(Godot.Key.M, "M to a map and V back");
    }

    /// <summary>Holds D (and Shift if <paramref name="fast"/>) for 0.6 s in the overview: the focus's travel per second, in camera distances.</summary>
    private async Task<float> PanRate(bool fast)
    {
        await Seconds(0.3);
        var (f0, _, _) = P.UniverseCameraAim;
        var d = P.UniverseZoom.Now;
        var t0 = P.NowSeconds;
        if (fast) await Hold(0.6, Godot.Key.Shift, Godot.Key.D);
        else await Hold(0.6, Godot.Key.D);
        var seconds = P.NowSeconds - t0;
        var (f1, _, _) = P.UniverseCameraAim;
        return f0.DistanceTo(f1) / d / (float)MathF.Max((float)seconds, 1e-3f);
    }

    /// <summary>A wheel notch at a window point, with or without Shift held (the event carries the modifier, as Windows sends it).</summary>
    private static void Wheel(MouseButton button, Vector2 at, bool shift)
    {
        Inject(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at, ShiftPressed = shift });
        Inject(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at, ShiftPressed = shift });
    }

    /// <summary>Esc, then one sample a frame until the overview has closed and the walking camera settled (≤ 8 s); a second
    /// Esc <paramref name="hurryAfterSeconds"/> after the first when that is ≥ 0.</summary>
    private async Task<List<(double At, (bool Overview, float DistanceRadii, float OffCentreDegrees, float Flash) Sample)>> TraceReturn(double hurryAfterSeconds)
    {
        var trace = new List<(double, (bool, float, float, float))>();
        var returns = P.UniverseReturns;
        Key1(Godot.Key.Escape, true);
        await Frames(1);
        Key1(Godot.Key.Escape, false);
        var t0 = P.NowSeconds;
        var hurried = false;
        while (P.NowSeconds - t0 < 8)
        {
            trace.Add((P.NowSeconds, P.ReturnTraceSample()));
            if (!hurried && hurryAfterSeconds >= 0 && P.NowSeconds - t0 >= hurryAfterSeconds)
            {
                hurried = true;
                Key1(Godot.Key.Escape, true);
                await Frames(1);
                Key1(Godot.Key.Escape, false);
                continue;
            }

            if (!P.UniverseOpen && P.CameraSettled) break;
            await Frames(1);
        }

        Check(P.UniverseReturns == returns + 1, "Esc began the way back down (one way back)");
        return trace;
    }

    /// <summary>Walks a little, turns the camera, leaves the planets with <paramref name="leave"/> and comes back with V.</summary>
    private async Task LegacyRoundTrip(Godot.Key leave, string how)
    {
        await Until(() => !P.Busy && !P.UniverseOpen && P.CameraSettled, 10);
        await Hold(0.8, Godot.Key.W);
        await Seconds(0.4);
        await DragCamera(new Vector2(140, 50));
        await Seconds(0.6);
        var feet = P.Walker.Feet;
        var facing = P.Walker.Facing;
        var heading = P.CameraHeadingDegrees();
        var pitch = P.ChosenPitchDegrees;
        var shots = P.EstablishingShots;
        var folder = App.Navigator.Current;
        await Key(leave);
        await Until(() => App.Experience != Experience.Planets, 5);
        await Seconds(1.0);
        var legacy = App.Experience;
        await Key(Godot.Key.V);
        await Until(() => App.Experience == Experience.Planets && P.Composition != null, 10);
        await Frames(5);
        var moved = (P.Walker.Feet - feet).Length();
        var turned = Mathf.RadToDeg(MathF.Acos(Math.Clamp(System.Numerics.Vector3.Dot(P.Walker.Facing, facing), -1f, 1f)));
        var headingNow = P.CameraHeadingDegrees();
        var dh = MathF.Abs(Mathf.Wrap(headingNow - heading, -180f, 180f));
        Log(F($"{how} ({legacy}): decision {P.LastResume}; moved {moved:0.000} m, facing {turned:0.0}°, camera heading {heading:0.0}° → {headingNow:0.0}°, chosen pitch {pitch:0.0}° → {P.ChosenPitchDegrees:0.0}°, establishing shots {shots} → {P.EstablishingShots}"));
        Check(P.LastResume == SurfaceResumeDecision.Resume && App.Navigator.Current == folder && moved < 0.05f && turned < 1f && dh < 1f && MathF.Abs(P.ChosenPitchDegrees - pitch) < 0.5f && P.EstablishingShots == shots,
            F($"{how}: back where you stood, facing and looking as before (moved {moved:0.00} m, heading {dh:0.0}° off)"));
    }
}
