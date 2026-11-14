# Architecture decisions

Each entry: problem → decision → rationale → alternatives → revisit when. Dates are 2026-11-03 unless noted.

## AD-01 Godot 4.7 .NET as the provisional prototype vehicle

- **Problem:** We need a real engine for an immersive look, plus C#, on this laptop (Radeon 890M iGPU), without large toolchains.
- **Decision:** Godot 4.7.2 .NET (portable editor, Forward+ renderer; D3D12 by default on Windows per AD-11), with all filesystem, layout, picking and selection logic in an engine-independent `FileScape.Core` (net8.0).
- **Rationale:** C# end to end with no bridge. It needed only a ~110 MB portable editor and the existing .NET SDK. Forward+ provides volumetric fog, SSR, SSAO and glow, and these rendered on the 890M. Builds and runs directly from the WSL path (AD-09).
- **Alternatives:** Unreal C++/Blueprints is the strongest alternative, with a higher cinematic ceiling. It needs VS 2026 C++, a large install and royalty terms that have not been authoritatively checked. Stride is MIT and C#, but has thinner docs. Desktop .NET viewports were rejected for the cinematic goal.
- **Revisit when:** the District look cannot reach DJ's bar with Godot's renderer; the Vulkan instability (KD-01) persists on both Vulkan and D3D12; or scale needs exceed what MultiMesh + CPU layout can do. Core would carry over to any engine; only `app/` is Godot-specific.

## AD-02 Visualization vs theme

- **Decision:** A *visualization* (`app/Scripts/Visual/*`) owns a layout parameter set, a geometry treatment, a colour meaning, and an environment. A *theme* would be a set of parameters inside a visualization. Themes are not implemented yet.
- **Rationale:** Classic and District already differ in layout parameters, connector style, colour meaning (age vs type) and height multiplier, but share identity, selection, picking, labels and the inspector.
- **Revisit when:** a third visualization needs a fundamentally different layout (for example a radial forest), or themes become user data.

## AD-03 Identity and selection independent of scene objects

- **Decision:** A node is a dense id into `FsTree`, assigned breadth first and stable for the scan's lifetime. `SelectionState` (selected, hovered, history) stores ids only. Render instances do not map back to nodes; picking is geometric (AD-05).
- **Consequence:** A visualization switch rebuilds geometry and re-frames the same id. Bookmarks across rescans would need path plus file ID (later).

## AD-04 Scan threading and data flow

- **Decision:** `ScanSession` runs an `IScanSource` on one background thread. The source emits immutable `ScanBatch`es (new nodes in id order, plus directory state updates) into a concurrent queue. The Godot main thread drains batches within a 6 ms budget per frame and is the only writer of `FsTree`. Re-layout during a scan is throttled adaptively, to roughly 1/8 of wall time.
- **Rationale:** The Godot scene tree is not thread-safe. A single writer avoids locks in the model.
- **Known cost:** layout plus instance rebuild runs on the main thread: 20–40 ms at 50k nodes during a scan (measured). **Revisit:** move layout to a worker over an immutable snapshot once trees above about 100k matter.

## AD-05 Picking and framing in Core, against layout geometry

- **Decision:** CPU ray vs AABB picking, using directory subtree bounds as a BVH (`Picker`). The camera frames layout boxes.
- **Rationale:** Engine-independent, testable, and identical across visualizations. MultiMesh has no per-instance picking.
- **Evidence:** 40/40 on-screen unoccluded targets resolved to the intended node in every scripted run, zero-byte tiles included.

## AD-06 Batching

- **Decision:** One `MultiMeshInstance3D` per 4096 instances, per geometry kind (pedestal, block, wire/street, marker), filled in depth-first order so each chunk is spatially compact for frustum culling. Shared shader materials read per-instance colour and custom data (world scale, style bits). Re-layout reuses MultiMesh nodes and replaces buffers.
- **Revisit when:** draw calls or culling become limiting (LOD and subtree summarization are not implemented).

## AD-07 Size transformation: log scale, absolute

- **Decision:** height = (0.12 + 0.42·log10(1+bytes)) × multiplier, capped (Classic: ×1, cap 6 u; District: base 0.25, ×2.4, cap 14 u). Zero-byte files get thin tiles. Footprints are constant. Directory pedestal height in Classic grows slightly with direct file bytes.
- **Rationale:** FSN's exaggerated mode (size^0.5) is normalised to the largest file, so heights would change while a scan discovers files. An absolute log scale is stable during scans and readable from bytes to TB. The inspector always shows exact bytes, the mapping, and whether a cap applied.
- **Alternative:** FSN square-root mode is an option for a "historical" preset later.

## AD-08 Fishbone arrangement (default) with historical rows retained

- **Problem:** FSN's arrangement (one row per hierarchy level, children spread along X) made the 12k-node sample about 20× wider than deep. The overview became a sliver on the horizon (observed in screenshots `journey-1`).
- **Decision:** Default `Arrangement.Fishbone`. Child subtrees line both sides of a spine running back from the parent, placed on the shorter side each time. A single child continues the spine. Many children split across k parallel spines joined by a cross street, with k chosen so the block is roughly square. Connectors are orthogonal and run down the spines and gaps. `Arrangement.Rows` remains in Core.
- **Evidence:** tests show no overlaps in any arrangement, fishbone aspect < 6 on the sample (rows much wider), and a 300-child directory aspect < 3. Screenshots show a readable landscape and city grid on synthetic and real NuGet-cache data.
- **Revisit when:** DJ prefers stricter FSN fidelity; the `Rows` arrangement can be exposed as an option.

## AD-09 Windows workflow from the WSL repository (no staging copy)

- **Decision:** The repository stays authoritative in WSL. Windows `dotnet.exe` and the Godot editor work directly on `\\wsl.localhost\Ubuntu-24.04\home\david\src\filescape`. Windows builds write to `obj.win/` and `bin.win/` (Directory.Build.props), so WSL and Windows build state never collide. No staging directory or sync is needed, so there are no divergent-edit rules to enforce.
- **Evidence:** Godot import, C# build, run, export and the Windows test suite all worked from the UNC path.
- **Quirks:** see `docs/windows-workflow.md` (MSBuild apphost copy denial; build servers holding pipes open).

## AD-10 Test stack

- **Decision:** xUnit 2.9.3 + FluentAssertions **7.2.0** (Apache-2.0). FluentAssertions 8.x changed to the Xceed licence (paid for commercial use), so we stay on 7.x unless DJ decides otherwise. Windows-only tests use `[WindowsFact]`, which skips with a stated reason on non-Windows hosts.

## AD-11 D3D12 as the default Windows rendering driver

- **Problem:** KD-01. Vulkan lost the device intermittently on the Radeon 890M (driver 32.0.22024.19001): 3 of 4 sustained uncapped perf runs, plus 1 during a journey.
- **Decision:** `rendering/rendering_device/driver.windows="d3d12"` in `app/project.godot`. Vulkan stays available via `--rendering-driver vulkan`.
- **Evidence:** D3D12 had 0 crashes in 4 perf runs plus all final journey, scanperf and live runs. D3D12 means are about 10–30% higher than Vulkan, but its tails are tighter (see the status tables).
- **Revisit when:** a newer AMD driver or Godot release is installed (re-run `stability.sh`-style comparisons), or D3D12 shows its own instability during dogfooding.

## AD-12 Navigation model: orbit for inspection, hold-right fly for travel

- **Problem:** DJ (not a gamer) tried the arrow keys, which were unbound, and found no discoverable way to travel. The baseline OS-input check also found real defects: an orbit drag interrupted by Alt-Tab stayed "stuck"; diagonals were about 41% faster; movement keys were polled even while dialogs were open.
- **Decision:** Keep orbit as the resting mode (left-drag orbit, middle-drag pan, wheel zoom, ground keyboard movement). While the right mouse button is held, the camera flies: captured mouse-look, WASD/arrows along the view, Q/E world-vertical, and the wheel sets speed. Right-drag no longer pans; pan moved to the middle button (stated in Help). Both modes share one pose (target, yaw, look pitch, distance), so transitions are exact. After flight the orbit pivot moves onto whatever is under the screen centre without moving the camera. Inputs are named actions (`InputBindings`). Presses count only if no UI control consumed them, and releases are tracked before the GUI.
- **Alternatives:** keep right-drag pan and put fly on a toggle key (less discoverable, and a toggle can be forgotten while the mouse is captured); fly-only navigation (worse for inspection and selection); a game-style mouse-captured default (hostile to clicking items).
- **Revisit when:** DJ's hands-on feedback; a trackpad-only user (holding right is awkward there, so a toggle key may be needed); or a keybinding editor is wanted.

## AD-13 Windows tooling through direct WSL interop

- **Problem:** `tools/fs.sh` built PowerShell command strings and relied on `dotnet` being on the PowerShell PATH, which was false in the reviewer's environment. Its exit codes came from filtered text, and quoting broke on unusual paths.
- **Decision:** Execute `dotnet.exe` and Godot directly via interop with argv arrays. `dotnet.exe` is discovered via `%ProgramFiles%`, then `where`, then the `FILESCAPE_DOTNET` override. The real exit codes are returned. Build servers are disabled via `WSLENV`-forwarded variables (this also fixed a ~10 min export stall). The export is verified by checking for the exe and its .NET assemblies.
- **Evidence:** a missing dotnet gives exit 127; failing tests give exit 1; arguments with spaces and an apostrophe arrive intact (`docs/evidence/2026-11-03-navigation/args.log`); export takes 9 s.

## AD-14 Places: separate local places linked by gates, with vistas (2026-11-04)

- **Problem:** DJ wants to feel *inside* a folder, know where they are, see every child destination and how to reach it, with what is next fading into the distance. The whole-tree maps (Classic, District) present the tree at equal prominence; fogging them would not create local places. A single continuous constant-scale geography of an arbitrary tree either overlaps or becomes enormous (fanout grows exponentially with depth, while a place should keep a human scale).
- **Decision:** Each folder is composed, on demand, as its own local place by an engine-independent `PlaceComposer` (Core): a circular plaza with a central landmark (name sign), files as glass towers grouped into type quarters on the near (south) half, child folders as gates on the far (north) arc, and the parent gate behind the arrival point. Beyond each gate an avenue leads to a *vista*: a simplified preview of that child (its landmark, its largest files as a skyline, beacons for its children). Crossing a gate's threshold, or an explicit travel command, *re-composes* the world around the child; the transition (glide + fade) makes the boundary explicit. The folder's accent colour and landmark form derive from a hash of its name, so a vista and the place it previews look alike.
- **Honesty:** places are discontinuous. Within one place, positions are real (that is where the gate is). Avenue length and vista distance are symbolic. Ascent shows only the local neighbourhood; the whole filesystem is the maps' job, and they mark "You are here". This is stated in the HUD and Help.
- **Wide folders:** ≤ 24 child gates on one ring with vistas; more are arranged on ascending tiers in alphabetical order, each tier signed with its range and count; vistas are omitted for tiers (too many to preview legibly) and gate signs are distance-managed. A destination list in the HUD lists every child.
- **Alternatives considered:** (1) one continuous balloon/radial layout of the whole tree with distance fading — honest geography, but child places must shrink with depth (no consistent human scale) or overlap; (2) fog/LOD over the existing Fishbone layout — rejected by the brief; (3) pure portals with no vistas — loses "revealing what is next".
- **Responsibility boundary for future worlds:** `PlaceComposition` (positions of landmark, files, gates, vistas, arrival) is the contract. A forest or alien-city theme would supply another composer and its own dressing; selection, navigation (`PlaceNavigator`), thresholds, inspector and shell actions stay shared. No plugin framework now.
- **Revisit when:** DJ finds the discontinuity confusing; trees with >1000 children per folder are common (tiers become large); or a continuous geography becomes desirable for a specific theme.

## AD-15 Three experiences, one model (2026-11-04)

- **Decision:** The app has three experiences over the same tree, selection and shell actions: **Places** (default, AD-14) and the reference whole-tree maps **Classic FSN** and **District**. Tab cycles them; **M** toggles Places ↔ the last map. The *current folder* (`PlaceNavigator`) persists across switches and is marked in the maps; **G** on a folder in a map enters it in Places. While in Places the whole-tree layout is not computed (the local composition is), which keeps scanning responsive; switching to a map lays out on demand.
- **Alternative:** fork a second application for Places — rejected (duplicated identity, metadata, inspector, shell, input).

## AD-16 Walking: a kinematic walker in Core over terrain composed with the place (2026-11-05)

- **Problem:** DJ wants to inhabit places on foot, with jumping, slopes and steps, and flight as an explicit secondary mode. Places had no ground or collision (KD-10).
- **Decision:** `PlaceComposer` also produces a `PlaceTerrain` (support surfaces: plaza, arrival terrace with stairs, overlook with ramp and jump blocks, tier rings; solids: towers, landmark, gate pillars; a boundary circle). A `Walker` in Core (capsule as a vertical circle, eye 1.75 m, step 0.45 m, gravity, jump with coyote time/buffer/ledge tolerance, slide-along collision) integrates input at a fixed 120 Hz sub-step. The app feeds input and copies the eye pose to the camera. Flight is a second locomotion mode in the same controller; landing uses the terrain's support query and relocates to the arrival when there is no valid surface.
- **Rationale:** deterministic, engine-independent and unit-testable (slopes, steps, snagging, diagonal speed, frame-rate independence) in the same way as the rest of Core; simple shapes are enough because the composition is ours; no physics colliders to keep in sync with MultiMesh geometry.
- **Alternatives:** Godot `CharacterBody3D` with colliders generated per place (more general, but harder to test headlessly and duplicates geometry); keeping free camera movement with height following only (no jumping or obstacles).
- **Revisit when:** places need arbitrary meshes (themes), moving platforms, or physics interactions.

## AD-17 One connected namespace for live data, listed on demand (2026-11-05)

- **Problem:** links can lead outside the chosen root, and DJ wants to reach drives and WSL distributions without relaunching. One `FsTree` per scan root cannot represent "outside"; a separate tree per destination would duplicate identities and break the shared selection/history; grafting link targets under links duplicates sub-worlds and loops.
- **Decision:** live data is a single `FsTree` whose root is a **"This computer" hub**; its children are *location roots* (drives, WSL distributions as `\\wsl.localhost\<distro>`, and other UNC/volume roots when reached). A chosen folder is ensured as a path under its location: its ancestors exist as *unlisted* stubs (known name, contents not yet listed) and it becomes the navigator's **arrival root**. A `WorldExplorer` scan source runs a coordinator thread that assigns ids and emits batches (preserving the single-writer model, AD-04) and runs listings on worker tasks (several in flight, so one slow distribution blocks nothing else). Priorities: the current place, then its children (gate states and vistas), then background traversal of the arrival root's subtree only. Listing a stub merges with the children it already has. Name matching uses the location's rule: case-insensitive for Windows volumes, case-sensitive (ordinal) for WSL.
- **Rationale:** one identity space keeps selection, history, maps and inspector unchanged; cycles and repeated destinations are naturally the same node; nothing broad is ever scanned wholesale.
- **Alternatives:** per-region trees with cross-tree references; re-rooting the tree when going above the root; lazy-only listing (maps would show almost nothing).
- **Consequences:** the maps show the *explored* world (the hub, the ancestor chain, what has been listed) — labelled as such. Totals of ancestors cover listed content only ("so far").
- **Revisit when:** persistent caches or watchers arrive; trees exceed the 1M-node limit through exploration; Explorer's virtual namespaces are added.

## AD-18 Link policy (2026-11-05)

- **Decision:** reparse points are classified by tag (junction, symbolic link, mounted volume, Linux symlink, app alias, cloud placeholder, Linux special file, other). Link nodes remain leaves; background traversal never follows them (FS-03). Following is deliberate: resolving a link ensures its target path in the namespace (stubs + on-demand listing) and records the target node id on the link. Totals never cross links. A link whose target is an ancestor, or a second link to a known target, resolves to the existing node. Linux symlinks in WSL locations are identified but not followed: FileScape could not obtain their targets through the route it uses (tested on 2026-11-05 through one access route only — Windows file APIs (.NET and Win32) on `\\wsl.localhost\Ubuntu-24.04\…` (WSL 2, the 9P redirector) on DJ's laptop: `FindFirstFileEx` reports reparse tag `0xA000001D` (IO_REPARSE_TAG_LX_SYMLINK) and a size equal to the target text's length; `FileSystemInfo.LinkTarget` returns null; `FSCTL_GET_REPARSE_POINT` on a handle opened with `FILE_FLAG_OPEN_REPARSE_POINT` fails with ERROR_INVALID_FUNCTION (1); `ReadFile` on that handle fails with ERROR_INVALID_HANDLE (6); opening the path to follow it fails with ERROR_PATH_NOT_FOUND (3) for relative, absolute, `/mnt/c` and dangling targets alike; listing through a directory link fails (DirectoryNotFound). **Not tested:** the `\\wsl$` alias, extended attributes (`NtQueryEaFile`), `GetFileInformationByHandleEx` classes, WSL 1, other distributions or Windows builds, and any route that runs inside the distribution). They are shown as *Linux link · can't follow* and following them remains an unmet requirement (LINK-09).
- **Alternatives:** expand links during scanning with a visited set (still expands far beyond the root and costs scan time); a WSL-side helper (`wsl.exe readlink`) — an execution bridge, deferred until needed.
- **Revisit when:** a supported Windows API exposes LX symlink targets, or DJ wants an explicit WSL helper.

## AD-19 Visibility defaults (2026-11-05)

- **Decision:** in Places, entries with the Windows **hidden or system** attribute are hidden by default (Explorer's default), with a toggle (H, toolbar) and an "N hidden" indication. Dot-prefixed names are a separate, *shown by default* category (they are common, important project files). Visibility filters composition only: totals, selection, history and the tree are unchanged; the inspector says totals include hidden entries. The maps keep showing everything (reference views).
- **Revisit when:** DJ wants per-location rules or a separate dot-file toggle in the HUD.

## AD-20 Place identity separate from style and state (2026-11-05)

- **Decision:** `PlaceKind` (hub, location root, project, ordinary) is decided by an explainable classifier from names only (no contents): a project needs a manifest (`*.sln`, `*.csproj`, `*.fsproj`, `package.json`, `Cargo.toml`, `pyproject.toml`, `go.mod`, `pom.xml`, `build.gradle(.kts)`, `CMakeLists.txt`, `project.godot`) **and** a README or `.git`; the evidence is listed in the inspector; the user can override to ordinary (per session). Identity changes composition (the project core by the arrival) and dressing; the futuristic *style* is unchanged; *state* (listing/access) stays in `ListingState`.
- **Alternatives:** content sniffing (rejected: privacy, and FS-01); many presets (rejected: shallow).
- **Revisit when:** photos/documents identities are added (they would need bounded previews, KD-15).

## AD-21 Delivering a build safely (2026-11-05, correction)

- **Problem:** exporting straight into the installed build deleted the files of a build DJ was running (morning of 2026-11-05); a reviewer then could not find the reported artifact. Delivery needs to be safe and verifiable.
- **Decision:** `tools/fs.sh export` resolves and checks the build base (refuses an unresolved or odd `LOCALAPPDATA`), exports into a new `staging-<UTC stamp>-<pid>` directory that is never reused or deleted by the export, verifies required files and counts, writes `build-info.txt` (commit, uncommitted paths, SHA-256 of the exe and the app/core assemblies), smoke-launches the staged exe (`--automate=smoke`), determines running FileScape processes and **fails closed** if it cannot, refuses to replace a running build, and promotes by renaming (`build` → `previous-<stamp>`, staging → `build`). `tools/fs.sh builds` lists every build directory with completeness and identity; `prune-builds` deletes only non-running staging/previous directories and keeps the newest previous build. The app logs and shows its build identifier (Help).
- **Alternatives:** copying over the installed build (unsafe while running); versioned installs per commit (more machinery than one user needs).
- **Revisit when:** there is an installer, or more than one person installs builds.

## AD-22 Two named modes over one app (2026-11-05, correction)

- **Problem:** the connected world (AD-17) made the maps show only the explored world; DJ asked to keep the previous whole-tree visualization as a working reference.
- **Decision:** two explicitly named modes: **Connected world (explore on demand)** — the default (WorldExplorer) — and **Bounded tree scan (whole-tree reference)** — one chosen folder scanned completely with the original policy (DirectoryScanner: links shown, never followed; broad places confirmed first), so Classic FSN and District show that whole tree and Places works over it. Both are started from Locations (Go / Scan as tree / Scan this folder as a tree) or the command line (`--scan=tree`). No code is duplicated: the modes differ only in the scan source.
- **Revisit when:** the explored-world maps become good enough to replace the reference, or a cached whole-tree index exists.

## AD-23 Movement, upward routes, gate identification and summaries after DJ's feedback (2026-11-05, evening)

- **Problem:** DJ's first hands-on session: mouse look felt reversed on both axes; walking and running were too slow and travel between folders long; portal names vanished up close; going up repeatedly meant turning round the landmark; summaries lacked folder counts and did not say what totals meant.
- **Decisions:**
  - *Look direction* is a per-axis user preference (Core `Preferences.LookDelta`), applied only at the Places mouse-look input; the camera conventions (docs/units.md) and arrow keys are untouched. Default both reversed for this iteration (DJ's stated feel), revisable in one place.
  - *Speed and distance together:* 8 / 16 m/s (2× minus a little, because the layout change also shortened routes) and the file quarters split either side of the avenue (the avenue had pushed a middle wedge's files outward row by row). Transition 0.75 s; held keys survive place changes. Rejected: rescaling the world (would change every size and the eye-height feel); only raising speed (the 43 m arrival distance was the larger cause).
  - *Upward routes are verified by simulation*, not geometry: `PlaceRoutes.ContinueUp` tries spots from the exited child's gate towards the landmark and keeps the first from which the real walker, walking and running straight ahead, crosses the ▲ gate touching nothing. Cost ≈ 8–14 ms once per Up, during the fade. Rejected: a geometric line test (would miss stairs and step-up rules).
  - *Gate identity up close:* a fixed eye-level plate per gate plus a screen chip for the approached/hovered gate, rather than more or larger overhead signs.
  - *Summaries* from incremental aggregates (`SubtreeUnsettledDirs`, link and hidden counts) with explicit completeness; never a scan to fill a label.
  - *Natural name order* in Places only; the maps keep the tree's order.
- **Found and fixed on the way:** a walker touching a tall solid counted it as support and was snapped onto its top (seen at 30 m/s against a pillar, and in a running jump into a tower).
- **Revisit when:** DJ has played this build (KD-20/KD-22): speeds, the default look direction, the stand-off of upward arrivals, and whether plates or the chip carry identification better.

## AD-24 Familiar file icons from original SVG, rasterized at start (2026-11-06)

- **Problem:** DJ found the colour key unintuitive; a file's type should be recognisable without memorising a palette.
- **Decision:** a small set of original type silhouettes (`Visual/FileIcons`), written as SVG paths in code and rasterized by Godot's built-in SVG rasterizer (`Image.LoadSvgFromString`) into one atlas at start-up. No third-party artwork, no import pipeline, no new dependency. In the world they are badges on the plaza-facing side of each tower (fading out by 46 m so distant towers stay clean) and above category signs; in the HUD they lead signs, Destinations rows and the inspector card. Colour stays as atmosphere. `FileIcons.For(node)` is the single place a node's appearance is decided — where a bounded preview provider (thumbnails, first pages, KD-15) would plug in.
- **Alternatives:** emoji glyphs (inconsistent across fonts, not restrained); licensed icon sets (licence review, new assets); per-extension icons (too many shapes for a glance).
- **Revisit when:** previews are added, or DJ wants more specific icons (spreadsheets vs documents, photos vs drawings).

## AD-25 Destination names in screen space, shortened rather than hidden (2026-11-06)

- **Problem:** overhead Label3D gate signs were placed by priority and hidden whenever they collided with another sign or a HUD panel, so most gates were anonymous until approached (DJ: "too many destination names are hidden"). Earlier work had favoured complete names and collision suppression.
- **Decision:** a screen-space `SignLayer` names every gate in view (and every place from the air): focused first, then nearest; names shortened with a middle ellipsis by distance; detail lines for the nearest four; a colliding sign tries higher, lower and sideways slots with a leader line before being dropped; never over the HUD or another sign; at most 40. The overhead 3D gate signs were removed; eye-level plates stay for close range. A shortened visible name beats an invisible complete one; the full name is in the chip, the card and Destinations.
- **Alternatives:** larger or more staggered 3D signs (still collide, perspective-dependent size); show all names (unreadable in wide places).
- **Revisit when:** DJ finds signs too busy (a density setting), or VR/curved worlds need signs in 3D again.

## AD-26 The aerial world: a world layout, place frames and bounded discovery (2026-11-06)

- **Problem:** flight showed only the current place; DJ wants flight to explore the larger filesystem world, without silently scanning drives or faking a skyline.
- **Decision:**
  - *Layout:* `WorldLayout` places every known folder on a world plane as an island. Children lie ahead of their place (its north, away from its parent) over 160° in natural gate order; each subtree is summarised by a rectangle in its own frame (across and ahead — descendants are always ahead, so only the island itself is behind), and children are packed by the angle their rectangles need; a wide place's empty folders share wedges, near to far. The world's centre is the tree's root (This computer, or the scanned root). Deterministic; a discovery re-arranges only the children of the discovered folder's ancestors; sizes rounded in 1.4× steps.
  - *Frames:* every place has a `PlaceFrame` (world centre and north). The aerial world is drawn in the current place's local frame (a floating origin), so the place you are in never moves and stays full-detail exactly on its island. Travel from the air converts the camera between frames.
  - *Discovery:* only what is known is drawn; unlisted folders are veiled "not listed yet" islands. Flying near unlisted places inside the arrival folder or below a visited place lists them (`WorldExplorer.Reveal`, ≤ 4/s, never a location root); everything else waits until you go there. Bounded tree mode shows the whole scanned tree.
  - *Travel:* G or double-click on an island flies there (arriving above it); T over an island lands in it; history and identity as for any place change. Vistas (symbolic) remain the walking view; above 14 m they give way to the islands.
- **Alternatives tried and rejected:** a disc per subtree centred on its child (a chain of six single folders reached 25 km; B framed from 19.5 km); equal angular slots per child (stable but pushed big subtrees kilometres out); aligning islands exactly with gate directions (infeasible without overlaps beyond one level).
- **Revisit when:** places become planetoids (frames become 3D, `docs/planetoid-feasibility.md`), the explored world exceeds 4,000 places regularly (aggregate distant regions), or DJ wants islands to match their gates' directions.

## AD-27 Flight controls: level movement, explicit vertical keys, walking-only levelling (2026-11-06)

- **Problem:** DJ: automatic levelling made flight frustrating ("I want to look down from above"); vertical flight "feels wrong" (no cause given). Observation (recorded, not confirmed as the cause): W moved along the gaze, so looking down to see the place made W dive and looking up made it climb; the only vertical keys were E/Space and Q.
- **Decision:** levelling after a mouse look applies on foot only; in flight the chosen pitch stays. Flight W/S/A/D move level along the heading whatever the gaze; E or Space climb, Q or C descend (C added); speed scales with height above the ground (1×–30×); T rises 16 m and settles the gaze at −35° once; landing levels the gaze. A Settings option restores "W flies where you look". The flight chip lists the controls.
- **Revisit when:** DJ has flown it — if "wrong" persists, the next suspects are vertical speed (0.8 × flight speed) and the take-off rise.

## AD-28 Closing: an orderly shutdown and a synchronous console launcher (2026-11-06)

- **Problem:** DJ: after closing FileScape with the window's X, "the launching command does not return to the OS prompt until Ctrl-C".
- **Findings (demonstrated):**
  - Every route, closed in each state below, returned within 0.2 s (process) or 1.2 s (WSL wrapper):
    - routes: the installed exe from PowerShell and from WSL, `tools/fs.sh run` (Godot's console wrapper), and the Godot GUI binary;
    - states: idle, listing a WSL folder, WSL `/proc` and `/sys`, flight, the native folder picker open, a listing stalled for 120 s, and after Reveal in Explorer.
  - `FileScape.exe` is a GUI-subsystem program. From PowerShell the next command ran 0.01 s after launch, and Godot then printed its log into the same console after the prompt. Closing the window therefore left no new prompt, which looks exactly like a command that never returned. This is the **likely cause (provisional)**; DJ's exact route is not confirmed.
- **Decision:**
  - *Orderly shutdown:* on close, FileScape stops input and per-frame work and cancels scanning. Listings in flight are abandoned on background threads and never awaited. It prints `[exit] … exit code 0, shutdown 1 ms`.
  - *Synchronous launcher:* `FileScape.console.exe` (Godot's console wrapper, now exported in release) waits for the window, relays the log and returns the exit code (PowerShell returned 0.8 s after close, `$LASTEXITCODE` 0).
  - *Console notice:* when `FileScape.exe` itself writes into an interactive console that did not wait, it says so at start and at exit.
  - *Child processes:* editors are started with `ShellExecuteEx`, which does not inherit FileScape's console or pipes.
  - *Kept as it is:* no launcher is made to detach.
- **Revisit when:** DJ names the launch route that hung, if it differs from these.

## AD-29 Verification tiers (2026-11-06)

- **Problem:** the functional suite felt close to an hour.
- **Measured before:**

  | Run | Time |
  |---|---|
  | Previous sequence on one build, without real OS input or recording | 570 s |
  | Previous evidence pass, including real input and the recording | ≈ 14–17 min |

  - Per journey: walk 92 s × 2 scales, explore 50 s × 2, places 70 s × 2, navigation 42 s, walk-perf 30 s, planetoids 33 s, tests 19 s.
  - Fixed waits are 13 s per walking journey, so they are not the cost. The cost is journeys repeated at both UI scales, measurement segments and screenshot encoding on the main thread, plus repeated full passes and exports during iteration.
- **Decision:** `tools/fs.sh check fast | smoke | full | release` (`tools/verify.sh`):
  - The functional tier keeps every assertion and leaves out measurement-only segments, logging each as `SKIP perf …`.
  - Screenshots are encoded on worker threads.
  - Only the journeys whose HUD geometry depends on scale repeat at 1.25.
  - Performance, real OS input and the recording are release-only.
  - `fs.sh stage` exports and verifies without installing.
  - Journeys run one at a time, because they share the screen, focus and the timing checks.
- **Revisit when:** a tier exceeds its budget, or a check moves between tiers.

## AD-30 This PC is the hub: an explicit location request (2026-11-06)

- **Problem:** choosing This PC in the native folder picker shows "You can't open this location using this program". This PC is a shell location with no filesystem path.
- **Decision:**
  - `LocationRequest` maps This PC, This computer, Computer, My Computer, its CLSID and `shell:` forms, and `hub` to the hub; it never invents a path.
  - Open… (Ctrl+O, L) opens the in-app chooser with ⌂ This computer first; Browse… opens the native picker for folders.
  - Go to path and `--root=hub` accept the same names.
- **Revisit when:** a native picker can return shell locations safely.

## AD-31 Listing ahead, bounded (2026-11-06)

- **Decision:** priorities are, in order:
  1. a requested destination;
  2. the current place;
  3. its destinations and the way back (a parent known only as a stub);
  4. further context — the destinations' own folders — up to 48 per visited place, one at a time, only when nothing else is queued or in flight;
  5. background traversal.
- **Never listed ahead:**
  - Linux `/proc`, `/sys`, `/dev`, `/run`, `/mnt`, `/media`, `/snap` and `/lost+found` (each has its reason in the gate note; it is listed one level when entered);
  - network shares, as context or background;
  - the hub's locations;
  - cloud placeholders (as before).
- **States:** gates show *queued* and *listing now…* separately; unlisted and failed states keep their wording and Retry.
- **Revisit when:** context should follow the gaze or recent travel.

## AD-32 Explainable prominence (2026-11-06)

- **Decision:** destinations stand out only for a stated reason, from what is already known:
  - pins (P, saved in preferences);
  - Windows known folders as the OS reports them;
  - Linux homes by their standard paths;
  - project evidence from names, or a folder whose listed children include projects.
- **How it shows:** a mark and colour on the sign (named first when space is short), a light above the gate, the mark on Destinations rows, and the reason on the card. Order and access never change. "Not a project" also removes the project mark; nothing is listed to find candidates.
- **Revisit when:** DJ wants recent places or user-defined categories.

## AD-33 Project actions: README as inert text, editors by platform, always asked (2026-11-06)

- **README:** read only on request (R or the card), at most 256 KB / 4,000 lines, refused when binary. Markdown is converted after escaping every `[`. Links stay text, images become their alt text, HTML is shown as written; nothing is fetched or run.
- **Editors (all confirmed first):**
  - a Windows solution opens with its own association;
  - a Windows folder opens in VS Code if installed;
  - a WSL project opens in VS Code's WSL remote if VS Code is installed, never through a Windows association;
  - otherwise the card says no editor was found.
- **Build or deploy scripts:** never run.

## AD-34 Compact places (2026-11-06)

- **Problem:** places too large, so walking needed 8–16 m/s.
- **Decision:** `PlaceParams.Compact` is the default, and `--layout=spacious` keeps the previous layout for comparison.
  - The way back stands on the gate ring at 14 m, and the arrival terrace sits among the files, 1 m east of the avenue.
  - Gates are 14 m out, 6.5 m apart and 6 m tall; tiers start at 24 m, 9 m apart; vistas are 46 m beyond.
  - Speeds are 5.5 / 11 m/s. Saved speeds are kept; a notice and Suggested speeds offer the change.
  - Tier rings now meet: a third tier was unreachable in both layouts.
- **Measured:** typical gates 27–30 m away (5.2–5.7 s walking) instead of 47–48 m (6.0–6.1 s at 8 m/s); four Ups in a row 15.7 s instead of 19.7 s. The wide place's farthest tier is 5–8 % slower walking (13.3 s against 12.3 s), and 6.8 s running.
- **Revisit when:** DJ has walked it.

## AD-35 The horizon: silhouettes and haze (2026-11-06)

- **Decision:** beyond each vista, one more level is drawn as dark silhouettes, at most 3 per vista beacon, sized by subtree. Everything deeper in that direction is one band of haze whose size and brightness follow its count. Unknown places are pale veils.
- **Limits:** at most 220 marks, no labels, hidden from the air. Fog is thinner (volumetric 0.011 → 0.0078) so the band reads as shape.
- **Revisit when:** DJ judges the depth.

## AD-36 Pin identity is platform-aware (2026-11-06, follow-up)

- **Context:** pins and known folders were matched without case everywhere, so on Linux and in WSL `Notes` and `notes` (two folders) were one pin: pinning the second did nothing, unpinning either removed both, and reloading merged them.
- **Decision:** `PathIdentity` decides when two paths are one folder. Linux paths (POSIX, and the Linux part of `\\wsl.localhost\Distro\…`) compare exactly; Windows paths (drives, shares) without case, as Windows does by default. `\\wsl$\` is an alias of `\\wsl.localhost\`, the distribution name matches without case, and a trailing separator is ignored. Pins are stored in the canonical spelling with the case the user saw. Used by `Preferences.WithPin`/`IsPinned`, preference loading, `TogglePin` and `ProminenceContext`.
- **Limit:** NTFS per-directory case sensitivity (`fsutil file setCaseSensitiveInfo`) is not detected; such folders compare as Windows usually does.

## AD-37 Verification steps are judged on exit and on a usable result (2026-11-06, follow-up)

- **Context:** `verify.sh` ignored the exit codes of the real-input and recording commands. Its real-input count could never pass (`grep -c` with no match exits 1, so the count became "0 1"). A journey that exited 0 with an empty or assertion-free log passed: the full run of `be3c673` reported the planetoid step as passing with 0 assertions.
- **Decision:** one judge (`tools/verify-lib.sh`) for every probe step: non-zero exit (timeouts named), missing or empty result file, a file without PASS/FAIL lines, or any FAIL fails the step, with the reason in the summary. A recording needs exit 0 and a non-empty video. `fs.sh check harness` exercises every path with test doubles in ~3 s, so the harness can be checked without running a suite.

## AD-38 The aerial world reserves the current place (2026-11-06, follow-up)

- **Context:** the layout truncated breadth-first at `MaxIslands` without regard to where you are, and landing looked up the current place's island unconditionally. Deep in a large explored world, the current place could be missing: the aerial world was drawn in an identity frame, and landing over open space threw.
- **Decision:** `WorldLayout.Compute(…, focus)` reserves the focus's ancestry, the focus and up to half the remaining budget for its own folders, then fills the rest breadth-first; the budget still holds. When the way down alone exceeds the budget, `FocusShown` is false and the app does not draw the aerial world, says so in the chip, ignores island picks and lands in the current place. `--test-max-islands=N` makes the case reachable in the exported app.

## AD-39 The planetary world runs on the shared session; worlds vary only four things (2026-11-11)

- **Context:** the 2026-11-06 planetoid experiment owned its own sample, camera, rendering, input and travel. DJ asked for a playable planetary FileScape on the same session as Places, and for boundaries a future world (a forest) could use.
- **Decision:** `Main` keeps the tree, scan, explorer, navigator (current folder, history), selection, shell actions, HUD and state file. A world talks to them only through `IWorldSession` (`app/Scripts/Planets/WorldSession.cs`). What a world varies:
  1. **Surface and gravity:** `PlanetSurface` + `SphereWalker` (planets) / `PlaceTerrain` + `Walker` (Places). Both use the shared `JumpModel` (jump, coyote, buffer, short hop, one airborne boost).
  2. **Camera frame:** the planets' chase camera with local up (capped roll) / the Places rig.
  3. **Placement and travel:** `PlanetComposer` + launches / `PlaceComposer` + gates.
  4. **Visual and audio treatment:** `PlanetArt`, `PlanetVisual`, `AvatarRig`, `SkySynth` / `PlaceWorld`.
- Navigation is the navigator's: a launcher calls `TravelInto`/`TravelUp` at the release, and any change made elsewhere (Backspace, breadcrumb, Destinations, a path) starts the same launch from where you stand. Switching worlds (V) keeps the folder, selection and history because neither world owns them.
- **Consequence:** the flat Places world is unchanged except for the shared jump model (it gained the airborne boost) and small routing in `Main` (the new experience, V, the toolbar button).

## AD-40 Planets are whole spheres with circle obstacles, not charts (2026-11-11)

- **Context:** the experiment ran the flat walker in an azimuthal chart, which distorts across (sin φ/φ) and has a seam at the antipode, so a place had to stay within ≈ 35° of the pole.
- **Decision:** `SphereWalker` moves a unit "up" vector by rotation about up × velocity and parallel-transports its facing and velocity, so it is the same everywhere (tested through ±Y and antipodes). Solids are circles measured along the surface (`SurfaceObstacle`), so collision matches what is drawn anywhere on the sphere. Ground height is one smooth field (`PlanetShape.HeightAt`) shared by the walker and the mesh. Limits: obstacles are columns from the ground (no overhangs or head bumps), there is no slope limit, and an obstacle's top is measured from the ground under the walker.

## AD-41 Nested scale and a floating origin for travel (2026-11-11)

- **Context:** a whole neighbourhood (up to 10 child worlds plus the parent) must fit in view, and travel must never cut.
- **Decision:** the current planet is at the origin. A child hangs at its composed sky position at the scale (preview radius ÷ real radius) that makes it small in the sky, turned so its arrival meadow faces back towards the parent at 45°. The parent is drawn at the inverse of the current planet's placement in it: large, behind you. Because placements are a deterministic function of the two compositions, going down and coming back up agree. A launch flies into the target's placement while the traveller shrinks (or grows) into its scale; at touchdown everything (camera and its smoothing state included) is re-expressed in the target's frame, so the frame does not change on screen. Worlds not in the sky are reached by a warp (a rise into light, the new world built at the brightest moment, a descent).

## AD-42 Original procedural audio, locked to the tempo (2026-11-11)

- **Decision:** `SkySynth` (Core, pure C#, tested offline) generates music per theme (key and mode, 84 BPM, pad, music box, low pulse, a flight arpeggio) and effects in the current key; tempo-locked effects wait for the next 32nd note (≤ 89 ms), movement sounds are immediate, and the launch release is aligned to the sixteenth-note grid by stretching the wind-up by ≤ 0.09 s. `SkyAudio` streams it through an `AudioStreamGenerator` only while the planets are shown. No samples or assets.
- **Unknown:** nobody has listened to it; the levels are measured, not judged.
- **Amended 2026-11-11 (DJ: "a livelier tempo and a recognizable melody that returns with variations"):** 108 BPM for every theme; the tempo-locked grid stays the 32nd note (69.4 ms; a sixteenth, 139 ms, would exceed the ≈ 90 ms budget), so `QuantisedLatencyBound` is 70 ms, and `AlignToSixteenth` follows the tempo (stretch ≤ 0.09 s ≥ half a sixteenth, so a launch always lands on the grid). The music runs in 16-bar cycles: the original 8-bar FileScape theme (`SkySynth.ThemeMelody`: a 2-bar motif, its answer, the motif raised, a cadence; scale degrees on the pentatonic subset, so it maps into every key and mode) on a bell lead with a little glide, then 8 bars of free music box. Each return is a `ThemeVariation` (plain on cycles 0, 4, 8, …; otherwise ornamented, syncopated, octave up with a counter-line, inverted answer or augmented ending, chosen per seed, theme and cycle, never twice running). A new world starts its plain theme on the next bar line. Under it: a pad changing chord every bar under the theme (per mode, avoiding the diminished triad) and every 2 bars between, a plucked bass, quiet offbeat noise ticks. Music level unchanged (≈ -28 dBFS for the demo). Before/after samples: `%LOCALAPPDATA%\FileScape\evidence\2026-11-11-planets-2\music\`. **Superseded in part by AD-49 (2026-11-14):** the single 108 BPM tempo, the 70 ms bound and the bar-line start.

## AD-43 The Starter Sky: a curated sample of real files (2026-11-11)

- **Decision:** the first impression is a small folder of real, original files (`SampleSky`) the app writes into its own data folder (`%APPDATA%\Godot\app_userdata\FileScape\samples`), never anywhere else, marked and idempotent, refusing to touch an unmarked folder. Opening Planets from the synthetic sample (the default start) opens it; `--root=sample` starts there. The same contents are a fixture (`create-sky`) under `%TEMP%\FileScape-fixtures` and `/tmp/filescape-wsl-fixtures`.

## AD-44 Launchers on rings round the arrival, read like a clock (2026-11-11)

- **Context:** DJ's playtest: on the Ubuntu root, finding home took a while. Launchers were spread round the planet's band (55–125° from the arrival) and only 10 got pads (ranked by prominence and size), so home could be a moon in the belt or on the far side.
- **Decision:** up to 28 child folders get pads on rings round the arrival meadow: the first ring 9 m out, then every 7 m, each holding as many as fit 6.4 m apart. Names go in natural order; without a way back the first is straight ahead and they go clockwise; with it, the way back is exactly behind (6 o'clock) and the names spread evenly across the rest, reading left to right in front of you. A circle of small lights on the ground joins each ring. Each folder's world hangs on its pad's bearing, 32° further out, so the worlds stand round the horizon. The landmark and overlook move beyond the outermost ring.
- **Kept from Codex's draft:** nothing of its 57° band (it did not make room for more pads).

## AD-45 Names pinned to what they name (2026-11-11)

- **Decision:** the planets lay their names out every frame, each pinned straight above its anchor with a stem and a dot (`SignCandidate.FixedAnchor`), clamped on screen near the edges. Pads are named within 46 m; a world in the sky only when looked at (within ≈ 12° of the centre), pointed at or selected, and never when its pad is named on screen (one name per destination). The planet hides names: the line from the camera to the anchor must not meet the ground. Priority: selected or pointed-at, then the looked-at boost, then the nearest. The flat world's signs are unchanged.

## AD-46 The overview: the known hierarchy as nested star systems (2026-11-11)

- **Decision:** `UniverseLayout` (Core, tested) lays out every known folder (≤ 1,500; ≤ 64 per system) as a world whose subfolders orbit it on one tilted ring; ring radii come bottom-up from the children's extents, so nothing overlaps. Chains (one subfolder) are drawn as short links, systems as faint rings. Listed folders are solid in their theme's colour, partly listed dimmer, unknown ones pale ghosts; visited ones are ringed; where you stand has a gold beacon. The way to where you stand is shown even through hidden folders. The overview is drawn in the same scene far from every planet; selection is the shared selection; landing goes through the navigator (`TravelInto`), so history and Backspace work, and you come down out of the light as from a warp. T or the toolbar opens it; Esc or T glides back down to where you stood.

## AD-47 Planets camera controls: left-drag looks, clicks act on release, ← → turn (2026-11-14)

- **Problem:** DJ expects to left-drag to look round; only right-drag did. A press selected at once, so a press that began a look would also select. GUI keyboard focus (the planet list's filter) ended a camera drag the frame it began (reproduced on `d2695b8` with real input: a 360 px drag turned nothing). Nothing turned the camera from the keyboard while standing still (← → strafed).
- **Decision:** On a planet and in the overview, left- and right-drag both look (orbit). A left press over the world waits: it becomes a drag after 6 px of travel, otherwise a click on release (single: select; double: launch or land, as before). A drag never selects or activates on release, even when it began on a world. Presses over the HUD and the planet's panels go to the GUI. A drag ends with its button, a lost window focus or a modal dialog, never because a control holds keyboard focus; a press in the world releases GUI focus. ← → turn the camera at 110°/s (Shift ×1.6), eased in over 0.12 s, as they turn the view in Places; movement follows the camera, so W + → curves the walk; A/D still strafe, WASD steering is unchanged. In the overview ← → orbit; WASD pans.
- **Alternatives:** Q/E for turning (E is "look closer"); left-drag that selects on press (the bug DJ named); pitch keys (Page Up is "up a level"; the chase camera's pitch follows the ground well enough, and the wheel zooms).
- **Revisit if:** DJ wants pitch on keys, a different turn rate, or arrows back as strafe.

## AD-48 World character: what kind of place it is, heard and seen (2026-11-14)

- **Problem:** Every world sounded and looked alike apart from what its files were (the content theme). DJ asked for distinct identities for the hub, filesystem roots (a Windows volume vs a WSL distribution), ordinary folders by depth, and repositories wherever they sit.
- **Decision:** A Core `WorldCharacter` (names only, FS-01): `Hub` (This computer, or the starting folder when no hub is above it), `WindowsRoot` (drive, volume, share), `WslRoot`, `Repository` (a `.git` folder, or a `.git` file: a linked worktree or submodule; it wins over the root kinds), else `Folder` in a depth band below its root: 1 (depth 1, "the outskirts"), 2 (2–3, "the trails"), 3 (4–5, "the deep woods"), 4 (6+, "the underglow"), remembering the repository it is inside. The content theme keeps the ground, palette and props; the character adds, on top: a sky tint and details (hub: a warm dawn low on one side; Windows: a pane lattice and a blue aurora; WSL: an amber aurora and a phosphor scanline shimmer; repository: a commit graph among the stars; bands: a tint that deepens with the band, more stars, aurora from band 3, a commit-graph echo inside a project), the sun's colour, and one prop beside the arrival where you see it as you land (Windows: a four-pane glass gateway; WSL: a terminal monolith with a blinking `>_`; folders: a waymarker with one glowing ring per band, a teal bead inside a project). The repository's prop is its workbench (AD-50). The music follows the character (AD-49).
- **Alternatives:** Characters as new content themes (would lose what the folder holds); the gateway as the landmark (it stood beyond the launcher rings, out of view on arrival); vendor logos (not ours to use).
- **Known gaps:** The gateway and monolith do not block walking. A Windows volume root could not be entered in evidence runs (never C:\); its look was photographed with a probe-only override on the Starter Sky.
- **Revisit if:** DJ wants more bands, other kinds (network shares, removable drives, cloud folders), or props that collide.

## AD-49 Music per world character (2026-11-14)

- **Context:** DJ: the music was "pleasant and a little too chill for exploring"; the hub should feel like a calm arrival, and once you set off the music needs bounce and momentum, audible without watching. Roots are gateways (Windows and WSL distinct), folders change with depth but stay one journey, repositories feel special on arrival (PLANET-28; characters from AD-48's `WorldCharacter`).
- **Decision:** `SkySynth.SetWorld(theme, character, visits)` is called on every arrival. `IdentityOf(character)` picks one of eight original identities (`MusicIdentity`), each with its own tempo, groove (a synthesised kit: sine-glide kick, noise snare/clap/rim, hats, toms, clockwork ticks, keystrokes), bass, lead instrument and motif; the theme (`PlanetTheme`) still sets the key's root, the identity may set the mode:
  - **Hub** 94 BPM, the theme's own mode: no kit, a soft sub, pad and bell; the FileScape theme stated simply but its cadence replaced by a rising question (la sol la do' — re', over IV → V) that never lands on the tonic; between returns the chords wander (vi IV ii V) and three rising "journeys" sound left, right and centre. Leaving it, the next world's first downbeat rings its tonic: the answer.
  - **Windows volume** 116 BPM, lydian: glass. Soft four-on-the-floor, claps, offbeat hats, an octave bass, a 16th-note glass FM arpeggio; the theme on a glassy bell; a "gateway call" (mi sol do' — re') between; a glass sweep on arrival.
  - **WSL distribution** 112 BPM, dorian, light shuffle: the terminal. Keystroke clicks in a typing rhythm with an enter on every second bar, dry rims, a syncopated square bass, the theme on a square lead re-striking its long notes like a held key; between returns a typed command (a run of 16ths), enter, and a three-note answer; a prompt blip on arrival.
  - **Repository** 126 BPM, mixolydian, straight: clockwork. Kick on 1, the and of 2, 3; tight 16th hats; tick-tock wood blocks; an FM sequencer bass; the theme on a "digital" FM lead with chord stabs answering it every second bar; between returns the commit motif, a call ("da, da-da DAAA" up to the octave) and response (two stabs, "commit"), whose second bar climbs (a worktree's falls, like a branch). On arrival a one-bar flourish: crash, a 32nd-note run up the scale, the stabs.
  - **Folder bands** with bounce, quicker as you go deeper, never slower or sadder: 1 the outskirts 120 BPM (swung, marimba, octave-hopping bass), 2 the trails 124 (four on the floor, offbeat square bass, open hats, dotted-eighth plucks), 3 the deep woods 128 (dorian, toms and rims, shaker, deep bass, a wood lead), 4 the underglow 132 (lydian dominant, 16th hats and FM pulse, glowing fourths). They share the **band motif**, grown from the theme's head (mi sol la sol do' | la sol mi sol re, in 3 + 3 + 4), shaped per band (as written; displaced with a rising answer; an octave down with an echo; octave leaps) and varied per phrase (diminution, a sequence a step up, an open ending). A folder inside a project keeps a quiet echo of its repository: the tick-tock, soft stabs, and the commit call high and quiet every 8 bars.
  - The 16-bar cycle and the FileScape theme's returns and variations (AD-42) are the family's common thread in every identity.
- **Transitions:** the new world starts on the next beat of the music playing, as its own first downbeat and clock; the old world stops there, releases, and fades over 1.2 s (equal power). A further call before that downbeat only retargets it, and a call back to the world still playing cancels the change (bit-identical), so hops never stack; the deck being reused is cleared in ≈ 40 ms. The same world and key again (a sibling folder of the same band and theme) keeps the groove and answers with a drum fill. Arrival fanfares are skipped when hopping (< 1.5 s apart) and on returns. Up to 8 worlds are remembered: a return resumes on the world's next half-cycle (its own material, or the next variation) instead of starting cold. `visits` develops a world: from the second visit a counter-line and ghost notes, from the third fuller drums (open hats, a doubled arpeggio, the theme's long notes doubled below) and a later, varied cycle.
- **Timing:** tempo-locked effects wait for the next 32nd of the music playing (the old world's until the new downbeat, which is itself on that grid; then the new world's): at most 79.8 ms at 94 BPM, 56.8 ms at 132; `QuantisedLatencyBound` is 80 ms. `AlignToGrid(duration, maxShift)` (Core) ends a wind-up on a sixteenth of the music that will be playing then, at its tempo; `SkyAudio.AlignToSixteenth` delegates to it. `Arrive` sounds in the arriving world's key.
- **Mix:** the landing, launch, wind-up and arrival duck the music (40–75 %, a 4 ms attack for the landing and launch, ≈ 0.25 s recovery); measured, the cue stands 5–12 dB over the ducked music in every world. During flight the kick and snare thin out. Music level unchanged (≈ −28 dBFS RMS, as before); voices 40 (28 for music); a 16-bar band render peaks ≤ 0.36.
- **Evidence:** SkySynth tests (tempo per identity, grid alignment at every tempo and right after a change, `AlignToGrid`, 8 worlds in 2 s within budget and click-free, determinism, a cancelled change is bit-identical, revisits differ ≥ 30 % and add a counter-line, returns resume, identities differ in tempo/onset density/brightness with the hub the calm one, cue clearance, the hub's open question). Samples and a comparison route: `%LOCALAPPDATA%\FileScape\evidence\2026-11-14-planets-3\music\` (README.txt). **Unknown:** nobody has listened yet; all judgements so far are measurements.

## AD-50 The repository workbench: land to reveal, spin to read (2026-11-14)

- **Problem:** DJ asked for one playful move that does something a file explorer is for, with a readable target, a satisfying response, and a result worth the motion; movement may reveal, select or preview, never open, run or change.
- **Decision:** On a repository world the overlook becomes a workbench (deck 1.0 m: one plain jump; walking does not climb it). **Landing on it** reveals the project's key places (`ProjectBench`, from names only: README first, the solution, manifests, entry files here or one level down in a listed source folder, then src/tests/docs/tools/CI folders, a build recipe, the licence; at most 9): they rise out of the bench as a slowly turning ring of glowing cards, each named with what it is for, and the bench's panel lists them (click selects, so the card shows it; double-click goes there). **A spin from the bench** (Space, then Space in the air) opens the README in the preview. **E at the bench** and **a click on the bench** do the same without moving; the panel is the list route. Enter (or the preview's button) remains the only way to open anything with Windows, with the usual confirmations.
- **Why this pairing:** the overlook already was the "inspection station" you climb to; on a project the first useful things are where to start (README) and where things are (entry points), and both are reveal/preview actions. A spin is the planets' most expressive move and reads as "show me".
- **Alternatives:** Ground-pound on a file to preview it (not reachable everywhere, no project-level payoff); a spin to open in an editor (violates "deliberate action only").
- **Revisit if:** DJ wants the bench elsewhere (non-repository projects), more entries, or a different gesture.

