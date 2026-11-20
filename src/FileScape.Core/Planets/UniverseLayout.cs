using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;

namespace FileScape.Core.Planets;

/// <summary>How much of a folder is known: the overview shows discovered and undiscovered parts differently.</summary>
public enum Discovery
{
    /// <summary>Its entries are listed (completely).</summary>
    Listed,

    /// <summary>Partly listed, or being listed now.</summary>
    Partial,

    /// <summary>Known only by name: its contents are not listed yet (or could not be).</summary>
    Unknown,
}

/// <summary>One world in the overview: a folder (or the hub), where it hangs, its size and how much of it is known.</summary>
public sealed record UniverseBody(int NodeId, int ParentId, Vector3 Position, float Radius, int Depth, Discovery Discovery, int HiddenChildren, Vector3 OrbitNormal, float OrbitRadius);

/// <summary>
/// A place the player's attention is on, for the overview's budget (PLANET-101, AD-81): a folder and how far its
/// attention is discounted, in attention units (<see cref="UniverseLayout.Attention"/>): 0 for where you stand and the
/// selected world, a little more for the approached world, more again for worlds you looked at before.
/// </summary>
public readonly record struct AttentionFocus(int Id, float Offset);

/// <summary>The room a system was given in an earlier layout, metres: its ring's radius and its whole extent (PLANET-105).</summary>
public readonly record struct Reserve(float Ring, float Extent);

/// <summary>
/// The overview of the known folder hierarchy in 3D (PLANET-19, AD-46, 2026-11-11; "Universe Sandbox–style"): every known
/// folder is a world; a folder's subfolders orbit it on one tilted ring, so the hierarchy reads as nested star systems.
/// <para>
/// It runs in three passes. (1) Breadth-first selection within the budget: at most <see cref="MaxBodies"/> worlds and
/// <see cref="MaxChildren"/> per system, near the root first; pinned folders (the way to where you stand) always make it
/// in, and the rest are counted as <see cref="UniverseBody.HiddenChildren"/>. (2) Bottom-up sizes: each body's radius
/// from its subtree size, each ring's radius just large enough that its children's whole systems never overlap, and
/// each system's extent (ring + its widest child system). (3) Top-down placement: children round their parent's ring,
/// each taking an arc proportional to its extent, on a ring tilted by an angle derived from the folder's id.
/// </para>
/// <para>
/// Everything is deterministic: the same tree gives the same picture every time (the overview is a map you learn),
/// with no randomness — the "random-looking" tilts and starting angles all come from <see cref="Hash"/> of the folder id.
/// Only what the tree already knows is laid out: nothing is listed for the overview. The app
/// (<c>PlanetWorld.Universe.cs</c>) draws the bodies and lets you fly round them.
/// </para>
/// Changing any constant here changes every saved mental map of the overview; the layout tests check the invariants
/// (no overlaps, children on their parent's ring in its plane), not the exact picture.
/// </summary>
public static class UniverseLayout
{
    /// <summary>The most worlds the overview shows (draw cost and legibility); the rest are counted, not drawn.</summary>
    public const int MaxBodies = 1500;

    /// <summary>The most children one system shows: a ring of more is unreadable; extra children are counted per system.</summary>
    public const int MaxChildren = 64;

    /// <summary>Gap between neighbouring systems on a ring, metres (overview units).</summary>
    private const float Gap = 2.5f;

    // ---- body size: radius = MinBodyRadius + BodyRadiusPerDecade × log10(1 + entries below it)
    /// <summary>The radius of an empty folder's world, metres: still big enough to see and click.</summary>
    private const float MinBodyRadius = 1.2f;

    /// <summary>How much a world grows per tenfold more entries below it, metres (a log scale: big trees stay comparable).</summary>
    private const float BodyRadiusPerDecade = 0.8f;

    // ---- the per-folder hash: a fixed "random" number per id, so tilts and angles look varied yet never change
    /// <summary>
    /// Knuth's multiplicative-hash constant (a prime close to 2³² / φ ≈ 2654435769.5): multiplying the id by it scatters neighbouring ids (5, 6, 7…)
    /// across the whole 32-bit range, so sibling systems get unrelated tilts and starting angles.
    /// </summary>
    private const uint KnuthMultiplier = 2654435761u;

    /// <summary>Tilt axis directions: the hash picks one of this many steps round the horizontal circle (628 ≈ 2π × 100).</summary>
    private const uint TiltAxisSteps = 628;

    /// <summary>Steps per radian for <see cref="TiltAxisSteps"/>: steps 0…627 become angles 0…6.27 rad (just short of a full turn).</summary>
    private const float TiltAxisStepsPerRadian = 100f;

    /// <summary>The smallest ring tilt, degrees: enough that no system looks flat-on from the default view.</summary>
    private const float MinTiltDegrees = 12f;

    /// <summary>Whole-degree tilt values added to <see cref="MinTiltDegrees"/>: tilts range 12°…34° (12 + 0…22).</summary>
    private const uint TiltDegreeSteps = 23;

    /// <summary>
    /// The reference axis for building a ring's in-plane axes is X unless the ring's normal is within ≈ 26° of X
    /// (|n.X| ≥ 0.9 = cos 25.8°): the cross product of two nearly parallel vectors is tiny and its direction unstable.
    /// </summary>
    private const float NearlyParallelToX = 0.9f;

    /// <summary>Starting angle of a system's first child: whole degrees 0…359 from the hash.</summary>
    private const uint StartAngleDegreeSteps = 360;

    /// <summary>The per-folder hash (see <see cref="KnuthMultiplier"/>). Note the arithmetic: an int times a uint is a long
    /// in C#, and the cast keeps its low 32 bits, so negative ids would wrap the same way every time.</summary>
    private static uint Hash(int id) => (uint)(id * KnuthMultiplier);

    /// <param name="pinned">Folders that are always shown whatever the budget (the way to where you stand).</param>
    /// <param name="attention">Null: the budget goes breadth-first from the root (the rule before PLANET-101, and the
    /// behaviour baseline's). Otherwise it goes to the worlds nearest the player's attention first (<see cref="Attention"/>),
    /// and systems keep at least the room they were given in earlier layouts (<see cref="Attention.Reserves"/>).</param>
    public static IReadOnlyList<UniverseBody> Compute(FsTree tree, Func<FsNode, bool>? visible = null, int root = 0, IReadOnlySet<int>? pinned = null, Attention? attention = null)
    {
        pinned ??= new HashSet<int>();
        if (tree.Count == 0 || !tree.Contains(root)) return [];
        visible ??= _ => true;
        var (order, kids, hidden) = attention == null ? Select(tree, visible, root, pinned) : SelectByAttention(tree, visible, root, pinned, attention);
        var reserves = attention?.Reserves;

        // Bottom-up: body radius from size, ring radius from the children's extents, extent of the whole system.
        var bodyRadius = new Dictionary<int, float>();
        var ringRadius = new Dictionary<int, float>();
        var extent = new Dictionary<int, float>();
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var id = order[i];
            var n = tree[id];
            var size = n.SubtreeFileCount + n.SubtreeDirCount;
            var r = MinBodyRadius + BodyRadiusPerDecade * MathF.Log10(1 + size);
            bodyRadius[id] = r;
            var children = kids[id];
            var reserved = reserves != null && reserves.TryGetValue(id, out var rv) ? rv : default;
            if (children.Count == 0)
            {
                // A system whose children gave way to attention elsewhere keeps its room (PLANET-105): its ring's radius
                // stays as its haze, and its neighbours do not close in on it.
                ringRadius[id] = reserved.Ring;
                extent[id] = MathF.Max(r, reserved.Extent);
                if (reserves != null) reserves[id] = new Reserve(ringRadius[id], extent[id]);
                continue;
            }

            // The ring must (a) clear the body itself plus the widest child system and (b) have a circumference that holds
            // every child's diameter plus a gap; the chord test below then catches the case where arcs fit but straight-line
            // distances between neighbours do not (few, large children).
            var maxExt = children.Max(c => extent[c]);
            var around = children.Sum(c => 2 * extent[c] + Gap);
            var ring = MathF.Max(r + Gap + maxExt, around / (2 * MathF.PI));
            if (children.Count >= 2)
            {
                // The chord between neighbours (not the arc) must hold both systems: R ≥ (e_i + e_j + gap) / (2 sin(Δθ/2)).
                for (var k = 0; k < children.Count; k++)
                {
                    var a = children[k];
                    var b = children[(k + 1) % children.Count];
                    var dTheta = ((2 * extent[a] + Gap) + (2 * extent[b] + Gap)) / around * MathF.PI; // half of each share
                    if (children.Count == 2) dTheta = MathF.PI;
                    ring = MathF.Max(ring, (extent[a] + extent[b] + Gap) / (2 * MathF.Sin(MathF.Min(dTheta, MathF.PI) / 2)));
                }
            }

            ring = MathF.Max(ring, reserved.Ring);
            ringRadius[id] = ring;
            extent[id] = MathF.Max(ring + maxExt, reserved.Extent);
            if (reserves != null) reserves[id] = new Reserve(ring, extent[id]);
        }

        // Top-down: place children on their parent's tilted ring, angles by cumulative extent.
        // (The expressions below keep their exact operation order: reordering float arithmetic would move worlds.)
        var position = new Dictionary<int, Vector3> { [root] = Vector3.Zero };
        var normal = new Dictionary<int, Vector3>();
        var depth = new Dictionary<int, int> { [root] = 0 };
        var bodies = new List<UniverseBody>(order.Count);
        foreach (var id in order)
        {
            var children = kids[id];
            var h = Hash(id);
            // The ring's normal: straight up (+Y), tipped by 12°…34° about a horizontal axis pointing in a hashed
            // direction. The tip alternates sign with depth, so a child system leans the other way from its parent and
            // nested rings read as separate planes instead of stacking into one tilted disc.
            var tiltAxis = Vector3.Normalize(new Vector3(MathF.Cos(h % TiltAxisSteps / TiltAxisStepsPerRadian), 0, MathF.Sin(h % TiltAxisSteps / TiltAxisStepsPerRadian)));
            var tilt = (MinTiltDegrees + h % TiltDegreeSteps) * MathF.PI / 180f * (depth[id] % 2 == 0 ? 1 : -1);
            var n = Vector3.Transform(Vector3.UnitY, Quaternion.CreateFromAxisAngle(tiltAxis, tilt));
            normal[id] = n;
            if (children.Count > 0)
            {
                // Two unit axes spanning the ring's plane: a = n × reference (X, or Z when n is nearly along X, see
                // NearlyParallelToX), b = n × a. Children sit at cos·a + sin·b, so the ring lies exactly in the plane ⟂ n.
                var a = Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.X) < NearlyParallelToX ? Vector3.UnitX : Vector3.UnitZ));
                var b = Vector3.Cross(n, a);
                // Each child takes an arc proportional to its system's diameter plus the gap (the same sum that sized the
                // ring), centred in its arc, starting from a hashed angle so siblings' rings do not all begin at +a.
                var total = children.Sum(c => 2 * extent[c] + Gap);
                var acc = (h % StartAngleDegreeSteps) * MathF.PI / 180f;
                foreach (var c in children)
                {
                    var share = (2 * extent[c] + Gap) / total * 2 * MathF.PI;
                    var angle = acc + share / 2;
                    acc += share;
                    position[c] = position[id] + (a * MathF.Cos(angle) + b * MathF.Sin(angle)) * ringRadius[id];
                    depth[c] = depth[id] + 1;
                }
            }

            var node = tree[id];
            var discovery = node.ScanState == DirScanState.Complete && (node.Flags & NodeFlags.Incomplete) == 0 ? Discovery.Listed
                : node.ScanState is DirScanState.Complete or DirScanState.Listing ? Discovery.Partial
                : Discovery.Unknown;
            bodies.Add(new UniverseBody(id, id == root ? -1 : node.ParentId, position[id], bodyRadius[id], depth[id], discovery, hidden[id], n, ringRadius[id]));
        }

        return bodies;
    }

    /// <summary>
    /// The worlds the overview would lay out, without laying them out (PLANET-90: background discovery during play lists
    /// exactly what the overview would draw, without the cost of placing it): each with its depth below
    /// <paramref name="root"/>, in the layout's breadth-first order. Same rules and budget as <see cref="Compute"/>.
    /// </summary>
    public static IReadOnlyList<(int Id, int Depth)> Selection(FsTree tree, Func<FsNode, bool>? visible = null, int root = 0, IReadOnlySet<int>? pinned = null, Attention? attention = null)
    {
        if (tree.Count == 0 || !tree.Contains(root)) return [];
        var (order, _, _) = attention == null ? Select(tree, visible ?? (_ => true), root, pinned ?? new HashSet<int>()) : SelectByAttention(tree, visible ?? (_ => true), root, pinned ?? new HashSet<int>(), attention);
        var rootDepth = tree[root].Depth;
        return order.Select(id => (id, tree[id].Depth - rootDepth)).ToList();
    }

    /// <summary>
    /// How the overview spends its budget when it follows attention (PLANET-101..105, AD-81). Each folder's <b>attention
    /// cost</b> is its distance in the tree from the nearest focus — <see cref="UpCost"/> per level up to where the two
    /// paths meet, <see cref="DownCost"/> per level down from there — plus that focus's offset; and never more than the
    /// background's <see cref="BackgroundCostPerLevel"/> × its depth below the overview's root, so the top of the hierarchy
    /// is always there for scale. The budget goes to the cheapest folders first (then the shallower, then name order):
    /// the selected world's children (cost 1), its grandchildren and its siblings (2), its great-grandchildren, nephews and
    /// aunts (3), and so on, whatever else was explored before. A folder's parent is always cheaper than the folder or on
    /// a focus's way (which is cheaper still), so what is laid out is always connected.
    /// </summary>
    public sealed record Attention
    {
        /// <summary>The foci (ids not in the tree are ignored).</summary>
        public IReadOnlyList<AttentionFocus> Foci { get; init; } = [];

        /// <summary>The most worlds laid out. <see cref="MaxBodies"/> by default; the app passes its measured budget.</summary>
        public int Budget { get; init; } = MaxBodies;

        /// <summary>Attention cost per level up from a focus (to a sibling's branch, an aunt's…).</summary>
        public float UpCost { get; init; } = 1f;

        /// <summary>Attention cost per level down (to a child, a grandchild…).</summary>
        public float DownCost { get; init; } = 1f;

        /// <summary>
        /// The background's cost per level below the root: 2 makes the root's children as dear as the selected world's
        /// grandchildren, so the top two or three levels of the whole hierarchy stay in view (scale) without taking the
        /// budget from where you look.
        /// </summary>
        public float BackgroundCostPerLevel { get; init; } = 2f;

        /// <summary>Folders laid out last time: they give way only to folders clearly nearer attention (hysteresis, so a
        /// small move of attention does not fold and unfold systems at the edge of the budget).</summary>
        public IReadOnlySet<int>? Retained { get; init; }

        /// <summary>How much nearer, in attention units, a new folder must be to take a retained folder's place.</summary>
        public float RetainBonus { get; init; } = 1.5f;

        /// <summary>
        /// In and out: each system's room from earlier layouts while the overview stays open (<see cref="Reserve"/>). A
        /// system never gets less room than it had, so when its children give way to attention elsewhere it stays where it
        /// is and nothing round it moves (PLANET-105); the dictionary is updated with this layout's room. Null: no memory.
        /// </summary>
        public IDictionary<int, Reserve>? Reserves { get; init; }
    }

    /// <summary>
    /// A folder's attention cost (see <see cref="Attention"/>), or +∞ when it is not below <paramref name="root"/>. Walks
    /// up from the folder once per focus (O(depth × foci)): for candidates and status, not for whole trees. The retained
    /// bonus is not applied (it is the layout's hysteresis, not attention).
    /// </summary>
    public static float AttentionCost(FsTree tree, int id, int root, Attention attention)
    {
        if (!tree.Contains(id) || !tree.Contains(root)) return float.PositiveInfinity;
        var rootDepth = tree[root].Depth;
        var below = false;
        for (var a = id; a >= 0; a = tree[a].ParentId)
        {
            if (a != root) continue;
            below = true;
            break;
        }

        if (!below) return float.PositiveInfinity;
        var best = attention.BackgroundCostPerLevel * (tree[id].Depth - rootDepth);
        foreach (var f in attention.Foci)
        {
            if (!tree.Contains(f.Id)) continue;
            // The deepest common ancestor: walk both up to the same depth, then together.
            int a = id, b = f.Id;
            while (a >= 0 && b >= 0 && tree[a].Depth > tree[b].Depth) a = tree[a].ParentId;
            while (a >= 0 && b >= 0 && tree[b].Depth > tree[a].Depth) b = tree[b].ParentId;
            while (a >= 0 && b >= 0 && a != b)
            {
                a = tree[a].ParentId;
                b = tree[b].ParentId;
            }

            if (a < 0 || b < 0) continue;
            var common = tree[a].Depth;
            best = MathF.Min(best, f.Offset + attention.UpCost * (tree[f.Id].Depth - common) + attention.DownCost * (tree[id].Depth - common));
        }

        return best;
    }

    /// <summary>The cost of a folder on the way to a focus (plus the focus's offset): below every other cost, so the ways
    /// to all foci are laid out first.</summary>
    private const float WayCost = -1000f;

    /// <summary>
    /// Pass 1 with attention (<see cref="Attention"/>): best-first from the root by attention cost. A folder's children
    /// enter the frontier when it is laid out (the pinned ones first, then the cheapest, then name order, at most
    /// <see cref="MaxChildren"/>); the cheapest in the frontier is laid out next, until the budget is spent. Pinned folders
    /// always make it in. What is left in the frontier is counted as its parent's hidden folders.
    /// </summary>
    private static (List<int> Order, Dictionary<int, List<int>> Kids, Dictionary<int, int> Hidden) SelectByAttention(FsTree tree, Func<FsNode, bool> visible, int root, IReadOnlySet<int> pinned, Attention attention)
    {
        var foci = new List<AttentionFocus>(attention.Foci.Count);
        foreach (var f in attention.Foci)
        {
            if (tree.Contains(f.Id)) foci.Add(f);
        }

        var nf = foci.Count;
        // The way to each focus: folder → (focus index, levels above it). A folder on several ways keeps each.
        var ways = new Dictionary<int, List<(int Focus, int Up)>>();
        for (var k = 0; k < nf; k++)
        {
            var up = 0;
            for (var a = foci[k].Id; a >= 0; a = tree[a].ParentId)
            {
                if (!ways.TryGetValue(a, out var list)) ways[a] = list = new List<(int, int)>(1);
                list.Add((k, up++));
            }
        }

        // Each folder's distance to every focus, in one flat array (nf per slot): on a focus's way it is the levels up;
        // off it, one level further down than its parent's.
        var dist = new List<float>(1024 * Math.Max(1, nf));
        var slot = new Dictionary<int, int>();
        var rootDepth = tree[root].Depth;

        int Place(int id, int parentSlot)
        {
            var s = dist.Count / Math.Max(1, nf);
            for (var k = 0; k < nf; k++) dist.Add(parentSlot >= 0 ? dist[parentSlot * nf + k] + attention.DownCost : float.PositiveInfinity);
            if (ways.TryGetValue(id, out var on))
            {
                foreach (var (k, up) in on) dist[s * nf + k] = attention.UpCost * up;
            }

            slot[id] = s;
            return s;
        }

        float CostOf(int id, int s)
        {
            var c = attention.BackgroundCostPerLevel * (tree[id].Depth - rootDepth);
            var onWay = ways.TryGetValue(id, out var on) ? on : null;
            for (var k = 0; k < nf; k++) c = MathF.Min(c, foci[k].Offset + dist[s * nf + k]);
            if (onWay != null)
            {
                // The way to a focus comes before anything else (it is how the focus hangs in the picture: a deep selected
                // world's ancestors must not lose to the shallow levels of other branches), the most attended focus's first.
                foreach (var (k, _) in onWay) c = MathF.Min(c, WayCost + foci[k].Offset);
            }

            if (attention.Retained?.Contains(id) == true) c -= attention.RetainBonus;
            return c;
        }

        var kids = new Dictionary<int, List<int>>();
        var hidden = new Dictionary<int, int>();
        var order = new List<int>();
        var frontier = new PriorityQueue<int, (float Cost, int Depth, int Seq)>();
        var seq = 0;
        var pinnedPending = 0;
        foreach (var pid in pinned)
        {
            if (pid != root && tree.Contains(pid)) pinnedPending++;
        }

        Place(root, -1);
        frontier.Enqueue(root, (float.NegativeInfinity, 0, seq++));
        var scored = new List<(int Id, float Cost, bool Pinned)>();
        var byName = Comparer<(int Id, float Cost, bool Pinned)>.Create((x, y) =>
        {
            if (x.Pinned != y.Pinned) return x.Pinned ? -1 : 1;
            var c = x.Cost.CompareTo(y.Cost);
            if (c != 0) return c;
            c = NaturalNameOrder.Instance.Compare(tree[x.Id].Name, tree[y.Id].Name);
            return c != 0 ? c : x.Id.CompareTo(y.Id);
        });
        while (frontier.TryDequeue(out var id, out _))
        {
            var isPinned = id == root || pinned.Contains(id);
            if (!isPinned && order.Count + pinnedPending >= attention.Budget)
            {
                // Out of budget: it stays folded inside its parent (pinned folders still come through the queue).
                var p = tree[id].ParentId;
                hidden[p] = hidden.GetValueOrDefault(p) + 1;
                continue;
            }

            if (isPinned && id != root) pinnedPending--;
            order.Add(id);
            var s = slot[id];
            scored.Clear();
            var pinnedKids = 0;
            foreach (var c in tree[id].Children)
            {
                var cn = tree[c];
                var p = pinned.Contains(c);
                if (!cn.IsDirectory || !p && !visible(cn)) continue;
                var cs = Place(c, s);
                scored.Add((c, p ? float.NegativeInfinity : CostOf(c, cs), p));
                if (p) pinnedKids++;
            }

            // At most MaxChildren per system (a ring of more is unreadable), the pinned ones always among them.
            var take = Math.Min(scored.Count, Math.Max(MaxChildren, pinnedKids));
            scored.Sort(byName); // also orders ties by name, so the layout's order (discovery's tie-break) is name order
            hidden[id] = hidden.GetValueOrDefault(id) + scored.Count - take;
            for (var i = 0; i < take; i++) frontier.Enqueue(scored[i].Id, (scored[i].Cost, tree[scored[i].Id].Depth, seq++));
        }

        // Only what was laid out is a child in the layout, in name order round its parent's ring.
        foreach (var id in order) kids[id] = [];
        foreach (var id in order)
        {
            if (id != root) kids[tree[id].ParentId].Add(id);
            hidden.TryAdd(id, 0);
        }

        var nameOrder = Comparer<int>.Create((x, y) =>
        {
            var c = NaturalNameOrder.Instance.Compare(tree[x].Name, tree[y].Name);
            return c != 0 ? c : x.CompareTo(y);
        });
        foreach (var list in kids.Values)
        {
            if (list.Count > 1) list.Sort(nameOrder);
        }

        return (order, kids, hidden);
    }

    /// <summary>Pass 1 of <see cref="Compute"/>: breadth-first selection within the budget (see the class summary).</summary>
    private static (List<int> Order, Dictionary<int, List<int>> Kids, Dictionary<int, int> Hidden) Select(FsTree tree, Func<FsNode, bool> visible, int root, IReadOnlySet<int> pinned)
    {
        // Breadth-first selection within the budget (the hierarchy near the root first).
        var kids = new Dictionary<int, List<int>>();
        var hidden = new Dictionary<int, int>();
        var order = new List<int> { root };
        var queue = new Queue<int>();
        queue.Enqueue(root);
        // Pinned folders not yet placed, kept as a running count (PLANET-77: the overview now pins every world it already
        // draws, ≈ 1,500, so counting them afresh for every folder was quadratic). Children are never already in the
        // order, so for each folder the count of pinned ids neither placed nor among its children is this minus mustHave.
        var pinnedPending = pinned.Count(pid => pid != root);
        while (queue.TryDequeue(out var id))
        {
            var all = tree[id].Children.Where(c => tree[c].IsDirectory && (visible(tree[c]) || pinned.Contains(c))).OrderBy(c => tree[c].Name, NaturalNameOrder.Instance).ThenBy(c => c).ToList();
            // Pinned folders first (never cut by the budget), then the rest in name order within what is left.
            var mustHave = all.Where(pinned.Contains).ToList();
            var room = Math.Max(0, MaxBodies - order.Count - (pinnedPending - mustHave.Count));
            var take = mustHave.Concat(all.Where(c => !pinned.Contains(c)).Take(Math.Max(0, Math.Min(MaxChildren, room) - mustHave.Count)))
                .OrderBy(c => tree[c].Name, NaturalNameOrder.Instance).ThenBy(c => c).ToList();
            kids[id] = take;
            hidden[id] = all.Count - take.Count;
            foreach (var c in take)
            {
                order.Add(c);
                queue.Enqueue(c);
                if (pinned.Contains(c)) pinnedPending--;
            }
        }

        return (order, kids, hidden);
    }
}
