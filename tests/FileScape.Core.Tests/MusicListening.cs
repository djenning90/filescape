using FileScape.Core.Audio;

namespace FileScape.Core.Tests;

/// <summary>
/// Measures for "listening" to music analytically (PLANET-172, AD-95): what a listener would notice over minutes and
/// revisits — loudness and its consistency, how often an identical bar or phrase comes back, how many different pitches and
/// rhythms the tune uses, how dense and bright the sound is — from the notes (where they are known) and from the audio
/// (for every piece alike, including the generated classic hub). Used by SkySynthStarterThemeTests' cheap checks and its
/// evidence render; nothing in the game reads it.
/// <para>What a change must preserve: <see cref="Lufs"/> is ITU-R BS.1770 integrated loudness (the same algorithm as
/// SkySynthTests' 44.1 kHz copy, here for any rate), so its numbers compare with AD-52/AD-79's; the bar measures work on
/// whole bars from the render's start (pieces start on a downbeat).</para>
/// </summary>
internal static class MusicListening
{
    /// <summary>Two bars' chroma vectors at least this similar (cosine) count as "the same bar again" to the ear: near-identical pitch content and weight.</summary>
    public const double SameBarSimilarity = 0.97;

    /// <summary>The short-term loudness window, seconds (BS.1771's 3 s short-term loudness).</summary>
    private const double ShortTermSeconds = 3;

    /// <summary>
    /// Integrated loudness after ITU-R BS.1770 (K-weighting, 400 ms blocks with 75 % overlap, the −70 LUFS absolute and
    /// −10 LU relative gates), in LUFS, of interleaved stereo at <paramref name="rate"/>.
    /// </summary>
    public static double Lufs(float[] stereo, int rate)
    {
        var (powers, _) = BlockPowers(stereo, rate, 0.4, 0.1);
        static double L(double power) => -0.691 + 10 * Math.Log10(Math.Max(1e-12, power));
        var absolute = powers.Where(z => L(z) > -70).ToList();
        if (absolute.Count == 0) return -70;
        var relative = L(absolute.Average()) - 10;
        return L(absolute.Where(z => L(z) > relative).Average());
    }

    /// <summary>
    /// The spread of short-term (3 s) loudness, LU: the 95th minus the 10th percentile of the gated windows (as EBU's loudness
    /// range): how much the piece swells and recedes. A few LU breathes; near 0 is a flat wall; over 10 has quiet gaps.
    /// </summary>
    public static double LoudnessRange(float[] stereo, int rate)
    {
        var (powers, _) = BlockPowers(stereo, rate, ShortTermSeconds, 1);
        var l = powers.Select(p => -0.691 + 10 * Math.Log10(Math.Max(1e-12, p))).Where(x => x > -70).ToList();
        if (l.Count < 2) return 0;
        var gate = 10 * Math.Log10(l.Select(x => Math.Pow(10, x / 10)).Average()) - 20;
        var kept = l.Where(x => x > gate).OrderBy(x => x).ToList();
        return Percentile(kept, 0.95) - Percentile(kept, 0.10);
    }

    private static double Percentile(List<double> sorted, double q) => sorted[(int)Math.Clamp(Math.Round(q * (sorted.Count - 1)), 0, sorted.Count - 1)];

    /// <summary>K-weighted mean-square power per block (<paramref name="blockSeconds"/> long, every <paramref name="hopSeconds"/>), both channels summed.</summary>
    private static (List<double> Powers, int Hop) BlockPowers(float[] stereo, int rate, double blockSeconds, double hopSeconds)
    {
        var frames = stereo.Length / 2;
        var weighted = new double[2][];
        for (var ch = 0; ch < 2; ch++)
        {
            var x = new double[frames];
            for (var i = 0; i < frames; i++) x[i] = stereo[2 * i + ch];
            foreach (var (b0, b1, b2, a1, a2) in new[] { Shelf(rate), HighPass(rate) })
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

        var block = (int)(blockSeconds * rate);
        var hop = Math.Max(1, (int)(hopSeconds * rate));
        var powers = new List<double>();
        for (var start = 0; start + block <= frames; start += hop)
        {
            double sum = 0;
            for (var i = start; i < start + block; i++) sum += weighted[0][i] * weighted[0][i] + weighted[1][i] * weighted[1][i];
            powers.Add(sum / block);
        }

        return (powers, hop);
    }

    // The K-weighting filter's two biquads (BS.1770's high shelf and RLB high-pass), designed for the rate.
    private static (double, double, double, double, double) Shelf(int rate)
    {
        const double f0 = 1681.974450955533, g = 3.999843853973347, q = 0.7071752369554196;
        var k = Math.Tan(Math.PI * f0 / rate);
        var vh = Math.Pow(10, g / 20);
        var vb = Math.Pow(vh, 0.4996667741545416);
        var a0 = 1 + k / q + k * k;
        return ((vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
    }

    private static (double, double, double, double, double) HighPass(int rate)
    {
        const double f0 = 38.13547087602444, q = 0.5003270373238773;
        var k = Math.Tan(Math.PI * f0 / rate);
        var a0 = 1 + k / q + k * k;
        return (1, -2, 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
    }

    /// <summary>The largest absolute sample.</summary>
    public static float Peak(float[] s) => s.Length == 0 ? 0 : s.Max(MathF.Abs);

    /// <summary>Onsets per second: 10 ms frames whose high-passed energy jumps above 1.6× the previous three (and a floor) — SkySynthTests' "bounce" detector.</summary>
    public static double OnsetRate(float[] stereo, int rate)
    {
        var hop = rate / 100;
        var frames = stereo.Length / 2;
        var e = new List<double>();
        for (var f = 0; f + hop <= frames; f += hop)
        {
            double x = 0;
            for (var i = Math.Max(1, f); i < f + hop; i++)
            {
                var hp = stereo[2 * i] - stereo[2 * i - 2];
                x += hp * (double)hp;
            }

            e.Add(x);
        }

        if (e.Count < 4) return 0;
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

        return onsets / (frames / (double)rate);
    }

    /// <summary>Energy in second differences as a fraction of the total (SkySynthTests' HighShare): a brightness index, higher for hats, glass and open filters.</summary>
    public static double Brightness(float[] s)
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

    /// <summary>
    /// A chroma vector (12 pitch classes, C first, unit length) per bar of <paramref name="barSeconds"/>: the energy at every
    /// equal-tempered pitch from C2 to B6 (Goertzel on the mono mix, decimated to half rate), folded into its pitch class.
    /// What a listener hears as "which notes, how strongly" in each bar.
    /// </summary>
    public static double[][] BarChroma(float[] stereo, int rate, double barSeconds)
    {
        const int lowMidi = 36, highMidi = 95;
        var decimatedRate = rate / 2.0;
        var mono = new float[stereo.Length / 4];
        for (var i = 0; i < mono.Length; i++) mono[i] = 0.25f * (stereo[4 * i] + stereo[4 * i + 1] + stereo[4 * i + 2] + stereo[4 * i + 3]);
        var barFrames = (int)(barSeconds * decimatedRate);
        var bars = mono.Length / barFrames;
        var result = new double[bars][];
        var coef = new double[highMidi - lowMidi + 1];
        for (var m = lowMidi; m <= highMidi; m++) coef[m - lowMidi] = 2 * Math.Cos(2 * Math.PI * 440 * Math.Pow(2, (m - 69) / 12.0) / decimatedRate);
        for (var b = 0; b < bars; b++)
        {
            var chroma = new double[12];
            for (var m = lowMidi; m <= highMidi; m++)
            {
                double s1 = 0, s2 = 0;
                var c = coef[m - lowMidi];
                for (var i = b * barFrames; i < (b + 1) * barFrames; i++)
                {
                    var s0 = mono[i] + c * s1 - s2;
                    s2 = s1;
                    s1 = s0;
                }

                chroma[m % 12] += Math.Sqrt(Math.Max(0, s1 * s1 + s2 * s2 - c * s1 * s2));
            }

            var norm = Math.Sqrt(chroma.Sum(x => x * x));
            result[b] = norm > 0 ? [.. chroma.Select(x => x / norm)] : chroma;
        }

        return result;
    }

    /// <summary>
    /// Bar recurrence from chroma: for each bar after the first, its similarity to the most similar earlier bar; the share
    /// at or above <see cref="SameBarSimilarity"/> ("an identical-sounding bar again") and the mean of those best matches.
    /// </summary>
    public static (double SameBarShare, double MeanBestMatch) BarRecurrence(double[][] chroma)
    {
        if (chroma.Length < 2) return (0, 0);
        int same = 0;
        double sum = 0;
        for (var i = 1; i < chroma.Length; i++)
        {
            var best = 0.0;
            for (var j = 0; j < i; j++) best = Math.Max(best, Dot(chroma[i], chroma[j]));
            sum += best;
            if (best >= SameBarSimilarity) same++;
        }

        return (same / (double)(chroma.Length - 1), sum / (chroma.Length - 1));
    }

    /// <summary>The entropy (bits, of 3.58 at most) of the whole render's pitch-class energy: how many different notes are heard, weighted by how much.</summary>
    public static double ChromaEntropy(double[][] chroma)
    {
        var total = new double[12];
        foreach (var c in chroma)
        {
            for (var k = 0; k < 12; k++) total[k] += c[k] * c[k];
        }

        return Entropy(total);
    }

    private static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (var k = 0; k < a.Length; k++) s += a[k] * b[k];
        return s;
    }

    /// <summary>Shannon entropy in bits of a histogram (counts or weights).</summary>
    public static double Entropy(IEnumerable<double> weights)
    {
        var w = weights.Where(x => x > 0).ToArray();
        var sum = w.Sum();
        return sum <= 0 ? 0 : -w.Sum(x => x / sum * Math.Log2(x / sum));
    }

    /// <summary>A melodic onset: its time from the start in sixteenths (of the piece's tempo) and its MIDI pitch.</summary>
    public readonly record struct Onset(long Sixteenth, int Midi);

    /// <summary>
    /// The melody's long-form measures over <paramref name="bars"/> bars: the share of bars with melody; distinct melodic
    /// bars among them (an identical bar, same onsets and pitches, counts once); the share of four-bar phrases (aligned to
    /// four bars) that repeat an earlier one exactly; pitch-class entropy of the onsets; distinct rhythms (onset patterns)
    /// among melodic bars; melody notes per minute; the share of leaps (more than two semitones); the range in semitones.
    /// </summary>
    public static MelodyMeasures Melody(IReadOnlyList<Onset> onsets, int bars, double beatsPerMinute)
    {
        var perBar = new List<(int Step, int Midi)>[bars];
        for (var b = 0; b < bars; b++) perBar[b] = [];
        foreach (var o in onsets.OrderBy(o => o.Sixteenth).ThenBy(o => o.Midi))
        {
            var b = (int)(o.Sixteenth / 16);
            if (b >= 0 && b < bars) perBar[b].Add(((int)(o.Sixteenth % 16), o.Midi));
        }

        string Sig(int b) => string.Join(",", perBar[b].Select(x => $"{x.Step}:{x.Midi}"));
        string Rhythm(int b) => string.Join(",", perBar[b].Select(x => x.Step).Distinct());
        var melodic = Enumerable.Range(0, bars).Where(b => perBar[b].Count > 0).ToList();
        var phrases = Enumerable.Range(0, bars / 4).Select(p => string.Join("|", Enumerable.Range(4 * p, 4).Select(Sig))).ToList();
        var repeated = phrases.Where((p, i) => p.Replace("|", "").Length > 0 && phrases.Take(i).Contains(p)).Count();
        var pcs = new double[12];
        foreach (var o in onsets) pcs[((o.Midi % 12) + 12) % 12]++;
        var ordered = onsets.OrderBy(o => o.Sixteenth).ToList();
        var leaps = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            if (Math.Abs(ordered[i].Midi - ordered[i - 1].Midi) > 2) leaps++;
        }

        var minutes = bars * 4 / beatsPerMinute;
        return new MelodyMeasures(
            melodic.Count / (double)bars,
            melodic.Count == 0 ? 0 : melodic.Select(Sig).Distinct().Count() / (double)melodic.Count,
            phrases.Count == 0 ? 0 : repeated / (double)phrases.Count,
            Entropy(pcs),
            melodic.Count == 0 ? 0 : melodic.Select(Rhythm).Distinct().Count() / (double)melodic.Count,
            onsets.Count / minutes,
            ordered.Count < 2 ? 0 : leaps / (double)(ordered.Count - 1),
            onsets.Count == 0 ? 0 : onsets.Max(o => o.Midi) - onsets.Min(o => o.Midi));
    }

    /// <summary>The melody's long-form measures (see <see cref="Melody"/>).</summary>
    public sealed record MelodyMeasures(double MelodicBarShare, double DistinctBarShare, double RepeatedPhraseShare, double PitchClassEntropy,
        double DistinctRhythmShare, double NotesPerMinute, double LeapShare, int RangeSemitones);

    /// <summary>
    /// A piece's melody onsets (its <see cref="SketchPart.Melody"/> notes) as played from the top for <paramref name="cycles"/>
    /// cycles from <paramref name="firstCycle"/> (the variations taking turns, as a deck plays them), transposed by
    /// <paramref name="transpose"/> semitones.
    /// </summary>
    public static List<Onset> PieceMelody(ExperiencePiece piece, int cycles, int firstCycle = 0, int transpose = 0)
    {
        var list = new List<Onset>();
        for (var c = 0; c < cycles; c++)
        {
            foreach (var s in piece.Steps(piece.VariationOf(firstCycle + c)))
            {
                if (s.Note.Part == SketchPart.Melody) list.Add(new Onset(c * (long)ExperiencePiece.CycleSixteenths + s.Step, s.Note.MidiNote + transpose));
            }
        }

        return list;
    }

    /// <summary>
    /// The most voices a score holds at once (estimate): notes from their start to their written end plus
    /// <paramref name="ringSeconds"/> (a typical release), counted at every onset.
    /// </summary>
    public static int PeakVoices(SketchScore score, double ringSeconds)
    {
        var spans = score.Notes.Select(n => (Start: n.Start.TotalSeconds, End: (n.Start + n.Length).TotalSeconds + ringSeconds)).ToList();
        return spans.Max(a => spans.Count(b => b.Start <= a.Start && b.End > a.Start));
    }

    /// <summary>Writes interleaved stereo as a 16-bit PCM WAV file.</summary>
    public static void WriteWav(string path, ReadOnlySpan<float> interleaved, int rate)
    {
        using var w = new BinaryWriter(File.Create(path));
        var dataBytes = interleaved.Length * 2;
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
