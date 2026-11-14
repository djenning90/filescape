using System.Globalization;
using FileScape.App.Places;
using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// Finding, inspecting and opening on a planet (PLANET-13): the nearest thing in front of you is the focus and the
/// prompt says what E (look closer) or Space (on a launcher) will do; E selects it in the shared session (the card
/// shows its metadata and actions), and shows a preview: the picture for an image, the first lines for text, a few
/// seconds for a sound. Enter opens it with Windows, with the session's usual confirmations. Clicking an object or a
/// world in the sky selects it; double-clicking a world launches there. J opens the planet's list (every folder and
/// file here, filterable: the direct route for folders too large to show as objects).
/// </summary>
public partial class PlanetWorld
{
    private PlanetItem? _focusItem;
    private PlanetLauncher? _focusLauncher;
    private bool _focusTelescope;
    private int _prepared = -1;
    private bool _orbiting;
    private Vector2 _orbitStart;

    public int FocusNodeId => _focusItem?.NodeId ?? _focusLauncher?.TargetId ?? -1;
    public string PromptText => _prompt.Text;

    // ------------------------------------------------------------------ input

    /// <summary>A left press on the world, not yet a click or a drag (PLANET-24): it becomes a drag after
    /// <see cref="DragThresholdPixels"/> of travel, otherwise a click on release.</summary>
    private Vector2? _leftPress;
    private bool _leftPressDouble;
    private MouseButton _orbitButton = MouseButton.None;
    private bool _pointerBlocked;
    private bool _lastLeftWasDrag;

    /// <summary>Set by Main as each event arrives (focus can return with the very click that should start a look).</summary>
    public bool PointerBlocked { set => _pointerBlocked = value; }

    /// <summary>How far (screen pixels) a held left button travels before the press is a drag, not a click.</summary>
    public const float DragThresholdPixels = 6f;

    /// <summary>How fast ← → turn the camera (and orbit the overview), degrees per second.</summary>
    public const float KeyLookDegreesPerSecond = 110f;

    /// <summary>A left- or right-drag is looking around now (probes).</summary>
    public bool Orbiting => _orbiting;

    /// <summary>
    /// From Main._Input: the camera drags and the wheel (before the GUI sees them). Left-drag and right-drag both look
    /// around (AD-47); a left press over the world is only a click if it is released before it travels
    /// <see cref="DragThresholdPixels"/>, and the click acts on release, so a drag never selects or launches whatever
    /// happens to be under the cursor when it ends. Presses over the HUD and the planet's panels go to the GUI.
    /// </summary>
    public bool Input(InputEvent e)
    {
        // Releases first, whatever else is going on: a drag always ends with its button.
        if (e is InputEventMouseButton { Pressed: false } up && (up.ButtonIndex == MouseButton.Left || up.ButtonIndex == MouseButton.Right))
        {
            if (_orbiting && up.ButtonIndex == _orbitButton)
            {
                EndOrbit();
                _leftPress = null;
                return true;
            }

            if (up.ButtonIndex == MouseButton.Left && _leftPress is { } at)
            {
                _leftPress = null;
                Click(at, _leftPressDouble);
                return true;
            }
        }

        if (_universe != null && UniverseInput(e)) return true;
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right or MouseButton.Left } press when !_orbiting && !_pointerBlocked && !OverHud(press.Position):
                // The world under the cursor: GUI keyboard focus (a list's filter, say) must not end the look (PLANET-25).
                GetViewport().GuiReleaseFocus();
                HurryBlend();
                if (press.ButtonIndex == MouseButton.Right)
                {
                    BeginOrbit(MouseButton.Right, press.Position);
                    return true;
                }

                _leftPress = press.Position;
                // A double-click only counts when the press before it was a click too (a drag warps the cursor back to its
                // start, so a click right after a quick drag would otherwise read as a double-click and launch).
                _leftPressDouble = press.DoubleClick && !_lastLeftWasDrag;
                _lastLeftWasDrag = false;
                return true;
            case InputEventMouseMotion mm when _leftPress is { } from && !_orbiting:
                if (mm.Position.DistanceTo(from) >= DragThresholdPixels)
                {
                    BeginOrbit(MouseButton.Left, from);
                    Look(mm.Position - from);
                }

                return true;
            case InputEventMouseMotion mm when _orbiting:
                Look(mm.ScreenRelative);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel when _universe == null && !OverHud(wheel.Position):
                _camDistance = Mathf.Clamp(_camDistance * (wheel.ButtonIndex == MouseButton.WheelUp ? 0.9f : 1.1f), 3.5f, 16f);
                return true;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } when _orbiting:
                EndOrbit();
                return true;
        }

        return false;
    }

    private void BeginOrbit(MouseButton button, Vector2 start)
    {
        if (button == MouseButton.Left) _lastLeftWasDrag = true;
        _orbiting = true;
        _orbitButton = button;
        _orbitStart = start;
        _leftPress = null;
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Captured;
    }

    /// <summary>Mouse travel while dragging: the overview orbits, the surface camera turns.</summary>
    private void Look(Vector2 screenPixels)
    {
        if (_universe != null) OrbitUniverse(screenPixels);
        else OrbitCamera(screenPixels);
    }

    /// <summary>A left click released without a drag: select what is under it (double-click: go there).</summary>
    private void Click(Vector2 at, bool doubleClick)
    {
        if (_universe != null)
        {
            var uid = PickUniverse(at);
            if (uid < 0) return;
            SelectInUniverse(uid);
            if (doubleClick) BeginUniverseLanding(uid);
            return;
        }

        if (Busy) return;
        if (ClickedBenchTile(at, doubleClick)) return;
        var hit = Pick(at);
        if (hit.Id < 0)
        {
            ClickedBench(at);
            return;
        }

        if (doubleClick) Activate(hit.Id, hit.Sky);
        else
        {
            _s.Select(hit.Id);
            _audio.Select();
        }
    }

    private void EndOrbit()
    {
        _leftPress = null;
        if (!_orbiting) return;
        _orbiting = false;
        _orbitButton = MouseButton.None;
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        if (_orbitStart != Vector2.Zero) Godot.Input.WarpMouse(_orbitStart);
    }

    private void OrbitCamera(Vector2 screenPixels)
    {
        _sinceOrbitSeconds = 0;
        var up = Gd(_walker.Up);
        _camFwd = _camFwd.Rotated(up, -screenPixels.X * Mathf.DegToRad(0.25f)).Normalized();
        _camPitchDegrees = Mathf.Clamp(_camPitchDegrees + screenPixels.Y * 0.15f, -8f, 70f);
    }

    /// <summary>From Main._UnhandledInput: Space, E, clicks. Returns true when consumed (other keys go to the shared commands).</summary>
    public bool UnhandledInput(InputEvent e)
    {
        if (_universe != null) return UniverseCommand(e);
        if (e is InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true }) HurryBlend();
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } k when k.IsAction(InputBindings.Jump):
                _jumpQueued = true;
                return true;
            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.E }:
                if (!Busy) InspectFocus();
                return true;
            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.N, CtrlPressed: false }:
                _audio.ToggleMute();
                _s.Toast(_audio.Muted ? "Sound off (N turns it on)." : "Sound on.");
                return true;
            case InputEventKey { Pressed: true, Echo: false } j when j.IsAction(InputBindings.Destinations):
                ToggleContents();
                return true;
        }

        return false;
    }

    private void Activate(int id, bool sky)
    {
        var tree = _s.Tree;
        if (tree == null || !tree.Contains(id)) return;
        _s.Select(id);
        if (sky || tree[id].IsDirectory || tree[id].Kind == NodeKind.Link) LaunchToSelection();
        else Approach(id);
    }

    /// <summary>A ray from the camera against the objects on this planet and the worlds in the sky.</summary>
    public (int Id, bool Sky) Pick(Vector2 screen)
    {
        if (_current == null) return (-1, false);
        var o = _camera.ProjectRayOrigin(screen);
        var d = _camera.ProjectRayNormal(screen);
        var best = float.MaxValue;
        var id = -1;
        var sky = false;
        void Test(Godot.Vector3 c, float r, int nodeId, bool isSky)
        {
            var oc = o - c;
            var b = oc.Dot(d);
            var cc = oc.LengthSquared() - r * r;
            var disc = b * b - cc;
            if (disc < 0) return;
            var t = -b - MathF.Sqrt(disc);
            if (t < 0 || t >= best) return;
            best = t;
            id = nodeId;
            sky = isSky;
        }

        var surf = _current.Comp.Surface;
        foreach (var it in _current.Comp.Items)
        {
            if (it.NodeId < 0) continue;
            Test(Gd(surf.PointAt(it.Direction, 0.55f * it.Scale)), 0.65f * it.Scale, it.NodeId, false);
        }

        foreach (var l in _current.Comp.Launchers) Test(Gd(surf.PointAt(l.Direction, 0.3f)), 1.3f, l.TargetId, false);
        foreach (var (fid, body) in _children) Test(body.Node.GlobalPosition, body.Node.Transform.Basis.Scale.X * body.Comp.Shape.Radius * 1.05f, fid, true);
        foreach (var moon in _current.Comp.SkyBodies)
        {
            if (moon.InBelt) Test(Gd(moon.Position), MathF.Max(moon.Radius * 1.4f, 1.2f), moon.TargetId, true); // the ring of small moons: click to select, G to go
        }

        if (_parent != null) Test(_parent.Node.GlobalPosition, _parent.Node.Transform.Basis.Scale.X * _parent.Comp.Shape.Radius, _parent.FolderId, true);
        // The ground in front blocks what is behind it.
        var groundT = RayPlanet(o, d, surf.Radius);
        if (groundT is { } gt && gt < best) return (-1, false);
        return (id, sky);
    }

    private static float? RayPlanet(Godot.Vector3 o, Godot.Vector3 d, float r)
    {
        var b = o.Dot(d);
        var c = o.LengthSquared() - r * r;
        var disc = b * b - c;
        if (disc < 0) return null;
        var t = -b - MathF.Sqrt(disc);
        return t > 0 ? t : null;
    }

    // ------------------------------------------------------------------ focus and prompts

    /// <summary>The launcher pad under the traveller, if any.</summary>
    private PlanetLauncher? FocusLauncher()
    {
        if (_current == null) return null;
        foreach (var l in _current.Comp.Launchers)
        {
            if (PlanetShape.Angle(_walker.Up, l.Direction) * _current.Comp.Shape.Radius < PadRadius + 0.25f) return l;
        }

        return null;
    }

    private const float PadRadius = 1.3f;

    private void UpdateFocus()
    {
        var tree = _s.Tree;
        if (_current == null || tree == null) return;
        _focusLauncher = FocusLauncher();
        if (_focusLauncher is { } pad && pad.TargetId != _prepared)
        {
            _prepared = pad.TargetId;
            Prepare(pad.TargetId);
        }
        _focusItem = null;
        _focusTelescope = false;
        if (_focusLauncher == null)
        {
            var best = 2.8f;
            var r = _current.Comp.Shape.Radius;
            foreach (var it in _current.Comp.Items)
            {
                var dist = PlanetShape.Angle(_walker.Up, it.Direction) * r - 0.3f * it.Scale;
                if (dist > best) continue;
                var to = it.Direction - _walker.Up * NVector3.Dot(it.Direction, _walker.Up);
                var ahead = to.LengthSquared() > 1e-6f ? NVector3.Dot(NVector3.Normalize(to), _walker.Facing) : 1;
                var score = dist - ahead * 0.8f;
                if (ahead < -0.2f && dist > 1.0f) continue;
                if (score < best)
                {
                    best = score;
                    _focusItem = it;
                }
            }

            if (_focusItem == null && OnOverlook) _focusTelescope = true;
        }

        if (_focusLauncher is { } l && tree.Contains(l.TargetId))
        {
            var name = tree.DisplayName(l.TargetId);
            var what = l.IsParent ? $"Space · fly back up to {name}"
                : l.IsLink ? (l.Followable ? $"Space · follow the link to {name}" : $"{name}: this link can't be followed ({PlaceText.LinkShort(tree, tree[l.TargetId])})")
                : $"Space · fly to {name}";
            SetPrompt(what, l.IsParent ? "the way back" : l.IsLink ? $"a link · {PlaceText.LinkShort(tree, tree[l.TargetId])}" : _s.Summary(l.TargetId));
        }
        else if (_focusItem is { } it2)
        {
            if (it2.Kind == PlanetItemKind.Pile)
            {
                SetPrompt($"E · list all {it2.HiddenCount:N0} more {PlaceText.CategoryName(it2.Category).ToLowerInvariant()} here", "too many to stand here one by one");
            }
            else if (tree.Contains(it2.NodeId))
            {
                var n = tree[it2.NodeId];
                var sel = _s.Selection.Selected == n.Id;
                SetPrompt(sel ? $"Enter · open {n.Name} with Windows   ·   E · close" : $"E · look closer at {n.Name}", $"{PlaceText.CategoryName(FileCategories.Of(n.Name))} · {SizeFormat.Short(n.Size)}");
            }
        }
        else if (_focusTelescope && OnBenchWorld)
        {
            SetPrompt("E · read the README   ·   or spin here: Space, Space", _benchEntries.Count > 0 ? $"the workbench · {_benchEntries.Count} key places in this project" : "the workbench");
        }
        else if (_focusTelescope)
        {
            SetPrompt("E · the telescope: list every world and file here", "the inspection station");
        }
        else
        {
            SetPrompt("");
        }
    }

    private void InspectFocus()
    {
        var tree = _s.Tree;
        if (tree == null || _current == null) return;
        if (_previewPanel.Visible && _focusItem != null && _s.Selection.Selected == _focusItem.NodeId)
        {
            ClosePreview();
            return;
        }

        if (_focusItem is { Kind: PlanetItemKind.Pile } pile)
        {
            OpenContents(pile.Category);
            _audio.Inspect();
            return;
        }

        if (_focusItem is { } it && it.NodeId >= 0)
        {
            InspectNode(it.NodeId);
            return;
        }

        if (_focusTelescope && OnBenchWorld)
        {
            ReadReadme("E");
            return;
        }

        if (_focusTelescope)
        {
            OpenContents(null);
            _audio.Inspect();
            return;
        }

        if (_focusLauncher is { } l)
        {
            _s.Select(l.TargetId);
            _audio.Select();
        }
    }

    /// <summary>Selects a file in the session and shows its preview; the traveller turns to it.</summary>
    private void InspectNode(int id)
    {
        var tree = _s.Tree;
        if (tree == null || !tree.Contains(id)) return;
        _s.Select(id);
        _audio.Inspect();
        var n = tree[id];
        ShowPreview(tree, n);
        if (_current?.Comp.Items.FirstOrDefault(i => i.NodeId == id) is { } item)
        {
            var to = item.Direction - _walker.Up * NVector3.Dot(item.Direction, _walker.Up);
            if (to.LengthSquared() > 1e-6f) _walker.Teleport(_current.Comp.Surface, _walker.Up, NVector3.Normalize(to));
        }
    }

    /// <summary>From the session (Destinations, the list): go and stand by it (a hop), or face its world in the sky.</summary>
    public void Approach(int id)
    {
        var tree = _s.Tree;
        if (tree == null || _current == null || !tree.Contains(id) || Busy) return;
        if (_current.Comp.Items.FirstOrDefault(i => i.NodeId == id) is { } item)
        {
            var (stand, facing) = StandPoint(item.Direction, item.Scale);
            StartHop(stand, facing, id);
            return;
        }

        if (_current.Comp.Launchers.FirstOrDefault(l => l.TargetId == id) is { } launcher)
        {
            var (stand, facing) = StandPoint(launcher.Direction, 0.1f);
            StartHop(launcher.Direction, facing, -1);
            _ = stand;
            return;
        }

        var n = tree[id];
        if (!n.IsDirectory && n.ParentId == _current.FolderId)
        {
            // Not standing here one by one (it is in a pile): hop to its pile and show it.
            var cat = FileCategories.Of(n.Name);
            if (_current.Comp.Items.FirstOrDefault(i => i.Kind == PlanetItemKind.Pile && i.Category == cat) is { } pile)
            {
                var (stand, facing) = StandPoint(pile.Direction, pile.Scale);
                StartHop(stand, facing, id);
                return;
            }

            InspectNode(id);
        }
    }

    private (NVector3 Up, NVector3 Facing) StandPoint(NVector3 target, float size)
    {
        var r = _current!.Comp.Shape.Radius;
        var away = _walker.Up - target * NVector3.Dot(_walker.Up, target);
        if (away.LengthSquared() < 1e-6f) away = PlanetShape.Tangents(target).A;
        away = NVector3.Normalize(away);
        var off = (1.5f + size * 0.6f) / r;
        var stand = NVector3.Normalize(target * MathF.Cos(off) + away * MathF.Sin(off));
        var facing = target - stand * NVector3.Dot(target, stand);
        return (stand, NVector3.Normalize(facing));
    }

    /// <summary>F: turn to face the selection (an object here, or its world in the sky).</summary>
    public void FaceSelection()
    {
        var tree = _s.Tree;
        if (tree == null || _current == null) return;
        var id = _s.Selection.Selected;
        Godot.Vector3? at = null;
        if (_current.Comp.Items.FirstOrDefault(i => i.NodeId == id) is { } it) at = Gd(_current.Comp.Surface.PointAt(it.Direction));
        else if (_children.TryGetValue(id, out var b)) at = b.Node.GlobalPosition;
        else if (_parent?.FolderId == id) at = _parent.Node.GlobalPosition;
        if (at is not { } p) return;
        var up = Gd(_walker.Up);
        var to = p - Gd(_walker.Feet);
        to -= up * to.Dot(up);
        if (to.LengthSquared() > 1e-4f) _camFwd = to.Normalized();
        _sinceOrbitSeconds = 0;
    }

    /// <summary>Esc: close the planet's own overlays first (list, preview); false when there was nothing to close.</summary>
    public bool CloseOverlay()
    {
        if (_contentsPanel.Visible) { _contentsPanel.Visible = false; return true; }
        if (_previewPanel.Visible) { ClosePreview(); return true; }
        if (_benchPanel.Visible) { _benchPanel.Visible = false; return true; }
        return false;
    }

    private void CloseOverlays()
    {
        _contentsPanel.Visible = false;
        _benchPanel.Visible = false;
        ClosePreview();
    }

    private void CloseOverlaysOnMove()
    {
        // The README the bench showed stays open while you are at the bench (a spin is a move too).
        if (_benchPreviewId >= 0 && _s.Selection.Selected == _benchPreviewId && NearBench()) return;
        if (_previewPanel.Visible && _s.Selection.Selected >= 0 && _focusItem?.NodeId != _s.Selection.Selected) ClosePreview();
    }

    /// <summary>The audio files you walk past ring softly (a bell flower each).</summary>
    private void NoteFootstepNear()
    {
        if (_current == null) return;
        var r = _current.Comp.Shape.Radius;
        foreach (var it in _current.Comp.Items)
        {
            if (it.Category != FileCategory.Audio || it.NodeId < 0) continue;
            if (PlanetShape.Angle(_walker.Up, it.Direction) * r < 1.6f)
            {
                _audio.Chime(it.NodeId);
                _current.Visual?.Ring(it.NodeId);
                break;
            }
        }
    }

    // ------------------------------------------------------------------ destinations (the shared drawer) and the planet's list

    /// <summary>The HUD's Destinations drawer, filled from this planet: the way back, then every world in its sky.</summary>
    public void UpdateDestinations()
    {
        var tree = _s.Tree;
        if (tree == null || _current == null || !_active) return;
        var items = new List<(int, string, Color, string, Texture2D?)>();
        if (_current.Comp.Launchers.FirstOrDefault(l => l.IsParent) is { } up)
        {
            items.Add((up.TargetId, $"Back up to {tree.DisplayName(up.TargetId)}", new Color(0.75f, 0.8f, 0.95f), "The way back: the launcher behind the arrival meadow.", FileIcons.Get(IconKind.Up)));
        }

        foreach (var c in tree[_current.FolderId].Children.Where(c => tree[c].IsDirectory || tree[c].Kind == NodeKind.Link).Where(c => _s.IsVisible(tree[c])).OrderBy(c => tree[c].Name, NaturalNameOrder.Instance))
        {
            var n = tree[c];
            var shown = _current.Comp.Launchers.Any(l => l.TargetId == c) ? "a launcher on this planet" : _current.Comp.SkyBodies.Any(k => k.TargetId == c && k.InBelt) ? "a moon in the ring (select it, then G)" : "not in the sky (select it, then G)";
            items.Add((c, $"{n.Name}   ·  {_s.Summary(c)}", PlanetArt.Palette(PlanetComposer.ThemeOf(tree, c)).Accent.Lightened(0.25f), $"{shown}. {tree.FullPath(c)}", FileIcons.Get(FileIcons.For(n))));
        }

        _s.Hud.SetDestinations(items, $"Worlds from {tree.DisplayName(_current.FolderId)} · {items.Count:N0}");
    }

    // ------------------------------------------------------------------ the session's state, for probes

    public string StateJson()
    {
        if (!_active || _current == null) return ",\"planets\":null";
        var f = _walker.Feet;
        var sb = System.FormattableString.Invariant(
            $",\"planets\":{{\"folder\":{_current.FolderId},\"theme\":\"{_current.Comp.Theme}\",\"radius\":{_current.Comp.Shape.Radius:0.##},\"phase\":\"{_phase}\",\"transfers\":{Transfers},\"grounded\":{(_walker.Grounded ? "true" : "false")},\"height\":{_walker.Height.Value:0.###},\"feet\":[{f.X:0.###},{f.Y:0.###},{f.Z:0.###}],\"r\":{f.Length():0.###},\"jumps\":{_walker.Jumps},\"airJumps\":{_walker.AirJumps},\"focus\":{FocusNodeId},\"onOverlook\":{(OnOverlook ? "true" : "false")},\"prompt\":\"{Esc(_prompt.Text)}\",\"items\":{_current.Comp.Items.Count},\"launchers\":{_current.Comp.Launchers.Count},\"children\":{_children.Count},\"parent\":{_parent?.FolderId ?? -1},\"previewOpen\":{(_previewPanel.Visible ? "true" : "false")},\"listOpen\":{(_contentsPanel.Visible ? "true" : "false")},\"maxRollRate\":{_maxRollRateDegreesPerSecond:0.#},\"camYaw\":{CameraHeadingDegrees():0.##},\"orbiting\":{(_orbiting ? "true" : "false")},\"overview\":{(_universe != null ? "true" : "false")},\"uniYaw\":{_uniYaw:0.###},\"guiFocus\":\"{Esc(GetViewport().GuiGetFocusOwner()?.GetType().Name ?? "")}\",\"character\":\"{_character.Kind}\",\"band\":{_character.Band},\"depth\":{_character.Depth},\"benchWorld\":{(OnBenchWorld ? "true" : "false")},\"benchRevealed\":{(_benchRevealed ? "true" : "false")},\"benchEntries\":{_benchEntries.Count},\"benchPanel\":{(_benchPanel.Visible ? "true" : "false")},\"benchBearing\":{BenchBearing().Degrees:0.#},\"benchDistance\":{BenchBearing().Metres:0.##},\"onBench\":{(StandingOnBench() ? "true" : "false")},\"arrivals\":{Arrivals(_current.FolderId)},\"worldOnScreen\":{(ClickableWorld() is { } cw ? $"[{cw.Id},{cw.Centre.X:0},{cw.Centre.Y:0}]" : "null")}}}");
        return sb;
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
}
