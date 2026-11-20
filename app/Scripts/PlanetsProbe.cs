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
/// <para>
/// Other modes: planets-discover, planets-flows (the three flows from DJ's 2026-11-11 playtest), planets-route and
/// planets-route-hub (PLANET-27..30, world characters), planets-landmarks (PLANET-45, the root landmarks' terminal,
/// path and places panel; <c>PlanetsProbe.Landmarks.cs</c>), planets-lookouts (PLANET-55/56, PLANET-73..75: the telescope from its approach, scenery that answers, the
/// bell tower and the lighthouse; <c>PlanetsProbe.Lookouts.cs</c>), planets-reel (a short recording) and planets-camera-trace (a
/// frame-by-frame trace of the overview camera for comparing a refactor). Timing: waits use <see cref="ProbeBase"/>'s
/// clock, which is simulated time while a movie is written, so the same thresholds hold for a recording; frame times
/// are judged only outside the functional tier.
/// </para>
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

    /// <summary>
    /// Sign ids for names that are not a pad's own: a destination's world in the sky is <c>-100_000 - id</c>
    /// (PlanetWorld.Overlay) and a key place over the workbench is <c>-200_000 - id</c> (PlanetWorld.Bench).
    /// </summary>
    private const int WorldSignOffset = -100_000, BenchSignOffset = -200_000;

    /// <summary>
    /// Records each frame's time and phase while <see cref="_recording"/>, and tracks the traveller's screen position
    /// (0.6 m above its origin, as a fraction of the screen height per frame) so <see cref="Fly"/> can judge the landing
    /// frame and the flight frames separately. Also logs the first part of each flight (u &lt; 0.12, at most 40 lines),
    /// where a jump would show if the camera were handed over badly.
    /// </summary>
    public override void _Process(double delta)
    {
        base._Process(delta); // simulated time for recordings
        TrackFrameGap(); // PlanetsProbe.Audio.cs
        TrackDiscoveryFrame(delta); // PlanetsProbe.Discovery.cs
        TrackTravelFrame(); // PlanetsProbe.Travel.cs
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
        else if (Mode == "planets-route") await Route();
        else if (Mode == "planets-route-hub") await HubRoute();
        else if (Mode == "planets-camera-trace") await CameraTraceRun();
        else if (Mode == "planets-tutorial") await TutorialRun();
        else if (Mode == "planets-tutorial-cues") await TutorialCuesRun(); // PlanetsProbe.TutorialCues.cs
        else if (Mode == "planets-hazard") await HazardTour(); // PlanetsProbe.Hazard.cs
        else if (Mode == "planets-actions") await ActionsTour(); // PlanetsProbe.Actions.cs
        else if (Mode == "planets-arrival") await ArrivalTour(); // PlanetsProbe.Actions.cs
        else if (Mode == "planets-reading") await ReadingTour(); // PlanetsProbe.Actions.cs
        else if (Mode == "planets-sound") await SoundRun(); // PlanetsProbe.Sound.cs (PLANET-47)
        else if (Mode == "planets-landmarks") await LandmarksRun(); // PlanetsProbe.Landmarks.cs (PLANET-45)
        else if (Mode == "planets-lookouts") await LookoutsRun(); // PlanetsProbe.Lookouts.cs (PLANET-55, PLANET-56)
        else if (Mode == "planets-overview-routes") await OverviewRoutes(); // PlanetsProbe.Overview.cs (FOLLOW-1)
        else if (Mode == "planets-audio-gaps") await AudioGaps(); // PlanetsProbe.Audio.cs (FOLLOW-2)
        else if (Mode == "planets-navigation") await NavigationRun(); // PlanetsProbe.Navigation.cs
        else if (Mode == "planets-navigation-denied") await NavigationDeniedRun();
        else if (Mode == "planets-destinations-keys") await DestinationsKeysRun(); // PlanetsProbe.Input.cs (PLANET-51)
        else if (Mode == "planets-dialog-keys") await DialogKeysRun(); // PlanetsProbe.Dialogs.cs (PLANET-69/70)
        else if (Mode == "planets-reader") await ReaderRun(); // PlanetsProbe.Reader.cs (PLANET-52/53)
        else if (Mode == "planets-near-far") await NearFarRun(); // PlanetsProbe.NearFar.cs (PLANET-58)
        else if (Mode == "planets-relief") await ReliefRun(); // PlanetsProbe.Relief.cs (PLANET-59)
        else if (Mode == "planets-sky") await SkyRun(); // PlanetsProbe.Sky.cs (PLANET-60)
        else if (Mode == "planets-overview-discovery") await OverviewDiscoveryRun(); // PlanetsProbe.Discovery.cs (PLANET-76..78)
        else if (Mode == "planets-look") await LookRun(); // PlanetsProbe.Look.cs (PLANET-67/68)
        else if (Mode == "planets-travel") await TravelRun(); // PlanetsProbe.Travel.cs (PLANET-81/82)
        else if (Mode == "planets-overview-enter") await OverviewEnterRun(); // PlanetsProbe.Travel.cs (PLANET-83)
        else await Journey(reel: Mode == "planets-reel");
        ReportPerf();
    }

    /// <summary>Waits until the planets world is shown and not busy (no launch, landing or recomposition in progress), then 3 frames.</summary>
    private async Task WaitSettled(double timeout = 20)
    {
        await Until(() => App.Experience == Experience.Planets && P.Composition != null && !P.Busy && Tree.Count > 0, timeout);
        await Frames(3);
    }

    /// <summary>The child of <paramref name="parent"/> with this name (case-insensitive), or −1.</summary>
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
        // 1.6 s forward then 0.9 s diagonally must cover more than 6 m, with the feet within 5 cm of the surface.
        await Hold(1.6, Godot.Key.W);
        await Hold(0.9, Godot.Key.W, Godot.Key.D);
        await Seconds(0.3);
        var walked = Vector3Distance(start, P.Walker.Feet);
        var r = P.Walker.Feet.Length() - comp.Shape.GroundRadius(P.Walker.Up) - P.Walker.Height.Value;
        Check(walked > 6f && MathF.Abs(r) < 0.05f && P.Walker.Grounded, F($"walking on the planet: {walked:0.0} m, feet on the surface ({r:+0.000;-0.000} m), grounded"));
        // A 0.5°/s allowance over the cap (its rationale is not recorded).
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
        // A single jump peaks at about 1.2 m, so an apex over 2.2 m can only come from the air boost.
        Check(P.Walker.AirJumps == air0 + 1 && apex > 2.2f, F($"jump and twirl: one air boost, apex {apex:0.00} m (a single jump peaks ≈ 1.2 m)"));
        await Shot("after-twirl");

        // The overlook: its payoff is that every destination is named. [direct] stand on it (its steps are tested in Core).
        if (comp.Overlook is { } ov)
        {
            P.ProbeStandOn(ov.Direction, ov.Top);
            await Seconds(1.6);
            Check(P.OnOverlook, "standing on the overlook");
            // A destination counts as named when its pad or its world is (one name per destination, 2026-11-11).
            var named = comp.SkyBodies.Count(k => !k.InBelt && (App.Signs.ShownIds.Contains(k.TargetId) || App.Signs.ShownIds.Contains(WorldSignOffset - k.TargetId)));
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
            // 64 is PlanetComposer.MaxItems (PLANET-16): beyond it, files are gathered into piles and none is lost.
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

    /// <summary>
    /// Presses the key that starts a launch and follows it to the landing; checks the phases and the landing. The phase
    /// windows bracket the design values with room for frame timing: the wind-up is PlanetWorld.WindUpSeconds (0.4 s)
    /// moved by at most ±0.09 s onto the music's grid, so 0.25…0.55 s; the crossing must be brisk, 1.2…2.7 s.
    /// </summary>
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
        Check(_maxLandingScreenJump < LandingScreenJumpLimit, F($"the landing does not cut: the traveller moves {_maxLandingScreenJump * 100:0.0} % of the screen height in the landing frame (flight frames ≤ {_maxFlightScreenJump * 100:0.0} %, the largest at {_maxFlightJumpAt:0.00} of the way)"));
        Check(P.Walker.Grounded, "landed on foot");
    }

    /// <summary>
    /// The largest move of the traveller on screen in the landing frame, as a fraction of the screen height: 4 %. A cut
    /// (the camera handed over to a new pose) would move it much further; the check's text also reports the largest
    /// flight-frame move for comparison.
    /// </summary>
    private const float LandingScreenJumpLimit = 0.04f;

    /// <summary>Go to path with the real controls: Ctrl+L, type the path, Enter; then waits for the travel to start and end.</summary>
    private async Task GoToPath(string path)
    {
        await KeyCombo(Godot.Key.L, ctrl: true);
        await Frames(3);
        await TypeText(path);
        await Key(Godot.Key.Enter);
        await Until(() => P.Busy, 6);
        await Until(() => !P.Busy, 10);
    }

    /// <summary>Goes to a named child of <paramref name="root"/> by path, waits for its listing, runs the check and photographs it.</summary>
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

    /// <summary>--automate=planets-fixture: the bounded real fixture (see the class summary).</summary>
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

    /// <summary>
    /// The 95th-percentile frame time for walking and for flying must stay under <see cref="FrameP95LimitMs"/>; a phase
    /// with fewer than 10 recorded frames is not judged. In the functional tier the line is logged as SKIP, not judged.
    /// </summary>
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
            else Check(p95 < FrameP95LimitMs, line);
        }
    }

    /// <summary>
    /// Walking and flying frame budget, milliseconds at the 95th percentile: 25 ms is 40 fps (rationale for this figure
    /// not recorded).
    /// </summary>
    private const double FrameP95LimitMs = 25;

    private static float Vector3Distance(NVector3 a, NVector3 b) => NVector3.Distance(a, b);

    // ------------------------------------------------------------------ 2026-11-11: the three flows from DJ's playtest

    /// <summary>The value of a <c>--name=value</c> user argument, or null.</summary>
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
        // Turn the camera towards it with the real control, in steps (right-drag; ≈ 0.25°/px). At most 30 steps of up
        // to 40°, each given 6 frames to settle; within 6° counts as facing it.
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
        // Walk onto it: hold W (camera-relative) in 0.25 s steps, correcting the heading when it drifts by more than 8°.
        // "Straight" is judged below as at most 1.6 × the pad's distance plus 3 m (the margin's rationale is not recorded).
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

    /// <summary>
    /// --automate=planets-route (Starter Sky, PLANET-27..30): the hub, into the repository (Lantern Workshop), onto its
    /// workbench by a real jump (reveal), a spin from it (README), E there (README again), into a child folder (inside the
    /// project), back, then down the Long Way Down through every depth band, and home. Each world's character is checked
    /// and photographed.
    /// </summary>
    private async Task Route()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5); // the establishing shot
        var root = App.Navigator.Current;
        Check(P.Character.Kind == WorldKind.Hub, $"the Starter Sky is the hub here: {P.Character.Describe()}");
        await Shot("route-hub");

        async Task Go(int id, string what)
        {
            App.Selection.Select(id); // [direct] the selection a click makes
            await Frames(2);
            await Fly(Godot.Key.G, id, what);
            await Seconds(1.2);
        }

        var workshop = Child(root, "Lantern Workshop");
        await Go(workshop, "Lantern Workshop");
        var c = P.Character;
        Check(c.Kind == WorldKind.Repository && P.OnBenchWorld, $"Lantern Workshop is a repository with a workbench: {c.Describe()}");
        Check(P.Composition!.Overlook.Top <= PlanetComposer.RepositoryBenchTop + 1e-3f, F($"the workbench is low: {P.Composition.Overlook.Top:0.00} m"));
        Check(P.BenchEntries.Count >= 5 && Tree[P.BenchEntries[0].NodeId].Name == "README.md", $"the bench reads the project: {string.Join(", ", P.BenchEntries.Select(e => $"{Tree[e.NodeId].Name} ({e.Role})"))}");
        await Shot("route-repository-arrival");

        // A real jump onto the bench: stand a step from its edge, face it, run and jump.
        var ov = P.Composition.Overlook;
        var r = P.Composition.Shape.Radius;
        // [direct] set-up: stand 1.4 m outside the bench's edge, facing its centre; then the jump itself is real input.
        var near = PlanetFrame.Move(ov.Direction, P.Walker.Up - ov.Direction, ov.Radius + 1.4f, r);
        var toward = ov.Direction - near * System.Numerics.Vector3.Dot(ov.Direction, near);
        P.Walker.Teleport(P.Composition.Surface, near, System.Numerics.Vector3.Normalize(toward));
        P.FaceCameraAlong(System.Numerics.Vector3.Normalize(toward));
        await Frames(10);
        Check(!P.BenchRevealed, "nothing is revealed before you get onto the bench");
        HoldKey(Godot.Key.W, true);
        await Frames(6);
        await Key(Godot.Key.Space);
        await Until(() => P.BenchRevealed, 3);
        HoldKey(Godot.Key.W, false);
        await Until(() => P.Walker.Grounded, 2);
        await Frames(10);
        Check(P.BenchRevealed && P.BenchPanelOpen, $"jumping onto the bench reveals the project's key places (panel open: {P.BenchPanelOpen})");
        await Seconds(1.2);
        var named = P.BenchEntries.Count(e => App.Signs.ShownIds.Contains(BenchSignOffset - e.NodeId));
        Check(named >= P.BenchEntries.Count - 1, $"the key places rise over the bench, named: {named} of {P.BenchEntries.Count} ({string.Join(" | ", App.Signs.ShownText.Values.Take(10))})");
        await Shot("route-bench-revealed");
        Check(!P.PreviewOpen, "landing reveals; it does not open anything");

        // The spin: jump from the bench and twirl (the second Space 0.2 s after the first tap ends, in time: also when
        // recording, as Seconds counts simulated time then).
        await Key(Godot.Key.Space);
        await Seconds(0.2);
        await Key(Godot.Key.Space);
        await Until(() => P.PreviewOpen, 2);
        await Until(() => P.Walker.Grounded, 3);
        var readme = P.BenchEntries[0].NodeId;
        Check(P.PreviewOpen && App.Selection.Selected == readme, $"a spin from the bench opens the README preview (selected {Tree[App.Selection.Selected >= 0 ? App.Selection.Selected : root].Name})");
        Check(App.ShellLog.Count == 0, $"nothing was opened with Windows ({App.ShellLog.Count} shell requests)");
        await Shot("route-bench-spin-readme");
        await Key(Godot.Key.Escape);
        await Frames(5);
        // The keyboard route: E at the bench.
        if (!P.StandingOnBench()) P.ProbeStandOn(ov.Direction, ov.Top);
        await Frames(10);
        await Key(Godot.Key.E);
        await Until(() => P.PreviewOpen, 2);
        Check(P.PreviewOpen && App.Selection.Selected == readme, $"E at the bench does the same (prompt was \"{P.PromptText}\")");
        await Key(Godot.Key.Escape);
        await Frames(5);

        // Into a child folder of the project, then back.
        var src = Child(workshop, "src");
        await Go(src, "src");
        c = P.Character;
        Check(c.Kind == WorldKind.Folder && c.InsideRepository && c.RepositoryId == workshop, $"src is a folder inside the project: {c.Describe()} (band {c.Band})");
        await Shot("route-inside-project");
        await Key(Godot.Key.Backspace);
        await Until(() => App.Navigator.Current == workshop && !P.Busy, 10);
        await Seconds(1);
        Check(P.Arrivals(workshop) >= 2, $"back at the workshop: arrival {P.Arrivals(workshop)} (the music develops on a return)");
        await Key(Godot.Key.Backspace);
        await Until(() => App.Navigator.Current == root && !P.Busy, 10);
        await Seconds(1);
        Check(P.Character.Kind == WorldKind.Hub, "home at the hub");

        // The Long Way Down: every depth band in turn.
        var at = Child(root, "Long Way Down");
        var bands = new List<int>();
        for (var depth = 1; depth <= 7 && at >= 0; depth++)
        {
            await Go(at, Tree[at].Name);
            bands.Add(P.Character.Band);
            if (depth is 1 or 2 or 4 or 7) await Shot($"route-band-{P.Character.Band}-depth-{depth}");
            at = Tree[at].Children.FirstOrDefault(ch => Tree[ch].IsDirectory, -1);
        }

        Check(bands.SequenceEqual([1, 2, 2, 3, 3, 4, 4]), $"the Long Way Down passes every band in order: {string.Join(" ", bands)}");
        await KeyCombo(Godot.Key.Home, ctrl: true);
        await Until(() => App.Navigator.Current == root && !P.Busy, 15);
        await Seconds(1);
        Check(P.Character.Kind == WorldKind.Hub, "Ctrl+Home: back to the hub");

        // The two roots' looks, photographed here with a probe-only override (no Windows volume is entered or listed).
        foreach (var kind in new[] { WorldKind.WindowsRoot, WorldKind.WslRoot })
        {
            P.ProbeCharacter = new WorldCharacter(kind, 0, 0, -1);
            P.ProbeRebuild();
            await Seconds(1.5);
            Check(P.Character.Kind == kind, $"[instrument: override] the {P.Character.Describe()} look on the Starter Sky");
            await Shot($"route-look-{kind.ToString().ToLowerInvariant()}-override");
        }

        P.ProbeCharacter = null;
        P.ProbeRebuild();
    }

    /// <summary>
    /// --automate=planets-route-hub --root=hub --repo=&lt;a repository path, e.g. the WSL fixture's project&gt; (PLANET-27..30):
    /// This computer (calm hub); a Windows drive's world seen from the hub (its pane gateway; never entered, never listed);
    /// the WSL distribution's root (its terminal monolith); the repository by Ctrl+L (its workbench, by a real jump); a child
    /// folder inside it; Backspace back through each; home.
    /// </summary>
    private async Task HubRoute()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5);
        var hub = App.Navigator.Current;
        Check(Tree[hub].IsHub && P.Character.Kind == WorldKind.Hub, $"arrives at This computer, the hub ({P.Character.Describe()})");
        await Shot("hub-this-computer");
        var drives = Tree[hub].Children.Where(ch => Tree[ch].Location == LocationKind.Drive).ToList();
        var distros = Tree[hub].Children.Where(ch => Tree[ch].Location == LocationKind.Wsl).ToList();
        Check(drives.All(d => P.CharacterOf(d).Kind == WorldKind.WindowsRoot) && distros.All(d => P.CharacterOf(d).Kind == WorldKind.WslRoot),
            $"the hub's worlds: {drives.Count} Windows volumes, {distros.Count} WSL distributions, each with its own character");
        if (drives.Count > 0)
        {
            // Look at a drive's world in the sky: its gateway shows on the world itself; nothing is listed or entered.
            var d = drives[0];
            App.Selection.Select(d); // [direct] a click on it
            await Key(Godot.Key.F); // face the selection
            await Seconds(1.5);
            await Shot("hub-windows-volume-in-the-sky");
        }

        var ubuntu = distros.FirstOrDefault(ch => Tree[ch].Name.StartsWith("Ubuntu", StringComparison.OrdinalIgnoreCase), distros.FirstOrDefault(-1));
        if (ubuntu < 0)
        {
            Check(false, "a WSL distribution to visit");
            return;
        }

        App.Selection.Select(ubuntu);
        await Frames(2);
        await Fly(Godot.Key.G, ubuntu, Tree[ubuntu].Name);
        await Seconds(1.5);
        Check(P.Character.Kind == WorldKind.WslRoot, $"{Tree[ubuntu].Name}: {P.Character.Describe()}");
        await Shot("wsl-root");

        var repo = Arg("repo");
        if (string.IsNullOrEmpty(repo))
        {
            Check(false, "a repository to visit (--repo=…, e.g. the WSL fixture's project): the route is incomplete without it");
            return;
        }

        await GoToPath(repo);
        await Until(() => !P.Busy && P.Composition != null && Tree[App.Navigator.Current].ScanState == DirScanState.Complete, 15);
        await Seconds(1.5);
        var repoId = App.Navigator.Current;
        var c = P.Character;
        Check(c.Kind == WorldKind.Repository && P.OnBenchWorld, $"{Tree[repoId].Name} is a repository ({c.Describe()}, depth {c.Depth} below {Tree[ubuntu].Name})");
        await Shot("repository");
        var ov = P.Composition!.Overlook;
        var r = P.Composition.Shape.Radius;
        // [direct] set-up: stand 1.4 m outside the bench's edge, facing its centre; then the jump itself is real input.
        var near = PlanetFrame.Move(ov.Direction, P.Walker.Up - ov.Direction, ov.Radius + 1.4f, r);
        var toward = ov.Direction - near * System.Numerics.Vector3.Dot(ov.Direction, near);
        P.Walker.Teleport(P.Composition.Surface, near, System.Numerics.Vector3.Normalize(toward));
        P.FaceCameraAlong(System.Numerics.Vector3.Normalize(toward));
        await Frames(10);
        HoldKey(Godot.Key.W, true);
        await Frames(6);
        await Key(Godot.Key.Space);
        await Until(() => P.BenchRevealed, 3);
        HoldKey(Godot.Key.W, false);
        await Until(() => P.Walker.Grounded, 2);
        await Seconds(1.2);
        Check(P.BenchRevealed, $"the workbench shows the project: {string.Join(", ", P.BenchEntries.Select(e => $"{Tree[e.NodeId].Name} ({e.Role})"))}");
        await Shot("repository-bench");
        var docs = Tree[repoId].Children.FirstOrDefault(ch => Tree[ch].IsDirectory && !Tree[ch].Name.StartsWith('.'), -1);
        if (docs >= 0)
        {
            App.Selection.Select(docs);
            await Frames(2);
            await Fly(Godot.Key.G, docs, Tree[docs].Name);
            await Seconds(1.2);
            Check(P.Character.InsideRepository, $"{Tree[docs].Name}: {P.Character.Describe()}");
            await Shot("inside-repository");
            await Key(Godot.Key.Backspace);
            await Until(() => App.Navigator.Current == repoId && !P.Busy, 10);
            await Seconds(1);
        }

        await Key(Godot.Key.Backspace);
        await Until(() => !P.Busy, 10);
        await Seconds(1);
        await KeyCombo(Godot.Key.Home, ctrl: true);
        await Until(() => App.Navigator.Current == hub && !P.Busy, 15);
        await Seconds(1);
        Check(P.Character.Kind == WorldKind.Hub, "Ctrl+Home: back at This computer");
    }

    /// <summary>
    /// --automate=planets-camera-trace (Starter Sky, with the engine's --fixed-fps 60): a frame-by-frame trace of the
    /// overview camera and the landing flash — open the overview, select Star Archive's 2025, land there — written to
    /// camera-trace.txt in --out, so a refactor of the camera or flash code can be compared with diff (2026-11-14).
    /// </summary>
    /// <remarks>Frames traced: 90 to open, 90 to select, 150 to land (1.5 s, 1.5 s and 2.5 s at 60 fps).</remarks>
    private async Task CameraTraceRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(4);
        var lines = new System.Text.StringBuilder();
        async Task Trace(string what, int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                await Frames(1);
                lines.Append(what).Append(' ').Append(i).Append(' ').Append(P.CameraTrace()).Append('\n');
            }
        }

        await Key(Godot.Key.T);
        await Trace("open", 90);
        var archive = Child(App.Navigator.Current, "Star Archive");
        var y2025 = archive >= 0 ? Child(archive, "2025") : -1;
        if (y2025 >= 0 && P.UniverseKnows(y2025)) P.SelectInUniverse(y2025);
        await Trace("select", 90);
        await Key(Godot.Key.Enter);
        await Trace("land", 150);
        System.IO.File.WriteAllText(System.IO.Path.Combine(Out, "camera-trace.txt"), lines.ToString());
        Check(lines.Length > 0, $"camera trace written ({lines.ToString().Split('\n').Length - 1} frames)");
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

    /// <summary>
    /// Labels during a full camera turn (120 frames of 12 px): every pad name stays pinned on its pad (within 1 px of
    /// the freshly projected anchor, unless clamped at a screen edge), no pad the planet hides is named, names never
    /// overlap, and at least 8 pads (or all, if fewer) are named at some point. Then a selected destination is named
    /// exactly once, on its pad or its world.
    /// </summary>
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
                // A pinned name may be raised straight up on a longer stem when another name holds its place (one slot,
                // SignLayer.PinnedSlots, 2026-11-17; or just over the names in its way, PLANET-58): anywhere from its own
                // slot up to the longest stem counts as on its pad; lower, or sideways, does not.
                var dy = rect.End.Y + 16 - a.Y;
                var gapY = dy > 0 ? dy : MathF.Max(0, a.Y - rect.End.Y - Ui.SignLayer.PinnedMaxStemPx);
                if (!clamped) maxOffset = MathF.Max(maxOffset, MathF.Max(MathF.Abs(rect.GetCenter().X - a.X), gapY));
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
            var onWorld = App.Signs.ShownAnchors.TryGetValue(WorldSignOffset - target, out var worldAt);
            var launcher = c.Launchers.First(l => l.TargetId == target);
            var (padAnchor, _) = P.PadAnchor(launcher);
            var (worldCentre, radiusPx) = P.WorldOnScreen(target);
            var padGap = onPad && !P.Camera.IsPositionBehind(padAnchor) ? P.Camera.UnprojectPosition(padAnchor).DistanceTo(padAt) : 999f;
            // A world's name sits just outside its disc (1.15 × its screen radius); 999 px stands for "not shown there".
            var worldGap = onWorld ? worldAt.DistanceTo(worldCentre) - radiusPx * 1.15f : 999f;
            Check(onPad != onWorld && (padGap < 1f || worldGap <= 1f), F($"the selected destination ({Tree[target].Name}) is named once, pinned {(onPad ? $"on its pad ({padGap:0.00} px)" : $"on its world ({worldGap:0.0} px outside its disc)")}"));
            await Shot("labels-selected-wins");
            App.Selection.Select(-1);
        }
    }

    /// <summary>
    /// The overview (T): it opens with more than 30 known folders as worlds, orbits and zooms with the real controls,
    /// selects a world elsewhere in the tree by clicking it, lands there with Enter (one history entry), returns with
    /// Backspace, and leaves with Escape exactly where you stood (within 5 cm).
    /// </summary>
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
