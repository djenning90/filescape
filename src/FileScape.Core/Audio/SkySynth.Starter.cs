using FileScape.Core.Planets;

namespace FileScape.Core.Audio;

// AD-95 (PLANET-169..171): the hub identity's music is a choice. The classic (AD-49's generated hub music) is Core's
// default, so every existing render, test and behaviour-baseline hash is unchanged; the game passes the player's choice
// (Preferences.StarterMusic, default StarterThemes.GameDefault). A written starter theme plays on a hub deck exactly as the
// overview's star chart plays on its deck (ScoreStep: a sixteenth at a time, transposed into the place's root), keeps the
// hub's tempo and clock, and is remembered apart from the other themes, so switching is a world change heard on the next
// beat and a return resumes the theme that was left.
public sealed partial class SkySynth
{
    private StarterTheme _hubTheme = StarterTheme.Classic;

    /// <summary>The music the hub identity plays (AD-95): <see cref="StarterTheme.Classic"/> unless <see cref="SetHubTheme"/> chose another.</summary>
    public StarterTheme HubTheme => _hubTheme;

    /// <summary>
    /// Chooses the hub's music (AD-95). While the hub's own music is playing it changes on the next beat (a change of the
    /// player's choice is never held by the minimum dwell); otherwise it is heard the next time the hub's music begins.
    /// The same theme again does nothing.
    /// </summary>
    public void SetHubTheme(StarterTheme theme)
    {
        if (theme == _hubTheme) return;
        _hubTheme = theme;
        var (placeTheme, character, visits, score) = _place;
        if (_experience != MusicExperience.World || score != MusicIdentity.Hub) return;
        ChangeMusic(placeTheme, character, visits, score, experienceChange: true);
    }

    /// <summary>
    /// The key a hub deck plays a starter theme's piece in: the place's own root and sound (its profile), with the piece's
    /// mode as the scale, so the effects pitched while it plays (arrival, select, the flight arpeggio) agree with the music.
    /// </summary>
    private static Profile HubPieceProfile(PlanetTheme theme, DiatonicMode mode)
    {
        var p = KeyProfiles[(int)MusicIdentity.Hub][(int)theme];
        return p with { Scale = Modes.Semitones(mode), Progression = [0, 3, 4, 0], Mode = Modes.Name(mode) + " (the starter theme)" };
    }
}
