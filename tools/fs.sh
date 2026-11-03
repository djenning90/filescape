#!/usr/bin/env bash
# FileScape helper for working from WSL against the Windows toolchain. Source of truth stays in this WSL repo.
#   tools/fs.sh build            build the Godot C# project (and Core) with Windows dotnet
#   tools/fs.sh test             run the test suite with Windows dotnet.exe (includes Windows-only tests)
#   tools/fs.sh test-wsl         run the test suite with the Linux SDK (Windows-only tests skip)
#   tools/fs.sh run [-- args]    launch the app on Windows (args after -- go to FileScape, e.g. --root=C:\some\dir)
#   tools/fs.sh editor           open the Godot editor on the project
#   tools/fs.sh export           export a Windows build to %LOCALAPPDATA%\FileScape\build
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
GODOT_DIR="${FILESCAPE_GODOT_DIR:-/mnt/c/Users/$(powershell.exe -NoProfile -Command '$env:USERNAME' | tr -d '\r')/tools/godot/4.7.2-stable-mono/Godot_v4.7.2-stable_mono_win64}"
GODOT_CONSOLE="$(wslpath -w "$GODOT_DIR/Godot_v4.7.2-stable_mono_win64_console.exe")"
GODOT_GUI="$(wslpath -w "$GODOT_DIR/Godot_v4.7.2-stable_mono_win64.exe")"
APP_W="$(wslpath -w "$REPO/app")"
REPO_W="$(wslpath -w "$REPO")"
ps() { (cd /mnt/c && powershell.exe -NoProfile -Command "$1" | tr -d '\r'); }
cmd="${1:-}"; shift || true
[[ "${1:-}" == "--" ]] && shift
case "$cmd" in
  # Plain dotnet build (Godot's --build-solutions can leave build servers holding the pipe open from WSL).
  build) out="$(ps "dotnet build '$APP_W\\FileScape.App.csproj' -nologo -v q -nodeReuse:false /p:UseSharedCompilation=false 2>&1 | Select-String -Pattern 'error|warn|Elapsed'")"
         echo "$out"; if echo "$out" | grep -qE "[1-9][0-9]* Error\(s\)"; then echo "BUILD FAILED" >&2; exit 1; fi ;;
  test) ps "dotnet test '$REPO_W\\tests\\FileScape.Core.Tests' -nologo 2>&1 | Select-Object -Last 15" ;;
  test-wsl) dotnet test "$REPO/tests/FileScape.Core.Tests" -nologo 2>&1 | tail -5 ;;
  run) ARGS=""; for a in "$@"; do ARGS="$ARGS '$a'"; done
       ps "& '$GODOT_CONSOLE' --path '$APP_W' -- $ARGS 2>&1" ;;
  editor) ps "Start-Process '$GODOT_GUI' -ArgumentList '--editor','--path','$APP_W'" ;;
  export) ps "New-Item -ItemType Directory -Force \"\$env:LOCALAPPDATA\\FileScape\\build\" | Out-Null; & '$GODOT_CONSOLE' --headless --path '$APP_W' --export-release 'Windows Desktop' \"\$env:LOCALAPPDATA\\FileScape\\build\\FileScape.exe\" 2>&1" ;;
  *) sed -n '2,10p' "$0"; exit 2 ;;
esac
