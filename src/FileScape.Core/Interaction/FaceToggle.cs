using System.Numerics;
using FileScape.Core.Units;

namespace FileScape.Core.Interaction;

/// <summary>Whether a second F turns back (<see cref="Restore"/>), or why it is an ordinary Face instead.</summary>
public enum FaceRestore
{
    Restore,
    NothingToRestore,
    OtherWorld,
    OtherSelection,
    Moved,
    LookedAway,
    TooLate,
}

/// <summary>
/// Makes F / Face reversible (PLANET-123, AD-84). DJ (2026-11-20): "A second press should restore the prior facing or
/// camera orientation when that state is still applicable." The app records the view it had before each Face
/// (<see cref="Faced"/>, an opaque <typeparamref name="TView"/>: the walking camera's heading, or the overview's framing)
/// with where things stood just after it; the next press asks <see cref="TryRestore"/>.
/// <para>
/// <b>Applicable</b> means all of: the same world (or view) and the same selection as the Face; moved no more than
/// <see cref="DefaultMaxMoved"/> since (a shuffle, not a walk away); the view still within
/// <see cref="DefaultMaxLookedAway"/> of what the Face turned to (turning by hand makes the old view stale); and within
/// <see cref="DefaultWindow"/>. Otherwise the press is a fresh Face, and the stale record is forgotten.
/// </para>
/// Preserve: one record at a time (F, F, F alternates face / back / face); never restores across worlds; pure and
/// deterministic (time is passed in).
/// </summary>
public sealed class FaceToggle<TView>(Meters maxMoved, Degrees maxLookedAway, TimeSpan window)
{
    /// <summary>Moving further than this, metres, since the Face makes turning back stale (≈ a third of a second's run at 8 m/s).</summary>
    public static readonly Meters DefaultMaxMoved = Meters.Of(2.5f);

    /// <summary>Turning the view further than this from what the Face turned to makes turning back stale (by eye).</summary>
    public static readonly Degrees DefaultMaxLookedAway = Degrees.Of(20f);

    /// <summary>A second press later than this after the Face is a fresh Face (a glance, then back; by eye).</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(20);

    public FaceToggle() : this(DefaultMaxMoved, DefaultMaxLookedAway, DefaultWindow)
    {
    }

    /// <summary>Where things stand: the world (or view) id, the selection, a position in metres, the view's forward (unit), the app clock.</summary>
    public readonly record struct Where(int World, int Selection, Vector3 PositionMetres, Vector3 Forward, TimeSpan Now);

    private (Where After, TView Prior)? _last;

    /// <summary>A Face happened: <paramref name="prior"/> is the view before it, <paramref name="after"/> where things stand just after.</summary>
    public void Faced(Where after, TView prior) => _last = (after, prior);

    /// <summary>Forgets the last Face (a world change, the view closing).</summary>
    public void Forget() => _last = null;

    /// <summary>Whether a press now would turn back, or why not.</summary>
    public FaceRestore Check(Where now)
    {
        if (_last is not { } last) return FaceRestore.NothingToRestore;
        var a = last.After;
        if (now.World != a.World) return FaceRestore.OtherWorld;
        if (now.Selection != a.Selection) return FaceRestore.OtherSelection;
        if (Vector3.Distance(now.PositionMetres, a.PositionMetres) > maxMoved.Value) return FaceRestore.Moved;
        var cos = Vector3.Dot(Unit(now.Forward), Unit(a.Forward));
        if (cos < MathF.Cos(maxLookedAway.ToRadians())) return FaceRestore.LookedAway;
        if (now.Now - a.Now > window) return FaceRestore.TooLate;
        return FaceRestore.Restore;
    }

    /// <summary>
    /// A press: true with the view to restore when applicable (the record is then used up); false otherwise, and a stale
    /// record is forgotten, so the caller faces again (and records that Face).
    /// </summary>
    public bool TryRestore(Where now, out TView prior)
    {
        var ok = Check(now) == FaceRestore.Restore;
        prior = ok ? _last!.Value.Prior : default!;
        _last = null;
        return ok;
    }

    private static Vector3 Unit(Vector3 v) => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : v;
}
