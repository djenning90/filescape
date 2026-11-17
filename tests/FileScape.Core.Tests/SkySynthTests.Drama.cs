using FileScape.Core.Audio;
using FileScape.Core.Planets;

namespace FileScape.Core.Tests;

/// <summary>Runs only when SKYSYNTH_DRAMA_DIR names an output folder (PLANET-48's before-and-after listening samples, AD-60); otherwise skipped.</summary>
public sealed class SkySynthDramaFactAttribute : FactAttribute
{
    public SkySynthDramaFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKYSYNTH_DRAMA_DIR")))
        {
            Skip = "Set SKYSYNTH_DRAMA_DIR to a folder to render the PLANET-48 drama and hazard-score samples.";
        }
    }
}

/// <summary>
/// PLANET-48 (AD-60): the listening samples for the phrase-level drama and the hazard score, and the band-energy analysis
/// that stands in for listening on headphones and laptop speakers (nobody here can listen). The same script renders the
/// "before" set on the previous build and the "after" set on this one, so the two are comparable clip for clip.
/// </summary>
public partial class SkySynthTests
{
    /// <summary>
    /// The samples (SKYSYNTH_DRAMA_DIR): a window of each arrangement's cycle where its drama is (the festival's drop and
    /// build, the orchestra's climax, drum and bass's break, build and drop), and the hazard world through an arrival, about
    /// 20 s on the surface with the edge warning rising, a fall and the return. Each clip is 20–40 s.
    /// </summary>
    [SkySynthDramaFact]
    public void Render_drama_samples_to_wav()
    {
        var dir = Environment.GetEnvironmentVariable("SKYSYNTH_DRAMA_DIR")!;
        Directory.CreateDirectory(dir);
        double Bar(MusicIdentity id) => 16 * ThemeFramesPerSixteenth(id) / Rate;

        // Each arrangement from a flight in (its first downbeat ≈ one pulse beat after the arrival), saving a window of bars.
        void Arrangement(string name, PlanetTheme theme, WorldCharacter character, int fromBar, int toBar)
        {
            var id = SkySynth.IdentityOf(character);
            var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Meadow, Rate, Band(1)));
            r.Walk(1.0);
            r.Flight(theme, character, 1);
            var downbeat = r.T + 60.0 / SkySynth.TempoFor(id);
            r.Walk(downbeat + toBar * Bar(id) + 1.0 - r.T);
            SaveWindow(r, Path.Combine(dir, name), downbeat + fromBar * Bar(id) - 0.5, downbeat + toBar * Bar(id) + 1.0);
            ReportBands(r, name, downbeat, Bar(id));
        }

        Arrangement("festival-trails.wav", PlanetTheme.Library, Band(2), 4, 17);
        Arrangement("orchestral-deep-woods.wav", PlanetTheme.Theatre, Band(3), 10, 17);
        Arrangement("dnb-underglow-drop.wav", PlanetTheme.Crystal, Band(4), 5, 16);

        void Hazard(string name, MusicIdentity? score)
        {
            var (r, start, downbeat) = HazardRoute(score);
            SaveWindow(r, Path.Combine(dir, name), start - 0.5, r.T);
            ReportBands(r, name, downbeat, 16 * ThemeFramesPerSixteenth(score ?? MusicIdentity.Hazard) / Rate);
        }

        Hazard("hazard-arrival-surface-fall.wav", null);
        Hazard("abyss-old-hazard-score-kept.wav", MusicIdentity.Abyss);

        {
            // The hazard world's whole cycle from its first downbeat (no cues): the drive, the climb, the chase, the break, the build.
            var s = SynthFor(2026, PlanetTheme.Vault, MusicIdentity.Hazard);
            var r = new Route(s);
            r.Until(SkySynth.CycleBars * 16 * ThemeFramesPerSixteenth(MusicIdentity.Hazard) / Rate + 1.5);
            SaveWindow(r, Path.Combine(dir, "hazard-full-cycle.wav"), 0, r.T);
        }
    }

    /// <summary>
    /// The hazard sample's script (route seconds: the start, and the hazard music's first downbeat): a flight in from the
    /// outskirts, ≈ 20 s on the surface (walking; the edge warning rising over its last 7 s), the ground gives way, the fall,
    /// the return to the outskirts and a few seconds there. <paramref name="score"/> plays another score in the hazard world
    /// (the abyss: AD-52's, kept by AD-60).
    /// </summary>
    private static (Route Route, double Start, double Downbeat) HazardRoute(MusicIdentity? score)
    {
        var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Meadow, Rate, Band(1)));
        var s = r.Synth;
        r.Walk(1.0);
        var start = r.T;
        r.Flight(PlanetTheme.Vault, HazardWorld, 1, score: score);
        r.Events.Add($"arrival {r.T:0.00} s");
        var downbeat = r.T + 60.0 / SkySynth.TempoFor(score ?? MusicIdentity.Hazard);
        r.Walk(13);
        var t0 = r.T;
        r.Events.Add($"warning rises {r.T:0.00} s");
        r.Until(t0 + 7, now => s.HazardWarning((float)(0.1 + 0.9 * (now - t0) / 7)));
        s.FallStart();
        r.Events.Add($"fall {r.T:0.00} s");
        t0 = r.T;
        r.Until(t0 + 2.6, now => s.Fall((float)((now - t0) / 2.6)));
        s.Fall(1);
        s.SetWorld(PlanetTheme.Meadow, Band(1), 2);
        s.FallReturn();
        s.Arrive();
        r.Events.Add($"return {r.T:0.00} s");
        r.Walk(5);
        return (r, start, downbeat);
    }

    /// <summary>
    /// DJ: "Save that arrangement." The abyss is the AD-52 hazard score unchanged: the hazard sample's script played with it
    /// is, sample for sample (16-bit), the "before" render made on 74cd1b6 (SHA-256 of its WAV data on each platform,
    /// recorded 2026-11-16).
    /// </summary>
    [Fact]
    public void The_abyss_is_the_AD52_hazard_score_unchanged()
    {
        // The maths library differs between platforms in the last bits, so each has its own hash: the "before" render of
        // the AD-52 hazard score (be71525: 74cd1b6 plus this script) with the Linux SDK and with Windows dotnet.
        const string Ad52HazardSampleSha256 = "0575971ed3e3a44691e16096f49fc97594350e935ac5498d5c96ad2715036094";
        const string Ad52HazardSampleSha256Windows = "67040f50e4d3865e1739c45e63274034320c45613b366872c9e3dc30e37db262";
        var (r, start, _) = HazardRoute(MusicIdentity.Abyss);
        var a = Math.Max(0, (int)((start - 0.5) * Rate)) * 2;
        var b = Math.Min(r.Samples.Count, (int)(r.T * Rate) * 2);
        var bytes = new byte[(b - a) * 2];
        for (var i = a; i < b; i++)
        {
            var v = (short)Math.Round(Math.Clamp(r.Samples[i], -1f, 1f) * 32767);
            bytes[(i - a) * 2] = (byte)v;
            bytes[(i - a) * 2 + 1] = (byte)(v >> 8);
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        output.WriteLine($"abyss render: {hash} ({(OperatingSystem.IsWindows() ? "Windows" : "Linux")})");
        hash.Should().Be(OperatingSystem.IsWindows() ? Ad52HazardSampleSha256Windows : Ad52HazardSampleSha256);
    }

    /// <summary>Saves the samples between two route times (seconds) and logs the clip's peak, loudness and events.</summary>
    private void SaveWindow(Route r, string path, double from, double to)
    {
        var a = Math.Max(0, (int)(from * Rate)) * 2;
        var b = Math.Min(r.Samples.Count, (int)(to * Rate) * 2);
        var clip = r.Samples.GetRange(a, b - a);
        WriteWav(path, clip, Rate);
        var m = Measure([.. clip]);
        output.WriteLine($"wrote {Path.GetFileName(path)}: {clip.Count / 2 / (double)Rate:0.0} s ({from:0.0}–{to:0.0} s of the route), peak {m.Peak:0.000}, {Lufs([.. clip]):0.0} LUFS, max jump {m.MaxJump:0.000}, music voices ≤ {r.MaxMusicVoices}; {string.Join(", ", r.Events)}");
        m.Peak.Should().BeLessThan(1f);
    }

    /// <summary>
    /// Per bar of the route after <paramref name="downbeat"/>: the sub (30–60 Hz), the low harmonics (100–300 Hz) and what a
    /// laptop's small speakers can play (above 200 Hz) in dBFS RMS, and the loudness: the stand-in for listening on
    /// headphones (everything) and ordinary speakers (no sub).
    /// </summary>
    private void ReportBands(Route r, string name, double downbeat, double bar)
    {
        var all = r.Samples.ToArray();
        var sub = Band(all, 30, 60);
        var harm = Band(all, 100, 300);
        var small = Band(all, 200, 16000); // what a laptop's small speakers can play (≈ nothing below 200 Hz)
        for (var k = 0; k < 17; k++)
        {
            var a = (int)((downbeat + k * bar) * Rate);
            var b = (int)((downbeat + (k + 1) * bar) * Rate);
            if (b > sub.Length) break;
            var seg = all[(2 * a)..(2 * b)];
            output.WriteLine($"{name} bar {k,2}: sub 30–60 Hz {Db(Rms(sub, a, b)):0.0} dBFS, harmonics 100–300 Hz {Db(Rms(harm, a, b)):0.0} dBFS, above 200 Hz {Db(Rms(small, a, b)):0.0} dBFS, {Lufs(seg):0.0} LUFS");
        }
    }

    private static double Rms(double[] x, int a, int b)
    {
        double sum = 0;
        for (var i = a; i < b; i++) sum += x[i] * x[i];
        return Math.Sqrt(sum / Math.Max(1, b - a));
    }

    /// <summary>The mono sum of interleaved stereo, band-passed between two frequencies (fourth-order Butterworth each side).</summary>
    private static double[] Band(float[] stereo, double lowHz, double highHz)
    {
        var x = new double[stereo.Length / 2];
        for (var i = 0; i < x.Length; i++) x[i] = 0.5 * (stereo[2 * i] + stereo[2 * i + 1]);
        foreach (var high in new[] { true, true, false, false })
        {
            var f = high ? lowHz : highHz;
            var w = 2 * Math.PI * f / Rate;
            var alpha = Math.Sin(w) / (2 * 0.7071);
            var c = Math.Cos(w);
            var a0 = 1 + alpha;
            var (b0, b1, b2) = high ? ((1 + c) / 2, -(1 + c), (1 + c) / 2) : ((1 - c) / 2, 1 - c, (1 - c) / 2);
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (var i = 0; i < x.Length; i++)
            {
                var y = (b0 * x[i] + b1 * x1 + b2 * x2 - -2 * c * y1 - (1 - alpha) * y2) / a0;
                x2 = x1;
                x1 = x[i];
                y2 = y1;
                y1 = y;
                x[i] = y;
            }
        }

        return x;
    }

    // ---------------------------------------------------------------- the drama's shape and the cues' room through it (AD-60)

    /// <summary>The loudness (LUFS) of each bar of the first cycle of <paramref name="id"/> from its first downbeat (frame 0), and the samples.</summary>
    private static (double[] Lufs, float[] All) BarLoudness(MusicIdentity id, PlanetTheme theme, int seed = 2026)
    {
        var s = SynthFor(seed, theme, id);
        var bar = 16 * ThemeFramesPerSixteenth(id);
        var all = RenderSeconds(s, (SkySynth.CycleBars + 1) * bar / Rate);
        var lufs = new double[SkySynth.CycleBars];
        for (var k = 0; k < lufs.Length; k++) lufs[k] = Lufs(all[(2 * (int)(k * bar))..(2 * (int)((k + 1) * bar))]);
        return (lufs, all);
    }

    /// <summary>The loudness of a part of a bar: sixteenths <paramref name="from"/> to <paramref name="to"/> of bar <paramref name="k"/>.</summary>
    private static double PartLufs(float[] all, MusicIdentity id, int k, int from, int to)
    {
        var fps = ThemeFramesPerSixteenth(id);
        return Lufs(all[(2 * (int)((k * 16 + from) * fps))..(2 * (int)((k * 16 + to) * fps))]);
    }

    private static double Mean(IEnumerable<double> xs) => xs.Average();

    [Fact]
    public void Each_arrangement_has_its_phrase_level_drama()
    {
        // Drum and bass: the breakdown dips, the build rises, the drop is the loudest part, and deeper.
        {
            var (l, all) = BarLoudness(MusicIdentity.Underglow, PlanetTheme.Crystal);
            output.WriteLine($"drum and bass per bar: {string.Join(" ", l.Select(x => $"{x:0.0}"))} LUFS");
            var theme = Mean(l[..8]);
            var breakdown = Mean(l[8..10]);
            var drop = Mean(l[12..16]);
            breakdown.Should().BeLessThan(theme - 2.5, "the breakdown (bars 8–9) drops out the drums and the reese");
            l[11].Should().BeGreaterThan(breakdown + 1.5, "the build rises out of the breakdown");
            drop.Should().BeGreaterThan(theme + 0.5, "the drop is the cycle's loudest stretch");
            PartLufs(all, MusicIdentity.Underglow, 11, 15, 16).Should().BeLessThan(l[11] - 10, "a gasp of silence before the drop");
            var sub = Band(all, 30, 60);
            var harmonics = Band(all, 100, 300);
            var barFrames = 16 * ThemeFramesPerSixteenth(MusicIdentity.Underglow);
            double Level(double[] x, int k) => Db(Rms(x, (int)(k * barFrames), (int)((k + 1) * barFrames)));
            var themeSub = Mean(Enumerable.Range(0, 8).Select(k => Level(sub, k)));
            var themeHarm = Mean(Enumerable.Range(0, 8).Select(k => Level(harmonics, k)));
            output.WriteLine($"drum and bass: the drop's first bar sub 30–60 Hz {Level(sub, 12):0.0} dBFS (theme bars {themeSub:0.0}), harmonics 100–300 Hz {Level(harmonics, 12):0.0} dBFS (theme bars {themeHarm:0.0})");
            Level(sub, 12).Should().BeGreaterThan(themeSub + 2, "a deeper drop: the sub drop and the heavy reese's sub");
            Level(harmonics, 12).Should().BeGreaterThan(themeHarm + 1.5, "and heard on small speakers: the harmonics rise with it");
        }

        // The orchestra: a general pause, then the climax well above the bars before it.
        {
            var (l, all) = BarLoudness(MusicIdentity.DeepWoods, PlanetTheme.Theatre);
            output.WriteLine($"orchestra per bar: {string.Join(" ", l.Select(x => $"{x:0.0}"))} LUFS");
            l[14].Should().BeGreaterThan(Mean(l[10..13]) + 2, "the bar-14 climax stands above the quiet bars before it");
            PartLufs(all, MusicIdentity.DeepWoods, 13, 14, 16).Should().BeLessThan(PartLufs(all, MusicIdentity.DeepWoods, 13, 8, 14) - 8, "the general pause before the climax");
        }

        // The festival: the stop under the drop's last note.
        {
            var (l, all) = BarLoudness(MusicIdentity.Trails, PlanetTheme.Library);
            output.WriteLine($"festival per bar: {string.Join(" ", l.Select(x => $"{x:0.0}"))} LUFS");
            PartLufs(all, MusicIdentity.Trails, 7, 12, 16).Should().BeLessThan(PartLufs(all, MusicIdentity.Trails, 6, 12, 16) - 3, "the beat stops under the drop's last note");
        }

        // The hazard world: the climb builds on the opening, the break drops away, the build climbs out of it.
        {
            var (l, all) = BarLoudness(MusicIdentity.Hazard, PlanetTheme.Vault);
            output.WriteLine($"hazard per bar: {string.Join(" ", l.Select(x => $"{x:0.0}"))} LUFS");
            var breakBars = Mean(l[12..14]);
            breakBars.Should().BeLessThan(Mean(l[4..8]) - 3, "the floor drops away in the break");
            l[15].Should().BeGreaterThan(breakBars + 2, "the build climbs out of it");
            PartLufs(all, MusicIdentity.Hazard, 15, 14, 16).Should().BeLessThan(l[15] - 10, "a gasp of silence before the theme returns");
        }
    }

    public static TheoryData<MusicIdentity> HazardScores() => new([MusicIdentity.Hazard, MusicIdentity.Abyss]);

    /// <summary>
    /// The edge warning held at the edge (nearness 1) through a whole cycle of the hazard score: in every bar, quiet ones and
    /// the busiest (the climb, the build), it stands ≥ 4 dB over the music it ducks (AD-52's bound, kept by AD-60).
    /// </summary>
    [Theory]
    [MemberData(nameof(HazardScores))]
    public void The_edge_warning_stands_clear_in_every_bar_of_the_hazard_score(MusicIdentity id)
    {
        var bar = 16 * ThemeFramesPerSixteenth(id);
        float[] Run(float music)
        {
            var s = SynthFor(30, PlanetTheme.Vault, id, musicVolume: music);
            var all = new List<float>();
            var frames = (int)(SkySynth.CycleBars * bar / 735) + 1;
            for (var f = 0; f < frames; f++)
            {
                s.HazardWarning(1f);
                all.AddRange(s.Render(735));
            }

            return [.. all];
        }

        var cue = Run(0);
        var with = Run(1);
        var worst = double.MaxValue;
        var line = new List<string>();
        for (var k = 0; k < SkySynth.CycleBars; k++)
        {
            double c = 0, m = 0;
            for (var i = 2 * (int)(k * bar); i < 2 * (int)((k + 1) * bar) && i < cue.Length; i++)
            {
                c += cue[i] * (double)cue[i];
                m += Math.Pow(with[i] - cue[i], 2);
            }

            var snr = 10 * Math.Log10(c / Math.Max(1e-12, m));
            worst = Math.Min(worst, snr);
            line.Add($"{snr:0.0}");
        }

        output.WriteLine($"{id}: warning over the ducked music per bar {string.Join(" ", line)} dB (worst {worst:0.0})");
        worst.Should().BeGreaterThan(4, $"{id}'s every bar leaves room for the warning");
    }

    public static TheoryData<MusicIdentity, int> DramaPeaks() => new()
    {
        { MusicIdentity.Underglow, 12 }, { MusicIdentity.Underglow, 14 }, { MusicIdentity.DeepWoods, 14 }, { MusicIdentity.Trails, 6 },
        { MusicIdentity.Hazard, 6 }, { MusicIdentity.Hazard, 15 }, { MusicIdentity.Hazard, 0 },
    };

    /// <summary>The fall and the return (the return lands in any world) still stand ≥ 4 dB clear at the loudest moments of the drama.</summary>
    [Theory]
    [MemberData(nameof(DramaPeaks))]
    public void Fall_and_return_stand_clear_at_the_drama_s_peaks(MusicIdentity id, int bar)
    {
        var at = (bar == 0 ? SkySynth.CycleBars : bar) * 16 * ThemeFramesPerSixteenth(id) / Rate + 0.05;
        (string Name, Action<SkySynth> Start, Action<SkySynth, int> Frame, double Seconds)[] cues =
        [
            ("fall", s => s.FallStart(), (s, f) => s.Fall(f / 60f), 1.0),
            ("return", s => s.FallReturn(), (_, _) => { }, 0.4),
        ];
        foreach (var (name, start, frame, seconds) in cues)
        {
            float[] Run(float music)
            {
                var s = SynthFor(30, PlanetTheme.Hearth, id, musicVolume: music);
                RenderSeconds(s, at);
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
            output.WriteLine($"{id} bar {bar}: {name} {snr:+0.0;-0.0} dB over the ducked music");
            snr.Should().BeGreaterThan(4, $"{id}'s bar {bar} leaves room for the {name} cue");
        }
    }
}
