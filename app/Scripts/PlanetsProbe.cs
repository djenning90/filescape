using FileScape.App.Places;
using FileScape.App.Planets;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// The planetary world's journeys in the exported app (PLANET-10..16). Input is injected through the same path as
/// real devices (keys, typing, Enter); set-up steps that call the world directly are labelled [direct].
/// <list type="bullet">
/// <item><c>--automate=planets-journey --root=sample --test-shell=record</c>: the first minutes on the Starter Sky —
/// arrive, see a destination, walk, jump and twirl, the overlook, stand on a launcher and fly to the picture meadow,
/// inspect a picture (its preview), open it (recorded, nothing starts), switch to Places and back (same folder,
/// selection, history), fly back, take the direct route to a world that is not in the sky (a warp), a folder too large
/// for objects (piles and the list), the empty moon, the workshop's README and the music box.</item>
/// <item><c>--automate=planets-fixture --root=&lt;Wayfarer&gt; [--wsl-root=\\wsl.localhost\…]</c>: the same system on a
/// bounded real fixture: links, an empty room, hidden entries, and (if given) a WSL folder.</item>
/// </list>
/// Measured throughout: frame times while walking and flying, the camera's roll rate, launch phase timing, and the
/// traveller's screen position across each landing (it must not jump: there is no cut).
/// </summary>
public partial class PlanetsProbe : ProbeBase
{
    private readonly List<(double Ms, string Phase)> _frames = [];
    private bool _recording;
    private Vector2 _lastAvatarScreen = new(float.NaN, float.NaN);
    private float _maxLandingScreenJump, _maxFlightScreenJump;
    private string _lastPhase = "";
    private float _maxFlightJumpAt;
    private int _flightLogLines;

    public PlanetsProbe(Main main, string outDir, string mode) : base(main, outDir, mode)
    {
    }

    private PlanetWorld P => App.Planets;
    private FsTree Tree => App.Tree!;

    public override void _Process(double delta)
    {
        if (!_recording || !P.Active) return;
        var phase = P.PhaseName;
        _frames.Add((delta * 1000, phase));
        // The traveller's place on screen, frame to frame: across a landing it must stay put (no cut).
        var cam = P.Camera;
        var at = P.Avatar.GlobalPosition + P.Avatar.GlobalBasis.Y * 0.6f;
        var screen = cam.IsPositionBehind(at) ? new Vector2(float.NaN, float.NaN) : cam.UnprojectPosition(at);
        var size = App.GetViewport().GetVisibleRect().Size;
        if (!float.IsNaN(_lastAvatarScreen.X) && !float.IsNaN(screen.X))
        {
            var jump = screen.DistanceTo(_lastAvatarScreen) / size.Y;
            if (_lastPhase == "Flying" && phase == "Walking") _maxLandingScreenJump = MathF.Max(_maxLandingScreenJump, jump);
            if (phase == "Flying" && _lastPhase == "Flying" && jump > _maxFlightScreenJump)
            {
                _maxFlightScreenJump = jump;
                _maxFlightJumpAt = P.FlightProgress;
            }
        }

        if (phase == "Flying" && P.FlightProgress < 0.12f && _flightLogLines < 40)
        {
            _flightLogLines++;
            Log(F($"flight u={P.FlightProgress:0.000} dt={delta * 1000:0.0} ms avatar screen=({screen.X:0},{screen.Y:0}) camera→avatar {cam.GlobalPosition.DistanceTo(at):0.0} m scale {P.Avatar.Scale.X:0.00}"));
        }

        _lastAvatarScreen = screen;
        _lastPhase = phase;
    }

    protected override async Task RunProbe()
    {
        if (Mode == "planets-fixture") await FixtureTour();
        else await Journey(reel: Mode == "planets-reel");
        ReportPerf();
    }

    private async Task WaitSettled(double timeout = 20)
    {
        await Until(() => App.Experience == Experience.Planets && P.Composition != null && !P.Busy && Tree.Count > 0, timeout);
        await Frames(3);
    }

    private int Child(int parent, string name) =>
        Tree[parent].Children.FirstOrDefault(c => string.Equals(Tree[c].Name, name, StringComparison.OrdinalIgnoreCase), -1);

    // ------------------------------------------------------------------ the first minutes on the Starter Sky

    /// <summary>The first minutes; <paramref name="reel"/> stops after the flight home (a short recording: --write-movie).</summary>
    private async Task Journey(bool reel = false)
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        _recording = true;
        var root = App.Navigator.Current;
        var comp = P.Composition!;
        Check(P.EstablishingShots == 1, "the first world is introduced by an establishing shot (high over it, then down behind the traveller)");
        await Seconds(0.9);
        await Shot("opening-establishing-shot");
        await Until(() => P.CameraSettled, 5);
        Log(F($"arrived: {Tree.FullPath(root)} theme={comp.Theme} radius={comp.Shape.Radius:0.0} m items={comp.Items.Count} launchers={comp.Launchers.Count} sky={comp.SkyBodies.Count}"));
        Check(P.TitleText == Tree.DisplayName(root), $"the title card introduces the world: \"{P.TitleText}\"");
        await Seconds(0.6);
        var inView = P.WorldsInView();
        Check(inView.Count >= 1, $"a destination is in view from the arrival (E1): {string.Join(", ", inView.Select(i => Tree[i].Name))}");
        Check(App.Signs.ShownText.Values.Any(t => inView.Any(i => t.Contains(Tree[i].Name.Length > 10 ? Tree[i].Name[..8] : Tree[i].Name, StringComparison.OrdinalIgnoreCase))), $"and named in the sky: {string.Join(" | ", App.Signs.ShownText.Values.Take(6))}");
        await Shot("arrival-starter-sky");

        // Walking: camera-relative, round the curve; feet stay on the ground; the camera's roll stays capped.
        var start = P.Walker.Feet;
        await Hold(1.6, Godot.Key.W);
        await Hold(0.9, Godot.Key.W, Godot.Key.D);
        await Seconds(0.3);
        var walked = Vector3Distance(start, P.Walker.Feet);
        var r = P.Walker.Feet.Length() - comp.Shape.GroundRadius(P.Walker.Up) - P.Walker.Height.Value;
        Check(walked > 6f && MathF.Abs(r) < 0.05f && P.Walker.Grounded, F($"walking on the planet: {walked:0.0} m, feet on the surface ({r:+0.000;-0.000} m), grounded"));
        Check(P.MaxCameraRollRateDegreesPerSecond <= PlanetWorld.CameraRollCapDegreesPerSecond + 0.5f, F($"the camera's roll stays within the cap (E4): max {P.MaxCameraRollRateDegreesPerSecond:0.0}°/s ≤ {PlanetWorld.CameraRollCapDegreesPerSecond:0}°/s"));

        // Jump, then a second press in the air: one deliberate boost (twirl), higher than a single jump.
        var air0 = P.Walker.AirJumps;
        var apex = 0f;
        Key1(Godot.Key.Space, true);
        await Seconds(0.3);
        Key1(Godot.Key.Space, false);
        await Frames(2);
        Key1(Godot.Key.Space, true);
        for (var i = 0; i < 90; i++) { await Frames(1); apex = MathF.Max(apex, P.Walker.Height.Value); }
        Key1(Godot.Key.Space, false);
        await Until(() => P.Walker.Grounded, 3);
        Check(P.Walker.AirJumps == air0 + 1 && apex > 2.2f, F($"jump and twirl: one air boost, apex {apex:0.00} m (a single jump peaks ≈ 1.2 m)"));
        await Shot("after-twirl");

        // The overlook: its payoff is that every destination is named. [direct] stand on it (its steps are tested in Core).
        if (comp.Overlook is { } ov)
        {
            P.ProbeStandOn(ov.Direction, ov.Top);
            await Seconds(1.6);
            Check(P.OnOverlook, "standing on the overlook");
            var named = comp.SkyBodies.Count(k => !k.InBelt && App.Signs.ShownIds.Contains(k.TargetId));
            Check(named >= Math.Min(3, comp.SkyBodies.Count(k => !k.InBelt)), $"from the overlook the worlds are named ({named} of {comp.SkyBodies.Count(k => !k.InBelt)}): {string.Join(" | ", App.Signs.ShownText.Values.Take(8))}");
            await Shot("overlook");
        }

        // Travel: stand on the picture meadow's launcher (the Destinations route walks you there) and press Space.
        var garden = Child(root, "Garden Photos");
        Check(garden >= 0, "the Starter Sky has Garden Photos");
        P.Approach(garden); // [direct] as a click in Destinations: a hop to its launcher
        await Until(() => !P.Hopping, 4);
        await Frames(4);
        Check(P.FocusNodeId == garden && P.PromptText.Contains("fly to Garden Photos", StringComparison.Ordinal), $"on the launcher, the prompt says what Space does: \"{P.PromptText}\"");
        await Shot("on-the-launcher");
        await Fly(Godot.Key.Space, garden, "Garden Photos");
        Check(P.Composition!.Theme == PlanetTheme.Meadow, $"Garden Photos is a picture meadow ({P.Composition.Theme})");
        await Shot("landed-garden-photos");

        // Find a picture and look closer: its preview; then open it (recorded: nothing starts).
        var pic = P.Composition.Items.Where(i => i.Category == FileCategory.Image && i.NodeId >= 0).OrderBy(i => PlanetShape.Angle(i.Direction, P.Walker.Up)).FirstOrDefault();
        Check(pic != null, "pictures stand on the meadow");
        if (pic != null)
        {
            P.Approach(pic.NodeId); // [direct] as a click in the list: hop beside it and look closer
            await Until(() => P.PreviewOpen, 5);
            await Until(() => P.PreviewHasImage, 6);
            Check(App.Selection.Selected == pic.NodeId && P.PreviewHasImage, $"looking closer at {Tree[pic.NodeId].Name}: selected, its picture shown");
            await Until(() => P.ThumbnailsShown > 0, 6);
            Check(P.ThumbnailsShown > 0, $"the easels show the real pictures ({P.ThumbnailsShown})");
            await Shot("inspect-picture");
            await Key(Godot.Key.Enter);
            await Frames(5);
            Check(App.LastShellMessage?.Contains(Tree.FullPath(pic.NodeId), StringComparison.Ordinal) == true, $"Enter opens it with Windows: {App.LastShellMessage}");
        }

        // Switch worlds: the same folder, selection and history in Places, and back.
        var sel = App.Selection.Selected;
        var history = App.Navigator.History.Count;
        await Key(Godot.Key.V);
        await Until(() => App.Experience == Experience.Places && App.Place != null && !App.InTransition, 8);
        await Seconds(0.6);
        Check(App.Experience == Experience.Places && App.Navigator.Current == garden && App.Selection.Selected == sel && App.Navigator.History.Count == history, $"V: Places shows the same folder ({Tree[App.Navigator.Current].Name}), selection ({(sel >= 0 ? Tree[sel].Name : "-")}) and history ({App.Navigator.History.Count})");
        await Shot("switched-to-places");
        await Key(Godot.Key.V);
        await WaitSettled();
        await Seconds(0.5);
        var near = pic != null ? PlanetShape.Angle(P.Walker.Up, pic.Direction) * P.Composition.Shape.Radius : 0;
        Check(App.Experience == Experience.Planets && App.Navigator.Current == garden && App.Selection.Selected == sel && near < 4f, F($"V again: back on the planet beside the selection ({near:0.0} m away)"));
        await Shot("switched-back");

        // Return: Backspace flies back up; you land beside the world you left.
        await Fly(Godot.Key.Backspace, root, Tree.DisplayName(root));
        var gl = P.Composition.Launchers.FirstOrDefault(l => l.TargetId == garden);
        var besides = gl != null ? PlanetShape.Angle(P.Walker.Up, gl.Direction) * P.Composition.Shape.Radius : 99;
        Check(besides < 5f, F($"back home beside the Garden Photos launcher ({besides:0.0} m)"));
        Check(P.RememberedRouteCount >= 1 && P.RouteBeadCount > 0, $"the route you flew is remembered: a trail of light marks the Garden Photos launcher's arc ({P.RouteBeadCount} beads)");
        if (reel)
        {
            await Hold(2.0, Godot.Key.W);
            await Seconds(1.0);
            return;
        }

        await Shot("back-home");

        // The direct route to a world that is not in the sky: Go to path (a warp).
        var archive = Child(root, "Star Archive");
        var deep = archive >= 0 ? Tree.FullPath(archive) + @"\2025" : "";
        if (archive >= 0)
        {
            await GoToPath(deep);
            await Until(() => Tree[App.Navigator.Current].Name == "2025" && !P.Busy && P.Composition?.FolderId == App.Navigator.Current, 15);
            Check(Tree[App.Navigator.Current].Name == "2025" && P.LastWasWarp, $"Go to path: a warp to {deep}");
            await Shot("warp-arrival");

            // A folder too large for one object per file: piles, and the list for direct access.
            await GoToPath(Tree.FullPath(archive));
            await Until(() => App.Navigator.Current == archive && !P.Busy && P.Composition?.FolderId == archive, 15);
            await Until(() => Tree[archive].ScanState == DirScanState.Complete, 10);
            await Seconds(1.0);
            var c = P.Composition!;
            var files = Tree[archive].Children.Count(id => Tree[id].Kind == NodeKind.File && App.IsVisibleInPlaceForProbe(Tree[id]));
            var shown = c.Items.Count(i => i.Kind == PlanetItemKind.File);
            var piled = c.Items.Where(i => i.Kind == PlanetItemKind.Pile).Sum(i => i.HiddenCount);
            Check(files >= 1000 && shown <= 64 && shown + piled == files, $"Star Archive: {files:N0} files, {shown} stand as objects and {piled:N0} are in {c.Items.Count(i => i.Kind == PlanetItemKind.Pile)} piles");
            await Shot("archive-piles");
            await Key(Godot.Key.J);
            await Frames(4);
            Check(P.ListOpen && P.ListCount >= files, $"J lists every world and file here ({P.ListCount:N0})");
            await TypeText("readings-0042");
            await Frames(4);
            Check(P.ListCount >= 1, $"typing filters the list ({P.ListCount} for \"readings-0042\")");
            await Shot("archive-list");
            await Key(Godot.Key.Escape);
        }

        // The empty moon, the workshop and the music box (through the list, the direct route).
        await VisitByPath(root, "Quiet Moon", w =>
        {
            Check(P.Composition!.Theme == PlanetTheme.Quiet && P.Composition.Items.Count == 0, $"Quiet Moon is an empty moon ({P.Composition.Theme}, {P.Composition.Items.Count} objects); its title: {P.TitleDetail}");
        });
        await VisitByPath(root, "Lantern Workshop", w =>
        {
            Check(P.Composition!.Theme == PlanetTheme.Workshop, $"Lantern Workshop is a workshop world ({P.Composition.Theme})");
        });
        var readme = P.Composition!.Items.FirstOrDefault(i => i.NodeId >= 0 && Tree[i.NodeId].Name.Equals("README.md", StringComparison.OrdinalIgnoreCase));
        if (readme != null)
        {
            P.Approach(readme.NodeId);
            await Until(() => P.PreviewHasText, 5);
            Check(P.PreviewHasText, "the workshop's README reads as text in the preview");
            await Shot("workshop-readme");
            await Key(Godot.Key.Escape);
        }

        await VisitByPath(root, "Music Box", w =>
        {
            Check(P.Composition!.Theme == PlanetTheme.Chime, $"Music Box is a world of bell flowers ({P.Composition.Theme})");
        });
        var song = P.Composition!.Items.FirstOrDefault(i => i.Category == FileCategory.Audio && i.NodeId >= 0 && Tree[i.NodeId].Name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));
        if (song != null)
        {
            var chimes = P.Audio.ChimeCount;
            P.Approach(song.NodeId);
            await Until(() => P.PreviewOpen, 4);
            await Seconds(0.8);
            Check(P.PreviewPlaying, $"looking closer at {Tree[song.NodeId].Name} plays it");
            await Shot("music-box");
            await Key(Godot.Key.Escape);
            await Hold(1.2, Godot.Key.W);
            Log($"bell flowers rung while walking: {P.Audio.ChimeCount - chimes}");
        }

        Log(F($"launch log: {string.Join(", ", P.LaunchLog.TakeLast(12).Select(l => $"{l.Phase}@{l.AtSeconds:0.00}"))}"));
    }

    /// <summary>Presses the key that starts a launch and follows it to the landing; checks the phases and the landing.</summary>
    private async Task Fly(Key key, int expected, string name)
    {
        var before = P.Transfers;
        _maxLandingScreenJump = 0;
        _maxFlightScreenJump = 0;
        await Key(key);
        await Until(() => P.Busy, 2);
        await Until(() => !P.Busy, 8);
        await Frames(3);
        Check(App.Navigator.Current == expected && P.Composition?.FolderId == expected && P.Transfers == before + 1, $"{key}: flew to {name}");
        Check(P.LastWindUpSeconds is > 0.25 and < 0.55, F($"a visible wind-up first: {P.LastWindUpSeconds:0.00} s (0.4 s, aligned to the beat ±0.09 s)"));
        Check(P.LastFlightSeconds is >= 1.2 and <= 2.7, F($"a brisk crossing: {P.LastFlightSeconds:0.00} s"));
        Check(_maxLandingScreenJump < 0.04f, F($"the landing does not cut: the traveller moves {_maxLandingScreenJump * 100:0.0} % of the screen height in the landing frame (flight frames ≤ {_maxFlightScreenJump * 100:0.0} %, the largest at {_maxFlightJumpAt:0.00} of the way)"));
        Check(P.Walker.Grounded, "landed on foot");
    }

    private async Task GoToPath(string path)
    {
        await KeyCombo(Godot.Key.L, ctrl: true);
        await Frames(3);
        await TypeText(path);
        await Key(Godot.Key.Enter);
        await Until(() => P.Busy, 6);
        await Until(() => !P.Busy, 10);
    }

    private async Task VisitByPath(int root, string name, Action<int> check)
    {
        var id = Child(root, name);
        if (id < 0)
        {
            Check(false, $"{name} exists in the sample");
            return;
        }

        await GoToPath(Tree.FullPath(id));
        await Until(() => App.Navigator.Current == id && !P.Busy && P.Composition?.FolderId == id, 15);
        await Until(() => Tree[id].ScanState == DirScanState.Complete, 8);
        await Seconds(1.0);
        check(id);
        await Shot(name.ToLowerInvariant().Replace(' ', '-'));
    }

    // ------------------------------------------------------------------ a bounded real fixture (Windows, links, WSL)

    private async Task FixtureTour()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        _recording = true;
        var root = App.Navigator.Current;
        var c = P.Composition!;
        Log(F($"fixture root {Tree.FullPath(root)}: theme {c.Theme}, {c.Items.Count} objects, {c.Launchers.Count} launchers ({c.Launchers.Count(l => l.IsLink)} links), {c.SkyBodies.Count} in the sky"));
        await Shot("fixture-arrival");
        var photos = Child(root, "Photos");
        if (photos >= 0 && c.Launchers.Any(l => l.TargetId == photos))
        {
            P.Approach(photos);
            await Until(() => !P.Hopping, 4);
            await Frames(3);
            await Fly(Godot.Key.Space, photos, "Photos");
            await Shot("fixture-photos");
            await Fly(Godot.Key.Backspace, root, Tree.DisplayName(root));
        }

        var link = c.Launchers.FirstOrDefault(l => l.IsLink && l.Followable);
        if (link != null)
        {
            Log($"link launcher: {Tree[link.TargetId].Name} → {PlaceText.LinkShort(Tree, Tree[link.TargetId])}");
            P.Approach(link.TargetId);
            await Until(() => !P.Hopping, 4);
            await Frames(3);
            Check(P.PromptText.Contains("follow the link", StringComparison.Ordinal), $"a link's launcher says it follows a link: \"{P.PromptText}\"");
            await Shot("fixture-link-launcher");
            await Key(Godot.Key.Space);
            await Until(() => P.Busy, 2);
            await Until(() => !P.Busy, 12);
            Check(App.Navigator.CurrentVia == link.TargetId, $"through the link to {Tree.FullPath(App.Navigator.Current)}");
            await Shot("fixture-through-link");
            await Key(Godot.Key.Backspace);
            await Until(() => P.Busy, 2);
            await Until(() => !P.Busy, 12);
        }

        var empty = Child(root, "empty-room");
        if (empty >= 0)
        {
            await GoToPath(Tree.FullPath(empty));
            await Until(() => App.Navigator.Current == empty && !P.Busy && P.Composition?.FolderId == empty, 12);
            await Until(() => Tree[empty].ScanState == DirScanState.Complete, 6);
            await Seconds(0.8);
            Check(P.Composition!.Theme == PlanetTheme.Quiet, $"an empty folder is a quiet moon ({P.Composition.Theme}): \"{P.TitleDetail}\"");
            await Shot("fixture-empty-room");
        }

        var wsl = System.Environment.GetCommandLineArgs().Concat(OS.GetCmdlineUserArgs()).FirstOrDefault(a => a.StartsWith("--wsl-root=", StringComparison.Ordinal))?["--wsl-root=".Length..];
        if (!string.IsNullOrEmpty(wsl))
        {
            await GoToPath(wsl);
            await Until(() => !P.Busy && P.Composition != null && Tree.FullPath(App.Navigator.Current).TrimEnd('\\').EndsWith(wsl.TrimEnd('\\').Split('\\').Last(), StringComparison.Ordinal), 20);
            await Until(() => Tree[App.Navigator.Current].ScanState == DirScanState.Complete, 10);
            await Seconds(1.2);
            var w = P.Composition!;
            var names = Tree[App.Navigator.Current].Children.Select(id => Tree[id].Name).ToList();
            Check(names.Contains("Notes") && names.Contains("notes"), $"a WSL folder as a planet: {Tree.FullPath(App.Navigator.Current)} ({w.Theme}, {w.Launchers.Count} launchers, {w.Items.Count} objects; Notes and notes are separate worlds)");
            Check(w.Launchers.Count(l => l.IsLink) + w.Items.Count(i => i.Kind == PlanetItemKind.Link) > 0, "its symbolic links are marked as links");
            await Shot("fixture-wsl");
        }
    }

    // ------------------------------------------------------------------ responsiveness

    private void ReportPerf()
    {
        foreach (var phase in new[] { "Walking", "Flying" })
        {
            var ms = _frames.Where(f => f.Phase == phase).Select(f => f.Ms).OrderBy(x => x).ToList();
            if (ms.Count < 10) continue;
            var mean = ms.Average();
            var p95 = ms[(int)(ms.Count * 0.95)];
            var p99 = ms[(int)(ms.Count * 0.99)];
            var line = F($"perf {phase.ToLowerInvariant()}: {ms.Count} frames, mean {mean:0.0} ms, p95 {p95:0.0} ms, p99 {p99:0.0} ms, max {ms[^1]:0.0} ms");
            if (ProbeTier.Functional) Log("SKIP " + line + " (functional tier: not a measurement)");
            else Check(p95 < 25, line);
        }
    }

    private static float Vector3Distance(NVector3 a, NVector3 b) => NVector3.Distance(a, b);
}
