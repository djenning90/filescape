using System.Text.RegularExpressions;
using FileScape.Core.Shell;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>PLANET-53: usable Markdown links — the render's link table and anchors, and what a clicked address means.</summary>
public class MarkdownLinksTests
{
    private const string Doc = @"\\wsl.localhost\Ubuntu-24.04\tmp\fixture\guide.md";

    [Fact]
    public void Links_are_tags_that_only_hold_an_index()
    {
        var d = MarkdownText.Render("See [the end](#the-end), [start](<docs/getting started.md>) and [web](https://example.com/a \"t\").\n\n<https://auto.example>");
        d.Links.Select(l => l.Target).Should().Equal("#the-end", "docs/getting started.md", "https://example.com/a", "https://auto.example");
        d.Links[0].Text.Should().Be("the end");
        foreach (Match m in Regex.Matches(d.Bbcode, @"\[url=([^\]]*)\]")) m.Groups[1].Value.Should().MatchRegex(@"^\d+$");
        Regex.Count(d.Bbcode, @"\[url=").Should().Be(4).And.Be(Regex.Count(d.Bbcode, @"\[/url\]"));
        d.Bbcode.Should().Contain("[url=2][color=#9cc8ff][u]web[/u][/color][/url] [color=#8899aa]↗[/color]", "a web link is marked as leaving FileScape");
        d.Bbcode.Should().NotContain("‹").And.NotContain("example.com/a", "the address is not repeated in the text");
    }

    [Theory]
    [InlineData("[x](javascript:alert(1)) [y](<a]b>) [z](\"q]\")")]
    [InlineData("[a](http://e/[url=evil]) [b](#[/url])")]
    public void Nothing_from_a_link_reaches_a_tag(string md)
    {
        var d = MarkdownText.Render(md);
        foreach (Match m in Regex.Matches(d.Bbcode, @"\[url=([^\]]*)\]")) m.Groups[1].Value.Should().MatchRegex(@"^\d+$");
        Regex.Count(d.Bbcode, @"\[url=").Should().Be(Regex.Count(d.Bbcode, @"\[/url\]"));
    }

    [Fact]
    public void The_inert_form_is_unchanged()
    {
        MarkdownText.ToBbcode("[a](https://e.com)").Should().Contain("‹https://e.com›").And.NotContain("[url");
        MarkdownText.ToBbcode("[a](<x y.md>)").Should().Contain("‹x y.md›", "the angle form is understood there too");
    }

    [Fact]
    public void Headings_have_github_anchors_at_their_paragraphs()
    {
        var md = "# Guide\n\nIntro.\n\n```\ncode\nmore\n```\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n## The `last` *section*\n\nText.\n\n## Guide\n\n> ## Quoted\n";
        var d = MarkdownText.Render(md);
        d.Anchors.Keys.Should().BeEquivalentTo(["guide", "the-last-section", "guide-1"], "a quote's headings get no anchor; a repeat is numbered");
        d.Anchors["guide"].Should().Be(0);
        // Paragraphs as RichTextLabel counts them: a line break outside a table (a code block and a table are one each).
        var top = TopLevelParagraphs(d.Bbcode);
        top[d.Anchors["the-last-section"]].Should().Contain("[b]The [code]last[/code] [i]section[/i][/b]");
        top[d.Anchors["guide-1"]].Should().Contain("[b]Guide[/b]");
    }

    /// <summary>The output split as RichTextLabel splits its paragraphs: at line breaks outside [table]…[/table].</summary>
    private static List<string> TopLevelParagraphs(string bb)
    {
        var list = new List<string>();
        var cur = new System.Text.StringBuilder();
        var depth = 0;
        for (var i = 0; i < bb.Length; i++)
        {
            if (bb[i] == '[' && string.CompareOrdinal(bb, i, "[table", 0, 6) == 0) depth++;
            if (bb[i] == '[' && string.CompareOrdinal(bb, i, "[/table]", 0, 8) == 0) depth--;
            if (bb[i] == '\n' && depth == 0) { list.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(bb[i]);
        }

        list.Add(cur.ToString());
        return list;
    }

    [Theory]
    [InlineData("Getting Started", "getting-started")]
    [InlineData("What's new? (v2.0)", "whats-new-v20")]
    [InlineData("snake_case and _emphasis_", "snake_case-and-emphasis")]
    [InlineData("[Linked](x.md) heading", "linked-heading")]
    public void Slugs_follow_github(string heading, string slug) => MarkdownLinks.Slug(heading).Should().Be(slug);

    [Theory]
    [InlineData("#the-last-section", MarkdownLinkKind.Anchor, "", "the-last-section")]
    [InlineData("docs/getting%20started.md", MarkdownLinkKind.Local, @"\\wsl.localhost\Ubuntu-24.04\tmp\fixture\docs\getting started.md", null)]
    [InlineData("docs/getting started.md", MarkdownLinkKind.Local, @"\\wsl.localhost\Ubuntu-24.04\tmp\fixture\docs\getting started.md", null)]
    [InlineData("./docs/units.md#angles", MarkdownLinkKind.Local, @"\\wsl.localhost\Ubuntu-24.04\tmp\fixture\docs\units.md", "angles")]
    [InlineData("src/", MarkdownLinkKind.Local, @"\\wsl.localhost\Ubuntu-24.04\tmp\fixture\src", null)]
    [InlineData("../../../../../x.md?raw=1", MarkdownLinkKind.Local, @"\\wsl.localhost\Ubuntu-24.04\x.md", null)]
    [InlineData("https://example.com/filescape", MarkdownLinkKind.Web, "https://example.com/filescape", null)]
    [InlineData("MAILTO:a@b.c", MarkdownLinkKind.Web, "MAILTO:a@b.c", null)]
    [InlineData("javascript:alert(1)", MarkdownLinkKind.Refused, "javascript:alert(1)", null)]
    [InlineData("file:///C:/Windows/notepad.exe", MarkdownLinkKind.Refused, "file:///C:/Windows/notepad.exe", null)]
    [InlineData("//evil.example/x", MarkdownLinkKind.Refused, "//evil.example/x", null)]
    public void Targets_resolve_against_the_document(string target, MarkdownLinkKind kind, string address, string? fragment)
    {
        var t = MarkdownLinks.Resolve(target, Doc);
        t.Kind.Should().Be(kind);
        t.Address.Should().Be(address);
        t.Fragment.Should().Be(fragment);
        if (kind == MarkdownLinkKind.Refused) t.Reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Roots_and_other_path_forms()
    {
        MarkdownLinks.Resolve("/docs/a b.md", @"C:\p\repo\docs\x.md", @"C:\p\repo").Address.Should().Be(@"C:\p\repo\docs\a b.md", "a leading / is the repository's root");
        MarkdownLinks.Resolve("../README.md", @"C:\p\repo\docs\x.md").Address.Should().Be(@"C:\p\repo\README.md");
        MarkdownLinks.Resolve(@"..\..\..\..\up.md", @"C:\p\x.md").Address.Should().Be(@"C:\up.md", "never above the drive");
        MarkdownLinks.Resolve("notes.md", "/home/u/proj/README.md").Address.Should().Be("/home/u/proj/notes.md");
        MarkdownLinks.Resolve(@"C:\elsewhere\b.md", Doc).Address.Should().Be(@"C:\elsewhere\b.md");
        MarkdownLinks.Resolve("bad%zz.md", Doc).Address.Should().EndWith(@"\bad%zz.md", "a malformed escape is kept as written");
    }
}
