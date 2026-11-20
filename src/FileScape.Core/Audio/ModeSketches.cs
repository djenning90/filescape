namespace FileScape.Core.Audio;

/// <summary>
/// The mode sketches (PLANET-79, PLANET-80, AD-72): the FileScape theme (<see cref="SkySynth.ThemeMelody"/>, the tune every
/// arrangement shares) written once in scale degrees and pitched in each of the seven diatonic modes, an original
/// launcher phrase in major and Mixolydian, and fuller arrangements that use a mode for a place or a moment. Everything is
/// a <see cref="SketchScore"/> for offline listening (rendered by <see cref="SkySynth.RenderSketch"/>); the game's own
/// music does not read anything here, so its arrangements and their selection are unchanged.
/// <para>
/// What a change must preserve: in the reel and the launcher pair only the mode differs between versions (tempo, tonic,
/// rhythm, instruments, velocities and chord roots as scale degrees are shared), so a listener hears the mode and nothing
/// else; the melodies are the FileScape theme's own (and the launcher phrase is original): never quote other music.
/// </para>
/// </summary>
public static class ModeSketches
{
    // ---------------------------------------------------------------- shared tuning

    /// <summary>The tonal centre of every sketch: D4 (MIDI 62), so the theme lies D4–F5 and its bed D2–D4.</summary>
    public const int TonicMidi = 62;

    /// <summary>The reel's tempo, beats per minute: a 5-bar version lasts 10.7 s (the brief asked for 8–12 s each).</summary>
    public const double ReelBpm = 112;

    /// <summary>The quiet between the reel's versions, seconds: the last chord's ring fades into it, then a moment of silence announces the next mode.</summary>
    public const double ReelGapSeconds = 1.6;

    /// <summary>The launcher pair's tempo, beats per minute (a lift above the hub's 94 and the repository's 126).</summary>
    public const double LauncherBpm = 128;

    /// <summary>The Lydian overlook's tempo, beats per minute: slow, floating.</summary>
    public const double OverlookBpm = 84;

    /// <summary>The Aeolian lament's tempo, beats per minute: the slowest, a long way from home.</summary>
    public const double LamentBpm = 72;

    /// <summary>The mode-shift journey's tempo, beats per minute: one pulse throughout, so the modes, not the tempo, change the world.</summary>
    public const double JourneyBpm = 112;

    /// <summary>How long each sketch rings after its last bar, seconds (the reverb and the last chord's release).</summary>
    private const double RingSeconds = 2.5;

    private const SketchPart M = SketchPart.Melody;

    // ---------------------------------------------------------------- the theme in seven modes (PLANET-79)

    /// <summary>
    /// The chord roots (scale degrees) under the short theme's four bars in <paramref name="mode"/>: the tonic, the mode's
    /// characteristic chord (the one holding its colour note), the chord before the cadence, the tonic. Ionian I IV V I;
    /// Dorian i IV ♭VII i (IV major: the raised sixth); Phrygian i ♭II ♭vii i; Lydian I II vii I (II major: the raised
    /// fourth); Mixolydian I v ♭VII I (both hold the lowered seventh); Aeolian i ♭VI ♭VII i; Locrian i° ♭II ♭V i°. Each chord
    /// holds the long note of its bar (re or mi), so the tune sits on its harmony in every mode.
    /// </summary>
    public static int[] ShortThemeChords(DiatonicMode mode) => mode switch
    {
        DiatonicMode.Ionian => [0, 3, 4, 0],
        DiatonicMode.Dorian => [0, 3, 6, 0],
        DiatonicMode.Phrygian => [0, 1, 6, 0],
        DiatonicMode.Lydian => [0, 1, 6, 0],
        DiatonicMode.Mixolydian => [0, 4, 6, 0],
        DiatonicMode.Aeolian => [0, 5, 6, 0],
        _ => [0, 1, 4, 0],
    };

    /// <summary>
    /// The short FileScape theme the reel plays (4 bars): the motif's two bars and the cadence's two (bars 1–2 and 7–8 of
    /// the written theme). The written theme touches only do re mi sol la (it was written to fold into a pentatonic key),
    /// so on its own Ionian, Lydian and Mixolydian would sound alike; two ornaments, the same in every mode, let the tune
    /// itself reach fa and ti: bar 2's long mi becomes a turn mi–fa–mi, and the cadence's long re becomes re–ti–re before
    /// do. The rhythm of every other note is the written one.
    /// </summary>
    public static IReadOnlyList<ThemeNote> ShortTheme { get; } = BuildShortTheme();

    private static ThemeNote[] BuildShortTheme()
    {
        var written = SkySynth.ThemeMelody;
        var notes = new List<ThemeNote>();
        foreach (var (fromBar, toBar) in new[] { (0, 0), (1, 1), (6, 2), (7, 3) })
        {
            foreach (var n in written.Where(n => n.StartSixteenths / 16 == fromBar))
            {
                var at = toBar * 16 + n.StartSixteenths % 16;
                if (toBar == 1 && n.LengthSixteenths == 8)
                {
                    // the long mi: a turn through fa
                    notes.Add(new ThemeNote(at, 4, n.Degree));
                    notes.Add(new ThemeNote(at + 4, 2, n.Degree + 1));
                    notes.Add(new ThemeNote(at + 6, 2, n.Degree));
                }
                else if (toBar == 2 && n.LengthSixteenths == 6)
                {
                    // the long re: round the seventh below the tonic
                    notes.Add(new ThemeNote(at, 4, n.Degree));
                    notes.Add(new ThemeNote(at + 4, 2, -1));
                }
                else if (toBar == 2 && n.StartSixteenths % 16 == 14)
                {
                    notes.Add(new ThemeNote(at, 2, 1)); // re again before do (written: mi)
                }
                else
                {
                    notes.Add(new ThemeNote(at, n.LengthSixteenths, n.Degree));
                }
            }
        }

        return [.. notes];
    }

    /// <summary>
    /// One version of the reel (PLANET-79) at the writer's cursor, in <paramref name="mode"/>: a bar that states the mode
    /// (the tonic chord, the mode's scale climbing on the harp, the bass on the tonic), then the short theme on the bell
    /// over its chords (<see cref="ShortThemeChords"/>), with the harp arpeggiating each chord, a round bass and soft
    /// offbeat hats; the last bar holds the tonic chord and a glass bell rings the characteristic degree. Five bars.
    /// </summary>
    public static void WriteThemeInMode(SketchWriter w, DiatonicMode mode)
    {
        w.Mode = mode;
        w.Mark(Modes.Name(mode));
        var chords = ShortThemeChords(mode);
        // Bar 0: the mode itself, do to do' in eighths, over the tonic chord.
        Triad(w, SketchInstrument.Pad, 0, 16, 0, -1, 0.9f);
        BassBar(w, 0, 0);
        for (var i = 0; i < 8; i++) w.Degree(SketchPart.Arpeggio, SketchInstrument.Harp, i * 2, 2, i, -1, 0.8f, 0.35f);
        Hats(w, 0, 0.3f);
        for (var bar = 0; bar < 4; bar++)
        {
            var at = 16 * (bar + 1);
            var root = chords[bar];
            Triad(w, SketchInstrument.Pad, at, 16, root, -1, 0.9f);
            BassBar(w, at, root);
            if (bar < 3) Arpeggio(w, at, root, -1, 0.6f); // D3–F#4, below the tune
            Hats(w, at, 0.3f);
        }

        foreach (var n in ShortTheme) w.Degree(M, SketchInstrument.Bell, 16 + n.StartSixteenths, n.LengthSixteenths, n.Degree, 0, 0.9f, -0.05f);
        w.Degree(SketchPart.Colour, SketchInstrument.Glass, 16 * 4 + 8, 8, Modes.CharacteristicDegree(mode), 1, 0.55f, 0.3f);
    }

    /// <summary>The reel: the theme in all seven modes, Ionian → Locrian, each followed by a short gap (≈ 12.3 s a version).</summary>
    public static SketchScore ModesReel()
    {
        var w = new SketchWriter(ReelBpm, DiatonicMode.Ionian, TonicMidi);
        foreach (var mode in Modes.All)
        {
            WriteThemeInMode(w, mode);
            w.AdvanceBars(5);
            if (mode != DiatonicMode.Locrian) w.AdvanceSeconds(ReelGapSeconds);
        }

        return w.Build("modes-reel", RingSeconds);
    }

    /// <summary>One version of the reel on its own (for comparing two modes back to back).</summary>
    public static SketchScore ThemeInMode(DiatonicMode mode)
    {
        var w = new SketchWriter(ReelBpm, mode, TonicMidi);
        WriteThemeInMode(w, mode);
        w.AdvanceBars(5);
        return w.Build("theme-" + Modes.Name(mode), RingSeconds);
    }

    // ---------------------------------------------------------------- the launcher theme (PLANET-80)

    /// <summary>
    /// The launcher phrase (original, 4 bars, as (start sixteenth, degree, length) per bar): a leap up to the octave and a
    /// turn round the seventh; the seventh held over the fifth's chord; a climb to la'; a fall to the seventh and up to do'.
    /// The seventh is in every bar but the third, held in bar 2 and the last step home in bar 4, so major's ti and
    /// Mixolydian's te are both heard plainly.
    /// </summary>
    private static readonly (int Start, int Degree, int Length)[][] LauncherPhrase =
    [
        [(0, 0, 2), (2, 4, 2), (4, 7, 3), (7, 6, 1), (8, 7, 2), (10, 9, 2), (12, 8, 4)],
        [(0, 7, 2), (2, 6, 2), (4, 4, 4), (8, 6, 6), (14, 4, 2)],
        [(0, 5, 2), (2, 7, 2), (4, 10, 4), (8, 9, 2), (10, 10, 2), (12, 12, 4)],
        [(0, 9, 2), (2, 8, 2), (4, 7, 2), (6, 6, 2), (8, 7, 8)],
    ];

    /// <summary>The launcher phrase's melody notes as theme notes (start in sixteenths from its first downbeat), for tests and logs.</summary>
    public static IReadOnlyList<ThemeNote> LauncherMelody { get; } = LauncherPhrase
        .SelectMany((bar, b) => bar.Select(n => new ThemeNote(b * 16 + n.Start, n.Length, n.Degree))).ToArray();

    /// <summary>
    /// The launcher's chord roots per half bar: I I | V V | IV IV | VII I. In major: I V IV vii° I (a leading-tone close);
    /// in Mixolydian: I v IV ♭VII I (the lowered seventh in the fifth's chord and in the bass's last step home).
    /// </summary>
    private static readonly int[] LauncherHalfBarChords = [0, 0, 4, 4, 3, 3, 6, 0];

    /// <summary>
    /// The launcher pair (PLANET-80): the same phrase in <paramref name="mode"/> (Ionian or Mixolydian for DJ's comparison),
    /// the same tempo, instruments and dynamics: a bar of drums and bass, the phrase on the pluck, then again with a glass
    /// bell an octave up, and the tonic held. ≈ 19 s.
    /// </summary>
    public static SketchScore Launcher(DiatonicMode mode)
    {
        var w = new SketchWriter(LauncherBpm, mode, TonicMidi);
        w.Mark("intro");
        LauncherBed(w, 0, [0, 0], drums: 0.8f, last: true);
        for (var pass = 0; pass < 2; pass++)
        {
            w.AdvanceBars(1);
            w.Mark(pass == 0 ? "phrase" : "phrase again, doubled");
            for (var b = 0; b < 4; b++) LauncherBed(w, 16 * b, LauncherHalfBarChords.AsSpan(2 * b, 2), drums: 1f, last: b == 3);
            LauncherMelodyAt(w, SketchInstrument.Pluck, 0, 0.95f);
            if (pass == 1) LauncherMelodyAt(w, SketchInstrument.Glass, 1, 0.5f);
            w.AdvanceBars(3);
        }

        w.AdvanceBars(1);
        Triad(w, SketchInstrument.Pad, 0, 16, 0, -1, 0.9f);
        w.Degree(SketchPart.Bass, SketchInstrument.Bass, 0, 16, 0, -2, 0.9f);
        w.Hit(SketchInstrument.Kick, 0, 0.8f);
        w.Hit(SketchInstrument.Crash, 0, 0.6f);
        w.AdvanceBars(1);
        return w.Build("launcher-" + (mode == DiatonicMode.Ionian ? "major" : Modes.Name(mode)), RingSeconds);
    }

    /// <summary>
    /// The launcher developed (PLANET-80), Mixolydian at the pair's tempo: a two-bar wind-up (a rising noise, the snare from
    /// eighths to sixteenths, the bass pulsing, the horns' do then te), the launch (a big kick, a crash and a falling sub
    /// boom) into the phrase on supersaw and pluck; the phrase an octave up with the horns singing te against it; the
    /// ♭VII–IV–I lift; and two bars of flight (glass arpeggios climbing, the tonic held). ≈ 28 s.
    /// </summary>
    public static SketchScore LauncherTakeoff()
    {
        var w = new SketchWriter(LauncherBpm, DiatonicMode.Mixolydian, TonicMidi);
        w.Mark("wind-up");
        Triad(w, SketchInstrument.StringPad, 0, 24, 0, -1, 0.9f);
        Triad(w, SketchInstrument.StringPad, 24, 8, 6, -1, 1f);
        w.Hit(SketchInstrument.Riser, 0, 1f, 32);
        for (var i = 0; i < 32; i++)
        {
            w.Degree(SketchPart.Bass, SketchInstrument.SawBass, i, 1, i < 24 ? 0 : -1, -2, 0.55f + 0.4f * i / 32);
            if (i >= 16 || i % 2 == 0) w.Hit(SketchInstrument.Snare, i, 0.25f + 0.6f * i / 32);
        }

        w.Degree(SketchPart.Counter, SketchInstrument.Horn, 0, 16, 0, 0, 0.7f);
        w.Degree(SketchPart.Counter, SketchInstrument.Horn, 16, 8, 0, 0, 0.8f);
        w.Degree(SketchPart.Counter, SketchInstrument.Horn, 24, 8, -1, 0, 1f); // te: the call to go
        w.AdvanceBars(2);

        w.Mark("launch");
        w.Hit(SketchInstrument.BigKick, 0, 1f);
        w.Hit(SketchInstrument.Crash, 0, 1f);
        w.Hit(SketchInstrument.SubBoom, 0, 1f, 16);
        for (var b = 0; b < 4; b++) LauncherBed(w, 16 * b, LauncherHalfBarChords.AsSpan(2 * b, 2), drums: 1f, last: b == 3, saw: true);
        LauncherMelodyAt(w, SketchInstrument.SuperSaw, 0, 0.8f);
        LauncherMelodyAt(w, SketchInstrument.Pluck, 0, 0.6f);
        w.AdvanceBars(4);

        w.Mark("climb: the phrase an octave up, the horns on te");
        w.Hit(SketchInstrument.Crash, 0, 0.7f);
        for (var b = 0; b < 4; b++) LauncherBed(w, 16 * b, LauncherHalfBarChords.AsSpan(2 * b, 2), drums: 1.1f, last: b == 3, saw: true);
        LauncherMelodyAt(w, SketchInstrument.SuperSaw, 1, 0.7f);
        LauncherMelodyAt(w, SketchInstrument.Glass, 1, 0.45f);
        int[] horn = [2, -1, 0, -1, 0];
        int[] hornStart = [0, 16, 32, 48, 56];
        int[] hornLength = [16, 16, 16, 8, 8];
        for (var i = 0; i < horn.Length; i++) w.Degree(SketchPart.Counter, SketchInstrument.Horn, hornStart[i], hornLength[i], horn[i], 0, 0.85f, 0.25f);
        w.AdvanceBars(4);

        w.Mark("lift: bVII - IV - I");
        Triad(w, SketchInstrument.StringPad, 0, 8, 6, -1, 1f);
        Triad(w, SketchInstrument.StringPad, 8, 8, 3, -1, 1f);
        Triad(w, SketchInstrument.StringPad, 16, 16, 0, -1, 1f);
        LauncherBed(w, 0, [6, 3], drums: 1.1f, last: true, saw: true);
        w.Degree(M, SketchInstrument.SuperSaw, 0, 8, 6, 1, 0.8f);
        w.Degree(M, SketchInstrument.SuperSaw, 8, 8, 5, 1, 0.8f);
        w.Degree(SketchPart.Counter, SketchInstrument.Horn, 0, 8, 1, 0, 0.9f);
        w.Degree(SketchPart.Counter, SketchInstrument.Horn, 8, 8, 0, 0, 0.9f);
        for (var i = 0; i < 4; i++) w.Hit(SketchInstrument.Snare, 12 + i, 0.5f + 0.12f * i);
        w.Mark("flight", 16);
        w.Hit(SketchInstrument.BigKick, 16, 0.9f);
        w.Hit(SketchInstrument.Crash, 16, 0.9f);
        w.Degree(M, SketchInstrument.SuperSaw, 16, 16, 7, 1, 0.8f);
        w.Degree(SketchPart.Counter, SketchInstrument.Horn, 16, 16, 2, 0, 0.9f);
        w.Degree(SketchPart.Bass, SketchInstrument.Bass, 16, 48, 0, -2, 0.9f);
        w.AdvanceBars(2);

        Triad(w, SketchInstrument.StringPad, 0, 32, 0, -1, 0.8f);
        for (var i = 0; i < 32; i++)
        {
            w.Degree(SketchPart.Arpeggio, SketchInstrument.Glass, i, 1, Ladder(0, i % 12), 0, 0.35f + 0.25f * (i % 12) / 12f, i % 2 == 0 ? -0.4f : 0.4f);
            if (i % 2 == 1) w.Hit(SketchInstrument.Hat, i, 0.35f, pan: 0.2f);
        }

        w.Degree(SketchPart.Colour, SketchInstrument.Glass, 24, 8, 6, 1, 0.5f, 0.3f); // te, high, as it flies off
        w.AdvanceBars(2);
        return w.Build("launcher-takeoff", RingSeconds + 1);
    }

    private static void LauncherMelodyAt(SketchWriter w, SketchInstrument instrument, int octaves, float velocity)
    {
        foreach (var n in LauncherMelody) w.Degree(M, instrument, n.StartSixteenths, n.LengthSixteenths, n.Degree, octaves, velocity * (n.StartSixteenths % 16 == 0 ? 1f : 0.88f), -0.05f);
    }

    /// <summary>One bar of the launcher's bed at <paramref name="at"/>: a pad chord (strings when <paramref name="saw"/>) per half bar, the saw bass in eighths on its root, four on the floor, the backbeat, offbeat hats; the last bar's bass ends on the tonic's leading or lowered seventh.</summary>
    private static void LauncherBed(SketchWriter w, double at, ReadOnlySpan<int> halves, float drums, bool last, bool saw = false)
    {
        for (var h = 0; h < 2; h++)
        {
            // With the supersaw on top, the strings take the chord in place of the soft pad (fuller, and within the voice budget).
            Triad(w, saw ? SketchInstrument.StringPad : SketchInstrument.Pad, at + 8 * h, 8, halves[h], -1, saw ? 0.8f : 0.9f);
        }

        for (var e = 0; e < 8; e++)
        {
            var root = halves[e / 4];
            var degree = last && e == 7 && halves[1] == 0 ? 6 - 7 : root;
            w.Degree(SketchPart.Bass, SketchInstrument.SawBass, at + 2 * e, 2, Low(degree), -2, e % 2 == 0 ? 0.9f : 0.7f);
            w.Hit(SketchInstrument.Hat, at + 2 * e + 1, 0.45f * drums, pan: 0.2f);
        }

        for (var beat = 0; beat < 4; beat++) w.Hit(SketchInstrument.Kick, at + 4 * beat, 0.8f * drums);
        w.Hit(SketchInstrument.Snare, at + 4, 0.55f * drums);
        w.Hit(SketchInstrument.Snare, at + 12, 0.55f * drums);
    }

    // ---------------------------------------------------------------- the arrangements (PLANET-80)

    /// <summary>
    /// "The overlook" (Lydian, 84 BPM, 16 bars): the whole written theme on the flute over a tonic pedal while the strings and the harp
    /// move between I and II (the major chord on the second degree that holds the raised fourth: Lydian's float), a glass
    /// bell ringing the raised fourth at the ends of phrases, the second half again an octave up on the glass, and a II–I
    /// close. For the overview, the telescope's view, an establishing shot of a new system. ≈ 48 s.
    /// <para>
    /// AD-79 takes it into the game as the overlook's own music (<see cref="ExperiencePieces.Overlook"/>):
    /// <paramref name="variation"/> 0 is the sketch exactly as DJ heard it (2026-11-19: "beautiful"); 1, the second light
    /// (a revisit, and every other time round), keeps the form, the harmony and the harp and lets the glass carry the theme
    /// an octave up with the flute a third below it, the answer return to the flute alone, and the strings give way to the
    /// aurora pad, whose filter opens with each chord.
    /// </para>
    /// </summary>
    public static SketchScore LydianOverlook(int variation = 0)
    {
        var second = variation % 2 == 1;
        var w = new SketchWriter(OverlookBpm, DiatonicMode.Lydian, TonicMidi);
        int[] upper = [0, 1, 0, 2, 5, 1, 1, 0];
        w.Mark("intro: I and II over the pedal");
        for (var bar = 0; bar < 2; bar++)
        {
            OverlookBar(w, 16 * bar, bar == 0 ? 0 : 1, second);
        }

        w.Degree(SketchPart.Colour, SketchInstrument.Glass, 24, 8, 3, 1, 0.5f, 0.35f);
        w.AdvanceBars(2);
        w.Mark(second ? "the theme (glass, the flute a third below)" : "the theme (flute)");
        for (var bar = 0; bar < 8; bar++)
        {
            OverlookBar(w, 16 * bar, upper[bar], second);
            if (upper[bar] == 1 && bar % 2 == 1) w.Degree(SketchPart.Colour, SketchInstrument.Glass, 16 * bar + 12, 4, 3, 1, 0.45f, 0.35f);
        }

        foreach (var n in SkySynth.ThemeMelody)
        {
            if (!second)
            {
                w.Degree(M, SketchInstrument.Flute, n.StartSixteenths, n.LengthSixteenths, n.Degree, 0, 0.9f, -0.1f);
                continue;
            }

            w.Degree(M, SketchInstrument.Glass, n.StartSixteenths, n.LengthSixteenths, n.Degree, 1, 0.6f, 0.15f);
            w.Degree(SketchPart.Counter, SketchInstrument.Flute, n.StartSixteenths, n.LengthSixteenths, n.Degree - 2, 0, 0.55f, -0.25f); // a third below
        }

        w.AdvanceBars(8);
        w.Mark(second ? "the answer (the flute alone)" : "the answer an octave up (glass), the flute below");
        for (var bar = 0; bar < 4; bar++) OverlookBar(w, 16 * bar, upper[4 + bar], second);
        foreach (var n in SkySynth.ThemeMelody.Where(n => n.StartSixteenths >= 64))
        {
            if (second)
            {
                w.Degree(M, SketchInstrument.Flute, n.StartSixteenths - 64, n.LengthSixteenths, n.Degree, 0, 0.85f, -0.1f);
                continue;
            }

            w.Degree(M, SketchInstrument.Glass, n.StartSixteenths - 64, n.LengthSixteenths, n.Degree, 1, 0.6f, 0.15f);
            w.Degree(SketchPart.Counter, SketchInstrument.Flute, n.StartSixteenths - 64, n.LengthSixteenths, n.Degree - 2, 0, 0.45f, -0.25f); // a third below
        }

        w.AdvanceBars(4);
        w.Mark("close: II - I");
        OverlookBar(w, 0, 1, second);
        OverlookBar(w, 16, 0, second);
        w.Degree(M, SketchInstrument.Flute, 0, 8, 1, 1, 0.7f);
        w.Degree(M, SketchInstrument.Flute, 8, 8, 3, 0, 0.7f);
        w.Degree(M, SketchInstrument.Flute, 16, 16, 2, 0, 0.7f);
        w.Degree(SketchPart.Colour, SketchInstrument.Glass, 20, 12, 3, 1, 0.45f, 0.35f);
        w.Degree(SketchPart.Bass, SketchInstrument.Timpani, 16, 4, 0, -2, 0.5f);
        w.AdvanceBars(2);
        return w.Build(second ? "overlook-second-light" : "arrangement-lydian-overlook", RingSeconds + 1);
    }

    /// <summary>A bar of the overlook: the tonic pedal low, the strings (the aurora pad in the second light) on <paramref name="upper"/>'s triad, the harp rising through it in eighths.</summary>
    private static void OverlookBar(SketchWriter w, double at, int upper, bool aurora = false)
    {
        w.Degree(SketchPart.Bass, SketchInstrument.Bass, at, 16, 0, -2, 0.7f);
        Triad(w, aurora ? SketchInstrument.AuroraPad : SketchInstrument.StringPad, at, 16, upper, -1, aurora ? 0.8f : 1f);
        for (var e = 0; e < 8; e++) w.Degree(SketchPart.Arpeggio, SketchInstrument.Harp, at + 2 * e, 2, Ladder(Low(upper), e < 5 ? e + 1 : 9 - e), -1, 0.55f, 0.35f);
    }

    /// <summary>
    /// "The empty moon" (Aeolian, 72 BPM): the written theme on the cellos (its first half) and the clarinet (its second),
    /// over strings and a harp in quarter notes, chords i ♭VI i III ♭VI i ♭VII i, then the ♭VI–♭VII–i close with the
    /// timpani. For an empty moon, a deep and forgotten folder, the way home after a fall. ≈ 45 s.
    /// </summary>
    public static SketchScore AeolianLament()
    {
        var w = new SketchWriter(LamentBpm, DiatonicMode.Aeolian, TonicMidi);
        int[] chords = [0, 5, 0, 2, 5, 0, 6, 0];
        w.Mark("intro");
        LamentBar(w, 0, 0, 0.8f);
        LamentBar(w, 16, 5, 0.8f);
        w.AdvanceBars(2);
        w.Mark("the theme (cellos, then clarinet)");
        for (var bar = 0; bar < 8; bar++) LamentBar(w, 16 * bar, chords[bar], 1f);
        foreach (var n in SkySynth.ThemeMelody)
        {
            var second = n.StartSixteenths >= 64;
            w.Degree(M, second ? SketchInstrument.Clarinet : SketchInstrument.Cello, n.StartSixteenths, n.LengthSixteenths, n.Degree, second ? 0 : -1, 0.9f, second ? 0.15f : -0.2f);
            if (!second && n.StartSixteenths >= 32) w.Degree(SketchPart.Counter, SketchInstrument.Violins, n.StartSixteenths, n.LengthSixteenths, n.Degree, 0, 0.35f, 0.3f);
        }

        w.AdvanceBars(8);
        w.Mark("close: bVI - bVII - i");
        LamentBar(w, 0, 5, 1f);
        LamentBar(w, 16, 6, 1f);
        LamentBar(w, 32, 0, 0.8f);
        w.Degree(M, SketchInstrument.Violins, 0, 16, 7, 0, 0.6f);
        w.Degree(M, SketchInstrument.Violins, 16, 12, 8, 0, 0.65f);
        w.Degree(M, SketchInstrument.Violins, 28, 4, 6, 0, 0.6f);
        w.Degree(M, SketchInstrument.Violins, 32, 16, 7, 0, 0.55f);
        for (var i = 0; i < 8; i++) w.Note(SketchPart.Bass, SketchInstrument.Timpani, 24 + i, 1, w.Pitch(4, -2), 0.25f + 0.05f * i);
        w.Degree(SketchPart.Bass, SketchInstrument.Timpani, 32, 4, 0, -2, 0.8f);
        w.AdvanceBars(3);
        return w.Build("arrangement-aeolian-empty-moon", RingSeconds + 1);
    }

    /// <summary>A bar of the lament: the bass on the chord's root, the strings' chord, the harp in quarter notes up its ladder.</summary>
    private static void LamentBar(SketchWriter w, double at, int chord, float velocity)
    {
        w.Degree(SketchPart.Bass, SketchInstrument.Bass, at, 16, Low(chord), -2, 0.75f * velocity);
        Triad(w, SketchInstrument.StringPad, at, 16, chord, -1, velocity);
        for (var q = 0; q < 4; q++) w.Degree(SketchPart.Arpeggio, SketchInstrument.Harp, at + 4 * q, 4, Ladder(Low(chord), q), -1, 0.5f * velocity, 0.35f);
    }

    /// <summary>
    /// The mode-shift journey (PLANET-80): the theme's first four bars return in a new mode at each moment of a trip at one
    /// pulse (112 BPM). On the home world, Ionian (marimba, light hats), and a wind-up bar; the launch: Mixolydian (supersaw,
    /// full kit, the horns' te); the flight: Lydian (glass and flute over harp sparkle, no kick) and a landing bar; landed
    /// in the deep woods: Dorian (cellos, strings, timpani); crossing into a hazard world: Phrygian (the unstable bell over a
    /// heartbeat and a b2 ostinato), closing on ♭II–i. ≈ 53 s.
    /// </summary>
    public static SketchScore ModeShiftJourney()
    {
        var w = new SketchWriter(JourneyBpm, DiatonicMode.Ionian, TonicMidi);

        // ---- home world: Ionian
        w.Mark("home world (Ionian)");
        JourneySection(w, DiatonicMode.Ionian, [0, 5, 0, 4], SketchInstrument.Marimba, 0, bar =>
        {
            Hats(w, 16 * bar, 0.3f);
            if (bar % 2 == 0) w.Hit(SketchInstrument.Kick, 16 * bar, 0.5f);
        });
        w.AdvanceBars(4);
        w.Mark("wind-up");
        Triad(w, SketchInstrument.Pad, 0, 16, 4, -1, 0.9f);
        w.Hit(SketchInstrument.Riser, 0, 0.9f, 16);
        for (var i = 0; i < 16; i++)
        {
            w.Degree(SketchPart.Bass, SketchInstrument.SawBass, i, 1, 4 - 7, -2, 0.5f + 0.4f * i / 16);
            if (i >= 8 || i % 2 == 0) w.Hit(SketchInstrument.Snare, i, 0.25f + 0.5f * i / 16);
        }

        w.AdvanceBars(1);

        // ---- launch: Mixolydian
        w.Mark("launch (Mixolydian)");
        w.Hit(SketchInstrument.BigKick, 0, 1f);
        w.Hit(SketchInstrument.Crash, 0, 0.9f);
        w.Hit(SketchInstrument.SubBoom, 0, 0.9f, 16);
        JourneySection(w, DiatonicMode.Mixolydian, [0, 5, 0, 4], SketchInstrument.SuperSaw, 0, bar =>
        {
            for (var beat = 0; beat < 4; beat++) w.Hit(SketchInstrument.Kick, 16 * bar + 4 * beat, 0.75f);
            w.Hit(SketchInstrument.Snare, 16 * bar + 4, 0.5f);
            w.Hit(SketchInstrument.Snare, 16 * bar + 12, 0.5f);
            Hats(w, 16 * bar, 0.45f);
            w.Degree(SketchPart.Counter, SketchInstrument.Horn, 16 * bar + 8, 8, bar % 2 == 0 ? -1 : 1, 0, 0.6f, 0.25f); // te answers every other bar
        }, sawBass: true);
        Triad(w, SketchInstrument.Pad, 56, 8, 6, -1, 0.9f); // bVII under the last half bar: up into the flight
        w.AdvanceBars(4);

        // ---- flight: Lydian
        w.Mark("flight (Lydian)");
        JourneySection(w, DiatonicMode.Lydian, [0, 1, 0, 4], SketchInstrument.Glass, 0, bar =>
        {
            for (var i = 0; i < 16; i++) w.Degree(SketchPart.Arpeggio, SketchInstrument.Harp, 16 * bar + i, 1, Ladder(bar % 2 == 1 ? 1 : 0, 3 + (i % 6)), -1, 0.3f + 0.02f * (i % 6), i % 2 == 0 ? -0.45f : 0.45f);
            Hats(w, 16 * bar, 0.2f);
        }, strings: true, pedal: true, doubling: SketchInstrument.Flute);
        w.AdvanceBars(4);
        w.Mark("landing");
        Triad(w, SketchInstrument.StringPad, 0, 16, 1, -1, 0.9f);
        w.Degree(SketchPart.Bass, SketchInstrument.Bass, 0, 16, 0, -2, 0.7f);
        for (var i = 0; i < 12; i++) w.Degree(SketchPart.Arpeggio, SketchInstrument.Harp, i, 1, 14 - i, -1, 0.6f, 0.3f);
        w.Hit(SketchInstrument.Riser, 0, 0.4f, 12);
        w.AdvanceBars(1);

        // ---- landed in the deep woods: Dorian
        w.Mark("landed (Dorian)");
        w.Hit(SketchInstrument.Kick, 0, 0.9f);
        w.Degree(SketchPart.Bass, SketchInstrument.Timpani, 0, 4, 0, -2, 0.9f);
        JourneySection(w, DiatonicMode.Dorian, [0, 3, 0, 4], SketchInstrument.Cello, -1, bar =>
        {
            for (var q = 0; q < 4; q++) w.Degree(SketchPart.Arpeggio, SketchInstrument.Harp, 16 * bar + 4 * q + 2, 2, Ladder(Low(new[] { 0, 3, 0, 4 }[bar]), q + 1), -1, 0.4f, 0.35f);
            if (bar > 0) w.Degree(SketchPart.Bass, SketchInstrument.Timpani, 16 * bar, 4, Low(new[] { 0, 3, 0, 4 }[bar]), -2, 0.5f);
        }, strings: true, doubling: SketchInstrument.Clarinet);
        w.AdvanceBars(4);
        w.Mark("the edge of a hazard world");
        w.Mode = DiatonicMode.Phrygian;
        Triad(w, SketchInstrument.StringPad, 0, 16, 0, -1, 0.8f);
        for (var i = 0; i < 8; i++) w.Degree(SketchPart.Bass, SketchInstrument.SawBass, 2 * i, 2, i % 2 == 0 ? 0 : 1, -2, 0.5f + 0.05f * i);
        Heartbeat(w, 0, 0.6f);
        w.Hit(SketchInstrument.Riser, 0, 0.6f, 16);
        w.AdvanceBars(1);

        // ---- a new kind of world: Phrygian
        w.Mark("hazard world (Phrygian)");
        w.Hit(SketchInstrument.Crash, 0, 0.7f);
        w.Hit(SketchInstrument.SubBoom, 0, 0.7f, 16);
        int[] hazard = [0, 1, 1, 0];
        for (var bar = 0; bar < 4; bar++)
        {
            var at = 16 * bar;
            Triad(w, SketchInstrument.StringPad, at, 16, hazard[bar], -1, 0.9f);
            for (var i = 0; i < 8; i++) w.Degree(SketchPart.Bass, SketchInstrument.SawBass, at + 2 * i, 2, bar == 3 && i >= 4 ? 0 : i % 4 == 3 ? 1 : 0, -2, 0.85f);
            Heartbeat(w, at, 0.8f);
            w.Hit(SketchInstrument.Snare, at + 12, 0.45f);
            for (var i = 0; i < 4; i++) w.Hit(SketchInstrument.Hat, at + 4 * i + 2, 0.35f, pan: 0.2f);
        }

        foreach (var n in ShortTheme) w.Degree(M, SketchInstrument.Unstable, n.StartSixteenths, n.LengthSixteenths, n.Degree, 0, 0.95f, -0.05f);
        w.AdvanceBars(4);
        Triad(w, SketchInstrument.StringPad, 0, 16, 0, -1, 0.7f);
        w.Degree(SketchPart.Bass, SketchInstrument.Bass, 0, 16, 0, -2, 0.8f);
        w.Degree(SketchPart.Colour, SketchInstrument.Glass, 4, 8, 1, 1, 0.45f, 0.3f); // the b2, high, as the world settles
        w.AdvanceBars(1);
        return w.Build("arrangement-mode-shift-journey", RingSeconds + 1);
    }

    /// <summary>
    /// A journey section at the cursor: the theme's first four bars (motif and answer) in <paramref name="mode"/> on
    /// <paramref name="lead"/> over the four chords, a pad (or strings), the bass (round, or the saw in eighths; or a tonic
    /// pedal), and <paramref name="perBar"/>'s own kit and colour per bar.
    /// </summary>
    private static void JourneySection(SketchWriter w, DiatonicMode mode, int[] chords, SketchInstrument lead, int octaves, Action<int> perBar,
        bool sawBass = false, bool strings = false, bool pedal = false, SketchInstrument? doubling = null)
    {
        w.Mode = mode;
        for (var bar = 0; bar < 4; bar++)
        {
            var at = 16 * bar;
            Triad(w, strings ? SketchInstrument.StringPad : SketchInstrument.Pad, at, 16, chords[bar], -1, 0.9f);
            if (sawBass)
            {
                for (var e = 0; e < 8; e++) w.Degree(SketchPart.Bass, SketchInstrument.SawBass, at + 2 * e, 2, Low(chords[bar]), -2, e % 2 == 0 ? 0.85f : 0.65f);
            }
            else
            {
                BassBar(w, at, pedal ? 0 : chords[bar]);
            }

            perBar(bar);
        }

        foreach (var n in SkySynth.ThemeMelody.Where(n => n.StartSixteenths < 64))
        {
            w.Degree(M, lead, n.StartSixteenths, n.LengthSixteenths, n.Degree, octaves, 0.9f, -0.05f);
            if (doubling is { } d && n.StartSixteenths >= 32) w.Degree(SketchPart.Counter, d, n.StartSixteenths, n.LengthSixteenths, n.Degree, octaves + (d == SketchInstrument.Clarinet ? 1 : 0), 0.45f, 0.2f);
        }
    }

    // ---------------------------------------------------------------- the overview's star chart (AD-79)

    /// <summary>The star chart's tempo, beats per minute: between the overlook's float (84) and the reel's walk (112); 16 bars last 40 s.</summary>
    public const double StarChartBpm = 96;

    /// <summary>
    /// The star chart's chords under the theme's answer and raised motif (its bars 6–9), as scale degrees: Ionian vi V IV I
    /// (a falling bass from the relative minor home); Dorian i v ♭VII i (the written theme's own Dorian harmony).
    /// </summary>
    private static int[] StarChartMiddleChords(DiatonicMode mode) => mode == DiatonicMode.Dorian ? [0, 4, 6, 0] : [5, 4, 3, 0];

    /// <summary>The star chart's chords under its constellation (bars 10–13): Ionian IV V iii vi; Dorian IV v ♭III i.</summary>
    private static int[] StarChartConstellationChords(DiatonicMode mode) => mode == DiatonicMode.Dorian ? [3, 4, 2, 0] : [3, 4, 2, 5];

    /// <summary>
    /// The constellation (bars 10–13 of the star chart, original): a slow line joining chord tones, a held note and a step
    /// per bar, rising to the octave's ninth and settling on the octave, then a step up into the cadence. (start, degree, length) per bar.
    /// </summary>
    private static readonly (int Start, int Degree, int Length)[][] Constellation =
    [
        [(0, 7, 12), (12, 8, 4)],
        [(0, 6, 8), (8, 4, 8)],
        [(0, 4, 4), (4, 6, 4), (8, 9, 8)],
        [(0, 7, 12), (12, 8, 4)],
    ];

    /// <summary>
    /// "The star chart" (AD-79, original, built from the FileScape theme): the overview's own music, 16 bars at
    /// <see cref="StarChartBpm"/> in <paramref name="mode"/> (Ionian, or Dorian when you look out from a minor place). The
    /// intro states the mode as the reel does (the harp climbing its scale) under the aurora pad opening like a sky; the
    /// short theme follows on the bell over the reel's chords (bars 2–5: the reel's Ionian version, the one DJ liked best);
    /// then the theme's answer and raised motif on the aurora lead with the bell an octave above (6–9); the constellation,
    /// glass stars over the pad (10–13); and the cadence, the characteristic degree ringing on the glass (14–15).
    /// <paramref name="variation"/> 1 (a revisit, every other time round) gives the short theme to the aurora lead and the
    /// answer to the bell. Soft offbeat hats and a kick every second bar from bar 6 give it a slow pulse.
    /// </summary>
    public static SketchScore StarChart(DiatonicMode mode, int variation = 0)
    {
        var swap = variation % 2 == 1;
        var w = new SketchWriter(StarChartBpm, mode, TonicMidi);
        var shortChords = ShortThemeChords(mode);
        w.Mark("intro: the mode, the sky opening");
        Triad(w, SketchInstrument.AuroraPad, 0, 16, 0, -1, 0.75f);
        Triad(w, SketchInstrument.AuroraPad, 16, 16, 3, -1, 0.8f);
        w.Degree(SketchPart.Bass, SketchInstrument.AuroraBass, 0, 16, 0, -2, 0.7f);
        w.Degree(SketchPart.Bass, SketchInstrument.AuroraBass, 16, 16, Low(3), -2, 0.7f);
        for (var i = 0; i < 8; i++) w.Degree(SketchPart.Arpeggio, SketchInstrument.Harp, i * 2, 2, i, -1, 0.75f, 0.35f);
        w.Degree(SketchPart.Colour, SketchInstrument.Glass, 20, 6, 4, 1, 0.4f, -0.3f);
        w.Degree(SketchPart.Colour, SketchInstrument.Glass, 26, 6, 7, 1, 0.4f, 0.3f);
        w.AdvanceBars(2);

        w.Mark(swap ? "the short theme (aurora lead)" : "the short theme (bell)");
        for (var bar = 0; bar < 4; bar++)
        {
            var at = 16 * bar;
            Triad(w, SketchInstrument.Pad, at, 16, shortChords[bar], -1, 0.9f);
            BassBar(w, at, shortChords[bar]);
            Arpeggio(w, at, shortChords[bar], -1, 0.55f);
            Hats(w, at, 0.25f);
        }

        foreach (var n in ShortTheme) w.Degree(M, swap ? SketchInstrument.AuroraLead : SketchInstrument.Bell, n.StartSixteenths, n.LengthSixteenths, n.Degree, 0, 0.9f, -0.05f);
        w.AdvanceBars(4);

        w.Mark(swap ? "the answer and the raised motif (bell, glass above)" : "the answer and the raised motif (aurora lead, the bell above)");
        var middle = StarChartMiddleChords(mode);
        for (var bar = 0; bar < 4; bar++)
        {
            var at = 16 * bar;
            Triad(w, SketchInstrument.AuroraPad, at, 16, middle[bar], -1, 0.85f);
            BassBar(w, at, middle[bar]);
            Hats(w, at, 0.3f);
            if (bar % 2 == 0) w.Hit(SketchInstrument.Kick, at, 0.45f);
        }

        foreach (var n in SkySynth.ThemeMelody.Where(n => n.StartSixteenths is >= 32 and < 96))
        {
            var at = n.StartSixteenths - 32;
            w.Degree(M, swap ? SketchInstrument.Bell : SketchInstrument.AuroraLead, at, n.LengthSixteenths, n.Degree, 0, 0.85f, -0.05f);
            w.Degree(SketchPart.Counter, swap ? SketchInstrument.Glass : SketchInstrument.Bell, at, n.LengthSixteenths, n.Degree, 1, 0.35f, 0.25f);
        }

        w.AdvanceBars(4);

        w.Mark("the constellation (glass stars over the pad)");
        var stars = StarChartConstellationChords(mode);
        for (var bar = 0; bar < 4; bar++)
        {
            var at = 16 * bar;
            Triad(w, SketchInstrument.AuroraPad, at, 16, stars[bar], -1, 0.9f);
            w.Degree(SketchPart.Bass, SketchInstrument.AuroraBass, at, 8, Low(stars[bar]), -2, 0.75f);
            w.Degree(SketchPart.Bass, SketchInstrument.AuroraBass, at + 8, 8, Low(stars[bar]) + 4, -2, 0.6f);
            for (var e = 0; e < 4; e++) w.Degree(SketchPart.Arpeggio, SketchInstrument.Glass, at + 4 * e + 2, 2, Ladder(stars[bar], 3 + e), -1, 0.3f + 0.05f * e, e % 2 == 0 ? -0.45f : 0.45f);
            Hats(w, at, 0.22f);
            if (bar % 2 == 0) w.Hit(SketchInstrument.Kick, at, 0.4f);
            foreach (var (start, degree, length) in Constellation[bar]) w.Degree(M, swap ? SketchInstrument.Glass : SketchInstrument.Bell, at + start, length, degree, 0, 0.75f, -0.1f);
        }

        w.AdvanceBars(4);

        w.Mark("the cadence, the mode's colour on the glass");
        for (var bar = 0; bar < 2; bar++)
        {
            var at = 16 * bar;
            var root = shortChords[2 + bar];
            Triad(w, SketchInstrument.Pad, at, 16, root, -1, 0.9f);
            Triad(w, SketchInstrument.AuroraPad, at, 16, root, -1, 0.6f);
            BassBar(w, at, root);
            if (bar == 0) Arpeggio(w, at, root, -1, 0.5f);
            Hats(w, at, 0.2f);
        }

        foreach (var n in ShortTheme.Where(n => n.StartSixteenths >= 32)) w.Degree(M, SketchInstrument.Bell, n.StartSixteenths - 32, n.LengthSixteenths, n.Degree, 0, 0.85f, -0.05f);
        w.Degree(SketchPart.Colour, SketchInstrument.Glass, 16 + 8, 8, Modes.CharacteristicDegree(mode), 1, 0.5f, 0.3f);
        w.AdvanceBars(2);
        return w.Build("star-chart-" + Modes.Name(mode).ToLowerInvariant() + (swap ? "-second" : ""), RingSeconds + 1);
    }

    /// <summary>
    /// The DSP extension's before and after (AD-79, PLANET-99): one phrase, the star chart's middle (the theme's answer and
    /// raised motif over vi V IV I, then the cadence), with the same notes, velocities and pans either way. Before: the engine's
    /// existing analog-flavoured voices, the horns (paired saws, a fixed filter swell) for the line, the string pad for the
    /// chords and the saw bass. After: the aurora voices (lead, pad, bass), whose resonant filters follow their own
    /// envelopes and the velocity. Ionian on D, <see cref="StarChartBpm"/>; 6 bars, ≈ 15 s plus the ring.
    /// </summary>
    public static SketchScore AuroraComparison(bool aurora)
    {
        var w = new SketchWriter(StarChartBpm, DiatonicMode.Ionian, TonicMidi);
        var lead = aurora ? SketchInstrument.AuroraLead : SketchInstrument.Horn;
        var pad = aurora ? SketchInstrument.AuroraPad : SketchInstrument.StringPad;
        var bass = aurora ? SketchInstrument.AuroraBass : SketchInstrument.SawBass;
        w.Mark(aurora ? "after: the aurora voices" : "before: horns, string pad, saw bass");
        int[] chords = [5, 4, 3, 0, 4, 0];
        for (var bar = 0; bar < chords.Length; bar++)
        {
            var at = 16 * bar;
            Triad(w, pad, at, 16, chords[bar], -1, 0.85f);
            for (var e = 0; e < 4; e++) w.Degree(SketchPart.Bass, bass, at + 4 * e, 4, Low(chords[bar]) + (e == 2 ? 4 : 0), -2, e % 2 == 0 ? 0.85f : 0.6f);
        }

        foreach (var n in SkySynth.ThemeMelody.Where(n => n.StartSixteenths is >= 32 and < 96)) w.Degree(M, lead, n.StartSixteenths - 32, n.LengthSixteenths, n.Degree, 0, n.StartSixteenths % 16 == 0 ? 0.95f : 0.75f, -0.05f);
        foreach (var n in ShortTheme.Where(n => n.StartSixteenths >= 32)) w.Degree(M, lead, 64 + n.StartSixteenths - 32, n.LengthSixteenths, n.Degree, 0, 0.8f, -0.05f);
        w.AdvanceBars(chords.Length);
        return w.Build(aurora ? "dsp-after-aurora" : "dsp-before-saws", RingSeconds);
    }

    // ---------------------------------------------------------------- the bed

    /// <summary>A chord root folded into the lower half of the octave (degrees 4–6 an octave down), so voicings stay near the tonic.</summary>
    private static int Low(int root) => root >= 4 ? root - 7 : root;

    /// <summary>The <paramref name="rung"/>th chord tone up from <paramref name="root"/>: root, third, fifth, then the next octave's.</summary>
    private static int Ladder(int root, int rung) => root + rung / 3 * Modes.Length + rung % 3 * 2;

    /// <summary>The triad on <paramref name="root"/> (root, third, fifth in the mode, root folded low), spread left to right.</summary>
    private static void Triad(SketchWriter w, SketchInstrument instrument, double at, double length, int root, int octaves, float velocity)
    {
        var low = Low(root);
        for (var i = 0; i < 3; i++) w.Degree(SketchPart.Chord, instrument, at, length, low + 2 * i, octaves, velocity, -0.4f + 0.4f * i);
    }

    /// <summary>A bar of round bass on <paramref name="root"/>: the root held, again on the and of two, then the chord's fifth, and the root to lead on.</summary>
    private static void BassBar(SketchWriter w, double at, int root)
    {
        var low = Low(root);
        w.Degree(SketchPart.Bass, SketchInstrument.Bass, at, 6, low, -2, 0.9f);
        w.Degree(SketchPart.Bass, SketchInstrument.Bass, at + 6, 2, low, -2, 0.6f);
        w.Degree(SketchPart.Bass, SketchInstrument.Bass, at + 8, 6, low + 4, -2, 0.75f);
        w.Degree(SketchPart.Bass, SketchInstrument.Bass, at + 14, 2, low, -2, 0.6f);
    }

    /// <summary>The harp arpeggiating <paramref name="root"/>'s chord in eighths (up the ladder and back).</summary>
    private static void Arpeggio(SketchWriter w, double at, int root, int octaves, float velocity)
    {
        ReadOnlySpan<int> rungs = [0, 1, 2, 3, 4, 3, 2, 1];
        for (var e = 0; e < 8; e++) w.Degree(SketchPart.Arpeggio, SketchInstrument.Harp, at + 2 * e, 2, Ladder(Low(root), rungs[e]), octaves, velocity, 0.35f);
    }

    /// <summary>Soft closed hats on a bar's offbeat eighths.</summary>
    private static void Hats(SketchWriter w, double at, float velocity)
    {
        for (var e = 0; e < 4; e++) w.Hit(SketchInstrument.Hat, at + 4 * e + 2, velocity, pan: 0.2f);
    }

    /// <summary>A heartbeat on each half bar: two kicks a dotted sixteenth apart, the second softer.</summary>
    private static void Heartbeat(SketchWriter w, double at, float velocity)
    {
        for (var h = 0; h < 2; h++)
        {
            w.Hit(SketchInstrument.Kick, at + 8 * h, velocity);
            w.Hit(SketchInstrument.Kick, at + 8 * h + 1.5, velocity * 0.6f);
        }
    }

    /// <summary>Every sketch for the listening folder, by file name (without .wav), in README order.</summary>
    public static IReadOnlyList<SketchScore> All() =>
    [
        ModesReel(), Launcher(DiatonicMode.Ionian), Launcher(DiatonicMode.Mixolydian), LauncherTakeoff(),
        LydianOverlook(), AeolianLament(), ModeShiftJourney(),
    ];
}
