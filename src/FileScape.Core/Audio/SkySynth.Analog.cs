using System.Runtime.CompilerServices;

namespace FileScape.Core.Audio;

// AD-79 (PLANET-99), the DSP extension: an expressive subtractive voice ("aurora") in the spirit of classic analog
// polysynths, written from scratch. Two or three detuned polyBLEP saws (the existing stack) are driven into a soft
// saturator and then a resonant, topology-preserving state-variable low-pass (12 dB per octave), whose cutoff rises and
// falls with its own attack-decay-sustain envelope and follows the amplitude's release. Each note drifts a few cents from
// true. The voice is a Wave of the shared pool (no extra voices, no allocation); every other wave renders exactly as before.
// What a change must preserve: the filter stays stable for any cutoff up to 0.45 × the sample rate and any resonance up to
// AnalogMaxResonance (the trapezoidal SVF is stable for any positive coefficients, recomputed here at the control rate);
// the drive's make-up keeps a full-scale saw near full scale; nothing allocates per sample.
public sealed partial class SkySynth
{
    /// <summary>
    /// The highest resonance a note may ask for (0..1 of the way to self-oscillation): 0.95 is damping 0.1, Q 10, a
    /// pronounced peak (≈ +20 dB at the cutoff) that still decays; the aurora sounds use 0.2–0.6.
    /// </summary>
    private const float AnalogMaxResonance = 0.95f;

    /// <summary>
    /// Renders frames <paramref name="i"/>…<paramref name="end"/> (one control period) of an analog voice into
    /// <paramref name="mono"/>: the filter envelope and the filter's coefficients once for the period (one tan), then per
    /// frame the saw stack, the drive and the filter. Returns the frame after the last one rendered.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int RenderAnalog(Voice v, int i, int end, float inc, float amp, float dAmp, bool released, float[] mono)
    {
        var m = end - i;
        // The filter envelope: a linear rise over FilterAttack, an exponential fall to FilterSustain (T60 FilterDecay), and
        // with the note's release the amplitude's own release, so the tone darkens as it fades.
        if (released)
        {
            v.Fenv *= m == ControlFrames ? v.ReleaseCoefControl : MathF.Pow(v.ReleaseCoef, m);
            v.FenvRising = false;
        }
        else if (v.FenvRising)
        {
            v.Fenv += v.FenvInc * m / ControlFrames;
            if (v.Fenv >= 1f)
            {
                v.Fenv = 1f;
                v.FenvRising = false;
            }
        }
        else
        {
            var coef = m == ControlFrames ? v.FenvDecayCoefControl : MathF.Pow(v.FenvDecayCoefControl, m / (float)ControlFrames);
            v.Fenv = v.FenvSustain + (v.Fenv - v.FenvSustain) * coef;
        }

        var cutoff = MathF.Min(v.FenvBaseHz * MathF.Pow(2f, v.FenvOctaves * v.Fenv), 0.45f * _rate);
        var g = MathF.Tan(MathF.PI * cutoff * _invRate);
        var k = v.FilterDamping;
        var a1 = 1f / (1f + g * (g + k));
        var a2 = g * a1;
        var a3 = g * a2;
        float ic1 = v.Ic1, ic2 = v.Ic2;
        float p0 = v.U0, p1 = v.U1, p2 = v.U2;
        float i0 = inc * v.D0, i1 = inc * v.D1, i2 = inc * v.D2;
        float w2 = v.W2, norm = v.UnisonNorm, drive = v.Drive, makeUp = v.DriveNorm;
        var copies = v.D1 == 1f && w2 == 0f ? 1 : w2 == 0f ? 2 : 3; // as the saw stack: a single saw, a pair or three
        for (; i < end; i++)
        {
            var x = BlepSaw(p0, i0);
            p0 += i0;
            if (p0 >= 1f) p0 -= 1f;
            if (copies > 1)
            {
                x += BlepSaw(p1, i1);
                p1 += i1;
                if (p1 >= 1f) p1 -= 1f;
                if (copies > 2)
                {
                    x += BlepSaw(p2, i2);
                    p2 += i2;
                    if (p2 >= 1f) p2 -= 1f;
                }
            }

            x = SoftSaturate(x * norm * drive) * makeUp;
            // The trapezoidal (topology-preserving) state-variable filter, low-pass output.
            var v3 = x - ic2;
            var v1 = a1 * ic1 + a2 * v3;
            var v2 = ic2 + a2 * ic1 + a3 * v3;
            ic1 = 2f * v1 - ic1;
            ic2 = 2f * v2 - ic2;
            mono[i] = v2 * amp;
            amp += dAmp;
        }

        v.Ic1 = ic1;
        v.Ic2 = ic2;
        v.U0 = p0;
        v.U1 = p1;
        v.U2 = p2;
        return i;
    }

    /// <summary>
    /// A smooth, odd saturation: tanh's rational (Padé) approximation x(27 + x²)/(27 + 9x²), exact at 0, within 2 % of tanh
    /// up to |x| = 3, where it reaches 1 and is held. Gentle below |x| ≈ 0.5, so a clean (drive 1) quiet note is barely touched.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SoftSaturate(float x)
    {
        if (x > 3f) return 1f;
        if (x < -3f) return -1f;
        var x2 = x * x;
        return x * (27f + x2) / (27f + 9f * x2);
    }

    /// <summary>
    /// The aurora voice's three sounds (AD-79), for the sketches and the experience pieces: <paramref name="role"/> picks a
    /// slow pad whose filter opens like a sky (2.2 s), a soft brassy lead whose filter blooms with each note, or a round
    /// bass with a short resonant pluck. <paramref name="velocity"/> (0..1) scales how far the filter opens as well as the
    /// level, so soft notes are darker as well as quieter: the "expressive" part. Gain, pan and group are the caller's.
    /// </summary>
    private static NoteSpec AuroraSpec(AuroraRole role, float hz, float holdSeconds, float velocity) => role switch
    {
        // A wide, slowly opening pad: three saws ±9 cents, the cutoff from 1.5× the pitch (min 260 Hz) up 3 octaves at
        // the top of a 2.2 s rise, settling at 55 %; a light resonance and a little drive for warmth.
        AuroraRole.Pad => new NoteSpec
        {
            Wave = Wave.Analog, FreqHz = hz, Unison = 3, DetuneCents = 9f, ToneHz = MathF.Max(260f, hz * 1.5f),
            Resonance = 0.35f, FilterEnvOctaves = 3f * (0.55f + 0.45f * velocity), FilterAttack = 2.2f, FilterDecay = 4f, FilterSustain = 0.55f,
            Drive = 1.6f, DriftCents = 4f, Attack = 0.9f, Decay = 3f, Sustain = 0.85f, Hold = holdSeconds, Release = 1.4f, Send = 0.55f, Group = -1,
        },

        // A soft brass-like lead: two saws ±6 cents, the cutoff from the pitch up 2.6 octaves in 80 ms, falling to 35 %
        // over ≈ 0.7 s; more resonance and drive, so each note "blooms" and then mellows.
        AuroraRole.Lead => new NoteSpec
        {
            Wave = Wave.Analog, FreqHz = hz, Unison = 2, DetuneCents = 6f, ToneHz = MathF.Max(300f, hz),
            Resonance = 0.55f, FilterEnvOctaves = 2.6f * (0.45f + 0.55f * velocity), FilterAttack = 0.08f, FilterDecay = 0.7f, FilterSustain = 0.35f,
            Drive = 2.2f, DriftCents = 3f, Attack = 0.025f, Decay = 1.2f, Sustain = 0.8f, Hold = holdSeconds, Release = 0.35f, Send = 0.4f,
            VibratoHz = 4.8f, VibratoDepth = 0.0018f, Group = -1,
        },

        // A round bass: one saw, the cutoff from 2× the pitch up 2 octaves for a 25 ms pluck, falling to 15 % in 0.35 s.
        _ => new NoteSpec
        {
            Wave = Wave.Analog, FreqHz = hz, Unison = 1, ToneHz = MathF.Max(120f, hz * 2f),
            Resonance = 0.5f, FilterEnvOctaves = 2f * (0.5f + 0.5f * velocity), FilterAttack = 0.025f, FilterDecay = 0.35f, FilterSustain = 0.15f,
            Drive = 2.5f, DriftCents = 2f, Attack = 0.004f, Decay = 0.9f, Sustain = 0.6f, Hold = holdSeconds, Release = 0.12f, Send = 0.06f, Group = -1,
        },
    };

    /// <summary>
    /// The aurora features an offline render switches off (PLANET-173, AD-95: the demonstration of what each contributes;
    /// <see cref="RenderSketch"/>). The game never sets this, so its aurora sounds are unchanged.
    /// </summary>
    private AuroraFeatures _auroraOff = AuroraFeatures.None;

    /// <summary>
    /// <paramref name="spec"/> with the features in <paramref name="off"/> switched off (PLANET-173): the detuned copies and
    /// the per-note drift (one saw, in tune); the drive (at its minimum, 1: nearly clean); the filter's motion (its cutoff held
    /// where its envelope would settle, so only the movement goes, not the brightness); the resonance (no peak at the cutoff).
    /// </summary>
    private static NoteSpec WithoutFeatures(NoteSpec spec, AuroraFeatures off)
    {
        if (off == AuroraFeatures.None) return spec;
        if (off.HasFlag(AuroraFeatures.Detune)) spec = spec with { Unison = 1, DetuneCents = 0f, DriftCents = 0f };
        if (off.HasFlag(AuroraFeatures.Drive)) spec = spec with { Drive = 1f };
        if (off.HasFlag(AuroraFeatures.FilterMotion)) spec = spec with { ToneHz = spec.ToneHz * MathF.Pow(2f, spec.FilterEnvOctaves * spec.FilterSustain), FilterEnvOctaves = 0f };
        if (off.HasFlag(AuroraFeatures.Resonance)) spec = spec with { Resonance = 0f };
        return spec;
    }

    /// <summary>What an aurora note is for (<see cref="AuroraSpec"/>).</summary>
    private enum AuroraRole
    {
        Pad,
        Lead,
        Bass,
    }
}

/// <summary>
/// The aurora voice's features (AD-79), as flags an offline render can switch off one at a time to hear what each adds
/// (PLANET-173, AD-95; <see cref="SkySynth.RenderSketch"/>). Nothing in the game switches any off.
/// </summary>
[Flags]
public enum AuroraFeatures
{
    /// <summary>Everything on: the aurora as the game plays it.</summary>
    None = 0,

    /// <summary>The two or three saws detuned against each other, and each note's few cents of drift: the width and slow shimmer.</summary>
    Detune = 1,

    /// <summary>The soft saturating drive before the filter: warmth and density (extra harmonics, gently rounded peaks).</summary>
    Drive = 2,

    /// <summary>The filter's own envelope: the cutoff opening and settling with each note (the "bloom" and the pad's slow sky).</summary>
    FilterMotion = 4,

    /// <summary>The filter's resonance: the peak at the cutoff that makes its movement sing.</summary>
    Resonance = 8,
}
