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
    private MeshInstance3D? _cursor;
    private static readonly Dictionary<string, ArrayMesh> CharacterMeshes = [];
    private readonly Dictionary<int, Node3D> _launchers = [];
    private readonly Dictionary<int, (MeshInstance3D Mesh, PlanetItem Item, FileArt Art)> _items = [];
    private readonly Dictionary<int, float> _rings = [];
    private readonly HashSet<int> _thumbRequested = [];
    private readonly List<(int Id, Image Image)> _thumbReady = [];
    private int _thumbLoading;
    private MeshInstance3D? _selectRing, _focusRing;
    private (int Target, float Seconds, float T)? _charge;
    private float _time;

    public int ThumbnailsShown { get; private set; }

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
        // The atmosphere shell reads as a soft rim from outside; from inside (the planet you stand on) it would haze the
        // whole view, so only worlds in the sky get it.
        if (!full) AddChild(PlanetArt.Atmosphere(comp.Shape.Radius, _pal));
        if (comp.Theme == PlanetTheme.Veiled) AddChild(PlanetArt.Halo(_pal.Glow, comp.Shape.Radius * 1.35f, 0.9f));

        if (comp.Landmark is { } lm)
        {
            if (!LandmarkMeshes.TryGetValue(comp.Theme, out var mesh)) LandmarkMeshes[comp.Theme] = mesh = PlanetArt.Landmark(comp.Theme);
            AddChild(new MeshInstance3D { Name = "Landmark", Mesh = mesh, Transform = Stand(surf, lm.Direction, PlanetFrame.North(lm.Direction), 1f, sink: 0.15f) });
        }

        var tGround = sw.Elapsed.TotalMilliseconds;
        if (!full) return;
        BuildOverlook(surf);
        BuildArrivalMark(surf);
        BuildLaunchers(tree, surf);
        BuildRingLights(surf);
        var tLaunch = sw.Elapsed.TotalMilliseconds;
        BuildItems(tree, surf);
        var tItems = sw.Elapsed.TotalMilliseconds;
        BuildDecorations(surf);
        var tDeco = sw.Elapsed.TotalMilliseconds;
        BuildSigns(surf);
        BuildBelt();
        BuildTimes = $"ground+landmark {tGround:0} ms, overlook+launchers {tLaunch - tGround:0}, items {tItems - tLaunch:0}, decorations {tDeco - tItems:0}, signs+belt {sw.Elapsed.TotalMilliseconds - tDeco:0}";
        _selectRing = Ring(new Color(1f, 0.9f, 0.5f), 1.6f);
        _focusRing = Ring(new Color(0.75f, 0.9f, 1f), 0.9f);
    }

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
        var dir = PlanetFrame.Move(PlanetFrame.Move(up, facing, root ? 4.6f : 3.4f, r), left, root ? 3.6f : 2.6f, r);
        var towardArrival = up - dir * NVector3.Dot(up, dir);
        towardArrival = towardArrival.LengthSquared() > 1e-8f ? NVector3.Normalize(towardArrival) : facing;
        switch (_character.Kind)
        {
            case WorldKind.Folder:
                AddChild(new MeshInstance3D { Name = "Waymarker", Mesh = Shared($"waymarker-{_character.Band}-{_character.InsideRepository}", () => PlanetArt.Waymarker(_character.Band, _character.InsideRepository)), Transform = Stand(surf, dir, facing, 1f, sink: 0.12f) });
                break;
            case WorldKind.WindowsRoot:
                AddChild(new MeshInstance3D { Name = "Gateway", Mesh = Shared("pane-gateway", () => PlanetArt.PaneGateway(_pal)), Transform = Stand(surf, dir, towardArrival, 0.55f, sink: 0.12f) });
                break;
            case WorldKind.WslRoot:
                // The prompt is on the monolith's +Z face: turned so that face looks at the arrival.
                var monolith = new MeshInstance3D { Name = "Monolith", Mesh = Shared("terminal-monolith", () => PlanetArt.TerminalMonolith(_pal)), Transform = Stand(surf, dir, -towardArrival, 0.55f, sink: 0.12f) };
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

    private void BuildOverlook(PlanetSurface surf)
    {
        if (_comp.Overlook is not { } ov) return;
        // On a repository world the overlook is the workbench (AD-50).
        var mesh = _character.Kind == WorldKind.Repository ? Shared($"workbench-{ov.Top:0.00}", () => PlanetArt.Workbench(_pal, ov.Top)) : _overlookMesh ??= PlanetArt.Overlook();
        AddChild(new MeshInstance3D { Name = _character.Kind == WorldKind.Repository ? "Workbench" : "Overlook", Mesh = mesh, Transform = Stand(surf, ov.Direction, PlanetFrame.North(ov.Direction), ov.Radius / 2.2f, sink: 0.1f) });
        var i = 0;
        foreach (var step in ov.Steps)
        {
            AddChild(new MeshInstance3D { Name = $"Step{i}", Mesh = PlanetArt.SteppingStone(step.Top, i++), Transform = Stand(surf, step.Direction, PlanetFrame.North(step.Direction), step.Radius / 0.6f, sink: 0.05f) });
        }
    }

    private void BuildLaunchers(FsTree tree, PlanetSurface surf)
    {
        _launcherArt ??= PlanetArt.Launcher();
        foreach (var l in _comp.Launchers)
        {
            var node = _launcherArt.Instantiate(open01: 1f);
            node.Name = $"Launcher-{l.TargetId}";
            var toCentre = -l.Direction;
            node.Transform = Stand(surf, l.Direction, PlanetFrame.North(l.Direction), l.PadRadius / _launcherArt.PadRadiusMetres, sink: 0.02f);
            var color = l.IsParent ? new Color(0.78f, 0.84f, 1f) : l.IsLink ? new Color(0.6f, 0.95f, 1f) : PlanetArt.Palette(SkyTheme(l)).Accent;
            foreach (var child in node.GetChildren().OfType<MeshInstance3D>())
            {
                if (child.Name.ToString().StartsWith("Petal", StringComparison.Ordinal)) child.SetInstanceShaderParameter("tint", color);
            }

            if (node.GetNodeOrNull<MeshInstance3D>("Core") is { } core) core.MaterialOverride = PlanetArt.GlowMaterial(l.Followable ? color : new Color(0.45f, 0.45f, 0.5f), 2.4f, pulse: 1f);
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
        var rings = _comp.Launchers.Select(l => MathF.Round(surf.SurfaceDistance(l.Direction, System.Numerics.Vector3.UnitY) * 4f) / 4f).Distinct().ToList();
        if (rings.Count == 0) return;
        var lights = new List<Transform3D>();
        foreach (var dist in rings)
        {
            var polar = dist / surf.Radius;
            var circumference = 2 * MathF.PI * surf.Radius * MathF.Sin(polar);
            var n = Math.Max(12, (int)(circumference / 1.25f));
            for (var i = 0; i < n; i++)
            {
                var d = PlanetFrame.FromPolar(polar, 2 * MathF.PI * i / n);
                if (_comp.Launchers.Any(l => surf.SurfaceDistance(l.Direction, d) < l.PadRadius + 0.5f)) continue; // not under a pad
                if (surf.Near(d, 0.3f).Any()) continue; // nor inside a file object or solid scenery
                lights.Add(Stand(surf, d, PlanetFrame.North(d), 0.11f, height: 0.06f, sink: 0f));
            }
        }

        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new SphereMesh { Radius = 1, Height = 1.2f, RadialSegments = 8, Rings = 4 }, InstanceCount = lights.Count };
        for (var i = 0; i < lights.Count; i++) mm.SetInstanceTransform(i, lights[i]);
        AddChild(new MultiMeshInstance3D { Name = "RingLights", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = PlanetArt.GlowMaterial(_pal.Glow, 1.6f, pulse: 0.4f) });
    }

    private PlanetTheme SkyTheme(PlanetLauncher l) => l.SkyBody >= 0 && l.SkyBody < _comp.SkyBodies.Count ? _comp.SkyBodies[l.SkyBody].Theme : PlanetTheme.Veiled;

    private void BuildItems(FsTree tree, PlanetSurface surf)
    {
        foreach (var it in _comp.Items)
        {
            var pile = it.Kind == PlanetItemKind.Pile;
            if (!FileMeshes.TryGetValue((it.Category, pile), out var art)) FileMeshes[(it.Category, pile)] = art = pile ? PlanetArt.Pile(it.Category) : PlanetArt.FileObject(it.Category);
            var mi = new MeshInstance3D { Mesh = art.Mesh, Transform = Stand(surf, it.Direction, it.Facing, it.Scale) };
            if (art.FaceSurface >= 0) mi.SetSurfaceOverrideMaterial(art.FaceSurface, PlanetArt.FacePlaceholder());
            AddChild(mi);
            var key = it.NodeId >= 0 ? it.NodeId : -10_000 - _items.Count;
            _items[key] = (mi, it, art);
        }
    }

    private void BuildDecorations(PlanetSurface surf)
    {
        var groups = new Dictionary<ArrayMesh, List<Transform3D>>();
        foreach (var d in _comp.Decorations)
        {
            var variant = Math.Abs(d.Seed) % 3;
            if (!DecorationMeshes.TryGetValue((_comp.Theme, d.Kind, variant), out var mesh))
            {
                DecorationMeshes[(_comp.Theme, d.Kind, variant)] = mesh = PlanetArt.Decoration((DecorationKind)(int)d.Kind, _comp.Theme, variant * 7919 + 13);
            }

            var spin = (d.Seed % 628) / 100f;
            var (a, b) = PlanetShape.Tangents(d.Direction);
            var facing = a * MathF.Cos(spin) + b * MathF.Sin(spin);
            if (!groups.TryGetValue(mesh, out var list)) groups[mesh] = list = [];
            list.Add(Stand(surf, d.Direction, facing, d.Scale, sink: 0.08f));
        }

        foreach (var (mesh, list) in groups)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = list.Count };
            for (var i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, list[i]);
            AddChild(new MultiMeshInstance3D { Multimesh = mm });
        }
    }

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
                PixelSize = 0.0045f,
                Modulate = new Color(1f, 0.95f, 0.84f),
                OutlineModulate = new Color(0.08f, 0.05f, 0.16f, 0.85f),
                Position = at,
                VisibilityRangeBegin = 2.5f,
                VisibilityRangeBeginMargin = 1.5f,
                VisibilityRangeEnd = 16f,
                VisibilityRangeEndMargin = 6f,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                NoDepthTest = false,
                DoubleSided = true,
                LineSpacing = -6,
            };
            AddChild(label);
        }
    }

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

    public void Animate(float dt, NVector3 camera, int selected, int focus)
    {
        _time += dt;
        if (_cursor != null) _cursor.Visible = (_time % 1.06f) < 0.6f; // the terminal's cursor blinks
        if (!_full) return;
        var surf = _comp.Surface;
        foreach (var (id, node) in _launchers)
        {
            var open = 0.85f + 0.15f * MathF.Sin(_time * 1.6f + id);
            if (_charge is { } c && c.Target == id)
            {
                var t = c.T + dt;
                _charge = (c.Target, c.Seconds, t);
                open = t < c.Seconds ? 1f - Smooth(t / c.Seconds) : MathF.Min(1f, (t - c.Seconds) * 5f);
                if (t > c.Seconds + 0.5f) _charge = null;
            }

            _launcherArt!.SetOpen(node, open);
        }

        foreach (var key in _rings.Keys.ToList())
        {
            var t = _rings[key] + dt;
            if (t > 1.2f) { _rings.Remove(key); t = 1.2f; }
            else _rings[key] = t;
            if (_items.TryGetValue(key, out var e))
            {
                var sway = MathF.Sin(t * 18f) * 0.18f * (1 - t / 1.2f);
                var tr = Stand(surf, e.Item.Direction, e.Item.Facing, e.Item.Scale);
                e.Mesh.Transform = new Transform3D(tr.Basis * new Basis(Godot.Vector3.Forward, sway), tr.Origin);
            }
        }

        PlaceRing(_selectRing, selected, 1.15f);
        PlaceRing(_focusRing, focus != selected ? focus : -1, 0.95f);
    }

    private void PlaceRing(MeshInstance3D? ring, int id, float grow)
    {
        if (ring == null) return;
        if (id < 0 || !_items.TryGetValue(id, out var e))
        {
            if (id >= 0 && _launchers.TryGetValue(id, out var l))
            {
                ring.Visible = true;
                ring.Transform = l.Transform.ScaledLocal(new Godot.Vector3(1.55f * grow, 1, 1.55f * grow)).TranslatedLocal(new Godot.Vector3(0, 0.25f, 0));
                return;
            }

            ring.Visible = false;
            return;
        }

        ring.Visible = true;
        var r = MathF.Max(0.55f, e.Item.Scale * 0.6f) * grow;
        ring.Transform = Stand(_comp.Surface, e.Item.Direction, e.Item.Facing, 1f, height: 0.06f).ScaledLocal(new Godot.Vector3(r, 1, r));
    }

    private static float Smooth(float x) => x * x * (3 - 2 * x);

    // ------------------------------------------------------------------ real pictures on the easels

    /// <summary>A picture read deliberately (E) is shown on its easel too.</summary>
    public void ShowThumbnail(int id, Image img)
    {
        lock (_thumbReady) _thumbReady.Add((id, img));
        _thumbRequested.Add(id);
    }

    /// <summary>
    /// Shows pictures on the easels: those read deliberately (<see cref="ShowThumbnail"/>), and, when
    /// <paramref name="automatic"/> (only the app's own Starter Sky, FILE-02/DEV-02: your files' contents are read only
    /// when you ask), loads the rest two at a time off the main thread. Cloud placeholders and links are never read.
    /// </summary>
    public void PumpThumbnails(FsTree tree, int maxConcurrent, bool automatic = true)
    {
        lock (_thumbReady)
        {
            foreach (var (id, img) in _thumbReady)
            {
                if (!_items.TryGetValue(id, out var e) || e.Art.FaceSurface < 0) continue;
                var mat = new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(img), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic };
                e.Mesh.SetSurfaceOverrideMaterial(e.Art.FaceSurface, mat);
                ThumbnailsShown++;
            }

            _thumbReady.Clear();
        }

        if (!automatic || Volatile.Read(ref _thumbLoading) >= maxConcurrent) return;
        foreach (var (id, e) in _items)
        {
            if (Volatile.Read(ref _thumbLoading) >= maxConcurrent) break;
            if (id < 0 || e.Art.FaceSurface < 0 || _thumbRequested.Contains(id) || !tree.Contains(id)) continue;
            var n = tree[id];
            var ext = System.IO.Path.GetExtension(n.Name).ToLowerInvariant();
            if (n.Kind != NodeKind.File || (n.Flags & (NodeFlags.CloudPlaceholder | NodeFlags.ReparsePoint)) != 0 || ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp") || n.Size > 24L * 1024 * 1024) { _thumbRequested.Add(id); continue; }
            _thumbRequested.Add(id);
            Interlocked.Increment(ref _thumbLoading);
            var path = tree.FullPath(id);
            _ = Thumbnails.LoadAsync(path, 192, maxBytes: 8L * 1024 * 1024).ContinueWith(t =>
            {
                lock (_thumbReady)
                {
                    if (t.Result is { } img) _thumbReady.Add((id, img));
                }

                Interlocked.Decrement(ref _thumbLoading);
            });
        }
    }
}
