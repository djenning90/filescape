using System.Text.RegularExpressions;
using FileScape.Core.Shell;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>PLANET-44: Markdown rendered for the README views, inert (DEV-02): only the converter's own tags, nothing fetched or followed.</summary>
public class MarkdownTextTests
{
    /// <summary>The only tags the converter writes; anything else in the output would be markup from a file.</summary>
    private static readonly HashSet<string> OwnTags = ["lb", "b", "i", "u", "s", "code", "color", "font_size", "indent", "table", "cell"];

    private static void OnlyOwnTags(string bb)
    {
        foreach (Match m in Regex.Matches(bb, @"\[/?([a-z_]+)[^\]]*\]"))
        {
            OwnTags.Should().Contain(m.Groups[1].Value, $"“{m.Value}” must come from the converter");
        }

        // Every tag opened is closed (lb stands alone).
        foreach (var t in OwnTags.Where(t => t != "lb"))
        {
            Regex.Count(bb, $@"\[{t}[=\] ]").Should().Be(Regex.Count(bb, $@"\[/{t}\]"), $"[{t}] is balanced");
        }
    }

    [Fact]
    public void Blocks_render_headings_lists_code_tables_quotes_and_rules()
    {
        const string md = """
            Lantern
            =======

            A *small* library with **bold** ideas, ~~old~~ new `code()` and snake_case_names.
            It continues on a second line.

            ## Build

            ```bash
            dotnet build Lantern.sln
            # [not a heading] **not bold**
            ```

                indented code
                stays code

            1. First
            2. Second
               - nested *bullet*
                 - deeper
            3) Third

            - [ ] todo
            - [x] done

            > A quote with a [link](https://example.com/a "title").

            | Name | Role |
            |------|:----:|
            | `src` | the source \| code |

            ---
            Sub-heading
            -----------
            ![remote logo](https://example.com/logo.png) and ![](local.png) and <https://example.com/auto>
            """;
        var bb = MarkdownText.ToBbcode(md);
        bb.Should().Contain("[font_size=26][b]Lantern[/b][/font_size]", "setext level 1");
        bb.Should().Contain("[font_size=22][b]Build[/b][/font_size]");
        bb.Should().Contain("[font_size=22][b]Sub-heading[/b][/font_size]", "setext level 2");
        bb.Should().Contain("A [i]small[/i] library with [b]bold[/b] ideas, [s]old[/s] new [code]code()[/code] and snake_case_names. It continues on a second line.", "soft breaks join a paragraph; underscores inside words stay");
        bb.Should().Contain("[code]dotnet build Lantern.sln\n# [lb]not a heading] **not bold**[/code]", "code is literal");
        bb.Should().Contain("[code]indented code\nstays code[/code]");
        bb.Should().Contain(" 1. First").And.Contain(" 2. Second").And.Contain(" 3) Third");
        bb.Should().Contain("[indent] ◦ nested [i]bullet[/i][/indent]").And.Contain("[indent][indent] ▪ deeper[/indent][/indent]");
        bb.Should().Contain(" ☐ todo").And.Contain(" ☑ done");
        bb.Should().Contain("[indent][color=#b4c0d8][i]A quote with a [color=#9cc8ff][u]link[/u][/color] [color=#8899aa]‹https://example.com/a›[/color].[/i][/color][/indent]");
        bb.Should().Contain("[table=2]").And.Contain("[b]Name[/b]").And.Contain("[code]src[/code]").And.Contain("the source | code");
        bb.Should().Contain("────────");
        bb.Should().Contain("(image: remote logo)").And.Contain("(image: no description)").And.NotContain("logo.png").And.NotContain("local.png");
        bb.Should().Contain("[u]https://example.com/auto[/u]");
        OnlyOwnTags(bb);
    }

    [Theory]
    [InlineData("[color=red]x[/color] [url=https://e]y[/url] [img]https://e/i.png[/img]")]
    [InlineData("**[b]** _[i]_ `[code]` [x](javascript:alert(1)) ![a](http://e/[img])")]
    [InlineData("# [font_size=99]Big[/font_size]\n- [table=1][cell]c[/cell][/table]\n> [indent]q")]
    [InlineData("```\n[/code][url]x[/url]\n```\nunclosed ``` [b] *")]
    public void Nothing_in_a_file_becomes_markup(string md)
    {
        var bb = MarkdownText.ToBbcode(md);
        OnlyOwnTags(bb);
        bb.Should().NotContain("[url").And.NotContain("[img").And.NotContain("[font_size=99");
        MarkdownText.PlainToBbcode(md).Should().NotContain("[color").And.Contain("[lb]");
    }

    [Fact]
    public void A_real_long_readme_renders_every_heading_and_stays_inert()
    {
        // This repository's own README and requirements: long, real Markdown with headings, lists, code and tables.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FileScape.sln"))) dir = dir.Parent;
        dir.Should().NotBeNull("the tests run inside the repository");
        foreach (var name in new[] { "README.md", Path.Combine("docs", "requirements.md"), Path.Combine("docs", "decisions.md") })
        {
            var text = File.ReadAllText(Path.Combine(dir!.FullName, name));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var bb = MarkdownText.ToBbcode(text);
            sw.Elapsed.TotalSeconds.Should().BeLessThan(2, $"{name} converts quickly");
            OnlyOwnTags(bb);
            foreach (var h in text.Split('\n').Where(l => Regex.IsMatch(l, @"^#{1,6} ")).Take(40))
            {
                var title = h.TrimStart('#').Trim();
                if (title.Contains('`') || title.Contains('*') || title.Contains('[') || title.Contains('_')) continue; // formatted titles: checked above
                bb.Should().Contain($"[b]{title}[/b][/font_size]", $"{name}: the heading “{title}”");
            }
        }
    }
}
