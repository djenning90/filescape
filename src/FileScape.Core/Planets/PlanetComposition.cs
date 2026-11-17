using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Places;

namespace FileScape.Core.Planets;

// Everything here is in a planet's own frame (docs/units.md): the planet's centre is the origin, 1 unit = 1 metre,
// +Y is the arrival pole, and the arrival faces −Z. Directions are unit vectors from the centre; plain float lengths are
// metres (surface distances are measured at the base radius, as PlanetSurface defines them); angles are radians and say
// so in their names. Heights ("Top", "Height") are metres above the local ground.

/// <summary>Where and which way to stand on a planet: the unit direction of the feet and the unit tangent faced.</summary>
/// <param name="Up">Unit direction from the planet centre to the standing point.</param>
/// <param name="Facing">Unit tangent (perpendicular to <paramref name="Up"/>) the walker faces.</param>
public readonly record struct PlanetPose(Vector3 Up, Vector3 Facing);

/// <summary>The planet's landmark: a solid silhouette ahead of the arrival, seen over the horizon on arrival.</summary>
/// <param name="Direction">Unit direction of its axis.</param>
/// <param name="Radius">Footprint radius along the surface, metres.</param>
/// <param name="Top">Height above the ground, metres.</param>
public sealed record PlanetLandmark(Vector3 Direction, float Radius, float Top);

/// <summary>
/// The overlook: a raised round platform beside the landmark, reached by walking up a spiral of stepping stones or by a
/// jump and boost from the ground. Its payoff (shown by the app) is the labels of every destination; it is also the
/// planet's inspection station (nothing else stands on it).
/// </summary>
/// <param name="Direction">Unit direction of the platform's centre.</param>
/// <param name="Top">Platform height above the ground, metres.</param>
/// <param name="Radius">Platform radius along the surface, metres.</param>
/// <param name="Steps">The stepping stones, lowest first (each rises at most <see cref="PlanetParams.MaxStepRise"/>).</param>
public sealed record PlanetOverlook(Vector3 Direction, float Top, float Radius, IReadOnlyList<SurfaceObstacle> Steps)
{
    /// <summary>Where the first stepping stone stands: the foot of the walking route up.</summary>
    public Vector3 StepFoot => Steps.Count > 0 ? Steps[0].Direction : Direction;
}

/// <summary>
/// A bloom launcher: a flat standable pad on the surface. Standing on it and pressing Space travels to
/// <see cref="TargetId"/> (or, for <see cref="IsParent"/>, back up to the parent).
/// </summary>
/// <param name="TargetId">The child folder, directory link or (for <paramref name="IsParent"/>) the parent folder.</param>
/// <param name="Direction">Unit direction of the pad's centre.</param>
/// <param name="PadRadius">Pad radius along the surface, metres.</param>
/// <param name="PadTop">Pad height above the ground, metres (low enough to step onto).</param>
/// <param name="State">The target's listing state (<see cref="ListingState.Link"/> for a link).</param>
/// <param name="Name">The target's display name.</param>
/// <param name="SkyBody">Index into <see cref="PlanetComposition.SkyBodies"/> of the body hanging above it.</param>
/// <param name="IsParent">The way back up.</param>
/// <param name="IsLink">A directory link (junction, symlink, mount point).</param>
/// <param name="Followable">For a link: whether it can be followed (LinkInfo.CanFollow); folders are always true.</param>
/// <param name="LinkTargetId">For a resolved link: the folder it leads to in this tree, otherwise −1.</param>
/// <param name="Prominence">Why it stands out, if it does (PROM-01).</param>
public sealed record PlanetLauncher(
    int TargetId,
    Vector3 Direction,
    float PadRadius,
    float PadTop,
    ListingState State,
    string Name,
    int SkyBody,
    bool IsParent = false,
    bool IsLink = false,
    bool Followable = true,
    int LinkTargetId = -1,
    ProminenceInfo? Prominence = null)
{
    /// <summary>Folders can always be launched to; a link only when it can be followed (AD-18).</summary>
    public bool CanLaunch => !IsLink || Followable;
}

/// <summary>
/// A destination seen in the sky: a small planet hanging above its launcher, the parent above and behind the arrival, or
/// (<see cref="InBelt"/>) a tiny moon in the belt for child folders beyond the launcher budget.
/// </summary>
/// <param name="TargetId">The folder or link it stands for.</param>
/// <param name="Position">Centre, planet frame, metres.</param>
/// <param name="Radius">Radius, metres.</param>
/// <param name="Theme">Its theme guessed from its own listing when known, else <see cref="PlanetTheme.Veiled"/>.</param>
/// <param name="State">Its listing state.</param>
/// <param name="Launcher">Index into <see cref="PlanetComposition.Launchers"/> of its launcher, or −1 (belt).</param>
/// <param name="InBelt">A moon in the belt (no launcher of its own).</param>
/// <param name="IsParent">The parent planet.</param>
public sealed record SkyBody(int TargetId, Vector3 Position, float Radius, PlanetTheme Theme, ListingState State, int Launcher, bool InBelt = false, bool IsParent = false);

/// <summary>What a planet item is: a file, a file link, or a pile standing for the rest of a grove.</summary>
public enum PlanetItemKind
{
    File,
    Link,
    Pile,
}

/// <summary>
/// An approachable object on the surface (a file, a file link, or a grove's pile).
/// </summary>
/// <param name="NodeId">The file or link; −1 for a pile.</param>
/// <param name="Kind">File, link or pile.</param>
/// <param name="Category">Its file kind (for a pile: the grove's).</param>
/// <param name="Direction">Unit direction it stands at.</param>
/// <param name="HeadingRadians">Which way it faces: radians clockwise (seen from outside) from the local north, the tangent towards the arrival pole (see <see cref="PlanetFrame.Facing"/>).</param>
/// <param name="Scale">Size factor 0.6–1.6 from log10 of the file size (a visual encoding, not a physical size).</param>
/// <param name="Grove">Index into <see cref="PlanetComposition.Groves"/>.</param>
/// <param name="HiddenCount">For a pile: how many files of the grove it stands for; 0 otherwise.</param>
public sealed record PlanetItem(int NodeId, PlanetItemKind Kind, FileCategory Category, Vector3 Direction, float HeadingRadians, float Scale, int Grove, int HiddenCount = 0)
{
    /// <summary>Footprint radius of its solid along the surface, metres.</summary>
    public float Radius => PlanetComposer.ItemRadius(Scale);

    /// <summary>Height of its solid above the ground, metres.</summary>
    public float Top => PlanetComposer.ItemTop(Scale);

    /// <summary>The unit tangent it faces.</summary>
    public Vector3 Facing => PlanetFrame.Facing(Direction, HeadingRadians);
}

/// <summary>A grove: the files of one category, a contiguous patch of the surface.</summary>
/// <param name="Category">The file kind.</param>
/// <param name="CentreDirection">Unit direction of the patch's centre.</param>
/// <param name="Count">Files of this kind shown by the planet (individually or in the pile).</param>
/// <param name="Shown">Files placed individually.</param>
/// <param name="Label">For example "Images · 214".</param>
public sealed record PlanetGrove(FileCategory Category, Vector3 CentreDirection, int Count, int Shown, string Label)
{
    /// <summary>Files represented by the grove's pile (0 when every file stands on its own).</summary>
    public int Hidden => Count - Shown;
}

/// <summary>Scenery kinds; the theme decides the mix.</summary>
public enum DecorationKind
{
    Tree,
    Rock,
    Flower,
    Grass,
    Mushroom,
    Crystal,
    Lamp,
}

/// <summary>A piece of scenery. Colliding decorations have a solid in <see cref="PlanetSurface.Obstacles"/>.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Direction">Unit direction it stands at.</param>
/// <param name="Scale">Size factor (dimensionless, ≈ 0.7–1.4).</param>
/// <param name="Seed">Per-piece variation for the renderer.</param>
/// <param name="Collides">Whether it is solid (tree trunks, lamps, crystals, rocks).</param>
public sealed record PlanetDecoration(DecorationKind Kind, Vector3 Direction, float Scale, int Seed, bool Collides);

/// <summary>A label for the app to render: a grove's or a launcher's.</summary>
/// <param name="Direction">Unit direction it stands over.</param>
/// <param name="Height">Height above the ground, metres.</param>
/// <param name="Text">The main line (a target name, a grove label).</param>
/// <param name="Detail">The second line (FolderSummary.Gate for a launcher).</param>
/// <param name="TargetId">The launcher's target, or −1 for a grove sign.</param>
public sealed record PlanetSign(Vector3 Direction, float Height, string Text, string Detail, int TargetId = -1);

/// <summary>A walking route drawn on the ground, from the arrival to a launcher or to the overlook's first step.</summary>
/// <param name="TargetId">The launcher's target, or −1 for the overlook.</param>
/// <param name="Points">Unit directions; consecutive points are joined by great-circle arcs.</param>
public sealed record PlanetPath(int TargetId, IReadOnlyList<Vector3> Points);

/// <summary>The folder's direct entries, for the HUD.</summary>
/// <param name="Folders">Visible child folders.</param>
/// <param name="Files">Visible files (file links included).</param>
/// <param name="Links">Visible links (directory and file links).</param>
/// <param name="Hidden">Entries hidden by the visibility filter.</param>
public readonly record struct PlanetChildCounts(int Folders, int Files, int Links, int Hidden);

/// <summary>
/// The full description of one folder's planet, independent of any renderer (planet frame; see the file header).
/// Made by <see cref="PlanetComposer.Compose"/>; the app draws it (<c>PlanetVisual</c>), walks it through
/// <see cref="Surface"/> (<see cref="SphereWalker"/>), and recomposes when <see cref="Signature"/> changes, swapping the
/// surface under the walker (<see cref="SphereWalker.ChangeSurface"/>) so a listing arriving never interrupts a walk.
/// <para>
/// It is not changed once made (the app replaces it whole). The index fields (<see cref="PlanetLauncher.SkyBody"/>, <see cref="SkyBody.Launcher"/>, <see cref="PlanetItem.Grove"/>)
/// point into this composition's own lists and mean nothing across compositions; node ids are the stable references.
/// </para>
/// </summary>
public sealed class PlanetComposition
{
    /// <summary>The folder this planet stands for.</summary>
    public required int FolderId { get; init; }

    /// <summary>The folder's display name.</summary>
    public required string Name { get; init; }

    public required PlanetTheme Theme { get; init; }

    /// <summary>The ground: radius, hills and flat spots (the same as <see cref="Surface"/>'s).</summary>
    public required PlanetShape Shape { get; init; }

    /// <summary>The walkable world: <see cref="Shape"/> and every colliding solid.</summary>
    public required PlanetSurface Surface { get; init; }

    public required ListingState State { get; init; }

    /// <summary>FolderSummary.Place: what is here and in the subtree (two lines).</summary>
    public required string Summary { get; init; }

    /// <summary>The folder was listed only in part.</summary>
    public bool IsPartial => State == ListingState.Partial;

    /// <summary>The folder's contents are unknown or incomplete (not to be read as empty).</summary>
    public bool IsIncomplete => Listing.IsUnknown(State);

    /// <summary>Something is not shown individually: a pile, belt moons or omitted folders.</summary>
    public bool IsTruncated => Groves.Any(g => g.Hidden > 0) || SkyBodies.Any(b => b.InBelt) || OmittedFolders > 0;

    /// <summary>The default arrival: at the +Y pole facing −Z.</summary>
    public required PlanetPose Arrival { get; init; }

    /// <summary>Flat radius of the arrival meadow, metres.</summary>
    public required float ArrivalRadius { get; init; }

    public required PlanetLandmark Landmark { get; init; }
    public required PlanetOverlook Overlook { get; init; }

    /// <summary>The inspection station: at the overlook (its centre); nothing else stands within <see cref="InspectionRadius"/>.</summary>
    public Vector3 OverlookDirection => Overlook.Direction;

    /// <summary>Radius of the item-free area of the inspection station, metres.</summary>
    public float InspectionRadius => Overlook.Radius;

    /// <summary>Child launchers in azimuth order, then the parent launcher (if any) last.</summary>
    public required IReadOnlyList<PlanetLauncher> Launchers { get; init; }

    /// <summary>Bodies in the sky: one per launcher, then the belt moons.</summary>
    public required IReadOnlyList<SkyBody> SkyBodies { get; init; }

    /// <summary>Child folders and links shown neither as a launcher nor in the belt.</summary>
    public required int OmittedFolders { get; init; }

    public required IReadOnlyList<PlanetItem> Items { get; init; }

    /// <summary>Largest first; <see cref="PlanetItem.Grove"/> indexes this list.</summary>
    public required IReadOnlyList<PlanetGrove> Groves { get; init; }

    public required IReadOnlyList<PlanetDecoration> Decorations { get; init; }
    public required IReadOnlyList<PlanetSign> Signs { get; init; }
    public required IReadOnlyList<PlanetPath> Paths { get; init; }
    public required PlanetChildCounts Counts { get; init; }

    /// <summary>
    /// Why this is a hazard world (PLANET-34, AD-53; <see cref="HazardRules"/>), or null for an ordinary one. A hazard
    /// world's <see cref="Surface"/> carries its broken shell (<see cref="PlanetSurface.Shell"/>).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public HazardReason? Hazard { get; init; }

    /// <summary>
    /// A filesystem root's landmark (PLANET-45, AD-58): the Windows gateway or the WSL monolith, solid and placed clear of
    /// the walking routes; null on every other world. Its solids are among the <see cref="Surface"/>'s obstacles.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PlanetRootMark? RootMark { get; init; }

    /// <summary>The tree version this was made from.</summary>
    public required int TreeVersion { get; init; }

    /// <summary>See <see cref="PlanetComposer.Signature"/>.</summary>
    public required string Signature { get; init; }

    public PlanetLauncher? ParentLauncher => Launchers.FirstOrDefault(l => l.IsParent);

    /// <summary>The launcher for a child folder or link (or, through a resolved link, for the folder it leads to).</summary>
    public PlanetLauncher? LauncherFor(int nodeId) =>
        Launchers.FirstOrDefault(l => !l.IsParent && l.TargetId == nodeId) ?? Launchers.FirstOrDefault(l => !l.IsParent && l.LinkTargetId == nodeId && nodeId >= 0);

    /// <summary>
    /// Where to arrive when coming from <paramref name="fromNodeId"/>: back from a child (or through its link), beside that
    /// child's launcher on the side towards the arrival meadow, facing away from it; otherwise the default arrival.
    /// </summary>
    public PlanetPose ArrivalFor(int fromNodeId)
    {
        if (LauncherFor(fromNodeId) is not { } l) return Arrival;
        var towardPole = PlanetFrame.North(l.Direction);
        var up = PlanetFrame.Move(l.Direction, towardPole, l.PadRadius + PlanetComposer.ReturnStandOff, Shape.Radius);
        var away = up - l.Direction;
        var facing = Vector3.Normalize(away - up * Vector3.Dot(away, up));
        return new PlanetPose(up, facing);
    }
}

/// <summary>
/// Direction and tangent helpers for a planet's frame (+Y = the arrival pole). Positions on a planet are unit directions
/// from its centre, so "moving" is rotating along great circles and "north" is towards the arrival pole: the composer
/// lays out with these, and the app's labels and probes read the layout back with them. Azimuth is clockwise seen from
/// above the pole, 0 straight ahead (−Z), matching the place angle convention of docs/units.md (radians here).
/// </summary>
public static class PlanetFrame
{
    /// <summary>
    /// The local north at <paramref name="up"/>: the unit tangent pointing towards the arrival pole (+Y). At the poles
    /// (where that is undefined) it is −Z, the arrival's facing.
    /// </summary>
    public static Vector3 North(Vector3 up)
    {
        var t = Vector3.UnitY - up * up.Y;
        if (t.LengthSquared() < 1e-8f) // within ≈ 0.006° of a pole
        {
            t = -Vector3.UnitZ - up * Vector3.Dot(-Vector3.UnitZ, up);
        }

        return Vector3.Normalize(t);
    }

    /// <summary>The local east at <paramref name="up"/>: north × up (so north, east, up have the place frame's handedness).</summary>
    public static Vector3 East(Vector3 up) => Vector3.Normalize(Vector3.Cross(North(up), up));

    /// <summary>The unit tangent for a heading: radians clockwise (seen from outside) from <see cref="North"/>.</summary>
    public static Vector3 Facing(Vector3 up, float headingRadians) =>
        Vector3.Normalize(North(up) * MathF.Cos(headingRadians) + East(up) * MathF.Sin(headingRadians));

    /// <summary>The heading (radians, as <see cref="Facing"/>) of a tangent at <paramref name="up"/>.</summary>
    public static float HeadingOf(Vector3 up, Vector3 tangent) => MathF.Atan2(Vector3.Dot(tangent, East(up)), Vector3.Dot(tangent, North(up)));

    /// <summary>
    /// The direction from the pole: <paramref name="polarRadians"/> from +Y and <paramref name="azimuthRadians"/> clockwise
    /// (seen from above the pole) from −Z, the arrival's facing (so azimuth 0 is straight ahead, π/2 to the right, +X).
    /// </summary>
    public static Vector3 FromPolar(float polarRadians, float azimuthRadians)
    {
        var s = MathF.Sin(polarRadians);
        return new Vector3(s * MathF.Sin(azimuthRadians), MathF.Cos(polarRadians), -s * MathF.Cos(azimuthRadians));
    }

    /// <summary>The azimuth of a direction (radians, as <see cref="FromPolar"/>; 0 for the poles).</summary>
    public static float AzimuthOf(Vector3 up) => MathF.Atan2(up.X, -up.Z);

    /// <summary>The angle of a direction from the arrival pole, radians.</summary>
    public static float PolarOf(Vector3 up) => PlanetShape.Angle(up, Vector3.UnitY);

    /// <summary>
    /// Walks <paramref name="metres"/> along the surface (at <paramref name="radius"/>) from <paramref name="up"/> in the
    /// direction of a tangent, along the great circle.
    /// </summary>
    public static Vector3 Move(Vector3 up, Vector3 tangent, float metres, float radius)
    {
        var t = tangent - up * Vector3.Dot(tangent, up);
        if (t.LengthSquared() < 1e-12f) return up;
        t = Vector3.Normalize(t);
        var a = metres / radius;
        return Vector3.Normalize(up * MathF.Cos(a) + t * MathF.Sin(a));
    }

    /// <summary>The surface distance (metres, at <paramref name="radius"/>) from <paramref name="p"/> to the great-circle arc a→b.</summary>
    /// <remarks>
    /// Projects p onto the arc's great-circle plane: if the projection lies between a and b the distance is the angle to
    /// the plane (asin |p·n|), otherwise the nearer end. Degenerate arcs (a ≈ b or antipodal) and points at the plane's
    /// poles fall back to the ends.
    /// </remarks>
    public static float DistanceToArc(Vector3 p, Vector3 a, Vector3 b, float radius)
    {
        var n = Vector3.Cross(a, b);
        var nl = n.Length();
        var toEnds = MathF.Min(PlanetShape.Angle(p, a), PlanetShape.Angle(p, b));
        if (nl < 1e-6f) return toEnds * radius;
        n /= nl;
        var off = Vector3.Dot(p, n);
        var q = p - n * off;
        if (q.LengthSquared() < 1e-12f) return toEnds * radius;
        q = Vector3.Normalize(q);
        var ab = PlanetShape.Angle(a, b);
        var inside = PlanetShape.Angle(a, q) + PlanetShape.Angle(q, b) <= ab + 1e-4f;
        return (inside ? MathF.Min(MathF.Asin(Math.Clamp(MathF.Abs(off), 0, 1)), toEnds) : toEnds) * radius;
    }
}
