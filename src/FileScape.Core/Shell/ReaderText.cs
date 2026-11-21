using System.Globalization;
using FileScape.Core.Interaction;

namespace FileScape.Core.Shell;

/// <summary>
/// What the reading view says around a document (PLANET-52, PLANET-152): the body when a file could not be read (the
/// OS's message, literal), the note where a long file is cut, and the footer line. The document itself is
/// <see cref="MarkdownText"/>'s. Pure, so Main.Reader's text is tested in Core.
/// Preserve: the OS message is literal text (it can quote a path with brackets in it); the limits named are
/// <see cref="Readme"/>'s own.
/// </summary>
public static class ReaderText
{
    /// <summary>An error's colour (the reader's warm orange) and the cut note's (a paler gold).</summary>
    private const string ErrorColor = "#ffb070", CutColor = "#ffcf80";

    /// <summary>The body shown instead of a file that could not be read.</summary>
    public static string ErrorBody(string osMessage) => $"[color={ErrorColor}]{UiText.Escape(osMessage)}[/color]";

    /// <summary>The note at the end of a file cut at <see cref="Readme.MaxBytes"/> or <see cref="Readme.MaxLines"/>.</summary>
    public static string CutNote =>
        string.Create(CultureInfo.InvariantCulture, $"\n[color={CutColor}][b]— The file continues. The reader shows the first {Readme.MaxBytes / 1024} KB or {Readme.MaxLines:N0} lines; Open with Windows (Enter) shows all of it. —[/b][/color]\n");

    /// <summary>The footer: what kind of reading this is and what the keys do (a plain Label shows it).</summary>
    public static string Footer(bool markdown, bool truncated) =>
        (markdown ? "Markdown" : "plain text") + " · read here, locally: links go only where you click, images are not loaded, nothing is run · Enter opens it with Windows · Esc closes"
        + (truncated ? string.Create(CultureInfo.InvariantCulture, $" · shortened: the first {Readme.MaxBytes / 1024} KB / {Readme.MaxLines:N0} lines") : "");
}
