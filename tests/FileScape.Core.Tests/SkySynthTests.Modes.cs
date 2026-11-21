using FileScape.Core.Audio;
using static FileScape.Core.Tests.SkySynthKit;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>Runs only when SKYSYNTH_MODES_DIR names an output folder (PLANET-79/80's mode sketches, AD-72); otherwise skipped.</summary>
public sealed class SkySynthModesFactAttribute : FactAttribute
{
    public SkySynthModesFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKYSYNTH_MODES_DIR")))
        {
            Skip = "Set SKYSYNTH_MODES_DIR to a folder to render the PLANET-79/80 mode sketches.";
        }
    }
}

/// <summary>
/// PLANET-79/80 (AD-72): the diatonic modes (<see cref="Modes"/>), the FileScape theme in each of them, the launcher pair
/// and the arrangements (<see cref="ModeSketches"/>), and the offline renderer (<see cref="SkySynth.RenderSketch"/>):
/// intervals, the tune's rhythm and contour kept in every mode, only the mode differing within a comparison, the
/// characteristic degree heard, and renders that neither clip nor fall silent where they should not.
/// </summary>
public class SkySynthModeTests(ITestOutputHelper output)
{
    [Fact]
    public void Each_mode_has_the_intervals_of_its_rotation_of_the_major_scale()
    {
        var expected = new Dictionary<DiatonicMode, int[]>
        {
            [DiatonicMode.Ionian] = [0, 2, 4, 5, 7, 9, 11],
            [DiatonicMode.Dorian] = [0, 2, 3, 5, 7, 9, 10],
            [DiatonicMode.Phrygian] = [0, 1, 3, 5, 7, 8, 10],
            [DiatonicMode.Lydian] = [0, 2, 4, 6, 7, 9, 11],
            [DiatonicMode.Mixolydian] = [0, 2, 4, 5, 7, 9, 10],
            [DiatonicMode.Aeolian] = [0, 2, 3, 5, 7, 8, 10],
            [DiatonicMode.Locrian] = [0, 1, 3, 5, 6, 8, 10],
        };
        foreach (var (mode, semitones) in expected) Modes.Semitones(mode).Should().Equal(semitones, Modes.Name(mode));
        Modes.SemitonesOf(DiatonicMode.Dorian, 7).Should().Be(12);
        Modes.SemitonesOf(DiatonicMode.Mixolydian, -1).Should().Be(-2, "te below do");
        Modes.SemitonesOf(DiatonicMode.Ionian, -1).Should().Be(-1, "ti below do");
        Modes.SemitonesOf(DiatonicMode.Lydian, 10).Should().Be(18, "the raised fourth an octave up");
        Modes.Midi(DiatonicMode.Phrygian, 62, 1).Should().Be(63, "E-flat over D");

        // Brightest to darkest: each mode lowers exactly one degree of the one before (one semitone less in all).
        for (var i = 1; i < Modes.BrightestFirst.Count; i++)
        {
            var a = Modes.Semitones(Modes.BrightestFirst[i - 1]);
            var b = Modes.Semitones(Modes.BrightestFirst[i]);
            a.Zip(b).Count(p => p.First != p.Second).Should().Be(1);
            (a.Sum() - b.Sum()).Should().Be(1);
        }
    }

    [Fact]
    public void Each_characteristic_degree_is_where_the_mode_differs_from_its_neighbour_and_its_chords_have_their_qualities()
    {
        void Differs(DiatonicMode mode, DiatonicMode neighbour) =>
            Modes.SemitonesOf(mode, Modes.CharacteristicDegree(mode)).Should().NotBe(Modes.SemitonesOf(neighbour, Modes.CharacteristicDegree(mode)), $"{mode} against {neighbour}");
        Differs(DiatonicMode.Ionian, DiatonicMode.Mixolydian);
        Differs(DiatonicMode.Dorian, DiatonicMode.Aeolian);
        Differs(DiatonicMode.Phrygian, DiatonicMode.Aeolian);
        Differs(DiatonicMode.Lydian, DiatonicMode.Ionian);
        Differs(DiatonicMode.Mixolydian, DiatonicMode.Ionian);
        Differs(DiatonicMode.Aeolian, DiatonicMode.Dorian);
        Differs(DiatonicMode.Locrian, DiatonicMode.Phrygian);

        Modes.TriadOn(DiatonicMode.Ionian, 6).Should().Be(TriadQuality.Diminished);
        Modes.TriadOn(DiatonicMode.Dorian, 3).Should().Be(TriadQuality.Major, "Dorian's IV");
        Modes.TriadOn(DiatonicMode.Phrygian, 1).Should().Be(TriadQuality.Major, "Phrygian's bII");
        Modes.TriadOn(DiatonicMode.Lydian, 1).Should().Be(TriadQuality.Major, "Lydian's II");
        Modes.TriadOn(DiatonicMode.Mixolydian, 4).Should().Be(TriadQuality.Minor, "Mixolydian's v");
        Modes.TriadOn(DiatonicMode.Mixolydian, 6).Should().Be(TriadQuality.Major, "Mixolydian's bVII");
        Modes.TriadOn(DiatonicMode.Aeolian, 5).Should().Be(TriadQuality.Major, "Aeolian's bVI");
        Modes.TriadOn(DiatonicMode.Locrian, 0).Should().Be(TriadQuality.Diminished, "Locrian's tonic");
        foreach (var mode in Modes.All)
        {
            Enumerable.Range(0, 7).Count(d => Modes.TriadOn(mode, d) == TriadQuality.Diminished).Should().Be(1, $"{mode}: one diminished triad");
            Modes.IsMinor(mode).Should().Be(mode is DiatonicMode.Dorian or DiatonicMode.Phrygian or DiatonicMode.Aeolian or DiatonicMode.Locrian);
        }
    }

    [Fact]
    public void The_short_theme_keeps_the_written_motif_and_cadence_and_reaches_every_degree()
    {
        var written = SkySynth.ThemeMelody;
        var shortTheme = ModeSketches.ShortTheme;
        // Bar 1 exactly as written; every bar filled to 16 sixteenths with no overlaps.
        shortTheme.Where(n => n.StartSixteenths < 16).Should().Equal(written.Where(n => n.StartSixteenths < 16));
        for (var i = 1; i < shortTheme.Count; i++) shortTheme[i].StartSixteenths.Should().Be(shortTheme[i - 1].StartSixteenths + shortTheme[i - 1].LengthSixteenths);
        // Every written onset of bars 2, 7 and 8 is kept, on the written degree.
        foreach (var (from, to) in new[] { (1, 1), (6, 2), (7, 3) })
        {
            foreach (var n in written.Where(n => n.StartSixteenths / 16 == from && !(from == 6 && n.StartSixteenths % 16 == 14)))
            {
                shortTheme.Should().Contain(m => m.StartSixteenths == to * 16 + n.StartSixteenths % 16 && m.Degree == n.Degree, $"written bar {from + 1}, sixteenth {n.StartSixteenths % 16}");
            }
        }

        shortTheme[^1].Degree.Should().Be(0, "it ends on the tonic");
        // The tune itself touches every degree of the scale (written alone it has no fa and no ti), so each mode's colour is in the melody.
        shortTheme.Select(n => ((n.Degree % 7) + 7) % 7).Distinct().Should().HaveCount(7);
        written.Select(n => n.Degree % 7).Distinct().Should().NotContain([3, 6], "why the ornaments: the written theme is pentatonic");
    }

    [Fact]
    public void In_the_reel_only_the_mode_changes_between_versions_and_each_chord_holds_its_bars_long_note()
    {
        var versions = Modes.All.ToDictionary(m => m, ModeSketches.ThemeInMode);
        var ionian = versions[DiatonicMode.Ionian].Notes;
        foreach (var (mode, score) in versions)
        {
            score.Notes.Should().HaveCount(ionian.Count, Modes.Name(mode));
            for (var i = 0; i < ionian.Count; i++)
            {
                var (a, b) = (ionian[i], score.Notes[i]);
                (b.Start, b.Length, b.Part, b.Velocity, b.Pan).Should().Be((a.Start, a.Length, a.Part, a.Velocity, a.Pan), $"{mode} note {i}: same rhythm, part, dynamics and place");
                if (b.Part != SketchPart.Colour) b.Instrument.Should().Be(a.Instrument);
            }

            // The tune's contour: every step goes the same way in every mode, and the characteristic degree is in the tune.
            var tune = score.Notes.Where(n => n.Part == SketchPart.Melody).ToList();
            var steps = tune.Zip(tune.Skip(1), (x, y) => Math.Sign(y.MidiNote - x.MidiNote));
            steps.Should().Equal(ShortThemeSteps(), $"{mode}: the contour");
            var characteristic = Modes.SemitonesOf(mode, Modes.CharacteristicDegree(mode)) % 12;
            tune.Should().Contain(n => (n.MidiNote - ModeSketches.TonicMidi + 120) % 12 == characteristic, $"{mode}: the colour note is in the tune");

            // Each bar's longest tune note is a tone of its chord, or (bar 2's mi over the characteristic chord) its seventh,
            // ninth or sixth: a colour, never a wrong note.
            var chords = ModeSketches.ShortThemeChords(mode);
            for (var bar = 0; bar < 4; bar++)
            {
                var longest = ModeSketches.ShortTheme.Where(n => n.StartSixteenths / 16 == bar).MaxBy(n => n.LengthSixteenths);
                var tones = (bar == 1 ? new[] { 0, 2, 4, 6, 1, 5 } : [0, 2, 4]).Select(t => (chords[bar] + t) % 7);
                tones.Should().Contain(((longest.Degree % 7) + 7) % 7, $"{mode} bar {bar + 1}: the long note {longest.Degree} on chord {chords[bar]}");
            }

            // The characteristic degree is sounded by the bed too (a chord, or the scale in the first bar).
            score.Notes.Where(n => n.Part is SketchPart.Chord or SketchPart.Arpeggio)
                .Should().Contain(n => (n.MidiNote - ModeSketches.TonicMidi + 120) % 12 == characteristic);
        }

        int[] ShortThemeSteps()
        {
            var t = ModeSketches.ShortTheme;
            return t.Zip(t.Skip(1), (x, y) => Math.Sign(y.Degree - x.Degree)).ToArray();
        }
    }

    [Fact]
    public void The_launcher_pair_differs_only_in_the_seventh_which_is_in_its_tune_and_bass()
    {
        var major = ModeSketches.Launcher(DiatonicMode.Ionian);
        var mixolydian = ModeSketches.Launcher(DiatonicMode.Mixolydian);
        major.Duration.Should().Be(mixolydian.Duration);
        major.Notes.Should().HaveCount(mixolydian.Notes.Count);
        var differing = 0;
        for (var i = 0; i < major.Notes.Count; i++)
        {
            var (a, b) = (major.Notes[i], mixolydian.Notes[i]);
            (b.Start, b.Length, b.Part, b.Instrument, b.Velocity, b.Pan).Should().Be((a.Start, a.Length, a.Part, a.Instrument, a.Velocity, a.Pan));
            if (a.MidiNote == b.MidiNote) continue;
            differing++;
            (a.MidiNote - b.MidiNote).Should().Be(1, "only ti becomes te");
            ((a.MidiNote - ModeSketches.TonicMidi + 120) % 12).Should().Be(11);
        }

        output.WriteLine($"launcher: {major.Notes.Count} notes, {differing} lowered in Mixolydian; {major.Duration.TotalSeconds:0.0} s");
        int Sevenths(SketchScore s, SketchPart part) => s.Notes.Count(n => n.Part == part && (n.MidiNote - ModeSketches.TonicMidi + 120) % 12 == 10);
        Sevenths(mixolydian, SketchPart.Melody).Should().BeGreaterThanOrEqualTo(6, "te in the tune");
        Sevenths(mixolydian, SketchPart.Bass).Should().BeGreaterThanOrEqualTo(4, "te in the bass");
        Sevenths(major, SketchPart.Melody).Should().Be(0);
        // The phrase is original and stays bright: it starts on do and ends on do' and climbs overall.
        ModeSketches.LauncherMelody[0].Degree.Should().Be(0);
        ModeSketches.LauncherMelody[^1].Degree.Should().Be(7);
    }

    [Fact]
    public void A_sketch_renders_deterministically_unclipped_and_without_gaps()
    {
        var score = ModeSketches.ThemeInMode(DiatonicMode.Dorian);
        var a = SkySynth.RenderSketch(score, Rate);
        var b = SkySynth.RenderSketch(score, Rate);
        a.Should().Equal(b, "the same score gives the same samples");
        a.Length.Should().Be((int)Math.Ceiling(score.Duration.TotalSeconds * Rate) * 2);
        var scan = Scan(a, MusicEnd(score));
        output.WriteLine($"theme-dorian: {score.Duration.TotalSeconds:0.0} s, peak {scan.Peak:0.000}, {Lufs(a):0.0} LUFS, silent windows {scan.SilentSeconds:0.00} s: {string.Join(", ", scan.Runs)}");
        scan.Peak.Should().BeInRange(0.1f, SoftClipFrom, "audible and below the soft clipper");
        scan.SilentSeconds.Should().Be(0, "no gap while it plays");
        a.All(float.IsFinite).Should().BeTrue();
    }

    [Fact]
    public void A_sketch_synth_plays_no_world_music_of_its_own()
    {
        // An empty score is silence: the stopped decks never schedule the hub's music.
        var empty = new SketchWriter(100, DiatonicMode.Ionian, 62).Build("empty", 3);
        SkySynth.RenderSketch(empty, Rate).Max(MathF.Abs).Should().Be(0f);
    }

    /// <summary>How long a voice is counted after its written end in <see cref="PeakVoices"/>, seconds (a typical release or decay to inaudible; an estimate).</summary>
    private const double VoiceRingSeconds = 0.6;

    /// <summary>
    /// An estimate of the most synth voices a score needs at once: each note's voices (a supersaw three, a flute, bass or
    /// twinned bell two, a timpani three, a kick two) from its start to its end plus <see cref="VoiceRingSeconds"/>. Over
    /// <see cref="SkySynth.MaxVoices"/> the quietest voices are stolen (click-free, but the tails shorten).
    /// </summary>
    private static int PeakVoices(SketchScore score)
    {
        static int VoicesOf(SketchInstrument i) => i switch
        {
            SketchInstrument.SuperSaw or SketchInstrument.Timpani => 3,
            SketchInstrument.Flute or SketchInstrument.Unstable or SketchInstrument.Bass or SketchInstrument.SawBass or SketchInstrument.Kick or SketchInstrument.BigKick => 2,
            _ => 1,
        };
        var events = score.Notes.SelectMany(n => new[] { (n.Start.TotalSeconds, VoicesOf(n.Instrument)), ((n.Start + n.Length).TotalSeconds + VoiceRingSeconds, -VoicesOf(n.Instrument)) })
            .OrderBy(e => e.Item1).ThenBy(e => e.Item2);
        int now = 0, most = 0;
        foreach (var (_, d) in events) most = Math.Max(most, now += d);
        return most;
    }

    /// <summary>Where the last note's written length ends, seconds: after it only the ring, which fades to silence as it should.</summary>
    private static double MusicEnd(SketchScore score) => score.Notes.Max(n => n.Start + n.Length).TotalSeconds;

    /// <summary>
    /// The listening folder (SKYSYNTH_MODES_DIR): every sketch (<see cref="ModeSketches.All"/>) and each mode's version on its
    /// own (single/), with durations, peaks, loudness, the gap scan and the markers logged; fails if a render clips or
    /// falls silent outside the reel's deliberate gaps.
    /// </summary>
    [SkySynthModesFact]
    public void Render_the_mode_sketches_to_wav()
    {
        var dir = Environment.GetEnvironmentVariable("SKYSYNTH_MODES_DIR")!;
        Directory.CreateDirectory(Path.Combine(dir, "single"));
        var scores = ModeSketches.All().Select(s => (s, Path.Combine(dir, s.Name + ".wav")))
            .Concat(Modes.All.Select(m => ModeSketches.ThemeInMode(m)).Select(s => (s, Path.Combine(dir, "single", s.Name + ".wav"))));
        foreach (var (score, path) in scores)
        {
            var samples = SkySynth.RenderSketch(score, Rate);
            WriteWav(path, [.. samples], Rate);
            var scan = Scan(samples, MusicEnd(score));
            output.WriteLine($"{Path.GetRelativePath(dir, path)}: {score.Duration.TotalSeconds:0.0} s, peak {scan.Peak:0.000}, {Lufs(samples):0.0} LUFS, " +
                             $"silent runs: {(scan.Runs.Count == 0 ? "none" : string.Join(", ", scan.Runs.Select(r => $"{r.At:0.00}+{r.Length:0.00} s")))}");
            output.WriteLine($"  most voices at once (estimate: held plus {VoiceRingSeconds} s): {PeakVoices(score)} of {SkySynth.MaxVoices}");
            output.WriteLine("  " + string.Join(" · ", score.Markers.Select(m => $"{m.At.TotalSeconds:0.0} s {m.Label}")));
            scan.Peak.Should().BeLessThan(SoftClipFrom, score.Name);
            if (score.Name == "modes-reel")
            {
                scan.Runs.Should().HaveCountLessThanOrEqualTo(6, "silence only in the six gaps between versions");
                foreach (var (at, _) in scan.Runs)
                {
                    score.Markers.Should().Contain(m => m.At.TotalSeconds > at && m.At.TotalSeconds - at <= ModeSketches.ReelGapSeconds + 0.1, $"the silence at {at:0.00} s is a gap before a version");
                }
            }
            else
            {
                scan.Runs.Should().BeEmpty(score.Name);
            }
        }
    }
}
