namespace FileScape.App.Planets;

/// <summary>
/// N in the planets (on the surface and in the overview, PLANET-47): asks the session to toggle the sound, so the key and
/// the toolbar's sound button are one control with one saved state (Main owns it). Without a listener (a world used on
/// its own) N toggles this world's audio directly, as before.
/// </summary>
public partial class PlanetWorld
{
    /// <summary>N was pressed: the session toggles the sound (Main.Sound.cs) and updates the toolbar.</summary>
    public event Action? SoundToggleRequested;

    private void RequestSoundToggle()
    {
        if (SoundToggleRequested != null)
        {
            SoundToggleRequested();
            return;
        }

        _audio.ToggleMute();
        _s.Toast(_audio.Muted ? "Sound off (N turns it on)." : "Sound on.");
    }
}
