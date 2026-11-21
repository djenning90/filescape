using FileScape.App.Planets;
using FileScape.Core.Interaction;
using FileScape.Core.Samples;
using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-text --root=&lt;Markup Names&gt; --vis=planets --test-shell=record --ui-scale=1</c> (PLANET-152..156,
/// AD-93): the text surfaces on the markup-names fixture (<c>FileScape.Fixtures create-markup</c>), whose names and
/// texts look like BBCode, Markdown and FileScape's key-cap markup. It checks what only the running app can show —
/// that the labels really display those names and texts as written, that each surface fits the window, and that the
/// wheel over Help and the reader scrolls them, not the camera — and screenshots every surface at each representative
/// window size and UI scale for a person to look at. What the text is (escaping, caps, shortening, fitting sizes) is
/// Core's and tested there (LiteralTextTests, UiTextTests, PanelFitTests); this journey does not repeat it.
/// <para>
/// Window sizes larger than the screen's usable area are skipped with a note (the OS would clamp them), not failed.
/// The window is put back to its starting size and scale at the end.
/// </para>
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>The least room a surface must leave inside the window, viewport units (a pixel of rounding is allowed).</summary>
    private const float SurfaceFitSlack = 1f;

    private async Task TextRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.2); // the arrival title fades
        var root = App.Navigator.Current;
        var plain = Child(root, "plain [i]text.txt");
        var md = Child(root, "notes [b].md");
        var longFolder = Child(root, MarkupNamesSample.LongName);
        var keycapFolder = Child(root, "{Space} keycap folder");
        if (!Check(Tree[root].Name == MarkupNamesSample.RootName && plain >= 0 && md >= 0 && longFolder >= 0 && keycapFolder >= 0,
                $"the markup-names fixture is open (FileScape.Fixtures create-markup): {Tree.FullPath(root)}")) return;

        await TextSurfaces(plain, md, keycapFolder, longFolder, "1600x900-1.00", literalChecks: true);

        // ---- every surface at each representative window size and UI scale (screenshots for a person to judge)
        var window = GetWindow();
        var size0 = window.Size;
        var scale0 = window.ContentScaleFactor;
        var usable = DisplayServer.ScreenGetUsableRect(window.CurrentScreen).Size;
        foreach (var (w, h) in PanelFit.RepresentativeWindows)
        {
            if (w > usable.X || h > usable.Y)
            {
                Log(F($"SKIP {w}×{h}: larger than the screen's usable {usable.X}×{usable.Y} (the coordinator can run it with --window={w}x{h} on a larger screen)"));
                continue;
            }

            window.Size = new Vector2I(w, h);
            foreach (var scale in PanelFit.RepresentativeScales)
            {
                window.ContentScaleFactor = scale;
                await Frames(8);
                await TextSurfaces(plain, md, keycapFolder, longFolder, F($"{w}x{h}-{scale:0.00}"), literalChecks: false);
            }
        }

        window.ContentScaleFactor = scale0;
        window.Size = size0;
        await Frames(6);
    }

    /// <summary>Opens each text surface in turn at the current window size, checks it fits (and, once, what it shows), and
    /// screenshots it as <c>text-&lt;surface&gt;-&lt;tag&gt;</c>.</summary>
    private async Task TextSurfaces(int plain, int md, int keycapFolder, int longFolder, string tag, bool literalChecks)
    {
        var vp = App.GetViewport().GetVisibleRect();
        bool Fits(Rect2 r) => r.Size.X > 0 && r.Position.X >= vp.Position.X - SurfaceFitSlack && r.Position.Y >= vp.Position.Y - SurfaceFitSlack
                              && r.End.X <= vp.End.X + SurfaceFitSlack && r.End.Y <= vp.End.Y + SurfaceFitSlack;
        string R(Rect2 r) => F($"{r.Position.X:0},{r.Position.Y:0} {r.Size.X:0}×{r.Size.Y:0} in {vp.Size.X:0}×{vp.Size.Y:0}");

        // The prompt by a file whose name looks like markup, and its plain-text preview.
        P.Approach(plain); // [direct] as Enter on its row in the list: hop there and look closer
        await Until(() => !P.Busy && P.PreviewOpen && P.PreviewHasText, 8);
        await Seconds(0.3);
        Check(Fits(P.PromptBoxRect) && Fits(P.PreviewRect), $"{tag}: the prompt and the preview fit the window (prompt {R(P.PromptBoxRect)}; preview {R(P.PreviewRect)})");
        if (literalChecks)
        {
            Check(P.PromptText.Contains("plain [i]text.txt", StringComparison.Ordinal), $"the prompt names the file as it is named: \"{P.PromptText}\"");
            Check(P.PreviewShownText.Contains("[b]This is not bold[/b] & <b>nor this</b>.", StringComparison.Ordinal) && P.PreviewShownText.Contains("{E} or twirl · *not emphasised* · ~not quiet~", StringComparison.Ordinal),
                $"a plain text file's preview shows its brackets, braces and marks as written: \"{P.PreviewShownText.Split('\n')[0]}…\"");
        }

        await Shot($"text-prompt-preview-{tag}");

        // The Markdown preview, then the reading view; the wheel over it scrolls it, never the camera.
        P.Approach(md);
        await Until(() => !P.Busy && P.PreviewOpen && P.PreviewExpandable, 8);
        await Seconds(0.3);
        if (literalChecks)
        {
            Check(P.PreviewShownText.Contains("Notes [b]not bold[/b]", StringComparison.Ordinal) && P.PreviewShownText.Contains("[url=https://example.com]a fake BBCode link[/url]", StringComparison.Ordinal)
                  && P.PreviewShownText.Contains("{Space} is not a key cap here", StringComparison.Ordinal),
                $"the Markdown preview renders its own marks and shows BBCode-like text as written: \"{P.PreviewShownText.Split('\n')[0]}…\"");
        }

        await Key(Godot.Key.R);
        await Until(() => App.Hud.ReadmeVisible, 6);
        await Frames(4);
        Check(App.Hud.ReadmeVisible && Fits(App.Hud.ReadmeRect), $"{tag}: the reading view fits the window ({R(App.Hud.ReadmeRect)})");
        if (literalChecks)
        {
            Check(App.Hud.ReadmeShownText.Contains("[img]C:/x.png[/img]", StringComparison.Ordinal) && App.Hud.ReadmeShownText.Contains("~tilde~ & <i>html</i> &amp; entity", StringComparison.Ordinal),
                "the reading view shows the file's markup-like text as written");
            var cam = P.ChaseDistanceMetres;
            await WheelAt(App.Hud.ReadmeTextRect.GetCenter(), down: true, 3);
            Check(Mathf.IsEqualApprox(P.ChaseDistanceMetres, cam), F($"the wheel over the reading view leaves the camera ({cam:0.00} → {P.ChaseDistanceMetres:0.00} m)"));
        }

        await Shot($"text-reader-{tag}");
        await Key(Godot.Key.Escape);
        await Until(() => !App.Hud.ReadmeVisible, 3);
        await Key(Godot.Key.E); // close the preview
        await Frames(3);

        // A launcher whose world's name looks like a key cap, and one too long for a line.
        P.Approach(keycapFolder);
        await Until(() => !P.Busy && P.FocusNodeId == keycapFolder, 8);
        await Frames(3);
        if (literalChecks) Check(P.PromptText.Contains("fly to {Space} keycap folder", StringComparison.Ordinal), $"on the launcher the world's name is shown as named: \"{P.PromptText}\"");
        await Shot($"text-launcher-keycap-{tag}");
        P.Approach(longFolder);
        await Until(() => !P.Busy && P.FocusNodeId == longFolder, 8);
        await Frames(3);
        Check(Fits(P.PromptBoxRect), $"{tag}: a long name's prompt fits ({R(P.PromptBoxRect)}): \"{P.PromptText}\"");
        if (literalChecks) Check(P.PromptText.Contains('…') && P.PromptText.EndsWith("[end]", StringComparison.Ordinal), $"the long name is shortened in its middle, its end kept: \"{P.PromptText}\"");
        await Shot($"text-launcher-long-{tag}");

        // The planet's list (Destinations) with every markup-like name.
        await Key(Godot.Key.J);
        await Until(() => P.ListOpen, 3);
        await Frames(4);
        Check(P.ListOpen && Fits(P.ContentsRect), $"{tag}: the list fits the window ({R(P.ContentsRect)})");
        if (literalChecks)
        {
            var names = P.DestinationRows.Select(r => r.Name).ToHashSet();
            var missing = MarkupNamesSample.Folders.Where(f => !names.Contains(f)).ToList();
            Check(missing.Count == 0, $"the list's rows carry every folder's name as named (missing: {string.Join(" | ", missing)})");
        }

        await Shot($"text-destinations-{tag}");
        await Key(Godot.Key.Escape);
        await Until(() => !P.ListOpen, 3);

        // Help: headings and key caps; it fits; the wheel scrolls it.
        await Key(Godot.Key.F1);
        await Until(() => App.Hud.HelpVisible, 3);
        await Frames(4);
        Check(App.Hud.HelpVisible && Fits(App.Hud.HelpRect), $"{tag}: Help fits the window ({R(App.Hud.HelpRect)})");
        if (literalChecks)
        {
            Check(App.Hud.HelpShownText.Contains("✦ Planets", StringComparison.Ordinal) && !App.Hud.HelpShownText.Contains('{') && !App.Hud.HelpShownText.Contains("[b]", StringComparison.Ordinal),
                "Help shows its headings and caps, no stray markup");
            var cam = P.ChaseDistanceMetres;
            await WheelAt(App.Hud.HelpRect.GetCenter(), down: true, 3);
            Check(Mathf.IsEqualApprox(P.ChaseDistanceMetres, cam), F($"the wheel over Help leaves the camera ({cam:0.00} → {P.ChaseDistanceMetres:0.00} m)"));
        }

        await Shot($"text-help-{tag}");
        await Key(Godot.Key.F1);
        await Until(() => !App.Hud.HelpVisible, 3);
    }
}
