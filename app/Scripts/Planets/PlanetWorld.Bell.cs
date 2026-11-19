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
/// just a jump. Nothing else happens (it opens nothing). <b>The telescope</b> (PLANET-55, PLANET-73): walking towards the
/// overlook brings up a sign over the telescope, "E · Telescope", and the prompt says what E does. DJ's 2026-11-19 report
/// ("I tried the telescope … and nothing happened"): the sign said "E" from about 7 m, but E only acted on the platform,
/// which few players reached (the stones wind round it). Now, wherever the sign shows, E acts: off the platform it lifts
/// you up beside the telescope (a hop, as Destinations' approach does), where the overlook names every destination; on
/// the platform E opens this world's list, the telescope flares and a caption says what the list is. The stones still
/// climb, and a jump and twirl still reaches the top. Both only set prompts when nothing nearer is in focus: a file or a
/// pad in reach keeps E (what a change must preserve), and at the bell tower the bell's prompt is the one cue.
/// </summary>
public partial class PlanetWorld
{
    // ---- the telescope's cue (by eye, 2026-11-17)
    /// <summary>The telescope's sign starts to appear this far from the overlook's edge, metres (about the foot of its stones' ring and a few steps more).</summary>
    private const float TelescopeCueFromMetres = 9f;

    /// <summary>It is full this close to the overlook's edge, metres (on the stones, or at their foot).</summary>
    private const float TelescopeCueFullMetres = 2f;

    /// <summary>
    /// Within this distance of the overlook's edge (and not on it) the sign shows, the prompt says "E · up to the
    /// telescope" and E lifts you onto the platform, metres (PLANET-73: the sign and E share one reach, so a sign that
    /// says E always means it). The stones' ring is 1.2 m wide; 6 m is a few steps beyond it.
    /// </summary>
    private const float TelescopeApproachPromptMetres = 6f;

    /// <summary>E off the platform lands you this far from its centre towards the telescope, metres (beside it, facing where it points).</summary>
    private const float TelescopeStandFromCentreMetres = 0.2f;

    /// <summary>The caption shown when E at the telescope opens the list (PLANET-73: what the list is, in one line).</summary>
    private const string TelescopeListCaption = "Through the telescope: every world and file here. Choose one and press Enter to go there.";

    /// <summary>Times E lifted you up to the telescope, and times it opened the list from the platform (probes).</summary>
    public int TelescopeClimbs { get; private set; }

    /// <inheritdoc cref="TelescopeClimbs"/>
    public int TelescopeLists { get; private set; }

    /// <summary>Off the platform, within the telescope's reach, with nothing nearer in focus: E lifts you up (set by <see cref="UpdateLandmarkApproachPrompt"/>).</summary>
    private bool _focusTelescopeFoot;

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

    /// <summary>The telescope's sign: its name and detail line on the platform and off it (probes check the words).</summary>
    public const string TelescopeSignName = "E · Telescope", TelescopeSignDetail = "this world's files and destinations",
        TelescopeSignDetailBelow = "E lifts you up · it lists this world's files and destinations";

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
        if (_current?.Visual is not { TelescopePoint: not null } || TelescopePoint is not { } at || _current.Comp.Overlook is not { } ov) return;
        // PLANET-73: shown exactly where E acts (on the platform, or within the approach reach), never farther out.
        if (!OnOverlook && (OverlookEdgeMetres() is not { } edge || edge > TelescopeApproachPromptMetres)) return;
        if (AtBellTower() || AtLighthouse()) return; // beside the overlook: at the bell or the lighthouse, its prompt is the one cue
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

        _signs.Add(new SignCandidate(TelescopeSignId, anchor, TelescopeSignName, OnOverlook ? TelescopeSignDetail : TelescopeSignDetailBelow,
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
        _focusTelescopeFoot = false;
        if (_current == null) return false;
        // The bell first: the tower stands beside the overlook, so at the tower you are also near the stones.
        if (AtBellTower())
        {
            SetPrompt(UnderBell ? "Space, Space · jump and twirl to ring the bell" : "Step under the bell, then jump and twirl (Space, Space) to ring it", "the bell tower");
            return true;
        }

        if (UpdateLighthousePrompt()) return true; // PLANET-74 (PlanetWorld.Whimsy.cs): the lighthouse stands beside the overlook too

        if (OverlookEdgeMetres() is { } edge && edge <= TelescopeApproachPromptMetres && !OnOverlook)
        {
            _focusTelescopeFoot = true;
            SetPrompt(TelescopeFootPrompt, "the overlook · from the top every destination is named, and E there lists them all");
            return true;
        }

        return false;
    }

    /// <summary>The prompt within the telescope's reach, off the platform (probes check it).</summary>
    public const string TelescopeFootPrompt = "E · up to the telescope   ·   or climb the stones";

    /// <summary>
    /// E within the telescope's reach, off the platform (PLANET-73): a hop up onto the platform beside the telescope,
    /// facing the way it points; the overlook view then rises as it does when you climb (<see cref="UpdateWalking"/>).
    /// </summary>
    private void ClimbToTelescope()
    {
        if (_current?.Comp.Overlook is not { } ov) return;
        var north = PlanetFrame.North(ov.Direction); // the overlook mesh's −Z (where the telescope stands and points) is its north (PlanetVisual.BuildOverlook)
        var stand = PlanetFrame.Move(ov.Direction, north, TelescopeStandFromCentreMetres, _current.Comp.Shape.Radius);
        StartHop(stand, PlanetFrame.North(stand), -1, toHeight: ov.Top);
        TelescopeClimbs++;
        GD.Print($"[planets] E: up to the telescope ({TelescopeClimbs})");
    }

    /// <summary>E at the telescope on the platform: this world's list, the telescope's flare and a caption saying what the list is.</summary>
    private void LookThroughTelescope()
    {
        OpenContents(null);
        _audio.Inspect();
        _current?.Visual?.FlareTelescope();
        ShowHint(TelescopeListCaption);
        TelescopeLists++;
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
        // The overlook's stepping stones in climbing order, each [bearing, metres to its centre] (the real-input probe climbs them).
        var stones = c?.Comp.Overlook is { } ov2 && !OnBenchWorld
            ? string.Join(",", ov2.Steps.Select(st => System.FormattableString.Invariant($"[{CameraTurnTo(st.Direction):0.#},{PlanetShape.Angle(_walker.Up, st.Direction) * c.Comp.Shape.Radius:0.##}]")))
            : "";
        return System.FormattableString.Invariant($"\"telescopeFoot\":{(_focusTelescopeFoot ? "true" : "false")},\"telescopeClimbs\":{TelescopeClimbs},\"telescopeLists\":{TelescopeLists},{WhimsyStateJson()}\"stones\":[{stones}],\"bellRings\":{BellRings},\"underBell\":{(UnderBell ? "true" : "false")},\"bellSwing\":{BellSwing:0.###},\"bellBearing\":{bb:0.#},\"bellDistance\":{bd:0.##},\"telescopeCue\":{cue:0.##},\"telescopeSign\":{(visible ? "true" : "false")},\"overlookBearing\":{tb:0.#},\"overlookEdge\":{td:0.##},\"uniZoom\":{zoom:0.#},\"uniWorlds\":{UniverseWorldsInView},");
    }

    /// <summary>Where the bell tower's axis is and a point <paramref name="metres"/> out from it towards the arrival (probes walk in from there), or null without a bell.</summary>
    public (NVector3 Axis, NVector3 Outside)? BellTowerApproach(float metres) =>
        _current?.Comp.Landmark is { Bell: true } lm ? (lm.Direction, PlanetFrame.Move(lm.Direction, PlanetFrame.North(lm.Direction), metres, _current.Comp.Shape.Radius)) : null;
}
