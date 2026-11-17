using Godot;

namespace FileScape.App.Ui;

/// <summary>
/// The toolbar's sound button (PLANET-47, AD-60): one click silences FileScape while it stays open, one more brings the
/// sound back; N does the same in the planets. Sound on, it is a short "🔊 Sound" (the speaker glyph alone rendered too small
/// to read in the 2026-11-16 screenshots) so the crowded top bar still fits at 125 % UI scale; sound off, it becomes an
/// amber "🔇 Sound off", which is the persistent muted indicator: it stays in
/// the top bar, on the surface and in the overview, for as long as the sound is off. Its scope is the planets, the only
/// experience with sound: the legacy views are silent and their top bar is fuller (at 125 % the Places bar overflowed by
/// 9 px with the button), so there it is hidden and the state simply carries back (SetExperience). The button only reports the click
/// (<see cref="SoundToggled"/>); Main owns the state (saved in Preferences) and calls <see cref="SetSoundMuted"/>.
/// <para>A change must keep: one action each way, the key named in the tooltip, the muted look unmistakable at a glance.</para>
/// </summary>
public partial class Hud
{
    /// <summary>The sound button was clicked: Main toggles the sound (the same as N).</summary>
    public event Action? SoundToggled;

    private Button _soundButton = null!;

    /// <summary>The muted indicator's amber (the caution colour of the HUD's other warnings).</summary>
    private static readonly Color SoundOffColor = new(1f, 0.72f, 0.3f);

    /// <summary>The button's text with the sound on: the speaker and one word.</summary>
    private const string SoundOnText = "🔊 Sound";

    /// <summary>The button's text with the sound off: the struck-through speaker and the words, so it reads without the icon.</summary>
    private const string SoundOffText = "🔇 Sound off";

    public Rect2 SoundButtonRect => _soundButton.GetGlobalRect();
    public string SoundButtonText => _soundButton.Text;
    public string SoundButtonTooltip => _soundButton.TooltipText;
    public bool SoundButtonVisible => _soundButton.IsVisibleInTree();

    /// <summary>The toast's text while one is shown, else empty (probes).</summary>
    public string ShownToast => _toastPanel.Visible ? _toast.Text : "";

    /// <summary>The top bar's rectangle (viewport units), for the probes' fit checks.</summary>
    public Rect2 TopBarRect => _topBar.GetGlobalRect();

    /// <summary>Adds the sound button to the top bar (after Help).</summary>
    private void BuildSound(HBoxContainer topRow)
    {
        _soundButton = MakeButton(SoundOnText, () => SoundToggled?.Invoke(), "");
        topRow.AddChild(_soundButton);
        SetSoundMuted(false);
    }

    /// <summary>Shows the sound's state: the compact speaker when on, the amber "Sound off" indicator when off.</summary>
    public void SetSoundMuted(bool muted)
    {
        _soundButton.Text = muted ? SoundOffText : SoundOnText;
        _soundButton.TooltipText = muted
            ? "Sound is off: music and every sound effect are silent. Click, or press N, to turn the sound back on."
            : "Sound on. Click, or press N, to silence FileScape (music and sound effects) while it stays open. Saved for next time.";
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" })
        {
            if (muted) _soundButton.AddThemeColorOverride(state, SoundOffColor);
            else _soundButton.RemoveThemeColorOverride(state);
        }
    }
}
