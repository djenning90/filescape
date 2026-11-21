using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-192 (AD-98): how far the overview's wheel may zoom out, derived from the focus's neighbourhood and the
/// universe's extent instead of a fixed 6,000 m, through <see cref="OverviewFraming"/> (the functions
/// <c>PlanetWorld.Universe.cs</c> calls on every wheel notch), on layouts from <see cref="UniverseLayout.Compute"/>.
/// </summary>
public sealed class OverviewFramingTests
{
    /// <summary>A broad root like a Linux distribution's: <paramref name="top"/> folders, each with <paramref name="inner"/> subfolders of 6 more.</summary>
    private static FsTree Broad(int top, int inner)
    {
        var spec = new Spec("root");
        for (var i = 0; i < top; i++)
        {
            var d = new Spec($"top{i:00}");
            for (var j = 0; j < inner; j++) d = d.Dir(Spec.WithDirs($"top{i:00}-{j:00}", 6));
            spec = spec.Dir(d);
        }

        return spec.Build();
    }

    /// <summary>The camera distance at which a sphere of <paramref name="size"/> metres fits the overview's 62° lens vertically.</summary>
    private static float Fits(float size) => size / MathF.Tan(31f * MathF.PI / 180f);

    [Fact]
    public void At_a_broad_root_the_wheel_reaches_far_enough_to_see_the_whole_root_system()
    {
        // A root like a Linux distribution's, laid out by the app's own attention budget (6,000 worlds). Measured: such a
        // universe is ≈ 1 km across, so the old 6,000 m was not what bound there (PLANET-192 records this); the limit is
        // still derived, and from it the whole root system fits the view.
        var tree = Broad(22, 14);
        var bodies = new OverviewAttention { Current = 0 }.Layout(tree, null, 0, null, keep: false);
        var root = bodies[0];
        var extent = OverviewFraming.Extent(bodies, root.Position);
        var max = OverviewFraming.MaxDistance(OverviewFraming.NeighbourhoodSize(root, null), extent);
        max.Should().BeGreaterThanOrEqualTo(Fits(OverviewFraming.SystemSize(root)), "from the farthest zoom the root's whole system fits the view");
        max.Should().BeGreaterThanOrEqualTo(Fits(extent), "and so does the whole universe");
        OverviewFraming.FarClip(max, extent).Should().BeGreaterThan(max + extent, "and nothing of it is clipped away");
    }

    [Fact]
    public void A_universe_larger_than_the_old_limit_can_be_seen_whole()
    {
        // A root whose ring is 9 km across (a vast universe, AD-81's fixture is larger still): 6,000 m showed a fraction of it.
        var root = new UniverseBody(0, -1, Vector3.Zero, 5f, 0, Discovery.Listed, 0, Vector3.UnitY, 9000f);
        var child = new UniverseBody(1, 0, new Vector3(9000f, 0, 0), 3f, 1, Discovery.Listed, 0, Vector3.UnitY, 1200f);
        var bodies = new[] { root, child };
        var extent = OverviewFraming.Extent(bodies, root.Position);
        var max = OverviewFraming.MaxDistance(OverviewFraming.NeighbourhoodSize(root, null), extent);
        max.Should().BeGreaterThan(OverviewFraming.FloorMetres);
        max.Should().BeGreaterThanOrEqualTo(Fits(OverviewFraming.SystemSize(root)), "the root's whole system fits the view");
        var fromChild = OverviewFraming.MaxDistance(OverviewFraming.NeighbourhoodSize(child, root), extent, 9000f);
        fromChild.Should().BeGreaterThanOrEqualTo(Fits(9000f + OverviewFraming.SystemSize(root)), "from a world on its ring, its neighbours (the ring) fit too");
        OverviewFraming.FarClip(fromChild, extent).Should().BeGreaterThan(fromChild + extent);
    }

    [Fact]
    public void A_selected_world_can_always_be_seen_among_its_neighbours()
    {
        var tree = Broad(22, 14);
        var bodies = UniverseLayout.Compute(tree);
        var byId = bodies.ToDictionary(b => b.NodeId);
        var extent = OverviewFraming.Extent(bodies, bodies[0].Position);
        foreach (var b in bodies.Where(b => b.ParentId >= 0))
        {
            var parent = byId[b.ParentId];
            var hood = OverviewFraming.NeighbourhoodSize(b, parent);
            hood.Should().BeGreaterThanOrEqualTo(Vector3.Distance(b.Position, parent.Position) + parent.Radius, "its neighbourhood reaches the world it orbits");
            foreach (var sibling in bodies.Where(s => s.ParentId == b.ParentId))
                Vector3.Distance(b.Position, sibling.Position).Should().BeLessThanOrEqualTo(hood, "and every sibling (its neighbours)");
            var max = OverviewFraming.MaxDistance(hood, extent, Vector3.Distance(b.Position, bodies[0].Position));
            max.Should().BeGreaterThanOrEqualTo(Fits(hood), $"from {tree.DisplayName(b.NodeId)} the wheel reaches a view of its neighbourhood");
        }
    }

    [Fact]
    public void A_small_universe_keeps_the_old_limit_and_a_vast_one_has_a_ceiling()
    {
        var tree = Spec.WithDirs("root", 5).Build();
        var bodies = UniverseLayout.Compute(tree);
        var extent = OverviewFraming.Extent(bodies, bodies[0].Position);
        OverviewFraming.MaxDistance(OverviewFraming.NeighbourhoodSize(bodies[0], null), extent).Should().Be(OverviewFraming.FloorMetres, "never more constrained than before");
        OverviewFraming.MaxDistance(1e9f, 1e9f).Should().Be(OverviewFraming.CeilingMetres);
        OverviewFraming.MaxDistance(1e5f, 200f).Should().Be(OverviewFraming.FloorMetres, "the universe's extent caps the neighbourhood (never beyond 3 × the map), but not below the floor");
        OverviewFraming.MaxDistance(5000f, 10000f).Should().Be(15000f, "3 × the neighbourhood");
        OverviewFraming.MaxDistance(5000f, 3000f).Should().Be(9000f, "3 × the universe when that is less");
    }

    [Fact]
    public void The_wheel_stops_at_the_limit_and_a_smaller_limit_never_pulls_the_camera_in()
    {
        var o = OverviewControls.WheelDistanceFactor(zoomIn: false, fast: false);
        var i = OverviewControls.WheelDistanceFactor(zoomIn: true, fast: false);
        OverviewFraming.WheelDistance(1000f, o, 9000f).Should().BeApproximately(1180f, 0.01f, "a plain notch, as before");
        OverviewFraming.WheelDistance(8500f, o, 9000f).Should().Be(9000f, "out stops at the limit");
        OverviewFraming.WheelDistance(20000f, o, 9000f).Should().Be(20000f, "past a limit that shrank (a smaller focus chosen): zooming out stops where it is");
        OverviewFraming.WheelDistance(20000f, i, 9000f).Should().BeApproximately(17000f, 0.5f, "and zooming in works as always");
        OverviewFraming.WheelDistance(4.2f, i, 9000f).Should().Be(OverviewFraming.MinMetres, "in stops at the closest");
    }
}
