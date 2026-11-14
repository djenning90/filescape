using FileScape.Core.Planets;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>PLANET-29: the workbench's reading of a project, from names only.</summary>
public class ProjectBenchTests
{
    [Fact]
    public void A_csharp_repository_shows_readme_first_then_solution_project_entry_and_folders()
    {
        var t = new Spec("Lantern").Dir(new Spec(".git")).Dir(new Spec("src").File("Lantern.cs").File("Program.cs")).Dir(new Spec("tests").File("LanternTests.cs")).Dir(new Spec("docs"))
            .File("Lantern.csproj").File("Lantern.sln").File("README.md").File("LICENSE").File(".gitignore").Build();
        var bench = ProjectBench.KeyLocations(t, 0);
        bench.Select(b => b.Role).Should().StartWith([BenchRole.Readme, BenchRole.Solution, BenchRole.Manifest, BenchRole.Entry, BenchRole.Source, BenchRole.Tests, BenchRole.Docs]);
        t[bench[0].NodeId].Name.Should().Be("README.md");
        t[bench.Single(b => b.Role == BenchRole.Entry).NodeId].Name.Should().Be("Program.cs");
        bench.Single(b => b.Role == BenchRole.Entry).Why.Should().Contain("src");
        bench.Should().Contain(b => b.Role == BenchRole.License);
        bench.Select(b => b.NodeId).Should().OnlyHaveUniqueItems();
        bench.Should().NotContain(b => t[b.NodeId].Name == ".git" || t[b.NodeId].Name == ".gitignore");
    }

    [Fact]
    public void A_python_project_and_a_bare_repository_are_read_too_and_the_list_is_bounded()
    {
        var py = new Spec("tool").Dir(new Spec(".git")).File("pyproject.toml").File("README.rst").File("main.py").File("Makefile").Build();
        ProjectBench.KeyLocations(py, 0).Select(b => b.Role).Should().Equal(BenchRole.Readme, BenchRole.Manifest, BenchRole.Entry, BenchRole.Build);

        var bare = new Spec("empty").Dir(new Spec(".git")).Build();
        ProjectBench.KeyLocations(bare, 0).Should().BeEmpty();

        var big = new Spec("big").File("README.md").File("a.sln").File("package.json").File("Cargo.toml").File("index.js").File("main.go");
        foreach (var d in new[] { "src", "app", "lib", "tests", "docs", "tools", "scripts", ".github" }) big = big.Dir(new Spec(d));
        ProjectBench.KeyLocations(big.Build(), 0).Should().HaveCount(ProjectBench.MaxEntries);
    }
}
