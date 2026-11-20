using System.Numerics;
using FileScape.Core.Planets;

namespace FileScape.Core.Tests;

/// <summary>
/// AD-85 (PLANET-125/126): the telescope's survey — which worlds it frames, in what order the keys step through them, where
/// it starts, and what it says about each one's relation to the world you stand on — through <see cref="TelescopeSurvey.Plan"/>,
/// the entry point the app calls when you look through the telescope.
/// </summary>
public class TelescopeSurveyTests
{
    // Standing on a planet's north pole: up +Y, the telescope pointing along −Z (its "ahead"); right is +X.
    private static readonly Vector3 Eye = new(0, 10, 0), Up = Vector3.UnitY, Pointing = -Vector3.UnitZ;

    /// <summary>A world in the sky at <paramref name="bearingDegrees"/> (+ right of where the telescope points), 30° up, 40 m away.</summary>
    private static SurveyCandidate At(int id, string name, float bearingDegrees, SurveyRelation relation = SurveyRelation.Child, int folders = 2, int files = 5, bool visited = false, bool listed = true, bool followable = true)
    {
        var b = bearingDegrees * MathF.PI / 180;
        var e = 30 * MathF.PI / 180;
        var dir = Vector3.Normalize(Pointing * MathF.Cos(b) * MathF.Cos(e) + Vector3.UnitX * MathF.Sin(b) * MathF.Cos(e) + Up * MathF.Sin(e));
        return new SurveyCandidate(id, name, relation, Eye + dir * 40, 3f, folders, files, visited, listed, followable);
    }

    [Fact]
    public void It_frames_the_worlds_from_left_to_right_and_starts_on_the_one_it_points_at()
    {
        var s = TelescopeSurvey.Plan([At(1, "Music", 70), At(2, "Docs", -40), At(3, "Up", 170, SurveyRelation.Parent), At(4, "Code", 5)], Eye, Up, Pointing);
        s.Targets.Select(t => t.World.NodeId).Should().Equal([2, 4, 1, 3], "left to right round the sky (bearing −40°, 5°, 70°, 170°)");
        s.Current!.World.Name.Should().Be("Code", "the world nearest where the telescope points");
        s.Current.BearingDegrees.Should().BeApproximately(5, 0.01f);
        s.Current.ElevationDegrees.Should().BeApproximately(30, 0.01f);
        s.Current.DistanceMetres.Should().BeApproximately(40, 0.01f);
        s.Step(1);
        s.Current!.World.Name.Should().Be("Music", "→ steps right");
        s.Step(1);
        s.Step(1);
        s.Current!.World.Name.Should().Be("Docs", "past the last it wraps round to the first");
        s.Step(-1);
        s.Current!.World.Name.Should().Be("Up", "← steps left, wrapping too");
        s.Counter.Should().Be("4 of 4");
    }

    [Fact]
    public void A_world_already_chosen_is_where_it_starts_and_a_click_aims_it()
    {
        var s = TelescopeSurvey.Plan([At(1, "Music", 70), At(2, "Docs", -40), At(4, "Code", 5)], Eye, Up, Pointing, preferredId: 1);
        s.Current!.World.NodeId.Should().Be(1, "the shared selection, if it is in this sky");
        s.Aim(2).Should().BeTrue();
        s.Current!.World.NodeId.Should().Be(2);
        s.Aim(99).Should().BeFalse("a world not in this sky");
        s.Current!.World.NodeId.Should().Be(2, "and the aim stays where it was");
        TelescopeSurvey.Plan([At(4, "Code", 5)], Eye, Up, Pointing, preferredId: 99).Current!.World.NodeId.Should().Be(4, "a selection elsewhere does not count");
    }

    [Fact]
    public void Without_folders_it_starts_on_the_world_above_and_with_nothing_it_says_so()
    {
        var parentOnly = TelescopeSurvey.Plan([At(3, "Starter Sky", 170, SurveyRelation.Parent)], Eye, Up, Pointing);
        parentOnly.Current!.World.Relation.Should().Be(SurveyRelation.Parent);
        var none = TelescopeSurvey.Plan([], Eye, Up, Pointing);
        none.IsEmpty.Should().BeTrue();
        none.Current.Should().BeNull();
        none.Step(1); // no throw
        TelescopeSurvey.EmptyText("Notes").Should().Be("Nothing hangs in this sky: Notes has no folders inside it. J lists its files.");
    }

    [Fact]
    public void A_long_ring_of_moons_is_cut_to_the_ones_nearest_its_view_and_the_rest_are_counted()
    {
        var moons = Enumerable.Range(0, 30).Select(i => At(100 + i, $"moon {i}", -175 + i * 12, SurveyRelation.Moon)).ToList();
        var s = TelescopeSurvey.Plan([.. moons, At(1, "Big", 20)], Eye, Up, Pointing);
        s.Targets.Count(t => t.World.Relation == SurveyRelation.Moon).Should().Be(TelescopeSurvey.MaxRingMoons);
        s.OmittedMoons.Should().Be(30 - TelescopeSurvey.MaxRingMoons);
        s.Targets.Where(t => t.World.Relation == SurveyRelation.Moon).Should().OnlyContain(t => MathF.Abs(t.BearingDegrees) <= 60, "the moons kept are those nearest where it points");
        s.Targets.Select(t => t.BearingDegrees).Should().BeInAscendingOrder();
        s.Current!.World.Name.Should().NotStartWith("moon", "a hung world wins the start over a moon");
    }

    [Fact]
    public void Each_world_says_how_it_relates_to_where_you_stand()
    {
        TelescopeSurvey.RelationText(At(1, "Music", 0), "Starter Sky").Should().Be("a world inside Starter Sky");
        TelescopeSurvey.RelationText(At(1, "Pebble", 0, SurveyRelation.Moon), "Starter Sky").Should().Be("a moon in Starter Sky's ring: a small world inside it");
        TelescopeSurvey.RelationText(At(1, "This PC", 0, SurveyRelation.Parent), "Starter Sky").Should().Be("the world Starter Sky is inside · U flies up to it");
        TelescopeSurvey.RelationText(At(1, "Elsewhere", 0, SurveyRelation.Link), "Starter Sky").Should().Be("a link from Starter Sky to another place");
        TelescopeSurvey.RelationText(At(1, "Gone", 0, SurveyRelation.Link, followable: false), "Starter Sky").Should().Be("a link from Starter Sky · it can't be followed");

        TelescopeSurvey.DetailText(At(1, "Music", 0, folders: 3, files: 12)).Should().Be("3 folders · 12 files");
        TelescopeSurvey.DetailText(At(1, "Music", 0, folders: 1, files: 1, visited: true)).Should().Be("1 folder · 1 file · you have been there");
        TelescopeSurvey.DetailText(At(1, "Music", 0, folders: 0, files: 0)).Should().Be("empty");
        TelescopeSurvey.DetailText(At(1, "Music", 0, folders: 0, files: 0, listed: false)).Should().Be("not listed yet: going there lists it");
    }
}
