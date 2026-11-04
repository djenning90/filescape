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

Places (2026-11-04, AD-14/AD-15):

```
FileScape.Core/Places                                   app/
┌──────────────────────────────────────────────┐        ┌───────────────────────────────────────────────┐
│ PlaceComposer   (tree, folder) → PlaceComposition│──────▶│ Main.Places (controller: experience switching,│
│   landmark, files in type quarters, gates      │        │   recompose on signature change, transitions, │
│   (ring ≤ 24 / tiers), parent gate, vistas,    │        │   thresholds, HUD sync, map "You are here")    │
│   avenues, arrival/return/approach poses       │        │ Places/PlaceWorld (MultiMesh towers, gates,   │
│ PlaceNavigator  current folder, history, root  │◀───────│   portals, beacons, routes; pooled Label3D     │
│   (separate from SelectionState, PLACE-01)     │        │   signs with distance management)              │
│ ThresholdTracker gate crossing + hysteresis    │        │ Places/PlaceAtmosphere (environment)           │
│ PlacePicker     ray → node id in this place    │        │ Ui/Hud.Places (breadcrumb, Destinations, fade) │
│ Listing         Open/Empty vs unknown states   │        │ PlacesProbe (--automate=places|-scan|-tour)    │
└──────────────────────────────────────────────┘        └───────────────────────────────────────────────┘
```

Responsibilities:

- **Filesystem model and operations (Core):** node identity, hierarchy, metadata, flags, scan states, enumeration policy (FS-*), shell actions. See `DirectoryScanner` remarks for the exact policies.
- **Spatial layout (Core):** pure functions from (tree, parameters) to boxes and connector polylines. They are deterministic, and subtrees never overlap.
- **Visualization (app):** geometry, materials, environment, colour meaning. It is replaceable per AD-02.
- **Interaction (Core + app):** selection and picking in Core; camera, input, labels and HUD in the app.

- **Places (Core + app):** *what is where* in a folder's place is decided by `PlaceComposer` (pure, deterministic, local: folder, children, grandchildren, parent's largest files). `PlaceComposition` is the contract a future themed world (forest, alien city) would produce with its own composer; rendering (`PlaceWorld`) and atmosphere are app-side. Current folder (`PlaceNavigator`) and selection (`SelectionState`) are separate concepts.
- **File appearance (future extension point):** today a file is a tower whose height/colour come from `SizeScale.Place` and `FileCategories`. Content previews (a photo's image, a document's first page) would plug in as a per-file appearance provider consulted by `PlaceWorld`, fed by a separate, deliberate, bounded preview reader — never by the metadata scan (FS-01).

What is deliberately absent: plugin framework, services/IPC, persistence, watchers, LOD/subtree summarization, background layout (AD-04 revisit).

Key flows:
- **Scan:** `Main.StartScan(source)` → new `FsTree` + `ScanSession.Start()` → batches drained per frame → `Relayout()` (adaptive cadence) keeps the selected node fixed on screen.
- **Select:** a mouse click → `Picker.Pick` → `SelectionState.Select(id)` → inspector, highlight, beam and labels update.
- **Switch visualization:** new layout for the same tree → rebuild instances → fly to the same selected id.
- **Places travel:** gate crossing / G / Destinations / U / breadcrumb → `PlaceNavigator` changes the current folder → transition: glide out through the gate + fade (0.42 s) → `PlaceComposer.Compose(new folder)` + `PlaceWorld.Build` → glide in to the arrival (or return) pose (0.62 s). During scans the place is recomposed only when `PlaceComposer.Signature` (the local neighbourhood) changes, at most every 0.35 s; the whole-tree layout is computed only when a map is shown.
- **Open:** `RequestOpen` → mock (synthetic) / confirmation (code, link, cloud) / `ShellExecuteExW` → result toast and log.
