#!/usr/bin/env bash
# Creates a small, synthetic WSL fixture for LOC-03 / LINK-01 checks and prints its \\wsl.localhost path.
#   tools/make-wsl-fixture.sh [parent]     default parent: /tmp/filescape-wsl-fixtures
#   tools/make-wsl-fixture.sh --delete <dir>
# Contents: case pairs (Notes/notes, README.md/readme.md), Linux symlinks (to a folder, a file, a missing target,
# an absolute path, a loop, and /mnt/c), a FIFO, an executable script (never run by FileScape), and a small project
# (Cargo.toml + README.md + src/). Only this directory is created or deleted.
set -euo pipefail
if [[ "${1:-}" == "--delete" ]]; then
  d="${2:?dir}"; [[ -f "$d/.filescape-wsl-fixture" ]] || { echo "refusing: $d is not a FileScape WSL fixture" >&2; exit 2; }
  rm -rf -- "$d"; echo "deleted $d"; exit 0
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
mkdir -p project/.git && echo "ref: refs/heads/main" > project/.git/HEAD
distro="${WSL_DISTRO_NAME:-Ubuntu-24.04}"
echo "\\\\wsl.localhost\\$distro$(echo "$root" | tr '/' '\\')"
