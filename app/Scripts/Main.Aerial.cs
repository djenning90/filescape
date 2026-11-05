using FileScape.App.Places;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Units;
using Godot;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;

namespace FileScape.App;

/// <summary>
/// The aerial world controller (AERIAL-01..06, AD-26). Flying up out of a place reveals the explored world around it:
/// every known folder as an island laid out by <see cref="WorldLayout"/>, drawn by <see cref="AerialWorld"/> in the
/// current place's frame. Walking keeps the local, progressive view (vistas beyond the gates); above
/// <see cref="AerialAltitude"/> the vistas give way to the real places, the haze thins, and the sign layer names places.
/// <para>
/// Scope (AERIAL-05): the world is what is already known — the bounded tree, or in the connected world the hub, the
/// arrival folder's ancestors (as "not listed yet" islands) and everything listed so far. Flying near an island inside
/// the arrival folder's subtree or a place you have visited lists it (Reveal, a few per second, never a drive or a
/// distribution); anything else stays "not listed yet" until you go there. Nothing scans a drive or a profile.
/// </para>
/// </summary>
public partial class Main
{
    /// <summary>Above this height over the ground (metres), flying shows the aerial world.</summary>
    public const float AerialAltitude = 14f;

    /// <summary>The layout is recomputed at most this often while the explored world changes.</summary>
    private static readonly TimeSpan WorldLayoutInterval = TimeSpan.FromSeconds(0.6);

    /// <summary>At most this many reveals are asked for per second (they queue behind what the user waits for).</summary>
    private const int RevealsPerSecond = 4;

    private AerialWorld _aerial = null!;
    private WorldMap? _worldMap;
    private TimeSpan _lastWorldLayout = TimeSpan.FromSeconds(-10), _lastReveal;
    private int _aerialBuiltFor = -1, _aerialBuiltSelected = -2;
    private WorldMap? _aerialBuiltMap;
    private bool _aerialShown;
    private readonly HashSet<int> _visitedPlaces = [];
    private readonly HashSet<int> _revealed = [];

    /// <summary>A pending arrival from the air: where the camera was in world terms, and whether to land on arrival.</summary>
    private sealed record AerialTravel(int Target, NVector2 WorldXz, float Height, Degrees WorldYaw, Degrees Pitch, bool Land);

    private AerialTravel? _aerialTravel;
    private bool _landAfterArrival;

    public WorldMap? WorldMap => _worldMap;
    public AerialWorld Aerial => _aerial;
    public bool AerialShown => _aerialShown;
    public IReadOnlySet<int> VisitedPlaces => _visitedPlaces;
    public int RevealsRequested => _revealed.Count;

    private void BuildAerialScene()
    {
        _aerial = new AerialWorld { Visible = false };
        AddChild(_aerial);
    }

    /// <summary>Height of the camera above the ground below it, metres.</summary>
    public float CameraAltitude
    {
        get
        {
            var c = _rig.CameraPosition;
            var ground = _place == null ? 0f : Terrain.SupportHeight(new NVector2(c.X, c.Z), 0.1f, c.Y, 0);
            return c.Y - Mathf.Max(0, ground);
        }
    }

    private bool WantAerial =>
        _experience == Experience.Places && _place != null && _tree != null && _transition == null &&
        (_locomotion == Locomotion.Flying || (_guided && _afterGuide == Locomotion.Flying)) && _landing == null && CameraAltitude > AerialAltitude;

    /// <summary>Per frame in Places: show or hide the aerial world, keep it current, thin the haze, reveal nearby places.</summary>
    private void UpdateAerial()
    {
        var want = WantAerial;
        if (want != _aerialShown)
        {
            _aerialShown = want;
            _aerial.Visible = want;
            _placeWorld.SetVistasVisible(!want);
            if (!want && _experience == Experience.Places) PlaceAtmosphere.SetAerial(_world, 0, _quality);
        }

        if (!want || _tree == null) return;
        PlaceAtmosphere.SetAerial(_world, Mathf.Clamp((CameraAltitude - AerialAltitude) / 40f, 0, 1), _quality);
        EnsureWorldMap(force: false);
        if (_worldMap == null) return;
        if (_worldMap != _aerialBuiltMap || _aerialBuiltFor != _nav.Current || _aerialBuiltSelected != _selection.Selected)
        {
            _aerial.Build(_tree, _worldMap, _nav.Current, _visitedPlaces, _selection.Selected, ShowInPlaces);
            _aerialBuiltMap = _worldMap;
            _aerialBuiltFor = _nav.Current;
            _aerialBuiltSelected = _selection.Selected;
            GD.Print(System.FormattableString.Invariant($"[aerial] {_aerial.IslandCount} places, {_aerial.InstanceCount} instances, build {_aerial.LastBuildMs:0.0} ms"));
        }

        RevealNearby();
    }

    /// <summary>Recomputes the world layout when the explored world changed (throttled), or now when <paramref name="force"/>.</summary>
    private void EnsureWorldMap(bool force)
    {
        if (_tree == null) return;
        if (_worldMap != null && _worldMap.TreeVersion == _tree.Version && _worldMap.Contains(_nav.Current)) return;
        if (!force && _worldMap != null && _now - _lastWorldLayout < WorldLayoutInterval) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // The way to where you are is always in the world, even through a hidden folder (AppData is hidden on Windows).
        _onTheWay.Clear();
        foreach (var a in _tree.Ancestors(_nav.Current)) _onTheWay.Add(a);
        foreach (var a in _tree.Ancestors(_nav.Root)) _onTheWay.Add(a);
        _onTheWay.Add(_nav.Current);
        _onTheWay.Add(_nav.Root);
        _worldMap = WorldLayout.Compute(_tree, n => ShowInPlaces(n) || _onTheWay.Contains(n.Id));
        _lastWorldLayout = _now;
        LastWorldLayoutMs = sw.Elapsed.TotalMilliseconds;
    }

    public double LastWorldLayoutMs { get; private set; }

    private readonly HashSet<int> _onTheWay = [];

    /// <summary>Whether an entry is shown in Places (the hidden/system filter, VISI-04): the aerial world matches it.</summary>
    private bool ShowInPlaces(FsNode n) => _showHidden || (n.Flags & (NodeFlags.Hidden | NodeFlags.System)) == 0;

    /// <summary>
    /// Lists unlisted islands near the camera (AERIAL-04) — only inside the arrival folder's subtree or below a place
    /// visited this session, a few per second, each once.
    /// </summary>
    private void RevealNearby()
    {
        if (_explorer == null || _worldMap == null || _tree == null || _now - _lastReveal < TimeSpan.FromSeconds(1.0 / RevealsPerSecond)) return;
        var cam = _aerial.Frame.ToWorld(new NVector2(_rig.CameraPosition.X, _rig.CameraPosition.Z));
        var reach = Mathf.Max(260f, CameraAltitude * 4f);
        var best = -1;
        var bestD = float.MaxValue;
        foreach (var i in _worldMap.Islands.Values)
        {
            if (_revealed.Contains(i.Id) || Listing.Of(_tree[i.Id]) != ListingState.Unlisted || !MayReveal(i.Id)) continue;
            var d = NVector2.Distance(cam, i.Center);
            if (d < reach && d < bestD)
            {
                bestD = d;
                best = i.Id;
            }
        }

        if (best < 0) return;
        _revealed.Add(best);
        _lastReveal = _now;
        _explorer.Reveal(best);
        GD.Print($"[aerial] reveal {_tree.FullPath(best)} ({bestD:0} m)");
    }

    /// <summary>A folder may be listed from the air if it is inside the arrival folder or below a place visited this session.</summary>
    private bool MayReveal(int id)
    {
        if (_tree == null) return false;
        foreach (var a in _tree.Ancestors(id))
        {
            if (a == _nav.Root || _visitedPlaces.Contains(a)) return true;
        }

        return false;
    }

    /// <summary>Remembers that the user has been to a place (warm ring on its island; its subtree may be revealed).</summary>
    private void NoteVisited(int id) => _visitedPlaces.Add(id);

    /// <summary>The island under a screen point while the aerial world is shown, else −1.</summary>
    private int PickIsland(Vector2 screen)
    {
        if (!_aerialShown || _worldMap == null) return -1;
        var origin = _rig.Camera.ProjectRayOrigin(screen);
        var dir = _rig.Camera.ProjectRayNormal(screen);
        if (dir.Y > -1e-3f) return -1;
        var t = (0.2f - origin.Y) / dir.Y;
        var hit = origin + dir * t;
        var id = _worldMap.IslandAt(_aerial.Frame.ToWorld(new NVector2(hit.X, hit.Z)), 1.15f);
        return id == _nav.Current ? -1 : id;
    }

    /// <summary>The camera's pose in world terms (for an arrival from the air in another place's frame).</summary>
    private (NVector2 WorldXz, Degrees WorldYaw) WorldCamera()
    {
        var frame = _worldMap?.FrameOf(_nav.Current) ?? PlaceFrame.Identity;
        var c = _rig.CameraPosition;
        return (frame.ToWorld(new NVector2(c.X, c.Z)), _rig.Yaw - Degrees.Of(frame.NorthDeg));
    }

    /// <summary>
    /// Goes to another place from the air (AERIAL-03): over its island with T (and lands), or by G / double-click on
    /// it (arriving above it, flying, looking down). History, selection and link identity work as for any place change.
    /// </summary>
    public bool AerialTravelTo(int id, bool land)
    {
        if (_tree == null || _worldMap == null || !_worldMap.Contains(id) || id == _nav.Current || _transition != null) return false;
        var (xz, yaw) = WorldCamera();
        _aerialTravel = new AerialTravel(id, xz, _rig.CameraPosition.Y, yaw, _rig.LookPitch, land);
        if (_nav.GoTo(_tree, id)) return true;
        _aerialTravel = null;
        return false;
    }

    /// <summary>The arrival pose after travel from the air, in the new place's frame (null when this change is not one).</summary>
    private PlacePose? AerialArrival(PlaceChange change)
    {
        if (_aerialTravel is not { } at || at.Target != change.To) return null;
        _aerialTravel = null;
        EnsureWorldMap(force: true);
        var frame = _worldMap!.FrameOf(change.To);
        _landAfterArrival = at.Land;
        _selection.Select(-1); // what was selected belongs to where you came from
        if (at.Land)
        {
            // Exactly where you were in the world, so the landing below is the spot you chose.
            var local = frame.ToLocal(at.WorldXz);
            return new PlacePose(new System.Numerics.Vector3(local.X, at.Height, local.Y), at.WorldYaw + Degrees.Of(frame.NorthDeg), at.Pitch);
        }

        // Above the new place, south of its landmark, looking down across it to its gates: high enough to see all of
        // it, low enough to read it (50–140 m, whatever height you left from).
        var height = Mathf.Clamp(at.Height, 50f, 140f);
        return new PlacePose(new System.Numerics.Vector3(0, height, height * 0.85f), Degrees.Zero, Degrees.Of(-38));
    }

    /// <summary>B: rise (or glide) to a view of this place, its parent and its child places from above (AERIAL-06).</summary>
    private void AerialView()
    {
        if (_place == null || _transition != null || _tree == null) return;
        if (_locomotion == Locomotion.Walking) TakeOff(Meters.Zero);
        EnsureWorldMap(force: true);
        var b = _place.Bounds;
        var min = new GVector3(b.Min.X, 0, b.Min.Z);
        var max = new GVector3(b.Max.X, 20, b.Max.Z);
        if (_worldMap != null && _worldMap.Islands.TryGetValue(_nav.Current, out var self))
        {
            var frame = _worldMap.FrameOf(_nav.Current);
            foreach (var i in _worldMap.Islands.Values)
            {
                if (i.ParentId != _nav.Current && i.Id != self.ParentId) continue;
                var l = frame.ToLocal(i.Center);
                min = min.Min(new GVector3(l.X - i.Radius, 0, l.Y - i.Radius));
                max = max.Max(new GVector3(l.X + i.Radius, 20, l.Y + i.Radius));
            }
        }

        StartGuideFrame(new Aabb(min, max - min), Degrees.Of(52), _rig.Yaw, 0.85f, Locomotion.Flying);
    }

    /// <summary>The status line while the aerial world is shown (scope and what to do), or null.</summary>
    private string? AerialChip()
    {
        if (!_aerialShown || _worldMap == null || _tree == null) return null;
        var unlisted = 0;
        foreach (var i in _worldMap.Islands.Values) if (Listing.IsUnknown(Listing.Of(_tree[i.Id]))) unlisted++;
        var more = _worldMap.Truncated > 0 ? $" · {_worldMap.Truncated:N0} deeper folders not drawn" : "";
        return $"Aerial view · {_worldMap.Islands.Count:N0} known places ({unlisted:N0} not listed yet){more} · fly over one and press T to land there · click one, then G to go · B frames this neighbourhood";
    }
}
