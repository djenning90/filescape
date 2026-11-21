using System.Text.RegularExpressions;
using FileScape.Core.Interaction;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-141..143 (AD-92): which game commands act in which play context (<see cref="PlayCommands"/>, read by
/// <c>Main.HandleCommand</c> and <c>PlanetWorld.TelescopeKey</c>). DJ: legacy views were reached by accident from the
/// overview, panels and pictures. Run against the old rule (every command acts everywhere), the legacy-switch and
/// telescope tests fail; the source test keeps the command list and the app's bindings in step.
/// </summary>
public class PlayCommandsTests
{
    private static readonly PlayContext[] PlanetsContexts = [PlayContext.PlanetWorld, PlayContext.Overview, PlayContext.Telescope];

    [Fact]
    public void No_planets_context_switches_to_a_legacy_view()
    {
        foreach (var context in PlanetsContexts)
        foreach (var command in PlayCommands.LegacySwitches)
        {
            PlayCommands.Answer(context, command).Should().Be(CommandAnswer.IgnoreLegacySwitch, $"{command} in {context}: the Legacy views menu is the deliberate way");
        }

        PlayCommands.LegacySwitches.Should().BeEquivalentTo([GameCommand.WorldToggle, GameCommand.SwitchVis, GameCommand.MapToggle]);
        PlayCommands.LegacyHint.Should().Contain("Legacy views");
    }

    [Fact]
    public void The_legacy_views_keep_every_command_so_V_returns_to_the_planets()
    {
        foreach (var context in new[] { PlayContext.Places, PlayContext.Map })
        foreach (var command in Enum.GetValues<GameCommand>())
        {
            PlayCommands.Answer(context, command).Should().Be(CommandAnswer.Act, $"{command} in {context}");
        }
    }

    [Fact]
    public void Every_other_command_acts_on_a_world_and_in_the_overview()
    {
        foreach (var context in new[] { PlayContext.PlanetWorld, PlayContext.Overview })
        foreach (var command in Enum.GetValues<GameCommand>().Except(PlayCommands.LegacySwitches))
        {
            PlayCommands.Answer(context, command).Should().Be(CommandAnswer.Act, $"{command} in {context}");
        }
    }

    /// <summary>The telescope stepped back for every key it did not use (F, B, V, Tab, X…) and then ran it.</summary>
    [Fact]
    public void The_telescope_steps_back_only_for_commands_that_go_somewhere_and_keeps_its_view_for_the_panels()
    {
        foreach (var c in new[] { GameCommand.HistoryBack, GameCommand.UpPlace, GameCommand.EnterFolder, GameCommand.GoToPath, GameCommand.Locations, GameCommand.OpenFolder, GameCommand.Destinations, GameCommand.FlightToggle, GameCommand.Home, GameCommand.Readme })
        {
            PlayCommands.Answer(PlayContext.Telescope, c).Should().Be(CommandAnswer.StepBackThenAct, $"{c}: you leave the eyepiece for it");
        }

        foreach (var c in new[] { GameCommand.Help, GameCommand.Settings, GameCommand.Legend, GameCommand.Quality, GameCommand.Stats, GameCommand.HiddenToggle, GameCommand.Pin })
        {
            PlayCommands.Answer(PlayContext.Telescope, c).Should().Be(CommandAnswer.Act, $"{c}: opens over the view, which stays");
        }

        foreach (var c in new[] { GameCommand.Frame, GameCommand.Overview, GameCommand.DiscoveryPause })
        {
            PlayCommands.Answer(PlayContext.Telescope, c).Should().Be(CommandAnswer.Ignore, $"{c} means nothing at the eyepiece");
        }
    }

    [Fact]
    public void Every_context_answers_every_command()
    {
        foreach (var context in Enum.GetValues<PlayContext>())
        foreach (var command in Enum.GetValues<GameCommand>())
        {
            Enum.IsDefined(PlayCommands.Answer(context, command)).Should().BeTrue();
        }

        PlanetsContexts.Should().OnlyContain(c => PlayCommands.IsPlanets(c));
        PlayCommands.IsPlanets(PlayContext.Places).Should().BeFalse();
        PlayCommands.IsPlanets(PlayContext.Map).Should().BeFalse();
    }

    /// <summary>Every command binding in the app (<c>InputBindings</c>: <c>public const string X = "cmd_…"</c>) has a
    /// <see cref="GameCommand"/> of the same name, and the other way round, so a new shortcut gets an answer here.</summary>
    [AppSourceFact]
    public void Every_command_binding_has_a_command()
    {
        var source = AppSource.Read("InputBindings.cs")!;
        var bound = Regex.Matches(source, @"\b(\w+)\s*=\s*""cmd_\w+""").Select(m => m.Groups[1].Value).ToHashSet();
        bound.Should().NotBeEmpty();
        bound.Should().BeEquivalentTo(Enum.GetNames<GameCommand>(), "InputBindings' commands and GameCommand list the same shortcuts");
    }
}

/// <summary>A test that reads the app's sources (<see cref="AppSource"/>): skipped with a reason outside a full checkout.</summary>
public sealed class AppSourceFactAttribute : FactAttribute
{
    public AppSourceFactAttribute()
    {
        if (AppSource.ScriptsDirectory == null) Skip = "Needs the repository's app/Scripts (a full checkout): the test checks the app against a Core rule.";
    }
}

/// <summary>
/// The app's C# sources, for the few Core tests that check the app keeps to a Core rule (every command bound has an
/// answer, every travel route is inventoried). Found by walking up from the test assembly to the repository's <c>app/Scripts</c>.
/// </summary>
public static class AppSource
{
    /// <summary>The <c>app/Scripts</c> directory, or null when the tests run outside a full checkout.</summary>
    public static string? ScriptsDirectory { get; } = Find();

    private static string? Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var scripts = Path.Combine(d.FullName, "app", "Scripts");
            if (File.Exists(Path.Combine(d.FullName, "app", "project.godot")) && Directory.Exists(scripts)) return scripts;
        }

        return null;
    }

    /// <summary>One file under <c>app/Scripts</c> (relative path), or null without a checkout.</summary>
    public static string? Read(string relative)
    {
        if (ScriptsDirectory == null) return null;
        return File.ReadAllText(Path.Combine(ScriptsDirectory, relative));
    }

    /// <summary>Every app source file that is product code (not a probe or automation instrument), with its relative path.</summary>
    public static IEnumerable<(string Path, string[] Lines)> ProductFiles()
    {
        if (ScriptsDirectory == null) yield break;
        foreach (var f in Directory.EnumerateFiles(ScriptsDirectory, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(f);
            if (name.Contains("Probe", StringComparison.Ordinal) || name.StartsWith("Automation", StringComparison.Ordinal)) continue;
            yield return (Path.GetRelativePath(ScriptsDirectory, f).Replace('\\', '/'), File.ReadAllLines(f));
        }
    }
}
