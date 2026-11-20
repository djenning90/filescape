using FileScape.App.Planets;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-hazard --root=sample --vis=planets --test-shell=record</c> (PLANET-34, PLANET-46; AD-53, AD-59): the
/// Starter Sky's Hazard Worlds. Into Event Horizon: the arrival is on solid ground with the caution cue, the black hole's
/// jet in view and open sky close on both sides of the ridge ahead; the way back on foot along its wide bridge; along the
/// ridge (a narrow bridge with drops on both sides) and over its gap with a real jump; on to the easiest child launcher; a
/// hop from the list keeps to the route; then deliberately off an edge: the warning and crumbling rock, the fall (it spirals
/// round the black hole), the return to the Starter Sky beside Event Horizon's launcher by ordinary navigation, and straight
/// back in. AD-65, AD-69: the ridge's step is a "step" ahead; a slip off the rim caught by Space early (in the coyote
/// time; the mid-window and late catches are in Core, TEST-09); an uncaught slip falls within 0.55 s; Space in the fall, early and late, is a
/// last effort every time and the fall goes on; the fall-to-parent time is measured. Then Debris Field, the crowded one: no
/// more pads than its ground holds, the rest as moons reached by G, the frame time while walking there, its leap crossed
/// with a twirl at the top of the jump (the late twirl and late take-off are in Core, TEST-09), and the same jump without the twirl
/// falling short — its fall back onto Event Horizon's solid ground. Walking and jumping are real
/// input (W and Space); the camera's heading is set [direct] towards the next waypoint, as a player steers with the mouse,
/// and stand-points for the edge are set [direct]. Screenshots: in the sky, arrival, ridge and gap, launcher, edge,
/// mid-fall, return, re-entered, Debris Field arrival.
/// </summary>
public partial class PlanetsProbe
{
    private async Task HazardTour()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5); // the establishing shot
        var root = App.Navigator.Current;
        var eh = Child(root, "Event Horizon");
        Check(eh >= 0, "the Starter Sky has Event Horizon (sample version 5)");
        if (eh < 0) return;
        var debris = Child(eh, "Debris Field");
        Check(debris >= 0, "Event Horizon holds Debris Field, the crowded Hazard World");

        // Classification on the live sample: Event Horizon and Debris Field (marked, inside the marked sample) and nothing else.
        var hazards = Tree.Nodes.Where(n => n.IsDirectory && HazardRules.IsHazard(Tree, n.Id)).Select(n => n.Name).OrderBy(n => n).ToList();
        Check(hazards.SequenceEqual(["Debris Field", "Event Horizon"]), $"only Event Horizon and Debris Field are Hazard Worlds in the sample: [{string.Join(", ", hazards)}]");
        var hubLauncher = P.Composition!.LauncherFor(eh);
        Check(hubLauncher != null, "Event Horizon has a launcher on the Starter Sky");
        _recording = true;
        if (hubLauncher != null)
        {
            // [direct] face Event Horizon's world in the sky (its launcher's bearing): a Hazard World reads as one from afar.
            var up0 = P.Walker.Up;
            var t = hubLauncher.Direction - up0 * NVector3.Dot(hubLauncher.Direction, up0);
            P.Walker.Teleport(P.Composition.Surface, up0, NVector3.Normalize(t));
            P.FaceCameraAlong(NVector3.Normalize(t));
            await Seconds(0.6);
            await Shot("hazard-in-the-sky");
        }

        // ---- in, by the ordinary launch (G on the selection)
        App.Selection.Select(eh); // [direct] the selection a click makes
        await Frames(2);
        await Fly(Godot.Key.G, eh, "Event Horizon");
        var comp = P.Composition!;
        var shell = P.Shell;
        Check(shell != null && comp.Hazard?.Rule == HazardRule.Sample, $"Event Horizon is a Hazard World: {shell?.Lands.Count ?? 0} islands, {shell?.Spans.Count ?? 0} bridges, {shell?.Links.Count(l => l.Jump) ?? 0} gaps to jump ({comp.Hazard?.Why})");
        if (shell == null) return;
        Check(P.Character.Kind == WorldKind.Hazard, $"its character: {P.Character.Describe()}");
        Check(P.CautionShown && P.CautionText.Contains("Hazard World", StringComparison.Ordinal), $"a caution cue on arrival: \"{P.CautionText}\"");
        var r = comp.Shape.Radius;
        var walkable = shell.WalkableFraction();
        Check(walkable is > 0.25f and < 0.55f, F($"about a third of the shell is ground: {walkable:P0} (radius {r:0.0} m)"));
        Check(!shell.OverHole(P.Walker.Up) && P.EdgeMetresHere > comp.ArrivalRadius + HazardTerrain.EdgeMarginMetres - 0.1f, F($"the arrival is solid ground, {P.EdgeMetresHere:0.0} m from the nearest edge"));
        var dropAhead = DropAhead(shell, r, 0f);
        Check(dropAhead < 6.5f, F($"the ground ends {dropAhead:0.0} m straight ahead of the arrival (the jet's opening)"));
        var standFrom = _frames.Count;
        await Seconds(2.2); // the camera turns to the opening as you land
        Log(FrameStats(standFrom, "standing on Event Horizon's arrival"));
        var jet = JetOnScreen(shell, r);
        Check(jet != null, $"the black hole's jet is in view on landing{(jet is { } j ? F($" at ({j.X:0}, {j.Y:0})") : "")}");
        await Shot("hazard-arrival");

        // ---- the way back on foot (its wide bridge, no gap), then Home
        var falls = P.FallsStarted;
        var back = comp.ParentLauncher!;
        var walk = await WalkRoute(back.Direction, back.PadRadius * 0.5f, "the way back");
        Check(walk.Reached && P.FallsStarted == falls && walk.Jumps == 0, F($"the walk to the way back keeps to the route (no gap, no fall; nearest edge {walk.ClosestEdge:0.0} m)"));
        var hopOpen = 0f;
        await Key(Godot.Key.Home);
        while (P.Hopping)
        {
            if (shell.OverHole(P.Walker.Up)) hopOpen += 1;
            await Frames(1);
        }

        await Frames(10);
        Check(hopOpen == 0 && P.FallsStarted == falls, $"Home hops back along the way back's bridge ({hopOpen} frames over open sky)");

        // ---- along the ridge: a narrow bridge with drops on both sides, and its gap, cleared with a real jump
        var foot = shell.Waypoints.OrderBy(w => PlanetShape.Angle(w, comp.Overlook.Direction)).First();
        var ridgeLink = shell.Links.First(l => shell.Waypoints[l.From] == foot || shell.Waypoints[l.To] == foot);
        var ridge = shell.Spans[ridgeLink.Span];
        Log(F($"the ridge: {ridge.LengthMetres:0.0} m, {2 * ridge.HalfWidthMetres:0.0} m wide, gap {(ridge.Gaps.Count > 0 ? ridge.Gaps[0].ToMetres - ridge.Gaps[0].FromMetres : 0):0.00} m"));
        walk = await WalkRoute(foot, 0.6f, "the overlook's foot", shotBeforeGap: "hazard-gap");
        Check(walk.Reached && P.FallsStarted == falls, F($"along the ridge to the overlook's foot: {walk.Jumps} jump(s), nearest edge {walk.ClosestEdge:0.00} m, no fall"));
        Check(walk.Jumps >= 1 && walk.GapsCleared >= 1, $"a gap cleared with a lined-up jump ({walk.GapsCleared} gap(s) crossed)");
        Check(walk.NarrowMetres > 3f, F($"a narrow bridge crossed: {walk.NarrowMetres:0.0} m walked with the edge within 1.3 m on the way"));
        await Shot("hazard-ridge");

        // ---- on to the child launcher the route reaches most easily
        var child = P.EasiestChildLauncher()!;
        walk = await WalkRoute(child.Direction, child.PadRadius * 0.5f, child.Name);
        Check(walk.Reached && P.FallsStarted == falls, F($"along the route to {child.Name}'s launcher: {walk.Jumps} jump(s), no fall"));
        await Seconds(0.4);
        await Shot("hazard-launcher");

        // ---- a hop from the list (Approach) keeps to the route: over solid ground, or over a gap it hops
        if (comp.Items.FirstOrDefault(i => i.NodeId >= 0) is { } anyItem)
        {
            var item = comp.Items.Where(i => i.NodeId >= 0).OrderBy(i => NVector3.Dot(i.Direction, child.Direction)).First();
            var openFrames = 0;
            P.Approach(item.NodeId); // what Destinations or the list (J) does
            while (P.Hopping)
            {
                if (shell.EdgeMetres(P.Walker.Up) < -HazardTerrain.MaxJumpGapMetres) openFrames++;
                await Frames(1);
            }

            Check(openFrames == 0 && P.FallsStarted == falls && !shell.OverHole(P.Walker.Up), $"a hop to {Tree[item.NodeId].Name} from the list follows the route (never more than a gap's width from the ground) and lands on solid ground");
            await Key(Godot.Key.Escape);
            _ = anyItem;
        }

        // ---- off an edge, on purpose: from the arrival island towards the open sky by the jet
        await Key(Godot.Key.Home);
        await Until(() => !P.Hopping, 4);
        await Frames(10);
        var edge = Enumerable.Range(1, 400).Select(i => PlanetFrame.FromPolar(i * 0.025f / r, -25f * MathF.PI / 180f)).First(shell.OverHole);
        var inward = NVector3.Normalize(NVector3.UnitY - edge * edge.Y);
        var stand = PlanetFrame.Move(edge, inward, 1.3f, r);
        var toward = -NVector3.Normalize(inward - stand * NVector3.Dot(inward, stand));

        // AD-65, AD-69: slips off that rim, caught by Space early (mid-window and late: Core, TEST-09); then a slip not caught at all.
        await RecoverySlips(comp, stand, toward);

        P.Walker.Teleport(comp.Surface, stand, toward); // [direct] stand 1.3 m from the edge, facing it
        P.FaceCameraAlong(toward);
        await Frames(25);
        Check(P.EdgeNearness > 0.4f, F($"near the edge the warning rises: {P.EdgeNearness:0.00} ({P.EdgeMetresHere:0.0} m)"));
        Check(P.CrumbsFalling, "rock crumbles off the edge nearest you");
        await Shot("hazard-edge");
        var started = P.FallsStarted;
        var returns = P.FallReturns;
        var efforts = P.Walker.LastEfforts + P.LateEfforts;
        HoldKey(Godot.Key.W, true);
        await Until(() => !P.Walker.Grounded, 3);
        var offMs = Time.GetTicksMsec();
        await Until(() => P.Falling, 3);
        var fallAfterSlip = P.AirSeconds;
        var fallMs = Time.GetTicksMsec();
        HoldKey(Godot.Key.W, false);
        Check(P.FallsStarted == started + 1, "walking on past the edge starts a fall");
        // PLANET-71 (AD-69): an uncaught slip falls promptly, as soon as the catch's window has closed.
        Check(fallAfterSlip < 0.55f, F($"an uncaught slip falls {fallAfterSlip:0.00} s after it (game time; {fallMs - offMs} ms on the clock) — the catch's 0.4 s window and no more"));
        // Space now is too late: a last effort (a flail, a bump and its cry), and the fall goes on — every press, early
        // and late in the fall (PLANET-72: there used to be nothing after its first 0.6 s).
        await Seconds(0.15);
        await Key(Godot.Key.Space);
        Check(P.Walker.LastEfforts + P.LateEfforts == efforts + 1 && P.Falling && P.LastHazardPress.Was == "fall-effort", $"Space pressed too late is a last effort and the fall goes on ({P.Walker.LastEfforts + P.LateEfforts - efforts} effort(s), falling {P.Falling}, \"{P.LastHazardPress.Was}\")");
        await Shot("hazard-last-effort");
        await Until(() => P.FallProgress > 0.45f, 4);
        await Key(Godot.Key.Space);
        await Until(() => P.FallProgress > 0.62f, 4);
        await Shot("hazard-mid-fall");
        await Key(Godot.Key.Space);
        Check(P.Walker.LastEfforts + P.LateEfforts == efforts + 3 && P.Falling, F($"Space mid-fall and later is a last effort each time (3 presses, {P.Walker.LastEfforts + P.LateEfforts - efforts} efforts; the last {P.LastHazardPress.AfterSeconds:0.00} s after the slip)"));
        await Until(() => P.FallProgress < 0, 4);
        Check(P.LastFallTurns >= 2f && P.LastFallClosestMetres < 0.3f * r, F($"the fall spirals round the black hole: {P.LastFallTurns:0.00} turns, down to {P.LastFallClosestMetres:0.0} m from the centre"));
        await Until(() => P.FallReturns == returns + 1 && !P.Busy, 8);
        var returnMs = Time.GetTicksMsec();
        // The sequence stays prompt (PLANET-72): the fall is ≈ 2.6 s and the return a warp's descent; the presses add nothing.
        Check(returnMs - fallMs < 6000, F($"slip to fall {fallMs - offMs} ms, fall to standing on the parent {returnMs - fallMs} ms (under 6 s)"));
        await Frames(3);
        Check(App.Navigator.Current == root && P.Composition?.FolderId == root, $"the fall returns you to the parent by ordinary navigation: {Tree.DisplayName(App.Navigator.Current)}");
        var landing = P.Composition!.LauncherFor(eh);
        var fromPad = landing != null ? PlanetShape.Angle(P.Walker.Up, landing.Direction) * P.Composition.Shape.Radius : float.NaN;
        Check(landing != null && fromPad < landing.PadRadius + 2.5f, F($"set down beside Event Horizon's launcher ({fromPad:0.0} m from its centre)"));
        Check(App.Selection.Selected == eh, "Event Horizon is selected (as coming back up from any world)");
        Check(P.Walker.Grounded && P.Composition.Surface.Shell == null, "standing on the Starter Sky's whole ground");
        Log($"launch log: {string.Join(", ", P.LaunchLog.TakeLast(10).Select(l => F($"{l.Phase}@{l.AtSeconds:0.00}")))}");
        await Seconds(0.6);
        await Shot("hazard-return");

        // ---- straight back in from that launcher
        await WalkToPad(landing!.Direction, landing.PadRadius, "Event Horizon's launcher");
        await Fly(Godot.Key.Space, eh, "Event Horizon again");
        Check(P.Shell != null && !P.Shell.OverHole(P.Walker.Up), "re-entered at once, on solid ground");
        await Seconds(0.8);
        await Shot("hazard-reentered");

        // ---- Debris Field: the crowded one
        if (debris >= 0) await DebrisField(eh, debris);

        // ---- and the way back by the keyboard (U) is the ordinary flight
        if (App.Navigator.Current != eh) await Fly(Godot.Key.U, eh, "Event Horizon");
        await Fly(Godot.Key.U, root, Tree.DisplayName(root));
    }

    /// <summary>
    /// Debris Field (16 child worlds): pads only as many as its ground holds, the rest moons reached by G; the frame time
    /// while walking its route; then off an edge, falling back onto Event Horizon beside Debris Field's launcher.
    /// </summary>
    private async Task DebrisField(int eh, int debris)
    {
        App.Selection.Select(debris); // [direct] the selection a click (or the list) makes
        await Frames(2);
        await Fly(Godot.Key.G, debris, "Debris Field");
        var comp = P.Composition!;
        var shell = P.Shell;
        Check(shell != null, $"Debris Field is a Hazard World: {shell?.Lands.Count ?? 0} islands, {shell?.Spans.Count ?? 0} bridges");
        if (shell == null) return;
        var children = Tree[debris].Children.Where(c => Tree[c].IsDirectory).ToList();
        var pads = comp.Launchers.Count(l => !l.IsParent);
        var moons = comp.SkyBodies.Count(s => s.InBelt);
        Check(pads <= 10 && pads + moons == children.Count, $"a crowded Hazard World: {children.Count} child worlds as {pads} pads and {moons} moons in its sky");
        var standFrom = _frames.Count;
        await Seconds(2.2); // the camera turns to the opening as you land
        Log(FrameStats(standFrom, "standing on Debris Field's arrival"));
        Check(!shell.OverHole(P.Walker.Up) && JetOnScreen(shell, comp.Shape.Radius) != null, "landed on solid ground with the jet in view");
        await Shot("debris-arrival");

        // Walk the route to its easiest launcher, timing the frames.
        var from = _frames.Count;
        var child = P.EasiestChildLauncher()!;
        var walk = await WalkRoute(child.Direction, child.PadRadius * 0.5f, child.Name);
        Check(walk.Reached && walk.Falls == 0, F($"along Debris Field's route to {child.Name}: {walk.Jumps} jump(s), no fall"));
        var ms = _frames.Skip(from).Where(f => f.Phase == "Walking").Select(f => f.Ms).OrderBy(x => x).ToList();
        if (ms.Count >= 10)
        {
            var p95 = ms[(int)(ms.Count * 0.95)];
            var line = F($"frame time walking Debris Field: {ms.Count} frames, mean {ms.Average():0.0} ms, p95 {p95:0.0} ms, max {ms[^1]:0.0} ms");
            if (ProbeTier.Functional) Log("SKIP " + line + " (functional tier: not a measurement)");
            else Check(p95 < FrameP95LimitMs, line);
        }

        // AD-65: its leap, crossed with a twirl at the top of the jump.
        await LeapCrossing(comp, shell);

        // A moon (a child without a pad): selected (as a click or the list does) and flown to with G; U back.
        var moon = children.FirstOrDefault(c => comp.LauncherFor(c) == null);
        if (moon > 0)
        {
            App.Selection.Select(moon); // [direct] the selection a click on the moon makes
            await Frames(2);
            await Fly(Godot.Key.G, moon, Tree[moon].Name + " (a moon in Debris Field's sky)");
            await Fly(Godot.Key.U, debris, "Debris Field");
        }

        // PLANET-71: the long jump again, the twirl late in its window; then the same jump without the twirl, which falls
        // short — that fall returns to Event Horizon, itself a Hazard World, beside Debris Field's launcher. Without a
        // leap, off the nearest edge instead.
        var returns = P.FallReturns;
        var leap = shell.Links.Any(l => l.Advanced) && P.Composition?.FolderId == debris;
        // TEST-09 (AD-88): the late twirl (0.45 s) and the late take-off (PLANET-85: 0.05 s past the board, the twirl at
        // 0.37 s) are replayed on this same leap in Core (HazardSampleReplayTests), which caught the same injected
        // regression as these steps did; the journey keeps the apex twirl above and the plain jump with its fall.
        if (leap)
        {
            // Straight after the flight back from the moon the arrival's camera is still turning, and the gap ahead is read
            // along the camera ("chasm" instead of "leap", 2026-11-20): let the landing settle first.
            await WaitSettled();
            await Until(() => P.CameraSettled, 5);
            if (P.Composition?.FolderId == debris && P.FallReturns == returns) await LeapCrossing(P.Composition!, P.Shell!, LeapTwirl.Plain);
            else Log("SKIP the plain jump: not on Debris Field after the moon");
        }

        var open = leap ? null : P.NearestOpenSky();
        if (leap || open != null)
        {
            if (open is { } edge)
            {
                var up = P.Walker.Up;
                var toward = NVector3.Normalize(edge - up * NVector3.Dot(edge, up));
                P.Walker.Teleport(P.Composition!.Surface, up, toward); // [direct] face the nearest open sky
                P.FaceCameraAlong(toward);
                await Frames(3);
                HoldKey(Godot.Key.W, true);
                await Until(() => P.Falling, 6);
                HoldKey(Godot.Key.W, false);
            }

            await Until(() => P.FallReturns == returns + 1 && !P.Busy, 12);
            await Frames(3);
            var back = P.Composition!.LauncherFor(debris);
            var fromPad = back != null ? PlanetShape.Angle(P.Walker.Up, back.Direction) * P.Composition.Shape.Radius : float.NaN;
            Check(App.Navigator.Current == eh && P.Shell != null && !P.Shell.OverHole(P.Walker.Up) && P.Walker.Grounded,
                F($"a fall from Debris Field returns to Event Horizon on solid ground, {fromPad:0.0} m from Debris Field's launcher"));
        }
    }

    /// <summary>
    /// AD-65's recovery twirl with the real keys, at the three press times AD-69 promises (PLANET-71): from
    /// <paramref name="stand"/> (1.3 m from an edge, facing it along <paramref name="toward"/>), W walks off the rim and
    /// Space comes early (0.03 s after the feet leave it, inside the coyote time, W still held: it used to be a coyote jump
    /// further out), mid-window (0.2 s, W let go) or late (0.05 s before the window closes: 0.30 s with AD-75's 0.35 s,
    /// W held) — each lifts the traveller back onto the rim.
    /// The state file says the same (recoveries, pressAfterSeconds, pressWas).
    /// </summary>
    /// <remarks>
    /// TEST-09 (AD-88): only the early catch runs here, through the keys and PlanetWorld; the mid-window and late catches
    /// from the same stand point are replayed in Core (HazardSampleReplayTests.The_hazard_journey_s_rim_catches_on_event_horizon).
    /// </remarks>
    private async Task RecoverySlips(PlanetComposition comp, NVector3 stand, NVector3 toward)
    {
        await RecoverySlip(comp, stand, toward, 0.03f, true, "early");
    }

    /// <summary>One slip of <see cref="RecoverySlips"/>: Space <paramref name="pressAfter"/> s (game time) after the feet leave the rim, W held on or let go as they do.</summary>
    private async Task RecoverySlip(PlanetComposition comp, NVector3 stand, NVector3 toward, float pressAfter, bool holdOn, string what)
    {
        var shell = comp.Surface.Shell!;
        var falls = P.FallsStarted;
        var recoveries = P.Walker.Recoveries;
        P.Walker.Teleport(comp.Surface, stand, toward); // [direct] stand 1.3 m from the edge, facing it
        P.FaceCameraAlong(toward);
        await Frames(20);
        HoldKey(Godot.Key.W, true);
        var off = false;
        var pressed = false;
        var pressFrames = 0;
        var lowest = 0f;
        for (var frame = 0; frame < 300; frame++)
        {
            if (!P.Walker.Grounded && !off)
            {
                off = true;
                if (!holdOn) HoldKey(Godot.Key.W, false);
            }

            if (off && !pressed && P.AirSeconds >= pressAfter - 0.008f) // half a frame early: the press is read on the next frame
            {
                pressed = true;
                HoldKey(Godot.Key.Space, true);
            }

            if (pressed && ++pressFrames == 5 && what == "early") await Shot("hazard-recovery");
            if (pressed && pressFrames > 12) HoldKey(Godot.Key.Space, false);
            lowest = MathF.Min(lowest, P.Walker.Height.Value);
            if (P.Falling || (pressed && P.Walker.Grounded && pressFrames > 3)) break;
            await Frames(1);
        }

        HoldKey(Godot.Key.W, false);
        HoldKey(Godot.Key.Space, false);
        await Frames(20);
        var (at, was) = P.LastHazardPress;
        Check(P.Walker.Recoveries == recoveries + 1 && P.FallsStarted == falls && P.Walker.Grounded && !shell.OverHole(P.Walker.Up),
            F($"a slip off the rim caught by Space {what} ({at:0.00} s after the slip, W {(holdOn ? "held on" : "let go")}: \"{was}\"): back on the ground ({P.Walker.Recoveries - recoveries} recovery, {P.FallsStarted - falls} falls, {lowest:0.00} m below the rim at the lowest)"));
        if (what == "early") await Shot("hazard-recovered");
        if (P.Falling) await Until(() => !P.Busy && P.FallReturns > 0, 12); // a failed catch: let the return finish
    }

    /// <summary>
    /// The long jump's presses in <see cref="LeapCrossing"/>: the twirl at the top of the jump, late in its window, or none;
    /// <see cref="LateTakeOff"/> (PLANET-85): the take-off press <see cref="LeapLateTakeOffSeconds"/> after the feet have
    /// left the board (in the coyote time), the twirl <see cref="LeapLateTakeOffTwirlSeconds"/> after that. Since TEST-09
    /// (AD-88) the journey runs only Apex and Plain; Late and LateTakeOff are replayed in Core (HazardSampleReplayTests)
    /// and kept here so a regression found there can be watched in the app by adding the call back.
    /// </summary>
    private enum LeapTwirl { Apex, Late, Plain, LateTakeOff }

    /// <summary>The late take-off press comes this long after the feet leave the board, seconds (inside the 0.12 s coyote time: what real keys timed from a stale state did on cfee25d).</summary>
    private const float LeapLateTakeOffSeconds = 0.05f;

    /// <summary>...and the twirl this long after leaving the board, seconds (cfee25d's real-input second press came at 0.367 s).</summary>
    private const float LeapLateTakeOffTwirlSeconds = 0.37f;

    /// <summary>
    /// The long jump's twirl comes this long after take-off in <see cref="LeapTwirl.Late"/>, seconds (game time): late in the
    /// window HazardRecoveryTimingTests measures for these inputs (≈ 0.23–0.52 s after take-off with each press held 0.3 s), so the window is shown
    /// to be forgiving.
    /// </summary>
    private const float LeapLateTwirlSeconds = 0.45f;

    /// <summary>
    /// AD-65's leap with the real keys, on a world that has one: along the route to its near land, then [direct] stood 3 m
    /// before its take-off board facing across; W, Space on the board (0.35 m before the edge) and Space again at the top of
    /// the jump (the vertical speed within ±1.5 m/s), or late in the twirl's window (<see cref="LeapLateTwirlSeconds"/>):
    /// over, onto the landing ledge. The ground ahead reads "leap" first. <see cref="LeapTwirl.Plain"/> (PLANET-71): the same
    /// jump without the twirl falls short, and the fall takes the traveller back up. True when across.
    /// </summary>
    private async Task<bool> LeapCrossing(PlanetComposition comp, BrokenShell shell, LeapTwirl mode = LeapTwirl.Apex)
    {
        var k = shell.Links.ToList().FindIndex(l => l.Advanced);
        if (k < 0) { Log("SKIP no leap on this world"); return false; }
        var link = shell.Links[k];
        var s = shell.Spans[link.Span];
        var (g0, g1) = s.Gaps[0];
        Log(F($"the leap: {g1 - g0:0.00} m, on a bridge of {s.LengthMetres:0.0} m"));
        // The plain jump and the late take-off come straight after a crossing, from the far side: they are stood back on
        // the near side [direct] (the walk round from there is not what they test).
        if (mode is not (LeapTwirl.Plain or LeapTwirl.LateTakeOff))
        {
            var near = await WalkRoute(shell.Waypoints[link.From], 0.8f, "the leap's near side");
            if (!near.Reached) return false;
        }
        var start = s.PointAlong(MathF.Max(0.5f, g0 - 3f));
        var across = s.DirectionAt(start);
        P.Walker.Teleport(comp.Surface, start, across); // [direct] stand on the bridge before the take-off board, facing across
        P.FaceCameraAlong(across);
        await Seconds(0.6);
        Check(P.GapClassAhead() == "leap", $"the gap ahead reads as a leap: \"{P.GapClassAhead()}\"");
        if (mode == LeapTwirl.Apex) await Shot("leap-takeoff");
        var falls = P.FallsStarted;
        var apex = P.Walker.ApexTwirls;
        HoldKey(Godot.Key.W, true);
        bool jumped = false, twirled = false;
        var pressesSince = P.HazardPresses.Count > 0 ? P.HazardPresses[^1].AtSeconds : -1; // the press log (PlanetWorld.Hazard)
        var holdFrames = 0;
        var twirlAt = -1f;
        for (var frame = 0; frame < 360; frame++)
        {
            var along = s.Measure(P.Walker.Up).AlongMetres;
            var airborne = jumped && !P.Walker.Grounded;
            var twirlNow = mode switch
            {
                LeapTwirl.Apex => holdFrames == 0 && MathF.Abs(P.Walker.VerticalVelocity.Value) < 1.5f,
                LeapTwirl.Late => P.AirSeconds >= LeapLateTwirlSeconds - 0.008f, // half a frame early: read on the next frame
                LeapTwirl.LateTakeOff => P.AirSeconds >= LeapLateTakeOffTwirlSeconds - 0.008f,
                _ => false,
            };
            var takeOffNow = mode == LeapTwirl.LateTakeOff
                ? !P.Walker.Grounded && P.AirSeconds >= LeapLateTakeOffSeconds - 0.008f
                : P.Walker.Grounded && along >= g0 - 0.35f;
            if (!jumped && takeOffNow) { jumped = true; HoldKey(Godot.Key.Space, true); holdFrames = mode == LeapTwirl.LateTakeOff ? 8 : 18; }
            else if (airborne && !twirled && twirlNow)
            {
                twirled = true;
                twirlAt = P.AirSeconds;
                HoldKey(Godot.Key.Space, true);
                holdFrames = 18;
            }

            if (holdFrames > 0 && --holdFrames == 0) HoldKey(Godot.Key.Space, false);
            if (P.Falling || (jumped && P.Walker.Grounded && along > g1)) break;
            await Frames(1);
        }

        HoldKey(Godot.Key.W, false);
        HoldKey(Godot.Key.Space, false);
        await Frames(15);
        var beyond = !P.Falling && P.Shell == shell && s.Measure(P.Walker.Up).AlongMetres > g1;
        var fell = P.FallsStarted - falls;
        var presses = P.HazardPresses.Where(p => p.AtSeconds > pressesSince).ToList();
        foreach (var p in presses) Log(F($"  press: {p.AirSeconds:0.000} s after leaving the ground, {p.Phase}, vy {p.VerticalSpeed:+0.00;-0.00} m/s, h {p.Height:+0.00;-0.00} m, open sky {p.OverHole}, to the edge {p.ToEdge:0.00} m → \"{p.Was}\""));
        var takeOffWas = presses.Count > 0 ? presses[0].Was : "none";
        switch (mode)
        {
            case LeapTwirl.Apex:
                Check(beyond && fell == 0 && P.Walker.ApexTwirls == apex + 1, F($"across the leap with a twirl at the top of the jump ({twirlAt:0.00} s after take-off; {(beyond ? "landed beyond" : "short")}, {P.Walker.ApexTwirls - apex} apex twirl, {fell} falls)"));
                await Shot("leap-landed");
                break;
            case LeapTwirl.Late:
                Check(beyond && fell == 0, F($"across the leap with the twirl late in its window ({twirlAt:0.00} s after take-off, \"{P.LastHazardPress.Was}\"; {(beyond ? "landed beyond" : "short")}, {fell} falls)"));
                break;
            case LeapTwirl.LateTakeOff:
                Check(beyond && fell == 0 && takeOffWas == "coyote-jump" && P.LastHazardPress.Was == "apex-twirl",
                    F($"the take-off press {LeapLateTakeOffSeconds:0.00} s after leaving the board is the long jump's take-off (\"{takeOffWas}\"), the twirl at {twirlAt:0.00} s \"{P.LastHazardPress.Was}\": {(beyond ? "landed beyond" : "short")}, {fell} falls"));
                break;
            default:
                Check(!beyond && fell == 1, F($"the same jump without the twirl falls short of the leap ({fell} fall{(fell == 1 ? "" : "s")})"));
                break;
        }

        if (P.Falling || fell > 0) await Until(() => !P.Busy && !P.Falling, 12);
        return beyond;
    }

    /// <summary>The walking frames recorded since index <paramref name="from"/>: count, mean, p95 and max, milliseconds.</summary>
    private string FrameStats(int from, string what)
    {
        var ms = _frames.Skip(from).Where(f => f.Phase == "Walking").Select(f => f.Ms).OrderBy(x => x).ToList();
        return ms.Count == 0 ? $"frame time {what}: no frames" : F($"frame time {what}: {ms.Count} frames, mean {ms.Average():0.0} ms, p95 {ms[(int)(ms.Count * 0.95)]:0.0} ms, max {ms[^1]:0.0} ms");
    }

    /// <summary>Metres from the arrival, <paramref name="degrees"/> right of straight ahead, to the first open sky (99 if none within 15 m).</summary>
    private static float DropAhead(BrokenShell shell, float r, float degrees)
    {
        for (var d = 0.25f; d <= 15f; d += 0.25f)
        {
            if (shell.OverHole(PlanetFrame.FromPolar(d / r, degrees * MathF.PI / 180f))) return d;
        }

        return 99f;
    }

    /// <summary>Where the jet shows on screen: the first of a few points along it above the shell (1.1 to 2 planet radii out along the spin axis) that is in front of the camera and on screen, or null.</summary>
    private Vector2? JetOnScreen(BrokenShell shell, float r)
    {
        var size = App.GetViewport().GetVisibleRect().Size;
        foreach (var radii in new[] { 1.1f, 1.3f, 1.6f, 2f })
        {
            var p = new Vector3(shell.JetAxis.X, shell.JetAxis.Y, shell.JetAxis.Z) * (r * radii);
            if (P.Camera.IsPositionBehind(p)) continue;
            var s = P.Camera.UnprojectPosition(p);
            if (s.X >= 0 && s.Y >= 0 && s.X <= size.X && s.Y <= size.Y) return s;
        }

        return null;
    }

    /// <summary>What a walk along the route did.</summary>
    private readonly record struct RouteWalk(bool Reached, int Jumps, int GapsCleared, int Falls, float ClosestEdge, float NarrowMetres);

    /// <summary>
    /// Walks the route to <paramref name="target"/> with the real keys, as a player who has learnt it: W held, the camera
    /// turned [direct] towards the next waypoint each frame (what steering with the mouse does), and Space pressed (held
    /// 0.3 s, a full jump) when the next leg crosses a gap and the edge is less than 0.8 m ahead. Up to 40 s.
    /// </summary>
    private async Task<RouteWalk> WalkRoute(NVector3 target, float within, string what, string? shotBeforeGap = null)
    {
        var r = P.Composition!.Shape.Radius;
        var falls = P.FallsStarted;
        var closest = float.PositiveInfinity;
        var jumps = 0;
        var gaps = 0;
        var narrow = 0f;
        var hold = 0;
        var wasOverGap = false;
        var prevUp = P.Walker.Up;
        HoldKey(Godot.Key.W, true);
        for (var frame = 0; frame < 2400; frame++)
        {
            if (P.Falling || P.FallsStarted != falls) break;
            if (PlanetShape.Angle(P.Walker.Up, target) * r < within && P.Walker.Grounded) break;
            var step = P.NextRouteStep(target);
            if (step is not { } s) break;
            var up = P.Walker.Up;
            var to = s.Point - up * NVector3.Dot(s.Point, up);
            if (to.LengthSquared() > 1e-10f) P.AimCameraAlong(NVector3.Normalize(to)); // [direct] steer
            var e = P.EdgeMetresHere;
            if (P.Walker.Grounded) closest = MathF.Min(closest, e);
            if (P.Walker.Grounded && e < 1.3f) narrow += PlanetShape.Angle(prevUp, up) * r;
            prevUp = up;
            var overGap = P.Shell is { } sh && sh.OverHole(up);
            if (wasOverGap && !overGap && P.Walker.Grounded) gaps++;
            if (overGap) wasOverGap = true;
            else if (P.Walker.Grounded) wasOverGap = false;
            if (s.Jump && hold == 0 && P.Walker.Grounded)
            {
                var gap = P.GapAheadMetres();
                if (gap is > 0 and < 0.8f)
                {
                    Log($"jumping a gap ahead that reads \"{P.GapClassAhead()}\" on the way to {what}");
                    if (shotBeforeGap != null) { HoldKey(Godot.Key.W, false); await Shot(shotBeforeGap); shotBeforeGap = null; HoldKey(Godot.Key.W, true); }
                    HoldKey(Godot.Key.Space, true);
                    hold = 18;
                    jumps++;
                }
            }

            if (hold > 0 && --hold == 0) HoldKey(Godot.Key.Space, false);
            await Frames(1);
        }

        HoldKey(Godot.Key.W, false);
        HoldKey(Godot.Key.Space, false);
        await Frames(12);
        var reached = PlanetShape.Angle(P.Walker.Up, target) * r < within + 0.4f && P.FallsStarted == falls;
        Check(reached, F($"walked the route to {what} ({PlanetShape.Angle(P.Walker.Up, target) * r:0.0} m from it)"));
        return new RouteWalk(reached, jumps, gaps, P.FallsStarted - falls, closest, narrow);
    }

    /// <summary>
    /// Walks onto a pad with the real keys (W, and A or D to steer towards it), up to 10 s; returns the closest the walk came
    /// to an edge, metres (on a whole world: +∞).
    /// </summary>
    private async Task<float> WalkToPad(NVector3 pad, float padRadius, string what)
    {
        var closest = float.PositiveInfinity;
        var r = P.Composition!.Shape.Radius;
        // [direct] turn to face it first (what a drag of the camera does), so W walks towards it.
        var up = P.Walker.Up;
        var toward = pad - up * NVector3.Dot(pad, up);
        if (toward.LengthSquared() > 1e-8f)
        {
            P.Walker.Teleport(P.Composition.Surface, up, NVector3.Normalize(toward));
            P.FaceCameraAlong(NVector3.Normalize(toward));
        }

        await Frames(3);
        HoldKey(Godot.Key.W, true);
        var held = Godot.Key.None;
        for (var frame = 0; frame < 600 && PlanetShape.Angle(P.Walker.Up, pad) * r > padRadius * 0.45f; frame++) // ≈ 10 s at 60 Hz
        {
            closest = MathF.Min(closest, P.EdgeMetresHere);
            up = P.Walker.Up;
            var to = pad - up * NVector3.Dot(pad, up);
            var camF = -P.Camera.GlobalBasis.Z;
            var f = new NVector3(camF.X, camF.Y, camF.Z);
            f -= up * NVector3.Dot(f, up);
            var right = NVector3.Cross(f, up);
            var side = to.LengthSquared() > 1e-8f && right.LengthSquared() > 1e-8f ? NVector3.Dot(NVector3.Normalize(to), NVector3.Normalize(right)) : 0;
            var want = side > 0.12f ? Godot.Key.D : side < -0.12f ? Godot.Key.A : Godot.Key.None;
            if (want != held)
            {
                if (held != Godot.Key.None) HoldKey(held, false);
                if (want != Godot.Key.None) HoldKey(want, true);
                held = want;
            }

            await Frames(1);
        }

        HoldKey(Godot.Key.W, false);
        if (held != Godot.Key.None) HoldKey(held, false);
        await Frames(12);
        var at = PlanetShape.Angle(P.Walker.Up, pad) * r;
        Check(at < padRadius, F($"walked onto {what}'s pad ({at:0.0} m from its centre)"));
        return closest;
    }
}
