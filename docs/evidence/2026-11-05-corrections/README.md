# Evidence — correction and units pass (2026-11-05, afternoon)

All runs below used the **installed build** `C:\Users\David\AppData\Local\FileScape\build\FileScape.exe`, identity `2dc4ae8c3234` (clean tree; SHA-256 in `build-info-installed.txt`), produced by `tools/fs.sh export` (staged, verified, smoke-launched, promoted; the previous build kept as `previous-20261105-045758-315390`, see `builds.txt`). Raw runs: `%LOCALAPPDATA%\FileScape\evidence\2026-11-05-corrections\final\`. Fixtures: Wayfarer `walk-20261105-043048-b5a7d4` (with a long-named folder), Atlas Studio, the WSL fixture `wsl-20261105-094515`.

| File | What | Result |
|---|---|---|
| `walk-ui1.0.log`, `walk-ui1.25.log` | Walking journey (injected input) at UI scale 1.0 and 1.25, incl. overlay-label **and world-sign** checks at five places | 71 PASS, 0 FAIL each |
| `os-walk-results.txt` | Real SendInput probe | 17/17 |
| `places-connected.log` / `places-bounded-tree.log` | Places journey in the connected world / the Bounded tree scan mode (map lays out 1,713/1,713 nodes) | 52/0 · 51/0 |
| `places-scan.log` | Places during a 50k synthetic scan; cancel states | 0 failures (p99 11.9 ms) |
| `navigation.log`, map journey log | Map camera (speeds identical before/after the units refactor) and the map journey | 0 failures |
| `walk-perf-isolation.log`, `walk-perf-ui1.0.csv` | Frame cost (machine back to normal: walking 6.98 ms mean, p99 8.6 ms, High) | — |
| `export-failure-paths.txt` | `fs.sh export` against a throwaway base: process check fails (exit 6), normal promotion, previous kept, running build (exit 4), prune | as designed |
| `allocations.txt` | Release micro-benchmark before/after the units refactor | no new allocations |
| `arrival-ui1.0.png`, `arrival-ui1.25.png`, `wsl-arrival-ui1.25.png`, `unavailable-drive.png`, `bounded-tree-map.png` | Screenshots | — |
