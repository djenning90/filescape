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

    /// <summary>A world in the overview on screen (or null when behind the camera).</summary>
    public Vector2? UniverseScreen(int id)
    {
        if (!_uniIndex.TryGetValue(id, out var i)) return null;
        var at = _uniBodies[i].At;
        return _camera.IsPositionBehind(at) ? null : _camera.UnprojectPosition(at);
    }

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
            $"\"falling\":{(Falling ? "true" : "false")},\"fallsStarted\":{FallsStarted},\"fallReturns\":{FallReturns},\"edgeMetres\":{(float.IsFinite(edge) ? edge : -1):0.##},\"holeBearing\":{hb:0.#},\"holeDistance\":{hd:0.##},\"childPad\":{child?.TargetId ?? -1},\"childPadBearing\":{cb:0.#},\"childPadDistance\":{cd:0.##},\"routeBearing\":{rb:0.#},\"routeDistance\":{rd:0.##},\"routeJump\":{(step?.Jump == true ? "true" : "false")},\"routeLeft\":{step?.RemainingMetres ?? -1:0.##},\"gapAheadMetres\":{GapAheadMetres():0.##},\"selPadBearing\":{sb:0.#},\"selPadDistance\":{sd:0.##},");
    }

    /// <summary>On a Hazard World, the nearest point over open sky (searched in rings out to 20 m), or null.</summary>
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
                var p = PlanetFrame.Move(_walker.Up, a * MathF.Cos(th) + b * MathF.Sin(th), d, r);
                if (shell.OverHole(p)) return p;
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

    public void ProbeStandOn(NVector3 up, float top)
    {
        if (_current == null) return;
        var (a, _) = PlanetShape.Tangents(up);
        _walker.Teleport(_current.Comp.Surface, up, a);
        SnapCamera();
    }
}
