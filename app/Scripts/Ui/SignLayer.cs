using FileScape.App.Visual;
using Godot;

namespace FileScape.App.Ui;

/// <summary>One destination the sign layer may name: a gate in a place, or a place in the aerial world.</summary>
/// <param name="Anchor">The world point the sign names (it is drawn just above where this projects).</param>
/// <param name="Focus">Approached, selected or hovered: placed first, larger, with its detail line.</param>
/// <param name="Boost">Priority, world units: the sign ranks as if this much nearer than it is.</param>
/// <param name="FixedAnchor">Pinned straight above its anchor with a stem (planets); only ever raised straight up on a longer stem (one slot, or just over the signs in its way), and sized by nearness.</param>
public readonly record struct SignCandidate(
    int Id,
    Vector3 Anchor,
    string Name,
    string Detail,
    Color Color,
    IconKind Icon,
    bool Focus,
    float Boost = 0f,
    bool FixedAnchor = false); // pinned straight above its anchor with a stem (planets); only ever raised straight up

/// <summary>
/// Glanceable destination names (LABEL-01, AD-25): screen-space signs for every gate (or aerial place) in view, so a
/// user looking across a place can identify several destinations at once without aiming at each.
/// <para>
/// It replaces "show the full name or hide it" (the 2026-11-05 overhead signs hid most names behind collisions and
/// HUD panels) with "show a shorter name somewhere near": names are shortened with a middle ellipsis to fit the
/// distance, the nearest and focused destinations also get a detail line, and a sign that would overlap tries higher
/// slots (with a thin leader line to its gate) before it is dropped. Signs never cover HUD panels or each other. The
/// full name stays in the gate chip, the inspector, Destinations and the eye-level plates.
/// </para>
/// Drawn in <see cref="_Draw"/> each frame (no node per sign): positions follow the camera smoothly.
/// </summary>
public partial class SignLayer : Control
{
    /// <summary>Most signs placed at once: enough for a wide place's visible arc, few enough to stay readable.</summary>
    public const int MaxSigns = 40;

    /// <summary>The detail line is added for this many nearest destinations (and every focused one).</summary>
    public const int DetailCount = 4;

    /// <summary>Anchors this far outside the window (viewport units) are still considered: their sign may fit inside it.</summary>
    private const int OffscreenMargin = 40;

    /// <summary>Slots a pinned sign may take: straight above its anchor, or stacked one sign higher on a longer stem.</summary>
    private const int PinnedSlots = 2;

    // ---- near and far pinned names (PLANET-58, AD-64): a pinned name grows smoothly as the camera nears its anchor, so
    // when a near and a far destination line up the nearer one reads as nearer (larger, drawn in front, a thicker stem
    // and a bigger dot). Size = PinnedMinPx + (PinnedMaxPx − PinnedMinPx) × smoothstep(PinnedGrowFarMetres → PinnedGrowNearMetres,
    // distance): 14 px at 30 m and beyond, 19 px at 8 m and nearer (the chase camera is ≈ 8 m from a pad you stand on),
    // e.g. 14.7 px at 25 m, 16.5 px at 19 m, 18.3 px at 13 m. Signs are never larger than the cap however near: the view
    // stays clear (a focused name is 18 px, the old near band 17 px).
    /// <summary>A pinned name at <see cref="PinnedGrowFarMetres"/> or farther, pixels at UI scale 1.</summary>
    public const float PinnedMinPx = 14f;

    /// <summary>A pinned name is never larger than this, pixels at UI scale 1 (reached at <see cref="PinnedGrowNearMetres"/>).</summary>
    public const float PinnedMaxPx = 19f;

    /// <summary>Growth starts this near (camera to anchor), metres.</summary>
    public const float PinnedGrowFarMetres = 30f;

    /// <summary>Growth reaches the cap this near, metres.</summary>
    public const float PinnedGrowNearMetres = 8f;

    /// <summary>The dot on a pinned sign's anchor grows from this radius far away to <see cref="PinnedDotMaxPx"/> near, pixels.</summary>
    private const float PinnedDotMinPx = 3f, PinnedDotMaxPx = 4.5f;

    /// <summary>The stem grows from this width far away to <see cref="PinnedStemMaxPx"/> near, pixels.</summary>
    private const float PinnedStemMinPx = 1.5f, PinnedStemMaxPx = 2.25f;

    /// <summary>A pinned name keeps this many characters far away and up to <see cref="PinnedCharsNear"/> near (before a middle ellipsis).</summary>
    private const int PinnedCharsFar = 16, PinnedCharsNear = 26;

    /// <summary>
    /// When its own slots are taken, a pinned sign climbs to just above whatever sign covers it (a near name with a detail
    /// line is taller than one slot), as long as its stem stays this short, pixels: a far pad straight behind a near one
    /// keeps its name (2026-11-17, the arrival on a crowded root: "var" vanished behind "etc").
    /// </summary>
    public const float PinnedMaxStemPx = 150f;

    /// <summary>How far up a pinned sign may climb past signs in its way (each climb clears one sign).</summary>
    private const int PinnedClimbs = 3;

    /// <summary>0 at <see cref="PinnedGrowFarMetres"/> or farther, 1 at <see cref="PinnedGrowNearMetres"/> or nearer, smooth between.</summary>
    public static float Nearness(float metres)
    {
        var t = Mathf.Clamp((PinnedGrowFarMetres - metres) / (PinnedGrowFarMetres - PinnedGrowNearMetres), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>A pinned name's size at this camera distance, pixels at UI scale 1 (see <see cref="PinnedMinPx"/>).</summary>
    public static float PinnedSizePx(float metres) => PinnedMinPx + (PinnedMaxPx - PinnedMinPx) * Nearness(metres);

    /// <summary>
    /// Distance bands, world units (metres in Places and the aerial world): within 25 a sign is 17 px and keeps 26
    /// characters, within 60 15 px / 20, within 140 14 px / 16, beyond 13 px / 12; a focused sign is 18 px / 40.
    /// </summary>
    private const float NearBand = 25f, MidBand = 60f, FarBand = 140f;

    /// <summary>The detail line keeps at most this many characters and is 4 px smaller than the name.</summary>
    private const int DetailMaxChars = 46, DetailFontReduction = 4;

    private readonly List<Placed> _placed = [];
    private readonly List<Rect2> _occupied = [];
    private Font _font = null!;
    private StyleBoxFlat _back = null!, _focusBack = null!;
    private Texture2D? _icons;

    /// <summary>
    /// A sign placed this frame: its rectangle (viewport units), the projected anchor, the text as shortened, whether it
    /// needs a leader line, its draw scale (a pinned name's fractional size: drawn at the whole <c>FontSize</c> scaled by
    /// this, within ±3 %), its nearness (0 far … 1 near) and its camera distance (world units: far signs are drawn first).
    /// </summary>
    private readonly record struct Placed(SignCandidate C, Rect2 Rect, Vector2 AnchorScreen, string Name, string Detail, int FontSize, bool Leader, float Scale, float Near, float Distance);

    /// <summary>A focused (approached, selected or hovered) sign's size, pixels at UI scale 1.</summary>
    private const int FocusPx = 18;

    /// <summary>Ids named in the last layout, in placement order.</summary>
    public List<int> ShownIds { get; } = [];

    /// <summary>Screen rectangles of the placed signs (so other overlays avoid them; checks).</summary>
    public List<Rect2> ShownRects { get; } = [];

    /// <summary>Candidates that were in front of the camera and inside the window in the last layout.</summary>
    public int InView { get; private set; }

    /// <summary>The text shown for each placed id (name line, as shortened), for checks.</summary>
    public Dictionary<int, string> ShownText { get; } = [];

    /// <summary>Where each shown sign's anchor projected this frame (screen), for probes: a pinned sign sits right above it.</summary>
    public Dictionary<int, Vector2> ShownAnchors { get; } = [];

    /// <summary>Each shown sign's rectangle by id (probes measure it against a freshly projected anchor).</summary>
    public Dictionary<int, Rect2> ShownRectById { get; } = [];

    /// <summary>Each shown sign's name size by id, pixels at UI scale 1 (probes compare a near and a far name).</summary>
    public Dictionary<int, float> ShownSizePx { get; } = [];

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _font = (Font)Hud.BuildTheme().DefaultFont;
        _back = new StyleBoxFlat { BgColor = new Color(0.02f, 0.03f, 0.06f, 0.55f), ContentMarginLeft = 6, ContentMarginRight = 6 };
        _back.SetCornerRadiusAll(5);
        _focusBack = new StyleBoxFlat { BgColor = new Color(0.03f, 0.05f, 0.1f, 0.78f), BorderColor = new Color(1f, 0.92f, 0.6f, 0.8f) };
        _focusBack.SetBorderWidthAll(1);
        _focusBack.SetCornerRadiusAll(5);
        _icons = FileIcons.Atlas;
    }

    /// <summary>Removes every sign (leaving a world, or when no candidates apply).</summary>
    public void Clear()
    {
        _placed.Clear();
        ShownIds.Clear();
        ShownRects.Clear();
        ShownText.Clear();
        InView = 0;
        QueueRedraw();
    }

    /// <summary>
    /// Lays out the signs for this frame. <paramref name="blocked"/> are HUD panels (viewport units); <paramref name="skip"/>
    /// returns true for a candidate already identified another way right now (an eye-level plate in clear view).
    /// </summary>
    public void Layout(Camera3D camera, IReadOnlyList<SignCandidate> candidates, IEnumerable<Rect2> blocked, Func<SignCandidate, float, bool>? skip = null)
    {
        _placed.Clear();
        _occupied.Clear();
        _occupied.AddRange(blocked);
        ShownIds.Clear();
        ShownRects.Clear();
        ShownText.Clear();
        ShownAnchors.Clear();
        ShownRectById.Clear();
        ShownSizePx.Clear();
        var vp = GetViewportRect();
        var eye = camera.GlobalPosition;
        var ordered = new List<(SignCandidate C, float D, Vector2 S)>();
        foreach (var c in candidates)
        {
            if (camera.IsPositionBehind(c.Anchor)) continue;
            var s = camera.UnprojectPosition(c.Anchor);
            if (s.X < -OffscreenMargin || s.X > vp.Size.X + OffscreenMargin || s.Y < -OffscreenMargin || s.Y > vp.Size.Y + OffscreenMargin) continue;
            var d = eye.DistanceTo(c.Anchor);
            if (skip != null && skip(c, d)) continue;
            ordered.Add((c, d, s));
        }

        InView = ordered.Count;
        // Focused first (approached, selected, hovered), then nearest; a boost brings important ones forward (visited, large).
        ordered.Sort((a, b) => a.C.Focus != b.C.Focus ? (a.C.Focus ? -1 : 1) : (a.D - a.C.Boost).CompareTo(b.D - b.C.Boost));
        var rank = 0;
        foreach (var (c, d, s) in ordered)
        {
            if (_placed.Count >= MaxSigns) break;
            var detailed = c.Focus || rank < DetailCount;
            // Nearer and focused signs are larger and carry more of the name; far ones are short (a glance, not a read).
            // A pinned name (planets) grows smoothly with nearness instead of in bands (PLANET-58): drawn at a whole font
            // size scaled by the remainder, so it never jumps a pixel at a band edge.
            int size, maxChars;
            var scale = 1f;
            var near = 0f;
            if (c.FixedAnchor)
            {
                near = Nearness(d);
                var px = c.Focus ? MathF.Max(FocusPx, PinnedSizePx(d)) : PinnedSizePx(d);
                size = (int)MathF.Round(px);
                scale = px / size;
                maxChars = c.Focus ? 40 : (int)MathF.Round(Mathf.Lerp(PinnedCharsFar, PinnedCharsNear, near));
            }
            else
            {
                size = c.Focus ? FocusPx : d < NearBand ? 17 : d < MidBand ? 15 : d < FarBand ? 14 : 13;
                maxChars = c.Focus ? 40 : d < NearBand ? 26 : d < MidBand ? 20 : d < FarBand ? 16 : 12;
            }

            // Three tries: as chosen; without the detail line; then 2 px smaller and at most 10 characters.
            if (TryPlace(c, s, size, scale, maxChars, detailed, vp, near, d) || (detailed && TryPlace(c, s, size, scale, maxChars, false, vp, near, d)) || TryPlace(c, s, Math.Max(12, size - 2), scale, Math.Min(maxChars, 10), false, vp, near, d))
            {
                rank++;
            }
        }

        QueueRedraw();
    }

    /// <summary>
    /// Tries the slots round <paramref name="anchor"/> (screen) for one sign of this font size and length; the first slot
    /// inside the window and clear of HUD panels and earlier signs wins and is recorded. False if none is free.
    /// </summary>
    private bool TryPlace(SignCandidate c, Vector2 anchor, int size, float scale, int maxChars, bool detailed, Rect2 vp, float near, float distance)
    {
        var name = Fit(c.Name, maxChars);
        var detail = detailed && c.Detail.Length > 0 ? Fit(c.Detail, DetailMaxChars) : "";
        var nameSize = _font.GetStringSize(name, HorizontalAlignment.Left, -1, size);
        var detailSize = detail.Length > 0 ? _font.GetStringSize(detail, HorizontalAlignment.Left, -1, size - DetailFontReduction) : Vector2.Zero;
        var icon = size + 2;
        var w = (Mathf.Max(nameSize.X + icon + 6, detailSize.X) + 12) * scale;
        var h = (nameSize.Y + (detail.Length > 0 ? detailSize.Y : 0) + 4) * scale;
        // Slots: right above the anchor, then stacked higher (a leader line keeps it attached), then nudged sideways.
        // When the space above is taken (often by the top HUD panels close to a gate), the sign drops onto the gate's face.
        Span<Vector2> offsets =
        [
            new(0, 0), new(0, -(h + 4)), new(0, h + 14), new(0, -2 * (h + 4)), new(0, 2 * h + 18),
            new(-w * 0.55f, -(h + 4) * 0.5f), new(w * 0.55f, -(h + 4) * 0.5f), new(0, -3 * (h + 4)), new(0, 3 * h + 22),
            new(-w * 0.6f, -1.5f * (h + 4)), new(w * 0.6f, -1.5f * (h + 4)),
        ];
        // A pinned sign may also stack one slot higher on a longer stem: from the higher arrival camera (PLANET-42) a near
        // pad and a far pad on the same bearing line up, and the second name used to vanish (2026-11-17). The stem still
        // runs to the pad, so which pad it names stays plain.
        for (var i = 0; i < (c.FixedAnchor ? PinnedSlots : offsets.Length); i++)
        {
            // The sign's bottom sits 6 units above the anchor (16 for a pinned sign, leaving room for its stem and dot).
            var r = new Rect2(anchor.X - w / 2 + offsets[i].X, anchor.Y - h - (c.FixedAnchor ? 16 : 6) + offsets[i].Y, w, h);
            if (c.FixedAnchor && !vp.Encloses(r) && vp.HasPoint(anchor))
            {
                // A pinned sign near the edge is kept on screen; its stem still reaches the anchor.
                r.Position = new Vector2(Mathf.Clamp(r.Position.X, vp.Position.X + 2, vp.End.X - w - 2), Mathf.Clamp(r.Position.Y, vp.Position.Y + 2, vp.End.Y - h - 2));
                if (r.HasPoint(anchor)) r.Position = r.Position with { Y = anchor.Y + 14 }; // never over the thing it names
            }

            if (!vp.Encloses(r) || Blocker(r) != null) continue;
            Accept(r, i > 0);
            return true;
        }

        if (c.FixedAnchor)
        {
            // Climb (PLANET-58): straight above the anchor, just over whatever sign is in the way, while the stem stays
            // short. A near name with a detail line is taller than this sign's own slot, so "one slot higher" was not
            // always enough for the far pad straight behind it.
            var r = new Rect2(anchor.X - w / 2, anchor.Y - h - 16, w, h);
            for (var k = 0; k < PinnedClimbs && Blocker(r) is { } o; k++)
            {
                r.Position = r.Position with { Y = o.Position.Y - h - 1 };
                if (anchor.Y - r.End.Y > PinnedMaxStemPx || !vp.Encloses(r)) break;
                if (Blocker(r) == null)
                {
                    Accept(r, true);
                    return true;
                }
            }
        }

        return false;

        // The topmost occupied rectangle overlapping r (signs and HUD panels), or null when r is clear.
        Rect2? Blocker(Rect2 r)
        {
            Rect2? top = null;
            foreach (var o in _occupied)
            {
                if (o.Intersects(r) && (top == null || o.Position.Y < top.Value.Position.Y)) top = o;
            }

            return top;
        }

        void Accept(Rect2 r, bool leader)
        {
            _occupied.Add(r.Grow(2));
            _placed.Add(new Placed(c, r, anchor, name, detail, size, leader, scale, near, distance));
            ShownIds.Add(c.Id);
            ShownRects.Add(r);
            ShownText[c.Id] = name;
            ShownAnchors[c.Id] = anchor;
            ShownRectById[c.Id] = r;
            ShownSizePx[c.Id] = size * scale;
        }
    }

    /// <summary>The id of the sign drawn topmost at a point (viewport units), or null: a name can be clicked (PLANET-58).</summary>
    public int? IdAt(Vector2 point)
    {
        int? hit = null;
        foreach (var p in DrawOrder())
        {
            if (p.Rect.HasPoint(point)) hit = p.C.Id;
        }

        return hit;
    }

    /// <summary>
    /// The pieces of the segment a→b not covered by the rectangles of <paramref name="order"/> from <paramref name="from"/>
    /// on (the signs drawn over it): each rectangle removes the parameter interval where the segment is inside it.
    /// </summary>
    private static List<(Vector2 A, Vector2 B)> Uncovered(Vector2 a, Vector2 b, List<Placed> order, int from)
    {
        var cuts = new List<(float T0, float T1)>();
        var d = b - a;
        for (var j = from; j < order.Count; j++)
        {
            // Liang–Barsky: the parameter range of the segment inside the rectangle, if any.
            var rr = order[j].Rect;
            float t0 = 0, t1 = 1;
            var inside = Clip(-d.X, a.X - rr.Position.X, ref t0, ref t1) && Clip(d.X, rr.End.X - a.X, ref t0, ref t1)
                && Clip(-d.Y, a.Y - rr.Position.Y, ref t0, ref t1) && Clip(d.Y, rr.End.Y - a.Y, ref t0, ref t1);
            if (inside && t1 > t0) cuts.Add((t0, t1));
        }

        var pieces = new List<(Vector2, Vector2)>();
        var t = 0f;
        foreach (var (c0, c1) in cuts.OrderBy(c => c.T0))
        {
            if (c0 > t) pieces.Add((a + d * t, a + d * c0));
            t = MathF.Max(t, c1);
        }

        if (t < 1) pieces.Add((a + d * t, b));
        return pieces;

        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (MathF.Abs(p) < 1e-6f) return q >= 0;
            var u = q / p;
            if (p < 0) { if (u > t1) return false; if (u > t0) t0 = u; }
            else { if (u < t0) return false; if (u < t1) t1 = u; }
            return true;
        }
    }

    /// <summary>Back to front: far signs first, near ones over them, focused ones last (on top).</summary>
    private IEnumerable<Placed> DrawOrder() => _placed.OrderBy(p => p.C.Focus).ThenByDescending(p => p.Distance);

    /// <summary>Draws the signs placed by the last <see cref="Layout"/>: stem or leader, backing, icon, name and detail, each with a dark outline.</summary>
    public override void _Draw()
    {
        var order = DrawOrder().ToList();
        for (var i = 0; i < order.Count; i++)
        {
            var p = order[i];
            var r = p.Rect;
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            if (p.C.FixedAnchor)
            {
                // A pinned sign (planets, 2026-11-11): a short stem and a dot on the very thing it names, so it is plain
                // which pad or world it belongs to, wherever the camera turns. Both grow a little with nearness (PLANET-58).
                // The stem and dot pass behind nearer signs (cut where a sign drawn later covers them; the boxes are
                // see-through, so a far stem would otherwise cross a near name's text).
                var foot = new Vector2(Mathf.Clamp(p.AnchorScreen.X, r.Position.X + 4, r.End.X - 4), r.Position.Y > p.AnchorScreen.Y ? r.Position.Y : r.End.Y);
                var width = Mathf.Lerp(PinnedStemMinPx, PinnedStemMaxPx, p.Near);
                foreach (var (a, b) in Uncovered(foot, p.AnchorScreen, order, i + 1)) DrawLine(a, b, p.C.Color with { A = 0.8f }, width, true);
                if (!order.Skip(i + 1).Any(q => q.Rect.HasPoint(p.AnchorScreen))) DrawCircle(p.AnchorScreen, Mathf.Lerp(PinnedDotMinPx, PinnedDotMaxPx, p.Near), p.C.Color);
            }
            else if (p.Leader)
            {
                var from = r.Position.Y > p.AnchorScreen.Y ? new Vector2(r.Position.X + r.Size.X / 2, r.Position.Y) : new Vector2(r.Position.X + r.Size.X / 2, r.End.Y);
                DrawLine(from, p.AnchorScreen, p.C.Color with { A = 0.55f }, 1.5f, true);
            }

            // The box and text are drawn at the whole font size, scaled about the sign's corner by the fractional rest.
            DrawSetTransform(r.Position, 0, new Vector2(p.Scale, p.Scale));
            r = new Rect2(Vector2.Zero, r.Size / p.Scale);
            DrawStyleBox(p.C.Focus ? _focusBack : _back, r);
            var iconSize = p.FontSize + 2;
            var x = r.Position.X + 6;
            var nameY = r.Position.Y + 2 + _font.GetAscent(p.FontSize);
            if (_icons != null)
            {
                var src = new Rect2((int)p.C.Icon * FileIcons.Cell, 0, FileIcons.Cell, FileIcons.Cell);
                DrawTextureRectRegion(_icons, new Rect2(x, r.Position.Y + 2 + (_font.GetHeight(p.FontSize) - iconSize) / 2, iconSize, iconSize), src, p.C.Color);
            }

            DrawStringOutline(_font, new Vector2(x + iconSize + 4, nameY), p.Name, HorizontalAlignment.Left, -1, p.FontSize, 4, new Color(0, 0, 0, 0.85f));
            DrawString(_font, new Vector2(x + iconSize + 4, nameY), p.Name, HorizontalAlignment.Left, -1, p.FontSize, p.C.Color);
            if (p.Detail.Length > 0)
            {
                var dy = nameY + _font.GetDescent(p.FontSize) + _font.GetAscent(p.FontSize - DetailFontReduction);
                DrawStringOutline(_font, new Vector2(x, dy), p.Detail, HorizontalAlignment.Left, -1, p.FontSize - DetailFontReduction, 3, new Color(0, 0, 0, 0.85f));
                DrawString(_font, new Vector2(x, dy), p.Detail, HorizontalAlignment.Left, -1, p.FontSize - DetailFontReduction, new Color(0.82f, 0.88f, 0.98f));
            }
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>Shortens with a middle ellipsis, keeping the start (what it is) and the end (often a number or extension).</summary>
    public static string Fit(string name, int max)
    {
        if (name.Length <= max) return name;
        var tail = Math.Max(2, max / 3);
        var head = Math.Max(1, max - tail - 1);
        if (char.IsHighSurrogate(name[head - 1])) head--; // never split a surrogate pair (emoji, rare CJK)
        if (char.IsLowSurrogate(name[name.Length - tail])) tail--;
        return string.Concat(name.AsSpan(0, head), "…", name.AsSpan(name.Length - tail));
    }
}
