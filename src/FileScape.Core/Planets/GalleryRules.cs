using FileScape.Core.Interaction;
using FileScape.Core.Model;

namespace FileScape.Core.Planets;

/// <summary>
/// The picture gallery prototype's rules (PLANET-96, AD-78; the Gallery of <c>docs/next-worlds.md</c>, narrowed to what
/// can be played now). <b>Which folder is a gallery:</b> one holding a file named <see cref="MarkerName"/> — recognised by
/// its name alone, as <c>.filescape-hazard</c> is, and made by the person (FileScape never creates one in their folders;
/// its own gallery sample carries one). The marker is the per-folder "yes" the design sketch asks for before pictures are
/// read as you walk: in a gallery the easels load their pictures by themselves, nearest first; anywhere else a picture is
/// read only when you ask (E), as before (FILE-02). <b>Which files the viewer steps through:</b>
/// <see cref="Pictures"/> — the folder's files with a picture extension the app decodes, in natural name order, and for
/// each the reason it cannot be shown, if any, decided from metadata alone (<see cref="Verdict"/>), so stepping past a
/// cloud placeholder or an over-large file reads nothing.
/// Preserve: no file contents are read here (names, sizes, flags only); the limits are the preview's (<see cref="MaxPictureBytes"/>).
/// </summary>
public static class GalleryRules
{
    /// <summary>The marker file's name: a folder holding a file of this name is a gallery.</summary>
    public const string MarkerName = ".filescape-gallery";

    /// <summary>The picture extensions the app decodes (Godot's PNG, JPEG, WebP and BMP loaders), lower case.</summary>
    public static readonly IReadOnlySet<string> PictureExtensions = new HashSet<string>(StringComparer.Ordinal) { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

    /// <summary>The largest picture file read for the viewer or a preview, bytes: the preview's existing 24 MiB limit
    /// (PlanetWorld.Overlay's MaxImageBytes), so the gallery reads nothing a preview would not.</summary>
    public const long MaxPictureBytes = 24L * 1024 * 1024;

    /// <summary>Whether a picture can be shown, decided from metadata.</summary>
    public enum PictureVerdict { Viewable, TooLarge, CloudPlaceholder, Link }

    /// <summary>True when <paramref name="folderId"/> is a folder holding a <see cref="MarkerName"/> file.</summary>
    public static bool IsGallery(FsTree tree, int folderId)
    {
        if (!tree.Contains(folderId)) return false;
        var folder = tree[folderId];
        if (folder.Kind != NodeKind.Directory) return false;
        foreach (var c in folder.Children)
        {
            var n = tree[c];
            if (n.Kind == NodeKind.File && n.Name.Equals(MarkerName, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>True when the name has a picture extension the app decodes.</summary>
    public static bool IsPicture(string name) => PictureExtensions.Contains(Path.GetExtension(name).ToLowerInvariant());

    /// <summary>The picture files directly in <paramref name="folderId"/>, in natural name order (hidden ones only when
    /// <paramref name="includeHidden"/>, as the world shows them).</summary>
    public static IReadOnlyList<int> Pictures(FsTree tree, int folderId, bool includeHidden)
    {
        if (!tree.Contains(folderId)) return [];
        return tree[folderId].Children
            .Where(c => tree[c] is { Kind: NodeKind.File } n && IsPicture(n.Name) && (includeHidden || (n.Flags & NodeFlags.Hidden) == 0))
            .OrderBy(c => tree[c].Name, NaturalNameOrder.Instance)
            .ToList();
    }

    /// <summary>Whether the viewer may read <paramref name="n"/>: never a cloud placeholder (reading it could download it),
    /// never through a link, never a file over <see cref="MaxPictureBytes"/>.</summary>
    public static PictureVerdict Verdict(FsNode n) =>
        (n.Flags & NodeFlags.CloudPlaceholder) != 0 ? PictureVerdict.CloudPlaceholder
        : (n.Flags & NodeFlags.ReparsePoint) != 0 ? PictureVerdict.Link
        : n.Size > MaxPictureBytes ? PictureVerdict.TooLarge
        : PictureVerdict.Viewable;
}
