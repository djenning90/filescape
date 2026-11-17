using Godot;

namespace FileScape.App;

/// <summary>
/// --automate=planets-sky (PLANET-60, AD-64): the sky's slow turn and a shooting star, standing and in flight, on the
/// Starter Sky. Owns the measured claims that the turn moves stars far too slowly to read as camera drift while
/// standing (pixels per second on screen), that a shooting star is short and in view, and that the overview shows
/// none; and the stills (and, under <c>--write-movie … --fixed-fps 30</c>, the clip) DJ judges the look by. The random
/// shooting stars are paused so the clip shows one at a chosen moment; their timing is named in PlanetWorld.Sky.cs.
/// A change must keep: standing first, then one flight, each with one shooting star, about 20 s in all.
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>Standing still, a star may move at most this fast on screen from the sky's turn, pixels per second (at UI scale 1).</summary>
    private const float SkyDriftLimitPxPerSecond = 0.5f;

    private async Task SkyRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        P.PauseMeteors(true);
        await Until(() => P.CameraSettled, 8);
        await Seconds(1.0);
        _recording = true;

        // ---- standing: the turn's speed on screen, measured where it is fastest (90° from the axis, in view)
        var cam = P.Camera;
        var t0 = P.SkyTurnDegrees;
        await Seconds(2.0);
        var turned = P.SkyTurnDegrees - t0;
        var rate = turned / 2.0f * 60f;
        Check(MathF.Abs(rate - Planets.PlanetWorld.SkyTurnDegreesPerMinute) < 0.1f, F($"the sky turns at {rate:0.00}°/min (set {Planets.PlanetWorld.SkyTurnDegreesPerMinute}°/min)"));
        var fwd = -cam.GlobalBasis.Z;
        var screenPerDegree = App.GetViewport().GetVisibleRect().Size.Y / cam.Fov; // pixels per degree near the view's centre
        var pxPerSecond = Planets.PlanetWorld.SkyTurnDegreesPerMinute / 60f * screenPerDegree;
        Check(pxPerSecond <= SkyDriftLimitPxPerSecond, F($"standing still a star moves at most {pxPerSecond:0.00} px/s from the turn ({screenPerDegree:0.0} px per degree at the {cam.Fov:0}° view): far below camera drift"));
        await Shot("sky-standing");

        // ---- a shooting star while standing
        await Seconds(1.5);
        var shown = P.MeteorsShown;
        Check(P.StartMeteor(0.3f, 0.5f, 0.4f, leftward: true), "[direct] a shooting star is started on open sky");
        var start = cam.UnprojectPosition(cam.GlobalPosition + P.MeteorFrom * 1000f);
        var inView = !cam.IsPositionBehind(cam.GlobalPosition + P.MeteorFrom * 1000f) && App.GetViewport().GetVisibleRect().HasPoint(start);
        Check(P.MeteorShowing && P.MeteorsShown == shown + 1 && inView, F($"a shooting star starts in view, at ({start.X:0},{start.Y:0})"));
        await Seconds(0.35);
        await Shot("sky-standing-shooting-star");
        await Seconds(Planets.PlanetWorld.MeteorSeconds);
        Check(!P.MeteorShowing, $"and is gone within {Planets.PlanetWorld.MeteorSeconds} s");
        await Seconds(2.0);

        // ---- the sky a few minutes on (the same view): what the turn amounts to
        if (!RecordingMovie) // a clip shows the sky as it is: no jump
        {
            P.AdvanceSkyTurn(3f); // [direct] three minutes' worth
            await Frames(3);
            await Shot("sky-standing-3-minutes-later");
            P.AdvanceSkyTurn(-3f);
            await Frames(3);
        }

        // ---- a flight, with a shooting star on the way
        var hub = App.Navigator.Current;
        var target = Tree[hub].Children.FirstOrDefault(c => Tree[c].IsDirectory && Tree[c].Name == "Constellations", -1);
        if (target < 0) target = Tree[hub].Children.First(c => Tree[c].IsDirectory);
        App.Selection.Select(target); // [direct] the selection a click makes
        await Frames(2);
        await Key(Godot.Key.G);
        await Until(() => P.FlightProgress > 0.3f, 6);
        var flightStar = P.StartMeteor(0.4f, 0.45f, 0.5f, leftward: true);
        var fs = P.Camera.UnprojectPosition(P.Camera.GlobalPosition + P.MeteorFrom * 1000f);
        Check(flightStar, F($"[direct] a shooting star in flight, from ({fs.X:0},{fs.Y:0})"));
        await Seconds(0.3);
        var fs2 = P.Camera.UnprojectPosition(P.Camera.GlobalPosition + P.MeteorFrom * 1000f);
        Log(F($"0.3 s on, the shooting star's start is at ({fs2.X:0},{fs2.Y:0}) on screen (the flight camera turns), showing: {P.MeteorShowing}"));
        await Shot("sky-flight-shooting-star");
        await Until(() => !P.Busy, 8);
        await WaitSettled();
        Check(App.Navigator.Current == target, $"landed on {Tree[target].Name}");
        P.PauseMeteors(false);
        var landed = P.MeteorsShown;
        P.MeteorDueNow(); // [direct] a random one falls due just after the landing
        await Seconds(0.5);
        Check(P.MeteorsShown == landed, "no shooting star in the first seconds after a landing");
        P.PauseMeteors(true);
        await Seconds(2.0);
        await Shot("sky-arrived");

        // ---- the overview shows none
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 4);
        P.PauseMeteors(false);
        var before = P.MeteorsShown;
        P.MeteorDueNow(); // [direct] due while the overview is open
        await Seconds(1.0);
        Check(P.UniverseOpen && P.MeteorsShown == before, "the overview shows no shooting star");
        await Key(Godot.Key.Escape);
        await Until(() => !P.UniverseOpen, 4);
        await Seconds(0.5);
    }
}
