using FileScape.Core.Model;
using FileScape.Core.Shell;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// Reading on a planet (PLANET-44, PLANET-52/53, AD-57, AD-62): a Markdown file's preview is rendered (headings, lists,
/// code, tables, emphasis; links usable, images as their alt text), any other text file is shown as plain text. The
/// preview is an excerpt (the first <see cref="PreviewTextBytes"/>); the Expand icon in its upper-right corner (or R)
/// replaces it with the HUD's reading view of the whole file (bounded to <see cref="Readme.MaxBytes"/>), and closing
/// the reader brings the preview back while you are still on that file. Everything is read locally, off the main thread;
/// nothing is fetched (remote images stay alt text), and a link goes somewhere only when it is clicked (Main.Reader).
/// <para>
/// The preview panel's label switches between BBCode (Markdown) and plain text per file: a plain text file is never
/// parsed as markup, and a Markdown file's own brackets are escaped by <see cref="MarkdownText"/>.
/// </para>
/// Preserve: the Expand icon is visible whenever the preview shows text that can be read in full, and R does the same.
/// </summary>
public partial class PlanetWorld
{
    /// <summary>The preview's body size, pixels (the panel is narrow; the reading view uses the HUD's 15 px).</summary>
    private const int PreviewFontPixels = 13;

    /// <summary>The Expand button's text size, pixels: the icon and its word read at a glance in the corner (by eye, 2026-11-17).</summary>
    private const int ExpandIconPixels = 15;

    /// <summary>The file whose text the preview shows now (−1: none), for the expand button and R.</summary>
    private int _previewTextId = -1;

    /// <summary>The preview's file, hidden while the reading view shows it (−1: none): closing the reader brings the preview back.</summary>
    private int _previewHiddenForReader = -1;

    /// <summary>The preview's links (a Markdown excerpt), by the index in their [url] tags.</summary>
    private IReadOnlyList<MarkdownLink> _previewLinks = [];

    private Button _expandButton = null!;
    private bool _readerHooked;

    /// <summary>True for the extensions rendered as Markdown (the same set as <see cref="Readme.Read"/>'s "markdown").</summary>
    public static bool IsMarkdown(string name) => System.IO.Path.GetExtension(name).ToLowerInvariant() is ".md" or ".markdown";

    /// <summary>The preview shows text that can be expanded (probes).</summary>
    public bool PreviewExpandable => _previewPanel.Visible && _previewTextId >= 0;

    /// <summary>The preview is rendering Markdown (probes).</summary>
    public bool PreviewIsMarkdown => _previewText.BbcodeEnabled;

    /// <summary>The preview's text as shown (markup applied, tags gone; probes).</summary>
    public string PreviewShownText => _previewText.GetParsedText();

    /// <summary>The Expand icon, in the preview's title row (BuildPreviewPanel places it in the upper-right corner).</summary>
    private Button MakeExpandButton()
    {
        _expandButton = new Button { Text = "⤢ Expand (R)", FocusMode = Control.FocusModeEnum.None, Visible = false, TooltipText = "Expand: read the whole file in the large reading view (R). Esc or R closes it." };
        _expandButton.AddThemeFontSizeOverride("font_size", ExpandIconPixels);
        _expandButton.Pressed += () =>
        {
            if (!ExpandPreview()) OpenViewer(); // a text file reads in the reading view; a picture opens the picture viewer (PLANET-96)
        };
        return _expandButton;
    }

    /// <summary>Shows a text preview's result: Markdown as BBCode with usable links, anything else plain; then offers the
    /// reading view. <paramref name="excerpt"/>: the file goes on past what is shown.</summary>
    private void PreviewText(int id, string name, string text, bool excerpt)
    {
        if (!_previewPanel.Visible || _s.Selection.Selected != id) return;
        _previewText.Visible = true;
        _previewText.BbcodeEnabled = IsMarkdown(name);
        if (_previewText.BbcodeEnabled)
        {
            var doc = MarkdownText.Render(text, PreviewFontPixels);
            _previewLinks = doc.Links;
            _previewText.Text = doc.Bbcode + (excerpt ? ExcerptNote : "");
        }
        else
        {
            _previewLinks = [];
            _previewText.Text = text + (excerpt ? "\n…" : "");
        }

        _previewText.ScrollToLine(0);
        _previewTextId = id;
        _expandButton.Visible = true;
    }

    /// <summary>The line under a Markdown excerpt that goes on in the file.</summary>
    private const string ExcerptNote = "\n[color=#8899aa][i]… an excerpt: ⤢ (or R) reads the whole file[/i][/color]\n";

    /// <summary>Plain status text in the preview (loading, "no preview"…): never markup, never expandable.</summary>
    private void PreviewPlain(string text)
    {
        _previewText.BbcodeEnabled = false;
        _previewText.Text = text;
        _previewTextId = -1;
        _previewLinks = [];
        _expandButton.Visible = false;
    }

    /// <summary>
    /// The reading view for the previewed text file (the Expand icon or R): the whole file up to
    /// <see cref="Readme.MaxBytes"/> / <see cref="Readme.MaxLines"/>, read on a worker by the session and shown in the
    /// HUD's reading view, which replaces the preview (at the heading <paramref name="anchor"/> if given). False when there
    /// is nothing to expand (R then falls through to the project's README).
    /// </summary>
    public bool ExpandPreview(string? anchor = null)
    {
        if (!PreviewExpandable || _s.Tree is not { } tree || !tree.Contains(_previewTextId)) return false;
        var n = tree[_previewTextId];
        if (n.Kind != NodeKind.File) return false;
        if (!_readerHooked)
        {
            _readerHooked = true;
            _s.Hud.ReaderClosed += OnReaderClosed;
        }

        var path = tree.FullPath(n.Id);
        var title = $"{tree.DisplayName(n.ParentId >= 0 ? n.ParentId : n.Id)} · {n.Name}";
        _s.ShowDocument(path, title, anchor);
        _previewHiddenForReader = n.Id;
        _previewPanel.Visible = false; // replaced by the reading view (the sound preview, if any, is not touched: text has none)
        ReadingViews++;
        GD.Print($"[planets] reading view: {n.Name} ({(IsMarkdown(n.Name) ? "markdown" : "plain")})");
        _audio.Inspect();
        return true;
    }

    /// <summary>The reading view closed: the preview comes back if you are still on its file and nothing else took its place.</summary>
    private void OnReaderClosed()
    {
        var id = _previewHiddenForReader;
        _previewHiddenForReader = -1;
        if (id < 0 || !_active || _universe != null || _s.Selection.Selected != id || _contentsPanel.Visible || Busy) return;
        _previewPanel.Visible = true;
    }

    /// <summary>A link clicked in the preview's excerpt: an anchor opens the reading view at that heading; anything else is followed as from the reader.</summary>
    private void OnPreviewLink(string meta)
    {
        if (!int.TryParse(meta, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var i) || i < 0 || i >= _previewLinks.Count) return;
        var link = _previewLinks[i];
        if (_s.Tree is not { } tree || !tree.Contains(_previewTextId)) return;
        var path = tree.FullPath(_previewTextId);
        var target = MarkdownLinks.Resolve(link.Target, path);
        if (target.Kind == MarkdownLinkKind.Anchor) ExpandPreview(target.Fragment);
        else _s.FollowLink(path, link);
    }

    /// <summary>Probe access: the same as clicking link <paramref name="index"/> in the preview.</summary>
    public void ProbeClickPreviewLink(int index) => OnPreviewLink(index.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Where <paramref name="text"/> is in the preview, viewport units (probes click links there), or null when not in view.</summary>
    public Vector2? PreviewTextPoint(string text) => Ui.Hud.RichTextPoint(_previewText, text, PreviewFontPixels);

    /// <summary>Reading views opened from a preview this session (probes).</summary>
    public int ReadingViews { get; private set; }
}
