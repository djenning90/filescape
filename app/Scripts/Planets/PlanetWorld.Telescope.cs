using FileScape.Core.Interaction;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// Looking through the overlook's telescope (AD-85, PLANET-125..127). DJ (2026-11-20): "The telescope's current action is
/// difficult to discover and feels underwhelming. E opens a list, but the object suggests something more … a useful way
/// to survey the surrounding worlds … frames useful destinations and makes their relationship to the current world
/// clearer … clear controls and a return to where I stood."
/// <para>
/// On the platform, E (or a jump and twirl beside the telescope, as at the lighthouse) puts your eye to it: the camera
/// rises to the eyepiece over <see cref="ScopeEnterSeconds"/>, a round field closes in, and the lens narrows onto one world
/// of this sky at a time — the worlds that hang here, a few of the ring's moons and the world above — each framed to fill
/// a share of the view, with its name, how it relates to the world you stand on, what is known of it, and where it is in
/// the sweep (<see cref="TelescopeSurvey"/> chooses, orders and words them). ← → (or A D) swing to the next world round
/// the sky (with one world or none they do nothing, and the key line leaves them out: PLANET-189), ↑ ↓ (W S) or the wheel
/// zoom, a drag looks around, a click aims at a world; Enter (or G) or a double-click on the framed world (PLANET-188)
/// flies there through the same departure as G (its pad first, PlanetWorld.Departure); T opens the whole overview with
/// that world chosen; J the list; Esc or E steps back. The traveller never moves: stepping back glides the camera down
/// to exactly where it was. The Lydian overlook plays throughout (MusicActivity: the telescope is the overlook's music).
/// </para>
/// <para>What a change must preserve: the view opens only on the platform with nothing in motion and closes before anything
/// travels (a departure, the overview, a navigation made elsewhere); the walker's place and the walking camera's own state
/// are untouched while it is open (the way back is the landing blend from where the eye was); while it is open the framed
/// world is the shared selection (the card shows it), and once it closes the selection is
/// <see cref="TelescopeSurvey.SelectionAfter"/>'s: the framed world only when you went on to it (Enter, a double-click,
/// T), otherwise the selection from before (PLANET-190, AD-98: a framed folder left selected was opened with Windows by a
/// later Enter, 2026-11-21; before, this said "G or Enter after closing still go there", but Enter outside the view is Open).</para>
/// </summary>
public partial class PlanetWorld
{
    // ---- the view (by eye, 2026-11-20; DJ to judge)
    /// <summary>The camera's rise to the eyepiece and the round field's close, seconds (a deliberate step, not a cut).</summary>
    private const float ScopeEnterSeconds = 0.9f;

    /// <summary>Stepping back: the glide from the eyepiece down to the walking camera, seconds (the landing blend's mechanism).</summary>
    private const float ScopeExitSeconds = 0.9f;

    /// <summary>The eyepiece is this far above the telescope's tripod top, metres (just over the tube, so the view clears it).</summary>
    private const float EyepieceAboveMetres = 0.35f;

    /// <summary>How fast the aim swings to a new world, per second (1 − e^(−rate·dt) of the gap a frame: ≈ 0.25 s to 1/e).</summary>
    private const float ScopeSlewRatePerSecond = 4f;

    /// <summary>How fast the lens follows its target field of view, per second.</summary>
    private const float ScopeZoomRatePerSecond = 3f;

    /// <summary>A framed world's diameter fills this share of the view's height (it reads as the subject, with sky round it).</summary>
    private const float ScopeFrameShare = 0.32f;

    /// <summary>
    /// At the eyepiece the near clip moves out to this many metres, so the telescope's own brass tube and the platform's
    /// rail, which stand round the eye, are not drawn across the view (seen 2026-11-20: the tube filled the field with a
    /// blurred gold disc). Restored to the walking camera's when you step back.
    /// </summary>
    private const float ScopeNearMetres = 1.6f;

    /// <summary>The planet counts as this share of its ground radius for hiding worlds behind it (a little under 1, so one just over the horizon still counts as seen).</summary>
    private const float ScopeHorizonShare = 0.97f;

    /// <summary>A world's framed size counts its glowing atmosphere: this many times its ground radius (by eye on the Starter Sky).</summary>
    private const float ScopeAtmosphereFactor = 1.25f;

    /// <summary>The lens's narrowest and widest vertical field of view, degrees (the walking camera's is 62°).</summary>
    private const float ScopeMinFovDegrees = 5f, ScopeMaxFovDegrees = 50f;

    /// <summary>One wheel notch or a held ↑/↓ for a second multiplies the zoom by these (in, out); the zoom stays within 0.35–2.5 of the framing.</summary>
    private const float ScopeWheelIn = 0.85f, ScopeWheelOut = 1.18f, ScopeKeyZoomPerSecond = 1.8f, ScopeMinZoom = 0.35f, ScopeMaxZoom = 2.5f;

    /// <summary>A drag looks this many degrees per screen pixel, at most <see cref="ScopeMaxLookDegrees"/> off the framed world (it stays the subject).</summary>
    private const float ScopeLookDegreesPerPixel = 0.06f, ScopeMaxLookDegrees = 25f;

    /// <summary>A press that travels less than this many pixels before release is a click (an aim), as on the surface.</summary>
    private const float ScopeClickPixels = 6f;

    /// <summary>A twirl counts as at the telescope within this distance of it along the ground, metres (the platform is ≈ 2.4 m across).</summary>
    private const float ScopeTwirlReachMetres = 1.6f;

    /// <summary>The words under the view with two worlds or more (probes check them); a sparser sky's are <see cref="TelescopeSurvey.KeysMarkup"/>'s.</summary>
    public static readonly string ScopeKeysText = TelescopeSurvey.KeysMarkup(2);

    /// <summary>The telescope panel's designed half width, viewport units (it narrows on a small window: <see cref="PanelFit"/>).</summary>
    private const float ScopePanelHalfWidth = 450f;

    private TelescopeSurvey? _scope;
    private float _scopeT, _scopeFov, _scopeZoom = 1f;
    private Transform3D _scopeFrom;
    private Vector3 _scopeLook;
    private Vector2 _scopeLookOffset; // degrees: yaw (+ right), pitch (+ up), from a drag
    private Vector2? _scopePress;
    private bool _scopeDragged, _scopeQueuedByTwirl;

    /// <summary>The press is the second of a double-click, and the click before it aimed the view (PLANET-188: a double-click travels only to a world already framed).</summary>
    private bool _scopePressDouble, _scopeLastClickAimed;

    /// <summary>The shared selection when the view opened: given back when you step back (PLANET-190).</summary>
    private int _scopeSelectionBefore = -1;

    private NVector3 _scopeFeet;
    private PanelContainer _scopePanel = null!;
    private Label _scopeName = null!, _scopeRelation = null!, _scopeDetail = null!;

    /// <summary>The view's controls as key caps (PLANET-153: they were one 13 px line, trimmed at every window size).</summary>
    private RichTextLabel _scopeKeys = null!;
    private ColorRect _scopeField = null!;
    private ShaderMaterial _scopeFieldMaterial = null!;

    /// <summary>The telescope's view is open (probes).</summary>
    public bool TelescopeViewOpen => _scope != null;

    /// <summary>Times the view was opened, and how the last one was (E or a twirl), for probes.</summary>
    public int TelescopeViews { get; private set; }

    /// <inheritdoc cref="TelescopeViews"/>
    public string LastTelescopeEntry { get; private set; } = "";

    /// <summary>How the view last closed (probes: a step back gives back the selection, PLANET-190).</summary>
    public TelescopeExit LastTelescopeExit { get; private set; }

    /// <summary>The selection when the view last opened (probes).</summary>
    public int TelescopeSelectionBefore => _scopeSelectionBefore;

    /// <summary>The framed world's folder (−1 without one), how many the survey frames, and its caption (probes).</summary>
    public int TelescopeTargetId => _scope?.Current?.World.NodeId ?? -1;

    /// <inheritdoc cref="TelescopeTargetId"/>
    public int TelescopeTargetCount => _scope?.Targets.Count ?? 0;

    /// <inheritdoc cref="TelescopeTargetId"/>
    public string TelescopeCaption => _scope == null ? "" : $"{_scopeName.Text}\n{_scopeRelation.Text}\n{_scopeDetail.Text}";

    /// <summary>Degrees between where the camera looks and the framed world (probes: small once it has swung there).</summary>
    public float TelescopeAimErrorDegrees => _scope?.Current is { } t && _current != null && TargetPoint(t.World) is { } p
        ? Mathf.RadToDeg((-_camera.GlobalBasis.Z).AngleTo((p - _camera.GlobalPosition).Normalized())) : -1f;

    /// <summary>Where the framed world is on screen (viewport units), or null (probes double-click it, PLANET-188).</summary>
    public Vector2? TelescopeTargetScreen => _scope?.Current is { } t && TargetPoint(t.World) is { } p && !_camera.IsPositionBehind(p) ? _camera.UnprojectPosition(p) : null;

    /// <summary>The traveller's distance from where it stood when the view opened, metres (probes: 0 — it never moves).</summary>
    public float TelescopeStandError => _scope != null || _scopeFeet != default ? NVector3.Distance(_walker.Feet, _scopeFeet) : 0f;

    /// <summary>Builds the view's round field and its caption panel (hidden), from <see cref="BuildOverlay"/>.</summary>
    private void BuildTelescopeView(Control root)
    {
        // The round field: dark outside a soft circle (its radius a share of the shorter side), a faint warm rim.
        _scopeFieldMaterial = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type canvas_item;
                    uniform float open01 = 0.0;   // 0 hidden … 1 fully closed in
                    uniform vec2 size = vec2(1600.0, 900.0);
                    void fragment() {
                        vec2 p = (UV - 0.5) * size;
                        float r = length(p) / (0.5 * min(size.x, size.y));
                        float radius = mix(1.9, 0.96, open01);          // the field closes from beyond the corners
                        float dark = smoothstep(radius - 0.06, radius + 0.02, r);
                        float rim = (1.0 - smoothstep(0.0, 0.025, abs(r - radius + 0.02))) * 0.35 * open01;
                        COLOR = vec4(mix(vec3(0.95, 0.8, 0.5), vec3(0.01, 0.01, 0.03), dark), max(dark * 0.97, rim) * open01);
                    }
                    """,
            },
        };
        _scopeField = new ColorRect { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Material = _scopeFieldMaterial };
        _scopeField.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_scopeField);
        root.MoveChild(_scopeField, 1); // just over the flash, under every label and panel

        _scopePanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = PanelStyle();
        style.BgColor = style.BgColor with { A = 0.78f };
        _scopePanel.AddThemeStyleboxOverride("panel", style);
        _scopePanel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        // Viewport units: 900 wide, centred, its bottom 66 above the screen's edge (clear of the HUD's key line under it).
        _scopePanel.OffsetLeft = -ScopePanelHalfWidth; _scopePanel.OffsetRight = ScopePanelHalfWidth; _scopePanel.OffsetTop = -196; _scopePanel.OffsetBottom = -66;
        _scopePanel.GrowVertical = Control.GrowDirection.Begin; // the wrapped key line grows the panel upwards
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        _scopeName = MakeLabel(24, new Color(1f, 0.93f, 0.72f), HorizontalAlignment.Center, outline: 0);
        _scopeRelation = MakeLabel(17, new Color(0.75f, 0.92f, 1f) /* the telescope's light */, HorizontalAlignment.Center, outline: 0);
        _scopeDetail = MakeLabel(14, new Color(0.85f, 0.88f, 0.98f), HorizontalAlignment.Center, outline: 0);
        _scopeKeys = MakeRichLine(UiTextStyle.Small, new Color(0.7f, 0.74f, 0.86f), outline: 0);
        _scopeKeys.AddThemeConstantOverride("line_separation", 3); // room for the caps' backgrounds when it wraps
        SetRich(_scopeKeys, ScopeKeysText, UiTextStyle.Small);
        box.AddChild(_scopeName);
        box.AddChild(_scopeRelation);
        box.AddChild(_scopeDetail);
        box.AddChild(_scopeKeys);
        _scopePanel.AddChild(box);
        root.AddChild(_scopePanel);
    }

    /// <summary>The eyepiece (world frame), or null away from a telescope.</summary>
    private Vector3? Eyepiece() => TelescopePoint is { } p ? p + Gd(_walker.Up) * EyepieceAboveMetres : null;

    /// <summary>Where a framed world is drawn now (world frame), or null if it is no longer in the scene.</summary>
    private Vector3? TargetPoint(SurveyCandidate w)
    {
        if (w.Relation == SurveyRelation.Parent) return _parent?.FolderId == w.NodeId ? _parent.Node.GlobalPosition : null;
        if (_children.TryGetValue(w.NodeId, out var b)) return b.Node.GlobalPosition;
        return ToGlobal(Gd(w.Position));
    }

    /// <summary>The worlds in this sky as the survey's candidates: the hung worlds and links, the ring's moons, the world above.</summary>
    private List<SurveyCandidate> SurveyCandidates()
    {
        var list = new List<SurveyCandidate>();
        if (_current == null || _s.Tree is not { } tree) return list;
        foreach (var sky in _current.Comp.SkyBodies)
        {
            if (sky.IsParent || sky.TargetId < 0 || !tree.Contains(sky.TargetId)) continue;
            var n = tree[sky.TargetId];
            var relation = n.Kind == Core.Model.NodeKind.Link ? SurveyRelation.Link : sky.InBelt ? SurveyRelation.Moon : SurveyRelation.Child;
            // A hung world is drawn at its node's scale (floored, so a large folder can be drawn bigger than its sky radius).
            var (at, radius) = _children.TryGetValue(sky.TargetId, out var b)
                ? (Nv(ToLocal(b.Node.GlobalPosition)), b.Comp.Shape.Radius * b.Node.Basis.Scale.X)
                : (sky.Position, sky.Radius);
            list.Add(SurveyCandidate.From(tree, sky.TargetId, relation, at, MathF.Max(radius, sky.Radius) * ScopeAtmosphereFactor, Arrivals(sky.TargetId) > 0));
        }

        if (_parent is { } p && tree.Contains(p.FolderId))
        {
            var scale = p.Node.Basis.Scale.X;
            list.Add(SurveyCandidate.From(tree, p.FolderId, SurveyRelation.Parent, Nv(ToLocal(p.Node.GlobalPosition)), p.Comp.Shape.Radius * scale * ScopeAtmosphereFactor, Arrivals(p.FolderId) > 0));
        }

        return list;
    }

    /// <summary>E on the platform or a twirl beside the telescope: put your eye to it (nothing happens while anything moves).</summary>
    private void OpenTelescopeView(string how)
    {
        if (_scope != null || Busy || _hop != null || _universe != null || _current?.Comp.Overlook is not { } ov || Eyepiece() is not { } eye) return;
        CloseOverlays();
        EndOrbit();
        _leftPress = null;
        _jumpQueued = false;
        var tree = _s.Tree!;
        var pointing = PlanetFrame.North(ov.Direction); // the telescope points over the mesh's −Z edge: the overlook's north (PlanetVisual.BuildOverlook)
        // The ground's radius under the overlook stands in for the planet's (it is the horizon the eye looks over).
        _scopeSelectionBefore = _s.Selection.Selected;
        _scope = TelescopeSurvey.Plan(SurveyCandidates(), Nv(ToLocal(eye)), _walker.Up, pointing, _s.Selection.Selected, _current.Comp.Shape.GroundRadius(ov.Direction) * ScopeHorizonShare);
        SetRich(_scopeKeys, TelescopeSurvey.KeysMarkup(_scope.Targets.Count), UiTextStyle.Small); // PLANET-189: only the keys that do something here
        _scopeLastClickAimed = false;
        _scopeT = 0;
        _scopeFrom = _camera.GlobalTransform;
        _scopeLook = _camera.GlobalPosition - _camera.GlobalBasis.Z * 10f; // the view turns from where it was looking
        _scopeFov = _camera.Fov;
        _scopeZoom = 1f;
        _scopeLookOffset = Vector2.Zero;
        _scopePress = null;
        _scopeFeet = _walker.Feet;
        TelescopeViews++;
        LastTelescopeEntry = how;
        _current.Visual?.FlareTelescope();
        _audio.Inspect();
        SetPrompt("");
        _hintTime = 0; // the overlook's hint would sit under the caption
        _scopePanel.Visible = true;
        _scopeField.Visible = true;
        AimScope();
        GD.Print($"[planets] telescope: open by {how}, {_scope.Targets.Count} worlds framed" + (_scope.OmittedMoons > 0 ? $" (+{_scope.OmittedMoons} moons)" : "") + $" from {tree.DisplayName(_current.FolderId)}");
    }

    /// <summary>
    /// Steps back from the eyepiece: the camera glides down to the walking camera, which has not moved (the landing blend),
    /// and the lens widens back (the walking camera's own field-of-view recovery). The selection becomes what
    /// <paramref name="exit"/> leaves (<see cref="TelescopeSurvey.SelectionAfter"/>, PLANET-190): the framed world for a
    /// trip or the overview, the selection from before otherwise.
    /// </summary>
    private void CloseTelescopeView(bool glide = true, TelescopeExit exit = TelescopeExit.StepBack)
    {
        if (_scope == null) return;
        var framed = _scope.Current?.World.NodeId ?? -1;
        _scope = null;
        var keep = TelescopeSurvey.SelectionAfter(exit, _scopeSelectionBefore, framed);
        if (keep >= 0 && _s.Tree is { } tree && !tree.Contains(keep)) keep = -1; // the tree changed under it
        if (_s.Selection.Selected != keep) _s.Selection.Select(keep, recordHistory: false); // the framing was a look, not a choice
        LastTelescopeExit = exit;
        _scopePanel.Visible = false;
        _scopeField.Visible = false;
        _scopePress = null;
        _avatar.Visible = true;
        _camera.Near = SurfaceNearMetres;
        ShowLandmark(true);
        if (glide)
        {
            _camBlendFrom = _camera.GlobalTransform;
            _camBlend = _camBlendTotal = ScopeExitSeconds;
        }

        GD.Print("[planets] telescope: step back");
    }

    /// <summary>
    /// The world's landmark (the book tower on the Starter Sky, a bell tower, a lighthouse) stands a few metres from the
    /// overlook and hid the worlds behind it from the eyepiece (seen 2026-11-20: the book tower's pages filled the field);
    /// it is hidden while the eye is at the eyepiece and shown again as you step back.
    /// </summary>
    private void ShowLandmark(bool visible)
    {
        if (_current?.Visual?.GetNodeOrNull<Node3D>("Landmark") is { } lm && lm.Visible != visible) lm.Visible = visible;
    }

    /// <summary>Selects the framed world (the shared selection: the card shows it) and writes its caption.</summary>
    private void AimScope()
    {
        if (_scope == null || _current == null || _s.Tree is not { } tree) return;
        var here = tree.DisplayName(_current.FolderId);
        _scopeLookOffset = Vector2.Zero;
        _scopeZoom = 1f;
        if (_scope.Current is not { } t)
        {
            _scopeName.Text = "An empty sky";
            _scopeRelation.Text = TelescopeSurvey.EmptyText(here);
            _scopeDetail.Text = "";
            return;
        }

        _s.Select(t.World.NodeId);
        _scopeName.Text = t.World.Name;
        _scopeRelation.Text = TelescopeSurvey.RelationText(t, here);
        var moons = _scope.OmittedMoons > 0 ? $"   ·   {_scope.OmittedMoons} more moons in the ring (J lists them)" : "";
        if (_scope.BelowHorizon > 0) moons += $"   ·   {_scope.BelowHorizon} below the horizon (T shows them)";
        _scopeDetail.Text = $"{TelescopeSurvey.DetailText(t.World)}   ·   {_scope.Counter}{moons}";
    }

    /// <summary>One frame with the view open (instead of walking): the rise to the eyepiece, the swing to the framed world, the lens, the field.</summary>
    private void UpdateTelescopeView(float dt, bool blockedKeys)
    {
        if (_scope == null || _current == null || Eyepiece() is not { } eye) { CloseTelescopeView(exit: TelescopeExit.Interrupted); return; }
        var scopeHalf = PanelFit.Width(2 * ScopePanelHalfWidth, GetViewport().GetVisibleRect().Size.X, ScreenTextMargin) / 2; // PLANET-155
        _scopePanel.OffsetLeft = -scopeHalf;
        _scopePanel.OffsetRight = scopeHalf;
        _scopeT += dt;
        var rise = Mathf.Clamp(_scopeT / ScopeEnterSeconds, 0, 1);
        var k = rise * rise * (3 - 2 * rise);
        if (!blockedKeys)
        {
            var zoomKeys = (Godot.Input.IsPhysicalKeyPressed(Key.S) || Godot.Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0) - (Godot.Input.IsPhysicalKeyPressed(Key.W) || Godot.Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0);
            if (zoomKeys != 0) _scopeZoom = Mathf.Clamp(_scopeZoom * MathF.Pow(ScopeKeyZoomPerSecond, zoomKeys * dt), ScopeMinZoom, ScopeMaxZoom);
        }

        var up = Gd(_walker.Up);
        var pos = _scopeFrom.Origin.Lerp(eye, k);
        var fovTarget = ScopeMaxFovDegrees;
        var look = _scopeLook;
        if (_scope.Current is { } t && TargetPoint(t.World) is { } p)
        {
            var dist = MathF.Max(0.5f, (p - eye).Length());
            fovTarget = Mathf.Clamp(Mathf.RadToDeg(2 * MathF.Atan(t.World.Radius / (dist * ScopeFrameShare))) * _scopeZoom, ScopeMinFovDegrees, ScopeMaxFovDegrees);
            var dir = (p - eye).Normalized();
            // A drag looks round the framed world: yaw about up, pitch about the view's right.
            var right = dir.Cross(up);
            if (right.LengthSquared() > 1e-8f) dir = dir.Rotated(right.Normalized(), Mathf.DegToRad(_scopeLookOffset.Y));
            dir = dir.Rotated(up, -Mathf.DegToRad(_scopeLookOffset.X));
            look = eye + dir * dist;
        }

        _scopeLook = _scopeLook.Lerp(look, 1 - MathF.Exp(-ScopeSlewRatePerSecond * dt));
        _scopeFov = Mathf.Lerp(_scopeFov, Mathf.Lerp(WalkingFovDegrees, fovTarget, k), 1 - MathF.Exp(-ScopeZoomRatePerSecond * dt));
        var forward = (_scopeLook - pos).Normalized();
        var camUp = MathF.Abs(forward.Dot(up)) > 0.995f ? _camUp : up; // straight up or down: keep the walking camera's up
        _camera.LookAtFromPosition(pos, pos + forward, camUp);
        _camera.Fov = _scopeFov;
        _avatar.Visible = k < 0.6f; // your eye is at the eyepiece: the traveller would fill the view behind it
        _camera.Near = k < 0.6f ? SurfaceNearMetres : ScopeNearMetres;
        ShowLandmark(k < 0.6f);
        _scopeFieldMaterial.SetShaderParameter("open01", k);
        _scopeFieldMaterial.SetShaderParameter("size", GetViewport().GetVisibleRect().Size);
        _scopePanel.Modulate = new Color(1, 1, 1, k);
    }

    /// <summary>Keys while the view is open (from <see cref="UnhandledInput"/>): true when consumed; any other key steps back first and goes on.</summary>
    private bool TelescopeKey(InputEvent e)
    {
        if (_scope == null || e is not InputEventKey { Pressed: true } k) return false;
        switch (k.PhysicalKeycode)
        {
            case Key.Left or Key.A or Key.Right or Key.D:
                // PLANET-189: with one world or none there is nowhere to swing; the key does nothing (no sound, no re-aim).
                if (!_scope.Step(k.PhysicalKeycode is Key.Right or Key.D ? 1 : -1)) return true;
                _audio.Select();
                AimScope();
                return true;
            case Key.Up or Key.Down or Key.W or Key.S or Key.Space or Key.Shift:
                return true; // the zoom is read per frame; nothing jumps or runs while you look
            case Key.Escape or Key.E:
                if (!k.Echo) CloseTelescopeView();
                return true;
            case Key.Enter or Key.KpEnter or Key.G:
                if (!k.Echo) TravelFromScope("Enter");
                return true;
        }

        if (!k.Echo && k.IsAction(InputBindings.FlightToggle))
        {
            CloseTelescopeView(glide: false, exit: TelescopeExit.Overview); // the overview opens from the eyepiece (its zoom out is the transition)
            ToggleUniverse(); // the framed world is the shared selection: Enter there lands on it
            return true;
        }

        if (!k.Echo && k.IsAction(InputBindings.Destinations))
        {
            CloseTelescopeView();
            ToggleContents();
            return true;
        }

        // What the telescope's own keys left is Core's PlayCommands (AD-92): Backspace, U, L, Ctrl+L… step back first and
        // then act; Help, Settings and the key open over the view; F, B, X and the legacy-view switches do nothing here.
        if (InputBindings.CommandOf(k) is not { } command) return true; // an unbound key: nothing to do, and the view stays
        if (k.Echo) return true;
        switch (PlayCommands.Answer(PlayContext.Telescope, command))
        {
            case CommandAnswer.StepBackThenAct:
                CloseTelescopeView();
                return false; // the shared command runs (Main.HandleCommand)
            case CommandAnswer.Act:
                return false;
            default:
                return !_s.CommandActs(PlayContext.Telescope, command); // consumed (a legacy switch says once where the views went)
        }
    }

    /// <summary>The mouse while the view is open (from <see cref="Input"/>): a drag looks round, a click aims at a world, the wheel zooms.</summary>
    private bool TelescopeMouse(InputEvent e)
    {
        if (_scope == null) return false;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } w when !OverHud(w.Position):
                _scopeZoom = Mathf.Clamp(_scopeZoom * (w.ButtonIndex == MouseButton.WheelUp ? ScopeWheelIn : ScopeWheelOut), ScopeMinZoom, ScopeMaxZoom);
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right, Pressed: true } press when !_pointerBlocked && !OverHud(press.Position):
                GetViewport().GuiReleaseFocus();
                _scopePress = press.Position;
                _scopePressDouble = press.DoubleClick && press.ButtonIndex == MouseButton.Left;
                _scopeDragged = false;
                return true;
            case InputEventMouseMotion m when _scopePress is { } at:
                if (!_scopeDragged && m.Position.DistanceTo(at) < ScopeClickPixels) return true;
                _scopeDragged = true;
                _scopeLookOffset = (_scopeLookOffset + new Vector2(m.ScreenRelative.X, -m.ScreenRelative.Y) * ScopeLookDegreesPerPixel).Clamp(-Vector2.One * ScopeMaxLookDegrees, Vector2.One * ScopeMaxLookDegrees);
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right, Pressed: false } release when _scopePress != null:
                _scopePress = null;
                if (_scopeDragged || release.ButtonIndex != MouseButton.Left)
                {
                    _scopeLastClickAimed = false; // a drag between two clicks makes them two clicks
                    return true;
                }

                // PLANET-188 (TelescopeSurvey.Click): a click aims at another world; a double-click on the framed one flies there.
                var hit = Pick(release.Position) is { Id: >= 0 } h ? h.Id : -1;
                var action = _scope.Click(hit, _scopePressDouble, _scopeLastClickAimed);
                _scopeLastClickAimed = action == TelescopeClick.Aim;
                if (action == TelescopeClick.Aim)
                {
                    _audio.Select();
                    AimScope();
                }
                else if (action == TelescopeClick.Travel)
                {
                    TravelFromScope("double-click");
                }

                return true;
        }

        return false;
    }

    /// <summary>Times the view started a trip, and by what (Enter or a double-click), for probes.</summary>
    public int TelescopeTrips { get; private set; }

    /// <inheritdoc cref="TelescopeTrips"/>
    public string LastTelescopeTripBy { get; private set; } = "";

    /// <summary>
    /// Enter, G or a double-click on the framed world: step back (keeping it selected) and leave for it through the same
    /// departure as G, from the launcher that leads there (AD-84). Nothing with an empty sky.
    /// </summary>
    private void TravelFromScope(string by)
    {
        if (_scope?.Current is not { } t) return;
        var id = t.World.NodeId;
        CloseTelescopeView(exit: TelescopeExit.Travel);
        _s.Select(id);
        TelescopeTrips++;
        LastTelescopeTripBy = by;
        DepartFor(id, DepartureEntry.Telescope); // travel: TelescopeEnter
    }

    /// <summary>A twirl (the air jump) on the platform beside the telescope: look through it once you land (true: the twirl's other uses do not act).</summary>
    private bool TwirlAtTelescope()
    {
        if (!OnOverlook || OnBenchWorld || TelescopePoint is not { } tp) return false;
        var ground = Nv(ToLocal(tp));
        if (PlanetShape.Angle(_walker.Up, NVector3.Normalize(ground)) * (_current?.Comp.Shape.Radius ?? 1) > ScopeTwirlReachMetres) return false;
        _scopeQueuedByTwirl = true;
        return true;
    }

    /// <summary>Every walking frame: a twirl at the telescope opens the view once you are back on your feet.</summary>
    private bool OpenTelescopeAfterTwirl()
    {
        if (!_scopeQueuedByTwirl || !_walker.Grounded) return false;
        _scopeQueuedByTwirl = false;
        if (!OnOverlook) return false;
        OpenTelescopeView("twirl");
        return _scope != null;
    }

    /// <summary>The probe state's telescope-view fields (a JSON fragment ending in a comma).</summary>
    private string TelescopeViewStateJson() => System.FormattableString.Invariant(
        $"\"scope\":{{\"open\":{(_scope != null ? "true" : "false")},\"views\":{TelescopeViews},\"entry\":\"{LastTelescopeEntry}\",\"target\":{TelescopeTargetId},\"targets\":{TelescopeTargetCount},\"aimError\":{TelescopeAimErrorDegrees:0.##},\"fov\":{(_scope != null ? _camera.Fov : 0):0.#},\"standError\":{TelescopeStandError:0.###},\"exit\":\"{LastTelescopeExit}\",\"selectionBefore\":{_scopeSelectionBefore},\"trips\":{TelescopeTrips},\"tripBy\":\"{LastTelescopeTripBy}\",\"canStep\":{(_scope?.CanStep == true ? "true" : "false")}}},");
}
