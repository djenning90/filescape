namespace FileScape.Core.Tests;

/// <summary>
/// The test kinds (TEST-11, AD-90): an xUnit trait <c>[Trait(TestKinds.Trait, TestKinds.Perf)]</c> that says a test is
/// not an ordinary correctness check, so the runner can keep it out of the parallel run and give it the conditions it
/// needs. <c>tools/fs.sh test</c> and <c>test-wsl</c> run everything except <see cref="Perf"/> (in parallel);
/// <c>tools/fs.sh test-perf</c> runs only <see cref="Perf"/>, one test at a time, and <c>check full</c> / <c>release</c>
/// run it as its own step after the parallel tests. <c>--fast</c> also leaves out <see cref="Report"/> and
/// <see cref="Slow"/>.
/// Why: wall-clock bounds measured beside 900 other tests, the app build and the other host's suite failed under load and
/// passed alone (2026-11-19/20: "…_is_cheap" 61.5 ms vs 60 ms, the throttled-scan cancel 309 ms vs 250 ms), and a test
/// that only prints a table spends the default loop's time without checking anything.
/// What a change must preserve: an ordinary test (no trait) never depends on the machine's speed or load; a test that
/// asserts elapsed wall-clock time is <see cref="Perf"/>; the trait name and values are what <c>tools/fs.sh</c>'s filters
/// name, so renaming one means changing the script too.
/// </summary>
public static class TestKinds
{
    /// <summary>The trait's name in <c>dotnet test --filter "Kind!=Perf"</c>.</summary>
    public const string Trait = "Kind";

    /// <summary>
    /// Asserts on elapsed wall-clock time (a Stopwatch bound, "cancelled within N ms", "renders N s of audio in M ms").
    /// Runs only in the serial perf step, never beside the build, the other shards or the other host's tests.
    /// </summary>
    public const string Perf = "Perf";

    /// <summary>
    /// Prints a measurement table (a design record, e.g. tuning candidates compared one at a time) and asserts nothing.
    /// Deterministic, so it runs in the ordinary parallel run; left out of the fast loop.
    /// </summary>
    public const string Report = "Report";

    /// <summary>
    /// A deterministic correctness check that takes several seconds even alone (a sweep over many simulated attempts,
    /// many cycles of music, a large fixture). In the ordinary run; left out of the fast loop, whose purpose is a quick
    /// signal while editing. Mark a test Slow only when it takes over ≈ 3 s alone on WSL (the area set still runs it).
    /// </summary>
    public const string Slow = "Slow";
}
