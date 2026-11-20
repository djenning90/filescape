using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;
using CoreDecoration = FileScape.Core.Planets.DecorationKind;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// One planet drawn from its <see cref="PlanetComposition"/> (the visual treatment, AD-39): the ground (the same height
/// field the walker stands on), its atmosphere, the landmark and overlook, launchers, file objects and piles in their
/// groves, scenery, signposts and the belt of small moons. A preview (a world in the sky) is the ground, atmosphere and
/// landmark only. Meshes are shared per kind; a planet is a few hundred nodes at most.
/// </summary>
public partial class PlanetVisual : Node3D
{
    private static readonly Dictionary<(FileCategory, bool), FileArt> FileMeshes = [];
    private static readonly Dictionary<(PlanetTheme, CoreDecoration, int), ArrayMesh> DecorationMeshes = [];
    private static readonly Dictionary<PlanetTheme, ArrayMesh> LandmarkMeshes = [];
    private static LauncherArt? _launcherArt;
    private static ArrayMesh? _overlookMesh;

    private readonly PlanetComposition _comp;
    private readonly PlanetPalette _pal;
    private readonly bool _full;
    private readonly WorldCharacter _character;
    private MeshInstance3D? _cursor; // the terminal monolith's blinking cursor (WSL roots only)
    private static readonly Dictionary<string, ArrayMesh> CharacterMeshes = [];
    private readonly Dictionary<int, Node3D> _launchers = []; // by target node id
    private readonly Dictionary<int, (MeshInstance3D Mesh, PlanetItem Item, FileArt Art)> _items = []; // by node id (see BuildItems)
    private readonly Dictionary<int, float> _rings = []; // bell flowers ringing: seconds since each was walked past
    private MeshInstance3D? _selectRing, _focusRing;
    private (int Target, float Seconds, float T)? _charge; // the launcher winding up: its target, wind-up length, elapsed (seconds)
    private float _time; // seconds since the planet was built (drives the idle animations)

    // ---- tuning (visual choices; rationale for individual values not recorded unless noted)

    /// <summary>Veiled worlds wear a soft halo this many planet radii across (radius), at this energy: the veil seen from afar.</summary>
    private const float VeiledHaloRadii = 1.35f, VeiledHaloEnergy = 0.9f;

    /// <summary>The selection ring (warm gold, brighter) and the focus ring (the thing you look at; pale blue, dimmer).</summary>
    private static readonly Color SelectRingColor = new(1f, 0.9f, 0.5f), FocusRingColor = new(0.75f, 0.9f, 1f);

    private const float SelectRingEnergy = 1.6f, FocusRingEnergy = 0.9f;

    /// <summary>Size factors on the rings' base radius: the selection ring 15 % larger, the focus ring 5 % smaller (rationale not recorded).</summary>
    private const float SelectRingGrow = 1.15f, FocusRingGrow = 0.95f;

    /// <summary>
    /// The arrival mark's place, metres along the surface from the arrival: ahead and then to the left. Root marks (the
    /// gateway, the monolith) are bigger, so they stood farther out than a waymarker; since PLANET-45 a root's mark stands
    /// where the composer puts it (<see cref="PlanetComposition.RootMark"/>, the same spot unless a route is in the way)
    /// and these root values are only the fallback.
    /// </summary>
    private const float RootMarkAheadMetres = 4.6f, RootMarkLeftMetres = 3.6f, WaymarkerAheadMetres = 3.4f, WaymarkerLeftMetres = 2.6f;

    /// <summary>The pane gateway (≈ 7 m as built) and the terminal monolith (≈ 6 m) are drawn at this scale: ≈ 3.9 m and ≈ 3.5 m tall (rationale for 0.55 not recorded).</summary>
    private const float GatewayScale = 0.55f, MonolithScale = 0.55f;

    /// <summary>The stepping-stone mesh's nominal radius, metres: the composer's default <see cref="PlanetParams.StepRadius"/> (see <see cref="PlanetArt.SteppingStone"/>).</summary>
    private const float SteppingStoneNominalRadiusMetres = 0.6f;

    /// <summary>Petal tints: the way back (pale blue), a link (cyan); other pads take their destination world's accent.</summary>
    private static readonly Color ParentPadTint = new(0.78f, 0.84f, 1f), LinkPadTint = new(0.6f, 0.95f, 1f);

    /// <summary>A pad that cannot be followed (<see cref="PlanetLauncher.Followable"/> false) has a grey heart instead of its destination's colour.</summary>
    private static readonly Color UnfollowableCoreColor = new(0.45f, 0.45f, 0.5f);

    /// <summary>The glow of a launcher's heart (energy; pulsing).</summary>
    private const float LauncherCoreEnergy = 2.4f;

    /// <summary>Ring lights: about one every 1.25 m round each launcher ring, at least 12, kept 0.5 m clear of pads and 0.3 m of solid things.</summary>
    private const float RingLightSpacingMetres = 1.25f, RingLightPadClearanceMetres = 0.5f, RingLightObstacleClearanceMetres = 0.3f;

    private const int MinRingLights = 12;

    /// <summary>A ring light's size (a squashed unit sphere scaled to ≈ 0.22 m across) and its lift above the ground, metres.</summary>
    private const float RingLightScale = 0.11f, RingLightLiftMetres = 0.06f;

    /// <summary>
    /// A decoration's turn about its up axis comes from its seed: one of 628 steps of 0.01 rad (628 ≈ 2π × 100, so the
    /// steps cover just short of a full turn); a negative seed turns the other way. The same idiom as <c>UniverseLayout</c>.
    /// </summary>
    private const int DecorationSpinSteps = 628;

    private const float DecorationSpinStepsPerRadian = 100f;

    /// <summary>Grove signs are shown from about 2.5 m to 16 m away, with soft fades (Godot visibility-range margins of 1.5 m and 6 m): not in your face, not cluttering the distance.</summary>
    private const float SignNearMetres = 2.5f, SignNearFadeMetres = 1.5f, SignFarMetres = 16f, SignFarFadeMetres = 6f;

    /// <summary>The terminal cursor blinks: on for 0.6 s of every 1.06 s (rationale for the exact period not recorded).</summary>
    private const float CursorBlinkPeriodSeconds = 1.06f, CursorOnSeconds = 0.6f;

    /// <summary>Idle launchers breathe between 70 % and 100 % open at this angular rate (radians per second; a breath ≈ 3.9 s), each out of step by its id.</summary>
    private const float LauncherBreathRadiansPerSecond = 1.6f;

    /// <summary>After the wind-up the petals burst open at this rate (openness per second: fully open in 0.2 s); the charge ends this long after release, seconds.</summary>
    private const float BurstOpenPerSecond = 5f, BurstHoldSeconds = 0.5f;

    /// <summary>A rung bell flower sways for this long, seconds, decaying linearly.</summary>
    private const float BellRingSeconds = 1.2f;

    /// <summary>The sway: this angular frequency (radians per second, ≈ 2.9 Hz) and peak angle (radians, ≈ 10°).</summary>
    private const float BellSwayRadiansPerSecond = 18f, BellSwayRadians = 0.18f;

    /// <summary>Around a launcher the ring's radius and lift are in the launcher mesh's own metres (its pad radius is 1.65 m, its walkable top 0.32 m), so they follow the pad's scale.</summary>
    private const float LauncherRingLocalRadius = 1.55f, LauncherRingLocalLift = 0.25f;

    /// <summary>Around a file object: 0.6 × its scale in metres, never smaller than 0.55 m, lifted 6 cm.</summary>
    private const float ItemRingPerScale = 0.6f, MinItemRingMetres = 0.55f, ItemRingLiftMetres = 0.06f;

    /// <summary>The way back's pad has its marker treatment (PLANET-95).</summary>
    public bool WayBackMarked { get; private set; }

    /// <summary>The cache key it was built under (what it shows), kept so a release never files it under another key.</summary>
    public string CacheKey { get; init; } = "";

    /// <summary>Drops the meshes shared between planets (at exit).</summary>
    public static void ClearSharedMeshes()
    {
        FileMeshes.Clear();
        DecorationMeshes.Clear();
        LandmarkMeshes.Clear();
        CharacterMeshes.Clear();
        _launcherArt = null;
        _overlookMesh = null;
    }

    /// <summary>Where the build time went (diagnostics).</summary>
    public string BuildTimes { get; private set; } = "";

    /// <summary>
    /// Builds the planet. <paramref name="full"/>: the planet you stand on (everything) rather than a preview in the sky
    /// (ground, landmark and atmosphere only). <paramref name="ground"/>: ground vertex data already computed on a worker
    /// (<see cref="PlanetArt.PlanetMeshArrays"/>), or null to compute it here. Times each stage into <see cref="BuildTimes"/>.
    /// </summary>
    public PlanetVisual(FsTree tree, PlanetComposition comp, Quality quality, bool full, PlanetArt.GroundArrays? ground = null, WorldCharacter character = default)
    {
        _comp = comp;
        _character = character;
        _full = full;
        _pal = PlanetArt.Palette(comp.Theme);
        Name = full ? "PlanetVisual" : "PlanetPreview";
        var surf = comp.Surface;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var groundNode = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = ground != null ? PlanetArt.PlanetMesh(ground) : PlanetArt.PlanetMesh(comp.Shape, _pal, full ? PlanetArt.SubdivisionsFor(comp.Shape.Radius) : 3),
            // The ground never casts: a sphere shadowing itself at grazing sun angles made blocky acne on the near ground
            // (2026-11-11); hills are gentle, and everything standing on the ground still casts onto it.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(groundNode);
        if (comp.Surface.Shell is { } shell) BuildHazard(shell, ground?.Rim); // a Hazard World: its black hole and jet, rim glow and debris (PlanetVisual.Hazard.cs)
        // The atmosphere shell reads as a soft rim from outside; from inside (the planet you stand on) it would haze the
        // whole view, so only worlds in the sky get it.
        if (!full) AddChild(PlanetArt.Atmosphere(comp.Shape.Radius, _pal));
        if (comp.Theme == PlanetTheme.Veiled) AddChild(PlanetArt.Halo(_pal.Glow, comp.Shape.Radius * VeiledHaloRadii, VeiledHaloEnergy));

        if (comp.Landmark is { } lm)
        {
            // The bell tower's bell hangs on its own so it can swing (PLANET-56, PlanetVisual.Bell.cs).
            if (!LandmarkMeshes.TryGetValue(comp.Theme, out var mesh)) LandmarkMeshes[comp.Theme] = mesh = PlanetArt.Landmark(comp.Theme, withBell: !lm.Bell);
            var landmarkNode = new MeshInstance3D { Name = "Landmark", Mesh = mesh, Transform = Stand(surf, lm.Direction, PlanetFrame.North(lm.Direction), 1f, sink: BellTower.SinkMetres) };
            AddChild(landmarkNode);
            if (lm.Bell) HangBell(landmarkNode, comp.Theme);
            if (full && comp.Theme == PlanetTheme.Hearth) BuildLighthouseBeam(landmarkNode); // PLANET-74 (PlanetVisual.Whimsy.cs)
        }

        var tGround = sw.Elapsed.TotalMilliseconds;
        if (!full) return;
        BuildOverlook(surf);
        BuildArrivalMark(surf);
        BuildLaunchers(tree, surf);
        BuildWayBackMarker(surf); // PLANET-95: the way back's rim and spire (PlanetVisual.WayBack.cs)
        BuildRingLights(surf);
        var tLaunch = sw.Elapsed.TotalMilliseconds;
        BuildItems(tree, surf);
        var tItems = sw.Elapsed.TotalMilliseconds;
        BuildDecorations(surf);
        var tDeco = sw.Elapsed.TotalMilliseconds;
        BuildSigns(surf);
        BuildBelt();
        BuildTimes = $"ground+landmark {tGround:0} ms, overlook+launchers {tLaunch - tGround:0}, items {tItems - tLaunch:0}, decorations {tDeco - tItems:0}, signs+belt {sw.Elapsed.TotalMilliseconds - tDeco:0}";
        _selectRing = Ring(SelectRingColor, SelectRingEnergy);
        _focusRing = Ring(FocusRingColor, FocusRingEnergy);
    }

    /// <summary>A character prop's mesh, built once per <paramref name="key"/> and shared by every planet (cleared by <see cref="ClearSharedMeshes"/>).</summary>
    private static ArrayMesh Shared(string key, Func<ArrayMesh> build)
    {
        if (!CharacterMeshes.TryGetValue(key, out var m)) CharacterMeshes[key] = m = build();
        return m;
    }

    /// <summary>
    /// What the place is, standing beside the arrival (AD-48), ahead and to the left inside the meadow (the first launcher
    /// ring is 9 m out), facing you as you land: a Windows volume's pane gateway, a WSL distribution's terminal monolith
    /// (its cursor blinks), or a folder's depth-band waymarker (one glowing ring per band; a teal bead inside a project).
    /// </summary>
    private void BuildArrivalMark(PlanetSurface surf)
    {
        var up = _comp.Arrival.Up;
        var facing = _comp.Arrival.Facing;
        var left = NVector3.Normalize(NVector3.Cross(up, facing));
        var r = _comp.Shape.Radius;
        var root = _character.Kind is WorldKind.WindowsRoot or WorldKind.WslRoot;
        var dir = PlanetFrame.Move(PlanetFrame.Move(up, facing, root ? RootMarkAheadMetres : WaymarkerAheadMetres, r), left, root ? RootMarkLeftMetres : WaymarkerLeftMetres, r);
        // The tangent at the mark that points back towards the arrival (or the arrival's facing if they coincide).
        var towardArrival = up - dir * NVector3.Dot(up, dir);
        towardArrival = towardArrival.LengthSquared() > 1e-8f ? NVector3.Normalize(towardArrival) : facing;
        // A root's mark is solid and the composer chose its place clear of the routes (PLANET-45, AD-58).
        if (root && _comp.RootMark is { } rm)
        {
            dir = rm.Direction;
            towardArrival = rm.Facing;
            BuildRootMarkCue(surf, rm);
        }

        switch (_character.Kind)
        {
            case WorldKind.Folder:
                AddChild(new MeshInstance3D { Name = "Waymarker", Mesh = Shared($"waymarker-{_character.Band}-{_character.InsideRepository}", () => PlanetArt.Waymarker(_character.Band, _character.InsideRepository)), Transform = Stand(surf, dir, facing, 1f, sink: 0.12f) });
                break;
            case WorldKind.WindowsRoot:
                AddChild(new MeshInstance3D { Name = "Gateway", Mesh = Shared("pane-gateway", () => PlanetArt.PaneGateway(_pal)), Transform = Stand(surf, dir, towardArrival, GatewayScale, sink: 0.12f) });
                break;
            case WorldKind.WslRoot:
                // The prompt is on the monolith's +Z face: turned so that face looks at the arrival.
                var monolith = new MeshInstance3D { Name = "Monolith", Mesh = Shared("terminal-monolith", () => PlanetArt.TerminalMonolith(_pal)), Transform = Stand(surf, dir, -towardArrival, MonolithScale, sink: 0.12f) };
                AddChild(monolith);
                var (cursorMesh, at) = PlanetArt.TerminalCursor();
                _cursor = new MeshInstance3D { Name = "Cursor", Mesh = cursorMesh, Position = at };
                monolith.AddChild(_cursor);
                break;
        }
    }

    /// <summary>Stands a prop on the ground at a direction, facing a tangent, scaled; its base slightly sunk so it never floats on hills.</summary>
    public static Transform3D Stand(PlanetSurface surf, NVector3 up, NVector3 facing, float scale, float height = 0, float sink = 0.05f)
    {
        var y = PlanetWorld.Gd(up);
        var f = PlanetWorld.Gd(facing);
        f = (f - y * f.Dot(y));
        if (f.LengthSquared() < 1e-6f) f = PlanetWorld.Gd(PlanetFrame.North(up));
        f = f.Normalized();
        var z = -f; // a prop's front is −Z
        var x = y.Cross(z).Normalized();
        var basis = new Basis(x, y, z).Scaled(new Godot.Vector3(scale, scale, scale));
        return new Transform3D(basis, PlanetWorld.Gd(surf.PointAt(up, height - sink)));
    }

    /// <summary>The overlook (or, on a repository, the workbench) and its stepping stones, each scaled from its mesh's built size to the composed size.</summary>
    private void BuildOverlook(PlanetSurface surf)
    {
        if (_comp.Overlook is not { } ov) return;
        // On a repository world the overlook is the workbench (AD-50).
        var mesh = _character.Kind == WorldKind.Repository ? Shared($"workbench-{ov.Top:0.00}", () => PlanetArt.Workbench(_pal, ov.Top)) : _overlookMesh ??= PlanetArt.Overlook();
        var at = Stand(surf, ov.Direction, PlanetFrame.North(ov.Direction), ov.Radius / PlanetArt.OverlookRadiusMetres, sink: 0.1f);
        AddChild(new MeshInstance3D { Name = _character.Kind == WorldKind.Repository ? "Workbench" : "Overlook", Mesh = mesh, Transform = at });
        if (_character.Kind != WorldKind.Repository) BuildTelescopeCue(at); // PLANET-55: "E · Telescope" over it on approach
        var i = 0;
        foreach (var step in ov.Steps)
        {
            AddChild(new MeshInstance3D { Name = $"Step{i}", Mesh = PlanetArt.SteppingStone(step.Top, i++), Transform = Stand(surf, step.Direction, PlanetFrame.North(step.Direction), step.Radius / SteppingStoneNominalRadiusMetres, sink: 0.05f) });
        }
    }

    /// <summary>
    /// One bloom launcher per pad, scaled to the composed pad radius; the petals are tinted by destination (the way back,
    /// a link, or the destination world's accent) and the heart glows that colour, or grey when it cannot be followed.
    /// </summary>
    private void BuildLaunchers(FsTree tree, PlanetSurface surf)
    {
        _launcherArt ??= PlanetArt.Launcher();
        foreach (var l in _comp.Launchers)
        {
            var node = _launcherArt.Instantiate(open01: 1f);
            node.Name = $"Launcher-{l.TargetId}";
            var toCentre = -l.Direction;
            node.Transform = Stand(surf, l.Direction, PlanetFrame.North(l.Direction), l.PadRadius / _launcherArt.PadRadiusMetres, sink: 0.02f);
            var color = l.IsParent ? ParentPadTint : l.IsLink ? LinkPadTint : PlanetArt.Palette(SkyTheme(l)).Accent;
            foreach (var child in node.GetChildren().OfType<MeshInstance3D>())
            {
                if (child.Name.ToString().StartsWith("Petal", StringComparison.Ordinal)) child.SetInstanceShaderParameter("tint", color);
            }

            if (node.GetNodeOrNull<MeshInstance3D>("Core") is { } core) core.MaterialOverride = PlanetArt.GlowMaterial(l.Followable ? color : UnfollowableCoreColor, LauncherCoreEnergy, pulse: 1f);
            AddChild(node);
            _launchers[l.TargetId] = node;
            _ = toCentre;
            _ = tree;
        }
    }

    /// <summary>
    /// The launcher rings drawn on the ground (2026-11-11): a circle of small lights through the pads of each ring round
    /// the arrival, so the ring reads as one route you can follow from pad to pad; the way back's pad has its own colour.
    /// </summary>
    private void BuildRingLights(PlanetSurface surf)
    {
        // The rings are the pads' distances from the arrival (the pole, +Y), rounded to 0.25 m so pads on one ring share it.
        var rings = _comp.Launchers.Select(l => MathF.Round(surf.SurfaceDistance(l.Direction, System.Numerics.Vector3.UnitY) * 4f) / 4f).Distinct().ToList();
        if (rings.Count == 0) return;
        var lights = new List<Transform3D>();
        foreach (var dist in rings)
        {
            var polar = dist / surf.Radius;
            var circumference = 2 * MathF.PI * surf.Radius * MathF.Sin(polar);
            var n = Math.Max(MinRingLights, (int)(circumference / RingLightSpacingMetres));
            for (var i = 0; i < n; i++)
            {
                var d = PlanetFrame.FromPolar(polar, 2 * MathF.PI * i / n);
                if (_comp.Launchers.Any(l => surf.SurfaceDistance(l.Direction, d) < l.PadRadius + RingLightPadClearanceMetres)) continue; // not under a pad
                if (surf.Near(d, RingLightObstacleClearanceMetres).Any()) continue; // nor inside a file object or solid scenery
                lights.Add(Stand(surf, d, PlanetFrame.North(d), RingLightScale, height: RingLightLiftMetres, sink: 0f));
            }
        }

        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new SphereMesh { Radius = 1, Height = 1.2f, RadialSegments = 8, Rings = 4 }, InstanceCount = lights.Count };
        for (var i = 0; i < lights.Count; i++) mm.SetInstanceTransform(i, lights[i]);
        AddChild(new MultiMeshInstance3D { Name = "RingLights", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = PlanetArt.GlowMaterial(_pal.Glow, 1.6f, pulse: 0.4f) });
    }

    /// <summary>The theme of a launcher's destination world in the sky (Veiled when it has none).</summary>
    private PlanetTheme SkyTheme(PlanetLauncher l) => l.SkyBody >= 0 && l.SkyBody < _comp.SkyBodies.Count ? _comp.SkyBodies[l.SkyBody].Theme : PlanetTheme.Veiled;

    /// <summary>File objects and piles, their meshes shared per (category, pile); a face surface starts with the placeholder until a thumbnail arrives.</summary>
    private void BuildItems(FsTree tree, PlanetSurface surf)
    {
        foreach (var it in _comp.Items)
        {
            var pile = it.Kind == PlanetItemKind.Pile;
            if (!FileMeshes.TryGetValue((it.Category, pile), out var art)) FileMeshes[(it.Category, pile)] = art = pile ? PlanetArt.Pile(it.Category) : PlanetArt.FileObject(it.Category);
            var mi = new MeshInstance3D { Mesh = art.Mesh, Transform = Stand(surf, it.Direction, it.Facing, it.Scale) };
            if (art.FaceSurface >= 0) mi.SetSurfaceOverrideMaterial(art.FaceSurface, PlanetArt.FacePlaceholder());
            AddChild(mi);
            // Keyed by node id; an item without a node gets a unique negative key (−10 000 and below, clear of the −1 "none").
            var key = it.NodeId >= 0 ? it.NodeId : -10_000 - _items.Count;
            _items[key] = (mi, it, art);
            // A picture's canvas wears the picture face from the start (PlanetVisual.Pictures): the plain mat until it fills.
            if (it.NodeId >= 0 && tree.Contains(it.NodeId) && ShowsPicture(tree[it.NodeId], art)) CanvasFor(key);
        }
    }

    /// <summary>
    /// Scenery: three mesh variants per (theme, kind), picked by seed, each turned by its seed and drawn as one MultiMesh
    /// per mesh (hundreds of pieces in a handful of draw calls). The pieces that answer a twirl (PLANET-75) are remembered
    /// by their MultiMesh slot so they can move (<see cref="PlayScenery"/>).
    /// </summary>
    private void BuildDecorations(PlanetSurface surf)
    {
        var groups = new Dictionary<ArrayMesh, List<Transform3D>>();
        var slots = new List<(int Index, ArrayMesh Mesh, int Slot)>();
        for (var index = 0; index < _comp.Decorations.Count; index++)
        {
            var d = _comp.Decorations[index];
            var variant = Math.Abs(d.Seed) % 3;
            if (!DecorationMeshes.TryGetValue((_comp.Theme, d.Kind, variant), out var mesh))
            {
                // The variant's art seed (7919 × variant + 13): fixed per variant, so every planet of a theme shares them.
                DecorationMeshes[(_comp.Theme, d.Kind, variant)] = mesh = PlanetArt.Decoration((DecorationKind)(int)d.Kind, _comp.Theme, variant * 7919 + 13);
            }

            var spin = (d.Seed % DecorationSpinSteps) / DecorationSpinStepsPerRadian;
            var (a, b) = PlanetShape.Tangents(d.Direction);
            var facing = a * MathF.Cos(spin) + b * MathF.Sin(spin);
            if (!groups.TryGetValue(mesh, out var list)) groups[mesh] = list = [];
            if (Answers(d.Kind)) slots.Add((index, mesh, list.Count));
            list.Add(Stand(surf, d.Direction, facing, d.Scale, sink: 0.08f));
        }

        var multis = new Dictionary<ArrayMesh, MultiMesh>();
        foreach (var (mesh, list) in groups)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = list.Count };
            for (var i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, list[i]);
            AddChild(new MultiMeshInstance3D { Multimesh = mm });
            multis[mesh] = mm;
        }

        foreach (var (index, mesh, slot) in slots) _scenery[index] = (multis[mesh], slot, groups[mesh][slot]);
    }

    /// <summary>The grove names on the ground (billboarded Label3Ds; launchers are named elsewhere).</summary>
    private void BuildSigns(PlanetSurface surf)
    {
        foreach (var s in _comp.Signs)
        {
            // Launchers are named by their worlds in the sky (screen signs) and by the prompt on the pad; the ground signs
            // name the groves (integrated signage), small, and only from a few metres away (not in your face).
            if (s.TargetId >= 0) continue;
            var at = PlanetWorld.Gd(surf.PointAt(s.Direction, s.Height));
            var label = new Label3D
            {
                Text = s.Detail.Length > 0 ? $"{s.Text}\n{s.Detail}" : s.Text,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 48,
                OutlineSize = 12,
                PixelSize = 0.0045f, // a 48 px line ≈ 0.22 m tall
                Modulate = new Color(1f, 0.95f, 0.84f),
                OutlineModulate = new Color(0.08f, 0.05f, 0.16f, 0.85f),
                Position = at,
                VisibilityRangeBegin = SignNearMetres,
                VisibilityRangeBeginMargin = SignNearFadeMetres,
                VisibilityRangeEnd = SignFarMetres,
                VisibilityRangeEndMargin = SignFarFadeMetres,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                NoDepthTest = false,
                DoubleSided = true,
                LineSpacing = -6,
            };
            AddChild(label);
        }
    }

    /// <summary>The belt of small moons (sky bodies marked InBelt): one MultiMesh of spheres coloured by each moon's theme, faintly self-lit by this planet's glow.</summary>
    private void BuildBelt()
    {
        var belt = _comp.SkyBodies.Where(k => k.InBelt).ToList();
        if (belt.Count == 0) return;
        var sphere = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 14, Rings = 7 };
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = sphere, InstanceCount = belt.Count };
        for (var i = 0; i < belt.Count; i++)
        {
            var k = belt[i];
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Godot.Vector3(k.Radius, k.Radius, k.Radius)), PlanetWorld.Gd(k.Position)));
            mm.SetInstanceColor(i, PlanetArt.Palette(k.Theme).GroundHigh);
        }

        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.8f, EmissionEnabled = true, Emission = _pal.Glow * 0.25f } });
    }

    /// <summary>A hidden glowing ring (a unit torus, placed and sized by <see cref="PlaceRing"/>).</summary>
    private MeshInstance3D Ring(Color color, float energy)
    {
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.85f, OuterRadius = 1f, Rings = 40, RingSegments = 6 },
            MaterialOverride = PlanetArt.GlowMaterial(color, energy, pulse: 1f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(ring);
        return ring;
    }

    // ------------------------------------------------------------------ live behaviour

    /// <summary>The launcher to <paramref name="target"/> closes into a bud over the wind-up, then bursts open at release.</summary>
    public void ChargeLauncher(int target, float seconds) => _charge = (target, seconds, 0f);

    /// <summary>A bell flower rings (sways) when you walk past it.</summary>
    public void Ring(int nodeId) => _rings[nodeId] = 0f;

    /// <summary>
    /// Per frame: the cursor blinks, idle launchers breathe and the charging one closes to a bud and bursts open, rung bell
    /// flowers sway, and the selection and focus rings follow <paramref name="selected"/> and <paramref name="focus"/>
    /// (node ids, −1 for none). A preview only blinks its cursor.
    /// </summary>
    public void Animate(float dt, NVector3 camera, int selected, int focus)
    {
        _time += dt;
        if (_cursor != null) _cursor.Visible = (_time % CursorBlinkPeriodSeconds) < CursorOnSeconds; // the terminal's cursor blinks
        AnimateRootMarkCue(dt);
        if (!_full) return;
        AnimateBell(dt); // PLANET-56: the bell tower's bell swings when rung (PlanetVisual.Bell.cs)
        AnimateTelescopeCue(dt); // PLANET-55: the telescope's sign on approach (PlanetVisual.Telescope.cs)
        AnimateWhimsy(dt); // PLANET-74/75: the lighthouse's beam, the answering scenery and its shimmer (PlanetVisual.Whimsy.cs)
        AnimateWayBack(); // PLANET-95: the way back's spire climbs (PlanetVisual.WayBack.cs)
        var surf = _comp.Surface;
        foreach (var (id, node) in _launchers)
        {
            var open = 0.85f + 0.15f * MathF.Sin(_time * LauncherBreathRadiansPerSecond + id);
            if (_charge is { } c && c.Target == id)
            {
                var t = c.T + dt;
                _charge = (c.Target, c.Seconds, t);
                // Over the wind-up the petals close (smoothstep) to a bud; at release they burst open.
                open = t < c.Seconds ? 1f - Smooth(t / c.Seconds) : MathF.Min(1f, (t - c.Seconds) * BurstOpenPerSecond);
                if (t > c.Seconds + BurstHoldSeconds) _charge = null;
            }

            _launcherArt!.SetOpen(node, open);
        }

        foreach (var key in _rings.Keys.ToList())
        {
            var t = _rings[key] + dt;
            if (t > BellRingSeconds) { _rings.Remove(key); t = BellRingSeconds; }
            else _rings[key] = t;
            if (_items.TryGetValue(key, out var e))
            {
                var sway = MathF.Sin(t * BellSwayRadiansPerSecond) * BellSwayRadians * (1 - t / BellRingSeconds);
                var tr = Stand(surf, e.Item.Direction, e.Item.Facing, e.Item.Scale);
                e.Mesh.Transform = new Transform3D(tr.Basis * new Basis(Godot.Vector3.Forward, sway), tr.Origin);
            }
        }

        PlaceRing(_selectRing, selected, SelectRingGrow);
        PlaceRing(_focusRing, focus != selected ? focus : -1, FocusRingGrow);
    }

    /// <summary>Shows <paramref name="ring"/> round the file object or launcher <paramref name="id"/>, or hides it (−1 or not on this planet).</summary>
    private void PlaceRing(MeshInstance3D? ring, int id, float grow)
    {
        if (ring == null) return;
        if (id < 0 || !_items.TryGetValue(id, out var e))
        {
            if (id >= 0 && _launchers.TryGetValue(id, out var l))
            {
                ring.Visible = true;
                ring.Transform = l.Transform.ScaledLocal(new Godot.Vector3(LauncherRingLocalRadius * grow, 1, LauncherRingLocalRadius * grow)).TranslatedLocal(new Godot.Vector3(0, LauncherRingLocalLift, 0));
                return;
            }

            ring.Visible = false;
            return;
        }

        ring.Visible = true;
        var r = MathF.Max(MinItemRingMetres, e.Item.Scale * ItemRingPerScale) * grow;
        ring.Transform = Stand(_comp.Surface, e.Item.Direction, e.Item.Facing, 1f, height: ItemRingLiftMetres).ScaledLocal(new Godot.Vector3(r, 1, r));
    }

    /// <summary>Smoothstep on 0–1 (eases in and out).</summary>
    private static float Smooth(float x) => x * x * (3 - 2 * x);
}
