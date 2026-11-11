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
        base._Process(delta); // simulated time for recordings
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
        else if (Mode == "planets-discover") await DiscoverOnly();
        else if (Mode == "planets-flows") await Flows();
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
            // A destination counts as named when its pad or its world is (one name per destination, 2026-11-11).
            var named = comp.SkyBodies.Count(k => !k.InBelt && (App.Signs.ShownIds.Contains(k.TargetId) || App.Signs.ShownIds.Contains(-100_000 - k.TargetId)));
            Check(named >= Math.Min(3, comp.SkyBodies.Count(k => !k.InBelt)), $"from the overlook the destinations are named ({named} of {comp.SkyBodies.Count(k => !k.InBelt)}): {string.Join(" | ", App.Signs.ShownText.Values.Take(8))}");
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

    // ------------------------------------------------------------------ 2026-11-11: the three flows from DJ's playtest

    private static string? Arg(string name) =>
        OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith($"--{name}=", StringComparison.Ordinal))?[(name.Length + 3)..];

    /// <summary>--automate=planets-discover --find=NAME [--root=…]: find a named launcher from the arrival (no launch).</summary>
    private async Task DiscoverOnly()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Until(() => P.CameraSettled, 5);
        // Let the listing of this folder finish (the ring is composed from it).
        await Until(() => Tree[App.Navigator.Current].ScanState == DirScanState.Complete, 15);
        await Seconds(1.5);
        _recording = true;
        await Shot("discover-arrival");
        await FindLauncher(Arg("find") ?? "home", launch: false);
    }

    /// <summary>
    /// Launcher discovery: from the arrival, turn the camera with the real mouse (right-drag) towards the named pad,
    /// then walk onto it (camera-relative W). Checks: the pad is named on screen before you walk, the walk is a short
    /// straight one (no circling the planet), and on the pad the prompt says Space flies there.
    /// </summary>
    private async Task<bool> FindLauncher(string name, bool launch)
    {
        var c = P.Composition!;
        var here = App.Navigator.Current;
        var pad = c.Launchers.FirstOrDefault(l => !l.IsParent && Tree.Contains(l.TargetId) && string.Equals(Tree[l.TargetId].Name, name, StringComparison.OrdinalIgnoreCase));
        var ring = pad == null ? -1f : c.Surface.SurfaceDistance(pad.Direction, System.Numerics.Vector3.UnitY);
        Log(F($"{Tree.FullPath(here)}: {c.Launchers.Count(l => !l.IsParent)} launchers on {c.Launchers.Select(l => MathF.Round(c.Surface.SurfaceDistance(l.Direction, System.Numerics.Vector3.UnitY))).Distinct().Count()} ring(s), {c.SkyBodies.Count(b => b.InBelt)} in the belt; looking for {name}"));
        if (!Check(pad != null, $"{name} has a launcher on {Tree.DisplayName(here)} ({ring:0.0} m from the arrival)")) return false;
        var namedAtArrival = App.Signs.ShownIds.Contains(pad!.TargetId);
        // Turn the camera towards it with the real control, in steps (right-drag; ≈ 0.25°/px).
        var turned = 0f;
        for (var i = 0; i < 30 && MathF.Abs(P.CameraTurnTo(pad.Direction)) > 6f; i++)
        {
            var a = P.CameraTurnTo(pad.Direction);
            var step = Math.Clamp(a, -40f, 40f);
            turned += MathF.Abs(step);
            await DragCamera(new Vector2(step / 0.25f, 0)); // dragging right turns the camera right
            await Frames(6);
        }

        await Frames(10);
        var named = App.Signs.ShownIds.Contains(pad.TargetId);
        Check(named, F($"turning the camera {turned:0}° in place shows its name, pinned on its pad (named already at the arrival: {namedAtArrival})"));
        await Shot($"discover-{name.Replace(' ', '-').ToLowerInvariant()}-in-view");
        // Walk onto it: hold W (camera-relative), correcting the heading with the mouse now and then.
        var start = P.Walker.Feet;
        var walked = 0f;
        var last = start;
        for (var i = 0; i < 40 && P.FocusNodeId != pad.TargetId; i++)
        {
            var a = P.CameraTurnTo(pad.Direction);
            if (MathF.Abs(a) > 8f) await DragCamera(new Vector2(Math.Clamp(a, -30f, 30f) / 0.25f, 0));
            await Hold(0.25, Godot.Key.W);
            walked += System.Numerics.Vector3.Distance(last, P.Walker.Feet);
            last = P.Walker.Feet;
        }

        await Seconds(0.4);
        var on = P.FocusNodeId == pad.TargetId;
        Check(on && walked < ring * 1.6f + 3f, F($"walked straight onto it: {walked:0.0} m for a pad {ring:0.0} m away (no circling)"));
        Check(P.PromptText.Contains(Tree[pad.TargetId].Name, StringComparison.Ordinal) && P.PromptText.StartsWith("Space", StringComparison.Ordinal), $"the prompt says what Space does: \"{P.PromptText}\"");
        var hereName = Tree.DisplayName(App.Navigator.Current);
        Check(App.Hud.BreadcrumbNames.LastOrDefault() == hereName, $"the breadcrumb says where you are: {string.Join(" › ", App.Hud.BreadcrumbNames)} (expected …{hereName})");
        await Shot($"discover-on-{name.Replace(' ', '-').ToLowerInvariant()}");
        if (launch) await Fly(Godot.Key.Space, pad.TargetId, Tree.DisplayName(pad.TargetId));
        return on;
    }

    /// <summary>A real right-drag that turns the camera (the probe's own mouse, in window pixels).</summary>
    private async Task DragCamera(Vector2 pixels, int steps = 8)
    {
        var centre = ToWindow(App.GetViewport().GetVisibleRect().Size / 2);
        Mouse(MouseButton.Right, true, centre);
        await Frames(2);
        for (var i = 0; i < steps; i++) { Motion(pixels / steps); await Frames(1); }
        Mouse(MouseButton.Right, false, centre);
        await Frames(2);
    }

    /// <summary>--automate=planets-flows (Starter Sky): discovery on a crowded world, labels while the camera turns, the overview.</summary>
    private async Task Flows()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Until(() => P.CameraSettled, 5);
        _recording = true;
        var root = App.Navigator.Current;

        // 1. A crowded world: Constellations (36 folders: three rings of launchers and a belt of moons).
        var crowded = Child(root, "Constellations");
        Check(crowded >= 0, "the Starter Sky has Constellations");
        await FindLauncher("Constellations", launch: true);
        await Until(() => Tree[crowded].ScanState == DirScanState.Complete && !P.Busy, 10);
        await Seconds(1.2);
        await Shot("crowded-arrival");
        await FindLauncher("Snail on a Leaf", launch: false);

        // 2. Labels while the camera turns: every frame of a full turn, each name sits on its pad or world, nothing the
        // planet hides is named, and names never overlap.
        await Labels();

        // 3. The overview: zoom out, select a world elsewhere in the tree, land there; Backspace back; out and back again.
        await Overview(root);
    }

    private async Task Labels()
    {
        var c = P.Composition!;
        var frames = 0;
        var maxOffset = 0f;
        var hiddenNamed = 0;
        var overlapping = 0;
        var padNamesSeen = new HashSet<int>();
        var centre = ToWindow(App.GetViewport().GetVisibleRect().Size / 2);
        Mouse(MouseButton.Right, true, centre);
        await Frames(2);
        for (var i = 0; i < 120; i++)
        {
            Motion(new Vector2(12, i < 60 ? 0.6f : -0.6f)); // a full turn (≈ 360°) with a little tilt, over ≈ 2 s
            await Frames(1);
            frames++;
            foreach (var l in c.Launchers)
            {
                if (!App.Signs.ShownRectById.TryGetValue(l.TargetId, out var rect)) continue;
                padNamesSeen.Add(l.TargetId);
                var (anchor, hidden) = P.PadAnchor(l);
                if (hidden) hiddenNamed++;
                if (P.Camera.IsPositionBehind(anchor)) continue;
                // Measured independently of the layout: the sign's rectangle against the pad's freshly projected anchor
                // (centred above it, its bottom 16 px over it), except where it was clamped at a screen edge.
                var a = P.Camera.UnprojectPosition(anchor);
                var vp = App.GetViewport().GetVisibleRect();
                var clamped = rect.Position.X <= vp.Position.X + 3 || rect.End.X >= vp.End.X - 3 || rect.Position.Y <= vp.Position.Y + 3;
                if (!clamped) maxOffset = MathF.Max(maxOffset, MathF.Max(MathF.Abs(rect.GetCenter().X - a.X), MathF.Abs(rect.End.Y + 16 - a.Y)));
            }

            var rects = App.Signs.ShownRects;
            for (var a = 0; a < rects.Count; a++)
            {
                for (var b = a + 1; b < rects.Count; b++)
                {
                    if (rects[a].Intersects(rects[b])) overlapping++;
                }
            }

            if (i == 30 || i == 90) await Shot($"labels-turning-{i}");
        }

        Mouse(MouseButton.Right, false, centre);
        await Frames(3);
        Check(maxOffset < 1.0f, F($"over {frames} frames of a full camera turn every pad name stayed on its pad (largest gap {maxOffset:0.00} px)"));
        Check(hiddenNamed == 0, $"no pad hidden by the planet was named ({hiddenNamed} frames named one)");
        Check(overlapping == 0, $"names never overlapped ({overlapping})");
        Check(padNamesSeen.Count >= Math.Min(8, c.Launchers.Count), $"{padNamesSeen.Count} of {c.Launchers.Count} pads were named at some point of the turn");
        // Selected wins: select a world in view and check its name is shown even among others.
        var inView = P.WorldsInView();
        if (inView.Count > 0)
        {
            var target = inView[^1];
            App.Selection.Select(target); // [direct] as a click in the list
            await Frames(4);
            // One name per destination: on its pad when the pad is on screen, otherwise on its world.
            var onPad = App.Signs.ShownAnchors.TryGetValue(target, out var padAt);
            var onWorld = App.Signs.ShownAnchors.TryGetValue(-100_000 - target, out var worldAt);
            var launcher = c.Launchers.First(l => l.TargetId == target);
            var (padAnchor, _) = P.PadAnchor(launcher);
            var (worldCentre, radiusPx) = P.WorldOnScreen(target);
            var padGap = onPad && !P.Camera.IsPositionBehind(padAnchor) ? P.Camera.UnprojectPosition(padAnchor).DistanceTo(padAt) : 999f;
            var worldGap = onWorld ? worldAt.DistanceTo(worldCentre) - radiusPx * 1.15f : 999f;
            Check(onPad != onWorld && (padGap < 1f || worldGap <= 1f), F($"the selected destination ({Tree[target].Name}) is named once, pinned {(onPad ? $"on its pad ({padGap:0.00} px)" : $"on its world ({worldGap:0.0} px outside its disc)")}"));
            await Shot("labels-selected-wins");
            App.Selection.Select(-1);
        }
    }

    private async Task Overview(int root)
    {
        var before = App.Navigator.Current;
        var feet = P.Walker.Feet;
        var history = App.Navigator.History.Count;
        await Key(Godot.Key.T);
        await Seconds(1.6);
        Check(P.UniverseOpen && P.UniverseBodyCount > 30, $"T opens the overview: {P.UniverseBodyCount} known folders as worlds ({P.UniverseCounts.Listed} listed, {P.UniverseCounts.Partial} partly, {P.UniverseCounts.Unknown} not listed yet, {P.UniverseCounts.Visited} visited)");
        await Shot("overview-zoomed-out");
        // Orbit and zoom with the real controls.
        await DragCamera(new Vector2(180, 40), 12);
        Inject(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = ToWindow(App.GetViewport().GetVisibleRect().Size / 2) });
        await Seconds(0.8);
        await Shot("overview-orbited");
        // Select a world elsewhere in the tree (not a neighbour): Star Archive/2025, by clicking it.
        var archive = Child(root, "Star Archive");
        var target = archive >= 0 ? Child(archive, "2025") : -1;
        Check(target >= 0 && P.UniverseKnows(target), "Star Archive/2025 is in the overview");
        if (target < 0) return;
        P.SelectInUniverse(archive); // [direct] focus its system first so the small world is large enough to click
        await Seconds(1.4);
        if (P.UniverseScreen(target) is { } s) await Click(s / GetWindow().ContentScaleFactor);
        await Seconds(1.2);
        Check(P.UniverseSelected == target && App.Selection.Selected == target, $"clicking a world selects it (the shared selection: {(App.Selection.Selected >= 0 ? Tree[App.Selection.Selected].Name : "-")}) and zooms in on it");
        await Shot("overview-selected");
        await Key(Godot.Key.Enter);
        await Until(() => !P.UniverseOpen, 3);
        await Until(() => App.Navigator.Current == target && !P.Busy, 10);
        await Frames(4);
        Check(App.Navigator.Current == target && P.Composition?.FolderId == target && P.Walker.Grounded, $"Enter lands there: {Tree.FullPath(App.Navigator.Current)}");
        Check(App.Navigator.History.Count == history + 1, $"the landing is in the history ({history} → {App.Navigator.History.Count})");
        await Shot("overview-landed");
        await Key(Godot.Key.Backspace);
        await Until(() => P.Busy, 2);
        await Until(() => !P.Busy, 10);
        Check(App.Navigator.Current == before, $"Backspace returns to {Tree.DisplayName(before)}");
        // Out and back: the overview returns you to where you stood.
        var standing = P.Walker.Feet;
        await Key(Godot.Key.T);
        await Seconds(1.2);
        await Key(Godot.Key.Escape);
        await Until(() => !P.UniverseOpen, 2);
        await Until(() => P.CameraSettled, 3);
        await Frames(5);
        Check(!P.UniverseOpen && System.Numerics.Vector3.Distance(standing, P.Walker.Feet) < 0.05f && App.Navigator.Current == before, "Esc leaves the overview and you are where you stood");
        await Shot("overview-back-where-you-stood");
        _ = feet;
    }
}
