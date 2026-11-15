using FileScape.App.Places;
using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// The repository workbench (PLANET-29, AD-50). On a repository world the overlook is a low workbench (one plain jump).
/// <list type="bullet">
/// <item><b>Land on it</b> (or press E there, or click it): the project's README and key entry points come into view —
/// they rise out of the bench as a slow ring of glowing cards, each named with what it is for (click one to select it,
/// double-click to go to it), and they are listed on the bench's panel.</item>
/// <item><b>Spin on it</b> (jump from the bench and twirl in the air, Space twice): the README opens in the preview.</item>
/// </list>
/// Movement only reveals, selects and previews. Opening a file with Windows (an editor, an IDE) stays a deliberate
/// Enter or button press through the usual confirmations; nothing runs and nothing changes.
/// <para>
/// The bench is the composition's overlook (<see cref="PlanetComposition.Overlook"/>) on a repository world; the key
/// locations come from <see cref="ProjectBench.KeyLocations"/> (names only, FS-01). The cards are built lazily on the
/// first reveal and live in the current planet's frame, so they are rebuilt with the world (<see cref="ResetBench"/>).
/// </para>
/// </summary>
public partial class PlanetWorld
{
    // ---- the bench's look and timing (by eye, 2026-11-14; rationale not recorded unless stated)
    /// <summary>The bench's colour: a mint green (its panel's border, the reveal's burst; the lamp is a paler mint).</summary>
    private static readonly Color BenchColor = new(0.4f, 1f, 0.75f);

    /// <summary>"Near the bench": within this far of its edge, metres (on it, jumping from it, or just stepped off).</summary>
    private const float NearBenchMetres = 2.5f;

    /// <summary>The reveal plays over this long, seconds (the cards rise and the ring opens out).</summary>
    private const float BenchRevealSeconds = 0.9f;

    /// <summary>The bench lamp's energy standing on the bench and off it, and how fast it changes, per second.</summary>
    private const float BenchLampOnEnergy = 2.2f, BenchLampOffEnergy = 1.2f, BenchLampRatePerSecond = 4f;

    /// <summary>The ring of cards turns at this rate, radians per second (one turn in ≈ 35 s).</summary>
    private const float TileSpinRadiansPerSecond = 0.18f;

    /// <summary>A card is picked by the mouse within this radius of its centre, metres (a little more than the README's half-height).</summary>
    private const float TilePickRadiusMetres = 0.42f;

    private IReadOnlyList<BenchEntry> _benchEntries = [];
    private bool _benchRevealed;
    private float _benchReveal01;
    private bool _wasOnBench;
    private bool _jumpFromBench;
    private Node3D? _benchTiles;
    private readonly List<(int Id, MeshInstance3D Tile)> _tiles = [];
    private float _tileSpin;
    private OmniLight3D? _benchLamp;
    private PanelContainer _benchPanel = null!;
    private Label _benchTitle = null!;
    private ItemList _benchList = null!;
    private readonly List<int> _benchIds = [];
    private int _benchPreviewId = -1;

    /// <summary>Within a few metres of the bench (on it, jumping from it, or just stepped off).</summary>
    private bool NearBench() =>
        OnBenchWorld && _current!.Comp.Overlook is { } ov && PlanetShape.Angle(_walker.Up, ov.Direction) * _current.Comp.Shape.Radius < ov.Radius + NearBenchMetres;

    /// <summary>The current world is a repository with its workbench.</summary>
    public bool OnBenchWorld => _character.Kind == WorldKind.Repository && _current?.Comp.Overlook != null;

    /// <summary>The bench has shown the project's key locations (probes).</summary>
    public bool BenchRevealed => _benchRevealed;

    public IReadOnlyList<BenchEntry> BenchEntries => _benchEntries;

    public bool BenchPanelOpen => _benchPanel.Visible;

    /// <summary>The bench from where you stand: signed bearing from the camera's heading (degrees, + to the right) and
    /// distance to its edge along the ground (metres); (0, −1) when there is no bench (probes steer by it).</summary>
    public (float Degrees, float Metres) BenchBearing()
    {
        if (!OnBenchWorld || _current!.Comp.Overlook is not { } ov) return (0, -1);
        var r = _current.Comp.Shape.Radius;
        var dist = PlanetShape.Angle(_walker.Up, ov.Direction) * r - ov.Radius;
        return (CameraTurnTo(ov.Direction), dist);
    }

    /// <summary>Standing on the workbench deck now (on the ground, within 0.3 m of the deck's height and of its edge: the
    /// same test as the overlook's top in <see cref="UpdateWalking"/>).</summary>
    public bool StandingOnBench() =>
        OnBenchWorld && _current!.Comp.Overlook is { } ov && _walker.Grounded && _walker.Support.Value > ov.Top - 0.3f &&
        PlanetShape.Angle(_walker.Up, ov.Direction) * _current.Comp.Shape.Radius < ov.Radius + 0.3f;

    /// <summary>Builds the bench's panel (hidden until a reveal). Its list ignores clicks from a stale tree or while the overview is open.</summary>
    private void BuildBenchPanel(Control root)
    {
        _benchPanel = new PanelContainer { Visible = false };
        var style = PanelStyle();
        style.BorderColor = BenchColor with { A = 0.6f };
        _benchPanel.AddThemeStyleboxOverride("panel", style);
        // Bottom right, under the selection card (which sits top right).
        _benchPanel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _benchPanel.OffsetLeft = -420; _benchPanel.OffsetRight = -12; _benchPanel.OffsetTop = -296; _benchPanel.OffsetBottom = -66;
        root.AddChild(_benchPanel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        _benchPanel.AddChild(v);
        _benchTitle = MakeLabel(17, new Color(0.75f, 1f, 0.88f), HorizontalAlignment.Left, outline: 0);
        _benchList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Single, FixedIconSize = new Vector2I(18, 18), FocusMode = Control.FocusModeEnum.None };
        _benchList.ItemSelected += i =>
        {
            if (_universe != null || !ReferenceEquals(_s.Tree, _tree) || i < 0 || i >= _benchIds.Count) return;
            _s.Select(_benchIds[(int)i]);
            _audio.Select();
        };
        _benchList.ItemActivated += i =>
        {
            if (_universe != null || !ReferenceEquals(_s.Tree, _tree) || i < 0 || i >= _benchIds.Count) return;
            BenchGoTo(_benchIds[(int)i]);
        };
        var hint = new Label
        {
            Text = "Click: select (the card shows it) · double-click: go to it · spin on the bench (Space, Space) or E: read the README · Enter opens the selected file with Windows",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.86f, 0.85f));
        v.AddChild(_benchTitle);
        v.AddChild(_benchList);
        v.AddChild(hint);
    }

    /// <summary>
    /// A new world: forget the last bench and read this one's project (names only). On a recompose of the same world (the
    /// listing changed under you) the reveal stays: only the entries are re-read and the cards rebuilt.
    /// </summary>
    private void ResetBench(FsTree tree, bool recompose = false)
    {
        var entries = OnBenchWorld && _current != null ? ProjectBench.KeyLocations(tree, _current.FolderId, _s.IsVisible) : [];
        _benchTiles?.QueueFree();
        _benchTiles = null;
        _tiles.Clear();
        if (recompose && _benchRevealed && OnBenchWorld)
        {
            _benchEntries = entries;
            FillBench(tree);
            return;
        }

        _benchRevealed = false;
        _benchReveal01 = 0;
        _wasOnBench = false;
        _jumpFromBench = false;
        _benchPreviewId = -1;
        _benchLamp?.QueueFree();
        _benchLamp = null;
        _benchPanel.Visible = false;
        _benchEntries = entries;
    }

    /// <summary>Per frame while walking: landing on the bench reveals; a twirl from it reads the README.</summary>
    private void UpdateBench(float dt, Core.Movement.JumpEventKind ev)
    {
        if (!OnBenchWorld) return;
        var onBench = StandingOnBench();
        if (onBench && !_wasOnBench) RevealBench("landed");
        // A jump leaves the ground in the frame it is reported: whether it left from the bench is last frame's answer.
        if (ev == Core.Movement.JumpEventKind.Jump) _jumpFromBench = _wasOnBench || onBench;
        _wasOnBench = onBench;
        if (ev == Core.Movement.JumpEventKind.AirJump && _jumpFromBench)
        {
            _jumpFromBench = false;
            ReadReadme("spin");
        }

        if (ev == Core.Movement.JumpEventKind.Land && !onBench) _jumpFromBench = false;
        if (_benchRevealed) _benchReveal01 = MathF.Min(1, _benchReveal01 + dt / BenchRevealSeconds);
        UpdateBenchTiles(dt);
        if (_benchLamp != null) _benchLamp.LightEnergy = Mathf.Lerp(_benchLamp.LightEnergy, onBench ? BenchLampOnEnergy : BenchLampOffEnergy, 1 - MathF.Exp(-BenchLampRatePerSecond * dt));
    }

    /// <summary>
    /// Shows the project's key locations: the bench panel lists them, each gets a trail of light from the bench to where
    /// it stands on this planet (a file, its pile, or the launcher of the folder it is in), and they are named.
    /// </summary>
    public void RevealBench(string how)
    {
        if (!OnBenchWorld || _current == null || _s.Tree is not { } tree) return;
        var first = !_benchRevealed;
        _benchRevealed = true;
        if (first)
        {
            _benchReveal01 = 0;
            _audio.Inspect();
            if (_current.Comp.Overlook is { } ov)
            {
                var top = Gd(_current.Comp.Surface.PointAt(ov.Direction, ov.Top + 0.2f));
                _landFx.Burst(top, Gd(ov.Direction), BenchColor);
                // The lamp starts dim (0.2) and brightens towards its on/off energy (UpdateBench); 7 m range, 1.6 m over the deck.
                _benchLamp = new OmniLight3D { Name = "BenchLamp", LightColor = new Color(0.55f, 1f, 0.82f), LightEnergy = 0.2f, OmniRange = 7f, Position = Gd(_current.Comp.Surface.PointAt(ov.Direction, ov.Top + 1.6f)) };
                AddChild(_benchLamp);
            }

            ShowHint(_benchEntries.Count > 0
                ? "The workbench shows this project. Spin here (Space, Space) or press E to read its README."
                : "The workbench: this project has no README or entry points that FileScape recognises by name.");
        }

        FillBench(tree);
        _benchPanel.Visible = true;
        _titleTime = MathF.Min(_titleTime, 0.4f); // the arrival title makes way for the cards' names
        GD.Print($"[planets] bench revealed ({how}): {string.Join(", ", _benchEntries.Select(e => $"{tree[e.NodeId].Name} ({e.Role})"))}");
    }

    /// <summary>Fills the bench's panel with the key locations: name, why it matters, and where it stands if not here.</summary>
    private void FillBench(FsTree tree)
    {
        _benchList.Clear();
        _benchIds.Clear();
        var folder = tree.DisplayName(_current!.FolderId);
        _benchTitle.Text = $"⚙ {folder} · the workbench";
        foreach (var e in _benchEntries)
        {
            if (!tree.Contains(e.NodeId)) continue;
            var n = tree[e.NodeId];
            var where = BenchWhere(e.NodeId) is { } w ? w.Label : "";
            _benchList.AddItem($"{n.Name}  ·  {e.Why}{where}", FileIcons.Get(FileIcons.For(n)));
            _benchList.SetItemTooltip(_benchList.ItemCount - 1, tree.FullPath(e.NodeId));
            _benchIds.Add(e.NodeId);
        }

        if (_benchEntries.Count == 0) _benchList.AddItem("Nothing recognised by name here.", null, selectable: false);
    }

    /// <summary>Where a key location stands on this planet: the file, its pile, or the launcher of the folder holding it
    /// (the anchors are the same as the names': 1.25 × scale + 0.3 m over an object, <see cref="PadNameHeightMetres"/> over a pad).</summary>
    private (Godot.Vector3 At, string Label)? BenchWhere(int id)
    {
        if (_current == null || _s.Tree is not { } tree || !tree.Contains(id)) return null;
        var surf = _current.Comp.Surface;
        if (_current.Comp.Items.FirstOrDefault(i => i.NodeId == id) is { } item) return (Gd(surf.PointAt(item.Direction, 1.25f * item.Scale + 0.3f)), "");
        if (_current.Comp.Launchers.FirstOrDefault(l => l.TargetId == id) is { } pad) return (Gd(surf.PointAt(pad.Direction, PadNameHeightMetres)), "");
        var n = tree[id];
        if (n.Kind == NodeKind.File && n.ParentId == _current.FolderId &&
            _current.Comp.Items.FirstOrDefault(i => i.Kind == PlanetItemKind.Pile && i.Category == FileCategories.Of(n.Name)) is { } pile)
        {
            return (Gd(surf.PointAt(pile.Direction, 1.25f * pile.Scale + 0.3f)), " · in a pile");
        }

        if (n.ParentId >= 0 && _current.Comp.Launchers.FirstOrDefault(l => l.TargetId == n.ParentId) is { } via) return (Gd(surf.PointAt(via.Direction, PadNameHeightMetres)), $" · via {tree[n.ParentId].Name}");
        return null;
    }

    /// <summary>Where a key location's card floats above the bench (rising and opening out as the reveal plays).</summary>
    private Godot.Vector3 TileAt(int index, int count)
    {
        var ov = _current!.Comp.Overlook;
        var surf = _current.Comp.Surface;
        var up = Gd(ov.Direction);
        var (t1, t2) = PlanetShape.Tangents(ov.Direction);
        // Each card rises in turn, 0.06 (in the × 1.4 progress) after the one before. With the reveal capped at 1, cards 0–6
        // rise fully; an eighth card or later stops just short (rise = 1.4 − 0.06 × index), which is barely visible.
        var rise = Ease(Mathf.Clamp(_benchReveal01 * 1.4f - index * 0.06f, 0, 1));
        var a = _tileSpin + index * Mathf.Tau / Math.Max(1, count);
        // Wide and staggered in height, so their names have room even in a small window (2026-11-14: at 1280×720 only half
        // the names fitted with a tighter ring).
        var radius = 0.4f + 2.7f * rise; // metres: from 0.4 (inside the bench) out to 3.1
        var height = ov.Top + 0.7f + 0.8f * rise + (index % 3) * 0.45f * rise; // low enough to stay clear of the HUD at the top
        return Gd(surf.PointAt(ov.Direction, height)) + (Gd(t1) * Mathf.Cos(a) + Gd(t2) * Mathf.Sin(a)) * radius;

        static float Ease(float x) => 1 - (1 - x) * (1 - x) * (1 - x); // ease-out cubic: fast, then settling
    }

    /// <summary>The ring of cards over the bench: built on the reveal, turning slowly, facing the camera.</summary>
    private void UpdateBenchTiles(float dt)
    {
        if (!_benchRevealed || _s.Tree is not { } tree || _current == null)
        {
            return;
        }

        if (_benchTiles == null)
        {
            _benchTiles = new Node3D { Name = "BenchTiles" };
            AddChild(_benchTiles);
            _tiles.Clear();
            foreach (var e in _benchEntries)
            {
                if (!tree.Contains(e.NodeId)) continue;
                var color = TileColor(e.Role);
                var big = e.Role == BenchRole.Readme; // the README's card is larger and brighter
                var tile = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = big ? new Godot.Vector3(0.62f, 0.8f, 0.06f) : new Godot.Vector3(0.46f, 0.58f, 0.05f) },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = color with { A = 0.85f },
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        EmissionEnabled = true,
                        Emission = color,
                        EmissionEnergyMultiplier = big ? 1.6f : 1.1f,
                        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    },
                };
                _benchTiles.AddChild(tile);
                _tiles.Add((e.NodeId, tile));
            }
        }

        _tileSpin += dt * TileSpinRadiansPerSecond;
        for (var i = 0; i < _tiles.Count; i++)
        {
            var (id, tile) = _tiles[i];
            tile.GlobalPosition = TileAt(i, _tiles.Count);
            var selected = _s.Selection.Selected == id;
            var s = selected ? 1.25f : 1f; // the selected card grows by a quarter, easing over ≈ 0.1 s
            tile.Scale = tile.Scale.Lerp(new Godot.Vector3(s, s, s), 1 - MathF.Exp(-10 * dt));
        }
    }

    /// <summary>A card's colour by role: the README gold, build and project files blue, entry points pink, the rest mint.</summary>
    private static Color TileColor(BenchRole role) => role switch
    {
        BenchRole.Readme => new Color(1f, 0.88f, 0.5f),
        BenchRole.Solution or BenchRole.Manifest or BenchRole.Build => new Color(0.55f, 0.75f, 1f),
        BenchRole.Entry => new Color(1f, 0.6f, 0.75f),
        _ => new Color(0.45f, 1f, 0.8f),
    };

    /// <summary>A card over the bench under the cursor (its node), or −1.</summary>
    private int PickBenchTile(Vector2 screen)
    {
        if (_tiles.Count == 0 || _benchReveal01 < 0.5f) return -1; // not until the cards are half risen
        var o = _camera.ProjectRayOrigin(screen);
        var d = _camera.ProjectRayNormal(screen);
        var best = float.MaxValue;
        var id = -1;
        foreach (var (nodeId, tile) in _tiles)
        {
            var oc = o - tile.GlobalPosition;
            var b = oc.Dot(d);
            var disc = b * b - (oc.LengthSquared() - TilePickRadiusMetres * TilePickRadiusMetres);
            if (disc < 0) continue;
            var t = -b - MathF.Sqrt(disc);
            if (t > 0 && t < best)
            {
                best = t;
                id = nodeId;
            }
        }

        return id;
    }

    /// <summary>A click on a card: select it (the card shows it); a double-click goes to it.</summary>
    private bool ClickedBenchTile(Vector2 screen, bool doubleClick)
    {
        var id = PickBenchTile(screen);
        if (id < 0) return false;
        if (doubleClick) BenchGoTo(id);
        else
        {
            _s.Select(id);
            _audio.Select();
            var idx = _benchIds.IndexOf(id);
            if (idx >= 0) _benchList.Select(idx);
        }

        return true;
    }

    /// <summary>The bench's names on screen (while revealed): each card is named with what it is for.</summary>
    private void AddBenchSigns(FsTree tree)
    {
        if (!_benchRevealed || _benchReveal01 < 0.45f) return; // names appear as the cards are nearly half risen
        var up = _camera.GlobalBasis.Y;
        for (var i = 0; i < _tiles.Count; i++)
        {
            var (id, tile) = _tiles[i];
            if (!tree.Contains(id)) continue;
            var e = _benchEntries.First(x => x.NodeId == id);
            var n = tree[id];
            var where = BenchWhere(id) is { } w ? w.Label : "";
            var focus = _s.Selection.Selected == id || _hovered == id;
            // One line each (the panel has the reasons), two for the README and the one selected or pointed at.
            var detail = focus || e.Role == BenchRole.Readme ? e.Why + where : "";
            // Pinned 0.5 m above the card; the README's name has the higher priority (50 against 25).
            _signs.Add(new SignCandidate(-200_000 - id, tile.GlobalPosition + up * 0.5f, n.Name, detail, TileColor(e.Role).Lightened(0.2f), FileIcons.For(n),
                focus, e.Role == BenchRole.Readme ? 50f : 25f, FixedAnchor: true));
        }
    }

    /// <summary>
    /// The README in the preview (a spin on the bench, E there, or the panel): selected in the session, its text shown.
    /// Reading a README is a preview, not an "open": nothing leaves FileScape.
    /// </summary>
    public void ReadReadme(string how)
    {
        if (!OnBenchWorld || _s.Tree is not { } tree) return;
        RevealBench(how);
        var readme = _benchEntries.FirstOrDefault(e => e.Role == BenchRole.Readme);
        if (readme == null || !tree.Contains(readme.NodeId))
        {
            _audio.Deny();
            _s.Toast("This project has no README that FileScape recognises by name.");
            return;
        }

        _s.Select(readme.NodeId);
        _audio.Open();
        ShowPreview(tree, tree[readme.NodeId]);
        _benchPreviewId = readme.NodeId;
        if (_current?.Comp.Overlook is { } ov) _landFx.Burst(Gd(_current.Comp.Surface.PointAt(ov.Direction, ov.Top + 1.2f)), Gd(ov.Direction), new Color(1f, 0.9f, 0.55f)); // a gold burst, like the README's card
        var idx = _benchIds.IndexOf(readme.NodeId);
        if (idx >= 0) _benchList.Select(idx);
        GD.Print($"[planets] README read on the bench ({how}): {tree[readme.NodeId].Name}");
    }

    /// <summary>From the bench's panel: go to a key location (fly to a folder; stand by a file here; preview one further down).</summary>
    private void BenchGoTo(int id)
    {
        if (_universe != null || _s.Tree is not { } tree || !tree.Contains(id)) return;
        var n = tree[id];
        _s.Select(id);
        if (n.IsDirectory || n.Kind == NodeKind.Link)
        {
            LaunchToSelection(); // leaves from its launcher's bearing when it has one
            return;
        }

        if (n.ParentId == _current?.FolderId) Approach(id);
        else
        {
            ShowPreview(tree, n); // a file one level down (src/Program.cs): previewed from here
            _benchPreviewId = id;
        }
    }

    /// <summary>A click on the workbench itself (when nothing else is hit): the same as landing on it.</summary>
    private bool ClickedBench(Vector2 screen)
    {
        if (!OnBenchWorld || _current?.Comp.Overlook is not { } ov) return false;
        var o = _camera.ProjectRayOrigin(screen);
        var d = _camera.ProjectRayNormal(screen);
        // The bench as a sphere of its own radius centred at 0.6 of its height (a generous target).
        var c = Gd(_current.Comp.Surface.PointAt(ov.Direction, ov.Top * 0.6f));
        var oc = o - c;
        var b = oc.Dot(d);
        var disc = b * b - (oc.LengthSquared() - ov.Radius * ov.Radius);
        if (disc < 0) return false;
        var t = -b - MathF.Sqrt(disc);
        if (t <= 0) return false; // behind the camera (or the camera is inside it)
        if (RayPlanet(o, d, _current.Comp.Shape.Radius - OcclusionInsetMetres) is { } ground && ground < t - OcclusionSlackMetres) return false; // the planet is in the way
        RevealBench("click");
        return true;
    }
}
