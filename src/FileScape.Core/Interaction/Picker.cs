using System.Numerics;
using FileScape.Core.Layout;
using FileScape.Core.Model;

namespace FileScape.Core.Interaction;

/// <summary>
/// Ray picking against layout geometry, with directory subtree bounds serving as a bounding-volume hierarchy.
/// It is CPU-only and engine-independent, so it can be tested and works the same in every visualization (AD-05).
/// </summary>
public static class Picker
{
    /// <summary>
    /// Returns the nearest node hit by the ray, or -1. <paramref name="direction"/> must be unit length. The layout
    /// may be older than the tree (during a scan). Nodes without geometry are skipped, because they are not drawn yet.
    /// </summary>
    public static int Pick(FsTree tree, LayoutResult layout, Vector3 origin, Vector3 direction)
    {
        if (tree.Count == 0 || layout.Count == 0)
        {
            return -1;
        }

        var best = -1;
        var bestT = float.MaxValue;
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            var bt = layout.SubtreeBounds[id].Intersect(origin, direction);
            if (bt is null || bt.Value > bestT)
            {
                continue;
            }

            var t = layout.NodeBoxes[id].Intersect(origin, direction);
            if (t is { } hit && hit < bestT)
            {
                bestT = hit;
                best = id;
            }

            var node = tree[id];
            foreach (var c in node.Children)
            {
                if (!layout.Contains(c))
                {
                    continue; // added to the tree after this layout was computed
                }

                if (tree[c].IsDirectory)
                {
                    stack.Push(c);
                }
                else
                {
                    var ct = layout.NodeBoxes[c].Intersect(origin, direction);
                    if (ct is { } ch && ch < bestT)
                    {
                        bestT = ch;
                        best = c;
                    }
                }
            }
        }

        return best;
    }
}
