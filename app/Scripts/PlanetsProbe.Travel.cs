using System.Globalization;
using System.Text;
using FileScape.App.Planets;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App;

/// <summary>
/// Travel's departures, landings and the overview's Enter (PLANET-81..83, AD-74), with injected input.
/// <list type="bullet">
/// <item><c>--automate=planets-travel --root=sample</c>: every entry point that starts a flight — Space on a pad, G, the
/// Destinations list (a world here and a trip), U, Backspace, Shift+Enter straight there, Go to path, a belt moon when
/// the world has one, and the overview's Enter — each timed from the command to the take-off (the added departure time
/// is what this measures), and every landing traced frame by frame (the traveller's lean, body axis as the camera sees
/// it, facing, squash, head and foot height) through its last second in the air and its first on the ground, written to
/// <c>landing-trace.csv</c> with a summary per landing in the log.</item>
/// <item><c>--automate=planets-overview-enter --root=&lt;a root with a child named --test-target (default "home")&gt;</c>:
/// DJ's route (2026-11-19) repeated in its variants — the list then the overview, the overview then the list, the list's
/// Enter, the overview's Enter, a click and a double-click — each judged by whether the accepted Enter completed (the
/// navigator there, the overview closed, on foot) or said why within the wait. With <c>--counts-only</c> (a real root)
/// nothing but the target's and the root's names is logged and no screenshot is taken.</item>
/// </list>
/// </summary>
public partial class PlanetsProbe
{
    // ------------------------------------------------------------------ the per-frame travel trace

    /// <summary>One drawn frame of the traveller (camera-space vectors are invariant under the landing's re-expression).</summary>
    private readonly record struct TravelFrame(double At, string Phase, float U, float Lean, float Tilt, Vector3 BodyCam, Vector3 FacingCam, float Squash, float Fly, float Head, float Foot, int Landings);

    private readonly List<TravelFrame> _travelFrames = [];
    private bool _traceTravel;

    /// <summary>Called from <see cref="_Process"/>: records the traveller's drawn pose every frame while tracing.</summary>
    private void TrackTravelFrame()
    {
        if (!_traceTravel || !P.Active) return;
        var a = P.Avatar;
        var cam = P.Camera.GlobalBasis.Inverse();
        var up = a.RigUp;
        var body = a.BodyAxis;
        // Tilt: the body's axis from the rig's up, degrees, signed + towards the facing (forward), − backwards.
        var tilt = Mathf.RadToDeg(body.AngleTo(up)) * (body.Dot(a.RigFacing) >= 0 ? 1 : -1);
        _travelFrames.Add(new TravelFrame(P.NowSeconds, P.PhaseName, P.FlightProgress, a.LeanDegrees, tilt, (cam * body).Normalized(), (cam * a.RigFacing).Normalized(),
            a.SquashAmount, a.FlightPose01, a.HeadHeightMetres, a.LowestFootMetres, a.LandingsAnimated));
    }

    /// <summary>A command's departure as measured: from the command to the take-off (flight or warp) and to the landing, seconds.</summary>
    private readonly List<(string Entry, double Departure, double ToLanding, string Marks)> _departures = [];

    /// <summary>
    /// Runs one travel command and waits for its landing; logs and keeps the departure time: from just before the
    /// command to the first take-off mark ("flight" or "warp") after it (a trip: its first hop).
    /// </summary>
    private async Task<bool> Timed(string entry, Func<Task> command, int expected, double timeout = 12)
    {
        var t0 = P.NowSeconds;
        var from = P.LaunchLog.Count;
        await command();
        await Until(() => P.LaunchLog.Skip(from).Any(l => l.Phase is "flight" or "warp"), timeout);
        await Until(() => !P.Busy && !P.UniverseOpen && App.Navigator.Current == expected && P.Composition?.FolderId == expected, timeout);
        await Frames(3);
        var log = P.LaunchLog.Skip(from).ToList();
        var off = log.FirstOrDefault(l => l.Phase is "flight" or "warp");
        var land = log.LastOrDefault(l => l.Phase == "land");
        var marks = string.Join(" ", log.Select(l => F($"{l.Phase}@{l.AtSeconds - t0:0.00}")));
        var ok = off.Phase != null && App.Navigator.Current == expected && P.Walker.Grounded;
        var dep = off.Phase != null ? off.AtSeconds - t0 : double.NaN;
        var toLand = land.Phase != null ? land.AtSeconds - t0 : double.NaN;
        _departures.Add((entry, dep, toLand, marks));
        Check(ok, F($"{entry}: took off {dep:0.00} s after the command, landed at {toLand:0.00} s ({marks})"));
        return ok;
    }

    /// <summary>The Starter Sky's travel tour (see the class summary).</summary>
    private async Task TravelRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.0);
        _traceTravel = true;
        var root = App.Navigator.Current;
        var comp = P.Composition!;
        var pads = comp.Launchers.Where(l => !l.IsParent && l.CanLaunch && P.HasWorldInSky(l.TargetId)).ToList();
        Check(pads.Count >= 3, $"the Starter Sky has worlds with pads ({pads.Count})");
        var near = pads.OrderBy(l => P.AngleFromWalker(l.Direction)).First();
        var far = pads.OrderByDescending(l => P.AngleFromWalker(l.Direction)).First(l => l.TargetId != near.TargetId);
        var other = pads.First(l => l.TargetId != near.TargetId && l.TargetId != far.TargetId);
        Log(F($"pads: near {Tree[near.TargetId].Name} ({P.AngleFromWalker(near.Direction) * comp.Shape.Radius:0.0} m), far {Tree[far.TargetId].Name} ({P.AngleFromWalker(far.Direction) * comp.Shape.Radius:0.0} m), other {Tree[other.TargetId].Name}"));

        // 1. Space on a pad (already standing on it).
        P.ProbeStandOn(near.Direction, near.PadTop); // [direct] stand on the pad's centre
        await Seconds(0.6);
        await Timed("pad Space (on the pad)", () => Key(Godot.Key.Space), near.TargetId);

        // 2. U: up to the parent, from the arrival meadow of the child.
        await Seconds(0.8);
        await Timed("U (parent)", () => Key(Godot.Key.U), root);

        // 3. Backspace: back to where you were (the child).
        await Seconds(0.8);
        await Timed("Backspace (history)", () => Key(Godot.Key.Backspace), near.TargetId);
        await Seconds(0.8);
        await Timed("U (parent, again)", () => Key(Godot.Key.U), root);

        // 4. G with a far world selected, from where you stand.
        await Seconds(0.8);
        App.Select(far.TargetId); // [direct] the selection (a click on its world does the same)
        await Frames(2);
        Log(F($"G from {P.AngleFromWalker(far.Direction) * comp.Shape.Radius:0.0} m along the ground to the far pad"));
        await Timed("G (selected world, far pad)", () => Key(Godot.Key.G), far.TargetId);
        await Seconds(0.8);
        await Timed("U (parent, from the far world)", () => Key(Godot.Key.U), root);

        // 5. The Destinations list: a world here, by name, Enter.
        await Seconds(0.8);
        await Timed("Destinations: a world here (J, type, Enter)", async () =>
        {
            await Key(Godot.Key.J);
            await Frames(3);
            await TypeText(Tree[other.TargetId].Name);
            await Frames(3);
            await Key(Godot.Key.Enter);
        }, other.TargetId);
        await Seconds(0.8);
        await Timed("U (parent, from the list's world)", () => Key(Godot.Key.U), root);

        // 6. A trip three levels down (Long Way Down › Meadow Gate › Fern Trail): its first hop's departure.
        var lwd = Child(root, "Long Way Down");
        var meadow = lwd >= 0 ? Child(lwd, "Meadow Gate") : -1;
        var fern = meadow >= 0 ? Child(meadow, "Fern Trail") : -1;
        if (fern >= 0)
        {
            await Seconds(0.8);
            var t0 = P.NowSeconds;
            var from = P.LaunchLog.Count;
            await Key(Godot.Key.J);
            await Frames(3);
            await TypeText("Fern Trail");
            await Frames(3);
            await Key(Godot.Key.Enter);
            await Until(() => P.TripActive, 3);
            await Until(() => !P.TripActive && !P.Busy, 30);
            await Frames(3);
            var log = P.LaunchLog.Skip(from).ToList();
            var offs = log.Where(l => l.Phase is "flight" or "warp").Select(l => l.AtSeconds - t0).ToList();
            var lands = log.Where(l => l.Phase == "land").Select(l => l.AtSeconds - t0).ToList();
            var hopDepartures = offs.Skip(1).Zip(lands, (o, l) => o - l).ToList(); // each later hop: from the previous landing to its take-off
            _departures.Add(("trip: first hop (J, type, Enter)", offs.FirstOrDefault(double.NaN), lands.FirstOrDefault(double.NaN), string.Join(" ", log.Select(l => F($"{l.Phase}@{l.AtSeconds - t0:0.00}")))));
            foreach (var (h, i) in hopDepartures.Select((h, i) => (h, i))) _departures.Add(($"trip: hop {i + 2} (landing → take-off, incl. the {0.55:0.00} s pause)", h, double.NaN, ""));
            Check(App.Navigator.Current == fern && P.LastTripOutcome == "arrived", F($"the trip arrived at Fern Trail: {lands.Count} landings, take-offs at {string.Join(", ", offs.Select(o => o.ToString("0.00", CultureInfo.InvariantCulture)))} s"));
            await Seconds(0.8);
            await Timed("Backspace (history, the whole trip)", () => Key(Godot.Key.Backspace), root);
        }

        // 7. Shift+Enter straight there: a warp (no pad leads there).
        var deep = fern >= 0 ? Child(fern, "Old Bridge") : -1;
        if (deep >= 0)
        {
            await Seconds(0.8);
            await Timed("Destinations: Shift+Enter straight there (warp)", async () =>
            {
                await Key(Godot.Key.J);
                await Frames(3);
                await TypeText("Old Bridge");
                await Frames(3);
                Inject(new InputEventKey { Keycode = Godot.Key.Shift, PhysicalKeycode = Godot.Key.Shift, Pressed = true, ShiftPressed = true });
                Inject(new InputEventKey { Keycode = Godot.Key.Enter, PhysicalKeycode = Godot.Key.Enter, Pressed = true, ShiftPressed = true });
                await Frames(2);
                Inject(new InputEventKey { Keycode = Godot.Key.Enter, PhysicalKeycode = Godot.Key.Enter, Pressed = false, ShiftPressed = true });
                Inject(new InputEventKey { Keycode = Godot.Key.Shift, PhysicalKeycode = Godot.Key.Shift, Pressed = false });
                await Frames(2);
            }, deep);
            await Seconds(0.8);
            await Timed("Backspace (history, from the warp)", () => Key(Godot.Key.Backspace), root);
        }

        // 8. A belt moon (no pad of its own), when this world has one.
        if (P.Composition!.SkyBodies.FirstOrDefault(k => k.InBelt && k.TargetId >= 0) is { } moon)
        {
            await Seconds(0.8);
            App.Select(moon.TargetId); // [direct]
            await Frames(2);
            await Timed("G (a belt moon: no pad)", () => Key(Godot.Key.G), moon.TargetId);
            await Seconds(0.8);
            await Timed("U (parent, from the moon)", () => Key(Godot.Key.U), root);
        }
        else Log("no belt moon on this world (its folders all have pads): the pad-less case is the warp above");

        // 9. The overview: T, select a world, Enter.
        await Seconds(0.8);
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 3);
        await Seconds(1.2);
        if (P.UniverseKnows(other.TargetId)) P.SelectInUniverse(other.TargetId); // [direct] the click's selection
        await Seconds(0.6);
        await Timed("overview Enter", () => Key(Godot.Key.Enter), other.TargetId);
        await Seconds(1.2);
        _traceTravel = false;
        ReportTravel();
    }

    /// <summary>Writes the trace around every landing and logs, per landing, what the measures show (see the class summary).</summary>
    private void ReportTravel()
    {
        var csv = new StringBuilder("landing,t_rel_s,phase,u,lean_deg,tilt_deg,body_cam_x,body_cam_y,body_cam_z,facing_cam_x,facing_cam_y,facing_cam_z,squash,fly,head_m,foot_m\n");
        var landings = 0;
        for (var i = 1; i < _travelFrames.Count; i++)
        {
            var prev = _travelFrames[i - 1];
            var f = _travelFrames[i];
            if (f.Phase != "Walking" || prev.Phase is not ("Flying" or "Warping")) continue;
            landings++;
            var t0 = f.At;
            var window = _travelFrames.Where(x => x.At >= t0 - 1.0 && x.At <= t0 + 1.0).ToList();
            foreach (var w in window)
            {
                csv.AppendLine(F($"{landings},{w.At - t0:0.0000},{w.Phase},{w.U:0.000},{w.Lean:0.0},{w.Tilt:0.0},{w.BodyCam.X:0.000},{w.BodyCam.Y:0.000},{w.BodyCam.Z:0.000},{w.FacingCam.X:0.000},{w.FacingCam.Y:0.000},{w.FacingCam.Z:0.000},{w.Squash:0.000},{w.Fly:0.000},{w.Head:0.00},{w.Foot:0.00}"));
            }

            var before = window.Where(x => x.At < t0).ToList();
            var after = window.Where(x => x.At >= t0).ToList();
            // On screen: the largest turn of the body's axis and of the facing between two frames, degrees (camera space).
            float MaxStep(List<TravelFrame> s, Func<TravelFrame, Vector3> v) => s.Zip(s.Skip(1), (a, b) => Mathf.RadToDeg(v(a).AngleTo(v(b)))).DefaultIfEmpty(0).Max();
            var stepAfter = MaxStep(window.Where(x => x.At >= t0 - 0.02).ToList(), x => x.BodyCam);
            var stepBefore = MaxStep(before.Where(x => x.At >= t0 - 0.5).ToList(), x => x.BodyCam);
            var yawStep = Mathf.RadToDeg(prev.FacingCam.AngleTo(f.FacingCam));
            var upright = after.FirstOrDefault(x => MathF.Abs(x.Tilt) < UprightDegrees && after.Where(y => y.At >= x.At).All(y => MathF.Abs(y.Tilt) < UprightDegrees));
            var squashed = after.Take(30).Select(x => x.Squash).DefaultIfEmpty(0).Min();
            Log(F($"landing {landings} ({prev.Phase}): tilt at touchdown {f.Tilt:+0;-0}° (lean {f.Lean:0}°, flight pose {f.Fly:0.00}), head {f.Head:0.00} m, lowest foot {f.Foot:+0.00;-0.00} m; the last 0.3 s in the air tilt {before.Where(x => x.At >= t0 - 0.3).Select(x => x.Tilt).DefaultIfEmpty(0).Max():0}° max; upright (<{UprightDegrees:0}°) {(upright.Phase != null ? F($"{upright.At - t0:0.00} s") : "never")} after touchdown; the largest body turn a frame on screen {stepAfter:0.0}° from touchdown (the half second before: {stepBefore:0.0}°); facing turned {yawStep:0.0}° in the touchdown frame; squash min {squashed:+0.00;-0.00} in the first 30 frames; landing squash events {f.Landings - prev.Landings}+{(after.Count > 2 ? after[2].Landings - f.Landings : 0)}"));
        }

        System.IO.File.WriteAllText($"{Out}/landing-trace.csv", csv.ToString());
        Log($"landing trace: {landings} landings, {Out}/landing-trace.csv");
        foreach (var d in _departures) Log(F($"departure | {d.Entry} | {d.Departure:0.00} s to take-off | {d.ToLanding:0.00} s to landing"));
    }

    /// <summary>The traveller counts as upright below this tilt, degrees.</summary>
    private const float UprightDegrees = 10f;

    // ------------------------------------------------------------------ DJ's overview route (PLANET-83)

    /// <summary>DJ's route in each variant, <see cref="OverviewEnterRepeats"/> times each (see the class summary).</summary>
    private async Task OverviewEnterRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 60);
        await WaitSettled(60);
        await Seconds(2.0);
        var countsOnly = OS.GetCmdlineUserArgs().Any(a => a == "--counts-only");
        var name = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--test-target=", StringComparison.Ordinal))?["--test-target=".Length..] ?? "home";
        var root = App.Navigator.Current;
        var target = Child(root, name);
        Check(target >= 0, $"the root has a child named {name}");
        if (target < 0) return;
        Log($"root: {Tree.DisplayName(root)} ({Tree[root].Children.Count} entries; counts only: {countsOnly}); target {name} listed: {Tree[target].ScanState}");
        var variants = new (string Name, Func<int, Task> Run)[]
        {
            ("list, Esc, T, click the world, Enter", async t => { await ListFilter(name); await Key(Godot.Key.Escape); await OpenOverview(); await ClickWorld(t, false); await Key(Godot.Key.Enter); }),
            ("list, toolbar Overview, click the world, Enter", async t => { await ListFilter(name); await Click(App.Hud.FlightButtonRect.GetCenter()); await Seconds(1.0); await ClickWorld(t, false); await Key(Godot.Key.Enter); }),
            ("T, J, type, click the world, Enter", async t => { await OpenOverview(); await ListFilter(name); await ClickWorld(t, false); await Key(Godot.Key.Enter); }),
            ("T, J, type, Enter (the list's)", async t => { await OpenOverview(); await ListFilter(name); await Key(Godot.Key.Enter); }),
            ("T, J, type, ↓, Enter", async t => { await OpenOverview(); await ListFilter(name); await Key(Godot.Key.Down); await Frames(3); await Key(Godot.Key.Enter); }),
            ("T, click the world, Enter", async t => { await OpenOverview(); await ClickWorld(t, false); await Key(Godot.Key.Enter); }),
            ("T, double-click the world", async t => { await OpenOverview(); await ClickWorld(t, true); }),
            ("T, J, type, click the row, Enter", async t => { await OpenOverview(); await ListFilter(name); await ClickRow(t); await Key(Godot.Key.Enter); }),
            ("T, J, type, click the row, Esc, Enter", async t => { await OpenOverview(); await ListFilter(name); await ClickRow(t); await Key(Godot.Key.Escape); await Frames(3); await Key(Godot.Key.Enter); }),
            ("J, type, click the row, Esc, T, Enter", async t => { await ListFilter(name); await ClickRow(t); await Key(Godot.Key.Escape); await OpenOverview(); await Key(Godot.Key.Enter); }),
        };
        var failed = 0;
        var deepOnly = OS.GetCmdlineUserArgs().Any(a => a == "--deep-only"); // only the trip-from-the-overview case below
        foreach (var (vname, run) in deepOnly ? [] : variants)
        {
            for (var rep = 0; rep < OverviewEnterRepeats; rep++)
            {
                await BackTo(root);
                var from = P.LaunchLog.Count;
                var toastBefore = App.Hud.ToastText;
                await run(target);
                var accepted = P.NowSeconds;
                await Until(() => (App.Navigator.Current == target && !P.Busy && !P.UniverseOpen && P.Composition?.FolderId == target) || (App.Hud.ToastText != "" && App.Hud.ToastText != toastBefore), OverviewEnterWaitSeconds);
                await Frames(3);
                var arrived = App.Navigator.Current == target && !P.UniverseOpen && P.Composition?.FolderId == target;
                var toast = App.Hud.ToastText != toastBefore ? App.Hud.ToastText : "";
                var marks = string.Join(" ", P.LaunchLog.Skip(from).Select(l => F($"{l.Phase}@{l.AtSeconds - accepted:+0.00;-0.00}")));
                var state = $"overview {(P.UniverseOpen ? "open" : "closed")}, phase {P.PhaseName}, trip {(P.TripActive ? "active" : "none")}, list {(P.ListOpen ? "open" : "closed")}, keyboard {App.KeyboardPanel}";
                if (!arrived) failed++;
                Check(arrived || toast != "", $"{vname} #{rep + 1}: {(arrived ? "landed on " + name : toast != "" ? "did not travel and said why: " + toast : "did NOT travel and said nothing")} ({state}; marks {marks})");
                if (!arrived && !countsOnly) await Shot($"enter-failed-{vname.Split(',')[0]}-{rep + 1}");
                if (P.TripActive) { await Key(Godot.Key.Escape); await Frames(5); } // a stuck trip stops on Esc (PLANET-40)
                if (P.UniverseOpen) { await Key(Godot.Key.Escape); await Frames(5); }
                if (P.UniverseOpen) { await Key(Godot.Key.Escape); await Frames(5); }
                if (P.ListOpen) { await Key(Godot.Key.Escape); await Frames(3); }
            }
        }

        // A folder two levels down chosen in the list opened in the overview: a trip from the overview (PLANET-83).
        var deep = Tree[target].Children.FirstOrDefault(c => Tree[c].IsDirectory, -1);
        var deepTries = 0;
        if (deep >= 0)
        {
            var deepName = Tree[deep].Name;
            for (var rep = 0; rep < OverviewEnterRepeats; rep++)
            {
                await BackTo(root);
                deepTries++;
                var from = P.LaunchLog.Count;
                var toastBefore = App.Hud.ToastText;
                await OpenOverview();
                await ListFilter(deepName);
                await Key(Godot.Key.Enter);
                var accepted = P.NowSeconds;
                await Until(() => (App.Navigator.Current == deep && !P.Busy && !P.UniverseOpen && P.Composition?.FolderId == deep) || (App.Hud.ToastText != "" && App.Hud.ToastText != toastBefore), OverviewEnterWaitSeconds + 6);
                await Frames(3);
                var arrived = App.Navigator.Current == deep && !P.UniverseOpen && P.Composition?.FolderId == deep;
                var toast = App.Hud.ToastText != toastBefore ? App.Hud.ToastText : "";
                var marks = string.Join(" ", P.LaunchLog.Skip(from).Select(l => F($"{l.Phase}@{l.AtSeconds - accepted:+0.00;-0.00}")));
                if (!arrived) failed++;
                Check(arrived || toast != "", $"T, J, type a folder two levels down, Enter #{rep + 1}: {(arrived ? "landed there" : toast != "" ? "did not travel and said why: " + toast : "did NOT travel and said nothing")} (overview {(P.UniverseOpen ? "open" : "closed")}, phase {P.PhaseName}, trip {(P.TripActive ? "active" : "none")}; marks {marks})");
                if (P.TripActive) { await Key(Godot.Key.Escape); await Frames(5); }
            }
        }

        Log($"overview Enter: {failed} of {variants.Length * OverviewEnterRepeats + deepTries} attempts did not land");
    }

    /// <summary>Each variant of the route is tried this many times (the failure DJ saw was intermittent).</summary>
    private const int OverviewEnterRepeats = 3;

    /// <summary>An accepted Enter must have landed, or said why, within this long, seconds (the overview's fly-in is ≈ 0.75 s, the descent 1 s; listing a WSL folder can take a few seconds).</summary>
    private const double OverviewEnterWaitSeconds = 8;

    private async Task ListFilter(string name)
    {
        await Key(Godot.Key.J);
        await Frames(4);
        await TypeText(name);
        await Frames(4);
    }

    private async Task OpenOverview()
    {
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 3);
        await Seconds(1.2);
    }

    /// <summary>Clicks the target's world in the overview (its system framed first if it is not on screen).</summary>
    private async Task ClickWorld(int id, bool doubleClick)
    {
        if (P.UniverseScreen(id) is not { } at || at.X < 0 || at.Y < 0 || at.X > App.GetViewport().GetVisibleRect().Size.X || at.Y > App.GetViewport().GetVisibleRect().Size.Y)
        {
            Log("the target's world is off screen: framed first [direct]");
            P.SelectInUniverse(id);
            await Seconds(1.0);
            at = P.UniverseScreen(id) ?? Vector2.Zero;
        }

        if (doubleClick) await DoubleClickAt(at); // as Godot delivers one (the second press flagged DoubleClick)
        else await Click(at);
        await Frames(3);
    }

    /// <summary>Clicks the target's row in the open Destinations list (selects it: the shared selection and the card).</summary>
    private async Task ClickRow(int id)
    {
        var r = P.DestinationRowRect($"f{id}");
        if (r.Size.X <= 0) Log("the target's row is not shown in the list");
        else await Click(r.GetCenter());
        await Frames(3);
    }

    /// <summary>Back on the root's surface, walking, nothing open (Backspace if a landing took you elsewhere).</summary>
    private async Task BackTo(int root)
    {
        for (var i = 0; i < 3 && (P.UniverseOpen || P.ListOpen); i++) { await Key(Godot.Key.Escape); await Frames(4); }
        await Until(() => !P.Busy, 10);
        if (App.Navigator.Current != root)
        {
            await Key(Godot.Key.Backspace);
            await Until(() => App.Navigator.Current == root && !P.Busy && P.Composition?.FolderId == root, 12);
        }

        await Seconds(0.8);
    }
}
