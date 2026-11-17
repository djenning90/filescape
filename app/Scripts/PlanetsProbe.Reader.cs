using FileScape.App.Planets;
using FileScape.Core.Model;
using FileScape.Core.Scanning;
using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-reader --root=&lt;FILESCAPE_WSL_README&gt; --vis=planets --test-shell=record</c> (PLANET-52/53,
/// AD-62): the README reader at both sizes on the fixture's guide.md (tools/make-wsl-fixture.sh --readme), with injected
/// input through the real path. The small preview: the Expand icon in its upper-right corner, an excerpt, the wheel
/// scrolling its text and not the camera, Enter opening the file. The reading view: the icon replaces the preview with
/// it, the wheel and the keys scroll it (not the camera, no shortcut), it reaches the end of the document, Enter and its
/// Open button open the file (recorded by the test shell; a refused open says why, above the reader), and Close brings the
/// preview back. Links, clicked where they are drawn: an anchor scrolls to its heading; a relative link with a space,
/// written %20 or &lt;with spaces&gt;, opens that document; file#section opens it at the heading; a folder link goes
/// there; a web link goes to the shell only on the click; a missing target is said. The remote image is alt text, never
/// an image tag. R expands and R closes.
/// <para>
/// Reproduced first (2026-11-17, <c>docs/evidence/2026-11-17-input/repro-reader.log</c>): the wheel over the reading
/// view zoomed the camera (7.20 → 9.58 m) and the text stayed at the top; Enter's result toast was drawn behind the
/// reader; the &lt;with spaces&gt; link form was shown raw; the preview stayed beside the reader.
/// </para>
/// </summary>
public partial class PlanetsProbe
{
    private async Task WheelAt(Vector2 viewportPoint, bool down, int notches)
    {
        var at = ToWindow(viewportPoint);
        Inject(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        var b = down ? MouseButton.WheelDown : MouseButton.WheelUp;
        for (var i = 0; i < notches; i++)
        {
            Inject(new InputEventMouseButton { ButtonIndex = b, Pressed = true, Position = at, GlobalPosition = at, Factor = 1, ButtonMask = (MouseButtonMask)(1 << ((int)b - 1)) });
            Inject(new InputEventMouseButton { ButtonIndex = b, Pressed = false, Position = at, GlobalPosition = at, Factor = 1 });
            await Frames(1);
        }

        await Frames(3);
    }

    /// <summary>Clicks a link where the reading view draws it (its text's position), logging where.</summary>
    private async Task<bool> ClickReaderLink(string text)
    {
        if (App.Hud.ReaderTextPoint(text) is not { } at)
        {
            Log($"link “{text}” is not in view");
            return false;
        }

        await Click(at);
        await Frames(3);
        return true;
    }

    private async Task ReaderRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await Until(() => !P.Busy && Tree[App.Navigator.Current].ScanState == DirScanState.Complete, 15);
        await WaitSettled();
        await Seconds(3.2);
        var repo = App.Navigator.Current;
        var guide = Child(repo, "guide.md");
        var docs = Child(repo, "docs");
        var src = Child(repo, "src");
        if (!Check(guide >= 0 && docs >= 0 && src >= 0, "the fixture has guide.md, docs and src (tools/make-wsl-fixture.sh --readme)")) return;

        // ---- the small preview
        P.Approach(guide); // [direct] as Enter on guide.md in the list: hop there and look closer
        await Until(() => P.PreviewOpen && P.PreviewExpandable, 6);
        await Seconds(0.4);
        var panel = P.PreviewRect;
        var icon = P.PreviewExpandRect;
        Check(icon.Size.X > 0 && icon.GetCenter().X > panel.GetCenter().X + panel.Size.X * 0.3f && icon.GetCenter().Y < panel.Position.Y + 48,
            F($"the Expand icon is in the preview's upper-right corner (icon at {icon.GetCenter().X:0},{icon.GetCenter().Y:0}; panel {panel.Position.X:0},{panel.Position.Y:0} {panel.Size.X:0}×{panel.Size.Y:0})"));
        Check(P.PreviewShownText.Contains("an excerpt", StringComparison.Ordinal) && !P.PreviewShownText.Contains("END-OF-GUIDE", StringComparison.Ordinal), "the preview is an excerpt and says so");
        await Shot("reader-preview");
        var cam = P.ChaseDistanceMetres;
        var scroll = P.PreviewScroll;
        await WheelAt(P.PreviewTextRect.GetCenter(), down: true, 3);
        Check(P.PreviewScroll > scroll && Mathf.IsEqualApprox(P.ChaseDistanceMetres, cam), F($"the wheel over the preview scrolls it ({scroll:0} → {P.PreviewScroll:0} px), not the camera ({cam:0.00} → {P.ChaseDistanceMetres:0.00} m)"));
        var feet = P.Walker.Feet;
        var selected = App.Selection.Selected;
        await Click(P.PreviewTextRect.GetCenter() + new Vector2(0, 30));
        Check(App.Selection.Selected == selected && (P.Walker.Feet - feet).Length() < 0.05f && P.PreviewOpen, $"a click in the preview's text stays in the preview (selected {Sel}; trace: {P.LastClickTrace})");
        var shell = App.ShellLog.Count;
        await Key(Godot.Key.Enter);
        await Frames(3);
        Check(App.ShellLog.Count == shell + 1 && App.ShellLog[^1] == "open " + Tree.FullPath(guide), $"Enter in the preview opens guide.md with Windows ({ShellSince(shell)})");
        Check(App.Hud.ShownToast.Contains("guide.md", StringComparison.Ordinal), $"…and says so (“{App.Hud.ShownToast}”)");

        // ---- a link in the excerpt: an anchor opens the reading view at its heading
        await WheelAt(P.PreviewTextRect.GetCenter(), down: false, 10);
        if (P.PreviewTextPoint("Jump to the last section") is { } jump)
        {
            await Click(jump);
            await Until(() => App.Hud.ReadmeVisible && App.Hud.ReadmeShownText.Contains("END-OF-GUIDE", StringComparison.Ordinal), 6);
            await Until(() => App.Hud.ReadmeScroll > 0, 2);
            await Frames(4);
            var lastAnchor = App.Hud.Reader?.Anchors is { } pa && pa.TryGetValue("the-last-section", out var pp) ? pp : -1;
            Check(App.Hud.ReadmeVisible && !P.PreviewOpen && lastAnchor > 0 && App.Hud.ReaderParagraphVisible(lastAnchor), F($"an anchor link in the preview opens the reading view at its heading (at {App.Hud.ReadmeScroll:0} px)"));
            await Key(Godot.Key.Escape);
            await Frames(3);
            Check(!App.Hud.ReadmeVisible && P.PreviewOpen, "Esc returns to the preview");
        }
        else Check(false, "the preview shows the guide's first link");

        // ---- the Expand icon: the reading view replaces the preview
        await Click(icon.GetCenter());
        await Until(() => App.Hud.ReadmeVisible && App.Hud.ReadmeShownText.Contains("END-OF-GUIDE", StringComparison.Ordinal), 6);
        await Frames(5);
        Check(App.Hud.ReadmeVisible && !P.PreviewOpen, "a click on the Expand icon replaces the preview with the reading view");
        Check(App.Hud.ReaderFocused, "the reading view has the keyboard");
        var bb = App.Hud.Reader?.Bbcode ?? "";
        Check(App.Hud.ReadmeShownText.Contains("(image: Build badge)", StringComparison.Ordinal) && !bb.Contains("[img", StringComparison.Ordinal) && !App.Hud.ReadmeShownText.Contains("shields.io", StringComparison.Ordinal),
            "the remote image is its alt text: no image tag, nothing to fetch");
        await Shot("reader-open");

        cam = P.ChaseDistanceMetres;
        await WheelAt(App.Hud.ReadmeTextRect.GetCenter(), down: true, 3);
        var wheeled = App.Hud.ReadmeScroll;
        Check(wheeled > 0 && Mathf.IsEqualApprox(P.ChaseDistanceMetres, cam), F($"the wheel over the reading view scrolls it (0 → {wheeled:0} px), not the camera ({cam:0.00} → {P.ChaseDistanceMetres:0.00} m)"));
        var count = App.UnhandledKeyCount;
        var state = WorldState();
        await Key(Godot.Key.Pagedown);
        await Key(Godot.Key.Down);
        var keyed = App.Hud.ReadmeScroll;
        await TypeKeys("jtlkbv");
        Check(keyed > wheeled, F($"Page Down and ↓ scroll it ({wheeled:0} → {keyed:0} px)"));
        Check(LeakedSince(count).Count == 0 && WorldState() == state && App.Hud.ReadmeVisible, $"letters while reading fire no shortcut (keys that reached the game: [{string.Join(",", LeakedSince(count))}])");
        await Key(Godot.Key.End);
        await Frames(4);
        Check(App.Hud.ReadmeAtEnd && App.Hud.ReaderTextPoint("END-OF-GUIDE") != null, "End reaches the end of the document: END-OF-GUIDE is in view");
        await Shot("reader-end");
        await WheelAt(App.Hud.ReadmeTextRect.GetCenter(), down: false, 200);
        Check(App.Hud.ReadmeScroll < 1, F($"the wheel back up reaches the top ({App.Hud.ReadmeScroll:0} px)"));

        // ---- links
        var anchors = App.Hud.Reader?.Anchors;
        await ClickReaderLink("Jump to the last section");
        await Frames(4);
        var last = anchors != null && anchors.TryGetValue("the-last-section", out var lp) ? lp : -1;
        Check(last > 0 && App.Hud.ReaderParagraphVisible(last) && App.Hud.ReadmeScroll > 0, F($"the anchor link scrolls to its heading (paragraph {last}, at {App.Hud.ReadmeScroll:0} px)"));
        await Shot("reader-anchor");
        await Key(Godot.Key.Home);
        await Frames(3);

        shell = App.ShellLog.Count;
        await ClickReaderLink("FileScape on the web");
        Check(App.ShellLog.Count == shell + 1 && App.ShellLog[^1] == "open https://example.com/filescape", $"a web link goes to the browser through the shell, on the click ({ShellSince(shell)})");
        Check(App.Hud.ReaderStatusText.Contains("browser", StringComparison.Ordinal) && App.Hud.ToastAboveReader, $"…and says so in the reader (“{App.Hud.ReaderStatusText}”), the toast above it");

        await ClickReaderLink("A missing page");
        Check(App.Hud.ReaderStatusText.StartsWith("Not found", StringComparison.Ordinal) && App.Hud.ReadmeVisible, $"a missing target is said (“{App.Hud.ReaderStatusText}”)");

        var views = App.ReaderViews;
        await ClickReaderLink("Getting started");
        await Until(() => App.ReaderViews > views, 5);
        await Frames(4);
        var gs = Tree.FullPath(docs) + "\\getting started.md";
        Check(App.Hud.Reader?.Path == gs && App.Hud.ReadmeShownText.Contains("Start here", StringComparison.Ordinal), $"a relative link written with %20 opens docs/getting started.md in the reader ({App.Hud.Reader?.Path})");
        await Shot("reader-relative-link");
        await Key(Godot.Key.Escape);
        await Frames(3);
        Check(!App.Hud.ReadmeVisible && P.PreviewOpen && App.Selection.Selected == guide, "Esc closes the reader and the guide's preview comes back");
        await Key(Godot.Key.R);
        await Until(() => App.Hud.ReadmeVisible && App.Hud.Reader?.Path == Tree.FullPath(guide), 5);
        await Frames(4);
        Check(App.Hud.ReadmeVisible && !P.PreviewOpen, "R expands it again");
        views = App.ReaderViews;
        await ClickReaderLink("Getting started, written with a space");
        await Until(() => App.ReaderViews > views, 5);
        await Frames(3);
        Check(App.Hud.Reader?.Path == gs, $"the same file written <with spaces> opens too ({App.Hud.Reader?.Path})");
        await Key(Godot.Key.R);
        await Frames(3);
        Check(!App.Hud.ReadmeVisible, "R closes the reader");
        await Key(Godot.Key.R);
        await Until(() => App.Hud.ReadmeVisible && App.Hud.Reader?.Path == Tree.FullPath(guide), 5);
        await Frames(4);

        views = App.ReaderViews;
        await ClickReaderLink("Units: angles");
        await Until(() => App.ReaderViews > views, 5);
        await Until(() => App.Hud.ReadmeScroll > 0, 2);
        await Frames(4);
        var units = App.Hud.Reader?.Anchors is { } ua && ua.TryGetValue("angles", out var ap) ? ap : -1;
        Check(App.Hud.Reader?.Path?.EndsWith("units.md", StringComparison.Ordinal) == true && units > 0 && App.Hud.ReaderParagraphVisible(units), F($"file#section opens docs/units.md at “Angles” (paragraph {units}, at {App.Hud.ReadmeScroll:0} px)"));
        await Shot("reader-file-anchor");
        await Key(Godot.Key.Escape);
        await Frames(3);

        // ---- Enter and Open from the reading view, and a refused open
        await Key(Godot.Key.R);
        await Until(() => App.Hud.ReadmeVisible && App.Hud.Reader?.Path == Tree.FullPath(guide), 5);
        await Frames(4);
        shell = App.ShellLog.Count;
        await Key(Godot.Key.Enter);
        await Frames(3);
        Check(App.ShellLog.Count == shell + 1 && App.ShellLog[^1] == "open " + Tree.FullPath(guide), $"Enter in the reading view opens guide.md with Windows ({ShellSince(shell)})");
        Check(App.Hud.ToastAboveReader && App.Hud.ShownToast.Contains("guide.md", StringComparison.Ordinal) && App.Hud.ReaderStatusText.Contains("guide.md", StringComparison.Ordinal),
            $"…and says so above the reader and in its status line (“{App.Hud.ReaderStatusText}”)");
        await Shot("reader-enter");
        shell = App.ShellLog.Count;
        await Click(App.Hud.ReadmeOpenRect.GetCenter());
        Check(App.ShellLog.Count == shell + 1 && App.ShellLog[^1] == "open " + Tree.FullPath(guide), $"its Open with Windows button does the same ({ShellSince(shell)})");
        App.ProbeFailNextOpen(1155); // [instrument] Windows' "no app is associated" for the next open
        await Key(Godot.Key.Enter);
        await Frames(3);
        Check(App.ShellLog[^1].StartsWith("open-failed ", StringComparison.Ordinal) && App.Hud.ReaderStatusText.Contains("no app is set to open .md files", StringComparison.Ordinal) && App.Hud.ShownToast.Contains("Could not open guide.md", StringComparison.Ordinal),
            $"a refused open says why, in the reader and above it (“{App.Hud.ReaderStatusText}”)");
        await Shot("reader-open-failed");
        await Click(App.Hud.ReadmeCloseRect.GetCenter());
        Check(!App.Hud.ReadmeVisible && P.PreviewOpen, "its Close button closes it and the preview comes back");

        // ---- a folder link travels there (from the reading view)
        await Key(Godot.Key.R);
        await Until(() => App.Hud.ReadmeVisible && App.Hud.Reader?.Path == Tree.FullPath(guide), 5);
        await Frames(4);
        await ClickReaderLink("The source folder");
        await Until(() => App.Navigator.Current == src && !P.Busy, 20);
        await Frames(3);
        Check(App.Navigator.Current == src && !App.Hud.ReadmeVisible, $"a folder link closes the reader and goes there ({Tree[App.Navigator.Current].Name})");
        Log($"links followed: {string.Join(" | ", App.FollowedLinks)}");
        Log($"keys that reached the game this run: {string.Join(",", App.UnhandledKeys)}");
    }
}
