# FileScape verification: how a step's outcome is judged (TEST-04, AD-37) and how long it took (TEST-05, AD-80). Sourced
# by tools/verify.sh and by tools/verify-selftest.sh (which exercises every failure path with test doubles). Needs OUT
# (a directory), SUM (the summary file) and FAILS (a counter) to be set by the caller.
#
# A step passes only if ALL of these hold:
#   - its command exited 0 (exit 124 from `timeout` is reported as "timed out"; any other non-zero as "exit N");
#   - its result file exists, is not empty, and has at least one PASS/FAIL assertion line;
#   - it has no FAIL line.
# A partial result file with passing assertions never hides a non-zero exit or a timeout. A recording passes only if its
# command exited 0 and the video file exists and is not empty.
#
# Timing (TEST-05): every step, judged or not, adds a row (name, result, seconds, note) with timing_record; timing_table
# prints them with the total. Results are pass, fail, timeout, skipped and interrupted (a step cut short by Ctrl+C or a
# kill). A step's seconds are its own run only: time spent waiting for the shared launch lock (FS_LAUNCH_LOCK) is
# reported separately in the note, so a step's duration does not depend on what other people run on the desktop.
# What a change must preserve: one row per step that ran or was skipped, in run order, and a table that the callers can
# print from an EXIT trap (it uses only shell state, never the step's files).

# counts FILE: sets P, X, K (PASS, FAIL, SKIP lines) and WHY (why the file itself is unusable, or empty).
counts() {
  local f="$1"
  P=0; X=0; K=0; WHY=""
  if [[ ! -e "$f" ]]; then WHY="no result file"; return; fi
  if [[ ! -s "$f" ]]; then WHY="empty result file"; return; fi
  P=$(grep -cE '(^| )PASS ' "$f" || true)
  X=$(grep -cE '(^| )FAIL ' "$f" || true)
  K=$(grep -cE '(^| )SKIP ' "$f" || true)
  [[ $P -eq 0 && $X -eq 0 ]] && WHY="no assertions in result file"
  return 0
}

# exit_reason CODE: "" for 0, "timed out" for 124, else "exit N".
exit_reason() {
  case "$1" in 0) echo "" ;; 124) echo "timed out" ;; *) echo "exit $1" ;; esac
}

# result_word CODE VERDICT: the timing table's result for a step (pass, fail or timeout).
result_word() {
  if [[ "$2" == ok ]]; then echo pass; elif [[ "$1" == 124 ]]; then echo timeout; else echo fail; fi
}

# ---- timing (TEST-05)
TIMING_ROWS=()
# timing_record NAME RESULT SECONDS [NOTE]
timing_record() { TIMING_ROWS+=("$1|$2|$3|${4:-}"); }

# secs_since START: seconds since START (a `date +%s.%N` value), one decimal.
secs_since() { printf '%.1f' "$(echo "$(date +%s.%N) - $1" | bc)"; }

# timing_table TITLE TOTAL_SECONDS: the end-of-run table: one line per step, then the counts and the total.
timing_table() {
  local title="$1" total="$2" row n r s note waited=0 w
  local -A count=()
  echo "== timing: $title =="
  printf '%-28s %-11s %8s  %s\n' step result seconds note
  for row in "${TIMING_ROWS[@]}"; do
    IFS='|' read -r n r s note <<<"$row"
    printf '%-28s %-11s %8s  %s\n' "$n" "$r" "$s" "$note"
    count[$r]=$(( ${count[$r]:-0} + 1 ))
    w="$(grep -oE 'lock wait [0-9.]+' <<<"$note" | grep -oE '[0-9.]+$' || true)"
    [[ -n "$w" ]] && waited="$(echo "$waited + $w" | bc)"
  done
  local summary="" k
  for k in pass fail timeout interrupted skipped; do [[ -n "${count[$k]:-}" ]] && summary+="${summary:+, }${count[$k]} $k"; done
  local tail="${summary:-no steps}"
  [[ "$waited" != 0 ]] && tail+="; launch lock waits $waited s (not in the steps' seconds)"
  printf '%-28s %-11s %8s  %s\n' TOTAL "" "$total" "$tail"
}

# ---- running a command (TEST-05): timed, under a timeout, interruptible, optionally holding the launch lock.
# FS_LAUNCH_LOCK=/path: every app launch (journeys, real-input probes, recordings) holds this flock for the launch only,
# so several people (or agents) sharing one desktop take turns at the screen. The lock's file descriptor is closed in
# the launched command, so nothing it starts can keep holding the lock after it exits.
CHILD=""; CUR_NAME=""; CUR_START=""
# run_cmd NAME TIMEOUT_S LOCK(0|1) OUTFILE CMD...: sets RUN_CODE, RUN_SECS and LOCK_NOTE ("" or "lock wait N s").
run_cmd() {
  local name="$1" to="$2" lock="$3" outf="$4"; shift 4
  local lfd="" w0
  LOCK_NOTE=""
  if [[ "$lock" == 1 && -n "${FS_LAUNCH_LOCK:-}" ]]; then
    w0=$(date +%s.%N)
    exec {lfd}>>"$FS_LAUNCH_LOCK"
    flock "$lfd"
    LOCK_NOTE="lock wait $(secs_since "$w0") s"
  fi
  CUR_NAME="$name"; CUR_START=$(date +%s.%N)
  # In the background and waited for, so Ctrl+C reaches the caller's trap at once (a foreground child would be waited
  # for first, up to its whole timeout).
  if [[ -n "$lfd" ]]; then timeout "$to" "$@" > "$outf" 2>&1 {lfd}>&- &
  else timeout "$to" "$@" > "$outf" 2>&1 &
  fi
  CHILD=$!
  wait "$CHILD"; RUN_CODE=$?
  CHILD=""
  RUN_SECS="$(secs_since "$CUR_START")"; CUR_NAME=""
  if [[ -n "$lfd" ]]; then flock -u "$lfd"; exec {lfd}>&-; fi
  return 0
}

# interrupt_current: for a signal trap: stops the running step's command (only the process this harness started) and
# records the step as interrupted.
interrupt_current() {
  if [[ -n "$CHILD" ]]; then kill -TERM "$CHILD" 2>/dev/null; wait "$CHILD" 2>/dev/null; CHILD=""; fi
  if [[ -n "$CUR_NAME" ]]; then timing_record "$CUR_NAME" interrupted "$(secs_since "$CUR_START")"; CUR_NAME=""; fi
}

# judge NAME CODE SECONDS FILE LABEL [NOTE]: records one probe step from its exit code and result file; returns 1 if it
# failed.
judge() {
  local name="$1" code="$2" secs="$3" f="$4" label="$5" note="${6:-}"
  counts "$f"
  local why; why="$(exit_reason "$code")"
  [[ -n "$WHY" ]] && why="${why:+$why; }$WHY"
  [[ $X -gt 0 ]] && why="${why:+$why; }$X failing assertion(s)"
  local verdict=ok
  if [[ -n "$why" ]]; then verdict=FAILED; FAILS=$((FAILS + 1)); fi
  printf '%-28s %7ss  %-6s %-14s %3s PASS %2s FAIL %2s SKIP%s\n' "$name" "$secs" "$verdict" "$label" "$P" "$X" "$K" "${why:+  ($why)}" | tee -a "$SUM"
  [[ -s "$f" ]] && grep -E '(^| )FAIL ' "$f" | head -5 | sed 's/^/    /' | tee -a "$SUM"
  timing_record "$name" "$(result_word "$code" "$verdict")" "$secs" "$P/$X/$K pass/fail/skip${note:+; $note}${why:+; $why}"
  [[ $verdict == ok ]]
}

# wanted NAME: true unless the caller defines step_wanted NAME and it says no (fs.sh check journeys NAME... runs only
# the chosen steps); an unwanted step is left out without a row.
wanted() { ! declare -F step_wanted > /dev/null || step_wanted "$1"; }

# probe_step NAME TIMEOUT RESULTFILE LABEL CMD...: runs CMD under `timeout` (holding the launch lock), output to
# $OUT/NAME.out, then judges it.
probe_step() {
  local name="$1" to="$2" f="$3" label="$4"; shift 4
  wanted "$name" || return 0
  run_cmd "$name" "$to" 1 "$OUT/$name.out" "$@"
  judge "$name" "$RUN_CODE" "$RUN_SECS" "$f" "$label" "$LOCK_NOTE"
}

# recording_step NAME TIMEOUT VIDEO CMD...: a recording passes only if CMD exits 0 and VIDEO exists and is not empty.
recording_step() {
  local name="$1" to="$2" video="$3"; shift 3
  wanted "$name" || return 0
  run_cmd "$name" "$to" 1 "$OUT/$name.out" "$@"
  local code=$RUN_CODE secs=$RUN_SECS
  local why size=0; why="$(exit_reason "$code")"
  if [[ ! -e "$video" ]]; then why="${why:+$why; }no video file"
  else size=$(stat -c %s "$video"); [[ $size -gt 0 ]] || why="${why:+$why; }empty video file"; fi
  local verdict=ok
  if [[ -n "$why" ]]; then verdict=FAILED; FAILS=$((FAILS + 1)); fi
  printf '%-28s %7ss  %-6s recording      %s bytes%s\n' "$name" "$secs" "$verdict" "$size" "${why:+  ($why)}" | tee -a "$SUM"
  timing_record "$name" "$(result_word "$code" "$verdict")" "$secs" "$size bytes${LOCK_NOTE:+; $LOCK_NOTE}${why:+; $why}"
  [[ $verdict == ok ]]
}

# skip_step NAME REASON: a step this run leaves out on purpose (a missing optional fixture), shown in the table.
skip_step() {
  wanted "$1" || return 0
  printf '%-28s %7s   SKIP   %s\n' "$1" "-" "$2" | tee -a "$SUM"
  timing_record "$1" skipped 0.0 "$2"
}
