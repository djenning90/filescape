# Implementation status

Last updated: 2026-11-20 (evening), the sixth pass (PLANET-101..140, TEST-06..09: the overview follows attention at any depth, Overview controls and the way back down, every panel's keyboard and mouse ownership, every departure from a launcher and a reversible F, the telescope survey, Star Chart Dorian, a real picture gallery and the yellow cube, Open vs Show in folder, the planetoid prototype retired, test-time accounting and the first unit-test migration; final candidate `5ba12f91daa3`); before that 2026-11-20, the fifth playtest follow-up (PLANET-81..99, TEST-05: departures and landings, the walking camera's return, the long jump with real keys, the selection card, Command Prompt after X, discovery in play and the overview's detail, the way back, a picture gallery, music by place and activity; final candidate `70b6ffc7ebb6`); before that 2026-11-19, the fourth playtest follow-up (PLANET-67..80: camera look, keyboard ownership, Hazard moves, landmarks and scenery, Overview discovery, musical modes; final candidate `cfee25d2b709`); before that 2026-11-17 (night), after the third playtest follow-up (PLANET-49..66); before that 2026-11-17, the second playtest iteration (PLANET-38..48); before that 2026-11-15, the first public playtest preparation; before that 2026-11-14 (later), the documentation pass; before that 2026-11-14, the third planets iteration (controls, world characters with their music and look, the repository workbench; commits `b4e1eec`…HEAD); before that 2026-11-11 (later), the second planets iteration from DJ's playtest (commits `1ba70a7`…`3ffe1bb`); before that the planetary world (commits `4d7ea48`…`a98fdb4`); before that 2026-11-06, the correctness follow-up on `8c18e8e3f107` (commits `bef9dee`…HEAD); before that the reliability/compact-places iteration (commits `3c838d7`…`4519f18`); before that the exploration pass (commits `91df1c2`…`77849d5`); before that view levelling (CTRL-04, commits `3efd3a3`…`1f61ff1`) and the usability pass (commits `dd0c639`…HEAD); before that the correction and units pass (commits `dbf2bc1`…HEAD) and the walking-world pass (commits `75deacf`…HEAD); the Places pass is 2026-11-04 (`2eabd73`…`bdc0a73`); earlier sections describe 2026-11-03 (commits `ee39c8f`…`5379de4`). Hardware: Lenovo 21RX, Ryzen AI 9 HX PRO 370, **Radeon 890M iGPU** (driver 32.0.22024.19001, 8 GiB UMA carve-out), 87.6 GiB RAM, Windows 11 Pro 26200, on AC power. Evidence files: `docs/evidence/2026-11-03/`. The full raw runs are in `%LOCALAPPDATA%\FileScape\evidence\` on DJ's machine and are not in the repo.

Vocabulary: **Demonstrated** = observed with recorded evidence; **Provisional** = works in limited conditions; **Failed** = observed not working; **Unknown** = not established.

## Seventh pass (2026-11-20)

### Stream G: the gallery as one browsing experience; a sideways step for arrow-key players (PLANET-159..168, AD-94)

Branch `worktree-agent-ae4500bf39202cf49` from main `ae32129`. No desktop use in this stream (the coordinator owns it):
everything below that needs the running app is **provisional** until the journeys and probes listed run.

**Found first (code reading):** the gestures on a picture disagreed — a click selected an easel while an open preview
went on showing another picture; a double-click on a picture launched Windows' Photos while R inspected in-game; only E
or walking off closed the preview; the viewer had two step buttons and no outside click. ← → turn on foot, A D strafe,
and a held A or D lets the chase camera swing in behind the sideways run after 1.2 s, turning a sidestep into a slow
turn (from the code, not yet observed).

| Area | Status | Evidence |
|---|---|---|
| One browsing rule in Core (`GalleryBrowse`): look, view large, Windows only on Open, the preview shows the selection, Esc one level, travel, browse by walking (PLANET-159..161, 167) | **Demonstrated** in Core; **provisional** in-app | `GalleryBrowseTests` (written with the rule, not seen failing first: new behaviour; the defect it replaces — the stale preview — is one of its cases) |
| The viewer's mouse zones (`ViewerPointer`) and keys: ‹ › edges, backdrop click, mouse back / forward, Home / End, Page Up / Down, Space inert (PLANET-162..163) | **Demonstrated** in Core; **provisional** in-app | `GalleryBrowseTests` zones and steps; `planets-gallery` steps written, not run |
| "Open with Windows ↗" one look (`Ui.LeaveButton`) in the viewer, the preview and the reader (PLANET-164) | **Provisional** (built, not seen) | screenshots to take: `gallery-preview`, `gallery-viewer-dawn`, `browse-viewer-next-edge` |
| Ctrl + ← → sidestep in Planets and Places; a pure sidestep keeps the view (PLANET-165) | **Demonstrated** in Core; **provisional** in-app and with real keys | `ArrowSidestep` tests; `planets-gallery` Ctrl + → 1.8 s; `os-planets-probe -Gallery` |
| Cues: prompts, arrival card, viewer key line, Help (PLANET-166) | **Provisional** | the prompt checks in `planets-gallery` |
| Bounded loading unchanged; mixed folders read nothing by themselves (PLANET-168) | **Provisional** (code paths unchanged) | `planets-gallery` bounds and the Desk step, unchanged |
| Core suite | 911 passed, 29 skipped, 10 failed — all ten are timing bounds (`SkySynthTests …_is_cheap` ×9, `WorldLayoutTests.Layout_is_deterministic_and_fast…`) in code this stream did not touch, at load average ≈ 27 with the other streams' runs (**environment**, as stream 6 saw) | `tools/fs.sh test-wsl` (457 s). Focused: `tools/fs.sh test-wsl --filter "FullyQualifiedName~GalleryBrowseTests"` (21/21, ≈ 60 s with the build); with `GalleryTests`, `MediaWorldTests`, `GoldenBehaviour` 36/36 after the sample README change |

**To run (coordinator):** `tools/fs.sh check journeys planets-gallery planets-gallery-fixture` (the fixture run has the
stress cases and the crowds; both now include the browsing steps, PlanetsProbe.GalleryBrowse.cs), then
`tools/os-planets-probe.ps1 -Exe <staged FileScape.exe> -Out <dir> -Gallery` (real input on the gallery fixture; needs
`FileScape.Fixtures create-gallery`). Also affected and worth one run: `planets-actions` (its .sln double-click must still
open; the click trace's wording for pictures changed), `planets-reader` (the reader's Open button is relabelled),
`planets-dialog-keys` / `os-planets-probe -Dialogs` (Ctrl is now a movement modifier on foot: Ctrl+L, Ctrl+O unchanged).

**How to play:** in a gallery, click a picture (or E beside it) to look; click another and the preview moves; walk on
and it follows the easel in front of you; Ctrl + ← → (or A D) slide sideways along the easels with the view kept;
double-click a picture (or R, or click the preview's picture) to view it large; ← → or the ‹ › edges step, the wheel
zooms; Esc or a click outside the picture brings you back where you stood; Enter or "Open with Windows ↗" opens the
original in Windows' app.

### Stream T: text presentation everywhere (PLANET-151..158, AD-93)

Branch `worktree-agent-abde1989b13d5dc12` from `ae32129`; not staged (the coordinator owns the desktop). DJ's item 4.

| Area | Status | Evidence |
|---|---|---|
| One formatter, one typography (PLANET-151) | **Demonstrated** in Core; on screen **provisional** | `UiTextTests`; built |
| Filesystem text literal everywhere (PLANET-152); two defects fixed | **Demonstrated** in Core | `LiteralTextTests` (failing first: 18 failures — inspector extension, trip panel's Backspace name — then 0); a real scan of the Markup Names fixture |
| Key caps in prompts, captions, the overview hint, the telescope's controls, the first-run hints (PLANET-153) | **Demonstrated** in Core, built; look: DJ | probes' plain text unchanged; `planets-text` screenshots |
| Help in Core with headings and caps (PLANET-154) | **Demonstrated** in Core; look: DJ | `UiTextTests.The_help_sheet…` |
| Surfaces fit 1280×720…2560×1440 at UI 1.0–1.5 (PLANET-155) | **Demonstrated** in Core; on screen **unknown** | `PanelFitTests`; `planets-text` |
| The planets-text journey and its fixture (PLANET-156) | **Unknown** (written, built, not run) | `--automate=planets-text`, `FileScape.Fixtures create-markup` |
| Wheel over Help and the reader (PLANET-157) | reader **demonstrated** (planets-reader); Help **unknown** | `planets-text` |
| Plain controls stay plain (PLANET-158) | **Demonstrated** (audit) | AD-93 |

**Whole Core suite once (WSL, 3 shards, 398 s with the build):** 1,092 passed, 9 failed, 29 skipped of 1,130. The 9 are
timing bounds only (eight `SkySynthTests …_is_cheap`, `WorldLayoutTests.Layout_is_deterministic_and_fast…`), run while the
other streams loaded the machine (load average ≈ 20); all 11 cases pass alone (41 s). None touches text.

**Fast loop for this area** (≈ 25–40 s on Linux): `tools/fs.sh test-wsl --filter "FullyQualifiedName~UiTextTests|FullyQualifiedName~LiteralTextTests|FullyQualifiedName~PanelFitTests|FullyQualifiedName~LessonTextTests|FullyQualifiedName~MarkdownText"`.
On screen: `planets-text` (above) after `FileScape.Fixtures create-markup`.

**Open / for DJ:** the caps' look in the outlined prompt over a bright sky; whether the overview's long second line
should wrap (it now does: two lines at 1600×900) or be shortened; the 48-character limit for names in prompts. Not
changed: the title card (a plain Label: a name), Destinations' and Locations' rows (plain Tree rows), the gallery
viewer (stream G), the toasts (plain), MarkdownText's own document colours.

### Seventh pass, stream K: legacy views only by the menu, keys per context, the way back on safe ground, every travel route, ▾ to Destinations (PLANET-141..150, AD-92)

Branch `worktree-agent-a86b829e57c686772` from `ae32129`. Built (`tools/fs.sh build`) and Core-tested only: no app launch,
no desktop use (the coordinator runs the journeys and probes listed below). DJ's items 3 and 9.

| Request | Implementation | Evidence | Status |
|---|---|---|---|
| Remove the shortcuts that switch to legacy views; keep them in the menu (item 3) | Core `PlayCommands` (commands × play contexts), `InputBindings.CommandOf`, `Main.HandleCommand` / `CommandActs`, `PlanetWorld.TelescopeKey`; Help, the menu's labels, the guides | `PlayCommandsTests` 6/6 (2 failed first against the old rule); journeys `planets`, `planets-overview-controls`; probe `-OverviewEnter` | **Provisional** (Core demonstrated; the journeys and probe not yet run) |
| Where else: the overview, the telescope, Ctrl+V / Shift+V, a panel open | the same table answers every planets context; the telescope consumes F, B, X instead of stepping back | `PlayCommandsTests`; journeys above | **Provisional** |
| Every context owns its keys; text fields (item 3) | `PanelOwnership` `TextField` row; `Main.KeyboardOwner` (`FocusHolder`, `OpenPanels`, Esc lets go); the audit table in AD-92 | `PanelOwnershipTests` 11/11 (3 failed first) | **Provisional** (Core demonstrated; `navigation`, `planets-dialog-keys` to run) |
| Root landmark actions take ↑ ↓ Enter at once; click outside closes and is consumed; the last panel returns control | unchanged (AD-83 already does this); re-read and recorded in the audit | journeys `planets-landmarks`, `planets-dialog-keys`; probes `-Dialogs`, `-Landmarks` | **Not reproduced** (no gap found in the code) |
| Restore world, position and camera after a legacy view, on every way out (item 3) | `SurfaceResume.MayCapture(travelling)`, `InOverview`, `SupportMetres`; `PlanetWorld.CaptureResume` / `Resume` / `Activate` (a Hazard World, the air, a fall, the overview reopened) | `SurfaceResumeTests`, `OverviewCameraTests`; journeys `planets-overview-controls` (menu round trips, from the overview), `planets-hazard` (mid-jump on Event Horizon) | **Provisional** |
| An unsafe saved position gets a nearby safe one | Core `SurfaceResume.Place`, `SafeStanding` (over a gap, at a rim, something new there; nearest safe ground within 24 m; else the arrival) | `SurfaceResumeTests` 7/7 | **Demonstrated** in Core; in-app provisional |
| Every travel route through the launcher; tests that enumerate them (item 9) | Core `TravelRoutes` (29 routes); `// travel:` tags on every navigator move and departure in the app; the telescope's Enter → `DepartFor(…, Telescope)` | `TravelRoutesTests` 5/5 (2 failed first; `DepartureEntry.Telescope` unused on `ae32129`) | **Demonstrated** in Core; journeys `planets-travel`, `planets-lookouts`, probe `-Depart` to run |
| A drop-down beside the current path opens Destinations; breadcrumb and ☰ unchanged; focus and dismissal (item 9) | `Hud.Places` ▾ (`CrumbDropdownRect`, pressed look follows the list); Help | journey `planets-destinations-keys` (new ▾ steps) | **Provisional** (built; not run) |

**Newly found:** the telescope stepped back for every key it did not use and then ran it (V, Tab: a legacy view from the
eyepiece); the telescope's Enter was labelled a G departure; Ctrl+V matched V. All fixed above. Possible, for stream G:
the picture viewer's row passes F1–F12, Ctrl+L and Ctrl+O; if a panel they open sits under the full-window viewer, the
row should pass nothing.

**Tests (Core, WSL):** see the stream's report for the run log; the whole WSL suite once at the end:
913 passed, 6 failed, 29 skipped of 948 (365 s, three shards, with an app build running beside it). The 6 are timing bounds
(`SkySynthTests` …_is_cheap ×5: 79.8 ms vs 60 ms; `WorldLayoutTests` …fast: 263 ms vs 150 ms), untouched by this stream; all 9 cases
pass alone (44 s).

### Seventh pass, stream M: the Starter Sky's music alternatives, Aurora explained (PLANET-169..173, AD-95)

Branch `worktree-agent-a12cf0c0d98952ea6` from `ae32129`. No desktop use: Core tests (WSL) and `fs.sh build` only.
Evidence: `%LOCALAPPDATA%\FileScape\evidence\2026-11-20-p7-music\` (README, metrics.txt, files.txt, aurora\README.txt).

| Area | Status | Evidence |
|---|---|---|
| Three written alternatives for the hub (revised theme, Aurora drift, Homecoming), four variations each, the classic kept (PLANET-169) | **Demonstrated** (Core, offline renders); by ear: DJ | `SkySynthStarterThemeTests` 8 (1 render fact skipped by default); behaviour baseline untouched (the classic's world key and stream unchanged; bit-identical test) |
| Settings → Music, heard on the next beat, saved by name; Core default classic, game default Homecoming (PLANET-170) | **Demonstrated** (Core); **unknown** in-app (not run) | Core: preference round trip, switch on the next beat and back, nothing elsewhere changes; `fs.sh build` OK |
| Revisits: resume at the next phrase, later visits later variations, written rests (PLANET-171) | **Demonstrated** (Core); in-app to run | Core resume/visit tests; `planets-starter-music` written, not run |
| Multi-minute analysis, comparison reel, revisit renders (PLANET-172) | **Demonstrated** (offline) | metrics.txt: same-sounding bars classic 92 %, revised 68 %, drift 58 %, Homecoming 69 %, Dorian 79 %; all −26.5…−27.7 LUFS |
| Aurora demonstration (PLANET-173) | **Demonstrated** (offline); by ear: DJ | `aurora\`: 6 comparison files + 10 feature-off files, loudness-matched |

To run on the desktop: `planets-starter-music` with `FILESCAPE_STARTER_MUSIC=Homecoming`, `FILESCAPE_AUDIO_RECORD`
and `FILESCAPE_AUDIO_RECORD_SECONDS=240` (the in-game recording); `planets-music` (unchanged checks; the hub now plays
Homecoming by default, its identity still Hub); a look at Settings at 1.0 and 1.25 UI scale (the new Music row).

## Seventh pass (2026-11-21)

### Stream S5: gameplay stalls removed (PLANET-174..180, AD-96)

Measured with `--automate=planets-stalls --diag-frames=…` on staged builds (injected input, never real OS input), the
same routes before (a7a528a: the instrument only, 5ba12f9's behaviour) and after (5a56183 / 8c05b98). Evidence:
`%LOCALAPPDATA%\FileScape\evidence\2026-11-21-p7-stalls\{before,after1,after2}\<route>\` (probe log, frames.csv; counts
and ids only). Frame = wall-clock gap between frames (vsync on, 60 Hz).

| Route | Before p50 / p95 / p99 / max (ms); >33/>100/>300 | After |
|---|---|---|
| Ubuntu root, walk 60 s, 1600×900 | 16.6 / 18.3 / 24.3 / 716; 20 / 13 / 13 | 16.6 / 18.4 / 24.0 / 62; 11 / 0 / 0 |
| Ubuntu root, 6 landings, 1600×900 | 16.6 / 17.9 / 29.5 / 776; 14 / 8 / 6 | 16.6 / 18.5 / 28.2 / 43; 9 / 0 / 0 |
| Ubuntu root, walk 60 s, fullscreen 3840×2160 | 19.6 / 28.8 / 47.9 / 769; 86 / 16 / 13 | 16.6 / 18.8 / 26.3 / 51; 15 / 0 / 0 |
| Ubuntu root, 6 landings, fullscreen | — | 16.6 / 18.4 / 29.0 / 49; 13 / 0 / 0 |
| Vast Reach (`--test-broad`), walk 30 s | 16.6 / 20.0 / 28.2 / 188; 15 / 8 / 0 | 16.6 / 18.8 / 23.5 / 74; 5 / 0 / 0 |
| Vast Reach, 6 landings | 16.6 / 19.3 / 50.0 / 165; 18 / 8 / 0 | 16.6 / 17.9 / 22.6 / 40; 5 / 0 / 0 |
| Vast Reach, overview open 4 s | 16.6 / 23.6 / 49.9 / 131; 4 / 2 / 0 | 16.6 / 19.8 / 23.9 / 72; 2 / 0 / 0 |
| Gallery fixture, walk 20 s + 4 landings | max 144 (walk), 66 (landings) | max 35, 39 |
| Saved Pictures, walk 20 s | max 131 | max 35 |

- Worst before: the recompose swap while walking (buildCurrent 610–760 ms, composes of three large children 40–200 ms
  each, GC 16–22 ms of 190–250 MB allocated); landing back on the root (555–773 ms). After: the worst walking frame is a
  launcher's full world built while its new ground was still being made (56 ms); 0 neighbours composed on the main
  thread; 70–74 background composes in 27–28 jobs per minute, longest 38 ms.
- GPU at 3840×2160 standing on the root: as shipped 18.2 ms; MSAA off 10.8, glow off 15.5, shadows off 17.5, sky shader
  off 14.3, scale 0.75 11.4, 0.5 6.8. After AD-96's automatic scale (0.67, FSR 1): 11.4 ms; full scale 19.9; MSAA 2× 11.0.
- Not changed: the session's first arrival (≈ 0.9–1.0 s at the Ubuntu root, 0.4–0.5 s on a gallery; composes its sky and
  waits for its ground behind the arrival title). DJ's 402 ms frame opening the overview on Vast Reach: not reproduced
  (131–174 ms before, 72 ms after). DJ's 45–55 ms fullscreen GPU frames at 2560×1440: not reproduced. Windowed GPU p50
  was 4 ms in the after2 runs and 7–9 ms in the before and after1 runs at the same 1600×900 scale 1: unexplained
  (machine state; the render scale stayed 1, no "[render]" line).
- Tests: NeighbourhoodSchedulingTests (12, area `stalls`); the stall guard journey `planets-stalls` (functional tier).

### Stream V2: journey speed: a simulated clock, journeys three at a time, the desktop's cost shown (TEST-14..17, AD-91)

The same machine and the representative set (walk-ui1.0, planets-look, -tutorial-cues, -dialog-keys, -hazard,
-navigation, -music, -overview-attention), run with `check journeys` and `FS_LAUNCH_LOCK`. Other streams were building
and testing in WSL throughout (load average 15–33), so the absolute numbers are noisy.

| Run | Build | Mode | Desktop (s) | Active (s) | Failures |
|---|---|---|---|---|---|
| baseline | main `ae32129` | wall clock, one at a time | 902 | 902 | look (a "largest step a frame" check), attention |
| par3-1 | `c76fc64` | simulated clock, 3 at once, full resolution | 497 | 1,338 | look (timed out at 0.49× real time), navigation (list-delay instrument), attention |
| A | `629f25a` | simulated, 3 at once, 3D at 0.5 | 332 | 671 | dialog-keys (2: the reader's click outside), attention |
| B | `629f25a` + gate clock | simulated, 3 at once, 3D at 0.5 (the new default) | **239** | 453 | attention |

- **Desktop occupancy on the set: 902 s → 332 s and 239 s** (−63 % and −74 %) in the two runs of the new default.
  Six simulated journeys ran in 213.6 s and 125.7 s of wall time against 552 and 340 s of steps (×2.58, ×2.70).
  Navigation and music run alone afterwards on the wall clock (89–95 s and 24 s, as before). **Demonstrated** (A, B).
- **Pace** (each probe's last `clock:` line). At full resolution, three at once ran at 0.42–0.60× real time with the
  GPU taking 18–29 ms a frame. Alone the GPU took 18.6 ms (0.56×). With 3D at 0.5, three at once ran at 0.88–1.47×
  (A) and 1.22–2.77× (B), with the GPU at 2.4–8.7 ms. The GPU (Radeon 890M at 1600×900), not the clock, limited
  speed. **Demonstrated.**
- **Load no longer changes the game.** walk-ui1.0's W+D check (one of the load-sensitive failures in earlier runs)
  read `5.58 vs 5.54 m/s` on the wall clock and exactly `5.50 vs 5.50 m/s` on the simulated clock at 0.42×, 0.89×
  and 1.30× real time. Its slope check read 5.55 vs 5.54 against 5.50 vs 5.50. planets-look, which failed in the
  baseline on its per-frame step check, passed in A and B. **Demonstrated** for these checks. An attempt to impose
  load with Godot's `--frame-delay 40` had no effect under `--fixed-fps`, so it was not used as evidence (unknown
  why).
- **Projection for `check full`** (from the sixth pass's table, `scratchpad/p6/full1.txt`): the real-time journeys
  sum to ≈ 537 s and stay serial. The other ≈ 1,410 s divided by the measured ×3.1–×5.2 (the set's serial seconds
  over its parallel wall time) gives ≈ 270–460 s. Journeys then take ≈ 810–1,000 s instead of ≈ 1,950 s, and the
  tier ≈ 27–30 min instead of 42.9. **Provisional:** short journeys have a larger share of start-up, and the whole
  full tier has not run in the new mode (the integrator's `check full` will show it).
- **Instrument defects found and fixed:** the probes timed walks, holds, falls and timeouts with `Time.GetTicksMsec`
  while the game moved by measured deltas (the W+D noise). planets-navigation and planets-overview-enter depend on
  the wall-clock `--test-list-delay-ms` hold. On a slow simulated clock the held folder finished before the check,
  so they are now real-time.
- **Product correctness (frame-rate independence):** the world's clock (`IWorldSession.Now`: discovery pacing,
  relayout and recompose throttles, the hover bob, the face toggle's window, hazard press times), the HUD's toast,
  hint and fly highlight, and the walking world's gate cooldown and upward settle now run on game time
  (`FrameClock`). In ordinary play this equals the wall clock.
- **Unexplained, kept visible:** run A's planets-dialog-keys failed "click outside the reader on empty sky or ground at
  (30,170): it closes…" (owner stayed Reader). It passed in B and in the baseline. This is probably an instrument race:
  the click point may have been chosen before the reader's panel was laid out under parallel load. Evidence:
  `%LOCALAPPDATA%\FileScape\evidence\checks\journeys-20261121-034235\planets-dialog-keys`. planets-overview-attention fails
  the same assertion in every run, including the baseline on main (the indicator's "X pauses" text). That failure is
  not caused by this stream.
- **Desktop and focus:** each launch still takes focus when its window opens, as the serial journeys always did.
  Injected input continues in an unfocused window under automation, and the logs show focus changes without
  failures. No other window is moved or minimised.

### Stream V1: Core-suite speed (TEST-10..13, AD-90)

Branch `worktree-agent-a3b00fa56f8434b6f` from `ae32129`. Tests and tools only; no product code changed (the
behaviour baseline is identical, Debug and Release).

**Why Windows Core went from 186 s to 330 s (demonstrated).** Mostly the machine, multiplied by one serial class:
- The 114 tests added between `70b6ffc` and `5ba12f9` cost ≈ 48 s of ≈ 708 s of test time (WSL, one process; 24 s of
  it OverviewAttentionTests); the SkySynth tests barely changed. The old commit's tests, built and run on the same
  machine today, take as long as the new ones (WSL, one process: 335 s at `70b6ffc`, 310 s at `ae32129`).
- The 330 s run (`full-20261120-195816`) was a slow machine: its build took 40 s (19 s in the 186 s run) and its stage
  49 s (28 s); the same 818 tests took 186 s and 237 s in two full runs an hour apart.
- What turned any slowdown into wall time: SkySynthTests was one partial class over nine files, so one xUnit collection
  of ≈ 200 tests run one after another on one core. In the integrator's 412 s TRX every other class had finished at
  166 s (HazardMovementTests, whose envelope sweep alone took 111 s) and most by 43 s; the rest of the run was
  SkySynthTests alone. Process start, fixture setup (writing the Starter Sky: 2 classes) and concurrency settings
  (xunit's default, one thread per core) were not the cause.
- Timing bounds inside the parallel run failed under load and passed alone: the throttled-scan cancel (309 ms vs
  250 ms, Windows), `Rendering_a_revisit_with_the_second_theme_is_cheap` for Outskirts and Underglow (61.5 / 60.1 ms vs
  60 ms, WSL) in the sixth pass's full run; earlier `…six_effects_is_cheap` and the 150 ms layout bound. All of them are
  `Kind=Perf` now.

**What changed** (commits `426d768`, `7b09bb9`, `2d6ca01`, docs):
- SkySynthTests is 14 classes (each section of SkySynthTests.cs and each SkySynthTests.*.cs file), sharing
  `SkySynthKit`; HazardMovementTests' world audits are HazardGapWorldTests and HazardLeapWorldTests. Files kept their
  names; helpers moved to `SkySynthTests.Kit.cs`.
- One `dotnet test` process per host (the WSL shards are gone), Release, tiered compilation off in the test host.
- `TestKinds`: 8 Perf methods, 14 cases (wall-clock bounds; three split from their correctness halves), 2 Report tests (the
  tightening candidates, the running envelope), 43 Slow methods. The ordinary run leaves out Perf/Report; `--fast`
  Slow too; `fs.sh test-perf` runs Perf/Report one at a time (Debug); `check full`/`release` run it as `tests-perf`
  after the parallel tests and the stage, before the first journey.
- Same assertions, less work: the envelope searched from the widest gap down in parallel batches (same widths 5.1 /
  6.6 / 10.1 / 10.1 m, 9.1 / 13.8 m); Every_variation and the loudness check computed side by side; pairwise
  FluentAssertions calls only where violated (PlaceTests, PlanetComposerTests, UniverseLayout, WorldLayout,
  LayoutAndInteraction); a sorted `Equal` instead of `BeEquivalentTo` on 800 ids.
- `fs.sh test-area` / `test-wsl-area AREA` (`areas.txt`, `TestAreaTests`), every run prints its seconds and where
  they went (`tools/test-times.py`), `FS_TEST_TRX=path` keeps the TRX.

**Before and after** (2026-11-20; other agents' test runs were on the machine throughout, load average 6–30 on WSL;
"before" Windows is the integrator's TRX, also beside another agent):

| Run | Before | After |
|---|---|---|
| Windows `fs.sh test` | 412 s (330 s in the sixth pass's full run) | 60 s: build 14 s + tests 46 s (span 37 s) |
| WSL `fs.sh test-wsl` | 209 s (3 shards); 329 s one process | 39 s: build 10 s + tests 28 s (span 26 s) |
| WSL `--fast` (718 tests) | — | 15 s |
| WSL area sets | — | music 20 s, hazard 12 s, places 10 s, gallery 10 s, overview 5 s, navigation 5 s, the others 2–7 s |
| Windows `fs.sh test-perf` (16 tests, serial, Debug) | inside the parallel run | 54 s (build 18 s + tests 36 s); 15/16 pass |
| Sum of the tests' own seconds (WSL) | 709 s | 302 s (one test at a time: 130 s) |

Per class, Windows (own = the class's tests summed; ends = when its last test ended, from the first test's start):

| Class | tests | own before (s) | ends before (s) | own after (s) | ends after (s) |
|---|---|---|---|---|---|
| SkySynthTests (14 classes after) | 206 → 197 | 412.5 | 412.5 | 206.3 | 34.5 |
| HazardMovementTests (3 classes after) | 19 → 19 | 164.9 | 166.3 | 52.9 | 29.8 |
| HazardWorldTests | 53 → 53 | 39.3 | 39.3 | 23.2 | 26.0 |
| PlanetComposerTests | 30 → 29 | 33.3 | 34.6 | 19.0 | 24.1 |
| WindowsScannerTests | 11 → 10 | 31.8 | 34.0 | 22.1 | 22.5 |
| OverviewAttentionTests | 7 → 7 | 28.2 | 28.4 | 6.2 | 6.1 |
| PlaceTests | 25 → 25 | 27.2 | 27.9 | 1.0 | 3.8 |
| HazardRecoveryTimingTests | 12 → 11 | 25.6 | 25.5 | 2.4 | 6.5 |
| SampleSkyTests | 13 → 13 | 25.2 | 42.7 | 15.2 | 36.8 |
| PlanetReliefTests | 74 → 74 | 22.9 | 23.0 | 5.3 | 7.4 |
| OverviewDiscoveryTests | 13 → 13 | 12.9 | 12.9 | 5.0 | 4.9 |
| GalleryTests | 7 → 7 | 7.2 | 26.6 | 5.1 | 20.5 |
| **run** | 931 → 921 (+16 Perf/Report apart) | 876 | 413 | 411 | 37 |

The same on WSL: SkySynth 334 → 175 s own, ends 310 → 25.5 s; HazardMovement 124 → 37.5 s, ends 118 → 26 s; run
709 → 302 s own, span 310 → 26 s. Test counts: Windows 931 + 6 new (3 Perf halves, 1 Report, 2 TestAreaTests) = 921
ordinary + 16 Perf/Report; WSL 929 + 6 = 919 + 16. Every test that passed before passes.

**Test-run log** (all by this stream; WSL unless named): baseline one process Debug 329 s; `70b6ffc` rebuilt on .NET 10,
one process 375 s (5 failures: 3 Perf-type bounds under load, the layout bound, one my copy's missing FileScape.sln);
after the split 117 s; `fs.sh test-wsl` 173 s (load 24), Release 87 s, 142→76 s comparisons; fast 105 s (load 25, before
tiering); perf 73 s (7/15 failed under load 25, serial confirmed); serial Release profile 127 s; thread-count and JIT
experiments 8 × ≈ 45 s; ordinary 83, 76, 55, 49, 39 s; fast 15 s; 11 area sets 2–20 s. Windows: 4 runs (ordinary
148 s with a first Release build, 61 s, 60 s; perf 54 s). Reruns: none for failures; repeated runs were measurements.

**Open / for the integrator:**
- `Rendering_one_second_of_music_with_six_effects_is_cheap` measured 68.8 ms (bound 60 ms) in the serial Windows perf
  run while other agents' tests loaded the machine (it was 17–19 ms alone on 2026-11-11). Unknown whether it passes
  on a quiet machine: the candidate's `check full` (`tests-perf`) decides; if it fails there alone, the music got
  slower since the bound was set (a product question for stream M), not the instrument.
- Nothing launches the app or uses the desktop; no journey or probe changed. `check harness` passes.

## Sixth pass (2026-11-20)

### The final candidate (`5ba12f91daa3`, staged, not installed)

Eight streams in separate worktrees, merged in the order 2, 7, 8, 4, 3, 5, 6, 1 (`ef6834d` … `d7c1ee0`); then the integration
fixes below. Staged at `%LOCALAPPDATA%\FileScape\staging-20261120-204612-2439435`; the installed build is still `6b9b60c274af`.

| Check | Result |
|---|---|
| `check full` on `d7c1ee0` (`evidence\checks\full-20261120-195816`) | 48 steps pass, 5 fail; wall 42.9 min (11:58–12:41 local), steps 2,570 s; lock wait 0 s |
| …the 5 failures | **Core timing bounds** (Windows: the throttled-scan cancel 309 ms vs 250 ms; WSL: two `…second_theme_is_cheap` 61.5 / 60.1 ms vs 60 ms), run beside the build and the other suite: all 5 pass alone. **walk-ui1.0** (W+D 5.58 vs W 5.38 m/s) and **planets-near-far-sample** (a label not shown): passed on rerun, same build (provisional: intermittent). **planets-destinations-keys** 33/2 twice: an instrument defect, the probe still expected a world click to keep the Destinations list open; stream 3 made it close (PLANET-117). Probe updated (`9518cb9`): 38/0 |
| Journeys vs the fifth pass's full run | ≈ 2,275 s → ≈ 1,952 s of journeys while adding 5 steps (overview-attention 68, overview-controls 33, gallery-fixture 34, the two reader journeys 28 now that their fixture is set). Trimmed: overview-enter 256 → 88, dialog-keys 172 → 94, music 164 → 23, hazard 84 → 76; moved to release: explore/places/planets at UI 1.25 (136 s); retired: planetoids (14 s). Grew: travel 63 → 80, lookouts 54 → 75 (the telescope view), tutorial-cues 100 → 119 |
| Core | Windows 902 passed, 1 failed (timing, above), 29 skipped of 932 (330 s, was 186 s for 818: **the slowest step now**, not investigated); WSL 898 / 2 / 29 of 929 (199 s, beside) |
| Real input on the candidate (`evidence\2026-11-20-os-cand`, `-cand2`) | -Dialogs 106/0, -OverviewEnter 10/0, -Depart 5/0, -Hazard -Recovery 21/0, -Look 23/0, -Tutorial 30/0, -Landmarks 27/0 (after its telescope checks were updated to the AD-85 view: 23/2 before, instrument), -Play 15/0, -Route 10/0, -InputFocus 14/0, -Audio 8/0. 1,426 s active, ≈ 6 s lock wait. No spill: Guard on every input |
| ZIP | `%LOCALAPPDATA%\FileScape\FileScape-5ba12f91daa3-win-x64.zip`, 74,798,813 bytes, 199 entries, no backslashes, no logs, sha256 `1ea907c714b011dd6fcb421119e5005a4389baffde1bfb94c2d8ea40397f12dc`; Expand-Archive into `%TEMP%\fs-zip-check-5ba12f9` and its smoke launch: ok |
| Fresh clone | staged (402 s, most of it waiting for the probes' lock) and packaged by itself: the same 199 entries; FileScape.exe, build-info.txt and the two app DLLs/PDBs differ (each DLL in 72 bytes, the same size; no build path in either: the assembly identity and debug checksum); every other file identical |

Integration fixes: a `using` doubled by streams 2 and 4; stream 1's far-out camera fix (`Basis.LookingAt`) carried into
stream 2's `PlaceUniverseCamera`; the instruments named above; planets-dialog-keys no longer sends Ctrl+V into a text
field (it pasted the real clipboard into the filter and the log); the gallery's `.uid` files.

**Verification time for the whole pass** (agent-hours; the streams ran side by side, so lock waits overlap): streams'
own launches ≈ 11,000 s active, ≈ 18,500 s waiting for the desktop lock (eight streams sharing one screen was the main cost),
reruns as each stream lists (mostly instrument defects found in the run). Integration: check full 2,763 s active (0 s lock
wait), reruns 201 s (3 journeys, then destinations-keys with its stage), real input 1,426 s (110 s of it the -Landmarks rerun),
clone stage 402 s.

**Open:** Windows Terminal, the Command Prompt shutdown and Open's no-job ShellExecuteEx route were not re-run on this
candidate; uncached cloud thumbnails (Windows returns none; FileScape downloads the picture); a 214 ms frame while a
real Pictures folder filled; the Windows Core run's time.

### Stream 2: Overview controls and transitions (PLANET-110..114, AD-82)

This is stream 2's branch `worktree-agent-aaf6043d34866abad`, from `7680d22`. It was staged for journeys only, not
exported or installed. DJ: Shift should accelerate the overview; Esc "slides across space and then cuts"; legacy views
should keep where you stood; Destinations while the overview is open should complete or say why.

**Diagnosed:** Esc removed the overview at once and blended the camera for 1.1 s straight from 40 km up to the
walking camera. The planet stayed beyond the 4 km far clip for ≈ 0.9 s, then appeared. Activation always rebuilt the
world at the arrival meadow with the establishing shot.

**Changed:** the Core files `OverviewCamera.cs` (`OverviewControls`, `ZoomPanPath`, `OverviewReturn`),
`SurfaceResume.cs` and `OverviewEnter.cs`. The app files `PlanetWorld.Universe.cs`, `PlanetWorld.UniverseNav.cs`,
`PlanetWorld.cs` (Activate/Deactivate), `PlanetWorld.Trip.cs` (one line) and `Hud.cs` (Help). The instruments are the
`planets-overview-controls` journey (`PlanetsProbe.OverviewControls.cs`) and the `-OverviewEnter` mode of
`os-planets-probe.ps1`. `planets-overview-enter` now plays each variant once (`--repeats=N`).

| Area | Status | Evidence |
|---|---|---|
| Shift: pan ×3, ← → orbit ×1.6, a wheel notch counts twice, drag orbit 1:1; no Shift unchanged (PLANET-110) | **Demonstrated**, Core, in-app and real input; feel: DJ | `OverviewCameraTests`; in-app key pan ×3.01, wheel ×1.180 / ×1.392; real keys Shift+Left ×1.66, real wheel ×1.180 / ×1.392 |
| Esc flies back down: zoom-and-pan path, hand-over at the same size and place, glide behind the traveller, soft light, second Esc hurries (PLANET-111) | **Demonstrated** in Core and in-app; look: DJ | hand-over 3.002 → 3.002 radii, 0.0° → 0.0°; overview part 1.80 s, at most 7.6 e-folds/s and 118°/s off centre; light ≤ 26 %; glide 1.51 s; hurried 0.57 s; feet 0.000 m; screenshots `return-*.png`. Real Esc: closed after 1.96 s (with the poll), 0.000 m |
| Back where you stood after V (Places) or M (a map) and V back (PLANET-112) | **Demonstrated** in Core and in-app | 0.000 m, facing 0.0°, heading 0.0°, pitch kept, no establishing shot, both trips |
| The hint and Help teach the controls; the hint sits clear of the "You are in" bar; the bottom line is no longer clipped (PLANET-113) | **Demonstrated** (built, checked in-app, screenshots); readability: DJ | `planets-overview-controls` hint check |
| Destinations in the overview: every commit lands or says why; a commit during the way back takes over (PLANET-114) | **Demonstrated** in Core, in-app and real input | `OverviewEnter` tests; `planets-overview-enter` 13/0 (11 of 11 landed); real keys: T, J, type, Enter / ↓ Enter / Esc Enter, each landed in 1.8–1.9 s |
| No regressions in the journeys that use Esc | **Demonstrated** | `planets-tutorial-cues` 55/0, `planets-overview-sample` 9/0 |

**Journey trimmed:** `planets-overview-enter` took 256 s (fifth pass) and now takes **88.1 s**: 33 attempts became 11.
The equivalent coverage is the Core `OverviewEnter` cases (the two fifth-pass failures). The new
`planets-overview-controls` takes 32.6 s.

**Open / for DJ:** the 1.8 s cap on a long way back, the light's strength, and pan ×3. During the way back the
overview's own keys (Enter, F, [ ], Z, Home) wait; shared commands interrupt it. A Hazard World is not resumed after a
legacy view (you arrive as before). The overview itself is not reopened after a legacy view. A plain Esc during the
fly-in to another world is ignored, as before.

### Stream 7: file actions (Open vs Show in folder, real Windows checks) and the planetoid prototype retired (PLANET-137..140, AD-87)

Branch `worktree-agent-a167623cd39e944bb` from `7680d22`. Staged (not promoted) `7090272` at
`%LOCALAPPDATA%\FileScape\staging-20261120-183534-2229009`; the WSL-distribution rule and the docs came after it.
Evidence: `%LOCALAPPDATA%\FileScape\evidence\2026-11-20-s7\` (`actions-detached`, `actions-wrapper-assoc`: `actions-probe.log`, `opened.txt`).

**What was found on the base build (`70b6ffc`):** Show in Explorer of a folder *did* select it in its parent (the app's
own `--test-hold=reveal` on a fixture, with and without the console route, and the bare SHOpenFolderAndSelectItems call
on a WSL fixture folder). The two actions looked alike because both open an Explorer window and nothing on the card said
which shows a folder's inside and which its place (**product**, labels). Under a job (FileScape.console.exe, and every
launch from WSL, PowerShell or WMI) Show went through the desktop shell's `explorer /select`: it reported success for a
missing item and opened Linux without selecting a distribution (**product**).

| Area | Status | Evidence |
|---|---|---|
| Labels say what opens: Open / Open folder / Open drive / Open target / Open This PC; Show in folder / Show in This PC / Show on server; never the same label (PLANET-137/138) | **Demonstrated** in Core | `SelectionActionsKindsTests` (7, through `SelectionActions.Describe`, written first: 5 failed before the rules); `SelectionActionsTests` 4/4 |
| Off with the reason, Enter obeys it: gone, unreachable, access-denied folder (Open), Linux pipe, broken/Linux link (Open), WSL distribution and letterless volume (Show); cloud placeholder warns it downloads (PLANET-139) | **Demonstrated** in Core; in-app via `card` steps for a folder and a file | the same tests; probe `card` lines |
| Show in folder selects the item in its containing folder: a file, a folder, a junction, a junction whose target is gone, a `.lnk`, `C:\` in This PC; a missing item says "no longer there" and opens nothing; no process of FileScape's started (PLANET-138) | **Demonstrated** on Windows, under a job both ways (WMI-launched and FileScape.console.exe) | probe 16 pass + 1 fail (WMI-launched; the fail is the WSL step, then still judged, see next row) and 19/0 including the 4 association opens (console wrapper; the WSL step recorded, not judged) |
| A WSL distribution in Linux | **Failed in Windows** (observed: Linux opens after ≈ 18 s, nothing selected, both routes) → the card's Show is off with the reason | probe `OBSERVED` line; `shopen` measurement |
| Open on a folder shows the folder itself | **Demonstrated** | probe |
| Real associations with apps already running (PLANET-140): `.txt` → DJ's running Notepad (new tab), `.html` → DJ's running Chrome (new tab; the per-user default reported by the registry key was Edge, Windows used Chrome), `.xml` → Edge (not running: started with the Personal profile and restored 14 pages), `.png` → Photos | **Demonstrated** (open landed, window titled with the fixture) | probe `opened.txt` |
| The direct (no job) ShellExecuteEx route for Open, as after a double-click | **Unknown**: no launch available here runs FileScape outside a job | `where` step: "parent WmiPrvSE, in a job" |
| `planets-selection-actions` on `7090272` (sample data, buttons and keys, recorded shell) | **Demonstrated** in-app | 44/0 in 60.5 s (as on the base) |

**Planetoid prototype retired** (traced first: Planetoid, PlanetoidTraveller and its Arc, PlanetoidExperiment and
`planetoid.gdshader` had no other user): the `--experiment=planetoids` option (`--experiment` stays for planet-gallery),
the scene, the Core types, the shader, `PlanetoidTests` (6 tests, 0.12 s together on WSL) and the `planetoids` journey
(14.3 s in the last full check) are removed. `docs/planetoid-feasibility.md` is marked historical; architecture, units,
legacy views, the workflow table and PLANET-02 updated. Saving: ≈ 14.4 s of `check full`, plus the experiment's build.

**Left open on DJ's desktop (for the handoff):** a tab `fs-actions-notes.txt` in DJ's Notepad; a tab `fs-actions-page`
(the `.html` fixture) in DJ's Chrome; Edge, started by the probe for the `.xml` fixture with DJ's Personal profile
(restoring 14 pages), was closed with WM_CLOSE, so Edge's saved session now holds the fixture tab; Edge then relaunched
itself in the background (10 windowless msedge processes from 11:00:27) and a windowless Photos (#71424) stayed — not
ended (ending them was refused by the session's permission check). Every Explorer window the probes opened was closed
(3 Explorer windows before and after); one Linux view left by an earlier run was found and closed.

**Next migration candidates:** the journey's label/enabled matrix per kind is now fully in Core (`SelectionActions.For` × `Describe`); its remaining in-app value is that a button and its key reach the same handler and the recorded shell request — a later pass could drop the label assertions and keep one cell per action.

### Stream 8: verification that is efficient and accountable; the first unit-test migration (TEST-06..09, AD-88)

DJ: testing has taken hours. Report active test time, desktop waits and reruns separately. Move deterministic
assertions from slow journeys into unit tests where they catch the regression as well. Document how to pick the
smallest test set. The guide is in `docs/windows-workflow.md`, "Choosing the smallest test set", together with a
ranked list of the next migrations.

| Area | Status | Evidence |
|---|---|---|
| Three totals at the end of every check (`active`, `lock-wait`, `reruns`); a session timing log (one session per branch) fed by every step and by `fs.sh timed NAME [--lock] [--why=…] -- CMD`; nested lock waits kept out of active time; `fs.sh timing` (TEST-06) | **Demonstrated** | `check harness` (new cases: accounting, timed, timed --lock); real runs below (e.g. a stage: 78.3 s active, 281.0 s lock wait) |
| UI 1.25 repeats of explore, places and planets out of `check full`, into `check release` (TEST-08) | **Demonstrated** (listing); saves ≈ 136 s per full run (44.4 + 51.4 + 39.7 s on 2026-11-20). Their PASS lines were identical to the 1.0 runs | `check journeys --list`; `full-20261120-062206` |
| Fixed waits reviewed (TEST-07) | **Demonstrated** (review): the shared helpers already wait on conditions. ≈ 470 s of per-journey settle waits are listed in the guide. None was changed | — |
| planets-hazard's mid-window and late catches and its late-twirl and late take-off leaps moved to Core `HazardSampleReplayTests` (TEST-09) | **Demonstrated** for the late take-off (both caught one injected regression), **provisional** for the late catch (the journey failed too, but by a cascade from its first slip) | see below |
| planets-hazard trimmed | **Demonstrated**: 78.6 s, 78/0/3 after, against 88.1 s and 86.1 s (84/0/3) for the base `70b6ffc` build on the same afternoon (83.8 s in the last full run), so ≈ 8–10 s (≈ 10 %) per run. The plain jump's new walk gives back ≈ 5 s of the ≈ 15 s removed. Core replay: 7 cases in ≈ 2 s | evidence `checks\journeys-20261120-181122` and `-184248` (before), `-190541` (after, `staging-20261120-185901-2295310`); the regressed runs `-181940` (ApexSpeed) and `-183137` (PLANET-85 + MaxDepth); the failed trims `-183819`, `-184956`; `tools/fs.sh test-wsl --filter FullyQualifiedName~HazardSampleReplayTests` |

**Equivalence (one injected regression, then removed):**
- The regression: PLANET-85's late take-off disabled in `SphereWalker.RecoveryPress`, plus `HazardMoveParams.MaxDepth`
  1.8 → 0.8 m.
- The unchanged journey on that build (`staging-20261120-182811-2178787`) went 71/11/4:
  - "the take-off press 0.05 s after leaving the board…" failed with "catch", then "carry-ignored", short, 1 fall;
  - the late catch failed, after the early slip had already fallen, and the rest cascaded.
- Core with the same regression: `LateTakeOff` failed ("take-off Recovery … fell") and `late` failed ("0 recovery,
  fell"). Apex, Late, Plain, early and mid-window passed. The existing HazardRecoveryTimingTests failed as well.
- A first regression (`ApexSpeed` 4.0 → 2.5 m/s) was caught only by Core. On that loaded run the journey's presses
  arrived up to 3 frames late, so its "late" twirl still came near the top. This is an instrument limitation of the
  in-app timing steps, and a reason they belong in Core.
- The replay's two-frame input lag was measured from the journey's press log: the apex twirl comes at 0.30 s in
  both. The replay walks the same leap (7.17 m on a 19.1 m bridge).

**Instrument defect found and fixed:** after the trim, the plain jump came straight after the flight back from a
moon. Stood on the bridge [direct], it read the gap ahead as "chasm", twice. The cause is that the gap is read along
the camera, which the fresh arrival still frames. Waiting for `CameraSettled` did not help, so the plain jump now
walks to the near side first, as the late twirl's run used to.

**Time log (this stream; `tools/fs.sh timing`):** active 1,244 s (13 logged rows: 7 planets-hazard runs 631 s, 5 stages 560 s, 1 build 53 s), lock-wait 1,999 s, reruns 869 s in 9 rows. The reruns, with their reasons: 2 regressed builds (an ApexSpeed regression the journey missed under load, then the PLANET-85 + MaxDepth one), the base again beside the after run (same load), and 2 instrument fixes to the trimmed plain jump (camera settle, then the walk). The first two stages were logged before the nested-wait fix: the second one hid a 45.2 s smoke-launch lock wait in its 188.5 s. Not in the log: ≈ 6 focused Core runs on WSL (≈ 40 s each, no lock) and ≈ 6 harness runs (≈ 5 s each).

**Files other streams may also touch:**
- `tools/verify.sh`: the full tier lost the three 1.25 lines; the finish line changed.
- `tools/verify-lib.sh`, `tools/fs.sh`, `tools/verify-selftest.sh`, `docs/windows-workflow.md` (the tier table
  and a new section).
- `app/Scripts/PlanetsProbe.Hazard.cs`.

### Stream 4: every departure from a launcher, Esc takes a dialog's move back, F again turns back (PLANET-120..124, AD-84)

DJ: "Some dialog-based navigation still launches the character from wherever they stand … route departures through the
intended launch-pad behaviour … Make F / Face reversible." The inventory of entry points is in AD-84.

**Reproduced first, in Core:** `DepartureTests` builds the query each entry point builds. Against the earlier rule
(the destination's own pad only), 12 of 23 failed. These were the deeper Destinations row / Straight there, a favourite
elsewhere, a repository below, Backspace further away (two cases), the breadcrumb / Ctrl+Home, Locations / Ctrl+L,
Ctrl+O's new root, the walkthrough's way back, the telescope, a link deeper down, and the top of the tree. The other 11
passed (the entry points that already had a pad, plus the pad-less, sky and cap cases).

**Changed:**
- `DeparturePlanner` (Core) chooses the launcher: the link's, the destination's own, the pad on the way down, or the
  way back. The run's cap (0.8 s, 1.1 s on a Hazard World) now lives in Core.
- `PlanetWorld.Depart` calls the planner for every entry point. The wind-up on a pad that leads on looks up at that
  world. A pad-less departure kindles a ring at the feet. A run starts from the feet's height.
- `PlaceNavigator.CanRevert/Revert`: Esc before the take-off takes a dialog's move back.
- Ctrl+O's new root is travelled to, not cut to.
- `FaceToggle` (Core): F again turns back if it is the same world and selection, you moved ≤ 2.5 m, looked away
  ≤ 20°, and it is within 20 s.
- Help (F1) and the card's tooltip say so. `DepartFor(target, DepartureEntry.Telescope)` is there for stream 5.

| Area | Status | Evidence |
|---|---|---|
| The rule per entry point (PLANET-120) | **Demonstrated** (Core) | `DepartureTests` 23/0 (12 failed first) |
| Dialog-based departures in-app | **Demonstrated** in-app (sample) | `planets-travel` 25/0. Shift+Enter Straight there three levels down: run 0.38 s to Long Way Down's pad, wind-up, warp from it (take-off 1.03 s after the command; before: 0.38–0.62 s, from where you stood). Ctrl+L to the same place: Toward, take-off 1.15 s. Backspace from there, after the trip and after the warp: WayOut, run 0.60 s, take-off 1.01–1.05 s |
| Esc on a dialog's departure (PLANET-121) | **Demonstrated** (Core, in-app) | `DepartureRevertTests` 3/0. `planets-travel`: Ctrl+L, Esc in the run: still at the Starter Sky's top, history as before, marks `depart-run cancel` |
| Quick and continuous (PLANET-122) | **Demonstrated** in-app | Pad Space 0.39 s (unchanged). U 0.97–1.23 s, G far 1.20 s, the list 1.61 s, as in AD-74. 17 of 17 landings feet first (tilt 0°, foot 0.00 m, head 0.82–0.84 m). The kindle was not seen in-app: the sample's top world has no belt moon |
| F reversible (PLANET-123) | **Demonstrated** (Core, in-app) | `FaceToggleTests` 8/0. `planets-travel`: F turned 83°, F again ended 0.0° from the start, a third F faced again |
| Help (PLANET-124) | **Demonstrated** (text) | `SelectionActionsTests` |
| Related journeys | **Demonstrated** in-app (staged `2ee076c` + the state fields) | `planets-selection-actions` 44/0, `planets-tutorial` 36/0, `planets-navigation` 47/0, `planets-hazard` 84/0/3, `planets-flows` 27/0/2: one conditional labels check did not run (it needs a world in view after the camera turn: instrument variability, not travel) |
| Real input (`os-planets-probe.ps1 -Depart`, staged `d26735866914`, Guard on every input) | **Demonstrated** | 5/0. J, "old bridge", Shift+Enter: Departing › WindUp › Warping › Walking, Toward, run 0.6 s, landed on Old Bridge. Backspace: WayOut. A real Esc in the run: still home, history as before, no take-off. A real click on a world: F turned 178°, F again ended 0.0° from the start (`%LOCALAPPDATA%\FileScape\evidence\2026-11-20-stream4\depart-2`) |

**Journey time:** `planets-travel` 63.0 → 84.0 s (15 → 25 checks: the launcher per dialog entry, Esc's revert, F).
Nothing was trimmed. The Core tests cover the *decision*, and the journey still checks each in-app wiring path once.

**Open / for DJ:** whether "up and over" through the way back reads right for a far place; whether the kindle is
visible enough; F's window (20 s, 2.5 m, 20°). The overview's "Zoom to (F)" is not reversible (stream 2's camera).

### Stream 5: the telescope's survey, Star Chart Dorian for the overview, the little light (PLANET-125..129, AD-85)

Branch `worktree-agent-af9cc87872401dfec` from `7680d22`. Not exported or installed; journeys ran on staged builds of the
worktree (`fs.sh check journeys`). Evidence: the journeys' folders under `%LOCALAPPDATA%\FileScape\evidence\checks\`
(`journeys-20261120-183053`, `-184303`, `-185821`, `-191155`, and the last lookouts run below) and the recordings in
`%LOCALAPPDATA%\FileScape\evidence\2026-11-20-music\` (README there).

**Seen on the base first:** E on the platform opened this world's Destinations list (the panel J opens anywhere) with
a caption; the telescope itself only flared. The overview played the Ionian star chart from 84 of 132 places, the
Starter Sky among them. `MusicActivityTests` (written before the change) failed 3 of 6 on the base: the overview's piece
from a major place, its mode in the synth, the telescope's piece by activity. The hysteresis and visits cases passed (they
pin behaviour that was moved, not changed).

| Area | Status | Evidence |
|---|---|---|
| The telescope on approach: sign and prompt in the landmarks' words; E and a twirl beside it do the same; Help line (PLANET-125) | **Demonstrated** in-app | `planets-lookouts` 65/0 (four runs): "E or twirl (Space, Space) · look through the telescope"; E opens the view (entry E), a jump and twirl opens it (entry twirl); on the Starter Sky it frames 8 worlds, 3 more counted below the horizon |
| The view: rise to the eyepiece, round field, one world framed at a time with its relation to where you stand, ← → round the sky, zoom, drag, click (PLANET-126) | **Demonstrated** in-app and Core; look: DJ | Core `TelescopeSurveyTests` 6/6 (written first: five failed to compile on the base; the horizon case was added failing after the runs showed it); in-app: the view on the framed world 0.0° off its centre through a ≈ 25° lens, each caption naming the world and its relation to the Starter Sky (e.g. "samples / the world Starter Sky is inside · U flies up to it / 1 folder · 0 files · 7 of 11"), → frames the next, the framed world is the selection |
| The way back and on: Esc to exactly where you stood, Enter departs through G's departure, T the overview with it chosen (PLANET-127) | **Demonstrated** in-app | Esc: 0.0 cm, the traveller back in view, the lens back to 61.8°; T: the overview opens with the framed world selected, Esc there lands back on the platform (0.0 cm); Enter: departure "run" (to its pad), lands on the framed world |
| Star Chart Dorian from every place; the deep sky later; the Ionian chart and the second variation reserved; the Lydian overlook through the telescope; activity → piece in Core (PLANET-128) | **Demonstrated** (Core and in-app); by ear: DJ | `MusicActivityTests` 6/6 (3 failed first); `SkySynthTests.Experience` updated (all 132 places Dorian; the pieces' grid, loudness and voices incl. the deep sky and the reserved ones); the reserved arrangements' fingerprints unchanged; in-app `planets-music` 11/0: overlook Lydian on B-flat from its intro, the telescope keeps it, the departure ends it at once, Constellations' own music on landing, the overview Dorian on B-flat (the Starter Sky is major), back to the hub, 0 underruns |
| Recordings from the running game (PLANET-129) | **Demonstrated** (recorded); by ear: DJ | `2026-11-20-music\`: telescope approach 8.9 s, use 21.6 s, departure 13.2 s, overview enter 18.0 s, leave 16.0 s, and the whole 92 s capture; peaks 0.33–0.61 |
| The little light as a design tenet | Done (docs) | `docs/architecture.md` Design tenets; AD-85 |

**Instrument and product defects found on the way.** *Product:* the first view framed through the telescope's
surroundings — a blurred gold disc filled the field (the near geometry and the Starter Sky's book tower, which stands
beside the overlook); fixed by moving the near clip out to 1.6 m at the eyepiece and hiding the landmark while the eye is
there. Then Music Box, which hangs over the far side of the Starter Sky, was framed through the planet's own ground haze:
worlds whose line of sight passes through the planet are now left out of the sweep and counted ("2 below the horizon (T
shows them)"; Core test first). The overlook's hint sat under the caption (hidden while looking); the caption overlapped
the HUD's key line (moved up). *Instrument / process:* one stage failed because I built the app while the stage exported the same tree
(`FileScape.App.dll` missing); rerun, nothing else changed.

**Does the telescope earn the climb? (judged from the runs, screenshots and the capture; DJ to confirm by eye and ear).**
Before, no: the climb ended in the same list J opens anywhere. Now mostly yes, with reservations. The climb ends in
something only the platform gives: you look out from where you stand at the worlds actually hanging round you, each named
and placed in relation to this one, and you can go to any of them or open the overview on it. The Lydian piece starts as
you arrive on the platform and carries the whole look-out (25 s in the recording) without a change, which is the
"moment" the music was written for. Reservations: (1) on a world with few folders it frames two or three bodies and the
survey is thin — the overview (T) is still where the whole neighbourhood is; (2) the departure cuts the Lydian piece at
once (the flight's build takes over) — correct by the scheme, but it may feel abrupt after a contemplative view; (3) the
view shows the same bodies the walking camera can see by looking up, only closer, so its value is the framing and the
words more than new information.

Core (WSL): the focused sets pass (`MusicActivityTests`, `TelescopeSurveyTests`, `SkySynthTests.Experience`); the full
SkySynth run under load average ≈ 13 failed 8 render-cost bounds (60 ms, measured 60–106 ms), the known shared-machine
instrument issue (AD-80); they do not touch this change's code — rerun alone (load ≈ 1): 9 of 10 passed, the Hazard bound once at 60+ ms and then 55 ms on its own rerun (a flaky bound, not this change). Core renders of the deep sky and the reserved arrangements: `2026-11-20-music\renders\` (peaks ≤ 0.23, −26.3…−27.9 LUFS).

**Journey time:** `planets-music` 164 s → 21.9 s (a non-recording run; 92.6 s when recording for listening): the second
climb's resume at a section and the quick hops' dwell are Core tests (`SkySynthTests.Revisits_…`,
`The_minimum_dwell_lets_the_first_phrase_finish_through_quick_hops`, `MusicActivityTests`); the long listening waits
run only with FILESCAPE_AUDIO_RECORD. `planets-lookouts` grew from ≈ 60 s to ≈ 72–77 s (the telescope's view, its twirl,
T and a departure and return).

**Open / for DJ:** whether the deep sky sounds like the chart you chose; the rise (0.9 s) and swing timings, the lens's
framing (a third of the view); whether the reserved uses suit the Ionian chart and the second variation; whether the
telescope should also frame siblings or farther places (today the overview's job).

### Stream 6: the picture world as a gallery, Windows' thumbnails, media worlds, the yellow cube (PLANET-130..136, AD-86)

Branch `worktree-agent-a2c2a4ab8c25ac9ed` from `7680d22`. Runs from the worktree project (`fs.sh run`, the shared
launch lock held per launch), not an exported build. Evidence: `%LOCALAPPDATA%\FileScape\evidence\2026-11-20-stream6\`
(`base-cube` = the stray highlight reproduced before its fix; `work-*` the fixture; `real-*` DJ's OneDrive Pictures and
a Screenshots folder, counts only in the logs, screenshots local). Fixture: `FileScape.Fixtures create-gallery` (now with
unmarked crowds of 3, 30 and 300 pictures beside the stress gallery).

**Found first:** on DJ's PC the Pictures known folder is redirected to `OneDrive\Pictures`; its top level holds 42
pictures, 4 videos and `desktop.ini` (49 of 53 files cloud-only: attributes `0x401620`; the rest `0x420`, synced and on
this PC); Saved Pictures is **empty**; Screenshots folders hold 1 and 31 pictures. Every synced file carries OneDrive's
Cloud Files tag, which FileScape read as "placeholder", hence a warning on every photo. The yellow cube is the legacy
views' selection box left drawn after switching to Planets (AD-86): reproduced on the base code (`base-cube`: "drawn in
Places True; after Planets drawn True").

| Area | Status | Evidence |
|---|---|---|
| Media character by marker, contents, picture library (redirected Pictures), inside a gallery; mixed folders ordinary (PLANET-130) | **Demonstrated** (Core, in-app, DJ's folders) | `MediaWorldTests` 6/6 (written first, 6/6 failing on stubs); `GalleryTests` (Sketches inherits, Desk ordinary, a crowd by contents); `planets-gallery`: Sketches fills by itself, the Desk reads nothing (0 asked); DJ's Pictures and Screenshots: Gallery (MostlyPictures), the arrival card "a picture gallery of 42 pictures" |
| Windows' thumbnails first, then FileScape's decoder; bounded (PLANET-131) | **Demonstrated** | `ShellThumbnailTests` (Windows) pass; `ThumbnailPlanTests` 8/8 (7 seen failing on stubs first); fixture: 136 answers from Windows' cache, median 8 ms (p95 12 ms); DJ's Screenshots: 46 from the cache, median 14 ms; peaks 2 in flight, ≤ 62 shown, ≤ 3 sharp everywhere |
| Cloud-only thumbnails | **Provisional** | On DJ's PC every cloud-only picture Windows had **cached** came from the cache without a download; for the uncached ones Windows' extraction returned nothing (`0 extracted`, 24 then 8 "none"), so FileScape fetched (downloaded) the picture to decode it — allowed by DJ, ≈ 0.4–2.5 s each. Since the last change a canvas fetches only pictures ≤ 8 MiB by itself; larger ones wait for E or R. **17 of DJ's cloud-only pictures in `OneDrive\Pictures` are now on this PC** because of the first real run (contents unchanged; nothing pinned, moved or renamed) |
| Readable canvases: scale 2 facing the arrival, fitted in a mat; 3 / 30 / 300 (PLANET-132) | **Demonstrated** (Core, screenshots); composition by feel: DJ | `PlanetComposerTests` 3/30/300 (written with the change, not before); crowds: 3 + 0, 30 + 0, 62 + 238 in the pile, radii 17.6 / 26.2 / 31.9 m; screenshots `work-4\14..16` |
| Loading and failure on the canvas; near pictures sharpen; frame time (PLANET-133) | **Demonstrated** on the fixture; **provisional** on DJ's folder | fixture frames while 62 canvases fill: p95 18 ms, max 35 ms; the damaged picture's canvas "failed: could not be decoded"; beside the dawn picture its canvas is sharp. DJ's Pictures: p95 41 ms, max 214 ms while filling (the planet was recomposed once its listing finished; not separated) |
| Fill time | **Demonstrated** (fixture, before the last change) | crowd of 300: first canvas 16 ms, half 2.4 s, all 62 in 5.9 s; crowd of 30: all in 2.4 s; DJ's Screenshots (31): all 30 in 3.2 s. The plan asked only every 0.2 s although the cache answered in 8 ms: it now plans again as soon as a request finishes. After that change (`work-5`, 62/0) the gallery's 11 canvases were answered within 0.1 s of the arrival settling, and in each crowd (3, 30, 62 of 300) every canvas was already filled when the arrival had settled (Windows' thumbnail cache warm from the earlier runs) |
| Memory | **Demonstrated** | canvas textures 0.8 / 7.5 / 15.5 MiB at 3 / 30 / 62 canvases; session cache ≤ 30.5 MiB (of 64); Godot texture memory 318 → 349 MiB across the three crowds; working set 671–706 MiB (the whole app) |
| Progressive viewer, cloud fetch, oversize / 63 Mpx / HEIC via Windows, damaged refused (PLANET-134) | **Demonstrated** | fixture: a picture in the viewer's first frame (canvas thumbnail or Windows' 1,024 px in 7–11 ms), then the decode; the 24.1 MiB poster and the 63 Mpx scan shown from Windows' thumbnail with the reason; damaged refused. DJ's folders: quick picture in 13–41 ms, cloud-only photos fetched and decoded in 0.4–2.5 s (Pictures) and 0.7–12.5 s (Screenshots; three wide ones 11.6–12.5 s); GIFs shown from Windows' thumbnail |
| No warnings for ordinary photos; Open without the question (PLANET-135) | **Demonstrated** | Core verdicts; preview of a cloud-only photo shows the picture (no "cloud file" text); Enter in the viewer: no question, Windows opened it (`real-screenshots`, `--open-one`: Photos started, PID tracked and closed by me afterwards; it was not running before) |
| The yellow cube (PLANET-136) | **Demonstrated** | failed on the base code, passes after (fixture, DJ's Pictures, Screenshots) |
| Existing journeys after the change | **Demonstrated** in-app | `planets-gallery --root=gallery` (the app's sample, now version 2 with the Desk) 46/0; `planets-journey` 41/0 (the Starter Sky's Garden Photos is now a gallery by its contents); the fixture journey 62/0 (`work-5`), added to `verify.sh` as `planets-gallery-fixture` (skipped when no `create-gallery` fixture with crowds exists) |
| Runs and time | — | 11 launches, 451 s active, 2,940 s waiting for the shared launch lock. Reruns: `work-1..3` (a stale planet reference in the probe after the Places round trip, then a diagnosis line — instrument), `real-pictures-2` (the same stale reference in the fill measurement — instrument), `work-4/5` (after the fixes). No product rerun |
| Core | WSL 801 passed, 29 skipped, 5 failed — all five are render/layout **timing bounds** (`…_is_cheap`, `Layout_is_deterministic_and_fast…`) in code this stream did not touch, measured at load average ≈ 7.6 with other streams' runs (**environment**) | `tools/fs.sh test-wsl` (227 s) |

**How to play:** any folder that is mostly pictures — or any folder in your Pictures (also when Pictures lives in
OneDrive) with some pictures — is a gallery: the pictures stand large, facing where you land, and fill in as you walk
(nearest first; the one beside you sharpens). E on a picture, R to view it large: it appears at once and sharpens; a
photo only in OneDrive is fetched (the caption says so). Enter opens it with Windows, without a question.

### Stream 1 — the overview follows attention at any depth (PLANET-101..109, AD-81)

DJ: "Wherever I focus, I should be able to see and explore the surrounding filesystem. The current 1,500-world budget
becomes visible as a failure." Branch `worktree-agent-a4bcda4b3c3edd056` from `7680d22`. Staged (not installed):
`%LOCALAPPDATA%\FileScape\staging-20261120-193820-2389243` (`f2514ea` + uncommitted docs). Evidence:
`%LOCALAPPDATA%\FileScape\evidence\2026-11-20-stream1\` (`att-*`, `bodies-*`, `wsl-route-1`) and
`…\evidence\checks\journeys-20261120-195315\`.

**The failure, reproduced first in Core through the app's own calls (product defect):** the layout's 1,500-world budget
went breadth-first from the root and every relayout pinned every world already drawn. Once the explored branches filled
it, a selected world's listing arrived but its children stayed folded ("+N more"); discovery, offered only laid-out
worlds, had nothing left near the selection and said "done … 1,500 worlds"; a fresh overview at a deep world spent the
budget on the shallow levels and drew the world alone. On the base code: selecting down a 14-level spine after
exploring twelve wide branches revealed children at the first level only ("found 1"); reopening at level 10 drew the
world with 0 of its 5 folders. The indicator's longest line ran past its 520-wide label beside the button.

**What changed (AD-81):** the budget is spent by attention (`OverviewAttention`, `UniverseLayout.Attention`): the selected
world, where you stand, the approached world and the last four attended to, the ways to them first, a background of two
levels for scale; nothing is pinned but the way to where you stand; distant detail folds in place (each system keeps its
room while the overview is open); discovery only within four levels of attention; the indicator in two lines ("✓ Round
app: complete, 3 levels" / "312 farther folders wait until you look there · 4,000 worlds") beside a fixed button; the
budget 1,500 → 6,000 from measurements; only shown worlds, rings and hazes are sent to the GPU each frame; a folded
system's haze holds a cluster of faint lights.

| Area | Status | Evidence |
|---|---|---|
| A selected world reveals its folders at any depth after many branches were explored (PLANET-101) | **Demonstrated** in Core (failing first on the base) and in-app | `After_exploring_wide_branches_every_selected_world_down_a_deep_spine_reveals_its_children`; `att-3`: three distant regions and a sector in each, then Region 09's route passage by passage to Passage 14 (level 16 below the arrival): every focus's folders drawn (102 PASS / 0 FAIL) |
| The overview reopened at a deep world shows its folders at once (PLANET-102) | **Demonstrated** in Core (failing first) and in-app | `Reopening_the_overview_at_a_deep_visited_world_shows_its_children`; `att-3`: landed on Passage 12 (level 14), T: 5 of 5 folders laid out at once, neighbourhood 19 worlds complete at 0.0 s |
| Three levels of the neighbourhood unfold and complete (PLANET-103) | **Demonstrated** | Core `A_deep_neighbourhood_unfolds_three_levels_and_is_complete_while_far_branches_wait` (5 / 17 / 17 per level); in-app every focus complete within 0.0–8.2 s (a cluster's 79 worlds in ≈ 8 s at 10 listings a second) |
| No recursive discovery far from attention (PLANET-104) | **Demonstrated** in Core | `Discovery_never_recurses_far_from_attention` (far branches listed two levels below the root at most). In-app the deep fixture's discovery journey now lists to level 5 in 30 s (619 worlds) where it listed to 6 (905): the reach, as intended |
| Distant detail folds in place; nothing relisted; returning is immediate (PLANET-105) | **Demonstrated** in Core; in-app the return | `Distant_detail_gives_way_in_place_and_returns_without_listing_again` (without the kept room 28 m of drift on a 48 m ring, with it < 1 ‰; fails without `Reserves`); `att-3`: back at Region 01/Sector C/Cluster 4 after the route, its 6 folders laid out within 1 s and its 79-world neighbourhood complete at 0.0 s. In-app with the budget full: `att-4-budget1500` (`--test-overview-budget=1500`): the budget full from the third region on (up to 508 folders folded by it), every focus down to Passage 14 still unfolded and completed, the return to Cluster 4 laid out again and complete at once, 102 PASS / 0 FAIL |
| Stable camera and selection; ring order (PLANET-106) | **Demonstrated** | `planets-overview-discovery` 26/0 (jumps, anchor, camera never reset) and `planets-overview-navigate` 13/0 on the staged build; **product defect found and fixed**: with attention the bodies came in cost order, so [ after ] did not step back (`navigate` 11/2 on `b7cc538`); Core `The_layouts_order_is_breadth_first_by_name_whatever_is_selected` (fails without the fix) |
| The indicator: complete here vs deferred elsewhere, never "done" at a budget, fits at UI 1.0 and 1.25 (PLANET-107) | **Demonstrated** in Core and in-app; the wording: DJ | Core `The_status_tells_a_complete_neighbourhood_from_discovery_deferred_elsewhere`; `att-3`: every focus "complete" and its text within its label and clear of the button; at 1.00 and 1.25 (`07-`/`08-attention-status-*.png`) |
| The budget from measurements; far systems as haze and lights (PLANET-108) | **Demonstrated** (measured); the look: DJ | `bodies-2`/`bodies-3` (table in AD-81): with the level of detail on, per-frame cost no longer grows with what is laid out (46,000: p50 39.6 → 12.9 ms); the relayout on the main thread does (6,000: ≈ 35 ms; 20,000: ≈ 140 ms), so 6,000 |
| The real route (PLANET-109) | **Demonstrated** (counts only) | `wsl-route-1`, `\\wsl.localhost\Ubuntu-24.04\` → `home\david\src\filescape\app` (5 levels): at every step the folders drawn and the neighbourhood complete within 1.0–8.6 s (the root's: 428 worlds, 12 can't be listed); 886 worlds laid out of 6,000; no names logged, no screenshots |
| Frame time while attention moves | **Demonstrated** (vsync) | `att-3`: 2,888 frames p50 16.7 / p95 16.7 / p99 18.1 ms, max 66.6 ms; longest relayout 21.3 ms |
| Core | **Demonstrated** | the area's tests (layout, discovery, detail, attention, baseline) 34 passed on WSL; behaviour baseline identical before/after (the layout without attention is unchanged). The full suites are the integrator's |

**Instrument defects:** `att-1` failed 10 of 41 "the indicator says complete" checks: the indicator works out its counts
every 0.25 s and the probe read it in the same frame as its own count (it now waits up to 1.5 s). The discovery journey's
"a small indicator says discovery is going on" read only the first line; at 0 s the arrival's neighbourhood is already
complete (the visit listed it), so it now reads either line (an expectation updated for AD-81). **Product defects found
on the way and fixed:** the ring order above; the overview camera's `LookAt` failed far out in the vast overview (Godot
judges a few metres "the same position" at hundreds of km; the camera now looks along its offset).

**Open / for DJ:** how far old neighbourhoods should persist (`RecentFoci` 4, `RetainBonus` 0.5); whether three levels is
the right neighbourhood; the indicator's wording; the haze's lights; the deep fixture's 30 s view drew more overlapping
pairs in this run (38 % of on-screen worlds at the journey's close zoom, against 6 % in stream 4's run; not
investigated, the camera's place differs between runs); a larger budget wants the layout off the main thread. Not run:
real input (`os-planets-probe.ps1 -Discovery`), `check full`.

**Time (this stream):** app launches (journeys, measurements, journey checks) active ≈ 836 s, plus two stages ≈ 165 s of
their own; waiting for the launch lock ≈ 2,880 s (the desktop shared with seven streams; one first attempt was stopped
in the queue to move the lock wait out of its timeout); reruns: `att-1` (instrument),
`bodies-2`/`-3` and `att-2`/`-3` (after each optimisation, on new staged builds), the discovery/navigate pair (after the
ring-order fix). Core: the attention tests run in ≈ 7 s (WSL).

## Fifth playtest follow-up (2026-11-19)

### Stream 3: every panel's keyboard and mouse ownership (PLANET-115..119, AD-83)

Branch `worktree-agent-a7cd53113f299da62` from `7680d22`. Not exported for delivery; app runs were the worktree
project (`fs.sh run`) and one stage (`staging-20261120-182632-2170182`, `2e1f64d`) by `check journeys`.

**Reproduced first (Core):** `PanelOwnershipTests` against a table that mirrored the old rule: 5 of 10 failed. The
location actions were never a keyboard panel (AD-68 listed them among the overlays that leave the keys to play), so the
walk read the arrows every frame and only the number keys, reaching the world after everything else, chose rows. The
welcome and Settings had no focusable control. A world click with Destinations or the reader open acted behind them;
with Settings or Locations open it was dead and the panel stayed.

| Area | Status | Evidence |
|---|---|---|
| One ownership table in Core (`PanelOwnership`), read by `Main.KeyboardOwner`; every panel × every kind of key, the owner, every panel × a world press or click (PLANET-116/118) | **Demonstrated** (Core) | `PanelOwnershipTests` 10/10 (5/10 against the old rule) |
| The location actions own the keys at once, by E or by a jump and twirl: ↑ ↓ Tab Home End move the highlight, Enter runs it, 1–9 run their rows, E/Esc close; ↑, W, ←, Space held move nothing (PLANET-115) | **Demonstrated** in-app | `planets-dialog-keys` 138/0 (sample with a root's look: E and a twirl, 0.00 m moved, Enter recorded the terminal); `planets-landmarks` 74/0 (38 s; 71/3 before the probe's Home fix) |
| Settings opens on its first setting (Tab, Space); the welcome answers with ← → / Enter, Esc is "Explore on my own" | **Demonstrated** in-app | `planets-dialog-keys` |
| A world click closes Settings, Locations, Help (on the press), Destinations, the reader, the location actions (on a click); it selects nothing, S walks again, the next click is single; a right-drag beside the list still turns (60°) and the list stays; a confirmation and the welcome keep the click (PLANET-117) | **Demonstrated** in-app | `planets-dialog-keys` (a click on a world in the sky where one showed, else empty ground) |
| A panel closing over another hands the keys back (F4 over Destinations, Ctrl+L over the location actions, then Esc) (PLANET-118) | **Demonstrated** in-app | `planets-dialog-keys` |
| Real input (PLANET-119), staged `ac3a6278b939` | **Demonstrated** across two runs; a third stopped | `-Dialogs` run 1: 101/2 — every typed-keys case as before; a real click on a world closes Settings and selects nothing; a real right-drag beside the list turns 90° and it stays; then on the fixture with a root's look, real keys steer to the gateway, E owns the keyboard on the terminal, Up/W/Left held move 0 m, Home/Down/Enter records Explorer, 3 copies (recorded), Esc; a real jump + twirl opens on the terminal, Down/Enter; F4 over them then Esc hands the keys back; after travel and back, the same (the 2 failures: no world in view for the Locations and Destinations clicks, instrument). Run 2 (open sky as the fallback point): 87/0 — Settings, Locations, Destinations each closed by a real click, nothing selected, S walks again; its second launch stopped before any input (not logged then; the probe now logs stops). Run 3: stopped by Guard before input (another app, ChatGPT, was in front). No input went to another window |
| No regressions nearby | **Demonstrated** / one timing bound | `planets-navigation` 46/1 on the project: the one failure is "the list stays responsive" (slowest rebuild 17.1 ms against a 16.7 ms frame; a Debug project run beside other streams; not this change's code) |
| `planets-dialog-keys` trimmed | **Demonstrated** | 172 s (fifth pass) → 89 s on the staged build with the new cases (128/7 then; 138/0 on the project after the instrument fixes, ≈ 97 s) |

**Instrument defects (fixed):** the trimmed typed set had lost End after Home; Ctrl+V pastes the real clipboard into the
filter (long text, mid-text caret), so clearing it needs End and up to 300 Backspaces; the click-outside point fell on
the reader or Help when no world showed (a grid search now); a held ↑ in `planets-landmarks` moved the highlight before
the ↓ check (Home first). **Changed on purpose** (AD-83): Esc closes the owner, not the first panel in the old peel
order; a second twirl no longer arms Explorer (↓ does); Enter on an E-opened panel runs its first row.
**For DJ:** whether a drag beside Destinations should close it too; whether ↓ is as quick as the second twirl.

### The final candidate (`70b6ffc7ebb6`, staged, not installed)

Seven streams merged (travel, camera and Hazard, selection card and shutdown, discovery and the large universe, tutorial /
way back / gallery, music, testing time), then fixed from the merged runs. Staged at
`%LOCALAPPDATA%\FileScape\staging-20261120-062533-1902637\FileScape.exe`; the installed build is still `6b9b60c274af`.
ZIP: `%LOCALAPPDATA%\FileScape\FileScape-70b6ffc7ebb6-win-x64.zip` (74.7 MB, 199 entries, sha256 `c2fa12ef…`),
checked from a clean extraction (Expand-Archive into an empty folder; smoke launch `smoke ok build=70b6ffc7ebb6`; no
logs or machine paths; README.txt is `docs/windows-playtest.txt`, now with the Command Prompt routes). A fresh clone of
`70b6ffc` staged (36 s) and packaged by itself: the same 199 entries, the DLLs identical, only `FileScape.exe` and
`build-info.txt` differ (export time).

| Check | Result |
|---|---|
| `FS_LAUNCH_LOCK=… tools/fs.sh check full` on `70b6ffc` | **52 of 52 steps pass**, 2 skipped (the README journeys, run apart with `FILESCAPE_WSL_README`: 13/0, 33/0). Core: Windows 789 passed / 29 skipped, WSL 787 / 28, 0 failed. 2,633 s, no lock waits (43.9 min wall) |
| Time to the first journey | build 19 s, Windows tests 186 s, stage 28 s, the WSL tests (125 s) beside them: ≈ 233 s, against ≈ 1,007 s before TEST-05 (stream 7's baseline under the same load; journeys unchanged at ≈ 1,660–1,700 s) |
| Real input on `b072f62` (the merged build; Guard on every input; desktop idle ≈ 3 h) | `-Hazard -Recovery` **5 runs, 21/0 each**: the long jump crossed every time (board press, then a press at the top), the same jump without the twirl fell short, a press just after the 0.35 s window was an effort, not a catch. `-Look` usual 23/0, reversed 23/0; `-Dialogs` 79/0; `-Play` 15/0; `-Route` 10/0; `-Discovery` 14/0 (X pauses with a real key); `-InputFocus` 14/0; `-Audio` 8/0; controls 24/0 |
| Real input on the candidate `70b6ffc` | `-Tutorial` 30/0, `-Landmarks` 25/0 (the zoom step completes after 10 real wheel notches: 51 → 53 worlds in view, 60 drawn), `-Look` 23/0, `-Hazard -Recovery` 21/0, `-Dialogs` 79/0 |
| Real input found | `-Landmarks`: the tutorial's "5 · Zoom out" never completed with the real wheel — the level of detail folds systems as you zoom out, so ten more worlds never came into view (**product**, fixed in `0de688c`: the bar counts drawn worlds). `-Tutorial` once stuck at the book tower on the way to the pad (steering; 30/0 on the rerun). `-Look`: one smoothness step judged per poll, not per frame (instrument, fixed) |
| Merged runs found | `planets-selection-actions`: "Zoom to (F)" judged before the overview followed the card's selection (instrument). `planets-tutorial-cues`: near the tower the low walking view (PLANET-84) pins the cue at the horizon over the tower (instrument expectation, 55/0 since). `planets-overview-enter` once 34/1 when another program's dialog took the window's focus mid-attempt (35/0 twice since; see below) |
| Spills on DJ's desktop | Stream 3's open checks: an "Open with" dialog for a `.csv` (closed by its PID's window), Edge relaunching in the background (ended), and **Edge opening DJ's Personal profile and restoring its session** for the `.xml` fixtures (his saved session now holds fixture tabs). No input went to another window. A WebStorm "Start Failed" dialog was in front from about 21:52 (WebStorm running since 2026-11-17; not started by these runs as far as found) and was left alone |

### Walking camera return, the long jump with real keys, a slightly tighter catch (PLANET-84..86, AD-75; stream 2)

Branch `worktree-agent-a2b7fed00055a8890` from `31c9758`. Commits `44fbd64` (instrument), `55a3936` (PLANET-85),
`580bd4b` (PLANET-84), `b48b935` (press log), `6968d2f` (PLANET-86) and this documentation commit. Not exported,
staged or installed: every app run was from the worktree project (`fs.sh run`) or, for the base, the staged
`cfee25d2b709`.

**What was reproduced on the base build:**
- *Camera:* `planets-look` on the staged `cfee25d`: after the arrival drags left the chosen pitch at −12°, a 1.2 s walk
  kept it; a drag down while walking stayed at −44° for 2 s of walking. Nothing brought a sharp look back.
- *The long jump's second press:* the three `cfee25d` real-input runs (`2026-11-19-os-cfee25d*`): "buffered" at
  0.367 s, fell (twice), and a plain jump whose take-off press was a "catch" at 0.100 s. The Core replay of the
  probe's inputs at 60 fps on the unmodified walker: a take-off press 0.02–0.12 s after the feet leave the board's edge
  is the catch; it carries the walker back and spends the twirl, so the second press does nothing, and W still held
  walks it off again. Space on the board itself always crossed.
- **Instrument defect:** the probe timed presses from a state file written every 150 ms (≈ 1 m of walking between
  snapshots): the board press could land past the edge, the "0.04 s" catch press arrived at 0.13–0.17 s, the "0.33 s"
  at 0.42–0.48 s (outside the window), the fall's start was read late. **Product defect:** a person pressing a frame or
  two late at the board gets the same catch.
- Real input on the base this session: **not run** — the probe launched the staged build and stopped before any input
  (Windows would not bring the window to the front; the foreground was ShellHost and the desktop had been idle 7 min).
  Retried once ≈ 1 h later (`base-hazard-2`): the same stop, with another program's "Open with" dialog in front and the
  desktop idle ≈ 1 h. Nothing was sent anywhere; the launched builds were closed by the probe.

| Area | Status | Evidence |
|---|---|---|
| Walking eases a sharp look back to the usual pitch (2.5/s after 0.5 s); a held drag owns the camera; standing keeps any look; heading, distance and arrival framing kept; the overlook returns to its 34° (PLANET-84) | **Demonstrated** in-app; feel: DJ | `planets-look` 42/0 (base 35/0): sharp down 70° → 18.3° in 2 s of W, view −70.0° → −15.8° (usual walking −15.4°), largest step 1.42° a frame, heading 0.0°; sharp up −20° → 17.8°; a drag held while walking 48.0° → 48.0° for 1.5 s, then 18.2° after letting go; overlook boom 34.0° |
| The overview unchanged | **Demonstrated** in-app | the same `planets-look` overview lines as on the base: −31.5° → −60.2° → −60.2°, −60.2° → −2.9° → −2.9° |
| A take-off press just past a leap's board is the take-off (PLANET-85) | **Demonstrated** in Core and in-app | `The_real_input_long_jump_replayed_press_by_press` (both leap lengths, every coyote-time press crosses), `A_slip_off_the_side_of_a_leap_board_is_still_the_catch`; `planets-hazard`: take-off 0.083 s after the board "coyote-jump", twirl "apex-twirl", across |
| Catch window 0.40 → 0.35 s, measured one parameter at a time; too-late presses still an effort (PLANET-86) | **Demonstrated** in Core and in-app; feel: DJ | `The_tightening_candidates_measured_one_at_a_time` (table in AD-75); `planets-hazard` on the final build (see below) |
| Press log and fresh state for probes | **Demonstrated** in-app | `planets.presses` in the state file; each press logged in `planets-hazard` |
| Real input `-Hazard -Recovery` ×5 and `-Look` | **Unknown**: written and parse-checked, not run (see above) | — |
| Core (WSL) | 737 passed, 26 skipped, 0 failed; behaviour baseline identical before/after | `tools/fs.sh test-wsl` |
| No regressions | **Demonstrated** in-app | `planets-navigation` 47/0 (with its listing-delay instrument; a first run without the instrument's arguments failed its two listing checks, an invocation error); `planets-hazard` 87/0 |

Evidence: `%LOCALAPPDATA%\FileScape\evidence\2026-11-19-stream2\` (`base-look`, `work-look-1`, `work-hazard-1`
(fix, window 0.40), `work-hazard-2` (final), `work-nav-2`, `base-hazard-1`/`-2` (the stopped real-input attempts)).

**How to play them now:** slip off an edge? Space within about a third of a second (0.35 s) walking, about a quarter
running. Long jump: Space on the amber board — or just as you run off it — and again at the top of the jump.

**Open / for DJ:** how the camera's return feels (rate 2.5/s, wait 0.5 s after a drag; no dead band, so a small
deliberate tilt also returns while walking); whether 0.35 s is the right "slightly tighter"; a press during the
catch's own carry still does nothing (now visible in the log as `carry-ignored`). Real-input runs are for the
integrator: `os-planets-probe.ps1 -Hazard -Recovery` (5 runs), `-Look` (each `-LookPrefs`).

### Music by activity, place and visit; the minimum dwell; the aurora voice (PLANET-97..99, AD-79)

DJ, after the seven-mode reel: bring selected sketches into the game (the Lydian overlook on the overlook or telescope, an
overview identity), a small scheme for variations, phrases that develop through quick folder changes, keep the existing
arrangements, one DSP extension. **Reproduced on the base code first** (Core, the app's call sequence): twelve hops 1.5 s
apart cut the music twelve times, each world sounding 3–5 notes of the theme's 10-note motif; the overlook, telescope and
overview played the world's music (AD-72's pieces were offline only). Commits on `worktree-agent-a7e3217d9f49ef0a9`
(Core, app and journey, docs).

| Area | Status | Evidence |
|---|---|---|
| Scheme: activity picks the piece, place the key and mode (overlook Lydian; overview Ionian, Dorian where the place is Dorian: 84/48 of 132 places; Locrian never), visits the variation; existing arrangements where they were (PLANET-97) | **Demonstrated** (Core and in-app) | `SkySynthTests.Experience.cs`; behaviour baseline identical, all ten existing scores' whole-cycle hashes (visits 1–3) identical; `planets-music` 13/0 (journey-2) |
| Minimum dwell 3.5 s then the next two-bar boundary; hazard and activity changes at once; retargets, cancels, arrival chord in the key sounding (PLANET-97) | **Demonstrated** (Core); in-app **demonstrated** on the sample's hops (2 world changes through 6 arrivals 2–3 s apart, each after ≥ 3.5 s; the hazard world at once; ends on the world you stand on); feel: DJ | Core: 12 → 6 changes, 10 theme notes a world; journey-2 |
| The Lydian overlook on the overlook and at the telescope (as heard, then its second light), the star chart in the overview, both in the world's key, live on a deck (PLANET-98) | **Demonstrated** in-app (B-flat on the Starter Sky; returns resume at a section); by ear: DJ | journey-2; renders peaks ≤ 0.23, −26.3…−27.6 LUFS, music voices ≤ 23/28, no gaps |
| The aurora voice (driven saws → resonant SVF with its own envelope, velocity, drift), in the star chart and the second light; before/after (PLANET-99) | **Demonstrated** (measured); by ear: DJ | 12 ns per voice-sample Release (0.05 % of a core per voice; 30 ns Debug), the saw stack 9; extremes finite; `dsp-*.wav` |
| Playback stability | **Demonstrated** in-app | journey-2 (≈ 2 min 40 s of music, 0 underruns after the first sound, voices ≤ 35/40); the capture scanned: true peak 0.61, no sample at full scale, no silent window |
| Core suite | WSL 752 passed / 27 skipped / 5 failed: four render-cost bounds (second theme, 75–84 ms vs 60) and the layout speed test, all timing under the other streams' load (load average ≈ 7); the five pass run alone (instrument: shared machine) | `tools/fs.sh test-wsl` (4 min 49 s) |

Samples and README: `%LOCALAPPDATA%\FileScape\evidence\2026-11-19-music-integration\` (in-game captures and per-piece
renders, durations there). **Instrument defect found and fixed:** journey-1's dwell check failed because the probe read
the key of the world *waiting* for the dwell (`Key`) as if it were playing; `SkySynth.PlayingKey` now reports the key
sounding. **Unknown / for DJ by ear:** the star chart (new music), the second light, the aurora sound, whether 3.5 s
feels right; the overlook music also plays in the B overlook view (by design: it is the overlook's view). The Aeolian
lament and the Mixolydian launcher stay offline. Not exported or installed. Real-input probes not run (none needed: no
input changes).

### Tutorial card, the way back's look, a picture gallery (PLANET-94..96, AD-78)

Stream 5. Branch `worktree-agent-a00ee56f8b7f9b056`. Evidence: `%LOCALAPPDATA%\FileScape\evidence\2026-11-19-stream5\`
(`base-looks2` = the unmodified code with only the instrument added; `looks-*`, `tutorial-*`, `gallery-*`). Runs from
the worktree project with `fs.sh run`, not an exported build. Not exported or installed.

**Seen on the base code first** (`planets-looks`, 1600 × 900, UI 1.0): the card's steps were one plain sentence each
("Press J for this world's list, then choose Music Box (double-click, or ↓ and Enter).") with the keys repeated as caps
in a row at the card's right edge. On Constellations (29 pads, 8 moons) the way back's pad is pink-blue among pink pads;
from across the rings only its sign's small ▲ and "the way back" set it apart, and from the far side only the sign
shows (the pad is below the curve).

| Area | Status | Evidence |
|---|---|---|
| Card: the action leads in bold gold, keys as inline caps, one quieter tip line (PLANET-94) | **Demonstrated** in-app; readability by feel: DJ | `planets-looks`: steps 1, 2, 4, 8, 10, 13 each show all their keys; screenshots `looks-2\01..06`. Core `LessonTextTests` 4/4 |
| Walkthrough unchanged | **Demonstrated** in-app: `planets-tutorial` 36/0, `planets-tutorial-cues` 55/0 (titles, keys and steps unchanged); real input `-Tutorial`: **not run** (matches titles and the key list, which did not change) | `planets-tutorial`, `planets-tutorial-cues` |
| Way back: blue rim round the pad, a spire of climbing rings over it, a framed sign; only on the planet you stand on (PLANET-95) | **Demonstrated** in-app (built, visible in the shots); recognisable at a glance: **provisional**, DJ | `looks-2\07..12` against `base-looks2\07..12`. From the arrival the rim reads clearly; across the rings on Constellations the spire and frame mark it, though the sign overlaps the spire's lower half and the lighthouse stands behind it; on Garden Photos the spire shows over the horizon with the pad hidden. From the far side of a 29 m world (38 m) neither pad nor spire can show (the curve hides ≈ 80 m of height); the framed sign still does |
| Gallery easels fill by themselves in a marked folder, nearest first, bounded (PLANET-96) | **Demonstrated** in-app | `planets-gallery` on the fixture: 8 pictures shown on 11 picture easels (the three stress cases stay blank), 10 reads asked (≤ 32), the arrival's frames ≤ 17 ms while they loaded (only a few frames measured: they loaded within the first second) |
| Viewer: R (or ⤢) on a picture's preview; ← → through the folder's 11 pictures; wheel zoom about the pointer, drag pan, 0 fit; Esc/R back where you stood with the preview on the last picture; game keys inert (PLANET-96) | **Demonstrated** in-app; feel: DJ | `planets-gallery` 45/0 (`gallery-2`): moved 0.00 m with W, T, J, U, Space, G pressed while viewing; the camera's zoom unchanged by the wheel |
| Bounds: the large print 2560 × 1920 decoded at 2048 × 1536; 63 Mpx refused from its header in 2 ms; 24.1 MiB refused from its size with no read; damaged refused with the reason; ≤ 1 decode at once, ≤ 3 kept; six quick steps started 2 decodes; decodes 0–92 ms (199/247 ms on the first run) | **Demonstrated** in-app and Core | `planets-gallery`; Core `GalleryTests` 6/6 |
| An unmarked folder (Sketches) reads nothing by itself; E then R still views its 2 pictures | **Demonstrated** in-app | `planets-gallery` |
| The app's own sample (`--root=gallery`) | **Demonstrated** in-app: `planets-gallery --root=gallery` 39/0 (`gallery-sample2`; the sample has no stress cases). The first run failed one check: the prompt named a neighbouring easel nearer than the hopped-to picture (an instrument defect: the probe now checks the viewer prompt only when that picture is in focus; the fixture run `gallery-2` passed 45/0 with the stricter check) | `gallery-sample` |
| Existing journeys after the change | **Demonstrated** in-app: `planets-journey` 41/0 (Garden Photos' easels, nearest-first now), `planets-dialog-keys` 148/0 (keyboard ownership with the new viewer panel), `planets-reader` 33/0 (R still expands text; WSL readme fixture), `planets-looks` 9/0. Windows Core: 742 passed, 1 failed — `SkySynthTests.Rendering_a_revisit_with_the_second_theme_is_cheap` (a render-time bound, 61–76 ms against 60 ms, while other streams' journeys ran; untouched code: **environment**, not a product defect of this change); my tests 10/10 on Windows | |
| Core | **Demonstrated**: WSL 744 passed, 26 skipped, 0 failed (4 min 14 s) | `tools/fs.sh test-wsl` |

**How to play the gallery:** `FileScape.exe -- --root=gallery` (or from the Starter Sky press U: "Picture Gallery"
stands beside it). The easels fill with pictures as you walk. Walk up to one, **E** to look, **R** (or ⤢) to view it
large; **← →** step through the folder's pictures, the **wheel** zooms about the pointer, **drag** pans, **0** fits,
**Enter** opens it with Windows, **Esc** (or R, ✕) brings you back where you stood. Any folder of your own works the same
from E then R; put an empty file named `.filescape-gallery` in it and its easels also fill by themselves.

**Why the gallery:** it is the proposal whose interaction can be played today on generated data — you walk among the
pictures and step into one — and the one DJ named. The others need decisions first (below).

**The content-world proposals (`docs/next-worlds.md`, AD-61) — where they stand:**

- **Gallery (photos and videos):** prototyped here, narrower than the sketch. Built: recognition by a person-made marker
  (the per-folder "yes"), walking thumbnails (nearest first, ≤ 32, two at a time), the picture viewer, header-checked
  bounds. Already there before: the Meadow theme for picture-heavy folders, easels with automatic thumbnails in the
  Starter Sky only, the 512 px preview on E. Not built: known-folder, camera-name and mix recognition; the gallery path
  and month arches; the contact sheet; video frames; a saved per-folder choice or a "Show the pictures here?" prompt.
- **Reading Room (documents):** not built. What exists: the Library theme for document-heavy folders, the bounded text
  and Markdown preview (E) and the reading view (R) with usable links (PLANET-44/52/53). Missing: recognition, the
  reading nook (recent documents ring), version stacks.
- **Arrivals Port (Downloads):** not built (AD-61 ranked it first to prototype). What exists: `AgeBands`, piles, the
  list (J) and Reveal. Missing: recognition by the Downloads known folder, the arrivals board, parcels in age rings,
  partial-download parcels, its music.
- **Audio folders:** no world of their own. What exists: audio-heavy folders become Chime worlds (bell flowers, the bell
  tower, PLANET-56); E on a .wav/.ogg/.mp3 under 12 MiB plays a bounded preview with the music ducked. Opportunity: a
  listening room on the gallery's pattern — a marker as the folder's "yes", the nearest file's first seconds playing as
  you pass (one at a time, ducked, bounded like the preview), a step-through player like the picture viewer.

Instrument defects: the first `planets-looks` run looped forever skipping steps — skipping past "5 · The overview"
without opening it is undone at once by the walkthrough; the probe now opens the overview there (and has a guard). The
first "from the arrival" shot did not face the way back (the arrival framing held the camera); the probe now stands the
traveller facing it first.

### Travel: leave from the pad, land on your feet, the overview's Enter always lands or says why (PLANET-81..83, AD-74)

DJ: landings "on their backside, followed by an immediate snap upright"; take off from the destination's pad whatever
chose it (trips, parent, history, the overview, pad-less destinations too); and an overview Enter (Ubuntu root →
Destinations "home" → its world → Enter) that "heard a sound but stayed in Overview". Branch
`worktree-agent-a03bd450f2dc2296d`. Instruments: `--automate=planets-travel --root=sample` (every entry point timed from
the command to the take-off; every landing traced frame by frame to `landing-trace.csv`) and
`--automate=planets-overview-enter --root=<root-like WSL fixture>` (DJ's route in ten variants × 3, and a folder two
levels down × 3; `--test-list-delay-ms=6000 --test-list-delay-match=home` models a slow `/home`).

**Reproduced first (unmodified code, probe-only commits):**

- *Landing* (15 landings, `…\2026-11-19-travel\base-travel\`): in every touchdown frame the traveller was still in the
  full flight pose — lean 68°, lowest foot 0.52 m off the ground, head 0.67 m (standing 0.83 m): folded at the hips,
  seated from the chase camera. It then eased upright with a 1/6 s time constant; the landing frame is long (the new
  world is built in it), so the first step was 31° and up to 29° a frame on screen, upright 0.2–0.3 s later. Warps
  came down facing an arbitrary side and turned 90° in the touchdown frame. The landing squash never played (0 events).
- *Departure:* only Space on a pad started on a pad; G, the list, trips, U and Backspace took off where you stood.
- *Overview Enter* (`base-overview-rootlike`, `base-overview-delay`, `base-overview-deep`): 21 of 21 variants that
  choose the world in the overview landed, with and without the listing delay. Two silent failures: **a folder two
  levels down committed in the list opened in the overview** started a trip whose wind-up played and never ran (the
  overview ticks only itself): stayed in the overview, phase WindUp, trip active, 3 of 3 — DJ's symptom; and **a row
  clicked in the list, then the overview's Enter** acted on the overview's own "you are here" and glided back down
  without travelling, 6 of 6. Which one DJ met is not established (the first matches "a sound, stayed, Esc" best).
- *Instrument defect fixed:* the probe's double-click was two plain clicks (never a double-click); it now sends the
  second press flagged DoubleClick, as the other probes do.
- *Not done, on purpose:* the real Ubuntu root. Landing on `/home` lists the user's folder (the context listing goes
  two levels down) and the overview's discovery lists drawn worlds below the arrival, so it would scan a private home.
  The root-like fixture (`tools/make-wsl-fixture.sh --root-like`, the same 21 top-level folders and `home/fixture-user`)
  with the listing-delay instrument stands in.

**Changed** (`PlanetWorld.Departure.cs`, `PlanetWorld.Travel.cs`, `PlanetWorld.Universe.cs`, `PlanetWorld.Trip.cs`,
`AvatarRig*.cs`, `Main.Planets.cs`): a flight turns feet first over its last 0.45 s (a function of time) and faces the
way it will stand; warps rise and descend standing; one departure routine runs to the destination's pad (0.3 s + 1 s
per 30 m, ≤ 0.8 s; ≤ 1.1 s on a Hazard route), winds up on the pad, or bursts at your feet when no pad leads there; U and
Backspace move the navigator at the take-off, so Esc stops them; from the overview every commit lands through the
overview, the selection is shared both ways, refusals say why, and a safety net closes the overview over any travel
begun under it.

| Area | Status | Evidence |
|---|---|---|
| Feet-first landings (PLANET-81) | **Demonstrated** in-app; the look: DJ | `after-travel-2`: 15 of 15 touchdowns tilt 0°, foot 0.00 m, head 0.81–0.83 m, facing ≤ 1.7° in the touchdown frame, one squash event each; largest body turn a frame from touchdown 1.6–3.3° for flights (one 10.0°: the next trip hop's crouch), 17.7–23.9° once after two warps with the body upright on its own up (the camera) |
| Departures from the pad (PLANET-82), added time from the command to the take-off | **Demonstrated** in-app; the pace: DJ | before → after: pad Space 0.47 → 0.47 s (0); U 0.37–0.45 → 1.04–1.21 s (+0.6–0.8); Backspace 0.45 → 0.77 s (+0.38); G to the far pad 0.48 → 1.28 s (+0.8, the cap); the list's world 0.69 → 1.54 s (+0.8, the cap); trip first hop 0.67 → 1.45 s, later hops 0.87–0.93 → ≈ 1.5 s (+0.6); warps (Straight there, the history's far end) 0.38–0.62 → 0.39–0.63 s (0, with the launch burst); the overview 0.77 → 0.80 s (0) |
| Esc stops a run to the pad (PLANET-82) | **Demonstrated** in-app | `after-travel-2`: G, Esc in the run: no take-off, still here, standing on the pad (0.0 m from its centre); `planets-travel` 16/0 |
| The overview's Enter (PLANET-83) | **Demonstrated** in-app on the fixture | `after-overview-delay`: 33 of 33 landed (the two failing cases 9 of 9), the safety net never needed; 35 PASS / 0 FAIL |
| Real keys through the Guard | **Unknown**: not run (the desktop is shared by six streams; the injected route covers the logic) | — |
| Core (WSL) | 734 passed, 26 skipped, 0 failed (268 s) | `tools/fs.sh test-wsl` |
| No regressions (functional tier, from the worktree with `fs.sh run`) | **Demonstrated** in-app | `planets-navigation` 47/0, `planets-journey` 39/0, `planets-arrival` 25/0, `planets-flows` 28/0, `planets-tutorial` 36/0, `planets-hazard` 82/0, `planets-dialog-keys` 148/0, `planets-overview-routes` 9/0 (`…\2026-11-19-travel\reg-*`) |

**Open / for DJ:** the run's pace and arc (`DepartRun*`), the feet-first turn's timing (`LandingUprightSeconds`), whether
Esc should also undo a breadcrumb/Locations/path move (needs a navigator undo), and whether the overview's departure
should show the traveller on its pad. After the overview's landing one frame at ≈ 0.33 s turns the body 23.5° in camera
space with the body upright on its own up: the camera, not the traveller (not investigated; stream 4's area).

### The selection card, a real Windows open, and Command Prompt after X (PLANET-87..89, AD-76)

Stream 3, branch `worktree-agent-a6cacd782b3b77a70` (from `31c9758`). DJ: "Audit the right-side panel's Open, Parent, Copy
Path, Show in Explorer, and Fly to actions … Some appeared to do nothing or behaved unexpectedly." "After I close the
game with X, the command remains busy until I press Ctrl+C."

**Selection card, reproduced first** (`--automate=planets-selection-actions`, the base product with the new instrument,
13 failures; `%LOCALAPPDATA%\FileScape\evidence\2026-11-19-selection\base-sample\`): *Fly to* did nothing in every
Planets cell (it called the legacy maps' camera) while F turned to face the target, or silently nothing when the target
was not on this world; *Parent (Esc)*: Esc clears the selection on a planet and leaves the overview; *Open (Enter)* in the
overview: Enter lands; the hub ("This computer") has an empty path, so Open and Show in Explorer handed `""` to Windows
and Copy path copied nothing; Parent on the hub was disabled with no reason; after Parent in the overview F framed the
world selected before. Instrument note: Copy path wrote the real clipboard even under `--test-shell=record` (fixed first:
it is recorded now, like the root landmark's copy).

| View → / target ↓ | Open | Show in Explorer | Parent | Fly to / Face it / Zoom to | Copy path |
|---|---|---|---|---|---|
| File on this world | Open (Enter): opens with its app | reveals | Parent: selects its world | Face it (F): turns to it | copies |
| Child world (in the sky) | Open (Enter): Explorer | reveals | selects this world | Face it (F) | copies |
| The world you stand on | Open (Enter): Explorer | reveals | selects its parent | off: "you are standing on …" | copies |
| Location root (`C:\`, `\\wsl.localhost\Ubuntu-24.04`) | Explorer | reveals | selects This computer | off: "not on this world … Go inside (G) or the list (J)" | copies |
| Hub (This computer) | Open (Enter): This PC (`shell:MyComputerFolder`) | off: "FileScape's hub, not a folder" | off: "the top" | off | off: "no path to copy" |
| A world in the overview | Open (Enter lands; the button opens with Windows) | reveals | selects the containing world and zooms to it | Zoom to (F) | copies |

Disabled actions say why in their tooltip ("Unavailable: …"); their key, pressed anyway, shows the same reason as a toast.
The legacy maps keep "Parent (Esc)" and "Fly to (F)".

| Area | Status | Evidence |
|---|---|---|
| Card actions, every view × target, buttons and shortcuts (PLANET-87) | **Demonstrated** in-app; real mouse and keys: unknown | `planets-selection-actions` 44/0 on the Starter Sky (`fix1-sample`, root `C:\`) and 44/0 on the WSL fixture (`fix1-wsl`, root `\\wsl.localhost\Ubuntu-24.04`); base 13 failures (`base-sample`); Core `SelectionActionsTests` 4/4 |
| Real Windows open, app not running, then running (PLANET-88) | **Demonstrated** for Edge with `.xml`; other handlers unknown | `tools/os-open-probe.ps1`: base and fix 5/5 each. The first fixture opened in a new Edge window titled with its name; the second landed in that same Edge process, which came forward; both stayed open after FileScape closed. On the fix build Edge's parent is Explorer, on the base FileScape |
| Command Prompt after X, DJ's command typed at an interactive conhost cmd (PLANET-89) | **Demonstrated** for conhost; DJ's own terminal (likely Windows Terminal): unknown | `tools/os-shutdown-probe.ps1`, table below |
| Core | WSL 738 passed, 26 skipped, 0 failed; Windows 737 passed, 29 skipped, 0 failed (a first Windows run had 2 failures whose names were not captured, under the other streams' load: **unknown**) | `tools/fs.sh test-wsl`, `tools/fs.sh test` |

**Command Prompt, reproduced first.** All runs used `--root=sample` or a fixture, plus a probe preferences file. X was sent as `WM_SYSCOMMAND SC_CLOSE` to FileScape's own window. Evidence: `%TEMP%\FileScape-shutdown\out\` (`base`, `base-zip`, `base-open-edge`, `fix`, `fix-open-edge`).

| Route (typed at the prompt) | Base `cfee25d2b709` (staged copy and ZIP extraction) | Fix (`staging-20261120-015228-1713692`) |
|---|---|---|
| `"…\FileScape.exe" -- …` | cmd free (it ran a typed command while FileScape ran and 1.7 s after X), but ≈ 12 log lines sat under the prompt; the last line was `[exit] … press Enter for a fresh prompt.`, with no prompt: **looks busy** | nothing under the prompt; the prompt is the last line during the run and after X; a typed command ran 1.7 s after X |
| `"…\FileScape.console.exe" -- …` | busy while running (it waits, as designed); free 1.7 s after X | the same, 1.7 s |
| the same, FileScape opening an `.xml` in Edge (`--test-hold=open:`) | **busy after X** until Edge was closed (Edge was FileScape's child, inside the wrapper's job); cmd returned 4.9 s after the probe closed Edge; Ctrl+C would have killed Edge | free 1.8 s after X; Edge (child of Explorer) stayed open |
| `cmd /c "…FileScape.exe …"` | returned 1.5 s after X | returned after X |
| a `.bat` that runs it | waits while FileScape runs, free after X | the same |

**Root causes:**
- **Typed `FileScape.exe`:** Godot's Windows build attaches to the parent console in native start-up and prints under a prompt cmd has already shown. This is DJ's handoff command.
- **`FileScape.console.exe`:** the wrapper waits for every process in its job, so an app FileScape opened kept it waiting.

**Fixes (AD-76):**
- FileScape.exe lets go of a console that did not wait, and the engine header is off.
- Under a job, Open, Start and Show in Explorer go through the desktop's Explorer.
- Show in Explorer uses SHOpenFolderAndSelectItems (before: `Process.Start explorer.exe`, which passed on FileScape's inheritable handles).
- The log file keeps everything, including the `[console]` and `[exit]` lines.

**Instrument defects:**
- Copy path wrote the real clipboard under the test shell (fixed in the product, recorded now).
- The audit at first compared `select:X` with `select:(none)` as the same effect.
- The shutdown probe listed "nothing" when exactly one process ran (PowerShell unrolled a one-item list).
- The open probe stopped when an Edge helper process ended mid-listing, while still reporting 0 failures.

All four are fixed.

**Spills on DJ's desktop (recorded):**
1. A base-build run opened a `.csv`. `.csv` has no per-user default here (AssocQueryString still names Excel), so Windows' "How do you want to open" (OpenWith.exe, PID 14844) stayed in front from 17:56:51 until the integrator reported it. It blocked another stream's Guard. Closed with WM_CLOSE to its window. Both probes now refuse a type without a UserChoice.
2. After the fix-build Edge run, Edge relaunched itself in the background (`--no-startup-window`, no window) from 18:35 to about 18:42. I ended that process tree; the probes now end such relaunches.
3. Edge opened DJ's Personal profile and restored its earlier session ("and 9 more pages"), so those pages were on screen during the runs. Edge's saved session now also holds the fixture tabs.

No input went to another window: these probes send no desktop input.

**Open / DJ:**
- DJ's own terminal: if it is Windows Terminal, the same Godot attach applies, but it was not tested; a new tab could have landed in DJ's open Windows Terminal.
- Under the wrapper, a type with no app gets Windows' own "Open with" dialog: Explorer reports no error back to FileScape.
- Real keys and mouse on the card: not run.
- Not exported or installed.

### A large universe: discovery in play, detail apart from discovery, varied worlds, navigation, the "X pauses" closeout (PLANET-90..93, AD-77)

DJ: "The progressive Overview discovery is especially compelling to watch. Keep building on that feeling while making
navigation more dependable and the larger universe easier to read." Branch `worktree-agent-af0d56d54acba5ad2` (stream 4).
All in-app runs are from the worktree project (`fs.sh run`, 1600 × 900, UI 1.0), not an exported build. Evidence:
`%LOCALAPPDATA%\FileScape\evidence\2026-11-19-stream4\` (one folder and one `.out` per run). Real roots (`C:\`,
`\\wsl.localhost\Ubuntu-24.04\`) ran with `--counts-only`: counts and timings only, no names, no screenshots.

**The "X pauses" closeout (product defect, instrument correct).** Reproduced on the unmodified builds by repeating the
discovery journey on the staged exes: `78a41f3` (the export that failed twice in `check full`) failed 1 of 6 runs;
`cfee25d` passed 6 of 6, and failed 1 of 4 with a CPU load in WSL — with the detailed message: `paused True,
"◐ Discovering… 919 worlds, 303 listed · X pauses"`: the flag was set, the indicator not yet. Cause: `OverviewDiscovery.State` became Paused only
at the scheduler's next step (every 0.1 s, i.e. every 6–7 frames), so for up to 0.1 s after X the indicator still said
"Discovering… · X pauses"; the journey reads it a constant ≈ 80 ms after pressing X (every log), so it failed whenever
the next step fell later. Shown deterministically in Core on the base source (`Pausing_says_so_at_once_not_at_the_next_step`
fails there: "found Discovering"). Fix: setting `Paused` sets the state at once. After the fix: 8 of 8 passed (6 plain, 2 under the load), plus every other discovery run on this branch.

**What changed (AD-77):** discovery also runs while you walk, at a quieter pace, with your own world first. The overview
draws only what fits on screen: a system unfolds when its children fit apart, or when it is on the way to the selected
world, the approached world or where you stand. Folded systems show a haze and "+N worlds inside". Worlds take their
colour and pattern from what the folder holds and from its names. The wheel zooms toward the pointer, [ and ] step
round a ring, Z goes up a level, and a two-line hint teaches these when the overview opens.

| Area | Status | Evidence |
|---|---|---|
| X pauses at once (the closeout) | **Demonstrated**: Core test (fails on the base source, passes now); discovery journey 8 of 8 after the fix (6 plain, 2 under a WSL CPU load), plus `disc-deep-4`; before: 2 of 16 failed on the base exes | `x-*`, `xfix-*`, `disc-deep-4` |
| Background discovery in play: current world, then its parent, then the rest; 2/s, 2 in flight; only while walking (PLANET-90) | **Demonstrated** in Core and in-app | Core `During_play_the_current_world_goes_first_then_its_parent_at_the_quieter_pace`; `play-deep-on2`: in 30 s of walking 20 → 80 folders listed (67 asked), deepest level 3 → 4; the overview then opened with 253 worlds (levels 0–5) instead of 51 (0–3) with `--test-play-discovery=off` |
| Play stays responsive (PLANET-90) | **Demonstrated** (deep fixture, no vsync, 30 s walking) | on: p50 3.7 / p95 4.2 / p99 5.6 / max 39.0 ms, 7 frames over 25 ms (the 7 recompositions, ≤ 34.8 ms each); off: 3.7 / 4.2 / 5.6 / 42.2 ms, 1 over 25 ms. **Product defect found and fixed:** without the recompose gap the walking world was rebuilt every 0.35 s as its subtree was discovered: p99 27.8 ms vs 9.1 ms off (`play-deep-on`, `play-deep-off`). Candidate rebuild ≤ 4.4 ms every ≥ 2 s |
| Detail apart from discovery: overlap on screen, before → after (PLANET-91) | **Demonstrated** | Deep fixture (`--test-broad`) at 10 s: 338 worlds on screen, 420 overlapping disc pairs, 82 % of them overlapping another → 55, 2, 7 %; at 30 s: 507, 1,328, 82 % → 95, 3, 6 % (`disc-deep-off` vs `disc-deep-4`). `C:\` (counts only, 16 s): 695, 4,856, 99 % → 12 drawn (742 worlds folded into them), 1 pair, 17 % (`nav-c-detailoff` vs `nav-c-on3`; the intermediate rules gave 125 drawn / 126 pairs / 80 % and 48 / 32 / 50 %). Ubuntu root (counts only, 16 s): 471, 1,513, 97 % → 26 drawn (1,199 folded), 15 pairs, 65 % — the remaining pairs are on the root's own ring, which stays open as the way in and is seen at a slant (`nav-ubu-off` vs `nav-ubu-on3`); after zooming toward a world, 9 on screen, 0 pairs |
| Discovery unchanged by the detail | **Demonstrated** | Deep fixture with play discovery off (as AD-71 measured): 40 → 320 → 905 worlds below the arrival at 0/10/30 s, as before (40, 317, 905); every laid-out world stays a candidate |
| A folded system shows a haze and "+N worlds inside"; the selected world's way is always drawn | **Demonstrated** in-app; how it reads: DJ | `planets-overview-navigate`: 478 worlds folded into 48 drawn (deep, 16 s); screenshots `nav-deep-4\02-nav-full.png`, `disc-deep-4\03-discovery-30s.png` (before: `disc-deep-off\03-discovery-30s.png`) |
| Varied, stable worlds (PLANET-92) | **Demonstrated** (stability and variety by measure); how it looks: DJ | Core `OverviewWorldLookTests` (4); two runs on the deep fixture: 172 shared listed worlds, all identical (`nav-deep-3`/`-4`), 170 of 170 (`nav-deep-4`/`-5`); `C:\` 200 of 200 (`nav-c-on2`/`-on3`); `looks.txt` holds path and look hashes only |
| Draw cost at 1,500 worlds (PLANET-92) | **Demonstrated**: no measurable cost | `C:\`, every world drawn (`--test-overview-detail=off`), 1,500 worlds, no vsync: varied looks p50 5.8 / p95 7.1 / p99 7.6 ms; the old material 7.1 / 11.1 / 14.8 ms (the machine was shared, so the spread is noise); 145 draw calls in both: one draw for every solid world |
| Navigation: selection anchors the orbit; [ ] round a ring; Z up; wheel toward the pointer; F; the hint (PLANET-93) | **Demonstrated** in-app (injected input); the feel: DJ | `planets-overview-navigate` 13/0 on the deep fixture, `C:\` and the Ubuntu root: ] three times gives three new worlds on the same ring with the orbit centred on each; [ steps back; Z selects the parent; five notches over a world brought the orbit's centre from 595–1,047 m to 3–176 m from it and the distance from 1,150–1,643 to 510–729 m; frame times while navigating p95 16.7 ms (vsync) and 5.0 ms (`C:\`, no vsync) |
| Fill-in, before vs after | **Demonstrated** | Overview only: unchanged (above). With play discovery: after 30 s of walking the overview opens with 253 worlds rather than 51. `C:\` (overview, counts only): 283 → 526 → 1,074 worlds at 0/10/30 s (before 220 → 518 → 1,070) |
| Core | On the final branch: WSL 748 passed / 26 skipped / 0 failed (2 m 28 s); Windows 747 / 29 / 0 (4 m 17 s). Earlier, mid-work, one Windows run failed `Rendering_each_arrangement_with_cues_is_cheap(DeepWoods)`, a timing test that passed alone (4/4): load from the shared desktop. Behaviour baseline identical before/after the layout refactor | `tools/fs.sh test-wsl`, `tools/fs.sh test` |
| Real input (`os-planets-probe.ps1`) | **Unknown**: not run (the coordinator runs real input); `-Discovery` should still pass (its steps do not depend on the 0 s picture) but was not re-run | — |

**Instrument defects:** the first discovery runs on this branch failed "more levels without a visit" (0..4 → 0..5): play
discovery had already listed a level before T, so the journey's 0 s baseline moved. `verify.sh` now runs that journey with
`--test-play-discovery=off`, so it measures the overview alone as before. The navigation probe's orbit-centre check
allowed 1 m and failed at 1.0 m on `C:\` while a relayout glided the world; it now allows 1 % of the view distance. The
first fingerprint of looks compared whole sets, which differ between runs (discovery's timing); `looks.txt` compares the
worlds both runs share. One run (`nav-deep-4`, first attempt) found no App assembly: a WSL `dotnet build` of the app had
left `.godot/mono/temp/bin` in a state the next Windows build did not fill; a clean Windows build fixed it, and the app is
built only with `fs.sh build` now. **Product defects found by the instruments and fixed:** the recompose storm while
discovering in play (above); a `TimeSpan.MinValue` overflow in the first version of the recompose gap (8,384 errors in one
run, caught by the run's error count); the detail rule's first two versions left 80 % and then 50 % of the drawn worlds
on `C:\` overlapping (the focus lowered the spacing bar too, where you stand counted as a focus, and an average spacing
ignored that the layout packs small worlds side by side), now the closest two children must be 16 px apart and worlds
on a ring that must stay open are drawn down to 2.5 px.

**Provisional / for DJ:** how the folding feels (when systems open and fold as you zoom, `ExpandRingPixels`,
`ChildSpacingPixels`), the haze's strength, the colour families on his own folders, the wheel's pull toward the pointer
(30 % a notch), whether [ ] and Z are the right keys, the hint's wording and timing, the play pace (2/s) and the planet
growing in 4-second steps while discovery lists your own world. Not run: real input; an exported build; `check full`
(the integrator runs it). On `C:\` the 1,500-world layout budget still limits how far discovery reaches.

### Testing time made visible, avoidable waits removed (TEST-05, AD-80)

DJ: measure the integration suite's elapsed time and report total and per-step durations on every run, including
failures and timeouts; find the slowest steps and take straightforward savings without weakening coverage.

**What was already there:** `verify.sh` printed each step's seconds as it went and a TOTAL line. There was no
end-of-run table, nothing after an early exit or Ctrl+C, and skips were not counted.

**Before and after.** Both runs were `check full` with `FS_LAUNCH_LOCK` on a desktop shared with six other streams.
Step seconds exclude lock waits.

| Segment | Before (`c8f5e96`, reporting only) | After (`5461f7b`) |
|---|---|---|
| build | 23.7 s | 21.0 s |
| tests-windows | 267.6 s (1 process) | 169.2 s (3 shards; Windows now back to 1 process: see below) |
| tests-wsl | 222.5 s, after Windows | 122.8 s (3 shards), **beside** build, Windows tests and stage |
| stage | 493.3 s (with the smoke launch's lock wait) | 33.8 s (lock wait 0.0 s, reported apart) |
| Up to the first journey (wall) | ≈ 1,007 s | ≈ 224 s |
| 43 journeys (sum) | 1,695.7 s | 1,661.0 s (unchanged: no probe code changed) |
| Steps total / wall | 2,706 s / 7,573 s (4,866 s lock waits) | 1,888 s / 5,187 s (3,298 s lock waits) |
| Counts | 44 pass, 1 fail, 2 skipped | 43 pass, 2 fail, 2 skipped |

- **Final configuration** (`check fast` after the Windows change, quiet desktop): tests-windows 202.4 s as one
  process, tests-wsl 90.9 s in shards beside it, 205.3 s wall. Counts: 736/0/27 of 763 and 734/0/26 of 760, the same
  as before.
- **The quiet morning reference** (`78a41f3`, serial): 2 + 154 + 148 + 31 s before the first journey.
- **Journey counts:** every journey's PASS/FAIL/SKIP counts are identical before and after, except
  `planets-overview-discovery` "X pauses". It passed before and failed after. This is the intermittent failure from
  the fourth follow-up (cause **unknown**), not caused by this work.
- **Failures in these runs (instrument, from load):**
  - Before: `WorldLayoutTests.Layout_is_deterministic_and_fast_for_thousands_of_folders` on WSL, 242 ms against a
    150 ms bound, beside the other streams.
  - After: `SkySynthTests.Rendering_one_second_of_music_with_six_effects_is_cheap` on Windows, 63.8 ms against a
    60 ms bound, with six test processes at once. Windows therefore runs as one process again.

| Area | Status | Evidence |
|---|---|---|
| Timing table at the end of every run (pass/fail/timeout/skipped/interrupted, seconds, total), from an EXIT trap, also in `timing.txt` | **Demonstrated** | Both full runs; an early exit (no staged exe) printed the table; `check harness`: rows for every judged path, a skip, a lock wait kept out of the seconds, SIGTERM mid-step (stopped in 0.0 s, row `interrupted`) |
| `FS_LAUNCH_LOCK` per launch (journeys, probes, recordings, smoke launch, `fs.sh run`), wait reported apart | **Demonstrated** | Both full runs (waits 0–644 s per journey while the step seconds matched the quiet run to ≈ 1 s) |
| WSL Core tests in three complementary shards (each test once; a shard without a result fails) | **Demonstrated** | 152 s → 80 s alone, 734/26/760 as before |
| WSL tests beside build, Windows tests and stage | **Demonstrated** | After run, `check fast` |
| `stage --reuse` by `source_state` | **Provisional**: the code is in the tiers by default, but no measured re-run hit the cache (the after-run's commit was new; the integrator's next run on the same commit is the first real use). Saves ≈ 31 s per run | — |
| `check journeys NAME...`, `--list`, `--measure` | **Demonstrated** for `--list` and an unmatched name; no journey launched through it yet | — |

**Commands:** `FS_LAUNCH_LOCK=/tmp/claude-1000/filescape-journey.lock tools/fs.sh check full`;
`tools/fs.sh check journeys 'planets-nav*' planets-look`; `tools/fs.sh check journeys --list`;
`FS_TEST_SHARDS=0|1 tools/fs.sh test[-wsl]`.

**Not changed:**
- No journeys were merged.
- `planets-dialog-keys` (170 s) is frame-bound typing; speeding it up would change the frame pacing its checks rely on.
- The fixed waits inside journeys are settle times of 4 s or less before a measurement.
- SkySynthTests was not split into separate classes, which would conflict with the streams editing it.

**Open:**
- SkySynthTests is still the WSL critical path (about 75 s per shard).
- The Windows tests (150–200 s) are now the longest step before the journeys.
- Timing-bound Core tests fail under heavy load: an instrument problem to address in the tests, not by serialising
  the harness.

## Fourth playtest follow-up (2026-11-19)

### The final candidate (`cfee25d2b709`, staged, not installed)

Six streams merged (camera look, keyboard ownership, Hazard moves, landmarks, Overview discovery, musical modes), then
fixed from the real-input runs. Staged at `%LOCALAPPDATA%\FileScape\staging-20261119-220649-1583361\FileScape.exe`;
the installed build is still `6b9b60c274af`. ZIP: `%LOCALAPPDATA%\FileScape\FileScape-cfee25d2b709-win-x64.zip`
(72 MB, 196 files, standard entry names since `e6daf54`), checked from a clean extraction (Expand-Archive into an empty
folder; the smoke launch passes; README.txt is `docs/windows-playtest.txt`; no logs; no machine paths). A fresh clone of
`c200de5` staged and packaged by itself (`docs/windows-playtest.txt` committed in `4d1b6b5`).

| Check | Result |
|---|---|
| `fs.sh check full` on `78a41f3` (all six merged) | 40 of 41 journeys, Core 736/734 passed (Windows/WSL). The one failure, discovery's "X pauses", failed twice on that export and passed twice on `c200de5` and after: cause not established (**unknown**) |
| In-app on `cfee25d` | `planets-dialog-keys` 148/0, `planets-navigation` 47/0 (with its listing-delay instrument) |
| Real input on `cfee25d` (`os-planets-probe.ps1`, Guard on every input) | `-Look` 17/0, `-Dialogs` 79/0, `-Landmarks` 25/0, `-Discovery` 14/0, `-Tutorial` 30/0, `-Play` 15/0, controls 24/0, `-Route` 10/0, `-InputFocus` 14/0, `-Audio` 8/0; `-Hazard -Recovery` 16/1 twice: **the long jump fell** (second press recorded as "buffered" at 0.367 s; it crossed with real input on `6d5229a`) — **provisional**, DJ to try |
| Real input found and fixed | Esc needed twice for a panel opened by its toolbar button (the button's tooltip under the resting mouse spent the first press; now taken in `Main._Input`); after ⚙ then Esc the toolbar button kept the focus and blocked R and J (released now) — AD-68. Probe defects: state read before written, `$p` reused (left the app running), the Hearth world typed from a world that does not list it, R with an earlier selection |
| Interruptions | One run was cut short when DJ used the machine: the Guard stopped each probe at the first input with ChatGPT or Notepad in front; nothing went to another window. Those modes were run again |
| Not run | `-RealOpen` (Windows' own file association: it would open a window in DJ's Notepad) |


### Vertical camera look (PLANET-67..68, AD-67)

DJ (build `bcde9b33b338`): vertical drags seemed locked out. **Reproduced first** with `--automate=planets-look`, which injects drags the way a mouse sends them. Standing on arrival, just after a landing, in the overlook view and on a Hazard World's arrival, a vertical drag tilted the drawn view **0.0°** and the wheel did not zoom. After walking, while walking and in the overview it worked. **Root cause:** the arrival and overlook framings set the pitch and distance to fixed values instead of adding to the chosen ones. The arrival framing holds the whole time you stand still, so it overrode the drag. A 62° cap on a Hazard World also left a dead band. The Places reverse and levelling settings, the swing-in, the keep-in-frame rule and the overview were not involved. **Fix:** framings are now offsets on what you choose (no change at the defaults). Each arrival starts from the designed pitch. The cap fades instead of cutting. A drag up past the boom's floor tilts the view up to about +19° (it was +8°). The state file now has `planets.camPitch`, `viewPitch`, `arrivalFraming`, `overlookView` and `uniPitch`.

| Area | Status | Evidence |
|---|---|---|
| Vertical look on arrival, after and while walking, overlook view, after a landing, Hazard World, overview; wheel during the arrival framing; arrival boom 29° until you look and after every landing (PLANET-67) | **Demonstrated** in-app | `planets-look` 35/0 with no prefs file, the "usual" file (reverse and levelling off) and the "reversed" file (all on); before the fix 24 passed, 11 failed (`repro`, the earlier probe version) |
| Looking up past the boom's floor (PLANET-68) | **Demonstrated** in-app; how it feels: DJ | `planets-look` sweep: +19.4° … −70.0°, every 60 px step tilts |
| No regressions | **Demonstrated** | `planets-arrival` 25/0, `planets-hazard` 72/0, `planets-journey` 39/0, `planets-lookouts` 29/0, `planets-tutorial-cues` 55/0, `planets-tutorial` 36/0; Core (WSL) 700/0 |
| Real input `tools/os-planets-probe.ps1 -Look [-LookPrefs default\|usual\|reversed]` | **Unknown**: written and parse-checked, not run (the coordinator runs real input) | — |

Evidence: `%LOCALAPPDATA%\FileScape\evidence\2026-11-19-look\` (`repro`, `look-default`, `look-usual`, `look-reversed`, `reg-*`). Runs were made from the worktree project with `fs.sh run`, not from an exported build.

### Keyboard ownership across all panels (PLANET-69..70, AD-68)

DJ: typing into a navigation dialog still switched out of Planets into a legacy view (dialog unknown). Reproduced
first with the new in-app instrument (`planets-dialog-keys`, injected key events, each key judged on its own;
`docs/evidence/2026-11-19-input/repro-dialog-keys-summary.txt`): 564 of 916 typed keys reached the game's shortcuts.
With the **Locations / Open location rows** focused (L, its button, Ctrl+O, Open…, or Tab from the path field): every
letter (V is Planets → Places), digit, Backspace, Home/End, ←/→ and chord. **Settings**: every key, Tab and V included.
From the **Destinations results** and the **reader**: Ctrl+V and Ctrl+Tab, both switching to Places live. From the
**path field**: Ctrl+Tab. Plain typing in the Destinations filter and the path field was already safe. The most likely
case for DJ is Locations with its rows focused, or a paste. The fix is one rule at the point where keys reach the game
(`Main.KeyboardOwner`, AD-68). Commits `8439c53`, `946cb4f`, `5c07066` and this documentation commit on
`worktree-agent-afc40f1875c66477c`.

| Area | Status | Evidence |
|---|---|---|
| Every keyboard panel owns the keyboard: Destinations (J, ☰), Locations (L, its button), Open location (Ctrl+L, Ctrl+O, Open…), Settings (F4, ⚙), the reader (R, ⤢ Expand). Tested typing at once and from the results, after travel (Enter in the filter), home again (Enter in the path field) and in the overview. Esc closes only the panel (PLANET-69) | **Demonstrated** in-app | `planets-dialog-keys` 148/0: 3,447 keys, 0 leaks, after 564 leaks before the fix. Evidence in `%LOCALAPPDATA%\FileScape\evidence\2026-11-19-dialogs\r2\` |
| The panels' own keys and the existing journeys | **Demonstrated** in-app | `planets-destinations-keys` 35/0, `planets-navigation` 47/0 (Locations and Destinations open together), `planets-reader` 33/0, `planets-tutorial` 36/0, `planets-journey` 39/0; `fs.sh check smoke` on `5c07066` (staged, not installed): 0 failing steps, Core 703/700 passed (Windows/WSL), `%LOCALAPPDATA%\FileScape\evidence\checks\smoke-20261119-202536\` |
| Real keyboard and mouse, every panel (PLANET-70) | **Unknown**: `tools/os-planets-probe.ps1 -Dialogs` is written and parse-checked (`Parser::ParseFile`, 0 errors) but has not been run. The coordinator runs real-desktop input serially | — |

**Instrument defects fixed:** the probe's R step failed at first. A panel had left a world selected, and R reads the
selected folder's README ("No README to show here"). The step now presses Esc first, as a person would.
`Main.ProbeShortcutsInert` (probe-only) logs and drops the keys that reach the shortcuts, so one run lists every leak
without the first one leaving Planets.
**Product defect found by the fix:** when Locations and Destinations were open together (PLANET-41), the fixed panel
order kept keys from the list you opened last. Now the panel holding the GUI focus owns the keys, and J from the
Locations rows opens the list beside them.
**Changed on purpose:** chords other than Ctrl+L and Ctrl+O no longer get past an open panel (before, Ctrl+Home got
through Destinations). Esc in the overview with a panel open closes the panel, not the overview.
**Provisional:** DJ's own keystrokes are unknown, and `-Dialogs` has not been run with real keys. Overlays that do not
take the keyboard (the preview, the bench, the root landmark's E panel, Help) are unchanged: you can walk with them
open. Not exported or installed.

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

### The overview discovers more of the filesystem (PLANET-76..78, AD-71)

DJ: T showed only a couple of levels; more appeared only after visits. Now, while the overview is open, it lists the
worlds it draws that are known only by name, breadth-first, with the selected (or zoomed-toward) world's subtree first,
through the session's explorer (`WorldExplorer.Discover`, metadata only), and the overview grows by gliding. A small
indicator above the prompt ("◐ Discovering… N worlds, M listed · X pauses") has a Pause/Resume button; X does the same.
Commits on branch `worktree-agent-ade6c21bd8f842cf5`: `74aa99f` (Core scheduler, explorer request, layout, tests), `a9ce0ee`
(overview growth, indicator), the evidence commit (fixtures, `--test-broad`, journey, verify step, real-input mode), and
this documentation commit.

**Limits chosen (`OverviewDiscovery.Limits`, `PlanetWorld.Discovery`):** 3 listings in flight; 10 new listings per second
(burst 3); stop at 200,000 tree entries (a fifth of the 1,000,000 session budget); a request unanswered for 20 s gives up
its slot; only drawn worlds (≤ 1,500, ≤ 64 per system) and only inside the arrival folder or below a visited place; steps
every 0.1 s, relayouts at most every 1.5 s, each glide 1.2 s.

**Fill-in (journey `planets-overview-discovery`, exported-from-project build, 1600 × 900, UI 1.0)** — worlds drawn below the
arrival folder, levels drawn, deepest level listed (0 = the arrival folder); discovery's own requests:

| Root | 0 s | 10 s | 30 s |
|---|---|---|---|
| Deep fixture, Windows (`create-deep`, `--test-broad`) | 40 worlds, levels 0–3, listed to 2 | 317, levels 0–5, listed to 4 (102 asked) | 905, levels 0–7, listed to 6 (302 asked) |
| Deep fixture, WSL through `\\wsl.localhost` (`--deep`, `--test-broad`) | 41, 0–3, to 2 | 318, 0–5, to 4 (102) | 906, 0–7, to 6 (303) |
| `C:\` (counts only: no names logged, no screenshots, no landing) | 220, 0–3, to 2 (tree 710 entries) | 518, 0–4, to 3 (102 asked, 5 failed; tree 3,449) | 1,070, 0–5, to 4 (302 asked, 16 failed; tree 13,223) |

At 0 s the visit's own listings (the arrival folder, its destinations and their children) are what is known; everything
after is discovery. On `C:\` the folders are wide, so the budget fills by breadth before depth (1,089 of 1,500 worlds at
34 s); the failures are access-denied system folders, reported as such and not retried.

| Area | Status | Evidence |
|---|---|---|
| Breadth-first discovery, focus first, bounds, links/denied/refused/location roots (PLANET-76) | **Demonstrated** | Core `OverviewDiscoveryTests` 11/11 (WSL; the POSIX test skips on Windows): order, focus, in-flight ≤ 3, rate ≤ 10/s + burst, tree-size stop, pause, timeout, refusal, failure not retried; real explorer: symlink loop not followed, mode-000 folder AccessDenied, nothing outside listed, 7 levels. In-app on both deep fixtures: 61 of the 61 folders listed in the 6 s after selecting West were inside it (`C:\`: 59 of 60); the link back to the root shown and never followed; the folder a link points to never listed; the denied folder Failed/AccessDenied, asked once; the hidden folder never drawn |
| Growth without jumps; camera and selection responsive (PLANET-77) | **Demonstrated** in-app; the feel: DJ | Largest per-frame move of an on-screen world 2.8 % (Windows), 2.6 % (WSL), 0.8 % (`C:\`) of the screen height across 6 glided relayouts; the selected world 0.00 % a frame; yaw, pitch and focus unchanged; click-select, wheel zoom (306 → 188 m), Enter lands and Backspace returns during discovery; frame times while the overview ran p95 16.7 ms on all three (max 42–56 ms); longest relayout 5.6 ms (deep), 3.7 ms (WSL), 9.6 ms (`C:\`); `Relaying_out_with_what_is_drawn_pinned_only_adds_worlds`; behaviour baseline identical before/after the layout change |
| What is learned is kept (PLANET-78) | **Demonstrated** | Landing on a world discovery had listed (level 6) lists nothing again (4 entries before and after); T after landing and returning: 977 worlds (was 916 at 30 s), `C:\` 1,089 (was 1,073) |
| Pause / resume | **Demonstrated** in-app | X pauses (nothing asked in 2 s), the indicator's button resumes |
| Real input (`tools/os-planets-probe.ps1 -Discovery -DiscoveryRoot <Deep Atlas>`) | **Unknown**: written and parse-checked, **not run** (the coordinator runs real input serially) | — |
| Core suites | Windows 710 passed / 28 skipped, WSL 711 / 25, 0 failed | `tools/fs.sh test`, `test-wsl` |

Evidence: `%LOCALAPPDATA%\FileScape\evidence\2026-11-19-discovery\` (`deep-3`, `wsl-1`, `c-1`; `deep-1`/`deep-2` are the
runs with the instrument defect below).

**Instrument defects fixed:** the jump measure first counted worlds far off screen (one projected to (−1892, 2414) in a
1600 × 900 view, "48 %" per frame, where perspective magnifies tiny motion); it now judges only worlds on screen in both
frames. The hidden-folder check first asked that the folder stay unlisted, but the visit's ordinary context listing (two
levels down) lists it; the check is now that it is never drawn, so discovery never asks for it.

**Provisional / open:** the deep fixtures are measured with `--test-broad` (as on a drive root): on a non-broad arrival the
background traversal lists the subtree anyway and discovery adds the order and the live overview. The growth pace, glide
length and the keep-framing zoom-out are by eye. The overview's 1,500-world budget caps how far discovery goes on wide
roots. Real-input `-Discovery` not run. Not exported or installed.

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
- ~~KD-09 Open via a document association is unverified~~ **Closed 2026-11-20:** documents open through their real associations (.txt, .html, .xml, .png; PLANET-140, sixth pass).
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
- ~~KD-27 Not promoted (`4a7b9ce6fdf6`)~~ **Superseded:** every later candidate is staged, and installing is DJ's step (close FileScape, `tools/fs.sh promote`); the current candidate is named at the top of this file.
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
- **KD-33 Planets, not established** (updated 2026-11-20: DJ and others have played and listened since, see the playtest sections; the "feel" part is closed): ~~how it feels to a person~~; real large trees in the app (Core composes 800 files in 2.6 ms); NTFS cloud placeholders in the app (they are never read; not exercised); the music on other audio devices; UI scale 1.5 and above.

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
