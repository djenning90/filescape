using FileScape.Core.Planets;
using Godot;

namespace FileScape.App;

/// <summary>
/// --automate=planets-relief (PLANET-59): ordinary worlds' relief judged in the app, on a small and a large world of the
/// Starter Sky. Owns the in-app evidence that the drawn ground, the traveller's feet and the pads agree while walking
/// and landing, and the screenshots for comparing the relief (run once more with <c>--test-relief=classic</c> for the
/// hills as they were). Per world: a shot late in the landing (the silhouette from above), the arrival, a look towards
/// the highest rise near the arrival, and a walk up it (silhouettes against the sky) with the feet measured every frame.
/// A change must keep: the same worlds (the smallest Starter Sky folder with at least three folders, and the largest).
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>On the ground, the feet may be this far from the walked ground (the shape's height there), metres.</summary>
    private const float FeetOnGroundMetres = 0.002f;

    /// <summary>How long each walk out over the rises lasts, seconds of held W (at 6 m/s ≈ 30 m: past the rings, onto the hills).</summary>
    private const double ReliefWalkSeconds = 5;

    private async Task ReliefRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        var hub = App.Navigator.Current;
        var folders = Tree[hub].Children.Where(c => Tree[c].IsDirectory && HazardRules.Classify(Tree, c) == null && Tree[c].Children.Count(k => Tree[k].IsDirectory) >= 3).ToList();
        if (!Check(folders.Count >= 2, $"the Starter Sky has worlds to compare ({folders.Count})")) return;
        var small = folders.OrderBy(c => Tree[c].Children.Count).First();
        var large = folders.OrderByDescending(c => Tree[c].Children.Count).First();
        _recording = true;
        foreach (var (id, label) in new[] { (small, "small"), (large, "large") })
        {
            if (App.Navigator.Current != hub)
            {
                await Key(Godot.Key.Backspace);
                await Until(() => P.Busy, 3);
                await Until(() => !P.Busy, 10);
                await WaitSettled();
            }

            await ReliefWorld(id, label);
        }
    }

    /// <summary>Flies to one world (a shot late in the landing), then looks towards its highest nearby rise and walks up it.</summary>
    private async Task ReliefWorld(int id, string label)
    {
        var name = Tree[id].Name;
        App.Selection.Select(id); // [direct] the selection a click makes
        await Frames(2);
        await Key(Godot.Key.G);
        await Until(() => P.FlightProgress > 0.9f, 8);
        await Shot($"relief-{label}-landing");
        await Until(() => !P.Busy, 8);
        await WaitSettled();
        var c = P.Composition!;
        var s = c.Shape;
        Check(c.FolderId == id, $"landed on {name}");
        Log(F($"{label} world {name}: radius {s.Radius:0.0} m, relief {s.Relief}, hills {s.HillAmplitude:0.00} m, flat fade {s.FlatFade:0.0} m"));
        Check(c.Launchers.All(l => s.HeightAt(l.Direction) == 0) && s.HeightAt(P.Walker.Up) == 0, "the pads and the landing spot stand on level ground");
        await Until(() => P.CameraSettled, 5);
        await Seconds(1);
        await Shot($"relief-{label}-arrival");

        // The highest ground 12–30 m from the arrival (a rise to walk up), and a look towards it.
        var top = PlanetComposer.FibonacciSphere(4000)
            .Where(d => c.Surface.SurfaceDistance(d, System.Numerics.Vector3.UnitY) is > 12f and < 30f)
            .OrderByDescending(d => s.HeightAt(d)).First();
        Log(F($"the highest ground near the arrival: {s.HeightAt(top):0.00} m, {c.Surface.SurfaceDistance(top, System.Numerics.Vector3.UnitY):0.0} m out"));
        var toward = top - P.Walker.Up * System.Numerics.Vector3.Dot(top, P.Walker.Up);
        P.FaceCameraAlong(System.Numerics.Vector3.Normalize(toward)); // [direct] set-up
        await Frames(20);
        await Shot($"relief-{label}-toward-rise");

        // Walk up it, re-aiming when the heading drifts, measuring the feet against the ground every frame.
        var worst = 0f;
        var lo = float.MaxValue;
        var hi = float.MinValue;
        var frames = 0;
        HoldKey(Godot.Key.W, true);
        var t0 = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - t0 < ReliefWalkSeconds * 1000)
        {
            await Frames(1);
            var ahead = top - P.Walker.Up * System.Numerics.Vector3.Dot(top, P.Walker.Up);
            if (ahead.LengthSquared() > 1e-4f && c.Surface.SurfaceDistance(P.Walker.Up, top) > 1.5f) P.AimCameraAlong(System.Numerics.Vector3.Normalize(ahead)); // [direct] steering, as the route probes do
            if (!P.Walker.Grounded || P.Walker.Height.Value > 1e-4f) continue;
            frames++;
            var h = s.HeightAt(P.Walker.Up);
            lo = MathF.Min(lo, h);
            hi = MathF.Max(hi, h);
            worst = MathF.Max(worst, MathF.Abs(P.Walker.Feet.Length() - s.GroundRadius(P.Walker.Up)));
            if (frames == 150) await Shot($"relief-{label}-walking");
        }

        HoldKey(Godot.Key.W, false);
        await Frames(5);
        Check(frames > 60 && worst <= FeetOnGroundMetres, F($"walking over {label} {name}'s rises the feet stay on the ground: {frames} grounded frames, largest gap {worst * 1000:0.0} mm; ground from {lo:0.00} to {hi:0.00} m"));
        await Shot($"relief-{label}-walked");
    }
}
