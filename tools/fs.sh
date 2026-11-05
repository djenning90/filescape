#!/usr/bin/env bash
# FileScape helper for working from WSL against the Windows toolchain. The source of truth stays in this WSL repo.
#   tools/fs.sh build            build the Godot C# project (and Core) with Windows dotnet
#   tools/fs.sh test [args]      run the test suite with Windows dotnet (includes Windows-only tests)
#   tools/fs.sh test-wsl         run the test suite with the Linux SDK (Windows-only tests skip)
#   tools/fs.sh run [-- args]    launch the app on Windows (args after -- go to FileScape, e.g. --root='C:\some dir')
#   tools/fs.sh editor           open the Godot editor on the project
#   tools/fs.sh export           export a Windows build to %LOCALAPPDATA%\FileScape\build and verify its files
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
  export)
    # Export into a staging folder first and replace the build only when it is complete and not running. On
    # 2026-11-05 a direct export deleted the unlocked files of a build DJ had open (the files were restored); never again.
    G="$(find_godot_dir)"
    OUT_W="$(win_env LOCALAPPDATA)\\FileScape\\build"
    OUT="$(wslpath "$OUT_W")"
    STAGE_W="$OUT_W-staging"
    STAGE="$(wslpath "$STAGE_W")"
    rm -rf "$STAGE"; mkdir -p "$STAGE"
    set +e
    "$G/Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path "$APP_W" --export-release "Windows Desktop" "$STAGE_W\\FileScape.exe"
    code=$?
    set -e
    # Godot can report success while producing an incomplete export (e.g. no .NET assemblies), so check the files.
    if [[ $code -ne 0 ]]; then echo "fs.sh: Godot export exited with $code" >&2; exit $code; fi
    if [[ ! -f "$STAGE/FileScape.exe" || ! -f "$STAGE/data_FileScape.App_windows_x86_64/FileScape.App.dll" || ! -f "$STAGE/data_FileScape.App_windows_x86_64/FileScape.Core.dll" ]]; then
      echo "fs.sh: export incomplete: FileScape.exe or its .NET assemblies are missing in $STAGE_W" >&2; exit 3
    fi
    running="$( (cd /mnt/c && powershell.exe -NoProfile -Command "(Get-Process FileScape -ErrorAction SilentlyContinue | Where-Object { \$_.Path -like '$OUT_W*' }).Id") | tr -d '\r' )"
    if [[ -n "$running" ]]; then
      echo "fs.sh: FileScape is running from $OUT_W (pid $running); the new export is complete in $STAGE_W. Close FileScape and run 'tools/fs.sh export' again (or use the staged build)." >&2
      exit 4
    fi
    rm -rf "$OUT"
    mv "$STAGE" "$OUT"
    echo "fs.sh: export OK -> $OUT_W\\FileScape.exe" ;;
  *)
    sed -n '2,13p' "$0"; exit 2 ;;
esac
