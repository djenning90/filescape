using FileScape.Core.Audio;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// What the player is doing, told to the music every frame (AD-79, AD-85): the overview (T) plays Star Chart Dorian; the
/// overlook view (standing on the overlook, looking through its telescope, or B) plays the Lydian overlook; anything else,
/// the world's own music, which resumes where it was. The decision — the overlook's hysteresis, the telescope at once, travel
/// ending the overlook, the visits per place — is Core's <see cref="MusicActivity"/> (tested frame by frame in
/// MusicActivityTests); this file only describes the frame to it and passes a change on.
/// What a change must preserve: <see cref="SkyAudio.SetExperience"/> is only called when the experience changes (the synth
/// treats a repeat as nothing, but the lock is not free).
/// </summary>
public partial class PlanetWorld
{
    private readonly MusicActivity _musicActivity = new();

    /// <summary>The experience the music was last told (probes).</summary>
    public MusicExperience MusicExperience => _musicActivity.Current;

    /// <summary>Called every frame (surface and overview): describes the frame and tells the music when the experience changes.</summary>
    private void UpdateMusicExperience(float dt)
    {
        if (_current == null) return;
        var travelling = _phase != Phase.Walking || _hop != null;
        var change = _musicActivity.Update(Core.Units.Durations.FromSeconds(dt),
            new MusicActivity.Moment(_current.FolderId, InOverview: _universe != null, OnOverlook: OnOverlook, AtTelescope: _scope != null, Travelling: travelling));
        if (change is not { } c) return;
        _audio.SetExperience(c.Experience, c.Visits);
        GD.Print($"[planets] music: {c.Experience} (visit {c.Visits})");
    }

    /// <summary>The music's state for the state file (AD-79): the experience told and the synth's own view.</summary>
    private string MusicStateJson() => $"\"music\":{_audio.MusicStateJson()},";
}
