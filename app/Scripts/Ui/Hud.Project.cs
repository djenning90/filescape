using Godot;

namespace FileScape.App.Ui;

/// <summary>
/// PROM-02/03 and DEV-01..03: the card's prominence line and pin toggle, the project actions (README, open in an editor),
/// and the README button (the reading view itself is Hud.Reader). Everything here is deliberate: nothing is read or
/// launched until clicked.
/// </summary>
public partial class Hud
{
    public event Action? PinToggled;
    public event Action? ReadmeRequested;
    public event Action<int>? EditorRequested;

    private Label _prominence = null!;
    private Button _pinButton = null!, _readmeButton = null!;
    private HBoxContainer _editorRow = null!;

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

    /// <summary>Probe access: the same as clicking the card's editor button <paramref name="index"/>.</summary>
    public void ProbeRequestEditor(int index) => EditorRequested?.Invoke(index);
}
