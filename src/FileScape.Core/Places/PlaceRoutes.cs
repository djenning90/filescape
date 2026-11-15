using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Units;

namespace FileScape.Core.Places;

/// <summary>The result of walking a route with a simulated <see cref="Walker"/> (positions in place space, metres).</summary>
/// <param name="Crossed">The target gate was crossed outward, facing through it (the same test as <see cref="ThresholdTracker"/>).</param>
/// <param name="Time">Walking time until the crossing (or until giving up).</param>
/// <param name="MaxDeviation">The largest sideways distance from the intended straight line (straight routes only), metres.</param>
/// <param name="Distance">Ground distance walked, metres.</param>
public readonly record struct RouteWalk(bool Crossed, TimeSpan Time, float MaxDeviation, float Distance);

/// <summary>Where to stand after going up, and whether walking straight ahead from there verifiably reaches the next way up.</summary>
/// <param name="Pose">The arrival pose: eye, heading along the route, slight downward gaze.</param>
/// <param name="Verified">Walking straight forward from <paramref name="Pose"/> was simulated and crossed the ▲ gate.</param>
/// <param name="StandOff">How far the pose is from the gate of the child just left (metres; 0 when there is none).</param>
/// <param name="Walk">The simulated walk (default when there is no way up).</param>
public readonly record struct UpwardRoute(PlacePose Pose, bool Verified, float StandOff, RouteWalk Walk);

/// <summary>
/// Routes through a place, checked with the real walker (PLACE-16, WALK-10). Going up arrives facing the next way up,
/// so holding W walks straight through one ▲ gate after another: no turning round, no circling the landmark.
/// <para>
/// The checks run the same <see cref="Walker"/> over the same <see cref="PlaceTerrain"/> as the app, at a fixed 60 Hz
/// frame, so "verified" means the real collision response touched nothing, not a geometric estimate. Tests and probes
/// also use <see cref="FollowWaypoints"/> to time walks.
/// </para>
/// </summary>
public static class PlaceRoutes
{
    /// <summary>The lane beside the landmark that straight upward routes use (metres off the north–south axis).</summary>
    public const float LaneOffset = 1.5f;

    /// <summary>The gaze on arrival after going up: a little down, so the avenue and the ▲ gate ahead are both in view.</summary>
    public static readonly Degrees ArrivalLookPitch = Degrees.Of(-4);

    /// <summary>The candidate spot beside the landmark is this far north of the landmark's edge beyond the walker's radius (metres).</summary>
    private const float BesideLandmarkClearance = 2f;

    /// <summary>The route aims at a point this far beyond the ▲ gate (metres): the heading then runs through the opening, not just up to it.</summary>
    private const float AimBeyondGate = 3f;

    /// <summary>The aim point is this far towards the lane's side of the ▲ gate's centre (metres): off-centre, still well inside the opening.</summary>
    private const float AimLaneOffset = 0.8f;

    /// <summary>A simulated walk gives up after its straight-line time × this, plus <see cref="WalkTimeSlackSeconds"/>.</summary>
    private const double WalkTimeAllowance = 1.5;

    private const double WalkTimeSlackSeconds = 1.5;

    /// <summary>The simulated frame rate, frames per second (the walker sub-steps inside each frame as in the app).</summary>
    private const double SimulatedFramesPerSecond = 60.0;

    /// <summary>The autopilot moves on to the next waypoint within this distance of the current one (metres).</summary>
    private const float WaypointReachRadius = 1.2f;

    /// <summary>A verified route never pushes the walker further than this off its straight line (metres): it touches nothing.</summary>
    public const float MaxDeviation = 0.05f;

    /// <summary>
    /// The pose for arriving in <paramref name="place"/> from its child <paramref name="childId"/> by going up. It is as
    /// close to that child's gate as a verified straight route allows: candidates run from the usual return spot in
    /// front of the gate towards a spot beside the landmark, and the first from which a simulated walker, walking
    /// straight ahead at walking and at running speed, crosses the ▲ gate without touching anything is used. Without a ▲ gate (the
    /// top of the tree) it is the usual return pose, facing into the place.
    /// </summary>
    public static UpwardRoute ContinueUp(PlaceComposition place, int childId, PlaceParams p, WalkParams w)
    {
        if (place.ParentGate is not { } up)
        {
            return new UpwardRoute(place.ReturnPose(childId, p), false, 0, default);
        }

        var gate = place.GateFor(childId);
        var from = gate == null ? (Vector3?)null : gate.Position - gate.Outward * p.ReturnStandOff;
        var side = from is { X: < -0.01f } ? -1f : 1f;
        var beside = new Vector3(side * LaneOffset, 0, -(p.LandmarkRadius + w.Radius.Value + BesideLandmarkClearance));
        // Through the ▲ gate's opening on the lane's side (the ▲ gate always faces south, outward = +Z).
        var through = up.Position + up.Outward * AimBeyondGate;
        var aim = new Vector3(through.X + side * AimLaneOffset, 0, through.Z);

        // With a child gate, eleven candidates, from the usual return spot (i = 0) to beside the landmark (i = 10): the first that verifies
        // is the closest to the child's gate, so you arrive as near as possible to where you came from.
        var candidates = new List<Vector3>();
        if (from is { } f)
        {
            for (var i = 0; i <= 10; i++) candidates.Add(Vector3.Lerp(f, beside, i / 10f));
        }
        else
        {
            candidates.Add(beside);
        }

        UpwardRoute? fallback = null;
        foreach (var c in candidates)
        {
            var ground = place.Terrain.SupportHeight(new Vector2(c.X, c.Z), w.Radius.Value, 0, w.StepHeight.Value);
            if (!place.Terrain.IsClear(new Vector2(c.X, c.Z), w.Radius.Value, ground, w.BodyHeight.Value)) continue;
            var dir = aim - c;
            var angle = PlaceTerrain.PlaceAngle(new Vector2(dir.X, dir.Z));
            var pose = PlaceGeometry.FacingPose(c with { Y = ground }, angle, w.EyeHeight, ArrivalLookPitch);
            // Both speeds must pass cleanly: the collision response differs slightly per sub-step distance.
            var walk = WalkStraight(place, pose, up, w, run: false);
            var run = walk.Crossed && walk.MaxDeviation < MaxDeviation ? WalkStraight(place, pose, up, w, run: true) : default;
            var standOff = gate == null ? 0 : Vector2.Distance(new Vector2(c.X, c.Z), new Vector2(gate.Position.X, gate.Position.Z));
            var route = new UpwardRoute(pose, run.Crossed && run.MaxDeviation < MaxDeviation, standOff, walk);
            if (route.Verified) return route;
            fallback ??= route;
        }

        // Nothing verified (should not happen with the composer's clear corridor): the spot beside the landmark.
        return fallback ?? new UpwardRoute(place.ReturnPose(childId, p), false, 0, default);
    }

    /// <summary>
    /// Walks straight ahead from <paramref name="start"/> (no steering) with a simulated walker and reports whether and
    /// when <paramref name="target"/> is crossed, and how far the walker was pushed off the straight line.
    /// </summary>
    public static RouteWalk WalkStraight(PlaceComposition place, PlacePose start, Gate target, WalkParams w, bool run, TimeSpan? limit = null)
    {
        var walker = new Walker(w);
        walker.TeleportToEye(place.Terrain, start.Eye, start.Yaw, start.LookPitch);
        var origin = new Vector2(walker.Feet.X, walker.Feet.Z);
        var heading = CameraMath.HorizontalForward(start.Yaw);
        var along = Vector2.Normalize(new Vector2(heading.X, heading.Z));
        var speed = (run ? w.RunSpeed : w.WalkSpeed).Value;
        var distance = Vector2.Distance(origin, new Vector2(target.Position.X, target.Position.Z));
        var max = limit ?? Durations.FromSeconds(distance / speed * WalkTimeAllowance + WalkTimeSlackSeconds);
        var frame = Durations.FromSeconds(1 / SimulatedFramesPerSecond);
        var input = new WalkInput(1, 0, 0, run, false); // W held (and Shift when running), nothing else
        var t = TimeSpan.Zero;
        var deviation = 0f;
        var walked = 0f;
        var last = walker.Eye;
        while (t < max)
        {
            walker.Update(place.Terrain, input, frame);
            t += frame;
            var eye = walker.Eye;
            var p = new Vector2(eye.X, eye.Z) - origin;
            deviation = MathF.Max(deviation, MathF.Abs(p.X * along.Y - p.Y * along.X)); // |2D cross product|: distance off the line
            walked += Vector2.Distance(new Vector2(last.X, last.Z), new Vector2(eye.X, eye.Z));
            if (ThresholdTracker.Crosses(target, last, eye) && Vector3.Dot(heading, target.Outward) >= ThresholdTracker.MinFacingCosine)
            {
                return new RouteWalk(true, t, deviation, walked);
            }

            last = eye;
        }

        return new RouteWalk(false, t, deviation, walked);
    }

    /// <summary>
    /// Walks a polyline of ground waypoints, turning towards each in turn (a simple autopilot, for timing routes in tests
    /// and probes), until <paramref name="target"/> is crossed. The heading is set directly each frame, so the time is
    /// the walking time of the route, not of anyone's steering.
    /// </summary>
    public static RouteWalk FollowWaypoints(PlaceComposition place, PlacePose start, IReadOnlyList<Vector2> waypoints, Gate target, WalkParams w, bool run, TimeSpan? limit = null)
    {
        var walker = new Walker(w);
        walker.TeleportToEye(place.Terrain, start.Eye, start.Yaw, start.LookPitch);
        var frame = Durations.FromSeconds(1 / SimulatedFramesPerSecond);
        var max = limit ?? TimeSpan.FromSeconds(120); // a generous cap: no route in a place takes minutes
        var t = TimeSpan.Zero;
        var next = 0;
        var walked = 0f;
        var last = walker.Eye;
        while (t < max)
        {
            var feet = new Vector2(walker.Feet.X, walker.Feet.Z);
            while (next < waypoints.Count - 1 && Vector2.Distance(feet, waypoints[next]) < WaypointReachRadius) next++;
            var to = waypoints[next] - feet;
            if (to.LengthSquared() > 1e-4f) walker.SetGaze(PlaceGeometry.YawFor(PlaceTerrain.PlaceAngle(to)), walker.LookPitch);
            walker.Update(place.Terrain, new WalkInput(1, 0, 0, run, false), frame);
            t += frame;
            var eye = walker.Eye;
            walked += Vector2.Distance(new Vector2(last.X, last.Z), new Vector2(eye.X, eye.Z));
            if (ThresholdTracker.Crosses(target, last, eye) && Vector3.Dot(CameraMath.HorizontalForward(walker.Yaw), target.Outward) >= ThresholdTracker.MinFacingCosine)
            {
                return new RouteWalk(true, t, 0, walked);
            }

            last = eye;
        }

        return new RouteWalk(false, t, 0, walked);
    }

    /// <summary>
    /// A walking route from the arrival terrace to a child gate: down the front stairs, past the landmark on the gate's
    /// side, and through the gate (ground waypoints, metres). The offsets suit the composed terrace and landmark: 6 m is
    /// just past the foot of the front stairs (≈ 5 m in front of the terrace centre), and 2.6 m off the axis passes clear
    /// of the landmark (≤ 0.85 m radius) with the walker's radius to spare.
    /// </summary>
    public static IReadOnlyList<Vector2> ArrivalToGate(PlaceComposition place, Gate gate)
    {
        static Vector2 Xz(Vector3 v) => new(v.X, v.Z);
        var arrival = Xz(place.Arrival.Eye);
        var side = gate.Position.X < -0.01f ? -1f : 1f;
        var points = new List<Vector2>
        {
            arrival + new Vector2(0, -6f),             // the foot of the terrace's front stairs
            new(side * 2.6f, 3.5f),                    // beside the landmark, south
            new(side * 2.6f, -3.5f),                   // beside the landmark, north
            Xz(gate.Position - gate.Outward * 3f),     // in front of the gate
            Xz(gate.Position + gate.Outward * 3f),     // through it
        };
        return points;
    }
}
