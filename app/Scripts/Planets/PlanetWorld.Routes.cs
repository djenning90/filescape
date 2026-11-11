using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// Remembered routes (research E3: the route is revealed by flying it, then remembered): once you have flown between two
/// worlds, the launcher's arc to that world is marked by a faint trail of glowing beads, in both directions, for the rest
/// of the session. New worlds have none, so where you have been and where you have not reads at a glance.
/// </summary>
public partial class PlanetWorld
{
    private readonly HashSet<(int, int)> _flownRoutes = [];
    private MultiMeshInstance3D? _routeBeads;

    public int RememberedRouteCount => _flownRoutes.Count;
    public int RouteBeadCount => _routeBeads?.Multimesh?.InstanceCount ?? 0;

    private void RememberRoute(int from, int to)
    {
        if (from < 0 || to < 0 || from == to) return;
        _flownRoutes.Add((Math.Min(from, to), Math.Max(from, to)));
    }

    private bool Flown(int a, int b) => _flownRoutes.Contains((Math.Min(a, b), Math.Max(a, b)));

    /// <summary>Redraws the beads for the current planet's launchers whose worlds you have flown to (or from).</summary>
    private void UpdateRouteBeads()
    {
        _routeBeads?.QueueFree();
        _routeBeads = null;
        if (_current == null) return;
        var beads = new List<(Transform3D T, Color C)>();
        foreach (var l in _current.Comp.Launchers)
        {
            if (!Flown(_current.FolderId, l.TargetId)) continue;
            Body? body = l.IsParent ? _parent : _children.GetValueOrDefault(l.TargetId);
            if (body == null) continue;
            var from = Gd(_current.Comp.Surface.PointAt(l.Direction, 0.6f));
            var pose = l.IsParent ? body.Comp.ArrivalFor(_current.FolderId) : body.Comp.Arrival;
            var to = body.Node.Transform * Gd(body.Comp.Surface.PointAt(pose.Up));
            var fromUp = Gd(l.Direction);
            var toUp = (body.Node.Transform.Basis * Gd(pose.Up)).Normalized();
            var dist = from.DistanceTo(to);
            var lift = (fromUp + toUp).Normalized();
            if (!lift.IsFinite() || lift.LengthSquared() < 0.1f) lift = fromUp;
            var control = (from + to) / 2 + lift * dist * 0.38f;
            var color = PlanetArt.Palette(body.Comp.Theme).Accent.Lightened(0.3f);
            var n = Math.Clamp((int)(dist / 1.6f), 8, 60);
            for (var i = 1; i < n; i++)
            {
                var u = i / (float)n;
                var p = from.Lerp(control, u).Lerp(control.Lerp(to, u), u);
                var s = Mathf.Lerp(0.16f, 0.07f, u);
                beads.Add((new Transform3D(Basis.Identity.Scaled(new Godot.Vector3(s, s, s)), p), color));
            }
        }

        if (beads.Count == 0) return;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 8, Rings = 4 }, InstanceCount = beads.Count };
        for (var i = 0; i < beads.Count; i++)
        {
            mm.SetInstanceTransform(i, beads[i].T);
            mm.SetInstanceColor(i, beads[i].C);
        }

        _routeBeads = new MultiMeshInstance3D
        {
            Name = "RememberedRoutes",
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(1, 1, 1, 0.55f) },
        };
        AddChild(_routeBeads);
    }
}
