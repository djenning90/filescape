# FileScape

Explore a real Windows filesystem as a 3D place. This is the first prototype: a Godot 4.7 .NET app with two visualizations, a classic FSN-inspired landscape and a futuristic District, over a shared, engine-independent C# core.

**Status (2026-11-03):** usable, reviewable prototype; see `docs/implementation-status.md` for what is demonstrated, provisional and unknown, including the Vulkan stability defect (KD-01) that made D3D12 the default.

## Try it

Prerequisites (already set up on DJ's laptop; details in `docs/windows-workflow.md`): Windows .NET SDK 8+ and the portable Godot 4.7.2 .NET editor under `C:\Users\David\tools\godot\`.

**Standalone build** (fastest):

```
%LOCALAPPDATA%\FileScape\build\FileScape.exe
```

Rebuild it from WSL with `tools/fs.sh build && tools/fs.sh export`. It renders with D3D12 by default. Vulkan crashed intermittently on this GPU (KD-01). To compare, run `FileScape.exe --rendering-driver vulkan`.

**From the repository (WSL):**

```
tools/fs.sh build
tools/fs.sh run                                    # synthetic sample data
tools/fs.sh run -- --root='C:\Program Files\dotnet'   # a live folder
tools/fs.sh test                                    # 70 tests with Windows dotnet.exe (exit code = test result)
```

It starts on **synthetic sample data**, shown by the amber badge "SYNTHETIC SAMPLE DATA". Use **Open folder…** to pick a real folder. The badge turns green ("LIVE · path"). Drive roots, your profile, OneDrive and network paths ask before scanning.

### Controls (also in Help, F1)

| To… | Do this |
|---|---|
| Move around | **W A S D** or **arrow keys** · **Q / E** down / up · hold **Shift** for 3× speed |
| Look around and fly | **hold the right mouse button** and move the mouse; W A S D / arrows then move where you look; the **wheel** changes flight speed (shown at the top); release (or **Esc**) to stop |
| Orbit / pan / zoom | **left-drag** orbits around the view centre · **middle-drag** pans · **wheel** zooms |
| Inspect | **click** to select · **double-click** or **F** to fly to it · **Esc** parent · **Backspace** previous |
| Recover | **Home** start view · **B** bird's-eye overview |
| Open | **Enter** opens with Windows (asks first for programs, links, cloud files); MOCK on sample data |
| Switch view | **Tab** Classic ↔ District (the selection stays) · **F2** quality · **F3** stats |

Change from the first prototype: right-drag used to pan. It now flies (look around), and pan is on the middle button.

### What to try

1. Click **Open folder…** and pick a project or tool folder. Watch the landscape grow, and try **Cancel scan** on a big folder.
2. Hold the **right mouse button**, look around, and fly with **W**/**arrows**. Use the **wheel** to go faster or slower. Let go, and **left-drag** to orbit what you were looking at.
3. Click a block and read the inspector. Press **Enter** to open it with Windows.
4. Press **Tab** to switch Classic ↔ District with the same item selected. Fly away, then press **Home**.

## Layout of the repository

```
src/FileScape.Core      model, scanning, layout, picking, selection, shell actions (no engine dependency)
src/FileScape.Fixtures  on-disk Windows edge-case fixture builder
tests/                  xUnit + FluentAssertions
app/                    Godot project (Scripts/, Shaders/)
tools/fs.sh             WSL → Windows build/run/test/export helper
docs/                   requirements, decisions, architecture, workflow, status, evidence
```

Project history and context: `FileScape_Project_Handoff.md`, `INVESTIGATION_01_FINDINGS.md`, `docs/development-methodology.md`.
