namespace FileScape.Core.Audio;

/// <summary>
/// A sound in a written sketch (PLANET-79/80, AD-72): one of SkySynth's own instruments (the leads of AD-49/AD-52, a
/// pad, basses, the drums and effects), played by <see cref="SkySynth.RenderSketch"/>. The names say what they sound
/// like; the voices themselves are SkySynth's (<c>LeadSpec</c>, <c>Kick</c>, …), so a sketch sounds like FileScape.
/// </summary>
public enum SketchInstrument
{
    /// <summary>The hub's FM bell lead (the theme as most people first hear it).</summary>
    Bell,

    /// <summary>The Windows volume's glassy bell.</summary>
    Glass,

    /// <summary>The outskirts' marimba (no sustain).</summary>
    Marimba,

    /// <summary>A soft square pluck.</summary>
    Pluck,

    /// <summary>The orchestra's flute (sine, breath, vibrato).</summary>
    Flute,

    /// <summary>The orchestra's clarinet.</summary>
    Clarinet,

    /// <summary>The orchestra's horns (their filter opens as the note swells).</summary>
    Horn,

    /// <summary>The orchestra's first violins.</summary>
    Violins,

    /// <summary>The orchestra's cellos.</summary>
    Cello,

    /// <summary>The festival's supersaw (two stacks spread wide and a bell an octave up).</summary>
    SuperSaw,

    /// <summary>The hazard world's twinned, wobbling bell.</summary>
    Unstable,

    /// <summary>A harp-like plucked bell with a long ring.</summary>
    Harp,

    /// <summary>A soft triangle pad voice (one note of a chord: a chord is several).</summary>
    Pad,

    /// <summary>A slow-swelling string-section pad voice.</summary>
    StringPad,

    /// <summary>A round bass: a triangle with a sine an octave below.</summary>
    Bass,

    /// <summary>A short filtered saw bass (driving eighths).</summary>
    SawBass,

    /// <summary>A sub-bass boom that falls in pitch (a launch's impact).</summary>
    SubBoom,

    /// <summary>The kit's kick.</summary>
    Kick,

    /// <summary>The kick with a long sub tail (a drop or a launch).</summary>
    BigKick,

    /// <summary>The kit's snare.</summary>
    Snare,

    /// <summary>A closed hi-hat.</summary>
    Hat,

    /// <summary>An open hi-hat.</summary>
    OpenHat,

    /// <summary>A soft crash.</summary>
    Crash,

    /// <summary>Rising filtered noise over the note's length (a wind-up), peaking at its end.</summary>
    Riser,

    /// <summary>A timpani stroke on the note's pitch.</summary>
    Timpani,

    /// <summary>The aurora pad (AD-79): three detuned saws into a resonant filter that opens over ≈ 2 s, like a sky.</summary>
    AuroraPad,

    /// <summary>The aurora lead (AD-79): a soft, brassy analog lead whose filter blooms with each note; louder is brighter.</summary>
    AuroraLead,

    /// <summary>The aurora bass (AD-79): one saw with a short resonant pluck.</summary>
    AuroraBass,
}

/// <summary>What a sketch note is for: the melody (the tune whose mode is being heard), the bed under it, or the kit.</summary>
public enum SketchPart
{
    Melody,
    Counter,
    Chord,
    Bass,
    Arpeggio,

    /// <summary>A single note that states the mode's characteristic degree on its own (the reel's colour bell).</summary>
    Colour,
    Drums,
    Effect,
}

/// <summary>
/// One note of a sketch: when it starts and how long it is held (from the sketch's start), the instrument, its MIDI
/// pitch (ignored by unpitched drums and effects), velocity 0–1 and pan −1 (left) … +1.
/// </summary>
public readonly record struct SketchNote(TimeSpan Start, TimeSpan Length, SketchPart Part, SketchInstrument Instrument, int MidiNote, float Velocity, float Pan);

/// <summary>A labelled moment in a sketch (a section's start: "launch", "Dorian", …), for the README and the logs.</summary>
public readonly record struct SketchMarker(TimeSpan At, string Label);

/// <summary>
/// A written piece of music for offline listening (PLANET-79/80, AD-72): its notes in time order, labelled moments, and
/// the length to render (the last note's ring included). Built by <see cref="SketchWriter"/>, rendered by
/// <see cref="SkySynth.RenderSketch"/>. Immutable once built.
/// </summary>
public sealed class SketchScore(string name, IReadOnlyList<SketchNote> notes, IReadOnlyList<SketchMarker> markers, TimeSpan duration)
{
    /// <summary>A short file-friendly name ("modes-reel", "launcher-major", …).</summary>
    public string Name { get; } = name;

    /// <summary>Every note, ordered by start.</summary>
    public IReadOnlyList<SketchNote> Notes { get; } = notes;

    /// <summary>The sections' starts, in order.</summary>
    public IReadOnlyList<SketchMarker> Markers { get; } = markers;

    /// <summary>How long the render lasts: the last note's end plus its ring.</summary>
    public TimeSpan Duration { get; } = duration;
}

/// <summary>
/// Writes a <see cref="SketchScore"/> bar by bar in musical time: a cursor (the current section's first downbeat), a
/// tempo, and a key (a <see cref="DiatonicMode"/> on a tonic), so a note is placed by bar and sixteenth and pitched by
/// scale degree. Changing the mode, tempo or tonic between sections is how a theme returns in another mode. Times are
/// rounded to 100 ns ticks (<see cref="Units.Durations.FromSeconds"/>); a sketch is deterministic.
/// </summary>
public sealed class SketchWriter
{
    /// <summary>Sixteenths in a 4/4 bar.</summary>
    public const int BarSixteenths = 16;

    private readonly List<SketchNote> _notes = [];
    private readonly List<SketchMarker> _markers = [];
    private double _cursorSeconds;

    /// <summary>Starts a sketch at <paramref name="beatsPerMinute"/> in <paramref name="mode"/> on <paramref name="tonicMidi"/>.</summary>
    public SketchWriter(double beatsPerMinute, DiatonicMode mode, int tonicMidi)
    {
        BeatsPerMinute = beatsPerMinute;
        Mode = mode;
        TonicMidi = tonicMidi;
    }

    /// <summary>Quarter notes per minute from the cursor on.</summary>
    public double BeatsPerMinute { get; set; }

    /// <summary>The mode degrees are pitched in.</summary>
    public DiatonicMode Mode { get; set; }

    /// <summary>The tonic (degree 0) as a MIDI note.</summary>
    public int TonicMidi { get; set; }

    /// <summary>One sixteenth at the current tempo, seconds.</summary>
    public double SixteenthSeconds => 60.0 / (BeatsPerMinute * 4.0);

    /// <summary>The cursor: where bar 0 of the current section starts.</summary>
    public TimeSpan Cursor => Units.Durations.FromSeconds(_cursorSeconds);

    /// <summary>The MIDI note of <paramref name="degree"/> in the current key, <paramref name="octaves"/> octaves from the tonic's.</summary>
    public int Pitch(int degree, int octaves = 0) => Modes.Midi(Mode, TonicMidi, degree) + 12 * octaves;

    /// <summary>Adds a note at MIDI <paramref name="midi"/>, <paramref name="startSixteenths"/> after the cursor, held <paramref name="lengthSixteenths"/>.</summary>
    public void Note(SketchPart part, SketchInstrument instrument, double startSixteenths, double lengthSixteenths, int midi, float velocity, float pan = 0f)
    {
        var start = Units.Durations.FromSeconds(_cursorSeconds + startSixteenths * SixteenthSeconds);
        var length = Units.Durations.FromSeconds(lengthSixteenths * SixteenthSeconds);
        _notes.Add(new SketchNote(start, length, part, instrument, midi, velocity, pan));
    }

    /// <summary>Adds a note on scale degree <paramref name="degree"/> (0-based, any octave) of the current key.</summary>
    public void Degree(SketchPart part, SketchInstrument instrument, double startSixteenths, double lengthSixteenths, int degree, int octaves, float velocity, float pan = 0f) =>
        Note(part, instrument, startSixteenths, lengthSixteenths, Pitch(degree, octaves), velocity, pan);

    /// <summary>Adds an unpitched hit (a drum or an effect).</summary>
    public void Hit(SketchInstrument instrument, double startSixteenths, float velocity, double lengthSixteenths = 1, float pan = 0f) =>
        Note(instrument is SketchInstrument.Riser or SketchInstrument.Crash ? SketchPart.Effect : SketchPart.Drums, instrument, startSixteenths, lengthSixteenths, 0, velocity, pan);

    /// <summary>Labels the moment <paramref name="startSixteenths"/> after the cursor.</summary>
    public void Mark(string label, double startSixteenths = 0) => _markers.Add(new SketchMarker(Units.Durations.FromSeconds(_cursorSeconds + startSixteenths * SixteenthSeconds), label));

    /// <summary>Moves the cursor on by <paramref name="bars"/> bars at the current tempo.</summary>
    public void AdvanceBars(double bars) => _cursorSeconds += bars * BarSixteenths * SixteenthSeconds;

    /// <summary>Moves the cursor on by <paramref name="seconds"/> (a gap between pieces).</summary>
    public void AdvanceSeconds(double seconds) => _cursorSeconds += seconds;

    /// <summary>The score so far; it lasts until the cursor plus <paramref name="ringSeconds"/>, or the last note's end plus that ring if later.</summary>
    public SketchScore Build(string name, double ringSeconds)
    {
        var notes = _notes.OrderBy(n => n.Start).ToArray();
        var end = notes.Length == 0 ? TimeSpan.Zero : notes.Max(n => n.Start + n.Length);
        var duration = Units.Durations.FromSeconds(Math.Max(end.TotalSeconds, _cursorSeconds) + ringSeconds);
        return new SketchScore(name, notes, _markers.ToArray(), duration);
    }
}
