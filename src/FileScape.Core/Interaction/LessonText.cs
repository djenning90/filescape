using System.Text;

namespace FileScape.Core.Interaction;

/// <summary>
/// The Learn to play card's text (PLANET-94, AD-78): turns a lesson written in a tiny markup into BBCode for the card's
/// RichTextLabel, so the steps are written as plain sentences and the look is decided in one place. Markup:
/// <c>*the action*</c> is the step's current action (bold, warm gold); <c>{Space}</c> is a key cap (the key's name on a
/// dark lime-edged cap, as the beacon's colour); everything else is plain text and is escaped, so a lesson can never
/// produce a tag of its own. <see cref="Plain"/> gives the same text without markup (the state file, probes, a screen
/// reader's view).
/// Preserve: the output's only tags are the ones written here (b, color, bgcolor, font_size); an unmatched <c>*</c> or
/// <c>{</c> is shown literally; a key cap never breaks across lines (its spaces are non-breaking).
/// </summary>
public static class LessonText
{
    /// <summary>The emphasised action's colour (the card title's warm gold).</summary>
    public const string ActionColor = "#ffd873";

    /// <summary>A key cap's letters and its background: the beacon's lime on a dark green, the card's existing key-cap look
    /// (dark caps with a lime edge) drawn inline.</summary>
    public const string CapColor = "#bdf26b", CapBackground = "#2e4720";

    /// <summary>A key cap's font size relative to the line, pixels at UI scale 1: slightly smaller than the 18 px text so the
    /// cap's background sits inside the line.</summary>
    public const int CapFontPixels = 16;

    /// <summary>The space padding each side of a key's name inside its cap (a no-break space, so the cap stays whole).</summary>
    private const string CapPad = " ";

    /// <summary>The lesson as BBCode (see the class summary for the markup).</summary>
    public static string ToBbcode(string lesson) => Convert(lesson, bbcode: true);

    /// <summary>The lesson as plain text: actions unmarked, key caps as their names.</summary>
    public static string Plain(string lesson) => Convert(lesson, bbcode: false);

    /// <summary>The key names written as caps in <paramref name="lesson"/>, in order.</summary>
    public static IReadOnlyList<string> Keys(string lesson)
    {
        var keys = new List<string>();
        for (var i = 0; i < lesson.Length; i++)
        {
            if (lesson[i] != '{') continue;
            var end = lesson.IndexOf('}', i + 1);
            if (end < 0) break;
            keys.Add(lesson[(i + 1)..end]);
            i = end;
        }

        return keys;
    }

    private static string Convert(string lesson, bool bbcode)
    {
        var sb = new StringBuilder(lesson.Length * 2);
        var inAction = false;
        for (var i = 0; i < lesson.Length; i++)
        {
            var c = lesson[i];
            if (c == '{' && lesson.IndexOf('}', i + 1) is var end and > 0)
            {
                var key = lesson[(i + 1)..end];
                if (bbcode)
                {
                    var name = Escape(key).Replace(" ", " ");
                    sb.Append($"[bgcolor={CapBackground}][color={CapColor}][font_size={CapFontPixels}]{CapPad}{name}{CapPad}[/font_size][/color][/bgcolor]");
                }
                else
                {
                    sb.Append(key);
                }

                i = end;
                continue;
            }

            if (c == '*' && (inAction || lesson.IndexOf('*', i + 1) > i))
            {
                inAction = !inAction;
                if (bbcode) sb.Append(inAction ? $"[b][color={ActionColor}]" : "[/color][/b]");
                continue;
            }

            sb.Append(bbcode && c == '[' ? "[lb]" : c.ToString());
        }

        if (inAction && bbcode) sb.Append("[/color][/b]");
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("[", "[lb]");
}
