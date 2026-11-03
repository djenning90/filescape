using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using Godot;
using GVector3 = Godot.Vector3;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Visual;

/// <summary>Rendering quality presets (VIS-09). Low exists so the cinematic look does not demand maximum effects.</summary>
public enum Quality
{
    Low,
    High,
}

/// <summary>Style bits passed to shaders in INSTANCE_CUSTOM.w. Keep in sync with block.gdshader and tower.gdshader.</summary>
public static class StyleBits
{
    public const float Dim = 1, Problem = 2, Link = 4, Cloud = 8;

    public static float For(FsNode n)
    {
        float s = 0;
        if ((n.Flags & (NodeFlags.Hidden | NodeFlags.System)) != 0) s += Dim;
        if (FsTree.IsProblem(n.Flags)) s += Problem;
        else if (n.Kind == NodeKind.Link) s += Link;
        else if ((n.Flags & NodeFlags.CloudPlaceholder) != 0) s += Cloud;
        return s;
    }
}

/// <summary>
/// A visualization maps the shared tree and selection onto a layout and a look (architecture AD-02). Classic and
/// District differ in layout parameters, geometry treatment, colour meaning, and environment. They share node
/// identity, picking, labels, and interaction, all of which live outside this class.
/// </summary>
public abstract class Visualization
{
    protected Visualization()
    {
        Root = new Node3D { Name = GetType().Name };
    }

    public Node3D Root { get; }
    public abstract string Name { get; }
    public abstract LayoutParams LayoutParams { get; }
    public abstract string ColorMeaning { get; }
    public abstract Color SelectionColor { get; }
    public abstract Color HoverColor { get; }

    /// <summary>Legend swatches for the colour mapping.</summary>
    public abstract IReadOnlyList<(Color Color, string Label)> Legend { get; }

    /// <summary>The colour that encodes the node's meaning (for the inspector swatch and the legend).</summary>
    public abstract Color ColorFor(FsNode node, FsTree tree);

    public abstract void ConfigureEnvironment(WorldEnvironment world, DirectionalLight3D sun, MeshInstance3D ground, Quality quality);

    /// <summary>Rebuilds all instance batches from the layout. Returns the instance count.</summary>
    public int Rebuild(FsTree tree, LayoutResult layout) => Build(tree, layout, DepthFirstOrder(tree));

    private readonly Dictionary<string, List<MultiMeshInstance3D>> _pools = [];

    /// <summary>Builds a named batch into reused MultiMesh nodes (see <see cref="InstanceBatch.BuildInto"/>).</summary>
    protected void Emit(string slot, InstanceBatch batch, Mesh mesh, Material material, bool castShadows = true)
    {
        if (!_pools.TryGetValue(slot, out var pool))
        {
            pool = [];
            _pools[slot] = pool;
        }

        batch.BuildInto(pool, Root, mesh, material, castShadows);
    }

    /// <summary>Removes all geometry, for example when a new scan starts.</summary>
    public void Clear()
    {
        foreach (var pool in _pools.Values)
        {
            foreach (var m in pool) m.QueueFree();
            pool.Clear();
        }
    }

    protected abstract int Build(FsTree tree, LayoutResult layout, List<int> order);

    /// <summary>Depth-first order: each directory followed by its leaves, then its child directories.</summary>
    protected static List<int> DepthFirstOrder(FsTree tree)
    {
        var order = new List<int>(tree.Count);
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            order.Add(id);
            var children = tree[id].Children;
            foreach (var c in children)
            {
                if (!tree[c].IsDirectory)
                {
                    order.Add(c);
                }
            }

            for (var i = children.Count - 1; i >= 0; i--)
            {
                if (tree[children[i]].IsDirectory)
                {
                    stack.Push(children[i]);
                }
            }
        }

        return order;
    }

    /// <summary>A directory whose listing is missing or partial gets a floating marker, so the gap is visible (FS-08).</summary>
    protected static Color? MarkerColor(FsNode n)
    {
        if ((n.Flags & NodeFlags.AccessDenied) != 0) return Palette.Problem;
        if ((n.Flags & (NodeFlags.Vanished | NodeFlags.EnumerationError)) != 0) return Palette.Vanished;
        if ((n.Flags & NodeFlags.CloudNotTraversed) != 0) return Palette.Cloud;
        if (n.IsDirectory && (n.ScanState is DirScanState.Cancelled or DirScanState.NotTraversed || (n.Flags & NodeFlags.Incomplete) != 0)) return Palette.Incomplete;
        if (n.IsDirectory && n.ScanState == DirScanState.Pending) return Palette.Pending;
        return null;
    }

    protected static void AddMarker(InstanceBatch markers, Box box, Color color)
    {
        var top = new GVector3(box.Center.X, box.Max.Y + 0.9f, box.Center.Z);
        // A diamond: a unit cube rotated 45° about Y and squashed.
        var s = 0.42f;
        var colX = new GVector3(s, 0, s) * 0.7071f;
        var colZ = new GVector3(-s, 0, s) * 0.7071f;
        markers.Add(colX, new GVector3(0, s * 1.4f, 0), colZ, top, color, new Color(s, s * 1.4f, s, 0));
    }

    public static GVector3 Gd(NVector3 v) => new(v.X, v.Y, v.Z);
    public static NVector3 Num(GVector3 v) => new(v.X, v.Y, v.Z);
}

/// <summary>Shared colours for states that mean the same thing in every visualization.</summary>
public static class Palette
{
    public static readonly Color Problem = new(1f, 0.18f, 0.12f);
    public static readonly Color Vanished = new(1f, 0.62f, 0.1f);
    public static readonly Color LinkCyan = new(0.2f, 0.95f, 1f);
    public static readonly Color Cloud = new(0.8f, 0.9f, 1f);
    public static readonly Color Incomplete = new(0.95f, 0.85f, 0.25f);
    public static readonly Color Pending = new(0.55f, 0.6f, 0.7f);

    /// <summary>FSN-style age colours, newest (hot) to oldest (cool): seven bands, as in FSN's shipped defaults.</summary>
    public static readonly Color[] Age =
    [
        new("ff4a3d"), new("ff9a2e"), new("ffd23f"), new("7ee35a"), new("2fd0c8"), new("3b8dff"), new("8e6cff"),
    ];

    public static readonly Dictionary<FileCategory, Color> Category = new()
    {
        [FileCategory.Document] = new("f2e6c9"),
        [FileCategory.Image] = new("ff5fa2"),
        [FileCategory.Audio] = new("b37cff"),
        [FileCategory.Video] = new("ff7a3d"),
        [FileCategory.Archive] = new("ffcc33"),
        [FileCategory.Code] = new("37f0c6"),
        [FileCategory.Executable] = new("ff3b5c"),
        [FileCategory.Data] = new("4aa8ff"),
        [FileCategory.Other] = new("9aa7bd"),
    };
}
