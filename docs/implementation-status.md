# Implementation status

Last updated: 2026-11-05 (evening), after the usability pass (commits `dd0c639`…HEAD); before that the correction and units pass (commits `dbf2bc1`…HEAD) and the walking-world pass (commits `75deacf`…HEAD); the Places pass is 2026-11-04 (`2eabd73`…`bdc0a73`); earlier sections describe 2026-11-03 (commits `ee39c8f`…`5379de4`). Hardware: Lenovo 21RX, Ryzen AI 9 HX PRO 370, **Radeon 890M iGPU** (driver 32.0.22024.19001, 8 GiB UMA carve-out), 87.6 GiB RAM, Windows 11 Pro 26200, on AC power. Evidence files: `docs/evidence/2026-11-03/`. The full raw runs are in `%LOCALAPPDATA%\FileScape\evidence\` on DJ's machine and are not in the repo.

Vocabulary: **Demonstrated** = observed with recorded evidence; **Provisional** = works in limited conditions; **Failed** = observed not working; **Unknown** = not established.

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
- **KD-15 Content previews are not implemented** (future direction recorded in requirements and architecture: a per-file appearance provider fed by a separate, deliberate preview reader).

## Provisional decisions to confirm with DJ

Godot (AD-01), the Fishbone default (AD-08), the log height scale (AD-07), click = select-only (FSN approached on click), and D3D12 as the default driver (KD-01, AD-11).

## Next useful iteration (recommended, 2026-11-05)

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
