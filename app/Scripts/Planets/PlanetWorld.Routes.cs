using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// Remembered routes (research E3: the route is revealed by flying it, then remembered): once you have flown between two
/// worlds, the launcher's arc to that world is marked by a faint trail of glowing beads, in both directions, for the rest
/// of the session. New worlds have none, so where you have been and where you have not reads at a glance.
/// <para>
/// The beads trace the arc a flight takes (the same Bézier construction and <see cref="ArcLiftFraction"/> as
/// <see cref="StartFlight"/>; a flight starts from your feet, the beads 0.6 m above the pad), from
/// the pad to the target's arrival point (or, for the way back, to where you arrive beside this world's launcher on the
/// parent). They are rebuilt whenever the current planet is (<see cref="BuildCurrent"/>), since they live in its frame.
/// </para>
/// </summary>
public partial class PlanetWorld
{
    /// <summary>Folder pairs flown between this session, unordered (smaller id first), so a route counts both ways.</summary>
    private readonly HashSet<(int, int)> _flownRoutes = [];
    private MultiMeshInstance3D? _routeBeads;

    // The beads (by eye; rationale not recorded): about one per 1.6 m of straight-line distance, 8–60 per route, shrinking
    // from 0.16 m to 0.07 m in radius towards the destination, starting 0.6 m above the pad.
    private const float BeadSpacingMetres = 1.6f;
    private const int MinBeadsPerRoute = 8, MaxBeadsPerRoute = 60;
    private const float BeadStartRadiusMetres = 0.16f, BeadEndRadiusMetres = 0.07f, BeadPadHeightMetres = 0.6f;

    public int RememberedRouteCount => _flownRoutes.Count;
    public int RouteBeadCount => _routeBeads?.Multimesh?.InstanceCount ?? 0;

    /// <summary>Remembers that you have flown between two folders (ignored for an unknown folder, −1, or a self-loop).</summary>
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
            var from = Gd(_current.Comp.Surface.PointAt(l.Direction, BeadPadHeightMetres));
            var pose = l.IsParent ? body.Comp.ArrivalFor(_current.FolderId) : body.Comp.Arrival;
            var to = body.Node.Transform * Gd(body.Comp.Surface.PointAt(pose.Up));
            var fromUp = Gd(l.Direction);
            var toUp = (body.Node.Transform.Basis * Gd(pose.Up)).Normalized();
            var dist = from.DistanceTo(to);
            var lift = (fromUp + toUp).Normalized();
            if (!lift.IsFinite() || lift.LengthSquared() < 0.1f) lift = fromUp;
            var control = (from + to) / 2 + lift * dist * ArcLiftFraction; // the flight's own arc
            var color = PlanetArt.Palette(body.Comp.Theme).Accent.Lightened(0.3f);
            var n = Math.Clamp((int)(dist / BeadSpacingMetres), MinBeadsPerRoute, MaxBeadsPerRoute);
            for (var i = 1; i < n; i++)
            {
                var u = i / (float)n;
                var p = from.Lerp(control, u).Lerp(control.Lerp(to, u), u);
                var s = Mathf.Lerp(BeadStartRadiusMetres, BeadEndRadiusMetres, u); // the unit sphere scaled: s is the radius
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
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(1, 1, 1, 0.55f) }, // faint: 55 % opaque
        };
        AddChild(_routeBeads);
    }
}
