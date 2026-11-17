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

        {
            // The hazard world: a flight in from the outskirts, ≈ 20 s on the surface (walking; the edge warning rising over
            // its last 7 s), the ground gives way, the fall, the return to the outskirts, and a few seconds there.
            var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Meadow, Rate, Band(1)));
            var s = r.Synth;
            r.Walk(1.0);
            var start = r.T;
            r.Flight(PlanetTheme.Vault, HazardWorld, 1);
            r.Events.Add($"arrival {r.T:0.00} s");
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
            SaveWindow(r, Path.Combine(dir, "hazard-arrival-surface-fall.wav"), start - 0.5, r.T);
        }
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

    /// <summary>Per bar of the route after <paramref name="downbeat"/>: the sub (30–60 Hz) and the low harmonics (100–300 Hz) in dBFS RMS, and the loudness.</summary>
    private void ReportBands(Route r, string name, double downbeat, double bar)
    {
        var all = r.Samples.ToArray();
        var sub = Band(all, 30, 60);
        var harm = Band(all, 100, 300);
        for (var k = 0; k < 17; k++)
        {
            var a = (int)((downbeat + k * bar) * Rate);
            var b = (int)((downbeat + (k + 1) * bar) * Rate);
            if (b > sub.Length) break;
            var seg = all[(2 * a)..(2 * b)];
            output.WriteLine($"{name} bar {k,2}: sub 30–60 Hz {Db(Rms(sub, a, b)):0.0} dBFS, harmonics 100–300 Hz {Db(Rms(harm, a, b)):0.0} dBFS, {Lufs(seg):0.0} LUFS");
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
}
