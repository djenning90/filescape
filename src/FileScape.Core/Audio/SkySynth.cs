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
    /// <summary>A note of the theme's lead line (the world's lead instrument, with a little glide).</summary>
    Theme,

    /// <summary>A grace note just before a theme note.</summary>
    Grace,

    /// <summary>The counter-line under an <see cref="ThemeVariation.OctaveCounter"/> return, or a revisit's counter-line.</summary>
    Counter,

    /// <summary>The freer music box between returns (the hub and Windows volumes).</summary>
    MusicBox,

    /// <summary>A world's own motif between returns: the band motif, the repository's commit figure, a typed command, a gateway call.</summary>
    Motif,

    /// <summary>A folder band's second theme between returns (AD-66: the lantern song or the trail call, SkySynth.SecondThemes.cs).</summary>
    SecondTheme,
}

/// <summary>A melody note as it is scheduled (diagnostics: <see cref="SkySynth.MelodyObserver"/>).</summary>
/// <param name="Frame">The output frame at which it starts.</param>
/// <param name="MidiNote">Its pitch as a MIDI note number.</param>
/// <param name="Part">The melodic layer.</param>
/// <param name="Variation">The variation of the cycle it was played in.</param>
public readonly record struct MelodyNote(long Frame, int MidiNote, MelodyPart Part, ThemeVariation Variation);

/// <summary>The musical identity of a world (AD-49): one per <see cref="WorldKind"/>, and one per depth band for folders.</summary>
public enum MusicIdentity
{
    /// <summary>This computer: calm, open, the theme stated simply; its phrases end on a question.</summary>
    Hub,

    /// <summary>A Windows volume: a bright, glassy arpeggiated pulse in lydian.</summary>
    WindowsRoot,

    /// <summary>A WSL distribution: warm square plucks, a typing figure, dorian.</summary>
    WslRoot,

    /// <summary>A repository: a tight clockwork groove and the "commit" call and response, mixolydian.</summary>
    Repository,

    /// <summary>Folder band 1 (the outskirts): a bouncy swung groove with marimba.</summary>
    Outskirts,

    /// <summary>
    /// Folder band 2 (the trails), the festival arrangement (AD-52): a pumping big-room drop with the theme on a supersaw,
    /// a breakdown, a build (snare roll, riser, filter opening) and the drop again; a flight out of it builds, and the
    /// arrival drops.
    /// </summary>
    Trails,

    /// <summary>
    /// Folder band 3 (the deep woods), the orchestral arrangement (AD-52): strings, horns, woodwinds, timpani and harp in
    /// dorian, the theme passed between them with dynamics that rise and fall across the cycle; a broad half-time theme
    /// over an eighth-note string ostinato (156 BPM under a 78 BPM theme since AD-66; 144 under 72 before).
    /// </summary>
    DeepWoods,

    /// <summary>
    /// Folder band 4 (the underglow), the drum-and-bass arrangement (AD-52): 172 BPM breakbeats under a half-time theme,
    /// a detuned reese bass and sub, airy strings and a glowing lead, dorian.
    /// </summary>
    Underglow,

    /// <summary>
    /// A hazard world (<see cref="WorldKind.Hazard"/>): AD-60's frenetic "get out of here" score in the same phrygian
    /// tonality, the theme at full tempo (152 BPM) over a racing heartbeat, a ♭2 bass ostinato, ticking metal and the
    /// beating drone, with a break and a build before each return; with its own warning and fall cues
    /// (<see cref="SkySynth.HazardWarning"/>). The AD-52 score it replaced is kept as <see cref="Abyss"/>.
    /// </summary>
    Hazard,

    /// <summary>
    /// The AD-52 hazard score, kept unchanged (AD-60) for a future world type (proposed: a sealed world, a folder FileScape
    /// cannot list): ominous and unstable but still the theme, in phrygian at half time (70 over a 140 pulse) over a beating
    /// drone, a heartbeat and ticking metal. No world kind maps to it yet; it is heard through
    /// <see cref="SkySynth.SetWorld(PlanetTheme, WorldCharacter, int, MusicIdentity)"/>. Appended last, so the other
    /// identities keep their numbers (and the behaviour baseline its audio).
    /// </summary>
    Abyss,
}

/// <summary>
/// The Planets experience's original procedural audio ("Lantern Cosmos"): generative music and pitched sound effects,
/// synthesised from scratch in plain C# (no engine types, no samples), so it can be rendered offline and tested.
/// <para>
/// <b>Worlds (AD-49, AD-52, AD-60, AD-66).</b> The music has a family of ten identities (<see cref="MusicIdentity"/>), nine chosen from
/// a world's <see cref="WorldCharacter"/> by <see cref="IdentityOf"/> (the tenth, the abyss, is AD-52's hazard score kept for a
/// future world type), each with its own tempo (<see cref="TempoFor"/>: the hub 94 BPM, roots 112–116, the repository 126,
/// folder bands 120 → 126 → 156 → 172 (the orchestra lifted from 144 by AD-66), the hazard world 152), groove, instruments and motif. Three folder bands are AD-52's arrangements
/// (SkySynth.Arrangements.cs, with AD-60's breaks, builds, drops and climaxes): the trails a festival drop and build, the deep
/// woods an orchestra, the underglow drum and bass; the hazard world plays AD-60's frenetic phrygian score
/// (SkySynth.Escape.cs); the orchestra, drum and bass and the abyss
/// play the theme at half their pulse (<see cref="ThemeTempoFor"/>). The key's root comes from the
/// world's <see cref="PlanetTheme"/> and the mode from the theme or the identity (<see cref="KeyFor(PlanetTheme, WorldCharacter)"/>).
/// <see cref="SetWorld"/> is called on every arrival. A new world starts on the next beat of the music playing (its own
/// first downbeat, then its own clock); the old deck stops there and its tails fade (equal power, <see cref="ThemeCrossfade"/>).
/// A call before that downbeat only retargets it, so hops never stack; a call for the same world and key keeps the groove
/// and answers with a drum fill. Worlds left recently (8 remembered) resume where they were, on the next half-cycle,
/// rather than starting cold; the <c>visits</c> count develops a world (a counter-line from the second visit, fuller drums
/// and later variations from the third).
/// </para>
/// <para>
/// <b>Music.</b> The music runs in 16-bar cycles (<see cref="CycleBars"/>): the original FileScape theme
/// (<see cref="ThemeMelody"/>, 8 bars: a 2-bar motif, its answer, the motif raised, a cadence; written in scale degrees so it
/// fits every key and mode) on the world's lead instrument, then 8 bars of the world's own material (the hub's music box
/// and rising "journeys", the band motif — the theme's motif made bouncy and varied per band —, the repository's commit
/// figure, a typed command and its answer, a gateway call). In the folder bands, bars 8–11 of every odd cycle (and of a
/// fresh revisit's first free section) play a second theme instead of the band motif (AD-66, SkySynth.SecondThemes.cs: the
/// lantern song in the outskirts and the deep woods, the trail call in the trails and the underglow). Each return is a <see cref="ThemeVariation"/>. In the hub the
/// theme's cadence is replaced by a rising question that stays open. Under it: a pad (a progression per mode), a bass line
/// and, outside the hub, a synthesised drum kit (kick, snare or clap, hats, toms, ticks) per identity; while
/// <see cref="SetFlight"/> is above zero, a rising arpeggio joins and the kick and snare thin out.
/// </para>
/// <para>
/// <b>Effects.</b> All are pitched in the key of the music playing. Movement sounds (<see cref="Step"/>, <see cref="Jump"/>,
/// <see cref="AirJump"/>, <see cref="Land"/>, <see cref="WindUp"/>, <see cref="Launch"/>, <see cref="Fly"/>) start at
/// the render cursor (<see cref="ImmediateLatencyBound"/>). The others (<see cref="Arrive"/>, <see cref="Select"/>,
/// <see cref="Inspect"/>, <see cref="Open"/>, <see cref="Deny"/>, <see cref="Chime"/>) wait for the next point of the
/// playing music's 32nd-note grid (79.8 ms at 94 BPM, 56.8 ms at 132), so they lock to its tempo and never wait longer than
/// <see cref="QuantisedLatencyBound"/>. <see cref="AlignToGrid"/> stretches a wind-up so the launch lands on a sixteenth.
/// The launch, landing and arrival cues duck the music briefly, so the drums never mask them. In a festival world a flight
/// (<see cref="Fly"/>) plays a build and the next <see cref="Arrive"/> releases it as a drop (AD-52). The hazard cues
/// (<see cref="HazardWarning"/>, self-throttling, every frame near an edge; <see cref="FallStart"/>, <see cref="Fall"/> every
/// frame of the fall, <see cref="FallReturn"/>) duck the music under them, deep for the fall.
/// </para>
/// <para>
/// <b>Engine.</b> A pool of <see cref="MaxVoices"/> voices (sine, triangle, soft band-limited square, 2-operator FM bell,
/// state-variable-filtered noise, and a polyBLEP saw stack of up to three detuned copies through a two-pole filter) with
/// ADSR envelopes, exact-duration pitch glides, optional tone sweeps and vibrato; per deck a plain and a "pumped" voice
/// group (the festival's sidechain, <see cref="PumpGain"/>); equal-power pan; a light
/// Freeverb-style reverb (4 combs + 2 allpasses per channel); a peak limiter followed by a soft clipper, so every output
/// sample satisfies |x| &lt; 1. Rendering is block-based (64 frames), allocation-free in steady state, and deterministic
/// for a seed. Not thread-safe: call everything from one thread (the Godot node calls it from <c>_Process</c>).
/// </para>
/// Time is the render cursor (<see cref="FramesRendered"/>): an effect called now starts at the next frame rendered.
/// </summary>
public sealed partial class SkySynth
{
    /// <summary>The default and intended sample rate, frames per second (the Godot generator's mix rate).</summary>
    public const int DefaultSampleRateHz = 44100;

    /// <summary>Bars per music cycle: the theme (<see cref="ThemeLengthBars"/>, or one more for an augmented ending), then the world's own material.</summary>
    public const int CycleBars = 16;

    /// <summary>The written theme's length in bars (4/4).</summary>
    public const int ThemeLengthBars = 8;

    /// <summary>The size of the voice pool (music and effects together).</summary>
    public const int MaxVoices = 40;

    /// <summary>
    /// Voices the music may use at once; the rest are kept for effects. When the music's share is full, a new music note
    /// takes over the quietest music voice (never an effect's).
    /// </summary>
    public const int MaxMusicVoices = 28;

    /// <summary>Worlds remembered for a return (<see cref="SetWorld"/> resumes them rather than starting cold).</summary>
    public const int RememberedWorlds = 8;

    /// <summary>The FileScape theme as written: 8 bars of scale degrees and rhythm (see <see cref="ThemeNote"/>).</summary>
    public static IReadOnlyList<ThemeNote> ThemeMelody { get; } = Array.AsReadOnly(BuildTheme());

    /// <summary>How long the old world's music takes to fade after the new one's first downbeat (equal power).</summary>
    public static readonly TimeSpan ThemeCrossfade = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// The longest a tempo-locked effect waits for the grid: one 32nd note at the slowest tempo (the hub, 94 BPM: 79.8 ms),
    /// rounded up; within the ≈ 90 ms budget at every tempo.
    /// </summary>
    public static readonly TimeSpan QuantisedLatencyBound = TimeSpan.FromMilliseconds(80);

    /// <summary>The longest a movement effect waits before it sounds (it starts at the next rendered frame; attacks are ≤ 3 ms).</summary>
    public static readonly TimeSpan ImmediateLatencyBound = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Frames rendered per block (1.45 ms at 44.1 kHz): music is scheduled and group gains are ramped once per block, so
    /// every gain change must be slow compared with it. The reverb's delay lines are all longer than a block (see <see cref="Reverb"/>).
    /// </summary>
    private const int Block = 64;

    // Voice groups: each deck's music, the flight arpeggio and the effects mix into their own buffers so each can be faded alone.
    // Each deck has a second, "pumped" group (its index + GroupPumpOffset): the voices an arrangement sidechains to its kick
    // (the festival's pads, supersaws and bass, AD-52), so the kick and the lead are not pumped with them.
    private const int GroupCount = 6; // DeckA, DeckB, Flight, Sfx, DeckA pumped, DeckB pumped
    private const int GroupFlight = 2;
    private const int GroupSfx = 3;
    private const int GroupPumpOffset = 4;

    /// <summary>Envelope level (≈ −66 dB) below which a decaying or released voice is finished and frees its slot.</summary>
    private const float EndLevel = 0.0005f;

    /// <summary>Control-rate period in frames: envelopes, glides and FM index are computed every 16 frames and interpolated between.</summary>
    private const int ControlFrames = 16;

    /// <summary>Peak level above which the limiter pulls the output down (linear; the soft clipper above it catches the rest).</summary>
    private const float LimiterThreshold = 0.8f;
    private const float OutputGain = 1.6f; // voice gains are designed small; this sets the overall loudness

    // ---- smoothing times (T60: the time to close 99.9 % of a step, see Coef)
    /// <summary>T60 of a stolen voice's last sample as it fades through its group's residual: short, but a ramp, so a steal never clicks.</summary>
    private const float StolenVoiceResidualT60Seconds = 0.003f;

    /// <summary>T60 of the master volume and mute ramp: fast enough to feel instant, slow enough not to click.</summary>
    private const float MasterVolumeT60Seconds = 0.015f;

    /// <summary>T60 of the limiter's release after a peak (attack is instant): longer pumps less, shorter recovers loudness sooner.</summary>
    private const float LimiterReleaseT60Seconds = 0.2f;

    /// <summary>T60 of the flight layer's level following <see cref="SetFlight"/>: the arpeggio swells in rather than switching on.</summary>
    private const float FlightLayerT60Seconds = 0.35f;

    /// <summary>
    /// How long a new world's deck takes to come up to full level (linear in the fade, equal-power in gain). Short: the
    /// new world starts on a downbeat and should be heard there; the old one's longer <see cref="ThemeCrossfade"/> covers the join.
    /// </summary>
    private const float DeckFadeInSeconds = 0.15f;

    // ---- the cue duck (DuckMusic): AD-49 "Mix"
    /// <summary>Default duck attack, seconds (time constant): quick enough that the cue is clear from its start.</summary>
    private const float DefaultDuckAttackSeconds = 0.02f;

    /// <summary>The fastest duck attack, seconds (time constant): a floor so a 4 ms landing duck is still a ramp, never a step.</summary>
    private const float MinDuckAttackSeconds = 0.005f;

    /// <summary>Time constant with which the music comes back after a cue, seconds (≈ 0.25 s recovery, AD-49).</summary>
    private const float DuckReleaseSeconds = 0.25f;

    /// <summary>The flight wind releases itself when <see cref="Fly"/> has not been called for this long, seconds (a dropped flight never drones on).</summary>
    private const float FlyTimeoutSeconds = 0.3f;

    /// <summary>Time constant of the music's return at a festival drop, seconds: much quicker than <see cref="DuckReleaseSeconds"/>, so the drop slams in.</summary>
    private const float DropDuckReleaseSeconds = 0.03f;

    /// <summary>The downward touchdown speed that gives the loudest, lowest landing, m/s (≈ a big drop); harder landings sound the same.</summary>
    private const float HardestLandingMetersPerSecond = 12f;

    private readonly int _rate;
    private readonly float _invRate;
    private readonly int _seed;
    private readonly Voice[] _voices = new Voice[MaxVoices];
    private readonly List<(long Frame, NoteSpec Note)> _pending = new(128);
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
    private readonly float _fadeOutStepPerFrame;
    private readonly float _fadeInStepPerFrame;

    private long _frame;
    private int _current; // index of the deck that plays the current world (or will, from its first downbeat)
    private int _generation;
    private int _switches;
    private long _lastSwitchFrame = long.MinValue / 2;
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
    private long _lastFlyFrame = long.MinValue / 2;
    private float _flyProgress = 1f;
    private bool _stepLeft;
    private float _duck;
    private float _duckDepth;
    private long _duckUntil;
    private float _duckAttackSeconds = DefaultDuckAttackSeconds;
    private float _duckReleaseSeconds = DuckReleaseSeconds;

    /// <summary>Creates a synth playing <paramref name="theme"/>'s music from the first frame.</summary>
    /// <param name="seed">Chooses the melodies and noise; the same seed and calls give the same samples.</param>
    /// <param name="theme">The theme to start in (the key's root and, for folders and the hub, its mode).</param>
    /// <param name="sampleRateHz">Frames per second (default 44 100).</param>
    /// <param name="character">The world to start in (default: the hub, the calm arrival).</param>
    public SkySynth(int seed = 1, PlanetTheme theme = PlanetTheme.Hearth, int sampleRateHz = DefaultSampleRateHz, WorldCharacter? character = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRateHz, 8000);
        _rate = sampleRateHz;
        _invRate = 1f / sampleRateHz;
        _seed = seed;
        // Knuth's multiplicative hash spreads neighbouring seeds; | 1 keeps the generator state odd, hence never zero
        // (an xorshift voice seeded with 0 would stay silent forever).
        _noiseSeed = (uint)seed * 2654435761u | 1u;
        for (var i = 0; i < MaxVoices; i++) _voices[i] = new Voice();
        for (var g = 0; g < GroupCount; g++)
        {
            _gL[g] = new float[Block];
            _gR[g] = new float[Block];
            _gS[g] = new float[Block];
        }

        // Per-frame coefficients: Coef is the decay multiplier per frame; 1 − Coef is the fraction of the gap closed per frame.
        _residualK = Coef(StolenVoiceResidualT60Seconds);
        _masterK = 1f - Coef(MasterVolumeT60Seconds);
        _limiterReleaseK = 1f - Coef(LimiterReleaseT60Seconds);
        _flightK = 1f - Coef(FlightLayerT60Seconds);
        _fadeOutStepPerFrame = 1f / (ThemeCrossfade.ToSecondsF() * sampleRateHz);
        _fadeInStepPerFrame = 1f / (DeckFadeInSeconds * sampleRateHz);
        _reverb = new Reverb(sampleRateHz);
        var start = character ?? HubCharacter;
        _decks[0] = new Deck(0) { Fade = 1, FadeTarget = 1 };
        _decks[1] = new Deck(1);
        Configure(_decks[0], theme, start, IdentityOf(start), visits: 1, origin: 0, quickHop: false);
        Configure(_decks[1], theme, start, IdentityOf(start), visits: 1, origin: 0, quickHop: false);
        _decks[1].Live = false;
        _masterGain = _masterVolume;
    }

    /// <summary>Frames per second of the output.</summary>
    public int SampleRateHz => _rate;

    /// <summary>The render cursor: frames rendered so far (the time base of every call).</summary>
    public long FramesRendered => _frame;

    /// <summary>The theme of the current world (playing, or starting on its first downbeat).</summary>
    public PlanetTheme Theme => _decks[_current].Theme;

    /// <summary>The current world's character (the last <see cref="SetWorld"/>).</summary>
    public WorldCharacter Character => _decks[_current].Character;

    /// <summary>The current world's musical identity.</summary>
    public MusicIdentity Identity => _decks[_current].Identity;

    /// <summary>How far the current world has developed: 1 on a first visit, 2 on the second, 3 from the third.</summary>
    public int Development => _decks[_current].Development;

    /// <summary>The current world's key (that of <see cref="Theme"/> and <see cref="Character"/>).</summary>
    public MusicalKey Key => ToKey(_decks[_current].Profile);

    /// <summary>The tempo of the music playing at the render cursor, beats (quarter notes) per minute.</summary>
    public float TempoBeatsPerMinute => ClockAt(_frame).Style.Bpm;

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

    /// <summary>Music voices sounding now (at most <see cref="MaxMusicVoices"/>).</summary>
    public int ActiveMusicVoices
    {
        get
        {
            var n = 0;
            foreach (var v in _voices) if (v.Active && v.Group != GroupSfx) n++;
            return n;
        }
    }

    /// <summary>Music level relative to its designed (moderate) level, 0..1 (default 1). 0 leaves only the effects.</summary>
    public float MusicVolume { get; set; } = 1f;

    /// <summary>The variation of the current theme cycle (diagnostics and probes).</summary>
    public ThemeVariation Variation => _decks[_current].Variation;

    /// <summary>Called (on the render thread, inside <see cref="Render(Span{float})"/>) for every melody note as it is scheduled; null by default.</summary>
    public Action<MelodyNote>? MelodyObserver { get; set; }

    /// <summary>The key of a theme as a folder of band 1 plays it (the theme's own root and mode).</summary>
    public static MusicalKey KeyFor(PlanetTheme theme) => ToKey(Profiles[(int)theme]);

    /// <summary>The key of a world: the theme's root, and its mode unless the identity has its own (roots, repositories, deep bands).</summary>
    public static MusicalKey KeyFor(PlanetTheme theme, WorldCharacter character) => ToKey(KeyProfiles[(int)IdentityOf(character)][(int)theme]);

    private static MusicalKey ToKey(Profile p) => new(p.Root, p.Mode, Array.AsReadOnly(p.Scale.ToArray()));

    /// <summary>
    /// Changes the theme (key) and keeps the current world's character and development: <see cref="SetWorld"/> with them
    /// (no-op if the theme is already playing).
    /// </summary>
    public void SetTheme(PlanetTheme theme)
    {
        var d = _decks[_current];
        if (theme == d.Theme) return;
        SetWorld(theme, d.Character, d.Visits);
    }

    /// <summary>Flight intensity 0..1: above zero, a rising arpeggio layer joins the music (smoothed, so it can be set every frame).</summary>
    public void SetFlight(float intensity01) => _flightTarget = Clamp01(intensity01);

    /// <summary>Overall output level 0..1 (linear amplitude; default 0.8), smoothed over ≈ 15 ms.</summary>
    public void SetMasterVolume(float volume01) => _masterVolume = Clamp01(volume01);

    /// <summary>Silences (or restores) the output with a short ramp; music time keeps running.</summary>
    public void Mute(bool muted) => _muted = muted;

    /// <summary>
    /// A duration near <paramref name="duration"/> (within <paramref name="maxShift"/>) that ends on the sixteenth-note
    /// grid of the music that will be playing then (the current world's own clock once its first downbeat has come; the pulse's
    /// sixteenths, which in a half-time arrangement are its theme's thirty-seconds), so a
    /// launch released after it lands on the beat at any tempo. Measured from the render cursor (synth time, the time the
    /// release sound is rendered at). Returns <paramref name="duration"/> unchanged when no grid point is within the shift.
    /// A half sixteenth is at most 80 ms (94 BPM), so a shift of ≥ 0.08 s always lands.
    /// </summary>
    public TimeSpan AlignToGrid(TimeSpan duration, TimeSpan maxShift)
    {
        var frames = duration.TotalSeconds * _rate;
        if (!double.IsFinite(frames) || frames < 0) return duration;
        var end = _frame + frames;
        var d = ClockAt((long)Math.Round(end));
        var fps = d.GridFramesPerSixteenth;
        var k = Math.Round((end - d.Origin) / fps);
        var grid = d.Origin + Math.Ceiling(k * fps - 1e-6);
        if (grid <= _frame) grid = d.Origin + Math.Ceiling((k + 1) * fps - 1e-6);
        var shift = grid - end;
        if (Math.Abs(shift) > maxShift.TotalSeconds * _rate) return duration;
        return TimeSpan.FromTicks((long)Math.Round((grid - _frame) * TimeSpan.TicksPerSecond / (double)_rate));
    }

    /// <summary>
    /// Lowers the music by <paramref name="depth"/> (0..1 of its level) for <paramref name="seconds"/>, so a cue stands clear of the drums.
    /// Overlapping ducks combine to the deepest depth, the latest end and the fastest attack, so a second cue never
    /// lets the music back up early. The ramp itself runs in <see cref="RenderBlock"/>.
    /// </summary>
    private void DuckMusic(float depth, float seconds, float attackSeconds = DefaultDuckAttackSeconds)
    {
        var active = _frame < _duckUntil;
        _duckDepth = active ? MathF.Max(_duckDepth, depth) : depth;
        _duckUntil = Math.Max(_duckUntil, _frame + Frames(seconds));
        _duckAttackSeconds = active ? MathF.Min(_duckAttackSeconds, attackSeconds) : attackSeconds;
        _duckReleaseSeconds = DuckReleaseSeconds;
    }

    // ---------------------------------------------------------------- movement effects (immediate)

    /// <summary>A footstep: a soft filtered-noise tap with a faint in-key pluck. <paramref name="intensity"/> 0..1 (walk ≈ 0.5, run ≈ 1).</summary>
    public void Step(float intensity)
    {
        var k = Clamp01(intensity);
        if (k <= 0) return;
        _stepLeft = !_stepLeft; // feet alternate: a little left, a little right, and a different note for each
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
        var k = Clamp01(impactMetersPerSecond / HardestLandingMetersPerSecond);
        if (!float.IsFinite(k)) return;
        var now = _frame;
        DuckMusic(0.55f + 0.2f * k, 0.3f, attackSeconds: 0.004f);
        var root = PitchHz(0, -1);
        Enqueue(now, NoteSpec.Tone(Wave.Sine, root * 2f) with { GlideToHz = root * 0.75f, Glide = 0.09f, Attack = 0.001f, Decay = 0.2f, Gain = 0.10f + 0.22f * k, Send = 0.06f });
        Enqueue(now, NoteSpec.Noise(700, 350, 0.08f, q: 0.8f, lowpass: true) with { Attack = 0.001f, Decay = 0.09f, Gain = 0.04f + 0.09f * k, Send = 0.05f });
    }

    /// <summary>A launcher wind-up: a rising shimmer that swells for exactly <paramref name="duration"/> and peaks at launch.</summary>
    public void WindUp(TimeSpan duration)
    {
        var t = Math.Clamp(duration.ToSecondsF(), 0.05f, 5f);
        var now = _frame;
        DuckMusic(0.45f, t + 0.1f, attackSeconds: t * 0.5f); // the music leans back as the wind-up swells
        Enqueue(now, NoteSpec.Noise(500, 4200, t, q: 2.2f) with { Attack = t, Decay = 0.12f, Gain = 0.09f, Send = 0.25f });
        Enqueue(now, NoteSpec.Tone(Wave.Triangle, PitchHz(0, 1)) with { GlideToHz = PitchHz(0, 2), Glide = t, Attack = t, Decay = 0.15f, Gain = 0.055f, ToneHz = 3200, Pan = -0.15f, Send = 0.3f });
        Enqueue(now, NoteSpec.Tone(Wave.Sine, PitchHz(4, 1)) with { GlideToHz = PitchHz(4, 2), Glide = t, Attack = t, Decay = 0.15f, Gain = 0.04f, Pan = 0.15f, Send = 0.3f });
    }

    /// <summary>The launch: a whoosh with an upward harp-like glissando through the scale.</summary>
    public void Launch()
    {
        var now = _frame;
        DuckMusic(0.6f, 0.6f, attackSeconds: 0.004f);
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
        _flyProgress = p;
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
            // One long-held noise voice, steered every frame: level and cutoff glide to each new target (T60 60 ms and
            // 80 ms) so per-frame calls sound continuous rather than stepped.
            v.LevelK = 1f - Coef(0.06f);
            _flyVoice = v;
            _flyGeneration = v.Generation;
        }

        v.LevelTarget = level;
        v.SetCutoffTarget(cutoff, 1f - Coef(0.08f));
    }

    // ---------------------------------------------------------------- hazard cues (AD-52)

    /// <summary>Nearness (0..1) below which <see cref="HazardWarning"/> is silent: the edge is not yet a concern.</summary>
    private const float WarningFromNearness = 0.05f;

    /// <summary>Seconds between warning pulses just past <see cref="WarningFromNearness"/>: a slow, patient reminder.</summary>
    private const float WarningSlowestIntervalSeconds = 1.0f;

    /// <summary>Seconds between warning pulses at the very edge (nearness 1): urgent, but never a buzz.</summary>
    private const float WarningFastestIntervalSeconds = 0.25f;

    /// <summary>How deep the music ducks at the edge (nearness 1), 0..1 (≈ −7 dB): the warning is heard over any world, the music does not stop.</summary>
    private const float WarningDuckDepth = 0.6f;

    /// <summary>How deep the music ducks while falling, 0..1 (≈ −14 dB): the fall is the event; the music waits under it.</summary>
    private const float FallDuckDepth = 0.8f;

    /// <summary>The warning rumble and the fall's voices release when their method has not been called for this long, seconds.</summary>
    private const float SteeredCueTimeoutSeconds = 0.3f;

    /// <summary>The fall's pitch: it starts this many semitones above the key's root …</summary>
    private const float FallFromSemitones = 24f;

    /// <summary>… and falls this far by the end (three octaves: from two above the root to one below).</summary>
    private const float FallSpanSemitones = 36f;

    /// <summary>The spiral's rotation (the fall's two voices trading places left and right): turns per second at the start, and added by the end.</summary>
    private const float FallSpinStartHz = 0.6f, FallSpinAddedHz = 4.4f;

    private Voice? _warnVoice, _fallNoise, _fallLeft, _fallRight;
    private int _warnGeneration, _fallNoiseGeneration, _fallLeftGeneration, _fallRightGeneration;
    private long _lastWarnFrame = long.MinValue / 2, _nextWarningFrame = long.MinValue / 2, _lastFallFrame = long.MinValue / 2;
    private float _fallSpin;

    /// <summary>Warning pulses scheduled so far (diagnostics: the throttle's test).</summary>
    public int WarningPulses { get; private set; }

    /// <summary>
    /// The edge warning, for a hazard world: call it every frame while the player is near an edge, with
    /// <paramref name="nearness01"/> 0 (far) … 1 (at the edge). A low rumble follows the nearness, and a falling tritone pulse
    /// (a warning, deliberately outside the key) repeats faster the nearer you are (every 1 s at the threshold, 0.25 s at
    /// the edge), on the music's 32nd grid; the music ducks a little with it. Self-throttling and O(1) per call: calling
    /// it every frame never stacks pulses, and when the calls stop (or nearness falls to ≈ 0) it falls silent within 0.3 s.
    /// </summary>
    public void HazardWarning(float nearness01)
    {
        var k = Clamp01(nearness01);
        if (k < WarningFromNearness)
        {
            ReleaseSteered(ref _warnVoice, _warnGeneration);
            return;
        }

        _lastWarnFrame = _frame;
        DuckMusic(WarningDuckDepth * k, 0.2f, attackSeconds: 0.03f);
        var level = 0.04f + 0.08f * k * k;
        var cutoff = 90f + 260f * k;
        var v = Steered(_warnVoice, _warnGeneration);
        if (v == null)
        {
            v = StartVoice(NoteSpec.Noise(cutoff, cutoff, 0, q: 1.1f, lowpass: true) with
            {
                Attack = 0.15f, Sustain = 1f, Decay = 0.1f, Hold = 3600f, Release = 0.4f, Gain = level, Send = 0.3f, Group = GroupSfx,
            }, 0);
            if (v != null)
            {
                v.LevelK = 1f - Coef(0.12f);
                _warnVoice = v;
                _warnGeneration = v.Generation;
            }
        }

        if (v != null)
        {
            v.LevelTarget = level;
            v.SetCutoffTarget(cutoff, 1f - Coef(0.12f));
        }

        if (_frame < _nextWarningFrame) return;
        var at = NextGridFrame();
        var root = ClockAt(_frame).Profile.Root;
        // The falling tritone, in two registers: "careful".
        Enqueue(at, NoteSpec.Bell(MidiHz(root + 30), ratio: 2f, index: 1.3f, indexDecay: 0.1f) with { Attack = 0.002f, Decay = 0.35f, Gain = 0.06f + 0.05f * k, Pan = -0.1f, Send = 0.25f });
        Enqueue(at, NoteSpec.Tone(Wave.Triangle, MidiHz(root + 18)) with { Attack = 0.004f, Decay = 0.3f, Gain = 0.07f + 0.05f * k, ToneHz = 1600, Send = 0.1f });
        var second = at + GridStepFrames(at) * 2;
        Enqueue(second, NoteSpec.Bell(MidiHz(root + 24), ratio: 2f, index: 1.3f, indexDecay: 0.1f) with { Attack = 0.002f, Decay = 0.45f, Gain = 0.055f + 0.045f * k, Pan = 0.1f, Send = 0.3f });
        Enqueue(second, NoteSpec.Tone(Wave.Triangle, MidiHz(root + 12)) with { Attack = 0.004f, Decay = 0.35f, Gain = 0.065f + 0.045f * k, ToneHz = 1400, Send = 0.1f });
        var interval = WarningSlowestIntervalSeconds + (WarningFastestIntervalSeconds - WarningSlowestIntervalSeconds) * k;
        _nextWarningFrame = at + Frames(interval);
        WarningPulses++;
    }

    /// <summary>
    /// The start of a fall (the ground gave way at an edge): a crack, falling glass shards and a dropping thud, and the music
    /// ducks deep (≈ −14 dB) for the fall. Immediate. Then call <see cref="Fall"/> every frame, and <see cref="FallReturn"/>
    /// when the player reappears.
    /// </summary>
    public void FallStart()
    {
        var now = _frame;
        _fallSpin = 0;
        _lastFallFrame = now;
        ReleaseSteered(ref _warnVoice, _warnGeneration);
        _nextWarningFrame = now + Frames(1f); // no warning pulse on top of the fall
        DuckMusic(FallDuckDepth, 0.6f, attackSeconds: 0.01f);
        var root = ClockAt(now).Profile.Root;
        Enqueue(now, NoteSpec.Noise(5200, 1400, 0.18f, q: 1.4f) with { Attack = 0.001f, Decay = 0.25f, Gain = 0.12f, Send = 0.3f });
        // Shards: inharmonic, out of the key (the shell is broken), scattered across the field.
        ReadOnlySpan<(int Semitones, float Delay, float Pan)> shards = [(42, 0f, -0.5f), (37, 0.025f, 0.45f), (46, 0.055f, -0.1f)];
        foreach (var (semis, delay, pan) in shards)
        {
            Enqueue(now + Frames(delay), NoteSpec.Bell(MidiHz(root + semis), ratio: 2.76f, index: 1.6f, indexDecay: 0.08f) with { Attack = 0.001f, Decay = 0.5f, Gain = 0.045f, Pan = pan, Send = 0.45f });
        }

        Enqueue(now, NoteSpec.Tone(Wave.Sine, MidiHz(root)) with { GlideToHz = MidiHz(root - 24), Glide = 0.45f, Attack = 0.002f, Decay = 0.7f, Gain = 0.13f, Send = 0.15f });
    }

    /// <summary>
    /// The fall (or the spiral into the black hole), following <paramref name="progress01"/> 0 (the start) … 1 (the end):
    /// a whoosh whose pitch falls three octaves in the key (two above the root to one below, faster towards the end), its
    /// two voices circling left and right ever faster, over wind that darkens. Call it every frame while falling; it keeps
    /// the music ducked, and fades by itself at 1 or ≈ 0.3 s after the last call.
    /// </summary>
    public void Fall(float progress01)
    {
        var p = Clamp01(progress01);
        var dt = Math.Clamp((_frame - _lastFallFrame) * _invRate, 0f, 0.1f);
        _lastFallFrame = _frame;
        if (p >= 1f)
        {
            ReleaseFall();
            return;
        }

        DuckMusic(FallDuckDepth, 0.3f, attackSeconds: 0.03f);
        var root = ClockAt(_frame).Profile.Root;
        var semis = FallFromSemitones - FallSpanSemitones * MathF.Pow(p, 1.3f);
        var hz = 440f * MathF.Pow(2f, (root + semis - 69) / 12f);
        _fallSpin += dt * (FallSpinStartHz + FallSpinAddedHz * p);
        _fallSpin -= MathF.Floor(_fallSpin);
        var c = MathF.Cos(2f * MathF.PI * _fallSpin);
        var level = 0.03f + 0.03f * p;
        var wind = 0.04f + 0.04f * MathF.Sin(MathF.PI * p);
        var cutoff = 160f + 4000f * MathF.Pow(1f - p, 1.6f);
        var left = SteerFallVoice(ref _fallLeft, ref _fallLeftGeneration, NoteSpec.Saw(hz, 2, 18f, 1600f) with { Pan = -0.85f, Send = 0.4f }, level * (0.55f + 0.45f * c));
        left?.SetFreqTarget(hz, 1f - Coef(0.08f));
        var right = SteerFallVoice(ref _fallRight, ref _fallRightGeneration, NoteSpec.Saw(hz * 1.004f, 2, 18f, 1600f) with { Pan = 0.85f, Send = 0.4f }, level * (0.55f - 0.45f * c));
        right?.SetFreqTarget(hz * 1.004f, 1f - Coef(0.08f));
        var noise = SteerFallVoice(ref _fallNoise, ref _fallNoiseGeneration, NoteSpec.Noise(cutoff, cutoff, 0, q: 0.9f) with { Send = 0.35f }, wind);
        noise?.SetCutoffTarget(cutoff, 1f - Coef(0.08f));
    }

    /// <summary>
    /// Back from a fall: the player reappears at the parent world. The fall's voices stop, an upward pop and a bright
    /// tonic chord in the key of the world arrived at sound at once, and the music comes back up: a short duck (0.7 for 0.4 s,
    /// room for the pop) replaces the fall's deep one. Immediate; <see cref="Arrive"/> may follow as usual.
    /// </summary>
    public void FallReturn()
    {
        var now = _frame;
        ReleaseFall();
        _lastFallFrame = long.MinValue / 2;
        _duckDepth = 0.7f;
        _duckUntil = now + Frames(0.4f);
        _duckReleaseSeconds = DuckReleaseSeconds;
        var arriving = _decks[_current].Profile;
        var len = arriving.Scale.Length;
        Enqueue(now, NoteSpec.Tone(Wave.Sine, PitchHz(arriving, 0, -1)) with { GlideToHz = PitchHz(arriving, len == 5 ? 3 : 4, 1), Glide = 0.12f, Attack = 0.002f, Decay = 0.3f, Gain = 0.12f, Send = 0.2f });
        Enqueue(now, NoteSpec.Noise(300, 6000, 0.2f, q: 1.2f) with { Attack = 0.12f, Decay = 0.15f, Gain = 0.07f, Send = 0.3f });
        // The tonic, its fifth and the octave in any scale (the fifth is degree 4 of seven, 3 of a pentatonic).
        ReadOnlySpan<(int Degree, float Pan)> chord = [(0, -0.3f), (len == 5 ? 3 : 4, 0.3f), (len, 0f)];
        foreach (var (degree, pan) in chord)
        {
            Enqueue(now + Frames(0.08f), NoteSpec.Bell(PitchHz(arriving, degree, 1), ratio: 2f, index: 1.1f, indexDecay: 0.25f) with { Attack = 0.003f, Decay = 1.1f, Gain = 0.07f, Pan = pan, Send = 0.45f });
        }
    }

    /// <summary>A steered cue voice if it is still the one we started (not stolen, not released), else null.</summary>
    private static Voice? Steered(Voice? v, int generation) => v != null && v.Active && v.Generation == generation && !v.Released ? v : null;

    /// <summary>Releases a steered cue voice (if it is still ours) and forgets it.</summary>
    private static void ReleaseSteered(ref Voice? v, int generation)
    {
        Steered(v, generation)?.Release();
        v = null;
    }

    private void ReleaseFall()
    {
        ReleaseSteered(ref _fallLeft, _fallLeftGeneration);
        ReleaseSteered(ref _fallRight, _fallRightGeneration);
        ReleaseSteered(ref _fallNoise, _fallNoiseGeneration);
    }

    /// <summary>Starts (held, its level smoothed over T60 60 ms) or steers one of the fall's voices to <paramref name="level"/>.</summary>
    private Voice? SteerFallVoice(ref Voice? slot, ref int generation, NoteSpec spec, float level)
    {
        var v = Steered(slot, generation);
        if (v == null)
        {
            v = StartVoice(spec with { Attack = 0.06f, Sustain = 1f, Decay = 0.1f, Hold = 3600f, Release = 0.25f, Gain = level, Group = GroupSfx }, 0);
            if (v == null) return null;
            v.LevelK = 1f - Coef(0.06f);
            slot = v;
            generation = v.Generation;
        }

        v.LevelTarget = level;
        return v;
    }

    /// <summary>The warning rumble and the fall's voices release themselves when their methods stop being called (a dropped cue never drones on).</summary>
    private void ReleaseStaleHazardVoices()
    {
        var timeout = (long)(SteeredCueTimeoutSeconds * _rate);
        if (_warnVoice != null && _frame - _lastWarnFrame > timeout) ReleaseSteered(ref _warnVoice, _warnGeneration);
        if ((_fallLeft != null || _fallRight != null || _fallNoise != null) && _frame - _lastFallFrame > timeout) ReleaseFall();
    }

    // ---------------------------------------------------------------- interface effects (tempo-locked)

    /// <summary>
    /// Arrival: a warm chord, then a second, softer chime ≈ 120 ms later. On the playing music's grid, but in the key of the
    /// world being arrived at (after <see cref="SetWorld"/>, the old world plays on, ducked, for at most a beat).
    /// </summary>
    public void Arrive()
    {
        var at = NextGridFrame();
        var drop = ArrivalDrop();
        DuckMusic(0.35f, 0.45f);
        if (drop >= 0)
        {
            // A festival build is being released (AD-52): the cue duck (this one and the landing's) ends at the drop and lets
            // the music straight back in, so the drop lands at full level instead of swelling up out of the duck.
            _duckUntil = Math.Min(_duckUntil, drop);
            _duckReleaseSeconds = DropDuckReleaseSeconds;
        }

        var arriving = _decks[_current].Profile;
        int[] chord = [0, 2, 4];
        for (var i = 0; i < chord.Length; i++)
        {
            Enqueue(at, NoteSpec.Tone(Wave.Triangle, PitchHz(arriving, chord[i], 0)) with
            {
                Attack = 0.012f, Decay = 1.6f, Gain = 0.075f, ToneHz = 1900, Pan = -0.35f + 0.35f * i, Send = 0.4f,
            });
        }

        Enqueue(at, NoteSpec.Tone(Wave.Sine, PitchHz(arriving, 0, -1)) with { Attack = 0.012f, Decay = 1.2f, Gain = 0.07f, Send = 0.2f });
        Enqueue(at + Frames(0.12f), NoteSpec.Bell(PitchHz(arriving, arriving.Scale.Length, 1), ratio: 3.5f, index: 1.4f, indexDecay: 0.3f) with
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
        Enqueue(at + GridStepFrames(at), NoteSpec.Bell(PitchHz(4, 1), ratio: 2f, index: 1f, indexDecay: 0.12f) with { Attack = 0.002f, Decay = 0.6f, Gain = 0.06f, Pan = 0.15f, Send = 0.3f });
    }

    /// <summary>Opening: a rising three-note figure over a soft whoosh.</summary>
    public void Open()
    {
        var at = NextGridFrame();
        var step = GridStepFrames(at);
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
        Enqueue(at + GridStepFrames(at), NoteSpec.Tone(Wave.SoftSquare, PitchHz(0, 0)) with { Attack = 0.004f, Decay = 0.3f, Gain = 0.07f, ToneHz = 800, Send = 0.12f });
    }

    /// <summary>A bell-flower note (e.g. walking past an audio file): scale degree <paramref name="index"/> (wraps through two octaves).</summary>
    public void Chime(int index)
    {
        var len = CurrentScale.Length;
        var degree = ((index % (2 * len)) + 2 * len) % (2 * len); // a true modulo, so negative indices wrap too
        // A scattered but repeatable place: one of 7 pan positions −0.5…+0.5 (37 is coprime with 7, so neighbouring
        // indices land in different positions).
        var pan = (((index * 37) % 7 + 7) % 7 - 3) / 6f;
        Enqueue(NextGridFrame(), NoteSpec.Bell(PitchHz(degree, 1), ratio: 1.4f, index: 2f, indexDecay: 0.4f) with { Attack = 0.003f, Decay = 1.8f, Gain = 0.06f, Pan = pan, Send = 0.45f });
    }

    // ---------------------------------------------------------------- the bell tower (PLANET-56)

    /// <summary>The bell tower's partials as (scale degree, octave, gain, decay seconds) of its strike: a hum an octave below
    /// the tonic, the tonic, the third and fifth of the scale above it, and the octave (a church bell's hum, prime, tierce,
    /// quint and nominal, in the world's own scale). Gains and decays by ear, 2026-11-17.</summary>
    private static readonly (int Degree, int Octave, float Gain, float Decay)[] BellPartials =
    [
        (0, -1, 0.075f, 5.5f), (0, 0, 0.085f, 4.5f), (2, 0, 0.035f, 2.6f), (4, 0, 0.03f, 2.2f), (0, 1, 0.045f, 1.6f),
    ];

    /// <summary>The answering strike (the bell swinging back) is this much quieter than the first.</summary>
    private const float BellAnswerGain = 0.55f;

    /// <summary>The music ducks this deep (fraction kept) for this long under the toll, seconds: the bell is the event.</summary>
    private const float BellDuckDepth = 0.5f, BellDuckSeconds = 1.4f;

    /// <summary>The last toll's first strike (frame) and every note it struck (MIDI), for tests; (−1, empty) before any.</summary>
    public (long Frame, IReadOnlyList<int> MidiNotes) LastBellToll { get; private set; } = (-1, []);

    /// <summary>
    /// The bell tower rung (PLANET-56, a jump and twirl under the bell): a big bell's strike on the music's next 32nd note,
    /// in the world's key, and its answer a beat later a fifth up as the bell swings back — then both ring on for seconds
    /// while the music ducks a little. Each partial is an FM bell (inharmonic, so it sounds struck, not played) with a
    /// short bright clang at the strike. Call it once per ring; a ring while the last still sounds simply strikes again.
    /// </summary>
    public void BellToll()
    {
        var at = NextGridFrame();
        var beat = GridStepFrames(at) * 8; // a beat: eight 32nd notes
        DuckMusic(BellDuckDepth, BellDuckSeconds);
        var profile = ClockAt(at).Profile;
        var notes = new List<int>();
        void Strike(long frame, int shift, float level, float pan)
        {
            foreach (var (degree, octave, gain, decay) in BellPartials)
            {
                var midi = profile.Root + 12 * octave + ScaleNote(profile.Scale, degree + shift);
                notes.Add(midi);
                Enqueue(frame, NoteSpec.Bell(MidiHz(midi), ratio: 1.41f, index: 1.4f, indexDecay: 0.5f) with
                {
                    Attack = 0.002f, Decay = decay, Gain = gain * level, Pan = pan, Send = 0.55f, ToneHz = 6000,
                });
            }

            // The clang: a short bright burst at the strike.
            Enqueue(frame, NoteSpec.Noise(2500, 5000, 0.04f, q: 2.5f) with { Attack = 0.0005f, Decay = 0.06f, Gain = 0.05f * level, Pan = pan, Send = 0.3f });
        }

        Strike(at, 0, 1f, -0.1f);
        Strike(at + beat, 4, BellAnswerGain, 0.15f);
        LastBellToll = (at, notes);
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

    /// <summary>
    /// Renders one block of at most <see cref="Block"/> frames: schedules the music due in it, starts due notes, renders every
    /// voice into its group, applies the group gains (deck crossfades, the cue duck, the flight layer), the reverb, the
    /// master volume, the limiter and the soft clipper, then advances the render cursor.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void RenderBlock(Span<float> output, int n)
    {
        var blockEnd = _frame + n;
        ScheduleMusic(blockEnd);
        StartDueNotes(blockEnd);
        if (_flyVoice != null && _frame - _lastFlyFrame > (long)(FlyTimeoutSeconds * _rate))
        {
            if (_flyVoice.Active && _flyVoice.Generation == _flyGeneration) _flyVoice.Release();
            _flyVoice = null;
        }

        ReleaseStaleHazardVoices();

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
        // The cue duck: towards its depth while a cue sounds, back up over ≈ 0.25 s after it.
        var music0 = Clamp01(MusicVolume) * (1f - _duck);
        var duckTarget = _frame < _duckUntil ? _duckDepth : 0f;
        var duckTau = duckTarget > _duck ? MathF.Max(MinDuckAttackSeconds, _duckAttackSeconds) : _duckReleaseSeconds;
        // Exponential smoothing over the block's n frames with time constant duckTau (closes 63 % per tau, whatever the block size).
        _duck += (duckTarget - _duck) * (1f - MathF.Exp(-n / (duckTau * _rate)));
        var music1 = Clamp01(MusicVolume) * (1f - _duck);
        // Each deck's fade moves linearly (in over DeckFadeInSeconds, out over ThemeCrossfade); its gain follows the
        // equal-power law sin(fade · π/2), so a fade sounds even rather than dipping in the middle.
        for (var d = 0; d < 2; d++)
        {
            var deck = _decks[d];
            g0[d] = MathF.Sin(deck.Fade * MathF.PI * 0.5f) * music0 * deck.Level;
            var step = deck.FadeTarget > deck.Fade ? _fadeInStepPerFrame : _fadeOutStepPerFrame;
            deck.Fade = MoveTowards(deck.Fade, deck.FadeTarget, step * n);
            g1[d] = MathF.Sin(deck.Fade * MathF.PI * 0.5f) * music1 * deck.Level;
            // The deck's pumped voices follow its sidechain (1 when the arrangement does not pump).
            g0[d + GroupPumpOffset] = g0[d] * PumpGain(deck, _frame);
            g1[d + GroupPumpOffset] = g1[d] * PumpGain(deck, blockEnd);
        }

        g0[GroupFlight] = _flight * music0;
        _flight += (_flightTarget - _flight) * (1f - MathF.Pow(1f - _flightK, n)); // n frames of the per-frame smoothing at once
        g1[GroupFlight] = _flight * music1;
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

            // Snap an inaudible residual (−140 dB) to zero, so the per-sample residual path switches off again.
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
                // A defensive mute: a NaN or infinity never reaches the speakers, and the reverb (which would recirculate it) is cleared below.
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
        float lp2 = v.ToneState2, toneHz = v.ToneHz, vibPhase = v.VibPhase;
        var toneRemaining = v.ToneRemaining;
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

            if (v.FreqK > 0)
            {
                // Steered pitch (SetFreqTarget): exponential smoothing in log frequency, so a fall sounds even across octaves.
                freq *= MathF.Pow(v.FreqTarget / freq, full ? v.FreqKControl : 1f - MathF.Pow(1f - v.FreqK, m));
            }

            if (toneRemaining > 0)
            {
                // A tone sweep: the filter's cutoff moves by a constant ratio per frame, its coefficient recomputed per control period.
                var s = Math.Min(m, toneRemaining);
                toneHz *= s == ControlFrames ? v.ToneRatioControl : MathF.Pow(v.ToneRatio, s);
                toneRemaining -= s;
                lpA = 1f - MathF.Exp(-2f * MathF.PI * toneHz * invRate);
            }

            var vib = 1f;
            if (v.VibDepth > 0)
            {
                vibPhase += v.VibInc * m;
                if (vibPhase >= 1f) vibPhase -= (int)vibPhase;
                vib = 1f + v.VibDepth * MathF.Sin(2f * MathF.PI * vibPhase);
            }

            var levelEnd = v.LevelK != 0 ? level + (v.LevelTarget - level) * (1f - MathF.Pow(1f - v.LevelK, m)) : level;
            var amp = env * level;
            var dAmp = (envEnd * levelEnd - amp) / m;
            var inc = freq * vib * invRate;
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

                case Wave.Saw:
                {
                    // Up to three detuned polyBLEP saws (the third weighted 0 for a pair), scaled by 1/√copies, through two
                    // one-pole low-passes in series (12 dB per octave: a sweep of it is a recognisable filter opening).
                    float p0 = v.U0, p1 = v.U1, p2 = v.U2;
                    float i0 = inc * v.D0, i1 = inc * v.D1, i2 = inc * v.D2;
                    float w2 = v.W2, norm = v.UnisonNorm;
                    var single = v.D1 == 1f && w2 == 0f;
                    for (; i < end; i++)
                    {
                        var x = BlepSaw(p0, i0);
                        p0 += i0;
                        if (p0 >= 1f) p0 -= 1f;
                        if (!single)
                        {
                            x += BlepSaw(p1, i1) + w2 * BlepSaw(p2, i2);
                            p1 += i1;
                            if (p1 >= 1f) p1 -= 1f;
                            p2 += i2;
                            if (p2 >= 1f) p2 -= 1f;
                        }

                        lp += lpA * (x * norm - lp);
                        lp2 += lpA * (lp - lp2);
                        mono[i] = lp2 * amp;
                        amp += dAmp;
                    }

                    v.U0 = p0;
                    v.U1 = p1;
                    v.U2 = p2;
                    break;
                }

                default:
                    // White noise (xorshift) through a topology-preserving state-variable filter.
                    for (; i < end; i++)
                    {
                        noise ^= noise << 13;
                        noise ^= noise >> 17;
                        noise ^= noise << 5;
                        var w = (int)noise * (1f / 2147483648f); // the state as a signed int, scaled by 2^-31 to −1…1
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
        v.ToneA = lpA;
        v.ToneState2 = lp2;
        v.ToneHz = toneHz;
        v.ToneRemaining = toneRemaining;
        v.VibPhase = vibPhase;
        if (done) v.Active = false;
    }

    /// <summary>
    /// A naive sawtooth (−1…1 over a phase in cycles) with the polyBLEP correction around its reset: the two-sample
    /// polynomial step residual that removes most of the aliasing of the jump, cheaply (<paramref name="dt"/> is the phase increment).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float BlepSaw(float p, float dt)
    {
        var x = 2f * p - 1f;
        if (p < dt)
        {
            var t = p / dt;
            x -= t + t - t * t - 1f;
        }
        else if (p > 1f - dt)
        {
            var t = (p - 1f) / dt;
            x -= t * t + t + t + 1f;
        }

        return x;
    }

    // ---------------------------------------------------------------- voices and scheduling

    /// <summary>
    /// Schedules a note to start at <paramref name="frame"/> (never earlier than the render cursor); a note without a group
    /// (<c>Group == -1</c>, the <see cref="NoteSpec.Tone"/> default) is an effect.
    /// </summary>
    private void Enqueue(long frame, NoteSpec note)
    {
        if (note.Group == -1) note = note with { Group = GroupSfx };
        _pending.Add((Math.Max(frame, _frame), note));
    }

    /// <summary>Starts every pending note due before <paramref name="blockEnd"/>, each at its own offset into the block (swap-remove: order is not kept).</summary>
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

    /// <summary>
    /// Starts a note on a free voice, or steals one: music may only take the quietest music voice once it holds
    /// <see cref="MaxMusicVoices"/>, so effects always find room; an effect takes the quietest voice of any kind. A voice
    /// started in this same pass is never stolen. Returns null when the note is dropped.
    /// </summary>
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

    /// <summary>The next value of the synth's own generator (the Numerical Recipes LCG constants), odd so it can seed a voice's xorshift.</summary>
    private uint NextSeed()
    {
        _noiseSeed = _noiseSeed * 1664525u + 1013904223u;
        return _noiseSeed | 1u;
    }

    /// <summary>Uniform in [0, 1): the top 24 bits of <see cref="NextSeed"/> (an LCG's low bits are its weakest).</summary>
    private float Rand01()
    {
        var s = NextSeed();
        return (s >> 8) * (1f / 16777216f);
    }

    /// <summary>
    /// The frame of a deck's sixteenth <paramref name="step"/> (counted from its origin, its first downbeat). Rounded up,
    /// less a hair (1e-6 frames) so a product that lands a rounding error above a whole frame does not slip a frame late.
    /// </summary>
    private static long StepFrame(Deck d, long step) => d.Origin + (long)Math.Ceiling(step * d.FramesPerSixteenth - 1e-6);

    /// <summary>
    /// The deck whose clock is the music's at <paramref name="frame"/>: the current deck once its first downbeat has come,
    /// otherwise the deck it is taking over from (which plays up to that downbeat).
    /// </summary>
    private Deck ClockAt(long frame)
    {
        var current = _decks[_current];
        return frame >= current.Origin ? current : _decks[1 - _current];
    }

    /// <summary>The next point of the playing music's 32nd-note grid at or after the render cursor.</summary>
    private long NextGridFrame()
    {
        var d = ClockAt(_frame);
        var half = d.GridFramesPerSixteenth / 2;
        var k = (long)Math.Ceiling((_frame - d.Origin) / half - 1e-9);
        var at = Math.Max(_frame, d.Origin + (long)Math.Ceiling(k * half - 1e-6));
        // A takeover's first downbeat is itself a point of the old grid; never wait past it onto the old grid.
        var next = _decks[_current];
        return next.Origin > _frame && at > next.Origin ? next.Origin : at;
    }

    /// <summary>One 32nd note of the music playing at <paramref name="frame"/>, in frames.</summary>
    private long GridStepFrames(long frame) => (long)Math.Round(ClockAt(frame).GridFramesPerSixteenth / 2);

    private long Frames(float seconds) => (long)Math.Round(seconds * _rate);

    /// <summary>
    /// The per-frame multiplier of an exponential decay that falls to 0.001 (−60 dB) in <paramref name="t60Seconds"/>.
    /// As a smoothing factor, <c>1 − Coef</c> is the fraction of the remaining gap closed each frame.
    /// </summary>
    private float Coef(float t60Seconds) => MathF.Exp(MathF.Log(0.001f) / (t60Seconds * _rate));

    /// <summary>The scale of the music playing now: every effect is pitched in it.</summary>
    private int[] CurrentScale => ClockAt(_frame).Profile.Scale;

    private float PitchHz(int degree, int octave) => PitchHz(ClockAt(_frame).Profile, degree, octave);

    private static float PitchHz(Profile p, int degree, int octave) => MidiHz(p.Root + 12 * octave + ScaleNote(p.Scale, degree));

    /// <summary>Semitones above the root of scale degree <paramref name="degree"/>; degrees beyond the scale (or negative) wrap into other octaves.</summary>
    private static int ScaleNote(int[] scale, int degree)
    {
        var len = scale.Length;
        var oct = (int)Math.Floor(degree / (double)len);
        return scale[degree - oct * len] + 12 * oct;
    }

    /// <summary>Equal temperament: MIDI 69 is A 440 Hz, and each semitone is a factor of 2^(1/12).</summary>
    private static float MidiHz(int note) => 440f * MathF.Pow(2f, (note - 69) / 12f);

    private static float Clamp01(float x) => float.IsFinite(x) ? Math.Clamp(x, 0f, 1f) : 0f;

    private static float MoveTowards(float x, float target, float step) => x < target ? MathF.Min(target, x + step) : MathF.Max(target, x - step);

    /// <summary>
    /// Leaves |x| ≤ 0.85 untouched, bends anything louder smoothly (tanh) towards 1, and caps at 0.9999: the output
    /// contract |x| &lt; 1 holds even if the limiter has not caught a peak yet.
    /// </summary>
    private static float SoftClip(float x)
    {
        const float knee = 0.85f;
        var a = MathF.Abs(x);
        if (a > knee) a = knee + (1f - knee) * MathF.Tanh((a - knee) / (1f - knee));
        a = MathF.Min(a, 0.9999f);
        return x < 0 ? -a : a;
    }

    // A 2048-entry sine table with linear interpolation (phase in cycles, 0..1). One extra entry (TableSize + 1) repeats
    // the first, so interpolation at the last index never needs a wrap.
    private const int TableSize = 2048;
    private static readonly float[] SineTable = BuildSine();

    private static float[] BuildSine()
    {
        var t = new float[TableSize + 1];
        for (var i = 0; i <= TableSize; i++) t[i] = MathF.Sin(2f * MathF.PI * i / TableSize);
        return t;
    }

    // ---------------------------------------------------------------- voice

    /// <summary>
    /// A voice's oscillator: the pitched waveforms and filtered noise (drums, wind, whooshes). <see cref="Saw"/> (AD-52) is a
    /// band-limited (polyBLEP) sawtooth stack of up to three detuned copies (<see cref="NoteSpec.Unison"/>) through a
    /// two-pole low-pass at <see cref="NoteSpec.ToneHz"/>: the supersaw, the string ensemble, the horns and the reese bass.
    /// </summary>
    private enum Wave
    {
        Sine,
        Triangle,
        SoftSquare,
        Bell,
        Noise,
        Saw,
    }

    /// <summary>
    /// A voice's envelope stage: a linear attack to 1, an exponential decay towards the sustain level, then an exponential
    /// release once the note's <c>Hold</c> (counted from its start) runs out or it is released. A note with zero sustain
    /// can end in its decay.
    /// </summary>
    private enum Stage
    {
        Attack,
        Decay,
        Release,
    }

    /// <summary>
    /// What to play. Times are seconds (Attack, Glide, Hold), Decay/Release are T60 seconds; frequencies Hz. Gain is linear
    /// amplitude (voices are designed small, see <see cref="OutputGain"/>), Pan −1 (left) … +1, Send the share into the
    /// reverb, ToneHz a one-pole low-pass (0: none; two poles for <see cref="Wave.Saw"/>), Group the voice group (−1 until
    /// <see cref="Enqueue"/> makes it an effect). The factories give the defaults every effect and score starts from, adjusted
    /// with <c>with</c>.
    /// <para>
    /// AD-52 additions, all off by default (so the older sounds render exactly as before): ToneToHz and ToneGlide sweep the
    /// tone filter (exponentially, in ToneGlide seconds: a filter opening, a horn's swell); VibratoHz and VibratoDepth (a
    /// fraction of the pitch, 0.003 ≈ 5 cents) wobble the pitch; Unison (1–3) and DetuneCents (the outer copies' offset) stack
    /// detuned saws.
    /// </para>
    /// </summary>
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
        public float ToneToHz { get; init; }
        public float ToneGlide { get; init; }
        public float VibratoHz { get; init; }
        public float VibratoDepth { get; init; }
        public int Unison { get; init; }
        public float DetuneCents { get; init; }

        public static NoteSpec Tone(Wave wave, float hz) => new() { Wave = wave, FreqHz = hz, Attack = 0.003f, Decay = 0.5f, Release = 0.3f, Gain = 0.1f, Group = -1 };

        public static NoteSpec Bell(float hz, float ratio, float index, float indexDecay) =>
            Tone(Wave.Bell, hz) with { FmRatio = ratio, FmIndex = index, FmIndexDecay = indexDecay };

        public static NoteSpec Noise(float cutoffHz, float cutoffToHz, float glideSeconds, float q, bool lowpass = false) =>
            Tone(Wave.Noise, 0) with { CutoffHz = cutoffHz, CutoffToHz = cutoffToHz, CutoffGlide = glideSeconds, Q = q, Lowpass = lowpass };

        /// <summary>A stack of <paramref name="unison"/> saws, the outer ones <paramref name="detuneCents"/> above and below (a middle one stays in tune).</summary>
        public static NoteSpec Saw(float hz, int unison, float detuneCents, float toneHz) =>
            Tone(Wave.Saw, hz) with { Unison = unison, DetuneCents = detuneCents, ToneHz = toneHz };
    }

    /// <summary>
    /// One pooled voice: oscillator, envelope, glide, one-pole tone filter, pan and send, and for noise a state-variable
    /// filter. <c>Start</c> reinitialises every field, so voices are reused without allocation; <see cref="Generation"/>
    /// tells a caller holding a voice (the flight wind) whether it has since been stolen for another note. Filter cutoffs
    /// are clamped to 20 Hz … 0.45 × the sample rate, just under Nyquist, where the filters stay stable.
    /// </summary>
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

        // AD-52: the second tone pole (saws), the tone sweep, vibrato, the saw stack and frequency steering.
        public float ToneState2, ToneHz, ToneRatio, ToneRatioControl;
        public int ToneRemaining;
        public float VibPhase, VibInc, VibDepth;
        public float U0, U1, U2, D0, D1, D2, W2, UnisonNorm;
        public float FreqTarget, FreqK, FreqKControl;

        // Noise filter (topology-preserving state-variable filter).
        private float _cutoffHz, _cutoffRatio, _cutoffTargetHz, _cutoffK;
        private int _cutoffRemaining;
        public float FilterK, A1, A2, A3, Ic1, Ic2;
        public bool Lowpass;
        private int _rate;

        /// <summary>Starts <paramref name="n"/> <paramref name="delay"/> frames into the next block, precomputing every per-frame and per-control-period coefficient.</summary>
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
            // Equal-power pan: pan −1…+1 becomes an angle 0…π/2, and cos² + sin² = 1 keeps loudness constant across the field.
            var angle = (Math.Clamp(n.Pan, -1f, 1f) + 1f) * MathF.PI * 0.25f;
            PanL = MathF.Cos(angle);
            PanR = MathF.Sin(angle);
            Send = n.Send;
            // The one-pole tone (low-pass) filter's coefficient for ToneHz; 0 means no filtering (coefficient 1).
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
            ToneState2 = 0;
            ToneHz = n.ToneHz > 0 ? MathF.Min(n.ToneHz, 0.45f * rate) : 0;
            var toneFrames = (int)Math.Round(n.ToneGlide * rate);
            ToneRemaining = ToneHz > 0 && n.ToneToHz > 0 && toneFrames > 0 ? toneFrames : 0;
            ToneRatio = ToneRemaining > 0 ? MathF.Pow(MathF.Min(n.ToneToHz, 0.45f * rate) / ToneHz, 1f / toneFrames) : 1f;
            ToneRatioControl = MathF.Pow(ToneRatio, ControlFrames);
            VibPhase = 0;
            VibInc = n.VibratoHz / rate;
            VibDepth = n.VibratoDepth;
            // The saw stack: copy 0 in tune, copies 1 and 2 detuned up and down; the stack is scaled by 1/√copies (detuned
            // copies add in power, not amplitude), so a supersaw is about as loud as one saw. Phases start spread so the
            // copies do not all reset together on the first cycle.
            var unison = Math.Clamp(n.Unison, 1, 3);
            var detune = MathF.Pow(2f, n.DetuneCents / 1200f);
            U0 = 0;
            U1 = 0.37f;
            U2 = 0.71f;
            D0 = 1f;
            D1 = unison >= 2 ? detune : 1f;
            D2 = unison >= 3 ? 1f / detune : 1f;
            W2 = unison >= 3 ? 1f : 0f;
            UnisonNorm = 1f / MathF.Sqrt(unison);
            FreqTarget = 0;
            FreqK = 0;
            FreqKControl = 0;
            Lowpass = n.Lowpass;
            FilterK = 1f / MathF.Max(0.5f, n.Q); // damping = 1/Q; Q is floored at 0.5 (damping at most 2: the broadest, least resonant band)
            _cutoffHz = Math.Clamp(n.CutoffHz, 20f, 0.45f * rate);
            var cutoffFrames = (int)Math.Round(n.CutoffGlide * rate);
            _cutoffRemaining = n.CutoffToHz > 0 && cutoffFrames > 0 ? cutoffFrames : 0;
            _cutoffRatio = _cutoffRemaining > 0 ? MathF.Pow(Math.Clamp(n.CutoffToHz, 20f, 0.45f * rate) / _cutoffHz, 1f / cutoffFrames) : 1f;
            _cutoffTargetHz = 0;
            _cutoffK = 0;
            if (Wave == Wave.Noise) SetCoefficients();
        }

        /// <summary>Moves the voice to its release now (it then fades out by its own release time).</summary>
        public void Release()
        {
            Released = true;
            if (Stage != Stage.Release) Stage = Stage.Release;
            HoldRemaining = 0;
        }

        /// <summary>Releases quickly (T60 <paramref name="t60Seconds"/>, ≈ 40 ms): fast enough to clear a deck, slow enough not to click.</summary>
        public void Kill(float t60Seconds)
        {
            Release();
            ReleaseCoef = T60(t60Seconds, _rate);
            ReleaseCoefControl = MathF.Pow(ReleaseCoef, ControlFrames);
        }

        /// <summary>
        /// Makes the pitch follow <paramref name="hz"/> (in log frequency, closing the fraction <paramref name="k"/> of the
        /// interval per frame), for a voice steered every frame (the fall's spiral); replaces any glide.
        /// </summary>
        public void SetFreqTarget(float hz, float k)
        {
            if (!(hz > 0) || !float.IsFinite(hz)) return;
            FreqTarget = hz;
            FreqK = k;
            FreqKControl = 1f - MathF.Pow(1f - k, ControlFrames);
            GlideRemaining = 0;
        }

        /// <summary>Makes the noise filter's cutoff follow <paramref name="hz"/>, closing the fraction <paramref name="k"/> of the gap per frame (replaces any glide).</summary>
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

        /// <summary>The topology-preserving (trapezoidal) state-variable filter's coefficients for the current cutoff and damping.</summary>
        private void SetCoefficients()
        {
            var g = MathF.Tan(MathF.PI * MathF.Min(_cutoffHz, 0.45f * _rate) / _rate);
            A1 = 1f / (1f + g * (g + FilterK));
            A2 = g * A1;
            A3 = g * A2;
        }

        /// <summary>Per-frame multiplier falling to −60 dB in <paramref name="seconds"/> (at least 2 ms, so no envelope is a click).</summary>
        private static float T60(float seconds, int rate) => MathF.Exp(MathF.Log(0.001f) / (MathF.Max(0.002f, seconds) * rate));
    }

    // ---------------------------------------------------------------- reverb

    /// <summary>
    /// A light Freeverb-style reverb: 4 damped feedback combs and 2 allpasses per channel, mono in, stereo out. Half of
    /// Freeverb's 8 combs and 4 allpasses keeps the cost low; the shared "send" bus gives every voice the same room.
    /// </summary>
    private sealed class Reverb
    {
        /// <summary>Comb delay lengths in frames at 44.1 kHz: four of Freeverb's eight (mutually unrelated lengths, so echoes do not reinforce).</summary>
        private static readonly int[] CombTuning = [1116, 1277, 1422, 1557];

        /// <summary>Allpass delay lengths in frames at 44.1 kHz: two of Freeverb's four (they diffuse the combs' echoes).</summary>
        private static readonly int[] AllpassTuning = [556, 341];

        /// <summary>Extra frames on the right channel's lines (Freeverb's stereo spread): the channels decorrelate into width.</summary>
        private const int Spread = 23;

        /// <summary>Comb feedback: the room size. Higher rings longer.</summary>
        private const float Feedback = 0.83f;

        /// <summary>High-frequency damping inside the combs: higher sounds darker and softer.</summary>
        private const float Damp = 0.3f;

        /// <summary>Level of the send into the combs (the wet level; the voices' sends set each one's share).</summary>
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
                    line[idx] = s + b * 0.5f; // Freeverb's allpass feedback
                    output[i] = b - s;
                    if (++idx == line.Length) idx = 0;
                }

                ai[a] = idx;
            }
        }
    }
}
