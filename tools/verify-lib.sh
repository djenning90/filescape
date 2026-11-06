# FileScape verification: how a step's outcome is judged (TEST-04, AD-37). Sourced by tools/verify.sh and by
# tools/verify-selftest.sh (which exercises every failure path with test doubles). Needs OUT (a directory), SUM (the
# summary file) and FAILS (a counter) to be set by the caller.
#
# A step passes only if ALL of these hold:
#   - its command exited 0 (exit 124 from `timeout` is reported as "timed out"; any other non-zero as "exit N");
#   - its result file exists, is not empty, and has at least one PASS/FAIL assertion line;
#   - it has no FAIL line.
# A partial result file with passing assertions never hides a non-zero exit or a timeout. A recording passes only if its
# command exited 0 and the video file exists and is not empty.

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

# judge NAME CODE SECONDS FILE LABEL: records one probe step from its exit code and result file; returns 1 if it failed.
judge() {
  local name="$1" code="$2" secs="$3" f="$4" label="$5"
  counts "$f"
  local why; why="$(exit_reason "$code")"
  [[ -n "$WHY" ]] && why="${why:+$why; }$WHY"
  [[ $X -gt 0 ]] && why="${why:+$why; }$X failing assertion(s)"
  local verdict=ok
  if [[ -n "$why" ]]; then verdict=FAILED; FAILS=$((FAILS + 1)); fi
  printf '%-28s %7ss  %-6s %-14s %3s PASS %2s FAIL %2s SKIP%s\n' "$name" "$secs" "$verdict" "$label" "$P" "$X" "$K" "${why:+  ($why)}" | tee -a "$SUM"
  [[ -s "$f" ]] && grep -E '(^| )FAIL ' "$f" | head -5 | sed 's/^/    /' | tee -a "$SUM"
  [[ $verdict == ok ]]
}

# probe_step NAME TIMEOUT RESULTFILE LABEL CMD...: runs CMD under `timeout`, output to $OUT/NAME.out, then judges it.
probe_step() {
  local name="$1" to="$2" f="$3" label="$4"; shift 4
  local s; s=$(date +%s.%N)
  timeout "$to" "$@" > "$OUT/$name.out" 2>&1
  local code=$?
  judge "$name" "$code" "$(printf '%.1f' "$(echo "$(date +%s.%N) - $s" | bc)")" "$f" "$label"
}

# recording_step NAME TIMEOUT VIDEO CMD...: a recording passes only if CMD exits 0 and VIDEO exists and is not empty.
recording_step() {
  local name="$1" to="$2" video="$3"; shift 3
  local s; s=$(date +%s.%N)
  timeout "$to" "$@" > "$OUT/$name.out" 2>&1
  local code=$?
  local secs; secs=$(printf '%.1f' "$(echo "$(date +%s.%N) - $s" | bc)")
  local why size=0; why="$(exit_reason "$code")"
  if [[ ! -e "$video" ]]; then why="${why:+$why; }no video file"
  else size=$(stat -c %s "$video"); [[ $size -gt 0 ]] || why="${why:+$why; }empty video file"; fi
  local verdict=ok
  if [[ -n "$why" ]]; then verdict=FAILED; FAILS=$((FAILS + 1)); fi
  printf '%-28s %7ss  %-6s recording      %s bytes%s\n' "$name" "$secs" "$verdict" "$size" "${why:+  ($why)}" | tee -a "$SUM"
  [[ $verdict == ok ]]
}
