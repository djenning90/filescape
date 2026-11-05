using FileScape.App.Places;
using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Scanning;
using Godot;
using GVector3 = Godot.Vector3;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>The three experiences over one model (AD-15).</summary>
public enum Experience
{
    Places,
    Classic,
    District,
}

/// <summary>
/// The Places experience controller (PLACE-01..15, AD-14). The current folder lives in <see cref="PlaceNavigator"/>,
/// apart from the selection. The world shows one composed place at a time; travel recomposes it behind a short
/// threshold transition. While in Places the whole-tree layout is not computed (PLACE-12).
/// </summary>
public partial class Main
{
    private const double RecomposeInterval = 0.35;
    private const float PivotAhead = 12f;
    private const double TransitionOut = 0.42, TransitionIn = 0.62;

    private readonly PlaceNavigator _nav = new();
    private readonly ThresholdTracker _threshold = new();
    private readonly PlaceParams _placeParams = PlaceParams.Default;
    private PlaceWorld _placeWorld = null!;
    private PlaceComposition? _place;
    private Experience _experience = Experience.Places;
    private double _lastComposeAt = -10;
    private PlaceTransition? _transition;
    private Label3D _hereLabel = null!;
    private MeshInstance3D _hereRing = null!;
    private double _now, _lastSignUpdate;
    private int _transitions;

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

    private sealed class PlaceTransition
    {
        public required PlaceChange Change;
        public required GVector3 FromEye;
        public required GVector3 ExitDirection;
        public required float FromYaw, FromPitch;
        public required string Caption;
        public double T;
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
    }

    // ------------------------------------------------------------------ experience switching (AD-15)

    public void SetExperience(Experience e)
    {
        if (e == _experience) return;
        var from = _experience;
        _experience = e;
        if (e != Experience.Places) _visIndex = e == Experience.Classic ? 0 : 1;
        ApplyExperience(frame: true, from);
    }

    /// <summary>Map index (0 Classic, 1 District) → that map experience. Kept for the reference-view scripts.</summary>
    public void SetVisualization(int index) => SetExperience(index == 0 ? Experience.Classic : Experience.District);

    public void ToggleMap() => SetExperience(_experience == Experience.Places ? (_visIndex == 0 ? Experience.Classic : Experience.District) : Experience.Places);

    private void ApplyExperience(bool frame, Experience? from = null)
    {
        var places = _experience == Experience.Places;
        for (var i = 0; i < _visualizations.Length; i++)
        {
            _visualizations[i].Root.Visible = !places && i == _visIndex;
        }

        _placeWorld.Visible = places;
        _hereRing.Visible = false;
        _hereLabel.Visible = false;
        _beam.Visible = false;
        _spot.Visible = false;
        _labels.HideAll();
        EndFly();
        _transition = null;
        _hud.SetTransition(0, null);

        EndPlaceLook();
        _locomotion = Locomotion.Walking;
        _landing = null;
        _guided = false;
        UpdateFlightHud();
        if (places)
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
                    T = TransitionOut,
                    Arrived = true,
                    Arrival = _place.Arrival,
                    ApproachEye = Gd(_place.Arrival.Eye) + new GVector3(0, 6, 10),
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
        list.Add((PlacePalette.Pending, "gate: scanning…"));
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

    private void FrameFolderInMap(int id)
    {
        if (_layout == null || !_layout.Contains(id) || _tree == null) return;
        var box = _layout.NodeBoxes[id];
        foreach (var c in _tree[id].Children)
        {
            if (_layout.Contains(c)) box = box.Union(_layout.NodeBoxes[c]);
        }

        _rig.Frame(new Aabb(Gd(box.Min), Gd(box.Size)), pitch: 35, padding: 1.25f);
    }

    // ------------------------------------------------------------------ composition

    /// <summary>
    /// Recomposes the current place when its neighbourhood changed (PLACE-12): cheap, local, throttled. The camera stays
    /// where it is; the threshold tracker follows the new gate positions.
    /// </summary>
    private void MaybeRecompose(double now)
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
        }
    }

    private void Recompose(bool rebuildOnly)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _place = PlaceComposer.Compose(_tree!, _nav.Current, _placeParams, IsVisibleInPlace, KindOf(_nav.Current));
        if (!rebuildOnly) _explorer?.Visit(_nav.Current); // list it now (and its child folders) if not yet listed
        LastComposeMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        _placeWorld.Build(_tree!, _place, _quality);
        LastPlaceBuildMs = sw.Elapsed.TotalMilliseconds;
        InstanceCount = _placeWorld.InstanceCount;
        Perf.MarkRelayout(LastComposeMs + LastPlaceBuildMs);
        if (rebuildOnly) _threshold.Follow(_place, _rig.CameraPosition.ToNumerics());
        else _threshold.Reset(_place, _rig.CameraPosition.ToNumerics(), _now);
        _placeWorld.UpdateSigns(_rig.CameraPosition);
        UpdatePlaceHud();
        if (_selection.Selected < 0) UpdateInspector();
    }

    /// <summary>Puts the user at a pose: instantly (travel, first frame), or by a glide that ends on foot (Home, WALK-08).</summary>
    private void ArriveAt(PlacePose pose, bool snap)
    {
        if (snap) PlaceUserAt(pose);
        else StartGuide(pose, Locomotion.Walking);
        if (_place != null) _threshold.Reset(_place, pose.Eye, _now);
    }

    // ------------------------------------------------------------------ travel (PLACE-06..08)

    /// <summary>Travels into a folder: a child goes through its gate, any other folder jumps there.</summary>
    public bool TravelInto(int id)
    {
        if (_tree == null || !_tree.Contains(id) || _transition != null) return false;
        if (_place?.ParentGate?.TargetId == id) return TravelUp();
        if (_tree[id].Kind == NodeKind.Link) return FollowLink(id); // deliberate, resolved on demand (LINK-02)

        if (!_tree[id].IsDirectory) return false;
        return _tree[id].ParentId == _nav.Current ? _nav.Enter(_tree, id) : _nav.GoTo(_tree, id);
    }

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

    public bool TravelRoot() => _tree != null && _transition == null && _nav.GoToRoot();

    /// <summary>G: go into the selected folder (Places through its gate; from a map straight into its place).</summary>
    public void EnterSelection()
    {
        if (_tree == null || _selection.Selected < 0) return;
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

    private void ApproachDestination(int id)
    {
        if (id < 0)
        {
            ToggleHiddenVisibility(); // the "N hidden" row
            return;
        }

        if (_tree == null || _place == null || _transition != null) return;
        _selection.Select(id);
        var pose = _place.ParentGate?.TargetId == id
            ? _place.Standing(_place.ParentGate.Position - _place.ParentGate.Outward * _placeParams.ApproachStandOff, 180, -3, _placeParams)
            : _place.ApproachPose(id, _placeParams);
        StartGuide(pose, _locomotion);
    }

    private void OnPlaceChanged(PlaceChange change)
    {
        _landing = null;
        _guided = false;
        _riseRemaining = 0;
        if (_tree == null) return;
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
            exit = new GVector3(0, 0.6f, 0);
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
        _held.Clear();
    }

    /// <summary>Advances the threshold transition: glide out and fade (old place), recompose, glide in (new place).</summary>
    private void UpdateTransition(double delta)
    {
        var tr = _transition;
        if (tr == null) return;
        tr.T += delta;
        if (!tr.Arrived)
        {
            var k = Mathf.Clamp((float)(tr.T / TransitionOut), 0, 1);
            var e = k * k * (3 - 2 * k);
            _rig.SetPose(tr.FromEye + tr.ExitDirection * (10f * e), tr.FromYaw, tr.FromPitch, PivotAhead);
            _hud.SetTransition(e, tr.Caption);
            if (k >= 1)
            {
                Arrive(tr);
            }

            return;
        }

        var t = Mathf.Clamp((float)((tr.T - TransitionOut) / TransitionIn), 0, 1);
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
        }
    }

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

        tr.Arrived = true;
        tr.Arrival = pose;
        UpdateInspector();
        var fwd = Gd(CameraMath.HorizontalForward(pose.Yaw));
        tr.ApproachEye = Gd(pose.Eye) - fwd * 9f + new GVector3(0, 2.5f, 0);
        _rig.SetPose(tr.ApproachEye, pose.Yaw, pose.LookPitch, PivotAhead);
        GD.Print($"[place] {change.Move} {_tree!.FullPath(change.From)} -> {_tree.FullPath(change.To)} gates={place.Gates.Count} files={place.Files.Count} compose_ms={LastComposeMs:0.0} build_ms={LastPlaceBuildMs:0.0}");
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
        var crossed = _threshold.Update(_rig.CameraPosition.ToNumerics(), _now, CameraMath.HorizontalForward(_rig.Yaw));
        if (crossed == null) return;
        if (crossed.IsParent) TravelUp();
        else TravelInto(crossed.TargetId);
    }

    // ------------------------------------------------------------------ Places HUD, labels, highlight

    private void UpdatePlaceHud()
    {
        if (_tree == null || _tree.Count == 0) return;
        var cur = _nav.Current;
        // The breadcrumb is the physical location, from the top (This computer) down (LINK-03, LOC-01).
        var path = PlaceNavigator.Breadcrumb(_tree, cur).Select(id => (id, _tree.DisplayName(id))).ToList();
        _hud.SetPlace(path, PlaceText.Summary(_tree, _tree[cur]) + LocationNote(cur), canUp: !_tree[cur].IsRoot, atRoot: cur == _nav.Root);
        _hud.SetConnection(ConnectionText());
        if (_place == null) return;
        _hud.SetHiddenCount(_place.HiddenCount, _showHidden);
        _hud.SetProblems(PlaceProblems());
        var items = new List<(int, string, Color, string)>();
        if (_place.ParentGate is { } pg)
        {
            items.Add((pg.TargetId, $"▲  Up to {_tree.DisplayName(pg.TargetId)}", PlacePalette.Up, "The way back: the gate behind the arrival point."));
        }

        foreach (var g in _place.Gates.OrderBy(g => _tree[g.TargetId].Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => _tree[g.TargetId].Name, StringComparer.Ordinal))
        {
            var n = _tree[g.TargetId];
            var state = Listing.Describe(g.State);
            var text = g.IsLink
                ? $"⇢ {n.Name}   ·  {PlaceText.LinkShort(_tree, n)}"
                : g.State is ListingState.Open or ListingState.Partial
                ? $"{n.Name}   ·  {n.DirectFileCount:N0} files, {n.DirectDirCount:N0} folders{(g.State == ListingState.Partial ? " · partly listed" : "")}"
                : $"{n.Name}   ·  {state}";
            var where = _place.IsWide ? $"Tier {g.Tier + 1}" : "On the gate ring";
            items.Add((g.TargetId, text, PlacePalette.ForGate(g, PlaceIdentity.For(n.Name)).Color.Lightened(0.3f), $"{where}. {_tree.FullPath(g.TargetId)}"));
        }

        if (_place.HiddenCount > 0 && !_showHidden)
        {
            items.Add((-1, $"({_place.HiddenCount:N0} hidden or system item{(_place.HiddenCount == 1 ? "" : "s")} not shown · H shows them)", new Color(0.6f, 0.66f, 0.78f), "Entries with the Windows hidden or system attribute. Names starting with a dot are always shown."));
        }

        var title = _place.Gates.Count == 0
            ? (Listing.IsUnknown(_place.State) ? $"Destinations · contents {Listing.Describe(_place.State)}" : "Destinations · no subfolders here")
            : $"Destinations · {_place.Gates.Count:N0} folder{(_place.Gates.Count == 1 ? "" : "s")}" + (_place.IsWide ? $" on {_place.Tiers.Count} tiers" : "");
        _hud.SetDestinations(items, title);
        _hud.HighlightDestination(_selection.Selected);
    }

    private void UpdatePlaceFrame()
    {
        if (_place == null || _tree == null) return;
        if (_now - _lastSignUpdate > 0.2)
        {
            _lastSignUpdate = _now; // sorting every sign by distance each frame would be wasteful in wide folders
            _placeWorld.UpdateSigns(_rig.CameraPosition);
        }

        PlaceBox(_selectionBox, _selection.Selected, 1f);
        PlaceBox(_hoverBox, _selection.Hovered != _selection.Selected ? _selection.Hovered : -1, 0.6f);
        var height = _rig.CameraPosition.Y;
        _hud.SetAscentChip(height > 40
            ? $"Above {_tree.DisplayName(_nav.Current)}: this is its neighbourhood only (places are separate spaces linked by gates). M: whole-filesystem map · Home: land"
            : null);
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
        var r = Mathf.Max(b.Size.X, b.Size.Z) * 0.75f + 1f;
        _hereRing.Position = new GVector3(b.Center.X, b.Max.Y + 0.15f, b.Center.Z);
        _hereRing.Scale = new GVector3(r, 1, r);
        _hereLabel.Position = new GVector3(b.Center.X, b.Max.Y + 2.5f, b.Center.Z);
        _hereLabel.Text = $"You are here · {_tree.DisplayName(_nav.Current)}";
        _hereRing.Visible = _hereLabel.Visible = true;
    }

    private int PickPlace(Vector2 screen)
    {
        if (_place == null) return -1;
        var origin = _rig.Camera.ProjectRayOrigin(screen);
        var dir = _rig.Camera.ProjectRayNormal(screen);
        return PlacePicker.Pick(_place, origin.ToNumerics(), dir.ToNumerics());
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

        var half = NVector3.Max(b.Size * 0.5f, new NVector3(3.5f, 0, 3.5f));
        StartGuideFrame(new Aabb(Gd(c - half), Gd(half * 2)), 22, null, 1.3f, Locomotion.Flying);
    }

    /// <summary>B in Places: rise above the place to see its neighbourhood (PLACE-10).</summary>
    private void Ascend()
    {
        if (_place == null || _transition != null) return;
        if (_locomotion == Locomotion.Walking) TakeOff(rise: 0); // ascending is flight (WALK-06)
        var b = _place.Bounds;
        StartGuideFrame(new Aabb(Gd(b.Min), Gd(b.Size)), 58, _rig.Yaw, 0.9f, Locomotion.Flying);
    }

    private static GVector3 Gd(NVector3 v) => new(v.X, v.Y, v.Z);
}

internal static class VectorInterop
{
    public static NVector3 ToNumerics(this Vector3 v) => new(v.X, v.Y, v.Z);
}
