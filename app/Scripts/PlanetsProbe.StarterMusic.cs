using FileScape.Core.Audio;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-starter-music --root=sample --vis=planets</c> (AD-95, PLANET-171): the Starter Sky's chosen music
/// in the running game, and a revisit — the wiring and the playback; the choices, the switch on the next beat, the return at
/// a phrase and the loudness are Core's (SkySynthStarterThemeTests). The theme is <c>FILESCAPE_STARTER_MUSIC</c> (a
/// <see cref="StarterTheme"/> name: Classic, Revised, AuroraDrift, Homecoming) or else the game's default; it is applied
/// through the preferences, the path Settings → Music takes (automation saves nothing). Then: the Starter Sky's music
/// plays that theme's piece; a stay; Ctrl+L to the Lantern Workshop (its own music); a short stay; Backspace home, where the
/// theme resumes; a second stay. With FILESCAPE_AUDIO_RECORD set the stays are long (110 s, 25 s, 75 s: ≈ 3.6 min, set
/// FILESCAPE_AUDIO_RECORD_SECONDS ≥ 230) and each phase is logged as "MARK name at s" for cutting; otherwise 3 s, 2 s, 3 s.
/// Throughout: no audio underruns after the first sound, the voice pool never full.
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>The stays when recording for listening, seconds: on the Starter Sky, away, and back (as the Core render's revisit).</summary>
    private const double StarterFirstStaySeconds = 110, StarterAwaySeconds = 25, StarterSecondStaySeconds = 75;

    private async Task StarterMusicRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        var skips0 = P.Audio.AudibleSkips;
        _trackMusic = true;
        var root = App.Navigator.Current;
        var asked = System.Environment.GetEnvironmentVariable("FILESCAPE_STARTER_MUSIC");
        var theme = StarterThemes.FromKey(asked) ?? App.Preferences.StarterMusic;
        Check(string.IsNullOrEmpty(asked) || StarterThemes.FromKey(asked) != null, $"FILESCAPE_STARTER_MUSIC names a theme ({asked ?? "not set: the game's default"})");
        App.SetPreferences(App.Preferences with { StarterMusic = theme }); // as Settings → Music does
        var piece = StarterThemes.PieceOf(theme)?.Name;
        await Until(() => P.Audio.HubMusic().PlayingPiece == piece && P.Audio.MusicState().Playing == MusicIdentity.Hub, 3);
        var (chosen, playing) = P.Audio.HubMusic();
        var m = P.Audio.MusicState();
        Log($"the Starter Sky's music: {StarterThemes.Title(theme)} ({theme}); recording for listening: {RecordingAudio}");
        Check(chosen == theme && playing == piece && m.Playing == MusicIdentity.Hub, $"the Starter Sky plays {StarterThemes.Title(theme)}: piece {playing ?? "(generated: the classic)"}, {m.Mode} on {m.Root}, bar {m.Bar}, variation {m.Variation}");
        Mark("starter-first-stay");
        await Listen(StarterFirstStaySeconds, 3);
        m = P.Audio.MusicState();
        Log(F($"  leaving at variation {m.Variation}, bar {m.Bar}"));

        var away = Child(root, "Lantern Workshop");
        Check(away >= 0, "the Lantern Workshop is in the sample");
        if (away < 0) return;
        Mark("starter-leave");
        await GoToPath(Tree.FullPath(away));
        await Until(() => !P.Busy && App.Navigator.Current == away, 12);
        await Until(() => P.Audio.MusicState().Playing != MusicIdentity.Hub, 8);
        Check(App.Navigator.Current == away && P.Audio.MusicState().Playing != MusicIdentity.Hub, $"away in {Tree.DisplayName(away)}: its own music ({P.Audio.MusicState().Playing})");
        await Listen(StarterAwaySeconds, 2);

        Mark("starter-return");
        await Key(Godot.Key.Backspace);
        await Until(() => !P.Busy && App.Navigator.Current == root, 12);
        await Until(() => P.Audio.MusicState().Playing == MusicIdentity.Hub, 8);
        m = P.Audio.MusicState();
        Check(P.Audio.HubMusic().PlayingPiece == piece && m.Playing == MusicIdentity.Hub,
            $"back on the Starter Sky: {StarterThemes.Title(theme)} again, resumed at variation {m.Variation}, bar {m.Bar} (a written theme resumes at its next phrase: bar % 4 == 0)");
        await Listen(StarterSecondStaySeconds, 3);
        Mark("end");

        _trackMusic = false;
        Check(P.Audio.AudibleSkips == skips0, $"no audio gaps ({P.Audio.AudibleSkips - skips0} underruns; {P.Audio.Skips} counted since playback began)");
        Check(_mostVoices <= SkySynth.MaxVoices, $"voices at most {_mostVoices} of {SkySynth.MaxVoices}");
        Log($"music changes: {_musicChanges.Count}; audio {P.Audio.DiagnosticsJson()}");
    }
}
