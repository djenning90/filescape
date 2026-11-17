using FileScape.App.Planets;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-actions --root=sample --vis=planets --test-shell=record</c> (PLANET-42..44, AD-57): the
/// repository arrival and file actions on the Starter Sky's Lantern Workshop. Clicks and double-clicks are injected
/// through the real input path at the projected screen point of what they aim at; the test shell records Open and
/// launches nothing. Stand-points and the bench's reveal are set [direct].
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>A double-click as Godot delivers one: press, release, then a press flagged DoubleClick, release.</summary>
    private async Task DoubleClickAt(Vector2 viewportPoint)
    {
        var at = ToWindow(viewportPoint);
        Inject(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        Mouse(MouseButton.Left, true, at);
        await Frames(2);
        Mouse(MouseButton.Left, false, at);
        await Frames(3);
        Inject(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        await Frames(2);
        Mouse(MouseButton.Left, false, at);
        await Frames(3);
    }

    /// <summary>A single click on the world at a viewport point (the press and release a click is).</summary>
    private async Task ClickWorldAt(Vector2 viewportPoint)
    {
        var at = ToWindow(viewportPoint);
        Inject(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        Mouse(MouseButton.Left, true, at);
        await Frames(2);
        Mouse(MouseButton.Left, false, at);
        await Frames(3);
    }

    /// <summary>[direct] Stands <paramref name="metres"/> from a surface direction, facing it, with the camera behind.</summary>
    private async Task StandFacing(NVector3 target, float metres)
    {
        var comp = P.Composition!;
        var r = comp.Shape.Radius;
        var away = P.Walker.Up - target * NVector3.Dot(P.Walker.Up, target);
        if (away.LengthSquared() < 1e-6f) away = PlanetShape.Tangents(target).A;
        var stand = PlanetFrame.Move(target, NVector3.Normalize(away), metres, r);
        var toward = target - stand * NVector3.Dot(target, stand);
        P.Walker.Teleport(comp.Surface, stand, NVector3.Normalize(toward));
        P.FaceCameraAlong(NVector3.Normalize(toward));
        await Seconds(0.5);
    }

    private async Task ActionsTour()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5);
        var root = App.Navigator.Current;
        var workshop = Child(root, "Lantern Workshop");
        App.Selection.Select(workshop); // [direct] the selection a click makes
        await Frames(2);
        await Fly(Godot.Key.G, workshop, "Lantern Workshop");
        await Seconds(1.2);
        Check(P.OnBenchWorld, "Lantern Workshop has its workbench");
        await Shot("actions-arrival");

        var sln = Child(workshop, "Lantern.sln");
        var src = Child(workshop, "src");
        Check(sln >= 0 && src >= 0, "the workshop has Lantern.sln and src");

        await FileActions(workshop, sln, src);
        await TwirlActions(workshop);
    }

    /// <summary>
    /// PLANET-43: a twirl by a file previews it (named first in the prompt) and never opens it; a twirl with nothing in
    /// reach is only a twirl; the bench's spin still reads the README.
    /// </summary>
    private async Task TwirlActions(int workshop)
    {
        var csproj = Child(workshop, "Lantern.csproj");
        var item = P.Composition!.Items.FirstOrDefault(i => i.NodeId == csproj);
        Check(item != null, "Lantern.csproj stands on the planet");
        if (item == null) return;
        App.Selection.Select(-1);
        await StandFacing(item.Direction, 2.2f);
        await Frames(5);
        Check(P.FocusNodeId == csproj && P.PromptText.Contains("twirl"), $"by Lantern.csproj the prompt names the twirl's preview first: \"{P.PromptText}\"");
        await Shot("twirl-prompt");
        var before = App.ShellLog.Count;
        var previews = P.TwirlPreviews;
        await Key(Godot.Key.Space);
        await Seconds(0.2);
        await Key(Godot.Key.Space);
        await Until(() => P.PreviewOpen, 2);
        await Until(() => P.Walker.Grounded, 3);
        await Seconds(0.5);
        Check(P.PreviewOpen && App.Selection.Selected == csproj && P.TwirlPreviews == previews + 1, $"a twirl by Lantern.csproj previews it (selected {Sel}, preview {P.PreviewOpen})");
        Check(App.ShellLog.Count == before, $"the twirl opened nothing ({App.ShellLog.Count - before} shell requests)");
        await Shot("twirl-preview");
        await Key(Godot.Key.Escape);
        await Frames(3);

        // Away from every file: a twirl is only a twirl.
        P.Walker.Teleport(P.Composition.Surface, P.Composition.Arrival.Up, P.Composition.Arrival.Facing); // [direct] the arrival meadow
        await Seconds(0.5);
        if (P.FocusNodeId < 0)
        {
            previews = P.TwirlPreviews;
            await Key(Godot.Key.Space);
            await Seconds(0.2);
            await Key(Godot.Key.Space);
            await Until(() => P.Walker.Grounded, 3);
            await Seconds(0.3);
            Check(!P.PreviewOpen && P.TwirlPreviews == previews && App.ShellLog.Count == before, "a twirl on the open meadow previews and opens nothing");
        }
        else Log($"(the meadow has {Tree[P.FocusNodeId].Name} in reach; the empty-twirl check is skipped)");
    }

    /// <summary>
    /// <c>--automate=planets-arrival</c> (PLANET-42): the arrival framing judged in motion. On the Starter Sky (the opening),
    /// Lantern Workshop (a repository: its workbench must be on screen), Music Box (large) and Quiet Moon (the smallest):
    /// photographs at landing +0.3 s and +1.5 s standing still, then while running (0.3, 0.8, 2 s of W), with the eye height,
    /// the camera's distance from the traveller and the framing weight logged; the framing must hold while standing and
    /// be gone (back to the close follow) after 2 s of running.
    /// </summary>
    private async Task ArrivalTour()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        var root = App.Navigator.Current;
        await Seconds(0.8);
        await Shot("arrival-opening-0.8s");
        await Seconds(2.6);
        await ArrivalFrames("Starter Sky", opening: true);
        foreach (var name in new[] { "Lantern Workshop", "Music Box", "Quiet Moon" })
        {
            var id = Child(root, name);
            if (id < 0) { Check(false, $"{name} exists in the sample"); continue; }
            App.Selection.Select(id); // [direct] the selection a click makes
            await Frames(2);
            await Fly(Godot.Key.G, id, name);
            if (name == "Lantern Workshop")
            {
                var bench = P.BenchOnScreen();
                Check(bench != null, $"the workbench is on screen as you land on {name}{(bench is { } b ? F($" at ({b.X:0},{b.Y:0})") : "")}");
                await Seconds(0.3);
                Check(App.Signs.ShownIds.Contains(PlanetWorld.BenchMarkerSignId), $"the workbench is named from the arrival, 0.3 s after landing ({string.Join(" | ", App.Signs.ShownText.Values.Take(8))})");
            }

            await ArrivalFrames(name, opening: false);
            await Key(Godot.Key.Backspace);
            await Until(() => App.Navigator.Current == root && !P.Busy, 10);
            await Seconds(0.8);
        }
    }

    /// <summary>The arrival sequence on the world just reached: standing, then running away from the camera.</summary>
    private async Task ArrivalFrames(string name, bool opening)
    {
        var tag = name.Replace(' ', '-').ToLowerInvariant();
        var r = P.Composition!.Shape.Radius;
        string State() => F($"eye {P.CameraEyeHeight():0.0} m, camera {P.CameraToTraveller():0.0} m from the traveller, framing {P.ArrivalFraming:0.00}");
        if (!opening)
        {
            await Seconds(0.3);
            Log($"{name} (radius {r:0.0} m) +0.3 s: {State()}");
            await Shot($"arrival-{tag}-0.3s");
        }

        await Seconds(1.2);
        var standingEye = P.CameraEyeHeight();
        Log($"{name} +1.5 s standing: {State()}");
        Check(P.ArrivalFraming > 0.99f && standingEye > 3.2f, F($"{name}: standing after the arrival keeps the higher view (eye {standingEye:0.0} m; the close follow is ≈ 2.2 m)"));
        await Shot($"arrival-{tag}-1.5s-standing");
        HoldKey(Godot.Key.W, true);
        foreach (var (t, wait) in new[] { (0.3, 0.3), (0.8, 0.5), (2.0, 1.2) })
        {
            await Seconds(wait);
            Log(F($"{name} running {t:0.0} s: {State()}"));
            await Shot(F($"arrival-{tag}-run-{t:0.0}s"));
        }

        HoldKey(Godot.Key.W, false);
        await Frames(2);
        // Back to the close follow: the framing's weight is 0 and the boom is the walking one (7.2 m, plus the follow lag).
        // (The eye's height over the ground below it is not compared: on a small world the ground falls away under any camera.)
        Check(P.ArrivalFraming < 0.01f && P.CameraToTraveller() < 8.6f,
            $"{name}: once moving the camera is back in close ({State()})");
    }

    private string Sel => App.Selection.Selected >= 0 ? Tree[App.Selection.Selected].Name : "-";

    /// <summary>What the shell recorded since <paramref name="before"/> entries.</summary>
    private string ShellSince(int before) => string.Join(" | ", App.ShellLog.Skip(before));

    /// <summary>
    /// PLANET-43: one rule for a world file icon (Lantern.sln's object), the workbench's cards (Lantern.sln, src) and a
    /// launcher pad (src): click selects; double-click opens a file through the deliberate Open (recorded) or goes to a
    /// folder; Enter opens the selection; a refused open is reported by name and reason. Every click's resolution is
    /// logged (hit test → dispatch → action).
    /// </summary>
    private async Task FileActions(int workshop, int sln, int src)
    {
        // ---- a world file icon (Lantern.sln's object on the planet)
        var item = P.Composition!.Items.FirstOrDefault(i => i.NodeId == sln);
        Check(item != null, "Lantern.sln stands on the planet as an object");
        if (item != null)
        {
            await StandFacing(item.Direction, 4f);
            var at = P.Camera.UnprojectPosition(P.ItemCentre(sln)!.Value);
            await Shot("actions-sln-object");
            App.Selection.Select(-1);
            var before = App.ShellLog.Count;
            var feet = P.Walker.Feet;
            await ClickWorldAt(at);
            await Seconds(0.4);
            Log($"trace: {P.LastClickTrace}");
            Check(App.Selection.Selected == sln && App.ShellLog.Count == before && (P.Walker.Feet - feet).Length() < 0.05f,
                $"click on the Lantern.sln object selects it, nothing more (selected {Sel}, shell +{App.ShellLog.Count - before})");
            await DoubleClickAt(at);
            await Seconds(1.2);
            Log($"trace: {P.LastClickTrace}");
            var hop = (P.Walker.Feet - feet).Length();
            Check(App.ShellLog.Count == before + 1 && App.ShellLog[^1].StartsWith("open ", StringComparison.Ordinal) && App.ShellLog[^1].EndsWith("Lantern.sln", StringComparison.Ordinal) && hop < 0.05f,
                F($"double-click on the Lantern.sln object opens it with the deliberate Open (shell: {ShellSince(before)}; the traveller moved {hop:0.00} m, no hop)"));
            Check(P.LastClickTrace.Contains("object or pad") && P.LastClickTrace.Contains("open"), $"the trace names the hit test and the action: {P.LastClickTrace}");
            Check(App.LastShellMessage?.Contains("Lantern.sln") == true, $"the open is reported: \"{App.LastShellMessage}\"");
            await Shot("actions-sln-object-double-clicked");

            // Enter: the same Open on the selection.
            before = App.ShellLog.Count;
            await Key(Godot.Key.Enter);
            await Frames(3);
            Check(App.ShellLog.Count == before + 1 && App.ShellLog[^1].EndsWith("Lantern.sln", StringComparison.Ordinal), $"Enter opens the selection the same way ({ShellSince(before)})");

            // A refused open (faked: the test shell fails the next open as Windows would with no app for .sln).
            Check(App.ProbeFailNextOpen(1155), "[instrument] the test shell will refuse the next open (error 1155)");
            before = App.ShellLog.Count;
            await DoubleClickAt(at);
            await Frames(4);
            Log($"trace: {P.LastClickTrace}");
            Check(ShellSince(before).StartsWith("open-failed", StringComparison.Ordinal) && App.LastShellMessage?.Contains("Could not open Lantern.sln: no app is set to open .sln files") == true,
                $"a refused open says what happened: \"{App.LastShellMessage}\"");
            await Shot("actions-open-refused");
            await Key(Godot.Key.Escape); // close the preview or selection card if one opened
            await Frames(3);
        }

        // ---- a launcher pad on the surface (src): double-click goes there
        if (P.Composition!.LauncherFor(src) is { } pad)
        {
            await StandFacing(pad.Direction, 5f);
            var padCentre = P.Composition.Surface.PointAt(pad.Direction, 0.3f);
            var at = P.Camera.UnprojectPosition(new Vector3(padCentre.X, padCentre.Y, padCentre.Z));
            var before = App.ShellLog.Count;
            await DoubleClickAt(at);
            await Frames(3);
            Log($"trace: {P.LastClickTrace}");
            Check(P.Busy && App.ShellLog.Count == before, $"double-click on the src pad goes there (phase {P.PhaseName}, nothing opened)");
            await Until(() => !P.Busy, 10);
            await Seconds(0.8);
            await Key(Godot.Key.Backspace);
            await Until(() => App.Navigator.Current == workshop && !P.Busy, 10);
            await Seconds(1);
        }

        // ---- the workbench's cards
        P.RevealBench("probe"); // [direct] as landing on it
        var ov = P.Composition!.Overlook;
        await StandFacing(ov.Direction, ov.Radius + 3.5f);
        await Seconds(1.5);
        async Task<Vector2?> CardAt(int id)
        {
            await Frames(1);
            var t = P.BenchTileCentre(id);
            if (t == null || P.Camera.IsPositionBehind(t.Value)) return null;
            return P.Camera.UnprojectPosition(t.Value);
        }

        if (await CardAt(sln) is { } card)
        {
            App.Selection.Select(-1);
            var before = App.ShellLog.Count;
            await ClickWorldAt(card);
            Log($"trace: {P.LastClickTrace}");
            Check(App.Selection.Selected == sln && App.ShellLog.Count == before, $"click on the Lantern.sln card selects it (selected {Sel})");
            card = await CardAt(sln) ?? card; // the ring turns slowly
            await DoubleClickAt(card);
            await Frames(3);
            Log($"trace: {P.LastClickTrace}");
            Check(App.ShellLog.Count == before + 1 && App.ShellLog[^1].EndsWith("Lantern.sln", StringComparison.Ordinal) && !P.Busy,
                $"double-click on the Lantern.sln card opens the solution (shell: {ShellSince(before)})");
            Check(P.LastClickTrace.Contains("bench card"), $"the trace says a card answered: {P.LastClickTrace}");
            await Shot("actions-card-sln-opened");
        }
        else Check(false, "the Lantern.sln card is on screen");

        if (await CardAt(src) is { } srcCard)
        {
            var before = App.ShellLog.Count;
            var transfers = P.Transfers;
            await DoubleClickAt(srcCard);
            await Frames(3);
            Log($"trace: {P.LastClickTrace}");
            Check(P.Busy && App.ShellLog.Count == before, $"double-click on the src card goes there (phase {P.PhaseName}, nothing opened)");
            await Until(() => !P.Busy, 10);
            Check(P.Transfers == transfers + 1 && App.Navigator.Current == src, "arrived in src");
            await Seconds(0.8);
            await Key(Godot.Key.Backspace);
            await Until(() => App.Navigator.Current == workshop && !P.Busy, 10);
            await Seconds(1);
        }
        else Check(false, "the src card is on screen");

        // Enter with nothing selected says so (never a silent no-op).
        App.Selection.Select(-1);
        var n = App.ShellLog.Count;
        await Key(Godot.Key.Enter);
        await Frames(3);
        Check(App.ShellLog.Count == n, "Enter with nothing selected opens nothing");
    }
}
