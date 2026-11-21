using Godot;

namespace FileScape.App;

/// <summary>
/// Sound off and on (PLANET-47, AD-60): one state for the toolbar's sound button and N (both in the planets: the surface
/// and the overview), saved in <see cref="Core.Interaction.Preferences.SoundMuted"/> and applied to the planets'
/// <see cref="Planets.SkyAudio"/>, the only thing in FileScape that makes sound (the legacy views are silent and do not
/// show the button; the state carries across).
/// A mute restored from the preferences is made obvious: the amber "Sound off" button from the first frame and a toast
/// shortly after start. A change must keep the button, N and the saved preference one state. The Starter Sky's music
/// (<see cref="Core.Interaction.Preferences.StarterMusic"/>, AD-95) is applied here too: it is the other sound preference.
/// </summary>
public partial class Main
{
    /// <summary>How long after start the "sound is off" reminder appears, seconds: after the first frames settle (rationale for 2 s not recorded beyond that).</summary>
    private const double RestoredMuteToastDelaySeconds = 2.0;

    /// <summary>How long the restored-mute reminder stays, seconds (long enough to read twice).</summary>
    private const double RestoredMuteToastSeconds = 7.0;

    /// <summary>Whether the sound is off (the saved preference).</summary>
    public bool SoundMuted => _prefs.SoundMuted;

    /// <summary>Wires the button and N to <see cref="ToggleSound"/> (called once the planets exist, before the preferences load).</summary>
    private void SetupSound()
    {
        _hud.SoundToggled += ToggleSound;
        _planets.SoundToggleRequested += ToggleSound;
    }

    /// <summary>Sound off ↔ on: saves the preference (which applies it), and a toast says how to undo it.</summary>
    public void ToggleSound()
    {
        SetPreferences(_prefs with { SoundMuted = !_prefs.SoundMuted });
        _hud.Toast(_prefs.SoundMuted ? "Sound off. Click 🔇 Sound off at the top, or press N, to turn it back on." : "Sound on.");
    }

    /// <summary>
    /// The preference onto the audio and the toolbar; called by ApplyPreferences whenever preferences change.
    /// <paramref name="loaded"/>: they were just loaded at start, so a restored mute also gets its reminder toast.
    /// </summary>
    private void ApplySound(bool loaded)
    {
        if (_planets?.Audio == null) return; // before the planets exist (they are built first; see SetupSound)
        _planets.Audio.SetMuted(_prefs.SoundMuted);
        _planets.Audio.SetHubTheme(_prefs.StarterMusic); // AD-95: heard on the next beat if the hub is playing
        _hud.SetSoundMuted(_prefs.SoundMuted);
        if (!loaded || !_prefs.SoundMuted) return;
        GetTree().CreateTimer(RestoredMuteToastDelaySeconds).Timeout += () =>
        {
            if (_prefs.SoundMuted) _hud.Toast("Sound is off (as you left it). Click 🔇 Sound off at the top, or press N, to turn it on.", new Color(1f, 0.85f, 0.55f), RestoredMuteToastSeconds);
        };
    }
}
