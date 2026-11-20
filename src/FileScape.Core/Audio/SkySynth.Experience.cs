using FileScape.Core.Planets;

namespace FileScape.Core.Audio;

/// <summary>
/// What the player is doing, for the music (AD-79): on a world (walking, jumping, flying between worlds), standing on the
/// overlook (or looking out from it, B, and through its telescope), or in the overview. The world's own music plays for
/// <see cref="World"/>; the others have their own pieces (<see cref="ExperiencePieces"/>), in the world's key.
/// </summary>
public enum MusicExperience
{
    /// <summary>The world's own music (its character's arrangement, AD-49/52/60/66): unchanged by AD-79.</summary>
    World,

    /// <summary>The overlook and its telescope: the Lydian overlook.</summary>
    Overlook,

    /// <summary>The overview (T): the star chart.</summary>
    Overview,
}

// AD-79 (PLANET-97, PLANET-98): the variation scheme's three questions, asked in order, and the minimum dwell.
//   1. Activity picks the piece: MusicExperience (World → the place's arrangement as before; Overlook → the Lydian overlook;
//      Overview → the star chart). A change of activity is heard at once (the next beat), never held by the dwell.
//   2. Place picks the key and the mode family: the world's theme gives the root (the pieces are transposed into it); the
//      overlook is always Lydian; the overview is Ionian, or Dorian where the place's own key is Dorian already.
//   3. Visits pick the variation: as for the worlds (AD-49), the visits count starts a forgotten place on a later cycle and
//      a remembered one resumes where it was (for a piece, at its next section), and the pieces' variations take turns by cycle.
//   Minimum dwell: a world's music, once begun, keeps playing for at least MinimumDwell and then to its next two-bar
//   phrase boundary before another world replaces it, so quick hops between folders hear phrases, not fragments. Hazard
//   worlds and changes of activity are never held.
// The experience pieces play on a deck like any world (cross-fades, memory, the effects' grid in their tempo); their notes
// are the sketches' (SkySynth.Sketches.cs), scheduled a sixteenth at a time like every score.
public sealed partial class SkySynth
{
    /// <summary>
    /// The phrase the dwell completes, bars of the music playing (its own bars: in a half-time arrangement the theme's): two
    /// bars, the FileScape theme's motif.
    /// </summary>
    public const int DwellPhraseBars = 2;

    /// <summary>
    /// The minimum dwell the game uses (<see cref="MinimumDwell"/>, set by the app): 3.5 s, so every world's music plays at
    /// least its first two-bar phrase whatever its tempo (two bars last 3.8 s at 126 BPM, 5.1 s at the hub's 94, 6.2 s for the
    /// orchestra's half-time theme). A quick hop then waits at most ≈ 6 s for the new world's music.
    /// </summary>
    public static readonly TimeSpan GameMinimumDwell = TimeSpan.FromSeconds(3.5);

    /// <summary>
    /// How long a world's music plays, at least, before another world replaces it (AD-79): a change requested sooner waits
    /// until this much has played and then for the music's next <see cref="DwellPhraseBars"/>-bar boundary (until then a
    /// further change only retargets it, and a change back cancels it). Zero (the default, as before AD-79) changes on the
    /// next beat. Never applies to a hazard world (either way), to a change of activity (<see cref="SetExperience"/>), or to
    /// the first change after the synth starts.
    /// </summary>
    public TimeSpan MinimumDwell { get; set; } = TimeSpan.Zero;

    /// <summary>The current activity (<see cref="SetExperience"/>).</summary>
    public MusicExperience Experience => _experience;

    /// <summary>The place the last <see cref="SetWorld(PlanetTheme, WorldCharacter, int, MusicIdentity)"/> named: its music plays for <see cref="MusicExperience.World"/>.</summary>
    public (PlanetTheme Theme, WorldCharacter Character, int Visits, MusicIdentity Score) Place => _place;

    /// <summary>A world change is waiting for the dwell (the music playing now is the previous world's; the next starts at its phrase boundary).</summary>
    public bool ChangeHeld => _decks[_current] is { Deferred: true } d && d.Origin > _frame;

    /// <summary>How long the music playing now has played since its first downbeat (0 before any), for probes and tests.</summary>
    public TimeSpan PlayingFor
    {
        get
        {
            var d = ClockAt(_frame);
            return _frame <= d.Origin ? TimeSpan.Zero : TimeSpan.FromSeconds((_frame - d.Origin) / (double)_rate);
        }
    }

    /// <summary>The identity of the music sounding now (the previous world's while a change waits for its downbeat).</summary>
    public MusicIdentity PlayingIdentity => ClockAt(_frame).Identity;

    /// <summary>The key of the music sounding now (<see cref="Key"/> is the current world's, which a held change has not started yet).</summary>
    public MusicalKey PlayingKey => ToKey(ClockAt(_frame).Profile);

    /// <summary>The theme (key) of the music sounding now.</summary>
    public PlanetTheme PlayingTheme => ClockAt(_frame).Theme;

    /// <summary>The last <see cref="Arrive"/>: the frame its chord sounds and the key's root it sounds in (−1, −1 before any), for tests.</summary>
    public (long Frame, int RootMidi) LastArrival { get; private set; } = (-1, -1);

    private MusicExperience _experience = MusicExperience.World;
    private (PlanetTheme Theme, WorldCharacter Character, int Visits, MusicIdentity Score) _place;

    /// <summary>
    /// Changes the activity (AD-79): the overlook and the overview play their pieces in the key of the place (the last
    /// <see cref="SetWorld(PlanetTheme, WorldCharacter, int)"/>); back to <see cref="MusicExperience.World"/> the place's music
    /// returns, resuming where it was. <paramref name="visits"/> counts how often this experience has begun at this place (1
    /// the first time): a later visit starts the piece's next variation. Heard on the next beat (never held by the dwell).
    /// The same experience again does nothing.
    /// </summary>
    public void SetExperience(MusicExperience experience, int visits = 1)
    {
        if (experience == _experience) return;
        _experience = experience;
        var (theme, character, placeVisits, score) = _place;
        var id = experience switch
        {
            MusicExperience.Overlook => MusicIdentity.Overlook,
            MusicExperience.Overview => MusicIdentity.Overview,
            _ => score,
        };
        ChangeMusic(theme, character, experience == MusicExperience.World ? placeVisits : visits, id, experienceChange: true);
    }

    /// <summary>The identities a place can have (every one but the experiences' pieces): the worlds' own music, AD-49 to AD-66.</summary>
    public static IReadOnlyList<MusicIdentity> WorldIdentities { get; } = [.. Enum.GetValues<MusicIdentity>().Where(i => !IsExperience(i))];

    /// <summary>The identities that play an experience's piece rather than a place's music.</summary>
    public static bool IsExperience(MusicIdentity id) => id is MusicIdentity.Overlook or MusicIdentity.Overview;

    /// <summary>
    /// The mode an identity is heard in at a place, as AD-79's scheme picks it for the experience pieces: the overlook Lydian;
    /// the overview Dorian where the place's own key (its identity's, <see cref="KeyFor(PlanetTheme, WorldCharacter)"/>) is
    /// Dorian already (the WSL root, the deep woods, the underglow, a Theatre folder), else Ionian, the mode DJ liked best
    /// (a hazard world's overview is Ionian: the view out is the relief). Locrian and Phrygian are never chosen. For a world identity, Ionian (unused).
    /// </summary>
    public static DiatonicMode ExperienceModeFor(MusicIdentity id, PlanetTheme theme, WorldCharacter place) => id switch
    {
        MusicIdentity.Overlook => DiatonicMode.Lydian,
        MusicIdentity.Overview => KeyProfiles[(int)IdentityOf(place)][(int)theme].Scale.SequenceEqual(Dorian) ? DiatonicMode.Dorian : DiatonicMode.Ionian,
        _ => DiatonicMode.Ionian,
    };

    /// <summary>The piece an experience identity plays at a place (null for a world identity).</summary>
    public static ExperiencePiece? PieceFor(MusicIdentity id, PlanetTheme theme, WorldCharacter place) => id switch
    {
        MusicIdentity.Overlook => ExperiencePieces.Overlook,
        MusicIdentity.Overview => ExperiencePieces.StarChart(ExperienceModeFor(id, theme, place)),
        _ => null,
    };

    /// <summary>A change between these two is heard at once: a hazard world (danger, and the way out of it) or an experience's piece.</summary>
    private static bool BypassesDwell(MusicIdentity from, MusicIdentity to) =>
        IsExperience(from) || IsExperience(to) || from is MusicIdentity.Hazard or MusicIdentity.Abyss || to is MusicIdentity.Hazard or MusicIdentity.Abyss;

    /// <summary>
    /// The step of <paramref name="playing"/> at which a held change starts (AD-79): the first boundary of a
    /// <see cref="DwellPhraseBars"/>-bar phrase of its music at least <see cref="MinimumDwell"/> after its first downbeat; −1
    /// when it has already played that long (the change takes the next beat, as before).
    /// </summary>
    private long DwellEndStep(Deck playing)
    {
        if (MinimumDwell <= TimeSpan.Zero || !playing.Live) return -1;
        var dwellEnd = playing.Origin + (long)Math.Round(MinimumDwell.TotalSeconds * _rate);
        if (_frame >= dwellEnd) return -1;
        const int phrase = DwellPhraseBars * 16;
        var k = Math.Max(playing.NextStep, playing.IntroSteps);
        // Bounded: the phrase boundary is at most one phrase after the dwell's end.
        while (StepFrame(playing, k) < dwellEnd || Mod(k - playing.IntroSteps + playing.StepOffset, phrase) != 0) k++;
        return k;
    }

    /// <summary>Starts a held change on <paramref name="playing"/>'s next beat instead (a hazard world or an experience came in while it waited).</summary>
    private void PullIn(Deck pending, Deck playing)
    {
        var k = playing.NextStep;
        while (Mod(k, playing.StepsPerBeat) != 0) k++;
        var origin = StepFrame(playing, k);
        if (origin >= pending.Origin) return;
        playing.StopFrame = origin;
        pending.Origin = origin;
        pending.Deferred = false;
        _lastSwitchFrame = origin;
    }

    /// <summary>Semitones that bring a piece written on <see cref="ModeSketches.TonicMidi"/> to <paramref name="rootMidi"/>'s pitch class, within −6…+5 (the nearest).</summary>
    private static int TransposeTo(int rootMidi)
    {
        var shift = (int)Mod(rootMidi - ModeSketches.TonicMidi, 12);
        return shift > 5 ? shift - 12 : shift;
    }

    /// <summary>
    /// One sixteenth of an experience's piece (AD-79): every note of the cycle's variation that starts on it, transposed into
    /// the deck's key, into the deck's group. The deck's cycle and position come from its step and resume offset, as for every score.
    /// </summary>
    private void ScoreStep(Deck d, long step, long frame)
    {
        var piece = d.Piece!;
        var abs = step + d.StepOffset;
        var pos = (int)Mod(abs, CycleSixteenths);
        var cycle = (abs - pos) / CycleSixteenths;
        var variation = piece.VariationOf(cycle);
        if (pos == 0 || !d.CycleReady)
        {
            d.ScoreVariation = variation;
            var notes = piece.Steps(variation);
            d.NextNote = 0;
            while (d.NextNote < notes.Length && notes[d.NextNote].Step < pos) d.NextNote++; // resumed mid-cycle
            d.CycleReady = true;
        }

        var steps = piece.Steps(d.ScoreVariation);
        while (d.NextNote < steps.Length && steps[d.NextNote].Step == pos)
        {
            PlaySketchNote(steps[d.NextNote].Note, frame, d.Group, d.ScoreTranspose);
            d.NextNote++;
        }

        d.Bar = pos / 16;
        d.InBar = pos % 16;
    }

    /// <summary>The variation of the piece playing now (−1 when the music is a world's), for probes and tests.</summary>
    public int PieceVariation => ClockAt(_frame) is { Piece: { } piece } d ? d.NextStep == 0 ? piece.VariationOf(d.StepOffset / CycleSixteenths) : d.ScoreVariation : -1;

    /// <summary>
    /// The bar of the music playing now (its cycle's bar, 0–15; −1 in an intro), for probes and tests; on the first downbeat,
    /// before its first step is played, the bar it starts in.
    /// </summary>
    public int PlayingBar => ClockAt(_frame) is var d && d.NextStep == 0 ? (d.IntroSteps > 0 ? -1 : (int)(Mod(d.StepOffset, CycleSixteenths) / 16)) : d.Bar;
}
