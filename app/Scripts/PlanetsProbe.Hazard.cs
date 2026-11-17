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
/// back in. Then Debris Field, the crowded one: no more pads than its ground holds, the rest as moons reached by G, the
/// frame time while walking there, and a fall from it back onto Event Horizon's solid ground. Walking and jumping are real
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
        P.Walker.Teleport(comp.Surface, stand, toward); // [direct] stand 1.3 m from the edge, facing it
        P.FaceCameraAlong(toward);
        await Frames(25);
        Check(P.EdgeNearness > 0.4f, F($"near the edge the warning rises: {P.EdgeNearness:0.00} ({P.EdgeMetresHere:0.0} m)"));
        Check(P.CrumbsFalling, "rock crumbles off the edge nearest you");
        await Shot("hazard-edge");
        var started = P.FallsStarted;
        var returns = P.FallReturns;
        HoldKey(Godot.Key.W, true);
        await Until(() => P.Falling, 3);
        HoldKey(Godot.Key.W, false);
        Check(P.FallsStarted == started + 1, "walking on past the edge starts a fall");
        await Until(() => P.FallProgress > 0.62f, 4);
        await Shot("hazard-mid-fall");
        await Until(() => P.FallProgress < 0, 4);
        Check(P.LastFallTurns >= 2f && P.LastFallClosestMetres < 0.3f * r, F($"the fall spirals round the black hole: {P.LastFallTurns:0.00} turns, down to {P.LastFallClosestMetres:0.0} m from the centre"));
        await Until(() => P.FallReturns == returns + 1 && !P.Busy, 8);
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

        // A moon (a child without a pad): selected (as a click or the list does) and flown to with G; U back.
        var moon = children.FirstOrDefault(c => comp.LauncherFor(c) == null);
        if (moon > 0)
        {
            App.Selection.Select(moon); // [direct] the selection a click on the moon makes
            await Frames(2);
            await Fly(Godot.Key.G, moon, Tree[moon].Name + " (a moon in Debris Field's sky)");
            await Fly(Godot.Key.U, debris, "Debris Field");
        }

        // Off the nearest edge: the fall returns to Event Horizon, itself a Hazard World, beside Debris Field's launcher.
        var returns = P.FallReturns;
        var open = P.NearestOpenSky();
        if (open is { } o)
        {
            var up = P.Walker.Up;
            var toward = NVector3.Normalize(o - up * NVector3.Dot(o, up));
            P.Walker.Teleport(P.Composition!.Surface, up, toward); // [direct] face the nearest open sky
            P.FaceCameraAlong(toward);
            await Frames(3);
            HoldKey(Godot.Key.W, true);
            await Until(() => P.Falling, 6);
            HoldKey(Godot.Key.W, false);
            await Until(() => P.FallReturns == returns + 1 && !P.Busy, 12);
            await Frames(3);
            var back = P.Composition!.LauncherFor(debris);
            var fromPad = back != null ? PlanetShape.Angle(P.Walker.Up, back.Direction) * P.Composition.Shape.Radius : float.NaN;
            Check(App.Navigator.Current == eh && P.Shell != null && !P.Shell.OverHole(P.Walker.Up) && P.Walker.Grounded,
                F($"a fall from Debris Field returns to Event Horizon on solid ground, {fromPad:0.0} m from Debris Field's launcher"));
        }
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
