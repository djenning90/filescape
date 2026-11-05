using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Model;

namespace FileScape.Core.Places;

/// <summary>
/// A folder place's position in the aerial world (AERIAL-02): a point on the world plane (x, z in metres; north = −Z,
/// as in a place) and the direction its own "north" points — away from its parent, so in its local frame the ▲ gate
/// is south and its child folders lie to the north, as in the place itself.
/// </summary>
/// <param name="Id">The folder's node id.</param>
/// <param name="Center">World position of the place's centre (its landmark), metres; Vector2.Y is world Z.</param>
/// <param name="NorthDeg">The place angle (clockwise from world north) that is "north" inside this place.</param>
/// <param name="Radius">The place's estimated footprint radius, metres (its gates and file quarters).</param>
/// <param name="SubtreeRadius">The radius of the disc that holds this place and everything laid out below it.</param>
/// <param name="ParentId">The folder it is laid out from (−1 for the world's centre).</param>
/// <param name="Depth">Steps from the world's centre.</param>
/// <param name="HiddenChildren">Direct child folders not laid out because the world reached its size limit.</param>
public sealed record WorldIsland(int Id, Vector2 Center, float NorthDeg, float Radius, float SubtreeRadius, int ParentId, int Depth, int HiddenChildren);

/// <summary>Parameters of the aerial world layout (metres and degrees). See <see cref="WorldLayout"/>.</summary>
public sealed record WorldLayoutParams
{
    /// <summary>Gap kept between neighbouring subtrees, metres.</summary>
    public float Gap { get; init; } = 24f;

    /// <summary>Children of a place spread over at most this arc around its north (the centre uses the full circle).</summary>
    public float ChildArcDeg { get; init; } = 160f;

    /// <summary>More children than this are set in several rows (nearer and farther), like a wide place's tiers.</summary>
    public int ChildrenPerRow { get; init; } = 14;

    /// <summary>
    /// Subtree radii are rounded up to the next step of this ratio, so a subtree that grows a little (a deeper folder
    /// listed) keeps its size and its siblings do not move (stability, AERIAL-02).
    /// </summary>
    public float RadiusStep { get; init; } = 1.25f;

    /// <summary>Places laid out at most: the explored namespace of a long session, or a bounded tree's folders. Beyond it,
    /// the deepest folders are counted on their parent (HiddenChildren) rather than drawn (a stated limit, AERIAL-05).</summary>
    public int MaxIslands { get; init; } = 4000;

    /// <summary>Footprint of a folder whose contents are not known yet (a small, veiled island).</summary>
    public float UnknownRadius { get; init; } = 22f;

    public static readonly WorldLayoutParams Default = new();
}

/// <summary>The aerial world: every laid-out place, its link connections, and what was left out.</summary>
public sealed class WorldMap
{
    public required IReadOnlyDictionary<int, WorldIsland> Islands { get; init; }

    /// <summary>Links between places: from the place holding the link to the place it leads to (both laid out).</summary>
    public required IReadOnlyList<(int LinkId, int From, int To)> LinkEdges { get; init; }

    /// <summary>The centre of the world (the hub, or the scanned root).</summary>
    public required int RootId { get; init; }

    /// <summary>Folders known but not laid out because of <see cref="WorldLayoutParams.MaxIslands"/>.</summary>
    public required int Truncated { get; init; }

    public required int TreeVersion { get; init; }

    public bool Contains(int id) => Islands.ContainsKey(id);

    /// <summary>The frame of a place: its world centre and north (AERIAL-02, docs/units.md "World and place frames").</summary>
    public PlaceFrame FrameOf(int id) => Islands.TryGetValue(id, out var i) ? new PlaceFrame(i.Center, i.NorthDeg) : PlaceFrame.Identity;

    /// <summary>The island whose footprint contains a world point (the nearest centre wins), or −1.</summary>
    public int IslandAt(Vector2 world, float slack = 1f)
    {
        var best = -1;
        var bestD = float.MaxValue;
        foreach (var i in Islands.Values)
        {
            var d = Vector2.Distance(world, i.Center);
            if (d <= i.Radius * slack && d < bestD)
            {
                bestD = d;
                best = i.Id;
            }
        }

        return best;
    }
}

/// <summary>
/// A place's frame in the aerial world (AERIAL-02): where its origin is and which way its north points. Inside a place
/// everything is in local coordinates (landmark at the origin, north = −Z, docs/units.md); the aerial world is drawn in
/// the current place's local coordinates (a floating origin), so the place you are in never moves and precision stays
/// good far from the world's centre. This is the boundary a planetoid world would replace with a curved surface frame.
/// </summary>
public readonly record struct PlaceFrame(Vector2 Center, float NorthDeg)
{
    public static readonly PlaceFrame Identity = new(Vector2.Zero, 0);

    private Vector2 North => Flat(PlaceGeometry.Direction(NorthDeg));
    private Vector2 East => Flat(PlaceGeometry.Direction(NorthDeg + 90));

    /// <summary>World plane point → local (x, z).</summary>
    public Vector2 ToLocal(Vector2 world)
    {
        var d = world - Center;
        return new Vector2(Vector2.Dot(d, East), -Vector2.Dot(d, North));
    }

    /// <summary>Local (x, z) → world plane point.</summary>
    public Vector2 ToWorld(Vector2 local) => Center + East * local.X + North * -local.Y;

    /// <summary>A world place angle (clockwise from world north) as seen in this frame.</summary>
    public float AngleToLocal(float worldDeg) => worldDeg - NorthDeg;

    public float AngleToWorld(float localDeg) => localDeg + NorthDeg;

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);
}

/// <summary>
/// Lays out the known folders as an aerial world (AERIAL-02, AD-26): a tree layout on the plane in which every folder is
/// an island and its child folders sit ahead of it (its north, away from its parent) across a 160° arc, in the same
/// natural A→Z order as its gates (west → east), each child's whole subtree in a disc inside its own angular slot, so
/// nothing overlaps. The centre of the world is the tree's root (This computer, or a scanned root), children all around.
/// <para>
/// Deterministic for a given tree. Stable while discovery happens: a child's angle depends only on how many siblings it
/// has, and its distance only on its own subtree (radii rounded up in <see cref="WorldLayoutParams.RadiusStep"/> steps),
/// so listing a folder deeper down moves at most that folder's ancestors along their own direction, never their siblings
/// (except later rows of a wide folder). A folder that gains children re-spaces its own children. Distances are symbolic.
/// Pure and engine-free; O(n) in the folders laid out.
/// </para>
/// </summary>
public static class WorldLayout
{
    public static WorldMap Compute(FsTree tree, Func<FsNode, bool>? include = null, WorldLayoutParams? parameters = null)
    {
        var p = parameters ?? WorldLayoutParams.Default;
        include ??= _ => true;
        var root = tree.Root.Id;
        // Breadth-first: the nearest levels first, so a size limit drops the deepest folders.
        var kids = new Dictionary<int, List<int>>();
        var order = new List<int> { root };
        var hidden = new Dictionary<int, int>();
        var depth = new Dictionary<int, int> { [root] = 0 };
        var truncated = 0;
        for (var qi = 0; qi < order.Count; qi++)
        {
            var id = order[qi];
            var list = new List<int>();
            foreach (var c in tree[id].Children)
            {
                var n = tree[c];
                if (!n.IsDirectory || !include(n)) continue;
                if (order.Count >= p.MaxIslands)
                {
                    truncated += 1 + n.SubtreeDirCount; // it and the folders known below it
                    hidden[id] = hidden.GetValueOrDefault(id) + 1;
                    continue;
                }

                list.Add(c);
                order.Add(c);
                depth[c] = depth[id] + 1;
            }

            list.Sort((a, b) =>
            {
                var r = NaturalNameOrder.Instance.Compare(tree[a].Name, tree[b].Name);
                return r != 0 ? r : a.CompareTo(b);
            });
            kids[id] = list;
        }

        // Post-order: each place's own radius, where each child goes (angle and distance) and its subtree disc.
        // Each child has its own angular slot (equal shares of the arc, in name order) and its own distance, set by its
        // own subtree: a child's disc always fits its slot's cone, so siblings never overlap, and a subtree growing
        // deeper down moves only itself (never its siblings). Many children are set in rows (like tiers), so a wide
        // folder's places stay near it.
        var own = new Dictionary<int, float>(order.Count);
        var subtree = new Dictionary<int, float>(order.Count);
        var slots = new Dictionary<int, (float AngleOffsetDeg, float Distance)[]>(order.Count);
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var id = order[i];
            var r = FootprintRadius(tree[id], kids[id].Count, p);
            own[id] = r;
            var children = kids[id];
            if (children.Count == 0)
            {
                subtree[id] = Round(r, p);
                continue;
            }

            var full = id == root;
            var arc = full ? 360f : p.ChildArcDeg;
            var rows = children.Count <= p.ChildrenPerRow ? 1 : (int)MathF.Ceiling(children.Count / (float)p.ChildrenPerRow);
            var perRow = (int)MathF.Ceiling(children.Count / (float)rows);
            var slot = arc / perRow;
            var half = MathF.Min(slot, 179f) * 0.5f * MathF.PI / 180f;
            var placed = new (float, float)[children.Count];
            var rowBase = r + p.Gap;
            var extent = r;
            for (var row = 0; row < rows; row++)
            {
                var rowFar = rowBase;
                for (var k = row; k < children.Count; k += rows)
                {
                    var rc = subtree[children[k]];
                    var col = k / rows;
                    // Out of the parent's own footprint (or the previous row), and far enough that the disc fits the cone.
                    var dist = MathF.Max(rowBase + rc, (rc + p.Gap / 2) / MathF.Sin(half));
                    var offset = full ? (col + 0.5f) * slot - 180f : (col + 0.5f) * slot - arc / 2;
                    placed[k] = (offset, dist);
                    rowFar = MathF.Max(rowFar, dist + rc);
                }

                rowBase = rowFar + p.Gap;
                extent = MathF.Max(extent, rowFar);
            }

            slots[id] = placed;
            subtree[id] = Round(extent, p);
        }

        // Pre-order: positions and norths (west → east = clockwise = left to right when facing a place's north).
        var islands = new Dictionary<int, WorldIsland>(order.Count)
        {
            [root] = new WorldIsland(root, Vector2.Zero, 0, own[root], subtree[root], -1, 0, hidden.GetValueOrDefault(root)),
        };
        foreach (var id in order)
        {
            var children = kids[id];
            if (children.Count == 0) continue;
            var self = islands[id];
            var placed = slots[id];
            for (var k = 0; k < children.Count; k++)
            {
                var (offset, dist) = placed[k];
                var a = self.NorthDeg + offset;
                var dir = PlaceGeometry.Direction(a);
                var c = children[k];
                islands[c] = new WorldIsland(c, self.Center + new Vector2(dir.X, dir.Z) * dist, a, own[c], subtree[c], id, depth[c], hidden.GetValueOrDefault(c));
            }
        }

        var links = new List<(int, int, int)>();
        foreach (var id in order)
        {
            foreach (var c in tree[id].Children)
            {
                var n = tree[c];
                if (n.Kind == NodeKind.Link && n.Link is { TargetId: >= 0 } li && islands.ContainsKey(li.TargetId) && li.TargetId != id) links.Add((c, id, li.TargetId));
            }
        }

        return new WorldMap { Islands = islands, LinkEdges = links, RootId = root, Truncated = truncated, TreeVersion = tree.Version };
    }

    /// <summary>
    /// A place's footprint radius estimated from its counts (metres), consistent with <see cref="PlaceComposer"/>: the
    /// gate ring (≥ 24 m, wider with more gates, tiers beyond 24) and the file quarters (≈ 2.4 m cells). Not exact — the
    /// aerial world needs a stable size without composing every place.
    /// </summary>
    public static float FootprintRadius(FsNode n, int childPlaces, WorldLayoutParams p)
    {
        var state = Listing.Of(n);
        if (Listing.IsUnknown(state) && state != ListingState.Partial && n.Children.Count == 0) return p.UnknownRadius;
        var gates = Math.Max(childPlaces, n.DirectDirCount + n.DirectLinkCount);
        var gateRing = gates <= 24 ? MathF.Max(24f, gates * 2.9f) : 40f + 13f * (MathF.Ceiling(gates / 32f) - 1);
        var files = n.DirectFileCount;
        var fileRing = 8f + MathF.Sqrt(files * 5.76f / 2.1f);
        return MathF.Max(30f, MathF.Max(gateRing + 8f, fileRing + 4f));
    }

    private static float Round(float r, WorldLayoutParams p)
    {
        const float unit = 30f;
        if (r <= unit) return unit;
        var steps = MathF.Ceiling(MathF.Log(r / unit) / MathF.Log(p.RadiusStep));
        return unit * MathF.Pow(p.RadiusStep, steps);
    }
}
