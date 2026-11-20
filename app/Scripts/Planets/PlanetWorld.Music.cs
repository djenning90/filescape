using FileScape.Core.Audio;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// What the player is doing, told to the music every frame (AD-79, PLANET-98): the overview (T) plays the star chart; the
/// overlook view (standing on the overlook, at its telescope, or B) plays the Lydian overlook; anything else, the world's
/// own music, which resumes where it was. The overlook is entered after <see cref="OverlookMusicEnterSeconds"/> on it and
/// left after <see cref="OverlookMusicLeaveSeconds"/> off it, so stepping across its rim does not flip the music; the
/// overview changes at once (T is deliberate). Each experience counts its visits per folder: the second time at the same
/// place starts the piece's next variation if the synth no longer remembers where it was.
/// What a change must preserve: <see cref="SkyAudio.SetExperience"/> is only called when the experience changes (the synth
/// treats a repeat as nothing, but the lock is not free); a launch or a hop ends the overlook at once (the flight's music is
/// the world's: its build and drop, AD-52).
/// </summary>
public partial class PlanetWorld
{
    /// <summary>Seconds the overlook view must hold before its music comes in (a glance from its rim does not start it).</summary>
    private const float OverlookMusicEnterSeconds = 0.4f;

    /// <summary>Seconds off the overlook before the world's music comes back (a step back from the rim keeps the piece going).</summary>
    private const float OverlookMusicLeaveSeconds = 1.2f;

    private MusicExperience _musicExperience = MusicExperience.World;
    private float _overlookOnSeconds, _overlookOffSeconds;
    private readonly Dictionary<(int Folder, MusicExperience Experience), int> _experienceVisits = [];

    /// <summary>The experience the music was last told (probes).</summary>
    public MusicExperience MusicExperience => _musicExperience;

    /// <summary>Called every frame (surface and overview): works out the experience and tells the music when it changes.</summary>
    private void UpdateMusicExperience(float dt)
    {
        if (_current == null) return;
        var travelling = _phase != Phase.Walking || _hop != null;
        if (OnOverlook && !travelling)
        {
            _overlookOnSeconds += dt;
            _overlookOffSeconds = 0;
        }
        else
        {
            _overlookOffSeconds += dt;
            _overlookOnSeconds = 0;
        }

        var wanted = _universe != null ? MusicExperience.Overview
            : _musicExperience == MusicExperience.Overlook ? (travelling || _overlookOffSeconds >= OverlookMusicLeaveSeconds ? MusicExperience.World : MusicExperience.Overlook)
            : _overlookOnSeconds >= OverlookMusicEnterSeconds ? MusicExperience.Overlook : MusicExperience.World;
        if (wanted == _musicExperience) return;
        _musicExperience = wanted;
        var visits = 1;
        if (wanted != MusicExperience.World)
        {
            var key = (_current.FolderId, wanted);
            visits = _experienceVisits.GetValueOrDefault(key) + 1;
            _experienceVisits[key] = visits;
        }

        _audio.SetExperience(wanted, visits);
        GD.Print($"[planets] music: {wanted} (visit {visits})");
    }

    /// <summary>The music's state for the state file (AD-79): the experience told and the synth's own view.</summary>
    private string MusicStateJson() => $"\"music\":{_audio.MusicStateJson()},";
}
