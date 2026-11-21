using FileScape.App.Planets;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using FileScape.Core.Samples;
using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-gallery --root=&lt;fixture&gt;\Picture Gallery</c> (PLANET-96, AD-78; PLANET-130..136, AD-86; the
/// fixture from <c>FileScape.Fixtures create-gallery</c>, or <c>--root=gallery</c> for the app's own sample without the
/// stress cases and crowds): the gallery played with injected input. <b>The canvases fill as you walk</b>, bounded by
/// <see cref="PlanetVisual.Budget"/> (in flight, shown, sharp), the frame time watched while they load, a near picture
/// sharpening; where each came from (Windows' cache, Windows, FileScape's decoder) is logged. <b>The stray highlight</b>
/// (PLANET-136): a selection made in Places leaves no box in Planets. <b>Inspect:</b> walk up to a picture [direct: the
/// list's hop], E for its preview, R for the viewer, shown at once and then sharp; → steps through the folder's pictures
/// (the large print shrunk; the over-limit poster and the giant scan shown from Windows' thumbnail and saying why, or
/// refused with the reason; the damaged one refused); wheel zoom, drag pan, 0 fit; game keys inert; quick steps bounded;
/// Esc back where you stood. <b>Browsing (AD-94, PlanetsProbe.GalleryBrowse):</b> a click on another easel moves the
/// preview, a click on empty ground puts it away, a double-click views large (never Windows), the viewer's edge steps and
/// its backdrop goes back, Home / End, Space inert, Ctrl + → sidesteps without the view swinging. <b>Inheritance:</b> Sketches (two pictures inside the gallery) is a gallery too; the Desk
/// (one sketch among notes) is ordinary and reads nothing by itself. <b>Counts:</b> the crowds of 3, 30 and 300 beside the
/// fixture fill within the bounds, with times, texture bytes and memory logged.
/// <para><c>--automate=planets-gallery-real --root=PATH</c>: the same measurements on a real picture folder (DJ allowed
/// their OneDrive Pictures, 2026-11-20): only counts, states, sources and times are logged — never a file or folder
/// name; screenshots stay in the run's evidence folder. With <c>--open-one</c> Enter opens one picture with Windows.</para>
/// </summary>
public partial class PlanetsProbe
{
    private async Task GalleryRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        var gallery = App.Navigator.Current;
        // The first composition can come before the listing (a veiled world): wait for the pictures to stand.
        await Until(() => P.Composition!.Items.Any(i => i.NodeId >= 0 && GalleryRules.IsPicture(Tree[i.NodeId].Name)), 15);
        await Frames(2);
        var stress = Tree[gallery].Children.Any(c => Tree[c].Name == GallerySample.OversizeName);
        Check(P.OnGallery, $"{Tree.DisplayName(gallery)} is a gallery ({P.Composition!.Media.Reason}), stress cases: {stress}");
        var visual = P.CurrentVisual!;
        var pictureItems = P.Composition!.Items.Count(i => i.NodeId >= 0 && GalleryRules.IsPicture(Tree[i.NodeId].Name));
        Check(P.Composition.Items.Where(i => i.NodeId >= 0 && GalleryRules.IsPicture(Tree[i.NodeId].Name)).All(i => i.Scale == PlanetComposer.GalleryPictureScale), $"its {pictureItems} pictures stand large (scale {PlanetComposer.GalleryPictureScale})");

        // ---- the canvases fill by themselves, bounded, while the frame time is watched.
        var (frames, secs) = await WatchFill(visual, pictureItems, 10);
        frames.Sort();
        var p95 = frames.Count > 0 ? frames[(int)(frames.Count * 0.95)] : 0;
        Log(F($"canvases: {visual.CanvasesShown} shown, {visual.CanvasesSharp} sharp, {visual.CanvasesFailed} failed of {pictureItems} in {secs:0.0} s; asked {visual.AutoThumbnailsRequested}; peaks in flight {visual.PeakInFlight}, shown {visual.PeakShown}, sharp {visual.PeakSharp}; frames while loading: {frames.Count}, p95 {p95:0.0} ms, max {(frames.Count > 0 ? frames[^1] : 0):0.0} ms"));
        Log("sources: " + GalleryThumbnails.Summary());
        Check(visual.CanvasesShown >= pictureItems - (stress ? 3 : 0), $"the canvases show their pictures by themselves ({visual.CanvasesShown} of {pictureItems}; only stress cases may stay blank)");
        Check(visual.PeakInFlight <= PlanetVisual.Budget.MaxInFlight && visual.PeakShown <= PlanetVisual.Budget.MaxShown && visual.PeakSharp <= PlanetVisual.Budget.MaxLarge,
            $"bounded: ≤ {PlanetVisual.Budget.MaxInFlight} in flight ({visual.PeakInFlight}), ≤ {PlanetVisual.Budget.MaxShown} shown ({visual.PeakShown}), ≤ {PlanetVisual.Budget.MaxLarge} sharp ({visual.PeakSharp})");
        Check(frames.Count == 0 || frames[^1] < 250, F($"the world kept moving while pictures loaded (longest frame {(frames.Count > 0 ? frames[^1] : 0):0} ms < 250 ms)"));
        if (stress)
        {
            var damaged = Tree[gallery].Children.First(c => Tree[c].Name == GallerySample.DamagedName);
            var ds = visual.CanvasState(damaged);
            Check(ds.State is "failed" or "none" or "waiting", $"the damaged picture's canvas says it failed ({ds.State}: {ds.Refusal})");
        }

        await Shot("gallery-arrival-easels");

        // ---- PLANET-136: a selection made in a legacy view leaves no box behind in Planets.
        await StrayHighlightCheck();

        // ---- look at one: it sharpens; then view it large.
        var dawn = Tree[gallery].Children.FirstOrDefault(c => Tree[c].Name == "dawn-over-the-low-hills.png", -1);
        Check(dawn >= 0 && P.Composition.Items.Any(i => i.NodeId == dawn), "the dawn picture stands on the planet");
        P.Approach(dawn); // [direct] as a click in the list: hop beside it and look closer
        await Until(() => P.PreviewOpen && P.PreviewHasImage, 6);
        visual = P.CurrentVisual!; // the Places round trip above rebuilt the planet (instrument: the old visual is gone)
        await Until(() => visual.CanvasState(dawn).State == "sharp", 5);
        Check(visual.CanvasState(dawn).State == "sharp", $"beside it, its canvas is sharp ({visual.CanvasState(dawn).State}, from {visual.CanvasState(dawn).Source}; {visual.CanvasesSharp} sharp now; plan {visual.PlanDebug(dawn)}; feet {P.Walker.Feet}, canvas {visual.CanvasPosition(dawn)}, camera {P.ViewCamera.GlobalPosition})");
        await Frames(4);
        Check(P.PreviewOpen && P.PreviewHasImage && (P.FocusNodeId != dawn || (P.PromptText.Contains("view it large", StringComparison.Ordinal) && P.PromptText.Contains("open with Windows ↗", StringComparison.Ordinal))), $"E: its preview, and the prompt offers the viewer and, apart, Windows ↗ when it is in focus (focus {(P.FocusNodeId >= 0 ? Tree[P.FocusNodeId].Name : "-")}): \"{P.PromptText}\"");
        var feet = P.Walker.Feet;
        var v = P.Viewer;
        await Shot("gallery-preview");
        await Key(Godot.Key.R);
        await Frames(1);
        Check(P.ViewerOpen && v.State == "shown", $"R: a picture in the viewer's first frame ({v.State}, from {v.Source})");
        await Until(() => v.Sharp, 5);
        var (i0, count) = v.Place;
        Check(P.ViewerOpen && v.Current == dawn && v.State == "shown" && v.Sharp && App.KeyboardPanel == Main.KeyPanel.Viewer, $"then sharp ({v.Source}, {i0} of {count}, {v.ShownPixels.X} × {v.ShownPixels.Y} px), owning the keyboard");
        Check(v.FrameRect.Size.X > 900, F($"large: the picture spans {v.FrameRect.Size.X:0} × {v.FrameRect.Size.Y:0} of the window"));
        await Shot("gallery-viewer-dawn");

        // ---- step through the folder: every kind of answer.
        var seen = new List<string>();
        for (var step = 0; step < count; step++)
        {
            var started = v.DecodesStarted;
            await Key(Godot.Key.Right);
            await Until(() => v.Sharp || v.State == "refused", 8);
            var n = Tree[v.Current];
            seen.Add($"{n.Name}: {v.State} {v.Source}{(v.State == "shown" ? $" {v.ShownPixels.X}×{v.ShownPixels.Y} (of {v.SourcePixels.X}×{v.SourcePixels.Y})" : "")}{(v.Note.Length > 0 ? $" — {v.Note}" : v.Refusal.Length > 0 ? $" — {v.Refusal}" : "")}");
            Log($"  → {seen[^1]} (decodes started +{v.DecodesStarted - started})");
            Check(App.Selection.Selected == v.Current, $"  the world selects {n.Name}");
            if (v.Source == "decoded") Check(Math.Max(v.ShownPixels.X, v.ShownPixels.Y) <= PictureViewer.ViewerMaxPixels, $"  decoded within {PictureViewer.ViewerMaxPixels} px");
            switch (n.Name)
            {
                case "aurora-print-large.png":
                    Check(v.SourcePixels == new Vector2I(2560, 1920) && v.ShownPixels == new Vector2I(2048, 1536), $"  the large print is shrunk: {v.SourcePixels} → {v.ShownPixels}");
                    await Shot("gallery-viewer-large-print");
                    break;
                case GallerySample.OversizeName:
                    Check((v.State == "shown" && v.Source.StartsWith("windows", StringComparison.Ordinal) && v.Note.Contains("over the", StringComparison.Ordinal)) || (v.State == "refused" && v.Refusal.Contains("over the", StringComparison.Ordinal)),
                        $"  over 24 MiB: never read by FileScape; Windows' thumbnail stands in, saying why ({v.State}, {v.Source}: \"{v.Note}{v.Refusal}\")");
                    await Shot("gallery-viewer-over-the-limit");
                    break;
                case GallerySample.HugeDimensionsName:
                    Check(v.Note.Contains("million", StringComparison.Ordinal) || v.Refusal.Contains("million", StringComparison.Ordinal), F($"  63 Mpx: FileScape refuses it from its header (\"{v.Note}{v.Refusal}\"; {v.State} from {v.Source})"));
                    break;
                case GallerySample.DamagedName:
                    Check(v.State == "refused" && v.Refusal.Length > 0, $"  damaged: says so (\"{v.Refusal}\")");
                    break;
                case "harbour-of-lanterns-wide.png":
                    await Shot("gallery-viewer-wide");
                    break;
                case "lantern-tower-tall.png":
                    await Shot("gallery-viewer-tall");
                    break;
            }
        }

        Check(v.Current == dawn, $"→ round the folder ({count} pictures) comes back to the first");
        Check(v.MaxConcurrentDecodes <= 1 && v.MaxCached <= PictureViewer.CacheSize, $"bounded: at most {v.MaxConcurrentDecodes} load at once, {v.MaxCached} pictures kept (≤ {PictureViewer.CacheSize})");
        Log(F($"viewer quick pictures appeared in {string.Join(", ", v.QuickMs.Select(ms => ms.ToString("0", System.Globalization.CultureInfo.InvariantCulture)))} ms"));

        // ---- quick steps: one load at a time, only the latest wish kept.
        var before = v.DecodesStarted;
        for (var k = 0; k < 6; k++)
        {
            Key1(Godot.Key.Left, true);
            await Frames(1);
            Key1(Godot.Key.Left, false);
            await Frames(1);
        }

        await Until(() => v.Sharp || v.State == "refused", 8);
        Log($"six quick ← in 12 frames: {v.DecodesStarted - before} loads started, now on {Tree[v.Current].Name} ({v.State})");
        Check(v.DecodesStarted - before <= 6 && v.MaxConcurrentDecodes <= 1 && v.MaxCached <= PictureViewer.CacheSize, $"quick steps stay bounded: {v.DecodesStarted - before} loads for 6 steps, ≤ 1 at once, ≤ {PictureViewer.CacheSize} kept");

        // ---- zoom, pan, fit.
        while (Tree[v.Current].Name != "aurora-print-large.png")
        {
            await Key(Godot.Key.Right);
            await Until(() => v.Sharp || v.State == "refused", 8);
        }

        var centre = v.FrameRect.GetCenter() + new Vector2(200, 80);
        var at = ToWindow(centre);
        Inject(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        var zoom0 = P.ChaseDistanceMetres;
        for (var k = 0; k < 4; k++)
        {
            Mouse(MouseButton.WheelUp, true, at);
            Mouse(MouseButton.WheelUp, false, at);
            await Frames(2);
        }

        Check(v.Zoom > 2f && MathF.Abs(P.ChaseDistanceMetres - zoom0) < 0.01f, F($"the wheel zooms the picture (×{v.Zoom:0.00}), not the camera"));
        var frame0 = v.FrameRect.Position;
        Mouse(MouseButton.Left, true, at);
        await Frames(1);
        Inject(new InputEventMouseMotion { Position = at + new Vector2(-150, -60), GlobalPosition = at + new Vector2(-150, -60), Relative = new Vector2(-150, -60), ButtonMask = MouseButtonMask.Left });
        await Frames(2);
        Mouse(MouseButton.Left, false, at + new Vector2(-150, -60));
        await Frames(2);
        Check(v.FrameRect.Position.DistanceTo(frame0) > 50, F($"a drag pans it ({frame0} → {v.FrameRect.Position})"));
        await Shot("gallery-viewer-zoomed");
        await Key(Godot.Key.Key0);
        Check(MathF.Abs(v.Zoom - 1f) < 0.001f, "0 fits it again");

        // ---- Enter opens it with Windows (recorded under --test-shell=record).
        await Key(Godot.Key.Enter);
        await Frames(3);
        Check(App.ShellLog.Count == 0 || App.ShellLog[^1].Contains("aurora-print-large.png", StringComparison.Ordinal), $"Enter: opens the shown picture with Windows, with no question ({(App.ShellLog.Count > 0 ? App.ShellLog[^1] : App.LastShellMessage)})");
        Check(!App.ModalOpen && P.ViewerOpen, "no confirmation asked for an ordinary picture; the viewer stays");

        // ---- game keys do nothing while it is open.
        var here = App.Navigator.Current;
        var commands = App.ShortcutCommandCount;
        await Hold(0.5, Godot.Key.W);
        foreach (var key in new[] { Godot.Key.T, Godot.Key.J, Godot.Key.U, Godot.Key.Space, Godot.Key.G })
        {
            await Key(key);
            await Frames(2);
        }

        Check(App.Navigator.Current == here && !P.UniverseOpen && !P.ListOpen && !P.Busy && v.IsOpen && Vector3Distance(feet, P.Walker.Feet) < 0.05f && App.ShortcutCommandCount == commands,
            F($"W, T, J, U, Space, G while viewing: nothing moves or opens (moved {Vector3Distance(feet, P.Walker.Feet):0.00} m)"));

        // ---- Esc: back where you stood, the preview on the last picture.
        var lastPicture = v.Current;
        await Key(Godot.Key.Escape);
        await Frames(4);
        Check(!P.ViewerOpen && P.PreviewOpen && App.Selection.Selected == lastPicture && Vector3Distance(feet, P.Walker.Feet) < 0.05f && App.KeyboardPanel == Main.KeyPanel.None,
            $"Esc: back in the world where you stood, the preview on {Tree[lastPicture].Name}");
        await Until(() => P.PreviewHasImage, 4);
        await Shot("gallery-back-in-the-world");
        // R opens again, R closes too.
        await Key(Godot.Key.R);
        await Until(() => v.State != "loading", 5);
        Check(P.ViewerOpen, "R opens it again");
        await Key(Godot.Key.R);
        await Frames(3);
        Check(!P.ViewerOpen, "and R closes it");
        await Key(Godot.Key.Escape);
        await Frames(3);

        // ---- AD-94: the browsing gestures as one experience (PlanetsProbe.GalleryBrowse.cs).
        await GalleryBrowsing(dawn);
        await Key(Godot.Key.Escape);
        await Frames(3);

        // ---- inside the gallery: Sketches (its own pictures) is a gallery too; the Desk (one sketch among notes) is not.
        var sketches = Tree[gallery].Children.FirstOrDefault(c => Tree[c].Name == "Sketches", -1);
        if (sketches >= 0)
        {
            await GoToPath(Tree.FullPath(sketches));
            await WaitSettled();
            var sv = P.CurrentVisual!;
            await Until(() => sv.CanvasesShown >= 2, 6);
            Check(P.OnGallery && P.Composition!.Media.Reason == MediaReason.InsideGallery && sv.CanvasesShown >= 2, $"Sketches inherits the gallery ({P.Composition!.Media.Reason}): its {sv.CanvasesShown} pictures fill by themselves");
            await Shot("sketches-gallery");
        }

        var desk = Tree[gallery].Children.FirstOrDefault(c => Tree[c].Name == GallerySample.DeskName, -1);
        if (desk >= 0)
        {
            await GoToPath(Tree.FullPath(desk));
            await WaitSettled();
            await Seconds(1.5);
            var dv = P.CurrentVisual!;
            Check(!P.OnGallery && P.Composition!.Media.Character == MediaCharacter.SomePictures && dv.AutoThumbnailsRequested == 0 && dv.ThumbnailsShown == 0, $"the Desk (pictures among notes) is ordinary: nothing read by itself ({dv.AutoThumbnailsRequested} asked, {dv.ThumbnailsShown} shown)");
            await Shot("desk-ordinary");
            var sketch = P.Composition!.Items.FirstOrDefault(i => i.NodeId >= 0 && GalleryRules.IsPicture(Tree[i.NodeId].Name));
            if (sketch != null)
            {
                P.Approach(sketch.NodeId); // [direct]
                await Until(() => P.PreviewOpen && P.PreviewHasImage, 6);
                await Key(Godot.Key.R);
                await Until(() => v.Sharp || v.State == "refused", 5);
                Check(P.ViewerOpen && v.State == "shown" && v.Place.Count == 1, $"E then R there: the viewer, {v.Place.Count} picture to look at");
                await Key(Godot.Key.Escape);
                await Frames(3);
                Check(!P.ViewerOpen && dv.AutoThumbnailsRequested == 0, $"still nothing read by itself: only the picture you looked at ({dv.AutoThumbnailsRequested} asked, {dv.ThumbnailsShown} shown)");
            }

            await Key(Godot.Key.Escape);
        }

        if (sketches >= 0 || desk >= 0)
        {
            // Back to the gallery: arriving with its listing known, the title card says what it is.
            await GoToPath(Tree.FullPath(gallery));
            await WaitSettled();
            await Frames(3);
            Check(P.TitleDetail.Contains("picture gallery", StringComparison.Ordinal), $"back in the gallery, the title card says so: \"{P.TitleDetail}\"");
            await Until(() => P.CurrentVisual!.CanvasesShown >= pictureItems - (stress ? 3 : 0), 3);
            Log($"back in the gallery: {P.CurrentVisual!.CanvasesShown} canvases shown within 3 s; {P.CurrentVisual.AutoThumbnailsRequested} asked of Windows or decoded (the rest from the session's cache, {GalleryThumbnails.Cache.Count} kept, {GalleryThumbnails.Cache.Bytes / 1048576.0:0.0} MiB)");
            await Shot("gallery-title-card");
        }

        // ---- the crowds beside the fixture: 3, 30 and 300 pictures, galleries by their contents.
        var parent = System.IO.Path.GetDirectoryName(Tree.FullPath(gallery));
        foreach (var n in new[] { 3, 30, 300 })
        {
            var crowd = parent == null ? null : System.IO.Path.Combine(parent, $"Crowd of {n}");
            if (crowd == null || !System.IO.Directory.Exists(crowd)) continue;
            await GoToPath(crowd);
            await WaitSettled();
            await Until(() => P.Composition!.Items.Any(i => i.NodeId >= 0), 10);
            await CrowdMeasure($"crowd of {n}", n);
        }

        Log($"viewer: {v.DecodesStarted} loads started, {v.DecodesFinished} decodes finished, at most {v.MaxConcurrentDecodes} at once, at most {v.MaxCached} kept; decode times {string.Join(", ", v.DecodeMs.Select(ms => ms.ToString("0", System.Globalization.CultureInfo.InvariantCulture)))} ms");
        Log("sources: " + GalleryThumbnails.Summary());
        Log("folder seen: " + string.Join(" | ", seen));
    }

    /// <summary>Frames until every picture object's canvas has an answer (shown or failed) or <paramref name="seconds"/> pass.</summary>
    private async Task<(List<double> Frames, double Seconds)> WatchFill(PlanetVisual visual, int pictureItems, double seconds)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var frames = new List<double>();
        var last = sw.Elapsed.TotalMilliseconds;
        var target = Math.Min(pictureItems, PlanetVisual.Budget.MaxShown);
        while (sw.Elapsed.TotalSeconds < seconds && visual.CanvasesShown + visual.CanvasesFailed < target)
        {
            await Frames(1);
            var now = sw.Elapsed.TotalMilliseconds;
            frames.Add(now - last);
            last = now;
        }

        return (frames, sw.Elapsed.TotalSeconds);
    }

    /// <summary>The world as it fills: times to the first canvas, to half and to all; peaks; texture bytes; memory.</summary>
    private async Task CrowdMeasure(string label, int expectPictures, bool shots = true)
    {
        var visual = P.CurrentVisual!;
        var comp = P.Composition!;
        var items = comp.Items.Count(i => i.NodeId >= 0 && GalleryRules.IsPicture(Tree[i.NodeId].Name));
        var pile = comp.Items.Where(i => i.Kind == PlanetItemKind.Pile && i.Category == FileScape.Core.Interaction.FileCategory.Image).Sum(i => i.HiddenCount);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double first = -1, half = -1;
        var target = Math.Min(items, PlanetVisual.Budget.MaxShown);
        var frames = new List<double>();
        var last = 0.0;
        while (sw.Elapsed.TotalSeconds < 20 && visual.CanvasesShown + visual.CanvasesFailed < target)
        {
            await Frames(1);
            // A recompose (the listing finishing, a rescan) builds the planet again: follow the visual shown now
            // (instrument defect found on the real folder, 2026-11-20: the old visual stopped counting).
            if (P.CurrentVisual is { } now0 && !ReferenceEquals(now0, visual)) { visual = now0; comp = P.Composition!; }
            var now = sw.Elapsed.TotalMilliseconds;
            frames.Add(now - last);
            last = now;
            if (first < 0 && visual.CanvasesShown > 0) first = now;
            if (half < 0 && visual.CanvasesShown * 2 >= target) half = now;
        }

        var all = sw.Elapsed.TotalMilliseconds;
        await Seconds(1.5); // let the nearest sharpen
        visual = P.CurrentVisual!;
        frames.Sort();
        var p95 = frames.Count > 0 ? frames[(int)(frames.Count * 0.95)] : 0;
        var ws = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576.0;
        var tex = Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / 1048576.0;
        Log(F($"{label}: {comp.Media.Character} ({comp.Media.Reason}), {comp.Media.Pictures} pictures, {items} standing + {pile} in the pile, planet radius {comp.Shape.Radius:0.0} m; first canvas {first:0} ms, half {half:0} ms, all {all:0} ms; shown {visual.CanvasesShown}, sharp {visual.CanvasesSharp}, failed {visual.CanvasesFailed}; asked {visual.AutoThumbnailsRequested}; peaks in flight {visual.PeakInFlight}, shown {visual.PeakShown}, sharp {visual.PeakSharp}; canvas textures {visual.TextureBytes / 1048576.0:0.0} MiB; Godot texture memory {tex:0} MiB; working set {ws:0} MiB; session cache {GalleryThumbnails.Cache.Count} ({GalleryThumbnails.Cache.Bytes / 1048576.0:0.0} MiB); frames p95 {p95:0.0} ms, max {(frames.Count > 0 ? frames[^1] : 0):0.0} ms"));
        if (expectPictures > 0)
        {
            Check(comp.Media.IsGallery && items + pile == expectPictures, $"{label}: a gallery by its contents; every picture stands or is in the pile ({items} + {pile})");
            Check(visual.CanvasesShown >= target, $"{label}: every standing picture fills ({visual.CanvasesShown} of {target})");
        }

        Check(visual.PeakInFlight <= PlanetVisual.Budget.MaxInFlight && visual.PeakShown <= PlanetVisual.Budget.MaxShown && visual.PeakSharp <= PlanetVisual.Budget.MaxLarge, $"{label}: within the bounds (in flight {visual.PeakInFlight}, shown {visual.PeakShown}, sharp {visual.PeakSharp})");
        if (shots) await Shot($"{label.Replace(' ', '-')}-arrival");
    }

    /// <summary>
    /// PLANET-136 (DJ's "translucent yellow cube with glowing borders, pulsing about once a second" in Saved Pictures): the
    /// legacy views' selection box (Places and the maps draw it round the selection; highlight.gdshader, pale gold, a
    /// 0.8 Hz pulse) stayed drawn where it was when the view changed to Planets. Select in Places, come back: no box.
    /// </summary>
    private async Task StrayHighlightCheck()
    {
        var pick = P.Composition!.Items.FirstOrDefault(i => i.NodeId >= 0)?.NodeId ?? -1;
        App.SetExperience(Experience.Places); // [direct] what the Legacy views menu's Places item does
        await Seconds(1.0);
        if (pick >= 0) App.Selection.Select(pick); // [direct] a click on the file in Places
        await Frames(6);
        var inPlaces = App.LegacyHighlightVisible;
        App.SetExperience(Experience.Planets); // [direct] the toolbar's Planets button
        await WaitSettled();
        await Frames(4);
        var cam = P.ViewCamera;
        var seen = App.LegacyHighlightVisible && cam.IsPositionInFrustum(App.LegacyHighlightPosition);
        Log(F($"stray highlight: drawn in Places {inPlaces}; after Planets drawn {App.LegacyHighlightVisible}, at {App.LegacyHighlightPosition}, in the planets camera's view {seen}"));
        Check(!App.LegacyHighlightVisible, $"PLANET-136: no legacy selection box in Planets (drawn {App.LegacyHighlightVisible}, in view {seen})");
        await Shot("stray-highlight-after-places");
    }

    /// <summary>
    /// The gallery on a real picture folder (--automate=planets-gallery-real): counts, states, sources and times only,
    /// never a name (DJ's folders; AD-86's measurements).
    /// </summary>
    private async Task GalleryRealRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 60);
        await WaitSettled(60);
        await Until(() => P.Composition!.Items.Any(i => i.NodeId >= 0), 30);
        await Frames(2);
        var comp = P.Composition!;
        var here = App.Navigator.Current;
        var files = Tree[here].Children.Where(c => Tree[c].Kind == NodeKind.File).Select(c => Tree[c]).ToList();
        Log($"real folder: {files.Count} files, {files.Count(f => GalleryRules.IsPicture(f.Name))} pictures; cloud-only {files.Count(f => (f.Flags & NodeFlags.CloudPlaceholder) != 0)}, synced on this PC {files.Count(f => (f.Flags & (NodeFlags.CloudSynced | NodeFlags.CloudPlaceholder)) == NodeFlags.CloudSynced)}; verdicts {string.Join(", ", files.Where(f => GalleryRules.IsPicture(f.Name)).GroupBy(f => GalleryRules.Verdict(f)).Select(g => $"{g.Key} {g.Count()}"))}; child folders {Tree[here].Children.Count(c => Tree[c].IsDirectory)}");
        Log($"character: {comp.Media.Character} ({comp.Media.Reason}); title: \"{(P.TitleDetail.Contains("picture gallery", StringComparison.Ordinal) ? "a picture gallery…" : "(not a gallery)")}\"");
        await CrowdMeasure("real folder", 0);
        Log("sources: " + GalleryThumbnails.Summary());
        await StrayHighlightCheck();

        var first = comp.Items.Where(i => i.NodeId >= 0 && GalleryRules.IsPicture(Tree[i.NodeId].Name)).Select(i => i.NodeId).FirstOrDefault(-1);
        if (first < 0) return;
        P.Approach(first); // [direct]
        await Until(() => P.PreviewOpen && P.PreviewHasImage, 15);
        Log($"preview: open {P.PreviewOpen}, picture {P.PreviewHasImage}, text \"{(P.PreviewHasImage ? "" : "(no picture)")}\"");
        await Shot("real-preview");
        var v = P.Viewer;
        await Key(Godot.Key.R);
        await Frames(1);
        var firstFrame = v.State;
        await Until(() => v.Sharp || v.State == "refused", 30);
        Log($"viewer: first frame {firstFrame}; then {v.State} from {v.Source}, sharp {v.Sharp}, {v.ShownPixels.X}×{v.ShownPixels.Y} of {v.SourcePixels.X}×{v.SourcePixels.Y}{(v.Note.Length > 0 ? ", note: Windows' thumbnail only" : "")}");
        await Shot("real-viewer-1");
        for (var k = 0; k < Math.Min(6, v.Place.Count - 1); k++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Key(Godot.Key.Right);
            await Until(() => v.State == "shown", 10);
            var shownMs = sw.Elapsed.TotalMilliseconds;
            await Until(() => v.Sharp || v.State == "refused", 30);
            var n = Tree[v.Current];
            Log(F($"  step {k + 1}: {GalleryRules.Verdict(n)}, {((n.Flags & NodeFlags.CloudPlaceholder) != 0 ? "cloud-only" : "on this PC")}; shown in {shownMs:0} ms ({v.Source}), final in {sw.Elapsed.TotalMilliseconds:0} ms: {v.State} {v.Source} {v.ShownPixels.X}×{v.ShownPixels.Y}{(v.Note.Length > 0 ? " (Windows' thumbnail only)" : "")}{(v.State == "refused" ? " (refused)" : "")}"));
            if (k == 2) await Shot("real-viewer-step");
        }

        Log(F($"viewer quick pictures appeared in {string.Join(", ", v.QuickMs.Select(ms => ms.ToString("0", System.Globalization.CultureInfo.InvariantCulture)))} ms; decodes {string.Join(", ", v.DecodeMs.Select(ms => ms.ToString("0", System.Globalization.CultureInfo.InvariantCulture)))} ms"));
        if (System.Environment.GetCommandLineArgs().Concat(OS.GetCmdlineUserArgs()).Any(a => a == "--open-one"))
        {
            await Key(Godot.Key.Enter);
            await Frames(10);
            Log($"Enter: asked a question {App.ModalOpen}; shell: {(App.LastShellResult is { } r ? $"{(r.Succeeded ? "succeeded" : "failed")}{(r.WasMock ? " (recorded)" : "")}" : "none")}");
            Check(!App.ModalOpen, "Enter opens the picture with Windows without a question");
        }

        await Key(Godot.Key.Escape);
        await Frames(3);
        Log("sources: " + GalleryThumbnails.Summary());
        Log(F($"Windows thumbnails: {FileScape.Core.Shell.ShellThumbnails.CacheHits} from the cache, {FileScape.Core.Shell.ShellThumbnails.Extracted} extracted, {FileScape.Core.Shell.ShellThumbnails.Misses} none"));
    }
}
