FileScape Planets: music per world (third planets iteration, 2026-11-14; PLANET-28, AD-49)
=====================================================================================

Every file is 44.1 kHz, 16-bit stereo, rendered offline by SkySynth (seed 2026): no samples, everything synthesised.
The levels match the old music (about -28 dBFS RMS), so the comparison is fair. The app plays it 5 dB louder.

Start here
----------
1. before-108bpm-hearth-30s.wav   The old behaviour, rendered from the code before this change: Hearth at 108 BPM,
                                  walking, a launch at 14 s, a landing at 17 s. Every world sounded like this.
2. hub-25s.wav, then band2-25s.wav  The calm arrival, then the first trail out. This is the main "set off" contrast.
3. journey-route-75s.wav          The whole family in one playthrough.

One clip per world (25 s each)
------------------------------
In each clip: first a wind-up (from 2.5 s), a launch (about 3.2 s), 2.4 s of flight and a landing (about 5.7 s) back in
the same world, so you hear the cues over that world's beat; a selection tick (about 9.7 s) and a bell-flower chime
(about 10.5 s); then the world's music, uninterrupted, while walking. The theme starts at 0 s (the repository's at its
second bar); its last two bars and the world's own material after it are in the clean second half of each clip.

hub-25s.wav                       This computer, 94 BPM. No drums: a pad, a soft sub on beats 1 and 3, the FileScape
                                  theme on a bell, stated simply. Listen to the end of the theme (from about 15 s):
                                  it does not land on home. It rises (la sol la do' - re') and hangs on an open chord,
                                  a question. It is only answered when you leave: the next world's first downbeat rings
                                  its home note.
windows-root-25s.wav              A Windows volume, 116 BPM, lydian (bright). A glassy 16th-note arpeggio, a soft
                                  four-on-the-floor kick, claps on 2 and 4, an octave-bouncing bass, the theme on a
                                  glassy bell.
wsl-root-25s.wav                  A WSL distribution, 112 BPM, dorian (warmer, modal), a slight shuffle. Keystroke
                                  clicks in a typing rhythm with an "enter" every second bar, dry rim clicks, a square
                                  bass, the theme on a square lead whose long notes repeat like a held key. Compare it
                                  with windows-root: glass and light against keys and warmth.
repository-25s.wav                A repository, 126 BPM, mixolydian, dead straight: clockwork. It opens with a one-bar
                                  arrival flourish (crash, a fast run up the scale, two stabs). Then tick-tock wood
                                  blocks, tight 16th hats, a sequencer bass, and the "commit" figure: a call
                                  (da, da-da DAAA) answered by two short chord stabs (chk-CHUNK). The stabs also answer
                                  the theme every second bar.
band1-25s.wav                     Folder band 1, "the outskirts", 120 BPM: a bouncy swing, marimba, an octave-hopping bass.
band2-25s.wav                     Band 2, "the trails", 124 BPM: four-on-the-floor, an offbeat square bass, open hats,
                                  dotted-eighth plucks that pull forward.
band3-25s.wav                     Band 3, "the deep woods", 128 BPM, dorian: toms and rim clicks, a 16th shaker, a deep
                                  syncopated bass, a woody lead.
band4-25s.wav                     Band 4, "the underglow", 132 BPM, lydian dominant: the fastest. 16th hats and a 16th
                                  FM bass pulse, glowing fourths. Deeper means more mysterious, still driving, not
                                  slower or sadder.
band2-inside-repository-25s.wav   A band-2 folder inside a project: the trails groove with a quiet echo of the
                                  repository (tick-tock, soft stabs, and the commit call high and quiet every 8 bars).
                                  Compare it with band2-25s.wav.
band2-third-visit-25s.wav         The same trails folder on its third visit: a counter-line, ghost notes, open hats, the
                                  arpeggio doubled, the theme's long notes doubled below, and a later variation of the
                                  theme. Compare it with band2-25s.wav.

What links the bands: after the theme (8 bars), each band plays the band motif, which grows out of the theme's
opening (mi sol ...) in a bouncy 3+3+4 rhythm. Each band shapes it differently (as written, displaced with a rising
answer, an octave down with an echo, octave leaps) and varies it phrase by phrase. The theme itself returns every 16 bars
in every world, with variations.

Routes
------
journey-route-75s.wav             This computer -> WSL root -> band 1 -> band 2 -> repository -> a band-2 folder in it ->
                                  back to the repository (second visit) -> This computer (second visit). Each move is a
                                  wind-up aligned to the beat, a launch, 2.4 s of flight and a landing:
                                    launch  7.8 s, land 10.2 s (WSL)          launch 17.6 s, land 20.0 s (band 1)
                                    launch 27.3 s, land 29.7 s (band 2)       launch 36.9 s, land 39.3 s (repository)
                                    launch 47.1 s, land 49.5 s (in the repo)  launch 56.2 s, land 58.6 s (repository, back)
                                    launch 65.4 s, land 67.8 s (hub, back)
                                  Listen for: the new world comes in on the next beat after landing, at its own tempo;
                                  the first repository arrival has its flourish, the return (58.6 s) does not and picks
                                  up in the commit motif rather than starting over; the hub at the end resumes in its
                                  wandering "journeys" section with a counter-line, not cold.
quick-hops-20s.wav                Band 1, then five hops in 6 s without flying (4.0, 5.2, 6.4, 7.6, 8.8 s: band 2,
                                  band 3, another band-2 folder, the repository, a folder in it), then Backspace to the
                                  repository (10.2 s) and forward again (14.2 s). Listen for: no pile-up or mush, no
                                  clicks, each world taking over on a beat, no fanfares while hopping, and the returns
                                  picking up where they were (here close to their start, since each was heard only
                                  for a moment).

What to judge (nobody has listened yet; everything above is measured, not heard)
--------------------------------------------------------------------------------
- Is the hub calm enough, and does the music after it have enough bounce and momentum?
- Can you tell Windows from WSL, and each band from the next, with your eyes closed?
- Does the repository feel special on arrival? Is the commit figure memorable, or too busy?
- Do the launch and landing still stand out over the drums? (Measured: 5-12 dB clear of the ducked music.)
- Are any of the drum sounds harsh, or any level wrong?

Measurements (seed 2026; the clips include walking and the cues)
---------------------------------------------------------------
file                               RMS dBFS  peak dBFS  onsets/s
before-108bpm-hearth-30s            -28.8     -11.5       3.4
hub-25s                             -28.3     -13.8       3.1
windows-root-25s                    -27.9      -8.9       5.3
wsl-root-25s                        -29.6     -12.4       3.9
repository-25s                      -28.5      -9.7       7.9
band1-25s                           -28.8      -9.7       5.3
band2-25s                           -28.6     -10.0       5.7
band3-25s                           -28.4      -9.1       8.3
band4-25s                           -27.0     -10.2       8.3
band2-inside-repository-25s         -28.5     -10.4       5.4
band2-third-visit-25s               -28.3     -10.0       7.7
journey-route-75s                   -28.3     -10.2       3.7
quick-hops-20s                      -28.0     -10.2       7.9
Music alone, 16 s per world (SkySynth tests): the hub 1.9 onsets/s; the other worlds 4.3-8.8. Music voices at most 24
of 28 in any clip. The onset counter is crude (it also counts footsteps); the WSL keystrokes are narrow and quiet, so it
undercounts them.

How these were made
-------------------
tests/FileScape.Core.Tests/SkySynthTests.cs, Render_world_evidence_to_wav, run with SKYSYNTH_EVIDENCE_DIR set to this
folder (skipped in the normal test run). The "before" file was rendered from a frozen copy of the pre-change SkySynth.
