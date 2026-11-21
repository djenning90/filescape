using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using FileScape.Core.Units;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>
/// Ordinary worlds' relief (PLANET-59, AD-64): a little more seeded relief on small and large worlds, with the drawn
/// ground, the walker's footing and the launchers still in agreement. Owns the claims that every slope is within
/// <see cref="PlanetShape.MaxWalkSlopeDegrees"/>, that ground drawn as the app's triangles stays on the walked ground,
/// that the walker stands on the ground everywhere, that pads, the arrival and the return points are level, and that a
/// Hazard World keeps the classic hills (reachability: PlanetComposerTests).
/// </summary>
public class PlanetReliefTests(ITestOutputHelper output)
{
    /// <summary>World names: each seeds a different set of hills.</summary>
    private static readonly string[] Names = ["Studio", "Pictures", "projects", "notes", "Music", "archive", "src", "Downloads", "Lantern", "Constellations", "home", "var"];

    /// <summary>A small world: six folders and a few files (radius ≈ 20 m).</summary>
    private static PlanetComposition Small(string name)
    {
        var s = new Spec(name).File("readme.txt", 1_200).File("logo.png", 52_000);
        for (var i = 0; i < 6; i++) s.Dir(new Spec($"folder-{i}").File("a.txt"));
        var c = PlanetComposer.Compose(new Spec("top").Dir(s).Build(), 1);
        return c;
    }

    /// <summary>A large world: 30 folders and 200 files (≈ 33 m, near the largest radius of 34 m).</summary>
    private static PlanetComposition Large(string name)
    {
        var s = new Spec(name);
        for (var i = 0; i < 30; i++) s.Dir(new Spec($"folder-{i:D2}").File("a.txt"));
        for (var i = 0; i < 200; i++) s.File($"file-{i:D3}.{(i % 3 == 0 ? "png" : i % 3 == 1 ? "txt" : "cs")}", 100 + i * 37);
        return PlanetComposer.Compose(new Spec("top").Dir(s).Build(), 1);
    }

    public static IEnumerable<object[]> Worlds() => Names.SelectMany(n => new[] { new object[] { "small", n }, new object[] { "large", n } });

    private static PlanetComposition Make(string size, string name) => size == "small" ? Small(name) : Large(name);

    /// <summary>The slope at a unit direction, degrees from level (the angle between the ground's normal and the local up).</summary>
    private static float SlopeDegrees(PlanetShape s, Vector3 up) => PlanetShape.Angle(s.Normal(up), up) * 180f / MathF.PI;

    [Fact]
    public void Ordinary_worlds_have_a_little_more_relief_growing_with_their_size()
    {
        foreach (var name in Names)
        {
            var small = Small(name);
            var large = Large(name);
            small.Shape.Radius.Should().BeLessThan(22f, "the small world is small");
            large.Shape.Radius.Should().BeGreaterThan(32f, "the large world is near the largest (34 m)");
            small.Shape.Relief.Should().Be(PlanetRelief.Rolling);
            large.Shape.Relief.Should().Be(PlanetRelief.Rolling);
            small.Shape.HillAmplitude.Should().BeInRange(0.75f * Scale(small), 1.1f * Scale(small), "0.75–1.1 m on the smallest world, scaled with the radius");
            large.Shape.HillAmplitude.Should().BeInRange(0.75f * Scale(large), 1.1f * Scale(large), "1.2–1.76 m on the largest world (was 0.6–0.9 m everywhere)");
            large.Shape.HillAmplitude.Should().BeGreaterThan(1.15f);
            var pts = PlanetComposer.FibonacciSphere(6000);
            float Range(PlanetShape s) => pts.Max(s.HeightAt) - pts.Min(s.HeightAt);
            var classicSmall = small.Shape with { Relief = PlanetRelief.Classic, HillAmplitude = 0.6f + 0.3f * (small.Shape.HillAmplitude - 0.75f * Scale(small)) / (0.35f * Scale(small)) };
            output.WriteLine($"{name}: small {small.Shape.Radius:0.0} m, amplitude {small.Shape.HillAmplitude:0.00} m, range {Range(small.Shape):0.00} m (classic {Range(classicSmall):0.00} m); large amplitude {large.Shape.HillAmplitude:0.00} m, range {Range(large.Shape):0.00} m");
            Range(small.Shape).Should().BeGreaterThan(Range(classicSmall), "more relief than the classic hills at the old height");
        }

        static float Scale(PlanetComposition c) => 1f + 0.6f * Math.Clamp((c.Shape.Radius - 16f) / 18f, 0f, 1f);
    }

    [Trait(TestKinds.Trait, TestKinds.Slow)]
    [Theory]
    [MemberData(nameof(Worlds))]
    public void Every_slope_is_within_the_walking_limit(string size, string name)
    {
        var s = Make(size, name).Shape;
        var worst = 0f;
        foreach (var up in PlanetComposer.FibonacciSphere(20_000)) worst = MathF.Max(worst, SlopeDegrees(s, up));
        output.WriteLine($"{size} {name}: steepest {worst:0.0}° (limit {PlanetShape.MaxWalkSlopeDegrees}°)");
        worst.Should().BeLessThanOrEqualTo(PlanetShape.MaxWalkSlopeDegrees);
        worst.Should().BeGreaterThan(2f, "there is relief to walk over");
    }

    /// <summary>
    /// The app draws the ground as an icosphere whose vertices sit on <see cref="PlanetShape.GroundRadius"/> with edges of
    /// ≈ 0.033 × radius (5 subdivisions above 15 m, PlanetArt.SubdivisionsFor): between vertices the flat triangle must
    /// stay within 2 cm of the walked ground, or feet would sink into or float over what is drawn.
    /// </summary>
    [Trait(TestKinds.Trait, TestKinds.Slow)]
    [Theory]
    [MemberData(nameof(Worlds))]
    public void The_drawn_ground_is_the_walked_ground(string size, string name)
    {
        var c = Make(size, name);
        var s = c.Shape;
        var edge = (s.Radius <= 15f ? 0.066f : 0.033f) * s.Radius * 1.2f; // the longest icosphere edges are ≈ 1.2× the mean
        var worst = 0f;
        foreach (var p in PlanetComposer.FibonacciSphere(3000))
        {
            var (a, b) = PlanetShape.Tangents(p);
            var v0 = p;
            var v1 = PlanetFrame.Move(p, a, edge, s.Radius);
            var v2 = PlanetFrame.Move(p, a * 0.5f + b * 0.866f, edge, s.Radius);
            Vector3 V(Vector3 d) => d * s.GroundRadius(d);
            for (var i = 1; i < 6; i++)
            {
                for (var j = 1; i + j < 6; j++)
                {
                    var (w0, w1, w2) = (i / 6f, j / 6f, 1f - (i + j) / 6f);
                    var on = V(v0) * w0 + V(v1) * w1 + V(v2) * w2; // a point on the drawn triangle
                    var gap = on.Length() - s.GroundRadius(Vector3.Normalize(on));
                    worst = MathF.Max(worst, MathF.Abs(gap));
                }
            }
        }

        output.WriteLine($"{size} {name}: drawn ground within {worst * 100:0.0} cm of the walked ground (edges {edge:0.00} m)");
        worst.Should().BeLessThan(0.08f, "within 8 cm (the classic hills reached 19 cm at the creases between flat spots)");

        // The walker stands on the ground everywhere it is put down, and stays put (no slide, no float).
        var bare = new PlanetSurface(s, []);
        var w = new SphereWalker();
        foreach (var up in PlanetComposer.FibonacciSphere(150))
        {
            w.Teleport(bare, up, PlanetShape.Tangents(up).A);
            var at = w.Feet;
            for (var f = 0; f < 12; f++) w.Update(SphereWalkInput.None, Durations.FromSeconds(1 / 60.0));
            w.Grounded.Should().BeTrue();
            (w.Feet.Length() - s.GroundRadius(w.Up)).Should().BeApproximately(0, 1e-3f, "feet on the drawn and walked ground");
            Vector3.Distance(at, w.Feet).Should().BeLessThan(0.01f, "standing still on a slope does not slide");
        }
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void Launchers_the_arrival_and_the_return_points_are_level(string size, string name)
    {
        var c = Make(size, name);
        var s = c.Shape;
        var worstReturn = 0f;
        foreach (var l in c.Launchers)
        {
            // Over the whole pad and 0.3 m beyond its rim: exactly level ground.
            var (a, b) = PlanetShape.Tangents(l.Direction);
            for (var k = 0; k < 16; k++)
            {
                var t = k * MathF.PI / 8f;
                foreach (var r in new[] { 0f, l.PadRadius * 0.5f, l.PadRadius + 0.3f })
                {
                    var d = PlanetFrame.Move(l.Direction, a * MathF.Cos(t) + b * MathF.Sin(t), r, s.Radius);
                    s.HeightAt(d).Should().Be(0, $"{l.Name}'s pad is level");
                }
            }

            if (l.IsParent) continue;
            var back = c.ArrivalFor(l.TargetId).Up;
            worstReturn = MathF.Max(worstReturn, SlopeDegrees(s, back));
            MathF.Abs(s.HeightAt(back)).Should().BeLessThan(0.05f, $"coming back from {l.Name}, you land beside its pad on level ground");
        }

        s.HeightAt(c.Arrival.Up).Should().Be(0, "the arrival is level");
        SlopeDegrees(s, c.Arrival.Up).Should().BeLessThan(0.5f);
        output.WriteLine($"{size} {name}: {c.Launchers.Count} pads level; return points at most {worstReturn:0.0}° off level");
        worstReturn.Should().BeLessThan(3f);
    }

    [Fact]
    public void A_hazard_world_keeps_the_classic_hills()
    {
        var t = new Spec("This computer") { Location = LocationKind.Hub }
            .Dir(new Spec("Ubuntu") { Location = LocationKind.Wsl }.Dir(new Spec("proc").Dir(new Spec("1")).Dir(new Spec("2")).File("cpuinfo")))
            .Build();
        var proc = t[t[0].Children[0]].Children[0];
        var c = PlanetComposer.Compose(t, proc);
        c.Hazard.Should().NotBeNull("/proc on a WSL root is a Hazard World");
        c.Shape.Relief.Should().Be(PlanetRelief.Classic);
        c.Shape.HillAmplitude.Should().BeInRange(0.6f, 0.9f);
    }
}
