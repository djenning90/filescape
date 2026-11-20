using FileScape.Core.Interaction;
using FileScape.Core.Model;

namespace FileScape.Core.Planets;

/// <summary>
/// The picture gallery's rules for files (PLANET-96, AD-78; widened by PLANET-130..135, AD-86). <b>Which folder is a
/// gallery</b> is <see cref="MediaWorld.Classify"/>'s answer; the marker (<see cref="MarkerName"/>, a file the person
/// makes, recognised by its name as <c>.filescape-hazard</c> is) is one of its reasons. <b>Which files are pictures:</b>
/// <see cref="IsPicture"/> — those FileScape decodes itself (<see cref="Decodes"/>) and those only Windows' thumbnails
/// show (HEIC, GIF, TIFF…). <b>Which files the viewer steps through:</b> <see cref="Pictures"/>, in natural name order.
/// <b>How each may be read</b> (<see cref="Verdict"/>), decided from metadata alone so stepping past one reads nothing:
/// a file on this PC is read like any file (also when a sync provider manages it); a cloud-only picture may be fetched
/// (DJ allowed ordinary picture loading from their cloud folders, 2026-11-20); a link is never read through; a file
/// over <see cref="MaxPictureBytes"/> is never read whole (Windows' thumbnail stands in).
/// Preserve: no file contents are read here (names, sizes, flags only); the size limit is the preview's.
/// </summary>
public static class GalleryRules
{
    /// <summary>The marker file's name: a folder holding a file of this name is a gallery.</summary>
    public const string MarkerName = ".filescape-gallery";

    /// <summary>The picture extensions the app decodes (Godot's PNG, JPEG, WebP and BMP loaders), lower case.</summary>
    public static readonly IReadOnlySet<string> PictureExtensions = new HashSet<string>(StringComparer.Ordinal) { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

    /// <summary>Pictures FileScape does not decode but Windows' thumbnail handlers usually show (PLANET-131), lower case.</summary>
    public static readonly IReadOnlySet<string> WindowsPictureExtensions = new HashSet<string>(StringComparer.Ordinal) { ".gif", ".heic", ".heif", ".tif", ".tiff", ".avif", ".jfif" };

    /// <summary>The largest picture file read for the viewer or a preview, bytes: the preview's existing 24 MiB limit
    /// (PlanetWorld.Overlay's MaxImageBytes), so the gallery reads nothing a preview would not.</summary>
    public const long MaxPictureBytes = 24L * 1024 * 1024;

    /// <summary>How a picture can be shown, decided from metadata.</summary>
    public enum PictureVerdict
    {
        /// <summary>On this PC and decodable: read and decoded.</summary>
        Viewable,

        /// <summary>Over <see cref="MaxPictureBytes"/>: never read whole; Windows' thumbnail may stand in.</summary>
        TooLarge,

        /// <summary>A real link (not a sync provider's tag): never read through.</summary>
        Link,

        /// <summary>Only in the cloud: reading it makes the provider fetch it (allowed for pictures, with feedback).</summary>
        Download,

        /// <summary>A kind FileScape does not decode (HEIC, GIF, TIFF…): Windows' thumbnail shows it.</summary>
        WindowsOnly,
    }

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

    private static string Ext(string name) => Path.GetExtension(name).ToLowerInvariant();

    /// <summary>True when the name is a picture: one the app decodes or one Windows' thumbnails show.</summary>
    public static bool IsPicture(string name)
    {
        var e = Ext(name);
        return PictureExtensions.Contains(e) || WindowsPictureExtensions.Contains(e);
    }

    /// <summary>True when FileScape decodes the picture itself (PNG, JPEG, WebP, BMP).</summary>
    public static bool Decodes(string name) => PictureExtensions.Contains(Ext(name));

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

    /// <summary>A reparse point that is a real link or special file, not a sync provider's tag on an ordinary file.</summary>
    public static bool IsLinkLike(FsNode n) => (n.Flags & NodeFlags.ReparsePoint) != 0 && (n.Flags & NodeFlags.CloudSynced) == 0;

    /// <summary>How the picture <paramref name="n"/> may be read (see <see cref="PictureVerdict"/>): a link never; a file
    /// over <see cref="MaxPictureBytes"/> never whole; a kind FileScape does not decode only through Windows' thumbnail;
    /// a cloud-only one by fetching it.</summary>
    public static PictureVerdict Verdict(FsNode n) =>
        IsLinkLike(n) ? PictureVerdict.Link
        : n.Size > MaxPictureBytes ? PictureVerdict.TooLarge
        : !Decodes(n.Name) ? PictureVerdict.WindowsOnly
        : (n.Flags & NodeFlags.CloudPlaceholder) != 0 ? PictureVerdict.Download
        : PictureVerdict.Viewable;

    /// <summary>
    /// True when Open may hand <paramref name="n"/> to Windows without first asking about a cloud download: a picture
    /// (DJ, 2026-11-20: "repeated warnings for ordinary photos obstruct the experience"; opening one only fetches the
    /// photo for its viewer). Links and anything that runs code keep their own questions (Main's open checks).
    /// </summary>
    public static bool OpensWithoutAsking(FsNode n) => n.Kind == NodeKind.File && IsPicture(n.Name) && !IsLinkLike(n);
}
