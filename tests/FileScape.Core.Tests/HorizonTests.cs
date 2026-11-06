using FileScape.Core.Model;
using FileScape.Core.Places;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>HORIZON-01..03: one more level as silhouettes, deeper structure as haze, unknown places veiled; bounded.</summary>
public class HorizonTests
{
    [Fact]
    public void Deeper_levels_show_as_silhouettes_and_haze_and_unknown_places_are_veiled()
    {
        Spec Deep(string name, int width) // name / g0..g(width-1) / h0..h2 / leaf.txt
        {
            var s = new Spec(name);
            for (var i = 0; i < width; i++)
            {
                var g = new Spec($"{name}-g{i}");
                for (var k = 0; k < 3; k++) g.Dir(new Spec($"{name}-g{i}-h{k}").File("leaf.txt"));
                s.Dir(g);
            }

            return s;
        }

        var unknown = new Spec("mystery") { State = DirScanState.Unlisted };
        var tree = new Spec("here").Dir(Deep("alpha", 4)).Dir(Deep("beta", 2)).Dir(new Spec("gamma").Dir(unknown)).Dir(new Spec("void")).Build();
        var place = PlaceComposer.Compose(tree, tree.Root.Id);
        var marks = Horizon.Compose(tree, place, PlaceParams.Default);

        marks.Should().Contain(m => m.Kind == HorizonKind.Silhouette, "the level beyond the vistas' beacons is drawn");
        marks.Where(m => m.Kind == HorizonKind.Silhouette).Should().OnlyContain(m => tree[m.FolderId].Name.Contains("-h", StringComparison.Ordinal),
            "silhouettes stand for great-grandchildren of the place (one level beyond the vista beacons)");
        marks.Should().Contain(m => m.Kind == HorizonKind.Unknown && tree[m.FolderId].Name == "mystery", "an unlisted place is veiled, not drawn as empty");
        marks.Should().NotContain(m => tree[m.FolderId].Name == "void", "an empty place has nothing beyond it");
        foreach (var v in place.Vistas)
        {
            // Everything stands further out than the vista it belongs to, along its bearing.
            var mine = marks.Where(m => tree[m.FolderId].Name.StartsWith(tree[v.FolderId].Name, StringComparison.Ordinal)).ToList();
            foreach (var m in mine) new System.Numerics.Vector2(m.Position.X, m.Position.Z).Length().Should().BeGreaterThan(new System.Numerics.Vector2(v.Center.X, v.Center.Z).Length(), $"{m.Kind} {tree[m.FolderId].Name} beyond vista {tree[v.FolderId].Name} at {v.Center}, mark at {m.Position}");
        }

        Horizon.Compose(tree, place, PlaceParams.Default).Should().Equal(marks, "deterministic");
    }

    [Fact]
    public void The_horizon_is_bounded()
    {
        var wide = new Spec("wide");
        for (var i = 0; i < 20; i++)
        {
            var c = new Spec($"c{i:D2}");
            for (var j = 0; j < 12; j++) c.Dir(new Spec($"c{i:D2}-g{j:D2}").Dir(new Spec("x").File("a")).Dir(new Spec("y").File("b")).Dir(new Spec("z").File("c")).Dir(new Spec("w").File("d")));
            wide.Dir(c);
        }

        var tree = wide.Build();
        var place = PlaceComposer.Compose(tree, tree.Root.Id);
        Horizon.Compose(tree, place, PlaceParams.Default).Count.Should().BeLessThanOrEqualTo(Horizon.MaxMarks);
    }
}
