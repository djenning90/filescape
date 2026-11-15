using System.Numerics;
using FileScape.Core.Layout;
using FileScape.Core.Model;

namespace FileScape.Core.Interaction;

/// <summary>Why a node is a label candidate. The order of the values is the priority order.</summary>
public enum LabelRole
{
    Selected,
    Hovered,
    Related,
    Directory,
    File,
}

/// <summary>
/// Chooses label candidates in priority order (VIS-07): the selection, the hovered node, the selection's parent
/// and child directories, then the nearest directories, then nearby files. It only returns ids present in the
/// displayed layout, which can be older than the tree during a scan. Screen projection and overlap rejection stay
/// in the app.
/// </summary>
public static class LabelPlanner
{
    /// <summary>
    /// The label candidates, highest priority first, each id once. Distances are compared squared (the order is the same).
    /// </summary>
    /// <param name="maxDirectories">How many of the nearest directories may be labelled (then the app's overlap test thins them).</param>
    /// <param name="maxFiles">How many of the nearest files may be labelled.</param>
    /// <param name="fileRadius">Files further than this from the camera, layout units, are never labelled: file labels only make sense close up.</param>
    /// <remarks>The app's LabelLayer passes its own counts and keeps the default radius (rationale for the default figures not recorded).</remarks>
    public static List<(int Id, LabelRole Role)> Candidates(
        FsTree tree, LayoutResult layout, SelectionState selection, Vector3 cameraPosition,
        int maxDirectories = 220, int maxFiles = 160, float fileRadius = 30f)
    {
        var result = new List<(int, LabelRole)>();
        var seen = new HashSet<int>();

        void Add(int id, LabelRole role)
        {
            if (layout.Contains(id) && tree.Contains(id) && seen.Add(id))
            {
                result.Add((id, role));
            }
        }

        Add(selection.Selected, LabelRole.Selected);
        Add(selection.Hovered, LabelRole.Hovered);
        if (layout.Contains(selection.Selected) && tree.Contains(selection.Selected))
        {
            var sel = tree[selection.Selected];
            if (!sel.IsRoot)
            {
                Add(sel.ParentId, LabelRole.Related);
            }

            if (sel.IsDirectory)
            {
                foreach (var c in sel.Children)
                {
                    if (tree[c].IsDirectory)
                    {
                        Add(c, LabelRole.Related);
                    }
                }
            }
        }

        var dirs = new List<(float D, int Id)>();
        var files = new List<(float D, int Id)>();
        var limit = Math.Min(layout.Count, tree.Count);
        var r2 = fileRadius * fileRadius;
        for (var id = 0; id < limit; id++)
        {
            var d = Vector3.DistanceSquared(layout.NodeBoxes[id].Center, cameraPosition);
            if (tree[id].IsDirectory)
            {
                dirs.Add((d, id));
            }
            else if (d < r2)
            {
                files.Add((d, id));
            }
        }

        dirs.Sort((a, b) => a.D.CompareTo(b.D));
        files.Sort((a, b) => a.D.CompareTo(b.D));
        foreach (var (_, id) in dirs.Take(maxDirectories))
        {
            Add(id, LabelRole.Directory);
        }

        foreach (var (_, id) in files.Take(maxFiles))
        {
            Add(id, LabelRole.File);
        }

        return result;
    }
}
