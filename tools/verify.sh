#!/usr/bin/env bash
# FileScape verification tiers (TEST-01, AD-29; see docs/windows-workflow.md "Verification tiers").
#   tools/fs.sh check harness   ~3 s     the harness's own judging, with test doubles (tools/verify-selftest.sh).
#   tools/fs.sh check fast      ~1.5 min app build + Core tests (Windows and WSL side by side). Every commit.
#   tools/fs.sh check smoke     ~5 min   fast + a staged export (reused for the same source state) + the short end-to-end
#                                        journeys (walk-explore, hub → project → README/editor, the planets basics).
#   tools/fs.sh check full      ~30 min  smoke + every functional journey (assertions only; measurement segments
#                                        are logged as SKIP) at UI 1.0, and the HUD-sensitive journeys at UI 1.25.
#   tools/fs.sh check release   ~40 min  full, with measurements, plus walk at 1.25, walk-perf, real OS input at 1.0
#                                        and 1.25 (moves the real mouse and takes focus), and the recording.
#   tools/fs.sh check journeys [--measure] NAME...   only the named steps (shell patterns, e.g. 'planets-nav*'), with a
#                                        staged build (reused) and no Core tests; real-OS-input steps (os-*) only when
#                                        named exactly. --measure keeps measurement segments; --list prints the names.
# Options: --exe=<staged FileScape.exe (Windows path)> reuses a build instead of staging one; --fresh-stage always
# exports a new one; --out=<Windows dir>. FS_LAUNCH_LOCK=/path takes that flock for each app launch only (shared desktop).
# Journeys run one at a time: they share the screen, focus and GPU, and their timing checks must not contend.
# Every run ends with a timing table (each step's result and seconds, and the total), also on a failure, a timeout, an
# early exit or Ctrl+C; it is printed last and written to <out>\timing.txt (TEST-05, AD-80). A step fails on a non-zero
# exit or timeout, a missing/empty result file or one without assertions, or any FAIL (tools/verify-lib.sh).
set -uo pipefail
main() {
REPO="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=tools/verify-lib.sh
source "$REPO/tools/verify-lib.sh"
TIER="${1:-}"; shift || true
case "$TIER" in fast|smoke|full|release|journeys) ;; *) sed -n '2,19p' "$0"; exit 2 ;; esac
EXEW=""; OUTW=""; FRESH=0; MEASURE=0; LIST=0; PATTERNS=()
for a in "$@"; do
  case "$a" in
    --exe=*) EXEW="${a#--exe=}" ;; --out=*) OUTW="${a#--out=}" ;; --fresh-stage) FRESH=1 ;;
    --measure) MEASURE=1 ;; --list) LIST=1 ;;
    -*) echo "verify: unknown option $a" >&2; exit 2 ;;
    *) PATTERNS+=("$a") ;;
  esac
done
# What runs: the journeys mode walks the release tier's steps and keeps only the named ones (step_wanted).
LEVEL="$TIER"; [[ "$TIER" == journeys ]] && LEVEL=release
[[ $LIST == 1 && "$TIER" != journeys ]] && { echo "verify: --list goes with the journeys mode" >&2; exit 2; }
if [[ "$TIER" == journeys ]]; then
  [[ $LIST == 1 || ${#PATTERNS[@]} -gt 0 ]] || { echo "usage: fs.sh check journeys [--measure] [--exe=...] NAME... | --list" >&2; exit 2; }
  declare -gA MATCHED=()
  # step_wanted NAME: a shell pattern matches NAME; a real-OS-input step (os-*: moves the mouse, takes focus) only by its
  # exact name. --list prints every step instead of running any.
  step_wanted() {
    local p
    if [[ $LIST == 1 ]]; then echo "$1"; return 1; fi
    for p in "${PATTERNS[@]}"; do
      if [[ "$1" == os-* ]]; then [[ "$1" == "$p" ]] || continue; else [[ "$1" == $p ]] || continue; fi
      MATCHED[$p]=1; return 0
    done
    return 1
  }
fi

LAD_W="$(cd /mnt/c && cmd.exe /d /c "echo %LOCALAPPDATA%" 2>/dev/null | tr -d '\r')"
TMP_W="$(cd /mnt/c && cmd.exe /d /c "echo %TEMP%" 2>/dev/null | tr -d '\r')"
STAMP="$(date -u +%Y%m%d-%H%M%S)"
if [[ $LIST == 1 ]]; then OUT="$(mktemp -d)"; OUTW="(list only)"
else
  OUTW="${OUTW:-$LAD_W\\FileScape\\evidence\\checks\\$TIER-$STAMP}"
  OUT="$(wslpath "$OUTW")"; mkdir -p "$OUT"
fi
SUM="$OUT/summary.txt"; : > "$SUM"
FAILS=0
T0=$(date +%s.%N)
EXIT_NOTE=""
log() { echo "$*" | tee -a "$SUM"; }

# The timing table (TEST-05) comes from the EXIT trap, so it is printed on every way out: the end, an early exit, a
# failure, a timeout (the step's row says so) and Ctrl+C or a kill (the step in flight is stopped and marked interrupted).
declare -gA BG_PID=() BG_START=()
finish() {
  local code=$?
  trap - EXIT INT TERM
  [[ $LIST == 1 ]] && { rm -rf "$OUT"; exit "$code"; }
  interrupt_current
  local n
  for n in "${!BG_PID[@]}"; do
    pkill -TERM -P "${BG_PID[$n]}" 2>/dev/null; kill -TERM "${BG_PID[$n]}" 2>/dev/null; wait "${BG_PID[$n]}" 2>/dev/null
    timing_record "$n" interrupted "$(secs_since "${BG_START[$n]}")" "ran in the background"
  done
  if [[ "$TIER" == journeys && $LIST == 0 && $code -le 1 ]]; then
    local p; for p in "${PATTERNS[@]}"; do [[ -n "${MATCHED[$p]:-}" ]] || { EXIT_NOTE+="${EXIT_NOTE:+; }no step matched '$p'"; code=2; }; done
  fi
  {
    echo
    timing_table "check $TIER, exit $code, failing steps $FAILS${EXIT_NOTE:+; $EXIT_NOTE}" "$(secs_since "$T0")"
  } | tee "$OUT/timing.txt" | tee -a "$SUM"
  echo "timing: $OUTW\\timing.txt   summary: $OUTW\\summary.txt"
  exit "$code"
}
trap finish EXIT
trap 'EXIT_NOTE="interrupted (SIGINT)"; exit 130' INT
trap 'EXIT_NOTE="interrupted (SIGTERM)"; exit 143' TERM

# Owned fixtures only (CLAUDE.md): the newest Wayfarer and Atlas Studio trees, and the WSL fixture.
FIX="$(wslpath "$TMP_W")/FileScape-fixtures"
WAYF="$(ls -1d "$FIX"/walk-*/Wayfarer 2>/dev/null | sort | tail -1)"
ATLAS="$(ls -1d "$FIX"/places-*/"Atlas Studio" 2>/dev/null | sort | tail -1)"
if [[ "$TIER" != fast && $LIST == 0 ]] && [[ -z "$WAYF" || -z "$ATLAS" ]]; then
  EXIT_NOTE="fixtures missing under $FIX (FileScape.Fixtures.dll create-walk / create-places)"; echo "verify: $EXIT_NOTE" >&2; exit 3
fi
W="$(wslpath -w "$WAYF" 2>/dev/null)"; R="$(wslpath -w "$ATLAS" 2>/dev/null)"
WSLFIX="$(ls -1d /tmp/filescape-wsl-fixtures/wsl-* 2>/dev/null | sort | tail -1)"
if [[ -n "$WSLFIX" ]]; then
  export FILESCAPE_WSL_FIXTURE="\\\\wsl.localhost\\${WSL_DISTRO_NAME:-Ubuntu-24.04}${WSLFIX//\//\\}"
  export WSLENV="FILESCAPE_WSL_FIXTURE/w:${WSLENV:-}"
fi

# Upper bounds for the steps that are not journeys, seconds: a hung build or export (once ~10 min, quirk 2 in
# docs/windows-workflow.md) ends as a timeout row instead of waiting for ever. Far above the usual 2 s / 90 s / 30 s.
BUILD_TIMEOUT_S=600 TESTS_TIMEOUT_S=1200 STAGE_TIMEOUT_S=1800

# step_done NAME CODE SECONDS [NOTE]: prints and records a step that is not a journey.
step_done() {
  local name="$1" code="$2" secs="$3" note="${4:-}"
  [[ $code -ne 0 ]] && FAILS=$((FAILS + 1))
  printf '%-28s %7ss  exit %s%s\n' "$name" "$secs" "$code" "${note:+  ($note)}" | tee -a "$SUM"
  timing_record "$name" "$(result_word "$code" "$( [[ $code -eq 0 ]] && echo ok)")" "$secs" "$note"
}

# step NAME TIMEOUT_S COMMAND...: run, time, and record its exit code.
step() {
  local name="$1" to="$2"; shift 2
  run_cmd "$name" "$to" 0 "$OUT/$name.out" "$@"
  step_done "$name" "$RUN_CODE" "$RUN_SECS" "${STEP_NOTE:-}"
  return "$RUN_CODE"
}

# bg_step NAME TIMEOUT_S COMMAND...: starts a step in the background (only one that launches no window and has no
# timing checks of its own that the steps beside it could disturb: the WSL Core tests); bg_wait NAME records it.
bg_step() {
  local name="$1" to="$2"; shift 2
  BG_START[$name]=$(date +%s.%N)
  ( s=$(date +%s.%N); timeout "$to" "$@" > "$OUT/$name.out" 2>&1; c=$?; echo "$c $(secs_since "$s")" > "$OUT/$name.code" ) &
  BG_PID[$name]=$!
  printf '%-28s %8s  started in the background\n' "$name" "" | tee -a "$SUM"
}
bg_wait() {
  local name="$1" code secs
  [[ -n "${BG_PID[$name]:-}" ]] || return 0
  wait "${BG_PID[$name]}"; unset "BG_PID[$name]"
  read -r code secs < "$OUT/$name.code" 2>/dev/null || { code=1; secs="$(secs_since "${BG_START[$name]}")"; }
  step_done "$name" "$code" "$secs" "$(test_counts "$OUT/$name.out")${BG_NOTE:+; $BG_NOTE}"
}

# test_counts FILE: "734 passed, 0 failed, 26 skipped of 760" from fs.sh test's combined line (or dotnet's own).
test_counts() {
  local l; l="$(grep -E '^Core tests .*(Passed|Failed)!' "$1" 2>/dev/null | tail -1)"
  [[ -n "$l" ]] || l="$(grep -E '(Passed|Failed)!' "$1" 2>/dev/null | tail -1)"
  [[ -n "$l" ]] || return 0
  local n; for n in Passed Failed Skipped Total; do eval "local $n=$(grep -oE "$n: *[0-9]+" <<<"$l" | grep -oE '[0-9]+$' || echo '?')"; done
  echo "$Passed passed, $Failed failed, $Skipped skipped of $Total"
}

# journey NAME LOGNAME TIMEOUT ARGS...: launch the staged build on one probe; judged by tools/verify-lib.sh (exit code
# or timeout, and a non-empty result file with assertions and no FAIL).
journey() {
  local name="$1" logname="$2" to="$3"; shift 3
  probe_step "$name" "$to" "$OUT/$name/$logname" "journey" "$EXE" -- "$@" "--out=$OUTW\\$name" --window=1600x900
}

[[ $LIST == 1 ]] || log "FileScape check $TIER${PATTERNS[*]:+ ${PATTERNS[*]}}  $(date -u +%FT%TZ)  commit $(git -C "$REPO" rev-parse --short=12 HEAD)$( [[ -n "$(git -C "$REPO" status --porcelain)" ]] && echo ' (uncommitted changes)')${FS_LAUNCH_LOCK:+  launch lock $FS_LAUNCH_LOCK}"
[[ $LIST == 1 ]] || log "out: $OUTW"

if [[ "$TIER" != journeys ]]; then
  step build "$BUILD_TIMEOUT_S" "$REPO/tools/fs.sh" build
  run_cmd tests-windows "$TESTS_TIMEOUT_S" 0 "$OUT/tests-windows.out" "$REPO/tools/fs.sh" test
  step_done tests-windows "$RUN_CODE" "$RUN_SECS" "$(test_counts "$OUT/tests-windows.out")"
  run_cmd tests-wsl "$TESTS_TIMEOUT_S" 0 "$OUT/tests-wsl.out" "$REPO/tools/fs.sh" test-wsl
  step_done tests-wsl "$RUN_CODE" "$RUN_SECS" "$(test_counts "$OUT/tests-wsl.out")"
fi

if [[ "$LEVEL" != fast ]]; then
  if [[ $LIST == 1 ]]; then EXE=/nonexistent
  else
    if [[ -z "$EXEW" ]]; then
      STEP_NOTE=""; reuse=()
      run_cmd stage "$STAGE_TIMEOUT_S" 0 "$OUT/stage.out" "$REPO/tools/fs.sh" stage "${reuse[@]}"
      grep -q 'stage reused' "$OUT/stage.out" && STEP_NOTE="reused $(grep -oE 'staging-[0-9-]+' "$OUT/stage.out" | tail -1)"
      step_done stage "$RUN_CODE" "$RUN_SECS" "$STEP_NOTE"
      EXEW="$(grep -oE '[A-Z]:\\[^ ]*staging-[^ ]*\\FileScape.exe' "$OUT/stage.out" | tail -1)"
    fi
    EXE="$(wslpath "$EXEW" 2>/dev/null)"
    [[ -n "$EXEW" && -x "$EXE" ]] || { EXIT_NOTE="no staged exe ($EXEW)"; log "verify: $EXIT_NOTE"; exit 4; }
    log "build: $(grep '^build ' "$(dirname "$EXE")/build-info.txt" | cut -d' ' -f2-)  ($EXEW)"
  fi
  BG_NOTE="ran beside build, tests-windows and stage"; bg_wait tests-wsl
  F=(--tier=functional)
  [[ "$LEVEL" == release && ( "$TIER" == release || $MEASURE == 1 ) ]] && F=()

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
  # 2026-11-17 (FOLLOW-1): the overview from the routes into the sample draws worlds you can see (DJ saw it blank).
  journey planets-overview-sample planets-overview-routes.log 300 --automate=planets-overview-routes --root=sample --vis=planets --test-shell=record --ui-scale=1 "${F[@]}"
  journey planets-overview-hub planets-overview-routes.log 300 --automate=planets-overview-routes --root=hub --vis=planets --test-shell=record --ui-scale=1 "${F[@]}"
  # FOLLOW-2: no audio gaps at startup, in travel, or while the main loop stalls (the Windows move/resize loop).
  journey planets-audio-gaps planets-audio-gaps.log 300 --automate=planets-audio-gaps --root=sample --vis=planets --test-shell=record --ui-scale=1 "${F[@]}"
  journey planets-hazard planets-hazard.log 300 --automate=planets-hazard --root=sample --vis=planets --test-shell=record --ui-scale=1 "${F[@]}"
  # 2026-11-16 (PLANET-47): the sound button and N; travel, inspect, the warning and a fall measured silent while muted.
  journey planets-sound planets-sound.log 300 --automate=planets-sound --root=sample --vis=planets --test-shell=record --ui-scale=1.25 "${F[@]}"
fi

if [[ "$LEVEL" == full || "$LEVEL" == release ]]; then
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
  # PLANET-76..78: the overview discovers by itself (deep fixture, treated as a broad place so only discovery lists it):
  # levels at 0/10/30 s, no jumps, select/zoom/pause/land/return during discovery. FileScape.Fixtures create-deep makes it.
  DEEP="$(ls -1d "$FIX"/deep-*/"Deep Atlas" 2>/dev/null | sort | tail -1)"
  if [[ -n "$DEEP" ]]; then
    journey planets-overview-discovery planets-overview-discovery.log 300 --automate=planets-overview-discovery "--root=$(wslpath -w "$DEEP")" --test-broad --vis=planets --test-shell=record --ui-scale=1 "${F[@]}"
  else
    skip_step planets-overview-discovery "no deep fixture under $FIX (FileScape.Fixtures create-deep)"
  fi
  # Finding "home" on the WSL distribution's root from the arrival (lists only the root's own entries; never enters home).
  journey planets-discover-wsl-root planets-discover.log 200 --automate=planets-discover "--root=\\\\wsl.localhost\\${WSL_DISTRO_NAME:-Ubuntu-24.04}\\" --find=home --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  # PLANET-58: a near and a far launcher lined up on a crowded world (a fixture shaped like a distribution's root, made
  # if missing, and Constellations on the Starter Sky): both named, attached, the near one larger, both clickable.
  ROOTLIKE="$(ls -1d /tmp/filescape-wsl-fixtures/rootlike-* 2>/dev/null | sort | tail -1)"
  [[ -z "$ROOTLIKE" && $LIST == 0 ]] && "$REPO/tools/make-wsl-fixture.sh" --root-like >/dev/null && ROOTLIKE="$(ls -1d /tmp/filescape-wsl-fixtures/rootlike-* 2>/dev/null | sort | tail -1)"
  journey planets-near-far-root planets-near-far.log 200 --automate=planets-near-far "--root=\\\\wsl.localhost\\${WSL_DISTRO_NAME:-Ubuntu-24.04}${ROOTLIKE//\//\\}" --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  journey planets-near-far-sample planets-near-far.log 250 --automate=planets-near-far --root=sample --world=Constellations --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  # PLANET-59: ordinary worlds' relief on a small and a large Starter Sky world: pads and landings level, feet on the
  # ground every frame of a walk up the highest nearby rise.
  journey planets-relief planets-relief.log 250 --automate=planets-relief --root=sample --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  # PLANET-60: the sky's slow turn (far below camera drift standing still) and a shooting star standing and in flight;
  # none just after a landing or in the overview.
  journey planets-sky planets-sky.log 200 --automate=planets-sky --root=sample --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  # PLANET-67/68: vertical look (drags up and down) on arrival, after and while walking, the overlook view, after a
  # landing, a Hazard World and the overview; every step of a sweep tilts the view; the wheel zooms during the arrival.
  journey planets-look planets-look.log 250 --automate=planets-look --root=sample --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  journey planets-ui1.25 planets-journey.log 300 --automate=planets-journey --root=sample --vis=planets --test-shell=record --ui-scale=1.25 --tier=functional
  # This computer -> the WSL root -> the WSL fixture's real repository (workbench) -> a child folder -> home (never C:\).
  # PLANET-42..44: file actions on the workbench, world icons and pads (double-click opens through the deliberate Open,
  # recorded; a faked refusal is reported); the arrival framing in motion; reading a long real README (needs
  # FILESCAPE_WSL_README, the path tools/make-wsl-fixture.sh --readme prints; without it that step is left out).
  journey planets-actions planets-actions.log 300 --automate=planets-actions --root=sample --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  journey planets-arrival planets-arrival.log 300 --automate=planets-arrival --root=sample --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  if [[ -n "${FILESCAPE_WSL_README:-}" ]]; then journey planets-reading planets-reading.log 200 --automate=planets-reading "--root=$FILESCAPE_WSL_README" --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  else skip_step planets-reading "FILESCAPE_WSL_README not set (tools/make-wsl-fixture.sh --readme)"; fi
  journey planets-route-hub planets-route-hub.log 300 --automate=planets-route-hub --root=hub --vis=planets --test-shell=record --ui-scale=1 "--repo=${FILESCAPE_WSL_FIXTURE:+$FILESCAPE_WSL_FIXTURE\\project}" --tier=functional
  # PLANET-45: the root landmarks (solid; E: terminal here, the path's forms, places) on fixture folders whose names have
  # spaces, shown with a root's look (never C:\ itself); the WSL panel's "/" goes to the distribution's real root.
  [[ $LIST == 1 ]] || for d in "$FIX/dir with spaces" "/tmp/filescape-wsl-fixtures/dir with spaces"; do
    mkdir -p "$d/src" "$d/docs"; [[ -f "$d/README.md" ]] || echo "# A folder with spaces" > "$d/README.md"; [[ -f "$d/notes.txt" ]] || echo notes > "$d/notes.txt"
  done
  journey planets-landmarks planets-landmarks.log 300 --automate=planets-landmarks "--root=$(wslpath -w "$FIX/dir with spaces")" "--wsl-root=\\\\wsl.localhost\\${WSL_DISTRO_NAME:-Ubuntu-24.04}\\tmp\\filescape-wsl-fixtures\\dir with spaces" --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  # PLANET-55/56: the telescope's cue on the walk up and E there; the bell tower walked onto and under, rung by a twirl
  # underneath (not by a jump, not beside it), swinging, opening nothing.
  journey planets-lookouts planets-lookouts.log 300 --automate=planets-lookouts --root=sample --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  # 2026-11-16 (PLANET-39..41): the Destinations tree, trips hop by hop and their Stop, Locations, favourites; one folder
  # held "still listing" by the delay instrument; then the fixture's inaccessible folder as the list shows it.
  journey planets-navigation planets-navigation.log 300 --automate=planets-navigation --root=sample --vis=planets --test-shell=record --ui-scale=1 --test-list-delay-ms=12000 "--test-list-delay-match=Pressed Flowers" --tier=functional
  journey planets-navigation-denied planets-navigation-denied.log 200 --automate=planets-navigation-denied "--root=$R" --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  # 2026-11-17 (PLANET-51..53): typing in Destinations never reaches a shortcut, the keyboard route through its results;
  # the reader at both sizes (wheel, keys, Expand, Enter/Open, links). The reader needs FILESCAPE_WSL_README (from
  # tools/make-wsl-fixture.sh --readme, which now also writes guide.md); without it that step is left out.
  journey planets-destinations-keys planets-destinations-keys.log 300 --automate=planets-destinations-keys --root=sample --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  # 2026-11-19 (PLANET-69/70, AD-68): every keyboard panel (Destinations, Locations, Open location, Settings, the reader),
  # opened by shortcut and toolbar, typed into key by key at once and from its results, after travel and in the overview.
  journey planets-dialog-keys planets-dialog-keys.log 420 --automate=planets-dialog-keys --root=sample --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  if [[ -n "${FILESCAPE_WSL_README:-}" ]]; then journey planets-reader planets-reader.log 200 --automate=planets-reader "--root=$FILESCAPE_WSL_README" --vis=planets --test-shell=record --ui-scale=1 --tier=functional
  else skip_step planets-reader "FILESCAPE_WSL_README not set (tools/make-wsl-fixture.sh --readme)"; fi
fi

if [[ "$LEVEL" == release ]]; then
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

BG_NOTE="ran beside build and tests-windows"; bg_wait tests-wsl   # the fast tier (the others waited before the journeys)
APPDATA_W="$(cd /mnt/c && cmd.exe /d /c "echo %APPDATA%" 2>/dev/null | tr -d '\r')"
[[ $LIST == 0 && -n "$APPDATA_W" && -d "$(wslpath "$APPDATA_W")/FileScape" ]] && log "note: %APPDATA%\\FileScape exists (your preferences; probes never read or write it)"
exit $(( FAILS > 0 ? 1 : 0 ))
}
# Bash reads a script as it runs; wrapping the body in main() means an edit during a run cannot change the run.
main "$@"