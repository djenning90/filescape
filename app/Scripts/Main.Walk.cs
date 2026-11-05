using FileScape.Core.Interaction;
using FileScape.Core.Places;
using FileScape.Core.Units;
using Godot;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>How the user moves in Places (WALK-01..09, AD-16).</summary>
public enum Locomotion
{
    /// <summary>On foot (the default): gravity, steps, slopes, jumping, collision.</summary>
    Walking,

    /// <summary>The explicit flight mode (T): free movement along the view, no collision.</summary>
    Flying,
}

/// <summary>
/// Places locomotion. The Core <see cref="Walker"/> owns walking; this file feeds it input, runs flight (take-off,
/// free flight, landing), pauses both while the camera glides to a requested pose (Home, Destinations, B), and copies
/// the eye pose to the camera every frame. Look capture (right mouse held) changes heading and gaze in both modes.
/// </summary>
public partial class Main
{
    // Places flight (world units are metres here; docs/units.md).

    /// <summary>How high T lifts you before free flight takes over (above the eye on foot).</summary>
    /// <summary>
    /// How high T lifts you above the eye (FLY-04): 16 m (was 3.5 m) — high enough to see the whole plaza and its gates
    /// below, low enough that the place still fills the view. B goes higher (the neighbourhood).
    /// </summary>
    private static readonly Meters TakeOffRise = Meters.Of(16f);

    /// <summary>The gaze the take-off settles on, once: looking down across the place (FLY-04). Never forced again.</summary>
    private static readonly Degrees TakeOffGaze = Degrees.Of(-35);

    /// <summary>How quickly the take-off gaze is reached (≈ 0.75 s to 90 %).</summary>
    private static readonly PerSecond TakeOffGazeRate = PerSecond.Of(3f);

    /// <summary>Climbing and descending (E/Space, Q/C) move at this share of the flight speed.</summary>
    private const float VerticalFlightShare = 0.8f;

    /// <summary>The take-off's target gaze while it is being eased in; null once reached or once the user looks.</summary>
    private Degrees? _takeOffPitch;

    /// <summary>The vertical flight input this frame (−1 down … 1 up), for checks (FLY-03).</summary>
    public float LastFlightVertical { get; private set; }

    /// <summary>
    /// Flight speed grows with height above the ground (FLY-02): ×1 below 15 m, ×(height / 15) above, up to ×30, so the
    /// plaza is crossed gently and the aerial world (places hundreds of metres apart) in seconds. The wheel still sets
    /// the base speed.
    /// </summary>
    public float FlightAltitudeFactor => Mathf.Clamp(CameraAltitude / 15f, 1f, 30f);

    /// <summary>
    /// The take-off rise eases out: its speed is the remaining height × this rate (3.5 m → 9.1 m/s at first), never
    /// below <see cref="TakeOffMinRiseSpeed"/>, so it ends gently but does end. It takes ≈ 1.3 s.
    /// </summary>
    private static readonly PerSecond TakeOffEasing = PerSecond.Of(2.6f);

    private static readonly MetersPerSecond TakeOffMinRiseSpeed = MetersPerSecond.Of(0.8f);

    private static readonly MetersPerSecond FlightDefaultSpeed = MetersPerSecond.Of(10f);

    /// <summary>The eye never goes lower than this above the ground under it while flying.</summary>
    private static readonly Meters FlightMinClearance = Meters.Of(0.6f);

    /// <summary>How quickly flight velocity follows the keys: 14/s, a time constant of ≈ 70 ms (as in map flight).</summary>
    private static readonly PerSecond FlightResponse = PerSecond.Of(14f);

    /// <summary>Landing glides at about this speed, taking between the two durations (continuous, never a cut).</summary>
    private static readonly MetersPerSecond LandingSpeed = MetersPerSecond.Of(9f);

    private static readonly TimeSpan LandingMinDuration = TimeSpan.FromSeconds(0.45), LandingMaxDuration = TimeSpan.FromSeconds(1.8);

    private readonly Walker _walker = new();
    private Locomotion _locomotion = Locomotion.Walking;
    private bool _placeLook;

    /// <summary>The eased return to level after a mouse look (CTRL-04); null when none is running.</summary>
    private LookLeveling? _leveling;
    private bool _jumpQueued;

    /// <summary>The flying eye (world, metres) and its velocity (world, metres per second).</summary>
    private GVector3 _flyEye, _flightVelocity;

    private MetersPerSecond _flightSpeed = FlightDefaultSpeed;

    /// <summary>Height of the take-off rise still to come.</summary>
    private Meters _riseRemaining;
    private LandingGlide? _landing;
    private bool _guided;
    private Locomotion _afterGuide;
    private int _relocations;

    public Locomotion Locomotion => _locomotion;
    public Walker Walker => _walker;
    public bool PlaceLookCaptured => _placeLook;
    public bool Leveling => _leveling != null;
    public bool LandingInProgress => _landing != null;
    public bool GuidedGlide => _guided;
    public int Relocations => _relocations;
    public MetersPerSecond FlightSpeed => _flightSpeed;
    public string? LastLandingNote { get; private set; }

    /// <summary>A continuous descent from the flying eye to a standing eye (world positions).</summary>
    private sealed class LandingGlide
    {
        public required GVector3 From, To;
        public required TimeSpan Duration;
        public TimeSpan Elapsed;
    }

    private PlaceTerrain Terrain => _place?.Terrain ?? PlaceTerrain.Flat;

    // ------------------------------------------------------------------ per frame

    /// <summary>Advances walking or flight and puts the camera at the eye. <paramref name="blocked"/>: no input reaches the scene.</summary>
    private void UpdatePlaceLocomotion(TimeSpan dt, bool blocked)
    {
        if (_place == null || _transition != null) return;
        if (_guided)
        {
            var anyInput = !blocked && (_held.Any || _jumpQueued);
            if (!_rig.IsSettled && !anyInput) return;
            EndGuide();
        }

        if (_leveling != null)
        {
            _walker.SetGaze(_walker.Yaw, _leveling.Step(dt));
            if (_leveling.Done) _leveling = null;
        }

        var fwd = blocked ? 0 : _held.Axis(InputBindings.Back, InputBindings.Forward);
        var strafe = blocked ? 0 : _held.Axis(InputBindings.Left, InputBindings.Right);
        var turn = blocked ? 0 : _held.Axis(InputBindings.TurnLeft, InputBindings.TurnRight);
        var run = !blocked && _held.IsHeld(InputBindings.Fast);
        var jump = !blocked && _jumpQueued;
        _jumpQueued = false;
        if (!blocked && (fwd != 0 || strafe != 0 || turn != 0)) _hud.NoteInteraction(flew: false);

        if (_locomotion == Locomotion.Walking)
        {
            _walker.Update(Terrain, new WalkInput(fwd, strafe, turn, run, jump, JumpReleased: blocked || !_held.IsHeld(InputBindings.Jump)), dt);
            if (_walker.IsLost)
            {
                Relocate("You fell out of this place, so you were brought back to its arrival point.");
                return;
            }

            _rig.SetPose(Gd(_walker.Eye), _walker.Yaw, _walker.LookPitch, PivotAhead);
            return;
        }

        if (_landing is { } l)
        {
            l.Elapsed += dt;
            var k = Mathf.Clamp((float)(l.Elapsed / l.Duration), 0, 1); // progress 0..1, smoothstepped below
            var s = k * k * (3 - 2 * k);
            _rig.SetPose(l.From.Lerp(l.To, s), _walker.Yaw, _walker.LookPitch, PivotAhead);
            if (k >= 1)
            {
                _landing = null;
                _locomotion = Locomotion.Walking;
                _walker.TeleportToEye(Terrain, l.To.ToNumerics(), _walker.Yaw, _walker.LookPitch);
                _rig.SetPose(Gd(_walker.Eye), _walker.Yaw, _walker.LookPitch, PivotAhead);
                // Back on foot: the walking gaze returns to level (FLY-05), whatever was chosen in the air.
                if (Mathf.Abs(_walker.LookPitch.Value) > 0.5f) _leveling = new LookLeveling(_walker.LookPitch);
                UpdateFlightHud();
            }

            return;
        }

        // Free flight (FLY-02/03): W/S and A/D move level along the heading, whatever the gaze, so you can look down and
        // still fly across; E or Space climbs, Q or C descends — the only vertical controls (unless the Settings option
        // "fly where you look" is on). Speed grows with height, so crossing the aerial world takes seconds, not minutes.
        _walker.Look(-(_walker.Params.TurnRate * dt) * turn, Degrees.Zero);
        if (_takeOffPitch is { } aim)
        {
            // The take-off settles the gaze on a downward view once, smoothly; looking with the mouse takes over at once.
            var k = TakeOffGazeRate.SmoothingFactor(dt);
            _walker.SetGaze(_walker.Yaw, _walker.LookPitch + (aim - _walker.LookPitch) * k);
            if (_riseRemaining <= Meters.Zero && Mathf.Abs((aim - _walker.LookPitch).Value) < 0.5f) _takeOffPitch = null;
        }

        var vertical = blocked ? 0 : _held.Axis(InputBindings.Down, InputBindings.Up) + (_held.IsHeld(InputBindings.Jump) ? 1 : 0);
        LastFlightVertical = Mathf.Clamp(vertical, -1, 1);
        var speed = _flightSpeed * FlightAltitudeFactor * (run ? 3f : 1f);
        GVector3 wantedVelocity;
        if (_prefs.FlyAlongGaze)
        {
            wantedVelocity = Gd(CameraMath.FlyVelocity(new NVector3(strafe, LastFlightVertical, fwd), _walker.Yaw, _walker.LookPitch, speed.Value));
        }
        else
        {
            var heading = CameraMath.HorizontalForward(_walker.Yaw);
            var right = CameraMath.Right(_walker.Yaw);
            var level = new GVector3(heading.X, 0, heading.Z) * fwd + new GVector3(right.X, 0, right.Z) * strafe;
            if (level.LengthSquared() > 1) level = level.Normalized();
            wantedVelocity = level * speed.Value + GVector3.Up * (LastFlightVertical * speed.Value * VerticalFlightShare);
        }
        _flightVelocity = _flightVelocity.Lerp(wantedVelocity, FlightResponse.SmoothingFactor(dt));
        _flyEye += _flightVelocity * dt.ToSecondsF();
        if (_riseRemaining > Meters.Zero)
        {
            // Take-off is a continuous rise (WALK-06), fast at first and easing out.
            var riseSpeed = MetersPerSecond.Of(MathF.Max(TakeOffMinRiseSpeed.Value, _riseRemaining.Value * TakeOffEasing.Value));
            var rise = Meters.Min(_riseRemaining, riseSpeed * dt);
            _flyEye.Y += rise.Value;
            _riseRemaining -= rise;
        }

        var ground = Terrain.SupportHeight(new NVector2(_flyEye.X, _flyEye.Z), 0.1f, _flyEye.Y, 0);
        _flyEye.Y = Mathf.Max(_flyEye.Y, ground + FlightMinClearance.Value);
        _rig.SetPose(_flyEye, _walker.Yaw, _walker.LookPitch, PivotAhead);
        _hud.SetFlyStatus(true, (_flightSpeed * FlightAltitudeFactor).Value, highlight: false, places: true, landing: false, altitude: CameraAltitude, alongGaze: _prefs.FlyAlongGaze);
    }

    // ------------------------------------------------------------------ flight (WALK-06/07)

    /// <summary>T / the Flight button: take off when walking, land when flying.</summary>
    public void ToggleFlight()
    {
        if (_experience != Experience.Places || _place == null || _transition != null) return;
        if (_guided) EndGuide();
        if (_locomotion == Locomotion.Walking) TakeOff();
        else Land();
    }

    private void TakeOff()
    {
        TakeOff(TakeOffRise);
        _takeOffPitch = TakeOffGaze; // a useful downward view, once (FLY-04)
    }

    private void TakeOff(Meters rise)
    {
        _takeOffPitch = null;
        _locomotion = Locomotion.Flying;
        _landing = null;
        _flyEye = _rig.CameraPosition;
        _flightVelocity = GVector3.Zero;
        _riseRemaining = rise;
        _hud.NoteInteraction(flew: true);
        UpdateFlightHud();
    }

    /// <summary>
    /// Returns to walking on a valid surface below (WALK-07). Above a spot that is outside the walkable place (a vista,
    /// beyond the boundary) or inside a structure, the nearest clear ground nearby is used, else the arrival point —
    /// and the user is told why.
    /// </summary>
    public void Land()
    {
        if (_locomotion != Locomotion.Flying || _place == null) return;
        _riseRemaining = Meters.Zero;
        _takeOffPitch = null;
        if (_aerialShown && _worldMap != null && _tree != null)
        {
            // From the air (AERIAL-03): over another place, land in it; over open space far from here, land in the
            // nearest place (at its arrival point if there is no ground below). Over this place, land here.
            var (xz, _) = WorldCamera();
            var under = _worldMap.IslandAt(xz, 1.0f);
            if (under < 0)
            {
                var here = _worldMap.Islands[_nav.Current];
                if (System.Numerics.Vector2.Distance(xz, here.Center) > here.Radius + 15f)
                {
                    under = _worldMap.Islands.Values.MinBy(i => System.Numerics.Vector2.Distance(xz, i.Center) - i.Radius)!.Id;
                }
            }

            if (under >= 0 && under != _nav.Current && AerialTravelTo(under, land: true))
            {
                LastLandingNote = $"Landing in {_tree.DisplayName(under)}";
                return;
            }
        }

        var eye = _rig.CameraPosition;
        var p = new NVector2(eye.X, eye.Z);
        var r = _walker.Params.Radius.Value;
        var spot = FindLandingSpot(p, eye.Y);
        if (spot is { } s)
        {
            var target = new GVector3(s.X, Terrain.SupportHeight(s, r, 1000, 0) + _walker.Params.EyeHeight.Value, s.Y);
            var moved = new NVector2(s.X - p.X, s.Y - p.Y).Length();
            LastLandingNote = moved > 0.3f ? $"Landed {moved:0.#} m away: there was no clear ground right below." : null;
            if (LastLandingNote != null) _hud.Toast(LastLandingNote);
            var duration = Meters.Of(eye.DistanceTo(target)) / LandingSpeed;
            _landing = new LandingGlide { From = eye, To = target, Duration = duration < LandingMinDuration ? LandingMinDuration : duration > LandingMaxDuration ? LandingMaxDuration : duration };
            UpdateFlightHud();
            return;
        }

        LastLandingNote = "There is no walkable ground below here (that is a distant view, outside this place), so you are landing at the arrival point.";
        _hud.Toast(LastLandingNote);
        _relocations++;
        _locomotion = Locomotion.Walking;
        StartGuide(_place.Arrival, Locomotion.Walking);
        UpdateFlightHud();
    }

    private NVector2? FindLandingSpot(NVector2 p, float eyeY)
    {
        var t = Terrain;
        var w = _walker.Params;
        var radius = w.Radius.Value;
        var eyeHeight = w.EyeHeight.Value;
        var bodyHeight = w.BodyHeight.Value;
        var stepHeight = w.StepHeight.Value;
        bool Ok(NVector2 q, out float support)
        {
            support = t.SupportHeight(q, radius, 1000, 0);
            if (q.Length() >= t.BoundaryRadius - radius - 0.2f || support + eyeHeight > eyeY + 0.5f || !t.IsClear(q, radius, support, bodyHeight)) return false;
            // Land on ground, terraces, stairs, the overlook — not on the roof of a file tower, the landmark or a pillar.
            foreach (var i in t.BoxesNear(q, radius))
            {
                var b = t.Boxes[i];
                if (b.Kind is TerrainKind.Tower or TerrainKind.Landmark or TerrainKind.Pillar && b.Top > stepHeight && b.Overlaps(q, radius)) return false;
            }

            return true;
        }

        if (Ok(p, out _)) return p;
        if (p.Length() > t.BoundaryRadius + 1) return null;
        // Inside a structure or just outside the edge: the nearest clear spot within a few metres.
        for (var ring = 1; ring <= 12; ring++)
        {
            var searchRadius = ring * 0.5f;
            for (var k = 0; k < 16; k++)
            {
                var a = k * Mathf.Tau / 16;
                var q = p + new NVector2(Mathf.Cos(a), Mathf.Sin(a)) * searchRadius;
                if (Ok(q, out _)) return q;
            }
        }

        return null;
    }

    /// <summary>Back to the arrival point on foot, with a message (lost walkers, failed landings).</summary>
    private void Relocate(string why)
    {
        _relocations++;
        LastLandingNote = why;
        _hud.Toast(why);
        _locomotion = Locomotion.Walking;
        _landing = null;
        ArriveAt(_place!.Arrival, snap: true);
        UpdateFlightHud();
    }

    private void UpdateFlightHud()
    {
        var places = _experience == Experience.Places;
        var flying = places && _locomotion == Locomotion.Flying;
        _hud.SetFlightMode(places, flying, _landing != null);
        if (places) _hud.SetFlyStatus(flying, _flightSpeed.Value, highlight: false, places: true, landing: _landing != null);
    }

    // ------------------------------------------------------------------ guided glides (Home, Destinations, B, F)

    /// <summary>Eases the camera to a pose; locomotion resumes there (on foot or flying) when it settles or on any input.</summary>
    private void StartGuide(PlacePose pose, Locomotion after)
    {
        _leveling = null;
        _guided = true;
        _afterGuide = after;
        _landing = null;
        _rig.GlideToPose(Gd(pose.Eye), pose.Yaw, pose.LookPitch, PivotAhead);
    }

    private void StartGuideFrame(Aabb box, Degrees elevation, Degrees? yaw, float padding, Locomotion after)
    {
        _leveling = null;
        _guided = true;
        _afterGuide = after;
        _landing = null;
        _rig.Frame(box, elevation: elevation, yaw: yaw, padding: padding);
    }

    private void EndGuide()
    {
        _guided = false;
        _locomotion = _afterGuide;
        var eye = _rig.CameraPosition;
        if (_locomotion == Locomotion.Walking)
        {
            _walker.TeleportToEye(Terrain, eye.ToNumerics(), _rig.Yaw, _rig.LookPitch);
            _rig.SetPose(Gd(_walker.Eye), _walker.Yaw, _walker.LookPitch, PivotAhead);
        }
        else
        {
            _walker.SetGaze(_rig.Yaw, _rig.LookPitch);
            _flyEye = eye;
            _flightVelocity = GVector3.Zero;
        }

        UpdateFlightHud();
    }

    /// <summary>Puts the user at a pose instantly in the current locomotion (arrival after travel, first frame).</summary>
    private void PlaceUserAt(PlacePose pose)
    {
        _leveling = null;
        _guided = false;
        _landing = null;
        if (_locomotion == Locomotion.Walking)
        {
            _walker.TeleportToEye(Terrain, pose.Eye, pose.Yaw, pose.LookPitch);
            _rig.SetPose(Gd(_walker.Eye), _walker.Yaw, _walker.LookPitch, PivotAhead);
        }
        else
        {
            _walker.SetGaze(pose.Yaw, pose.LookPitch);
            _flyEye = Gd(pose.Eye);
            _flightVelocity = GVector3.Zero;
            _rig.SetPose(_flyEye, pose.Yaw, pose.LookPitch, PivotAhead);
        }
    }

    /// <summary>F while walking: turn to look at the selection without leaving the ground.</summary>
    private void LookAtInPlace(NVector3 target)
    {
        var eye = _rig.CameraPosition.ToNumerics();
        var d = target - eye;
        var flat = new NVector2(d.X, d.Z).Length();
        if (flat < 0.01f) return;
        var yaw = PlaceGeometry.YawFor(PlaceTerrain.PlaceAngle(new NVector2(d.X, d.Z)));
        var pitch = Degrees.FromRadians(MathF.Atan2(d.Y, flat)).Clamp(Degrees.Of(-60), Degrees.Of(60));
        StartGuide(new PlacePose(eye, CameraRig.NearestAngle(_rig.Yaw, yaw), pitch), Locomotion.Walking);
    }

    // ------------------------------------------------------------------ look capture (hold right mouse)

    private void BeginPlaceLook(Vector2 mousePosition)
    {
        if (ModalOpen || !_windowFocused || _placeLook) return;
        if (_guided) EndGuide();
        _flyStartMouse = mousePosition;
        _placeLook = true;
        _leveling = null;
        _takeOffPitch = null;
        _pressButton = MouseButton.None;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void EndPlaceLook()
    {
        if (!_placeLook) return;
        _placeLook = false;
        LevelAfterLook();
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            if (_flyStartMouse != Vector2.Zero) Input.WarpMouse(_flyStartMouse);
        }
    }

    /// <summary>
    /// A mouse look has ended (right button released, Esc, focus lost, or a left-button drag let go): if the user wants
    /// it (Settings, on by default), ease the pitch back to level over <see cref="LookLeveling.Duration"/> (CTRL-04).
    /// </summary>
    private void LevelAfterLook()
    {
        // On foot only (CTRL-04/FLY-01): in flight the pitch you choose stays, so you can look down from above.
        if (!_prefs.LevelViewOnRelease || _experience != Experience.Places || _place == null || _locomotion != Locomotion.Walking) return;
        if (MathF.Abs(_walker.LookPitch.Value) < 0.01f) return;
        _leveling = new LookLeveling(_walker.LookPitch);
    }

    /// <summary>
    /// Look from a mouse delta in physical screen pixels (x right, y down) at <paramref name="degreesPerPixel"/>, in the
    /// direction the user chose (Settings: reverse left/right, reverse up/down).
    /// </summary>
    private void PlaceLook(Vector2 screenPixels, float degreesPerPixel = CameraRig.LookDegreesPerPixel)
    {
        if (_transition != null) return;
        if (_guided) EndGuide();
        _leveling = null; // looking again takes over from any return to level
        _takeOffPitch = null;
        // The user's direction preference applies here only: arrow-key turning and the camera conventions are unchanged (CTRL-01).
        var (yaw, pitch) = _prefs.LookDelta(screenPixels.ToNumerics2(), degreesPerPixel);
        _walker.Look(yaw, pitch);
    }

    private void StepPlaceFlightSpeed(float notches)
    {
        _flightSpeed = MetersPerSecond.Of(CameraMath.StepFlySpeed(_flightSpeed.Value, notches));
        _hud.SetFlyStatus(true, _flightSpeed.Value, highlight: true, places: true, landing: false);
    }
}

public partial class Main
{
    /// <summary>[direct] Puts the walker at a pose (probes use it to set up a case, never to test a control).</summary>
    public void ProbePlaceUser(PlacePose pose) => PlaceUserAt(pose);

    /// <summary>[direct] Puts a flying user at an eye position (set-up for the landing cases).</summary>
    public void ProbeFlyAt(Godot.Vector3 eye)
    {
        if (_locomotion != Locomotion.Flying) TakeOff(Meters.Zero);
        _flyEye = eye;
        _flightVelocity = GVector3.Zero;
        _rig.SetPose(eye, _walker.Yaw, _walker.LookPitch, PivotAhead);
    }

    /// <summary>[direct] Points the gaze (heading, look pitch) without moving.</summary>
    public void ProbeSetGaze(Degrees yaw, Degrees lookPitch)
    {
        _leveling = null;
        _walker.SetGaze(yaw, lookPitch);
        if (_locomotion == Locomotion.Walking) _rig.SetPose(Gd(_walker.Eye), yaw, lookPitch, PivotAhead);
    }
}
