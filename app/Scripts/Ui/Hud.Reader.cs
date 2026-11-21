using FileScape.Core.Shell;
using Godot;

namespace FileScape.App.Ui;

/// <summary>A document for the reading view: what it shows, where it came from (to open it with Windows and resolve its
/// links), and its links and heading anchors when it was rendered with usable links (<see cref="MarkdownText.Render"/>).</summary>
public sealed record ReaderDocument(string Title, string Bbcode, string Footer, string? Path = null,
    IReadOnlyList<MarkdownLink>? Links = null, IReadOnlyDictionary<string, int>? Anchors = null);

/// <summary>
/// The reading view (DEV-02, PLANET-44, PLANET-52/53, AD-62): a large opaque panel over the world that shows a text or
/// Markdown file Main has already read and rendered. It owns the pointer and the keyboard while it is open — the wheel
/// and clicks over it reach it, never the camera (it is one of <see cref="BlockingRects"/>), its text takes keyboard
/// focus so the arrows and Page keys scroll it — and its toasts are drawn above it. A link click is decided here for an
/// in-document anchor (it scrolls to the heading) and handed to Main for anything else (<see cref="ReaderLinkClicked"/>);
/// "Open with Windows" asks Main too (<see cref="ReaderOpenRequested"/>). A status line under the text says where a link
/// goes while the pointer is on it, and what happened when one was followed or the file opened.
/// Preserve: nothing here reads a file, follows a link or starts a program; the BBCode's only meta tags are link indices
/// (<see cref="MarkdownText.Render"/>), so a document cannot make the view do anything a click does not ask for.
/// </summary>
public partial class Hud
{
    /// <summary>A link other than an in-document anchor was clicked in the reading view (Main resolves and follows it).</summary>
    public event Action<ReaderDocument, MarkdownLink>? ReaderLinkClicked;

    /// <summary>"Open with Windows" (or Enter) in the reading view, for the document's path.</summary>
    public event Action<string>? ReaderOpenRequested;

    /// <summary>The reading view closed (Esc, Close, or a link that went elsewhere): the small preview may come back.</summary>
    public event Action? ReaderClosed;

    private PanelContainer _readmePanel = null!;
    private Label _readmeTitle = null!, _readmeFooter = null!, _readmeStatus = null!;
    private RichTextLabel _readmeText = null!;
    private Button _readmeClose = null!, _readmeOpen = null!;
    private ReaderDocument? _reader;

    /// <summary>A heading to scroll to once the new text is laid out (RichTextLabel lays out after the text is set).</summary>
    private string? _pendingAnchor;

    /// <summary>The reading view's body sizes, pixels: text, bold and code (by eye, 2026-11-16).</summary>
    private const int ReaderFontPixels = 15, ReaderMonoPixels = 14;

    private static readonly Color ReaderStatusColor = new(0.75f, 0.85f, 1f), ReaderStatusBadColor = new(1f, 0.62f, 0.5f);

    public bool ReadmeVisible => _readmePanel.Visible;

    /// <summary>Test and probe access: the README text as shown (BBCode stripped by the label).</summary>
    public string ReadmeShownText => _readmeText.GetParsedText();

    /// <summary>The document shown (null before the first; kept after closing until the next).</summary>
    public ReaderDocument? Reader => _reader;

    /// <summary>The status line's text: where a link goes, or what following one did (probes).</summary>
    public string ReaderStatusText => _readmeStatus.Text;

    /// <summary>The reading view: a nearly full-window opaque panel (inset 120 at the sides, below the top bar), hidden until <see cref="ShowReader"/>.</summary>
    private void BuildReadmeViewer()
    {
        _readmePanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _readmePanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _readmePanel.OffsetLeft = 120; _readmePanel.OffsetRight = -120; _readmePanel.OffsetTop = 80; _readmePanel.OffsetBottom = -60;
        var style = new StyleBoxFlat { BgColor = new Color(0.06f, 0.07f, 0.11f, 1f), BorderColor = new Color(0.4f, 0.48f, 0.7f), ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 12 };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(6);
        _readmePanel.AddThemeStyleboxOverride("panel", style);
        _root.AddChild(_readmePanel);
        AddBlocking(_readmePanel); // PLANET-52: the wheel and clicks over it are the reader's, never the camera's
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        _readmePanel.AddChild(v);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        _readmeTitle = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _readmeTitle.AddThemeFontSizeOverride("font_size", 18);
        head.AddChild(_readmeTitle);
        // AD-94: leaving FileScape for Windows' app looks like leaving (↗, the cool outline), apart from reading here.
        _readmeOpen = LeaveButton.Make("Open with Windows ↗ (Enter)", "Leaves FileScape: opens this file with the app Windows uses for it.");
        _readmeOpen.Pressed += RequestReaderOpen;
        head.AddChild(_readmeOpen);
        _readmeClose = MakeButton("Close (Esc)", HideReadme, "Close the reading view.");
        head.AddChild(_readmeClose);
        v.AddChild(head);
        _readmeText = new RichTextLabel
        {
            BbcodeEnabled = true,
            SelectionEnabled = true,
            ScrollActive = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.All, // the arrows, Page Up/Down, Home and End scroll it
            MetaUnderlined = false, // the renderer underlines links itself
        };
        _readmeText.AddThemeFontSizeOverride("normal_font_size", ReaderFontPixels);
        _readmeText.AddThemeFontSizeOverride("bold_font_size", ReaderFontPixels);
        _readmeText.AddThemeFontSizeOverride("mono_font_size", ReaderMonoPixels);
        _readmeText.AddThemeStyleboxOverride("focus", new StyleBoxEmpty()); // the panel's border already says where the keyboard is
        _readmeText.MetaClicked += meta => OnReaderMeta(meta.AsString());
        _readmeText.MetaHoverStarted += meta => { if (LinkAt(meta.AsString()) is { } l) SetReaderStatus(DescribeLink(l), bad: false); };
        _readmeText.MetaHoverEnded += _ => { if (!_statusSticky) _readmeStatus.Text = ""; };
        v.AddChild(_readmeText);
        _readmeStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _readmeStatus.AddThemeFontSizeOverride("font_size", 13);
        _readmeStatus.AddThemeColorOverride("font_color", ReaderStatusColor);
        v.AddChild(_readmeStatus);
        _readmeFooter = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _readmeFooter.AddThemeFontSizeOverride("font_size", 12);
        _readmeFooter.AddThemeColorOverride("font_color", new Color(0.65f, 0.72f, 0.86f));
        v.AddChild(_readmeFooter);
    }

    /// <summary>Shows a README Main has already read and converted to BBCode, without usable links (the Places project card's README).</summary>
    public void ShowReadme(string title, string bbcode, string footer) => ShowReader(new ReaderDocument(title, bbcode, footer));

    /// <summary>
    /// Shows <paramref name="doc"/> from the top (or at the heading <paramref name="anchor"/>), above every other panel
    /// and below the toasts, with the keyboard in its text.
    /// </summary>
    public void ShowReader(ReaderDocument doc, string? anchor = null)
    {
        _reader = doc;
        FitReadingWidth();
        _readmeTitle.Text = doc.Title;
        _readmeText.Text = doc.Bbcode;
        _readmeText.ScrollToLine(0);
        _readmeFooter.Text = doc.Footer;
        _readmeOpen.Visible = doc.Path != null;
        _statusSticky = false;
        _readmeStatus.Text = "";
        _readmePanel.Visible = true;
        _readmePanel.MoveToFront();
        _toastPanel.MoveToFront(); // PLANET-52: what Open or a link did is never hidden behind the reader
        _readmeText.GrabFocus();
        _pendingAnchor = string.IsNullOrEmpty(anchor) ? null : anchor;
    }

    /// <summary>Per frame (from <see cref="_Process"/>): a pending heading is scrolled to once the text is laid out.</summary>
    private void UpdateReader()
    {
        if (_pendingAnchor == null || !_readmePanel.Visible || !_readmeText.IsFinished() || _readmeText.GetParagraphCount() == 0) return;
        var anchor = _pendingAnchor;
        _pendingAnchor = null;
        JumpToAnchor(anchor);
    }

    /// <summary>Scrolls to the heading with this anchor; false (and the status says so) when the document has none.</summary>
    public bool JumpToAnchor(string anchor)
    {
        if (_reader?.Anchors is { } anchors && (anchors.TryGetValue(anchor, out var p) || anchors.TryGetValue(anchor.ToLowerInvariant(), out p)))
        {
            _readmeText.ScrollToParagraph(p);
            SetReaderStatus($"At “{anchor}”.", bad: false, sticky: true);
            return true;
        }

        SetReaderStatus($"This document has no heading “{anchor}”.", bad: true, sticky: true);
        return false;
    }

    /// <summary>The paragraph the view is scrolled to (probes compare it with an anchor's paragraph).</summary>
    public int ReaderTopParagraph
    {
        get
        {
            var y = _readmeText.GetVScrollBar().Value;
            var count = _readmeText.GetParagraphCount();
            for (var i = 0; i < count; i++) if (_readmeText.GetParagraphOffset(i) > y + 1) return Math.Max(0, i - 1);
            return Math.Max(0, count - 1);
        }
    }

    /// <summary>Paragraph <paramref name="p"/> starts inside the visible part of the reading view (probes: an anchor's heading is in view).</summary>
    public bool ReaderParagraphVisible(int p)
    {
        var bar = _readmeText.GetVScrollBar();
        var y = _readmeText.GetParagraphOffset(p);
        return y >= bar.Value - 1 && y <= bar.Value + bar.Page - 10;
    }

    private bool _statusSticky;

    /// <summary>
    /// A line under the text: where a hovered link goes, or (<paramref name="sticky"/>) the outcome of a click or an open,
    /// which stays until the next one. <paramref name="bad"/> colours a refusal or a failure.
    /// </summary>
    public void SetReaderStatus(string text, bool bad, bool sticky = false)
    {
        _readmeStatus.Text = text;
        _readmeStatus.AddThemeColorOverride("font_color", bad ? ReaderStatusBadColor : ReaderStatusColor);
        _statusSticky = sticky;
    }

    private MarkdownLink? LinkAt(string meta) =>
        int.TryParse(meta, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var i) && _reader?.Links is { } links && i >= 0 && i < links.Count ? links[i] : null;

    private string DescribeLink(MarkdownLink l) =>
        _reader?.Path is { } path ? MarkdownLinks.Describe(MarkdownLinks.Resolve(l.Target, path)) : l.Target;

    /// <summary>A link clicked: an anchor scrolls here; anything else is Main's to follow (a deliberate click, PLANET-53).</summary>
    private void OnReaderMeta(string meta)
    {
        if (LinkAt(meta) is not { } link || _reader == null) return;
        if (link.Target.TrimStart().StartsWith('#'))
        {
            JumpToAnchor(MarkdownLinks.Resolve(link.Target, _reader.Path ?? "").Fragment ?? "");
            return;
        }

        ReaderLinkClicked?.Invoke(_reader, link);
    }

    /// <summary>Probe access: the same as clicking link <paramref name="index"/> in the reading view.</summary>
    public void ProbeClickReaderLink(int index) => OnReaderMeta(index.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Open with Windows (the button, or Enter while reading).</summary>
    public void RequestReaderOpen()
    {
        if (_reader?.Path is { } path) ReaderOpenRequested?.Invoke(path);
    }

    /// <summary>The reading view's widest text column, viewport units: about 110 characters at 15 px, a comfortable line
    /// (PLANET-44, 2026-11-16); a narrower window keeps the 120-unit side insets it always had.</summary>
    private const float ReadingMaxWidth = 1040f;

    /// <summary>Centres the reading view and caps its width at <see cref="ReadingMaxWidth"/> for the current window.</summary>
    private void FitReadingWidth()
    {
        var width = _root.GetViewportRect().Size.X;
        var inset = MathF.Max(120f, (width - ReadingMaxWidth) / 2f);
        _readmePanel.OffsetLeft = inset;
        _readmePanel.OffsetRight = -inset;
    }

    /// <summary>The reading view's text is longer than its window, so it scrolls (probes).</summary>
    public bool ReadmeScrolls => _readmeText.GetVScrollBar() is { } bar && bar.MaxValue > bar.Page + 1;

    /// <summary>Scrolls the reading view by a fraction of its page (probes; the wheel and the scrollbar do the same).</summary>
    public void ScrollReadme(float pages)
    {
        var bar = _readmeText.GetVScrollBar();
        bar.Value += bar.Page * pages;
    }

    /// <summary>The reading view's scroll position, 0 at the top (probes).</summary>
    public double ReadmeScroll => _readmeText.GetVScrollBar().Value;

    /// <summary>The reading view's panel, text area and buttons on screen, viewport units (probes aim the wheel and clicks here).</summary>
    public Rect2 ReadmeRect => _readmePanel.GetGlobalRect();

    public Rect2 ReadmeTextRect => _readmeText.GetGlobalRect();

    public Rect2 ReadmeCloseRect => _readmeClose.GetGlobalRect();

    public Rect2 ReadmeOpenRect => _readmeOpen.GetGlobalRect();

    /// <summary>Gives the reading view's text the keyboard again (a key arrived while something else had it).</summary>
    public void FocusReader()
    {
        if (_readmePanel.Visible && !_readmeText.HasFocus()) _readmeText.GrabFocus();
    }

    /// <summary>The reading view's text has the keyboard (probes and the state file).</summary>
    public bool ReaderFocused => _readmeText.HasFocus();

    /// <summary>A toast is drawn above the reading view (probes: feedback must not be hidden behind it).</summary>
    public bool ToastAboveReader => _toastPanel.GetIndex() > _readmePanel.GetIndex();

    /// <summary>The reading view is scrolled to its end: the scroll bar's page reaches its maximum (probes, PLANET-52).</summary>
    public bool ReadmeAtEnd => _readmeText.GetVScrollBar() is { } bar && bar.Value + bar.Page >= bar.MaxValue - 1;

    /// <summary>Where <paramref name="text"/> first appears in the reading view, viewport units (probes click links there), or null if it is not in view.</summary>
    public Vector2? ReaderTextPoint(string text) => RichTextPoint(_readmeText, text, ReaderFontPixels);

    /// <summary>
    /// The screen point a little inside the first occurrence of <paramref name="text"/> in a RichTextLabel (viewport
    /// units), or null when it is not on screen: its line from the label's layout, the x from the width of the line's text
    /// before it in the label's normal font at <paramref name="fontPixels"/> (good for links at the start of a list item or
    /// a paragraph, which is where the probes click). Probes only.
    /// </summary>
    public static Vector2? RichTextPoint(RichTextLabel label, string text, int fontPixels)
    {
        var parsed = label.GetParsedText();
        var at = parsed.IndexOf(text, StringComparison.Ordinal);
        if (at < 0) return null;
        var line = label.GetCharacterLine(at);
        var range = label.GetLineRange(line);
        var font = label.GetThemeFont("normal_font");
        var before = parsed[Math.Clamp(range.X, 0, at)..at];
        var x = font.GetStringSize(before, HorizontalAlignment.Left, -1, fontPixels).X + font.GetStringSize(text[..Math.Min(3, text.Length)], HorizontalAlignment.Left, -1, fontPixels).X;
        var y = label.GetLineOffset(line) - (float)label.GetVScrollBar().Value + label.GetLineHeight(line) / 2f;
        var margin = label.GetThemeStylebox("normal")?.GetMargin(Side.Top) ?? 0;
        var rect = label.GetGlobalRect();
        var p = rect.Position + new Vector2(x + (label.GetThemeStylebox("normal")?.GetMargin(Side.Left) ?? 0), y + margin);
        return rect.HasPoint(p) ? p : null;
    }

    /// <summary>Closes the reader (Esc or Close); the text stays until the next <see cref="ShowReader"/>.</summary>
    public void HideReadme()
    {
        if (!_readmePanel.Visible) return;
        _readmePanel.Visible = false;
        _readmeText.ReleaseFocus();
        _pendingAnchor = null;
        ReaderClosed?.Invoke();
    }
}
