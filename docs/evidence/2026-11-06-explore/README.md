# Evidence — exploration pass (2026-11-06): reclaimed view, glanceable names, icons, solid stairs, jump, flight, aerial world, planetoid experiment

All app runs used the **staged, verified build** `C:\Users\David\AppData\Local\FileScape\staging-20261105-150710-406825\FileScape.exe`, identity **`4a7b9ce6fdf6`** (clean tree; SHA-256 in `build-info-staged.txt`), produced by `tools/fs.sh export` (staged, verified, smoke-launched). It was **not promoted** to `build\`: DJ's own FileScape (started 19:55, no arguments) was running from `build\` throughout, and the export never replaces a running build (AD-21). `builds.txt` lists what is installed and kept. Raw runs, including a 200 MB recording (`explore-recording.avi`, the exploration journey at 1280×720, 30 fps), are in `%LOCALAPPDATA%\FileScape\evidence\2026-11-06-explore\final4\`. Fixtures: Wayfarer `walk-20261105-071512-10a41c`, Atlas Studio `places-20261104-111359-0a3db4`, WSL `wsl-20261105-094515`.

Kinds of evidence:
- **Injected:** events go through Godot's input pipeline.
- **Real:** Win32 SendInput.
- **Direct:** probe set-up steps, marked [direct] in the logs.
- **Measured:** frame times.
- **Visual:** screenshots I looked at.

| File | What | Result |
|---|---|---|
| `verification-summary.txt` | The whole suite on the staged build | see below |
| `explore-ui1.0.log`, `explore-ui1.25.log` | The exploration journey (injected). It covers: <ul><li>the default view (82 % / 75 % of the window shows the world);</li><li>the key on K; the Destinations drawer by keyboard and by mouse;</li><li>10 of 10 gates named at once;</li><li>the jump (held 1.20 m, tap 0.66 m);</li><li>take-off (16 m up, looking down at −35°), with the pitch kept in flight;</li><li>vertical controls at gazes of −60°, 0° and +40°;</li><li>B and the aerial world (42 places); discovery bounded to the arrival folder;</li><li>clicking an island, then G to arrive above Journeys;</li><li>flying with W to Coast and landing with T; Backspace;</li><li>landing far from everything;</li><li>aerial performance.</li></ul> | 29/0 each |
| `explore-ui1.0-low-load.log` | The same journey when the machine was quiet. Aerial flight: 3.9 ms mean, p99 5.2 ms | 29/0 |
| `walk-ui1.0.log`, `walk-ui1.25.log` | The walking-world journey (all earlier checks, WSL included) | 103/0 each |
| `walk-ui1.25-load-flake.log` | A run at 125 % during heavy machine load. "Walking while looking down is as fast" measured 7.66 against 8.13 m/s (the tolerance is 3 %). The rerun gave 8.01 against 8.01. | 102/1, a timing flake under load |
| `os-walk-results-ui1.0.txt`, `os-walk-results-ui1.25.txt` | Real SendInput: look direction; levelling on foot; J/Esc and K; T; real E/Q/W in flight; landing; Destinations filtered with real keys, then a row clicked; a link and back; settings after a relaunch; holding W up a chain | 29/0 each |
| `places-connected.log`, `places-bounded-tree.log` | Places journey at UI 1.0. A 240-gate place names 40 gates at once (the cap) without overlaps. | 53/0 · 52/0 |
| `places-scan.log`, `navigation.log`, `map-journey.log` | 50k-node synthetic scan in Places; map navigation; map journey | 0 failures |
| `planetoids.log` | Experiment: walk on the teal world, reach the pad, launch, land walking on the coral world | 4/0 |
| `walk-perf-low-load.csv`, `walk-perf-under-load.csv` | Stationary frame cost. With the machine quiet: plaza 6.79 ms mean (6.9 ms on the previous pass). With DJ's instance and other load: about 15–20 ms. The previous build measured the same under that load (15.8–16.1 ms), so the load comes from the environment. | — |
| `explore-*.png`, `wide-place-*.png`, `file-badges-7m-ui1.0.png`, `terrace-stairs-ui1.25.png`, `overlook-solid-ui1.0.png`, `planetoid-*.png` | Screenshots: the default view, the take-off, the aerial neighbourhood at 1.0 and 1.25, arriving above Journeys, landing, a wide place's names and aerial fan, badges at 7 m, the solid stairs and overlook, the planetoid experiment | — |

Automated checks establish behaviour. Whether it is *enjoyable* is DJ's judgement.
