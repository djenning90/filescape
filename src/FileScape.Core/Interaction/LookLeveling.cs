using FileScape.Core.Units;

namespace FileScape.Core.Interaction;

/// <summary>
/// Eases the look pitch back to level in the legacy walking views: after a mouse look ends (CTRL-04), so letting go of
/// the button never leaves you staring at the sky or the ground, and after a flight lands you on foot (FLY-05). Only the
/// pitch changes; the heading you turned to is kept. Smoothstep over <see cref="Duration"/>, so it starts and ends
/// gently and always finishes. Planets levels its own look elsewhere (PlanetWorld), not through this type.
/// </summary>
public sealed class LookLeveling(Degrees from)
{
    /// <summary>
    /// How long the return to level takes, whatever the starting pitch: quick enough not to wait for, slow enough to
    /// follow. A fixed time rather than a rate, so a steep look and a slight one both finish together. (Seventh-pass
    /// review: the name reads clearly as <c>LookLeveling.Duration</c> at its call sites, so it is kept.)
    /// </summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(0.35);

    private TimeSpan _elapsed;

    public Degrees From { get; } = from;
    public bool Done => _elapsed >= Duration;

    /// <summary>Advances by <paramref name="dt"/> and returns the pitch to use now (exactly zero once done).</summary>
    public Degrees Step(TimeSpan dt)
    {
        _elapsed += dt;
        if (Done) return Degrees.Zero;
        var k = (float)(_elapsed / Duration);
        var s = k * k * (3 - 2 * k);
        return From * (1 - s);
    }
}
