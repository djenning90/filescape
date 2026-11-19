namespace FileScape.Core.Audio;

/// <summary>
/// The seven diatonic modes, in the order of the major scale's degrees they start on (Ionian on do, Dorian on re, …,
/// Locrian on ti). PLANET-79 (AD-72): the same FileScape tune rendered in each, so DJ can hear what a mode does to it.
/// The numeric order is the reel's order (Ionian → Locrian); changing it reorders the reel and the tests' tables.
/// </summary>
public enum DiatonicMode
{
    Ionian,
    Dorian,
    Phrygian,
    Lydian,
    Mixolydian,
    Aeolian,
    Locrian,
}

/// <summary>
/// The arithmetic of the diatonic modes (PLANET-79, AD-72): each mode's semitones above its tonic, the pitch of a scale
/// degree in any octave, the quality of the triad built on a degree, and the mode's characteristic degree (the one note
/// that tells it apart from the nearest major or minor scale). Pure and allocation-light; SkySynth's own scales
/// (<c>SkySynth.Music.cs</c>: Lydian, Dorian, …) are separate arrays and are not changed by anything here.
/// <para>
/// Degrees are 0-based scale indices (0 = the tonic, 6 = the seventh, 7 = the tonic an octave up; negative degrees go
/// down), the numbering of <see cref="ThemeNote.Degree"/>. Semitones are relative to the tonic.
/// </para>
/// </summary>
public static class Modes
{
    /// <summary>The major scale's semitones above its tonic (Ionian): every mode is a rotation of it.</summary>
    private static readonly int[] MajorSemitones = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>Notes per octave in a diatonic mode.</summary>
    public const int Length = 7;

    /// <summary>Semitones in an octave.</summary>
    private const int OctaveSemitones = 12;

    /// <summary>All seven modes in reel order (Ionian → Locrian).</summary>
    public static IReadOnlyList<DiatonicMode> All { get; } = Array.AsReadOnly(Enum.GetValues<DiatonicMode>());

    /// <summary>The seven modes from brightest to darkest (each one lowers one more degree than the last).</summary>
    public static IReadOnlyList<DiatonicMode> BrightestFirst { get; } = Array.AsReadOnly(new[]
    {
        DiatonicMode.Lydian, DiatonicMode.Ionian, DiatonicMode.Mixolydian, DiatonicMode.Dorian,
        DiatonicMode.Aeolian, DiatonicMode.Phrygian, DiatonicMode.Locrian,
    });

    /// <summary>The mode's seven semitone offsets above its tonic, ascending from 0 (the major scale rotated to start on the mode's degree).</summary>
    public static int[] Semitones(DiatonicMode mode)
    {
        var start = (int)mode;
        var result = new int[Length];
        for (var i = 0; i < Length; i++)
        {
            var j = start + i;
            result[i] = MajorSemitones[j % Length] + (j >= Length ? OctaveSemitones : 0) - MajorSemitones[start];
        }

        return result;
    }

    /// <summary>Semitones above the tonic of <paramref name="degree"/> (any integer: 7 is the octave, −1 the seventh below).</summary>
    public static int SemitonesOf(DiatonicMode mode, int degree)
    {
        var octave = (int)Math.Floor(degree / (double)Length);
        return Semitones(mode)[degree - octave * Length] + OctaveSemitones * octave;
    }

    /// <summary>The MIDI note of <paramref name="degree"/> in <paramref name="mode"/> on the tonic <paramref name="tonicMidi"/>.</summary>
    public static int Midi(DiatonicMode mode, int tonicMidi, int degree) => tonicMidi + SemitonesOf(mode, degree);

    /// <summary>
    /// The mode's characteristic degree (0-based): the note that differs from the nearest major or minor scale, or for the
    /// two that are themselves major and minor, the note that tells them from their neighbours. Ionian: the major seventh
    /// (the leading tone, against Mixolydian's); Dorian: the major sixth (against Aeolian's); Phrygian: the minor second;
    /// Lydian: the augmented fourth; Mixolydian: the minor seventh; Aeolian: the minor sixth (against Dorian's); Locrian:
    /// the diminished fifth.
    /// </summary>
    public static int CharacteristicDegree(DiatonicMode mode) => mode switch
    {
        DiatonicMode.Ionian => 6,
        DiatonicMode.Dorian => 5,
        DiatonicMode.Phrygian => 1,
        DiatonicMode.Lydian => 3,
        DiatonicMode.Mixolydian => 6,
        DiatonicMode.Aeolian => 5,
        _ => 4,
    };

    /// <summary>Whether the mode's third is minor (Dorian, Phrygian, Aeolian, Locrian) rather than major.</summary>
    public static bool IsMinor(DiatonicMode mode) => SemitonesOf(mode, 2) == 3;

    /// <summary>The quality of the triad built from <paramref name="rootDegree"/>, its third and fifth in the mode.</summary>
    public static TriadQuality TriadOn(DiatonicMode mode, int rootDegree)
    {
        var root = SemitonesOf(mode, rootDegree);
        var third = SemitonesOf(mode, rootDegree + 2) - root;
        var fifth = SemitonesOf(mode, rootDegree + 4) - root;
        return (third, fifth) switch
        {
            (4, 7) => TriadQuality.Major,
            (3, 7) => TriadQuality.Minor,
            (3, 6) => TriadQuality.Diminished,
            _ => TriadQuality.Augmented, // never in a diatonic mode
        };
    }

    /// <summary>A lower-case name for files and logs ("ionian", "dorian", …).</summary>
    public static string Name(DiatonicMode mode) => mode.ToString().ToLowerInvariant();
}

/// <summary>A triad's quality from its third and fifth above the root (<see cref="Modes.TriadOn"/>).</summary>
public enum TriadQuality
{
    Major,
    Minor,
    Diminished,
    Augmented,
}
