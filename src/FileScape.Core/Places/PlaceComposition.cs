using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Units;

namespace FileScape.Core.Places;

/// <summary>
/// What is known about a folder's listing, as shown on its gate and in its place (PLACE-11). "Unknown" states
/// (pending, not scanned, denied, failed, cloud) must never look like <see cref="Empty"/>.
/// </summary>
public enum ListingState
{
    /// <summary>Listed completely and has entries.</summary>
    Open,

    /// <summary>Listed completely and has no entries at all.</summary>
    Empty,

    /// <summary>Queued: the scan has not reached it yet.</summary>
    Pending,

    /// <summary>Listed in part (cancellation, node limit, or an error part-way).</summary>
    Partial,

    /// <summary>Not listed: the scan was cancelled or stopped at the node limit before reaching it.</summary>
    NotScanned,

    /// <summary>Windows refused to list it.</summary>
    Denied,

    /// <summary>It vanished or listing failed with another error.</summary>
    Failed,

    /// <summary>A cloud placeholder directory that is deliberately not listed (FS-13).</summary>
    Cloud,

    /// <summary>A directory link (junction, symlink, mount point): a connection to elsewhere, followed only on purpose (AD-18).</summary>
    Link,

    /// <summary>Known to exist, not listed yet: it is listed when visited (on-demand exploration, AD-17).</summary>
    Unlisted,

    /// <summary>Its volume, share or distribution could not be reached.</summary>
    Unavailable,

    /// <summary>Being listed right now (LIST-03); <see cref="Pending"/> is queued.</summary>
    Listing,
}

public static class Listing
{
    public static ListingState Of(FsNode n)
    {
        if (n.Kind == NodeKind.Link) return ListingState.Link;
        if ((n.Flags & NodeFlags.Unavailable) != 0 && n.Children.Count == 0) return ListingState.Unavailable;
        if ((n.Flags & NodeFlags.AccessDenied) != 0) return ListingState.Denied;
        if ((n.Flags & (NodeFlags.Vanished | NodeFlags.EnumerationError)) != 0 && n.Children.Count == 0) return ListingState.Failed;
        if ((n.Flags & NodeFlags.CloudNotTraversed) != 0) return ListingState.Cloud;
        return n.ScanState switch
        {
            DirScanState.Pending => ListingState.Pending,
            DirScanState.Listing => ListingState.Listing,
            DirScanState.Cancelled or DirScanState.NotTraversed => ListingState.NotScanned,
            DirScanState.Failed => ListingState.Failed,
            DirScanState.Unlisted => ListingState.Unlisted,
            _ when (n.Flags & (NodeFlags.Incomplete | NodeFlags.Vanished | NodeFlags.EnumerationError)) != 0 => ListingState.Partial,
            _ => n.Children.Count == 0 ? ListingState.Empty : ListingState.Open,
        };
    }

    /// <summary>True when the folder's contents are unknown or incomplete (as opposed to known to be empty).</summary>
    public static bool IsUnknown(ListingState s) => s is not (ListingState.Open or ListingState.Empty or ListingState.Link);

    public static string Describe(ListingState s) => s switch
    {
        ListingState.Open => "",
        ListingState.Empty => "empty",
        ListingState.Pending => "queued",
        ListingState.Listing => "listing now…",
        ListingState.Partial => "partly listed",
        ListingState.NotScanned => "not scanned",
        ListingState.Denied => "access denied",
        ListingState.Failed => "listing failed",
        ListingState.Cloud => "cloud — not listed",
        ListingState.Link => "link",
        ListingState.Unlisted => "not listed yet",
        ListingState.Unavailable => "unavailable",
        _ => "",
    };
}

/// <summary>A file structure in the place. <see cref="Box"/> is its solid (also used for picking).</summary>
public sealed record PlacedFile(int Id, Box Box, FileCategory Category, bool Core = false);

/// <summary>A labelled type quarter: files of one category share an angular wedge of the near half (PLACE-03).</summary>
public sealed record FileQuarter(FileCategory Category, int Count, float StartDeg, float EndDeg, Vector3 SignPosition);

/// <summary>
/// A gate: the entrance to a child folder (or, for <see cref="IsParent"/>, the way back up). <see cref="AngleDeg"/>
/// is the outward direction measured clockwise from north (-Z): 0 = north, 90 = east (+X), 180 = south (+Z).
/// Crossing the gate plane outward, below <see cref="Height"/>, is travel (PLACE-06).
/// </summary>
public sealed record Gate(
    int TargetId,
    Vector3 Position,
    float AngleDeg,
    float Width,
    float Height,
    int Tier,
    ListingState State,
    int ChainLength,
    Box Bounds,
    bool IsParent = false,
    bool Followable = true)
{
    public Vector3 Outward => PlaceGeometry.Direction(AngleDeg);
    public Vector3 Tangent => new(-Outward.Z, 0, Outward.X);

    /// <summary>Folders can always be entered; a link gate only when its link can be followed (AD-18, LINK-06).</summary>
    public bool CanEnter => State != ListingState.Link || Followable;

    public bool IsLink => State == ListingState.Link;
}

/// <summary>An alphabetical tier of gates in a wide folder (PLACE-05).</summary>
public sealed record GateTier(int Index, float Radius, float Rise, int Count, string FirstName, string LastName, float StartDeg, float EndDeg);

/// <summary>A tower in a vista skyline: one of the previewed folder's largest files, at its true mapped height.</summary>
public sealed record VistaTower(int Id, Vector3 BaseCenter, float Footprint, float Height, FileCategory Category);

/// <summary>A beacon in a vista: one of the previewed folder's own child folders ("hints of what follows").</summary>
public sealed record VistaBeacon(int Id, Vector3 Position, float Height, ListingState State);

/// <summary>
/// A preview of a neighbouring place seen beyond its gate (PLACE-09): its landmark, a skyline of its largest files
/// and beacons for its children. It is a simplified preview at a symbolic distance, not the place itself (AD-14).
/// </summary>
public sealed record Vista(
    int FolderId,
    Vector3 Center,
    float Radius,
    PlaceIdentity Identity,
    float LandmarkHeight,
    ListingState State,
    IReadOnlyList<VistaTower> Towers,
    IReadOnlyList<VistaBeacon> Beacons,
    int HiddenBeacons,
    int FileCount,
    int FolderCount);

/// <summary>A route drawn on the ground: from the landmark to a gate and on to its vista.</summary>
public sealed record Avenue(int TargetId, Vector3[] Points);

public sealed record Landmark(Vector3 Base, float Height, float Radius, PlaceIdentity Identity)
{
    public Box Bounds => Box.FromBase(Base, Radius * 2, Height, Radius * 2);
}

/// <summary>
/// The overlook (JUMP-02): a platform at <see cref="Top"/> (its centre, at floor height) facing the place centre, with a
/// walking ramp starting at <see cref="RampFoot"/> and jump blocks as an optional shortcut.
/// </summary>
public sealed record Overlook(Vector3 Top, float AngleDeg, float HalfSize, Vector2 RampFoot, IReadOnlyList<Vector2> JumpBlocks);

/// <summary>
/// A camera pose: the eye in world space (metres), the yaw (CameraMath convention: 0 looks north, −Z; positive turns
/// left) and the look pitch (negative looks down).
/// </summary>
public readonly record struct PlacePose(Vector3 Eye, Degrees Yaw, Degrees LookPitch);

/// <summary>
/// The full description of one folder's place, independent of any renderer. Positions are in the place's own frame:
/// the landmark is at the origin, the arrival point south of it, child gates on the northern arc.
/// </summary>
public sealed class PlaceComposition
{
    public required int FolderId { get; init; }
    public required ListingState State { get; init; }
    public required Landmark Landmark { get; init; }
    public required PlacePose Arrival { get; init; }
    public required IReadOnlyList<PlacedFile> Files { get; init; }
    public required IReadOnlyList<FileQuarter> Quarters { get; init; }
    public required IReadOnlyList<Gate> Gates { get; init; }
    public required IReadOnlyList<GateTier> Tiers { get; init; }
    public required Gate? ParentGate { get; init; }
    public required IReadOnlyList<Vista> Vistas { get; init; }
    public required Vista? ParentVista { get; init; }
    public required IReadOnlyList<Avenue> Avenues { get; init; }

    /// <summary>The radius of the plaza floor (covers the files, the first gate ring and the parent gate).</summary>
    public required float PlazaRadius { get; init; }

    /// <summary>The radius reached by the file quarters (0 when there are no files).</summary>
    public required float FileRadius { get; init; }

    public required Box Bounds { get; init; }

    /// <summary>The walkable ground (AD-16): supports, solids and the boundary.</summary>
    public required PlaceTerrain Terrain { get; init; }

    /// <summary>The overlook platform with the place's summary (JUMP-02).</summary>
    public required Overlook Overlook { get; init; }

    /// <summary>Entries of this folder not shown because of the visibility filter (VISI-04).</summary>
    public int HiddenCount { get; init; }

    /// <summary>The place's identity (AD-20): hub, location root, project or ordinary folder, with its evidence.</summary>
    public PlaceKindInfo Kind { get; init; } = PlaceKindInfo.Ordinary;

    /// <summary>The tree version this composition was made from.</summary>
    public required int TreeVersion { get; init; }

    /// <summary>The local-neighbourhood signature it was made from (see <see cref="PlaceComposer.Signature"/>).</summary>
    public required long Signature { get; init; }

    public bool IsWide => Tiers.Count > 1;

    public Gate? GateFor(int childId) => Gates.FirstOrDefault(g => g.TargetId == childId);

    /// <summary>Where to stand when coming back up from a child: in front of its gate, facing into this place (PLACE-07).</summary>
    public PlacePose ReturnPose(int childId, PlaceParams p)
    {
        var gate = GateFor(childId);
        return gate == null ? Arrival : Standing(gate.Position - gate.Outward * p.ReturnStandOff, gate.AngleDeg + 180, Degrees.Of(-4), p);
    }

    /// <summary>Where to stand to look at a gate before going through it (destination list, PLACE-06).</summary>
    public PlacePose ApproachPose(int childId, PlaceParams p)
    {
        var gate = GateFor(childId);
        return gate == null ? Arrival : Standing(gate.Position - gate.Outward * p.ApproachStandOff, gate.AngleDeg, Degrees.Of(-3), p);
    }

    /// <summary>A pose standing on the ground at a point (eye 1.75 m above the support there, WALK-01).</summary>
    public PlacePose Standing(Vector3 at, float angleFromNorthDeg, Degrees lookPitch, PlaceParams p)
    {
        var ground = Terrain.SupportHeight(new Vector2(at.X, at.Z), 0.3f, 0, 0.45f);
        return PlaceGeometry.FacingPose(at with { Y = ground }, angleFromNorthDeg, p.EyeHeight, lookPitch);
    }
}

/// <summary>A folder's deterministic identity: accent hue and landmark form, derived from its name (PLACE-14).</summary>
public readonly record struct PlaceIdentity(float Hue, int Form)
{
    public const int Forms = 3;

    /// <summary>FNV-1a over the case-folded name. string.GetHashCode is randomised per process, so it cannot be used.</summary>
    public static PlaceIdentity For(string name)
    {
        var h = 2166136261u;
        foreach (var ch in name.ToUpperInvariant())
        {
            h ^= ch;
            h *= 16777619u;
        }

        // A futuristic palette: hues are drawn from cyan → violet → magenta → amber bands, avoiding muddy greens.
        float[] bands = [0.50f, 0.56f, 0.62f, 0.72f, 0.80f, 0.88f, 0.95f, 0.08f, 0.13f];
        var hue = bands[(int)(h % (uint)bands.Length)] + ((h >> 8) % 100) / 100f * 0.035f;
        return new PlaceIdentity(hue - MathF.Floor(hue), (int)((h >> 16) % Forms));
    }
}

public static class PlaceGeometry
{
    /// <summary>The horizontal unit vector for an angle measured clockwise from north (-Z).</summary>
    public static Vector3 Direction(float angleDeg)
    {
        var a = angleDeg * MathF.PI / 180f;
        return new Vector3(MathF.Sin(a), 0, -MathF.Cos(a));
    }

    /// <summary>The camera yaw (CameraMath convention: 0 looks -Z, positive turns left) that looks along an angle.</summary>
    /// <summary>
    /// Converts a place angle (clockwise from north, as gates use) to a camera yaw (counter-clockwise, CameraMath): the
    /// two conventions differ only in sign. This is the one place where they meet.
    /// </summary>
    public static Degrees YawFor(float angleFromNorthDeg) => Degrees.Of(-angleFromNorthDeg);

    /// <summary>A pose standing at <paramref name="ground"/> (world, metres), looking along a place angle.</summary>
    public static PlacePose FacingPose(Vector3 ground, float angleFromNorthDeg, Meters eyeHeight, Degrees lookPitch) =>
        new(ground with { Y = ground.Y + eyeHeight.Value }, YawFor(angleFromNorthDeg), lookPitch);

    /// <summary>The axis-aligned bounds of a w×d×h block centred at <paramref name="baseCenter"/>, rotated to face <paramref name="angleDeg"/>.</summary>
    public static Box RotatedBounds(Vector3 baseCenter, float angleDeg, float width, float depth, float height)
    {
        var o = Direction(angleDeg);
        var t = new Vector3(-o.Z, 0, o.X);
        var ex = MathF.Abs(t.X) * width / 2 + MathF.Abs(o.X) * depth / 2;
        var ez = MathF.Abs(t.Z) * width / 2 + MathF.Abs(o.Z) * depth / 2;
        return new Box(new Vector3(baseCenter.X - ex, baseCenter.Y, baseCenter.Z - ez), new Vector3(baseCenter.X + ex, baseCenter.Y + height, baseCenter.Z + ez));
    }
}
