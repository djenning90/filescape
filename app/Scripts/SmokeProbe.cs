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

    /// <summary>
    /// How long the app may take to compose its first place, seconds: generous, so a slow first start of a fresh export
    /// is not mistaken for a broken build (the exact figure's rationale is not recorded).
    /// </summary>
    private const double FirstPlaceTimeoutSeconds = 30;

    /// <summary>How long the walk forward is held, seconds: enough for several physics frames of movement.</summary>
    private const double WalkSeconds = 0.5;

    protected override async Task RunProbe()
    {
        await Until(() => App.Place != null && !App.InTransition, FirstPlaceTimeoutSeconds);
        Check(App.Place != null && App.Place.Gates.Count > 0, "a place is composed on the sample data");
        await Hold(WalkSeconds, Godot.Key.Up);
        Check(App.Locomotion == Locomotion.Walking && App.Walker.Grounded, "the walker moves and stays grounded");
        if (Failures == 0) Log($"smoke ok build={BuildInfo.Identifier}");
    }
}
