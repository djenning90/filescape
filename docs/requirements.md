# FileScape — product intent and requirements

Status labels: **Demonstrated** (observed working, with evidence in `docs/implementation-status.md`), **Partial**, **Not started**, **Later** (deliberately out of the first prototype). IDs are stable. Code comments and tests cite them.

## Intent

FileScape makes a real Windows filesystem into an explorable 3D place. It must stay useful (navigation, readable hierarchy, true metadata, deliberate opening) while it looks and feels like somewhere. Classic FSN is the reference starting point. Cinematic worlds (district, city, forest) are the long-term direction. See `FileScape_Project_Handoff.md` §3–4 for the origin, and Appendix A for the historical FSN inventory. That inventory is reference material, not a backlog.

Principal journey for the first prototype:
choose a root → scan with visible progress → navigate the 3D hierarchy → select and inspect → deliberately open through Windows → switch visualization with the selection preserved → recover a useful view when disoriented.

## Filesystem and scanning (FS)

| ID | Requirement | Status |
|---|---|---|
| FS-01 | Metadata scanning only. No file mutations, no automatic execution, and files are never opened during a scan. | Demonstrated |
| FS-02 | Asynchronous scan on a background thread, with live progress (entries, dirs, files, links, bytes, problems, queue, elapsed). The UI stays navigable. | Demonstrated |
| FS-03 | Reparse points (junctions, symlinks, mount points) are shown as links and never traversed. This prevents cycles and volume crossings. | Demonstrated (fixture junction loop + dir/file symlinks) |
| FS-04 | Hidden and system entries are included and flagged, not silently skipped. | Demonstrated |
| FS-05 | Inaccessible and vanished entries are recorded with the OS message, and the scan continues. | Demonstrated (deny-ACE fixture; deleted-mid-scan test) |
| FS-06 | Unicode names and long paths (>260 chars) work where the APIs permit, and failures are surfaced. | Demonstrated for the scan, inspector and FullPath (372-char fixture path). Shell actions on long paths: not specifically tested |
| FS-07 | Logical size is the primary measure. Totals are labelled "so far", and hard links count once per name (possible double counting). Allocated size is labelled as not queried. | Demonstrated (labels). Hard-link double counting is not fixture-tested |
| FS-08 | Incompleteness is visible: pending, cancelled, not-traversed (cloud, limit), failed and partial directories have markers and inspector text. | Demonstrated |
| FS-09 | Cancellation is acknowledged immediately and no new work starts. An outstanding blocking OS call is reported (path + duration), distinct from application unresponsiveness. | Demonstrated (unit test with a blocked call; live cancel 0.2 ms) |
| FS-10 | A purposeful node limit (1,000,000 entries) protects memory. Reaching it is reported, and the partial tree stays usable. | Demonstrated in a unit test (limit 50). Not exercised at 1M |
| FS-11 | Deterministic synthetic data: wide/deep trees, zero-byte files, Unicode names, size outliers, simulated problem cases, always labelled SYNTHETIC. | Demonstrated |
| FS-12 | Broad roots (drive root, user profile, OneDrive, network path) need confirmation. Nothing broad is scanned by default. | Implemented; not exercised in automation |
| FS-13 | Directories marked RECALL_ON_DATA_ACCESS, **including a chosen root**, are shown but not enumerated (Microsoft documents that enumerating them can fetch remote data); the root then appears as "not listed — cloud placeholder directory". One policy function serves root and descendants; there is no override yet. FileScape does not claim metadata access is hydration-free. | Implemented; unit-tested with a simulated attribute (real placeholders need a cloud provider); not tested against a real provider |

## Visualization (VIS)

| ID | Requirement | Status |
|---|---|---|
| VIS-01 | Classic FSN-inspired view: pedestals, file blocks, parent-child wires, FSN's seven age colours. | Demonstrated |
| VIS-02 | One futuristic District interpretation with a coherent identity (towers, plazas, streets, wet ground, haze). | Demonstrated |
| VIS-03 | The size transformation is explained (log scale, cap, zero-byte tiles), and the inspector shows exact bytes. | Demonstrated |
| VIS-04 | Colour meaning is shown in a legend for each visualization. | Demonstrated |
| VIS-05 | Subtrees never overlap. | Demonstrated (tests, all arrangements) |
| VIS-06 | Spatial stability where practical: subtrees move as rigid blocks, and the selected node stays fixed on screen during re-layout. | Partial (anchoring implemented; whole subtrees can still shift during a scan) |
| VIS-07 | Bounded labels (≤ 60), prioritized and overlap-rejected. | Demonstrated |
| VIS-08 | Batching: chunked MultiMesh, with shared materials per geometry kind. No per-file scene objects. | Demonstrated |
| VIS-09 | Quality presets (High/Low), so the cinematic look does not demand maximum effects. | Demonstrated |
| VIS-10 | Every consumer of geometry (labels, picking, framing, Home, highlights) uses only nodes present in the *displayed* layout, which is deliberately older than the tree during scans. | Demonstrated (Core regression test; 224-frame stale interval in the navigation probe with commands injected, no errors) |
| VIS-11 | Bright sources and their reflections do not flash on and off while the camera moves (DJ observed this in District). Reflections are kept. | Investigation 2026-11-04: per-frame bright-pixel statistics along a fixed camera path, with effects toggled one at a time, before and after any fix. |

## Navigation and interaction (NAV)

| ID | Requirement | Status |
|---|---|---|
| NAV-01 | Inspection controls: left-drag orbit, middle-drag pan, wheel zoom, keyboard ground movement (heading-relative) plus Q/E elevation. | Demonstrated via injected and OS-level input (2026-11-03 navigation pass). Feel: awaiting DJ |
| NAV-02 | Click selects; double-click or F flies to the selection. Picking resolves the intended node, including zero-byte tiles. | Demonstrated (40/40 in each run) |
| NAV-03 | Parent (Esc) and Back (Backspace) navigation. | Demonstrated |
| NAV-04 | Home/Reset recovers a useful view (root and its first children). | Demonstrated |
| NAV-05 | Bird's-eye overview of the whole tree (B). | Demonstrated |
| NAV-06 | Switching visualization preserves the selection (by node id) and re-frames it. | Demonstrated (also after flight, via injected Tab) |
| NAV-07 | Arrow keys are aliases of WASD (Up/Down = W/S, Left/Right = A/D) in and out of flight. Pressing two aliases of one action does not double speed. Bindings are named actions (`InputBindings`) ready for remapping; there is no editor. | Demonstrated (injected: ratios 0.99–1.00; OS SendInput: arrows move, W+Up ≈ W) |
| NAV-08 | Input ownership: a focused UI control or an open dialog keeps its keys. Focus loss (Alt-Tab), dialogs, and lost button releases leave no stuck keys, drags, or mouse capture, and recovery on return is clean. | Demonstrated (injected text field + dialog; simulated focus loss; real OS Alt-Tab during flight) |
| NAV-09 | The horizon stays level (no roll). Look pitch is limited to ±85°, so the view never flips. Orbit↔fly transitions cause no jump, rotation, or snap back. Diagonal movement is normalized, and movement is frame-rate independent. | Demonstrated (injected: Δpos ≤ 0.0007 u on transitions; pitch clamps at ±85° with up.y > 0; Core tests) |
| NAV-10 | Fly mode while the right mouse button is held: mouse-look with captured cursor; W/S along the view, A/D strafe, Q/E world-vertical, Shift ×3; wheel adjusts flight speed (×1.25 per notch, honouring multi-notch and fractional wheels) within 0.5–3000 u/s; the starting speed scales with viewing distance; release or Esc ends flight. | Demonstrated (injected and OS-level) |
| NAV-11 | Discoverability: a compact first-run hint ("WASD / arrows move · Hold right mouse to look · …"), a "Help (F1)" toolbar button, Help that explains the wheel and the right/middle change, and a flight chip showing speed while flying. | Demonstrated (screenshot, injected checks) |

## Places experience (PLACE) — added 2026-11-04

Product intent (DJ, accepted direction): *"You should feel like you are in the root folder or wherever you start. You should know where you are, see the child folders, and clearly understand how to reach the next place. The world can fade into the distance, revealing what is next and hints of what follows. Seeing the whole filesystem should be possible through deliberate exploration or ascent. Atmosphere and distinct characteristics should make it feel like a place."*

Principal journey: choose a root → arrive inside its place → recognize the current folder → understand its files and child destinations → travel to a child → recognize the new place and its relationship to the parent → return → deliberately ascend or switch to an overview.

Names: **Places** is the new experience. **Classic FSN** and **District** are retained as the *whole-tree reference views* ("maps"). The design choices behind the requirements are in AD-14 and AD-15; the IDs and acceptance criteria below are the contract.

| ID | Requirement | Acceptance criterion (observable) |
|---|---|---|
| PLACE-01 | **Current folder ≠ selection.** The app holds a *current folder* (where you are) separately from the *selection* (what you inspect). Selecting a file or a child folder never changes the current folder; only deliberate travel does. | Clicking a file in a place: inspector shows the file, breadcrumb and current-folder id unchanged (injected click + state). |
| PLACE-02 | **Arrival and identity.** Entering a folder places the camera at a deterministic *arrival point* at eye level inside the place, facing its child destinations. The folder is identified by an in-world landmark carrying its name, and by a breadcrumb (root › … › current) in the HUD. | After every travel, the landmark sign text and the breadcrumb's last segment equal the folder's display name; the arrival pose is identical on repeated visits. |
| PLACE-03 | **Local composition.** Each folder is composed as a local plaza: files as inspectable structures grouped into labelled type quarters, child folders as named gates on the far side, the way back (parent gate) behind the arrival point. | Screenshot of arrival; Core tests: no two structures overlap; quarters/gates/parent gate occupy their own sectors. |
| PLACE-04 | **Every child is a destination.** Every immediate child directory has exactly one gate with its name, reachable on foot, and an entry in the HUD destination list. None is silently omitted. Directory links appear as sealed gates ("link, not followed"). | Core test over all fixtures: gate ids = child directory/link ids exactly; app check: destination list count = gate count = child count. |
| PLACE-05 | **Wide and deep cases.** Up to 24 children sit on one gate ring with vistas beyond. More children are arranged on ascending alphabetical tiers (each tier signed with its name range and count); vistas are omitted there and stated. Name signs are distance-managed so the arrival view does not show hundreds at once. A chain of single-child folders is announced on its gate ("chain of N single folders"). Empty folders, files-only folders and child-only folders each have an explicit appearance. | Core tests: 300-child folder has all gates, tiers and ranges in order, no overlaps; app: visible gate signs bounded; screenshots of wide, deep, empty. |
| PLACE-06 | **Travel and thresholds.** Walking or flying outward through a gate (below its lintel) enters that child; through the parent gate goes up. Crossing requires having been ≥ 2 u inside the place (hysteresis) and is disabled for 1 s after arrival, so standing on a boundary cannot flicker between places. Travel also by double-click on a gate, **G** (go into the selected folder), the destination list, **U** / the Up button (parent), and the breadcrumb. | Core tests for crossing/hysteresis; injected and OS-level held-W walk through a gate changes the current folder exactly once. |
| PLACE-07 | **Transition and return context.** Travel shows a short threshold transition (glide through the gate and a fade, ≈ 1 s), never an instant cut. Returning to the parent arrives in front of the gate you came from, facing into the parent, with that child selected. | Injected U after entering a child: current = parent, selected = the child, camera in front of its gate. |
| PLACE-08 | **Home and root.** Home returns to the arrival point of the current folder. **Ctrl+Home** / the Root button returns to the chosen root's arrival point. | Injected Home / Ctrl+Home: pose equals the arrival pose of the current / root folder. |
| PLACE-09 | **Progressive revelation.** The current place is shown in full; beyond each gate a *vista* previews the child (its landmark, its largest files as a skyline, beacons for its own children); further places are not drawn. Haze fades the distance. Only the local neighbourhood is built (visibility management), not the whole tree. | Instance count depends on the local neighbourhood, not the tree size (measured: same folder in a 1k and 50k tree). |
| PLACE-10 | **Honest discontinuity.** Places are separate local spaces linked by gates; avenue lengths are symbolic. Ascent (**B**) rises above the current place and shows its neighbourhood only; the whole filesystem is shown by the maps (**M** / Tab), which mark "You are here". The HUD says this; no view implies continuous geography that does not exist. | Ascent screenshot with the explanatory chip; map screenshot with the "You are here" marker on the current folder. |
| PLACE-11 | **Unknown ≠ empty.** A gate/place whose listing is pending, cancelled, not traversed, denied, cloud or failed is visibly different from an empty folder, and says which. | Fixture: denied, pending (slow synthetic scan), cancelled and empty gates all render and label differently (screenshots + state checks). |
| PLACE-12 | **Responsive during scans.** The place is recomposed from the local neighbourhood only, throttled, while the scan runs; navigation stays responsive and the whole-tree layout is not computed while in Places. | Frame-time p95/p99 during a 50k streaming scan in Places, versus the map views. |
| PLACE-13 | **Shared operations.** Inspector, true metadata, Open (deliberate, with the same confirmations), Show in Explorer, Copy path, and the navigation controls (NAV-07..11) work unchanged in Places. Size/colour mappings are explicit in the legend (height = log size; colour = file type). | Inspector check on a fixture file; Open via the same code path (directory → Explorer verified externally). |
| PLACE-14 | **Futuristic atmosphere.** One coherent vocabulary: wet glassy ground with reflections, glass file towers with lit windows, glowing gate arches with signs, a per-folder landmark whose accent colour and form are derived deterministically from the folder (so places are distinct and recognizable from their vistas), haze and a night sky. | Screenshot journey; my critique is reported as observation, not as DJ's acceptance. |
| PLACE-15 | **Reference views retained.** Classic FSN and District remain usable as whole-tree maps; switching keeps the selection; the map marks the current folder; **G** on a folder in a map enters it in Places. | Navigation probe and journey regression pass; injected M/Tab/G checks. |

**Future direction (recorded, not implemented):** themed worlds (forest, jungle, alien city) would supply their own place composer and dressing behind the same `PlaceComposition` contract (AD-14). File structures should eventually show recognizable content — a photograph's image, a document's first page, other meaningful traits — through a file-appearance extension point; scanning stays metadata-only and any preview reading would be a separate, deliberate, bounded process.

## Deliberate file actions (FILE)

| ID | Requirement | Status |
|---|---|---|
| FILE-01 | Explicit Open uses the Windows association (ShellExecuteEx default verb). | Demonstrated for a **directory** only (Explorer window observed externally, then closed). Opening a **document through its file association remains unverified** by automation; DJ's open Notepad session was not disturbed |
| FILE-02 | Nothing opens or runs from hover, selection or navigation. | Demonstrated by design and code review; no automatic call sites |
| FILE-03 | Confirmation before opening code-running files, links, or cloud placeholders. | Implemented; dialog not exercised in automation |
| FILE-04 | Show in Explorer and Copy path. | Implemented; not exercised in automation |

## Inspection (INSP) and UX

| ID | Requirement | Status |
|---|---|---|
| INSP-01 | Full path (with length) and exact logical bytes. | Demonstrated |
| INSP-02 | Created/modified/accessed (local), attributes, and flags with explanations. | Demonstrated |
| INSP-03 | Directory direct and subtree aggregates, labelled "so far" while incomplete. | Demonstrated |
| INSP-04 | Unavailable or unqueried values are labelled, never shown as zero. | Demonstrated (allocated size; missing times) |
| UX-01 | Root selection via the native folder dialog and `--root=`. | The dialog is implemented but not exercised in automation; `--root` is demonstrated |
| UX-02 | Errors leave the app usable. | Demonstrated (problem directories; failed Open message) |
| UX-06 | Live data, synthetic data and mock actions are always distinguishable: badge, inspector footer, MOCK messages. | Demonstrated |

## Later (not in this prototype)

Search, watchers/live refresh, persistent scan caches, bookmarks, split views, mutations (copy/move/rename/delete), shell context menus, multi-select, rich previews, warp mode and other historical-fidelity features, themes as data packages, sound/weather, installers, signing, marketplaces.

## Open questions for DJ

- Is the Fishbone arrangement acceptable as "FSN-inspired"? The historical Rows arrangement is retained (AD-08).
- Should single-click also *approach* (FSN behaviour), or stay select-only (current)?
- Camera feel (speeds, sensitivities, fly-speed default) is provisional until DJ tries it.
- What tree sizes matter in practice (10k vs 1M nodes)? This drives LOD and background layout work.
- Which mutation (if any) belongs in the first useful release?
- Places: does entering a gate on foot feel deliberate enough, or should entry require a confirm key? Is the arrival viewpoint comfortable? (Design choices pending DJ's hands-on judgement.)
