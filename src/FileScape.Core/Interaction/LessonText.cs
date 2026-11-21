namespace FileScape.Core.Interaction;

/// <summary>
/// The Learn to play card's text (PLANET-94, AD-78): a lesson written in <see cref="UiText"/>'s markup, in the card's
/// style (<see cref="UiTextStyle.Lesson"/>). <c>*the action*</c> is the step's current action (bold, warm gold);
/// <c>{Space}</c> is a key cap; everything else is plain text and is escaped, so a lesson can never produce a tag of its
/// own. <see cref="Plain"/> gives the same text without markup (the state file, probes, a screen reader's view). Since
/// AD-93 this is a thin name for the shared formatter, kept because the card and its tests speak of lessons.
/// Preserve: the output's only tags are the ones <see cref="UiText"/> writes; an unmatched <c>*</c> or <c>{</c> is shown
/// literally; a key cap never breaks across lines.
/// </summary>
public static class LessonText
{
    /// <summary>The emphasised action's colour (the card title's warm gold).</summary>
    public const string ActionColor = UiText.StrongColor;

    /// <summary>A key cap's letters and its background (<see cref="UiText.CapColor"/>).</summary>
    public const string CapColor = UiText.CapColor, CapBackground = UiText.CapBackground;

    /// <summary>A key cap's font size, pixels at UI scale 1: slightly smaller than the card's 18 px text.</summary>
    public static int CapFontPixels => UiTextStyle.Lesson.CapPixels;

    /// <summary>The lesson as BBCode (see the class summary for the markup).</summary>
    public static string ToBbcode(string lesson) => UiText.ToBbcode(lesson, UiTextStyle.Lesson);

    /// <summary>The lesson as plain text: actions unmarked, key caps as their names.</summary>
    public static string Plain(string lesson) => UiText.Plain(lesson);

    /// <summary>The key names written as caps in <paramref name="lesson"/>, in order.</summary>
    public static IReadOnlyList<string> Keys(string lesson) => UiText.Keys(lesson);
}
