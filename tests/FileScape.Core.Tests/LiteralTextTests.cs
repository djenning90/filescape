using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Shell;

namespace FileScape.Core.Tests;

/// <summary>
/// A reference reading of Godot's BBCode for tests (PLANET-152): what a RichTextLabel shows (<c>[lb]</c> → '[',
/// <c>[rb]</c> → ']', every other bracketed run is a tag and shows nothing) and which tags it saw. Godot's own parser
/// behaves the same for these purposes; what it does with an unknown tag (shows it) only makes this reading stricter.
/// </summary>
public static class BbcodeReading
{
    /// <summary>The text a reader sees and the tag names (without '/' and arguments) the label would act on.</summary>
    public static (string Shown, List<string> Tags) Read(string bbcode)
    {
        var shown = new System.Text.StringBuilder();
        var tags = new List<string>();
        for (var i = 0; i < bbcode.Length; i++)
        {
            if (bbcode[i] != '[') { shown.Append(bbcode[i]); continue; }
            var end = bbcode.IndexOf(']', i + 1);
            if (end < 0) { shown.Append(bbcode[i..]); break; }
            var tag = bbcode[(i + 1)..end];
            if (tag == "lb") shown.Append('[');
            else if (tag == "rb") shown.Append(']');
            else tags.Add(tag.TrimStart('/').Split(' ', '=')[0]);
            i = end;
        }

        return (shown.ToString(), tags);
    }

    /// <summary>The text shown.</summary>
    public static string Shown(string bbcode) => Read(bbcode).Shown;
}

/// <summary>
/// Filenames and filesystem text render literally wherever they reach a BBCode label (PLANET-152, AD-93): every entry
/// point the app calls, with names that look like BBCode, Markdown, HTML and FileScape's own interface markup.
/// </summary>
public class LiteralTextTests
{
    /// <summary>Names that resemble markup. Each must be shown exactly as written. (Windows forbids some of these
    /// characters, WSL and other filesystems do not; the text paths must not care.)</summary>
    public static readonly string[] Hostile =
    [
        "[b]bold[/b]", "[url=https://example.com]click[/url]", "[color=red]red", "[lb]", "[rb]", "[", "]", "[[x]]",
        "[img]C:/a.png[/img]", "[font_size=90]big", "[/b]", "a & b; c", "<b>html</b>", "&amp;", "{Space}", "{E} or twirl",
        "*star*", "~quiet~", "\\back\\slash\\", "# heading", "## sub", "- dash", "**md**", "`code`", "[link](http://x)", "_u_",
        "[center]centred[/center]", "plain name.txt",
    ];

    public static IEnumerable<object[]> HostileNames() => Hostile.Select(n => new object[] { n });

    /// <summary>The hostile names a file or folder can really have: no '/' (no filesystem allows one in a name), plus
    /// bracket names without it.</summary>
    public static IEnumerable<object[]> HostileFileNames() =>
        Hostile.Where(n => !n.Contains('/')).Concat(["[b]bold", "[url=https:x]click", "[img]a.png", "[i]", "x[b]y.[b]"]).Select(n => new object[] { n });

    // ------------------------------------------------------------------ the legacy views' inspector

    [Theory]
    [MemberData(nameof(HostileFileNames))]
    public void The_inspector_shows_a_files_name_path_and_extension_literally(string stem)
    {
        var name = stem + ".x" + stem; // the extension is filesystem text too (it was shown unescaped, 2026-11-20)
        var tree = new Spec("root").File(name, 1234).Build();
        var file = tree.Find(name);
        var bb = InspectorText.Describe(file, tree, SizeScale.Classic);
        var shown = BbcodeReading.Shown(bb);
        shown.Should().Contain(tree.FullPath(file.Id), "the full path is shown as written");
        shown.Should().Contain(System.IO.Path.GetExtension(name), "the type row's extension is shown as written");
    }

    [Theory]
    [MemberData(nameof(HostileFileNames))]
    public void The_inspector_shows_a_folders_name_and_its_error_literally(string name)
    {
        var tree = new Spec("root").Dir(new Spec(name)).Build();
        var dir = tree.Find(name);
        var shown = BbcodeReading.Shown(InspectorText.Describe(dir, tree, SizeScale.Classic));
        shown.Should().Contain(tree.FullPath(dir.Id));
    }

    // ------------------------------------------------------------------ the planets' trip panel

    [Theory]
    [MemberData(nameof(HostileNames))]
    public void The_trip_panel_shows_every_folder_name_literally(string name)
    {
        BbcodeReading.Shown(TripText.Progress([name, "b", name], 1, stopRequested: true)).Should().Contain($"Trip to {name}").And.Contain($"{name} › b › {name}");
        foreach (var outcome in new[] { "arrived", "stopped", "unavailable", "refused" })
        {
            var shown = BbcodeReading.Shown(TripText.Outcome(outcome, name, name, 3, 2, name));
            shown.Should().Contain($"at {name}", outcome);
            shown.Should().Contain($"back to {name}", $"{outcome}: Backspace's destination is a folder name too (it was shown unescaped, 2026-11-20)");
            if (outcome == "unavailable") shown.Should().Contain($"· {name}", "the reason can quote a name or the OS");
        }
    }

    // ------------------------------------------------------------------ the reader and previews

    [Theory]
    [MemberData(nameof(HostileNames))]
    public void Plain_text_files_and_markdown_text_are_shown_literally(string text)
    {
        BbcodeReading.Shown(MarkdownText.PlainToBbcode(text)).Should().Be(text);
        BbcodeReading.Read(MarkdownText.Render("A " + text).Bbcode).Tags.Where(t => !MarkdownTags.Contains(t)).Should().BeEmpty("a document can only produce the converter's own tags");
    }

    [Theory]
    [MemberData(nameof(HostileNames))]
    public void The_readers_error_is_the_os_message_as_written(string message)
    {
        var (shown, tags) = BbcodeReading.Read(ReaderText.ErrorBody($"Could not read C:\\{message}: access denied"));
        shown.Should().Be($"Could not read C:\\{message}: access denied");
        tags.Should().Equal("color", "color");
        BbcodeReading.Read(ReaderText.CutNote).Tags.Should().OnlyContain(t => t == "color" || t == "b");
    }

    [Fact]
    public void The_markup_names_fixture_scans_and_every_real_name_renders_literally()
    {
        // The planets-text journey's fixture, written and scanned for real (NTFS on Windows, ext4 on Linux).
        var parent = Path.Combine(Path.GetTempPath(), "FileScape-fixtures", "markup-test-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var root = FileScape.Core.Samples.MarkupNamesSample.Create(parent);
            var (tree, _) = ScanRunner.Run(new FileScape.Core.Scanning.DirectoryScanner(root));
            var names = FileScape.Core.Samples.MarkupNamesSample.Folders.Concat(FileScape.Core.Samples.MarkupNamesSample.Files.Select(f => f.Name)).ToList();
            foreach (var name in names)
            {
                var node = tree.Find(name);
                BbcodeReading.Shown(InspectorText.Describe(node, tree, SizeScale.Classic)).Should().Contain(tree.FullPath(node.Id));
                UiText.Plain($"{{E}} · look closer at {UiText.Lit(name)}").Should().Be($"E · look closer at {name}");
                BbcodeReading.Shown(UiText.ToBbcode($"{{Space}} · fly to {UiText.Lit(name)}", UiTextStyle.Prompt)).Should().EndWith($"fly to {name}");
            }

            foreach (var (name, text) in FileScape.Core.Samples.MarkupNamesSample.Files.Where(f => f.Name.EndsWith(".txt", StringComparison.Ordinal)))
            {
                BbcodeReading.Shown(MarkdownText.PlainToBbcode(File.ReadAllText(Path.Combine(root, name)))).Should().Be(text, "a plain file is shown as written");
            }
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    /// <summary>The tags MarkdownText writes itself.</summary>
    private static readonly HashSet<string> MarkdownTags = ["b", "i", "s", "u", "code", "color", "font_size", "indent", "table", "cell", "url"];
}
