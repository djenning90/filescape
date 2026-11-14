# The planetary FileScape: a short guided first playthrough

Launch: `C:\Users\David\AppData\Local\FileScape\staging-20261111-065057-821915\FileScape.exe -- --root=sample --vis=planets`
(the verified build `a98fdb4696c6`; once promoted with `tools/fs.sh promote staging-20261111-065057-821915` it is
`C:\Users\David\AppData\Local\FileScape\build\FileScape.exe`)
(or start FileScape as usual and click **✦ Planets (V)** in the toolbar: from the sample data it opens the Starter Sky).

The Starter Sky is a small folder of real, original files that FileScape writes into its own data folder
(`%APPDATA%\Godot\app_userdata\FileScape\samples\Starter Sky`). Nothing outside it is written.

1. **Arrive.** You land on the Starter Sky, a library world: a tower of books ahead, worlds hanging in the sky. The
   title card says what the folder holds. Run with **W/A/S/D** (or the arrows); the camera follows by itself.
2. **Jump, then twirl.** **Space** jumps; **Space again in the air** twirls you higher (once per jump). The round
   platform by the landmark is the overlook: walk up its stepping stones or twirl onto it. From the top every world is
   named; **E** at its telescope lists everything here.
3. **Choose a world you can see.** Walk towards the green world (**Garden Photos**): under it is a flower-shaped
   launcher. Stand on it; the prompt says **Space · fly to Garden Photos**. Press Space: the petals close, you crouch,
   the camera turns to the world, and you fly. You shrink into the little world on the way and land on it.
4. **Look closer at a file.** The easels show the real pictures. Walk up to one; the prompt says **E · look closer**.
   **E** shows the picture and its details (and selects it: the card on the right has Open, Show in Explorer, Copy
   path). **Enter** opens it with Windows.
5. **Compare with Places.** Press **V**: the flat Places world, in the same folder with the same picture selected.
   **V** again: back on the planet, beside the picture.
6. **Go back.** **Backspace** flies you back up; you land beside the launcher of the world you left.
7. **Take the direct route.** **J** lists every world and file on the planet you are on (type to filter; double-click
   or Enter goes there). **Ctrl+L** goes to any path: a world that is not in the sky is reached by a warp.
8. **Try the others.** Star Archive (1,500 files: 64 stand as objects, the rest in piles; J lists them all), Music
   Box (bell flowers ring as you pass; E plays a song), Lantern Workshop (a code project; E on README.md reads it),
   Constellations (36 small worlds: ten launchers and a ring of moons), Quiet Moon (empty).
9. **Your own files.** **Ctrl+O** (Open…) chooses This computer, a drive, a WSL distribution or a folder; the planets
   work the same on them. In your own folders FileScape reads a file's contents only when you ask (E), so an easel
   shows its picture once you have looked at it; cloud files that are not on this PC are never downloaded for a preview. **Esc** closes what is open; **F1** is help.

Keys (2026-11-14): W/A/S/D or ↑ ↓ run (Shift: faster) · **drag with the left or right mouse button, or ← →, to look
around** (also while running) · click selects (on release), double-click goes there · Space jump / twirl / launch ·
E look closer · Enter open · Backspace back · U up · G fly to the selected world · T overview · J the planet's list ·
L locations · Ctrl+L go to path · V Places/Planets · mouse wheel camera distance · Home back to the arrival meadow ·
N sound on/off · F1 help.

## Second iteration (2026-11-11): three flows to try

**1. Find a named launcher (try the Ubuntu root).** Ctrl+L, type `\\wsl.localhost\Ubuntu-24.04\` (or open it from
Locations). You land in the middle of a ring of flower launchers: names go in order like a clock, the way back is
behind you. Right-drag to turn the camera until you see **⌂ home**; walk straight to it (about 9 m). On the pad the
prompt says *Space · fly to home*. (Constellations in the Starter Sky is the crowded case: 28 pads on three rings and
a ring of moons for the rest.)

**2. Watch the names while you turn.** Hold the right mouse button and sweep the camera all the way round. Each name
stays pinned to its pad or world by a short stem; pads behind the curve of the planet lose their names; names never
overlap, and the one you select (click) or point at wins.

**3. The overview.** Press **T** (or ✧ Overview in the toolbar). You pull back out of a soft flash to every folder
FileScape knows, as star systems in 3D: solid worlds are listed, pale ones not yet, ringed ones you have visited, the
gold beacon is you. Right-drag orbits, middle-drag or WASD pans, the wheel zooms. Click a world (say Star Archive's
**2025**): it is selected and the view zooms in on it. **Enter** lands you there. **Backspace** takes you back. Press
**T** again and then **Esc**: you glide back down to exactly where you stood.

**Music.** Listen for the tune that opens a world (on Hearth it starts A–C–F, rising) and comes back every 16 bars,
each time a little different. Samples to compare: `%LOCALAPPDATA%\FileScape\evidence\2026-11-11-planets-2\music\`
(`before\` and `after\`, and `after\hearth-theme-returns-89s.wav` for three returns of the theme; README.txt says what
to listen for).

## Third iteration (2026-11-14): controls, worlds with their own music and look, the workbench

**Launch** (Command Prompt): see the build in `docs/implementation-status.md` (§ Planets, third iteration); the
commands below assume `%FS%` is that `FileScape.exe`.

### On this PC (about 4 minutes): This computer → Ubuntu → a real repository → a child folder → back

`"%FS%" -- --root=hub --vis=planets`

1. **This computer.** You arrive at the hub: a calm world, a warm dawn low in the sky, the drives and distributions
   hanging above their launchers. Hold the **left** mouse button and drag to look round (right-drag still works);
   then stand still and hold **←** or **→**: the camera turns, you don't move. A plain click on a world selects it;
   a drag that starts on a world only turns the camera.
2. **A filesystem root.** Fly to **Ubuntu-24.04** (stand on its launcher and press Space, or click its world and press G).
   On landing, an amber aurora and a dark terminal monolith with a blinking `>_` stand beside you: a Linux
   distribution. The music changes from the hub's open, unhurried tune to a quicker gateway groove. (C:\ is a Windows
   volume: a four-pane glass gateway under a blue aurora; try it if you like — the checks never enter C:\.)
3. **A real repository.** Press **Ctrl+L** and go to `\\wsl.localhost\Ubuntu-24.04\home\david\src\filescape` (this
   project). It is a repository: the sky has a commit graph among the stars, the overlook is a low **workbench**, and
   the music has its own developer groove from the first bar.
4. **The workbench.** Walk to it and **jump on** (one plain jump). The project's README and key places rise out of the
   bench as a ring of glowing cards, each named with what it is for, and the bench's panel lists them. **Spin**
   (jump from the bench, then Space again in the air): the README opens in the preview. **E** at the bench does the
   same without the jump; clicking the bench reveals; clicking a card selects it, double-clicking goes there.
   Nothing is opened with Windows unless you press Enter (or the Open button) yourself.
5. **A child folder.** Fly into **docs** (or **src**): the world keeps a quiet echo of the project — a teal bead on its
   waymarker, a faint commit graph — and the music keeps a hint of the repository's motif over the depth band's groove.
6. **Back again.** **Backspace** to the repository (the music picks up where it was and develops on a return),
   **Backspace** again, then **Ctrl+Home** to This computer.

### On any PC: the Starter Sky (about 3 minutes)

`"%FS%" -- --root=sample --vis=planets`

1. The Starter Sky is your starting point, so it is the hub here (calm, a dawn).
2. **Lantern Workshop** is a small repository: jump onto its workbench, spin to read its README, press E, click a card.
3. **src** inside it: the trails band, with the project's echo.
4. **Long Way Down** goes six levels deep: Meadow Gate (the outskirts, one ring on the waymarker), Fern Trail and Old
   Bridge (the trails, amber), Deep Woods and Hollow Oak (the deep woods, violet), Glowing Spring (the underglow,
   cyan, four rings). Listen for the band motif returning as you go down; **Backspace** a few times quickly and back
   in again to hear how quick hops and returns sound.
5. Throughout: left-drag and ← → to look, **T** for the overview (left-drag orbits it too), Esc back down.
