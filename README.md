# FileScape

Explore a real Windows filesystem as a place. A Godot 4.7 .NET app over a shared, engine-independent C# core, with three experiences over the same model:

- **Places** (default): you are *inside* a folder, **on foot**. Its files stand around you as glass towers in type quarters, every child folder is a named gate you walk through, and beyond each gate a vista previews the next place. Places are separate spaces linked by gates (docs/decisions.md AD-14); since 2026-11-05 you walk them (AD-16), flight is a mode you switch on, and the world is connected: links lead where they point, and drives and WSL distributions are reachable from a hub (AD-17, AD-18).
- **Classic FSN** and **District**: the whole-tree reference maps from the first prototype (FSN-inspired landscape, futuristic city). They mark where you are in Places.

**Status (2026-11-05):** see `docs/implementation-status.md` for what is demonstrated, provisional and unknown. Movement feel and the looks await DJ's hands-on judgement.

## Try it

Prerequisites (already set up on DJ's laptop; details in `docs/windows-workflow.md`): Windows .NET SDK 8+ and the portable Godot 4.7.2 .NET editor under `C:\Users\David\tools\godot\`.

**Standalone build:** `C:\Users\David\AppData\Local\FileScape\build\FileScape.exe` (`%LOCALAPPDATA%\FileScape\build`). Its identity (commit, hashes) is in `build-info.txt` next to it and at the end of Help (F1). `tools/fs.sh export` builds, verifies and smoke-launches a new build in a fresh `staging-*` folder, then installs it, keeping the previous one as `previous-*`; if FileScape is running it installs nothing and names the verified staged exe instead. `tools/fs.sh builds` shows what is where.

**Two modes** (AD-22): **Connected world** (default: start anywhere, everything listed when you visit it, links followed, drives and WSL in Locations) and **Bounded tree scan** (the original whole-tree reference: one folder scanned completely, links not followed, the Classic FSN and District maps show its whole tree). In Locations (L) use **Go** for the first, **Scan as tree** / **Scan this folder as a tree** for the second; from the command line `--scan=tree --root=…`.

It starts in **Places** on synthetic sample data (amber badge). Click **Open folder…**, or press **Ctrl+L** and paste a path, to explore your own folders (green "LIVE" badge). Nothing is scanned wholesale: a folder is listed when you go in (and the folder you chose is listed in the background). Rebuild with `tools/fs.sh build && tools/fs.sh export`. It renders with D3D12 by default (KD-01).

Test trees (deterministic, in `%TEMP%\FileScape-fixtures`, safe to delete with `… delete <dir>`):
- `dotnet src\FileScape.Fixtures\bin.win\Debug\net8.0\FileScape.Fixtures.dll create-walk` → **Wayfarer**: a software project, junctions (outside the root, a loop, two routes, a broken one), a denied folder, hidden/system folders, a Unicode name.
- `… create-places` → **Atlas Studio**: wide, deep, empty, many-files cases.
- `tools/make-wsl-fixture.sh` (in WSL) prints a `\\wsl.localhost\…` path with case pairs, Linux symlinks, a FIFO and a small project.

**From the repository (WSL):**

```
tools/fs.sh build
tools/fs.sh run                                        # synthetic sample data, in Places
tools/fs.sh run -- --root='C:\Program Files\dotnet'     # a live folder (the arrival point)
tools/fs.sh run -- --vis=district                      # start in a map: places | classic | district
tools/fs.sh test                                        # tests with Windows dotnet.exe (exit code = result)
```

### A short journey to try (open Wayfarer)

1. **Arrive on foot.** You stand on a terrace at the folder's entrance; its name is on the landmark ahead and in "You are in". **↑** walks you down the stairs; **← / →** turn; hold the **right mouse button** to look around.
2. **Climb the overlook** (to the left of the landmark, west): walk up its ramp, or hop the two low blocks with **Space**. The panel up there sums up the place.
3. **Enter a child.** Click **Photos** in Destinations (you walk up to its gate; its name is on a plate across the opening and in the chip at the bottom), then keep walking through the gate. **U** brings you back, facing the ▲ gate.
4. **Keep going up.** Go into Journeys › Coast › Harbour › Pier (select, **G**), press **U** once, then just hold **W**: each ▲ gate leads to the next, up to Wayfarer.
5. **Follow a link.** Walk through **to-shared-library** (a teal junction gate). You arrive in *Shared Library*, outside Wayfarer: the breadcrumb shows where it really is, a line says which link you came through. **Backspace** returns through the link; **U** would go to its real parent instead.
6. **Try the odd ones.** *back-to-wayfarer* says it loops back here; *photos-shortcut* and *also-photos* reach the same Photos place; *broken-link* is hatched and explains itself; "⚠ … need attention" lists the problems. **H** shows the two hidden/system folders.
7. **A project.** Projects › lighthouse is recognised as a software project: its README and manifests stand on lit plinths by the avenue; the inspector says why.
8. **Elsewhere on the computer.** **L** opens Locations (drives, WSL distributions, the hub); **Ctrl+L** takes a pasted path such as `\\wsl.localhost\Ubuntu-24.04\home\you` or `/home/you`. Linux names keep their case (*Notes* and *notes* are two gates).
9. **Fly and land.** **T** takes off (E/Space up, Q down, wheel speed); **T** again lands you safely on foot. **Home** is this folder's arrival point, **Ctrl+Home** the folder you started in; **M** the map.

### Controls (also in Help, F1)

| To… | Do this (Places) |
|---|---|
| Walk | **↑ / ↓** forward / back · **← / →** turn (*changed: arrows used to step sideways*) · **W A S D** walk and step sideways · hold **Shift** to run (walk 8 m/s, run 16 m/s; adjustable in Settings) |
| Look and steer | **hold the right mouse button** and move the mouse (left/right turns your heading, up/down your gaze) · or **left-drag**. Direction: **⚙ Settings / F4** reverses left/right and up/down separately (default: both reversed — moving the mouse right turns you left, down looks up). Letting go eases the view back to level (heading kept; Settings can turn it off) |
| Jump | **Space** (never opens or runs anything) |
| Fly | **T** or the ✈ button: rise and fly — W A S D along the view, **E / Space** up, **Q** down, **wheel** speed, **Shift** ×3 · **T** again lands on foot (safely; if there is nowhere to stand below, at the arrival point, with a note) · **B** rises above the place |
| Go into a folder or link | walk **through its gate** facing it · **double-click** it or its Destinations row · select it and press **G** |
| Go back | **Backspace** where you came from (also back through a link) · **U / PgUp** physical parent (also above the folder you started in) · the ▲ gate · breadcrumb · **Ctrl+Home** arrival root. Going up arrives facing the next ▲ gate: keep walking forward to keep going up |
| Elsewhere | **L** Locations · **Ctrl+L** go to a typed or pasted Windows / WSL / Linux path · **Open folder…** |
| Show | **H** hidden and system items (off by default; dot-files always shown) |
| Inspect | **click** to select (never moves you) · **F** turns to look at it · **Esc** closes popups, then back to the folder itself |
| Maps | **M** Places ↔ map · **Tab** cycles Places → Classic FSN → District · **G** on a folder in a map enters it in Places. In the maps the arrows still move sideways and the right mouse flies while held |
| Open | **Enter** opens with Windows (asks first for programs, links, cloud files); MOCK on sample data |
| Other | **F1** help · **F2** quality · **F3** stats · **F4** settings (saved to `%APPDATA%\FileScape\preferences.json`) |

Walking backwards through a gate never travels; flying above a gate's lintel leaves the place without travelling.

**Summaries.** "Here" counts what is directly in a folder (folders, files, links); "in all" is the whole subtree when every folder below is listed, otherwise "≥ … so far" with how many folders are not fully listed. Counts include hidden items; links never add what they lead to. Gates and Destinations are in name order with numbers by value (*Leg 2* before *Leg 10*), west → east as you arrive.

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
