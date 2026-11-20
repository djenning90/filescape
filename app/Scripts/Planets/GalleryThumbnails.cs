using FileScape.Core.Model;
using FileScape.Core.Planets;
using FileScape.Core.Shell;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// Where the gallery's pictures come from (PLANET-131, AD-86): one service for the easels, the preview and the viewer.
/// A request goes, in order, to the session's cache of decoded thumbnails (<see cref="Cache"/>, bounded by
/// <see cref="CacheBytes"/>), to Windows' thumbnail cache and handlers (<see cref="ShellThumbnails"/>: the same
/// thumbnails Explorer shows, and for a cloud-only photo the provider's thumbnail without downloading the file), and only
/// then to FileScape's own bounded decoder (<see cref="Thumbnails"/>) for a picture it decodes and may read
/// (<see cref="GalleryRules.Verdict"/>). Each answer says where it came from and how long it took, for the state file
/// and the measurements in AD-86.
/// <para>
/// Preserve: results reach Godot objects only on the main thread (callers queue them; the <see cref="Image"/> is built on
/// the worker, which Godot allows for an object nothing else holds yet); nothing here writes to a file or changes its
/// attributes; a link is never read; a file over <see cref="GalleryRules.MaxPictureBytes"/> is never read whole.
/// </para>
/// </summary>
public static class GalleryThumbnails
{
    /// <summary>Decoded thumbnails kept for the session, bytes (64 MiB: ≈ 190 small ones, or a planet's 64 small and 8
    /// sharp ones twice over). A world you come back to fills from here in a frame or two.</summary>
    public const long CacheBytes = 64L * 1024 * 1024;

    /// <summary>The session's cache: key "path|pixels|modified ticks", value the decoded image (main thread only).</summary>
    public static readonly ByteBudgetCache<string, Image> Cache = new(CacheBytes);

    /// <summary>A thumbnail or why there is none, where it came from ("cache", "windows-cache", "windows", "decoded")
    /// and the picture's own size when known (0 when Windows did not say).</summary>
    public readonly record struct Answer(Image? Image, string Source, int SourceWidth, int SourceHeight, string? Refusal, double Milliseconds);

    /// <summary>Answers per source this session, and every request's time (ms) per source: what AD-86 reports.</summary>
    public static readonly Dictionary<string, List<double>> Times = [];

    private static string Key(FsTree tree, FsNode n, int pixels) => $"{tree.FullPath(n.Id)}|{pixels}|{n.ModifiedUtc?.Ticks ?? 0}|{n.Size}";

    /// <summary>The cached thumbnail of <paramref name="n"/> at <paramref name="pixels"/>, if any (main thread).</summary>
    public static Image? Cached(FsTree tree, FsNode n, int pixels) => Cache.TryGet(Key(tree, n, pixels), out var img) ? img : null;

    /// <summary>Keeps an answer for the session (main thread), counted at its uncompressed size with mipmaps.</summary>
    public static void Keep(FsTree tree, FsNode n, int pixels, Image img) => Cache.Put(Key(tree, n, pixels), img, (long)img.GetWidth() * img.GetHeight() * 4 * 4 / 3);

    /// <summary>
    /// Starts reading <paramref name="n"/> at <paramref name="pixels"/> on a worker (call from the main thread with the
    /// node's current metadata). <paramref name="decode"/>: whether FileScape may decode the file itself when Windows has
    /// no thumbnail (the viewer always asks for a decode separately).
    /// </summary>
    public static Task<Answer> RequestAsync(FsTree tree, FsNode n, int pixels, bool decode = true)
    {
        var path = tree.FullPath(n.Id);
        var verdict = GalleryRules.Verdict(n);
        if (verdict == GalleryRules.PictureVerdict.Link) return Task.FromResult(new Answer(null, "refused", 0, 0, "it is a link, and FileScape does not read through links", 0));
        var mayDecode = decode && verdict is GalleryRules.PictureVerdict.Viewable or GalleryRules.PictureVerdict.Download;
        return Task.Run(async () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (await ShellThumbnails.GetAsync(path, pixels).ConfigureAwait(false) is { } shell)
            {
                var img = Image.CreateFromData(shell.Width, shell.Height, false, Image.Format.Rgba8, shell.Rgba);
                img.GenerateMipmaps();
                return Note(new Answer(img, shell.FromCache ? "windows-cache" : "windows", 0, 0, null, sw.Elapsed.TotalMilliseconds));
            }

            if (!mayDecode)
            {
                var why = verdict switch
                {
                    GalleryRules.PictureVerdict.TooLarge => $"it is {FileScape.Core.Interaction.SizeFormat.Short(n.Size)} and Windows has no thumbnail of it",
                    GalleryRules.PictureVerdict.Download => "it is only in the cloud and Windows has no thumbnail of it yet (E fetches it)",
                    _ => "Windows has no thumbnail of it, and FileScape does not decode this kind of picture",
                };
                return Note(new Answer(null, "none", 0, 0, why, sw.Elapsed.TotalMilliseconds));
            }

            var r = await Thumbnails.LoadWithReasonAsync(path, pixels, GalleryRules.MaxPictureBytes).ConfigureAwait(false);
            return Note(new Answer(r.Image, r.Image != null ? "decoded" : "none", r.SourceWidth, r.SourceHeight, r.Refusal, sw.Elapsed.TotalMilliseconds));
        });
    }

    private static Answer Note(Answer a)
    {
        lock (Times)
        {
            if (!Times.TryGetValue(a.Source, out var list)) Times[a.Source] = list = [];
            list.Add(a.Milliseconds);
        }

        return a;
    }

    /// <summary>One line per source: count, median and 95th percentile ms (state file and logs).</summary>
    public static string Summary()
    {
        lock (Times)
        {
            return string.Join("; ", Times.OrderBy(kv => kv.Key).Select(kv =>
            {
                var t = kv.Value.OrderBy(x => x).ToList();
                return System.FormattableString.Invariant($"{kv.Key} {t.Count} (median {t[t.Count / 2]:0} ms, p95 {t[Math.Min(t.Count - 1, (int)(t.Count * 0.95))]:0} ms)");
            }));
        }
    }
}
