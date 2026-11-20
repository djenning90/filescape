using FileScape.Core.Interaction;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>The Learn to play card's markup (PLANET-94): actions emphasised, keys as caps, nothing else becomes a tag.</summary>
public class LessonTextTests
{
    [Fact]
    public void Actions_and_keys_become_bbcode()
    {
        var b = LessonText.ToBbcode("*Jump:* press {Space}.");
        b.Should().StartWith($"[b][color={LessonText.ActionColor}]Jump:[/color][/b] press ");
        b.Should().Contain($"[bgcolor={LessonText.CapBackground}][color={LessonText.CapColor}][font_size={LessonText.CapFontPixels}] Space [/font_size][/color][/bgcolor].");
    }

    [Fact]
    public void Plain_text_and_keys_read_without_markup()
    {
        LessonText.Plain("*Go back:* press {Backspace}.").Should().Be("Go back: press Backspace.");
        LessonText.Keys("press {J}, then {↓} and {Enter}").Should().Equal("J", "↓", "Enter");
    }

    [Fact]
    public void A_cap_never_breaks_and_brackets_stay_text()
    {
        LessonText.ToBbcode("{wheel ↓}").Should().Contain("wheel ↓");
        LessonText.ToBbcode("a [url=x] tag").Should().Be("a [lb]url=x] tag");
        LessonText.ToBbcode("{[b]}").Should().Contain("[lb]b]");
    }

    [Fact]
    public void Unmatched_marks_are_shown_literally()
    {
        LessonText.ToBbcode("5 * 3 and {open").Should().Be("5 * 3 and {open");
        LessonText.Plain("*unclosed").Should().Be("*unclosed");
    }
}
