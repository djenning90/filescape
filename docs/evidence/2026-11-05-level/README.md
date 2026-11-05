# Evidence — view levelling after a mouse look (CTRL-04), 2026-11-05 evening

All app runs used the **installed build** `C:\Users\David\AppData\Local\FileScape\build\FileScape.exe`, identity `e03dd62adb62` (clean tree; hashes in `build-info-installed.txt`; `builds.txt` lists the kept previous builds). Raw runs: `%LOCALAPPDATA%\FileScape\evidence\2026-11-05-level\final\`. Fixtures as in `../2026-11-05-usability/`.

| File | What | Result |
|---|---|---|
| `walk-ui1.0.log`, `walk-ui1.25.log` | Walking journey, injected input, with the new levelling checks (right-hold and left-drag level after release, heading kept, a new look takes over, off by a real checkbox click) and all earlier checks, WSL steps included | 103 PASS, 0 FAIL each |
| `os-walk-results-ui1.0.txt`, `os-walk-results-ui1.25.txt` | Real SendInput: direction read while held, level 0.7 s after a real release, Settings click, relaunch, real W up a four-level chain | 25 PASS, 0 FAIL each |
| `os-walk-results-ui1.0-instrument-defect-3efd3a3.txt` | The run that exposed the probe's empty state read (one spurious FAIL in the chain: an empty folder name at 5.0 s; the product reached Wayfarer in 12.1 s) | instrument defect, fixed |
| `places-connected.log`, `places-bounded-tree.log` | Places journey at UI 1.0 (it assumes 1.0, KD) | 52/0 · 51/0 |
| `settings-panel-ui1.0.png`, `settings-panel-ui1.25.png` | Settings with the new checkbox, now above the other panels | — |

Automated checks establish the behaviour (pitch returns to 0 in 0.35 s, monotonically, heading unchanged); whether it *feels* right is DJ's judgement.
