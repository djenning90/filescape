using System.Numerics;
using FileScape.Core.Model;

namespace FileScape.Core.Layout;

/// <summary>How directory subtrees are arranged relative to their parent (AD-08).</summary>
public enum Arrangement
{
    /// <summary>
    /// The default. Child subtrees line both sides of a spine that runs back (-Z) from the parent pedestal, placed
    /// on the shorter side each time. A single child continues the spine straight. The tree fills a 2D area like a
    /// city plan, so overviews stay usable.
    /// </summary>
    Fishbone,

    /// <summary>
    /// Historical FSN arrangement: every hierarchy level shares one row receding along -Z, with children spread
    /// along X. It is faithful, but real trees become extremely wide (a 12k-node sample is ~20× wider than deep).
    /// </summary>
    Rows,
}

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
    public float MinPedestalSize { get; init; } = 1.4f;
    public float SiblingGap { get; init; } = 1.1f;
    public float LevelGap { get; init; } = 5.5f;
    public float PedestalBaseHeight { get; init; } = 0.3f;

    /// <summary>The pedestal height gained per ten-fold increase in the directory's direct file bytes (FSN VIS-02).</summary>
    public float PedestalPerDecade { get; init; } = 0.07f;

    public float PedestalMaxHeight { get; init; } = 1.2f;
    public float LinkHeight { get; init; } = 0.35f;
    public SizeScale Scale { get; init; } = SizeScale.Classic;
    public ConnectorStyle Connectors { get; init; } = ConnectorStyle.Wire;
    public Arrangement Arrangement { get; init; } = Arrangement.Fishbone;

    /// <summary>The width of the clear corridor along a fishbone spine, carrying wires/streets (Fishbone only).</summary>
    public float SpineWidth { get; init; } = 1.4f;

    /// <summary>The height at which connectors run.</summary>
    public float ConnectorHeight { get; init; } = 0.12f;

    public static readonly LayoutParams Classic = new();

    public static readonly LayoutParams District = new()
    {
        FileCell = 1.55f,
        FileFootprint = 1.1f,
        PedestalMargin = 0.9f,
        MinPedestalSize = 3.0f,
        SiblingGap = 3.2f,
        LevelGap = 8.0f,
        SpineWidth = 3.0f,
        ConnectorHeight = 0.02f,
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

    /// <summary>
    /// True if this layout has geometry for <paramref name="id"/>. During a scan the tree can grow while the
    /// displayed layout is deliberately kept (re-layout is throttled), so every geometry consumer must check this
    /// rather than the tree's node count.
    /// </summary>
    public bool Contains(int id) => id >= 0 && id < NodeBoxes.Length;
}

/// <summary>
/// A deterministic tree layout. Each directory is a pedestal holding its files in a grid. Child directories are
/// arranged behind it (-Z) according to <see cref="LayoutParams.Arrangement"/>. Subtrees never overlap.
/// </summary>
/// <remarks>
/// Node ids are assigned breadth first, so a parent's id is always lower than its children's. Iterating ids from
/// high to low therefore visits children before parents. This gives a post-order pass without recursion, which
/// matters for 40+ level trees.
/// <para>
/// Spatial stability (VIS-06): with Fishbone, a subtree's internal arrangement depends only on its own descendants.
/// Growth elsewhere moves it as a rigid block. The app compensates by keeping the selected node fixed on screen.
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

        // Pedestal footprints, from each directory's leaf (file and link) count.
        var pedW = new float[n];
        var pedD = new float[n];
        var cols = new int[n];
        for (var id = 0; id < n; id++)
        {
            var node = tree[id];
            if (!node.IsDirectory)
            {
                continue;
            }

            var leaves = node.Children.Count - node.DirectDirCount;
            var c0 = leaves == 0 ? 0 : (int)MathF.Ceiling(MathF.Sqrt(leaves));
            var rows = c0 == 0 ? 0 : (int)MathF.Ceiling(leaves / (float)c0);
            cols[id] = c0;
            pedW[id] = MathF.Max(p.MinPedestalSize, c0 * p.FileCell + 2 * p.PedestalMargin);
            pedD[id] = MathF.Max(p.MinPedestalSize, rows * p.FileCell + 2 * p.PedestalMargin);
        }

        // The arrangement decides each pedestal's centre X and front-edge Z, and the connector routes.
        var centreX = new float[n];
        var frontZ = new float[n];
        if (p.Arrangement == Arrangement.Rows)
        {
            ArrangeRows(tree, p, pedW, pedD, centreX, frontZ);
        }
        else
        {
            ArrangeFishbone(tree, p, pedW, pedD, centreX, frontZ, result.Connectors);
        }

        // Pedestals and their leaf grids.
        for (var id = 0; id < n; id++)
        {
            var node = tree[id];
            if (!node.IsDirectory)
            {
                continue;
            }

            var cx = centreX[id];
            var pedH = p.PedestalHeight(node.DirectFileBytes);
            var pedestal = Box.FromBase(new Vector3(cx, 0, frontZ[id] - pedD[id] / 2), pedW[id], pedH, pedD[id]);
            result.NodeBoxes[id] = pedestal;

            // Leaves fill the grid row by row, from the front-left (+Z is the front, facing the default camera).
            var gridCols = cols[id];
            var leafIndex = 0;
            var gridLeft = cx - gridCols * p.FileCell / 2;
            var gridFront = pedestal.Max.Z - p.PedestalMargin;
            foreach (var c in node.Children)
            {
                var child = tree[c];
                if (child.IsDirectory)
                {
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

        // Subtree bounds (post-order).
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

        if (p.Arrangement == Arrangement.Rows)
        {
            for (var id = 1; id < n; id++)
            {
                var node = tree[id];
                if (node.IsDirectory)
                {
                    result.Connectors.Add(new Connector(node.ParentId, id,
                        RowConnector(result.NodeBoxes[node.ParentId], result.NodeBoxes[id], p)));
                }
            }
        }

        return result;
    }

    /// <summary>The historical FSN rows: one row per level, children spread along X and centred under the parent.</summary>
    private static void ArrangeRows(FsTree tree, LayoutParams p, float[] pedW, float[] pedD, float[] centreX, float[] frontZ)
    {
        var n = tree.Count;
        var subtreeW = new float[n];
        var maxDepth = 0;
        for (var id = n - 1; id >= 0; id--)
        {
            var node = tree[id];
            if (!node.IsDirectory)
            {
                continue;
            }

            maxDepth = Math.Max(maxDepth, node.Depth);
            var childWidth = 0f;
            var k = 0;
            foreach (var c in node.Children)
            {
                if (tree[c].IsDirectory)
                {
                    childWidth += subtreeW[c];
                    k++;
                }
            }

            childWidth += k > 1 ? p.SiblingGap * (k - 1) : 0;
            subtreeW[id] = MathF.Max(pedW[id], childWidth);
        }

        var rowDepth = new float[maxDepth + 1];
        for (var id = 0; id < n; id++)
        {
            if (tree[id].IsDirectory)
            {
                rowDepth[tree[id].Depth] = MathF.Max(rowDepth[tree[id].Depth], pedD[id]);
            }
        }

        var rowZ = new float[maxDepth + 1];
        for (var d = 1; d <= maxDepth; d++)
        {
            rowZ[d] = rowZ[d - 1] - (rowDepth[d - 1] / 2 + p.LevelGap + rowDepth[d] / 2);
        }

        for (var id = 0; id < n; id++)
        {
            var node = tree[id];
            if (!node.IsDirectory)
            {
                continue;
            }

            frontZ[id] = rowZ[node.Depth] + pedD[id] / 2;
            var total = 0f;
            var k = 0;
            foreach (var c in node.Children)
            {
                if (tree[c].IsDirectory)
                {
                    total += subtreeW[c];
                    k++;
                }
            }

            total += k > 1 ? p.SiblingGap * (k - 1) : 0;
            var x = centreX[id] - total / 2;
            foreach (var c in node.Children)
            {
                if (tree[c].IsDirectory)
                {
                    centreX[c] = x + subtreeW[c] / 2;
                    x += subtreeW[c] + p.SiblingGap;
                }
            }
        }
    }

    /// <summary>
    /// Fishbone: a post-order pass computes each subtree's extents (left and right of its pedestal centre, and depth
    /// behind its front edge), with each child at an offset relative to its parent. A pre-order pass then turns the
    /// offsets into absolute positions and routes connectors down the spine and across the gap in front of each child.
    /// </summary>
    private static void ArrangeFishbone(FsTree tree, LayoutParams p, float[] pedW, float[] pedD, float[] centreX, float[] frontZ, List<Connector> connectors)
    {
        var n = tree.Count;
        var extL = new float[n];
        var extR = new float[n];
        var depth = new float[n];
        var relX = new float[n];
        var relZ = new float[n];
        var spineOf = new float[n];      // X offset of the spine a child hangs from, relative to its parent
        var halfSpine = p.SpineWidth / 2;
        var gap = p.SiblingGap;

        for (var id = n - 1; id >= 0; id--)
        {
            var node = tree[id];
            if (!node.IsDirectory)
            {
                continue;
            }

            extL[id] = extR[id] = pedW[id] / 2;
            depth[id] = pedD[id];
            if (node.DirectDirCount == 0)
            {
                continue;
            }

            var aisleStart = pedD[id] + p.LevelGap * 0.5f;
            if (node.DirectDirCount == 1)
            {
                // A single child continues the spine, which keeps deep chains straight and readable.
                var only = node.Children.First(c => tree[c].IsDirectory);
                relX[only] = 0;
                relZ[only] = -aisleStart;
                extL[id] = MathF.Max(extL[id], extL[only]);
                extR[id] = MathF.Max(extR[id], extR[only]);
                depth[id] = aisleStart + depth[only];
                continue;
            }

            // Many children: split them across k parallel spines joined by a cross street behind the parent. k is
            // chosen so the block comes out roughly square. One spine is the classic fishbone.
            var kids = new List<int>(node.DirectDirCount);
            float sumD = 0, sumW = 0;
            foreach (var c in node.Children)
            {
                if (tree[c].IsDirectory)
                {
                    kids.Add(c);
                    sumD += depth[c] + gap;
                    sumW += extL[c] + extR[c];
                }
            }

            var avgDepth = sumD / kids.Count;
            var avgSpineWidth = 2 * (sumW / kids.Count) + p.SpineWidth + gap;
            var spines = Math.Clamp((int)MathF.Round(MathF.Sqrt(kids.Count * avgDepth / (2 * avgSpineWidth))), 1, Math.Max(1, kids.Count / 2));
            var perSpine = (int)MathF.Ceiling(kids.Count / (float)spines);

            var spineLeft = new float[spines];
            var spineRight = new float[spines];
            var spineDepth = new float[spines];
            for (var sIdx = 0; sIdx < spines; sIdx++)
            {
                float cursorL = 0, cursorR = 0, widestL = 0, widestR = 0;
                for (var k = sIdx * perSpine; k < Math.Min(kids.Count, (sIdx + 1) * perSpine); k++)
                {
                    var c = kids[k];
                    // Place on the shorter side; ties go right, so the first child sits to the right of its spine.
                    // relX is relative to the spine for now and is shifted once the spine positions are known.
                    if (cursorR <= cursorL)
                    {
                        relX[c] = halfSpine + extL[c];
                        relZ[c] = -(aisleStart + cursorR);
                        cursorR += depth[c] + gap;
                        widestR = MathF.Max(widestR, extL[c] + extR[c]);
                    }
                    else
                    {
                        relX[c] = -(halfSpine + extR[c]);
                        relZ[c] = -(aisleStart + cursorL);
                        cursorL += depth[c] + gap;
                        widestL = MathF.Max(widestL, extL[c] + extR[c]);
                    }
                }

                spineLeft[sIdx] = halfSpine + widestL;
                spineRight[sIdx] = halfSpine + widestR;
                spineDepth[sIdx] = MathF.Max(cursorL, cursorR) - gap;
            }

            // Lay the spines side by side, centred on the parent.
            var total = 0f;
            for (var sIdx = 0; sIdx < spines; sIdx++)
            {
                total += spineLeft[sIdx] + spineRight[sIdx] + (sIdx > 0 ? gap : 0);
            }

            var x = -total / 2;
            var deepest = 0f;
            for (var sIdx = 0; sIdx < spines; sIdx++)
            {
                var spineX = spines == 1 ? 0 : x + spineLeft[sIdx];
                x += spineLeft[sIdx] + spineRight[sIdx] + gap;
                for (var k = sIdx * perSpine; k < Math.Min(kids.Count, (sIdx + 1) * perSpine); k++)
                {
                    relX[kids[k]] += spineX;
                    spineOf[kids[k]] = spineX;
                }

                deepest = MathF.Max(deepest, spineDepth[sIdx]);
            }

            var leftEdge = spines == 1 ? spineLeft[0] : total / 2;
            var rightEdge = spines == 1 ? spineRight[0] : total / 2;
            extL[id] = MathF.Max(extL[id], spines == 1 && spineLeft[0] <= halfSpine ? 0 : leftEdge);
            extR[id] = MathF.Max(extR[id], rightEdge);
            depth[id] = aisleStart + deepest;
        }

        for (var id = 1; id < n; id++)
        {
            var node = tree[id];
            if (!node.IsDirectory)
            {
                continue;
            }

            var parent = node.ParentId;
            centreX[id] = centreX[parent] + relX[id];
            frontZ[id] = frontZ[parent] + relZ[id];

            var y = p.ConnectorHeight;
            var start = new Vector3(centreX[parent], y, frontZ[parent] - pedD[parent]);
            var end = new Vector3(centreX[id], y, frontZ[id]);
            if (MathF.Abs(relX[id]) < 1e-4f)
            {
                connectors.Add(new Connector(parent, id, [start, end]));
            }
            else
            {
                var spineX = centreX[parent] + spineOf[id];
                var crossZ = frontZ[id] + gap * 0.5f;
                if (MathF.Abs(spineOf[id]) < 1e-4f)
                {
                    // Down the parent's own spine, then across the clear gap just in front of the child.
                    connectors.Add(new Connector(parent, id,
                    [
                        start,
                        new Vector3(centreX[parent], y, crossZ),
                        new Vector3(centreX[id], y, crossZ),
                        end,
                    ]));
                }
                else
                {
                    // Back to the cross street behind the parent, along it to this child's spine, down the spine,
                    // then across into the child.
                    var streetZ = frontZ[parent] - pedD[parent] - p.LevelGap * 0.25f;
                    connectors.Add(new Connector(parent, id,
                    [
                        start,
                        new Vector3(centreX[parent], y, streetZ),
                        new Vector3(spineX, y, streetZ),
                        new Vector3(spineX, y, crossZ),
                        new Vector3(centreX[id], y, crossZ),
                        end,
                    ]));
                }
            }
        }
    }

    private static Vector3[] RowConnector(Box parent, Box child, LayoutParams p)
    {
        var from = new Vector3(parent.Center.X, parent.Max.Y * 0.5f, parent.Min.Z);
        var to = new Vector3(child.Center.X, child.Max.Y * 0.5f, child.Max.Z);
        if (p.Connectors == ConnectorStyle.Wire)
        {
            return [from, to];
        }

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
