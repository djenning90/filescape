using System.Numerics;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Units;

namespace FileScape.Core.Places;

/// <summary>How the current folder changed (<see cref="PlaceChange.Move"/>): the app picks the arrival pose and the transition from it.</summary>
public enum PlaceMove
{
    /// <summary>Into a child of the current folder.</summary>
    Enter,

    /// <summary>To the parent of the current folder.</summary>
    Up,

    /// <summary>Directly to another folder (breadcrumb, map, destination by id).</summary>
    Jump,

    /// <summary>Back to the chosen root.</summary>
    Root,

    /// <summary>Back to the previously visited folder.</summary>
    Back,

    /// <summary>Through a link to its destination (AD-18): history remembers the connection used.</summary>
    Link,
}

/// <summary>
/// One change of the current folder. <see cref="ViaLink"/> is the link involved: for <see cref="PlaceMove.Link"/> the
/// link followed; for <see cref="PlaceMove.Back"/> the link through which the folder being left was entered, so the
/// return arrives in front of that link's gate (LINK-03).
/// </summary>
public sealed record PlaceChange(int From, int To, PlaceMove Move, int ViaLink = -1);

/// <summary>
/// Where the user *is* (PLACE-01): the current folder, the visit history and the arrival root. It is deliberately
/// separate from <see cref="Interaction.SelectionState"/> (what the user inspects): selecting something never moves
/// the user, and travelling does not have to change the selection. History (where you came from, including links)
/// is distinct from parenthood (Up always goes to the physical parent, LINK-03); the arrival root is a starting
/// point, not an edge (LINK-04).
/// </summary>
public sealed class PlaceNavigator
{
    /// <summary>
    /// The most visits Back remembers; the oldest is dropped beyond it, so a long session's history stays bounded
    /// (the value itself: rationale not recorded).
    /// </summary>
    private const int MaxHistory = 200;

    /// <summary>Visited folders, oldest first, each with the link it was entered through (−1 if none), for <see cref="Back"/>.</summary>
    private readonly List<(int Id, int Via)> _history = [];

    public int Root { get; private set; }
    public int Current { get; private set; }

    /// <summary>The link through which the current folder was entered, or -1.</summary>
    public int CurrentVia { get; private set; } = -1;

    public IReadOnlyList<int> History => _history.Select(h => h.Id).ToList();

    /// <summary>Raised after every successful move (never for a refused one); the app follows it to the new place.</summary>
    public event Action<PlaceChange>? Changed;

    /// <summary>Starts again at <paramref name="root"/> with no history (a new scan or location). Raises no event.</summary>
    public void Reset(int root = 0)
    {
        _history.Clear();
        Root = root;
        Current = root;
        CurrentVia = -1;
    }

    /// <summary>Enters a child folder of the current folder. Links and files cannot be entered.</summary>
    public bool Enter(FsTree tree, int childId)
    {
        if (!tree.Contains(childId) || !tree[childId].IsDirectory || tree[childId].ParentId != Current)
        {
            return false;
        }

        return Move(childId, PlaceMove.Enter);
    }

    /// <summary>Up to the physical parent — beyond the arrival root too, up to the top of the tree.</summary>
    public bool Up(FsTree tree)
    {
        if (!tree.Contains(Current) || tree[Current].IsRoot)
        {
            return false;
        }

        return Move(tree[Current].ParentId, PlaceMove.Up);
    }

    /// <summary>Arrives at a link's destination folder (resolved by the explorer); Back returns through the link.</summary>
    public bool FollowLink(FsTree tree, int linkId, int targetId)
    {
        if (!tree.Contains(targetId) || !tree.Contains(linkId))
        {
            return false;
        }

        if (!tree[targetId].IsDirectory)
        {
            targetId = tree[targetId].ParentId;
        }

        if (targetId == Current)
        {
            return false;
        }

        return Move(targetId, PlaceMove.Link, linkId);
    }

    /// <summary>Goes to any folder at or below the root. A file's id goes to its folder.</summary>
    public bool GoTo(FsTree tree, int id, PlaceMove move = PlaceMove.Jump)
    {
        if (!tree.Contains(id))
        {
            return false;
        }

        if (!tree[id].IsDirectory)
        {
            id = tree[id].ParentId;
        }

        return id >= 0 && id != Current && Move(id, move);
    }

    /// <summary>Back to the arrival root (recorded in history, so Back returns here).</summary>
    public bool GoToRoot() => Current != Root && Move(Root, PlaceMove.Root);

    /// <summary>
    /// Returns to the previously visited folder, popping it from history (Back is not itself recorded, so repeated Back
    /// walks further into the past). The change carries the link the folder being left was entered through.
    /// </summary>
    public bool Back()
    {
        if (_history.Count == 0)
        {
            return false;
        }

        var (previous, previousVia) = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        var from = Current;
        var via = CurrentVia;
        Current = previous;
        CurrentVia = previousVia;
        Changed?.Invoke(new PlaceChange(from, previous, PlaceMove.Back, via));
        return true;
    }

    /// <summary>
    /// Makes the last <paramref name="moves"/> moves one step of history (AD-56, PLANET-40): a multi-hop trip records a
    /// visit per hop as it flies, and when it ends (arrived or stopped) only the entry for where it started is kept, so
    /// Backspace returns to the start of the trip. The caller guarantees those moves were the trip's own (nothing else
    /// moved meanwhile); entries trimmed from the front by <see cref="MaxHistory"/> do not matter, only the newest are removed.
    /// </summary>
    public void MergeLastMoves(int moves)
    {
        for (var i = 1; i < moves && _history.Count > 1; i++)
        {
            _history.RemoveAt(_history.Count - 1);
        }
    }

    /// <summary>Records the current folder in history, moves to <paramref name="to"/> and raises <see cref="Changed"/>; false (and nothing changes) when already there.</summary>
    private bool Move(int to, PlaceMove move, int via = -1)
    {
        if (to == Current)
        {
            return false;
        }

        var from = Current;
        _history.Add((from, CurrentVia));
        if (_history.Count > MaxHistory)
        {
            _history.RemoveAt(0);
        }

        Current = to;
        CurrentVia = via;
        Changed?.Invoke(new PlaceChange(from, to, move, via));
        return true;
    }

    /// <summary>Folder ids from the root to <paramref name="id"/>, inclusive (the breadcrumb, PLACE-02).</summary>
    public static List<int> Breadcrumb(FsTree tree, int id)
    {
        var path = new List<int> { id };
        path.AddRange(tree.Ancestors(id));
        path.Reverse();
        return path;
    }

    /// <summary>
    /// The child of <paramref name="ancestor"/> on the path down to <paramref name="descendant"/>, or -1. Returning to
    /// an ancestor arrives in front of that child's gate, so the user sees where they came from (PLACE-07).
    /// </summary>
    public static int ChildToward(FsTree tree, int ancestor, int descendant)
    {
        var cur = descendant;
        while (cur >= 0 && tree[cur].ParentId != ancestor)
        {
            cur = tree[cur].ParentId;
        }

        return cur;
    }
}

/// <summary>
/// Detects deliberate travel through gates (PLACE-06). A gate counts only when the camera crosses its plane
/// outward, inside its width and below its lintel, while facing through it (stepping or flying backwards never
/// travels), after having been at least <see cref="ArmDepth"/> inside the plane. That hysteresis, plus a cooldown
/// after every arrival, means hovering on a boundary cannot switch places back and forth.
/// </summary>
public sealed class ThresholdTracker
{
    /// <summary>How far inside a gate's plane the camera must have been before crossing it counts (hysteresis).</summary>
    public static readonly Meters ArmDepth = Meters.Of(2f);

    /// <summary>No crossing counts this long after arriving, so standing on a boundary cannot flicker between places.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The minimum cosine (dimensionless) between the horizontal view direction and the gate's outward direction:
    /// 0.3 ≈ facing within 72° of straight through. Walking backwards through a gate never travels.
    /// </summary>
    public const float MinFacingCosine = 0.3f;

    /// <summary>Gates the camera has been at least <see cref="ArmDepth"/> inside of; a gate is disarmed when it is crossed.</summary>
    private readonly HashSet<Gate> _armed = [];

    private PlaceComposition? _place;

    /// <summary>The app-clock time from which crossings count again.</summary>
    private TimeSpan _readyAt;

    /// <summary>The previous camera position (world, metres), to detect a crossing between frames.</summary>
    private Vector3? _last;

    /// <summary>Starts watching a new place. Gates the camera is already well inside of are armed immediately.</summary>
    /// <param name="now">The app clock (time since start).</param>
    public void Reset(PlaceComposition place, Vector3 camera, TimeSpan now)
    {
        _place = place;
        _armed.Clear();
        _last = camera;
        _readyAt = now + Cooldown;
        Arm(camera);
    }

    /// <summary>Keeps the arming state but follows a recomposed place (the same folder, re-laid out during a scan).</summary>
    public void Follow(PlaceComposition place, Vector3 camera)
    {
        _place = place;
        _armed.Clear();
        _last = camera;
        Arm(camera);
    }

    /// <summary>
    /// Returns the gate crossed between the previous and this camera position, or null. <paramref name="facing"/> is
    /// the horizontal view direction; when given, a crossing counts only if the camera faces through the gate.
    /// </summary>
    public Gate? Update(Vector3 camera, TimeSpan now, Vector3? facing = null)
    {
        if (_place == null)
        {
            return null;
        }

        var last = _last ?? camera;
        _last = camera;
        Gate? crossed = null;
        if (now >= _readyAt)
        {
            foreach (var g in Gates(_place))
            {
                if (_armed.Contains(g) && Crosses(g, last, camera) && (facing is not { } f || Vector3.Dot(f, g.Outward) >= MinFacingCosine))
                {
                    crossed = g;
                    break;
                }
            }
        }

        Arm(camera);
        if (crossed != null)
        {
            _armed.Remove(crossed);
        }

        return crossed;
    }

    private void Arm(Vector3 camera)
    {
        foreach (var g in Gates(_place!))
        {
            if (Depth(g, camera) <= -ArmDepth.Value)
            {
                _armed.Add(g);
            }
        }
    }

    private static IEnumerable<Gate> Gates(PlaceComposition place) =>
        place.ParentGate is { } pg ? place.Gates.Append(pg) : place.Gates;

    /// <summary>Signed distance (metres) of a point from the gate plane; negative inside the place, positive beyond it.</summary>
    public static float Depth(Gate g, Vector3 point) => Vector3.Dot(point - g.Position, g.Outward);

    /// <summary>
    /// True when the segment <paramref name="from"/> → <paramref name="to"/> goes from inside the gate's plane to on or
    /// beyond it, and the crossing point is within the opening (half the width either side, below the lintel). Pure
    /// geometry: arming, the cooldown and facing are the caller's (<see cref="Update"/>, <see cref="PlaceRoutes"/>).
    /// </summary>
    public static bool Crosses(Gate g, Vector3 from, Vector3 to)
    {
        if (!g.CanEnter)
        {
            return false;
        }

        var d0 = Depth(g, from);
        var d1 = Depth(g, to);
        if (!(d0 < 0 && d1 >= 0))
        {
            return false;
        }

        var t = d0 / (d0 - d1); // where along the segment the plane is met (d0 < 0 ≤ d1, so 0 < t ≤ 1)
        var x = Vector3.Lerp(from, to, t);
        var lateral = MathF.Abs(Vector3.Dot(x - g.Position, g.Tangent));
        return lateral <= g.Width / 2 && x.Y <= g.Position.Y + g.Height;
    }
}

/// <summary>Ray picking against a place's structures (PLACE-01: picking selects, it never travels).</summary>
public static class PlacePicker
{
    // Pick boxes for vista parts that have no solid of their own in the composition (metres): a vista's landmark is
    // picked as a 2.8 m square column, a beacon as a 1.2 m one (rationale for the sizes not recorded).
    private const float VistaLandmarkPickWidth = 2.8f;
    private const float BeaconPickWidth = 1.2f;

    /// <summary>Returns the node id of the nearest structure hit, or -1. Landmark → the folder; gates and vistas → their folder.</summary>
    public static int Pick(PlaceComposition place, Vector3 origin, Vector3 direction) => PickWithDistance(place, origin, direction).Id;

    /// <summary>
    /// The nearest structure the ray hits and its distance along the (unit) <paramref name="direction"/>, or (−1, float.MaxValue):
    /// the landmark, files, gates and, in vistas, landmarks, towers and beacons.
    /// </summary>
    public static (int Id, float Distance) PickWithDistance(PlaceComposition place, Vector3 origin, Vector3 direction)
    {
        var best = -1;
        var bestT = float.MaxValue;
        void Test(int id, Box box)
        {
            if (box.Intersect(origin, direction) is { } t && t < bestT)
            {
                bestT = t;
                best = id;
            }
        }

        Test(place.FolderId, place.Landmark.Bounds);
        foreach (var f in place.Files) Test(f.Id, f.Box);
        foreach (var g in place.Gates) Test(g.TargetId, g.Bounds);
        if (place.ParentGate is { } pg) Test(pg.TargetId, pg.Bounds);
        foreach (var v in place.ParentVista is { } pv ? place.Vistas.Append(pv) : place.Vistas)
        {
            Test(v.FolderId, Box.FromBase(v.Center, VistaLandmarkPickWidth, v.LandmarkHeight, VistaLandmarkPickWidth));
            foreach (var t in v.Towers) Test(t.Id, Box.FromBase(t.BaseCenter, t.Footprint, t.Height, t.Footprint));
            foreach (var b in v.Beacons) Test(b.Id, Box.FromBase(b.Position, BeaconPickWidth, b.Height, BeaconPickWidth));
        }

        return (best, bestT);
    }

    /// <summary>The solid used to highlight a node in this place, if it is represented here.</summary>
    public static Box? BoundsOf(PlaceComposition place, int id)
    {
        if (id == place.FolderId) return place.Landmark.Bounds;
        foreach (var g in place.Gates) if (g.TargetId == id) return g.Bounds;
        if (place.ParentGate is { } pg && pg.TargetId == id) return pg.Bounds;
        foreach (var f in place.Files) if (f.Id == id) return f.Box;
        foreach (var v in place.ParentVista is { } pv ? place.Vistas.Append(pv) : place.Vistas)
        {
            foreach (var t in v.Towers) if (t.Id == id) return Box.FromBase(t.BaseCenter, t.Footprint, t.Height, t.Footprint);
            foreach (var b in v.Beacons) if (b.Id == id) return Box.FromBase(b.Position, BeaconPickWidth, b.Height, BeaconPickWidth);
        }

        return null;
    }
}
