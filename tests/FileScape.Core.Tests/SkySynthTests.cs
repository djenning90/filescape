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
        // Several start offsets, so the quantised ones meet the grid at different distances.
        foreach (var preroll in new[] { 0, 1, 1000, 2345, 3900, 3939, 5000, 7000, 7874, 7876 })
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
        best.Should().BeLessThan(25);
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

    [SkySynthWavFact]
    public void Render_30_second_demo_to_wav()
    {
        var path = Environment.GetEnvironmentVariable("SKYSYNTH_WAV")!;
        var s = new SkySynth(seed: 2026, PlanetTheme.Hearth);
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
        output.WriteLine($"wrote {path}: {samples.Count / 2 / (double)Rate:0.0} s, peak {peak:0.000}, rms {rms:0.0000}");
        peak.Should().BeLessThan(1f);
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
