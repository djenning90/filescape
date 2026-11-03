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
tools/fs.sh test                                    # 57 tests with Windows dotnet.exe
```

It starts on **synthetic sample data**, shown by the amber badge "SYNTHETIC SAMPLE DATA". Use **Open folder…** to pick a real folder. The badge turns green ("LIVE · path"). Drive roots, your profile, OneDrive and network paths ask before scanning.

### What to try

1. Click **Open folder…** and pick a project or tool folder with a few thousand files. Watch the status strip count entries while the landscape grows. Press **Cancel scan** on a big folder: the partial tree stays explorable, and unscanned directories are marked "(not scanned)".
2. **Left-drag** to orbit, **right-drag** to pan, **wheel** to zoom, **WASD/QE** to move. **Click** a block to inspect it. **Double-click** or **F** to fly to it.
3. Read the inspector: the full path, exact bytes, the visual height mapping (log scale and cap), timestamps, attributes, and any flags (hidden, link not followed, access denied).
4. Press **Enter** (or **Open**) on a file. Windows opens it with its associated app. Programs, scripts, links and cloud placeholders ask first. On sample data, Open is clearly a MOCK.
5. Press **Tab** to switch between **Classic FSN** and **District**. The same item stays selected.
6. Get lost on purpose, then press **Home** to return to the root, or **B** for a bird's-eye overview. **Esc** selects the parent; **Backspace** goes back.
7. **F2** toggles quality (High: SSAO, SSR, volumetric haze; Low: none of these). **F3** shows performance stats. **F1** shows help.

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
