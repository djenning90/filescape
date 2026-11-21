using FileScape.Core.Interaction;

namespace FileScape.Core.Tests;

/// <summary>The shared interface-text formatter (PLANET-151..153, AD-93): its marks, its plain form, and that literal
/// text put in with <see cref="UiText.Lit"/> or <see cref="UiText.Escape"/> is always shown as written.</summary>
public class UiTextTests
{
    private static readonly HashSet<string> OwnTags = ["b", "color", "bgcolor", "font_size"];

    [Fact]
    public void Caps_emphasis_quiet_text_and_headings_become_bbcode()
    {
        var b = UiText.ToBbcode("# Moving\n*Run:* {W} or {↑} ~(Shift faster)~\n- one", UiTextStyle.Panel);
        b.Should().StartWith($"[font_size={UiTextStyle.Panel.HeadingPixels}][b][color={UiText.StrongColor}]Moving[/color][/b][/font_size]\n");
        b.Should().Contain($"[b][color={UiText.StrongColor}]Run:[/color][/b]");
        b.Should().Contain($"[bgcolor={UiText.CapBackground}][color={UiText.CapColor}][font_size={UiTextStyle.Panel.CapPixels}] W [/font_size][/color][/bgcolor]");
        b.Should().Contain($"[color={UiText.MutedColor}](Shift faster)[/color]");
        b.Should().EndWith("\n  • one");
        UiText.Plain("# Moving\n*Run:* {W} or {↑} ~(Shift faster)~").Should().Be("Moving\nRun: W or ↑ (Shift faster)");
    }

    [Fact]
    public void Plain_is_what_the_label_shows()
    {
        foreach (var m in new[] { "{E} · up to the telescope", "*a* {Space}, {Space} ~b~", "# H\n## S\n- x", "5 * 3 {open", "\\{x\\}" })
        {
            // The caps' padding (no-break spaces) is the only difference.
            BbcodeReading.Shown(UiText.ToBbcode(m, UiTextStyle.Prompt)).Replace(" ", "").Should().Be(UiText.Plain(m).Replace(" ", ""), m);
        }
    }

    [Fact]
    public void A_prompts_plain_text_is_unchanged_by_its_caps()
    {
        // The prompt strings the probes compare (PlanetWorld's constants) read the same with caps as before them.
        UiText.Plain("{E} · up to the telescope   ·   or climb the stones").Should().Be("E · up to the telescope   ·   or climb the stones");
        UiText.Plain("{E} or twirl ({Space}, {Space}) · look through the telescope").Should().Be("E or twirl (Space, Space) · look through the telescope");
        UiText.Keys("{↑} {↓} choose · {Enter}").Should().Equal("↑", "↓", "Enter");
    }

    [Fact]
    public void Unmatched_marks_are_shown_literally()
    {
        UiText.Plain("5 * 3 and {open ~").Should().Be("5 * 3 and {open ~");
        UiText.ToBbcode("5 * 3 and {open", UiTextStyle.Panel).Should().Be("5 * 3 and {open");
        UiText.Plain("a\\*b\\* \\{c\\}").Should().Be("a*b* {c}");
    }

    [Theory]
    [MemberData(nameof(LiteralTextTests.HostileNames), MemberType = typeof(LiteralTextTests))]
    public void Literal_text_inside_markup_is_shown_exactly_and_makes_no_tags(string name)
    {
        var markup = $"{{E}} · look closer at {UiText.Lit(name)}   ·   *{UiText.Lit(name)}* {UiText.Key(name)}";
        var (shown, tags) = BbcodeReading.Read(UiText.ToBbcode(markup, UiTextStyle.Prompt));
        shown.Should().Be($" E  · look closer at {name}   ·   {name}  {name.Replace(' ', ' ')} ");
        tags.Where(t => !OwnTags.Contains(t)).Should().BeEmpty();
        UiText.Plain(markup).Should().Be($"E · look closer at {name}   ·   {name} {name}");
        UiText.Keys(markup).Should().Equal("E", name);
    }

    [Theory]
    [MemberData(nameof(LiteralTextTests.HostileNames), MemberType = typeof(LiteralTextTests))]
    public void A_literal_at_the_start_of_a_line_is_never_a_heading_or_bullet(string name)
    {
        UiText.Plain(UiText.Lit(name)).Should().Be(name);
        BbcodeReading.Shown(UiText.ToBbcode(UiText.Lit(name), UiTextStyle.Panel)).Should().Be(name);
        BbcodeReading.Shown(UiText.Escape(name)).Should().Be(name);
    }

    [Fact]
    public void Long_names_are_shortened_in_the_middle()
    {
        UiText.Shorten("short.txt", 20).Should().Be("short.txt");
        var s = UiText.Shorten("a-very-long-name-for-a-holiday-photograph-2026.jpeg", 24);
        s.Length.Should().Be(24);
        s.Should().StartWith("a-very-long-name").And.EndWith("6.jpeg").And.Contain("…");
    }
}
