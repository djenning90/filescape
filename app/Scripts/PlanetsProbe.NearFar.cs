using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App;

/// <summary>
/// --automate=planets-near-far (PLANET-58): a near and a far launcher that line up on a crowded world. Owns the check
/// that both names stay shown, attached to their pads and selectable when they line up from the arrival camera and
/// while walking towards the near one, and that the near name grows smoothly as it is approached (never past its cap).
/// <para>
/// Steps: settle on the world (<c>--world=NAME</c> flies to a child first, e.g. Constellations on the Starter Sky); turn
/// the camera through a full circle in 15° steps with real right-drags, measuring every pair of named pads; turn back to
/// the best-aligned pair (a near pad and one at least 1.3× as far, their anchors closest on screen horizontally); walk
/// towards the near pad with W, re-aiming as the probe does elsewhere, measuring both names at every step; click each
/// name. A change must keep: the pair found by the same rule (so before/after screenshots show the same case).
/// </para>
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>A pair counts as lined up when their anchors are this close horizontally on screen, pixels (about half a short name's width).</summary>
    private const float AlignedPx = 60f;

    /// <summary>The far pad is at least this many times as far from the camera as the near one.</summary>
    private const float FarRatio = 1.3f;

    /// <summary>A named sign's stem foot is attached when its anchor projects within this of where the sign was laid out, pixels.</summary>
    private const float AttachedPx = 1.5f;

    /// <summary>The scan's camera steps, degrees, and how many (a full turn).</summary>
    private const float ScanStepDegrees = 15f;

    private const int ScanSteps = 24;

    /// <summary>Right-drag turn rate (PlanetWorld.Interact's OrbitYawDegreesPerPixel), degrees per pixel.</summary>
    private const float DragDegreesPerPixel = 0.25f;

    /// <summary>The best near/far pair on screen now: their ids, distances and horizontal separation (pixels); null when none.</summary>
    private (PlanetLauncher Near, PlanetLauncher Far, float Dn, float Df, float Dx)? AlignedPair()
    {
        var c = P.Composition!;
        var cam = P.Camera;
        var vp = App.GetViewport().GetVisibleRect().Grow(-8);
        var seen = new List<(PlanetLauncher L, Vector2 S, float D)>();
        foreach (var l in c.Launchers)
        {
            if (l.IsParent || !Tree.Contains(l.TargetId)) continue;
            var (a, hidden) = P.PadAnchor(l);
            if (hidden || cam.IsPositionBehind(a)) continue;
            var s = cam.UnprojectPosition(a);
            if (!vp.HasPoint(s)) continue;
            seen.Add((l, s, cam.GlobalPosition.DistanceTo(a)));
        }

        (PlanetLauncher, PlanetLauncher, float, float, float)? best = null;
        foreach (var n in seen)
        {
            foreach (var f in seen)
            {
                if (f.D < n.D * FarRatio || f.S.Y >= n.S.Y) continue;
                var dx = MathF.Abs(n.S.X - f.S.X);
                if (best == null || dx < best.Value.Item5) best = (n.L, f.L, n.D, f.D, dx);
            }
        }

        return best;
    }

    /// <summary>Turns the chase camera by real right-drags until its heading is within 2° of <paramref name="heading"/>.</summary>
    private async Task TurnToHeading(float heading)
    {
        for (var i = 0; i < 12; i++)
        {
            var delta = Mathf.Wrap(heading - P.CameraHeadingDegrees(), -180f, 180f);
            if (MathF.Abs(delta) < 2f) break;
            await DragCamera(new Vector2(Math.Clamp(delta, -40f, 40f) / DragDegreesPerPixel, 0));
            await Frames(10);
        }
    }

    /// <summary>Both names of the pair: shown, attached to their freshly projected anchors, not overlapping; logs their sizes.</summary>
    private (float NearPx, float FarPx) CheckPairNamed(PlanetLauncher near, PlanetLauncher far, string when)
    {
        var signs = App.Signs;
        var ok = true;
        var detail = new List<string>();
        foreach (var l in new[] { near, far })
        {
            var shown = signs.ShownIds.Contains(l.TargetId);
            var (a, _) = P.PadAnchor(l);
            var fresh = P.Camera.UnprojectPosition(a);
            var off = shown ? signs.ShownAnchors[l.TargetId].DistanceTo(fresh) : float.NaN;
            ok &= shown && off <= AttachedPx;
            detail.Add(F($"{Tree[l.TargetId].Name} at ({fresh.X:0},{fresh.Y:0}){(shown ? $" in {signs.ShownRectById[l.TargetId]}" : "")}: {(shown ? $"shown {signs.ShownSizePx[l.TargetId]:0.0} px, anchor off {off:0.00} px" : "NOT shown")}"));
        }

        var overlap = signs.ShownRectById.TryGetValue(near.TargetId, out var rn) && signs.ShownRectById.TryGetValue(far.TargetId, out var rf) && rn.Intersects(rf);
        Check(ok && !overlap, $"{when}: both names shown and attached, not overlapping ({string.Join("; ", detail)})");
        return (signs.ShownSizePx.GetValueOrDefault(near.TargetId, float.NaN), signs.ShownSizePx.GetValueOrDefault(far.TargetId, float.NaN));
    }

    /// <summary>
    /// Saves the screen round both pads' names, enlarged 3× with nearest-neighbour sampling, as NN-name.png in --out: the
    /// legibility of the names (their pixels, not a resampled view) and how the near one covers the far one's stem.
    /// </summary>
    private async Task CloseUp(string name, PlanetLauncher near, PlanetLauncher far)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = App.GetViewport().GetTexture().GetImage();
        var box = new Rect2(P.Camera.UnprojectPosition(P.PadAnchor(near).Anchor), Vector2.Zero);
        foreach (var id in new[] { near.TargetId, far.TargetId })
        {
            if (App.Signs.ShownRectById.TryGetValue(id, out var r)) box = box.Merge(r);
        }

        var scale = App.GetWindow().ContentScaleFactor; // viewport units to image pixels
        var region = new Rect2I((Vector2I)((box.Position - new Vector2(12, 12)) * scale), (Vector2I)((box.Size + new Vector2(24, 24)) * scale)).Intersection(new Rect2I(Vector2I.Zero, img.GetSize()));
        if (region.Size.X <= 0 || region.Size.Y <= 0) return;
        var crop = img.GetRegion(region);
        crop.Resize(region.Size.X * 3, region.Size.Y * 3, Image.Interpolation.Nearest);
        var path = $"{Out}/{name}.png";
        crop.SavePng(path);
        Log($"close-up {path} ({region})");
    }

    /// <summary>The near/far journey (see the type's summary).</summary>
    private async Task NearFarRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        if (Arg("world") is { } world)
        {
            var id = Child(App.Navigator.Current, world);
            if (!Check(id >= 0, $"{world} is here")) return;
            App.Selection.Select(id); // [direct] the selection a click makes
            await Frames(2);
            await Fly(Godot.Key.G, id, world);
            await WaitSettled();
        }

        await Until(() => P.CameraSettled, 5);
        await Until(() => Tree[App.Navigator.Current].ScanState == DirScanState.Complete, 15);
        await Seconds(1.5);
        var c = P.Composition!;
        Log(F($"{Tree.FullPath(App.Navigator.Current)}: {c.Launchers.Count(l => !l.IsParent)} launchers, radius {c.Shape.Radius:0.0} m"));
        await Shot("nearfar-arrival");

        // A full turn in place, measuring every pair (the arrival camera orbits the traveller as it turns).
        var start = P.CameraHeadingDegrees();
        (PlanetLauncher Near, PlanetLauncher Far, float Dn, float Df, float Dx, float Heading)? best = null;
        for (var i = 0; i < ScanSteps; i++)
        {
            if (AlignedPair() is { } p)
            {
                if (best == null || p.Dx < best.Value.Dx) best = (p.Near, p.Far, p.Dn, p.Df, p.Dx, P.CameraHeadingDegrees());
            }

            await DragCamera(new Vector2(ScanStepDegrees / DragDegreesPerPixel, 0));
            await Frames(10);
        }

        if (!Check(best is { Dx: < AlignedPx }, F($"a near and a far pad line up from the arrival camera: {(best is { } b0 ? $"{Tree[b0.Near.TargetId].Name} ({b0.Dn:0.0} m) and {Tree[b0.Far.TargetId].Name} ({b0.Df:0.0} m), {b0.Dx:0} px apart at heading {b0.Heading:0}°" : "no pair")}"))) return;
        var (near, far, _, _, _, heading) = best!.Value;
        await TurnToHeading(heading);
        await Frames(10);
        var nearName = Tree[near.TargetId].Name;
        var farName = Tree[far.TargetId].Name;
        if (AlignedPair() is { } now) Log(F($"turned back (from {start:0}°) to {heading:0}°: best pair now {Tree[now.Near.TargetId].Name}/{Tree[now.Far.TargetId].Name}, {now.Dx:0} px"));
        CheckPairNamed(near, far, "lined up from the arrival camera");
        await Shot("nearfar-aligned-arrival");
        await CloseUp("nearfar-aligned-closeup", near, far);

        // Walk towards the near pad, measuring both names after each step.
        var sizes = new List<(float Metres, float Near, float Far)>();
        var shots = new[] { 13f, 10.5f, 8.8f }; // camera to the near name, metres: the chase camera stays ≈ 8 m back on the pad
        var shotIndex = 0;
        for (var i = 0; i < 40 && P.FocusNodeId != near.TargetId; i++)
        {
            var a = P.CameraTurnTo(near.Direction);
            if (MathF.Abs(a) > 8f) await DragCamera(new Vector2(Math.Clamp(a, -30f, 30f) / DragDegreesPerPixel, 0));
            await Hold(0.08, Godot.Key.W);
            await Frames(4);
            var (anchor, _) = P.PadAnchor(near);
            var metres = P.Camera.GlobalPosition.DistanceTo(anchor);
            var shownBoth = App.Signs.ShownIds.Contains(near.TargetId) && App.Signs.ShownIds.Contains(far.TargetId);
            var (np, fp) = (App.Signs.ShownSizePx.GetValueOrDefault(near.TargetId, float.NaN), App.Signs.ShownSizePx.GetValueOrDefault(far.TargetId, float.NaN));
            sizes.Add((metres, np, fp));
            Log(F($"walking: camera {metres:0.0} m from {nearName}'s name: near {np:0.0} px, far {fp:0.0} px{(shownBoth ? "" : " (one not shown)")}"));
            if (shotIndex < shots.Length && metres <= shots[shotIndex])
            {
                CheckPairNamed(near, far, F($"walking, {metres:0.0} m from {nearName}"));
                await Shot(F($"nearfar-walk-{shots[shotIndex]:0.0}m"));
                shotIndex++;
            }
        }

        var shownNear = sizes.Where(s => !float.IsNaN(s.Near)).ToList();
        Check(shownNear.Count >= sizes.Count - 1, $"{nearName}'s name stays shown on the way ({shownNear.Count} of {sizes.Count} steps)");
        var maxStep = shownNear.Zip(shownNear.Skip(1), (x, y) => MathF.Abs(y.Near - x.Near)).DefaultIfEmpty(0).Max();
        var grew = shownNear.Count > 1 ? shownNear[^1].Near - shownNear[0].Near : 0;
        Log(F($"near name from {shownNear.FirstOrDefault().Near:0.0} px at {shownNear.FirstOrDefault().Metres:0.0} m to {shownNear.LastOrDefault().Near:0.0} px at {shownNear.LastOrDefault().Metres:0.0} m; largest step {maxStep:0.00} px"));
        Check(grew > 0, F($"{nearName}'s name grows as you approach (+{grew:0.0} px)"));
        Check(maxStep <= 1.0f, F($"... smoothly: at most 1 px per step of ≈ 0.7 m (largest {maxStep:0.00} px)"));
        Check(shownNear.All(s => s.Near <= Ui.SignLayer.PinnedMaxPx + 1e-3f), F($"... and never past its cap of {Ui.SignLayer.PinnedMaxPx:0} px"));
        var bothShown = sizes.Where(s => !float.IsNaN(s.Near) && !float.IsNaN(s.Far)).ToList();
        Check(bothShown.Count > 0 && bothShown.All(s => s.Near > s.Far), F($"the nearer name is the larger whenever both are shown ({bothShown.Count} steps)"));

        // Selectable: back a few steps so both are in view, then click each name.
        await Hold(0.5, Godot.Key.S);
        await Frames(10);
        foreach (var (l, name) in new[] { (far, farName), (near, nearName) })
        {
            if (!App.Signs.ShownRectById.TryGetValue(l.TargetId, out var r))
            {
                Check(false, $"{name}'s name is on screen to click");
                continue;
            }

            await Click(r.GetCenter());
            await Frames(4);
            Check(App.Selection.Selected == l.TargetId, $"clicking {name}'s name selects {name} (selected: {(App.Selection.Selected >= 0 ? Tree[App.Selection.Selected].Name : "nothing")})");
        }

        await Shot("nearfar-selected");
    }
}
