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
/// </summary>
public partial class PlanetWorld
{
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

    /// <summary>The current world is a repository with its workbench.</summary>
    public bool OnBenchWorld => _character.Kind == WorldKind.Repository && _current?.Comp.Overlook != null;

    /// <summary>The bench has shown the project's key locations (probes).</summary>
    public bool BenchRevealed => _benchRevealed;

    public IReadOnlyList<BenchEntry> BenchEntries => _benchEntries;

    public bool BenchPanelOpen => _benchPanel.Visible;

    /// <summary>Standing on the workbench deck now.</summary>
    private bool StandingOnBench() =>
        OnBenchWorld && _current!.Comp.Overlook is { } ov && _walker.Grounded && _walker.Support.Value > ov.Top - 0.3f &&
        PlanetShape.Angle(_walker.Up, ov.Direction) * _current.Comp.Shape.Radius < ov.Radius + 0.3f;

    private void BuildBenchPanel(Control root)
    {
        _benchPanel = new PanelContainer { Visible = false };
        var style = PanelStyle();
        style.BorderColor = new Color(0.4f, 1f, 0.75f, 0.6f);
        _benchPanel.AddThemeStyleboxOverride("panel", style);
        // Bottom right, under the selection card (which sits top right).
        _benchPanel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _benchPanel.OffsetLeft = -420; _benchPanel.OffsetRight = -12; _benchPanel.OffsetTop = -372; _benchPanel.OffsetBottom = -66;
        root.AddChild(_benchPanel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        _benchPanel.AddChild(v);
        _benchTitle = MakeLabel(17, new Color(0.75f, 1f, 0.88f), HorizontalAlignment.Left, outline: 0);
        _benchList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Single, FixedIconSize = new Vector2I(18, 18), FocusMode = Control.FocusModeEnum.None };
        _benchList.ItemSelected += i =>
        {
            if (i < 0 || i >= _benchIds.Count) return;
            _s.Select(_benchIds[(int)i]);
            _audio.Select();
        };
        _benchList.ItemActivated += i =>
        {
            if (i < 0 || i >= _benchIds.Count) return;
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

    /// <summary>A new world: forget the last bench, read this one's project (names only).</summary>
    private void ResetBench(FsTree tree)
    {
        _benchRevealed = false;
        _benchReveal01 = 0;
        _wasOnBench = false;
        _jumpFromBench = false;
        _benchTiles?.QueueFree();
        _benchTiles = null;
        _tiles.Clear();
        _benchLamp?.QueueFree();
        _benchLamp = null;
        _benchPanel.Visible = false;
        _benchEntries = OnBenchWorld && _current != null ? ProjectBench.KeyLocations(tree, _current.FolderId, _s.IsVisible) : [];
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
        if (_benchRevealed) _benchReveal01 = MathF.Min(1, _benchReveal01 + dt / 0.9f);
        UpdateBenchTiles(dt);
        if (_benchLamp != null) _benchLamp.LightEnergy = Mathf.Lerp(_benchLamp.LightEnergy, onBench ? 2.2f : 1.2f, 1 - MathF.Exp(-4 * dt));
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
                _landFx.Burst(top, Gd(ov.Direction), new Color(0.4f, 1f, 0.75f));
                _benchLamp = new OmniLight3D { Name = "BenchLamp", LightColor = new Color(0.55f, 1f, 0.82f), LightEnergy = 0.2f, OmniRange = 7f, Position = Gd(_current.Comp.Surface.PointAt(ov.Direction, ov.Top + 1.6f)) };
                AddChild(_benchLamp);
            }

            ShowHint(_benchEntries.Count > 0
                ? "The workbench shows this project. Spin here (Space, Space) or press E to read its README."
                : "The workbench: this project has no README or entry points that FileScape recognises by name.");
        }

        FillBench(tree);
        _benchPanel.Visible = true;
        GD.Print($"[planets] bench revealed ({how}): {string.Join(", ", _benchEntries.Select(e => $"{tree[e.NodeId].Name} ({e.Role})"))}");
    }

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

    /// <summary>Where a key location stands on this planet: the file, its pile, or the launcher of the folder holding it.</summary>
    private (Godot.Vector3 At, string Label)? BenchWhere(int id)
    {
        if (_current == null || _s.Tree is not { } tree || !tree.Contains(id)) return null;
        var surf = _current.Comp.Surface;
        if (_current.Comp.Items.FirstOrDefault(i => i.NodeId == id) is { } item) return (Gd(surf.PointAt(item.Direction, 1.25f * item.Scale + 0.3f)), "");
        if (_current.Comp.Launchers.FirstOrDefault(l => l.TargetId == id) is { } pad) return (Gd(surf.PointAt(pad.Direction, 1.35f)), "");
        var n = tree[id];
        if (n.Kind == NodeKind.File && n.ParentId == _current.FolderId &&
            _current.Comp.Items.FirstOrDefault(i => i.Kind == PlanetItemKind.Pile && i.Category == FileCategories.Of(n.Name)) is { } pile)
        {
            return (Gd(surf.PointAt(pile.Direction, 1.25f * pile.Scale + 0.3f)), " · in a pile");
        }

        if (n.ParentId >= 0 && _current.Comp.Launchers.FirstOrDefault(l => l.TargetId == n.ParentId) is { } via) return (Gd(surf.PointAt(via.Direction, 1.35f)), $" · via {tree[n.ParentId].Name}");
        return null;
    }

    /// <summary>Where a key location's card floats above the bench (rising and opening out as the reveal plays).</summary>
    private Godot.Vector3 TileAt(int index, int count)
    {
        var ov = _current!.Comp.Overlook;
        var surf = _current.Comp.Surface;
        var up = Gd(ov.Direction);
        var (t1, t2) = PlanetShape.Tangents(ov.Direction);
        var rise = Ease(Mathf.Clamp(_benchReveal01 * 1.4f - index * 0.06f, 0, 1));
        var a = _tileSpin + index * Mathf.Tau / Math.Max(1, count);
        var radius = 0.4f + 2.0f * rise;
        var height = ov.Top + 0.8f + 1.5f * rise + (index % 2) * 0.35f * rise;
        return Gd(surf.PointAt(ov.Direction, height)) + (Gd(t1) * Mathf.Cos(a) + Gd(t2) * Mathf.Sin(a)) * radius;

        static float Ease(float x) => 1 - (1 - x) * (1 - x) * (1 - x);
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
                var big = e.Role == BenchRole.Readme;
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

        _tileSpin += dt * 0.18f;
        for (var i = 0; i < _tiles.Count; i++)
        {
            var (id, tile) = _tiles[i];
            tile.GlobalPosition = TileAt(i, _tiles.Count);
            var selected = _s.Selection.Selected == id;
            var s = selected ? 1.25f : 1f;
            tile.Scale = tile.Scale.Lerp(new Godot.Vector3(s, s, s), 1 - MathF.Exp(-10 * dt));
        }
    }

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
        if (_tiles.Count == 0 || _benchReveal01 < 0.5f) return -1;
        var o = _camera.ProjectRayOrigin(screen);
        var d = _camera.ProjectRayNormal(screen);
        var best = float.MaxValue;
        var id = -1;
        foreach (var (nodeId, tile) in _tiles)
        {
            var oc = o - tile.GlobalPosition;
            var b = oc.Dot(d);
            var disc = b * b - (oc.LengthSquared() - 0.42f * 0.42f);
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
        if (!_benchRevealed || _benchReveal01 < 0.45f) return;
        var up = _camera.GlobalBasis.Y;
        for (var i = 0; i < _tiles.Count; i++)
        {
            var (id, tile) = _tiles[i];
            if (!tree.Contains(id)) continue;
            var e = _benchEntries.First(x => x.NodeId == id);
            var n = tree[id];
            var where = BenchWhere(id) is { } w ? w.Label : "";
            _signs.Add(new SignCandidate(-200_000 - id, tile.GlobalPosition + up * 0.5f, n.Name, e.Why + where, TileColor(e.Role).Lightened(0.2f), FileIcons.For(n),
                _s.Selection.Selected == id || _hovered == id, e.Role == BenchRole.Readme ? 50f : 25f, FixedAnchor: true));
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
        if (_current?.Comp.Overlook is { } ov) _landFx.Burst(Gd(_current.Comp.Surface.PointAt(ov.Direction, ov.Top + 1.2f)), Gd(ov.Direction), new Color(1f, 0.9f, 0.55f));
        var idx = _benchIds.IndexOf(readme.NodeId);
        if (idx >= 0) _benchList.Select(idx);
        GD.Print($"[planets] README read on the bench ({how}): {tree[readme.NodeId].Name}");
    }

    /// <summary>From the bench's panel: go to a key location (stand by a file here, fly to a folder).</summary>
    private void BenchGoTo(int id)
    {
        if (_s.Tree is not { } tree || !tree.Contains(id)) return;
        var n = tree[id];
        _s.Select(id);
        if (n.IsDirectory || n.Kind == NodeKind.Link)
        {
            if (_current?.Comp.Launchers.Any(l => l.TargetId == id) == true) Approach(id);
            else LaunchToSelection();
            return;
        }

        if (n.ParentId == _current?.FolderId) Approach(id);
        else ShowPreview(tree, n); // a file one level down (src/Program.cs): previewed from here
    }

    /// <summary>A click on the workbench itself (when nothing else is hit): the same as landing on it.</summary>
    private bool ClickedBench(Vector2 screen)
    {
        if (!OnBenchWorld || _current?.Comp.Overlook is not { } ov) return false;
        var o = _camera.ProjectRayOrigin(screen);
        var d = _camera.ProjectRayNormal(screen);
        var c = Gd(_current.Comp.Surface.PointAt(ov.Direction, ov.Top * 0.6f));
        var oc = o - c;
        var b = oc.Dot(d);
        var disc = b * b - (oc.LengthSquared() - ov.Radius * ov.Radius);
        if (disc < 0) return false;
        RevealBench("click");
        return true;
    }
}
