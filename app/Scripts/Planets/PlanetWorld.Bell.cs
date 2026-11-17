using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// Two landmark touches from DJ's 2026-11-17 playtest. <b>The bell tower</b> (PLANET-56): its base and the space under the
/// bell are walkable (<see cref="BellTower"/>), and a jump and twirl under the bell rings it — the bell swings
/// (<see cref="PlanetVisual.RingBell"/>) and tolls in the world's key on the music's grid (<see cref="SkyAudio.BellToll"/>).
/// Only the twirl (the air jump) rings it, and only under the bell: an ordinary jump there, or a twirl anywhere else, is
/// just a jump. Nothing else happens (it opens nothing). <b>The telescope</b> (PLANET-55): walking towards the overlook
/// brings up a sign over the telescope, "E · Telescope · this world's files and destinations", and the prompt says to go
/// up to it; on the platform E opens the list, as before. Both only set prompts when nothing nearer is in focus.
/// </summary>
public partial class PlanetWorld
{
    // ---- the telescope's cue (by eye, 2026-11-17)
    /// <summary>The telescope's sign starts to appear this far from the overlook's edge, metres (about the foot of its stones' ring and a few steps more).</summary>
    private const float TelescopeCueFromMetres = 9f;

    /// <summary>It is full this close to the overlook's edge, metres (on the stones, or at their foot).</summary>
    private const float TelescopeCueFullMetres = 2f;

    /// <summary>Within this distance of the overlook's edge (and not on it) the prompt points up to the telescope, metres.</summary>
    private const float TelescopeApproachPromptMetres = 6f;

    // ---- the bell
    /// <summary>Standing within this far of the tower's axis, metres (on its base or a step off it), the prompt says how to ring the bell.</summary>
    private const float BellPromptMetres = BellTower.BaseRadiusMetres + 0.8f;

    /// <summary>Times the bell has been rung in this run (probes).</summary>
    public int BellRings { get; private set; }

    /// <summary>The bell's swing now, radians (probes; 0 without a bell tower).</summary>
    public float BellSwing => _current?.Visual?.BellSwing ?? 0;

    /// <summary>Standing under the bell now (probes).</summary>
    public bool UnderBell => _current is { } c && BellTower.UnderBell(c.Comp.Landmark, _walker.Up, c.Comp.Shape.Radius);

    /// <summary>The telescope cue's current strength and whether its sign is offered to the sign layer now (probes; the layer's ShownIds say whether it fitted).</summary>
    public (float Cue, bool Offered) TelescopeCue => _current?.Visual is { } v ? (v.TelescopeCue, _telescopeSignOffered) : (0, false);

    /// <summary>The telescope's position (world), or null (probes project it to check the sign is in view).</summary>
    public Vector3? TelescopePoint => _current?.Visual is { TelescopePoint: { } p } v ? v.ToGlobal(p) : null;

    /// <summary>The telescope's sign id in the sign layer (a fixed negative id apart from pads, worlds, cards and the bench marker).</summary>
    public const int TelescopeSignId = -400_000;

    /// <summary>The telescope's sign: its name and detail line (probes check the words).</summary>
    public const string TelescopeSignName = "E · Telescope", TelescopeSignDetail = "this world's files and destinations";

    /// <summary>The sign is offered once the cue is at least this strong (about 7 m from the overlook's edge on the way in).</summary>
    private const float TelescopeSignFromCue = 0.25f;

    /// <summary>It floats this high over the telescope, metres (over a standing traveller's head on the platform).</summary>
    private const float TelescopeSignAboveMetres = 0.9f;

    /// <summary>Walking up, it is pinned to the platform's wall at this fraction of the platform's height.</summary>
    private const float TelescopeSignWallHeight = 0.55f;

    /// <summary>Its priority among signs: above a pad's name and the bench marker (it is what you are walking to), below a pointed-at destination's focus.</summary>
    private const float TelescopeSignPriority = 50f;

    private bool _telescopeSignOffered;

    /// <summary>From <c>UpdateSigns</c>: the telescope's pinned sign while you approach or stand on the overlook.</summary>
    private void AddTelescopeSign()
    {
        _telescopeSignOffered = false;
        if (_current?.Visual is not { TelescopePoint: not null } v || v.TelescopeCue < TelescopeSignFromCue || TelescopePoint is not { } at || _current.Comp.Overlook is not { } ov) return;
        if (AtBellTower()) return; // the tower stands beside the overlook: at the bell, the bell's prompt is the one cue
        _telescopeSignOffered = true;
        // On the platform it floats over the telescope. Walking up, the chase camera puts the platform's top near the top of
        // the view, with no room above it for a pinned sign (the HUD is there: the sign was dropped, 2026-11-17), so the
        // sign is pinned to the platform's wall on your side instead, halfway up, and its stem points at the way up.
        var anchor = at + Gd(_walker.Up) * TelescopeSignAboveMetres;
        if (!OnOverlook)
        {
            var toYou = _walker.Up - ov.Direction * NVector3.Dot(_walker.Up, ov.Direction);
            var wall = toYou.LengthSquared() > 1e-10f ? PlanetFrame.Move(ov.Direction, NVector3.Normalize(toYou), ov.Radius, _current.Comp.Shape.Radius) : ov.Direction;
            anchor = Gd(_current.Comp.Surface.PointAt(wall, ov.Top * TelescopeSignWallHeight));
        }

        _signs.Add(new SignCandidate(TelescopeSignId, anchor, TelescopeSignName, TelescopeSignDetail,
            new Color(0.7f, 0.92f, 1f) /* the telescope's light */, IconKind.Folder, true, TelescopeSignPriority, FixedAnchor: true));
    }

    /// <summary>
    /// On an air jump (the twirl): under the bell, ring it and return true (the twirl's other uses then do not act).
    /// The ring is the twirl's only effect here; it opens nothing.
    /// </summary>
    private bool RingBellIfUnder()
    {
        if (_current == null || !UnderBell) return false;
        BellRings++;
        _audio.BellToll();
        _current.Visual?.RingBell(_camera.GlobalPosition);
        GD.Print($"[planets] the bell rings ({BellRings})");
        return true;
    }

    /// <summary>The overlook's edge distance from where you stand, metres (negative on the platform), or null without one or on a workbench.</summary>
    private float? OverlookEdgeMetres()
    {
        if (_current?.Comp.Overlook is not { } ov || OnBenchWorld) return null;
        return PlanetShape.Angle(_walker.Up, ov.Direction) * _current.Comp.Shape.Radius - ov.Radius;
    }

    /// <summary>Every focus update: the telescope's sign follows how near the overlook you are (full on it).</summary>
    private void UpdateTelescopeCue()
    {
        if (_current?.Visual is not { } visual) return;
        var edge = OverlookEdgeMetres();
        visual.TelescopeCueTarget = edge is not { } e ? 0 : OnOverlook ? 1 : Mathf.Clamp((TelescopeCueFromMetres - e) / (TelescopeCueFromMetres - TelescopeCueFullMetres), 0, 1);
    }

    /// <summary>
    /// From <see cref="UpdateFocus"/> when nothing nearer is in focus: walking up to the overlook, the prompt points up to
    /// the telescope (true); under or beside the bell tower, it says how to ring the bell (true).
    /// </summary>
    private bool UpdateLandmarkApproachPrompt()
    {
        if (_current == null) return false;
        // The bell first: the tower stands beside the overlook, so at the tower you are also near the stones.
        if (AtBellTower())
        {
            SetPrompt(UnderBell ? "Space, Space · jump and twirl to ring the bell" : "Step under the bell, then jump and twirl (Space, Space) to ring it", "the bell tower");
            return true;
        }

        if (OverlookEdgeMetres() is { } edge && edge <= TelescopeApproachPromptMetres && !OnOverlook)
        {
            SetPrompt("Up the steps to the telescope: E there lists this world's files and destinations", "the overlook");
            return true;
        }

        return false;
    }

    /// <summary>On or beside the bell tower's base (within <see cref="BellPromptMetres"/> of its axis).</summary>
    private bool AtBellTower() =>
        _current?.Comp.Landmark is { Bell: true } lm && PlanetShape.Angle(_walker.Up, lm.Direction) * _current.Comp.Shape.Radius <= BellPromptMetres;

    /// <summary>The probe state's bell and telescope fields (a JSON fragment ending in a comma).</summary>
    private string LandmarkStateJson()
    {
        var (cue, visible) = TelescopeCue;
        // Where to steer (the real-input probe, os-planets-probe.ps1 -Landmarks): the bearing from the camera's heading
        // (degrees, + right) and the distance (metres) to the overlook's edge and to the bell tower's axis; −1 without one.
        var c = _current;
        var (tb, td) = c?.Comp.Overlook is { } ov && !OnBenchWorld ? (CameraTurnTo(ov.Direction), OverlookEdgeMetres() ?? -1) : (0f, -1f);
        var (bb, bd) = c?.Comp.Landmark is { Bell: true } lm ? (CameraTurnTo(lm.Direction), PlanetShape.Angle(_walker.Up, lm.Direction) * c.Comp.Shape.Radius) : (0f, -1f);
        var (zoom, _) = UniverseZoom;
        return System.FormattableString.Invariant($"\"bellRings\":{BellRings},\"underBell\":{(UnderBell ? "true" : "false")},\"bellSwing\":{BellSwing:0.###},\"bellBearing\":{bb:0.#},\"bellDistance\":{bd:0.##},\"telescopeCue\":{cue:0.##},\"telescopeSign\":{(visible ? "true" : "false")},\"overlookBearing\":{tb:0.#},\"overlookEdge\":{td:0.##},\"uniZoom\":{zoom:0.#},\"uniWorlds\":{UniverseWorldsInView},");
    }

    /// <summary>Where the bell tower's axis is and a point <paramref name="metres"/> out from it towards the arrival (probes walk in from there), or null without a bell.</summary>
    public (NVector3 Axis, NVector3 Outside)? BellTowerApproach(float metres) =>
        _current?.Comp.Landmark is { Bell: true } lm ? (lm.Direction, PlanetFrame.Move(lm.Direction, PlanetFrame.North(lm.Direction), metres, _current.Comp.Shape.Radius)) : null;
}
