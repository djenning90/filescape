using System.Globalization;
using FileScape.Core.Interaction;
using FileScape.Core.Model;

namespace FileScape.Core.Places;

/// <summary>
/// What a folder contains, in words (SUM-01..04): the immediate entries ("here") and the whole subtree ("in all"),
/// and whether the subtree totals are complete or only what has been found so far. Every number comes from the
/// tree's incremental aggregates; nothing here walks the tree or asks for a listing.
/// <list type="bullet">
/// <item>Counts include hidden and system entries (the visibility filter only hides them from view).</item>
/// <item>Sizes are logical sizes. Links are counted as links; their destinations never contribute (no double counting).</item>
/// <item>An unknown count is never shown as zero: a folder that is not listed says so instead.</item>
/// </list>
/// </summary>
public static class FolderSummary
{
    private static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    private static string Plural(int n, string one, string many) => $"{N(n)} {(n == 1 ? one : many)}";

    /// <summary>True when the folder's own entries are known (listed, completely or in part).</summary>
    public static bool HereKnown(FsNode folder) => folder.ScanState == DirScanState.Complete;

    /// <summary>True when the subtree totals are complete: every folder below (and this one) is fully listed.</summary>
    public static bool TotalsComplete(FsNode folder) => folder.SubtreeUnsettledDirs == 0;

    /// <summary>"3 folders · 12 files · 1 link" — the immediate entries, or null when they are not known yet.</summary>
    public static string? Here(FsNode folder)
    {
        if (!HereKnown(folder)) return null;
        if (folder.Children.Count == 0) return (folder.Flags & NodeFlags.Incomplete) != 0 ? "no entries listed" : "empty";
        var text = $"{Plural(folder.DirectDirCount, "folder", "folders")} · {Plural(folder.DirectFileCount, "file", "files")}";
        if (folder.DirectLinkCount > 0) text += $" · {Plural(folder.DirectLinkCount, "link", "links")}";
        return text;
    }

    /// <summary>
    /// The subtree size: "4.2 MB in all" when complete, "≥ 4.2 MB so far" when parts are not listed, and "size not
    /// known yet" when nothing with a size has been found and parts are missing (never "0 B").
    /// </summary>
    public static string TotalSize(FsNode folder)
    {
        if (TotalsComplete(folder)) return $"{SizeFormat.Short(folder.SubtreeBytes)} in all";
        return folder.SubtreeFileCount == 0 ? "size not known yet" : $"≥ {SizeFormat.Short(folder.SubtreeBytes)} so far";
    }

    /// <summary>
    /// A gate's second line (concise): immediate folders and files, then the subtree size with its completeness;
    /// or the listing state when the entries are not known. For example "3 folders · 12 files · 4.2 MB in all".
    /// </summary>
    public static string Gate(FsNode child, ListingState state)
    {
        if (state is ListingState.Empty) return "empty";
        if (Here(child) is not { } here) return Listing.Describe(state);
        var text = here == "empty" ? "empty" : $"{here} · {TotalSize(child)}";
        if (state == ListingState.Partial) text += " · partly listed";
        return text;
    }

    /// <summary>
    /// The place's own summary (HUD and the landmark plaque), two lines: what is here, and what is in the subtree.
    /// </summary>
    public static string Place(FsNode folder, ListingState state)
    {
        var here = Here(folder);
        var first = here == null ? $"Here: {Listing.Describe(state)}" : $"Here: {here}";
        if (here != null && folder.DirectHiddenCount > 0) first += $" (incl. {N(folder.DirectHiddenCount)} hidden)";
        if (here == null) return first;
        if (folder.Children.Count == 0 && TotalsComplete(folder)) return first;
        var second = TotalsComplete(folder)
            ? $"In all: {Plural(folder.SubtreeDirCount, "folder", "folders")} · {Plural(folder.SubtreeFileCount, "file", "files")} · {SizeFormat.Short(folder.SubtreeBytes)}"
            : $"Found so far: {Plural(folder.SubtreeDirCount, "folder", "folders")} · {Plural(folder.SubtreeFileCount, "file", "files")} · {(folder.SubtreeFileCount == 0 ? "size not known yet" : "≥ " + SizeFormat.Short(folder.SubtreeBytes))} ({Unlisted(folder)})";
        return first + "\n" + second;
    }

    /// <summary>"7 folders not listed yet" (this one included when its own listing is incomplete).</summary>
    public static string Unlisted(FsNode folder) =>
        $"{Plural(folder.SubtreeUnsettledDirs, "folder", "folders")} not fully listed yet";

    /// <summary>The inspector's rows: label, value (plain text).</summary>
    public static IEnumerable<(string Label, string Value)> Inspector(FsNode folder)
    {
        if (HereKnown(folder))
        {
            var here = $"{Plural(folder.DirectDirCount, "folder", "folders")}, {Plural(folder.DirectFileCount, "file", "files")} ({SizeFormat.Exact(folder.DirectFileBytes)})";
            if (folder.DirectLinkCount > 0) here += $", {Plural(folder.DirectLinkCount, "link", "links")}";
            if (folder.DirectHiddenCount > 0) here += $"; {N(folder.DirectHiddenCount)} of these hidden or system";
            if ((folder.Flags & NodeFlags.Incomplete) != 0) here += " — listed in part";
            yield return ("Here (direct)", here);
        }
        else
        {
            yield return ("Here (direct)", "not known — this folder is not listed yet");
        }

        var all = $"{Plural(folder.SubtreeDirCount, "folder", "folders")}, {Plural(folder.SubtreeFileCount, "file", "files")}, {SizeFormat.Exact(folder.SubtreeBytes)} ({SizeFormat.Short(folder.SubtreeBytes)})";
        if (folder.SubtreeLinkCount > 0) all += $", {Plural(folder.SubtreeLinkCount, "link", "links")}";
        yield return TotalsComplete(folder)
            ? ("In all (complete)", all)
            : ("Found so far", $"{all} — at least this much: {Unlisted(folder)}");
    }

    /// <summary>The fixed explanation shown under the totals.</summary>
    public const string Conventions = "Counts include hidden and system items. Sizes are logical sizes. Links are counted as links; what they lead to is never added (nothing is counted twice), and hard-linked files count once per name. Totals marked “so far” grow as folders are listed — FileScape lists folders when you visit them and never scans just to fill a label.";
}
