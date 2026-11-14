using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>PLANET-27: a world's character from what the place is (hub, roots, repositories, depth bands).</summary>
public class WorldCharacterTests
{
    private static FsTree Computer()
    {
        var repoDeep = new Spec("filescape").Dir(new Spec(".git")).Dir(new Spec("src").Dir(new Spec("App").Dir(new Spec("Scripts")))).File("README.md");
        var worktree = new Spec("filescape-wt").File(".git").File("README.md").Dir(new Spec("docs"));
        var home = new Spec("home").Dir(new Spec("dj").Dir(new Spec("src").Dir(repoDeep).Dir(worktree)));
        var wsl = new Spec("Ubuntu") { Location = LocationKind.Wsl }.Dir(home).Dir(new Spec("etc"));
        var deep = Spec.Chain("a", 8);
        var c = new Spec("C:") { Location = LocationKind.Drive }.Dir(new Spec("Users").Dir(deep));
        var rootRepo = new Spec("D:") { Location = LocationKind.Drive }.Dir(new Spec(".git")).Dir(new Spec("lib"));
        return new Spec("This computer") { Location = LocationKind.Hub }.Dir(wsl).Dir(c).Dir(rootRepo).Build();
    }

    private static WorldCharacter Of(FsTree t, string name) => WorldCharacter.Of(t, t.Find(name).Id, 0);

    [Fact]
    public void The_hub_and_the_two_kinds_of_root_are_told_apart()
    {
        var t = Computer();
        Of(t, "This computer").Kind.Should().Be(WorldKind.Hub);
        Of(t, "Ubuntu").Kind.Should().Be(WorldKind.WslRoot);
        Of(t, "C:").Kind.Should().Be(WorldKind.WindowsRoot);
    }

    [Fact]
    public void A_repository_is_recognised_wherever_it_sits_and_its_children_remember_it()
    {
        var t = Computer();
        var repo = Of(t, "filescape");
        repo.Kind.Should().Be(WorldKind.Repository);
        repo.Depth.Should().Be(4, "Ubuntu / home / dj / src / filescape");
        repo.Worktree.Should().BeFalse();
        var src = Of(t, "Scripts");
        src.Kind.Should().Be(WorldKind.Folder);
        src.InsideRepository.Should().BeTrue();
        src.RepositoryId.Should().Be(t.Find("filescape").Id);
        Of(t, "etc").InsideRepository.Should().BeFalse();
    }

    [Fact]
    public void A_worktree_marker_file_counts_and_a_root_that_is_a_repository_is_a_repository()
    {
        var t = Computer();
        var wt = Of(t, "filescape-wt");
        wt.Kind.Should().Be(WorldKind.Repository);
        wt.Worktree.Should().BeTrue();
        Of(t, "D:").Kind.Should().Be(WorldKind.Repository, "the project is what you came for; the root is still named a drive in the HUD");
    }

    [Fact]
    public void Depth_bands_follow_the_depth_below_the_root()
    {
        var t = Computer();
        Of(t, "etc").Band.Should().Be(1);
        Of(t, "Users").Band.Should().Be(1);
        var chain = new List<int>();
        var id = t.Find("Users").Id;
        while (t[id].Children.FirstOrDefault(c => t[c].IsDirectory) is var next && next > 0)
        {
            id = next;
            chain.Add(WorldCharacter.Of(t, id, 0).Band);
        }

        chain.Should().StartWith([2, 2, 3, 3, 4]).And.OnlyContain(b => b >= 1 && b <= WorldCharacter.MaxBand);
        chain.Should().BeInAscendingOrder("deeper never goes back to a shallower band");
        WorldCharacter.BandOf(1).Should().Be(1);
        WorldCharacter.BandOf(3).Should().Be(2);
        WorldCharacter.BandOf(5).Should().Be(3);
        WorldCharacter.BandOf(40).Should().Be(4);
    }

    [Fact]
    public void Without_a_hub_the_starting_folder_is_the_hub_and_depth_counts_from_it()
    {
        var t = new Spec("Starter Sky").Dir(new Spec("Letters").Dir(new Spec("Drafts"))).Dir(new Spec("Lantern Workshop").Dir(new Spec(".git")).Dir(new Spec("src"))).Build();
        WorldCharacter.Of(t, 0, 0).Kind.Should().Be(WorldKind.Hub);
        WorldCharacter.Of(t, t.Find("Drafts").Id, 0).Should().Match<WorldCharacter>(c => c.Kind == WorldKind.Folder && c.Depth == 2 && c.Band == 2);
        WorldCharacter.Of(t, t.Find("Lantern Workshop").Id, 0).Kind.Should().Be(WorldKind.Repository);
        WorldCharacter.Of(t, t.Find("src").Id, 0).InsideRepository.Should().BeTrue();
    }

    [Fact]
    public void A_repository_has_a_low_workbench_with_no_stepping_stones()
    {
        var t = new Spec("proj").Dir(new Spec(".git")).Dir(new Spec("src")).File("README.md").File("a.csproj").Build();
        var c = PlanetComposer.Compose(t, 0);
        c.Overlook.Top.Should().Be(PlanetComposer.RepositoryBenchTop);
        c.Overlook.Steps.Should().BeEmpty("you jump onto the bench");
        c.Paths.Should().Contain(p => p.TargetId == -1, "a route still leads to it");
        var plain = PlanetComposer.Compose(new Spec("plain").Dir(new Spec("src")).File("README.md").Build(), 0);
        plain.Overlook.Steps.Should().NotBeEmpty();
    }

    [Fact]
    public void A_start_folder_under_a_location_root_is_your_home_base()
    {
        var t = Computer();
        var dj = t.Find("dj").Id;
        var src = t[dj].Children.First(ch => t[ch].Name == "src"); // Ubuntu/home/dj/src, opened directly
        WorldCharacter.Of(t, src, src).Kind.Should().Be(WorldKind.Hub, "where you started is home, whatever the tree shows above it");
        WorldCharacter.Of(t, t.Find("filescape").Id, src).Should().Match<WorldCharacter>(c => c.Kind == WorldKind.Repository && c.Depth == 1, "depth counts from where you started");
        var ubuntu = t.Find("Ubuntu").Id;
        WorldCharacter.Of(t, ubuntu, ubuntu).Kind.Should().Be(WorldKind.WslRoot, "a root you start at keeps its own character");
    }

    [Theory]
    [InlineData(false, false, false)] // walking into it: blocked
    [InlineData(true, false, true)]   // a tapped hop lands on it
    [InlineData(true, true, true)]    // a held jump too
    public void The_workbench_is_reached_by_any_jump_and_not_by_walking(bool jump, bool hold, bool onTop)
    {
        var t = new Spec("proj").Dir(new Spec(".git")).File("README.md").Build();
        var c = PlanetComposer.Compose(t, 0);
        var ov = c.Overlook;
        var r = c.Shape.Radius;
        var w = new FileScape.Core.Planets.SphereWalker();
        var start = PlanetFrame.Move(ov.Direction, c.Arrival.Up - ov.Direction, ov.Radius + 1.2f, r);
        Vector3 Toward(Vector3 up) { var d = ov.Direction - up * Vector3.Dot(up, ov.Direction); return Vector3.Normalize(d); }
        w.Teleport(c.Surface, start, Toward(start));
        var frame = FileScape.Core.Units.Durations.FromSeconds(1 / 120.0);
        for (var i = 0; i < 240; i++)
        {
            var press = jump && i == 8;
            var held = jump && hold && i >= 8 && i < 70;
            w.Update(new SphereWalkInput(Toward(w.Up), false, press, held || press), frame);
        }

        var onBench = w.Grounded && w.Support.Value > ov.Top - 0.05f;
        onBench.Should().Be(onTop, $"support {w.Support.Value:0.00} m, deck {ov.Top} m");
    }
}
