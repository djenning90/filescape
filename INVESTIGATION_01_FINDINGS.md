# Investigation 01 — findings and first-experiment recommendation

Prepared by Claude Code, 2026-11-03, in response to `CLAUDE_INVESTIGATION_01.md`. Inputs read in full: that brief and `FileScape_Project_Handoff.md` including Appendix A. No ancestor `AGENTS.md`/`CLAUDE.md` applies.

Evidence vocabulary used throughout: **read** (source/docs inspected), **built**, **tested** (automated checks ran), **launched**, **visually observed**, **measured**. Nothing in this report was visually observed or performance-measured. Labels: **[F]** fact from a cited primary source or observed command output; **[I]** inference; **[U]** unknown/unverified; **[P]** preference (DJ's or mine, stated as such).

---

## 1. Summary

- **Recommendation [P, mine]:** first experiment in **Godot 4 (.NET / C#)**, Forward+ renderer, with the filesystem model, scanner and Windows shell actions in a plain .NET class library that the Godot project references. This keeps DJ's C# preference without a bridge, gives a real engine for the cinematic treatment (volumetric fog, SDFGI, SSR, glow, tone mapping), and builds/iterates from the existing tooling with the smallest install footprint.
- **Strongest alternative:** **Unreal Engine 5, C++ + Blueprints, no C# bridge.** Higher ceiling for the cinematic treatment (Lumen, Nanite, Fab asset content), at the cost of a large install, a Visual Studio 2026 C++ toolchain (none present), slower iteration, C++ for the filesystem core, a 5 % royalty above US$1 M, and unmeasured Lumen behaviour on an iGPU (Lumen with instanced meshes also requires Nanite). Do not add a C# bridge (see §4).
- **Not recommended as foundation:** forking `sp00nznet/fsvng` (FSV-descended treemap/radial layouts, immediate-mode per-frame mesh rebuild, ANSI-path handling, LGPL obligations — §3). Treat it, `fsv`, and `fsnav` as **idea/algorithm references only**; no code copied.
- **Decision gate before committing:** one bounded rendering spike (§7.6) on the 890M to see whether Godot's fog/GI looks "cinematic enough" at 1920×1200 and whether instanced geometry + labels hold frame time. If Godot fails the visual gate, rerun the same spike in Unreal before any FileScape code is ported.

---

## 2. Environment evidence and proposed Windows workflow

### 2.1 Observed (commands in §9)

| Item | Observation | Differs from manager check? |
|---|---|---|
| Host | WSL2 `Ubuntu-24.04` (Ubuntu 24.04.4 LTS), kernel 6.18.33.2, WSL 2.7.12.0, WSLg 1.0.73.2, systemd on | — |
| Git | `/home/david/src/filescape` is a Git repo on `main`, **no commits**, both management docs untracked. (The harness reported "not a git repository"; `git status` shows otherwise.) | Consistent with manager's note |
| Windows | Windows 11 Pro 10.0.26200 (build 26200.9711), 64-bit | — |
| Machine | Lenovo 21RX000JUS; AMD Ryzen AI 9 HX PRO 370 (12 C / 24 T); ~87.6 GiB RAM visible; battery present, on AC at check time | New detail |
| GPU | AMD Radeon 890M (integrated, RDNA 3.5 per AMD marketing [U: not checked against AMD docs]); driver 32.0.22024.19001; registry `qwMemorySize` = 8 GiB (UMA carve-out; shared with system RAM); `AdapterRAM` reports 4 GiB (32-bit field, unreliable) | New detail |
| Displays | Primary internal panel `1920×1200@60` per CIM; Windows Forms reports `DISPLAY1` bounds 1536×960 (logical, i.e. ~125 % scaling) and a second 1920×1200 display above it | New detail |
| DirectX | `dxdiag` not run; DirectX registry key only shows legacy `4.09.00.0904` (meaningless) | [U] D3D12 feature level / Vulkan version not established |
| .NET (Windows) | SDK 10.0.403; runtimes NETCore 8.0.33 & 10.0.14, WindowsDesktop 8.0.33 & 10.0.14 | Manager only checked WSL SDK (10.0.400) |
| .NET (WSL) | SDK 10.0.400 | Same |
| Windows tools on PATH | git, node, winget, `wsl.exe`; `python.exe` is the WindowsApps alias (likely Store stub [U]) | — |
| Not found (Windows) | Visual Studio / vswhere / MSVC `cl` / clang / MSBuild (standalone) / CMake / Windows 10 SDK (`Windows Kits\10`) / Godot / Unreal / Epic Games Launcher folder | New: confirms no C++ toolchain on Windows |
| IDE | JetBrains Rider 2026.2.1 installed on Windows | New |
| Long paths | `HKLM\…\FileSystem\LongPathsEnabled = 0`; Developer Mode (`AllowDevelopmentWithoutDevLicense`) = 1 | New |
| `bwrap` | Not on WSL PATH (same as manager); my shell ran normally | — |

### 2.2 Workflow probe (built + launched, not a FileScape artifact)

A throwaway `net10.0-windows` console app in the session scratchpad (outside the repo) was built with **Windows** `dotnet.exe` directly from its `\\wsl.localhost\Ubuntu-24.04\…` UNC path, then launched from that UNC path. Observed: build succeeded in 3.6 s; process reported `Microsoft Windows 10.0.26200`, X64, .NET 10.0.14; saw `C:\Windows`; created a 472-character Unicode path under Windows `%TEMP%` (30 nested `segment_NN_ü` dirs + `zero_日本.txt`), enumerated 31 entries, and deleted it. **This shows .NET 10 handles >260-char paths even with `LongPathsEnabled=0`** for the APIs used [F for this probe; I that it generalises to all .NET IO APIs — .NET Core documents automatic long-path handling, Win32 shell APIs do not get it].

No NuGet packages were downloaded (framework-only project).

### 2.3 Proposed workflow

- **Source of truth stays in `/home/david/src/filescape`** (no relocation). Edit from WSL or from Rider via `\\wsl.localhost\Ubuntu-24.04\home\david\src\filescape`.
- **Pure .NET libraries and xUnit tests:** build/test in WSL with the Linux SDK for fast iteration of platform-neutral logic (layout, model). Anything touching Windows (scanner attributes, ShellExecute, reparse points) is tested only by **Windows `dotnet.exe`** invoked from WSL (`powershell.exe -NoProfile -Command "dotnet test '\\wsl.localhost\…'"`), because a Linux run says nothing about NTFS behaviour.
- **Engine projects:** [I] engine editors on a `\\wsl.localhost` path are a known risk (file watching, import caches, performance, Unreal's build tooling rejecting UNC paths). Proposed mitigation, *to be validated in the spike, not assumed*: keep the engine project in the repo, and either (a) open it directly via UNC and measure import/reload, or (b) if that misbehaves, have a one-way sync script mirror the repo to `C:\dev\filescape-mirror` for editor/build only, with the WSL repo authoritative. Option (b) would be a deviation the manager should approve; it is not a relocation of the workspace.
- **Runtime target paths** are always Windows paths (`C:\…`, `D:\…`, `\\server\share\…`). The app never scans `\\wsl.localhost` or `/mnt/c` as evidence of Windows behaviour.
- **Debugging:** Rider (already installed) supports Godot .NET debugging [U: exact Rider/Godot plugin state on this machine not checked]; Visual Studio Community would be required for Unreal C++ debugging.

### 2.4 What I could not inspect

GPU feature level/Vulkan support, VRAM budget as seen by D3D12/Vulkan, shader-compile behaviour, any rendering performance, Rider plugins, Defender/SmartScreen interference, whether a Store Python is real. No engine was installed.

---

## 3. Existing work: source and licence findings

All three repositories were cloned read-only into the session scratchpad (outside the repo) and inspected; none were built or run. GitHub metadata via the public REST API.

### 3.1 `sp00nznet/fsvng` — inspected at `ec49689bb4a5d6277f2d3149068fd618f5bb86bb` (2026-10-19)

**Activity [F]:** 17 commits, 2026-02-08 → 2026-10-19, two author names (`Ned Heller`, Feb 2026; `sp00nz`, Oct 2026); 0 stars, 0 forks, 0 open issues; one release **v0.10.0** (2026-10-19) with Windows/Linux/macOS archives. Single-maintainer, very young, burst activity.

**Organisation [F, read]:** ~11.6 k lines C++17 in `src/{core,geometry,camera,animation,renderer,color,ui,app}`, 7 GoogleTest files, GLSL 3.3 shaders, CMake with `FetchContent` for SDL2 2.30.10, Dear ImGui 1.93.0-docking, GLM 1.0.1, glad 2.0.8 (needs Python+jinja2), nlohmann/json 3.11.3. CI on GitHub Actions (Windows/Ubuntu/macOS). Emscripten/WebGL2 build. Architecture doc: singleton subsystems driven from `App::run()`.

**README claims vs source:**

| README claim | Source finding |
|---|---|
| "Open" on Windows | [F] `src/ui/Dialogs.cpp:333-340` uses **`ShellExecuteA`** (ANSI) with `"open"` / `"explore"` on a UTF-8-ish `std::string` path. [I] Non-ASCII paths are likely to fail. |
| Explorer context menu | [F] `Dialogs.cpp:229-325`: real `SHParseDisplayName` → `SHBindToParent` → `IContextMenu::QueryContextMenu/TrackPopupMenu/InvokeCommand`, converting path from UTF-8. Reasonable reference pattern (no `IContextMenu2/3` message forwarding, so owner-drawn submenus such as "Send to"/"Open with" may be incomplete [I]). |
| "Properties" | [F] An **ImGui** dialog showing name/path/size/counts (`Dialogs.cpp:366-400`), **not** the Windows shell Properties sheet. |
| Background scan, responsive UI | [F] `std::thread` running `FsScanner::scan`, atomic `cancelRequested` checked once per directory, progress every ~100 ms. [F] Tree becomes visible only on completion (`MainWindow::finishScan`); **no incremental scene**. |
| Unicode / Windows paths | [F] `FsScanner.cpp:95` stores `entry.path().filename().string()` inside `try { } catch (...) { skip }`. [I] With MSVC, `path::string()` converts to the ANSI code page and throws for unrepresentable characters (no UTF-8 manifest / `/utf-8` setting found), so such entries would be **silently dropped**. [U] not runtime-verified. |
| Metadata | [F] logical size via `file_size`; allocated size via `GetCompressedFileSizeW`; `atime = ctime = mtime` (copied, not real); permissions are POSIX-style approximations; no hidden/system/reparse/cloud attributes. |
| Links/junctions | [F] Uses `symlink_status`, recurses only for `file_type::directory`; max depth 128. [I] On MSVC junctions report a non-directory type and are therefore not traversed — safe, but shown as "unknown" rather than as links. |
| Rendering | [F] `MapVLayout.cpp:470-495`: comment "Rebuild mesh each frame"; builds vertex vectors and **creates/uploads a new `MeshBuffer` per directory per frame**. Picking = colour-ID offscreen FBO + `glReadPixels` (`NodePicker.cpp`). Labels = ImGui 2D overlay with overlap rejection; `TextRenderer` is a stub (ROADMAP). Not DPI-aware (ROADMAP). |
| Visualizations | [F] MapV (treemap) and TreeV (radial); DiscV removed. These are **FSV** layouts, not FSN's pedestal-and-wire landscape. |

**Licence [F]:** `LICENSE` = LGPL-2.1 text; `NOTICE` says LGPL-2.1-or-later, layout/camera/animation code "ported from fsv's geometry.c, camera.c, animation.c and colexp.c, and stay under that licence"; release binaries distributed under LGPL-3 because glad's generated loader includes Apache-2.0 Khronos content. **No per-file licence headers** in `src/`. The Windows release zip (downloaded to an isolated scratch folder, **listed, not executed**, SHA-256 `cca8f56e…291fe`) contains `fsvng.exe`, shaders, LICENSE, LICENSES/ (glad, glm, GPL-3.0, imgui, LGPL-3.0, nlohmann-json, SDL2), NOTICE, README.

**Reusability for FileScape [I]:** low. The parts that are FileScape-relevant (scanner, shell actions) are small, have the Unicode defects above, and would be rewritten for C#/an engine anyway. The layout/camera algorithms are FSV, not FSN, and any port carries LGPL-2+ derivative-work obligations. The renderer and UI are tied to SDL2/ImGui/GL 3.3 and the per-frame mesh rebuild would not scale to cinematic scenes.

### 3.2 `jtsiomb/fsnav` — inspected at `0591f26b50987041d6c03deb3a671c997e943bea` (2026-07-17)

[F] 18 commits 2009 → 2026 (mostly 2009–2016; small fixes 2021/2025/2026); 78 stars; release v0.1 (2025-07). ~2.1 k lines C/C++ in `src/` plus bundled `vmath` and `image` libraries; GLUT + legacy OpenGL + FreeType + libpng/libjpeg; Makefile (Cygwin target dated 2009). README: double-click to move to directory, drag to rotate/zoom, click or space to show file attributes, optional quad-buffer stereo. Scanner (`src/fstree.cc:45-95`) uses `opendir/readdir`, **`chdir` into each directory** and **`stat` (follows symlinks)** — [I] cycle-prone and not thread-safe. Picking is CPU ray-vs-box (`FSNode::intersect`, `Dir::find_intersection`). It is closer in spirit to FSN (pedestals + links) than FSV.

**Licence [F]:** GPL-3.0-or-later (README + `COPYING`). **Flag:** `data/courbd.ttf` is Monotype/Microsoft *Courier New Bold* (font metadata says "All Rights Reserved"), not GPL-compatible redistributable content [I]. Use as an idea reference only; GPL would bind any code copied.

### 3.3 `mcuelenaere/fsv` — inspected at `4ef9040fcf14cddd52d79dad87a80355fc885602` (2021-04-07)

[F] **Archived**; README says "not actively maintained"; 543 stars, 67 forks. Original 1999 fsv by Daniel Richard G. with patches; C, GTK+ 2 + GtkGLExt, autotools; ~13 k lines in `src/`. `scanfs.c:71` uses `lstat` (does not follow links). Picking via legacy `GL_SELECT` (`ogl.c:223-239`); text via `GL_FEEDBACK` (`geometry.c:3121`). Per-file headers: LGPL-2-or-later (e.g. `src/geometry.c`). Value: historical reference for MapV/TreeV/DiscV layout and camera easing, which fsvng already ports.

### 3.4 Other handoff leads

Not pursued: `thiekus/fsvwin` (older GTK2 port of the same lineage — would not change the conclusion), `codeberg.org/squidd/fsvng` (Rust/Bevy; could inform large-tree handling but not the stack choice for a C#-preferring Windows app), `unixmonkey/fsn_jurassic_park` (scene/colour reference, useful later for an FSN-film preset), others (unavailable/secondary).

### 3.5 Three distinct reuse options

1. **Fork fsvng** — inherit a working Windows app with FSV layouts, C++/SDL2/ImGui/GL3.3, LGPL-2.1+/LGPL-3 binary obligations for the whole fork. Contradicts the C# preference and the immersive-engine direction; would need rewrite of scanner, renderer and Unicode handling. **Not recommended.**
2. **Selective code reuse** — e.g. port fsv/fsvng layout or camera code into FileScape. Ported code stays LGPL-2+ (fsv) or GPL-3+ (fsnav), which constrains FileScape's licence choice and requires per-file attribution. **Not recommended for the first experiment**; revisit only if FileScape intentionally adopts a compatible licence and a specific algorithm proves worth it.
3. **Independent implementation, informed by ideas** — re-derive layouts from the FSN manual/Appendix A and general knowledge (pedestals, wires, file grids, height exponent), using these repos only to learn pitfalls (Unicode, per-frame rebuilds, link-following, label overlap). Keeps the licence decision open. **Recommended.**

Licence questions for DJ (not decided here): FileScape's own licence; whether copying any LGPL/GPL code is acceptable at all; engine EULA acceptability (§4).

---

## 4. Stack comparison

Sources were fetched on 2026-11-03 (URLs in §9.3). Some Epic pages could not be retrieved (licence page HTTP 403 to the fetch tool; API reference and packaging pages rendered empty). Those points are marked [U] or "snippet": the latter means the text came from a search-result excerpt of the official page.

### 4.1 Facts by candidate

**Unreal Engine 5 (docs show 5.8)**
- [F] Programming is C++ plus Blueprints. Epic's docs give no official C#/.NET scripting route.
- [F] Lumen software ray tracing needs DX12 SM6. The Lumen technical page cites "GTX-1070 or higher". The hardware/software spec page says Lumen GI/Reflections need "AMD RX-6000+, Intel Arc A+, or NVIDIA RTX 2000+", and recommends 32 GB RAM and 8 GB+ GPU memory. Integrated GPUs are not mentioned. [I] The 890M is RDNA 3.5 with an 8 GiB UMA carve-out, so it is plausibly in the supported feature class. iGPU performance is [U].
- [F] Lumen's surface cache supports ISM/foliage "only if the mesh is using Nanite". Nanite needs DX12 SM6.6 atomics or Vulkan and works with ISM/HISM.
- [F] ISM/HISM offer Per Instance Custom Data (floats readable in materials). Collision, material and shadow settings are per component. [U] Per-instance hit index (`FHitResult.Item`): the API page was empty. I expect it from prior knowledge but did not verify it.
- [F] Volumetric fog cost is quoted as ~1 ms on PS4 at High and ~3 ms on a GTX 970 at Epic.
- [F] Toolchain: Visual Studio 2026 18.0+ is recommended, MSVC ≥ 14.38, Windows SDK ≥ 10.0.22621, .NET 10 required. **None of the C++ toolchain is installed** (§2.1).
- Licence (snippet + Epic pricing blog): 5 % royalty on lifetime gross above US$1 M for apps that use engine code at runtime. Seat fees apply only to non-runtime use by companies with more than $1 M in revenue. [I] FileScape would fall under the royalty model, and the Unreal EULA is not an open-source licence. DJ should re-read the licence page by hand.
- [U] Editor install size and Windows packaging steps were not retrieved.

**Godot 4 .NET (stable docs 4.7; latest release 4.7.2, 2026-08-18)**
- [F] C# is supported on desktop. The C# basics page says ".NET 8 or later". C# projects cannot export to the web. Windows SDK 10.0.403 and runtime 8/10 are already installed.
- [F] MultiMesh supports per-instance colour and custom data. Culling is per MultiMesh, not per instance; the docs advise splitting by area. It has no picking/collision API, so per-instance picking needs our own ray-vs-bounds. That fits §6.1, which puts picking in Core anyway.
- [F] Label3D does billboarded 3D text. Large font sizes can stutter while new glyphs are rasterised.
- [F] SDFGI, VoxelGI, volumetric fog, SSR and SSIL require the Forward+ renderer (Vulkan/D3D12, with automatic fallback since 4.4). SDFGI is "viable for procedurally generated levels"; LightmapGI is not, because it bakes in the editor. The GI page recommends offering a toggle to disable SDFGI/VoxelGI on integrated graphics. This matches the Classic/Cinematic presets.
- [F] The active scene tree is not thread-safe. Use `call_deferred`, or build off-tree on one thread. This matches the "scanner publishes batches, main thread merges" design.
- [F] Windows export supports signing via SignTool/osslsigncode. [U] .NET export layout details not retrieved.
- Licence: MIT [I: the page fetched didn't restate it; Godot's MIT licence is long-standing].

**Stride (4.4.1, 2026-10-17)**
- [F] MIT licence. Builds with the .NET 10 SDK. Recent releases overhauled Vulkan/D3D12, rewrote the shader compiler, and fixed a Vulkan instanced-draw bug. It has an Instancing component and voxel GI.
- [F/I] Several manual pages were empty or 404, so the docs are noticeably thinner than Godot's or Epic's. [U] The Game Studio editor is believed to be Windows-only; per-instance colour and fog quality are unverified.

**Desktop .NET viewport**
- [F] Helix Toolkit v2 is "in maintenance mode", and v3 is still SharpDX/DX11. Silk.NET provides raw bindings only. [I] Neither is credible for the cinematic goal without writing a renderer, so both are excluded from the shortlist.

### 4.2 Comparison (I = inference/judgement)

| Criterion | Unreal C++/BP | Godot .NET | Stride |
|---|---|---|---|
| Cinematic ceiling | Highest (Lumen, Nanite, volumetrics, Fab assets) [F/I] | Good: SDFGI, volumetric fog, SSR, glow; below Lumen [I] | Moderate; voxel GI exists; fewer worked examples [I] |
| 890M fit | Officially in class, unmeasured; Lumen+ISM requires Nanite [F]; heavy [I] | Docs explicitly address iGPU toggles [F] | Unknown |
| Instancing / per-instance data | ISM/HISM + custom data [F] | MultiMesh colour/custom data [F] | Instancing component [F] |
| Picking | Physics trace, instance index [U] | Own ray-vs-AABB (planned anyway) [F/I] | Own ray-vs-AABB [I] |
| Labels | Text Render / UMG; limits [U] | Label3D + 2D overlay [F] | SpriteFont/UI [U] |
| Async scan integration | C++ threads/tasks; or a .NET core would need a bridge [I] | Plain .NET `Task`/`Channel` + `call_deferred` [F/I] | Plain .NET [I] |
| Windows shell (ShellExecuteEx, IContextMenu) | Native Win32 from C++ [I, easy] | P/Invoke / CsWin32 from C# [I, easy] | Same as Godot |
| Language fit (DJ prefers C#) | C++ for core; no bridge recommended | C# throughout | C# throughout |
| Iteration | Slow C++ compile, Live Coding; large editor [I] | Fast; small editor; C# rebuild ~seconds [I] | Medium [I] |
| Install footprint now | VS 2026 C++ + Win SDK + UE: tens of GB [I] | Editor zip; .NET already present [F/I] | NuGet/launcher; .NET present [I] |
| Deployment | Packaged build; royalty terms [F snippet] | Export exe + .NET assemblies; MIT [F/I] | .NET app; MIT [F] |
| Asset authoring | Best tooling and marketplace [I] | glTF import, decent [I] | Adequate [I] |
| Maintainability/community | Large, Epic-controlled [I] | Large, open [I] | Small [I] |
| WSL/Windows friction | Highest: UNC paths, MSVC toolchain [I] | Moderate: editor on UNC unverified [U] | Moderate [U] |

### 4.3 Unreal and the C# bridge question

Taking Unreal directly means writing the scanner and shell actions in C++. That code is small (§3.1 shows the pattern) and needs only Win32 calls, which C++ handles natively. A C# core hosted in Unreal (an embedded CLR, an unofficial plugin such as UnrealSharp, or a separate process speaking IPC) would add a second runtime, marshalling of large node arrays, and an unofficial dependency that has to track engine updates. I see no demonstrated benefit beyond language preference. **If Unreal is chosen, go C++/Blueprints without a bridge.**

### 4.4 Recommendation

**Godot 4.7 .NET** is the recommendation [P, mine], for four reasons:
- It satisfies the C# preference without a bridge.
- Its cinematic feature set (fog, SDFGI, SSR) works with procedurally generated scenes, which is FileScape's case.
- It needs no C++ toolchain install.
- Its docs explicitly support the iGPU quality-toggle approach this laptop needs.

**Strongest alternative: Unreal C++/BP.** It is the right switch if the spike (§7.6) shows Godot's look falls short of DJ's bar. Stride is a weaker third: MIT and C#, but the thinner documentation and smaller community raise risk without a visual advantage over Godot.

---

## 5. Requirement classification

| Item | Classification | Notes |
|---|---|---|
| Pedestals + wires + file blocks (VIS-01), height=size / colour=age (VIS-02), exaggerated-height exponent 0.5 (VIS-03), directory totals (VIS-04) | **Historical reference** → **Prototype** (classic geometry) | Prototype uses one mode: file height ∝ size^0.5, capped; directory pedestal from direct-file total. Other modes later. |
| Hover names, selection spotlight (VIS-05) | Historical → **Prototype** | |
| Shrink-on-approach, extra perspective narrowing (VIS-06/07) | Historical → **Later** | Fidelity details. |
| Landscape themes (VIS-08) | Historical → **Later** (as themes) | |
| Click-to-select-and-approach; shift-click select only; connector→parent (NAV-01/02) | Historical → **Prototype** (approach + parent nav) | Mouse/keyboard adapted to Windows norms. |
| Middle-drag free travel (NAV-03/04) | Historical → **Proposed MVP** | Prototype: orbit/pan/zoom + WASD fly. |
| Reset / back (NAV-05) | Historical → **Prototype** (reset + back) | Bird's-eye later. |
| Warp mode, row travel, overview, split views, marks (NAV-06..10) | Historical → **Later** | |
| Open/execute via OS (FILE-01) | Accepted direction → **Prototype** (explicit Open only) | Executables: Open prompts confirmation in prototype. |
| Text viewer, print, copy/move/link, staged delete, context menu (FILE-02..06) | Historical → **Later** (shell context menu = MVP candidate) | |
| Root selection (IDX-01) | Historical → **Prototype** | |
| Cached DB, live monitoring, prune, persistence (IDX-02..08) | Historical → **Later** | |
| Filesystem-type boundary (IDX-03) | Historical → **Prototype policy** (§6.4) | |
| Search/path entry (UI-03) | Historical → **MVP** | |
| Async scan with progress + cancellation, metadata inspector with true numbers, inaccessible-entry handling, Unicode/long paths | **Accepted direction / proposed Windows additions** → **Prototype** | |
| Cinematic treatment (one bounded district) | **Accepted direction** → **Prototype** (bounded) | |
| Forests, cities, alien worlds, weather, sound, marketplace/downloadable themes, authoring tools | **Later ambitions** | |
| Mutations, watchers, full-drive scans, split views, plugin system | **Excluded** from first experiment | Per brief. |

---

## 6. Minimal architecture sketch

### 6.1 Boundaries (four modules, no plugin framework)

```
FileScape.Core (netX class library, no engine refs)
  Model       NodeId(int), NodeStore (struct-of-arrays), FsNodeInfo, ScanStatus flags
  Scan        IScanSource → WindowsScanner | SyntheticFixtureSource ; ScanSession (progress, cancel)
  Shell       IShellActions → WindowsShellActions (ShellExecuteEx open/properties/select-in-Explorer) | MockShellActions
  Layout      ILayout.Compute(NodeStore, LayoutParams) → LayoutResult (per-NodeId transform, bounds, slot)
  Selection   SelectionState (NodeId selected/hovered, nav history)        ← lives here, not in the scene

FileScape.App (engine project)
  Visualization  IVisualization.Build/Update(LayoutResult, NodeStore) → batched render data
                   ClassicFsnVisualization, DistrictVisualization
  Theme          data asset: palette, sky/fog/lighting/material params (per visualization)
  Interaction    camera rig, picking (ray → NodeId), inspector UI, commands
```

- **Identity:** `NodeId` = dense index into `NodeStore`, stable for the lifetime of a scan. Path is derivable (parent chain + name). Selection, hover, history and inspector all hold `NodeId`; render instances map `instanceIndex → NodeId` through a per-batch array. Switching visualization rebuilds batches but keeps `SelectionState`, so the same node stays selected and the camera re-targets its new position. Persisted identity across rescans (bookmarks) would use path + file ID (`FileId`/volume serial) — later.
- **Scan → model → layout → visual** flows one way. The scanner publishes immutable batches (e.g. "directory X fully enumerated: N children") on a channel; the main/render thread merges batches into `NodeStore` once per frame within a time budget. No engine objects are touched off the main thread.
- **Theme vs visualization:** a *visualization* = layout choice + mapping of node attributes to geometry (pedestal/block vs district/tower). A *theme* = parameters applied to a visualization's look (colours, sky, fog density, material set) without changing positions. Prototype ships two visualizations and one theme each; theme switching is not in scope.
- **Spatial stability:** layout of a directory depends only on its own children, sorted deterministically (directories then files; by name, ordinal case-insensitive), so a late-arriving subtree grows in place without moving siblings' contents. Parent spacing may expand when a child's footprint grows; the prototype records how often that happens rather than promising zero movement.
- **Size transformations:** visual height = `min(hMax, hMin + k·size^0.5)` (FSN's exponent default); footprint fixed per file in classic; in the district, footprint fixed and height transformed similarly (volume is *not* proportional to size, and the legend says so). The inspector always shows exact bytes (logical and, where supported, allocated) plus the mapping in use and whether the value was capped.
- **Batching/visibility/labels:** one instanced mesh per (visualization, mesh kind), per-instance transform + colour/custom data; directory batches can be chunked for culling. Labels: only for the selected node, hovered node, its siblings and the N nearest on-screen nodes (N≈64, proposal), screen-space with overlap rejection; distant directories get summary labels. Picking: CPU ray vs layout AABBs (the layout already has bounds; independent of render engine), which keeps picking testable without the engine.

### 6.2 Scanner policies (prototype)

| Case | Policy | Visible as |
|---|---|---|
| Reparse points (junctions, symlinks, mount points, other tags) | **Never traverse.** Record as a leaf with `ReparsePoint` flag and tag if available. Important: .NET's `FileSystemEnumerator` recursion has **no reparse-point check** by default (dotnet/runtime source, §9.3), so the scanner must recurse manually or set `ShouldRecursePredicate` to exclude `FileAttributes.ReparsePoint`. A fixture test asserts the junction loop is not followed. | Distinct marker; inspector shows "link — not followed". |
| Cycles | Impossible under "never traverse"; also cap depth (e.g. 256) as a guard. | Status flag if cap hit. |
| Volume/share boundaries | Do not cross to a different volume (compare volume serial); root may be any local drive path or UNC share chosen by the user. | "Different volume — not scanned". |
| Hidden/system | **Include and flag** (don't silently skip). .NET's `EnumerationOptions` defaults to `AttributesToSkip = Hidden \| System` and `IgnoreInaccessible = true` [F], so set `AttributesToSkip = 0` and `IgnoreInaccessible = false`, then record failures ourselves. | Dimmed + attribute list in inspector. |
| Access denied / vanished during scan | Record node with `Inaccessible`/`Vanished` status, count it, continue. | Red marker; per-scan error count. |
| Long and Unicode paths | Use .NET APIs throughout (probe showed >260 chars working); pass full paths to shell APIs via `ShellExecuteEx` W variants; shell APIs may still fail on >MAX_PATH — report the error. Fixture includes such names. | Error surfaced in status line. |
| Logical vs allocated size | Show logical always. Allocated via `GetCompressedFileSizeW` only on explicit inspector request (avoids per-file syscalls during scan). | Labelled "allocated (on disk)". |
| Hard links | Prototype counts each name (double counts); inspector shows link count when requested and a note. | "Hard links: n — totals may double count". |
| Cloud placeholders (OneDrive etc.) | Detect `RECALL_ON_DATA_ACCESS`/`RECALL_ON_OPEN`/`OFFLINE` attributes; never open file contents during scan (metadata only, so no hydration [I — verify]). Show logical size with "cloud placeholder" flag. | Cloud marker. |
| Sparse/compressed | Flag attributes; logical size primary. | Attribute list. |

Unsupported behaviour is flagged, never silently normalised.

---

## 7. Next implementation brief (Experiment 02)

### 7.1 Goal

A runnable Windows build that scans a user-selected real directory (or the synthetic fixture), shows it as classic FSN-style geometry and as one cinematic "district" treatment from the **same** `NodeStore`/`SelectionState`, supports inspection, navigation and explicit Open, and produces measurements — not a finished app.

### 7.2 Scope

- `FileScape.Core` (.NET class library) + xUnit/FluentAssertions tests: model, scanner (Windows + synthetic), layout, selection, picking math, shell-action interface. Tests run on Windows `dotnet.exe` for Windows-specific parts.
- Engine app project: two visualizations, camera (orbit/pan/zoom/fly, approach-on-select, back, reset), root picker (folder dialog + command-line path), progress bar + Cancel, inspector panel (full path, type, logical size, timestamps created/modified/accessed, attributes, status flags, visual-mapping note), Open button (+ "Show in Explorer"), data-source badge.
- Fixture generator CLI (§7.4).
- Measurement overlay + CSV log (§7.5).

### 7.3 Exclusions

Mutations, context menus, watchers/refresh, caching, full-drive scans, search, overview, split views, bookmarks, theme switching, sound, weather, asset marketplace, plugin system, historical-fidelity details (warp, row travel, perspective narrowing), installer/signing.

### 7.4 Datasets

- **Synthetic fixture (deterministic, seed-driven, generated on Windows into a user-chosen empty folder e.g. `C:\FileScapeFixture`):** ~10,000 entries: one wide dir (2,000 files), one deep chain (40 levels), mixed fanout elsewhere; 200 zero-byte files; Unicode names (CJK, combining marks, emoji, RTL, trailing-dot avoided); a path >300 chars; size outliers (sparse 10 GiB file via `FSCTL_SET_SPARSE` + SetLength so it costs no disk, plus 1 B…100 MB spread); hidden/system files; a directory junction looping to an ancestor (created with `mklink /J`, no admin), a file symlink if Developer Mode permits (it is enabled: `AllowDevelopmentWithoutDevLicense=1`); a directory with an explicit deny ACE for the current user (inaccessible); a "vanishing" directory deleted by the generator mid-scan in a test mode. Generator emits a manifest (counts, expected flags) used by tests. Shareable: contains no personal data.
- **In-memory synthetic source:** same shape without touching disk, to separate scan cost from render cost and to scale to 100k entries.
- **Real directory:** user-selected by DJ, bounded (proposal: 20k–100k entries, e.g. a source tree or SDK folder). Reports record only aggregate counts and timings, **never listings**.
- Dataset counts reported per run: dirs, files, total logical bytes, flagged entries by category, max depth, max fanout.

### 7.5 Hardware assumptions, quality settings and hypotheses

- Hardware: this laptop (Ryzen AI 9 HX PRO 370, Radeon 890M, 8 GiB UMA carve-out, ~88 GiB RAM), on AC, primary panel 1920×1200@60 (125 % scale). Results are not generalised to other GPUs.
- Settings: window 1920×1080 (or full panel 1920×1200), two presets: **Classic** (no GI, simple fog, MSAA/FXAA), **Cinematic** (volumetric fog, engine GI option, SSR, bloom, tone mapping). VSync off for measurement, on for normal use.
- **Hypotheses (targets, not results):** H1 classic, 50k nodes: 95th-percentile frame ≤ 16.7 ms. H2 cinematic, 10k nodes: p95 ≤ 33 ms. H3 during scan: p99 frame ≤ 50 ms, no frame > 100 ms caused by merge work. H4 cancel: scan stops and UI reports cancelled ≤ 250 ms after click. H5 memory: ≤ 500 bytes/node managed for `NodeStore` (excl. engine). H6 picking: selection resolves the intended node in ≥ 95 % of 40 scripted clicks on fixture targets incl. small/zero-byte ones. H7 labels: selected + hovered labels always legible at 1080p; label pass ≤ 2 ms.

### 7.6 Decision-gate spike (first, ~1–2 days)

Question: on this 890M, does Godot Forward+ give a convincing cinematic look *and* hold H1/H2 with 50k instanced boxes + bounded labels + ray picking? Prerequisites: DJ approves installing Godot .NET (≈ one zip, no installer) and, conditionally, Unreal + VS C++ workload (tens of GB). Spike uses only the in-memory synthetic source; no FileScape repo code beyond a throwaway folder (e.g. `spikes/godot-render/`, deleted or kept per manager). Stopping criterion: screenshots of classic + district at both presets, a frame-time CSV for a scripted 60 s camera path, and a yes/no on H1/H2 — or two days elapsed. If **no** on visuals: repeat the same spike in Unreal before choosing. If **no** on performance only: try MultiMesh chunking/LOD before switching.

### 7.7 Observable acceptance criteria (Experiment 02 proper)

1. Launch on Windows, choose fixture folder → progress counts update ≥ 5×/s, camera stays navigable, Cancel stops ≤ 250 ms (logged).
2. Fixture manifest counts equal the app's reported counts, per flag category (automated test + on-screen summary).
3. Every fixture special case is visible with its marker; selecting it shows correct full path (incl. Unicode/long path) and exact byte size (tests compare to manifest).
4. Click selects + approaches; Backspace = back; Esc/parent button = parent; Home = reset; works after visualization switch with selection preserved (scripted test + video).
5. Open on a `.txt` launches the user's associated app (manual check, recorded on video); Open on an executable asks for confirmation; Open on inaccessible/vanished shows an error, app keeps running.
6. Switching Classic ↔ District keeps the same node selected and inspector unchanged.
7. Badge shows **LIVE: C:\…** vs **SYNTHETIC (in-memory)**, and Open is disabled with "mock" label for in-memory data.
8. Measurement CSV + screenshots + short screen recordings for both visualizations at both presets, on fixture and real directory, with the hypotheses marked pass/fail/not-measured.

### 7.8 Validation steps

- `dotnet test` (Windows) for Core; fixture-generator manifest round-trip test.
- Scripted camera path + scripted click list (deterministic seeds) for repeatable frame-time and picking runs; logs written next to the build, aggregated into a report.
- Manual checklist with video for shell actions (functional OS action vs mock is verified by observing the external app window, not the in-app message).
- Memory: managed heap via `GC.GetGCMemoryInfo` and process private bytes from the overlay; engine memory reported separately.

### 7.9 Decision gates

- **G1 (after spike):** engine selection — Godot proceeds if visuals accepted by DJ and H1/H2 within ~1.5×; else Unreal spike.
- **G2 (after Experiment 02):** whether classic FSN fidelity (warp, overview) or cinematic depth is the next investment — DJ product decision from the videos.
- **G3:** FileScape licence before any third-party code/assets are copied in.

---

## 8. Risks and open questions

- Godot cinematic ceiling vs Unreal on an iGPU [U until spike].
- Engine editor behaviour on `\\wsl.localhost` paths [U].
- Godot .NET + `CsWin32`/P/Invoke for shell APIs inside an engine process (COM apartment on main thread) [I: feasible; verify].
- Scale of real directories DJ wants (10k vs 1M nodes) drives LOD/summarisation design; not yet agreed.
- Asset authoring for the district: kitbashed primitives vs licensed packs; licensing of any packs.

---

## 9. Evidence ledger

### 9.1 Commands and observed results (2026-11-03, this session)

| # | Command (abridged) | Observed | Kind |
|---|---|---|---|
| 1 | `git status; git branch -a` in repo | `main`, no commits, 2 untracked docs | read |
| 2 | `powershell.exe -NoProfile` CIM queries: `Win32_OperatingSystem`, `Win32_Processor`, `Win32_VideoController`, `Win32_ComputerSystem`, `Win32_Battery`; registry `LongPathsEnabled`, `AppModelUnlock`, GPU class key `qwMemorySize`; `[Windows.Forms.Screen]::AllScreens` | Values in §2.1 | read |
| 3 | `dotnet --list-sdks/--list-runtimes` (Windows); `dotnet --version` (WSL) | 10.0.403 / 8.0.33+10.0.14 runtimes; WSL 10.0.400 | read |
| 4 | `Get-Command` for git, cmake, cl, clang, msbuild, godot, UnrealEditor, winget, python, node…; `Test-Path` for VS, Windows Kits, Epic, Godot dirs; vswhere | See §2.1 (no VS/MSVC/SDK/CMake/engines; Rider present) | read |
| 5 | `wsl.exe --version`; `uname -a`; `/etc/os-release`; `/etc/wsl.conf` | WSL 2.7.12.0, Ubuntu 24.04.4, systemd=true | read |
| 6 | Windows `dotnet build '\\wsl.localhost\…\scratchpad\wsprobe' -c Release`, then run the exe from UNC | Build succeeded (3.64 s); output in §2.2 | **built, launched** (probe only) |
| 7 | `git clone` of the three repos into scratchpad; `git log`, `wc -l`, `grep`, `sed` over sources | §3 | read |
| 8 | `curl api.github.com/repos/{repo}` and `/releases` | Stars/forks/dates/licence keys in §3 | read |
| 9 | `curl -L` fsvng v0.10.0 Windows zip into isolated scratch dir; `sha256sum`; `python3 -I zipfile` listing | SHA-256 `cca8f56eac235d9557d76fd9234fe79bffeffff47f26716c8af0808cd8b291fe`; contents in §3.1; **not executed** | read |
| 10 | `file`/`strings` on `fsnav/data/courbd.ttf` | Monotype Courier New Bold, "All Rights Reserved" | read |

Nothing was **tested** (no third-party test suites run), **visually observed** or **measured** beyond the probe's build time. No engines, SDKs, packages or assets were installed. The probe and clones live only in the session scratchpad (`/tmp/claude-1000/-home-david-src-filescape/5ed7e784-efd2-4a33-804f-1639c7ff7b34/scratchpad/`), outside the repo, and contain no private filesystem listings. Its own temp directory under Windows `%TEMP%` was deleted by the probe.

### 9.2 Inspected source revisions and locations

- `sp00nznet/fsvng` @ `ec49689bb4a5d6277f2d3149068fd618f5bb86bb`: `README.md`, `NOTICE`, `LICENSE`, `ROADMAP.md`, `docs/architecture.md`, `cmake/FetchDependencies.cmake`, `.github/workflows/ci.yml`, `src/core/FsScanner.cpp`, `src/core/FsNode.{h,cpp}`, `src/ui/Dialogs.cpp:225-400`, `src/geometry/MapVLayout.cpp:440-500`, `src/renderer/NodePicker.cpp`, `tests/test_FsScanner.cpp`.
- `jtsiomb/fsnav` @ `0591f26b50987041d6c03deb3a671c997e943bea`: `README.md`, `COPYING`, `Makefile`, `src/fstree.cc:45-95,401-540`, `src/fsnav.cc`, `data/`.
- `mcuelenaere/fsv` @ `4ef9040fcf14cddd52d79dad87a80355fc885602`: `README.md`, `src/geometry.c` header, `src/scanfs.c:71`, `src/ogl.c:223-239`.

### 9.3 Primary-source URLs (accessed 2026-11-03)

- https://github.com/sp00nznet/fsvng · https://github.com/sp00nznet/fsvng/releases/tag/v0.10.0 · https://github.com/jtsiomb/fsnav · https://github.com/mcuelenaere/fsv
- Unreal (docs 5.8): https://dev.epicgames.com/documentation/en-us/unreal-engine/lumen-technical-details-in-unreal-engine · https://dev.epicgames.com/documentation/en-us/unreal-engine/hardware-and-software-specifications-for-unreal-engine · https://dev.epicgames.com/documentation/en-us/unreal-engine/nanite-virtualized-geometry-in-unreal-engine · https://dev.epicgames.com/documentation/en-us/unreal-engine/instanced-static-mesh-component-in-unreal-engine · https://dev.epicgames.com/documentation/en-us/unreal-engine/volumetric-fog-in-unreal-engine · https://dev.epicgames.com/documentation/en-us/unreal-engine/setting-up-visual-studio-development-environment-for-cplusplus-projects-in-unreal-engine · https://www.unrealengine.com/en-US/license (403; snippet only) · https://www.unrealengine.com/en-US/blog/we-are-updating-unreal-engine-twinmotion-and-realitycapture-pricing-in-late-april
- Godot (stable 4.7): https://godotengine.org/download/windows/ · https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html · https://docs.godotengine.org/en/stable/classes/class_multimesh.html · https://docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html · https://docs.godotengine.org/en/stable/tutorials/physics/ray-casting.html · https://docs.godotengine.org/en/stable/classes/class_label3d.html · https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html · https://docs.godotengine.org/en/stable/tutorials/3d/global_illumination/introduction_to_global_illumination.html · https://docs.godotengine.org/en/stable/tutorials/3d/volumetric_fog.html · https://docs.godotengine.org/en/stable/tutorials/performance/thread_safe_apis.html · https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_windows.html
- Stride: https://github.com/stride3d/stride · https://github.com/stride3d/stride/releases · https://doc.stride3d.net/4.0/en/ReleaseNotes/
- .NET viewports: https://github.com/helix-toolkit/helix-toolkit · https://github.com/dotnet/Silk.NET
- .NET/Windows: https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/latency · https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-shellexecuteinfow · https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shellexecuteexw · https://learn.microsoft.com/en-us/windows/win32/api/shlobj/nf-shlobj-shmultifileproperties · https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation · https://learn.microsoft.com/en-us/dotnet/standard/io/file-path-formats (snippet) · https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findfirstfileexw · https://learn.microsoft.com/en-us/windows/win32/fileio/file-attribute-constants · https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getcompressedfilesizew · https://learn.microsoft.com/en-us/windows/win32/api/fileapi/ns-fileapi-by_handle_file_information · https://learn.microsoft.com/en-us/dotnet/api/system.io.enumerationoptions.attributestoskip · https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/IO/EnumerationOptions.cs · https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/IO/Enumeration/FileSystemEnumerator.Windows.cs

### 9.4 Unverified claims and blockers

- The Unreal licence page could not be fetched. Royalty terms come from a search snippet plus Epic's blog. **DJ should read the page directly.**
- Unreal per-instance hit index, Text Render/UMG label limits, packaging steps, and editor size are [U].
- Godot .NET export layout and editor download size are [U]. MIT licence restated from long-standing knowledge, not from today's fetched page.
- Stride editor platform and per-instance colour are [U].
- fsvng: the Unicode drop and `ShellExecuteA` failure on non-ANSI paths are inferred from MSVC semantics, not runtime-tested. README claims about runtime behaviour were not observed (binary not run).
- The 890M's D3D12/Vulkan feature levels and all rendering performance are unmeasured.
- Engine editor behaviour on `\\wsl.localhost` paths is [U].
- The cloud-placeholder "no hydration from metadata enumeration" claim is [I]. Microsoft documents that enumerating a `RECALL_ON_DATA_ACCESS` *directory* can fetch from the remote store, so the fixture/real-dir test should include a OneDrive folder only with DJ's consent.
- Blocker for the next step: installing Godot .NET (and conditionally Unreal + VS 2026 C++ workload + Windows SDK) requires DJ/manager approval.
