# Publishing FileScape on GitHub

A review of what a fresh clone and a release package would expose (2026-11-15, PLANET-36; refreshed 2026-11-17 for the
build after the second playtest), what has been fixed, how the history is backed up and prepared for publication, the
decisions that are DJ's to make, and the exact remaining steps. The private backup is on GitHub (see the newest
refresh); nothing has been published.

## 2026-11-20 (seventh pass) refresh at a glance

| Item | State |
|---|---|
| Off-machine backup (GitHub, private) | **in use**: `origin` = github.com/djenning90/filescape (private, Git Credential Manager). DJ pushed the sixth pass (`e03ab2e`, 2026-11-20 17:04). From the seventh pass, DJ asked agents to push completed, verified milestones of `main`; the pushed commit is named in docs/implementation-status.md |
| Owner and repository | settled in practice: `djenning90/filescape` (private). The public name stays parked (AD-73) |
| .NET | **.NET 10** (AD-89) |
| Decisions | [Decisions for DJ](#decisions-for-dj-before-publishing): licence first. Note on item 3: the private origin already holds the original-email history, so a rewritten public history needs a new repository or a force-push, not a visibility switch |

## 2026-11-20 (evening) refresh at a glance

| Item | State |
|---|---|
| Off-machine backup (GitHub, private) | **remote set up by DJ**: `origin` = github.com/djenning90/filescape with Git Credential Manager; `origin/main` is `7680d22` (the fifth follow-up). The sixth pass (main, 60+ commits ahead) is **not pushed**: agents don't push (CLAUDE.md); DJ pushes |
| Windows ZIP | **`5ba12f91daa3`**, 74.8 MB, 199 entries, checked from a clean extraction; a fresh clone staged and packaged it by itself |
| Name | FileScape for now (AD-73); the naming note is preserved there |
| Decisions | unchanged: [Decisions for DJ](#decisions-for-dj-before-publishing) (licence first) |

## 2026-11-20 refresh at a glance

| Item | State |
|---|---|
| Off-machine backup (GitHub, private) | **still waiting for DJ's account** (checked 2026-11-20: no remote, no `gh`, no credential helper). Steps unchanged: [Backup](#backup-private-off-machine) |
| Local safety copy | **refreshed** after the fifth follow-up: the newest `%LOCALAPPDATA%\FileScape\backup\filescape-*.bundle` (verified, test-cloned) |
| Windows ZIP | **`70b6ffc7ebb6`**, 74.7 MB, 199 entries, checked from a clean extraction; a fresh clone staged and packaged it by itself |
| Name | FileScape for now (AD-73); the naming note is preserved there |
| Decisions | unchanged: [Decisions for DJ](#decisions-for-dj-before-publishing) |

## 2026-11-19 refresh at a glance

| Item | State |
|---|---|
| Off-machine backup (GitHub, private) | **waiting for DJ's account** (checked 2026-11-19): no remote, no `gh`, no SSH key, no GitHub account stored in Git Credential Manager, no credential helper in WSL git. Steps: [Backup](#backup-private-off-machine) |
| Local safety copy | **refreshed**: a verified bundle of `main` and the tags (the newest `%LOCALAPPDATA%\FileScape\backup\filescape-*.bundle`); the working tree is clean, so there is no `backup/working-tree` to add this time |
| Reviewed docs | **committed** (`4d1b6b5`): Codex's README, Windows workflow and `docs/windows-playtest.txt`; a fresh clone now stages and packages by itself (checked on `c200de5`) |
| Windows ZIP | **checked from a clean extraction** on `cfee25d2b709`: 72 MB, 196 files, smoke launch passes; standard ZIP entry names (`tools/fs.sh package` uses Windows' `tar.exe` since `e6daf54`; `Compress-Archive` wrote backslashes) |
| Publication candidate | **rebuilt on the final `main`** in a separate clone (placeholder identity, proposed MIT LICENSE as its last commit); the original history is untouched |
| .NET 10 | unchanged since 2026-11-17: verified in a scratch export, not adopted (see [.NET](#net-version)) |
| Decisions | [Decisions for DJ](#decisions-for-dj-before-publishing) |

## 2026-11-17 refresh at a glance

| Item | State | Where |
|---|---|---|
| Off-machine backup (GitHub, private) | **ready, needs DJ's account**: no remote exists and no GitHub access is configured in WSL (no `gh`, no SSH key); Git for Windows has Git Credential Manager 2.9 for a browser sign-in | `tools/backup-snapshot.sh`, [Backup](#backup-private-off-machine) |
| Local safety copy | **done** (246 commits): a verified git bundle of `main`, `backup/working-tree` (the uncommitted edits) and every tag on the Windows disk (same machine, not off-machine) | `%LOCALAPPDATA%\FileScape\backup\filescape-*.bundle` |
| History review | **done**: 216 commits, no secrets, no private folder contents; one identity to rewrite | [History](#history-what-it-exposes-and-the-smallest-cleanup) |
| Publication candidate | **built for review** with placeholder identity; rerun with your GitHub noreply address | `tools/publication-candidate.sh` → `../filescape-publication-candidate` |
| Windows ZIP | **checked from a clean extraction**: 74 MB zip, 196 files, `README.txt` quick start (Codex), licences, no logs, smoke launch passes | [Windows ZIP](#windows-zip) |
| .NET 10 | **verified in a scratch export**: app and Core on `net10.0` build, export with the .NET 10.0.15 runtime and pass the smoke tier (see below); not merged (a decision: .NET 8 support ended 2026-11-10) | [.NET](#net-version) |
| Licence, name, account, visibility, history | **DJ's decisions** | [Decisions](#decisions-for-dj-before-publishing) |

## What was checked

All tracked files and the full history (every commit and ref; no file was ever deleted from history), a sample of the
evidence screenshots and every evidence log path, the export preset, and the strings in a built package.

- **No secrets** (tokens, keys, passwords) anywhere in the history.
- **No private folder contents** in evidence: the screenshots and logs show fixtures, the bundled Starter Sky, FileScape's
  own data, or system locations (drive letters, the WSL root's top level). Exceptions worth a look: four files from
  2026-11-03 that scan `C:\Users\David\.nuget\packages` (`docs/evidence/2026-11-03/live-nuget*`; they show which NuGet
  packages were installed) and `live-dotnet.log` (`C:\Program Files\dotnet`). Many logs show the Windows user name
  "David" in fixture paths.
- **Assets:** everything is original and generated in code: worlds, icons (`FileIcons.cs`), the app icon (`app/icon.svg`),
  shaders, the sample files, and the music (synthesised, no samples). No fonts are bundled (Godot's default font).
- **Third-party software:** the Godot Engine and GodotSharp (MIT), the .NET runtime bundled in the release (MIT, with its
  third-party notices). Test-only packages (xUnit Apache-2.0, FluentAssertions 7.x Apache-2.0, Microsoft.NET.Test.Sdk
  MIT) are not shipped.

## Fixed

| Finding | Fix |
|---|---|
| The export packed build intermediates (`obj.win/…`, with machine paths such as `\\wsl.localhost\…\home\david\…`) into the PCK | `app/export_presets.cfg` excludes `obj.win/*, obj/*, bin/*, bin.win/*, .idea/*` |
| Source and PDB paths of the build machine in the DLLs and PDBs | `PathMap` (`/_/app/`, `/_/src/FileScape.Core/`) and deterministic builds in both projects |
| A release zip would carry no licences or notices for Godot and .NET, and would include the smoke-test log | `tools/fs.sh package <build>`: zips a verified build without `smoke/` and logs, with `licenses/` (Godot's licence and third-party list generated by the engine, the .NET licence and third-party notices), a standalone Windows playtest quick start, and FileScape's `LICENSE` once one exists |
| The window used the Godot logo as its icon | an original icon (`app/icon.svg`). The `.exe` file's own icon is still Godot's (changing it needs `rcedit`; `application/modify_resources` is off) |
| README only worked on DJ's laptop | rewritten for a newcomer: play, the walkthrough, legacy views, build from source (generic prerequisites, `FILESCAPE_GODOT_DIR`), credits |
| Hard-coded `C:\Users\David` in tools | `tools/verify.sh`, `tools/compare-drivers.sh` ask Windows for `%APPDATA%`/`%LOCALAPPDATA%` |
| `.claude/` (local agent worktrees) only ignored locally | in `.gitignore` |

## History: what it exposes and the smallest cleanup

DJ's preference (2026-11-17): keep the development history public, so someone can follow FileScape from the flat-world
prototype to Planets and through the playtests. Scan of every commit, message and file version (216 commits on `main`;
every `worktree-*` branch was merged, 0 commits ahead; none remain as of the seventh pass):

| Exposed | Where | Recommendation |
|---|---|---|
| The author email (a personal Gmail address) | every commit's author and committer, and quoted in this file in 56 commits | **rewrite** to your GitHub noreply address (the one cleanup recommended) |
| The Windows user name "David" | ~190,000 path occurrences, almost all `C:\Users\David\AppData\…` in evidence logs and fixtures | keep (it is the author's first name, already in the commit identity) |
| Laptop hardware, driver versions | `docs/archive/INVESTIGATION_01_FINDINGS.md`, `docs/implementation-status.md` | keep (part of the performance story) |
| Installed NuGet packages | `docs/evidence/2026-11-03/live-nuget*` (4 files), `live-dotnet.log` | keep, or `--drop` them (low sensitivity) |
| Agent prompts and handoffs | `docs/archive/FileScape_Project_Handoff.md`, `docs/archive/CLAUDE_INVESTIGATION_01.md`, `CLAUDE.md`, `docs/development-methodology.md` (names another project, "Possibility Engine") | keep: they are how the project was built |
| Long quotations from Nintendo interviews and manuals | `docs/galaxy-research.md` | the most copyright-sensitive file: keep, paraphrase at HEAD, or `--drop` (a drop removes it from every commit) |
| Evidence screenshots and logs | `docs/evidence/` (124 MB, 336 files; largest 1.6 MB) | keep: within GitHub's limits (no file over 50 MB) |
| Secrets, tokens, private folder contents | none found (fixtures, the Starter Sky, system locations only) | — |

**The smallest cleanup that preserves the story:** rewrite only the identity — every commit keeps its message, date,
files, order and co-author lines; the email is replaced in commit headers and in the copies of this file that quoted it.
Hashes change (identity is part of a commit), so anything that cites a hash (docs cite a few, e.g. build IDs) still
reads correctly but points at the old hash; `../filescape-publication-candidate-map.txt` maps old to new. No squash.

`tools/publication-candidate.sh --name NAME --email ID+USER@users.noreply.github.com --with-working-tree [--license-holder "NAME"] [--drop PATH]`
builds the candidate in a **separate clone** (`../filescape-publication-candidate`); this repository and its history are
never changed. Its check on 2026-11-17 (placeholder identity, on `main` at the end of the session): 247 commits (245 +
the working-tree commit + the proposed LICENSE), 162 trees identical and the others differing only in the copies of this
file and of the script's own early versions that spelled the email out, the old email in 0 commit headers, messages or
files. Rerun it after the final merge with your real noreply address (GitHub → Settings → Emails).

## Backup (private, off-machine)

Superseded 2026-11-20: the https `origin` exists and is in use (steps 1–3 below are done). Kept for the record.
What a push of `main` alone would miss: uncommitted working-tree edits and any tag (Codex's docs were committed in `4d1b6b5`). `tools/backup-snapshot.sh snapshot` commits the working tree onto
`backup/working-tree` (parent `main`) without touching the index, the working tree or `main`; `bundle DIR` writes and
test-clones a bundle; `push URL` pushes `main`, `backup/*` and tags and verifies every ref from a fresh clone.

Steps (your account):
1. Create an **empty private** repository on GitHub (web UI: New repository → Private, no README/licence), e.g. `<owner>/filescape`.
2. Let WSL git use the Windows credential manager (one browser sign-in, stored in Windows):
   `git config --global credential.helper "/mnt/c/Program\ Files/Git/mingw64/bin/git-credential-manager.exe"`
3. `tools/backup-snapshot.sh snapshot && tools/backup-snapshot.sh push https://github.com/<owner>/filescape.git`
   (prints "pushed and verified from a fresh clone").
4. If GitHub rejects the push because it exposes your email ("Block command line pushes that expose my email"), either
   push the rewritten candidate as the backup (`cd ../filescape-publication-candidate && git push <url> main`) and keep
   the original history locally and in the bundle, or turn the setting off for this one private push.
Tags worth adding before the push (suggested, not created): `playtest-1` at `c29785d` (first public playtest build),
`playtest-2` at `6b9b60c`, and the final build of this iteration.

## Windows ZIP

`tools/fs.sh package <build>` (2026-11-17, Codex's change: the standalone `docs/windows-playtest.txt` ships as
`README.txt`, because the repository README linked to files the ZIP did not contain). Checked by extracting the ZIP into
an empty folder: `FileScape/FileScape.exe`, `FileScape.console.exe`, `README.txt`, `build-info.txt`,
`data_FileScape.App_windows_x86_64/` (the app, Core and the bundled .NET runtime), `licenses/` (Godot's licence and
third-party list, the .NET licence and notices); no `smoke/`, no logs; 74 MB zipped, 185 MB extracted, 196 files; the
extracted copy passes the smoke launch. No machine paths: the only `obj.win` string is the mapped `/_/src/…` PDB path
(the step 7 grep below now excludes `/_/`). `LICENSE.txt` is added once a `LICENSE` exists. Unsigned: SmartScreen warns.
2026-11-19: `docs/windows-playtest.txt` is committed (`4d1b6b5`) and a fresh clone packages by itself; the ZIP is now
written by Windows' `tar.exe` with forward-slash entry names (Linux/macOS `unzip` made single files named
`FileScape\FileScape.exe` from the `Compress-Archive` ZIP).

## .NET version

.NET 8's support ended on 2026-11-10. **Adopted 2026-11-20 (seventh pass, AD-89):** the app, Core and Fixtures
target `net10.0` (the tests already did), so the export carries the .NET 10 runtime. The seventh pass's candidate is
verified on it (`check full`, real input, ZIP, fresh clone: see docs/implementation-status.md). The 2026-11-17 trial
in the `net10-check` worktree (the same three `TargetFramework` lines) is superseded by this change.

## Decisions for DJ (before publishing)

Short list (2026-11-19):

1. **Licence and copyright name (blocker).** No `LICENSE` yet, so a public repository would be "all rights reserved".
   Proposed: **MIT** (simplest; matches Godot and .NET). Needed from you: the licence and the exact copyright holder
   ("Copyright (c) 2026 <name>"). Then add `LICENSE` at the root; `tools/fs.sh package` puts it in the ZIP.
2. **GitHub owner and repository name.** Personal account or an organisation, and the name (`filescape` assumed).
   Note the name overlap: "FileScape" is also a 2008 GPLv2 Linux file manager on SourceForge and an MIT Media Lab
   project page; "FileScope" products exist. No trademark search was done.
3. **Author email.** Every commit carries your personal address. Choose: (a) rewrite it to your GitHub noreply
   address (`<id>+<user>@users.noreply.github.com`; the candidate does exactly this and nothing else; recommended for a
   public repo), (b) publish as is, or (c) a squashed snapshot (loses the history). For the **private backup**, the
   original history can be pushed as is unless GitHub's "Block command line pushes that expose my email" is on.
4. **The public Windows release.** Source only, or also a GitHub pre-release with the ZIP (e.g. `v0.1.0-playtest`);
   it is unsigned (SmartScreen warns) and targets .NET 10.
5. **.NET 10.** Done 2026-11-20 (AD-89): the app, Core and Fixtures target `net10.0`.
6. **Visibility.** Private first (backup); public only after you have reviewed the candidate from a fresh clone.
   Optional drops from history: the 2026-11-03 NuGet scans, `docs/galaxy-research.md` (long quotations).

## Steps (replace `<commit>`, `<owner>`, `<tag>`)

Steps 3, 4, 9 and 11 need your credentials. Build the candidate first (History, above) and run these from it.

0. Make the decisions above; add `LICENSE` and commit. If you rewrite history (3), do it now: it changes `<commit>`.
1. Confirm the commit: `git log -1 --format='%H %an <%ae>' <commit>`.
2. A fresh clone builds: `git clone --no-local /home/david/src/filescape /tmp/fs-clone && cd /tmp/fs-clone && git checkout <commit> && dotnet test tests/FileScape.Core.Tests`; on Windows follow the README.
3. `gh auth login`.
4. `gh repo create <owner>/filescape --private --description "Explore your files as little worlds you can run around"`.
5. Push only the reviewed commit as `main` (never `--all` or `--mirror`: local `worktree-*` branches and other refs would go too):
   `git remote add origin git@github.com:<owner>/filescape.git && git push origin <commit>:refs/heads/main`.
6. Tag: `git tag -a <tag> <commit> -m "FileScape <tag>: first public playtest" && git push origin refs/tags/<tag>`.
7. Build the release from `<commit>` in a clean worktree: `git worktree add ../filescape-release <commit> && ../filescape-release/tools/fs.sh stage`; its `build-info.txt` must name `<commit>` with no uncommitted paths. Check for machine paths: `strings -a <staging>/FileScape.exe <staging>/data_*/FileScape.*.dll | grep -iE 'home.david|Users.David|obj\.win/' | grep -vc '^/_/'` should print 0.
8. `tools/fs.sh package staging-<stamp>` → `%LOCALAPPDATA%\FileScape\FileScape-<build>-win-x64.zip` (and its SHA-256).
9. `gh release create <tag> FileScape-<build>-win-x64.zip --repo <owner>/filescape --verify-tag --prerelease --title "FileScape <tag>" --notes-file notes.md` (notes: unsigned build, the SHA-256, "Learn to play" on first launch).
10. Verify from GitHub: clone, check `git rev-parse HEAD` equals `<commit>`, build per the README; download the zip, check the SHA-256, run it on a clean Windows profile.
11. Make it public: `gh repo edit <owner>/filescape --visibility public --accept-visibility-change-consequences`.
12. `git worktree remove ../filescape-release`.
