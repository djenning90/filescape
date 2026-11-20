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
   **E** shows the picture and its details (and selects it: the card on the right has Open, Show in folder, Copy
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

**Launch** (Command Prompt), the installed build `1533fdf17515`:
`"%LOCALAPPDATA%\FileScape\build\FileScape.exe" -- --root=hub --vis=planets` (this PC) or `… -- --root=sample --vis=planets`
(any PC). Below, `%FS%` stands for `%LOCALAPPDATA%\FileScape\build\FileScape.exe`.

### On this PC (about 4 minutes): This computer → Ubuntu → a real repository → a child folder → back

`"%FS%" -- --root=hub --vis=planets`

1. **This computer.** You arrive at the hub: a calm world, a warm dawn low in the sky, the drives and distributions
   hanging above their launchers. The music is unhurried here, no drums: a question that only resolves when you leave.
   Hold the **left** mouse button and drag to look round (right-drag still works);
   then stand still and hold **←** or **→**: the camera turns, you don't move. A plain click on a world selects it;
   a drag that starts on a world only turns the camera.
2. **A filesystem root.** Fly to **Ubuntu-24.04** (stand on its launcher and press Space, or click its world and press G).
   On landing, an amber aurora and a dark terminal monolith with a blinking `>_` stand beside you: a Linux
   distribution. The music changes from the hub's open, unhurried tune to a quicker gateway groove. (C:\ is a Windows
   volume: a four-pane glass gateway under a blue aurora; try it if you like — the checks never enter C:\.)
3. **A real repository.** Press **Ctrl+L** and go to `\\wsl.localhost\Ubuntu-24.04\home\david\src\filescape` (this
   project). It is a repository: the sky has a commit graph among the stars, the overlook is a low **workbench**, and
   the music has its own developer groove from the first bar.
4. **The workbench.** Walk to it and **jump on** (any jump, even a tap). The project's README and key places rise out of the
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

## First public playtest (2026-11-15): a newcomer's route

**Launch** (Command Prompt): `"%LOCALAPPDATA%\FileScape\build\FileScape.exe"` — no arguments: Planets at This computer.

1. **Welcome.** The first time, a small dialog offers *Learn to play*, *Explore on my own* and *Don't show this again*.
   Choose **Learn to play** (Help → Learn to play brings it back any time).
2. **The walkthrough** (Starter Sky, about four minutes). A small card at the top says what to do; a column of warm light
   marks where; a chime and a burst of light say you did it. Look round with a left-drag, walk to the tower, jump, fly
   from the Lantern Workshop launcher, jump onto its workbench and press E for the README (or spin), U up, J to Music Box,
   U and then Backspace (up vs back), T for the overview, drag to orbit, Esc back to where you stood. *Skip step*,
   *Start over* and *Leave* are always there; if you wander off, *Back to the lesson* takes you back.
3. **The black-hole world.** In the Starter Sky, fly to **Event Horizon** (J, type "event", Enter). A caution banner on
   arrival; the launchers and paths are on solid rock. Walk out past the rings: the ground breaks off over a spinning
   black hole. Step off — you spiral down round it and come back up beside its launcher in the Starter Sky; press Space
   on the launcher and you are straight back in. Nothing happens to any file.
4. **The music.** Listen as you go deeper: the Long Way Down passes the trails (festival: a build while you fly, the drop
   as you land), the deep woods (orchestral) and the underglow (drum and bass). The hub stays gentle; Event Horizon has its
   own uneasy theme, a warning as you near an edge and a falling whoosh. Samples to compare:
   `%LOCALAPPDATA%\FileScape\evidence\2026-11-15-playtest\music\` (README.txt says what to listen for).
5. **Legacy views.** The toolbar's *Legacy views* menu opens Places and the two maps; V returns to Planets.

## Repository arrivals, file actions and reading (2026-11-16)

1. **Land on Lantern Workshop** (Starter Sky: select it, G). The camera arrives a little higher and looks further ahead:
   the launchers, the groves and, at about one o'clock on the first ring, the workbench with its sign ("the workbench ·
   jump on"). Stand still and take it in; the first step (or a jump) eases the camera back in close behind you.
   Compare a small world (Quiet Moon) and a large one (Music Box).
2. **Files and folders, one rule.** Click Lantern.sln (its object, or its card over the bench once revealed): it is
   selected and the card shows its actions. Double-click it: it opens with Windows (the app associated with .sln), as
   Enter or the card's Open would; nothing hops. Double-click the src card or the src pad: you fly there. If Windows
   cannot open a file, a red message says which file, why (no app for .sln, gone, access denied) and Windows' error.
3. **Twirl by a file.** Walk up to Lantern.csproj: the prompt says "E · look closer · or twirl (Space, Space) to preview".
   Twirl: its preview opens; nothing is launched. On the bench the spin still reads the README.
4. **Read.** On the bench, spin (or E): the README preview is rendered Markdown. Press R (or Read ⤢): the whole file in
   a wide reading view that scrolls; Esc closes it. A .txt file stays plain text. Links are shown, not followed; images
   are their description, never loaded.

## Silence and music (2026-11-16): sound off, drama, the new hazard score

1. **Sound off.** On the Starter Sky, click **🔊 Sound** at the right end of the toolbar (after Help): the music and every
   sound stop at once and the button turns amber, **🔇 Sound off**. Fly somewhere, walk to an edge of Event Horizon and fall:
   all silent. Press **N** (or click the amber button): the sound is back where the music would be. Quit while muted and
   start again: the button is amber from the start and a toast says the sound is off. (The legacy views make no sound and
   show no button; the state carries back to Planets.)
2. **Event Horizon's new score.** Fly in and stay a while: the same dark phrygian key and unstable lead, now racing: a
   heartbeat on every beat, a ♭2 bass, ticking metal; after the theme the music climbs (snare, toms, tremolo), chases
   (the theme's head turned downwards, a step higher each time), then the floor drops away (only the heart and the
   ticks), a build, and the theme hits again. Near an edge the warning should still cut through; fall off and listen to
   the spiral and the return.
3. **The drama elsewhere.** Go deeper down the Long Way Down: the trails' drop ends on a climax and a stop before its
   breakdown; the deep woods roll into a general pause and a tutti horn call; the underglow drops out, builds, and hits a
   sub drop. Samples, before and after: `%LOCALAPPDATA%\FileScape\evidence\2026-11-16-playtest-2\music\` (README.txt).

## Root landmarks (2026-11-16): the gateway and the monolith do something

1. From This computer, fly to your Linux distribution (Ubuntu). Its terminal monolith stands near the arrival, a faint amber ring of light round its foot. (On a root with many folders it stands a little further out, between the first two rings of launchers, so that no straight walk to a launcher runs into it.)
2. Walk up to it: the ring brightens, a warm light comes up on its face and the prompt says **E · terminal here · copy path · places**. Walk into it: it is solid; you slide off round it as round any pillar.
3. Press **E**: a small panel. **1** opens a shell of that distribution in `/`; **2** copies `/`; **3** copies `\\wsl.localhost\Ubuntu-24.04\`; then places: **~ home**, **/tmp**, **/mnt/c** (the Windows C: drive, opened as `C:\`). Choosing a place travels there. E or Esc closes the panel; walking away closes it too. Nothing starts until you press a number or click a row.
4. On a Windows drive the pane gateway does the same in blue: **1** opens Windows Terminal (or PowerShell if Windows Terminal is not installed) in that drive's root, **2** copies `D:\`, **3** copies `/mnt/d`, and the places are the drive's root, Home, Desktop, Documents and Downloads.

## Direct navigation (2026-11-16): the Destinations tree, trips, favourites

**Launch** from source (any PC): `tools/fs.sh run -- --root=sample --vis=planets` (the Starter Sky). About three minutes.

1. **The denser list.** Press **J** (or ☰ Destinations). One row per world, about a third more of them on screen; each
   says what is behind it: *36 folders*, *8 files*, *empty* (Quiet Moon), *junction*. The ★ section at the top lists your
   pins and the repositories FileScape has found (Lantern Workshop).
2. **Down the hierarchy.** Click the small ▸ beside **Long Way Down**: its folder opens underneath. Or with the keyboard:
   type "long", ↓, then → opens it and → again steps in; keep going three levels; ← closes, ← again steps out. A folder
   that has not been listed says *listing…* while FileScape lists just that one.
3. **A trip.** With **Old Bridge** (four levels down) chosen, press **Enter**: you fly Long Way Down → Meadow Gate →
   Fern Trail → Old Bridge, each hop the usual launch and landing, a little quicker, with a panel at the bottom: *Trip to
   Old Bridge · hop 2 of 4* and the route lit as you go. **Esc** (or ■ Stop) stops it: a launch that is winding up is
   cancelled, a hop in the air lands first; you stay on the last world reached. Shift+Enter (*Straight there*) warps
   directly instead.
4. **Up and back after a trip.** **U** goes to the parent of where you are (Fern Trail). **Backspace** undoes that, and
   Backspace again takes you to where the trip began: the whole trip is one step back.
5. **Find it deep down.** J, type "hollow": the list opens the way to Hollow Oak, six levels down; ↓ Enter flies there.
6. **Places you care about.** On a folder's card press **P** (★ Pin). From anywhere, J shows it under ★ Favourites &
   repositories with the repositories; choose one and it is one warp away.
7. **Dialogs out of the way.** Press **L** (Open a location): the row for where you are is marked *you are here* and
   cannot be chosen. Leave it open, press J and pick a world: Open Location and the list close before you fly.

## Walkthrough cues (2026-11-16): finding the target

Start the walkthrough (Help → Learn to play, or the welcome dialog on a fresh profile) and try to lose the target:

1. **Step 1 (look):** no beacon. The card shows the keys: `drag` and `← →`.
2. **Step 2 (the tower):** a lime column rises from the tower, with a lime ring round its base. Turn away until the tower is behind you: a big lime arrow appears at the screen's edge, pointing the way to turn, with "behind you · 20 m" under it. The card's line says the same. Keep turning: the arrow swaps sides. Face the tower from across the planet and the chevron waits at the horizon. Walk on and it pins over the tower's top. Within 6 m it grows and a lime pill says "Walk into the ring".
3. **Step 4 (the launcher):** the cue moves to the Lantern Workshop pad when the step begins. Walk away until the pad is behind the curve ("over the horizon"), then come back: on the pad the pill says "Stand here · press Space to fly" and the column fades so it doesn't fill the view.
4. **Steps 5 and 6 (the bench):** after the flight the cue is the workbench on the new world. On the deck the pill says "Press E to read the README".
5. **Steps 7 to 14:** key caps only (U, J ↓ Enter, U, Backspace, T, drag, wheel ↓ (since 2026-11-17), Esc).
6. **Leave** at any time: the column, the ring and the arrow disappear.

## Hazard Worlds (2026-11-16, PLANET-46, AD-59)

**Launch:** the sample world (toolbar *Sample world*, or `--root=sample --vis=planets`).

1. **Land.** In the Starter Sky, J, type "event", Enter. You land on a small island: straight ahead the ground ends a few
   steps away, and the black hole's jet climbs out of the opening into the sky; the camera turns to it as you land. The
   lit stripes are the routes; to the right a narrow ridge runs out across the open sky to the tower and its lookout.
2. **The way back** is behind you along a wide bridge (no gap): walk it once, then Home to hop back.
3. **The ridge.** Follow the lit stripe right of ahead: a 2 m bridge with nothing either side, and a gap of about
   1.5 m. Run at it and press Space a step before the edge (holding Space jumps further). On the far side, the stones
   up to the lookout.
4. **Launchers** stand on their own islands round the arrival (never in the opening ahead); every one is reached along
   the stripes, some with a gap on the way. J lists every folder and file; G flies to the selected one from anywhere.
5. **Fall.** Walk off any edge: rock crumbles, the rim glows, then the spiral round the black hole and back to the Starter
   Sky beside Event Horizon's launcher; Space on it and you are straight back in.
6. **Debris Field** (Event Horizon's third launcher) is the crowded one: 16 child worlds, 10 pads on its islands, the
   other 6 as moons in its sky (click one and press G, or pick it in J). Falling off it puts you back on Event Horizon,
   beside its launcher, on solid ground.
7. **Judge by feel:** bridge widths (1.8–3 m), the gap lengths (1.4–2.2 m), the higher camera on Hazard Worlds, the turn
   to the jet on landing, the warning sound on bridges (the edge is never far there), and the frame rate on Debris Field.

## Typing in Destinations and reading READMEs (2026-11-17, PLANET-51..53, AD-62)

**Launch** from source: `tools/fs.sh run -- --root=sample --vis=planets` for the list; for the reader, a repository
with a README, or the fixture `tools/make-wsl-fixture.sh --readme` prints (`tools/fs.sh run -- "--root=<that path>"
--vis=planets`). About four minutes.

1. **Type at once.** Press **J** and type a name straight away, capitals and all ("Lantern"): every letter lands in
   *Type to filter*, and nothing else happens (L is Locations, N is sound, T the overview… none of them fire). The same
   with the ☰ Destinations button, and again after you have travelled somewhere.
2. **Into the results and back, without the mouse.** **↓** or **Tab** moves into the results, on the best match.
   **→** opens a folder in place and → again steps into it; **←** steps out and closes it. **Backspace** in the results
   edits the filter (it no longer takes you back a world); **Home**, **End**, **Page Up/Down** move within the list (Page
   Up no longer flies you up a level). **Tab** or ↑ on the first row returns to the filter. **Enter** goes.
3. **Click away, keep typing.** With the list open, click the world (to look around), then type: the letters still go
   into the filter. **Esc** or **J** closes the list; then your keys are the game's again.
4. **The small preview.** Walk to a README or a .md file and press **E**. Its preview shows an excerpt (a long file
   ends "… an excerpt"); the wheel over it scrolls the text, not the camera. Its links work: click one.
5. **Expand.** Click **⤢ Expand (R)** in the preview's top-right corner, or press **R**: the reading view replaces the
   preview. The wheel scrolls it to the end (never the camera); the arrows, Page Up/Down, Home and End scroll it; other
   keys do nothing while you read. **Esc**, **R** or **Close** returns to the preview.
6. **Links.** In the reading view: a "#section" link scrolls to that heading; a link to another document in the
   project (even one with a space in its name) opens it here; a link to a folder takes you there; a web link opens your
   browser (only when you click it; the line under the text says where a link goes as you point at it); a missing target
   says *Not found*. Pictures stay their description: nothing is downloaded.
7. **Open with Windows.** **Enter** in the preview or the reading view, or the **Open with Windows** button, opens the
   file with its usual app; the message appears above the reader and on its status line, and if Windows refuses it says
   why. The app that opens it should come to the front (Windows decides); Alt+Tab returns to FileScape.
8. **Judge:** whether typing after J ever does anything else on your keyboard; the Expand button's place and look; the
   reading view's feel with the wheel; whether the opened app comes forward on your machine.

## Landmarks you can use, and the wheel (2026-11-17, PLANET-54..57, AD-63)

On the sample world (Sample world in the toolbar, or `--root=sample`):

1. **The telescope.** From the arrival, walk towards the lookout platform beside the tower (the Starter Sky's is next to
   the stack of books). About 7 m from it a sign comes up on the platform's wall: **E · Telescope — this world's files
   and destinations**, a cyan light comes on at the telescope, and the prompt says *Up the steps to the telescope*.
   Climb the stones (or jump and twirl straight up): on top the sign stands over the telescope and **E** opens the
   world's list. From the arrival nothing is shown.
2. **The bell tower.** Fly to **Music Box** (J, type "music", Enter). Its landmark is the bell tower. Walk straight in
   between two pillars: you step up onto the round stone base and stand under the bell (only the four pillars stop
   you). The prompt says *Space, Space · jump and twirl to ring the bell*. Jump: nothing. **Jump and twirl**: the bell
   swings and tolls — a big low bell in Music Box's key, on the beat, answered a beat later as it swings back. Twirl
   again to ring it again. A twirl beside the tower (off the base) is just a twirl. Walk on out the far side.
3. **The wheel.** Learn to play now has 14 steps. After **T** and the orbit drag, step 13 says *Scroll the mouse wheel
   down to zoom out*, with a `wheel ↓` key cap. A few notches show the next world along and the card says *Keep
   scrolling ↓ · N worlds in view*; at about 12 notches from Music Box the Starter Sky's neighbouring systems come in
   and the step is done. Scrolling up zooms back in. Then **Esc** puts you back where you stood. The arrival title no
   longer hangs over the map.
4. **The root landmarks.** On a Windows drive or a WSL distribution (from This computer), walk up to the gateway or the
   monolith: the prompt now says **E · Show location actions**, and its second line offers the twirl. **E** opens the
   panel; every row says what it does underneath: **1** Open terminal here, **2** Show in Explorer (new), **3** Copy
   path, **4** Copy the other form, then the places. **The quick route:** instead of E, jump and twirl at the mark — the
   panel opens with *Open terminal here* armed (lime border, "Enter"), and the prompt says *Enter · Open terminal here ·
   twirl again · Show in Explorer*. Twirl again to arm Show in Explorer. **Enter** runs the armed one; nothing starts
   from the twirl itself. A plain jump at the mark does nothing; a few steps away a twirl is only a twirl.
5. **Judge by feel and ear:** the bell's toll and swing, whether twelve wheel notches feel long, and whether the twirl
   then Enter at a root is quicker than E then 1.

## Near and far, relief, sky motion (2026-11-17, PLANET-58..60, AD-64)

**Launch:** the sample world (`--root=sample --vis=planets`), and for a crowded root a fixture made by
`tools/make-wsl-fixture.sh --root-like` (it prints the `\\wsl.localhost\…` path to open with Ctrl+L).

1. **Near and far.** Fly to Constellations (J, type "const", Enter). From the arrival, drag slowly round: where a pad
   on the inner ring lines up with one on the outer ring, the nearer name is larger and sits in front; the farther one
   stands just above it on a longer stem, and its stem passes behind the nearer name. Walk towards the near pad: its
   name grows a little, smoothly, and stops growing about when you reach the pad (never larger than 19 px). Click either
   name: it selects that destination (double-click flies there). On the root-like fixture, "etc" and "var" line up
   like this when you turn about 60° left from the arrival.
2. **Relief.** On Constellations, walk out past the outer ring towards the highest ground: long low rises and shallow
   dips, the horizon's outline no longer a perfect arc; pads, the arrival meadow and the spot where you land beside a
   pad stay level. Compare with `--test-relief=classic` (the hills as they were).
3. **Sky.** Stand still for a minute or two: about once a minute (40–90 s) a faint shooting star crosses the upper part
   of the view in under a second. Nothing appears for 10 s after a landing or in the overview (T). The stars turn
   about 1° a minute: invisible while you watch, but after a few minutes the galaxies and nebulae have moved on a
   little. A short recording: `%LOCALAPPDATA%\FileScape\evidence\2026-11-17-sky\sky-clip.avi`.

## Hazard World moves and gaps (2026-11-17, PLANET-61..63, AD-65)

**Launch:** the sample world (toolbar *Sample world*, or `--root=sample --vis=planets`), then J, "event", Enter.

1. **Read the gaps.** Three kinds, told apart by the ground. A **step** (the ridge's, 1.4–2.2 m) has pale ledges either
   side: any jump makes it. A **leap** (about 7 m) has glowing amber take-off boards with two amber posts at each end:
   it takes a twirl. Anything else is either a **hop** (a crack of 2 m or less between two pieces of rock) or a **chasm**
   (11 m or more): if it looks too far, find the lit path.
2. **A leap.** Debris Field (Event Horizon's launcher of that name) has one. Walk to its board, jump from the board and
   press Space again at the top of the jump: the twirl leans you forward. A plain jump falls short; so does a twirl
   pressed too early or once you are already dropping.
3. **The recovery twirl.** Walk slowly off any rim, let go of W, and press Space just after you drop: the traveller
   twirls back up and onto the rim. After a jump that just falls short, press Space the instant you drop below the far
   rim. Too late (or nothing within a metre) and it is a last effort: a flail and a cry, then the spiral as before, back
   to the Starter Sky, and Space on the launcher puts you straight back in.
4. **Judge by feel:** the recovery's window (about a tenth of a second after a short jump, 0.12–0.2 s after walking off),
   the twirl at the top of the jump, whether the leap's boards read from across the sky, the island outlines (spurs,
   lobes, ragged bridges), and whether any gap still looks jumpable and is not (where, please).

## Panels that own the keys, and a click on the world (2026-11-20, PLANET-115..119, AD-83)

**Launch:** the sample world (`--root=sample --vis=planets`); for a root's landmark, a real drive or distribution from
This computer (or, for a check, a fixture with `--test-root-look=windows`).

1. **The location actions.** At a root's gateway or monolith, E (or a jump and twirl) opens the panel, and it has the
   keys at once: **↑ ↓** move the lime highlight (it starts on the first row; after a twirl, on *Open terminal here*),
   **Enter** runs the highlighted row, the numbers **1–9** run their rows, **E** or **Esc** closes. With the panel open,
   W A S D, the arrows and Space do nothing in the world. (The second twirl that armed Show in Explorer is now ↓.)
2. **Settings** (F4 or ⚙) opens on its first setting: **Tab** and **↑ ↓** move, **Space** toggles, **← →** move a
   slider. **The welcome** (first launch) answers with **← →**, **Tab** and **Enter**; **Esc** is *Explore on my own*.
3. **Click the world** with Settings, Locations, Help or a menu open: the panel closes and the click does nothing else.
   With Destinations, the location actions or the reader open, a click closes them the same way, and a drag still looks
   around beside them. A confirmation and the welcome keep the click: they need an answer.
4. **One panel over another:** J, then F4, then Esc: Settings closes and typing lands in the list's filter again.
5. **Judge by feel:** whether a drag beside Destinations should close it too, and whether ↓ is as quick as the second
   twirl was.
