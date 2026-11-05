using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Places;
using Godot;

namespace FileScape.App.Ui;

/// <summary>
/// Bounded screen-space name labels (VIS-07). At most <see cref="MaxLabels"/> are shown, chosen by priority:
/// the selection, the hovered node, the selection's parent and child directories, then the nearest visible
/// directories, then the nearest files. A candidate is skipped if it overlaps a label already placed or the UI
/// panels. Labels are 2D overlays, so they stay crisp and constant-size at any distance. They are not hidden
/// behind 3D geometry, which is a deliberate readability choice.
/// </summary>
public partial class LabelLayer : Control
{
    public const int MaxLabels = 60;
    private const int MaxDirCandidates = 220;
    private const int MaxFileCandidates = 160;

    private readonly List<Label> _pool = [];
    private readonly List<Rect2> _placed = [];
    private LabelSettings _dirSettings = null!, _fileSettings = null!, _selSettings = null!, _hoverSettings = null!;

    public int LastShown { get; private set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        var font = (Font)Hud.BuildTheme().DefaultFont;
        _dirSettings = Settings(font, 14, new Color(0.9f, 0.95f, 1f));
        _fileSettings = Settings(font, 12, new Color(0.78f, 0.84f, 0.92f));
        _selSettings = Settings(font, 16, new Color(1f, 0.95f, 0.65f));
        _hoverSettings = Settings(font, 15, new Color(0.7f, 0.9f, 1f));
        for (var i = 0; i < MaxLabels; i++)
        {
            var l = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
            AddChild(l);
            _pool.Add(l);
        }
    }

    private static LabelSettings Settings(Font font, int size, Color color) => new()
    {
        Font = font,
        FontSize = size,
        FontColor = color,
        OutlineSize = 5,
        OutlineColor = new Color(0.01f, 0.015f, 0.03f, 0.95f),
        ShadowSize = 2,
        ShadowColor = new Color(0, 0, 0, 0.5f),
    };

    private enum Kind { Selected, Hovered, Related, Dir, File }

    private int _used;
    private readonly HashSet<int> _shown = [];

    private readonly List<Rect2> _labelRects = [];

    /// <summary>Screen rectangles of the labels shown in the last refresh (VISI-01 checks).</summary>
    public IReadOnlyList<Rect2> ShownRects => _labelRects;

    private void Begin(IEnumerable<Rect2> blocked)
    {
        _placed.Clear();
        _placed.AddRange(blocked);
        _labelRects.Clear();
        _used = 0;
        _shown.Clear();
    }

    private bool Free(Rect2 r)
    {
        if (!GetViewportRect().Encloses(r)) return false;
        foreach (var p in _placed)
        {
            if (p.Intersects(r)) return false;
        }

        return true;
    }

    private void End()
    {
        for (var i = _used; i < _pool.Count; i++)
        {
            _pool[i].Visible = false;
        }

        LastShown = _used;
    }

    /// <summary>Places one label at a 3D anchor if it is on screen and does not collide with earlier labels or the UI.</summary>
    private void TryPlace(Camera3D camera, FsTree tree, int id, Vector3 anchor, Kind kind, bool belowAnchor)
    {
        if (_used >= MaxLabels || !tree.Contains(id) || !_shown.Add(id) || camera.IsPositionBehind(anchor))
        {
            return;
        }

        var p = camera.UnprojectPosition(anchor);
        if (!GetViewportRect().HasPoint(p))
        {
            return;
        }

        var node = tree[id];
        var label = _pool[_used];
        label.LabelSettings = kind switch
        {
            Kind.Selected => _selSettings,
            Kind.Hovered => _hoverSettings,
            Kind.File => _fileSettings,
            _ => _dirSettings,
        };
        label.Text = Text(node, tree.DisplayName(id), kind);
        var size = label.LabelSettings.Font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.LabelSettings.FontSize) + new Vector2(10, 6);
        var rect = new Rect2(p - new Vector2(size.X / 2, size.Y + (belowAnchor ? 0 : 4)), size);
        // Keep labels fully on screen (VISI-01): slide a label at the edge inwards rather than clipping it.
        var vp = GetViewportRect();
        rect.Position = new Vector2(Mathf.Clamp(rect.Position.X, vp.Position.X + 2, vp.End.X - size.X - 2), Mathf.Clamp(rect.Position.Y, vp.Position.Y + 2, vp.End.Y - size.Y - 2));
        if (!Free(rect))
        {
            if (kind is not (Kind.Selected or Kind.Hovered))
            {
                _shown.Remove(id);
                return;
            }

            // The selection and the hovered item get the nearest free spot above or below the anchor, never over the HUD
            // or another label (VISI-01).
            var found = false;
            foreach (var dy in new[] { -1.1f, 1.1f, -2.2f, 2.2f, -3.3f, 3.3f })
            {
                foreach (var dx in new[] { 0f, -0.6f, 0.6f })
                {
                    var r2 = rect with { Position = rect.Position + new Vector2(dx * size.X, dy * size.Y) };
                    if (!Free(r2)) continue;
                    rect = r2;
                    found = true;
                    break;
                }

                if (found) break;
            }

            if (!found)
            {
                // No free spot: no overlay label (the inspector and the in-world sign still name it) rather than overlap.
                _shown.Remove(id);
                return;
            }
        }

        _placed.Add(rect);
        _labelRects.Add(rect);
        label.Position = rect.Position + new Vector2(5, 3);
        label.Size = size;
        label.Visible = true;
        _used++;
    }

    public void Refresh(Camera3D camera, FsTree tree, LayoutResult layout, SelectionState selection, IEnumerable<Rect2> blocked)
    {
        Begin(blocked);
        var camPos = camera.GlobalPosition;

        // Candidate choice lives in Core (LabelPlanner) and only yields ids the displayed layout contains, which
        // matters while a scan grows the tree ahead of the throttled re-layout.
        foreach (var (id, role) in LabelPlanner.Candidates(tree, layout, selection, Visual.Visualization.Num(camPos), MaxDirCandidates, MaxFileCandidates))
        {
            var node = tree[id];
            var box = layout.NodeBoxes[id];
            // Directory labels sit at the pedestal's front edge. File labels sit above the block.
            var anchor = node.IsDirectory
                ? new Vector3(box.Center.X, box.Max.Y + 0.15f, box.Max.Z)
                : new Vector3(box.Center.X, box.Max.Y + 0.1f, box.Center.Z);
            TryPlace(camera, tree, id, anchor, role switch
            {
                LabelRole.Selected => Kind.Selected,
                LabelRole.Hovered => Kind.Hovered,
                LabelRole.Related => Kind.Related,
                LabelRole.Directory => Kind.Dir,
                _ => Kind.File,
            }, node.IsDirectory);
        }

        End();
    }

    /// <summary>
    /// Places: folder names are in-world signs (PlaceWorld), so the overlay labels only the selection, the hovered
    /// structure and the nearest file towers, which keeps the arrival view uncluttered (PLACE-05).
    /// </summary>
    public void RefreshPlace(Camera3D camera, FsTree tree, PlaceComposition place, SelectionState selection, IEnumerable<Rect2> blocked, int maxFiles = 10, float fileRange = 24f, IEnumerable<Rect2>? worldSigns = null)
    {
        Begin(blocked);
        // In-world signs (gate names, the landmark) are not covered by file labels either.
        if (worldSigns != null) _placed.AddRange(worldSigns);
        void Box(int id, Kind kind)
        {
            if (id >= 0 && PlacePicker.BoundsOf(place, id) is { } b)
            {
                TryPlace(camera, tree, id, new Vector3(b.Center.X, b.Max.Y + 0.25f, b.Center.Z), kind, false);
            }
        }

        if (selection.Selected != place.FolderId) Box(selection.Selected, Kind.Selected); // the landmark already names it
        Box(selection.Hovered, Kind.Hovered);
        var cam = Visual.Visualization.Num(camera.GlobalPosition);
        foreach (var f in place.Files
                     .Select(f => (f, D: System.Numerics.Vector3.Distance(f.Box.Center, cam)))
                     .Where(x => x.D < fileRange)
                     .OrderBy(x => x.D)
                     .Take(maxFiles * 2))
        {
            if (_used >= maxFiles + 2) break;
            Box(f.f.Id, Kind.File);
        }

        End();
    }

    public void HideAll()
    {
        foreach (var l in _pool)
        {
            l.Visible = false;
        }

        LastShown = 0;
    }

    private static string Text(FsNode n, string name, Kind kind)
    {
        var suffix = "";
        // Short markers only (VISI-02); the inspector has the explanation.
        if ((n.Flags & NodeFlags.AccessDenied) != 0) suffix = "  ⚠ denied";
        else if ((n.Flags & NodeFlags.Vanished) != 0) suffix = "  ⚠ vanished";
        else if (n.Kind == NodeKind.Link) suffix = "  ⇢";
        else if (n.IsDirectory && n.ScanState == DirScanState.Pending) suffix = "  …";
        else if (n.IsDirectory && n.ScanState is DirScanState.Cancelled or DirScanState.NotTraversed) suffix = "  (not scanned)";

        if (kind is Kind.Selected or Kind.Hovered || kind == Kind.File)
        {
            var size = n.IsDirectory ? SizeFormat.Short(n.SubtreeBytes) : n.Kind == NodeKind.File ? SizeFormat.Short(n.Size) : "";
            return size.Length > 0 ? $"{name}  ·  {size}{suffix}" : name + suffix;
        }

        return name + suffix;
    }
}
