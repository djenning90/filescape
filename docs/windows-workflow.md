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
tools/fs.sh test         # full test suite with Windows dotnet.exe (includes Windows-only tests), one process (FS_TEST_SHARDS=1: shards)
tools/fs.sh test-wsl     # Linux SDK in three shards side by side; the Windows-only tests skip with a reason
tools/fs.sh test --filter 'FullyQualifiedName~PlaceTests'   # with arguments: one plain `dotnet test` (FS_TEST_SHARDS=0 too)
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

Fixture tool (Windows): `dotnet src\FileScape.Fixtures\bin.win\Debug\net8.0\FileScape.Fixtures.dll create|delete <dir>|delete-all`. It only creates under `%TEMP%\FileScape-fixtures` by default, and refuses to delete anything without its marker file.

## Verification tiers (2026-11-06, AD-29)

| Command | Takes | Runs | When |
|---|---|---|---|
| `tools/fs.sh check harness` | ≈ 3 s | the harness's own judging, every pass/fail path with test doubles (nothing launched) | after changing `verify.sh` or `verify-lib.sh` |
| `tools/fs.sh check fast` | ≈ 3.5 min (205 s, 2026-11-19) | app build, Core tests on Windows and WSL | every change |
| `tools/fs.sh check smoke` | ≈ 10 min | fast + `fs.sh stage --reuse` + walk-explore, the hub → project journey and the planets journey on the Starter Sky (UI 1.0, functional) | before a commit that touches the app |
| `tools/fs.sh check full` | ≈ 32 min of steps (2026-11-19: 1,888 s, plus any launch-lock waits) | smoke + walk, places (connected, tree), scan, navigation, map journey, planetoids, aerial limit (budgets 24 and 8) at 1.0; explore, places and planets at 1.25 (functional); the planets fixture tour (Wayfarer: links, empty room; the WSL fixture) | before handing a build over |
| `tools/fs.sh check release` | ≈ 50 min (not re-measured) | full with measurements, walk at 1.25, walk-perf, real OS input at 1.0 and 1.25 (moves the mouse, takes focus), recordings (planets journey, explore) | evidence for a report |
| `tools/fs.sh check journeys NAME...` | the named steps | only the named journeys (shell patterns: `'planets-nav*'`), on a staged build (reused) and without Core tests; real-input steps (`os-*`) only by exact name; `--measure` keeps measurement segments; `--list` prints every step name | while working on a journey |

- **Output:** each run writes `%LOCALAPPDATA%\FileScape\evidence\checks\<tier>-<stamp>\summary.txt`, with every step's seconds, its exit code, and its PASS/FAIL/SKIP counts.
- **Timing table (TEST-05, AD-80):** every run ends with one table: each step's name, result (`pass`, `fail`, `timeout`, `skipped`, `interrupted`) and seconds, then the total and the counts. It is printed last, appended to `summary.txt` and written to `timing.txt` in the same folder. It also appears after a failure, a timeout, an early exit (no fixtures, no staged build) and Ctrl+C or SIGTERM. A signal stops the running step's command (only the process the harness started) and marks that step `interrupted`. The Core tests' rows carry their counts (`734 passed, 0 failed, 26 skipped of 760`).
- **Shared desktop: `FS_LAUNCH_LOCK=/path`.** Each app launch (every journey, real-input probe and recording, the stage's smoke launch, `fs.sh run`) holds this flock for that launch only. The lock's descriptor is closed in the launched command. A step's seconds are its own run. Its wait for the lock is in its note (`lock wait 48.2 s`), and the TOTAL line sums the waits. Example: `FS_LAUNCH_LOCK=/tmp/claude-1000/filescape-journey.lock tools/fs.sh check full`.
- **What runs side by side:** the WSL Core tests run as three `dotnet test --no-build` processes with complementary filters (SkySynthTests with "theme" in the name, SkySynthTests without, everything else). Each test runs exactly once, and a shard without a result line fails. The WSL tests run in the background beside the build, the Windows tests and the stage, because they use their own `obj/`, `bin/` and SDK. The journeys wait for them, because their timing checks must not share the machine.
- **Upper bounds:** build 600 s, each host's tests 1,200 s, stage 1,800 s. A hang ends as a `timeout` row.
- **Functional tier (`--tier=functional`):** keeps every assertion. It leaves out measurement-only frame recordings and logs each one as `SKIP perf …`, so a functional run is visibly not a performance run.
- **Reusing a build:** `--exe=<staged FileScape.exe>` reuses a build, and `fs.sh stage` makes one without installing it. Since 2026-11-19 the tiers call `fs.sh stage --reuse`. It uses the newest staging folder that is complete, passed its smoke launch and has the same `source_state` in `build-info.txt`: a hash of the commit, the uncommitted diff and the untracked files that are not ignored. A clean tree also matches an older build-info by its full commit and `uncommitted_paths 0`. The stage row then says `reused staging-…`. `--fresh-stage` always exports a new one.
- **How a step is judged (AD-37, `tools/verify-lib.sh`):** it fails on a non-zero exit (a timeout is named), a missing or empty result file, a result file with no PASS/FAIL lines, or any FAIL; a recording fails without exit 0 and a non-empty video. The reason is printed on the step's line. A partial result file with passing lines never hides a crash or timeout.
- **Real input, right-hold:** `tools/os-righthold-repeat.ps1 -Exe … -Root … -Out … [-Cycles 20] [-Launches 1] [-UiScale 1]` repeats the right-hold look check with per-cycle timing from the state file (`seq`, `appMs`, `lookBeganMs`, last long frame). It moves the mouse and takes focus; it closes only the process it started.
- **Aerial budget:** `--test-max-islands=N --automate=walk-aerial-limit --root=<Wayfarer>` runs the aerial-limit journey with a small budget.
- **Planets (2026-11-11):** `--vis=planets` starts in the planetary world; `--root=sample` starts on the Starter Sky (the app writes it once into `%APPDATA%\Godot\app_userdata\FileScape\samples`); `--automate=planets-journey` (or `planets-reel`, the first part only, for a recording) (the first minutes: arrive, walk, twirl, overlook, launch, inspect a picture, open it, V to Places and back, fly back, warp, piles, the list, empty moon, workshop, music box) and `--automate=planets-fixture --root=<Wayfarer> [--wsl-root=\\wsl.localhost\…]`. `--test-shell=record` records Open/Reveal on live files and starts nothing. `--experiment=planet-gallery [--automate=planet-gallery --out=DIR]` shows every planet, object, launcher, landmark and the traveller. `FileScape.Fixtures create-sky [parent]` makes the Starter Sky as a fixture.
- **No parallel journeys:** journeys share the screen, focus and GPU, and their speed checks must not contend, so they run one at a time.

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
