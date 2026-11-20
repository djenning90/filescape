# Architecture (prototype)

## Design tenets

Durable design commitments: a change that weakens one needs DJ's agreement and a decision (AD) saying why.

**The traveller's little light (AD-85).** The traveller carries a small warm lantern on a swaying antenna over its head
(`app/Scripts/Planets/AvatarRig.cs`: `LanternColor` (1.0, 0.8, 0.42), the glowing core `_lanternCore` with its halo,
and a soft `OmniLight3D`, energy 0.7 and range 2.8 m, that brightens with a launcher's wind-up and in flight; the antenna is
a spring that lags behind starts and swings on stops). DJ (2026-11-20): "the character's little light [is a particular
success]. Several people independently called out that light as adorable … It represents the warm, playful aesthetic I
want." It is the project's clearest statement of how FileScape should feel: small, warm, alive and a little funny, a
companion's light carried through a large and possibly cold space (your filesystem).

- *Why it matters:* it is what people remember and like; it makes a technical tool feel friendly; it gives every scene,
  however dark (a Hazard World, the deep sky of the overview), one warm point that is you. It reads at a glance from the
  chase camera, so it also says where you are.
- *What future work must preserve:* the lantern stays on the traveller, warm (amber, never a cold or neutral white),
  small and soft (a glow and a halo, not a beam or a spotlight), alive (it sways with the body's motion and answers what
  the traveller does: brighter winding up and flying); it is not hidden or replaced by effects, outfits or UI except
  where the camera is the traveller's own eye (the telescope's view) and it returns the moment the camera does.
- *The same care elsewhere:* new things should carry the same character — small, warm, responsive details that answer
  the player (the bell's swing, scenery that answers a twirl, the telescope's flare), rather than generic chrome. When a
  new feature has a choice between a neutral control and a warm, playful one that is just as clear, choose the warm one.
  Clarity still comes first: playfulness never hides what a key does.


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

Walking world and connected namespace (2026-11-05, AD-16..AD-20):

```
FileScape.Core                                             app/
┌───────────────────────────────────────────────────┐      ┌─────────────────────────────────────────────────┐
│ Scanning/WorldExplorer (IScanSource, on demand)    │─────▶│ Main.World: StartLive, replies, FollowLink,      │
│   coordinator thread: ids, bookkeeping, batches   │ batch│   Locations (L) / Go to path (Ctrl+L), H filter, │
│   worker tasks: listings, path probes, link       │+reply│   identity override, Retry                       │
│   resolution (several in flight; LOC-04)          │◀─────│   Visit(current) on every arrival                │
│   hub "This computer" → drives, WSL distributions │ req. │ Main.Walk: locomotion (walking / flight /        │
│ Scanning/Reparse  tag → kind (LINK-01)            │      │   landing / guided glides), look capture         │
│ Scanning/Locations discovery, normalisation, case │      │ Ui/Hud.World: Locations popup, connection chip,  │
│ Model: LinkInfo, LocationKind, Unlisted state     │      │   problem list, Hidden toggle, Retry/override    │
│ Places/PlaceTerrain  boxes, ramps, rings, boundary│      │ Places/PlaceWorld: terrain, overlook panel,      │
│ Places/Walker        kinematic first person       │─────▶│   identity dressing, link gate styles            │
│ Places/PlaceKinds    hub / location / project     │      │ Ui/LabelLayer: labels never over HUD/each other  │
│ Places/PlaceNavigator FollowLink, via-link history│      │ WalkProbe (walk, walk-perf, walk-tour)           │
└───────────────────────────────────────────────────┘      └─────────────────────────────────────────────────┘
```

- **One namespace (AD-17):** live data is one `FsTree` rooted at the hub. Location roots carry their absolute path (`LocationPath`); every other node's path is built from its location. The arrival folder's ancestors are *Unlisted* stubs until visited; listing a stub merges the child it already knew. The navigator's *root* is the arrival folder, not node 0; Up continues to the physical parent and to the hub.
- **Threading:** the explorer's coordinator thread is the only place ids are assigned, so batches stay in id order and the main thread stays the only writer of `FsTree` (AD-04). Listings, path probes and link resolutions are worker tasks; replies (`ExplorerReply`) and link updates travel in the same batches.
- **Links (AD-18):** links are leaves; following one ensures its target *path* in the same tree, so a loop or a second route reaches an existing node. Totals never cross links. History remembers the link used (`PlaceChange.ViaLink`); Back returns in front of it.
- **Walking (AD-16):** `PlaceComposer` produces `PlaceTerrain` with the place; `Walker` integrates input at 120 Hz sub-steps; the app copies the eye pose into `CameraRig.SetPose`. Flight and landing live in `Main.Walk`; glides use the rig's orbit easing and hand back to walking or flight when they settle.
- **Identity (AD-20):** `PlaceKinds` classifies from names only; the composer uses it for the project core, `PlaceWorld` for dressing; it never affects listing state or the world style.

Exploration pass (2026-11-06, AD-24..27):

```
FileScape.Core                                             app/
┌───────────────────────────────────────────────────┐      ┌─────────────────────────────────────────────────┐
│ Places/WorldLayout  known folders → islands on a   │─────▶│ Main.Aerial: when (flying ∧ > 14 m) show the     │
│   world plane; PlaceFrame (centre, north)          │      │   aerial world; layout throttled; Reveal nearby; │
│ Scanning/WorldExplorer.Reveal (list from the air,  │◀─────│   pick islands; G/T travel converts frames       │
│   never a location root)                           │      │ Places/AerialWorld  islands, roads, link arcs    │
│ Places/Walker  jump arc (hang, hop), head clearance│      │ Ui/SignLayer  screen-space names (gates, places) │
│                                                   │      │ Visual/FileIcons  SVG → atlas (badges, HUD)      │
└───────────────────────────────────────────────────┘      │ Ui/Hud  key, Destinations drawer, inspector card │
                                                           └─────────────────────────────────────────────────┘
```

- **Two views of the same world:** walking keeps the local, progressive place (vistas are symbolic previews); flight above 14 m shows the explored world as islands. Both read the same `FsTree`; the current place is drawn once, full-detail, at its island (floating origin, `docs/units.md`).
- **Names:** gates and places are named by `SignLayer` (screen space); `PlaceWorld` keeps in-world plates, the plaque and category signs; `LabelLayer` names files near you.
- **File appearance:** `FileIcons.For(node)` decides a node's silhouette — the extension point for a future preview provider (thumbnails, first pages) fed by a separate, bounded reader, never by the scan.

Units, coordinates and quantity types: `docs/units.md` (Places: 1 world unit = 1 m; maps: abstract layout units; `FileScape.Core.Units`).

Responsibilities:

- **Filesystem model and operations (Core):** node identity, hierarchy, metadata, flags, scan states, enumeration policy (FS-*), shell actions. See `DirectoryScanner` remarks for the exact policies.
- **Spatial layout (Core):** pure functions from (tree, parameters) to boxes and connector polylines. They are deterministic, and subtrees never overlap.
- **Visualization (app):** geometry, materials, environment, colour meaning. It is replaceable per AD-02.
- **Interaction (Core + app):** selection and picking in Core; camera, input, labels and HUD in the app.

- **Places (Core + app):** *what is where* in a folder's place is decided by `PlaceComposer` (pure, deterministic, local: folder, children, grandchildren, parent's largest files). `PlaceComposition` is the contract a future themed world (forest, alien city) would produce with its own composer; rendering (`PlaceWorld`) and atmosphere are app-side. Current folder (`PlaceNavigator`) and selection (`SelectionState`) are separate concepts.
- **File appearance (future extension point):** today a file is a tower whose height/colour come from `SizeScale.Place` and `FileCategories`. Content previews (a photo's image, a document's first page) would plug in as a per-file appearance provider consulted by `PlaceWorld`, fed by a separate, deliberate, bounded preview reader — never by the metadata scan (FS-01).

What is deliberately absent: plugin framework, services/IPC, a WSL-side helper (AD-18), persistence, watchers, LOD/subtree summarization, background layout (AD-04 revisit).

Key flows:
- **Scan:** `Main.StartScan(source)` → new `FsTree` + `ScanSession.Start()` → batches drained per frame → `Relayout()` (adaptive cadence) keeps the selected node fixed on screen.
- **Select:** a mouse click → `Picker.Pick` → `SelectionState.Select(id)` → inspector, highlight, beam and labels update.
- **Switch visualization:** new layout for the same tree → rebuild instances → fly to the same selected id.
- **Live start:** `--root` / Open folder… → `WorldExplorer(path)` → hub + locations → arrival path ensured (reply ticket 0) → navigator root → Visit → listing → place composed.
- **Link travel:** gate crossing / G / double-click on a link → `FollowLink` → worker resolves the target → path ensured in the tree → reply → `PlaceNavigator.FollowLink` → transition; Backspace returns through the link.
- **Places travel:** gate crossing / G / Destinations / U / breadcrumb → `PlaceNavigator` changes the current folder → transition: glide out through the gate + fade (0.42 s) → `PlaceComposer.Compose(new folder)` + `PlaceWorld.Build` → glide in to the arrival (or return) pose (0.62 s). During scans the place is recomposed only when `PlaceComposer.Signature` (the local neighbourhood) changes, at most every 0.35 s; the whole-tree layout is computed only when a map is shown.
- **Open:** `RequestOpen` → mock (synthetic) / confirmation (code, link, cloud) / `ShellExecuteExW` → result toast and log.
- **Close (AD-28):** window X → `NotificationWMCloseRequest` → `BeginShutdown`: `_closing` stops `_Process`/`_Input`, `ScanSession.Dispose` cancels the explorer (its coordinator stops before the next unit; worker listings are abandoned on background threads, never awaited) → `[exit]` line → Godot quits. `ConsoleLaunch` reports whether stdout is an interactive console that did not wait (FileScape.exe from a terminal) or the console wrapper.
- **This computer (AD-30):** `LocationRequest.Parse` (Core) maps This PC and its aliases to the hub; `Main.GoToHub` travels there, or starts the connected world at the hub (`StartLive(null)`). The chooser (`Hud.ShowLocations`) always offers it first; Browse… is the native picker.
- **Listing ahead (AD-31):** `WorldExplorer` queues: urgent → prefetch (destinations, the way back) → context (the destinations' folders, per-visit budget, idle only, one at a time) → background. `ListAhead.Refusal` (Core) bounds what may be listed ahead; refusals become the gate's note. `DirScanState.Listing` → `ListingState.Listing` ("listing now…").
- **Prominence and projects (AD-32, AD-33):** `Prominence.For` (Core: pins, known folders passed in by the app, Linux home paths, `PlaceKinds` evidence) → cached per tree version in `Main.Projects` → sign mark and boost, `PlaceWorld.SetProminentGates`, Destinations rows, card line. `Readme.Read`/`ToBbcode` and `EditorLaunch.Options` (Core) feed the card's README viewer (`Hud.Project`) and editor buttons; launching goes through `IShellActions.Start` (ShellExecuteEx) after `Confirm`.
- **Compact places and the horizon (AD-34, AD-35):** `PlaceParams.Compact` (default) / `Spacious` (`--layout=spacious`). `Horizon.Compose` (Core) turns the composition's vistas and the tree into silhouette, haze and veil marks; `PlaceWorld` draws them in the vista slots (hidden from the air).
- **Planetoid experiment (retired 2026-11-20):** the isolated prototype that tested curved bodies (PLANET-02) was removed once the planets superseded it; `docs/planetoid-feasibility.md` keeps its findings.
- **Verification (AD-29):** `tools/verify.sh` (via `fs.sh check`) runs tiers; `ProbeTier` (app) implements `--tier=functional` and off-thread screenshot saving.

The planetary world on the shared session (2026-11-11, AD-39..43):

```
Main (owns the session: FsTree, ScanSession, WorldExplorer, PlaceNavigator, SelectionState, IShellActions, Hud, state file)
  │  IWorldSession (app/Scripts/Planets/WorldSession.cs): Tree, Navigator, Selection, Explorer, Hud, Signs,
  │    TravelInto / TravelUp / GoBack / Select / OpenSelection / ShowReadme / Summary / ProminenceOf / IsVisible
  ├── Places experience (Main.Places, PlaceWorld)         └── PlanetWorld (app/Scripts/Planets/)
  │                                                            controller: activation, nav changes → launches,
  │                                                            camera (local up, capped roll, offset tracking),
  │                                                            travel (wind-up, arc, nested scale, floating origin,
  │                                                            warp, hop), interaction (focus, E, previews, the list)
  │                                                            PlanetVisual (one world: ground, props, signs, belt)
  │                                                            PlanetArt (procedural meshes, palettes, shaders)
  │                                                            AvatarRig (the traveller) · SkyAudio (streams SkySynth)

What varies between the walking worlds          Places                         Planets
  surface and gravity                           PlaceTerrain + Walker          PlanetSurface + SphereWalker
  (shared)                                      JumpModel (jump, coyote, buffer, short hop, one airborne boost)
  camera frame                                  CameraRig (world Y up)         chase camera, local up, roll ≤ 90°/s
  placement and travel                          PlaceComposer, gates           PlanetComposer, launches, warps
  visual and audio treatment                    PlaceWorld, PlaceAtmosphere    PlanetVisual, PlanetArt, SkySynth

FileScape.Core/Planets: PlanetShape (height field shared by mesh and walker), SurfaceObstacle, PlanetSurface,
  SphereWalker, PlanetComposer → PlanetComposition (theme, shape, surface, arrival, landmark, overlook + steps,
  launchers, sky bodies + belt, items + piles in groves, decorations, signs, paths, signature)
FileScape.Core/Movement: JumpModel (shared by Walker and SphereWalker)
FileScape.Core/Audio: SkySynth (generative music and tempo-locked effects, offline-testable)
FileScape.Core/Samples: SampleSky (the Starter Sky: real files for the first impression)
```

Threads: planet ground vertex data is computed on worker threads (`PlanetArt.PlanetMeshArrays`, pure maths) and
prefetched for every world in the sky; only the mesh upload and the props are built on the main thread (3–17 ms).
The overview (T) discovers while open (PLANET-76, AD-71): `OverviewDiscovery` (Core) picks drawn worlds known only by name, breadth-first, focus first, within its limits; `WorldExplorer.Discover` lists them (metadata only); `PlanetWorld.Discovery` relayouts at most every 1.5 s, pinning what is drawn, and glides into it.
Picture thumbnails are decoded on workers. A listing that changes the planet recomposes asynchronously (the new world is
swapped in when its ground is ready).
