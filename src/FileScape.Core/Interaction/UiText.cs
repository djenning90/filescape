using System.Globalization;
using System.Text;

namespace FileScape.Core.Interaction;

/// <summary>
/// One look for a family of text surfaces (PLANET-151, AD-93): the body size the label is set to and the sizes of the
/// key caps and headings in it, pixels at UI scale 1. The colours are shared by every style (<see cref="UiText"/>), so
/// emphasis, caps, headings and quiet text read the same in Help, the Learn to play card, the on-screen prompts and the
/// panels; only the sizes follow the surface.
/// </summary>
/// <param name="BodyPixels">The label's normal font size (what the surface sets on its RichTextLabel).</param>
/// <param name="CapPixels">A key cap's letters: a little smaller than the line, so the cap's background sits inside it.</param>
/// <param name="HeadingPixels">A <c># heading</c> line.</param>
/// <param name="SubheadingPixels">A <c>## subheading</c> line.</param>
public sealed record UiTextStyle(int BodyPixels, int CapPixels, int HeadingPixels, int SubheadingPixels)
{
    /// <summary>The Learn to play card: an 18 px action line with 16 px caps (PLANET-94, by eye 2026-11-19).</summary>
    public static readonly UiTextStyle Lesson = new(18, 16, 22, 20);

    /// <summary>Help and other reading panels on the HUD's 15 px body (the theme's default size).</summary>
    public static readonly UiTextStyle Panel = new(15, 13, 20, 17);

    /// <summary>The planets' prompt line over the world (21 px, outlined; PlanetWorld.Overlay).</summary>
    public static readonly UiTextStyle Prompt = new(21, 18, 21, 21);

    /// <summary>The prompt's quiet second line and other small print (14 px).</summary>
    public static readonly UiTextStyle Small = new(14, 12, 16, 15);
}

/// <summary>
/// Interface text, formatted the same way everywhere (PLANET-151..153, AD-93): a tiny authored markup turned into Godot
/// BBCode for a RichTextLabel, and the same text plain (probes, logs, the state file, a screen reader's view).
/// <para>
/// Markup, for text written in FileScape's source: <c>{Key}</c> is a key cap (the key's name on a dark lime-edged cap,
/// as the beacon's colour; it never breaks across lines); <c>*words*</c> are emphasised (bold, warm gold: "the action");
/// <c>~words~</c> are quiet (the panels' muted blue-grey); a line starting <c># </c> or <c>## </c> is a heading or a
/// subheading; a line starting <c>- </c> is a bullet. A backslash makes the next character literal. An unmatched
/// <c>{</c>, <c>*</c> or <c>~</c> is shown as written.
/// </para>
/// <para>
/// Literal text — every file and folder name, path, OS message and file content — goes in through <see cref="Lit"/>
/// (inside markup) or <see cref="Escape"/> (straight into BBCode), never pasted in raw: then a name such as
/// <c>[b]x</c>, <c>{Space}</c> or <c>*draft*</c> is shown exactly as it is named.
/// </para>
/// Preserve: the BBCode's only tags are the ones written here (b, color, bgcolor, font_size); nothing from a literal can
/// become a tag or markup (LiteralTextTests); <see cref="Plain"/> of a markup string is exactly what the label shows.
/// </summary>
public static class UiText
{
    // ---- the shared look (the Learn to play card's, 2026-11-19, now every surface's)
    /// <summary>Emphasis and headings: the warm gold of the card titles and the Learn to play button.</summary>
    public const string StrongColor = "#ffd873";

    /// <summary>A key cap's letters and its background: the beacon's lime on a dark green.</summary>
    public const string CapColor = "#bdf26b", CapBackground = "#2e4720";

    /// <summary>Quiet text (notes, build line, labels in a card): the HUD's muted blue-grey.</summary>
    public const string MutedColor = "#8fa6c8";

    /// <summary>The padding each side of a key's name inside its cap: a no-break space, so the cap stays whole.</summary>
    private const string CapPad = " ";

    /// <summary>The bullet a <c>- </c> line starts with, after this indent.</summary>
    private const string BulletPrefix = "  • ";

    /// <summary>The characters the markup reads (escaped by <see cref="Lit"/>).</summary>
    private const string MarkupChars = "\\{}*~#-";

    /// <summary>Escapes literal text for BBCode: '[' becomes <c>[lb]</c> (the only character BBCode reads as markup).</summary>
    public static string Escape(string literal) => literal.Replace("[", "[lb]", StringComparison.Ordinal);

    /// <summary>Escapes literal text for this markup (a backslash before each markup character), so it is shown as written
    /// when the markup is rendered. BBCode escaping happens at rendering.</summary>
    public static string Lit(string literal)
    {
        var sb = new StringBuilder(literal.Length + 4);
        foreach (var c in literal)
        {
            if (MarkupChars.Contains(c)) sb.Append('\\');
            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>A key cap in markup, for a key name built at run time (it is escaped).</summary>
    public static string Key(string name) => "{" + Lit(name) + "}";

    /// <summary>
    /// Shortens a long name for a one-line surface by cutting its middle (the start and the extension are what tell names
    /// apart): at most <paramref name="maxChars"/> characters, an ellipsis in the cut. Short names are returned unchanged.
    /// </summary>
    public static string Shorten(string name, int maxChars)
    {
        if (maxChars < 3 || name.Length <= maxChars) return name;
        var keep = maxChars - 1;
        var tail = keep / 3;
        return name[..(keep - tail)] + "…" + name[^tail..];
    }

    /// <summary>The markup as BBCode in <paramref name="style"/>'s sizes (see the class summary).</summary>
    public static string ToBbcode(string markup, UiTextStyle style) => Convert(markup, style);

    /// <summary>The markup as plain text: marks removed, caps as their key names, escapes resolved.</summary>
    public static string Plain(string markup) => Convert(markup, null);

    /// <summary>The key names written as caps in <paramref name="markup"/>, in order.</summary>
    public static IReadOnlyList<string> Keys(string markup)
    {
        var keys = new List<string>();
        foreach (var line in markup.Split('\n'))
        {
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == '\\') { i++; continue; }
                if (line[i] != '{' || Closing(line, i + 1, '}') is not (var end and >= 0)) continue;
                keys.Add(Unescape(line[(i + 1)..end]));
                i = end;
            }
        }

        return keys;
    }

    private static string Convert(string markup, UiTextStyle? style)
    {
        var sb = new StringBuilder(markup.Length * 2);
        var lines = markup.Replace("\r\n", "\n").Split('\n');
        for (var n = 0; n < lines.Length; n++)
        {
            if (n > 0) sb.Append('\n');
            var line = lines[n];
            if (line.StartsWith("## ", StringComparison.Ordinal)) Block(sb, line[3..], style, style?.SubheadingPixels);
            else if (line.StartsWith("# ", StringComparison.Ordinal)) Block(sb, line[2..], style, style?.HeadingPixels);
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                sb.Append(BulletPrefix);
                Inline(sb, line[2..], style);
            }
            else Inline(sb, line, style);
        }

        return sb.ToString();
    }

    /// <summary>A heading line: bold, gold, at <paramref name="pixels"/>.</summary>
    private static void Block(StringBuilder sb, string text, UiTextStyle? style, int? pixels)
    {
        if (style == null)
        {
            Inline(sb, text, null);
            return;
        }

        sb.Append(CultureInfo.InvariantCulture, $"[font_size={pixels}][b][color={StrongColor}]");
        Inline(sb, text, style);
        sb.Append("[/color][/b][/font_size]");
    }

    /// <summary>One line's inline marks: escapes, caps, emphasis and quiet text (each closed by the end of the line).</summary>
    private static void Inline(StringBuilder sb, string line, UiTextStyle? style)
    {
        var bb = style != null;
        bool strong = false, muted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\' && i + 1 < line.Length)
            {
                Literal(sb, line[++i], bb);
                continue;
            }

            if (c == '{' && Closing(line, i + 1, '}') is var end and >= 0)
            {
                var name = Unescape(line[(i + 1)..end]);
                if (bb)
                {
                    var shown = Escape(name).Replace(" ", " ", StringComparison.Ordinal);
                    sb.Append(CultureInfo.InvariantCulture, $"[bgcolor={CapBackground}][color={CapColor}][font_size={style!.CapPixels}]{CapPad}{shown}{CapPad}[/font_size][/color][/bgcolor]");
                }
                else sb.Append(name);

                i = end;
                continue;
            }

            if (c == '*' && (strong || Closing(line, i + 1, '*') >= 0))
            {
                strong = !strong;
                if (bb) sb.Append(strong ? $"[b][color={StrongColor}]" : "[/color][/b]");
                continue;
            }

            if (c == '~' && (muted || Closing(line, i + 1, '~') >= 0))
            {
                muted = !muted;
                if (bb) sb.Append(muted ? $"[color={MutedColor}]" : "[/color]");
                continue;
            }

            Literal(sb, c, bb);
        }

        // Marks are closed in the reverse order of opening when the line ends (only reachable with an escaped closer).
        if (bb && muted) sb.Append("[/color]");
        if (bb && strong) sb.Append("[/color][/b]");
    }

    private static void Literal(StringBuilder sb, char c, bool bb)
    {
        if (bb && c == '[') sb.Append("[lb]");
        else sb.Append(c);
    }

    /// <summary>The index of the next unescaped <paramref name="mark"/> at or after <paramref name="from"/>, or −1.</summary>
    private static int Closing(string line, int from, char mark)
    {
        for (var i = from; i < line.Length; i++)
        {
            if (line[i] == '\\') i++;
            else if (line[i] == mark) return i;
        }

        return -1;
    }

    private static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length) i++;
            sb.Append(s[i]);
        }

        return sb.ToString();
    }
}
