using FileScape.App.Planets;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-lookouts --root=sample --vis=planets --test-shell=record</c> (PLANET-55, PLANET-56): the Starter
/// Sky's telescope and the Music Box's bell tower with real keys. <b>Telescope:</b> far from the overlook its sign is
/// hidden; walking up (W) it appears and the prompt points up the steps; climbing the stones it reads in full; on the
/// platform E opens the world's list. <b>Bell:</b> an ordinary jump and a twirl beside the tower ring nothing; W walks
/// onto the base and under the bell (it stands on the base, as drawn); an ordinary jump there rings nothing; a jump and
/// twirl rings it once (the bell swings) and opens nothing; walking on out between the far pillars works. The test shell
/// records any start: nothing may be recorded here.
/// </summary>
public partial class PlanetsProbe
{
    private async Task LookoutsRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5);
        var root = App.Navigator.Current;
        var shell0 = App.ShellLog.Count;
        await TelescopeVisit();
        await Key(Godot.Key.Escape);
        await Frames(4);

        var music = Child(root, "Music Box");
        App.Selection.Select(music); // [direct] the selection a click on its world makes
        await Frames(2);
        await Fly(Godot.Key.G, music, "Music Box");
        await Seconds(1.5);
        await BellVisit();
        Check(App.ShellLog.Count == shell0, $"nothing was started or recorded by the shell in this run ({App.ShellLog.Count - shell0})");
    }

    /// <summary>The telescope: hidden far away, the sign and the prompt on the walk up, E on the platform opens the list.</summary>
    private async Task TelescopeVisit()
    {
        var c = P.Composition!;
        var ov = c.Overlook;
        var r = c.Shape.Radius;
        // [direct] start 14 m out from the overlook on the arrival's side, facing it (where you come from).
        var away = NVector3.UnitY - ov.Direction * NVector3.Dot(NVector3.UnitY, ov.Direction);
        var start = PlanetFrame.Move(ov.Direction, NVector3.Normalize(away), ov.Radius + 14f, r);
        P.Walker.Teleport(c.Surface, start, ov.Direction - start);
        P.FaceCameraAlong(NVector3.Normalize(ov.Direction - start * NVector3.Dot(ov.Direction, start)));
        await Seconds(1.0);
        var far = P.TelescopeCue;
        Check(!far.Offered && !App.Signs.ShownIds.Contains(PlanetWorld.TelescopeSignId) && far.Cue < 0.05f, F($"14 m from the overlook the telescope's sign is hidden (cue {far.Cue:0.00})"));
        await Shot("lookouts-telescope-far");

        // Walk up to the foot of the stones (real keys).
        await WalkToward(ov.StepFoot, c.Overlook.Steps[0].Radius + 0.9f, 15);
        await Seconds(0.6);
        var near = P.TelescopeCue;
        var shown = App.Signs.ShownText.GetValueOrDefault(PlanetWorld.TelescopeSignId, "");
        Check(near.Cue > 0.6f && App.Signs.ShownIds.Contains(PlanetWorld.TelescopeSignId) && shown.Contains(PlanetWorld.TelescopeSignName),
            F($"at the foot of the stones the sign over the telescope shows \"{shown.Replace('\n', ' ')}\" (cue {near.Cue:0.00})"));
        Check(P.PromptText.Contains("telescope", StringComparison.OrdinalIgnoreCase) && P.PromptText.Contains("files and destinations"), $"the prompt points up to it: \"{P.PromptText}\"");
        if (P.TelescopePoint is { } tp && !P.Camera.IsPositionBehind(tp))
        {
            var s = P.Camera.UnprojectPosition(tp);
            var size = App.GetViewport().GetVisibleRect().Size;
            Check(s.X > 0 && s.Y > 0 && s.X < size.X && s.Y < size.Y, F($"the telescope (and its sign above it) is on screen at ({s.X:0},{s.Y:0})"));
        }

        await Shot("lookouts-telescope-approach");

        // Up the stones (real keys: W towards each stone in turn, jumping when stuck), onto the platform.
        foreach (var step in ov.Steps) await WalkToward(step.Direction, step.Radius * 0.6f, 6);
        await WalkToward(ov.Direction, ov.Radius * 0.5f, 6);
        await Until(() => P.OnOverlook, 3);
        await Seconds(0.8);
        var top = P.TelescopeCue;
        Check(P.OnOverlook && App.Signs.ShownIds.Contains(PlanetWorld.TelescopeSignId) && top.Cue > 0.9f, F($"on the platform (by its stones) the sign is shown and the telescope lit: cue {top.Cue:0.00}"));
        Check(P.PromptText.StartsWith("E · Telescope", StringComparison.Ordinal), $"the prompt: \"{P.PromptText}\"");
        await Shot("lookouts-telescope-top");
        await Key(Godot.Key.E);
        await Until(() => P.ListOpen, 3);
        Check(P.ListOpen, "E at the telescope opens this world's list (files and destinations)");
        await Seconds(0.4);
        await Shot("lookouts-telescope-list");
    }

    /// <summary>The bell tower: jumps beside it ring nothing; walk under the bell; a jump there rings nothing; a twirl rings it.</summary>
    private async Task BellVisit()
    {
        var c = P.Composition!;
        var lm = c.Landmark;
        Check(lm.Bell && c.Theme == PlanetTheme.Chime, $"Music Box has the bell tower ({c.Theme})");
        if (P.BellTowerApproach(5f) is not { } at) return;
        var r = c.Shape.Radius;
        P.Walker.Teleport(c.Surface, at.Outside, at.Axis - at.Outside); // [direct] 5 m out on the arrival's side, facing it
        P.FaceCameraAlong(NVector3.Normalize(at.Axis - at.Outside * NVector3.Dot(at.Axis, at.Outside)));
        await Seconds(1.0);
        await Shot("lookouts-bell-approach");
        var rings0 = P.BellRings;

        // Beside the tower (outside its base): an ordinary jump, then a jump and twirl: just jumps.
        await Key(Godot.Key.Space);
        await Until(() => P.Walker.Grounded, 3);
        await Seconds(0.3);
        await JumpAndTwirl();
        Check(P.BellRings == rings0 && !P.PreviewOpen && !P.ListOpen, F($"beside the tower ({P.DistanceTo(lm.Direction):0.0} m from its axis) a jump and a twirl are only jumps: rings {P.BellRings - rings0}"));

        // Walk in (real keys) onto the base and under the bell.
        await WalkToward(lm.Direction, 0.35f, 10);
        await Seconds(0.6);
        var d = P.DistanceTo(lm.Direction);
        Check(P.UnderBell && d < 0.9f, F($"walked onto the base and under the bell: {d:0.00} m from its axis (the old solid stopped you 1.95 m out)"));
        Check(MathF.Abs(P.Walker.Support.Value - BellTower.BaseTopMetres) < 0.02f, F($"standing on the base as drawn: support {P.Walker.Support.Value:0.00} m (base top {BellTower.BaseTopMetres:0.00} m)"));
        Check(P.PromptText.Contains("twirl to ring the bell"), $"the prompt says how to ring it: \"{P.PromptText}\"");
        await Shot("lookouts-bell-under");

        // An ordinary jump under the bell: nothing.
        await Key(Godot.Key.Space);
        await Until(() => P.Walker.Grounded, 3);
        await Seconds(0.4);
        Check(P.BellRings == rings0, "an ordinary jump under the bell does not ring it");

        // Jump and twirl under the bell: it rings once and swings.
        var swing = 0f;
        var apex = await JumpAndTwirl(() => swing = MathF.Max(swing, MathF.Abs(P.BellSwing)), shot: "lookouts-bell-twirl");
        Check(apex + BellTower.TravellerHeightMetres < BellTower.BellRimMetres, F($"the twirl stays under the bell: feet up to {apex:0.00} m + the traveller {BellTower.TravellerHeightMetres:0.00} m < the rim at {BellTower.BellRimMetres:0.00} m"));
        Check(P.BellRings == rings0 + 1, $"a jump and twirl under the bell rings it once ({P.BellRings - rings0})");
        for (var i = 0; i < 40; i++) { await Frames(1); swing = MathF.Max(swing, MathF.Abs(P.BellSwing)); }
        Check(swing > 0.1f, F($"the bell swings: up to {swing * 180 / MathF.PI:0.0}°"));
        await Shot("lookouts-bell-swing");
        Check(!P.PreviewOpen && !P.ListOpen, "ringing opens nothing");
        await Seconds(1.5);

        // Again: a second twirl rings it a second time.
        await JumpAndTwirl();
        Check(P.BellRings == rings0 + 2, $"another twirl rings it again ({P.BellRings - rings0})");

        // And out through the far side, between the pillars.
        var through = PlanetFrame.Move(lm.Direction, -PlanetFrame.North(lm.Direction), 4f, r);
        await WalkToward(through, 0.6f, 8);
        Check(P.DistanceTo(through) < 1.2f, F($"walked on out between the far pillars ({P.DistanceTo(through):0.0} m from the far side)"));
        Log($"the walk: {P.Walker.Jumps} jumps, {P.Walker.AirJumps} twirls in all; bell rings {P.BellRings}");
    }

    /// <summary>Space held 0.3 s (a full jump), released, then pressed again in the air (the twirl), as a player does; <paramref name="each"/> runs every frame until landing.</summary>
    private async Task<float> JumpAndTwirl(Action? each = null, string? shot = null)
    {
        var air0 = P.Walker.AirJumps;
        var apex = 0f;
        Key1(Godot.Key.Space, true);
        await Seconds(0.3);
        Key1(Godot.Key.Space, false);
        await Frames(2);
        Key1(Godot.Key.Space, true);
        for (var i = 0; i < 90 && !(i > 10 && P.Walker.Grounded); i++)
        {
            await Frames(1);
            each?.Invoke();
            apex = MathF.Max(apex, P.Walker.Height.Value);
            if (shot != null && i == 12) await Shot(shot);
        }

        Key1(Godot.Key.Space, false);
        await Until(() => P.Walker.Grounded, 3);
        await Frames(4);
        Check(P.Walker.AirJumps == air0 + 1, F($"  (one twirl, feet up to {apex:0.00} m)"));
        return apex;
    }
}
