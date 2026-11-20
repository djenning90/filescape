using System.Numerics;
using FileScape.Core.Model;

namespace FileScape.Core.Planets;

/// <summary>
/// Where you stood on a planet and how the camera looked, kept while a legacy view (Places, the maps) is shown, so V or
/// Tab back to the planets puts you there again (PLANET-112, AD-82). DJ (sixth pass): "Preserve position, camera, and
/// relevant movement state when switching from Planets to a legacy view and back." Before, every activation arrived
/// afresh at the arrival meadow with the establishing shot.
/// <para>
/// Kept: the folder, the tree it belongs to (by reference: a rescan is a new tree and new ids), the shared selection at
/// the time, the traveller's up (its place on the sphere) and facing, and the camera's heading, chosen pitch and distance.
/// Not kept: velocity, a jump or a fall in progress, travel, the overview (closed by the switch) — you come back standing.
/// <see cref="Decide"/> says whether it still applies: only to the same tree and folder and the same selection (a
/// selection made in the legacy view of something on this world is honoured instead: you arrive beside it, as before).
/// Engine-free (<c>OverviewCameraTests</c>); <c>PlanetWorld.cs</c> captures it in <c>Deactivate</c> and applies it in
/// <c>Activate</c>. Vectors are in the planet's frame (its centre at the origin), unit length.
/// </para>
/// </summary>
public sealed record SurfaceResume(FsTree Tree, int FolderId, int Selected, Vector3 Up, Vector3 Facing, Vector3 CameraForward, float CameraPitchDegrees, float CameraDistanceMetres)
{
    /// <summary>The arrival framing's weight at the time (0…1; 1 just after landing, PLANET-67), so a view framed for the
    /// arrival comes back framed.</summary>
    public float ArrivalFraming { get; init; }

    /// <summary>Whether you had moved since arriving (the framing was easing out).</summary>
    public bool ArrivalReleased { get; init; } = true;

    /// <summary>Whether the planets should resume here, and if not, why (logged; probes read it).</summary>
    public static SurfaceResumeDecision Decide(SurfaceResume? saved, FsTree? tree, int folderId, int selected) =>
        saved == null ? SurfaceResumeDecision.NothingSaved
        : tree == null || !ReferenceEquals(tree, saved.Tree) ? SurfaceResumeDecision.NewTree
        : folderId != saved.FolderId ? SurfaceResumeDecision.MovedElsewhere
        // Clearing the selection elsewhere is not a new wish; choosing something else is (you arrive beside it).
        : selected >= 0 && selected != saved.Selected ? SurfaceResumeDecision.SelectionChanged
        : SurfaceResumeDecision.Resume;

    /// <summary>Whether the moment allows capturing: walking on the ground, nothing travelling (else you arrive as before).</summary>
    public static bool MayCapture(bool walking, bool grounded, bool travelling) => walking && grounded && !travelling;
}

/// <summary>The outcome of <see cref="SurfaceResume.Decide"/>.</summary>
public enum SurfaceResumeDecision
{
    /// <summary>Same tree, folder and selection: stand where you stood, the camera as it was.</summary>
    Resume,

    /// <summary>Nothing was kept (the first activation, or you were travelling or in the air when you left).</summary>
    NothingSaved,

    /// <summary>A new tree (Open…, a rescan): node ids start again.</summary>
    NewTree,

    /// <summary>The legacy view went somewhere else: arrive there as usual.</summary>
    MovedElsewhere,

    /// <summary>Something else was selected meanwhile: arrive beside it (or at the arrival) as usual.</summary>
    SelectionChanged,
}
