using FileScape.Core.Interaction;
using FileScape.Core.Model;
using Godot;

namespace FileScape.App.Visual;

/// <summary>The type silhouettes FileScape draws (ICON-01): familiar shapes, so a type never has to be learnt from a colour.</summary>
public enum IconKind
{
    Folder,
    Document,
    Image,
    Audio,
    Video,
    Archive,
    Code,
    Program,
    Data,
    Other,
    Link,
    Up,
    Drive,
    Linux,
    Hub,
}

/// <summary>
/// Original, simple type icons (ICON-01, AD-24): white silhouettes on a 24-unit grid, drawn for FileScape (no third-party
/// artwork), rasterized at start-up from SVG text (Godot's built-in SVG rasterizer) into one atlas texture. White means
/// the caller tints them: in the HUD with the label colour, in the world with the type's atmospheric colour.
/// <para>
/// This is the first stage of a file's appearance (KD-15): <see cref="For(FsNode)"/> decides the silhouette from the
/// node's kind and category only. A later preview provider (thumbnails, first pages) would replace the silhouette for
/// one file where a preview exists; it would never be fed by the metadata scan (FS-01).
/// </para>
/// </summary>
public static class FileIcons
{
    /// <summary>Atlas cell size in pixels: crisp at the 0.9 m world badge from close by and at 28 px in the HUD.</summary>
    public const int Cell = 128;

    private const string Ink = "#1b2333";

    private static readonly Dictionary<IconKind, string> Paths = new()
    {
        [IconKind.Folder] = "<path d='M2 6.5A1.5 1.5 0 0 1 3.5 5h5.2l2.1 2.3h9.7A1.5 1.5 0 0 1 22 8.8v9.7a1.5 1.5 0 0 1-1.5 1.5h-17A1.5 1.5 0 0 1 2 18.5z' fill='#fff'/><path d='M2 10h20' stroke='" + Ink + "' stroke-opacity='.35' stroke-width='1.2'/>",
        [IconKind.Document] = "<path d='M6 2.5h8.2l4.8 4.8V21a.6.6 0 0 1-.6.6H6a.6.6 0 0 1-.6-.6V3.1a.6.6 0 0 1 .6-.6z' fill='#fff'/><path d='M14.2 2.5v4.8H19' fill='none' stroke='" + Ink + "' stroke-opacity='.45' stroke-width='1.1'/><path d='M8.2 11.2h7.6M8.2 14.3h7.6M8.2 17.4h4.8' stroke='" + Ink + "' stroke-width='1.5' stroke-linecap='round'/>",
        [IconKind.Image] = "<rect x='2.5' y='4.5' width='19' height='15' rx='2' fill='#fff'/><path d='M4.6 17.4l5-6.1 3.6 4.2 2.4-2.5 3.9 4.4z' fill='" + Ink + "'/><circle cx='16.4' cy='8.8' r='1.8' fill='" + Ink + "'/>",
        [IconKind.Audio] = "<path d='M9 17.6V5.6l11-2.2v12' fill='none' stroke='#fff' stroke-width='2.2' stroke-linejoin='round'/><circle cx='6.4' cy='17.6' r='3.1' fill='#fff'/><circle cx='17.4' cy='15.4' r='3.1' fill='#fff'/>",
        [IconKind.Video] = "<rect x='2.5' y='5' width='14.5' height='14' rx='2' fill='#fff'/><path d='M17.6 10.2l4-2.6v8.8l-4-2.6z' fill='#fff'/><path d='M7.6 8.8l5.2 3.2-5.2 3.2z' fill='" + Ink + "'/>",
        [IconKind.Archive] = "<rect x='3.2' y='7.6' width='17.6' height='12.6' rx='1.5' fill='#fff'/><rect x='2' y='3.6' width='20' height='4.6' rx='1' fill='#fff'/><path d='M12 9.2v1.8m0 1.6v1.8m0 1.6v1.8' stroke='" + Ink + "' stroke-width='2.1'/>",
        [IconKind.Code] = "<path d='M8 6.8L3 12l5 5.2M16 6.8l5 5.2-5 5.2M13.6 4.8l-3.2 14.4' fill='none' stroke='#fff' stroke-width='2.3' stroke-linecap='round' stroke-linejoin='round'/>",
        [IconKind.Program] = "<rect x='2.5' y='3.5' width='19' height='17' rx='2' fill='#fff'/><rect x='4' y='7.6' width='16' height='11.4' rx='1' fill='" + Ink + "'/><path d='M10 10.4l5.2 2.9-5.2 2.9z' fill='#fff'/><circle cx='5.3' cy='5.5' r='.85' fill='" + Ink + "'/><circle cx='7.8' cy='5.5' r='.85' fill='" + Ink + "'/>",
        [IconKind.Data] = "<ellipse cx='12' cy='5.6' rx='8' ry='3' fill='#fff'/><path d='M4 5.6v12.8c0 1.7 3.6 3.1 8 3.1s8-1.4 8-3.1V5.6c0 1.7-3.6 3.1-8 3.1S4 7.3 4 5.6z' fill='#fff'/><path d='M4 10.2c0 1.7 3.6 3.1 8 3.1s8-1.4 8-3.1M4 14.6c0 1.7 3.6 3.1 8 3.1s8-1.4 8-3.1' fill='none' stroke='" + Ink + "' stroke-width='1.3'/>",
        [IconKind.Other] = "<path d='M6 2.5h8.2l4.8 4.8V21a.6.6 0 0 1-.6.6H6a.6.6 0 0 1-.6-.6V3.1a.6.6 0 0 1 .6-.6z' fill='#fff'/><path d='M14.2 2.5v4.8H19' fill='none' stroke='" + Ink + "' stroke-opacity='.45' stroke-width='1.1'/><circle cx='9' cy='14.5' r='1.2' fill='" + Ink + "'/><circle cx='12.2' cy='14.5' r='1.2' fill='" + Ink + "'/><circle cx='15.4' cy='14.5' r='1.2' fill='" + Ink + "'/>",
        [IconKind.Link] = "<path d='M4.5 19c0-6.4 3.9-9.6 10.2-9.6h2.8' fill='none' stroke='#fff' stroke-width='2.6' stroke-linecap='round'/><path d='M15.4 4.6l6 4.8-6 4.8z' fill='#fff'/>",
        [IconKind.Up] = "<path d='M12 3.5l9.5 16H2.5z' fill='#fff'/>",
        [IconKind.Drive] = "<rect x='2.5' y='7.5' width='19' height='10' rx='2' fill='#fff'/><circle cx='17.6' cy='12.5' r='1.3' fill='" + Ink + "'/><path d='M5.6 12.5h7.4' stroke='" + Ink + "' stroke-width='1.5' stroke-linecap='round'/>",
        [IconKind.Linux] = "<rect x='2.5' y='4' width='19' height='16' rx='2' fill='#fff'/><path d='M6 9l3.6 3L6 15M11.2 15.6h6.4' stroke='" + Ink + "' stroke-width='1.9' fill='none' stroke-linecap='round' stroke-linejoin='round'/>",
        [IconKind.Hub] = "<rect x='3' y='4' width='18' height='12' rx='1.5' fill='#fff'/><rect x='5' y='6' width='14' height='8' fill='" + Ink + "'/><path d='M9 20h6M12 16v4' stroke='#fff' stroke-width='2' stroke-linecap='round'/>",
    };

    private static Texture2D? _atlas;
    private static readonly Dictionary<IconKind, Texture2D> Singles = [];

    /// <summary>All icons side by side, <see cref="Cell"/> pixels each, in <see cref="IconKind"/> order (world badges sample it).</summary>
    public static Texture2D Atlas => _atlas ??= BuildAtlas();

    public static int Count => Paths.Count;

    /// <summary>One icon as its own texture (HUD lists and the inspector).</summary>
    public static Texture2D Get(IconKind kind)
    {
        if (Singles.TryGetValue(kind, out var t)) return t;
        t = ImageTexture.CreateFromImage(Rasterize(kind, 48));
        Singles[kind] = t;
        return t;
    }

    public static IconKind For(FileCategory c) => c switch
    {
        FileCategory.Document => IconKind.Document,
        FileCategory.Image => IconKind.Image,
        FileCategory.Audio => IconKind.Audio,
        FileCategory.Video => IconKind.Video,
        FileCategory.Archive => IconKind.Archive,
        FileCategory.Code => IconKind.Code,
        FileCategory.Executable => IconKind.Program,
        FileCategory.Data => IconKind.Data,
        _ => IconKind.Other,
    };

    /// <summary>The silhouette for a node: its kind (folder, link, drive, WSL, hub) first, then a file's category.</summary>
    public static IconKind For(FsNode n) => n.Kind switch
    {
        NodeKind.Link => IconKind.Link,
        NodeKind.Directory => n.Location switch
        {
            LocationKind.Hub => IconKind.Hub,
            LocationKind.Drive or LocationKind.Network or LocationKind.Volume => IconKind.Drive,
            LocationKind.Wsl => IconKind.Linux,
            _ => IconKind.Folder,
        },
        _ => For(FileCategories.Of(n.Name)),
    };

    private static Image Rasterize(IconKind kind, int pixels)
    {
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'>{Paths[kind]}</svg>";
        var img = new Image();
        var err = img.LoadSvgFromString(svg, pixels / 24f);
        if (err != Error.Ok || img.IsEmpty())
        {
            GD.PrintErr($"[icons] could not rasterize {kind}: {err}");
            img = Image.CreateEmpty(pixels, pixels, false, Image.Format.Rgba8);
        }

        img.Convert(Image.Format.Rgba8);
        return img;
    }

    private static Texture2D BuildAtlas()
    {
        var atlas = Image.CreateEmpty(Cell * Count, Cell, false, Image.Format.Rgba8);
        foreach (var kind in Enum.GetValues<IconKind>())
        {
            var img = Rasterize(kind, Cell);
            atlas.BlitRect(img, new Rect2I(0, 0, Cell, Cell), new Vector2I((int)kind * Cell, 0));
        }

        atlas.GenerateMipmaps();
        return ImageTexture.CreateFromImage(atlas);
    }
}
