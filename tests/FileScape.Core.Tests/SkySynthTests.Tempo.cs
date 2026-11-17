using System.Security.Cryptography;
using FileScape.Core.Audio;
using FileScape.Core.Planets;

namespace FileScape.Core.Tests;

/// <summary>Runs only when SKYSYNTH_CYCLE_HASHES_OUT names an output file (AD-66's per-score audio hashes); otherwise skipped.</summary>
public sealed class SkySynthCycleHashFactAttribute : FactAttribute
{
    public SkySynthCycleHashFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKYSYNTH_CYCLE_HASHES_OUT")))
        {
            Skip = "Set SKYSYNTH_CYCLE_HASHES_OUT to a file to write a hash of a whole cycle of every score at visits 1–3.";
        }
    }
}

/// <summary>
/// PLANET-64/65 (AD-66): the orchestra's tempo lift and the second themes. Here: the instrument that shows which scores a
/// change touched (a hash of a whole cycle of every score, first to third visit, before and after), and the tests.
/// </summary>
public partial class SkySynthTests
{
    /// <summary>
    /// Writes, per score and visit (1–3), a SHA-256 of one whole cycle plus two seconds rendered from its first downbeat
    /// (the Library key, seed 66), so a before/after diff names exactly the scores and visits a change reached. The behaviour
    /// baseline only covers six identities' first 6 s at visit 1; this covers the rest (the drama, revisits, the abyss).
    /// </summary>
    [SkySynthCycleHashFact]
    public void Write_a_hash_of_a_whole_cycle_of_every_score()
    {
        var path = Environment.GetEnvironmentVariable("SKYSYNTH_CYCLE_HASHES_OUT")!;
        var lines = new List<string>();
        foreach (var id in Enum.GetValues<MusicIdentity>())
        {
            for (var visits = 1; visits <= 3; visits++)
            {
                var s = new SkySynth(66, PlanetTheme.Library, Rate, CharacterOf(id));
                s.SetWorld(PlanetTheme.Library, CharacterOf(id), visits, id); // before the first downbeat: a retarget
                var seconds = SkySynth.CycleBars * 16 * ThemeFramesPerSixteenth(id) / Rate + 2;
                var buf = RenderSeconds(s, seconds);
                var bytes = new byte[buf.Length * 4];
                Buffer.BlockCopy(buf, 0, bytes, 0, bytes.Length);
                lines.Add($"{id} visits {visits}: {Convert.ToHexString(SHA256.HashData(bytes))}");
            }
        }

        File.WriteAllLines(path, lines);
        foreach (var l in lines) output.WriteLine(l);
    }
}

/// <summary>Runs only when SKYSYNTH_MUSIC66_DIR names an output folder (AD-66's before-and-after listening samples); otherwise skipped.</summary>
public sealed class SkySynthMusic66FactAttribute : FactAttribute
{
    public SkySynthMusic66FactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKYSYNTH_MUSIC66_DIR")))
        {
            Skip = "Set SKYSYNTH_MUSIC66_DIR to a folder to render the AD-66 tempo and second-theme samples.";
        }
    }
}

public partial class SkySynthTests
{
    /// <summary>
    /// The folder DJ heard (/home/david/src/possibility-engine/src on the Ubuntu-24.04 distribution) as FileScape sees it
    /// from its path alone: five levels below the distribution's root, so depth band 3 (the deep woods, the orchestra),
    /// inside the possibility-engine repository (its parent), in the Workshop key (a source folder). The repository id is
    /// arbitrary here (the app's is the tree's node id); it seeds the deck's random stream, not the score.
    /// </summary>
    private static readonly WorldCharacter PossibilityEngineSrc = new(WorldKind.Folder, 5, 3, 4242);

    /// <summary>The repository above it (possibility-engine), where a flight into src starts.</summary>
    private static readonly WorldCharacter PossibilityEngine = new(WorldKind.Repository, 4, 0, 4242);

    /// <summary>
    /// The samples (SKYSYNTH_MUSIC66_DIR): the same script on the build before AD-66 (names given by SKYSYNTH_MUSIC66_TAG,
    /// "before" or "after"), clip for clip. (1) The flight from possibility-engine into src and the first ≈ 26 s there
    /// (arrival, the theme); (2) the same visit's bars 11–16 (the horn call, the general pause, the climax, the harp);
    /// (3) per folder band, a revisit's bars where the second theme now plays (the band motif before).
    /// </summary>
    [SkySynthMusic66Fact]
    public void Render_tempo_and_second_theme_samples_to_wav()
    {
        var dir = Environment.GetEnvironmentVariable("SKYSYNTH_MUSIC66_DIR")!;
        var tag = Environment.GetEnvironmentVariable("SKYSYNTH_MUSIC66_TAG") ?? "after";
        Directory.CreateDirectory(dir);
        double Bar(MusicIdentity id) => 16 * ThemeFramesPerSixteenth(id) / Rate;

        {
            var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Workshop, Rate, PossibilityEngine));
            var notes = new List<MelodyNote>();
            r.Synth.MelodyObserver = notes.Add;
            r.Walk(1.5);
            r.Flight(PlanetTheme.Workshop, PossibilityEngineSrc, 1);
            var land = r.T;
            var downbeat = land + 60.0 / SkySynth.TempoFor(MusicIdentity.DeepWoods);
            r.Walk(downbeat + 16 * Bar(MusicIdentity.DeepWoods) + 1.5 - r.T);
            SaveWindow(r, Path.Combine(dir, $"orchestra-src-arrival-{tag}.wav"), land - 0.5, land + 26);
            SaveWindow(r, Path.Combine(dir, $"orchestra-src-climax-{tag}.wav"), downbeat + 11 * Bar(MusicIdentity.DeepWoods) - 0.3, downbeat + 16 * Bar(MusicIdentity.DeepWoods) + 1.5);
            output.WriteLine($"orchestra: {SkySynth.TempoFor(MusicIdentity.DeepWoods)} BPM pulse, theme {SkySynth.ThemeTempoFor(MusicIdentity.DeepWoods)} BPM, bar {Bar(MusicIdentity.DeepWoods):0.00} s; landed {land:0.00} s");
            ReportBands(r, $"orchestra-src-{tag}", downbeat, Bar(MusicIdentity.DeepWoods));
            LogLine(notes, (long)(downbeat * Rate), (long)((downbeat + 16 * Bar(MusicIdentity.DeepWoods)) * Rate), "orchestra-src first visit");
        }

        void Revisit(string name, PlanetTheme theme, WorldCharacter character, int fromBar, int toBar)
        {
            var id = SkySynth.IdentityOf(character);
            var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Meadow, Rate, Band(1, repositoryId: 99)));
            var notes = new List<MelodyNote>();
            r.Synth.MelodyObserver = notes.Add;
            r.Walk(1.0);
            r.Flight(theme, character, 2); // a second visit (not remembered: it starts on its second cycle)
            var downbeat = r.T + 60.0 / SkySynth.TempoFor(id);
            r.Walk(downbeat + toBar * Bar(id) + 1.0 - r.T);
            SaveWindow(r, Path.Combine(dir, $"{name}-{tag}.wav"), downbeat + fromBar * Bar(id) - 0.3, downbeat + toBar * Bar(id) + 1.0);
            LogLine(notes, (long)((downbeat + 8 * Bar(id)) * Rate), (long)((downbeat + 12 * Bar(id)) * Rate), name + " bars 8–11");
        }

        Revisit("revisit-outskirts", PlanetTheme.Hearth, Band(1), 6, 14);
        Revisit("revisit-trails-festival", PlanetTheme.Library, Band(2), 6, 14);
        Revisit("revisit-orchestra-src", PlanetTheme.Workshop, PossibilityEngineSrc, 7, 12);
        Revisit("revisit-underglow-dnb", PlanetTheme.Crystal, Band(4), 7, 12);
    }

    /// <summary>Logs the melodic notes (theme, motif, second theme) between two frames as note names: the listen-proxy.</summary>
    private void LogLine(List<MelodyNote> notes, long from, long to, string label)
    {
        string Name(int midi) => new[] { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" }[midi % 12] + (midi / 12 - 1);
        var parts = notes.Where(n => n.Frame >= from && n.Frame < to && n.Part != MelodyPart.Grace && n.Part != MelodyPart.Counter)
            .GroupBy(n => n.Part)
            .Select(g => $"{g.Key}: {string.Join(" ", g.Select(n => Name(n.MidiNote)))}");
        output.WriteLine($"{label}: {string.Join(" | ", parts)}");
    }
}

public partial class SkySynthTests
{
    [Fact]
    public void The_orchestra_is_lifted_modestly_and_keeps_its_half_time_shape()
    {
        var lift = SkySynth.TempoFor(MusicIdentity.DeepWoods) / 144.0;
        output.WriteLine($"deep woods: {SkySynth.TempoFor(MusicIdentity.DeepWoods)} BPM pulse (was 144), theme {SkySynth.ThemeTempoFor(MusicIdentity.DeepWoods)} BPM (was 72): +{(lift - 1) * 100:0.0} %");
        lift.Should().BeInRange(1.06, 1.10, "DJ asked for a modest lift (AD-66)");
        SkySynth.ThemeTempoFor(MusicIdentity.DeepWoods).Should().Be(SkySynth.TempoFor(MusicIdentity.DeepWoods) / 2, "still the broad half-time theme over the ostinato");
        SkySynth.TempoFor(MusicIdentity.DeepWoods).Should().BeLessThan(SkySynth.TempoFor(MusicIdentity.Underglow), "the bands still quicken with depth");
        SkySynth.ThemeTempoFor(MusicIdentity.DeepWoods).Should().BeLessThan(SkySynth.ThemeTempoFor(MusicIdentity.Hazard));
    }

    /// <summary>
    /// The lifted orchestra keeps the launch and the landing on its beat: a wind-up stretched by AlignToGrid launches on a
    /// sixteenth of the new pulse, and after a flight from possibility-engine into src the theme's first note comes within one
    /// pulse beat of the arrival, as written (plain, every note in G dorian).
    /// </summary>
    [Fact]
    public void In_the_lifted_orchestra_launches_land_on_the_grid_and_the_theme_starts_as_written()
    {
        var sixteenth = FramesPerSixteenth(MusicIdentity.DeepWoods);
        var s = SynthFor(5, PlanetTheme.Workshop, MusicIdentity.DeepWoods);
        RenderSeconds(s, 3.3);
        var windUp = s.AlignToGrid(TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(0.09));
        var launchFrame = s.FramesRendered + windUp.TotalSeconds * Rate;
        var offGrid = Math.Abs(launchFrame / sixteenth - Math.Round(launchFrame / sixteenth)) * sixteenth;
        output.WriteLine($"wind-up {windUp.TotalMilliseconds:0.0} ms, launch {offGrid:0.0} frames off the 156 BPM sixteenth grid");
        offGrid.Should().BeLessThan(2, "the launch lands on a sixteenth of the lifted pulse");

        var r = new Route(new SkySynth(seed: 2026, PlanetTheme.Workshop, Rate, PossibilityEngine));
        var theme = new List<MelodyNote>();
        r.Synth.MelodyObserver = n =>
        {
            if (n.Part == MelodyPart.Theme) theme.Add(n);
        };
        r.Walk(1.5);
        r.Flight(PlanetTheme.Workshop, PossibilityEngineSrc, 1);
        var arrival = (long)(r.T * Rate);
        theme.Clear();
        r.Walk(30);
        var first = theme[0];
        var wait = (first.Frame - arrival) / (double)Rate;
        output.WriteLine($"first theme note {wait * 1000:0} ms after the arrival ({first.Variation}); one pulse beat = {60000 / SkySynth.TempoFor(MusicIdentity.DeepWoods):0} ms");
        wait.Should().BeLessThanOrEqualTo(60.0 / SkySynth.TempoFor(MusicIdentity.DeepWoods) + 0.03, "the new world starts on the next beat");
        first.Variation.Should().Be(ThemeVariation.Plain, "the theme as written on arrival");
        var key = SkySynth.KeyFor(PlanetTheme.Workshop, PossibilityEngineSrc);
        var fromFirst = theme.Select(n => n with { Frame = n.Frame - first.Frame }).ToList();
        ThemeMatch(fromFirst, key, 0, ThemeFramesPerSixteenth(MusicIdentity.DeepWoods)).Should().Be(1, $"every note of the written theme, in {key.Mode}");
    }
}
