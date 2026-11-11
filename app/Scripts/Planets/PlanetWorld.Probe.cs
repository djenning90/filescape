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
            if (s.X >= 0 && s.Y >= 0 && s.X <= size.X && s.Y <= size.Y) list.Add(id);
        }

        return list;
    }

    /// <summary>[direct] Stands the traveller on a raised top (the overlook) at a surface direction.</summary>
    public void ProbeStandOn(NVector3 up, float top)
    {
        if (_current == null) return;
        var (a, _) = PlanetShape.Tangents(up);
        _walker.Teleport(_current.Comp.Surface, up, a);
        SnapCamera();
    }
}
