#!/usr/bin/env bash
# FileScape verification tiers (TEST-01, AD-29; see docs/windows-workflow.md "Verification tiers").
#   tools/fs.sh check harness   ~3 s     the harness's own judging, with test doubles (tools/verify-selftest.sh).
#   tools/fs.sh check fast      ~1 min   app build + Core tests (Windows and WSL). Every commit.
#   tools/fs.sh check smoke     ~3 min   fast + a staged export + the short end-to-end journeys (walk-explore, and
#                                        hub → project → README/editor), UI 1.0.
#   tools/fs.sh check full      ~8 min   smoke + every functional journey (assertions only; measurement segments
#                                        are logged as SKIP) at UI 1.0, and the HUD-sensitive journeys at UI 1.25.
#   tools/fs.sh check release   ~16 min  full, with measurements, plus walk at 1.25, walk-perf, real OS input at 1.0
#                                        and 1.25 (moves the real mouse and takes focus), and the recording.
# Options: --exe=<staged FileScape.exe (Windows path)> reuses a build instead of staging one; --out=<Windows dir>.
# Journeys run one at a time: they share the screen, focus and GPU, and their timing checks must not contend.
# A summary with each step's duration and PASS/FAIL counts is written to <out>\summary.txt and printed. A step fails on
# a non-zero exit or timeout, a missing/empty result file or one without assertions, or any FAIL (tools/verify-lib.sh).
set -uo pipefail
main() {
REPO="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=tools/verify-lib.sh
source "$REPO/tools/verify-lib.sh"
TIER="${1:-}"; shift || true
case "$TIER" in fast|smoke|full|release) ;; *) sed -n '2,13p' "$0"; exit 2 ;; esac
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

# journey NAME LOGNAME TIMEOUT ARGS...: launch the staged build on one probe; judged by tools/verify-lib.sh (exit code
# or timeout, and a non-empty result file with assertions and no FAIL).
journey() {
  local name="$1" logname="$2" to="$3"; shift 3
  probe_step "$name" "$to" "$OUT/$name/$logname" "journey" "$EXE" -- "$@" "--out=$OUTW\\$name" --window=1600x900
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
  journey project-ui1.0 walk-project.log 300 --automate=walk-project --root=hub "--project-root=$W\\Projects" --ui-scale=1 "${F[@]}"
  # The planetary world (PLANET-10..16): the first minutes on the Starter Sky (the app's own sample of real files),
  # opening recorded by the test shell (nothing starts).
  journey planets-ui1.0 planets-journey.log 300 --automate=planets-journey --root=sample --vis=planets --test-shell=record --ui-scale=1 "${F[@]}"
  # 2026-11-11 (DJ's playtest): launcher discovery on a crowded world, labels during a full camera turn, overview travel.
  journey planets-flows planets-flows.log 300 --automate=planets-flows --root=sample --vis=planets --test-shell=record --ui-scale=1 "${F[@]}"
  # 2026-11-14: world characters, the repository workbench (jump, spin, E) and the depth bands on the Starter Sky.
  journey planets-route planets-route.log 300 --automate=planets-route --root=sample --vis=planets --test-shell=record --ui-scale=1 "${F[@]}"
  # 2026-11-15 (the first public playtest): the walkthrough, played through, and the black-hole world (safe route, a fall, re-entry).
  journey planets-tutorial planets-tutorial.log 300 --automate=planets-tutorial --root=sample --vis=planets --test-shell=record --ui-scale=1 "${F[@]}"
  # The walkthrough's target cues (PLANET-38): beacon on screen, edge arrow when behind, horizon, approach, Leave.
  journey planets-tutorial-cues planets-tutorial-cues.log 300 --automate=planets-tutorial-cues --root=sample --vis=planets --test-shell=record --ui-scale=1 "${F[@]}"
  journey planets-hazard planets-hazard.log 300 --automate=planets-hazard --root=sample --vis=planets --test-shell=record --ui-scale=1 "${F[@]}"
  # 2026-11-16 (PLANET-47): the sound button and N; travel, inspect, the warning and a fall measured silent while muted.
  journey planets-sound planets-sound.log 300 --automate=planets-sound --root=sample --vis=planets --test-shell=record --ui-scale=1.25 "${F[@]}"
fi

if [[ "$TIER" == full || "$TIER" == release ]]; then
  journey walk-ui1.0 walk.log 420 --automate=walk "--root=$W" --ui-scale=1 "${F[@]}"
  journey places-connected places.log 300 --automate=places "--root=$R" --ui-scale=1 "${F[@]}"
  journey places-tree places.log 300 --automate=places "--root=$R" --scan=tree --ui-scale=1 --tier=functional
  journey places-scan places-scan.log 300 --automate=places-scan --synthetic=50000 --synthetic-rate=10000 --ui-scale=1
  journey navigation navigation.log 300 --automate=navigation --ui-scale=1
  journey map-journey journey.log 400 --automate=journey --ui-scale=1
  journey planetoids planetoids.log 200 --experiment=planetoids --automate=planetoids --ui-scale=1
  # AERIAL-07: a deliberately small aerial budget, the current place outside breadth-first (kept), then beyond it (recovery).
  journey aerial-limit-24 walk-aerial-limit.log 200 --automate=walk-aerial-limit "--root=$W" --test-max-islands=24 --ui-scale=1
  journey aerial-limit-8 walk-aerial-limit.log 200 --automate=walk-aerial-limit "--root=$W" --test-max-islands=8 --ui-scale=1
  # UI scaling changes the HUD, signs, drawer and picking geometry: those journeys repeat at 1.25.
  journey explore-ui1.25 walk-explore.log 300 --automate=walk-explore "--root=$W" --ui-scale=1.25 --tier=functional
  journey places-ui1.25 places.log 300 --automate=places "--root=$R" --ui-scale=1.25 --tier=functional
  # The same planetary system on the bounded real fixture: links, an empty room, and a WSL folder.
  journey planets-fixture planets-fixture.log 300 --automate=planets-fixture "--root=$W" --vis=planets --test-shell=record --ui-scale=1 "--wsl-root=${FILESCAPE_WSL_FIXTURE:-}" --tier=functional
  # Finding "home" on the WSL distribution's root from the arrival (lists only the root's own entries; never enters home).
  journey planets-discover-wsl-root planets-discover.log 200 --automate=planets-discover "--root=\\\\wsl.localhost\\${WSL_DISTRO_NAME:-Ubuntu-24.04}\\" --find=home --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  journey planets-ui1.25 planets-journey.log 300 --automate=planets-journey --root=sample --vis=planets --test-shell=record --ui-scale=1.25 --tier=functional
  # This computer -> the WSL root -> the WSL fixture's real repository (workbench) -> a child folder -> home (never C:\).
  # PLANET-42..44: file actions on the workbench, world icons and pads (double-click opens through the deliberate Open,
  # recorded; a faked refusal is reported); the arrival framing in motion; reading a long real README (needs
  # FILESCAPE_WSL_README, the path tools/make-wsl-fixture.sh --readme prints; without it that step is left out).
  journey planets-actions planets-actions.log 300 --automate=planets-actions --root=sample --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  journey planets-arrival planets-arrival.log 300 --automate=planets-arrival --root=sample --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  if [[ -n "${FILESCAPE_WSL_README:-}" ]]; then journey planets-reading planets-reading.log 200 --automate=planets-reading "--root=$FILESCAPE_WSL_README" --vis=planets --test-shell=record --ui-scale=1 --tier=functional; fi
  journey planets-route-hub planets-route-hub.log 300 --automate=planets-route-hub --root=hub --vis=planets --test-shell=record --ui-scale=1 "--repo=${FILESCAPE_WSL_FIXTURE:+$FILESCAPE_WSL_FIXTURE\\project}" --tier=functional
  # PLANET-45: the root landmarks (solid; E: terminal here, the path's forms, places) on fixture folders whose names have
  # spaces, shown with a root's look (never C:\ itself); the WSL panel's "/" goes to the distribution's real root.
  for d in "$FIX/dir with spaces" "/tmp/filescape-wsl-fixtures/dir with spaces"; do
    mkdir -p "$d/src" "$d/docs"; [[ -f "$d/README.md" ]] || echo "# A folder with spaces" > "$d/README.md"; [[ -f "$d/notes.txt" ]] || echo notes > "$d/notes.txt"
  done
  journey planets-landmarks planets-landmarks.log 300 --automate=planets-landmarks "--root=$(wslpath -w "$FIX/dir with spaces")" "--wsl-root=\\\\wsl.localhost\\${WSL_DISTRO_NAME:-Ubuntu-24.04}\\tmp\\filescape-wsl-fixtures\\dir with spaces" --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  # 2026-11-16 (PLANET-39..41): the Destinations tree, trips hop by hop and their Stop, Locations, favourites; one folder
  # held "still listing" by the delay instrument; then the fixture's inaccessible folder as the list shows it.
  journey planets-navigation planets-navigation.log 300 --automate=planets-navigation --root=sample --vis=planets --test-shell=record --ui-scale=1 --test-list-delay-ms=12000 "--test-list-delay-match=Pressed Flowers" --tier=functional
  journey planets-navigation-denied planets-navigation-denied.log 200 --automate=planets-navigation-denied "--root=$R" --vis=planets --test-shell=record --ui-scale=1 --tier=functional
fi

if [[ "$TIER" == release ]]; then
  journey walk-ui1.25 walk.log 420 --automate=walk "--root=$W" --ui-scale=1.25 --tier=functional
  journey walk-perf walk-perf.log 200 --automate=walk-perf "--root=$W" --ui-scale=1
  grep -h 'perf ' "$OUT/walk-perf/walk-perf.log" 2>/dev/null | sed 's/^/    /' | tee -a "$SUM"
  TOOLS_W="$OUTW\\tools"; mkdir -p "$OUT/tools"; cp "$REPO"/tools/os-walk-probe.ps1 "$REPO"/tools/os-input-probe.ps1 "$REPO"/tools/os-righthold-repeat.ps1 "$REPO"/tools/os-planets-probe.ps1 "$OUT/tools/"
  for sc in 1 1.25; do
    probe_step "os-ui$sc" 360 "$OUT/os-ui$sc/os-walk-results.txt" "real OS input" \
      powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$TOOLS_W\\os-walk-probe.ps1" -Exe "$EXEW" -Root "$W" -Out "$OUTW\\os-ui$sc" -UiScale $sc -ChainRoot "$W\\Journeys\\Coast\\Harbour\\Pier"
  done
  # Planets with real input (2026-11-15): controls (drags, clicks, focus), the sample route, and a person's first launch
  # with the walkthrough on a clean settings profile. Input goes only to the FileScape these probes start.
  for mode in Controls Route Tutorial Hazard; do
    a=(); [[ $mode != Controls ]] && a=("-$mode")
    probe_step "os-planets-$mode" 600 "$OUT/os-planets-$mode/os-planets.log" "real OS input" \
      powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$TOOLS_W\\os-planets-probe.ps1" -Exe "$EXEW" -Out "$OUTW\\os-planets-$mode" "${a[@]}"
  done
  recording_step planets-recording 400 "$OUT/planets-recording.avi" \
    "$EXE" --write-movie "$OUTW\\planets-recording.avi" --fixed-fps 30 -- --automate=planets-reel --root=sample --vis=planets --test-shell=record "--out=$OUTW\\planets-recording" --window=1280x720 --ui-scale=1 --tier=functional
  recording_step recording 400 "$OUT/explore-recording.avi" \
    "$EXE" --write-movie "$OUTW\\explore-recording.avi" --fixed-fps 30 -- --automate=walk-explore "--root=$W" "--out=$OUTW\\recording" --window=1280x720 --ui-scale=1
fi

log "$(printf '%-28s %7ss' TOTAL "$(( $(date +%s) - T0 ))")  failing steps: $FAILS"
APPDATA_W="$(cd /mnt/c && cmd.exe /d /c "echo %APPDATA%" 2>/dev/null | tr -d '\r')"
[[ -n "$APPDATA_W" && -d "$(wslpath "$APPDATA_W")/FileScape" ]] && log "note: %APPDATA%\\FileScape exists (your preferences; probes never read or write it)"
exit $(( FAILS > 0 ? 1 : 0 ))
}
# Bash reads a script as it runs; wrapping the body in main() means an edit during a run cannot change the run.
main "$@"