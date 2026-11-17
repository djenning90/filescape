using FileScape.Core.Model;
using FileScape.Core.Shell;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// Reading on a planet (PLANET-44, AD-57): a Markdown file's preview is rendered (headings, lists, code, tables,
/// emphasis; links as text, images as their alt text), any other text file is shown as plain text, and either can be
/// expanded (the preview's "Read ⤢" button, or R while it is open) into the HUD's full reading view: wide, scrolling,
/// closed by Esc, bounded to <see cref="Readme.MaxBytes"/>. Everything is read locally, off the main thread, and nothing
/// is fetched (remote images in a README stay alt text) or followed.
/// <para>
/// The preview panel's label switches between BBCode (Markdown) and plain text per file: a plain text file is never
/// parsed as markup, and a Markdown file's own brackets are escaped by <see cref="MarkdownText"/>.
/// </para>
/// </summary>
public partial class PlanetWorld
{
    /// <summary>The preview's body size, pixels (the panel is narrow; the reading view uses the HUD's 15 px).</summary>
    private const int PreviewFontPixels = 13;

    /// <summary>The file whose text the preview shows now (−1: none), for the expand button and R.</summary>
    private int _previewTextId = -1;

    private Button _expandButton = null!;

    /// <summary>True for the extensions rendered as Markdown (the same set as <see cref="Readme.Read"/>'s "markdown").</summary>
    public static bool IsMarkdown(string name) => System.IO.Path.GetExtension(name).ToLowerInvariant() is ".md" or ".markdown";

    /// <summary>The preview shows text that can be expanded (probes).</summary>
    public bool PreviewExpandable => _previewPanel.Visible && _previewTextId >= 0;

    /// <summary>The preview is rendering Markdown (probes).</summary>
    public bool PreviewIsMarkdown => _previewText.BbcodeEnabled;

    /// <summary>The preview's text as shown (markup applied, tags gone; probes).</summary>
    public string PreviewShownText => _previewText.GetParsedText();

    /// <summary>The expand button, built with the preview panel's button row (BuildPreviewPanel adds it).</summary>
    private Button MakeExpandButton()
    {
        _expandButton = new Button { Text = "Read ⤢ (R)", FocusMode = Control.FocusModeEnum.None, Visible = false, TooltipText = "Read the whole file in a large view (Esc closes it)." };
        _expandButton.Pressed += () => ExpandPreview();
        return _expandButton;
    }

    /// <summary>Shows a text preview's result: Markdown as BBCode, anything else plain; then offers the expanded view.</summary>
    private void PreviewText(int id, string name, string text)
    {
        if (!_previewPanel.Visible || _s.Selection.Selected != id) return;
        _previewText.Visible = true;
        _previewText.BbcodeEnabled = IsMarkdown(name);
        _previewText.Text = _previewText.BbcodeEnabled ? Readme.ToBbcode(text, markdown: true, PreviewFontPixels) : text;
        _previewText.ScrollToLine(0);
        _previewTextId = id;
        _expandButton.Visible = true;
    }

    /// <summary>Plain status text in the preview (loading, "no preview"…): never markup, never expandable.</summary>
    private void PreviewPlain(string text)
    {
        _previewText.BbcodeEnabled = false;
        _previewText.Text = text;
        _previewTextId = -1;
        _expandButton.Visible = false;
    }

    /// <summary>
    /// The expanded reading view for the previewed text file (the button or R): the whole file up to
    /// <see cref="Readme.MaxBytes"/> / <see cref="Readme.MaxLines"/>, read on a worker and shown in the HUD's reading view
    /// from the top. False when there is nothing to expand (R then falls through to the project's README).
    /// </summary>
    public bool ExpandPreview()
    {
        if (!PreviewExpandable || _s.Tree is not { } tree || !tree.Contains(_previewTextId)) return false;
        var n = tree[_previewTextId];
        if (n.Kind != NodeKind.File) return false;
        var path = tree.FullPath(n.Id);
        var title = $"{tree.DisplayName(n.ParentId >= 0 ? n.ParentId : n.Id)} · {n.Name}";
        var markdown = IsMarkdown(n.Name);
        _ = Task.Run(() => Readme.Read(path)).ContinueWith(t => Deliver(() =>
        {
            var r = t.Result;
            var body = r.Error != null ? $"[color=#ffb070]{r.Error.Replace("[", "[lb]")}[/color]" : Readme.ToBbcode(r.Text, markdown);
            var footer = (markdown ? "Markdown" : "plain text") + " · read here, locally: links are not followed, images not loaded, nothing is run · Esc closes"
                + (r.Truncated ? $" · only the first {Readme.MaxBytes / 1024} KB / {Readme.MaxLines:N0} lines" : "");
            _s.Hud.ShowReadme(title, body, footer);
            ReadingViews++;
            GD.Print($"[planets] reading view: {n.Name} ({r.Text.Length} chars, {(markdown ? "markdown" : "plain")}, truncated={r.Truncated})");
        }));
        _audio.Inspect();
        return true;
    }

    /// <summary>Reading views opened this session (probes).</summary>
    public int ReadingViews { get; private set; }
}
