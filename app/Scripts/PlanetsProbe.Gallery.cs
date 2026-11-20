using FileScape.App.Planets;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using FileScape.Core.Samples;
using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-gallery --root=&lt;fixture&gt;\Picture Gallery</c> (PLANET-96, AD-78; the fixture from
/// <c>FileScape.Fixtures create-gallery</c>, or <c>--root=gallery</c> for the app's own sample without the stress cases):
/// the gallery prototype played with injected input. <b>Bounded loading as you walk:</b> the marked folder's easels fill
/// with pictures by themselves, nearest first, never more than <see cref="PlanetVisual.MaxAutoThumbnails"/>, at most two
/// at once, the frame time watched while they load; the stress pictures (damaged, 63 Mpx, over 24 MiB) never stall the
/// world. <b>Inspect:</b> walk up to a picture [direct: the list's hop], E for its preview, R for the viewer; the picture
/// shown large and decoded no larger than <see cref="PictureViewer.ViewerMaxPixels"/>; → steps through the folder's
/// pictures (the large print shrunk, the refused ones saying why without a decode where metadata or the header decides);
/// the wheel zooms about the pointer, a drag pans, 0 fits; game keys do nothing while it is open; quick steps never run
/// more than one decode or keep more than <see cref="PictureViewer.CacheSize"/> pictures; Esc goes back to where you
/// stood with the preview on the last picture. <b>An ordinary folder</b> (the gallery's Sketches, unmarked) reads nothing
/// by itself, and the viewer still works from a deliberate E then R.
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
        Check(P.OnGallery, $"{Tree.DisplayName(gallery)} is a gallery (its marker), stress cases: {stress}");
        var visual = P.CurrentVisual!;

        // ---- the easels fill by themselves, bounded, while the frame time is watched.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var frames = new List<double>();
        var last = sw.Elapsed.TotalMilliseconds;
        var pictureItems = P.Composition!.Items.Count(i => i.NodeId >= 0 && GalleryRules.IsPicture(Tree[i.NodeId].Name));
        while (sw.Elapsed.TotalSeconds < 6 && visual.ThumbnailsShown < pictureItems - (stress ? 3 : 0))
        {
            await Frames(1);
            var now = sw.Elapsed.TotalMilliseconds;
            frames.Add(now - last);
            last = now;
        }

        await Seconds(1.0);
        frames.Sort();
        var p95 = frames.Count > 0 ? frames[(int)(frames.Count * 0.95)] : 0;
        Log(F($"easels: {visual.ThumbnailsShown} pictures shown of {pictureItems} picture objects in {sw.Elapsed.TotalSeconds:0.0} s; reads asked {visual.AutoThumbnailsRequested}; frames while loading: {frames.Count}, p95 {p95:0.0} ms, max {(frames.Count > 0 ? frames[^1] : 0):0.0} ms"));
        Check(visual.ThumbnailsShown >= pictureItems - (stress ? 3 : 0), $"the easels show their pictures by themselves ({visual.ThumbnailsShown} of {pictureItems}; the stress cases stay blank)");
        Check(visual.AutoThumbnailsRequested <= PlanetVisual.MaxAutoThumbnails, $"bounded: {visual.AutoThumbnailsRequested} reads asked ≤ {PlanetVisual.MaxAutoThumbnails}");
        Check(frames.Count == 0 || frames[^1] < 250, F($"the world kept moving while pictures loaded (longest frame {(frames.Count > 0 ? frames[^1] : 0):0} ms < 250 ms)"));
        await Shot("gallery-arrival-easels");

        // ---- look at one, then view it large.
        var dawn = Tree[gallery].Children.FirstOrDefault(c => Tree[c].Name == "dawn-over-the-low-hills.png", -1);
        Check(dawn >= 0 && P.Composition.Items.Any(i => i.NodeId == dawn), "the dawn picture stands on the planet");
        P.Approach(dawn); // [direct] as a click in the list: hop beside it and look closer
        await Until(() => P.PreviewOpen && P.PreviewHasImage, 6);
        await Frames(4);
        // The prompt names the object in focus: after the hop that is the picture, unless a neighbour stands nearer.
        Check(P.PreviewOpen && P.PreviewHasImage && (P.FocusNodeId != dawn || P.PromptText.StartsWith("R · view it large", StringComparison.Ordinal)), $"E: its preview, and the prompt offers the viewer when it is in focus (focus {(P.FocusNodeId >= 0 ? Tree[P.FocusNodeId].Name : "-")}): \"{P.PromptText}\"");
        var feet = P.Walker.Feet;
        var v = P.Viewer;
        await Shot("gallery-preview");
        await Key(Godot.Key.R);
        await Until(() => v.State != "loading", 5);
        var (i0, count) = v.Place;
        Check(P.ViewerOpen && v.Current == dawn && v.State == "shown" && App.KeyboardPanel == Main.KeyPanel.Viewer, $"R: the viewer shows it ({v.State}, {i0} of {count}, {v.ShownPixels.X} × {v.ShownPixels.Y} px), owning the keyboard");
        Check(v.FrameRect.Size.X > 900, F($"large: the picture spans {v.FrameRect.Size.X:0} × {v.FrameRect.Size.Y:0} of the window"));
        await Shot("gallery-viewer-dawn");

        // ---- step through the folder: every kind of answer.
        var seen = new List<string>();
        for (var step = 0; step < count; step++)
        {
            var started = v.DecodesStarted;
            await Key(Godot.Key.Right);
            await Until(() => v.State != "loading", 8);
            var n = Tree[v.Current];
            seen.Add($"{n.Name}: {v.State}{(v.State == "shown" ? $" {v.ShownPixels.X}×{v.ShownPixels.Y} (of {v.SourcePixels.X}×{v.SourcePixels.Y})" : $" — {v.Refusal}")}");
            Log($"  → {seen[^1]} (decodes started +{v.DecodesStarted - started})");
            Check(App.Selection.Selected == v.Current, $"  the world selects {n.Name}");
            if (v.State == "shown") Check(Math.Max(v.ShownPixels.X, v.ShownPixels.Y) <= PictureViewer.ViewerMaxPixels, $"  decoded within {PictureViewer.ViewerMaxPixels} px");
            switch (n.Name)
            {
                case "aurora-print-large.png":
                    Check(v.SourcePixels == new Vector2I(2560, 1920) && v.ShownPixels == new Vector2I(2048, 1536), $"  the large print is shrunk: {v.SourcePixels} → {v.ShownPixels}");
                    await Shot("gallery-viewer-large-print");
                    break;
                case GallerySample.OversizeName:
                    Check(v.State == "refused" && v.Refusal.Contains("over the", StringComparison.Ordinal) && v.DecodesStarted == started, $"  over 24 MiB: refused from its size, nothing read (\"{v.Refusal}\")");
                    await Shot("gallery-viewer-over-the-limit");
                    break;
                case GallerySample.HugeDimensionsName:
                    Check(v.State == "refused" && v.Refusal.Contains("million", StringComparison.Ordinal) && v.DecodeMs[^1] < 1000, F($"  63 Mpx: refused from its header in {v.DecodeMs[^1]:0} ms (\"{v.Refusal}\")"));
                    break;
                case GallerySample.DamagedName:
                    Check(v.State == "refused" && v.Refusal.Contains("decoded", StringComparison.Ordinal), $"  damaged: says so (\"{v.Refusal}\")");
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
        Check(v.MaxConcurrentDecodes <= 1 && v.MaxCached <= PictureViewer.CacheSize, $"bounded: at most {v.MaxConcurrentDecodes} decode at once, {v.MaxCached} pictures kept (≤ {PictureViewer.CacheSize})");

        // ---- quick steps: one decode at a time, only the latest wish kept.
        var before = v.DecodesStarted;
        for (var k = 0; k < 6; k++)
        {
            Key1(Godot.Key.Left, true);
            await Frames(1);
            Key1(Godot.Key.Left, false);
            await Frames(1);
        }

        await Until(() => v.State != "loading", 8);
        Log($"six quick ← in 12 frames: {v.DecodesStarted - before} decodes started, now on {Tree[v.Current].Name} ({v.State})");
        Check(v.DecodesStarted - before <= 6 && v.MaxConcurrentDecodes <= 1 && v.MaxCached <= PictureViewer.CacheSize, $"quick steps stay bounded: {v.DecodesStarted - before} decodes for 6 steps, ≤ 1 at once, ≤ {PictureViewer.CacheSize} kept");

        // ---- zoom, pan, fit.
        while (Tree[v.Current].Name != "aurora-print-large.png")
        {
            await Key(Godot.Key.Right);
            await Until(() => v.State != "loading", 8);
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

        // ---- an ordinary folder: nothing read by itself; the viewer still works when asked.
        var sketches = Tree[gallery].Children.FirstOrDefault(c => Tree[c].Name == "Sketches", -1);
        if (sketches >= 0)
        {
            await GoToPath(Tree.FullPath(sketches));
            await WaitSettled();
            await Seconds(2.0);
            var sv = P.CurrentVisual!;
            Check(!P.OnGallery && sv.AutoThumbnailsRequested == 0 && sv.ThumbnailsShown == 0, $"Sketches (no marker): no picture read by itself ({sv.AutoThumbnailsRequested} asked, {sv.ThumbnailsShown} shown)");
            await Shot("sketches-blank-easels");
            var sketch = P.Composition!.Items.FirstOrDefault(i => i.NodeId >= 0 && GalleryRules.IsPicture(Tree[i.NodeId].Name));
            if (sketch != null)
            {
                P.Approach(sketch.NodeId); // [direct]
                await Until(() => P.PreviewOpen && P.PreviewHasImage, 6);
                await Key(Godot.Key.R);
                await Until(() => v.State != "loading", 5);
                Check(P.ViewerOpen && v.State == "shown" && v.Place.Count == 2, $"E then R there: the viewer, {v.Place.Count} pictures to step through");
                await Shot("sketches-viewer");
                await Key(Godot.Key.Escape);
                await Frames(3);
                Check(!P.ViewerOpen && sv.AutoThumbnailsRequested == 0, $"still nothing read by itself: only the pictures you looked at ({sv.AutoThumbnailsRequested} asked)");
            }

            // Back to the gallery by history: arriving with its listing known, the title card says what it is.
            await Key(Godot.Key.Escape);
            await Key(Godot.Key.Backspace);
            await Until(() => P.Busy, 5);
            await Until(() => !P.Busy && App.Navigator.Current == gallery, 15);
            await Frames(3);
            Check(P.TitleDetail.Contains("picture gallery", StringComparison.Ordinal), $"back in the gallery, the title card says so: \"{P.TitleDetail}\"");
            await Shot("gallery-title-card");
        }

        Log($"viewer: {v.DecodesStarted} decodes started, {v.DecodesFinished} finished, at most {v.MaxConcurrentDecodes} at once, at most {v.MaxCached} kept; decode times {string.Join(", ", v.DecodeMs.Select(ms => ms.ToString("0", System.Globalization.CultureInfo.InvariantCulture)))} ms");
        Log("folder seen: " + string.Join(" | ", seen));
    }
}
