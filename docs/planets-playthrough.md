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

Keys: W/A/S/D or arrows run (Shift: faster) · Space jump / twirl / launch · E look closer · Enter open · Backspace
back · U up · G fly to the selected world · J the planet's list · L locations · Ctrl+L go to path · V Places/Planets ·
right-drag turn the camera · mouse wheel camera distance · Home back to the arrival meadow · N sound on/off · F1 help.

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
