using System.Globalization;
using System.Text;
using FileScape.Core.Audio;
using FileScape.Core.Interaction;
using FileScape.Core.Planets;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>Runs only when SKYSYNTH_STARTER_DIR names an output folder (the seventh pass's Starter Sky listening samples and analysis, AD-95); otherwise skipped.</summary>
public sealed class SkySynthStarterFactAttribute : FactAttribute
{
    public SkySynthStarterFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKYSYNTH_STARTER_DIR")))
        {
            Skip = "Set SKYSYNTH_STARTER_DIR to a folder to render the Starter Sky music comparison, the long renders with a revisit and the aurora demonstration.";
        }
    }
}

/// <summary>
/// PLANET-169..173 (AD-95): the Starter Sky's (the hub identity's) music as a choice — the classic AD-49 music by default
/// in Core, three written alternatives (<see cref="StarterThemeScores"/>) a deck plays like the overview's star chart. The
/// cheap checks: the catalogue, switching on the next beat and back, returns at a phrase and later visits on a later
/// variation, nothing changing elsewhere, the classic bit-identical, loudness with the classic hub and the voice budget,
/// and the long-form measures that keep revisits from fatiguing (from the notes, no rendering of minutes). The listening
/// renders and the analysis are <see cref="Render_the_starter_theme_listening_samples"/>, skipped by default.
/// <para>A separate class from the partial SkySynthTests (being restructured for speed by another stream): its own small
/// helpers, renders at a low rate and at most one cycle each.</para>
/// </summary>
public class SkySynthStarterThemeTests(ITestOutputHelper output)
{
    /// <summary>The cheap checks' sample rate, Hz: half the game's, so a cycle renders in half the time (the measures here do not need the top octave).</summary>
    private const int LowRate = 22050;

    /// <summary>The game's rate, for the listening renders.</summary>
    private const int Rate = SkySynth.DefaultSampleRateHz;

    private static readonly WorldCharacter Hub = new(WorldKind.Hub, 0, 0, -1);
    private static readonly WorldCharacter Outskirts = new(WorldKind.Folder, 1, 1, -1);

    /// <summary>The Starter Sky's theme (B♭ major: the sample sky's root world is a Library world, as planets-music logs).</summary>
    private const PlanetTheme StarterSky = PlanetTheme.Library;

    private static readonly StarterTheme[] Written = [StarterTheme.Revised, StarterTheme.AuroraDrift, StarterTheme.Homecoming];

    /// <summary>One cycle of a piece at the hub's tempo, seconds (16 bars of 4 beats at 94 BPM: 40.9 s).</summary>
    private static readonly double CycleSeconds = SkySynth.CycleBars * 4 * 60 / StarterThemeScores.Bpm;

    /// <summary>One beat at the hub's tempo, seconds: a change of theme is heard within it.</summary>
    private static readonly double BeatSeconds = 60 / StarterThemeScores.Bpm;

    private static SkySynth StarterSynth(StarterTheme theme, int rate = LowRate, WorldCharacter? start = null, int seed = 2026)
    {
        var s = new SkySynth(seed, StarterSky, rate, start ?? Hub);
        s.SetHubTheme(theme);
        return s;
    }

    private static float[] RenderSeconds(SkySynth s, double seconds)
    {
        var all = new float[(int)Math.Round(seconds * s.SampleRateHz) * 2];
        const int chunk = 512 * 2;
        for (var done = 0; done < all.Length; done += chunk) s.Render(all.AsSpan(done, Math.Min(chunk, all.Length - done)));
        return all;
    }

    /// <summary>Renders in 10 ms steps until <paramref name="done"/> holds; the seconds it took, or null after <paramref name="limit"/>.</summary>
    private static double? SecondsUntil(SkySynth s, Func<bool> done, double limit)
    {
        var step = s.SampleRateHz / 100;
        for (var t = 0.0; t <= limit; t += 0.01)
        {
            if (done()) return t;
            s.Render(step);
        }

        return null;
    }

    [Fact]
    public void The_catalogue_has_the_classic_and_three_written_four_variation_cycles_at_the_hub_tempo()
    {
        StarterThemes.All.Should().Equal(StarterTheme.Classic, StarterTheme.Revised, StarterTheme.AuroraDrift, StarterTheme.Homecoming);
        ((int)StarterTheme.Classic).Should().Be(0, "the classic is the synth's default and keeps the hub's world key (and its audio) as it was");
        StarterThemes.PieceOf(StarterTheme.Classic).Should().BeNull("the classic is the synth's own generated hub music");
        StarterThemes.LevelOf(StarterTheme.Classic).Should().Be(1f);
        StarterThemes.GameDefault.Should().NotBe(StarterTheme.Classic, "AD-95: the game's default is the strongest candidate; the classic stays selectable");
        foreach (var t in StarterThemes.All)
        {
            StarterThemes.Title(t).Should().NotBeNullOrWhiteSpace();
            StarterThemes.Description(t).Should().EndWith(".");
            StarterThemes.FromKey(StarterThemes.Key(t)).Should().Be(t, "a stored choice reads back");
        }

        StarterThemes.FromKey(null).Should().BeNull();
        StarterThemes.FromKey("nursery").Should().BeNull();
        StarterThemes.FromKey("2").Should().BeNull("numbers are not names: a renumbering must never change a saved choice");
        StarterThemes.FromKey("homecoming").Should().Be(StarterTheme.Homecoming, "case does not matter");

        foreach (var t in Written)
        {
            var piece = StarterThemes.PieceOf(t)!;
            piece.BeatsPerMinute.Should().Be(SkySynth.TempoFor(MusicIdentity.Hub), $"{t} plays on the hub's clock");
            piece.Variations.Should().HaveCount(StarterThemeScores.Variations);
            piece.SectionStarts.Should().Equal(StarterThemes.SectionStarts);
            var prints = piece.Variations.Select(MusicActivityTests.Fingerprint).ToList();
            prints.Distinct().Should().HaveCount(prints.Count, $"{t}: every variation differs, so 16 bars come round again only after {StarterThemeScores.Variations} cycles");
            foreach (var v in piece.Variations)
            {
                MusicListening.PeakVoices(v, ringSeconds: 0.8).Should().BeLessThanOrEqualTo(SkySynth.MaxMusicVoices - 4, $"{v.Name} leaves room under the music's {SkySynth.MaxMusicVoices} voices");
                v.Notes.Should().OnlyContain(n => n.Start < TimeSpan.FromSeconds(CycleSeconds), "every note starts inside the cycle");
            }
        }
    }

    [Fact]
    public void The_starter_music_preference_defaults_to_the_game_default_round_trips_by_name_and_ignores_what_it_cannot_read()
    {
        Preferences.Default.StarterMusic.Should().Be(StarterThemes.GameDefault, "a new player, and an older file with no key, hear the chosen default");
        var classic = Preferences.Default with { StarterMusic = StarterTheme.Classic };
        classic.Should().NotBe(Preferences.Default, "equality includes the music");
        classic.ToJson().Should().Contain("\"starterMusic\": \"Classic\"", "stored by name");
        Preferences.FromJson(classic.ToJson(), out var problem).Should().Be(classic);
        problem.Should().BeNull();
        Preferences.FromJson("{\"version\":1,\"soundMuted\":true}", out _).StarterMusic.Should().Be(StarterThemes.GameDefault);
        Preferences.FromJson("{\"starterMusic\":\"nursery\"}", out _).StarterMusic.Should().Be(StarterThemes.GameDefault, "an unknown name is ignored");
        Preferences.FromJson("{\"starterMusic\":3}", out _).StarterMusic.Should().Be(StarterThemes.GameDefault, "a number is not a name");
        classic.Describe().Should().EndWith("; starter music Classic");
        Preferences.Default.Describe().Should().NotContain("starter", "the summary mentions only a choice away from the default");
    }

    [Fact]
    public void The_synth_plays_the_classic_hub_unless_told_otherwise_and_a_new_theme_starts_on_the_next_beat_and_back()
    {
        var s = new SkySynth(7, StarterSky, LowRate, Hub);
        s.HubTheme.Should().Be(StarterTheme.Classic, "Core's default: offline renders, tests and the behaviour baseline keep the classic");
        s.Render(LowRate);
        s.PieceVariation.Should().Be(-1, "the classic hub is generated, not a written piece");
        var root = SkySynth.KeyFor(StarterSky, Hub).RootMidiNote;
        foreach (var t in Written.Append(StarterTheme.Classic))
        {
            s.Render(LowRate / 3);
            s.SetHubTheme(t);
            var took = SecondsUntil(s, () => s.PlayingPieceName == StarterThemes.PieceOf(t)?.Name && s.PlayingFor > TimeSpan.Zero, 2);
            took.Should().NotBeNull($"{t} starts");
            took!.Value.Should().BeLessThanOrEqualTo(BeatSeconds + 0.02, $"{t}: a change of the player's choice is heard on the next beat");
            s.PlayingIdentity.Should().Be(MusicIdentity.Hub, "it is still the hub's music");
            s.PlayingKey.RootMidiNote.Should().Be(root, "in the Starter Sky's own root (B♭)");
            if (t != StarterTheme.Classic)
            {
                s.PlayingKey.Mode.Should().StartWith(Modes.Name(StarterThemes.PieceOf(t)!.Mode), "the effects are pitched in the piece's own mode");
                s.PieceVariation.Should().Be(0, "a first visit starts at the first variation");
                s.PlayingBar.Should().Be(0, "from its first bar");
            }

            s.TempoBeatsPerMinute.Should().Be(SkySynth.TempoFor(MusicIdentity.Hub));
        }
    }

    [Fact]
    public void Choosing_a_theme_elsewhere_changes_nothing_until_the_hub_music_begins_again()
    {
        var s = StarterSynth(StarterTheme.Classic, start: Outskirts);
        s.Render(LowRate);
        s.SetHubTheme(StarterTheme.AuroraDrift);
        s.Render(LowRate);
        s.PlayingIdentity.Should().Be(MusicIdentity.Outskirts, "a folder keeps its own music");
        s.PieceVariation.Should().Be(-1);
        s.SetWorld(StarterSky, Hub, 1);
        SecondsUntil(s, () => s.PlayingIdentity == MusicIdentity.Hub && s.PieceVariation >= 0, 2).Should().NotBeNull("arriving at the hub plays the chosen theme");

        // In the overview the chart plays on; leaving it brings the newly chosen hub theme.
        s.SetExperience(MusicExperience.Overview);
        SecondsUntil(s, () => s.PlayingIdentity == MusicIdentity.Overview, 2).Should().NotBeNull();
        s.SetHubTheme(StarterTheme.Homecoming);
        s.Render(LowRate);
        s.PlayingIdentity.Should().Be(MusicIdentity.Overview, "Star Chart Dorian is untouched by the hub's choice");
        s.SetExperience(MusicExperience.World);
        SecondsUntil(s, () => s.PlayingIdentity == MusicIdentity.Hub && s.PieceVariation >= 0, 2).Should().NotBeNull();
        s.PlayingKey.Mode.Should().StartWith("ionian", "Homecoming, not the Mixolydian drift chosen before");
    }

    [Fact]
    public void A_return_resumes_the_theme_at_its_next_phrase_and_a_later_visit_starts_a_later_variation()
    {
        var s = StarterSynth(StarterTheme.Homecoming);
        s.Render((int)(14 * LowRate)); // into bar 5 (2.55 s a bar)
        var leftBar = s.PlayingBar;
        s.SetWorld(StarterSky, Outskirts, 1);
        SecondsUntil(s, () => s.PlayingIdentity == MusicIdentity.Outskirts, 2).Should().NotBeNull();
        s.Render(5 * LowRate);
        s.SetWorld(StarterSky, Hub, 2);
        SecondsUntil(s, () => s.PlayingIdentity == MusicIdentity.Hub && s.PlayingFor > TimeSpan.Zero, 2).Should().NotBeNull();
        s.PieceVariation.Should().Be(0, "a remembered hub resumes in the cycle it was left in");
        (s.PlayingBar % 4).Should().Be(0, "on a phrase (every section is four bars)");
        s.PlayingBar.Should().BeGreaterThan(leftBar, $"after where it was left (bar {leftBar}), not from the top");

        foreach (var visits in new[] { 2, 3 })
        {
            var fresh = StarterSynth(StarterTheme.Homecoming, start: Outskirts);
            fresh.Render(LowRate);
            fresh.SetWorld(StarterSky, Hub, visits);
            SecondsUntil(fresh, () => fresh.PlayingIdentity == MusicIdentity.Hub && fresh.PlayingFor > TimeSpan.Zero, 2).Should().NotBeNull();
            fresh.PieceVariation.Should().Be(visits - 1, $"a forgotten hub on visit {visits} starts on a later variation, so a revisit is not the same opening again");
        }
    }

    [Fact]
    public void The_classic_hub_renders_bit_identically_whether_or_not_it_was_chosen_explicitly()
    {
        var plain = new SkySynth(11, StarterSky, LowRate, Hub);
        var chosen = new SkySynth(11, StarterSky, LowRate, Hub);
        chosen.SetHubTheme(StarterTheme.Classic);
        RenderSeconds(chosen, 3).Should().Equal(RenderSeconds(plain, 3));
        var away = new SkySynth(11, StarterSky, LowRate, Hub);
        away.SetHubTheme(StarterTheme.Revised);
        away.SetHubTheme(StarterTheme.Classic); // before the first downbeat: a retarget back to the classic
        RenderSeconds(away, 3).Should().Equal(RenderSeconds(new SkySynth(11, StarterSky, LowRate, Hub), 3));
    }

    [Fact]
    public void Every_theme_sits_with_the_classic_hub_in_loudness_without_clipping_or_running_out_of_voices()
    {
        var classic = RenderSeconds(StarterSynth(StarterTheme.Classic), CycleSeconds);
        var reference = MusicListening.Lufs(classic, LowRate);
        output.WriteLine(FormattableString.Invariant($"classic hub on the Starter Sky: {reference:0.0} LUFS, peak {MusicListening.Peak(classic):0.00} (one cycle, {LowRate} Hz)"));
        foreach (var t in Written)
        {
            var s = StarterSynth(t);
            var all = new float[(int)Math.Round(CycleSeconds * LowRate) * 2];
            var most = 0;
            for (var done = 0; done < all.Length; done += 1024)
            {
                s.Render(all.AsSpan(done, Math.Min(1024, all.Length - done)));
                most = Math.Max(most, s.ActiveMusicVoices);
            }

            var lufs = MusicListening.Lufs(all, LowRate);
            output.WriteLine(FormattableString.Invariant($"{t}: {lufs:0.0} LUFS ({lufs - reference:+0.0;-0.0} dB), peak {MusicListening.Peak(all):0.00}, most music voices {most}"));
            lufs.Should().BeApproximately(reference, 1.5, $"{t} is as loud as the classic hub (AD-52: every world within ≈ 2 dB)");
            MusicListening.Peak(all).Should().BeLessThan(0.5f, "music never reaches the limiter");
            most.Should().BeLessThan(SkySynth.MaxMusicVoices, $"{t} never fills the music's voices (a full pool steals notes)");
        }
    }

    [Fact]
    public void Over_four_cycles_each_written_theme_varies_more_and_repeats_less_than_the_classic_theme()
    {
        // The classic's melody: the theme as written every cycle (the variations ornament it; plain every fourth) plus the
        // music box between. Its foreground repeats the same eight bars each cycle: the measure the written themes must beat.
        const int bars = 4 * SkySynth.CycleBars;
        var classicTheme = new List<MusicListening.Onset>();
        for (var c = 0; c < 4; c++)
        {
            foreach (var n in SkySynth.ThemeMelody) classicTheme.Add(new MusicListening.Onset(c * 256L + n.StartSixteenths, 62 + Modes.SemitonesOf(DiatonicMode.Ionian, n.Degree)));
        }

        var classic = MusicListening.Melody(classicTheme, bars, StarterThemeScores.Bpm);
        output.WriteLine($"classic theme (as written, every cycle): {classic}");
        foreach (var t in Written)
        {
            var m = MusicListening.Melody(MusicListening.PieceMelody(StarterThemes.PieceOf(t)!, 4), bars, StarterThemeScores.Bpm);
            output.WriteLine($"{t}: {m}");
            m.RepeatedPhraseShare.Should().BeLessThan(classic.RepeatedPhraseShare, $"{t}: fewer four-bar phrases come back identical in 2 min 43 s");
            m.MelodicBarShare.Should().BeLessThanOrEqualTo(0.8, $"{t}: the tune rests in at least a fifth of the bars (room to breathe on a long stay)");
            m.MelodicBarShare.Should().BeGreaterThanOrEqualTo(0.3, $"{t}: but it is still a tune, not only a bed");
            m.PitchClassEntropy.Should().BeGreaterThanOrEqualTo(classic.PitchClassEntropy - 0.05, $"{t}: at least as many different notes as the pentatonic theme");
        }
    }

    // ---------------------------------------------------------------- the listening renders and the analysis (evidence)

    /// <summary>The gap between the comparison reel's excerpts, seconds (silence, so each starts clearly).</summary>
    private const double ReelGapSeconds = 1.5;

    /// <summary>Each excerpt's length in the reel, seconds (the brief: 20–30 s of each).</summary>
    private const double ExcerptSeconds = 25;

    /// <summary>The long renders' length for the analysis, seconds: four minutes, six cycles of a written theme.</summary>
    private const double AnalysisSeconds = 240;

    /// <summary>The revisit route: on the hub, then away in the outskirts, then back (seconds of each).</summary>
    private const double RevisitFirstStay = 110, RevisitAway = 25, RevisitSecondStay = 75;

    /// <summary>What the analysis renders: the four hub themes and the overview's chart (the quality reference), each from its first downbeat on the Starter Sky.</summary>
    private static readonly (string File, string Label, StarterTheme? Hub)[] Subjects =
    [
        ("classic", "Classic (the current Starter Sky)", StarterTheme.Classic),
        ("revised", "The theme, revised", StarterTheme.Revised),
        ("aurora-drift", "Aurora drift", StarterTheme.AuroraDrift),
        ("homecoming", "Homecoming", StarterTheme.Homecoming),
        ("star-chart-dorian", "Star Chart Dorian (the overview; reference)", null),
    ];

    /// <summary>Where each subject's reel excerpt starts in its render, seconds: a representative 25 s (Aurora drift's first cycle is its bed alone, so its excerpt is from the second cycle's third bar, where the lead sings).</summary>
    private static double ExcerptFrom(string file) => file == "aurora-drift" ? CycleSeconds + 2 * 4 * BeatSeconds : 0;

    private static SkySynth SubjectSynth(StarterTheme? hub)
    {
        var s = new SkySynth(2026, StarterSky, Rate, Hub) { MinimumDwell = SkySynth.GameMinimumDwell };
        if (hub is { } t) s.SetHubTheme(t);
        else s.SetExperience(MusicExperience.Overview);
        return s;
    }

    [SkySynthStarterFact]
    public void Render_the_starter_theme_listening_samples()
    {
        var dir = Environment.GetEnvironmentVariable("SKYSYNTH_STARTER_DIR")!;
        Directory.CreateDirectory(dir);
        var inv = CultureInfo.InvariantCulture;
        var metrics = new StringBuilder();
        metrics.AppendLine("Starter Sky music: listening analysis (AD-95). Each subject rendered 4 min from its first downbeat on the Starter Sky (B-flat), as the game plays it (seed 2026, the game's dwell), 44.1 kHz.");
        metrics.AppendLine("Audio measures: integrated loudness (LUFS), loudness range (LU, P95-P10 of 3 s windows), peak, onsets/s, brightness (second-difference energy share),");
        metrics.AppendLine(FormattableString.Invariant($"bars that sound like an earlier bar again (chroma cosine >= {MusicListening.SameBarSimilarity}), the mean best match, pitch-class entropy of the sound (bits, max 3.58)."));
        metrics.AppendLine("Melody measures (the foreground line's notes): bars with melody, distinct melodic bars, four-bar phrases repeating an earlier one exactly, pitch-class entropy, distinct rhythms, notes/min, leap share, range.");
        metrics.AppendLine();
        var renders = new Dictionary<string, float[]>();
        foreach (var (file, label, hub) in Subjects)
        {
            var s = SubjectSynth(hub);
            var melody = new List<MusicListening.Onset>();
            var fps = Rate * 60.0 / (StarterThemeScores.Bpm * 4);
            if (hub == StarterTheme.Classic)
            {
                s.MelodyObserver = n =>
                {
                    if (n.Part is MelodyPart.Theme or MelodyPart.Grace or MelodyPart.MusicBox or MelodyPart.Motif) melody.Add(new MusicListening.Onset((long)Math.Round(n.Frame / fps), n.MidiNote));
                };
            }

            var all = RenderSeconds(s, AnalysisSeconds);
            renders[file] = all;
            var bpm = hub == null ? ModeSketches.StarChartBpm : StarterThemeScores.Bpm;
            var barSeconds = 4 * 60 / bpm;
            var bars = (int)(AnalysisSeconds / barSeconds);
            if (hub is { } t && StarterThemes.PieceOf(t) is { } piece) melody = MusicListening.PieceMelody(piece, (int)Math.Ceiling(bars / 16.0));
            if (hub == null) melody = MusicListening.PieceMelody(ExperiencePieces.Overview, (int)Math.Ceiling(bars / 16.0));
            var chroma = MusicListening.BarChroma(all, Rate, barSeconds);
            var (same, best) = MusicListening.BarRecurrence(chroma);
            var m = MusicListening.Melody(melody, bars, bpm);
            metrics.AppendLine(label);
            metrics.AppendLine(string.Format(inv, "  audio: {0:0.0} LUFS, range {1:0.0} LU, peak {2:0.00}, onsets {3:0.00}/s, brightness {4:0.000}; same-sounding bars {5:P0}, mean best match {6:0.000}, sound pitch-class entropy {7:0.00} bits",
                MusicListening.Lufs(all, Rate), MusicListening.LoudnessRange(all, Rate), MusicListening.Peak(all), MusicListening.OnsetRate(all, Rate), MusicListening.Brightness(all), same, best, MusicListening.ChromaEntropy(chroma)));
            metrics.AppendLine(string.Format(inv, "  melody: in {0:P0} of bars, distinct bars {1:P0}, repeated 4-bar phrases {2:P0}, pitch-class entropy {3:0.00} bits, distinct rhythms {4:P0}, {5:0} notes/min, leaps {6:P0}, range {7} semitones",
                m.MelodicBarShare, m.DistinctBarShare, m.RepeatedPhraseShare, m.PitchClassEntropy, m.DistinctRhythmShare, m.NotesPerMinute, m.LeapShare, m.RangeSemitones));
            if (hub is { } tt && StarterThemes.PieceOf(tt) is { } p2)
            {
                // Per-variation loudness (a cycle each, from the 4 min render): how even the long form is.
                var per = Enumerable.Range(0, (int)(AnalysisSeconds / CycleSeconds)).Select(c => MusicListening.Lufs(all.AsSpan((int)(c * CycleSeconds * Rate) * 2, (int)(CycleSeconds * Rate) * 2).ToArray(), Rate));
                metrics.AppendLine("  per cycle (variations " + string.Join(" ", Enumerable.Range(0, (int)(AnalysisSeconds / CycleSeconds)).Select(c => p2.VariationOf(c) + 1)) + "): " + string.Join(", ", per.Select(x => x.ToString("0.0", inv))) + " LUFS");
                metrics.AppendLine(FormattableString.Invariant($"  exact repetition: the whole texture comes round again after {StarterThemeScores.Variations} cycles ({StarterThemeScores.Variations * CycleSeconds:0} s)"));
            }
            else if (hub == StarterTheme.Classic)
            {
                metrics.AppendLine(FormattableString.Invariant($"  exact repetition: the theme (8 bars, the same pitches) returns every cycle ({CycleSeconds:0.0} s), plain on cycles 1, 5, 9…; the music box between is a seeded random walk"));
            }
            else
            {
                metrics.AppendLine(FormattableString.Invariant($"  exact repetition: two variations, so the chart comes round again after {2 * SkySynth.CycleBars * 4 * 60 / ModeSketches.StarChartBpm:0} s"));
            }

            output.WriteLine(metrics.ToString());
        }

        // The comparison reel: ≈ 25 s of each, faded, with a gap between.
        var reel = new List<float>();
        var readme = new StringBuilder();
        readme.AppendLine("FileScape seventh pass: the Starter Sky's music, alternatives (AD-95, PLANET-169..173). Offline renders from Core (SkySynth, as the game plays it), B-flat (the Starter Sky's key).");
        readme.AppendLine("Rendered by: SKYSYNTH_STARTER_DIR=<this folder> dotnet test tests/FileScape.Core.Tests --filter Render_the_starter_theme_listening_samples");
        readme.AppendLine();
        readme.AppendLine("comparison-reel.wav, in order (start - end):");
        foreach (var (file, label, _) in Subjects)
        {
            var all = renders[file];
            var from = (int)(ExcerptFrom(file) * Rate);
            var n = (int)(ExcerptSeconds * Rate);
            var start = reel.Count / 2 / (double)Rate;
            for (var i = 0; i < n; i++)
            {
                var g = (float)Math.Min(1, Math.Min(i / (0.05 * Rate), (n - i) / (2.0 * Rate))); // a 50 ms fade in, a 2 s fade out
                reel.Add(all[2 * (from + i)] * g);
                reel.Add(all[2 * (from + i) + 1] * g);
            }

            readme.AppendLine(FormattableString.Invariant($"  {Clock(start)} - {Clock(start + ExcerptSeconds)}  {label}{(ExcerptFrom(file) > 0 ? FormattableString.Invariant($" (from {ExcerptFrom(file):0.0} s into it: its second cycle, where the lead sings)") : "")}"));
            reel.AddRange(new float[(int)(ReelGapSeconds * Rate) * 2]);
        }

        MusicListening.WriteWav(Path.Combine(dir, "comparison-reel.wav"), reel.ToArray(), Rate);

        // The long renders with a revisit: on the hub, away to an outskirts folder, back.
        readme.AppendLine();
        readme.AppendLine(FormattableString.Invariant($"long-<theme>-with-revisit.wav: {RevisitFirstStay:0} s on the Starter Sky, then {RevisitAway:0} s in an outskirts folder (its own music), then back for {RevisitSecondStay:0} s (the hub resumes at its next phrase)."));
        foreach (var (file, label, hub) in Subjects.Where(x => x.Hub != null))
        {
            var s = SubjectSynth(hub);
            var all = new List<float>(RenderSeconds(s, RevisitFirstStay));
            var leftAt = (s.PieceVariation, s.PlayingBar);
            s.SetWorld(StarterSky, Outskirts, 1);
            s.Arrive();
            all.AddRange(RenderSeconds(s, RevisitAway));
            s.SetWorld(StarterSky, Hub, 2);
            s.Arrive();
            var back = RenderSeconds(s, 1);
            var resumedAt = (s.PieceVariation, s.PlayingBar);
            all.AddRange(back);
            all.AddRange(RenderSeconds(s, RevisitSecondStay - 1));
            MusicListening.WriteWav(Path.Combine(dir, $"long-{file}-with-revisit.wav"), all.ToArray(), Rate);
            readme.AppendLine(FormattableString.Invariant($"  long-{file}-with-revisit.wav  {label}: left at variation {leftAt.Item1 + 1} bar {leftAt.Item2 + 1}; back at {Clock(RevisitFirstStay + RevisitAway)}, resumed at variation {resumedAt.Item1 + 1} bar {resumedAt.Item2 + 1}{(hub == StarterTheme.Classic ? " (the classic resumes on its next half-cycle; variation -1 means generated)" : "")}"));
        }

        // The aurora demonstration: the same phrase on the previous voices and on the aurora, alone and in the arrangement,
        // then the aurora with each feature off in turn (loudness-matched to the full aurora, so only the colour changes).
        readme.AppendLine();
        readme.AppendLine("aurora/: the aurora voice (AD-79) explained by ear; see aurora/README.txt.");
        var auroraDir = Path.Combine(dir, "aurora");
        Directory.CreateDirectory(auroraDir);
        var before = ModeSketches.AuroraComparison(aurora: false);
        var after = ModeSketches.AuroraComparison(aurora: true);
        SketchScore Only(SketchScore s, SketchPart part, string name) => new(name, [.. s.Notes.Where(n => n.Part == part)], s.Markers, s.Duration);
        var demo = new List<(string File, float[] Samples, string What)>
        {
            ("1-lead-solo-previous-horn", SkySynth.RenderSketch(Only(before, SketchPart.Melody, "lead-before"), Rate), "the line alone on the previous voice (the orchestra's horns: paired saws, a fixed filter swell)"),
            ("2-lead-solo-aurora", SkySynth.RenderSketch(Only(after, SketchPart.Melody, "lead-after"), Rate), "the same line alone on the aurora lead"),
            ("3-pad-solo-previous-strings", SkySynth.RenderSketch(Only(before, SketchPart.Chord, "pad-before"), Rate), "the chords alone on the previous pad (the string-section pad)"),
            ("4-pad-solo-aurora", SkySynth.RenderSketch(Only(after, SketchPart.Chord, "pad-after"), Rate), "the same chords alone on the aurora pad"),
            ("5-arrangement-previous", SkySynth.RenderSketch(before, Rate), "line, chords and bass together on the previous voices (horns, string pad, saw bass)"),
            ("6-arrangement-aurora", SkySynth.RenderSketch(after, Rate), "the same arrangement on the aurora voices (lead, pad, bass)"),
        };
        var leadRef = MusicListening.Lufs(demo[1].Samples, Rate);
        var padRef = MusicListening.Lufs(demo[3].Samples, Rate);
        var k = 7;
        foreach (var (feature, words) in new[]
                 {
                     (AuroraFeatures.Detune, "detune off (one saw, in tune)"), (AuroraFeatures.Drive, "drive off (at its minimum)"),
                     (AuroraFeatures.FilterMotion, "filter motion off (the cutoff held where it would settle)"), (AuroraFeatures.Resonance, "resonance off"),
                     (AuroraFeatures.Detune | AuroraFeatures.Drive | AuroraFeatures.FilterMotion | AuroraFeatures.Resonance, "everything off (a plain saw through a fixed filter)"),
                 })
        {
            var slug = feature.ToString().Replace(", ", "-", StringComparison.Ordinal).ToLowerInvariant();
            foreach (var (part, refLufs, role) in new[] { (SketchPart.Melody, leadRef, "lead"), (SketchPart.Chord, padRef, "pad") })
            {
                var x = SkySynth.RenderSketch(Only(after, part, role), feature, Rate);
                var gain = (float)Math.Pow(10, (refLufs - MusicListening.Lufs(x, Rate)) / 20);
                for (var i = 0; i < x.Length; i++) x[i] = Math.Clamp(x[i] * gain, -0.99f, 0.99f);
                demo.Add(($"{k++}-{role}-solo-aurora-{(feature.HasFlag(AuroraFeatures.Resonance) && feature != AuroraFeatures.Resonance ? "all-off" : slug + "-off")}", x, FormattableString.Invariant($"the aurora {role} alone, {words}; level matched to file {(part == SketchPart.Melody ? 2 : 4)} ({20 * Math.Log10(gain):+0.0;-0.0} dB)")));
            }
        }

        var aReadme = new StringBuilder();
        foreach (var (f, x, what) in demo)
        {
            MusicListening.WriteWav(Path.Combine(auroraDir, f + ".wav"), x, Rate);
            aReadme.AppendLine(FormattableString.Invariant($"{f}.wav ({x.Length / 2.0 / Rate:0.0} s, {MusicListening.Lufs(x, Rate):0.0} LUFS): {what}"));
        }

        File.WriteAllText(Path.Combine(auroraDir, "files.txt"), aReadme.ToString());
        File.WriteAllText(Path.Combine(dir, "metrics.txt"), metrics.ToString());
        File.WriteAllText(Path.Combine(dir, "files.txt"), readme.ToString());
        output.WriteLine(readme.ToString());
        output.WriteLine(aReadme.ToString());
    }

    private static string Clock(double seconds) => FormattableString.Invariant($"{(int)(seconds / 60)}:{seconds % 60:00.0}");
}
