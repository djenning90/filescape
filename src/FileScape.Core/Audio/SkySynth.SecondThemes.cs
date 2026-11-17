namespace FileScape.Core.Audio;

// AD-66 (PLANET-65): two original second themes for the folder bands, heard between the FileScape theme's returns.
//   - the lantern song (the outskirts and the deep woods, the gentler bands): lyrical, falling sighs and a turn, long notes;
//   - the trail call (the trails and the underglow, the dance bands): a syncopated 3 + 3 + 2 call with a lift, answered.
// Each is 4 bars, played over bars 8–11 of a cycle in place of the band motif (the band motif keeps bars 12–15 where it
// played them, and every arrangement's drama is untouched). Written relative to each bar's chord and voice-led from note
// to note, so every key, mode and progression gets its own version of the same shape and no long note clashes with its
// chord. When: every odd cycle of a world's music (a world's music runs on across visits: a return resumes on its next
// half-cycle, so the more often you come back, the sooner it comes round), and the first free section of a revisit that
// starts afresh (the world was forgotten: its music starts on a later cycle); never in the first cycle of a first visit,
// so the arrival as DJ knows it is unchanged, and not straight after a resumed return (it continues where it was: the
// band motif goes on, as AD-49 promised). The hub, the roots, the repository, the hazard world and the abyss never play
// them (their scores are unchanged, sample for sample).
public sealed partial class SkySynth
{
    /// <summary>The second themes' length in bars (bars 8–11 of the cycle, the first half of the world's own material).</summary>
    private const int SecondThemeBars = 4;

    /// <summary>The first bar of the cycle that the second theme takes (the first bar after an unaugmented theme).</summary>
    private const int SecondThemeFirstBar = ThemeLengthBars;

    /// <summary>
    /// A second-theme note: onset and length in sixteenths within its bar, its tone as scale steps above the bar's chord
    /// root (0 root, 2 third, 4 fifth; the others passing or neighbour notes), and how it is placed from the note before:
    /// −1 the nearest below it, +1 the nearest above, 0 the nearest either way.
    /// </summary>
    private readonly record struct SecondNote(int Start, int Length, int Tone, int Direction);

    /// <summary>
    /// The lantern song: "sol fa mi re | sol fa mi | mi fa mi do ti | mi … re" over each bar's chord, so a sigh falling
    /// through the chord twice, a turn and a fall, and an open end on the chord's second step.
    /// </summary>
    private static readonly SecondNote[][] LanternSong =
    [
        [new(0, 6, 4, 0), new(6, 2, 3, -1), new(8, 6, 2, -1), new(14, 2, 1, -1)],
        [new(0, 6, 4, +1), new(6, 2, 3, -1), new(8, 8, 2, -1)],
        [new(0, 4, 2, +1), new(4, 2, 3, +1), new(6, 2, 2, -1), new(8, 6, 0, -1), new(14, 2, 6, -1)],
        [new(0, 12, 2, 0), new(12, 4, 1, -1)],
    ];

    /// <summary>
    /// The trail call: "do sol la sol mi" in a 3 + 3 + 2 lilt, answered "mi sol do' ti"; the call again a chord higher with
    /// a lift, and an answer that climbs back to the fifth (open, into the build or the drop).
    /// </summary>
    private static readonly SecondNote[][] TrailCall =
    [
        [new(0, 3, 0, 0), new(3, 3, 4, +1), new(6, 2, 5, +1), new(8, 2, 4, -1), new(10, 6, 2, -1)],
        [new(0, 3, 2, 0), new(3, 3, 4, +1), new(6, 2, 0, +1), new(8, 8, 6, -1)],
        [new(0, 3, 0, 0), new(3, 3, 4, +1), new(6, 2, 5, +1), new(8, 2, 4, -1), new(10, 2, 2, -1), new(12, 4, 4, +1)],
        [new(0, 2, 5, +1), new(2, 2, 4, -1), new(4, 4, 2, -1), new(8, 8, 4, +1)],
    ];

    /// <summary>The second theme of a folder band: the lantern song for the outskirts and the deep woods, the trail call for the trails and the underglow.</summary>
    private static SecondNote[][] SecondThemeOf(MusicIdentity id) => id is MusicIdentity.Outskirts or MusicIdentity.DeepWoods ? LanternSong : TrailCall;

    /// <summary>
    /// Whether this cycle's bars 8–11 play the second theme (decided on bar 8): only in the folder bands, only after a theme
    /// of its written length (an augmented ending takes bar 8), on every odd cycle and on the first free section of a revisit
    /// that starts afresh (not resumed from memory).
    /// </summary>
    private static bool PlaysSecondTheme(Deck d, long cycle) =>
        IsBand(d.Identity) && d.ThemeBars == ThemeLengthBars && ((d.Development >= 2 && !d.Resumed && !d.SecondThemeHeard) || cycle % 2 == 1);

    /// <summary>
    /// The second theme's part of PlayFree (bars 8–11): decides on bar 8 whether this cycle plays it, then plays a bar of
    /// it on each bar line. True while it plays, so the band motif rests in those bars.
    /// </summary>
    private bool SecondTheme(Deck d, int bar, int inBar, long cycle, long frame, int chord)
    {
        if (bar < SecondThemeFirstBar || bar >= SecondThemeFirstBar + SecondThemeBars) return false;
        if (bar == SecondThemeFirstBar && inBar == 0)
        {
            d.SecondThemeCycle = PlaysSecondTheme(d, cycle) ? cycle : -1;
            if (d.SecondThemeCycle == cycle) d.SecondThemeHeard = true;
            d.SecondDegree = MapDegree(d.Identity is MusicIdentity.Outskirts or MusicIdentity.DeepWoods ? 4 : 2, d.Profile.Scale.Length);
        }

        if (d.SecondThemeCycle != cycle) return false;
        if (inBar == 0) SecondThemeBar(d, bar - SecondThemeFirstBar, frame, chord);
        return true;
    }

    /// <summary>
    /// Plays bar <paramref name="k"/> (0–3) of the deck's second theme over <paramref name="chord"/>: each note placed from the
    /// one before (<see cref="SecondNote.Direction"/>) within a tenth or so above the key's root an octave up, on the band's
    /// instrument (the marimba with a soft sustain under long notes; the festival's pluck; the clarinet, then the first
    /// violins over the cellos; drum and bass's liquid lead).
    /// </summary>
    private void SecondThemeBar(Deck d, int k, long frame, int chord)
    {
        var p = d.Profile;
        var len = p.Scale.Length;
        foreach (var n in SecondThemeOf(d.Identity)[k])
        {
            var degree = PlaceSecondNote(chord + MapDegree(n.Tone, len), d.SecondDegree, n.Direction, len); // a pentatonic key folds the tone like the theme's degrees
            d.SecondDegree = degree;
            var midi = p.Root + 12 + ScaleNote(p.Scale, degree);
            var hz = MidiHz(midi);
            var at = StepAt(d, frame, n.Start);
            var hold = MathF.Max(0.04f, (float)(n.Length * d.FramesPerSixteenth / _rate) - 0.02f);
            var velocity = (n.Start == 0 ? 1f : 0.85f) * SecondThemeVelocity;
            var gain = 0.075f * velocity * d.Style.LeadGain;
            switch (d.Identity)
            {
                case MusicIdentity.DeepWoods when k < 2:
                    PlayLead(d, Lead.Clarinet, at, hz, hold, gain * 0.9f, -0.15f);
                    break;
                case MusicIdentity.DeepWoods:
                    PlayLead(d, Lead.Violins, at, hz, hold, gain * 0.85f, -0.45f);
                    PlayLead(d, Lead.Cello, at, hz * 0.5f, hold, gain * 0.45f, 0.35f);
                    break;
                case MusicIdentity.Underglow:
                    PlayLead(d, Lead.Liquid, at, hz, hold, gain, 0.05f);
                    break;
                default:
                    Enqueue(at, LeadSpec(d.Style.Lead, hz, hold) with { Gain = gain * (p.Pad == PadKind.Normal ? 1f : 0.6f), Pan = 0.1f, Group = d.Group });
                    if (d.Identity == MusicIdentity.Outskirts && n.Length >= 6)
                    {
                        // The marimba cannot hold a note: a soft triangle an octave below sustains the long ones.
                        Enqueue(at, NoteSpec.Tone(Wave.Triangle, hz * 0.5f) with
                        {
                            Attack = 0.04f, Decay = 0.8f, Sustain = 0.4f, Hold = hold, Release = 0.3f, Gain = 0.03f * velocity, ToneHz = 1600, Pan = -0.2f, Send = 0.35f, Group = d.Group,
                        });
                    }

                    break;
            }

            MelodyObserver?.Invoke(new MelodyNote(at, midi, MelodyPart.SecondTheme, d.Variation));
        }
    }

    /// <summary>The second themes' velocity against the band motif's (0.85): a touch softer, so the theme's return stays the headline (not tuned by ear).</summary>
    private const float SecondThemeVelocity = 0.8f;

    /// <summary>
    /// The scale index of the tone class <paramref name="tone"/> (mod the scale) nearest <paramref name="previous"/> in the
    /// given direction, within −3 … len + 5 (a fourth or so below the key's root an octave up, to a sixth above the next
    /// octave) and at most len − 2 steps away (a sixth in a 7-note scale: the line stays singable); otherwise the nearest
    /// either way within that reach (the shape bends rather than leaps); if that is the same note, a scale step the written
    /// way (a pentatonic key folds two tones onto one: the line still moves); and at the edge of the range with nothing in
    /// reach, the note before again. So no step of the line is wider than len − 2 scale steps.
    /// </summary>
    private static int PlaceSecondNote(int tone, int previous, int direction, int len)
    {
        const int Low = -3;
        var high = len + 5;
        var widest = len - 2;
        var best = int.MinValue;
        for (var pass = 0; pass < 2 && best == int.MinValue; pass++)
        {
            for (var octave = -3; octave <= 3; octave++)
            {
                var c = (int)Mod(tone, len) + octave * len;
                if (c < Low || c > high || Math.Abs(c - previous) > widest) continue;
                if (pass == 0 && ((direction < 0 && c >= previous) || (direction > 0 && c <= previous))) continue;
                if (best == int.MinValue || Math.Abs(c - previous) < Math.Abs(best - previous)) best = c;
            }
        }

        if (best == int.MinValue) return previous;
        var step = previous + direction;
        if (best == previous && direction != 0 && step >= Low && step <= high) best = step;
        return best;
    }
}
