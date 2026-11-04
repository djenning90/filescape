# Evidence — Places pass and VIS-11 (2026-11-04)

Machine: Lenovo 21RX, Radeon 890M (driver 32.0.22024.19001), Windows 11, D3D12, exported release build
(`%LOCALAPPDATA%\FileScape\build\FileScape.exe`). Data: the deterministic Places fixture ("Atlas Studio",
`FileScape.Fixtures create-places`, owned temp directory) and the synthetic sample — no private file listings.
Full raw runs (all screenshots, CSVs, the recording) are in `%LOCALAPPDATA%\FileScape\evidence\2026-11-04-places\`.

| File | What it shows | Evidence tier |
|---|---|---|
| `places.log` | The Places journey: every check, PASS/FAIL, `[direct]` set-up steps labelled | injected input (exported build) |
| `places-perf.csv` | Walking frame times per case (root High/Low, wide-hall, many-files) | measurement |
| `places-scan.log`, `places-scan-perf.csv` | Scan responsiveness (50k nodes at 10k/s) and cancellation states | injected input + measurement |
| `os-places-results.txt` | Real SendInput journey (Destinations click, W through a gate, S does not travel, U, G, Ctrl+Home, Alt-Tab, B, M, Enter → Explorer) | OS-level input |
| `navigation.log`, `maps-journey.log` | Reference views: the 2026-11-03 navigation probe and map journey re-run on this build | injected input / direct calls |
| `flicker-summary-orbit.csv` | VIS-11 on an orbit around a selection: `pre-fix` (sun specular 1.0, puddle roughness 0.04) vs `baseline` (mitigated) and one-effect-off rows; columns end with cell spikes, frames with a spike, peak cell deviation | measurement (exported build) |
| `flicker-summary-beamtop.csv` | VIS-11 with the beam top in view: `pre-fix`/`beam-unclamped-only` ≈ 80 bright px per 320×180 frame vs 0.1 fixed | measurement (exported build) |
| `flicker-summary-high.csv`, `flicker-summary-street.csv` | Earlier paths: a high dolly (no blob-scale flicker either way) and a street-level path that turned out unusable as an instrument (near-object parallax) | measurement (editor build, before the fix) |
| `flicker-beam-pre-fix.png` / `flicker-beam-fixed.png` | The sun-like blob at the top of the District selection beam (NaN spread by glow), before and after the clamp | screenshot A/B |
| `01-arrival.png` … `18-locked.png` | The journey (numbers match `places.log`): arrival, looking back toward the way in, approaching a gate, threshold transition, arrival in a child, return in front of the gate, ascent, map "You are here", wide arrival and ascent, empty, denied | screenshots |
| `scan-in-progress.png`, `scan-after-cancel.png` | Pending gates during a scan; "not scanned" gates after cancelling | screenshots |
| `places-tour.log` | The recorded tour's log (the AVI itself stays outside the repo) | recording |

The recording (`tour.avi`, MJPEG, Godot movie writer at 30 fps) stays outside the repository because of its size.
