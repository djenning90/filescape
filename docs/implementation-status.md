# Implementation status

Last updated: 2026-11-03, after the first prototype run (commits `ee39c8f`…HEAD). Hardware: Lenovo 21RX, Ryzen AI 9 HX PRO 370, **Radeon 890M iGPU** (driver 32.0.22024.19001, 8 GiB UMA carve-out), 87.6 GiB RAM, Windows 11 Pro 26200, on AC power. Evidence files: `docs/evidence/2026-11-03/`. The full raw runs are in `%LOCALAPPDATA%\FileScape\evidence\` on DJ's machine and are not in the repo.

Vocabulary: **Demonstrated** = observed with recorded evidence; **Provisional** = works in limited conditions; **Failed** = observed not working; **Unknown** = not established.

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

## Provisional decisions to confirm with DJ

Godot (AD-01), the Fishbone default (AD-08), the log height scale (AD-07), click = select-only (FSN approached on click), and D3D12 as the default driver (KD-01, AD-11).

## Next useful iteration (recommended)

1. DJ dogfoods the standalone build on a couple of real folders and reports friction: camera feel, label density, District readability.
2. Keep watching KD-01 on D3D12 during dogfooding. Reproduce the Vulkan loss on a minimal Godot scene (sustained volumetric fog + SSR, then a readback) and, if it reproduces, report it upstream. Add a shader warm-up for KD-03.
3. Move layout and instance packing off the main thread (KD-02), then try 200k–1M nodes with subtree summarization and LOD for distant districts.
4. One more District pass: ground-level light pools, distant-block impostors, and street names at intersections.
