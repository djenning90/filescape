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

/// <summary>Runs only when SKYSYNTH_EVIDENCE_DIR names an output folder (the per-world clips and routes of PLANET-28); otherwise skipped.</summary>
public sealed class SkySynthEvidenceFactAttribute : FactAttribute
{
    public SkySynthEvidenceFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKYSYNTH_EVIDENCE_DIR")))
        {
            Skip = "Set SKYSYNTH_EVIDENCE_DIR to a folder to render the SkySynth world clips, the journey and the quick hops.";
        }
    }
}

/// <summary>Runs only when SKYSYNTH_ARRANGEMENTS_DIR names an output folder (PLANET-35's listening samples, AD-52); otherwise skipped.</summary>
public sealed class SkySynthArrangementsFactAttribute : FactAttribute
{
    public SkySynthArrangementsFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKYSYNTH_ARRANGEMENTS_DIR")))
        {
            Skip = "Set SKYSYNTH_ARRANGEMENTS_DIR to a folder to render the AD-52 arrangement and hazard samples.";
        }
    }
}

/// <summary>The Planets audio synth (pure DSP; PLANET-23, PLANET-28, AD-49): safety, determinism, latency, smooth crossfades, the worlds' music and cost.</summary>
public partial class SkySynthTests(ITestOutputHelper output)
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
        { nameof(SkySynth.BellToll), true },
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
            case nameof(SkySynth.BellToll): s.BellToll(); break;
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
        // Several start offsets, so the quantised ones meet the grid (a 32nd note: 3518.6 frames at the hub's 94 BPM) at different distances.
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
            // The busiest world: the underglow inside a project (16th hats and bass, arpeggio, the project's echo).
            var s = new SkySynth(seed: run, PlanetTheme.Wild, Rate, new WorldCharacter(WorldKind.Folder, 6, 4, 42, false));
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

    // ---------------------------------------------------------------- worlds and tempo (PLANET-28, AD-49)

    private const int LowRate = 8000;
    private static readonly WorldCharacter Hub = new(WorldKind.Hub, 0, 0, -1);
    private static readonly WorldCharacter Windows = new(WorldKind.WindowsRoot, 0, 0, -1);
    private static readonly WorldCharacter Wsl = new(WorldKind.WslRoot, 0, 0, -1);
    private static readonly WorldCharacter Repo = new(WorldKind.Repository, 3, 0, 42);
    private static readonly WorldCharacter Worktree = new(WorldKind.Repository, 3, 0, 43, Worktree: true);
    private static readonly WorldCharacter HazardWorld = new(WorldKind.Hazard, 2, 0, -1);

    private static WorldCharacter Band(int band, int repositoryId = -1) =>
        new(WorldKind.Folder, band switch { 1 => 1, 2 => 2, 3 => 4, _ => 6 }, band, repositoryId);

    /// <summary>One representative character per identity.</summary>
    public static TheoryData<MusicIdentity> Identities() => new(Enum.GetValues<MusicIdentity>());

    private static WorldCharacter CharacterOf(MusicIdentity id) => id switch
    {
        MusicIdentity.Hub => Hub,
        MusicIdentity.WindowsRoot => Windows,
        MusicIdentity.WslRoot => Wsl,
        MusicIdentity.Repository => Repo,
        MusicIdentity.Hazard or MusicIdentity.Abyss => HazardWorld,
        _ => Band(id - MusicIdentity.Outskirts + 1),
    };

    /// <summary>
    /// A synth playing <paramref name="id"/> from its first downbeat: its representative character, and for a score with no
    /// world kind of its own (the abyss, AD-60) that score chosen before the first downbeat (a retarget, so nothing else plays).
    /// </summary>
    private static SkySynth SynthFor(int seed, PlanetTheme theme, MusicIdentity id, float musicVolume = 1f, int rate = Rate)
    {
        var s = new SkySynth(seed, theme, rate, CharacterOf(id)) { MusicVolume = musicVolume };
        if (SkySynth.IdentityOf(CharacterOf(id)) != id) s.SetWorld(theme, CharacterOf(id), 1, id);
        return s;
    }

    private static double FramesPerSixteenth(MusicIdentity id, int rate = Rate) => rate * 60.0 / (SkySynth.TempoFor(id) * 4.0);

    private static float[] RenderSeconds(SkySynth s, double seconds)
    {
        var all = new float[Frames(seconds) * 2];
        for (var done = 0; done < all.Length; done += 735 * 2) s.Render(all.AsSpan(done, Math.Min(735 * 2, all.Length - done)));
        return all;
    }

    [Fact]
    public void Each_identity_has_its_tempo_the_hub_calm_and_the_journey_quicker()
    {
        foreach (var id in Enum.GetValues<MusicIdentity>()) output.WriteLine($"{id,-12} {SkySynth.TempoFor(id)} BPM");
        SkySynth.TempoFor(MusicIdentity.Hub).Should().BeInRange(90, 96, "a calm arrival");
        // AD-49's tempos (hub 94, Windows volume 116, WSL 112, repository 126 BPM) with a few BPM of room either side.
        SkySynth.TempoFor(MusicIdentity.WindowsRoot).Should().BeInRange(110, 118);
        SkySynth.TempoFor(MusicIdentity.WslRoot).Should().BeInRange(110, 118);
        SkySynth.TempoFor(MusicIdentity.Repository).Should().BeInRange(122, 128);
        var bands = new[] { MusicIdentity.Outskirts, MusicIdentity.Trails, MusicIdentity.DeepWoods, MusicIdentity.Underglow }.Select(SkySynth.TempoFor).ToList();
        bands.Should().BeInAscendingOrder("deeper is more adventurous, not slower").And.OnlyContain(b => b >= 120);
        // AD-52: the festival at big-room tempo, the orchestra's ostinato at 144 under a 72 BPM theme (AD-66: 156 under 78), drum and bass at 172 under an 86 BPM theme.
        SkySynth.TempoFor(MusicIdentity.Trails).Should().BeInRange(124, 128);
        SkySynth.TempoFor(MusicIdentity.DeepWoods).Should().BeInRange(150, 162, "AD-66 lifted the orchestra from 144 by a modest 8 %");
        SkySynth.TempoFor(MusicIdentity.Underglow).Should().BeInRange(170, 175);
        SkySynth.ThemeTempoFor(MusicIdentity.Underglow).Should().Be(SkySynth.TempoFor(MusicIdentity.Underglow) / 2, "drum and bass: the theme at half time");
        SkySynth.ThemeTempoFor(MusicIdentity.Trails).Should().Be(SkySynth.TempoFor(MusicIdentity.Trails));
        // The abyss (AD-52's hazard score, kept by AD-60): a slow, heavy theme (half time) on a pulse quick enough for the effects' grid.
        SkySynth.ThemeTempoFor(MusicIdentity.Abyss).Should().BeLessThan(SkySynth.ThemeTempoFor(MusicIdentity.Hub), "the abyss is the slowest, heaviest theme");
        SkySynth.TempoFor(MusicIdentity.Abyss).Should().BeGreaterThanOrEqualTo(SkySynth.TempoFor(MusicIdentity.Hub));
        // AD-60: the hazard world's own score is frenetic, "get out of here": the theme at full tempo, the fastest theme of all.
        SkySynth.TempoFor(MusicIdentity.Hazard).Should().BeInRange(148, 160);
        SkySynth.ThemeTempoFor(MusicIdentity.Hazard).Should().Be(SkySynth.TempoFor(MusicIdentity.Hazard), "the hazard theme runs at its full pulse");
        Enum.GetValues<MusicIdentity>().Where(i => i != MusicIdentity.Hazard).Should().OnlyContain(i => SkySynth.ThemeTempoFor(i) < SkySynth.ThemeTempoFor(MusicIdentity.Hazard), "no theme is more urgent than the hazard world's");
        SkySynth.TempoFor(MusicIdentity.WindowsRoot).Should().NotBe(SkySynth.TempoFor(MusicIdentity.WslRoot));

        // The synth follows: the old tempo until the new world's first downbeat (≤ one beat), then the new one.
        var s = new SkySynth(seed: 1, PlanetTheme.Hearth, LowRate);
        s.TempoBeatsPerMinute.Should().Be(SkySynth.TempoFor(MusicIdentity.Hub));
        s.Render(LowRate);
        s.SetWorld(PlanetTheme.Meadow, Band(4), visits: 1);
        s.Identity.Should().Be(MusicIdentity.Underglow);
        s.TempoBeatsPerMinute.Should().Be(SkySynth.TempoFor(MusicIdentity.Hub), "the hub plays on to its next beat");
        s.Render((int)(LowRate * 60 / SkySynth.TempoFor(MusicIdentity.Hub)) + 64);
        s.TempoBeatsPerMinute.Should().Be(SkySynth.TempoFor(MusicIdentity.Underglow));
    }

    [Fact]
    public void Characters_map_to_identities()
    {
        SkySynth.IdentityOf(Hub).Should().Be(MusicIdentity.Hub);
        SkySynth.IdentityOf(Windows).Should().Be(MusicIdentity.WindowsRoot);
        SkySynth.IdentityOf(Wsl).Should().Be(MusicIdentity.WslRoot);
        SkySynth.IdentityOf(Repo).Should().Be(MusicIdentity.Repository);
        SkySynth.IdentityOf(Worktree).Should().Be(MusicIdentity.Repository);
        SkySynth.IdentityOf(Band(1)).Should().Be(MusicIdentity.Outskirts);
        SkySynth.IdentityOf(Band(2, repositoryId: 42)).Should().Be(MusicIdentity.Trails, "inside a project a folder keeps its band (and an echo of the project)");
        SkySynth.IdentityOf(Band(3)).Should().Be(MusicIdentity.DeepWoods);
        SkySynth.IdentityOf(Band(4)).Should().Be(MusicIdentity.Underglow);
        SkySynth.IdentityOf(new WorldCharacter(WorldKind.Folder, 0, 0, -1)).Should().Be(MusicIdentity.Outskirts);
        SkySynth.IdentityOf(HazardWorld).Should().Be(MusicIdentity.Hazard);
        SkySynth.KeyFor(PlanetTheme.Hearth, HazardWorld).Mode.Should().StartWith("phrygian");
        Enum.GetValues<WorldKind>().Select(k => SkySynth.IdentityOf(new WorldCharacter(k, 2, 1, -1))).Should().NotContain(MusicIdentity.Abyss, "the abyss is kept for a future world type (AD-60): no world plays it yet");
        SkySynth.KeyFor(PlanetTheme.Hearth, Windows).Mode.Should().StartWith("lydian");
        SkySynth.KeyFor(PlanetTheme.Hearth, Wsl).Mode.Should().StartWith("dorian");
        SkySynth.KeyFor(PlanetTheme.Meadow, Repo).RootMidiNote.Should().Be(SkySynth.KeyFor(PlanetTheme.Meadow).RootMidiNote, "the theme keeps the root");
        SkySynth.KeyFor(PlanetTheme.Meadow, Band(1)).Should().BeEquivalentTo(SkySynth.KeyFor(PlanetTheme.Meadow));
    }

    /// <summary>The first frame (after <paramref name="from"/>) where |x| exceeds the threshold, or −1.</summary>
    private static long FirstSound(float[] buffer, long from, float threshold = 1e-6f)
    {
        var i = Array.FindIndex(buffer, x => MathF.Abs(x) > threshold);
        return i < 0 ? -1 : from + i / 2;
    }

    [Theory]
    [MemberData(nameof(Identities))]
    public void Tempo_locked_effects_land_on_the_32nd_grid_of_every_identity_within_the_bound(MusicIdentity id)
    {
        var grid = FramesPerSixteenth(id) / 2;
        var bound = (long)(SkySynth.QuantisedLatencyBound.TotalSeconds * Rate);
        SkySynth.QuantisedLatencyBound.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(grid / Rate)).And.BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(90));
        var worst = 0L;
        foreach (var preroll in new[] { 0, 1, 1234, (int)grid - 1, (int)grid + 2, 5000, 9999 })
        {
            var s = SynthFor(5, PlanetTheme.Hearth, id, musicVolume: 0);
            s.Render(preroll);
            s.Select();
            var first = FirstSound(s.Render(Frames(0.2)), preroll);
            var nearest = Math.Round(first / grid) * grid;
            Math.Abs(first - nearest).Should().BeLessThan(8, $"{id}: the tick starts on its 32nd-note grid (preroll {preroll})");
            (first - preroll).Should().BeLessThanOrEqualTo(bound);
            worst = Math.Max(worst, first - preroll);
        }

        output.WriteLine($"{id}: 32nd {grid * 1000 / Rate:0.0} ms, worst wait {worst * 1000.0 / Rate:0.0} ms (bound {SkySynth.QuantisedLatencyBound.TotalMilliseconds} ms)");
    }

    /// <summary>The frame of the first beat at or after <paramref name="frame"/> on a clock starting at <paramref name="origin"/>.</summary>
    private static long NextBeat(long frame, long origin, double framesPerSixteenth)
    {
        var k = (long)Math.Ceiling((frame - origin) / (4 * framesPerSixteenth) - 1e-9);
        return origin + (long)Math.Ceiling(4 * k * framesPerSixteenth - 1e-6);
    }

    [Fact]
    public void Right_after_a_tempo_change_effects_follow_the_old_grid_then_the_new_one_from_its_first_downbeat()
    {
        var hub = FramesPerSixteenth(MusicIdentity.Hub);
        var glow = FramesPerSixteenth(MusicIdentity.Underglow);
        var s = new SkySynth(seed: 3, PlanetTheme.Hearth, Rate, Hub) { MusicVolume = 0 };
        s.Render(Frames(3.3));
        var called = s.FramesRendered;
        s.SetWorld(PlanetTheme.Crystal, Band(4), visits: 1);
        var origin = NextBeat(called, 0, hub);
        output.WriteLine($"called at {called}, the new world's first downbeat at {origin} (+{(origin - called) * 1000.0 / Rate:0} ms)");
        (origin - called).Should().BeLessThanOrEqualTo((long)(4 * hub) + 1, "within one beat of the hub");

        // Before the downbeat: the hub's grid.
        s.Select();
        var before = FirstSound(s.Render(Frames(0.1)), called, 1e-4f);
        if (before < origin) (Math.Abs(before - Math.Round(before / (hub / 2)) * (hub / 2))).Should().BeLessThan(8);
        else before.Should().Be(origin, "it never waits past the new downbeat");

        // After it: the new world's grid, counted from its downbeat, at its tempo.
        var quietUntil = origin + Frames(1.5); // past the first tick's reverb tail
        s.Render((int)(quietUntil - s.FramesRendered) + 777);
        var at = s.FramesRendered;
        s.TempoBeatsPerMinute.Should().Be(SkySynth.TempoFor(MusicIdentity.Underglow));
        s.Select();
        var after = FirstSound(s.Render(Frames(0.1)), at, 1e-4f);
        var rel = after - origin;
        Math.Abs(rel - Math.Round(rel / (glow / 2)) * (glow / 2)).Should().BeLessThan(8, "the tick is on the underglow's 32nd grid");
        (after - at).Should().BeLessThanOrEqualTo((long)(glow / 2) + 1);
    }

    [Theory]
    [MemberData(nameof(Identities))]
    public void AlignToGrid_ends_a_wind_up_on_a_sixteenth_at_every_tempo(MusicIdentity id)
    {
        var sixteenth = FramesPerSixteenth(id);
        foreach (var preroll in new[] { 0, 777, 4321, 12345 })
        {
            var s = SynthFor(2, PlanetTheme.Wild, id);
            s.Render(preroll);
            var wanted = TimeSpan.FromSeconds(0.75);
            var aligned = s.AlignToGrid(wanted, TimeSpan.FromSeconds(0.09));
            var end = preroll + aligned.TotalSeconds * Rate;
            var offGrid = Math.Abs(end - Math.Round(end / sixteenth) * sixteenth);
            offGrid.Should().BeLessThan(1.5, $"{id}: the launch lands on a sixteenth (preroll {preroll})");
            Math.Abs((aligned - wanted).TotalSeconds).Should().BeLessThanOrEqualTo(sixteenth / 2 / Rate + 1e-4, "never more than half a sixteenth");
        }

        new SkySynth(seed: 2).AlignToGrid(TimeSpan.FromSeconds(0.75), TimeSpan.Zero).Should().BeCloseTo(TimeSpan.FromSeconds(0.75), TimeSpan.FromMilliseconds(0.1),
            "with no room to shift, the duration is kept");
    }

    [Fact]
    public void AlignToGrid_uses_the_new_world_s_grid_when_the_launch_falls_after_its_first_downbeat()
    {
        var hub = FramesPerSixteenth(MusicIdentity.Hub);
        var repo = FramesPerSixteenth(MusicIdentity.Repository);
        var s = new SkySynth(seed: 2, PlanetTheme.Hearth, Rate, Hub);
        s.Render(Frames(2.05));
        var called = s.FramesRendered;
        s.SetWorld(PlanetTheme.Workshop, Repo, visits: 1);
        var origin = NextBeat(called, 0, hub);
        var aligned = s.AlignToGrid(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(0.09));
        var end = called + aligned.TotalSeconds * Rate;
        end.Should().BeGreaterThan(origin);
        var rel = end - origin;
        Math.Abs(rel - Math.Round(rel / repo) * repo).Should().BeLessThan(1.5, "on the repository's sixteenths, from its downbeat");
    }

    [Fact]
    public void Eight_worlds_in_two_seconds_stay_within_the_voice_budget_bounded_and_click_free()
    {
        WorldCharacter[] route = [Band(1), Band(2), Repo, Band(2, 42), Band(3), Wsl, Windows, Band(4)];
        PlanetTheme[] themes = [PlanetTheme.Meadow, PlanetTheme.Library, PlanetTheme.Workshop, PlanetTheme.Workshop, PlanetTheme.Theatre, PlanetTheme.Hearth, PlanetTheme.Crystal, PlanetTheme.Vault];
        var s = new SkySynth(seed: 21, PlanetTheme.Hearth, Rate, Hub);
        s.SetMasterVolume(1);
        var steady = Measure(RenderSeconds(s, 3));
        float peak = 0, jump = 0;
        var maxMusic = 0;
        var maxVoices = 0;
        double sum = 0;
        var n = 0;
        for (var i = 0; i < 12 * 60; i++) // 12 s in 60 Hz frames
        {
            if (i < 120 && i % 15 == 0)
            {
                s.SetWorld(themes[i / 15], route[i / 15], visits: 1); // 8 calls within 2 s
                s.Arrive();
            }

            var buf = s.Render(735);
            foreach (var x in buf)
            {
                float.IsFinite(x).Should().BeTrue();
                peak = MathF.Max(peak, MathF.Abs(x));
                sum += x * (double)x;
                n++;
            }

            jump = MathF.Max(jump, Measure(buf).MaxJump);
            maxMusic = Math.Max(maxMusic, s.ActiveMusicVoices);
            maxVoices = Math.Max(maxVoices, s.ActiveVoices);
        }

        var settled = Measure(RenderSeconds(s, 4));
        output.WriteLine($"steady jump {steady.MaxJump:0.000}, settled jump {settled.MaxJump:0.000}; hops: peak {peak:0.000}, jump {jump:0.000}, rms {Math.Sqrt(sum / n):0.0000} (settled {settled.Rms:0.0000}), music voices ≤ {maxMusic}, voices ≤ {maxVoices}");
        peak.Should().BeLessThan(1f);
        maxMusic.Should().BeLessThanOrEqualTo(SkySynth.MaxMusicVoices);
        maxVoices.Should().BeLessThanOrEqualTo(SkySynth.MaxVoices);
        jump.Should().BeLessThan(MathF.Max(steady.MaxJump, settled.MaxJump) * 1.6f + 0.02f, "no clicks beyond the music's own transients");
        Math.Sqrt(sum / n).Should().BeLessThan(settled.Rms * 2, "hops do not pile up into mush");
        s.Identity.Should().Be(MusicIdentity.Underglow);
    }

    [Fact]
    public void A_world_route_renders_identically_for_a_seed_and_differently_for_another()
    {
        float[] Run(int seed)
        {
            var s = new SkySynth(seed, PlanetTheme.Hearth, LowRate, Hub);
            var all = new List<float>();
            all.AddRange(s.Render(LowRate));
            s.SetWorld(PlanetTheme.Hearth, Wsl, 1);
            s.Arrive();
            all.AddRange(s.Render(LowRate * 2));
            s.WindUp(s.AlignToGrid(TimeSpan.FromSeconds(0.75), TimeSpan.FromSeconds(0.09)));
            all.AddRange(s.Render(LowRate));
            s.Launch();
            s.SetFlight(0.8f);
            all.AddRange(s.Render(LowRate));
            s.SetFlight(0);
            s.SetWorld(PlanetTheme.Workshop, Repo, 1);
            s.SetWorld(PlanetTheme.Workshop, Band(2, 42), 1);
            all.AddRange(s.Render(LowRate * 3));
            s.SetWorld(PlanetTheme.Workshop, Repo, 2);
            all.AddRange(s.Render(LowRate * 3));
            return [.. all];
        }

        Run(42).Should().Equal(Run(42));
        Run(42).Should().NotEqual(Run(43));
    }

    [Fact]
    public void Going_back_before_the_new_downbeat_changes_nothing()
    {
        var plain = new SkySynth(seed: 6, PlanetTheme.Hearth, LowRate, Band(2));
        var changed = new SkySynth(seed: 6, PlanetTheme.Hearth, LowRate, Band(2));
        var a = new List<float>(plain.Render(LowRate * 2));
        var b = new List<float>(changed.Render(LowRate * 2));
        changed.SetWorld(PlanetTheme.Vault, Band(3), 1);
        changed.SetWorld(PlanetTheme.Hearth, Band(2), 1); // Backspace before the deep woods' first downbeat
        changed.Identity.Should().Be(MusicIdentity.Trails);
        a.AddRange(plain.Render(LowRate * 4));
        b.AddRange(changed.Render(LowRate * 4));
        b.Should().Equal(a);
    }

    /// <summary>Energy above ≈ 7 kHz (first differences of first differences), as a fraction of the total: hats, shakers, glass.</summary>
    private static double HighShare(float[] s)
    {
        double hf = 0, all = 0;
        for (var i = 4; i < s.Length; i++)
        {
            var d2 = s[i] - 2 * s[i - 2] + s[i - 4];
            hf += d2 * (double)d2;
            all += s[i] * (double)s[i];
        }

        return hf / Math.Max(1e-12, all);
    }

    /// <summary>Onsets per second: 10 ms frames whose high-passed energy jumps above 1.6× the previous three (and a floor).</summary>
    private static double OnsetRate(float[] s)
    {
        const int hop = Rate / 100;
        var frames = s.Length / 2;
        var e = new List<double>();
        for (var f = 0; f + hop <= frames; f += hop)
        {
            double x = 0;
            for (var i = Math.Max(1, f); i < f + hop; i++)
            {
                var hp = s[2 * i] - s[2 * i - 2];
                x += hp * (double)hp;
            }

            e.Add(x);
        }

        var floor = e.Average() * 0.2;
        var onsets = 0;
        for (var i = 3; i < e.Count; i++)
        {
            if (e[i] > floor && e[i] > 1.6 * (e[i - 1] + e[i - 2] + e[i - 3]) / 3 && e[i] >= e[i - 1])
            {
                onsets++;
                i += 4;
            }
        }

        return onsets / (frames / (double)Rate);
    }

    [Fact]
    public void The_identities_sound_measurably_different_and_the_hub_is_the_calm_one()
    {
        var features = new Dictionary<MusicIdentity, (float Bpm, double Onsets, double High, double Rms)>();
        foreach (var id in Enum.GetValues<MusicIdentity>())
        {
            var s = SynthFor(2026, PlanetTheme.Hearth, id);
            var buf = RenderSeconds(s, 16);
            var f = (SkySynth.TempoFor(id), OnsetRate(buf), HighShare(buf), Measure(buf).Rms);
            features[id] = f;
            output.WriteLine($"{id,-12} {f.Item1,5} BPM, {f.Item2,5:0.0} onsets/s, high share {f.Item3:0.0000}, rms {f.Item4:0.0000}");
        }

        var hub = features[MusicIdentity.Hub];
        foreach (var (id, f) in features)
        {
            if (id == MusicIdentity.Hub) continue;
            // The orchestra (AD-52) moves on its string ostinato under a broad, legato half-time theme; this detector (jumps in
            // the left channel's high frequencies) mostly hears the sustained sections over the spiccato, so it is held to
            // more onsets than the hub rather than twice as many (a measurement limit, not a judgement by ear).
            var factor = id == MusicIdentity.DeepWoods ? 1.1 : 2;
            f.Onsets.Should().BeGreaterThan(hub.Onsets * factor, $"{id} has more bounce and momentum than the hub");
            f.Bpm.Should().BeGreaterThan(hub.Bpm + 10);
        }

        var ids = features.Keys.ToList();
        foreach (var a in ids)
        {
            foreach (var b in ids.Where(b => b > a))
            {
                var (fa, fb) = (features[a], features[b]);
                var differ = Math.Abs(fa.Bpm - fb.Bpm) >= 4 || Math.Abs(fa.Onsets - fb.Onsets) >= 0.15 * Math.Max(fa.Onsets, fb.Onsets)
                    || Math.Abs(Math.Log(fa.High / fb.High)) >= Math.Log(1.25);
                differ.Should().BeTrue($"{a} and {b} differ in tempo, onset density or brightness");
            }
        }

        (features[MusicIdentity.WindowsRoot].High / features[MusicIdentity.WslRoot].High).Should().BeGreaterThan(1.5, "Windows is the glassy one, WSL the warm one");
    }

    [Theory]
    [InlineData(MusicIdentity.Trails)]
    [InlineData(MusicIdentity.Repository)]
    [InlineData(MusicIdentity.Hub)]
    public void A_third_visit_develops_the_world_audibly(MusicIdentity id)
    {
        (float[] Audio, int Counter) Run(int visits)
        {
            var s = new SkySynth(seed: 12, PlanetTheme.Library, Rate, Band(1));
            s.SetWorld(PlanetTheme.Library, CharacterOf(id), visits, id); // retargets before the first downbeat
            s.Development.Should().Be(Math.Min(3, visits));
            var counter = 0;
            s.MelodyObserver = n =>
            {
                if (n.Part == MelodyPart.Counter) counter++;
            };
            return (RenderSeconds(s, 40), counter);
        }

        var first = Run(1);
        var third = Run(3);
        var a = Measure(first.Audio);
        var b = Measure(third.Audio);
        double diff = 0;
        for (var i = 0; i < first.Audio.Length; i++) diff += Math.Pow(first.Audio[i] - third.Audio[i], 2);
        var rel = Math.Sqrt(diff / first.Audio.Length) / a.Rms;
        output.WriteLine($"{id}: visit 1 rms {a.Rms:0.0000}, high {HighShare(first.Audio):0.0000}, counter notes {first.Counter}; visit 3 rms {b.Rms:0.0000}, high {HighShare(third.Audio):0.0000}, counter notes {third.Counter}; difference {rel:P0} of the music");
        rel.Should().BeGreaterThan(0.3, "a revisit is a different rendering, not the same one");
        third.Counter.Should().BeGreaterThan(first.Counter, "a counter-line joins on later visits");
        if (id != MusicIdentity.Hub) HighShare(third.Audio).Should().BeGreaterThan(HighShare(first.Audio), "fuller drums on the third visit");
    }

    [Fact]
    public void Returning_to_a_world_resumes_it_on_its_next_half_cycle_instead_of_starting_cold()
    {
        var s = new SkySynth(seed: 4, PlanetTheme.Meadow, Rate, Band(2));
        RenderSeconds(s, 6); // inside the theme's first statement
        s.SetWorld(PlanetTheme.Workshop, Repo, 1);
        RenderSeconds(s, 6);
        var parts = new List<MelodyNote>();
        s.MelodyObserver = parts.Add;
        var back = s.FramesRendered;
        s.SetWorld(PlanetTheme.Meadow, Band(2), 2);
        RenderSeconds(s, 4);
        var bar = 16 * FramesPerSixteenth(MusicIdentity.Trails);
        var repoBeat = 4 * FramesPerSixteenth(MusicIdentity.Repository); // the repository plays on to its next beat: its notes are not the return's
        var firstBars = parts.Where(n => n.Frame > back + repoBeat + 1 && n.Frame < back + bar * 2.5).ToList();
        output.WriteLine($"after the return: {string.Join(", ", firstBars.Take(8).Select(n => n.Part))}");
        firstBars.Should().NotBeEmpty();
        firstBars.Should().NotContain(n => n.Part == MelodyPart.Theme, "it resumes in its own material (bar 9), not at the top of the theme");
        firstBars.Should().Contain(n => n.Part == MelodyPart.Motif, "the band motif continues");
    }

    [Fact]
    public void The_same_world_again_keeps_the_groove_on_its_clock()
    {
        var s = new SkySynth(seed: 4, PlanetTheme.Meadow, Rate, Band(2));
        var notes = new List<MelodyNote>();
        s.MelodyObserver = notes.Add;
        RenderSeconds(s, 3);
        s.SetWorld(PlanetTheme.Meadow, Band(2) with { Depth = 3 }, 1); // a sibling folder of the same band and theme
        s.Identity.Should().Be(MusicIdentity.Trails);
        RenderSeconds(s, 3);
        var sixteenth = FramesPerSixteenth(MusicIdentity.Trails);
        notes.Where(n => n.Part == MelodyPart.Theme).Should().OnlyContain(n => Math.Abs(n.Frame / sixteenth - Math.Round(n.Frame / sixteenth)) * sixteenth < 2,
            "the theme carries on from the same downbeat");
        notes.Count(n => n.Part == MelodyPart.Theme && n.Frame < 8).Should().Be(1, "it was not restarted");
    }

    [Theory]
    [MemberData(nameof(Identities))]
    public void Launch_and_landing_cues_stand_clear_of_every_world_s_music(MusicIdentity id)
    {
        // The cue alone (music silent: the voices and their allocation are the same) against the music as the cue ducks it
        // (the full render minus the cue alone), in the 0.3 s after a landing and launch, at three moments of the bar.
        foreach (var at in new[] { 4.0, 4.08, 4.31 })
        {
            float[] Run(float music)
            {
                var s = SynthFor(30, PlanetTheme.Hearth, id, musicVolume: music);
                s.Render(Frames(at));
                s.Land(6);
                s.Launch();
                return s.Render(Frames(0.3));
            }

            var cue = Run(0);
            var with = Run(1);
            double c = 0, m = 0;
            for (var i = 0; i < cue.Length; i++)
            {
                c += cue[i] * (double)cue[i];
                m += Math.Pow(with[i] - cue[i], 2);
            }

            var snr = 10 * Math.Log10(c / Math.Max(1e-12, m));
            output.WriteLine($"{id} at {at} s: cue {snr:+0.0;-0.0} dB over the ducked music");
            snr.Should().BeGreaterThan(4, $"{id}'s drums must not mask the cues");
        }
    }

    [Fact]
    public void The_hub_asks_a_question_and_never_cadences_on_the_tonic()
    {
        var key = SkySynth.KeyFor(PlanetTheme.Hearth);
        var s = new SkySynth(seed: 7, PlanetTheme.Hearth, LowRate, Hub);
        var lead = new List<MelodyNote>();
        s.MelodyObserver = n =>
        {
            if (n.Part == MelodyPart.Theme) lead.Add(n);
        };
        var fps = FramesPerSixteenth(MusicIdentity.Hub, LowRate);
        var buffer = new float[4096 * 2];
        for (var done = 0; done < 3 * 256 * fps; done += 4096) s.Render(buffer);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var last = lead.Where(n => n.Frame < (cycle + 1) * 256 * fps).Last();
            output.WriteLine($"cycle {cycle}: the theme ends on MIDI {last.MidiNote} ({last.Variation})");
            (last.MidiNote % 12).Should().NotBe(key.RootMidiNote % 12, "the hub's phrase stays open: the journeys are ahead");
        }
    }

    // ---------------------------------------------------------------- the FileScape theme

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
    private static double ThemeMatch(IReadOnlyList<MelodyNote> onsets, MusicalKey key, int startBar, double framesPerSixteenth)
    {
        var found = 0;
        foreach (var t in SkySynth.ThemeMelody)
        {
            var expected = (startBar * 16 + t.StartSixteenths) * framesPerSixteenth;
            var pitchClass = Midi(key, t.Degree) % 12;
            if (onsets.Any(o => Math.Abs(o.Frame - expected) <= 2 * framesPerSixteenth + 1 && o.MidiNote % 12 == pitchClass)) found++;
        }

        return found / (double)SkySynth.ThemeMelody.Count;
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

    [Theory]
    [InlineData(MusicIdentity.Outskirts)]
    [InlineData(MusicIdentity.Repository)]
    [InlineData(MusicIdentity.WindowsRoot)]
    public void The_theme_returns_every_16_bars_with_variations_and_the_bars_between_are_not_the_theme(MusicIdentity id)
    {
        var fps = FramesPerSixteenth(id);
        foreach (var theme in new[] { PlanetTheme.Hearth, PlanetTheme.Workshop, PlanetTheme.Library })
        {
            var character = CharacterOf(id);
            var key = SkySynth.KeyFor(theme, character);
            var s = SynthFor(2026, theme, id);
            var intro = id == MusicIdentity.Repository ? 1 : 0; // the repository's arrival bar comes first
            var lead = new List<MelodyNote>();
            var all = new List<MelodyNote>();
            s.MelodyObserver = n =>
            {
                all.Add(n);
                if (n.Part == MelodyPart.Theme) lead.Add(n);
            };
            var bars = 64 + intro;
            var total = (int)Math.Round(bars * 16 * fps);
            var buffer = new float[4096 * 2];
            for (var done = 0; done < total; done += 4096) s.Render(buffer);

            var returns = 0;
            var variations = new HashSet<ThemeVariation>();
            var fingerprints = new HashSet<string>();
            for (var cycle = 0; cycle < 4; cycle++)
            {
                var startBar = intro + cycle * SkySynth.CycleBars;
                var match = ThemeMatch(lead, key, startBar, fps);
                var freeMatch = ThemeMatch(all, key, startBar + SkySynth.ThemeLengthBars, fps);
                var window = lead.Where(n => n.Frame >= startBar * 16 * fps && n.Frame < (startBar + 10) * 16 * fps).ToList();
                var variation = window.Count > 0 ? window[0].Variation : ThemeVariation.Plain;
                output.WriteLine($"{id} {theme} cycle {cycle} (bar {startBar + 1}): {variation,-16} theme match {match:P0}, between {freeMatch:P0}, {window.Count} lead notes");
                // The world's own motifs grow from the theme's head (the family's common thread) and are dense, so a loose match
                // finds some of it; but the bars between never count as a return, and the theme's lead is silent there.
                freeMatch.Should().BeLessThan(0.7, $"the bars after cycle {cycle} are the world's own material, not the theme");
                var between = (startBar + SkySynth.ThemeLengthBars + 1) * 16 * fps;
                lead.Should().NotContain(n => n.Frame >= between && n.Frame < (startBar + SkySynth.CycleBars) * 16 * fps);
                if (match >= 0.7)
                {
                    returns++;
                    variations.Add(variation);
                    fingerprints.Add(string.Join(",", window.Select(n => $"{Math.Round((n.Frame - startBar * 16 * fps) / fps)}:{n.MidiNote}")));
                }
            }

            ThemeMatch(lead, key, intro, fps).Should().Be(1, $"{theme}: the first return is the theme as written");
            returns.Should().BeGreaterThanOrEqualTo(3, $"{theme}: the theme recurs");
            variations.Count.Should().BeGreaterThanOrEqualTo(2, $"{theme}: the returns vary");
            fingerprints.Count.Should().BeGreaterThanOrEqualTo(2, $"{theme}: the variations sound different");
        }
    }

    [Fact]
    public void Every_variation_keeps_the_theme_recognisable_in_every_key()
    {
        // Over many cycles each variation appears; in each key the lead plays it in the scale.
        var seen = new HashSet<ThemeVariation>();
        var fps = FramesPerSixteenth(MusicIdentity.Outskirts, LowRate);
        foreach (var theme in Enum.GetValues<PlanetTheme>())
        {
            foreach (var character in new[] { Band(1), Band(4), Wsl })
            {
                var key = SkySynth.KeyFor(theme, character);
                // 8 kHz: the notes are what is checked here, and the low rate keeps 12 cycles per key quick.
                var s = new SkySynth(seed: (int)theme * 17 + 3, theme, LowRate, character);
                var lead = new List<MelodyNote>();
                s.MelodyObserver = n =>
                {
                    if (n.Part is MelodyPart.Theme or MelodyPart.Grace or MelodyPart.Counter) lead.Add(n);
                };
                var buffer = new float[4096 * 2];
                var total = (int)Math.Round(12 * 16 * 16 * fps); // ≈ 12 cycles
                for (var done = 0; done < total; done += 4096) s.Render(buffer);

                var scale = key.IntervalsSemitones.Select(x => (x + key.RootMidiNote) % 12).ToHashSet();
                lead.Should().OnlyContain(n => scale.Contains(n.MidiNote % 12), $"{theme}'s lead stays in {key.Mode}");
                lead.Should().OnlyContain(n => n.MidiNote >= 40 && n.MidiNote <= 100, $"{theme}'s lead stays in a singable range");
                foreach (var n in lead) seen.Add(n.Variation);
            }
        }

        seen.Should().BeEquivalentTo(Enum.GetValues<ThemeVariation>());
    }

    [Fact]
    public void A_new_world_greets_you_with_the_plain_theme_on_its_first_downbeat_within_a_beat()
    {
        var hub = FramesPerSixteenth(MusicIdentity.Hub);
        var s = new SkySynth(seed: 8, PlanetTheme.Hearth, Rate, Hub);
        s.Render(Frames(5.3));
        var called = s.FramesRendered;
        var first = (MelodyNote?)null;
        var origin = NextBeat(called, 0, hub);
        s.MelodyObserver = n =>
        {
            if (first == null && n.Part == MelodyPart.Theme && n.Frame >= origin) first = n; // the hub sings on until then
        };
        s.SetWorld(PlanetTheme.Meadow, Band(1), 1);
        s.Render(Frames(2));
        first.Should().NotBeNull();
        first!.Value.Variation.Should().Be(ThemeVariation.Plain);
        first.Value.Frame.Should().BeCloseTo(origin, 2);
        first.Value.MidiNote.Should().Be(Midi(SkySynth.KeyFor(PlanetTheme.Meadow), 2), "the theme's first note, mi, in the new key");
        (first.Value.Frame - called).Should().BeLessThanOrEqualTo((long)(4 * hub) + 1, "within one beat of the hub (0.64 s)");
    }

    [Fact]
    public void Variations_are_deterministic_per_seed_and_cycle()
    {
        List<ThemeVariation> Run(int seed)
        {
            var s = new SkySynth(seed, PlanetTheme.Chime, LowRate, Band(2));
            var list = new List<ThemeVariation>();
            s.MelodyObserver = n =>
            {
                if (n.Part == MelodyPart.Theme && (list.Count == 0 || list[^1] != n.Variation)) list.Add(n.Variation);
            };
            var buffer = new float[4096 * 2];
            var total = (int)Math.Round(8 * 16 * 16 * FramesPerSixteenth(MusicIdentity.Trails, LowRate));
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

    // ---------------------------------------------------------------- AD-52: the arrangements and the hazard cues (PLANET-35)

    private static readonly MusicIdentity[] NewArrangements = [MusicIdentity.Trails, MusicIdentity.DeepWoods, MusicIdentity.Underglow, MusicIdentity.Hazard];

    /// <summary>The identities whose AD-49 arrangements are still in use: the reference for loudness.</summary>
    private static readonly MusicIdentity[] CurrentArrangements = [MusicIdentity.Hub, MusicIdentity.WindowsRoot, MusicIdentity.WslRoot, MusicIdentity.Repository, MusicIdentity.Outskirts];

    public static TheoryData<MusicIdentity> NewArrangementIds() => new(NewArrangements);

    /// <summary>Frames per sixteenth of the theme (the score: half the pulse in a half-time arrangement).</summary>
    private static double ThemeFramesPerSixteenth(MusicIdentity id, int rate = Rate) => rate * 60.0 / (SkySynth.ThemeTempoFor(id) * 4.0);

    /// <summary>
    /// Integrated loudness after ITU-R BS.1770 (K-weighting, 400 ms blocks with 75 % overlap, the −70 LUFS absolute and
    /// −10 LU relative gates), in LUFS, of interleaved stereo at <see cref="Rate"/>.
    /// </summary>
    private static double Lufs(float[] s)
    {
        // The K-weighting filter's two biquads (the high shelf and the RLB high-pass), designed for this sample rate.
        static (double B0, double B1, double B2, double A1, double A2) Shelf()
        {
            const double f0 = 1681.974450955533, g = 3.999843853973347, q = 0.7071752369554196;
            var k = Math.Tan(Math.PI * f0 / Rate);
            var vh = Math.Pow(10, g / 20);
            var vb = Math.Pow(vh, 0.4996667741545416);
            var a0 = 1 + k / q + k * k;
            return ((vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
        }

        static (double B0, double B1, double B2, double A1, double A2) HighPass()
        {
            const double f0 = 38.13547087602444, q = 0.5003270373238773;
            var k = Math.Tan(Math.PI * f0 / Rate);
            var a0 = 1 + k / q + k * k;
            return (1, -2, 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
        }

        var frames = s.Length / 2;
        var weighted = new double[2][];
        foreach (var ch in new[] { 0, 1 })
        {
            var x = new double[frames];
            for (var i = 0; i < frames; i++) x[i] = s[2 * i + ch];
            foreach (var (b0, b1, b2, a1, a2) in new[] { Shelf(), HighPass() })
            {
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                for (var i = 0; i < frames; i++)
                {
                    var y = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                    x2 = x1;
                    x1 = x[i];
                    y2 = y1;
                    y1 = y;
                    x[i] = y;
                }
            }

            weighted[ch] = x;
        }

        var block = (int)(0.4 * Rate);
        var hop = block / 4;
        var powers = new List<double>();
        for (var start = 0; start + block <= frames; start += hop)
        {
            double sum = 0;
            for (var i = start; i < start + block; i++) sum += weighted[0][i] * weighted[0][i] + weighted[1][i] * weighted[1][i];
            powers.Add(sum / block);
        }

        static double L(double power) => -0.691 + 10 * Math.Log10(Math.Max(1e-12, power));
        var absolute = powers.Where(z => L(z) > -70).ToList();
        if (absolute.Count == 0) return -70;
        var relative = L(absolute.Average()) - 10;
        var gated = absolute.Where(z => L(z) > relative).ToList();
        return L(gated.Average());
    }

    [Theory]
    [MemberData(nameof(NewArrangementIds))]
    public void Every_new_arrangement_keeps_the_theme_recognisable_and_returning(MusicIdentity id)
    {
        var fps = ThemeFramesPerSixteenth(id);
        foreach (var theme in new[] { PlanetTheme.Hearth, PlanetTheme.Library, PlanetTheme.Theatre }) // 7-note keys: ThemeMatch maps degrees directly
        {
            var character = CharacterOf(id);
            var key = SkySynth.KeyFor(theme, character);
            var s = SynthFor(2026, theme, id);
            var lead = new List<MelodyNote>();
            s.MelodyObserver = n =>
            {
                if (n.Part == MelodyPart.Theme) lead.Add(n);
            };
            var total = (int)Math.Round(33 * 16 * fps); // two cycles and the next return's first bar
            var buffer = new float[4096 * 2];
            for (var done = 0; done < total; done += 4096) s.Render(buffer);
            var first = ThemeMatch(lead, key, 0, fps);
            var second = ThemeMatch(lead, key, SkySynth.CycleBars, fps);
            output.WriteLine($"{id} in {theme} ({key.Mode}, theme at {SkySynth.ThemeTempoFor(id)} BPM over a {SkySynth.TempoFor(id)} BPM pulse): first return {first:P0}, second {second:P0} ({lead.LastOrDefault().Variation})");
            first.Should().Be(1, $"{id} states the theme as written on arrival");
            second.Should().BeGreaterThanOrEqualTo(0.7, $"{id}'s second return is still the theme");
        }
    }

    [Fact]
    public void Every_arrangement_is_as_loud_as_the_others_within_2_dB_and_never_clips()
    {
        var results = new Dictionary<MusicIdentity, (double Lufs, double Rms, float Peak)>();
        foreach (var id in Enum.GetValues<MusicIdentity>())
        {
            var lufs = new List<double>();
            var rms = new List<double>();
            float peak = 0;
            foreach (var theme in new[] { PlanetTheme.Hearth, PlanetTheme.Workshop, PlanetTheme.Vault })
            {
                var s = SynthFor(77, theme, id);
                var buf = RenderSeconds(s, 32);
                lufs.Add(Lufs(buf));
                var m = Measure(buf);
                rms.Add(Db(m.Rms));
                peak = MathF.Max(peak, m.Peak);
            }

            results[id] = (lufs.Average(), rms.Average(), peak);
            output.WriteLine($"{id,-12} {results[id].Lufs:0.0} LUFS (K-weighted, gated), {results[id].Rms:0.0} dBFS RMS, peak {peak:0.000} ({Db(peak):0.0} dBFS)");
        }

        var reference = CurrentArrangements.Average(id => results[id].Lufs);
        output.WriteLine($"reference (the AD-49 arrangements still in use): {reference:0.0} LUFS");
        foreach (var id in NewArrangements)
        {
            results[id].Lufs.Should().BeInRange(reference - 2, reference + 2, $"{id} sits with the others");
        }

        var all = NewArrangements.Concat(CurrentArrangements).Select(id => results[id].Lufs).ToList();
        (all.Max() - all.Min()).Should().BeLessThanOrEqualTo(4, "every arrangement within ±2 dB of the middle");
        // The limiter (threshold 0.8) never has to act on the music alone, so nothing is ever squashed or clipped.
        foreach (var (id, r) in results) r.Peak.Should().BeLessThan(0.8f, $"{id}'s music leaves headroom");
    }

    [Theory]
    [MemberData(nameof(NewArrangementIds))]
    public void Switching_into_and_out_of_each_arrangement_never_clicks(MusicIdentity id)
    {
        var s = new SkySynth(seed: 13, PlanetTheme.Hearth, Rate, Band(1));
        s.SetMasterVolume(1);
        var steady = Measure(RenderSeconds(s, 3));
        float worst = 0;
        foreach (var (theme, character) in new[] { (PlanetTheme.Library, CharacterOf(id)), (PlanetTheme.Hearth, Band(1)), (PlanetTheme.Crystal, CharacterOf(id)), (PlanetTheme.Crystal, Repo) })
        {
            s.SetWorld(theme, character, 1);
            s.Arrive();
            var change = Measure(RenderSeconds(s, 1.5));
            worst = MathF.Max(worst, change.MaxJump);
            RenderSeconds(s, 2);
        }

        var alone = new SkySynth(seed: 13, PlanetTheme.Library, Rate, CharacterOf(id));
        alone.SetMasterVolume(1);
        var own = Measure(RenderSeconds(alone, 6));
        output.WriteLine($"{id}: max sample-to-sample jump across changes {worst:0.000} (steady outskirts {steady.MaxJump:0.000}, its own music {own.MaxJump:0.000})");
        worst.Should().BeLessThan(MathF.Max(steady.MaxJump, own.MaxJump) * 1.6f + 0.02f, "no clicks beyond the music's own transients");
    }

    [Fact]
    public void Rapid_hops_through_every_arrangement_and_back_stay_within_budget_and_click_free()
    {
        WorldCharacter[] route = [Band(2), Band(3), HazardWorld, Band(4), Repo, Band(2, 42), Windows, Band(3)];
        PlanetTheme[] themes = [PlanetTheme.Library, PlanetTheme.Theatre, PlanetTheme.Vault, PlanetTheme.Crystal, PlanetTheme.Workshop, PlanetTheme.Workshop, PlanetTheme.Hearth, PlanetTheme.Meadow];
        var s = new SkySynth(seed: 31, PlanetTheme.Hearth, Rate, Hub);
        s.SetMasterVolume(1);
        var steady = Measure(RenderSeconds(s, 3));
        float peak = 0, jump = 0;
        int maxMusic = 0, maxVoices = 0;
        double sum = 0;
        var n = 0;
        for (var i = 0; i < 16 * 60; i++) // 16 s in 60 Hz frames: 8 hops in 2 s, then backtracking through them, a hop every 0.75 s
        {
            if (i < 120 && i % 15 == 0)
            {
                s.SetWorld(themes[i / 15], route[i / 15], visits: 1);
                s.Arrive();
            }
            else if (i >= 240 && i < 240 + 8 * 45 && (i - 240) % 45 == 0)
            {
                var back = 7 - (i - 240) / 45; // the way back, most recent first (returns resume)
                s.SetWorld(themes[back], route[back], visits: 2);
                s.Arrive();
            }

            var buf = s.Render(735);
            foreach (var x in buf)
            {
                float.IsFinite(x).Should().BeTrue();
                peak = MathF.Max(peak, MathF.Abs(x));
                sum += x * (double)x;
                n++;
            }

            jump = MathF.Max(jump, Measure(buf).MaxJump);
            maxMusic = Math.Max(maxMusic, s.ActiveMusicVoices);
            maxVoices = Math.Max(maxVoices, s.ActiveVoices);
        }

        var settled = Measure(RenderSeconds(s, 4));
        // The music's own transients, world by world (a click is a jump beyond them).
        var own = route.Select((c, i) =>
        {
            var alone = new SkySynth(seed: 31, themes[i], Rate, c);
            alone.SetMasterVolume(1);
            return Measure(RenderSeconds(alone, 6)).MaxJump;
        }).Max();
        output.WriteLine($"the worlds' own largest jump {own:0.000}; steady jump {steady.MaxJump:0.000}, settled jump {settled.MaxJump:0.000}; hops and backtracking: peak {peak:0.000}, jump {jump:0.000}, rms {Math.Sqrt(sum / n):0.0000} (settled {settled.Rms:0.0000}), music voices ≤ {maxMusic}, voices ≤ {maxVoices}; ends in {s.Identity}");
        peak.Should().BeLessThan(1f);
        maxMusic.Should().BeLessThanOrEqualTo(SkySynth.MaxMusicVoices);
        maxVoices.Should().BeLessThanOrEqualTo(SkySynth.MaxVoices);
        jump.Should().BeLessThan(MathF.Max(own, MathF.Max(steady.MaxJump, settled.MaxJump)) * 1.6f + 0.02f, "no clicks beyond the music's own transients");
        Math.Sqrt(sum / n).Should().BeLessThan(settled.Rms * 2, "hops do not pile up into mush");
        s.Identity.Should().Be(MusicIdentity.Trails, "the way back ends where the hops began");
    }

    [Theory]
    [MemberData(nameof(NewArrangementIds))]
    public void A_full_minute_in_one_arrangement_varies_and_never_runs_away(MusicIdentity id)
    {
        var s = new SkySynth(seed: 60, PlanetTheme.Library, Rate, CharacterOf(id));
        var parts = new List<MelodyNote>();
        s.MelodyObserver = parts.Add;
        var windows = new List<double>();
        var voices = new List<int>();
        var all = new List<float>(Rate * 2 * 60);
        for (var w = 0; w < 12; w++)
        {
            var chunk = new float[Frames(5) * 2];
            for (var done = 0; done < chunk.Length; done += 735 * 2)
            {
                s.Render(chunk.AsSpan(done, Math.Min(735 * 2, chunk.Length - done)));
                voices.Add(s.ActiveMusicVoices);
            }

            windows.Add(Db(Measure(chunk).Rms));
            all.AddRange(chunk);
        }

        var half = Frames(30);
        string Fingerprint(IEnumerable<MelodyNote> notes) => string.Join(",", notes.Select(n => n.MidiNote));
        var firstHalf = Fingerprint(parts.Where(n => n.Frame < half && n.Part == MelodyPart.Theme));
        var secondHalf = Fingerprint(parts.Where(n => n.Frame >= half && n.Part == MelodyPart.Theme));
        var kinds = parts.Select(n => n.Part).Distinct().ToList();
        var (peak, jump, _) = Measure([.. all]);
        output.WriteLine($"{id}: 5 s windows {string.Join(" ", windows.Select(x => x.ToString("0.0")))} dBFS; music voices mean {voices.Average():0.0}, max {voices.Max()}; parts {string.Join("/", kinds)}; peak {peak:0.000}, jump {jump:0.000}");
        kinds.Should().Contain(MelodyPart.Theme).And.Contain(MelodyPart.Motif, "the theme and the world's own material");
        secondHalf.Should().NotBe(firstHalf, "the minute is not one loop");
        (windows.Max() - windows.Min()).Should().BeGreaterThan(1, "the dynamics move (a build, a breakdown, a tutti)");
        voices.Max().Should().BeLessThanOrEqualTo(SkySynth.MaxMusicVoices);
        voices.Average().Should().BeLessThan(SkySynth.MaxMusicVoices * 0.85, "the voice budget is not saturated all the time");
        voices.TakeLast(60).Average().Should().BeLessThan(SkySynth.MaxMusicVoices, "no voices pile up over the minute");
        peak.Should().BeLessThan(0.8f);
    }

    [Theory]
    [MemberData(nameof(NewArrangementIds))]
    public void Rendering_each_arrangement_with_cues_is_cheap(MusicIdentity id)
    {
        var best = double.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var s = new SkySynth(seed: run, PlanetTheme.Wild, Rate, CharacterOf(id) with { RepositoryId = id == MusicIdentity.Hazard ? -1 : 42 });
            s.Render(Frames(14)); // into the busiest part of the cycle
            var buffer = new float[Frames(1) * 2];
            var sw = Stopwatch.StartNew();
            s.Launch();
            s.Arrive();
            s.AirJump();
            s.FallStart();
            s.Chime(1);
            s.Open();
            for (var chunk = 0; chunk < 20; chunk++)
            {
                s.Fly(chunk / 20f);
                s.Fall(chunk / 20f);
                s.HazardWarning(0.8f);
                s.Render(buffer.AsSpan(chunk * buffer.Length / 20, buffer.Length / 20));
            }

            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }

        output.WriteLine($"{id}: best {best:0.00} ms per second of audio (music, flight build, fall, warning and six effects)");
        best.Should().BeLessThan(60, "the same bound as the busiest AD-49 world (6 % of a core, Debug)");
    }

    [Fact]
    public void A_flight_out_of_the_festival_builds_and_the_arrival_drops_into_the_theme()
    {
        var key = SkySynth.KeyFor(PlanetTheme.Library, Band(2));
        var beat = 4 * FramesPerSixteenth(MusicIdentity.Trails);
        (List<MelodyNote> Notes, long Arrival, double Early, double Late) Fly(bool flight, PlanetTheme to, WorldCharacter where)
        {
            var s = new SkySynth(seed: 9, PlanetTheme.Library, Rate, Band(2));
            var notes = new List<MelodyNote>();
            s.MelodyObserver = notes.Add;
            RenderSeconds(s, 20); // into the breakdown (bar 11 of 16)
            var windUp = s.AlignToGrid(TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(0.09));
            s.WindUp(windUp);
            RenderSeconds(s, windUp.TotalSeconds);
            s.Launch();
            var audio = new List<float>();
            for (var f = 0; f < 120; f++) // a 2 s flight in 60 Hz frames
            {
                if (flight) s.Fly(f / 120f);
                audio.AddRange(s.Render(735));
            }

            s.Fly(1);
            s.Land(8);
            s.SetWorld(to, where, 1);
            var arrival = s.FramesRendered;
            s.Arrive();
            RenderSeconds(s, 3);
            // The build's energy above ≈ 1 kHz (first differences): the roll thickens and the riser opens.
            double Hf(int from, int count)
            {
                double e = 0;
                for (var i = from * 2 + 2; i < (from + count) * 2; i++) e += Math.Pow(audio[i] - audio[i - 2], 2);
                return e / count;
            }

            return (notes, arrival, Hf(Frames(0.1), Frames(0.5)), Hf(Frames(1.4), Frames(0.5)));
        }

        // A flight to a sibling of the same world: it keeps its groove and the arrival drops into the top of the theme.
        var (notes, arrival, early, late) = Fly(true, PlanetTheme.Library, Band(2) with { Depth = 3 });
        var drop = notes.FirstOrDefault(n => n.Part == MelodyPart.Theme && n.Frame >= arrival);
        output.WriteLine($"build: high-frequency energy {early:0.000e0} early in the flight, {late:0.000e0} late; the drop's first theme note {(drop.Frame - arrival) * 1000.0 / Rate:0} ms after the arrival (MIDI {drop.MidiNote})");
        late.Should().BeGreaterThan(early * 1.5, "the build grows through the flight");
        (drop.Frame - arrival).Should().BeInRange(Frames(0.15) - 2, Frames(0.15) + (long)beat + 2, "the drop lands on the first beat after the arrival (≥ 0.15 s)");
        (drop.MidiNote % 12).Should().Be(Midi(key, 2) % 12, "the drop starts the theme from its first note");

        // Without the flight (a hop), the same arrival keeps its place in the breakdown: no drop.
        var (hopNotes, hopArrival, _, _) = Fly(false, PlanetTheme.Library, Band(2) with { Depth = 3 });
        hopNotes.Should().NotContain(n => n.Part == MelodyPart.Theme && n.Frame >= hopArrival && n.Frame < hopArrival + Frames(1.5), "a hop is not a build");

        // A flight to another festival world (another key): its first downbeat is the drop, at least 0.15 s after the arrival.
        var (newNotes, newArrival, _, _) = Fly(true, PlanetTheme.Meadow, Band(2));
        var first = newNotes.First(n => n.Part == MelodyPart.Theme && n.Frame >= newArrival);
        output.WriteLine($"into a new festival world: the theme {(first.Frame - newArrival) * 1000.0 / Rate:0} ms after the arrival");
        (first.Frame - newArrival).Should().BeInRange(Frames(0.15) - 2, Frames(0.15) + (long)beat + 2);
        (first.MidiNote % 12).Should().Be(Midi(SkySynth.KeyFor(PlanetTheme.Meadow, Band(2)), 2) % 12);
    }

    [Fact]
    public void The_hazard_warning_throttles_itself_and_falls_silent_when_the_calls_stop()
    {
        int Pulses(float nearness, int framesAt60Hz, int callsPerFrame = 1)
        {
            var s = new SkySynth(seed: 3, PlanetTheme.Vault, LowRate, HazardWorld) { MusicVolume = 0 };
            s.Render(LowRate);
            for (var f = 0; f < framesAt60Hz; f++)
            {
                for (var c = 0; c < callsPerFrame; c++) s.HazardWarning(nearness);
                s.Render(LowRate / 60);
            }

            return s.WarningPulses;
        }

        var edge = Pulses(1f, 180);
        var near = Pulses(0.3f, 180);
        var far = Pulses(0f, 180);
        var hammered = Pulses(1f, 180, callsPerFrame: 50);
        output.WriteLine($"pulses in 3 s: at the edge {edge}, nearness 0.3 {near}, far {far}, at the edge called 50× per frame {hammered}");
        edge.Should().BeInRange(9, 13, "every ≈ 0.25 s at the edge");
        near.Should().BeInRange(3, 6, "slower when further away");
        far.Should().Be(0);
        hammered.Should().Be(edge, "calling it more often changes nothing");

        // When the calls stop, the rumble and pulses fade within a second.
        var t = new SkySynth(seed: 3, PlanetTheme.Vault, Rate, HazardWorld) { MusicVolume = 0 };
        t.Render(Rate);
        for (var f = 0; f < 60; f++)
        {
            t.HazardWarning(0.9f);
            t.Render(735);
        }

        t.Render(Frames(1.2));
        Measure(t.Render(Frames(0.3))).Peak.Should().BeLessThan(1e-3f, "nothing drones on after the warning stops");
    }

    [Fact]
    public void The_fall_spirals_downwards_and_the_return_brings_the_music_back()
    {
        var s = new SkySynth(seed: 5, PlanetTheme.Hearth, Rate, HazardWorld) { MusicVolume = 0 };
        s.Render(Frames(2));
        s.FallStart();
        var fall = new List<float>();
        for (var f = 0; f < 150; f++) // a 2.5 s fall
        {
            s.Fall(f / 150f);
            fall.AddRange(s.Render(735));
        }

        // Pitch from zero crossings of the left channel over 0.4 s windows: it falls.
        double Crossings(int from)
        {
            var c = 0;
            for (var i = from + 1; i < from + Frames(0.4); i++)
            {
                if (Math.Sign(fall[2 * i]) != Math.Sign(fall[2 * i - 2])) c++;
            }

            return c / 0.4;
        }

        // The spiral: the balance between left and right swings back and forth, faster towards the end.
        int Swings(int from, int to)
        {
            var swings = 0;
            var last = 0;
            for (var w = from; w + 441 <= to; w += 441)
            {
                double l = 0, r = 0;
                for (var i = w; i < w + 441; i++)
                {
                    l += fall[2 * i] * fall[2 * i];
                    r += fall[2 * i + 1] * fall[2 * i + 1];
                }

                var side = l > r * 1.2 ? -1 : r > l * 1.2 ? 1 : 0;
                if (side != 0 && side != last)
                {
                    if (last != 0) swings++;
                    last = side;
                }
            }

            return swings;
        }

        var early = Crossings(Frames(0.4));
        var late = Crossings(Frames(2.0));
        var swingsEarly = Swings(0, Frames(1.25));
        var swingsLate = Swings(Frames(1.25), Frames(2.5));
        output.WriteLine($"fall: {early:0} crossings/s early, {late:0} late; left-right swings {swingsEarly} in the first half, {swingsLate} in the second");
        late.Should().BeLessThan(early * 0.6, "the whoosh falls in pitch");
        swingsLate.Should().BeGreaterThan(swingsEarly, "the spiral turns faster");

        // The music: ducked deep during the fall, back near its level within half a second of the return.
        float[] Music(bool ret)
        {
            var m = new SkySynth(seed: 5, PlanetTheme.Hearth, Rate, HazardWorld);
            m.Render(Frames(8));
            m.FallStart();
            for (var f = 0; f < 150; f++)
            {
                m.Fall(f / 150f);
                m.Render(735);
            }

            if (ret) m.FallReturn();
            m.Render(Frames(0.6));
            return m.Render(Frames(1));
        }

        var quiet = new SkySynth(seed: 5, PlanetTheme.Hearth, Rate, HazardWorld);
        quiet.Render(Frames(10.5));
        var reference = Measure(quiet.Render(Frames(1))).Rms;
        var after = Measure(Music(true)).Rms;
        output.WriteLine($"music after the return {Db(after):0.0} dBFS (undisturbed {Db(reference):0.0})");
        after.Should().BeGreaterThan(reference * 0.5, "the music comes back after the return");
    }

    [Theory]
    [MemberData(nameof(Identities))]
    public void Hazard_cues_stand_clear_of_every_world_s_music(MusicIdentity id)
    {
        (string Name, Action<SkySynth> Start, Action<SkySynth, int> Frame, double Seconds)[] cues =
        [
            ("warning", _ => { }, (s, f) => s.HazardWarning(1f), 0.6),
            ("fall", s => s.FallStart(), (s, f) => s.Fall(f / 60f), 1.0),
            ("return", s => s.FallReturn(), (_, _) => { }, 0.4),
        ];
        foreach (var (name, start, frame, seconds) in cues)
        {
            float[] Run(float music)
            {
                var s = SynthFor(30, PlanetTheme.Hearth, id, musicVolume: music);
                s.Render(Frames(4.0));
                start(s);
                var all = new List<float>();
                for (var f = 0; f < (int)(seconds * 60); f++)
                {
                    frame(s, f);
                    all.AddRange(s.Render(735));
                }

                return [.. all];
            }

            var cue = Run(0);
            var with = Run(1);
            double c = 0, m = 0;
            for (var i = 0; i < cue.Length; i++)
            {
                c += cue[i] * (double)cue[i];
                m += Math.Pow(with[i] - cue[i], 2);
            }

            var snr = 10 * Math.Log10(c / Math.Max(1e-12, m));
            output.WriteLine($"{id}: {name} {snr:+0.0;-0.0} dB over the ducked music");
            snr.Should().BeGreaterThan(4, $"{id}'s music leaves room for the {name} cue");
        }
    }

    // ---------------------------------------------------------------- listening evidence (opt-in)

    [SkySynthWavFact]
    public void Render_30_second_demo_to_wav()
    {
        var path = Environment.GetEnvironmentVariable("SKYSYNTH_WAV")!;
        var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Hearth, Rate, Hub));
        r.Walk(6);
        r.Travel(PlanetTheme.Meadow, Band(1), 1);
        r.Walk(6);
        r.Travel(PlanetTheme.Workshop, Repo, 1);
        r.Until(30);
        WriteWav(path, r.Samples, Rate);
        var (peak, _, rms) = Measure([.. r.Samples]);
        output.WriteLine($"wrote {path}: {r.Samples.Count / 2 / (double)Rate:0.0} s, peak {peak:0.000} ({Db(peak):0.0} dBFS), rms {rms:0.0000} ({Db(rms):0.0} dBFS)");
        peak.Should().BeLessThan(1f);
    }

    private static double Db(double linear) => 20 * Math.Log10(Math.Max(1e-9, linear));

    /// <summary>
    /// The listening evidence for PLANET-28 (SKYSYNTH_EVIDENCE_DIR): a 25 s clip per identity (a wind-up, launch, flight and
    /// a landing back into the same world's music early on, a few interface sounds, then its music while walking), a third visit
    /// of the trails, the 75 s journey (hub → WSL → band 1 → band 2 → repository → a folder in it → back → hub) and a
    /// quick-hop sequence (5 hops in 6 s, then back).
    /// </summary>
    [SkySynthEvidenceFact]
    public void Render_world_evidence_to_wav()
    {
        var dir = Environment.GetEnvironmentVariable("SKYSYNTH_EVIDENCE_DIR")!;
        Directory.CreateDirectory(dir);
        (string Name, PlanetTheme Theme, WorldCharacter Character, int Visits)[] clips =
        [
            ("hub", PlanetTheme.Hearth, Hub, 1),
            ("windows-root", PlanetTheme.Hearth, Windows, 1),
            ("wsl-root", PlanetTheme.Hearth, Wsl, 1),
            ("repository", PlanetTheme.Workshop, Repo, 1),
            ("band1", PlanetTheme.Meadow, Band(1), 1),
            ("band2", PlanetTheme.Library, Band(2), 1),
            ("band3", PlanetTheme.Theatre, Band(3), 1),
            ("band4", PlanetTheme.Crystal, Band(4), 1),
            ("band2-inside-repository", PlanetTheme.Workshop, Band(2, 42), 1),
            ("band2-third-visit", PlanetTheme.Library, Band(2), 3),
        ];
        foreach (var (name, theme, character, visits) in clips)
        {
            var s = new SkySynth(seed: 2026, theme, Rate, Band(1));
            s.SetWorld(theme, character, visits);
            // The cues early (a landing back into the same world keeps its music), then the music uninterrupted: the theme's
            // end (the hub's question at 15–20 s) and the world's own material after it.
            var r = new Route(s);
            r.Walk(2.5);
            r.Travel(theme, character, visits);
            r.Walk(4);
            s.Select();
            r.Until(r.T + 0.8);
            s.Chime(2);
            r.Walk(25 - r.T);
            Save(r, Path.Combine(dir, $"{name}-25s.wav"));
        }

        {
            var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Hearth, Rate, Hub));
            r.Walk(7);
            r.Travel(PlanetTheme.Hearth, Wsl, 1);
            r.Walk(6.5);
            r.Travel(PlanetTheme.Meadow, Band(1), 1);
            r.Walk(6.5);
            r.Travel(PlanetTheme.Library, Band(2), 1);
            r.Walk(6.5);
            r.Travel(PlanetTheme.Workshop, Repo, 1);
            r.Walk(7);
            r.Travel(PlanetTheme.Workshop, Band(2, 42), 1);
            r.Walk(6);
            r.Travel(PlanetTheme.Workshop, Repo, 2);
            r.Walk(6);
            r.Travel(PlanetTheme.Hearth, Hub, 2);
            r.Until(75);
            Save(r, Path.Combine(dir, "journey-route-75s.wav"));
        }

        {
            var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Meadow, Rate, Band(1)));
            r.Walk(4);
            (PlanetTheme, WorldCharacter)[] hops = [(PlanetTheme.Library, Band(2)), (PlanetTheme.Theatre, Band(3)), (PlanetTheme.Chime, Band(2) with { Depth = 3 }), (PlanetTheme.Workshop, Repo), (PlanetTheme.Workshop, Band(2, 42))];
            var start = r.T;
            for (var i = 0; i < hops.Length; i++)
            {
                r.Until(start + 1.2 * i);
                r.Hop(hops[i].Item1, hops[i].Item2, 1);
            }

            r.Until(start + 6.2);
            r.Hop(PlanetTheme.Workshop, Repo, 2); // back (Backspace)
            r.Walk(4);
            r.Hop(PlanetTheme.Workshop, Band(2, 42), 2); // and forward again
            r.Until(20);
            Save(r, Path.Combine(dir, "quick-hops-20s.wav"));
        }
    }

    /// <summary>
    /// The listening samples for PLANET-35 (SKYSYNTH_ARRANGEMENTS_DIR): each new arrangement with launches and arrivals, the
    /// theme's first 8 bars in each arrangement back to back, the hazard world with its warning, fall and return, rapid hops
    /// and backtracking. Launches here follow the app (a 0.4 s wind-up aligned to the grid, a 2 s flight, landing impact 8 m/s,
    /// SetWorld then Arrive; no SetFlight, which the app does not call).
    /// </summary>
    [SkySynthArrangementsFact]
    public void Render_arrangement_samples_to_wav()
    {
        var dir = Environment.GetEnvironmentVariable("SKYSYNTH_ARRANGEMENTS_DIR")!;
        Directory.CreateDirectory(dir);

        {
            // The festival: a flight in from the outskirts (its first downbeat drops), the drop, the breakdown, then a flight to a
            // sibling folder at ≈ 23 s (the build in the air, the drop on arrival), the drop again, the breakdown,
            // and the cycle's own build and drop (≈ 56 s).
            var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Meadow, Rate, Band(1)));
            r.Walk(1.5);
            r.Flight(PlanetTheme.Library, Band(2), 1);
            r.Walk(23 - r.T);
            r.Flight(PlanetTheme.Library, Band(2) with { Depth = 3 }, 1);
            r.Walk(58 - r.T);
            Save(r, Path.Combine(dir, "festival-trails-58s.wav"));
        }

        {
            // The orchestra: arriving from the outskirts, one full cycle (the theme passed from woodwind to violins to horns to
            // the tutti, the cellos' motif, the horn call and the bar-14 climax), then a flight to another deep-woods world.
            var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Meadow, Rate, Band(1)));
            r.Walk(1.5);
            r.Flight(PlanetTheme.Theatre, Band(3), 1);
            r.Walk(52 - r.T);
            r.Flight(PlanetTheme.Hearth, Band(3), 1);
            r.Until(60);
            Save(r, Path.Combine(dir, "orchestral-deep-woods-60s.wav"));
        }

        {
            // Drum and bass: a flight out of the festival (its build) lands in the underglow (its first downbeat hits with the
            // impact), the half-breakdown at bars 9–10, then a flight to another underglow world.
            var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Library, Rate, Band(2)));
            r.Walk(3);
            r.Flight(PlanetTheme.Crystal, Band(4), 1);
            r.Walk(44 - r.T);
            r.Flight(PlanetTheme.Vault, Band(4), 1);
            r.Until(56);
            Save(r, Path.Combine(dir, "dnb-underglow-56s.wav"));
        }

        {
            // The same melody across the arrangements: the theme's first 8 bars in each, back to back (fresh synths, the
            // first downbeat at the top of each part, Library's B♭ root throughout).
            var all = new List<float>();
            foreach (var id in new[] { MusicIdentity.Trails, MusicIdentity.Underglow, MusicIdentity.DeepWoods })
            {
                var s = new SkySynth(seed: 2026, PlanetTheme.Library, Rate, CharacterOf(id));
                var frames = (int)Math.Round(8 * 16 * ThemeFramesPerSixteenth(id)) + Frames(0.4);
                var part = new float[frames * 2];
                for (var done = 0; done < part.Length; done += 735 * 2) s.Render(part.AsSpan(done, Math.Min(735 * 2, part.Length - done)));
                // A 0.3 s fade at the end of each part, so the cut to the next is a breath rather than a click.
                var fade = Frames(0.3);
                for (var i = 0; i < fade; i++)
                {
                    var g = 1f - (i + 1) / (float)fade;
                    part[(frames - fade + i) * 2] *= g;
                    part[(frames - fade + i) * 2 + 1] *= g;
                }

                all.AddRange(part);
                output.WriteLine($"theme A/B: {id} from {all.Count / 2 / (double)Rate - frames / (double)Rate:0.0} s");
            }

            WriteWav(Path.Combine(dir, "theme-across-arrangements-60s.wav"), all, Rate);
            var (peak, jump, rms) = Measure([.. all]);
            output.WriteLine($"wrote theme-across-arrangements-60s.wav: {all.Count / 2 / (double)Rate:0.0} s, peak {peak:0.000}, rms {Db(rms):0.0} dBFS, max jump {jump:0.000}");
        }

        {
            // The hazard world: arriving, walking, nearing an edge (the warning speeds up), backing off, nearing again, the
            // fall (crack, the spiral down), the return at the parent (the outskirts), then a flight back into the hazard world.
            var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Meadow, Rate, Band(1)));
            var s = r.Synth;
            r.Walk(1.5);
            r.Flight(PlanetTheme.Vault, HazardWorld, 1);
            r.Walk(15 - r.T);
            var t0 = r.T;
            r.Events.Add($"warning rises {r.T:0.00} s");
            r.Until(t0 + 6, now => s.HazardWarning((float)((now - t0) / 6)));
            t0 = r.T;
            r.Events.Add($"backs off {r.T:0.00} s");
            r.Until(t0 + 3, now => s.HazardWarning(1f - (float)((now - t0) / 3)));
            r.Walk(2);
            t0 = r.T;
            r.Until(t0 + 3, now => s.HazardWarning(0.4f + 0.6f * (float)((now - t0) / 3)));
            s.FallStart();
            r.Events.Add($"fall {r.T:0.00} s");
            t0 = r.T;
            r.Until(t0 + 2.5, now => s.Fall((float)((now - t0) / 2.5)));
            s.Fall(1);
            s.SetWorld(PlanetTheme.Meadow, Band(1), 2);
            s.FallReturn();
            s.Arrive();
            r.Events.Add($"return {r.T:0.00} s");
            r.Walk(42 - r.T);
            r.Flight(PlanetTheme.Vault, HazardWorld, 2);
            r.Walk(52 - r.T);
            t0 = r.T;
            r.Until(t0 + 4, now => s.HazardWarning(0.7f));
            r.Until(60);
            Save(r, Path.Combine(dir, "hazard-60s.wav"));
        }

        {
            // Rapid hops: 8 worlds in 2 s through every arrangement, then settling in the last.
            var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Hearth, Rate, Hub));
            r.Walk(4);
            (PlanetTheme, WorldCharacter)[] hops =
            [
                (PlanetTheme.Library, Band(2)), (PlanetTheme.Theatre, Band(3)), (PlanetTheme.Crystal, Band(4)), (PlanetTheme.Vault, HazardWorld),
                (PlanetTheme.Workshop, Repo), (PlanetTheme.Meadow, Band(1)), (PlanetTheme.Hearth, Wsl), (PlanetTheme.Library, Band(2) with { Depth = 3 }),
            ];
            var start = r.T;
            for (var i = 0; i < hops.Length; i++)
            {
                r.Until(start + 0.25 * i);
                r.Hop(hops[i].Item1, hops[i].Item2, 1);
            }

            r.Until(20);
            Save(r, Path.Combine(dir, "rapid-hops-20s.wav"));
        }

        {
            // Backtracking: out from the hub through the festival, the orchestra and drum and bass by flights, then back the
            // same way by quick returns (Backspace: each world resumes where it was), ending at the hub.
            var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Hearth, Rate, Hub));
            r.Walk(2);
            r.Flight(PlanetTheme.Library, Band(2), 1);
            r.Walk(4);
            r.Flight(PlanetTheme.Theatre, Band(3), 1);
            r.Walk(4);
            r.Flight(PlanetTheme.Crystal, Band(4), 1);
            r.Walk(4);
            r.Flight(PlanetTheme.Theatre, Band(3), 2);
            r.Walk(1.6);
            r.Hop(PlanetTheme.Library, Band(2), 2);
            r.Walk(1.6);
            r.Hop(PlanetTheme.Hearth, Hub, 2);
            r.Until(30);
            Save(r, Path.Combine(dir, "backtrack-30s.wav"));
        }
    }

    private void Save(Route r, string path)
    {
        WriteWav(path, r.Samples, Rate);
        var (peak, jump, rms) = Measure([.. r.Samples]);
        output.WriteLine($"wrote {Path.GetFileName(path)}: {r.Samples.Count / 2 / (double)Rate:0.0} s, peak {peak:0.000} ({Db(peak):0.0} dBFS), rms {Db(rms):0.0} dBFS, max jump {jump:0.000}, music voices ≤ {r.MaxMusicVoices}; {string.Join(", ", r.Events)}");
        peak.Should().BeLessThan(1f);
        r.MaxMusicVoices.Should().BeLessThanOrEqualTo(SkySynth.MaxMusicVoices);
    }

    /// <summary>A scripted playthrough in 60 Hz frames: walking, a launch (wind-up aligned to the grid), flight, a landing and arrival.</summary>
    private sealed class Route(SkySynth s)
    {
        public readonly List<float> Samples = new(Rate * 2 * 80);
        public readonly List<string> Events = [];
        public double T;
        public int MaxMusicVoices;

        public void Until(double seconds, Action<double>? each = null)
        {
            while (T < seconds)
            {
                each?.Invoke(T);
                Samples.AddRange(s.Render(735));
                MaxMusicVoices = Math.Max(MaxMusicVoices, s.ActiveMusicVoices);
                T += 735 / (double)Rate;
            }
        }

        public void Walk(double seconds)
        {
            var next = T + 0.3;
            Until(T + seconds, now =>
            {
                if (now < next) return;
                s.Step(0.6f);
                next += 0.36;
            });
        }

        public void Travel(PlanetTheme theme, WorldCharacter character, int visits)
        {
            var windUp = s.AlignToGrid(TimeSpan.FromSeconds(0.75), TimeSpan.FromSeconds(0.09));
            s.WindUp(windUp);
            Until(T + windUp.TotalSeconds);
            s.Launch();
            Events.Add($"launch {T:0.00} s");
            var t0 = T;
            Until(t0 + 2.4, now =>
            {
                var p = (float)((now - t0) / 2.4);
                s.Fly(p);
                s.SetFlight(MathF.Sin(MathF.PI * Math.Clamp(p, 0, 1)));
            });
            s.Fly(1);
            s.SetFlight(0);
            s.Land(6);
            s.SetWorld(theme, character, visits);
            s.Arrive();
            Events.Add($"land {T:0.00} s ({SkySynth.IdentityOf(character)})");
        }

        /// <summary>The synth the route drives (for cues the route has no method for).</summary>
        public SkySynth Synth => s;

        /// <summary>A launch as the app makes it (AD-52 samples): a 0.4 s wind-up on the grid, a flight calling Fly every frame, landing at 8 m/s, SetWorld, Arrive.</summary>
        public void Flight(PlanetTheme theme, WorldCharacter character, int visits, double flightSeconds = 2.0, MusicIdentity? score = null)
        {
            var windUp = s.AlignToGrid(TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(0.09));
            s.WindUp(windUp);
            Until(T + windUp.TotalSeconds);
            s.Launch();
            Events.Add($"launch {T:0.00} s");
            var t0 = T;
            Until(t0 + flightSeconds, now => s.Fly((float)((now - t0) / flightSeconds)));
            s.Fly(1);
            s.Land(8);
            s.SetWorld(theme, character, visits, score ?? SkySynth.IdentityOf(character));
            s.Arrive();
            Events.Add($"land {T:0.00} s ({score ?? SkySynth.IdentityOf(character)})");
        }

        public void Hop(PlanetTheme theme, WorldCharacter character, int visits)
        {
            s.SetWorld(theme, character, visits);
            s.Arrive();
            Events.Add($"hop {T:0.00} s ({SkySynth.IdentityOf(character)})");
        }
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
