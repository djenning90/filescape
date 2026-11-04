# FileScape

Explore a real Windows filesystem as a place. A Godot 4.7 .NET app over a shared, engine-independent C# core, with three experiences over the same model:

- **Places** (default, new on 2026-11-04): you are *inside* a folder. Its files stand around you as glass towers in type quarters, every child folder is a named gate you can walk through, and beyond each gate a vista previews the next place. Places are separate spaces linked by gates (docs/decisions.md AD-14).
- **Classic FSN** and **District**: the whole-tree reference maps from the first prototype (FSN-inspired landscape, futuristic city). They mark where you are in Places.

**Status (2026-11-04):** see `docs/implementation-status.md` for what is demonstrated, provisional and unknown. Camera feel and the Places look await DJ's hands-on judgement.

## Try it

Prerequisites (already set up on DJ's laptop; details in `docs/windows-workflow.md`): Windows .NET SDK 8+ and the portable Godot 4.7.2 .NET editor under `C:\Users\David\tools\godot\`.

**Standalone build** (fastest): `%LOCALAPPDATA%\FileScape\build\FileScape.exe`

It starts in **Places** on synthetic sample data (amber badge "SYNTHETIC SAMPLE DATA"). Click **Open folder…** to explore a real folder (green "LIVE" badge). Drive roots, your profile, OneDrive and network paths ask first. Rebuild with `tools/fs.sh build && tools/fs.sh export`. It renders with D3D12 by default (Vulkan crashed intermittently on this GPU, KD-01).

A deterministic test tree for trying Places is created by `dotnet src\FileScape.Fixtures\bin.win\Debug\net8.0\FileScape.Fixtures.dll create-places` (it prints the path of "Atlas Studio" under `%TEMP%\FileScape-fixtures`); open that folder.

**From the repository (WSL):**

```
tools/fs.sh build
tools/fs.sh run                                        # synthetic sample data, in Places
tools/fs.sh run -- --root='C:\Program Files\dotnet'     # a live folder
tools/fs.sh run -- --vis=district                      # start in a map: places | classic | district
tools/fs.sh test                                        # tests with Windows dotnet.exe (exit code = result)
```

### A short journey to try in Places

1. **Arrive.** You stand on a terrace at the entrance of the folder. The name is on the landmark ahead and in "You are in" at the top left. Towers around the avenue are the files (height = size, colour = type); the gates beyond are the child folders, each with its name. The list on the left (**Destinations**) names every one of them.
2. **Look around.** Hold the **right mouse button** and move the mouse. Turn round: behind you is the **▲ Up** gate (not at the root) and a preview of the parent beyond it.
3. **Go somewhere.** Click a folder in Destinations: you walk up to its gate. Hold **W** (or ↑) and walk through. A short fade marks the threshold, and you arrive in the new place, with its own colour and landmark.
4. **Come back.** Turn round and walk through the ▲ gate, or press **U**. You arrive in front of the gate you came from, facing into the parent, with that folder selected.
5. **Inspect without moving.** Click any tower or gate: the inspector shows true metadata; **Enter** opens it with Windows. Selecting never changes where you are.
6. **Rise and see the map.** **B** rises above the place (it shows this neighbourhood only). **M** opens the whole-filesystem map with "You are here"; **M** again comes back. In a map, select any folder and press **G** to go there.
7. **Get home.** **Home** returns to this folder's arrival point, **Ctrl+Home** to the folder you chose. Try the wide folder (many gates on tiers), a deep chain, an empty folder and a locked one.

### Controls (also in Help, F1)

| To… | Do this |
|---|---|
| Walk | **W A S D** or **arrow keys** · **Q / E** down / up · hold **Shift** for 3× speed |
| Look around and fly | **hold the right mouse button** and move the mouse; W A S D / arrows then move where you look; the **wheel** changes flight speed; release (or **Esc**) to stop |
| Go into a folder (Places) | walk **through its gate** facing it · **double-click** the gate or its Destinations row · select it and press **G** |
| Go back (Places) | walk through the **▲ Up** gate · **U** / **PgUp** · **Backspace** previous place · breadcrumb segments · **Ctrl+Home** root |
| Orbit / pan / zoom | **left-drag** orbits · **middle-drag** pans · **wheel** zooms |
| Inspect | **click** to select · **F** frames the selection · **Esc** in Places: back to the folder itself; in maps: parent |
| Recover | **Home** arrival point (maps: start view) · **B** ascend (maps: overview) |
| Maps | **M** Places ↔ map · **Tab** cycles Places → Classic FSN → District · **G** on a folder in a map enters it in Places |
| Open | **Enter** opens with Windows (asks first for programs, links, cloud files); MOCK on sample data |
| Other | **F1** help · **F2** quality · **F3** stats |

Walking backwards through a gate never travels, and flying above a gate's lintel escapes the place without travelling.

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
