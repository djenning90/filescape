namespace FileScape.Core.Audio;

// AD-60's hazard-world score ("Escape", PLANET-48): DJ loved the AD-52 hazard tune's scale and tonality and asked for a
// more frenetic, tense score with a feeling of "get out of here". So: the same phrygian key and chords (i ♭II i ♭VI), the
// same beating drone and dark strings, the same unstable lead and the theme's head turned downwards, but the theme at full
// tempo (152 BPM, the AD-52 score's was 70) over a racing heartbeat, a ♭2 bass ostinato and ticking metal, with phrase-level
// drama in every 16-bar cycle:
//   bars 0–3   the theme over the drive (heartbeat on every beat, sixteenth ticks, the ostinato);
//   bars 4–7   the climb: a snare backbeat, taiko-like toms, a string tremolo, a crash on bar 6;
//   bars 8–11  the chase: the theme's head turned downwards in quick eighths, sequenced a step higher each time;
//   bars 12–13 the break: the floor drops away (no bass, no drums; the heart slows, the ticks race in 32nds, one long
//              wobbling note on the lead, a reversed swell);
//   bars 14–15 the build: a snare roll from eighths to 32nds, toms, the ostinato climbing the scale, a riser, the last
//              eighth silent; then the top of the cycle hits (a crash and a sub impact) as the theme returns.
// The warning and fall cues (SkySynth.cs) still duck this music and stand ≥ 4 dB clear of it (SkySynthTests). The old score
// is kept unchanged as MusicIdentity.Abyss (SkySynth.Arrangements.cs).
public sealed partial class SkySynth
{
    /// <summary>The cycle's bars where the floor drops away (no bass, no drums; a slowing heart, racing ticks).</summary>
    private const int EscapeBreakBar = 12;

    /// <summary>The cycle's build bars start here (to the end of the cycle).</summary>
    private const int EscapeBuildBar = 14;

    /// <summary>The climb (backbeat, toms, tremolo) starts at this bar of the theme.</summary>
    private const int EscapeClimbBar = 4;

    // The ostinato per beat of four sixteenths: 0 the chord's root, 1 the ♭2 above it (phrygian's semitone), as rungs of the scale.
    private static readonly int[] EscapeOstinato = [0, 0, 1, 0];

    // Taiko-like toms on the climb and the chase (X accent, x normal).
    private static readonly float[] EscapeToms = Hits("...x..x...X...x.");

    /// <summary>
    /// One sixteenth of the hazard world's score (see the file's header for the cycle's shape). <paramref name="step"/> is the
    /// deck's step (the cycle's top hits only after the first downbeat, which has its own arrival).
    /// </summary>
    private void EscapeGroove(Deck d, long step, int inBar, int bar, bool intro, long frame, int chord)
    {
        if (intro) return;
        var p = d.Profile;
        var g = d.Group;
        var dev = d.Development;
        var breakBars = bar is EscapeBreakBar or EscapeBreakBar + 1;
        var build = bar >= EscapeBuildBar;
        var gasp = bar == CycleBars - 1 && inBar >= 14; // the last eighth before the top: silence
        if (gasp) return;

        if (bar == 0 && inBar == 0 && step != 0)
        {
            // The top of the cycle after the build: the theme returns on an impact.
            Crash(frame, 1f, g);
            DropImpact(p, frame, 0.7f, g);
        }

        // The heart: racing (lub-dub on every beat), slowing to half in the break.
        if (breakBars ? inBar is 0 or 8 : inBar % 4 == 0)
        {
            Heartbeat(frame, breakBars ? 0.8f : inBar == 0 ? 1f : 0.8f, g);
            Pump(d, frame);
        }
        else if (breakBars ? inBar is 1 or 9 : inBar % 4 == 1)
        {
            Heartbeat(frame, 0.45f, g);
        }

        // The clock: a tick on every sixteenth; from the climb (or a second visit) the 32nds between; in the break, 32nds
        // everywhere, louder bar by bar (time running out).
        var tickVel = breakBars ? 0.5f + 0.4f * ((bar - EscapeBreakBar) * 16 + inBar) / 32f : inBar % 4 == 0 ? 1f : 0.6f;
        HazardTick(p, frame, tickVel, inBar % 2 == 0 ? -0.4f : 0.4f, g);
        if (breakBars || bar >= EscapeClimbBar || dev >= 2) HazardTick(p, frame + (long)(d.FramesPerSixteenth / 2), tickVel * 0.55f, 0.25f, g);

        if (breakBars)
        {
            // A reversed swell over the break into the build.
            if (bar == EscapeBreakBar && inBar == 0)
            {
                var len = (float)(32 * d.FramesPerSixteenth / _rate);
                Enqueue(frame, NoteSpec.Noise(200, 3000, len, q: 1.5f) with { Attack = len, Decay = 0.05f, Gain = 0.035f, Send = 0.5f, Group = g });
            }

            return;
        }

        // The ostinato: sixteenths on the chord's root and its ♭2; in the build it climbs the scale, a step every eighth.
        var rung = build ? ((bar - EscapeBuildBar) * 16 + inBar) / 2 : EscapeOstinato[inBar % 4];
        EscapeBass(d, frame, chord, rung, inBar % 4 == 0 ? 1f : 0.7f);

        var climb = bar >= EscapeClimbBar && bar < EscapeBreakBar;
        if (climb && inBar is 4 or 12) Snare(SnareKind.Snare, frame, 0.75f, 0, g);
        if ((climb || build) && EscapeToms[inBar] > 0) Tom(frame, EscapeToms[inBar], inBar < 8 ? -0.25f : 0.25f, g);
        if (bar is EscapeClimbBar + 2 && inBar == 0) Crash(frame, 0.8f, g);
        if (bar is >= EscapeClimbBar and < 8) Tremolo(d, frame, chord, inBar, 0.5f + 0.5f * ((bar - EscapeClimbBar) * 16 + inBar) / 64f);

        if (build)
        {
            // The roll: eighths through bar 14, sixteenths then 32nds in bar 15, rising.
            var q = ((bar - EscapeBuildBar) * 16 + inBar) / 32f;
            var every = bar == EscapeBuildBar ? 2 : 1;
            if (inBar % every == 0) Snare(SnareKind.Snare, frame, 0.3f + 0.6f * q, inBar % 2 == 0 ? -0.15f : 0.15f, g);
            if (bar == CycleBars - 1 && inBar >= 8) Snare(SnareKind.Snare, frame + (long)(d.FramesPerSixteenth / 2), (0.3f + 0.6f * q) * 0.85f, 0.15f, g);
            if (bar == EscapeBuildBar && inBar == 0)
            {
                var len = (float)(30 * d.FramesPerSixteenth / _rate);
                Enqueue(frame, NoteSpec.Noise(300, 7000, len, q: 1.4f) with { Attack = len, Decay = 0.05f, Gain = 0.04f, Send = 0.4f, Group = g });
            }
        }
    }

    /// <summary>
    /// The ostinato's note: a short, dark two-saw pluck an octave below the key's root on <paramref name="rung"/> of the scale
    /// above the chord's root, its filter snapping shut (harmonics up to ≈ 1 kHz, so it is heard on small speakers), pumped by the heart.
    /// </summary>
    private void EscapeBass(Deck d, long at, int chord, int rung, float vel)
    {
        var p = d.Profile;
        var midi = p.Root - 12 + ScaleNote(p.Scale, chord + rung);
        Enqueue(at, NoteSpec.Saw(MidiHz(midi), 2, 9f, 1100f) with
        {
            ToneToHz = 280f, ToneGlide = 0.07f, Attack = 0.002f, Decay = 0.16f, Gain = 0.06f * vel, Send = 0.05f, Pan = -0.05f, Group = d.PumpGroup,
        });
    }

    /// <summary>A low tom (taiko-like): a sine falling 120 → 70 Hz with a thud of low-passed noise.</summary>
    private void Tom(long at, float vel, float pan, int g)
    {
        Enqueue(at, NoteSpec.Tone(Wave.Sine, 120f) with { GlideToHz = 70f, Glide = 0.12f, Attack = 0.002f, Decay = 0.45f, Gain = 0.14f * vel, Pan = pan, Send = 0.3f, Group = g });
        Enqueue(at, NoteSpec.Noise(700, 300, 0.06f, q: 0.8f, lowpass: true) with { Attack = 0.001f, Decay = 0.08f, Gain = 0.05f * vel, Pan = pan, Send = 0.25f, Group = g });
    }

    /// <summary>The climb's string tremolo: a bright, bitten sixteenth on the chord's tones two octaves up, swelling with <paramref name="vel"/>.</summary>
    private void Tremolo(Deck d, long at, int chord, int inBar, float vel)
    {
        ReadOnlySpan<int> rungs = [0, 2, 1, 2];
        var p = d.Profile;
        var midi = p.Root + 12 + ScaleNote(p.Scale, LadderDegree(p, chord, rungs[inBar % 4]));
        Enqueue(at, NoteSpec.Saw(MidiHz(midi), 2, 7f, 3200f) with
        {
            ToneToHz = 1300f, ToneGlide = 0.06f, Attack = 0.003f, Decay = 0.12f, Gain = 0.026f * vel, Pan = 0.3f, Send = 0.4f, Group = d.Group,
        });
    }

    /// <summary>
    /// The hazard world's pad: the AD-52 drone two octaves down (the root twice, 7 cents either side, and the fifth), held
    /// across four bars (it is on the key's root whatever the chord, so restriking it every bar would only pile up voices),
    /// and the dark strings on each chord, quicker to come and go than the abyss's (the chords change every bar at 152 BPM).
    /// </summary>
    private void EscapePad(Deck d, int chord, float hold, long frame)
    {
        var p = d.Profile;
        var g = d.Group;
        if (frame >= d.DroneUntil - (long)d.FramesPerSixteenth)
        {
            var bars = d.Bar < 0 ? 4 : 4 - d.Bar % 4;
            var droneHold = (float)((bars * 16 - Math.Max(0, d.InBar)) * d.FramesPerSixteenth / _rate) + 0.05f;
            d.DroneUntil = frame + (long)(droneHold * _rate);
            var detune = MathF.Pow(2f, 7f / 1200f);
            ReadOnlySpan<(float Ratio, int Semitones, float Pan)> drone = [(detune, 0, -0.3f), (1f / detune, 0, 0.3f), (1f, 7, 0f)];
            foreach (var (ratio, semis, pan) in drone)
            {
                Enqueue(frame, NoteSpec.Tone(Wave.Sine, MidiHz(p.Root - 24 + semis) * ratio) with
                {
                    Attack = 0.3f, Decay = 2f, Sustain = 0.9f, Hold = droneHold, Release = 0.6f, Gain = 0.03f, Pan = pan, Send = 0.4f, Group = g,
                });
            }
        }

        foreach (var (tone, pan) in (ReadOnlySpan<(int, float)>)[(0, -0.4f), (2, 0.4f)])
        {
            Enqueue(frame, NoteSpec.Saw(MidiHz(p.Root + ScaleNote(p.Scale, chord + tone)), 2, 12f, 800f) with
            {
                Attack = 0.25f, Decay = 2f, Sustain = 0.8f, Hold = hold, Release = 0.5f, Gain = 0.012f, VibratoHz = 0.4f, VibratoDepth = 0.004f, Pan = pan, Send = 0.6f, Group = g,
            });
        }
    }

    /// <summary>
    /// The hazard world's lead note: the AD-52 unstable lead (two bells 18 cents apart, the twin wobbling) with a short
    /// release, as the theme runs at 152 BPM (the abyss's long tails would pile up voices), and a biting saw an octave down
    /// on notes of an eighth or more, for urgency.
    /// </summary>
    private void EscapeLead(Deck d, long at, float hz, float hold, float gain, float fromHz)
    {
        var g = d.Group;
        var spec = LeadSpec(Lead.Unstable, fromHz > 0 ? fromHz : hz, hold) with
        {
            GlideToHz = fromHz > 0 ? hz : 0, Glide = 0.035f, Attack = 0.008f, Release = EscapeLeadReleaseSeconds, Group = g,
        };
        Enqueue(at, spec with { Gain = gain * 0.75f, Pan = -0.15f });
        Enqueue(at, spec with { FreqHz = spec.FreqHz * UnstableTwinRatio, GlideToHz = spec.GlideToHz * UnstableTwinRatio, VibratoHz = 0.93f, Gain = gain * 0.6f, Pan = 0.3f });
        if (hold < 2 * d.FramesPerSixteenth / _rate - 0.03f) return;
        Enqueue(at, NoteSpec.Saw(hz * 0.5f, 2, 10f, 1800f) with
        {
            ToneToHz = 900f, ToneGlide = MathF.Max(0.05f, hold), Attack = 0.004f, Decay = 0.6f, Sustain = 0.5f, Hold = hold, Release = 0.1f, Gain = gain * 0.3f, Pan = -0.15f, Send = 0.3f, Group = g,
        });
    }

    /// <summary>The hazard lead's release, T60 seconds: a sixteenth or two of tail at 152 BPM (the abyss's lead lets go over 0.7 s).</summary>
    private const float EscapeLeadReleaseSeconds = 0.25f;

    /// <summary>A theme note in the hazard world (<see cref="EscapeLead"/>).</summary>
    private void EscapeThemeNote(Deck d, long at, float hz, float fromHz, float hold, float gain) => EscapeLead(d, at, hz, hold, gain, fromHz);

    /// <summary>A note of the chase (the theme's head turned downwards) on the hazard lead, reported like any motif note.</summary>
    private void EscapeMotifNote(Deck d, long at, int degree, int lengthSixteenths, float velocity)
    {
        var p = d.Profile;
        var midi = p.Root + 12 + ScaleNote(p.Scale, MapDegree(degree, p.Scale.Length));
        var hold = MathF.Max(0.04f, (float)(lengthSixteenths * d.FramesPerSixteenth / _rate) - 0.02f);
        EscapeLead(d, at, MidiHz(midi), hold, 0.075f * velocity * d.Style.LeadGain, 0f);
        MelodyObserver?.Invoke(new MelodyNote(at, midi, MelodyPart.Motif, d.Variation));
    }

    /// <summary>
    /// Between the hazard world's returns: the chase (bars 8–11), the theme's head turned downwards (do' sol mi re/do) in
    /// quick eighths, twice per two bars, the second a step higher; in the break (bar 12) one long, wobbling note on the lead.
    /// </summary>
    private void EscapeFree(Deck d, int bar, bool first, long frame)
    {
        if (!first) return;
        if (bar < EscapeBreakBar)
        {
            var lift = (bar - 8) / 2; // the second phrase a step higher
            foreach (var start in (ReadOnlySpan<int>)[0, 16])
            {
                var up = lift + (start == 16 ? 1 : 0);
                var last = start == 16 ? 0 : 1;
                foreach (var (s, length, degree) in (ReadOnlySpan<(int, int, int)>)[(0, 2, 7), (2, 2, 4), (4, 2, 2), (6, 6, last)])
                {
                    EscapeMotifNote(d, StepAt(d, frame, start + s), degree + up, length, 0.8f);
                }
            }

            return;
        }

        if (bar == EscapeBreakBar) EscapeMotifNote(d, frame, 7, 30, 0.7f);
    }

    /// <summary>The hazard world's first downbeat: the AD-52 deep gong and falling boom, now with a crash: you have arrived somewhere you should not stay.</summary>
    private void EscapeArrival(Deck d, long frame)
    {
        AbyssArrival(d, frame);
        Crash(frame, 0.9f, d.Group);
    }
}
