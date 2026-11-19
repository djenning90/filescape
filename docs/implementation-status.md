# Implementation status

Last updated: 2026-11-19, the fourth playtest follow-up (landmarks and scenery: PLANET-73..75); before that 2026-11-17 (night), after the third playtest follow-up (PLANET-49..66); before that 2026-11-17, the second playtest iteration (PLANET-38..48); before that 2026-11-15, the first public playtest preparation; before that 2026-11-14 (later), the documentation pass; before that 2026-11-14, the third planets iteration (controls, world characters with their music and look, the repository workbench; commits `b4e1eec`…HEAD); before that 2026-11-11 (later), the second planets iteration from DJ's playtest (commits `1ba70a7`…`3ffe1bb`); before that the planetary world (commits `4d7ea48`…`a98fdb4`); before that 2026-11-06, the correctness follow-up on `8c18e8e3f107` (commits `bef9dee`…HEAD); before that the reliability/compact-places iteration (commits `3c838d7`…`4519f18`); before that the exploration pass (commits `91df1c2`…`77849d5`); before that view levelling (CTRL-04, commits `3efd3a3`…`1f61ff1`) and the usability pass (commits `dd0c639`…HEAD); before that the correction and units pass (commits `dbf2bc1`…HEAD) and the walking-world pass (commits `75deacf`…HEAD); the Places pass is 2026-11-04 (`2eabd73`…`bdc0a73`); earlier sections describe 2026-11-03 (commits `ee39c8f`…`5379de4`). Hardware: Lenovo 21RX, Ryzen AI 9 HX PRO 370, **Radeon 890M iGPU** (driver 32.0.22024.19001, 8 GiB UMA carve-out), 87.6 GiB RAM, Windows 11 Pro 26200, on AC power. Evidence files: `docs/evidence/2026-11-03/`. The full raw runs are in `%LOCALAPPDATA%\FileScape\evidence\` on DJ's machine and are not in the repo.

Vocabulary: **Demonstrated** = observed with recorded evidence; **Provisional** = works in limited conditions; **Failed** = observed not working; **Unknown** = not established.

## Fourth playtest follow-up (2026-11-19)

### Hazard World: the rim catch, the long jump, an answer to every press (PLANET-71..72, AD-69)

DJ: "I couldn't get the late rim catch to work. Once falling, the character seemed completely unresponsive. Pressing
Space produced no visible effort." **Root cause:** the app started the fall 0.6 m below the rim (≈ 0.23 s after a slip)
without asking the walker whether a catch was still open, and Space in the first 0.12 s was a coyote jump further out,
so walking the catch was open only ≈ 0.12–0.23 s (running, one frame); with W still held a catch was steered back out;
after one last effort further presses did nothing, and in the fall only the first press in its first 0.6 s answered,
too quietly to hear under the fall. A tapped long jump never crossed a leap.

**How to play them (what to tell a player):**

- **Rim catch:** slip off an edge? **Press Space at once — within 0.4 s of your feet leaving the rim** when walking (about
  a quarter of a second when running). Holding W or letting go makes no difference. You land back on the rim. Too late,
  and the character flails and cries out instead; every press on the way down does that, and the fall starts promptly.
- **Long jump** (the "leap" gaps with amber take-off boards): **walk** at it, press Space on the amber board, then press
  Space again **at the top of the jump, about a third of a second later** — anywhere from about 0.25 to 0.4 s works,
  tapped or held (held, up to about 0.5 s). A plain jump never makes a leap; a running jump still does.

| Area | Status | Evidence |
|---|---|---|
| Rim catch from the first instant, 0–0.40 s walking (0–0.27 s running), W held or not, onto narrow bridges; late jumps at a step still jump (PLANET-71) | **Demonstrated** in Core and in-app | `HazardRecoveryTimingTests` (press time enumerated every 10 ms, walk/run, W held/let go); `planets-hazard`: real presses caught at 0.07, 0.23 and 0.38 s |
| Long jump for taps and holds; plain jump never (PLANET-71) | **Demonstrated** in Core and in-app | twirl window ≥ 0.17 s tapped from every board position; `planets-hazard`: crossed with the twirl at the top and late (0.45 s), the plain jump falls short |
| Every press answered: flail, hop, bump up the fall's path, louder cue (PLANET-72) | **Demonstrated** in Core and in-app; look and sound: DJ | 3 of 3 presses in the walker and in the fall; the cue +3.5 dB over the fall and the hazard score |
| Fall and return prompt (PLANET-72) | **Demonstrated** in-app | uncaught slip falls 0.43 s after it (Core 0.40 s); fall to standing on the parent 3.7 s |
| Checks | Core 711 passed / 0 failed (WSL); behaviour baseline identical; `planets-hazard` **82/0** | `%LOCALAPPDATA%\FileScape\evidence\2026-11-19-hazard-recovery\planets-hazard-2\` |
| Real input `os-planets-probe.ps1 -Hazard -Recovery` (early/mid/late presses, presses in the fall, the long jump and the plain jump on Debris Field) | **Unknown**: written and parse-checked, not run (the coordinator runs real input) | — |

Instrument note: the old in-app probe pressed at 0.15 s, inside the 0.12–0.23 s that worked, so it never saw the dead
window; the probes now time presses from the app's clock (`airSeconds`) and read back what each press did
(`pressAfterSeconds`, `pressWas`).

### Landmark interactions and scenery that answers (PLANET-73..75, AD-70)

DJ: "The bell works and sounds good. I tried the telescope and lighthouse and nothing happened." **Why the telescope
seemed to do nothing (product defect):** its sign "E · Telescope" showed from ≈ 7 m off the overlook's edge, but E only
acted on the 2.4 m platform, reached by a ring of seven stones winding round it (on the Starter Sky the book tower stands
1.5 m beside it); E where the sign said E did nothing. The lighthouse was pure scenery.

| Area | Status | Evidence |
|---|---|---|
| Telescope: sign and E share one reach (≤ 6 m off the edge); "E · up to the telescope · or climb the stones"; E lifts you onto the platform beside it; E there opens the list with the telescope's flare and a caption; the stones still climb; a file in reach keeps E (PLANET-73) | **Demonstrated** in-app; the file-keeps-E case is not exercised on the Starter Sky (no file stands within the reach there; the focus order guarantees it) | `planets-lookouts` 52/0 (runs lookouts-1..3 all 0 failures); `%LOCALAPPDATA%\FileScape\evidence\2026-11-19-landmarks\` (before: `baseline\`) |
| Lighthouse (Hearth; Constellations in the sample): "E or twirl (Space, Space) · light the lighthouse"; two beams sweep 1.5 turns in 3.2 s and fade, the lamp flares, horn and bells in key; opens nothing (PLANET-74) | **Demonstrated** in-app; look and sound: DJ | `planets-lookouts`; `SkySynthTests.Landmarks` |
| Scenery that answers a twirl: mushrooms (bounce, boing), crystals (sway, glow, glassy notes), lamps/lanterns (sway, glow, chime); the nearest within 4.5 m shimmers (light and two motes); its prompt until a kind has answered twice (PLANET-75) | **Demonstrated** in-app (a lantern on the Starter Sky); mushrooms and crystals by code and Core only; the shimmer is subtle by design (two small motes and a soft light; seen at 4 m in `lookouts-3\05`): **provisional** until DJ sees it | `planets-lookouts`; `SkySynthTests.Landmarks` |
| Core | 705 passed, 0 failed (WSL) | `tools/fs.sh test-wsl` |
| Real input (`os-planets-probe.ps1 -Landmarks`): telescope by E from the approach (replaces the flaky stone climb), a piece of scenery, the bell, the lighthouse on Constellations, the zoom step | **Unknown**: written and parse-checked (0 errors), not run here | — |

Where to find them on the sample: the telescope on the Starter Sky's overlook beside the book tower; lanterns on the
Starter Sky (5, the clearest one out past the pads); the lighthouse on Constellations (Starter Sky's child); mushrooms
on Hearth, Chime, Theatre, Wild and Quiet worlds; crystals on Workshop, Vault, Crystal, Wild and Quiet worlds.

### Musical modes: the theme in seven modes, a Mixolydian launcher, arrangements for places (PLANET-79..80, AD-72)

DJ asked for modes studied from two 8-bit Music Theory videos (notes, and what could be reached: titles, descriptions
and chapter lists only, no audio or transcript: `docs/music-modes-notes.md`) and used for original music. Core gained a
mode abstraction (`DiatonicMode`, `Modes`), written sketches (`SketchScore`, `SketchWriter`, `ModeSketches`) and an
offline renderer on SkySynth's own instruments (`SkySynth.RenderSketch`, `SkySynth.Sketches.cs`). Samples and README:
`%LOCALAPPDATA%\FileScape\evidence\2026-11-19-modes\` (rendered by `SKYSYNTH_MODES_DIR=<folder> tools/fs.sh test-wsl
--filter Render_the_mode_sketches`).

| Area | Status | Evidence |
|---|---|---|
| The FileScape theme (motif and cadence, two ornaments so it reaches fa and ti) in all seven modes, only pitch differing; reel Ionian → Locrian, 10.7 s each and a 1.6 s gap (PLANET-79) | **Demonstrated** (measured); by ear: DJ | `SkySynthTests.Modes.cs` (7 tests); `modes-reel.wav` 87 s, silence only in the six gaps; `single/theme-<mode>.wav` |
| Launcher phrase in major and Mixolydian, identical but for ti → te; developed take-off (PLANET-80) | **Demonstrated** (measured); by ear: DJ | `launcher-major.wav`, `launcher-mixolydian.wav` (21 s each), `launcher-takeoff.wav` (30 s) |
| Arrangements: Lydian overlook, Aeolian empty moon, the mode-shift journey (Ionian → Mixolydian at launch → Lydian flight → Dorian landed → Phrygian hazard world) (PLANET-80) | **Demonstrated** offline; by ear: DJ | `arrangement-*.wav` (47–55 s), peaks ≤ 0.50, −26 to −28 LUFS, no silent windows |
| Existing music unchanged; nothing integrated in-game | **Demonstrated** | behaviour baseline and whole-cycle hashes of every score at visits 1–3 identical before/after; Core 707 passed, 26 skipped (WSL); doc coverage 100 % |

## Third playtest follow-up (2026-11-17, PLANET-49..66)

DJ's follow-ups on `6b9b60c` and the staged `a9d2e7e`: the blank Overview and audio gaps (FOLLOW-1/2), then keyboard
focus in Destinations and the README reader, the tutorial's zoom, the telescope, the bell tower and root landmarks,
near and far names, Hazard World moves and terrain, the resize audio glitch, music, relief and sky, and publication
prep. Five parallel streams merged into `main`; decisions AD-62..66. Final build: **`bcde9b33b338`**, staged at `%LOCALAPPDATA%\FileScape\staging-20261117-233012-1305834\` (not installed; the installed build is still `6b9b60c274af`); ZIP `FileScape-bcde9b33b338+4-uncommitted-win-x64.zip` (the +4 are Codex's uncommitted docs).

| Area | Status | Evidence |
|---|---|---|
| Overview draws visible worlds on every route (PLANET-49) | **Demonstrated** | `planets-overview-sample` 9/0, `-hub` 7/0 |
| Audio gaps (PLANET-50): audio thread, startup headroom, instant mute; Pro Audio scheduling and 1 ms timer (margin) | **Demonstrated** in-app and for measured real moves/resizes; DJ's remaining resize glitch **not reproduced** | `planets-audio-gaps` 9/0; real `-Audio` runs: 0 skips, clean Godot mix (WAV) and clean device loopback (`tools/os-loopback-record.ps1`) in five runs incl. 24-thread load and the hub at 2200 × 1250; F3 shows audio gaps for a manual check |
| Destinations take the keyboard while open; typed letters go to the filter; results keys; Enter goes to the best current match (PLANET-51) | **Demonstrated** with real keys | `planets-destinations-keys` 35/0; real `-InputFocus` (J and ☰, after travel) |
| README reader: Expand icon, excerpt preview, wheel scrolls the document at both sizes, keys while reading, Enter/Open said above the reader (PLANET-52) | **Demonstrated** with real input (recording shell); real Windows association **not run** (Notepad was open: it would have added a tab) | `planets-reader` 33/0 at 1.0 and 1.25, `planets-reading` 13/0; real `-InputFocus` 26/0 |
| Markdown links: anchors, relative project links incl. spaces, web links on click; images stay local (PLANET-53) | **Demonstrated** in-app | `MarkdownLinksTests`, `planets-reader` |
| Tutorial zoom-out step (PLANET-54) | **Demonstrated** with the real wheel (fixed: could not finish when the overview opened already showing the sky) | `planets-tutorial` 36/0, real `-Landmarks` |
| Telescope cue on approach (PLANET-55) | **Demonstrated**; real climb passed 1 of 2 runs (the probe's steering wedged by the book stack once) | `planets-lookouts` 29/0 |
| Walkable bell tower rung by a jump-and-twirl under it (PLANET-56) | **Demonstrated** with real keys; the toll by ear: DJ | `planets-lookouts`, real `-Landmarks` (ring once, 11.6° swing, nothing opened) |
| Root landmarks: "E · Show location actions", described rows, Show in Explorer, the twirl arms and Enter commits (PLANET-57, AD-63) | **Demonstrated** in-app (recorded shell) | `planets-landmarks` 68/0 |
| Near/far names grow to a cap and climb clear (PLANET-58) | **Demonstrated** on a root-like fixture and the sample | `planets-near-far-root` 12/0, `-sample` 18/0; `%LOCALAPPDATA%\FileScape\evidence\2026-11-17-nearfar\` |
| A little more relief on ordinary worlds (PLANET-59) | **Demonstrated** (mesh within 6.9 cm of footing, slopes ≤ 18°) | `PlanetReliefTests`, `planets-relief` 7/0 |
| Shooting stars and a 1°/min sky (PLANET-60, AD-64) | **Demonstrated**; feel: DJ | `planets-sky` 9/0; clip `%LOCALAPPDATA%\FileScape\evidence\2026-11-17-sky\sky-clip.avi` |
| Hazard gap classes, timed twirl, recovery twirl, last effort, broken outlines (PLANET-61..63, AD-65) | **Demonstrated** in-app; **provisional** with real keys: the catch worked on the first real try once its reach was 1.5 m (Space 0.14 s after the drop), but on the final build Space 0–0.07 s after the drop was taken as the ordinary jump/twirl and did not save (0 of 3); the last effort, the fall, the return and the retry pass | `HazardMovementTests` 88/88, `planets-hazard` 72/0, real `-Hazard -Recovery` |
| Orchestra +8 % and two second themes (PLANET-64..65, AD-66) | **Demonstrated offline**; by ear: DJ | samples `%LOCALAPPDATA%\FileScape\evidence\2026-11-17-music\` |
| Publication prep: backup tool and bundle, history review and candidate, ZIP check, .NET 10 export | see `docs/publishing.md` | bundle verified; candidate 217 commits, old email 0 |
| Full tier on `49ae684` | **39 of 39 journeys**, Core 703/700 (Windows/WSL) | `%LOCALAPPDATA%\FileScape\evidence\checks\full-20261117-223613\` |
| Final build `bcde9b3` (fixes from the real runs after the full tier) | in-app `planets-tutorial` 36/0, `-tutorial-cues` 55/0, `-lookouts` 29/0, `-overview-routes` 9/0, `planets-hazard` 72/0, `planets-reader` 33/0 ×2, `planets-reading` 13/0; Core 700/0 | `%LOCALAPPDATA%\FileScape\evidence\2026-11-17-final\` |
| Real input on the final build (`tools/os-planets-probe.ps1`) | `-Tutorial` 30/0, `-Play` 15/0, controls 24/0, `-Route` 10/0, `-InputFocus` 26/0, `-Audio` 8/0 (device loopback clean), `-Landmarks` 13/2 (the probe's climb to the overlook; passed in an earlier run), `-Hazard -Recovery` 11/1 (the early-press case above) | `%LOCALAPPDATA%\FileScape\evidence\2026-11-17-os-final\` |

**Instrument defects fixed:** the controls probe clicked a world the drag had turned out of view; verify.sh read the wrong log names for the overview and audio-gap journeys; the real-input
`Wheel` could not send a downward notch; mode blocks from parallel streams lost a closing brace in the merge (caught by
a parse check before any run); the drag helper's guard was swallowed by a comment; the overlook probe jumped at the
wall instead of climbing its stones; the recovery probe pressed by a timer instead of reacting to the drop.

**Merge note:** a merge commit briefly swept Codex's uncommitted README/workflow/quick-start files in; it was amended
before anything else, and they remain uncommitted for review (`backup/working-tree` holds a snapshot).

## Second playtest iteration (2026-11-16/17, PLANET-38..48)

DJ's playtest of `c29785d9b32a`: the walkthrough's target was lost by step two; denser, deeper navigation with trips;
the repository workbench on arrival and a consistent double-click; a developer purpose for the root landmarks; Hazard
Worlds that look hazardous; a sound-off control and more drama in the music; a sketch of the next worlds. Built as six
parallel streams and merged; decisions AD-55..61; playthrough sections in `docs/planets-playthrough.md`.

| Area | Status | Evidence |
|---|---|---|
| Walkthrough cues (PLANET-38, AD-55): lime beacon and ground ring, pinned chevron, horizon chevron, edge arrow ("behind you"), approach pill, key caps for key-only steps, text hint kept | **Demonstrated** in-app; real input: see the run below | `planets-tutorial-cues` 53/0 (fresh prefs; turned away on purpose, behind the curve, approach, advance, Leave); `planets-tutorial` 30/0 |
| Destinations tree (PLANET-39): ~20 px rows, open folders in place (▸, → ←), listed on demand, empty / listing… / access denied shown, deep filter; hover submenu rejected (AD-56) | **Demonstrated** in-app | `planets-navigation` 47/0, `planets-navigation-denied` 4/0, `DestinationTreeTests` |
| Trips (PLANET-40): hop by hop with a Stop panel; stop in the air lands that hop; an unavailable hop stops with the reason; a trip is one Backspace; U unchanged | **Demonstrated** in-app | `planets-navigation` (4-hop trip, stop in the air, Esc in a wind-up, unavailable hop) |
| Dialogs dismissed on commit, Locations "you are here", ★ favourites and repositories (PLANET-41) | **Demonstrated** in-app (reproduced first: `docs/evidence/2026-11-16-navigation/repro-*.png`) | `planets-navigation`; repositories are remembered only for the session |
| Workbench in the arrival view, higher arrival camera easing into the follow camera (PLANET-42) | **Demonstrated**; feel: DJ | `planets-arrival` 25/0; `ArrivalCompositionTests` |
| One click / double-click / Enter rule; double-click opens a file through Open; failed opens say why; twirl previews, never opens (PLANET-43) | **Demonstrated** in-app with the recording shell (defect reproduced first); the real Windows association not exercised | `planets-actions` 27/0 |
| Markdown README rendering and the reading view (R), plain text otherwise; nothing fetched or followed (PLANET-44) | **Demonstrated** | `MarkdownTextTests`; `planets-reading` 13/0 (needs `FILESCAPE_WSL_README`) |
| Root landmarks (PLANET-45, AD-58): solid and placed clear of routes; E panel: terminal here, two path forms, places | **Demonstrated**; terminal start folder verified with spaces, `'` and `&` without opening a window | `RootMarkTests`, `DeveloperActionsTests`, `planets-landmarks` 46/0 |
| Hazard Worlds (PLANET-46, AD-59): islands and bridges round the destinations, ~⅓ walkable, a jump gap, the jet in view on landing, at most 10 pads (the rest moons), "Hazard World" in player text | **Demonstrated** in-app | `HazardWorldTests` (the real walker reaches every launcher); `planets-hazard` 67/0; Debris Field walking p95 16.7 ms |
| Sound off (PLANET-47): toolbar 🔊 Sound / amber 🔇 Sound off, same as N, saved across launches; planets only | **Demonstrated** (output and Master bus measured silent through travel, chimes, the edge warning and a fall) | `planets-sound` 35/0 at 1.0 and 1.25 |
| Music drama (PLANET-48, AD-60): DnB breakdown, build and sub drop; festival climax; orchestral pause and tutti; a 152 BPM phrygian hazard score; the old hazard score kept as Abyss | **Demonstrated offline**; by ear: DJ | SkySynth tests incl. per-bar phrase shape; warning ≥ 7.1 dB over the music; samples `%LOCALAPPDATA%\FileScape\evidence\2026-11-16-playtest-2\music\` |
| Next worlds sketched (AD-61, proposed): Gallery, Reading Room, Arrivals Port (first prototype) | design only | `docs/next-worlds.md` |
| Full tier on `3dc18d1`, then on the final `6b9b60c` | **29 of 29 journeys pass** both times, Core 541/538 (Windows/WSL) | `%LOCALAPPDATA%\FileScape\evidence\checks\` |
| Real input on the staged final build (`tools/os-planets-probe.ps1`) | **0 failures in every mode**: `-Tutorial` 29 checks (incl. the edge arrow with the tower behind, the cue back in view, "here" on the pad, key caps, cleared on Leave), `-Play` (repository arrival with the bench in view, a real double-click on Lantern.sln opened through Open with no jump, a 4-hop trip, U and Backspace after it, Stop clicked mid-trip, Locations + Destinations both open and both gone for the flight), `-Hazard`, controls, `-Route` | `%LOCALAPPDATA%\FileScape\evidence\2026-11-17-os\` |

**Build.** **`6b9b60c274af`** is **installed** in `%LOCALAPPDATA%\FileScape\build\` (export verified and smoke-launched; KD-32 recurred on the first rename and `fs.sh promote` finished it; previous build `c29785d9b32a` kept as `previous-20261117-090231-800122`). Its build-info says "+4-uncommitted": documentation files being edited outside this work (README, publishing, windows-workflow, windows-playtest) — no app code.

**Found by the real-input runs and fixed:** typing "lantern" in J picked Lantern Moth three levels down before Lantern Workshop here (A→Z order): ↓/Enter now land on the best match (whole name, prefix, then the shallowest). Instrument fixes: the state file's JSON escapes line breaks; the probe's solution point avoids HUD panels and waits for the camera to settle; the guard names the window that took the front (Rider took it after file edits several times; each run stopped itself before any input went astray).

**Merge defects found by the full tier and fixed:** from the higher arrival camera a near and a far launcher line up and
one name vanished (home on the Ubuntu root): a pinned name may now stack one slot higher on a longer stem; the
walkthrough's horizon chevron sat above the lesson card from that camera and fell back to a side arrow: it now waits at
the top of the free area. The sound probe used a hazard helper the new terrain removed.

**Instrument defects fixed:** parallel agents' windows (and a person using the machine) took focus and the app ignored
injected input (KD-31): under `--automate` a focus loss now leaves input on and held probe keys are pressed again (a
person's launch is unchanged); the navigation probe now asks for focus loss to be honoured when it tests it. The
shared Starter Sky in `app_userdata` was rewritten by one stream's sample change while others ran (version 5 now).
`tools/doc-coverage.py` found nothing inside a worktree (fixed: repo-relative paths).

## First public playtest (2026-11-15, PLANET-32..37)

DJ's brief: a build to hand someone and watch them learn on their own. Planets is the default (AD-51); learning by playing (AD-54); a black-hole world for system folders (AD-53); three new arrangements and hazard cues (AD-52); ready for GitHub (`docs/publishing.md`).

| Area | Status | Evidence |
|---|---|---|
| A person's launch opens Planets at This computer; arguments still win; automation keeps its defaults; Legacy views menu (PLANET-32) | **Demonstrated** with real input on a clean profile | `os-planets-probe.ps1 -Tutorial`; journeys in every tier still pass (the walk OS probe now asks for `--vis=places` explicitly) |
| Welcome dialog, Don't show again (saved, honoured on the next launch), Help → Learn to play, the 13-step walkthrough, leaving mid-lesson (PLANET-33) | **Demonstrated** with real input | `-Tutorial`: every step completed by the real action; in-app `planets-tutorial`: wrong turn recovered with Back to the lesson, skip, start over |
| The black-hole world: broken shell, safe route, fall and spiral, return to the parent, re-entry; classification by platform and path context (PLANET-34) | **Demonstrated** for the sample; real system folders on fixtures only | Core tests (406 at merge; look-alikes stay ordinary); `planets-hazard` 37/0; real input `-Hazard` 8/0 (safe route without a fall; off an edge; back at the Starter Sky; straight back in) |
| Music: festival (band 2), orchestral (band 3), drum and bass (band 4), hazard identity and cues (PLANET-35) | **Demonstrated offline**; by ear: DJ | see the section below; samples in `%LOCALAPPDATA%\FileScape\evidence\2026-11-15-playtest\music\` |
| README, screenshots from sample data, publication review and fixes (PLANET-36) | **Done**; licence, visibility, history identity and what stays public: DJ's decisions | `docs/publishing.md` |
| Release checks (PLANET-37) | see the build line below | release tier on `b55444b`: 28 of 32 steps passed; the 4 failures were test-instrument issues (the walk OS probe assumed the old default view; walk-perf made no assertion; the real-input walkthrough's jump onto the bench did not re-aim after a sidestep), fixed and rerun on the final build |

**Known:** one Core test failed once in a full run during the music merge and did not fail again in four further runs (not identified; possibly a timing measurement under load). The `.exe` file's own icon is still Godot's (the window and taskbar use FileScape's).

## Music arrangements and hazard cues (2026-11-15, PLANET-35, AD-52)

Three original arrangements of the FileScape theme replace the plainest folder-band ones: **festival** in band 2 (the trails: drop, breakdown, build; a flight out of it builds and the next arrival drops), **orchestral** in band 3 (the deep woods), **drum and bass** in band 4 (the underglow); a **hazard** identity for `WorldKind.Hazard`; hazard cues `HazardWarning`, `FallStart`, `Fall`, `FallReturn` on `SkySynth` and `SkyAudio`. The hub, roots, repositories and band 1 are bit-identical (behaviour baseline audio hashes).

| Area | Status | Evidence |
|---|---|---|
| Arrangements keep the theme, placed per AD-52 | **Demonstrated offline**; by ear: DJ | SkySynth tests: the theme as written on arrival and ≥ 86 % on the next return in every new arrangement and key tested |
| Festival build on flight, drop on arrival (existing calls only) | **Demonstrated offline** | the build's high-frequency energy ×2.7 through a 2 s flight; the drop's first theme note 0.5 s after `Arrive` (≥ 0.15 s, on the beat); no drop after a hop |
| Loudness, clipping, clicks, rapid hops, backtracking, a minute per world, cost | **Demonstrated offline** | −27.1…−27.3 LUFS vs −26.9 reference; music peaks ≤ 0.37; voices ≤ 28/40; 26–27 ms per second of audio (bound 60) |
| Hazard warning (self-throttled), fall, return; room in the mix | **Demonstrated offline**; wired in the app (the hazard world calls them; heard in the real-input runs, judged by ear: DJ) | pulses 12 in 3 s at the edge whether called once or 50× per frame; fall pitch falls ×9, spiral speeds up; every cue ≥ 4.4 dB over the ducked music in every world |
| Samples | written | `%LOCALAPPDATA%\FileScape\evidence\2026-11-15-playtest\music\` (README.txt) |

**Unknown:** nobody has listened yet.

## Documentation and readability pass (2026-11-14, later)

Every hand-written type now has a header saying what it owns, why FileScape needs it and what a change must preserve (340 of 340, was 277 of 339). Methods are documented where the reasoning is not obvious (layout maths, camera and input, traversal, scanning, world transitions), and tuning values are named with units next to their use. Values with no recorded reason say "(rationale not recorded)". Conventions for new code: `docs/code-documentation.md` (linked from CLAUDE.md).

**No behaviour change, checked:** the behaviour baseline (every layout, composer, walker trajectory and an audio hash, round-trip floats) is bit-identical before and after; the overview camera trace (`--automate=planets-camera-trace`, `--fixed-fps 60`) is identical for opening and selecting, and the landing flash has the same shape (that phase depends on the audio clock); an adversarial review of 3,390 changed code lines found nothing; full tier 22/22 on `40f8dfb`. The installed build is still `1533fdf17515` (the pass changes no behaviour, so there was nothing to re-export).

**Possible product defects noticed during the pass (documented, not changed):** `WorldExplorer` routes the Context priority to the background queue; `PlanetWorld.Walk` FlightCamera's 12 m warp distance is overwritten on the next line; `AvatarRig.Land` ignores its impact argument and `SetFlying` assumes 60 Hz; `PlanetVisual` tries thumbnails up to 24 MiB but reads at most 8 MiB; the experimental `Planetoids.PlaceOn` does not clear a guided arc; the workbench's card stagger stops an eighth card or later just short of full height.

## Planets, third iteration: controls, world characters, the workbench (2026-11-14)

DJ's brief: natural camera controls (left-drag, click on release, a focus bug, stationary keyboard look); music and atmosphere per kind of place (hub, Windows volume vs WSL distribution, depth bands, repositories) with more bounce once you set off; one playful move that does something useful; a playable route; a sample route for any machine. Requirements PLANET-24..31; decisions AD-47 (controls), AD-48 (world character), AD-49 (music), AD-50 (workbench). Playthrough: `docs/planets-playthrough.md` § Third iteration. Evidence: `docs/evidence/2026-11-14-planets-3/` and `%LOCALAPPDATA%\FileScape\evidence\2026-11-14-planets-3\`.

**Build.** **`1533fdf17515`** is **installed** in `C:\Users\David\AppData\Local\FileScape\build\` (promoted; previous builds kept: `previous-20261114-234116-197776` = `56c79e5`, `previous-20261114-232228-183225` = the old `217a37f7ffa9`). KD-32 did not recur on the first rename this time. The second rename failed once and left `build\` missing until `fs.sh promote` finished the job; export and promote now put the previous build back if that happens.

| Area | Status | Evidence |
|---|---|---|
| Left-drag look (surface and overview), click on release (6 px), a drag never selects, double-click kept (PLANET-24) | **Demonstrated** with real Windows input | `os/controls-1533fdf.log`: 24 checks, 0 failures (drag from a world selects nothing; click selects; double-click launches; overview orbit) |
| Camera input after UI focus changes (PLANET-25) | **Demonstrated** | Defect **reproduced first** on `d2695b8` (`os/repro-d2695b8.log`: a right-drag with the list's filter focused changed 1.5 % of the view vs 60 %); fixed: 93° after a toolbar click, the list open, after dismissing it, the overview round trip and a focus loss (to the probe's own window, never Alt-Tab) |
| ← → turn the camera standing still and while running (PLANET-26) | **Demonstrated** | ← 102°, traveller moved 0 m; W + → 44°, 5 m; help and hint text updated |
| World character (PLANET-27) | **Demonstrated** | Core tests (hub, roots, repository by `.git` folder or worktree file anywhere, depth bands, start folder); route probes |
| Music per character (PLANET-28, AD-49) | **Demonstrated offline**; by ear: DJ | SkySynth tests (tempo 94–132 BPM per identity, cues on the grid at every tempo and across changes, 8 worlds in 2 s within the voice budget, revisits develop, identities differ); WAVs + README in `…\2026-11-14-planets-3\music\`; `route-reel.avi` (live, in-app) |
| Look per character (PLANET-28) | **Demonstrated** (by eye: DJ) | hub dawn; Windows pane gateway + blue aurora and WSL terminal monolith + amber aurora (the Windows look photographed with a probe-only override: evidence runs never enter C:\); band waymarkers and skies; repository commit graph |
| The workbench (PLANET-29, AD-50) | **Demonstrated** with real input | jump on (a tapped hop or a held jump; walking does not; Core test) → six key places rise as named cards and a panel; spin → README preview; E and click; nothing opened with Windows (0 shell requests) |
| Playable route (PLANET-30) | **Demonstrated** | real keys/mouse on the installed build: Starter Sky → Lantern Workshop (J-list typed), steered to the bench, jump, spin, E, src, back home (`os/route-1533fdf.log`); in-app: This computer → Ubuntu → the WSL fixture's real git repository → docs → home |
| No regression (PLANET-31) | **Demonstrated** | full tier on `56c79e5`: **22 of 22 steps pass** (`full-56c79e5-summary.txt`); smoke on `1533fdf` 5/5 |
| Review | 24 confirmed findings fixed (3 refuted) | adversarially verified 4-dimension review (input, world, bench, probes) |

**Instrument defects found and fixed this iteration (not product defects):** (1) the planets OS probe's Alt-Tab brought another app (DJ's Codex window) forward and the next drags and keys went there; the probes now never Alt-Tab, check that FileScape is in front (and under the cursor for clicks) before every input, release anything held on failure, and use an Alt-free focus. (2) The %TEMP% Windows fixtures had been emptied (2026-11-12, likely temp cleanup): recreated. (3) The places probe waited for an exact tree version with no timeout and hung on a release build; it now waits for the place to show what the tree says (the app's own test) and is bounded.

**Open / for DJ's judgement:** the tune and mix by ear (drums' timbre, whether the repository groove is memorable rather than busy, Quiet/Veiled folders deliberately without kick/snare until the 3rd visit); the look of each character by eye; whether ← → as camera turn feels right for arrow-key walkers (A/D still strafe); the workbench's feel (0.7 m deck, card ring); gateway and monolith do not block walking.

## Planets, second iteration from DJ's playtest (2026-11-11, later)

DJ's playtest brief: launchers easy to find from arrival (home on the Ubuntu root took a while), labels that belong to what they name, no artificial horizon and smaller distant worlds, a Universe Sandbox–style overview with landing and return, livelier music with a returning melody, and samples to judge by ear. Requirements PLANET-19..23; decisions AD-44 (launcher rings), AD-45 (pinned names), AD-46 (overview), AD-42 amended (music). Playthrough: `docs/planets-playthrough.md` § Second iteration.

**Codex's untested draft** was inspected before editing. Kept: the sky shader without the horizon band, fog off, SignLayer's fixed-anchor signs, the T key, the overview and distant-world hooks (rewritten). Replaced: the composer's 57° launcher band (ring layout instead), Label3D launcher names (2D signs pinned with a stem instead, so overlap and occlusion are handled in one place), the input draft and the overview file (rewritten; it did not compile).

| Area | Status | Evidence |
|---|---|---|
| Launchers on rings round the arrival, read like a clock; the way back at 6 o'clock; up to 28 pads, the rest as moons (PLANET-19) | **Demonstrated** | Core tests (Ubuntu-like root: 21 folders incl. home on rings; crowded world: no two worlds share a bearing); planets-flows: Constellations 28 pads on 3 rings; Ubuntu root (`\\wsl.localhost\Ubuntu-24.04\`): 21 launchers on 2 rings, **home 9 m from arrival, found by turning ~100° and walking 8.8 m**, prompt "Space · fly to home" |
| Names pinned to pads/worlds; hidden when the planet blocks them; one name per destination; selected/pointed-at wins (PLANET-20) | **Demonstrated** | planets-flows: a 120-frame full camera turn, every shown name within 0.00 px of its anchor, none shown for an occluded pad, no overlaps, the selected destination named exactly once |
| Sky without the artificial horizon or fog; distant folder worlds small and unnamed (PLANET-21) | **Demonstrated** (look: DJ to judge) | screenshots; up to 120 distant worlds at 320–880 m |
| Overview (T): known hierarchy as nested 3D systems, listed / partial / unknown shown apart, visited rings, orbit/pan/zoom, select, zoom in, land via the navigator (history), Esc back to where you stood (PLANET-22) | **Demonstrated** | UniverseLayout tests (5); planets-flows: 62 worlds (51 listed, 11 unknown), click-select Star Archive\2025, Enter lands (history +1), Backspace back, T then Esc returns to the same spot |
| Music at 108 BPM, an original 8-bar theme returning every 16 bars with variations; launch cues still on the beat (PLANET-23) | **Demonstrated** (by ear: DJ to judge) | SkySynth tests; samples `%LOCALAPPDATA%\FileScape\evidence\2026-11-11-planets-2\music\` (`before\`, `after\`, README.txt) |
| Review | 17 confirmed findings fixed | adversarially verified review of the iteration (commit `7ce2658`) |

**Full tier on `7ce2658`: 20 of 20 steps pass, 680 s**. Planets 39/0 at 1.0 and 1.25; planets-flows 26/0; planets fixture 15/0; **planets-discover on the Ubuntu root 4/0** (home 9.0 m from arrival, a 100° turn, an 8.9 m walk, "Space · fly to home"). Walk 101/0. Places, scan, navigation, map, planetoids, aerial limits, explore and project all 0 failures. Core 325/20 (Windows), 327/18 (WSL). Trying the build turned up one defect: on a freshly opened root the planets left "You are in" empty, because the HUD was set before the root was listed. It is fixed in `d2695b8`, and the discover probe now checks the breadcrumb (passes on the dev build: "This computer › Ubuntu-24.04"). **Smoke tier on `d2695b8`: 4 of 4 pass** (explore 29/0, project 19/0, planets 39/0, planets-flows 28/0). Summaries: `docs/evidence/2026-11-11-planets/checks/full-7ce2658-iteration2.txt` and `smoke-d2695b8-iteration2.txt`. Screenshots and logs of the three flows: `docs/evidence/2026-11-11-planets/iteration2/`.

**Build.** **`d2695b807dc0`** (clean tree) was exported, verified and smoke-launched in `C:\Users\David\AppData\Local\FileScape\staging-20261111-081426-876800\`. Promotion was refused again (KD-32: Windows "Permission denied" moving `build`; nothing was closed), so the installed build is still `217a37f7ffa9`. Launch: `…\staging-20261111-081426-876800\FileScape.exe -- --root=sample --vis=planets`.

**Open:** the feel of ring spacing on very small worlds and of the overview's camera speed are unknown until DJ plays; decorative objects untouched as asked; promotion into `build\` is still refused by Windows (KD-32), so the new build is staged, not installed.

## The planetary FileScape (2026-11-11, overnight)

DJ's brief: a playable planetary FileScape, a spiritual descendant of *Super Mario Galaxy* with original art, sound and interaction, on the shared session, alongside the flat world. Research: `docs/galaxy-research.md` (what was examined, what it changed). Requirements: PLANET-10..18. Decisions: AD-39..43. Guided first playthrough: `docs/planets-playthrough.md`. Evidence: `docs/evidence/2026-11-11-planets/`.

**Build.** **`a98fdb4696c6`** (clean tree) is verified (exported, files and hashes checked, smoke-launched) in `C:\Users\David\AppData\Local\FileScape\staging-20261111-065057-821915\`. It is **not promoted** into `build\` (KD-32): Windows refused to move the current `build` folder although no FileScape was running and no file in it was open; nothing was closed. The installed build is still `217a37f7ffa9`. Launch the planets directly from the staging folder (below) or run `tools/fs.sh promote staging-20261111-065057-821915` once the folder is free.

**Launch:** `C:\Users\David\AppData\Local\FileScape\staging-20261111-065057-821915\FileScape.exe -- --root=sample --vis=planets` (or start it plainly and click **✦ Planets (V)**: from the sample data it opens the Starter Sky).

| Area | Status | Evidence |
|---|---|---|
| Research (Iwata Asks ×4 volumes + Galaxy 2, manual, wikis, decompilation, essays; no video could be played) | Done | `docs/galaxy-research.md` (sources, observations, experiments E1–E8, what changed) |
| A world per folder, themes, landmarks, groves, launchers, sky (PLANET-10) | **Demonstrated** | Core tests (21 composer tests), journey screenshots of 7 distinct worlds |
| Walking all round, jump + twirl in both worlds, camera with capped roll (PLANET-11) | **Demonstrated** (feel: unknown) | SphereWalker edge tests (±Y, antipodes, frame-rate), JumpModel tests, journey: 12.8 m walked, apex 2.6 m, roll ≤ 90°/s |
| Launch travel: wind-up, arc, seamless landing, warp (PLANET-12) | **Demonstrated** | wind-up 0.32–0.49 s, flight 1.5–2.4 s, landing frame jump 0.1–0.6 % of the screen height; recorded reel watched frame by frame |
| Inspect, preview, open real files (PLANET-13) | **Demonstrated** | picture, README text and WAV previews; Enter recorded by the test shell |
| Direct routes (PLANET-14) | **Demonstrated** | J list of 1,501 entries with filtering; Go to path warps |
| Starter Sky (PLANET-15) | **Demonstrated** | SampleSky tests; every journey runs on it |
| Large, empty, linked, incomplete, WSL (PLANET-16) | **Demonstrated** | 1,500-file archive (64 objects + piles); Quiet Moon; junction followed; WSL fixture with Notes/notes and symlinks |
| Switching worlds keeps folder, selection, history (PLANET-17) | **Demonstrated** | journey: V to Places and back |
| Responsiveness on the Radeon 890M (PLANET-18) | **Demonstrated** (dev build) | flight frames max 16.7 ms; walking p99 18–25 ms; planet builds 3–17 ms on the main thread (ground on workers) |
| Review | 27 confirmed findings fixed | an adversarially verified 5-dimension review (travel, session, Core, threads, safety): see the commit `89a83cb` |

**Checks on the staged builds** (`docs/evidence/2026-11-11-planets/checks/`):

| Check | Result |
|---|---|
| Core tests | Windows 309 passed / 19 skipped (`63dfed0`); WSL 313 passed / 17 skipped (HEAD) |
| Smoke tier (`b714751`+) | explore 29/0, hub → project 19/0, planets 37/0 |
| Full tier on `63dfed0` | 18 of 20 steps passed: planets 38/0, planets fixture (Wayfarer + WSL) 15/0, places, scan, navigation, map, planetoids, aerial limits, explore and places at 1.25 all 0 failures; **walk 99/2** (KD-31, intermittent, not reproduced in 3 reruns); planets at 1.25 37/1 (a Backspace wind-up of 0.25 s, by design then; all wind-ups are now 0.4 s) |
| **Full tier on the final `a98fdb4`** | **20 of 20 steps pass, 606 s**: planets 39/0, planets at 1.25 39/0, planets fixture (Wayfarer + WSL) 15/0, walk 101/0, explore 29/0 (1.0 and 1.25), places, scan, navigation, map, planetoids, aerial limits, project all 0 failures |
| Recording | `%LOCALAPPDATA%\FileScape\evidence\2026-11-11-planets\planets-reel.avi` (1280×720, 30 fps, with sound; 0 failures) |

**What changed after trying the build** (each from a screenshot, a measurement or a recorded reel): the camera's look is smoothed and keeps the traveller in frame (the traveller left the frame at launch); the flight camera travels with the traveller as an offset (it lagged at 30 m/s); the traveller keeps its flight scale (it popped to size at touchdown); the camera rides along with hops (it rolled behind); the wind-up turns the camera to the destination (the return flight came at the lens); the atmosphere shell only on worlds in the sky (it hazed the view from inside); no self-shadowing ground (blocky acne); grove signs small and close (they filled the screen); planet ground built on worker threads and prefetched (60–220 ms hitches); an establishing shot for the first world; six-petal launchers (five read as a star); a deeper Library palette; a slim flight trail (it read as a beam); +5 dB (the mix measured ≈ −28 dBFS); remembered routes; hover names; easels read pictures by themselves only in the Starter Sky (FILE-02).

## Correctness follow-up on build 8c18e8e3f107 (2026-11-06)

A ChatGPT review independently confirmed the executable hash and the 217/220 Core test counts of `8c18e8e3f107`, and named three issues. The review's aerial-limit finding came through chat: absence from repository history did not mean it was done, and it was not done.

Requirements: PROM-05, AERIAL-07, TEST-03, TEST-04. Decisions: AD-36..38. Evidence: `docs/evidence/2026-11-06-followup/`.

**Build.** **`217a37f7ffa9`** (clean tree) is **installed** at `C:\Users\David\AppData\Local\FileScape\build\` (FileScape.exe and FileScape.console.exe). No FileScape was running when it was exported. The previous build `8c18e8e3f107` is kept as `previous-20261106-131840-516900`.

| Item | Status | Evidence |
|---|---|---|
| Pin identity (PROM-05, AD-36) | **Demonstrated (Core).** `Notes` and `notes` under WSL and POSIX are separate pins through save, reload, prominence and unpin. Windows paths compare without case. `\\wsl$\`, distro-name case and a trailing separator are one folder. | 16 new tests (`ProminenceAndProjectTests`). Not exercised in the app: pinning two case-variant WSL folders by hand (the fixture has none). |
| Aerial limit (AERIAL-07, AD-38) | **Demonstrated (Core and exported app).** The current place, its ancestry and its folders are kept under the budget. Past the budget, the aerial view is withheld and landing recovers in the place. | 3 Core tests: the current place is outside breadth-first (premise asserted), a wide current place, and the way down exceeding the budget. On the installed build at budget 24: 10/0, Harbour kept with 3/3 of its folders, a safe landing over open space. At budget 8: 11/0, the chip names the limit, T lands in Harbour. The old code's crash was not reproduced in the app: the old build lacks `--test-max-islands`. |
| verify.sh exit handling (TEST-04, AD-37) | **Demonstrated (test doubles).** | `fs.sh check harness`: 14 paths, 12 failures counted with the right reason, 2 s (`checks/harness-selftest.txt`). |
| Right-hold real-input failure (TEST-03) | **Instrument race reproduced.** The original occurrence is unconfirmed but consistent with it (KD-28). | Below. |
| Shutdown | **Unchanged:** no hang reproduced on the tested routes. `FileScape.console.exe` is a demonstrated alternative launcher; the original cause remains provisional. | — |

**The old harness, judged on the same test doubles:**
- The real-input count could never pass: a clean result read as "0 1" failures.
- It never read the exit code.
- The recording was never judged.
- A journey that exited 0 with an empty or assertion-free log passed. The `be3c673` full run reported planetoids as passing with 0 assertions.

So the release tier's earlier "real OS input" lines were not evidence either way; the 29/0 results came from separate runs.

**Right-hold investigation.** The original failure (`os-input/original-failure-ui1.txt`) was the first right-press of its session, and its check recorded no values.

- **Repetition** (`righthold/`):
  - 20 cycles at each UI scale;
  - then 6 launches × 3 cycles at each scale, focusing on first presses;
  - 76 cycles in all.
- **Result:** the original condition (one sample 150 ms after the press) was false in 3 of the 76 cycles: twice on a first press and once on a third.
- **Cause, from the state file's new timing fields** (two of the three cases; the first 40-cycle run predates the fields):
  - the false sample was written *before the app began the look* (app 10313 < 10332 ms, 13279 < 13296 ms);
  - no long frame was near the press (the last one over 50 ms was at start-up, about 0.46 s);
  - the app writes its state every 150 ms or more, so a single sample 150 ms after the press can predate the app's reaction.
- **Product failures** (no capture or release seen in a fresh snapshot within 1.5 s): 0 of 76.
- **Fixed check:** waits for a snapshot written after the press and records its values. Real input then passed 29/0 at UI 1.0 and 1.25 on the staged build, and 29/0 at UI 1.0 on the installed build. Capture was seen at 154–159 ms.

| Check on `217a37f7ffa9` (installed) | Result |
|---|---|
| Smoke tier | 86 s, 0 failing steps (Core tests: Windows 233 passed / 18 skipped, WSL 236 / 15; explore 29/0; hub → project 19/0) |
| Aerial limit at budget 24 / 8 | 10/0, 11/0 |
| Real OS input at UI 1.0 | 29/0 |
| Harness self-test | all 14 paths as expected |

Not run in this follow-up: the full and release tiers (by design; the brief asked for targeted evidence).

## Iteration: reliable operation, faster verification, intimate places, purposeful exploration (2026-11-06)

**DJ's feedback, from use.**
- **Working, and kept:** doorway names, the looks, and the planetoid direction.
- **Asked for:**
  - closing must end the launch command;
  - verification felt close to an hour;
  - This PC gave "You can't open this location";
  - "not listed yet" places did not progress;
  - projects and everyday places were hard to find;
  - places felt too large, forcing implausible speeds;
  - the horizon felt too close;
  - planet transfers were too slow and passive.

**Earlier reviews.** No outstanding aerial or planetoid review corrections from ChatGPT were found in the repository or its history; the last recorded ChatGPT review (six corrections) was delivered on 2026-11-05 (above). The planetoid handedness and distortion findings named in the brief were established and resolved here.

Requirements: SHUT-01/02, TEST-01, LOC-06, LIST-01..03, PROM-01..03, DEV-01..03, PLACE-14, HORIZON-01..03, PLANET-03/04. Decisions: AD-28..35. Evidence: `docs/evidence/2026-11-06-iter/`.

**Build.** **`8c18e8e3f107`** (clean tree) is **installed** at `C:\Users\David\AppData\Local\FileScape\build\FileScape.exe`, next to `FileScape.console.exe`. No FileScape was running when it was exported. The previous build `77849d512a82` is kept as `previous-20261106-065052-486216`.

**Verification** (summaries in `docs/evidence/2026-11-06-iter/checks/`):

| Check | Result |
|---|---|
| Core tests, Windows | 217 passed, 18 skipped (POSIX-only tests) |
| Core tests, WSL | 220 passed, 15 skipped (Windows-only tests) |
| Release tier | 631 s |
| Release tier failures | Places at 1.25 (fixed below), and real-input runs that failed in under a second because `verify.sh` did not copy the probe's helper script (instrument defect, fixed) |
| Smoke tier on the installed build | 85 s, 0 failing steps (explore 29/0, hub → project 19/0) |
| Real OS input on the installed build, UI 1.25 | 29/0 |
| Real OS input on the installed build, UI 1.0 | 28/1 at first ("right-hold captures the mouse"), 29/0 on the rerun. Follow-up: an instrument sampling race reproduced 3 of 76 cycles; the original is unconfirmed but consistent (KD-28) |
| Places at 1.25 after the probe fixes | 0 failures |
| Planetoids | 9/0 |
| Recording | 199 MB |
| Before/after transfer recordings | `%LOCALAPPDATA%\FileScape\evidence\2026-11-06-iter\final\planetoid-transfer-{before,after}.avi` |

- **Places at UI 1.25 had never run before.** It failed the same way on the pre-compact build, because the Places probe injected unscaled clicks. These are instrument defects, fixed in two places.
- **Performance, quiet machine (walk-perf):** plaza 6.92 ms mean, GPU 6.06 ms (previous pass 6.79 ms), so the horizon marks and thinner fog cost no measurable time. Low quality: 2.38 ms.

**Verification time:**

| Run | Before | Now |
|---|---|---|
| Functional suite | 570 s | `full`: 441 s of measured steps, with Places at 1.25 short-circuited by its then-defect; ≈ 490 s expected with it passing. It also covers more than before (the project journey and Places at 1.25), so the reduction here is modest. |
| Smoke | — | 85–90 s |
| Fast | — | ≈ 30 s |
| Release, including real input and the recording | ≈ 14–17 min | 631 s, with the real-input runs short-circuited |

- **What remains costly** is the journeys themselves (walk 88–92 s, places 50–70 s), run one at a time because they share the screen and focus.
- **Repeats** of unchanged steps and exports were the hour DJ saw; the tiers and `fs.sh stage` (≈ 20 s) avoid them.

### Closing (SHUT-01/02, AD-28)

- **Reproduction** (instrument: `close-mine.ps1` sends WM_CLOSE only to the instance started with a unique marker): no hang was reproduced.
  - **Routes:** PowerShell, WSL interop, `tools/fs.sh run`, and the Godot GUI binary.
  - **States:** idle, a WSL listing, `/proc`, `/sys`, flight, the native picker open, a listing stalled for 120 s (`--test-list-delay-ms`), and after Reveal in Explorer.
  - **Result:** each process exited 0.2 s after close, and each wrapper returned within 1.2 s.
- **Likely cause (provisional):** `FileScape.exe` is a GUI program. PowerShell runs its next command 0.01 s after launch, then Godot's log lines print after the prompt, so closing leaves no fresh prompt.
- **Fix:**
  - orderly shutdown with an `[exit]` report;
  - `FileScape.console.exe` as the waiting launcher (PowerShell returned 0.8 s after close, exit code 0);
  - a console notice from `FileScape.exe`;
  - editors started without inheriting handles.

### Verification time (TEST-01, AD-29)

The previous sequence, run on one build without real input or the recording, took **570 s**:

| Step | Time |
|---|---|
| Tests | 19 s |
| Walk, 100% and 125% | 92 s each |
| Explore, 100% and 125% | 51 s and 49 s |
| Places, connected and tree | 70 s and 69 s |
| Scan | 6 s |
| Navigation | 42 s |
| Map journey | 16 s |
| Walk performance | 30 s |
| Planetoids | 33 s |

The new tiers are in `docs/windows-workflow.md`; their measured times are below.

### This PC (LOC-06, AD-30)

- **Open… (Ctrl+O, L)** is the in-app chooser, with **⌂ This computer** first; Browse… opens the Windows picker.
- **Go to path** accepts "This PC" and its aliases.
- **`--root=hub`** starts at the hub.
- **Bugs caught by the new journey and fixed:**
  - the hub check had first landed in "Scan as tree";
  - Ctrl+L kept the previous text.

### Listing ahead (LIST-01..03, AD-31)

- **Order:** the way back and destinations first, then up to 48 further folders per visit in idle time, one at a time.
- **Never ahead:** `/proc`, `/sys`, `/dev`, `/run`, `/mnt` and others, each with its reason as the gate's note; no context on shares.
- **States:** queued and listing-now are separate.
- **Evidence (Core):** a cold fixture shows every place starting unlisted, the context bound, and `/proc` entered with nothing listed ahead.
- **Not shown in the app:** progress over time on a slow share.

### Prominence and projects (PROM, DEV, AD-32/33)

- **Marks:** ⌂ homes (Linux `/home`, user homes, the Windows profile), ◆ known folders (Downloads, Pictures… as Windows reports them), ⚙ projects and folders of projects, ★ pins (P).
- **Where they show:** sign priority, a gate light, Destinations rows and the card; nothing is reordered.
- **Project actions:** R reads the README as inert text; the card offers the editors this computer has, and always asks first.
- **Journey evidence (`--automate=walk-project`):** hub → Ctrl+O chooser → "This PC" → Projects (lighthouse ⚙, notes-only unmarked) → into lighthouse → README → editor confirmation, cancelled → pin and unpin → Open… → This computer → Backspace.
- **Not demonstrated:** launching an editor, which would open DJ's editor. Detecting VS Code on this machine is not reported.

### Compact places (PLACE-14, AD-34)

Measured in Core with the real walker, compact at 5.5 / 11 m/s against spacious at 8 m/s:

| Journey | Spacious at 8 m/s | Compact walking | Compact running |
|---|---|---|---|
| Typical place, nearest gate | 48 m, 6.1 s | 27 m, 5.2 s | 2.8 s |
| Typical place, farthest gate | 47 m, 6.0 s | 30 m, 5.7 s | 2.9 s |
| Wide place, nearest gate | 58 m, 7.4 s | 32 m, 6.1 s | 3.2 s |
| Wide place, farthest tier | 97 m, 12.3 s | 72 m, 13.3 s | 6.8 s |
| Four Ups in a row | 19.7 s | 15.7 s | — |

- **Way back:** 7.8 m (was 6.9 m).
- **Screenshots:** eye-level shots of ordinary, wide, empty and project places in both layouts are in `layout/`.
- **Tests pinned to the old layout:** the historical traversal and golden-walk tests now name `PlaceParams.Spacious`, because they guard earlier claims.
- **Changed expectation:** the default-speed test expects the new 5.5 / 11 m/s.
- **Fix in both layouts:** tier rings were too narrow, so a third tier was unreachable.

### Horizon (HORIZON-01..03, AD-35)

Silhouettes stand one level beyond the vista beacons, haze shows deeper counts, and unknown places are veiled. The marks are bounded at 220 and hidden from the air; fog is a little thinner. The look is for DJ to judge.

### Planetoids (PLANET-03/04)

- **Handedness:** a mirrored chart was found and fixed, with a test.
- **Distortion:** stated and tested.
- **Orchard:** a composed folder-place on a 48 m world, with walking, jumping, looking, picking and a guided transfer.
- **Transfer time:** **2.23 s** against **13.56 s** on the ballistic arc.

## Exploration pass: a readable world, an unobstructed view, flight that explores (2026-11-06)

DJ liked the place concept and the ring of gates, but found:
- a permanent legend hiding about a quarter of the view, and a large Destinations panel;
- no familiar file-type cues;
- most names hidden until aimed at;
- stairs that looked like transparent boxes;
- a jump that could be higher;
- flight that showed only the current room;
- levelling that fought looking down from the air;
- vertical flight that "felt wrong".

**Build.** **`4a7b9ce6fdf6`** (clean tree) is staged and verified at `C:\Users\David\AppData\Local\FileScape\staging-20261105-150710-406825\FileScape.exe`.
- **Not promoted:** DJ's own FileScape was running from `build\` throughout, and the export never replaces a running build (AD-21).
- **To install:** close FileScape, then run `tools/fs.sh export` (or run the staged exe directly).
- **Installed (`build\`):** still `e03dd62adb62`. Previous builds `4b030e39b93a` and `2dc4ae8c3234` are kept.

Requirements: VIEW-01..04, ICON-01, LABEL-01, WALK-11, JUMP-04, FLY-01..05, AERIAL-01..06, PLANET-01/02. Decisions: AD-24..27. Evidence: `docs/evidence/2026-11-06-explore/`.

- **View (VIEW-01..04).**
  - The key is off by default in Places (K or the Key button).
  - Destinations is a drawer: J opens it with a filter focused (↑/↓ choose, Enter go, Esc close); the ☰ button opens it without taking the keyboard; a mouse pick hands the keyboard back.
  - The inspector is a card shown only while something is selected, with Details ▸.
  - The status strip shows only while scanning; the place bar is two lines; Quality moved into Settings.
  - The world now fills 82 % of the window at 1.0 and 75 % at 1.25 (before: roughly half).
- **Icons (ICON-01, AD-24).** Original SVG silhouettes, rasterized at start-up. They appear as badges on tower faces, above category signs, and in signs, Destinations and the card. `FileIcons.For(node)` is the extension point for previews.
- **Names (LABEL-01, AD-25).** The screen-space `SignLayer` replaces the overhead 3D gate signs. It shortens names, stacks them with leader lines, and never overlaps a sign or a HUD panel. 10 of 10 gates are named at the arrival point (before: about 1–3); a 240-gate place shows 40 at once.
- **Stairs (WALK-11).** Terrain uses a solid, lit shader. The cause was that walkable boxes were drawn with the gate frame shader: near-black metal with glowing edges.
- **Jump (JUMP-04).** A held jump peaks at 1.2 m (was 0.83 m), with an apex hang; a tap gives a short hop. A head-clearance check was added (none existed before).
- **Flight (FLY-01..05, AD-27).**
  - Levelling on release happens on foot only.
  - T rises 16 m and settles the gaze at −35° once.
  - W/S/A/D fly level whatever the gaze; E/Space climb, Q/C descend; speed scales ×1–×30 with height.
  - Landing levels the view.
  - Settings can restore "W flies where you look".
  - The vertical-flight cause is recorded as an observation, not a diagnosis.
- **Aerial world (AERIAL-01..06, AD-26).** Above 14 m every known folder is an island, laid out by `WorldLayout` and drawn in the current place's frame.
  - Each island shows roads to its child folders, cyan link arcs, a warm visited ring, and a gold "You are here" beacon.
  - Vistas give way, the haze thins, and SSR/SSAO switch off high up.
  - Click an island, then G, to fly there; T over a place lands in it; T over nothing lands in the nearest place.
  - Places are revealed only inside the arrival folder or visited places.
  - B frames the neighbourhood, clear of the HUD.
  - The layout was revised once during the pass: the first version blew up exponentially on a chain of single folders (25 km).
- **Planetoids (PLANET-01/02).** `docs/planetoid-feasibility.md` gives a system-by-system assessment. The isolated `--experiment=planetoids` shows the unchanged walker running in each body's chart, with travel between bodies.
- **Defects found and fixed on the way:**
  - The Settings panel was not drawn on top (fixed earlier the same evening).
  - The aerial world was empty below a hidden ancestor (AppData).
  - The filter kept the keyboard after a mouse pick.
- **Instrument defects found and fixed:**
  - The OS probe lacked J/K key codes and clicked fixed row positions.
  - A probe teleport counted as walking through a gate.
  - The explore probe clicked an island under the HUD at 1.25.
  - Real Esc first closed a destination row's tooltip (the cursor rested on the row). The probe now returns the cursor to the scene; this explanation is supported by the passing rerun, not proven.

**Verification on the staged build `4a7b9ce6fdf6`:**

| Check | Result |
|---|---|
| Windows tests (live WSL test included) | 165 passed + 13 skipped |
| WSL tests | 163 passed + 15 skipped |
| Exploration journey, UI 1.0 and 1.25 | 29/0 each |
| Walking journey, UI 1.0 and 1.25 (WSL included) | 103/0 each |
| Real SendInput, UI 1.0 and 1.25 | 29/0 each |
| Places journey, connected / bounded tree | 53/0 / 52/0 |
| Scan scenario, navigation, map journey | 0 failures |
| Planetoid experiment | 4/0 |

Notes on the runs:
- One walking run at 125 % under heavy load missed a 3 % speed tolerance (7.66 against 8.13 m/s); the rerun gave 8.01 against 8.01.
- Performance with the machine quiet: walking at the plaza 6.79 ms mean (High, 1600×900); aerial flight 3.9 ms mean, p99 5.2 ms.
- Under load (DJ's instance running): 15–20 ms. The previous build measured the same under that load.
- DJ's `%APPDATA%\FileScape` was not created by any run.

## View levelling after a mouse look (2026-11-05, evening, follow-up)

DJ asked that letting go after looking up or down with a click-drag should return the view to level instead of leaving it on the sky or the ground. **Delivered artifact:** `C:\Users\David\AppData\Local\FileScape\build\FileScape.exe`, build **`e03dd62adb62`** (clean tree). Previous builds kept: `4b030e39b93a` (the usability pass), `2dc4ae8c3234`. Requirement CTRL-04. Evidence: `docs/evidence/2026-11-05-level/`.

- **Behaviour.** When a mouse look ends — right button released, a left-button drag let go, Esc, focus lost or a lost release — the look pitch eases back to level over **0.35 s** (smoothstep; never past level). The heading you turned to is kept. Looking again during the return takes over at once. Glides that aim the view on purpose (F, Home, Destinations) are not levelled, nor are arrival poses; after the next look, the view settles at 0° rather than the arrival's slight downward gaze. Applies on foot and in flight (in flight the pitch steers W, so after letting go W flies level; E/Q climb and descend).
- **Setting.** ⚙ Settings (F4) → "Level the view when you let go", on by default, saved in `preferences.json` (`levelViewOnRelease`; older files get the default).
- **Defect found and fixed:** the Settings panel was drawn *under* the breadcrumb, Destinations, legend and inspector panels (they were added after it), hiding its title, the speed labels and the value column — visible in the previous pass's 125 % screenshot and missed then. It is now brought to the front and opaque when opened (`settings-panel-ui1.0.png`, `settings-panel-ui1.25.png`).
- **Instrument updates:** the walk and OS probes read the look pitch while the button is still held (on release it now levels) and check the levelling separately. **Instrument defect fixed:** the OS probe read the state file while the app rewrote it (`WriteAllText` truncates first), parsed an empty file as no state, and saw an empty folder name once in the upward chain (one spurious FAIL on `3efd3a3`; the product reached Wayfarer in 12.1 s as usual; kept as `os-walk-results-ui1.0-instrument-defect-3efd3a3.txt`). It now retries until the file parses.

**Verification on the installed build `e03dd62adb62`:** Windows tests **152 passed + 12 skipped** (live WSL test included), WSL **149 + 15**; walking journey **103/0 at UI 1.0 and 1.25** (WSL steps included; 8 new levelling checks); real SendInput **25/0 at 1.0 and 1.25** (real release → level 0.7 s later, heading kept; upward chain 12.1 / 12.2 s); Places journey 52/0 (connected) and 51/0 (bounded tree) at UI 1.0. Walking frame time 6.96 ms mean, p99 8.21 ms (High, 1600×900). `%APPDATA%\FileScape` not created by any run. Whether 0.35 s feels right is DJ's to judge.

## Usability pass from DJ's hands-on feedback (2026-11-05, evening)

DJ played the walking world (build `2dc4ae8c3234`) and reported five problems; all are addressed in the exported application, not only in documents. **Delivered artifact:** `C:\Users\David\AppData\Local\FileScape\build\FileScape.exe`, build **`4b030e39b93a`** (clean tree; hashes in `build-info.txt` and Help). Previous build kept: `previous-20261105-072550-333871` = `2dc4ae8c3234`. Requirements CTRL-01..03, WALK-10, PLACE-16/17, SUM-01..04, ORD-01; decision AD-23. Evidence: `docs/evidence/2026-11-05-usability/`.

- **Mouse direction (CTRL-01/02).** Verified before changing anything: Places used the usual first-person mapping (mouse right → turn right, down → look down). Now a per-axis preference, default both reversed; ⚙ Settings (F4) with two checkboxes and walk/run sliders, saved to `%APPDATA%\FileScape\preferences.json`. Right-hold and left-drag, walking and flying, follow it; arrow keys and the maps do not change.
- **Speed and distance (WALK-10, CTRL-03).** Measured with the real walker: arrival → nearest gate of a typical place took 16.0 s walking / 8.1 s running, mostly because the avenue pushed a file wedge outward (30 files reached 44 m). Now files sit either side of the avenue and speeds are 8 / 16 m/s: **6.1 s / 3.1 s**. Wide place, farthest gate: 23.3 → 12.3 s walking (old speeds on the new layout as the "before"). Transition 1.04 → 0.75 s; held keys survive a gate (they were cleared). **Space**: jumping never adds horizontal speed (measured over 4 s, walking and running); no input defect found — the most likely source of "Space accelerates" is flight, where Space rises, or air momentum when W is released mid-jump. Not changed.
- **Gate identification (PLACE-17).** Eye-level name plates across every opening and a gate chip for the gate you are walking towards (or pointing at). From 9 m through the gate, the name was on screen in every frame at 1.0 and 1.25.
- **Continuous upward travel (PLACE-16).** Up arrives on a walker-verified straight route facing the next ▲ gate (re-worked if the listing lands during the transition). Injected: Harbour → Coast → Journeys → Wayfarer with only W held, 12.0 s; real keys 12.2 s; four Ups in typical places ≈ 19.7 s vs ≈ 54.8 s before (Core).
- **Summaries (SUM-01..04).** "Here: N folders · N files (· links)" and "In all: …" or "Found so far: … (N folders not fully listed yet)"; gates "3 folders · 12 files · 4.2 MiB in all" / "≥ … so far"; never a zero for unknown; from incremental aggregates only.
- **Order (ORD-01).** Natural numbers, Linux case variants distinct; gates and Destinations agree; stated in the Destinations title.
- **Defect found and fixed:** a walker touching a tall solid was treated as standing on it (30 m/s into a pillar → on top; a running jump into a tower → on its roof). The golden walk was re-captured with this and the layout change explained in the test.
- **Instrument updates (not weakenings):** the walk probe's look-down step follows the preference; SteadySpeed starts north of the landmark (at 8 m/s the old start ran into it); the Places probe clicks any visible part of a tower and checks the new Up arrival; the OS probe uses its own `--prefs` file.

**Verification on the installed build `4b030e39b93a`:** Windows tests **148 passed + 12 skipped** (POSIX-only; the live WSL test passed with `FILESCAPE_WSL_FIXTURE`), WSL **145 + 15** (Windows-only) — Core unchanged since `cd19c2a`, where these ran; walking journey **95/0 at UI 1.0 and 1.25** (WSL steps included); real SendInput **25/25 at 1.0 and 1.25**; Places journey 52/0 (connected) and 51/0 (bounded tree); scan scenario 0 failures (p99 12.6 ms); map navigation and map journey 0 failures. Walking frame time 6.89 ms mean, p99 8.19 ms (High, 1600×900). DJ's own preferences file was never created by any run.

## Correction and units pass (2026-11-05, afternoon)

Independent review (ChatGPT: Windows tests 125 passed, 12 skipped — the WSL check skipped for want of `FILESCAPE_WSL_FIXTURE`) asked for six corrections; DJ asked for a units refactor. **Delivered artifact:** `C:\Users\David\AppData\Local\FileScape\build\FileScape.exe`, build **`2dc4ae8c3234`** (clean tree, hashes in `build-info.txt` beside it and in Help).

- **Safe, verified delivery (AD-21).** `fs.sh export` now stages into a unique `staging-*`, verifies files, records identity and hashes, smoke-launches the staged exe, fails closed when it cannot tell which FileScape runs, refuses to replace a running build, promotes by renaming and keeps `previous-*`; `builds`/`prune-builds` added. Failure paths exercised against a throwaway base. **The damaged build:** its directory was removed by the 11:14 promotion (the old export's `rm -rf build`), so it could not be inspected further; `build\FileScape.exe` has existed continuously since 11:14 (verified at the start of this pass). Why the reviewer did not find it is not established.
- **Link failures by probing (LINK-06).** `ProbeTarget`/`ClassifyFailure` replace `Exists` checks; Windows tests cover denied (deny ACE), unavailable (a subst drive removed after the junction was made) and broken targets with Retry recovering after each cause is fixed.
- **Entry limit (FS-08/FS-10).** Listings are capped by the remaining budget at dispatch and on apply; cut folders are "partly listed" with the reason and "Start fresh here"; a reserve keeps requested destinations reachable; tested with a 40-entry budget on a 300-entry folder. The limit bounds the tree; transient buffering ≤ concurrent listings × remaining budget.
- **Whole-tree reference restored (AD-22).** "Bounded tree scan (whole-tree reference)" alongside "Connected world (explore on demand)", from Locations or `--scan=tree`.
- **Readability (VISI-01).** World signs are hidden under HUD panels and when overlapping another sign; long names shortened on signs; checked at UI scale 1.0 and 1.25 (DJ's scaling), which also exposed and fixed HUD overlaps (breadcrumb, Destinations/legend, hint, badge) and a real defect: **mouse look slowed to 1/1.25 at 125 %** (Godot's scaled `Relative`; now `ScreenRelative`).
- **WSL scope stated precisely (AD-18, LINK-09 unmet).** The tested access route and each call's result are recorded; following Linux symlinks remains unmet, with a half-day investigation proposed (KD-17).
- **Units (docs/units.md).** `FileScape.Core.Units` (Meters, MetersPerSecond, MetersPerSecondSquared, Degrees, DegreesPerSecond, PerSecond) and `TimeSpan` for durations; camera math, walker, thresholds, poses, camera rig, flight, transitions and frame-loop timing converted; conventions documented (Places metres vs map layout units, two angle conventions and their single bridge, viewport units vs physical pixels). Golden numbers captured before the refactor still hold; no new allocations; the typed arithmetic costs ≈ 0.3 µs per walker frame. Found on the way: `TimeSpan.FromSeconds` truncates (1/60 s → 166,666 ticks) and a 120 Hz integrator then lags one step — computed seconds now go through `Durations.FromSeconds` (rounding).

**Verification on the installed build:** Windows tests **136 passed + 12 skipped** (POSIX-only), WSL **133 + 15** (Windows-only), the live WSL test passing with a valid fixture; walking journey 71/0 at UI 1.0 and 1.25; real-input probe 17/17; Places journey 52/0 (connected) and 51/0 (bounded tree); scan scenario, map navigation and map journey 0 failures. **Performance (local session again):** walking 6.98 ms mean, p95 7.9, p99 8.6 (High, 1600×900); stationary 7.05 ms High, 2.40 ms Low; Places during a 50k scan p99 11.9 ms — consistent with 2026-11-04, confirming that the morning's 3× slowdown was machine state (KD-16).

## Walking world pass (2026-11-05)

DJ's direction after playing Places: the place concept is achieved and the visuals are compelling (preserved); next, *a world you inhabit on foot, connected to the real places on your computer*. Walking is primary, flight explicit. ChatGPT has not played the app (its desktop control is unavailable). **Nothing below claims DJ's acceptance of movement feel or looks.**

**What exists now (requirements WALK/JUMP/LINK/LOC/VISI/IDENT; decisions AD-16..AD-20).**
- *On foot by default.* A Core kinematic walker (eye 1.75 m, step 0.45 m, radius 0.35 m, gravity, slide-along collision, 120 Hz sub-steps) over terrain composed with each place: towers, landmark and gate pillars block; the arrival terrace has stairs front and back; tiers are walkable terraces (rise 0.42 m). ↑/↓ walk, ←/→ turn (they used to strafe), WASD walk and strafe, hold right mouse to look and steer (heading and gaze separate), left-drag also turns the head, Space jumps (coyote time, jump buffer, ledge tolerance), Shift runs.
- *An overlook in every place*: a platform with the place's summary, reached by a ramp (no jumping) or by two jump blocks (optional shortcut). Every gate is reachable without jumping (tested over the terrain).
- *Flight is a mode* (T / ✈ button): a continuous rise, the old fly controls, wheel speed; T lands on the clear surface below (never a tower roof), else the nearest clear spot, else the arrival point with an explanation. B rises above the place in flight.
- *One connected namespace* (AD-17): "This computer" → drives and WSL distributions (registry, no wake-up) → the arrival folder as a path; listed on visit (plus its child folders); background traversal only under the arrival folder and never for broad places; several listings in flight on workers.
- *Links followed on purpose* (AD-18): reparse tags identify junctions, symbolic links, mounted volumes, Linux symlinks, app aliases, cloud placeholders (folders, not links) and Linux special files. Walking through a followable link's gate (or G / double-click) resolves it and travels; the HUD names the connection; the breadcrumb is the physical location; Backspace returns in front of the link; Up goes to the real parent and on past the arrival root; loops and second routes reach the existing node; broken/unsupported/denied/unavailable are distinct and listed in "⚠ N here need attention" with Retry. Totals never include link targets.
- *Locations and paths* (LOC): L opens Locations (hub, arrival, drives, distributions); Ctrl+L takes a typed or pasted path (`C:\…`, `\\wsl.localhost\…`, `\\wsl$\…`, `/mnt/c/…`, `/home/…` → default distribution). Open folder… in a live world moves the arrival root without restarting.
- *Visibility* (AD-19): hidden/system entries hidden by default (H / button, "N hidden" count); dot-names always shown; totals unchanged and labelled.
- *Identity* (AD-20): hub (halo rings, location gates labelled by type), drive/distribution roots (banner with the case rule), software projects (manifest + README or .git; README and manifests on lit plinths by the avenue; evidence in the inspector; "Not a project" override).
- *Readability*: overlay labels never overlap each other, the HUD or the screen edge; the left HUD column stacks; the landmark plaque is sized for a walker and hides up close; short words on gates.

**Evidence tiers** (raw runs in `%LOCALAPPDATA%\FileScape\evidence\2026-11-05-walk\`; copies in `docs/evidence/2026-11-05-walk/`):
- *Core tests:* **126 passed + 11 skipped on Windows** (`dotnet.exe`; the skips are POSIX-symlink tests, with a reason), **123 + 14 skipped on WSL**. New: 19 walker/terrain tests (`WalkTests`), 13 explorer tests on real POSIX symlinks (`WorldExplorerTests`), 3 Windows tests on real junctions plus a live WSL fixture through `\\wsl.localhost` (`WindowsWorldTests`), 7 connected-place tests. Two earlier Places tests were revised to the new link contract (LINK-02 supersedes "never followed") — a requirement change, not a weakening.
- *Injected input through Godot's pipeline* (`--automate=walk`, fixture "Wayfarer" + WSL fixture): **66 checks, 0 failures** (`walk.log`): arrival on foot; look; stairs; looking down; curve; diagonal; jump; the overlook by ramp and by three jumps; Home; a child and back; a junction outside the root and back; a loop; two routes; a broken link; problems; hidden/system; a project; Up past the root; WSL by pasted path (case pair, Linux link); the hub; a not-ready drive (unavailable, worst frame 26 ms); take-off and landing; landing above a vista; modal Locations; labels clear at five places. `[direct]` steps only set up a case (placing the walker for a speed sample or a jump, selecting an item before G).
- *Earlier journeys, ported to the shipping configuration:* Places journey **52/52** (`places.log`, Atlas Studio; expectations updated where the design changed: physical breadcrumb, captured look, junction now followed), Places scan scenario **0 failures** (`places-scan.log`), map navigation probe **0 failures** (`navigation.log`).
- *OS-level input* (`tools/os-walk-probe.ps1`, real SendInput and Alt-Tab, judged from the state file, staged export): **17/17** (`os-walk-results.txt`).
- *My visual observation:* journey screenshots and frames sampled from a 48 s guided recording at eye level (`walk-tour.avi`, Godot movie writer, 1,430 frames at 30 fps, 1600×900, ≈ 300 MB, outside the repo).

**Measurements — read with care.** On 2026-11-05 Windows reported a *remote (Terminal Services) session* and the GPU was markedly slower than on 2026-11-04: **yesterday's export, measured again today, gave root High 20.2 ms mean (6.5 ms yesterday)**, wide-hall 19.1, Low 9.4. Absolute numbers below are therefore not comparable with the 2026-11-04 table; same-run comparisons are.
- Stationary isolation at one spot (`walk-perf`, High): eye 1.75 m 13.95 ms GPU, eye 4.6 m 13.64 ms, all place geometry hidden 12.85 ms, SSR off 12.03 ms, Low quality 4.93 ms — the walking view and the new terrain add about 1 ms; the rest is the environment and post-processing under today's conditions. Effect toggles drifted upwards over the run (machine state).
- Walking with turns and a run (journey, High, 146 nodes, 226 instances): mean 16.5–28.9 ms across runs today (p99 27–44 ms) — dominated by machine state, as the control shows.
- Explorer: the arrival place appears ≈ 1.4 s after launch on the fixture (including Godot start-up); a not-ready drive answers within one frame budget (worst frame 26 ms).

**Instrument defects found and fixed (not product defects):** jump apex measured against a ramp's changing support; the jump-block script kept walking after landing; "typing doesn't walk" held W into the path field (so the path got a "w"); distance-over-wall-clock speed comparisons (replaced by steady-state speed from a fixed clear start — one run had walked into the landmark); per-frame take-off thresholds that measured frame time; the old Places probe assumed node 0 and a finishing scan; the scan scenario assumed the first place appears before its neighbourhood completes (yesterday's build fails the same way today; now re-observed on a slow stream); `os-walk-probe` would have targeted the first FileScape window (DJ's session was open) — it now targets only the process it starts.
**Product defects found and fixed:** OneDrive (cloud tag) was a "link"; Esc did not close Locations; the HUD's left column overlapped when the place panel grew; selected/hovered labels ignored the HUD; labels clipped at the screen edge; the landmark plaque filled the view at walking height; take-off rose up to 16 m/s; landing could set you on a tower roof; the overlook panel was huge at the rail and its title collided with its summary; the walker's 0.3 m radius let tower faces fill the view; world signs read through the translucent HUD (panels now 93 % opaque); the inspector stayed "not listed yet" after a selected folder failed to list.
**Process incident:** `fs.sh export` deleted the unlocked files of the installed build while DJ's FileScape (started 2026-11-04 23:01) was running from it. DJ's process was not touched; the 163 missing runtime files were restored from a staged export; `fs.sh export` now exports to `build-staging` and refuses (exit 4) to replace a running build. **Installed 2026-11-05 11:14** after DJ closed FileScape: `tools/fs.sh export` (exit 0) replaced `%LOCALAPPDATA%\FileScape\build` with the build of `0ef8dfb` (189 runtime files); a launch check on the installed exe arrived on foot in Wayfarer, took off and landed (`walk-look`, 0 failures).

**Linux symlinks in WSL (AD-18, LINK-09 — unmet):** Tested on 2026-11-05 through one access route only — Windows file APIs (.NET and Win32) on `\\wsl.localhost\Ubuntu-24.04\…` (WSL 2, the 9P redirector) on DJ's laptop: `FindFirstFileEx` reports reparse tag `0xA000001D` (IO_REPARSE_TAG_LX_SYMLINK) and a size equal to the target text's length; `FileSystemInfo.LinkTarget` returns null; `FSCTL_GET_REPARSE_POINT` on a handle opened with `FILE_FLAG_OPEN_REPARSE_POINT` fails with ERROR_INVALID_FUNCTION (1); `ReadFile` on that handle fails with ERROR_INVALID_HANDLE (6); opening the path to follow it fails with ERROR_PATH_NOT_FOUND (3) for relative, absolute, `/mnt/c` and dangling targets alike; listing through a directory link fails (DirectoryNotFound). **Not tested:** the `\\wsl$` alias, extended attributes (`NtQueryEaFile`), `GetFileInformationByHandleEx` classes, WSL 1, other distributions or Windows builds, and any route that runs inside the distribution. FileScape identifies them and marks them "Linux link · can't follow". FIFOs show as tag `0x80000024`. `\\wsl.localhost\` itself cannot be listed; distributions come from the registry.

## Places pass (2026-11-04)

DJ's direction after the first prototype: feel *inside* a folder, know where you are, see the child folders and how to reach them, with what is next fading into the distance; atmosphere and distinct characteristics. ChatGPT had not played the prototype (its desktop control is broken), so DJ's hands-on feedback remains the primary experience evidence. **Nothing below claims DJ's aesthetic acceptance.**

**What exists.** A third experience, **Places** (default), alongside the retained whole-tree maps **Classic FSN** and **District** (AD-14, AD-15; PLACE-01..15). One folder at a time is composed as a local plaza: landmark with a name plaque, file towers in type quarters along an avenue, a ring of named gate arches (or alphabetical tiers on terraces beyond 24 children), the ▲ Up gate behind the arrival terrace, and beyond each gate a vista of the next place (its landmark in its own colour, a skyline of its largest files, beacons for its children). Travel is deliberate: walk through a gate facing it, double-click it, Destinations, G, U, breadcrumb, Ctrl+Home. A glide-and-fade transition marks every threshold. The current folder (navigator) and the selection are separate. While in Places the whole-tree layout is not computed.

**Evidence tiers** (raw runs in `%LOCALAPPDATA%\FileScape\evidence\2026-11-04-places\`; copies in `docs/evidence/2026-11-04-places/`):
- *Core tests:* 25 Places tests (composition of every folder of a 6k synthetic tree, 0–300 children, no overlaps, no gate hidden behind the landmark, tiers in alphabetical order, chains, empty/unknown states, thresholds with hysteresis, facing and cooldown, navigator, picking, identity, locality of the signature). Full suite: **95/95 on Windows `dotnet.exe`; 84 + 11 Windows-only skipped on WSL**.
- *Injected input through Godot's pipeline* (`--automate=places`, final exported build, fixture "Atlas Studio"): the journey below, **51 checks, 0 failures** (`places.log`); scan scenario 8 checks, 0 failures (`places-scan.log`). `[direct]` steps in the log are set-up only (placing the camera for the dithering test, selecting on a map, selecting a file).
- *OS-level input* (`tools/os-places-probe.ps1`, real SendInput and a real Alt-Tab, judged from the state file): **14/14** (`os-places-results.txt`): Destinations click selects without moving; held W walks through the Code gate; held S backs away without travelling; U returns with Code selected; G enters; a real double-click on a row travels; Ctrl+Home; Alt-Tab while walking leaves nothing held; B; M and back; **Enter opened Explorer at the fixture's Documents folder** (then closed by the probe). Two instrument defects were fixed first (argument quoting; DPI-unaware clicks at 125 % scaling — the second one briefly opened Explorer on the fixture's `locked` folder; no window was left open).
- *Reference views on the final build:* navigation probe 56/56 and the map journey, 0 failures (`navigation.log`, `maps-journey.log`).
- *My visual observation:* screenshots at each step (critique below), and frames sampled from the recording `tour.avi` (Godot movie writer, 879 frames, 29 s at 30 fps, 1600×900, MJPEG, ≈ 170 MB; outside the repo in `…\2026-11-04-places\tour\`).

**Journey (fixture "Atlas Studio", 1,713 entries; exported build, 1600×900, High):** starts in Places at the root; breadcrumb and landmark plaque name it; camera at the arrival pose; all 11 children have a gate with their name and a Destinations row; right-hold look works; Home returns to the arrival pose; clicking a tower selects it without changing the current folder; clicking a Destinations row walks up to that gate without travelling; holding W walks through it and starts the transition; arrival in the child (breadcrumb, plaque, ▲ gate behind, arrival pose, exactly one place change); dithering W/S on the threshold does not switch places; U returns in front of the gate you came from, facing in, with that child selected; double-click a destination travels; G ×5 descends the chain; Ctrl+Home returns to the root arrival; B ascends with the explanatory chip; M opens the map with "You are here · Atlas Studio"; Tab cycles to District; G on a folder in a map enters it in Places; wide-hall (240 gates on 8 tiers, ≤ 70 signs visible, 241 Destinations rows), many-files (800 towers), empty-room (explicit empty), locked (contents access denied), the junction (sealed gate; double-click does not travel); empty and denied gates carry different signs.

**Measurements** (exported build, D3D12, Radeon 890M, VSync off while measuring, walking W/A/S/D plus mouse-look ≈ 6 s):

| Case | Quality | Entries in tree | Instances | Mean ms | p95 | p99 | Compose / build ms |
|---|---|---|---|---|---|---|---|
| root (6 files, 11 gates) | High | 1,713 | 197 | 6.5 | 7.5 | 8.0 | 0.7 / 1.3 |
| root | Low | 1,713 | 197 | 2.5 | 3.2 | 4.4 | 0.7 / 1.3 |
| wide-hall (240 gates, 8 tiers on terraces) | High | 1,713 | 2,238 | 6.5 | 7.7 | 8.7 | 0.5 / 5.0 |
| many-files (800 towers) | High | 1,713 | 906 | 6.3 | 7.2 | 7.7 | 0.9 / 1.6 |
| Places during a 50k synthetic scan at 10k nodes/s | High | 50,000 | — | 6.9 | 8.3 | 12.6 | 1 recomposition, 1.7 ms |

For comparison, the maps during the same kind of scan (2026-11-03): p99 34–39 ms because of whole-tree re-layout (KD-02). In Places the place appeared 232 ms after launch while the scan ran, and only one recomposition was needed (the root's neighbourhood stops changing once its children are listed). The largest frame in the scan run (177 ms) is the startup frame (KD-03). Walking runs show isolated ~30 ms frames when a right-drag starts (not investigated).

**Visual assessment (mine, from screenshots; not DJ's acceptance).** What works: the arrival terrace view reads as a place (avenue of lit towers, landmark with name plaque, ring of named gates, vistas visible through the gates); entering many-files feels like arriving in a different district; the wide folder from above reads as an amphitheatre; empty/denied/not-scanned are unmistakable. What is weak: from the root terrace the near towers can crowd the frame edges; signs of far gates are small at the arrival distance; vistas are sparse clusters rather than convincing skylines; the plaza floor's reflections are subtle on Low; the Up-route strip is a plain line; there is no sound, weather or ambient motion. Several artefacts were found and fixed during the pass: oversized signs, a landmark hiding the central gate, gate lights scattering in volumetric fog, and **NaN pixels from `pow()` of a negative base** (beacon/landmark rim terms) that glow spread into white blobs.

**Instrument defects found and fixed (not product defects):** probe keys sent without waiting for processing; Destinations rows clicked without the scroll offset; a breadcrumb count miscounted; an over-short wait for the throttled recomposition after cancel on the faster exported build; a Core test whose "above the lintel" path started below it. **Product defects found and fixed:** clicking an already-highlighted Destinations row did nothing (ItemList emits item_selected only on change); stepping back (S) from the arrival terrace walked through the ▲ gate (now gates require facing through them); the inspector kept a stale "You are here" after travel; the toolbar showed two maps pressed; M framed the old selection instead of "You are here"; the vista beacons' NaN blobs.

## VIS-11 flicker investigation (2026-11-04)

DJ: a sun-like bright source and/or its reflection flashing on and off, especially while moving in District. Instrument: `--automate=flicker` flies a fixed camera path and records per frame: bright pixels, single-frame flash pixels (bright now, dark in a 5×5 neighbourhood before and after) and blob-scale *cell spikes* (16×9 cells whose luminance departs from the mean of the neighbouring frames by > 0.06), with one effect toggled at a time and a stationary control. Synthetic 12k sample, 1280×720, High, D3D12.

- **Instrument corrections:** the first path drove through a tower and measured the HUD; the pixel-level flash metric is dominated by sub-pixel window twinkle that no effect changes (it stays ≈ 22–31 per frame in every configuration), so the blob-scale metric was added; a street-level path proved unusable (near-object parallax swamps every metric).
- **Root cause found (reproduced, fixed):** the selection beam's shader computed `pow(1.0 - height01, 1.6)`; at the top cap interpolation can push `height01` just above 1, and `pow()` of a negative base is NaN. Glow blurs NaN/Inf into a bright, sun-like blob at the top of the beam. In District the beam is 80 u tall and appears only beyond ≈ 28 u from the selection, so moving and zooming switch the "sun" on and off; the wet ground reflects it. With the beam top in view (`--open-test=beamtop`): **pre-fix ≈ 80 bright pixels per 320×180 frame at the beam top; with the clamp 0.1; with glow off 0.1**. Screenshots `flicker-beamtop/flicker-pre-fix-mid.png` vs `flicker-baseline-mid.png`. Fix: clamp before `pow()` (also applied to two Places shaders that had the same pattern).
- **Second contributor (measured, mitigated):** on an orbit around a selection, blob-scale spikes halve when the directional light's specular is removed or puddles are roughened (peak cell deviation 0.142 → 0.078, spikes 10 → 6); SSR, the sky's sun disc, volumetric fog and its reprojection, SSAO, MSAA, shadows, the selection spotlight and the beam made no difference there. Mitigation: sun specular 0.1 and puddle roughness 0.12 (SSR reflections of lit towers unchanged) — measured equal to specular 0.
- **Residual / unknown:** small spikes remain that only disabling glow reduces (emissive edges and windows amplified by bloom). I could not observe DJ's exact view; whether the beam blob is what DJ saw is a strong hypothesis, **not confirmed**. Reproduction for DJ: District, select a file, back away > 28 u so the beam appears with its top on screen, move or zoom across that distance.

## Navigation pass (2026-11-03, after DJ's first run)

DJ's feedback: the concept is promising and performance seems fine; the look is somewhat bland; navigation is the priority. DJ tried the arrow keys (unbound) before reading Help.

**What was actually wrong vs missing.** Baseline on the previously installed export, using real OS input (Win32 SendInput, `docs/evidence/2026-11-03-navigation/baseline.txt`):
- **Working:** W moved the view. The earlier "keyboard doesn't work" report was **discoverability**, not a defect.
- **Missing:** arrow-key bindings (no effect). Fly/mouse-look did not exist. There was no in-scene hint.
- **Actual defects:**
  - A left-drag interrupted by Alt-Tab stayed stuck, so later button-less mouse moves kept orbiting (OS-verified).
  - Diagonals were about 41% faster (code inspection, now covered by tests).
  - Movement keys were polled while dialogs were open or the window was unfocused (code inspection).
  - The documented right-drag pan produced no change under SendInput (cause not investigated; superseded by the new design).
  - Review finding 1, stale-layout indexing, also affected picking (disabled whenever tree and layout counts differed), F-framing and Home during scans (possible index errors).
  - Found during this pass: multi-notch wheel events counted as one notch; a missing root path read as a cloud placeholder (all-bits-set attributes). Both fixed, with tests.

**Evidence tiers** (what each claim rests on):
- *Unit tests (Core):* camera math, alias tracking, the stale-layout interval, and the cloud-root policy. 70/70 pass on Windows `dotnet.exe` (59 + 11 Windows-only skipped on WSL).
- *Injected input through Godot's pipeline* (`--automate=navigation`, `Input.ParseInputEvent` → InputMap → GUI → `_Input`/`_UnhandledInput`): **56/56 checks** on the exported build (`navigation.log`). Measured, in orbit mode:
  - W 157.6 u/s, Up 155.6 (ratio 0.99), W+Up ratio 0.99, W+D 0.99 at 45°, Shift ×2.96.
  - Flight: W 228.3 u/s at the starting speed, Up/W+Up/W+A ratios 1.00, Shift ×3.01.
  - Wheel ×3 notches = ×1.953 speed.
  - Orbit↔fly transitions: Δpos ≤ 0.0007 u, with no rotation and no snap back.
  - Look pitch clamps at ±85° with the camera's up.y > 0; no roll.

  Set-up exceptions: the dialog test opens the dialog by a direct call, and focus loss is a simulated notification.
- *OS-level input* (`tools/os-input-probe.ps1`, real SendInput and a real Alt-Tab against the exported build, judged via `--state-file`): every check passes (`os-probe-results.txt`):
  - W and arrows move; W+Up ≈ W.
  - Right-hold captures the mouse and flies; mouse-look turns.
  - The wheel raises speed.
  - Alt-Tab mid-flight ends flight and releases the cursor with nothing held, and button-less mouse movement after returning does nothing.
  - Escape ends flight; middle-drag pans.
- *Manual observation by me:* screenshots only (the hint and toolbar). **Camera feel is not accepted until DJ tries it.**

**Limits of the earlier journey evidence.** The `journey` script mostly calls app methods directly (Select, SetVisualization, ResetView). It shows the behaviours exist, but not that the controls reach them. The navigation probe now covers the controls.

## Journey status

| Step | Status | Evidence |
|---|---|---|
| Choose a root | Provisional. `--root=` is demonstrated on 3 real folders. The native folder dialog is implemented but was not exercised, since automation cannot drive it | live logs |
| Scan with visible progress | Demonstrated. The status strip shows counts and the queue; the tree grows in place | screenshots; `scanperf` |
| Navigate the 3D hierarchy | Demonstrated through injected and OS-level input (see the navigation pass above). Feel: awaiting DJ | `navigation.log`, `os-probe-results.txt` |
| Select and inspect | Demonstrated: exact bytes, full path (incl. 372-char Unicode path), flags, timestamps | screenshots 02/03/07 |
| Deliberately open via Windows | Demonstrated for a directory: ShellExecuteEx opened Explorer at the exact path, confirmed externally via `Shell.Application` and then closed. Not auto-verified for a document, to avoid disturbing DJ's open, unsaved Notepad session | `live-fixture` log |
| Switch visualization, selection preserved | Demonstrated (synthetic and live) | `journey.log`, live logs |
| Recover when disoriented | Demonstrated: Home returns to the root neighbourhood (root pedestal on screen and pickable); B gives the whole-tree overview | screenshots 05/06/06b |

## Measurements (hypotheses from Investigation 01 are targets, not limits)

Method: the in-app `PerfRecorder` (wall time between `_Process` calls) plus Godot's measured GPU viewport time. VSync off; 1920×1080 window; exported release build; scripted 20 s camera path (overview orbit, then a dolly to street level). Datasets: synthetic seed 1965, 50,000 nodes (≈ 65.6k instances).

Default driver **D3D12** (AD-11), exported release build:

| View | Quality | Frames | Mean ms | p50 | p95 | p99 | Max | GPU mean | GPU p95 |
|---|---|---|---|---|---|---|---|---|---|
| Classic | High | 2113 | 9.47 | 9.43 | 10.37 | 10.79 | 15.62 | 8.45 | 9.22 |
| Classic | Low | 3859 | 5.18 | 5.06 | 6.09 | 8.35 | 11.30 | 4.23 | 4.85 |
| District | High | 1607 | 12.45 | 12.42 | 13.53 | 14.18 | 18.06 | 11.46 | 12.46 |
| District | Low | 4097 | 4.88 | 4.77 | 5.76 | 8.04 | 10.73 | 3.95 | 4.53 |

For comparison, Vulkan (editor-hosted build, earlier run) had lower means but wider tails: Classic High mean 6.88 / p95 11.04 / p99 13.12; District High mean 9.48 / p95 12.74 / p99 13.79 (`perf-vulkan-editor-summary.csv`). Against the Investigation 01 hypotheses: H1 (Classic, 50k, p95 ≤ 16.7 ms) is met on both drivers. H2 (cinematic, 10k, p95 ≤ 33 ms) is met with margin even at 50k.

Scan-time rendering (50k synthetic nodes streamed at 10k nodes/s while orbiting; VSync off):

| View | Frames during scan | Mean ms | p95 | p99 | Max | Re-layouts | Re-layout mean / max |
|---|---|---|---|---|---|---|---|
| Classic (D3D12) | 522 | 9.73 | 9.97 | 34.05 | 206 (startup) | 21 | 21.5 / 38.9 ms |
| District (D3D12) | 398 | 12.83 | 14.95 | 38.81 | 213 (startup) | 20 | 22.7 / 38.2 ms |

H3 (p99 ≤ 50 ms during scanning) is met. "No frame > 100 ms caused by merge work" is met, but the first frames after launch exceed 100 ms because of startup (KD-03). H4 (cancel ≤ 250 ms) is met by a wide margin.

Other measurements:
- **Scan duration (live, warm cache):** fixture 743 entries in 13 ms; `C:\Program Files\dotnet` 6,883 entries in 64 ms; NuGet cache 8,955 entries in 98–149 ms. Cold-cache and network timings: Unknown.
- **Cancellation:** `Cancel()` returns to the UI thread in ≈ 0.3–0.6 ms. On a live NuGet-cache scan the source stopped 0.2 ms after the request (state Cancelled, 252–272 entries kept). A unit test with a deliberately blocked OS call shows Cancelling acknowledged while the call is outstanding, and no further directories enumerated (FS-09).
- **Memory:** managed heap 1.5–17 MB. Process private bytes ≈ 1.0–1.1 GB and working set ≈ 0.66–0.80 GB, regardless of dataset (743 or 50k nodes), so this is an engine/driver baseline (Vulkan/D3D12 on a UMA iGPU). Godot-reported video memory 350–520 MB. Per-node memory was not isolated.
- **Picking/labels:** 40/40 random unoccluded on-screen targets resolved correctly in every run (both visualizations, synthetic and live), zero-byte tiles included. Labels are capped at 60 and overlap-rejected.
- **Re-layout:** 50k nodes take layout ≈ 10 ms plus instance rebuild ≈ 10–20 ms on the main thread. During scans this shows as periodic 20–40 ms frames (see p99).

## Known defects and gaps

- **KD-01 Intermittent Vulkan device loss.** "Couldn't wait for Vulkan fence (VkResult -4) … Vulkan device was lost", followed by a crash. On Vulkan it occurred in 4 of about 20 launches. Under sustained uncapped perf runs it hit 3 of 4 runs on Vulkan versus 0 of 4 on D3D12 (`stability-vulkan-vs-d3d12.txt`, `vulkan-device-lost-excerpt.txt`). Three of the crashes happened at the same point of the perf script: the switch from District High to District Low quality (volumetric fog + SSR off) with VSync off at 1920×1080. 120 quality/visualization toggles with VSync on did not reproduce it. No Windows display-driver event was logged. Root cause: Unknown (driver, Godot, or our environment swap). **Mitigation applied:** D3D12 is now the default on Windows (AD-11). All final evidence runs (journey, perf, scanperf, live) passed on D3D12. With 4 + 5 clean runs, D3D12 is *provisionally* stable, not proven.
- **KD-02 Re-layout hitches during scans.** Main-thread layout and rebuild grow linearly (≈ 0.6–0.8 ms per 1k nodes). Fix: background layout over a snapshot (AD-04 revisit).
- **KD-03 Startup frames of 150–220 ms** (first frame and first relayout; likely shader/pipeline compilation). No shader warm-up yet.
- **KD-04 Hand-feel not reviewed.** Speeds and sensitivities are measured and consistent, but were chosen without DJ's hands-on judgement: ground speed max(4, 0.9·distance) u/s; starting flight speed clamp(0.6·distance, 2, 400) u/s; look 0.12°/px; orbit 0.28/0.22°/px.
- **KD-08 Trackpads.** Holding the right button while moving is awkward on a trackpad, and there is no fly toggle yet.
- **KD-09 Open via a document association is unverified** (only directory → Explorer was verified).
- **KD-05 Not exercised by automation:** the native folder dialog (keyboard movement after closing it is covered only by the equivalent in-engine dialog case), the broad-root confirmation, the code-running-file confirmation dialog, Show in Explorer, Copy path, DPI scaling above 1.0 (automation forces `--ui-scale=1`; the default is DPI/96).
- **KD-06 Spatial stability during scans is partial.** The selection stays fixed on screen, but other subtrees can shift sideways as siblings grow.
- **KD-07 District overview is dark at bird's-eye distance.** It reads mainly through labels and streets. LOD/impostors are not implemented.
- Allocated size, hard-link counts, owner/security info: not queried (labelled in the inspector).
- **KD-10 (resolved 2026-11-05)** Places now has ground following, collision, stairs, ramps and jumping (AD-16). In flight there is still no collision (by design).
- **KD-11 Places geography is local and partly symbolic (by design, AD-14).** Vista distances are symbolic; vistas are omitted beyond 24 children (tiers); grandchildren appear only as beacons (≤ 16, the rest counted). Whether this reads as honest is DJ's call.
- **KD-12 Gates can shift while the current folder is still being listed** (alphabetical insertion during a scan). In practice a folder's entries arrive in one batch, so this is rare; the camera never moves.
- **KD-13 Isolated ~30 ms frames when a right-drag starts** in the walking measurements (p99 stays ≈ 8 ms). Not investigated.
- **KD-14 (partly resolved 2026-11-05)** The Places HUD and the walking journey were exercised at UI scale 1.25 (DJ's Windows scaling): panel overlaps, the breadcrumb and mouse-look scaling were fixed. Not exercised: other scales, other window sizes; `tools/os-places-probe.ps1` still assumes 1.0.
- **KD-14 (original) Places HUD at DPI scale > 1 not exercised** (automation forces `--ui-scale=1`; the OS probe's Destinations coordinates assume it).
- **KD-16 (resolved)** The morning's measurements were machine-state dominated (remote session); re-measured in the afternoon on the installed build: walking 6.98 ms mean, p99 8.6 ms (High), in line with 2026-11-04.
- **KD-17 Following Linux symlinks in WSL is unmet (LINK-09).** What was tested is in AD-18. Smallest bounded investigation proposed (≈ half a day, no new architecture): (1) repeat the probe through `\\wsl$` and read the link's extended attributes (`NtQueryEaFile`) and `GetFileInformationByHandleEx` classes, in case the 9P server exposes the target as metadata; (2) if not, time an explicit, per-link, on-demand `wsl.exe -d <distro> --exec readlink -- <linux path>` with a timeout, on a running and a stopped distribution, with Unicode and spaces — invoked only when the user follows a Linux link, no service or background process; (3) write up the result as a go/no-go for LINK-09.
- **KD-18 Not exercised:** a stopped WSL distribution (entering one would start it), Windows directory symlinks and mounted volumes (creating them needs Developer Mode/admin), a link to a denied or unavailable target, the "Not a project" button and Retry by automation, DPI scaling above 1.0 for the new HUD parts, the C:\ root in evidence runs (privacy).
- **KD-19 The maps show the explored world.** With live data the Classic/District maps lay out the hub, the arrival folder's ancestor chain and whatever has been listed; arrows in the maps still strafe (NAV-07).
- **KD-20 Movement feel (partly tuned to DJ's feedback, 2026-11-05 evening):** speeds are now 8 / 16 m/s and adjustable, the look direction is DJ's stated preference (reversible per axis); turn rate 120°/s, jump 0.83 m, flight 10 m/s and look sensitivity are still my choices. Whether it *feels* right is DJ's call.
- **KD-22 Upward arrival stand-off:** the verified straight route to the ▲ gate must cross the arrival terrace along its stairs, so Up arrives 14–31 m from the exited child's gate in the fixture (beside the landmark), and 63–76 m from gates on outer tiers of wide folders. The child's gate is behind you, the ▲ gate ahead. A side ramp on the terrace (or moving it off the axis) would let arrivals stay nearer the child; not done.
- **KD-23 Not exercised in this pass:** UI scales other than 1.0/1.25; Up into the hub from a drive root by automation (the hub has no ▲ gate: Core-tested); gate plates for very long names beyond 24 characters on real data (shortened, full name in the chip and inspector); the top bar at 1.25: the source badge is cut at the window edge (full text in its tooltip), and while a scan runs Cancel appears and may push Help off; `os-places-probe.ps1` still assumes UI 1.0.
- **KD-21 Cosmetic:** the identity-banner overlap was fixed (it now sits above the name's height); landing can still face you straight at the landmark from close by; frame-time spikes of 40–80 ms remain in walking runs today (machine state not separated).
- **KD-24 Aerial world (2026-11-06):**
  - Distances between places are symbolic, and child places of an 11-folder place sit about 600 m away, so B views the neighbourhood from about 1–1.5 km up and islands are small.
  - A discovery can slide its ancestors' children along their arcs (AERIAL-02 states the guarantee).
  - Islands are not to scale with the full place drawn at the current island.
  - Revealing from the air was not exercised in the app, because in the fixture every nearby folder was already listed (Core-tested).
  - The world is laid out up to 4,000 places.
  - Not exercised: the hub with real drives from the air (privacy), a bounded tree of more than 4,000 folders in the app, touchpads.
- **KD-25 Names:** at most 40 signs; far names are shortened to 12 characters, so similar prefixes can look alike from far away (full names in the chip, card and Destinations); detail lines are small (11–13 px).
- **KD-26 Feel not judged:**
  - the jump arc, take-off height and gaze;
  - the ×30 height speed;
  - level flight against "where you look";
  - badge size and fade;
  - the tread brightness of the stairs.

  If vertical flight still "feels wrong", the next suspects are the vertical speed (0.8 × flight speed) and the take-off rise.
- **KD-27 Not promoted:** build `4a7b9ce6fdf6` is staged, not installed (DJ's FileScape was running). Install by closing FileScape and running `tools/fs.sh export`.
- **KD-28 Right-hold real-input check (open as intermittent, instrument).** The check read one state snapshot 150 ms after the right press, so it could see a snapshot written before the app reacted. This was reproduced 3 times in 76 cycles, each with the sample predating the app's look (where timed), and with no product failure in a fresh snapshot.
  - **Original occurrence (2026-11-06):** it recorded no values, so its cause is unconfirmed, though consistent with this race.
  - **Fix:** the check now waits up to 1.5 s for a fresh snapshot and logs its values. A future failure will therefore show which part failed.
  - **Reopen as a product defect:** if a fresh snapshot ever fails to show the capture.
- **KD-29 Aerial limit, not exercised:** a real namespace of more than 4,000 places in the app, and the old build's failure in the app (it has no small-budget switch). With a budget too small for the way down, the aerial view is unavailable in that place by design.
- **KD-30 Pin identity:** NTFS per-directory case sensitivity is not detected (AD-36). Two case-variant WSL folders were not pinned by hand in the app.
- **KD-15 Content previews:** implemented in the planetary world only (PLANET-13: pictures, text, sound, on a deliberate E; Places still shows README text only).
- **KD-31 Injected input intermittently ignored in tier runs (open, cause unknown, probably environment).**
  - **Walk journey, full tier on `63dfed091e8b`:** two look-direction checks after closing Settings measured a smaller turn (−13.3° instead of −36.0°) and the wrong pitch sign for the left-drag. The same build passed alone, after the planets journey in the same order, and the previous build passed too (3 of 4 runs pass).
  - **Explore journey, full tier on `88899d32ccf3`:** 9 checks in flight and over the aerial world saw keys and a click do nothing for a few seconds. The same build passed alone right after (1 of 2 runs pass).
  - **Common symptom:** injected keys and mouse events have no effect for a stretch, which is what Places does by design while the window is not focused. No change to Places' input, look or flight code was found; the flat walker's only change is the shared jump model.
  - **Diagnostics added:** probes now log every window focus change ("window focus LOST"). A recurrence next to such a line is the environment; one without it reopens this as a product defect.
- **KD-32 Not promoted:** the planetary build is verified in its staging folder but could not be moved into `build`: Windows refused to move the current `build` folder (no file in it is open, so a folder handle is held, for example a terminal or Explorer window open inside it). Nothing was closed. `tools/fs.sh promote <staging-name>` installs it once the folder is free.
- **KD-33 Planets, not established:** how it feels to a person (movement, camera, scale, sound: nobody has played or listened yet); real large trees in the app (Core composes 800 files in 2.6 ms); NTFS cloud placeholders in the app (they are never read; not exercised); the music on other audio devices; UI scale 1.5 and above.

## Provisional decisions to confirm with DJ

Godot (AD-01), the Fishbone default (AD-08), the log height scale (AD-07), click = select-only (FSN approached on click), and D3D12 as the default driver (KD-01, AD-11).

## Next useful iteration (recommended, 2026-11-06)

0. DJ closes FileScape and installs `4a7b9ce6fdf6` (`tools/fs.sh export`), or runs the staged exe directly.
1. DJ tries, then judges KD-26:
   - the default view, J and K;
   - names across the plaza and the stairs;
   - the jump: hold and tap;
   - T and looking down; E/Q/C;
   - B and the aerial world: click and G, T over a place.
2. Depending on the verdict:
   - If flight is still wrong, try the vertical speed and the take-off rise.
   - If the aerial distances feel too large, tighten the layout (several rows for non-empty children too).
3. Planetoids: if DJ wants them, step 1 of `docs/planetoid-feasibility.md`: one place as a planetoid style.

## Earlier recommendation (2026-11-05)

0. (After the usability pass) DJ tries build `4b030e39b93a`: the look direction (Settings, F4), walking/running speed, plates and the chip near gates, U then W held up a chain, the new summaries; then decide speeds, the default direction and KD-22. (Earlier: DJ tries the installed build `2dc4ae8c3234`; the half-day WSL symlink investigation (KD-17) if Linux links matter to DJ; type `PlaceParams`/terrain lengths if they become a source of mistakes (docs/units.md lists them as deliberate primitives).)

1. **DJ walks it:** arrival on foot, the stairs, ←/→ turning (the changed arrows), Space, the overlook, T flight and landing; then a junction, Backspace, Locations, a pasted WSL path. Tune speeds, turn rate, jump and flight to DJ's hands (KD-20).
2. **Re-measure performance on a local session** (KD-16) and, if walking views are costly, add a cheaper "walking" quality between High and Low.
3. **Richer identities with bounded previews:** photo collections (thumbnails as facades) and document collections — the first content-aware places, behind the file-appearance extension point (KD-15).
4. **Connected-world depth:** a remembered history across sessions, Explorer's virtual namespaces (This PC libraries, network neighbourhood), and an explicit opt-in WSL helper for Linux symlinks (KD-17).
5. **Maps over the explored world:** frame the arrival subtree by default and mark the ancestor chain as a path (KD-19).

### Earlier recommendation (2026-11-04)

1. **DJ plays Places** on the fixture and on a real project folder: is arrival comfortable, are gates and the way back obvious, does walking through a gate feel deliberate (or should entry need a confirm key), is the discontinuity (separate places, symbolic vistas) acceptable, does District's flicker still occur after the beam fix?
2. **Grounded walking:** follow the plaza/terrace height and step down from the arrival terrace; gentle collision with towers and gate pillars (camera only).
3. **Vistas as skylines:** more of the child's structure (quarters as massing, its gate ring as a glow), and a consistent "next-next" horizon; parent vista seen through the ▲ gate.
4. **Atmosphere:** per-folder ambient variation within the vocabulary (accent, haze density, light pools), subtle motion (drifting particles), and the first content-aware file appearance (image thumbnails as tower facades) behind the extension point.
5. Earlier items still open: KD-02 background layout for maps, KD-03 shader warm-up, KD-08 trackpad fly toggle.

### Earlier recommendation (2026-11-03)


1. DJ dogfoods the standalone build on a couple of real folders and reports friction: camera feel, label density, District readability.
2. Keep watching KD-01 on D3D12 during dogfooding. Reproduce the Vulkan loss on a minimal Godot scene (sustained volumetric fog + SSR, then a readback) and, if it reproduces, report it upstream. Add a shader warm-up for KD-03.
3. Move layout and instance packing off the main thread (KD-02), then try 200k–1M nodes with subtree summarization and LOD for distant districts.
4. One more District pass: ground-level light pools, distant-block impostors, and street names at intersections.
