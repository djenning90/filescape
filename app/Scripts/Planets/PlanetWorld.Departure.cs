using FileScape.Core.Places;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// Departures (PLANET-82, AD-74): every trip off a world starts at the pad that leads there. DJ (2026-11-19): "When I
/// choose a destination through a command or list, get the character to the appropriate launch pad before taking off …
/// brisk enough that direct navigation stays useful … consistently [for] trips, parent and history navigation".
/// <para>
/// One routine, <see cref="Depart"/>, used by every launch that does not start on its pad: G and the card's Go inside,
/// a double-click on a world in the sky, the Destinations list (a world here, each hop of a trip), U, Backspace, and any
/// navigator change made elsewhere (the breadcrumb, Locations, Go to path, a favourite, Straight there, a link answering).
/// It finds the destination's pad on this world (<see cref="PadFor"/>: the folder's own launcher, the way back for the
/// parent, a link's launcher for a link) and then:
/// <list type="bullet">
/// <item><b>off the pad:</b> a brisk run-hop onto it (<see cref="Phase.Departing"/>; the walking hop's arc, quicker and
/// lower, on a Hazard World along its route), then the usual wind-up on the pad and the take-off;</item>
/// <item><b>on the pad already</b> (within <see cref="OnPadShare"/> of its radius): the wind-up at once, as Space on the
/// pad always did — no time added;</item>
/// <item><b>no pad</b> (a belt moon beyond the launcher budget, a place not in this sky: the history's far end, a
/// favourite, a path, Straight there): the wind-up where you stand and, at the release, a launch burst at your feet
/// (<see cref="LaunchEffect"/>), then the flight or the warp's rise — no time added.</item>
/// </list>
/// </para>
/// <para>
/// Esc (<see cref="DepartureKey"/>, through Main.KeyboardOwner, first, as a trip's): a departure the navigator has not
/// committed to yet (G, the list, a double-click, U, Backspace, a pad, a trip's hop) stops — a run lands on the pad as an
/// ordinary hop and you stay; a wind-up is cancelled. A change already made elsewhere (the breadcrumb, Locations, a path)
/// is not undone by Esc: the navigator is already there, and Backspace after the landing returns.
/// </para>
/// Preserve: a departure is <see cref="Busy"/> from its first frame (nothing else travels or rebuilds the world under
/// it); the wind-up, its beat alignment, the flight and the landing are the existing ones; the navigator moves at the
/// release for an uncommitted departure, exactly as for a pad launch; the run's added time stays within
/// <see cref="DepartRunMaxSeconds"/> (<see cref="DepartRunRouteMaxSeconds"/> along a Hazard route).
/// </summary>
public partial class PlanetWorld
{
    /// <summary>What an uncommitted departure's release asks the session for: into the target (or up, for the parent), or history back.</summary>
    private enum ReleaseAction { Into, Back }

    // ---- the run to the pad (PLANET-82; by eye, DJ to judge). A run takes 0.3 s plus a second per 30 m along the ground,
    // at most 0.8 s (1.1 s along a Hazard World's bridges), so the first ring of pads (≈ 9 m from the meadow) is
    // ≈ 0.6 s away and the far side of a world never more than 0.8 s; its arc peaks at 0.12 m per metre, 0.6–3.5 m (the
    // walking hop's 0.25 m per metre, 1.2–9 m, read as a jump, not a dash).
    private const float DepartRunBaseSeconds = 0.3f, DepartRunMetresPerSecond = 30f, DepartRunMaxSeconds = 0.8f, DepartRunRouteMaxSeconds = 1.1f;
    private const float DepartRunHeightPerMetre = 0.12f, DepartRunMinHeightMetres = 0.6f, DepartRunMaxHeightMetres = 3.5f;

    /// <summary>Standing within this share of a pad's radius from its centre counts as on the pad (the wind-up draws you to the centre).</summary>
    private const float OnPadShare = 0.8f;

    /// <summary>How the last departure began, for probes: "run" (to its pad), "on-pad", "no-pad".</summary>
    public string LastDepartureKind { get; private set; } = "";

    /// <summary>The last departure run's planned length, seconds (0 when there was none), for probes.</summary>
    public double LastDepartureRunSeconds { get; private set; }

    /// <summary>The launcher on this world that leads to <paramref name="target"/> (a link's, for a change through a link), or null.</summary>
    private PlanetLauncher? PadFor(int target, PlaceChange? change) =>
        _current?.Comp.Launchers.FirstOrDefault(l => l.CanLaunch && (l.TargetId == target || (change is { ViaLink: >= 0 } c && l.TargetId == c.ViaLink)));

    /// <summary>
    /// Leaves this world for <paramref name="target"/> from where you stand (see the class summary).
    /// <paramref name="change"/> is the navigator's change when it has already been made, else null (the release makes
    /// it, as <paramref name="release"/> says).
    /// </summary>
    private void Depart(int target, PlaceChange? change, ReleaseAction release)
    {
        if (_current == null) return;
        var pad = PadFor(target, change);
        var tr = new Transfer { LaunchTarget = target, Change = change, Released = false, FromPad = pad != null, PadDirection = pad?.Direction ?? _walker.Up, Release = release };
        _transfer = tr;
        if (change != null) Prepare(change.To); // build it now, while the camera is still, not in the release frame
        CloseOverlays();
        LastDepartureRunSeconds = 0;
        var metres = pad != null ? PlanetShape.Angle(_walker.Up, pad.Direction) * _current.Comp.Shape.Radius : 0;
        if (pad != null && metres > pad.PadRadius * OnPadShare)
        {
            tr.Run = NewHop(pad.Direction, RunFacing(_walker.Up, pad.Direction), -1, 0, DepartRunBaseSeconds, DepartRunMetresPerSecond, DepartRunMaxSeconds,
                DepartRunRouteMaxSeconds, DepartRunHeightPerMetre, DepartRunMinHeightMetres, DepartRunMaxHeightMetres);
            tr.Pad = pad;
            LastDepartureRunSeconds = tr.Run.Seconds;
            LastDepartureKind = "run";
            _phase = Phase.Departing;
            _audio.Jump();
            _avatar.Jump();
            Mark("depart-run");
            return;
        }

        tr.Pad = pad;
        LastDepartureKind = pad != null ? "on-pad" : "no-pad";
        BeginWindUp(tr);
    }

    /// <summary>The facing at the end of a run from <paramref name="from"/> to <paramref name="to"/>: on along the way it came.</summary>
    private NVector3 RunFacing(NVector3 from, NVector3 to)
    {
        var t = to * NVector3.Dot(from, to) - from; // the great circle's tangent at the end, pointing away from the start
        return t.LengthSquared() > 1e-8f ? NVector3.Normalize(t) : _walker.Facing;
    }

    /// <summary>The wind-up (the existing one: beat-aligned, the pad charging when there is one); its end is the release.</summary>
    private void BeginWindUp(Transfer tr)
    {
        tr.T = 0;
        tr.WindUp = _audio.AlignToSixteenth(WindUpSeconds, maxShiftSeconds: WindUpBeatShiftSeconds);
        _phase = Phase.WindUp;
        _audio.WindUp(tr.WindUp);
        if (tr.Pad != null) _current?.Visual?.ChargeLauncher(tr.Pad.TargetId, tr.WindUp);
        Mark("windup");
    }

    /// <summary>One frame of the run to the pad; on landing on it, the wind-up begins.</summary>
    private void UpdateDeparture(Transfer tr, float dt)
    {
        UpdateArrivalFraming(dt, moved: true); // a run is a move
        if (tr.Run == null || !StepHop(tr.Run, dt)) return;
        tr.Run = null;
        Mark("depart-pad");
        BeginWindUp(tr);
    }

    /// <summary>
    /// No pad leads there: the take-off shows where you stand — a burst of the world's accent at your feet at the
    /// release, before the flight or the warp's rise carries you off.
    /// </summary>
    private void LaunchEffect()
    {
        if (_current == null) return;
        _landFx.Burst(Gd(_walker.Feet), Gd(_walker.Up), PlanetArt.Palette(_current.Comp.Theme).Accent);
        Mark("launch-fx");
    }

    /// <summary>Stops a departure the navigator has not committed to: a run lands on the pad as an ordinary hop, a wind-up is cancelled.</summary>
    private void CancelDeparture()
    {
        if (_transfer is not { Released: false } tr) return;
        if (_phase == Phase.Departing && tr.Run != null)
        {
            _hop = tr.Run; // it lands where it was going; you stay on this world
            _phase = Phase.Walking;
            _transfer = null;
            Mark("cancel");
            return;
        }

        CancelLaunch(null);
    }

    /// <summary>Esc during a departure not yet committed (see the class summary); true when it was used.</summary>
    private bool DepartureKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return false;
        if (_phase is not (Phase.Departing or Phase.WindUp) || _transfer is not { Released: false, Change: null }) return false;
        CancelDeparture();
        return true;
    }

    /// <summary>U on a world: to the parent through its pad (the navigator moves at the release). False when there is no
    /// parent or something is in the way; the shared command then runs (it says when this is the top).</summary>
    public bool LaunchUp()
    {
        if (_universe != null || Busy || _current == null || _s.Tree is not { } tree || !tree.Contains(_current.FolderId) || tree[_current.FolderId].IsRoot) return false;
        var parent = tree[_current.FolderId].ParentId;
        if (!tree.Contains(parent)) return false;
        Depart(parent, null, ReleaseAction.Into); // the release goes up (IsParentTarget)
        return true;
    }

    /// <summary>Backspace on a world: back to the previous place through its pad, if it has one here (the navigator moves at
    /// the release). False when there is no history; the shared command then runs.</summary>
    public bool LaunchBack()
    {
        if (_universe != null || Busy || _current == null || _s.Tree is not { } tree) return false;
        var history = _s.Navigator.History;
        if (history.Count == 0) return false;
        var to = history[^1];
        if (!tree.Contains(to) || to == _current.FolderId) return false;
        Depart(to, null, ReleaseAction.Back);
        return true;
    }
}
