using System.Text;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using Godot;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// The walking-world journey (--automate=walk…, WALK/JUMP/LINK/LOC/VISI). Input is injected through Godot's pipeline
/// (ProbeBase); "[direct]" steps only set up a case.
/// </summary>
public partial class WalkProbe : ProbeBase
{
    private readonly StringBuilder _perf = new("case,quality,frames,mean_ms,p50_ms,p95_ms,p99_ms,max_ms,gpu_mean_ms,instances,tree_nodes\n");

    public WalkProbe(Main main, string outDir, string mode) : base(main, outDir, mode)
    {
    }

    private FsTree Tree => App.Tree!;
    private PlaceComposition Place => App.Place!;

    protected override void Finish() => System.IO.File.WriteAllText(System.IO.Path.Combine(Out, $"{Mode}-perf.csv"), _perf.ToString());

    protected override async Task RunProbe()
    {
        await Until(() => App.Place != null && !App.InTransition, 20);
        await Seconds(1.0);
        if (Mode == "walk-look") await LookAround();
        else await Journey();
    }

    /// <summary>Quick visual check of the walking scale: arrival, plaza, overlook, flight.</summary>
    private async Task LookAround()
    {
        await Shot("arrival-on-foot");
        LogPose("arrival");
        await Hold(1.6, Godot.Key.Up);
        await Seconds(0.4);
        LogPose("after walking down the stairs");
        await Shot("plaza-at-eye-level");
        var ov = Place.Overlook;
        var foot = ov.RampFoot;
        var axis = NVector2.Normalize(new NVector2(ov.Top.X, ov.Top.Z) - foot);
        var start = foot - axis * 2;
        App.ProbePlaceUser(new PlacePose(new NVector3(start.X, 1.75f, start.Y), -PlaceTerrain.PlaceAngle(axis), -5)); // [direct]
        await Frames(3);
        await Hold(3.2, Godot.Key.W);
        await Seconds(0.5);
        LogPose("after walking up the ramp");
        App.ProbeSetGaze(-PlaceTerrain.PlaceAngle(-new NVector2(ov.Top.X, ov.Top.Z)) , -8); // [direct] face the plaza
        await Frames(3);
        await Shot("overlook-view");
        App.ProbeSetGaze(-PlaceTerrain.PlaceAngle(new NVector2(ov.Top.X, ov.Top.Z)) + 180 - 180, 0);
        await Key(Godot.Key.T);
        await Seconds(1.5);
        LogPose("flying");
        await Shot("flying");
        await Key(Godot.Key.T);
        await Seconds(2.0);
        LogPose("landed");
        await Shot("landed");
    }

    private Task Journey() => Task.CompletedTask;

    private void LogPose(string what)
    {
        var w = App.Walker;
        Log(F($"{what}: locomotion={App.Locomotion} feet=({w.Feet.X:0.00},{w.Feet.Y:0.00},{w.Feet.Z:0.00}) support={w.Support:0.00} eye={App.Rig.CameraPosition.Y:0.00} yaw={w.Yaw:0.0} pitch={w.LookPitch:0.0} grounded={w.Grounded}"));
    }
}
