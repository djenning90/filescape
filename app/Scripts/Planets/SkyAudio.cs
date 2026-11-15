using System.Diagnostics;
using FileScape.Core.Audio;
using FileScape.Core.Planets;
using FileScape.Core.Units;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The Planets experience's sound: plays <see cref="SkySynth"/> (original, fully synthesised music and effects; no
/// samples) through an <see cref="AudioStreamPlayer"/> fed by an <see cref="AudioStreamGenerator"/> (44 100 Hz,
/// stereo, a 0.12 s ring buffer). Every <c>_Process</c> it renders just enough frames to keep
/// <see cref="TargetFill"/> queued, so an effect called this frame is heard after roughly that much (plus the
/// driver's own output latency), while a hitch shorter than the buffer still does not underrun.
/// <para>
/// Cost: ≈ 735 frames per 60 Hz frame. The synth renders 1 s of its busiest world (the underglow inside a project, drums and
/// flight arpeggio) with six effects in ≈ 26 ms (Debug build, SkySynthTests, 2026-11-14), so ≈ 0.45 ms per frame, plus one
/// span push; each AD-52 arrangement (2026-11-15) with a flight build, a fall, the edge warning and six effects in 26–27 ms.
/// Nothing allocates per frame.
/// </para>
/// Call the effect methods from the main thread (the same thread as <c>_Process</c>).
/// </summary>
public partial class SkyAudio : Node
{
    /// <summary>The generator's mix rate, frames per second (the synth renders at the same rate; Godot resamples to the device).</summary>
    public const int MixRateHz = SkySynth.DefaultSampleRateHz;

    /// <summary>The generator's ring buffer: room for hitches up to ≈ 0.12 s.</summary>
    public static readonly TimeSpan BufferLength = TimeSpan.FromSeconds(0.12);

    /// <summary>Frames rendered and pushed per step (the scratch buffers' size, so nothing allocates per frame).</summary>
    private const int PushChunkFrames = 512;

    private readonly SkySynth _synth;
    private readonly float[] _interleaved = new float[PushChunkFrames * 2];
    private readonly Vector2[] _frames = new Vector2[PushChunkFrames];
    private readonly Stopwatch _cost = new();
    private AudioStreamPlayer _player = null!;
    private AudioStreamGeneratorPlayback? _playback;
    /// <summary>The generator's capacity in frames, read once when it is empty (so queued = capacity − available).</summary>
    private int _capacityFrames;

    /// <summary>A synth with seed 1 starting in the Hearth theme (Godot needs a parameterless constructor).</summary>
    public SkyAudio()
        : this(seed: 1, PlanetTheme.Hearth)
    {
    }

    /// <summary>A synth seeded with <paramref name="seed"/> that starts in <paramref name="theme"/>.</summary>
    public SkyAudio(int seed, PlanetTheme theme)
    {
        _synth = new SkySynth(seed, theme, MixRateHz);
        ProcessMode = ProcessModeEnum.Always; // sound keeps flowing while the tree is paused
    }

    /// <summary>
    /// How much audio to keep queued ahead of playback. 0.05 s (≈ 3 frames at 60 Hz) keeps effects responsive; raise it
    /// if the machine hitches and the sound crackles. Clamped to the buffer.
    /// </summary>
    public TimeSpan TargetFill { get; set; } = TimeSpan.FromSeconds(0.05);

    /// <summary>The synth itself (render cursor, key, voices), for diagnostics and probes.</summary>
    public SkySynth Synth => _synth;

    /// <summary>Time spent rendering and pushing audio in the last <c>_Process</c>.</summary>
    public TimeSpan LastProcessCost { get; private set; }

    /// <summary>Buffer underruns reported by the generator since playback started (each is an audible gap).</summary>
    public long Skips => _playback?.GetSkips() ?? 0;

    public override void _Ready()
    {
        var generator = new AudioStreamGenerator
        {
            MixRateMode = AudioStreamGenerator.AudioStreamGeneratorMixRate.Custom,
            MixRate = MixRateHz,
            BufferLength = BufferLength.ToSecondsF(),
        };
        // +5 dB (2026-11-11): measured in a recorded reel, the mix sat at ≈ −28 dBFS RMS (peaks ≤ 0.32), quiet for a game.
        _player = new AudioStreamPlayer { Name = "SkyAudioPlayer", Stream = generator, VolumeDb = 5 };
        AddChild(_player);
        _player.Play();
        _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        _capacityFrames = _playback?.GetFramesAvailable() ?? 0;
        if (!_active)
        {
            _player.Stop();
            _playback = null;
            _synth.Mute(true);
        }
    }

    /// <summary>Tops up the buffer while active (see <see cref="Fill"/>).</summary>
    public override void _Process(double delta)
    {
        if (!_active) return;
        Fill();
    }

    private bool _active;

    /// <summary>
    /// On while the planetary world is shown: the music starts (muted ramp in), and off it stops filling, so the other
    /// experiences pay nothing for it (2026-11-11 integration).
    /// </summary>
    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        _synth.Mute(!active || Muted);
        if (_player == null) return;
        if (active)
        {
            if (!_player.Playing) _player.Play();
            _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
            Fill();
        }
        else
        {
            _player.Stop();
            _playback = null;
        }
    }

    /// <summary>N: sound on or off (the music and effects; kept while FileScape runs).</summary>
    public bool Muted { get; private set; }

    /// <summary>N: flips <see cref="Muted"/>.</summary>
    public void ToggleMute()
    {
        Muted = !Muted;
        _synth.Mute(Muted || !_active);
    }

    /// <summary>The music's level under a sound preview (linear, 20 %): the preview is clearly heard and the music
    /// does not stop (rationale for 0.2 not recorded).</summary>
    private const float DuckedMusicVolume = 0.2f;

    /// <summary>Lowers the music under a sound preview (and restores it).</summary>
    public void Duck(bool ducked) => _synth.MusicVolume = ducked ? DuckedMusicVolume : 1f;

    /// <summary>Bell flowers rung since start (probes).</summary>
    public int ChimeCount { get; private set; }

    /// <summary>
    /// A duration near <paramref name="seconds"/> (within <paramref name="maxShiftSeconds"/>) that ends on the sixteenth-note
    /// grid of the music playing then, at its own tempo (94–132 BPM: a sixteenth is 0.16–0.11 s), so a launch released after
    /// it lands on the beat (research E6). Measured in synth time, which is the time the release sound is rendered at
    /// (<see cref="SkySynth.AlignToGrid"/>).
    /// </summary>
    public float AlignToSixteenth(float seconds, float maxShiftSeconds)
    {
        var aligned = _synth.AlignToGrid(Durations.FromSeconds(seconds), Durations.FromSeconds(maxShiftSeconds));
        return MathF.Max(0.1f, aligned.ToSecondsF()); // never shorter than 0.1 s, whatever the grid says
    }

    public override void _ExitTree()
    {
        _player?.Stop();
        _playback = null;
    }

    /// <summary>
    /// Renders just enough to bring the queued audio up to <see cref="TargetFill"/> (never more than the buffer has room
    /// for), in chunks of <see cref="PushChunkFrames"/>. Rendering ahead of need would add latency to every effect, so
    /// this deliberately keeps the queue short. Records its own cost in <see cref="LastProcessCost"/>.
    /// </summary>
    private void Fill()
    {
        if (_playback == null) return;
        _cost.Restart();
        var available = _playback.GetFramesAvailable();
        var queued = _capacityFrames - available;
        var target = Math.Min(_capacityFrames, (int)Math.Round(TargetFill.TotalSeconds * MixRateHz));
        var needed = Math.Min(available, target - queued);
        while (needed > 0)
        {
            var n = Math.Min(needed, PushChunkFrames);
            var samples = _interleaved.AsSpan(0, n * 2);
            _synth.Render(samples);
            for (var i = 0; i < n; i++) _frames[i] = new Vector2(samples[i * 2], samples[i * 2 + 1]);
            _playback.PushBuffer(new ReadOnlySpan<Vector2>(_frames, 0, n));
            needed -= n;
        }

        _cost.Stop();
        LastProcessCost = _cost.Elapsed;
    }

    // ---------------------------------------------------------------- music

    /// <summary>
    /// The music of a world, on every arrival: its theme (key), its character (hub, Windows volume, WSL distribution,
    /// repository, or a folder's depth band) and how many times this folder has been arrived at this session (1 on the
    /// first). The new world starts on the next beat at its own tempo; hops and returns stay musical (see
    /// <see cref="SkySynth.SetWorld"/>).
    /// </summary>
    public void SetWorld(PlanetTheme theme, WorldCharacter character, int visits) => _synth.SetWorld(theme, character, visits);

    /// <summary>Changes the key to a planet theme's and keeps the current world's character (see <see cref="SetWorld"/>).</summary>
    public void SetTheme(PlanetTheme theme) => _synth.SetTheme(theme);

    /// <summary>Flight intensity 0..1 (adds the rising arpeggio layer); set it every frame during a launch arc.</summary>
    public void SetFlight(float intensity01) => _synth.SetFlight(intensity01);

    /// <summary>Overall level 0..1 (linear amplitude, default 0.8).</summary>
    public void SetMasterVolume(float volume01) => _synth.SetMasterVolume(volume01);

    /// <summary>Silences or restores all sound with a short ramp.</summary>
    public void Mute(bool muted) => _synth.Mute(muted);

    // ---------------------------------------------------------------- effects (see SkySynth for each sound)

    /// <summary>A footstep, intensity 0..1 (walk ≈ 0.5, run ≈ 1). Immediate.</summary>
    public void Step(float intensity) => _synth.Step(intensity);

    /// <summary>A jump. Immediate.</summary>
    public void Jump() => _synth.Jump();

    /// <summary>An air jump: a sparkly upward flourish. Immediate.</summary>
    public void AirJump() => _synth.AirJump();

    /// <summary>A landing; <paramref name="impactMetersPerSecond"/> is the downward speed at touchdown. Immediate.</summary>
    public void Land(float impactMetersPerSecond) => _synth.Land(impactMetersPerSecond);

    /// <summary>A launcher wind-up that peaks exactly <paramref name="duration"/> from now (call <see cref="Launch"/> then). Immediate.</summary>
    public void WindUp(TimeSpan duration) => _synth.WindUp(duration);

    /// <summary>The same, for a wind-up length in float seconds (as <see cref="AlignToSixteenth"/> returns it).</summary>
    public void WindUp(float seconds) => _synth.WindUp(Durations.FromSeconds(seconds));

    /// <summary>The launch whoosh and glissando. Immediate.</summary>
    public void Launch() => _synth.Launch();

    /// <summary>The flight wind for arc progress 0..1; call every frame while flying. Immediate.</summary>
    public void Fly(float progress01) => _synth.Fly(progress01);

    /// <summary>
    /// Arrival: a warm chord and a softer chime ≈ 120 ms later. Tempo-locked (≤ 80 ms at every tempo). After a flight out of
    /// a festival world (the trails, AD-52), it also releases the build the flight played: the drop lands on the first beat
    /// at least 0.15 s later. Call it after <see cref="SetWorld"/>, as now; nothing else is needed for the drop.
    /// </summary>
    public void Arrive() => _synth.Arrive();

    // ---------------------------------------------------------------- hazard cues (AD-52)

    /// <summary>
    /// The edge warning: call every frame while near an edge of a hazard world, <paramref name="nearness01"/> 0 (far) … 1 (at
    /// the edge). Self-throttling (a pulse every 1 s … 0.25 s on the beat grid, a rumble that follows the nearness) and cheap;
    /// silent below 0.05 and within 0.3 s of the last call.
    /// </summary>
    public void HazardWarning(float nearness01) => _synth.HazardWarning(nearness01);

    /// <summary>The ground gives way: a crack, falling shards, a drop; the music ducks deep for the fall. Immediate.</summary>
    public void FallStart() => _synth.FallStart();

    /// <summary>The fall or spiral for progress 0..1 (a whoosh falling three octaves, circling left and right ever faster); call every frame while falling. Immediate.</summary>
    public void Fall(float progress01) => _synth.Fall(progress01);

    /// <summary>Back at the parent after a fall: an upward pop and a bright chord, and the music returns. Immediate; <see cref="Arrive"/> may follow as usual.</summary>
    public void FallReturn() => _synth.FallReturn();

    /// <summary>Selection tick. Tempo-locked.</summary>
    public void Select() => _synth.Select();

    /// <summary>Inspection (E). Tempo-locked.</summary>
    public void Inspect() => _synth.Inspect();

    /// <summary>Opening a file or folder. Tempo-locked.</summary>
    public void Open() => _synth.Open();

    /// <summary>A gentle refusal. Tempo-locked.</summary>
    public void Deny() => _synth.Deny();

    /// <summary>A bell-flower note at scale degree <paramref name="index"/>. Tempo-locked.</summary>
    public void Chime(int index)
    {
        ChimeCount++;
        _synth.Chime(index);
    }
}
