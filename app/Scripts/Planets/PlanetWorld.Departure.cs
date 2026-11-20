using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// Departures (PLANET-82, AD-74; PLANET-120..122, AD-84): every trip off a world starts at the launcher that leads there.
/// DJ (2026-11-19): "get the character to the appropriate launch pad before taking off … brisk enough that direct
/// navigation stays useful"; (2026-11-20): "Some dialog-based navigation still launches the character from wherever they
/// stand. Find all navigation entry points and route departures through the intended launch-pad behaviour."
/// <para>
/// One routine, <see cref="Depart"/>, for every entry point (AD-84 lists them): a pad press, G and the card's Go inside,
/// a double-click on a world in the sky, the list's Enter, each hop of a trip, U, Backspace, the telescope
/// (<see cref="DepartFor"/>), and every navigator change made elsewhere (the Destinations list's deeper rows and Straight
/// there, favourites and repositories, a link answering, the breadcrumb, Locations, This PC, Go to path, Ctrl+Home, Open
/// folder's new root, the walkthrough's "back to the lesson"). Which launcher is Core's decision
/// (<see cref="DeparturePlanner"/>): the link's the navigator went through, the destination's own, or the way back for
/// the parent; for a place below, the pad of the world on the way down; for a place elsewhere, the way back (up and
/// over). Then:
/// <list type="bullet">
/// <item><b>off the pad:</b> a brisk run-hop onto it (<see cref="Phase.Departing"/>; the walking hop's arc, quicker and
/// lower, on a Hazard World along its route), then the usual wind-up on the pad (it charges; the camera looks up at the
/// world it leads to) and the take-off — a flight, or for a place not in this sky the warp's rise from that pad;</item>
/// <item><b>on the pad already</b> (within <see cref="DeparturePlanner.OnPadShare"/> of its radius): the wind-up at once,
/// as Space on the pad always did — no time added;</item>
/// <item><b>no pad</b> (a belt moon or a place below one): a ring of light kindles at your feet as the wind-up begins
/// (<see cref="KindleEffect"/>) and bursts at the release (<see cref="LaunchEffect"/>) — no time added.</item>
/// </list>
/// The overview and a fall's way back leave from the sky (their own descent; <see cref="DepartureKind.FromSky"/>).
/// </para>
/// <para>
/// Esc (<see cref="DepartureKey"/>, through Main.KeyboardOwner, first, as a trip's) stops a departure before its take-off:
/// a run lands on the pad as an ordinary hop and you stay; a wind-up is cancelled. A change already made elsewhere (a
/// dialog's choice) is taken back (<see cref="PlaceNavigator.Revert"/>), so you stay exactly where you were, history as
/// before; one that cannot be taken back (Open folder's new root clears history) is not stopped.
/// </para>
/// Preserve: a departure is <see cref="Busy"/> from its first frame (nothing else travels or rebuilds the world under
/// it); the wind-up, its beat alignment, the flight and the landing are the existing ones; the navigator moves at the
/// release for an uncommitted departure, exactly as for a pad launch; the run's added time stays within
/// <see cref="DeparturePlanner.RunMaxSeconds"/> (<see cref="DeparturePlanner.RunRouteMaxSeconds"/> along a Hazard route).
/// </summary>
public partial class PlanetWorld
{
    /// <summary>What an uncommitted departure's release asks the session for: into the target (or up, for the parent), or history back.</summary>
    private enum ReleaseAction { Into, Back }

    // ---- the run's arc (PLANET-82; by eye, DJ to judge); its timing and cap are Core's (DeparturePlanner). The arc peaks at
    // 0.12 m per metre, 0.6–3.5 m (the walking hop's 0.25 m per metre, 1.2–9 m, read as a jump, not a dash).
    private const float DepartRunHeightPerMetre = 0.12f, DepartRunMinHeightMetres = 0.6f, DepartRunMaxHeightMetres = 3.5f;

    /// <summary>The kindle's colour: the world's accent lightened by this share (softer than the release's burst; by eye).</summary>
    private const float KindleLighten = 0.35f;

    /// <summary>How the last departure began, for probes: "run" (to its pad), "on-pad", "no-pad".</summary>
    public string LastDepartureKind { get; private set; } = "";

    /// <summary>The last departure run's planned length, seconds (0 when there was none), for probes.</summary>
    public double LastDepartureRunSeconds { get; private set; }

    /// <summary>The last departure's launcher role (<see cref="DeparturePadRole"/>: Own, Link, WayBack, Toward, WayOut, None), for probes.</summary>
    public string LastDepartureRole { get; private set; } = "";

    /// <summary>The last departure's entry point (<see cref="DepartureEntry"/>), for probes.</summary>
    public string LastDepartureEntry { get; private set; } = "";

    /// <summary>The state file's departure fields (real-input probes): the last departure, whether Esc would stop the one in progress, history's length, F's last action.</summary>
    private string DepartureStateJson() => System.FormattableString.Invariant(
        $"\"departure\":{{\"entry\":\"{LastDepartureEntry}\",\"role\":\"{LastDepartureRole}\",\"kind\":\"{LastDepartureKind}\",\"runSeconds\":{LastDepartureRunSeconds:0.###},\"stoppable\":{(DepartureStoppable ? "true" : "false")},\"history\":{_s.Navigator.History.Count}}},\"faceAction\":\"{LastFaceAction}\",");

    /// <summary>
    /// Leaves this world for <paramref name="target"/> from where you stand (see the class summary).
    /// <paramref name="change"/> is the navigator's change when it has already been made, else null (the release makes
    /// it, as <paramref name="release"/> says); <paramref name="entry"/> says which entry point asked.
    /// </summary>
    private void Depart(int target, PlaceChange? change, ReleaseAction release, DepartureEntry entry)
    {
        if (_current == null || _s.Tree is not { } tree) return;
        var plan = DeparturePlanner.Plan(new DepartureQuery(tree, _current.Comp, _walker.Up, target, entry, change?.ViaLink ?? -1));
        var pad = plan.Pad;
        var tr = new Transfer { LaunchTarget = target, Change = change, Released = false, FromPad = pad != null, PadDirection = pad?.Direction ?? _walker.Up, Release = release, Pad = pad };
        _transfer = tr;
        _faceToggle.Forget(); // leaving: F's way back belongs to this world (PlanetWorld.Interact)
        if (change != null) Prepare(change.To); // build it now, while the camera is still, not in the release frame
        CloseOverlays();
        LastDepartureRunSeconds = 0;
        LastDepartureRole = plan.Role.ToString();
        LastDepartureEntry = entry.ToString();
        if (plan.Kind == DepartureKind.RunToPad && pad != null)
        {
            tr.Run = NewHop(pad.Direction, RunFacing(_walker.Up, pad.Direction), -1, 0, DeparturePlanner.RunBaseSeconds, DeparturePlanner.RunMetresPerSecond,
                DeparturePlanner.RunMaxSeconds, DeparturePlanner.RunRouteMaxSeconds, DepartRunHeightPerMetre, DepartRunMinHeightMetres, DepartRunMaxHeightMetres);
            // From wherever the feet are (the overlook's platform at the telescope, mid-jump): the arc starts there, no drop.
            tr.Run.FromHeight = MathF.Max(0, _walker.Height.Value);
            LastDepartureRunSeconds = tr.Run.Seconds;
            LastDepartureKind = "run";
            _phase = Phase.Departing;
            _audio.Jump();
            _avatar.Jump();
            Mark("depart-run");
            return;
        }

        LastDepartureKind = pad != null ? "on-pad" : "no-pad";
        if (pad == null) KindleEffect();
        BeginWindUp(tr);
    }

    /// <summary>
    /// The telescope's (and any later survey's) way to leave for <paramref name="target"/> (stream 5; AD-84): the same
    /// departure as G, from the launcher that leads there; from the overview, the overview's own landing. False when
    /// nothing can leave now (busy, no such folder, a file, already here — that one says so).
    /// </summary>
    public bool DepartFor(int target, DepartureEntry entry)
    {
        if (_universe != null)
        {
            LandFromOverview(target);
            return true;
        }

        if (Busy || _current == null || _s.Tree is not { } tree || !tree.Contains(target)) return false;
        if (target == _current.FolderId) { _s.Toast("You are already here."); return false; }
        if (!tree[target].IsDirectory && tree[target].Kind != NodeKind.Link) return false;
        _hop = null;
        Depart(target, null, ReleaseAction.Into, entry); // the release goes up for the parent (IsParentTarget)
        return true;
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
    /// No pad leads there: as the wind-up begins, a soft ring of the world's accent kindles at your feet — the spot you
    /// will leave from — before the release's burst (<see cref="LaunchEffect"/>).
    /// </summary>
    private void KindleEffect()
    {
        if (_current == null) return;
        _landFx.Burst(Gd(_walker.Feet), Gd(_walker.Up), PlanetArt.Palette(_current.Comp.Theme).Accent.Lightened(KindleLighten));
        Mark("kindle");
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

    /// <summary>
    /// Stops a departure before its take-off: a run lands on the pad as an ordinary hop, a wind-up is cancelled; a change
    /// the navigator already made is taken back (you stay where you were, history as before).
    /// </summary>
    private void CancelDeparture()
    {
        if (_transfer is not { Released: false } tr) return;
        if (_phase == Phase.Departing && tr.Run != null)
        {
            _hop = tr.Run; // it lands where it was going; you stay on this world
            _phase = Phase.Walking;
            _transfer = null;
            Mark("cancel");
        }
        else
        {
            CancelLaunch(null);
        }

        // Back where you stood: told as a Back change, which OnNavChanged recognises as here (no travel).
        if (tr.Change != null) _s.Navigator.Revert(tr.Change);
    }

    /// <summary>Whether Esc can stop the departure in progress (see the class summary).</summary>
    private bool DepartureStoppable =>
        _phase is Phase.Departing or Phase.WindUp && _transfer is { Released: false } tr && (tr.Change == null || _s.Navigator.CanRevert(tr.Change));

    /// <summary>Esc during a departure before its take-off (see the class summary); true when it was used.</summary>
    private bool DepartureKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return false;
        if (!DepartureStoppable) return false;
        CancelDeparture();
        return true;
    }

    /// <summary>
    /// A destination chosen while the overview is open (the list's Enter, a trip, G): the overview's own landing when the
    /// world is drawn, else the navigator goes there and the overview comes down onto it (OnNavChanged); a refusal says why.
    /// </summary>
    private void LandFromOverview(int target)
    {
        if (_uniIndex.ContainsKey(target)) BeginUniverseLanding(target);
        else if (!_s.TravelInto(target)) RefuseLanding(target);
    }

    /// <summary>U on a world: to the parent through its pad (the navigator moves at the release). False when there is no
    /// parent or something is in the way; the shared command then runs (it says when this is the top).</summary>
    public bool LaunchUp()
    {
        if (_universe != null || Busy || _current == null || _s.Tree is not { } tree || !tree.Contains(_current.FolderId) || tree[_current.FolderId].IsRoot) return false;
        var parent = tree[_current.FolderId].ParentId;
        if (!tree.Contains(parent)) return false;
        Depart(parent, null, ReleaseAction.Into, DepartureEntry.Parent); // the release goes up (IsParentTarget)
        return true;
    }

    /// <summary>Backspace on a world: back to the previous place through the launcher that leads there (the navigator
    /// moves at the release). False when there is no history; the shared command then runs.</summary>
    public bool LaunchBack()
    {
        if (_universe != null || Busy || _current == null || _s.Tree is not { } tree) return false;
        var history = _s.Navigator.History;
        if (history.Count == 0) return false;
        var to = history[^1];
        if (!tree.Contains(to) || to == _current.FolderId) return false;
        Depart(to, null, ReleaseAction.Back, DepartureEntry.HistoryBack);
        return true;
    }
}
