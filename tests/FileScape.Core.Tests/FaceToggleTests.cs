using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Units;

namespace FileScape.Core.Tests;

/// <summary>
/// F / Face is reversible (PLANET-123, AD-84): a second press turns back to the view you had before the first, while
/// that is still applicable — the same world, the same selection, not moved or looked away, within the window — and is
/// an ordinary Face otherwise. Driven through the calls PlanetWorld.FaceSelection makes.
/// </summary>
public class FaceToggleTests
{
    private static readonly Vector3 North = -Vector3.UnitZ, East = Vector3.UnitX;

    private static FaceToggle<string>.Where At(double seconds, Vector3? forward = null, Vector3? pos = null, int world = 7, int selection = 3) =>
        new(world, selection, pos ?? Vector3.Zero, forward ?? East, TimeSpan.FromSeconds(seconds));

    private static FaceToggle<string> Faced()
    {
        var t = new FaceToggle<string>();
        t.Faced(At(10), "looking north"); // F: was looking north, now faces the selection (east)
        return t;
    }

    [Fact]
    public void A_second_press_restores_the_view_before_the_first()
    {
        var t = Faced();
        t.Check(At(12)).Should().Be(FaceRestore.Restore);
        t.TryRestore(At(12), out var prior).Should().BeTrue();
        prior.Should().Be("looking north");
        t.Check(At(13)).Should().Be(FaceRestore.NothingToRestore, "a third press faces again");
    }

    [Fact]
    public void Nothing_to_restore_before_any_face()
    {
        new FaceToggle<string>().TryRestore(At(1), out _).Should().BeFalse();
    }

    [Fact]
    public void Another_world_or_selection_is_a_new_face()
    {
        Faced().Check(At(12, world: 8)).Should().Be(FaceRestore.OtherWorld);
        Faced().Check(At(12, selection: 4)).Should().Be(FaceRestore.OtherSelection);
    }

    [Fact]
    public void Moving_away_ends_it_but_a_shuffle_does_not()
    {
        var limit = FaceToggle<string>.DefaultMaxMoved.Value;
        Faced().Check(At(12, pos: new Vector3(limit * 0.5f, 0, 0))).Should().Be(FaceRestore.Restore);
        Faced().Check(At(12, pos: new Vector3(limit * 1.5f, 0, 0))).Should().Be(FaceRestore.Moved);
    }

    [Fact]
    public void Looking_away_ends_it_but_a_nudge_does_not()
    {
        var limit = FaceToggle<string>.DefaultMaxLookedAway.ToRadians();
        Vector3 Turned(float radians) => new(MathF.Cos(radians), 0, MathF.Sin(radians));
        Faced().Check(At(12, forward: Turned(limit * 0.5f))).Should().Be(FaceRestore.Restore);
        Faced().Check(At(12, forward: Turned(limit * 1.5f))).Should().Be(FaceRestore.LookedAway);
        Faced().Check(At(12, forward: North)).Should().Be(FaceRestore.LookedAway, "turned back by hand already");
    }

    [Fact]
    public void Too_late_after_the_window()
    {
        var window = FaceToggle<string>.DefaultWindow.TotalSeconds;
        Faced().Check(At(10 + window * 0.9)).Should().Be(FaceRestore.Restore);
        Faced().Check(At(10 + window * 1.1)).Should().Be(FaceRestore.TooLate);
    }

    [Fact]
    public void A_check_that_is_not_applicable_forgets_so_the_press_faces_again()
    {
        var t = Faced();
        t.TryRestore(At(12, world: 8), out _).Should().BeFalse();
        t.Check(At(12)).Should().Be(FaceRestore.NothingToRestore, "a stale face is not restored later");
    }

    [Fact]
    public void Tolerances_can_be_set_for_another_view()
    {
        var t = new FaceToggle<int>(Meters.Of(100), Degrees.Of(5), TimeSpan.FromSeconds(5));
        t.Faced(new FaceToggle<int>.Where(1, 2, Vector3.Zero, East, TimeSpan.Zero), 42);
        t.Check(new FaceToggle<int>.Where(1, 2, new Vector3(50, 0, 0), East, TimeSpan.FromSeconds(4))).Should().Be(FaceRestore.Restore);
        t.Check(new FaceToggle<int>.Where(1, 2, Vector3.Zero, East, TimeSpan.FromSeconds(6))).Should().Be(FaceRestore.TooLate);
    }
}
