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
| FS-13 | Directories marked RECALL_ON_DATA_ACCESS are not enumerated (Microsoft documents that enumerating them can fetch remote data). Even so, FileScape does not claim metadata access is hydration-free. | Implemented; not tested against a real cloud provider |

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

## Navigation and interaction (NAV)

| ID | Requirement | Status |
|---|---|---|
| NAV-01 | Orbit, pan, zoom, and WASD/QE movement, all eased. | Demonstrated (scripted; hand feel not reviewed by DJ) |
| NAV-02 | Click selects; double-click or F flies to the selection. Picking resolves the intended node, including zero-byte tiles. | Demonstrated (40/40 in each run) |
| NAV-03 | Parent (Esc) and Back (Backspace) navigation. | Demonstrated |
| NAV-04 | Home/Reset recovers a useful view (root and its first children). | Demonstrated |
| NAV-05 | Bird's-eye overview of the whole tree (B). | Demonstrated |
| NAV-06 | Switching visualization preserves the selection (by node id) and re-frames it. | Demonstrated |

## Deliberate file actions (FILE)

| ID | Requirement | Status |
|---|---|---|
| FILE-01 | Explicit Open uses the Windows association (ShellExecuteEx default verb). | Demonstrated for a directory (Explorer window observed externally, then closed). Not auto-verified for a document type (DJ's open Notepad session was not disturbed) |
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
- What tree sizes matter in practice (10k vs 1M nodes)? This drives LOD and background layout work.
- Which mutation (if any) belongs in the first useful release?
