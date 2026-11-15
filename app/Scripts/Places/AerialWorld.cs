using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Places;
using Godot;
using NVector2 = System.Numerics.Vector2;

namespace FileScape.App.Places;

/// <summary>
/// The aerial world (AERIAL-01..06, AD-26): every known folder place as an island on the world plane, seen when you fly
/// up out of a place. Drawn in the current place's local frame (floating origin), so the place you are in stays where
/// it is and its full-detail rendering (<see cref="PlaceWorld"/>) sits exactly on its island.
/// <para>
/// An island shows what is known about its folder and nothing invented: its rim in the folder's colour (or its listing
/// state: being listed pulses, not listed yet is veiled, denied is hatched), a spire as tall as its subtree is large, up
/// to nine towers for its largest files, and a warm ring once visited. Lines join each place to its child folders;
/// raised cyan arcs are links. The current place has a gold beacon. Names are screen-space signs (SignLayer).
/// Rebuilt only when the layout or the current place changes; labels follow the camera each frame.
/// </para>
/// </summary>
public partial class AerialWorld : Node3D
{
    /// <summary>Largest files shown on an island (an honest sample, not every file).</summary>
    public const int TowersPerIsland = 9;

    // ---- island shape, metres (an island's radius comes from the Core world map; these are the fixed proportions round it)
    /// <summary>The island disc: 0.8 m thick, centred 0.2 m below the ground, so its top is at <see cref="IslandTopMetres"/>.</summary>
    private const float DiscThicknessMetres = 0.8f, DiscCentreMetres = -0.2f;

    /// <summary>The island's top surface: spires and towers stand on it.</summary>
    private const float IslandTopMetres = 0.2f;

    /// <summary>
    /// The spire: 6 m plus 5 m per factor of ten in the subtree's entries (files and folders), so a folder with 10,000
    /// entries stands 26 m tall and big places read from far away; 0.12 of the island radius wide at the base.
    /// </summary>
    private const float SpireBaseMetres = 6f, SpireMetresPerDecade = 5f, SpireWidthRadii = 0.12f;

    /// <summary>The veil over a folder not listed yet: a translucent column 1.2 island radii wide and 18 m tall (PLACE-11).</summary>
    private const float VeilWidthRadii = 1.2f, VeilHeightMetres = 18f, VeilCentreMetres = 9f, VeilOpacity = 0.45f;

    /// <summary>A sign floats this far above its spire's top.</summary>
    private const float SignAboveSpireMetres = 2f;

    /// <summary>
    /// Sign priority (<see cref="SignCandidate.Boost"/>, metres): when signs compete for room, the current place's parent and
    /// children rank as if 120 m nearer than they are, visited places 80 m (the two add).
    /// </summary>
    private const float NeighbourSignBoost = 120f, VisitedSignBoost = 80f;

    /// <summary>Island style bits in INSTANCE_CUSTOM.w; keep in sync with island.gdshader.</summary>
    private const int IslandListing = 1, IslandHatched = 2, IslandVeiled = 4, IslandVisited = 8, IslandSelected = 16;

    /// <summary>The hub (all drives and distributions) has no folder colour of its own: a pale blue.</summary>
    private static readonly Color HubColor = new(0.75f, 0.85f, 1f);

    // ---- the current place's beacon (its full detail is PlaceWorld's): a 160 m gold column
    private const float BeaconWidthMetres = 2.4f, BeaconHeightMetres = 160f, BeaconCentreMetres = 80f;
    private static readonly Color BeaconColor = new(1f, 0.85f, 0.4f, 0.9f);

    /// <summary>The "You are here" sign: 30 m up the beacon, in its gold.</summary>
    private const float HereSignMetres = 30f;
    private static readonly Color HereSignColor = new(1f, 0.88f, 0.5f);

    // ---- lines between places, metres
    /// <summary>Parent-child lines lie just above the ground, from rim to rim.</summary>
    private const float TreeLineHeightMetres = 0.15f, TreeLineThicknessMetres = 0.1f;

    /// <summary>Lines in the current place's own neighbourhood are wider and brighter, so the way up and down reads first.</summary>
    private const float NeighbourLineWidthMetres = 2.2f, TreeLineWidthMetres = 1.4f, NeighbourLineBrightness = 0.9f, TreeLineBrightness = 0.45f;

    /// <summary>
    /// A link's arc (AD-18): from 2 m above each island, rising to 0.18 of the span at its middle (capped at 140 m, so a
    /// link across the world does not tower over everything), drawn as 12 straight pieces.
    /// </summary>
    private const float LinkEndMetres = 2f, LinkLiftPerMetre = 0.18f, MaxLinkLiftMetres = 140f;
    private const int LinkArcSegments = 12;
    private const float LinkWidthMetres = 1.2f, LinkThicknessMetres = 0.6f, LinkBrightness = 0.9f;

    // ---- file towers on an island
    /// <summary>
    /// Towers stand on two arcs either side of the north avenue: the first pair 60° off north, each further pair 18° more,
    /// at 0.42 island radii from the centre (like a place's file quarters); each is 2.2 m square.
    /// </summary>
    private const float TowerFirstAngleDeg = 60f, TowerStepAngleDeg = 18f, TowerRingRadii = 0.42f, TowerWidthMetres = 2.2f;

    private readonly BoxMesh _box = new() { Size = Vector3.One };
    private readonly CylinderMesh _disc = new() { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1, RadialSegments = 48, Rings = 1 };
    private readonly CylinderMesh _column = new() { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1, RadialSegments = 12, Rings = 1 };
    private readonly CylinderMesh _spire = new() { TopRadius = 0.08f, BottomRadius = 0.5f, Height = 1, RadialSegments = 10, Rings = 1 };
    private readonly ShaderMaterial _island, _spireMat, _tower, _beacon, _street;
    private readonly Dictionary<string, List<MultiMeshInstance3D>> _pools = [];
    private readonly List<SignCandidate> _labels = [];

    /// <summary>Label anchors for the islands (local frame), rebuilt with the geometry.</summary>
    public IReadOnlyList<SignCandidate> Labels => _labels;

    public int IslandCount { get; private set; }
    public int InstanceCount { get; private set; }
    public double LastBuildMs { get; private set; }

    /// <summary>The frame everything was built in (the current place's).</summary>
    public PlaceFrame Frame { get; private set; } = PlaceFrame.Identity;

    public AerialWorld()
    {
        Name = "AerialWorld";
        _island = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/island.gdshader") };
        _spireMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/landmark.gdshader") };
        _spireMat.SetShaderParameter("use_instance_color", true);
        _spireMat.SetShaderParameter("accent", new Color(1, 1, 1));
        _tower = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/tower.gdshader") };
        _beacon = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/beacon.gdshader") };
        _street = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/street.gdshader") };
        _street.SetShaderParameter("street_color", new Color(1, 1, 1));
        _street.SetShaderParameter("glow", 0.9f);
        _street.SetShaderParameter("pulse_speed", 0f);
    }

    /// <summary>
    /// Builds the islands in <paramref name="current"/>'s frame. <paramref name="visited"/> are places the user has been
    /// to this session; <paramref name="selected"/> is highlighted. The current place itself gets only its beacon.
    /// </summary>
    public void Build(FsTree tree, WorldMap map, int current, IReadOnlySet<int> visited, int selected, Func<FsNode, bool> showEntry)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Frame = map.FrameOf(current);
        var discs = new InstanceBatch();
        var spires = new InstanceBatch();
        var towers = new InstanceBatch();
        var veils = new InstanceBatch();
        var lines = new InstanceBatch();
        _labels.Clear();
        var currentIsland = map.Islands.GetValueOrDefault(current);
        foreach (var island in map.Islands.Values)
        {
            var n = tree[island.Id];
            var local = Frame.ToLocal(island.Center);
            var at = new Vector3(local.X, 0, local.Y);
            var state = Listing.Of(n);
            var identity = PlaceIdentity.For(n.Name);
            var color = n.IsHub ? HubColor : PlacePalette.For(state, identity);

            // The line to the parent (from rim to rim), brighter on the current place's own neighbourhood.
            if (island.ParentId >= 0 && map.Islands.TryGetValue(island.ParentId, out var parent))
            {
                var pl = Frame.ToLocal(parent.Center);
                var from = new Vector3(pl.X, TreeLineHeightMetres, pl.Y);
                var to = at with { Y = TreeLineHeightMetres };
                var dir = (to - from).Normalized();
                var near = island.ParentId == current || island.Id == current || island.Id == currentIsland?.ParentId;
                lines.AddSegment(from + dir * parent.Radius, to - dir * island.Radius, near ? NeighbourLineWidthMetres : TreeLineWidthMetres, TreeLineThicknessMetres, color * (near ? NeighbourLineBrightness : TreeLineBrightness), 0);
            }

            if (island.Id == current)
            {
                // The place you are in is drawn in full by PlaceWorld; from the air a gold beacon marks it.
                veils.Add(new Vector3(BeaconWidthMetres, 0, 0), new Vector3(0, BeaconHeightMetres, 0), new Vector3(0, 0, BeaconWidthMetres), new Vector3(0, BeaconCentreMetres, 0), BeaconColor, new Color(0, 0, 0, 0));
                _labels.Add(new SignCandidate(island.Id, new Vector3(0, HereSignMetres, 0), $"You are here · {tree.DisplayName(island.Id)}", "", HereSignColor, FileIcons.For(n), Focus: true));
                continue;
            }

            var style = state switch
            {
                ListingState.Pending or ListingState.Listing => IslandListing,
                ListingState.Denied or ListingState.Unavailable or ListingState.Failed => IslandHatched,
                _ when Listing.IsUnknown(state) && state != ListingState.Partial => IslandVeiled,
                _ => 0,
            } + (visited.Contains(island.Id) ? IslandVisited : 0) + (island.Id == selected ? IslandSelected : 0);
            var r = island.Radius;
            discs.Add(new Vector3(r * 2, 0, 0), new Vector3(0, DiscThicknessMetres, 0), new Vector3(0, 0, r * 2), at + new Vector3(0, DiscCentreMetres, 0), color, new Color(r, 0, 0, style));

            // A spire as tall as the subtree is large (log of its files), so big places stand out from far away.
            var h = SpireBaseMetres + SpireMetresPerDecade * Mathf.Log(1 + n.SubtreeFileCount + n.SubtreeDirCount) / Mathf.Log(10);
            spires.Add(new Vector3(r * SpireWidthRadii, 0, 0), new Vector3(0, h, 0), new Vector3(0, 0, r * SpireWidthRadii), at + new Vector3(0, h / 2 + IslandTopMetres, 0), color, new Color(0, 0, 0, 0));
            if ((style & IslandVeiled) != 0)
            {
                // Not listed yet: a veiled column, never a bare (empty-looking) island (PLACE-11).
                veils.Add(new Vector3(r * VeilWidthRadii, 0, 0), new Vector3(0, VeilHeightMetres, 0), new Vector3(0, 0, r * VeilWidthRadii), at + new Vector3(0, VeilCentreMetres, 0), color with { A = VeilOpacity }, new Color(0, 0, 0, 0));
            }

            AddTowers(tree, n, island, Frame, towers, showEntry);
            var detail = Listing.IsUnknown(state) && state != ListingState.Partial ? Listing.Describe(state) : FolderSummary.Gate(n, state);
            if (island.HiddenChildren > 0) detail += $" · {island.HiddenChildren} more folders not drawn";
            var near2 = island.ParentId == current || island.Id == currentIsland?.ParentId;
            _labels.Add(new SignCandidate(island.Id, at + new Vector3(0, h + SignAboveSpireMetres, 0), tree.DisplayName(island.Id), detail, color.Lightened(0.35f), FileIcons.For(n),
                island.Id == selected, Boost: (near2 ? NeighbourSignBoost : 0f) + (visited.Contains(island.Id) ? VisitedSignBoost : 0f)));
        }

        foreach (var (_, from, to) in map.LinkEdges)
        {
            // A link: a raised cyan arc between the two places (not a road: it is a shortcut, AD-18).
            var a = Frame.ToLocal(map.Islands[from].Center);
            var b = Frame.ToLocal(map.Islands[to].Center);
            var pa = new Vector3(a.X, LinkEndMetres, a.Y);
            var pb = new Vector3(b.X, LinkEndMetres, b.Y);
            var lift = Mathf.Min(MaxLinkLiftMetres, pa.DistanceTo(pb) * LinkLiftPerMetre);
            var prev = pa;
            for (var i = 1; i <= LinkArcSegments; i++)
            {
                var t = i / (float)LinkArcSegments;
                var p = pa.Lerp(pb, t) + new Vector3(0, 4 * lift * t * (1 - t), 0); // a parabola: 0 at the ends, lift at t = ½
                lines.AddSegment(prev, p, LinkWidthMetres, LinkThicknessMetres, PlacePalette.Link * LinkBrightness, 0);
                prev = p;
            }
        }

        Emit("discs", discs, _disc, _island, false);
        Emit("spires", spires, _spire, _spireMat, false);
        Emit("towers", towers, _box, _tower, false);
        Emit("veils", veils, _column, _beacon, false);
        Emit("lines", lines, _box, _street, false);
        IslandCount = map.Islands.Count;
        InstanceCount = discs.Count + spires.Count + towers.Count + veils.Count + lines.Count;
        LastBuildMs = sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>Up to <see cref="TowersPerIsland"/> of the folder's largest files, on an arc around its spire.</summary>
    private static void AddTowers(FsTree tree, FsNode n, WorldIsland island, PlaceFrame frame, InstanceBatch towers, Func<FsNode, bool> showEntry)
    {
        // Keep the largest TowersPerIsland files in one pass: add each, and when the list overflows sort it (largest first)
        // and drop the smallest. Ties keep no particular order (List.Sort is unstable).
        var files = new List<FsNode>(TowersPerIsland + 1);
        foreach (var c in n.Children)
        {
            var f = tree[c];
            if (f.Kind != NodeKind.File || !showEntry(f)) continue;
            files.Add(f);
            if (files.Count > TowersPerIsland)
            {
                files.Sort((x, y) => y.Size.CompareTo(x.Size));
                files.RemoveAt(files.Count - 1);
            }
        }

        if (files.Count == 0) return;
        var local = frame.ToLocal(island.Center);
        var north = frame.AngleToLocal(island.NorthDeg);
        for (var i = 0; i < files.Count; i++)
        {
            // Two arcs either side of the place's north avenue, like its file quarters.
            var side = i % 2 == 0 ? -1 : 1;
            var a = north + side * (TowerFirstAngleDeg + TowerStepAngleDeg * (i / 2)); // i / 2 is integer: one step per pair
            var dir = PlaceGeometry.Direction(a);
            var pos = new NVector2(local.X, local.Y) + new NVector2(dir.X, dir.Z) * island.Radius * TowerRingRadii;
            var h = SizeScale.Place.Height(files[i].Size);
            var cat = FileScape.Core.Interaction.FileCategories.Of(files[i].Name);
            towers.AddBox(new Vector3(pos.X, h / 2 + IslandTopMetres, pos.Y), new Vector3(TowerWidthMetres, h, TowerWidthMetres), Palette.Category[cat], 0);
        }
    }

    /// <summary>Builds one named batch into its reused pool of MultiMesh nodes (see <see cref="InstanceBatch.BuildInto"/>).</summary>
    private void Emit(string slot, InstanceBatch batch, Mesh mesh, Material material, bool shadows)
    {
        if (!_pools.TryGetValue(slot, out var pool))
        {
            pool = [];
            _pools[slot] = pool;
        }

        batch.BuildInto(pool, this, mesh, material, shadows);
    }
}
