using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;

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

    /// <summary>A directory link (junction, symlink, mount point). Shown, never followed (FS-03).</summary>
    Link,
}

public static class Listing
{
    public static ListingState Of(FsNode n)
    {
        if (n.Kind == NodeKind.Link) return ListingState.Link;
        if ((n.Flags & NodeFlags.AccessDenied) != 0) return ListingState.Denied;
        if ((n.Flags & (NodeFlags.Vanished | NodeFlags.EnumerationError)) != 0 && n.Children.Count == 0) return ListingState.Failed;
        if ((n.Flags & NodeFlags.CloudNotTraversed) != 0) return ListingState.Cloud;
        return n.ScanState switch
        {
            DirScanState.Pending => ListingState.Pending,
            DirScanState.Cancelled or DirScanState.NotTraversed => ListingState.NotScanned,
            DirScanState.Failed => ListingState.Failed,
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
        ListingState.Pending => "scanning…",
        ListingState.Partial => "partly listed",
        ListingState.NotScanned => "not scanned",
        ListingState.Denied => "access denied",
        ListingState.Failed => "listing failed",
        ListingState.Cloud => "cloud — not listed",
        ListingState.Link => "link — not followed",
        _ => "",
    };
}

/// <summary>A file structure in the place. <see cref="Box"/> is its solid (also used for picking).</summary>
public sealed record PlacedFile(int Id, Box Box, FileCategory Category);

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
    bool IsParent = false)
{
    public Vector3 Outward => PlaceGeometry.Direction(AngleDeg);
    public Vector3 Tangent => new(-Outward.Z, 0, Outward.X);
    public bool CanEnter => State != ListingState.Link;
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

/// <summary>A camera pose: eye position, yaw (0 looks north, -Z) and look pitch (negative looks down), in degrees.</summary>
public readonly record struct PlacePose(Vector3 Eye, float Yaw, float LookPitch);

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
        return gate == null ? Arrival : PlaceGeometry.FacingPose(gate.Position - gate.Outward * p.ReturnStandOff, gate.AngleDeg + 180, p.EyeHeight + Rise(gate), -4);
    }

    /// <summary>Where to stand to look at a gate before going through it (destination list, PLACE-06).</summary>
    public PlacePose ApproachPose(int childId, PlaceParams p)
    {
        var gate = GateFor(childId);
        return gate == null ? Arrival : PlaceGeometry.FacingPose(gate.Position - gate.Outward * p.ApproachStandOff, gate.AngleDeg, p.EyeHeight + Rise(gate), -3);
    }

    private float Rise(Gate g) => g.Tier >= 0 && g.Tier < Tiers.Count ? Tiers[g.Tier].Rise : 0;
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
    public static float YawFor(float angleDeg) => -angleDeg;

    public static PlacePose FacingPose(Vector3 ground, float angleDeg, float eyeHeight, float lookPitch) =>
        new(ground with { Y = ground.Y + eyeHeight }, YawFor(angleDeg), lookPitch);

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
