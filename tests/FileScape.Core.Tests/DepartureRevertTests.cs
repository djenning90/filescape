using FileScape.Core.Model;
using FileScape.Core.Places;

namespace FileScape.Core.Tests;

/// <summary>
/// Esc during a departure the navigator already made (PLANET-121, AD-84): a change chosen in a dialog moves the
/// navigator at once, so stopping its run must put the navigator back exactly as it was (current folder, the link it
/// was entered through, history) — and nothing else.
/// </summary>
public class DepartureRevertTests
{
    private static (FsTree Tree, PlaceNavigator Nav, List<PlaceChange> Changes) Start()
    {
        var tree = new Spec("top").Dir(new Spec("a").Dir(new Spec("a1"))).Dir(new Spec("b")).Build();
        var nav = new PlaceNavigator();
        nav.Reset(0);
        var changes = new List<PlaceChange>();
        nav.Changed += changes.Add;
        return (tree, nav, changes);
    }

    [Fact]
    public void Revert_puts_the_navigator_back_where_it_was()
    {
        var (t, nav, changes) = Start();
        nav.Enter(t, t.Find("a").Id).Should().BeTrue();
        var history = nav.History.ToList();
        nav.GoTo(t, t.Find("b").Id).Should().BeTrue(); // a favourite chosen in Destinations
        var change = changes[^1];
        nav.CanRevert(change).Should().BeTrue();
        nav.Revert(change).Should().BeTrue();
        nav.Current.Should().Be(t.Find("a").Id);
        nav.History.Should().Equal(history, "the reverted move leaves no trace in history");
        changes[^1].Should().Be(new PlaceChange(change.To, change.From, PlaceMove.Back, change.ViaLink), "the app is told it is back");
    }

    [Fact]
    public void Revert_refuses_a_change_that_is_not_the_last_or_was_itself_back()
    {
        var (t, nav, changes) = Start();
        nav.Enter(t, t.Find("a").Id);
        var first = changes[^1];
        nav.Enter(t, t.Find("a1").Id);
        nav.CanRevert(first).Should().BeFalse("another move came after it");
        nav.Revert(first).Should().BeFalse();
        nav.Back();
        nav.CanRevert(changes[^1]).Should().BeFalse("Back is not undone by Back (it popped history)");
        nav.Current.Should().Be(t.Find("a").Id);
    }

    [Fact]
    public void Revert_refuses_after_a_reset()
    {
        var (t, nav, changes) = Start();
        nav.GoTo(t, t.Find("b").Id);
        var change = changes[^1];
        nav.Reset(t.Find("a").Id); // Open folder's new root: no history to return through
        nav.CanRevert(change).Should().BeFalse();
    }
}
