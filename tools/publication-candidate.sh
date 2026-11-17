#!/usr/bin/env bash
# A publication candidate of FileScape's history, built in a separate clone so it can be reviewed before anything is
# public (2026-11-17, DJ: keep the commits that show how FileScape went from the flat-world prototype to Planets and its
# playtests; preserve the original history locally).
#
#   tools/publication-candidate.sh --name NAME --email NOREPLY_EMAIL [--out DIR] [--with-working-tree]
#                                  [--license-holder "HOLDER"] [--drop PATHSPEC]...
#
# What it does, in DIR (default ../filescape-publication-candidate; refuses to reuse a folder):
#   1. a fresh clone of main (this repository is never modified);
#   2. --with-working-tree: the commit on refs/heads/backup/working-tree (tools/backup-snapshot.sh snapshot: the
#      uncommitted README, Windows workflow and ZIP quick-start edits) becomes the last commit, re-labelled as pending review;
#   3. --license-holder: adds the proposed MIT LICENSE as one more commit (DJ's decision; omit to leave it out);
#   4. the smallest identity cleanup: every author/committer email of the original owner is rewritten to NOREPLY_EMAIL
#      (and the name to NAME); commit messages, dates, files and the Claude co-author lines are unchanged;
#      the same email inside files (docs/publishing.md quoted it) is replaced in every commit's copy;
#   5. --drop PATHSPEC (optional, repeatable): removes those paths from every commit (e.g. the 2026-11-03 NuGet scans);
#   6. checks: same number of commits; trees identical except where a file was scrubbed or a path dropped; the old
#      email appears in no commit header, message or file; prints what a public push would expose (size, largest files, root notes).
# Hashes change (identity is part of a commit), so tags and links to old hashes must be re-pointed; the script lists the
# mapping in DIR/../filescape-publication-candidate-map.txt.
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
OLD_EMAIL="the original author email (removed)"
NAME="" EMAIL="" OUT="$(dirname "$REPO")/filescape-publication-candidate" WT=0 HOLDER="" DROPS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --name) NAME="$2"; shift 2 ;;
    --email) EMAIL="$2"; shift 2 ;;
    --out) OUT="$2"; shift 2 ;;
    --with-working-tree) WT=1; shift ;;
    --license-holder) HOLDER="$2"; shift 2 ;;
    --drop) DROPS+=("$2"); shift 2 ;;
    *) sed -n 2,22p "$0"; exit 2 ;;
  esac
done
[[ -n "$NAME" && -n "$EMAIL" ]] || { sed -n 2,22p "$0"; exit 2; }
[[ -e "$OUT" ]] && { echo "publication-candidate: $OUT exists; choose another --out or remove it yourself" >&2; exit 3; }

git clone -q --no-local --single-branch --branch main "$REPO" "$OUT"
cd "$OUT"
git remote remove origin
if [[ $WT == 1 ]]; then
  git fetch -q "$REPO" refs/heads/backup/working-tree:refs/tmp/wt
  [[ "$(git rev-parse refs/tmp/wt^)" == "$(git rev-parse main)" ]] || { echo "backup/working-tree is not on top of main: run tools/backup-snapshot.sh snapshot" >&2; exit 4; }
  files="$(git diff --name-only main refs/tmp/wt | sed 's/^/  /')"
  c="$(printf 'README, Windows workflow and the Windows ZIP quick start (Codex, pending review with DJ)\n\nThe working-tree edits at %s, committed for the publication candidate only:\n%s\n' "$(date -u +%F)" "$files" | GIT_AUTHOR_NAME="$NAME" GIT_AUTHOR_EMAIL="$EMAIL" GIT_COMMITTER_NAME="$NAME" GIT_COMMITTER_EMAIL="$EMAIL" git commit-tree "refs/tmp/wt^{tree}" -p main)"
  git update-ref refs/heads/main "$c"; git update-ref -d refs/tmp/wt; git reset -q --hard main
fi
if [[ -n "$HOLDER" ]]; then
  cat > LICENSE <<LIC
MIT License

Copyright (c) $(date +%Y) $HOLDER

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
LIC
  git add LICENSE
  GIT_AUTHOR_NAME="$NAME" GIT_AUTHOR_EMAIL="$EMAIL" GIT_COMMITTER_NAME="$NAME" GIT_COMMITTER_EMAIL="$EMAIL" git commit -q -m "LICENSE: MIT (proposed, DJ's decision)"
fi

git rev-list --reverse main > ../.pc-before
mapfile -t BEFORE < ../.pc-before
declare -A TREE; for c in "${BEFORE[@]}"; do TREE[$c]="$(git rev-parse "$c^{tree}")"; done

export OLD_EMAIL NAME EMAIL
# The old email also appears inside files (the checklist quoted it): replaced in every commit's copy of the file.
scrub='git grep --cached -l -F "$OLD_EMAIL" | while IFS= read -r f; do m=$(git ls-files -s -- "$f" | cut -d" " -f1); h=$(git cat-file blob ":$f" | sed "s/$OLD_EMAIL/the original author email (removed)/g" | git hash-object -w --stdin); git update-index --cacheinfo "$m,$h,$f"; done'
drop=""
if [[ ${#DROPS[@]} -gt 0 ]]; then q=""; for d in "${DROPS[@]}"; do q+=" $(printf '%q' "$d")"; done; drop="git rm -r -q --cached --ignore-unmatch --$q; "; fi
FILTER_BRANCH_SQUELCH_WARNING=1 git filter-branch -f --index-filter "$drop$scrub" --env-filter '
  if [ "$GIT_AUTHOR_EMAIL" = "$OLD_EMAIL" ]; then GIT_AUTHOR_EMAIL="$EMAIL"; GIT_AUTHOR_NAME="$NAME"; fi
  if [ "$GIT_COMMITTER_EMAIL" = "$OLD_EMAIL" ]; then GIT_COMMITTER_EMAIL="$EMAIL"; GIT_COMMITTER_NAME="$NAME"; fi
' -- main >/dev/null
git for-each-ref --format='%(refname)' refs/original | xargs -r -n1 git update-ref -d
git reflog expire --expire=now --all; git gc -q --prune=now

mapfile -t AFTER < <(git rev-list --reverse main)
[[ ${#AFTER[@]} -eq ${#BEFORE[@]} ]] || { echo "FAIL commit count ${#BEFORE[@]} -> ${#AFTER[@]}" >&2; exit 5; }
map="$(dirname "$OUT")/$(basename "$OUT")-map.txt"; : > "$map"
same=0
for i in "${!BEFORE[@]}"; do
  echo "${BEFORE[$i]} ${AFTER[$i]}" >> "$map"
  [[ "$(git rev-parse "${AFTER[$i]}^{tree}")" == "${TREE[${BEFORE[$i]}]}" ]] && same=$((same + 1))
done
rm -f ../.pc-before
left="$(git log main --format='%ae%n%ce%n%B' | grep -c -F "$OLD_EMAIL" || true)"
inblobs="$(git rev-list main | while read -r c; do git grep -l -F "$OLD_EMAIL" "$c" -- 2>/dev/null || true; done | wc -l)"
echo "candidate: $OUT"
echo "  commits: ${#AFTER[@]} (same as before); trees identical: $same of ${#AFTER[@]}${DROPS:+ (paths dropped: ${DROPS[*]})}"
echo "  old email in commit headers and messages: $left; in files of any commit: $inblobs"
echo "  (trees differ only where the email was scrubbed from a file or paths were dropped)"
echo "  identities now: $(git log main --format='%an <%ae>' | sort | uniq -c | tr '\n' ';')"
echo "  old -> new hash map: $map"
echo "  size of a push: $(git count-objects -vH | sed -n 's/size-pack: //p'); largest files at HEAD:"
git ls-tree -r -l main | sort -k4 -n -r | head -5 | awk '{printf "    %6.1f MB  %s\n", $4/1048576, $5}'
echo "  root notes at HEAD: $(git ls-tree --name-only main | grep -E '\.md$' | tr '\n' ' ')"
