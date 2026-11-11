using System.Diagnostics;
using FileScape.Core.Audio;
using FileScape.Core.Planets;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>Runs only when SKYSYNTH_WAV names an output path (a 30 s evidence render); otherwise skipped with a reason.</summary>
public sealed class SkySynthWavFactAttribute : FactAttribute
{
    public SkySynthWavFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKYSYNTH_WAV")))
        {
            Skip = "Set SKYSYNTH_WAV to a .wav path to render the 30 s SkySynth demo.";
        }
    }
}

/// <summary>Runs only when SKYSYNTH_THEME_DIR names an output folder (a 12 s listening clip per theme); otherwise skipped.</summary>
public sealed class SkySynthThemeClipsFactAttribute : FactAttribute
{
    public SkySynthThemeClipsFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKYSYNTH_THEME_DIR")))
        {
            Skip = "Set SKYSYNTH_THEME_DIR to a folder to render 12 s SkySynth clips per theme.";
        }
    }
}

/// <summary>The Planets audio synth (pure DSP): safety, determinism, latency, smooth crossfades and cost.</summary>
public class SkySynthTests(ITestOutputHelper output)
{
    private const int Rate = SkySynth.DefaultSampleRateHz;

    private static int Frames(double seconds) => (int)Math.Round(seconds * Rate);

    private static (float Peak, float MaxJump, double Rms) Measure(float[] s)
    {
        float peak = 0, jump = 0;
        double sum = 0;
        for (var i = 0; i < s.Length; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(s[i]));
            sum += s[i] * (double)s[i];
            if (i >= 2) jump = MathF.Max(jump, MathF.Abs(s[i] - s[i - 2])); // same channel, consecutive frames
        }

        return (peak, jump, Math.Sqrt(sum / Math.Max(1, s.Length)));
    }

    private static void FireEverything(SkySynth s)
    {
        s.Step(1);
        s.Jump();
        s.AirJump();
        s.Land(30);
        s.WindUp(TimeSpan.FromSeconds(0.6));
        s.Launch();
        s.Fly(0.5f);
        s.Arrive();
        s.Select();
        s.Inspect();
        s.Open();
        s.Deny();
        for (var i = 0; i < 6; i++) s.Chime(i);
    }

    [Fact]
    public void Stress_all_effects_at_once_with_music_and_flight_stays_finite_and_below_full_scale()
    {
        var s = new SkySynth(seed: 7, PlanetTheme.Wild);
        s.SetMasterVolume(1);
        s.SetFlight(1);
        float peak = 0;
        for (var round = 0; round < 12; round++)
        {
            FireEverything(s);
            FireEverything(s); // twice over: voices are stolen
            if (round % 4 == 3) s.SetTheme((PlanetTheme)(round % 11));
            var buf = s.Render(Frames(0.25));
            foreach (var x in buf)
            {
                float.IsFinite(x).Should().BeTrue();
                peak = MathF.Max(peak, MathF.Abs(x));
            }
        }

        output.WriteLine($"stress peak {peak:0.0000}");
        peak.Should().BeLessThan(1f);
        s.ActiveVoices.Should().BeLessThanOrEqualTo(SkySynth.MaxVoices);
    }

    [Fact]
    public void Every_theme_plays_music_at_a_moderate_level()
    {
        foreach (var theme in Enum.GetValues<PlanetTheme>())
        {
            var s = new SkySynth(seed: 3, theme);
            var (peak, _, rms) = Measure(s.Render(Frames(8)));
            output.WriteLine($"{theme,-9} {SkySynth.KeyFor(theme).Mode,-28} peak {peak:0.000} rms {rms:0.0000}");
            rms.Should().BeGreaterThan(0.003, $"{theme} should be audible");
            peak.Should().BeLessThan(0.6f, $"{theme}'s music should stay well below the effects' headroom");
        }
    }

    [Fact]
    public void Same_seed_and_calls_give_identical_samples_and_another_seed_differs()
    {
        float[] Run(int seed)
        {
            var s = new SkySynth(seed, PlanetTheme.Meadow);
            var all = new List<float>();
            all.AddRange(s.Render(Frames(1)));
            s.Step(0.7f);
            s.Chime(3);
            s.SetTheme(PlanetTheme.Library);
            all.AddRange(s.Render(Frames(1.5)));
            s.SetFlight(0.8f);
            s.Launch();
            all.AddRange(s.Render(Frames(1)));
            return [.. all];
        }

        Run(42).Should().Equal(Run(42));
        Run(42).Should().NotEqual(Run(43));
    }

    public static TheoryData<string, bool> Effects() => new()
    {
        { nameof(SkySynth.Step), false },
        { nameof(SkySynth.Jump), false },
        { nameof(SkySynth.AirJump), false },
        { nameof(SkySynth.Land), false },
        { nameof(SkySynth.WindUp), false },
        { nameof(SkySynth.Launch), false },
        { nameof(SkySynth.Fly), false },
        { nameof(SkySynth.Arrive), true },
        { nameof(SkySynth.Select), true },
        { nameof(SkySynth.Inspect), true },
        { nameof(SkySynth.Open), true },
        { nameof(SkySynth.Deny), true },
        { nameof(SkySynth.Chime), true },
    };

    private static void Fire(SkySynth s, string name)
    {
        switch (name)
        {
            case nameof(SkySynth.Step): s.Step(0.6f); break;
            case nameof(SkySynth.Jump): s.Jump(); break;
            case nameof(SkySynth.AirJump): s.AirJump(); break;
            case nameof(SkySynth.Land): s.Land(6); break;
            case nameof(SkySynth.WindUp): s.WindUp(TimeSpan.FromSeconds(0.8)); break;
            case nameof(SkySynth.Launch): s.Launch(); break;
            case nameof(SkySynth.Fly): s.Fly(0.3f); break;
            case nameof(SkySynth.Arrive): s.Arrive(); break;
            case nameof(SkySynth.Select): s.Select(); break;
            case nameof(SkySynth.Inspect): s.Inspect(); break;
            case nameof(SkySynth.Open): s.Open(); break;
            case nameof(SkySynth.Deny): s.Deny(); break;
            case nameof(SkySynth.Chime): s.Chime(2); break;
            default: throw new ArgumentException(name);
        }
    }

    [Theory]
    [MemberData(nameof(Effects))]
    public void Every_effect_sounds_within_its_latency_bound(string effect, bool quantised)
    {
        var bound = quantised ? SkySynth.QuantisedLatencyBound : SkySynth.ImmediateLatencyBound;
        var boundFrames = (int)(bound.TotalSeconds * Rate);
        var worst = 0;
        // Several start offsets, so the quantised ones meet the grid (a 32nd note: 3062.5 frames at 108 BPM) at different distances.
        foreach (var preroll in new[] { 0, 1, 1000, 2345, 3061, 3064, 5000, 6124, 6127, 7000 })
        {
            var s = new SkySynth(seed: 5);
            s.MusicVolume = 0; // isolate the effect
            s.Render(preroll);
            Fire(s, effect);
            var buf = s.Render(Frames(0.3));
            var first = -1;
            for (var i = 0; i < buf.Length; i++)
            {
                if (MathF.Abs(buf[i]) > 1e-4f)
                {
                    first = i / 2;
                    break;
                }
            }

            first.Should().BeGreaterThanOrEqualTo(0, $"{effect} should make a sound (preroll {preroll})");
            first.Should().BeLessThanOrEqualTo(boundFrames, $"{effect} after preroll {preroll}");
            worst = Math.Max(worst, first);
        }

        output.WriteLine($"{effect}: worst first-sound latency {worst * 1000.0 / Rate:0.0} ms (bound {bound.TotalMilliseconds} ms)");
    }

    [Fact]
    public void Wind_up_peaks_at_launch_time()
    {
        var s = new SkySynth(seed: 9) { MusicVolume = 0 };
        s.WindUp(TimeSpan.FromSeconds(1));
        var buf = s.Render(Frames(1.4));
        double Rms(double fromSeconds, double toSeconds)
        {
            double sum = 0;
            int a = Frames(fromSeconds) * 2, b = Frames(toSeconds) * 2;
            for (var i = a; i < b; i++) sum += buf[i] * (double)buf[i];
            return Math.Sqrt(sum / (b - a));
        }

        var early = Rms(0.2, 0.3);
        var peak = Rms(0.9, 1.0);
        var after = Rms(1.25, 1.35);
        output.WriteLine($"wind-up rms early {early:0.0000} at launch {peak:0.0000} after {after:0.0000}");
        peak.Should().BeGreaterThan(early * 2);
        peak.Should().BeGreaterThan(after * 2);
    }

    [Fact]
    public void Arrive_has_a_second_softer_chime_after_the_chord()
    {
        // The chime is the only bell; measure energy above 2 kHz-ish via first differences around +120 ms.
        var s = new SkySynth(seed: 2) { MusicVolume = 0 };
        s.Arrive();
        var buf = s.Render(Frames(0.4));
        double Hf(int from, int to)
        {
            double e = 0;
            for (var i = from * 2 + 2; i < to * 2; i += 2) e += Math.Pow(buf[i] - buf[i - 2], 2);
            return e / (to - from);
        }

        var start = Array.FindIndex(buf, x => MathF.Abs(x) > 1e-4f) / 2;
        var before = Hf(start + Frames(0.06), start + Frames(0.115));
        var chime = Hf(start + Frames(0.125), start + Frames(0.18));
        output.WriteLine($"arrive high-frequency energy before {before:E2} with chime {chime:E2}");
        chime.Should().BeGreaterThan(before * 1.5);
    }

    [Fact]
    public void Theme_crossfade_has_no_clicks()
    {
        var s = new SkySynth(seed: 11, PlanetTheme.Hearth);
        var steady = Measure(s.Render(Frames(4)));
        float worst = 0;
        foreach (var theme in new[] { PlanetTheme.Crystal, PlanetTheme.Vault, PlanetTheme.Quiet, PlanetTheme.Veiled, PlanetTheme.Chime })
        {
            s.SetTheme(theme);
            var fade = Measure(s.Render(Frames(0.7)));
            s.SetTheme(theme == PlanetTheme.Chime ? PlanetTheme.Hearth : theme); // a change mid-fade too, sometimes
            var rest = Measure(s.Render(Frames(2.5)));
            worst = MathF.Max(worst, MathF.Max(fade.MaxJump, rest.MaxJump));
        }

        output.WriteLine($"max sample-to-sample jump: steady {steady.MaxJump:0.0000}, across crossfades {worst:0.0000}");
        worst.Should().BeLessThan(0.1f, "a click is a large sample-to-sample jump");
        worst.Should().BeLessThan(steady.MaxJump * 3 + 0.01f);
    }

    [Fact]
    public void Mute_and_volume_ramp_without_clicks()
    {
        var s = new SkySynth(seed: 4, PlanetTheme.Chime);
        s.Render(Frames(2));
        s.Mute(true);
        var muting = Measure(s.Render(Frames(0.3)));
        var silent = Measure(s.Render(Frames(0.5)));
        s.Mute(false);
        s.SetMasterVolume(0.3f);
        var back = Measure(s.Render(Frames(1)));
        silent.Peak.Should().BeLessThan(1e-4f);
        muting.MaxJump.Should().BeLessThan(0.1f);
        back.Rms.Should().BeGreaterThan(0.0005);
    }

    [Fact]
    public void Rendering_one_second_of_music_with_six_effects_is_cheap()
    {
        var best = double.MaxValue;
        for (var run = 0; run < 5; run++)
        {
            var s = new SkySynth(seed: run, PlanetTheme.Wild);
            s.SetFlight(0.7f);
            s.Render(Frames(3)); // warm up: pad, melody and flight layers sounding
            var buffer = new float[Frames(1) * 2];
            var sw = Stopwatch.StartNew();
            s.Launch();
            s.Arrive();
            s.AirJump();
            s.WindUp(TimeSpan.FromSeconds(0.5));
            s.Chime(1);
            s.Open();
            for (var chunk = 0; chunk < 20; chunk++)
            {
                s.Fly(chunk / 20f);
                s.Render(buffer.AsSpan(chunk * buffer.Length / 20, buffer.Length / 20));
            }

            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            output.WriteLine($"run {run}: {sw.Elapsed.TotalMilliseconds:0.00} ms for 1 s of audio, {s.ActiveVoices} voices at the end");
        }

        output.WriteLine($"best {best:0.00} ms per second of audio");
        // 2026-11-11: 17–19 ms alone, 28 ms best-of-5 while other builds ran in parallel (a flaky 25 ms bound). The real-time
        // budget is 1000 ms per second of audio; 60 ms (6 % of one core, Debug) still catches an order-of-magnitude regression.
        best.Should().BeLessThan(60);
    }

    [Fact]
    public void Keys_follow_the_themes()
    {
        SkySynth.KeyFor(PlanetTheme.Hearth).Should().Match<MusicalKey>(k => k.RootMidiNote % 12 == 5 && k.Mode == "lydian");
        SkySynth.KeyFor(PlanetTheme.Library).RootMidiNote.Should().Be(58); // B♭
        SkySynth.KeyFor(PlanetTheme.Theatre).Mode.Should().Be("dorian");
        foreach (var theme in Enum.GetValues<PlanetTheme>())
        {
            var k = SkySynth.KeyFor(theme);
            k.IntervalsSemitones[0].Should().Be(0);
            k.IntervalsSemitones.Should().BeInAscendingOrder();
        }

        var s = new SkySynth();
        s.SetTheme(PlanetTheme.Crystal);
        s.Theme.Should().Be(PlanetTheme.Crystal);
        s.Key.Mode.Should().Be("lydian");
    }

    // ---------------------------------------------------------------- tempo and the FileScape theme

    private const double FramesPerSixteenth = Rate * 60.0 / (SkySynth.TempoBeatsPerMinute * 4.0);
    private const int LowRate = 8000;
    private const double LowRateFramesPerSixteenth = LowRate * 60.0 / (SkySynth.TempoBeatsPerMinute * 4.0);

    private static int Midi(MusicalKey key, int degree)
    {
        var len = key.IntervalsSemitones.Count;
        var octave = (int)Math.Floor(degree / (double)len);
        return key.RootMidiNote + 12 + key.IntervalsSemitones[degree - octave * len] + 12 * octave;
    }

    /// <summary>
    /// The fraction of the written theme's notes (in a 7-note key) found among <paramref name="onsets"/> from bar
    /// <paramref name="startBar"/>: an onset of the same pitch class within ± 2 sixteenths (an octave shift, an
    /// anticipation or a delay still count; an ornament adds notes but keeps the theme's).
    /// </summary>
    private static double ThemeMatch(IReadOnlyList<MelodyNote> onsets, MusicalKey key, int startBar)
    {
        var found = 0;
        foreach (var t in SkySynth.ThemeMelody)
        {
            var expected = (startBar * 16 + t.StartSixteenths) * FramesPerSixteenth;
            var pitchClass = Midi(key, t.Degree) % 12;
            if (onsets.Any(o => Math.Abs(o.Frame - expected) <= 2 * FramesPerSixteenth + 1 && o.MidiNote % 12 == pitchClass)) found++;
        }

        return found / (double)SkySynth.ThemeMelody.Count;
    }

    [Fact]
    public void The_tempo_is_lively_and_effects_keep_to_its_32nd_note_grid()
    {
        SkySynth.TempoBeatsPerMinute.Should().Be(108f);
        var thirtySecond = TimeSpan.FromSeconds(60.0 / SkySynth.TempoBeatsPerMinute / 8);
        SkySynth.QuantisedLatencyBound.Should().BeGreaterThanOrEqualTo(thirtySecond).And.BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(90));

        // A tempo-locked effect starts exactly on a 32nd-note grid point.
        var s = new SkySynth(seed: 5) { MusicVolume = 0 };
        s.Render(1234);
        s.Select();
        var buf = s.Render(Frames(0.2));
        var first = Array.FindIndex(buf, x => MathF.Abs(x) > 1e-6f) / 2 + 1234;
        var grid = FramesPerSixteenth / 2;
        var nearest = Math.Round(first / grid) * grid;
        Math.Abs(first - nearest).Should().BeLessThan(8, "the select tick starts on the 32nd-note grid (allowing its attack)");
    }

    [Fact]
    public void The_written_theme_is_8_bars_built_from_a_motif_and_its_answer()
    {
        var theme = SkySynth.ThemeMelody;
        theme[^1].StartSixteenths.Should().Be(7 * 16, "the cadence's last note starts bar 8");
        theme.Sum(n => n.LengthSixteenths).Should().Be(7 * 16 + 8);
        int[] Bar(int b) => [.. theme.Where(n => n.StartSixteenths / 16 == b).Select(n => n.LengthSixteenths)];
        Bar(2).Should().Equal(Bar(0), "the answer starts with the motif's rhythm");
        Bar(4).Should().Equal(Bar(0), "bar 5 raises the motif in the same rhythm");
        theme.Where(n => n.StartSixteenths < 16).Select(n => n.Degree).Take(3)
            .Should().Equal(theme.Where(n => n.StartSixteenths is >= 32 and < 48).Select(n => n.Degree).Take(3), "the answer begins as the motif did");
        theme[^1].Degree.Should().Be(0, "it ends on the tonic");
        foreach (var n in theme) (n.Degree % 7).Should().BeOneOf([0, 1, 2, 4, 5], "the theme uses the pentatonic subset, so it maps into every key");
    }

    [Fact]
    public void The_theme_returns_every_16_bars_with_variations_and_the_free_bars_between_are_not_the_theme()
    {
        foreach (var theme in new[] { PlanetTheme.Hearth, PlanetTheme.Workshop, PlanetTheme.Library })
        {
            var key = SkySynth.KeyFor(theme);
            var s = new SkySynth(seed: 2026, theme);
            var lead = new List<MelodyNote>();
            var all = new List<MelodyNote>();
            s.MelodyObserver = n =>
            {
                all.Add(n);
                if (n.Part == MelodyPart.Theme) lead.Add(n);
            };
            var bars = 64;
            var total = (int)Math.Round(bars * 16 * FramesPerSixteenth);
            var buffer = new float[4096 * 2];
            for (var done = 0; done < total; done += 4096) s.Render(buffer);

            var returns = 0;
            var variations = new HashSet<ThemeVariation>();
            var fingerprints = new HashSet<string>();
            for (var cycle = 0; cycle < bars / SkySynth.CycleBars; cycle++)
            {
                var startBar = cycle * SkySynth.CycleBars;
                var match = ThemeMatch(lead, key, startBar);
                // The free half of the cycle, with every melody note (music box included), must not read as the theme.
                var freeMatch = ThemeMatch(all, key, startBar + SkySynth.ThemeLengthBars);
                var window = lead.Where(n => n.Frame >= startBar * 16 * FramesPerSixteenth && n.Frame < (startBar + 10) * 16 * FramesPerSixteenth).ToList();
                var variation = window.Count > 0 ? window[0].Variation : ThemeVariation.Plain;
                output.WriteLine($"{theme} cycle {cycle} (bar {startBar + 1}): {variation,-16} theme match {match:P0}, free bars {freeMatch:P0}, {window.Count} lead notes");
                freeMatch.Should().BeLessThan(0.5, $"the free bars after cycle {cycle} are not the theme");
                if (match >= 0.7)
                {
                    returns++;
                    variations.Add(variation);
                    fingerprints.Add(string.Join(",", window.Select(n => $"{Math.Round((n.Frame - startBar * 16 * FramesPerSixteenth) / FramesPerSixteenth)}:{n.MidiNote}")));
                }
            }

            ThemeMatch(lead, key, 0).Should().Be(1, $"{theme}: the first return is the theme as written");
            returns.Should().BeGreaterThanOrEqualTo(3, $"{theme}: the theme recurs");
            variations.Count.Should().BeGreaterThanOrEqualTo(2, $"{theme}: the returns vary");
            fingerprints.Count.Should().BeGreaterThanOrEqualTo(2, $"{theme}: the variations sound different");
        }
    }

    [Fact]
    public void Every_variation_keeps_the_theme_recognisable_in_every_key()
    {
        // Over many cycles each variation appears; in each key the lead plays it in the scale, starting on the bar line.
        var seen = new HashSet<ThemeVariation>();
        foreach (var theme in Enum.GetValues<PlanetTheme>())
        {
            var key = SkySynth.KeyFor(theme);
            // 8 kHz: the notes are what is checked here, and the low rate keeps 12 cycles per key quick.
            var s = new SkySynth(seed: (int)theme * 17 + 3, theme, sampleRateHz: LowRate);
            var lead = new List<MelodyNote>();
            s.MelodyObserver = n =>
            {
                if (n.Part is MelodyPart.Theme or MelodyPart.Grace or MelodyPart.Counter) lead.Add(n);
            };
            var buffer = new float[4096 * 2];
            var total = (int)Math.Round(12 * 16 * 16 * LowRateFramesPerSixteenth); // 12 cycles
            for (var done = 0; done < total; done += 4096) s.Render(buffer);

            var scale = key.IntervalsSemitones.Select(x => (x + key.RootMidiNote) % 12).ToHashSet();
            lead.Should().OnlyContain(n => scale.Contains(n.MidiNote % 12), $"{theme}'s lead stays in {key.Mode}");
            lead.Should().OnlyContain(n => n.MidiNote >= 40 && n.MidiNote <= 100, $"{theme}'s lead stays in a singable bell range");
            foreach (var n in lead) seen.Add(n.Variation);
        }

        seen.Should().BeEquivalentTo(Enum.GetValues<ThemeVariation>());
    }

    [Fact]
    public void A_new_world_greets_you_with_the_plain_theme_on_the_next_bar_line()
    {
        var s = new SkySynth(seed: 8, PlanetTheme.Hearth);
        s.Render(Frames(5.3));
        var called = s.FramesRendered;
        var first = (MelodyNote?)null;
        s.MelodyObserver = n =>
        {
            if (first == null && n.Part == MelodyPart.Theme) first = n;
        };
        s.SetTheme(PlanetTheme.Meadow);
        s.Render(Frames(4));
        var bar = 16 * FramesPerSixteenth;
        first.Should().NotBeNull();
        first!.Value.Variation.Should().Be(ThemeVariation.Plain);
        var expected = (long)Math.Ceiling(Math.Ceiling(called / bar) * bar - 1e-6);
        first.Value.Frame.Should().BeCloseTo(expected, 2);
        (first.Value.Frame - called).Should().BeLessThanOrEqualTo((long)bar, "within one bar (2.2 s)");
    }

    [Fact]
    public void Variations_are_deterministic_per_seed_and_cycle()
    {
        List<ThemeVariation> Run(int seed)
        {
            var s = new SkySynth(seed, PlanetTheme.Chime, sampleRateHz: LowRate);
            var list = new List<ThemeVariation>();
            s.MelodyObserver = n =>
            {
                if (n.Part == MelodyPart.Theme && (list.Count == 0 || list[^1] != n.Variation)) list.Add(n.Variation);
            };
            var buffer = new float[4096 * 2];
            var total = (int)Math.Round(8 * 16 * 16 * LowRateFramesPerSixteenth);
            for (var done = 0; done < total; done += 4096) s.Render(buffer);
            return list;
        }

        var one = Run(1);
        one.Should().Equal(Run(1));
        one[0].Should().Be(ThemeVariation.Plain);
        var two = Run(2);
        two.Should().NotEqual(one, "another seed varies differently");
        output.WriteLine($"seed 1: {string.Join(", ", one)}; seed 2: {string.Join(", ", two)}");
    }

    [SkySynthWavFact]
    public void Render_30_second_demo_to_wav()
    {
        var path = Environment.GetEnvironmentVariable("SKYSYNTH_WAV")!;
        var s = new SkySynth(seed: 2026, PlanetTheme.Hearth);
        LogThemeStatements(s);
        var samples = new List<float>(Rate * 2 * 30);
        var t = 0.0;
        void Until(double seconds, Action<double>? perChunk = null)
        {
            const int chunk = 735; // one 60 Hz frame
            while (t < seconds)
            {
                perChunk?.Invoke(t);
                samples.AddRange(s.Render(chunk));
                t += chunk / (double)Rate;
            }
        }

        // 0–6 s: Hearth, walking.
        var nextStep = 1.0;
        Until(4, now =>
        {
            if (now >= nextStep)
            {
                s.Step(0.6f);
                nextStep += 0.36;
            }
        });
        s.Jump();
        Until(4.5);
        s.AirJump();
        Until(5.2);
        s.Land(9);
        Until(6);
        // 6–9 s: a wind-up, launch and flight to a Meadow world.
        s.WindUp(TimeSpan.FromSeconds(0.8));
        Until(6.8);
        s.Launch();
        var flightStart = t;
        Until(8.8, now =>
        {
            var p = (float)((now - flightStart) / 2.0);
            s.Fly(p);
            s.SetFlight(MathF.Sin(MathF.PI * Math.Clamp(p, 0, 1)));
            if (p > 0.5f) s.SetTheme(PlanetTheme.Meadow);
        });
        s.Fly(1);
        s.SetFlight(0);
        s.Arrive();
        Until(10);
        // 10–15 s: looking around: select, inspect, open, a denial.
        s.Select();
        Until(11);
        s.Inspect();
        Until(12);
        s.Open();
        Until(13.5);
        s.Deny();
        Until(15);
        // 15–21 s: the Chime world, walking past bell flowers.
        s.SetTheme(PlanetTheme.Chime);
        var nextChime = 16.0;
        var chimeIndex = 0;
        Until(21, now =>
        {
            if (now >= nextChime)
            {
                s.Chime(chimeIndex++);
                s.Step(0.5f);
                nextChime += 0.6;
            }
        });
        // 21–30 s: Crystal, then Quiet and Veiled.
        s.SetTheme(PlanetTheme.Crystal);
        Until(25);
        s.SetTheme(PlanetTheme.Quiet);
        Until(27.5);
        s.SetTheme(PlanetTheme.Veiled);
        Until(30);

        WriteWav(path, samples, Rate);
        var (peak, _, rms) = Measure([.. samples]);
        output.WriteLine($"wrote {path}: {samples.Count / 2 / (double)Rate:0.0} s, peak {peak:0.000} ({Db(peak):0.0} dBFS), rms {rms:0.0000} ({Db(rms):0.0} dBFS)");
        peak.Should().BeLessThan(1f);
    }

    private static double Db(double linear) => 20 * Math.Log10(Math.Max(1e-9, linear));

    /// <summary>
    /// A 12 s listening clip per theme (Hearth, Meadow, Workshop, Chime): 8 s of music alone, then the launch cues
    /// (a 0.8 s wind-up, the launch, flight wind) and an arrival with a bell-flower chime, to judge the tune and the mix.
    /// </summary>
    [SkySynthThemeClipsFact]
    public void Render_12_second_theme_clips_to_wav()
    {
        var dir = Environment.GetEnvironmentVariable("SKYSYNTH_THEME_DIR")!;
        Directory.CreateDirectory(dir);
        foreach (var theme in new[] { PlanetTheme.Hearth, PlanetTheme.Meadow, PlanetTheme.Workshop, PlanetTheme.Chime })
        {
            var s = new SkySynth(seed: 2026, theme);
            LogThemeStatements(s, theme.ToString());
            var samples = new List<float>(Rate * 2 * 12);
            var t = 0.0;
            void Until(double seconds, Action<double>? perChunk = null)
            {
                const int chunk = 735;
                while (t < seconds)
                {
                    perChunk?.Invoke(t);
                    samples.AddRange(s.Render(chunk));
                    t += chunk / (double)Rate;
                }
            }

            Until(8);
            s.WindUp(TimeSpan.FromSeconds(0.8));
            Until(8.8);
            s.Launch();
            var flightStart = t;
            Until(10.3, now =>
            {
                var p = (float)((now - flightStart) / 1.5);
                s.Fly(p);
                s.SetFlight(MathF.Sin(MathF.PI * Math.Clamp(p, 0, 1)));
            });
            s.Fly(1);
            s.SetFlight(0);
            s.Arrive();
            Until(11);
            s.Chime(2);
            Until(12);

            var path = Path.Combine(dir, $"{theme.ToString().ToLowerInvariant()}-12s.wav");
            WriteWav(path, samples, Rate);
            var (peak, _, rms) = Measure([.. samples]);
            output.WriteLine($"wrote {path}: peak {peak:0.000} ({Db(peak):0.0} dBFS), rms {rms:0.0000} ({Db(rms):0.0} dBFS)");
            peak.Should().BeLessThan(1f);
        }

        // The theme's returns: 40 bars (≈ 89 s) of Hearth music alone, three statements of the theme.
        var hearth = new SkySynth(seed: 2026, PlanetTheme.Hearth);
        LogThemeStatements(hearth, "returns");
        var frames = (int)Math.Round(40 * 16 * FramesPerSixteenth);
        var returns = new List<float>(frames * 2);
        for (var done = 0; done < frames; done += 4096) returns.AddRange(hearth.Render(Math.Min(4096, frames - done)));
        var returnsPath = Path.Combine(dir, "hearth-theme-returns-89s.wav");
        WriteWav(returnsPath, returns, Rate);
        var m = Measure([.. returns]);
        output.WriteLine($"wrote {returnsPath}: peak {m.Peak:0.000} ({Db(m.Peak):0.0} dBFS), rms {m.Rms:0.0000} ({Db(m.Rms):0.0} dBFS)");
        m.Peak.Should().BeLessThan(1f);
    }

    /// <summary>Writes to the test output where each statement of the theme starts (seconds) and which variation it is.</summary>
    private void LogThemeStatements(SkySynth s, string label = "demo")
    {
        var last = long.MinValue / 2;
        var world = s.Theme;
        s.MelodyObserver = n =>
        {
            if (n.Part != MelodyPart.Theme) return;
            if (n.Frame - last > 16 * FramesPerSixteenth || s.Theme != world) output.WriteLine($"{label}: {s.Theme} theme at {n.Frame / (double)Rate:0.00} s ({n.Variation})");
            world = s.Theme;
            last = n.Frame;
        };
    }

    private static void WriteWav(string path, List<float> interleaved, int rate)
    {
        using var w = new BinaryWriter(File.Create(path));
        var dataBytes = interleaved.Count * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1); // PCM
        w.Write((short)2); // stereo
        w.Write(rate);
        w.Write(rate * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var x in interleaved) w.Write((short)Math.Round(Math.Clamp(x, -1f, 1f) * 32767));
    }
}
