using FileScape.Core.Planets;

namespace FileScape.Core.Audio;

// The AD-52 arrangements of SkySynth, the same FileScape theme in four new settings, each with its own groove, pads, lead
// instruments and material between the theme's returns:
//   - the festival (folder band 2, the trails): a pumping big-room drop, a breakdown and a build (snare roll, riser, filter
//     opening) per 16-bar cycle; a flight out of it builds (FlightBuild) and the arrival releases the drop (ArrivalDrop);
//   - the orchestra (band 3, the deep woods): strings, horns, woodwinds, timpani and harp, the theme passed between them,
//     dynamics rising and falling twice per cycle (OrchestraDynamics), half time over a string ostinato;
//   - drum and bass (band 4, the underglow): 172 BPM breaks under a half-time theme, a reese bass, airy strings;
//   - the abyss (until AD-60 the hazard world's score, kept for a future world type): phrygian at half time, a beating
//     drone, a heartbeat, ticking metal, an unstable lead. AD-60's hazard score is in SkySynth.Escape.cs.
// They reuse the score machinery of SkySynth.Music.cs (cycles, variations, development, the band motif); OnStep, PlayPad,
// PlayFree, PlayArrival and PlayThemeNote dispatch to them by Style.Arrangement. Everything here runs inside Render, except
// ArrivalDrop (from Arrive).
public sealed partial class SkySynth
{
    /// <summary>Which score a style plays: the AD-49 table-driven groove (<see cref="Classic"/>) or one of AD-52's arrangements.</summary>
    private enum Arrangement
    {
        Classic,
        Festival,
        Orchestral,
        DrumAndBass,

        /// <summary>The AD-52 hazard score, kept as <see cref="MusicIdentity.Abyss"/> (AD-60).</summary>
        Abyss,

        /// <summary>AD-60's hazard score: frenetic, "get out of here" (SkySynth.Escape.cs).</summary>
        Escape,

        /// <summary>AD-79: a written piece for an experience (<see cref="ExperiencePiece"/>), played note for note (SkySynth.Experience.cs).</summary>
        Score,
    }

    // ---- the sidechain pump

    /// <summary>The pump's dip, seconds from the kick to its deepest point: a ramp, so the pumped voices never click.</summary>
    private const float PumpAttackSeconds = 0.004f;

    /// <summary>The pump's recovery time constant, seconds: longer pumps deeper into the offbeat (0.13 s: back to ≈ 90 % by the offbeat eighth at 126 BPM).</summary>
    private const float PumpRecoverySeconds = 0.13f;

    // ---- the festival's build and drop

    /// <summary>The festival cycle's build starts at this bar (bars 12–15; the breakdown runs from the theme's end to it).</summary>
    private const int FestivalBuildBar = 12;

    /// <summary>The festival drop's climax starts at this bar (its last phrase): the line doubled an octave up (AD-60).</summary>
    private const int FestivalClimaxBar = 6;

    /// <summary>The stop before the breakdown: from this sixteenth of the drop's last bar only the lead and the fill play (AD-60).</summary>
    private const int FestivalStopSixteenth = 12;

    /// <summary>A flight's progress (0..1) the festival's build must reach for the arrival to release a drop: a short hop is not a build.</summary>
    private const float FlightBuildForDrop = 0.5f;

    /// <summary>The drop comes on the first beat at least this long after the arrival, seconds: the landing and arrival chord are heard first, then the drop.</summary>
    private const float DropMinDelaySeconds = 0.15f;

    /// <summary>The hazard lead's twin, a ratio above it (18 cents): close enough to beat slowly, far enough to sound uneasy.</summary>
    private static readonly float UnstableTwinRatio = MathF.Pow(2f, 18f / 1200f);

    /// <summary>A flight is under way (<see cref="Fly"/> called within its timeout, short of arrival): the festival builds.</summary>
    private bool FlightBuilding => _flyProgress < 1f && _frame - _lastFlyFrame <= (long)(FlyTimeoutSeconds * _rate);

    /// <summary>The gain of a deck's pumped group at <paramref name="frame"/>: 1 − depth at the kick (after a 4 ms ramp), recovering exponentially.</summary>
    private float PumpGain(Deck d, long frame)
    {
        if (d.PumpDepth <= 0) return 1f;
        var k = frame >= d.PumpLast ? d.PumpLast : d.PumpPrev;
        if (k <= long.MinValue / 4 || frame < k) return 1f;
        var t = (frame - k) * _invRate;
        var dip = t < PumpAttackSeconds ? t / PumpAttackSeconds : MathF.Exp(-(t - PumpAttackSeconds) / PumpRecoverySeconds);
        return 1f - d.PumpDepth * dip;
    }

    /// <summary>Registers a kick at <paramref name="at"/> as the deck's sidechain trigger.</summary>
    private static void Pump(Deck d, long at)
    {
        if (at <= d.PumpLast) return;
        d.PumpPrev = d.PumpLast;
        d.PumpLast = at;
    }

    /// <summary>
    /// Called by <see cref="Arrive"/>: if a festival build ran during the flight (it reached <see cref="FlightBuildForDrop"/>),
    /// the arrival releases it. A new world starts on a beat at least <see cref="DropMinDelaySeconds"/> away (its first
    /// downbeat moved a beat later if need be) with the drop's impact, and a festival world from the top of its theme;
    /// the same festival world again jumps to the top of its theme on such a beat. The hub's welcome stays gentle. Returns
    /// the drop's frame, or −1 when there is none. Clears the build either way.
    /// </summary>
    private long ArrivalDrop()
    {
        var built = MathF.Max(_decks[0].BuildPeak, _decks[1].BuildPeak);
        foreach (var deck in _decks)
        {
            deck.BuildPeak = 0;
            ReleaseSteered(ref deck.Riser, deck.RiserGeneration);
        }

        var target = _decks[_current];
        if (built < FlightBuildForDrop || !target.Live || target.Identity == MusicIdentity.Hub) return -1;
        var earliest = _frame + Frames(DropMinDelaySeconds);
        if (target.NextStep == 0)
        {
            // A new world, before its first downbeat: make that downbeat the drop.
            var old = _decks[1 - _current];
            if (target.Origin < earliest && old.Live && old.StopFrame == target.Origin)
            {
                var k = old.NextStep;
                while (Mod(k, old.StepsPerBeat) != 0 || StepFrame(old, k) < earliest) k++;
                var origin = StepFrame(old, k);
                old.StopFrame = origin;
                target.Origin = origin;
                _lastSwitchFrame = origin;
            }

            if (target.Style.Arrangement == Arrangement.Festival) target.StepOffset = (target.StepOffset + CycleSixteenths - 1) / CycleSixteenths * CycleSixteenths;
            target.DropArrival = true;
            return target.Origin;
        }

        var s = target.NextStep;
        while (Mod(s, 4) != 0 || StepFrame(target, s) < earliest) s++;
        if (target.Style.Arrangement == Arrangement.Festival)
        {
            // The same festival world: back to the top of its theme on that beat (AlignDrop), the fill cancelled.
            target.DropAtStep = s;
            target.FillFrom = long.MinValue;
        }
        else
        {
            DropImpact(target.Profile, StepFrame(target, s), 0.7f);
        }

        return StepFrame(target, s);
    }

    /// <summary>
    /// A drop on <paramref name="step"/> (a beat): the music jumps forward to the top of its next cycle (the theme), so the
    /// bar starts again on this beat; the shift is a whole number of beats, so the pulse's grid is unchanged.
    /// </summary>
    private static void AlignDrop(Deck d, long step)
    {
        d.IntroSteps = Math.Min(d.IntroSteps, step);
        var abs = step - d.IntroSteps + d.StepOffset;
        var top = abs <= 0 ? 0 : (abs + CycleSixteenths - 1) / CycleSixteenths * CycleSixteenths;
        d.StepOffset += top - abs;
        d.CycleReady = false;
        d.NeedChord = true;
        d.DropAtStep = -1;
        d.FillFrom = long.MinValue;
    }

    /// <summary>A drop's impact in a key: a sub boom falling an octave (to ≈ 30–60 Hz) and a long downlifter of noise.</summary>
    private void DropImpact(Profile p, long at, float vel, int group = GroupSfx)
    {
        Enqueue(at, NoteSpec.Tone(Wave.Sine, PitchHz(p, 0, -1)) with { GlideToHz = PitchHz(p, 0, -2), Glide = 0.25f, Attack = 0.002f, Decay = 1.4f, Gain = 0.15f * vel, Send = 0.1f, Group = group });
        Enqueue(at, NoteSpec.Noise(9000, 400, 1.2f, q: 0.7f) with { Attack = 0.002f, Decay = 1.5f, Gain = 0.05f * vel, Pan = -0.2f, Send = 0.5f, Group = group });
    }

    /// <summary>
    /// One note of an AD-52 lead instrument: <see cref="LeadSpec"/>'s voice, stacked (the supersaw: two stacks spread wide
    /// and a bell an octave up), echoed (the liquid lead, a dotted eighth later), twinned (the unstable lead: an 18-cent twin;
    /// with <paramref name="sag"/>, its long notes sag 30 cents) or with a breath (the flute). <paramref name="fromHz"/> &gt; 0
    /// glides in from the previous note (legato, 35 ms).
    /// </summary>
    private void PlayLead(Deck d, Lead lead, long at, float hz, float hold, float gain, float pan, float fromHz = 0f, bool sag = false)
    {
        var g = d.Group;
        var spec = LeadSpec(lead, fromHz > 0 ? fromHz : hz, hold) with { GlideToHz = fromHz > 0 ? hz : 0, Glide = 0.035f, Gain = gain, Pan = pan, Group = g };
        switch (lead)
        {
            case Lead.SuperSaw:
                Enqueue(at, spec with { Gain = gain * 0.6f, Pan = pan - 0.45f });
                Enqueue(at, spec with { FreqHz = spec.FreqHz * 1.003f, GlideToHz = spec.GlideToHz * 1.003f, DetuneCents = 9f, Gain = gain * 0.6f, Pan = pan + 0.45f });
                Enqueue(at, NoteSpec.Bell(hz * 2f, ratio: 2f, index: 1f, indexDecay: 0.12f) with { Attack = 0.002f, Decay = 0.25f, Gain = gain * 0.3f, Pan = pan, Send = 0.3f, Group = g });
                break;
            case Lead.Liquid:
                Enqueue(at, spec);
                Enqueue(at + (long)(3 * d.FramesPerSixteenth), spec with { FreqHz = hz, GlideToHz = 0, Gain = gain * 0.3f, Pan = -pan - 0.4f, Send = 0.65f });
                break;
            case Lead.Unstable:
            {
                var s = sag ? spec with { FreqHz = hz, GlideToHz = hz * 0.983f, Glide = hold } : spec;
                Enqueue(at, s with { Gain = gain * 0.75f, Pan = pan - 0.2f });
                Enqueue(at, s with { FreqHz = s.FreqHz * UnstableTwinRatio, GlideToHz = s.GlideToHz * UnstableTwinRatio, VibratoHz = 0.93f, Gain = gain * 0.6f, Pan = pan + 0.25f });
                break;
            }

            case Lead.Flute:
                Enqueue(at, spec);
                Enqueue(at, NoteSpec.Noise(hz * 2f, hz * 1.5f, 0.05f, q: 2f) with { Attack = 0.01f, Decay = 0.08f, Gain = gain * 0.25f, Pan = pan, Send = 0.4f, Group = g });
                break;
            default:
                Enqueue(at, spec);
                break;
        }
    }

    /// <summary>A theme note in an AD-52 arrangement (from <see cref="PlayThemeNote"/>, which reports it to the observer).</summary>
    private void ArrangedThemeNote(Deck d, in LeadNote note, long at, int midi, float fromHz, float hold, float gain)
    {
        var hz = MidiHz(midi);
        var p = d.Profile;
        switch (d.Style.Arrangement)
        {
            case Arrangement.Festival:
            {
                PlayLead(d, Lead.SuperSaw, at, hz, hold, gain, 0.05f, fromHz);
                // The drop's harmony: the line a third below in the scale, a narrower stack in the middle.
                var harmony = p.Root + 12 + ScaleNote(p.Scale, note.Degree - 2);
                Enqueue(at, LeadSpec(Lead.SuperSaw, MidiHz(harmony), hold) with { Unison = 2, Gain = gain * 0.35f, Pan = 0f, Group = d.Group });
                // The climax (AD-60): the drop's last phrase (and from bar 4 on a revisit) lifted by the line an octave up.
                var phraseBar = note.Start / 16;
                if (phraseBar >= FestivalClimaxBar || (d.Development >= 2 && phraseBar >= FestivalClimaxBar - 2))
                {
                    Enqueue(at, LeadSpec(Lead.SuperSaw, hz * 2f, hold) with { Unison = 2, GlideToHz = fromHz > 0 ? hz * 2f : 0, FreqHz = fromHz > 0 ? fromHz * 2f : hz * 2f, Glide = 0.035f, Gain = gain * 0.4f, Pan = -0.1f, Group = d.Group });
                }

                break;
            }

            case Arrangement.Orchestral:
                OrchestraThemeNote(d, note, at, midi, fromHz, hold, gain);
                break;
            case Arrangement.DrumAndBass:
                PlayLead(d, Lead.Liquid, at, hz, hold, gain, 0.05f, fromHz);
                if (d.Development >= 2 && note.Length >= 6)
                {
                    Enqueue(at, LeadSpec(Lead.Violins, hz * 0.5f, hold) with { Attack = 0.25f, Gain = gain * 0.3f, Pan = -0.3f, Send = 0.7f, Group = d.Group });
                }

                break;
            case Arrangement.Escape:
                EscapeThemeNote(d, at, hz, fromHz, hold, gain);
                break;
            default:
                PlayLead(d, Lead.Unstable, at, hz, hold, gain, 0.05f, fromHz, sag: note.Length >= 6);
                break;
        }
    }

    // ---------------------------------------------------------------- the festival (band 2, the trails)

    /// <summary>
    /// One sixteenth of the festival: in the theme's bars the drop (a big four-on-the-floor kick that pumps the pads and
    /// bass, claps, offbeat open hats, an offbeat saw bass, a crash and an impact at the top of each cycle); then the
    /// breakdown (no kick, a shaker, a sub under the band motif); from bar 12 the build (a snare roll from quarters to
    /// sixteenths, a riser, a pitch riser and rising stabs, the kick out for the last two bars and silence on the last beat).
    /// While a flight is under way it plays the flight's build instead (<see cref="FlightBuild"/>).
    /// </summary>
    private void FestivalGroove(Deck d, long step, int inBar, int bar, bool intro, long frame, int chord)
    {
        var g = d.Group;
        var dev = d.Development;
        if (FlightBuilding && d.StopFrame == long.MaxValue)
        {
            FlightBuild(d, inBar, frame, chord);
            return;
        }

        ReleaseSteered(ref d.Riser, d.RiserGeneration);
        if (intro) return;
        if (bar < d.ThemeBars)
        {
            if (bar == 0 && inBar == 0 && step != 0)
            {
                // The drop after the cycle's build (the first downbeat has its own arrival).
                Crash(frame, 1f, g);
                DropImpact(d.Profile, frame, 0.8f, g);
            }

            if (bar == d.ThemeBars - 1 && inBar >= FestivalStopSixteenth)
            {
                // The stop (AD-60): the beat drops out under the lead's last note; only the snare fill leads into the breakdown.
                Snare(SnareKind.Snare, frame, 0.35f + 0.12f * (inBar - 12), inBar % 2 == 0 ? -0.15f : 0.15f, g);
                return;
            }

            if (bar == FestivalClimaxBar && inBar == 0) Crash(frame, 0.8f, g); // into the climax
            if (inBar % 4 == 0)
            {
                BigKick(frame, 1f, g);
                Pump(d, frame);
            }

            if (inBar is 4 or 12)
            {
                Snare(SnareKind.Clap, frame, 0.9f, 0, g);
                Snare(SnareKind.Snare, frame, 0.45f, 0, g);
            }

            if (dev >= 3 && inBar % 2 == 0) Enqueue(frame, NoteSpec.Noise(9500, 9000, 0.05f, q: 0.9f) with { Attack = 0.001f, Decay = 0.12f, Gain = 0.03f, Pan = 0.35f, Send = 0.15f, Group = g }); // a third visit: a ride on the eighths
            if (inBar % 4 == 2) OpenHat(frame, 0.75f, g);
            else if (inBar % 2 == 0 || dev >= 2) ClosedHat(frame, inBar % 2 == 0 ? 0.55f : 0.3f, inBar % 4 < 2 ? -0.25f : 0.25f, g);
            if (inBar % 4 == 2) SawBass(d, frame, chord, up: inBar % 8 == 6, vel: 1f);
            else if (dev >= 2 && inBar % 4 == 3) SawBass(d, frame, chord, up: false, vel: 0.5f);
            return;
        }

        if (bar < FestivalBuildBar)
        {
            // The breakdown: air, a sub under each chord, the band motif (PlayFree).
            if (inBar % 2 == 0) Enqueue(frame, NoteSpec.Noise(6500, 6000, 0.03f, q: 1.2f) with { Attack = 0.006f, Decay = 0.06f, Gain = 0.025f, Pan = inBar % 4 < 2 ? -0.3f : 0.3f, Send = 0.1f, Group = g });
            if (inBar == 0)
            {
                Enqueue(frame, NoteSpec.Tone(Wave.Sine, MidiHz(d.Profile.Root - 12 + BassSemitones(d.Profile, chord))) with
                {
                    Attack = 0.05f, Decay = 2f, Sustain = 0.8f, Hold = (float)(15 * d.FramesPerSixteenth / _rate), Release = 0.3f, Gain = 0.035f, Send = 0.1f, Group = g,
                });
            }

            return;
        }

        // The build: 4 bars, q 0 → 1.
        var q = ((bar - FestivalBuildBar) * 16 + inBar) / 64f;
        if (bar == FestivalBuildBar && inBar == 0) CycleRisers(d, frame);
        var every = bar switch { FestivalBuildBar => 4, FestivalBuildBar + 1 => 2, _ => 1 };
        var breath = bar == CycleBars - 1 && inBar >= 12; // the last beat before the drop: silence but the risers
        if (!breath && inBar % every == 0) Snare(SnareKind.Snare, frame, 0.25f + 0.65f * q, inBar % 2 == 0 ? -0.15f : 0.15f, g);
        if (bar <= FestivalBuildBar + 1 && inBar % 4 == 0)
        {
            Kick(frame, 0.7f, g);
            Pump(d, frame);
        }

        if (bar >= FestivalBuildBar + 2 && !breath && inBar % 2 == 0) RisingStab(d, frame, chord, (bar - FestivalBuildBar - 2) * 8 + inBar / 2, 0.5f + 0.5f * q);
    }

    /// <summary>
    /// The flight's build (the festival world playing while you fly out of it): a snare roll from eighths to sixteenths to
    /// thirty-seconds as the flight goes on, stabs climbing with it and a steered noise riser opening from 400 Hz to 9 kHz;
    /// no kick. The arrival releases it (<see cref="ArrivalDrop"/>).
    /// </summary>
    private void FlightBuild(Deck d, int inBar, long frame, int chord)
    {
        var g = d.Group;
        var pr = _flyProgress;
        d.BuildPeak = MathF.Max(d.BuildPeak, pr);
        var vel = 0.3f + 0.6f * pr;
        if (pr < 0.35f)
        {
            if (inBar % 2 == 0) Snare(SnareKind.Snare, frame, vel, -0.1f, g);
        }
        else
        {
            Snare(SnareKind.Snare, frame, vel, inBar % 2 == 0 ? -0.15f : 0.15f, g);
            if (pr >= 0.7f) Snare(SnareKind.Snare, frame + (long)(d.FramesPerSixteenth / 2), vel * 0.85f, 0.15f, g);
        }

        if (inBar % 2 == 0) RisingStab(d, frame, chord, (int)(pr * 12), 0.4f + 0.6f * pr);
        var cutoff = 400f + 8600f * pr * pr;
        var level = 0.02f + 0.05f * pr;
        var v = Steered(d.Riser, d.RiserGeneration);
        if (v == null)
        {
            v = StartVoice(NoteSpec.Noise(cutoff, cutoff, 0, q: 1.3f) with { Attack = 0.2f, Sustain = 1f, Decay = 0.1f, Hold = 3600f, Release = 0.06f, Gain = level, Send = 0.4f, Group = g }, 0);
            if (v == null) return;
            v.LevelK = 1f - Coef(0.15f);
            d.Riser = v;
            d.RiserGeneration = v.Generation;
        }

        v.LevelTarget = level;
        v.SetCutoffTarget(cutoff, 1f - Coef(0.15f));
    }

    /// <summary>The cycle's build risers, 4 bars long and peaking on the drop: noise opening 300 Hz → 9 kHz and a saw stack gliding up an octave as its filter opens.</summary>
    private void CycleRisers(Deck d, long frame)
    {
        var p = d.Profile;
        var len = (float)(4 * 16 * d.FramesPerSixteenth / _rate);
        Enqueue(frame, NoteSpec.Noise(300, 9000, len, q: 1.4f) with { Attack = len, Decay = 0.05f, Gain = 0.05f, Send = 0.4f, Group = d.Group });
        Enqueue(frame, NoteSpec.Saw(MidiHz(p.Root + 12), 3, 20f, 900f) with
        {
            GlideToHz = MidiHz(p.Root + 24), Glide = len, ToneToHz = 6000f, ToneGlide = len, Attack = len, Decay = 0.05f, Gain = 0.025f, Send = 0.45f, Group = d.Group,
        });
    }

    /// <summary>A build's stab: a short saw stack <paramref name="rung"/> scale steps above the chord (an octave up), brighter as it climbs; pumped.</summary>
    private void RisingStab(Deck d, long at, int chord, int rung, float vel)
    {
        var p = d.Profile;
        var midi = p.Root + 12 + ScaleNote(p.Scale, chord + rung);
        Enqueue(at, NoteSpec.Saw(MidiHz(midi), 3, 12f, 2500f + 200f * rung) with { Attack = 0.003f, Decay = 0.16f, Gain = 0.03f * vel, Pan = rung % 2 == 0 ? -0.3f : 0.3f, Send = 0.35f, Group = d.PumpGroup });
    }

    /// <summary>The drop's kick: the kit's kick with a longer, lower sine tail (58 → 46 Hz) under it.</summary>
    private void BigKick(long at, float vel, int g)
    {
        Kick(at, vel, g);
        Enqueue(at, NoteSpec.Tone(Wave.Sine, 58f) with { GlideToHz = 46f, Glide = 0.3f, Attack = 0.002f, Decay = 0.45f, Gain = 0.08f * vel, Send = 0f, Group = g });
    }

    /// <summary>The drop's offbeat bass: a short, filtered pair of saws on the chord's root (an octave up when <paramref name="up"/>); pumped.</summary>
    private void SawBass(Deck d, long at, int chord, bool up, float vel)
    {
        var p = d.Profile;
        var midi = p.Root - 12 + BassSemitones(p, chord) + (up ? 12 : 0);
        Enqueue(at, NoteSpec.Saw(MidiHz(midi), 2, 10f, 700f) with { Attack = 0.003f, Decay = 0.22f, Gain = 0.07f * vel, Send = 0.05f, Group = d.PumpGroup });
    }

    /// <summary>
    /// The festival's pad: a supersaw chord in the pumped group, bright in the drop (3 kHz), filtered in the breakdown (700 →
    /// 1300 Hz over the chord) and opening through the build (600 Hz → 5 kHz across its 4 bars, the classic filter sweep).
    /// </summary>
    private void FestivalPad(Deck d, int chord, float hold, long frame)
    {
        var p = d.Profile;
        var bar = d.Bar;
        float from, to, gain, attack;
        if (bar >= 0 && bar < d.ThemeBars)
        {
            (from, to, gain, attack) = (3000f, 3000f, 0.02f, 0.01f);
        }
        else if (bar >= FestivalBuildBar)
        {
            var q0 = ((bar - FestivalBuildBar) * 16 + d.InBar) / 64f;
            var q1 = MathF.Min(1f, q0 + (float)(hold * _rate / d.FramesPerSixteenth) / 64f);
            (from, to, gain, attack) = (600f + 4400f * q0 * q0, 600f + 4400f * q1 * q1, 0.014f + 0.016f * q0, 0.05f);
        }
        else
        {
            (from, to, gain, attack) = (700f, 1300f, 0.022f, 0.3f);
        }

        ReadOnlySpan<int> tones = [0, 2, 4];
        for (var i = 0; i < tones.Length; i++)
        {
            var midi = p.Root + 12 + ScaleNote(p.Scale, chord + tones[i]);
            Enqueue(frame, NoteSpec.Saw(MidiHz(midi), 3, 16f, from) with
            {
                ToneToHz = to, ToneGlide = hold, Attack = attack, Decay = 1.5f, Sustain = 0.8f, Hold = hold, Release = 0.35f, Gain = gain, Pan = -0.5f + 0.5f * i, Send = 0.5f, Group = d.PumpGroup,
            });
        }
    }

    /// <summary>Between the festival's returns: the band motif in the breakdown (the trails' displaced shape, on a pluck); the build has none.</summary>
    private void FestivalFree(Deck d, int bar, bool first, int phrase, long cycle, long frame)
    {
        if (bar < FestivalBuildBar && first) BandMotif(d, frame, phrase, cycle);
    }

    /// <summary>The festival's first downbeat: it starts on the drop (a crash and the impact).</summary>
    private void FestivalArrival(Deck d, long frame)
    {
        Crash(frame, 1f, d.Group);
        DropImpact(d.Profile, frame, 0.8f, d.Group);
    }

    // ---------------------------------------------------------------- the orchestra (band 3, the deep woods)

    /// <summary>
    /// The orchestra's pulse, BPM (the theme at half: 78). AD-66 (PLANET-64): lifted from the written 144 by +8.3 % at DJ's
    /// request ("a modest tempo lift while keeping what made it feel that way"); the score, its phrasing, the harmonic
    /// rhythm (a chord per bar) and the drama are unchanged, so everything measured in sixteenths simply runs 8 % sooner.
    /// </summary>
    private const float OrchestraBpm = 156f;

    /// <summary>The tempo the orchestra was written and balanced at (AD-52), BPM: the reference for <see cref="OrchestraTails"/>.</summary>
    private const float OrchestraWrittenBpm = 144f;

    /// <summary>
    /// The factor on the orchestra's long tails (the sections' and basses' releases over the chord change, the horns', the
    /// harp's ring and the bar-5 cymbal's), written in seconds at 144 BPM: 144 / the tempo (0.92 at 156), so a tail lasts
    /// as many beats as it did and the chords overlap as before. Attacks and the percussion's own decays stay in seconds
    /// (they are the instruments' physics, not the phrasing); the reverb is shared by every world and unchanged.
    /// </summary>
    private static float OrchestraTails(Deck d) => OrchestraWrittenBpm / d.Style.Bpm;

    /// <summary>
    /// The orchestra's dynamics per bar, 0..1 (pp ≈ 0.4 … ff 1): through the theme a crescendo from the woodwind's solo to
    /// the tutti cadence; between returns soft (the cellos' motif), then a second wave (horn call, answer, a tutti climax on
    /// bar 14) and a diminuendo into the next statement.
    /// </summary>
    private static float OrchestraDynamics(Deck d, int bar)
    {
        if (bar < 0) return 0.6f;
        if (bar < d.ThemeBars) return bar < 2 ? 0.4f : bar < 4 ? 0.6f : bar < 6 ? 0.8f : 1f;
        return bar switch { < 12 => 0.45f, 12 => 0.65f, 13 => 0.85f, 14 => 1f, _ => 0.5f };
    }

    /// <summary>
    /// The theme in the orchestra, by its 2-bar phrase: the motif on a woodwind (flute, an octave up; clarinet for a
    /// syncopated or inverted return; oboe for the octave and augmented ones), the answer on the first violins, the raised
    /// motif on the horns with the violins above, the cadence tutti (violins, horns an octave down, the flute an octave up).
    /// </summary>
    private void OrchestraThemeNote(Deck d, in LeadNote note, long at, int midi, float fromHz, float hold, float gain)
    {
        var hz = MidiHz(midi);
        var phrase = note.Start / 16 / 2;
        var fluteUp = midi + 12 <= 96; // the flute an octave up while it stays in its range
        switch (phrase)
        {
            case 0:
            {
                var ww = d.Variation switch
                {
                    ThemeVariation.Syncopated or ThemeVariation.InvertedAnswer => Lead.Clarinet,
                    ThemeVariation.OctaveCounter or ThemeVariation.AugmentedEnding => Lead.Oboe,
                    _ => Lead.Flute,
                };
                var up = ww == Lead.Flute && fluteUp ? 2f : 1f;
                PlayLead(d, ww, at, hz * up, hold, gain * 0.8f, -0.15f, fromHz * up);
                break;
            }

            case 1:
                PlayLead(d, Lead.Violins, at, hz, hold, gain * 0.85f, -0.45f, fromHz);
                break;
            case 2:
                PlayLead(d, Lead.Horn, at, hz * 0.5f, hold, gain * 1.1f, 0.25f, fromHz * 0.5f);
                PlayLead(d, Lead.Violins, at, hz, hold, gain * 0.4f, -0.45f, fromHz);
                break;
            default:
                PlayLead(d, Lead.Violins, at, hz, hold, gain, -0.45f, fromHz);
                PlayLead(d, Lead.Horn, at, hz * 0.5f, hold, gain * 0.8f, 0.25f, fromHz * 0.5f);
                if (fluteUp) PlayLead(d, Lead.Flute, at, hz * 2f, hold, gain * 0.35f, -0.1f, fromHz * 2f);
                break;
        }
    }

    /// <summary>
    /// The orchestra's strings and horns: violins II, violas and cellos on the chord (three-saw sections with vibrato, panned
    /// left to right, well into the reverb), their filter following the dynamics and opening towards the next bar's;
    /// basses on the root; the horns join the chord when it is loud (f and up).
    /// </summary>
    private void OrchestraPad(Deck d, int chord, float hold, long frame)
    {
        var p = d.Profile;
        var bar = d.Bar;
        var dyn = OrchestraDynamics(d, bar);
        var next = OrchestraDynamics(d, bar < 0 ? 0 : (bar + 1) % CycleBars);
        var pause = bar == OrchestraClimaxBar - 1; // the sections stop for the general pause (AD-60)
        var tails = OrchestraTails(d);
        var release = pause ? 0.12f : 0.9f * tails;
        if (pause) hold = MathF.Max(0.1f, hold - (float)((16 - OrchestraPauseSixteenth) * d.FramesPerSixteenth / _rate) - 0.05f);
        ReadOnlySpan<(int Tone, int Octave, float Pan)> section = [(0, 0, 0.15f), (2, 12, -0.3f), (4, 12, -0.1f)];
        foreach (var (tone, octave, pan) in section)
        {
            var midi = p.Root + octave + ScaleNote(p.Scale, chord + tone);
            Enqueue(frame, NoteSpec.Saw(MidiHz(midi), 3, 7f, 700f + 1300f * dyn) with
            {
                ToneToHz = 700f + 1300f * next, ToneGlide = hold, Attack = 0.25f, Decay = 2f, Sustain = 0.85f, Hold = hold, Release = release,
                Gain = 0.013f * (0.5f + 0.5f * dyn), VibratoHz = 5.2f, VibratoDepth = 0.0025f, Pan = pan, Send = 0.65f, Group = d.Group,
            });
        }

        Enqueue(frame, NoteSpec.Saw(MidiHz(p.Root - 12 + BassSemitones(p, chord)), 2, 5f, 500f + 500f * dyn) with
        {
            Attack = 0.2f, Decay = 2f, Sustain = 0.85f, Hold = hold, Release = pause ? release : 0.8f * tails, Gain = 0.03f * (0.5f + 0.5f * dyn), Pan = 0.45f, Send = 0.45f, Group = d.Group,
        });
        if (dyn < 0.8f) return;
        foreach (var tone in (ReadOnlySpan<int>)[2, 4])
        {
            Enqueue(frame, LeadSpec(Lead.Horn, MidiHz(p.Root + ScaleNote(p.Scale, chord + tone)), hold) with { Attack = 0.25f, Gain = 0.018f * dyn, Pan = 0.25f, Group = d.Group, Release = pause ? release : 0.45f * tails });
        }
    }

    /// <summary>
    /// One sixteenth of the orchestra's pulse: the strings' pizzicato under the woodwind's motif, then their spiccato ostinato
    /// on chord tones (from the answer on, eighths of the pulse, softer and darker when the dynamics are low), timpani in the loud bars with a roll into the bar-14
    /// climax, suspended-cymbal swells into the climaxes and a crash on them, and the harp's falling arpeggio in bar 15.
    /// </summary>
    private void OrchestraGroove(Deck d, int inBar, int bar, bool intro, long frame, int chord)
    {
        if (intro) return;
        var p = d.Profile;
        var g = d.Group;
        var dyn = OrchestraDynamics(d, bar);
        if (bar < 2 && inBar % 2 == 0)
        {
            // Under the woodwind's motif the strings play pizzicato: plucked chord tones on the pulse's eighths.
            ReadOnlySpan<int> pizz = [0, 2, 1, 2, 0, 2, 1, 2];
            var midi = p.Root + ScaleNote(p.Scale, LadderDegree(p, chord, pizz[inBar / 2]));
            Enqueue(frame, NoteSpec.Saw(MidiHz(midi), 1, 0f, 3500f) with
            {
                ToneToHz = 500f, ToneGlide = 0.12f, Attack = 0.001f, Decay = 0.3f, Gain = inBar % 4 == 0 ? 0.06f : 0.045f, Pan = 0.15f, Send = 0.45f, Group = g,
            });
        }
        else if (bar >= 2 && (bar != CycleBars - 1 || inBar < 8) && !(bar == OrchestraClimaxBar - 1 && inBar >= OrchestraPauseSixteenth))
        {
            ReadOnlySpan<int> rungs = [0, 2, 1, 2, 0, 2, 1, 2, 0, 2, 1, 2, 0, 3, 2, 1];
            var midi = p.Root + ScaleNote(p.Scale, LadderDegree(p, chord, rungs[inBar]));
            var accent = inBar % 4 == 0 ? 1f : 0.7f;
            // Spiccato: the bow's bite (the filter starts bright and closes within 80 ms), then a short ring.
            Enqueue(frame, NoteSpec.Saw(MidiHz(midi), 2, 6f, 3200f + 1600f * dyn) with
            {
                ToneToHz = 1000f + 1200f * dyn, ToneGlide = 0.08f, Attack = 0.003f, Decay = 0.16f, Gain = 0.05f * accent * (0.45f + 0.55f * dyn), Pan = 0.15f, Send = 0.45f, Group = g,
            });
        }

        if (bar < d.ThemeBars)
        {
            if (bar >= 4 && inBar == 0) Timpani(d, frame, chord, 0.8f * dyn);
            else if (bar >= 4 && inBar == 8) Timpani(d, frame, chord, 0.5f * dyn);
            else if (bar >= 6 && inBar is 4 or 12) Timpani(d, frame, chord, 0.35f);
        }
        else if (bar == OrchestraClimaxBar - 1 && inBar < OrchestraPauseSixteenth)
        {
            // The roll into the climax: two strokes per sixteenth, crescendo, and the upper strings' tremolo swelling with it
            // (AD-60); both stop for the general pause.
            var vel = 0.2f + 0.6f * inBar / 16f;
            Timpani(d, frame, chord, vel);
            Timpani(d, frame + (long)(d.FramesPerSixteenth / 2), chord, vel * 0.9f);
            StringTremolo(d, frame, chord, inBar, vel);
        }
        else if (bar == OrchestraClimaxBar && inBar is 0 or 8)
        {
            Timpani(d, frame, chord, inBar == 0 ? 1f : 0.6f);
            if (inBar == 0) Timpani(d, frame, 0, 0.8f); // and the tonic under it: the climax's weight
        }

        if (inBar == 0 && bar == 5) CymbalSwell(d, frame, 16, 0.8f * OrchestraTails(d));
        if (inBar == 0 && bar == OrchestraClimaxBar - 1) CymbalSwell(d, frame, OrchestraPauseSixteenth, 0.1f); // choked for the general pause
        if (inBar == 0 && (bar == 6 || bar == 14)) Crash(frame, 0.8f, g);
        if (bar == CycleBars - 1 && inBar < 8) Harp(d, frame, chord, 7 - inBar, 0.8f);
    }

    /// <summary>The orchestra's climax bar (tutti, ff), after the timpani roll and the general pause (AD-60).</summary>
    private const int OrchestraClimaxBar = 14;

    /// <summary>The general pause: the last two sixteenths of the bar before the climax are silent (AD-60).</summary>
    private const int OrchestraPauseSixteenth = 14;

    /// <summary>
    /// The upper strings' measured tremolo into the climax: two bowed notes per sixteenth on the chord's tones, an octave
    /// above the key's root, swelling with <paramref name="vel"/>.
    /// </summary>
    private void StringTremolo(Deck d, long frame, int chord, int inBar, float vel)
    {
        var p = d.Profile;
        ReadOnlySpan<int> rungs = [2, 1];
        for (var k = 0; k < 2; k++)
        {
            var midi = p.Root + 12 + ScaleNote(p.Scale, LadderDegree(p, chord, rungs[(inBar + k) % 2]));
            Enqueue(frame + (long)(k * d.FramesPerSixteenth / 2), NoteSpec.Saw(MidiHz(midi), 2, 6f, 2400f + 1600f * vel) with
            {
                Attack = 0.004f, Decay = 0.12f, Gain = 0.032f * vel, VibratoHz = 5.5f, VibratoDepth = 0.002f, Pan = -0.35f, Send = 0.5f, Group = d.Group,
            });
        }
    }

    /// <summary>
    /// Between the orchestra's returns: the cellos' band motif (the flute echoing), then the horn call (mi sol do') and the
    /// violins' answer falling back, stopping short for the general pause; and the climax (AD-60): the horn call again, an
    /// octave up on the violins over the horns, fortissimo.
    /// </summary>
    private void OrchestraFree(Deck d, int bar, int inBar, bool first, int phrase, long cycle, long frame, int chord)
    {
        if (bar < 12)
        {
            if (first) BandMotif(d, frame, phrase, cycle);
            return;
        }

        if (inBar != 0) return;
        if (bar == 12)
        {
            foreach (var (step, length, degree) in (ReadOnlySpan<(int, int, int)>)[(0, 4, 2), (4, 4, 4), (8, 8, 7)]) MotifNote(d, StepAt(d, frame, step), degree, length, 1f, Lead.Horn);
        }
        else if (bar == OrchestraClimaxBar - 1)
        {
            foreach (var (step, length, degree) in (ReadOnlySpan<(int, int, int)>)[(0, 4, 9), (4, 2, 8), (6, 2, 7), (8, 6, 4)]) MotifNote(d, StepAt(d, frame, step), degree, length, 0.9f, Lead.Violins);
        }
        else if (bar == OrchestraClimaxBar)
        {
            foreach (var (step, length, degree) in (ReadOnlySpan<(int, int, int)>)[(0, 4, 2), (4, 4, 4), (8, 8, 7)])
            {
                MotifNote(d, StepAt(d, frame, step), degree + 7, length, 1f, Lead.Violins);
                MotifNote(d, StepAt(d, frame, step), degree, length, 0.9f, Lead.Horn);
            }
        }
    }

    /// <summary>A timpani stroke on the chord's root (an octave below the key's root): a sine settling 1 % down, its overtone, the felt's thump.</summary>
    private void Timpani(Deck d, long at, int chord, float vel)
    {
        var hz = MidiHz(d.Profile.Root - 12 + BassSemitones(d.Profile, chord));
        var g = d.Group;
        Enqueue(at, NoteSpec.Tone(Wave.Sine, hz * 1.01f) with { GlideToHz = hz, Glide = 0.08f, Attack = 0.002f, Decay = 1.1f, Gain = 0.12f * vel, Pan = 0.05f, Send = 0.35f, Group = g });
        Enqueue(at, NoteSpec.Tone(Wave.Triangle, hz * 1.5f) with { Attack = 0.002f, Decay = 0.35f, Gain = 0.03f * vel, ToneHz = 900, Send = 0.35f, Group = g });
        Enqueue(at, NoteSpec.Noise(500, 300, 0.05f, q: 0.8f, lowpass: true) with { Attack = 0.001f, Decay = 0.06f, Gain = 0.05f * vel, Send = 0.3f, Group = g });
    }

    /// <summary>A suspended cymbal rolled up over <paramref name="sixteenths"/> (a bar: peaking on the next downbeat), then let ring for <paramref name="decaySeconds"/> (T60; short: choked).</summary>
    private void CymbalSwell(Deck d, long at, int sixteenths, float decaySeconds)
    {
        var len = (float)(sixteenths * d.FramesPerSixteenth / _rate);
        Enqueue(at, NoteSpec.Noise(5000, 8000, len, q: 0.6f) with { Attack = len, Decay = decaySeconds, Gain = 0.035f, Pan = -0.2f, Send = 0.6f, Group = d.Group });
    }

    /// <summary>A harp note on the chord-tone ladder (rung 0 the chord's root, an octave above the key's).</summary>
    private void Harp(Deck d, long at, int chord, int rung, float vel)
    {
        var p = d.Profile;
        var midi = p.Root + 12 + ScaleNote(p.Scale, LadderDegree(p, chord, rung));
        Enqueue(at, NoteSpec.Bell(MidiHz(midi), ratio: 2f, index: 0.8f, indexDecay: 0.05f) with { Attack = 0.002f, Decay = 1.2f * OrchestraTails(d), Gain = 0.04f * vel, Pan = 0.4f, Send = 0.6f, ToneHz = 5000, Group = d.Group });
    }

    /// <summary>The orchestra's first downbeat: a harp glissando up the chord, a timpani stroke and a soft cymbal.</summary>
    private void OrchestraArrival(Deck d, long frame)
    {
        for (var i = 0; i < 8; i++) Harp(d, frame + (long)(i * d.FramesPerSixteenth * 0.5), 0, i, 0.7f + 0.04f * i);
        Timpani(d, frame, 0, 0.9f);
        Crash(frame, 0.5f, d.Group);
    }

    // ---------------------------------------------------------------- drum and bass (band 4, the underglow)

    // Two bars of the 172 BPM break per bar of the half-time score: 32 pulse sixteenths (X accent, x normal, o ghost).
    private static readonly float[] BreakKick = Hits("X.........X.....X..o......X.....");
    private static readonly float[] BreakSnare = Hits("....X.......X.......X.......X...");
    private static readonly float[] BreakGhost = Hits(".......o.o.....o.......o.o....oo");

    // Drum and bass's phrase-level drama (AD-60): bars 0–7 the theme over the break; 8–9 the breakdown; 10–11 the build;
    // 12–15 the drop, which opens with a sub drop and plays the heavy reese.

    /// <summary>The breakdown's first bar: no drums, no reese; the strings, a held sub and the band motif.</summary>
    private const int DnbBreakdownBar = 8;

    /// <summary>The build's first bar: a snare roll from the pulse's eighths to 32nds, a riser, the reese climbing an octave.</summary>
    private const int DnbBuildBar = 10;

    /// <summary>The drop's first bar: the sub drop, then the heavy reese and the full break to the cycle's end.</summary>
    private const int DnbDropBar = 12;

    /// <summary>The sub drop's dive, seconds: from the key's root an octave below middle down another octave (≈ 30–45 Hz).</summary>
    private const float SubDropDiveSeconds = 0.9f;

    /// <summary>
    /// One score sixteenth of drum and bass (two of the pulse). The theme's bars (0–7) play the break (a two-step kick, snares
    /// on the pulse's 2 and 4, ghosts from the second visit, a fill into the breakdown), sixteenth hats, a metallic ride on the
    /// offbeats and the reese. Then the drama (AD-60): the breakdown (bars 8–9: drums and reese out, a held sub, soft air), the
    /// build (bars 10–11: a snare roll from the pulse's eighths through sixteenths to 32nds, a kick on the beat in bar 10, a
    /// riser, the reese climbing an octave, the last eighth silent), and the drop (bars 12–15: a sub drop and an impact, then
    /// the full break with ghosts and the heavy reese, <see cref="Reese"/>). The theme's return at bar 0 lands on a crash.
    /// </summary>
    private void DrumAndBassGroove(Deck d, long step, int inBar, int bar, bool intro, long frame, int chord)
    {
        var g = d.Group;
        var dev = d.Development;
        var p = d.Profile;
        var breakdown = !intro && bar is DnbBreakdownBar or DnbBreakdownBar + 1;
        var buildUp = !intro && bar is DnbBuildBar or DnbBuildBar + 1;
        var drop = !intro && bar >= DnbDropBar;
        if (buildUp && bar == DnbBuildBar + 1 && inBar == 15) return; // the gasp before the drop: one pulse eighth of silence

        if (!intro && bar == 0 && inBar == 0 && step != 0) Crash(frame, 0.9f, g); // the theme returns
        if (breakdown)
        {
            if (inBar % 4 == 2) Enqueue(frame, NoteSpec.Noise(7000, 6500, 0.03f, q: 1.2f) with { Attack = 0.008f, Decay = 0.08f, Gain = 0.02f, Pan = inBar % 8 < 4 ? -0.3f : 0.3f, Send = 0.3f, Group = g });
            if (inBar == 0)
            {
                Enqueue(frame, NoteSpec.Tone(Wave.Sine, MidiHz(p.Root - 24 + BassSemitones(p, chord))) with
                {
                    Attack = 0.08f, Decay = 2f, Sustain = 0.85f, Hold = (float)(15 * d.FramesPerSixteenth / _rate), Release = 0.4f, Gain = 0.05f, Send = 0.05f, Group = g,
                });
            }

            return;
        }

        if (buildUp)
        {
            var q = ((bar - DnbBuildBar) * 16 + inBar) / 32f;
            var per = bar == DnbBuildBar ? 1 : inBar < 8 ? 2 : 4; // the pulse's eighths, sixteenths, then 32nds
            for (var k = 0; k < per; k++)
            {
                Snare(SnareKind.Snare, frame + (long)(k * d.FramesPerSixteenth / per), 0.25f + 0.6f * q, k % 2 == 0 ? -0.15f : 0.15f, g);
            }

            if (bar == DnbBuildBar && inBar % 4 == 0) Kick(frame, 0.75f, g);
            if (inBar % 2 == 0) ClosedHat(frame, 0.3f + 0.4f * q, inBar % 4 == 0 ? -0.3f : 0.3f, g);
            if (bar == DnbBuildBar && inBar == 0)
            {
                var len = (float)(31 * d.FramesPerSixteenth / _rate);
                Enqueue(frame, NoteSpec.Noise(300, 8000, len, q: 1.4f) with { Attack = len, Decay = 0.05f, Gain = 0.045f, Send = 0.4f, Group = g });
                var hz = MidiHz(p.Root - 12 + BassSemitones(p, chord));
                Enqueue(frame, NoteSpec.Saw(hz, 2, 14f, 300f) with
                {
                    GlideToHz = hz * 2f, Glide = len, ToneToHz = 1500f, ToneGlide = len, Attack = 0.05f, Decay = 3f, Sustain = 0.9f, Hold = len, Release = 0.05f, Gain = 0.05f, Send = 0.1f, Group = g,
                });
            }

            return;
        }

        if (drop && bar == DnbDropBar && inBar == 0) SubDrop(d, frame);
        var roller = drop;
        for (var h = 0; h < 2; h++)
        {
            var sub = inBar * 2 + h;
            var at = frame + (long)(h * d.FramesPerSixteenth * 0.5);
            if (BreakKick[sub] > 0) Kick(at, BreakKick[sub], g);
            var fill = bar == d.ThemeBars - 1 && sub >= 24; // into the breakdown
            if (fill) Snare(SnareKind.Snare, at, 0.35f + 0.08f * (sub - 24), sub % 2 == 0 ? -0.2f : 0.2f, g);
            else if (BreakSnare[sub] > 0) BreakSnareHit(at, BreakSnare[sub], g);
            else if ((dev >= 2 || roller) && BreakGhost[sub] > 0) Snare(SnareKind.Snare, at, 0.22f, 0.15f, g);

            if (sub % 2 == 0) ClosedHat(at, 0.6f, sub % 4 == 0 ? -0.3f : 0.3f, g);
            else if (dev >= 2 || roller) ClosedHat(at, 0.28f, 0.35f, g);
            if (sub % 4 == 2) Enqueue(at, NoteSpec.Bell(2400f, ratio: 1.41f, index: 2.2f, indexDecay: 0.05f) with { Attack = 0.0005f, Decay = 0.35f, Gain = 0.02f, Pan = 0.4f, Send = 0.2f, ToneHz = 9000, Group = g });
        }

        // The reese: held notes per score bar (even bars root … root, fifth; odd bars root, octave, root); heavy in the drop.
        var even = bar % 2 == 0 || intro;
        var (play, steps, interval) = even
            ? inBar switch { 0 => (true, 8, 0), 8 => (true, 4, 0), 12 => (true, 4, -1), _ => (false, 0, 0) }
            : inBar switch { 0 => (true, 6, 0), 6 => (true, 2, 12), 8 => (true, 8, 0), _ => (false, 0, 0) };
        if (play)
        {
            var semis = BassSemitones(p, chord) + (interval == -1 ? ScaleNote(p.Scale, chord + 4) - ScaleNote(p.Scale, chord) : interval);
            Reese(d, frame, p.Root - 12 + semis, (float)(steps * d.FramesPerSixteenth / _rate) - 0.03f, heavy: drop);
        }
    }

    /// <summary>
    /// The reese bass: two saws 14 cents apart (they phase against each other, the reese's growl), filtered at 260 Hz opening
    /// to 650 Hz over the note, with a sine sub an octave below. <paramref name="heavy"/> (the drop, AD-60): the filter opens
    /// from 400 Hz to 1.6 kHz and a third saw an octave up joins, so its harmonics carry it on small speakers that cannot
    /// play the sub (measured: SkySynthTests' drama render reports the 100–300 Hz band per bar), and the sub is louder.
    /// </summary>
    private void Reese(Deck d, long at, int midi, float hold, bool heavy = false)
    {
        var hz = MidiHz(midi);
        var held = MathF.Max(0.05f, hold);
        if (!heavy)
        {
            Enqueue(at, NoteSpec.Saw(hz, 2, 14f, 260f) with { ToneToHz = 650f, ToneGlide = held, Attack = 0.01f, Decay = 2f, Sustain = 0.9f, Hold = held, Release = 0.08f, Gain = 0.055f, Send = 0.05f, Group = d.Group });
            Enqueue(at, NoteSpec.Tone(Wave.Sine, hz * 0.5f) with { Attack = 0.01f, Decay = 2f, Sustain = 0.9f, Hold = held, Release = 0.08f, Gain = 0.08f, Send = 0f, Group = d.Group });
            return;
        }

        Enqueue(at, NoteSpec.Saw(hz, 2, 18f, 400f) with { ToneToHz = 1600f, ToneGlide = held, Attack = 0.008f, Decay = 2f, Sustain = 0.9f, Hold = held, Release = 0.08f, Gain = 0.06f, Send = 0.05f, Group = d.Group });
        Enqueue(at, NoteSpec.Saw(hz * 2f, 1, 0f, 900f) with { ToneToHz = 2200f, ToneGlide = held, Attack = 0.01f, Decay = 2f, Sustain = 0.8f, Hold = held, Release = 0.08f, Gain = 0.03f, Pan = 0.2f, Send = 0.1f, Group = d.Group });
        Enqueue(at, NoteSpec.Tone(Wave.Sine, hz * 0.5f) with { Attack = 0.01f, Decay = 2f, Sustain = 0.9f, Hold = held, Release = 0.08f, Gain = 0.1f, Send = 0f, Group = d.Group });
    }

    /// <summary>
    /// The drop's sub drop (AD-60): a sine diving an octave from the key's root an octave below the bass (≈ 30–45 Hz at the
    /// bottom) and holding, with a saw an octave above it diving with it (its harmonics, ≈ 100–400 Hz, let small speakers
    /// hear the drop the sub alone would hide), a crash and the snare's crack.
    /// </summary>
    private void SubDrop(Deck d, long at)
    {
        var p = d.Profile;
        var g = d.Group;
        var hz = MidiHz(p.Root - 12);
        Enqueue(at, NoteSpec.Tone(Wave.Sine, hz) with { GlideToHz = hz * 0.5f, Glide = SubDropDiveSeconds, Attack = 0.003f, Decay = 2.5f, Sustain = 0.7f, Hold = 1.6f, Release = 0.5f, Gain = 0.13f, Send = 0f, Group = g });
        Enqueue(at, NoteSpec.Saw(hz * 2f, 2, 12f, 1400f) with { GlideToHz = hz, Glide = SubDropDiveSeconds, ToneToHz = 500f, ToneGlide = 1.2f, Attack = 0.003f, Decay = 1.4f, Sustain = 0f, Release = 0.3f, Gain = 0.08f, Send = 0.15f, Group = g });
        Crash(at, 1f, g);
        BreakSnareHit(at, 1f, g);
    }

    /// <summary>The break's snare: the kit's snare with a bright crack on top.</summary>
    private void BreakSnareHit(long at, float vel, int g)
    {
        Snare(SnareKind.Snare, at, vel, 0, g);
        Enqueue(at, NoteSpec.Noise(4200, 3000, 0.03f, q: 1f) with { Attack = 0.0005f, Decay = 0.09f, Gain = 0.05f * vel, Send = 0.2f, Group = g });
    }

    /// <summary>Drum and bass's strings: an airy three-saw chord an octave up, slow to swell and slow to fade, with a slow chorus-like wobble.</summary>
    private void DrumAndBassPad(Deck d, int chord, float hold, long frame)
    {
        var p = d.Profile;
        ReadOnlySpan<int> tones = [0, 2, 4];
        for (var i = 0; i < tones.Length; i++)
        {
            var midi = p.Root + 12 + ScaleNote(p.Scale, chord + tones[i]);
            Enqueue(frame, NoteSpec.Saw(MidiHz(midi), 3, 10f, 1200f) with
            {
                Attack = 0.7f, Decay = 2f, Sustain = 0.8f, Hold = hold, Release = 1.4f, Gain = 0.013f, VibratoHz = 0.35f, VibratoDepth = 0.002f, Pan = -0.5f + 0.5f * i, Send = 0.75f, Group = d.Group,
            });
        }
    }

    /// <summary>Drum and bass's first downbeat: kick, snare, a crash and the impact, all at once.</summary>
    private void DrumAndBassArrival(Deck d, long frame)
    {
        Kick(frame, 1f, d.Group);
        BreakSnareHit(frame, 1f, d.Group);
        Crash(frame, 1f, d.Group);
        DropImpact(d.Profile, frame, 0.6f, d.Group);
    }

    // ---------------------------------------------------------------- the abyss (the AD-52 hazard score, kept by AD-60)

    /// <summary>
    /// One score sixteenth of the abyss (until AD-60 the hazard world's score): a heartbeat (lub-dub on beats 1 and 3), a metallic tick on every step (and,
    /// from the second visit, between them), a struck metal on beat 4 of odd bars, and a reversed swell into every second
    /// bar line (the black hole drawing in).
    /// </summary>
    private void AbyssGroove(Deck d, int inBar, int bar, long frame, int chord)
    {
        var p = d.Profile;
        var g = d.Group;
        if (inBar is 0 or 8) Heartbeat(frame, 0.9f, g);
        if (inBar is 1 or 9) Heartbeat(frame, 0.5f, g);
        HazardTick(p, frame, inBar % 4 == 0 ? 1f : 0.6f, inBar % 2 == 0 ? -0.4f : 0.4f, g);
        if (d.Development >= 2) HazardTick(p, frame + (long)(d.FramesPerSixteenth / 2), 0.35f, 0.2f, g);
        if (bar % 2 == 1 && inBar == 12)
        {
            Enqueue(frame, NoteSpec.Bell(MidiHz(p.Root - 12), ratio: 1.41f, index: 3f, indexDecay: 0.6f) with { Attack = 0.002f, Decay = 2.5f, Gain = 0.05f, Pan = 0.2f, Send = 0.7f, Group = g });
        }

        if (bar % 2 == 1 && inBar == 0)
        {
            var len = (float)(16 * d.FramesPerSixteenth / _rate);
            Enqueue(frame, NoteSpec.Noise(250, 2500, len, q: 1.5f) with { Attack = len, Decay = 0.04f, Gain = 0.03f, Send = 0.5f, Group = g });
        }
    }

    /// <summary>A heartbeat thump: a low sine falling 90 → 38 Hz, no click.</summary>
    private void Heartbeat(long at, float vel, int g) =>
        Enqueue(at, NoteSpec.Tone(Wave.Sine, 90f) with { GlideToHz = 38f, Glide = 0.08f, Attack = 0.003f, Decay = 0.35f, Gain = 0.17f * vel, Send = 0.1f, Group = g });

    /// <summary>A ticking-metal click four octaves above the key's root (inharmonic FM, 1 : 1.41).</summary>
    private void HazardTick(Profile p, long at, float vel, float pan, int g) =>
        Enqueue(at, NoteSpec.Bell(MidiHz(p.Root + 48), ratio: 1.41f, index: 1.2f, indexDecay: 0.02f) with { Attack = 0.0005f, Decay = 0.04f, Gain = 0.03f * vel, Pan = pan, Send = 0.25f, Group = g });

    /// <summary>
    /// The abyss's pad (the AD-52 hazard pad): a drone two octaves down (the root twice, 7 cents either side, so it beats, and the fifth),
    /// dark strings on the chord with an uneasy wobble, and a low rumble; all slow to come and go.
    /// </summary>
    private void AbyssPad(Deck d, int chord, float hold, long frame)
    {
        var p = d.Profile;
        var g = d.Group;
        var detune = MathF.Pow(2f, 7f / 1200f);
        ReadOnlySpan<(float Ratio, int Semitones, float Pan)> drone = [(detune, 0, -0.3f), (1f / detune, 0, 0.3f), (1f, 7, 0f)];
        foreach (var (ratio, semis, pan) in drone)
        {
            Enqueue(frame, NoteSpec.Tone(Wave.Sine, MidiHz(p.Root - 24 + semis) * ratio) with
            {
                Attack = 1.2f, Decay = 2f, Sustain = 0.9f, Hold = hold, Release = 1.5f, Gain = 0.035f, Pan = pan, Send = 0.4f, Group = g,
            });
        }

        foreach (var (tone, pan) in (ReadOnlySpan<(int, float)>)[(0, -0.4f), (2, 0.4f)])
        {
            Enqueue(frame, NoteSpec.Saw(MidiHz(p.Root + ScaleNote(p.Scale, chord + tone)), 2, 12f, 650f) with
            {
                Attack = 1.5f, Decay = 2f, Sustain = 0.8f, Hold = hold, Release = 1.6f, Gain = 0.014f, VibratoHz = 0.4f, VibratoDepth = 0.004f, Pan = pan, Send = 0.7f, Group = g,
            });
        }

        Enqueue(frame, NoteSpec.Noise(130, 130, 0, q: 0.7f, lowpass: true) with { Attack = 2f, Decay = 2f, Sustain = 0.9f, Hold = hold, Release = 2f, Gain = 0.03f, Send = 0.3f, Group = g });
    }

    /// <summary>Between the abyss's returns: the theme's head turned downwards (do' sol mi re), low on the unstable lead, every other bar.</summary>
    private void AbyssFree(Deck d, int bar, bool first, long frame)
    {
        if (!first) return;
        var last = bar % 4 == 2 ? 0 : 1; // the second call sinks to the tonic
        foreach (var (step, length, degree) in (ReadOnlySpan<(int, int, int)>)[(0, 4, 7), (4, 4, 4), (8, 4, 2), (12, 12, last)])
        {
            MotifNote(d, StepAt(d, frame, step), degree - 7, length, 0.8f, Lead.Unstable);
        }
    }

    /// <summary>The abyss's first downbeat: a deep, inharmonic gong and a boom falling an octave.</summary>
    private void AbyssArrival(Deck d, long frame)
    {
        var p = d.Profile;
        Enqueue(frame, NoteSpec.Bell(MidiHz(p.Root - 12), ratio: 1.41f, index: 2.5f, indexDecay: 1.5f) with { Attack = 0.003f, Decay = 4f, Gain = 0.08f, Send = 0.7f, Group = d.Group });
        Enqueue(frame, NoteSpec.Tone(Wave.Sine, PitchHz(p, 0, -1)) with { GlideToHz = PitchHz(p, 0, -2), Glide = 0.6f, Attack = 0.003f, Decay = 1.8f, Gain = 0.12f, Send = 0.2f, Group = d.Group });
    }
}
