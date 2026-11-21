# Legacy views: Places and the maps

FileScape began with these experiences. They still work and live in the toolbar's **Legacy views** menu (or V and Tab). This page keeps their guide (moved from the README on 2026-11-15, when Planets became the main view, AD-51). Going from the planets to a legacy view and back (V, Tab, M or the menu) puts you back where you stood on the planet, facing and looking as before, unless you moved elsewhere or chose something else meanwhile (2026-11-20, PLANET-112, AD-82).

**Two modes** (AD-22): **Connected world** (default: start anywhere, everything listed when you visit it, links followed, drives and WSL in Locations) and **Bounded tree scan** (the original whole-tree reference: one folder scanned completely, links not followed, the Classic FSN and District maps show its whole tree). In Locations (L) use **Go** for the first, **Scan as tree** / **Scan this folder as a tree** for the second; from the command line `--scan=tree --root=…`.

**Launching (2026-11-06):**

| Command | What it does |
|---|---|
| `FileScape.exe -- --root=hub` | Starts at **This computer** (This PC): every drive and WSL distribution as a gate, nothing listed until you go in. |
| `FileScape.console.exe …` | From a terminal: the same, but it waits for the window and returns to the prompt with the exit code. `FileScape.exe` returns at once, and its log lands after the prompt. |
| `FileScape.exe -- --layout=spacious` | The pre-compact layout, for comparison. |

It starts in **Places** on synthetic sample data (amber badge). Click **Open…** (Ctrl+O) and choose **⌂ This computer**, a drive, a path or Browse…, or press **Ctrl+L** and paste a path, to explore your own folders (green "LIVE" badge). Projects are marked ⚙: **R** reads the README, and the card offers your editors. Nothing is scanned wholesale: a folder is listed when you go in (and the folder you chose is listed in the background). Rebuild with `tools/fs.sh build && tools/fs.sh export`. It renders with D3D12 by default (KD-01).

Test trees (deterministic, in `%TEMP%\FileScape-fixtures`, safe to delete with `… delete <dir>`):
- `dotnet src\FileScape.Fixtures\bin.win\Debug\net10.0\FileScape.Fixtures.dll create-walk` → **Wayfarer**: a software project, junctions (outside the root, a loop, two routes, a broken one), a denied folder, hidden/system folders, a Unicode name.
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
9. **Fly and explore.** **T** rises 16 m and looks down across the place; **B** shows this place, its parent and its child places as islands. Fly level with **W A S D** (looking down never dives), climb with **E / Space**, descend with **Q / C**. Click an island and press **G** to fly there, or fly over one and press **T** to land in it. **Backspace** goes back. **Home** is this folder's arrival point, **Ctrl+Home** the folder you started in; **M** the map.
10. **Panels on demand.** The view is the world by default: **J** opens Destinations (type to filter, ↑↓ Enter), **K** the colour key, **F4** settings; clicking something opens a small card (Details ▸ for everything).

### Controls (also in Help, F1)

| To… | Do this (Places) |
|---|---|
| Walk | **↑ / ↓** forward / back · **← / →** turn (*changed: arrows used to step sideways*) · **W A S D** walk and step sideways · hold **Shift** to run (walk 5.5 m/s, run 11 m/s in the compact places; adjustable in Settings) · **P** pin · **R** README |
| Look and steer | **hold the right mouse button** and move the mouse (left/right turns your heading, up/down your gaze) · or **left-drag**. Direction: **⚙ Settings / F4** reverses left/right and up/down separately (default: both reversed — moving the mouse right turns you left, down looks up). On foot, letting go eases the view back to level (heading kept; Settings can turn it off); in flight the view stays where you leave it |
| Jump | **Space**: hold for a high jump (≈ 1.2 m), tap for a hop (never opens or runs anything) |
| Fly | **T** or the ✈ button: rise 16 m and look down · **W A S D** fly level along your heading (whatever the gaze) · **E / Space** up · **Q / C** down · **wheel** speed (and faster the higher you are) · **Shift** ×3 · **B** aerial view of the neighbourhood · **T** again lands on foot — over another place you land in it; far from everything, in the nearest place. Settings: "W flies where you look" restores the old flight |
| The aerial world | above 14 m every known folder is an island: roads to child folders, cyan arcs for links, a warm ring where you have been, a gold beacon where you are. **Click** an island to inspect it, **G** to fly there, **T** over it to land. Places near you inside the folder you opened are listed as you fly; others stay "not listed yet" until you go there |
| Panels | **J** Destinations drawer · **K** key (colours, heights) · **F4** settings (look direction, levelling, flight mode, speeds, quality) · **Esc** closes the card or drawer |
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
