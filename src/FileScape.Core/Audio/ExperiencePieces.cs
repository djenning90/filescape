namespace FileScape.Core.Audio;

/// <summary>A note of an <see cref="ExperiencePiece"/> on the piece's sixteenth-note grid (AD-79): its step in the 16-bar cycle and the note itself.</summary>
public readonly record struct StepNote(int Step, SketchNote Note);

/// <summary>
/// A written piece the game plays for an experience rather than a place (AD-79, PLANET-98): the overlook's Lydian piece,
/// the overview's star chart. It is one 16-bar cycle (<see cref="SkySynth.CycleBars"/>) at a fixed tempo, written in D (the
/// sketches' <see cref="ModeSketches.TonicMidi"/>) and transposed by the deck into the world's key, in one or more
/// variations (the first as written, the next for a revisit or the next time round), with the section starts where a
/// return may pick it up. Built once from <see cref="SketchScore"/>s, so the same notes render offline (samples, tests)
/// and in the game. Immutable.
/// <para>What a change must preserve: every note starts on a whole sixteenth inside the cycle (the deck plays a piece one
/// sixteenth at a time); every variation has the same length and sections, so a return lands on a phrase in any of them.</para>
/// </summary>
public sealed class ExperiencePiece
{
    private readonly StepNote[][] _steps;

    /// <summary>Builds a piece from its variations' scores (each 16 bars at <paramref name="beatsPerMinute"/>).</summary>
    public ExperiencePiece(string name, double beatsPerMinute, DiatonicMode mode, IReadOnlyList<SketchScore> variations, IReadOnlyList<int> sectionStartsSixteenths)
    {
        ArgumentOutOfRangeException.ThrowIfZero(variations.Count);
        Name = name;
        BeatsPerMinute = beatsPerMinute;
        Mode = mode;
        Variations = variations;
        SectionStarts = sectionStartsSixteenths;
        var sixteenth = 60.0 / (beatsPerMinute * 4.0);
        _steps = new StepNote[variations.Count][];
        for (var v = 0; v < variations.Count; v++)
        {
            var list = new List<StepNote>(variations[v].Notes.Count);
            foreach (var n in variations[v].Notes)
            {
                var exact = n.Start.TotalSeconds / sixteenth;
                var step = (int)Math.Round(exact);
                if (Math.Abs(exact - step) > 1e-3 || step < 0 || step >= CycleSixteenths)
                {
                    throw new InvalidOperationException($"{name}: a note at {exact:0.###} sixteenths is off the grid or outside the {SkySynth.CycleBars}-bar cycle");
                }

                list.Add(new StepNote(step, n));
            }

            _steps[v] = [.. list.OrderBy(s => s.Step)];
        }
    }

    /// <summary>Sixteenths in the piece's cycle (16 bars of 4/4).</summary>
    public const int CycleSixteenths = SkySynth.CycleBars * 16;

    /// <summary>A short name for logs and files ("overlook", "star-chart-ionian").</summary>
    public string Name { get; }

    /// <summary>The tempo it is written at, quarter notes per minute (the deck plays it at this tempo).</summary>
    public double BeatsPerMinute { get; }

    /// <summary>The mode it is written in.</summary>
    public DiatonicMode Mode { get; }

    /// <summary>The variations as scores (in D, for offline renders): [0] as written, then the others in turn.</summary>
    public IReadOnlyList<SketchScore> Variations { get; }

    /// <summary>Where its sections start, sixteenths into the cycle (0 first): where a return picks it up.</summary>
    public IReadOnlyList<int> SectionStarts { get; }

    /// <summary>The variation the cycle numbered <paramref name="cycle"/> (from 0) plays: they take turns.</summary>
    public int VariationOf(long cycle) => (int)(((cycle % Variations.Count) + Variations.Count) % Variations.Count);

    /// <summary>The notes of a variation in step order.</summary>
    public ReadOnlySpan<StepNote> Steps(int variation) => _steps[variation];

    /// <summary>The first section start at or after <paramref name="position"/> (sixteenths into the music, any cycle).</summary>
    public long NextSectionStart(long position)
    {
        var cycle = Math.Max(0, position) / CycleSixteenths;
        var inCycle = Math.Max(0, position) - cycle * CycleSixteenths;
        foreach (var s in SectionStarts)
        {
            if (s >= inCycle) return cycle * CycleSixteenths + s;
        }

        return (cycle + 1) * CycleSixteenths;
    }
}

/// <summary>
/// The pieces the game plays for experiences (AD-79): the overlook (and the telescope on it) has the Lydian overlook DJ
/// heard in the mode sketches; the overview has the star chart, Ionian, or Dorian when you look out from a minor place.
/// <see cref="SkySynth.SetExperience"/> chooses between them; <see cref="SkySynth"/> plays them on a deck like any world.
/// </summary>
public static class ExperiencePieces
{
    /// <summary>The overlook: <see cref="ModeSketches.LydianOverlook"/> as written and its second light; sections intro, theme, answer, close.</summary>
    public static ExperiencePiece Overlook { get; } = new("overlook", ModeSketches.OverlookBpm, DiatonicMode.Lydian,
        [ModeSketches.LydianOverlook(0), ModeSketches.LydianOverlook(1)], [0, 32, 160, 224]);

    private static readonly ExperiencePiece StarChartIonian = new("star-chart-ionian", ModeSketches.StarChartBpm, DiatonicMode.Ionian,
        [ModeSketches.StarChart(DiatonicMode.Ionian, 0), ModeSketches.StarChart(DiatonicMode.Ionian, 1)], [0, 32, 96, 160, 224]);

    private static readonly ExperiencePiece StarChartDorian = new("star-chart-dorian", ModeSketches.StarChartBpm, DiatonicMode.Dorian,
        [ModeSketches.StarChart(DiatonicMode.Dorian, 0), ModeSketches.StarChart(DiatonicMode.Dorian, 1)], [0, 32, 96, 160, 224]);

    /// <summary>The overview's star chart in <paramref name="mode"/> (Dorian, or Ionian for any other mode).</summary>
    public static ExperiencePiece StarChart(DiatonicMode mode) => mode == DiatonicMode.Dorian ? StarChartDorian : StarChartIonian;
}
