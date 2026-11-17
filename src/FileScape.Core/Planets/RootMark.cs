using System.Numerics;
using FileScape.Core.Model;

namespace FileScape.Core.Planets;

/// <summary>
/// A filesystem root's landmark (PLANET-45, AD-58): the Windows volume's pane gateway or the WSL distribution's terminal
/// monolith, standing near the arrival, facing it. It is solid (<see cref="Solids"/> are in the planet's
/// <see cref="PlanetSurface.Obstacles"/>) and it is a developer's entry point: walking up to it offers E, which opens a
/// small panel (open a terminal here, copy the path, useful places on that filesystem; the app's
/// <c>PlanetWorld.RootMark.cs</c>).
/// </summary>
/// <param name="Kind"><see cref="WorldKind.WindowsRoot"/> (the gateway) or <see cref="WorldKind.WslRoot"/> (the monolith).</param>
/// <param name="Direction">Unit direction of the mark's centre.</param>
/// <param name="Facing">Unit tangent at <paramref name="Direction"/> pointing back towards the arrival point: the mark's
/// front faces this way (the gateway's panes, the monolith's prompt).</param>
/// <param name="HalfWidthMetres">Half the mark's width across <paramref name="Facing"/>, metres (what the solids cover).</param>
/// <param name="Solids">The circles that make it solid, in a row across its face; their tops are out of a jump's reach.</param>
/// <param name="ClearanceMetres">How far the solids keep from the nearest route, launcher clear area, ring, landmark or
/// overlook beyond the margins the layout requires, metres (≥ 0 when it blocks nothing; exact up to
/// <see cref="RootMarkLayout.ComfortMetres"/>, a lower bound beyond; probes and tests report it).</param>
public sealed record PlanetRootMark(WorldKind Kind, Vector3 Direction, Vector3 Facing, float HalfWidthMetres, IReadOnlyList<SurfaceObstacle> Solids, float ClearanceMetres);

/// <summary>
/// Where a root's landmark stands and what makes it solid (PLANET-45, AD-58). Before 2026-11-16 the app stood it at a fixed
/// spot (4.6 m ahead, 3.6 m left) and it had no collider, so you walked through it; making it solid there would have put it
/// across the straight walks to launchers on a crowded root (an Ubuntu root has 21 pads, a spoke every ≈ 16°). So the
/// composer chooses its place: the spot nearest that original one whose footprint keeps clear of every walking route
/// (pole → each pad, the way back, the overlook and landmark), every launcher's clear area, the launcher rings, and the
/// landmark and overlook — first in the meadow's ring (inside the first launcher ring), then between rings.
/// <para>
/// Preserve: the footprint constants match the meshes in <c>PlanetArt.PaneGateway</c> / <c>TerminalMonolith</c> at the
/// app's 0.55 scale (change both together); placement is deterministic (no randomness, fixed search order); only root
/// worlds have a mark, so every other composition is unchanged (the behaviour baseline).
/// </para>
/// </summary>
public static class RootMarkLayout
{
    /// <summary>The original spot: this far ahead and to the left of the arrival, metres (it still stands here when nothing is in the way).</summary>
    public const float PreferredAheadMetres = 4.6f, PreferredLeftMetres = 3.6f;

    /// <summary>
    /// The gateway's half-width (pillars and lintel, 3.05 m × 0.55 scale ≈ 1.68 m) and the monolith's (its 3.4 m slab × 0.55
    /// ≈ 0.94 m), metres; the solids are circles of <see cref="SolidRadiusMetres"/> in a row covering it.
    /// </summary>
    public const float GatewayHalfWidthMetres = 1.65f, MonolithHalfWidthMetres = 0.94f;

    /// <summary>The footprint kept free of files and scenery round the mark's centre, metres: the gateway's and the
    /// monolith's round stone bases (4.0 m and 2.6 m × 0.55).</summary>
    public const float GatewayFootprintMetres = 2.2f, MonolithFootprintMetres = 1.45f;

    /// <summary>Each solid's radius, metres: a little more than half the face's depth (0.39 m gateway, 0.5 m monolith),
    /// so the row has no gap a walker (0.35 m) fits through.</summary>
    public const float SolidRadiusMetres = 0.32f;

    /// <summary>The solids' top, metres: above a jump and boost (2.7 m), so the mark cannot be climbed or stood on; the
    /// meshes stand ≈ 3.5–3.9 m tall.</summary>
    public const float SolidTopMetres = 3.6f;

    /// <summary>Clearance beyond the required margins that counts as fully clear, metres: a spot this clear is as good as
    /// any other, so the one nearest the original spot wins rather than the emptiest.</summary>
    public const float ComfortMetres = 0.75f;

    /// <summary>The bearing search step, degrees (360 candidates per distance; 7 solids × ≈ 30 routes each: ≈ 1 ms per distance on a crowded root).</summary>
    private const int BearingStepDeg = 1;

    /// <summary>Which mark a folder has: a WSL distribution's root the monolith, a Windows volume, drive or share the
    /// gateway; null for anything else — a root that is also a repository is a repository first (as in
    /// <see cref="WorldCharacter.Of"/>).</summary>
    public static WorldKind? KindOf(FsTree tree, FsNode folder)
    {
        if (WorldCharacter.RepositoryMarker(tree, folder) != null) return null;
        return folder.Location switch
        {
            LocationKind.Wsl => WorldKind.WslRoot,
            LocationKind.Drive or LocationKind.Volume or LocationKind.Network => WorldKind.WindowsRoot,
            _ => null,
        };
    }

    /// <summary>The mark's half-width across its face, metres.</summary>
    public static float HalfWidth(WorldKind kind) => kind == WorldKind.WslRoot ? MonolithHalfWidthMetres : GatewayHalfWidthMetres;

    /// <summary>The radius kept free of files and scenery round the mark's centre, metres.</summary>
    public static float Footprint(WorldKind kind) => kind == WorldKind.WslRoot ? MonolithFootprintMetres : GatewayFootprintMetres;

    /// <summary>A zone the mark's solids must keep out of: a circle (<paramref name="Radius"/> metres round a direction).</summary>
    internal readonly record struct Zone(Vector3 Direction, float Radius);

    /// <summary>
    /// Places the mark. <paramref name="routes"/> are walking routes as arcs (each solid keeps
    /// <paramref name="pathHalfWidth"/> from them), <paramref name="zones"/> circles to keep out of, <paramref name="rings"/>
    /// the launcher rings' surface distances from the arrival (walking routes too), <paramref name="meadowRadius"/> the
    /// arrival meadow's radius (metres). The original spot's distance is tried first, then each of
    /// <paramref name="outerDistances"/> (between rings, metres from the arrival), all bearings at each.
    /// </summary>
    internal static PlanetRootMark Place(WorldKind kind, float radius, IReadOnlyList<(Vector3 A, Vector3 B)> routes, IReadOnlyList<Zone> zones,
        IReadOnlyList<float> rings, float pathHalfWidth, float meadowRadius, IReadOnlyList<float> outerDistances)
    {
        var half = HalfWidth(kind);
        var preferredDistance = MathF.Sqrt(PreferredAheadMetres * PreferredAheadMetres + PreferredLeftMetres * PreferredLeftMetres);
        var preferredAz = MathF.Atan2(-PreferredLeftMetres, PreferredAheadMetres); // left is negative azimuth
        var distances = new List<float> { preferredDistance };
        distances.AddRange(outerDistances);
        // Odd, so one solid stands at the centre; spaced at most 1.5 radii apart, so the circles overlap.
        var reach = half - SolidRadiusMetres;
        var count = (int)MathF.Ceiling(2 * reach / (1.5f * SolidRadiusMetres)) + 1;
        if (count % 2 == 0) count++;
        var env = new Surroundings(radius, routes, zones, rings, pathHalfWidth, meadowRadius);
        foreach (var distance in distances)
        {
            (Vector3 Dir, Vector3 Facing, float Clear, float Cost)? best = null;
            for (var deg = -180; deg < 180; deg += BearingStepDeg)
            {
                var az = deg * MathF.PI / 180f;
                var dir = PlanetFrame.FromPolar(MathF.Min(distance / radius, MaxPolarRadians), az);
                var facing = PlanetFrame.North(dir); // towards the arrival pole
                // Cheap tests first (composition runs whenever a listing changes): the centre solid in the way rejects the
                // spot; a circle round the whole face (radius = the half-width) that is fully clear accepts it.
                if (env.Clear(dir, SolidRadiusMetres, stopBelow: 0) < 0) continue;
                var clear = env.Clear(dir, half, stopBelow: float.MinValue);
                if (clear < ComfortMetres)
                {
                    clear = float.MaxValue;
                    foreach (var s in Solids(dir, facing, half, count, radius)) clear = MathF.Min(clear, env.Clear(s.Direction, s.Radius, stopBelow: 0));
                    if (clear < 0) continue;
                }

                // Up to ComfortMetres, clearance is traded against turning away from the original bearing; beyond, only the bearing counts.
                var turn = MathF.Abs(MathF.IEEERemainder(az - preferredAz, 2 * MathF.PI));
                var cost = turn - MathF.Min(clear, ComfortMetres) * ClearanceWorthRadiansPerMetre;
                if (best is not { } b || cost < b.Cost - 1e-5f) best = (dir, facing, clear, cost);
            }

            // The first distance (meadow side first) with any spot clear of everything wins.
            if (best is { } found) return Make(kind, found.Dir, found.Facing, half, count, radius, found.Clear);
        }

        // Nowhere is clear (not seen on any tree tested): the least obstructive spot at the original distance;
        // ClearanceMetres says by how much it intrudes.
        (Vector3 Dir, Vector3 Facing, float Clear)? leastBad = null;
        for (var deg = -180; deg < 180; deg += BearingStepDeg)
        {
            var dir = PlanetFrame.FromPolar(MathF.Min(preferredDistance / radius, MaxPolarRadians), deg * MathF.PI / 180f);
            var facing = PlanetFrame.North(dir);
            var clear = Solids(dir, facing, half, count, radius).Min(s => env.Clear(s.Direction, s.Radius, float.MinValue));
            if (leastBad is not { } lb || clear > lb.Clear + 1e-5f) leastBad = (dir, facing, clear);
        }

        var (d, f, c) = leastBad!.Value;
        return Make(kind, d, f, half, count, radius, c);
    }

    /// <summary>Up to <see cref="ComfortMetres"/>, a metre of clearance is worth turning this far from the original bearing, radians (25 cm ≈ 57°).</summary>
    private const float ClearanceWorthRadiansPerMetre = 4f;

    /// <summary>On a small planet a between-rings distance can come close to the antipode; the mark stays within 162° of the pole.</summary>
    private const float MaxPolarRadians = MathF.PI * 0.9f;

    private static PlanetRootMark Make(WorldKind kind, Vector3 dir, Vector3 facing, float half, int count, float radius, float clear) =>
        new(kind, dir, facing, half, Solids(dir, facing, half, count, radius), clear);

    /// <summary>The row of solids across the face: <paramref name="count"/> circles from −half to +half (inset by a radius).</summary>
    private static List<SurfaceObstacle> Solids(Vector3 dir, Vector3 facing, float half, int count, float radius)
    {
        var across = Vector3.Normalize(Vector3.Cross(dir, facing));
        var list = new List<SurfaceObstacle>(count);
        var reach = half - SolidRadiusMetres;
        for (var i = 0; i < count; i++)
        {
            var offset = count == 1 ? 0 : -reach + 2 * reach * i / (count - 1);
            list.Add(new SurfaceObstacle(PlanetFrame.Move(dir, across, offset, radius), SolidRadiusMetres, SolidTopMetres));
        }

        return list;
    }

    /// <summary>
    /// What the mark must keep clear of: walking routes, zones, rings and the meadow (planet radius in metres). Clearances
    /// above <see cref="ComfortMetres"/> are never needed exactly (the cost caps them), so a route or zone that is
    /// surely farther than that is skipped by a dot product before anything is measured.
    /// </summary>
    private sealed class Surroundings
    {
        private readonly float _radius, _pathHalfWidth, _meadowRadius;
        private readonly (Vector3 A, Vector3 B, Vector3 Normal)[] _routes;
        private readonly Zone[] _zones;
        private readonly float[] _rings;

        public Surroundings(float radius, IReadOnlyList<(Vector3 A, Vector3 B)> routes, IReadOnlyList<Zone> zones, IReadOnlyList<float> rings, float pathHalfWidth, float meadowRadius)
        {
            _radius = radius;
            _pathHalfWidth = pathHalfWidth;
            _meadowRadius = meadowRadius;
            _routes = routes.Select(r =>
            {
                var n = Vector3.Cross(r.A, r.B);
                return (r.A, r.B, n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.Zero);
            }).ToArray();
            _zones = zones.ToArray();
            _rings = rings.ToArray();
        }

        /// <summary>
        /// The least clearance of a circle (<paramref name="r"/> metres round <paramref name="d"/>) from every route,
        /// zone, ring and the meadow, metres (negative: in the way), exact up to <see cref="ComfortMetres"/> and at least
        /// that otherwise; it returns as soon as it is below <paramref name="stopBelow"/>, so a rejection is cheap.
        /// </summary>
        public float Clear(Vector3 d, float r, float stopBelow)
        {
            var fromArrival = PlanetFrame.PolarOf(d) * _radius;
            var least = fromArrival - r - _meadowRadius;
            foreach (var ring in _rings) least = MathF.Min(least, MathF.Abs(fromArrival - ring) - r - _pathHalfWidth);
            if (least < stopBelow) return least;
            foreach (var z in _zones)
            {
                // Farther than the zone, the circle and the comfort margin put together: cannot lower the result.
                if (Vector3.Dot(d, z.Direction) < MathF.Cos(MathF.Min(MathF.PI, (z.Radius + r + ComfortMetres) / _radius))) continue;
                least = MathF.Min(least, PlanetShape.Angle(d, z.Direction) * _radius - r - z.Radius);
                if (least < stopBelow) return least;
            }

            var routeSin = MathF.Sin(MathF.Min(MathF.PI / 2, (_pathHalfWidth + r + ComfortMetres) / _radius));
            foreach (var (a, b, n) in _routes)
            {
                if (n != Vector3.Zero && MathF.Abs(Vector3.Dot(d, n)) >= routeSin) continue; // farther than that from the whole great circle
                least = MathF.Min(least, PlanetFrame.DistanceToArc(d, a, b, _radius) - r - _pathHalfWidth);
                if (least < stopBelow) return least;
            }

            return least;
        }
    }
}
