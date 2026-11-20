using FileScape.App.Places;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// What probes read, and the few set-up steps they may take directly (labelled [direct] in their logs). The geometric
/// checks here mirror the ones the world itself uses (the same name anchors and occlusion test), so a probe measures
/// what the player sees; change them together.
/// </summary>
public partial class PlanetWorld
{
    /// <summary>The travel phase by name, or "Hopping" during a hop (probes compare the names).</summary>
    public string PhaseName => _hop != null ? "Hopping" : _phase.ToString();
    public bool Hopping => _hop != null;
    public bool CameraSettled => _camBlend <= 0;
    /// <summary>The flight's progress 0..1, or −1 when not flying.</summary>
    public float FlightProgress => _transfer is { } t && _phase == Phase.Flying ? Mathf.Clamp(t.T / t.Flight, 0, 1) : -1;
    public bool LastWasWarp => LaunchLog.LastOrDefault(l => l.Phase is "warp" or "flight").Phase == "warp";
    public string TitleDetail => _titleDetail.Text;
    public bool PreviewPlaying => _previewPlayer.Playing;
    public int ThumbnailsShown => _current?.Visual?.ThumbnailsShown ?? 0;

    /// <summary>The way back's pad carries its marker treatment on this world (PLANET-95; probes and the state file).</summary>
    public bool WayBackMarked => _current?.Visual?.WayBackMarked ?? false;

    /// <summary>The planet you stand on, as drawn (probes read its thumbnail counters).</summary>
    public PlanetVisual? CurrentVisual => _current?.Visual;

    /// <summary>The chase camera's distance, metres (the wheel zooms it; PLANET-52: the wheel over a reader must not).</summary>
    public float ChaseDistanceMetres => _camDistance;

    /// <summary>The small preview's panel and its text on screen, viewport units (probes aim the wheel and clicks here).</summary>
    public Rect2 PreviewRect => _previewPanel.GetGlobalRect();

    public Rect2 PreviewTextRect => _previewText.GetGlobalRect();

    /// <summary>The preview text's scroll position, pixels from the top (probes: the wheel over it scrolls it).</summary>
    public double PreviewScroll => _previewText.GetVScrollBar().Value;

    /// <summary>The preview's Expand button on screen (an empty rectangle while it is hidden).</summary>
    public Rect2 PreviewExpandRect => _expandButton.IsVisibleInTree() ? _expandButton.GetGlobalRect() : new Rect2();

    /// <summary>Worlds in the sky whose centre is on screen and in front of the camera (research E1: you can see where to go).</summary>
    public List<int> WorldsInView()
    {
        var list = new List<int>();
        var size = GetViewport().GetVisibleRect().Size;
        foreach (var (id, b) in _children)
        {
            var p = b.Node.GlobalPosition;
            if (_camera.IsPositionBehind(p)) continue;
            var s = _camera.UnprojectPosition(p);
            var cam = _camera.GlobalPosition;
            var hidden = _current != null && RayPlanet(cam, (p - cam).Normalized(), _current.Comp.Shape.Radius - OcclusionInsetMetres) is { } hit && hit < cam.DistanceTo(p);
            if (!hidden && s.X >= 0 && s.Y >= 0 && s.X <= size.X && s.Y <= size.Y) list.Add(id);
        }

        return list;
    }

    /// <summary>Signed angle (degrees, positive = to the right) from the camera's heading to a point on the surface.</summary>
    public float CameraTurnTo(NVector3 surfaceDirection)
    {
        var up = Gd(_walker.Up);
        var to = Gd(_current!.Comp.Surface.PointAt(surfaceDirection)) - Gd(_walker.Feet);
        to -= up * to.Dot(up);
        var f = -_camera.GlobalBasis.Z;
        f -= up * f.Dot(up);
        if (to.LengthSquared() < 1e-6f || f.LengthSquared() < 1e-6f) return 0;
        return -Mathf.RadToDeg(f.Normalized().SignedAngleTo(to.Normalized(), up));
    }

    /// <summary>Where a pad's name is anchored (surface point 1.35 m up), and whether the planet hides it from the camera.</summary>
    public (Godot.Vector3 Anchor, bool Hidden) PadAnchor(PlanetLauncher l)
    {
        var a = Gd(_current!.Comp.Surface.PointAt(l.Direction, PadNameHeightMetres));
        var cam = _camera.GlobalPosition;
        var d = cam.DistanceTo(a);
        var hidden = RayPlanet(cam, (a - cam).Normalized(), _current.Comp.Shape.Radius - OcclusionInsetMetres) is { } hit && hit < d - OcclusionSlackMetres;
        return (a, hidden);
    }

    /// <summary>Where a world in the sky is anchored (above it, along the camera's up), for the label check.</summary>
    public Godot.Vector3? WorldAnchor(int folderId)
    {
        Body? b = _children.GetValueOrDefault(folderId) ?? (_parent?.FolderId == folderId ? _parent : null);
        if (b == null) return null;
        return b.Node.GlobalPosition + _camera.GlobalBasis.Y * (b.Comp.Shape.Radius * b.Node.Transform.Basis.Scale.X * WorldNameLiftRadii);
    }

    /// <summary>A world in the sky on screen: its projected centre and radius (viewport units); far off screen with radius 0 when it is behind the camera or unknown.</summary>
    public (Vector2 Centre, float Radius) WorldOnScreen(int folderId)
    {
        Body? b = _children.GetValueOrDefault(folderId) ?? (_parent?.FolderId == folderId ? _parent : null);
        if (b == null || _camera.IsPositionBehind(b.Node.GlobalPosition)) return (new Vector2(-9999, -9999), 0);
        var c = _camera.UnprojectPosition(b.Node.GlobalPosition);
        var edge = _camera.UnprojectPosition(b.Node.GlobalPosition + _camera.GlobalBasis.Y * (b.Comp.Shape.Radius * b.Node.Transform.Basis.Scale.X));
        return (c, c.DistanceTo(edge));
    }

    /// <summary>The biggest world in the sky that a click at its centre would pick (for the real-input probe), or null.</summary>
    public (int Id, Vector2 Centre)? ClickableWorld()
    {
        (int, Vector2)? best = null;
        var bestR = 0f;
        var size = GetViewport().GetVisibleRect().Size;
        foreach (var id in WorldsInView())
        {
            var (c, r) = WorldOnScreen(id);
            // Clear of the screen's edges and the HUD bars (60 px at the sides, 160 at the top, 120 at the bottom).
            if (r <= bestR || c.X < 60 || c.Y < 160 || c.X > size.X - 60 || c.Y > size.Y - 120 || OverHud(c)) continue;
            if (Pick(c).Id != id) continue;
            best = (id, c);
            bestR = r;
        }

        return best;
    }

    /// <summary>The camera's heading on the ground, degrees from the local north, positive to the right (for probes).</summary>
    public float CameraHeadingDegrees()
    {
        var up = Gd(_walker.Up);
        var north = Gd(PlanetFrame.North(_walker.Up));
        var f = _camFwd - up * _camFwd.Dot(up);
        if (f.LengthSquared() < 1e-6f) return 0;
        return -Mathf.RadToDeg(north.SignedAngleTo(f.Normalized(), up));
    }

    /// <summary>The heading to the world <paramref name="id"/> in this sky, as <see cref="CameraHeadingDegrees"/> measures it (0 when it is not in the sky; probes).</summary>
    public float HeadingTo(int id)
    {
        if (SkyCentreOf(id) is not { } p) return 0;
        var up = Gd(_walker.Up);
        var north = Gd(PlanetFrame.North(_walker.Up));
        var f = p - Gd(_walker.Feet);
        f -= up * f.Dot(up);
        if (f.LengthSquared() < 1e-6f) return 0;
        return -Mathf.RadToDeg(north.SignedAngleTo(f.Normalized(), up));
    }

    /// <summary>
    /// The look's elevation as the player sees it, degrees (PLANET-67): the angle of the camera's view direction above the
    /// local horizontal — the traveller's up on a planet, the overview's up in the overview; negative looks down. A
    /// vertical drag must move this, whatever framing is in force (arrival, overlook, hazard view, edge peek).
    /// </summary>
    public float ViewPitchDegrees
    {
        get
        {
            var up = _universe != null ? Godot.Vector3.Up : Gd(_walker.Up);
            var fwd = -_camera.GlobalBasis.Z;
            return Mathf.RadToDeg(MathF.Asin(Mathf.Clamp(fwd.Normalized().Dot(up), -1f, 1f)));
        }
    }

    /// <summary>The pitch you chose with drags, degrees above the traveller (the framings add to it; probes).</summary>
    public float ChosenPitchDegrees => _camPitchDegrees;

    /// <summary>The pitch the chase camera's boom stood at in the last walking frame, degrees above the traveller: the
    /// chosen pitch with the arrival, overlook, hazard and edge framings applied (probes).</summary>
    public float BoomPitchDegrees => _boomPitchDegrees;

    /// <summary>The walking camera's usual pitch, the arrival framing's and the overlook view's, degrees (probes compare with them).</summary>
    public const float StartPitch = StartCamPitchDegrees, ArrivalPitch = ArrivalPitchDegrees, OverlookPitch = OverlookPitchDegrees;

    /// <summary>The overlook view's weight now, 0..1 (probes).</summary>
    public float OverlookView01 => _overlook01;

    /// <summary>The overview camera's elevation round its focus, degrees (positive: above; probes).</summary>
    public float UniversePitchDegrees => Mathf.RadToDeg(_uniPitch);

    /// <summary>A world in the overview on screen (or null when behind the camera).</summary>
    public Vector2? UniverseScreen(int id)
    {
        if (!_uniIndex.TryGetValue(id, out var i)) return null;
        var at = _uniBodies[i].At;
        return _camera.IsPositionBehind(at) ? null : _camera.UnprojectPosition(at);
    }

    /// <summary>The folders drawn as worlds in this world's sky (probes, PLANET-87).</summary>
    public IEnumerable<int> ChildWorldIds => _children.Keys;

    /// <summary>The overview has a world for this folder.</summary>
    public bool UniverseKnows(int id) => _uniIndex.ContainsKey(id);

    /// <summary>Turns the chase camera to look along a surface direction (probes).</summary>
    public void FaceCameraAlong(NVector3 tangent)
    {
        var up = Gd(_walker.Up);
        var f = Gd(tangent);
        f -= up * f.Dot(up);
        if (f.LengthSquared() > 1e-6f) _camFwd = f.Normalized();
        _sinceOrbitSeconds = 0;
        SnapCamera();
    }

    /// <summary>
    /// [direct] Aims the chase camera along a surface direction without turning the traveller (what steering with the
    /// mouse does): probes that walk a route turn the view towards the next waypoint each frame and hold W.
    /// </summary>
    public void AimCameraAlong(NVector3 tangent)
    {
        var up = Gd(_walker.Up);
        var f = Gd(tangent);
        f -= up * f.Dot(up);
        if (f.LengthSquared() < 1e-6f) return;
        _sinceOrbitSeconds = 0;
        SnapCamera(f.Normalized());
    }

    /// <summary>Rebuilds the current world where you stand (probes: after <see cref="ProbeCharacter"/> changes).</summary>
    public void ProbeRebuild()
    {
        if (_s.Tree is not { } tree || _current == null) return;
        BuildCurrent(tree, _current.FolderId, arriveFrom: -2);
        SnapCamera();
    }

    /// <summary>The white flash's current opacity (0–1) and the overview's camera state (probes: the camera trace).</summary>
    public string CameraTrace() => System.FormattableString.Invariant(
        $"flash={_flash.Color.A:R} pos=({_camera.GlobalPosition.X:R},{_camera.GlobalPosition.Y:R},{_camera.GlobalPosition.Z:R}) fov={_camera.Fov:R} dist={_uniDistance:R} focus=({_uniFocus.X:R},{_uniFocus.Y:R},{_uniFocus.Z:R}) phase={PhaseName}");

    /// <summary>[direct] Stands the traveller at a surface direction (on the overlook, say) and snaps the camera;
    /// <paramref name="top"/> is not used.</summary>
    /// <summary>
    /// For the real-input probe (2026-11-15; AD-59, 2026-11-16): falls, the nearest open sky, the child launcher the route
    /// reaches most easily (fewest jumps, then shortest), the next waypoint on the route to it, and the launcher of whatever
    /// is selected, each as a bearing from the camera (degrees, + right) and a surface distance (metres); and how far ahead,
    /// along the camera's heading, the ground ends (for timing a jump). Steer by <c>routeBearing</c> until
    /// <c>routeLeft</c> is under a metre; when <c>routeJump</c> is true, run and press Space once <c>gapAheadMetres</c>
    /// is under about 0.8.
    /// </summary>
    /// <summary>
    /// Metres along the leap's bridge to its take-off edge on the side the walker is nearer (the side its stand point is
    /// on), or −1 off that bridge or past the edge: "leapToEdge" in the state file and the press log.
    /// </summary>
    private float LeapToEdgeMetres()
    {
        if (_current?.Comp.Surface.Shell is not { } shell) return -1;
        var k = shell.Links.ToList().FindIndex(l => l.Advanced);
        if (k < 0) return -1;
        var s = shell.Spans[shell.Links[k].Span];
        if (s.Gaps.Count == 0) return -1;
        var (g0, g1) = s.Gaps[0];
        var standA = s.PointAlong(MathF.Max(0.5f, g0 - LeapStandMetres));
        var standB = s.PointAlong(MathF.Min(s.LengthMetres - 0.5f, g1 + LeapStandMetres));
        var fromA = PlanetShape.Angle(_walker.Up, standA) <= PlanetShape.Angle(_walker.Up, standB);
        var (along, side) = s.Measure(_walker.Up);
        var onBridge = MathF.Abs(side) <= s.HalfWidthMetres && along > 0 && along < s.LengthMetres;
        return !onBridge ? -1f : fromA ? (along <= g0 ? g0 - along : -1f) : (along >= g1 ? along - g1 : -1f);
    }

    /// <summary>The long jump's stand point in <see cref="LeapStateJson"/> is this far before the take-off edge along the bridge, metres (a run-up of ≈ 0.5 s at walking pace).</summary>
    private const float LeapStandMetres = 3f;

    /// <summary>
    /// The state file's long-jump fields on a Hazard World with a leap (PLANET-71; tools/os-planets-probe.ps1 -Recovery):
    /// "leapRoute*" — the route's next step towards the stand point <see cref="LeapStandMetres"/> before the leap's nearer
    /// take-off edge (bearing as the other route fields, metres, whether it crosses a gap, metres left); "leapAcrossBearing"
    /// — the turn to face across from there; "leapToEdge" — metres along the bridge to the take-off edge while on that
    /// bridge (−1 otherwise); "leapWidth" the gap's width. All −1 / 0 without a leap.
    /// </summary>
    private string LeapStateJson()
    {
        var none = "\"leapRouteBearing\":0,\"leapRouteDistance\":-1,\"leapRouteJump\":false,\"leapRouteLeft\":-1,\"leapAcrossBearing\":0,\"leapToEdge\":-1,\"leapWidth\":0,";
        if (_current?.Comp.Surface.Shell is not { } shell) return none;
        var k = shell.Links.ToList().FindIndex(l => l.Advanced);
        if (k < 0) return none;
        var s = shell.Spans[shell.Links[k].Span];
        if (s.Gaps.Count == 0) return none;
        var (g0, g1) = s.Gaps[0];
        var r = _current.Comp.Shape.Radius;
        var standA = s.PointAlong(MathF.Max(0.5f, g0 - LeapStandMetres));
        var standB = s.PointAlong(MathF.Min(s.LengthMetres - 0.5f, g1 + LeapStandMetres));
        var fromA = PlanetShape.Angle(_walker.Up, standA) <= PlanetShape.Angle(_walker.Up, standB);
        var stand = fromA ? standA : standB;
        var across = fromA ? s.PointAlong(g1 + 2f) : s.PointAlong(g0 - 2f);
        var toEdge = LeapToEdgeMetres();
        var step = NextRouteStep(stand);
        var (rb, rd) = step is { } st ? (CameraTurnTo(st.Point), PlanetShape.Angle(_walker.Up, st.Point) * r) : (0f, -1f);
        return System.FormattableString.Invariant(
            $"\"leapRouteBearing\":{rb:0.#},\"leapRouteDistance\":{rd:0.##},\"leapRouteJump\":{(step?.Jump == true ? "true" : "false")},\"leapRouteLeft\":{step?.RemainingMetres ?? -1:0.##},\"leapAcrossBearing\":{CameraTurnTo(across):0.#},\"leapToEdge\":{toEdge:0.##},\"leapWidth\":{g1 - g0:0.##},");
    }

    private string HazardStateJson()
    {
        if (_current == null) return "";
        (float B, float D) To(NVector3 dir) => (CameraTurnTo(dir), PlanetShape.Angle(_walker.Up, dir) * _current.Comp.Shape.Radius);
        var open = NearestOpenSky();
        var (hb, hd) = open is { } o ? To(o) : (0f, -1f);
        var child = EasiestChildLauncher();
        var (cb, cd) = child != null ? To(child.Direction) : (0f, -1f);
        var step = child != null ? NextRouteStep(child.Direction) : null;
        var (rb, rd) = step is { } st ? To(st.Point) : (0f, -1f);
        var sel = _current.Comp.Launchers.FirstOrDefault(l => l.TargetId == _s.Selection.Selected);
        var (sb, sd) = sel != null ? To(sel.Direction) : (0f, -1f);
        var edge = EdgeMetresHere;
        return System.FormattableString.Invariant(
            $"\"falling\":{(Falling ? "true" : "false")},\"fallsStarted\":{FallsStarted},\"fallReturns\":{FallReturns},\"recoveries\":{_walker.Recoveries},\"lastEfforts\":{_walker.LastEfforts + LateEfforts},\"apexTwirls\":{_walker.ApexTwirls},\"belowRimSeconds\":{_walker.BelowRimSeconds:0.###},\"recoveryOpen\":{(_walker.RecoveryOpen ? "true" : "false")},\"carrying\":{(_walker.Carrying ? "true" : "false")},{LeapStateJson()}\"airSeconds\":{_airSeconds:0.###},\"pressAfterSeconds\":{LastHazardPress.AfterSeconds:0.###},\"pressWas\":\"{LastHazardPress.Was}\",\"fallEfforts\":{LateEfforts},\"walkerEfforts\":{_walker.LastEfforts},\"gapClassAhead\":\"{GapClassAhead()}\",\"edgeMetres\":{(float.IsFinite(edge) ? edge : -1):0.##},\"holeBearing\":{hb:0.#},\"holeDistance\":{hd:0.##},\"childPad\":{child?.TargetId ?? -1},\"childPadBearing\":{cb:0.#},\"childPadDistance\":{cd:0.##},\"routeBearing\":{rb:0.#},\"routeDistance\":{rd:0.##},\"routeJump\":{(step?.Jump == true ? "true" : "false")},\"routeLeft\":{step?.RemainingMetres ?? -1:0.##},\"gapAheadMetres\":{GapAheadMetres():0.##},\"selPadBearing\":{sb:0.#},\"selPadDistance\":{sd:0.##},\"catchWindow\":{_walker.Params.Hazard.Window.TotalSeconds:0.###},\"presses\":{HazardPressesJson()},");
    }

    /// <summary>
    /// A real drop is open sky that goes on at least this far beyond its edge along the same bearing, metres: since AD-65
    /// the nearest open sky is often a crack you hop (or skim across walking), not somewhere you fall.
    /// </summary>
    private const float DropBeyondMetres = 3f;

    /// <summary>On a Hazard World, the nearest point over open sky with a real drop beyond it (<see cref="DropBeyondMetres"/>; searched in rings out to 20 m), or null.</summary>
    public NVector3? NearestOpenSky()
    {
        if (Shell is not { } shell || _current == null) return null;
        var r = _current.Comp.Shape.Radius;
        var (a, b) = PlanetShape.Tangents(_walker.Up);
        // Coarse on purpose (the real-input probe reads it every 0.15 s): rings 0.4 m apart, 32 bearings each.
        for (var d = 0.4f; d <= 20f; d += 0.4f)
        {
            for (var k = 0; k < 32; k++)
            {
                var th = Mathf.Tau * k / 32;
                var dir = a * MathF.Cos(th) + b * MathF.Sin(th);
                var p = PlanetFrame.Move(_walker.Up, dir, d, r);
                if (!shell.OverHole(p)) continue;
                var drop = true;
                for (var e = 0.5f; e <= DropBeyondMetres && drop; e += 0.5f) drop = shell.OverHole(PlanetFrame.Move(_walker.Up, dir, d + e, r));
                if (drop) return p;
            }
        }

        return null;
    }

    /// <summary>On a Hazard World, the child launcher with the easiest route from here (fewest jumps, then shortest); elsewhere the first child launcher.</summary>
    public PlanetLauncher? EasiestChildLauncher()
    {
        if (_current == null) return null;
        var children = _current.Comp.Launchers.Where(l => !l.IsParent).ToList();
        if (Shell is not { } shell) return children.FirstOrDefault();
        return children
            .Select(l => (l, Route: shell.Route(_walker.Up, l.Direction)))
            .OrderBy(x => x.Route.Count(s => s.Jump))
            .ThenBy(x => RouteMetres(x.Route))
            .Select(x => x.l)
            .FirstOrDefault();
    }

    /// <summary>The next point to head for on the route to <paramref name="target"/>, whether a gap lies before it, and the metres left to the target; null off a Hazard World.</summary>
    public (NVector3 Point, bool Jump, float RemainingMetres)? NextRouteStep(NVector3 target)
    {
        if (Shell is not { } shell || _current == null) return null;
        var route = shell.Route(_walker.Up, target);
        var r = _current.Comp.Shape.Radius;
        // A waypoint within 0.8 m counts as reached: head for the one after it.
        var k = 0;
        while (k < route.Count - 1 && PlanetShape.Angle(_walker.Up, route[k].Point) * r < 0.8f) k++;
        return (route[k].Point, route[k].Jump, RouteMetres(route));
    }

    /// <summary>The length of a route from where the traveller stands, metres.</summary>
    private float RouteMetres(IReadOnlyList<RouteStep> route)
    {
        var sum = 0f;
        var prev = _walker.Up;
        foreach (var s in route) { sum += PlanetShape.Angle(prev, s.Point); prev = s.Point; }
        return sum * (_current?.Comp.Shape.Radius ?? 0);
    }

    /// <summary>On a Hazard World, how far ahead along the camera's heading the ground ends, metres (up to 6 m; −1 when it does not).</summary>
    public float GapAheadMetres()
    {
        if (Shell is not { } shell || _current == null) return -1;
        var up = Gd(_walker.Up);
        var f = -_camera.GlobalBasis.Z;
        f -= up * f.Dot(up);
        if (f.LengthSquared() < 1e-6f) return -1;
        var fwd = new NVector3(f.X, f.Y, f.Z) / f.Length();
        for (var d = 0f; d <= 6f; d += 0.1f)
        {
            if (shell.OverHole(PlanetFrame.Move(_walker.Up, fwd, d, _current.Comp.Shape.Radius))) return d;
        }

        return -1;
    }

    /// <summary>
    /// On a Hazard World, what the open sky ahead along the camera's heading is (AD-65): "step" or "leap" (a bridge's own
    /// gap of that class), "hop" (other ground within <see cref="HazardTerrain.HopMaxMetres"/>), "chasm" (no ground for
    /// <see cref="HazardTerrain.SteeringMinMetres"/>), "gap" (other ground in between: what AD-65 removes where it misleads),
    /// or "" (no edge within 6 m, or a whole world). For probes and the state file.
    /// </summary>
    public string GapClassAhead()
    {
        if (Shell is not { } shell || _current == null) return "";
        var edge = GapAheadMetres();
        if (edge < 0) return "";
        var up = Gd(_walker.Up);
        var f = -_camera.GlobalBasis.Z;
        f -= up * f.Dot(up);
        var fwd = new NVector3(f.X, f.Y, f.Z) / f.Length();
        var r = _current.Comp.Shape.Radius;
        var start = PlanetFrame.Move(_walker.Up, fwd, edge, r);
        foreach (var s in shell.Spans)
        {
            if (s.Gaps.Count == 0) continue;
            var (along, side) = s.Measure(start);
            var (g0, g1) = s.Gaps[0];
            if (MathF.Abs(side) < s.HalfWidthMetres + ShellSpan.AdvancedLedgeMetres && along > g0 - 1f && along < g1 + 1f) return s.GapClass == ShellGapClass.Advanced ? "leap" : "step";
        }

        for (var d = 0.1f; d <= HazardTerrain.SteeringMinMetres; d += 0.1f)
        {
            if (!shell.OverHole(PlanetFrame.Move(start, fwd, d, r))) return d <= HazardTerrain.HopMaxMetres ? "hop" : "gap";
        }

        return "chasm";
    }

    public void ProbeStandOn(NVector3 up, float top)
    {
        if (_current == null) return;
        var (a, _) = PlanetShape.Tangents(up);
        _walker.Teleport(_current.Comp.Surface, up, a);
        SnapCamera();
    }
}

/// <summary>
/// The overview as it is drawn (FOLLOW-1, 2026-11-17): which bodies are actually inside the view and how big they look,
/// so a probe can tell a blank overview from one that only has bodies on paper (a positive body count alone missed it).
/// </summary>
public partial class PlanetWorld
{
    /// <summary>A body counts as drawn when its centre is in the view and it is at least this wide, pixels (viewport units).</summary>
    private const float DrawnBodyMinPixels = 3f;

    /// <summary>Bodies in the view now: node, screen centre (viewport units), apparent diameter (pixels).</summary>
    public List<(int Id, Vector2 At, float Pixels)> UniverseOnScreen()
    {
        var list = new List<(int, Vector2, float)>();
        if (_universe == null) return list;
        var vp = GetViewport().GetVisibleRect();
        var right = _camera.GlobalBasis.X;
        for (var i = 0; i < _uniBodies.Count; i++)
        {
            var (b, at) = _uniBodies[i];
            if (!Drawn(i) || _camera.IsPositionBehind(at)) continue; // folded worlds are not on screen (PLANET-91)
            var c = _camera.UnprojectPosition(at);
            var edge = _camera.UnprojectPosition(at + right * DrawnRadius(b, at));
            var px = 2f * c.DistanceTo(edge);
            if (vp.HasPoint(c) && px >= DrawnBodyMinPixels) list.Add((b.NodeId, c, px));
        }

        return list;
    }

    /// <summary>The overview camera's distance from what it frames, metres, and the framed body.</summary>
    public (float Metres, int Focus) UniverseFraming => (_uniDistance, _uniSelected);
}
