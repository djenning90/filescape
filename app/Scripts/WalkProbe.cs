using System.Text;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using Godot;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// The walking-world journey (--automate=walk…, WALK/JUMP/LINK/LOC/VISI). Input is injected through Godot's pipeline
/// (ProbeBase); "[direct]" steps only set up a case.
/// </summary>
public partial class WalkProbe : ProbeBase
{
    private readonly StringBuilder _perf = new("case,quality,frames,mean_ms,p50_ms,p95_ms,p99_ms,max_ms,gpu_mean_ms,instances,tree_nodes\n");

    public WalkProbe(Main main, string outDir, string mode) : base(main, outDir, mode)
    {
    }

    private FsTree Tree => App.Tree!;
    private PlaceComposition Place => App.Place!;

    protected override void Finish() => System.IO.File.WriteAllText(System.IO.Path.Combine(Out, $"{Mode}-perf.csv"), _perf.ToString());

    protected override async Task RunProbe()
    {
        await Until(() => App.Place != null && !App.InTransition, 20);
        await Seconds(1.0);
        if (Mode == "walk-look") await LookAround();
        else if (Mode == "walk-perf") await PerfIsolation();
        else if (Mode == "walk-tour") await Tour();
        else await Journey();
    }

    /// <summary>Quick visual check of the walking scale: arrival, plaza, overlook, flight.</summary>
    private async Task LookAround()
    {
        await Shot("arrival-on-foot");
        LogPose("arrival");
        await Hold(1.6, Godot.Key.Up);
        await Seconds(0.4);
        LogPose("after walking down the stairs");
        await Shot("plaza-at-eye-level");
        var ov = Place.Overlook;
        var foot = ov.RampFoot;
        var axis = NVector2.Normalize(new NVector2(ov.Top.X, ov.Top.Z) - foot);
        var start = foot - axis * 2;
        App.ProbePlaceUser(new PlacePose(new NVector3(start.X, 1.75f, start.Y), -PlaceTerrain.PlaceAngle(axis), -5)); // [direct]
        await Frames(3);
        await Hold(3.2, Godot.Key.W);
        await Seconds(0.5);
        LogPose("after walking up the ramp");
        App.ProbeSetGaze(-PlaceTerrain.PlaceAngle(-new NVector2(ov.Top.X, ov.Top.Z)) , -8); // [direct] face the plaza
        await Frames(3);
        await Shot("overlook-view");
        App.ProbeSetGaze(-PlaceTerrain.PlaceAngle(new NVector2(ov.Top.X, ov.Top.Z)) + 180 - 180, 0);
        await Key(Godot.Key.T);
        await Seconds(1.5);
        LogPose("flying");
        await Shot("flying");
        await Key(Godot.Key.T);
        await Seconds(2.0);
        LogPose("landed");
        await Shot("landed");
    }

    private Walker W => App.Walker;
    private float EyeAboveSupport => App.Rig.CameraPosition.Y - W.Support;
    private NVector2 FeetXz => new(W.Feet.X, W.Feet.Z);

    private int Find(int parent, string name) => Tree[parent].Children.FirstOrDefault(c => Tree[c].Name == name, -1);

    private async Task WaitPlace(double seconds = 15)
    {
        await Until(() => App.Place != null && !App.InTransition && !App.AwaitingArrival && App.PendingRequests == 0, seconds);
        await Frames(4);
    }

    private async Task WaitGuide()
    {
        await Until(() => !App.GuidedGlide && !App.InTransition, 8);
        await Frames(3);
    }

    /// <summary>Clicks a Destinations row (the HUD walks you up to that gate).</summary>
    private async Task<bool> ClickDestination(int id)
    {
        var ids = App.Hud.DestinationIds().ToList();
        var index = ids.IndexOf(id);
        if (index < 0) return false;
        App.Hud.EnsureDestinationVisible(index);
        await Frames(3);
        await Click(App.Hud.DestinationItemRect(index).GetCenter());
        await WaitGuide();
        return true;
    }

    private async Task<bool> WalkThrough(Godot.Key key, double maxSeconds)
    {
        var before = App.CompletedTransitions;
        var start = Time.GetTicksMsec();
        Key1(key, true);
        while (!App.InTransition && (Time.GetTicksMsec() - start) / 1000.0 < maxSeconds) await Frames(1);
        Key1(key, false);
        await WaitPlace();
        return App.CompletedTransitions > before;
    }

    /// <summary>Horizontal speed after the start-up ramp, from two timed samples (independent of key-event latency).</summary>
    private async Task<float> SteadySpeed(float yaw, float pitch, params Godot.Key[] keys)
    {
        // [direct] the same clear start in the south corridor each time: ~10 m of free plaza ahead (north).
        App.ProbePlaceUser(new PlacePose(new NVector3(0, 1.75f, 12f), yaw, pitch));
        await Frames(3);
        foreach (var k in keys) Key1(k, true);
        await Seconds(0.45);
        var p0 = FeetXz;
        var t0 = Time.GetTicksUsec();
        await Seconds(1.0); // a longer window keeps one frame of sampling jitter below 2 %
        var p1 = FeetXz;
        var t1 = Time.GetTicksUsec();
        foreach (var k in keys) Key1(k, false);
        await Seconds(0.3);
        return NVector2.Distance(p0, p1) / ((t1 - t0) / 1e6f);
    }

    private async Task Look(Vector2 total, int steps = 20)
    {
        var centre = App.GetViewport().GetVisibleRect().Size / 2;
        Mouse(MouseButton.Right, true, centre);
        await Frames(2);
        for (var i = 0; i < steps; i++) { Motion(total / steps); await Frames(1); }
        Mouse(MouseButton.Right, false, centre);
        await Frames(3);
    }

    /// <summary>VISI-01: overlay labels are on screen, never over each other or a HUD panel.</summary>
    private async Task LabelsClear(string where)
    {
        await Seconds(0.35); // labels refresh every 80 ms; hover follows the mouse
        var rects = App.LabelRects.ToList();
        var hud = App.Hud.BlockingRects.ToList();
        var vp = App.GetViewport().GetVisibleRect();
        var overlaps = 0;
        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++) if (rects[i].Intersects(rects[j])) overlaps++;
        }

        var onHud = rects.Count(r => hud.Any(h => h.Intersects(r)));
        var off = rects.Count(r => !vp.Encloses(r));
        Check(overlaps == 0 && onHud == 0 && off == 0, $"labels at {where}: {rects.Count} shown, {overlaps} overlapping, {onHud} on the HUD, {off} off screen");
    }

    private async Task Journey()
    {
        var tree = Tree;
        var root = App.Navigator.Root;
        var rootName = tree.DisplayName(root);
        Log($"arrival root {tree.FullPath(root)}");

        // ---- 1. Arrive on foot (WALK-01, PLACE-02)
        Check(App.Locomotion == Locomotion.Walking, "arrives on foot (walking is the default)");
        Check(MathF.Abs(EyeAboveSupport - 1.75f) < 0.03f, F($"eye is 1.75 m above the support (got {EyeAboveSupport:0.00} on support {W.Support:0.00})"));
        Check(App.Hud.BreadcrumbNames.LastOrDefault() == rootName && App.Hud.BreadcrumbNames.FirstOrDefault() == "This computer", $"breadcrumb runs from This computer to {rootName} ({string.Join(" › ", App.Hud.BreadcrumbNames)})");
        Check(App.PlaceWorld.LandmarkSign == rootName, "the landmark plaque names the place");
        await Shot("arrival-on-foot");
        await LabelsClear("arrival");

        // ---- 2. Look and steer with the right mouse (WALK-03)
        var yaw0 = W.Yaw;
        await Look(new Vector2(-300, 120));
        Check(MathF.Abs(W.Yaw - yaw0 - 36f) < 2f, F($"right-drag right/left turns the heading ({W.Yaw - yaw0:0.0}°)"));
        Check(W.LookPitch < -15f, F($"right-drag down lowers the gaze ({W.LookPitch:0.0}°)"));
        Check(!App.PlaceLookCaptured && Input.MouseMode == Input.MouseModeEnum.Visible, "releasing the right button releases the cursor");
        App.ProbeSetGaze(0, -6); // [direct] face north again

        // ---- 3. Walk down the stairs (WALK-05)
        var start = FeetXz;
        await Hold(1.4, Godot.Key.Up);
        await Seconds(0.3);
        Check(W.Grounded && W.Support < 0.01f && W.Jumps == 0, F($"Up arrow walks off the terrace down the stairs without jumping (support {W.Support:0.00})"));
        Check(NVector2.Distance(start, FeetXz) > 4f, F($"walked {NVector2.Distance(start, FeetXz):0.0} m"));
        await Shot("plaza-at-eye-level");

        // ---- 4. Looking sharply down does not push into the ground (WALK-02)
        App.ProbeSetGaze(W.Yaw, 0); // [direct]
        await Look(new Vector2(0, 700));
        Check(W.LookPitch < -70f, F($"looking sharply down ({W.LookPitch:0} deg)"));
        var downSpeed = await SteadySpeed(0, W.LookPitch, Godot.Key.W);
        var levelSpeed = await SteadySpeed(0, 0, Godot.Key.W);
        Check(MathF.Abs(downSpeed / levelSpeed - 1) < 0.03f && W.Support < 0.01f, F($"walking while looking down is as fast and stays on the ground ({downSpeed:0.00} m/s vs {levelSpeed:0.00} m/s level)"));
        await Shot("looking-down-while-walking");

        // ---- 5. Turning with the arrows while walking curves (WALK-03), diagonal not faster (WALK-04)
        yaw0 = W.Yaw;
        var a = FeetXz;
        await Hold(1.0, Godot.Key.Up, Godot.Key.Left);
        Check(W.Yaw - yaw0 > 90f && NVector2.Distance(a, FeetXz) > 2.5f, F($"Up + Left walks a curve (turned {W.Yaw - yaw0:0}°, moved {NVector2.Distance(a, FeetXz):0.0} m)"));
        var diag = await SteadySpeed(45, 0, Godot.Key.W, Godot.Key.D); // heading north-west, so W+D runs due north
        Check(MathF.Abs(diag / levelSpeed - 1) < 0.03f, F($"W+D is not faster than W ({diag:0.00} vs {levelSpeed:0.00} m/s)"));

        // ---- 6. Jump (JUMP-01), on flat plaza ground
        App.ProbePlaceUser(new PlacePose(new NVector3(0, 1.75f, 5.2f), 0, -5)); // [direct] a clear flat spot south of the landmark
        await Frames(3);
        var takeoff = W.Feet.Y;
        var apex = 0f;
        var jumps = W.Jumps;
        Key1(Godot.Key.Space, true);
        await Frames(2);
        Key1(Godot.Key.Space, false);
        for (var i = 0; i < 70; i++) { apex = MathF.Max(apex, W.Feet.Y - takeoff); await Frames(1); }
        await Until(() => W.Grounded, 2);
        Check(W.Jumps == jumps + 1 && apex > 0.6f && apex < 0.95f && W.Grounded, F($"Space jumps about 0.8 m and lands (apex {apex:0.00} m)"));

        // ---- 7. The overlook by its ramp, then by the jump blocks (JUMP-02)
        var ov = Place.Overlook;
        var foot = ov.RampFoot;
        var axis = NVector2.Normalize(new NVector2(ov.Top.X, ov.Top.Z) - foot);
        var rampStart = foot - axis * 1.5f;
        App.ProbePlaceUser(new PlacePose(new NVector3(rampStart.X, 1.75f, rampStart.Y), -PlaceTerrain.PlaceAngle(axis), -5)); // [direct]
        await Frames(3);
        jumps = W.Jumps;
        await Hold(3.0, Godot.Key.W);
        await Seconds(0.3);
        Check(MathF.Abs(W.Support - ov.Top.Y) < 0.02f && W.Jumps == jumps, F($"the ramp leads up to the overlook without jumping (support {W.Support:0.00})"));
        App.ProbeSetGaze(-PlaceTerrain.PlaceAngle(-new NVector2(ov.Top.X, ov.Top.Z)), -10); // [direct] face the plaza
        await Frames(3);
        await Shot("overlook");

        var blocks = ov.JumpBlocks;
        var along = NVector2.Normalize(blocks[1] - blocks[0]);
        var before1 = blocks[0] - along * 4f;
        App.ProbePlaceUser(new PlacePose(new NVector3(before1.X, 1.75f, before1.Y), -PlaceTerrain.PlaceAngle(along), -5)); // [direct]
        await Frames(3);
        var heights = new[] { ov.Top.Y / 3, ov.Top.Y * 2 / 3, ov.Top.Y };
        var stops = new[] { blocks[0], blocks[1], new NVector2(ov.Top.X, ov.Top.Z) };
        var reached = 0;
        for (var k = 0; k < 3; k++)
        {
            var target = Place.Terrain.Boxes.First(b => b.Kind is TerrainKind.JumpBlock or TerrainKind.Platform && b.Closest(stops[k]).Distance == 0 && MathF.Abs(b.Top - heights[k]) < 0.01f);
            App.ProbeSetGaze(-PlaceTerrain.PlaceAngle(NVector2.Normalize(stops[k] - FeetXz)), -5); // [direct] aim (steering is the player's job)
            Key1(Godot.Key.W, true);
            await Until(() => target.Closest(FeetXz).Distance < 1.0f, 3);
            Key1(Godot.Key.Space, true);
            await Frames(2);
            Key1(Godot.Key.Space, false);
            await Until(() => W.Grounded && W.Support > heights[k] - 0.05f, 1.5);
            Key1(Godot.Key.W, false); // stop on the block (a 1.4 m block is quickly walked off)
            await Seconds(0.35);
            if (MathF.Abs(W.Support - heights[k]) < 0.05f) reached++;
            else break;
        }

        Check(reached == 3, $"the jump blocks lead onto the overlook with three jumps ({reached}/3)");
        await Key(Godot.Key.Home);
        await WaitGuide();
        Check(MathF.Abs(App.Rig.CameraPosition.DistanceTo(ToGd(Place.Arrival.Eye))) < 0.1f && App.Locomotion == Locomotion.Walking, "Home returns to the arrival point on foot");

        // ---- 8. Enter a child and come back (PLACE-06/07)
        var photos = Find(root, "Photos");
        Check(await ClickDestination(photos) && App.Locomotion == Locomotion.Walking && W.Grounded, "clicking Photos in Destinations walks you up to its gate");
        Check(await WalkThrough(Godot.Key.W, 6) && App.Navigator.Current == photos, "walking through the Photos gate enters Photos");
        Check(MathF.Abs(EyeAboveSupport - 1.75f) < 0.03f, "arrival in the child is on foot");
        await Shot("child-entered");
        await Key(Godot.Key.U);
        await WaitPlace();
        Check(App.Navigator.Current == root && App.Selection.Selected == photos, "U returns in front of the Photos gate with Photos selected");

        // ---- 9. Follow a junction outside the root, and return (LINK-02..04)
        var shared = Find(root, "to-shared-library");
        Check(shared >= 0 && Tree[shared].Link?.Kind == LinkKind.Junction, "to-shared-library is identified as a junction");
        Check(Place.GateFor(shared) is { CanEnter: true, IsLink: true }, "its gate is a connection that can be entered");
        Check(await ClickDestination(shared), "approach the link's gate");
        Check(await WalkThrough(Godot.Key.W, 8), "walking through the link's gate travels");
        var here = App.Navigator.Current;
        Check(Tree.DisplayName(here) == "Shared Library" && !Tree.FullPath(here).Contains(@"\Wayfarer\", StringComparison.Ordinal), $"arrived at the link's real destination outside the root ({Tree.FullPath(here)})");
        Check(App.Hud.ConnectionText.Contains("to-shared-library", StringComparison.Ordinal), $"the HUD names the connection used: {App.Hud.ConnectionText}");
        Check(!App.Hud.BreadcrumbNames.Contains("Wayfarer") && App.Hud.BreadcrumbNames.LastOrDefault() == "Shared Library", $"the breadcrumb is the physical location ({string.Join(" › ", App.Hud.BreadcrumbNames)})");
        await Shot("arrived-through-a-link");
        await LabelsClear("the link's destination with the link selected");
        await Key(Godot.Key.Backspace);
        await WaitPlace();
        Check(App.Navigator.Current == root && App.Selection.Selected == shared, "Backspace returns through the link, in front of its gate, with the link selected");
        await Shot("back-through-the-link");

        // ---- 10. A loop, and two routes to one destination (LINK-05)
        var count = Tree.Count;
        App.Selection.Select(Find(root, "back-to-wayfarer")); // [direct] select
        await Key(Godot.Key.G);
        await Until(() => App.LastWorldNote != null, 5);
        Check(App.Navigator.Current == root && (App.LastWorldNote ?? "").Contains("loop", StringComparison.Ordinal), $"a loop link says it leads back here: {App.LastWorldNote}");
        App.Selection.Select(Find(root, "photos-shortcut")); // [direct]
        await Key(Godot.Key.G);
        await WaitPlace();
        var viaFirst = App.Navigator.Current;
        await Key(Godot.Key.Backspace);
        await WaitPlace();
        App.Selection.Select(Find(root, "also-photos")); // [direct]
        await Key(Godot.Key.G);
        await WaitPlace();
        Check(viaFirst == photos && App.Navigator.Current == photos, "two links reach the same Photos place");
        Check(App.Hud.ConnectionText.Contains("also reached by 1 other link", StringComparison.Ordinal), $"…and the HUD says so: {App.Hud.ConnectionText}");
        Check(Tree.Count == count, $"no duplicate sub-world was built ({count} → {Tree.Count} nodes)");
        await Key(Godot.Key.Backspace);
        await WaitPlace();

        // ---- 11. A broken link and an inaccessible folder: distinct, aggregated (LINK-06, VISI-03)
        var broken = Find(root, "broken-link");
        App.Selection.Select(broken); // [direct]
        await Key(Godot.Key.G);
        await Until(() => Tree[broken].Link?.Status == LinkStatus.Broken, 5);
        await Frames(10);
        Check(Tree[broken].Link?.Status == LinkStatus.Broken && App.Navigator.Current == root, "a broken link is reported as broken and does not travel");
        Check(Place.GateFor(broken) is { CanEnter: false }, "a broken link's gate cannot be walked through");
        Check(App.Hud.ProblemCount >= 2 && App.Hud.ProblemButtonText.Contains('⚠'), $"problems are aggregated: {App.Hud.ProblemButtonText}");
        await Shot("problems-and-links");

        // ---- 12. Hidden and system entries (VISI-04)
        var gatesBefore = Place.Gates.Count;
        var hiddenBefore = Place.HiddenCount;
        var selected = App.Selection.Selected;
        var history = App.Navigator.History.Count;
        var bytes = Tree[root].SubtreeBytes;
        Check(hiddenBefore == 2 && Place.Gates.Any(g => Tree[g.TargetId].Name == ".config") && Place.Files.Any(f => Tree[f.Id].Name == ".env"),
            $"hidden/system folders are hidden by default ({hiddenBefore}) while dot-names stay visible");
        await Key(Godot.Key.H);
        await Frames(4);
        Check(Place.Gates.Count == gatesBefore + 2 && App.ShowHidden, $"H shows them ({gatesBefore} → {Place.Gates.Count} gates)");
        Check(App.Selection.Selected == selected && App.Navigator.Current == root && App.Navigator.History.Count == history && Tree[root].SubtreeBytes == bytes, "visibility changes nothing else (selection, place, history, totals)");
        await Key(Godot.Key.H);
        await Frames(4);

        // ---- 13. A software project (IDENT-02..04)
        var projects = Find(root, "Projects");
        App.Selection.Select(projects); // [direct]
        await Key(Godot.Key.G);
        await WaitPlace();
        var lighthouse = Find(projects, "lighthouse");
        App.Selection.Select(lighthouse); // [direct]
        await Key(Godot.Key.G);
        await WaitPlace();
        Check(Place.Kind.Kind == PlaceKind.Project && Place.Kind.Evidence.Any(e => e.Contains("Lighthouse.csproj", StringComparison.Ordinal)), $"lighthouse is recognised as a software project ({string.Join(", ", Place.Kind.Evidence)})");
        Check(Place.Files.Count(f => f.Core) >= 2 && Place.Files.Any(f => f.Core && Tree[f.Id].Name == "README.md"), "its README and manifests form the project core");
        await Shot("project-place");
        await LabelsClear("the project place");
        await Key(Godot.Key.U);
        await WaitPlace();
        var notesOnly = Find(projects, "notes-only");
        Check(PlaceKinds.Classify(Tree, notesOnly).Kind == PlaceKind.Ordinary, "a README without a manifest stays an ordinary folder");

        // ---- 14. Up beyond the arrival root (LINK-04)
        await KeyCombo(Godot.Key.Home, ctrl: true);
        await WaitPlace();
        await Key(Godot.Key.U);
        await WaitPlace();
        var parent = Tree[root].ParentId;
        Check(App.Navigator.Current == parent && Tree[parent].ScanState == DirScanState.Complete, $"U from the arrival root goes on to its parent, listed on arrival ({Tree.DisplayName(parent)})");
        await Shot("above-the-arrival-root");
        await KeyCombo(Godot.Key.Home, ctrl: true);
        await WaitPlace();
        Check(App.Navigator.Current == root, "Ctrl+Home returns to the arrival root");

        // ---- 15. WSL through a typed path (LOC-01, LOC-03)
        var wsl = System.Environment.GetEnvironmentVariable("FILESCAPE_WSL_FIXTURE");
        if (!string.IsNullOrEmpty(wsl))
        {
            await KeyCombo(Godot.Key.L, ctrl: true);
            await Frames(3);
            Check(App.Hud.LocationsVisible && App.Hud.PathEdit.HasFocus(), "Ctrl+L opens Go to path with the field focused");
            var feetBefore = W.Feet;
            await TypeText(wsl);
            await Hold(0.5, Godot.Key.Up); // the arrow keys belong to the focused field, not to walking
            Check(NVector3.Distance(feetBefore, W.Feet) < 0.01f && App.Hud.PathEdit.Text == wsl, "keys go to the focused path field and do not move you");
            Key1(Godot.Key.Enter, true);
            await Frames(2);
            Key1(Godot.Key.Enter, false);
            await Until(() => App.Place != null && App.PendingRequests == 0 && !App.InTransition && Tree.FullPath(App.Navigator.Current).Equals(wsl, StringComparison.OrdinalIgnoreCase), 30);
            await WaitPlace(30);
            var wid = App.Navigator.Current;
            Check(Tree.FullPath(wid).Equals(wsl, StringComparison.OrdinalIgnoreCase), $"arrived in the WSL folder ({Tree.FullPath(wid)}; last error: {App.LastExplorerError ?? "-"})");
            Check(App.Hud.BreadcrumbNames.Contains("Ubuntu-24.04"), $"breadcrumb: {string.Join(" › ", App.Hud.BreadcrumbNames)}");
            await Until(() => Tree[wid].ScanState == DirScanState.Complete, 10);
            Check(Place.Gates.Any(g => Tree[g.TargetId].Name == "Notes") && Place.Gates.Any(g => Tree[g.TargetId].Name == "notes"), "Notes and notes are two separate gates (case-sensitive)");
            var lx = Tree[wid].Children.Select(c => Tree[c]).FirstOrDefault(n => n.Name == "to-real");
            Check(lx?.Link is { Kind: LinkKind.LinuxSymlink, Status: LinkStatus.Unsupported }, "a Linux symlink is identified and marked as not followable through Windows");
            await Shot("wsl-arrival");
            await LabelsClear("the WSL folder");
        }
        else
        {
            Log("SKIP WSL steps: FILESCAPE_WSL_FIXTURE not set");
        }

        // ---- 16. The Locations hub (LOC-01)
        await Key(Godot.Key.L);
        await Frames(3);
        Check(App.Hud.LocationsVisible && App.Hud.LocationRowCount >= 3, $"L opens Locations ({App.Hud.LocationRowCount} rows)");
        var hubRow = App.Hud.LocationRowIndex(0);
        await Click(App.Hud.LocationRowRect(hubRow).GetCenter());
        await WaitPlace();
        Check(App.Navigator.Current == 0 && Place.Kind.Kind == PlaceKind.Hub && Place.Gates.Count >= 2, $"the hub is a place with a gate per location ({Place.Gates.Count})");
        Check(Place.Gates.Any(g => Tree[g.TargetId].Location == LocationKind.Wsl) && Place.Gates.Any(g => Tree[g.TargetId].Location == LocationKind.Drive), "drives and WSL distributions are both there");
        await Shot("locations-hub");
        await LabelsClear("the hub");
        await KeyCombo(Godot.Key.Home, ctrl: true);
        await WaitPlace();

        // ---- 17. Flight and safe landing (WALK-06/07)
        await Hold(1.2, Godot.Key.Up);
        var y0 = App.Rig.CameraPosition.Y;
        var maxStep = 0f;
        var last = y0;
        Key1(Godot.Key.T, true);
        await Frames(2);
        Key1(Godot.Key.T, false);
        for (var i = 0; i < 60; i++) { await Frames(1); var y = App.Rig.CameraPosition.Y; maxStep = MathF.Max(maxStep, MathF.Abs(y - last)); last = y; }
        Check(App.Locomotion == Locomotion.Flying && last - y0 > 3f && maxStep < 0.5f && App.Hud.FlyChipVisible && App.Hud.FlightButtonPressed,
            F($"T takes off: a continuous rise of {last - y0:0.0} m (largest step {maxStep:0.00} m), chip and pressed button"));
        await Hold(1.5, Godot.Key.E);
        await Hold(1.0, Godot.Key.W);
        await Shot("flying");
        await Key(Godot.Key.T);
        await Until(() => App.Locomotion == Locomotion.Walking && !App.LandingInProgress, 5);
        await Seconds(0.3);
        Check(App.Locomotion == Locomotion.Walking && W.Grounded && MathF.Abs(EyeAboveSupport - 1.75f) < 0.05f && Place.Terrain.IsClear(FeetXz, W.Params.Radius, W.Support, W.Params.BodyHeight),
            F($"T lands on a valid surface below, not inside anything (support {W.Support:0.00})"));
        await Shot("landed");

        var vista = Place.Vistas.Count > 0 ? Place.Vistas[0] : null;
        if (vista != null)
        {
            var reloc = App.Relocations;
            App.ProbeFlyAt(new Vector3(vista.Center.X, 12, vista.Center.Z)); // [direct] above a vista, outside the walkable place
            await Frames(3);
            await Key(Godot.Key.T);
            await WaitGuide();
            await Seconds(0.3);
            Check(App.Relocations == reloc + 1 && App.Locomotion == Locomotion.Walking && (App.LastLandingNote ?? "").Contains("arrival", StringComparison.Ordinal) && MathF.Abs(App.Rig.CameraPosition.DistanceTo(ToGd(Place.Arrival.Eye))) < 0.2f,
                $"landing above a vista guides you to the arrival point and says why: {App.LastLandingNote}");
        }

        // ---- 18. Modal UI and focus (WALK-09)
        await Key(Godot.Key.L);
        var f0 = W.Feet;
        await Hold(0.6, Godot.Key.Up);
        Check(NVector3.Distance(f0, W.Feet) < 0.01f, "with Locations open, the arrow keys do not walk");
        Key1(Godot.Key.Escape, true);
        await Frames(2);
        Key1(Godot.Key.Escape, false);
        await Frames(3);
        Check(!App.Hud.LocationsVisible, "Esc closes Locations");
        Check(App.Hud.BlockingRects.Any(), "HUD rectangles are known to labels and picking");

        // ---- 19. Performance while walking (no scan running)
        await PerfWalk("wayfarer-walk");
    }

    /// <summary>Holds keys for a number of frames (the movie writer runs at a fixed frame rate, so time is frames).</summary>
    private async Task HoldFrames(int frames, params Godot.Key[] keys)
    {
        foreach (var k in keys) Key1(k, true);
        await Frames(frames);
        foreach (var k in keys) Key1(k, false);
        await Frames(1);
    }

    private async Task LookFrames(Vector2 perFrame, int frames)
    {
        var centre = App.GetViewport().GetVisibleRect().Size / 2;
        Mouse(MouseButton.Right, true, centre);
        await Frames(1);
        for (var i = 0; i < frames; i++) { Motion(perFrame); await Frames(1); }
        Mouse(MouseButton.Right, false, centre);
        await Frames(1);
    }

    /// <summary>
    /// A short guided recording at eye level (--write-movie … --fixed-fps 30): arrival, the plaza, the overlook by its
    /// ramp and a jump down, a junction to another place and back, WSL by a typed path, take-off and a safe landing.
    /// Steps marked [direct] only frame the shot.
    /// </summary>
    private async Task Tour()
    {
        var root = App.Navigator.Root;
        App.SetLabelsVisible(true);
        await Frames(45);
        await LookFrames(new Vector2(-3, 0), 40);
        await LookFrames(new Vector2(3, 0), 40);
        await HoldFrames(75, Godot.Key.Up); // down the stairs and along the clear avenue
        await HoldFrames(22, Godot.Key.Up, Godot.Key.Left);
        await HoldFrames(30, Godot.Key.Up, Godot.Key.Right);
        await Frames(15);

        // The overlook: up the ramp, look out over the plaza, jump down.
        var ov = Place.Overlook;
        var axis = NVector2.Normalize(new NVector2(ov.Top.X, ov.Top.Z) - ov.RampFoot);
        var start = ov.RampFoot - axis * 2.5f;
        App.ProbePlaceUser(new PlacePose(new NVector3(start.X, 1.75f, start.Y), -PlaceTerrain.PlaceAngle(axis), -4)); // [direct]
        await Frames(10);
        await HoldFrames(80, Godot.Key.W);
        await LookFrames(new Vector2(-6, 0.4f), 30);
        await Frames(30);
        await LookFrames(new Vector2(-6, -0.4f), 30);
        Key1(Godot.Key.W, true);
        await Frames(8);
        Key1(Godot.Key.Space, true);
        await Frames(2);
        Key1(Godot.Key.Space, false);
        await Frames(30);
        Key1(Godot.Key.W, false);
        await Frames(20);

        // Through the junction to the Shared Library, and back.
        var link = Tree[root].Children.FirstOrDefault(c => Tree[c].Name == "to-shared-library", -1);
        if (link >= 0 && Place.GateFor(link) is { } g)
        {
            var approach = Place.ApproachPose(link, PlaceParams.Default);
            App.ProbePlaceUser(approach); // [direct]
            App.Selection.Select(link);
            await Frames(30);
            await HoldFrames(70, Godot.Key.W);
            for (var i = 0; i < 120 && (App.InTransition || App.PendingRequests > 0); i++) await Frames(1);
            await Frames(40);
            await LookFrames(new Vector2(4, 0), 30);
            await Frames(30);
            await Key(Godot.Key.Backspace);
            for (var i = 0; i < 120 && App.InTransition; i++) await Frames(1);
            await Frames(45);
        }

        // WSL by a typed path.
        var wsl = System.Environment.GetEnvironmentVariable("FILESCAPE_WSL_FIXTURE");
        if (!string.IsNullOrEmpty(wsl))
        {
            await KeyCombo(Godot.Key.L, ctrl: true);
            await Frames(10);
            await TypeText(wsl);
            await Frames(20);
            Key1(Godot.Key.Enter, true);
            await Frames(2);
            Key1(Godot.Key.Enter, false);
            for (var i = 0; i < 600 && !(App.Place != null && !App.InTransition && App.PendingRequests == 0 && Tree.FullPath(App.Navigator.Current).Equals(wsl, StringComparison.OrdinalIgnoreCase)); i++) await Frames(1);
            await Frames(30);
            await HoldFrames(45, Godot.Key.Up);
            await LookFrames(new Vector2(-3, 0), 40);
            await Frames(20);
            await KeyCombo(Godot.Key.Home, ctrl: true);
            for (var i = 0; i < 120 && App.InTransition; i++) await Frames(1);
            await Frames(30);
        }

        // Flight: take off, fly a little, land on foot.
        await HoldFrames(30, Godot.Key.Up);
        await Key(Godot.Key.T);
        await Frames(50);
        await HoldFrames(40, Godot.Key.E);
        await LookFrames(new Vector2(0, 2f), 20);
        await HoldFrames(45, Godot.Key.W);
        await Frames(15);
        await Key(Godot.Key.T);
        for (var i = 0; i < 150 && (App.LandingInProgress || App.GuidedGlide); i++) await Frames(1);
        await LookFrames(new Vector2(0, -2f), 20);
        await Frames(45);
        Log($"tour done: locomotion={App.Locomotion} current={Tree.FullPath(App.Navigator.Current)}");
    }

    /// <summary>Stationary frame cost at the same spot, varying eye height, quality and one effect at a time.</summary>
    private async Task PerfIsolation()
    {
        var env = App.World.Environment;
        async Task Measure(string label)
        {
            await Seconds(0.6);
            App.Perf.StartRecording();
            await Seconds(2.5);
            var samples = App.Perf.StopRecording();
            var s2 = PerfRecorder.Summarize(samples.Select(x => x.FrameMs).ToList());
            var g = PerfRecorder.Summarize(samples.Select(x => x.GpuMs).ToList());
            _perf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{label},{App.Quality},{s2.Count},{s2.Mean:0.00},{s2.P50:0.00},{s2.P95:0.00},{s2.P99:0.00},{s2.Max:0.00},{g.Mean:0.00},{App.InstanceCount},{Tree.Count}\n");
            Log(F($"perf {label} {App.Quality}: mean={s2.Mean:0.00} p95={s2.P95:0.00} gpu={g.Mean:0.00} ms"));
        }

        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        var a = Place.Arrival;
        await Measure("arrival-eye-3.35");
        App.ProbePlaceUser(new PlacePose(new NVector3(0, 1.75f, 9f), 0, -4)); // [direct] plaza, facing north
        await Measure("plaza-eye-1.75");
        App.ProbeFlyAt(new Vector3(0, 4.6f, 9f)); // [direct] same spot, the old walking height
        await Measure("plaza-eye-4.6");
        App.ProbeFlyAt(new Vector3(0, 1.75f, 9f));
        env.SsrEnabled = false; await Measure("eye-1.75-no-ssr"); env.SsrEnabled = true;
        env.VolumetricFogEnabled = false; await Measure("eye-1.75-no-volfog"); env.VolumetricFogEnabled = true;
        env.GlowEnabled = false; await Measure("eye-1.75-no-glow"); env.GlowEnabled = true;
        env.SsaoEnabled = false; await Measure("eye-1.75-no-ssao"); env.SsaoEnabled = true;
        App.SetLabelsVisible(false); App.PlaceWorld.Visible = false; await Measure("eye-1.75-no-place-geometry"); App.PlaceWorld.Visible = true; App.SetLabelsVisible(true);
        App.SetQuality(FileScape.App.Visual.Quality.Low);
        await Measure("eye-1.75-low");
        App.SetQuality(FileScape.App.Visual.Quality.High);
        App.ProbePlaceUser(a);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
    }

    private async Task PerfWalk(string label)
    {
        await Seconds(0.5);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        App.Perf.StartRecording();
        await Hold(1.5, Godot.Key.Up);
        await Hold(1.2, Godot.Key.Up, Godot.Key.Left);
        await Hold(1.2, Godot.Key.W, Godot.Key.Shift);
        await Look(new Vector2(600, 40), 90);
        await Hold(1.0, Godot.Key.S);
        var samples = App.Perf.StopRecording();
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
        var s2 = PerfRecorder.Summarize(samples.Select(x => x.FrameMs).ToList());
        var g = PerfRecorder.Summarize(samples.Select(x => x.GpuMs).ToList());
        _perf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{label},{App.Quality},{s2.Count},{s2.Mean:0.00},{s2.P50:0.00},{s2.P95:0.00},{s2.P99:0.00},{s2.Max:0.00},{g.Mean:0.00},{App.InstanceCount},{Tree.Count}\n");
        Log(F($"perf {label} {App.Quality}: frames={s2.Count} mean={s2.Mean:0.00} p95={s2.P95:0.00} p99={s2.P99:0.00} max={s2.Max:0.00} gpu={g.Mean:0.00} instances={App.InstanceCount}"));
    }

    private static Vector3 ToGd(NVector3 v) => new(v.X, v.Y, v.Z);

    private void LogPose(string what)
    {
        var w = App.Walker;
        Log(F($"{what}: locomotion={App.Locomotion} feet=({w.Feet.X:0.00},{w.Feet.Y:0.00},{w.Feet.Z:0.00}) support={w.Support:0.00} eye={App.Rig.CameraPosition.Y:0.00} yaw={w.Yaw:0.0} pitch={w.LookPitch:0.0} grounded={w.Grounded}"));
    }
}
