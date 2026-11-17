using Godot;

namespace FileScape.App.Ui;

/// <summary>
/// PROM-02/03 and DEV-01..03: the card's prominence line and pin toggle, the project actions (README, open in an editor),
/// and the read-only README viewer. Everything here is deliberate: nothing is read or launched until clicked.
/// </summary>
public partial class Hud
{
    public event Action? PinToggled;
    public event Action? ReadmeRequested;
    public event Action<int>? EditorRequested;

    private Label _prominence = null!;
    private Button _pinButton = null!, _readmeButton = null!;
    private HBoxContainer _editorRow = null!;
    private PanelContainer _readmePanel = null!;
    private Label _readmeTitle = null!, _readmeFooter = null!;
    private RichTextLabel _readmeText = null!;

    /// <summary>Called from the inspector's construction: adds the prominence line and the project rows under the brief.</summary>
    private void BuildProjectCard(VBoxContainer v, int afterIndex)
    {
        _prominence = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        _prominence.AddThemeFontSizeOverride("font_size", 13);
        _prominence.AddThemeColorOverride("font_color", new Color(1f, 0.86f, 0.55f));
        v.AddChild(_prominence);
        v.MoveChild(_prominence, afterIndex + 1);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        _pinButton = MakeButton("☆ Pin (P)", () => PinToggled?.Invoke(), "Pin this folder: it stands out wherever it appears (saved in your settings). Press again to unpin.");
        _readmeButton = MakeButton("README (R)", () => ReadmeRequested?.Invoke(), "Read this project's README here, as text. Nothing in it is run, opened or fetched.");
        row.AddChild(_pinButton);
        row.AddChild(_readmeButton);
        v.AddChild(row);
        v.MoveChild(row, afterIndex + 2);
        _editorRow = new HBoxContainer { Visible = false };
        _editorRow.AddThemeConstantOverride("separation", 6);
        v.AddChild(_editorRow);
        v.MoveChild(_editorRow, afterIndex + 3);
        _pinButton.Visible = _readmeButton.Visible = false;
        BuildReadmeViewer();
    }

    /// <summary>
    /// The card's extras for the selected folder: why it stands out (or null), whether it can be pinned and is, whether a
    /// README can be shown, and the editor options (labels and explanations) that exist on this computer.
    /// </summary>
    public void SetCardExtras(string? prominence, bool canPin, bool pinned, bool hasReadme, IReadOnlyList<(string Label, string Explanation)> editors)
    {
        _prominence.Text = prominence ?? "";
        _prominence.Visible = !string.IsNullOrEmpty(prominence);
        _pinButton.Visible = canPin;
        _pinButton.Text = pinned ? "★ Unpin (P)" : "☆ Pin (P)";
        _readmeButton.Visible = hasReadme;
        foreach (var c in _editorRow.GetChildren()) c.QueueFree();
        for (var i = 0; i < editors.Count; i++)
        {
            var index = i;
            _editorRow.AddChild(MakeButton(editors[i].Label + "…", () => EditorRequested?.Invoke(index), editors[i].Explanation + " You are asked first."));
        }

        _editorRow.Visible = editors.Count > 0;
    }

    public bool ReadmeVisible => _readmePanel.Visible;

    /// <summary>Probe access: the same as clicking the card's editor button <paramref name="index"/>.</summary>
    public void ProbeRequestEditor(int index) => EditorRequested?.Invoke(index);

    /// <summary>Test and probe access: the README text as shown (BBCode stripped by the label).</summary>
    public string ReadmeShownText => _readmeText.GetParsedText();

    /// <summary>The README viewer: a nearly full-window opaque panel (inset 120 at the sides, below the top bar), hidden until <see cref="ShowReadme"/>.</summary>
    private void BuildReadmeViewer()
    {
        _readmePanel = new PanelContainer { Visible = false };
        _readmePanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _readmePanel.OffsetLeft = 120; _readmePanel.OffsetRight = -120; _readmePanel.OffsetTop = 80; _readmePanel.OffsetBottom = -60;
        var style = new StyleBoxFlat { BgColor = new Color(0.06f, 0.07f, 0.11f, 1f), BorderColor = new Color(0.4f, 0.48f, 0.7f), ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 12 };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(6);
        _readmePanel.AddThemeStyleboxOverride("panel", style);
        _root.AddChild(_readmePanel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        _readmePanel.AddChild(v);
        var head = new HBoxContainer();
        _readmeTitle = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _readmeTitle.AddThemeFontSizeOverride("font_size", 18);
        head.AddChild(_readmeTitle);
        head.AddChild(MakeButton("Close (Esc)", HideReadme, "Close the README."));
        v.AddChild(head);
        _readmeText = new RichTextLabel
        {
            BbcodeEnabled = true,
            SelectionEnabled = true,
            ScrollActive = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            // Links are not clickable (no meta handling): the README is read, never followed (DEV-02).
            MetaUnderlined = false,
        };
        _readmeText.AddThemeFontSizeOverride("normal_font_size", 15);
        _readmeText.AddThemeFontSizeOverride("bold_font_size", 15);
        _readmeText.AddThemeFontSizeOverride("mono_font_size", 14);
        v.AddChild(_readmeText);
        _readmeFooter = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _readmeFooter.AddThemeFontSizeOverride("font_size", 12);
        _readmeFooter.AddThemeColorOverride("font_color", new Color(0.65f, 0.72f, 0.86f));
        v.AddChild(_readmeFooter);
        _readmePanel.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                HideReadme();
                _readmePanel.AcceptEvent();
            }
        };
    }

    /// <summary>Shows a README Main has already read and converted to BBCode (<paramref name="footer"/>: what was shown, e.g. a truncation note), from the top, above other panels.</summary>
    public void ShowReadme(string title, string bbcode, string footer)
    {
        FitReadingWidth();
        _readmeTitle.Text = title;
        _readmeText.Text = bbcode;
        _readmeText.ScrollToLine(0);
        _readmeFooter.Text = footer;
        _readmePanel.Visible = true;
        _readmePanel.MoveToFront();
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

    /// <summary>Closes the viewer (Esc or Close); the text stays until the next <see cref="ShowReadme"/>.</summary>
    public void HideReadme()
    {
        _readmePanel.Visible = false;
        _readmeText.ReleaseFocus();
    }
}
