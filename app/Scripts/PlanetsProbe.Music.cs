using FileScape.App.Planets;
using FileScape.Core.Audio;
using FileScape.Core.Interaction;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-music --root=sample --vis=planets --test-shell=record</c> (AD-79, AD-85): which music plays where,
/// in the app — the wiring and the playback, not the choices (those are Core's: MusicActivityTests for activity → piece,
/// the overlook's hysteresis, travel and visits; SkySynthTests.Experience for keys, variations, returns at a section and the
/// minimum dwell through quick hops). Arriving on the Starter Sky, the world plays its own arrangement. Walking up to the
/// overlook it plays on; E up beside the telescope: the Lydian overlook in the world's key, from its intro; E again looks
/// through the telescope and the overlook plays on; Enter departs to the framed world (the flight's music is the world's)
/// and it lands to that world's music; Backspace back. T: Star Chart Dorian on the Starter Sky's root (a major place);
/// Esc: the world's music again. Throughout: no audio underruns after the first sound, the voice pool never full. With
/// FILESCAPE_AUDIO_RECORD the output is captured, the listening waits run long (≈ 2 min in all, set
/// FILESCAPE_AUDIO_RECORD_SECONDS ≥ 150) and each phase's start is logged as "MARK name at s" for cutting the samples.
/// AD-85 trimmed it from 164 s: the second climb's resume and the quick hops' dwell are Core tests now.
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>Changes of the music sounding (identity and root), seen once a frame: when, from, to, and how long the old had played.</summary>
    private readonly List<(double At, string From, string To, MusicIdentity FromId, MusicIdentity ToId, double Played)> _musicChanges = [];
    private (MusicIdentity Id, int Root) _lastMusic = (MusicIdentity.Hub, -1);
    private double _lastPlayedFor;
    private int _mostVoices;
    private bool _trackMusic;

    /// <summary>Every frame while the music journey runs: notes each change of the music sounding.</summary>
    private void TrackMusic()
    {
        if (!_trackMusic || !P.Active) return;
        var m = P.Audio.MusicState();
        _mostVoices = Math.Max(_mostVoices, m.Voices);
        var now = (m.Playing, m.Root);
        if (_lastMusic.Root >= 0 && now != _lastMusic)
        {
            _musicChanges.Add((P.Audio.RecordSeconds, $"{_lastMusic.Id}@{_lastMusic.Root}", $"{now.Playing}@{now.Root}", _lastMusic.Id, now.Playing, _lastPlayedFor));
            Log(F($"  music: {_lastMusic.Id}@{_lastMusic.Root} → {now.Playing}@{now.Root} after {_lastPlayedFor:0.00} s ({m.Mode}, bar {m.Bar}, variation {m.Variation})"));
        }

        _lastMusic = now;
        _lastPlayedFor = m.PlayingForSeconds;
    }

    /// <summary>Logs where a phase starts in the output recording (for cutting the samples).</summary>
    private void Mark(string name) => Log(F($"MARK {name} at {P.Audio.RecordSeconds:0.000}"));

    /// <summary>Waits until the music sounding is <paramref name="id"/>, at most <paramref name="seconds"/>.</summary>
    private Task UntilPlaying(MusicIdentity id, double seconds) => Until(() => P.Audio.MusicState().Playing == id, seconds);

    /// <summary>Recording for listening (FILESCAPE_AUDIO_RECORD set): the listening waits run long; otherwise they are only settle times.</summary>
    private static bool RecordingAudio => !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FILESCAPE_AUDIO_RECORD"));

    /// <summary>A wait that is long when recording for a listener (<paramref name="listenSeconds"/>) and short otherwise (<paramref name="checkSeconds"/>).</summary>
    private Task Listen(double listenSeconds, double checkSeconds) => Seconds(RecordingAudio ? listenSeconds : checkSeconds);

    private async Task MusicRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(1);
        var skips0 = P.Audio.AudibleSkips;
        _trackMusic = true;
        var root = App.Navigator.Current;
        var theme = P.Composition!.Theme;
        var character = P.Character;
        var placeId = SkySynth.IdentityOf(character);
        var key = SkySynth.KeyFor(theme, character);
        var m = P.Audio.MusicState();
        Log($"the Starter Sky: {theme}, {character}, its music {placeId} ({key.Mode}, root {key.RootMidiNote}); recording for listening: {RecordingAudio}");
        Check(m.Experience == MusicExperience.World && m.Playing == placeId && m.Root == key.RootMidiNote, $"on arrival the world plays its own arrangement as before: {m.Playing} in {m.Mode} on {m.Root}");
        Check(P.Audio.MinimumDwellSeconds == SkySynth.GameMinimumDwell.TotalSeconds, F($"the game's minimum dwell is set ({P.Audio.MinimumDwellSeconds:0.0} s; its behaviour through quick hops is Core's MusicActivityTests and SkySynthTests)"));
        Mark("world-arrival");
        await Listen(6, 0.5);

        // ---- the telescope: the approach on foot, up, the view, a departure (AD-85)
        Mark("telescope-approach");
        await ApproachOverlookOnFoot();
        Check(P.MusicExperience == MusicExperience.World && P.Audio.MusicState().Playing == placeId, "walking up to the overlook the world's music plays on");
        await Key(Godot.Key.E); // up beside the telescope
        await Until(() => P.OnOverlook && P.Walker.Grounded, 4);
        await UntilPlaying(MusicIdentity.Overlook, 3);
        m = P.Audio.MusicState();
        Check(m.Playing == MusicIdentity.Overlook && m.Root == key.RootMidiNote && m.Mode.StartsWith("lydian", StringComparison.Ordinal) && m.Variation == 0 && m.Bar <= 1,
            $"on the overlook: the Lydian overlook in the world's key ({m.Mode}, root {m.Root}), first variation, from its intro (bar {m.Bar})");
        Mark("telescope-up");
        await Listen(5, 0.3);
        await Key(Godot.Key.E);
        await Until(() => P.TelescopeViewOpen, 2);
        Mark("telescope-view");
        await Listen(8, 1.2);
        Check(P.TelescopeViewOpen && P.Audio.MusicState().Playing == MusicIdentity.Overlook, "E looks through the telescope and the overlook plays on");
        if (P.TelescopeTargetCount > 1)
        {
            await Key(Godot.Key.Right);
            await Listen(6, 0.5);
            await Key(Godot.Key.Right);
            await Listen(6, 0.5);
        }

        var go = P.TelescopeTargetId;
        Mark("telescope-depart");
        await Key(Godot.Key.Enter);
        await Until(() => P.Busy, 2);
        await Until(() => P.MusicExperience == MusicExperience.World, 2);
        Check(P.MusicExperience == MusicExperience.World, "the departure ends the overlook at once: the flight's music is the world's build and drop");
        await Until(() => !P.Busy && App.Navigator.Current == go, 12);
        var goId = SkySynth.IdentityOf(P.Character);
        await UntilPlaying(goId, 8);
        Check(App.Navigator.Current == go && P.Audio.MusicState().Playing == goId, $"landed on {Tree.DisplayName(go)}: its own music ({goId})");
        Mark("telescope-landed");
        await Listen(8, 0.5);
        await Key(Godot.Key.Backspace);
        await Until(() => !P.Busy && App.Navigator.Current == root, 12);
        await UntilPlaying(placeId, 8);
        await Listen(4, 0.5);

        // ---- the overview: Star Chart Dorian from the Starter Sky (a major place), then back (AD-85)
        Mark("overview-enter");
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 3);
        await UntilPlaying(MusicIdentity.Overview, 3);
        m = P.Audio.MusicState();
        Check(m.Playing == MusicIdentity.Overview && m.Root == key.RootMidiNote && m.Mode.StartsWith("dorian", StringComparison.Ordinal) && m.Variation == 0,
            $"in the overview: Star Chart Dorian, {m.Mode} on {m.Root} (the Starter Sky's root; the place itself is {key.Mode})");
        await Listen(24, 1);
        Mark("overview-leave");
        await Key(Godot.Key.Escape);
        await Until(() => !P.UniverseOpen, 3);
        await UntilPlaying(placeId, 4);
        Check(P.Audio.MusicState().Playing == placeId, "leaving the overview brings the world's music back");
        await Listen(10, 0.5);
        Mark("end");

        _trackMusic = false;
        Check(P.Audio.AudibleSkips == skips0, $"no audio gaps in the whole journey ({P.Audio.AudibleSkips - skips0} underruns; {P.Audio.Skips} counted since playback began)");
        Check(_mostVoices <= SkySynth.MaxVoices, $"voices at most {_mostVoices} of {SkySynth.MaxVoices}");
        Log($"music changes: {_musicChanges.Count}; audio {P.Audio.DiagnosticsJson()}");
    }

    /// <summary>[direct] 6 m out from the foot of the overlook's stones, facing them; then W walks up to the foot (a [direct] step to it if the walk is blocked).</summary>
    private async Task ApproachOverlookOnFoot()
    {
        var c = P.Composition!;
        var ov = c.Overlook;
        var r = c.Shape.Radius;
        var outward = ov.StepFoot - ov.Direction * NVector3.Dot(ov.StepFoot, ov.Direction);
        var away = NVector3.Normalize(outward - ov.StepFoot * NVector3.Dot(outward, ov.StepFoot));
        var start = PlanetFrame.Move(ov.StepFoot, away, ov.Steps[0].Radius + 6f, r);
        P.Walker.Teleport(c.Surface, start, ov.StepFoot - start);
        P.FaceCameraAlong(NVector3.Normalize(ov.StepFoot - start * NVector3.Dot(ov.StepFoot, start)));
        await Seconds(0.5);
        await WalkToward(ov.StepFoot, ov.Steps[0].Radius + 1.2f, 6);
        if (P.PromptText != UiText.Plain(PlanetWorld.TelescopeFootPrompt))
        {
            var foot = PlanetFrame.Move(ov.StepFoot, away, ov.Steps[0].Radius + 1.2f, r);
            P.Walker.Teleport(c.Surface, foot, ov.StepFoot - foot); // [direct] the walk was blocked: to the foot
            Log("  (the walk up was blocked: placed at the foot of the stones)");
            await Seconds(0.5);
        }

        await Frames(4);
    }
}
