using System.Reflection;

namespace FileScape.Core.Tests;

/// <summary>
/// The area sets and test kinds stay usable (TEST-11, TEST-13, AD-90). <c>tools/fs.sh test-area AREA</c> filters by the
/// class-name prefixes in <c>areas.txt</c>, so a test class that matches no area would only ever run in the whole suite;
/// and the kinds' trait values are what the script's filters name.
/// What a change must preserve: every test class starts with some area's prefix; every area has at least one class; a
/// test's Kind trait, if any, is one of <see cref="TestKinds"/>' values.
/// </summary>
public class TestAreaTests
{
    private static Dictionary<string, string[]> Areas()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "areas.txt");
        File.Exists(path).Should().BeTrue("areas.txt is copied beside the test assembly");
        return File.ReadAllLines(path)
            .Select(l => l.Split('#')[0].Trim())
            .Where(l => l.Contains(':'))
            .ToDictionary(l => l[..l.IndexOf(':')].Trim(), l => l[(l.IndexOf(':') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static IEnumerable<MethodInfo> TestMethods(Type t) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.GetCustomAttributes<FactAttribute>(true).Any());

    private static List<Type> TestClasses() =>
        typeof(TestAreaTests).Assembly.GetTypes().Where(t => t.IsPublic && !t.IsAbstract && TestMethods(t).Any()).ToList();

    [Fact]
    public void Every_test_class_is_in_an_area()
    {
        var areas = Areas();
        areas.Should().ContainKeys("music", "hazard", "overview", "gallery", "places", "panels", "navigation", "scanning");
        var prefixes = areas.Values.SelectMany(p => p).ToList();
        var classes = TestClasses();
        classes.Should().HaveCountGreaterThan(50);
        classes.Where(c => !prefixes.Any(c.Name.StartsWith)).Select(c => c.Name)
            .Should().BeEmpty("each test class belongs to an area in tests/FileScape.Core.Tests/areas.txt (tools/fs.sh test-area)");
        foreach (var (area, ps) in areas)
        {
            classes.Should().Contain(c => ps.Any(c.Name.StartsWith), $"the area {area} names at least one class");
        }
    }

    [Fact]
    public void Every_kind_trait_is_a_known_kind()
    {
        var known = new[] { TestKinds.Perf, TestKinds.Report, TestKinds.Slow };
        // TraitAttribute keeps its name and value only as constructor arguments in the metadata.
        var data = TestClasses().SelectMany(TestMethods)
            .SelectMany(m => m.GetCustomAttributesData().Where(d => d.AttributeType == typeof(TraitAttribute)))
            .Select(d => ((string)d.ConstructorArguments[0].Value!, (string)d.ConstructorArguments[1].Value!))
            .ToList();
        data.Where(d => d.Item1 == TestKinds.Trait).Select(d => d.Item2).Should().OnlyContain(v => known.Contains(v));
        data.Should().Contain((TestKinds.Trait, TestKinds.Perf), "the wall-clock bounds are marked");
    }
}
