using System.Diagnostics;
using FileScape.Core.Audio;
using FileScape.Core.Planets;

namespace FileScape.Core.Tests;

/// <summary>Runs only when SKYSYNTH_MUSIC_DIR names an output folder (AD-79's listening samples); otherwise skipped.</summary>
public sealed class SkySynthMusicFactAttribute : FactAttribute
{
    public SkySynthMusicFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKYSYNTH_MUSIC_DIR")))
        {
            Skip = "Set SKYSYNTH_MUSIC_DIR to a folder to render the AD-79 music-integration samples.";
        }
    }
}

/// <summary>Runs only when SKYSYNTH_SCAN_WAV names 16-bit PCM WAV files (separated by ';'): the clipping and gap scan of recordings.</summary>
public sealed class SkySynthScanFactAttribute : FactAttribute
{
    public SkySynthScanFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKYSYNTH_SCAN_WAV")))
        {
            Skip = "Set SKYSYNTH_SCAN_WAV to WAV files (';'-separated) to scan them for clipping and silence gaps.";
        }
    }
}

/// <summary>
/// AD-79 (PLANET-97..99): the variation scheme (activity picks the piece, place its key and mode, visits the variation),
/// the minimum dwell, the experiences' pieces (the Lydian overlook, the star chart) and the aurora voice. The existing
/// arrangements are pinned where they were: every place plays the identity it played before, with or without an
/// experience in between (the behaviour baseline and the whole-cycle hashes are the sample-level check).
/// </summary>
public partial class SkySynthTests
{
    /// <summary>Every kind of place the app can arrive at, with its depth bands, inside a project or not.</summary>
    private static readonly WorldCharacter[] Places =
        [Hub, Windows, Wsl, Repo, Worktree, Band(1), Band(2), Band(3), Band(4), Band(2, repositoryId: 42), Band(3, repositoryId: 42), HazardWorld];

    /// <summary>A synth as the game makes it (AD-79: with the game's minimum dwell).</summary>
    private static SkySynth GameSynth(int seed = 7, PlanetTheme theme = PlanetTheme.Hearth, WorldCharacter? start = null) =>
        new(seed, theme, Rate, start) { MinimumDwell = SkySynth.GameMinimumDwell };

    /// <summary>Renders until <paramref name="done"/> holds or <paramref name="seconds"/> have passed, in 10 ms steps; true if it held.</summary>
    private static bool RenderUntil(SkySynth s, Func<bool> done, double seconds)
    {
        for (var t = 0.0; t < seconds; t += 0.01)
        {
            if (done()) return true;
            s.Render(Frames(0.01));
        }

        return done();
    }

    [Fact]
    public void Without_an_experience_every_place_plays_the_arrangement_it_played_before_and_gets_it_back_after_one()
    {
        foreach (var theme in Enum.GetValues<PlanetTheme>())
        {
            foreach (var c in Places)
            {
                var s = GameSynth(3, theme);
                s.Render(Frames(0.2));
                s.SetWorld(theme, c, 1);
                RenderUntil(s, () => s.PlayingIdentity == SkySynth.IdentityOf(c), 1.0).Should().BeTrue($"{c} plays {SkySynth.IdentityOf(c)}");
                s.Experience.Should().Be(MusicExperience.World);
                s.Key.Should().BeEquivalentTo(SkySynth.KeyFor(theme, c), "the key it had");
                s.SetExperience(MusicExperience.Overlook);
                RenderUntil(s, () => s.PlayingIdentity == MusicIdentity.Overlook, 1.0).Should().BeTrue();
                s.SetExperience(MusicExperience.World);
                RenderUntil(s, () => s.PlayingIdentity == SkySynth.IdentityOf(c), 1.0).Should().BeTrue($"{c} gets {SkySynth.IdentityOf(c)} back");
                s.Key.Should().BeEquivalentTo(SkySynth.KeyFor(theme, c));
            }
        }

        // No place is ever given an experience's piece as its own music.
        foreach (var kind in Enum.GetValues<WorldKind>())
        {
            for (var band = 0; band <= WorldCharacter.MaxBand + 1; band++) SkySynth.IsExperience(SkySynth.IdentityOf(new WorldCharacter(kind, band, band, -1))).Should().BeFalse();
        }

        SkySynth.WorldIdentities.Should().HaveCount(10).And.NotContain([MusicIdentity.Overlook, MusicIdentity.Overview]);
    }

    [Fact]
    public void Activity_picks_the_piece_and_the_place_its_key_and_mode_never_Locrian_or_Phrygian()
    {
        var families = new Dictionary<DiatonicMode, int>();
        foreach (var theme in Enum.GetValues<PlanetTheme>())
        {
            foreach (var c in Places)
            {
                var place = SkySynth.KeyFor(theme, c);
                var s = GameSynth(4, theme, c);
                s.SetWorld(theme, c, 1);
                s.Render(Frames(0.3));
                s.SetExperience(MusicExperience.Overlook);
                RenderUntil(s, () => s.PlayingIdentity == MusicIdentity.Overlook, 1).Should().BeTrue();
                s.Key.Mode.Should().StartWith("lydian");
                s.Key.RootMidiNote.Should().Be(place.RootMidiNote, "the overlook in the world's key");
                s.SetExperience(MusicExperience.Overview);
                RenderUntil(s, () => s.PlayingIdentity == MusicIdentity.Overview, 1).Should().BeTrue();
                var mode = SkySynth.ExperienceModeFor(MusicIdentity.Overview, theme, c);
                mode.Should().Be(place.IntervalsSemitones.SequenceEqual([0, 2, 3, 5, 7, 9, 10]) ? DiatonicMode.Dorian : DiatonicMode.Ionian, $"{theme} {c}: the overview is Dorian only where the place is ({place.Mode})");
                s.Key.Mode.Should().StartWith(mode == DiatonicMode.Dorian ? "dorian" : "ionian");
                s.Key.RootMidiNote.Should().Be(place.RootMidiNote);
                families[mode] = families.GetValueOrDefault(mode) + 1;
            }
        }

        output.WriteLine("the overview's mode across " + families.Values.Sum() + " places: " + string.Join(", ", families.Select(f => $"{f.Key} {f.Value}")));
        families.Keys.Should().BeSubsetOf([DiatonicMode.Ionian, DiatonicMode.Dorian]);
        families[DiatonicMode.Ionian].Should().BeGreaterThan(families[DiatonicMode.Dorian], "weighted to the mode DJ liked best");
        ExperiencePieces.Overlook.Mode.Should().Be(DiatonicMode.Lydian);
    }

    [Fact]
    public void A_change_of_activity_is_heard_on_the_next_beat_even_while_a_world_change_waits()
    {
        var s = GameSynth();
        s.Render(Frames(1));
        s.SetWorld(PlanetTheme.Library, Band(1), 1); // the first change is never held
        RenderUntil(s, () => s.PlayingIdentity == MusicIdentity.Outskirts, 1).Should().BeTrue();
        s.Render(Frames(0.5));
        s.SetWorld(PlanetTheme.Meadow, Band(2), 1);
        s.ChangeHeld.Should().BeTrue("half a second of the outskirts: the change waits for its phrase");
        s.Render(Frames(0.3));
        var asked = s.FramesRendered;
        s.SetExperience(MusicExperience.Overlook);
        RenderUntil(s, () => s.PlayingIdentity == MusicIdentity.Overlook, 1).Should().BeTrue();
        var beat = 60.0 / SkySynth.TempoFor(MusicIdentity.Outskirts);
        ((s.FramesRendered - asked) / (double)Rate).Should().BeLessThanOrEqualTo(beat + 0.02, "the overlook comes in on the next beat");

        // A place named during an experience is remembered, and its music comes back when the experience ends.
        s.SetWorld(PlanetTheme.Theatre, Band(1), 1);
        s.Render(Frames(2));
        s.PlayingIdentity.Should().Be(MusicIdentity.Overlook);
        s.Place.Theme.Should().Be(PlanetTheme.Theatre);
        asked = s.FramesRendered;
        s.SetExperience(MusicExperience.World);
        RenderUntil(s, () => s.PlayingIdentity == MusicIdentity.Outskirts && s.PlayingTheme == PlanetTheme.Theatre, 1).Should().BeTrue();
        ((s.FramesRendered - asked) / (double)Rate).Should().BeLessThanOrEqualTo(60.0 / SkySynth.ThemeTempoFor(MusicIdentity.Overlook) / 2 + 0.02, "back on the overlook's next pulse beat");
    }

    /// <summary>Twelve fresh worlds (no two share a theme and band), as the app names them on quick hops.</summary>
    private static readonly (PlanetTheme Theme, WorldCharacter Character)[] HopWorlds =
    [
        (PlanetTheme.Workshop, Band(1)), (PlanetTheme.Library, Band(2)), (PlanetTheme.Meadow, Band(1)), (PlanetTheme.Theatre, Band(2)),
        (PlanetTheme.Crystal, Band(1)), (PlanetTheme.Wild, Band(2)), (PlanetTheme.Vault, Band(1)), (PlanetTheme.Chime, Band(2)),
        (PlanetTheme.Hearth, Band(1)), (PlanetTheme.Quiet, Band(2)), (PlanetTheme.Veiled, Band(1)), (PlanetTheme.Workshop, Band(2)),
    ];

    /// <summary>
    /// Quick hops (one every <paramref name="hopSeconds"/>) through <see cref="HopWorlds"/> with Arrive after each, as the
    /// app does; returns each change of the music sounding (when, from what and to what, how long the old one had played,
    /// its bar at the end) and the theme notes heard per world.
    /// </summary>
    private static (List<(double At, string From, string To, double Played, int Bar)> Changes, List<int> ThemeNotes, List<(double At, int Root, int PlayingRoot, bool Held)> Arrivals)
        Hops(SkySynth s, double hopSeconds, int hops)
    {
        var notes = new List<long>();
        s.MelodyObserver = n =>
        {
            if (n.Part == MelodyPart.Theme) notes.Add(n.Frame);
        };
        s.Render(Frames(2));
        var changes = new List<(double, string, string, double, int)>();
        var arrivals = new List<(long Frame, double At, int Root, bool Held)>();
        var playing = new List<(long From, PlanetTheme Theme)> { (0, s.PlayingTheme) };
        var last = (s.PlayingIdentity, s.PlayingTheme);
        var lastPlayed = 0.0;
        var lastBar = 0;
        var starts = new List<long> { s.FramesRendered };
        var total = hops * hopSeconds + 8;
        var next = 0.0;
        var hop = 0;
        for (var t = 0.0; t < total; t += 0.01)
        {
            if (hop < hops && t >= next)
            {
                var (theme, c) = HopWorlds[hop % HopWorlds.Length];
                s.SetWorld(theme, c, 1);
                s.Arrive();
                arrivals.Add((s.LastArrival.Frame, t, s.LastArrival.RootMidi, s.ChangeHeld));
                hop++;
                next += hopSeconds;
            }

            lastPlayed = s.PlayingFor.TotalSeconds;
            lastBar = s.PlayingBar;
            s.Render(Frames(0.01));
            var now = (s.PlayingIdentity, s.PlayingTheme);
            if (now != last)
            {
                changes.Add((t, $"{last.Item1} {last.Item2}", $"{now.Item1} {now.Item2}", lastPlayed, lastBar));
                playing.Add((s.FramesRendered - (long)Math.Round(s.PlayingFor.TotalSeconds * Rate), now.Item2));
                starts.Add(s.FramesRendered);
                last = now;
            }
        }

        starts.Add(long.MaxValue);
        var perWorld = new List<int>();
        for (var i = 0; i + 1 < starts.Count; i++) perWorld.Add(notes.Count(f => f >= starts[i] && f < starts[i + 1]));
        // The key playing where each arrival chord sounds (every theme's root is its key's root in the bands and the hub).
        var heard = arrivals.Select(a => (a.At, a.Root, SkySynth.KeyFor(playing.Last(p => p.From <= a.Frame).Theme).RootMidiNote, a.Held)).ToList();
        return (changes, perWorld, heard);
    }

    [Fact]
    public void The_minimum_dwell_lets_the_first_phrase_finish_through_quick_hops()
    {
        // The base behaviour (dwell 0, as before AD-79, and what Core's other tests keep): every hop cuts the music.
        var before = Hops(new SkySynth(5, PlanetTheme.Hearth, Rate), 1.5, 12);
        output.WriteLine($"without the dwell: {before.Changes.Count} changes in {12 * 1.5 + 8:0} s; theme notes per world: {string.Join(" ", before.ThemeNotes)}");
        foreach (var c in before.Changes) output.WriteLine($"  {c.At,6:0.00} s {c.From} → {c.To} after {c.Played:0.00} s (bar {c.Bar})");
        before.Changes.Count.Should().Be(12, "one change per hop");
        before.ThemeNotes.Skip(1).Take(11).Should().OnlyContain(n => n <= 6, "1.5 s of each world: the motif cut off after a few notes");

        var s = GameSynth(5);
        var after = Hops(s, 1.5, 12);
        output.WriteLine($"with the dwell ({SkySynth.GameMinimumDwell.TotalSeconds} s, then the next {SkySynth.DwellPhraseBars}-bar boundary): {after.Changes.Count} changes; theme notes per world: {string.Join(" ", after.ThemeNotes)}");
        foreach (var c in after.Changes) output.WriteLine($"  {c.At,6:0.00} s {c.From} → {c.To} after {c.Played:0.00} s (bar {c.Bar})");
        after.Changes.Count.Should().BeInRange(3, 6, "the music changes once a phrase, not once a hop");
        foreach (var c in after.Changes.Skip(1))
        {
            c.Played.Should().BeGreaterThanOrEqualTo(SkySynth.GameMinimumDwell.TotalSeconds - 0.02, $"{c.From} played its dwell");
            (c.Bar % SkySynth.DwellPhraseBars).Should().Be(SkySynth.DwellPhraseBars - 1, $"{c.From} ended at a phrase boundary (its last bar was {c.Bar})");
        }

        after.ThemeNotes.Skip(1).SkipLast(1).Should().OnlyContain(n => n >= 10, "each world held by the dwell plays its whole two-bar motif (10 notes)");
        after.Changes[^1].To.Should().Be($"{SkySynth.IdentityOf(HopWorlds[11].Character)} {HopWorlds[11].Theme}", "the hops end where the player is: a held change is retargeted, never stacked");

        // While a change is held, the arrival chord sounds in the key still playing (not the waiting world's); at a downbeat, the new one's.
        after.Arrivals.Count(a => a.Held).Should().BeGreaterThanOrEqualTo(6);
        foreach (var (at, root, playing, _) in after.Arrivals.Where(a => a.Held)) root.Should().Be(playing, $"the arrival chord asked at {at:0.00} s sounds in the key playing when it sounds");

        // A change back to the world still playing cancels a held change.
        var u = GameSynth(6);
        u.Render(Frames(1));
        u.SetWorld(PlanetTheme.Library, Band(1), 1);
        RenderUntil(u, () => u.PlayingIdentity == MusicIdentity.Outskirts, 1);
        u.Render(Frames(0.5));
        u.SetWorld(PlanetTheme.Meadow, Band(2), 1);
        u.ChangeHeld.Should().BeTrue();
        u.SetWorld(PlanetTheme.Library, Band(1), 1);
        u.ChangeHeld.Should().BeFalse();
        u.Render(Frames(8));
        (u.PlayingIdentity, u.PlayingTheme).Should().Be((MusicIdentity.Outskirts, PlanetTheme.Library), "the outskirts never stopped");
    }

    [Fact]
    public void Hazard_worlds_and_their_way_out_are_never_held()
    {
        var s = GameSynth();
        s.Render(Frames(1));
        s.SetWorld(PlanetTheme.Library, Band(1), 1);
        RenderUntil(s, () => s.PlayingIdentity == MusicIdentity.Outskirts, 1);
        s.Render(Frames(0.5));
        var asked = s.FramesRendered;
        s.SetWorld(PlanetTheme.Library, HazardWorld, 1);
        s.ChangeHeld.Should().BeFalse();
        RenderUntil(s, () => s.PlayingIdentity == MusicIdentity.Hazard, 1).Should().BeTrue();
        ((s.FramesRendered - asked) / (double)Rate).Should().BeLessThanOrEqualTo(60.0 / SkySynth.TempoFor(MusicIdentity.Outskirts) + 0.02);
        s.Render(Frames(0.5));
        s.SetWorld(PlanetTheme.Library, Band(2), 1); // the fall's way out
        s.ChangeHeld.Should().BeFalse();
        RenderUntil(s, () => s.PlayingIdentity == MusicIdentity.Trails, 1).Should().BeTrue();

        // A hazard world named while another change is held is pulled in to the next beat.
        s.Render(Frames(0.5));
        s.SetWorld(PlanetTheme.Meadow, Band(1), 1);
        s.ChangeHeld.Should().BeTrue();
        asked = s.FramesRendered;
        s.SetWorld(PlanetTheme.Meadow, HazardWorld, 1);
        RenderUntil(s, () => s.PlayingIdentity == MusicIdentity.Hazard, 1).Should().BeTrue();
        ((s.FramesRendered - asked) / (double)Rate).Should().BeLessThanOrEqualTo(60.0 / SkySynth.TempoFor(MusicIdentity.Trails) + 0.02);
    }

    [Fact]
    public void Revisits_pick_a_variation_and_a_return_resumes_the_piece_at_a_section()
    {
        var s = GameSynth();
        s.SetWorld(PlanetTheme.Hearth, Band(1), 1);
        s.Render(Frames(2));
        s.SetExperience(MusicExperience.Overlook, 1);
        RenderUntil(s, () => s.PlayingIdentity == MusicIdentity.Overlook, 1).Should().BeTrue();
        (s.PieceVariation, s.PlayingBar).Should().Be((0, 0), "the first time: the sketch as DJ heard it, from its intro");
        s.Render(Frames(10)); // bar 3 of the theme (a bar is 2.86 s at 84 BPM)
        s.SetExperience(MusicExperience.World);
        s.Render(Frames(5));
        s.SetExperience(MusicExperience.Overlook, 2);
        RenderUntil(s, () => s.PlayingIdentity == MusicIdentity.Overlook, 1).Should().BeTrue();
        s.PlayingBar.Should().Be(10, "a return picks the piece up at its next section (the answer), not from the intro");

        var fresh = GameSynth();
        fresh.SetWorld(PlanetTheme.Hearth, Band(1), 1);
        fresh.Render(Frames(1));
        fresh.SetExperience(MusicExperience.Overlook, 2);
        RenderUntil(fresh, () => fresh.PlayingIdentity == MusicIdentity.Overlook, 1).Should().BeTrue();
        (fresh.PieceVariation, fresh.PlayingBar).Should().Be((1, 0), "a second visit it does not remember: the second light, from the top");
        fresh.Render(Frames(16 * 4 * 60 / ModeSketches.OverlookBpm + 1));
        fresh.PieceVariation.Should().Be(0, "and the variations take turns as it goes round");

        var overview = GameSynth();
        overview.SetWorld(PlanetTheme.Library, Hub, 1);
        overview.Render(Frames(1));
        overview.SetExperience(MusicExperience.Overview, 1);
        RenderUntil(overview, () => overview.PlayingIdentity == MusicIdentity.Overview, 1).Should().BeTrue();
        overview.PieceVariation.Should().Be(0);
    }

    [Fact]
    public void The_experience_pieces_are_on_the_grid_the_overlook_as_DJ_heard_it_and_in_game_they_render_clean()
    {
        // The first overlook is the AD-72 sketch, note for note.
        var heard = ModeSketches.LydianOverlook();
        ExperiencePieces.Overlook.Variations[0].Notes.Should().Equal(heard.Notes);
        ExperiencePieces.Overlook.Variations[1].Notes.Should().NotEqual(heard.Notes);
        SkySynth.ThemeTempoFor(MusicIdentity.Overlook).Should().Be((float)ExperiencePieces.Overlook.BeatsPerMinute);
        SkySynth.ThemeTempoFor(MusicIdentity.Overview).Should().Be((float)ExperiencePieces.StarChart(DiatonicMode.Ionian).BeatsPerMinute);
        foreach (var piece in new[] { ExperiencePieces.Overlook, ExperiencePieces.StarChart(DiatonicMode.Ionian), ExperiencePieces.StarChart(DiatonicMode.Dorian) })
        {
            foreach (var v in piece.Variations) v.Notes.Max(n => n.Start).TotalSeconds.Should().BeLessThan(16 * 4 * 60 / piece.BeatsPerMinute, $"{v.Name} fits one cycle");
            piece.SectionStarts[0].Should().Be(0);
        }

        // In the game's way (a world, then the experience), two cycles each: no clipping, no gaps, the voice pool.
        foreach (var (experience, theme, place) in new[] { (MusicExperience.Overlook, PlanetTheme.Workshop, Band(1)), (MusicExperience.Overview, PlanetTheme.Library, Hub), (MusicExperience.Overview, PlanetTheme.Theatre, Band(3)) })
        {
            var s = GameSynth(9, theme, place);
            s.SetWorld(theme, place, 1);
            s.Render(Frames(0.5));
            s.SetExperience(experience, 1);
            RenderUntil(s, () => s.PlayingIdentity != SkySynth.IdentityOf(place), 1);
            var seconds = 2 * 16 * 4 * 60 / SkySynth.ThemeTempoFor(s.PlayingIdentity);
            var all = new float[Frames(seconds) * 2];
            int most = 0, mostMusic = 0;
            for (var done = 0; done < all.Length; done += 735 * 2)
            {
                s.Render(all.AsSpan(done, Math.Min(735 * 2, all.Length - done)));
                most = Math.Max(most, s.ActiveVoices);
                mostMusic = Math.Max(mostMusic, s.ActiveMusicVoices);
            }

            var scan = Scan(all, seconds);
            var lufs = Lufs(all);
            output.WriteLine($"{experience} in {theme} ({s.Key.Mode}, root {s.Key.RootMidiNote}): {seconds:0.0} s, peak {scan.Peak:0.000}, true peak {TruePeak(all, 2):0.000}, {lufs:0.0} LUFS, voices ≤ {most} (music {mostMusic} of {SkySynth.MaxMusicVoices}), silent {scan.SilentSeconds:0.00} s");
            scan.Peak.Should().BeLessThan(SoftClipFrom);
            scan.Runs.Should().BeEmpty();
            lufs.Should().BeInRange(-29.5, -24.5, "as loud as the worlds' music (−27 ± 2 LUFS)");
            mostMusic.Should().BeLessThanOrEqualTo(SkySynth.MaxMusicVoices);
        }
    }

    [Fact]
    public void The_aurora_voice_stays_finite_bounded_and_cheap()
    {
        // The extremes: every role, low to high, soft to hard, resonance at the voices' settings; the output stays finite and below the clipper.
        var w = new SketchWriter(120, DiatonicMode.Ionian, 62);
        var i = 0;
        foreach (var instrument in new[] { SketchInstrument.AuroraPad, SketchInstrument.AuroraLead, SketchInstrument.AuroraBass })
        {
            for (var midi = 24; midi <= 108; midi += 12)
            {
                foreach (var v in new[] { 0.1f, 0.6f, 1f }) w.Note(SketchPart.Melody, instrument, i++ * 2, 6, midi, v);
            }
        }

        var x = SkySynth.RenderSketch(w.Build("aurora-extremes", 3), Rate);
        x.Should().OnlyContain(f => float.IsFinite(f) && MathF.Abs(f) < 1f);
        output.WriteLine($"aurora extremes: peak {x.Max(MathF.Abs):0.000}, true peak {TruePeak(x, 2):0.000}");
        x.Max(MathF.Abs).Should().BeLessThan(SoftClipFrom);

        // Before and after: the same phrase within 3 LU of each other (a fair comparison), both clean.
        var before = SkySynth.RenderSketch(ModeSketches.AuroraComparison(aurora: false), Rate);
        var after = SkySynth.RenderSketch(ModeSketches.AuroraComparison(aurora: true), Rate);
        var (lb, la) = (Lufs(before), Lufs(after));
        output.WriteLine($"before {lb:0.0} LUFS peak {before.Max(MathF.Abs):0.000}; after {la:0.0} LUFS peak {after.Max(MathF.Abs):0.000}");
        Math.Abs(la - lb).Should().BeLessThan(3);
        after.Max(MathF.Abs).Should().BeLessThan(SoftClipFrom);

        // The cost per voice, against the saw stack it extends: 16 held notes for 4 s each.
        var cost = VoiceCost();
        output.WriteLine($"per voice: aurora pad {cost.Aurora:0} ns/sample ({cost.Aurora * Rate / 1e7:0.00} % of real time), saw-stack strings {cost.Saw:0} ns/sample ({cost.Saw * Rate / 1e7:0.00} %), empty mix {cost.Empty:0} ns/frame");
        (cost.Aurora * Rate / 1e9).Should().BeLessThan(0.02, "one aurora voice costs well under 2 % of one core in real time (Debug build)");
    }

    /// <summary>
    /// The cost of one held voice, nanoseconds per sample: 16 held aurora pads (three saws, the filter) and 16 held string
    /// pads (three saws, the old two-pole filter) rendered for 4 s, less the cost of the same length with no notes, over 16;
    /// the best of three runs (the machine is shared). Also the empty mix's cost per frame.
    /// </summary>
    internal static (double Aurora, double Saw, double Empty) VoiceCost()
    {
        const int voices = 16;
        const double seconds = 4;
        SketchScore Held(SketchInstrument instrument)
        {
            var w = new SketchWriter(60, DiatonicMode.Ionian, 50);
            for (var v = 0; v < voices; v++) w.Note(SketchPart.Chord, instrument, 0, 4 * seconds, 50 + v, 0.8f);
            return w.Build(instrument.ToString(), 0);
        }

        double Best(SketchScore score)
        {
            var best = double.MaxValue;
            for (var run = 0; run < 3; run++)
            {
                var sw = Stopwatch.StartNew();
                SkySynth.RenderSketch(score, Rate);
                best = Math.Min(best, sw.Elapsed.TotalSeconds);
            }

            return best;
        }

        var frames = seconds * Rate;
        var empty = Best(new SketchWriter(60, DiatonicMode.Ionian, 50).Build("empty", seconds));
        var aurora = Best(Held(SketchInstrument.AuroraPad));
        var saw = Best(Held(SketchInstrument.StringPad));
        return ((aurora - empty) * 1e9 / (frames * voices), (saw - empty) * 1e9 / (frames * voices), empty * 1e9 / frames);
    }

    /// <summary>
    /// The true peak of interleaved stereo (BS.1770-style): the largest absolute value of the signal 4× oversampled with a
    /// windowed-sinc interpolator (48 taps), which finds inter-sample peaks a sample peak misses.
    /// </summary>
    private static double TruePeak(float[] x, int channels)
    {
        const int factor = 4, half = 6; // 4 phases, 12 taps each (±6 samples)
        var kernel = new double[factor, 2 * half];
        for (var p = 0; p < factor; p++)
        {
            for (var k = 0; k < 2 * half; k++)
            {
                var t = k - half + 1 - p / (double)factor; // distance from the interpolated point to tap k
                var sinc = Math.Abs(t) < 1e-9 ? 1 : Math.Sin(Math.PI * t) / (Math.PI * t);
                var window = 0.5 + 0.5 * Math.Cos(Math.PI * t / half); // Hann over ±half
                kernel[p, k] = sinc * window;
            }
        }

        var frames = x.Length / channels;
        double peak = 0;
        for (var c = 0; c < channels; c++)
        {
            for (var n = half; n < frames - half; n++)
            {
                for (var p = 0; p < factor; p++)
                {
                    double y = 0;
                    for (var k = 0; k < 2 * half; k++) y += kernel[p, k] * x[(n - half + 1 + k) * channels + c];
                    peak = Math.Max(peak, Math.Abs(y));
                }
            }
        }

        return peak;
    }

    /// <summary>A 16-bit PCM WAV as interleaved floats, its sample rate and channels.</summary>
    private static (float[] Samples, int Rate, int Channels) ReadPcmWav(string path)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        r.ReadBytes(12);
        int rate = 0, channels = 0;
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            var id = new string(r.ReadChars(4));
            var size = r.ReadInt32();
            if (id == "fmt ")
            {
                r.ReadInt16();
                channels = r.ReadInt16();
                rate = r.ReadInt32();
                r.ReadBytes(size - 8);
            }
            else if (id == "data")
            {
                var n = size / 2;
                var s = new float[n];
                for (var i = 0; i < n; i++) s[i] = r.ReadInt16() / 32768f;
                return (s, rate, channels);
            }
            else
            {
                r.ReadBytes(size);
            }
        }

        throw new InvalidDataException(path + ": no data chunk");
    }

    /// <summary>
    /// The scan of a recording (SKYSYNTH_SCAN_WAV, e.g. the in-app capture of the music journey): duration, sample and true
    /// peak, samples at full scale, and every silent run (50 ms windows below −60 dBFS) after the first second.
    /// </summary>
    [SkySynthScanFact]
    public void Scan_recordings_for_clipping_and_gaps()
    {
        foreach (var path in Environment.GetEnvironmentVariable("SKYSYNTH_SCAN_WAV")!.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var (x, rate, ch) = ReadPcmWav(path);
            var frames = x.Length / ch;
            var full = x.Count(v => MathF.Abs(v) >= 0.999f);
            var window = (int)(SilenceWindowSeconds * rate);
            var runs = new List<(double At, double Length)>();
            double? start = null;
            for (var f = rate; f + window <= frames; f += window)
            {
                double sum = 0;
                for (var i = f * ch; i < (f + window) * ch; i++) sum += x[i] * x[i];
                var quiet = Math.Sqrt(sum / (window * ch)) < SilenceRms;
                var t = f / (double)rate;
                if (quiet) start ??= t;
                else if (start is { } s0)
                {
                    runs.Add((s0, t - s0));
                    start = null;
                }
            }

            if (start is { } s1) runs.Add((s1, frames / (double)rate - s1));
            output.WriteLine($"{Path.GetFileName(path)}: {frames / (double)rate:0.0} s at {rate} Hz, peak {x.Max(MathF.Abs):0.000}, true peak {TruePeak(x, ch):0.000}, {full} samples at full scale, " +
                             $"silent runs: {(runs.Count == 0 ? "none" : string.Join(", ", runs.Select(r => $"{r.At:0.00}+{r.Length:0.00} s")))}");
        }
    }
}
