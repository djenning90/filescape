using FileScape.Core.Places;
using FileScape.Core.Units;
using Godot;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// The exploration pass journey (--automate=walk-explore, and the end of --automate=walk): the unobstructed view and
/// on-demand panels (VIEW-01..04), several destinations named at once (LABEL-01), the higher jump (JUMP-04), flight
/// camera and vertical controls (FLY-01..05), and the aerial world: discovery, travel to another place, landing,
/// history (AERIAL-01..06). Injected input through Godot's pipeline; "[direct]" steps only set up a case.
/// </summary>
public partial class WalkProbe
{
    private async Task Explore()
    {
        var root = App.Navigator.Current;
        _unlistedAtStart = Tree.Nodes.Where(n => n.IsDirectory && Listing.Of(n) == ListingState.Unlisted && n.Id != root).Select(n => n.Id).ToHashSet();
        App.ProbePlaceUser(Place.Arrival); // [direct] the arrival pose
        await Frames(10);

        // ---- 1. The default view is the world (VIEW-01/02)
        Check(!App.Hud.LegendVisible && !App.Hud.DestinationsOpen && !App.Hud.InspectorVisible, "default view: no key, no Destinations drawer, no inspector card");
        var free = WorldVisibleFraction();
        Check(free >= 0.72f, F($"at arrival {free:P0} of the window shows the world (HUD panels cover the rest)"));
        await Shot("explore-arrival-default-view");
        await Key(Godot.Key.K);
        await Frames(3);
        Check(App.Hud.LegendVisible, F($"K shows the key on demand (world visible then: {WorldVisibleFraction():P0})"));
        await Key(Godot.Key.K);
        await Frames(3);
        Check(!App.Hud.LegendVisible, "K hides it again");

        // ---- 2. Several destinations named at once (LABEL-01)
        await Frames(4);
        var gatesInView = App.Signs.InView;
        var named = App.Signs.ShownIds.Count(id => Place.GateFor(id) != null || Place.ParentGate?.TargetId == id);
        Check(named >= Math.Min(8, gatesInView) && named >= gatesInView * 0.75f, F($"from the arrival point {named} of {gatesInView} gates in view are named at once"));
        Check(NoOverlaps(App.Signs.ShownRects) && App.Signs.ShownRects.All(r => !App.Hud.BlockingRects.Any(h => h.Intersects(r))), "no two signs overlap, and none is under a HUD panel");
        var shortened = App.Signs.ShownText.Where(kv => kv.Value.Contains('…')).Select(kv => $"{kv.Value} ({Tree[kv.Key].Name})").ToList();
        Log("signs: " + string.Join(", ", App.Signs.ShownText.Values));
        if (shortened.Count > 0) Log("shortened: " + string.Join("; ", shortened));

        // Destinations on demand, by keyboard: J opens with the filter focused; typing filters; Esc closes, nothing moved.
        var feet = W.Feet;
        await Key(Godot.Key.J);
        await Frames(3);
        Check(App.Hud.DestinationsOpen && App.Hud.DestinationFilter.HasFocus(), "J opens Destinations with its filter focused");
        await TypeText("jour");
        await Frames(3);
        var filtered = App.Hud.DestinationIds().Where(id => id >= 0).Select(id => Tree[id].Name).ToList();
        Check(filtered.Count >= 1 && filtered.All(n => n.Contains("jour", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Up to", StringComparison.Ordinal)), $"typing filters the list ({string.Join(", ", filtered)})");
        await Key(Godot.Key.Escape);
        await Frames(3);
        Check(!App.Hud.DestinationsOpen && NVector3.Distance(feet, W.Feet) < 0.01f, "Esc closes it; typing never moved you");
        App.Hud.DestinationFilter.Text = ""; // [direct] clear for later steps

        // Pick with the mouse after filtering: the keyboard comes back (Esc closes the drawer), the gate is selected.
        await Key(Godot.Key.J);
        await TypeText("shared");
        await Frames(3);
        await Click(App.Hud.DestinationItemRect(0).GetCenter());
        await Frames(10);
        var focusAfterClick = App.GetViewport().GuiGetFocusOwner()?.GetType().Name ?? "none";
        await Key(Godot.Key.Escape);
        await Frames(3);
        var picked = App.Selection.Selected >= 0 ? Tree[App.Selection.Selected].Name : "-";
        Check(!App.Hud.DestinationsOpen && picked == "to-shared-library", $"filter 'shared', click the row, Esc: drawer closed, {picked} selected (focus after the click: {focusAfterClick})");
        App.Hud.DestinationFilter.Text = "";
        await WaitGuide();
        App.ProbePlaceUser(Place.Arrival); // [direct] back to the arrival pose
        App.Selection.Select(-1);
        await Frames(5);

        // ---- 3. Higher jump: a held jump peaks over 1.1 m, a tap is a short hop (JUMP-04, injected Space)
        var apexHeld = await JumpApex(holdSeconds: 0.6);
        var apexTap = await JumpApex(holdSeconds: 0.03);
        Check(apexHeld is > 1.1f and < 1.35f && apexTap < apexHeld - 0.3f, F($"Space held jumps {apexHeld:0.00} m, a tap {apexTap:0.00} m (was 0.83 m)"));

        // ---- 4. Take-off: rise to a downward view once; in flight the pitch you choose stays (FLY-01/04)
        App.ProbePlaceUser(Place.Arrival);
        await Frames(4);
        await Key(Godot.Key.T);
        await Seconds(2.2);
        var alt = App.CameraAltitude;
        Check(App.Locomotion == Locomotion.Flying && alt > 14f && MathF.Abs(W.LookPitch.Value + 35f) < 4f, F($"T rises to {alt:0} m and looks down ({W.LookPitch.Value:0}°)"));
        await Shot("explore-take-off");
        await Look(new Vector2(0, App.Preferences.ReverseLookVertical ? -260 : 260)); // look further down with the real control
        var lookedTo = _pitchAtRelease;
        await Seconds(0.7);
        Check(lookedTo < -45f && MathF.Abs(W.LookPitch.Value - lookedTo) < 0.5f, F($"in flight the view stays at {W.LookPitch.Value:0}° after letting go (on foot it would level)"));

        // ---- 5. Vertical controls at several gazes (FLY-03): E/Space up, Q/C down, W level — whatever the pitch
        foreach (var pitch in new[] { -60f, 0f, 40f })
        {
            App.ProbeSetGaze(W.Yaw, Degrees.Of(pitch)); // [direct]
            var (upY, upXz) = await FlightDelta(Godot.Key.E);
            var (spaceY, _) = await FlightDelta(Godot.Key.Space);
            var (downY, downXz) = await FlightDelta(Godot.Key.Q);
            var (cY, _) = await FlightDelta(Godot.Key.C);
            var (wY, wXz) = await FlightDelta(Godot.Key.W);
            Check(upY > 2f && spaceY > 2f && downY < -2f && cY < -2f && upXz < 0.3f && downXz < 0.3f && MathF.Abs(wY) < 0.3f && wXz > 3f,
                F($"gaze {pitch:+0;-0}°: E {upY:+0.0;-0.0} m, Space {spaceY:+0.0;-0.0}, Q {downY:+0.0;-0.0}, C {cY:+0.0;-0.0} (sideways {upXz:0.00}/{downXz:0.00} m); W {wXz:0.0} m level ({wY:+0.00;-0.00} m)"));
        }

        // ---- 6. The aerial world: B frames this place and its neighbours (AERIAL-01/06)
        App.ProbeSetGaze(W.Yaw, Degrees.Of(-35));
        await Key(Godot.Key.B);
        await Until(() => !App.GuidedGlide, 6);
        await Seconds(0.8);
        var map = App.WorldMap;
        var known = Tree.Nodes.Count(n => n.IsDirectory && (n.Flags & (Core.Model.NodeFlags.Hidden | Core.Model.NodeFlags.System)) == 0);
        Check(App.AerialShown && map != null && App.Aerial.IslandCount == map.Islands.Count && map.Islands.Count >= known && map.Contains(root),
            F($"B: the aerial world shows {App.Aerial.IslandCount} places, every known folder ({known} not hidden) and the way here (layout {App.LastWorldLayoutMs:0.0} ms, build {App.Aerial.LastBuildMs:0.0} ms)"));
        var children = Tree[root].Children.Where(c => Tree[c].IsDirectory && map!.Contains(c)).ToList();
        var namedPlaces = App.Signs.ShownIds.Count(id => children.Contains(id));
        Check(namedPlaces >= Math.Min(5, children.Count), F($"from above, {namedPlaces} of {children.Count} child places are named"));
        Check(App.Hud.AscentChipVisible, "a chip says what the aerial view shows and how to land or go");
        await Shot("explore-aerial-neighbourhood");

        // Discovery: unlisted places nearby inside the arrival folder are listed from the air (AERIAL-04).
        var before = App.RevealsRequested;
        await Seconds(2.5);
        var revealed = App.RevealsRequested - before;
        var stillUnlisted = map!.Islands.Keys.Count(id => MayBeRevealed(id, root) && Listing.Of(Tree[id]) == ListingState.Unlisted);
        Log($"reveals requested so far: {App.RevealsRequested} (+{revealed} in 2.5 s); unlisted inside the arrival folder now: {stillUnlisted}");
        Check(App.RevealsRequested > 0 || stillUnlisted == 0, "places near you inside the arrival folder are listed from the air");
        var outside = map.Islands.Keys.Where(id => !MayBeRevealed(id, root) && id != root).ToList();
        Check(outside.All(id => !IsListedByReveal(id)), F($"nothing outside the arrival folder was listed from the air ({outside.Count} places there stay as they were)"));

        // ---- 7. Go to another place from the air: click its island, then G (AERIAL-03)
        var journeys = Find(root, "Journeys");
        await Until(() => App.Aerial.Labels.Any(l => l.Id == journeys), 3);
        // A point on the island that is on screen and not under a HUD panel (as a user would click).
        var island = App.WorldMap!.Islands[journeys];
        var centre = App.Aerial.Frame.ToLocal(island.Center);
        var vp = App.GetViewport().GetVisibleRect();
        var blocked = App.Hud.BlockingRects.ToList();
        var target2 = new Vector2(-1, -1);
        foreach (var (dx, dz) in new[] { (0f, 0f), (0.5f, 0f), (-0.5f, 0f), (0f, 0.5f), (0f, -0.5f), (0.35f, 0.35f), (-0.35f, -0.35f) })
        {
            var p = new Vector3(centre.X + dx * island.Radius, 0.2f, centre.Y + dz * island.Radius);
            if (App.Rig.Camera.IsPositionBehind(p)) continue;
            var sp = App.Rig.Camera.UnprojectPosition(p);
            if (vp.HasPoint(sp) && !blocked.Any(r => r.HasPoint(sp)) && !App.Signs.ShownRects.Any(r => r.HasPoint(sp)))
            {
                target2 = sp;
                break;
            }
        }

        Check(target2.X >= 0, "Journeys' island is in view, clear of the HUD");
        await Click(target2);
        await Frames(3);
        Check(App.Selection.Selected == journeys && App.Navigator.Current == root, "clicking an island selects that folder; nothing moves");
        Check(App.Hud.InspectorVisible && App.Hud.InspectorTitle.Contains("Journeys", StringComparison.Ordinal), $"the inspector card names it ({App.Hud.InspectorTitle}: {App.Hud.InspectorBrief})");
        var history = App.Navigator.History.Count;
        await Key(Godot.Key.G);
        await WaitPlace();
        await Seconds(0.5);
        Check(App.Navigator.Current == journeys && App.Locomotion == Locomotion.Flying && App.CameraAltitude > 30f && App.Navigator.History.Count == history + 1,
            F($"G flies you to Journeys and you arrive above it ({App.CameraAltitude:0} m up, history {history} → {App.Navigator.History.Count})"));
        await Shot("explore-arrived-above-journeys");

        // ---- 8. Fly over a neighbouring place with real keys and land there with T (AERIAL-03, FLY-05)
        map = App.WorldMap!;
        var target = Tree[journeys].Children.Where(c => Tree[c].IsDirectory && map.Contains(c)).OrderBy(c => Tree[c].Name, Core.Interaction.NaturalNameOrder.Instance).FirstOrDefault(-1);
        if (target < 0) target = root;
        var frame = map.FrameOf(journeys);
        var goal = frame.ToLocal(map.Islands[target].Center);
        var flew = 0.0;
        for (var i = 0; i < 400; i++)
        {
            var c = App.Rig.CameraPosition;
            var to = new NVector2(goal.X - c.X, goal.Y - c.Z);
            if (to.Length() < map.Islands[target].Radius * 0.5f) break;
            App.ProbeSetGaze(PlaceGeometry.YawFor(PlaceTerrain.PlaceAngle(NVector2.Normalize(to))), W.LookPitch); // [direct] steer
            Key1(Godot.Key.W, true);
            await Frames(1);
            flew += 1 / 60.0;
        }

        Key1(Godot.Key.W, false);
        await Frames(2);
        Log(F($"flew to {Tree[target].Name} in about {flew:0.0} s at {App.FlightAltitudeFactor:0.0}× speed"));
        await Key(Godot.Key.T);
        await Until(() => App.Navigator.Current == target && !App.InTransition && App.Locomotion == Locomotion.Walking && !App.LandingInProgress && !App.GuidedGlide, 12);
        await Seconds(0.8);
        Check(App.Navigator.Current == target && App.Locomotion == Locomotion.Walking && W.Grounded, $"T over {Tree[target].Name} lands you in it, on foot ({App.LastLandingNote ?? "-"})");
        Check(MathF.Abs(W.LookPitch.Value) < 3f, F($"on foot again the view is level ({W.LookPitch.Value:0.0}°)"));
        await Shot("explore-landed");

        // History and identity: Back returns to Journeys; the breadcrumb is the physical path (AERIAL-03).
        await Key(Godot.Key.Backspace);
        await WaitPlace();
        Check(App.Navigator.Current == journeys && App.Hud.BreadcrumbNames.LastOrDefault() == "Journeys", "Backspace returns to Journeys (history kept across aerial travel)");

        // ---- 9. Safe landing over open space: land in the nearest place (AERIAL-03, WALK-07)
        await Key(Godot.Key.T);
        await Seconds(1.8);
        var far = App.Rig.CameraPosition + new Vector3(0, 60, 0) + App.Rig.Camera.GlobalTransform.Basis.Z * 900f;
        App.ProbeFlyAt(far); // [direct] far out over nothing
        await Frames(6);
        await Key(Godot.Key.T);
        await Until(() => !App.InTransition && App.Locomotion == Locomotion.Walking && !App.LandingInProgress && !App.GuidedGlide, 15);
        await Seconds(0.8);
        Check(App.Locomotion == Locomotion.Walking && W.Grounded && App.Place != null, $"T far from every place still lands you safely, in {Tree.DisplayName(App.Navigator.Current)} ({App.LastLandingNote ?? "-"})");

        // ---- 10. Performance while the aerial world is shown and you fly across it
        await Key(Godot.Key.T);
        await Seconds(1.8);
        await Key(Godot.Key.B);
        await Until(() => !App.GuidedGlide, 6);
        await PerfFly("aerial-flight");
        await Key(Godot.Key.T);
        await Until(() => App.Locomotion == Locomotion.Walking && !App.LandingInProgress && !App.GuidedGlide && !App.InTransition, 15);
        App.ProbeSetGaze(W.Yaw, Degrees.Zero);
    }

    /// <summary>The share of the window not covered by HUD panels (a 40 × 24 sample grid).</summary>
    private float WorldVisibleFraction()
    {
        var vp = App.GetViewport().GetVisibleRect().Size;
        var rects = App.Hud.BlockingRects.ToList();
        var free = 0;
        const int nx = 40, ny = 24;
        for (var i = 0; i < nx; i++)
        {
            for (var j = 0; j < ny; j++)
            {
                var p = new Vector2((i + 0.5f) * vp.X / nx, (j + 0.5f) * vp.Y / ny);
                if (!rects.Any(r => r.HasPoint(p))) free++;
            }
        }

        return free / (float)(nx * ny);
    }

    private static bool NoOverlaps(IReadOnlyList<Rect2> rects)
    {
        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                if (rects[i].Intersects(rects[j])) return false;
            }
        }

        return true;
    }

    /// <summary>Jumps in place with Space held for <paramref name="holdSeconds"/>; returns the apex above the start (metres).</summary>
    private async Task<float> JumpApex(double holdSeconds)
    {
        App.ProbePlaceUser(new PlacePose(new NVector3(0, 1.75f, Place.Arrival.Eye.Z - 12f), Degrees.Zero, Degrees.Zero)); // [direct] flat plaza
        await Frames(6);
        var y0 = W.Feet.Y;
        var apex = 0f;
        Key1(Godot.Key.Space, true);
        var start = Time.GetTicksMsec();
        var released = false;
        for (var i = 0; i < 120; i++)
        {
            if (!released && (Time.GetTicksMsec() - start) / 1000.0 >= holdSeconds)
            {
                Key1(Godot.Key.Space, false);
                released = true;
            }

            await Frames(1);
            apex = MathF.Max(apex, W.Feet.Y - y0);
            if (i > 10 && W.Grounded) break;
        }

        if (!released) Key1(Godot.Key.Space, false);
        await Frames(4);
        return apex;
    }

    /// <summary>Holds a key for 0.5 s in flight; returns the change in height and the horizontal distance moved.</summary>
    private async Task<(float Dy, float Dxz)> FlightDelta(Godot.Key key)
    {
        var a = App.Rig.CameraPosition;
        await Hold(0.5, key);
        await Seconds(0.25); // flight velocity eases out (≈ 70 ms time constant)
        var b = App.Rig.CameraPosition;
        return (b.Y - a.Y, new Vector2(b.X - a.X, b.Z - a.Z).Length());
    }

    private bool MayBeRevealed(int id, int root)
    {
        foreach (var a in Tree.Ancestors(id)) if (a == root || App.VisitedPlaces.Contains(a)) return true;
        return false;
    }

    /// <summary>Folders outside the allowed area must not be listed by the probe's flight: they were stubs before and still are.</summary>
    private bool IsListedByReveal(int id) => Tree[id].ScanState == Core.Model.DirScanState.Complete && !App.VisitedPlaces.Contains(id) && _unlistedAtStart.Contains(id);

    private HashSet<int> _unlistedAtStart = [];

    private async Task PerfFly(string label)
    {
        await Seconds(0.5);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        App.Perf.StartRecording();
        await Hold(1.5, Godot.Key.W);
        await Hold(1.0, Godot.Key.W, Godot.Key.Left);
        await Hold(0.8, Godot.Key.E);
        await Look(new Vector2(500, 0), 60);
        await Hold(1.0, Godot.Key.S, Godot.Key.Q);
        var samples = App.Perf.StopRecording();
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
        var s2 = PerfRecorder.Summarize(samples.Select(x => x.FrameMs).ToList());
        var g = PerfRecorder.Summarize(samples.Select(x => x.GpuMs).ToList());
        _perf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{label},{App.Quality},{s2.Count},{s2.Mean:0.00},{s2.P50:0.00},{s2.P95:0.00},{s2.P99:0.00},{s2.Max:0.00},{g.Mean:0.00},{App.InstanceCount + App.Aerial.InstanceCount},{Tree.Count}\n");
        Log(F($"perf {label} {App.Quality}: frames={s2.Count} mean={s2.Mean:0.00} p95={s2.P95:0.00} p99={s2.P99:0.00} max={s2.Max:0.00} gpu={g.Mean:0.00} aerial instances={App.Aerial.InstanceCount}"));
    }
}
