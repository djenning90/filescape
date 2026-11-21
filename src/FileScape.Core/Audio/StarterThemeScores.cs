namespace FileScape.Core.Audio;

/// <summary>
/// The seventh pass's alternatives to the Starter Sky's music (PLANET-169..173, AD-95): three written pieces for the hub
/// identity (the Starter Sky and This computer), each one 16-bar cycle at the hub's tempo in four variations, so a minute
/// spent there does not hear the same 16 bars twice and a revisit picks up a different part. The classic hub music
/// (AD-49's generated theme, music box and journeys) stays in <see cref="SkySynth"/> untouched as
/// <see cref="StarterTheme.Classic"/>; these are played by a deck like the overview's star chart (<see cref="ExperiencePiece"/>).
/// <list type="bullet">
/// <item><b>Revised</b> (Ionian): the FileScape theme kept, re-harmonised (major sevenths, a soft dominant, an open close)
/// and passed between flute, the old bell, violins and a flute/clarinet hocket, phrased differently each time; the music
/// box's random walk is replaced by a composed answering phrase, and half of one cycle in four is a breath with no tune.</item>
/// <item><b>Aurora drift</b> (Mixolydian): slow two-bar chords over a held tonic, the aurora pad and bass, a syncopated
/// pluck ostinato (3 + 3 + 4 + 3 + 3 sixteenths), the theme's head as a high glass call, and a long-note aurora lead whose
/// phrases change from cycle to cycle; the flattened seventh (♭VII → I) is its signature.</item>
/// <item><b>Homecoming</b> (Ionian with borrowed chords): a new, long-breathed melody grown from the theme's rising sixth,
/// over strings, harp and cello; its second half darkens through borrowed iv and ♭VI before coming home, and one cycle
/// in four is an interlude for harp and glass.</item>
/// </list>
/// Written on D (<see cref="ModeSketches.TonicMidi"/>) and transposed by the deck into the place's key (B♭ on the Starter Sky).
/// <para>What a change must preserve: every note starts on a whole sixteenth inside the 16-bar cycle (the deck's contract,
/// checked by <see cref="ExperiencePiece"/>); every variation has the same length and the sections in
/// <see cref="StarterThemes"/>; the tempo equals the hub's (<see cref="SkySynth.TempoFor"/>), because a hub deck keeps the
/// hub's clock. The melodies are original (the theme's own, or grown from it): never quote other music.</para>
/// </summary>
public static class StarterThemeScores
{
    /// <summary>The pieces' tempo, beats per minute: the hub's (AD-49), so the effects' grid and the dwell keep the hub's timing; 16 bars last 40.9 s.</summary>
    public const double Bpm = 94;

    /// <summary>How long a variation's score rings after its last bar in an offline render, seconds (the pads' release and the reverb).</summary>
    private const double RingSeconds = 3;

    /// <summary>Variations per piece: four cycles (2 min 43 s at <see cref="Bpm"/>) before any 16 bars come round again.</summary>
    public const int Variations = 4;

    private const int Bar = SketchWriter.BarSixteenths;

    // ---------------------------------------------------------------- notation

    /// <summary>A pitch as a scale degree of the piece's mode (0 = the tonic D4, 7 = D5, −7 = D3) and a chromatic alteration in semitones (−1: flattened, a borrowed note).</summary>
    private readonly record struct P(int Degree, int Alter = 0)
    {
        public static implicit operator P(int degree) => new(degree);
    }

    /// <summary>A flattened degree: the borrowed notes of Homecoming's iv and ♭VI.</summary>
    private static P Flat(int degree) => new(degree, -1);

    /// <summary>A bar's harmony: the bass note and the pad's voicing (each a degree); optionally a second chord from the bar's half (<paramref name="Then"/>).</summary>
    private sealed record Harmony(P Bass, P[] Pad, Harmony? Then = null);

    /// <summary>Whether two bars have the same harmony (bass and voicing): a held chord rather than a new one.</summary>
    private static bool Same(Harmony a, Harmony b) => a.Bass == b.Bass && a.Pad.SequenceEqual(b.Pad) && a.Then is null && b.Then is null;

    /// <summary>The MIDI note of <paramref name="p"/> in the writer's key, <paramref name="octaves"/> octaves up.</summary>
    private static int Midi(SketchWriter w, P p, int octaves = 0) => w.Pitch(p.Degree, octaves) + p.Alter;

    /// <summary>Plays a bar's melodic line (start, length, pitch; sixteenths from the bar) on <paramref name="instrument"/>, shifted <paramref name="shift"/> degrees and <paramref name="octaves"/> octaves.</summary>
    private static void Line(SketchWriter w, SketchPart part, SketchInstrument instrument, int bar, (int Start, int Length, P Pitch)[] tones, int octaves, float velocity, float pan, int shift = 0)
    {
        foreach (var (start, length, pitch) in tones)
        {
            // A note on the bar's downbeat leans a little (phrasing), the rest as given.
            var v = start == 0 ? velocity * DownbeatLean : velocity;
            w.Note(part, instrument, bar * Bar + start, length, Midi(w, pitch with { Degree = pitch.Degree + shift }, octaves), Math.Min(1f, v), pan);
        }
    }

    /// <summary>How much louder a melody note on the downbeat is than the others (linear): enough to shape a phrase, not to accent it.</summary>
    private const float DownbeatLean = 1.08f;

    /// <summary>Holds a chord's voicing on <paramref name="instrument"/> for <paramref name="length"/> sixteenths, spread left to right.</summary>
    private static void Chord(SketchWriter w, SketchInstrument instrument, double at, double length, P[] voicing, float velocity, int octaves = 0)
    {
        for (var i = 0; i < voicing.Length; i++)
        {
            var pan = voicing.Length == 1 ? 0f : -0.4f + 0.8f * i / (voicing.Length - 1);
            w.Note(SketchPart.Chord, instrument, at, length, Midi(w, voicing[i], octaves), velocity, pan);
        }
    }

    /// <summary>
    /// The harp's flowing figure over a chord for one bar: six notes on the 3 + 3 + 2 + 3 + 3 + 2 grouping (sixteenths 0 3 6 8
    /// 11 14), up the voicing and back, so it moves without the square eighths of a music box.
    /// </summary>
    private static void HarpFigure(SketchWriter w, double at, P[] voicing, float velocity, int octaves = 0)
    {
        ReadOnlySpan<int> steps = [0, 3, 6, 8, 11, 14];
        ReadOnlySpan<int> rungs = [0, 1, 2, 3, 2, 1];
        for (var i = 0; i < steps.Length; i++)
        {
            var p = voicing[rungs[i] % voicing.Length];
            var lean = i is 0 or 3 ? 1f : 0.8f;
            w.Note(SketchPart.Arpeggio, SketchInstrument.Harp, at + steps[i], 3, Midi(w, p, octaves), velocity * lean, 0.35f);
        }
    }

    // ---------------------------------------------------------------- Revised (Ionian): the FileScape theme, re-harmonised

    /// <summary>
    /// The revised hub's harmony, a chord per bar (D Ionian): under the theme (bars 0–7) I(add9) IVmaj7 vi7 IV/V I/3 IV, ii7
    /// to A sus4 in bar 6, I; under the answering phrase (8–15) vi IV ii7 A sus4 iii7 vi IVmaj7 IV/V, ending on the soft
    /// dominant so the cycle leans into its next start. The classic hub used plain triads on I IV V and wandering vi IV ii V.
    /// </summary>
    private static readonly Harmony[] RevisedChords =
    [
        new(-14, [-7, -3, 1]),
        new(-11, [-4, -2, 0]),
        new(-9, [-5, -3, -2, 0]),
        new(-10, [-4, -2, 0]),
        new(-12, [-7, -3, 0]),
        new(-11, [-4, -2, 0]),
        new(-13, [-4, -2, 0, 1], new(-10, [-3, 0, 1])),
        new(-14, [-7, -3, 0, 2]),
        new(-9, [-5, -2, 0]),
        new(-11, [-4, -2, 0]),
        new(-13, [-4, -2, 0, 1]),
        new(-10, [-3, 0, 1]),
        new(-12, [-5, -3, -1, 1]),
        new(-9, [-5, -2, 0]),
        new(-11, [-4, -2, 0, 2]),
        new(-10, [-4, -2, 0]),
    ];

    /// <summary>
    /// The composed answer to the theme (bars 8–15), replacing the music box's seeded random walk: two four-bar phrases
    /// that step down from the sixth and climb back, the second a varied sequence of the first that ends open on E over
    /// the soft dominant.
    /// </summary>
    private static readonly (int, int, P)[][] RevisedAnswer =
    [
        [(0, 6, 5), (6, 2, 4), (8, 4, 2), (12, 4, 4)],
        [(0, 12, 3), (12, 4, 5)],
        [(0, 6, 7), (6, 2, 5), (8, 8, 3)],
        [(0, 16, 1)],
        [(0, 6, 6), (6, 2, 4), (8, 4, 2), (12, 4, 8)],
        [(0, 12, 7), (12, 4, 5)],
        [(0, 4, 4), (4, 4, 5), (8, 8, 9)],
        [(0, 8, 8), (8, 8, 4)],
    ];

    /// <summary>The duet's upper line in its fifth bar (over F#m7): the answer a third up, but ending on F#' (the chord's root) where a third up would be G', a minor ninth over the F#.</summary>
    private static readonly (int, int, P)[] RevisedDuetFifthBar = [(0, 6, 8), (6, 2, 6), (8, 4, 4), (12, 4, 9)];

    /// <summary>The hub's old open question (AD-49: la sol la do' — re'), kept as the last bar of the fourth variation, so the revised hub still ends a round on a question.</summary>
    private static readonly (int, int, P)[] HubQuestion = [(0, 2, 5), (2, 2, 4), (4, 4, 5), (8, 4, 7), (12, 4, 8)];

    /// <summary>
    /// A new consequent for the theme's second half (bars 4–7) in the second variation, on the clarinet: instead of the
    /// raised motif and the cadence again, a falling line from the sixth that comes to rest on the third (F#), not the tonic.
    /// </summary>
    private static readonly (int, int, P)[][] RevisedConsequent =
    [
        [(0, 4, 4), (4, 4, 2), (8, 8, 7)],
        [(0, 6, 5), (6, 2, 4), (8, 8, 3)],
        [(0, 4, 1), (4, 4, 3), (8, 8, 4)],
        [(0, 16, 2)],
    ];

    /// <summary>The theme's bar <paramref name="bar"/> (0–7) as a line, from <see cref="SkySynth.ThemeMelody"/>.</summary>
    private static (int, int, P)[] ThemeBar(int bar) =>
        [.. SkySynth.ThemeMelody.Where(n => n.StartSixteenths / Bar == bar).Select(n => (n.StartSixteenths % Bar, n.LengthSixteenths, (P)n.Degree))];

    /// <summary>
    /// <paramref name="tones"/> phrased late: every note that starts on a beat and lasts a beat or more starts an eighth
    /// later (and ends where it did), so the line leans back against the pulse instead of landing on it.
    /// </summary>
    private static (int, int, P)[] Lazy((int Start, int Length, P Pitch)[] tones) =>
        [.. tones.Select(t => t.Start % 4 == 0 && t.Length >= 4 ? (t.Start + 2, t.Length - 2, t.Pitch) : (t.Start, t.Length, t.Pitch))];

    /// <summary>
    /// The revised hub, variation <paramref name="variation"/> (0–3) of its 16-bar cycle (D Ionian, <see cref="Bpm"/>):
    /// 0 the theme on the flute and the answer on the clarinet; 1 the theme's first half on the old bell, then a new
    /// consequent on the clarinet, then a breath (harp an octave up, the theme's head as a glass call, no tune); 2 the theme
    /// phrased late on the violins with a cello counter-line, the answer as a duet in thirds (the flute's upper line leads,
    /// the clarinet below); 3 the theme as a hocket, bar by bar between flute and clarinet, the answer on the horn with the
    /// violins above, its second phrase starting higher, ending on the hub's open question.
    /// Under all of them a string pad, the harp's figure and a cello bass.
    /// </summary>
    public static SketchScore Revised(int variation)
    {
        var v = Mod(variation, Variations);
        var w = new SketchWriter(Bpm, DiatonicMode.Ionian, ModeSketches.TonicMidi);
        w.Mark(v switch
        {
            0 => "revised 1: the theme (flute), the answer (clarinet)",
            1 => "revised 2: the theme (bell, then clarinet), a breath",
            2 => "revised 3: the theme (violins, cello below), the answer in thirds",
            _ => "revised 4: the theme as a hocket, the answer (horn), the question",
        });
        for (var bar = 0; bar < Bar; bar++)
        {
            var breath = v == 1 && bar >= 8;
            Bed(w, bar, RevisedChords[bar], padVelocity: breath ? 0.6f : 0.8f, harpVelocity: breath ? 0.45f : 0.5f, harpOctaves: breath ? 1 : 0, roundBass: v is 2 or 3 && !breath);
        }

        // The theme (bars 0–7); the hocket leaves out the cadence's last note, so the answer begins after a breath.
        for (var bar = 0; bar < (v == 3 ? 7 : 8); bar++)
        {
            var tones = ThemeBar(bar);
            switch (v)
            {
                case 0: Line(w, SketchPart.Melody, SketchInstrument.Flute, bar, tones, 0, 0.8f, -0.05f); break;
                case 1:
                    if (bar < 4) Line(w, SketchPart.Melody, SketchInstrument.Bell, bar, tones, 0, 0.75f, -0.1f);
                    else Line(w, SketchPart.Melody, SketchInstrument.Clarinet, bar, RevisedConsequent[bar - 4], 0, 0.8f, 0.15f);
                    break;
                case 2:
                    Line(w, SketchPart.Melody, SketchInstrument.Violins, bar, Lazy(tones), 0, 0.8f, -0.1f);
                    // The cello's counter-line: the chord's third, a half note, then its fifth.
                    var h = RevisedChords[bar];
                    w.Note(SketchPart.Counter, SketchInstrument.Cello, bar * Bar, 8, Midi(w, h.Pad[^2], 0), 0.55f, -0.3f);
                    w.Note(SketchPart.Counter, SketchInstrument.Cello, bar * Bar + 8, 8, Midi(w, h.Pad[^1], 0), 0.5f, -0.3f);
                    break;
                default:
                    if (bar % 2 == 0) Line(w, SketchPart.Melody, SketchInstrument.Flute, bar, tones, 0, 0.8f, -0.25f);
                    else Line(w, SketchPart.Melody, SketchInstrument.Clarinet, bar, tones, 0, 0.8f, 0.25f);
                    break;
            }
        }

        // The answer (bars 8–15), or the breath. Rests are written in: the first variation's answer breathes after each
        // phrase (bars 11 and 15), the third's duet ends two bars early, so a long stay is not one unbroken line.
        for (var i = 0; i < 8; i++)
        {
            var bar = 8 + i;
            if ((v == 0 && i is 3 or 7) || (v == 2 && i >= 6)) continue;
            var tones = v == 3 && i == 7 ? HubQuestion : RevisedAnswer[i];
            switch (v)
            {
                case 0: Line(w, SketchPart.Melody, SketchInstrument.Clarinet, bar, tones, 0, 0.8f, 0.15f); break;
                case 1:
                    if (i is 1 or 5) GlassCall(w, bar, 0.4f);
                    break;
                case 2:
                    Line(w, SketchPart.Melody, SketchInstrument.Flute, bar, i == 4 ? RevisedDuetFifthBar : tones, 0, 0.7f, -0.2f, shift: i == 4 ? 0 : 2);
                    Line(w, SketchPart.Counter, SketchInstrument.Clarinet, bar, tones, 0, 0.6f, 0.2f);
                    break;
                default:
                    // The second phrase's first two bars a third higher (the fifth bar as the duet's, ending on F#'), then as written and the question.
                    var lifted = i == 4 ? RevisedDuetFifthBar : tones;
                    var lift = i == 5 ? 2 : 0;
                    Line(w, SketchPart.Melody, SketchInstrument.Horn, bar, lifted, 0, 0.8f, 0.1f, shift: lift);
                    Line(w, SketchPart.Counter, SketchInstrument.Violins, bar, lifted, 1, 0.35f, -0.25f, shift: lift);
                    break;
            }
        }

        w.AdvanceBars(Bar);
        return w.Build($"starter-revised-{v + 1}", RingSeconds);
    }

    /// <summary>The theme's head (mi sol do') high on the glass in the bar's second half: the hub's signature, heard through the breath.</summary>
    private static void GlassCall(SketchWriter w, int bar, float velocity)
    {
        w.Note(SketchPart.Colour, SketchInstrument.Glass, bar * Bar + 8, 2, w.Pitch(2, 1), velocity, -0.35f);
        w.Note(SketchPart.Colour, SketchInstrument.Glass, bar * Bar + 10, 2, w.Pitch(4, 1), velocity, 0f);
        w.Note(SketchPart.Colour, SketchInstrument.Glass, bar * Bar + 12, 8, w.Pitch(7, 1), velocity * 1.1f, 0.35f);
    }

    /// <summary>
    /// One bar of the chamber bed shared by Revised and Homecoming: the string pad on the bar's chord (or chords), the harp's
    /// figure, the cello on the bass an octave up, and with <paramref name="roundBass"/> the round bass under it on the
    /// downbeat and the and of three.
    /// </summary>
    private static void Bed(SketchWriter w, int bar, Harmony h, float padVelocity, float harpVelocity, int harpOctaves, bool roundBass)
    {
        var at = bar * Bar;
        (Harmony Chord, int From, int Length)[] parts = h.Then is { } then ? [(h, 0, 8), (then, 8, 8)] : [(h, 0, 16)];
        foreach (var (part, from, length) in parts)
        {
            Chord(w, SketchInstrument.StringPad, at + from, length, part.Pad, padVelocity);
            w.Note(SketchPart.Bass, SketchInstrument.Cello, at + from, length, Midi(w, part.Bass, 1), 0.5f, -0.25f);
            if (roundBass)
            {
                w.Note(SketchPart.Bass, SketchInstrument.Bass, at + from, Math.Min(6, length), Midi(w, part.Bass), 0.6f);
                if (length == 16) w.Note(SketchPart.Bass, SketchInstrument.Bass, at + 10, 6, Midi(w, part.Bass), 0.45f);
            }
        }

        HarpFigure(w, at, h.Pad, harpVelocity, harpOctaves);
    }

    // ---------------------------------------------------------------- Aurora drift (Mixolydian)

    /// <summary>
    /// Aurora drift's harmony, one chord per two bars (D Mixolydian): I, ♭VII over the held tonic (C over D), IV, I; vi, IV,
    /// ♭VII; then v7 and ♭VII, so each cycle closes ♭VII → I, the mode's flattened-seventh cadence. IV is voiced without its
    /// third (G D E), so the melody's C over it is a suspension, never a rub against a held B.
    /// </summary>
    private static readonly Harmony[] DriftChords =
    [
        new(-14, [-7, -3, 1, 2]),
        new(-14, [-7, -3, 1, 2]),
        new(-14, [-4, -1, 1]),
        new(-14, [-4, -1, 1]),
        new(-11, [-4, 0, 1]),
        new(-11, [-4, 0, 1]),
        new(-14, [-5, -3, 0, 1]),
        new(-14, [-5, -3, 0, 1]),
        new(-9, [-5, -2, 0]),
        new(-9, [-5, -2, 0]),
        new(-11, [-4, 0, 1]),
        new(-11, [-4, 0, 1]),
        new(-15, [-4, -1, 1]),
        new(-15, [-4, -1, 1]),
        new(-10, [-4, -1, 1]),
        new(-15, [-4, -1, 1]),
    ];

    /// <summary>The aurora lead's first phrase (bars 4–7): the theme's head slowed (F# A D'), the flattened seventh falling to the fifth, and home on the tonic.</summary>
    private static readonly (int, int, P)[][] DriftPhraseA =
    [
        [(0, 4, 2), (4, 4, 4), (8, 8, 7)],
        [(0, 6, 6), (6, 2, 5), (8, 8, 4)],
        [(0, 4, 4), (4, 4, 2), (8, 4, 1), (12, 4, 2)],
        [(0, 12, 0)],
    ];

    /// <summary>The second phrase (bars 8–11), over vi and IV: higher, to the ninth, settling on the fifth.</summary>
    private static readonly (int, int, P)[][] DriftPhraseB =
    [
        [(0, 6, 5), (6, 2, 4), (8, 4, 5), (12, 4, 7)],
        [(0, 12, 8), (12, 4, 7)],
        [(0, 8, 6), (8, 4, 5), (12, 4, 3)],
        [(0, 16, 4)],
    ];

    /// <summary>The closing phrase (bars 12–15), over ♭VII, v7, ♭VII: the head again, displaced, and the ♭7 → 1 cadence.</summary>
    private static readonly (int, int, P)[][] DriftPhraseC =
    [
        [(2, 2, 4), (4, 4, 6), (8, 8, 7)],
        [(0, 6, 9), (6, 2, 8), (8, 8, 7)],
        [(0, 4, 8), (4, 4, 7), (8, 8, 4)],
        [(0, 8, 6), (8, 8, 7)],
    ];

    /// <summary>The third variation's own close (bars 12–15): the line climbs to the raised F#' over C (a bright, Lydian-coloured lift) before the same cadence.</summary>
    private static readonly (int, int, P)[][] DriftPhraseLift =
    [
        [(0, 4, 7), (4, 4, 8), (8, 8, 9)],
        [(0, 6, 10), (6, 2, 9), (8, 8, 8)],
        [(0, 4, 7), (4, 4, 6), (8, 8, 4)],
        [(0, 8, 6), (8, 8, 7)],
    ];

    /// <summary>
    /// Aurora drift, variation <paramref name="variation"/> (0–3) of its cycle (D Mixolydian, <see cref="Bpm"/>): 0 the sky
    /// alone — pad, bass and ostinato, the glass call, the lead only in the closing phrase; 1 the whole song on the aurora
    /// lead with a slow pulse (soft hats, a kick every second bar); 2 the first phrase on the lead, the second doubled high
    /// on the glass, and its own lifting close; 3 a dissolve — the first phrase on the glass, the ostinato thinned, no pulse,
    /// the closing phrase quietly on the lead. The aurora pad and bass, and the pluck ostinato on 3 + 3 + 4 + 3 + 3, under all.
    /// </summary>
    public static SketchScore AuroraDrift(int variation)
    {
        var v = Mod(variation, Variations);
        var w = new SketchWriter(Bpm, DiatonicMode.Mixolydian, ModeSketches.TonicMidi);
        w.Mark(v switch
        {
            0 => "aurora drift 1: the sky (pad, bass, ostinato), the glass call",
            1 => "aurora drift 2: the song on the aurora lead, a slow pulse",
            2 => "aurora drift 3: lead and glass, the lifting close",
            _ => "aurora drift 4: the dissolve",
        });
        // The pad swells over a cycle: quieter at its edges, fullest in the middle (velocity also opens the aurora filter).
        ReadOnlySpan<float> padArc = [0.6f, 0.7f, 0.8f, 0.85f, 0.85f, 0.8f, 0.75f, 0.65f];
        for (var bar = 0; bar < Bar; bar++)
        {
            var h = DriftChords[bar];
            var at = bar * Bar;
            if (bar == 0 || !Same(DriftChords[bar - 1], h))
            {
                var hold = 1;
                while (bar + hold < Bar && Same(DriftChords[bar + hold], h)) hold++;
                Chord(w, SketchInstrument.AuroraPad, at, hold * Bar, h.Pad, padArc[bar / 2] * (v == 3 ? 0.85f : 1f));
            }

            // The aurora bass: the root held through the bar's first half, again on the and of three (the octave on even bars, the fifth on odd).
            w.Note(SketchPart.Bass, SketchInstrument.AuroraBass, at, 7, Midi(w, h.Bass), 0.6f);
            w.Note(SketchPart.Bass, SketchInstrument.AuroraBass, at + 10, 6, Midi(w, h.Bass with { Degree = h.Bass.Degree + (bar % 2 == 0 ? 7 : 4) }), 0.45f);
            Ostinato(w, at, h.Pad, v == 3 && bar >= 4, v == 0 && bar < 2);
            if (v is 1 or 2)
            {
                for (var e = 0; e < 4; e++) w.Hit(SketchInstrument.Hat, at + 4 * e + 2, e % 2 == 0 ? 0.16f : 0.2f, pan: 0.25f);
                if (bar % 2 == 0) w.Hit(SketchInstrument.Kick, at, 0.32f);
                if (bar % 4 == 3) w.Hit(SketchInstrument.OpenHat, at + 14, 0.14f);
            }
        }

        if (v is 0 or 3) GlassCall(w, 1, 0.45f);
        if (v == 0) GlassCall(w, 9, 0.35f);
        for (var i = 0; i < 4; i++)
        {
            switch (v)
            {
                case 0:
                    Line(w, SketchPart.Melody, SketchInstrument.AuroraLead, 12 + i, DriftPhraseC[i], 0, 0.6f, -0.05f);
                    break;
                case 1:
                    Line(w, SketchPart.Melody, SketchInstrument.AuroraLead, 4 + i, DriftPhraseA[i], 0, 0.75f, -0.05f);
                    Line(w, SketchPart.Melody, SketchInstrument.AuroraLead, 8 + i, DriftPhraseB[i], 0, 0.8f, -0.05f);
                    Line(w, SketchPart.Melody, SketchInstrument.AuroraLead, 12 + i, DriftPhraseC[i], 0, 0.75f, -0.05f);
                    break;
                case 2:
                    Line(w, SketchPart.Melody, SketchInstrument.AuroraLead, 4 + i, DriftPhraseA[i], 0, 0.75f, -0.05f);
                    Line(w, SketchPart.Melody, SketchInstrument.AuroraLead, 8 + i, DriftPhraseB[i], 0, 0.7f, -0.1f);
                    Line(w, SketchPart.Counter, SketchInstrument.Glass, 8 + i, DriftPhraseB[i], 1, 0.3f, 0.3f);
                    Line(w, SketchPart.Melody, SketchInstrument.AuroraLead, 12 + i, DriftPhraseLift[i], 0, 0.8f, -0.05f);
                    break;
                default:
                    Line(w, SketchPart.Melody, SketchInstrument.Glass, 4 + i, DriftPhraseA[i], 1, 0.35f, 0.2f);
                    Line(w, SketchPart.Melody, SketchInstrument.AuroraLead, 12 + i, DriftPhraseC[i], 0, 0.55f, -0.05f);
                    break;
            }
        }

        w.AdvanceBars(Bar);
        return w.Build($"starter-aurora-drift-{v + 1}", RingSeconds);
    }

    /// <summary>
    /// One bar of Aurora drift's pluck ostinato an octave above the pad: five notes on 3 + 3 + 4 + 3 + 3 sixteenths (0 3 6 10
    /// 13) through the chord (root, fifth, third, the top, the fifth), alternating sides. <paramref name="thin"/> keeps only
    /// the first two (the dissolve); <paramref name="silent"/> rests (the first bars of the first variation).
    /// </summary>
    private static void Ostinato(SketchWriter w, int at, P[] voicing, bool thin, bool silent)
    {
        if (silent) return;
        ReadOnlySpan<int> steps = [0, 3, 6, 10, 13];
        ReadOnlySpan<int> rungs = [0, 2, 1, 3, 2];
        ReadOnlySpan<float> velocity = [0.5f, 0.36f, 0.42f, 0.46f, 0.34f];
        for (var i = 0; i < (thin ? 2 : steps.Length); i++)
        {
            w.Note(SketchPart.Arpeggio, SketchInstrument.Pluck, at + steps[i], 2, Midi(w, voicing[rungs[i] % voicing.Length], 1), velocity[i], i % 2 == 0 ? -0.3f : 0.3f);
        }
    }

    // ---------------------------------------------------------------- Homecoming (Ionian with borrowed chords)

    /// <summary>
    /// Homecoming's harmony, a chord per bar (D Ionian). First half: I(add9) vi7 IVmaj7 V sus4, iii7 vi ii7 IV/V (a soft
    /// dominant that stays open). Second half: I/3 IV, then the borrowed minor iv (G minor), I, the borrowed ♭VI (B♭ major),
    /// iv again, IV/V and I(add9): the darkening that makes the return home sound earned rather than sweet.
    /// </summary>
    private static readonly Harmony[] HomecomingChords =
    [
        new(-14, [-7, -3, 1, 2]),
        new(-9, [-5, -3, -2, 0]),
        new(-11, [-4, -2, 0, 2]),
        new(-10, [-3, 0, 1]),
        new(-12, [-5, -3, -1, 1]),
        new(-9, [-5, -2, 0]),
        new(-13, [-4, -2, 0, 1]),
        new(-10, [-4, -2, 0]),
        new(-12, [-7, -3, 0]),
        new(-11, [-4, -2, 0]),
        new(-11, [-4, Flat(-2), 0]),
        new(-14, [-5, -3, 0]),
        new(Flat(-9), [-7, Flat(-5), Flat(-2)]),
        new(-11, [-4, Flat(-2), 0]),
        new(-10, [-4, -2, 0]),
        new(-14, [-7, -3, 1, 2]),
    ];

    /// <summary>
    /// Homecoming's melody (original), a bar per row. Its cell is the FileScape theme's rising sixth (mi to do') in a dotted
    /// rhythm, F# A D', falling back by step ("a sigh"); the first half sequences it (F# A E') and ends open on E; the second
    /// climbs to G', then takes the borrowed B♭ and F natural over iv and ♭VI and sighs down to the third of the home chord.
    /// </summary>
    private static readonly (int, int, P)[][] HomecomingMelody =
    [
        [(8, 3, 2), (11, 1, 4), (12, 4, 7)],
        [(0, 6, 7), (6, 2, 6), (8, 8, 5)],
        [(0, 3, 4), (3, 1, 5), (4, 8, 9), (12, 4, 8)],
        [(0, 4, 8), (4, 12, 7)],
        [(8, 3, 2), (11, 1, 4), (12, 4, 8)],
        [(0, 6, 7), (6, 2, 6), (8, 4, 5), (12, 4, 4)],
        [(0, 4, 3), (4, 4, 5), (8, 4, 4), (12, 4, 3)],
        [(0, 12, 1)],
        [(0, 3, 4), (3, 1, 5), (4, 8, 7), (12, 4, 9)],
        [(0, 8, 10), (8, 4, 9), (12, 4, 8)],
        [(0, 8, 7), (8, 8, Flat(5))],
        [(0, 12, 4), (12, 4, 2)],
        [(0, 8, 7), (8, 8, Flat(9))],
        [(0, 4, 7), (4, 4, Flat(5)), (8, 8, 4)],
        [(0, 4, 5), (4, 4, 4), (8, 4, 3), (12, 4, 1)],
        [(0, 12, 2)],
    ];

    /// <summary>
    /// The reprise of the fourth variation (bars 4–15; bars 0–3 are the song's): the sequence climbs higher (A C#' F#'), to
    /// the song's peak (A' over the soft dominant), falls back through the borrowed chords as before, and for once resolves
    /// on the tonic. So the last round of the cycle is the song remembered, not repeated.
    /// </summary>
    private static readonly (int, int, P)[][] HomecomingReprise =
    [
        [(8, 3, 4), (11, 1, 6), (12, 4, 9)],
        [(0, 6, 10), (6, 2, 9), (8, 8, 7)],
        [(0, 4, 8), (4, 4, 10), (8, 4, 9), (12, 4, 8)],
        [(0, 10, 11), (10, 6, 8)],
        [(0, 6, 9), (6, 2, 8), (8, 8, 7)],
        [(0, 3, 5), (3, 1, 7), (4, 8, 10), (12, 4, 9)],
        [(0, 8, 7), (8, 8, Flat(5))],
        [(0, 12, 4), (12, 4, 2)],
        [(0, 8, 7), (8, 8, Flat(9))],
        [(0, 4, 7), (4, 4, Flat(5)), (8, 8, 4)],
        [(0, 4, 4), (4, 4, 3), (8, 4, 1), (12, 4, 5)],
        [(0, 16, 7)],
    ];

    /// <summary>The cello's counter-line under the clarinet in the second variation (bars 0–7): half notes moving against the tune (down when it rises).</summary>
    private static readonly (int, int, P)[][] HomecomingCounter =
    [
        [(8, 8, -5)],
        [(0, 8, -2), (8, 8, -3)],
        [(0, 8, 0), (8, 8, -2)],
        [(0, 8, -3), (8, 8, -6)],
        [(0, 8, -5), (8, 8, -1)],
        [(0, 16, -2)],
        [(0, 8, -4), (8, 8, -6)],
        [(0, 16, -2)],
    ];

    /// <summary>The horn's inner line through the darkening (bars 12–15): F (the fifth of B♭), D, B falling to A, D.</summary>
    private static readonly (int, int, P)[][] HomecomingHorn =
    [
        [(0, 16, Flat(2))],
        [(0, 16, 0)],
        [(0, 8, -2), (8, 8, -3)],
        [(0, 16, 0)],
    ];

    // The interlude's velocities (0–1): the pad and harp fuller than under the song and the stars clear, so the interlude
    // rests rather than fades (at 0.65 / 0.55 / 0.35 it measured 4.3 dB under the song's cycle, 2026-11-20).
    private const float InterludePadVelocity = 0.85f, InterludeHarpVelocity = 0.7f, InterludeStarVelocity = 0.5f;

    /// <summary>The interlude's glass stars (bar, sixteenth, degree an octave up): scattered high chord tones while the tune rests.</summary>
    private static readonly (int Bar, int Step, int Degree)[] HomecomingStars =
        [(0, 4, 2), (1, 10, 4), (3, 2, 0), (4, 8, 2), (5, 12, 4), (6, 6, 1), (7, 12, 4), (9, 4, 2), (11, 10, 0)];

    /// <summary>
    /// Homecoming, variation <paramref name="variation"/> (0–3) of its cycle (D Ionian with borrowed iv and ♭VI,
    /// <see cref="Bpm"/>): 0 the melody on the flute over strings, harp and cello; 1 the first half on the clarinet an
    /// octave down with the cello's counter-line, the second phrased late on the violins with the flute an octave above and
    /// the horn's inner line through the darkening; 2 an interlude — harp an octave up and glass stars, the tune only in its
    /// last four bars, softly and high on the flute; 3 the reprise on the violins (<see cref="HomecomingReprise"/>: higher,
    /// to the peak, resolving home), the horn through the darkening and a fuller bass.
    /// </summary>
    public static SketchScore Homecoming(int variation)
    {
        var v = Mod(variation, Variations);
        var w = new SketchWriter(Bpm, DiatonicMode.Ionian, ModeSketches.TonicMidi);
        w.Mark(v switch
        {
            0 => "homecoming 1: the song (flute)",
            1 => "homecoming 2: clarinet and cello, then violins, flute and horn",
            2 => "homecoming 3: the interlude (harp and glass)",
            _ => "homecoming 4: the reprise (violins), the horn, home",
        });
        for (var bar = 0; bar < Bar; bar++)
        {
            Bed(w, bar, HomecomingChords[bar], padVelocity: v == 2 ? InterludePadVelocity : 0.8f, harpVelocity: v == 2 ? InterludeHarpVelocity : 0.45f, harpOctaves: v == 2 ? 1 : 0, roundBass: v != 2 && (v != 0 || bar >= 4));
        }

        for (var bar = 0; bar < Bar; bar++)
        {
            var tones = HomecomingMelody[bar];
            var second = bar >= 8;
            switch (v)
            {
                case 0: Line(w, SketchPart.Melody, SketchInstrument.Flute, bar, tones, 0, 0.8f, -0.05f); break;
                case 1:
                    if (!second)
                    {
                        // The clarinet leaves the open E out: a bar's breath (the cello alone) before the violins take the tune.
                        if (bar < 7) Line(w, SketchPart.Melody, SketchInstrument.Clarinet, bar, tones, -1, 0.85f, 0.15f);
                        Line(w, SketchPart.Counter, SketchInstrument.Cello, bar, HomecomingCounter[bar], 0, 0.5f, -0.3f);
                    }
                    else
                    {
                        Line(w, SketchPart.Melody, SketchInstrument.Violins, bar, Lazy(tones), 0, 0.8f, -0.1f);
                        Line(w, SketchPart.Counter, SketchInstrument.Flute, bar, Lazy(tones), 1, 0.35f, 0.2f);
                    }

                    break;
                case 2:
                    if (bar >= 12) Line(w, SketchPart.Melody, SketchInstrument.Flute, bar, tones, 1, 0.5f, -0.05f);
                    break;
                default: Line(w, SketchPart.Melody, SketchInstrument.Violins, bar, bar < 4 ? tones : HomecomingReprise[bar - 4], 0, 0.8f, -0.1f); break;
            }
        }

        if (v is 1 or 3)
        {
            for (var i = 0; i < 4; i++) Line(w, SketchPart.Counter, SketchInstrument.Horn, 12 + i, HomecomingHorn[i], 0, 0.5f, 0.3f);
        }

        if (v == 2)
        {
            foreach (var (bar, step, degree) in HomecomingStars) w.Degree(SketchPart.Colour, SketchInstrument.Glass, bar * Bar + step, 6, degree, 1, InterludeStarVelocity, step % 4 == 0 ? -0.4f : 0.4f);
        }

        w.AdvanceBars(Bar);
        return w.Build($"starter-homecoming-{v + 1}", RingSeconds);
    }

    /// <summary>A true modulo (never negative).</summary>
    private static int Mod(int x, int m) => ((x % m) + m) % m;
}
