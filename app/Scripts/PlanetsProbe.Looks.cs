using FileScape.Core.Planets;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-looks --root=sample</c> (PLANET-94/95, AD-78): screenshots for judging two looks by eye, with a
/// few checks that the shots show what they claim. <b>The Learn to play card</b> at steps with different kinds of
/// instruction (a drag, a walk with a beacon, a key, a sequence of keys, the wheel); the card's text must name its keys.
/// <b>The way back</b> on a crowded world (Constellations, 36 child folders: three rings of pads and a belt) and on the
/// Starter Sky: from the arrival looking back at it, from across the rings (so it stands among ordinary pads), and from
/// far out (the far side of the outermost ring). The way-back treatment's state is read back (<c>WayBackMarked</c>).
/// Teleports and camera turns are [direct]: this is an instrument for pictures, not a play-through.
/// </summary>
public partial class PlanetsProbe
{
    private async Task LooksRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.0);
        var root = App.Navigator.Current;

        // ---- the Learn to play card
        App.StartTutorial();
        await Until(() => App.TutorialStep == 0, 15);
        await Seconds(0.6);
        Log($"card step 1: \"{App.LessonShownText}\"");
        Check(App.LessonShownText.Contains("drag", StringComparison.OrdinalIgnoreCase), $"step 1's instruction names its action: \"{App.LessonShownText}\"");
        await Shot("tutorial-card-1-look");
        foreach (var (step, name) in new[] { (1, "walk"), (3, "fly"), (7, "list"), (9, "back"), (12, "zoom") })
        {
            // Skipping past "5 · The overview" without opening it is undone at once (the later steps need it open): open it.
            for (var guard = 0; guard < 20 && App.TutorialStep < step && App.TutorialStep >= 0; guard++)
            {
                var before = App.TutorialStep;
                if (App.TutorialTitle.StartsWith("5 · The overview", StringComparison.Ordinal)) await Key(Godot.Key.T);
                else await Click(App.LessonButtonRect("skip").GetCenter());
                await Until(() => App.TutorialStep != before, 4);
            }

            await Seconds(0.8);
            var keys = App.TutorialKeys.Split('+');
            Log($"card step {step + 1}: \"{App.LessonShownText}\" keys '{App.TutorialKeys}'");
            Check(App.TutorialStep == step && keys.All(k => App.LessonShownText.Contains(k.Split(' ')[0], StringComparison.OrdinalIgnoreCase)), $"step {step + 1}'s card shows each of its keys ({App.TutorialKeys})");
            await Shot($"tutorial-card-{step + 1}-{name}");
        }

        if (P.UniverseOpen)
        {
            await Key(Godot.Key.Escape);
            await Until(() => !P.UniverseOpen && !P.Busy, 8);
        }

        await Click(App.LessonButtonRect("leave").GetCenter());
        await Frames(3);
        Check(!App.TutorialActive, "the walkthrough closes");
        if (App.Navigator.Current != root)
        {
            await Key(Godot.Key.U);
            await Until(() => !P.Busy && App.Navigator.Current == root, 15);
        }

        // ---- the way back
        var constellations = Child(root, "Constellations");
        if (constellations >= 0)
        {
            await GoToPath(Tree.FullPath(constellations));
            await Seconds(1.5);
            await WayBackShots("constellations");
        }

        // The Starter Sky's own children have the hub/This computer above? It is the sample's top: use a child of it instead.
        var garden = Child(root, "Garden Photos");
        if (garden >= 0)
        {
            await GoToPath(Tree.FullPath(garden));
            await Seconds(1.5);
            await WayBackShots("garden");
        }
    }

    /// <summary>Three views of the way back on the current world: from the arrival facing it, from across the rings, from far out.</summary>
    private async Task WayBackShots(string tag)
    {
        var c = P.Composition!;
        if (c.ParentLauncher is not { } up)
        {
            Check(false, $"{tag}: the world has a way back");
            return;
        }

        var r = c.Shape.Radius;
        Log(F($"{tag}: {c.Launchers.Count} pads, {c.SkyBodies.Count(b => b.InBelt)} moons, radius {r:0.0} m; way back at {P.DistanceTo(up.Direction):0.0} m"));
        Check(P.WayBackMarked, $"{tag}: the way back carries its marker treatment ({P.WayBackMarked})");

        // 1. From the arrival, turned round to face it.
        var arrival = P.Walker.Up;
        P.Walker.Teleport(c.Surface, arrival, Tangent(arrival, up.Direction));
        P.FaceCameraAlong(Tangent(arrival, up.Direction));
        await Seconds(1.2);
        Log(F($"{tag}: from the arrival, the way back {P.CameraTurnTo(up.Direction):0}° off the camera's heading"));
        await Shot($"wayback-{tag}-1-from-arrival");

        // 2. From across the rings: the point opposite the way back at the outermost pad's distance, facing it.
        var outer = c.Launchers.Max(l => SurfaceAngle(arrival, l.Direction)) * r;
        var across = PlanetFrame.Move(arrival, -Tangent(arrival, up.Direction), MathF.Min(outer * 0.6f, r * 1.2f), r);
        P.Walker.Teleport(c.Surface, across, Tangent(across, up.Direction));
        P.FaceCameraAlong(Tangent(across, up.Direction));
        await Seconds(1.2);
        Log(F($"{tag}: the way back {P.CameraTurnTo(up.Direction):0}° off the camera's heading"));
        Log(F($"{tag}: across the rings, {P.DistanceTo(up.Direction):0.0} m from the way back"));
        await Shot($"wayback-{tag}-2-across-the-rings");

        // 3. From far out: past the outermost ring on the far side.
        var far = PlanetFrame.Move(arrival, -Tangent(arrival, up.Direction), MathF.Min(outer + 6f, r * 1.6f), r);
        P.Walker.Teleport(c.Surface, far, Tangent(far, up.Direction));
        P.FaceCameraAlong(Tangent(far, up.Direction));
        await Seconds(1.2);
        Log(F($"{tag}: far out, {P.DistanceTo(up.Direction):0.0} m from the way back"));
        await Shot($"wayback-{tag}-3-far");
        P.Walker.Teleport(c.Surface, arrival, Tangent(arrival, up.Direction));
        await Seconds(0.3);
    }

    /// <summary>The unit tangent at <paramref name="from"/> pointing along the surface towards <paramref name="to"/> (unit directions).</summary>
    private static NVector3 Tangent(NVector3 from, NVector3 to)
    {
        var t = to - from * NVector3.Dot(from, to);
        return t.LengthSquared() < 1e-8f ? PlanetFrame.North(from) : NVector3.Normalize(t);
    }

    /// <summary>The angle between two unit directions, radians.</summary>
    private static float SurfaceAngle(NVector3 a, NVector3 b) => MathF.Acos(Math.Clamp(NVector3.Dot(a, b), -1f, 1f));
}
