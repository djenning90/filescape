using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Places;

/// <summary>Colours of the Places vocabulary (PLACE-11, PLACE-14). Unknown listings never use a folder's own accent.</summary>
public static class PlacePalette
{
    public static readonly Color Pending = new(0.55f, 0.62f, 0.8f);
    public static readonly Color NotScanned = new(0.98f, 0.78f, 0.25f);
    public static readonly Color Denied = new(1f, 0.22f, 0.16f);
    public static readonly Color Failed = new(1f, 0.55f, 0.12f);
    public static readonly Color Cloud = new(0.85f, 0.92f, 1f);
    public static readonly Color Link = new(0.2f, 0.95f, 1f);
    public static readonly Color Up = new(0.92f, 0.95f, 1f);
    public static readonly Color Unlisted = new(0.62f, 0.68f, 0.86f);
    public static readonly Color Unavailable = new(0.62f, 0.6f, 0.66f);
    public static readonly Color LinkBlocked = new(0.35f, 0.55f, 0.6f);

    public static Color Accent(PlaceIdentity id, float value = 1f) => Color.FromHsv(id.Hue, 0.6f, value * 0.95f);

    /// <summary>The colour of a gate or vista: the target folder's accent when it is listed, otherwise its state colour.</summary>
    public static Color For(ListingState state, PlaceIdentity id) => state switch
    {
        ListingState.Pending => Pending,
        ListingState.NotScanned => NotScanned,
        ListingState.Denied => Denied,
        ListingState.Failed => Failed,
        ListingState.Cloud => Cloud,
        ListingState.Link => Link,
        ListingState.Unlisted => Unlisted,
        ListingState.Unavailable => Unavailable,
        ListingState.Empty => Accent(id, 0.65f),
        _ => Accent(id),
    };

    /// <summary>Gate shader style bits: 1 pulse (pending), 2 hatched (cannot be passed), 4 dim (empty).</summary>
    public static float Style(ListingState state) => state switch
    {
        ListingState.Pending => 1,
        ListingState.Unavailable => 2 + 4,
        ListingState.Empty => 4,
        _ => 0,
    };

    /// <summary>A gate's colour and style, with links as connections: open when followable, hatched when not (AD-18).</summary>
    public static (Color Color, float Style) ForGate(Gate g, PlaceIdentity id) =>
        g.IsLink ? (g.Followable ? (Link, 0f) : (LinkBlocked, 2f)) : (For(g.State, id), Style(g.State));
}

/// <summary>
/// Renders one <see cref="PlaceComposition"/> (AD-14): plaza, landmark, file towers, gates with membranes and signs,
/// routes, and vistas of neighbouring places. Geometry is batched (MultiMesh, AD-06); signs are pooled Label3D
/// nodes whose visibility is managed by distance so wide folders do not show hundreds of names at once (PLACE-05).
/// Only the local neighbourhood is ever built (PLACE-09).
/// </summary>
public partial class PlaceWorld : Node3D
{
    private readonly BoxMesh _box = new() { Size = Vector3.One };
    private readonly CylinderMesh _column = new() { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1, RadialSegments = 16, Rings = 1 };
    private readonly CylinderMesh _spire = new() { TopRadius = 0.08f, BottomRadius = 0.5f, Height = 1, RadialSegments = 12, Rings = 1 };
    private readonly ShaderMaterial _tower, _gate, _portal, _beacon, _street, _landmark, _vistaSpire, _plaza;
    private readonly StandardMaterial3D _halo;
    private readonly Dictionary<string, List<MultiMeshInstance3D>> _pools = [];
    private readonly List<Label3D> _signs = [];
    private readonly List<(Label3D Label, float Range, int Priority)> _managed = [];
    private readonly List<Node3D> _transient = [];
    private MeshInstance3D _plazaMesh = null!;
    private Font _font = null!;

    public int InstanceCount { get; private set; }
    public int SignCount => _managed.Count;
    public int VisibleSigns { get; private set; }

    /// <summary>The landmark's name sign text (checked by automation: PLACE-02).</summary>
    public string LandmarkSign { get; private set; } = "";

    /// <summary>Gate id → its sign, for checks and highlighting.</summary>
    public Dictionary<int, Label3D> GateSigns { get; } = [];

    public PlaceWorld()
    {
        Name = "PlaceWorld";
        _tower = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/tower.gdshader") };
        _gate = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/gate.gdshader") };
        _portal = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/portal.gdshader") };
        _beacon = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/beacon.gdshader") };
        _street = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/street.gdshader") };
        _street.SetShaderParameter("street_color", new Color(1, 1, 1));
        _street.SetShaderParameter("glow", 0.9f);
        _street.SetShaderParameter("pulse_speed", 5f);
        _landmark = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/landmark.gdshader") };
        _vistaSpire = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/landmark.gdshader") };
        _vistaSpire.SetShaderParameter("use_instance_color", true);
        _vistaSpire.SetShaderParameter("accent", new Color(1, 1, 1));
        _plaza = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/plaza.gdshader") };
        _halo = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = false };
    }

    public override void _Ready()
    {
        _font = (Font)Ui.Hud.BuildTheme().DefaultFont;
        _plazaMesh = new MeshInstance3D { Mesh = new PlaneMesh { Size = Vector2.One }, MaterialOverride = _plaza, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_plazaMesh);
    }

    /// <summary>Shows or hides one geometry batch (diagnostics: isolating which element produces an artefact).</summary>
    public void SetSlotVisible(string slot, bool visible)
    {
        if (_pools.TryGetValue(slot, out var pool))
        {
            foreach (var m in pool) m.Visible = visible;
        }
    }

    public void Clear()
    {
        foreach (var pool in _pools.Values)
        {
            foreach (var m in pool) m.QueueFree();
            pool.Clear();
        }

        foreach (var n in _transient) n.QueueFree();
        _transient.Clear();
        _nearHide.Clear();
        foreach (var s in _signs) s.Visible = false;
        _managed.Clear();
        GateSigns.Clear();
        InstanceCount = 0;
        if (_plazaMesh != null) _plazaMesh.Visible = false;
    }

    public void Build(FsTree tree, PlaceComposition c, Quality quality)
    {
        Clear();
        var accent = PlacePalette.Accent(c.Landmark.Identity);
        _plazaMesh.Visible = true;
        _plazaMesh.Scale = new Vector3(c.PlazaRadius * 2 + 2, 1, c.PlazaRadius * 2 + 2);
        _plazaMesh.Position = new Vector3(0, 0.012f, 0);
        _plaza.SetShaderParameter("accent", accent);
        _plaza.SetShaderParameter("radius", c.PlazaRadius);
        _plaza.SetShaderParameter("gate_ring", c.Tiers.Count > 0 ? c.Tiers[0].Radius - 2.5f : c.PlazaRadius - 3);
        _ringRadius = c.Tiers.Count > 0 ? c.Tiers[^1].Radius : 0;
        _landmark.SetShaderParameter("accent", accent);

        var towers = new InstanceBatch();
        var gates = new InstanceBatch();
        var portals = new InstanceBatch();
        var beacons = new InstanceBatch();
        var streets = new InstanceBatch();
        var spires = new InstanceBatch();
        var signIndex = 0;

        Label3D Sign(string text, Vector3 at, float pixel, Color color, float range, int priority, bool billboard = true, float? facingYaw = null)
        {
            if (signIndex >= _signs.Count)
            {
                var created = new Label3D
                {
                    Font = _font,
                    FontSize = 64,
                    OutlineSize = 14,
                    OutlineModulate = new Color(0.01f, 0.012f, 0.03f, 0.92f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Shaded = false,
                    DoubleSided = true,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                AddChild(created);
                _signs.Add(created);
            }

            var l = _signs[signIndex++];
            l.Text = text;
            l.Position = at;
            l.PixelSize = pixel;
            l.Modulate = color;
            l.Billboard = billboard && facingYaw == null ? BaseMaterial3D.BillboardModeEnum.FixedY : BaseMaterial3D.BillboardModeEnum.Disabled;
            l.Rotation = new Vector3(0, facingYaw ?? 0, 0);
            l.Visible = true;
            _managed.Add((l, range, priority));
            return l;
        }

        // ---- landmark: the folder's identity (PLACE-02)
        BuildLandmark(c.Landmark, accent);
        var folder = tree[c.FolderId];
        LandmarkSign = tree.DisplayName(c.FolderId);
        // The folder's name is a plaque at eye level in front of the landmark, facing the way you arrive.
        var plaque = new Vector3(0, 0, c.Landmark.Radius * 2.9f);
        // Sized for a walker (eye 1.75 m): readable from the arrival terrace, hidden when you stand next to it.
        var nameSign = Sign(LandmarkSign, plaque + new Vector3(0, 2.7f, 0), 0.0115f, accent.Lightened(0.55f), 1e9f, 0);
        var summarySign = Sign(PlaceText.Summary(tree, folder), plaque + new Vector3(0, 2.1f, 0), 0.0052f, new Color(0.82f, 0.88f, 1f), 1e9f, 0);
        _nearHide[nameSign] = 6.5f;
        _nearHide[summarySign] = 6.5f;
        // Lights scatter in volumetric fog as glowing spheres; gate lights must not, the landmark only a little.
        var light = new OmniLight3D { LightColor = accent, LightEnergy = 3.2f, OmniRange = 26, Position = new Vector3(0, 3.5f, 0), LightSpecular = 0.1f, ShadowEnabled = false, LightVolumetricFogEnergy = 0.15f };
        AddChild(light);
        _transient.Add(light);

        BuildIdentity(tree, c, accent, plaque, towers, gates, (t, at, px, col, rng, pri) => Sign(t, at, px, col, rng, pri));

        if (c.State is not (ListingState.Open or ListingState.Empty))
        {
            Sign($"Contents {Listing.Describe(c.State)}", plaque + new Vector3(0, 1.3f, 1.5f), 0.009f, StateColor(c.State), 1e9f, 0);
        }
        else if (c.State == ListingState.Empty)
        {
            Sign("This folder is empty — no files, no subfolders", plaque + new Vector3(0, 1.3f, 1.5f), 0.008f, new Color(0.8f, 0.86f, 1f), 1e9f, 0);
        }

        // ---- files: towers in type quarters (PLACE-03)
        foreach (var f in c.Files)
        {
            var n = tree[f.Id];
            towers.AddBox(Gd(f.Box.Center), Gd(f.Box.Size), Palette.Category[f.Category], StyleBits.For(n));
        }

        foreach (var q in c.Quarters.Where(q => q.Count >= 5))
        {
            var col = Palette.Category[q.Category];
            Arc(streets, q.SignPosition.Length() + 0.6f, q.StartDeg + 1, q.EndDeg - 1, 0.35f, col);
            Sign($"{PlaceText.CategoryName(q.Category)} · {q.Count:N0}", Gd(q.SignPosition) + new Vector3(0, 0.5f, 0), 0.0075f, col.Lightened(0.35f), 60, 2);
        }

        // ---- gates (PLACE-04/05/06/11)
        var singleRing = !c.IsWide;
        var gateIndex = 0;
        foreach (var g in c.Gates)
        {
            var child = tree[g.TargetId];
            var id = PlaceIdentity.For(child.Name);
            var (col, gateStyle) = PlacePalette.ForGate(g, id);
            AddGate(gates, portals, g, col, gateStyle);
            var signScale = singleRing ? Mathf.Clamp(g.Position.Length() / 30f, 1f, 1.8f) : 1f;
            // Alternate sign heights so neighbouring names never print over each other.
            var stagger = (c.Gates.Count > 6 && (gateIndex++ & 1) == 1) ? 1.7f : 0f;
            var label = Sign(child.Name + "\n" + PlaceText.GateSubtitle(tree, child, g), Gd(g.Position) + new Vector3(0, g.Height + 0.5f + stagger + g.Tier * 0.25f, 0), 0.014f * signScale, col.Lightened(0.45f), singleRing ? 200 : 55, 1);
            GateSigns[g.TargetId] = label;
            if (singleRing)
            {
                var gl = new OmniLight3D { LightColor = col, LightEnergy = 1.1f, OmniRange = 9, Position = Gd(g.Position - g.Outward * 1.5f) + new Vector3(0, 2.5f, 0), ShadowEnabled = false, LightSpecular = 0.2f, LightVolumetricFogEnergy = 0f };
                AddChild(gl);
                _transient.Add(gl);
            }
        }

        if (c.Gates.Count == 0 && c.State is ListingState.Open)
        {
            Sign("No subfolders here", new Vector3(0, 1.6f, -c.PlazaRadius * 0.6f), 0.014f, new Color(0.75f, 0.8f, 0.95f), 1e9f, 0);
        }

        foreach (var t in c.Tiers.Where(t => c.IsWide && t.Rise > 0))
        {
            // A terrace ring under each raised tier, so its gates stand on something.
            var steps = Math.Max(8, (int)((t.EndDeg - t.StartDeg + 8) * t.Radius / 160f));
            for (var i = 0; i < steps; i++)
            {
                var a0 = t.StartDeg - 4 + (t.EndDeg - t.StartDeg + 8) * i / steps;
                var a1 = t.StartDeg - 4 + (t.EndDeg - t.StartDeg + 8) * (i + 1) / steps;
                var p0 = Gd(PlaceGeometry.Direction(a0) * t.Radius);
                var p1 = Gd(PlaceGeometry.Direction(a1) * t.Radius);
                var along = p1 - p0;
                var len = along.Length() + 0.05f;
                var dir = along.Normalized();
                var side = dir.Cross(Vector3.Up).Normalized();
                gates.Add(dir * len, Vector3.Up * t.Rise, side * 7f, (p0 + p1) / 2 + new Vector3(0, t.Rise / 2, 0), accent * 0.35f, new Color(len, t.Rise, 7f, 4));
            }
        }

        foreach (var t in c.Tiers.Where(_ => c.IsWide))
        {
            Arc(streets, t.Radius - 3f, t.StartDeg, t.EndDeg, 0.45f, accent with { A = 1 } * 0.8f);
            var text = $"Tier {t.Index + 1} · {PlaceText.Short(t.FirstName)} – {PlaceText.Short(t.LastName)} · {t.Count} folders";
            Sign(text, Gd(PlaceGeometry.Direction(t.StartDeg - 4) * (t.Radius - 3)) + new Vector3(0, 2.2f, 0), 0.013f, accent.Lightened(0.5f), 1e9f, 0);
            Sign(text, Gd(PlaceGeometry.Direction(t.EndDeg + 4) * (t.Radius - 3)) + new Vector3(0, 2.2f, 0), 0.013f, accent.Lightened(0.5f), 1e9f, 0);
        }

        if (c.IsWide)
        {
            Sign($"{c.Gates.Count} folders on {c.Tiers.Count} tiers · A→Z from west to east, tier by tier outward\nNo vistas for this many folders · every folder is listed in Destinations",
                plaque + new Vector3(0, 1.3f, 1.5f), 0.0055f, new Color(0.8f, 0.86f, 1f), 1e9f, 0);
        }

        // ---- walkable terrain (AD-16): the arrival terrace and its stairs, the overlook with its ramp, rails and jump
        // blocks. Calm accent colours only: decoration must never look like a state (denied, pending, failed).
        foreach (var b in c.Terrain.Boxes)
        {
            var tint = b.Kind switch
            {
                TerrainKind.Platform => accent * 0.6f,
                TerrainKind.Step => accent * 0.5f,
                TerrainKind.JumpBlock => accent.Lightened(0.35f) * 0.75f,
                TerrainKind.Rail => accent * 0.9f,
                _ => default,
            };
            if (tint == default) continue;
            AddTerrainBox(gates, b, tint);
        }

        foreach (var r in c.Terrain.Ramps)
        {
            AddRamp(gates, r, accent * 0.55f);
        }

        // The overlook's panel: the place's summary, facing the plaza (JUMP-02).
        var ov = c.Overlook;
        var od = Gd(PlaceGeometry.Direction(ov.AngleDeg));
        var panelAt = Gd(ov.Top) + od * (ov.HalfSize - 0.35f) + new Vector3(0, 1.75f, 0);
        var faceYaw = Mathf.Atan2(-od.X, -od.Z);
        var ovTitle = Sign($"OVERLOOK · {tree.DisplayName(c.FolderId)}", panelAt + new Vector3(0, 0.5f, 0), 0.005f, accent.Lightened(0.6f), 40, 2, facingYaw: faceYaw);
        var ovText = Sign(PlaceText.OverlookText(tree, folder, c), panelAt, 0.0034f, new Color(0.84f, 0.9f, 1f), 40, 2, facingYaw: faceYaw);
        _nearHide[ovTitle] = 2.2f; // readable from the top of the ramp; out of the way at the rail
        _nearHide[ovText] = 2.2f;
        var rampSign = ov.RampFoot + System.Numerics.Vector2.Normalize(new System.Numerics.Vector2(ov.Top.X, ov.Top.Z) - ov.RampFoot) * 2.2f;
        Sign("▲ overlook", new Vector3(rampSign.X, 1.5f, rampSign.Y), 0.004f, accent.Lightened(0.4f), 22, 2);

        // ---- the way back (PLACE-07)
        if (c.ParentGate is { } pg)
        {
            AddGate(gates, portals, pg, PlacePalette.Up, 0);
            Sign($"▲ Up to {tree.DisplayName(pg.TargetId)}", Gd(pg.Position) + new Vector3(0, pg.Height + 0.5f, 0), 0.013f, PlacePalette.Up, 1e9f, 0);
        }

        // ---- routes and vistas (PLACE-09, AD-14: symbolic distance)
        foreach (var a in c.Avenues)
        {
            var target = tree[a.TargetId];
            var isParent = c.ParentGate?.TargetId == a.TargetId;
            var col = isParent ? PlacePalette.Up * 0.13f : PlacePalette.For(Listing.Of(target), PlaceIdentity.For(target.Name)) * 0.55f;
            var along = 0f;
            for (var i = 1; i < a.Points.Length; i++)
            {
                var p0 = Gd(a.Points[i - 1]) with { Y = 0.03f };
                var p1 = Gd(a.Points[i]) with { Y = 0.03f };
                streets.AddSegment(p0, p1, i == 1 ? 0.5f : 0.8f, 0.03f, col, along);
                along += p0.DistanceTo(p1);
            }
        }

        foreach (var v in c.ParentVista is { } pv ? c.Vistas.Append(pv) : c.Vistas)
        {
            BuildVista(tree, v, towers, beacons, spires, streets, (t, a, px, col, rng, pri, bb) => Sign(t, a, px, col, rng, pri, bb), isParent: v == c.ParentVista);
        }

        Emit("towers", towers, _box, _tower, true);
        Emit("gates", gates, _box, _gate, true);
        Emit("portals", portals, _box, _portal, false);
        Emit("beacons", beacons, _column, _beacon, false);
        Emit("streets", streets, _box, _street, false);
        Emit("spires", spires, _spire, _vistaSpire, false);
        InstanceCount = towers.Count + gates.Count + portals.Count + beacons.Count + streets.Count + spires.Count;
        for (var i = signIndex; i < _signs.Count; i++) _signs[i].Visible = false;
    }

    /// <summary>
    /// Identity dressing (AD-20, IDENT-02): a banner over the landmark naming what kind of place this is, the project
    /// core's plinths and role labels, and the hub's halo of rings. Style stays futuristic; state stays in the gates.
    /// </summary>
    private void BuildIdentity(FsTree tree, PlaceComposition c, Color accent, Vector3 plaque, InstanceBatch towers, InstanceBatch gates,
        Func<string, Vector3, float, Color, float, int, Label3D> sign)
    {
        var lm = c.Landmark;
        switch (c.Kind.Kind)
        {
            case PlaceKind.Project:
            {
                sign("⚙ SOFTWARE PROJECT", plaque + new Vector3(0, 3.35f, 0), 0.0062f, new Color(0.55f, 1f, 0.85f), 1e9f, 0);
                var coreColor = new Color(0.45f, 1f, 0.8f);
                foreach (var f in c.Files.Where(f => f.Core))
                {
                    var b = f.Box;
                    // A lit plinth under each core file, and its role.
                    gates.Add(new Vector3(2.1f, 0, 0), new Vector3(0, 0.14f, 0), new Vector3(0, 0, 2.1f), new Vector3(b.Center.X, 0.07f, b.Center.Z), coreColor * 0.9f, new Color(2.1f, 0.14f, 2.1f, 0));
                    var role = PlaceKinds.IsReadme(tree[f.Id].Name) ? "README · start here" : "manifest";
                    sign($"{tree[f.Id].Name}\n{role}", new Vector3(b.Center.X, b.Max.Y + 0.6f, b.Center.Z), 0.0048f, coreColor, 45, 1);
                }

                break;
            }

            case PlaceKind.Hub:
                sign("LOCATIONS · every drive and Linux distribution is a gate", plaque + new Vector3(0, 3.35f, 0), 0.0058f, new Color(0.85f, 0.9f, 1f), 1e9f, 0);
                for (var i = 0; i < 3; i++)
                {
                    var m = new MeshInstance3D
                    {
                        Mesh = new TorusMesh { InnerRadius = 3.2f + i * 1.6f, OuterRadius = 3.32f + i * 1.6f, Rings = 96 },
                        Position = new Vector3(0, 4.5f + i * 2.4f, 0),
                        MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = accent * (1.6f - i * 0.3f) },
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    };
                    AddChild(m);
                    _transient.Add(m);
                }

                break;
            case PlaceKind.LocationRoot:
                var folder = tree[c.FolderId];
                var text = folder.Location == LocationKind.Wsl ? $"WSL · LINUX · {folder.Name} · names are case-sensitive" : $"{c.Kind.Label.ToUpperInvariant()} · {folder.Name}";
                sign(text, plaque + new Vector3(0, 3.35f, 0), 0.0058f, new Color(0.85f, 0.9f, 1f), 1e9f, 0);
                break;
        }
    }

    /// <summary>A terrain box (oriented like gates: width along the tangent, depth along the outward direction).</summary>
    private static void AddTerrainBox(InstanceBatch batch, TerrainBox b, Color color)
    {
        var a = Mathf.DegToRad(b.AngleDeg);
        var v = new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a));
        var u = new Vector3(-v.Z, 0, v.X);
        var w = b.HalfExtents.X * 2;
        var d = b.HalfExtents.Y * 2;
        var h = b.Top - b.Bottom;
        batch.Add(u * w, Vector3.Up * h, v * d, new Vector3(b.Center.X, b.Bottom + h / 2, b.Center.Y), color, new Color(w, h, d, 0));
    }

    /// <summary>A ramp as a sloped slab with lit edges.</summary>
    private static void AddRamp(InstanceBatch batch, TerrainRamp r, Color color)
    {
        var start = new Vector3(r.Start.X, r.StartHeight, r.Start.Y);
        var end = new Vector3(r.End.X, r.EndHeight, r.End.Y);
        var along = end - start;
        var flat = new Vector3(along.X, 0, along.Z).Normalized();
        var side = flat.Cross(Vector3.Up).Normalized();
        var normal = side.Cross(along).Normalized();
        if (normal.Y < 0) normal = -normal;
        const float thick = 0.18f;
        var centre = (start + end) / 2 - normal * (thick / 2);
        batch.Add(side * (r.HalfWidth * 2), normal * thick, along, centre, color, new Color(r.HalfWidth * 2, thick, along.Length(), 0));
    }

    /// <summary>Vista names repeat the gate names, so on the plaza they stay hidden; from above or beyond the gates they show.</summary>
    private const int VistaPriority = 3;

    private float _ringRadius;

    /// <summary>Approximate screen rectangles of the visible in-world signs (so overlay labels avoid them, VISI-01).</summary>
    public IEnumerable<Rect2> VisibleSignRects(Camera3D camera)
    {
        foreach (var (label, _, _) in _managed)
        {
            if (!label.Visible || label.Modulate.A < 0.3f) continue;
            var aabb = label.GetAabb();
            var xf = label.GlobalTransform;
            if (camera.IsPositionBehind(xf.Origin)) continue;
            // Billboarded signs face the camera: project the centre and the box's half-extent at that depth.
            var centre = xf * aabb.GetCenter();
            var right = camera.GlobalTransform.Basis.X * aabb.Size.X * 0.5f;
            var up = Vector3.Up * aabb.Size.Y * 0.5f;
            var c = camera.UnprojectPosition(centre);
            var dx = (camera.UnprojectPosition(centre + right) - c).Length();
            var dy = (camera.UnprojectPosition(centre + up) - c).Length();
            yield return new Rect2(c.X - dx, c.Y - dy, dx * 2, dy * 2);
        }
    }

    /// <summary>Signs that hide when the camera is closer than this (horizontally): they would fill the view.</summary>
    private readonly Dictionary<Label3D, float> _nearHide = [];

    /// <summary>Distance-managed signs (PLACE-05): far gate names hide; at most <paramref name="maxVisible"/> managed signs show.</summary>
    public void UpdateSigns(Vector3 camera, int maxVisible = 70)
    {
        var shown = 0;
        var overview = camera.Y > 14f || new Vector2(camera.X, camera.Z).Length() > _ringRadius + 4f;
        foreach (var (label, range, priority) in _managed
                     .Select(m => (m.Label, m.Range, m.Priority, D: m.Label.GlobalPosition.DistanceTo(camera)))
                     .OrderBy(m => m.Priority == 0 ? 0 : 1).ThenBy(m => m.D)
                     .Select(m => (m.Label, m.Range, m.Priority)))
        {
            var visible = priority == VistaPriority ? overview : label.GlobalPosition.DistanceTo(camera) <= range && (priority == 0 || shown < maxVisible);
            if (visible && _nearHide.TryGetValue(label, out var near))
            {
                var flat = new Vector2(label.GlobalPosition.X - camera.X, label.GlobalPosition.Z - camera.Z).Length();
                visible = flat > near;
                label.Modulate = label.Modulate with { A = Mathf.Clamp((flat - near) / 4f, 0, 1) };
            }
            label.Visible = visible;
            if (visible) shown++;
        }

        VisibleSigns = shown;
    }

    private void BuildLandmark(Landmark lm, Color accent)
    {
        var h = lm.Height;
        var r = lm.Radius;
        void Mesh(Mesh mesh, Vector3 pos, Vector3 scale, Material mat)
        {
            var m = new MeshInstance3D { Mesh = mesh, Position = pos, Scale = scale, MaterialOverride = mat };
            AddChild(m);
            _transient.Add(m);
        }

        var haloMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = accent * 1.8f };
        var baseMat = new StandardMaterial3D { AlbedoColor = new Color(0.025f, 0.028f, 0.04f), Metallic = 0.7f, Roughness = 0.25f };
        Mesh(new CylinderMesh { TopRadius = r * 2.6f, BottomRadius = r * 2.8f, Height = 0.3f }, new Vector3(0, 0.15f, 0), Vector3.One, baseMat);
        Mesh(new TorusMesh { InnerRadius = r * 2.62f, OuterRadius = r * 2.72f, Rings = 64 }, new Vector3(0, 0.3f, 0), Vector3.One, haloMat);
        switch (lm.Identity.Form)
        {
            case 0: // spire
                Mesh(new CylinderMesh { TopRadius = 0.06f, BottomRadius = r, Height = h, RadialSegments = 6 }, new Vector3(0, h / 2, 0), Vector3.One, _landmark);
                Mesh(new TorusMesh { InnerRadius = r * 1.5f, OuterRadius = r * 1.62f, Rings = 48 }, new Vector3(0, h * 0.42f, 0), Vector3.One, haloMat);
                break;
            case 1: // obelisk with a floating halo
                Mesh(new BoxMesh { Size = new Vector3(r * 1.25f, h * 0.82f, r * 1.25f) }, new Vector3(0, h * 0.41f, 0), Vector3.One, _landmark);
                Mesh(new TorusMesh { InnerRadius = r * 1.7f, OuterRadius = r * 1.85f, Rings = 48 }, new Vector3(0, h * 0.95f, 0), Vector3.One, haloMat);
                break;
            default: // ring tower
                Mesh(new CylinderMesh { TopRadius = r * 0.32f, BottomRadius = r * 0.4f, Height = h }, new Vector3(0, h / 2, 0), Vector3.One, _landmark);
                for (var i = 0; i < 3; i++)
                {
                    var rr = r * (1.7f - 0.35f * i);
                    Mesh(new TorusMesh { InnerRadius = rr, OuterRadius = rr + 0.14f, Rings = 48 }, new Vector3(0, h * (0.35f + 0.25f * i), 0), Vector3.One, haloMat);
                }

                break;
        }
    }

    private static void AddGate(InstanceBatch frames, InstanceBatch portals, Gate g, Color color, float style)
    {
        var o = Gd(g.Outward);
        var t = Gd(g.Tangent);
        var up = Vector3.Up;
        var basePos = Gd(g.Position);
        const float pillar = 0.55f;
        var depth = 0.9f;
        foreach (var side in new[] { -1f, 1f })
        {
            var c = basePos + t * side * (g.Width / 2 - pillar / 2) + up * (g.Height / 2);
            frames.Add(t * pillar, up * g.Height, o * depth, c, color, new Color(pillar, g.Height, depth, style));
        }

        var lintel = basePos + up * (g.Height - 0.35f);
        frames.Add(t * (g.Width + 0.6f), up * 0.7f, o * (depth + 0.15f), lintel, color, new Color(g.Width + 0.6f, 0.7f, depth + 0.15f, style));
        var mw = g.Width - pillar * 2;
        var mh = g.Height - 0.7f;
        portals.Add(t * mw, up * mh, o * 0.02f, basePos + up * (mh / 2), color with { A = g.State == ListingState.Link ? 0.9f : 0.75f }, new Color(mw, mh, 0.02f, style));
    }

    private void BuildVista(FsTree tree, Vista v, InstanceBatch towers, InstanceBatch beacons, InstanceBatch spires, InstanceBatch streets,
        Func<string, Vector3, float, Color, float, int, bool, Label3D> sign, bool isParent)
    {
        var col = isParent ? PlacePalette.Accent(v.Identity) : PlacePalette.For(v.State, v.Identity);
        var center = Gd(v.Center);
        spires.Add(new Vector3(2.4f, 0, 0), new Vector3(0, v.LandmarkHeight, 0), new Vector3(0, 0, 2.4f), center + new Vector3(0, v.LandmarkHeight / 2, 0), col, new Color(0, 0, 0, 0));
        foreach (var t in v.Towers)
        {
            towers.AddBox(Gd(t.BaseCenter) + new Vector3(0, t.Height / 2, 0), new Vector3(t.Footprint, t.Height, t.Footprint), Palette.Category[t.Category], 0);
        }

        foreach (var b in v.Beacons)
        {
            var bc = PlacePalette.For(b.State, PlaceIdentity.For(tree[b.Id].Name));
            beacons.Add(new Vector3(0.9f, 0, 0), new Vector3(0, b.Height * 2.2f, 0), new Vector3(0, 0, 0.9f), Gd(b.Position) + new Vector3(0, b.Height * 1.1f, 0), bc with { A = 0.9f }, new Color(0, 0, 0, 0));
        }

        if (Listing.IsUnknown(v.State))
        {
            // Unknown contents: a veiled column, never a bare (empty-looking) platform (PLACE-11).
            beacons.Add(new Vector3(v.Radius * 1.4f, 0, 0), new Vector3(0, 16, 0), new Vector3(0, 0, v.Radius * 1.4f), center + new Vector3(0, 8, 0), col with { A = 0.55f }, new Color(0, 0, 0, 0));
        }
        else if (v.State == ListingState.Empty)
        {
            Arc(streets, v.Radius * 0.6f, 0, 360, 0.3f, col, center);
        }

        var name = isParent ? tree.DisplayName(v.FolderId) : tree[v.FolderId].Name;
        var sub = Listing.IsUnknown(v.State) || v.State == ListingState.Empty
            ? Listing.Describe(v.State)
            : $"{v.FileCount:N0} files · {v.FolderCount:N0} folders" + (v.HiddenBeacons > 0 ? $" ({v.Beacons.Count} shown)" : "");
        sign(name + "\n" + sub, center + new Vector3(0, v.LandmarkHeight + 1.5f, 0), 0.026f, col.Lightened(0.2f) with { A = 0.85f }, 1e9f, VistaPriority, true);
    }

    /// <summary>An arc of street segments at radius <paramref name="r"/> between two angles (clockwise from north).</summary>
    private static void Arc(InstanceBatch streets, float r, float fromDeg, float toDeg, float width, Color color, Vector3? center = null)
    {
        var c = center ?? Vector3.Zero;
        var steps = Math.Max(2, (int)(Math.Abs(toDeg - fromDeg) * r / 180f));
        var prev = c + Gd(PlaceGeometry.Direction(fromDeg) * r) with { Y = 0.04f };
        var along = 0f;
        for (var i = 1; i <= steps; i++)
        {
            var next = c + Gd(PlaceGeometry.Direction(fromDeg + (toDeg - fromDeg) * i / steps) * r) with { Y = 0.04f };
            streets.AddSegment(prev, next, width, 0.02f, color, along);
            along += prev.DistanceTo(next);
            prev = next;
        }
    }

    private static Color StateColor(ListingState s) => PlacePalette.For(s, default);

    private void Emit(string slot, InstanceBatch batch, Mesh mesh, Material material, bool shadows)
    {
        if (!_pools.TryGetValue(slot, out var pool))
        {
            pool = [];
            _pools[slot] = pool;
        }

        batch.BuildInto(pool, this, mesh, material, shadows);
    }

    private static Vector3 Gd(NVector3 v) => new(v.X, v.Y, v.Z);
}

/// <summary>Texts used on signs and in the HUD for Places.</summary>
public static class PlaceText
{
    public static string Summary(FsTree tree, FsNode folder)
    {
        var state = Listing.Of(folder);
        var parts = $"{folder.DirectFileCount:N0} files · {folder.DirectDirCount:N0} folders · {SizeFormat.Short(folder.SubtreeBytes)} in all";
        if (folder.ScanState == DirScanState.Pending || Listing.IsUnknown(state)) parts += " so far";
        return state is ListingState.Open or ListingState.Empty ? parts : parts + " · " + Listing.Describe(state);
    }

    /// <summary>A link's status in a few words (VISI-02): a connection, not an error, unless it is broken or unsupported.</summary>
    public static string LinkShort(FsTree tree, FsNode link)
    {
        if (link.Link is not { } li) return "link";
        return li.Status switch
        {
            LinkStatus.Resolved when li.TargetId >= 0 && tree.Contains(li.TargetId) => $"to {tree.DisplayName(li.TargetId)}",
            LinkStatus.Unresolved => li.Kind is LinkKind.LinuxSymlink or LinkKind.Other ? "can't follow" : li.KindName,
            LinkStatus.Broken => "broken link",
            LinkStatus.Denied => "can't read link",
            LinkStatus.Unavailable => "target unavailable",
            LinkStatus.Unsupported => li.Kind == LinkKind.LinuxSymlink ? "Linux link · can't follow" : "can't follow",
            _ => "link",
        };
    }

    public static string GateSubtitle(FsTree tree, FsNode child, Gate g)
    {
        var state = g.State;
        if (child.Location != LocationKind.None)
        {
            var kind = child.Location switch { LocationKind.Wsl => "WSL · Linux", LocationKind.Drive => "Windows drive", LocationKind.Network => "network", _ => "volume" };
            return state is ListingState.Open or ListingState.Empty ? kind : $"{kind} · {Listing.Describe(state)}";
        }

        if (state == ListingState.Link) return LinkShort(tree, child);
        if (Listing.IsUnknown(state) && state != ListingState.Partial) return Listing.Describe(state);
        if (state == ListingState.Empty) return "empty";
        var text = $"{child.SubtreeFileCount:N0} files · {SizeFormat.Short(child.SubtreeBytes)}";
        if (g.ChainLength >= 3) text += $"\nchain of {g.ChainLength} single folders";
        if (state == ListingState.Partial) text += " · partly listed";
        return text;
    }

    /// <summary>The overlook panel (JUMP-02): what this place holds and how it is laid out, for orientation.</summary>
    public static string OverlookText(FsTree tree, FsNode folder, PlaceComposition c)
    {
        var lines = new List<string> { Summary(tree, folder) };
        var top = c.Quarters.OrderByDescending(q => q.Count).Take(3).Select(q => $"{CategoryName(q.Category)} {q.Count:N0}");
        if (c.Quarters.Count > 0) lines.Add("Files by type: " + string.Join(" · ", top) + (c.Quarters.Count > 3 ? " · …" : ""));
        lines.Add(c.Gates.Count == 0 ? "No folders lead on from here" : $"{c.Gates.Count:N0} gate{(c.Gates.Count == 1 ? "" : "s")} ahead, to the north" + (c.IsWide ? $", on {c.Tiers.Count} tiers A→Z" : ""));
        if (c.ParentGate != null) lines.Add($"▲ {tree.DisplayName(c.ParentGate.TargetId)} is behind the arrival terrace");
        return string.Join("\n", lines);
    }

    public static string CategoryName(FileCategory c) => c switch
    {
        FileCategory.Document => "Documents",
        FileCategory.Image => "Images",
        FileCategory.Audio => "Audio",
        FileCategory.Video => "Video",
        FileCategory.Archive => "Archives",
        FileCategory.Code => "Code",
        FileCategory.Executable => "Programs",
        FileCategory.Data => "Data",
        _ => "Other files",
    };

    public static string Short(string name) => name.Length <= 14 ? name : name[..13] + "…";
}
