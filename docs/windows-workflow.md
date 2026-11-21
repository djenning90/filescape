# Windows build, run and test workflow

The source of truth is this WSL repository (`/home/david/src/filescape`). Windows tools operate on it directly through `\\wsl.localhost\Ubuntu-24.04\home\david\src\filescape`. There is no staging copy, so there are no sync rules.

## Installed tools (user-local; nothing system-wide)

| Tool | Version | Location | Source / verification |
|---|---|---|---|
| Godot .NET editor (portable, self-contained mode via `_sc_`) | 4.7.2-stable mono (`4.7.2.stable.mono.official.ed1daf0bf`) | `C:\Users\David\tools\godot\4.7.2-stable-mono\Godot_v4.7.2-stable_mono_win64\` | https://github.com/godotengine/godot/releases/tag/4.7.2-stable, `Godot_v4.7.2-stable_mono_win64.zip`, SHA-512 `79229fd1…367757` (matched `SHA512-SUMS.txt`) |
| Godot .NET export templates (Windows x86_64 only extracted) | 4.7.2.stable.mono | `…\Godot_v4.7.2-stable_mono_win64\editor_data\export_templates\4.7.2.stable.mono\` | `Godot_v4.7.2-stable_mono_export_templates.tpz`, SHA-512 `bb5c41d7…ff6bd5a` (matched); the full .tpz is kept alongside |
| .NET SDK (pre-existing) | Windows 10.0.403 (runtimes 8.0.33, 10.0.14); WSL 10.0.400 | system | not installed by this work |

Project packages (restored from nuget.org): `Godot.NET.Sdk` 4.7.2 (MIT), xunit 2.9.3 (Apache-2.0), xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.0.1 (MIT), FluentAssertions 7.2.0 (Apache-2.0; see AD-10). Godot exports also bundle GodotSharp (MIT).

To remove everything: delete `C:\Users\David\tools\godot` and `%LOCALAPPDATA%\FileScape`. Godot writes run logs to `%APPDATA%\Godot\app_userdata\FileScape\logs`.

## Commands (from WSL)

`tools/fs.sh` runs Windows programs directly through WSL interop. Arguments keep their boundaries (spaces and apostrophes are fine) and the real exit code is returned. `dotnet.exe` is found via `%ProgramFiles%\dotnet`, then `where dotnet.exe`; override with `FILESCAPE_DOTNET`. Godot is found under `%USERPROFILE%\tools\godot\…`; override with `FILESCAPE_GODOT_DIR`. `tools/fs.sh which` shows what was found.

```
tools/fs.sh build        # Windows dotnet build of app + Core (fails loudly on errors)
tools/fs.sh test         # Core tests with Windows dotnet.exe (includes Windows-only tests): one Release process, every class side by side, no Perf/Report
tools/fs.sh test-wsl     # the same with the Linux SDK; the Windows-only tests skip with a reason
tools/fs.sh test-wsl --fast           # the edit loop: also without the Slow tests
tools/fs.sh test-wsl-area music       # one area's classes (areas: tests/FileScape.Core.Tests/areas.txt); test-area on Windows
tools/fs.sh test-perf    # only the Perf (wall-clock) and Report tests, one at a time, Debug; run it with nothing else of ours running
tools/fs.sh test --filter 'FullyQualifiedName~PlaceTests'   # other arguments: one plain `dotnet test` with them (Release)
tools/fs.sh run -- --root='C:\some\folder'      # run from the project via the editor binary
tools/fs.sh editor       # open the Godot editor on app/
tools/fs.sh export       # standalone build → %LOCALAPPDATA%\FileScape\build\FileScape.exe (+ data_FileScape.App_windows_x86_64\)
```

Input verification tools: `--automate=navigation --out=DIR` injects input events through Godot's input pipeline and writes `navigation.log`. `tools/os-input-probe.ps1` sends real Win32 SendInput keyboard and mouse events (and Alt-Tab) to a running FileScape started with `--state-file=PATH`; it moves the real mouse and takes focus, so run it only when that is acceptable.

App arguments go after `--`:
`--root=PATH`, `--synthetic=N`, `--synthetic-rate=N/s`, `--vis=planets|places|classic|district` (normal launches default to Planets; automated runs retain their existing defaults), `--quality=high|low`, `--window=WxH`, `--ui-scale=F`, `--automate=journey|perf|scanperf|live|qualitystress|navigation|flicker|places|places-scan|places-tour|places-debug|args --out=DIR [--open-test=NAME]`, `--state-file=PATH`.

Places evidence (2026-11-04):
- `FileScape.Fixtures.dll create-places` creates the deterministic "Atlas Studio" tree and an empty root under `%TEMP%\FileScape-fixtures\places-*` (marker-guarded; `delete-all` removes it, including its deny ACE and junction).
- `--automate=places --root=<Atlas Studio>`: the Places journey through the injected input path, screenshots and walking frame times. `--automate=places-scan --synthetic=50000 --synthetic-rate=10000`: responsiveness during a scan and cancellation states. `--automate=places-tour` with Godot's `--write-movie tour.avi --fixed-fps 30` (engine arguments, before `--`) records the guided tour.
- `--automate=flicker --vis=district [--open-test=selection|beamtop|street]`: the VIS-11 instrument (per-frame bright/flash/cell-spike statistics with one effect toggled at a time).
- `tools/os-places-probe.ps1 -Exe ... -Root ... -Out ... [-OpenTest]`: real SendInput journey judged from `--state-file`; it moves the real mouse, takes focus and (with `-OpenTest`) opens and then closes one Explorer window on the fixture's Documents folder.
- *Walking world (2026-11-05):* `FileScape.Fixtures.dll create-walk` creates "Wayfarer" (project, junctions to outside/loop/two routes/broken, denied, hidden/system, Unicode) and "Shared Library" under `%TEMP%\FileScape-fixtures\walk-*`; cleanup removes every reparse point first, without following any. `tools/make-wsl-fixture.sh` (WSL) creates a small Linux tree under `/tmp/filescape-wsl-fixtures` and prints its `\\wsl.localhost\…` path; set `FILESCAPE_WSL_FIXTURE` to it (and add it to `WSLENV`) for the WSL test and the journey's WSL steps.
- `--automate=walk --root=<Wayfarer>`: the walking-world journey (95 checks with the WSL fixture, 2026-11-05 evening: look direction and Settings, speeds, gate plates/chip, U then W up a chain); `--automate=walk-perf`: stationary frame cost with one effect toggled at a time; `--automate=walk-tour` with `--write-movie walk-tour.avi --fixed-fps 30`: the guided recording; `--automate=walk-look`: quick screenshots.
- `tools/os-walk-probe.ps1 -Exe ... -Root <Wayfarer> -Out ... [-UiScale 1.25] [-ChainRoot <Wayfarer>\Journeys\Coast\Harbour\Pier]`: real SendInput for walking, turning, jumping, look direction before and after a real click on the Settings checkbox (its own `--prefs` file in `-Out`, checked again after a relaunch), flight, Alt-Tab, H, L/Esc, a junction followed and returned, and with `-ChainRoot` U then W held up to Wayfarer. It only targets the window of the process it starts (DJ may have FileScape open). `tools/os-places-probe.ps1` still uses the first FileScape window: close other FileScape windows before running it.
- **Exporting and delivering a build (AD-21):** `tools/fs.sh export` → a new `%LOCALAPPDATA%\FileScape\staging-<UTC stamp>-<pid>` (never reused, never deleted by the export) → file and count checks → `build-info.txt` (commit, uncommitted paths, SHA-256) → a smoke launch (`--automate=smoke`) → a check of running FileScape processes that fails closed → promotion by renaming (`build` → `previous-<stamp>`, staging → `build`) → verification again. Exit codes: 3 incomplete export, 4 a FileScape runs from `build` (the verified staged build is kept and named), 5 smoke launch failed, 6 running processes could not be determined, 7 the promoted build failed verification. `tools/fs.sh builds` lists `build`, `staging-*` and `previous-*` with completeness and identity, plus what runs; `tools/fs.sh prune-builds` deletes only directories nothing runs from and keeps the newest previous build. Export from a clean tree, so the identifier is a plain commit. Test hooks: `FILESCAPE_TEST_RUNNING=fail|<path>` and `FILESCAPE_TEST_LOCALAPPDATA=<dir>` (the failure paths were exercised against a throwaway base).

Engine arguments go before `--`, for example `--rendering-driver d3d12`.

Fixture tool (Windows): `dotnet src\FileScape.Fixtures\bin.win\Debug\net10.0\FileScape.Fixtures.dll create|delete <dir>|delete-all`. It only creates under `%TEMP%\FileScape-fixtures` by default, and refuses to delete anything without its marker file.

## Verification tiers (2026-11-06, AD-29)

| Command | Takes | Runs | When |
|---|---|---|---|
| `tools/fs.sh check harness` | ≈ 3 s | the harness's own judging, every pass/fail path with test doubles (nothing launched) | after changing `verify.sh` or `verify-lib.sh` |
| `tools/fs.sh check fast` | ≈ 1.5 min (TEST-10..13, 2026-11-20: the Core runs ≈ 50 s WSL and ≈ 60 s Windows with their builds) | app build, Core tests on Windows and WSL (without Perf/Report) | every change |
| `tools/fs.sh check smoke` | ≈ 10 min | fast + `fs.sh stage --reuse` + walk-explore, the hub → project journey and the planets journey on the Starter Sky (UI 1.0, functional) | before a commit that touches the app |
| `tools/fs.sh check full` | ≈ 32 min of steps (2026-11-19: 1,888 s, plus any launch-lock waits) | smoke + `tests-perf` (the Perf/Report Core tests one at a time, before the first journey, AD-90) + walk, places (connected, tree), scan, navigation, map journey, aerial limit (budgets 24 and 8) at 1.0 (planets-sound at 1.25; the 1.25 repeats are in release, AD-88); the planets fixture tour (Wayfarer: links, empty room; the WSL fixture) | before handing a build over |
| `tools/fs.sh check release` | ≈ 50 min (not re-measured) | full with measurements, the UI 1.25 repeats (explore, places, planets, walk), walk-perf, real OS input at 1.0 and 1.25 (moves the mouse, takes focus), recordings (planets journey, explore) | evidence for a report |
| `tools/fs.sh check journeys NAME...` | the named steps | only the named journeys (shell patterns: `'planets-nav*'`), on a staged build (reused) and without Core tests; real-input steps (`os-*`) only by exact name; `--measure` keeps measurement segments; `--list` prints every step name | while working on a journey |

- **Output:** each run writes `%LOCALAPPDATA%\FileScape\evidence\checks\<tier>-<stamp>\summary.txt`, with every step's seconds, its exit code, and its PASS/FAIL/SKIP counts.
- **Timing table (TEST-05, AD-80):** every run ends with one table: each step's name, result (`pass`, `fail`, `timeout`, `skipped`, `interrupted`) and seconds, then the total and the counts. It is printed last, appended to `summary.txt` and written to `timing.txt` in the same folder. It also appears after a failure, a timeout, an early exit (no fixtures, no staged build) and Ctrl+C or SIGTERM. A signal stops the running step's command (only the process the harness started) and marks that step `interrupted`. The Core tests' rows carry their counts (`734 passed, 0 failed, 26 skipped of 760`).
- **Shared desktop: `FS_LAUNCH_LOCK=/path`.** Each app launch (every journey, real-input probe and recording, the stage's smoke launch, `fs.sh run`) holds this flock for that launch only. The lock's descriptor is closed in the launched command. A step's seconds are its own run. Its wait for the lock is in its note (`lock wait 48.2 s`), and the TOTAL line sums the waits. Example: `FS_LAUNCH_LOCK=/tmp/claude-1000/filescape-journey.lock tools/fs.sh check full`.
- **Accounting (TEST-06, AD-88):** the table's last three lines are `active` (the steps' own seconds), `lock-wait` (waiting for the shared desktop) and `reruns` (steps this session had already run, with their seconds and names). Each step that ran is also appended to the session timing log (`~/.local/state/filescape/timing.tsv`, or `FS_TIMING_LOG`; `off` disables it). A session is the git branch (`FS_TIMING_SESSION` overrides), so each worktree's agent has its own totals. A step whose name the session has already logged is a rerun; say why with `FS_RERUN_WHY="…"`. Ad-hoc launches go into the same log: `tools/fs.sh timed NAME [--lock] [--why=…] -- CMD…` runs CMD (holding `FS_LAUNCH_LOCK` with `--lock`), keeps CMD's exit code and takes nested lock waits (a stage's smoke launch) out of the active seconds. Do not wrap `fs.sh check` in it, because check logs its own steps. `tools/fs.sh timing [--rows] [--all|--session=S]` prints the three totals for the handoff.
- **What runs side by side:** each host's Core tests are one `dotnet test` process (AD-90; the three WSL shards of AD-80 are gone: they existed to split SkySynthTests, which is now several classes). A run without a result line fails. The WSL tests run in the background beside the build, the Windows tests and the stage, because they use their own `obj/`, `bin/` and SDK; none of them asserts on wall-clock time any more (those are `Kind=Perf`, run by `tests-perf` after them in full and release). The journeys wait for them, because their timing checks must not share the machine.
- **Upper bounds:** build 600 s, each host's tests 1,200 s, stage 1,800 s. A hang ends as a `timeout` row.
- **Functional tier (`--tier=functional`):** keeps every assertion. It leaves out measurement-only frame recordings and logs each one as `SKIP perf …`, so a functional run is visibly not a performance run.
- **Reusing a build:** `--exe=<staged FileScape.exe>` reuses a build, and `fs.sh stage` makes one without installing it. Since 2026-11-19 the tiers call `fs.sh stage --reuse`. It uses the newest staging folder that is complete, passed its smoke launch and has the same `source_state` in `build-info.txt`: a hash of the commit, the uncommitted diff and the untracked files that are not ignored. A clean tree also matches an older build-info by its full commit and `uncommitted_paths 0`. The stage row then says `reused staging-…`. `--fresh-stage` always exports a new one.
- **How a step is judged (AD-37, `tools/verify-lib.sh`):** it fails on a non-zero exit (a timeout is named), a missing or empty result file, a result file with no PASS/FAIL lines, or any FAIL; a recording fails without exit 0 and a non-empty video. The reason is printed on the step's line. A partial result file with passing lines never hides a crash or timeout.
- **Real input, right-hold:** `tools/os-righthold-repeat.ps1 -Exe … -Root … -Out … [-Cycles 20] [-Launches 1] [-UiScale 1]` repeats the right-hold look check with per-cycle timing from the state file (`seq`, `appMs`, `lookBeganMs`, last long frame). It moves the mouse and takes focus; it closes only the process it started.
- **Aerial budget:** `--test-max-islands=N --automate=walk-aerial-limit --root=<Wayfarer>` runs the aerial-limit journey with a small budget.
- **The overview at scale (2026-11-20, AD-81):** `FileScape.Fixtures create-vast [parent|-] [moons]` makes "Vast Reach" (≈ 46,200 folders, routes 18 deep); `--automate=planets-overview-attention --test-broad` (the journey; `--counts-only --route=a/b/c` on a real root) and `--automate=planets-overview-bodies --budgets=1500,6000,… --no-vsync` (bodies against frame time); `--test-overview-budget=N` lays out at most N worlds.
- **Planets (2026-11-11):** `--vis=planets` starts in the planetary world; `--root=sample` starts on the Starter Sky (the app writes it once into `%APPDATA%\Godot\app_userdata\FileScape\samples`); `--automate=planets-journey` (or `planets-reel`, the first part only, for a recording) (the first minutes: arrive, walk, twirl, overlook, launch, inspect a picture, open it, V to Places and back, fly back, warp, piles, the list, empty moon, workshop, music box) and `--automate=planets-fixture --root=<Wayfarer> [--wsl-root=\\wsl.localhost\…]`. `--test-shell=record` records Open/Reveal on live files and starts nothing. `--experiment=planet-gallery [--automate=planet-gallery --out=DIR]` shows every planet, object, launcher, landmark and the traveller. `FileScape.Fixtures create-sky [parent]` makes the Starter Sky as a fixture.
- **No parallel journeys:** journeys share the screen, focus and GPU, and their speed checks must not contend, so they run one at a time.

## Choosing the smallest test set (TEST-06..09, AD-88)

Run the cheapest check that can catch the regression you might have made. Run a broader tier only when the change
crosses a boundary the cheap check cannot see. Typical costs (2026-11-20): one Core test class on WSL takes 5–20 s,
including the build; an area set 2–20 s; the whole Core run ≈ 50–60 s (below). `check fast` takes ≈ 1.5 min. A stage takes ≈ 30 s on a quiet machine, ≈ 150 s under load, and
0 s when reused. One journey takes 5–260 s (see `timing.txt` of the last full run). `check full` takes ≈ 42 min.

**While developing:**

1. **Logic first, in Core.** Write or extend a Core test that calls the entry point the app calls. For a bug, write
   it before the fix and see it fail. Run only that test, then its neighbours:
   `tools/fs.sh test-wsl --filter 'FullyQualifiedName~HazardSampleReplayTests'`. Use several classes with `|`, for
   example `'FullyQualifiedName~Hazard|FullyQualifiedName~SphereWalker'`. Windows-only behaviour (paths, shell,
   junctions) uses `tools/fs.sh test --filter …`.
2. **Then the app, only where the change reaches it.** Build once with `tools/fs.sh build` (≈ 20–50 s). Stage once
   with `tools/fs.sh stage --reuse`, which reuses a staging folder of the same source state. Then run only the
   journeys that exercise the change:
   `FS_LAUNCH_LOCK=/tmp/claude-1000/filescape-journey.lock tools/fs.sh check journeys planets-hazard --exe='C:\…\staging-…\FileScape.exe'`.
   Use `--list` for the names and shell patterns (`'planets-nav*'`). With `--exe` the stage step is skipped
   altogether. A probe-only change still needs a new stage, because the probes are compiled into the build.
3. **A rerun needs a reason.** Set `FS_RERUN_WHY="…"` (or `fs.sh timed … --why=…`) so the session's accounting
   says why the step ran again. Before rerunning a failed journey, read its log and decide whether the failure is
   in the instrument or in the product. A load-sensitive timing step that fails once is not by itself a product
   defect.
4. **Ad-hoc launches and probes** (`fs.sh run`, `powershell.exe -File tools/os-*.ps1`, a stage) go through
   `tools/fs.sh timed NAME [--lock] -- CMD…`, so they appear in `tools/fs.sh timing` with their lock waits apart.

### Core tests: the fast loop, area sets, the full run and the perf step (TEST-10..13, AD-90)

Every Core run prints the build's and the tests' seconds, the classes that finished last (each class is one xUnit
collection, run in parallel with the others; its tests run one after another) and the slowest tests, from the run's
TRX (`tools/test-times.py`). Read those lines before blaming the change: under load from other agents every test
slows down together.

| Command | Runs | Takes (WSL, 2026-11-20) | When |
|---|---|---|---|
| `tools/fs.sh test-wsl --filter 'FullyQualifiedName~X'` | the class or test you are writing | 3–15 s with the build | while writing a test, and to see it fail first |
| `tools/fs.sh test-wsl-area AREA[,AREA]` | one area's classes: `music`, `hazard`, `walking`, `overview`, `gallery`, `places`, `panels`, `input`, `navigation`, `scanning`, `baseline` (`tests/FileScape.Core.Tests/areas.txt`) | music 20 s, hazard 12 s, places 10 s, gallery 10 s, the others 2–7 s (with a no-op build) | after the change works, for its area |
| `tools/fs.sh test-wsl --fast` | everything but `Perf`, `Report` and `Slow` (718 of 919) | ≈ 15 s | before a commit that crosses areas |
| `tools/fs.sh test-wsl` / `test` | everything but `Perf` and `Report` (Windows adds its Windows-only tests) | WSL ≈ 40–50 s (28–37 s of tests); Windows ≈ 60 s (46–50 s of tests) | before a commit; in `check fast` |
| `tools/fs.sh test-perf` | the `Perf` (wall-clock bounds) and `Report` (measurement tables) tests, one at a time, Debug | ≈ 55 s on Windows (36 s of tests) | only on a quiet machine; `check full`/`release` run it as `tests-perf` |

- **Smallest first.** A change inside one area: its area set, then `--fast` (or the whole run) before the commit.
  A change to something several areas share (SphereWalker, PlanetComposer, the scanner, SkySynth's core): the whole
  run. A merge or a candidate: `check full` (which adds `tests-perf`).
- **Kinds (`TestKinds`, `[Trait(TestKinds.Trait, TestKinds.X)]`).** `Perf`: asserts on elapsed wall-clock time (a
  Stopwatch bound, "cancelled within 250 ms", "renders a second of audio in under 60 ms"). `Report`: prints a table
  and asserts nothing. `Slow`: a deterministic check over ≈ 3 s alone. An ordinary test must not depend on the
  machine's speed or load: if it needs a bound on time, split the bound into a `Perf` test (as
  `Layout_of_thousands_of_folders_takes_under_150_ms` beside `Layout_is_deterministic_for_thousands_of_folders`); a
  wait with a generous timeout (≥ 100× the normal time, e.g. a 2 s hang guard on something that takes milliseconds) is
  not a bound. A `Perf` failure in a run shared with other work is not a product defect until it fails in
  `test-perf` on a quiet machine.
- **Where a new test goes.** Music: the SkySynth class of its area (`SkySynthTests.cs` holds the effects, worlds and
  tempo, theme, arrangement, hazard-cue and evidence classes; `SkySynthTests.Bell.cs`, `.Drama.cs`, `.Experience.cs`,
  `.Landmarks.cs`, `.Modes.cs`, `.SecondThemes.cs`, `.Tempo.cs`, `.Effort.cs` one class each), using the shared
  helpers in `SkySynthKit` (`SkySynthTests.Kit.cs`). A new area of music gets a new `SkySynthTests.Area.cs` file with
  its own class. Hazard: the bare-gap envelope and recovery in `HazardMovementTests`, composed-world audits in
  `HazardGapWorldTests` / `HazardLeapWorldTests`, world shape in `HazardWorldTests`, timing in
  `HazardRecoveryTimingTests`, Starter Sky replays in `HazardSampleReplayTests`. A class that grows past ≈ 30 s of
  work alone becomes the run's critical path: split it by section into classes (a class per collection), as these
  were. A new class must start with an area's prefix (`TestAreaTests` fails otherwise): add the prefix to
  `areas.txt` if no area fits.
- **Making a heavy test cheaper without weakening it** (TEST-12): independent cases computed side by side with
  `TestParallel.Map` and asserted afterwards in order; per-pair assertions only where violated
  (`if (overlap) …Should()…`), since a FluentAssertions call per pair costs more than the geometry; sorted `Equal`
  instead of `BeEquivalentTo` on hundreds of items; a sweep that searches for a maximum tried from the top down.
  Show the result is unchanged (the envelope's printed widths, the counts) when you do.
- **Release path.** The integrator runs `check full` on the merged candidate: Core on both hosts in parallel with the
  build and the stage, then `tests-perf` alone, then the journeys. `tests-perf` failing there is a real regression
  candidate; rerun it alone (`tools/fs.sh test-perf`) once before calling it one.
- **Configuration.** The ordinary runs build Release (as the exported game, `--export-release`) with tiered
  compilation off in the test host (every method optimised at once: with 24 classes side by side the tiering kept
  postponing it); `test-perf` builds Debug because its bounds were measured on Debug. `dotnet test` from an IDE or by hand is Debug: it runs the same
  tests, slower.

**Broader tiers:**

| Change touches | Also run |
|---|---|
| Core only (no app call sites changed) | the Core classes for that area; `check fast` before the commit |
| app code, but not input, rendering or audio | `check fast`; the journeys for that feature |
| input routing, keyboard ownership, focus, panels | the feature's journeys, plus `planets-dialog-keys` and `planets-destinations-keys`; real input (`os-planets-probe.ps1`) for the input path you changed |
| rendering, camera, animation, sound | the feature's journeys (they measure frames, screen positions, audio levels); a recording if the look matters |
| `verify.sh`, `verify-lib.sh`, `fs.sh` | `tools/fs.sh check harness` (≈ 5 s) |
| a merge of several streams, or a build to hand over | `check full`, run once by the integrator; `check release` for a report's evidence |

Never run `check full` in a parallel stream. The integrator runs it once on the merged candidate.

**Moving journey assertions into Core (the path for later assignments).** For each slow journey:

1. List its assertions (`grep -E 'PASS|FAIL' <evidence>\<journey>\<journey>.log`) and mark each one as either
   input delivery, rendering, animation, audio, Windows integration, or the boundary between systems. Those stay.
   Deterministic state or timing is the rest, and it can move.
2. Find (or extract) the Core entry point the app uses for that logic: PlanetComposer, SphereWalker,
   DestinationTree, OverviewDiscovery, HazardRules, KeyboardOwner policy and so on. Write the Core test with the
   journey's own inputs: its fixture (the Starter Sky can be written and scanned in Core: `SampleSkyFixture`,
   `ScanRunner`), its stand points and press timing, and the measured input lag (2 frames, AD-88).
3. **Show equivalence.** Inject one regression that the journey step catches. Run the unchanged journey on a
   staged build with the regression, and run the Core test. Both must fail. Then remove the regression.
4. Trim the journey. Keep one representative path through the running app per feature, so the boundary is still
   exercised. Leave a comment naming the Core test. Measure the journey before and after on the same staged builds
   under the same load, and record both numbers in the implementation status.

**Next opportunities, ranked by estimated seconds saved per full run** (from `full-20261120-062206`; estimates):

| # | Journey (s) | Assertions to move | Core home | Est. saved |
|---|---|---|---|---|
| 1 | planets-overview-enter (256) | Enter lands, or says why not, for each overview selection state (PLANET-83); the held-back listing's message | the overview's Enter policy, extracted from Main.Planets | ≈ 150 s (stream 2's area this pass) |
| 2 | planets-music (164) | which piece plays where, the minimum dwell through quick hops, a return resuming the overlook (AD-79) | the music selector with simulated time (SkySynth's selection, not playback) | ≈ 120 s; keep the underrun check and one transition (stream 5's area) |
| 3 | planets-dialog-keys (172) | for each panel and key, who owns the key (AD-68) | Main.KeyboardOwner's decision as a pure table | ≈ 110 s; keep one typed route per panel (stream 3's area) |
| 4 | planets-look (135) | the pitch's return while walking (rate 2.5/s, delay 0.5 s, snap), a drag holding, the overlook pitch | `ReturnPitchWhileWalking`, extracted from PlanetWorld.Walk into a Core camera-pitch model | ≈ 70 s; keep the drags (input) and one sweep (rendering) |
| 5 | planets-tutorial-cues (100) | which cue shows (beacon, edge arrow, horizon pin) for a target's screen position | a Core cue-placement policy over projected points | ≈ 50 s |
| 6 | planets-navigation (84) | the trip state machine: hop by hop, Stop, Esc in a wind-up, an unavailable hop, Backspace | the trip and history state (PlaceNavigator, DestinationTree) | ≈ 45 s; keep one trip and the delayed listing |
| 7 | places-tree (51) | its two own assertions (a bounded tree scan does not travel through a junction; the map lays out the whole tree) | the scanner and the place layout | ≈ 45 s, by dropping the journey (its other assertions duplicate places-connected) |
| 8 | planets-hazard (≈ 70 after TEST-09) | an uncaught slip falls within 0.55 s; the efforts in the fall; the classification of the sample | HazardRecoveryTimingTests and HazardRules (much is already there) | ≈ 15 s |
| 9 | planets-overview-hub vs -sample (22 each) | the same overview check from two starting routes | — (one of them could move to release) | ≈ 22 s |
| — | legacy views: walk-ui1.0, places ×3, explore, navigation, map-journey, aerial ×2 (≈ 330 s) | — | whether they belong in `check full` at all is DJ's call (docs/legacy-views.md) | up to ≈ 330 s |

**Fixed waits (TEST-07, 2026-11-20 review).** The shared helpers already wait on conditions. `ProbeBase.Until`, and
`Fly` and `WaitSettled` in PlanetsProbe, poll every frame with a timeout. The launch overhead is ≈ 1–4 s per journey.
The fixed `await Seconds(…)` calls are settle times before a screenshot or a measurement inside each journey. They add
up to about 470 s over all probe files, but only part of that runs in any one journey. The largest are
PlanetsProbe.Music (133 s in 12 waits: dwell times the music checks measure, so they are not avoidable),
PlanetsProbe.cs (48 s, shared by planets-ui and the flows), Travel (25 s), Actions (25 s), Discovery (21 s) and
Lookouts (20 s). When you replace one of them with `Until(condition, timeout)`, the condition must be what the
following check needs (for example `P.CameraSettled` before a framing screenshot), and the journey must keep the same
PASS lines.

## Launching and closing (AD-28, AD-76)

- **`FileScape.exe`** is a window program: cmd and PowerShell return to their prompt at once. Since AD-76 it writes nothing into a terminal that did not wait for it (Godot attaches to the terminal in native start-up; FileScape lets go at once, and the engine header is off), so the prompt stays the last line and is usable while FileScape runs and after X. Its log is in `%APPDATA%\Godot\app_userdata\FileScape\logs`.
- **`FileScape.console.exe`** (same folder, same arguments) waits until the window closes, shows the log and returns the exit code. Use it from a terminal when you want the log. Godot's wrapper waits for every process in its job; since AD-76 what FileScape opens for you (an app, Explorer, a terminal) is started by the desktop's Explorer, outside that job, so it neither keeps the terminal waiting after X nor dies on Ctrl+C.
- **Checking it:** `tools/os-shutdown-probe.ps1 -Exe <staged FileScape.exe or .console.exe> -Out <dir> [-Shell cmdk|cmdc|batch] [-AppArgs '-- …'] [-OpenedApp msedge]` types the command into an interactive cmd in its own conhost window, closes the window as X does and checks whether cmd takes a command. `tools/os-open-probe.ps1` checks a real open with a free handler (it refuses if the handler is already running).
- **From WSL**, either one waits, because WSL interop waits for the Windows process.
- **Default launch.** With no arguments, FileScape opens in Planets at **This computer**. Use `--vis` to choose a view or `--root` to choose a starting folder. Use `--root=hub` (or `--root="This PC"`) to request the hub explicitly.

## Quirks found and how they are handled

1. **Shared tree, two hosts.** WSL and Windows builds would clobber each other's `obj/project.assets.json` (different absolute paths). `Directory.Build.props` sends Windows builds to `obj.win/` and `bin.win/`, and excludes both hosts' generated sources from compilation.
2. **Hung pipes.** MSBuild nodes and the compiler server inherit stdout and outlive the build, so the caller waits (Godot's internal `dotnet publish` once stalled an export for about 10 min). `fs.sh` disables them for every Windows process via `WSLENV`-forwarded `MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_USE_MSBUILD_SERVER=0`, `UseSharedCompilation=false`, plus `--disable-build-servers`.
3. **apphost copy denied.** MSBuild intermittently got "access denied" copying `apphost.exe` to `bin.win\…\FileScape.Fixtures.exe` on the UNC share, while a plain PowerShell copy of an .exe to the same folder worked. Root cause not established. Mitigation: `UseAppHost=false` for the fixtures tool.
4. **Export needs a solution file named after the assembly** (`app/FileScape.App.sln`). Without it, Godot exported a bare exe with no .NET assemblies, which then crashed on launch (0xC0000005).
5. **Vulkan device loss (KD-01).** D3D12 is the project default (AD-11). `tools/compare-drivers.sh` repeats the Vulkan/D3D12 stability comparison on the exported build.
6. **Replacing a running build (2026-11-05).** Exporting straight into `build` while DJ's FileScape ran from it deleted every file the process did not hold open. Fixed by exporting to `build-staging` and refusing to replace a running build (see above); the deleted runtime files were restored from the staged export without touching the running process.

**Preferences (2026-11-05 evening).** The app reads and writes `%APPDATA%\FileScape\preferences.json` (look direction, levelling on release, walk/run speed). `--prefs=PATH` uses another file; under `--automate` nothing is read or written unless `--prefs` is given, so probe runs never change DJ's settings. Fixture `create-walk` now includes `Journeys/Coast/Harbour/Pier` and `Leg 1/2/10`.
