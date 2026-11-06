#!/usr/bin/env bash
# FileScape verification tiers (TEST-01, AD-29; see docs/windows-workflow.md "Verification tiers").
#   tools/fs.sh check fast      ~1 min   app build + Core tests (Windows and WSL). Every commit.
#   tools/fs.sh check smoke     ~2 min   fast + a staged export + the short end-to-end journey (walk-explore, UI 1.0).
#   tools/fs.sh check full      ~8 min   smoke + every functional journey (assertions only; measurement segments
#                                        are logged as SKIP) at UI 1.0, and the HUD-sensitive journeys at UI 1.25.
#   tools/fs.sh check release   ~16 min  full, with measurements, plus walk at 1.25, walk-perf, real OS input at 1.0
#                                        and 1.25 (moves the real mouse and takes focus), and the recording.
# Options: --exe=<staged FileScape.exe (Windows path)> reuses a build instead of staging one; --out=<Windows dir>.
# Journeys run one at a time: they share the screen, focus and GPU, and their timing checks must not contend.
# A summary with each step's duration and PASS/FAIL counts is written to <out>\summary.txt and printed.
set -uo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
TIER="${1:-}"; shift || true
case "$TIER" in fast|smoke|full|release) ;; *) sed -n '2,10p' "$0"; exit 2 ;; esac
EXEW=""; OUTW=""
for a in "$@"; do case "$a" in --exe=*) EXEW="${a#--exe=}" ;; --out=*) OUTW="${a#--out=}" ;; esac; done

LAD_W="$(cd /mnt/c && cmd.exe /d /c "echo %LOCALAPPDATA%" 2>/dev/null | tr -d '\r')"
TMP_W="$(cd /mnt/c && cmd.exe /d /c "echo %TEMP%" 2>/dev/null | tr -d '\r')"
STAMP="$(date -u +%Y%m%d-%H%M%S)"
OUTW="${OUTW:-$LAD_W\\FileScape\\evidence\\checks\\$TIER-$STAMP}"
OUT="$(wslpath "$OUTW")"; mkdir -p "$OUT"
SUM="$OUT/summary.txt"; : > "$SUM"
FAILS=0
T0=$(date +%s)
log() { echo "$*" | tee -a "$SUM"; }

# Owned fixtures only (CLAUDE.md): the newest Wayfarer and Atlas Studio trees, and the WSL fixture.
FIX="$(wslpath "$TMP_W")/FileScape-fixtures"
WAYF="$(ls -1d "$FIX"/walk-*/Wayfarer 2>/dev/null | sort | tail -1)"
ATLAS="$(ls -1d "$FIX"/places-*/"Atlas Studio" 2>/dev/null | sort | tail -1)"
if [[ "$TIER" != fast ]] && [[ -z "$WAYF" || -z "$ATLAS" ]]; then
  echo "verify: fixtures missing under $FIX (FileScape.Fixtures.dll create-walk / create-places)" >&2; exit 3
fi
W="$(wslpath -w "$WAYF" 2>/dev/null)"; R="$(wslpath -w "$ATLAS" 2>/dev/null)"
WSLFIX="$(ls -1d /tmp/filescape-wsl-fixtures/wsl-* 2>/dev/null | sort | tail -1)"
if [[ -n "$WSLFIX" ]]; then
  export FILESCAPE_WSL_FIXTURE="\\\\wsl.localhost\\${WSL_DISTRO_NAME:-Ubuntu-24.04}${WSLFIX//\//\\}"
  export WSLENV="FILESCAPE_WSL_FIXTURE/w:${WSLENV:-}"
fi

# step NAME COMMAND...: run, time, and record its exit code.
step() {
  local name="$1"; shift
  local s; s=$(date +%s.%N)
  "$@" > "$OUT/$name.out" 2>&1
  local code=$?
  local d; d=$(printf '%.1f' "$(echo "$(date +%s.%N) - $s" | bc)")
  [[ $code -ne 0 ]] && FAILS=$((FAILS + 1))
  printf '%-28s %7ss  exit %s\n' "$name" "$d" "$code" | tee -a "$SUM"
  return $code
}

# journey NAME LOGNAME TIMEOUT ARGS...: launch the staged build on one probe; count its PASS/FAIL/SKIP lines.
journey() {
  local name="$1" logname="$2" to="$3"; shift 3
  local s; s=$(date +%s.%N)
  timeout "$to" "$EXE" -- "$@" "--out=$OUTW\\$name" --window=1600x900 > "$OUT/$name.out" 2>&1
  local code=$?
  local d; d=$(printf '%.1f' "$(echo "$(date +%s.%N) - $s" | bc)")
  local f="$OUT/$name/$logname"
  local p=0 x=0 k=0
  if [[ -f "$f" ]]; then p=$(grep -c ' PASS ' "$f"); x=$(grep -c ' FAIL ' "$f"); k=$(grep -c ' SKIP ' "$f"); else x=1; fi
  [[ $code -ne 0 || $x -gt 0 ]] && FAILS=$((FAILS + 1))
  printf '%-28s %7ss  exit %-3s %3s PASS %2s FAIL %2s SKIP\n' "$name" "$d" "$code" "$p" "$x" "$k" | tee -a "$SUM"
  [[ -f "$f" ]] && grep ' FAIL ' "$f" | head -5 | sed 's/^/    /' | tee -a "$SUM"
}

log "FileScape check $TIER  $(date -u +%FT%TZ)  commit $(git -C "$REPO" rev-parse --short=12 HEAD)$( [[ -n "$(git -C "$REPO" status --porcelain)" ]] && echo ' (uncommitted changes)')"
log "out: $OUTW"

step build "$REPO/tools/fs.sh" build
step tests-windows "$REPO/tools/fs.sh" test
step tests-wsl "$REPO/tools/fs.sh" test-wsl
grep -hE 'Passed!|Failed!' "$OUT/tests-windows.out" "$OUT/tests-wsl.out" | sed 's/^/    /' | tee -a "$SUM"

if [[ "$TIER" != fast ]]; then
  if [[ -z "$EXEW" ]]; then
    step stage "$REPO/tools/fs.sh" stage
    EXEW="$(grep -oE '[A-Z]:\\[^ ]*staging-[^ ]*\\FileScape.exe' "$OUT/stage.out" | tail -1)"
  fi
  EXE="$(wslpath "$EXEW")"
  [[ -x "$EXE" ]] || { log "verify: no staged exe ($EXEW)"; exit 4; }
  log "build: $(grep '^build ' "$(dirname "$EXE")/build-info.txt" | cut -d' ' -f2-)  ($EXEW)"
  F=(--tier=functional)
  [[ "$TIER" == release ]] && F=()

  journey explore-ui1.0 walk-explore.log 300 --automate=walk-explore "--root=$W" --ui-scale=1 "${F[@]}"
fi

if [[ "$TIER" == full || "$TIER" == release ]]; then
  journey walk-ui1.0 walk.log 420 --automate=walk "--root=$W" --ui-scale=1 "${F[@]}"
  journey places-connected places.log 300 --automate=places "--root=$R" --ui-scale=1 "${F[@]}"
  journey places-tree places.log 300 --automate=places "--root=$R" --scan=tree --ui-scale=1 --tier=functional
  journey places-scan places-scan.log 300 --automate=places-scan --synthetic=50000 --synthetic-rate=10000 --ui-scale=1
  journey navigation navigation.log 300 --automate=navigation --ui-scale=1
  journey map-journey journey.log 400 --automate=journey --ui-scale=1
  journey planetoids planetoids.log 200 --experiment=planetoids --automate=planetoids --ui-scale=1
  # UI scaling changes the HUD, signs, drawer and picking geometry: those journeys repeat at 1.25.
  journey explore-ui1.25 walk-explore.log 300 --automate=walk-explore "--root=$W" --ui-scale=1.25 --tier=functional
  journey places-ui1.25 places.log 300 --automate=places "--root=$R" --ui-scale=1.25 --tier=functional
fi

if [[ "$TIER" == release ]]; then
  journey walk-ui1.25 walk.log 420 --automate=walk "--root=$W" --ui-scale=1.25 --tier=functional
  journey walk-perf walk-perf.log 200 --automate=walk-perf "--root=$W" --ui-scale=1
  grep -h 'perf ' "$OUT/walk-perf/walk-perf.log" 2>/dev/null | sed 's/^/    /' | tee -a "$SUM"
  TOOLS_W="$OUTW\\tools"; mkdir -p "$OUT/tools"; cp "$REPO"/tools/os-walk-probe.ps1 "$OUT/tools/"
  for sc in 1 1.25; do
    s=$(date +%s)
    timeout 360 powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$TOOLS_W\\os-walk-probe.ps1" -Exe "$EXEW" -Root "$W" -Out "$OUTW\\os-ui$sc" -UiScale $sc -ChainRoot "$W\\Journeys\\Coast\\Harbour\\Pier" > "$OUT/os-ui$sc.out" 2>&1
    f="$OUT/os-ui$sc/os-walk-results.txt"; p=$(grep -c PASS "$f" 2>/dev/null); x=$(grep -c FAIL "$f" 2>/dev/null || echo 1)
    [[ "$x" != 0 ]] && FAILS=$((FAILS + 1))
    printf '%-28s %7ss  real OS input %3s PASS %2s FAIL\n' "os-ui$sc" "$(( $(date +%s) - s ))" "$p" "$x" | tee -a "$SUM"
  done
  s=$(date +%s)
  timeout 400 "$EXE" --write-movie "$OUTW\\explore-recording.avi" --fixed-fps 30 -- --automate=walk-explore "--root=$W" "--out=$OUTW\\recording" --window=1280x720 --ui-scale=1 > "$OUT/recording.out" 2>&1
  printf '%-28s %7ss  %s\n' recording "$(( $(date +%s) - s ))" "$(ls -la "$OUT/explore-recording.avi" 2>/dev/null | awk '{print $5" bytes"}')" | tee -a "$SUM"
fi

log "$(printf '%-28s %7ss' TOTAL "$(( $(date +%s) - T0 ))")  failing steps: $FAILS"
[[ -d /mnt/c/Users/David/AppData/Roaming/FileScape ]] && log "note: %APPDATA%\\FileScape exists (DJ's preferences; probes never read or write it)"
exit $(( FAILS > 0 ? 1 : 0 ))
