FileScape music: three new arrangements of the FileScape theme, plus the hazard world (2026-11-15, PLANET-35, AD-52)
=================================================================================================================

All files: 44.1 kHz, 16-bit, stereo, rendered offline by FileScape's own synth (SkySynth, plain C#, no samples), the same
code the game runs. Launches in these clips work as the game does them: a 0.4 s wind-up aligned to the beat, a 2 s flight
(Fly called every frame), the landing, then SetWorld and Arrive. Rendered with
SKYSYNTH_ARRANGEMENTS_DIR=<folder> tools/fs.sh test-wsl --filter Render_arrangement_samples.

Nobody has listened to these yet. Everything below is a measurement, not a judgement by ear. Please judge by ear.

Where each arrangement lives in the game
----------------------------------------
- Festival / big room: folder band 2, "the trails" (folders 2–3 levels below a root). This is the band you fly through
  most, and siblings with the same theme keep the music going, so the build in the air and the drop on landing happen
  often. 126 BPM, the folder's own key and mode.
- Orchestral: folder band 3, "the deep woods" (depth 4–5). Dorian. The theme moves at 72 BPM over string eighths at 144.
- Drum and bass: folder band 4, "the underglow" (depth 6 and deeper). 172 BPM breaks under the theme at half time (86),
  dorian. Deeper in the tree is faster, as before.
- Hazard: worlds of kind Hazard (the broken shell over a black hole). Phrygian, the theme at 70 BPM over a 140 pulse.
- Unchanged: the hub (still gentle; no drop ever lands there), Windows volumes, WSL distributions, repositories and band 1
  (the outskirts).

The files
---------
festival-trails-58s.wav (58 s)
  0–1.9 s   the outskirts. Launch at 1.9 s, land at 3.9 s: the trails' first downbeat is the drop (crash and sub impact).
  4–19 s    the drop: the theme on a wide supersaw with a harmony a third below, a four-on-the-floor kick that pumps the
            pads and bass (sidechain), claps, offbeat open hats, an offbeat saw bass.
  19–23 s   the breakdown: no kick, a filtered pad, the band motif on a pluck.
  23.4 s    launch. Listen to the flight: a snare roll that thickens from eighths to sixteenths to thirty-seconds, stabs
            climbing and a noise riser opening. Land at 25.4 s; the drop arrives on the next beat (≈ 0.5 s later) with
            the theme from the top.
  ≈ 41–48 s breakdown; 48–56 s the cycle's own build (snare roll from quarters to sixteenths, a riser, the pad's filter
            opening, rising stabs, one beat of silence); the drop again at ≈ 56 s.
  Listen for: whether the drop lands as a payoff after the flight, whether the pump feels like momentum or like
  wobbling, and whether the theme is still clear on supersaws.

dnb-underglow-56s.wav (56 s)
  0–3.5 s   the trails. Launch at 3.5 s (the flight builds in the festival), land at 5.5 s in the underglow: kick, snare,
            crash and impact together.
  Then the break (a two-step kick, snares on the 2 and 4 of the fast pulse, ghost notes), a reese bass (two saws 14 cents
  apart, the filter opening on each note) and its sub, airy strings, and the theme at half time on a soft FM lead with a
  dotted echo. Around 28–33 s the kick and snare rest for two bars (hats and ride only); the last four bars of each
  cycle roll harder, with a fill into the next. Launch at 44.5 s, land at 46.5 s in another underglow world (other key).
  Listen for: whether it feels like a different world from the festival, the reese's growl, whether the half-time
  theme sits over a 172 break.

orchestral-deep-woods-60s.wav (60 s)
  Land at 3.9 s (harp glissando up, timpani, a soft cymbal). One cycle of the theme:
  ≈ 4–10 s    the motif on the flute (an octave up) over pizzicato strings, quiet;
  ≈ 10–17 s   the answer on the first violins, the spiccato ostinato starts;
  ≈ 17–24 s   the raised motif on the horns with the violins above, timpani, a cymbal swell into
  ≈ 24–31 s   the cadence tutti (violins, horns, flute an octave up), crash;
  ≈ 31–44 s   soft: the band motif in the cellos, the flute echoing it;
  ≈ 44–51 s   a horn call (mi sol do'), the violins' answer, a timpani roll (≈ 47.5–51 s) into the tutti climax at
              ≈ 51 s (in a longer stay, bar 15 follows: the harp falling, the strings thinning out).
  Launch at 52.5 s (it cuts into the climax), land at 54.5 s in another deep-woods world.
  Other returns hand the opening phrase to the clarinet (syncopated or inverted returns) or the oboe (octave or
  augmented returns). Listen for: a sense of ensemble (sections, not single synth lines), space (panning: violins left,
  violas and cellos right, horns back right), and the two dynamic waves.

theme-across-arrangements-60s.wav (65.4 s; the name was asked for as 60 s)
  The theme's first 8 bars in each arrangement, back to back, all in the same key (B flat major root, the Library theme's;
  each arrangement keeps its own mode):
  0.0 s festival (15.6 s), 15.6 s drum and bass (22.8 s), 38.4 s orchestra (27 s; the half-time parts take longer).
  Listen for: the same melody recognisable in all three. For the earlier arrangements, see
  ..\..\2026-11-14-planets-3\music\.

hazard-60s.wav (60 s)
  Land at 3.9 s in the hazard world (a deep inharmonic gong and a falling boom). Then a beating low drone, a heartbeat,
  ticking metal, reversed swells into every second bar line, and the theme on an unstable, wobbling lead whose long
  notes sag.
  15–21 s   approaching an edge: HazardWarning rises 0 → 1. A falling tritone pulse speeds up from one a second to four,
            a low rumble grows, the music ducks a little.
  21–24 s   backing off (the warning slows and stops); 26–29 s approaching again.
  29.1 s    the fall: a crack, falling glass, a thud; 2.5 s of a whoosh falling three octaves, its two voices circling
            left and right faster and faster; the music ducked deep (≈ −14 dB).
  31.6 s    FallReturn, back at the parent (the outskirts): an upward pop and a bright chord, the music comes back.
  Launch at 42.4 s, land at 44.4 s back in the hazard world; a held warning at 52–56 s.
  Listen for: whether the warning reads as "careful" rather than as an error, whether the fall is unmistakable, whether
  the music leaves room for these cues.

rapid-hops-20s.wav (20 s)
  The hub, then 8 worlds in 2 s (4.0–5.8 s): trails, deep woods, underglow, hazard, repository, outskirts, WSL, trails.
  Only the last one is heard properly (each change retargets the next beat; no fanfares while hopping), then it settles.
  Listen for: no pile-up, no clicks, no restart of anything mid-note.

backtrack-30s.wav (30 s)
  Hub → trails → deep woods → underglow by flights, then back: deep woods (a flight), trails and the hub (quick returns).
  A world you come back to picks up where it was (on its next half-cycle) instead of starting from the top. The flight
  out of the trails builds and lands in the deep woods with an impact. Back in the hub, no drop: its welcome stays gentle.

Measurements (tests in tests/FileScape.Core.Tests/SkySynthTests.cs; 32 s per arrangement, three keys)
---------------------------------------------------------------------------------------------------
Integrated loudness (K-weighted, gated, as in BS.1770), music alone:
  hub −26.6, Windows −26.2, WSL −28.3, repository −26.9, outskirts −26.8 LUFS (the reference: −26.9)
  festival −27.1, orchestra −27.2, drum and bass −27.1, hazard −27.3 LUFS: all within 0.4 dB of the reference.
Peaks: at most 0.37 (−8.6 dBFS) for the music alone; the limiter (0.8) never acts on music. Clip peaks: 0.27–0.42.
Clicks: the largest sample-to-sample step across world changes stays below 1.6× the music's own largest step.
Rapid hops and backtracking: ≤ 28 music voices of 28, ≤ 40 voices of 40, no pile-up (RMS 0.050 vs 0.030 settled).
A minute in one world: mean music voices 10–14 (max 23–28), the theme and the world's own material both present, the
  second half differs from the first; 5 s loudness ranges: festival −27.4…−33.4 dB (drop vs breakdown), orchestra
  −28.0…−34.3, drum and bass −27.2…−28.3, hazard −27.1…−28.2.
Cues over the ducked music (higher is clearer; 4 dB is the floor): launch/landing 5–13 dB in every world; the hazard
  warning 4.8–10.6 dB, the fall 8–14 dB, the return 4.4–12.5 dB (the tightest everywhere is the outskirts' marimba).
Cost: 26–27 ms to render 1 s of each new arrangement with a flight build, a fall, the warning and six effects (Debug
  build; the bound is 60 ms, the busiest AD-49 world took 27–28 ms): ≈ 0.45 ms per 60 Hz frame.
