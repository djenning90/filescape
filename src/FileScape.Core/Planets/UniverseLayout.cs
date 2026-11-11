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
/// The overview of the known folder hierarchy in 3D (PLANET-19, 2026-11-12; "Universe Sandbox–style"): every known
/// folder is a world; a folder's subfolders orbit it on one tilted ring, so the hierarchy reads as nested star systems.
/// Ring radii are found bottom-up so neighbours never overlap (a ring's circumference holds its children's extents), and
/// each ring is tilted by a deterministic angle so the systems are not flat. Only what the tree already knows is laid
/// out: nothing is listed for the overview. Bounded: at most <see cref="MaxBodies"/> worlds, <see cref="MaxChildren"/>
/// per system (the rest is counted as <see cref="UniverseBody.HiddenChildren"/>).
/// </summary>
public static class UniverseLayout
{
    public const int MaxBodies = 1500;
    public const int MaxChildren = 64;

    /// <summary>Gap between neighbouring systems on a ring, metres (overview units).</summary>
    private const float Gap = 2.5f;

    public static IReadOnlyList<UniverseBody> Compute(FsTree tree, Func<FsNode, bool>? visible = null, int root = 0)
    {
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
            var all = tree[id].Children.Where(c => tree[c].IsDirectory && visible(tree[c])).OrderBy(c => tree[c].Name, NaturalNameOrder.Instance).ThenBy(c => c).ToList();
            var room = Math.Max(0, MaxBodies - order.Count);
            var take = all.Take(Math.Min(MaxChildren, room)).ToList();
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
            var r = 1.2f + 0.8f * MathF.Log10(1 + size);
            bodyRadius[id] = r;
            var children = kids[id];
            if (children.Count == 0)
            {
                ringRadius[id] = 0;
                extent[id] = r;
                continue;
            }

            var maxExt = children.Max(c => extent[c]);
            var around = children.Sum(c => 2 * extent[c] + Gap);
            var ring = MathF.Max(r + Gap + maxExt, around / (2 * MathF.PI));
            ringRadius[id] = ring;
            extent[id] = ring + maxExt;
        }

        // Top-down: place children on their parent's tilted ring, angles by cumulative extent.
        var position = new Dictionary<int, Vector3> { [root] = Vector3.Zero };
        var normal = new Dictionary<int, Vector3>();
        var depth = new Dictionary<int, int> { [root] = 0 };
        var bodies = new List<UniverseBody>(order.Count);
        foreach (var id in order)
        {
            var children = kids[id];
            var h = (uint)(id * 2654435761u);
            var tiltAxis = Vector3.Normalize(new Vector3(MathF.Cos(h % 628 / 100f), 0, MathF.Sin(h % 628 / 100f)));
            var tilt = (12f + h % 23) * MathF.PI / 180f * (depth[id] % 2 == 0 ? 1 : -1);
            var n = Vector3.Transform(Vector3.UnitY, Quaternion.CreateFromAxisAngle(tiltAxis, tilt));
            normal[id] = n;
            if (children.Count > 0)
            {
                var a = Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ));
                var b = Vector3.Cross(n, a);
                var total = children.Sum(c => 2 * extent[c] + Gap);
                var acc = (h % 360) * MathF.PI / 180f; // a deterministic starting angle per system
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
