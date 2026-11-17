using System.Diagnostics;
using FileScape.Core.Audio;
using FileScape.Core.Planets;

namespace FileScape.Core.Tests;

/// <summary>PLANET-65 (AD-66): the folder bands' second themes: where they play, how they read as notes, the mix, the warning's room and the cost.</summary>
public partial class SkySynthTests
{
    public static TheoryData<MusicIdentity> FolderBands() => new([MusicIdentity.Outskirts, MusicIdentity.Trails, MusicIdentity.DeepWoods, MusicIdentity.Underglow]);

    private static bool PlaysLanternSong(MusicIdentity id) => id is MusicIdentity.Outskirts or MusicIdentity.DeepWoods;

    /// <summary>A synth playing <paramref name="id"/> from its first downbeat at the given visit (a retarget before the downbeat, so it starts as a fresh visit would).</summary>
    private static SkySynth SynthAtVisit(int seed, PlanetTheme theme, MusicIdentity id, int visits, float musicVolume = 1f, int rate = Rate)
    {
        var s = SynthFor(seed, theme, id, musicVolume, rate);
        s.SetWorld(theme, CharacterOf(id), visits, id);
        return s;
    }

    /// <summary>The melodic notes of <paramref name="part"/> over <paramref name="seconds"/> of a synth at the low test rate.</summary>
    private static List<MelodyNote> Observe(SkySynth s, double seconds, Func<MelodyNote, bool> keep)
    {
        var notes = new List<MelodyNote>();
        s.MelodyObserver = n =>
        {
            if (keep(n)) notes.Add(n);
        };
        var buf = new float[4096 * 2];
        for (long done = 0; done < (long)(seconds * LowRate); done += 4096) s.Render(buf);
        return notes;
    }


    /// <summary>
    /// The second themes (AD-66) play only where they belong: in the folder bands, never in the first cycle of a first visit
    /// (the arrival as before), in bars 8–11 of every odd cycle and of a revisit's first free section (unless an augmented
    /// ending holds bar 8), and in no other world at any visit.
    /// </summary>
    [Theory]
    [MemberData(nameof(Identities))]
    public void The_second_theme_plays_on_revisits_and_odd_cycles_in_the_folder_bands_only(MusicIdentity id)
    {
        var band = id is >= MusicIdentity.Outskirts and <= MusicIdentity.Underglow;
        var fps = ThemeFramesPerSixteenth(id, LowRate);
        var cycleFrames = fps * 16 * SkySynth.CycleBars;
        for (var visits = 1; visits <= 3; visits++)
        {
            var notes = Observe(SynthAtVisit(11, PlanetTheme.Library, id, visits, rate: LowRate), 2 * cycleFrames / LowRate + 1, n => n.Part is MelodyPart.Theme or MelodyPart.SecondTheme);
            var second = notes.Where(n => n.Part == MelodyPart.SecondTheme).ToList();
            if (!band)
            {
                second.Should().BeEmpty($"{id} keeps its score unchanged (visit {visits})");
                continue;
            }

            var heard = false; // a revisit plays it in its first free section that has room (an augmented ending takes bar 8)
            for (var c = 0L; c < 2; c++)
            {
                var variation = notes.First(n => n.Part == MelodyPart.Theme && (long)(n.Frame / cycleFrames) == c).Variation;
                var inCycle = second.Where(n => (long)(n.Frame / cycleFrames) == c).ToList();
                var absCycle = c + visits - 1; // a forgotten revisit starts on a later cycle (its development)
                var expected = (absCycle % 2 == 1 || (visits >= 2 && !heard)) && variation != ThemeVariation.AugmentedEnding;
                heard |= expected;
                output.WriteLine($"{id} visit {visits} cycle {absCycle} ({variation}): {inCycle.Count} second-theme notes");
                if (!expected)
                {
                    inCycle.Should().BeEmpty($"{id}'s cycle {absCycle} keeps the band motif (visit {visits})");
                    continue;
                }

                inCycle.Count.Should().Be(PlaysLanternSong(id) ? 14 : 19, $"{id} plays the whole second theme in cycle {absCycle}");
                foreach (var n in inCycle) ((long)(n.Frame / (fps * 16)) % SkySynth.CycleBars).Should().BeInRange(8, 11, "the second theme has bars 8–11");
            }
        }
    }

    /// <summary>
    /// Read back as notes, in every key: in the key's scale, within the lead's middle range, singable (no leap beyond a
    /// major sixth), and the shapes as written: the lantern song opens with a falling sigh, the trail call with its rising call.
    /// </summary>
    [Theory]
    [MemberData(nameof(FolderBands))]
    public void The_second_themes_stay_in_key_in_range_and_singable_in_every_key(MusicIdentity id)
    {
        var fps = ThemeFramesPerSixteenth(id, LowRate);
        foreach (var theme in Enum.GetValues<PlanetTheme>())
        {
            var key = SkySynth.KeyFor(theme, CharacterOf(id));
            var line = Observe(SynthAtVisit(3, theme, id, 2, rate: LowRate), 13 * 16 * fps / LowRate, n => n.Part == MelodyPart.SecondTheme).Select(n => n.MidiNote).ToList();
            output.WriteLine($"{id} in {theme} (root {key.RootMidiNote}, {key.Mode}): {string.Join(" ", line)}");
            if (line.Count == 0) continue; // an augmented ending took bar 8 this cycle
            var root = key.RootMidiNote;
            foreach (var m in line)
            {
                key.IntervalsSemitones.Should().Contain(((m - root) % 12 + 12) % 12, $"{id} in {theme}: every note in the key");
                m.Should().BeInRange(root + 3, root + 36, $"{id} in {theme}: in the lead's middle range");
            }

            for (var i = 1; i < line.Count; i++) Math.Abs(line[i] - line[i - 1]).Should().BeLessThanOrEqualTo(9, $"{id} in {theme}: no leap beyond a major sixth");
            if (PlaysLanternSong(id)) (line[0] >= line[1] && line[1] >= line[2] && line[2] >= line[3] && line[0] > line[3]).Should().BeTrue($"{id} in {theme}: the lantern song opens with a falling sigh");
            else (line[1] > line[0] && line[2] >= line[1]).Should().BeTrue($"{id} in {theme}: the trail call opens rising");
        }
    }

    /// <summary>
    /// The second theme's bars sit in the mix like the band motif's (no louder than the theme's own bars of that cycle +1 dB,
    /// inside −31…−24 LUFS), never clip (peak ≤ 0.4), and leave the edge warning ≥ 4 dB of room in each of its bars (AD-52's bound).
    /// </summary>
    [Theory]
    [MemberData(nameof(FolderBands))]
    public void The_second_theme_keeps_the_mix_and_the_warning_s_room(MusicIdentity id)
    {
        var bar = 16 * ThemeFramesPerSixteenth(id);
        var all = RenderSeconds(SynthAtVisit(2026, PlanetTheme.Library, id, 2), (SkySynth.CycleBars + 0.5) * bar / Rate);
        double Section(int a, int b) => Lufs(all[(2 * (int)(a * bar))..(2 * (int)(b * bar))]);
        var themeBars = Section(0, 8);
        var second = Section(8, 12);
        var peak = Measure(all).Peak;
        output.WriteLine($"{id} revisit: theme bars {themeBars:0.0} LUFS, second-theme bars {second:0.0} LUFS, whole cycle {Lufs(all):0.0} LUFS, peak {peak:0.000}");
        second.Should().BeLessThanOrEqualTo(themeBars + 1, "the theme's return stays the headline");
        second.Should().BeInRange(-31, -24);
        peak.Should().BeLessThanOrEqualTo(0.4f, "music peaks stay where AD-52 measured them");

        float[] Run(float music)
        {
            var s = SynthAtVisit(2026, PlanetTheme.Library, id, 2, music);
            var o = new List<float>();
            for (var f = 0; f < (int)(12 * bar / 735) + 1; f++)
            {
                if (f * 735 >= 8 * bar) s.HazardWarning(1f);
                o.AddRange(s.Render(735));
            }

            return [.. o];
        }

        var cue = Run(0);
        var with = Run(1);
        for (var k = 8; k < 12; k++)
        {
            double c = 0, m = 0;
            for (var i = 2 * ((int)(k * bar) + Rate / 2); i < 2 * (int)((k + 1) * bar) && i < cue.Length; i++) // from half a second into the bar: the warning has risen
            {
                c += cue[i] * (double)cue[i];
                m += Math.Pow(with[i] - cue[i], 2);
            }

            var snr = 10 * Math.Log10(c / Math.Max(1e-12, m));
            output.WriteLine($"{id} bar {k}: warning {snr:+0.0} dB over the ducked music");
            snr.Should().BeGreaterThan(4, $"{id}'s second-theme bar {k} leaves room for the warning");
        }
    }

    /// <summary>The second theme costs nothing measurable: a revisit's bars 8–11 with cues render within the usual bound (60 ms per second of audio, Debug).</summary>
    [Theory]
    [MemberData(nameof(FolderBands))]
    public void Rendering_a_revisit_with_the_second_theme_is_cheap(MusicIdentity id)
    {
        var bar = 16 * ThemeFramesPerSixteenth(id);
        var best = double.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var s = SynthAtVisit(run, PlanetTheme.Wild, id, 2);
            s.Render((int)(8.5 * bar)); // into the second theme
            var buffer = new float[Frames(1) * 2];
            var sw = Stopwatch.StartNew();
            s.Launch();
            s.Arrive();
            s.Chime(1);
            for (var chunk = 0; chunk < 20; chunk++)
            {
                s.Fly(chunk / 20f);
                s.HazardWarning(0.8f);
                s.Render(buffer.AsSpan(chunk * buffer.Length / 20, buffer.Length / 20));
            }

            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }

        output.WriteLine($"{id} revisit (second theme): best {best:0.00} ms per second of audio");
        best.Should().BeLessThan(60);
    }
}
