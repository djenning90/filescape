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
/// </summary>
public partial class PlanetWorld
{
    private enum Phase { Walking, WindUp, AwaitNav, Flying, Warping }

    /// <summary>The wind-up before release (research E2: a visible, physical beginning).</summary>
    public const float WindUpSeconds = 0.4f;

    private Phase _phase = Phase.Walking;

    private sealed class Transfer
    {
        public int LaunchTarget = -1;
        public int TargetId = -1;
        public PlaceChange? Change;
        public Body? Target;
        public bool FromPad;
        public NVector3 PadDirection;
        public float T;
        public float WindUp = WindUpSeconds;
        public float Flight = 2f;
        public Godot.Vector3 From, Control, To, FromUp, ToUp;
        public float EndScale = 1f;
        public NVector3 LandUp, LandFacing;
        public int ArriveFrom = -1;
        public bool Released;
        public bool Built;
        public float Await;
        public float MaxSpeed;
    }

    private Transfer? _transfer;

    /// <summary>Launch log for probes: (phase, app seconds).</summary>
    public readonly List<(string Phase, double AtSeconds)> LaunchLog = [];

    public int Transfers { get; private set; }
    public double LastFlightSeconds { get; private set; }
    public double LastWindUpSeconds { get; private set; }
    public float LastLandingCameraJump { get; private set; }
    public float LastLandingUpError { get; private set; }
    public float MaxFrameCameraStepInFlight { get; private set; }

    private void Mark(string phase) => LaunchLog.Add((phase, _s.Now.TotalSeconds));

    /// <summary>Space on a launcher pad: wind up towards its world, then release.</summary>
    private void BeginLaunch(int targetId, NVector3 padDirection, bool fromPad)
    {
        if (Busy || _current == null || _s.Tree == null) return;
        _hop = null;
        _transfer = new Transfer { LaunchTarget = targetId, FromPad = fromPad, PadDirection = padDirection };
        _transfer.WindUp = _audio.AlignToSixteenth(WindUpSeconds, maxShiftSeconds: 0.09f);
        _phase = Phase.WindUp;
        _audio.WindUp(_transfer.WindUp);
        _current.Visual?.ChargeLauncher(targetId, _transfer.WindUp);
        CloseOverlays();
        Mark("windup");
    }

    /// <summary>The navigator changed: the launch we wound up for, or a change made elsewhere (then launch from here).</summary>
    public void OnNavChanged(PlaceChange change)
    {
        if (!_active) return;
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
            _transfer = new Transfer { LaunchTarget = change.To, Change = change, Released = true, From = Gd(_walker.Feet), FromUp = Gd(_walker.Up), T = WarpRiseSeconds };
            _transfer.ArriveFrom = _s.Tree is { } t ? ArrivalFrom(t, change, _current?.FolderId ?? -1) : -1;
            _phase = Phase.Warping;
            _flightCamStarted = false;
            Transfers++;
            Mark("warp");
            return;
        }

        if (_phase is Phase.WindUp or Phase.AwaitNav && _transfer != null)
        {
            _transfer.Change = change;
            if (_transfer.Released) StartFlight();
            return;
        }

        if (_phase is Phase.Flying or Phase.Warping)
        {
            return; // the arrival compares the navigator with where it lands and moves on if they differ
        }

        // Backspace, U, the breadcrumb, Destinations, Go to path: the same launch, from where you stand.
        _hop = null;
        _transfer = new Transfer { LaunchTarget = change.To, Change = change, Released = false };
        Prepare(change.To); // build it now, at the start of the wind-up (the camera is still), not in the release frame
        _transfer.WindUp = _audio.AlignToSixteenth(WindUpSeconds, maxShiftSeconds: 0.09f);
        _phase = Phase.WindUp;
        _audio.WindUp(_transfer.WindUp);
        CloseOverlays();
        Mark("windup");
    }

    private void UpdateTravel(float dt, bool blockedKeys)
    {
        var tr = _transfer;
        if (tr == null || _s.Tree is not { } tree)
        {
            _phase = Phase.Walking;
            return;
        }

        tr.T += dt;
        _overlook01 = Mathf.MoveToward(_overlook01, 0, dt * 1.6f);
        switch (_phase)
        {
            case Phase.WindUp:
            {
                var k = Mathf.Clamp(tr.T / tr.WindUp, 0, 1);
                // Drawn to the pad's centre, crouching; the camera turns to show where you are going.
                if (tr.FromPad && _current != null)
                {
                    var target = NVector3.Normalize(NVector3.Lerp(_walker.Up, tr.PadDirection, 1 - MathF.Exp(-14 * dt)));
                    _walker.Teleport(_current.Comp.Surface, target, _walker.Facing);
                }

                PoseAvatar(dt, windUp: k);
                TurnCameraTowards(TargetCentreForCamera(tr), dt);
                UpdateCamera(dt, lookAt: TargetCentreForCamera(tr), extraBack: 1.5f * k, extraUp: 0.6f * k);
                if (k >= 1 && !tr.Released)
                {
                    tr.Released = true;
                    LastWindUpSeconds = tr.T;
                    Mark("release");
                    if (tr.Change != null)
                    {
                        StartFlight();
                    }
                    else
                    {
                        var ok = IsParentTarget(tree, tr.LaunchTarget) ? _s.TravelUp() : _s.TravelInto(tr.LaunchTarget);
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
                if (tr.Await > 0.25f && _s.PendingRequests == 0) CancelLaunch(null); // the session refused or already answered (it said why)
                else if (tr.Await > 8f) CancelLaunch("The link has not answered yet; you are still here (it may still open later).");
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
        _camFwd = _camFwd.Rotated(up, ang * (1 - MathF.Exp(-9f * dt))).Normalized();
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

    private static bool IsAncestor(FsTree tree, int ancestor, int id)
    {
        for (var p = tree[id].ParentId; p >= 0; p = tree[p].ParentId)
        {
            if (p == ancestor) return true;
        }

        return false;
    }

    private bool IsParentTarget(FsTree tree, int id) => _current != null && tree.Contains(_current.FolderId) && tree[_current.FolderId].ParentId == id;

    private void CancelLaunch(string? why)
    {
        _audio.Deny();
        if (why != null) _s.Toast(why);
        _phase = Phase.Walking;
        _transfer = null;
        Mark("cancel");
    }

    private Godot.Vector3? TargetCentreForCamera(Transfer tr)
    {
        var id = tr.Change?.To ?? tr.LaunchTarget;
        if (_children.TryGetValue(id, out var c)) return c.Node.GlobalPosition;
        if (_parent != null && _parent.FolderId == id) return _parent.Node.GlobalPosition;
        return null;
    }

    /// <summary>Release: fly into a neighbour in the sky, or warp to a world that is not in view.</summary>
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
        if (!lift.IsFinite() || lift.LengthSquared() < 0.1f) lift = tr.FromUp;
        // Raised away from both worlds; parametrised linearly, the arc is fastest leaving and arriving, slowest at the top.
        tr.Control = (tr.From + tr.To) / 2 + lift * dist * 0.38f;
        tr.Flight = Mathf.Clamp(1.0f + dist / 75f, 1.5f, 2.5f);
        tr.MaxSpeed = 0;
        MaxFrameCameraStepInFlight = 0;
        _phase = Phase.Flying;
        _flightCamStarted = false;
        _flightTrail.Begin(tr.From);
        Mark("flight");
    }

    private Godot.Vector3 _lastFlightCam;

    private void UpdateFlight(FsTree tree, Transfer tr, float dt)
    {
        var u = Mathf.Clamp(tr.T / tr.Flight, 0, 1);
        var a = tr.From.Lerp(tr.Control, u);
        var b = tr.Control.Lerp(tr.To, u);
        var pos = a.Lerp(b, u);
        var vel = (b - a) * 2 / tr.Flight;
        tr.MaxSpeed = MathF.Max(tr.MaxSpeed, vel.Length());
        // The traveller shrinks (or grows) into the target's scale on the way, so it lands at walking size there.
        var s = MathF.Exp(Mathf.Lerp(0, MathF.Log(tr.EndScale), Smooth(u)));
        // Up turns from where you left to where you land over the second half (feet first for the landing).
        var up = tr.FromUp.Slerp(tr.ToUp, Smooth(Mathf.Clamp((u - 0.35f) / 0.65f, 0, 1)));
        var fwd = vel.LengthSquared() > 1e-6f ? vel.Normalized() : tr.ToUp;
        _avatar.SetFlying(pos, up, fwd, u, s);
        _flightTrail.Add(pos, s);
        _audio.Fly(u);
        FlightCamera(pos, up, fwd, s, u, tr, dt);
        var step = _camera.GlobalPosition.DistanceTo(_lastFlightCam);
        if (u > 0.02f) MaxFrameCameraStepInFlight = MathF.Max(MaxFrameCameraStepInFlight, step / MathF.Max(s, 0.05f));
        _lastFlightCam = _camera.GlobalPosition;
        if (u >= 1) Land(tree, tr);
    }

    private static float Smooth(float x) => x * x * (3 - 2 * x);

    /// <summary>Touchdown: re-express everything in the target's frame (no visible jump), make it the current planet.</summary>
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
        _avatar.Land(impact: 8f);
        var flightUp = (inv.Basis * tr.ToUp).Normalized();
        PoseAvatar(0); // the traveller stands where it touched down in this same frame (no stale pose: no cut)
        LastLandingUpError = Mathf.RadToDeg(Gd(up).AngleTo(flightUp));
        _overlook01 = 0;
        _flightTrail.End();
        _audio.Land(8f);
        _audio.Arrive();
        _landFx.Burst(Gd(_walker.Feet), Gd(up), PlanetArt.Palette(body.Comp.Theme).Accent);
        _phase = Phase.Walking;
        _transfer = null;
        _camBlend = _camBlendTotal = 0.6f;
        _camBlendFrom = _camera.GlobalTransform;
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

    private const float WarpRiseSeconds = 0.55f, WarpDescentSeconds = 1.0f;

    private void UpdateWarp(FsTree tree, Transfer tr, float dt)
    {
        if (!tr.Built)
        {
            // Rise into the streak: up and away fast, the screen brightening.
            var k = Mathf.Clamp(tr.T / WarpRiseSeconds, 0, 1);
            var pos = tr.From + tr.FromUp * (k * k * 60f);
            _avatar.SetFlying(pos, tr.FromUp, tr.FromUp, k * 0.5f);
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
                tr.From = tr.To + tr.ToUp * 70f;
                tr.Built = true;
                tr.T = 0;
                _flightTrail.End();
                _flightTrail.Begin(tr.From);
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
        _avatar.SetFlying(p, tr.ToUp, -tr.ToUp, 0.5f + d * 0.5f);
        _flightTrail.Add(p, 1f);
        _audio.Fly(0.5f + d * 0.5f);
        FlightCamera(p, tr.ToUp, -tr.ToUp, 1f, 0.5f + 0.5f * d, tr, dt, warp: true);
        if (d >= 1)
        {
            LastFlightSeconds = WarpRiseSeconds + tr.T;
            _walker.Teleport(_current!.Comp.Surface, tr.LandUp, tr.LandFacing);
            _avatar.Land(impact: 9f);
            PoseAvatar(0);
            _overlook01 = 0;
            _flightTrail.End();
            _audio.Land(9f);
            _audio.Arrive();
            _landFx.Burst(Gd(_walker.Feet), Gd(tr.LandUp), PlanetArt.Palette(_current.Comp.Theme).Accent);
            _phase = Phase.Walking;
            _transfer = null;
            _warpFlash = 0;
            _camBlend = _camBlendTotal = 0.6f;
            _camBlendFrom = _camera.GlobalTransform;
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
