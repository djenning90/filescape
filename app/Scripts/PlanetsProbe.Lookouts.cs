using FileScape.App.Planets;
using FileScape.Core.Interaction;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-lookouts --root=sample --vis=planets --test-shell=record</c> (PLANET-55/56, PLANET-73..75): the
/// Starter Sky's telescope, a lantern that answers, the Music Box's bell tower and a Hearth world's lighthouse with real
/// keys. <b>Telescope:</b> far from the overlook its sign is hidden; walking up (W) it appears with the prompt
/// "E · up to the telescope"; E there lifts you onto the platform (PLANET-73: DJ pressed E where the sign showed and
/// nothing happened); on the platform E looks through it (AD-85, PLANET-125..127): the view rises to the eyepiece and
/// frames a world of this sky with its relation to the Starter Sky, → frames the next, Esc steps back to exactly where you
/// stood; a twirl beside it opens the same view; T from it opens the overview with the framed world chosen, Esc comes back
/// down to the platform; Enter from it departs (its pad first) and lands on the framed world. Then, from the foot again,
/// walking up the stones still reaches the top, and a file in reach keeps E. <b>Scenery</b> (PLANET-75): the nearest answering
/// piece shimmers; beside it the prompt says to twirl; a twirl makes it answer (it moves, nothing opens), and after two
/// replies its prompt steps aside. <b>Bell:</b> an ordinary jump and a twirl beside the tower ring nothing; W walks onto
/// the base and under the bell (it stands on the base, as drawn); an ordinary jump there rings nothing; a jump and twirl
/// rings it once (the bell swings) and opens nothing; walking on out between the far pillars works. <b>Lighthouse</b>
/// (PLANET-74): on the first Hearth world in the sample, its prompt at the foot; a twirl lights it (the beam sweeps and
/// stops), and so does E; nothing opens. The test shell records any start: nothing may be recorded here.
/// </summary>
public partial class PlanetsProbe
{
    private async Task LookoutsRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5);
        var root = App.Navigator.Current;
        var shell0 = App.ShellLog.Count;
        await TelescopeVisit();
        await Key(Godot.Key.Escape);
        await Frames(4);
        await SceneryVisit();

        var music = Child(root, "Music Box");
        App.Selection.Select(music); // [direct] the selection a click on its world makes
        await Frames(2);
        await Fly(Godot.Key.G, music, "Music Box");
        await Seconds(1.5);
        await BellVisit();
        await LighthouseVisit(root);
        Check(App.ShellLog.Count == shell0, $"nothing was started or recorded by the shell in this run ({App.ShellLog.Count - shell0})");
    }

    /// <summary>The telescope: hidden far away, the sign and the prompt on the walk up, E on the platform opens the list.</summary>
    private async Task TelescopeVisit()
    {
        var c = P.Composition!;
        var ov = c.Overlook;
        var r = c.Shape.Radius;
        // [direct] start 14 m out from the overlook on the arrival's side, facing it (where you come from).
        var away = NVector3.UnitY - ov.Direction * NVector3.Dot(NVector3.UnitY, ov.Direction);
        var start = PlanetFrame.Move(ov.Direction, NVector3.Normalize(away), ov.Radius + 14f, r);
        P.Walker.Teleport(c.Surface, start, ov.Direction - start);
        P.FaceCameraAlong(NVector3.Normalize(ov.Direction - start * NVector3.Dot(ov.Direction, start)));
        await Seconds(1.0);
        var far = P.TelescopeCue;
        Check(!far.Offered && !App.Signs.ShownIds.Contains(PlanetWorld.TelescopeSignId) && far.Cue < 0.05f, F($"14 m from the overlook the telescope's sign is hidden (cue {far.Cue:0.00})"));
        await Shot("lookouts-telescope-far");

        // Walk up to the foot of the stones (real keys).
        await WalkToward(ov.StepFoot, c.Overlook.Steps[0].Radius + 0.9f, 15);
        await Seconds(0.6);
        var near = P.TelescopeCue;
        var shown = App.Signs.ShownText.GetValueOrDefault(PlanetWorld.TelescopeSignId, "");
        Check(near.Cue > 0.6f && App.Signs.ShownIds.Contains(PlanetWorld.TelescopeSignId) && shown.Contains(PlanetWorld.TelescopeSignName),
            F($"at the foot of the stones the sign over the telescope shows \"{shown.Replace('\n', ' ')}\" (cue {near.Cue:0.00})"));
        Check(P.PromptText == UiText.Plain(PlanetWorld.TelescopeFootPrompt), $"the prompt says what E does here: \"{P.PromptText}\"");
        if (P.TelescopePoint is { } tp && !P.Camera.IsPositionBehind(tp))
        {
            var s = P.Camera.UnprojectPosition(tp);
            var size = App.GetViewport().GetVisibleRect().Size;
            Check(s.X > 0 && s.Y > 0 && s.X < size.X && s.Y < size.Y, F($"the telescope (and its sign above it) is on screen at ({s.X:0},{s.Y:0})"));
        }

        await Shot("lookouts-telescope-approach");

        // PLANET-73: E where the sign shows lifts you up beside the telescope (DJ pressed E here and nothing happened).
        var climbs0 = P.TelescopeClimbs;
        await Key(Godot.Key.E);
        await Until(() => P.OnOverlook && P.Walker.Grounded, 4);
        await Seconds(0.8);
        var top = P.TelescopeCue;
        Check(P.TelescopeClimbs == climbs0 + 1 && P.OnOverlook && MathF.Abs(P.Walker.Support.Value - ov.Top) < 0.05f && !P.ListOpen,
            F($"E at the foot lifts you onto the platform: support {P.Walker.Support.Value:0.00} m (top {ov.Top:0.00} m), the overlook view, nothing opened"));
        Check(App.Signs.ShownIds.Contains(PlanetWorld.TelescopeSignId) && top.Cue > 0.9f, F($"on the platform the sign is shown and the telescope lit: cue {top.Cue:0.00}"));
        Check(P.PromptText == UiText.Plain(PlanetWorld.TelescopeTopPrompt), $"the prompt says E and the twirl look through it: \"{P.PromptText}\"");
        await Shot("lookouts-telescope-top");
        await TelescopeViewVisit(root: App.Navigator.Current);

        // The stones still climb (the walking route is unchanged): from the foot, W up each stone in turn.
        var outward = ov.StepFoot - ov.Direction * NVector3.Dot(ov.StepFoot, ov.Direction);
        var foot = PlanetFrame.Move(ov.StepFoot, NVector3.Normalize(outward - ov.StepFoot * NVector3.Dot(outward, ov.StepFoot)), ov.Steps[0].Radius + 1.2f, r);
        P.Walker.Teleport(c.Surface, foot, ov.StepFoot - foot); // [direct] back at the foot of the stones, facing the first
        P.FaceCameraAlong(NVector3.Normalize(ov.StepFoot - foot * NVector3.Dot(ov.StepFoot, foot)));
        await Seconds(0.8);
        Check(!P.OnOverlook && P.PromptText == UiText.Plain(PlanetWorld.TelescopeFootPrompt), $"back at the foot: \"{P.PromptText}\"");
        foreach (var step in ov.Steps) await WalkToward(step.Direction, step.Radius * 0.6f, 6);
        await WalkToward(ov.Direction, ov.Radius * 0.5f, 6);
        await Until(() => P.OnOverlook, 3);
        Check(P.OnOverlook && P.TelescopeClimbs == climbs0 + 1, F($"walking up the stones still reaches the platform (support {P.Walker.Support.Value:0.00} m; E not used)"));

        // A file within the telescope's reach keeps E (its own prompt), if this world has one there.
        var fileNear = c.Items.Where(i => i.NodeId >= 0 && i.Kind != PlanetItemKind.Pile && PlanetShape.Angle(i.Direction, ov.Direction) * r - ov.Radius is > 1.5f and < 5.5f)
            .OrderBy(i => PlanetShape.Angle(i.Direction, ov.Direction)).FirstOrDefault();
        if (fileNear != null)
        {
            var side = fileNear.Direction - ov.Direction;
            side -= fileNear.Direction * NVector3.Dot(side, fileNear.Direction);
            var stand = PlanetFrame.Move(fileNear.Direction, -NVector3.Normalize(side), 1.2f * fileNear.Scale + 0.6f, r);
            P.Walker.Teleport(c.Surface, stand, fileNear.Direction - stand); // [direct] beside the file, on the overlook's side
            P.FaceCameraAlong(NVector3.Normalize(fileNear.Direction - stand * NVector3.Dot(fileNear.Direction, stand)));
            await Seconds(0.6);
            var climbs1 = P.TelescopeClimbs;
            Check(P.FocusNodeId == fileNear.NodeId && !P.PromptText.Contains("telescope"), F($"beside {Tree[fileNear.NodeId].Name} ({P.DistanceTo(ov.Direction) - ov.Radius:0.0} m from the overlook's edge) the file has the prompt: \"{P.PromptText}\""));
            await Key(Godot.Key.E);
            await Seconds(0.5);
            Check(P.PreviewOpen && P.TelescopeClimbs == climbs1, "E there looks closer at the file, not up to the telescope");
            await Key(Godot.Key.Escape);
            await Frames(4);
        }
        else Log("(no file stands within the telescope's reach on this world: the file-keeps-E check is covered where one does)");
    }

    /// <summary>
    /// AD-85 (PLANET-125..127) on the platform: E looks through the telescope; the view frames worlds and says how each
    /// relates to this one; → steps; Esc steps back to exactly where you stood; a twirl opens it too; T from it opens the
    /// overview on the framed world; Enter departs to it. Ends back on <paramref name="root"/>'s platform.
    /// </summary>
    private async Task TelescopeViewVisit(int root)
    {
        var here = Tree.DisplayName(root);
        var feet = P.Walker.Feet;
        var views0 = P.TelescopeViews;
        await Key(Godot.Key.E);
        await Until(() => P.TelescopeViewOpen, 2);
        await Seconds(1.6); // the rise (0.9 s) and the swing to the first world
        Check(P.TelescopeViewOpen && P.TelescopeViews == views0 + 1 && P.LastTelescopeEntry == "E" && !P.ListOpen,
            $"E at the telescope looks through it: {P.TelescopeTargetCount} worlds framed, no list");
        var first = P.TelescopeTargetId;
        var caption = P.TelescopeCaption;
        Check(first >= 0 && caption.Contains(here, StringComparison.Ordinal) && caption.StartsWith(Tree.DisplayName(first), StringComparison.Ordinal),
            $"it frames {(first >= 0 ? Tree.DisplayName(first) : "nothing")} and says how it relates to {here}: \"{caption.Replace("\n", " / ")}\"");
        Check(P.TelescopeAimErrorDegrees is >= 0 and < 4f, F($"the view has swung onto it ({P.TelescopeAimErrorDegrees:0.0}° off its centre) through a narrower lens ({P.Camera.Fov:0.#}°)"));
        Check(P.Camera.Fov < 50f && !P.Avatar.Visible, F($"the eye at the eyepiece: lens {P.Camera.Fov:0.#}° (walking 62°), the traveller out of the view"));
        Check(App.Selection.Selected == first, "the framed world is the selection (its card shows it)");
        Check(P.MusicExperience == Core.Audio.MusicExperience.Overlook, "the Lydian overlook plays on through the telescope");
        await Shot("lookouts-telescope-view");
        if (P.TelescopeTargetCount > 1)
        {
            await Key(Godot.Key.Right);
            await Seconds(1.2);
            Check(P.TelescopeTargetId != first && P.TelescopeTargetId >= 0 && P.TelescopeAimErrorDegrees < 4f,
                F($"→ frames the next world round the sky: {Tree.DisplayName(P.TelescopeTargetId)} ({P.TelescopeAimErrorDegrees:0.0}° off)"));
            await Shot("lookouts-telescope-next");
        }

        await Key(Godot.Key.W); // a held key zooms in, a tap barely; it must not walk
        await Key(Godot.Key.Escape);
        await Until(() => !P.TelescopeViewOpen, 1);
        await Until(() => P.CameraSettled, 3);
        await Frames(10);
        Check(!P.TelescopeViewOpen && NVector3.Distance(P.Walker.Feet, feet) < 0.01f && P.OnOverlook && P.Avatar.Visible,
            F($"Esc steps back to exactly where you stood ({NVector3.Distance(P.Walker.Feet, feet) * 100:0.0} cm), the traveller in view"));
        Check(P.Camera.Fov > 55f, F($"  and the lens widens back ({P.Camera.Fov:0.#}°)"));
        await Shot("lookouts-telescope-back");

        // The jump and twirl beside it does the same (as at the lighthouse).
        await JumpAndTwirl();
        await Until(() => P.TelescopeViewOpen, 2);
        Check(P.TelescopeViewOpen && P.LastTelescopeEntry == "twirl", $"a jump and twirl beside the telescope looks through it too ({P.LastTelescopeEntry})");
        await Seconds(1.2);
        feet = P.Walker.Feet; // the twirl may land a few centimetres from where it took off

        // T from the view: the overview with the framed world chosen; Esc comes back down to the platform.
        var framed = P.TelescopeTargetId;
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 2);
        await Seconds(1.0);
        Check(P.UniverseOpen && !P.TelescopeViewOpen && App.Selection.Selected == framed, $"T from the telescope opens the overview with {Tree.DisplayName(framed)} chosen");
        await Key(Godot.Key.Escape);
        await Until(() => !P.UniverseOpen, 2);
        await Until(() => P.CameraSettled, 3);
        await Seconds(0.5);
        Check(NVector3.Distance(P.Walker.Feet, feet) < 0.05f && P.OnOverlook, F($"and Esc there comes back down to the platform ({NVector3.Distance(P.Walker.Feet, feet) * 100:0.0} cm)"));

        // Enter from the view: the departure every trip uses (its pad first), then the landing on the framed world.
        await Key(Godot.Key.E);
        await Until(() => P.TelescopeViewOpen, 2);
        await Seconds(1.2);
        var go = P.TelescopeTargetId;
        var transfers = P.Transfers;
        await Key(Godot.Key.Enter);
        await Until(() => P.Busy, 2);
        Check(!P.TelescopeViewOpen && P.Busy, $"Enter in the telescope departs for {Tree.DisplayName(go)} ({P.LastDepartureKind})");
        await Until(() => !P.Busy && App.Navigator.Current == go, 12);
        Check(App.Navigator.Current == go && P.Transfers == transfers + 1 && P.Walker.Grounded, $"  and lands there on foot: {Tree.DisplayName(App.Navigator.Current)}");
        await Seconds(1.0);
        await Key(Godot.Key.Backspace);
        await Until(() => !P.Busy && App.Navigator.Current == root, 12);
        await Seconds(1.0);
    }

    /// <summary>
    /// Scenery that answers (PLANET-75): the answering piece clearest of files and pads; it shimmers from a few metres; W
    /// up beside it shows the twirl prompt; a twirl makes it answer (it moves, nothing opens); after two replies the
    /// prompt steps aside (the shimmer stays).
    /// </summary>
    private async Task SceneryVisit()
    {
        var c = P.Composition!;
        var r = c.Shape.Radius;
        var kinds = new[] { FileScape.Core.Planets.DecorationKind.Mushroom, FileScape.Core.Planets.DecorationKind.Crystal, FileScape.Core.Planets.DecorationKind.Lamp };
        float Clearance(NVector3 d) => c.Items.Select(i => PlanetShape.Angle(i.Direction, d) * r).Concat(c.Launchers.Select(l => PlanetShape.Angle(l.Direction, d) * r - 2f)).DefaultIfEmpty(99f).Min();
        var pick = c.Decorations.Select((d, i) => (d, i)).Where(x => kinds.Contains(x.d.Kind)).OrderByDescending(x => MathF.Min(8f, Clearance(x.d.Direction))).ThenBy(x => PlanetShape.Angle(x.d.Direction, NVector3.UnitY)).FirstOrDefault();
        Log($"answering scenery here: {string.Join(", ", kinds.Select(k => $"{c.Decorations.Count(d => d.Kind == k)} {k}"))}");
        if (pick.d == null)
        {
            Check(false, "this world has a piece of scenery that answers");
            return;
        }

        var (piece, index) = pick;
        var clear = Clearance(piece.Direction);
        // [direct] 4 m from it, on the side away from the nearest file or pad, facing it.
        var nearest = c.Items.Select(i => i.Direction).Concat(c.Launchers.Select(l => l.Direction)).OrderBy(d => PlanetShape.Angle(d, piece.Direction)).FirstOrDefault();
        var away = nearest == default ? PlanetFrame.North(piece.Direction) : piece.Direction - nearest;
        away -= piece.Direction * NVector3.Dot(away, piece.Direction);
        var start = PlanetFrame.Move(piece.Direction, NVector3.Normalize(away), 4f, r);
        P.Walker.Teleport(c.Surface, start, piece.Direction - start);
        P.FaceCameraAlong(NVector3.Normalize(piece.Direction - start * NVector3.Dot(piece.Direction, start)));
        await Seconds(0.8);
        Check(P.SceneryCueIndex == index, F($"4 m from a {piece.Kind} ({clear:0.0} m clear of files and pads) it shimmers (cue on {P.SceneryCueIndex}, the piece is {index})"));
        await Shot("lookouts-scenery-shimmer");
        await WalkToward(piece.Direction, 1.1f, 8);
        await Seconds(0.3);
        Check(P.PromptText.StartsWith("Space, Space · twirl by the", StringComparison.Ordinal), $"beside it the prompt says how: \"{P.PromptText}\"");
        var replies0 = P.SceneryReplies;
        var twirls0 = P.TwirlPreviews;
        var played = false;
        await JumpAndTwirl(() => played |= P.SceneryPlaying, shot: "lookouts-scenery-answers");
        Check(P.SceneryReplies == replies0 + 1 && played && P.LastSceneryKind == piece.Kind, F($"a twirl beside the {piece.Kind} makes it answer (replies +{P.SceneryReplies - replies0}, it moved: {played})"));
        Check(!P.PreviewOpen && !P.ListOpen && P.TwirlPreviews == twirls0, "it opens nothing");
        await Seconds(1.0);
        Check(!P.SceneryPlaying, "and it is brief: still again within a second");
        await JumpAndTwirl();
        await Seconds(0.4);
        Check(P.SceneryReplies == replies0 + 2, "a second twirl, a second reply (pleasant to repeat)");
        Check(!P.PromptText.Contains("twirl by the"), $"after two replies its prompt steps aside: \"{P.PromptText}\" (the shimmer stays: cue on {P.SceneryCueIndex})");
        using (var doc = System.Text.Json.JsonDocument.Parse(App.StateJson()))
        {
            var pl = doc.RootElement.GetProperty("planets");
            Check(pl.GetProperty("sceneryReplies").GetInt32() == P.SceneryReplies && pl.GetProperty("sceneryDistance").GetSingle() < 2f && pl.GetProperty("sceneryKind").GetString() == piece.Kind.ToString(),
                F($"  state file: sceneryKind {pl.GetProperty("sceneryKind").GetString()}, sceneryDistance {pl.GetProperty("sceneryDistance").GetSingle():0.00} m, sceneryReplies {pl.GetProperty("sceneryReplies").GetInt32()}"));
        }
    }

    /// <summary>
    /// The lighthouse (PLANET-74, AD-70): on the sample's first Hearth world (by path, as Ctrl+L does), walk to its foot;
    /// the prompt says E or a twirl lights it; a twirl does (the beam sweeps, then stops), and so does E; nothing opens.
    /// </summary>
    private async Task LighthouseVisit(int root)
    {
        var hearth = HearthWorld(root);
        if (hearth < 0)
        {
            Check(false, "the sample has a Hearth world (the lighthouse)");
            return;
        }

        await GoToPath(Tree.FullPath(hearth));
        await Seconds(1.5);
        var c = P.Composition!;
        Check(c.Theme == PlanetTheme.Hearth && c.FolderId == hearth, $"on {Tree.DisplayName(hearth)}, a Hearth world with the lighthouse ({c.Theme})");
        var lm = c.Landmark!;
        var r = c.Shape.Radius;
        var start = PlanetFrame.Move(lm.Direction, PlanetFrame.North(lm.Direction), lm.Radius + 7f, r);
        P.Walker.Teleport(c.Surface, start, lm.Direction - start); // [direct] 7 m out from its footprint, facing it
        P.FaceCameraAlong(NVector3.Normalize(lm.Direction - start * NVector3.Dot(lm.Direction, start)));
        await Seconds(1.0);
        Check(P.PromptText != UiText.Plain(PlanetWorld.LighthousePrompt), $"from afar it is scenery: \"{P.PromptText}\"");
        await WalkToward(lm.Direction, lm.Radius + 1.0f, 10);
        await Seconds(0.4);
        Check(P.PromptText == UiText.Plain(PlanetWorld.LighthousePrompt), F($"at its foot ({P.DistanceTo(lm.Direction):0.0} m from its axis) the prompt: \"{P.PromptText}\""));
        await Shot("lookouts-lighthouse-foot");
        var lights0 = P.LighthouseLights;
        var beam = false;
        await JumpAndTwirl(() => beam |= P.LighthouseBeam, shot: "lookouts-lighthouse-twirl");
        Check(P.LighthouseLights == lights0 + 1 && beam, $"a twirl at its foot lights it: the beam sweeps (lights +{P.LighthouseLights - lights0})");
        await Seconds(0.6);
        await Shot("lookouts-lighthouse-beam");
        await Until(() => !P.LighthouseBeam, 5);
        Check(!P.LighthouseBeam, "and it stops by itself (brief)");
        await Key(Godot.Key.E);
        await Frames(4);
        Check(P.LighthouseLights == lights0 + 2 && P.LighthouseBeam, "E there lights it too");
        Check(!P.PreviewOpen && !P.ListOpen, "lighting it opens nothing");
        // [direct] step back 12 m to see the sweep whole (evidence: at the foot the beam passes overhead).
        var back = PlanetFrame.Move(lm.Direction, PlanetFrame.North(lm.Direction), lm.Radius + 12f, r);
        P.Walker.Teleport(c.Surface, back, lm.Direction - back);
        P.FaceCameraAlong(NVector3.Normalize(lm.Direction - back * NVector3.Dot(lm.Direction, back)));
        await Seconds(0.5);
        await Shot("lookouts-lighthouse-sweep");
        using var doc = System.Text.Json.JsonDocument.Parse(App.StateJson());
        var pl = doc.RootElement.GetProperty("planets");
        Check(pl.GetProperty("lighthouse").GetBoolean() && pl.GetProperty("lighthouseLights").GetInt32() == P.LighthouseLights && MathF.Abs(pl.GetProperty("lighthouseDistance").GetSingle() - (lm.Radius + 12f)) < 0.5f && pl.GetProperty("lighthouseBeam").GetBoolean(),
            F($"  state file: lighthouseLights {pl.GetProperty("lighthouseLights").GetInt32()}, lighthouseDistance {pl.GetProperty("lighthouseDistance").GetSingle():0.0} m, beam {pl.GetProperty("lighthouseBeam").GetBoolean()}"));
    }

    /// <summary>The shallowest folder under <paramref name="root"/> whose world is Hearth (the lighthouse), or −1.</summary>
    private int HearthWorld(int root)
    {
        var queue = new Queue<int>([root]);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (id != root && PlanetComposer.ThemeOf(Tree, id) == PlanetTheme.Hearth) return id;
            foreach (var ch in Tree[id].Children)
                if (Tree.Contains(ch) && Tree[ch].IsDirectory) queue.Enqueue(ch);
        }

        return -1;
    }

    /// <summary>The bell tower: jumps beside it ring nothing; walk under the bell; a jump there rings nothing; a twirl rings it.</summary>
    private async Task BellVisit()
    {
        var c = P.Composition!;
        var lm = c.Landmark;
        Check(lm.Bell && c.Theme == PlanetTheme.Chime, $"Music Box has the bell tower ({c.Theme})");
        if (P.BellTowerApproach(5f) is not { } at) return;
        var r = c.Shape.Radius;
        P.Walker.Teleport(c.Surface, at.Outside, at.Axis - at.Outside); // [direct] 5 m out on the arrival's side, facing it
        P.FaceCameraAlong(NVector3.Normalize(at.Axis - at.Outside * NVector3.Dot(at.Axis, at.Outside)));
        await Seconds(1.0);
        await Shot("lookouts-bell-approach");
        var rings0 = P.BellRings;

        // Beside the tower (outside its base): an ordinary jump, then a jump and twirl: just jumps.
        await Key(Godot.Key.Space);
        await Until(() => P.Walker.Grounded, 3);
        await Seconds(0.3);
        await JumpAndTwirl();
        Check(P.BellRings == rings0 && !P.PreviewOpen && !P.ListOpen, F($"beside the tower ({P.DistanceTo(lm.Direction):0.0} m from its axis) a jump and a twirl are only jumps: rings {P.BellRings - rings0}"));

        // Walk in (real keys) onto the base and under the bell.
        await WalkToward(lm.Direction, 0.35f, 10);
        await Seconds(0.6);
        var d = P.DistanceTo(lm.Direction);
        Check(P.UnderBell && d < 0.9f, F($"walked onto the base and under the bell: {d:0.00} m from its axis (the old solid stopped you 1.95 m out)"));
        Check(MathF.Abs(P.Walker.Support.Value - BellTower.BaseTopMetres) < 0.02f, F($"standing on the base as drawn: support {P.Walker.Support.Value:0.00} m (base top {BellTower.BaseTopMetres:0.00} m)"));
        Check(P.PromptText.Contains("twirl to ring the bell"), $"the prompt says how to ring it: \"{P.PromptText}\"");
        await Shot("lookouts-bell-under");

        // An ordinary jump under the bell: nothing.
        await Key(Godot.Key.Space);
        await Until(() => P.Walker.Grounded, 3);
        await Seconds(0.4);
        Check(P.BellRings == rings0, "an ordinary jump under the bell does not ring it");

        // Jump and twirl under the bell: it rings once and swings.
        var swing = 0f;
        var apex = await JumpAndTwirl(() => swing = MathF.Max(swing, MathF.Abs(P.BellSwing)), shot: "lookouts-bell-twirl");
        Check(apex + BellTower.TravellerHeightMetres < BellTower.BellRimMetres, F($"the twirl stays under the bell: feet up to {apex:0.00} m + the traveller {BellTower.TravellerHeightMetres:0.00} m < the rim at {BellTower.BellRimMetres:0.00} m"));
        Check(P.BellRings == rings0 + 1, $"a jump and twirl under the bell rings it once ({P.BellRings - rings0})");
        for (var i = 0; i < 40; i++) { await Frames(1); swing = MathF.Max(swing, MathF.Abs(P.BellSwing)); }
        Check(swing > 0.1f, F($"the bell swings: up to {swing * 180 / MathF.PI:0.0}°"));
        await Shot("lookouts-bell-swing");
        Check(!P.PreviewOpen && !P.ListOpen, "ringing opens nothing");
        // The state file carries what the real-input probe (os-planets-probe.ps1 -Landmarks) steers and judges by.
        using (var doc = System.Text.Json.JsonDocument.Parse(App.StateJson()))
        {
            var pl = doc.RootElement.GetProperty("planets");
            Check(pl.GetProperty("bellRings").GetInt32() == P.BellRings && pl.GetProperty("underBell").GetBoolean() && pl.GetProperty("bellDistance").GetSingle() < 1.3f && pl.GetProperty("overlookEdge").GetSingle() > 0,
                F($"  state file: bellRings {pl.GetProperty("bellRings").GetInt32()}, underBell, bellDistance {pl.GetProperty("bellDistance").GetSingle():0.00} m, overlookEdge {pl.GetProperty("overlookEdge").GetSingle():0.0} m"));
        }
        await Seconds(1.5);

        // Again: a second twirl rings it a second time.
        await JumpAndTwirl();
        Check(P.BellRings == rings0 + 2, $"another twirl rings it again ({P.BellRings - rings0})");

        // And out through the far side, between the pillars.
        var through = PlanetFrame.Move(lm.Direction, -PlanetFrame.North(lm.Direction), 4f, r);
        await WalkToward(through, 0.6f, 8);
        Check(P.DistanceTo(through) < 1.2f, F($"walked on out between the far pillars ({P.DistanceTo(through):0.0} m from the far side)"));
        Log($"the walk: {P.Walker.Jumps} jumps, {P.Walker.AirJumps} twirls in all; bell rings {P.BellRings}");
    }

    /// <summary>Space held 0.3 s (a full jump), released, then pressed again in the air (the twirl), as a player does; <paramref name="each"/> runs every frame until landing.</summary>
    private async Task<float> JumpAndTwirl(Action? each = null, string? shot = null)
    {
        var air0 = P.Walker.AirJumps;
        var apex = 0f;
        Key1(Godot.Key.Space, true);
        await Seconds(0.3);
        Key1(Godot.Key.Space, false);
        await Frames(2);
        Key1(Godot.Key.Space, true);
        for (var i = 0; i < 90 && !(i > 10 && P.Walker.Grounded); i++)
        {
            await Frames(1);
            each?.Invoke();
            apex = MathF.Max(apex, P.Walker.Height.Value);
            if (shot != null && i == 12) await Shot(shot);
        }

        Key1(Godot.Key.Space, false);
        await Until(() => P.Walker.Grounded, 3);
        await Frames(4);
        Check(P.Walker.AirJumps == air0 + 1, F($"  (one twirl, feet up to {apex:0.00} m)"));
        return apex;
    }
}
