using FileScape.App.Places;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Planets;
using FileScape.Core.Units;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// Travel between planets (PLANET-12, AD-39): a launch has a visible, continuous beginning (a 0.4 s wind-up whose
/// release lands on the music's sixteenth-note grid), a brisk arc (1.5–2.5 s) into the target's placement, and a
/// landing that re-expresses the whole scene in the target's frame, so nothing on screen jumps. The navigator is told
/// at the release (so the folder is listed while you fly); a change that comes from elsewhere (Backspace, the
/// breadcrumb, Destinations, a path) starts the same launch from where you stand. A destination that is not a
/// neighbour in the sky is reached by a warp: a rise into a streak of light and a descent onto the new world.
/// <para>
/// The phases (<see cref="Phase"/>): <b>WindUp</b> (crouch, camera turns to the target; at its end the release asks the
/// session to travel) → <b>AwaitNav</b> only if the session has not answered yet (a link resolving: hover on the pad) →
/// <b>Flying</b> (a quadratic Bézier arc into the target's placement, ending in <c>Land</c>) or <b>Warping</b> (rise,
/// build the new world at the brightest moment, descend) → <b>Walking</b>. One transfer at a time
/// (<see cref="Busy"/>); a navigator change that arrives mid-flight is picked up at the landing and launches on.
/// </para>
/// </summary>
public partial class PlanetWorld
{
    /// <summary>Where the planetary world is in a place change (Walking when none is in progress; hops are separate).</summary>
    private enum Phase { Walking, Departing, WindUp, AwaitNav, Flying, Warping, Falling }

    /// <summary>The wind-up before release (research E2: a visible, physical beginning).</summary>
    public const float WindUpSeconds = 0.4f;

    /// <summary>The wind-up may stretch or shrink by up to this, seconds, so the release lands on the music's sixteenth
    /// grid (AD-42: ≥ half a sixteenth at every tempo, so it always can).</summary>
    private const float WindUpBeatShiftSeconds = 0.09f;

    /// <summary>On a pad, the wind-up draws the traveller to the pad's centre at this rate, per second (≈ 0.07 s: quick, but not a snap).</summary>
    private const float PadCaptureRatePerSecond = 14f;

    /// <summary>The wind-up turns the camera towards the destination at this rate, per second (≈ 0.11 s).</summary>
    private const float WindUpTurnRatePerSecond = 9f;

    /// <summary>
    /// Waiting for the session after a release: if nothing is pending after this long, seconds, the session refused or
    /// already answered (and said why), so the launch is cancelled quietly.
    /// </summary>
    private const float AwaitAnswerGraceSeconds = 0.25f;

    /// <summary>A link that has not answered in this long, seconds, cancels the launch with a message (the link may still open later).</summary>
    private const float AwaitLinkTimeoutSeconds = 8f;

    // ---- the flight arc (PLANET-12: a brisk arc of 1.5–2.5 s; PLANET-03: ≤ 2.7 s)
    /// <summary>
    /// The arc's control point is raised this fraction of the straight-line distance away from both worlds (the Bézier's
    /// peak is half that). The remembered-route beads trace the same curve (<see cref="UpdateRouteBeads"/>), so the two
    /// must use the same value.
    /// </summary>
    private const float ArcLiftFraction = 0.38f;

    /// <summary>A flight takes 1 s plus a second per 75 m of straight-line distance, within 1.5–2.5 s (PLANET-12).</summary>
    private const float FlightBaseSeconds = 1.0f, FlightMetresPerExtraSecond = 75f, MinFlightSeconds = 1.5f, MaxFlightSeconds = 2.5f;

    /// <summary>The landing's impact, as a downward speed in m/s: it sets the landing sound (a flight lands firmly, a warp
    /// a little harder; a hop softly at 4).</summary>
    private const float FlightLandingImpact = 8f, WarpLandingImpact = 9f;

    private Phase _phase = Phase.Walking;

    /// <summary>One place change in progress, from the wind-up to the landing. Seconds are float seconds; positions are in
    /// the current planet's frame.</summary>
    private sealed class Transfer
    {
        /// <summary>The folder the wind-up is for (what the release will ask the session for).</summary>
        public int LaunchTarget = -1;

        /// <summary>The folder actually flown to (the navigator's answer, set at the start of the flight).</summary>
        public int TargetId = -1;

        /// <summary>The navigator's change, once it has answered (null while winding up for a launch from a pad).</summary>
        public PlaceChange? Change;

        /// <summary>The body flown into (null for a warp).</summary>
        public Body? Target;
        public bool FromPad;
        public NVector3 PadDirection;

        /// <summary>Seconds into the current phase.</summary>
        public float T;
        public float WindUp = WindUpSeconds;
        public float Flight = 2f;

        /// <summary>The arc: start, Bézier control point and end; up at each end.</summary>
        public Godot.Vector3 From, Control, To, FromUp, ToUp;

        /// <summary>The target's drawn scale: the traveller ends the flight at it.</summary>
        public float EndScale = 1f;
        public NVector3 LandUp, LandFacing;

        /// <summary>The folder to arrive beside on landing (−1: the arrival meadow); see <see cref="ArrivalFrom"/>.</summary>
        public int ArriveFrom = -1;

        /// <summary>The wind-up has finished and the session has been asked.</summary>
        public bool Released;

        /// <summary>Warp: the new world has been built (the descent has begun).</summary>
        public bool Built;

        /// <summary>Seconds spent waiting for the session (AwaitNav).</summary>
        public float Await;

        /// <summary>The fastest the traveller has moved in this flight, world units per second (probes).</summary>
        public float MaxSpeed;

        /// <summary>The departure's run to its pad (PlanetWorld.Departure), while <see cref="Phase.Departing"/>.</summary>
        public Hop? Run;

        /// <summary>The pad this departure leaves from (null: none leads there; PlanetWorld.Departure).</summary>
        public PlanetLauncher? Pad;

        /// <summary>What the release asks the session for when no change has been made yet (the navigator moves then).</summary>
        public ReleaseAction Release;
    }

    private Transfer? _transfer;

    /// <summary>Launch log for probes: (phase, app seconds).</summary>
    public readonly List<(string Phase, double AtSeconds)> LaunchLog = [];

    // ---- measurements for probes (the PLANET-12 journey checks): flights and warps started, the last flight's and
    // wind-up's length (seconds), the landing's camera jump (always 0 here: the probe measures it across the frame), the angle between the flight's up and the
    // landing's up (degrees, E5: < 2°), and the largest camera step in one flight frame (in units of the traveller's scale).
    public int Transfers { get; private set; }
    public double LastFlightSeconds { get; private set; }
    public double LastWindUpSeconds { get; private set; }
    public float LastLandingCameraJump { get; private set; }
    public float LastLandingUpError { get; private set; }
    public float MaxFrameCameraStepInFlight { get; private set; }

    /// <summary>Logs a phase change for probes (the names are read by probes: keep them as they are).</summary>
    private void Mark(string phase) => LaunchLog.Add((phase, _s.Now.TotalSeconds));

    /// <summary>Space on a launcher pad: wind up towards its world, then release.</summary>
    private void BeginLaunch(int targetId, NVector3 padDirection, bool fromPad)
    {
        // One transfer at a time; during a trip only the trip launches (its hop is marked InHop just before).
        if (_phase != Phase.Walking || _trip is { InHop: false } || _current == null || _s.Tree == null) return;
        if (_universe != null)
        {
            LandFromOverview(targetId); // PLANET-83: the overview lands; a launch on the surface would never run under it
            return;
        }

        _hop = null;
        if (!fromPad)
        {
            Depart(targetId, change: null, ReleaseAction.Into); // PLANET-82: to its pad first (PlanetWorld.Departure)
            return;
        }

        _transfer = new Transfer { LaunchTarget = targetId, FromPad = fromPad, PadDirection = padDirection };
        _transfer.WindUp = _audio.AlignToSixteenth(WindUpSeconds, maxShiftSeconds: WindUpBeatShiftSeconds);
        _phase = Phase.WindUp;
        _audio.WindUp(_transfer.WindUp);
        _current.Visual?.ChargeLauncher(targetId, _transfer.WindUp);
        CloseOverlays();
        Mark("windup");
    }

    /// <summary>
    /// The navigator changed: the launch we wound up for, or a change made elsewhere (then launch from here). From the
    /// overview (or <c>_arriveFromSky</c>), the change is a warp that starts at the top of its rise: the light is already
    /// up, so the new world is built at once and you come down onto it. During a flight or warp it is ignored here and
    /// picked up at the landing.
    /// </summary>
    public void OnNavChanged(PlaceChange change)
    {
        if (!_active) return;
        if (_phase == Phase.Falling) OnNavChangedWhileFalling(); // a fall's way back (PlanetWorld.Hazard): out of the light, as below
        if (_universe != null)
        {
            // A navigation change from the overview (Backspace, the list, a path): come down onto it out of the light.
            CloseUniverse(returnToSurface: false);
            _arriveFromSky = true;
            _warpFlash = 1f;
        }

        if (_arriveFromSky)
        {
            _arriveFromSky = false;
            _hop = null;
            CloseOverlays();
            // T = the whole rise: the warp builds the new world on its first frame and descends (no rise from the overview).
            _transfer = new Transfer { LaunchTarget = change.To, Change = change, Released = true, From = Gd(_walker.Feet), FromUp = Gd(_walker.Up), T = WarpRiseSeconds };
            _transfer.ArriveFrom = _s.Tree is { } t ? ArrivalFrom(t, change, _current?.FolderId ?? -1) : -1;
            _phase = Phase.Warping;
            _flightCamStarted = false;
            Transfers++;
            Mark("warp");
            return;
        }

        if (_phase is Phase.Departing or Phase.WindUp or Phase.AwaitNav && _transfer != null)
        {
            _transfer.Change = change;
            if (_transfer.Released) StartFlight();
            return;
        }

        if (_phase is Phase.Flying or Phase.Warping)
        {
            return; // the arrival compares the navigator with where it lands and moves on if they differ
        }

        // The breadcrumb, Destinations, Locations, Go to path, a link answering: the same departure as any other (its pad
        // first, PLANET-82), the navigator already there.
        _hop = null;
        Depart(change.To, change, ReleaseAction.Into);
    }

    /// <summary>One frame of a place change (any phase but Walking); the overlook view fades out meanwhile.</summary>
    private void UpdateTravel(float dt, bool blockedKeys)
    {
        if (_phase == Phase.Falling)
        {
            UpdateFall(dt); // off a broken world's edge (PlanetWorld.Hazard): no transfer until the way back is asked for
            return;
        }

        var tr = _transfer;
        if (tr == null || _s.Tree is not { } tree)
        {
            _phase = Phase.Walking;
            return;
        }

        tr.T += dt;
        _overlook01 = Mathf.MoveToward(_overlook01, 0, dt * OverlookBlendPerSecond);
        switch (_phase)
        {
            case Phase.Departing:
                UpdateDeparture(tr, dt); // the run to the pad (PlanetWorld.Departure)
                break;
            case Phase.WindUp:
            {
                var k = Mathf.Clamp(tr.T / tr.WindUp, 0, 1);
                // Drawn to the pad's centre, crouching; the camera turns to show where you are going.
                if (tr.FromPad && _current != null)
                {
                    var target = NVector3.Normalize(NVector3.Lerp(_walker.Up, tr.PadDirection, 1 - MathF.Exp(-PadCaptureRatePerSecond * dt)));
                    _walker.Teleport(_current.Comp.Surface, target, _walker.Facing);
                }

                PoseAvatar(dt, windUp: k);
                TurnCameraTowards(TargetCentreForCamera(tr), dt);
                UpdateCamera(dt, lookAt: TargetCentreForCamera(tr), extraBack: WindUpPullBackMetres * k, extraUp: WindUpRiseMetres * k);
                if (k >= 1 && !tr.Released)
                {
                    tr.Released = true;
                    LastWindUpSeconds = tr.T;
                    Mark("release");
                    if (tr.FromPad == false) LaunchEffect(); // no pad: the take-off shows where you stand (PlanetWorld.Departure)
                    if (tr.Change != null)
                    {
                        StartFlight();
                    }
                    else
                    {
                        var ok = tr.Release == ReleaseAction.Back ? _s.GoBack() : IsParentTarget(tree, tr.LaunchTarget) ? _s.TravelUp() : _s.TravelInto(tr.LaunchTarget);
                        if (_phase == Phase.WindUp)
                        {
                            if (!ok) CancelLaunch("That world can't be reached from here.");
                            else { _phase = Phase.AwaitNav; tr.Await = 0; } // a link resolving: hover on the pad
                        }
                    }
                }

                break;
            }
            case Phase.AwaitNav:
                tr.Await += dt;
                PoseAvatar(dt, windUp: 1, hover: true);
                UpdateCamera(dt, lookAt: TargetCentreForCamera(tr));
                if (tr.Await > AwaitAnswerGraceSeconds && _s.PendingRequests == 0) CancelLaunch(null); // the session refused or already answered (it said why)
                else if (tr.Await > AwaitLinkTimeoutSeconds) CancelLaunch("The link has not answered yet; you are still here (it may still open later).");
                break;
            case Phase.Flying:
                UpdateFlight(tree, tr, dt);
                break;
            case Phase.Warping:
                UpdateWarp(tree, tr, dt);
                break;
        }
    }

    /// <summary>
    /// The wind-up turns the camera to look where you are going (the destination ahead, you in front of the lens), so the
    /// launch carries you away from the camera instead of past it.
    /// </summary>
    private void TurnCameraTowards(Godot.Vector3? target, float dt)
    {
        if (target is not { } t) return;
        var up = Gd(_walker.Up);
        var to = t - Gd(_walker.Feet);
        to -= up * to.Dot(up);
        if (to.LengthSquared() < 1e-4f) return;
        to = to.Normalized();
        var ang = _camFwd.SignedAngleTo(to, up);
        _camFwd = _camFwd.Rotated(up, ang * (1 - MathF.Exp(-WindUpTurnRatePerSecond * dt))).Normalized();
        _sinceOrbitSeconds = 0;
    }

    /// <summary>
    /// Where to arrive, as Places does (PLACE-07, LINK-03): coming back up (or back to an ancestor), beside the launcher
    /// of the world on the way to where you were, with that world selected; back through a link, beside the link's
    /// launcher with the link selected; otherwise on the arrival meadow (the selection is kept).
    /// </summary>
    private int ArrivalFrom(FsTree tree, PlaceChange change, int fromId)
    {
        if (fromId < 0 || !tree.Contains(fromId)) return -1;
        if (change.Move == PlaceMove.Back && change.ViaLink >= 0 && tree.Contains(change.ViaLink) && tree[change.ViaLink].ParentId == change.To)
        {
            _s.Select(change.ViaLink);
            return change.ViaLink;
        }

        if (change.Move != PlaceMove.Link && (tree[fromId].ParentId == change.To || (change.Move != PlaceMove.Root && IsAncestor(tree, change.To, fromId))))
        {
            var child = PlaceNavigator.ChildToward(tree, change.To, fromId);
            if (child >= 0) _s.Select(child);
            return child;
        }

        return -1;
    }

    /// <summary>True when <paramref name="ancestor"/> is a proper ancestor of <paramref name="id"/>.</summary>
    private static bool IsAncestor(FsTree tree, int ancestor, int id)
    {
        for (var p = tree[id].ParentId; p >= 0; p = tree[p].ParentId)
        {
            if (p == ancestor) return true;
        }

        return false;
    }

    /// <summary>The launch goes up to the current folder's parent (the release then asks the session to go up, not into).</summary>
    private bool IsParentTarget(FsTree tree, int id) => _current != null && tree.Contains(_current.FolderId) && tree[_current.FolderId].ParentId == id;

    /// <summary>Back to walking where you stand, with the refusal sound and, if given, a message saying why.</summary>
    private void CancelLaunch(string? why)
    {
        _audio.Deny();
        if (why != null) _s.Toast(why);
        _phase = Phase.Walking;
        _transfer = null;
        Mark("cancel");
    }

    /// <summary>Where the destination hangs in the sky (for the wind-up's camera), or null when it is not in view (a warp).</summary>
    private Godot.Vector3? TargetCentreForCamera(Transfer tr)
    {
        var id = tr.Change?.To ?? tr.LaunchTarget;
        if (_children.TryGetValue(id, out var c)) return c.Node.GlobalPosition;
        if (_parent != null && _parent.FolderId == id) return _parent.Node.GlobalPosition;
        return null;
    }

    /// <summary>
    /// Release: fly into a neighbour in the sky, or warp to a world that is not in view. The flight's end is the target's
    /// landing point as it hangs now (its placement transform applied), so the arc ends exactly where the world is drawn.
    /// </summary>
    private void StartFlight()
    {
        var tr = _transfer!;
        var tree = _s.Tree!;
        var change = tr.Change!;
        tr.TargetId = change.To;
        tr.T = 0;
        Body? body = null;
        if (_children.TryGetValue(change.To, out var child)) body = child;
        else if (_parent != null && _parent.FolderId == change.To) body = _parent;
        var fromId = _current?.FolderId ?? -1;
        tr.ArriveFrom = ArrivalFrom(tree, change, fromId);
        _audio.Launch();
        _avatar.Launch();
        Transfers++;
        if (body == null || _current == null)
        {
            _flightCamStarted = false;
            _phase = Phase.Warping;
            tr.From = Gd(_walker.Feet);
            tr.FromUp = Gd(_walker.Up);
            Mark("warp");
            return;
        }

        // The target in full, where it already hangs (same composition, so the same placement).
        if (!body.Full)
        {
            BuildFull(tree, body);
        }

        tr.Target = body;
        var (landUp, landFacing) = tr.ArriveFrom >= 0 ? body.Comp.ArrivalFor(tr.ArriveFrom) : body.Comp.Arrival;
        tr.LandUp = landUp;
        tr.LandFacing = landFacing;
        var place = body.Node.Transform;
        tr.To = place * Gd(body.Comp.Surface.PointAt(landUp));
        tr.ToUp = (place.Basis * Gd(landUp)).Normalized();
        tr.EndScale = place.Basis.Scale.X;
        tr.From = Gd(_walker.Feet);
        tr.FromUp = Gd(_walker.Up);
        var dist = tr.From.DistanceTo(tr.To);
        var lift = (tr.FromUp + tr.ToUp).Normalized();
        if (!lift.IsFinite() || lift.LengthSquared() < 0.1f) lift = tr.FromUp; // the two ups (nearly) opposite: lift along the departure's up
        // Raised away from both worlds; parametrised linearly, the arc is fastest leaving and arriving, slowest at the top.
        tr.Control = (tr.From + tr.To) / 2 + lift * dist * ArcLiftFraction;
        tr.Flight = Mathf.Clamp(FlightBaseSeconds + dist / FlightMetresPerExtraSecond, MinFlightSeconds, MaxFlightSeconds) * (_trip != null ? TripFlightScale : 1f);
        tr.MaxSpeed = 0;
        MaxFrameCameraStepInFlight = 0;
        _phase = Phase.Flying;
        _flightCamStarted = false;
        _flightTrail.Begin(tr.From);
        Mark("flight");
    }

    /// <summary>The camera's position last flight frame (for the probe's largest per-frame step).</summary>
    private Godot.Vector3 _lastFlightCam;

    /// <summary>
    /// One flight frame: the point on the quadratic Bézier at u = t / flight (two nested lerps; its velocity is
    /// 2(b − a) / flight), the traveller's scale and up, the trail, the sound and the camera; touchdown at u = 1.
    /// </summary>
    private void UpdateFlight(FsTree tree, Transfer tr, float dt)
    {
        var u = Mathf.Clamp(tr.T / tr.Flight, 0, 1);
        var a = tr.From.Lerp(tr.Control, u);
        var b = tr.Control.Lerp(tr.To, u);
        var pos = a.Lerp(b, u);
        var vel = (b - a) * 2 / tr.Flight;
        tr.MaxSpeed = MathF.Max(tr.MaxSpeed, vel.Length());
        // The traveller shrinks (or grows) into the target's scale on the way, so it lands at walking size there. The scale
        // is interpolated in log space (geometric), so a 1/10 shrink looks as steady as a 10× growth.
        var s = MathF.Exp(Mathf.Lerp(0, MathF.Log(tr.EndScale), Smooth(u)));
        // Up turns from where you left to where you land over the last 65 % of the flight (from u = 0.35; feet first for the
        // landing; research E5 asks for at least the last 0.3 s).
        var up = tr.FromUp.Slerp(tr.ToUp, Smooth(Mathf.Clamp((u - 0.35f) / 0.65f, 0, 1)));
        var fwd = vel.LengthSquared() > 1e-6f ? vel.Normalized() : tr.ToUp;
        // PLANET-81 (AD-74): feet first. Over the flight's last LandingUprightSeconds the traveller turns out of the
        // streamlined pose into standing, and its facing turns to the way it will stand (where the camera looks, as Land
        // sets it), so it touches down on its feet and nothing is left to snap upright in the landing frame (before: the
        // full flight pose at touchdown, then up to 31° a frame back to standing).
        var upright = LandingUpright(tr);
        var faceFwd = fwd;
        if (upright > 0)
        {
            var look = -_camera.GlobalBasis.Z;
            look -= up * look.Dot(up);
            var along = fwd - up * fwd.Dot(up);
            if (look.LengthSquared() > 1e-4f && along.LengthSquared() > 1e-4f) faceFwd = along.Normalized().Slerp(look.Normalized(), upright);
        }

        _avatar.SetFlying(pos, up, faceFwd, u, s, dt, pose01: 1 - upright);
        _flightTrail.Add(pos, s);
        _audio.Fly(u);
        FlightCamera(pos, up, fwd, s, u, tr, dt);
        var step = _camera.GlobalPosition.DistanceTo(_lastFlightCam);
        // Measured relative to the traveller's scale (floored at 0.05), and not in the first 2 % (the take-off frame's step
        // from the walking camera is not a flight step).
        if (u > 0.02f) MaxFrameCameraStepInFlight = MathF.Max(MaxFrameCameraStepInFlight, step / MathF.Max(s, 0.05f));
        _lastFlightCam = _camera.GlobalPosition;
        if (u >= 1) Land(tree, tr);
    }

    /// <summary>
    /// A flight turns feet first over its last this many seconds (PLANET-81): long enough to read as a deliberate turn
    /// (at 60 Hz about 27 frames, ≈ 2.5° a frame for the 68° streamlined lean), short enough that most of the flight keeps
    /// its streamlined pose; at most <see cref="LandingUprightMaxShare"/> of a short flight. By eye; DJ to judge.
    /// </summary>
    private const float LandingUprightSeconds = 0.45f, LandingUprightMaxShare = 0.4f;

    /// <summary>How far the flight's turn to standing has gone, 0 (streamlined) to 1 (standing, at touchdown): a smoothstep
    /// of the flight's remaining time, so it is a function of time, never of the frame length.</summary>
    private static float LandingUpright(Transfer tr)
    {
        var window = MathF.Min(LandingUprightSeconds, LandingUprightMaxShare * tr.Flight);
        return Smooth(Mathf.Clamp(1 - (tr.Flight - tr.T) / window, 0, 1));
    }

    /// <summary>Smoothstep: 0 → 1 with zero slope at both ends (an ease in and out).</summary>
    private static float Smooth(float x) => x * x * (3 - 2 * x);

    /// <summary>
    /// Touchdown: re-express everything in the target's frame (no visible jump), make it the current planet.
    /// <para>
    /// Multiplying by the inverse of the target's placement turns the target's frame into the world frame (AD-41): the
    /// camera, its smoothing state and the planet you left are all moved by it, so on screen nothing changes. Anything
    /// new that holds a position across a landing must be moved here too, or it will jump. The walker then stands where
    /// the flight ended, facing where the camera looks, and the camera blends into its walking pose (E5: ≤ 0.6 s).
    /// </para>
    /// </summary>
    private void Land(FsTree tree, Transfer tr)
    {
        var body = tr.Target!;
        var inv = body.Node.Transform.AffineInverse();
        var camBefore = _camera.GlobalTransform;
        var old = _current;
        // The camera and its smoothing state, in the new frame (the scale is undone too).
        _camPos = inv * _camPos;
        _camLook = inv * _camLook;
        _camUp = (inv.Basis * _camUp).Normalized();
        _camFwd = (inv.Basis * _camFwd).Normalized();
        var camAfter = inv * camBefore;
        camAfter.Basis = camAfter.Basis.Orthonormalized();
        _camera.GlobalTransform = camAfter;
        if (old != null) old.Node.Transform = inv * old.Node.Transform;
        LastFlightSeconds = tr.T;
        if (old != null) RememberRoute(old.FolderId, body.FolderId);
        BuildCurrent(tree, body.FolderId, tr.ArriveFrom, reuse: body, keep: old);
        if (old != null && old != _parent && !_children.ContainsValue(old)) FreeBody(old);
        // Face where the camera looks (projected on the ground), so the view rolls onto the new surface instead of swinging.
        var up = tr.LandUp;
        var look = Nv(-_camera.GlobalBasis.Z);
        var facing = look - up * NVector3.Dot(look, up);
        facing = facing.LengthSquared() > 0.01f ? NVector3.Normalize(facing) : tr.LandFacing;
        _walker.Teleport(body.Comp.Surface, up, facing);
        _avatar.Scale = Godot.Vector3.One;
        _avatar.Land(impact: FlightLandingImpact);
        var flightUp = (inv.Basis * tr.ToUp).Normalized();
        PoseAvatar(0); // the traveller stands where it touched down in this same frame (no stale pose: no cut)
        LastLandingUpError = Mathf.RadToDeg(Gd(up).AngleTo(flightUp));
        _overlook01 = 0;
        _flightTrail.End();
        _audio.Land(FlightLandingImpact);
        _audio.Arrive();
        _landFx.Burst(Gd(_walker.Feet), Gd(up), PlanetArt.Palette(body.Comp.Theme).Accent);
        _phase = Phase.Walking;
        _transfer = null;
        _camBlend = _camBlendTotal = LandingBlendSeconds;
        _camBlendFrom = _camera.GlobalTransform;
        BeginArrivalFraming(); // PLANET-42: land into the higher arrival view (PlanetWorld.Arrival)
        LastLandingCameraJump = 0; // measured by the probe across the frame (camera position before vs after)
        Mark("land");
        ShowTitleCard(tree, body.FolderId, null);
        if (_s.Navigator.Current != body.FolderId && tree.Contains(_s.Navigator.Current))
        {
            // The navigator moved on while we flew (a link answering late, say): launch on from here, never a cut.
            OnNavChanged(new PlaceChange(body.FolderId, _s.Navigator.Current, PlaceMove.Jump));
        }
    }

    // ------------------------------------------------------------------ warp (a destination that is not in the sky)

    /// <summary>A warp's rise into the light and its descent onto the new world, seconds.</summary>
    private const float WarpRiseSeconds = 0.55f, WarpDescentSeconds = 1.0f;

    /// <summary>The rise climbs this high, metres (accelerating: k² of it); the descent starts this high above the landing point.</summary>
    private const float WarpRiseMetres = 60f, WarpDescentStartMetres = 70f;

    /// <summary>
    /// One warp frame. The rise and the descent each drive half of the flight progress (0–0.5, then 0.5–1) for the sound,
    /// the pose and the camera, and the flash follows them: up to full on the rise, down to none on the descent. The
    /// new world is built at the top, behind the brightest flash, so the swap is never seen.
    /// </summary>
    private void UpdateWarp(FsTree tree, Transfer tr, float dt)
    {
        if (!tr.Built)
        {
            // Rise into the streak: up and away fast, the screen brightening.
            var k = Mathf.Clamp(tr.T / WarpRiseSeconds, 0, 1);
            var pos = tr.From + tr.FromUp * (k * k * WarpRiseMetres);
            // Straight up, standing and facing as it stood (PLANET-81: the streamlined pose leaned 68° towards an arbitrary
            // side here, the travel's direction being straight up).
            _avatar.SetFlying(pos, tr.FromUp, Gd(_walker.Facing), k * 0.5f, 1f, dt, pose01: 0f);
            _flightTrail.Add(pos, 1f);
            _audio.Fly(k * 0.5f);
            _warpFlash = k;
            FlightCamera(pos, tr.FromUp, tr.FromUp, 1f, k * 0.5f, tr, dt, warp: true);
            if (k >= 1)
            {
                // At the brightest moment the new world is built at the origin; you come down onto it from above.
                var target = tr.Change?.To ?? tr.LaunchTarget;
                BuildCurrent(tree, target, tr.ArriveFrom);
                var pose = tr.ArriveFrom >= 0 ? _current!.Comp.ArrivalFor(tr.ArriveFrom) : _current!.Comp.Arrival;
                tr.LandUp = pose.Up;
                tr.LandFacing = pose.Facing;
                tr.To = Gd(_current.Comp.Surface.PointAt(tr.LandUp));
                tr.ToUp = Gd(tr.LandUp);
                tr.From = tr.To + tr.ToUp * WarpDescentStartMetres;
                tr.Built = true;
                tr.T = 0;
                _flightTrail.End();
                _flightTrail.Begin(tr.From);
                // The camera is placed afresh in the new world's frame (the old frame is gone): 8 m above and 10 m ahead of
                // you. The descent's shot looks down, nearly along the local up, so its up is a horizontal (−facing)
                // instead; the local up would leave LookAt without a roll. Distances by eye.
                _camPos = tr.From + tr.ToUp * 8f + Gd(tr.LandFacing) * 10f;
                _camUp = Gd(tr.LandFacing) * -1f;
                Mark("warp-built");
            }

            return;
        }

        var d = Mathf.Clamp(tr.T / WarpDescentSeconds, 0, 1);
        var e = 1 - (1 - d) * (1 - d) * (1 - d); // fast in, settling: a decisive landing
        var p = tr.From.Lerp(tr.To, e);
        _warpFlash = 1 - d;
        // Feet first all the way down, already facing the way it will stand (PLANET-81: before, the streamlined pose faced an
        // arbitrary side and turned 90° in the touchdown frame).
        _avatar.SetFlying(p, tr.ToUp, Gd(tr.LandFacing), 0.5f + d * 0.5f, 1f, dt, pose01: 0f);
        _flightTrail.Add(p, 1f);
        _audio.Fly(0.5f + d * 0.5f);
        FlightCamera(p, tr.ToUp, -tr.ToUp, 1f, 0.5f + 0.5f * d, tr, dt, warp: true);
        if (d >= 1)
        {
            LastFlightSeconds = WarpRiseSeconds + tr.T;
            _walker.Teleport(_current!.Comp.Surface, tr.LandUp, tr.LandFacing);
            _avatar.Land(impact: WarpLandingImpact);
            PoseAvatar(0);
            _overlook01 = 0;
            _flightTrail.End();
            _audio.Land(WarpLandingImpact);
            _audio.Arrive();
            _landFx.Burst(Gd(_walker.Feet), Gd(tr.LandUp), PlanetArt.Palette(_current.Comp.Theme).Accent);
            _phase = Phase.Walking;
            _transfer = null;
            _warpFlash = 0;
            _camBlend = _camBlendTotal = LandingBlendSeconds;
            _camBlendFrom = _camera.GlobalTransform;
            BeginArrivalFraming();
            Mark("land");
            ShowTitleCard(tree, _current.FolderId, null);
        }
    }

    // ------------------------------------------------------------------ commands from the session

    /// <summary>G: launch to the selected folder from where you stand (or to the folder of a selected file).</summary>
    public void LaunchToSelection()
    {
        if (_universe != null)
        {
            BeginUniverseLanding(_s.Selection.Selected); // G in the overview lands on the selected world
            return;
        }

        var tree = _s.Tree;
        if (tree == null || _current == null || Busy) return;
        var id = _s.Selection.Selected;
        if (id < 0 || !tree.Contains(id)) { _s.Toast("Select a world first: click a planet in the sky, or pick one in the list (J)."); return; }
        var n = tree[id];
        if (id == _current.FolderId) { _s.Toast("You are already here."); return; }
        if (!n.IsDirectory && n.Kind != NodeKind.Link) { _s.Toast("Files are not worlds: E looks closer, Enter opens it with Windows."); return; }
        var launcher = _current.Comp.Launchers.FirstOrDefault(l => l.TargetId == id);
        BeginLaunch(id, launcher?.Direction ?? _walker.Up, fromPad: false);
    }

    /// <summary>Home: back to the arrival meadow by a short hop.</summary>
    public void ReturnToArrival()
    {
        if (_current == null || Busy) return;
        StartHop(_current.Comp.Arrival.Up, _current.Comp.Arrival.Facing, -1);
    }
}
