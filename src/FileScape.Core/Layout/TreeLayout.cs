using System.Numerics;
using FileScape.Core.Model;

namespace FileScape.Core.Layout;

public enum ConnectorStyle
{
    /// <summary>FSN-style straight wire from the parent's back edge to the child's front edge.</summary>
    Wire,

    /// <summary>An orthogonal street at ground level: back from the parent, across, then into the child.</summary>
    Street,
}

/// <summary>Spatial parameters that distinguish one visualization's layout from another's.</summary>
public sealed record LayoutParams
{
    /// <summary>The spacing of the file grid on a pedestal.</summary>
    public float FileCell { get; init; } = 1.0f;

    /// <summary>The width and depth of a file block inside its cell.</summary>
    public float FileFootprint { get; init; } = 0.62f;

    public float PedestalMargin { get; init; } = 0.45f;
    public float MinPedestalSize { get; init; } = 1.6f;
    public float SiblingGap { get; init; } = 1.6f;
    public float LevelGap { get; init; } = 5.5f;
    public float PedestalBaseHeight { get; init; } = 0.3f;

    /// <summary>The pedestal height gained per ten-fold increase in the directory's direct file bytes (FSN VIS-02).</summary>
    public float PedestalPerDecade { get; init; } = 0.07f;

    public float PedestalMaxHeight { get; init; } = 1.2f;
    public float LinkHeight { get; init; } = 0.35f;
    public SizeScale Scale { get; init; } = SizeScale.Classic;
    public ConnectorStyle Connectors { get; init; } = ConnectorStyle.Wire;

    public static readonly LayoutParams Classic = new();

    public static readonly LayoutParams District = new()
    {
        FileCell = 1.55f,
        FileFootprint = 1.1f,
        PedestalMargin = 0.9f,
        MinPedestalSize = 3.0f,
        SiblingGap = 3.2f,
        LevelGap = 8.0f,
        PedestalBaseHeight = 0.08f,
        PedestalPerDecade = 0f,
        PedestalMaxHeight = 0.08f,
        LinkHeight = 0.5f,
        Scale = SizeScale.District,
        Connectors = ConnectorStyle.Street,
    };

    public float PedestalHeight(long directBytes) =>
        MathF.Min(PedestalBaseHeight + PedestalPerDecade * MathF.Log10(1f + MathF.Max(0, directBytes)), PedestalMaxHeight);
}

/// <summary>A connection between a directory and a child directory, as a polyline in layout space.</summary>
public sealed record Connector(int ParentId, int ChildId, Vector3[] Points);

/// <summary>
/// The output of a layout: geometry for every node, indexed by node id. It is independent of any renderer.
/// Picking and camera framing use it directly (AD-05).
/// </summary>
public sealed class LayoutResult
{
    internal LayoutResult(int count, LayoutParams parameters, int treeVersion)
    {
        NodeBoxes = new Box[count];
        SubtreeBounds = new Box[count];
        Parameters = parameters;
        TreeVersion = treeVersion;
    }

    public LayoutParams Parameters { get; }
    public int TreeVersion { get; }

    /// <summary>The visible solid for each node: a pedestal for a directory, a block for a file or link.</summary>
    public Box[] NodeBoxes { get; }

    /// <summary>For directories: the bounds of the pedestal, its files, and all descendant subtrees. Otherwise the node box.</summary>
    public Box[] SubtreeBounds { get; }

    public List<Connector> Connectors { get; } = [];
    public Box Bounds => SubtreeBounds.Length == 0 ? Box.Empty : SubtreeBounds[0];
    public int Count => NodeBoxes.Length;
}

/// <summary>
/// A deterministic tree layout in the FSN tradition. Each directory is a pedestal holding its files in a grid.
/// Child directories form a row further back (-Z), and every level of the hierarchy shares one row depth.
/// A subtree occupies a contiguous X range, so siblings never overlap.
/// </summary>
/// <remarks>
/// Node ids are assigned breadth first, so a parent's id is always lower than its children's. Iterating ids from
/// high to low therefore visits children before parents. This gives a post-order pass without recursion, which
/// matters for 40+ level trees.
/// <para>
/// Spatial stability (VIS-06): a directory's own grid depends only on its own children. Adding nodes elsewhere can
/// still shift whole subtrees sideways. The app compensates by keeping the selected node fixed on screen.
/// </para>
/// </remarks>
public static class TreeLayout
{
    public static LayoutResult Compute(FsTree tree, LayoutParams p)
    {
        var n = tree.Count;
        var result = new LayoutResult(n, p, tree.Version);
        if (n == 0)
        {
            return result;
        }

        var pedW = new float[n];
        var pedD = new float[n];
        var cols = new int[n];
        var subtreeW = new float[n];
        var maxDepth = 0;

        // Pass 1 (post-order): pedestal footprint and subtree width.
        for (var id = n - 1; id >= 0; id--)
        {
            var node = tree[id];
            if (!node.IsDirectory)
            {
                continue;
            }

            maxDepth = Math.Max(maxDepth, node.Depth);
            var leafCount = 0;
            var childWidth = 0f;
            var dirChildren = 0;
            foreach (var c in node.Children)
            {
                if (tree[c].IsDirectory)
                {
                    childWidth += subtreeW[c];
                    dirChildren++;
                }
                else
                {
                    leafCount++;
                }
            }

            if (dirChildren > 1)
            {
                childWidth += p.SiblingGap * (dirChildren - 1);
            }

            var c0 = leafCount == 0 ? 0 : (int)MathF.Ceiling(MathF.Sqrt(leafCount));
            var rows = c0 == 0 ? 0 : (int)MathF.Ceiling(leafCount / (float)c0);
            cols[id] = c0;
            pedW[id] = MathF.Max(p.MinPedestalSize, c0 * p.FileCell + 2 * p.PedestalMargin);
            pedD[id] = MathF.Max(p.MinPedestalSize, rows * p.FileCell + 2 * p.PedestalMargin);
            subtreeW[id] = MathF.Max(pedW[id], childWidth);
        }

        // Each level shares a row. The row's Z spacing follows the deepest pedestal on that level.
        var rowDepth = new float[maxDepth + 1];
        for (var id = 0; id < n; id++)
        {
            var node = tree[id];
            if (node.IsDirectory)
            {
                rowDepth[node.Depth] = MathF.Max(rowDepth[node.Depth], pedD[id]);
            }
        }

        var rowZ = new float[maxDepth + 1];
        for (var d = 1; d <= maxDepth; d++)
        {
            rowZ[d] = rowZ[d - 1] - (rowDepth[d - 1] / 2 + p.LevelGap + rowDepth[d] / 2);
        }

        // Pass 2 (pre-order): positions. centreX holds each directory's assigned X centre.
        var centreX = new float[n];
        for (var id = 0; id < n; id++)
        {
            var node = tree[id];
            if (!node.IsDirectory)
            {
                continue;
            }

            var cx = centreX[id];
            var z = rowZ[node.Depth];
            var pedH = p.PedestalHeight(node.DirectFileBytes);
            var pedestal = Box.FromBase(new Vector3(cx, 0, z), pedW[id], pedH, pedD[id]);
            result.NodeBoxes[id] = pedestal;

            // Child directories, left to right, centred on the parent.
            var total = 0f;
            var dirChildren = 0;
            foreach (var c in node.Children)
            {
                if (tree[c].IsDirectory)
                {
                    total += subtreeW[c];
                    dirChildren++;
                }
            }

            total += dirChildren > 1 ? p.SiblingGap * (dirChildren - 1) : 0;
            var x = cx - total / 2;

            // Leaves fill the grid row by row, starting at the front-left (+Z is the front, facing the default camera).
            var gridCols = cols[id];
            var leafIndex = 0;
            var gridLeft = cx - gridCols * p.FileCell / 2;
            var gridFront = pedestal.Max.Z - p.PedestalMargin;
            foreach (var c in node.Children)
            {
                var child = tree[c];
                if (child.IsDirectory)
                {
                    centreX[c] = x + subtreeW[c] / 2;
                    x += subtreeW[c] + p.SiblingGap;
                    continue;
                }

                var col = leafIndex % gridCols;
                var row = leafIndex / gridCols;
                leafIndex++;
                var fx = gridLeft + (col + 0.5f) * p.FileCell;
                var fz = gridFront - (row + 0.5f) * p.FileCell;
                var h = child.Kind == NodeKind.Link ? p.LinkHeight : p.Scale.Height(child.Size);
                var box = Box.FromBase(new Vector3(fx, pedH, fz), p.FileFootprint, h, p.FileFootprint);
                result.NodeBoxes[c] = box;
                result.SubtreeBounds[c] = box;
            }
        }

        // Pass 3 (post-order): subtree bounds and connectors.
        for (var id = n - 1; id >= 0; id--)
        {
            var node = tree[id];
            if (!node.IsDirectory)
            {
                continue;
            }

            var bounds = result.NodeBoxes[id];
            foreach (var c in node.Children)
            {
                bounds = bounds.Union(result.SubtreeBounds[c]);
            }

            result.SubtreeBounds[id] = bounds;
        }

        for (var id = 0; id < n; id++)
        {
            var node = tree[id];
            if (!node.IsDirectory || node.IsRoot)
            {
                continue;
            }

            result.Connectors.Add(new Connector(node.ParentId, id,
                ConnectorPoints(result.NodeBoxes[node.ParentId], result.NodeBoxes[id], p)));
        }

        return result;
    }

    private static Vector3[] ConnectorPoints(Box parent, Box child, LayoutParams p)
    {
        var from = new Vector3(parent.Center.X, parent.Max.Y * 0.5f, parent.Min.Z);
        var to = new Vector3(child.Center.X, child.Max.Y * 0.5f, child.Max.Z);
        if (p.Connectors == ConnectorStyle.Wire)
        {
            return [from, to];
        }

        // Streets run at ground level. The cross street sits halfway between the rows.
        const float y = 0.02f;
        var midZ = (from.Z + to.Z) / 2;
        return
        [
            new Vector3(from.X, y, from.Z),
            new Vector3(from.X, y, midZ),
            new Vector3(to.X, y, midZ),
            new Vector3(to.X, y, to.Z),
        ];
    }
}
