namespace FileScape.App;

/// <summary>
/// The delivery smoke check (--automate=smoke, run by tools/fs.sh export before promoting a build): the app starts on
/// synthetic data, composes a place, walks a few frames, and quits with 0. It proves the exported files run.
/// </summary>
public partial class SmokeProbe : ProbeBase
{
    public SmokeProbe(Main main, string outDir) : base(main, outDir, "smoke")
    {
    }

    protected override async Task RunProbe()
    {
        await Until(() => App.Place != null && !App.InTransition, 30);
        Check(App.Place != null && App.Place.Gates.Count > 0, "a place is composed on the sample data");
        await Hold(0.5, Godot.Key.Up);
        Check(App.Locomotion == Locomotion.Walking && App.Walker.Grounded, "the walker moves and stays grounded");
        if (Failures == 0) Log($"smoke ok build={BuildInfo.Identifier}");
    }
}
