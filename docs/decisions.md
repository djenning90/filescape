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
