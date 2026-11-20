using FileScape.App.Ui;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The overview's reading of a large universe (fifth playtest follow-up, AD-77): how much of the laid-out hierarchy is
/// drawn (PLANET-91), what each miniature world looks like (PLANET-92) and the controls for moving through it
/// (PLANET-93). <see cref="PlanetWorld.Universe"/> lays out and draws; this part decides, every
/// <see cref="DetailIntervalSeconds"/>, which worlds are shown (<see cref="OverviewDetail"/>: systems open when their ring
/// is big enough on screen, on the way to the selected or approached world, sooner below it, later away from it) and
/// fades worlds in and out; folded systems wear a haze (<c>overview_haze.gdshader</c>) and say how many worlds they hold.
/// Nothing here changes the layout or what discovery lists: discovery still works from every laid-out world.
/// <para>
/// Preserve: per-frame work stays linear in the bodies (≤ 1,500) and allocation-free except at the detail steps; the
/// level of detail never moves a world (only its drawn scale and the haze change), so it cannot cause a jump; the selected
/// world and the way to it are always drawn. Main thread only.
/// </para>
/// </summary>
public partial class PlanetWorld
{
    /// <summary>False with --test-overview-detail=off (instrument): every laid-out world is drawn, as before PLANET-91.
    /// Settable for the bodies-against-frame-time measurement (PLANET-108), which compares both in one run.</summary>
    public bool OverviewDetailEnabled { get; set; } = true;

    /// <summary>False with --test-overview-look=plain (instrument): the old palette colour and material, as before PLANET-92.</summary>
    public bool OverviewLooksVaried { get; init; } = true;

    /// <summary>How often the level of detail is decided, seconds: ten times a second follows a zoom closely; the fades
    /// (<see cref="DetailFadeRatePerSecond"/>) smooth what happens between.</summary>
    private const float DetailIntervalSeconds = 0.1f;

    /// <summary>How fast a world fades in or out as its system opens or folds, per second (time constant ≈ 0.17 s): quick
    /// enough to follow the wheel, slow enough to read as unfolding rather than popping.</summary>
    private const float DetailFadeRatePerSecond = 6f;

    /// <summary>The haze's strength over a folded system: this much for a handful of folded worlds, rising by
    /// <see cref="HazeAlphaPerDecade"/> per tenfold more, at most <see cref="HazeMaxAlpha"/>. Faint: it marks, it does not hide.</summary>
    private const float HazeBaseAlpha = 0.10f, HazeAlphaPerDecade = 0.06f, HazeMaxAlpha = 0.26f;

    /// <summary>A haze is at least this many body radii across (a folded leaf the budget cut has no ring of its own).</summary>
    private const float HazeMinRadii = 2.6f;

    /// <summary>The folded systems in view holding the most worlds are named with their count: this many, each holding at
    /// least <see cref="FoldedLabelMin"/> worlds (fewer would crowd the names the overview already shows).</summary>
    private const int FoldedLabels = 4, FoldedLabelMin = 8;

    private bool[] _uniShown = [], _uniExpanded = [], _uniWasExpanded = [];
    private int[] _uniFolded = [];
    private float[] _uniShowFade = [], _uniOpenFade = [], _uniPpm = [];
    private float _uniDetailT = float.MaxValue;
    private bool _uniDetailSnap = true;
    private int _uniApproached = -1;

    /// <summary>The bodies that may wear a haze (a system in the layout, or a world with folders the budget left out), by body index.</summary>
    private readonly List<int> _uniHazeBodies = [];
    private MultiMesh? _uniHazeMesh;

    /// <summary>The detail decision's cost, milliseconds (probes).</summary>
    public double DetailMsMax { get; private set; }

    /// <summary>
    /// Carries the fades over a relayout by node id (a world already drawn keeps how shown it was, so a relayout never
    /// flashes the level of detail); a new world starts unshown and fades in once its system is open.
    /// </summary>
    private void RemapDetail(Dictionary<int, int> oldIndex, IReadOnlyList<UniverseBody> bodies, bool first)
    {
        var oldShow = _uniShowFade;
        var oldOpen = _uniOpenFade;
        var oldExpanded = _uniExpanded;
        var n = bodies.Count;
        _uniShown = new bool[n];
        _uniExpanded = new bool[n];
        _uniWasExpanded = new bool[n];
        _uniFolded = new int[n];
        _uniShowFade = new float[n];
        _uniOpenFade = new float[n];
        _uniPpm = new float[n];
        _uniMinPx = new float[n];
        Array.Fill(_uniMinPx, MinDrawnRadiusPixels);
        _uniChords = OverviewDetail.ClosestChildChords(bodies); // per layout: the places only glide between layouts
        for (var i = 0; i < n; i++)
        {
            if (first || !oldIndex.TryGetValue(bodies[i].NodeId, out var oi) || oi >= oldShow.Length) continue;
            _uniShowFade[i] = oldShow[oi];
            _uniOpenFade[i] = oldOpen[oi];
            _uniWasExpanded[i] = oldExpanded[oi];
        }

        _uniDetailSnap = first;
        _uniDetailT = float.MaxValue; // decide at once
    }

    /// <summary>
    /// Every frame from <see cref="PlaceUniverse"/>, after the bodies' places: decides the level of detail every
    /// <see cref="DetailIntervalSeconds"/> and moves the fades toward it.
    /// </summary>
    private void UpdateUniverseDetail(float dt)
    {
        var n = _uniBodies.Count;
        if (n == 0 || _uniShowFade.Length != n) return;
        _uniDetailT += dt;
        if (_uniDetailT >= DetailIntervalSeconds)
        {
            _uniDetailT = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!OverviewDetailEnabled)
            {
                Array.Fill(_uniShown, true);
                Array.Fill(_uniExpanded, true);
                for (var i = 0; i < n; i++) _uniFolded[i] = _uniBodies[i].Body.HiddenChildren;
            }
            else
            {
                // Each body's on-screen scale at its distance: pixels per radian / distance (a world 1 m across at d metres is
                // pixelsPerRadian / d pixels across).
                var pixelsPerRadian = GetViewport().GetVisibleRect().Size.Y / (2f * MathF.Tan(Mathf.DegToRad(_camera.Fov) / 2f));
                var cam = _camera.GlobalPosition;
                for (var i = 0; i < n; i++)
                {
                    // A ring seen at a slant is an ellipse: its circumference (what holds the children apart) is about the
                    // mean of its axes, r (1 + |n·v|) / 2, so a ring seen edge-on opens later than one seen face-on.
                    var at = _uniBodies[i].At;
                    var d = MathF.Max(1f, cam.DistanceTo(at));
                    var slant = MathF.Abs(Gd(_uniBodies[i].Body.OrbitNormal).Dot((at - cam) / d));
                    _uniPpm[i] = pixelsPerRadian / d * (1f + slant) / 2f;
                }

                _uniApproached = ZoomedToward();
                var focus = new List<int>(2) { _uniSelected, _uniApproached };
                // Where you stand stays drawn, however far, but its subtree gets no lower bar (on a root that is everything).
                int[] here = _current != null ? [_current.FolderId] : [];
                var was = (bool[])_uniExpanded.Clone();
                for (var i = 0; i < n; i++) was[i] |= _uniWasExpanded[i];
                OverviewDetail.Compute(_uniBodies.Select(b => b.Body).ToList(), _uniPpm, focus, was, _uniShown, _uniExpanded, _uniFolded, pathOnly: here);
                Array.Clear(_uniWasExpanded);
                // Worlds on a ring that is open although crowded (the way to the selected world or where you stand: a
                // drive root's own ring) are drawn smaller than the usual 6 px, down to CrowdedMinPixels, so neighbours
                // stay apart instead of merging into one shape.
                for (var i = 0; i < n; i++)
                {
                    var p = _uniBodies[i].Body.ParentId;
                    if (p < 0 || !_uniIndex.TryGetValue(p, out var pi) || _uniChords[pi] <= 0)
                    {
                        _uniMinPx[i] = MinDrawnRadiusPixels;
                        continue;
                    }

                    var chordPx = _uniChords[pi] * pixelsPerRadian / MathF.Max(1f, cam.DistanceTo(_uniBodies[pi].At));
                    _uniMinPx[i] = Mathf.Clamp(chordPx * CrowdedRadiusShare, CrowdedMinPixels, MinDrawnRadiusPixels);
                }
            }

            DetailMsMax = Math.Max(DetailMsMax, sw.Elapsed.TotalMilliseconds);
        }

        var k = _uniDetailSnap ? 1f : 1 - Mathf.Exp(-dt * DetailFadeRatePerSecond);
        for (var i = 0; i < n; i++)
        {
            _uniShowFade[i] = Mathf.Lerp(_uniShowFade[i], _uniShown[i] ? 1f : 0f, k);
            _uniOpenFade[i] = Mathf.Lerp(_uniOpenFade[i], _uniExpanded[i] ? 1f : 0f, k);
        }

        _uniDetailSnap = false;
    }

    /// <summary>A world on a crowded open ring is drawn at most this share of the gap to its closest sibling as radius
    /// (0.4: two such discs leave a fifth of the gap between them), and never under <see cref="CrowdedMinPixels"/>.</summary>
    private const float CrowdedRadiusShare = 0.4f;

    /// <summary>The smallest radius a world on a crowded ring is drawn, pixels: still a dot you can see and click.</summary>
    private const float CrowdedMinPixels = 2.5f;

    /// <summary>Each body's least drawn radius in pixels now (see <see cref="CrowdedRadiusShare"/>) and each system's
    /// closest-children distance in the current layout, metres.</summary>
    private float[] _uniMinPx = [], _uniChords = [];

    private float MinPixelsAt(int i) => i < _uniMinPx.Length ? _uniMinPx[i] : MinDrawnRadiusPixels;

    /// <summary>How much a body is drawn now, 0…1 (its level-of-detail fade).</summary>
    private float ShowScale(int i) => i < _uniShowFade.Length ? _uniShowFade[i] : 1f;

    /// <summary>A body is drawn enough to pick, name and count (more than half faded in).</summary>
    private bool Drawn(int i) => ShowScale(i) > 0.5f;

    /// <summary>A system's children are drawn now, 0…1 (its ring's fade).</summary>
    private float OpenScale(int i) => i < _uniOpenFade.Length ? _uniOpenFade[i] : 1f;

    /// <summary>Worlds folded inside body <paramref name="i"/> now (0 when its system is open), for its name and the probes.</summary>
    private int FoldedAt(int i) => i < _uniFolded.Length ? _uniFolded[i] : 0;

    /// <summary>The haze batch for a layout: one camera-facing quad per system that can fold (colours set per frame).</summary>
    private void BuildHaze(IReadOnlyList<UniverseBody> bodies, Dictionary<int, int> childCount)
    {
        _uniHazeBodies.Clear();
        _uniHazeMesh = null;
        for (var i = 0; i < bodies.Count; i++)
        {
            if (childCount.GetValueOrDefault(bodies[i].NodeId) > 0 || bodies[i].HiddenChildren > 0) _uniHazeBodies.Add(i);
        }

        if (_uniHazeBodies.Count == 0) return;
        _uniHazeMesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = new QuadMesh { Size = new Vector2(2, 2) }, InstanceCount = _uniHazeBodies.Count };
        // Each haze's cluster of lights is seeded by its folder (PLANET-108), so it keeps its stars across relayouts.
        for (var k = 0; k < _uniHazeBodies.Count; k++) _uniHazeMesh.SetInstanceCustomData(k, new Color((uint)(bodies[_uniHazeBodies[k]].NodeId * 2654435761u) / (float)uint.MaxValue, 0, 0, 0));
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/overview_haze.gdshader") };
        _universe!.AddChild(new MultiMeshInstance3D { Multimesh = _uniHazeMesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>Every frame: each foldable system's haze, at its ring's size, as strong as it is folded and shown.</summary>
    private void PlaceHaze()
    {
        if (_uniHazeMesh == null) return;
        for (var k = 0; k < _uniHazeBodies.Count; k++)
        {
            var i = _uniHazeBodies[k];
            var b = _uniBodies[i].Body;
            var folded = FoldedAt(i);
            var alpha = folded <= 0 || !OverviewDetailEnabled ? 0f : ShowScale(i) * (1 - OpenScale(i)) * MathF.Min(HazeMaxAlpha, HazeBaseAlpha + HazeAlphaPerDecade * MathF.Log10(1 + folded));
            var r = MathF.Max(b.OrbitRadius * 1.1f, DrawnRadius(b, _uniBodies[i].At) * HazeMinRadii) * AppearScale(i);
            _uniHazeMesh.SetInstanceTransform(k, new Transform3D(Basis.Identity.Scaled(new Vector3(r, r, r)), _uniLocal[i]));
            _uniHazeMesh.SetInstanceColor(k, new Color(0.75f, 0.82f, 1f, alpha));
        }
    }

    /// <summary>
    /// A solid world's per-instance look (PLANET-92): its base colour and, in custom data, its second colour and pattern
    /// (<see cref="OverviewWorldLook"/>), or the old palette colour with --test-overview-look=plain. Partly listed worlds
    /// are darker, as before.
    /// </summary>
    private (Color Base, Color Custom) WorldLookOf(FsTree tree, UniverseBody b)
    {
        if (!OverviewLooksVaried)
        {
            var pal = PlanetArt.Palette(PlanetComposer.ThemeOf(tree, b.NodeId, _s.IsVisible));
            var h = (uint)(b.NodeId * 2654435761u);
            var c = pal.Accent.Lerp(pal.GroundHigh, 0.35f);
            c = Color.FromHsv(Mathf.PosMod(c.H + (h % 100 / 100f - 0.5f) * 0.08f, 1f), Mathf.Clamp(c.S * 1.1f, 0, 1), c.V * 0.95f);
            return (b.Discovery == Discovery.Partial ? c.Darkened(0.35f) : c, new Color(0, 0, 0, 0));
        }

        // A look is worked out from the folder's entries (a pass over them): kept per world until its listing changes,
        // so a relayout every 1.5 s does not walk every drawn folder's entries again.
        if (!ReferenceEquals(_lookCacheTree, tree))
        {
            _lookCache.Clear();
            _lookCacheTree = tree;
        }

        var node = tree[b.NodeId];
        if (!_lookCache.TryGetValue(b.NodeId, out var cached) || cached.Entries != node.Children.Count || cached.State != node.ScanState)
        {
            cached = (node.Children.Count, node.ScanState, OverviewWorldLook.Of(tree, b.NodeId, _s.IsVisible));
            _lookCache[b.NodeId] = cached;
        }

        var look = cached.Look;
        var baseColor = new Color(look.Base.X, look.Base.Y, look.Base.Z);
        var second = new Color(look.Second.X, look.Second.Y, look.Second.Z);
        if (b.Discovery == Discovery.Partial)
        {
            baseColor = baseColor.Darkened(0.35f);
            second = second.Darkened(0.35f);
        }

        // Pattern and phase packed in alpha as (pattern + phase) / 8 (overview_world.gdshader); the phase is kept below 1.
        return (baseColor, new Color(second.R, second.G, second.B, ((int)look.Pattern + MathF.Min(look.Phase, 0.99f)) / 8f));
    }

    /// <summary>Looks worked out so far, by node id, with the entry count and state they were made from (one tree's ids).</summary>
    private readonly Dictionary<int, (int Entries, DirScanState State, WorldLook Look)> _lookCache = [];
    private FsTree? _lookCacheTree;

    /// <summary>The solid worlds' shared material: the patterned shader (one draw for all), or the old standard material.</summary>
    private Material SolidWorldMaterial() => OverviewLooksVaried
        ? new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/overview_world.gdshader") }
        : new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.8f, RimEnabled = true, Rim = 0.45f, RimTint = 0.6f };

    // ---------------------------------------------------------------- probes (counts only)

    /// <summary>
    /// The overlap the player sees (PLANET-91's measure): of the worlds drawn and on screen, how many pairs have discs
    /// that overlap (centre distance below the sum of their drawn radii, viewport pixels), and how many worlds overlap
    /// at least one other. O(n²) over the drawn worlds: probes only.
    /// </summary>
    public (int Drawn, int Pairs, int Worlds) UniverseOverlap()
    {
        var vp = GetViewport().GetVisibleRect();
        var right = _camera.GlobalBasis.X;
        var discs = new List<(Vector2 C, float R)>();
        for (var i = 0; i < _uniBodies.Count; i++)
        {
            if (!Drawn(i)) continue;
            var at = _uniBodies[i].At;
            if (_camera.IsPositionBehind(at)) continue;
            var c = _camera.UnprojectPosition(at);
            if (!vp.HasPoint(c)) continue;
            var r = c.DistanceTo(_camera.UnprojectPosition(at + right * DrawnRadius(_uniBodies[i].Body, at)));
            discs.Add((c, r));
        }

        var pairs = 0;
        var over = new bool[discs.Count];
        for (var a = 0; a < discs.Count; a++)
        {
            for (var b = a + 1; b < discs.Count; b++)
            {
                if (discs[a].C.DistanceTo(discs[b].C) >= discs[a].R + discs[b].R) continue;
                pairs++;
                over[a] = over[b] = true;
            }
        }

        return (discs.Count, pairs, over.Count(x => x));
    }

    /// <summary>Worlds drawn now (more than half faded in), and worlds folded inside drawn systems (probes).</summary>
    public (int Drawn, int Folded) UniverseDetailCounts()
    {
        var drawn = 0;
        var folded = 0;
        for (var i = 0; i < _uniBodies.Count; i++)
        {
            if (!Drawn(i)) continue;
            drawn++;
            folded += FoldedAt(i);
        }

        return (drawn, folded);
    }

    /// <summary>The world the camera is zoomed toward, as the level of detail last saw it (probes).</summary>
    public int UniverseApproached => _uniApproached;

    /// <summary>A world is drawn in the overview now (probes).</summary>
    public bool UniverseDrawn(int id) => _uniIndex.TryGetValue(id, out var i) && Drawn(i);

    /// <summary>The look the overview gives a world (probes: stable across runs), or null when it is not a solid world.</summary>
    public (Color Base, Color Custom)? UniverseLook(int id) =>
        _uniIndex.TryGetValue(id, out var i) && _s.Tree is { } t && _uniBodies[i].Body.Discovery != Discovery.Unknown ? WorldLookOf(t, _uniBodies[i].Body) : null;
}
