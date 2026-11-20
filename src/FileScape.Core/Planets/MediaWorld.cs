using System.Runtime.InteropServices;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Scanning;

namespace FileScape.Core.Planets;

/// <summary>What a folder's pictures make of its world (PLANET-130).</summary>
public enum MediaCharacter
{
    /// <summary>No pictures (or an unknown listing): an ordinary world.</summary>
    None,

    /// <summary>Pictures among other files: an ordinary world; a picture is read when you look at it (E).</summary>
    SomePictures,

    /// <summary>A picture gallery: large framed pictures that fill in as you walk, the viewer one key away.</summary>
    Gallery,
}

/// <summary>Why a folder is a gallery (said on the arrival card and in the state file), in the order they are tried.</summary>
public enum MediaReason
{
    None,

    /// <summary>The person's own <see cref="GalleryRules.MarkerName"/> file.</summary>
    Marker,

    /// <summary>At least <see cref="MediaWorld.MinPicturesByContent"/> pictures and <see cref="MediaWorld.ContentShare"/> of its files.</summary>
    MostlyPictures,

    /// <summary>A picture library (Pictures, also redirected into OneDrive; Camera Roll, Screenshots, Saved Pictures) or a folder inside one, with enough pictures.</summary>
    PictureLibrary,

    /// <summary>Directly inside a gallery (by marker or contents), with enough pictures of its own.</summary>
    InsideGallery,
}

/// <summary>A folder's media character, why, and what it was counted from (visible files; the marker is not one).</summary>
public sealed record MediaVerdict(MediaCharacter Character, MediaReason Reason, int Pictures, int Videos, int Files)
{
    public static readonly MediaVerdict Nothing = new(MediaCharacter.None, MediaReason.None, 0, 0, 0);

    public bool IsGallery => Character == MediaCharacter.Gallery;
}

/// <summary>
/// The folders this PC uses for pictures (PLANET-130): the Pictures known folder wherever it is redirected (on DJ's PC,
/// into OneDrive), the Camera Roll, Screenshots and Saved Pictures known folders, and OneDrive's own Pictures. A folder
/// at or under one of them is "in a picture library". Paths match whole folders: case-insensitively for Windows paths,
/// exactly for POSIX ones, with or without a trailing separator. <see cref="FromSystem"/> asks Windows; tests and other
/// hosts pass paths. Preserve: no folder is listed or read here (path strings only).
/// </summary>
public sealed class PictureLibraries
{
    public static readonly PictureLibraries Empty = new([]);

    private readonly List<string> _paths;

    public PictureLibraries(IEnumerable<string> paths) =>
        _paths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Trim).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<string> Paths => _paths;

    private static string Trim(string p) => p.Length > 1 && (p[^1] == '\\' || p[^1] == '/') && !(p.Length == 3 && p[1] == ':') ? p[..^1] : p;

    /// <summary>True when <paramref name="path"/> is a library or inside one.</summary>
    public bool Contains(string path)
    {
        if (_paths.Count == 0 || string.IsNullOrEmpty(path)) return false;
        path = Trim(path);
        foreach (var lib in _paths)
        {
            var cmp = Locations.IsCaseSensitive(lib) || lib.StartsWith('/') ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (!path.StartsWith(lib, cmp)) continue;
            if (path.Length == lib.Length) return true;
            if (path[lib.Length] is '\\' or '/') return true;
        }

        return false;
    }

    /// <summary>
    /// This PC's picture folders: the Pictures, Camera Roll, Screenshots and Saved Pictures known folders (resolved by
    /// Windows, so a redirection into OneDrive is followed) and <c>%OneDrive%\Pictures</c>. Empty on other hosts. Asks
    /// for paths only, never creating a folder (KF_FLAG_DONT_VERIFY: a missing folder is not checked).
    /// </summary>
    public static PictureLibraries FromSystem()
    {
        if (!OperatingSystem.IsWindows()) return Empty;
        var paths = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
        foreach (var id in KnownPictureFolders) paths.Add(KnownFolderPath(id) ?? "");
        if (Environment.GetEnvironmentVariable("OneDrive") is { Length: > 0 } od) paths.Add(Path.Combine(od, "Pictures"));
        return new PictureLibraries(paths);
    }

    /// <summary>FOLDERID_CameraRoll, FOLDERID_Screenshots and FOLDERID_SavedPictures (KnownFolders.h; fixed by Windows).</summary>
    private static readonly Guid[] KnownPictureFolders =
    [
        new("AB5FB87B-7CE2-4F83-915D-550846C9537B"),
        new("B7BEDE81-DF94-4682-A7D8-57A52620B86F"),
        new("3B193882-D3AD-4EAB-965A-69829D1FB59F"),
    ];

    /// <summary>KF_FLAG_DONT_VERIFY: return the path without checking (or creating) the folder.</summary>
    private const uint KnownFolderDontVerify = 0x00004000;

    private static string? KnownFolderPath(Guid id)
    {
        try
        {
            if (SHGetKnownFolderPath(id, KnownFolderDontVerify, IntPtr.Zero, out var p) != 0) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { Marshal.FreeCoTaskMem(p); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}

/// <summary>
/// Which folders take a picture gallery's character (PLANET-130, AD-86; before it only a marker file did, AD-78). From
/// the folder's own listing, its parent's, and the picture libraries — never a file's contents:
/// <list type="number">
/// <item>the marker (<see cref="GalleryRules.MarkerName"/>) makes a gallery;</item>
/// <item>at least <see cref="MinPicturesByContent"/> pictures making at least <see cref="ContentShare"/> of the visible
/// files (videos count with the pictures, as long as pictures lead) make one anywhere;</item>
/// <item>in a picture library (<see cref="PictureLibraries"/>: a redirected Pictures folder and its Screenshots, Camera
/// Roll, Saved Pictures…) a folder with a picture and <see cref="LibraryShare"/> of its files pictures is one — a mixed
/// folder there is still a gallery with its other files beside the pictures;</item>
/// <item>directly inside a gallery (by 1 or 2), <see cref="InsideShare"/> is enough.</item>
/// </list>
/// Otherwise a folder with pictures is <see cref="MediaCharacter.SomePictures"/>: an ordinary world, its pictures read when
/// you look (FILE-02). An empty folder or one of folders only has nothing to hang (<see cref="MediaCharacter.None"/>),
/// even in a library. Hidden and system files (desktop.ini, Thumbs.db) count only when the world shows them.
/// Preserve: deterministic and cheap (one pass over the folder and at most one over its parent; no recursion further);
/// names, flags and the tree only.
/// </summary>
public static class MediaWorld
{
    /// <summary>The fewest pictures that make a gallery by contents alone (two pictures among nothing else are a pair, not a gallery).</summary>
    public const int MinPicturesByContent = 3;

    /// <summary>The share of visible files that pictures (and videos) need for a gallery by contents alone (rationale: a clear majority).</summary>
    public const float ContentShare = 0.6f;

    /// <summary>The share needed inside a picture library: a quarter (one picture beside three notes in Pictures\Holiday is still a holiday gallery).</summary>
    public const float LibraryShare = 0.25f;

    /// <summary>The share needed directly inside a gallery (a subfolder of a photo folder that holds its own photos and notes).</summary>
    public const float InsideShare = 0.4f;

    /// <summary>The folder's media character (see the class summary).</summary>
    public static MediaVerdict Classify(FsTree tree, int folderId, PictureLibraries libraries, Func<FsNode, bool>? visible = null)
    {
        if (!tree.Contains(folderId)) return MediaVerdict.Nothing;
        var folder = tree[folderId];
        if (!folder.IsDirectory || Listing.Of(folder) is not (ListingState.Open or ListingState.Partial)) return MediaVerdict.Nothing;
        var (pictures, videos, files, marker) = Count(tree, folder, visible);
        MediaVerdict V(MediaCharacter c, MediaReason r) => new(c, r, pictures, videos, files);
        if (marker) return V(MediaCharacter.Gallery, MediaReason.Marker);
        if (pictures == 0) return V(MediaCharacter.None, MediaReason.None);
        var share = (pictures + Math.Min(videos, pictures)) / (float)Math.Max(1, files);
        if (pictures >= MinPicturesByContent && share >= ContentShare) return V(MediaCharacter.Gallery, MediaReason.MostlyPictures);
        if (tree.Source.IsLive && share >= LibraryShare && libraries.Contains(tree.FullPath(folderId))) return V(MediaCharacter.Gallery, MediaReason.PictureLibrary);
        if (share >= InsideShare && !folder.IsRoot && tree.Contains(folder.ParentId) && IsGalleryByItself(tree, tree[folder.ParentId], visible))
            return V(MediaCharacter.Gallery, MediaReason.InsideGallery);
        return V(MediaCharacter.SomePictures, MediaReason.None);
    }

    /// <summary>A parent that is a gallery by its marker or its contents (not by inheritance: one level only).</summary>
    private static bool IsGalleryByItself(FsTree tree, FsNode folder, Func<FsNode, bool>? visible)
    {
        if (!folder.IsDirectory || Listing.Of(folder) is not (ListingState.Open or ListingState.Partial)) return false;
        var (pictures, videos, files, marker) = Count(tree, folder, visible);
        return marker || (pictures >= MinPicturesByContent && (pictures + Math.Min(videos, pictures)) / (float)Math.Max(1, files) >= ContentShare);
    }

    private static (int Pictures, int Videos, int Files, bool Marker) Count(FsTree tree, FsNode folder, Func<FsNode, bool>? visible)
    {
        int pictures = 0, videos = 0, files = 0;
        var marker = false;
        foreach (var c in folder.Children)
        {
            var n = tree[c];
            if (n.Kind != NodeKind.File) continue;
            if (n.Name.Equals(GalleryRules.MarkerName, StringComparison.Ordinal)) { marker = true; continue; }
            if (visible != null && !visible(n)) continue;
            files++;
            if (GalleryRules.IsPicture(n.Name)) pictures++;
            else if (FileCategories.Of(n.Name) == FileCategory.Video) videos++;
        }

        return (pictures, videos, files, marker);
    }
}
