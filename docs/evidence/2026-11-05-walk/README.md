# Evidence — walking world pass (2026-11-05)

Raw runs (with every intermediate run of the day) are in `%LOCALAPPDATA%\FileScape\evidence\2026-11-05-walk\` on DJ's laptop; this folder holds the final runs. Fixtures: "Wayfarer" (`FileScape.Fixtures create-walk`, under `%TEMP%\FileScape-fixtures\walk-20261105-025501-8e77bd`), "Atlas Studio" (2026-11-04), and a WSL fixture made by `tools/make-wsl-fixture.sh` (`\\wsl.localhost\Ubuntu-24.04\tmp\filescape-wsl-fixtures\wsl-20261105-094515`). No private folder was listed: the hub shows only drive letters and distribution names; the only real drives entered were D:\ (removable, no media — "not ready"); C:\ was never entered.

Builds: all runs used exported builds in `%LOCALAPPDATA%\FileScape\build-staging` (DJ's FileScape was running from `build`, so the export was staged — see implementation-status). `walk.log` (journey) was run on the final commit `5f261df`; `places-journey.log` and `os-walk-results.txt` on `24b6167`; `places-scan.log`, `navigation.log`, `walk-perf-isolation.*` and the tour on `466e6d9`. Later commits changed only HUD opacity, the overlook title position, the plaque summary size and the inspector refresh.

| File | What | Result |
|---|---|---|
| `walk.log`, `NN-*.png` | Walking-world journey, injected input (`--automate=walk`), 1600×900, High | 66 PASS, 0 FAIL |
| `os-walk-results.txt`, `os-0N-*.png` | Real SendInput (`tools/os-walk-probe.ps1`) | 17/17 |
| `places-journey.log` | The 2026-11-04 Places journey, ported to the connected model | 52 PASS, 0 FAIL |
| `places-scan.log` | Places during a 50k synthetic scan; cancel states | 0 failures (pending/recomposition re-observed on a slow stream) |
| `navigation.log` | Map navigation probe (Classic, synthetic) | 0 failures |
| `walk-perf-isolation.*` | Stationary frame cost, one effect at a time | see implementation-status (machine-state caveat) |
| `walk-perf.csv`, `places-journey-perf.csv` | Walking frame times | see caveat |
| `control-old-build-perf-today.log` | **Control:** the 2026-11-04 export measured today on the same machine | root High 20.2 ms (6.5 ms on 2026-11-04) |
| `walk-tour.log`, `tour-frame-*.jpg` | Guided recording at eye level (`walk-tour.avi`, 1,454 frames at 30 fps, ≈ 309 MB, not in the repo) and sampled frames | recorded |

Screenshots (journey): arrival on foot, plaza at eye level, looking down while walking, overlook, child entered, arrived through a link, back through the link, problems and links, project place, above the arrival root, WSL arrival, locations hub, unavailable drive, flying, landed.

Caveat on performance: Windows reported a remote (Terminal Services) session; frame times varied 2× between identical runs within minutes. Compare only within a run.
