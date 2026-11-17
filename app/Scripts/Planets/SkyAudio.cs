using System.Diagnostics;
using FileScape.Core.Audio;
using FileScape.Core.Planets;
using FileScape.Core.Units;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The Planets experience's sound: plays <see cref="SkySynth"/> (original, fully synthesised music and effects; no
/// samples) through an <see cref="AudioStreamPlayer"/> fed by an <see cref="AudioStreamGenerator"/> (44 100 Hz,
/// stereo, a 0.5 s ring buffer). Its own audio thread (FOLLOW-2, 2026-11-17) wakes every few milliseconds and renders
/// just enough frames to keep <see cref="TargetFill"/> queued, so an effect called this frame is heard after roughly
/// that much (plus the driver's own output latency). The thread does not depend on Godot's frame callback: the music
/// keeps flowing while the main loop stalls — loading a world, compiling shaders at startup, and the Windows move and
/// resize loop, which stop <c>_Process</c>. (Filled from <c>_Process</c> with 50 ms queued, every frame longer than
/// that was a gap: 206 at startup, 144 in three hops, 8–163 per 60–500 ms stall; planets-audio-gaps.)
/// <para>
/// Threading: the synth is touched under <see cref="_gate"/> by both threads (the audio thread renders a chunk of at
/// most <see cref="PushChunkFrames"/> under it, ≈ 0.3 ms; every effect call takes it briefly), so a change must keep
/// every <c>_synth</c> access inside the lock.
/// </para>
/// <para>
/// Cost: ≈ 735 frames per 60 Hz frame. The synth renders 1 s of its busiest world (the underglow inside a project, drums and
/// flight arpeggio) with six effects in ≈ 26 ms (Debug build, SkySynthTests, 2026-11-14), so ≈ 0.45 ms per frame, plus one
/// span push; each AD-52 arrangement (2026-11-15) with a flight build, a fall, the edge warning and six effects in 26–27 ms.
/// Nothing allocates per frame.
/// </para>
/// Call the effect methods from the main thread.
/// </summary>
public partial class SkyAudio : Node
{
    /// <summary>The generator's mix rate, frames per second (the synth renders at the same rate; Godot resamples to the device).</summary>
    public const int MixRateHz = SkySynth.DefaultSampleRateHz;

    /// <summary>The generator's ring buffer: the most that could be queued (the audio thread keeps only TargetFill in it).</summary>
    public static readonly TimeSpan BufferLength = TimeSpan.FromSeconds(0.5);

    /// <summary>How long the audio thread sleeps between top-ups, milliseconds (Windows rounds short sleeps up to its
    /// timer, ≈ 1–16 ms; well inside TargetFill either way).</summary>
    private const int FillIntervalMs = 4;

    /// <summary>
    /// Startup headroom (FOLLOW-2): for the first 8 s of playback 0.2 s stays queued instead of <see cref="TargetFill"/>.
    /// With the audio thread alone, 3 gaps remained at startup (frames up to 0.56 s while the first worlds load; the
    /// runtime's garbage collections pause this thread as well). Effects in those seconds are heard ≈ 0.15 s later.
    /// </summary>
    private static readonly TimeSpan StartupFill = TimeSpan.FromSeconds(0.2), StartupHeadroomDuration = TimeSpan.FromSeconds(8);

    private readonly Stopwatch _sinceStart = new();
    private readonly Stopwatch _sinceFill = new();
    private long _lastSkips;

    /// <summary>
    /// Underruns after the first <see cref="FirstSoundDuration"/> of playback: each is a gap in sound already playing. The
    /// generator counts a few empty blocks as playback starts (+3 at 10 ms in every run, with 0.2 s already queued
    /// by the next top-up): silence before the first sound, not a gap.
    /// </summary>
    public long AudibleSkips { get; private set; }

    /// <summary>The start of playback where the generator's own empty first blocks fall, 0.1 s.</summary>
    private static readonly TimeSpan FirstSoundDuration = TimeSpan.FromSeconds(0.1);

    /// <summary>When underruns were noticed (diagnostics, FOLLOW-2): time since playback began, what was queued, the gap since the previous top-up.</summary>
    public List<string> SkipLog { get; } = [];

    /// <summary>The player's volume while sound is on, dB (+5: the mix sat at ≈ −28 dBFS RMS, quiet for a game, 2026-11-11).</summary>
    private const float PlayerVolumeDb = 5f;

    /// <summary>The player's volume while muted, dB: Godot's floor, silent.</summary>
    private const float MutedVolumeDb = -80f;

    /// <summary>Guards the synth and the playback between the main thread and the audio thread.</summary>
    private readonly object _gate = new();
    private System.Threading.Thread? _thread;
    private volatile bool _running;

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

    /// <summary>Time spent rendering and pushing audio in the audio thread's last top-up.</summary>
    public TimeSpan LastProcessCost { get; private set; }

    /// <summary>Buffer underruns reported by the generator since playback started (each is an audible gap).</summary>
    public long Skips => _playback is { } pb ? pb.GetSkips() : 0;

    /// <summary>Audio queued ahead of playback at the last fill, before topping up, seconds (FOLLOW-2 diagnostics).</summary>
    public double QueuedSeconds { get; private set; }

    /// <summary>The least audio found queued at a fill since <see cref="ResetQueueLow"/>, seconds: 0 means it ran dry.</summary>
    public double QueueLowSeconds { get; private set; } = double.MaxValue;

    /// <summary>Starts a new <see cref="QueueLowSeconds"/> measurement.</summary>
    public void ResetQueueLow()
    {
        QueueLowSeconds = double.MaxValue;
        LoopGapMaxMs = 0;
        MixGapMaxMs = 0;
    }

    /// <summary>
    /// The diagnostics as a JSON object for the state file (FOLLOW-2): underruns, queue, the audio thread's longest wake gap,
    /// the mixer's longest gap, the runtime's collections and total pause time, and the first underruns' log.
    /// </summary>
    public string DiagnosticsJson()
    {
        var log = string.Join(",", SkipLog.Select(l => "\"" + l.Replace("\"", "'") + "\""));
        var low = QueueLowSeconds == double.MaxValue ? -1 : QueueLowSeconds * 1000;
        return System.FormattableString.Invariant($"{{\"skips\":{Skips},\"audible\":{AudibleSkips},\"queuedMs\":{QueuedSeconds * 1000:0},\"lowMs\":{low:0},\"loopGapMaxMs\":{LoopGapMaxMs:0.#},\"mixGapMaxMs\":{MixGapMaxMs:0.#},\"gc0\":{GC.CollectionCount(0)},\"gc1\":{GC.CollectionCount(1)},\"gc2\":{GC.CollectionCount(2)},\"gcPauseMs\":{GC.GetTotalPauseDuration().TotalMilliseconds:0},\"latencyMs\":{AudioServer.GetOutputLatency() * 1000:0},\"recordS\":{RecordSeconds:0.000},\"recordOverruns\":{RecordOverruns},\"log\":[{log}]}}");
    }

    public override void _Ready()
    {
        var generator = new AudioStreamGenerator
        {
            MixRateMode = AudioStreamGenerator.AudioStreamGeneratorMixRate.Custom,
            MixRate = MixRateHz,
            BufferLength = BufferLength.ToSecondsF(),
        };
        // +5 dB (2026-11-11): measured in a recorded reel, the mix sat at ≈ −28 dBFS RMS (peaks ≤ 0.32), quiet for a game.
        _player = new AudioStreamPlayer { Name = "SkyAudioPlayer", Stream = generator, VolumeDb = Muted ? MutedVolumeDb : PlayerVolumeDb };
        AddChild(_player);
        lock (_gate)
        {
            _player.Play();
            _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
            _capacityFrames = _playback?.GetFramesAvailable() ?? 0;
            _sinceStart.Restart();
            if (!_active)
            {
                _player.Stop();
                _playback = null;
                _synth.Mute(true);
            }
            else
            {
                Fill(); // primed before the audio server mixes its first block (an empty first block was a gap)
            }
        }

        StartOutputRecording();
    }

    // ---------------------------------------------------------------- output recording (FOLLOW-2 diagnostics)

    /// <summary>
    /// When FILESCAPE_AUDIO_RECORD names a .wav path (probes only), what the audio server actually mixed on the Master bus
    /// is captured (an <see cref="AudioEffectCapture"/>) and written there when the node leaves the tree, so a gap a person
    /// hears can be looked for in the output itself, not only in the generator's counters (2026-11-17: the real resize
    /// glitch with 0 skips). Off otherwise: nothing is added to the bus and nothing is stored.
    /// </summary>
    private string? _recordPath;
    private AudioEffectCapture? _capture;
    private readonly List<float> _recorded = [];
    private readonly Stopwatch _recordClock = new();

    /// <summary>The most a recording keeps, seconds of output (≈ 23 MB at 48 kHz stereo floats).</summary>
    private const int MaxRecordSeconds = 60;

    private void StartOutputRecording()
    {
        var path = System.Environment.GetEnvironmentVariable("FILESCAPE_AUDIO_RECORD");
        if (string.IsNullOrEmpty(path)) return;
        _recordPath = path;
        _capture = new AudioEffectCapture { BufferLength = 1f };
        AudioServer.AddBusEffect(0, _capture);
        _recordClock.Restart();
    }

    /// <summary>Wall-clock seconds since the recording started (probes write it next to their phases to place a gap).</summary>
    public double RecordSeconds => _recordClock.Elapsed.TotalSeconds;

    /// <summary>Times the capture's buffer was found full (frames lost from the recording, not from the output).</summary>
    public int RecordOverruns { get; private set; }

    /// <summary>Drains the capture into the recording (the audio thread, after each fill).</summary>
    private void DrainCapture()
    {
        if (_capture == null) return;
        var n = _capture.GetFramesAvailable();
        if (n <= 0) return;
        if (n >= (int)(AudioServer.GetMixRate() * 0.95f)) RecordOverruns++;
        var frames = _capture.GetBuffer(n);
        if (_recorded.Count / 2 > MaxRecordSeconds * AudioServer.GetMixRate()) return;
        foreach (var f in frames)
        {
            _recorded.Add(f.X);
            _recorded.Add(f.Y);
        }
    }

    /// <summary>Writes the recording as a 16-bit stereo WAV at the mix rate.</summary>
    private void WriteRecording()
    {
        if (_recordPath == null) return;
        var rate = (int)AudioServer.GetMixRate();
        using var w = new System.IO.BinaryWriter(System.IO.File.Create(_recordPath));
        var bytes = _recorded.Count * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2);
        w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16); w.Write("data"u8); w.Write(bytes);
        foreach (var v in _recorded) w.Write((short)Math.Clamp((int)(v * 32767f), -32768, 32767));
        _recordPath = null;
    }

    /// <summary>Starts the audio thread (it tops up the buffer while active; see <see cref="Fill"/>).</summary>
    public override void _EnterTree()
    {
        if (_thread != null) return;
        _running = true;
        _thread = new System.Threading.Thread(AudioLoop) { IsBackground = true, Name = "FileScape audio", Priority = System.Threading.ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>The audio thread: top up, sleep, until the node leaves the tree.</summary>
    private void AudioLoop()
    {
        var wake = Stopwatch.StartNew();
        while (_running)
        {
            // FOLLOW-2 diagnostics (2026-11-17, the real resize gap): how late this thread woke, and how long since the
            // audio server last mixed, so a gap can be placed in this thread, the runtime's pauses or the driver's mixer.
            LoopGapMaxMs = Math.Max(LoopGapMaxMs, wake.Elapsed.TotalMilliseconds);
            wake.Restart();
            MixGapMaxMs = Math.Max(MixGapMaxMs, AudioServer.GetTimeSinceLastMix() * 1000);
            lock (_gate)
            {
                if (_active) Fill();
                DrainCapture();
            }

            System.Threading.Thread.Sleep(FillIntervalMs);
        }
    }

    /// <summary>The longest interval between two wakes of the audio thread since <see cref="ResetQueueLow"/>, milliseconds (diagnostics).</summary>
    public double LoopGapMaxMs { get; private set; }

    /// <summary>The longest time since the audio server's last mix seen by the audio thread since <see cref="ResetQueueLow"/>, milliseconds (diagnostics).</summary>
    public double MixGapMaxMs { get; private set; }

    private bool _active;

    /// <summary>
    /// On while the planetary world is shown: the music starts (muted ramp in), and off it stops filling, so the other
    /// experiences pay nothing for it (2026-11-11 integration).
    /// </summary>
    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        lock (_gate) _synth.Mute(!active || Muted);
        if (_player == null) return;
        if (active)
        {
            lock (_gate)
            {
                if (!_player.Playing) { _player.Play(); _sinceStart.Restart(); }
                _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
                Fill(); // primed at once, before the first frame that might stall
            }
        }
        else
        {
            lock (_gate)
            {
                _player.Stop();
                _playback = null;
            }
        }
    }

    /// <summary>
    /// Sound off (PLANET-47): the toolbar's sound button or N, through Main, which saves it in Preferences. Silences the
    /// synth's master (a 15 ms ramp), so the music and every cue (launch, landing, travel, chimes, hazard warning, fall) are
    /// silent while the music's clock keeps running; turning it back on resumes the music where it would be.
    /// </summary>
    public bool Muted { get; private set; }

    /// <summary>Sets <see cref="Muted"/> (see there).</summary>
    public void SetMuted(bool muted)
    {
        Muted = muted;
        lock (_gate) _synth.Mute(Muted || !_active);
        // The player too, at once: the audio thread keeps up to 0.2 s queued ahead (FOLLOW-2), so muting the synth alone
        // let that much already-rendered sound play on after the click.
        if (_player != null) _player.VolumeDb = muted ? MutedVolumeDb : PlayerVolumeDb;
    }

    /// <summary>Flips <see cref="Muted"/>.</summary>
    public void ToggleMute() => SetMuted(!Muted);

    /// <summary>
    /// The largest absolute sample pushed to the output since <see cref="ResetOutputPeak"/> (linear, either channel): the
    /// meter the sound probe reads to show that a muted FileScape sends silence (PLANET-47). Cheap: one comparison per sample.
    /// </summary>
    public float OutputPeak { get; private set; }

    /// <summary>Starts a new <see cref="OutputPeak"/> measurement.</summary>
    public void ResetOutputPeak() => OutputPeak = 0;

    /// <summary>The music's level under a sound preview (linear, 20 %): the preview is clearly heard and the music
    /// does not stop (rationale for 0.2 not recorded).</summary>
    private const float DuckedMusicVolume = 0.2f;

    /// <summary>Lowers the music under a sound preview (and restores it).</summary>
    public void Duck(bool ducked) { lock (_gate) _synth.MusicVolume = ducked ? DuckedMusicVolume : 1f; }

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
        TimeSpan aligned;
        lock (_gate) aligned = _synth.AlignToGrid(Durations.FromSeconds(seconds), Durations.FromSeconds(maxShiftSeconds));
        return MathF.Max(0.1f, aligned.ToSecondsF()); // never shorter than 0.1 s, whatever the grid says
    }

    public override void _ExitTree()
    {
        _running = false;
        _thread?.Join(200);
        _thread = null;
        lock (_gate) WriteRecording();
        lock (_gate)
        {
            _player?.Stop();
            _playback = null;
        }
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
        var skipsNow = _playback.GetSkips();
        if (skipsNow != _lastSkips && SkipLog.Count < 40) SkipLog.Add($"+{skipsNow - _lastSkips} at {_sinceStart.Elapsed.TotalSeconds:0.000} s (queued {queued * 1000 / MixRateHz} ms, last fill {(_sinceFill.IsRunning ? _sinceFill.Elapsed.TotalMilliseconds : -1):0} ms ago, mix {AudioServer.GetTimeSinceLastMix() * 1000:0} ms ago, gc2 {GC.CollectionCount(2)})");
        if (_sinceStart.Elapsed > FirstSoundDuration) AudibleSkips += skipsNow - _lastSkips;
        _lastSkips = skipsNow;
        _sinceFill.Restart();
        QueuedSeconds = (double)queued / MixRateHz;
        QueueLowSeconds = Math.Min(QueueLowSeconds, QueuedSeconds);
        // Startup headroom: for the first StartupHeadroomDuration of playback keep StartupFill queued (the app loads its
        // first worlds and compiles shaders then, and the runtime's collections pause every thread, this one too).
        var fill = _sinceStart.IsRunning && _sinceStart.Elapsed < StartupHeadroomDuration ? StartupFill : TargetFill;
        var target = Math.Min(_capacityFrames, (int)Math.Round(fill.TotalSeconds * MixRateHz));
        var needed = Math.Min(available, target - queued);
        while (needed > 0)
        {
            var n = Math.Min(needed, PushChunkFrames);
            var samples = _interleaved.AsSpan(0, n * 2);
            _synth.Render(samples);
            var peak = OutputPeak;
            for (var i = 0; i < n; i++)
            {
                var l = samples[i * 2];
                var r = samples[i * 2 + 1];
                peak = MathF.Max(peak, MathF.Max(MathF.Abs(l), MathF.Abs(r)));
                _frames[i] = new Vector2(l, r);
            }

            OutputPeak = peak;
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
    public void SetWorld(PlanetTheme theme, WorldCharacter character, int visits) { lock (_gate) _synth.SetWorld(theme, character, visits); }

    /// <summary>Changes the key to a planet theme's and keeps the current world's character (see <see cref="SetWorld"/>).</summary>
    public void SetTheme(PlanetTheme theme) { lock (_gate) _synth.SetTheme(theme); }

    /// <summary>Flight intensity 0..1 (adds the rising arpeggio layer); set it every frame during a launch arc.</summary>
    public void SetFlight(float intensity01) { lock (_gate) _synth.SetFlight(intensity01); }

    /// <summary>Overall level 0..1 (linear amplitude, default 0.8).</summary>
    public void SetMasterVolume(float volume01) { lock (_gate) _synth.SetMasterVolume(volume01); }

    /// <summary>Silences, or restores, all sound with a short ramp; never louder than <see cref="Muted"/> allows (it does not change Muted).</summary>
    public void Mute(bool muted) { lock (_gate) _synth.Mute(muted || Muted || !_active); }

    // ---------------------------------------------------------------- effects (see SkySynth for each sound)

    /// <summary>A footstep, intensity 0..1 (walk ≈ 0.5, run ≈ 1). Immediate.</summary>
    public void Step(float intensity) { lock (_gate) _synth.Step(intensity); }

    /// <summary>A jump. Immediate.</summary>
    public void Jump() { lock (_gate) _synth.Jump(); }

    /// <summary>An air jump: a sparkly upward flourish. Immediate.</summary>
    public void AirJump() { lock (_gate) _synth.AirJump(); }

    /// <summary>The bell tower rung (PLANET-56): a big bell's strike and its answer in the world's key. Tempo-locked.</summary>
    public void BellToll() { lock (_gate) _synth.BellToll(); }

    /// <summary>A Hazard World's recovery twirl: the twirl's bells, higher and quicker, with a rising whoosh (PLANET-62). Immediate.</summary>
    public void RecoveryTwirl() { lock (_gate) _synth.RecoveryTwirl(); }

    /// <summary>A Hazard World's last effort: a broken-off twirl and a strained glide down, before the fall (PLANET-62). Immediate.</summary>
    public void LastEffort() { lock (_gate) _synth.LastEffort(); }

    /// <summary>A landing; <paramref name="impactMetersPerSecond"/> is the downward speed at touchdown. Immediate.</summary>
    public void Land(float impactMetersPerSecond) { lock (_gate) _synth.Land(impactMetersPerSecond); }

    /// <summary>A launcher wind-up that peaks exactly <paramref name="duration"/> from now (call <see cref="Launch"/> then). Immediate.</summary>
    public void WindUp(TimeSpan duration) { lock (_gate) _synth.WindUp(duration); }

    /// <summary>The same, for a wind-up length in float seconds (as <see cref="AlignToSixteenth"/> returns it).</summary>
    public void WindUp(float seconds) { lock (_gate) _synth.WindUp(Durations.FromSeconds(seconds)); }

    /// <summary>The launch whoosh and glissando. Immediate.</summary>
    public void Launch() { lock (_gate) _synth.Launch(); }

    /// <summary>The flight wind for arc progress 0..1; call every frame while flying. Immediate.</summary>
    public void Fly(float progress01) { lock (_gate) _synth.Fly(progress01); }

    /// <summary>
    /// Arrival: a warm chord and a softer chime ≈ 120 ms later. Tempo-locked (≤ 80 ms at every tempo). After a flight out of
    /// a festival world (the trails, AD-52), it also releases the build the flight played: the drop lands on the first beat
    /// at least 0.15 s later. Call it after <see cref="SetWorld"/>, as now; nothing else is needed for the drop.
    /// </summary>
    public void Arrive() { lock (_gate) _synth.Arrive(); }

    // ---------------------------------------------------------------- hazard cues (AD-52)

    /// <summary>
    /// The edge warning: call every frame while near an edge of a hazard world, <paramref name="nearness01"/> 0 (far) … 1 (at
    /// the edge). Self-throttling (a pulse every 1 s … 0.25 s on the beat grid, a rumble that follows the nearness) and cheap;
    /// silent below 0.05 and within 0.3 s of the last call.
    /// </summary>
    public void HazardWarning(float nearness01) { lock (_gate) _synth.HazardWarning(nearness01); }

    /// <summary>The ground gives way: a crack, falling shards, a drop; the music ducks deep for the fall. Immediate.</summary>
    public void FallStart() { lock (_gate) _synth.FallStart(); }

    /// <summary>The fall or spiral for progress 0..1 (a whoosh falling three octaves, circling left and right ever faster); call every frame while falling. Immediate.</summary>
    public void Fall(float progress01) { lock (_gate) _synth.Fall(progress01); }

    /// <summary>Back at the parent after a fall: an upward pop and a bright chord, and the music returns. Immediate; <see cref="Arrive"/> may follow as usual.</summary>
    public void FallReturn() { lock (_gate) _synth.FallReturn(); }

    /// <summary>Selection tick. Tempo-locked.</summary>
    public void Select() { lock (_gate) _synth.Select(); }

    /// <summary>Inspection (E). Tempo-locked.</summary>
    public void Inspect() { lock (_gate) _synth.Inspect(); }

    /// <summary>Opening a file or folder. Tempo-locked.</summary>
    public void Open() { lock (_gate) _synth.Open(); }

    /// <summary>A gentle refusal. Tempo-locked.</summary>
    public void Deny() { lock (_gate) _synth.Deny(); }

    /// <summary>A bell-flower note at scale degree <paramref name="index"/>. Tempo-locked.</summary>
    public void Chime(int index)
    {
        ChimeCount++;
        lock (_gate) _synth.Chime(index);
    }
}
