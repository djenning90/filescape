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
# What a change must preserve: one row per step that ran or was skipped, in run order (a parallel batch's rows in the
# order its launches finished, AD-91), and a table that the callers can print from an EXIT trap (it uses only shell
# state, never the step's files).
#
# Accounting (TEST-06, AD-88; TEST-16, AD-91): the table ends with separate totals: active time (the steps' own seconds),
# lock-wait time (waiting for the shared desktop), reruns, desktop occupancy (seconds with this run's app windows open)
# and, when journeys ran side by side, parallelism (their summed seconds against the batches' wall time). Every row that
# ran is also appended to the session timing log (timing_log_append), which `tools/fs.sh timed NAME -- CMD` (ad-hoc
# launches) and `tools/fs.sh timing` (the session's totals) share; a check also appends one `desktop` row with its
# occupancy (timing_log_desktop). A rerun is a step whose name the same session has already logged; FS_RERUN_WHY says why.

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
# timing_record NAME RESULT SECONDS [NOTE]: a table row; a step that ran (not skipped) is also logged for the session, and
# marked a rerun when the session has logged that name before.
timing_record() {
  local note="${4:-}" rerun=0 w
  if [[ "$2" != skipped ]]; then
    w="$(grep -oE 'lock wait [0-9.]+' <<<"$note" | grep -oE '[0-9.]+$' || true)"
    rerun="$(timing_log_append check "$1" "$2" "$3" "${w:-0}")"
    [[ "$rerun" == 1 ]] && note+="${note:+; }rerun${FS_RERUN_WHY:+ ($FS_RERUN_WHY)}"
  fi
  TIMING_ROWS+=("$1|$2|$3|$note|${rerun:-0}")
}

# ---- the session timing log (TEST-06, AD-88)
# One tab-separated line per step or timed command: UTC time, session, kind (check|timed|desktop), name, result, active seconds,
# lock-wait seconds, rerun (0|1), why (FS_RERUN_WHY), commit. FS_TIMING_LOG chooses the file (default
# ~/.local/state/filescape/timing.tsv; "off" logs nothing); FS_TIMING_SESSION names the session (default: the git
# branch, so each worktree's agent has its own totals). Each append is one short line (O_APPEND), safe beside other
# writers. What a change must preserve: the column order (tools/fs.sh timing reads it) and the rerun rule.
timing_log_path() { echo "${FS_TIMING_LOG:-${XDG_STATE_HOME:-$HOME/.local/state}/filescape/timing.tsv}"; }
timing_session() {
  if [[ -n "${FS_TIMING_SESSION:-}" ]]; then echo "$FS_TIMING_SESSION"; return; fi
  git -C "${REPO:-.}" rev-parse --abbrev-ref HEAD 2>/dev/null || echo default
}
# timing_log_append KIND NAME RESULT SECONDS LOCK_WAIT_SECONDS: appends the line; prints 1 if it is a rerun, else 0.
timing_log_append() {
  local log session rerun=0 why=""
  log="$(timing_log_path)"
  if [[ "$log" == off ]]; then echo 0; return; fi
  session="$(timing_session)"
  mkdir -p "$(dirname "$log")" 2>/dev/null
  if [[ "$1" != desktop && -f "$log" ]] && awk -F'\t' -v s="$session" -v n="$2" '$2 == s && $4 == n { f = 1 } END { exit !f }' "$log"; then rerun=1; fi
  [[ $rerun == 1 ]] && why="${FS_RERUN_WHY:-(no reason given)}"
  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$(date -u +%FT%TZ)" "$session" "$1" "$2" "$3" "$4" "$5" "$rerun" "${why//$'\t'/ }" \
    "$(git -C "${REPO:-.}" rev-parse --short=12 HEAD 2>/dev/null)" >> "$log" 2>/dev/null
  echo "$rerun"
}

# timing_log_desktop NAME SECONDS: one `desktop` row (kind desktop, never a rerun): a check's desktop occupancy, which
# `tools/fs.sh timing` sums apart from the active seconds (TEST-16).
timing_log_desktop() { timing_log_append desktop "$1" ok "$2" 0 > /dev/null; }

# mono_s: a monotonic time stamp in seconds (/proc/uptime, 0.01 s). Durations never use the wall clock: WSL steps the
# wall clock to keep it in time with Windows, by about a second at a time (measured −0.8 s and −1.4 s within 20 s on
# 2026-11-20), which made a lock wait come out at −0.9 s in the harness self-test.
mono_s() { local u _; read -r u _ < /proc/uptime; echo "$u"; }
# secs_since START: seconds since START (a mono_s value), one decimal.
secs_since() { printf '%.1f' "$(echo "$(mono_s) - $1" | bc)"; }

# timing_table TITLE TOTAL_SECONDS: the end-of-run table: one line per step, then the counts and the total.
# Its last three lines are the accounting (TEST-06): active (the sum of the steps' own seconds; the WSL tests run beside
# other steps, so this can exceed the wall time), lock-wait (seconds waiting for the shared desktop) and reruns (steps
# this session had already logged, and their seconds).
timing_table() {
  local title="$1" total="$2" row n r s note rr waited=0 w active=0 rerun_s=0 reruns=()
  local -A count=()
  echo "== timing: $title =="
  printf '%-28s %-11s %8s  %s\n' step result seconds note
  for row in "${TIMING_ROWS[@]}"; do
    IFS='|' read -r n r s note rr <<<"$row"
    printf '%-28s %-11s %8s  %s\n' "$n" "$r" "$s" "$note"
    count[$r]=$(( ${count[$r]:-0} + 1 ))
    active="$(echo "$active + $s" | bc)"
    if [[ "$rr" == 1 ]]; then reruns+=("$n"); rerun_s="$(echo "$rerun_s + $s" | bc)"; fi
    w="$(grep -oE 'lock wait [0-9.]+' <<<"$note" | grep -oE '[0-9.]+$' || true)"
    [[ -n "$w" ]] && waited="$(echo "$waited + $w" | bc)"
  done
  local summary="" k
  for k in pass fail timeout interrupted skipped; do [[ -n "${count[$k]:-}" ]] && summary+="${summary:+, }${count[$k]} $k"; done
  local tail="${summary:-no steps}"
  [[ "$waited" != 0 ]] && tail+="; launch lock waits $waited s (not in the steps' seconds)"
  printf '%-28s %-11s %8s  %s\n' TOTAL "" "$total" "$tail"
  printf '%-28s %-11s %8s  %s\n' active "" "$(printf '%.1f' "$active")" "the steps' own seconds"
  printf '%-28s %-11s %8s  %s\n' lock-wait "" "$(printf '%.1f' "$waited")" "waiting for the shared desktop (FS_LAUNCH_LOCK)"
  printf '%-28s %-11s %8s  %s\n' reruns "" "$(printf '%.1f' "$rerun_s")" "${#reruns[@]} step(s) already run in session $(timing_session)${reruns[*]:+: ${reruns[*]}}"
  # TEST-16 (AD-91): how long this run had the desktop (at least one of its app windows open: journeys, real-input
  # probes, recordings; the stage's smoke launch is in the stage row), and what running journeys side by side saved.
  printf '%-28s %-11s %8s  %s\n' desktop "" "$(printf '%.1f' "${DESKTOP_S:-0}")" "seconds with this run's app windows open (the launch lock held)"
  if [[ "${PAR_STEPS:-0}" -gt 0 ]]; then
    printf '%-28s %-11s %8s  %s\n' parallel "" "$(printf '%.1f' "$PAR_WALL_S")" "$PAR_STEPS journeys, $(printf '%.1f' "$PAR_STEPS_S") s of steps in $(printf '%.1f' "$PAR_WALL_S") s, up to $PAR_MAX at once (×$(echo "scale=2; $PAR_STEPS_S / ($PAR_WALL_S + 0.001)" | bc | sed 's/^\./0./'))"
  fi
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
    w0=$(mono_s)
    exec {lfd}>>"$FS_LAUNCH_LOCK"
    flock "$lfd"
    local waited; waited="$(secs_since "$w0")"
    LOCK_NOTE="lock wait $waited s"
    # FS_LOCK_WAIT_FILE (set by fs.sh timed): each wait is appended, so a timed command's nested waits are summed (TEST-06).
    [[ -n "${FS_LOCK_WAIT_FILE:-}" ]] && echo "$waited" >> "$FS_LOCK_WAIT_FILE"
  fi
  CUR_NAME="$name"; CUR_START=$(mono_s)
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
  # An app launch (lock 1) has a window on the desktop for its whole run (TEST-16).
  [[ "$lock" == 1 ]] && DESKTOP_S="$(echo "$DESKTOP_S + $RUN_SECS" | bc)"
  return 0
}

# ---- journeys side by side (TEST-15, AD-91)
# A queue holds launches to run later: `sim` for window-only journeys on the simulated clock (run several at once),
# `rt` for those that keep the wall clock (run one at a time, after the parallel ones, so nothing contends with their
# real-time checks). pool_run runs a queue, at most N at once, and judges each launch as it ends, so a parallel batch's
# rows are in the order the launches finished. A batch of more than one at once holds FS_LAUNCH_LOCK for the whole
# batch (its wait is in the first row's note); one at a time holds it per launch, as before. Each launch has its own
# output file ($OUT/NAME.out) and result file, and its exit code goes through a file, so the judging is unchanged.
# What a change must preserve: every queued step gets exactly one row (pass, fail, timeout, or interrupted from the
# trap); a launch never inherits the lock's descriptor; pool_interrupt stops only processes this harness started.
declare -ga POOL_sim=() POOL_rt=()
declare -gA POOL_RUNNING=() POOL_NAME=() POOL_T0=()
DESKTOP_S=0            # seconds with at least one of this run's app windows open (journeys, probes, recordings)
PAR_STEPS=0 PAR_STEPS_S=0 PAR_WALL_S=0 PAR_MAX=1   # parallel batches: steps, their summed seconds, wall, widest

# pool_add QUEUE NAME TIMEOUT RESULTFILE LABEL NOTE CMD...: queues a launch (left out, without a row, if not wanted).
pool_add() {
  local q="$1" name="$2" to="$3" f="$4" label="$5" note="$6"; shift 6
  wanted "$name" || return 0
  local cmd; printf -v cmd '%q ' "$@"
  local -n queue="POOL_$q"
  queue+=("$name"$'\x1f'"$to"$'\x1f'"$f"$'\x1f'"$label"$'\x1f'"$note"$'\x1f'"$cmd")
}

# pool_run QUEUE N [STAGGER_S]: runs and empties the queue, at most N launches at once, starting them STAGGER_S apart
# (so N processes do not load .NET and compile shaders in the same second).
pool_run() {
  local q="$1" par="$2" stagger="${3:-0}"
  local -n queue="POOL_$q"
  (( ${#queue[@]} )) || return 0
  local -a jobs=("${queue[@]}"); queue=()
  local lfd="" w0 waited="" t0 i=0 running=0 pid idx code secs name to f label note cmd first=1 batch_lock=0
  (( par > 1 )) && batch_lock=1
  if [[ $batch_lock == 1 && -n "${FS_LAUNCH_LOCK:-}" ]]; then
    w0=$(mono_s); exec {lfd}>>"$FS_LAUNCH_LOCK"; flock "$lfd"; waited="$(secs_since "$w0")"
    [[ -n "${FS_LOCK_WAIT_FILE:-}" ]] && echo "$waited" >> "$FS_LOCK_WAIT_FILE"
  fi
  t0=$(mono_s)
  while (( i < ${#jobs[@]} || running > 0 )); do
    while (( i < ${#jobs[@]} && running < par )); do
      IFS=$'\x1f' read -r name to f label note cmd <<<"${jobs[$i]}"
      if [[ $batch_lock == 0 ]]; then
        # One at a time: exactly the serial path (lock per launch, timed, interruptible).
        eval "local -a argv=($cmd)"
        run_cmd "$name" "$to" 1 "$OUT/$name.out" "${argv[@]}"
        judge "$name" "$RUN_CODE" "$RUN_SECS" "$f" "$label" "${note:+$note; }$LOCK_NOTE"
        i=$((i + 1)); continue
      fi
      rm -f "$OUT/$name.code"
      ( [[ -n "$lfd" ]] && exec {lfd}>&-
        s=$(mono_s); eval "timeout $to $cmd" > "$OUT/$name.out" 2>&1; c=$?
        echo "$c $(secs_since "$s")" > "$OUT/$name.code" ) &
      POOL_RUNNING[$!]=$i; POOL_NAME[$!]="$name"; POOL_T0[$!]=$(mono_s)
      running=$((running + 1)); i=$((i + 1))
      if (( stagger > 0 && i < ${#jobs[@]} && running < par )); then sleep "$stagger"; fi
    done
    (( running > 0 )) || continue
    pid=""; wait -n -p pid "${!POOL_RUNNING[@]}" 2>/dev/null
    [[ -n "$pid" && -n "${POOL_RUNNING[$pid]:-}" ]] || continue
    idx=${POOL_RUNNING[$pid]}; unset "POOL_RUNNING[$pid]" "POOL_NAME[$pid]" "POOL_T0[$pid]"; running=$((running - 1))
    IFS=$'\x1f' read -r name to f label note cmd <<<"${jobs[$idx]}"
    read -r code secs < "$OUT/$name.code" 2>/dev/null || { code=1; secs=0.0; }
    note="${note:+$note; }parallel ×$par"
    if [[ $first == 1 && -n "$waited" ]]; then note+="; lock wait $waited s"; fi
    first=0
    judge "$name" "$code" "$secs" "$f" "$label" "$note"
    PAR_STEPS=$((PAR_STEPS + 1)); PAR_STEPS_S="$(echo "$PAR_STEPS_S + $secs" | bc)"
  done
  if [[ $batch_lock == 1 ]]; then
    local wall; wall="$(secs_since "$t0")"
    DESKTOP_S="$(echo "$DESKTOP_S + $wall" | bc)"; PAR_WALL_S="$(echo "$PAR_WALL_S + $wall" | bc)"
    (( par > PAR_MAX )) && PAR_MAX=$par
  fi
  if [[ -n "$lfd" ]]; then flock -u "$lfd"; exec {lfd}>&-; fi
  return 0
}

# pool_interrupt: for a signal trap: stops the parallel launches in flight (only processes this harness started) and
# records each as interrupted.
pool_interrupt() {
  local pid
  for pid in "${!POOL_RUNNING[@]}"; do
    pkill -TERM -P "$pid" 2>/dev/null; kill -TERM "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
    timing_record "${POOL_NAME[$pid]}" interrupted "$(secs_since "${POOL_T0[$pid]}")" "parallel"
    unset "POOL_RUNNING[$pid]"
  done
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
