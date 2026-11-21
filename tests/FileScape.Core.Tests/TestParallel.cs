namespace FileScape.Core.Tests;

/// <summary>
/// Evaluates independent, deterministic cases of one test side by side (TEST-12, AD-90): a sweep of simulated jumps, the
/// same check in every key, a render per arrangement. xUnit runs test classes in parallel but each class's tests (and a
/// test's own loop) one after another, so a single test that loops over 30 independent renders is the suite's
/// critical path however many cores there are.
/// What a change must preserve: <see cref="Map{TIn,TOut}"/> returns results in input order and only computes; the caller
/// asserts afterwards on the calling thread, in the same order and with the same messages as the loop it replaced, so a
/// failure reads as it did. A case must not share mutable state with another (each builds its own synth or walker).
/// </summary>
public static class TestParallel
{
    /// <summary>
    /// <paramref name="compute"/> for every item, side by side; the results in the items' order. An exception in any case
    /// is rethrown as it was (the first in input order), not wrapped.
    /// </summary>
    public static TOut[] Map<TIn, TOut>(IReadOnlyList<TIn> items, Func<TIn, TOut> compute)
    {
        var results = new TOut[items.Count];
        var errors = new Exception?[items.Count];
        Parallel.For(0, items.Count, i =>
        {
            try { results[i] = compute(items[i]); }
            catch (Exception e) { errors[i] = e; }
        });
        foreach (var e in errors)
        {
            if (e != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw();
        }

        return results;
    }
}
