#!/usr/bin/env bash
# Backup helpers for FileScape's history (2026-11-17, DJ: preserve how FileScape evolved; back it up off the machine).
#   tools/backup-snapshot.sh snapshot          commit the working tree as it is (tracked edits and untracked, unignored
#                                              files) onto refs/heads/backup/working-tree, without touching the index,
#                                              the working tree or main (a temporary index; main is its parent)
#   tools/backup-snapshot.sh bundle DIR        a git bundle of main, backup/* and every tag in DIR, verified and test-cloned
#   tools/backup-snapshot.sh push REMOTE_URL   push main, backup/* and tags to an (already created, PRIVATE) remote, then
#                                              clone it fresh and compare every ref's hash; needs DJ's credentials
# Agent worktree branches (worktree-*) are not backed up: each is merged into main (checked: 0 commits ahead).
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"; cd "$REPO"
refs() { echo refs/heads/main; git for-each-ref --format='%(refname)' refs/heads/backup refs/tags; }
case "${1:-}" in
  snapshot)
    idx="$(mktemp)"; trap 'rm -f "$idx"' EXIT
    cp .git/index "$idx"
    GIT_INDEX_FILE="$idx" git add -A
    tree="$(GIT_INDEX_FILE="$idx" git write-tree)"
    if [[ "$tree" == "$(git rev-parse main^{tree})" ]]; then echo "working tree matches main: nothing to snapshot"; exit 0; fi
    files="$(GIT_INDEX_FILE="$idx" git diff --cached --name-status main | sed 's/^/  /')"
    c="$(printf 'Working-tree snapshot %s (backup only, not reviewed)\n\nUncommitted edits on top of main %s:\n%s\n' "$(date -u +%FT%TZ)" "$(git rev-parse --short main)" "$files" | git commit-tree "$tree" -p main)"
    git update-ref refs/heads/backup/working-tree "$c"
    echo "backup/working-tree -> $c"; echo "$files" ;;
  bundle)
    dir="$2"; mkdir -p "$dir"; f="$dir/filescape-$(date +%Y%m%d-%H%M%S).bundle"
    git bundle create "$f" $(refs)
    git bundle verify "$f" >/dev/null
    t="$(mktemp -d)"; git clone -q --mirror "$f" "$t/c.git"
    for r in $(refs); do [[ "$(git rev-parse "$r")" == "$(git -C "$t/c.git" rev-parse "$r")" ]] || { echo "MISMATCH $r" >&2; exit 1; }; done
    echo "bundle $f: $(refs | wc -l) refs, $(git -C "$t/c.git" rev-list --all | wc -l) commits, test clone matches"; rm -rf "$t" ;;
  push)
    url="$2"
    git push "$url" $(refs | sed 's/.*/&:&/')
    t="$(mktemp -d)"; git clone -q --mirror "$url" "$t/c.git"
    for r in $(refs); do [[ "$(git rev-parse "$r")" == "$(git -C "$t/c.git" rev-parse "$r")" ]] || { echo "MISMATCH $r" >&2; exit 1; }; done
    echo "pushed and verified from a fresh clone: $(refs | wc -l) refs"; rm -rf "$t" ;;
  *) sed -n 2,9p "$0"; exit 2 ;;
esac
