using FileScape.Core.Audio;
using FileScape.Core.Planets;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>
/// What the SkySynth test classes share (TEST-10, AD-90): the sample rate and frame arithmetic, the representative world
/// characters per music identity, a synth playing an identity from its first downbeat, rendering, level, loudness (LUFS),
/// onset and theme-match measurements, the theory data over identities, and the scripted Route with its .wav writer.
/// Why it exists: the SkySynth tests were one partial class over nine files, so xUnit ran all ≈ 200 of them as one
/// collection, one after another (≈ 310 s on WSL, the whole suite's critical path). Each file (and each section of
/// SkySynthTests.cs) is now its own class, so its own collection, and they reach these helpers with
/// <c>using static FileScape.Core.Tests.SkySynthKit;</c>.
/// What a change must preserve: everything here is a pure function of its arguments or an immutable value (classes run
/// in parallel); theory-data providers are referenced as <c>[MemberData(nameof(X), MemberType = typeof(SkySynthKit))]</c>.
/// A helper used by one class only stays in that class.
/// </summary>
internal static class SkySynthKit
{
    internal const int Rate = SkySynth.DefaultSampleRateHz;

    internal static int Frames(double seconds) => (int)Math.Round(seconds * Rate);

    internal static (float Peak, float MaxJump, double Rms) Measure(float[] s)
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

    internal static void FireEverything(SkySynth s)
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

    internal const int LowRate = 8000;
    internal static readonly WorldCharacter Hub = new(WorldKind.Hub, 0, 0, -1);
    internal static readonly WorldCharacter Windows = new(WorldKind.WindowsRoot, 0, 0, -1);
    internal static readonly WorldCharacter Wsl = new(WorldKind.WslRoot, 0, 0, -1);
    internal static readonly WorldCharacter Repo = new(WorldKind.Repository, 3, 0, 42);
    internal static readonly WorldCharacter Worktree = new(WorldKind.Repository, 3, 0, 43, Worktree: true);
    internal static readonly WorldCharacter HazardWorld = new(WorldKind.Hazard, 2, 0, -1);

    internal static WorldCharacter Band(int band, int repositoryId = -1) =>
        new(WorldKind.Folder, band switch { 1 => 1, 2 => 2, 3 => 4, _ => 6 }, band, repositoryId);

    /// <summary>One representative character per identity.</summary>
    public static TheoryData<MusicIdentity> Identities() => new(Enum.GetValues<MusicIdentity>());

    internal static WorldCharacter CharacterOf(MusicIdentity id) => id switch
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
    internal static SkySynth SynthFor(int seed, PlanetTheme theme, MusicIdentity id, float musicVolume = 1f, int rate = Rate)
    {
        var s = new SkySynth(seed, theme, rate, CharacterOf(id)) { MusicVolume = musicVolume };
        if (SkySynth.IdentityOf(CharacterOf(id)) != id) s.SetWorld(theme, CharacterOf(id), 1, id);
        return s;
    }

    internal static double FramesPerSixteenth(MusicIdentity id, int rate = Rate) => rate * 60.0 / (SkySynth.TempoFor(id) * 4.0);

    internal static float[] RenderSeconds(SkySynth s, double seconds)
    {
        var all = new float[Frames(seconds) * 2];
        for (var done = 0; done < all.Length; done += 735 * 2) s.Render(all.AsSpan(done, Math.Min(735 * 2, all.Length - done)));
        return all;
    }

    /// <summary>The first frame (after <paramref name="from"/>) where |x| exceeds the threshold, or −1.</summary>
    internal static long FirstSound(float[] buffer, long from, float threshold = 1e-6f)
    {
        var i = Array.FindIndex(buffer, x => MathF.Abs(x) > threshold);
        return i < 0 ? -1 : from + i / 2;
    }

    /// <summary>The frame of the first beat at or after <paramref name="frame"/> on a clock starting at <paramref name="origin"/>.</summary>
    internal static long NextBeat(long frame, long origin, double framesPerSixteenth)
    {
        var k = (long)Math.Ceiling((frame - origin) / (4 * framesPerSixteenth) - 1e-9);
        return origin + (long)Math.Ceiling(4 * k * framesPerSixteenth - 1e-6);
    }

    /// <summary>Energy above ≈ 7 kHz (first differences of first differences), as a fraction of the total: hats, shakers, glass.</summary>
    internal static double HighShare(float[] s)
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
    internal static double OnsetRate(float[] s)
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

    internal static int Midi(MusicalKey key, int degree)
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
    internal static double ThemeMatch(IReadOnlyList<MelodyNote> onsets, MusicalKey key, int startBar, double framesPerSixteenth)
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

    internal static readonly MusicIdentity[] NewArrangements = [MusicIdentity.Trails, MusicIdentity.DeepWoods, MusicIdentity.Underglow, MusicIdentity.Hazard];

    /// <summary>The identities whose AD-49 arrangements are still in use: the reference for loudness.</summary>
    internal static readonly MusicIdentity[] CurrentArrangements = [MusicIdentity.Hub, MusicIdentity.WindowsRoot, MusicIdentity.WslRoot, MusicIdentity.Repository, MusicIdentity.Outskirts];

    public static TheoryData<MusicIdentity> NewArrangementIds() => new(NewArrangements);

    /// <summary>Frames per sixteenth of the theme (the score: half the pulse in a half-time arrangement).</summary>
    internal static double ThemeFramesPerSixteenth(MusicIdentity id, int rate = Rate) => rate * 60.0 / (SkySynth.ThemeTempoFor(id) * 4.0);

    /// <summary>
    /// Integrated loudness after ITU-R BS.1770 (K-weighting, 400 ms blocks with 75 % overlap, the −70 LUFS absolute and
    /// −10 LU relative gates), in LUFS, of interleaved stereo at <see cref="Rate"/>.
    /// </summary>
    internal static double Lufs(float[] s)
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

    internal static double Db(double linear) => 20 * Math.Log10(Math.Max(1e-9, linear));

    /// <summary>A scripted playthrough in 60 Hz frames: walking, a launch (wind-up aligned to the grid), flight, a landing and arrival.</summary>
    internal sealed class Route(SkySynth s)
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

    internal static void WriteWav(string path, List<float> interleaved, int rate)
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

    /// <summary>Saves the samples between two route times (seconds) and logs the clip's peak, loudness and events.</summary>
    internal static void SaveWindow(ITestOutputHelper output, Route r, string path, double from, double to)
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
    internal static void ReportBands(ITestOutputHelper output, Route r, string name, double downbeat, double bar)
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

    internal static double Rms(double[] x, int a, int b)
    {
        double sum = 0;
        for (var i = a; i < b; i++) sum += x[i] * x[i];
        return Math.Sqrt(sum / Math.Max(1, b - a));
    }

    /// <summary>The mono sum of interleaved stereo, band-passed between two frequencies (fourth-order Butterworth each side).</summary>
    internal static double[] Band(float[] stereo, double lowHz, double highHz)
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

    /// <summary>A 50 ms window quieter than this (RMS, ≈ −60 dBFS) counts as silence in the gap scan.</summary>
    internal const double SilenceRms = 0.001;

    /// <summary>The gap scan's window, seconds.</summary>
    internal const double SilenceWindowSeconds = 0.05;

    /// <summary>The soft clipper bends samples above this (SkySynth.SoftClip): a render whose peak stays below it is unclipped.</summary>
    internal const float SoftClipFrom = 0.85f;

    /// <summary>The peak, and how long (seconds) the 50 ms windows between 0.2 s and <paramref name="untilSeconds"/> are silent in all; also each silent run's start and length.</summary>
    internal static (float Peak, double SilentSeconds, List<(double At, double Length)> Runs) Scan(float[] x, double untilSeconds)
    {
        var peak = x.Max(MathF.Abs);
        var window = (int)(SilenceWindowSeconds * Rate);
        var runs = new List<(double, double)>();
        double silent = 0;
        double? runStart = null;
        for (var f = (int)(0.2 * Rate); f + window <= Math.Min(x.Length / 2, (int)(untilSeconds * Rate)); f += window)
        {
            double sum = 0;
            for (var i = f; i < f + window; i++) sum += x[2 * i] * x[2 * i] + x[2 * i + 1] * x[2 * i + 1];
            var quiet = Math.Sqrt(sum / (2 * window)) < SilenceRms;
            var t = f / (double)Rate;
            if (quiet)
            {
                silent += SilenceWindowSeconds;
                runStart ??= t;
            }
            else if (runStart is { } s)
            {
                runs.Add((s, t - s));
                runStart = null;
            }
        }

        if (runStart is { } last) runs.Add((last, untilSeconds - last));
        return (peak, silent, runs);
    }
}
