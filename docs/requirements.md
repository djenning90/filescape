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
| FS-03 | **Background scanning** never traverses reparse points (junctions, symlinks, mount points), which rules out cycles and volume crossings during a scan. *Revised 2026-11-05:* links are followed only by deliberate navigation (LINK-01..08). | Demonstrated for scanning (fixture junction loop + dir/file symlinks); deliberate following: see LINK-* |
| FS-04 | Hidden and system entries are included and flagged, not silently skipped. | Demonstrated |
| FS-05 | Inaccessible and vanished entries are recorded with the OS message, and the scan continues. | Demonstrated (deny-ACE fixture; deleted-mid-scan test) |
| FS-06 | Unicode names and long paths (>260 chars) work where the APIs permit, and failures are surfaced. | Demonstrated for the scan, inspector and FullPath (372-char fixture path). Shell actions on long paths: not specifically tested |
| FS-07 | Logical size is the primary measure. Totals are labelled "so far", and hard links count once per name (possible double counting). Allocated size is labelled as not queried. | Demonstrated (labels). Hard-link double counting is not fixture-tested |
| FS-08 | Incompleteness is visible: pending, cancelled, not-traversed (cloud, limit), failed and partial directories have markers and inspector text. | Demonstrated |
| FS-09 | Cancellation is acknowledged immediately and no new work starts. An outstanding blocking OS call is reported (path + duration), distinct from application unresponsiveness. | Demonstrated (unit test with a blocked call; live cancel 0.2 ms) |
| FS-10 | A purposeful node limit (1,000,000 entries) protects memory. Reaching it is reported, and the partial tree stays usable. | Demonstrated (2026-11-05 correction): a 40-entry budget cuts a 300-entry folder to a partial, explained listing; listings stop enumerating at the remaining budget; requested destinations stay reachable through a small reserve; the status line names the limit and offers "Start fresh here". The limit bounds the tree; transient buffering is bounded by concurrent listings × remaining budget. Not exercised at 1M |
| FS-11 | Deterministic synthetic data: wide/deep trees, zero-byte files, Unicode names, size outliers, simulated problem cases, always labelled SYNTHETIC. | Demonstrated |
| FS-12 | Broad roots (drive root, user profile, OneDrive, network path) are never scanned wholesale. *Revised 2026-11-05:* instead of a confirmation, broad places are listed on demand only (LOC-02). | Superseded by LOC-02 (demonstrated) |
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
| VIS-11 | Bright sources and their reflections do not flash on and off while the camera moves (DJ observed this in District). Reflections are kept. | Investigation 2026-11-04: per-frame bright-pixel statistics along a fixed camera path, with effects toggled one at a time, before and after any fix.. **Status:** root cause found and fixed (NaN at the selection beam top cap spread by glow, reproduced A/B); sun-specular puddle flashes mitigated (measured). DJ to confirm |

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

| ID | Requirement | Acceptance criterion (observable) | Status (2026-11-04) |
|---|---|---|---|
| PLACE-01 | **Current folder ≠ selection.** The app holds a *current folder* (where you are) separately from the *selection* (what you inspect). Selecting a file or a child folder never changes the current folder; only deliberate travel does. | Clicking a file in a place: inspector shows the file, breadcrumb and current-folder id unchanged (injected click + state). | Demonstrated (injected click; OS click on Destinations) |
| PLACE-02 | **Arrival and identity.** Entering a folder places the camera at a deterministic *arrival point* at eye level inside the place, facing its child destinations. The folder is identified by an in-world landmark carrying its name, and by a breadcrumb (root › … › current) in the HUD. | After every travel, the landmark sign text and the breadcrumb's last segment equal the folder's display name; the arrival pose is identical on repeated visits. | Demonstrated (journey checks after every travel) |
| PLACE-03 | **Local composition.** Each folder is composed as a local plaza: files as inspectable structures grouped into labelled type quarters, child folders as named gates on the far side, the way back (parent gate) behind the arrival point. | Screenshot of arrival; Core tests: no two structures overlap; quarters/gates/parent gate occupy their own sectors. | Demonstrated (Core tests; screenshots) |
| PLACE-04 | **Every child is a destination.** Every immediate child directory has exactly one gate with its name, reachable on foot, and an entry in the HUD destination list. None is silently omitted. Directory links appear as sealed gates ("link, not followed"). | Core test over all fixtures: gate ids = child directory/link ids exactly; app check: destination list count = gate count = child count. | Demonstrated (Core tests incl. 300 children; app check per place) |
| PLACE-05 | **Wide and deep cases.** Up to 24 children sit on one gate ring with vistas beyond. More children are arranged on ascending alphabetical tiers (each tier signed with its name range and count); vistas are omitted there and stated. Name signs are distance-managed so the arrival view does not show hundreds at once. A chain of single-child folders is announced on its gate ("chain of N single folders"). Empty folders, files-only folders and child-only folders each have an explicit appearance. | Core tests: 300-child folder has all gates, tiers and ranges in order, no overlaps; app: visible gate signs bounded; screenshots of wide, deep, empty. | Demonstrated (240-child fixture: 8 tiers, ≤ 70 signs; 25-level chain; empty) |
| PLACE-06 | **Travel and thresholds.** Walking or flying outward through a gate (below its lintel) enters that child; through the parent gate goes up. Crossing requires having been ≥ 2 u inside the place (hysteresis) and is disabled for 1 s after arrival, so standing on a boundary cannot flicker between places. Travel also by double-click on a gate, **G** (go into the selected folder), the destination list, **U** / the Up button (parent), and the breadcrumb. | Core tests for crossing/hysteresis; injected and OS-level held-W walk through a gate changes the current folder exactly once. | Demonstrated (Core tests; injected and OS-level W through a gate; dithering and backing-up checks) |
| PLACE-07 | **Transition and return context.** Travel shows a short threshold transition (glide through the gate and a fade, ≈ 1 s), never an instant cut. Returning to the parent arrives in front of the gate you came from, facing into the parent, with that child selected. | Injected U after entering a child: current = parent, selected = the child, camera in front of its gate. | Demonstrated (injected and OS-level U) |
| PLACE-08 | **Home and root.** Home returns to the arrival point of the current folder. **Ctrl+Home** / the Root button returns to the chosen root's arrival point. | Injected Home / Ctrl+Home: pose equals the arrival pose of the current / root folder. | Demonstrated (injected Home / Ctrl+Home; OS-level Ctrl+Home) |
| PLACE-09 | **Progressive revelation.** The current place is shown in full; beyond each gate a *vista* previews the child (its landmark, its largest files as a skyline, beacons for its own children); further places are not drawn. Haze fades the distance. Only the local neighbourhood is built (visibility management), not the whole tree. | Instance count depends on the local neighbourhood, not the tree size (measured: same folder in a 1k and 50k tree). | Demonstrated (vistas; local composition only, 0.4–1.0 ms compose) |
| PLACE-10 | **Honest discontinuity.** Places are separate local spaces linked by gates; avenue lengths are symbolic. Ascent (**B**) rises above the current place and shows its neighbourhood only; the whole filesystem is shown by the maps (**M** / Tab), which mark "You are here". The HUD says this; no view implies continuous geography that does not exist. | Ascent screenshot with the explanatory chip; map screenshot with the "You are here" marker on the current folder. | Demonstrated (ascent chip, map marker). Whether it *reads* as honest: awaiting DJ |
| PLACE-11 | **Unknown ≠ empty.** A gate/place whose listing is pending, cancelled, not traversed, denied, cloud or failed is visibly different from an empty folder, and says which. | Fixture: denied, pending (slow synthetic scan), cancelled and empty gates all render and label differently (screenshots + state checks). | Demonstrated (pending during scan, 25 not-scanned gates after cancel, denied, empty) |
| PLACE-12 | **Responsive during scans.** The place is recomposed from the local neighbourhood only, throttled, while the scan runs; navigation stays responsive and the whole-tree layout is not computed while in Places. | Frame-time p95/p99 during a 50k streaming scan in Places, versus the map views. | Demonstrated (50k streaming scan: p99 12 ms in Places vs 34–39 ms in the maps) |
| PLACE-13 | **Shared operations.** Inspector, true metadata, Open (deliberate, with the same confirmations), Show in Explorer, Copy path, and the navigation controls (NAV-07..11) work unchanged in Places. Size/colour mappings are explicit in the legend (height = log size; colour = file type). | Inspector check on a fixture file; Open via the same code path (directory → Explorer verified externally). | Demonstrated for inspector/metadata and Open of a directory (OS-level Enter → Explorer) |
| PLACE-14 | **Futuristic atmosphere.** One coherent vocabulary: wet glassy ground with reflections, glass file towers with lit windows, glowing gate arches with signs, a per-folder landmark whose accent colour and form are derived deterministically from the folder (so places are distinct and recognizable from their vistas), haze and a night sky. | Screenshot journey; my critique is reported as observation, not as DJ's acceptance. | Implemented; my visual critique in the status document; DJ's judgement pending |
| PLACE-15 | **Reference views retained.** Classic FSN and District remain usable as whole-tree maps; switching keeps the selection; the map marks the current folder; **G** on a folder in a map enters it in Places. | Navigation probe and journey regression pass; injected M/Tab/G checks. | Demonstrated; since 2026-11-05 the whole-tree maps of a chosen folder need the Bounded tree scan mode (AD-22): Places journey 51/0 in that mode, map lays out 1,713/1,713 nodes |

**Future direction (recorded, not implemented):** themed worlds (forest, jungle, alien city) would supply their own place composer and dressing behind the same `PlaceComposition` contract (AD-14). File structures should eventually show recognizable content — a photograph's image, a document's first page, other meaningful traits — through a file-appearance extension point; scanning stays metadata-only and any preview reading would be a separate, deliberate, bounded process.

## Walking world and connected filesystem (2026-11-05)

DJ's direction after playing Places: the place concept is achieved and the visuals are compelling — preserve them. Next: *a world you inhabit on foot, connected to the real places on your computer.* Walking is primary; flight is an explicit secondary mode. FileScape should move towards reaching anywhere Explorer reaches, starting with ordinary Windows directories, directory links and WSL filesystems.

Principal journey: arrive on foot → understand the folder and its destinations → walk, look around, jump, inspect → enter a child → follow a directory link to another location → understand the destination and return → visit a Windows volume or WSL location → deliberately enter flight → return safely to walking. The *current folder*, *selection*, *navigation history* and *arrival root* stay distinct concepts.

Decisions: AD-16 (walking), AD-17 (one connected namespace), AD-18 (links), AD-19 (visibility), AD-20 (place identity).

### Walking and flight (WALK)

| ID | Requirement | Acceptance criterion (observable) | Status |
|---|---|---|---|
| WALK-01 | **Grounded first person is the default in Places.** One world scale (1 u = 1 m); eye 1.75 m above the supporting surface; gravity and ground contact. | Core: standing on the plaza, the terrace and a tier gives eye − support = 1.75 ± 0.01. App: after arrival, eye height above the support equals 1.75. || Demonstrated (Core tests; injected: eye 1.75 m above terrace and plaza; OS: arrives on foot) |
| WALK-02 | **Gaze-independent heading.** Forward/back/strafe move horizontally along the heading whatever the vertical gaze; looking straight down never pushes into the ground or stops movement. | Core: horizontal speed at look pitch −85°, 0°, +85° equal within 1 %; vertical position unchanged while walking on the flat. Injected: same. || Demonstrated (Core; injected: 4.24 m/s looking down at −84° vs 4.16 m/s level, feet stay on the ground) |
| WALK-03 | **Controls.** Up/Down arrows walk forward/back; Left/Right arrows turn (Up+Left walks a curve); W/S forward/back, A/D strafe; hold right mouse to look and steer (horizontal = heading, vertical = gaze); Space jump; Shift run. Arrow keys previously strafed: the change is documented in Help and the README. Focused UI controls keep their keys (NAV-08). | Injected and OS-level: each binding produces its motion; Up+Left curves (heading changes and position advances); a focused text field keeps the arrows. || Demonstrated (injected and OS SendInput: arrows walk/turn/curve, A strafes, Space, right-hold look; a focused field keeps the keys). Feel: awaiting DJ |
| WALK-04 | **Movement quality.** Frame-rate independent; diagonals normalised; responsive starts (≥ 90 % of speed within 0.25 s) and stops (≤ 0.2 s); level horizon (no roll); no camera bob, landing shake or forced rotation. | Core: speeds at 30/60/144 Hz equal within 2 %; diagonal = straight within 1 %; start/stop times measured; injected: up.y > 0 and roll 0 throughout. || Demonstrated (Core: 30/60/144 Hz, diagonals, start ≤ 0.25 s, stop ≤ 0.25 s; injected W+D 4.15 vs W 4.16 m/s). No bob or shake by construction; feel awaiting DJ |
| WALK-05 | **Terrain.** Small steps (≤ 0.45 m) are climbed without jumping; walkable slopes (ramps, ≤ 35°) are walked up and down without bouncing; solid structures (towers, landmark, gate pillars, platform walls) block without snagging (the walker slides along them); thin decorations (signs, beacons, floor lines) never block. A boundary keeps walkers inside the place (no falling off the world). | Core tests: stairs climbed, ramp ascended at speed, walking into a tower at 45° slides along it, corridor between towers passable, boundary holds. || Demonstrated (Core: stairs, ramp, sliding, alleys, boundary; injected: terrace stairs and overlook ramp) |
| WALK-06 | **Flight is explicit.** A labelled **Flight (T)** control toggles flight; entering rises continuously and a chip says "Flying — T to land". Flight keeps the existing fly controls (look, WASD along view, E/Space up, Q down, wheel speed, Shift). **B** rises above the place in flight. | Injected T: mode = Flying, eye rises continuously (no jump > 0.5 m/frame), chip visible. || Demonstrated (injected and OS: T rises 3.5 m over ≈ 1.3 s, chip and pressed button; E climbs) |
| WALK-07 | **Safe landing.** Leaving flight descends continuously onto a valid surface below; where there is none (outside the walkable place, e.g. above a vista) the user is guided to the arrival point and told why. Never left falling indefinitely or inside geometry. | Injected: T above the plaza lands (support under feet, not inside a solid); T above a vista relocates to the arrival with a message; a forced fall below the world recovers to the arrival. || Demonstrated (injected: lands below on a clear surface, never a tower roof; above a vista → arrival with a message; Core: lost walker flagged; OS: T lands) |
| WALK-08 | **Home and Root.** Home = the current folder's safe arrival point (on foot); Ctrl+Home / Root = the arrival root's arrival point. | Injected Home/Ctrl+Home poses. || Demonstrated (injected Home on foot; Ctrl+Home) |
| WALK-09 | **Input recovery** (NAV-08 carried over): Escape releases look capture and steps back from the selection; Alt-Tab, dialogs and lost releases leave nothing held or captured; typing in a field never walks. | OS-level Alt-Tab while walking/looking; injected text field. || Demonstrated (OS Alt-Tab while walking; injected: Locations open blocks walking, the path field keeps keys, Esc closes) |

### Jumping (JUMP)

| ID | Requirement | Acceptance criterion | Status |
|---|---|---|---|
| JUMP-01 | A modest jump (apex ≈ 0.8 m) that keeps running momentum and allows some air steering; forgiving timing (coyote time after leaving a ledge, a short jump buffer before landing, a small ledge tolerance). | Core: apex 0.75–0.85 m; running jump carries > 4 m; jump pressed 0.1 s after walking off a ledge still jumps; pressed 0.1 s before landing jumps on landing. || Demonstrated (Core: apex, momentum, coyote, buffer; injected: apex ≈ 0.8 m, lands) |
| JUMP-02 | **A small route in every place:** an **overlook** platform with folder information (summary, orientation), reachable by a **walking ramp** and by an optional **jump shortcut** of low blocks. | Core: overlook reachable by walking only (ramp path) and by jumps; screenshots. || Demonstrated (Core; injected: the ramp, and three jumps onto the overlook; screenshots) |
| JUMP-03 | **No platforming required.** Every destination and file action stays reachable on foot without jumping (and by Destinations/G/double-click). Jumping never opens, executes or changes anything. Terrain relates to real structure where it is used (child-tier terraces, the overlook's summary); decoration never imitates access boundaries or errors. | Core: every gate reachable by a walking path from the arrival (no jump); code review: no action is bound to jumping. || Demonstrated (Core grid reachability of every gate and the overlook without jumping; no action is bound to jumping) |

### Connected filesystem and links (LINK)

| ID | Requirement | Acceptance criterion | Status |
|---|---|---|---|
| LINK-01 | **Kinds identified by the reparse tag**, not every reparse point treated as a symlink: junction, directory/file symbolic link, mounted volume, Linux symbolic link (WSL), app execution alias, cloud placeholder (a directory/file, not a link), Linux special files, and other/unknown tags (kept honestly, with the tag). | Windows test on fixtures: junction → Junction, `mklink /D` → SymbolicLink, OneDrive-style cloud tag → directory with cloud flag; WSL test: LX symlink → LinuxSymlink. || Demonstrated for junctions (Windows tests, live fixture), Linux symlinks (live WSL), cloud/app-alias/special tags (classifier tests). Windows directory symlinks and mounted volumes: classifier-tested only (creating them needs Developer Mode/admin) |
| LINK-02 | **Deliberate following.** Walking through a supported directory link's gate (or G/double-click/Destinations) resolves it on demand and travels to the destination; background scanning never expands links. | Injected walk through a junction gate: current = target folder; scan log shows no traversal through links. || Demonstrated (injected: walk through a junction gate; OS: G; Core and Windows tests) |
| LINK-03 | **Connection shown, return dependable.** On arrival via a link the HUD shows the destination's real location and the connection used ("via link *X* in *Y*"); Back (Backspace / the "Back through link" button) returns to the link's place, in front of its gate. **Up** goes to the destination's *physical* parent (navigation history ≠ parenthood; entry path ≠ resolved path). | Injected: after link travel, breadcrumb = target's physical path; connection chip names the link; Back returns to the link's folder with the link selected; Up goes to the physical parent. || Demonstrated (injected and OS: connection text, physical breadcrumb, Backspace returns in front of the link with it selected; Core: Up is the physical parent) |
| LINK-04 | **Outside the arrival root.** Links may lead anywhere reachable; the arrival root is an arrival point, not the edge of the world. Up from the arrival root continues to its parent. | Injected: a junction to a folder outside the fixture root travels there; Up from the root reaches its parent. || Demonstrated (junction to a folder outside the root; U from the arrival root reaches its parent, listed on arrival) |
| LINK-05 | **Cycles and repeated destinations.** A link back to an ancestor, or two links to one target, reach the *same* node: no new scan, no duplicate sub-world. The HUD says "leads back to *ancestor*" / "also reached via *other link*". | Core: loop junction target id = ancestor id; two links → same id; node count unchanged by repeated travel. || Demonstrated (loop link says it leads back here; two junctions reach one place; node count unchanged) |
| LINK-06 | **Distinct failure states.** Broken (target missing), inaccessible (access denied), unavailable (volume/share/distribution not reachable), unsupported (Linux link through Windows, unknown provider) each have their own marker and a concise explanation; retry where useful. A deliberately unexpanded link is a *connection*, not an error. | Demonstrated (2026-11-05 correction: probing instead of Exists): broken, denied (deny ACE), unavailable (drive removed after the junction was made) and unsupported (Linux) links distinct, with Retry recovering after the cause is fixed (Windows tests); denied/unavailable are not yet in the app journey |
| LINK-07 | **Totals across links.** Folder totals never include link targets (no double counting); the inspector states that links are not included and that totals cover what has been listed so far. | Core: subtree bytes unchanged after resolving and listing a link's target. || Demonstrated (POSIX test: arrival totals unchanged after listing a link target) |
| LINK-09 | **Follow Linux symbolic links in WSL locations** (added 2026-11-05 as an explicit gap). | Following a Linux link to a folder in the fixture arrives at its target. | **Not met.** Identified only; the targets could not be obtained through the Windows file APIs on `\\wsl.localhost` (see AD-18 for exactly what was tested). Proposed bounded investigation: implementation-status, KD-17 |
| LINK-08 | **Paths and actions tied to the intended item.** Displayed paths and Open/Show/Copy act on the selected item (the link itself or the destination folder, as labelled), with Unicode and case preserved. | Core: FullPath of a node reached via a link is its physical path; Unicode fixture name round-trips. || Partial: physical FullPath of link destinations tested; Unicode folder present in the fixture and journey; Open/Copy on it not automated |

### Locations: Windows volumes and WSL (LOC)

| ID | Requirement | Acceptance criterion | Status |
|---|---|---|---|
| LOC-01 | **A location hub ("This computer")** lists Windows drives and WSL distributions and can be visited as a place; a **Go to path** field (Ctrl+L) accepts pasted Windows paths, `\\wsl.localhost\…`/`\\wsl$\…` paths and Linux paths (mapped to the default distribution). No new launch is needed. | Injected: Ctrl+L, paste a WSL path, Enter → current = that folder. Hub shows C:\ and the registered distributions. || Demonstrated (injected: Ctrl+L with a pasted \\wsl.localhost path; L → hub; normalisation tests) |
| LOC-02 | **Progressive, on-demand listing.** Locations are listed when entered (plus their immediate children for gate states); drives, profiles and distributions are never scanned wholesale. Background traversal is limited to the arrival root's subtree (and is cancellable). | Entering C:\ lists C:\ and its children only (node count bounded); scan log. || Demonstrated (tests: only the arrival subtree is traversed; broad places are on demand). C:\ was deliberately not entered in evidence runs (privacy) |
| LOC-03 | **WSL through the supported UNC route** (`\\wsl.localhost\<distro>`), with Linux case distinctions preserved (`README` and `readme` are two entries) and Linux symlinks identified. | Partial: case pairs and identification of Linux links demonstrated live; following Linux links is unmet (LINK-09) |
| LOC-04 | **Slow or unavailable locations never freeze the UI.** Listing runs off the main thread with several listings in flight; a slow/stopped distribution or missing drive shows "waiting on Windows…" then unavailable, with Retry. | Frame times while a listing blocks (test hook); a not-ready drive shows unavailable. || Demonstrated (test: a blocked listing does not block another; app: a not-ready drive shows unavailable, worst frame 26 ms). A stopped distribution: Unknown (not exercised: it would start that distribution) |
| LOC-05 | **Ordinary permissions; no implicit execution.** No elevation or permission changes. Linux executables/scripts are browsed, never run implicitly; Open on a WSL file uses the Windows association and says so; unsupported actions are described. | Code review; inspector text for a WSL script. || Implemented by design (ordinary permissions, no implicit execution). Opening a WSL file through its Windows association: not exercised |

### Visibility, names and status (VISI)

| ID | Requirement | Acceptance criterion | Status |
|---|---|---|---|
| VISI-01 | **Labels do not overlap** each other or the HUD; priority: current place, destinations, selection/hover, nearby relevant files. | Demonstrated (overlay labels and, since the correction, in-world signs: none overlapping, none under the HUD, at five places, at UI scale 1.0 and 1.25) |
| VISI-02 | **Concise status markers** instead of long sentences on every object: a symbol and a short word in the world, detail in the inspector. A link is shown as a connection, not an error. | Screenshot; sign texts ≤ 2 short lines. || Demonstrated (screenshots: short words on gates; links read as connections) |
| VISI-03 | **Problem summary.** Access failures and unavailable destinations are aggregated in a discoverable summary (count + list), with details preserved. | Injected: summary count = problem nodes in the place; list entries select the node. || Demonstrated (injected: "⚠ 2 here need attention" lists the broken link and the denied folder) |
| VISI-04 | **Hidden/system visibility control** (H and a toolbar toggle), off by default, with a clear indication ("N hidden"). Dot-prefixed names are a separate category and are shown by default. Changing visibility never changes selection, current place, totals or history; totals always include hidden entries and say so. | Injected toggle: gates/towers count changes by the hidden count; selection/current/history unchanged. || Demonstrated (injected and OS: H toggles; 2 hidden by default, dot-names shown; selection, place, history and totals unchanged) |

### Place identity (IDENT)

| ID | Requirement | Acceptance criterion | Status |
|---|---|---|---|
| IDENT-01 | Place *identity* (what a folder is), world *style* (futuristic) and *availability/access state* are separate concepts. | Code structure: identity classifier separate from palette and from ListingState. || Demonstrated (PlaceKinds separate from palette and ListingState) |
| IDENT-02 | **First identities:** location hub, drive/distribution root, ordinary folder, software project. Each has a clear spatial distinction within the futuristic style. | Screenshots of each. || Demonstrated (screenshots: hub, drive/WSL root, project). Visual quality: awaiting DJ |
| IDENT-03 | **Explainable, overridable project recognition** from strong local evidence (manifests such as `*.sln`, `*.csproj`, `package.json`, `Cargo.toml`, `pyproject.toml`, `go.mod`, plus a README or `.git`), with a generic fallback; the inspector lists the evidence; an override returns the folder to "ordinary". No file contents are read. | Core tests: positive and negative cases; override. || Demonstrated in tests (evidence, negatives, override); the "Not a project" button exists but was not driven by automation |
| IDENT-04 | A project place surfaces its README and manifests (a "project core" by the arrival); no build/deploy actions. | Screenshot; Core composition test. || Demonstrated (injected: README and manifests form the core; screenshot) |

**Future direction (recorded):** photo collections, document collections and previews, financial collections (only from strong evidence), temporary workspaces, more world styles, long jumps/special movement, Explorer's virtual namespaces (This PC libraries, network neighbourhood, shell folders).

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
- Walking: speeds, turn rate, jump height and the arrow-key change (turn instead of strafe) are provisional until DJ tries them.
- Should the maps also use the new arrow behaviour? (Currently: maps keep arrows as strafe aliases, NAV-07.)
