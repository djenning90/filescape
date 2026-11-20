using FileScape.Core.Planets;

namespace FileScape.Core.Audio;

/// <summary>
/// What the player is doing, for the music (AD-79, AD-85): the app describes each frame (<see cref="Moment"/>: in the
/// overview, on the overlook, looking through its telescope, travelling, which folder) and this decides the
/// <see cref="MusicExperience"/> to tell <see cref="SkySynth.SetExperience"/>, and how many times that experience has begun
/// at that place (the synth's visits, which pick a forgotten revisit's variation). It owns the overlook's hysteresis —
/// on after <see cref="OverlookEnterSeconds"/> on it, off after <see cref="OverlookLeaveSeconds"/> off it — so stepping
/// across the rim does not flip the music; the overview and the telescope change at once (both are deliberate).
/// <para>Why it exists: the choice used to live in PlanetWorld.Music.cs, testable only by the in-app music journey's
/// waits (164 s); here it is a plain state machine the app calls and the Core tests drive frame by frame.</para>
/// <para>What a change must preserve: a change is reported only when the experience changes (the app takes the audio lock
/// for each); travel (a launch, a departure's run, a hop) ends the overlook at once — the flight's music is the world's
/// build and drop (AD-52); the telescope's view is the overlook's piece (DJ: keep the Lydian telescope music).</para>
/// </summary>
public sealed class MusicActivity
{
    /// <summary>Seconds the overlook must hold before its music comes in (a glance from its rim does not start it).</summary>
    public const double OverlookEnterSeconds = 0.4;

    /// <summary>Seconds off the overlook before the world's music comes back (a step back from the rim keeps the piece going).</summary>
    public const double OverlookLeaveSeconds = 1.2;

    /// <summary>One frame as the app sees it.</summary>
    /// <param name="Folder">The world you are on (its folder id): visits count per folder.</param>
    /// <param name="InOverview">The overview (T) is open.</param>
    /// <param name="OnOverlook">Standing on the overlook's platform, or its view held (B).</param>
    /// <param name="AtTelescope">Looking through the telescope (AD-85): the overlook's piece at once.</param>
    /// <param name="Travelling">A launch, a departure's run, a flight or a hop is under way.</param>
    public readonly record struct Moment(int Folder, bool InOverview = false, bool OnOverlook = false, bool AtTelescope = false, bool Travelling = false);

    private double _onSeconds, _offSeconds;
    private readonly Dictionary<(int Folder, MusicExperience Experience), int> _visits = [];

    /// <summary>The experience last reported (<see cref="MusicExperience.World"/> at first).</summary>
    public MusicExperience Current { get; private set; } = MusicExperience.World;

    /// <summary>
    /// One frame: the experience and its visit count when it changes (the app then calls <see cref="SkySynth.SetExperience"/>),
    /// else null. Back to <see cref="MusicExperience.World"/> the visit count is 1 (the synth resumes the place's own music).
    /// </summary>
    public (MusicExperience Experience, int Visits)? Update(TimeSpan dt, Moment m)
    {
        var onPlatform = (m.OnOverlook || m.AtTelescope) && !m.Travelling;
        if (onPlatform)
        {
            _onSeconds += dt.TotalSeconds;
            _offSeconds = 0;
        }
        else
        {
            _offSeconds += dt.TotalSeconds;
            _onSeconds = 0;
        }

        var wanted = m.InOverview ? MusicExperience.Overview
            : m.AtTelescope && !m.Travelling ? MusicExperience.Overlook
            : Current == MusicExperience.Overlook ? (m.Travelling || _offSeconds >= OverlookLeaveSeconds ? MusicExperience.World : MusicExperience.Overlook)
            : _onSeconds >= OverlookEnterSeconds - 1e-9 ? MusicExperience.Overlook : MusicExperience.World;
        if (wanted == Current) return null;
        Current = wanted;
        var visits = 1;
        if (wanted != MusicExperience.World)
        {
            var key = (m.Folder, wanted);
            visits = _visits.GetValueOrDefault(key) + 1;
            _visits[key] = visits;
        }

        return (wanted, visits);
    }

    /// <summary>
    /// The piece an activity plays at a place (AD-79's first question, AD-85's answer): the overlook and the telescope the
    /// Lydian overlook; the overview Star Chart Dorian; a world its own arrangement (null: <see cref="SkySynth.IdentityOf"/>).
    /// </summary>
    public static ExperiencePiece? PieceFor(MusicExperience activity, PlanetTheme theme, WorldCharacter place) => activity switch
    {
        MusicExperience.Overlook => SkySynth.PieceFor(MusicIdentity.Overlook, theme, place),
        MusicExperience.Overview => SkySynth.PieceFor(MusicIdentity.Overview, theme, place),
        _ => null,
    };
}
