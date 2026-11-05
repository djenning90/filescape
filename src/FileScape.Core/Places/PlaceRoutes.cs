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
/// </summary>
public static class PlaceRoutes
{
    /// <summary>The lane beside the landmark that straight upward routes use (metres off the north–south axis).</summary>
    public const float LaneOffset = 1.5f;

    /// <summary>The gaze on arrival after going up: a little down, so the avenue and the ▲ gate ahead are both in view.</summary>
    public static readonly Degrees ArrivalLookPitch = Degrees.Of(-4);

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
        var beside = new Vector3(side * LaneOffset, 0, -(p.LandmarkRadius + w.Radius.Value + 2f));
        // Through the ▲ gate's opening on the lane's side (the ▲ gate always faces south, outward = +Z).
        var through = up.Position + up.Outward * 3f;
        var aim = new Vector3(through.X + side * 0.8f, 0, through.Z);

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
        var max = limit ?? Durations.FromSeconds(distance / speed * 1.5 + 1.5);
        var frame = Durations.FromSeconds(1 / 60.0);
        var input = new WalkInput(1, 0, 0, run, false);
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
            deviation = MathF.Max(deviation, MathF.Abs(p.X * along.Y - p.Y * along.X));
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
        var frame = Durations.FromSeconds(1 / 60.0);
        var max = limit ?? TimeSpan.FromSeconds(120);
        var t = TimeSpan.Zero;
        var next = 0;
        var walked = 0f;
        var last = walker.Eye;
        while (t < max)
        {
            var feet = new Vector2(walker.Feet.X, walker.Feet.Z);
            while (next < waypoints.Count - 1 && Vector2.Distance(feet, waypoints[next]) < 1.2f) next++;
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
    /// side, and through the gate (ground waypoints, metres).
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
