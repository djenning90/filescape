using FileScape.App.Places;
using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Scanning;
using FileScape.Core.Units;
using Godot;
using GVector3 = Godot.Vector3;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>The experiences over one model (AD-15). Planets (2026-11-11, AD-39) is the planetary world on the same session.</summary>
public enum Experience
{
    Places,
    Classic,
    District,
    Planets,
}

/// <summary>
/// The Places experience controller (PLACE-01..15, AD-14). The current folder lives in <see cref="PlaceNavigator"/>,
/// apart from the selection. The world shows one composed place at a time; travel recomposes it behind a short
/// threshold transition. While in Places the whole-tree layout is not computed (PLACE-12).
/// <para>
/// This part also switches between experiences (<see cref="SetExperience"/>, AD-15). Travel is driven by the
/// navigator: anything that changes the current folder (a gate crossing, Backspace, a breadcrumb, a link, the planets)
/// raises <see cref="PlaceNavigator.Changed"/>, and <see cref="OnPlaceChanged"/> turns that into a transition here.
/// Composition is <see cref="PlaceComposer"/> (Core); drawing is <see cref="PlaceWorld"/>; walking is in
/// <c>Main.Walk.cs</c>.
/// </para>
/// </summary>
public partial class Main
{
    /// <summary>A place is recomposed at most this often while its neighbourhood changes (PLACE-12).</summary>
    private static readonly TimeSpan RecomposeInterval = TimeSpan.FromSeconds(0.35);

    /// <summary>World signs are re-ranked this often (sorting every sign each frame would be wasteful in wide folders).</summary>
    private static readonly TimeSpan SignRefreshInterval = TimeSpan.FromSeconds(0.2);

    /// <summary>
    /// The camera rig's pivot (its target) in Places, metres ahead of the eye. Places set the camera by its eye pose; the
    /// pivot is where the rig's target sits along the gaze (the value 12 m: rationale not recorded).
    /// </summary>
    private const float PivotAhead = 12f;

    /// <summary>
    /// The threshold transition: glide out and fade (old place), then glide in (new place) (PLACE-07). 0.30 + 0.45 s
    /// (was 0.42 + 0.62 s before 2026-11-05 evening): long enough to read as a passage, short enough not to stall
    /// repeated travel. Movement keys held through it keep working on arrival.
    /// </summary>
    private static readonly TimeSpan TransitionOut = TimeSpan.FromSeconds(0.30), TransitionIn = TimeSpan.FromSeconds(0.45);

    /// <summary>The glide out moves the eye this far along the way out, metres (through the gate, or up for a jump).</summary>
    private const float TransitionExitMetres = 10f;

    /// <summary>
    /// The glide in starts this far behind the arrival eye and this much above it, metres, so the new place is entered
    /// with a short forward-and-down move.
    /// </summary>
    private const float ApproachBackMetres = 9f, ApproachRiseMetres = 2.5f;

    private readonly PlaceNavigator _nav = new();
    private readonly ThresholdTracker _threshold = new();
    private PlaceParams _placeParams = PlaceParams.Default;

    /// <summary>The place layout in use: compact by default, <c>--layout=spacious</c> for the previous one (PLACE-14, KD-28).</summary>
    public PlaceParams PlaceParams => _placeParams;
    private PlaceWorld _placeWorld = null!;
    private PlaceComposition? _place;
    private Experience _experience = Experience.Places;
    private TimeSpan _lastComposeAt = TimeSpan.FromSeconds(-10);
    private PlaceTransition? _transition;
    private Label3D _hereLabel = null!;
    private MeshInstance3D _hereRing = null!;
    /// <summary>The app's wall clock for this frame: refresh throttles and diagnostics. Gate cooldowns and the upward settle use the game clock (AD-91).</summary>
    private TimeSpan _now, _lastSignUpdate;
    private int _transitions;
    private int _lastHudKey;
    private bool _mapNoteShown;

    public PlaceNavigator Navigator => _nav;
    public PlaceComposition? Place => _place;
    public PlaceWorld PlaceWorld => _placeWorld;
    public Experience Experience => _experience;
    public bool InTransition => _transition != null;
    public int CompletedTransitions => _transitions;
    public bool HereMarkerVisible => _hereLabel.Visible;
    public string HereMarkerText => _hereLabel.Text;
    public double LastComposeMs { get; private set; }
    public double LastPlaceBuildMs { get; private set; }

    /// <summary>The route chosen on the last arrival by going up (probes report it).</summary>
    public UpwardRoute? LastUpwardRoute { get; private set; }

    /// <summary>
    /// One threshold transition in progress (PLACE-07): the change, the pose it left from and the way out (a gate's
    /// outward direction, or up for a jump); after the midpoint (<see cref="Arrived"/>) the new place's arrival pose and
    /// the eye the glide in starts from.
    /// </summary>
    private sealed class PlaceTransition
    {
        public required PlaceChange Change;
        public required GVector3 FromEye;
        public required GVector3 ExitDirection;
        public required Degrees FromYaw, FromPitch;
        public required string Caption;
        public TimeSpan Elapsed;
        public bool Arrived;
        public PlacePose Arrival;
        public GVector3 ApproachEye;
    }

    private void BuildPlaceScene()
    {
        _placeWorld = new PlaceWorld { Visible = false };
        AddChild(_placeWorld);
        _nav.Changed += OnPlaceChanged;

        _hereRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 1.0f, OuterRadius = 1.12f, Rings = 64 },
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(2.4f, 2.1f, 0.8f) },
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_hereRing);
        _hereLabel = new Label3D
        {
            Text = "You are here",
            FixedSize = true,
            PixelSize = 0.0011f,
            FontSize = 30,
            OutlineSize = 10,
            Modulate = new Color(1f, 0.92f, 0.55f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Visible = false,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        AddChild(_hereLabel);

        _hud.BreadcrumbRequested += id => { if (_tree != null) _nav.GoTo(_tree, id); };
        _hud.UpRequested += () => TravelUp();
        _hud.RootRequested += () => TravelRoot();
        _hud.DestinationSelected += ApproachDestination;
        _hud.DestinationActivated += id => TravelInto(id);
        _hud.EnterRequested += EnterSelection;
        _hud.LocationChosen += OnLocationChosen;
        _hud.PathSubmitted += OnPathSubmitted;
        _hud.LocationsRequested += ShowLocations;
        _hud.HiddenToggled += ToggleHiddenVisibility;
        _hud.RetryRequested += RetrySelection;
        _hud.OrdinaryOverrideToggled += ToggleOrdinaryOverride;
        _hud.ProblemChosen += ApproachDestination;
        _hud.StartFreshRequested += StartFreshHere;
        _hud.ScanTreeRequested += ScanHereAsTree;
        _hud.ScanPathAsTreeRequested += OnScanPathAsTree;
    }

    // ------------------------------------------------------------------ experience switching (AD-15)

    public void SetExperience(Experience e)
    {
        if (e == _experience) return;
        ExperienceChangeCount++;
        if (e == Experience.Planets && (_tree == null || !_tree.Source.IsLive))
        {
            // From the synthetic sample (the default start), the planets open on the curated Starter Sky: real files, so
            // looking closer and opening work (PLANET-15). A live folder is kept as it is.
            if (TryStarterSky() is { } sky)
            {
                _hud.Toast("Opening the Starter Sky: a few sample worlds of real files FileScape keeps in its own folder. Open… (Ctrl+O) explores your own.", null, 7);
                StartLive(sky);
            }
        }

        var from = _experience;
        _experience = e;
        if (e is Experience.Classic or Experience.District) _visIndex = e == Experience.Classic ? 0 : 1;
        ApplyExperience(frame: true, from);
        if (e is Experience.Classic or Experience.District && _explorer != null && !_mapNoteShown)
        {
            _mapNoteShown = true;
            _hud.Toast("This map shows the explored world (what you have visited). For the whole tree of one folder: Locations (L) › Scan this folder as a tree.", null, 8);
        }
    }

    /// <summary>Map index (0 Classic, 1 District) → that map experience. Kept for the reference-view scripts.</summary>
    public void SetVisualization(int index) => SetExperience(index == 0 ? Experience.Classic : Experience.District);

    public void ToggleMap() => SetExperience(_experience is Experience.Places or Experience.Planets ? (_visIndex == 0 ? Experience.Classic : Experience.District) : Experience.Places);

    /// <summary>V: between the two walking worlds (the flat Places and the Planets), keeping the folder, selection and history.</summary>
    public void ToggleWorld() => SetExperience(_experience == Experience.Planets ? Experience.Places : Experience.Planets);

    private void ApplyExperience(bool frame, Experience? from = null)
    {
        var planets = _experience == Experience.Planets;
        var places = _experience == Experience.Places;
        for (var i = 0; i < _visualizations.Length; i++)
        {
            _visualizations[i].Root.Visible = !places && !planets && i == _visIndex;
        }

        _placeWorld.Visible = places;
        _ground.Visible = !planets;
        _rig.Camera.Current = !planets;
        if (!planets) _planets.Deactivate();
        _hereRing.Visible = false;
        _hereLabel.Visible = false;
        _beam.Visible = false;
        _spot.Visible = false;
        // PLANET-136: the selection and hover boxes belong to Places and the maps, which place them every frame; Planets
        // never does, so a box left drawn from the last legacy frame stood in the planet world as a pulsing pale-gold
        // cube nothing answered to (DJ, Saved Pictures, 2026-11-20). The view being entered draws its own again.
        _selectionBox.Visible = false;
        _hoverBox.Visible = false;
        _labels.HideAll();
        _signs.Clear();
        _aerialShown = false;
        _aerial.Visible = false;
        _placeWorld.SetVistasVisible(true);
        EndFly();
        _transition = null;
        _hud.SetTransition(0, null);

        EndPlaceLook();
        _locomotion = Locomotion.Walking;
        _landing = null;
        _guided = false;
        UpdateFlightHud();
        if (planets)
        {
            _hud.SetLegend("Planets: each folder is a small world; its folders hang in the sky above their launchers; files stand in groves by kind", PlanetLegend(), "Object size follows log10 of the file's logical size. Exact bytes are in the card.", markers: false);
        }
        else if (places)
        {
            PlaceAtmosphere.Configure(_world, _sun, _ground, _quality);
            ApplyQualityToViewport();
            _hud.SetLegend("Places: colour = file type (from extension) · gates glow in their folder's colour", PlaceLegend(),
                $"Tower height = log10 of logical size: each ×10 in size adds {SizeScale.Place.PerDecade * SizeScale.Place.Multiplier:0.00} u; zero-byte files are thin tiles; capped at {SizeScale.Place.Cap:0} u. Exact bytes are in the inspector. A gate glows in its folder's own colour unless its listing is unknown.", markers: false);
        }
        else
        {
            var vis = ActiveVisualization;
            vis.ConfigureEnvironment(_world, _sun, _ground, _quality);
            ApplyQualityToViewport();
            var scale = vis.LayoutParams.Scale;
            _hud.SetLegend(vis.ColorMeaning, vis.Legend,
                $"Height = log10 of logical size: each ×10 in size adds {scale.PerDecade * scale.Multiplier:0.00} u; zero-byte files are thin tiles; capped at {scale.Cap:0} u. Exact bytes are in the inspector.");
            ((ShaderMaterial)_selectionBox.MaterialOverride).SetShaderParameter("color", vis.SelectionColor);
            ((ShaderMaterial)_hoverBox.MaterialOverride).SetShaderParameter("color", vis.HoverColor with { A = 0.7f });
            ((ShaderMaterial)_beam.MaterialOverride).SetShaderParameter("color", vis.SelectionColor with { A = 0.35f });
            _spot.LightColor = vis.SelectionColor;
        }

        _hud.SetExperience((int)_experience);
        _hud.SetQuality(_quality.ToString());
        if (planets)
        {
            _planets.Activate(_world, _sun, frame);
            UpdatePlaceHud();
            UpdateInspector();
            return;
        }

        if (_tree == null || _tree.Count == 0)
        {
            return;
        }

        if (places)
        {
            ((ShaderMaterial)_selectionBox.MaterialOverride).SetShaderParameter("color", new Color(1f, 0.92f, 0.55f));
            ((ShaderMaterial)_hoverBox.MaterialOverride).SetShaderParameter("color", new Color(0.7f, 0.9f, 1f, 0.6f));
            Recompose(rebuildOnly: false);
            if (frame)
            {
                // Coming back from a map: arrive at the current folder, with a short fade so the change of world is clear.
                ArriveAt(_place!.Arrival, snap: true);
                _transition = new PlaceTransition
                {
                    Change = new PlaceChange(_nav.Current, _nav.Current, PlaceMove.Jump),
                    FromEye = _rig.CameraPosition,
                    ExitDirection = GVector3.Zero,
                    FromYaw = _rig.Yaw,
                    FromPitch = _rig.LookPitch,
                    Caption = _tree.DisplayName(_nav.Current),
                    Elapsed = TransitionOut,
                    Arrived = true,
                    Arrival = _place.Arrival,
                    ApproachEye = Gd(_place.Arrival.Eye) + new GVector3(0, 6, 10), // from a map: 6 m above, 10 m south (behind the arrival)
                };
            }

            UpdatePlaceHud();
        }
        else
        {
            Relayout();
            if (frame)
            {
                // From Places the map opens on "You are here"; between maps the selection is re-framed (NAV-06).
                if (from == Experience.Places || _selection.Selected < 0 || !_layout!.Contains(_selection.Selected)) FrameFolderInMap(_nav.Current);
                else FlyToSelection(snap: false);
            }
        }

        UpdateInspector();
    }

    private static IReadOnlyList<(Color, string)> PlaceLegend()
    {
        var list = Palette.Category.Select(kv => (kv.Value, PlaceText.CategoryName(kv.Key))).ToList();
        list.Add((PlacePalette.Pending, "gate: queued to be listed"));
        list.Add((PlacePalette.Listing, "gate: listing now…"));
        list.Add((PlacePalette.NotScanned, "gate: not scanned"));
        list.Add((PlacePalette.Denied, "gate: access denied"));
        list.Add((PlacePalette.Link, "⇢ link: a connection (walk in to follow)"));
        list.Add((PlacePalette.LinkBlocked, "⇢ link that can't be followed"));
        list.Add((PlacePalette.Unlisted, "gate: not listed yet"));
        list.Add((PlacePalette.Unavailable, "gate: unavailable"));
        list.Add((PlacePalette.Failed, "gate: listing failed"));
        list.Add((PlacePalette.Cloud, "gate: cloud, not listed"));
        list.Add((PlacePalette.Up, "▲ way up"));
        return list;
    }

    /// <summary>On a map, frames a folder with its children from 35° above (the map opened from Places: "You are here").</summary>
    private void FrameFolderInMap(int id)
    {
        if (_layout == null || !_layout.Contains(id) || _tree == null) return;
        var box = _layout.NodeBoxes[id];
        foreach (var c in _tree[id].Children)
        {
            if (_layout.Contains(c)) box = box.Union(_layout.NodeBoxes[c]);
        }

        _rig.Frame(new Aabb(Gd(box.Min), Gd(box.Size)), elevation: Degrees.Of(35), padding: 1.25f);
    }

    // ------------------------------------------------------------------ composition

    /// <summary>
    /// Recomposes the current place when its neighbourhood changed (PLACE-12): cheap, local, throttled. The camera stays
    /// where it is; the threshold tracker follows the new gate positions.
    /// </summary>
    private void MaybeRecompose(TimeSpan now)
    {
        if (_tree == null || _tree.Count == 0 || _transition != null || _awaitingArrival)
        {
            return;
        }

        if (_place == null)
        {
            Recompose(rebuildOnly: false);
            ArriveAt(_place!.Arrival, snap: true);
            return;
        }

        if (_place.TreeVersion == _tree.Version || now - _lastComposeAt < RecomposeInterval)
        {
            return;
        }

        _lastComposeAt = now;
        if (PlaceComposer.Signature(_tree, _nav.Current) != _place.Signature)
        {
            Recompose(rebuildOnly: true);
            SettleUpwardArrival();
        }
    }

    /// <summary>
    /// Composes the current folder's place and rebuilds its scene. <paramref name="rebuildOnly"/>: the same place changed
    /// (a listing arrived, the hidden filter or a correction changed) — the threshold tracker follows the moved gates and
    /// the folder is not visited again. Otherwise it is an arrival: the folder is listed if needed and marked visited.
    /// </summary>
    private void Recompose(bool rebuildOnly)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _place = PlaceComposer.Compose(_tree!, _nav.Current, _placeParams, IsVisibleInPlace, KindOf(_nav.Current));
        if (!rebuildOnly) _explorer?.Visit(_nav.Current); // list it now (and its child folders) if not yet listed
        if (!rebuildOnly) NoteVisited(_nav.Current);
        LastComposeMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        _placeWorld.Build(_tree!, _place, _quality);
        UpdateProminentGates();
        LastPlaceBuildMs = sw.Elapsed.TotalMilliseconds;
        InstanceCount = _placeWorld.InstanceCount;
        Perf.MarkRelayout(LastComposeMs + LastPlaceBuildMs);
        if (rebuildOnly) _threshold.Follow(_place, _rig.CameraPosition.ToNumerics());
        else _threshold.Reset(_place, _rig.CameraPosition.ToNumerics(), _gameClock.Now); // game time (AD-91)
        _placeWorld.UpdateSigns(_rig.Camera, _hud.BlockingRects.ToList());
        UpdatePlaceHud();
        UpdateInspector(); // listing states change under the selection too (found: a drive stayed "not listed yet" after failing)
    }

    /// <summary>Puts the user at a pose: instantly (travel, first frame), or by a glide that ends on foot (Home, WALK-08).</summary>
    private void ArriveAt(PlacePose pose, bool snap)
    {
        if (snap) PlaceUserAt(pose);
        else StartGuide(pose, Locomotion.Walking);
        if (_place != null) _threshold.Reset(_place, pose.Eye, _gameClock.Now);
    }

    // ------------------------------------------------------------------ travel (PLACE-06..08)

    /// <summary>Travels into a folder: a child goes through its gate, any other folder jumps there.</summary>
    public bool TravelInto(int id)
    {
        if (_experience == Experience.Planets) return !_planets.Busy && TravelIntoFromPlanets(id);
        if (_tree == null || !_tree.Contains(id) || _transition != null) return false;
        // From the air, another place is reached by flying there (arriving above it), not through a gate (AERIAL-03).
        if (_aerialShown && _tree[id].IsDirectory && AerialTravelTo(id, land: false)) return true;
        if (_place?.ParentGate?.TargetId == id) return TravelUp();
        if (_tree[id].Kind == NodeKind.Link) return FollowLink(id); // deliberate, resolved on demand (LINK-02)

        if (!_tree[id].IsDirectory) return false;
        return _tree[id].ParentId == _nav.Current ? _nav.Enter(_tree, id) : _nav.GoTo(_tree, id);
    }

    /// <summary>▲ / U: up to the parent folder; at the top, a toast says so instead. False when nothing happens.</summary>
    public bool TravelUp()
    {
        if (_tree == null || _transition != null) return false;
        if (!_nav.Up(_tree))
        {
            _hud.Toast(_tree.Root.IsHub ? $"This is {WorldExplorer.HubName}, the top: every drive and distribution is a gate here." : "This is the top of the sample data.");
            return false;
        }

        return true;
    }

    /// <summary>Ctrl+Home: to the arrival root (not during a transition).</summary>
    public bool TravelRoot() => _tree != null && _transition == null && _nav.GoToRoot();

    /// <summary>G: go into the selected folder (Places through its gate; from a map straight into its place).</summary>
    public void EnterSelection()
    {
        if (_tree == null || _selection.Selected < 0) return;
        if (_experience == Experience.Planets)
        {
            _planets.LaunchToSelection(); // the card's Go inside (G): fly there, staying on the planets
            return;
        }

        var id = _selection.Selected;
        if (_experience != Experience.Places)
        {
            var folder = _tree[id].IsDirectory ? id : _tree[id].ParentId;
            if (folder >= 0 && _tree[folder].IsDirectory)
            {
                _nav.GoTo(_tree, folder);
                SetExperience(Experience.Places);
            }

            return;
        }

        if (id == _nav.Current)
        {
            _hud.Toast("You are already here.");
            return;
        }

        if (!_tree[id].IsDirectory && _tree[id].Kind != NodeKind.Link)
        {
            _hud.Toast("Files are not places. Press Enter to open it with Windows, or select a gate.");
            return;
        }

        TravelInto(id);
    }

    /// <summary>
    /// A Destinations row (or a problem) chosen: select it and glide to stand before it — a gate, a file, or the ▲ gate
    /// (stood off in front of it, facing it). The "N hidden" row (id −1) toggles the hidden filter instead.
    /// </summary>
    private void ApproachDestination(int id)
    {
        if (id < 0)
        {
            ToggleHiddenVisibility(); // the "N hidden" row
            return;
        }

        if (_experience == Experience.Planets)
        {
            if (_tree == null || _planets.Busy) return;
            _selection.Select(id);
            _planets.Approach(id); // walk-hop beside it (files) or face its world (folders)
            return;
        }

        if (_tree == null || _place == null || _transition != null) return;
        _selection.Select(id);
        var pose = _place.ParentGate?.TargetId == id
            ? _place.Standing(_place.ParentGate.Position - _place.ParentGate.Outward * _placeParams.ApproachStandOff, 180, Degrees.Of(-3), _placeParams)
            : _place.ApproachPose(id, _placeParams);
        StartGuide(pose, _locomotion);
    }

    /// <summary>
    /// The navigator's current folder changed (any cause). On the planets the world handles the travel; in Places this
    /// starts the threshold transition, leaving through the gate that leads there when there is one. Any landing, glide
    /// or take-off rise in progress is abandoned.
    /// </summary>
    private void OnPlaceChanged(PlaceChange change)
    {
        _landing = null;
        _guided = false;
        _riseRemaining = Meters.Zero;
        if (_tree == null) return;
        if (_experience == Experience.Planets)
        {
            // The planetary world travels by launch (AD-39); the folder is listed now, so it is ready (or listing) on arrival.
            _explorer?.Visit(change.To);
            NoteVisited(change.To);
            _selection.Hovered = -1;
            _planets.OnNavChanged(change);
            UpdatePlaceHud();
            return;
        }

        if (_experience != Experience.Places || _place == null)
        {
            UpdatePlaceHud();
            return;
        }

        // Leave through the gate that leads there when there is one; otherwise rise and fade (jumps, root, back).
        GVector3 exit;
        var gate = change.Move == PlaceMove.Up || _place.ParentGate?.TargetId == change.To ? _place.ParentGate : _place.GateFor(change.To);
        if (gate != null)
        {
            exit = Gd(gate.Outward);
        }
        else
        {
            exit = new GVector3(0, 0.6f, 0); // no gate leads there: rise (0.6 × the glide-out distance, 6 m) and fade
        }

        _selection.Hovered = -1;
        var verb = change.Move switch
        {
            PlaceMove.Up => "Up to",
            PlaceMove.Root => "Back to the root:",
            PlaceMove.Back => "Back to",
            PlaceMove.Enter => "Entering",
            PlaceMove.Link => "Through the link to",
            _ => "Going to",
        };
        _transition = new PlaceTransition
        {
            Change = change,
            FromEye = _rig.CameraPosition,
            ExitDirection = exit,
            FromYaw = _rig.Yaw,
            FromPitch = _rig.LookPitch,
            Caption = $"{verb}  {_tree.DisplayName(change.To)}",
        };
        // Held movement keys are kept (their releases are still seen during the transition), so holding W through a ▲
        // gate keeps walking on arrival, towards the next one (PLACE-16). They used to be cleared, which forced a
        // release and re-press after every gate.
    }

    /// <summary>Advances the threshold transition: glide out and fade (old place), recompose, glide in (new place).</summary>
    private void UpdateTransition(double delta)
    {
        var tr = _transition;
        if (tr == null) return;
        tr.Elapsed += Durations.FromSeconds(delta);
        if (!tr.Arrived)
        {
            var k = Mathf.Clamp((float)(tr.Elapsed / TransitionOut), 0, 1); // progress 0..1, smoothstepped
            var e = k * k * (3 - 2 * k);
            _rig.SetPose(tr.FromEye + tr.ExitDirection * (TransitionExitMetres * e), tr.FromYaw, tr.FromPitch, PivotAhead);
            _hud.SetTransition(e, tr.Caption);
            if (k >= 1)
            {
                Arrive(tr);
            }

            return;
        }

        // A place entered before it was listed is recomposed as its listing arrives; going up, the arrival pose is
        // worked out again on the real layout, and the glide in simply bends towards it (PLACE-16).
        if (_place != null && _tree != null && _place.TreeVersion != _tree.Version && PlaceComposer.Signature(_tree, _nav.Current) != _place.Signature)
        {
            Recompose(rebuildOnly: true);
            if (tr.Change.Move == PlaceMove.Up) tr.Arrival = UpwardArrival(tr.Change);
        }

        var t = Mathf.Clamp((float)((tr.Elapsed - TransitionOut) / TransitionIn), 0, 1);
        var s = t * t * (3 - 2 * t);
        var eye = tr.ApproachEye.Lerp(Gd(tr.Arrival.Eye), s);
        _rig.SetPose(eye, tr.Arrival.Yaw, tr.Arrival.LookPitch, PivotAhead);
        _hud.SetTransition(1 - s, tr.Caption);
        if (t >= 1)
        {
            _transition = null;
            _hud.SetTransition(0, null);
            ArriveAt(tr.Arrival, snap: true);
            _transitions++;
            _upwardSettle = tr.Change.Move == PlaceMove.Up ? (tr.Change, _gameClock.Now) : null;
            if (_landAfterArrival)
            {
                _landAfterArrival = false;
                Land(); // T over another place: land in it, on the spot below (or the nearest clear ground)
            }
        }
    }

    /// <summary>
    /// The transition's midpoint, behind the fade: composes the new place and picks where to stand — before the link's
    /// gate when going back through a link, on the upward route when going up, before the gate on the way back when
    /// returning to an ancestor, at the arrival point otherwise, or in world terms after travel from the air.
    /// </summary>
    private void Arrive(PlaceTransition tr)
    {
        var change = tr.Change;
        Recompose(rebuildOnly: false);
        var place = _place!;
        var from = change.From;
        PlacePose pose;
        if (change.Move == PlaceMove.Back && change.ViaLink >= 0 && _tree!.Contains(change.ViaLink) && _tree[change.ViaLink].ParentId == change.To)
        {
            // Back through a link: stand in front of the link's gate, facing in, with the link selected (LINK-03).
            pose = place.ReturnPose(change.ViaLink, _placeParams);
            _selection.Select(change.ViaLink);
        }
        else if (change.Move == PlaceMove.Up)
        {
            // Up (the ▲ gate or U): beside the gate you came out of where a straight route allows, facing the next ▲
            // gate, so walking on goes up again (PLACE-16). At the top there is no ▲ gate: face into the place.
            pose = UpwardArrival(change);
            var child = PlaceNavigator.ChildToward(_tree!, change.To, from);
            if (child >= 0) _selection.Select(child);
        }
        else if (change.Move != PlaceMove.Link && (_tree![from].ParentId == change.To || (change.Move != PlaceMove.Root && IsAncestor(change.To, from))))
        {
            // Coming back up: stand in front of the gate on the way to where you were, facing in (PLACE-07).
            var child = PlaceNavigator.ChildToward(_tree, change.To, from);
            pose = place.ReturnPose(child, _placeParams);
            if (child >= 0) _selection.Select(child);
        }
        else
        {
            pose = place.Arrival;
        }

        if (AerialArrival(change) is { } aerialPose) pose = aerialPose; // from the air: arrive flying, in world terms
        NoteVisited(change.To);
        tr.Arrived = true;
        tr.Arrival = pose;
        UpdateInspector();
        var fwd = Gd(CameraMath.HorizontalForward(pose.Yaw));
        tr.ApproachEye = Gd(pose.Eye) - fwd * ApproachBackMetres + new GVector3(0, ApproachRiseMetres, 0);
        _rig.SetPose(tr.ApproachEye, pose.Yaw, pose.LookPitch, PivotAhead);
        GD.Print($"[place] {change.Move} {_tree!.FullPath(change.From)} -> {_tree.FullPath(change.To)} gates={place.Gates.Count} files={place.Files.Count} compose_ms={LastComposeMs:0.0} build_ms={LastPlaceBuildMs:0.0}");
    }

    /// <summary>The arrival pose after going up (<see cref="PlaceRoutes.ContinueUp"/>) on the current composition.</summary>
    private PlacePose UpwardArrival(PlaceChange change)
    {
        var child = PlaceNavigator.ChildToward(_tree!, change.To, change.From);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var route = PlaceRoutes.ContinueUp(_place!, child, _placeParams, _walker.Params);
        LastUpwardRoute = route;
        GD.Print(System.FormattableString.Invariant($"[route] up into {_tree!.DisplayName(change.To)}: verified={route.Verified} standoff={route.StandOff:0.0} m walk={route.Walk.Time.TotalSeconds:0.0} s route_ms={sw.Elapsed.TotalMilliseconds:0.0}"));
        return route.Pose;
    }

    /// <summary>How often a late listing moved the walker onto a re-worked upward route (probes report it).</summary>
    public int UpwardResettles { get; private set; }

    /// <summary>The last Up arrival, for 2 s: if the place is recomposed then, the walker is placed on the new route.</summary>
    private (PlaceChange Change, TimeSpan At)? _upwardSettle;

    /// <summary>
    /// The upward re-settle (PLACE-16) applies for this long after arrival, and only while the walker has moved less than
    /// <see cref="UpwardSettleMaxWalkMetres"/>: past either, the user has taken over and is never moved.
    /// </summary>
    private static readonly TimeSpan UpwardSettleWindow = TimeSpan.FromSeconds(2);

    private const float UpwardSettleMaxWalkMetres = 3f;

    /// <summary>A re-worked route whose eye is within this distance of the old one, metres, leaves the walker where it is.</summary>
    private const float UpwardSettleMinShiftMetres = 0.3f;

    /// <summary>
    /// A listing that lands just after an Up arrival changes the layout under the walker: within 2 s, and while the
    /// walker has gone less than 3 m, it is put on the route worked out again (PLACE-16).
    /// </summary>
    private void SettleUpwardArrival()
    {
        if (_upwardSettle is not { } u || _place == null) return;
        if (_gameClock.Now - u.At > UpwardSettleWindow || LastUpwardRoute is not { } before)
        {
            _upwardSettle = null;
            return;
        }

        var moved = _walker.Eye - before.Pose.Eye;
        if (new System.Numerics.Vector2(moved.X, moved.Z).Length() > UpwardSettleMaxWalkMetres) return;
        var pose = UpwardArrival(u.Change);
        if (System.Numerics.Vector3.Distance(pose.Eye, before.Pose.Eye) > UpwardSettleMinShiftMetres)
        {
            PlaceUserAt(pose);
            _threshold.Reset(_place, pose.Eye, _gameClock.Now);
            UpwardResettles++;
            GD.Print("[route] the listing changed the layout just after arrival: placed on the new route");
        }
    }

    private bool IsAncestor(int ancestor, int id)
    {
        foreach (var a in _tree!.Ancestors(id))
        {
            if (a == ancestor) return true;
        }

        return false;
    }

    /// <summary>Called every frame in Places after movement: deliberate gate crossings travel (PLACE-06).</summary>
    private void CheckThresholds()
    {
        if (_place == null || _transition != null) return;
        var crossed = _threshold.Update(_rig.CameraPosition.ToNumerics(), _gameClock.Now, CameraMath.HorizontalForward(_rig.Yaw));
        if (crossed == null) return;
        if (crossed.IsParent) TravelUp();
        else TravelInto(crossed.TargetId);
    }

    // ------------------------------------------------------------------ Places HUD, labels, highlight

    /// <summary>
    /// The place bar (breadcrumb, summary, connection), and in Places the Destinations panel: the ▲ gate, then every gate
    /// in the gates' own order (ORD-01), prominent ones marked, then the hidden-items row.
    /// </summary>
    private void UpdatePlaceHud()
    {
        if (_tree == null || _tree.Count == 0) return;
        var cur = _nav.Current;
        // The breadcrumb is the physical location, from the top (This computer) down (LINK-03, LOC-01).
        var path = PlaceNavigator.Breadcrumb(_tree, cur).Select(id => (id, _tree.DisplayName(id))).ToList();
        _hud.SetPlace(path, PlaceText.Summary(_tree, _tree[cur]) + LocationNote(cur), canUp: !_tree[cur].IsRoot, atRoot: cur == _nav.Root);
        _hud.SetConnection(ConnectionText());
        if (_experience == Experience.Planets)
        {
            _planets.UpdateDestinations();
            return;
        }

        if (_place == null) return;
        _hud.SetHiddenCount(_place.HiddenCount, _showHidden);
        _hud.SetProblems(PlaceProblems());
        var items = new List<(int, string, Color, string, Texture2D?)>();
        if (_place.ParentGate is { } pg)
        {
            items.Add((pg.TargetId, $"Up to {_tree.DisplayName(pg.TargetId)}", PlacePalette.Up, "The way back: the gate behind the arrival point.", FileIcons.Get(IconKind.Up)));
        }

        foreach (var g in _place.Gates.OrderBy(g => _tree[g.TargetId].Name, NaturalNameOrder.Instance).ThenBy(g => g.TargetId)) // the gates' own order (ORD-01)
        {
            var n = _tree[g.TargetId];
            var state = Listing.Describe(g.State);
            var text = g.IsLink
                ? $"{n.Name}   ·  {PlaceText.LinkShort(_tree, n)}"
                : $"{n.Name}   ·  {PlaceText.GateSubtitle(_tree, n, g).Replace("\n", " · ")}";
            var where = _place.IsWide ? $"Tier {g.Tier + 1}" : "On the gate ring";
            var prom = ProminenceOf(g.TargetId);
            if (prom.IsProminent)
            {
                items.Add((g.TargetId, $"{prom.Mark} {text}", ProminenceColor(prom), $"{prom.Reason} {where}. {_tree.FullPath(g.TargetId)}", FileIcons.Get(FileIcons.For(n))));
                continue;
            }

            items.Add((g.TargetId, text, PlacePalette.ForGate(g, PlaceIdentity.For(n.Name)).Color.Lightened(0.3f), $"{where}. {_tree.FullPath(g.TargetId)}", FileIcons.Get(FileIcons.For(n))));
        }

        if (_place.HiddenCount > 0 && !_showHidden)
        {
            items.Add((-1, $"({_place.HiddenCount:N0} hidden or system item{(_place.HiddenCount == 1 ? "" : "s")} not shown · H shows them)", new Color(0.6f, 0.66f, 0.78f), "Entries with the Windows hidden or system attribute. Names starting with a dot are always shown.", null));
        }

        var title = _place.Gates.Count == 0
            ? (Listing.IsUnknown(_place.State) ? $"Destinations · contents {Listing.Describe(_place.State)}" : "Destinations · no subfolders here")
            : $"Destinations · {_place.Gates.Count:N0} folder{(_place.Gates.Count == 1 ? "" : "s")}" + (_place.IsWide ? $" on {_place.Tiers.Count} tiers" : "") + " · A→Z, gates west → east";
        _hud.SetDestinations(items, title);
        _hud.HighlightDestination(_selection.Selected);
    }

    /// <summary>Per frame in Places: signs (re-ranked a few times a second, or at once when the HUD changes), gate chip, highlight boxes, aerial chip.</summary>
    private void UpdatePlaceFrame()
    {
        if (_place == null || _tree == null) return;
        // Signs also refresh at once when the HUD's layout changes (a panel opens or closes), so no sign shows through it.
        var hudKey = 17; // a hash seed
        foreach (var r in _hud.BlockingRects) hudKey = HashCode.Combine(hudKey, r);
        if (_now - _lastSignUpdate > SignRefreshInterval || hudKey != _lastHudKey)
        {
            _lastHudKey = hudKey;
            _lastSignUpdate = _now; // sorting every sign by distance each frame would be wasteful in wide folders
            UpdateGateChip();
            _placeWorld.UpdateSigns(_rig.Camera, _hud.BlockingRects.ToList());
        }

        UpdateGateSigns();
        PlaceBox(_selectionBox, _selection.Selected, 1f);
        PlaceBox(_hoverBox, _selection.Hovered != _selection.Selected ? _selection.Hovered : -1, 0.6f);
        _hud.SetAscentChip(AerialChip());
    }

    private readonly List<SignCandidate> _signCandidates = [];

    /// <summary>In the aerial view, gates are named only below this height, metres; above it the sign layer names places.</summary>
    private const float GateSignsMaxAltitude = 80f;

    /// <summary>A gate's sign is anchored this far above the gate's top, metres, so it clears the lintel.</summary>
    private const float GateSignLiftMetres = 0.4f;

    /// <summary>
    /// A prominent gate's sign priority per prominence level (PROM-02), metres: the sign layer ranks unfocused signs by
    /// distance, and a prominent one ranks as if it were this much closer per level, so it is named first when space is short.
    /// </summary>
    private const float ProminentSignBoostPerLevel = 90f;

    /// <summary>An unfocused gate closer than this, metres, whose eye-level plate is visible, gets no sign: the plate names it.</summary>
    private const float PlateNamesGateWithinMetres = 16f;

    /// <summary>
    /// Names the gates in view (LABEL-01): every gate and the ▲ gate is a candidate each frame; the sign layer shortens,
    /// stacks and drops them so several are readable at once. A gate whose eye-level plate is in clear view close by
    /// is skipped (the plate names it). Hidden in transitions and while the aerial world names places instead.
    /// </summary>
    private void UpdateGateSigns()
    {
        if (_place == null || _tree == null || _transition != null)
        {
            _signs.Clear();
            return;
        }

        _signCandidates.Clear();
        var gatesNamed = !_aerialShown || CameraAltitude < GateSignsMaxAltitude; // from high up, the places are named instead
        foreach (var g in !gatesNamed ? [] : _place.ParentGate is { } pg ? _place.Gates.Append(pg) : _place.Gates)
        {
            var n = _tree[g.TargetId];
            var focus = g.TargetId == GateChipTarget || g.TargetId == _selection.Selected || g.TargetId == _selection.Hovered;
            var anchor = Gd(g.Position) + new GVector3(0, g.Height + GateSignLiftMetres, 0);
            if (g.IsParent)
            {
                _signCandidates.Add(new SignCandidate(g.TargetId, anchor, $"Up to {_tree.DisplayName(g.TargetId)}", "the way up", PlacePalette.Up, IconKind.Up, focus));
                continue;
            }

            var color = PlacePalette.ForGate(g, PlaceIdentity.For(n.Name)).Color.Lightened(0.45f);
            var detail = PlaceText.GateSubtitle(_tree, n, g).Replace("\n", " · ");
            var prom = ProminenceOf(g.TargetId);
            if (prom.IsProminent)
            {
                // PROM-02: marked and named first when space is short, never moved or reordered.
                _signCandidates.Add(new SignCandidate(g.TargetId, anchor, $"{prom.Mark} {n.Name}", detail, ProminenceColor(prom).Lightened(0.25f), FileIcons.For(n), focus, Boost: ProminentSignBoostPerLevel * prom.Level));
                continue;
            }

            _signCandidates.Add(new SignCandidate(g.TargetId, anchor, n.Name, detail, color, FileIcons.For(n), focus));
        }

        if (_aerialShown) _signCandidates.AddRange(_aerial.Labels); // from the air: the places around, too (AERIAL-01)
        _signs.Layout(_rig.Camera, _signCandidates, _hud.BlockingRects, (c, d) =>
            !c.Focus && d < PlateNamesGateWithinMetres && _placeWorld.GatePlates.TryGetValue(c.Id, out var plate) && plate.Visible);
    }

    /// <summary>Within this distance of a gate, walking towards it, the gate chip names it (PLACE-17).</summary>
    private const float ApproachChipRange = 20f;

    /// <summary>The gate ahead counts only when the eye is lower than this, metres (on foot or flying low over the plaza).</summary>
    private const float ApproachChipMaxEyeHeight = 14f;

    /// <summary>cos 40°: a gate counts as ahead when it lies within 40° of the heading.</summary>
    private const float ApproachChipMinCos = 0.766f;

    /// <summary>Closer than this, metres, any heading counts (you are walking through it).</summary>
    private const float ApproachChipAnyHeadingMetres = 3f;

    /// <summary>A gate counts until you are this far through its plane, metres (<see cref="ThresholdTracker.Depth"/>: positive beyond it).</summary>
    private const float ApproachChipPastDepthMetres = 0.5f;

    /// <summary>The gate the chip names: the one you are walking up to, else the one under the cursor.</summary>
    public int GateChipTarget { get; private set; } = -1;

    /// <summary>
    /// The gate chip (PLACE-17): names the gate ahead (within 20 m, within 40° of the heading, on this side of it), or
    /// else the gate or ▲ gate under the cursor. Its signs may be above the view or hidden by the HUD; the chip is not.
    /// </summary>
    private void UpdateGateChip()
    {
        if (_place == null || _tree == null || _transition != null || _experience != Experience.Places)
        {
            GateChipTarget = -1;
            _hud.SetGateChip(null, "", default);
            return;
        }

        var cam = _rig.CameraPosition.ToNumerics();
        var fwd = CameraMath.HorizontalForward(_rig.Yaw);
        Gate? best = null;
        var bestDistance = float.MaxValue;
        if (cam.Y < ApproachChipMaxEyeHeight)
        {
            foreach (var g in _place.ParentGate is { } pg ? _place.Gates.Append(pg) : _place.Gates)
            {
                var depth = ThresholdTracker.Depth(g, cam);
                if (depth > ApproachChipPastDepthMetres || depth < -ApproachChipRange) continue;
                var to = new System.Numerics.Vector2(g.Position.X - cam.X, g.Position.Z - cam.Z);
                var d = to.Length();
                if (d > ApproachChipRange || d < 1e-3f) continue;
                var cos = (to.X * fwd.X + to.Y * fwd.Z) / d;
                if (cos < ApproachChipMinCos && d > ApproachChipAnyHeadingMetres) continue; // right at the gate, any heading through it counts
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = g;
                }
            }
        }

        var lead = "Ahead:";
        if (best == null && _selection.Hovered >= 0)
        {
            best = _place.ParentGate?.TargetId == _selection.Hovered ? _place.ParentGate : _place.GateFor(_selection.Hovered);
            lead = "Pointing at:";
        }

        if (best == null)
        {
            GateChipTarget = -1;
            _hud.SetGateChip(null, "", default);
            return;
        }

        GateChipTarget = best.TargetId;
        var n = _tree[best.TargetId];
        if (best.IsParent)
        {
            _hud.SetGateChip($"{lead} ▲ Up to {_tree.DisplayName(best.TargetId)}", "the way up · walk through to go up · U does the same", PlacePalette.Up.Lightened(0.3f));
            return;
        }

        var detail = PlaceText.GateSubtitle(_tree, n, best).Replace("\n", " · ");
        detail += best.CanEnter ? " · walk through to enter" : " · can't be followed from here";
        _hud.SetGateChip($"{lead} {(best.IsLink ? "⇢ " : "")}{n.Name}", detail, PlacePalette.ForGate(best, PlaceIdentity.For(n.Name)).Color.Lightened(0.45f));
    }

    private void PlaceBox(MeshInstance3D mesh, int id, float grow)
    {
        // The folder you are in is the landmark itself; a box around it would only veil it.
        if (id < 0 || _place == null || id == _place.FolderId || PlacePicker.BoundsOf(_place, id) is not { } b)
        {
            mesh.Visible = false;
            return;
        }

        var size = Gd(b.Size) * 1.04f + GVector3.One * 0.12f * grow;
        mesh.Position = Gd(b.Center);
        mesh.Scale = size;
        ((ShaderMaterial)mesh.MaterialOverride).SetShaderParameter("box_size", size);
        mesh.Visible = true;
    }

    /// <summary>The map marks the current folder (PLACE-10/15): a ring and a "You are here" tag.</summary>
    private void UpdateHereMarker()
    {
        if (_layout == null || _tree == null || !_layout.Contains(_nav.Current))
        {
            _hereRing.Visible = _hereLabel.Visible = false;
            return;
        }

        var b = _layout.NodeBoxes[_nav.Current];
        // The ring circles the folder's footprint with a margin (0.75 × its longer side + 1 u), just above its top; the tag floats 2.5 u up.
        var r = Mathf.Max(b.Size.X, b.Size.Z) * 0.75f + 1f;
        _hereRing.Position = new GVector3(b.Center.X, b.Max.Y + 0.15f, b.Center.Z);
        _hereRing.Scale = new GVector3(r, 1, r);
        _hereLabel.Position = new GVector3(b.Center.X, b.Max.Y + 2.5f, b.Center.Z);
        _hereLabel.Text = $"You are here · {_tree.DisplayName(_nav.Current)}";
        _hereRing.Visible = _hereLabel.Visible = true;
    }

    /// <summary>What is under a screen point in a place (gates, files, the landmark), else an island of the aerial world, else −1.</summary>
    private int PickPlace(Vector2 screen)
    {
        if (_place == null) return -1;
        var origin = _rig.Camera.ProjectRayOrigin(screen);
        var dir = _rig.Camera.ProjectRayNormal(screen);
        var hit = PlacePicker.Pick(_place, origin.ToNumerics(), dir.ToNumerics());
        return hit >= 0 ? hit : PickIsland(screen); // from the air, the other places can be picked too
    }

    /// <summary>F in Places: frame the selection if it is part of this place.</summary>
    private void FrameInPlace()
    {
        if (_place == null || _selection.Selected < 0) return;
        if (PlacePicker.BoundsOf(_place, _selection.Selected) is not { } b)
        {
            _hud.Toast("The selection is not in this place. Press G to go to its folder, or M to find it on the map.");
            return;
        }

        var c = b.Center;
        if (_locomotion == Locomotion.Walking)
        {
            LookAtInPlace(c); // on foot: turn to look at it
            return;
        }

        // In flight: frame it from 22° above, at least 7 m square so a small file keeps some of its surroundings, 30 % margin.
        var half = NVector3.Max(b.Size * 0.5f, new NVector3(3.5f, 0, 3.5f));
        StartGuideFrame(new Aabb(Gd(c - half), Gd(half * 2)), Degrees.Of(22), null, 1.3f, Locomotion.Flying);
    }

    /// <summary>B in Places: rise above the place to see its neighbourhood (PLACE-10).</summary>
    private void Ascend()
    {
        if (_place == null || _transition != null) return;
        if (_locomotion == Locomotion.Walking) TakeOff(Meters.Zero); // ascending is flight (WALK-06)
        var b = _place.Bounds;
        // The whole place from 58° above, facing as you are; a padding under 1 frames slightly inside the bounds (0.9: rationale not recorded).
        StartGuideFrame(new Aabb(Gd(b.Min), Gd(b.Size)), Degrees.Of(58), _rig.Yaw, 0.9f, Locomotion.Flying);
    }

    private static GVector3 Gd(NVector3 v) => new(v.X, v.Y, v.Z);
}

/// <summary>Conversions from Godot's vectors to System.Numerics, the types Core works in (docs/units.md).</summary>
internal static class VectorInterop
{
    public static NVector3 ToNumerics(this Vector3 v) => new(v.X, v.Y, v.Z);

    public static System.Numerics.Vector2 ToNumerics2(this Vector2 v) => new(v.X, v.Y);
}
