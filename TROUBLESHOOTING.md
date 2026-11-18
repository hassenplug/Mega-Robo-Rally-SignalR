# Troubleshooting

Fixes for problems on the game Pi (`mrobopi`) that are not bugs in the game itself.
Run commands as user `mrr` from `/home/mrr/Mega-Robo-Rally-SignalR` unless a step says otherwise.

- [Git: "object file ... is empty"](#git-object-file--is-empty)
  - [After the script](#after-the-script)
  - [Branch has diverged](#branch-has-diverged)
  - [Still broken after the script](#still-broken-after-the-script)
  - [Empty branch file](#empty-branch-file)
  - [Other git errors after a power loss](#other-git-errors-after-a-power-loss)
  - [Undo the repair](#undo-the-repair)
  - [Prevention](#prevention)

---

## Git: "object file ... is empty"

### Symptoms

Any git command (or VS Code's Source Control panel) shows one or more of:

```
error: object file .git/objects/bb/404a1aed39a79285f96d282a98d57f837f5d58 is empty
Your branch is based on 'origin/main', but the upstream is gone.
fatal: loose object 1289ca6b... (stored in .git/objects/12/89ca6b...) is corrupt
fatal: bad object refs/remotes/origin/main
```

`git fsck` lists many `object corrupt or missing` lines.

### Cause

The Pi lost power, or was unplugged, while git was writing files. Usually this happens during
a `git pull` or `git fetch`, or a background fetch by VS Code. Git had created the object
files but the SD card never got their contents, so they are left as **zero-byte files**. When
the newest commit from GitHub is one of them, `origin/main` points at nothing, and git reports
that "the upstream is gone".

You can confirm the power loss with `sudo dmesg | grep -i "orphan cleanup"` after the next
boot. That line means the SD card was not unmounted cleanly.

Your own committed work is normally fine. The damaged files are the ones git was writing at
the moment the power went, and GitHub still has copies of them.

### Fix

```bash
cd /home/mrr/Mega-Robo-Rally-SignalR
./install/fix-git-objects.sh --dry-run   # look first: lists what is wrong, changes nothing
./install/fix-git-objects.sh             # repair
```

The script ([install/fix-git-objects.sh](install/fix-git-objects.sh)):

1. Stops if a git command is running, or if a local branch file is empty (see
   [Empty branch file](#empty-branch-file)).
2. Backs up the whole `.git` folder to `~/git-backups/<repo>-git-<time>.tar.gz` (about 90 MB).
3. Moves every empty file under `.git/objects` to `~/git-backups/<repo>-empty-<time>/`. It
   also moves an empty `FETCH_HEAD`, `ORIG_HEAD` or remote-tracking ref. Nothing is deleted.
4. Runs `git fetch origin --prune` to download the lost objects again. **This needs the
   internet.** If the Pi is on the game LAN with no uplink, connect it first.
5. Runs `git fsck --full`, then shows how your branch compares with GitHub.

It never pulls, merges, resets or pushes, and it never touches your working files or
uncommitted changes. If VS Code is open on the repo, close it first, because its background
fetch can hold `.git/index.lock`.

A good run ends like this:

```
==> verifying with git fsck --full
git fsck: no errors

==> branch status
## main...origin/main [behind 5]
5 commit(s) behind origin/main - get them with: git pull --ff-only
```

### After the script

The last lines say what to do next:

| Script says | Meaning | Do |
|---|---|---|
| `in step with origin/main` | Nothing to do | — |
| `N commit(s) behind` | GitHub has newer commits (often the pull the power loss interrupted) | `git pull --ff-only` |
| `N local commit(s) not on GitHub yet` | You have commits that were never pushed | `git push` |
| `diverged` | Both sides have commits the other lacks | See [Branch has diverged](#branch-has-diverged) |

If the pull brings in new code for the game server, deploy it between games with
`mrrctl deploy game`.

### Branch has diverged

Both your Pi and GitHub have commits the other doesn't have. Look before merging:

```bash
git log --oneline HEAD..origin/main   # on GitHub only
git log --oneline origin/main..HEAD   # on this Pi only
git pull --no-rebase                  # merge them; fix any conflicts, then commit
git push
```

If the commits that exist only on this Pi are throwaway, ask the person who owns the repo
before discarding them. `git reset --hard origin/main` throws them away for good.

### Still broken after the script

`git fsck` still lists `missing` or `corrupt` objects after a successful fetch. That means
the damaged objects were never pushed, so GitHub doesn't have them either.

1. **If the files on disk are still correct** (the usual case), recreate the missing file
   contents from them:
   ```bash
   git ls-files -z | xargs -0 git hash-object -w >/dev/null
   git fsck --full
   ```
   This writes every tracked file's current contents back into git. It restores the objects
   only if a file still matches what was committed.
2. **If that doesn't clear it**, start from a fresh copy and bring your uncommitted work across:
   ```bash
   cd /home/mrr
   mv Mega-Robo-Rally-SignalR Mega-Robo-Rally-SignalR.broken
   git clone https://github.com/hassenplug/Mega-Robo-Rally-SignalR.git
   # copy any changed/new files from Mega-Robo-Rally-SignalR.broken/ into the new clone
   ```
   Commits that existed only on the Pi are lost this way. Copy their files across by hand.
   Keep the `.broken` folder until you have checked everything. The services find the repo
   by its path (`MRR_REPO` in `/etc/default/mrr`), so the new clone must use the same folder
   name.

### Empty branch file

The script stops with "a local branch or tag file is empty". It won't guess which commit a
branch should point at. The branch's history log usually still has it:

```bash
tail -1 .git/logs/refs/heads/main   # second column is the commit the branch last pointed to
git cat-file -t <that-commit>       # must print "commit"
echo <that-commit> > .git/refs/heads/main
./install/fix-git-objects.sh
```

If `.git/HEAD` itself is empty, run `echo 'ref: refs/heads/main' > .git/HEAD`.

### Other git errors after a power loss

| Error | Fix |
|---|---|
| `index file smaller than expected`, `bad index file sha1 signature`, `index file corrupt` | `mv .git/index ~/git-backups/index.broken && git reset`. This rebuilds the index from the last commit. Your working files are kept, but anything that was staged is unstaged. |
| `Unable to create '.../.git/index.lock': File exists` | Make sure no git is running (`pgrep -a git`), close VS Code, then `rm .git/index.lock` |
| `fatal: unable to access 'https://github.com/...'` | No internet or no GitHub sign-in. The repair can't fetch until this works. Check with `git ls-remote origin`. |

### Undo the repair

The script keeps the original `.git`, so you can go back to exactly how things were:

```bash
cd /home/mrr/Mega-Robo-Rally-SignalR
mv .git .git.repaired
tar -xzf ~/git-backups/Mega-Robo-Rally-SignalR-git-<time>.tar.gz
```

Once git has worked normally for a while, delete the old copies in `~/git-backups/`.

### Prevention

- **Shut the Pi down before unplugging it:** `sudo shutdown -h now`, then wait for the green
  activity light to stop. Turning off the power strip at the end of a session is the usual
  way this happens.
- Push after committing, so GitHub always has a copy of your work.
- If it keeps happening without a power loss, check the power supply (`dmesg | grep -i
  voltage` shows under-voltage warnings) and consider replacing the SD card.

### History

- **2026-11-17:** the Pi was turned off mid-pull. That left 34 empty object files, including
  `bb404a1` (the latest GitHub commit), plus an empty `FETCH_HEAD`, and git reported "the
  upstream is gone". Fixed with `install/fix-git-objects.sh`. The fetch restored every
  object, `git fsck` was clean, and the branch was 5 commits behind GitHub.
