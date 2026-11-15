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
    public static IReadOnlyList<UniverseBody> Compute(FsTree tree, Func<FsNode, bool>? visible = null, int root = 0, IReadOnlySet<int>? pinned = null)
    {
        pinned ??= new HashSet<int>();
        if (tree.Count == 0 || !tree.Contains(root)) return [];
        visible ??= _ => true;
        // Breadth-first selection within the budget (the hierarchy near the root first).
        var kids = new Dictionary<int, List<int>>();
        var hidden = new Dictionary<int, int>();
        var order = new List<int> { root };
        var queue = new Queue<int>();
        queue.Enqueue(root);
        while (queue.TryDequeue(out var id))
        {
            var all = tree[id].Children.Where(c => tree[c].IsDirectory && (visible(tree[c]) || pinned.Contains(c))).OrderBy(c => tree[c].Name, NaturalNameOrder.Instance).ThenBy(c => c).ToList();
            // Pinned folders first (never cut by the budget), then the rest in name order within what is left.
            var mustHave = all.Where(pinned.Contains).ToList();
            var room = Math.Max(0, MaxBodies - order.Count - pinned.Count(pid => !order.Contains(pid) && !mustHave.Contains(pid)));
            var take = mustHave.Concat(all.Where(c => !pinned.Contains(c)).Take(Math.Max(0, Math.Min(MaxChildren, room) - mustHave.Count)))
                .OrderBy(c => tree[c].Name, NaturalNameOrder.Instance).ThenBy(c => c).ToList();
            kids[id] = take;
            hidden[id] = all.Count - take.Count;
            foreach (var c in take)
            {
                order.Add(c);
                queue.Enqueue(c);
            }
        }

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
            if (children.Count == 0)
            {
                ringRadius[id] = 0;
                extent[id] = r;
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

            ringRadius[id] = ring;
            extent[id] = ring + maxExt;
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
}
