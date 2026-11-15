using FileScape.Core.Planets;

namespace FileScape.Core.Audio;

// The music of SkySynth: the world identities (AD-49), the two decks and their clocks, the FileScape theme and each world's
// grooves and motifs. Everything here runs inside Render (ScheduleMusic → OnStep), except SetWorld and Configure.
public sealed partial class SkySynth
{
    private const int CycleSixteenths = CycleBars * 16;
    private const int HalfCycleSixteenths = CycleSixteenths / 2;

    /// <summary>Capacity of a deck's prepared notes per cycle (the theme, its ornaments or counter-line): a fixed array, reused, so building a cycle never allocates.</summary>
    private const int MaxCycleNotes = 160;

    /// <summary>The development a world reaches from its third visit on (1 first visit, 2 second, 3 from the third: see <see cref="Development"/>).</summary>
    private const int FullDevelopment = 3;

    /// <summary>
    /// Two world changes closer than this are a hop (AD-49: "&lt; 1.5 s apart"): the arrival fanfare is skipped, so
    /// hopping through several folders stays musical rather than a pile-up of flourishes.
    /// </summary>
    private const float QuickHopSeconds = 1.5f;

    /// <summary>T60 with which a reused deck's voices are cut, seconds (≈ 40 ms, AD-49): fast enough to clear the deck, slow enough not to click.</summary>
    private const float ClearedDeckT60Seconds = 0.04f;

    /// <summary>Flight intensity (<see cref="SetFlight"/>'s target) above which the flight arpeggio plays; at or below it the arpeggio stops and restarts from its first note next time.</summary>
    private const float FlightArpeggioFrom = 0.02f;

    /// <summary>Flight intensity above which the flight arpeggio climbs an octave (the high part of a flight).</summary>
    private const float FlightArpeggioOctaveUpFrom = 0.6f;

    private static readonly ThemeNote[] ThemeNotes = BuildTheme();

    /// <summary>The character the synth starts in when none is given: the hub, the calm arrival.</summary>
    private static readonly WorldCharacter HubCharacter = new(WorldKind.Hub, 0, 0, -1);

    /// <summary>Recently left worlds and where each was left (sixteenths into its music), most recent first.</summary>
    private readonly (long Key, long Position)[] _memory = new (long, long)[RememberedWorlds];
    private int _memoryCount;

    // ---------------------------------------------------------------- worlds

    /// <summary>The musical identity of a world's character: its kind, and for a folder its depth band (1–4).</summary>
    public static MusicIdentity IdentityOf(WorldCharacter character) => character.Kind switch
    {
        WorldKind.Hub => MusicIdentity.Hub,
        WorldKind.WindowsRoot => MusicIdentity.WindowsRoot,
        WorldKind.WslRoot => MusicIdentity.WslRoot,
        WorldKind.Repository => MusicIdentity.Repository,
        WorldKind.Hazard => MusicIdentity.Hazard,
        _ => MusicIdentity.Outskirts + (Math.Clamp(character.Band, 1, WorldCharacter.MaxBand) - 1),
    };

    /// <summary>
    /// The tempo of an identity, beats (quarter notes) per minute: its pulse, the drums' tempo and the grid that tempo-locked
    /// effects and <see cref="AlignToGrid"/> follow.
    /// </summary>
    public static float TempoFor(MusicIdentity identity) => Styles[(int)identity].Bpm;

    /// <summary>
    /// The tempo the theme, the harmony and the score move at, beats per minute: <see cref="TempoFor"/>, or half of it in a
    /// half-time arrangement (AD-52: the orchestra, drum and bass, the hazard world), whose broad theme rides on a pulse twice as fast.
    /// </summary>
    public static float ThemeTempoFor(MusicIdentity identity) => Styles[(int)identity].HalfTime ? Styles[(int)identity].Bpm / 2 : Styles[(int)identity].Bpm;

    /// <summary>A folder's depth-band identity (the outskirts to the underglow), the ones that share the band motif.</summary>
    private static bool IsBand(MusicIdentity id) => id is >= MusicIdentity.Outskirts and <= MusicIdentity.Underglow;

    /// <summary>
    /// Moves the music to a world, on every arrival. <paramref name="visits"/> counts the arrivals at this folder in the
    /// session (1 on the first): a world develops with it. The new world starts on the next beat of the music playing, on
    /// its own first downbeat and clock; until that downbeat a further call only retargets it (so hops never stack), and a
    /// call back to the world still playing cancels the change. The same world and key again keeps the groove and plays a
    /// drum fill on the last beat of the bar. A world left recently resumes on its next half-cycle.
    /// </summary>
    public void SetWorld(PlanetTheme theme, WorldCharacter character, int visits)
    {
        visits = Math.Max(1, visits);
        var cur = _decks[_current];
        if (cur.Live && cur.Theme == theme && cur.Character == character && cur.Visits == visits) return;
        var id = IdentityOf(character);
        var key = WorldKeyOf(id, theme, character);
        var other = _decks[1 - _current];
        if (cur.Live && cur.NextStep == 0)
        {
            // The current world has not reached its first downbeat: retarget it, or, back to the world still playing, stay.
            if (other.Live && other.WorldKey == key && other.StopFrame != long.MaxValue)
            {
                other.StopFrame = long.MaxValue;
                other.Character = character;
                other.Visits = visits;
                other.Development = Math.Clamp(visits, 1, FullDevelopment);
                other.FadeTarget = 1;
                cur.Live = false;
                cur.FadeTarget = 0;
                ClearDeck(cur);
                _current = 1 - _current;
                return;
            }

            Configure(cur, theme, character, visits, cur.Origin, cur.QuickHop);
            return;
        }

        if (cur.Live && cur.WorldKey == key)
        {
            // The same world (a sibling folder of the same band and theme, or a revisit): keep the groove, mark the arrival.
            cur.Character = character;
            cur.Visits = visits;
            cur.Development = Math.Clamp(visits, 1, FullDevelopment);
            // The fill takes the last beat of this bar (sixteenths 12–15), or of the next bar once that beat has begun.
            var s = cur.NextStep;
            var fill = s - Mod(s, 16) + 12;
            if (s > fill) fill += 16;
            cur.FillFrom = fill;
            return;
        }

        // A new world: the old one plays to its next beat, which becomes the new one's first downbeat.
        ClearDeck(other);
        var k = cur.NextStep;
        while (Mod(k, cur.StepsPerBeat) != 0) k++; // the next beat of its pulse (every fourth sixteenth; every second in half time)
        var origin = StepFrame(cur, k);
        cur.StopFrame = origin;
        var quick = origin - _lastSwitchFrame < Frames(QuickHopSeconds);
        _lastSwitchFrame = origin;
        _switches++;
        other.From = cur.Identity;
        Configure(other, theme, character, visits, origin, quick);
        other.FadeTarget = 1;
        _current = 1 - _current;
    }

    /// <summary>
    /// Prepares a deck for a world whose first downbeat is at <paramref name="origin"/>: its score and key, its clock,
    /// where in its music it starts (a remembered position, or later cycles for a developed world) and its random stream.
    /// Everything is a function of the seed, the world and the number of switches so far, so a session replays exactly.
    /// </summary>
    private void Configure(Deck d, PlanetTheme theme, WorldCharacter character, int visits, long origin, bool quickHop)
    {
        var id = IdentityOf(character);
        d.Identity = id;
        d.Style = Styles[(int)id];
        d.Theme = theme;
        d.Character = character;
        d.Visits = Math.Max(1, visits);
        d.Development = Math.Clamp(visits, 1, FullDevelopment);
        d.Profile = KeyProfiles[(int)id][(int)theme];
        d.Echo = character.InsideRepository && IsBand(id);
        d.WorldKey = WorldKeyOf(id, theme, character);
        d.Origin = origin;
        // The pulse's sixteenth (the effects' grid) and the score's: the same, except in a half-time arrangement, whose
        // score steps are eighths of the pulse (AD-52).
        d.GridFramesPerSixteenth = _rate * 60.0 / (d.Style.Bpm * 4.0); // frames per minute ÷ sixteenths per minute (4 per beat); fractional, and each step's frame is computed from the origin, so the clock never drifts
        d.FramesPerSixteenth = d.Style.HalfTime ? d.GridFramesPerSixteenth * 2 : d.GridFramesPerSixteenth;
        d.StepsPerBeat = d.Style.HalfTime ? 2 : 4;
        d.Level = d.Style.Level;
        d.PumpDepth = d.Style.PumpDepth;
        d.PumpLast = d.PumpPrev = long.MinValue / 2;
        d.DropAtStep = -1;
        d.DropArrival = false;
        d.BuildPeak = 0;
        d.Riser = null;
        d.NextStep = 0;
        d.StopFrame = long.MaxValue;
        d.Live = true;
        d.QuickHop = quickHop;
        var saved = Recall(d.WorldKey);
        d.Resumed = saved >= 0;
        // A return resumes on the next half-cycle; a later visit (forgotten) starts a later, varied cycle.
        d.StepOffset = d.Resumed ? (saved + HalfCycleSixteenths - 1) / HalfCycleSixteenths * HalfCycleSixteenths : (long)(d.Development - 1) * CycleSixteenths;
        d.IntroSteps = id == MusicIdentity.Repository && !d.Resumed ? 16 : 0; // one bar for the repository's arrival flourish
        // The deck's random stream: the seed, the world and the switch count hashed together (golden-ratio and xxHash
        // multipliers, then Mix), so each arrival varies yet the same session replays sample for sample.
        d.Rng = new Random(unchecked((int)Mix((uint)_seed * 0x9E3779B1u ^ (uint)d.WorldKey ^ (uint)(d.WorldKey >> 32) ^ (uint)_switches * 0x85EBCA77u)));
        d.NeedChord = true;
        d.CycleReady = false;
        d.FillFrom = long.MinValue;
        d.CounterDegree = 2;
        d.ResetMelody();
    }

    /// <summary>
    /// A world's identity for the music: its identity, theme, repository and worktree flag, packed into one long (identity
    /// in bits 56+, theme 48–55, worktree bit 47, repository id + 1 below; 0 for none). Two folders with the same key share
    /// their music: a sibling of the same band and theme keeps the groove.
    /// </summary>
    private static long WorldKeyOf(MusicIdentity id, PlanetTheme theme, WorldCharacter c)
    {
        var repo = c.RepositoryId >= 0 && (id == MusicIdentity.Repository || c.InsideRepository) ? c.RepositoryId + 1L : 0L;
        return ((long)id << 56) | ((long)theme << 48) | ((c.Worktree ? 1L : 0L) << 47) | (repo & 0x7FFF_FFFF_FFFFL);
    }

    /// <summary>Where a remembered world was left (sixteenths into its music), or −1.</summary>
    private long Recall(long key)
    {
        for (var i = 0; i < _memoryCount; i++)
        {
            if (_memory[i].Key == key) return _memory[i].Position;
        }

        return -1;
    }

    /// <summary>Remembers where a world was left (most recent first; the oldest is forgotten when full).</summary>
    private void Remember(long key, long position)
    {
        var i = 0;
        while (i < _memoryCount && _memory[i].Key != key) i++;
        if (i == _memoryCount)
        {
            if (_memoryCount < _memory.Length) _memoryCount++;
            i = _memoryCount - 1;
        }

        for (; i > 0; i--) _memory[i] = _memory[i - 1];
        _memory[0] = (key, position);
    }

    /// <summary>Clears a deck's groups (plain and pumped): its voices release in ≈ 40 ms and its scheduled notes are dropped.</summary>
    private void ClearDeck(Deck d)
    {
        foreach (var v in _voices)
        {
            if (v.Active && d.Owns(v.Group)) v.Kill(ClearedDeckT60Seconds);
        }

        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (d.Owns(_pending[i].Note.Group))
            {
                _pending[i] = _pending[^1];
                _pending.RemoveAt(_pending.Count - 1);
            }
        }
    }

    /// <summary>A true modulo (never negative), unlike C#'s %.</summary>
    private static long Mod(long x, long m) => ((x % m) + m) % m;

    // ---------------------------------------------------------------- scheduling

    /// <summary>
    /// Runs each live deck's sixteenth-note steps that fall before <paramref name="blockEnd"/> (and before its stop frame),
    /// so notes are scheduled one block ahead at most. A deck whose stop frame falls in this block stops there: it remembers
    /// where it was, releases its voices and drops its notes scheduled from then on.
    /// </summary>
    private void ScheduleMusic(long blockEnd)
    {
        for (var i = 0; i < 2; i++)
        {
            var d = _decks[i];
            if (!d.Live) continue;
            while (true)
            {
                var f = StepFrame(d, d.NextStep);
                if (f >= blockEnd || f >= d.StopFrame) break;
                OnStep(d, d.NextStep, Math.Max(f, _frame));
                d.NextStep++;
            }

            if (d.StopFrame < blockEnd)
            {
                // Its successor's first downbeat: this world stops, remembers where it was, and its tails fade.
                d.Live = false;
                d.FadeTarget = 0;
                Remember(d.WorldKey, Math.Max(d.StepOffset, d.NextStep - d.IntroSteps + d.StepOffset));
                foreach (var v in _voices)
                {
                    if (v.Active && d.Owns(v.Group)) v.Release();
                }

                for (var j = _pending.Count - 1; j >= 0; j--)
                {
                    if (d.Owns(_pending[j].Note.Group) && _pending[j].Frame >= d.StopFrame)
                    {
                        _pending[j] = _pending[^1];
                        _pending.RemoveAt(_pending.Count - 1);
                    }
                }
            }
        }
    }

    /// <summary>
    /// One sixteenth of a deck's music, starting at <paramref name="frame"/>: builds the cycle's theme notes at its start,
    /// changes the pad chord when due, then plays the arrival (step 0), the theme or the world's own material, the groove
    /// and, while flying, the flight arpeggio. <paramref name="step"/> counts from the deck's first downbeat; the position in
    /// the world's music (<c>abs</c>) adds the deck's resume offset and skips the intro bar.
    /// </summary>
    private void OnStep(Deck d, long step, long frame)
    {
        if (step == d.DropAtStep) AlignDrop(d, step);
        var p = d.Profile;
        var st = d.Style;
        var len = p.Scale.Length;
        var intro = step < d.IntroSteps;
        var abs = step - d.IntroSteps + d.StepOffset; // sixteenths into the world's music (cycles count from 0)
        // The bar follows the music's position (a drop re-starts the bar on its beat, AD-52); otherwise the same as the step's.
        var inBar = intro ? (int)Mod(step, 16) : (int)Mod(abs, 16);
        var pos = (int)Mod(abs, CycleSixteenths);
        var cycle = (abs - pos) / CycleSixteenths;
        var bar = pos / 16;
        if (!intro && (pos == 0 || !d.CycleReady))
        {
            BuildCycle(d, cycle);
            d.CycleReady = true;
            while (d.NextNote < d.NoteCount && d.Notes[d.NextNote].Start < pos) d.NextNote++; // resumed mid-cycle
        }

        // Harmony: a chord per bar under the theme; between returns, every st.FreeChordBars bars.
        int chord;
        int holdSixteenths;
        bool change;
        if (intro)
        {
            chord = p.Progression[0];
            holdSixteenths = (int)(d.IntroSteps - step);
            change = step == 0;
        }
        else if (bar < d.ThemeBars)
        {
            var drone = p.Pad != PadKind.Normal;
            // The augmented ending stretches bars 7–8 over 7–9: the harmony follows (V under the stretched approach, I under
            // the last note), and a drone ends where the theme ends (2026-11-11 review).
            var chordBar = d.Variation == ThemeVariation.AugmentedEnding && bar >= 6 ? (bar < 8 ? 6 : 7) : bar;
            chord = drone ? p.Progression[0] : ThemeChordAt(p, chordBar, d.Identity == MusicIdentity.Hub);
            holdSixteenths = drone ? Math.Min(32 - pos % 32, d.ThemeBars * 16 - pos) : 16 - pos % 16;
            change = drone ? pos % 32 == 0 : inBar == 0;
        }
        else
        {
            var per = st.FreeChordBars;
            var free = bar - d.ThemeBars;
            chord = FreeChord(d, bar);
            holdSixteenths = Math.Min(per - free % per, 16 - bar) * 16 - inBar;
            change = inBar == 0 && free % per == 0;
        }

        d.Bar = intro ? -1 : bar;
        d.InBar = inBar;
        if (change || d.NeedChord)
        {
            d.NeedChord = false;
            PlayPad(d, chord, (float)(holdSixteenths * d.FramesPerSixteenth / _rate) + 0.05f, frame); // held 50 ms past the change, so chords overlap rather than gap
        }

        d.Chord = chord;
        if (step == 0) PlayArrival(d, frame);

        // The FileScape theme (and its variation) from the deck's prepared notes.
        if (!intro)
        {
            while (d.NextNote < d.NoteCount && d.Notes[d.NextNote].Start <= pos)
            {
                var note = d.Notes[d.NextNote++];
                if (note.Start == pos) PlayThemeNote(d, note, step);
            }

            if (bar >= d.ThemeBars) PlayFree(d, bar, inBar, cycle, frame, chord);
        }

        switch (st.Arrangement)
        {
            case Arrangement.Festival:
                FestivalGroove(d, step, inBar, bar, intro, frame, chord);
                break;
            case Arrangement.Orchestral:
                OrchestraGroove(d, inBar, bar, intro, frame, chord);
                break;
            case Arrangement.DrumAndBass:
                DrumAndBassGroove(d, inBar, bar, intro, frame, chord);
                break;
            case Arrangement.Hazard:
                HazardGroove(d, inBar, bar, frame, chord);
                break;
            default:
                PlayGroove(d, step, inBar, bar, intro, frame, chord);
                break;
        }

        if (st.Arrangement != Arrangement.Classic && d.Echo) ProjectEcho(d, intro, bar, inBar, frame, chord);

        // Flight: a rising arpeggio through the chord, climbing with the intensity.
        if (_flightTarget > FlightArpeggioFrom)
        {
            // Root, third, fifth, then the same an octave up: a six-step climb, one note per sixteenth.
            ReadOnlySpan<int> arpBase = [0, 2, 4];
            var a = _arpCounter++ % 6;
            var arpDegree = arpBase[a % 3] + (a / 3) * len;
            var lift = _flightTarget > FlightArpeggioOctaveUpFrom ? len : 0;
            Enqueue(frame, NoteSpec.Bell(MidiHz(p.Root + 12 + ScaleNote(p.Scale, chord + arpDegree + lift)), ratio: 2f, index: 0.8f, indexDecay: 0.1f) with
            {
                Attack = 0.003f, Decay = 0.4f, Gain = 0.045f, Pan = -0.4f + 0.16f * a, Send = 0.4f, Group = GroupFlight,
            });
        }
        else
        {
            _arpCounter = 0;
        }
    }

    /// <summary>The chord of a bar between the theme's returns.</summary>
    private static int FreeChord(Deck d, int bar)
    {
        var prog = d.Identity == MusicIdentity.Hub ? HubFreeProgression(d.Profile) : d.Profile.Progression;
        var free = Math.Max(0, bar - d.ThemeBars);
        return prog[free / d.Style.FreeChordBars % prog.Length];
    }

    /// <summary>The hub between returns wanders (vi, IV, ii, V) and never cadences: the journeys are still ahead.</summary>
    private static int[] HubFreeProgression(Profile p) => p.Scale.Length == 7 ? HubWander7 : HubWander5;

    private static readonly int[] HubWander7 = [5, 3, 1, 4];
    private static readonly int[] HubWander5 = [4, 1, 2, 3];

    // ---------------------------------------------------------------- arrivals

    /// <summary>The first downbeat of a world: a resolution after the hub, and each world's own short arrival.</summary>
    private void PlayArrival(Deck d, long frame)
    {
        var p = d.Profile;
        var g = d.Group;
        var fps = d.FramesPerSixteenth;
        if (d.From == MusicIdentity.Hub && d.Identity != MusicIdentity.Hub)
        {
            // The hub's open question answered: the new world's tonic, rung once.
            foreach (var (deg, delay, pan) in (ReadOnlySpan<(int, int, float)>)[(0, 0, -0.2f), (4, 1, 0.2f), (7, 2, 0f)])
            {
                Enqueue(frame + (long)(delay * fps * 0.5), NoteSpec.Bell(MidiHz(p.Root + 12 + ScaleNote(p.Scale, deg)), ratio: 2f, index: 1.2f, indexDecay: 0.3f) with
                {
                    Attack = 0.003f, Decay = 1.6f, Gain = 0.045f, Pan = pan, Send = 0.5f, Group = g,
                });
            }
        }

        if (d.DropArrival)
        {
            // A festival build released into this world (AD-52): the impact, whatever the hop or return; a festival world
            // starts on its drop, any other world goes on to its own arrival below.
            d.DropArrival = false;
            if (d.Identity == MusicIdentity.Trails)
            {
                DropImpact(p, frame, 1f);
                Crash(frame, 1f, g);
                return;
            }

            if (d.Identity != MusicIdentity.Underglow || d.QuickHop || d.Resumed) DropImpact(p, frame, 0.7f); // drum and bass's own arrival has one
        }

        if (d.QuickHop || d.Resumed) return; // hopping through, or coming back: no fanfare
        switch (d.Identity)
        {
            case MusicIdentity.Trails:
                FestivalArrival(d, frame);
                break;
            case MusicIdentity.DeepWoods:
                OrchestraArrival(d, frame);
                break;
            case MusicIdentity.Underglow:
                DrumAndBassArrival(d, frame);
                break;
            case MusicIdentity.Hazard:
                HazardArrival(d, frame);
                break;
            case MusicIdentity.Repository:
            {
                // The arrival flourish: a crash, a 32nd-note run up the scale into the octave, then the commit stabs (intro bar).
                Crash(frame, 0.9f, g);
                for (var i = 0; i < 8; i++)
                {
                    var at = frame + (long)(i * fps * 0.5);
                    var midi = p.Root + 12 + ScaleNote(p.Scale, i);
                    Enqueue(at, LeadSpec(Lead.Digital, MidiHz(midi), 0.05f) with { Gain = 0.05f + 0.005f * i, Pan = -0.4f + 0.1f * i, Group = g });
                    MelodyObserver?.Invoke(new MelodyNote(at, midi, MelodyPart.Motif, d.Variation));
                }

                Stab(d, frame + (long)(10 * fps), p.Progression[0], 0.8f);
                Stab(d, frame + (long)(12 * fps), p.Progression[0], 1f);
                break;
            }

            case MusicIdentity.WindowsRoot:
                // A glass sweep: four chord tones up, fast and wide.
                for (var i = 0; i < 4; i++)
                {
                    var midi = p.Root + 24 + ScaleNote(p.Scale, LadderDegree(p, 0, i + 1));
                    Enqueue(frame + (long)(i * fps * 0.5), ArpSpec(ArpKind.Glass, MidiHz(midi)) with { Gain = 0.05f, Pan = -0.6f + 0.4f * i, Group = g });
                }

                break;
            case MusicIdentity.WslRoot:
                // A prompt: three quick square blips and a key press.
                for (var i = 0; i < 3; i++)
                {
                    var midi = p.Root + 24 + ScaleNote(p.Scale, 2 * i);
                    Enqueue(frame + (long)(i * fps), NoteSpec.Tone(Wave.SoftSquare, MidiHz(midi)) with { Attack = 0.002f, Decay = 0.09f, Gain = 0.04f, ToneHz = 2600, Pan = 0.15f, Send = 0.2f, Group = g });
                }

                break;
            case >= MusicIdentity.Outskirts:
                Crash(frame, 0.35f + 0.1f * (d.Identity - MusicIdentity.Outskirts), g);
                break;
        }
    }

    // ---------------------------------------------------------------- between the theme's returns

    /// <summary>One sixteenth of the world's own material between the theme's returns (bars 8–15): each identity's motif or music box, and from the second visit a counter-line.</summary>
    private void PlayFree(Deck d, int bar, int inBar, long cycle, long frame, int chord)
    {
        var free = bar - 8; // bars 8–15 of the cycle: four 2-bar phrases
        var phrase = free / 2;
        var first = free % 2 == 0 && inBar == 0;
        switch (d.Identity)
        {
            case MusicIdentity.Hub:
                MusicBox(d, inBar, frame, chord, 0.55, Lead.Bell);
                if (free is 1 or 3 or 5 && inBar == 0) Journey(d, frame, free / 2);
                break;
            case MusicIdentity.WindowsRoot:
                if (first && phrase % 2 == 0) GatewayCall(d, frame, phrase);
                else if (phrase % 2 == 1) MusicBox(d, inBar, frame, chord, 0.45, Lead.Glass);
                break;
            case MusicIdentity.WslRoot:
                if (inBar == 0) CommandLine(d, frame, bar, chord);
                break;
            case MusicIdentity.Repository:
                if (first) CommitMotif(d, frame, phrase, 1f);
                break;
            case MusicIdentity.Trails:
                FestivalFree(d, bar, first, phrase, cycle, frame);
                break;
            case MusicIdentity.DeepWoods:
                OrchestraFree(d, bar, inBar, first, phrase, cycle, frame, chord);
                break;
            case MusicIdentity.Hazard:
                HazardFree(d, bar, first, frame);
                break;
            default:
                if (first) BandMotif(d, frame, phrase, cycle);
                break;
        }

        // From the second visit: a slow counter-line of chord tones, every half bar.
        if (d.Development >= 2 && inBar % 8 == 0) CounterNote(d, frame, chord, inBar == 0 ? 1f : 0.8f);
    }

    /// <summary>The freer music box: a seeded random walk in the scale on eighth notes (chord tones on the beat).</summary>
    private void MusicBox(Deck d, int inBar, long frame, int chord, double densityScale, Lead lead)
    {
        var p = d.Profile;
        var len = p.Scale.Length;
        // A whole bar may rest; otherwise each eighth sounds by chance, more often on the beat (×1.2) than off it (×0.7).
        if (inBar == 0) d.RestBar = d.Rng.NextDouble() < p.RestChance;
        if (inBar % 2 != 0 || d.RestBar || d.Rng.NextDouble() >= p.MelodyDensity * densityScale * (inBar % 4 == 0 ? 1.2 : 0.7)) return;
        // Mostly steps, sometimes a skip or a repeat; the walk reflects off the bottom and two octaves up.
        ReadOnlySpan<int> moves = [-2, -1, -1, 0, 1, 1, 2];
        var deg = d.MelodyDegree + moves[d.Rng.Next(moves.Length)];
        var hi = 2 * len;
        if (deg < 0) deg = -deg;
        if (deg > hi) deg = hi - (deg - hi);
        if (inBar % 4 == 0)
        {
            // On the beat, nudge to the nearest chord tone (root, third or fifth of the chord), so the walk stays consonant.
            foreach (var k in (ReadOnlySpan<int>)[0, 1, -1, 2, -2])
            {
                var c = ((deg + k - chord) % len + len) % len;
                if (c is 0 or 2 or 4 && deg + k >= 0 && deg + k <= hi)
                {
                    deg += k;
                    break;
                }
            }
        }

        d.MelodyDegree = deg;
        var accent = inBar % 4 == 0 ? 1f : 0.75f + 0.2f * (float)d.Rng.NextDouble();
        var midi = p.Root + p.MelodyOctave * 12 + ScaleNote(p.Scale, deg);
        var spec = lead == Lead.Bell
            ? NoteSpec.Bell(MidiHz(midi), ratio: p.BellRatio, index: 1.3f, indexDecay: 0.25f) with { Attack = 0.003f, Decay = 1.2f, Send = 0.45f }
            : LeadSpec(lead, MidiHz(midi), 0.1f);
        Enqueue(frame, spec with { Gain = 0.05f * accent, Pan = (float)(d.Rng.NextDouble() * 0.9 - 0.45), Group = d.Group });
        MelodyObserver?.Invoke(new MelodyNote(frame, midi, MelodyPart.MusicBox, d.Variation));
    }

    /// <summary>The hub's "journeys ahead": a rising figure from a different place each time, left, centre, right; it never lands.</summary>
    private void Journey(Deck d, long frame, int which)
    {
        var p = d.Profile;
        var fps = d.FramesPerSixteenth;
        var start = which switch { 0 => 0, 1 => 2, _ => 4 };
        var pan = which switch { 0 => -0.55f, 1 => 0.55f, _ => 0f };
        ReadOnlySpan<int> rise = [0, 2, 4, 8];
        for (var i = 0; i < rise.Length; i++)
        {
            var midi = p.Root + 12 + ScaleNote(p.Scale, MapDegree(start + rise[i], p.Scale.Length));
            var at = frame + (long)(i * 2 * fps);
            Enqueue(at, NoteSpec.Bell(MidiHz(midi), ratio: 3f, index: 1.1f, indexDecay: 0.2f) with
            {
                Attack = 0.004f, Decay = i == rise.Length - 1 ? 2.2f : 0.9f, Gain = 0.04f, Pan = pan, Send = 0.55f, Group = d.Group,
            });
            MelodyObserver?.Invoke(new MelodyNote(at, midi, MelodyPart.Motif, d.Variation));
        }
    }

    /// <summary>A Windows volume's gateway call: the theme's head (mi, sol, do) in bright quarter notes, held on re above.</summary>
    private void GatewayCall(Deck d, long frame, int phrase)
    {
        var p = d.Profile;
        var fps = d.FramesPerSixteenth;
        ReadOnlySpan<(int Step, int Length, int Degree)> call = phrase == 0
            ? [(0, 4, 2), (4, 4, 4), (8, 4, 7), (12, 20, 8)]
            : [(0, 4, 2), (4, 4, 4), (8, 4, 7), (12, 4, 9), (16, 16, 7)];
        foreach (var n in call)
        {
            var midi = p.Root + 12 + ScaleNote(p.Scale, MapDegree(n.Degree, p.Scale.Length));
            var at = frame + (long)(n.Step * fps);
            Enqueue(at, LeadSpec(Lead.Glass, MidiHz(midi), (float)(n.Length * fps / _rate)) with { Gain = 0.06f, Pan = 0.1f, Group = d.Group });
            MelodyObserver?.Invoke(new MelodyNote(at, midi, MelodyPart.Motif, d.Variation));
        }
    }

    /// <summary>A WSL distribution's command line: a typed run of 16ths, the enter key, and a three-note answer.</summary>
    private void CommandLine(Deck d, long frame, int bar, int chord)
    {
        var p = d.Profile;
        var fps = d.FramesPerSixteenth;
        var count = 4 + d.Rng.Next(3);
        var deg = 2 + d.Rng.Next(3);
        for (var i = 0; i < count; i++)
        {
            var midi = p.Root + 12 + ScaleNote(p.Scale, deg);
            var at = frame + (long)(i * fps);
            Enqueue(at, LeadSpec(Lead.Square, MidiHz(midi), 0.04f) with { Gain = 0.04f * (i == 0 ? 1f : 0.8f), Pan = 0.2f, Group = d.Group });
            MelodyObserver?.Invoke(new MelodyNote(at, midi, MelodyPart.Motif, d.Variation));
            deg += d.Rng.Next(4) switch { 0 => -1, 1 => 0, _ => 1 };
        }

        Enter(frame + (long)(8 * fps), d.Group);
        if (bar % 2 == 1)
        {
            // The answer: the output scrolls down through the chord.
            ReadOnlySpan<int> answer = [7, 4, 2];
            for (var i = 0; i < answer.Length; i++)
            {
                var midi = p.Root + 12 + ScaleNote(p.Scale, chord + answer[i]);
                var at = frame + (long)((10 + 2 * i) * fps);
                Enqueue(at, LeadSpec(Lead.Square, MidiHz(midi), (float)(2 * fps / _rate)) with { Gain = 0.05f, Pan = -0.2f, Group = d.Group });
                MelodyObserver?.Invoke(new MelodyNote(at, midi, MelodyPart.Motif, d.Variation));
            }
        }
    }

    /// <summary>
    /// The repository's motif, a call and response: "da, da-da DAAA" rising to the octave, answered by two stabs ("commit");
    /// the second bar climbs further (a worktree's falls, like a branch). Phrases vary the ending and transpose the call.
    /// </summary>
    private void CommitMotif(Deck d, long frame, int phrase, float level)
    {
        var p = d.Profile;
        var fps = d.FramesPerSixteenth;
        var lift = phrase == 2 ? 2 : 0;
        ReadOnlySpan<(int Step, int Length, int Degree)> a = [(0, 2, 4), (2, 1, 4), (3, 1, 5), (4, 4, 7)];
        ReadOnlySpan<(int Step, int Length, int Degree)> b = d.Character.Worktree
            ? [(16, 2, 4), (18, 1, 4), (19, 1, 3), (20, 4, 1)]
            : phrase == 1 ? [(16, 2, 7), (18, 1, 7), (19, 1, 6), (20, 4, 4)]
            : [(16, 2, 7), (18, 1, 7), (19, 1, 8), (20, 2, 9), (22, 2, 8)];
        foreach (var n in a) MotifNote(d, StepAt(d, frame, n.Step), n.Degree + lift, n.Length, level, Lead.Digital);
        foreach (var n in b) MotifNote(d, StepAt(d, frame, n.Step), n.Degree, n.Length, level * 0.9f, Lead.Digital);
        var chordA = FreeChord(d, 8 + 2 * phrase);
        var chordB = FreeChord(d, 9 + 2 * phrase);
        Stab(d, frame + (long)(10 * fps), chordA, 0.75f * level);
        Stab(d, frame + (long)(12 * fps), chordA, level);
        Stab(d, frame + (long)((phrase == 3 ? 26 : 27) * fps), chordB, 0.75f * level);
        Stab(d, frame + (long)(28 * fps), chordB, level);
    }

    /// <summary>
    /// The band motif, developed from the theme's head (mi–sol) in a bouncy 3 + 3 + 4 rhythm: "mi sol la sol do' | la sol mi
    /// sol re", climbing on and ending open. One shape per band (as written; displaced with a rising answer; an octave down with
    /// an echo; octave leaps), varied per phrase (diminution, a sequence a step up, an open ending).
    /// </summary>
    private void BandMotif(Deck d, long frame, int phrase, long cycle)
    {
        var fps = d.FramesPerSixteenth;
        var band = d.Identity - MusicIdentity.Outskirts; // 0..3
        var v = phrase == 0 ? 0 : (int)(Mix((uint)_seed * 0x9E3779B1u ^ (uint)band * 0x85EBCA77u ^ (uint)cycle * 0xC2B2AE3Du ^ (uint)phrase * 0x27D4EB2Fu) % 4);
        Span<(int Step, int Length, int Degree)> notes = stackalloc (int, int, int)[10];
        ReadOnlySpan<(int, int, int)> motif = [(0, 3, 2), (3, 3, 4), (6, 4, 5), (10, 2, 4), (12, 4, 7), (16, 3, 5), (19, 3, 4), (22, 2, 2), (24, 2, 4), (26, 6, 1)];
        motif.CopyTo(notes);
        for (var i = 0; i < notes.Length; i++)
        {
            ref var n = ref notes[i];
            switch (band)
            {
                case 1: // the trails: the call an eighth late, the answer rising (mirrored about its first note)
                    if (i < 5)
                    {
                        n.Step += 2;
                        n.Length = Math.Min(n.Length, 16 - n.Step);
                    }
                    else
                    {
                        n.Degree = 10 - n.Degree;
                    }

                    break;
                case 2: // the deep woods: an octave down
                    n.Degree -= 7;
                    break;
                case 3: // the underglow: octave leaps
                    if (i % 2 == 1) n.Degree += 7;
                    break;
            }

            switch (v)
            {
                case 1 when i < 5: // diminution: the call twice as fast, twice
                    n.Step /= 2;
                    n.Length = Math.Max(1, n.Length / 2);
                    break;
                case 2: // a sequence: a step higher
                    n.Degree += 1;
                    break;
                case 3 when i == notes.Length - 1: // an open ending, a third higher
                    n.Degree += 2;
                    break;
            }
        }

        var lead = d.Style.Lead;
        var echo = d.Style.Arrangement == Arrangement.Orchestral ? Lead.Flute : lead; // in the orchestra the flute echoes the cellos
        foreach (var n in notes)
        {
            MotifNote(d, StepAt(d, frame, n.Step), n.Degree, n.Length, 0.85f, lead);
            if (v == 1 && n.Step < 8) MotifNote(d, StepAt(d, frame, n.Step + 8), n.Degree, n.Length, 0.6f, lead);
            if (band == 2) MotifNote(d, StepAt(d, frame, n.Step + 3), n.Degree + 7, 1, d.Style.Arrangement == Arrangement.Orchestral ? 0.5f : 0.3f, echo); // the woods' echo
        }
    }

    /// <summary>The frame <paramref name="steps"/> sixteenths after a bar line at <paramref name="barFrame"/>, swung like the groove.</summary>
    private static long StepAt(Deck d, long barFrame, int steps) =>
        barFrame + (long)((steps + (steps % 2 == 1 ? d.Style.Swing : 0f)) * d.FramesPerSixteenth);

    /// <summary>
    /// A motif note (degree in 7-note numbering) on <paramref name="lead"/>, an octave above the key's root, held 20 ms short
    /// of its written length so repeated notes re-articulate; quieter in drone and cluster worlds.
    /// </summary>
    private void MotifNote(Deck d, long at, int degree, int lengthSixteenths, float velocity, Lead lead)
    {
        var p = d.Profile;
        var midi = p.Root + 12 + ScaleNote(p.Scale, MapDegree(degree, p.Scale.Length));
        var hold = (float)(lengthSixteenths * d.FramesPerSixteenth / _rate) - 0.02f;
        if (lead >= Lead.SuperSaw)
        {
            // The AD-52 instruments (stacked, echoed or doubled: PlayLead); their worlds ignore the drone and cluster pads.
            PlayLead(d, lead, at, MidiHz(midi), MathF.Max(0.04f, hold), 0.075f * velocity * d.Style.LeadGain, -0.1f);
        }
        else
        {
            Enqueue(at, LeadSpec(lead, MidiHz(midi), MathF.Max(0.04f, hold)) with
            {
                Gain = 0.075f * velocity * d.Style.LeadGain * (p.Pad == PadKind.Normal ? 1f : 0.6f), Pan = -0.1f, Group = d.Group,
            });
        }

        MelodyObserver?.Invoke(new MelodyNote(at, midi, MelodyPart.Motif, d.Variation));
    }

    /// <summary>A revisit's counter-line: the chord tone nearest the last one, held half a bar (a soft triangle).</summary>
    private void CounterNote(Deck d, long frame, int chord, float velocity)
    {
        var p = d.Profile;
        var len = p.Scale.Length;
        var best = d.CounterDegree;
        var bestDistance = int.MaxValue;
        foreach (var k in (ReadOnlySpan<int>)[0, 2, 4])
        {
            for (var octave = -1; octave <= 1; octave++)
            {
                var c = chord + k + octave * len;
                if (c < 0 || c > len + 3 || c == d.CounterDegree) continue;
                var distance = Math.Abs(c - d.CounterDegree);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = c;
                }
            }
        }

        d.CounterDegree = best;
        var midi = p.Root + 12 + ScaleNote(p.Scale, best);
        Enqueue(frame, NoteSpec.Tone(Wave.Triangle, MidiHz(midi)) with
        {
            Attack = 0.03f, Decay = 1.2f, Sustain = 0.45f, Hold = (float)(7 * d.FramesPerSixteenth / _rate), Release = 0.5f,
            Gain = 0.035f * velocity, ToneHz = 1500, Pan = -0.35f, Send = 0.4f, Group = d.Group,
        });
        MelodyObserver?.Invoke(new MelodyNote(frame, midi, MelodyPart.Counter, d.Variation));
    }

    // ---------------------------------------------------------------- the groove

    /// <summary>
    /// One sixteenth of the world's groove from its <see cref="Style"/>: kick, snare (with a fill in the cycle's last bar
    /// and after a same-world arrival), hats, percussion, bass, arpeggio, and the repository's stabs and echo. Development
    /// adds ghost notes (second visit) and open hats and doubling (third); quiet and veiled worlds keep the kit light;
    /// odd sixteenths are late by the style's swing.
    /// </summary>
    private void PlayGroove(Deck d, long step, int inBar, int bar, bool intro, long frame, int chord)
    {
        var st = d.Style;
        var p = d.Profile;
        var g = d.Group;
        var dev = d.Development;
        var sparse = !p.Pulse; // a quiet or veiled world: the kit stays light
        var air = 1f - 0.75f * _flight; // airborne, the kick and snare thin out
        var at = inBar % 2 == 1 ? frame + (long)(st.Swing * d.FramesPerSixteenth) : frame;

        var kick = st.Kick[inBar];
        if (kick > 0 && (!sparse || dev >= 3)) Kick(at, kick * air * (sparse ? 0.6f : 1f), g);

        if (st.Snare != SnareKind.None && !sparse)
        {
            var fillStep = (!intro && bar == CycleBars - 1 && inBar >= 12) || (step >= d.FillFrom && step < d.FillFrom + 4);
            if (fillStep && st.Id != MusicIdentity.Hub) Snare(st.Snare, at, (0.4f + 0.15f * (inBar - 12)) * air, inBar % 2 == 0 ? -0.1f : 0.1f, g);
            else if (st.Back[inBar] > 0) Snare(st.Snare, at, st.Back[inBar] * air, 0, g);
            else if (dev >= 2 && st.Ghosts && inBar is 7 or 15) Snare(st.Snare, at, 0.25f * air, 0.1f, g);
        }

        var hat = st.Hat[inBar];
        if (hat == 0 && dev >= 2 && st.Ghosts && inBar % 2 == 1) hat = 0.3f;
        if (hat > 0) ClosedHat(at, hat * (sparse ? 0.6f : 1f), inBar % 4 < 2 ? -0.25f : 0.25f, g);
        var open = st.OpenHat[inBar];
        if (open == 0 && dev >= 3 && st.Ghosts && inBar % 4 == 2) open = 0.55f;
        if (open > 0) OpenHat(at, open, g);

        var perc = st.Perc[inBar];
        if (perc > 0 && dev >= st.PercFromDevelopment) Percussion(d, st.PercKind, at, perc, inBar, bar, g);

        // Bass.
        var b = st.Bass[inBar];
        if (b != '.')
        {
            var len = p.Scale.Length;
            var fifth = len == 7 ? 4 : 3;
            var semis = BassSemitones(p, chord) + char.ToUpperInvariant(b) switch
            {
                'O' => 12,
                'F' => ScaleNote(p.Scale, chord + fifth) - ScaleNote(p.Scale, chord),
                'T' => ScaleNote(p.Scale, chord + 2) - ScaleNote(p.Scale, chord),
                _ => 0,
            };
            var vel = (char.IsLower(b) ? 0.55f : inBar == 0 ? 1f : 0.85f) * (sparse ? 0.6f : 1f);
            Bass(d, st.BassKind, at, MidiHz(p.Root - 12 + semis), vel);
        }

        // The arpeggio (Windows glass, the trails' dotted plucks, the underglow's fourths); from the third visit doubled an octave up.
        var arp = st.Arp[inBar];
        if (arp >= 0)
        {
            var midi = p.Root + 12 + ScaleNote(p.Scale, LadderDegree(p, chord, arp));
            var vel = inBar % 4 == 0 ? 1f : 0.7f;
            var pan = (inBar % 2 == 0 ? -0.45f : 0.45f) * (st.ArpKind == ArpKind.Glass ? 1f : 0.6f);
            Enqueue(at, ArpSpec(st.ArpKind, MidiHz(midi)) with { Gain = ArpGain(st.ArpKind) * vel, Pan = pan, Group = g });
            if (dev >= 3) Enqueue(at, ArpSpec(st.ArpKind, MidiHz(midi + 12)) with { Gain = ArpGain(st.ArpKind) * vel * 0.4f, Pan = -pan, Group = g });
        }

        // The repository's presence: commit stabs answering the theme every second bar; inside a project, a quiet echo of it.
        if (!intro && st.Id == MusicIdentity.Repository && bar < d.ThemeBars && bar % 2 == 1 && inBar is 10 or 12)
        {
            Stab(d, frame, chord, inBar == 10 ? 0.4f : 0.5f);
        }

        if (d.Echo) ProjectEcho(d, intro, bar, inBar, frame, chord);
    }

    /// <summary>Inside a project, under any band's groove: the repository's tick-tock, its soft stabs every 4 bars and the commit call every 8.</summary>
    private void ProjectEcho(Deck d, bool intro, int bar, int inBar, long frame, int chord)
    {
        if (inBar % 2 == 0) Tick(frame, inBar % 4 == 0, 0.35f, d.Profile, d.Group);
        if (!intro && bar % 4 == 3 && inBar is 10 or 12) Stab(d, frame, chord, inBar == 10 ? 0.3f : 0.4f);
        if (!intro && bar % 8 == 7 && inBar == 0) CommitEcho(d, frame);
    }

    /// <summary>Inside a project, once every 8 bars: the commit call, high and quiet.</summary>
    private void CommitEcho(Deck d, long frame)
    {
        var fps = d.FramesPerSixteenth;
        ReadOnlySpan<(int Step, int Degree)> call = [(0, 4), (2, 4), (3, 5), (4, 7)];
        foreach (var n in call)
        {
            var p = d.Profile;
            var midi = p.Root + 24 + ScaleNote(p.Scale, MapDegree(n.Degree, p.Scale.Length));
            var at = frame + (long)(n.Step * fps);
            Enqueue(at, LeadSpec(Lead.Digital, MidiHz(midi), 0.06f) with { Gain = 0.022f, Pan = 0.45f, Send = 0.5f, Group = d.Group });
            MelodyObserver?.Invoke(new MelodyNote(at, midi, MelodyPart.Motif, d.Variation));
        }
    }

    // ---------------------------------------------------------------- the kit (all synthesised)

    // Each kit piece takes a velocity (0..1, scaling its gain) and the voice group of the deck that plays it.

    /// <summary>A kick: a sine falling 190 → 48 Hz in 60 ms, with a short noise click for the beater.</summary>
    private void Kick(long at, float vel, int g)
    {
        Enqueue(at, NoteSpec.Tone(Wave.Sine, 190f) with { GlideToHz = 48f, Glide = 0.06f, Attack = 0.001f, Decay = 0.32f, Gain = 0.2f * vel, Send = 0.02f, Group = g });
        Enqueue(at, NoteSpec.Noise(2600, 1800, 0.01f, q: 1.2f) with { Attack = 0.0005f, Decay = 0.012f, Gain = 0.04f * vel, Send = 0, Group = g });
    }

    /// <summary>The style's backbeat: a clap (band-passed noise), a rim (bright noise and a high click) or a snare (noise over a falling body tone).</summary>
    private void Snare(SnareKind kind, long at, float vel, float pan, int g)
    {
        switch (kind)
        {
            case SnareKind.Clap:
                Enqueue(at, NoteSpec.Noise(1350, 1150, 0.05f, q: 1.1f) with { Attack = 0.003f, Decay = 0.14f, Gain = 0.1f * vel, Pan = pan, Send = 0.2f, Group = g });
                break;
            case SnareKind.Rim:
                Enqueue(at, NoteSpec.Noise(2700, 2400, 0.02f, q: 3f) with { Attack = 0.0005f, Decay = 0.035f, Gain = 0.07f * vel, Pan = pan, Send = 0.12f, Group = g });
                Enqueue(at, NoteSpec.Tone(Wave.Triangle, 820f) with { Attack = 0.0005f, Decay = 0.03f, Gain = 0.03f * vel, Pan = pan, Send = 0.05f, Group = g });
                break;
            default:
                Enqueue(at, NoteSpec.Noise(1900, 1500, 0.1f, q: 0.75f) with { Attack = 0.001f, Decay = 0.17f, Gain = 0.09f * vel, Pan = pan, Send = 0.15f, Group = g });
                Enqueue(at, NoteSpec.Tone(Wave.Triangle, 190f) with { GlideToHz = 160f, Glide = 0.05f, Attack = 0.001f, Decay = 0.08f, Gain = 0.05f * vel, Pan = pan, Send = 0.05f, Group = g });
                break;
        }
    }

    /// <summary>A closed hi-hat: very short, very high band-passed noise.</summary>
    private void ClosedHat(long at, float vel, float pan, int g) =>
        Enqueue(at, NoteSpec.Noise(9200, 8600, 0.02f, q: 1.6f) with { Attack = 0.0005f, Decay = 0.035f, Gain = 0.055f * vel, Pan = pan, Send = 0.04f, Group = g });

    /// <summary>An open hi-hat: high noise with a longer tail.</summary>
    private void OpenHat(long at, float vel, int g) =>
        Enqueue(at, NoteSpec.Noise(7800, 7000, 0.15f, q: 1.3f) with { Attack = 0.001f, Decay = 0.22f, Gain = 0.04f * vel, Pan = 0.3f, Send = 0.1f, Group = g });

    /// <summary>A soft crash for arrivals: wide, high noise whose cutoff falls over 1.2 s (T60 1.5 s), well into the reverb.</summary>
    private void Crash(long at, float vel, int g) =>
        Enqueue(at, NoteSpec.Noise(7000, 5200, 1.2f, q: 0.5f) with { Attack = 0.002f, Decay = 1.5f, Gain = 0.05f * vel, Pan = 0.2f, Send = 0.35f, Group = g });

    /// <summary>The terminal's enter key: a low, muffled thock (low-passed noise and a falling sine).</summary>
    private void Enter(long at, int g)
    {
        Enqueue(at, NoteSpec.Noise(900, 500, 0.05f, q: 0.8f, lowpass: true) with { Attack = 0.001f, Decay = 0.07f, Gain = 0.06f, Pan = 0.2f, Send = 0.05f, Group = g });
        Enqueue(at, NoteSpec.Tone(Wave.Sine, 140f) with { GlideToHz = 100f, Glide = 0.04f, Attack = 0.001f, Decay = 0.06f, Gain = 0.05f, Send = 0.02f, Group = g });
    }

    /// <summary>A clockwork tick (or tock, a fourth below): two short in-key wood-block clicks panned apart.</summary>
    private void Tick(long at, bool tick, float vel, Profile p, int g) =>
        Enqueue(at, NoteSpec.Bell(MidiHz(p.Root + 36 + (tick ? 7 : 0)), ratio: 2.7f, index: 0.7f, indexDecay: 0.02f) with
        {
            Attack = 0.0005f, Decay = 0.05f, Gain = 0.035f * vel, Pan = tick ? -0.45f : 0.45f, Send = 0.08f, Group = g,
        });

    /// <summary>The style's extra percussion voice (<see cref="PercKind"/>): shaker, keystrokes, clockwork or toms.</summary>
    private void Percussion(Deck d, PercKind kind, long at, float vel, int inBar, int bar, int g)
    {
        var p = d.Profile;
        switch (kind)
        {
            case PercKind.Shaker:
                Enqueue(at, NoteSpec.Noise(6500, 6000, 0.03f, q: 1.2f) with { Attack = 0.006f, Decay = 0.06f, Gain = 0.03f * vel, Pan = inBar % 4 < 2 ? -0.3f : 0.3f, Send = 0.08f, Group = g });
                break;
            case PercKind.Typing:
            {
                // Keystrokes: each a slightly different click; an enter at the end of every second bar.
                var h = Mix((uint)(inBar + 16 * (bar & 3)) * 0x9E3779B1u ^ (uint)_seed);
                var cut = 2600f + (h % 1600);
                Enqueue(at, NoteSpec.Noise(cut, cut * 0.9f, 0.01f, q: 5f) with { Attack = 0.0005f, Decay = 0.018f, Gain = 0.06f * vel, Pan = ((h >> 8) % 60) / 100f - 0.3f, Send = 0.05f, Group = g });
                if (inBar == 15 && bar % 2 == 1) Enter(at, g);
                break;
            }

            case PercKind.Clockwork:
                Tick(at, inBar % 4 == 0, vel, p, g);
                break;
            case PercKind.Toms:
            {
                var hi = inBar % 3 == 0;
                var hz = MidiHz(p.Root - 12 + (hi ? 19 : 12));
                Enqueue(at, NoteSpec.Tone(Wave.Sine, hz) with { GlideToHz = hz * 0.72f, Glide = 0.14f, Attack = 0.001f, Decay = 0.35f, Gain = 0.08f * vel, Pan = hi ? 0.3f : -0.3f, Send = 0.15f, Group = g });
                break;
            }
        }
    }

    /// <summary>A bass note at <paramref name="hz"/> in the style's bass sound (<see cref="BassKind"/>).</summary>
    private void Bass(Deck d, BassKind kind, long at, float hz, float vel)
    {
        var g = d.Group;
        switch (kind)
        {
            case BassKind.Sub:
                Enqueue(at, NoteSpec.Tone(Wave.Sine, hz) with
                {
                    Attack = 0.03f, Decay = 2f, Sustain = 0.6f, Hold = (float)(7 * d.FramesPerSixteenth / _rate), Release = 0.5f, Gain = 0.05f * vel, Send = 0.1f, Group = g,
                });
                break;
            case BassKind.Square:
                Enqueue(at, NoteSpec.Tone(Wave.SoftSquare, hz) with { Attack = 0.003f, Decay = 0.28f, Gain = 0.085f * vel, ToneHz = 700, Send = 0.06f, Group = g });
                break;
            case BassKind.Fm:
                Enqueue(at, NoteSpec.Bell(hz, ratio: 1f, index: 2.2f, indexDecay: 0.06f) with { Attack = 0.002f, Decay = 0.24f, Gain = 0.075f * vel, ToneHz = 1400, Send = 0.05f, Group = g });
                break;
            case BassKind.Deep:
                Enqueue(at, NoteSpec.Tone(Wave.Triangle, hz * 1.02f) with { GlideToHz = hz, Glide = 0.03f, Attack = 0.003f, Decay = 0.5f, Gain = 0.1f * vel, ToneHz = 380, Send = 0.06f, Group = g });
                break;
            default:
                Enqueue(at, NoteSpec.Tone(Wave.Triangle, hz) with { Attack = 0.003f, Decay = 0.35f, Gain = 0.095f * vel, ToneHz = 650, Send = 0.06f, Group = g });
                break;
        }
    }

    /// <summary>A "commit" stab: the chord, short and square, with a click.</summary>
    private void Stab(Deck d, long at, int chord, float vel)
    {
        var p = d.Profile;
        foreach (var (k, pan) in (ReadOnlySpan<(int, float)>)[(0, -0.25f), (2, 0f), (4, 0.25f)])
        {
            Enqueue(at, NoteSpec.Tone(Wave.SoftSquare, MidiHz(p.Root + ScaleNote(p.Scale, chord + k))) with
            {
                Attack = 0.002f, Decay = 0.12f, Gain = 0.035f * vel, ToneHz = 1700, Pan = pan, Send = 0.15f, Group = d.Group,
            });
        }

        Enqueue(at, NoteSpec.Noise(3000, 2500, 0.01f, q: 2f) with { Attack = 0.0005f, Decay = 0.015f, Gain = 0.035f * vel, Send = 0.05f, Group = d.Group });
    }

    /// <summary>A chord tone on the ladder above <paramref name="chord"/>: 0 root, 1 third, 2 fifth, 3 octave, …</summary>
    private static int LadderDegree(Profile p, int chord, int rung) => chord + rung / 3 * p.Scale.Length + (rung % 3) * 2;

    /// <summary>An arpeggio note's sound (gain from <see cref="ArpGain"/>, pan and group are the caller's).</summary>
    private static NoteSpec ArpSpec(ArpKind kind, float hz) => kind switch
    {
        ArpKind.Glass => NoteSpec.Bell(hz, ratio: 3.5f, index: 1.4f, indexDecay: 0.06f) with { Attack = 0.002f, Decay = 0.35f, Send = 0.45f, ToneHz = 8000 },
        ArpKind.Pluck => NoteSpec.Tone(Wave.SoftSquare, hz) with { Attack = 0.002f, Decay = 0.3f, Send = 0.45f, ToneHz = 2200 },
        _ => NoteSpec.Bell(hz, ratio: 2f, index: 0.9f, indexDecay: 0.08f) with { Attack = 0.002f, Decay = 0.3f, Send = 0.5f, ToneHz = 7000 },
    };

    /// <summary>Each arpeggio sound's level, balanced by ear against the lead (rationale for the exact values not recorded).</summary>
    private static float ArpGain(ArpKind kind) => kind switch { ArpKind.Glass => 0.04f, ArpKind.Pluck => 0.032f, _ => 0.024f };

    /// <summary>A lead instrument's note held for <paramref name="holdSeconds"/> (gain, pan and group are the caller's).</summary>
    private static NoteSpec LeadSpec(Lead lead, float hz, float holdSeconds) => lead switch
    {
        Lead.Glass => NoteSpec.Bell(hz, ratio: 3.5f, index: 1.8f, indexDecay: 0.12f) with { Attack = 0.002f, Decay = 0.9f, Sustain = 0.25f, Hold = holdSeconds, Release = 0.5f, Send = 0.45f, ToneHz = 7000 },
        Lead.Square => NoteSpec.Tone(Wave.SoftSquare, hz) with { Attack = 0.004f, Decay = 0.5f, Sustain = 0.45f, Hold = holdSeconds, Release = 0.12f, Send = 0.22f, ToneHz = 2400 },
        Lead.Digital => NoteSpec.Bell(hz, ratio: 1f, index: 1.3f, indexDecay: 0.25f) with { Attack = 0.003f, Decay = 0.6f, Sustain = 0.4f, Hold = holdSeconds, Release = 0.15f, Send = 0.25f, ToneHz = 4000 },
        Lead.Marimba => NoteSpec.Bell(hz, ratio: 4f, index: 0.9f, indexDecay: 0.04f) with { Attack = 0.002f, Decay = 0.6f, Sustain = 0f, Hold = holdSeconds, Release = 0.3f, Send = 0.3f, ToneHz = 6000 },
        Lead.Pluck => NoteSpec.Tone(Wave.SoftSquare, hz) with { Attack = 0.003f, Decay = 0.4f, Sustain = 0.3f, Hold = holdSeconds, Release = 0.15f, Send = 0.3f, ToneHz = 3000 },
        Lead.Wood => NoteSpec.Bell(hz, ratio: 2.76f, index: 1.1f, indexDecay: 0.05f) with { Attack = 0.002f, Decay = 0.7f, Sustain = 0.1f, Hold = holdSeconds, Release = 0.3f, Send = 0.35f, ToneHz = 4000 },
        Lead.Glow => NoteSpec.Bell(hz, ratio: 2f, index: 1.2f, indexDecay: 0.5f) with { Attack = 0.008f, Decay = 1.0f, Sustain = 0.3f, Hold = holdSeconds, Release = 0.5f, Send = 0.55f, ToneHz = 6000 },
        Lead.SuperSaw => NoteSpec.Saw(hz, 3, 14f, 4200f) with { Attack = 0.006f, Decay = 0.6f, Sustain = 0.55f, Hold = holdSeconds, Release = 0.25f, Send = 0.35f },
        Lead.Flute => NoteSpec.Tone(Wave.Sine, hz) with { Attack = 0.06f, Decay = 1.5f, Sustain = 0.8f, Hold = holdSeconds, Release = 0.25f, Send = 0.6f, ToneHz = 3000, VibratoHz = 5f, VibratoDepth = 0.003f },
        Lead.Oboe => NoteSpec.Bell(hz, ratio: 1f, index: 0.9f, indexDecay: 0f) with { Attack = 0.04f, Decay = 1.5f, Sustain = 0.8f, Hold = holdSeconds, Release = 0.2f, Send = 0.55f, ToneHz = 3200, VibratoHz = 5.5f, VibratoDepth = 0.0025f },
        Lead.Clarinet => NoteSpec.Tone(Wave.SoftSquare, hz) with { Attack = 0.05f, Decay = 1.5f, Sustain = 0.8f, Hold = holdSeconds, Release = 0.2f, Send = 0.55f, ToneHz = 1900, VibratoHz = 4.5f, VibratoDepth = 0.002f },
        Lead.Horn => NoteSpec.Saw(hz, 2, 4f, 380f) with { ToneToHz = 1200f, ToneGlide = 0.35f, Attack = 0.1f, Decay = 1.5f, Sustain = 0.85f, Hold = holdSeconds, Release = 0.45f, Send = 0.7f, VibratoHz = 4.5f, VibratoDepth = 0.0015f },
        Lead.Violins => NoteSpec.Saw(hz, 3, 8f, 2400f) with { Attack = 0.07f, Decay = 1.5f, Sustain = 0.85f, Hold = holdSeconds, Release = 0.4f, Send = 0.6f, VibratoHz = 5.5f, VibratoDepth = 0.003f },
        Lead.Cello => NoteSpec.Saw(hz, 3, 7f, 1700f) with { Attack = 0.08f, Decay = 1.5f, Sustain = 0.85f, Hold = holdSeconds, Release = 0.45f, Send = 0.55f, VibratoHz = 5f, VibratoDepth = 0.003f },
        Lead.Liquid => NoteSpec.Bell(hz, ratio: 1f, index: 1.6f, indexDecay: 0.4f) with { Attack = 0.004f, Decay = 1.2f, Sustain = 0.35f, Hold = holdSeconds, Release = 0.6f, Send = 0.5f, ToneHz = 5000, VibratoHz = 4.5f, VibratoDepth = 0.002f },
        Lead.Unstable => NoteSpec.Bell(hz, ratio: 1f, index: 1.1f, indexDecay: 0.8f) with { Attack = 0.02f, Decay = 1.4f, Sustain = 0.4f, Hold = holdSeconds, Release = 0.7f, Send = 0.6f, ToneHz = 3500, VibratoHz = 0.7f, VibratoDepth = 0.006f },
        _ => NoteSpec.Bell(hz, ratio: 2f, index: 1.5f, indexDecay: 0.35f) with { Attack = 0.004f, Decay = 0.8f, Sustain = 0.32f, Hold = holdSeconds, Release = 0.4f, Send = 0.35f, ToneHz = 5200 },
    };

    // ---------------------------------------------------------------- pad and theme

    /// <summary>A pad chord (three soft triangles, a drone or a cluster; the AD-52 arrangements their own), held for <paramref name="holdSeconds"/>.</summary>
    private void PlayPad(Deck d, int chord, float holdSeconds, long frame)
    {
        switch (d.Style.Arrangement)
        {
            case Arrangement.Festival:
                FestivalPad(d, chord, holdSeconds, frame);
                return;
            case Arrangement.Orchestral:
                OrchestraPad(d, chord, holdSeconds, frame);
                return;
            case Arrangement.DrumAndBass:
                DrumAndBassPad(d, chord, holdSeconds, frame);
                return;
            case Arrangement.Hazard:
                HazardPad(d, chord, holdSeconds, frame);
                return;
        }

        var p = d.Profile;
        var st = d.Style;
        var group = d.Group;
        ReadOnlySpan<int> offsets = p.Pad == PadKind.Cluster ? [0, 1, 2, 4] : p.Pad == PadKind.Drone ? [0, 2] : [0, 2, 4];
        var normal = p.Pad == PadKind.Normal;
        var attack = normal ? st.PadAttack : 2.4f;
        var release = normal ? 1.0f : 1.8f;
        for (var i = 0; i < offsets.Length; i++)
        {
            var pan = -0.4f + 0.8f * i / (offsets.Length - 1);
            var midi = p.Root + ScaleNote(p.Scale, chord + offsets[i]) - (p.Pad == PadKind.Drone ? 12 : 0);
            Enqueue(frame, NoteSpec.Tone(p.Pad == PadKind.Drone ? Wave.Sine : Wave.Triangle, MidiHz(midi)) with
            {
                Attack = attack, Decay = 2f, Sustain = 0.75f, Hold = holdSeconds, Release = release, Gain = p.PadGain * (normal ? 0.7f : 1f) * st.PadGain,
                ToneHz = p.PadToneHz * st.PadTone, Pan = pan, Send = 0.5f, Group = group,
            });
        }

        if (!p.Pulse)
        {
            // Without a plucked bass, a soft sustained root carries the low end.
            Enqueue(frame, NoteSpec.Tone(Wave.Sine, MidiHz(p.Root - 12 + BassSemitones(p, chord))) with
            {
                Attack = attack, Decay = 2f, Sustain = 0.7f, Hold = holdSeconds, Release = release, Gain = 0.05f, Send = 0.2f, Group = group,
            });
        }

        if (p.Pad == PadKind.Cluster)
        {
            Enqueue(frame, NoteSpec.Noise(520, 520, 0, q: 0.7f, lowpass: true) with
            {
                Attack = 2.5f, Decay = 2f, Sustain = 0.8f, Hold = holdSeconds, Release = 2f, Gain = 0.025f, Send = 0.6f, Group = group,
            });
        }
    }

    /// <summary>The chord's root in semitones above the key's root, folded into −5..+7 so the bass stays low and close.</summary>
    private static int BassSemitones(Profile p, int chord)
    {
        var len = p.Scale.Length;
        var semis = p.Scale[((chord % len) + len) % len];
        return semis > 7 ? semis - 12 : semis;
    }

    /// <summary>
    /// Plays a prepared note of the theme an octave above the key's root: a theme note on the world's lead (with a short
    /// glide from a legato neighbour within a fourth), a grace note on a small bell, or a counter-line note on a soft triangle.
    /// </summary>
    private void PlayThemeNote(Deck d, in LeadNote note, long step)
    {
        var p = d.Profile;
        var st = d.Style;
        var group = d.Group;
        var midi = p.Root + 12 + ScaleNote(p.Scale, note.Degree);
        var hz = MidiHz(midi);
        var sixteenth = (float)(d.FramesPerSixteenth / _rate);
        var at = Math.Max(_frame, StepFrame(d, step) + (long)Math.Round(note.Delay * d.FramesPerSixteenth));
        var themeGain = (p.Pad == PadKind.Normal ? 1f : 0.55f) * st.LeadGain;
        switch (note.Part)
        {
            case MelodyPart.Theme:
            {
                // A little glide from the previous note when the line is legato and the step is small.
                var glide = d.LastLeadEnd == step && midi != d.LastLeadMidi && Math.Abs(midi - d.LastLeadMidi) <= 5;
                var from = glide ? MidiHz(d.LastLeadMidi) : hz;
                if (st.Arrangement != Arrangement.Classic)
                {
                    ArrangedThemeNote(d, note, at, midi, glide ? from : 0f, MathF.Max(0.05f, note.Length * sixteenth - 0.02f), 0.085f * note.Velocity * st.LeadGain);
                    d.LastLeadEnd = step + note.Length;
                    d.LastLeadMidi = midi;
                    break;
                }

                Enqueue(at, LeadSpec(st.Lead, from, MathF.Max(0.05f, note.Length * sixteenth - 0.02f)) with
                {
                    GlideToHz = glide ? hz : 0, Glide = 0.035f, Gain = 0.085f * note.Velocity * themeGain, Pan = 0.05f, Group = group,
                });
                if (d.Development >= 3 && note.Length >= 4)
                {
                    // A third visit: the long notes doubled an octave below, softly.
                    Enqueue(at, NoteSpec.Tone(Wave.Triangle, hz * 0.5f) with
                    {
                        Attack = 0.01f, Decay = 0.8f, Sustain = 0.3f, Hold = MathF.Max(0.05f, note.Length * sixteenth - 0.05f), Release = 0.3f,
                        Gain = 0.03f * note.Velocity, ToneHz = 1600, Pan = -0.2f, Send = 0.3f, Group = group,
                    });
                }

                d.LastLeadEnd = step + note.Length;
                d.LastLeadMidi = midi;
                break;
            }

            case MelodyPart.Grace:
                Enqueue(at, NoteSpec.Bell(hz, ratio: 2f, index: 1f, indexDecay: 0.1f) with
                {
                    Attack = 0.002f, Decay = 0.18f, Gain = 0.05f * note.Velocity * themeGain, Pan = 0.05f, Send = 0.3f, ToneHz = 5200, Group = group,
                });
                break;
            default:
                Enqueue(at, NoteSpec.Tone(Wave.Triangle, hz) with
                {
                    Attack = 0.03f, Decay = 1.2f, Sustain = 0.45f, Hold = MathF.Max(0.05f, note.Length * sixteenth - 0.05f), Release = 0.6f,
                    Gain = 0.04f * note.Velocity * themeGain, ToneHz = 1500, Pan = -0.3f, Send = 0.4f, Group = group,
                });
                break;
        }

        MelodyObserver?.Invoke(new MelodyNote(at, midi, note.Part, d.Variation));
    }

    // ---------------------------------------------------------------- the FileScape theme

    /// <summary>
    /// Prepares cycle <paramref name="cycle"/>'s theme notes in the deck's key: the written theme mapped into the scale,
    /// then transformed by the cycle's variation (and the world: the hub's open question, the terminal's re-struck notes).
    /// Allocation-free (the deck's note array is reused).
    /// </summary>
    private void BuildCycle(Deck deck, long cycle)
    {
        var p = deck.Profile;
        var len = p.Scale.Length;
        var hub = deck.Identity == MusicIdentity.Hub;
        var variation = VariationFor(deck, cycle);
        deck.Variation = variation;
        var notes = deck.Notes;
        var n = 0;
        var theme = ThemeNotes;
        // (Sixteenth 96 is bar 7, the cadence; 32–63 are bars 3–4, the answer.)
        var graceDelay = 1f - 0.05f / (float)(deck.FramesPerSixteenth / _rate); // a grace note sounds 50 ms before its note
        var pivot = int.MinValue;
        var previous = int.MinValue;
        for (var i = 0; i < theme.Length; i++)
        {
            var t = theme[i];
            if (hub && t.StartSixteenths >= 96) break; // the hub asks its own question instead of the cadence (below)
            var d = MapDegree(t.Degree, len);
            var next = i + 1 < theme.Length ? MapDegree(theme[i + 1].Degree, len) : d;
            int start = t.StartSixteenths, length = t.LengthSixteenths;
            var velocity = start % 16 == 0 || length >= 6 ? 1f : start % 4 == 0 ? 0.9f : 0.8f; // downbeats and long notes lead, then beats, then the rest
            switch (variation)
            {
                case ThemeVariation.OctaveCounter:
                    d += len;
                    next += len;
                    break;
                case ThemeVariation.InvertedAnswer when start is >= 32 and < 64:
                    if (pivot == int.MinValue) pivot = d;
                    d = 2 * pivot - d;
                    next = start + length < 64 ? 2 * pivot - next : next;
                    break;
                case ThemeVariation.AugmentedEnding when start >= 96:
                    start = 96 + 2 * (start - 96);
                    length *= 2;
                    break;
            }

            if (variation == ThemeVariation.Ornamented)
            {
                if (previous != int.MinValue && d - previous >= 3) notes[n++] = new LeadNote(start - 1, 1, d - 1, 0.55f, graceDelay, MelodyPart.Grace);
                previous = d;
                if (i + 1 < theme.Length && length >= 6)
                {
                    // A turn on the long notes: the note, its upper neighbour, the note again.
                    notes[n++] = new LeadNote(start, 3, d, velocity, 0, MelodyPart.Theme);
                    notes[n++] = new LeadNote(start + 3, 1, d + 1, 0.6f, 0, MelodyPart.Theme);
                    notes[n++] = new LeadNote(start + 4, length - 4, d, 0.75f, 0, MelodyPart.Theme);
                    continue;
                }

                if (i + 1 < theme.Length && length >= 4)
                {
                    // A passing note into a third, otherwise a neighbour note away from the next one.
                    var diff = next - d;
                    var ornament = Math.Abs(diff) == 2 ? d + Math.Sign(diff) : diff > 0 ? d - 1 : d + 1;
                    notes[n++] = new LeadNote(start, length - 2, d, velocity, 0, MelodyPart.Theme);
                    notes[n++] = new LeadNote(start + length - 2, 2, ornament, 0.7f, 0, MelodyPart.Theme);
                    continue;
                }
            }

            if (deck.Identity == MusicIdentity.WslRoot && length >= 6)
            {
                // The terminal re-strikes its long notes like a held key repeating.
                notes[n++] = new LeadNote(start, 2, d, velocity, 0, MelodyPart.Theme);
                for (var r = 2; r + 2 <= length; r += 2) notes[n++] = new LeadNote(start + r, 2, d, 0.5f, 0, MelodyPart.Theme);
                continue;
            }

            notes[n++] = new LeadNote(start, length, d, velocity, 0, MelodyPart.Theme);
        }

        if (hub)
        {
            // The question: la sol la do' — re', held over V (sus), never resolved here.
            var shift = variation == ThemeVariation.OctaveCounter ? len : 0;
            ReadOnlySpan<(int Start, int Length, int Degree)> question = [(96, 2, 5), (98, 2, 4), (100, 4, 5), (104, 8, 7), (112, 16, 8)];
            foreach (var q in question) notes[n++] = new LeadNote(q.Start, q.Length, MapDegree(q.Degree, len) + shift, q.Start == 112 ? 0.9f : 0.85f, 0, MelodyPart.Theme);
        }

        if (variation == ThemeVariation.Syncopated)
        {
            // Anticipate a beat where the note before can give up an eighth, otherwise delay the note by one.
            for (var i = 1; i < n; i++)
            {
                ref var cur = ref notes[i];
                ref var prev = ref notes[i - 1];
                if (cur.Start % 4 != 0 || prev.Start + prev.Length != cur.Start) continue;
                if (prev.Length >= 4)
                {
                    prev.Length -= 2;
                    cur.Start -= 2;
                    cur.Length += 2;
                }
                else if (cur.Length >= 4)
                {
                    prev.Length += 2;
                    cur.Start += 2;
                    cur.Length -= 2;
                }
            }
        }

        if (variation == ThemeVariation.OctaveCounter)
        {
            // A slow counter-line of chord tones an octave below the (raised) theme, moving every half bar.
            var counter = 2;
            for (var b = 0; b < ThemeLengthBars; b++)
            {
                var root = p.Pad == PadKind.Normal ? ThemeChordAt(p, b, hub) : p.Progression[0];
                var halves = b == ThemeLengthBars - 1 ? 1 : 2;
                for (var h = 0; h < halves; h++)
                {
                    var best = counter;
                    var bestDistance = int.MaxValue;
                    foreach (var k in (ReadOnlySpan<int>)[0, 2, 4])
                    {
                        for (var octave = -1; octave <= 1; octave++)
                        {
                            var c = root + k + octave * len;
                            if (c < -2 || c > len + 3 || (h == 1 && c == counter)) continue;
                            var distance = Math.Abs(c - counter);
                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                best = c;
                            }
                        }
                    }

                    counter = best;
                    notes[n++] = new LeadNote(b * 16 + h * 8, halves == 1 ? 16 : 8, counter, 0.8f, 0, MelodyPart.Counter);
                }
            }
        }

        // Order by onset (insertion sort: a few dozen notes, once per 16 bars).
        for (var i = 1; i < n; i++)
        {
            var x = notes[i];
            var j = i - 1;
            while (j >= 0 && notes[j].Start + notes[j].Delay > x.Start + x.Delay)
            {
                notes[j + 1] = notes[j];
                j--;
            }

            notes[j + 1] = x;
        }

        var end = 0;
        for (var i = 0; i < n; i++)
        {
            if (notes[i].Part == MelodyPart.Theme) end = Math.Max(end, notes[i].Start + notes[i].Length);
        }

        deck.NoteCount = n;
        deck.NextNote = 0;
        // The theme's bars, rounded up to whole bars (9 for an augmented ending); at most 12, so the world's own material keeps 4 bars.
        deck.ThemeBars = Math.Clamp((end + 15) / 16, ThemeLengthBars, CycleBars - 4);
        deck.LastLeadEnd = long.MinValue;
    }

    /// <summary>
    /// The variation of a world's cycle: plain on cycles 0, 4, 8, …; otherwise a seeded choice per seed, identity, theme and
    /// cycle that never repeats the last one. The hub keeps its gentle ones (its cadence is its question).
    /// </summary>
    private ThemeVariation VariationFor(Deck d, long cycle)
    {
        if (cycle % 4 == 0) return ThemeVariation.Plain;
        // 1 + h % 5 picks one of the five non-plain variations (ThemeVariation's values 1–5); a repeat moves on to the next.
        var h = Mix((uint)_seed * 0x9E3779B1u ^ (uint)((int)d.Theme + 1) * 0x85EBCA77u ^ (uint)cycle * 0xC2B2AE3Du ^ (uint)d.Identity * 0x27D4EB2Fu);
        var v = 1 + (int)(h % 5);
        if ((ThemeVariation)v == d.Variation) v = 1 + v % 5;
        var variation = (ThemeVariation)v;
        if (d.Identity == MusicIdentity.Hub && variation is ThemeVariation.AugmentedEnding) variation = d.Variation == ThemeVariation.Ornamented ? ThemeVariation.OctaveCounter : ThemeVariation.Ornamented;
        return variation;
    }

    /// <summary>A theme degree (7-note numbering) in a scale of <paramref name="len"/> notes: identity for 7, la/sol/mi/re/do for pentatonics.</summary>
    private static int MapDegree(int degree, int len)
    {
        if (len == 7) return degree;
        var octave = (int)Math.Floor(degree / 7.0);
        var r = degree - 7 * octave;
        ReadOnlySpan<int> pentatonic = [0, 1, 2, 2, 3, 4, 4]; // do re mi (fa→mi) sol la (ti→la): the missing steps fold down
        return (len == 5 ? pentatonic[r] : (int)Math.Round(r * len / 7.0)) + len * octave;
    }

    /// <summary>
    /// The chord root under bar <paramref name="bar"/> of the theme, per mode (avoiding the mode's diminished triad). In the
    /// hub the last two bars move IV → V (a sus over the pentatonic), under its open question.
    /// </summary>
    private static int ThemeChordAt(Profile p, int bar, bool hub = false)
    {
        if (bar < 0 || bar >= ThemeLengthBars) return 0; // an augmented ending's extra bar rests on the tonic
        var five = p.Scale.Length == 5;
        if (hub && bar >= 6) return bar == 6 ? (five ? 4 : 3) : (five ? 3 : 4);
        ReadOnlySpan<int> chords = five ? [0, 4, 0, 3, 1, 0, 3, 0]
            : ReferenceEquals(p.Scale, Lydian) || ReferenceEquals(p.Scale, LydianDominant) ? [0, 5, 0, 4, 1, 0, 4, 0]
            : ReferenceEquals(p.Scale, Dorian) ? [0, 3, 0, 4, 6, 0, 4, 0]
            : ReferenceEquals(p.Scale, Phrygian) ? [0, 5, 0, 3, 1, 0, 1, 0] // i VI i iv bII i, and the phrygian cadence bII → i (its v is diminished)
            : [0, 5, 0, 4, 3, 0, 4, 0];
        return chords[bar];
    }

    /// <summary>
    /// A 32-bit integer finaliser (the "lowbias32" xor-shift-multiply constants): each input bit flips about half the
    /// output bits, so the hashed seeds above (golden-ratio and xxHash multipliers, xored) give unrelated choices for neighbouring
    /// inputs. Changing it changes every seeded choice in the music.
    /// </summary>
    private static uint Mix(uint h)
    {
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }

    /// <summary>The written FileScape theme as <see cref="ThemeNote"/>s (each bar's notes fill its 16 sixteenths). Changing it changes the music's identity everywhere.</summary>
    private static ThemeNote[] BuildTheme()
    {
        // (degree, sixteenths) per bar: motif (bars 1–2), answer (3–4), the motif raised (5–6), the cadence (7–8).
        (int Degree, int Length)[][] bars =
        [
            [(2, 2), (4, 2), (7, 6), (5, 2), (4, 4)],
            [(5, 2), (4, 2), (2, 2), (1, 2), (2, 8)],
            [(2, 2), (4, 2), (7, 6), (8, 2), (9, 4)],
            [(8, 2), (7, 2), (5, 4), (4, 8)],
            [(4, 2), (5, 2), (9, 6), (8, 2), (7, 4)],
            [(5, 2), (7, 2), (4, 2), (2, 2), (4, 8)],
            [(5, 2), (4, 2), (2, 2), (4, 2), (1, 6), (2, 2)],
            [(0, 8)],
        ];
        var notes = new List<ThemeNote>();
        for (var b = 0; b < bars.Length; b++)
        {
            var at = b * 16;
            foreach (var (degree, length) in bars[b])
            {
                notes.Add(new ThemeNote(at, length, degree));
                at += length;
            }
        }

        return [.. notes];
    }

    // ---------------------------------------------------------------- identities (the scores)

    /// <summary>A lead instrument for the theme and motifs (its sound in <see cref="LeadSpec"/>): one per identity, in <see cref="MusicIdentity"/> order.</summary>
    private enum Lead
    {
        Bell,
        Glass,
        Square,
        Digital,
        Marimba,
        Pluck,
        Wood,
        Glow,

        // AD-52's instruments (PlayLead stacks, doubles or echoes them; LeadSpec gives each voice).

        /// <summary>The festival's supersaw: two three-saw stacks spread left and right, with a bell an octave up for its attack.</summary>
        SuperSaw,

        /// <summary>The orchestra's flute (a sine with breath and vibrato).</summary>
        Flute,

        /// <summary>The orchestra's oboe (steady-index FM: reedy).</summary>
        Oboe,

        /// <summary>The orchestra's clarinet (odd harmonics: the soft square, filtered).</summary>
        Clarinet,

        /// <summary>The orchestra's horns (paired saws whose filter opens as the note swells).</summary>
        Horn,

        /// <summary>The orchestra's first violins (a three-saw section with vibrato).</summary>
        Violins,

        /// <summary>The orchestra's cellos (a darker, lower section).</summary>
        Cello,

        /// <summary>Drum and bass's glowing lead (a soft FM electric piano) with a dotted echo.</summary>
        Liquid,

        /// <summary>The hazard world's lead: two slightly detuned, slowly wobbling bells whose long notes sag.</summary>
        Unstable,
    }

    /// <summary>A style's backbeat sound (<see cref="Snare"/>), or none (the hub has no kit).</summary>
    private enum SnareKind
    {
        None,
        Snare,
        Clap,
        Rim,
    }

    /// <summary>A style's extra percussion (<see cref="Percussion"/>), the part of the groove that says which world this is.</summary>
    private enum PercKind
    {
        None,
        Shaker,
        Typing,
        Clockwork,
        Toms,
    }

    /// <summary>A style's bass sound (<see cref="Bass"/>).</summary>
    private enum BassKind
    {
        Sub,
        Pluck,
        Square,
        Fm,
        Deep,
    }

    /// <summary>A style's arpeggio sound (<see cref="ArpSpec"/>), or none.</summary>
    private enum ArpKind
    {
        None,
        Glass,
        Pluck,
        Glow,
    }

    /// <summary>
    /// A world identity's score: tempo, swing (a fraction of a sixteenth by which the off-sixteenths are late), a mode of its
    /// own (or the theme's), its lead, and one bar of each drum, bass and arpeggio pattern (16 steps).
    /// <para>
    /// Patterns: drums are <see cref="Hits"/> (Kick, Back = the backbeat snare, Hat, OpenHat, Perc), the arpeggio is
    /// <see cref="Rungs"/>, and the bass is a string of R root, O octave, F fifth, T third, . rest (lower case: softer).
    /// Ghosts enables the development extras (ghost snares and hats from the second visit, open hats from the third); PercFromDevelopment is the visit from which Perc plays.
    /// PadGain and PadTone scale the key's pad level and tone; PadAttack is seconds; FreeChordBars is how many bars each
    /// chord lasts between the theme's returns.
    /// </para>
    /// <para>
    /// AD-52: Arrangement names an arrangement with its own groove, pads and lead (SkySynth.Arrangements.cs), which then
    /// ignores the patterns above; HalfTime runs the score at half the pulse (<see cref="ThemeTempoFor"/>); PumpDepth (0..1)
    /// is how far the deck's pumped voices dip on each kick (0.6 ≈ −8 dB); Level is the deck's gain, which balances an
    /// arrangement's loudness with the others (measured, AD-52).
    /// </para>
    /// </summary>
    private sealed record Style(
        MusicIdentity Id, float Bpm, float Swing, int[]? Scale, int[]? Progression, string? Mode, Lead Lead, float LeadGain,
        float[] Kick, SnareKind Snare, float[] Back, float[] Hat, float[] OpenHat, bool Ghosts,
        PercKind PercKind, float[] Perc, int PercFromDevelopment, string Bass, BassKind BassKind, ArpKind ArpKind, int[] Arp,
        float PadGain, float PadTone, float PadAttack, int FreeChordBars,
        Arrangement Arrangement = Arrangement.Classic, bool HalfTime = false, float PumpDepth = 0f, float Level = 1f);

    /// <summary>A drum pattern: X accent, x normal, o ghost, . rest.</summary>
    private static float[] Hits(string s) => [.. s.Select(c => c switch { 'X' => 1f, 'x' => 0.75f, 'o' => 0.4f, _ => 0f })];

    /// <summary>An arpeggio pattern: a digit is a rung of the chord-tone ladder (0 root, 1 third, 2 fifth, 3 octave…), . a rest.</summary>
    private static int[] Rungs(string s) => [.. s.Select(c => c is >= '0' and <= '9' ? c - '0' : -1)];

    private const string Silent = "................";

    // The scales come first: the static tables below use them (static fields initialise in textual order).
    private static readonly int[] Major = [0, 2, 4, 5, 7, 9, 11];
    private static readonly int[] Lydian = [0, 2, 4, 6, 7, 9, 11];
    private static readonly int[] LydianDominant = [0, 2, 4, 6, 7, 9, 10];
    private static readonly int[] Mixolydian = [0, 2, 4, 5, 7, 9, 10];
    private static readonly int[] Dorian = [0, 2, 3, 5, 7, 9, 10];
    private static readonly int[] Phrygian = [0, 1, 3, 5, 7, 8, 10];
    private static readonly int[] MajorPentatonic = [0, 2, 4, 7, 9];
    private static readonly int[] MinorPentatonic = [0, 3, 5, 7, 10];

    private static readonly Style[] Styles =
    [
        // The hub: calm and open. No kit; a soft sub on 1 and 3; a shaker from the second visit; the theme on the bell.
        new(MusicIdentity.Hub, 94f, 0f, null, null, null, Lead.Bell, 1f,
            Hits(Silent), SnareKind.None, Hits(Silent), Hits(Silent), Hits(Silent), false,
            PercKind.Shaker, Hits("..o...o...o...o."), 2, "R.......F.......", BassKind.Sub, ArpKind.None, Rungs(Silent),
            0.85f, 1f, 0.45f, 2),
        // A Windows volume: glass. A soft four-on-the-floor, claps on 2 and 4, offbeat hats, an octave bass and a 16th glass arpeggio.
        new(MusicIdentity.WindowsRoot, 116f, 0f, Lydian, [0, 1, 4, 5], "lydian", Lead.Glass, 0.95f,
            Hits("X...x...X...x..."), SnareKind.Clap, Hits("....X.......X..."), Hits("..x...x...x...x."), Hits(Silent), true,
            PercKind.None, Hits(Silent), 1, "R.O.R.O.R.O.R.O.", BassKind.Pluck, ArpKind.Glass, Rungs("0123432101234321"),
            0.6f, 1.3f, 0.2f, 2),
        // A WSL distribution: the terminal. A shuffled kick, dry rims, keystrokes, a syncopated square bass, the theme re-struck.
        new(MusicIdentity.WslRoot, 112f, 0.12f, Dorian, [0, 3, 0, 6], "dorian", Lead.Square, 1.05f,
            Hits("X......x..x....."), SnareKind.Rim, Hits("....X.......X..."), Hits(Silent), Hits(Silent), true,
            PercKind.Typing, Hits("x.xx.x..xx.x.xx."), 1, "R..R..O...R.F...", BassKind.Square, ArpKind.None, Rungs(Silent),
            0.7f, 0.7f, 0.25f, 2),
        // A repository: clockwork. Kick on 1, the and of 2, 3; tight 16th hats; tick-tock; a sequencer bass; the commit stabs.
        new(MusicIdentity.Repository, 126f, 0f, Mixolydian, [0, 6, 3, 0], "mixolydian", Lead.Digital, 1f,
            Hits("X.....x.X......."), SnareKind.Snare, Hits("....X.......X..."), Hits("xoxoxoxoxoxoxoxo"), Hits(Silent), true,
            PercKind.Clockwork, Hits("X.x.X.x.X.x.X.x."), 1, "R.Rr.OR.R.Rr.FO.", BassKind.Fm, ArpKind.None, Rungs(Silent),
            0.5f, 1f, 0.15f, 1),
        // Band 1, the outskirts: a bouncy swing, marimba, an octave-hopping bass.
        new(MusicIdentity.Outskirts, 120f, 0.16f, null, null, null, Lead.Marimba, 1.4f,
            Hits("X.....x.X.....o."), SnareKind.Clap, Hits("....X.......X..."), Hits("..x...x...x...x."), Hits(Silent), true,
            PercKind.Shaker, Hits("o.o.o.o.o.o.o.o."), 2, "R..O..R.R..O..F.", BassKind.Pluck, ArpKind.None, Rungs(Silent),
            0.6f, 1f, 0.15f, 1),
        // Band 2, the trails, the festival arrangement (AD-52): the drop, breakdown and build are FestivalGroove's; the kick
        // pumps the pads, supersaws and bass (PumpDepth 0.6 ≈ −8 dB on each kick). The motif's breakdown pluck is the lead here.
        new(MusicIdentity.Trails, 126f, 0f, null, null, null, Lead.Pluck, 1.0f,
            Hits(Silent), SnareKind.Clap, Hits(Silent), Hits(Silent), Hits(Silent), true,
            PercKind.None, Hits(Silent), 1, Silent, BassKind.Pluck, ArpKind.None, Rungs(Silent),
            0.55f, 1f, 0.12f, 1, Arrangement.Festival, PumpDepth: 0.6f, Level: 1f),
        // Band 3, the deep woods, the orchestral arrangement (AD-52): dorian, a broad half-time theme (72 BPM) over the
        // string ostinato's eighths (144); the cellos carry the band motif and the flute echoes it.
        new(MusicIdentity.DeepWoods, 144f, 0f, Dorian, [0, 2, 6, 3], "dorian (orchestral)", Lead.Cello, 1.0f,
            Hits(Silent), SnareKind.None, Hits(Silent), Hits(Silent), Hits(Silent), true,
            PercKind.None, Hits(Silent), 1, Silent, BassKind.Deep, ArpKind.None, Rungs(Silent),
            0.65f, 1f, 0.3f, 1, Arrangement.Orchestral, HalfTime: true, Level: 0.85f),
        // Band 4, the underglow, the drum-and-bass arrangement (AD-52): 172 BPM breaks, the theme at half time (86) on the
        // liquid lead, a reese bass and sub, airy strings; dorian, i IV VII III between returns.
        new(MusicIdentity.Underglow, 172f, 0f, Dorian, [0, 3, 6, 2], "dorian (drum and bass)", Lead.Liquid, 1.0f,
            Hits(Silent), SnareKind.Snare, Hits(Silent), Hits(Silent), Hits(Silent), true,
            PercKind.None, Hits(Silent), 1, Silent, BassKind.Deep, ArpKind.None, Rungs(Silent),
            0.5f, 1f, 0.6f, 1, Arrangement.DrumAndBass, HalfTime: true, Level: 0.68f),
        // The hazard world (AD-52): phrygian at half time (70 over a 140 pulse), a beating drone, a heartbeat, ticking metal,
        // the theme on an unstable, sagging lead; chords every 2 bars between returns (i bII i VI).
        new(MusicIdentity.Hazard, 140f, 0f, Phrygian, [0, 1, 0, 5], "phrygian (hazard)", Lead.Unstable, 0.9f,
            Hits(Silent), SnareKind.None, Hits(Silent), Hits(Silent), Hits(Silent), true,
            PercKind.None, Hits(Silent), 1, Silent, BassKind.Sub, ArpKind.None, Rungs(Silent),
            0.6f, 1f, 1.5f, 2, Arrangement.Hazard, HalfTime: true, Level: 1f),
    ];

    // ---------------------------------------------------------------- keys

    /// <summary>A key's pad: a triad (Normal), two low sines on the tonic chord's lower notes, held over the theme (Drone, the Quiet theme) or a close cluster over soft noise (Cluster, Veiled).</summary>
    private enum PadKind
    {
        Normal,
        Drone,
        Cluster,
    }

    /// <summary>
    /// A theme's key and its sound: Root (MIDI), Mode and Scale (semitones), the free Progression (chord roots as scale
    /// degrees), the music box's MelodyDensity and RestChance (probabilities per eighth and per bar) and MelodyOctave,
    /// the bell's FM BellRatio, Pulse (false: no plucked bass, the kit stays light), and the pad's kind, tone (Hz) and gain.
    /// </summary>
    private sealed record Profile(
        int Root, string Mode, int[] Scale, int[] Progression, double MelodyDensity, double RestChance, int MelodyOctave,
        float BellRatio, bool Pulse, PadKind Pad, float PadToneHz, float PadGain);

    // Indexed by PlanetTheme. Roots sit in the octave C3..B3 (MIDI 48..59); melodies an octave or two above.
    private static readonly Profile[] Profiles =
    [
        new(53, "lydian", Lydian, [0, 1, 5, 4], 0.45, 0.25, 1, 3.0f, true, PadKind.Normal, 1200, 0.045f),               // Hearth: F
        new(50, "major pentatonic", MajorPentatonic, [0, 3, 1, 4], 0.5, 0.2, 2, 4.0f, true, PadKind.Normal, 1500, 0.04f),  // Meadow: D
        new(55, "mixolydian", Mixolydian, [0, 6, 3, 0], 0.5, 0.2, 1, 2.0f, true, PadKind.Normal, 1100, 0.045f),            // Workshop: G
        new(58, "major", Major, [0, 5, 3, 4], 0.4, 0.3, 1, 3.5f, true, PadKind.Normal, 1000, 0.045f),                     // Library: B♭
        new(52, "major pentatonic", MajorPentatonic, [0, 2, 3, 1], 0.55, 0.2, 2, 1.4f, true, PadKind.Normal, 1600, 0.04f), // Chime: E
        new(57, "dorian", Dorian, [0, 3, 6, 4], 0.45, 0.25, 1, 2.5f, true, PadKind.Normal, 1000, 0.045f),                 // Theatre: A
        new(48, "minor pentatonic", MinorPentatonic, [0, 2, 3, 1], 0.35, 0.3, 2, 2.0f, true, PadKind.Normal, 900, 0.045f), // Vault: C
        new(59, "lydian", Lydian, [0, 1, 4, 5], 0.5, 0.2, 1, 3.5f, true, PadKind.Normal, 1800, 0.04f),                    // Crystal: B
        new(55, "major pentatonic", MajorPentatonic, [0, 1, 3, 2], 0.6, 0.15, 2, 3.0f, true, PadKind.Normal, 1400, 0.04f), // Wild: G
        new(50, "minor pentatonic (drone)", MinorPentatonic, [0, 0, 3, 0], 0.12, 0.5, 1, 3.0f, false, PadKind.Drone, 700, 0.05f), // Quiet: D
        new(51, "major pentatonic (cluster)", MajorPentatonic, [0, 3, 0, 2], 0.15, 0.45, 2, 3.7f, false, PadKind.Cluster, 650, 0.035f), // Veiled: E♭
    ];

    /// <summary>[identity][theme]: the theme's key with the identity's own mode where it has one (built once).</summary>
    private static readonly Profile[][] KeyProfiles = BuildKeyProfiles();

    private static Profile[][] BuildKeyProfiles()
    {
        var table = new Profile[Styles.Length][];
        for (var i = 0; i < Styles.Length; i++)
        {
            var st = Styles[i];
            table[i] = new Profile[Profiles.Length];
            for (var t = 0; t < Profiles.Length; t++)
            {
                var p = Profiles[t];
                table[i][t] = st.Scale == null ? p : p with { Scale = st.Scale, Progression = st.Progression!, Mode = st.Mode! };
            }
        }

        return table;
    }

    /// <summary>
    /// One of the two music players. The current world plays on one deck; a world change prepares the other, starts it on
    /// the next beat and lets the old one stop and fade there, so tempo changes never share a clock. Holds the world's
    /// score and key, its clock, where it is in its music, its random stream and the cycle's prepared theme notes.
    /// </summary>
    private sealed class Deck(int group)
    {
        /// <summary>The voice group this deck plays into (its index).</summary>
        public readonly int Group = group;

        /// <summary>The deck's pumped group: voices that follow its sidechain (<see cref="PumpGain"/>), faded with the deck.</summary>
        public readonly int PumpGroup = group + GroupPumpOffset;

        /// <summary>Whether a voice group belongs to this deck (its plain or pumped group).</summary>
        public bool Owns(int g) => g == Group || g == PumpGroup;
        public MusicIdentity Identity;
        public Style Style = Styles[0];
        public PlanetTheme Theme;
        public WorldCharacter Character;
        public int Visits = 1;
        public int Development = 1;
        public Profile Profile = Profiles[0];

        /// <summary>A folder inside a repository: keeps a quiet echo of the repository's music (tick-tock, soft stabs, the commit call).</summary>
        public bool Echo;
        public long WorldKey;

        /// <summary>
        /// The deck's clock: its first downbeat (a frame) and its score's sixteenth note in frames; the pulse's sixteenth
        /// (the effects' grid), which is half of it in a half-time arrangement; and its steps per beat of the pulse (4, or 2).
        /// </summary>
        public long Origin;
        public double FramesPerSixteenth = 1;
        public double GridFramesPerSixteenth = 1;
        public int StepsPerBeat = 4;
        public long NextStep;

        /// <summary>The deck's gain (its style's <c>Level</c>: the arrangements' loudness balance).</summary>
        public float Level = 1f;

        /// <summary>How far the pumped group dips on a kick (0: no pump), and the frames of the last two kicks (the later may be ahead of the cursor).</summary>
        public float PumpDepth;
        public long PumpLast = long.MinValue / 2, PumpPrev = long.MinValue / 2;

        /// <summary>The step at which a festival drop re-starts the music at the top of a cycle (−1: none), and whether this deck's first downbeat is one.</summary>
        public long DropAtStep = -1;
        public bool DropArrival;

        /// <summary>The furthest a flight build has got in this deck (0..1: the flight's progress), and its steered riser voice.</summary>
        public float BuildPeak;
        public Voice? Riser;
        public int RiserGeneration;

        /// <summary>The bar of the cycle (−1 in an intro) and the sixteenth in it, of the step being played (the pads read them).</summary>
        public int Bar;
        public int InBar;

        /// <summary>The frame where it stops (its successor's first downbeat), or long.MaxValue.</summary>
        public long StopFrame = long.MaxValue;
        public bool Live;

        /// <summary>Arrived within <see cref="QuickHopSeconds"/> of the previous change: no arrival fanfare.</summary>
        public bool QuickHop;

        /// <summary>Picked up from memory: it resumes where it was left, without a fanfare.</summary>
        public bool Resumed;

        /// <summary>The identity of the world it took over from (the hub's open question is answered on arrival from it).</summary>
        public MusicIdentity? From;

        /// <summary>Sixteenths into the world's music at step 0 (a resumed or developed world starts later in it).</summary>
        public long StepOffset;

        /// <summary>Steps of an intro bar before the first cycle (the repository's flourish), 0 otherwise.</summary>
        public long IntroSteps;

        /// <summary>The step from which a four-sixteenth drum fill plays (a same-world arrival), or long.MinValue.</summary>
        public long FillFrom = long.MinValue;

        /// <summary>The crossfade position 0..1 and where it is heading (gain = sin(Fade · π/2), see RenderBlock).</summary>
        public float Fade;
        public float FadeTarget;
        public Random Rng = new(0);
        public bool NeedChord;
        public bool CycleReady;
        public bool RestBar;
        public int MelodyDegree;
        public int CounterDegree = 2;
        public int Chord;
        public ThemeVariation Variation;
        public int ThemeBars = ThemeLengthBars;
        public readonly LeadNote[] Notes = new LeadNote[MaxCycleNotes];
        public int NoteCount;
        public int NextNote;
        public int LastLeadMidi;
        public long LastLeadEnd = long.MinValue;

        /// <summary>Starts the melody over for a new world.</summary>
        public void ResetMelody()
        {
            MelodyDegree = Profile.Scale.Length / 2 + 2;
            Variation = ThemeVariation.Plain;
            NoteCount = NextNote = 0;
            LastLeadEnd = long.MinValue;
            Chord = Profile.Progression[0];
        }
    }

    /// <summary>A prepared theme note of the current cycle: onset/length in sixteenths from the cycle start, a degree in the deck's scale.</summary>
    private struct LeadNote(int start, int length, int degree, float velocity, float delay, MelodyPart part)
    {
        public int Start = start;
        public int Length = length;
        public int Degree = degree;
        public float Velocity = velocity;

        /// <summary>Extra onset delay in sixteenths (a grace note sounds just before its note).</summary>
        public float Delay = delay;
        public MelodyPart Part = part;
    }
}
