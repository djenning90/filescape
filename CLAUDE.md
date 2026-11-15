# FileScape — agent instructions

- **How we work:** `docs/development-methodology.md`. In short: plan with requirement IDs and acceptance criteria, ship vertical slices as separate commits, label results demonstrated / provisional / failed / unknown, separate instrument defects from product defects, and keep `docs/implementation-status.md` current.
- **Where things stand:** start from `docs/implementation-status.md`. Then read `docs/requirements.md` (IDs), `docs/decisions.md` (AD-xx) and `docs/architecture.md`.
- **Units and coordinates:** `docs/units.md`. Use the quantity types in `FileScape.Core.Units` and `TimeSpan` (via `Durations.FromSeconds` for computed values). Keep the two angle conventions straight, and name the unit of any primitive.
- **Code documentation:** `docs/code-documentation.md`. Every new or changed type gets a header saying what it owns, why it exists and what a change must preserve; name tuning values (cutoffs, timing, rates, limits) with units next to their use; check with `tools/doc-coverage.py --list`. A refactor that must not change behaviour is checked against the behaviour baseline (`FILESCAPE_BASELINE_OUT`).
- **Build, test, run, export from WSL:** `tools/fs.sh` (see `docs/windows-workflow.md`). Export only through `tools/fs.sh export`: it stages, verifies, smoke-launches and never replaces a running build.
- **Safety:**
  - Never disturb DJ's open applications, including a running FileScape.
  - Never list or scan private folders. Use the fixtures under `%TEMP%\FileScape-fixtures` and `/tmp/filescape-wsl-fixtures`.
  - Never change permissions on user files.
  - Don't push or publish.
