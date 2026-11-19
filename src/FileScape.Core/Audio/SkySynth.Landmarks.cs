namespace FileScape.Core.Audio;

/// <summary>The scenery that answers a twirl (PLANET-75, AD-70), each with its own little reply.</summary>
public enum SceneryVoice
{
    /// <summary>A mushroom: a springy "boing" up a fifth and a soft pluck on top.</summary>
    Mushroom,

    /// <summary>A crystal: three glassy notes climbing the chord, high and quick.</summary>
    Crystal,

    /// <summary>A lamp or lantern: two warm bell notes falling a third, like a lantern's chime.</summary>
    Lamp,
}

// The lighthouse's call and the scenery's replies (PLANET-74/75, AD-70, 2026-11-19). DJ: "a little sound, motion, glow, or
// musical reply is enough … brief enough that it's pleasant to repeat". So each reply is a short in-key figure (≤ 1.6 s
// of sound, most of it a decay) on the music's next 32nd note, like the bell flowers' chime and the bell tower's toll;
// nothing here ducks the music except the lighthouse (an event on the scale of the bell, but shorter and lighter).
// What a change must preserve: every note in the world's scale (SkySynthTests.Landmarks checks), on the grid, and short.
public sealed partial class SkySynth
{
    // ---- the lighthouse's call (by ear on paper, 2026-11-19; DJ to confirm by ear)
    /// <summary>The lamp's call: a soft low horn on the tonic an octave down, this long, seconds (a foghorn's hint, not a blast).</summary>
    private const float LighthouseHornSeconds = 1.1f;

    /// <summary>The horn's level and the bells' level over it (linear gain; the bell toll's strike is ≈ 0.085).</summary>
    private const float LighthouseHornGain = 0.05f, LighthouseBellGain = 0.06f;

    /// <summary>The music ducks this deep (fraction kept) for this long under the call, seconds (lighter than the bell's 0.5 for 1.4 s).</summary>
    private const float LighthouseDuckDepth = 0.7f, LighthouseDuckSeconds = 1.0f;

    // ---- the scenery's replies
    /// <summary>A scenery reply's notes are this far apart, in 32nd notes of the music's grid.</summary>
    private const int SceneryNoteSpacing32nds = 2;

    /// <summary>The replies' level (linear gain): a little under the bell flowers' 0.06, so a reply never stands over the music.</summary>
    private const float SceneryGain = 0.055f;

    /// <summary>The last landmark or scenery reply's first note (frame) and every pitch it played (MIDI), for tests; (−1, empty) before any.</summary>
    public (long Frame, IReadOnlyList<int> MidiNotes) LastLandmarkReply { get; private set; } = (-1, []);

    /// <summary>
    /// The lighthouse lit (PLANET-74, a twirl or E at its foot): a soft low horn on the tonic and, over it, the lamp's two
    /// bells — the fifth, then the octave a beat's half later — as the beam sweeps round. On the music's next 32nd note, in
    /// the world's key; the music dips a little under it.
    /// </summary>
    public void LighthouseCall()
    {
        var at = NextGridFrame();
        var half = GridStepFrames(at) * 4; // half a beat: four 32nd notes
        var profile = ClockAt(at).Profile;
        DuckMusic(LighthouseDuckDepth, LighthouseDuckSeconds);
        var notes = new List<int>();
        int Midi(int degree, int octave)
        {
            var m = profile.Root + 12 * octave + ScaleNote(profile.Scale, degree);
            notes.Add(m);
            return m;
        }

        var len = profile.Scale.Length;
        // The horn: two detuned saws through a closed filter that opens a little and closes (a breath, not a buzz).
        Enqueue(at, NoteSpec.Saw(MidiHz(Midi(0, -1)), unison: 2, detuneCents: 6, toneHz: 380) with
        {
            Attack = 0.12f, Decay = LighthouseHornSeconds, Sustain = 0, Release = 0.3f, Gain = LighthouseHornGain, ToneToHz = 900, ToneGlide = 0.35f, Send = 0.35f,
        });
        Enqueue(at, NoteSpec.Bell(MidiHz(Midi(4, 1)), ratio: 1.41f, index: 1.2f, indexDecay: 0.4f) with { Attack = 0.003f, Decay = 1.4f, Gain = LighthouseBellGain, Pan = -0.15f, Send = 0.5f });
        Enqueue(at + half, NoteSpec.Bell(MidiHz(Midi(len, 1)), ratio: 1.41f, index: 1.2f, indexDecay: 0.4f) with { Attack = 0.003f, Decay = 1.6f, Gain = LighthouseBellGain * 0.85f, Pan = 0.15f, Send = 0.5f });
        LastLandmarkReply = (at, notes);
    }

    /// <summary>
    /// A piece of scenery answers a twirl (PLANET-75): <paramref name="voice"/>'s short figure, transposed by
    /// <paramref name="variant"/> (the piece's seed: neighbours answer on different degrees, the same piece always the
    /// same), on the music's next 32nd note, in the world's key. Brief (under 1.2 s of sound) and quiet, so it is pleasant
    /// to repeat.
    /// </summary>
    public void SceneryReply(SceneryVoice voice, int variant)
    {
        var at = NextGridFrame();
        var step = GridStepFrames(at) * SceneryNoteSpacing32nds;
        var profile = ClockAt(at).Profile;
        var len = profile.Scale.Length;
        var shift = ((variant % 3) + 3) % 3 * 2; // 0, 2 or 4: the tonic, third or fifth of the chord
        var notes = new List<int>();
        int Midi(int degree, int octave)
        {
            var m = profile.Root + 12 * octave + ScaleNote(profile.Scale, degree);
            notes.Add(m);
            return m;
        }

        var pan = ((variant % 5) + 5) % 5 * 0.15f - 0.3f; // −0.3 … +0.3: a little to one side or the other
        switch (voice)
        {
            case SceneryVoice.Mushroom:
                // A springy boing: a triangle that glides up a fifth, then a soft pluck an octave over the start.
                var from = Midi(shift, 0);
                var to = Midi(shift + 4, 0);
                Enqueue(at, NoteSpec.Tone(Wave.Triangle, MidiHz(from)) with { GlideToHz = MidiHz(to), Glide = 0.09f, Attack = 0.003f, Decay = 0.3f, Gain = SceneryGain * 1.6f, ToneHz = 1800, Pan = pan, Send = 0.15f, VibratoHz = 9f, VibratoDepth = 0.012f });
                Enqueue(at + step, NoteSpec.Tone(Wave.Triangle, MidiHz(Midi(shift + len, 1))) with { Attack = 0.002f, Decay = 0.18f, Gain = SceneryGain, ToneHz = 2400, Pan = pan, Send = 0.2f });
                break;
            case SceneryVoice.Crystal:
                // Glass: three bright FM notes up the chord, high, with a long-ish shimmer.
                for (var i = 0; i < 3; i++)
                {
                    Enqueue(at + step * i, NoteSpec.Bell(MidiHz(Midi(shift + 2 * i, 2)), ratio: 3.5f, index: 1.4f, indexDecay: 0.2f) with
                    {
                        Attack = 0.001f, Decay = 0.9f, Gain = SceneryGain * 0.8f, Pan = pan + 0.1f * (i - 1), Send = 0.55f,
                    });
                }

                break;
            default: // SceneryVoice.Lamp
                // A lantern's chime: two warm bell notes falling a third.
                Enqueue(at, NoteSpec.Bell(MidiHz(Midi(shift + 2, 1)), ratio: 1.4f, index: 1.6f, indexDecay: 0.4f) with { Attack = 0.003f, Decay = 1.1f, Gain = SceneryGain, Pan = pan, Send = 0.45f });
                Enqueue(at + 2 * step, NoteSpec.Bell(MidiHz(Midi(shift, 1)), ratio: 1.4f, index: 1.6f, indexDecay: 0.4f) with { Attack = 0.003f, Decay = 1.2f, Gain = SceneryGain * 0.9f, Pan = -pan, Send = 0.45f });
                break;
        }

        LastLandmarkReply = (at, notes);
    }
}
