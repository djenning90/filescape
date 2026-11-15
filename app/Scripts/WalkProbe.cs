using System.Text;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Units;
using Godot;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// The walking-world journey (--automate=walk…, WALK/JUMP/LINK/LOC/VISI). Input is injected through Godot's pipeline
/// (ProbeBase); "[direct]" steps only set up a case.
/// <para>
/// It runs in Places on the owned Wayfarer fixture (its names — Photos, Journeys › Coast › Harbour › Pier,
/// to-shared-library, Projects/lighthouse — are what the checks look for) and proves walking at eye level: the eye
/// height, walk and run speeds, stairs, ramps and jump blocks, jumping, gates and links, the upward chain, hidden
/// entries, locations, flight and safe landing, mouse-look direction settings and levelling on release, and that
/// labels and signs never overlap the HUD or each other. Other modes are in WalkProbe.Explore.cs (camera, vertical
/// controls and the aerial world), WalkProbe.Project.cs (the hub and project actions) and WalkProbe.AerialLimit.cs.
/// </para>
/// <para>
/// Timing: speeds are measured over a timed window after the start-up ramp, so they do not depend on when a key
/// event lands; positions used as starting points are set directly (marked [direct]) so each measurement starts on
/// clear, flat ground.
/// </para>
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

    /// <summary>
    /// The walker's eye above its support, metres: the default <c>WalkParams.EyeHeight</c>, used for poses set
    /// directly and for the "on foot" checks (which allow 3 cm, or 5 cm just after a landing).
    /// </summary>
    private const float EyeMetres = 1.75f;

    protected override async Task RunProbe()
    {
        await Until(() => App.Place != null && !App.InTransition, 20);
        await Seconds(1.0);
        if (Mode == "walk-look") await LookAround();
        else if (Mode == "walk-perf") await PerfIsolation();
        else if (Mode == "walk-tour") await Tour();
        else if (Mode == "walk-explore") await Explore();
        else if (Mode == "walk-project") await ProjectJourney();
        else if (Mode == "walk-aerial-limit") await AerialLimitJourney();
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
        App.ProbePlaceUser(new PlacePose(new NVector3(start.X, EyeMetres, start.Y), PlaceGeometry.YawFor(PlaceTerrain.PlaceAngle(axis)), Degrees.Of(-5))); // [direct]
        await Frames(3);
        await Hold(3.2, Godot.Key.W);
        await Seconds(0.5);
        LogPose("after walking up the ramp");
        App.ProbeSetGaze(PlaceGeometry.YawFor(PlaceTerrain.PlaceAngle(-new NVector2(ov.Top.X, ov.Top.Z))), Degrees.Of(-8)); // [direct] face the plaza
        await Frames(3);
        await Shot("overlook-view");
        if (Place.Files.Count > 0)
        {
            // [direct] Close to a file tower, facing its badge side (ICON-01 readability at 7 m).
            var f = Place.Files.OrderBy(x => MathF.Abs(x.Box.Center.Length() - 14f)).First();
            var c = new NVector2(f.Box.Center.X, f.Box.Center.Z);
            var toward = NVector2.Normalize(c);
            var at = c - toward * 7f; // between the landmark (r ≈ 2.4 m) and the tower, on the plaza
            App.ProbePlaceUser(new PlacePose(new NVector3(at.X, EyeMetres, at.Y), PlaceGeometry.YawFor(PlaceTerrain.PlaceAngle(toward)), Degrees.Of(-4)));
            await Frames(4);
            await Shot("file-badges-7m");
        }

        // [direct] The arrival terrace and its stairs seen from the plaza (WALK-11: solid, lit steps).
        App.ProbePlaceUser(new PlacePose(new NVector3(0, EyeMetres, Place.Arrival.Eye.Z - 9f), PlaceGeometry.YawFor(180f) + Degrees.Of(20), Degrees.Of(-12)));
        await Frames(4);
        await Shot("terrace-stairs");
        App.ProbePlaceUser(Place.Arrival);
        await Frames(3);
        App.ProbeSetGaze(PlaceGeometry.YawFor(PlaceTerrain.PlaceAngle(new NVector2(ov.Top.X, ov.Top.Z))), Degrees.Zero);
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
    private float EyeAboveSupport => App.Rig.CameraPosition.Y - W.Support.Value;
    private NVector2 FeetXz => new(W.Feet.X, W.Feet.Z);

    /// <summary>The child of <paramref name="parent"/> with exactly this name, or −1.</summary>
    private int Find(int parent, string name) => Tree[parent].Children.FirstOrDefault(c => Tree[c].Name == name, -1);

    /// <summary>Waits until a place is shown with no transition, arrival or listing request outstanding, then 4 frames.</summary>
    private async Task WaitPlace(double seconds = 15)
    {
        await Until(() => App.Place != null && !App.InTransition && !App.AwaitingArrival && App.PendingRequests == 0, seconds);
        await Frames(4);
    }

    /// <summary>Waits (at most 8 s) for a guided glide (the walk up to a selected gate) and any transition to finish.</summary>
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
        await SetDestinationsOpen(true); // Destinations is an on-demand drawer (VIEW-03): open it with its button
        App.Hud.EnsureDestinationVisible(index);
        await Frames(3);
        await Click(App.Hud.DestinationItemRect(index).GetCenter());
        await WaitGuide();
        await SetDestinationsOpen(false);
        return true;
    }

    /// <summary>Opens or closes the Destinations drawer by clicking its real button (no keyboard focus is taken).</summary>
    private async Task SetDestinationsOpen(bool open)
    {
        if (App.Hud.DestinationsOpen == open) return;
        await Click(App.Hud.DestinationsButtonRect.GetCenter());
        await Frames(3);
    }

    /// <summary>Holds a key until a transition starts (or the time runs out) and the place settles; true if a place change completed.</summary>
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
    private async Task<float> SteadySpeed(Degrees yaw, Degrees pitch, params Godot.Key[] keys) => await SteadySpeed(yaw, pitch, 1.0, keys);

    private async Task<float> SteadySpeed(Degrees yaw, Degrees pitch, double window, params Godot.Key[] keys)
    {
        // [direct] the same clear start just north of the landmark each time: ≈ 20 m of free plaza ahead (north, the
        // gap between the gates). At 8 m/s the old start in the south corridor ran into the landmark.
        App.ProbePlaceUser(new PlacePose(new NVector3(0, EyeMetres, -3f), yaw, pitch));
        await Frames(3);
        foreach (var k in keys) Key1(k, true);
        await Seconds(0.45); // past the walker's start-up ramp, so the window measures the steady speed
        var p0 = FeetXz;
        var t0 = Time.GetTicksUsec();
        await Seconds(window); // a longer window keeps one frame of sampling jitter below 2 %
        var p1 = FeetXz;
        var t1 = Time.GetTicksUsec();
        foreach (var k in keys) Key1(k, false);
        await Seconds(0.3);
        return NVector2.Distance(p0, p1) / ((t1 - t0) / 1e6f);
    }

    /// <summary>A right-mouse look by <paramref name="total"/> pixels in equal steps, one a frame; records the pitch just before release.</summary>
    private async Task Look(Vector2 total, int steps = 20)
    {
        var centre = App.GetViewport().GetVisibleRect().Size / 2;
        Mouse(MouseButton.Right, true, centre);
        await Frames(2);
        for (var i = 0; i < steps; i++) { Motion(total / steps); await Frames(1); }
        _pitchAtRelease = W.LookPitch.Value;
        Mouse(MouseButton.Right, false, centre);
        await Frames(3);
    }

    /// <summary>The look pitch (degrees) just before the last <see cref="Look"/> or <see cref="DragLeft"/> let go: after that the view eases back to level (CTRL-04).</summary>
    private float _pitchAtRelease;

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

        // In-world signs too (2026-11-05 correction): none drawn under a HUD panel or over another sign.
        var signs = App.PlaceWorld.VisibleSignRects(App.Rig.Camera).ToList();
        var signOverlaps = 0;
        for (var i = 0; i < signs.Count; i++)
        {
            for (var j = i + 1; j < signs.Count; j++) if (signs[i].Intersects(signs[j])) signOverlaps++;
        }

        var signsOnHud = signs.Count(r => hud.Any(h => h.Intersects(r)));
        var signsOverLabels = signs.Count(r => rects.Any(l => l.Intersects(r)));
        Check(signOverlaps == 0 && signsOnHud == 0 && signsOverLabels == 0, $"world signs at {where}: {signs.Count} shown, {signOverlaps} overlapping, {signsOnHud} under the HUD, {signsOverLabels} under overlay labels");
    }

    /// <summary>
    /// --automate=walk: the main walking journey, nineteen numbered sections (see the comments), each starting from
    /// where the previous one left you. The tolerances are named in the check texts: speeds within 3 % of each other,
    /// the walk within 0.35 m/s and the run within 0.7 m/s of their settings, a tapped jump's apex between 0.6 and
    /// 0.95 m (about 0.8 m; a held jump goes higher, checked in WalkProbe.Explore.cs).
    /// </summary>
    private async Task Journey()
    {
        var tree = Tree;
        var root = App.Navigator.Root;
        var rootName = tree.DisplayName(root);
        Log($"arrival root {tree.FullPath(root)}");

        // ---- 1. Arrive on foot (WALK-01, PLACE-02)
        Check(App.Locomotion == Locomotion.Walking, "arrives on foot (walking is the default)");
        Check(MathF.Abs(EyeAboveSupport - EyeMetres) < 0.03f, F($"eye is 1.75 m above the support (got {EyeAboveSupport:0.00} on support {W.Support.Value:0.00})"));
        Check(App.Hud.BreadcrumbNames.LastOrDefault() == rootName && App.Hud.BreadcrumbNames.FirstOrDefault() == "This computer", $"breadcrumb runs from This computer to {rootName} ({string.Join(" › ", App.Hud.BreadcrumbNames)})");
        Check(App.PlaceWorld.LandmarkSign == rootName, "the landmark plaque names the place");
        await Shot("arrival-on-foot");
        await LabelsClear("arrival");

        // ---- 2. Look and steer with the mouse (WALK-03), in the direction the user chose (CTRL-01/02)
        await MouseDirection();
        var yaw0 = W.Yaw.Value;
        App.ProbeSetGaze(Degrees.Zero, Degrees.Of(-6)); // [direct] face north again

        // ---- 3. Walk down the stairs (WALK-05)
        var start = FeetXz;
        await Hold(1.4, Godot.Key.Up);
        await Seconds(0.3);
        Check(W.Grounded && W.Support.Value < 0.01f && W.Jumps == 0, F($"Up arrow walks off the terrace down the stairs without jumping (support {W.Support.Value:0.00})"));
        Check(NVector2.Distance(start, FeetXz) > 4f, F($"walked {NVector2.Distance(start, FeetXz):0.0} m"));
        await Shot("plaza-at-eye-level");

        // ---- 4. Looking sharply down does not push into the ground (WALK-02)
        App.ProbeSetGaze(W.Yaw, Degrees.Zero); // [direct]
        await Look(new Vector2(0, App.Preferences.ReverseLookVertical ? -700 : 700)); // down, in the chosen direction
        // 700 px × LookDegreesPerPixel (0.12) = 84°, just short of the −85° look limit; below −70° counts as "sharply".
        Check(_pitchAtRelease < -70f, F($"looking sharply down ({_pitchAtRelease:0} deg)"));
        var downSpeed = await SteadySpeed(Degrees.Zero, W.LookPitch, Godot.Key.W);
        var levelSpeed = await SteadySpeed(Degrees.Zero, Degrees.Zero, Godot.Key.W);
        Check(MathF.Abs(downSpeed / levelSpeed - 1) < 0.03f && W.Support.Value < 0.01f, F($"walking while looking down is as fast and stays on the ground ({downSpeed:0.00} m/s vs {levelSpeed:0.00} m/s level)"));
        var runSpeed = await SteadySpeed(Degrees.Zero, Degrees.Zero, 0.6, Godot.Key.W, Godot.Key.Shift);
        Check(MathF.Abs(levelSpeed - W.Params.WalkSpeed.Value) < 0.35f && MathF.Abs(runSpeed - W.Params.RunSpeed.Value) < 0.7f,
            F($"W walks at {levelSpeed:0.0} m/s and Shift+W runs at {runSpeed:0.0} m/s (settings: {W.Params.WalkSpeed.Value:0.0} / {W.Params.RunSpeed.Value:0.0})"));
        await Shot("looking-down-while-walking");

        // ---- 5. Turning with the arrows while walking curves (WALK-03), diagonal not faster (WALK-04)
        yaw0 = W.Yaw.Value;
        var a = FeetXz;
        // One second of Up + Left must turn more than 90° and still move more than 2.5 m: a curve, not a turn on the spot.
        await Hold(1.0, Godot.Key.Up, Godot.Key.Left);
        Check(W.Yaw.Value - yaw0 > 90f && NVector2.Distance(a, FeetXz) > 2.5f, F($"Up + Left walks a curve (turned {W.Yaw.Value - yaw0:0}°, moved {NVector2.Distance(a, FeetXz):0.0} m)"));
        var diag = await SteadySpeed(Degrees.Of(45), Degrees.Zero, Godot.Key.W, Godot.Key.D); // heading north-west, so W+D runs due north
        Check(MathF.Abs(diag / levelSpeed - 1) < 0.03f, F($"W+D is not faster than W ({diag:0.00} vs {levelSpeed:0.00} m/s)"));

        // ---- 6. Jump (JUMP-01), on flat plaza ground
        App.ProbePlaceUser(new PlacePose(new NVector3(0, EyeMetres, 5.2f), Degrees.Zero, Degrees.Of(-5))); // [direct] a clear flat spot south of the landmark
        await Frames(3);
        var takeoff = W.Feet.Y;
        var apex = 0f;
        var jumps = W.Jumps;
        Key1(Godot.Key.Space, true);
        await Frames(2);
        Key1(Godot.Key.Space, false);
        for (var i = 0; i < 70; i++) { apex = MathF.Max(apex, W.Feet.Y - takeoff); await Frames(1); } // 70 frames (≈ 1.2 s at 60 fps): longer than the ≈ 0.8 s air time
        await Until(() => W.Grounded, 2);
        Check(W.Jumps == jumps + 1 && apex > 0.6f && apex < 0.95f && W.Grounded, F($"Space jumps about 0.8 m and lands (apex {apex:0.00} m)"));

        // ---- 7. The overlook by its ramp, then by the jump blocks (JUMP-02)
        var ov = Place.Overlook;
        var foot = ov.RampFoot;
        var axis = NVector2.Normalize(new NVector2(ov.Top.X, ov.Top.Z) - foot);
        var rampStart = foot - axis * 1.5f;
        App.ProbePlaceUser(new PlacePose(new NVector3(rampStart.X, EyeMetres, rampStart.Y), PlaceGeometry.YawFor(PlaceTerrain.PlaceAngle(axis)), Degrees.Of(-5))); // [direct]
        await Frames(3);
        jumps = W.Jumps;
        await Hold(3.0, Godot.Key.W);
        await Seconds(0.3);
        Check(MathF.Abs(W.Support.Value - ov.Top.Y) < 0.02f && W.Jumps == jumps, F($"the ramp leads up to the overlook without jumping (support {W.Support.Value:0.00})"));
        App.ProbeSetGaze(PlaceGeometry.YawFor(PlaceTerrain.PlaceAngle(-new NVector2(ov.Top.X, ov.Top.Z))), Degrees.Of(-10)); // [direct] face the plaza
        await Frames(3);
        await Shot("overlook");

        var blocks = ov.JumpBlocks;
        var along = NVector2.Normalize(blocks[1] - blocks[0]);
        var before1 = blocks[0] - along * 4f;
        App.ProbePlaceUser(new PlacePose(new NVector3(before1.X, EyeMetres, before1.Y), PlaceGeometry.YawFor(PlaceTerrain.PlaceAngle(along)), Degrees.Of(-5))); // [direct]
        await Frames(3);
        var heights = new[] { ov.Top.Y / 3, ov.Top.Y * 2 / 3, ov.Top.Y };
        var stops = new[] { blocks[0], blocks[1], new NVector2(ov.Top.X, ov.Top.Z) };
        var reached = 0;
        for (var k = 0; k < 3; k++)
        {
            var target = Place.Terrain.Boxes.First(b => b.Kind is TerrainKind.JumpBlock or TerrainKind.Platform && b.Closest(stops[k]).Distance == 0 && MathF.Abs(b.Top - heights[k]) < 0.01f);
            App.ProbeSetGaze(PlaceGeometry.YawFor(PlaceTerrain.PlaceAngle(NVector2.Normalize(stops[k] - FeetXz))), Degrees.Of(-5)); // [direct] aim (steering is the player's job)
            Key1(Godot.Key.W, true);
            await Until(() => target.Closest(FeetXz).Distance < 1.0f, 3);
            Key1(Godot.Key.Space, true);
            await Frames(2);
            Key1(Godot.Key.Space, false);
            await Until(() => W.Grounded && W.Support.Value > heights[k] - 0.05f, 1.5);
            Key1(Godot.Key.W, false); // stop on the block (a 1.4 m block is quickly walked off)
            await Seconds(0.35);
            if (MathF.Abs(W.Support.Value - heights[k]) < 0.05f) reached++;
            else break;
        }

        Check(reached == 3, $"the jump blocks lead onto the overlook with three jumps ({reached}/3)");
        await Key(Godot.Key.Home);
        await WaitGuide();
        Check(MathF.Abs(App.Rig.CameraPosition.DistanceTo(ToGd(Place.Arrival.Eye))) < 0.1f && App.Locomotion == Locomotion.Walking, "Home returns to the arrival point on foot");

        // ---- 8. Enter a child and come back (PLACE-06/07)
        var photos = Find(root, "Photos");
        Check(await ClickDestination(photos) && App.Locomotion == Locomotion.Walking && W.Grounded, "clicking Photos in Destinations walks you up to its gate");
        await ApproachAndEnter(photos, "Photos");
        Check(MathF.Abs(EyeAboveSupport - EyeMetres) < 0.03f, "arrival in the child is on foot");
        await Shot("child-entered");
        await Key(Godot.Key.U);
        await WaitPlace();
        Check(App.Navigator.Current == root && App.Selection.Selected == photos, "U returns beside the Photos gate with Photos selected");

        // ---- 8b. Continuous upward travel (PLACE-16): Pier → Harbour → Coast → Journeys → Wayfarer with W held
        await UpwardChain(root);

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
        // Up from a link's destination is its physical parent (not history), arriving facing that place's ▲ gate.
        var libraryId = here;
        await Key(Godot.Key.U);
        await WaitPlace();
        Check(App.Navigator.Current == Tree[libraryId].ParentId && App.LastUpwardRoute is { Verified: true } && App.Selection.Selected == libraryId,
            F($"U from the link's destination goes to its physical parent ({Tree.DisplayName(App.Navigator.Current)}), facing its ▲ gate (verified route, {App.LastUpwardRoute?.StandOff:0.0} m from the Shared Library gate)"));
        await Key(Godot.Key.Backspace);
        await WaitPlace();
        Check(App.Navigator.Current == libraryId, "Backspace is history: back to the Shared Library");
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

        // An unavailable location (LOC-04): a removable drive with no media. Only entered when Windows reports it is
        // not ready, so nothing private can be listed.
        var notReady = Place.Gates.Select(g => Tree[g.TargetId]).FirstOrDefault(n => n.Location == LocationKind.Drive && n.Name != @"C:" && !new System.IO.DriveInfo(n.Name).IsReady);
        if (notReady != null)
        {
            App.Selection.Select(notReady.Id); // [direct]
            App.Perf.StartRecording();
            await Key(Godot.Key.G);
            await WaitPlace();
            await Until(() => Tree[notReady.Id].ScanState is DirScanState.Failed or DirScanState.Complete, 10);
            await Frames(10);
            var frames = App.Perf.StopRecording();
            var worst = frames.Count > 0 ? frames.Max(x => x.FrameMs) : 0;
            Check(App.Navigator.Current == notReady.Id && Place.State is ListingState.Unavailable or ListingState.Failed,
                F($"entering {notReady.Name} (no media) shows it as {Listing.Describe(Place.State)}: {Tree[notReady.Id].Error}"));
            // A frozen UI shows as one very long frame while Windows answers; 250 ms is the limit (rationale not recorded).
            Check(worst < 250, F($"the UI did not freeze while Windows answered (worst frame {worst:0} ms)"));
            await Shot("unavailable-drive");
            App.Hud.Toast(""); // [direct] clear the toast before the next shot
            await KeyCombo(Godot.Key.Home, ctrl: true);
            await WaitPlace();
        }
        else
        {
            Log("SKIP unavailable-location step: every removable drive has media (not listing it)");
        }
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
        // Continuous, not a cut: the rise takes visible time and no single frame carries much of it (a per-frame speed
        // measured from the probe's own timestamps proved too noisy against the frames' time steps).
        var riseStart = Time.GetTicksMsec();
        var riseEnd = riseStart;
        for (var i = 0; i < 150; i++)
        {
            await Frames(1);
            var y = App.Rig.CameraPosition.Y;
            maxStep = MathF.Max(maxStep, MathF.Abs(y - last));
            if (y - last > 0.002f) riseEnd = Time.GetTicksMsec();
            last = y;
        }

        // Judged: a rise of more than 3.3 m, no frame carrying more than a quarter of it, the rise lasting over 300 ms;
        // a cut would put most of the rise in one frame. A frame that rises more than 2 mm counts as still rising.
        var rise = last - y0;
        Check(App.Locomotion == Locomotion.Flying && rise > 3.3f && maxStep < 0.25f * rise && riseEnd - riseStart > 300 && App.Hud.FlyChipVisible && App.Hud.FlightButtonPressed,
            F($"T takes off: a continuous rise of {rise:0.0} m over {riseEnd - riseStart} ms (largest frame step {maxStep:0.00} m), chip and pressed button"));
        await Hold(1.5, Godot.Key.E);
        await Hold(1.0, Godot.Key.W);
        await Shot("flying");
        await Key(Godot.Key.T);
        await Until(() => App.Locomotion == Locomotion.Walking && !App.LandingInProgress, 5);
        await Seconds(0.3);
        Check(App.Locomotion == Locomotion.Walking && W.Grounded && MathF.Abs(EyeAboveSupport - EyeMetres) < 0.05f && Place.Terrain.IsClear(FeetXz, W.Params.Radius.Value, W.Support.Value, W.Params.BodyHeight.Value),
            F($"T lands on a valid surface below, not inside anything (support {W.Support.Value:0.00})"));
        await Shot("landed");

        var vista = Place.Vistas.Count > 0 ? Place.Vistas[0] : null;
        if (vista != null)
        {
            var reloc = App.Relocations;
            App.ProbeFlyAt(new Vector3(vista.Center.X, 12, vista.Center.Z)); // [direct] above a vista, outside the walkable place
            await Frames(3);
            Log(F($"before T: locomotion={App.Locomotion} landing={App.LandingInProgress} guided={App.GuidedGlide} transition={App.InTransition} aerial={App.AerialShown} altitude={App.CameraAltitude:0.0}"));
            await Key(Godot.Key.T);
            await Frames(2);
            Log(F($"after T: locomotion={App.Locomotion} landing={App.LandingInProgress} guided={App.GuidedGlide} transition={App.InTransition} note={App.LastLandingNote ?? "-"}"));
            await WaitGuide();
            await Seconds(0.3);
            // Since the aerial world (AERIAL-03/FLY-05): over open space you may instead land in the nearest place (it says so).
            await Until(() => !App.InTransition && !App.GuidedGlide && !App.LandingInProgress && App.Locomotion == Locomotion.Walking, 10);
            await Seconds(0.3);
            var atArrival = App.Relocations == reloc + 1 && (App.LastLandingNote ?? "").Contains("arrival", StringComparison.Ordinal) && MathF.Abs(App.Rig.CameraPosition.DistanceTo(ToGd(Place.Arrival.Eye))) < 0.2f;
            var inNearest = (App.LastLandingNote ?? "").StartsWith("Landing in", StringComparison.Ordinal) && W.Grounded;
            Check(App.Locomotion == Locomotion.Walking && (atArrival || inNearest),
                $"landing above a vista lands you safely and says where: {App.LastLandingNote}");
            if (inNearest) { await Key(Godot.Key.Backspace); await WaitPlace(); }
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

    /// <summary>
    /// CTRL-01/02: mouse-look direction with real injected input. The default reverses both axes relative to the
    /// previous Places behaviour; the Settings checkboxes (clicked, not set directly) switch each axis independently,
    /// for the right-mouse look and the left-drag look alike; the arrow keys keep turning the way they point.
    /// </summary>
    private async Task MouseDirection()
    {
        Log(F($"preferences at start: {App.Preferences.Describe()}; file: {App.PreferencesPath ?? "none (probe run: never read or saved)"}; UI scale {App.GetWindow().ContentScaleFactor:0.00}"));
        Check(App.PreferencesPath == null, "a probe run neither reads nor saves the user's preferences");
        Check(App.Preferences.ReverseLookHorizontal && App.Preferences.ReverseLookVertical, "default: both look axes reversed (DJ's request)");
        var k = CameraRig.LookDegreesPerPixel;

        async Task<(float Yaw, float Pitch)> Delta(Func<Task> act)
        {
            App.ProbeSetGaze(Degrees.Zero, Degrees.Zero); // [direct] a level start
            await Frames(2);
            var y = W.Yaw.Value;
            var p = W.LookPitch.Value;
            await act();
            return (W.Yaw.Value - y, _pitchAtRelease - p); // pitch while still held: on release it levels (checked below)
        }

        // A look of N px must turn N × LookDegreesPerPixel within 1°, in the direction the setting says.
        var right = await Delta(() => Look(new Vector2(300, 0)));
        var down = await Delta(() => Look(new Vector2(0, 120)));
        Check(MathF.Abs(right.Yaw - 300 * k) < 1f && MathF.Abs(right.Pitch) < 0.01f, F($"reversed (default), right mouse held: moving right turns left (yaw {right.Yaw:+0.0;-0.0}°, positive = left)"));
        Check(MathF.Abs(down.Pitch - 120 * k) < 1f && MathF.Abs(down.Yaw) < 0.01f, F($"reversed (default): moving down looks up (pitch {down.Pitch:+0.0;-0.0}°)"));
        Check(!App.PlaceLookCaptured && Input.MouseMode == Input.MouseModeEnum.Visible, "releasing the right button releases the cursor");
        var drag = await Delta(() => DragLeft(new Vector2(200, 100)));
        Check(drag.Yaw > 0 && drag.Pitch > 0, F($"reversed (default), left-drag: right turns left, down looks up (yaw {drag.Yaw:+0.0;-0.0}°, pitch {drag.Pitch:+0.0;-0.0}°)"));

        // Settings: F4, then click each checkbox (real clicks on the panel).
        await Key(Godot.Key.F4);
        await Frames(3);
        Check(App.Hud.SettingsVisible, "F4 opens Settings");
        await Shot("settings-panel");
        await Click(App.Hud.ReverseHorizontalRect.GetCenter());
        await Frames(2);
        Check(!App.Preferences.ReverseLookHorizontal && App.Preferences.ReverseLookVertical, "clicking “Reverse left / right” turns only that off");
        await Key(Godot.Key.Escape);
        await Frames(2);
        Check(!App.Hud.SettingsVisible, "Esc closes Settings");
        right = await Delta(() => Look(new Vector2(300, 0)));
        down = await Delta(() => Look(new Vector2(0, 120)));
        Check(right.Yaw < -300 * k + 1f && down.Pitch > 0, F($"horizontal usual, vertical still reversed: right turns right ({right.Yaw:+0.0;-0.0}°), down looks up ({down.Pitch:+0.0;-0.0}°)"));
        await Key(Godot.Key.F4);
        await Frames(3);
        await Click(App.Hud.ReverseVerticalRect.GetCenter());
        await Frames(2);
        await Key(Godot.Key.Escape);
        await Frames(2);
        down = await Delta(() => Look(new Vector2(0, 120)));
        drag = await Delta(() => DragLeft(new Vector2(200, 100)));
        Check(!App.Preferences.ReverseLookVertical && down.Pitch < -120 * k + 1f && drag.Yaw < 0 && drag.Pitch < 0, F($"both usual: down looks down ({down.Pitch:+0.0;-0.0}°); left-drag follows too (yaw {drag.Yaw:+0.0;-0.0}°, pitch {drag.Pitch:+0.0;-0.0}°)"));

        // The arrow keys are not affected (they turn the way they point), whatever the mouse setting.
        var arrow = await Delta(() => Hold(0.5, Godot.Key.Right));
        Check(arrow.Yaw < -30f, F($"→ turns right with the mouse usual ({arrow.Yaw:+0;-0}°)"));

        // Back to the defaults by clicking both again.
        await Key(Godot.Key.F4);
        await Frames(3);
        await Click(App.Hud.ReverseHorizontalRect.GetCenter());
        await Frames(2);
        await Click(App.Hud.ReverseVerticalRect.GetCenter());
        await Frames(2);
        await Key(Godot.Key.Escape);
        await Frames(2);
        arrow = await Delta(() => Hold(0.5, Godot.Key.Right));
        Check(App.Preferences.ReverseLookHorizontal && App.Preferences.ReverseLookVertical && App.Hud.ReverseHorizontalChecked && arrow.Yaw < -30f, F($"both reversed again; → still turns right ({arrow.Yaw:+0;-0}°)"));
        Check(App.PreferencesPath == null, "…and nothing was written");
        await LevelOnRelease();
    }

    /// <summary>CTRL-04: letting go after looking up or down eases the view back to level; the heading is kept; Settings can turn it off.</summary>
    private async Task LevelOnRelease()
    {
        Check(App.Preferences.LevelViewOnRelease, "default: the view levels when you let go");

        async Task<(float Held, float Soon, float Later, float YawHeld, float YawLater, bool Monotone, double Seconds)> LookAndLetGo(Func<Task> look)
        {
            App.ProbeSetGaze(Degrees.Zero, Degrees.Zero); // [direct] a level start
            await Frames(2);
            await look(); // releases, then waits 3 frames
            var soon = W.LookPitch.Value;
            var yawHeld = W.Yaw.Value;
            var t0 = Time.GetTicksUsec();
            var previous = MathF.Abs(soon);
            var monotone = true;
            while (App.Leveling && Time.GetTicksUsec() - t0 < 2_000_000)
            {
                await Frames(1);
                monotone &= MathF.Abs(W.LookPitch.Value) <= previous + 1e-3f;
                previous = MathF.Abs(W.LookPitch.Value);
            }

            var seconds = (Time.GetTicksUsec() - t0) / 1e6;
            await Seconds(0.3);
            return (_pitchAtRelease, soon, W.LookPitch.Value, yawHeld, W.Yaw.Value, monotone, seconds);
        }

        // Levelling must finish within 0.6 s of release, never overshoot level, and keep the heading; levelling is
        // watched for at most 2 s.
        var up = await LookAndLetGo(() => Look(new Vector2(150, -250)));
        Check(MathF.Abs(up.Held) > 20f && MathF.Abs(up.Soon) > 1f && MathF.Abs(up.Later) < 0.01f && up.Monotone && up.Seconds < 0.6,
            F($"right mouse: pitch {up.Held:+0;-0}° while held eases to level after release ({up.Soon:+0.0;-0.0}° three frames later, level within {up.Seconds + 0.05:0.00} s, never past level)"));
        Check(MathF.Abs(up.YawLater - up.YawHeld) < 0.01f && MathF.Abs(up.YawHeld) > 10f, F($"…and the heading turned to is kept ({up.YawHeld:+0;-0}°)"));
        var drag = await LookAndLetGo(() => DragLeft(new Vector2(0, 200)));
        Check(MathF.Abs(drag.Held) > 20f && MathF.Abs(drag.Later) < 0.01f && drag.Monotone, F($"left-drag: pitch {drag.Held:+0;-0}° levels after release too"));

        // Looking again while it levels takes over at once.
        App.ProbeSetGaze(Degrees.Zero, Degrees.Zero);
        await Look(new Vector2(0, -250));
        Check(App.Leveling, "levelling has started");
        await Look(new Vector2(0, -100), steps: 5);
        Check(MathF.Abs(_pitchAtRelease) > 10f, F($"a new look during levelling takes over ({_pitchAtRelease:+0;-0}° while held)"));
        for (var i = 0; i < 60 && App.Leveling; i++) await Frames(1);

        // Off in Settings (a real click): the view stays where you left it.
        await Key(Godot.Key.F4);
        await Frames(3);
        await Click(App.Hud.LevelViewRect.GetCenter());
        await Frames(2);
        await Key(Godot.Key.Escape);
        await Frames(2);
        var off = await LookAndLetGo(() => Look(new Vector2(0, -250)));
        Check(!App.Preferences.LevelViewOnRelease && MathF.Abs(off.Later - off.Held) < 0.01f && MathF.Abs(off.Held) > 20f, F($"levelling off (checkbox clicked): the view stays at {off.Later:+0;-0}°"));
        await Key(Godot.Key.F4);
        await Frames(3);
        await Click(App.Hud.LevelViewRect.GetCenter());
        await Frames(2);
        await Key(Godot.Key.Escape);
        await Frames(2);
        Check(App.Preferences.LevelViewOnRelease && App.PreferencesPath == null, "levelling back on; nothing was written");
        App.ProbeSetGaze(W.Yaw, Degrees.Zero);
    }

    /// <summary>A left-button drag in the scene by <paramref name="total"/> physical pixels (positions included, so it counts as a drag).</summary>
    private async Task DragLeft(Vector2 total, int steps = 20)
    {
        var centre = App.GetViewport().GetVisibleRect().Size / 2;
        var at = ToWindow(centre);
        Mouse(MouseButton.Left, true, at);
        await Frames(2);
        for (var i = 0; i < steps; i++)
        {
            var d = total / steps;
            at += d;
            Inject(new InputEventMouseMotion { Relative = d, ScreenRelative = d, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
            await Frames(1);
        }

        _pitchAtRelease = W.LookPitch.Value;
        Mouse(MouseButton.Left, false, at);
        await Frames(3);
    }

    /// <summary>
    /// PLACE-17: from the approach pose (≈ 9 m in front of a gate) walking through it, the gate is named on screen every
    /// frame — by its eye-level plate or the gate chip — and the plate is not under the HUD.
    /// </summary>
    private async Task ApproachAndEnter(int gateId, string name)
    {
        await Seconds(0.35); // signs and the chip refresh every 0.2 s
        var plate = App.PlaceWorld.GatePlates[gateId];
        var rect = Places.PlaceWorld.VisibleRectOf(plate, App.Rig.Camera);
        var vp = App.GetViewport().GetVisibleRect();
        var hud = App.Hud.BlockingRects.ToList();
        var gate = Place.GateFor(gateId)!;
        var dist0 = new NVector2(gate.Position.X - App.Rig.CameraPosition.X, gate.Position.Z - App.Rig.CameraPosition.Z).Length();
        Check(rect is { } r && vp.Encloses(r) && !hud.Any(h => h.Intersects(r)), F($"at {dist0:0.0} m the {name} gate's name plate is in view at eye level, clear of the HUD ({rect})"));
        Check(App.Hud.GateChipText.StartsWith($"Ahead: {name}", StringComparison.Ordinal), $"the gate chip names it: {App.Hud.GateChipText}");
        await Shot($"approaching-portal-{name}");
        var before = App.CompletedTransitions;
        var samples = 0;
        var named = 0;
        var byPlate = 0;
        var closeShot = false;
        var start = Time.GetTicksMsec();
        Key1(Godot.Key.W, true);
        while (!App.InTransition && (Time.GetTicksMsec() - start) < 6000) // 6 s for the ≈ 9 m approach: a generous bound
        {
            await Frames(1);
            if (App.InTransition) break;
            samples++;
            var plateOk = Places.PlaceWorld.VisibleRectOf(plate, App.Rig.Camera) is { } pr && vp.Encloses(pr);
            var chipOk = App.Hud.GateChipText.Contains(name, StringComparison.Ordinal);
            if (plateOk) byPlate++;
            if (plateOk || chipOk) named++;
            var d = new NVector2(gate.Position.X - App.Rig.CameraPosition.X, gate.Position.Z - App.Rig.CameraPosition.Z).Length();
            if (!closeShot && d < 3.5f) // one close-up of the gate at 3.5 m
            {
                closeShot = true;
                Key1(Godot.Key.W, false); // pause for the close-up, then walk on
                await Frames(8);
                await Shot($"portal-close-{name}");
                Log(F($"at {d:0.0} m: plate {(plateOk ? "in view" : "out of view")}, chip “{App.Hud.GateChipText}”"));
                Key1(Godot.Key.W, true);
            }
        }

        Key1(Godot.Key.W, false);
        await WaitPlace();
        Check(App.CompletedTransitions > before && App.Navigator.Current == gateId, $"walking through the {name} gate enters {name}");
        Check(samples > 10 && named == samples, F($"walking from {dist0:0} m through the gate, {name} was named on screen in {named}/{samples} frames (plate in view in {byPlate})"));
    }

    /// <summary>
    /// PLACE-16: down to Pier by G, then U, then only W held: each Up arrives facing the next ▲ gate, so walking on goes
    /// up again without turning. W is released as soon as the arrival root is reached (never above it: nothing beyond
    /// the fixture is listed). Back (history) then returns to Journeys.
    /// </summary>
    private async Task UpwardChain(int root)
    {
        var journeys = Find(root, "Journeys");
        var coast = Find(journeys, "Coast");
        var harbour = Find(coast, "Harbour");
        Check(journeys >= 0 && coast >= 0 && harbour >= 0, "the fixture has the Journeys › Coast › Harbour › Pier chain");
        foreach (var name in new[] { "Journeys", "Coast", "Harbour", "Pier" })
        {
            var id = Find(App.Navigator.Current, name);
            App.Selection.Select(id); // [direct] select; G is the control
            await Key(Godot.Key.G);
            await WaitPlace();
            if (name == "Journeys")
            {
                // ORD-01: natural order, the same in the gate ring (west → east) and in Destinations.
                var ring = string.Join(", ", Place.Gates.OrderBy(g => g.AngleDeg).Select(g => Tree[g.TargetId].Name));
                var listed = string.Join(", ", App.Hud.DestinationIds().Where(d => d >= 0 && d != Place.ParentGate?.TargetId).Select(d => Tree[d].Name));
                Check(ring == "Coast, Leg 1, Leg 2, Leg 10" && listed == ring, $"natural order west → east and in Destinations: ring “{ring}”, list “{listed}”");
                await Shot("natural-order-journeys");
            }
        }

        var pier = App.Navigator.Current;
        Check(Tree.DisplayName(pier) == "Pier", $"arrived in Pier ({Tree.FullPath(pier)})");
        await Key(Godot.Key.U);
        await WaitPlace();
        var route = App.LastUpwardRoute;
        Check(App.Navigator.Current == harbour && route is { Verified: true }, F($"U arrives in Harbour facing its ▲ gate ({route?.StandOff:0.0} m from the Pier gate, a verified straight route)"));
        await Shot("upward-arrival-harbour");
        var arrivals = new List<(string Name, double Seconds)>();
        var relocations = App.Relocations;
        Key1(Godot.Key.W, true);
        var segmentStart = Time.GetTicksMsec();
        var t0 = segmentStart;
        var last = App.Navigator.Current;
        while (App.Navigator.Current != root && (Time.GetTicksMsec() - t0) < 40_000) // 40 s for three Ups
        {
            await Frames(1);
            if (App.Navigator.Current != last)
            {
                // A transition began: the time from arrival to the next crossing, with W held throughout.
                arrivals.Add((Tree.DisplayName(App.Navigator.Current), (Time.GetTicksMsec() - segmentStart) / 1000.0));
                last = App.Navigator.Current;
                if (last == root) break;
                await Until(() => !App.InTransition, 3);
                segmentStart = Time.GetTicksMsec();
                if (App.LastUpwardRoute is not { Verified: true }) Log($"arrival in {Tree.DisplayName(last)}: route not verified");
                await Frames(2);
                await Shot($"upward-arrival-{Tree.DisplayName(last).ToLowerInvariant()}");
            }
        }

        Key1(Godot.Key.W, false); // released at once on reaching the arrival root: never above it
        var total = (Time.GetTicksMsec() - t0) / 1000.0;
        await WaitPlace();
        Log("upward chain with W held: " + string.Join(", ", arrivals.Select(a => F($"→ {a.Name} after {a.Seconds:0.0} s"))) + F($"; total {total:0.0} s"));
        Check(App.Navigator.Current == root && arrivals.Count == 3 && arrivals.Select(a => a.Name).SequenceEqual(new[] { "Coast", "Journeys", Tree.DisplayName(root) }),
            F($"holding only W from Harbour went up Coast → Journeys → {Tree.DisplayName(root)} ({arrivals.Count} Ups in {total:0.0} s)"));
        Check(App.Relocations == relocations, "only W was held (no turning, no mouse) and no relocation was needed");
        await Shot("upward-chain-end");
        await Key(Godot.Key.Backspace);
        await WaitPlace();
        Check(App.Navigator.Current == journeys, "Backspace (history) returns to Journeys, where the last Up started");
        await KeyCombo(Godot.Key.Home, ctrl: true);
        await WaitPlace();
    }

    /// <summary>Holds keys for a number of frames (the movie writer runs at a fixed frame rate, so time is frames).</summary>
    private async Task HoldFrames(int frames, params Godot.Key[] keys)
    {
        foreach (var k in keys) Key1(k, true);
        await Frames(frames);
        foreach (var k in keys) Key1(k, false);
        await Frames(1);
    }

    /// <summary>A right-mouse look of <paramref name="perFrame"/> pixels a frame for a number of frames (recording timing).</summary>
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
        App.ProbePlaceUser(new PlacePose(new NVector3(start.X, EyeMetres, start.Y), PlaceGeometry.YawFor(PlaceTerrain.PlaceAngle(axis)), Degrees.Of(-4))); // [direct]
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
            var approach = Place.ApproachPose(link, App.PlaceParams);
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
            // The run is a measurement, but it must have measured something: the harness rejects a result with no
            // assertions (2026-11-15: this step had none and failed the release tier).
            Check(s2.Count >= 30, F($"{label}: {s2.Count} frames measured"));
        }

        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        var a = Place.Arrival;
        await Measure("arrival-eye-3.35");
        App.ProbePlaceUser(new PlacePose(new NVector3(0, 1.75f, 9f), Degrees.Zero, Degrees.Of(-4))); // [direct] plaza, facing north
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

    /// <summary>
    /// Frame times for a fixed walk (forward, a curve, a run, a look round, back), vsync off; one CSV row. Measurement
    /// only: left out of the functional tier. Nothing is judged here: the CSV is the evidence.
    /// </summary>
    private async Task PerfWalk(string label)
    {
        if (!ProbeTier.Measure(label, Log)) return;
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

    /// <summary>Logs the walker's state (feet, support, eye, heading, pitch) under a label.</summary>
    private void LogPose(string what)
    {
        var w = App.Walker;
        Log(F($"{what}: locomotion={App.Locomotion} feet=({w.Feet.X:0.00},{w.Feet.Y:0.00},{w.Feet.Z:0.00}) support={w.Support:0.00} eye={App.Rig.CameraPosition.Y:0.00} yaw={w.Yaw:0.0} pitch={w.LookPitch:0.0} grounded={w.Grounded}"));
    }
}
