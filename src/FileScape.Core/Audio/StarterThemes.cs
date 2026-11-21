namespace FileScape.Core.Audio;

/// <summary>
/// Which music the hub identity plays (PLANET-169, AD-95): the Starter Sky and This computer. <see cref="Classic"/> is
/// AD-49's generated hub music, unchanged; the others are the seventh pass's written alternatives
/// (<see cref="StarterThemeScores"/>). The numbers are stored nowhere (preferences store the name), but
/// <see cref="Classic"/> must stay 0: it is the synth's default and keeps every existing render and hash identical.
/// </summary>
public enum StarterTheme
{
    /// <summary>AD-49's hub: the FileScape theme on the bell every 16 bars, the music box and the rising journeys (DJ, 2026-11-20: "fatiguing … predictable, overly simple, somewhat cheesy despite its charm").</summary>
    Classic,

    /// <summary>The theme kept, re-harmonised and re-orchestrated; a composed answer instead of the music box; a breath one cycle in four (<see cref="StarterThemeScores.Revised"/>).</summary>
    Revised,

    /// <summary>Mixolydian, the aurora voices: slow chords over a held tonic, a syncopated pluck ostinato, a long-note lead (<see cref="StarterThemeScores.AuroraDrift"/>).</summary>
    AuroraDrift,

    /// <summary>A new long-breathed melody from the theme's rising sixth, chamber strings and harp, borrowed iv and ♭VI (<see cref="StarterThemeScores.Homecoming"/>).</summary>
    Homecoming,
}

/// <summary>
/// The catalogue of <see cref="StarterTheme"/>s (AD-95): each one's piece (null for the classic), the deck level that
/// matches its loudness to the other worlds', a name and a sentence for Settings, and the game's default. The synth asks
/// <see cref="PieceOf"/> when it configures a hub deck; the app shows <see cref="Title"/> and <see cref="Description"/>
/// and stores <see cref="Key"/>.
/// <para>What a change must preserve: <see cref="StarterTheme.Classic"/> has no piece (the synth's own hub music plays);
/// each piece's tempo is the hub's; the levels are measured, not guessed (SkySynthStarterThemeTests holds every theme
/// within 1.5 dB of the classic hub); changing <see cref="GameDefault"/> is DJ's choice by ear (AD-95).</para>
/// </summary>
public static class StarterThemes
{
    /// <summary>
    /// Where a return may pick a piece up, sixteenths into its cycle: every four bars (each piece's phrases are four bars
    /// long), so leaving the Starter Sky and coming back resumes on a phrase, never mid-note.
    /// </summary>
    public static IReadOnlyList<int> SectionStarts { get; } = [0, 64, 128, 192];

    /// <summary>
    /// The theme the game plays unless the player chose another (AD-95): Homecoming, the candidate the listening analysis
    /// ranked strongest (the most varied melody and harmony with the lowest repetition, the Overview's Dorian left alone).
    /// DJ decides by ear: Settings → Music switches at the next beat.
    /// </summary>
    public const StarterTheme GameDefault = StarterTheme.Homecoming;

    private static readonly ExperiencePiece Revised = Build("starter-revised", DiatonicMode.Ionian, StarterThemeScores.Revised);
    private static readonly ExperiencePiece AuroraDrift = Build("starter-aurora-drift", DiatonicMode.Mixolydian, StarterThemeScores.AuroraDrift);
    private static readonly ExperiencePiece Homecoming = Build("starter-homecoming", DiatonicMode.Ionian, StarterThemeScores.Homecoming);

    private static ExperiencePiece Build(string name, DiatonicMode mode, Func<int, SketchScore> variation) =>
        new(name, StarterThemeScores.Bpm, mode, [.. Enumerable.Range(0, StarterThemeScores.Variations).Select(variation)], SectionStarts);

    /// <summary>Every theme, the classic first (the order Settings lists them in).</summary>
    public static IReadOnlyList<StarterTheme> All { get; } = Enum.GetValues<StarterTheme>();

    /// <summary>The written piece a theme plays on the hub's deck, or null for <see cref="StarterTheme.Classic"/> (the synth's own hub music).</summary>
    public static ExperiencePiece? PieceOf(StarterTheme theme) => theme switch
    {
        StarterTheme.Revised => Revised,
        StarterTheme.AuroraDrift => AuroraDrift,
        StarterTheme.Homecoming => Homecoming,
        _ => null,
    };

    /// <summary>
    /// The deck gain (linear) a theme's piece plays at, so its integrated loudness sits with the classic hub's (measured
    /// 2026-11-20 on the Starter Sky's B♭, SkySynthStarterThemeTests and the listening analysis): 1 for the classic.
    /// </summary>
    public static float LevelOf(StarterTheme theme) => theme switch
    {
        StarterTheme.Revised => RevisedLevel,
        StarterTheme.AuroraDrift => AuroraDriftLevel,
        StarterTheme.Homecoming => HomecomingLevel,
        _ => 1f,
    };

    // Deck gains (linear), set from measured integrated loudness against the classic hub (see LevelOf): at unity (2026-11-20,
    // one cycle on the Starter Sky) the revised theme measured +0.1 dB, Aurora drift −5.3 dB (its aurora voices are
    // filtered and sparse) and Homecoming +1.2 dB (the string pad and the doubled bass), so the drift is lifted 5.2 dB and
    // Homecoming lowered 1.1 dB.
    private const float RevisedLevel = 1f, AuroraDriftLevel = 1.82f, HomecomingLevel = 0.88f;

    /// <summary>The name Settings shows.</summary>
    public static string Title(StarterTheme theme) => theme switch
    {
        StarterTheme.Revised => "The theme, revised",
        StarterTheme.AuroraDrift => "Aurora drift",
        StarterTheme.Homecoming => "Homecoming",
        _ => "Classic",
    };

    /// <summary>One sentence for Settings' tooltip: what the listener will hear.</summary>
    public static string Description(StarterTheme theme) => theme switch
    {
        StarterTheme.Revised => "The FileScape theme with new harmony, passed between flute, bell, violins and clarinet, answered by a composed phrase; one round in four rests.",
        StarterTheme.AuroraDrift => "Slow Mixolydian chords on the aurora synth, a gently syncopated pluck, the theme's opening as a high glass call and a long-note lead.",
        StarterTheme.Homecoming => "A new, long-breathed melody grown from the theme's opening, for strings, harp and cello, darkening through borrowed chords before it comes home.",
        _ => "The original Starter Sky music: the FileScape theme on a bell every 16 bars, with a music box and rising journeys between.",
    };

    /// <summary>The name stored in preferences (the enum's name, so a renumbering never changes a saved choice).</summary>
    public static string Key(StarterTheme theme) => theme.ToString();

    /// <summary>The theme a stored name names, or null when it names none (an older or malformed preference).</summary>
    public static StarterTheme? FromKey(string? key) => Enum.TryParse<StarterTheme>(key, ignoreCase: true, out var t) && Enum.IsDefined(t) && !int.TryParse(key, out _) ? t : null;
}
