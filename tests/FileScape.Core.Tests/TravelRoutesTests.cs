using System.Text.RegularExpressions;
using FileScape.Core.Model;
using FileScape.Core.Planets;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-147..148 (AD-92): every route that starts travel is in <see cref="TravelRoutes"/>, every app line that moves the
/// navigator or starts a departure names its route (<c>// travel: Name</c>), and every launcher route plans a launcher.
/// Written before the app's lines were tagged (they all failed the tag test) and before the telescope's Enter used its
/// own entry (it called G's, so <see cref="DepartureEntry.Telescope"/> was used nowhere).
/// </summary>
public partial class TravelRoutesTests
{
    /// <summary>A navigator move, a departure or an overview landing started (not a method's own declaration).</summary>
    [GeneratedRegex(@"(?:_nav|Navigator)\.(?:GoTo|Enter|Back|Up|Reset|GoToRoot|FollowLink|Revert)\(|_s\.(?:TravelInto|TravelUp|GoBack|GoToPath)\(|(?<![\w.])(?:BeginLaunch|Depart|DepartFor|LandFromOverview|LandFromUniverse|StartTrip|LaunchToSelection|LaunchUp|LaunchBack|BeginUniverseLanding)\(|_arriveFromSky = true")]
    private static partial Regex TravelCall();

    /// <summary>A method declaration's own name: "void Depart(", "bool LaunchUp(" (not a call).</summary>
    [GeneratedRegex(@"\b(?:void|bool|int|Task)\s+\w+\($")]
    private static partial Regex Declared();

    [GeneratedRegex(@"//\s*travel:\s*([\w ,]+)")]
    private static partial Regex Tag();

    private static IEnumerable<(string File, int Line, string Text, string[] Tags)> TravelLines()
    {
        foreach (var (path, lines) in AppSource.ProductFiles())
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var text = lines[i];
                var trimmed = text.TrimStart();
                if (trimmed.StartsWith("///", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                var code = text.Split("//")[0];
                var calls = TravelCall().Matches(code).Where(m => !Declared().IsMatch(code[..(m.Index + m.Length)])).ToList();
                if (calls.Count == 0) continue;
                var tag = Tag().Match(text);
                var tags = tag.Success ? tag.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
                yield return (path, i + 1, text.Trim(), tags);
            }
        }
    }

    [Fact]
    public void Every_route_has_one_rule_that_says_why()
    {
        foreach (var r in Enum.GetValues<TravelRoute>()) TravelRoutes.Rules.Count(x => x.Route == r).Should().Be(1, $"{r}");
        TravelRoutes.Rules.Should().OnlyContain(r => r.Why.Length > 15 && r.Code.Length > 3);
        // From the sky by design, or not a departure at all: everything else leaves from a launcher.
        TravelRoutes.Rules.Where(r => !r.FromLauncher).Select(r => r.Route).Should().BeEquivalentTo(
            [TravelRoute.OverviewLanding, TravelRoute.OverviewNavigation, TravelRoute.HazardFall, TravelRoute.DepartureRevert, TravelRoute.SessionArrival, TravelRoute.LegacyAerial, TravelRoute.LegacyMapEnter]);
        TravelRoutes.Rules.Where(r => r.Entry is DepartureEntry.Overview or DepartureEntry.FallWayBack).Should().OnlyContain(r => !r.FromLauncher);
        Enum.GetValues<DepartureEntry>().Should().OnlyContain(e => TravelRoutes.Rules.Any(r => r.Entry == e), "every departure entry has a route");
    }

    [AppSourceFact]
    public void Every_navigator_move_and_departure_in_the_app_names_its_route()
    {
        var names = Enum.GetNames<TravelRoute>().Append(TravelRoutes.RelayTag).ToHashSet();
        var lines = TravelLines().ToList();
        lines.Should().HaveCountGreaterThan(30, "the app's travel lines are found");
        var untagged = lines.Where(l => l.Tags.Length == 0).Select(l => $"{l.File}:{l.Line}: {l.Text}").ToList();
        untagged.Should().BeEmpty("every line that moves the navigator or departs says which route it is (// travel: Name; TravelRoutes)");
        var unknown = lines.SelectMany(l => l.Tags.Where(t => !names.Contains(t)).Select(t => $"{l.File}:{l.Line}: {t}")).ToList();
        unknown.Should().BeEmpty("a tag names a TravelRoute (or relay)");
        var tagged = lines.SelectMany(l => l.Tags).ToHashSet();
        Enum.GetNames<TravelRoute>().Where(n => !tagged.Contains(n)).Should().BeEmpty("every route in the inventory is somewhere in the app");
    }

    [AppSourceFact]
    public void Only_the_sky_routes_come_down_out_of_the_light()
    {
        var sky = TravelRoutes.Rules.Where(r => !r.FromLauncher).Select(r => r.Route.ToString()).ToHashSet();
        var fromSky = TravelLines().Where(l => l.Text.Contains("_arriveFromSky = true", StringComparison.Ordinal)).ToList();
        fromSky.Should().NotBeEmpty();
        fromSky.Should().OnlyContain(l => l.Tags.Length > 0 && l.Tags.All(sky.Contains), "a descent out of the light skips the launcher: only the overview and a fall may");
    }

    [AppSourceFact]
    public void Every_launcher_entry_is_used_by_the_app()
    {
        var code = string.Join('\n', AppSource.ProductFiles().SelectMany(f => f.Lines));
        foreach (var entry in TravelRoutes.Rules.Where(r => r.FromLauncher && r.Entry != null).Select(r => r.Entry!.Value).Distinct())
        {
            code.Should().Contain($"DepartureEntry.{entry}", $"{entry} is a launcher route's entry: some departure must ask for it");
        }
    }

    /// <summary>Each launcher route's entry, asked from the meadow of a world with a child, a deeper folder and a place elsewhere, plans a pad.</summary>
    [Fact]
    public void Every_launcher_route_leaves_from_a_launcher_when_one_leads_there()
    {
        var here = new Spec("here").Dir(new Spec("child").Dir(new Spec("deeper"))).File("a.txt");
        var tree = new Spec("top").Dir(here).Dir(new Spec("elsewhere")).Build();
        int Id(string n) => tree.Find(n).Id;
        var comp = PlanetComposer.Compose(tree, Id("here"), PlanetParams.Default);
        foreach (var rule in TravelRoutes.Rules.Where(r => r.FromLauncher))
        foreach (var target in new[] { "child", "deeper", "elsewhere", "top" })
        {
            var entry = rule.Entry!.Value;
            if (entry == DepartureEntry.TripHop && target != "child") continue; // a hop is always to the next world down
            var plan = DeparturePlanner.Plan(new DepartureQuery(tree, comp, comp.Arrival.Up, Id(target), entry));
            plan.Pad.Should().NotBeNull($"{rule.Route} ({entry}) to {target}");
            plan.Kind.Should().BeOneOf([DepartureKind.RunToPad, DepartureKind.OnPad], $"{rule.Route} to {target}");
        }
    }
}
