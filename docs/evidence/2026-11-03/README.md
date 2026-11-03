# Evidence, 2026-11-03 (first prototype run)

A curated subset of the run outputs. Raw runs (every screenshot, every per-frame CSV) remain under `%LOCALAPPDATA%\FileScape\evidence\` on DJ's machine. No private file listings: the live runs used the generated fixture, `C:\Program Files\dotnet`, and the NuGet package cache. Logs record aggregate counts plus the few paths the scripts selected.

| File | What it shows | Kind |
|---|---|---|
| `journey.log` | Synthetic journey on the final exported build (D3D12): PASS/FAIL checks for selection preservation, reset, parent/back, picking accuracy, mock Open | launched, tested |
| `classic-home.png`, `classic-overview.png` | Home (reset) and bird's-eye views, Classic, 12k synthetic nodes | visually observed |
| `classic-selected-file.png`, `district-selected-file.png` | The same selected file in both visualizations, with the inspector | visually observed |
| `classic-access-denied.png`, `district-wide-2000.png`, `district-low-quality.png`, `district-after-reset.png` | Edge cases, a 2,000-file directory, the Low preset, recovery | visually observed |
| `live-fixture*.log`, `live-fixture-classic.png` | Live scan of the on-disk fixture: counts match the manifest; ShellExecuteEx opened an Explorer window (confirmed externally) | launched, tested |
| `live-nuget*.{log,png}`, `live-dotnet.log` | Live scans of tool directories; live cancellation (stopped 0.2 ms after the request) | launched, measured |
| `perf-d3d12-final*.{log,csv}`, `perf-vulkan-editor-summary.csv`, `perf-district-high-1080p.png` | Frame times at 50k nodes, 1920×1080, VSync off | measured |
| `scanperf-*.{log,csv}` | Per-frame times while a 50k-node scan streams in (columns: time, frame_ms, gpu_ms, scanning, relayout_ms, nodes) | measured |
| `stability-vulkan-vs-d3d12.txt`, `vulkan-device-lost-excerpt.txt`, `stress-novsync-district.log` | KD-01 investigation | measured / observed |
