using FileScape.App.Planets;
using FileScape.Core.Audio;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-music --root=sample --vis=planets --test-shell=record</c> (AD-79, PLANET-97/98): which music plays
/// where, in the app. Arriving on the Starter Sky, the world plays its character's arrangement as before (the identity
/// <see cref="SkySynth.IdentityOf"/> gives, in its key). Up to the telescope (E at the foot of the stones): the Lydian
/// overlook, in the world's key, from its intro, first variation; E at the telescope keeps it; off the overlook the world's
/// music returns. T: the star chart in the mode the scheme picks for this place; T again: the world's music. A second climb
/// resumes the overlook at a section. Quick hops (G to a child and Backspace back, as fast as the app allows): the music
/// changes only after the playing music has sounded the minimum dwell, and ends on the world you stand on. Throughout: no
/// audio underruns after the first sound, the voice pool never full. With FILESCAPE_AUDIO_RECORD (and
/// FILESCAPE_AUDIO_RECORD_SECONDS ≥ 330) the output is captured and each phase's start is logged as "MARK name at s".
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

    private async Task MusicRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(2);
        var skips0 = P.Audio.AudibleSkips;
        _trackMusic = true;
        var root = App.Navigator.Current;
        var theme = P.Composition!.Theme;
        var character = P.Character;
        var placeId = SkySynth.IdentityOf(character);
        var key = SkySynth.KeyFor(theme, character);
        var m = P.Audio.MusicState();
        Log($"the Starter Sky: {theme}, {character}, its music {placeId} ({key.Mode}, root {key.RootMidiNote})");
        Check(m.Experience == MusicExperience.World && m.Playing == placeId && m.Root == key.RootMidiNote, $"on arrival the world plays its own arrangement as before: {m.Playing} in {m.Mode} on {m.Root}");
        Mark("world-arrival");
        await Seconds(8);

        // ---- the overlook and its telescope
        Mark("overlook-climb");
        await ClimbToOverlook();
        await UntilPlaying(MusicIdentity.Overlook, 3);
        m = P.Audio.MusicState();
        Check(m.Playing == MusicIdentity.Overlook && m.Root == key.RootMidiNote && m.Mode.StartsWith("lydian", StringComparison.Ordinal) && m.Variation == 0 && m.Bar <= 1,
            $"on the overlook: the Lydian overlook in the world's key ({m.Mode}, root {m.Root}), first variation, from its intro (bar {m.Bar})");
        Mark("overlook");
        await Seconds(6);
        await Key(Godot.Key.E);
        await Until(() => P.ListOpen, 3);
        Check(P.ListOpen && P.Audio.MusicState().Playing == MusicIdentity.Overlook, "E at the telescope opens the list and the overlook plays on");
        await Seconds(40); // the rest of its 46 s cycle, for the capture
        await Key(Godot.Key.Escape);
        await Frames(4);
        Mark("overlook-leave");
        LeaveOverlook();
        await UntilPlaying(placeId, 4);
        m = P.Audio.MusicState();
        Check(m.Playing == placeId && m.Root == key.RootMidiNote && P.MusicExperience == MusicExperience.World, $"off the overlook the world's music returns: {m.Playing} ({m.Mode}, bar {m.Bar})");
        await Seconds(6);

        // ---- the overview
        Mark("overview");
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 3);
        await UntilPlaying(MusicIdentity.Overview, 3);
        m = P.Audio.MusicState();
        var mode = SkySynth.ExperienceModeFor(MusicIdentity.Overview, theme, character);
        Check(m.Playing == MusicIdentity.Overview && m.Root == key.RootMidiNote && m.Mode.StartsWith(mode.ToString().ToLowerInvariant(), StringComparison.Ordinal) && m.Variation == 0,
            $"in the overview: the star chart, {m.Mode} on {m.Root} (the scheme's mode here: {mode})");
        await Seconds(41);
        Mark("overview-close");
        await Key(Godot.Key.T);
        await Until(() => !P.UniverseOpen, 3);
        await UntilPlaying(placeId, 4);
        Check(P.Audio.MusicState().Playing == placeId, "closing the overview brings the world's music back");
        await Seconds(5);

        // ---- the overlook again: a return resumes it at a section
        Mark("overlook-again");
        await ClimbToOverlook();
        await UntilPlaying(MusicIdentity.Overlook, 3);
        await Seconds(0.3);
        m = P.Audio.MusicState();
        Check(m.Playing == MusicIdentity.Overlook && m.Bar is 2 or 3 or 10 or 11 or 14 or 15, $"a second climb picks the overlook up at a section, not its intro (bar {m.Bar}, variation {m.Variation})");
        await Seconds(10);
        LeaveOverlook();
        await UntilPlaying(placeId, 4);
        await Seconds(4);

        // ---- quick hops: the minimum dwell
        Mark("hops");
        var kids = P.Composition!.Launchers.Where(l => !l.IsParent && Tree.Contains(l.TargetId)).Select(l => l.TargetId).Take(3).ToList();
        var changes0 = _musicChanges.Count;
        var arrivals = new List<double>();
        foreach (var k in kids)
        {
            App.Selection.Select(k); // [direct] as a click on its world
            await Frames(2);
            await Key(Godot.Key.G);
            await Until(() => App.Navigator.Current == k && !P.Busy, 15);
            arrivals.Add(P.Audio.RecordSeconds);
            await Key(Godot.Key.Backspace);
            await Until(() => App.Navigator.Current == root && !P.Busy, 15);
            arrivals.Add(P.Audio.RecordSeconds);
        }

        Log("  arrivals " + string.Join(", ", arrivals.Select((a, i) => F($"{(i == 0 ? 0 : a - arrivals[i - 1]):0.00}"))) + " s apart");
        await Seconds(10);
        Mark("hops-end");
        var hops = _musicChanges.Skip(changes0).ToList();
        var held = hops.Where(c => !SkySynth.IsExperience(c.FromId) && !SkySynth.IsExperience(c.ToId) && c.FromId != MusicIdentity.Hazard && c.ToId != MusicIdentity.Hazard).ToList();
        Check(held.Count >= 1 && held.All(c => c.Played >= SkySynth.GameMinimumDwell.TotalSeconds - 0.06),
            F($"through {arrivals.Count} quick arrivals the music changed {held.Count} times, each after the playing music had sounded ≥ {SkySynth.GameMinimumDwell.TotalSeconds} s ({string.Join(", ", held.Select(c => $"{c.Played:0.0}"))} s)"));
        m = P.Audio.MusicState();
        Check(App.Navigator.Current == root && m.Playing == placeId && m.Root == key.RootMidiNote && !m.Held, $"and it ends on the world you stand on: {m.Playing} on {m.Root}");

        _trackMusic = false;
        Check(P.Audio.AudibleSkips == skips0, $"no audio gaps in the whole journey ({P.Audio.AudibleSkips - skips0} underruns; {P.Audio.Skips} counted since playback began)");
        Check(_mostVoices <= SkySynth.MaxVoices, $"voices at most {_mostVoices} of {SkySynth.MaxVoices}");
        Log($"music changes: {_musicChanges.Count}; audio {P.Audio.DiagnosticsJson()}");
    }

    /// <summary>[direct] to the foot of the overlook's stones, facing them; then E lifts you up beside the telescope (PLANET-73).</summary>
    private async Task ClimbToOverlook()
    {
        var c = P.Composition!;
        var ov = c.Overlook;
        var r = c.Shape.Radius;
        var outward = ov.StepFoot - ov.Direction * NVector3.Dot(ov.StepFoot, ov.Direction);
        var foot = PlanetFrame.Move(ov.StepFoot, NVector3.Normalize(outward - ov.StepFoot * NVector3.Dot(outward, ov.StepFoot)), ov.Steps[0].Radius + 1.2f, r);
        P.Walker.Teleport(c.Surface, foot, ov.StepFoot - foot);
        P.FaceCameraAlong(NVector3.Normalize(ov.StepFoot - foot * NVector3.Dot(ov.StepFoot, foot)));
        await Seconds(0.8);
        await Key(Godot.Key.E);
        await Until(() => P.OnOverlook && P.Walker.Grounded, 4);
        Check(P.OnOverlook, "E at the foot of the stones: up on the overlook");
    }

    /// <summary>[direct] 14 m out from the overlook, on the arrival's side (as walking off it and away).</summary>
    private void LeaveOverlook()
    {
        var c = P.Composition!;
        var ov = c.Overlook;
        var away = NVector3.UnitY - ov.Direction * NVector3.Dot(NVector3.UnitY, ov.Direction);
        var start = PlanetFrame.Move(ov.Direction, NVector3.Normalize(away), ov.Radius + 14f, c.Shape.Radius);
        P.Walker.Teleport(c.Surface, start, ov.Direction - start);
    }
}
