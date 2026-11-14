using FileScape.App.Places;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>What probes read, and the few set-up steps they may take directly (labelled [direct] in their logs).</summary>
public partial class PlanetWorld
{
    public string PhaseName => _hop != null ? "Hopping" : _phase.ToString();
    public bool Hopping => _hop != null;
    public bool CameraSettled => _camBlend <= 0;
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
            var hidden = _current != null && RayPlanet(cam, (p - cam).Normalized(), _current.Comp.Shape.Radius - 0.3f) is { } hit && hit < cam.DistanceTo(p);
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
        var a = Gd(_current!.Comp.Surface.PointAt(l.Direction, 1.35f));
        var cam = _camera.GlobalPosition;
        var d = cam.DistanceTo(a);
        var hidden = RayPlanet(cam, (a - cam).Normalized(), _current.Comp.Shape.Radius - 0.3f) is { } hit && hit < d - 0.5f;
        return (a, hidden);
    }

    /// <summary>Where a world in the sky is anchored (above it, along the camera's up), for the label check.</summary>
    public Godot.Vector3? WorldAnchor(int folderId)
    {
        Body? b = _children.GetValueOrDefault(folderId) ?? (_parent?.FolderId == folderId ? _parent : null);
        if (b == null) return null;
        return b.Node.GlobalPosition + _camera.GlobalBasis.Y * (b.Comp.Shape.Radius * b.Node.Transform.Basis.Scale.X * 1.06f);
    }

    /// <summary>A world in the sky on screen: its projected centre and radius (pixels).</summary>
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

    public bool UniverseKnows(int id) => _uniIndex.ContainsKey(id);

    /// <summary>[direct] Stands the traveller on a raised top (the overlook) at a surface direction.</summary>
    public void ProbeStandOn(NVector3 up, float top)
    {
        if (_current == null) return;
        var (a, _) = PlanetShape.Tangents(up);
        _walker.Teleport(_current.Comp.Surface, up, a);
        SnapCamera();
    }
}
