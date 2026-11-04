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
