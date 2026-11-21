using System.Numerics;
using FileScape.Core.Model;

namespace FileScape.Core.Planets;

/// <summary>
/// Where you stood on a planet and how the camera looked, kept while a legacy view (Places, the maps) is shown, so coming
/// back to the planets puts you there again (PLANET-112, AD-82; PLANET-144..145, AD-92). DJ (sixth pass): "Preserve
/// position, camera, and relevant movement state when switching from Planets to a legacy view and back"; (seventh pass)
/// "Restore the previous world, standing position, and camera when returning from a legacy view. Handle an unsafe saved
/// position with a nearby safe placement."
/// <para>
/// Kept: the folder, the tree it belongs to (by reference: a rescan is a new tree and new ids), the shared selection at
/// the time, the traveller's up (its place on the sphere) and facing, what was under the feet, whether the overview was
/// open, and the camera's heading, chosen pitch and distance. Not kept: velocity, a jump or a fall in progress, travel:
/// you come back standing. Since AD-92 a Hazard World, the air (a jump, a twirl, a fall off its rim) and the overview are
/// kept as well: only a departure, a hop or a trip in progress is not (<see cref="MayCapture"/>).
/// </para>
/// <para>
/// <see cref="Decide"/> says whether it still applies (only to the same tree and folder and the same selection: a
/// selection made in the legacy view of something on this world is honoured instead, you arrive beside it, as before; a
/// world removed meanwhile is a new tree, and you arrive wherever the navigator is).
/// <see cref="Place"/> then says where to stand: where you were if that is safe ground; else the nearest safe ground
/// within <see cref="SafeStanding.SearchMetres"/> (over a gap or the rim of a Hazard World, mid-air above one, or where
/// something now stands); else the arrival meadow.
/// </para>
/// Engine-free (<c>OverviewCameraTests</c>, <c>SurfaceResumeTests</c>); <c>PlanetWorld.cs</c> captures it in
/// <c>Deactivate</c> and applies it in <c>Activate</c>. Vectors are in the planet's frame (its centre at the origin), unit length.
/// </summary>
public sealed record SurfaceResume(FsTree Tree, int FolderId, int Selected, Vector3 Up, Vector3 Facing, Vector3 CameraForward, float CameraPitchDegrees, float CameraDistanceMetres)
{
    /// <summary>The arrival framing's weight at the time (0…1; 1 just after landing, PLANET-67), so a view framed for the
    /// arrival comes back framed.</summary>
    public float ArrivalFraming { get; init; }

    /// <summary>Whether you had moved since arriving (the framing was easing out).</summary>
    public bool ArrivalReleased { get; init; } = true;

    /// <summary>The height of what was under the traveller above the local ground, metres (0 the ground; a platform's or
    /// a step's top), as the walker last found it: if something taller stands there when you come back, it is new.</summary>
    public float SupportMetres { get; init; }

    /// <summary>The overview was open when the legacy view was chosen: it opens again over where you stood.</summary>
    public bool InOverview { get; init; }

    /// <summary>Whether the planets should resume here, and if not, why (logged; probes read it).</summary>
    public static SurfaceResumeDecision Decide(SurfaceResume? saved, FsTree? tree, int folderId, int selected) =>
        saved == null ? SurfaceResumeDecision.NothingSaved
        : tree == null || !ReferenceEquals(tree, saved.Tree) ? SurfaceResumeDecision.NewTree
        : folderId != saved.FolderId ? SurfaceResumeDecision.MovedElsewhere
        // Clearing the selection elsewhere is not a new wish; choosing something else is (you arrive beside it).
        : selected >= 0 && selected != saved.Selected ? SurfaceResumeDecision.SelectionChanged
        : SurfaceResumeDecision.Resume;

    /// <summary>
    /// Whether the moment allows capturing: anything but a departure, a hop or a trip in progress (they are on their way
    /// somewhere: you arrive there as before). On the ground, in the air, on a Hazard World or under the overview, it is
    /// kept; <see cref="Place"/> makes an unsafe spot safe.
    /// </summary>
    public static bool MayCapture(bool travelling) => !travelling;

    /// <summary>
    /// Where to stand when <paramref name="saved"/> resumes on <paramref name="surface"/> (the world as composed now): see
    /// the class summary. <paramref name="arrival"/> is the world's arrival meadow, the last resort.
    /// </summary>
    public static ResumePlacement Place(SurfaceResume saved, PlanetSurface surface, Vector3 arrival, float bodyRadiusMetres)
    {
        var why = SafeStanding.WhyUnsafe(surface, saved.Up, bodyRadiusMetres, saved.SupportMetres);
        if (why.Length == 0) return new ResumePlacement(ResumeStand.AsSaved, Vector3.Normalize(saved.Up), 0, "safe where you stood");
        if (SafeStanding.Nearest(surface, saved.Up, bodyRadiusMetres) is { } near)
        {
            return new ResumePlacement(ResumeStand.Nearby, near, surface.SurfaceDistance(Vector3.Normalize(saved.Up), near), why);
        }

        var meadow = Vector3.Normalize(arrival);
        return new ResumePlacement(ResumeStand.Arrival, meadow, surface.SurfaceDistance(Vector3.Normalize(saved.Up), meadow), why + "; no safe ground near");
    }
}

/// <summary>How <see cref="SurfaceResume.Place"/> stood you: as saved, moved to safe ground nearby, or at the arrival.</summary>
public enum ResumeStand
{
    AsSaved,
    Nearby,
    Arrival,
}

/// <summary>Where a resume stands the traveller (<see cref="Up"/>, unit, planet frame), how far that is from where it
/// stood (metres along the surface) and why it moved (logged; probes read it).</summary>
public readonly record struct ResumePlacement(ResumeStand Stand, Vector3 Up, float MovedMetres, string Why);

/// <summary>
/// Safe ground to stand a traveller on without an arrival (PLANET-145, AD-92): solid (not over a Hazard World's gap),
/// clear of its rim, with nothing new standing there (the walker would be put on its top). Where you stood counts as
/// safe a body's width from the rim (<see cref="StandClearMetres"/>: the narrowest bridge's centre line is); a new spot
/// found for you keeps well inside (<see cref="PlaceClearMetres"/>). Used by <see cref="SurfaceResume.Place"/>;
/// engine-free and deterministic (the search order is fixed: nearest ring first, then by bearing from the local tangent).
/// Preserve: on a whole planet with nothing new where you stood, the answer is always "safe" (resume changes nothing);
/// the search never returns ground nearer the rim than <see cref="PlaceClearMetres"/>.
/// </summary>
public static class SafeStanding
{
    /// <summary>Where you stood is safe this far inside the rim, metres: more than the body's radius (0.35 m), less than half
    /// the narrowest bridge (<see cref="HazardTerrain.MinBridgeWidthMetres"/> 1.8 m), so standing on a bridge resumes there.</summary>
    public const float StandClearMetres = 0.6f;

    /// <summary>A spot found for you keeps this far inside the rim, metres: the body (0.35 m) plus the terrain's own edge
    /// margin (<see cref="HazardTerrain.EdgeMarginMetres"/>, 0.8 m), rounded up, so you are never put on the crumbling edge
    /// or in the edge warning's loudest part (narrow bridges are passed over for the island beside them).</summary>
    public const float PlaceClearMetres = 1.2f;

    /// <summary>What now stands under you may be this much taller than what did, metres, and still be the same ground (a
    /// step's or a relief's difference; a file's tower is taller).</summary>
    public const float NewSupportToleranceMetres = 0.3f;

    /// <summary>The search for nearby safe ground goes this far, metres along the surface (a Hazard World's islands are
    /// a few metres across; beyond this the arrival meadow is as near in feel).</summary>
    public const float SearchMetres = 24f;

    /// <summary>Spacing of the search's rings and of the points on each ring, metres (finer than the body's width).</summary>
    public const float SearchStepMetres = 0.5f;

    /// <summary>Whether standing at <paramref name="up"/> is safe (see the class summary): <paramref name="supportBefore"/>
    /// is what stood under you there before, metres (0 for open ground); <paramref name="clearMetres"/> the rim's clearance.</summary>
    public static bool IsSafe(PlanetSurface surface, Vector3 up, float bodyRadiusMetres, float supportBefore, float clearMetres = StandClearMetres) =>
        WhyUnsafe(surface, up, bodyRadiusMetres, supportBefore, clearMetres).Length == 0;

    /// <summary>Why <paramref name="up"/> is not safe, or "" when it is.</summary>
    public static string WhyUnsafe(PlanetSurface surface, Vector3 up, float bodyRadiusMetres, float supportBefore, float clearMetres = StandClearMetres)
    {
        var u = Vector3.Normalize(up);
        if (surface.Shell is { } shell)
        {
            if (shell.OverHole(u)) return "over open space";
            if (shell.EdgeMetres(u) < clearMetres) return "at the rim";
        }

        var support = surface.SupportAt(u, bodyRadiusMetres, float.MaxValue, 0);
        return support > supportBefore + NewSupportToleranceMetres ? "something stands there now" : "";
    }

    /// <summary>The nearest safe open ground to <paramref name="up"/> within <see cref="SearchMetres"/> (clear of the rim by
    /// <see cref="PlaceClearMetres"/>), or null.</summary>
    public static Vector3? Nearest(PlanetSurface surface, Vector3 up, float bodyRadiusMetres)
    {
        var u = Vector3.Normalize(up);
        var (a, b) = PlanetShape.Tangents(u);
        for (var ring = 1; ring * SearchStepMetres <= SearchMetres; ring++)
        {
            var metres = ring * SearchStepMetres;
            var angle = metres / surface.Radius;
            var points = Math.Max(8, (int)MathF.Ceiling(2 * MathF.PI * metres / SearchStepMetres));
            for (var i = 0; i < points; i++)
            {
                var bearing = 2 * MathF.PI * i / points;
                var tangent = a * MathF.Cos(bearing) + b * MathF.Sin(bearing);
                var candidate = Vector3.Normalize(u * MathF.Cos(angle) + tangent * MathF.Sin(angle));
                if (IsSafe(surface, candidate, bodyRadiusMetres, 0, PlaceClearMetres)) return candidate;
            }
        }

        return null;
    }
}

/// <summary>The outcome of <see cref="SurfaceResume.Decide"/>.</summary>
public enum SurfaceResumeDecision
{
    /// <summary>Same tree, folder and selection: stand where you stood, the camera as it was.</summary>
    Resume,

    /// <summary>Nothing was kept (the first activation, or you were on your way somewhere when you left).</summary>
    NothingSaved,

    /// <summary>A new tree (Open…, a rescan): node ids start again.</summary>
    NewTree,

    /// <summary>The legacy view went somewhere else: arrive there as usual.</summary>
    MovedElsewhere,

    /// <summary>Something else was selected meanwhile: arrive beside it (or at the arrival) as usual.</summary>
    SelectionChanged,
}
