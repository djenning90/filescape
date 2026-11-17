# Next worlds: photos and videos, documents, Downloads

A design sketch (2026-11-16, AD-61, proposed). Nothing here is built. It answers DJ's brief: how photos and videos,
documents and Downloads could become distinct places; how FileScape would recognise each one; what would make each feel
different; and one thing the player could do there that makes the folder more useful. It also covers mixed folders,
overrides and risks, ranks the three ideas and names the first prototype.

## What exists to build on

- **Two layers already decide a planet's look.** `PlanetTheme` (`PlanetComposer.ThemeOf`) comes from what the folder
  holds. When one `FileCategory` has at least 45 % of the visible files (`DominantThemeShare`), the planet takes its
  theme: Meadow for images, Library for documents, Theatre for video, Vault for archives and executables. Otherwise it
  is Wild. `WorldCharacter` / `WorldKind` comes from what the place *is* (the hub, a volume, a distribution, a
  repository, a hazard, or a folder in depth band 1–4). It sets the sky, the sun, one arrival prop and the music
  (`MusicIdentity`, `SkySynth.Arrangements.cs`). The new worlds belong in the second layer. A folder that is mostly
  pictures already gets the Meadow. What it lacks is an identity and a useful thing to do.
- **Recognition is by names, platform and path context only** (FS-01). `HazardRules` shows the pattern: a short table
  of rules, each with the context it needs, a list of look-alikes tested as negatives, and a fixed precedence in
  `WorldCharacter.Of` (hub, then hazard, then repository, then roots, then folder).
- **The platform already names the folders.** `Main.Projects.KnownFolders()` asks Windows for Documents, Pictures,
  Videos, Music, Desktop and Downloads (Downloads by `FOLDERID_Downloads`). Paths redirected to OneDrive or another drive
  are reported where they really are. Localised display names don't matter, because the match is by path.
  `Prominence` marks these folders with ◆. It does this only on a live tree.
- **The file kinds come from extensions** (`FileCategories`). The table lacks several common kinds: `.heic`, `.tif`,
  camera raw (`.dng`, `.cr2`, `.nef`, `.arw`), `.m4v`, `.3gp`, `.xls`, `.ppt`, `.epub`, `.odp`, `.ods`, `.pages`,
  `.key`, and partial downloads (`.crdownload`, `.part`, `.partial`). Each world below needs some of them added.
- **Groves already form districts.** Files stand in one grove per category. A planet shows at most 64 files
  (`MaxItems`). A pile stands for the rest of a grove and says how many it holds.
- **Thumbnails are bounded, and they appear only when you ask.** Pictures (png/jpg/webp/bmp, ≤ 8 MiB read, 192 px) show
  on their easels automatically only in the Starter Sky. In your own folders a picture is read only when you preview it
  (FILE-02, DEV-02). Cloud placeholders and reparse points are never read.
- **The workbench is the model of "one useful thing"** (AD-50, PLANET-29). A playful move (landing, or a spin) only
  reveals, selects or previews. A panel lists the same entries. E and a click reach them without the move. Enter remains
  the only way to open anything.
- **Overrides exist, but they are not saved.** "Not a project" (IDENT-03) lasts for the session only. Pins are saved in
  `preferences.json`, keyed by `PathIdentity`.

## 1. Photos and videos: the Gallery

**Recognition** (first match wins; never inside a repository):

| Signal | Rule | False positives it must reject |
|---|---|---|
| Known folder | the Pictures or Videos known folder, or the `Pictures`/`Videos` child of a Linux home | none known |
| Camera names | a folder named `DCIM`, `Camera`, `Camera Roll`, `Screenshots`, `WhatsApp Images`, or one whose image files mostly follow camera patterns (`IMG_`, `DSC`, `PXL_`, `VID_`, a leading `yyyymmdd`) | `Screenshots` inside a project's docs (excluded because it is inside a repository) |
| Mix | ≥ 60 % of files are images or video, ≥ 12 of them, and a median image size ≥ 150 KB | icon sets, sprite sheets and game textures (many small files); web `assets/` folders (inside a repository); a `.svg`/`.ico` library |

The median size uses metadata FileScape already holds. It separates photographs from icons without reading a single
pixel.

**Feel.** A gallery planet: low, smooth hills, warm late-afternoon light, and a sky with soft drifting clouds that frame
the view. Each file stands as a framed picture on a stand, the next step up from today's easels. Video files are small
lantern screens, as in the Theatre theme. Frames hang along a **gallery path**: a looping walk that starts at the
arrival meadow and runs in date order, with a low arch for each month. The date comes from a date in the name when
there is one, and otherwise from the modified date. EXIF is never read. Music: a waltz in 3/4 with the theme on a soft
music box and harp, light brushes, and a shutter click as the hi-hat, near the hub's calm but with a gentle sway. A big
folder still shows at most 64 frames. These are the newest ones, spread over the months. Each arch's pile says "212 in
March 2024", and the panel lists them all.

**One useful action: the contact-sheet walk.** Walking along the path brings the pictures near you to life. The
thumbnails of the nearest ~24 frames load two at a time, and the farthest ones are released. A **spin under a month
arch** opens that month as a contact sheet in the panel: a grid of thumbnails for that month only. Click selects, and
double-click goes to the frame in the world. Enter opens the selected file, and Reveal and Copy path work as everywhere
else. This answers "where is that photo from last spring?" without opening the Photos app.

**Read-only and privacy: needs DJ's decision.** Today your pictures are read only when you preview one. The gallery
must read pictures as you walk, so it needs an explicit, per-folder yes. The first visit asks once ("Show the pictures
in this folder?"), and the answer is saved with the world override (below). Thumbnails stay in memory: none are written
to disk, nothing leaves the machine, and they are never captured in logs or probe screenshots of real folders.
Placeholders are never read. A Pictures folder synced with OneDrive may be mostly placeholders, so it shows empty
frames labelled "in the cloud".

## 2. Documents: the Reading Room

**Recognition** (never inside a repository, because a project's `docs/` would otherwise match):

| Signal | Rule | False positives it must reject |
|---|---|---|
| Known folder | the Documents known folder, or the `Documents` child of a Linux home | none, but see the note below |
| Mix | ≥ 60 % of files are documents (after adding `.xls`, `.ppt`, `.epub`, `.ods`, `.odp`), at least 8 of them | a folder of logs or exports (`.log` and `.csv` count as Data); a notes vault of hundreds of `.md` files (it qualifies, and that is correct) |

The Documents known folder is often a jumble of program folders ("My Games", "Visual Studio 2022", "Zoom"). In that
case it is mostly folders, and it stays a Hearth with the reading room as its arrival prop, not a full library planet.

**Feel.** Today's Library theme (lecterns and paper) with an identity of its own: an evening sky, warm lamplight
pooling on the paths, and slowly falling leaves of paper. Each kind of document gets its own shape: PDFs are bound
books, spreadsheets are ledgers, slides are standing boards, text and notes are loose sheets. Music: the theme on a
felt piano with soft tape warmth, a brushed snare like a page turn, about 84 BPM. It is the quietest world after the hub
and leaves room to think. In a big folder the lecterns stand by kind and the piles count the rest.

**One useful action: the reading nook.** Beside the arrival meadow stand an armchair and a lamp. **Sitting** (walk
onto the chair and press E, or land on it from a jump) raises a ring of cards: the 9 most recently modified documents
in this folder and in its subfolders that are already listed. Nothing new is listed to fill the ring. The panel lists
the same cards. **Version stacks** gather names that differ only by a version mark (`v2`, `final`, `FINAL`, `(1)`,
`copy`, a date) into one card that shows the newest and says "4 versions". Text and Markdown show the existing bounded,
inert preview. Other kinds show their details, with Enter to open (confirmed as now). This answers "what was I working
on?" and "which one is the latest?", the two questions a Documents folder is worst at answering.

## 3. Downloads: the Arrivals Port

**Recognition:**

| Signal | Rule | False positives it must reject |
|---|---|---|
| Known folder | the Downloads known folder (by `FOLDERID_Downloads`, so a moved Downloads is found) | none known |
| Name and place | a folder named `Downloads` or `Download` directly under a Windows volume root (`D:\Downloads`), a user profile, or a Linux home | a `downloads` folder inside a project, such as a website's release files (excluded because it is inside a repository); `Downloads` deeper in a tree (left alone) |

There is no guess from the mix alone. A folder of installers and archives is already a Vault, and calling it Downloads
would be wrong more often than right.

**Feel.** A small spaceport: a landing apron for the arrival meadow, a sky with dawn colours and slow-moving freight
lights, and an **arrivals board** next to the landmark. Files are parcels: crates for archives, sealed capsules for
installers (with a caution stripe; never a hazard), envelopes for documents, framed parcels for pictures. Parcels stand
in **rings by age**, using the existing `AgeBands` (≤ 1 week … > 1 year). The newest ring is around the arrival and the
oldest is far away. A partial download (`.crdownload`, `.part`) is a parcel still on its way, with a pulsing light.
Music: a station chime made of the theme's first four notes as the arrival flourish, then a steady motorik pulse with
plucked arpeggios, busy without urgency (about 120 BPM, between the hub and the outskirts). A folder of 5,000 files
keeps its 64 parcels for the newest arrivals. Each older ring is one pile labelled "1,140 older than a year · 38 GB".

**One useful action: the arrivals board.** Walk up to the board (or press E, or click it) and it flips like a station
board. The panel opens as a list sorted by arrival: the created date, which Windows sets when the download is written,
or the modified date when the created date is missing. It is grouped *Today / This week / This month / Older*. Each row
shows the name, its kind, its size and "3 days ago", with **Reveal in Explorer** and Copy path. Duplicates (`setup (1).exe`,
`setup (2).exe`) are gathered into one row. A footer sums up each age ring ("312 files older than six months, 21 GB").
The board turns "what did I just download, and where is it?" into one move. It also shows what could be tidied, and the
tidying stays in Explorer, where the person does it.

**Read-only.** The board needs nothing beyond names, sizes and dates. There's a case for one exception, "Move to
Recycle Bin" for old downloads, but this sketch argues **against** it for now. It would break the promise in the README.
It would need confirmations, undo and permission handling. And Reveal in Explorer gets the person to the same place in
one click. The Mark of the Web (the `Zone.Identifier` stream) is not read either.

## Mixed folders

- **One identity per world, districts for the rest.** Each world gets one identity: its sky, its music and its main
  feature. The identity comes from the known folder or the override, and failing those from the mix rules above. Any
  grove of at least 12 files keeps its category's look as a district, and one secondary feature may appear for the
  largest grove. A Downloads folder that is 70 % photos stays the Arrivals Port, and its picture grove gets a small
  gallery alcove (one month arch, the contact sheet behind it).
- **Precedence in `WorldCharacter.Of`:** hub, then hazard, then repository, then volume and distribution roots, then
  these new kinds, then folder bands. A hazard or a repository is never relabelled automatically.
- **Their children stay ordinary**, as the folders inside a hazard do. `Pictures\2024\March` is a Gallery only if its own
  mix says so. Most photo folders do, so the identity carries down naturally.

## Overrides

- **A per-folder choice saved in Preferences** (recommended): `worldKinds: { "<path>": "gallery" | "reading" |
  "arrivals" | "ordinary" }`, keyed by `PathIdentity` like the pins. It is set from the world's card ("This is a… ▾"),
  and "ordinary" turns recognition off for that folder. The Gallery's "show pictures" answer is stored with it. This
  writes only FileScape's own file, so the read-only promise holds. Unlike "Not a project" today, the choice persists.
- **A marker file** (optional, later): an empty file named `.filescape-gallery`, `.filescape-reading` or
  `.filescape-arrivals`, recognised by its name alone, as `.filescape-hazard` is. FileScape never creates one: the
  person makes it, and FileScape tells them how. It travels with the folder (to a USB drive or a share), and it lets the
  Starter Sky carry the new worlds with no special case. A preference wins over a marker, and a marker wins over
  recognition. Neither can make a folder a hazard or stop a hazard from being one.

## Risks

- **Privacy.** Photos are the most personal files on a PC. A gallery reads them only after the per-folder yes. Its
  thumbnails stay in memory and never leave the machine, and no real photo appears in evidence, logs or screenshots
  (probes use the Starter Sky and the fixtures). File names can be private too: version stacks and the board only
  display them.
- **Performance.** At 10,000 files the cost is sorting and grouping metadata that is already loaded (O(n log n), done
  once per visit in Core and tested for a time bound). The world stays within the existing 64-item and 48-moon budgets.
  Thumbnails are capped at about 96 textures in memory (192² × 4 B ≈ 14 MB), loaded two at a time, nearest first.
- **Cloud folders.** OneDrive-backed Pictures and Documents hold placeholders, which are never read: they appear as
  "in the cloud", never as broken.
- **Video frames.** Godot cannot decode a frame from mp4/mov. The Windows shell thumbnail could provide one, but it may
  make a cloud provider download the file. Until that is settled, videos stay lantern screens without pictures.
- **Wrong labels.** Each rule ships with its look-alikes as negative tests, as `HazardWorldTests` does, and the override
  fixes the rest.

## Ranking and the first prototype

1. **Downloads: the Arrivals Port. Prototype it first.** It answers a real, frequent question ("where did that file
   go?") with one move. Recognition is almost free of false positives (a known folder, or the name in one of a few
   places). It needs names, sizes and dates only, so it raises no new privacy or read-only question. It reuses
   `AgeBands`, piles, the panel and Reveal. It can be tested end to end in Core and with a fixture.
2. **Photos and videos: the Gallery.** The most magical of the three, and the one most likely to delight. It goes
   second because it needs DJ's decision on reading pictures while walking, plus a memory budget for thumbnails.
3. **Documents: the Reading Room.** The reading nook and the version stacks are useful, but the Library theme already
   gives documents a look. The Documents known folder is often a jumble, so the identity would show less often.

### Prototype acceptance criteria (draft IDs, numbered when the work is planned)

| ID | Requirement | Acceptance |
|---|---|---|
| NW-1 | **Recognition.** A folder is the Arrivals Port when it is the Downloads known folder, or is named `Downloads`/`Download` directly under a volume root, a profile or a Linux home; never inside a repository or a hazard; a saved override wins. Names only. | Core tests on synthetic trees: every positive; negatives (a project's `downloads`, `Downloads` at depth 3, a hazard named so, a repository called `Downloads`); precedence; override to ordinary and back. |
| NW-2 | **Identity.** The port has its own sky, arrival prop (the board) and music identity, with the station chime on arrival; launch and landing cues stay on the beat. | `SkySynth` tests (theme returns, loudness within 2 dB of the others, no clicks across a switch); a probe screenshot of the sample port. |
| NW-3 | **Age rings.** Parcels stand in `AgeBands` rings, newest nearest the arrival; partial downloads look unfinished; older rings collapse into piles with a count and a size. Budgets and reachability hold. | Composer tests: ring order by age; ≤ 64 items; every launcher and the board reachable; 5,000-file fixture composes in ≤ 50 ms. |
| NW-4 | **The board.** Walking up to it, E, or a click opens the arrivals list (Today / This week / This month / Older, newest first; duplicates gathered; per-ring totals) with Reveal and Copy path; double-click goes to the parcel. | In-app probe on the sample port with `--test-shell=record`: board opens from each route; order matches the fixture's dates; Reveal is recorded with the right path; nothing is opened. |
| NW-5 | **Read-only.** Nothing is read but names, sizes and dates; nothing is moved, changed or deleted; no alternate stream is read. | Code review and a probe log showing no content reads; existing FILE-02 checks still pass. |
| NW-6 | **Sample.** The Starter Sky gains a small "Arrivals" folder with a `.filescape-arrivals` marker and dated files, so anyone can visit the port. | `SampleSkyTests`: it classifies as the port and nothing else changes; the walkthrough probes still pass. |

Everything in this document is **provisional**: it is a design, and nothing has been played or measured.
