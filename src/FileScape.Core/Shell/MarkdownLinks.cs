using System.Text;
using System.Text.RegularExpressions;

namespace FileScape.Core.Shell;

/// <summary>A link as written in a Markdown file: its address (<see cref="Target"/>) and its text.</summary>
public sealed record MarkdownLink(string Target, string Text);

/// <summary>
/// A Markdown file rendered with usable links (<see cref="MarkdownText.Render"/>): the BBCode, whose <c>[url=N]</c> tags
/// index <see cref="Links"/>, and each heading's anchor (GitHub's slug) with the paragraph it starts.
/// </summary>
public sealed record MarkdownDocument(string Bbcode, IReadOnlyList<MarkdownLink> Links, IReadOnlyDictionary<string, int> Anchors);

/// <summary>What following a link means (PLANET-53).</summary>
public enum MarkdownLinkKind
{
    /// <summary>A heading in the same document (<c>#section</c>).</summary>
    Anchor,

    /// <summary>A file or folder on disk, relative to the document (or its project root for a leading '/').</summary>
    Local,

    /// <summary>An http, https or mailto address: the browser or mail program, on a deliberate click.</summary>
    Web,

    /// <summary>Anything else (javascript:, file:, a protocol-relative //host…): not followed, and the reason is said.</summary>
    Refused,
}

/// <summary>A link resolved against its document: the kind, the full local path or web address, and the #fragment (decoded).</summary>
public sealed record LinkTarget(MarkdownLinkKind Kind, string Address, string? Fragment, string? Reason = null);

/// <summary>
/// The rules for Markdown links in FileScape's reader (PLANET-53, AD-62), pure so they are tested without Godot:
/// GitHub's heading anchors (<see cref="Slug"/>) and what a clicked address means (<see cref="Resolve"/>). Nothing here
/// touches the disk or the network; the reader checks a local target exists only when the link is clicked.
/// Preserve: only http, https and mailto ever count as web addresses (everything else with a scheme is refused), and a
/// relative path never climbs above the document's drive or share.
/// </summary>
public static class MarkdownLinks
{
    private static readonly Regex Scheme = new(@"^([a-zA-Z][a-zA-Z0-9+.\-]*):", RegexOptions.Compiled);

    /// <summary>True for the addresses that open in the browser or mail program (http, https, mailto).</summary>
    public static bool IsWeb(string target) =>
        Scheme.Match(target.Trim()) is { Success: true } m && m.Groups[1].Value.ToLowerInvariant() is "http" or "https" or "mailto";

    /// <summary>
    /// GitHub's anchor for a heading: the text as shown (code spans, emphasis and link syntax removed), lower case,
    /// punctuation dropped except '-' and '_', each space a '-'. Duplicates are numbered by the renderer.
    /// </summary>
    public static string Slug(string heading)
    {
        var t = Regex.Replace(heading, @"!?\[([^\]]*)\]\([^)]*\)", "$1"); // [text](url) and images → their text
        t = t.Replace("`", "").Replace("*", "").Replace("~~", "");
        t = Regex.Replace(t, @"(?<![\w])_+|_+(?![\w])", ""); // _emphasis_ markers, not snake_case
        var sb = new StringBuilder();
        foreach (var ch in t.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_') sb.Append(ch);
            else if (ch == ' ') sb.Append('-');
        }

        return sb.ToString();
    }

    /// <summary>
    /// What clicking <paramref name="target"/> in the document at <paramref name="documentPath"/> means. A relative path
    /// is percent-decoded (<c>getting%20started.md</c>), may be written with spaces (<c>&lt;getting started.md&gt;</c> is
    /// already unwrapped by the renderer), loses any <c>?query</c>, and is joined to the document's folder with the
    /// document's own separator; a leading '/' starts at <paramref name="projectRoot"/> (GitHub's repository root) when
    /// known. '..' climbs, but never above the drive or share.
    /// </summary>
    public static LinkTarget Resolve(string target, string documentPath, string? projectRoot = null)
    {
        var t = target.Trim();
        if (t.StartsWith('#')) return new LinkTarget(MarkdownLinkKind.Anchor, "", Decode(t[1..]));
        if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith(@"\\", StringComparison.Ordinal))
            return new LinkTarget(MarkdownLinkKind.Refused, t, null, "a link to another computer is not followed from a document");
        if (Scheme.Match(t) is { Success: true } m && m.Groups[1].Length > 1)
        {
            return IsWeb(t)
                ? new LinkTarget(MarkdownLinkKind.Web, t, null)
                : new LinkTarget(MarkdownLinkKind.Refused, t, null, $"FileScape does not follow {m.Groups[1].Value.ToLowerInvariant()}: links");
        }

        string? fragment = null;
        var hash = t.IndexOf('#');
        if (hash >= 0)
        {
            fragment = Decode(t[(hash + 1)..]);
            t = t[..hash];
        }

        var query = t.IndexOf('?');
        if (query >= 0) t = t[..query];
        t = Decode(t);
        if (t.Length == 0) return new LinkTarget(MarkdownLinkKind.Anchor, "", fragment); // "#x" written as "?a#x" or "" with a fragment

        var sep = documentPath.Contains('\\') ? '\\' : '/';
        var (prefix, parts) = Split(documentPath);
        if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); // the document's folder
        if (t.Length >= 2 && t[1] == ':')
        {
            // An absolute Windows path in a document (C:\…): taken as written.
            (prefix, parts) = Split(t);
            sep = '\\';
            t = "";
        }
        else if (t.StartsWith('/') || t.StartsWith('\\'))
        {
            // GitHub reads a leading '/' as the repository's root; without one known, the drive or share (or Linux's /).
            if (projectRoot != null) (prefix, parts) = Split(projectRoot);
            else parts.Clear();
        }

        foreach (var seg in t.Split('/', '\\'))
        {
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(seg);
        }

        var path = prefix + string.Join(sep, parts);
        if (prefix.Length == 0 && sep == '/') path = "/" + path;
        return new LinkTarget(MarkdownLinkKind.Local, path, fragment);
    }

    /// <summary>A path's root that '..' cannot climb above (<c>C:\</c>, <c>\\server\share\</c>, or "" for a Linux path) and its folders.</summary>
    private static (string Prefix, List<string> Parts) Split(string path)
    {
        var sep = path.Contains('\\') ? '\\' : '/';
        string prefix;
        string rest;
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // \\server\share\… (and \\wsl.localhost\Distro\…): the server and share are the root.
            var segs = path[2..].Split('\\');
            var take = Math.Min(2, segs.Length);
            prefix = @"\\" + string.Join('\\', segs.Take(take)) + "\\";
            rest = string.Join('\\', segs.Skip(take));
        }
        else if (path.Length >= 2 && path[1] == ':')
        {
            prefix = path[..2] + sep;
            rest = path[2..];
        }
        else
        {
            prefix = "";
            rest = path;
        }

        return (prefix, rest.Split('/', '\\').Where(s => s.Length > 0).ToList());
    }

    /// <summary>Percent-decoding as a browser does for a path (%20 → space); a malformed escape is kept as written.</summary>
    private static string Decode(string s)
    {
        try
        {
            return Uri.UnescapeDataString(s);
        }
        catch (UriFormatException)
        {
            return s;
        }
    }

    /// <summary>The text that says where a link goes, for the reader's status line ("↗ opens … in your browser").</summary>
    public static string Describe(LinkTarget t) => t.Kind switch
    {
        MarkdownLinkKind.Anchor => t.Fragment is { Length: > 0 } f ? $"jumps to “{f}” in this document" : "the top of this document",
        MarkdownLinkKind.Web => t.Address.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? $"↗ writes to {t.Address[7..]} with your mail program" : $"↗ opens {t.Address} in your browser",
        MarkdownLinkKind.Local => t.Address + (t.Fragment is { Length: > 0 } f2 ? " #" + f2 : ""),
        _ => $"not followed: {t.Reason}",
    };
}
