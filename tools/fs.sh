#!/usr/bin/env bash
# FileScape helper for working from WSL against the Windows toolchain. The source of truth stays in this WSL repo.
#   tools/fs.sh build            build the Godot C# project (and Core) with Windows dotnet
#   tools/fs.sh test [args]      run the test suite with Windows dotnet (includes Windows-only tests)
#   tools/fs.sh test-wsl         run the test suite with the Linux SDK (Windows-only tests skip)
#   tools/fs.sh run [-- args]    launch the app on Windows (args after -- go to FileScape, e.g. --root='C:\some dir')
#   tools/fs.sh editor           open the Godot editor on the project
#   tools/fs.sh export           export to a fresh staging dir, verify (files, hashes, a smoke launch), then promote it to
#                                %LOCALAPPDATA%\FileScape\build, keeping the previous build as previous-<stamp>
#   tools/fs.sh stage            the same export, verification and smoke launch into a fresh staging dir, never promoted
#                                (for verification runs while DJ uses the installed build)
#   tools/fs.sh builds           list build, staging and previous directories, and any FileScape that is running
#   tools/fs.sh prune-builds     delete staging/previous dirs that are not running (keeps the newest previous build)
#   tools/fs.sh which            print the discovered tools
#
# Windows programs are executed directly through WSL interop (no PowerShell command strings), so every argument
# keeps its boundaries (spaces and apostrophes included) and the real exit code is returned. Override discovery
# with FILESCAPE_DOTNET (path to dotnet.exe) or FILESCAPE_GODOT_DIR (folder containing the Godot .exe files).
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
    echo "sha256 FileScape.exe $(sha256sum "$d/FileScape.exe" | cut -d' ' -f1)"
    echo "sha256 FileScape.console.exe $(sha256sum "$d/FileScape.console.exe" | cut -d" " -f1)"
    echo "sha256 FileScape.App.dll $(sha256sum "$data/FileScape.App.dll" | cut -d' ' -f1)"
    echo "sha256 FileScape.Core.dll $(sha256sum "$data/FileScape.Core.dll" | cut -d' ' -f1)"
  } > "$d/build-info.txt"
}

smoke_build() {
  # Launch the staged exe on synthetic data: it must compose a place and quit cleanly (--automate=smoke).
  local dw="$1" d="$2"
  set +e
  (cd "$d" && timeout 120 ./FileScape.exe -- --automate=smoke "--out=$dw\\smoke" --window=640x360 --ui-scale=1 >/dev/null 2>&1)
  local code=$?
  set -e
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
    "$(find_dotnet)" test "$(wslpath -w "$REPO/tests/FileScape.Core.Tests")" -nologo "${NOSERVERS[@]}" "$@" ;;
  test-wsl)
    dotnet test "$REPO/tests/FileScape.Core.Tests" -nologo "$@" ;;
  run)
    G="$(find_godot_dir)"
    "$G/Godot_v4.7.2-stable_mono_win64_console.exe" --path "$APP_W" -- "$@" ;;
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
    mv "$STAGE" "$OUT"
    verify_build "$OUT" || { echo "fs.sh: promoted build failed verification at $OUT_W" >&2; exit 7; }
    echo "fs.sh: export OK -> $OUT_W\\FileScape.exe ($(grep '^build ' "$OUT/build-info.txt" | cut -d' ' -f2-))" ;;
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
    sed -n '2,13p' "$0"; exit 2 ;;
esac
