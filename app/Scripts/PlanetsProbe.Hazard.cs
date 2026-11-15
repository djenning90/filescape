using FileScape.App.Planets;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-hazard --root=sample --vis=planets --test-shell=record</c> (PLANET-34, AD-53): the Starter Sky's
/// broken world. Into Event Horizon; the arrival is on solid ground with the caution cue; the safe route on foot to the
/// way back and to a child launcher (no fall); a hop from the list never crosses a hole; then deliberately off an edge:
/// the warning, the fall (it spirals round the black hole), the return to the Starter Sky beside Event Horizon's launcher
/// by ordinary navigation, and straight back in from that launcher. Walking is real input (keys); stand-points are set
/// [direct]. Screenshots: arrival, launcher, edge, mid-fall, return, re-entered.
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
        Check(eh >= 0, "the Starter Sky has Event Horizon (sample version 4)");
        if (eh < 0) return;

        // Classification on the live sample: Event Horizon (marked, inside the marked sample) and nothing else.
        var hazards = Tree.Nodes.Where(n => n.IsDirectory && HazardRules.IsHazard(Tree, n.Id)).Select(n => n.Name).ToList();
        Check(hazards.Count == 1 && hazards[0] == "Event Horizon", $"only Event Horizon is a hazard in the sample: [{string.Join(", ", hazards)}]");
        var hubLauncher = P.Composition!.LauncherFor(eh);
        Check(hubLauncher != null, "Event Horizon has a launcher on the Starter Sky");
        _recording = true;
        if (hubLauncher != null)
        {
            // [direct] face Event Horizon's world in the sky (its launcher's bearing): a broken world reads as one from afar.
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
        Check(shell != null && comp.Hazard?.Rule == HazardRule.Sample, $"Event Horizon is a broken world: {shell?.Holes.Count ?? 0} holes ({comp.Hazard?.Why})");
        if (shell == null) return;
        Check(P.Character.Kind == WorldKind.Hazard, $"its character: {P.Character.Describe()}");
        Check(P.CautionShown && P.CautionText.Contains("broken", StringComparison.OrdinalIgnoreCase), $"a caution cue on arrival: \"{P.CautionText}\"");
        var r = comp.Shape.Radius;
        Check(!shell.OverHole(P.Walker.Up) && P.EdgeMetresHere > comp.ArrivalRadius + BrokenShell.SafeMarginMetres, F($"the arrival is solid ground, {P.EdgeMetresHere:0.0} m from the nearest edge"));
        Log(F($"Event Horizon: radius {r:0.0} m, holes {string.Join(", ", shell.Holes.Select(h => $"{h.RadiusRadians * r:0.0} m at {PlanetFrame.PolarOf(h.Centre) * 180 / MathF.PI:0}°"))}"));
        await Seconds(0.8);
        await Shot("hazard-arrival");

        // ---- the safe route on foot: to the way back, home (Home), then to a child's launcher; no fall, warning quiet
        var falls = P.FallsStarted;
        var back = comp.ParentLauncher!;
        var closest = await WalkToPad(back.Direction, back.PadRadius, "the way back");
        Check(P.FallsStarted == falls && closest > 3f, F($"the walk to the way back stays on solid ground (closest edge {closest:0.0} m, warning {P.EdgeNearness:0.00})"));
        await Key(Godot.Key.Home);
        await Until(() => !P.Hopping, 4);
        await Frames(10);
        var child = comp.Launchers.First(l => !l.IsParent);
        closest = await WalkToPad(child.Direction, child.PadRadius, child.Name);
        Check(P.FallsStarted == falls && closest > 3f, F($"the walk to {child.Name}'s launcher stays on solid ground (closest edge {closest:0.0} m)"));
        await Seconds(0.5);
        await Shot("hazard-launcher");

        // ---- a hop from the list (Approach) never crosses a hole, even to a file across the planet
        if (comp.Items.FirstOrDefault(i => i.NodeId >= 0) != null)
        {
            // The file farthest from the arrival (the hop starts from the launcher pad we stand on).
            var item = comp.Items.Where(i => i.NodeId >= 0).OrderBy(i => NVector3.Dot(i.Direction, NVector3.UnitY)).First();
            var overHole = false;
            P.Approach(item.NodeId); // what Destinations or the list (J) does
            while (P.Hopping)
            {
                overHole |= shell.OverHole(P.Walker.Up);
                await Frames(1);
            }

            Check(!overHole && P.FallsStarted == falls, $"a hop to {(item.NodeId >= 0 ? Tree[item.NodeId].Name : "a pile")} from the list stays over solid ground");
            await Key(Godot.Key.Escape);
        }

        // ---- off an edge, on purpose: the side breach nearest the arrival, at its point nearest the arrival
        var (edge, hole) = NearestEdgeToArrival(shell);
        var away = NVector3.Normalize(edge - hole.Centre * NVector3.Dot(edge, hole.Centre));
        var outward = -NVector3.Normalize(hole.Centre - edge * NVector3.Dot(hole.Centre, edge)); // along the ground, away from the hole
        var stand = PlanetFrame.Move(edge, outward, 1.3f, r);
        var toward = NVector3.Normalize(edge - stand * NVector3.Dot(edge, stand));
        _ = away;
        P.Walker.Teleport(comp.Surface, stand, toward); // [direct] stand 1.3 m from the edge, facing it
        P.FaceCameraAlong(toward);
        await Frames(20);
        Check(P.EdgeNearness > 0.4f, F($"near the edge the warning rises: {P.EdgeNearness:0.00} ({P.EdgeMetresHere:0.0} m)"));
        await Shot("hazard-edge");
        var started = P.FallsStarted;
        var returns = P.FallReturns;
        HoldKey(Godot.Key.W, true);
        await Until(() => P.Falling, 3);
        HoldKey(Godot.Key.W, false);
        Check(P.FallsStarted == started + 1, "walking on past the edge starts a fall");
        await Until(() => P.FallProgress > 0.62f, 4);
        await Shot("hazard-mid-fall");
        await Until(() => P.FallProgress < 0 , 4);
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

        // ---- and the way back by the keyboard (U) is the ordinary flight
        await Fly(Godot.Key.U, root, Tree.DisplayName(root));
        Check(P.FallsStarted == started + 1, "no other falls on the way");
    }

    /// <summary>The point of any hole's edge nearest the arrival pole, and its hole.</summary>
    private static (NVector3 Edge, ShellHole Hole) NearestEdgeToArrival(BrokenShell shell)
    {
        var best = (Edge: NVector3.UnitY, Hole: shell.Holes[0]);
        var bestDot = -2f;
        foreach (var h in shell.Holes)
        {
            for (var i = 0; i < 720; i++)
            {
                var b = h.BoundaryPoint(i * MathF.PI / 360f);
                if (b.Y > bestDot) { bestDot = b.Y; best = (b, h); }
            }
        }

        return best;
    }

    /// <summary>
    /// Walks onto a pad with the real keys (W, and A or D to steer towards it), up to 10 s; returns the closest the walk came
    /// to an edge, metres.
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
