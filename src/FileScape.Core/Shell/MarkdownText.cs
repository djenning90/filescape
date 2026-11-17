using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FileScape.Core.Shell;

/// <summary>
/// Markdown → Godot BBCode for FileScape's read-only text views (PLANET-44, AD-57; DEV-02): the README viewer, the
/// planets' preview panel and its expanded reading view. It renders the common GitHub-flavoured blocks — ATX and setext
/// headings, paragraphs (soft line breaks joined), fenced and indented code blocks, block quotes, unordered, ordered and
/// task lists with nesting, tables and thematic breaks — and the inline forms: code spans, strong, emphasis,
/// strikethrough, links and images.
/// <para>
/// Safety, which every change must keep (DEV-02): the output contains only this converter's own tags. Every '[' from the
/// file becomes <c>[lb]</c>, so nothing in a file can become markup. <see cref="ToBbcode"/> keeps links as text (their
/// address shown after them, muted, never followed); <see cref="Render"/> (PLANET-53, AD-62) makes them usable as
/// <c>[url=N]</c> tags holding only an index into its link table, so a click is decided by the view and no address is
/// ever markup. Images become their alt text and are never fetched, remote or local; HTML is shown as written. It is pure
/// and deterministic, so the rendering is tested in Core without Godot.
/// </para>
/// </summary>
public static class MarkdownText
{
    // ---- the look (by eye, 2026-11-16, on a 15 px body; sizes scale with the base size passed in)
    /// <summary>Heading sizes above the body size, pixels: # +11, ## +7, ### +4, #### and deeper +2 (a 15 px body: 26, 22, 19, 17).</summary>
    private static readonly int[] HeadingExtraPixels = [11, 7, 4, 2, 2, 2];

    /// <summary>The dark slate behind code blocks; the muted grey-blue of link addresses, image notes and rules; the quote colour.</summary>
    private const string CodeBackground = "#1c2333", MutedColor = "#8899aa", QuoteColor = "#b4c0d8", LinkColor = "#9cc8ff";

    /// <summary>Bullets by nesting depth (the deepest repeats).</summary>
    private static readonly string[] Bullets = ["•", "◦", "▪"];

    /// <summary>A thematic break is drawn as this many box-drawing dashes (a line that reads as a rule at any width).</summary>
    private const int RuleDashes = 40;

    /// <summary>Spaces a nesting level is worth when a list item's indent is measured (CommonMark allows 2–4; 2 is the usual).</summary>
    private const int SpacesPerLevel = 2;

    private static readonly Regex Fence = new(@"^ {0,3}(`{3,}|~{3,})", RegexOptions.Compiled);
    private static readonly Regex Atx = new(@"^ {0,3}(#{1,6})(?:[ \t]+(.*?))?(?:[ \t]+#+)?[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex Rule = new(@"^ {0,3}((\*[ \t]*){3,}|(-[ \t]*){3,}|(_[ \t]*){3,})$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^(\s*)([-*+])[ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Ordered = new(@"^(\s*)(\d{1,9})([.)])[ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Task = new(@"^\[([ xX])\][ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex TableDivider = new(@"^\s*\|?\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)*\|?\s*$", RegexOptions.Compiled);

    /// <summary>Plain text for the same views: every '[' escaped, nothing else changed (line breaks kept).</summary>
    public static string PlainToBbcode(string text) => Escape(text);

    /// <summary>The whole document as BBCode. <paramref name="baseFontSize"/> is the view's body size, pixels (headings scale from it).
    /// Links stay text here (the address shown after them): the inert form, for views that do not follow links.</summary>
    public static string ToBbcode(string text, int baseFontSize = 15) => Convert(text, baseFontSize, null);

    /// <summary>
    /// The document with its links usable (PLANET-53, AD-62): each link becomes <c>[url=N]…[/url]</c>, where N is only the
    /// link's index in <see cref="MarkdownDocument.Links"/> (nothing from the file is ever inside a tag), and every heading's
    /// GitHub-style anchor is listed with the paragraph it starts (<see cref="MarkdownDocument.Anchors"/>). What a click does
    /// is decided by the view (<see cref="MarkdownLinks.Resolve"/>): nothing here follows or fetches anything.
    /// </summary>
    public static MarkdownDocument Render(string text, int baseFontSize = 15)
    {
        var ctx = new RenderContext();
        var bb = Convert(text, baseFontSize, ctx);
        return new MarkdownDocument(bb, ctx.Links, ctx.Anchors);
    }

    /// <summary>What a linkable render collects as it goes: the links in order and the headings' anchors.</summary>
    private sealed class RenderContext
    {
        public readonly List<MarkdownLink> Links = [];
        public readonly Dictionary<string, int> Anchors = new(StringComparer.Ordinal);

        /// <summary>Headings inside a block quote are rendered by a nested pass whose paragraphs are not the document's: no anchors there.</summary>
        public bool Nested;

        // The top-level paragraph count of the output so far, counted incrementally (see Paragraph).
        private int _scanned, _paragraphs, _tableDepth;

        /// <summary>
        /// The index of the paragraph that starts at the end of <paramref name="sb"/>, as Godot's RichTextLabel counts them
        /// (ScrollToParagraph): one per line break outside a table, because a table's cells are frames of their own.
        /// </summary>
        public int Paragraph(StringBuilder sb)
        {
            for (; _scanned < sb.Length; _scanned++)
            {
                var c = sb[_scanned];
                if (c == '\n' && _tableDepth == 0) _paragraphs++;
                else if (c == '[' && Matches(sb, _scanned, "[table")) _tableDepth++;
                else if (c == '[' && Matches(sb, _scanned, "[/table]")) _tableDepth--;
            }

            return _paragraphs;
        }

        private static bool Matches(StringBuilder sb, int at, string s)
        {
            if (at + s.Length > sb.Length) return false;
            for (var i = 0; i < s.Length; i++) if (sb[at + i] != s[i]) return false;
            return true;
        }
    }

    private static string Convert(string text, int baseFontSize, RenderContext? ctx)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\u0001", "").Replace("\u0002", "").Split('\n');
        var sb = new StringBuilder();
        var para = new List<string>();
        var blank = true; // the last block ended with a blank line (or the document just began)

        void FlushPara()
        {
            if (para.Count == 0) return;
            Gap();
            sb.Append(Inline(string.Join(' ', para.Select(l => l.Trim())), ctx)).Append('\n');
            para.Clear();
        }

        void Gap()
        {
            // One empty line between blocks (never two, never at the very top).
            if (sb.Length > 0 && !(sb.Length >= 2 && sb[^1] == '\n' && sb[^2] == '\n')) sb.Append('\n');
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0)
            {
                FlushPara();
                blank = true;
                continue;
            }

            // Fenced code: ``` or ~~~ (an info string after it is ignored), closed by the same fence or the end of the file.
            if (Fence.Match(line) is { Success: true } fm)
            {
                FlushPara();
                var marker = fm.Groups[1].Value;
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith(marker, StringComparison.Ordinal); i++) code.Add(lines[i]);
                Gap();
                CodeBlock(sb, code);
                blank = false;
                continue;
            }

            // Indented code: four spaces after a blank line, outside a paragraph and a list.
            if (para.Count == 0 && blank && line.StartsWith("    ", StringComparison.Ordinal) && !Bullet.IsMatch(line) && !Ordered.IsMatch(line))
            {
                var code = new List<string>();
                for (; i < lines.Length && (lines[i].StartsWith("    ", StringComparison.Ordinal) || lines[i].Trim().Length == 0); i++) code.Add(lines[i].Length >= 4 ? lines[i][4..] : "");
                i--;
                while (code.Count > 0 && code[^1].Trim().Length == 0) code.RemoveAt(code.Count - 1);
                Gap();
                CodeBlock(sb, code);
                blank = false;
                continue;
            }

            if (Atx.Match(line) is { Success: true } h)
            {
                FlushPara();
                Gap();
                Heading(sb, h.Groups[1].Length, h.Groups[2].Value, baseFontSize, ctx);
                blank = false;
                continue;
            }

            // Setext: a paragraph line underlined with === (level 1) or --- (level 2).
            if (para.Count > 0 && Regex.IsMatch(line, @"^ {0,3}(=+|-+)\s*$"))
            {
                var text1 = string.Join(' ', para.Select(l => l.Trim()));
                para.Clear();
                Gap();
                Heading(sb, line.TrimStart()[0] == '=' ? 1 : 2, text1, baseFontSize, ctx);
                blank = false;
                continue;
            }

            if (Rule.IsMatch(line))
            {
                FlushPara();
                Gap();
                sb.Append($"[color={MutedColor}]").Append(new string('─', RuleDashes)).Append("[/color]\n");
                blank = false;
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                FlushPara();
                var quote = new List<string>();
                for (; i < lines.Length && lines[i].TrimStart().StartsWith('>'); i++)
                {
                    var q = lines[i].TrimStart()[1..];
                    quote.Add(q.StartsWith(' ') ? q[1..] : q);
                }

                i--;
                Gap();
                // A quote's links join the document's (the same context, so their numbers run on); its headings get no anchors.
                var wasNested = ctx?.Nested ?? false;
                if (ctx != null) ctx.Nested = true;
                sb.Append($"[indent][color={QuoteColor}][i]").Append(Convert(string.Join('\n', quote), baseFontSize, ctx).TrimEnd('\n')).Append("[/i][/color][/indent]\n");
                if (ctx != null) ctx.Nested = wasNested;
                blank = false;
                continue;
            }

            var bm = Bullet.Match(line);
            var om = Ordered.Match(line);
            if ((bm.Success || om.Success) && (para.Count == 0 || blank || bm.Success || om.Success))
            {
                FlushPara();
                if (blank) Gap();
                var indent = (bm.Success ? bm.Groups[1].Value : om.Groups[1].Value).Replace("\t", "    ").Length;
                var level = Math.Min(indent / SpacesPerLevel, 6);
                var body = bm.Success ? bm.Groups[3].Value : om.Groups[4].Value;
                // Lazy continuation: following lines indented past the marker (not new items, not blank) belong to it.
                while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0 && !Bullet.IsMatch(lines[i + 1]) && !Ordered.IsMatch(lines[i + 1])
                       && (lines[i + 1].StartsWith(' ') || lines[i + 1].StartsWith('\t')) && !Fence.IsMatch(lines[i + 1]))
                {
                    body += " " + lines[++i].Trim();
                }

                string mark;
                if (bm.Success && Task.Match(body) is { Success: true } tm)
                {
                    mark = tm.Groups[1].Value == " " ? "☐" : "☑";
                    body = tm.Groups[2].Value;
                }
                else mark = bm.Success ? Bullets[Math.Min(level, Bullets.Length - 1)] : om.Groups[2].Value + om.Groups[3].Value;

                for (var k = 0; k < level; k++) sb.Append("[indent]");
                sb.Append(' ').Append(mark).Append(' ').Append(Inline(body, ctx));
                for (var k = 0; k < level; k++) sb.Append("[/indent]");
                sb.Append('\n');
                blank = false;
                continue;
            }

            // A table: a header row, then a divider row (|---|:--:|), then rows.
            if (line.Contains('|') && i + 1 < lines.Length && TableDivider.IsMatch(lines[i + 1]) && lines[i + 1].Contains('-'))
            {
                FlushPara();
                var rows = new List<string[]> { Cells(line) };
                for (i += 2; i < lines.Length && lines[i].Contains('|') && lines[i].Trim().Length > 0; i++) rows.Add(Cells(lines[i]));
                i--;
                Gap();
                Table(sb, rows, ctx);
                blank = false;
                continue;
            }

            para.Add(line);
            blank = false;
        }

        FlushPara();
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    private static void Heading(StringBuilder sb, int level, string text, int baseFontSize, RenderContext? ctx)
    {
        if (ctx is { Nested: false })
        {
            // GitHub's anchor for the heading; a repeated one gets -1, -2… as GitHub numbers them.
            var slug = MarkdownLinks.Slug(text);
            var unique = slug;
            for (var n = 1; ctx.Anchors.ContainsKey(unique); n++) unique = slug + "-" + n.ToString(CultureInfo.InvariantCulture);
            ctx.Anchors[unique] = ctx.Paragraph(sb);
        }

        var size = baseFontSize + HeadingExtraPixels[Math.Clamp(level, 1, 6) - 1];
        sb.Append(CultureInfo.InvariantCulture, $"[font_size={size}][b]").Append(Inline(text.Trim(), ctx)).Append("[/b][/font_size]\n");
    }

    /// <summary>A code block on its slate panel: a one-cell table, so the background spans the block, not just the glyphs.</summary>
    private static void CodeBlock(StringBuilder sb, List<string> code)
    {
        sb.Append($"[table=1][cell bg={CodeBackground},{CodeBackground} padding=10,6,10,6][code]")
            .Append(Escape(string.Join('\n', code)))
            .Append("[/code][/cell][/table]\n");
    }

    /// <summary>A table's cells from one row (outer pipes optional; "\|" is a literal pipe).</summary>
    private static string[] Cells(string row)
    {
        var t = row.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|') && !t.EndsWith("\\|", StringComparison.Ordinal)) t = t[..^1];
        return Regex.Split(t, @"(?<!\\)\|").Select(c => c.Trim().Replace("\\|", "|")).ToArray();
    }

    private static void Table(StringBuilder sb, List<string[]> rows, RenderContext? ctx)
    {
        var columns = rows.Max(r => r.Length);
        sb.Append(CultureInfo.InvariantCulture, $"[table={columns}]");
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var cell = c < rows[r].Length ? Inline(rows[r][c], ctx) : "";
                sb.Append("[cell padding=6,2,12,2]").Append(r == 0 ? $"[b]{cell}[/b]" : cell).Append("[/cell]");
            }
        }

        sb.Append("[/table]\n");
    }

    /// <summary>A usable link: its index is the only thing in the tag; a web address is marked ↗ after the text.</summary>
    private static string LinkTag(RenderContext ctx, string url, string text, string label)
    {
        ctx.Links.Add(new MarkdownLink(url, text));
        var index = (ctx.Links.Count - 1).ToString(CultureInfo.InvariantCulture);
        var web = MarkdownLinks.IsWeb(url) ? $" [color={MutedColor}]↗[/color]" : "";
        return $"[url={index}][color={LinkColor}][u]{label}[/u][/color][/url]{web}";
    }

    /// <summary>Escapes a literal: '[' → [lb] (the only character BBCode reads as markup).</summary>
    private static string Escape(string s) => s.Replace("[", "[lb]");

    /// <summary>Strikethrough, strong and emphasis on already escaped text (underscores inside words stay literal).</summary>
    private static string Emphasis(string s)
    {
        s = Regex.Replace(s, @"~~(?=\S)(.+?)(?<=\S)~~", "[s]$1[/s]");
        s = Regex.Replace(s, @"\*\*(?=\S)(.+?)(?<=\S)\*\*", "[b]$1[/b]");
        s = Regex.Replace(s, @"(?<![\w])__(?=\S)(.+?)(?<=\S)__(?![\w])", "[b]$1[/b]");
        s = Regex.Replace(s, @"(?<![\*\w])\*(?=[^\s*])(.+?)(?<=[^\s*])\*(?![\*\w])", "[i]$1[/i]");
        return Regex.Replace(s, @"(?<![\w])_(?=[^\s_])(.+?)(?<=[^\s_])_(?![\w])", "[i]$1[/i]");
    }

    /// <summary>
    /// Inline Markdown on raw text: code spans, images, links and autolinks are cut out first (their contents stay
    /// literal), the rest is escaped, then strong, emphasis and strikethrough are applied, then the cut-outs go back.
    /// </summary>
    public static string Inline(string raw) => Inline(raw, null);

    private static string Inline(string raw, RenderContext? ctx)
    {
        var held = new List<string>();
        string Hold(string bbcode)
        {
            held.Add(bbcode);
            return "\u0001" + (held.Count - 1).ToString(CultureInfo.InvariantCulture) + "\u0002";
        }

        // Backslash escapes: the punctuation itself, literally.
        var s = Regex.Replace(raw, @"\\([\\`*_{}\[\]()#+\-.!|~<>])", m => Hold(Escape(m.Groups[1].Value)));
        s = Regex.Replace(s, @"(`+)(.+?)\1", m => Hold($"[code]{Escape(m.Groups[2].Value.Trim())}[/code]"));
        // Images: the alt text only (never fetched, remote or local).
        s = Regex.Replace(s, @"!\[([^\]]*)\]\(([^)]*)\)", m => Hold($"[i][color={MutedColor}](image: {Escape(m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : "no description")})[/color][/i]"));
        // Links: the text, underlined. Inert: then the address, muted (never followed); a link whose text is its address shows
        // once. Linkable: the text is the link ([url=N], N its index), a web address marked ↗ (it leaves FileScape).
        // The address may be written <with spaces> (CommonMark); an optional "title" is dropped.
        s = Regex.Replace(s, @"\[([^\]]+)\]\(\s*(?:<([^>\n]*)>|([^)\s]*))(?:\s+""[^""]*"")?\s*\)", m =>
        {
            var text = m.Groups[1].Value;
            var url = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            var label = Emphasis(Escape(text)); // the label may hold earlier cut-outs (a code span): restored with the rest below
            if (ctx != null && url.Length > 0) return Hold(LinkTag(ctx, url, text, label));
            return Hold(text == url || url.Length == 0 ? $"[color={LinkColor}][u]{label}[/u][/color]" : $"[color={LinkColor}][u]{label}[/u][/color] [color={MutedColor}]‹{Escape(url)}›[/color]");
        });
        s = Regex.Replace(s, @"<((?:https?|ftp|mailto):[^>\s]+)>", m => Hold(ctx != null
            ? LinkTag(ctx, m.Groups[1].Value, m.Groups[1].Value, Escape(m.Groups[1].Value))
            : $"[color={LinkColor}][u]{Escape(m.Groups[1].Value)}[/u][/color]"));
        s = Emphasis(Escape(s));
        // Restore the cut-outs (a link's label can itself hold cut-outs, so repeat until none are left).
        for (var pass = 0; pass < 4 && s.Contains('\u0001'); pass++)
        {
            s = Regex.Replace(s, "\u0001(\\d+)\u0002", m => held[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]);
        }

        return s;
    }
}
