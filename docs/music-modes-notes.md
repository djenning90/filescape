# Musical modes in game music: research notes (2026-11-19, PLANET-79/80, AD-72)

DJ asked for three references: https://youtu.be/tWthNcF_4Uk, https://youtu.be/SbRD3tPipNw and "The Dorian Mode Feels
Mysterious" (to be searched for). They are used only as inspiration: no melody from any game named here is quoted or
reproduced in FileScape. Everything below is paraphrased.

## What could be reached

- **No audio was heard and no transcript was read.** The videos' public watch pages gave the title, the author's
  description and the chapter list (timestamps and the pieces analysed). The auto-generated captions exist but fetching
  them was refused in this environment, and transcript mirrors and forum write-ups were blocked (HTTP 403). So the notes
  per video are the creator's own summary, what the chapter list shows he analyses, and standard theory about the named
  devices. Where a point is my reading rather than the video's, it says so.
- **The third reference is the second link.** `SbRD3tPipNw` is titled "The DORIAN Mode Feels MYSTERIOUS (among other
  things)". A search for the title found no other video by that name, so the two Dorian references are one video.
- Both are by **8-bit Music Theory**, from a series with one video per mode (Phrygian Dec 2022, Dorian Feb 2023, then
  "The AEOLIAN Mode Feels Devastating" and "The Ionian Mode Feels RELAXING" in 2023, per the TVDB episode list). Only the
  two DJ named were looked at.

## "The PHRYGIAN Mode Feels THREATENING" (tWthNcF_4Uk, 16 min 12 s)

- **Main claim (description):** Phrygian is the most threatening of the seven modes, more so than Locrian, even though
  Locrian is technically the darker scale. He came to this by looking at where each mode is actually used, not at the
  scales on paper.
- **The devices (chapters):** the **♭2 → 1 resolution** (the half step down onto the tonic); the **sus4♭9 chord** (a tonic
  chord carrying both the fourth and the flat second: a stacked, unstable sound); and the **♭II chord** (the major chord a
  half step above the tonic, moving back down to it).
- **Examples analysed (chapters):** a boss battle in Chrono Trigger; an empire's theme in Final Fantasy VI; a battle
  theme in Final Fantasy VII; Zoombinis; Norfair (Metroid) and an area of Super Metroid; a piece from Ni no Kuni II; an
  ending from For the Frog the Bell Tolls; the trainer battle in Pokémon Red/Blue (twice); Hollow Bastion (Kingdom
  Hearts). So: battles, villains' empires, hostile underground places.
- **My reading for FileScape:** the threat is a minor tonic leaned on from a half step above. Locrian's unstable tonic
  (a diminished chord) sounds more lost or broken than threatening, because it never feels like a home to be attacked
  from. This matches FileScape's existing hazard scores (both Phrygian, AD-52, AD-60) and suggests Locrian for something
  else: a broken, unsettled place rather than danger.

## "The DORIAN Mode Feels MYSTERIOUS (among other things)" (SbRD3tPipNw, 15 min 1 s)

- **Main claim (description):** Dorian is the most versatile of the seven; "mysterious" is in the title for the click,
  but the video's point is that Dorian ranges from mysterious through heroic to funky, and that what decides which is
  **how the characteristic raised sixth is handled**.
- **Examples analysed (chapters):** a lament from Twilight Princess; the first town of Hollow Knight; a character theme
  and a battle theme from the SNES Final Fantasy VI / Chrono Trigger era; a character theme from Octopath Traveler; a
  Halo 3 piece and the Halo theme; the Dark World of A Link to the Past; a Kirby's Block Ball stage; a well-known Ocarina
  of Time song; then a section "The Dorian mode is FUNKY". So: sad and mysterious places (a lament, a quiet ruined town,
  a dark mirror world), heroic and martial themes, and dance grooves.
- **My reading (standard theory of the device):** the raised sixth sits in the major IV chord of a minor key. Held in a
  slow melody or as a sustained IV under a minor tonic it sounds wistful and mysterious (a minor world with a light left
  on); voiced in a strong i–IV march it sounds heroic; repeated in a syncopated i–IV vamp it is funky. FileScape's deep
  woods orchestra (AD-52, AD-66) and the WSL root and drum and bass are Dorian already: this explains why the orchestra
  felt "beautiful and expressive" to DJ.

## What the sketches take from this (AD-72)

- One tune in all seven modes over a bed that states each mode's characteristic degree and chord (Dorian's IV,
  Phrygian's ♭II, Lydian's II, Mixolydian's ♭VII and v, Aeolian's ♭VI–♭VII, Locrian's diminished tonic and ♭II).
- Phrygian kept for threat (the hazard world in the mode-shift journey closes ♭II–i); Locrian heard as broken rather
  than threatening; Dorian used for the landed, wistful deep woods; Lydian for floating and wonder (the overlook); Aeolian
  for loss (the empty moon); Mixolydian for the bright, unresolved lift of a launch.

## Into the game (2026-11-19, AD-79, PLANET-97..99)

DJ's reaction to the reel: preference broadly declined Ionian → Locrian (Locrian unpleasant), "the Lydian Overlook is
beautiful". What went in: the Lydian overlook on the overlook and at its telescope (first time as heard, then a "second
light"), an original Ionian/Dorian star chart for the overview, a minimum dwell so phrases finish through quick hops, and
the aurora analog voice. Locrian is used nowhere; Phrygian stays the hazard world's; Aeolian (the empty moon) and the
Mixolydian launcher stay offline for now.

### Wider ideas for later (not built)

- **More experiences:** the Aeolian lament for an empty moon or a sealed (access-denied) world, alongside the abyss
  score; the Mixolydian launcher as an occasional take-off variation on a revisit of a launcher pad; a soft Dorian
  "reading room" bed under the reader's full-screen view (today the preview ducks the music, which already serves it).
- **The aurora voice further:** a 24 dB ladder (cascaded or Moog-style ZDF) as a second filter mode; pulse-width
  modulation and a sub-oscillator; filter keyboard tracking as a parameter; slow per-voice LFOs on cutoff (pads that
  breathe); unison spread across the stereo field; poly-glide (portamento) for leads; a resonance that self-oscillates
  for sweeps and risers; giving one world identity an aurora lead (the WSL root's square lead is the obvious candidate).
- **An FM voice (the other option):** a 4-operator stack with operator feedback and per-operator envelopes (DX-style
  algorithms), velocity-to-index for an electric-piano tine; it would sit beside the existing 2-operator bells.
- **Engine:** a per-deck send to a second, longer reverb for the experience pieces; a chorus/ensemble on the pads;
  a gentle bus compressor before the limiter; true-peak-aware limiting (today's peaks sit far below it).
- **The scheme:** time of day or session length as a fourth question (later variations after long play); a "home
  stretch" variation when returning to the hub after a long trip; a per-folder seed choosing among the theme's
  variations for first visits (stable surprise) without touching which arrangement plays.
