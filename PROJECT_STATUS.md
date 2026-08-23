# Mega Robo Rally — Project Status & Operations Handbook

**Last updated:** 2026-08-22
**Target host:** `mrobopi` — Raspberry Pi 5, Debian 13 (trixie), aarch64, kernel 6.18.34

This is the practical document: how to rebuild the machine, how to run the parts, how to
run a game, what is broken, and what is left. For *why* the code is shaped the way it is,
see [API_DECOMPOSITION_DESIGN.md](API_DECOMPOSITION_DESIGN.md); for supervision detail see
[install/PROCESS_MANAGER.md](install/PROCESS_MANAGER.md).

> **Read this first.** The architecture was substantially rebuilt on 2026-08-22 — four
> projects, two processes, and changes to the turn planner, card handling and robot
> dispatch. **None of it has been exercised by a played turn.** It builds clean and the
> pieces were tested individually against the live database, but the game as a whole is
> unproven since the rework. Treat the first game as a test, and see §4.

---

## 1. Building a new copy (SD card from scratch)

### 1.1 Base OS

Raspberry Pi OS (Debian 13 trixie), 64-bit, on a Pi 5. Set the hostname to `mrobopi` — the
default connection string and the docs assume it. Create the user `mrr`.

### 1.2 Hardware interfaces

The Sense HAT LEDs are driven over SPI, and the game host **will not start** without the
device present.

```bash
# /boot/firmware/config.txt must contain:
dtparam=spi=on
# then reboot. A runtime "sudo dtoverlay spi0-2cs" works but does NOT survive a reboot.

ls /dev/spidev0.0        # must exist
```

The `mrr` user needs these groups (current machine has all of them):

```bash
sudo usermod -aG spi,gpio,i2c,dialout,sudo,adm mrr
```

### 1.3 .NET

.NET SDK 9 at `/home/mrr/.dotnet` (currently 9.0.317). It is a user-local install, so
system services must use the absolute path `/home/mrr/.dotnet/dotnet` — a systemd unit
does not read `.bashrc`.

```bash
~/.dotnet/dotnet --version      # expect 9.0.x
```

### 1.4 Database

MariaDB 11.8 (`mariadb.service`), running locally on the Pi.

> **The database is local.** Older notes say `server=mrobopi3`; that machine is gone. Use
> `localhost` or `mrobopi`.

```bash
sudo apt-get install -y mariadb-server
sudo systemctl enable --now mariadb

# user + schema
sudo mysql < install/userMRR.sql          # creates mrr@localhost, grants on rally.*
mysql -u mrr -p rally < install/MRRDatabase.sql   # 36 tables + seed data
mysql -u mrr -p rally < install/rallyBoards.sql   # the board library (~89 boards)
```

**Do not run `install/gameconfig.sql` on a fresh install.** It is not a provisioning
script — it starts a specific test game and will overwrite `CurrentGameData`.

The schema is **tables only**: no stored procedures, functions, triggers or views. All that
logic lives in C#. Do not add database-side logic.

### 1.5 Build

```bash
cd ~/Mega-Robo-Rally-SignalR
~/.dotnet/dotnet build Mega-Robo-Rally-SignalR.sln
```

Expect **0 errors, 0 warnings**. A build takes roughly 40s–2min on the Pi.

### 1.6 Install as services (optional but recommended)

```bash
cd install/service
sudo ./install.sh          # units, scripts, /etc/default/mrr, sudoers, deploy, enable
```

Installs `mrr-server.service` (game, :5000), `mrr-config.service` (authoring, :5001), the
SPI loader, and the health/recover watchdog timers. See §2.2.

---

## 2. Running each part

Four projects, two processes:

| Project | What it is | Runs as |
|---|---|---|
| `MRR.Contracts` | Models and DTOs. No dependencies at all. | library |
| `MRR.Rules` | The turn planner. References Contracts *only* — it cannot reach a database, enforced by the build. | library |
| `MRR` | Game host — state machine, executor, robot sockets, SignalR, GM panel, admin. | `mrr-server.service` **:5000** |
| `MRR.Config` | Board and GameData authoring. | `mrr-config.service` **:5001** |

### 2.1 Development (by hand)

```bash
# game host
~/.dotnet/dotnet run --project MRR/MRR.csproj

# authoring host (independent; safe to run or not run)
~/.dotnet/dotnet run --project MRR.Config/MRR.Config.csproj
```

Stop the services first — both want the same ports. To stop a hand-started host safely:

```bash
kill $(ss -ltnp | grep ':5000' | sed -E 's/.*pid=([0-9]+).*/\1/')
```

> Do **not** use `pkill -f MRR...` — the pattern matches the shell running it and kills your
> own session. That happened during development and corrupted the git object database.

### 2.2 Production (`mrrctl`)

```bash
mrrctl status              # state, freezer, pid, memory, live health probe
mrrctl start | stop | restart      # the GAME host by default
mrrctl restart config      # just the board editor
mrrctl restart all         # both
mrrctl logs -f             # journal for all mrr-* units
mrrctl deploy [game|config|all]    # publish Release, keep previous, restart
mrrctl rollback [game|config]
```

A bare verb means the **game host**, not both — restarting the editor because you restarted
the game is harmless; the reverse loses a turn.

**`mrrctl pause` freezes the process. Do not use it mid-turn** — already-sent robot commands
keep running while the dispatch loop is suspended, so the board drifts out of step with
what the process resumes believing. Use `POST /api/execution/abort` instead (§3.4).

### 2.3 URLs

| Address | What |
|---|---|
| `http://mrobopi:5000/` | Player programming UI (phones) |
| `http://mrobopi:5000/gmindex.html` | GM panel |
| `http://mrobopi:5000/api/health` | Game host liveness |
| `http://mrobopi:5001/` | Board editor |
| `http://mrobopi:5001/api/health` | Authoring host liveness |
| `http://127.0.0.1:5000/api/admin/…` | Admin — **loopback only**, see §6.3 |

### 2.4 Exact commands

> **As of 2026-08-22 the services are NOT installed on `mrobopi`** — `mrrctl` is not on
> PATH and no `mrr-*` units exist. Use **Mode A**. Mode B works only after
> `sudo install/service/install.sh` has been run (§1.6).

Everything below assumes:

```bash
cd ~/Mega-Robo-Rally-SignalR
```

#### Mode A — by hand (works today)

**0. Check the database is up.** Nothing else will work without it.

```bash
systemctl is-active mariadb                       # expect: active
mysql -h localhost -u mrr -prallypass rally -e "SELECT COUNT(*) FROM Boards;"
```

**1. Build.** Expect `0 Error(s)`, `0 Warning(s)`.

```bash
~/.dotnet/dotnet build Mega-Robo-Rally-SignalR.sln
```

**2. Start the game host** (phones, GM panel, robots, SignalR) — port 5000:

```bash
~/.dotnet/dotnet run --project MRR/MRR.csproj
```

Leave that terminal open. To run it in the background instead:

```bash
nohup ~/.dotnet/dotnet run --project MRR/MRR.csproj > /tmp/mrr-game.log 2>&1 &
```

**3. Start the board editor** (optional, independent) — port 5001, second terminal:

```bash
~/.dotnet/dotnet run --project MRR.Config/MRR.Config.csproj
```

```bash
nohup ~/.dotnet/dotnet run --project MRR.Config/MRR.Config.csproj > /tmp/mrr-config.log 2>&1 &
```

**4. Confirm both are answering.**

```bash
curl -s http://127.0.0.1:5000/api/health     # {"status":"ok","state":4}
curl -s http://127.0.0.1:5001/api/health     # {"status":"ok","role":"config"}
```

`state` is the current game state (§3.2). If a host does not answer within ~30s, read its
log: `tail -40 /tmp/mrr-game.log`.

**5. Stop them.** By listening port — never by name, see the warning below.

```bash
kill $(ss -ltnp | grep ':5000' | sed -E 's/.*pid=([0-9]+).*/\1/')   # game host
kill $(ss -ltnp | grep ':5001' | sed -E 's/.*pid=([0-9]+).*/\1/')   # board editor
```

> **Never `pkill -f MRR` or `pkill -f dotnet`.** The pattern matches the shell you type it
> in, so it kills your own session. During development that happened mid-`git commit` and
> corrupted the git object database. Always kill by port, as above.

#### Mode B — as services (after install.sh)

```bash
mrrctl status                 # game host: state, pid, memory, live health probe
mrrctl status config          # board editor

mrrctl start                  # start the GAME host
mrrctl start all              # start both hosts
mrrctl restart                # restart the GAME host only
mrrctl restart config         # restart the board editor only
mrrctl stop all               # stop both; they stay down until started

mrrctl logs -f                # follow the journal for all mrr-* units
mrrctl logs 200               # last 200 lines

mrrctl deploy all             # publish Release for both, restart if running
mrrctl rollback game          # revert the game host to its previous build
```

A bare verb means the **game host**, not both.

> **Do not `mrrctl pause` during a turn.** It freezes the process while already-sent robot
> commands keep running, so the board ends up out of step with what the process believes.
> Use the abort endpoint (§3.4) instead.

#### Everyday one-liners

```bash
# what is the game doing right now?
curl -s http://127.0.0.1:5000/api/health

# in-memory state vs the database (loopback only)
curl -s http://127.0.0.1:5000/api/admin/diagnostics

# stop a turn that is going wrong
curl -s -X POST http://127.0.0.1:5000/api/execution/abort

# check a board before using it
curl -s http://127.0.0.1:5001/api/boardeditor/7/validate

# turn the robot touchscreens on / off
curl -s "http://127.0.0.1:5000/api/settings/robot-screen?enabled=true"

# back up the database
mysqldump -u mrr -prallypass rally > ~/rally-$(date +%F).sql

# back up the code (the SD card is a single point of failure)
git bundle create ~/mrr-$(date +%F).bundle --all && git fsck --full
```

#### From another machine

Replace `127.0.0.1` with `mrobopi`. Two exceptions:

- **Admin routes are loopback-only** and will return 403. Tunnel instead:
  ```bash
  ssh -L 5000:127.0.0.1:5000 mrr@mrobopi
  ```
- Phones just open `http://mrobopi:5000/` in a browser; nothing to install.

---

---

## 3. Game setup and execution

### 3.1 Before a game

1. **Robots on and on the network.** Check `RobotBases.IPAddress` matches reality; the
   discovered addresses are recorded in [install/notes.txt](install/notes.txt).
2. **Pick a board.** Validate it first — 12 of 89 boards currently fail (§4.2):
   ```
   curl http://mrobopi:5001/api/boardeditor/{boardId}/validate
   ```
3. **Assign the board to a GameData row** in the board editor.
4. **Activate the game** from the GM panel's game selection. This is the *only* supported
   way — the editor no longer activates a game as a side effect of saving.
5. **Start the game** (GM panel), which populates `Robots` from `OperatorData` and places
   robots on their start squares.

### 3.2 The turn cycle

The GM panel drives a state machine. States that matter:

| State | Meaning |
|---|---|
| 0 | Start game / init |
| 2 | Reset, shuffle, deal cards |
| 3 | Verify robot positions |
| 4 | **Wait for players to program** |
| 5 | Lock programs |
| 6 | Plan the turn (build the command list) |
| 7 | Run phase — waiting |
| 8 | Run phase — in progress |
| 9–11 | Run-phase sub-states |
| 12 | Next turn → back to 2 |
| 13–14 | Exit / reset → 0 |
| 15 | Recreate program → back to 4 |
| 16 | Reload positions → back to 3 |

A turn is: deal (2) → verify (3) → program (4) → lock (5) → **plan (6)** → **execute (7/8)**
→ next turn (12).

At state 6 the planner produces the whole turn's commands, they are written to
`CommandList`, and only then does the state advance to 7. That ordering is deliberate: the
stored command list is what lets a turn resume after a restart.

### 3.3 Players

Phones open `http://mrobopi:5000/` and see their hand and five registers. If robot
touchscreens are enabled (`GET /api/settings/robot-screen?enabled=true`) players can also
program from the robot's own LCD.

### 3.4 When a turn goes wrong

```bash
curl -X POST http://mrobopi:5000/api/execution/abort
```

Stops dispatch. Commands already sent cannot be recalled — the robot is physically moving.
Then choose recovery from the GM panel: **Reload Position (state 16)** to put robots back
where the turn started, or **Create Program (state 15)** to reprogram.

---

## 4. Known issues

### 4.1 Nothing has been played since the rework — the big one

The planner (`MRR.Rules`), the pre-drawn Spam deck, the debounced SignalR publishing and the
robot dispatch changes are all new as of 2026-08-22 and **have never run a real turn**. The
riskiest area is Spam resolution: replacement cards are now drawn up front by Master rather
than pulled from the database mid-simulation. Watch the first game closely, particularly a
turn where someone plays a Spam card.

### 4.2 Twelve of 89 boards are unplayable

- **6 boards have gaps in flag numbering** (board 3 is `1,4`; board 42 is `1,2,4`). A robot
  only advances when it reaches `LastFlag + 1`, so nothing bridges a gap and the board
  cannot be won. Boards: **3, 21, 42, 50, 70, 82**.
- **6 boards have duplicate player start positions** (two robots assigned the same square).
  Boards: **20, 40, 41, 59, 67, 71**.

All 6 gap boards are `GameType 1` (KingOfTheHill), where the numbering may be deliberate —
worth checking before "fixing" them. Validate any board before using it.

### 4.3 Every phone can see every player's hand

`index.html` pulls in `loadrobots.js`, which renders all robots and can display any
player's dealt cards. Passwords are no longer broadcast (fixed), but hands still are.
Fixing it needs per-seat SignalR groups plus phone-UI changes.

### 4.4 Winning does not end the game

`CreateCommands.AddFlag` correctly detects a win, but only posts a `"Game Winner:"`
message — `SquareAction.GameWinner` is commented out, so play continues.

### 4.5 `RefreshPlayerCards` does nothing

Its body is disabled by an early `return`, yet it has seven callers that read as though
they refresh card state. Probably redundant (the sync moved inline into `UpdateCardPlayed`)
but it should be deleted or restored, not left as a silent no-op.

### 4.6 Robot 6 has no address

`RobotBases` row 6 is a placeholder (`192.168.1.` / `AIM-??`). Harmless — the connect
attempt fails and is logged — but that seat cannot use a physical robot.

### 4.7 The database password is in a tracked file

`MRR/appsettings.json` and `MRR.Config/appsettings.json` contain `pwd=rallypass` and are
committed. Consider moving to `appsettings.Production.json` outside the repo before making
the repository public.

### 4.8 No automated tests

There is no test project. Every change is verified by building and, ultimately, by playing.
This is a deliberate decision, but it is why §4.1 matters so much.

---

## 5. Remaining steps

### 5.1 Architecture (see API_DECOMPOSITION_DESIGN.md §9)

| Step | Status |
|---|---|
| 0. Contracts project | **Done** |
| 1. Split `Player` into `PlayerState` + transport | **Done** |
| 2. Config host out | **Done** |
| 3. Purify Rules (`TurnRequest` → `TurnPlan`) | **Done** |
| 4. Split `DataService` | **Partial** — `SqlGateway` and `GameStateStore` extracted, concerns separated into partials. `RuleEffects` and the repositories remain |
| 5. Admin API | **Done** |
| 6. Device Gateway | **Partial** — dispatch bugs fixed; `IRobotTransport` remains |
| 7. Presentation | **Partial** — password leak, broadcast storm and `/` 404 fixed; per-seat groups remain |

The largest remaining piece is **`RuleEffects`**: `ProcessDbCommand` is ~25 cases each doing
"mutate in-memory state, then mirror to SQL", and the design wants it shared so the planner
and executor cannot disagree about rules. It sits on the executor's hot path, so it wants
tests or a very careful pass.

### 5.2 Game mechanics

`install/todo.md` is the authoritative list: **39 unstarted, 5 partial**. The notable ones
are reboot, pushers, merge conveyors, the damage-card draw mechanic, shutdown, and ending
the game on a win (§4.4). This is a larger body of work than what is left of the
architecture.

### 5.3 Housekeeping

- Push the branch. `pre-decomposition-cleanup` is well ahead of `origin` and the name stopped
  being accurate long ago — consider renaming it.
- Re-run `install/service/install.sh` on any machine that has the old layout: deploy
  directories moved from `/srv/mrr/app` to `/srv/mrr/game` and `/srv/mrr/config`.
- Decide about `RefreshPlayerCards` (§4.5) and the 12 invalid boards (§4.2).

---

## 6. Things worth keeping track of

### 6.1 Where the rules for the code live

`CLAUDE.md` is loaded automatically by Claude Code and carries the standing constraints:
Renegade rules only, no database-side logic, one game-wide `TotalFlags`, never hardcode the
hostname. Keep it accurate — it is the first thing any assistant reads.

### 6.2 Key invariants that are easy to break

- **`TotalFlags` is per game, not per player.** It lives in `CurrentGameData` iKey 7, taken
  from the board at game start. A `Player.TotalFlags` hardcoded to 5 caused every non-5-flag
  board to score wrong.
- **The `iKey` numbers in `CurrentGameData` are schema.** `GameStateStore` switches on them.
  Do not renumber.
- **`Direction` and `SquareAction` values are persisted.** Do not renumber those either.
- **`MRR.Rules` must never gain a `PackageReference`.** Its inability to reach a database is
  the guarantee the whole four-project split exists to provide.

### 6.3 Admin API

Loopback-only by design. From another machine, tunnel:

```bash
ssh -L 5000:127.0.0.1:5000 mrr@mrobopi
```

Every statement is audited to `admin-audit.log` beside the deployed binary, with caller,
turn, phase and rows affected. Every write reloads game state and republishes to the phones
— that is what makes hand-editing safe mid-game.

```
GET  /api/admin/tables               GET  /api/admin/sql/history
GET  /api/admin/tables/{name}?filter= GET  /api/admin/diagnostics
POST /api/admin/tables/{name}        POST /api/admin/sql
```

`/api/admin/diagnostics` reports drift between in-memory state and the database — the first
thing to check if the game behaves as though it has stale data.

### 6.4 Backups

- **Database:** `mysqldump -u mrr -p rally > rally-$(date +%F).sql`. Nothing automated.
- **Code:** the git repo corrupted once during development (a `git commit` killed
  mid-write). A verified bundle is at `~/mrr-recovery-backup/`. Recreate with:
  ```bash
  git bundle create ~/mrr-backup-$(date +%F).bundle --all
  git fsck --full        # check integrity
  ```
- **Pushing to GitHub is the real backup.** The Pi's SD card is a single point of failure.

### 6.5 Robot inventory

`RobotBases` maps `RobotBaseID` → robot. Numbering lines up end to end:
`RobotID N` → `RobotBaseID N` → `AIM-0N`. Addresses are in
[install/notes.txt](install/notes.txt); `AIMID` is the hardware identifier, `AIMName` the
label on the robot.

### 6.6 Diagnosing a bad start

| Symptom | Look at |
|---|---|
| Host will not start | `mrrctl logs \| tail -50` — the preflight line names the blocker |
| `/dev/spidev0.0 missing` | `dtparam=spi=on` in config.txt, then reboot |
| Robots do not connect | `RobotBases.IPAddress`, robots powered and on the right network |
| Odd startup crash / NRE | Check the database connection first. `SqlGateway` swallows database errors and returns empty results, so a bad connection string surfaces later as a null reference somewhere unrelated |
| Game acts on stale data | `GET /api/admin/diagnostics` for memory-vs-database drift |
| A turn hangs | A robot stopped responding; commands now time out after 30s and are logged. `POST /api/execution/abort` to stop the turn |
