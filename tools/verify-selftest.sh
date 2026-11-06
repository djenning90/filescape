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
rm -rf "$OUT"
if [[ $BAD -eq 0 ]]; then echo "verify harness self-test: all paths judged as expected"; else echo "verify harness self-test: $BAD WRONG"; fi
exit $(( BAD > 0 ? 1 : 0 ))
