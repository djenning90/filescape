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
- **Decision:** A Core `WorldCharacter` (names only, FS-01): `Hub` (This computer, or the folder you started from — your home base, whatever the tree shows above it — unless that folder is itself a root or a repository; depth counts from it), `WindowsRoot` (drive, volume, share), `WslRoot`, `Repository` (a `.git` folder, or a `.git` file: a linked worktree or submodule; it wins over the root kinds), else `Folder` in a depth band below its root: 1 (depth 1, "the outskirts"), 2 (2–3, "the trails"), 3 (4–5, "the deep woods"), 4 (6+, "the underglow"), remembering the repository it is inside. The content theme keeps the ground, palette and props; the character adds, on top: a sky tint and details (hub: a warm dawn low on one side; Windows: a pane lattice and a blue aurora; WSL: an amber aurora and a phosphor scanline shimmer; repository: a commit graph among the stars; bands: a tint that deepens with the band, more stars, aurora from band 3, a commit-graph echo inside a project), the sun's colour, and one prop beside the arrival where you see it as you land (Windows: a four-pane glass gateway; WSL: a terminal monolith with a blinking `>_`; folders: a waymarker with one glowing ring per band, a teal bead inside a project). The repository's prop is its workbench (AD-50). The music follows the character (AD-49).
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
- **Decision:** On a repository world the overlook becomes a workbench (deck 0.7 m, no stepping stones: any jump, even a tapped hop, lands on it; walking does not climb it). **Landing on it** reveals the project's key places (`ProjectBench`, from names only: README first, the solution, manifests, entry files here or one level down in a listed source folder, then src/tests/docs/tools/CI folders, a build recipe, the licence; at most 9): they rise out of the bench as a slowly turning ring of glowing cards, each named with what it is for, and the bench's panel lists them (click selects, so the card shows it; double-click goes there). **A spin from the bench** (Space, then Space in the air) opens the README in the preview. **E at the bench** and **a click on the bench** do the same without moving; the panel is the list route. Enter (or the preview's button) remains the only way to open anything with Windows, with the usual confirmations.
- **Why this pairing:** the overlook already was the "inspection station" you climb to; on a project the first useful things are where to start (README) and where things are (entry points), and both are reveal/preview actions. A spin is the planets' most expressive move and reads as "show me".
- **Alternatives:** Ground-pound on a file to preview it (not reachable everywhere, no project-level payoff); a spin to open in an editor (violates "deliberate action only").
- **Revisit if:** DJ wants the bench elsewhere (non-repository projects), more entries, or a different gesture.

## AD-51 Planets first; the earlier experiences become Legacy views (2026-11-15)

- **Problem:** FileScape is heading for its first public playtest and leads with Planets, but a plain launch opened the flat Places world on a synthetic 12,000-node tree, and the toolbar gave Places and the two maps equal billing.
- **Decision:** A person's launch (no `--automate`) opens **Planets at This computer** unless a folder (`--root`), the sample (`--root=sample`) or a view (`--vis`) was given; explicit arguments always win. There is no saved "last view" setting to override this (checked: preferences hold look, speed, pins and the welcome choice only). Automated journeys keep their historical defaults (Places on the synthetic tree; the maps for the evidence scripts), so their expectations stay valid. The toolbar shows **✦ Planets** and a **Legacy views ▾** drop-down holding Places and the two maps (an ordinary panel of buttons, drawn above the place bar); V and Tab still switch. **Sample world** opens the Starter Sky in Planets (the synthetic tree remains for `--synthetic=N` and the maps). Help and the README introduce Planets first; the old Places guide moved to `docs/legacy-views.md`.
- **Alternatives:** removing the legacy views (DJ wants to revisit them); a saved default view (more state, and a newcomer never chose one).
- **Revisit if:** DJ wants the last-used view remembered, or the legacy views removed.

## AD-52 Arrangements and hazard cues (2026-11-15)

- **Context:** DJ (PLANET-35): the same FileScape melody in different rhythms, instruments and scales; push the synth with festival production as inspiration for momentum, builds and release (technique, not material: supersaw stacks, sidechain pumping, risers, snare rolls, filter sweeps, drops); three contrasting original arrangements, each where it belongs; the hub's gentle welcome kept; the black-hole (hazard) world with its own warning and fall cues and room in the mix for them.
- **Decision — where they live.** The three new arrangements replace the three folder bands whose AD-49 arrangements were the plainest, so going deeper still escalates (bands ascend 120 → 126 → 144 → 172 BPM) and the band motif stays the bands' common thread:
  - **Festival (band 2, the trails), 126 BPM, the theme's own key and mode.** The trails is where you fly most, and siblings of the same band and theme keep the deck, so the payoff lands often. Per 16-bar cycle: the **drop** (the theme on a supersaw — two three-saw stacks spread wide, a bell an octave up for the attack, a narrower stack a third below as harmony — a four-on-the-floor kick with a long sine tail that **pumps** the pads, saw bass and stabs, claps, offbeat open hats, a crash and sub impact at the top), the **breakdown** (no kick, a filtered pad, the band motif on a pluck), the **build** (bars 12–15: a snare roll from quarters to sixteenths, a noise riser and a saw riser gliding up an octave, the pad's filter opening 600 Hz → 5 kHz, climbing stabs, the kick out for the last two bars, one silent beat).
  - **The launch/arrival payoff, with the existing calls only.** While the festival world plays and `Fly(progress)` is being called (every frame of a flight or warp, as now), the deck plays the **flight build** instead of its groove: a snare roll from eighths to sixteenths to thirty-seconds with the progress, climbing stabs and a steered noise riser (400 Hz → 9 kHz). The next `Arrive()` releases it if the flight got past halfway: the **drop** lands on the first beat at least 0.15 s after the arrival (after the landing thump and the arrival chord); the cue ducks end at the drop and the music returns in ≈ 30 ms instead of 250. A sibling festival world (same deck) jumps to the top of its theme on that beat (a whole number of beats, so the pulse's grid is untouched); a new festival world starts its first downbeat there, from the top of its theme, with the impact; any other world gets the impact on its first downbeat; **the hub never does** (its welcome stays gentle). A hop (no `Fly`) is not a build: same-world arrivals keep their place and drum fill. The app needs nothing new: `WindUp` → `Launch` → `Fly` each frame → `Land` → `SetWorld` → `Arrive`, as `PlanetWorld.Travel` already does.
  - **Orchestral (band 3, the deep woods), dorian, the theme at 72 BPM over a 144 BPM pulse.** The theme's phrases pass between the sections: the motif on a woodwind (flute an octave up; clarinet for syncopated or inverted returns, oboe for octave and augmented ones) over pizzicato, the answer on the first violins as the spiccato ostinato starts, the raised motif on the horns (paired saws whose filter opens as they swell) with the violins above and timpani, the cadence tutti (violins, horns an octave down, flute above) with a cymbal swell and crash; between returns the band motif in the cellos with the flute echoing, a horn call (mi sol do') and the violins' answer, a timpani roll into a tutti climax on bar 14, the harp falling in bar 15. Strings are three-saw sections with vibrato; their filter follows a per-bar dynamic (pp 0.4 → ff 1, two waves per cycle); space from panning (violins left, violas and cellos right, horns back right) and high reverb sends.
  - **Drum and bass (band 4, the underglow), dorian, 172 BPM breaks under the theme at 86.** An original two-step break (kick on 1 and the and of 3, snares on 2 and 4 of the pulse, ghosts from the second visit and in the last four bars, a fill), sixteenth hats, an inharmonic ride, a **reese** (two saws 14 cents apart, the filter opening on each held note) over a sine sub, airy strings, the theme on a soft FM lead with a dotted echo; bars 9–10 rest the kick and snare until the third visit.
  - **Hazard (`WorldKind.Hazard`, new `MusicIdentity.Hazard`), phrygian, the theme at 70 over a 140 pulse:** a drone two octaves down beating ±7 cents with its fifth, dark wobbling strings, a rumble, a heartbeat (lub-dub on 1 and 3), ticking inharmonic metal, a struck metal on beat 4 of odd bars, reversed swells into every second bar line; the theme on an unstable lead (two bells 18 cents apart, a slow wobble, long notes sagging 30 cents); between returns the theme's head turned downwards. On arrival a deep gong.
- **Hazard cues (SkySynth and SkyAudio, the signatures the app's hazard work calls):** `HazardWarning(nearness01)` every frame near an edge — a low rumble following the nearness and a falling-tritone pulse (deliberately outside the key) on the 32nd grid, every 1 s at the 0.05 threshold to 0.25 s at the edge, the music ducked up to 0.6; self-throttling and O(1) (50 calls a frame change nothing), silent within 0.3 s of the last call. `FallStart()` — a crack, falling glass shards, a dropping thud, the music ducked 0.8 (≈ −14 dB). `Fall(progress01)` every frame — a whoosh falling three octaves in the key (two above the root to one below), its two voices circling left and right faster (0.6 → 5 turns/s), wind darkening; it releases itself at 1 or 0.3 s after the last call. `FallReturn()` — an upward pop and a bright tonic chord in the arriving key; the fall's duck is replaced by a short one (0.7 for 0.4 s) and the music comes back. All immediate.
- **Engine additions (old sounds bit-identical):** a polyBLEP saw stack (up to three detuned copies, scaled 1/√n, through a two-pole low-pass); tone sweeps, vibrato and frequency steering per voice; a second, **pumped** voice group per deck whose gain follows the deck's kicks (dip 0.6 in 4 ms, recovery time constant 0.13 s); half-time styles (the score's sixteenth is twice the pulse's; the effects grid, `AlignToGrid` and the next-beat start of a new world follow the pulse, so every tempo-locked cue still waits ≤ 80 ms and launches land on the beat); a per-style level for loudness balance. The behaviour baseline's audio hashes for the hub, Windows, WSL and repository are unchanged; only bands 2 and 4 (now festival and drum and bass) changed.
- **Mix and measurements (tests in `SkySynthTests`):** integrated loudness (K-weighted, gated, BS.1770-style) of the new arrangements −27.1…−27.3 LUFS against −26.9 for the AD-49 ones still in use (all nine within 2.1 dB); music peaks ≤ 0.37, so the limiter never acts on music; no clicks across switches or in 8 worlds in 2 s plus backtracking (voices ≤ 28 music / 40 total); a minute in each new world varies (theme and own material, 5 s loudness spans of 1–7 dB) and never piles up (mean 10–14 music voices); render cost 26–27 ms per second of audio with a flight build, a fall, the warning and six effects (bound 60 ms, Debug); every cue stands ≥ 4 dB over the ducked music in every world (the outskirts' marimba is the tightest: warning 4.8, return 4.4 dB). Samples and README: `%LOCALAPPDATA%\FileScape\evidence\2026-11-15-playtest\music\`.
- **Alternatives:** the festival in the repository (its arrival flourish is already its payoff, and the clockwork identity is distinctive); orchestral at the Windows volume (the glass gateway is a strong identity); driving techno for band 4 (too close to the festival's four-on-the-floor); a new `Approach(destination)` call so the build could know where it lands (would need app changes; the departing world's build and the arrival's drop need none); a build in every world (it would break the hub's calm and the roots' identities).
- **Known limits:** the orchestra's onset rate (the "bounce" detector) is only ≈ 1.2× the hub's: it hears the sustained sections over the spiccato, so that test holds the orchestra to more than the hub rather than twice; the drum-and-bass sub (≈ 30–60 Hz) will not be heard on laptop speakers; `SetFlight` (the flight arpeggio) is still not called by the app. **Unknown:** nobody has listened; every judgement here is a measurement.
- **Revisit if:** DJ prefers other placements (each arrangement is one `Style` row and can move), a drop on every arrival, or a different warning sound.

## AD-53 The black-hole world: broken shell, safe route, a fall that navigates (2026-11-15)

- **Problem:** DJ asked for one memorable hazardous world (PLANET-34), inspired by the broken planets and black holes of Super Mario Galaxy: an incomplete spherical shell with visible broken edges over a fast-spinning black hole; a regular route that is easy to read and forgiving; danger only off that route; stepping off an edge as a short, satisfying fall that spirals and then returns you to the parent by normal navigation, ready to re-enter. A sample hazard everyone can visit, and a small, explainable set of real system-managed locations, matched by platform and path context so a project folder with the same name is not caught. No effect on files or permissions.
- **Classification (Core `HazardRules`, names only, FS-01):** a folder is a hazard world when one rule matches; only that folder is a hazard (the folders inside it are ordinary worlds), and location roots never are.

  | Rule | Folders | Context required | Why each warrants a caution cue |
  |---|---|---|---|
  | Windows system | `Windows` | directly under a Windows volume root (a drive or a volume location, or a tree opened at `C:\`); case-insensitive | the operating system's own files; changing them can stop Windows from starting |
  | | `Program Files`, `Program Files (x86)` | same | installed 64-/32-bit programs, managed by their installers |
  | | `ProgramData` | same | machine-wide program data and settings shared by every user |
  | | `$Recycle.Bin` | same | the Recycle Bin's hidden per-user stores |
  | | `System Volume Information` | same | restore points, shadow copies and indexing data, kept by Windows |
  | | `Recovery` | same | the Windows Recovery Environment, used to repair a PC that will not start |
  | Linux system | `proc` | directly under a Linux root (a WSL distribution `\\wsl.localhost\name`, the POSIX root `/`, or a tree opened at either); case-sensitive | the kernel's live view of processes and settings; its files are not files on disk |
  | | `sys` | same | the kernel's live view of devices and drivers |
  | | `dev` | same | device files: reading or writing them talks to hardware |
  | | `boot` | same | the kernel and boot loader the system starts from |
  | | `run` | same | runtime state of running services (sockets, locks), rebuilt at every start |
  | Sample | a folder holding a file `.filescape-hazard` | a proper ancestor holds the sample marker `.filescape-sample` (inside a FileScape sample) | the Starter Sky's Event Horizon: somewhere anyone can practise falling off, safely |

  Not hazards (tested): `D:\projects\Windows`, a repository called `Windows`, `C:\Users\dj\projects\Program Files`, `proc` and `sys` inside a repository, `Proc` at a distribution root (case), `proc` on a Windows drive, `Windows` at the POSIX root, the top folders of a network share (someone's shared folders, not an installed system), `C:\Windows\System32` (inside a hazard), a `.filescape-hazard` outside a sample, or one that is a folder. **Precedence** in `WorldCharacter.Of`: the hub first; then Hazard, over Repository and over "the folder you started from is the hub" (being system-managed is what matters about the place); roots are never matched.
- **The shell (Core `BrokenShell`, carried by `PlanetSurface.Shell`):** jagged holes (`ShellHole`: a base angle shaped by three broad lobes and two sets of triangle-wave teeth, from a seed; the edge stays within ±33 % of the base) carved by `PlanetComposer` after the route is laid out: a main breach near the antipode (base ≈ 32°), then up to three side breaches just past the equator (≈ 18°), each shrunk until its farthest tooth keeps **3 m** (`SafeMarginMetres`) from the arrival meadow, the landmark, the overlook and its stones, every launcher's clear area (3.3 m), every drawn route and the walk to the way back (± 1 m), and every launcher ring; 2.5 m of ground between holes (walkable bridges). Files and scenery keep 3 m from the edges, and nothing stands in a hole's "shadow" seen from the arrival (the straight way from the meadow to anything placed stays 1.5 m from every hole). Hazard worlds are at least 18 m in radius. On the sample about 15 % of the sphere is open. Non-hazard compositions are bit-identical (behaviour baseline).
- **No ground over a hole:** `SupportAt` reports `NoGroundMetres` (−1000 m) when the walker's centre is past an edge, so walking on, it leaves the ground and falls under ordinary gravity. Coyote time and the air jump still apply, so a quick jump or twirl just after stepping off can save you; once the feet are `FallThroughMetres` (0.6 m, ≈ 0.27 s) below the rim there is no ground anywhere (it cannot pop back up through the crust) and the fall is committed. Teleporting over a hole stands the walker at the rim's level (hops). Hops (Approach from the list or Destinations, Home) whose straight arc would cross a hole go through the arrival point instead, so keyboard and list navigation never route across an edge; G, U, Backspace and the list's travel are launches, unaffected.
- **The fall (app, `PlanetWorld.Hazard`, `Phase.Falling`):** 2.6 s along a parametric path that starts exactly where and as fast as you stepped off, drops towards the centre (u^1.8), flattens into the black hole's disc and winds 2¼ turns round it, faster and faster (u^2.2); the camera follows down through the hole, turning its up to the disc's axis, the field of view widens to 86°, speed lines and a violet flash build, the traveller tumbles and stretches thin. At the flash's peak the world calls the session's `TravelUp` — ordinary navigation: history, selection and listing behave as for U — and the navigator's change arrives as the warp descent out of the light onto the parent, beside the hazard world's launcher (`ArrivalFor`), with the hazard selected; a hint says to step on and press Space to try again. A navigation key during the fall becomes the fall's destination; at the top of the tree you are set down on the arrival instead. Sound: `SkyAudio.HazardWarning(nearness)` every walking frame on a hazard world, `FallStart`, `Fall(progress)` per frame, `FallReturn` on landing (placeholders here; PLANET-35 supplies the sounds).
- **Look:** the ground mesh is cut along the same edge function the walker falls at (corners over a hole are moved onto the edge), scorched near edges, with a dark underside a crust's thickness (1.6 m) below and rim walls showing a dark lip, soil, three rock bands and a glowing seam; a glowing line along every edge brightens as you approach; tumbling debris in and above the holes; a light plume rising out of each hole (it shows above the rim as you walk towards a hole; not from the sample's arrival, against its bright sky); at the centre a black core, a photon ring, a fast-spinning accretion disc (gold to violet, inner parts turning faster) and an orange light on the crust's underside. A hazard world in another's sky shows its holes, plume and black hole. On arrival a caution banner (amber) says what the place is and that the route is safe; near an edge the camera rises and looks ahead into the hole, a warm vignette deepens and a one-time hint explains the fall. The sky is a bruised violet with an ember glow.
- **What it never does:** no file is read (classification is by names), moved, changed or deleted; permissions and every file control (Open, Reveal, Copy path, preview, the list) behave as on any world. Falling is a navigation event only.
- **Evidence:** Core `HazardWorldTests` (every rule and look-alike, a path opened at a drive or distribution root, the sample marker only inside a sample, precedence; for five hazard worlds from empty to 40 child folders: the route's margins, walking to every launcher without falling, falls only off the shell, determinism, the edge profile) and `SampleSkyTests` (the written sample classifies Event Horizon and nothing else); the `planets-hazard` probe on the Starter Sky (arrival on solid ground with the caution cue, the safe route to the way back and a child launcher on foot, a list hop over solid ground, the edge warning, a fall that spirals 2¼ turns to within 3.3 m of the centre, the return to the Starter Sky 2.5 m from Event Horizon's launcher with it selected, immediate re-entry, U back).
- **Alternatives:** Classify by attributes (System/Hidden flags: many ordinary folders carry them, and reading them is not "names only"); a fixed hole pattern (would cut through launchers on crowded worlds); a fall that teleports without navigating (would leave history and selection inconsistent); dying or losing progress (DJ: the fall is a navigation event).
- **Known gaps / unknown:** the edge distance used for the warning is radial (slightly generous near a tooth's tip; margins use the farthest tooth instead); the fall's sounds are placeholders until the music work lands; nobody has played it yet. A real `C:\Windows` was not entered (never scan system folders in evidence runs); the Windows and Linux rules are tested on fixture trees.
- **Revisit if:** DJ wants more (or fewer) system locations, hazards inside the hazard, a way to opt out of the cue, or a different penalty-free consequence.

## AD-54 Learning by playing: a welcome dialog and a walkthrough with real actions (2026-11-15)

- **Problem:** A newcomer handed the build should learn by playing, without reading the README or a control sheet.
- **Decision:** On a person's ordinary launch (no folder or view arguments) and while the preference allows it, a small **welcome dialog** offers *Learn to play*, *Explore on my own* and *Don't show this again* (saved as `showWelcome` in preferences.json). **Help → Learn to play** reopens the walkthrough at any time. The walkthrough runs in the Starter Sky as 13 short steps in five lessons: look (left-drag, 120°) and walk to the tower; jump, then fly from the Lantern Workshop launcher; jump onto the project's workbench and press E for the README (the spin offered as the playful option); U to the parent, J's list straight to Music Box, U (up is the parent), Backspace (back is your history — to Music Box, where you were, not further up); T for the overview, drag to orbit, Esc back to where you stood. Each step is one instruction in a small card at the top centre (the arrival title moves down under it), a **beacon** in the world (a column of warm light with a bobbing ring and a pinned name) when there is a place to go, a one-line hint of which way to turn when the target is out of view, a check of the real action (never a Next button), and a chime and burst of light on success. **Skip step**, **Start over** and **Leave** are always on the card; after a wrong turn (another folder, not travelling, for a second) **Back to the lesson** makes a normal trip to the lesson's folder without restarting. At the end it offers **Explore This computer**.
- **Why these lessons:** they are the moves every later session needs, in the order you need them; the up-versus-back pair is staged where they differ (after a direct jump with J, up and back lead to different places).
- **Alternatives:** a video or a control sheet (DJ: learn by playing); a modal step-by-step overlay (hides the world).
- **Revisit if:** playtesters stall on a step (the probe and the real-input probe record every step's completion), or the sample world changes.

## AD-55 The walkthrough's target cues: a lime beacon, a pinned chevron, an edge arrow (2026-11-16)

- **Problem:** In DJ's playtest of `c29785d9b32a` the walkthrough's beacon (a warm-gold column with a ring and a pinned name) was lost by step two. The name was a sign in the destination-label style, so it blended in with ordinary labels. On the tower step the column stood inside the tower. Nothing on screen said where the target was once it left the view; only the green "turn left/right" line on the card helped. DJ got to step four of thirteen (PLANET-38).
- **Decision:** The active target has a visual language of its own, **lime** (`TutorialCue.CueColor`, 0.55/1/0.3). Nothing else in the planets uses that colour, and the card's turn hint, the key caps and the success burst now share it. The cue has **no sign-style label**.
  - **In the world** (`PlanetWorld.Tutorial.cs`): a 16 m column of light rising from the spot (bright at the base, fading upwards; its core breathes at 1 Hz) and a band of light round the spot on the ground marking where to stand, with ripples that close in on it. The band reaches down by the curve's drop so it meets the ground all round.
  - **On screen** (`TutorialCue`, a 2D layer under the planets' panels), one of three modes:
    - **onscreen:** a bobbing chevron with a dark outline, pinned over the spot (over the tower's top, over the pad, over the bench's deck), with the distance under it. It says "over the horizon" when the ground there is behind the curve but the chevron's point is in sight.
    - **horizon:** when that point is hidden too, the chevron waits where the horizon is in the target's direction (a point 30 m along the ground's tangent from the camera).
    - **edge:** otherwise (to the side or behind), a 64-unit arrow with a dark disc behind it, pulsing and nudging, at the left or right edge (70 units in, below the lesson card, level with the horizon). It points the way to turn, says "behind you" beyond 120°, and keeps its side while the target is within 15° of straight behind, so it doesn't flip.
  - **Approach:** within 6 m of the spot's edge the ring brightens and pulses, the ripples double their speed, the chevron grows and comes down, the distance gives way to a lime **pill** saying what to do there ("Stand here · press Space to fly", "Run at it and press Space", "Press E to read the README", "Walk into the ring"), and the column fades out (gone when you stand there, so it never fills the view). The pill stays while you stand on the spot.
  - **Steps with no place to go** (look, jump, U, J, U, Backspace, T, orbit, Esc) show no beacon: the card shows their keys as **key caps** (lime on dark), which is where the eye already is. This holds for every step: the instruction and the caps sit side by side.
  - The card's text line stays as the backup ("The green beacon is behind you: turn right →").
  - The arrival title now moves below the card's actual bottom; it used to overlap when the hint line was showing.
- **State for the real-input probe:** `cue` in the state file:
  - `mode`: none | onscreen | horizon | edge
  - `x`, `y`: window pixels
  - `angle`: the arrow's screen angle, degrees; 0 = right, 90 = down (a chevron), 180 = left
  - `distance`: metres to the spot's edge
  - `phase`: far | near | here
  - `overHorizon`, `action`, `keys`
- **Alternatives:** An arrow floating in the world to chase (it gets lost behind the avatar and off screen, which was the original complaint). A label in the destination style with a different colour (it would still read as a label). A minimap or compass bar (more UI over the world; the edge arrow does the job only when needed). Pointing at keys in the J list (the list's own filter and selection already guide; the key caps are enough).
- **Revisit if:** playtesters still lose the target, the lime clashes with a world's palette (a green aurora is the closest; the dark outlines keep the cue readable over it), or the pill and the bottom prompt feel redundant on the pad (they say the same thing in two places).

## AD-56 Direct navigation: an inline Destinations tree, trips hop by hop, one step of history (2026-11-16)

- **Problem:** DJ likes the Destinations list and wants to see more at once, go down the hierarchy from it, watch a trip several levels down with a clear way to stop, have the navigation dialogs get out of the way of the flight, and reach the places he cares about (repositories) fast (PLANET-39..41).
- **Decision (the list):** In Planets, J and ☰ Destinations open one panel: a dense **inline tree** (about 20 px a row instead of 27) built from `DestinationTree` (Core): the way back; **★ Favourites & repositories** (pins, then the repositories found so far); every world here, each expandable in place with ▸ (click) or → / ← (keyboard), level after level; and the files here in a group (closed when there are worlds, open when there are only files). Each folder row says what is behind it before it is opened (counts, "empty", "not listed yet", "listing…", "access denied" in the warning colour); an opened branch that turns out to have no folders says why in a note. Opening a folder that was never listed asks the scanner to list **that folder only** (`WorldExplorer.Expand`: urgent, not counted as a visit, so its children are not listed ahead). The filter finds folders anywhere below among what is already listed (bounded at 20,000 folders) and opens the way to each match; ↓ and Enter go to the first match. Rows are rebuilt at most every 0.25 s and only when they change, keeping selection, open branches and scroll. A pile or the telescope still opens the same panel as a flat list of one kind of file.
- **Why a tree, not hover submenus:** a submenu that opens on hover closes when the pointer drifts across a neighbour, cascades off the side of the screen after a few levels, and has no natural place for a filter or for "listing…"; the inline tree is one scrollable column at any depth, works the same with the keyboard and the mouse, and keeps the filter and states in view. Hover submenus were not built.
- **Decision (trips):** Committing a folder more than one level down starts a **trip**: the existing wind-up, flight and landing for each level, the flight 0.8× its usual length (1.2–2.0 s), 0.55 s on each world between hops; a panel at the bottom centre names the destination, "hop n of m", the route with the reached part lit, and **■ Stop (Esc)**. Stop during a wind-up cancels that launch (you stay); during a flight that hop lands and the trip ends. Before each hop the next folder is checked; if it has gone (or the launch is refused) the trip stops where you are and says why. **Shift+Enter / Straight there** warps directly instead. A trip is busy throughout (other travel waits; J waits).
- **Decision (history):** the whole trip is **one step of Backspace** (`PlaceNavigator.MergeLastMoves`): Backspace returns to where the trip began, as it would after any single jump; a stopped trip is one step too. **U** is unchanged: the parent of where you are. Rejected: one entry per hop (Backspace would retrace the hops one by one — the trip's intermediate worlds are a sequence you watched, not places you chose).
- **Decision (dialogs):** every commit (the list, Locations, a favourite, a pad launch, Go to path) closes Open Location, the HUD drawer, the problem list and the planet's list before the wind-up (`IWorldSession.DismissNavigationDialogs`, called from the launch's `CloseOverlays`); the trip panel and the walkthrough card stay, since they guide the sequence. Locations marks the row for where you are ("● … you are here", disabled) and the location you are inside.
- **Decision (places I care about):** pins already existed (P on a folder's card, saved in preferences, marked ★ on gates and signs) but had no route. The favourites section lists them first (a pin not yet in the tree is reached by its path, as Go to path), then every repository already in the tree (a `.git` folder or file, as `WorldCharacter`), found by `RepositoryIndex`, which reads only new nodes, 20,000 per frame, so the list stays responsive while scanning goes on. Each is one warp away. Known folders (Documents, Downloads) are not listed: they are DJ's private paths and were not asked for.
- **Alternatives:** keep the flat list and add a "go deeper" button (two lists to learn); persist repositories seen across sessions (useful at the hub, where nothing is listed yet: not done, as it needs a new preferences field shared with other work).
- **Revisit if:** DJ finds the trip too slow or too fast for long branches (TripFlightScale, TripHopPauseSeconds), wants repositories remembered between sessions, or the tree needs a "find anywhere" that lists (it would have to ask first).

## AD-57 Arrivals, file actions and reading (2026-11-16)

- **Problem:** DJ's playtest of the first public build: the workbench was beyond the launcher rings, out of sight on arrival; the landing view showed little of the planet past the near horizon; a double-click on `FileScape.sln` (its object, and its card on the bench) made the traveller hop and beep instead of opening it, while a folder card navigated; jump and twirl did nothing useful near files; the README preview was raw text.
- **Cause of the double-click defect (traced, then reproduced with `planets-actions` before the fix):** the click resolved correctly (hit test → `ClickedBenchTile` or `Pick` → the file's node), but dispatch mapped a double-click on a *file* to `Activate`/`BenchGoTo` → `Approach`: a hop to the file (the jump sound and animation) and, on landing, an inspect (the beep) and a preview; 0 shell requests. Folders went to `LaunchToSelection`. So one gesture meant "go there" for folders and "walk to it" for files, and nothing ever reached the Open path. Every click is now traced (`[planets] click single|double at (x,y): <hit test> #id → <kind> <name>: <action>`).
- **Decision — one rule (PLANET-43):** for workbench cards, objects on a world, launcher pads and worlds in the sky: **click** selects (the selection card shows Open, Show in Explorer, Go inside); **double-click** goes to a folder or link, and **opens a file** through `IWorldSession.OpenSelection` = `Main.RequestOpen`, the one deliberate Open (Windows association; asks first for code, links and cloud placeholders; the test shell records); **Enter** opens the selection. The bench's own list follows the same rule. The planet's list (J) and Destinations stay the "where is it?" tools: Enter there walks you to a file. A refused open is reported in red for 10 s with the file's name, the reason in plain words and Windows' text and code (`ShellErrors.OpenFailed`); Enter with nothing selected says so.
- **Decision — the twirl's contextual action:** a twirl (the air jump) with a file in focus selects it and shows its preview, as E does (a pile: its list); the prompt names it before you do it ("E · look closer at X · or twirl (Space, Space) to preview"). It never opens: a jump or twirl is movement, and movement must never start an external program. The workbench's spin (README) takes precedence; a twirl with nothing in reach is only a twirl. Reading a file for its preview is the same bounded, local, read-only read as E's (cloud placeholders and reparse points are never read).
- **Decision — arrival (PLANET-42):** on a repository the workbench takes a slot on the **first launcher ring**, the one nearest 1 o'clock (the largest grove grows front left); the ring is laid out for one more pad, so every folder keeps its own pad in name order. Until it is revealed a sign over it says "the workbench · jump on: this project's key files". Plain folders keep the overlook by the landmark. The **arrival framing** is a weight on the chase camera's boom (29° instead of 18°, up to 10 m instead of 7.2 m but never more than 0.6 × the planet's radius, and the aim 2.2 m further ahead), set by a landing, a warp's touchdown and the opening shot, released by a step, a jump or a hop and gone in ≈ 0.7 s; standing still or looking around keeps it. The flight, the launch and the landing blend are unchanged (landings still do not cut: 0.0 % of the screen in the landing frame).
- **Decision — reading (PLANET-44):** `MarkdownText` (Core) converts Markdown to BBCode for the HUD's reading view and the planets' preview: headings (ATX and setext), paragraphs with soft breaks joined, fenced and indented code on a slate panel, quotes, nested bullet, ordered and task lists, tables, rules, code spans, strong, emphasis, strikethrough, links as underlined text with the address muted (never followed), images as "(image: alt)" (never fetched, remote or local). Only its own tags reach the label: every '[' from a file is escaped. Non-Markdown text is plain. The preview's **Read ⤢ (R)** opens the whole file (bounded to 256 KiB / 4,000 lines, read on a worker) in the HUD reading view, centred and at most 1,040 units wide; Esc closes it. No pop-out window (one window keeps Esc, focus and the probes simple).
- **Alternatives:** a double-click that hops *and* opens (two outcomes from one gesture; the hop also moved the camera under the cursor); a twirl that opens with a confirmation (a confirmation dialog appearing mid-jump is exactly the surprise DJ ruled out); moving the bench next to the arrival meadow (no room inside the first ring: the meadow is 4 m, the ring 9 m, the bench 2.2 m); a separate arrival camera (a second camera would need its own hand-over to the follow camera; a weight on the same boom inherits every safety rule); links that open on a deliberate click (possible later; text is the safer default for a README viewer).
- **Evidence:** `planets-actions` (27/0), `planets-arrival` (25/0), `planets-reading` (13/0, on `tools/make-wsl-fixture.sh --readme`), Core `FileActionTests`, `ArrivalCompositionTests`, `MarkdownTextTests`; screenshots in `%LOCALAPPDATA%\FileScape\evidence\2026-11-16-arrivals\`.
- **Revisit if:** DJ finds the arrival view too high on some worlds or the ease-out too quick or slow; wants single-click to approach, or the list's Enter to open files too; wants clickable links or local images in READMEs.

## AD-58 Root landmarks with a developer purpose: solid, a cue, and a small panel (2026-11-16)

- **Problem:** DJ (PLANET-45): "The Windows and WSL root landmarks look like things I should be able to use, especially the terminal prompt on the WSL world. Right now I can walk through them and they do nothing." Make them genuinely useful to a developer, give them a clear cue, make them feel solid without blocking routes, and if a terminal is opened, only on an explicit command and in the intended Windows or WSL location (verified, including a path with spaces).
- **Options brainstormed:**
  1. *Open terminal here* — the action the monolith's prompt promises; the most frequent "I'm here, now let me work" step. Strong.
  2. *Copy the path in the native form* (`C:\…`, `/home/…`) and the other side's (`\\wsl.localhost\Distro\…` for a Linux folder, `/mnt/c/…` for a drive) — every developer translates these by hand. Strong, cheap, safe.
  3. *A few useful places on that filesystem* (WSL: `~`, `/tmp`, `/mnt/c`, `/`; Windows: the drive's root, Home, Desktop, Documents, Downloads from the known-folder map that already ranks prominent folders) — quick travel that respects the root's identity. Useful.
  4. *Open in VS Code / the editor here* — already offered on a project's card (DEV-03); duplicating it on a root, where you rarely want an editor, adds little.
  5. *Show in Explorer* — already on every card.
  6. *Run a command typed into the monolith* — too much power for a playful prop; FileScape never runs anything implicitly (FILE-02).
  7. *Disk usage / free space on the gateway* — informative, not an action; the hub's card can carry it later.
- **Decision:** 1 + 2 + 3 on one small panel, opened by **E** at the landmark. Walking up brightens a cue (rings of light on the ground in the mark's colour, a soft light on its face; faint from the arrival, full within 2.2 m) and the prompt says "E · terminal here · copy path · places". The panel lists numbered rows: **1** Open terminal here, **2** Copy path (native), **3** Copy the other form, then the places; number keys or a click choose; E or Esc closes; walking away closes it. Nothing starts on walking up or on opening the panel. The panel acts on the current folder (on a real root, the root).
  - **Terminal:** Windows folder → `wt.exe -d <dir>` (Windows Terminal's execution alias in `%LOCALAPPDATA%\Microsoft\WindowsApps`, if present; a `;` in the path escaped as `\;`), else `powershell.exe -NoLogo -NoExit -Command Set-Location -LiteralPath '<dir>'` (a single-quoted literal, apostrophes doubled; works for shares, which `cmd` cannot start in). WSL folder → `wsl.exe -d <distro> --cd <linux path>` (the distribution's own login shell in the Linux path; hosted by the default terminal app, Windows Terminal on Windows 11) — not `wt` with a profile, whose name is not known reliably and which re-parses its command line. Started through `IShellActions.Start` (ShellExecuteEx, detached; `--test-shell=record` records the program and its command line and starts nothing). The directory is always its own argument.
  - **Clipboard:** through the session; under the test shell (and for synthetic data) it is recorded, never copied, so a probe cannot overwrite the person's clipboard.
  - **Places:** `~` is resolved only when chosen (the distribution's `DefaultUid` from the Lxss registry key, looked up in its `/etc/passwd`, at most 64 KiB; `/home` otherwise); `/mnt/c` travels to `C:\` itself (listed natively, not through WSL's file server). Travel goes as a typed Ctrl+L path does.
  - **Solid without blocking:** the composer now places the mark (`PlanetComposition.RootMark`, Core `RootMarkLayout`). Its collider is a row of overlapping circles (0.32 m) across its face, 3.6 m tall (beyond a jump and boost), the gateway 3.3 m and the monolith 1.9 m wide. It stands at the spot nearest the original one (4.6 m ahead, 3.6 m left of the arrival) — searched over every bearing, 1° apart — whose solids keep the composer's route half-width (1 m) from the straight walk to every launcher, the way back, the overlook's route and the landmark, stay out of every launcher's clear area (3.3 m), the rings, the meadow, the landmark and the overlook; up to 0.75 m of extra clearance is traded against turning from the original bearing. If nowhere in the meadow's ring is clear (a crowded root: on an Ubuntu root the walks to 21 pads leave no 3 m gap there), it moves out between the first rings (12.5 m on Ubuntu and C:), never across a route. Files and scenery keep out of its base; the ground is level under it. Cost: about 1–5 ms per root composition; other worlds are unchanged (behaviour baseline bit-identical).
- **Not done (this round):** "Open terminal here" for any folder (e.g. on the folder's card) — `TerminalLaunch.For` already works for every folder; wiring it into the card belongs with the file-actions work that is changing the card now, so it is left as a one-line follow-up. A visual of the cursor typing on use; sounds of its own (it uses the existing inspect/select cues).
- **Evidence:** Core `RootMarkTests` (six roots: Ubuntu-like, small Debian, C:, empty D:, a crowded drive, a share; solids in the obstacles; clearance ≥ 0; the straight walk to every launcher never within a body of it; walking into it stops at its face; only roots; placement in front and beside the arrival on small roots, between rings on crowded ones) and `DeveloperActionsTests` (commands, path forms, places, passwd; on Windows, PowerShell and `wsl.exe` started with the app's own argument lists print exactly `…\dir with spaces` and `…\it's here & there`; `CommandLineToArgvW` gives Windows Terminal the folder as one argument). `planets-landmarks` in-app probe (46 checks, screenshots).
- **Revisit if:** DJ wants the terminal in Windows Terminal for WSL too (a `wt.exe -p <profile>` needs the profile's name), more or other places, a different key than E, or the general "terminal here" on every folder.

## AD-59 Hazard Worlds: islands and bridges round the destinations (2026-11-16; amends AD-53)

- **Problem:** DJ played the first public playtest build (PLANET-46): "On Program Files, I could hear that the place was different, but the terrain was too safe and the black hole was out of sight. I want to land and immediately see the black hole, the missing surface, and the route I could take." Irregular shells with large missing regions (about a third walkable on some worlds), narrow passages with a drop on both sides, a few jumps worth lining up; danger close, but a player who learns the route reaches useful places reliably; edges legible at walking pace; arrival on solid ground; the spiral fall, return to the parent and immediate retry kept; terrain designed around reachable destinations; crowded worlds handled; direct navigation and every file action still there. Player-facing name: **Hazard World** (was "broken world").
- **Decision — the shell is what is left, not what is cut out.** AD-53 broke a few holes out of a whole planet and kept the route 3 m from them; now the solid ground is the union of jagged **islands** (Core `ShellLand`: one per destination — the arrival, the way back, every launcher with its return stand point, the landmark with the overlook and its stones, a plateau per grove of files — each keeping `HazardTerrain.EdgeMarginMetres` (0.8 m) of ground beyond what it carries) and narrow **bridges** between their waypoints (`ShellSpan`: a strip along the great circle, at least `MinBridgeWidthMetres` (1.8 m) wide, ragged edges that only widen it by up to 0.3 m; the way back's bridge 3 m, the ridge to the landmark 2 m, others 1.8–2.6 m by seed), with **gaps** cut clean across one to three of them (1.4–2.2 m; at least 1.5 m of bridge either side). Everything else is open sky. `HazardTerrain.Design` (deterministic from the folder name's seed) makes the islands, places the file plateaus nearest the arrival that fit, adds outcrops (3.2–5.8 m, about a bridge's length from the existing ground) until the islands cover a target share (30–42 % by seed; crowded worlds end up a little higher), and joins everything in a spanning tree from the arrival (the way back and the ridge first, then Prim's cheapest bridge; a bridge whose straight way crosses the opening ahead or a jet's opening costs 3× its length). The ridge gets the first gap (the jump you see from the arrival, on the way to the overlook), more by seed, never the way back. A gap that another bridge or island covers is closed again. `BrokenShell.Route(from, to)` walks the tree (Dijkstra, a gap counting 8 m extra) and returns waypoints that keep to solid ground: inside an island through its centre (islands are star-shaped), between islands along the bridges' centre lines.
- **Decision — what you see on landing.** Straight ahead of the arrival the ground ends a few steps away (the arrival meadow is 3 m on a Hazard World, its island's nearest tooth 3.8 m out); the black hole's **jet** (a camera-facing beam along its spin axis, `hazard_jet.gdshader`, 3.2 planet radii long) climbs out of that opening (8° left of ahead, 3.5 m beyond the arrival island's farthest tooth, 3.5 m of open sky round it; the opposite jet is drawn when its side is open too); the ridge runs 32° right of ahead across the opening to the landmark (23 m out), with its gap; launcher pads never stand within ±50° of straight ahead. The black hole's disc lies across the jet axis and the fall spirals round the same axis. On landing at the arrival (not beside a launcher) the camera turns to the opening for 1.8 s unless you look round yourself (after a flight it faced wherever the flight came from). On a Hazard World the chase camera rides 16° higher and 2.2 m further back (the edge peek adds on top, capped at 62°), so the islands, the bridges' edges and the open sky between them read at walking pace. The geometry rules out seeing the black hole's core from the ground except straight down (every line of sight from a few metres up passes the centre at 15 m or more), which is why the jet carries "the black hole is right there".
- **Decision — edges at walking pace.** The ground is cut along the same `EdgeMetres` the walker falls at (finer ground on Hazard Worlds: subdivision 6, ≈ 0.3 m edges; previews 4), rim walls and a glowing edge line follow the cut's outline (made on the ground worker), the ground is scorched within 1.6 m of an edge, the route is a faintly lit stripe along every bridge's centre line ("follow the lit paths"), rock crumbs drop off the edge nearest you while the warning is up, and the AD-53 warning (vignette, camera peek, sound hook) stays. Hops (Approach, Home, the list) follow the route's waypoints, airborne over any gap, up to 1.8 s.
- **Decision — crowded worlds.** A Hazard World holds at most 10 launcher pads on at most three rings (15 m, 26 m, 37 m from the arrival, pads ≥ 12 m apart, the way back at 6 o'clock), and shows at most 24 files one by one (the rest in piles). Further child folders join the belt as moons: click and G, or the list (J). J, U, Backspace, G and the overview work as anywhere. The sample's crowded case is **Debris Field** (inside Event Horizon, 16 child worlds: 10 pads and 6 moons); falling off it returns you to Event Horizon beside its launcher, on solid ground.
- **Why not weaken the classification:** the crowded fixture is a sample folder with the existing `.filescape-hazard` marker, inside the marked sample; HazardRules is unchanged.
- **Evidence:** Core `HazardWorldTests` for six worlds (the sample, few, crowded with 40 folders and 30 files, files only, empty, Debris Field): 31–37 % walkable; every destination's content inside its island with the margin; the drop straight ahead within 6.5 m and open sky beside the ridge; both jet openings open sky; ≤ 10 pads and every child a pad, a moon or counted; the route from the arrival to every launcher and the overlook keeps at least half a bridge's width inside the ground on walking legs, crosses only gaps of 1.4–2.2 m on jump legs, and the way back has no jump; every bridge is ≥ 1.8 m wide outside its gaps; the real `SphereWalker` follows the route to every launcher walking and running, jumping each gap; every gap is cleared both ways, walking and running, with Space pressed 0.25, 0.7 or 1.3 m before the edge; falls only off the shell; determinism. `SampleSkyTests`: the written sample classifies Event Horizon and Debris Field and nothing else. In-app `planets-hazard` (see implementation status).
- **Alternatives:** larger holes in a whole planet (the first idea; the route stays wide and safe, and the danger stays far); all launchers on the ground of a crowded Hazard World (the islands merge into a continent and the danger disappears); straight-line hops over open sky (the old rule kept hops over ground; following the route keeps that); moving the black hole off-centre so it shows from the surface (the fall and the disc are built round the centre).
- **Known gaps / unknown:** how it feels: bridge widths, gap lengths, the camera's extra height and the arrival look are by eye and need DJ's hands; the warning sound now plays on most bridges (the edge is never far), which the music stream may want to temper; the real-input probe's route-following was rewritten without being run (the coordinator runs it).
- **Revisit if:** DJ finds the bridges too narrow or too forgiving, the jumps too hard, the arrival too busy, or a real system folder's world too crowded or too sparse.

## AD-60 Sound off in one click; drama in the music; a frenetic hazard score, the old one kept (amends AD-52) (2026-11-16)

- **Problem:** DJ's second playtest brief (PLANET-47, PLANET-48): "a visible one-action control to silence the game while it stays open, and make it easy to restore sound", reusing the N shortcut if appropriate, the muted state obvious, travel and hazard cues respecting it. For the music: keep the catchy melody, the scales and the instruments ("the different scales and instrumentation give the worlds real character"), and add phrase-level drama (breaks, build-ups, drops, climaxes) where each piece suits it; a deeper drum-and-bass drop, heard on headphones and ordinary speakers; save the hazard tune ("a scale and tonality I love") and write a more frenetic, tense hazard score with a feeling of "get out of here".
- **Decision — silence (PLANET-47):**
  - **One control, two ways in.** A toolbar button after Help: **"🔊 Sound"** while the sound is on, **amber "🔇 Sound off"** while it is off; the tooltip names N. **N** (unchanged key, on the surface and in the overview) now asks the session to toggle (`PlanetWorld.SoundToggleRequested` → `Main.ToggleSound`), so the key and the button are one state. A toast says how to undo it ("Click 🔇 Sound off at the top, or press N…").
  - **What it silences:** `SkyAudio.SetMuted` → `SkySynth.Mute`, the master gain (15 ms ramp). Every sound FileScape makes goes through it: music, launch, flight, landing, arrival, select, inspect, chimes, the hazard warning, the fall and the return. The synth keeps running (the music's clock and the cues' state go on), so turning the sound back on resumes where the music would be. Measured in-app (below), not assumed.
  - **Saved** in Preferences (`soundMuted`), because someone who silenced FileScape (at work, late at night) should not be surprised by music at the next launch. A restored mute is made obvious: the amber button from the first frame and a toast two seconds after start ("Sound is off (as you left it)…"). Automation still reads and writes no preferences unless `--prefs` names a file.
  - **Scope: the planets** (surface and overview). The legacy views make no sound; their top bar is fuller (with the button, Places overflowed the 1280-unit bar by 9 px at 125 %), so there the button is hidden and the state simply carries across (`Hud.SetExperience`). The sound word was added after the first screenshot: the speaker glyph alone rendered too small to read.
  - **Instrumentation:** `SkyAudio.OutputPeak` (every sample pushed to Godot) and the Master bus's own peak meter (`AudioServer`), read by the `planets-sound` probe.
- **Decision — drama per arrangement (PLANET-48), within the existing 16-bar cycle (theme bars 0–7, the world's own material 8–15), melodies, scales and instruments unchanged:**
  - **Drum and bass (band 4):** bars 8–9 a real **breakdown** on every visit (drums and reese out; the strings, a held sub, soft air, the band motif), bars 10–11 a **build** (a snare roll from the pulse's eighths through sixteenths to 32nds, a kick on the beat in bar 10, a riser, the reese climbing an octave, one pulse-eighth of silence), bar 12 the **drop**: a **sub drop** (a sine diving an octave from the key's root an octave below the bass, to ≈ 30–45 Hz, with a saw an octave above diving with it so small speakers hear the dive) and an impact, then to the cycle's end the full break with ghosts and a **heavy reese** (filter 400 Hz → 1.6 kHz instead of 260 → 650, a third saw an octave up, a louder sub); the theme's return lands on a crash. The theme's bars are unchanged.
  - **Festival (band 2):** the drop's last phrase is the **climax** (a crash on bar 6 and the line doubled an octave up on a supersaw; from bar 4 on a revisit), then a **stop**: the beat drops out under the lead's last note and only the snare fill leads into the breakdown. The breakdown, build and drop were already there (AD-52).
  - **Orchestra (band 3):** the timpani roll into the bar-14 climax gains the upper strings' measured **tremolo**, both swelling, the cymbal choked; a **general pause** (the last two sixteenths of bar 13: the sections stop); then the **climax**: the horn call (mi sol do') again, an octave up on the violins over the horns, fortissimo, with the tonic under the timpani.
  - **Not changed:** the hub (its welcome stays gentle), the roots, repositories and band 1 (the behaviour baseline is identical, audio hashes included; bands 2 and 4's baseline windows are only the first 6 s, before any drama).
- **Decision — the hazard score:**
  - **The AD-52 score is kept unchanged as `MusicIdentity.Abyss`** (appended to the enum, so no identity is renumbered), for a future world type. Proposed: a **sealed world**, a folder FileScape cannot list (access denied), whose uneasy calm suits a place you cannot enter. No world kind maps to it yet; `SkySynth.SetWorld(theme, character, visits, score)` plays it (samples, tests, and the future world). **Demonstrated identical:** the hazard sample's script played with the abyss is, sample for sample, the "before" render of the old hazard score, on Linux and on Windows (SHA-256 of the 16-bit data; `The_abyss_is_the_AD52_hazard_score_unchanged`).
  - **The hazard world's new score ("Escape", SkySynth.Escape.cs):** the same **phrygian** key and chords (i ♭II i ♭VI), the same **beating drone** (now held four bars instead of restruck per chord) and dark strings, the same **unstable lead** (two bells 18 cents apart, the twin wobbling; a short release at this tempo) and the theme's head turned downwards, but the theme at **full tempo, 152 BPM** (the old one's was 70) over a **racing heartbeat** (lub-dub on every beat, pumping) and a **♭2 bass ostinato** in sixteenths (root, root, ♭2, root), ticking metal in sixteenths and 32nds. Per cycle: bars 0–3 the theme over the drive; 4–7 the **climb** (snare backbeat, taiko-like toms, a string tremolo, a crash on bar 6); 8–11 the **chase** (the head turned downwards in quick eighths, each statement a step higher); 12–13 the **break** (the floor drops away: no bass or drums, the heart slows to half, the ticks race louder, one long wobbling note, a reversed swell); 14–15 the **build** (a snare roll from eighths to 32nds, toms, the ostinato climbing the scale, a riser, the last eighth silent); the theme returns on a crash and an impact. The arrival is the old gong and boom with a crash.
  - **Cues still read under it:** the edge warning held at the edge stands ≥ 7.1 dB over the ducked music in every bar of the hazard cycle (worst bar 0; the climb and build 9–11 dB) and ≥ 7.4 dB over the abyss; the fall and return ≥ 7.3 dB at the hazard's loudest bars; the return ≥ 5.5 dB at the drum-and-bass drop (the sub drop was lowered from 0.2 to 0.13 to keep it above 4 dB).
- **Mix and measurements (SkySynthTests; Linux, 2026-11-16, under the load of parallel agents):** integrated loudness hazard −27.1 LUFS (Level 0.95), abyss −27.3, drum and bass −27.7, festival −27.1, orchestra −27.2, against −26.2…−28.3 for the unchanged identities (all within the 2 dB test); music peaks ≤ 0.37; hazard music voices mean 13.1, max 25 (the AD-52 range was 10–14); render cost per second of audio with a flight build, a fall, the warning and six effects: hazard 48 ms, orchestra 42, festival 41, drum and bass 40 (bound 60; these machines were loaded, last round's 26–27 ms were measured alone). **Drama, per bar (LUFS):** drum and bass theme ≈ −26, breakdown −30.2, build −29.5 → −28.4, drop −24.2 … −25.0; orchestra bar 14 −27.0 against −29.2…−31.0 before it (was −33.2 before AD-60: the climax had no weight), the general pause ≥ 8 dB below the bar before it; festival the stop ≥ 3 dB below the same beat of bar 6; hazard break −31.1 against the climb's −25.9…−26.7. **The drop on small speakers:** in the drum-and-bass sample, the drop's first bar against the same bar before AD-60: sub (30–60 Hz) −36.4 vs −38.2 dBFS, low harmonics (100–300 Hz) −33.8 vs −36.0, everything above 200 Hz (what a laptop can play) −33.8 vs −35.5, loudness −24.4 vs −26.2 LUFS; from the breakdown to the drop +5.7 LU (K-weighted loudness, which itself discounts the sub), so the drop is heard on speakers that cannot play the sub.
- **Alternatives:** a mute indicator separate from the button (one more chip on a full bar; the amber button already is one), a session-only mute (a restored mute surprised no one we could test, and the toast makes it obvious), the button in every view (the legacy views are silent and Places would overflow at 125 %); for the hazard, a half-time drum-and-bass treatment (too close to the underglow), modulating up a step per cycle (would move the theme out of its key and break recognisability), a separate "panic" layer driven by the edge's nearness (needs new app calls; the warning already does that job).
- **Known limits:** the sound button's glyphs come from the system's emoji font (Windows: Segoe UI Emoji; monochrome at this size); toasts overlap the overview's own hint line at the bottom while both show. **Unknown:** nobody has listened; every musical judgement here is a measurement. DJ judges by ear.
- **Revisit if:** DJ prefers the mute per session, the button in the legacy views, a different hazard tempo (152 is one Style number), the climb or break placed elsewhere in the cycle, or a world type for the abyss.

## AD-61 Next worlds: photos and videos, documents, Downloads (proposed, 2026-11-16)

- **Status:** proposed. Nothing is built yet. The full sketch is in [`next-worlds.md`](next-worlds.md).
- **Problem:** DJ asked how photos and videos, documents and Downloads could become distinct places. For each one he
  wants to know how FileScape would recognise it, what would make it feel different, and one useful thing to do there.
  He also asked about mixed folders and whether users could override the classification.
- **Proposal:** three new kinds of place, each in the `WorldCharacter` layer, above the existing `PlanetTheme`:
  - the **Gallery**: a date-ordered gallery path and a contact sheet for each month;
  - the **Reading Room**: a reading nook of recent documents, with version stacks;
  - the **Arrivals Port**: parcels in age rings and an arrivals board with Reveal in Explorer.
- **How they are recognised:** by known-folder path, by names, and by the extension mix plus sizes. Names and metadata
  only, never inside a repository, and never over a hazard or a root.
- **Mixed folders and overrides:** a mixed folder gets one identity per world, and its large groves become districts.
  The person can override the classification for each folder. The choice is saved in Preferences, and a name-only
  marker file can come later; FileScape never creates a marker itself.
- **First prototype:** the **Arrivals Port** (Downloads). It is useful, has almost no false positives, and needs only
  names, sizes and dates. The draft acceptance criteria are NW-1..NW-6 in the sketch.
- **Open decision for DJ:** the Gallery has to read your pictures while you walk, so it needs a per-folder yes.
  Thumbnails would stay in memory only. Today pictures are read only when you preview one (FILE-02).
- **Alternatives considered:** a "Move to Recycle Bin" action for old downloads (rejected for now: it would break the
  read-only promise, and Reveal gets you to the same place); recognising Downloads from the mix alone (it would be
  wrong too often).
- **Revisit if:** DJ ranks the Gallery first, or wants these worlds as themes rather than identities.

## AD-62 The keyboard belongs to what you are typing or reading; links go only where you click (amends AD-57) (2026-11-17)

- **Problem:** DJ's playtest of the second iteration (PLANET-51..53): typing a name after J "interpreted a letter as a
  shortcut and changed what I was doing"; the wheel over the README reader zoomed the game; Enter in the reader "showed
  nothing"; Markdown links were inert text (AD-57 kept them so).
- **What was reproduced (in-app, injected key events with key code, physical code and character, before any fix):**
  letters typed right after J, after the ☰ button, after travel, in the overview, during a flight and with the pointer
  resting over the list all reached the filter. Keys typed *in the results* did not: Backspace (History back), Home
  (back to the arrival), PageUp (Up: it flew out of the Starter Sky with the list open) and Tab (switch view) went to
  the game's shortcuts, because the tree does not use them and the game takes every key no control takes. Separately,
  Enter from the filter went to a row left selected from an earlier filter (a trip to Constellations for "Lantern").
  The reader's panel was not in the HUD's blocking rectangles, so the planets' pre-GUI input took the wheel (camera
  7.20 → 9.58 m, text unscrolled); its toasts were added before it and drawn behind it. DJ's exact keystrokes on the real
  keyboard were not reproduced; `os-planets-probe.ps1 -InputFocus` types as he did and is the check.
- **Decision — the open list owns typed keys:** while Destinations is open, every key press that no control used goes
  to the list before any shortcut (`PlanetWorld.PanelKey` → `ListKey`): a character goes into the filter (the filter
  takes the keyboard back), Backspace edits it, ↓ or Tab moves into the results, Enter goes, and the other keys
  (PageUp, Home, arrows…) do nothing. J (to close it when the filter does not have the keyboard), Esc, Ctrl/Alt chords and
  the F keys still reach the game. In the results Backspace edits the filter, Tab returns to it, Home/End/Page keys move
  within them. The filter enters Godot's edit mode explicitly when it takes the keyboard. A filter change lets go of the
  chosen row, so Enter from the filter goes to the best match for what is typed now.
- **Decision — the reader owns the pointer and the keys over it:** the reading view is a blocking rectangle (wheel and
  clicks are its own), its text takes the keyboard (arrows, Page keys, Home/End scroll), and while it is open no other key
  reaches a shortcut: Enter opens the file with Windows, R or Esc closes it. Toasts are moved above it, and a status line
  under the text repeats what Open or a link did. The Expand icon sits in the preview's upper-right corner; expanding
  replaces the preview, closing brings it back while you are still on that file.
- **Decision — usable links (amends AD-57):** `MarkdownText.Render` emits `[url=N]` where N is only the link's index (no
  text from the file ever enters a tag) and lists each heading's GitHub anchor with the paragraph it starts (as
  RichTextLabel counts paragraphs: a table is one). `MarkdownLinks.Resolve` decides what a click means: an anchor scrolls;
  a relative path (percent-decoded, `<with spaces>`, `../` never above the drive or share, a leading `/` from the
  repository root) is checked on disk only on the click, then read in the reader (text), gone to (a folder), or its
  folder with it selected (another file); http, https and mailto go to the shell's Open (the browser or mail program;
  recorded under `--test-shell=record`); every other scheme is refused with the reason. Images stay alt text, remote or
  local: nothing is fetched. The inert `ToBbcode` is unchanged for any view that does not follow links.
- **Decision — the opened app may come forward:** before ShellExecuteEx, when FileScape is the foreground app (a person
  just pressed Enter or clicked Open), it calls `AllowSetForegroundWindow(ASFW_ANY)`, passing on its own right to take
  the foreground: an already-running viewer that receives the file can come forward instead of flashing on the taskbar.
  It never takes the foreground from another app; the right ends at the next input. Alt+Tab returns to FileScape.
- **Alternatives:** blocking shortcuts by checking `GuiGetFocusOwner` in the shortcut handler (a click on the world while
  the list is open would let letters through again); a modal list (DJ likes looking around with it open); following
  links on hover or showing previews of web pages (fetches); `[url=<address>]` tags (the address would be markup from the
  file); waiting for the launched process and calling SetForegroundWindow on it (Windows refuses it from a background
  process, and it does not apply to a viewer that was already open).
- **Known limits:** keys are judged as Godot delivers them; DJ's own keyboard sequence is checked by the real-input mode,
  not yet run. An anchor in a block quote's heading is not listed; HTML anchors (`<a name>`) are not either. The foreground
  hand-over is at Windows' discretion (focus assist, a full-screen app) and was not exercised here.
- **Evidence:** `planets-destinations-keys`, `planets-reader`, Core `MarkdownLinksTests`; the reproductions in
  `docs/evidence/2026-11-17-input/`.
- **Revisit if:** DJ wants J to type a "j" even when the filter has lost the keyboard, a web link to ask first, local
  images shown, or the preview kept beside the reader.

## AD-63 Landmarks you use by playing: the bell rings on a twirl; a root's twirl arms, Enter commits (2026-11-17)

- **Problem:** DJ's notes after completing Learn to play (PLANET-54..57). The bell tower's platform looked walkable but
  one solid cylinder blocked the whole structure; "use this as the landmark interaction … jump and twirl under it to ring
  it … it can simply be fun". At the Windows and WSL root landmarks the prompt "E · terminal here · copy path · places"
  was compressed; "explore whether a twirl at the landmark could give me a quick route to Terminal here or Show in
  Explorer. If you make the gesture open an external app directly, give it a clear target and cue before I commit.
  Ordinary jumping nearby should feel like ordinary jumping." At the overlook, nothing said that E at the telescope
  opens the list. In the overview, nothing taught the mouse wheel.
- **The bell tower (decided):** its collision is its drawing (Core `BellTower`, the one set of measurements the art draws
  from): a 2.3 m base you step onto (0.30 m), four pillars solid at their 0.28 m collars, nothing over the axis; level
  ground under the base. The bell was made smaller and higher (mouth 1.2 m, rim 4.65 m): a twirl from the base peaks at
  ≈ 2.9 m and the traveller is ≈ 1.45 m tall with its antenna, so its head stayed in the old 4.15 m bell. **A twirl (the
  air jump) while under the bell's mouth rings it**: the bell swings across your view (≈ 14°, dying away over ≈ 3 s) and
  `SkySynth.BellToll` strikes a big bell — hum, prime, third, fifth and octave of the world's scale as FM bells — on the
  music's next 32nd note, answered one beat later a fifth up (the swing back), ringing for seconds while the music
  ducks to half. An ordinary jump under it, or a twirl beside the tower, is only a jump; ringing opens nothing. Why the
  twirl and not a jump: a jump is how you move; the twirl is the deliberate move FileScape already gives meaning to
  (the bench's spin, a file's preview), and "jump and twirl" is what DJ asked for.
- **The root landmarks (decided):** the prompt is **"E · Show location actions"**; its second line names the mark and the
  path and offers the twirl. The panel's rows each say what they do on a line under them ("Opens Windows Terminal in …
  (a new window)", "Opens File Explorer at …", "Puts … on the clipboard"; the places share "choosing one flies you
  there"). **Show in Explorer** is added as row 2 (it was only on the folder's card): the folder opened with Windows
  (ShellExecute's default verb on a folder), not `explorer.exe <path>`, whose argument parser treats commas as switches.
- **The twirl's quick route — options:**
  1. *Twirl at the mark starts the terminal directly.* Fastest, but an external program would start from movement
     (AD-57 forbids it), and a playful twirl beside a monolith — the very thing the bell teaches — would open windows.
  2. *Two launch pads in front of the mark ("Terminal", "Explorer"), a twirl on one launches it, the pad's sign the
     cue.* A clear target, but still a commit by movement; two more solids and signs to place clear of routes on every
     root; and a twirl is easy to do by accident while playing near a pad.
  3. *Twirl arms, a key commits (chosen).* Within reach of the mark (2.2 m of its face, where the prompt already shows),
     a twirl opens the panel with **Open terminal here** armed — a lime border and "▶ … · Enter" on the row, and the
     prompt reads "Enter · Open terminal here · twirl again · Show in Explorer · E · close"; another twirl arms Show in
     Explorer (and round again); **Enter** runs the armed row. The target and what Enter will do are on screen before
     you commit; the commit is a key, never movement; an ordinary jump does nothing; beyond reach a twirl is only a
     twirl; walking away, E or Esc disarms. It saves the reading (twirl, Enter) without making movement launch anything.
  4. *No twirl at the mark.* Simplest; but DJ asked for the quick route and the panel alone is already E.
- **Telescope (decided):** walking towards the overlook (from ≈ 7 m off its edge) a pinned sign in the sign layer reads
  "E · Telescope / this world's files and destinations" and a soft light comes up on the telescope; the prompt says
  "Up the steps to the telescope: E there lists this world's files and destinations". Walking up, the chase camera puts
  the platform's top near the top of the view, where the HUD is (a sign over the telescope was dropped for want of
  room), so the sign is pinned to the platform's wall on your side; on the platform it stands over the telescope. A
  3D label was tried first: it sat under the breadcrumb bar. At the bell tower (which stands beside the overlook) the
  bell's prompt is the one cue.
- **The overview's wheel (decided):** a 14th walkthrough step, "5 · Zoom out", with the wheel's key cap, between looking
  round the overview and Esc. It completes when the camera is ≥ 3× as far out as at its start, has eased there, and
  ≥ 10 more worlds are in view: from Music Box at 1600 × 900 that is 12 notches (30 → 151 m, 2 → 16 worlds), where the
  Starter Sky's neighbouring systems come in; fewer notches showed only the next world along. Part way the card says
  "Keep scrolling ↓ · N worlds in view". The wheel's step (×1.18 a notch) is unchanged.
- **Evidence:** Core (`PlanetComposerTests` bell tower: walked under, standing at 0.30 m, twirl clear of the mouth, each
  pillar solid and no wider, everything reachable, other landmarks unchanged; `SkySynthTests.Bell`: grid, key, answer,
  tail, ≥ 4 dB over each world's music, 20 tolls within budget); behaviour baseline identical (no Chime world in it).
  `planets-lookouts` 28/0, `planets-landmarks` 69/0, `planets-tutorial` 36/0, `planets-tutorial-cues` 55/0.
- **Known limits:** twelve notches is a fair amount of scrolling; the toll is judged by measurement, not yet by ear;
  only the bell tower has an interaction of its own (the other landmarks are still silhouettes); real input not run
  here (`os-planets-probe.ps1 -Landmarks` is ready).
- **Revisit if:** DJ finds the zoom step long (raise the wheel's step, or let the step finish on fewer worlds), wants
  the twirl to launch directly (option 2, with pads), or wants landmark interactions on the other themes.

## AD-64 Near and far names, a little more relief, a sky with a little motion (2026-11-17)

- **Problem:** DJ (PLANET-58..60): when a nearby launcher and a farther one line up on a crowded world, help me see
  which is closer (try a nearby name or marker that grows slightly and smoothly as I approach, with a cap), keeping
  names attached, legible and selectable; a little more seeded relief on ordinary planets, small and large, with the
  mesh, footing and launchers in agreement; keep the sky, try an occasional subtle shooting star, and try an extremely
  slow apparent rotation, kept only if it feels good standing and in flight.
- **Near and far (reproduced first, on a fixture shaped like a distribution root, `tools/make-wsl-fixture.sh
  --root-like`):** from the arrival camera "etc" (14.5 m) and "var" (22.6 m) lined up to within 0 px, and var's name
  vanished: etc's name with its detail line was taller than the one-slot climb (PinnedSlots) allowed; a click on a name
  selected nothing (over sky) or the pad behind it. **Decision:** a pinned name's size follows the camera distance
  smoothly instead of in bands: 14 px at 30 m and beyond, smoothstep up to a cap of **19 px at 8 m** (the chase camera
  is ≈ 8 m from a pad you stand on), e.g. 14.7 px at 25 m, 16.5 px at 19 m, 18.3 px at 13 m; a focused name is at least
  18 px (as before); characters kept grow 16 → 26. Drawn at a whole font size scaled by the remainder (±3 %), so it
  never jumps a pixel at a band edge. Its anchor dot grows 3 → 4.5 px and its stem 1.5 → 2.25 px. Far names are drawn
  first, so a near name covers a far one, and a far stem is cut where a nearer name covers it (the boxes are
  see-through; a stem crossing a name's text read as belonging to it). A pinned name whose own slots are taken climbs
  to just above the names in its way (at most 3 climbs, a stem of at most 150 px), still straight above its pad. A
  click on a name selects what it names (a double-click acts on it as on the pad). Places and the aerial view keep their
  bands (not pinned). **Alternatives considered, not built:** dimming the far name (risks reading as disabled); a
  distance in metres in the detail line (more text in a busy view); scaling the 3D pads (they already shrink with
  distance; what went wrong was the names).
- **Relief:** ordinary worlds' hills are 0.75–1.1 m on the smallest world (were 0.6–0.9 m), growing linearly to ×1.6
  on the largest (34 m: 1.2–1.76 m), drawn `PlanetRelief.Rolling`: the same seven hills (so a world still looks like
  itself), hollows a third as deep as a hill is high (were half), three broad low rises added after them (34°–54° wide,
  0.3–0.6 of the amplitude), tops eased under the amplitude by tanh instead of clipped. Measuring first showed that the
  steepest ground and the largest gaps between the drawn and the walked ground were never the hills (3–6°) but the
  rims of flat spots, where the minimum of two fades leaves a crease (classic: slopes 12–18°, the app's triangles up to
  19 cm off the walked ground). Rolling multiplies the fades (smooth where they meet) and makes them 5 m long per metre
  of relief (at least 3.5 m), which keeps a rim under ≈ 17°. Measured on 12 names × a small (≈ 21 m) and a large
  (≈ 33 m) world: steepest 18.0° (design limit `PlanetShape.MaxWalkSlopeDegrees` = 20°: the walker itself has no slope
  limit), triangles within 6.9 cm, pads and the arrival exactly level, return points ≤ 2.4°. Hazard Worlds keep the
  classic hills (their islands were designed on them, AD-59). `--test-relief=classic` composes every world the old way
  for before/after screenshots in one build. **Alternatives:** more hills (changes every world's look); a larger
  amplitude without longer fades (rims reached 21–23°).
- **Sky — shooting stars: kept.** One at a time, every 40–90 s (uniformly random), lasting 0.8 s, travelling 14° with a
  6° trail, its head no brighter than the brightest stars (under the glow threshold most of its life), starting in the
  upper part of the view (0.15–0.85 across, 0.08–0.4 down) on open sky and falling on a 20–55° slant; none in the first
  10 s after a landing (travel resets that count) and none in the overview. Judged on
  stills: visible if you are looking, gone before you could turn to it, standing or in flight (in flight, over the
  bright nebulae, it is fainter still). DJ judges the motion in the clip.
- **Sky — rotation: kept, at 1°/min** (a full turn in 6 hours; the real night sky turns 0.25°/min), about an axis 49°
  from the arrival's zenith, so stars rise and set on a slant instead of sliding sideways as a camera's yaw would move
  them. The celestial sphere turns (stars, nebulae, galaxies, the commit graph); the gradient, the hub's dawn, the
  aurora, panes and scanlines stay fixed to the world. **Why kept:** at the walking camera's 62° view a star moves at
  most 0.24 px/s on a 900 px tall window (0.19 px/s at 720 px): ≈ 0.5 arcmin/s at a desk, under the 1–2 arcmin/s at which
  slow motion is noticed against a fixed horizon, so standing still it cannot read as drift or unsteadiness, and in
  flight the camera's own motion is hundreds of times faster. What it adds is a sky that has moved on when you look
  back after a few minutes (3° in 3 minutes: see the stills). A faster rate was not tried in the app: by the same
  arithmetic 4°/min (≈ 1 px/s) is at the edge of noticing and could read as the camera creeping. **Revisit if:** DJ notices it as drift, or
  wants it visible (then a faster rate, but only in the overview or in flight).
- **Evidence:** `planets-near-far` (root-like fixture and Constellations; before/after and close-ups in
  `%LOCALAPPDATA%\FileScape\evidence\2026-11-17-nearfar\`), `PlanetReliefTests`, `planets-relief`
  (`…\2026-11-17-relief\`), `planets-sky` and the clip (`…\2026-11-17-sky\`).

## AD-65 Hazard World gaps in three classes, a recovery twirl, broken outlines (2026-11-17; amends AD-59)

- **Problem:** DJ (2026-11-17): "Some gaps look jumpable from the takeoff edge even though I can't make them. Play the
  routes and distinguish gaps a normal jump can clear, gaps that reward an advanced move, and gaps that are there to
  steer me toward a bridge … Try a late recovery twirl … a short, bounded window … Also try a deliberate double jump or
  improved jump-and-twirl timing … The broken ground still looks a little like a graph of pads and bridges."
- **Measured first (Core, the real `SphereWalker`, a hazard-sized 22 m planet, over a clean gap):** at walking pace a
  plain jump clears 3.9 m wherever Space is pressed (0.25–1.3 m before the edge, held), 5.1–5.2 m at best (a 0.08 s tap
  3.5 m); running 8.1 m dependably, 9.3–9.4 m at best. The twirl (the air jump, 7.1 m/s up, whatever the timing) carried
  a walking jump to 10.4 m when held and pressed late, 5.4–7.6 m with taps. The old terrain's islands stood ≈ 5 m apart
  (outcrops were placed a "bridge's length" away), so open sky of 2.6–10 m between pieces with a long walk round was
  everywhere: a ray audit from every edge found 397–1473 such rays per world (the 5.2–7.0 m "looks jumpable" band
  46–220).
- **Decision — three classes, sized by that envelope** (`HazardTerrain`; checked against the walker in
  `HazardMovementTests`):
  - **Steps** (`ShellGapClass.Plain`): 1.4–2.2 m on the tree's bridges, their ends jagged (up to +0.2 m each,
    `ShellSpan.GapJagMetres`), and the bridge widens 0.6 m each side for 1.8 m either side of the gap (a ledge), drawn a
    pale lip colour. Any plain jump clears one.
  - **Leaps** (`ShellGapClass.Advanced`): 6.9–7.2 m (6.7–7.0 before the real-input tuning below), only on an extra bridge where the walk round is at least 1.6× its
    straight way plus 6 m (`ShellLink.Advanced`: the route never takes one, so every launcher stays reachable by walking
    and steps), with 4 m of open sky round its stretch. The bridge widens 1 m each side by the gap; the last metre before
    each end is a glowing amber take-off board with two pulsing amber posts at its corners. No plain jump at walking pace
    makes one, with or without a recovery (6.4 m); a twirl at the top of the jump does (≥ 15 of 18 attempts; a running
    jump also does). One or two per world where they fit cleanly (none where a leap would leave a misleading gap beside
    it); the sample's Debris Field has one (6.97 m).
  - **Hops and chasms:** wherever two pieces of ground (`ShellSeparations`: islands, and bridge stretches between gaps)
    face each other across open sky with a long walk round (more than twice the straight way plus 4 m, counting hops),
    the sky is a hop (≤ `HopMaxMetres` 2.6 m) or a chasm (≥ `SteeringMinMetres` 11 m, beyond everything at walking pace:
    10.2 m). Between, the two pieces grow spurs towards each other (`ShellSpur`, `SpanSpur`: a broad tongue of rock,
    flat-topped, star-shaped) until the gap is a hop of 1.5–2.1 m; where one side is hemmed in the other grows the whole
    way; where both are, a rock arch joins them (ground, not a route). The black hole's window (the opening ahead and the
    jets' clearings) is exempt: nothing grows into it. Up to six rounds.
- **Decision — the twirl over open sky** (`HazardMoveParams`; whole planets and solid ground unchanged):
  - **Timed twirl:** a twirl at the top of the jump (vertical speed within ±3 m/s) leans forward 2.2 m/s instead of
    1 m/s, up to the run speed. Walking jump and twirl: 10.2 m at best.
  - **Recovery twirl:** falling faster than 4 m/s, or with the feet below a rim's level, the twirl is the catch. With the
    twirl unused, within 0.3 s of dropping below the rim and no deeper than 1.1 m, and ground within 1.5 m (1.1 m until the real-keyboard run, see below): a lift to
    0.35 m above the rim (at least 3 m/s, full gravity whether Space is held or tapped) and a carry towards that ground,
    aiming 0.7 m inside it, over ≈ 0.4 s, at most 6.5 m/s. After a jump that falls short the window is in practice the
    first ≈ 0.1 s below the rim (the depth closes it); walking off a rim, 0.12–0.2 s after leaving it (after the coyote
    jump). Sound: the twirl's bells, higher and quicker, over a rising whoosh.
  - **Last effort:** the catch too late, too deep, too far, or after the twirl: no lift, a flail (arms up, kicking,
    leaning back) and a broken-off cue; Space in the first 0.6 s of the fall itself is a late last effort. The fall, the
    spiral, the return to the parent and the immediate retry are unchanged; the fall now starts when
    `SphereWalker.FallCommitted` (past saving), no longer at the depth alone. Until a recovery has been made, the first
    two returns from a fall add a tip ("press Space as you drop").
- **Decision — broken outlines:** every island's lobes and teeth (2, 3, 5, 7 lobes; 11, 17, 29 teeth) are drawn at a
  seeded strength (0.45–1 of their most), so outlines differ; bridges have a three-wave rag up to 0.45 m (was 0.3);
  outcrops are lobes grown onto an island (70 %) or islands far out, a chasm away (no more islands 5 m off). Lands are
  sized by their own wobble (content margins unchanged). Mesh, footing and edge warning all read `BrokenShell`, as before.
- **Measured after (Core; ray audit = rays from every edge across 2.6–11 m of sky with a long walk round):** misleading
  pairs 0 on all six test worlds (sample, few, crowded, files, empty, Debris Field); ray audit sample 408 → 231, few 625
  → 156, crowded 1473 → 433, files 584 → 294, empty 397 → 189, Debris Field 816 → 455; walkable 35–44 % (was 31–37 %;
  the test allows 25–55 %); compose (Release, loaded machine) 27–100 ms per Hazard World (was 17–66 ms). The behaviour
  baseline is bit-identical. In-app `planets-hazard` 75 checks, 0 failures; Debris Field walking p95 16.7 ms.
- **Alternatives:** enforcing hop-or-chasm along every stretch of edge (the ray audit's view) — it needs islands either
  merged or ≥ 11 m apart, which the launcher rings (pads 12 m apart) do not allow without a continent; fragments a crack
  away from islands (tried: they doubled the near-hop band); making a running jump count as "normal" (then a leap would
  have to exceed 9.4 m, too long for these planets); a recovery after the twirl as well (a held late twirl plus a catch
  reached 11.5 m, beyond any chasm that fits).
- **Known gaps / unknown:** the ray audit still finds 156–455 rays per world (mostly 5–10 m across a bay or past a spur);
  a running twirl (≈ 14 m) crosses some chasms — an expert's stunt, never needed; leaps exist only where they fit cleanly
  (none on four of the six test worlds); how the timing feels (the recovery's 0.1–0.2 s, the apex twirl's ±3 m/s) is by
  trial, not by hand; the real-input recovery steps are written but not run (another session had the desktop).
- **Revisit if:** DJ still finds gaps that look jumpable (then the ray audit is the measure to drive down), the recovery
  too easy or too strict, or wants leaps on every Hazard World.
- **Tuned after the real-keyboard run (2026-11-17):** three real walk-offs with Space about 0.15 s after the edge all came
  too late: the catch's 1.1 m reach closed it ≈ 0.18 s after the edge, before its 0.3 s window. The reach is now 1.5 m
  (≈ 0.25 s); a plain jump saved by the catch then reaches 6.8 m, so the leaps moved from 6.7–7.0 m to 6.9–7.2 m (the
  timed twirl still clears them; HazardMovementTests 88/88). DJ judges whether the window feels fair.

## AD-66 The orchestra a little quicker; two second themes for the folder bands (amends AD-52) (2026-11-17)

- **Problem:** DJ (PLANET-64, PLANET-65): "Keep the musical character. The track I heard in
  `\\wsl.localhost\Ubuntu-24.04\home\david\src\possibility-engine\src` was beautiful and expressive. Identify which
  arrangement that location uses and try a modest tempo lift while keeping what made it feel that way. I'm happy with the
  recent arrangements overall. Try a few additional original melodies or secondary themes if they give revisits and
  different worlds more variety … keep the existing music available, including Abyss."
- **Which arrangement (from the code alone; nothing under that folder was listed or opened):**
  - `WorldCharacter.Of` counts levels up to the nearest location root. `\\wsl.localhost\Ubuntu-24.04` is a WSL location
    (depth 0, the WSL root's own score), so `home` 1, `david` 2, `src` 3, `possibility-engine` 4, **`src` 5**.
    `BandOf(5)` = **band 3, the deep woods**: `IdentityOf` gives `MusicIdentity.DeepWoods`, **the orchestral arrangement**
    (AD-52, with AD-60's general pause and climax): dorian, the theme at half time over the strings' spiccato pulse. The
    hub above (This computer, where a normal launch starts) does not change the count: the WSL root is found first.
  - **The key** comes from the folder's `PlanetTheme` (what it holds), the mode from the orchestra (dorian always). A
    source folder is most likely a **Workshop** (code files at least 45 % of its files, or a manifest): **G dorian**
    (G3 = MIDI 55; the theme opens B♭ D G'). If `src` held only folders it would be a Hearth (F dorian); a mix, Wild (G
    dorian again).
  - **Inside a project or not.** If `possibility-engine` lists a `.git` (folder or file), it is a repository world (the
    clockwork, 126 BPM) and `src` is "the deep woods · in a project": the same orchestra plus the repository's quiet echo
    (`ProjectEcho`: a tick-tock on the pulse's eighths, soft stabs every 4 bars, the commit call high and quiet every 8)
    and its own music memory (the repository id is part of the world key). If not, the plain orchestra. **Likely: in a
    project** (DJ calls it a project; a `src` folder inside a named folder is the usual layout). Either way the
    arrangement is the orchestra; the tempo lift applies to both.
  - **On a revisit** (the arrivals count): the second visit adds the slow counter-line under the cellos' motif and starts
    a forgotten world on its second cycle (a varied return: clarinet for a syncopated or inverted one, oboe for the octave
    or augmented one); the third on its third cycle; a world left recently (8 remembered) resumes on its next half-cycle
    without the harp fanfare. From AD-66, the bars 8–11 of odd cycles (and of a fresh revisit's first free section) play
    the lantern song instead of the cellos' band motif.
- **Decision — the lift (PLANET-64):** the orchestra **144 → 156 BPM** (the theme **72 → 78**, +8.3 %; a bar 3.33 → 3.08 s,
  a cycle 53.3 → 49.2 s). Everything in the score is in sixteenths, so the phrasing, half time, harmonic rhythm (a chord
  per bar), dynamics, general pause and climax move together. The long tails that were written in seconds at 144 (the
  sections', basses' and horns' releases over the chord change, the harp's ring, the bar-5 cymbal) scale by 144/156
  (`OrchestraTails`), so they last as many beats as before and the chords overlap as they did; attacks (the strings'
  swell) and the percussion's own decays stay in seconds (they are the instruments, not the phrasing). The reverb is
  shared by every world and unchanged, so its tail is ≈ 8 % longer relative to the beat (not measurable in the per-bar
  loudness). 156 keeps the bands' ascent (126 → 156 → 172) and leaves the hazard world's theme (152) the most urgent.
- **Decision — second themes (PLANET-65):** two original 4-bar melodies, written relative to each bar's chord and voice-led
  (each note the nearest of its tone above or below the one before, within a sixth; pentatonic keys fold the tones as the
  theme's degrees fold), so every key and progression gets its own version of the same shape:
  - **The lantern song** (the outskirts and the deep woods): "sol fa mi re | sol fa mi | mi fa mi do ti | mi … re": two
    sighs falling through the chord, a turn and a fall, an open end. Orchestra: the clarinet (bars 8–9), then the first
    violins over the cellos an octave below (10–11), into the horn call. Outskirts: the marimba, a soft triangle under its
    long notes. In G dorian (DJ's folder): D C B♭ A | F E D | A B♭ A F E | E D.
  - **The trail call** (the trails and the underglow): "do sol la sol mi | mi sol do' ti" in a 3 + 3 + 2 lilt, the call
    again a chord higher with a lift, an answer climbing back to the fifth (open, into the festival's build or the drum
    and bass build). Festival: the breakdown's pluck. Drum and bass: the liquid lead with its dotted echo.
  - **When:** bars 8–11 (the band motif keeps 12–15; every arrangement's breaks, builds, drops and climaxes untouched) of
    **every odd cycle** of a world's music, and of **the first free section of a revisit that starts afresh**; not in the
    first cycle of a first visit (the arrival and its first 16 bars as DJ knows them), not straight after a resumed
    return (it continues where it was, AD-49), not when an augmented ending holds bar 8. A world's music runs on across
    returns, so the more you come back, the sooner its second theme comes round.
  - **Kept music:** the hub, the roots, the repository, the hazard world and **the abyss** play exactly as before (whole-cycle
    hashes, below; the abyss test is the AD-52 render, sample for sample). Every first visit's first cycle in the
    outskirts, trails and underglow is unchanged.
- **Tried and dropped:** a resumed return opening on the second theme (built first: it broke AD-49's "picks up where it
  was" and changed the abyss sample's script, which returns to the outskirts); a fallback that let the voice-leading leap
  a seventh at the range's edge (the singable test caught it in four keys). **Considered, not built:** a counter-melody
  over the theme's raised motif on the third visit (the octave counter-line and the third visit's doubling already
  thicken it, and it would change the theme's bars, which DJ likes as they are); second themes in the roots and the
  repository (their own material is their identity, AD-49).
- **Measured (SkySynthTests, Linux, 2026-11-17):** whole-cycle hashes (`SKYSYNTH_CYCLE_HASHES_OUT`, every score, visits 1–3,
  one cycle + 2 s, Library key): the lift changes **DeepWoods visits 1–3 only**; the second themes add **Outskirts,
  Trails, Underglow visits 2–3** (DeepWoods' already changed). The behaviour baseline (`FILESCAPE_BASELINE_OUT`) is
  identical: none of its six audio hashes changed (its windows are the first 6 s of a first visit, and it has no band 3).
  Orchestra loudness −27.4 LUFS (AD-60: −27.2), peak 0.228; the src arrival clip −26.9 LUFS (−26.7 before); per bar of
  the first cycle within 0.6 dB of before (the climax bar 14 −26.8 both); the general pause and the climax tests pass.
  Spectrum per octave (63 Hz–8 kHz) of the arrival clip within 0.4 dB of before; the climax clip's 63–125 Hz 1.0–1.2 dB
  lower (the basses' tails are shorter in seconds), the rest within 0.1 dB. The orchestra revisit with the lantern song:
  250 Hz −3.2 dB, 1–4 kHz +0.7…+1.4 dB against the cellos' motif it replaces (it sits higher).
  Edge warning over the orchestra +8.0 dB, fall +10.4, return +8.5; at the bar-14 climax fall +11.8, return +9.5 (bound 4).
  Tempo-locked cues: the 32nd at 156 is 48.1 ms, worst wait 48.1 ms (bound 80); a grid-aligned wind-up launches 0.4
  frames off the sixteenth; the theme's first note 114 ms after the arrival (a beat is 385 ms), plain, every note.
  Second themes (revisit, Library key): their bars 2.0 (outskirts), 2.4 (orchestra), 4.0 (trails, the breakdown),
  4.0 dB (underglow, breakdown and build) below the theme's bars of the same cycle; peaks ≤ 0.37; the edge warning
  +10.8 … +15.1 dB over them; render cost with cues 19–32 ms per second of audio (bound 60); the existing cost test
  34 ms for the orchestra.
- **Unknown:** nobody has listened. The melodies were judged by reading their notes back in every key and by the
  measurements above; whether the lift keeps the expressiveness DJ heard, and whether the second themes are good, is
  DJ's call by ear. Samples: `%LOCALAPPDATA%\FileScape\evidence\2026-11-17-music\` (README.txt).
- **Revisit if:** DJ wants the orchestra back at 144 (one constant, `OrchestraBpm`) or quicker, the second themes more or
  less often (`PlaysSecondTheme`), in other worlds, or one of them dropped.

## AD-67 Framings add to the pitch you choose; they never replace it (amends AD-57) (2026-11-19)

- **Problem:** DJ (playtest of `bcde9b33b338`): "Left- and right-drag turn the camera horizontally, but vertical
  dragging appears locked out. I can't tilt up or down. Check surface walking, arrival transitions, and Overview …
  keeping the improved arrival perspective. Check any leveling or camera-follow behavior that could immediately undo
  the input."
- **Reproduced first** (`--automate=planets-look`, injected drags through the mouse path, judged from the drawn camera):
  standing on arrival, just after a landing, in the overlook view and on a Hazard World's arrival a 200–400 px vertical
  drag tilted the view **0.0°** (the chosen pitch moved 30–56°; 9 of 9 sweep steps dead on the Hazard World); the wheel
  did not zoom during the arrival framing (8.5 → 8.5 m). After walking, while walking and in the overview it worked
  (≈ 30° per 200 px).
- **Root cause:** the chase camera blended its pitch *to* a fixed value: `Lerp(Lerp(chosen, 29°, arrival), 34°,
  overlook)`. The arrival framing (AD-57, PLANET-42) holds at weight 1 for as long as you stand still — by design a drag
  does not release it — so on arrival, where you stop to look round, the chosen pitch had no weight at all; the overlook
  view did the same. The distance blended the same way (the wheel). On a Hazard World the hazard view's cap,
  `Min(base + add, Max(62°, base))`, also held the pitch at 62° for 16° of drag. Not the cause: the overview (its own
  orbit, unaffected), the Places look settings (the planets camera never read them), levelling (Places only), the
  swing-in and the Hazard arrival look (heading only), the keep-in-frame rule (it limits the aim, not the boom).
- **Decision:** the framings are offsets on what you choose. Pitch = chosen + (the framing's pitch − 18°), the distance
  = chosen × (the framing's distance ÷ 7.2 m); with the usual 18° and 7.2 m that is exactly the framing as before. A drag
  keeps the chosen pitch inside the range the current framing leaves (clamped, then moved, so every pixel counts until
  an end). Each arrival (landing, warp, opening shot) starts from the usual pitch, so every landing is the designed
  arrival shot whatever you looked at before; the wheel's distance is kept. The hazard view and edge peek still add in
  full under the 62° cap; above it their addition fades out linearly to 0 at 70° instead of being cut. Looking up
  (PLANET-68): past the boom's floor (−8°) a further 12° of drag raises the aim over the traveller, so the view reaches
  ≈ +19° above the horizon (+8° before), kept under the 18° keep-in-frame rule so it never snaps back. The planets
  camera keeps its own convention (drag down raises the camera, the view looks down; drag right turns right) and does
  not use the Places reverse or levelling settings, as before; DJ found the horizontal direction right.
- **Alternatives:** releasing the arrival framing on a vertical drag (the distance and look-ahead DJ liked would ease
  away the moment you tilt); keeping your pitch across a flight (a landing after looking at the sky would show the sky,
  not the world: the arrival composition is lost); honouring Places' "reverse up/down" on planets (default on, it would
  flip the vertical direction against the horizontal one, which DJ is happy with).
- **Revisit if:** DJ wants the planets camera to follow the Places reverse settings, the pitch kept across flights,
  more look-up (`LookUpPastBoomDegrees`; past ≈ 13° the keep-in-frame rule would take it back) or a different
  sensitivity (`OrbitPitchDegreesPerPixel`, 0.15°/px).

## AD-68 An open panel owns the keyboard: one rule where keys reach the game (extends AD-62) (2026-11-19)

- **Problem:** DJ (PLANET-69): typing into a navigation dialog still switched out of Planets into a legacy view; he
  did not know which dialog. AD-62 had given the Destinations list and the reader their own guards; the other panels
  had none.
- **What was reproduced (in-app, `planets-dialog-keys`, injected key events, before any fix;
  `docs/evidence/2026-11-19-input/repro-dialog-keys-summary.txt`):** the game takes every key no control uses
  (`Main._UnhandledInput`). **Locations / Open location with the rows focused** (opened by L, its button, Ctrl+O, the
  Open… button, or Tab from the path field): Godot's ItemList uses only its own navigation keys, so every letter,
  digit, Backspace, Home/End, ←/→ and chord reached the shortcuts (Tab did not: Godot moves the focus with it) —
  V is Planets ↔ Places, G launched to the selected world (seen live), Backspace is History back. **Settings** (no focusable control): every key, Tab and V included. **Destinations results and
  the reader**: chords — Ctrl+V (paste) is V for Planets ↔ Places, Ctrl+Tab is Tab (switch view); both switched to
  Places with the panel open. **The path field**: Ctrl+Tab switched to Places. Plain typing in the Destinations filter
  and the path field was already safe. Most likely DJ's case: Locations with the rows focused, or a paste.
- **Decision:** one rule at the only place a key reaches the game's shortcuts, `Main.PanelOwnsKey` (Main.KeyboardOwner),
  first in `_UnhandledInput` for every experience. A trip's Esc stops the trip first. Then, while a keyboard panel is
  open (`KeyboardPanel`: the reader, Settings, Locations / Open location, the planets' Destinations list; the one holding
  the GUI focus, else the first in that order — Locations and Destinations can be open together, PLANET-41), that
  panel's own handler gets the key (`ReaderKey`, `PlanetWorld.ListKey`); Esc closes the topmost panel
  (`HandleEscape`), never the overview or a layer behind it; only the F keys, exactly Ctrl+L and Ctrl+O (they open the
  navigation panels) and J from the Destinations results (it closes the list) or the Locations rows (it opens the list
  beside them, as `planets-navigation` and `-Play` expect) pass; every other press, echo or chord
  stops there and is counted (`PanelConsumedKeyCount`). Held movement is blocked while such a panel is open too (the
  `blockedKeys` of the planets' tick and the maps' movement), whatever the focus.
- **Alternatives:** guard each panel (the AD-62 shape; it is what left Settings and Locations open); check the GUI focus
  owner in the shortcut handler (a click on the world leaves focus nowhere; a Button-only panel never has focus); make
  the panels modal windows (they would take the pointer too, and DJ looks round with Destinations open); intercept in
  `_Input` before the GUI (the panels' own controls would lose their keys).
- **Changed on purpose:** Ctrl+Home (root) and other chords no longer pass while a panel is open (AD-62 let every chord
  through Destinations); Esc in the overview with a panel open closes the panel, not the overview.
- **Known limits:** keys are judged as Godot delivers them; the real-keyboard check (`os-planets-probe.ps1 -Dialogs`)
  is written but not yet run. Non-keyboard overlays (the preview, the bench, the root landmark's E panel, Help, the
  legend) do not own the keyboard: you walk with them open, and the landmark's number keys are its own (AD-63). The
  folder picker and confirmations are separate windows with their own input.
- **Evidence:** `planets-dialog-keys` (see the implementation status), the reproduction above.
- **Revisit if:** a new text-entry or navigation panel is added (add it to `KeyboardPanel`, not a guard of its own),
  or DJ wants a paste in the Destinations results to go into the filter.
- **Tuned after the real-keyboard run (2026-11-19, `os-planets-probe -Dialogs` on the merged build):** two defects the
  injected-event probe could not see, because they need the real mouse resting on the toolbar.
  - *Esc after a toolbar click:* opened by its toolbar button, a panel needed Esc twice. The button's tooltip was up under
    the resting cursor and the GUI spent the first key press dismissing it (reproduced: after the first Esc nothing had
    changed, the filter still editing). Esc for an open keyboard panel is now taken in `Main._Input`, before the GUI,
    through the same `PanelOwnsKey` (a trip's Esc first).
  - *Shortcuts dead after ⚙ then Esc:* the toolbar button kept the GUI focus after its panel closed, and a focused
    control blocks the shortcuts (AD-12), so R and J did nothing until a click on the world. The Esc that closes the last
    keyboard panel now releases the focus.

## AD-69 Hazard World moves: a catch from the first instant, a long jump for taps, an answer to every press (2026-11-19; amends AD-65)

- **Problem:** DJ (2026-11-19): "I couldn't get the late rim catch to work. Once falling, the character seemed completely
  unresponsive. Pressing Space produced no visible effort … Separate and verify these two moves … Adjust the timing and
  reach so a player can discover and repeat them. During a failed recovery attempt, give the character a visible and
  audible effort, even if too late. Keep the fall and return-to-parent sequence moving promptly."
- **Root cause (measured first, Core `HazardRecoveryTimingTests` on the code as it was; press time after the feet leave
  the rim, 10 ms steps):**
  1. **The app started the fall at the depth alone.** `PlanetWorld.BeginFallIfThrough` took over once the feet were
     0.6 m below the rim (≈ 0.23 s after a walk-off), not when `SphereWalker.FallCommitted` said the walker was past
     saving — AD-65 says it did, but the app never used `FallCommitted` (since PLANET-34). Core's 0.3 s window and 1.1 m
     depth were never reachable in the app: every press after ≈ 0.23 s went to the fall phase.
  2. **The first 0.12 s were a coyote jump into the open sky.** Over a hole, Space within the coyote time took off as an
     ordinary jump from the rim, carrying the walker further out (walking or running, every press 0–0.11 s: no save) —
     the real-input run's "0–0.07 s did not save".
  3. **The catch's reach closed it almost at once.** 1.5 m from the walker's centre: walking, the catch was open
     0.12–0.23 s (≈ 0.11 s); running, one 10 ms step (0.11 s). With W still held the catch lifted the walker and the air
     steering carried it back out (every catch failed with W held).
  4. **Dead presses.** One last effort per airtime: in the walker, the second and later presses did nothing; in the fall,
     only a press in its first 0.6 s answered, once, and its flail and cue (gains 0.035–0.07) were lost under the fall's
     roar and the camera's 4 m distance.
  5. **The long jump needed a held Space.** A tapped jump and tapped twirl never crossed a leap (the twirl's rise took the
     short-hop gravity); held 0.3 s the window was 0.11–0.23 s; held throughout 0.46 s.
- **Decision — the rim catch** (`HazardMoveParams`, `SphereWalker`):
  - The app starts the fall at `FallCommitted`; `RecoveryOpen` now also needs the twirl unused and (once 0.6 m down,
    where it decides) ground in reach, so a run-off out of reach falls as promptly as before.
  - In the coyote time over open sky, the press is the catch unless the coyote jump would land
    (`CoyoteJumpLands`: a held, unsteered jump predicted to come down on ground with 1 m to spare); a late press at a
    step still jumps it.
  - Window 0.4 s (was 0.3) from the feet passing the rim's level — walking off, from the slip — and depth 1.8 m (was 1.1).
  - Reach: any ground within 1.5 m as before (the far rim of a gap barely missed; it keeps a plain jump saved by the
    catch short of a leap), and the rim last stood on within `ReturnReach` 3.2 m.
  - The carry lands 0.6 m inside (was 0.7) at the lift's own air time (was a fixed 0.4 s, up to 9 m/s, was 6.5), holds
    its course without air steering, waits while the feet are deeper than 0.15 m under the rim and the next move would
    pass under the rock, and stops dead on landing (a 1.2 m bridge is not slid off).
- **Decision — the long jump:** the timed twirl ("at the top") is now within ±4 m/s (was ±3, up to where a falling press
  becomes the catch), and its rise keeps full gravity whether Space is held or tapped (`JumpModel.HoldRise`, what a hold
  already did: the envelope's maximum is unchanged in kind). Plain jumps are untouched.
- **Decision — every press answered:** any press on the way down over open sky that is not the catch or the twirl is a
  last effort, every time (in the walker and, after the fall starts, in the app until the return); a flail with the
  body leaning back 26° (was 18°) and a 0.22 m hop of the pose, a 0.7 m bump back up the fall's path in the spiral
  (pose and path only: the fall's timing is unchanged), and the cue ≈ 9 dB louder with a strained "hup" in front and the
  music ducked by half for 0.45 s. The return tip says "Press Space at once (within about 0.4 s)".
- **Timing as measured (Core, a 22 m Hazard World):**

  | Move | Press | Result |
  |---|---|---|
  | Rim catch, walking (6 m/s), W held or let go | 0.00–0.40 s after the feet leave the rim | caught, back on the rim ≈ 0.5 m in |
  | Rim catch, running (11 m/s) | 0.00–0.27 s (W held) / 0.29 s (let go) | caught |
  | Uncaught slip | — | the fall starts 0.40 s (walking) / 0.27–0.29 s (running) after the slip |
  | Late jump at a step (≤ 2.6 m) | 0.00–0.10 s after leaving the edge | the coyote jump across |
  | Long jump, leap 7.4 m (widest, with jags), walking, Space on the board 0.2–0.6 m before the edge | second press tapped 0.12 s: 0.19–0.42 s after take-off (0.26–0.43 from the board's far end); each held 0.3 s: 0.19–0.52 s; held: 0.15–0.65 s | across |
  | Plain jump at a leap, walking | — | never across (a running jump is the other advanced way, as before) |
  | Any other press on the way down / in the fall | any time until the return | a last effort (flail, hop, cry) |

  Envelope: walking jump and twirl at best 10.1 m (was 9.8; chasms start at 11 m); a plain jump saved by the catch
  6.4 m (leaps start at 6.9). The behaviour baseline's whole-planet trajectories are unaffected (nothing here runs off a
  broken shell).
- **Measured in-app (`planets-hazard`, real Space presses):** caught at 0.07 s (W held, inside the coyote time), 0.23 s
  (W let go) and 0.38 s (W held); an uncaught slip fell 0.43 s after it (416 ms); three presses in the fall, three
  efforts; fall to standing on the parent 3.7 s; the leap crossed with the twirl at 0.30 s and at 0.45 s after take-off;
  the plain jump fell short.
- **Alternatives:** dropping the coyote jump over open sky altogether (simpler, but a late press at a step would pull you
  back instead of jumping it); a physical upward kick for the last effort (DJ's "maybe"): it lengthens a jump and so the
  envelope the gap classes are sized by, so the bump is drawn, not simulated; a longer `Reach` (1.8 m made a plain jump
  plus a catch clear a leap, AD-65); the catch carrying you onto whichever ground is nearest after a full jump (the take-off
  rim is ≥ 4 m back after a full jump, so `ReturnReach` never carries one across a leap).
- **Unknown:** how the 0.4 s window, the long jump's ≈ 0.25–0.4 s and the effort look and sound in DJ's hands; the real-input
  probe (`os-planets-probe.ps1 -Hazard -Recovery`) is written and parse-checked, not run.
- **Revisit if:** the catch feels too easy (shorten `Window` or `ReturnReach`), the long jump too strict for taps (widen
  `ApexSpeed` no further than `LateSpeed`), or the effort too loud (`LastEffortDuck`, the cue's gains).

## AD-70 What E and the twirl do at landmarks and scenery: the telescope lifts you, the lighthouse lights, a few kinds of scenery answer (2026-11-19; amends AD-63)

- **Problem:** DJ's fourth playtest (PLANET-73..75): "I tried the telescope and lighthouse and nothing happened."
  **Why the telescope seemed to do nothing:** its sign "E · Telescope" appeared from ≈ 7 m off the overlook's edge and
  the prompt said "Up the steps to the telescope: E there lists …", but E only acted on the platform, 2.4 m up a ring of
  seven 1.2 m stones that winds round it (a real-input probe climbed them once in two runs; on the Starter Sky the book
  tower stands 1.5 m beside the platform, between the arrival and the stones, and catches anyone walking straight at
  it). Pressing E where the sign said E did nothing. And when it did act, it opened the same list as J, with nothing to
  say so. The lighthouse was scenery: nothing at all answered.
- **The telescope (decided):** the sign and E share one reach. Within 6 m of the overlook's edge (the sign shows; it no
  longer shows farther out) the prompt reads **"E · up to the telescope · or climb the stones"** and E lifts you onto
  the platform beside the telescope with a hop (the hop Destinations' approach uses, now arcing from your height up to
  the platform's top); the overlook view rises as when you climb. On the platform **E** opens this world's list, the
  telescope's light flares, and a caption says what the list is ("Through the telescope: every world and file here.
  Choose one and press Enter to go there."). Options considered: (1) *only a clearer "climb the stones" prompt* —
  honest, but the climb itself is the difficulty; (2) *wider stones or a ramp* — changes every world's composition and
  the behaviour baseline, and a ramp crosses the routes round the overlook; (3) *E opens the list from the foot* — the
  list is J's list, so the overlook would add nothing; (4) *E lifts you up (chosen)* — the sign is true wherever it
  shows, the platform's view (every destination named) becomes the first thing E shows, and the second E is the list.
  The stones stay for players who climb; jump and twirl still reaches the top.
- **The lighthouse (decided: a small interaction):** at its foot (within 1.8 m of its footprint) the prompt reads **"E
  or twirl (Space, Space) · light the lighthouse"**; either lights it: two soft additive beams sweep round from the lamp
  room one and a half turns over 3.2 s and fade, the lamp flares, and `SkySynth.LighthouseCall` plays a soft low horn on
  the tonic with the lamp's two bells (fifth, then octave) on the music's grid, the music dipping to 70 % for a second.
  It opens nothing. Why both keys: E is what DJ tried, and the twirl is how the bell beside it is played (AD-63); the
  lighthouse is not a bell you stand under, so either deliberate act will do. Remaining scenery was considered and
  rejected: it is the most "interactive-looking" landmark (a door, a lamp) and DJ expected something.
- **Scenery that answers (decided):** three kinds the themes already scatter — **mushrooms** (squash and spring back,
  a boing up a fifth), **crystals** (a sway and glow, three glassy notes up the chord) and **lamps/lanterns** (a sway and
  glow, two warm bell notes falling a third) — answer a **twirl** within 1.7 m. Each reply is under a second of motion
  and about a second of sound, in the world's key on the grid, quieter than the bell flowers, and the same piece always
  answers with the same degree (neighbours differ). **Cue:** the nearest answering piece within 4.5 m shimmers (one soft
  breathing light); beside it, the prompt "Space, Space · twirl by the mushroom/crystal/lantern" shows until that kind
  has answered twice in the run, then the shimmer alone is the cue, so groves stay quiet. Why the twirl and not E: E is
  the file key; scenery stands among files, and the twirl is FileScape's deliberate playful move (bench spin, bell,
  file preview). Trees, rocks, grass and flowers stay still (the bell flowers already ring on footsteps by audio files).
- **What keeps files and routes safe:** a file or pad in focus wins E and the twirl before any of this (`UpdateFocus`
  order unchanged: pad, file, telescope on the platform, root landmark, then bell/lighthouse/telescope reach, then
  scenery); nothing adds a solid or moves a composition (the behaviour baseline is unchanged); scenery stays in its
  MultiMesh, one slot moving and returning to its built transform.
- **Evidence:** Core `SkySynthTests.Landmarks` (grid, key, brevity, level, variation); `planets-lookouts` (telescope from
  the approach, the stones, a file keeping E, a lantern answering, the bell, the lighthouse). Real input:
  `os-planets-probe.ps1 -Landmarks` extended (E from the approach, the lighthouse, a piece of scenery), parse-checked,
  not run here.
- **Known limits:** the sounds and looks are judged by measurement and screenshots, not yet by ear or by DJ's eye; the
  beam is a plain cone (no gradient along its length); only Hearth has a lighthouse, and the other landmarks (flower,
  chimney, book tower, tent, vault, spire, arches) remain silhouettes.
- **Revisit if:** DJ wants the climb kept as the only way up (drop E's lift), the scenery quieter or chattier
  (`SceneryPromptTimes`, `SceneryShimmerMetres`), other kinds to answer (flowers, trees) or other landmarks to answer.

## AD-71 The overview discovers by itself, breadth-first, within fixed bounds, and grows without jumping (2026-11-19)

- **Problem:** DJ (PLANET-76..78): the overview (T) showed only what visits had listed, a couple of levels; he wanted it to
  discover more while he looks round (breadth-first, the selected or zoomed-toward world first), with the existing
  listing system, bounded requests and memory, links and denied folders as in normal exploration, entries and metadata
  only, no camera reset or layout jumps, an indication with a pause, and what was learned kept.
- **Decision — who decides and who lists:** a Core scheduler, `OverviewDiscovery`, picks folders; the session's
  `WorldExplorer` lists them through a new request, `Discover` (like `Reveal`: not a visit, never a location root, only a
  folder never listed, at prefetch priority behind what the user waits for; unlike Reveal, through `ListAhead`). No new
  listing code, threads or caches: discovery is the explorer's ordinary metadata-only listing, and its results are the
  session's tree.
- **Decision — order:** the frontier is the overview's drawn worlds still known only by name. Worlds below a focus (the
  selected world, and the drawn world nearest the point the camera looks at) go first, breadth-first below that focus;
  then everything else breadth-first by depth, then the layout's order (names). Only drawn worlds are candidates, so
  discovery never lists what the overview cannot show; `UniverseLayout.MaxBodies` (1,500) and `MaxChildren` (64) bound it.
- **Decision — bounds (`OverviewDiscovery.Limits`):** at most **3 listings in flight** (the explorer runs 4, so a visit's
  own listing always has a worker); **10 new listings per second**, burst 3 (paces the growth DJ watches, ≈ 300 folders in
  30 s); **stop at 200,000 tree entries** (a fifth of the session's 1,000,000-entry budget, ≈ 1 KB each, so visits keep
  room; the explorer's own budget and reserve still apply); a request unanswered after **20 s** frees its slot and is not
  retried. Steps every 0.1 s; relayouts at most every 1.5 s. Only while the overview is open; X or the indicator's button
  pauses (the state is kept for the session).
- **Decision — what is never asked for:** links (not folders in the tree: shown, never followed, so no loop), the hub and
  location roots, failed, denied, unavailable, vanished and cloud folders (they keep their state until Retry), folders with
  a note (a ListAhead refusal, the entry limit), hidden folders (not drawn), anything ListAhead refuses (Linux system
  filesystems, network shares), and anything outside the arrival folder or a place visited this session (the aerial
  world's rule, `IWorldSession.MayListAhead`), so the stubs on the way to the arrival folder (`C:\Users\…`) are not listed.
- **Decision — growth without jumps:** the layout stays the global nested-rings layout (AD-46); a relayout (a) pins every
  world already drawn, so worlds are only added, never pushed out when the budget fills; (b) glides every world from where
  it is drawn to its new place over 1.2 s (smoothstep), a new one growing out of its parent; (c) shifts the whole picture
  so the anchor (the selected world, else the root) stays where it was headed, so the camera, which looks at it, never
  moves; (d) while the camera is still framed by opening or selecting (until a wheel or pan), eases the distance with the
  framed system's growth, so the growing ring stays in view. Yaw, pitch and focus are never touched.
- **Considered, not built:** a frozen incremental layout (new worlds placed without moving any old one). The packed
  layout reserves no room: a leaf's neighbours sit 2.5 m apart, so its discovered children would overlap them or shrink by
  ~×0.3 a level. A fractal layout with room reserved per level would change the overview DJ has learned (AD-46). The
  anchored glide keeps the picture and reads as unfolding; DJ to judge.
- **Considered, not built:** discovery while the overview is closed (DJ asked for it while open; nothing is listed behind
  the player's back); listing hidden folders (the overview does not draw them).
- **Measured:** see implementation status (fourth playtest follow-up): fill-in at 0, 10 and 30 s on the deep fixture, the
  WSL deep fixture and `C:\` (counts only), jumps, drift, frame times, relayout cost.
- **Instrument:** `--test-broad` treats the arrival folder as a broad place (no background listing), so a fixture
  measures discovery and not the background traversal of a non-broad arrival (which lists the whole subtree anyway).
- **Revisit if:** DJ finds the growth too fast, too slow or too busy (`ListingsPerSecond`, `UniverseGrowSeconds`,
  `UniverseRelayoutSeconds`), wants it to continue outside the overview, or wants the overview's 1,500-world budget larger.

## AD-72 The FileScape theme in seven modes; a Mixolydian launcher; arrangements for places and moments (offline sketches, nothing replaced) (2026-11-19)

- **Problem:** DJ (PLANET-79, PLANET-80): study how game composers use modes (two 8-bit Music Theory videos; notes and
  what could be accessed in `docs/music-modes-notes.md`), then: the same FileScape theme in all seven modes with the
  rest held constant; an original launcher theme that is bright but lets the lowered seventh be heard, as the same phrase
  in major and Mixolydian, then developed; fuller arrangements of the sketches that suggest a place or a moment, one
  returning the theme in a new mode at launch, landing and a crossing into a new kind of world; one reel and samples;
  keep the existing arrangements. Later (coordinator relaying DJ): "Try not to remove good, already working music from
  the game to make room for new ones. Aim for diversity and surprise instead of conforming to a set number of songs."
- **Which theme:** the FileScape theme (`SkySynth.ThemeMelody`): the only tune every arrangement plays (the hub, the
  roots, the repository, the four bands, the hazard scores), written in scale degrees. **Finding:** it uses only do re mi
  sol la (written to fold into a pentatonic key), so on its own Ionian, Lydian and Mixolydian give the same melody and
  only the harmony differs. The short version therefore takes the motif (bars 1–2) and the cadence (7–8) as written, with
  two ornaments the same in every mode: bar 2's long mi becomes a turn mi–fa–mi (Lydian's ♯4 and Locrian's/Phrygian's
  fa are heard in the tune) and the cadence's long re becomes re–ti–re before do (major's ti, the others' te).
- **Decision — a mode abstraction in Core:** `DiatonicMode` and `Modes` (`DiatonicMode.cs`): each mode as a rotation of
  the major scale, the pitch of any degree, triad qualities, the characteristic degree (Ionian ti, Dorian ♮6, Phrygian
  ♭2, Lydian ♯4, Mixolydian ♭7, Aeolian ♭6, Locrian ♭5). Separate from SkySynth's own scale arrays, which are unchanged.
- **Decision — written sketches on SkySynth's instruments:** `SketchScore`/`SketchWriter` (notes by bar, sixteenth and
  degree in a mode; the mode, tempo and tonic can change between sections), `ModeSketches` (the compositions) and
  `SkySynth.RenderSketch` (`SkySynth.Sketches.cs`): a synth whose decks are stopped plays only the score's notes, on the
  game's own lead, pad, bass, drum and effect voices, through the same reverb, limiter and clipper. Offline only.
- **The reel (PLANET-79):** D, 112 BPM, the bell over a soft pad, a round bass, the harp and offbeat hats; a bar that plays
  the mode's scale up on the harp over the tonic chord, then the short theme over i – characteristic chord – pre-cadence
  – i: Ionian I IV V I; Dorian i IV ♭VII i; Phrygian i ♭II ♭vii i; Lydian I II vii I; Mixolydian I v ♭VII I; Aeolian i ♭VI
  ♭VII i; Locrian i° ♭II ♭V i°; a glass bell rings the characteristic degree over the last chord. Only pitch differs
  between versions (tested note for note).
- **The launcher (PLANET-80):** an original 4-bar phrase (a leap to the octave and a turn round the seventh; the seventh
  held over the fifth's chord; a climb to la'; down to the seventh and up to do') over I | V | IV | VII–I, 128 BPM, pluck,
  pad, saw bass, four on the floor. In major ti, vii°–I; in Mixolydian te, v minor, ♭VII–I. The pair differs in nothing
  else (tested). The take-off develops the Mixolydian one: a wind-up ending on the horns' te, the launch hit, the phrase
  on supersaw, again an octave up with the horns holding te against it, a ♭VII–IV–I lift and two bars of flight.
- **Arrangements (PLANET-80):** the Lydian overlook (84 BPM, flute over I/II on a tonic pedal, harp, the ♯4 on glass:
  wonder, the overview); the Aeolian empty moon (72 BPM, cellos then clarinet, ♭VI–♭VII–i: loss, the way home); the
  mode-shift journey (112 BPM throughout): Ionian on the home world → wind-up → Mixolydian at the launch → Lydian in
  flight → a landing bar → Dorian landed in the deep woods → the edge of a hazard world → Phrygian, closing ♭II–i.
- **Integration: none (this iteration).** The game's music, its arrangements, themes and cues, and how each is chosen
  are unchanged: the behaviour baseline and the whole-cycle hash of every score (all 10 identities × visits 1–3) are
  identical before and after, and nothing in the game calls the new code. Per DJ's guidance, anything taken into the game
  later is **additive**: a new variation that can turn up alongside the existing ones (for example the mode-shift
  journey's Mixolydian return as an occasional launch variation on a revisit, the Lydian overlook as a new cue for the
  overview or telescope, the Aeolian lament for an empty moon), never replacing or removing a working piece; and
  verified the same way (the existing cases' hashes unchanged, the new piece only adding options).
- **Measured:** every render below the soft clipper (peaks 0.17–0.50), −26 to −28 LUFS like the game's music samples,
  silent 50 ms windows only in the reel's six gaps; the voice estimate is within the 40-voice pool except a brief
  conservative over-count in the take-off (stealing there is click-free). Samples and README:
  `%LOCALAPPDATA%\FileScape\evidence\2026-11-19-modes\`.
- **Unknown:** nobody has listened; the judgements in the README are readings of the notes and harmony, not of the sound.
- **Revisit if:** DJ picks pieces for the game (add them as new options as above), wants a different tonal centre or
  tempo for the reel (`ModeSketches.TonicMidi`, `ReelBpm`), or wants the plain written theme (without the two
  ornaments) in the reel.

## AD-73 Keep the name FileScape for now; the naming discussion is parked (2026-11-19)

- **Decision:** DJ chose to keep **FileScape** for now and revisit naming later. Continue GitHub preparation under that name.

The naming note, as DJ recorded it:

The desired public name should sound inviting and game-like to people who may not think in terms of desktops or
filesystems. It should suggest escape, discovery, and inhabiting another place, with room for forests, libraries,
and a possible full desktop as well as planets. DJ liked the escape concept. Folder-based names did not resonate.

- **Otherwhere** was the strongest recent favorite. The intended spelling is Otherwhere; Otherware was a dictation
  error. The appeal was the feeling of another place and freedom to grow beyond a space theme.
- **Elsewhere** was a favorite for the same breadth, but several games and studios already use it.
- **Starward** and **Pocket Cosmos** were early favorites. DJ later noted that Pocket Cosmos strongly suggests a
  planetary game and Starward also carries a space theme.
- **Outwander**, **Farther In**, and **World Within** were secondary possibilities. Orbitory appealed but already has
  an existing app use. Orbitale was hard to understand and pronounce. Foldernaut did not appeal.

The initial Otherwhere search found a close overlap with an interactive-fiction world app and an exploration game.
These findings led to parking the rename, rather than choosing a replacement. This was a search for existing uses;
trademark availability was not established. Recheck current uses before selecting any public name.

Sources checked during the discussion:
- Otherwhere interactive-fiction app: https://otherwhere.ai/
- Otherwhere exploration game: https://befuddlebug.itch.io/otherwhere
- Elsewhere crafting game: https://store.steampowered.com/app/1271150/Elsewhere/
- Orbitory app documentation: https://gist.github.com/Ali-Li26

Next time, resume from DJ's preference for Otherwhere's meaning and breadth. The private GitHub backup can proceed
as FileScape while naming remains open.

## AD-74 Travel: leave from the pad, land on your feet, and an accepted landing from the overview always lands or says why (2026-11-19)

- **Problem (DJ, fourth playtest):** "Some transfers end with the character apparently landing on their backside,
  followed by an immediate snap upright." "When I choose a destination through a command or list, get the character to
  the appropriate launch pad before taking off … brisk enough that direct navigation stays useful … consistently
  [for] trips, parent and history navigation, and travel from Overview … a coherent departure for destinations that
  have no physical pad." And an intermittent failure: Ubuntu root → Destinations filtered "home" → its world selected
  in the overview → Enter: "I heard a sound but stayed in Overview."
- **What was measured first (unmodified code, `planets-travel` and `planets-overview-enter`, PLANET-81..83):**
  - *Landing.* At every touchdown (11 flights, 4 warps) the traveller was still in the full flight pose: leaning 68°,
    feet 0.52 m off the ground, head 0.67 m up (standing: 0.83 m) — a figure folded at the hips, which from the chase
    camera reads as sitting. The pose then eased back with a 1/6 s time constant; the landing frame is long (the new
    world is built in it, ≈ 0.1 s), so the first step was 31° in one frame and up to 29° a frame on screen, upright
    0.2–0.3 s later. Warps also faced an arbitrary side on the way down and turned 90° in the touchdown frame (82° a
    frame on screen). The landing squash never played (0 events): it was posed with a zero frame, which cleared it.
  - *Departure.* Only Space on a pad started on a pad. G, the list, trips, U, Backspace and every other change took
    off from wherever you stood (0.37–0.48 s from the command, the wind-up only).
  - *Overview.* 30 attempts of the route's variants on a root-like WSL fixture (with and without a 6 s listing delay on
    `home`) found two defects, both silent: (1) a destination two levels down chosen in the Destinations list opened in
    the overview starts a **trip**, whose wind-up plays its sound but never runs — the overview ticks only itself — so
    you stay in the overview, phase WindUp, until Esc (3 of 3); (2) a world chosen by a click on its **row** in the
    list, then the overview's Enter (list closed) acts on the overview's own selection, "you are here", and glides
    back down without travelling (6 of 6). Every other variant landed (21 of 21). Which of the two DJ met is not
    established: (1) matches "a sound, stayed in the overview, Esc" best.
- **Decision — landing (PLANET-81):** rotate feet first in the air rather than add a get-up. Over the flight's last
  0.45 s (at most 40 % of a short flight) the streamlined pose turns to standing as a smoothstep of the flight's
  remaining time — a function of time, so a long frame cannot make it jump — and the facing turns to where it will
  stand (the camera's look, as the landing sets it). A warp rises and descends standing, facing its landing. A
  zero-length pose no longer drops the frame's events, so the touchdown's squash plays. *Why not a seated landing
  with a recovery:* it would add ≥ 0.5 s to every arrival, start from the very pose DJ disliked, and the long landing
  frame would still have to hide nothing; the turn uses flight time that already exists and adds none.
- **Decision — departure (PLANET-82):** one routine (`PlanetWorld.Depart`) for every launch that does not start on its
  pad. It finds the destination's pad on this world (the folder's launcher, the way back for the parent, a link's
  launcher) and: off the pad, a brisk run-hop onto it (0.3 s plus 1 s per 30 m along the ground, at most 0.8 s; 1.1 s
  along a Hazard World's bridges; a low arc, 0.6–3.5 m), then the existing wind-up and take-off; on the pad (within
  0.8 of its radius), the wind-up at once; **no pad** (a belt moon, the history's far end, a favourite, a path, Straight
  there), the wind-up where you stand with a burst of the world's accent at your feet at the release, then the flight
  or the warp's rise. U and Backspace now leave through their pad like a pad launch: the navigator moves at the release,
  so Esc during the run or the wind-up stops them (you stay, a run lands on the pad); a change already made elsewhere
  (the breadcrumb, Locations, Go to path) is not undone by Esc — Backspace after the landing returns. **The overview**
  keeps its own departure, the dive into the chosen world (≈ 0.75 s): the traveller is not on screen there, and gliding
  back down to run to a pad first would add ≈ 2 s (the 1.1 s glide, the run, the wind-up) to the quickest route there is.
- **Decision — the overview's Enter (PLANET-83):** a destination committed while the overview is open (the list's
  Enter, a trip, G) lands through the overview — its fly-in when the world is drawn, else the navigator goes there and
  the overview comes down onto it; the selection is shared both ways (a row clicked in the list, or a selection made
  before opening, is what Enter lands on); an Enter or a landing that cannot go says why in a toast (no world chosen,
  not reachable, a file, busy); and a safety net closes the overview over any surface travel that has begun under it
  anyway (logged, counted for probes), so nothing waits invisibly.
- **Alternatives:** a seated landing with a get-up (above); a run on foot at walking speed (≥ 2 s to a far pad: too
  slow for direct navigation); a teleport to the pad (a cut); a trip from the overview hop by hop on the surface (the
  overview would have to close and replay the hops — a long way round for a world you pointed at).
- **Revisit if:** DJ finds the run too quick, too slow or too bouncy (`DepartRun*`), the feet-first turn too early or
  late (`LandingUprightSeconds`), wants Esc to undo a breadcrumb or path change too (it would need a navigator undo),
  or wants the overview's departure to show the traveller on its pad.

## AD-75 The walking camera returns to its usual pitch; the long jump's late take-off; the catch window 0.35 s (2026-11-19; amends AD-67, AD-69)

- **Problem:** DJ, after the fourth playtest: "After I look sharply up or down, beginning to walk should smoothly bring
  the camera back to its usual walking perspective. Let an active mouse drag take control immediately. Preserve my
  heading and the useful arrival framing … Overview … needs its own camera behavior." And: "First diagnose the known
  final-build failure where a second Space press wasn't accepted as a twirl. Establish reliable real-input behavior for
  the long jump and rim recovery. Then make the recovery challenge only slightly tighter. Change one parameter at a
  time, compare it with the current build, and retain visible effort for presses too late to save the character."
- **Reproduced first (camera, base build `cfee25d`, in-app `planets-look`):** after the arrival drags left the chosen
  pitch at −12°, a 1.2 s walk kept it (the next drag started from −12°); "while walking a drag down tilts the view
  down: −15.6° → −44.2° → −44.3° 2 s later, still walking". AD-67 had made a look stay put everywhere, walking included.
- **Reproduced first (Hazard):** the three `cfee25d` real-input runs: the long jump fell twice with the second press
  "buffered" at 0.367 s (and a third run's plain jump showed its take-off press as a "catch" at 0.100 s). The Core
  replay of the probe's own inputs at 60 fps (`The_real_input_long_jump_replayed_press_by_press`, on the code as it was):
  Space on the board (0.2–0.55 m before the edge) and again at 0.367 s crossed; Space 0.02–0.12 s after the feet left
  the board's edge (inside the coyote time) was the **catch**, which carried the walker back and spent the twirl, so the
  second press did nothing (the app's "buffered", which covered every press that did nothing) and W, still held, walked
  it off again into a fall — the real-input failure exactly.
- **Instrument defect (the probe):** the state file it timed presses from was written every 150 ms; at walking pace the
  walker moves ≈ 1 m between snapshots, so "Space when the edge is ≤ 0.55 m ahead" could arrive past the edge, the
  "early" catch press aimed at 0.04 s arrived at 0.13–0.17 s, the "late" one aimed at 0.33 s at 0.42–0.48 s (outside
  the window: two of the earlier runs' failures), and "the fall began at 0.583 s" was read late. In-app probes press on
  the frame and never saw it. Fixed: `--state-interval-ms=N` (instrument; the probe asks for every frame in `-Hazard`
  and `-Look`), and a press log in the state file (`planets.presses`: game time, seconds after leaving the ground, the
  jump's phase, vertical speed, height, open sky, metres to the leap's edge, twirls left, and what it did; "buffered"
  is split into `carry-ignored` / `spent` / `buffered`).
- **Product defect (the walker):** a press a frame or two late at the board — which a person also makes — turned the
  long jump into a catch. AD-69 made a coyote-time press over open sky the catch unless the coyote jump alone would
  land, and no plain jump lands across a leap.
- **Decision — the long jump (PLANET-85):** in the coyote time, a press after leaving a leap's take-off board heading
  across is the take-off (`HazardMoveParams.LeapBoard` 1.0 m before either end of a gap no longer than
  `LeapBoardMaxGap` 7.6 m — every leap the terrain makes at its jags —, within `LeapBoardMaxDegrees` 45° of across;
  `BrokenShell.LeapTakeOff`, asked of the ground last stood on). A slip off the board's side, any press after the coyote
  time and every other edge keep AD-69's catch.
- **Decision — the catch, slightly tighter (PLANET-86):** `Window` 0.40 → 0.35 s; nothing else changed. Measured with
  the same scripted slips (Core enumeration, 10 ms steps, 9 m gap) against the current values, each candidate alone:

  | Values | Walking, W held / let go: catch until, uncaught slip falls | Running, W held / let go |
  |---|---|---|
  | `cfee25d` (Window 0.40 s, ReturnReach 3.2 m) | 0.40 s / 0.40 s; falls at 0.40 s | 0.27 / 0.29 s |
  | **Window 0.35 s alone (chosen)** | **0.35 / 0.35 s; falls at 0.35 s** | 0.27 / 0.29 s (unchanged) |
  | Window 0.30 s alone | 0.29 / 0.29 s | 0.27 / 0.29 s |
  | ReturnReach 2.9 m alone | 0.40 / 0.40 s (unchanged) | 0.24 / 0.25 s |
  | ReturnReach 2.6 m alone | 0.40 / 0.40 s | 0.22–0.23 / 0.23 s |

  The window is what a walking player meets (DJ's slips are mostly walking); 0.35 s takes an eighth off it and makes
  the fall begin 0.05 s sooner; running stays as it was. Every later press is still answered: 0.36–0.80 s the fall's
  effort (flail, bump, cry), no dead press. The in-game tip reads the window ("within about 0.35 s").
- **Measured in-app (`planets-hazard`, real Space presses on the frame):** with the long-jump fix and the old window
  (0.40 s) 87/0: catches at 0.07, 0.23 and 0.38 s, an uncaught slip fell 0.43 s after it, fall to standing 3.8 s;
  final (0.35 s) 87/0: catches at 0.07, 0.23 and 0.33 s (the late press is now the window − 0.05 s), the slip fell
  0.38 s after it, fall to standing 3.7 s, every press in the fall an effort; the late take-off (0.083 s after the
  board) "coyote-jump", then "apex-twirl", across. Real input: not run in this stream (AD-75's probes are written and
  parse-checked; the desktop would not give the probe the foreground).
- **Decision — the camera (PLANET-84):** once the traveller moves over 0.8 m/s (the swing-in's threshold), the chosen
  pitch eases back to the usual 18° at `PitchReturnRatePerSecond` 2.5/s (time constant 0.4 s: ≈ 90 % back after 0.9 s),
  starting `PitchReturnDelaySeconds` 0.5 s after the last vertical drag. While a drag is held (`_orbiting`) nothing
  eases. Standing still keeps any look (PLANET-67's checks). Only the chosen pitch moves: the heading (yaw, and its own
  swing-in), the wheel's distance and the arrival and overlook framings, which are offsets on top (AD-67), are
  untouched — so each landing is still the designed arrival shot, and walking in the overlook view returns to the
  overlook's own 34°. Only walking frames run it: the overview (its own orbit, stream 4's), flights, hops and the fall
  do not.
- **Alternatives:** releasing the coyote rule at a leap by predicting jump + twirl (it would also turn early presses
  at a 9 m stretch into jumps into the sky); answering a press during the catch's carry (it is not too late; the carry
  lands in ≈ 0.4 s); shortening `ReturnReach` (only running changes); a dead band that keeps small looks while walking
  (the view would settle at the band's edge, not the usual perspective); returning the pitch at once on release (the
  view springs back under the hand).
- **Known limits:** a press during the catch's carry still does nothing (now logged as `carry-ignored`); the feel of
  0.35 s, of the 0.4 s return and its 0.5 s wait: DJ.
- **Revisit if:** DJ finds the return too brisk or slow (`PitchReturnRatePerSecond`) or wants small looks kept while
  walking; the catch too hard (Window back towards 0.4 s) or still easy (`ReturnReach` next, one at a time).

## AD-76 The selection card says what each action does here; Command Prompt stays usable after X; apps FileScape opens are not its children (2026-11-19; amends AD-28)

- **Problem (DJ):** "Audit the right-side panel's Open, Parent, Copy Path, Show in Explorer, and Fly to actions … Some
  appeared to do nothing or behaved unexpectedly." "After I close the game with X, the command remains busy until I
  press Ctrl+C."
- **Selection card, found on the base build** (`--automate=planets-selection-actions`, 13 failures): the labels were
  the legacy maps'. On the planets *Fly to* called the maps' camera and did nothing (every cell); F did something else
  (turned to face the target, or silently nothing when it was not on this world); *Parent (Esc)*: Esc clears the
  selection on a planet and closes the overview; *Open (Enter)* in the overview: Enter lands; the hub's path is the
  empty string, so Open and Show in Explorer handed `""` to Windows and Copy path copied nothing; Parent on the hub was
  disabled without a reason; after Parent in the overview, F and Enter acted on the world selected before.
- **Decision (card):** one rules table in Core (`SelectionActions.For(view, target)`): per view (map, Places, planet
  surface, overview) the labels, what is enabled and, for what is not, a tooltip starting "Unavailable:" with the reason
  and what to do instead. One handler per action serves the button and its key; a key pressed for an unavailable action
  says why. On a planet *Face it (F)* (enabled for what is on this world or in its sky); in the overview *Zoom to (F)*,
  *Open* without Enter; *Parent* without Esc outside the maps. The hub's Open shows This PC (`shell:MyComputerFolder`),
  its Show in Explorer, Copy path and Parent are disabled. The overview's F and Enter act on the card's selection.
  Copy path is recorded, not copied, under the test shell (it used to overwrite the real clipboard in probes).
- **Shutdown, found on the base build** (`tools/os-shutdown-probe.ps1`: an interactive cmd in its own conhost, the
  command typed through WriteConsoleInput, X as WM_SYSCOMMAND SC_CLOSE):
  1. `FileScape.exe` typed at the prompt: cmd never waits (it ran a typed `echo` while FileScape ran and 1.7 s after the
     close), but Godot's Windows build attaches to the parent console in native start-up (AttachConsole, before any C#)
     and printed ≈ 12 lines under cmd's prompt; after X the last lines were `[exit] … press Enter for a fresh prompt.`
     and no prompt: it looks busy until Enter or Ctrl+C. This is DJ's handoff command. (AD-28's notice did not prevent it.)
  2. `FileScape.console.exe` (Godot's console wrapper): it runs FileScape in a job object with kill-on-close and waits
     until **every** process in the job has ended. Anything FileScape started (an app opened with Enter, the Explorer
     from Show in Explorer, a terminal) joined the job: after X the prompt stayed busy as long as that app was open, and
     Ctrl+C killed the person's app. Demonstrated: with FileScape opening an `.xml` fixture in Microsoft Edge, Edge's
     browser process was FileScape.exe's child; after X no typed command ran for 12 s, and cmd returned 4.9 s after the
     probe closed Edge.
  3. `cmd /c` (both exes) returned 1.5 s after the close; a .bat waited while FileScape ran and returned after X; the
     wrapper with nothing opened returned 1.7 s after the close. The staged and ZIP-extracted layouts are the same files.
- **Decision (shutdown):** (a) when stdout is an interactive console that did not wait, FileScape lets go of it at once
  (FreeConsole) and the engine header is off (`application/run/print_header=false`), so nothing is written under the
  prompt; the log stays in Godot's log file and in full through FileScape.console.exe. (b) Under a job, what FileScape
  starts for the person is started by the desktop's Explorer (`DesktopShell`: IShellWindows → the desktop view →
  IShellDispatch2.ShellExecute), outside the job; Show in Explorer uses SHOpenFolderAndSelectItems (no child with
  FileScape's handles; before, `Process.Start explorer.exe` with inherited handles).
- **Alternatives:** make FileScape.exe a console program (a console window would open on every double-click); our own
  console launcher waiting only for FileScape.exe (a native build step this project does not have); breaking away from
  the job (the wrapper's job does not allow it); injecting Enter into the console at exit (it would run whatever the
  person had typed).
- **Trade-offs:** typed `FileScape.exe` shows no log in the terminal (use FileScape.console.exe); under the wrapper,
  Windows' refusal to open a type with no app is shown by Windows itself (Explorer reports nothing back), only "it is no
  longer there" is checked first.
- **Revisit when:** Godot lets an exported GUI build skip AttachConsole, or DJ's terminal is Windows Terminal and it
  behaves differently (not tested: a new tab could land in DJ's open Windows Terminal).

## AD-77 Discovery in play, a level of detail separate from discovery, varied miniature worlds, and moving through a large overview (2026-11-19; amends AD-71)

- **Problem:** DJ (fifth playtest follow-up, PLANET-90..93): keep the progressive Overview discovery, but let it run during
  ordinary play too (same scheduler, what he is visiting first, the game responsive); the breadth and depth overwhelm the
  view ("at the Ubuntu root, worlds overlap until they form solid shapes"); many miniature planets look uniformly pink;
  navigation through a large universe should be more dependable; and the intermittent "X pauses" failure must be closed.
- **Decision — discovery during play (PLANET-90):** the overview's `OverviewDiscovery` and `WorldExplorer.Discover`, unchanged
  in what they list and refuse, at a quieter **play pace** (`Limits.PlayListingsPerSecond` 2/s, `PlayMaxInFlight` 2, so two
  of the explorer's four workers stay free for the visit), only while walking (never during a flight, a landing or a hop),
  paused by the same X. The candidates are what the overview would draw (`UniverseLayout.Selection`, the layout's first
  pass without placing anything; rebuilt at most every 2 s when the tree changed), and the focus is a priority order — the
  current world's subtree, then its parent's (its neighbours), then its grandparent's, then the rest breadth-first
  (`Step(..., Pace.Play, focusInOrder: true)`). The overview's own order (selected and approached worlds equal) is unchanged.
  Because discovery lists your own world's subtree first, and each listing changes its signature, the world you stand on
  recomposes at most every 4 s while discovery is listing (`RecomposeGapWhileDiscovering`): without it the planet was
  rebuilt every 0.35 s and the walking p99 went from 9.1 to 27.8 ms; with it p99 is 5.6 ms with discovery on and off.
  `verify.sh` runs the overview discovery journey with `--test-play-discovery=off` (its 0 s picture is the visit's own
  listings) and the WSL-root journey likewise (a real root: nothing listed in the background during a probe).
- **Decision — a level of detail separate from discovery (PLANET-91):** the layout and the tree are untouched; a separate
  rule (`OverviewDetail`) decides ten times a second which laid-out worlds are drawn. A system opens when its ring is large
  enough on screen to hold its children apart (≥ 40 px radius, and its two closest children ≥ 16 px apart on screen: the layout gives small worlds small arcs, so an average over the ring was not enough); on the way to the
  selected world, the approached world (nearest the orbit's centre) and where you stand it is always open; below the
  selected or approached world the size bar is 0.6 × (never the spacing: lowering both, and treating where you stand as a
  focus, left 80 % of the drawn worlds overlapping on `C:\`, since everything on a root is below where you stand); a
  ring's on-screen size counts its foreshortening (an ellipse's circumference, r (1 + |n·v|) / 2); for each level a branch is away from that path the bar rises by 0.5 ×
  (distant branches shallower); an open system folds again only below 0.8 × its bar. Worlds fade in and out (6/s). A
  folded system wears a faint haze the size of its ring, and its name (shown for the four biggest folded systems in view)
  says "+N worlds inside" (the first wording, "N worlds folded in (come closer)", was cut off in the name tag). Discovery's candidates stay every laid-out world, so what is known never
  depends on what is drawn, and a folded world is never re-listed or forgotten.
- **Decision — varied miniature worlds (PLANET-92):** `OverviewWorldLook` in Core: the dominant kind of file picks the hue
  family (images rose, documents blue, code amber, audio violet, video indigo, archives rust, data cyan), a strong second
  kind (≥ 20 %) the second colour; a folder of folders takes one of eight mineral families (sand, slate, sage, ochre,
  teal, rust, lilac, stone) — the old Hearth salmon came from every folder of folders; a project is the workshop's teal;
  an empty folder pale. Pattern: banded (folders of folders with ≥ 40 entries below), spotted (one kind ≥ 70 %), swirled
  (three or more kinds), capped or plain. The seed is FNV-1a of the case-folded names of the folder and its parent, never
  the node id (ids follow listing order, which discovery makes vary between runs), so looks are stable across runs.
  Drawn by one shared `ShaderMaterial` (`overview_world.gdshader`), the look passed as the MultiMesh instance colour and
  custom data: still one draw for every solid world.
- **Decision — navigation (PLANET-93):** selection is the orbit anchor (as before: a click, and now [ / ] and Z, select,
  frame and centre the orbit on the world); the **wheel zooms toward the pointer** (each notch in over a world slides the
  orbit's centre 30 % of the way to it); **[ ]** step round the selected world's ring; **Z** goes up a level; a two-line
  hint at the top of the view on opening (9 s the first two times, then 4 s; it fades once two of its controls are used).
  No key travels; landing stays Enter or a double-click through the shared path.
- **Decision — the "X pauses" closeout:** a product defect seen through a strict instrument. `OverviewDiscovery.State` moved to
  Paused only at the scheduler's next step (every 0.1 s, i.e. every 6–7 frames), so for up to 0.1 s after X the indicator
  still said "Discovering… · X pauses"; the journey read it ≈ 80 ms after the key and failed whenever the next step had not
  run yet (2 of 16 repeated runs on the base exes, one of them under load, besides the two `check full` failures; reproduced deterministically in Core on the base source). Setting `Paused` now sets
  the state at once. The instrument was right to fail; it is unchanged.
- **Considered, not built:** raising the overview's 1,500-world layout budget now that drawing is separate (a relayout at
  1,500 already costs up to ≈ 10 ms on the main thread); per-world materials or textures (a material per world would end
  the single draw); collapsing by tree depth alone (a fixed depth cannot tell a wide root from a deep one; screen size can);
  discovery in play outside the scope rule (still only inside the arrival folder or below a visited place).
- **Revisit if:** DJ finds the play pace too eager or too slow, the folding too early or late (`ExpandRingPixels`,
  `ChildSpacingPixels`, `FocusFactor`, `BranchPenaltyPerLevel`), the haze too faint or strong, a colour family wrong for
  his folders, the wheel's pull toward the pointer too strong (`WheelTowardShare`), or [ ] and Z unhelpful.

## AD-78 The tutorial card in rich text; the way back's rim and spire; a picture gallery as the first content world (2026-11-19)

- **Problem:** DJ after the fourth playtest (PLANET-94..96): the Learn to play steps were plain sentences with the keys
  in a separate row of caps at the card's right edge, so the action and its keys had to be found; the way back differed
  from other pads only by a pale tint and the ▲ on its sign, which in a crowded world (Constellations: 29 pads, 8 moons)
  is lost among ordinary destinations; and none of the content worlds of `docs/next-worlds.md` (AD-61) could be played.
- **Tutorial card:** each lesson is written in a tiny markup (`*the action*`, `{Key}`) that Core turns into BBCode
  (`LessonText`): the action leads in bold gold, the keys sit as caps (lime on dark green, the beacon's colour) where the
  sentence uses them, and at most one short, quieter tip line follows. The separate key-cap row is gone; the lesson's
  key list stays as data for probes and the state file, so the real-input `-Tutorial` mode and the in-app journeys match
  titles and keys exactly as before. Alternatives: keeping the right-hand caps (the eye still had to jump between them
  and the sentence); styled `[code]` spans (monospace read as code, not keys).
- **Way back:** a **rim** (a thin glowing ring in the way back's sky blue round its pad: a different outline at ground
  level) and a **spire** (four thin rings that climb from 2.2 m to 8.2 m over the pad, narrowing to a small light, on a
  faint thread: a ▲-shaped silhouette that shows over the world's curve when the pad is below the horizon), plus a
  framed sign (the only sign with a frame). Only on the planet you stand on. Chosen over a column of light (the
  walkthrough's beacon is a lime column; the two must never be confused), over brighter petals (bloom washes the pads
  beside it), and over a taller spire (it would compete with the landmark and the sky's worlds). Rings rather than a
  solid cone: it stays a line drawing, restrained, and you see through it.
- **Content world — why the Gallery:** of the three proposals (and audio folders) it is the one whose interaction can be
  *played* now on generated data, and the one DJ named. The Arrivals Port (ranked first in AD-61) is mostly a sorted
  list: useful, but its payoff is a panel, and its recognition needs the Downloads known folder; the Reading Room's nook
  needs version-stack design; audio has a working preview already. The Gallery shows what a content world adds that a
  file manager does not: walking among your pictures and stepping into one.
- **What was built (a prototype, narrower than the sketch):** recognition by a person-made marker only
  (`.filescape-gallery`, the per-folder "yes" the sketch asked for before pictures are read while walking; no known
  folder or mix rules yet); in a gallery the easels load their thumbnails by themselves (nearest first, two at a time,
  at most 32 per planet); everywhere, R (or ⤢) on a picture's preview opens a full-window **viewer** that steps through
  the folder's pictures, zooms about the pointer, pans and fits, and closes back to where you stood with the preview on
  the last picture. No gallery path, month arches, contact sheet, video frames or saved overrides.
- **Bounds (consistent with the preview, FILE-02):** the preview's 24 MiB read limit; cloud placeholders and links are
  never read (decided from metadata, nothing is touched); the header's pixel count is checked before decoding
  (`ImageHeader`, 60 Mpx — the budget that before applied only after decoding) — this now protects the preview and the
  easels too; decoding on a worker, shrunk to 2,048 px for the viewer; one viewer decode at a time (quick steps keep
  only the latest wish); three decoded pictures kept; a placeholder while loading and the reason when a picture cannot
  be shown. Unmarked folders read nothing by themselves (unchanged).
- **Sample data:** `GallerySample` writes a mixed "Picture Gallery" (pictures painted by the Starter Sky's own painters at
  several shapes and sizes, a BMP, README, notes, a table, a sound, an archive, a subfolder) beside the Starter Sky in
  the app's own data folder (`--root=gallery`, or U from the Starter Sky), and with stress cases (damaged, 63 Mpx,
  over 24 MiB) as a fixture (`FileScape.Fixtures create-gallery`). No image files are committed.
- **Revisit if:** DJ finds the spire too quiet or too busy (heights, ring count and energies are named constants in
  `PlanetVisual.WayBack.cs`); the gallery should be recognised without a marker (the AD-61 rules) or ask "Show the
  pictures here?" on arrival instead; the viewer's memory budget should follow the screen size.

## AD-79 Music by activity, place and visit; a minimum dwell; the aurora voice (2026-11-19; extends AD-49, AD-72)

- **Problem:** DJ (PLANET-97..99), after the seven-mode reel: "My preference broadly declined through the sequence, with
  the first version strongest and the seventh unpleasant to my ear … The Lydian Overlook is beautiful. Try it in the
  overlook or telescope experience. Overview could also have its own identity … Keep the existing arrangements
  available." A small, understandable scheme for choosing variations (place identity, activity, revisits); phrases that
  have time to develop when moving quickly between folders; one DSP extension with an audible payoff.
- **Reproduced on the base code (Core, the app's calls):** twelve hops 1.5 s apart cut the music twelve times; each world
  sounded 3–5 notes of the theme's two-bar motif (10 notes), and a world met again resumed in its free section, so the
  theme was hardly heard. The overlook, the telescope and the overview played the world's music unchanged (nothing else
  existed in game; AD-72's pieces were offline).
- **Decision — the scheme, three questions in order (`SkySynth.Experience.cs`):**
  1. **Activity picks the piece** (`MusicExperience`): on a world (walking, jumping, flying, hopping) the world's own
     arrangement, chosen exactly as before (AD-49/52/60/66); on the overlook (standing on it, at its telescope, or its
     view on B) **the Lydian overlook**; in the overview (T) **the star chart**. The app tells the synth when the activity
     changes (`PlanetWorld.Music.cs`: the overlook after 0.4 s on it, left after 1.2 s off it or at once by a launch or
     hop; the overview at once). A change of activity is heard on the next beat.
  2. **Place picks the key and the mode family**: the world's theme gives the root, and the pieces (written in D) are
     transposed to it (−6…+5 semitones); the overlook is always Lydian; the overview is **Ionian**, or **Dorian where the
     place is already Dorian** (the WSL root, the deep woods, the underglow, a Theatre folder): 84 of 132 place × theme
     combinations Ionian, 48 Dorian. **Locrian is omitted** (no piece, not even as colour); Phrygian stays the hazard
     world's; Aeolian and Mixolydian are not used by the new pieces (the repository keeps its Mixolydian).
  3. **Visits pick the variation**: as for worlds (AD-49), a remembered experience resumes, a piece **at its next
     section** (overlook: intro, theme, answer, close; star chart: intro, short theme, answer, constellation, cadence), and
     a forgotten one starts on visit n's cycle n−1. The pieces' variations take turns by cycle: the overlook's first time
     is **the sketch exactly as DJ heard it**, the second light gives the theme to the glass with the flute a third below
     and the strings to the aurora pad; the star chart's second variation swaps the bell and the aurora lead.
  - **Minimum dwell** (`MinimumDwell`; the game sets `GameMinimumDwell` = **3.5 s**, `DwellPhraseBars` = **2**): a world's
     music, once begun, plays at least 3.5 s and then to its next two-bar phrase boundary before another world replaces it
     (two bars last 3.8 s at 126 BPM, 5.1 s at the hub's 94, 6.2 s for the orchestra's half-time theme), so every world
     plays at least the theme's motif. Until then a further change only retargets it (the music ends where the player
     is), a change back cancels it, and an arrival chord sounds in the key playing. **Never held:** a hazard world either
     way (danger and the way out), a change of activity, the first change after start. Core's default is 0 (the old
     next-beat change), so offline renders and the existing tests are unchanged; `SkyAudio` turns it on.
- **Decision — how they play live:** the pieces are `ExperiencePiece`s built once from the sketches' `SketchScore`s and
  played by a deck (`Arrangement.Score`) one sixteenth at a time through the same instruments as `RenderSketch`
  (`PlaySketchNote`, now taking a frame, group and transposition), so they cross-fade, remember and grid-lock like worlds
  and cost what a world costs (no offline buffers). New identities `Overlook` and `Overview` are appended after the
  world identities (`WorldIdentities` lists the ten). The overlook runs as half time over a 168 pulse so the effects' 32nd
  stays within the 80 ms bound (an 84 BPM 32nd is 89 ms).
- **Decision — the DSP extension, the aurora voice (`SkySynth.Analog.cs`, `Wave.Analog`):** an expressive subtractive
  voice in the spirit of classic analog polysynths: the existing polyBLEP saw stack (1–3 copies, detuned), a soft
  saturating drive (a Padé tanh with make-up), and a resonant topology-preserving state-variable low-pass (12 dB/oct,
  resonance to 0.95) whose cutoff follows its own attack–decay–sustain envelope, the velocity (softer is darker as well
  as quieter) and the amplitude's release; each note drifts a few cents. Three sounds: a pad whose filter opens over 2.2 s
  "like a sky", a soft brassy lead that blooms with each note, a round plucked bass. Used in the star chart (pad, lead,
  bass) and the overlook's second light (pad). Every other wave renders bit-identically.
- **Measured (Core, WSL, Debug):** behaviour baseline identical; the whole-cycle hashes of all ten existing scores at
  visits 1–3 identical (new lines added for Overlook and Overview). Pieces as played in game: overlook in Workshop 91 s
  peak 0.19, −26.0 LUFS, music voices ≤ 21 of 28; star chart Ionian −27 LUFS after +1 dB level (0.21 peak), Dorian likewise;
  no silent windows; the loudness test (all identities within 2 dB) passes with them included; every tempo-locked and
  hazard cue test passes over them. Aurora: ≈ 30 ns per voice per sample (0.13 % of a core in real time per voice; the
  saw-stack strings it extends 20 ns), extremes (MIDI 24–108, velocities 0.1–1) finite, peak 0.16; before/after −31.3 /
  −29.4 LUFS.
- **Alternatives:** the experience pieces as one-shot cues over the world's music (two keys at once, and no memory);
  pre-rendered loops (memory and no transposition); a seeded mode per folder for the overview (less understandable, and it
  would spread the modes DJ liked less); a dwell measured in time only (it could cut a phrase) or in phrases only (the
  orchestra's four bars are 12 s); the FM voice (FileScape already has many FM bells: the subtractive voice adds a colour
  it did not have).
- **Unknown:** nobody has listened. Whether the star chart and the second light are good, whether the aurora sounds belong,
  and whether 3.5 s feels right through hops are DJ's call by ear. **Revisit if:** DJ wants the overlook on B left out, a
  different dwell (`GameMinimumDwell`), Dorian for fewer places, or the aurora in a world's arrangement.

## AD-80 Every check ends with a timing table; the Core tests and the export stop costing avoidable minutes (2026-11-19, TEST-05)

- **Problem:** DJ: make testing time visible and reduce avoidable waits. `verify.sh` printed each step's seconds as it
  went, but no end-of-run table. A failure, an early exit or Ctrl+C left no total, and skips were not counted. Agents
  sharing the desktop also needed one lock per app launch, and the lock wait must not count as a step's time.
- **Measured before** (`check full` on `c8f5e96`, the reporting only, beside six other streams): 2,706 s of steps
  plus 4,866 s of launch-lock waits. The steps before the journeys took 1,007 s: tests 268 + 223 s and stage 493 s
  (including its smoke launch's lock wait), all inflated by the load. The quiet run that morning on `78a41f3` took
  2 + 154 + 148 + 31 s. The journeys took 1,696 s, and their launch overhead is ≈ 1.2 s each. On each host the Core
  tests' wall time is SkySynthTests (≈ 145 s run serially, as one xunit collection).
- **Decision:**
  - A table at the end of every run (`verify-lib.sh` `timing_table`, printed from an EXIT trap, also to
    `timing.txt`). Results are pass/fail/timeout/skipped/interrupted. A signal stops only the step the harness started.
  - `FS_LAUNCH_LOCK` takes a flock for each launch. The lock descriptor is closed in the child, and the wait is
    reported apart from the step's seconds.
  - The WSL tests run in the background beside the build, the Windows tests and the stage. Their outputs are
    separate (Directory.Build.props). They are waited for before the first journey.
  - The WSL tests run as three complementary filter shards, so each test runs exactly once and a shard without a
    result fails. Windows keeps one process: sharding there gained nothing, and six test processes at once pushed a
    best-of timing bound (60 ms) to 63.8 ms.
  - `stage --reuse` reuses a verified staging folder of the same `source_state` (commit + diff + untracked files).
  - `check journeys NAME...` runs only the named steps. Real-input steps run only when named exactly.
- **Not done (kept as they are):**
  - Journeys were not merged.
  - Frame-bound typing (`planets-dialog-keys`, 170 s) was not sped up: that would change frame pacing, which the
    checks depend on.
  - Fixed waits inside journeys are mostly settle times (≤ 4 s) before a measurement, and were not changed.
  - SkySynthTests was not split into classes, which would conflict with streams editing it.
- **Revisit when:** the Core tests exceed ≈ 2 min on a quiet machine, or a timing-bound test fails beside the other
  host's tests.

## AD-84 Every departure leaves from the launcher that leads there; Esc takes a dialog's move back; F again turns back (2026-11-20; extends AD-74)

- **Problem (DJ, sixth pass):** "Some dialog-based navigation still launches the character from wherever they stand."
  AD-74 sent every departure through one routine, but it looked only for the destination's *own* pad. Anything not a
  neighbour in this sky — a deeper Destinations row, Straight there, a favourite or repository, Ctrl+L, the breadcrumb,
  Locations, Ctrl+Home, Backspace to a place further away, the walkthrough's way back — had no pad, so it wound up where
  you stood with a burst. Esc stopped only departures whose navigator move was still to come, so a dialog's choice could
  not be stopped. F turned the camera with no way back.
- **Reproduced first (Core, the API the app calls):** `DepartureTests` builds the query each entry point builds. Against
  the earlier rule, 12 of 23 failed: exactly the entry points above. The 11 that passed are the ones that had a pad
  before (pad press, G, U, Backspace to a child, trip hops, a world here from a dialog), plus the pad-less, sky and cap cases.
- **Decision — the rule (`DeparturePlanner.Plan`, Core):**
  1. The launcher of the link the navigator went through (Role Link).
  2. Else the destination's own launcher, or the way back for the parent (Own, WayBack).
  3. Else, for a place below this world, the launcher of the child on the way down (Toward). A link the navigator went
     through elsewhere counts as where the way leads.
  4. Else, for a place not below, the way back: up and over (WayOut).
  5. Else no pad: a belt moon or a place below one, or the top of the tree.

  A pad press is always on its pad. The overview and a fall leave from the sky. The run's timing and cap moved to Core
  with the rule: 0.3 s plus 1 s per 30 m, at most 0.8 s, or 1.1 s on a Hazard World. Within 0.8 of a pad's radius
  you are on it, and no time is added.
- **Decision — the departure:** the app's `PlanetWorld.Depart` calls the planner for every entry point. It then runs
  to the pad, or winds up on it, or winds up where you stand. A pad that leads on charges as usual, and the wind-up looks
  up at the world it leads to. The warp then rises from that pad (the destination is not in this sky). A pad-less
  departure now *kindles* a soft ring of the world's accent at the feet as the wind-up begins (mark `kindle`), and still
  bursts at the release. A run starts from the feet's height: before, the overlook's platform or a jump dropped to the
  ground in the run's first frame. Ctrl+O's new root in a live world is now travelled to like any change. Before, it
  was cut to: `Reset` raises no change, and the world rebuilt on the mismatch.
- **Decision — Esc:** before the take-off, Esc stops every departure. A move a dialog already made is taken back with
  `PlaceNavigator.Revert`. It applies only when that change is the navigator's last move (`CanRevert`), and pops it
  as Back does, so current, the entry link and history are as before. The app hears a Back change to where it stands
  and does nothing. A Reset's move cannot be taken back, so Esc leaves it alone. The target stays marked visited and
  listed: that is harmless, and not undone.
- **Decision — F reversible (`FaceToggle<T>`, Core):** each Face records the heading before it and where things stood
  just after. The next F turns back if all of these hold:
  - the same world and the same selection;
  - moved ≤ 2.5 m (a shuffle, ≈ a third of a second's run);
  - the heading within 20° of what F turned to (turning by hand makes the old view stale);
  - within 20 s.

  Otherwise it is a fresh Face, and the stale record is forgotten. A departure forgets it. A toast says "Turned back
  to your earlier view (F faces it again)". Help (F1) and the card's tooltip say so. The overview's "Zoom to (F)" is
  not made reversible here: stream 2 owns the overview's camera, and `FaceToggle` takes any view type when it wants one.
- **Inventory of entry points (planets; the legacy views travel through their own gates and aerial view):**

| Entry point | Code path | Entry | Navigator moves | Launcher (rule) |
|---|---|---|---|---|
| Space on a pad | Walk → `BeginLaunch` | PadPress | at the release | its pad (on it) |
| G, the card's Go inside, a world/pad double-clicked, the planet list's or Destinations' Enter on a world or link here, the bench list | `LaunchToSelection` → `BeginLaunch` | GoInside | at the release | own; for a selection not here (chosen in the overview, say): toward / way out |
| Destinations, a folder deeper down (Enter) — a multi-hop trip | `StartTrip` → each hop `BeginLaunch` | TripHop | each hop at its release | each hop's own pad |
| Destinations, Straight there (Shift+Enter); a trip to a place not below | `TravelInto` → `GoTo` → `OnNavChanged` | NavigatorChange | at once (Esc reverts) | toward / way out |
| Destinations, a link deeper down | `TravelInto` → `FollowLink` (resolves) → `OnNavChanged` | NavigatorChange (via the link) | when the link answers | toward the link's folder |
| Destinations, the way back row | `TravelUp` → `OnNavChanged` | NavigatorChange | at once | way back |
| Destinations, a favourite or repository | `TravelInto` / `GoToPath` → `OnNavChanged` | NavigatorChange | at once (or when found) | own / toward / way out |
| U | `LaunchUp` | Parent | at the release | way back |
| Backspace (history) | `LaunchBack` | HistoryBack | at the release | own / toward / way out |
| Breadcrumb; Locations, This PC; Ctrl+L Go to path; Ctrl+Home | Main → `PlaceNavigator` → `OnNavChanged` | NavigatorChange | at once (Esc reverts) | own / toward / way out |
| Ctrl+O, a new root in the live world | `Reset` + `OnPlaceChanged` (new) | NavigatorChange | at once (not revertible) | toward / way out |
| Ctrl+O / Starter Sky / a rescan making a new tree | the world is rebuilt (an arrival, not a departure) | — | — | — |
| The walkthrough's start and "Back to the lesson" | `GoTo` → `OnNavChanged` | NavigatorChange | at once | own / way back / way out |
| A link answering late; a move during a flight | the departure is retargeted / launches on from the landing | NavigatorChange | — | as the rule says from there |
| The overview: Enter, G, double-click, the list or a trip while it is open | `BeginUniverseLanding` / `LandFromOverview` | Overview | at the dive's end | from the sky (the overview's own dive; stream 2) |
| The telescope | today E opens the Destinations list (rows as above); stream 5's survey calls `DepartFor(target, Telescope)` | Telescope | at the release | own / toward / way out |
| A fall off a Hazard World | `OnNavChangedWhileFalling` | FallWayBack | the fall's TravelUp | from the sky |
| The selection card's Face it (F), Parent; Home | not travel (a turn, a selection, a hop to the meadow) | — | — | — |

- **Alternatives:**
  - *A pad-less burst for every non-neighbour* (AD-74): the launch did not say which way you go.
  - *Walk the whole route*, through each world on the way: that is the trip (Enter), and it is slow.
  - *A dedicated "far travel" pad on each world*: a new landmark on every world, and the way down and the way back
    already say where the trip leads.
  - *Making every dialog move uncommitted* (the navigator moves at the take-off): the breadcrumb, Locations, Ctrl+L
    and the explorer's replies all move the navigator in Main, so a revert is smaller and covers them all.
- **Unknown / for DJ:**
  - whether "up and over" reads right for a far place;
  - whether the kindle is visible enough;
  - whether F's window (20 s, 2.5 m, 20°) feels right.

  **Revisit if:** DJ wants a far destination to show its route (the way down several levels), or the overview's Zoom
  to reversible as well.
