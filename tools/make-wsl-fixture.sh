#!/usr/bin/env bash
# Creates a small, synthetic WSL fixture for LOC-03 / LINK-01 checks and prints its \\wsl.localhost path.
#   tools/make-wsl-fixture.sh [parent]     default parent: /tmp/filescape-wsl-fixtures
#   tools/make-wsl-fixture.sh --readme [parent]   a repository with a long real README (for --automate=planets-reading)
#   tools/make-wsl-fixture.sh --delete <dir>
# Contents: case pairs (Notes/notes, README.md/readme.md), Linux symlinks (to a folder, a file, a missing target,
# an absolute path, a loop, and /mnt/c), a FIFO, an executable script (never run by FileScape), and a small project
# (Cargo.toml + README.md + src/ + docs/, a real git repository) with a linked worktree. Only this directory is created or deleted.
set -euo pipefail
if [[ "${1:-}" == "--delete" ]]; then
  d="${2:?dir}"; [[ -f "$d/.filescape-wsl-fixture" ]] || { echo "refusing: $d is not a FileScape WSL fixture" >&2; exit 2; }
  rm -rf -- "$d"; echo "deleted $d"; exit 0
fi
if [[ "${1:-}" == "--readme" ]]; then
  # PLANET-44: a repository whose README is long, real Markdown (a copy of FileScape's own README, headings, lists, a
  # table, code, local images) plus a plain notes.txt, for --automate=planets-reading. Kept apart from the main fixture
  # so the other journeys' counts do not change.
  repo="$(cd "$(dirname "$0")/.." && pwd)"
  parent="${2:-/tmp/filescape-wsl-fixtures}"
  root="$parent/readme-$(date +%Y%m%d-%H%M%S)"
  mkdir -p "$root/.git/objects" "$root/.git/refs/heads" "$root/src" "$root/docs"
  touch "$root/.filescape-wsl-fixture"
  echo "ref: refs/heads/main" > "$root/.git/HEAD"
  cp "$repo/README.md" "$root/README.md"
  cp "$repo/docs/units.md" "$root/docs/units.md"
  printf 'namespace Demo;\npublic static class Program { public static void Main() { } }\n' > "$root/src/Program.cs"
  printf 'Plain notes: *not emphasis*, # not a heading, [b]not bold[/b].\nSecond line.\n' > "$root/notes.txt"
  distro="${WSL_DISTRO_NAME:-Ubuntu-24.04}"
  echo "\\\\wsl.localhost\\$distro$(echo "$root" | tr '/' '\\')"
  exit 0
fi
parent="${1:-/tmp/filescape-wsl-fixtures}"
root="$parent/wsl-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$root"
cd "$root"
touch .filescape-wsl-fixture
mkdir -p Notes notes real/sub project/src empty-room
echo "upper" > Notes/today.md
echo "lower" > notes/today.md
echo "# Title" > README.md
echo "# lower readme" > readme.md
printf '#!/bin/sh\necho "FileScape never runs this"\n' > run-me.sh && chmod +x run-me.sh
head -c 4096 /dev/zero > real/data.bin
echo "hi" > real/sub/hello.txt
ln -s real to-real
ln -s real/data.bin to-file
ln -s missing broken-link
ln -s /usr/share absolute-link
ln -s .. loop-up
ln -s /mnt/c to-windows
mkfifo pipe
printf '[package]\nname = "demo"\nversion = "0.1.0"\n' > project/Cargo.toml
echo "# Demo project" > project/README.md
echo 'fn main() {}' > project/src/main.rs
mkdir -p project/docs && echo "# Notes" > project/docs/notes.md
# A real repository (git init + one commit by a fixture identity) and a linked worktree, whose .git is a file (PLANET-27).
if command -v git >/dev/null 2>&1; then
  git -C project init -q -b main
  git -C project add -A
  git -C project -c user.name=fixture -c user.email=fixture@example.invalid commit -qm "fixture"
  git -C project worktree add -q ../project-worktree -b side
else
  mkdir -p project/.git && echo "ref: refs/heads/main" > project/.git/HEAD
fi
distro="${WSL_DISTRO_NAME:-Ubuntu-24.04}"
echo "\\\\wsl.localhost\\$distro$(echo "$root" | tr '/' '\\')"
