using FileScape.Core.Audio;
using FileScape.Core.Planets;

namespace FileScape.Core.Tests;

/// <summary>
/// AD-85 (PLANET-128): which piece the music plays for what the player is doing, through the entry points the app calls —
/// <see cref="MusicActivity"/> (what the app tells the music each frame: the overlook's hysteresis, the telescope, the
/// overview, travel, the visits per place) and <see cref="SkySynth.PieceFor"/> (the piece an activity plays at a place).
/// Star Chart Dorian is the overview's one identity from every place; the Ionian star chart and the "second" variation
/// are kept, note for note, as reserved arrangements; the Lydian overlook stays on the overlook and at the telescope.
/// These replace the in-app music journey's long listening waits, its second climb and its quick hops (AD-85).
/// </summary>
public class MusicActivityTests
{
    private static readonly WorldCharacter Hub = new(WorldKind.Hub, 0, 0, -1);

    /// <summary>Every kind of place, with its depth bands, inside a project or not (as SkySynthTests.Experience's Places).</summary>
    private static readonly WorldCharacter[] Places =
    [
        Hub, new(WorldKind.WindowsRoot, 0, 0, -1), new(WorldKind.WslRoot, 0, 0, -1), new(WorldKind.Repository, 3, 0, 42),
        new(WorldKind.Repository, 3, 0, 42, Worktree: true), new(WorldKind.Folder, 1, 1, -1), new(WorldKind.Folder, 2, 2, -1),
        new(WorldKind.Folder, 3, 3, -1), new(WorldKind.Folder, 4, 4, -1), new(WorldKind.Folder, 2, 2, 42), new(WorldKind.Folder, 3, 3, 42),
        new(WorldKind.Hazard, 2, 2, -1),
    ];

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1 / 60.0);

    /// <summary>Feeds <paramref name="seconds"/> of frames at 60 fps; returns every change the app would tell the music.</summary>
    private static List<(MusicExperience Experience, int Visits)> Run(MusicActivity a, double seconds, MusicActivity.Moment f)
    {
        var changes = new List<(MusicExperience, int)>();
        for (var t = 0.0; t < seconds - 1e-9; t += Tick.TotalSeconds)
        {
            if (a.Update(Tick, f) is { } change) changes.Add(change);
        }

        return changes;
    }

    [Fact]
    public void The_overview_plays_Star_Chart_Dorian_from_every_place_in_the_places_key()
    {
        foreach (var theme in Enum.GetValues<PlanetTheme>())
        {
            foreach (var c in Places)
            {
                var piece = SkySynth.PieceFor(MusicIdentity.Overview, theme, c);
                piece.Should().NotBeNull();
                piece!.Name.Should().Be("star-chart-dorian", $"{theme} {c}: Star Chart Dorian is the overview's identity everywhere (DJ, 2026-11-20)");
                piece.Mode.Should().Be(DiatonicMode.Dorian);
                SkySynth.ExperienceModeFor(MusicIdentity.Overview, theme, c).Should().Be(DiatonicMode.Dorian);
            }
        }

        ExperiencePieces.Overview.Variations[0].Notes.Should().Equal(ModeSketches.StarChart(DiatonicMode.Dorian, 0).Notes, "the first cycle is the Dorian sample DJ chose, note for note");
        ExperiencePieces.Overview.Variations.Should().HaveCount(2);
        ExperiencePieces.Overview.Variations[1].Name.Should().Be("star-chart-dorian-deep-sky", "its later cycle is the same chart deepened with the aurora voices, not the nursery-like second variation");
    }

    [Fact]
    public void The_Overview_in_the_synth_is_Dorian_on_the_places_root_from_an_Ionian_place()
    {
        var s = new SkySynth(4, PlanetTheme.Library, SkySynth.DefaultSampleRateHz, Hub) { MinimumDwell = SkySynth.GameMinimumDwell };
        s.SetWorld(PlanetTheme.Library, Hub, 1);
        s.Render(new float[SkySynth.DefaultSampleRateHz / 2 * 2]);
        var place = SkySynth.KeyFor(PlanetTheme.Library, Hub);
        place.Mode.Should().NotStartWith("dorian", "the hub is a major place: the case the old scheme gave Ionian");
        s.SetExperience(MusicExperience.Overview);
        var chunk = new float[SkySynth.DefaultSampleRateHz / 100 * 2];
        for (var i = 0; i < 100 && s.PlayingIdentity != MusicIdentity.Overview; i++) s.Render(chunk);
        s.PlayingIdentity.Should().Be(MusicIdentity.Overview);
        s.Key.Mode.Should().StartWith("dorian");
        s.Key.RootMidiNote.Should().Be(place.RootMidiNote, "in the world's key");
    }

    [Fact]
    public void The_Ionian_star_chart_and_the_second_variation_are_kept_note_for_note_as_reserved_arrangements()
    {
        var reserved = ExperiencePieces.Reserved.ToDictionary(r => r.Piece.Name);
        reserved.Keys.Should().BeEquivalentTo(["star-chart-ionian", "star-chart-second"]);
        reserved["star-chart-ionian"].Piece.Variations[0].Notes.Should().Equal(ModeSketches.StarChart(DiatonicMode.Ionian, 0).Notes);
        reserved["star-chart-ionian"].Piece.Variations[1].Notes.Should().Equal(ModeSketches.StarChart(DiatonicMode.Ionian, 1).Notes);
        reserved["star-chart-second"].Piece.Variations[0].Notes.Should().Equal(ModeSketches.StarChart(DiatonicMode.Ionian, 1).Notes, "the second variation as DJ heard it (from the hub, Ionian)");
        reserved["star-chart-second"].Piece.Variations[1].Notes.Should().Equal(ModeSketches.StarChart(DiatonicMode.Dorian, 1).Notes);
        foreach (var r in ExperiencePieces.Reserved) r.Use.Should().NotBeNullOrWhiteSpace("each says where it is meant to go");

        // The arrangements themselves are unchanged (pinned by their notes' fingerprint, taken before AD-85).
        Fingerprint(ModeSketches.StarChart(DiatonicMode.Ionian, 0)).Should().Be(StarChartIonianFingerprint);
        Fingerprint(ModeSketches.StarChart(DiatonicMode.Ionian, 1)).Should().Be(StarChartIonianSecondFingerprint);
        Fingerprint(ModeSketches.StarChart(DiatonicMode.Dorian, 0)).Should().Be(StarChartDorianFingerprint);
        Fingerprint(ModeSketches.StarChart(DiatonicMode.Dorian, 1)).Should().Be(StarChartDorianSecondFingerprint);
    }

    /// <summary>Fingerprints of the star chart's scores as AD-79 wrote them (2026-11-19), taken on 7680d22 before AD-85.</summary>
    private const string StarChartIonianFingerprint = "306:bdf046f14db3a839", StarChartIonianSecondFingerprint = "306:eaeb6821784054eb",
        StarChartDorianFingerprint = "306:e612141fb4ab19fc", StarChartDorianSecondFingerprint = "306:1bd6b0c0c192c366";

    /// <summary>An order-sensitive FNV-1a hash of every note's start (µs), length (µs), part, instrument, pitch, velocity and pan (thousandths), and the count.</summary>
    internal static string Fingerprint(SketchScore s)
    {
        var h = 1469598103934665603UL;
        void Mix(long v)
        {
            unchecked
            {
                h ^= (ulong)v;
                h *= 1099511628211UL;
            }
        }

        foreach (var n in s.Notes)
        {
            Mix((long)Math.Round(n.Start.TotalMicroseconds));
            Mix((long)Math.Round(n.Length.TotalMicroseconds));
            Mix((long)n.Part);
            Mix((long)n.Instrument);
            Mix(n.MidiNote);
            Mix((long)Math.Round(n.Velocity * 1000));
            Mix((long)Math.Round(n.Pan * 1000));
        }

        Mix(s.Notes.Count);
        return $"{s.Notes.Count}:{h:x16}";
    }

    [Fact]
    public void Activity_picks_the_piece_the_overlook_Lydian_the_telescope_Lydian_the_overview_Star_Chart_Dorian()
    {
        MusicActivity.PieceFor(MusicExperience.Overlook, PlanetTheme.Hearth, Hub)!.Name.Should().Be("overlook");
        MusicActivity.PieceFor(MusicExperience.Overlook, PlanetTheme.Hearth, Hub)!.Mode.Should().Be(DiatonicMode.Lydian);
        MusicActivity.PieceFor(MusicExperience.Overview, PlanetTheme.Hearth, Hub)!.Name.Should().Be("star-chart-dorian");
        MusicActivity.PieceFor(MusicExperience.World, PlanetTheme.Hearth, Hub).Should().BeNull("a world plays its own arrangement");

        // The telescope's view is the overlook's music, at once (you are on the platform).
        var a = new MusicActivity();
        Run(a, 0.1, new(Folder: 1, OnOverlook: true, AtTelescope: true)).Should().Equal([(MusicExperience.Overlook, 1)]);
        Run(a, 5, new(Folder: 1, OnOverlook: true, AtTelescope: true)).Should().BeEmpty("it keeps playing while you look");
        Run(a, 5, new(Folder: 1, OnOverlook: true)).Should().BeEmpty("stepping back from the eyepiece keeps it");
    }

    [Fact]
    public void The_overlook_comes_in_after_a_moment_and_leaves_after_a_step_back_the_overview_at_once()
    {
        var a = new MusicActivity();
        Run(a, 0.3, new(Folder: 1, OnOverlook: true)).Should().BeEmpty("a glance from the rim (0.3 s) does not start it");
        Run(a, 0.2, new(Folder: 1, OnOverlook: true)).Should().Equal([(MusicExperience.Overlook, 1)], "0.4 s on it does");
        Run(a, 1.0, new(Folder: 1)).Should().BeEmpty("a step back off the rim for 1 s keeps the piece");
        Run(a, 0.1, new(Folder: 1, OnOverlook: true)).Should().BeEmpty();
        Run(a, 1.3, new(Folder: 1)).Should().Equal([(MusicExperience.World, 1)], "1.2 s off it, the world's music returns");
        Run(a, 0.02, new(Folder: 1, InOverview: true)).Should().Equal([(MusicExperience.Overview, 1)], "T is deliberate: at once");
        Run(a, 0.02, new(Folder: 1)).Should().Equal([(MusicExperience.World, 1)]);
    }

    [Fact]
    public void Travel_ends_the_overlook_at_once_and_visits_count_per_place_and_activity()
    {
        var a = new MusicActivity();
        Run(a, 0.5, new(Folder: 1, OnOverlook: true, AtTelescope: true));
        Run(a, 0.02, new(Folder: 1, OnOverlook: true, Travelling: true)).Should().Equal([(MusicExperience.World, 1)], "a departure from the telescope: the flight's music is the world's (AD-52), at once");
        Run(a, 0.5, new(Folder: 2, OnOverlook: true)).Should().Equal([(MusicExperience.Overlook, 1)], "another world's overlook: its first visit");
        Run(a, 1.3, new(Folder: 2));
        Run(a, 0.5, new(Folder: 2, OnOverlook: true)).Should().Equal([(MusicExperience.Overlook, 2)], "the second climb there: visit 2");
        Run(a, 0.02, new(Folder: 2, InOverview: true)).Should().Equal([(MusicExperience.Overview, 1)], "visits count per activity");
        Run(a, 0.02, new(Folder: 1, InOverview: true)).Should().BeEmpty("the same activity again tells the music nothing");
    }
}
