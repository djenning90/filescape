using FileScape.Core.Interaction;
using FileScape.Core.Places;
using FileScape.Core.Shell;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>PROM-01..04 and DEV-01..03: explainable prominence, the deliberate README view and editor options.</summary>
public sealed class ProminenceAndProjectTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "filescape-readme-" + Guid.NewGuid().ToString("N")[..8]);

    public ProminenceAndProjectTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ProminenceContext Ctx(string[]? pins = null, (string, string)[]? known = null) => new()
    {
        Pins = new HashSet<string>(pins ?? [], ProminenceContext.PathComparer),
        KnownFolders = (known ?? []).ToDictionary(k => k.Item1, k => k.Item2, ProminenceContext.PathComparer),
    };

    [Theory]
    [InlineData(@"\\wsl.localhost\Ubuntu-24.04\home", "Home folders")]
    [InlineData(@"\\wsl.localhost\Ubuntu-24.04\home\someone", "Linux user's home")]
    [InlineData("/home/someone", "Linux user's home")]
    [InlineData("/root", "Linux user's home")]
    public void Linux_homes_stand_out_by_their_standard_paths(string path, string reason)
    {
        var p = Prominence.ByPath(path, Ctx())!;
        p.Kind.Should().Be(ProminenceKind.Home);
        p.Level.Should().Be(2);
        p.Reason.Should().Contain(reason);
    }

    [Theory]
    [InlineData(@"\\wsl.localhost\Ubuntu-24.04\usr")]
    [InlineData(@"\\wsl.localhost\Ubuntu-24.04\etc")]
    [InlineData(@"\\wsl.localhost\Ubuntu-24.04\home\someone\src")]
    [InlineData(@"C:\Windows")]
    public void Routine_locations_are_not_prominent(string path) => Prominence.ByPath(path, Ctx()).Should().BeNull();

    [Fact]
    public void Pins_and_platform_known_folders_explain_themselves()
    {
        var ctx = Ctx([@"C:\Work\Thing"], [(@"C:\Users\x\Downloads", "Downloads"), (@"C:\Users\x", "Home")]);
        Prominence.ByPath(@"c:\work\thing", ctx)!.Kind.Should().Be(ProminenceKind.Pinned, "Windows paths compare without case");
        Prominence.ByPath(@"C:\Users\x\Downloads", ctx)!.Reason.Should().Be("Your Downloads folder (as Windows reports it).");
        Prominence.ByPath(@"C:\Users\x", ctx)!.Level.Should().Be(2);
        Prominence.ByPath(@"C:\Users\x\Music", ctx).Should().BeNull("only folders the platform reported are named");
    }

    [Fact]
    public void Projects_and_folders_of_projects_are_recognised_from_listed_names_only()
    {
        var tree = new Spec("src")
            .Dir(new Spec("lighthouse").File("Lighthouse.sln").File("README.md"))
            .Dir(new Spec("engine").File("Cargo.toml").Dir(new Spec(".git")))
            .Dir(new Spec("notes").File("README.md"))
            .Build();
        var root = tree.Root.Id;
        var lighthouse = tree.Find("lighthouse").Id;
        var notes = tree.Find("notes").Id;
        Prominence.For(tree, lighthouse, Ctx()).Kind.Should().Be(ProminenceKind.Project);
        Prominence.For(tree, lighthouse, Ctx()).Reason.Should().Contain("Lighthouse.sln");
        Prominence.For(tree, notes, Ctx()).IsProminent.Should().BeFalse("a README alone is not a project");
        var container = Prominence.For(tree, root, Ctx());
        container.Kind.Should().Be(ProminenceKind.Projects);
        container.Reason.Should().Be("Contains software projects: engine, lighthouse.", "names in natural order, deterministic");
        var corrected = new ProminenceContext { NotProjects = new HashSet<int> { lighthouse } };
        Prominence.For(tree, lighthouse, corrected).IsProminent.Should().BeFalse("the user's 'Not a project' correction wins");
    }

    [Fact]
    public void Pins_round_trip_in_preferences_without_duplicates()
    {
        var p = Preferences.Default.WithPin(@"C:\A", true).WithPin(@"c:\a", true).WithPin(@"\\wsl.localhost\U\home\x\src", true);
        p.Pins.Should().HaveCount(2);
        var back = Preferences.FromJson(p.ToJson(), out var problem);
        problem.Should().BeNull();
        back.Pins.Should().Equal(@"C:\A", @"\\wsl.localhost\U\home\x\src");
        back.WithPin(@"C:\A", false).Pins.Should().Equal(@"\\wsl.localhost\U\home\x\src");
        Preferences.FromJson("{\"version\":1}", out _).Pins.Should().BeEmpty("older files have no pins");
    }

    [Fact]
    public void A_readme_is_shown_as_inert_text()
    {
        var path = Path.Combine(_dir, "README.md");
        File.WriteAllText(path, "# Title\n[color=red]x[/color] [url=http://e]y[/url]\n![logo](https://example.com/logo.png)\nSee [docs](https://example.com/docs) and `make all`.\n<script>alert(1)</script>\n```\nrm -rf /\n```\n- item **bold**\n");
        var r = Readme.Read(path);
        r.Error.Should().BeNull();
        r.Format.Should().Be("markdown");
        var bb = Readme.ToBbcode(r.Text, markdown: true);
        bb.Should().Contain("[font_size=26][b]Title[/b][/font_size]");
        bb.Should().Contain("[lb]color=red]x[lb]/color]", "markup in the file is escaped, never applied");
        bb.Should().NotContain("[url", "no tag from the file survives as a tag");
        bb.Should().Contain("(image: logo)").And.NotContain("https://example.com/logo.png", "images are never fetched or even linked");
        bb.Should().Contain("[u]docs[/u]").And.Contain("‹https://example.com/docs›", "links stay text");
        bb.Should().Contain("<script>alert(1)</script>", "HTML is shown as written");
        bb.Should().Contain("rm -rf /").And.Contain(" • item [b]bold[/b]");
    }

    [Fact]
    public void Readme_reading_is_bounded_and_refuses_binary()
    {
        var big = Path.Combine(_dir, "README.txt");
        File.WriteAllText(big, new string('a', 1000));
        var r = Readme.Read(big, maxBytes: 100);
        r.Truncated.Should().BeTrue();
        r.Text.Length.Should().Be(100);
        var bin = Path.Combine(_dir, "README");
        File.WriteAllBytes(bin, [0x50, 0, 1, 2]);
        Readme.Read(bin).Error.Should().Contain("binary");
        Readme.Read(Path.Combine(_dir, "missing.md")).Error.Should().StartWith("Could not read it");
        Readme.IsSupported("README.md").Should().BeTrue();
        Readme.IsSupported("readme.rst").Should().BeTrue();
        Readme.IsSupported("README.pdf").Should().BeFalse();
    }

    [Fact]
    public void Editor_options_respect_the_project_platform()
    {
        const string code = @"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe";
        var win = EditorLaunch.Options(@"C:\src\Lighthouse", ["Lighthouse.sln"], code);
        win.Select(o => o.Label).Should().Equal("Open Lighthouse.sln", "Open folder in VS Code");
        win[0].ViaAssociation.Should().BeTrue();
        win[0].Program.Should().Be(@"C:\src\Lighthouse\Lighthouse.sln");
        win[1].Args.Should().Equal(@"C:\src\Lighthouse");

        var wsl = EditorLaunch.Options(@"\\wsl.localhost\Ubuntu-24.04\home\x\src\engine", ["engine.sln"], code);
        wsl.Should().ContainSingle("a Linux project never opens through a Windows association, even with a .sln");
        wsl[0].Args.Should().Equal("--remote", "wsl+Ubuntu-24.04", "/home/x/src/engine");
        wsl[0].ViaAssociation.Should().BeFalse();

        EditorLaunch.Options(@"\\wsl.localhost\Ubuntu-24.04\home\x\src\engine", [], vsCode: null).Should().BeEmpty("nothing is installed or guessed");
        EditorLaunch.Options(@"C:\src\NoSolution", [], vsCode: null).Should().BeEmpty();
    }
}
