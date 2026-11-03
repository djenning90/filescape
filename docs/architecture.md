# Architecture (prototype)

```
FileScape.Core (net8.0, no engine reference)            app/ (Godot 4.7 .NET)
┌───────────────────────────────────────────┐          ┌──────────────────────────────────────────┐
│ Scanning  IScanSource ─▶ ScanSession ──batches──▶ Main (main thread, only writer of FsTree)       │
│   DirectoryScanner (live, metadata only)  │          │   drains batches (6 ms budget)            │
│   SyntheticTreeSource (deterministic)     │          │   throttled re-layout ─▶ Visualization    │
│ Model     FsTree / FsNode (ids, flags,    │          │     ClassicVisualization / District...     │
│           aggregates, FullPath)           │          │     InstanceBatch → chunked MultiMesh     │
│ Layout    TreeLayout (Fishbone | Rows)    │◀─────────│   CameraRig (eased orbit/fly/frame)       │
│           SizeScale, LayoutParams, Box    │          │   LabelLayer (≤60 bounded 2D labels)      │
│ Interaction Picker (ray vs subtree BVH)   │◀─────────│   Hud (inspector, status, legend, badge)  │
│           SelectionState (ids, history)   │          │   Inspector text, Automation, PerfRecorder│
│           AgeBands, FileCategories        │          └──────────────────────────────────────────┘
│ Shell     IShellActions: WindowsShellActions (ShellExecuteExW, Explorer) | MockShellActions        │
└───────────────────────────────────────────┘
FileScape.Fixtures: on-disk Windows edge-case fixture (owned temp dir, marker-guarded cleanup)
tests/FileScape.Core.Tests: xUnit + FluentAssertions; Windows-only tests run with Windows dotnet.exe
```

Responsibilities:

- **Filesystem model and operations (Core):** node identity, hierarchy, metadata, flags, scan states, enumeration policy (FS-*), shell actions. See `DirectoryScanner` remarks for the exact policies.
- **Spatial layout (Core):** pure functions from (tree, parameters) to boxes and connector polylines. They are deterministic, and subtrees never overlap.
- **Visualization (app):** geometry, materials, environment, colour meaning. It is replaceable per AD-02.
- **Interaction (Core + app):** selection and picking in Core; camera, input, labels and HUD in the app.

What is deliberately absent: plugin framework, services/IPC, persistence, watchers, LOD/subtree summarization, background layout (AD-04 revisit).

Key flows:
- **Scan:** `Main.StartScan(source)` → new `FsTree` + `ScanSession.Start()` → batches drained per frame → `Relayout()` (adaptive cadence) keeps the selected node fixed on screen.
- **Select:** a mouse click → `Picker.Pick` → `SelectionState.Select(id)` → inspector, highlight, beam and labels update.
- **Switch visualization:** new layout for the same tree → rebuild instances → fly to the same selected id.
- **Open:** `RequestOpen` → mock (synthetic) / confirmation (code, link, cloud) / `ShellExecuteExW` → result toast and log.
