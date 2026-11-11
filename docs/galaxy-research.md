# Super Mario Galaxy: research note for the planetary FileScape

Status: **provisional**. This note pulls together notes from four research agents. Nobody played the game and nobody watched a video. Every observation below comes from a text source that one of the agents read. Anything an agent marked as its own inference is labelled *inferred*. No source gave a video timestamp, so there are none here.

Purpose: feed design ideas into the planetary world (PLANET-10..18, AD-39..43) without copying Nintendo's content. Researched 2026-11-11 by four research agents (two read the Iwata Asks interviews in full, one gameplay sequences, one camera/gravity analyses) and synthesised here; section 5 (added after building) records what the research changed in the design and what was tried in the build.

---

## 1. Sources examined

### 1.1 Accessed (text)

| Ref | Source | Kind | Notes |
|---|---|---|---|
| IA1-1..4 | Iwata Asks: Super Mario Galaxy, Vol. 1 (Producer and Director: Shimizu, Koizumi), parts 1–4. Base `https://www.nintendo.com/en-gb/Iwata-Asks/Iwata-Asks-Super-Mario-Galaxy/Volume-1-The-Producer-and-Director/`: `1-How-Super-Mario-Galaxy-Was-Born/…-230044.html`, `2-Listening-to-Many-Voices/…-230081.html`, `3-A-Mario-Even-Beginners-Can-Play/…-230106.html`, `4-From-5-to-95/…-230131.html` | Official interview transcript | Fetched with curl. Quotes are verbatim. |
| IA2-1..5 | Iwata Asks: SMG, Vol. 2 (The Developers), parts 1–5. Same base, `Volume-2-The-Developers/…` (`-222572`, `-222607`, `-222643`, `-222675`, `-222714`) | Official interview transcript | Part 2, "Benefits of a Spherical Field", is the most relevant. |
| IA3-1..4 | Iwata Asks: SMG, Vol. 3 (The Sound Team), parts 1–4 (`-205026`, `-205066`, `-205095`, `-205134`) | Official interview transcript | The agents gave the part 1 URL in two forms: one with the `/Iwata-Asks/` path segment and one without it plus a doubled dash before the ID. Both resolved for the agents who used them. |
| IA4-1..4 | Iwata Asks: SMG, Vol. 4 (Shigeru Miyamoto), parts 1–4 (`-220647`, `-220674`, `-220703`, `-220743`) | Official interview transcript | |
| IAG2-V2-4 | Iwata Asks: SMG2, Vol. 2, "4. A New Continent": `https://www.nintendo.com/en-gb/Iwata-Asks/Iwata-Asks-Super-Mario-Galaxy-2/Volume-2-Koizumi-Motokura-Hayakawa-and-Hayashida/4-A-New-Continent/4-A-New-Continent-238140.html` | Official interview transcript | Covers the Galaxy 1 vs Galaxy 2 camera. |
| IAG2-V1-1 | Iwata Asks: SMG2, Vol. 1 (Miyamoto), "Playing a 3D Game Like It's 2D": `…/Iwata-Asks-Super-Mario-Galaxy-2/Volume-1-Miyamoto/1-Playing-a-3D-Game-Like-It-s-2D/…-230816.html` | Official interview transcript | |
| 3E8-1, 3E8-2 | Fan translations of Iwata Asks SMG Vol. 1 and 2: `https://3e8.org/writings/iwata-asks/super-mario-galaxy-vol-1.html`, `…-vol-2.html` | Fan transcript | Secondary. Used only where it adds a detail. |
| MAN | Official US manual PDF: `https://csassets.nintendo.com/noaext/image/private/t_KA_PDF/Wii_Super_Mario_Galaxy` | Manual (text recovered by a custom decoder) | Button glyphs were lost in decoding. |
| WIKI-* | Super Mario Wiki: Gateway Galaxy, Good Egg Galaxy, Honeyhive Galaxy, Comet Observatory, Launch Star, Pull Star, Sling Star, Spin, Star Bit, Gravity, Galaxy, Super Mario Galaxy (main page read from character 300000) at `https://www.mariowiki.com/<Page>` | Fan wiki | Secondary. Some claims there are not backed by a developer quote (see 2.4). |
| LW-G, LW-C | Luma's Workshop: `https://lumasworkshop.com/wiki/Gravity` (marked "in progress"), `https://lumasworkshop.com/wiki/Cameras` | Community modding wiki | Camera parameters were not checked against code. |
| GDB | SMG community object database: `https://smgcommunity.github.io/galaxydatabase/` (`config_GlobalSegmentGravity`, `config_GlobalPlaneGravity`, `config_CubeCameraArea`, `config_SuperSpinDriver`) | Community object database | |
| PET | Petari decompilation: `https://github.com/SMGCommunity/Petari/blob/master/src/Game/Gravity/PlanetGravityManager.cpp`, `…/PlanetGravity.cpp`, `…/MapObj/SuperSpinDriver.cpp` (partly read) | Community decompiled code | Frame counts were read from the code. Seconds assume 60 fps (*inferred*). The 60 fps target itself comes from IA2-5 / 3E8-2. |
| SIR | Sirlin, "The Fun of Super Mario Galaxy": `https://sirlin.net/articles/the-fun-of-super-mario-galaxy` | Essay | |
| KOT | Kottke quoting a Gamasutra interview with Koizumi: `https://kottke.org/07/11/nice-interview-with-nintendo-game-designer-yoshiaki` | Quotation of an article | The original (Gamasutra story 16386) was not fetched. |
| ALE | Jeremy Alessi, "Games Demystified: Super Mario Galaxy" (Gamasutra, 27 Mar 2008): `https://www.gamedeveloper.com/design/games-demystified-super-mario-galaxy` | Article | Alessi's own rebuild, not Nintendo's code. |
| GDC07 | Miyamoto GDC 2007 keynote report: `https://gamedeveloper.com/console/gdc-miyamoto-talks-game-design-i-super-mario-galaxy-i-` | Article | Only says the spherical planets come from Mario 128. |
| NUK | Nintendo UK "Gravity 101" (2008): `https://www.nintendo.com/en-gb/News/2008/Gravity-101-of-Super-Mario-Galaxy-250418.html` | Player-facing article | |
| NSIDR | `https://nsidr.com/archive/super-mario-galaxy-team-talks-about-sound-and-music` | Summary of the sound interview | Secondary. |
| DCH | Digital Chumps review: `https://digitalchumps.com/2008/03/23/super-mario-galaxy/3` | **Search snippet only** | Weak. |
| WP-OST | Wikipedia, Super Mario Galaxy Original Soundtrack | Encyclopedia | Its "several tempos" claim was not found in the interviews. |

### 1.2 Could not be accessed or used

- **Video playback of any kind.** On YouTube the pages returned only navigation and footer text: no description, no chapters, no transcript. This covers QLH_0T_xv3I ("How Spherical Planets Bent the Rules in Super Mario Galaxy"), vALtyrp87mI ("How Did They Do That – Mario Galaxy's Gravity", known from oEmbed only) and 8rPcbVJAqgI (Spanish, intro/cameras/gravity). **So there are no stills, no timestamps and no frame-by-frame timing.**
- **Game Maker's Toolkit:** search found no episode about Galaxy alone. The Amara transcript page needed a login.
- **iwataasks.nintendo.com:** certificate error. The Nintendo UK mirror was used instead.
- **GDC / CEDEC:** no talk on Galaxy's camera or gravity was found. A "GDC 2008 Koizumi camera talk" appeared only on spam pages.
- **Not read:** Iwata Asks SMG2 sound chapter. One agent did not read Vol. 3 part 2 or Vol. 4; another agent did read them.
- **Excluded:** `https://tcrf.net/Super_Mario_Galaxy/Level_Design_Changes` returned instructions aimed at AI agents (a prompt injection). Nothing from it is used here.
- **Gaps across all sources:** the arrival fly-over camera when you enter a galaxy, the landing animation and camera after a launch, how the camera handles roll on small planets in numbers, coyote time or landing tolerances, BPM figures, and the gravity strength values (normal, light, heavy).

---

## 2. Observations

### 2.1 Introducing a destination

- **You can see where you're going, and that makes you want to go.** Shirai: "because you are now able to look up to the stars and see all the different planets out there, you want to travel to those planets." [IA2-2]
- **Seeing the destination is how players avoid getting lost.** Iwata: "Being able to visualise the planet where you should be going to gives you the impression that ... you'll be able to get there when you need to without getting lost." [IA2-2] The problem he names: "there are quite a few people who get lost within the playing field, because they can't figure out which way to go next." [IA1-3]
- **The screen should be enough without text.** Shirai: "players would be able to figure out what to do next by just looking at the screen." Hint text was "the last resort". Iwata calls fixing a problem with text "an admission of defeat". [IA2-2]
- **The world is compact.** Motokura: "distinct planets floating around within a compact field of outer space." [IA2-2]
- **A small starting body teaches through a toy task before the journey begins.** Gateway Galaxy starts on a small planet with holes, pipes, pools, cottages and a monument. A hide-and-seek task has to be finished before the gateway appears and planet-hopping is taught. [WIKI Gateway Galaxy]
- **Some planets are scenery only.** In Good Egg, some planets can be seen but never reached, for example a multicoloured cube. [WIKI Good Egg Galaxy]
- **The way out is earned on the spot.** Collecting 5 star chips assembles the launcher to the next body (Gateway and Good Egg). [WIKI; MAN p.17]
- **Choosing a destination at the hub:** you grab an object at the top of a dome to enter a selection view. "Point at something other than a galaxy and press [A] to grab a point you can use to rotate the system for a better view." [MAN p.9–10]
- **Not found:** how a newly arrived galaxy is framed (fly-over or establishing shot). [WIKI Galaxy; gameplay-sequences agent]

### 2.2 Running and jumping feel

- **Feel comes first.** Hayashida: a Mario game "should always be a 'new toy' - that it should feel both pleasant and responsive." [IA2-1] Miyamoto: "it takes a whole year just to make the player-character movements". [IAG2-V1-1]
- **Moving around is fun in itself.** Koizumi wanted "lots of areas ... that could be enjoyed, even by little children, just by moving Mario around. In these places, you don't have to think". [IA1-4] Miyamoto: "Mario is a game where you can have fun even if you just move him around." [IA4-4]
- **Running over odd shapes is the experience.** Aoyagi, who wrote the gravity code: "I would like our players to have an experience like no other, running around all the weird-shaped planets." [IA2-5] He worked "to make the game run at 60 frames per second". [IA2-5; 3E8-2]
- **Jumping onto targets on a sphere is hard to judge.** Koizumi: "on a spherical map, it's pretty difficult to try to jump and stomp on an enemy." [IA1-3] The answer was a short-range action that can be judged "even when the camera is looking straight down from above". [IA1-3]
- **Few inputs, and no two-input combinations.** Koizumi: "limit the number of buttons ... to two." Adding a simultaneous button press "gets a little difficult". [IA1-3]
- **A cooldown creates rhythm.** "once you spin, you won't be able to spin again for a little while. That way, you'll learn to time shaking". [Miyamoto via Shimizu, IA1-3]
- **Analogue walk/run, and a rich set of jumps.** "Tilt [stick] a little to walk and a lot to run." The manual lists a triple jump (press on landing), a backward somersault, a side somersault, a long jump and a spin jump at the peak "to go higher than in a normal jump". [MAN p.11–13] Shimizu: "ten-odd varieties of jump". [3E8-2]
- **The spin gives a little extra air.** It provides "a slight aerial boost and falling delay", once per airtime. [WIKI Spin]
- **Moving platforms are forgiving.** You "can finally jump straight up while riding a moving platform and land on the platform". Wall jumps no longer need precise timing. [SIR]
- **Up is the surface normal.** ALE's rebuild (not Nintendo's code) uses the surface normal as "down", interpolates the character's orientation towards it, and adds a 250 ms jump boost along the normal. [ALE]
- **Gravity gives a direction, not a strength.** Gravity is combined by priority: the highest priority replaces the total, equal priorities are added as vectors, and the result is normalised. Only the direction is kept. [PET PlanetGravityManager.cpp] Per-source strength is `4000000/r²`, with r clamped to at least 1. Range −1 means unlimited. [PET PlanetGravity.cpp] There are many shape types: point, plane, disk, torus, cube, cone, segment and others. [LW-G]
- **How long a jump lasts, in a player-facing article:** a jump lands "a second or so later". [NUK]
- **Not found:** coyote time or landing tolerances.

### 2.3 Camera under changing gravity

- **The player should not think about the camera.** Development aimed to keep players from "thinking about the camera". The "planet camera" "gently follows Mario as he runs across a spherical world without sudden jerks or obscuring his positioning". [WIKI SMG development section, its ref 37] Koizumi: "We made the first Mario Galaxy so that you can play it without messing with the perspective." [IAG2-V2-4]
- **Choosing a camera mode is a burden.** On Sunshine's modes: "this burdened the players with an additional task; they had to decide on the camera angle before they could go into game play." [IA1-2]
- **Motion sickness comes from movement the player did not expect.** "a lot of people who get motion sickness if the camera moves automatically" [IA1-2]. The cause is a camera that "moves in a way that's not expected by the player". [IA4-2] The producer was the "human motion sickness sensor". Miyamoto had any spot that playtesters found sickening fixed. [IA1-2; 3E8-1] Koizumi is modest about the result: "just a camera that makes it harder to get motion sickness." [IA1-2]
- **Spheres help the camera.** On a sphere Mario "can run all he wants without falling or hitting a wall". In flat levels a wall "makes the camera turn around and runs the risk of making the player lost". [KOT] "this type of field is brilliant for camera angles too". [IA4-2]
- **Galaxy 1 accepted being upside down. Galaxy 2 kept the head up.** Hayashida: in Galaxy 1 "we tried to make it look weird and upside down" on the undersides of planets. In Galaxy 2 "his head will, for the most part, be up". [IAG2-V2-4]
- **Cameras were placed by hand.** Koizumi: "several hundred different camera types by hand". [IAG2-V2-4] About 48 camera classes, chosen by priority-ranked camera areas. Examples include a cube-planet camera that "rotates along based on Mario's current gravitational frame" and a fixed-eye camera used "mostly for landings after a launch star". [LW-C]
- **Default camera parameters** [LW-C, not cross-checked against code]: `camint` 120 frames to reach a new position (*inferred* 2.0 s), screen bounds upper 0.3 / lower 0.1 from centre, `fovy` 45°.
- **A 2D-like view is an option.** "if the camera is directly above or straight to the side, you can easily play as if it's a 2D game." [IAG2-V1-1]
- **Some player control remains.** C recentres the camera behind the player. In marked areas the pad moves the camera or toggles first-person. "There may be some areas where you can't change the camera view." [MAN p.18]

### 2.4 Departure and landing (launch)

- **There is no cut between worlds.** Motokura: earlier games would "cut-out and cut-in to a completely different planet". Here Mario "lands on the new one like a gymnast, so the players can enjoy the adventure continuing seamlessly on the next planet." [IA2-2]
- **Sequence from the code** [PET SuperSpinDriver.cpp, partly read; seconds *inferred* at 60 fps]:
  - Wait: 90 frames idle before the swing indicator starts rotating. Capture range 240 units.
  - Capture: a 60-frame blend into the centre. Capture ends 60 frames after the player is within 15 units.
  - ShootStart: a 15-frame blend; the shot begins after 45 frames (≈ 0.75 s wind-up).
  - Shoot: 300 frames by default (≈ 5.0 s), set per object. Motion phase markers at 50 / 230 / 280 frames (loop start, end start, fall), rescaled. Cancelling is ignored for the first 40 frames.
  - CoolDown: 60 frames (≈ 1.0 s).
- **The wind-up is visible and physical.** Standing under the launcher, "the gravity of it pulls him into the center of it automatically", and an inner ring pulls back before the launch. If you don't act within "a few seconds", it lets you go. [WIKI Launch Star]
- **The route is revealed by flying it.** On first use the path is hidden and drawn during the flight. On later uses in the same mission "the path will be visible before he uses it." [WIKI Launch Star] Earlier paths are "marked with a blue streak". [DCH, search snippet only]
- **Flight framing is authored per level.** The object has a count of event cameras for the flight [GDB SuperSpinDriver], and the code takes camera durations from data (*inferred*: authored, not computed). [PET]
- **The launch sound is tied to the music.** Kawamura: "When Mario shoots off from the Sling Star, for example, harp music plays as a sound effect ... in perfect timing with the background music." [IA3-1] The wiki says the main launcher's sound is also synced through MIDI data. **This is not backed by a developer quote.** [WIKI Launch Star]
- **Smaller launchers.** Short-hop variants "do not send Mario flying into space". [WIKI Sling Star] A hold-to-attract point has a "blue gravitational aura"; you let go gently, or act to release quickly. [WIKI Pull Star; MAN]
- **Not found:** the landing animation and the arrival camera, beyond the existence of a fixed-eye camera class used for landings [LW-C].

### 2.5 Visual composition, palette, silhouette

The sources are thin here. Nothing was seen directly.

- **Planets are named and recognised by their outline:** peanut, bean, bitten apple, pear, egg, capsule, star, UFO. [WIKI Good Egg Galaxy]
- **Any shape is allowed if each planet is different.** "as long as each of the planets are different, you can do whatever you want." [Iwata, IA2-4] "it's OK to have a planet in a shape of an apple." [Hayashida, IA2-4]
- **Shape shows function.** "when in doubt, use spikes!" [Motokura, IA2-2] "the fundamental element of Mario is to create form around function". [Miyamoto, IA4-3]
- **One body can have two moods.** Good Egg's start is a disc with a sunny flowered top and a dark underside with dead trees and falling collectibles. [WIKI Good Egg Galaxy]
- **Palette pairs and themes.** Honeyhive is green and leafy, the mirror of an autumnal galaxy. [WIKI Honeyhive] Gateway's metal planets are brown with green cables. [WIKI Gateway] A hub terrace shifts from dark purple to off-white. [WIKI Comet Observatory] Collectibles come in six colours: red, yellow, green, blue, purple and silver. [WIKI Star Bit]
- **Atmosphere:** the starting planet sits "inside a light blue atmosphere". [WIKI Gateway Galaxy]
- **Small bodies make big moments.** "giant boss suddenly appears with a huge roar on a very small planet, I think really has a lot of impact." [IA2-2; attributed to Aoyagi from the page summary, since the extracted line had no speaker]

### 2.6 Sound and music

- **The music follows the game's tempo.** Yokota: "I absolutely wanted to avoid using music that simply played regardless of the game's tempo". A studio tempo "usually is just a little off from the tempo of Mario running", so the orchestra played to a device that ticked "at the exact tempo with the game". [IA3-1] Kondo, on unsynchronised music: "it feels like you are obligated to play the game in time to the music". [IA3-1]
- **Sound effects land on the music's timing.** Kawamura: "the game synchronises MIDI data with the streaming data, and this is used to process the sound effects at the right time." [IA3-1]
- **Music should express the world.** Miyamoto chose the track that sounded "the most space-like", with "no drums used ... at all". [IA3-3] Iwata: he "wanted the music to aid the design to really make the game world come alive." [IA3-3]
- **The tone is cool, not cute.** "if somewhere in your mind you have an image that Mario is cute, please get rid of it." [Kondo via Yokota, IA3-2]
- **Key moments need their own sound.** If an important piece "has the same sound as another piece..., it won't leave an impression". [Yokota, IA3-3, condensed]
- **Effects must have weight.** Miyamoto: "This is the most important action in this game, but this sound makes it sound really weak." Effects should be "completely synchronised with the feel of the game". [IA3-4]
- **Sound can travel to the player.** The first sound plays on the TV and a deliberately slowed second sound plays on the hand speaker: "it makes it feel like the Star Bits actually fly into your hands." [Yokota, IA3-4]
- **Movement has a sound.** The attract-point sound "has an outer space feel to it, and it also feels like you're floating." [Kondo, IA3-4]
- **Mixed while playing, and the music gives way.** "we toned down the sparkling parts in the music so that the sound would fit in better within the game." [Kawamura, IA3-4] "I don't think the sound should ever get in the way of game play." [Yokota, IA3-4] (The agents disagree on whether "3 days" refers to the whole mixdown or one track, so it is left out.)
- **The hub music grows.** The hub theme gains instruments as the game goes on. [WIKI Comet Observatory]

### 2.7 Distinct curious places

- **There is always more on the far side.** "You are also able to discover new things about those planets when you travel to the other side of them." [Shirai, IA2-2]
- **Exploring is rewarded now and then, not always.** In 2D Mario, "in one out of the ten times that you go left, you'd find a little prize." [Miyamoto, IA4-3]
- **Rewards are scarce, which gives them value.** "By raising the value that the coins had in-game, it became worth the effort to try and grab one." [Shimizu, IA2-4]
- **Concrete small places** [WIKI Good Egg, Gateway, Honeyhive, Comet Observatory]:
  - a house with a "Gone…for a long time" sign, entered through a pipe on the far side;
  - a pedestal reward on top of a hollow planet;
  - an inverted spherical room reached through a pipe;
  - a hidden slide behind a waterfall;
  - a vine to a button and a side route;
  - jumping off the hub's edge drops you into a bubble that carries you back;
  - a countdown of remaining goals on the floor tiles.
- **Inviting experiments.** "try spinning in different places to see what happens". Feed the hungry creatures: "What happens when they finally get full …?" [MAN p.21]
- **Falling off is safe.** Falling off the hub returns you rather than punishing you. [WIKI Comet Observatory]

---

## 3. Experiments for FileScape

These are hypotheses, not requirements. Each one would get a requirement ID when it is planned. The numbers are FileScape starting values. Where a Galaxy value exists it is cited, and it is not a target. Units follow `docs/units.md` (s, m, degrees).

**E1. You can see the next destination from the arrival point.** (2.1)
- Change: at the arrival pose on a planet-place, at least one child destination body is inside the view frustum. It is not hidden behind the home body's horizon and covers at least 1.5° of the view.
- Acceptance: Core test over the fixture tree. For every place with ≥ 1 child, a ray from the arrival eye to the nearest child body's centre reaches it without obstruction, and its angular radius is ≥ 0.75°. In the app, an injected arrival screenshot shows a child body with its name label, at UI scales 1.0 and 1.25.

**E2. A visible wind-up before release.** (2.4; Galaxy ShootStart ≈ 0.75 s, PET)
- Change: departure has three phases.
  - Capture: ease to the launcher's centre, 0.5 s.
  - Wind-up: 0.4 s. The body is pulled back along −launch direction by 0.6 m, and an original spiral of light tightens around the launcher.
  - Release.
- Input during the wind-up is ignored. Afterwards there is a 1.0 s cooldown before the next launch.
- Acceptance: probe log of launch phase timestamps. Wind-up is 0.40 s ± 1 frame at 30, 60 and 144 Hz. The pull-back peak is 0.6 m ± 0.05. The spiral visual is present in a screenshot taken mid wind-up. A second launch attempt inside the cooldown is rejected and logged.

**E3. The route is revealed by flying it, then remembered.** (2.4)
- Change: the first transfer between two places draws its arc progressively, behind the traveller. Later visits show the full arc before departure, in a dimmer colour.
- Acceptance:
  - Core: `RouteMemory` marks a pair as known after one complete transfer.
  - Injected: the first-transfer screenshot at 50 % flight time shows the arc drawn to about 50 % (± 10 % of its length).
  - The second departure shows the full arc before release.
- Also check that the existing PLANET-03 bound (≤ 2.7 s) still holds.

**E4. Head-up camera with a limit on how fast it rolls.** (2.3; Galaxy 2's "head ... up", "without sudden jerks"; motion sickness comes from unexpected motion)
- Change: walking on a body, the camera's up vector follows the local surface normal. Its roll rate is capped at 90°/s and it eases in and out. There are no camera modes to choose from.
- Acceptance: injected walk around a 20 m-radius fixture body, a full circuit. The logged camera roll rate never exceeds 90°/s, with no single-frame change > 3°. The player stays within the central 0.3 / 0.1 vertical screen band throughout (borrowing LW-C's bounds as a test range). WALK-04's "no forced rotation" is re-read here as "no rotation faster than the cap". **DJ judges the feel.**

**E5. Landing settles; it doesn't jolt.** (2.4 "lands ... like a gymnast", continuity)
- Change: on arrival the traveller's up vector blends to the destination normal over the last 0.3 s of flight. The camera reaches its walking pose within 0.6 s of touchdown. There is no fade or cut.
- Acceptance:
  - Injected transfer: no frame between release and walking control has a camera position jump > 0.5 m.
  - The orientation error to the destination normal is < 2° at touchdown.
  - Walking input is accepted within 0.6 s after touchdown.
  - The frame-to-frame screen difference has no single-frame spike above the flight median ×3 (no cut).

**E6. Departure sound locked to the beat.** (2.6)
- Change: the ambient place music runs on a known tempo, starting at 96 BPM. The release cue is scheduled on the next beat subdivision (eighth note), and the wind-up stretches or shrinks by ≤ 0.1 s to meet it.
- Acceptance: the audio scheduler log over 20 launches shows the release cue offset from the nearest eighth-note boundary is ≤ 10 ms. The wind-up stays within 0.4 ± 0.1 s. With music muted, the cue still fires and the wind-up is exactly 0.4 s.

**E7. Bodies you can tell apart by silhouette alone.** (2.5)
- Change: `PlanetShape` picks from distinct outlines (for example a sphere, a capsule, a torus, a notched disc or a lobed shape), keyed deterministically from folder identity. Siblings never share an outline class. `PlanetTheme` gives each sibling a distinct hue.
- Acceptance:
  - Core: in the fixture tree, no two siblings share a (shape class, hue bucket) pair, and each is stable across runs.
  - Render: a flat black silhouette pass from the parent's arrival point. A simple outline classifier (area ratio + convexity + hole count) assigns each sibling its intended class with ≥ 90 % agreement.

**E8. The far side always holds something.** (2.7)
- Change: on every planet-place, at least one `SurfaceObstacle`/feature that cannot be seen from the arrival point sits on the hemisphere opposite it. For example, the folder's largest file shown as a landmark, or a quiet pocket with the newest item. It is not marked on the HUD.
- Acceptance:
  - Core: for every place, ≥ 1 feature has its centre > 120° of arc from the arrival point and is occluded from the arrival eye.
  - Injected: walking a straight great-circle path from the arrival point reaches it within 1.2× the body's half circumference.
- Optional: log whether a playtester finds it without being told. That is a test where we watch what the player does, not what they report.

(The "no text needed" idea in 2.1 runs through E1 and E8. It is not an experiment of its own: the check is that none of E1–E8 needs a HUD hint to pass.)

---

## 4. What not to copy

Keep FileScape original. Do not use any of these, in name, shape or likeness.

| Galaxy element | Don't use | Original FileScape equivalent |
|---|---|---|
| Mario, Rosalina, Bowser, Toads, any characters | Any character, mascot or companion species | No avatar character. The user is a first-person walker. Folders and files are the inhabitants. |
| Lumas (star-shaped creatures), Hungry Lumas that become planets | Star-shaped creatures, creatures that eat collectibles and become places | A "dormant" folder that becomes fully shown once you visit it (AERIAL-04's honest discovery) |
| Launch Star (five-pointed launcher, spin-activated) | Star shapes, a five-pointed launcher, a spin or shake to trigger it | A **ring or coil launcher** that comes from the folder-gate language (the circular gates from PLACE), triggered by walking into it plus a key. The wind-up shows as an original tightening spiral of light (E2), not a star ring. |
| Sling Star, Pull Star | Their names and shapes | A short-hop "step-ring" between features on one body. An "attractor beacon" only if needed. |
| Star Bits, Grand Stars, Power Stars, coins, 1-Ups | Star or coin collectibles, scoring, lives | No collectibles economy. The rewards are the files and folders themselves (2.7's "scarce reward" becomes one notable item per place, E8). |
| Star chips assembling a launcher | Collecting shards to build the exit | Exits always exist (a file explorer must never block navigation). An exit can *light up* as its target is listed. |
| Comet Observatory hub, domes, galaxy-selection screen | Their layout and the observatory or comet theme | The existing location hub ("This computer", LOC-01) and the aerial world (AERIAL-01). |
| Galaxy and planet names (Good Egg, Honeyhive, Gateway), the "M" made of collectibles | Names, letter-shaped arrangements | Folder names are the names. Shapes come from folder data (E7). |
| The music (waltz hub theme, orchestral style, harp launch figure) | Melodies, the harp-on-launch motif, the orchestration | An original ambient score. Only the *principle* is borrowed: effects quantised to the music's tempo (E6). |
| Two-screen / Remote-speaker sound trick | Device-specific gimmicks | Possibly stereo or spatial panning from source to listener for a pickup cue (untested, not an experiment yet). |
| Blue launch-path streak | That exact look | A route arc in the destination folder's theme colour (E3). |

Principles that are free to use (ideas, not expression): visible destinations, no text hints, a camera you don't manage, a cap on unexpected camera motion, continuous travel with no cuts, shape shows function, effects synced to the music's tempo, something waiting on the far side.

---

*Evidence status: every observation is sourced, **provisional** (secondary or community sources are marked). Galaxy frame timings are from decompiled code at an assumed 60 fps. Visual and landing observations are thin because no video or stills could be seen. All experiments are **unknown** until run.*

---

## 5. What the research changed in the build (2026-11-11)

What was examined is listed in section 1: official interview text (Iwata Asks, all four volumes of *Super Mario Galaxy* and two parts of *Galaxy 2*), the official manual, wiki and community pages, a decompilation read for launch timing, and essays. **No video could be played**, so no footage timestamps exist; motion was studied from the decompiled launch phase lengths, the manual's move list and the developers' descriptions.

| Observation (source) | What it changed in FileScape's planets | Tried in the build |
|---|---|---|
| You can see where to go, and seeing it makes you want to go there (Shirai, Iwata, IA2-2) | Child folders hang in the sky **above their own launchers**; the parent looms large behind you. Walking towards the world you see brings you to the pad that goes there. | E1 check: a world is in view and named from every arrival (journey, PASS). |
| Text is the last resort; the screen should be enough (IA2-2) | One prompt line that only appears when a key does something ("Space · fly to Garden Photos"); names in the sky; small grove signs. No tutorial text. | The prompt is checked on the pad. |
| A camera you don't manage; sickness comes from unexpected motion (IA1-2, IA4-2, IAG2-V2-4) | A chase camera that follows by itself, swings in behind a running traveller, keeps "head up" with a **roll-rate cap of 90°/s**, rides along rigidly with hops, and turns to the destination during a wind-up. Right-drag only if you want it. | Measured roll ≤ 90°/s (journey); landing re-expresses the scene so nothing jumps. |
| Travel never cuts; you land "like a gymnast" (Motokura, IA2-2) | A launch is one continuous motion: wind-up, arc, touchdown. Because each child hangs at a smaller scale, the traveller **shrinks into the child's scale on the way**, and at touchdown everything is re-expressed in the new world's frame (a floating origin): the screen does not change in that frame. | Landing jump of the traveller on screen: 0.1–0.6 % of the screen height (journey and fixture). |
| A visible wind-up (decompiled ShootStart ≈ 0.75 s at 60 fps) | **0.4 s** wind-up: the bloom launcher's petals close into a bud while the traveller crouches and the camera turns to the target, then they burst open. | 0.32–0.42 s measured. |
| Sound effects locked to the music (Kawamura, Yokota, IA3-1) | Original generative music at 84 BPM per world (key and mode by theme); tempo-locked effects snap to the next 32nd note (≤ 89 ms); the **launch release is aligned to the sixteenth-note grid** by stretching the wind-up ≤ 0.09 s. | SkySynth tests (latency, no clipping, no clicks). Nobody has listened yet. |
| A delayed second sound makes things feel like they arrive (Yokota, IA3-4) | The arrival chord is followed by a softer chime 120 ms later. | Tested in SkySynth. |
| Sound never gets in the way (IA3-4) | Music is moderate (RMS ≈ 0.04) and ducks under a sound-file preview. | — |
| Few buttons; avoid combined inputs (Koizumi, IA1-3) | Move, Space (jump, twirl, launch — the pad is the button), E (look closer), Enter (open). Nothing needs two keys at once. | — |
| The spin gives a small, once-per-airtime boost (wiki; manual) | **One airborne boost** ("twirl") per airtime, forgiving (any time in the air, buffered near the ground), in both worlds via the shared JumpModel; single jump ≈ 1.2 m, jump + twirl ≈ 2.7 m. Its payoff is the overlook. | Apex 2.6 m measured; Core tests on both walkers. |
| Places just to play; fun before it is a game (Koizumi IA1-4, Miyamoto IA4-2) | Small round worlds (16–34 m) you can run round in 15–25 s; bell flowers ring as you pass audio files; lanterns glow. | — (DJ to judge). |
| There is always more on the far side (Shirai, IA2-2) | Groves wrap round the planet, the landmark stands 40° ahead so its silhouette shows over the horizon on arrival. (E8, a deliberate far-side feature, was **not** built.) | — |
| Each planet different; shape shows function (IA2-4, IA4-3) | Eleven themes by what a folder holds (picture meadow, workshop, library, bell flowers, crystal, vault, theatre, wild garden, hearth, quiet moon, veiled world), each with its own palette and landmark silhouette; every file kind has its own readable object (easel with the real picture, lectern, machine, bell flower, chest, rocket, crystals…). Planets are all spheres (E7's distinct outlines were not built). | Gallery and journey screenshots. |

**Deliberate departures from the synthesis:** section 4 suggested no avatar (a first-person walker). The build has a small original traveller, because a chase camera on a sphere needs something to follow, and Galaxy's camera research is about a chase camera. The launcher is a six-petal flower bud (it had five in the first art pass; six keeps it from reading as a star), not a star or a coil; its form says what it does (it closes as it winds up and bursts open as it throws you).
