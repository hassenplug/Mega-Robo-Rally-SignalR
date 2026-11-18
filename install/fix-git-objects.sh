#!/bin/bash
# install/fix-git-objects.sh - repair the repo after a power loss left empty git object files.
#
# Symptoms (see TROUBLESHOOTING.md, "Git: object file ... is empty"):
#   error: object file .git/objects/bb/404a1a... is empty
#   Your branch is based on 'origin/main', but the upstream is gone.
#   fatal: loose object ... is corrupt   /   fatal: bad object refs/remotes/origin/main
#
# Usage: ./install/fix-git-objects.sh [--dry-run] [repo-dir]     (run as user mrr, not sudo)
#   --dry-run  only report what is wrong and what would be done; change nothing.
#   repo-dir   defaults to the repo this script is in.
#
# What it does:
#   1. Stops if a git command is running (.git/index.lock), or a local branch file is empty
#      (that needs the manual steps in TROUBLESHOOTING.md).
#   2. Backs up the whole .git folder to ~/git-backups/<repo>-git-<time>.tar.gz.
#   3. Moves every empty file under .git/objects, and an empty FETCH_HEAD / ORIG_HEAD or
#      remote-tracking ref, into ~/git-backups/<repo>-empty-<time>/ (moved, not deleted).
#   4. git fetch origin --prune: downloads again every lost object GitHub has.
#   5. git fsck --full, then shows how the branch compares with GitHub.
#
# It never pulls, merges, resets, pushes, or touches your working files or uncommitted changes.
set -euo pipefail

DRY_RUN=no
REPO=""
for a in "$@"; do
    case "$a" in
        --dry-run) DRY_RUN=yes ;;
        -h|--help) sed -n '2,/^set -euo/p' "$0" | sed '$d'; exit 0 ;;
        -*) echo "unknown option: $a (try --help)" >&2; exit 2 ;;
        *)  REPO="$a" ;;
    esac
done
[ -n "$REPO" ] || REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO="$(cd "$REPO" && pwd)"
GIT_DIR="$REPO/.git"
NAME="$(basename "$REPO")"
STAMP="$(date +%Y%m%d-%H%M%S)"
BACKUP_DIR="$HOME/git-backups"
QUARANTINE="$BACKUP_DIR/$NAME-empty-$STAMP"

info() { echo; echo "==> $*"; }
warn() { echo "!! $*" >&2; }
die()  { echo "FAILED: $*" >&2; exit 1; }

# ---------------------------------------------------------------- preconditions
[ "$(id -u)" -ne 0 ] || die "run as the repo's owner (mrr), not root - files written as root break git for mrr"
[ -d "$GIT_DIR/objects" ] || die "$REPO is not a git repo (no .git/objects)"
[ ! -e "$GIT_DIR/index.lock" ] || die "$GIT_DIR/index.lock exists: a git command (VS Code?) is running. Close it and retry; if nothing is running, see TROUBLESHOOTING.md"
cd "$REPO"

# ---------------------------------------------------------------- find the damage
mapfile -t EMPTY_OBJECTS < <(find "$GIT_DIR/objects" -type f -empty | sort)
mapfile -t EMPTY_REMOTE_REFS < <(find "$GIT_DIR/refs/remotes" -type f -empty 2>/dev/null | sort)
mapfile -t EMPTY_LOCAL_REFS < <(find "$GIT_DIR/refs/heads" "$GIT_DIR/refs/tags" -type f -empty 2>/dev/null | sort)
EMPTY_MISC=()
for f in FETCH_HEAD ORIG_HEAD; do [ -f "$GIT_DIR/$f" ] && [ ! -s "$GIT_DIR/$f" ] && EMPTY_MISC+=("$GIT_DIR/$f"); done
[ -s "$GIT_DIR/HEAD" ] || die ".git/HEAD is empty - see TROUBLESHOOTING.md (fix by hand: echo 'ref: refs/heads/main' > .git/HEAD)"

info "checking $REPO"
echo "empty object files:          ${#EMPTY_OBJECTS[@]}"
echo "empty remote-tracking refs:  ${#EMPTY_REMOTE_REFS[@]}"
echo "empty FETCH_HEAD/ORIG_HEAD:  ${#EMPTY_MISC[@]}"
echo "empty local branch/tag refs: ${#EMPTY_LOCAL_REFS[@]}"

if [ "${#EMPTY_LOCAL_REFS[@]}" -gt 0 ]; then
    printf '   %s\n' "${EMPTY_LOCAL_REFS[@]}" >&2
    die "a local branch or tag file is empty. This script won't guess its commit - follow 'Empty branch file' in TROUBLESHOOTING.md"
fi

# Does the checked-out commit still have everything it needs? (Informational: fetch can only
# bring back objects GitHub has, so a commit that was never pushed can't be restored this way.)
if git rev-list --objects HEAD >/dev/null 2>&1; then
    echo "current commit $(git rev-parse --short HEAD) and its history: intact"
    HEAD_OK=yes
else
    warn "objects needed by the current commit are damaged - fetch will restore them only if that commit was pushed"
    HEAD_OK=no
fi

TO_MOVE=("${EMPTY_OBJECTS[@]}" "${EMPTY_REMOTE_REFS[@]}" "${EMPTY_MISC[@]}")
if [ "${#TO_MOVE[@]}" -eq 0 ]; then
    info "no empty git files found - nothing to repair here"
    echo "If git still reports errors, see TROUBLESHOOTING.md (index, network or other problems)."
    exit 0
fi

if [ "$DRY_RUN" = yes ]; then
    info "dry run - would move these ${#TO_MOVE[@]} empty files to $QUARANTINE:"
    printf '   %s\n' "${TO_MOVE[@]#$REPO/}"
    echo "then: back up .git to $BACKUP_DIR/$NAME-git-$STAMP.tar.gz, git fetch origin --prune, git fsck --full"
    exit 0
fi

# ---------------------------------------------------------------- back up, then repair
mkdir -p "$BACKUP_DIR"
info "backing up .git to $BACKUP_DIR/$NAME-git-$STAMP.tar.gz"
tar -czf "$BACKUP_DIR/$NAME-git-$STAMP.tar.gz" -C "$REPO" .git
echo "backup: $(du -h "$BACKUP_DIR/$NAME-git-$STAMP.tar.gz" | cut -f1)"

info "moving ${#TO_MOVE[@]} empty files to $QUARANTINE"
for f in "${TO_MOVE[@]}"; do
    rel="${f#$REPO/}"
    mkdir -p "$QUARANTINE/$(dirname "$rel")"
    mv "$f" "$QUARANTINE/$rel"
done

info "fetching from origin to restore the lost objects"
if ! git fetch origin --prune; then
    warn "git fetch failed (no network, or GitHub sign-in). The empty files are already moved aside;"
    warn "run 'git fetch origin --prune' again once online, then 'git fsck --full'."
    exit 1
fi

# ---------------------------------------------------------------- verify
info "verifying with git fsck --full"
FSCK_OUT="$(git fsck --full --no-progress 2>&1 || true)"
# dangling/unreachable objects are normal leftovers, not damage.
PROBLEMS="$(printf '%s\n' "$FSCK_OUT" | grep -vE '^(dangling|unreachable) ' | grep -v '^$' || true)"
if [ -z "$PROBLEMS" ]; then
    echo "git fsck: no errors"
else
    printf '%s\n' "$PROBLEMS" | head -20 >&2
    warn "git fsck still reports problems (first 20 lines above) - see 'Still broken after the script' in TROUBLESHOOTING.md"
fi

info "branch status"
git status -sb | head -1
UPSTREAM="$(git rev-parse --abbrev-ref '@{upstream}' 2>/dev/null || true)"
if [ -n "$UPSTREAM" ]; then
    read -r AHEAD BEHIND < <(git rev-list --left-right --count "HEAD...$UPSTREAM")
    if [ "$AHEAD" = 0 ] && [ "$BEHIND" = 0 ]; then
        echo "in step with $UPSTREAM"
    elif [ "$AHEAD" = 0 ]; then
        echo "$BEHIND commit(s) behind $UPSTREAM - get them with: git pull --ff-only"
    elif [ "$BEHIND" = 0 ]; then
        echo "$AHEAD local commit(s) not on GitHub yet - send them with: git push"
    else
        echo "diverged: $AHEAD local and $BEHIND GitHub commit(s) differ - see 'Branch has diverged' in TROUBLESHOOTING.md"
    fi
fi
[ "$HEAD_OK" = yes ] || git rev-list --objects HEAD >/dev/null 2>&1 || \
    warn "the current commit is still missing objects - see 'Still broken after the script' in TROUBLESHOOTING.md"

cat <<EOF

Done. Kept for safety (delete once everything works):
  $BACKUP_DIR/$NAME-git-$STAMP.tar.gz   (the whole .git as it was)
  $QUARANTINE/   (the empty files that were moved)
EOF
