using FileScape.Core.Planets;
using System.Runtime.CompilerServices;
using FileScape.Core.Units;

namespace FileScape.Core.Audio;

/// <summary>A planet's musical key: the root note and the scale it uses (semitones above the root).</summary>
/// <param name="RootMidiNote">The tonic as a MIDI note number (60 = middle C, 69 = A 440 Hz).</param>
/// <param name="Mode">A human name for the scale ("lydian", "major pentatonic", …).</param>
/// <param name="IntervalsSemitones">Scale steps above the root, ascending within one octave, starting at 0.</param>
public sealed record MusicalKey(int RootMidiNote, string Mode, IReadOnlyList<int> IntervalsSemitones);

/// <summary>A note of the written FileScape theme (<see cref="SkySynth.ThemeMelody"/>).</summary>
/// <param name="StartSixteenths">Onset in sixteenth notes from the theme's first downbeat (16 per bar).</param>
/// <param name="LengthSixteenths">Length in sixteenth notes.</param>
/// <param name="Degree">Scale degree in 7-note numbering (0 = tonic, 7 = the octave above); pentatonic keys map it to do/re/mi/sol/la.</param>
public readonly record struct ThemeNote(int StartSixteenths, int LengthSixteenths, int Degree);

/// <summary>How the FileScape theme is played on one return (one per 16-bar cycle).</summary>
public enum ThemeVariation
{
    /// <summary>As written (the first return in a world, then every fourth).</summary>
    Plain,

    /// <summary>Turns on the long notes, passing and neighbour notes, grace notes before upward leaps.</summary>
    Ornamented,

    /// <summary>Rhythmically displaced: beats anticipated (or delayed) by an eighth note.</summary>
    Syncopated,

    /// <summary>An octave up, over a slow counter-line of chord tones.</summary>
    OctaveCounter,

    /// <summary>The answer (bars 3–4) mirrored around its first note, so it falls where it rose.</summary>
    InvertedAnswer,

    /// <summary>The cadence (bars 7–8) in doubled note values: the theme ends a bar later.</summary>
    AugmentedEnding,
}

/// <summary>Which melodic layer a <see cref="MelodyNote"/> belongs to.</summary>
public enum MelodyPart
{
    /// <summary>A note of the theme's lead line (a bell with a little glide).</summary>
    Theme,

    /// <summary>A grace note just before a theme note.</summary>
    Grace,

    /// <summary>The counter-line under an <see cref="ThemeVariation.OctaveCounter"/> return.</summary>
    Counter,

    /// <summary>The freer music box between returns.</summary>
    MusicBox,
}

/// <summary>A melody note as it is scheduled (diagnostics: <see cref="SkySynth.MelodyObserver"/>).</summary>
/// <param name="Frame">The output frame at which it starts.</param>
/// <param name="MidiNote">Its pitch as a MIDI note number.</param>
/// <param name="Part">The melodic layer.</param>
/// <param name="Variation">The variation of the cycle it was played in.</param>
public readonly record struct MelodyNote(long Frame, int MidiNote, MelodyPart Part, ThemeVariation Variation);

/// <summary>
/// The Planets experience's original procedural audio ("Lantern Cosmos"): generative music and pitched sound effects,
/// synthesised from scratch in plain C# (no engine types, no samples), so it can be rendered offline and tested.
/// <para>
/// <b>Music.</b> Every theme shares one tempo (<see cref="TempoBeatsPerMinute"/>, 108 BPM) and has its own key
/// (<see cref="KeyFor"/>). The music runs in 16-bar cycles (<see cref="CycleBars"/>): the original FileScape theme
/// (<see cref="ThemeMelody"/>, 8 bars: a 2-bar motif, its answer, the motif raised, a cadence; written in scale degrees so it
/// fits every key and mode) played by a bell lead with a little glide, then 8 bars of freer seeded music box. Each
/// return is a <see cref="ThemeVariation"/> (plain first and every fourth cycle, otherwise a seeded choice per seed, theme
/// and cycle). Under it: a soft pad that changes chord every bar under the theme and every 2 bars between (a progression
/// per mode), a plucked bass on beat 1, the "and" of 2, beat 3 and a pickup, quiet offbeat shaker ticks of filtered
/// noise, and — while <see cref="SetFlight"/> is above zero — a rising arpeggio. A new theme's first cycle starts on
/// the next bar line. <see cref="SetTheme"/> crossfades (equal power) over <see cref="ThemeCrossfade"/>. The music sits
/// well below the effects.
/// </para>
/// <para>
/// <b>Effects.</b> All are pitched in the current key. Movement sounds (<see cref="Step"/>, <see cref="Jump"/>,
/// <see cref="AirJump"/>, <see cref="Land"/>, <see cref="WindUp"/>, <see cref="Launch"/>, <see cref="Fly"/>) start at
/// the render cursor (<see cref="ImmediateLatencyBound"/>). The others (<see cref="Arrive"/>, <see cref="Select"/>,
/// <see cref="Inspect"/>, <see cref="Open"/>, <see cref="Deny"/>, <see cref="Chime"/>) wait for the next point of the
/// half-sixteenth grid (a 32nd note, 69.4 ms at 108 BPM; a sixteenth, 139 ms, would exceed the ≈ 90 ms budget), so they lock to the tempo and never wait longer than
/// <see cref="QuantisedLatencyBound"/>.
/// </para>
/// <para>
/// <b>Engine.</b> A pool of <see cref="MaxVoices"/> voices (sine, triangle, soft band-limited square, 2-operator FM bell,
/// state-variable-filtered noise) with ADSR envelopes and exact-duration pitch glides; equal-power pan; a light
/// Freeverb-style reverb (4 combs + 2 allpasses per channel); a peak limiter followed by a soft clipper, so every output
/// sample satisfies |x| &lt; 1. Rendering is block-based (64 frames), allocation-free in steady state, and deterministic
/// for a seed. Not thread-safe: call everything from one thread (the Godot node calls it from <c>_Process</c>).
/// </para>
/// Time is the render cursor (<see cref="FramesRendered"/>): an effect called now starts at the next frame rendered.
/// </summary>
public sealed class SkySynth
{
    /// <summary>The default and intended sample rate, frames per second (the Godot generator's mix rate).</summary>
    public const int DefaultSampleRateHz = 44100;

    /// <summary>Shared tempo of every theme, beats (quarter notes) per minute.</summary>
    public const float TempoBeatsPerMinute = 108f;

    /// <summary>Bars per music cycle: the theme (<see cref="ThemeLengthBars"/>, or one more for an augmented ending), then free music box.</summary>
    public const int CycleBars = 16;

    /// <summary>The written theme's length in bars (4/4).</summary>
    public const int ThemeLengthBars = 8;

    /// <summary>The size of the voice pool (music and effects together).</summary>
    public const int MaxVoices = 32;

    /// <summary>
    /// Voices the music may use at once; the rest are kept for effects. When the music's share is full, a new music note
    /// takes over the quietest music voice (never an effect's).
    /// </summary>
    public const int MaxMusicVoices = 22;

    /// <summary>The FileScape theme as written: 8 bars of scale degrees and rhythm (see <see cref="ThemeNote"/>).</summary>
    public static IReadOnlyList<ThemeNote> ThemeMelody { get; } = Array.AsReadOnly(BuildTheme());

    /// <summary>How long <see cref="SetTheme"/> takes to crossfade from one theme's music to the next.</summary>
    public static readonly TimeSpan ThemeCrossfade = TimeSpan.FromSeconds(2);

    /// <summary>The longest a tempo-locked effect waits for the grid (one 32nd note at 108 BPM ≈ 69.4 ms, rounded up; within the ≈ 90 ms budget).</summary>
    public static readonly TimeSpan QuantisedLatencyBound = TimeSpan.FromMilliseconds(70);

    /// <summary>The longest a movement effect waits before it sounds (it starts at the next rendered frame; attacks are ≤ 3 ms).</summary>
    public static readonly TimeSpan ImmediateLatencyBound = TimeSpan.FromMilliseconds(10);

    private const int Block = 64;
    private const int GroupCount = 4; // DeckA, DeckB, Flight, Sfx
    private const int GroupFlight = 2;
    private const int GroupSfx = 3;
    private const float EndLevel = 0.0005f;
    private const int ControlFrames = 16;
    private const float LimiterThreshold = 0.8f;
    private const float OutputGain = 1.6f; // voice gains are designed small; this sets the overall loudness
    private const int CycleSixteenths = CycleBars * 16;
    private const int MaxCycleNotes = 128;
    private static readonly ThemeNote[] ThemeNotes = [.. ThemeMelody];

    private readonly int _rate;
    private readonly float _invRate;
    private readonly double _framesPerSixteenth;
    private readonly int _seed;
    private readonly Voice[] _voices = new Voice[MaxVoices];
    private readonly List<(long Frame, NoteSpec Note)> _pending = new(64);
    private readonly Deck[] _decks = new Deck[2];
    private readonly float[][] _gL = new float[GroupCount][];
    private readonly float[][] _gR = new float[GroupCount][];
    private readonly float[][] _gS = new float[GroupCount][];
    private readonly float[] _residualL = new float[GroupCount];
    private readonly float[] _residualR = new float[GroupCount];
    private readonly float _residualK;
    private readonly Reverb _reverb;
    private readonly float[] _mixL = new float[Block];
    private readonly float[] _mixR = new float[Block];
    private readonly float[] _mixS = new float[Block];
    private readonly float[] _wetL = new float[Block];
    private readonly float[] _wetR = new float[Block];
    private readonly float[] _voiceBuffer = new float[Block];
    private readonly float _masterK;
    private readonly float _limiterReleaseK;
    private readonly float _flightK;
    private readonly float _fadeStepPerFrame;

    private long _frame;
    private long _nextStep;
    private int _current; // index of the deck that plays the current theme
    private int _generation;
    private int _themeChanges;
    private uint _noiseSeed;
    private float _masterVolume = 0.8f;
    private bool _muted;
    private float _masterGain;
    private float _flightTarget;
    private float _flight;
    private int _arpCounter;
    private float _limiterEnv;
    private Voice? _flyVoice;
    private int _flyGeneration;
    private long _lastFlyFrame;
    private bool _stepLeft;

    /// <summary>Creates a synth playing <paramref name="theme"/>'s music from the first frame.</summary>
    /// <param name="seed">Chooses the melodies and noise; the same seed and calls give the same samples.</param>
    /// <param name="theme">The theme to start in.</param>
    /// <param name="sampleRateHz">Frames per second (default 44 100).</param>
    public SkySynth(int seed = 1, PlanetTheme theme = PlanetTheme.Hearth, int sampleRateHz = DefaultSampleRateHz)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRateHz, 8000);
        _rate = sampleRateHz;
        _invRate = 1f / sampleRateHz;
        _framesPerSixteenth = sampleRateHz * 60.0 / (TempoBeatsPerMinute * 4.0);
        _seed = seed;
        _noiseSeed = (uint)seed * 2654435761u | 1u;
        for (var i = 0; i < MaxVoices; i++) _voices[i] = new Voice();
        for (var g = 0; g < GroupCount; g++)
        {
            _gL[g] = new float[Block];
            _gR[g] = new float[Block];
            _gS[g] = new float[Block];
        }

        _residualK = Coef(0.003f);
        _masterK = 1f - Coef(0.015f);
        _limiterReleaseK = 1f - Coef(0.2f);
        _flightK = 1f - Coef(0.35f);
        _fadeStepPerFrame = 1f / (ThemeCrossfade.ToSecondsF() * sampleRateHz);
        _reverb = new Reverb(sampleRateHz);
        _decks[0] = new Deck { Theme = theme, Profile = Profiles[(int)theme], Fade = 1, FadeTarget = 1, Rng = new Random(seed), NeedChord = true };
        _decks[1] = new Deck { Theme = theme, Profile = Profiles[(int)theme], Fade = 0, FadeTarget = 0, Rng = new Random(seed + 1) };
        _decks[0].ResetMelody();
        _masterGain = _masterVolume;
    }

    /// <summary>Frames per second of the output.</summary>
    public int SampleRateHz => _rate;

    /// <summary>The render cursor: frames rendered so far (the time base of every call).</summary>
    public long FramesRendered => _frame;

    /// <summary>The theme whose music is playing (or fading in).</summary>
    public PlanetTheme Theme => _decks[_current].Theme;

    /// <summary>The current key (that of <see cref="Theme"/>); every effect is pitched in it.</summary>
    public MusicalKey Key => KeyFor(Theme);

    /// <summary>Voices sounding now (for diagnostics).</summary>
    public int ActiveVoices
    {
        get
        {
            var n = 0;
            foreach (var v in _voices) if (v.Active) n++;
            return n;
        }
    }

    /// <summary>Music level relative to its designed (moderate) level, 0..1 (default 1). 0 leaves only the effects.</summary>
    public float MusicVolume { get; set; } = 1f;

    /// <summary>The variation of the current theme cycle (diagnostics and probes).</summary>
    public ThemeVariation Variation => _decks[_current].Variation;

    /// <summary>Called (on the render thread, inside <see cref="Render(Span{float})"/>) for every melody note as it is scheduled; null by default.</summary>
    public Action<MelodyNote>? MelodyObserver { get; set; }

    /// <summary>The key of a theme (all themes share <see cref="TempoBeatsPerMinute"/>).</summary>
    public static MusicalKey KeyFor(PlanetTheme theme)
    {
        var p = Profiles[(int)theme];
        return new MusicalKey(p.Root, p.Mode, Array.AsReadOnly(p.Scale.ToArray()));
    }

    /// <summary>Crossfades the music to <paramref name="theme"/> over <see cref="ThemeCrossfade"/> (no-op if already playing it).</summary>
    public void SetTheme(PlanetTheme theme)
    {
        if (theme == _decks[_current].Theme) return;
        var old = _decks[_current];
        old.FadeTarget = 0;
        var oldGroup = _current;
        foreach (var v in _voices)
        {
            if (v.Active && v.Group == oldGroup) v.Release();
        }

        _current = 1 - _current;
        _themeChanges++;
        var next = _decks[_current];
        next.Theme = theme;
        next.Profile = Profiles[(int)theme];
        next.FadeTarget = 1;
        next.Rng = new Random(unchecked(_seed * 31 + (int)theme * 1009 + _themeChanges * 7919));
        next.NeedChord = true;
        next.ResetMelody((_nextStep + 15) / 16);
    }

    /// <summary>Flight intensity 0..1: above zero, a rising arpeggio layer joins the music (smoothed, so it can be set every frame).</summary>
    public void SetFlight(float intensity01) => _flightTarget = Clamp01(intensity01);

    /// <summary>Overall output level 0..1 (linear amplitude; default 0.8), smoothed over ≈ 15 ms.</summary>
    public void SetMasterVolume(float volume01) => _masterVolume = Clamp01(volume01);

    /// <summary>Silences (or restores) the output with a short ramp; music time keeps running.</summary>
    public void Mute(bool muted) => _muted = muted;

    // ---------------------------------------------------------------- movement effects (immediate)

    /// <summary>A footstep: a soft filtered-noise tap with a faint in-key pluck. <paramref name="intensity"/> 0..1 (walk ≈ 0.5, run ≈ 1).</summary>
    public void Step(float intensity)
    {
        var k = Clamp01(intensity);
        if (k <= 0) return;
        _stepLeft = !_stepLeft;
        var pan = _stepLeft ? -0.18f : 0.18f;
        var now = _frame;
        var cut = 1000f * (0.85f + 0.4f * Rand01());
        Enqueue(now, NoteSpec.Noise(cut, cut * 0.7f, 0.05f, q: 1.3f) with { Attack = 0.001f, Decay = 0.05f, Gain = 0.05f + 0.10f * k, Pan = pan, Send = 0.05f });
        Enqueue(now, NoteSpec.Tone(Wave.Triangle, PitchHz(_stepLeft ? 0 : 2, 1)) with { Attack = 0.002f, Decay = 0.09f, Gain = 0.02f * k, Pan = pan, Send = 0.08f, ToneHz = 1800 });
    }

    /// <summary>A jump: a quick upward glide (root to the fifth above the octave) with a small air puff.</summary>
    public void Jump()
    {
        var now = _frame;
        Enqueue(now, NoteSpec.Tone(Wave.Triangle, PitchHz(0, 0)) with { GlideToHz = PitchHz(4, 1), Glide = 0.12f, Attack = 0.002f, Decay = 0.22f, Gain = 0.13f, ToneHz = 2600, Send = 0.15f });
        Enqueue(now, NoteSpec.Noise(1800, 3200, 0.1f, q: 1.2f) with { Attack = 0.002f, Decay = 0.08f, Gain = 0.04f, Send = 0.05f });
    }

    /// <summary>An air jump: a sparkly upward flourish of four quick bell notes over a high shimmer.</summary>
    public void AirJump()
    {
        var now = _frame;
        var len = CurrentScale.Length;
        int[] degrees = [2, 4, len, len + 2];
        for (var i = 0; i < degrees.Length; i++)
        {
            Enqueue(now + Frames(0.035f * i), NoteSpec.Bell(PitchHz(degrees[i], 1), ratio: 3f, index: 1.1f, indexDecay: 0.15f) with
            {
                Attack = 0.002f, Decay = 0.55f, Gain = 0.075f, Pan = -0.3f + 0.2f * i, Send = 0.35f,
            });
        }

        Enqueue(now, NoteSpec.Noise(5500, 9500, 0.25f, q: 3f) with { Attack = 0.003f, Decay = 0.3f, Gain = 0.035f, Send = 0.3f });
    }

    /// <summary>A landing thump, louder and lower for harder landings. <paramref name="impactMetersPerSecond"/> is the downward speed at touchdown (≈ 12 m/s is a big drop).</summary>
    public void Land(float impactMetersPerSecond)
    {
        var k = Clamp01(impactMetersPerSecond / 12f);
        if (!float.IsFinite(k)) return;
        var now = _frame;
        var root = PitchHz(0, -1);
        Enqueue(now, NoteSpec.Tone(Wave.Sine, root * 2f) with { GlideToHz = root * 0.75f, Glide = 0.09f, Attack = 0.001f, Decay = 0.2f, Gain = 0.10f + 0.22f * k, Send = 0.06f });
        Enqueue(now, NoteSpec.Noise(700, 350, 0.08f, q: 0.8f, lowpass: true) with { Attack = 0.001f, Decay = 0.09f, Gain = 0.04f + 0.09f * k, Send = 0.05f });
    }

    /// <summary>A launcher wind-up: a rising shimmer that swells for exactly <paramref name="duration"/> and peaks at launch.</summary>
    public void WindUp(TimeSpan duration)
    {
        var t = Math.Clamp(duration.ToSecondsF(), 0.05f, 5f);
        var now = _frame;
        Enqueue(now, NoteSpec.Noise(500, 4200, t, q: 2.2f) with { Attack = t, Decay = 0.12f, Gain = 0.09f, Send = 0.25f });
        Enqueue(now, NoteSpec.Tone(Wave.Triangle, PitchHz(0, 1)) with { GlideToHz = PitchHz(0, 2), Glide = t, Attack = t, Decay = 0.15f, Gain = 0.055f, ToneHz = 3200, Pan = -0.15f, Send = 0.3f });
        Enqueue(now, NoteSpec.Tone(Wave.Sine, PitchHz(4, 1)) with { GlideToHz = PitchHz(4, 2), Glide = t, Attack = t, Decay = 0.15f, Gain = 0.04f, Pan = 0.15f, Send = 0.3f });
    }

    /// <summary>The launch: a whoosh with an upward harp-like glissando through the scale.</summary>
    public void Launch()
    {
        var now = _frame;
        Enqueue(now, NoteSpec.Noise(450, 3400, 0.55f, q: 1.1f) with { Attack = 0.03f, Decay = 1.0f, Gain = 0.15f, Send = 0.25f });
        for (var i = 0; i < 7; i++)
        {
            Enqueue(now + Frames(0.04f * i), NoteSpec.Tone(Wave.Triangle, PitchHz(i, 1)) with
            {
                Attack = 0.002f, Decay = 0.7f, Gain = 0.06f, ToneHz = 3600, Pan = -0.5f + i / 6f, Send = 0.4f,
            });
        }
    }

    /// <summary>
    /// The flight's wind, following <paramref name="progress01"/> (0 at launch, 1 at arrival): it brightens and swells
    /// mid-flight. Call it every frame while flying; it fades by itself at 1 or ≈ 0.3 s after the last call.
    /// </summary>
    public void Fly(float progress01)
    {
        var p = Clamp01(progress01);
        _lastFlyFrame = _frame;
        var v = _flyVoice != null && _flyVoice.Active && _flyVoice.Generation == _flyGeneration && !_flyVoice.Released ? _flyVoice : null;
        if (p >= 1f)
        {
            v?.Release();
            _flyVoice = null;
            return;
        }

        var level = 0.03f + 0.09f * MathF.Sin(MathF.PI * p);
        var cutoff = 500f + 2600f * MathF.Sin(MathF.PI * MathF.Min(1f, p * 1.2f));
        if (v == null)
        {
            v = StartVoice(NoteSpec.Noise(cutoff, cutoff, 0, q: 0.9f) with { Attack = 0.06f, Sustain = 1f, Decay = 0.1f, Hold = 3600f, Release = 0.35f, Gain = level, Send = 0.2f, Group = GroupSfx }, 0);
            if (v == null) return;
            v.LevelK = 1f - Coef(0.06f);
            _flyVoice = v;
            _flyGeneration = v.Generation;
        }

        v.LevelTarget = level;
        v.SetCutoffTarget(cutoff, 1f - Coef(0.08f));
    }

    // ---------------------------------------------------------------- interface effects (tempo-locked)

    /// <summary>Arrival: a warm chord, then a second, softer chime ≈ 120 ms later.</summary>
    public void Arrive()
    {
        var at = NextGridFrame();
        int[] chord = [0, 2, 4];
        for (var i = 0; i < chord.Length; i++)
        {
            Enqueue(at, NoteSpec.Tone(Wave.Triangle, PitchHz(chord[i], 0)) with
            {
                Attack = 0.012f, Decay = 1.6f, Gain = 0.075f, ToneHz = 1900, Pan = -0.35f + 0.35f * i, Send = 0.4f,
            });
        }

        Enqueue(at, NoteSpec.Tone(Wave.Sine, PitchHz(0, -1)) with { Attack = 0.012f, Decay = 1.2f, Gain = 0.07f, Send = 0.2f });
        Enqueue(at + Frames(0.12f), NoteSpec.Bell(PitchHz(CurrentScale.Length, 1), ratio: 3.5f, index: 1.4f, indexDecay: 0.3f) with
        {
            Attack = 0.003f, Decay = 1.4f, Gain = 0.05f, Pan = 0.2f, Send = 0.5f,
        });
    }

    /// <summary>Selection: a small high bell tick.</summary>
    public void Select()
    {
        Enqueue(NextGridFrame(), NoteSpec.Bell(PitchHz(4, 2), ratio: 2f, index: 0.9f, indexDecay: 0.08f) with { Attack = 0.002f, Decay = 0.4f, Gain = 0.06f, Send = 0.25f });
    }

    /// <summary>Inspection: two bell notes rising a third, a 32nd note apart.</summary>
    public void Inspect()
    {
        var at = NextGridFrame();
        Enqueue(at, NoteSpec.Bell(PitchHz(2, 1), ratio: 2f, index: 1f, indexDecay: 0.12f) with { Attack = 0.002f, Decay = 0.5f, Gain = 0.06f, Pan = -0.15f, Send = 0.3f });
        Enqueue(at + GridStepFrames(), NoteSpec.Bell(PitchHz(4, 1), ratio: 2f, index: 1f, indexDecay: 0.12f) with { Attack = 0.002f, Decay = 0.6f, Gain = 0.06f, Pan = 0.15f, Send = 0.3f });
    }

    /// <summary>Opening: a rising three-note figure over a soft whoosh.</summary>
    public void Open()
    {
        var at = NextGridFrame();
        var step = GridStepFrames();
        int[] degrees = [0, 2, 4];
        for (var i = 0; i < degrees.Length; i++)
        {
            Enqueue(at + step * i, NoteSpec.Tone(Wave.SoftSquare, PitchHz(degrees[i], 1)) with
            {
                Attack = 0.003f, Decay = 0.45f, Gain = 0.045f, ToneHz = 2200, Pan = -0.2f + 0.2f * i, Send = 0.3f,
            });
        }

        Enqueue(at, NoteSpec.Noise(800, 2600, 0.3f, q: 1.4f) with { Attack = 0.05f, Decay = 0.35f, Gain = 0.05f, Send = 0.25f });
    }

    /// <summary>Refusal: two gentle, muffled notes stepping down (never harsh).</summary>
    public void Deny()
    {
        var at = NextGridFrame();
        Enqueue(at, NoteSpec.Tone(Wave.SoftSquare, PitchHz(2, 0)) with { Attack = 0.004f, Decay = 0.22f, Gain = 0.07f, ToneHz = 900, Send = 0.12f });
        Enqueue(at + GridStepFrames(), NoteSpec.Tone(Wave.SoftSquare, PitchHz(0, 0)) with { Attack = 0.004f, Decay = 0.3f, Gain = 0.07f, ToneHz = 800, Send = 0.12f });
    }

    /// <summary>A bell-flower note (e.g. walking past an audio file): scale degree <paramref name="index"/> (wraps through two octaves).</summary>
    public void Chime(int index)
    {
        var len = CurrentScale.Length;
        var degree = ((index % (2 * len)) + 2 * len) % (2 * len);
        var pan = (((index * 37) % 7 + 7) % 7 - 3) / 6f;
        Enqueue(NextGridFrame(), NoteSpec.Bell(PitchHz(degree, 1), ratio: 1.4f, index: 2f, indexDecay: 0.4f) with { Attack = 0.003f, Decay = 1.8f, Gain = 0.06f, Pan = pan, Send = 0.45f });
    }

    // ---------------------------------------------------------------- rendering

    /// <summary>Renders the next <paramref name="frames"/> frames as interleaved stereo (L, R, L, R, …), each |x| &lt; 1.</summary>
    public float[] Render(int frames)
    {
        var buffer = new float[Math.Max(0, frames) * 2];
        Render(buffer);
        return buffer;
    }

    /// <summary>Renders <c>interleavedStereo.Length / 2</c> frames into the span (allocation-free).</summary>
    public void Render(Span<float> interleavedStereo)
    {
        var frames = interleavedStereo.Length / 2;
        var offset = 0;
        while (offset < frames)
        {
            var n = Math.Min(Block, frames - offset);
            RenderBlock(interleavedStereo.Slice(offset * 2, n * 2), n);
            offset += n;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void RenderBlock(Span<float> output, int n)
    {
        var blockEnd = _frame + n;
        ScheduleMusic(blockEnd);
        StartDueNotes(blockEnd);
        if (_flyVoice != null && _frame - _lastFlyFrame > (long)(0.3f * _rate))
        {
            if (_flyVoice.Active && _flyVoice.Generation == _flyGeneration) _flyVoice.Release();
            _flyVoice = null;
        }

        for (var g = 0; g < GroupCount; g++)
        {
            Array.Clear(_gL[g], 0, n);
            Array.Clear(_gR[g], 0, n);
            Array.Clear(_gS[g], 0, n);
        }

        foreach (var v in _voices)
        {
            if (v.Active) RenderVoice(v, n, _gL[v.Group], _gR[v.Group], _gS[v.Group]);
        }

        // Group gains at the block's start and end (linear inside: all ramps are slow compared with 64 frames).
        Span<float> g0 = stackalloc float[GroupCount];
        Span<float> g1 = stackalloc float[GroupCount];
        var music = Clamp01(MusicVolume);
        for (var d = 0; d < 2; d++)
        {
            var deck = _decks[d];
            g0[d] = MathF.Sin(deck.Fade * MathF.PI * 0.5f) * music;
            deck.Fade = MoveTowards(deck.Fade, deck.FadeTarget, _fadeStepPerFrame * n);
            g1[d] = MathF.Sin(deck.Fade * MathF.PI * 0.5f) * music;
        }

        g0[GroupFlight] = _flight * music;
        _flight += (_flightTarget - _flight) * (1f - MathF.Pow(1f - _flightK, n));
        g1[GroupFlight] = _flight * music;
        g0[GroupSfx] = 1f;
        g1[GroupSfx] = 1f;

        var mixL = _mixL;
        var mixR = _mixR;
        var mixS = _mixS;
        Array.Clear(mixL, 0, n);
        Array.Clear(mixR, 0, n);
        Array.Clear(mixS, 0, n);
        var invN = 1f / n;
        var residualK = _residualK;
        for (var g = 0; g < GroupCount; g++)
        {
            var gain = g0[g];
            var dGain = (g1[g] - g0[g]) * invN;
            var rl = _residualL[g];
            var rr = _residualR[g];
            var hasResidual = rl != 0 || rr != 0;
            if (gain == 0 && dGain == 0)
            {
                _residualL[g] = _residualR[g] = 0;
                continue;
            }

            var gl = _gL[g];
            var gr = _gR[g];
            var gs = _gS[g];
            for (var i = 0; i < n; i++)
            {
                var l = gl[i];
                var r = gr[i];
                if (hasResidual)
                {
                    l += rl;
                    r += rr;
                    rl *= residualK;
                    rr *= residualK;
                }

                mixL[i] += l * gain;
                mixR[i] += r * gain;
                mixS[i] += gs[i] * gain;
                gain += dGain;
            }

            _residualL[g] = MathF.Abs(rl) < 1e-7f ? 0 : rl;
            _residualR[g] = MathF.Abs(rr) < 1e-7f ? 0 : rr;
        }

        _reverb.Process(mixS, _wetL, _wetR, n);

        var masterTarget = _muted ? 0f : _masterVolume;
        var masterGain = _masterGain;
        var masterK = _masterK;
        var env = _limiterEnv;
        var releaseK = _limiterReleaseK;
        var wetL = _wetL;
        var wetR = _wetR;
        var bad = false;
        for (var i = 0; i < n; i++)
        {
            masterGain += (masterTarget - masterGain) * masterK;
            var l = (mixL[i] + wetL[i]) * masterGain * OutputGain;
            var r = (mixR[i] + wetR[i]) * masterGain * OutputGain;
            if (!float.IsFinite(l) || !float.IsFinite(r))
            {
                l = r = 0;
                bad = true;
            }

            // Peak limiter (instant attack, 0.2 s release) then a soft clipper: never |x| ≥ 1.
            var peak = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            if (peak > env) env = peak;
            else env += (peak - env) * releaseK;
            if (env > LimiterThreshold)
            {
                var reduce = LimiterThreshold / env;
                l *= reduce;
                r *= reduce;
            }

            output[i * 2] = SoftClip(l);
            output[i * 2 + 1] = SoftClip(r);
        }

        if (bad) _reverb.Clear();
        _masterGain = masterGain;
        _limiterEnv = env;
        _frame = blockEnd;
    }

    /// <summary>
    /// Renders one voice into its group's buffers. Envelope, glide, level and FM index run at a control rate of
    /// <see cref="ControlFrames"/> frames (0.36 ms) and are interpolated linearly inside; the oscillators and filters run
    /// per frame, in one tight loop per waveform.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void RenderVoice(Voice v, int n, float[] bufL, float[] bufR, float[] bufS)
    {
        var start = v.Delay;
        v.Delay = 0;
        var wave = v.Wave;
        if (wave == Wave.Noise) v.UpdateFilter(n);
        var table = SineTable;
        var invRate = _invRate;
        var mono = _voiceBuffer;

        // Everything the loops touch lives in locals (fast in Debug builds too) and is written back afterwards.
        float phase = v.Phase, modPhase = v.ModPhase, freq = v.FreqHz, env = v.Env, level = v.Level;
        float lp = v.ToneState, lpA = v.ToneA, fmIndex = v.FmIndex, fmRatio = v.FmRatio;
        var stage = v.Stage;
        int hold = v.HoldRemaining, glide = v.GlideRemaining;
        var noise = v.NoiseState;
        float fk = v.FilterK, a1 = v.A1, a2 = v.A2, a3 = v.A3, ic1 = v.Ic1, ic2 = v.Ic2;
        var lowpass = v.Lowpass;
        var done = false;
        var i = start;
        while (i < n && !done)
        {
            var m = Math.Min(ControlFrames, n - i);
            var full = m == ControlFrames;

            // Control rate: where the envelope, level, pitch and FM index will be at the end of these m frames.
            var envEnd = env;
            switch (stage)
            {
                case Stage.Attack:
                    envEnd = env + v.AttackInc * m;
                    if (envEnd >= 1f)
                    {
                        envEnd = 1f;
                        stage = Stage.Decay;
                    }

                    break;
                case Stage.Decay:
                    envEnd = v.Sustain + (env - v.Sustain) * (full ? v.DecayCoefControl : MathF.Pow(v.DecayCoef, m));
                    if (v.Sustain <= 0f && envEnd < EndLevel) done = true;
                    break;
                default:
                    envEnd = env * (full ? v.ReleaseCoefControl : MathF.Pow(v.ReleaseCoef, m));
                    if (envEnd < EndLevel) done = true;
                    break;
            }

            if (hold > 0)
            {
                hold -= m;
                if (hold <= 0)
                {
                    hold = 0;
                    stage = Stage.Release;
                }
            }

            if (glide > 0)
            {
                var s = Math.Min(m, glide);
                freq *= s == ControlFrames ? v.GlideRatioControl : MathF.Pow(v.GlideRatio, s);
                glide -= s;
            }

            var levelEnd = v.LevelK != 0 ? level + (v.LevelTarget - level) * (1f - MathF.Pow(1f - v.LevelK, m)) : level;
            var amp = env * level;
            var dAmp = (envEnd * levelEnd - amp) / m;
            var inc = freq * invRate;
            var end = i + m;

            switch (wave)
            {
                case Wave.Sine:
                    for (; i < end; i++)
                    {
                        var t = phase * TableSize;
                        var j = (int)t;
                        var x = table[j] + (table[j + 1] - table[j]) * (t - j);
                        phase += inc;
                        if (phase >= 1f) phase -= 1f;
                        lp += lpA * (x - lp);
                        mono[i] = lp * amp;
                        amp += dAmp;
                    }

                    break;
                case Wave.Triangle:
                    for (; i < end; i++)
                    {
                        var x = 4f * MathF.Abs(phase - 0.5f) - 1f;
                        phase += inc;
                        if (phase >= 1f) phase -= 1f;
                        lp += lpA * (x - lp);
                        mono[i] = lp * amp;
                        amp += dAmp;
                    }

                    break;
                case Wave.SoftSquare:
                    // Odd harmonics 1, 3, 5 with gentle weights: square-ish, band-limited for every note we play.
                    for (; i < end; i++)
                    {
                        var t = phase * TableSize;
                        var j = (int)t;
                        var x = table[j] + (table[j + 1] - table[j]) * (t - j);
                        var f = phase * 3f;
                        t = (f - (int)f) * TableSize;
                        j = (int)t;
                        x += 0.26f * (table[j] + (table[j + 1] - table[j]) * (t - j));
                        f = phase * 5f;
                        t = (f - (int)f) * TableSize;
                        j = (int)t;
                        x += 0.1f * (table[j] + (table[j + 1] - table[j]) * (t - j));
                        phase += inc;
                        if (phase >= 1f) phase -= 1f;
                        lp += lpA * (0.8f * x - lp);
                        mono[i] = lp * amp;
                        amp += dAmp;
                    }

                    break;
                case Wave.Bell:
                {
                    // 2-operator FM (phase modulation); the modulator's index decays faster than the note.
                    var fmEnd = fmIndex * (full ? v.FmIndexCoefControl : MathF.Pow(v.FmIndexCoef, m));
                    var depth = fmIndex * (1f / (2f * MathF.PI));
                    var dDepth = (fmEnd - fmIndex) * (1f / (2f * MathF.PI)) / m;
                    var modInc = inc * fmRatio;
                    for (; i < end; i++)
                    {
                        var t = modPhase * TableSize;
                        var j = (int)t;
                        var f = phase + 1f + (table[j] + (table[j + 1] - table[j]) * (t - j)) * depth; // depth < 1, so f > 0
                        t = (f - (int)f) * TableSize;
                        j = (int)t;
                        var x = table[j] + (table[j + 1] - table[j]) * (t - j);
                        phase += inc;
                        if (phase >= 1f) phase -= 1f;
                        modPhase += modInc;
                        if (modPhase >= 1f) modPhase -= (int)modPhase;
                        depth += dDepth;
                        lp += lpA * (x - lp);
                        mono[i] = lp * amp;
                        amp += dAmp;
                    }

                    fmIndex = fmEnd;
                    break;
                }

                default:
                    // White noise (xorshift) through a topology-preserving state-variable filter.
                    for (; i < end; i++)
                    {
                        noise ^= noise << 13;
                        noise ^= noise >> 17;
                        noise ^= noise << 5;
                        var w = (int)noise * (1f / 2147483648f);
                        var v3 = w - ic2;
                        var v1 = a1 * ic1 + a2 * v3;
                        var v2 = ic2 + a2 * ic1 + a3 * v3;
                        ic1 = 2f * v1 - ic1;
                        ic2 = 2f * v2 - ic2;
                        var x = lowpass ? v2 : fk * v1 * 1.6f; // normalised band-pass (unity peak), lifted to match the low-pass
                        lp += lpA * (x - lp);
                        mono[i] = lp * amp;
                        amp += dAmp;
                    }

                    break;
            }

            env = envEnd;
            level = levelEnd;
        }

        // Equal-power pan into the group's dry buffers, and the reverb send.
        float panL = v.PanL, panR = v.PanR, send = v.Send;
        for (var k = start; k < i; k++)
        {
            var y = mono[k];
            bufL[k] += y * panL;
            bufR[k] += y * panR;
            bufS[k] += y * send;
        }

        var last = i > start ? mono[i - 1] : 0f;
        v.LastL = last * panL;
        v.LastR = last * panR;
        v.Phase = phase;
        v.ModPhase = modPhase;
        v.FreqHz = freq;
        v.Env = env;
        v.Level = level;
        v.ToneState = lp;
        v.FmIndex = fmIndex;
        v.Stage = stage;
        v.HoldRemaining = hold;
        v.GlideRemaining = glide;
        v.NoiseState = noise;
        v.Ic1 = ic1;
        v.Ic2 = ic2;
        if (done) v.Active = false;
    }

    // ---------------------------------------------------------------- music

    private void ScheduleMusic(long blockEnd)
    {
        while (true)
        {
            var stepFrame = StepFrame(_nextStep);
            if (stepFrame >= blockEnd) break;
            OnStep(_nextStep, Math.Max(stepFrame, _frame));
            _nextStep++;
        }
    }

    private void OnStep(long step, long frame)
    {
        var deck = _decks[_current];
        var p = deck.Profile;
        var group = _current;
        var len = p.Scale.Length;
        var inBar = (int)(step % 16);
        var rel = step - deck.StartBar * 16; // sixteenths since the deck's first cycle (negative: the lead-in bar)
        var pos = (int)(((rel % CycleSixteenths) + CycleSixteenths) % CycleSixteenths);
        var bar = pos / 16;
        if (rel >= 0 && pos == 0) BuildCycle(deck, rel / CycleSixteenths);

        // Harmony: a chord per bar under the theme, a chord every 2 bars between (the theme's progression per mode).
        int chord;
        int holdSixteenths;
        var change = false;
        if (rel < 0)
        {
            chord = p.Progression[0];
            holdSixteenths = (int)-rel;
            change = deck.NeedChord;
        }
        else if (bar < deck.ThemeBars)
        {
            var drone = p.Pad != PadKind.Normal;
            // The augmented ending stretches bars 7–8 over 7–9: the harmony follows (V under the stretched approach, I under
            // the last note), and a drone ends where the theme ends (2026-11-12 review).
            var chordBar = deck.Variation == ThemeVariation.AugmentedEnding && bar >= 6 ? (bar < 8 ? 6 : 7) : bar;
            chord = drone ? p.Progression[0] : ThemeChordAt(p, chordBar);
            holdSixteenths = drone ? Math.Min(32 - pos % 32, deck.ThemeBars * 16 - pos) : 16 - pos % 16;
            change = drone ? pos % 32 == 0 : inBar == 0;
        }
        else
        {
            var free = bar - deck.ThemeBars;
            chord = p.Progression[free / 2 % p.Progression.Length];
            holdSixteenths = Math.Min(2 - free % 2, 16 - bar) * 16 - inBar;
            change = inBar == 0 && free % 2 == 0;
        }

        if (change || deck.NeedChord)
        {
            deck.NeedChord = false;
            PlayPad(p, chord, (float)(holdSixteenths * _framesPerSixteenth / _rate) + 0.05f, frame, group);
        }

        deck.Chord = chord;

        // The FileScape theme (and its variation) from the deck's prepared notes.
        if (rel >= 0)
        {
            while (deck.NextNote < deck.NoteCount && deck.Notes[deck.NextNote].Start <= pos)
            {
                var note = deck.Notes[deck.NextNote++];
                if (note.Start == pos) PlayThemeNote(deck, note, step, group);
            }
        }

        // Between returns: the freer music box, a seeded random walk in the scale on eighth notes (chord tones on the beat).
        if (inBar == 0) deck.RestBar = deck.Rng.NextDouble() < p.RestChance;
        if (rel >= 0 && bar >= deck.ThemeBars && step % 2 == 0 && !deck.RestBar
            && deck.Rng.NextDouble() < p.MelodyDensity * (inBar % 4 == 0 ? 1.2 : 0.7))
        {
            ReadOnlySpan<int> moves = [-2, -1, -1, 0, 1, 1, 2];
            var deg = deck.MelodyDegree + moves[deck.Rng.Next(moves.Length)];
            var lo = 0;
            var hi = 2 * len;
            if (deg < lo) deg = lo + (lo - deg);
            if (deg > hi) deg = hi - (deg - hi);
            if (inBar % 4 == 0)
            {
                foreach (var d in (ReadOnlySpan<int>)[0, 1, -1, 2, -2])
                {
                    var c = ((deg + d - chord) % len + len) % len;
                    if (c is 0 or 2 or 4 && deg + d >= lo && deg + d <= hi)
                    {
                        deg += d;
                        break;
                    }
                }
            }

            deck.MelodyDegree = deg;
            var accent = inBar % 4 == 0 ? 1f : 0.75f + 0.2f * (float)deck.Rng.NextDouble();
            var midi = p.Root + p.MelodyOctave * 12 + ScaleNote(p.Scale, deg);
            Enqueue(frame, NoteSpec.Bell(MidiHz(midi), ratio: p.BellRatio, index: 1.3f, indexDecay: 0.25f) with
            {
                Attack = 0.003f, Decay = 1.2f, Gain = 0.05f * accent, Pan = (float)(deck.Rng.NextDouble() * 0.9 - 0.45), Send = 0.45f, Group = group,
            });
            MelodyObserver?.Invoke(new MelodyNote(frame, midi, MelodyPart.MusicBox, deck.Variation));
        }

        // The rhythmic bed: a plucked bass (beat 1, the "and" of 2, beat 3, a pickup) and soft offbeat shaker ticks.
        if (p.Pulse)
        {
            var fifth = len == 7 ? 4 : 3;
            var bassDegree = inBar switch { 0 => chord, 6 => chord, 8 => chord + fifth, 14 => chord, _ => int.MinValue };
            if (bassDegree != int.MinValue)
            {
                var semis = ScaleNote(p.Scale, bassDegree) - ScaleNote(p.Scale, chord) + BassSemitones(p, chord);
                var gain = inBar switch { 0 => 0.085f, 8 => 0.06f, 14 => 0.035f, _ => 0.045f };
                Enqueue(frame, NoteSpec.Tone(Wave.Triangle, MidiHz(p.Root - 12 + semis)) with
                {
                    Attack = 0.004f, Decay = inBar == 0 ? 0.6f : 0.4f, Gain = gain, ToneHz = 420, Send = 0.08f, Group = group,
                });
            }

            if (inBar % 4 == 2)
            {
                Enqueue(frame, NoteSpec.Noise(8200, 6800, 0.03f, q: 1.6f) with
                {
                    Attack = 0.0015f, Decay = 0.045f, Gain = 0.02f, Pan = inBar % 8 == 2 ? -0.28f : 0.28f, Send = 0.05f, Group = group,
                });
            }
            else if (inBar % 2 == 1 && Mix((uint)step * 0x9E3779B1u ^ (uint)_seed) % 3 != 0)
            {
                Enqueue(frame, NoteSpec.Noise(9500, 8000, 0.02f, q: 1.4f) with
                {
                    Attack = 0.001f, Decay = 0.025f, Gain = 0.007f, Pan = 0.1f, Send = 0.03f, Group = group,
                });
            }
        }

        // Flight: a rising arpeggio through the chord, climbing with the intensity.
        if (_flightTarget > 0.02f)
        {
            ReadOnlySpan<int> arpBase = [0, 2, 4];
            var a = _arpCounter++ % 6;
            var arpDegree = arpBase[a % 3] + (a / 3) * len;
            var lift = _flightTarget > 0.6f ? len : 0;
            Enqueue(frame, NoteSpec.Bell(MidiHz(p.Root + 12 + ScaleNote(p.Scale, chord + arpDegree + lift)), ratio: 2f, index: 0.8f, indexDecay: 0.1f) with
            {
                Attack = 0.003f, Decay = 0.4f, Gain = 0.045f, Pan = -0.4f + 0.16f * a, Send = 0.4f, Group = GroupFlight,
            });
        }
        else
        {
            _arpCounter = 0;
        }
    }

    /// <summary>A pad chord (three soft triangles, a drone or a cluster), held for <paramref name="holdSeconds"/>.</summary>
    private void PlayPad(Profile p, int chord, float holdSeconds, long frame, int group)
    {
        ReadOnlySpan<int> offsets = p.Pad == PadKind.Cluster ? [0, 1, 2, 4] : p.Pad == PadKind.Drone ? [0, 2] : [0, 2, 4];
        var normal = p.Pad == PadKind.Normal;
        var attack = normal ? 0.45f : 2.4f;
        var release = normal ? 1.0f : 1.8f;
        for (var i = 0; i < offsets.Length; i++)
        {
            var pan = -0.4f + 0.8f * i / (offsets.Length - 1);
            var midi = p.Root + ScaleNote(p.Scale, chord + offsets[i]) - (p.Pad == PadKind.Drone ? 12 : 0);
            Enqueue(frame, NoteSpec.Tone(p.Pad == PadKind.Drone ? Wave.Sine : Wave.Triangle, MidiHz(midi)) with
            {
                Attack = attack, Decay = 2f, Sustain = 0.75f, Hold = holdSeconds, Release = release, Gain = p.PadGain * (normal ? 0.7f : 1f), ToneHz = p.PadToneHz,
                Pan = pan, Send = 0.5f, Group = group,
            });
        }

        if (!p.Pulse)
        {
            // Without the plucked bass, a soft sustained root carries the low end.
            Enqueue(frame, NoteSpec.Tone(Wave.Sine, MidiHz(p.Root - 12 + BassSemitones(p, chord))) with
            {
                Attack = attack, Decay = 2f, Sustain = 0.7f, Hold = holdSeconds, Release = release, Gain = 0.05f, Send = 0.2f, Group = group,
            });
        }

        if (p.Pad == PadKind.Cluster)
        {
            Enqueue(frame, NoteSpec.Noise(520, 520, 0, q: 0.7f, lowpass: true) with
            {
                Attack = 2.5f, Decay = 2f, Sustain = 0.8f, Hold = holdSeconds, Release = 2f, Gain = 0.025f, Send = 0.6f, Group = group,
            });
        }
    }

    /// <summary>The chord's root in semitones above the key's root, folded into −5..+7 so the bass stays low and close.</summary>
    private static int BassSemitones(Profile p, int chord)
    {
        var len = p.Scale.Length;
        var semis = p.Scale[((chord % len) + len) % len];
        return semis > 7 ? semis - 12 : semis;
    }

    private void PlayThemeNote(Deck deck, in LeadNote note, long step, int group)
    {
        var p = deck.Profile;
        var midi = p.Root + 12 + ScaleNote(p.Scale, note.Degree);
        var hz = MidiHz(midi);
        var sixteenth = (float)(_framesPerSixteenth / _rate);
        var at = Math.Max(_frame, StepFrame(step) + (long)Math.Round(note.Delay * _framesPerSixteenth));
        var themeGain = p.Pad == PadKind.Normal ? 1f : 0.55f;
        switch (note.Part)
        {
            case MelodyPart.Theme:
            {
                // A little glide from the previous note when the line is legato and the step is small.
                var glide = deck.LastLeadEnd == step && midi != deck.LastLeadMidi && Math.Abs(midi - deck.LastLeadMidi) <= 5;
                var from = glide ? MidiHz(deck.LastLeadMidi) : hz;
                Enqueue(at, NoteSpec.Bell(from, ratio: 2f, index: 1.5f, indexDecay: 0.35f) with
                {
                    GlideToHz = glide ? hz : 0, Glide = 0.035f, Attack = 0.004f, Decay = 0.8f, Sustain = 0.32f,
                    Hold = MathF.Max(0.05f, note.Length * sixteenth - 0.02f), Release = 0.4f, Gain = 0.085f * note.Velocity * themeGain,
                    Pan = 0.05f, Send = 0.35f, ToneHz = 5200, Group = group,
                });
                deck.LastLeadEnd = step + note.Length;
                deck.LastLeadMidi = midi;
                break;
            }

            case MelodyPart.Grace:
                Enqueue(at, NoteSpec.Bell(hz, ratio: 2f, index: 1f, indexDecay: 0.1f) with
                {
                    Attack = 0.002f, Decay = 0.18f, Gain = 0.05f * note.Velocity * themeGain, Pan = 0.05f, Send = 0.3f, ToneHz = 5200, Group = group,
                });
                break;
            default:
                Enqueue(at, NoteSpec.Tone(Wave.Triangle, hz) with
                {
                    Attack = 0.03f, Decay = 1.2f, Sustain = 0.45f, Hold = MathF.Max(0.05f, note.Length * sixteenth - 0.05f), Release = 0.6f,
                    Gain = 0.04f * note.Velocity * themeGain, ToneHz = 1500, Pan = -0.3f, Send = 0.4f, Group = group,
                });
                break;
        }

        MelodyObserver?.Invoke(new MelodyNote(at, midi, note.Part, deck.Variation));
    }

    // ---------------------------------------------------------------- the FileScape theme

    /// <summary>
    /// Prepares cycle <paramref name="cycle"/>'s theme notes in the deck's key: the written theme mapped into the scale,
    /// then transformed by the cycle's variation. Allocation-free (the deck's note array is reused).
    /// </summary>
    private void BuildCycle(Deck deck, long cycle)
    {
        var p = deck.Profile;
        var len = p.Scale.Length;
        var variation = VariationFor(deck.Theme, cycle, deck.Variation);
        deck.Variation = variation;
        var notes = deck.Notes;
        var n = 0;
        var theme = ThemeNotes;
        var graceDelay = 1f - 0.05f / (float)(_framesPerSixteenth / _rate); // a grace note sounds 50 ms before its note
        var pivot = int.MinValue;
        var previous = int.MinValue;
        for (var i = 0; i < theme.Length; i++)
        {
            var t = theme[i];
            var d = MapDegree(t.Degree, len);
            var next = i + 1 < theme.Length ? MapDegree(theme[i + 1].Degree, len) : d;
            int start = t.StartSixteenths, length = t.LengthSixteenths;
            var velocity = start % 16 == 0 || length >= 6 ? 1f : start % 4 == 0 ? 0.9f : 0.8f;
            switch (variation)
            {
                case ThemeVariation.OctaveCounter:
                    d += len;
                    next += len;
                    break;
                case ThemeVariation.InvertedAnswer when start is >= 32 and < 64:
                    if (pivot == int.MinValue) pivot = d;
                    d = 2 * pivot - d;
                    next = start + length < 64 ? 2 * pivot - next : next;
                    break;
                case ThemeVariation.AugmentedEnding when start >= 96:
                    start = 96 + 2 * (start - 96);
                    length *= 2;
                    break;
            }

            if (variation == ThemeVariation.Ornamented)
            {
                if (previous != int.MinValue && d - previous >= 3) notes[n++] = new LeadNote(start - 1, 1, d - 1, 0.55f, graceDelay, MelodyPart.Grace);
                previous = d;
                if (i + 1 < theme.Length && length >= 6)
                {
                    // A turn on the long notes: the note, its upper neighbour, the note again.
                    notes[n++] = new LeadNote(start, 3, d, velocity, 0, MelodyPart.Theme);
                    notes[n++] = new LeadNote(start + 3, 1, d + 1, 0.6f, 0, MelodyPart.Theme);
                    notes[n++] = new LeadNote(start + 4, length - 4, d, 0.75f, 0, MelodyPart.Theme);
                    continue;
                }

                if (i + 1 < theme.Length && length >= 4)
                {
                    // A passing note into a third, otherwise a neighbour note away from the next one.
                    var diff = next - d;
                    var ornament = Math.Abs(diff) == 2 ? d + Math.Sign(diff) : diff > 0 ? d - 1 : d + 1;
                    notes[n++] = new LeadNote(start, length - 2, d, velocity, 0, MelodyPart.Theme);
                    notes[n++] = new LeadNote(start + length - 2, 2, ornament, 0.7f, 0, MelodyPart.Theme);
                    continue;
                }
            }

            notes[n++] = new LeadNote(start, length, d, velocity, 0, MelodyPart.Theme);
        }

        if (variation == ThemeVariation.Syncopated)
        {
            // Anticipate a beat where the note before can give up an eighth, otherwise delay the note by one.
            for (var i = 1; i < n; i++)
            {
                ref var cur = ref notes[i];
                ref var prev = ref notes[i - 1];
                if (cur.Start % 4 != 0 || prev.Start + prev.Length != cur.Start) continue;
                if (prev.Length >= 4)
                {
                    prev.Length -= 2;
                    cur.Start -= 2;
                    cur.Length += 2;
                }
                else if (cur.Length >= 4)
                {
                    prev.Length += 2;
                    cur.Start += 2;
                    cur.Length -= 2;
                }
            }
        }

        if (variation == ThemeVariation.OctaveCounter)
        {
            // A slow counter-line of chord tones an octave below the (raised) theme, moving every half bar.
            var counter = 2;
            for (var b = 0; b < ThemeLengthBars; b++)
            {
                var root = p.Pad == PadKind.Normal ? ThemeChordAt(p, b) : p.Progression[0];
                var halves = b == ThemeLengthBars - 1 ? 1 : 2;
                for (var h = 0; h < halves; h++)
                {
                    var best = counter;
                    var bestDistance = int.MaxValue;
                    foreach (var k in (ReadOnlySpan<int>)[0, 2, 4])
                    {
                        for (var octave = -1; octave <= 1; octave++)
                        {
                            var c = root + k + octave * len;
                            if (c < -2 || c > len + 3 || (h == 1 && c == counter)) continue;
                            var distance = Math.Abs(c - counter);
                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                best = c;
                            }
                        }
                    }

                    counter = best;
                    notes[n++] = new LeadNote(b * 16 + h * 8, halves == 1 ? 16 : 8, counter, 0.8f, 0, MelodyPart.Counter);
                }
            }
        }

        // Order by onset (insertion sort: a few dozen notes, once per 16 bars).
        for (var i = 1; i < n; i++)
        {
            var x = notes[i];
            var j = i - 1;
            while (j >= 0 && notes[j].Start + notes[j].Delay > x.Start + x.Delay)
            {
                notes[j + 1] = notes[j];
                j--;
            }

            notes[j + 1] = x;
        }

        var end = 0;
        for (var i = 0; i < n; i++)
        {
            if (notes[i].Part == MelodyPart.Theme) end = Math.Max(end, notes[i].Start + notes[i].Length);
        }

        deck.NoteCount = n;
        deck.NextNote = 0;
        deck.ThemeBars = Math.Clamp((end + 15) / 16, ThemeLengthBars, CycleBars - 4);
        deck.LastLeadEnd = long.MinValue;
    }

    /// <summary>The variation of a theme's cycle: plain on cycles 0, 4, 8, …; otherwise a seeded choice that never repeats the last one.</summary>
    private ThemeVariation VariationFor(PlanetTheme theme, long cycle, ThemeVariation previous)
    {
        if (cycle % 4 == 0) return ThemeVariation.Plain;
        var h = Mix((uint)_seed * 0x9E3779B1u ^ (uint)((int)theme + 1) * 0x85EBCA77u ^ (uint)cycle * 0xC2B2AE3Du);
        var v = 1 + (int)(h % 5);
        if ((ThemeVariation)v == previous) v = 1 + v % 5;
        return (ThemeVariation)v;
    }

    /// <summary>A theme degree (7-note numbering) in a scale of <paramref name="len"/> notes: identity for 7, la/sol/mi/re/do for pentatonics.</summary>
    private static int MapDegree(int degree, int len)
    {
        if (len == 7) return degree;
        var octave = (int)Math.Floor(degree / 7.0);
        var r = degree - 7 * octave;
        ReadOnlySpan<int> pentatonic = [0, 1, 2, 2, 3, 4, 4];
        return (len == 5 ? pentatonic[r] : (int)Math.Round(r * len / 7.0)) + len * octave;
    }

    /// <summary>The chord root under bar <paramref name="bar"/> of the theme, per mode (avoiding the mode's diminished triad).</summary>
    private static int ThemeChordAt(Profile p, int bar)
    {
        if (bar < 0 || bar >= ThemeLengthBars) return 0; // an augmented ending's extra bar rests on the tonic
        ReadOnlySpan<int> chords = p.Scale.Length == 5 ? [0, 4, 0, 3, 1, 0, 3, 0]
            : ReferenceEquals(p.Scale, Lydian) ? [0, 5, 0, 4, 1, 0, 4, 0]
            : ReferenceEquals(p.Scale, Dorian) ? [0, 3, 0, 4, 6, 0, 4, 0]
            : [0, 5, 0, 4, 3, 0, 4, 0];
        return chords[bar];
    }

    private static uint Mix(uint h)
    {
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }

    private static ThemeNote[] BuildTheme()
    {
        // (degree, sixteenths) per bar: motif (bars 1–2), answer (3–4), the motif raised (5–6), the cadence (7–8).
        (int Degree, int Length)[][] bars =
        [
            [(2, 2), (4, 2), (7, 6), (5, 2), (4, 4)],
            [(5, 2), (4, 2), (2, 2), (1, 2), (2, 8)],
            [(2, 2), (4, 2), (7, 6), (8, 2), (9, 4)],
            [(8, 2), (7, 2), (5, 4), (4, 8)],
            [(4, 2), (5, 2), (9, 6), (8, 2), (7, 4)],
            [(5, 2), (7, 2), (4, 2), (2, 2), (4, 8)],
            [(5, 2), (4, 2), (2, 2), (4, 2), (1, 6), (2, 2)],
            [(0, 8)],
        ];
        var notes = new List<ThemeNote>();
        for (var b = 0; b < bars.Length; b++)
        {
            var at = b * 16;
            foreach (var (degree, length) in bars[b])
            {
                notes.Add(new ThemeNote(at, length, degree));
                at += length;
            }
        }

        return [.. notes];
    }

    // ---------------------------------------------------------------- voices and scheduling

    private void Enqueue(long frame, NoteSpec note)
    {
        if (note.Group == -1) note = note with { Group = GroupSfx };
        _pending.Add((Math.Max(frame, _frame), note));
    }

    private void StartDueNotes(long blockEnd)
    {
        for (var i = 0; i < _pending.Count;)
        {
            var (frame, note) = _pending[i];
            if (frame < blockEnd)
            {
                StartVoice(note, (int)(frame - _frame));
                _pending[i] = _pending[^1];
                _pending.RemoveAt(_pending.Count - 1);
            }
            else
            {
                i++;
            }
        }
    }

    private Voice? StartVoice(NoteSpec note, int delay)
    {
        var music = note.Group != GroupSfx;
        Voice? slot = null;
        var musicCount = 0;
        foreach (var v in _voices)
        {
            if (!v.Active) slot ??= v;
            else if (v.Group != GroupSfx) musicCount++;
        }

        if (music && (musicCount >= MaxMusicVoices || slot == null))
        {
            // The music's share is full: take over the quietest music voice (never an effect's).
            slot = null;
            var quietest = float.MaxValue;
            foreach (var v in _voices)
            {
                if (!v.Active || v.Group == GroupSfx) continue;
                if (v.Stage == Stage.Attack && v.Env == 0f) continue; // started this pass: never steal a note that has not sounded
                var loud = v.Env * v.Level;
                if (loud < quietest)
                {
                    quietest = loud;
                    slot = v;
                }
            }

            if (slot == null) return null;
            _residualL[slot.Group] += slot.LastL;
            _residualR[slot.Group] += slot.LastR;
        }
        else if (slot == null)
        {
            // Steal the quietest voice; its last output decays through the group's residual, so the cut never clicks.
            var best = float.MaxValue;
            foreach (var v in _voices)
            {
                if (v.Stage == Stage.Attack && v.Env == 0f) continue; // started this pass
                var loud = v.Env * v.Level;
                if (loud < best)
                {
                    best = loud;
                    slot = v;
                }
            }

            if (slot == null) return null; // every voice has only just started: drop this note
            _residualL[slot.Group] += slot.LastL;
            _residualR[slot.Group] += slot.LastR;
        }

        slot!.Start(note, delay, _rate, ++_generation, NextSeed());
        return slot;
    }

    private uint NextSeed()
    {
        _noiseSeed = _noiseSeed * 1664525u + 1013904223u;
        return _noiseSeed | 1u;
    }

    private float Rand01()
    {
        var s = NextSeed();
        return (s >> 8) * (1f / 16777216f);
    }

    private long StepFrame(long step) => (long)Math.Ceiling(step * _framesPerSixteenth - 1e-6);

    /// <summary>The next point of the 32nd-note grid at or after the render cursor.</summary>
    private long NextGridFrame()
    {
        var half = _framesPerSixteenth / 2;
        var k = (long)Math.Ceiling(_frame / half - 1e-9);
        return Math.Max(_frame, (long)Math.Ceiling(k * half - 1e-6));
    }

    private long GridStepFrames() => (long)Math.Round(_framesPerSixteenth / 2);

    private long Frames(float seconds) => (long)Math.Round(seconds * _rate);

    private float Coef(float t60Seconds) => MathF.Exp(MathF.Log(0.001f) / (t60Seconds * _rate));

    private int[] CurrentScale => _decks[_current].Profile.Scale;

    private float PitchHz(int degree, int octave)
    {
        var p = _decks[_current].Profile;
        return MidiHz(p.Root + 12 * octave + ScaleNote(p.Scale, degree));
    }

    private static int ScaleNote(int[] scale, int degree)
    {
        var len = scale.Length;
        var oct = (int)Math.Floor(degree / (double)len);
        return scale[degree - oct * len] + 12 * oct;
    }

    private static float MidiHz(int note) => 440f * MathF.Pow(2f, (note - 69) / 12f);

    private static float Clamp01(float x) => float.IsFinite(x) ? Math.Clamp(x, 0f, 1f) : 0f;

    private static float MoveTowards(float x, float target, float step) => x < target ? MathF.Min(target, x + step) : MathF.Max(target, x - step);

    private static float SoftClip(float x)
    {
        const float knee = 0.85f;
        var a = MathF.Abs(x);
        if (a > knee) a = knee + (1f - knee) * MathF.Tanh((a - knee) / (1f - knee));
        a = MathF.Min(a, 0.9999f);
        return x < 0 ? -a : a;
    }

    // A 2048-entry sine table with linear interpolation (phase in cycles, 0..1).
    private const int TableSize = 2048;
    private static readonly float[] SineTable = BuildSine();

    private static float[] BuildSine()
    {
        var t = new float[TableSize + 1];
        for (var i = 0; i <= TableSize; i++) t[i] = MathF.Sin(2f * MathF.PI * i / TableSize);
        return t;
    }

    // ---------------------------------------------------------------- theme profiles

    private enum PadKind
    {
        Normal,
        Drone,
        Cluster,
    }

    private sealed record Profile(
        int Root, string Mode, int[] Scale, int[] Progression, double MelodyDensity, double RestChance, int MelodyOctave,
        float BellRatio, bool Pulse, PadKind Pad, float PadToneHz, float PadGain);

    private static readonly int[] Major = [0, 2, 4, 5, 7, 9, 11];
    private static readonly int[] Lydian = [0, 2, 4, 6, 7, 9, 11];
    private static readonly int[] Mixolydian = [0, 2, 4, 5, 7, 9, 10];
    private static readonly int[] Dorian = [0, 2, 3, 5, 7, 9, 10];
    private static readonly int[] MajorPentatonic = [0, 2, 4, 7, 9];
    private static readonly int[] MinorPentatonic = [0, 3, 5, 7, 10];

    // Indexed by PlanetTheme. Roots sit in the octave C3..B3 (MIDI 48..59); melodies an octave or two above.
    private static readonly Profile[] Profiles =
    [
        new(53, "lydian", Lydian, [0, 1, 5, 4], 0.45, 0.25, 1, 3.0f, true, PadKind.Normal, 1200, 0.045f),               // Hearth: F
        new(50, "major pentatonic", MajorPentatonic, [0, 3, 1, 4], 0.5, 0.2, 2, 4.0f, true, PadKind.Normal, 1500, 0.04f),  // Meadow: D
        new(55, "mixolydian", Mixolydian, [0, 6, 3, 0], 0.5, 0.2, 1, 2.0f, true, PadKind.Normal, 1100, 0.045f),            // Workshop: G
        new(58, "major", Major, [0, 5, 3, 4], 0.4, 0.3, 1, 3.5f, true, PadKind.Normal, 1000, 0.045f),                     // Library: B♭
        new(52, "major pentatonic", MajorPentatonic, [0, 2, 3, 1], 0.55, 0.2, 2, 1.4f, true, PadKind.Normal, 1600, 0.04f), // Chime: E
        new(57, "dorian", Dorian, [0, 3, 6, 4], 0.45, 0.25, 1, 2.5f, true, PadKind.Normal, 1000, 0.045f),                 // Theatre: A
        new(48, "minor pentatonic", MinorPentatonic, [0, 2, 3, 1], 0.35, 0.3, 2, 2.0f, true, PadKind.Normal, 900, 0.045f), // Vault: C
        new(59, "lydian", Lydian, [0, 1, 4, 5], 0.5, 0.2, 1, 3.5f, true, PadKind.Normal, 1800, 0.04f),                    // Crystal: B
        new(55, "major pentatonic", MajorPentatonic, [0, 1, 3, 2], 0.6, 0.15, 2, 3.0f, true, PadKind.Normal, 1400, 0.04f), // Wild: G
        new(50, "minor pentatonic (drone)", MinorPentatonic, [0, 0, 3, 0], 0.12, 0.5, 1, 3.0f, false, PadKind.Drone, 700, 0.05f), // Quiet: D
        new(51, "major pentatonic (cluster)", MajorPentatonic, [0, 3, 0, 2], 0.15, 0.45, 2, 3.7f, false, PadKind.Cluster, 650, 0.035f), // Veiled: E♭
    ];

    private sealed class Deck
    {
        public PlanetTheme Theme;
        public Profile Profile = Profiles[0];
        public float Fade;
        public float FadeTarget;
        public Random Rng = new(0);
        public bool NeedChord;
        public bool RestBar;
        public int MelodyDegree;
        public int Chord;

        /// <summary>The bar (counted from the synth's start) where this deck's first theme cycle begins.</summary>
        public long StartBar;
        public ThemeVariation Variation;
        public int ThemeBars = ThemeLengthBars;
        public readonly LeadNote[] Notes = new LeadNote[MaxCycleNotes];
        public int NoteCount;
        public int NextNote;
        public int LastLeadMidi;
        public long LastLeadEnd = long.MinValue;

        /// <summary>Starts over for a new theme: the first cycle (the plain theme) begins at bar <paramref name="startBar"/>.</summary>
        public void ResetMelody(long startBar = 0)
        {
            MelodyDegree = Profile.Scale.Length / 2 + 2;
            StartBar = startBar;
            Variation = ThemeVariation.Plain;
            NoteCount = NextNote = 0;
            LastLeadEnd = long.MinValue;
            Chord = Profile.Progression[0];
        }
    }

    /// <summary>A prepared theme note of the current cycle: onset/length in sixteenths from the cycle start, a degree in the deck's scale.</summary>
    private struct LeadNote(int start, int length, int degree, float velocity, float delay, MelodyPart part)
    {
        public int Start = start;
        public int Length = length;
        public int Degree = degree;
        public float Velocity = velocity;

        /// <summary>Extra onset delay in sixteenths (a grace note sounds just before its note).</summary>
        public float Delay = delay;
        public MelodyPart Part = part;
    }

    // ---------------------------------------------------------------- voice

    private enum Wave
    {
        Sine,
        Triangle,
        SoftSquare,
        Bell,
        Noise,
    }

    private enum Stage
    {
        Attack,
        Decay,
        Release,
    }

    /// <summary>What to play. Times are seconds (Attack, Glide, Hold), Decay/Release are T60 seconds; frequencies Hz.</summary>
    private readonly record struct NoteSpec
    {
        public Wave Wave { get; init; }
        public float FreqHz { get; init; }
        public float GlideToHz { get; init; }
        public float Glide { get; init; }
        public float Attack { get; init; }
        public float Decay { get; init; }
        public float Sustain { get; init; }
        public float Hold { get; init; }
        public float Release { get; init; }
        public float Gain { get; init; }
        public float Pan { get; init; }
        public float Send { get; init; }
        public float ToneHz { get; init; }
        public float FmRatio { get; init; }
        public float FmIndex { get; init; }
        public float FmIndexDecay { get; init; }
        public float CutoffHz { get; init; }
        public float CutoffToHz { get; init; }
        public float CutoffGlide { get; init; }
        public float Q { get; init; }
        public bool Lowpass { get; init; }
        public int Group { get; init; }

        public static NoteSpec Tone(Wave wave, float hz) => new() { Wave = wave, FreqHz = hz, Attack = 0.003f, Decay = 0.5f, Release = 0.3f, Gain = 0.1f, Group = -1 };

        public static NoteSpec Bell(float hz, float ratio, float index, float indexDecay) =>
            Tone(Wave.Bell, hz) with { FmRatio = ratio, FmIndex = index, FmIndexDecay = indexDecay };

        public static NoteSpec Noise(float cutoffHz, float cutoffToHz, float glideSeconds, float q, bool lowpass = false) =>
            Tone(Wave.Noise, 0) with { CutoffHz = cutoffHz, CutoffToHz = cutoffToHz, CutoffGlide = glideSeconds, Q = q, Lowpass = lowpass };
    }

    private sealed class Voice
    {
        public bool Active;
        public bool Released;
        public int Generation;
        public int Group;
        public Wave Wave;
        public int Delay;
        public float Phase, ModPhase, FreqHz, GlideRatio;
        public int GlideRemaining;
        public Stage Stage;
        public float Env, AttackInc, DecayCoef, Sustain, ReleaseCoef;
        public float DecayCoefControl, ReleaseCoefControl, GlideRatioControl, FmIndexCoefControl; // per ControlFrames
        public int HoldRemaining;
        public float Level, LevelTarget, LevelK;
        public float PanL, PanR, Send;
        public float ToneA, ToneState;
        public float FmRatio, FmIndex, FmIndexCoef;
        public uint NoiseState;
        public float LastL, LastR;

        // Noise filter (topology-preserving state-variable filter).
        private float _cutoffHz, _cutoffRatio, _cutoffTargetHz, _cutoffK;
        private int _cutoffRemaining;
        public float FilterK, A1, A2, A3, Ic1, Ic2;
        public bool Lowpass;
        private int _rate;

        public void Start(in NoteSpec n, int delay, int rate, int generation, uint noiseSeed)
        {
            _rate = rate;
            Active = true;
            Released = false;
            Generation = generation;
            Group = n.Group;
            Wave = n.Wave;
            Delay = delay;
            Phase = 0;
            ModPhase = 0;
            FreqHz = n.FreqHz;
            var glideFrames = (int)Math.Round(n.Glide * rate);
            GlideRemaining = n.GlideToHz > 0 && glideFrames > 0 ? glideFrames : 0;
            GlideRatio = GlideRemaining > 0 ? MathF.Pow(n.GlideToHz / n.FreqHz, 1f / glideFrames) : 1f;
            Stage = Stage.Attack;
            Env = 0;
            AttackInc = 1f / MathF.Max(1f, n.Attack * rate);
            DecayCoef = T60(n.Decay, rate);
            Sustain = n.Sustain;
            ReleaseCoef = T60(MathF.Max(0.01f, n.Release), rate);
            HoldRemaining = n.Hold > 0 ? Math.Max(1, (int)(n.Hold * rate)) : 0;
            Level = n.Gain;
            LevelTarget = n.Gain;
            LevelK = 0;
            var angle = (Math.Clamp(n.Pan, -1f, 1f) + 1f) * MathF.PI * 0.25f;
            PanL = MathF.Cos(angle);
            PanR = MathF.Sin(angle);
            Send = n.Send;
            ToneA = n.ToneHz > 0 ? 1f - MathF.Exp(-2f * MathF.PI * MathF.Min(n.ToneHz, 0.45f * rate) / rate) : 1f;
            ToneState = 0;
            FmRatio = n.FmRatio;
            FmIndex = n.FmIndex;
            FmIndexCoef = n.FmIndexDecay > 0 ? T60(n.FmIndexDecay * 3f, rate) : 1f; // index falls ≈ 20 dB in FmIndexDecay
            DecayCoefControl = MathF.Pow(DecayCoef, ControlFrames);
            ReleaseCoefControl = MathF.Pow(ReleaseCoef, ControlFrames);
            GlideRatioControl = MathF.Pow(GlideRatio, ControlFrames);
            FmIndexCoefControl = MathF.Pow(FmIndexCoef, ControlFrames);
            NoiseState = noiseSeed;
            LastL = LastR = 0;
            Ic1 = Ic2 = 0;
            Lowpass = n.Lowpass;
            FilterK = 1f / MathF.Max(0.5f, n.Q);
            _cutoffHz = Math.Clamp(n.CutoffHz, 20f, 0.45f * rate);
            var cutoffFrames = (int)Math.Round(n.CutoffGlide * rate);
            _cutoffRemaining = n.CutoffToHz > 0 && cutoffFrames > 0 ? cutoffFrames : 0;
            _cutoffRatio = _cutoffRemaining > 0 ? MathF.Pow(Math.Clamp(n.CutoffToHz, 20f, 0.45f * rate) / _cutoffHz, 1f / cutoffFrames) : 1f;
            _cutoffTargetHz = 0;
            _cutoffK = 0;
            if (Wave == Wave.Noise) SetCoefficients();
        }

        public void Release()
        {
            Released = true;
            if (Stage != Stage.Release) Stage = Stage.Release;
            HoldRemaining = 0;
        }

        public void SetCutoffTarget(float hz, float k)
        {
            _cutoffTargetHz = Math.Clamp(hz, 20f, 0.45f * _rate);
            _cutoffK = k;
            _cutoffRemaining = 0;
        }

        /// <summary>Advances the cutoff by one block (glide or smoothing) and recomputes the filter.</summary>
        public void UpdateFilter(int n)
        {
            if (_cutoffRemaining > 0)
            {
                var steps = Math.Min(n, _cutoffRemaining);
                _cutoffHz *= MathF.Pow(_cutoffRatio, steps);
                _cutoffRemaining -= steps;
            }
            else if (_cutoffK > 0)
            {
                _cutoffHz += (_cutoffTargetHz - _cutoffHz) * (1f - MathF.Pow(1f - _cutoffK, n));
            }
            else
            {
                return;
            }

            SetCoefficients();
        }

        private void SetCoefficients()
        {
            var g = MathF.Tan(MathF.PI * MathF.Min(_cutoffHz, 0.45f * _rate) / _rate);
            A1 = 1f / (1f + g * (g + FilterK));
            A2 = g * A1;
            A3 = g * A2;
        }

        private static float T60(float seconds, int rate) => MathF.Exp(MathF.Log(0.001f) / (MathF.Max(0.002f, seconds) * rate));
    }

    // ---------------------------------------------------------------- reverb

    /// <summary>A light Freeverb-style reverb: 4 damped feedback combs and 2 allpasses per channel, mono in, stereo out.</summary>
    private sealed class Reverb
    {
        private static readonly int[] CombTuning = [1116, 1277, 1422, 1557];
        private static readonly int[] AllpassTuning = [556, 341];
        private const int Spread = 23;
        private const float Feedback = 0.83f;
        private const float Damp = 0.3f;
        private const float InputGain = 0.06f;

        private readonly float[][] _combL, _combR, _apL, _apR;
        private readonly int[] _ciL, _ciR, _aiL, _aiR;
        private readonly float[] _filtL, _filtR;

        public Reverb(int rate)
        {
            var scale = rate / 44100.0;
            _combL = Lines(CombTuning, scale, 0);
            _combR = Lines(CombTuning, scale, Spread);
            _apL = Lines(AllpassTuning, scale, 0);
            _apR = Lines(AllpassTuning, scale, Spread);
            _ciL = new int[CombTuning.Length];
            _ciR = new int[CombTuning.Length];
            _aiL = new int[AllpassTuning.Length];
            _aiR = new int[AllpassTuning.Length];
            _filtL = new float[CombTuning.Length];
            _filtR = new float[CombTuning.Length];
        }

        private static float[][] Lines(int[] tuning, double scale, int spread)
        {
            var lines = new float[tuning.Length][];
            for (var i = 0; i < tuning.Length; i++) lines[i] = new float[Math.Max(1, (int)Math.Round((tuning[i] + spread) * scale))];
            return lines;
        }

        public void Clear()
        {
            foreach (var l in _combL) Array.Clear(l);
            foreach (var l in _combR) Array.Clear(l);
            foreach (var l in _apL) Array.Clear(l);
            foreach (var l in _apR) Array.Clear(l);
            Array.Clear(_filtL);
            Array.Clear(_filtR);
        }

        public void Process(float[] input, float[] left, float[] right, int n)
        {
            Channel(input, left, n, _combL, _ciL, _filtL, _apL, _aiL);
            Channel(input, right, n, _combR, _ciR, _filtR, _apR, _aiR);
        }

        // Block-wise: combs in parallel, then allpasses in series. Every line is longer than a block, so this is exactly
        // the per-sample Freeverb structure.
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void Channel(float[] input, float[] output, int n, float[][] combs, int[] ci, float[] filt, float[][] aps, int[] ai)
        {
            Array.Clear(output, 0, n);
            for (var c = 0; c < combs.Length; c++)
            {
                var line = combs[c];
                var idx = ci[c];
                var fs = filt[c];
                for (var i = 0; i < n; i++)
                {
                    var o = line[idx];
                    fs = o * (1f - Damp) + fs * Damp;
                    line[idx] = input[i] * InputGain + fs * Feedback;
                    if (++idx == line.Length) idx = 0;
                    output[i] += o;
                }

                ci[c] = idx;
                filt[c] = fs;
            }

            for (var a = 0; a < aps.Length; a++)
            {
                var line = aps[a];
                var idx = ai[a];
                for (var i = 0; i < n; i++)
                {
                    var b = line[idx];
                    var s = output[i];
                    line[idx] = s + b * 0.5f;
                    output[i] = b - s;
                    if (++idx == line.Length) idx = 0;
                }

                ai[a] = idx;
            }
        }
    }
}
