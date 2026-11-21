using FileScape.Core.Planets;
using FileScape.Core.Units;

namespace FileScape.Core.Audio;

// PLANET-79/80 (AD-72): written sketches played on SkySynth's own instruments, offline. A sketch synth has no live world
// music (both decks are stopped before the first block), so only the sketch's notes sound; they go through the effects
// group (gain 1, no deck fade, no duck), the shared reverb, the master volume, the limiter and the soft clipper exactly as
// the game's sound does. Nothing here is reached by the game's own music: SetWorld, the decks and the scores are untouched.
public sealed partial class SkySynth
{
    /// <summary>How far ahead of the render cursor a sketch's notes are queued, frames (≈ 46 ms at 44.1 kHz): enough for every note to start on its own frame, short enough to keep the pending list small.</summary>
    private const int SketchLookaheadFrames = 2048;

    /// <summary>The render chunk for a sketch, frames (≈ 23 ms at 44.1 kHz; any size gives the same samples, the blocks inside are 64 frames).</summary>
    private const int SketchChunkFrames = 1024;

    /// <summary>
    /// Renders <paramref name="score"/> as interleaved stereo at <paramref name="sampleRateHz"/>: every note on its frame,
    /// on SkySynth's instruments, through the game's mix chain. Deterministic for a score and seed. Each sample |x| &lt; 1
    /// (the synth's output contract). Not for the audio thread (it allocates the whole buffer).
    /// </summary>
    public static float[] RenderSketch(SketchScore score, int sampleRateHz = DefaultSampleRateHz, int seed = 72) => RenderSketch(score, AuroraFeatures.None, sampleRateHz, seed);

    /// <summary>
    /// <see cref="RenderSketch(SketchScore, int, int)"/> with the aurora features in <paramref name="auroraOff"/> switched
    /// off on every aurora note (PLANET-173, AD-95: the demonstration of what each feature contributes); every other
    /// instrument sounds as usual.
    /// </summary>
    public static float[] RenderSketch(SketchScore score, AuroraFeatures auroraOff, int sampleRateHz = DefaultSampleRateHz, int seed = 72)
    {
        var s = new SkySynth(seed, PlanetTheme.Hearth, sampleRateHz) { _auroraOff = auroraOff };
        foreach (var d in s._decks)
        {
            d.Live = false;
            d.Fade = d.FadeTarget = 0;
        }

        var total = (long)Math.Ceiling(score.Duration.TotalSeconds * sampleRateHz);
        var output = new float[total * 2];
        var next = 0;
        var notes = score.Notes;
        for (long done = 0; done < total;)
        {
            var n = (int)Math.Min(SketchChunkFrames, total - done);
            while (next < notes.Count && notes[next].Start.TotalSeconds * sampleRateHz < done + n + SketchLookaheadFrames)
            {
                s.PlaySketchNote(notes[next], (long)Math.Round(notes[next].Start.TotalSeconds * s._rate), GroupSfx, transposeSemitones: 0);
                next++;
            }

            s.Render(output.AsSpan((int)(done * 2), n * 2));
            done += n;
        }

        return output;
    }

    /// <summary>
    /// Queues one sketch note on its instrument at frame <paramref name="at"/> into voice group <paramref name="g"/>: offline,
    /// the effects group (gain 1, the reverb as each voice's send sets); in the game, the deck playing an experience's piece
    /// (AD-79, SkySynth.Experience.cs), <paramref name="transposeSemitones"/> into the world's key.
    /// </summary>
    private void PlaySketchNote(in SketchNote n, long at, int g, int transposeSemitones)
    {
        var hz = MidiHz(n.MidiNote + transposeSemitones);
        // A held note ends 20 ms before its written end, so repeated notes re-articulate (rationale as the theme's legato gap).
        var hold = MathF.Max(0.04f, n.Length.ToSecondsF() - 0.02f);
        var v = n.Velocity;
        var pan = n.Pan;
        switch (n.Instrument)
        {
            case SketchInstrument.Bell: Lead1(Lead.Bell, 0.075f); break;
            case SketchInstrument.Glass: Lead1(Lead.Glass, 0.065f); break;
            case SketchInstrument.Marimba: Lead1(Lead.Marimba, 0.08f); break;
            case SketchInstrument.Pluck: Lead1(Lead.Pluck, 0.06f); break;
            case SketchInstrument.Clarinet: Lead1(Lead.Clarinet, 0.07f); break;
            case SketchInstrument.Horn: Lead1(Lead.Horn, 0.06f); break;
            case SketchInstrument.Violins: Lead1(Lead.Violins, 0.05f); break;
            case SketchInstrument.Cello: Lead1(Lead.Cello, 0.06f); break;
            case SketchInstrument.Flute:
                Lead1(Lead.Flute, 0.075f);
                Enqueue(at, NoteSpec.Noise(hz * 2f, hz * 1.5f, 0.05f, q: 2f) with { Attack = 0.01f, Decay = 0.08f, Gain = 0.075f * v * 0.25f, Pan = n.Pan, Send = 0.4f, Group = g });
                break;
            case SketchInstrument.SuperSaw:
            {
                var spec = LeadSpec(Lead.SuperSaw, hz, hold) with { Gain = 0.045f * v, Group = g };
                Enqueue(at, spec with { Pan = n.Pan - 0.45f });
                Enqueue(at, spec with { FreqHz = hz * 1.003f, DetuneCents = 9f, Pan = n.Pan + 0.45f });
                Enqueue(at, NoteSpec.Bell(hz * 2f, ratio: 2f, index: 1f, indexDecay: 0.12f) with { Attack = 0.002f, Decay = 0.25f, Gain = 0.075f * v * 0.3f, Pan = n.Pan, Send = 0.3f, Group = g });
                break;
            }

            case SketchInstrument.Unstable:
            {
                var spec = LeadSpec(Lead.Unstable, hz, hold) with { Group = g };
                Enqueue(at, spec with { Gain = 0.07f * v * 0.75f, Pan = n.Pan - 0.2f });
                Enqueue(at, spec with { FreqHz = hz * UnstableTwinRatio, VibratoHz = 0.93f, Gain = 0.07f * v * 0.6f, Pan = n.Pan + 0.25f });
                break;
            }

            case SketchInstrument.Harp:
                Enqueue(at, NoteSpec.Bell(hz, ratio: 2f, index: 0.8f, indexDecay: 0.05f) with { Attack = 0.002f, Decay = 1.4f, Gain = 0.045f * v, Pan = n.Pan, Send = 0.6f, ToneHz = 5000, Group = g });
                break;
            case SketchInstrument.Pad:
                Enqueue(at, NoteSpec.Tone(Wave.Triangle, hz) with { Attack = 0.25f, Decay = 2f, Sustain = 0.75f, Hold = hold, Release = 0.8f, Gain = 0.03f * v, ToneHz = 1400, Pan = n.Pan, Send = 0.5f, Group = g });
                break;
            case SketchInstrument.StringPad:
                Enqueue(at, LeadSpec(Lead.Violins, hz, hold) with { Attack = 0.35f, Release = 0.7f, Gain = 0.022f * v, Pan = n.Pan, Send = 0.65f, ToneHz = 1800, Group = g });
                break;
            case SketchInstrument.Bass:
                Enqueue(at, NoteSpec.Tone(Wave.Triangle, hz) with { Attack = 0.004f, Decay = 0.9f, Sustain = 0.5f, Hold = hold, Release = 0.12f, Gain = 0.07f * v, ToneHz = 900, Pan = 0f, Send = 0.05f, Group = g });
                Enqueue(at, NoteSpec.Tone(Wave.Sine, hz * 0.5f) with { Attack = 0.006f, Decay = 1.2f, Sustain = 0.6f, Hold = hold, Release = 0.12f, Gain = 0.06f * v, Pan = 0f, Send = 0f, Group = g });
                break;
            case SketchInstrument.SawBass:
                Enqueue(at, NoteSpec.Saw(hz, 2, 6f, 700f) with { ToneToHz = 380f, ToneGlide = 0.12f, Attack = 0.003f, Decay = 0.35f, Sustain = 0.4f, Hold = hold, Release = 0.06f, Gain = 0.05f * v, Pan = 0f, Send = 0.04f, Group = g });
                Enqueue(at, NoteSpec.Tone(Wave.Sine, hz * 0.5f) with { Attack = 0.004f, Decay = 0.5f, Sustain = 0.5f, Hold = hold, Release = 0.06f, Gain = 0.05f * v, Send = 0f, Group = g });
                break;
            case SketchInstrument.SubBoom:
                Enqueue(at, NoteSpec.Tone(Wave.Sine, 70f) with { GlideToHz = 34f, Glide = 1.2f, Attack = 0.003f, Decay = 2.0f, Gain = 0.14f * v, Send = 0.05f, Group = g });
                break;
            case SketchInstrument.Kick: Kick(at, v, g); break;
            case SketchInstrument.BigKick: BigKick(at, v, g); break;
            case SketchInstrument.Snare: Snare(SnareKind.Snare, at, v, n.Pan, g); break;
            case SketchInstrument.Hat: ClosedHat(at, v, n.Pan, g); break;
            case SketchInstrument.OpenHat: OpenHat(at, v, g); break;
            case SketchInstrument.Crash: Crash(at, v, g); break;
            case SketchInstrument.Riser:
            {
                var len = n.Length.ToSecondsF();
                Enqueue(at, NoteSpec.Noise(600, 7000, len, q: 1.4f) with { Attack = len, Decay = 0.3f, Gain = 0.04f * v, Pan = n.Pan, Send = 0.5f, Group = g });
                break;
            }

            case SketchInstrument.AuroraPad: Aurora(AuroraRole.Pad, 0.02f); break;
            case SketchInstrument.AuroraLead: Aurora(AuroraRole.Lead, 0.034f); break;
            case SketchInstrument.AuroraBass: Aurora(AuroraRole.Bass, 0.05f); break;
            case SketchInstrument.Timpani:
                Enqueue(at, NoteSpec.Tone(Wave.Sine, hz * 1.01f) with { GlideToHz = hz, Glide = 0.08f, Attack = 0.002f, Decay = 1.1f, Gain = 0.12f * v, Pan = 0.05f, Send = 0.35f, Group = g });
                Enqueue(at, NoteSpec.Tone(Wave.Triangle, hz * 1.5f) with { Attack = 0.002f, Decay = 0.35f, Gain = 0.03f * v, ToneHz = 900, Send = 0.35f, Group = g });
                Enqueue(at, NoteSpec.Noise(500, 300, 0.05f, q: 0.8f, lowpass: true) with { Attack = 0.001f, Decay = 0.06f, Gain = 0.05f * v, Send = 0.3f, Group = g });
                break;
        }

        // One lead voice at a base gain (linear, before velocity; levels set by ear against the bed, rationale not recorded).
        void Lead1(Lead lead, float baseGain) => Enqueue(at, LeadSpec(lead, hz, hold) with { Gain = baseGain * v, Pan = pan, Group = g });

        // An aurora note (AD-79) at a base gain (linear, before velocity; set by measurement so it is about as loud as the
        // instrument it stands in for in the before/after comparison); velocity also opens its filter (AuroraSpec).
        void Aurora(AuroraRole role, float baseGain) => Enqueue(at, WithoutFeatures(AuroraSpec(role, hz, hold, v), _auroraOff) with { Gain = baseGain * v, Pan = pan, Group = g });
    }
}
