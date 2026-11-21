using Godot;

namespace FileScape.App.Ui;

/// <summary>
/// The one look for a button that hands a file to Windows from beside an in-game view (AD-94, PLANET-164): "Open with
/// Windows ↗" in the picture viewer, the small preview and the reading view. The ↗ and a cool sky-blue outline mark it
/// as leaving FileScape for another app, apart from the warm in-game buttons beside it (View large, Previous, Close),
/// so in-game inspection and external opening never look alike. The selection card keeps its own buttons (every one of
/// them is a Windows action; nothing in-game sits beside them to be confused with).
/// Preserve: the label keeps the ↗; the colour stays apart from the planets' warm gold (the panels' borders and titles).
/// </summary>
public static class LeaveButton
{
    /// <summary>The leaving colour: a cool sky blue (by eye, beside the panels' gold).</summary>
    public static readonly Color Colour = new(0.62f, 0.84f, 1f);

    /// <summary>A Windows-opening button with <paramref name="text"/> (which should end in "↗", with its key) and a tooltip
    /// that says it leaves FileScape.</summary>
    public static Button Make(string text, string tooltip)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, TooltipText = tooltip };
        b.AddThemeColorOverride("font_color", Colour);
        b.AddThemeColorOverride("font_hover_color", Colour.Lightened(0.3f));
        b.AddThemeColorOverride("font_pressed_color", Colour.Lightened(0.3f));
        var box = new StyleBoxFlat { BgColor = new Color(0.06f, 0.1f, 0.18f, 0.85f), BorderColor = Colour with { A = 0.8f }, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 4, ContentMarginBottom = 4 };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(6);
        b.AddThemeStyleboxOverride("normal", box);
        var hover = (StyleBoxFlat)box.Duplicate();
        hover.BgColor = new Color(0.1f, 0.16f, 0.28f, 0.95f);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        return b;
    }
}
