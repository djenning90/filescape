# Evidence — usability pass from DJ's hands-on feedback (2026-11-05, evening)

All app runs used the **installed build** `C:\Users\David\AppData\Local\FileScape\build\FileScape.exe`, identity `4b030e39b93a` (clean tree; SHA-256 in `build-info-installed.txt`), produced by `tools/fs.sh export` (staged, verified, smoke-launched, promoted; DJ's previous build `2dc4ae8c3234` kept as `previous-20261105-072550-333871`, see `builds.txt`). Raw runs: `%LOCALAPPDATA%\FileScape\evidence\2026-11-05-usability\final\`. Fixtures: Wayfarer `walk-20261105-071512-10a41c` (new: Journeys › Coast › Harbour › Pier, Leg 1/2/10), Atlas Studio `places-20261104-111359-0a3db4`, WSL `wsl-20261105-094515`.

| File | What | Result |
|---|---|---|
| `walk-ui1.0.log`, `walk-ui1.25.log` | Walking journey, injected input: look direction (default reversed; Settings checkboxes clicked; left-drag; arrows unchanged), walk/run speed, gate plate and chip every frame from 9 m through a gate, natural order, U then W held up four levels, Up from a link's destination, Back, and the earlier walking-world checks (WSL included) | 95 PASS, 0 FAIL each |
| `os-walk-results-ui1.0.txt`, `os-walk-results-ui1.25.txt`, `os-probe-preferences.json` | Real SendInput: real mouse direction before/after a real click on the checkbox, the saved file, applied again after a relaunch, real W held from Harbour to Wayfarer | 25/25 each |
| `traversal-timings.txt` | Core walker timings on composed places: arrival → gates, Up routes, four successive Ups; before/after | see file |
| `places-connected.log`, `places-bounded-tree.log` | Places journey, connected world / bounded tree scan | 52/0 · 51/0 |
| `places-scan.log`, `navigation.log`, `map-journey.log` | 50k synthetic scan in Places (p99 12.6 ms), map navigation, map journey | 0 failures each |
| `walk-perf-ui1.0.csv` | Walking frame cost (High, 1600×900): mean 6.89 ms, p99 8.19 ms | — |
| `approaching-portal-9m-ui1.0.png`, `portal-close-3m-ui1.0.png` | Plate and chip at 9 m; at 3 m the plate is under the HUD's top panels and the chip names the gate | — |
| `upward-arrival-harbour-ui1.0.png`, `os-upward-chain-end-ui1.0.png` | Arrival after U facing the ▲ gate; the end of the real-key chain in Wayfarer | — |
| `natural-order-journeys-ui1.0.png`, `settings-panel-ui1.25.png`, `arrival-ui1.25.png` | Leg 1, Leg 2, Leg 10; the Settings panel; the top bar fitting at 125 % | — |

Automated checks establish behaviour (directions, speeds, routes, identification, counts); whether the movement *feels* right is DJ's judgement.
