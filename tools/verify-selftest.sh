#!/usr/bin/env bash
# Self-test of the verification harness's judging (TEST-04): every pass and failure path of tools/verify-lib.sh, with
# isolated test doubles (shell commands that write result files and exit as told). Nothing is launched, nothing is
# exported, no window or input is touched; runs in about 10 s.  Usage: tools/fs.sh check-harness (or this script).
set -uo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
source "$REPO/tools/verify-lib.sh"
OUT="$(mktemp -d "${TMPDIR:-/tmp}/filescape-verify-selftest.XXXXXX")"
SUM="$OUT/summary.txt"; : > "$SUM"
FAILS=0
BAD=0
# The session timing log (TEST-06) goes to this run's own file and session, never the real one.
export FS_TIMING_LOG="$OUT/timing.tsv" FS_TIMING_SESSION=selftest

# A test double: write CONTENT (printf format, may be empty, or "-" for no file) to FILE, optionally sleep, exit CODE.
double() {
  local file="$1" content="$2" code="$3" sleep_s="${4:-0}"
  mkdir -p "$(dirname "$file")"
  [[ "$content" != "-" ]] && printf "$content" > "$file"
  [[ "$sleep_s" != 0 ]] && sleep "$sleep_s"
  return "$code"
}
export -f double

# expect NAME VERDICT REASON-SUBSTRING STEP...: run one judged step and check the counter moved (or not) and why.
expect() {
  local name="$1" want="$2" reason="$3"; shift 3
  local before=$FAILS
  "$@" > /dev/null
  local got=pass; [[ $FAILS -gt $before ]] && got=fail
  local line; line="$(grep -E "^$name " "$SUM" | tail -1)"
  if [[ "$got" == "$want" && ( -z "$reason" || "$line" == *"$reason"* ) ]]; then
    printf 'ok    %-26s %-4s %s\n' "$name" "$got" "${reason:+[$reason]}"
  else
    printf 'WRONG %-26s got %s, wanted %s %s\n      %s\n' "$name" "$got" "$want" "${reason:+[$reason]}" "$line"
    BAD=$((BAD + 1))
  fi
}

R="$OUT/res"
P2='PASS OS one\nPASS OS two\n'
# Probe steps (journeys and real OS input share these rules).
expect clean           pass ""                          probe_step clean 5 "$R/clean.txt" probe bash -c "double '$R/clean.txt' '$P2' 0"
expect assertion-fail  fail "1 failing assertion"       probe_step assertion-fail 5 "$R/af.txt" probe bash -c "double '$R/af.txt' 'PASS a\nFAIL b\n' 0"
expect exit-nonzero    fail "exit 3"                    probe_step exit-nonzero 5 "$R/nz.txt" probe bash -c "double '$R/nz.txt' '$P2' 3"
expect timed-out       fail "timed out"                 probe_step timed-out 1 "$R/to.txt" probe bash -c "double '$R/to.txt' '$P2' 0 5"
expect missing         fail "no result file"            probe_step missing 5 "$R/none.txt" probe bash -c "double '$R/none.txt' - 0"
expect empty           fail "empty result file"         probe_step empty 5 "$R/empty.txt" probe bash -c "double '$R/empty.txt' '' 0"
expect no-assertions   fail "no assertions"             probe_step no-assertions 5 "$R/na.txt" probe bash -c "double '$R/na.txt' 'starting\ndone failures=0\n' 0"
expect missing-and-exit fail "exit 1; no result file"   probe_step missing-and-exit 5 "$R/none2.txt" probe bash -c "double '$R/none2.txt' - 1"
# An OS-input script that dies mid-way after writing some passing lines (e.g. PowerShell throws): exit 1 must count.
expect os-partial      fail "exit 1"                    probe_step os-partial 5 "$R/os.txt" "real OS input" bash -c "double '$R/os.txt' 'PASS OS arrives\nPASS OS walks\n' 1"
# Recordings.
V="$OUT/video"
expect rec-clean       pass ""                          recording_step rec-clean 5 "$V/ok.avi" bash -c "double '$V/ok.avi' 'RIFFxxxxAVI ' 0"
expect rec-exit        fail "exit 2"                    recording_step rec-exit 5 "$V/exit.avi" bash -c "double '$V/exit.avi' 'RIFFxxxxAVI ' 2"
expect rec-timeout     fail "timed out"                 recording_step rec-timeout 1 "$V/to.avi" bash -c "double '$V/to.avi' 'RIFF' 0 5"
expect rec-missing     fail "no video file"             recording_step rec-missing 5 "$V/none.avi" bash -c "double '$V/none.avi' - 0"
expect rec-empty       fail "empty video file"          recording_step rec-empty 5 "$V/empty.avi" bash -c "double '$V/empty.avi' '' 0"

echo "summary lines written by the harness:"; sed 's/^/  /' "$SUM"
echo "failing steps counted: $FAILS (expected 12)"
[[ $FAILS -eq 12 ]] || BAD=$((BAD + 1))

# Timing rows (TEST-05): every judged step has one, with the result word the table shows.
skip_step skipped-one "an optional fixture is missing"
# row NAME RESULT: the step's row has this result.
row() {
  local r; r="$(printf '%s\n' "${TIMING_ROWS[@]}" | grep -E "^$1\|" | tail -1 | cut -d'|' -f2)"
  if [[ "$r" == "$2" ]]; then printf 'ok    %-26s row %s\n' "$1" "$2"; else printf 'WRONG %-26s row %s, wanted %s\n' "$1" "$r" "$2"; BAD=$((BAD + 1)); fi
}
row clean pass; row assertion-fail fail; row exit-nonzero fail; row timed-out timeout; row missing fail
row rec-clean pass; row rec-timeout timeout; row rec-empty fail; row skipped-one skipped
[[ ${#TIMING_ROWS[@]} -eq 15 ]] || { echo "WRONG timing rows: ${#TIMING_ROWS[@]}, wanted 15"; BAD=$((BAD + 1)); }

# The launch lock: the wait for it is in the note, not in the step's seconds.
LOCK="$OUT/launch.lock"
flock "$LOCK" sleep 1.5 & holder=$!
sleep 0.2
FS_LAUNCH_LOCK="$LOCK" probe_step locked 5 "$R/locked.txt" probe bash -c "double '$R/locked.txt' '$P2' 0" > /dev/null
wait "$holder"
locked="$(printf '%s\n' "${TIMING_ROWS[@]}" | grep '^locked|')"
lsecs="$(cut -d'|' -f3 <<<"$locked")"; lwait="$(grep -oE 'lock wait [0-9.]+' <<<"$locked" | grep -oE '[0-9.]+$')"
if [[ -n "$lwait" ]] && (( $(echo "$lwait >= 1.0 && $lsecs < 1.0" | bc) )); then echo "ok    locked                     waited $lwait s for the lock, ran $lsecs s"
else echo "WRONG locked: $locked"; BAD=$((BAD + 1)); fi

# Interrupted: a signal during a step (SIGTERM here: a background shell starts with SIGINT ignored, so the test cannot
# send Ctrl+C; verify.sh traps both) stops its command at once, records it as interrupted, and the table still prints.
INT_OUT="$OUT/int"; mkdir -p "$INT_OUT"
bash -c '
  source "$1/tools/verify-lib.sh"; OUT="$2"; SUM="$2/summary.txt"; FAILS=0; export -f double 2>/dev/null
  trap "interrupt_current; timing_table interrupted-run 9.9; exit 143" TERM
  probe_step long 30 "$2/long.txt" probe sleep 20
' _ "$REPO" "$INT_OUT" > "$INT_OUT/table.txt" 2>&1 & runner=$!
sleep 0.5; t=$(date +%s.%N); kill -TERM "$runner"; wait "$runner"; took="$(secs_since "$t")"
if grep -qE '^long +interrupted' "$INT_OUT/table.txt" && grep -q '^TOTAL .*1 interrupted' "$INT_OUT/table.txt" && (( $(echo "$took < 3" | bc) )); then
  echo "ok    interrupted                stopped in $took s, row and table printed"
else echo "WRONG interrupted (took $took s):"; sed 's/^/      /' "$INT_OUT/table.txt"; BAD=$((BAD + 1)); fi
pgrep -f 'sleep 20' > /dev/null && pgrep -af 'sleep 20' | grep -q "$INT_OUT" && { echo "WRONG interrupted: its command still runs"; BAD=$((BAD + 1)); }

# Accounting (TEST-06): a second run of a step in the same session is a rerun with its reason; the table's last three
# lines keep active, lock-wait and rerun seconds apart; the log has one line per step that ran (skips are not logged; the interrupted run above adds one).
FS_RERUN_WHY="second try" probe_step clean 5 "$R/clean.txt" probe bash -c "double '$R/clean.txt' '$P2' 0" > /dev/null
table="$(timing_table selftest 1.0)"
last="$(printf '%s\n' "${TIMING_ROWS[@]}" | tail -1)"
if [[ "$last" == clean\|pass\|*"rerun (second try)"*\|1 ]] && grep -qE '^reruns +[0-9.]+ +1 step\(s\) already run in session selftest: clean$' <<<"$table" \
   && grep -qE '^lock-wait +1\.[0-9]' <<<"$table" && grep -qE '^active +[0-9.]+ ' <<<"$table" \
   && [[ "$(wc -l < "$FS_TIMING_LOG")" -eq ${#TIMING_ROWS[@]} ]] && [[ "$(awk -F'\t' '$8 == 1' "$FS_TIMING_LOG" | cut -f4,9)" == $'clean\tsecond try' ]]; then
  echo "ok    accounting                 active, lock-wait and reruns apart; the rerun logged with its reason"
else echo "WRONG accounting: last row '$last', log $(wc -l < "$FS_TIMING_LOG") lines:"; sed 's/^/      /' <<<"$table" | tail -4; BAD=$((BAD + 1)); fi
# fs.sh timed: an ad-hoc command's row (kind timed), its exit code kept; fs.sh timing sums the session.
"$REPO/tools/fs.sh" timed ad-hoc -- bash -c 'sleep 0.2; exit 3' 2> /dev/null; tc=$?
summary="$("$REPO/tools/fs.sh" timing)"
if [[ $tc -eq 3 ]] && awk -F'\t' '$3 == "timed" && $4 == "ad-hoc" && $5 == "fail"' "$FS_TIMING_LOG" | grep -q . && grep -q '1 ad-hoc' <<<"$summary" && grep -qE 'reruns +[0-9.]+ s in 1 row' <<<"$summary"; then
  echo "ok    timed                      exit 3 kept, row logged, fs.sh timing sums the session"
else echo "WRONG timed (exit $tc):"; sed 's/^/      /' <<<"$summary"; BAD=$((BAD + 1)); fi
# fs.sh timed --lock: the wait for the desktop is logged apart from the active seconds.
flock "$LOCK" sleep 1.2 & holder=$!
sleep 0.2
FS_LAUNCH_LOCK="$LOCK" "$REPO/tools/fs.sh" timed waited --lock -- true 2> /dev/null
wait "$holder"
IFS=$'\t' read -r _ _ _ _ _ tsecs twait _ < <(awk -F'\t' '$4 == "waited"' "$FS_TIMING_LOG")
if (( $(echo "${twait:-0} >= 0.8 && ${tsecs:-9} < 0.5" | bc) )); then echo "ok    timed --lock               waited $twait s, active $tsecs s"
else echo "WRONG timed --lock: wait '$twait', active '$tsecs'"; BAD=$((BAD + 1)); fi

echo "timing table:"; timing_table selftest 1.0 | sed 's/^/  /'
rm -rf "$OUT"
if [[ $BAD -eq 0 ]]; then echo "verify harness self-test: all paths judged as expected"; else echo "verify harness self-test: $BAD WRONG"; fi
exit $(( BAD > 0 ? 1 : 0 ))
