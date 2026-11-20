#!/usr/bin/env bash
# FileScape helper for working from WSL against the Windows toolchain. The source of truth stays in this WSL repo.
#   tools/fs.sh build            build the Godot C# project (and Core) with Windows dotnet
#   tools/fs.sh test [args]      run the test suite with Windows dotnet (includes Windows-only tests), one process
#                                (FS_TEST_SHARDS=1: three shards side by side, then one combined count)
#   tools/fs.sh test-wsl [args]  the Linux SDK (Windows-only tests skip); without args three shards side by side
#   tools/fs.sh run [-- args]    launch the app on Windows (args after -- go to FileScape, e.g. --root='C:\some dir')
#   tools/fs.sh editor           open the Godot editor on the project
#   tools/fs.sh export           export to a fresh staging dir, verify (files, hashes, a smoke launch), then promote it to
#                                %LOCALAPPDATA%\FileScape\build, keeping the previous build as previous-<stamp>
#   tools/fs.sh stage [--reuse]  the same export, verification and smoke launch into a fresh staging dir, never promoted
#                                (for verification runs while DJ uses the installed build); --reuse names an existing
#                                verified staging dir of the same source state (commit + uncommitted changes) instead
#   tools/fs.sh promote NAME     promote a verified staging dir (staging-<stamp>) to build, with export's safety checks
#   tools/fs.sh check TIER       verification tiers: fast | smoke | full | release, or journeys NAME... (tools/verify.sh;
#                                every run ends with a timing table: each step's result and seconds, and the total)
#   tools/fs.sh timed NAME [--lock] [--why=TEXT] -- CMD...   run CMD and log its seconds (and lock wait) in the
#                                session timing log beside check's steps (TEST-06); --why says why a rerun was needed
#   tools/fs.sh timing [--all|--session=S] [--rows]   the session's active, lock-wait and rerun totals
#   tools/fs.sh builds           list build, staging and previous directories, and any FileScape that is running
#   tools/fs.sh prune-builds     delete staging/previous dirs that are not running (keeps the newest previous build)
#   tools/fs.sh which            print the discovered tools
#
# Windows programs are executed directly through WSL interop (no PowerShell command strings), so every argument
# keeps its boundaries (spaces and apostrophes included) and the real exit code is returned. Override discovery
# with FILESCAPE_DOTNET (path to dotnet.exe) or FILESCAPE_GODOT_DIR (folder containing the Godot .exe files).
# FS_LAUNCH_LOCK=/path: every app launch (run, the smoke launch, and verify.sh's journeys and probes) holds that flock
# for the launch only, so several people or agents sharing one desktop take turns at the screen.
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"

win_env() { (cd /mnt/c && cmd.exe /d /c "echo %$1%" 2>/dev/null | tr -d '\r'); }

find_dotnet() {
  if [[ -n "${FILESCAPE_DOTNET:-}" ]]; then echo "$FILESCAPE_DOTNET"; return; fi
  local pf; pf="$(wslpath "$(win_env ProgramFiles)")"
  if [[ -x "$pf/dotnet/dotnet.exe" ]]; then echo "$pf/dotnet/dotnet.exe"; return; fi
  local w; w="$( (cd /mnt/c && cmd.exe /d /c where dotnet.exe 2>/dev/null) | tr -d '\r' | head -1 || true)"
  if [[ -n "$w" ]]; then wslpath "$w"; return; fi
  echo "fs.sh: Windows dotnet.exe not found (set FILESCAPE_DOTNET)" >&2; return 1
}

find_godot_dir() {
  if [[ -n "${FILESCAPE_GODOT_DIR:-}" ]]; then echo "$FILESCAPE_GODOT_DIR"; return; fi
  local d; d="$(wslpath "$(win_env USERPROFILE)")/tools/godot/4.7.2-stable-mono/Godot_v4.7.2-stable_mono_win64"
  if [[ -x "$d/Godot_v4.7.2-stable_mono_win64_console.exe" ]]; then echo "$d"; return; fi
  echo "fs.sh: Godot 4.7.2 .NET not found at $d (set FILESCAPE_GODOT_DIR)" >&2; return 1
}

APP_W="$(wslpath -w "$REPO/app")"
# MSBuild nodes and the compiler server inherit our stdout and outlive the build, so a caller waits until they
# time out (~10 min was observed for Godot's internal `dotnet publish` during export). These variables disable
# them for every Windows process we start, including the ones Godot starts. WSLENV forwards them across interop.
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
export WSLENV="${WSLENV:+$WSLENV:}MSBUILDDISABLENODEREUSE:DOTNET_CLI_USE_MSBUILD_SERVER:UseSharedCompilation"
# Build servers keep the stdout pipe open after the build finishes; disabling them prevents hangs.
NOSERVERS=(--disable-build-servers -nodeReuse:false -p:UseSharedCompilation=false)


# ---- build delivery helpers (export, builds, prune-builds)
fs_base_w() {
  # FILESCAPE_TEST_LOCALAPPDATA: test hook for the path checks ("" or an odd value must be refused).
  local lad; lad="${FILESCAPE_TEST_LOCALAPPDATA-$(win_env LOCALAPPDATA)}"
  if [[ ! "$lad" =~ ^[A-Za-z]:\\.+ ]]; then echo "fs.sh: LOCALAPPDATA did not resolve ('$lad'); refusing to continue" >&2; return 1; fi
  echo "$lad\\FileScape"
}

check_base() {
  # The base must be a real .../AppData/Local/FileScape directory on a Windows drive; never anything shorter.
  case "$1" in
    /mnt/[a-z]/*/AppData/Local/FileScape) mkdir -p "$1" ;;
    *) echo "fs.sh: unexpected build base '$1'; refusing to continue" >&2; exit 1 ;;
  esac
}

verify_build() {
  local d="$1" data="$1/data_FileScape.App_windows_x86_64" f
  for f in "$d/FileScape.exe" "$d/FileScape.console.exe" "$data/FileScape.App.dll" "$data/FileScape.Core.dll" "$data/GodotSharp.dll" \
           "$data/FileScape.App.runtimeconfig.json" "$data/FileScape.App.deps.json" "$data/System.Private.CoreLib.dll"; do
    [[ -s "$f" ]] || { echo "fs.sh: missing or empty: $f" >&2; return 1; }
  done
  local n; n="$(find "$data" -maxdepth 1 -type f | wc -l)"
  [[ "$n" -ge 150 ]] || { echo "fs.sh: only $n files in $data (expected >= 150)" >&2; return 1; }
  [[ "$(stat -c %s "$d/FileScape.exe")" -gt 50000000 ]] || { echo "fs.sh: FileScape.exe is unexpectedly small" >&2; return 1; }
}

write_build_info() {
  local d="$1" data="$1/data_FileScape.App_windows_x86_64"
  local commit dirty
  commit="$(git -C "$REPO" rev-parse --short=12 HEAD)"
  dirty="$(git -C "$REPO" status --porcelain | wc -l)"
  {
    echo "build $commit$([[ $dirty -gt 0 ]] && echo "+$dirty-uncommitted") $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "commit $(git -C "$REPO" rev-parse HEAD)"
    echo "uncommitted_paths $dirty"
    echo "files $(find "$data" -maxdepth 1 -type f | wc -l)"
    echo "source_state $(source_state)"
    echo "sha256 FileScape.exe $(sha256sum "$d/FileScape.exe" | cut -d' ' -f1)"
    echo "sha256 FileScape.console.exe $(sha256sum "$d/FileScape.console.exe" | cut -d" " -f1)"
    echo "sha256 FileScape.App.dll $(sha256sum "$data/FileScape.App.dll" | cut -d' ' -f1)"
    echo "sha256 FileScape.Core.dll $(sha256sum "$data/FileScape.Core.dll" | cut -d' ' -f1)"
  } > "$d/build-info.txt"
}

# source_state: a hash of what an export is built from: the commit, the uncommitted changes to tracked files and the
# untracked files that are not ignored (TEST-05). Two exports with the same source_state are of the same sources, so
# `stage --reuse` may use the earlier one. What it must preserve: any change to a file the export reads changes it.
source_state() {
  {
    git -C "$REPO" rev-parse HEAD
    git -C "$REPO" diff HEAD --binary
    (cd "$REPO" && git ls-files -o --exclude-standard -z | xargs -0 -r sha256sum)
  } | sha256sum | cut -c1-16
}

# reusable_stage BASE: the newest staging dir that is complete, smoke-launched and of this source state; a clean tree
# also matches a build-info written before source_state existed by its full commit and "uncommitted_paths 0".
reusable_stage() {
  local base="$1" state head clean d
  state="$(source_state)"; head="$(git -C "$REPO" rev-parse HEAD)"
  clean=0; [[ -z "$(git -C "$REPO" status --porcelain)" ]] && clean=1
  for d in $(ls -1d "$base"/staging-* 2>/dev/null | sort -r); do
    [[ -s "$d/build-info.txt" ]] && grep -q "smoke ok" "$d/smoke/smoke.log" 2>/dev/null || continue
    if grep -qx "source_state $state" "$d/build-info.txt" \
       || { [[ $clean == 1 ]] && ! grep -q '^source_state ' "$d/build-info.txt" && grep -qx "commit $head" "$d/build-info.txt" && grep -qx "uncommitted_paths 0" "$d/build-info.txt"; }; then
      verify_build "$d" 2>/dev/null && { echo "$d"; return 0; }
    fi
  done
  return 1
}

# launch CMD...: runs an app launch, holding FS_LAUNCH_LOCK if it is set; the lock's descriptor is closed in the command,
# so nothing it starts keeps the lock. Sets LAUNCH_WAIT (seconds spent waiting for the lock, one decimal).
LAUNCH_WAIT=0
launch() {
  [[ -n "${FS_LAUNCH_LOCK:-}" ]] || { "$@"; return; }
  local t c=0 lfd; t=$(date +%s.%N)
  exec {lfd}>>"$FS_LAUNCH_LOCK"; flock "$lfd"
  LAUNCH_WAIT="$(printf '%.1f' "$(echo "$(date +%s.%N) - $t" | bc)")"
  "$@" {lfd}>&- || c=$?
  exec {lfd}>&-
  return $c
}

# Core tests (TEST-05, AD-80). xunit runs test classes in parallel but each class's tests one after another, and
# SkySynthTests alone is ~145 s of that (the rest of the suite ~125 s spread over many classes), so one run takes ~140 s.
# Three processes with complementary filters (SkySynthTests whose name contains "theme", SkySynthTests whose name does
# not, everything else) run every test exactly once by construction and finish in about the longest shard's time. A
# shard that reports no result fails the run, so a filter that matched nothing cannot pass silently.
# Default per host (2026-11-19 measurement): WSL shards (152 s -> 80 s); Windows stays one process. Windows shards gained
# nothing (169 s sharded beside the WSL tests vs 154 s plain) and six test processes at once pushed the timing-bound
# "Rendering_one_second_of_music_with_six_effects_is_cheap" (best < 60 ms) to 63.8 ms. FS_TEST_SHARDS=1 or 0 forces it.
CORE_TEST_SHARDS=(
  'FullyQualifiedName~.SkySynthTests.&FullyQualifiedName~theme'
  'FullyQualifiedName~.SkySynthTests.&FullyQualifiedName!~theme'
  'FullyQualifiedName!~.SkySynthTests.'
)
# shard_count NAME LINE: the number after "NAME:" in dotnet test's result line.
shard_count() { grep -oE "$1: *[0-9]+" <<<"$2" | grep -oE '[0-9]+$'; }
# core_tests HOST(windows|wsl) [ARGS...]: with ARGS (or FS_TEST_SHARDS=0) one plain `dotnet test` as before; otherwise
# a build, the shards side by side, their output one after another, and one line "Core tests (HOST, ...): Passed!/Failed!
# - Failed: N, Passed: N, Skipped: N, Total: N" summed over the shards.
core_tests() {
  local host="$1"; shift
  local -a dn extra; local proj
  if [[ "$host" == windows ]]; then dn=("$(find_dotnet)"); proj="$(wslpath -w "$REPO/tests/FileScape.Core.Tests")"; extra=("${NOSERVERS[@]}")
  else dn=(dotnet); proj="$REPO/tests/FileScape.Core.Tests"; extra=(); fi
  local shards=1; [[ "$host" == windows ]] && shards=0
  if [[ $# -gt 0 || "${FS_TEST_SHARDS:-$shards}" == 0 ]]; then "${dn[@]}" test "$proj" -nologo "${extra[@]}" "$@"; return; fi
  local t0=$SECONDS
  "${dn[@]}" build "$proj" -nologo -v q "${extra[@]}" || { echo "fs.sh: Core tests did not build ($host)" >&2; return 1; }
  local tmp i code=0; tmp="$(mktemp -d)"
  SHARD_PIDS=()
  trap 'kill "${SHARD_PIDS[@]}" 2>/dev/null; exit 130' INT TERM
  for i in "${!CORE_TEST_SHARDS[@]}"; do
    # The subshell inherits set -e: the exit code is caught with || so a failing shard still writes its code file.
    ( s=$SECONDS; c=0; "${dn[@]}" test "$proj" --no-build -nologo "${extra[@]}" --filter "${CORE_TEST_SHARDS[$i]}" > "$tmp/$i.out" 2>&1 || c=$?; echo "$c $((SECONDS - s))" > "$tmp/$i.code" ) &
    SHARD_PIDS+=($!)
  done
  wait "${SHARD_PIDS[@]}" || true
  trap - INT TERM
  local f=0 p=0 k=0 n=0 line c secs
  for i in "${!CORE_TEST_SHARDS[@]}"; do
    read -r c secs < "$tmp/$i.code" || { c=1; secs='?'; }
    echo "---- shard $((i + 1))/${#CORE_TEST_SHARDS[@]} ($host, ${secs} s, exit $c): ${CORE_TEST_SHARDS[$i]}"
    cat "$tmp/$i.out"
    line="$(grep -E '(Passed|Failed)! +- Failed:' "$tmp/$i.out" | tail -1 || true)"
    if [[ -z "$line" ]]; then echo "fs.sh: shard $((i + 1)) reported no test result" >&2; code=1; continue; fi
    [[ $c -ne 0 ]] && code=1
    f=$((f + $(shard_count Failed "$line"))); p=$((p + $(shard_count Passed "$line")))
    k=$((k + $(shard_count Skipped "$line"))); n=$((n + $(shard_count Total "$line")))
  done
  rm -rf "$tmp"
  local word="Passed!"; [[ $code -ne 0 || $f -gt 0 ]] && word="Failed!"
  echo "Core tests ($host, ${#CORE_TEST_SHARDS[@]} shards side by side, $((SECONDS - t0)) s with the build): $word  - Failed: $f, Passed: $p, Skipped: $k, Total: $n"
  return $code
}

smoke_build() {
  # Launch the staged exe on synthetic data: it must compose a place and quit cleanly (--automate=smoke).
  local dw="$1" d="$2"
  set +e
  launch env -C "$d" timeout 120 ./FileScape.exe -- --automate=smoke "--out=$dw\\smoke" --window=640x360 --ui-scale=1 >/dev/null 2>&1
  local code=$?
  set -e
  [[ -n "${FS_LAUNCH_LOCK:-}" ]] && echo "fs.sh: smoke launch lock wait $LAUNCH_WAIT s"
  [[ $code -eq 0 ]] && grep -q "smoke ok" "$d/smoke/smoke.log" 2>/dev/null && { echo "fs.sh: smoke launch OK ($(grep 'smoke ok' "$d/smoke/smoke.log" | tail -1))"; return 0; }
  echo "fs.sh: smoke launch failed (exit $code)" >&2; return 1
}

running_paths() {
  # Paths of running FileScape processes (one per line). Exit status is non-zero if this cannot be established,
  # so callers fail closed instead of assuming nothing runs.
  # FILESCAPE_TEST_RUNNING: test hook ("fail" = the check itself fails; any other value = that running path).
  if [[ -n "${FILESCAPE_TEST_RUNNING:-}" ]]; then [[ "$FILESCAPE_TEST_RUNNING" == fail ]] && return 1; echo "$FILESCAPE_TEST_RUNNING"; return 0; fi
  local out code
  set +e
  out="$( (cd /mnt/c && powershell.exe -NoProfile -NonInteractive -Command "try { Get-Process -Name FileScape -ErrorAction SilentlyContinue | ForEach-Object { if (-not \$_.Path) { throw 'path unavailable' }; \$_.Path }; exit 0 } catch { exit 3 }") 2>/dev/null )"
  code=$?
  set -e
  [[ $code -eq 0 ]] || return 1
  tr -d '\r' <<<"$out"
}

cmd="${1:-}"; shift || true
[[ "${1:-}" == "--" ]] && shift

case "$cmd" in
  which)
    echo "dotnet: $(find_dotnet)"; echo "godot:  $(find_godot_dir)"; echo "app:    $APP_W" ;;
  build)
    "$(find_dotnet)" build "$APP_W\\FileScape.App.csproj" -nologo -v q "${NOSERVERS[@]}" ;;
  test)
    core_tests windows "$@" ;;
  test-wsl)
    core_tests wsl "$@" ;;
  run)
    G="$(find_godot_dir)"
    launch "$G/Godot_v4.7.2-stable_mono_win64_console.exe" --path "$APP_W" -- "$@" ;;
  editor)
    G="$(find_godot_dir)"
    "$G/Godot_v4.7.2-stable_mono_win64.exe" --editor --path "$APP_W" >/dev/null 2>&1 & ;;
  export|stage)
    # Safe delivery (2026-11-05, see docs/windows-workflow.md "Exporting and delivering a build"):
    #  1. resolve and sanity-check every path before touching anything;
    #  2. export into a NEW, unique staging directory (never reused, never deleted by this command);
    #  3. verify completeness, write build-info.txt (commit, hashes), and smoke-launch the staged exe;
    #  4. determine which FileScape processes run, failing closed if that cannot be established;
    #  5. promote by renaming (build -> previous-<stamp>, staging -> build) only if nothing runs from build.
    G="$(find_godot_dir)"
    BASE_W="$(fs_base_w)"; BASE="$(wslpath "$BASE_W")"
    check_base "$BASE"
    if [[ "$cmd" == stage && "${1:-}" == --reuse ]] && found="$(reusable_stage "$BASE")"; then
      echo "fs.sh: stage reused (same source state $(source_state); not exported again) -> $BASE_W\\$(basename "$found")\\FileScape.exe ($(grep '^build ' "$found/build-info.txt" | cut -d' ' -f2-))"
      exit 0
    fi
    STAMP="$(date -u +%Y%m%d-%H%M%S)-$$"
    STAGE_W="$BASE_W\\staging-$STAMP"; STAGE="$BASE/staging-$STAMP"
    OUT_W="$BASE_W\\build"; OUT="$BASE/build"
    mkdir "$STAGE"   # fails if it somehow exists: a staging dir is never reused
    echo "fs.sh: exporting to $STAGE_W"
    set +e
    "$G/Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path "$APP_W" --export-release "Windows Desktop" "$STAGE_W\\FileScape.exe"
    code=$?
    set -e
    if [[ $code -ne 0 ]]; then echo "fs.sh: Godot export exited with $code; staged files left in $STAGE_W" >&2; exit $code; fi
    verify_build "$STAGE" || { echo "fs.sh: export incomplete in $STAGE_W (left for inspection)" >&2; exit 3; }
    write_build_info "$STAGE"
    smoke_build "$STAGE_W" "$STAGE" || { echo "fs.sh: the staged build did not pass the smoke launch; not promoted ($STAGE_W)" >&2; exit 5; }
    if [[ "$cmd" == stage ]]; then echo "fs.sh: stage OK (not promoted) -> $STAGE_W\\FileScape.exe ($(grep '^build ' "$STAGE/build-info.txt" | cut -d' ' -f2-))"; exit 0; fi
    running="$(running_paths)" || { echo "fs.sh: could not determine which FileScape processes run; not promoting. Verified build: $STAGE_W\\FileScape.exe" >&2; exit 6; }
    if grep -qiF "$OUT_W\\" <<<"$running"; then
      echo "fs.sh: FileScape is running from $OUT_W; not replacing it. Verified build: $STAGE_W\\FileScape.exe (run 'tools/fs.sh export' again after closing FileScape, or use it directly)." >&2
      exit 4
    fi
    if [[ -e "$OUT" ]]; then mv "$OUT" "$BASE/previous-$STAMP"; echo "fs.sh: previous build kept as $BASE_W\\previous-$STAMP"; fi
    # 2026-11-14: the second rename can fail too (a transient handle on the new folder); then put the old build back, so
    # build\ is never left missing, and say how to finish (fs.sh promote).
    if ! mv "$STAGE" "$OUT"; then
      [[ -e "$BASE/previous-$STAMP" && ! -e "$OUT" ]] && mv "$BASE/previous-$STAMP" "$OUT" && echo "fs.sh: put the previous build back in $BASE_W\\build" >&2
      echo "fs.sh: could not move the staged build into place; it is verified in $STAGE_W. Run: tools/fs.sh promote $(basename "$STAGE")" >&2; exit 8
    fi
    verify_build "$OUT" || { echo "fs.sh: promoted build failed verification at $OUT_W" >&2; exit 7; }
    echo "fs.sh: export OK -> $OUT_W\\FileScape.exe ($(grep '^build ' "$OUT/build-info.txt" | cut -d' ' -f2-))" ;;
  promote)
    # Promote an already verified staging dir (2026-11-11): the same safety as export's last steps (nothing may run from
    # build; build -> previous-<stamp>, staging -> build; verify), for when the rename failed (a transient handle).
    BASE_W="$(fs_base_w)"; BASE="$(wslpath "$BASE_W")"
    check_base "$BASE"
    name="${1:?usage: fs.sh promote staging-<stamp>}"
    [[ "$name" =~ ^staging-[0-9]{8}-[0-9]{6}-[0-9]+$ ]] || { echo "fs.sh: not a staging dir name: $name" >&2; exit 2; }
    STAGE="$BASE/$name"; OUT_W="$BASE_W\\build"; OUT="$BASE/build"
    verify_build "$STAGE" || { echo "fs.sh: $name is not a complete build" >&2; exit 3; }
    [[ -s "$STAGE/build-info.txt" && -d "$STAGE/smoke" ]] || { echo "fs.sh: $name has no build-info or smoke result (not staged by fs.sh)" >&2; exit 3; }
    running="$(running_paths)" || { echo "fs.sh: could not determine which FileScape processes run; not promoting." >&2; exit 6; }
    if grep -qiF "$OUT_W\\" <<<"$running"; then echo "fs.sh: FileScape is running from $OUT_W; not replacing it." >&2; exit 4; fi
    STAMP="$(date -u +%Y%m%d-%H%M%S)-$$"
    if [[ -e "$OUT" ]]; then mv "$OUT" "$BASE/previous-$STAMP" || { echo "fs.sh: could not move the current build aside (a file in it is open); nothing changed." >&2; exit 8; }; echo "fs.sh: previous build kept as $BASE_W\\previous-$STAMP"; fi
    if ! mv "$STAGE" "$OUT"; then
      [[ -e "$BASE/previous-$STAMP" && ! -e "$OUT" ]] && mv "$BASE/previous-$STAMP" "$OUT" && echo "fs.sh: put the previous build back in $OUT_W" >&2
      echo "fs.sh: could not move $name into place; try again in a moment." >&2; exit 8
    fi
    verify_build "$OUT" || { echo "fs.sh: promoted build failed verification at $OUT_W" >&2; exit 7; }
    echo "fs.sh: promote OK -> $OUT_W\\FileScape.exe ($(grep '^build ' "$OUT/build-info.txt" | cut -d' ' -f2-))" ;;
  package)
    # A release zip from a verified build folder (2026-11-15, the publication review): the build without its smoke-test
    # log, plus licences and notices (FileScape's LICENSE if the repo has one, the Godot Engine's licence and third-party
    # notices generated by the same Godot, and the .NET runtime's licence and third-party notices), plus a self-contained quick start for players.
    #   tools/fs.sh package [staging-<stamp>|build]   -> %LOCALAPPDATA%\FileScape\FileScape-<build>-win-x64.zip
    BASE_W="$(fs_base_w)"; BASE="$(wslpath "$BASE_W")"
    check_base "$BASE"
    name="${1:-build}"
    [[ "$name" == build || "$name" =~ ^staging-[0-9]{8}-[0-9]{6}-[0-9]+$ ]] || { echo "fs.sh: not a build folder name: $name" >&2; exit 2; }
    SRC="$BASE/$name"
    verify_build "$SRC" || { echo "fs.sh: $name is not a complete build" >&2; exit 3; }
    id="$(grep '^build ' "$SRC/build-info.txt" | cut -d' ' -f2)"
    WORK="$BASE/package-$id"; rm -rf -- "$WORK"; mkdir -p "$WORK/FileScape/licenses"
    (cd "$SRC" && tar cf - --exclude=./smoke --exclude='*.log' .) | (cd "$WORK/FileScape" && tar xf -)
    [[ -f "$REPO/LICENSE" ]] && cp "$REPO/LICENSE" "$WORK/FileScape/LICENSE.txt" || echo "fs.sh: NOTE no LICENSE in the repo yet: the package has none (a decision for DJ)" >&2
    cp "$REPO/docs/windows-playtest.txt" "$WORK/FileScape/README.txt"
    G="$(find_godot_dir)"
    "$G/Godot_v4.7.2-stable_mono_win64_console.exe" --headless --script "$(wslpath -w "$REPO/tools/godot-notices.gd")" -- "$(wslpath -w "$WORK/FileScape/licenses")\\godot-engine.txt" >/dev/null 2>&1
    [[ -s "$WORK/FileScape/licenses/godot-engine.txt" ]] || { echo "fs.sh: could not generate the Godot notices" >&2; exit 5; }
    DN="$(wslpath "$(win_env ProgramFiles)")/dotnet"
    cp "$DN/LICENSE.txt" "$WORK/FileScape/licenses/dotnet-LICENSE.txt" && cp "$DN/ThirdPartyNotices.txt" "$WORK/FileScape/licenses/dotnet-ThirdPartyNotices.txt" || { echo "fs.sh: .NET licence files not found in $DN" >&2; exit 5; }
    ZIP_W="$BASE_W\\FileScape-$id-win-x64.zip"
    # Windows' own bsdtar writes a standard ZIP (forward-slash entry names); Compress-Archive wrote "FileScape\FileScape.exe",
    # which Explorer accepts but unzip on Linux/macOS extracts as single files with backslashes in their names (2026-11-19).
    rm -f -- "$BASE/FileScape-$id-win-x64.zip"
    (cd "$WORK" && /mnt/c/Windows/System32/tar.exe -a -c -f "$ZIP_W" FileScape) || exit 6
    rm -rf -- "$WORK"
    sha="$(sha256sum "$BASE/FileScape-$id-win-x64.zip" | cut -d' ' -f1)"
    echo "fs.sh: package OK -> $ZIP_W (sha256 $sha)" ;;
  check)
    if [[ "${1:-}" == harness ]]; then exec "$REPO/tools/verify-selftest.sh"; fi
    exec "$REPO/tools/verify.sh" "$@" ;;
  timed)
    # TEST-06 (AD-88): an ad-hoc launch or probe in the session's accounting. `fs.sh timed NAME [--lock] [--why=TEXT] --
    # CMD...` runs CMD (holding FS_LAUNCH_LOCK with --lock), prints its seconds and appends a row to the session timing
    # log (tools/verify-lib.sh timing_log_append), the same log check's steps go to. Exits with CMD's exit code.
    source "$REPO/tools/verify-lib.sh"
    name="${1:?usage: fs.sh timed NAME [--lock] [--why=TEXT] -- CMD...}"; shift
    lock=0
    while [[ $# -gt 0 && "$1" != -- ]]; do
      case "$1" in --lock) lock=1 ;; --why=*) export FS_RERUN_WHY="${1#--why=}" ;; *) echo "fs.sh timed: unknown option $1" >&2; exit 2 ;; esac
      shift
    done
    [[ "${1:-}" == -- ]] && shift
    [[ $# -gt 0 ]] || { echo "usage: fs.sh timed NAME [--lock] [--why=TEXT] -- CMD..." >&2; exit 2; }
    LAUNCH_WAIT=0; c=0
    if [[ $lock == 1 && -n "${FS_LAUNCH_LOCK:-}" ]]; then
      # launch() measures the lock wait; the active seconds start once the lock is held.
      t_all=$(date +%s.%N); launch "$@" || c=$?
      secs="$(printf '%.1f' "$(echo "$(date +%s.%N) - $t_all - $LAUNCH_WAIT" | bc)")"
    else
      t0=$(date +%s.%N); "$@" || c=$?; secs="$(secs_since "$t0")"
    fi
    result=pass; [[ $c -eq 124 ]] && result=timeout; [[ $c -ne 0 && $c -ne 124 ]] && result=fail
    rerun="$(timing_log_append timed "$name" "$result" "$secs" "$LAUNCH_WAIT")"
    echo "fs.sh timed: $name $result ${secs} s active, lock wait ${LAUNCH_WAIT} s$([[ $rerun == 1 ]] && echo ", rerun (${FS_RERUN_WHY:-no reason given})") -> $(timing_log_path)" >&2
    exit $c ;;
  timing)
    # TEST-06: the session's totals from the timing log: active seconds, lock-wait seconds and reruns, kept apart.
    #   fs.sh timing [--session=NAME | --all] [--rows]
    source "$REPO/tools/verify-lib.sh"
    log="$(timing_log_path)"; sess="$(timing_session)"; rows=0
    for a in "$@"; do case "$a" in --all) sess="" ;; --session=*) sess="${a#--session=}" ;; --rows) rows=1 ;; *) echo "fs.sh timing: unknown option $a" >&2; exit 2 ;; esac; done
    [[ -f "$log" ]] || { echo "fs.sh timing: no log yet ($log)"; exit 0; }
    awk -F'\t' -v s="$sess" -v rows=$rows '
      s != "" && $2 != s { next }
      { n++; a += $6; w += $7; if ($8 == 1) { r++; rs += $6; why[$4] = why[$4] (why[$4] ? "; " : "") $9 }; if ($3 == "timed") { tn++; ts += $6 }
        if (rows) printf "%s  %-6s %-28s %-8s %8.1f s  wait %6.1f s%s\n", $1, $3, $4, $5, $6, $7, ($8 == 1 ? "  rerun: " $9 : "") }
      END {
        printf "session %s: %d rows (%d ad-hoc, %.1f s)\n", (s == "" ? "(all)" : s), n, tn, ts
        printf "  active    %9.1f s\n  lock-wait %9.1f s\n  reruns    %9.1f s in %d row(s)\n", a, w, rs, r
        for (k in why) printf "    rerun %s: %s\n", k, why[k]
      }' "$log" ;;
  builds)
    BASE_W="$(fs_base_w)"; BASE="$(wslpath "$BASE_W")"; check_base "$BASE"
    for d in "$BASE"/build "$BASE"/staging-* "$BASE"/previous-*; do
      [[ -d "$d" ]] || continue
      info="(no build-info)"; [[ -f "$d/build-info.txt" ]] && info="$(grep '^build ' "$d/build-info.txt" | cut -d' ' -f2-)"
      complete="complete"; verify_build "$d" >/dev/null 2>&1 || complete="INCOMPLETE"
      echo "$(basename "$d"): $complete $info"
    done
    echo "running: $(running_paths || echo '(could not determine)')" ;;
  prune-builds)
    BASE_W="$(fs_base_w)"; BASE="$(wslpath "$BASE_W")"; check_base "$BASE"
    running="$(running_paths)" || { echo "fs.sh: could not determine running FileScape processes; nothing deleted" >&2; exit 6; }
    newest_prev="$(ls -1d "$BASE"/previous-* 2>/dev/null | sort | tail -1 || true)"
    for d in "$BASE"/staging-* "$BASE"/previous-*; do
      [[ -d "$d" && "$d" != "$newest_prev" ]] || continue
      dw="$BASE_W\\$(basename "$d")"
      if grep -qiF "$dw\\" <<<"$running"; then echo "keep (running): $dw"; continue; fi
      rm -rf -- "$d"; echo "deleted: $dw"
    done ;;
  *)
    sed -n '2,28p' "$0"; exit 2 ;;
esac
