using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

public class UniverseLayoutTests
{
    private static FsTree Sample()
    {
        var spec = new Spec("root");
        for (var i = 0; i < 6; i++)
        {
            var d = new Spec($"d{i}");
            for (var j = 0; j < i * 3; j++) d = d.Dir(Spec.WithFiles($"d{i}-{j}", j));
            spec = spec.Dir(d);
        }

        return spec.Dir(new Spec("later") { State = DirScanState.Unlisted }).Build();
    }

    [Fact]
    public void Every_known_folder_is_one_world_and_none_overlap()
    {
        var tree = Sample();
        var bodies = UniverseLayout.Compute(tree);
        bodies.Select(b => b.NodeId).Should().OnlyHaveUniqueItems();
        bodies.Should().HaveCount(tree.Nodes.Count(n => n.IsDirectory));
        for (var i = 0; i < bodies.Count; i++)
        {
            for (var j = i + 1; j < bodies.Count; j++)
            {
                Vector3.Distance(bodies[i].Position, bodies[j].Position).Should().BeGreaterThan(bodies[i].Radius + bodies[j].Radius, $"{tree[bodies[i].NodeId].Name} and {tree[bodies[j].NodeId].Name} do not overlap");
            }
        }

        var byId = bodies.ToDictionary(b => b.NodeId);
        foreach (var b in bodies.Where(b => b.ParentId >= 0))
        {
            var p = byId[b.ParentId];
            Vector3.Distance(b.Position, p.Position).Should().BeApproximately(p.OrbitRadius, 0.01f, "on its parent's ring");
            MathF.Abs(Vector3.Dot(Vector3.Normalize(b.Position - p.Position), p.OrbitNormal)).Should().BeLessThan(0.01f, "in the ring's plane");
            b.Depth.Should().Be(p.Depth + 1);
        }
    }

    [Fact]
    public void Discovered_and_undiscovered_folders_are_told_apart()
    {
        var tree = Sample();
        var bodies = UniverseLayout.Compute(tree).ToDictionary(b => b.NodeId);
        bodies[tree.Find("later").Id].Discovery.Should().Be(Discovery.Unknown);
        bodies[tree.Find("d3").Id].Discovery.Should().Be(Discovery.Listed);
    }

    [Fact]
    public void The_overview_is_bounded_and_counts_what_it_leaves_out()
    {
        var tree = new Spec("top").Dir(Spec.WithDirs("wide", 200)).Build();
        var bodies = UniverseLayout.Compute(tree);
        var wide = bodies.Single(b => tree[b.NodeId].Name == "wide");
        wide.HiddenChildren.Should().Be(200 - UniverseLayout.MaxChildren);
        bodies.Count(b => b.ParentId == wide.NodeId).Should().Be(UniverseLayout.MaxChildren);
        UniverseLayout.Compute(tree).Select(b => b.Position).Should().Equal(bodies.Select(b => b.Position), "deterministic");
    }
}
