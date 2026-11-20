using FileScape.Core.Interaction;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-87 (AD-76): the selection card's actions for every kind of target in every view — a label shows a shortcut
/// only where the key does the same, a disabled action says why, and the hub is never handed to Windows as a path.
/// </summary>
public class SelectionActionsTests
{
    private static CardTargetInfo T(CardTarget kind, bool live = true, bool top = false, bool standing = false, bool inView = true) =>
        new(kind, kind == CardTarget.Hub ? "This computer" : "Item", live, top || kind == CardTarget.Hub, standing, inView);

    private static readonly CardView[] Views = [CardView.Map, CardView.Places, CardView.PlanetSurface, CardView.PlanetOverview];
    private static readonly CardTarget[] Kinds = [CardTarget.File, CardTarget.Folder, CardTarget.Link, CardTarget.LocationRoot, CardTarget.Hub];

    [Fact]
    public void Every_disabled_action_says_why_and_every_label_shows_only_its_own_views_keys()
    {
        foreach (var view in Views)
        foreach (var kind in Kinds)
        foreach (var live in new[] { true, false })
        foreach (var inView in new[] { true, false })
        {
            var a = SelectionActions.For(view, T(kind, live, inView: inView));
            foreach (var act in new[] { a.Open, a.Reveal, a.Parent, a.Fly, a.Copy })
            {
                if (!act.Enabled) { act.Tooltip.Should().StartWith(SelectionActions.Unavailable); act.Tooltip.Length.Should().BeGreaterThan(SelectionActions.Unavailable.Length + 10, "a reason follows"); }
                act.Tooltip.Should().NotBeNullOrWhiteSpace();
            }

            // Esc selects the parent only on a map; Enter opens everywhere except the overview, where it lands.
            a.Parent.Label.Contains("(Esc)").Should().Be(view == CardView.Map);
            a.Open.Label.Contains("(Enter)").Should().Be(view != CardView.PlanetOverview);
        }
    }

    [Fact]
    public void The_hub_opens_This_PC_and_offers_nothing_that_needs_a_path()
    {
        foreach (var view in Views)
        {
            var a = SelectionActions.For(view, T(CardTarget.Hub));
            a.Open.Enabled.Should().BeTrue();
            a.Open.Tooltip.Should().Contain("This PC");
            a.Reveal.Enabled.Should().BeFalse();
            a.Copy.Enabled.Should().BeFalse();
            a.Parent.Enabled.Should().BeFalse();
        }

        SelectionActions.ThisPcShellPath.Should().Be("shell:MyComputerFolder");
    }

    [Fact]
    public void On_a_planet_F_faces_what_is_on_this_world_and_says_why_not_otherwise()
    {
        SelectionActions.For(CardView.PlanetSurface, T(CardTarget.File)).Fly.Should().Be(new CardAction(true, "Face it (F)", "Turn to face it on this world or in its sky."));
        SelectionActions.For(CardView.PlanetSurface, T(CardTarget.Folder, standing: true)).Fly.Tooltip.Should().Contain("standing on");
        var far = SelectionActions.For(CardView.PlanetSurface, T(CardTarget.LocationRoot, inView: false)).Fly;
        far.Enabled.Should().BeFalse();
        far.Tooltip.Should().Contain("Go inside (G)");
        SelectionActions.For(CardView.PlanetOverview, T(CardTarget.Folder)).Fly.Label.Should().Be("Zoom to (F)");
        SelectionActions.For(CardView.PlanetOverview, T(CardTarget.Folder, inView: false)).Fly.Enabled.Should().BeFalse();
        SelectionActions.For(CardView.Map, T(CardTarget.File, inView: false)).Fly.Should().Be(new CardAction(true, "Fly to (F)", "Bring the selection into view."));
    }

    [Fact]
    public void Show_in_Explorer_needs_real_files_and_Parent_needs_something_above()
    {
        SelectionActions.For(CardView.PlanetSurface, T(CardTarget.File, live: false)).Reveal.Enabled.Should().BeFalse();
        SelectionActions.For(CardView.PlanetSurface, T(CardTarget.File)).Reveal.Enabled.Should().BeTrue();
        SelectionActions.For(CardView.Map, T(CardTarget.Folder, top: true)).Parent.Should().Be(new CardAction(false, "Parent (Esc)", "Unavailable: Item is the top: nothing contains it."));
        SelectionActions.For(CardView.PlanetSurface, T(CardTarget.LocationRoot)).Parent.Enabled.Should().BeTrue("a drive's parent is the hub");
    }
}
