# MRR Process Manager — Design

**Status:** design + reference implementation in [install/service/](service/)
**Target host:** `mrobopi` (Raspberry Pi 5, Debian 13 trixie, systemd 257)
**Last updated:** 2026-08-21

---

## 1. Problem

Today the game server is started by hand:

```
mrr  2471  dotnet run                                    # in a VS Code terminal
mrr  2538  /home/mrr/Mega-Robo-Rally-SignalR/MRR/bin/Debug/net9.0/MRR
```

That means: nothing runs after a power cycle, a crash ends game night, and there is
no way to stop/restart the server except finding the terminal it was launched from.

### Requirements

| # | Requirement | Notes |
|---|---|---|
| R1 | Start automatically when the Pi boots | Before any phone or robot connects |
| R2 | Restart a process that **crashes** | e.g. the `spidev` `IOException`, an unhandled task exception |
| R3 | Restart a process that **shuts down cleanly** | Exit code 0 must also be treated as "should be running" |
| R4 | Operator can **pause**, **stop**, **restart** | From an SSH shell, one command each |
| R5 | Handle more than one process | Sense HAT service, future side-cars, all managed as a group |
| R6 | Survive a **hang** (process alive, not serving) | A crash-only policy misses this |
| R7 | Never end up permanently dead | A crash loop must not latch off forever |

---

## 2. Approach: systemd, not a custom supervisor

| Option | Verdict |
|---|---|
| **systemd units** | **Chosen.** Already PID 1 on the Pi. Boot ordering, restart policy, `cgroup` freezer for pause, journald log capture, and dependency on `mariadb.service` are all declarative. Zero new runtime processes. |
| `supervisord` | Adds a Python daemon that itself needs a systemd unit to survive boot, and has no way to express "after MariaDB is up". Pure overhead here. |
| Custom C# manager | A supervisor that can crash is the thing you least want to write. Would also need its own systemd unit — so systemd is in the picture regardless. |
| `pm2` / Docker | Node runtime / container overhead on a Pi, and SPI + `/dev/spidev` device passthrough for containers is extra friction for zero gain. |

The design is therefore **a set of systemd units + one operator CLI (`mrrctl`) that
wraps `systemctl` in game-night vocabulary**, plus two small watchdog timers that cover
the cases stock systemd can't see (hang, latched crash-loop).

---

## 3. Architecture

```
multi-user.target                          (normal boot)
└── mrr.target                             ← ENABLED; the "game group" (R1, R5)
    ├── mrr-server.service                 ← game host, :5000 (PartOf the target)
    │     Requires  mariadb.service        ← DB must be up
    │     After     network-online.target
    │     Wants     mrr-spi.service         ← oneshot: load SPI overlay if missing
    │     Restart=always / RestartSec=5     (R2, R3)
    ├── mrr-config.service                 ← authoring host, :5001 (NOT PartOf — §10.1)
    │     Requires  mariadb.service
    │     no SPI, no dependency on the game host
    ├── mrr-health.timer  → mrr-health.service    every 30 s: one probe per host (R6)
    └── mrr-recover.timer → mrr-recover.service   every 2 min: un-latch a failed unit (R7)

/usr/local/bin/mrrctl        operator CLI (R4)
/usr/local/bin/mrr-preflight ExecStartPre gate, per role: DB? spidev (game only)? port free?
/etc/default/mrr             all tunables in one file
/srv/mrr/game                deployed game host — separate from the git repo
/srv/mrr/config              deployed authoring host
/srv/mrr/{game,config}.previous   previous deploys, for `mrrctl rollback <role>`
```

Managed services declare `PartOf=mrr.target`, so `stop`/`restart` on the target propagates
to them — **with one deliberate exception**: `mrr-config.service` omits `PartOf`, so a group
restart cannot bounce the board editor along with the game. See §10.1. Adding a further
process is one unit file plus one `systemctl enable` — see §10.

### Why the app is deployed out of the repo

`mrr-server.service` runs `/srv/mrr/game/MRR.dll`, **not** `MRR/bin/Debug/net9.0/MRR.dll`
(and `mrr-config.service` runs `/srv/mrr/config/MRR.Config.dll`).
The Pi is also the dev machine (VS Code Server runs on it), so pointing the service at the
build output would mean a `dotnet build` mid-game silently swaps the binaries under the
running game. `mrrctl deploy` publishes Release output into `/srv/mrr/<role>` as an explicit
act; day-to-day editing in the repo can't disturb a running game.

---

## 4. Restart / failure model

| Event | systemd sees | Result |
|---|---|---|
| Unhandled exception, exit 134 | non-zero exit | restart after 5 s (R2) |
| Clean `Environment.Exit(0)` / graceful shutdown | exit 0 | restart after 5 s — `Restart=always`, not `on-failure` (R3) |
| `mrrctl stop` | operator stop | stays `inactive`. **No** auto-restart, and the recover timer ignores `inactive` (R4) |
| `mrrctl pause` | freezer | process frozen in place, unit still `active`. Health probe skips frozen units (R4) |
| Crash loop: >10 starts in 300 s | start-limit hit | unit → `failed`, backs off. `mrr-recover.timer` resets and retries within 2 min (R7) |
| Kestrel alive but wedged | *nothing* — that's the gap | health probe fails 3× in a row (90 s) → `systemctl restart` (R6) |
| MariaDB down at boot | `mrr-preflight` exits 1 | start fails, retried every 5 s until the DB answers |
| `/dev/spidev0.0` missing | `mrr-spi.service` loads `spi0-2cs`; preflight re-checks | avoids the known startup `IOException` |

Two deliberate choices worth calling out:

- **`Restart=always`, not `on-failure`.** R3 asks for a restart after a clean shutdown,
  which `on-failure` would not do. The cost is that a legitimate one-shot exit is
  impossible — correct for a game server that should always be listening.
- **The start limit is kept, and un-latched by a timer.** Letting a broken build restart
  every 5 s forever hammers MariaDB and floods the journal. Letting it latch off forever
  violates R7. The limit + `mrr-recover.timer` gives "back off, then keep trying quietly."
  Because the recover timer only touches units in the `failed` state, an operator `stop`
  is never overridden.

---

## 5. Pause semantics

`mrrctl pause` uses `systemctl freeze`, i.e. the cgroup v2 freezer: every thread in the
service is suspended in place; `mrrctl resume` (`systemctl thaw`) continues exactly where
it left off. No signal handling, no state loss, no restart.

**What that means in practice:** the frozen process keeps its listening socket and its
open WebSockets, but answers nothing. Phones will show SignalR "reconnecting" after their
timeout and reconnect on resume; the AIM robot WebSockets may drop if the pause outruns
their keepalive. So freeze is right for *seconds-to-a-minute* interruptions — someone
bumped the board, a robot needs picking up — and `restart` is the right tool for anything
longer.

If what's actually wanted is **pausing the game** (stop issuing robot commands, keep
serving phones), that's a game-state feature in `GameController`, not a process-manager
feature — see §11.

---

## 6. Operator interface — `mrrctl`

Run as `mrr`; a sudoers drop-in makes the privileged verbs password-free.

| Command | Does |
|---|---|
| `mrrctl status` | Unit state, freezer state, uptime, PID, memory, last health result |
| `mrrctl start` / `stop` / `restart` | The **game host** by default; `all` for both hosts, `config` for the editor (§10.1) |
| `mrrctl pause` / `resume` | Freeze / thaw the game server (§5) |
| `mrrctl logs` / `logs -f` | `journalctl` for all MRR units, follow with `-f` |
| `mrrctl enable` / `disable` | Whether the group starts at boot |
| `mrrctl deploy [role]` | `dotnet publish` Release → `/srv/mrr/<role>`, keep previous copy, restart if running. Default `all` |
| `mrrctl update` | `git pull` then `deploy` |
| `mrrctl rollback [role]` | Swap `/srv/mrr/<role>.previous` back into place and restart. Default `game` |
| `mrrctl list` | All MRR units and their states |

Any verb takes an optional unit shorthand: `game`/`server`, `config`/`editor`, `health`,
`recover`, `spi`, `target`, `all`, or any `mrr-*` unit name.

### Health probe endpoint

Both hosts expose `GET /api/health` (added 2026-08-22), and that is what the probes use:

```sh
MRR_GAME_HEALTH_URL=http://127.0.0.1:5000/api/health
MRR_CONFIG_HEALTH_URL=http://127.0.0.1:5001/api/health
```

It is cheap and side-effect free. The game host's returns the current game state; the
config host's touches no database, so it answers even when MariaDB is down — which keeps a
DB outage from being misread as a wedged editor.

Two URLs the probe deliberately does **not** use:

- **`/api/alldata`** calls `hubContext.Clients.All.SendAsync(...)`, so probing it every
  30 s would broadcast an `AllDataUpdate` to every phone, forever.
- **`/`** on the game host returns **404**. [MRR/Program.cs](../MRR/Program.cs) calls
  `UseStaticFiles()` *before* `UseDefaultFiles()`, so `/` is never rewritten to
  `index.html`, and phones have to be pointed at the explicit filename. Swapping those two
  lines is still worth doing; `MRR.Config` already registers them in the correct order, so
  `http://<host>:5001/` serves the board editor directly.

---

## 7. Files installed

| Path | Mode | Purpose |
|---|---|---|
| `/etc/systemd/system/mrr.target` | 644 | Group / boot entry point |
| `/etc/systemd/system/mrr-server.service` | 644 | The game server |
| `/etc/systemd/system/mrr-spi.service` | 644 | Oneshot SPI overlay loader (root) |
| `/etc/systemd/system/mrr-health.{service,timer}` | 644 | Hang watchdog |
| `/etc/systemd/system/mrr-recover.{service,timer}` | 644 | Crash-loop un-latcher |
| `/usr/local/bin/mrrctl` | 755 | Operator CLI |
| `/usr/local/bin/mrr-preflight` | 755 | Start gate |
| `/usr/local/bin/mrr-health-check` | 755 | Probe + strike counter |
| `/usr/local/bin/mrr-recover` | 755 | Reset failed units |
| `/etc/default/mrr` | 644 | Tunables (never overwritten by re-install) |
| `/etc/sudoers.d/mrr-process-manager` | 440 | Password-free `systemctl` for the `mrr` user |
| `/srv/mrr/game`, `/srv/mrr/config` (+ `.previous` each) | mrr:mrr | Deployed hosts + rollback copies |

Source of truth for all of the above: [install/service/](service/).

### Tunables — `/etc/default/mrr`

```sh
DOTNET_ROOT=/home/mrr/.dotnet
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://*:5000        # overrides "Urls" in appsettings.json
MRR_GAME_APP_DIR=/srv/mrr/game
MRR_CONFIG_APP_DIR=/srv/mrr/config
MRR_DB_HOST=mrobopi
MRR_DB_PORT=3306
MRR_PORT=5000
MRR_GAME_HEALTH_URL=http://127.0.0.1:5000/api/health
MRR_CONFIG_HEALTH_URL=http://127.0.0.1:5001/api/health
MRR_HEALTH_TIMEOUT=5
MRR_HEALTH_STRIKES=3                 # consecutive failures before restart
MRR_HEALTH_GRACE=60                  # seconds after start before probing
```

`ASPNETCORE_URLS` and the connection-string host are the only two places that know about
network layout, both outside the binary — consistent with the CLAUDE.md rule that the
hostname is never hardcoded.

---

## 8. Install

### 8.1 Prerequisites

Already true on `mrobopi`, listed so a rebuild is reproducible:

- .NET 9 SDK at `/home/mrr/.dotnet/dotnet` (9.0.317)
- `mariadb.service` enabled and running, `rally` schema provisioned
  (`SRRDatabase.sql` + `rallyBoards.sql` — **not** `gameconfig.sql`)
- `dtparam=spi=on` in `/boot/firmware/config.txt`
- user `mrr` in groups `spi`, `gpio`, `i2c`, `sudo`, `adm`

### 8.2 Stop the hand-started server first

The service and a VS Code `dotnet run` both want TCP 5000. Preflight will refuse to start
while the port is held, so stop the manual instance (Ctrl-C in its terminal, or):

```bash
pkill -f 'dotnet run' ; pkill -f 'bin/Debug/net9.0/MRR'
```

### 8.3 Install

```bash
cd /home/mrr/Mega-Robo-Rally-SignalR/install/service
chmod +x install.sh
sudo ./install.sh
```

`install.sh` copies the units and scripts, seeds `/etc/default/mrr` (only if absent),
creates `/srv/mrr`, validates and installs the sudoers drop-in, publishes the app,
`daemon-reload`s, enables the group for boot, and starts it.
Use `sudo ./install.sh --no-start` to install without starting.

### 8.4 Verify

```bash
mrrctl status                     # active (running), freezer: running
curl -sf localhost:5000/api/health >/dev/null && echo game-ok
curl -sf localhost:5001/api/health >/dev/null && echo config-ok
systemctl is-enabled mrr.target   # enabled  → survives reboot
mrrctl logs | tail -30            # look for "preflight OK"
sudo reboot                       # the real test
```

After the reboot, `mrrctl status` should show the server up with an uptime shorter than
the host's, and `systemctl list-timers 'mrr-*'` should list both watchdog timers.

### 8.5 Prove the restart policy

```bash
mrrctl status | grep PID          # note the PID
sudo kill -9 <PID>                # simulate a crash
sleep 8 && mrrctl status          # new PID, "active (running)"

sudo systemctl kill -s SIGTERM mrr-server.service   # simulate a clean shutdown
sleep 8 && mrrctl status                            # back up — R3
```

---

## 9. Runbook

| Situation | Do |
|---|---|
| Game night start | `mrrctl status`; if not running, `mrrctl start` |
| Robot fell off the board | `mrrctl pause` … fix … `mrrctl resume` |
| Server acting strange | `mrrctl restart` (≈5 s; phones reconnect on their own) |
| Deploying a code change | `mrrctl deploy` (or `mrrctl update` to pull first) |
| A deploy broke the game | `mrrctl rollback` |
| Working on the code | `mrrctl stop`, then `dotnet run` as usual; `mrrctl start` when done |
| Nothing responds | `mrrctl logs \| tail -50` — preflight lines name the exact blocker |
| Won't start, DB suspect | `systemctl status mariadb`, then `mrrctl restart` |
| Won't start, `spidev` | `ls /dev/spidev*`; `sudo dtoverlay spi0-2cs`; confirm `dtparam=spi=on` in config.txt |

---

## 10. Adding a second managed process

The general recipe:

1. Copy `mrr-server.service` → `mrr-<name>.service`; change `Description`,
   `ExecStart`, and drop `Requires=mariadb.service` if it doesn't need the DB.
2. `[Install] WantedBy=mrr.target` makes it start with the group at boot.
   `PartOf=mrr.target` additionally makes a target stop/restart propagate to it —
   include it only if that coupling is wanted (see §10.1).
3. `sudo systemctl daemon-reload && sudo systemctl enable --now mrr-<name>.service`
4. `mrrctl list` now shows it; `mrrctl restart <name>` addresses it directly.

If the new process must not outlive the game server, add
`BindsTo=mrr-server.service` + `After=mrr-server.service`.

---

## 10.1 The two-process layout (API decomposition)

> **Status: implemented 2026-08-22.** Everything in this section is in
> [install/service/](service/). What landed:
>
> | File | Change |
> |---|---|
> | `mrr-config.service` | **New.** `WantedBy=mrr.target` but deliberately no `PartOf=`, no `mrr-spi` dependency, no `After=mrr-server` |
> | `mrr-preflight` | Takes a role: `mrr-preflight game` / `config`. Config skips the SPI check and gates on port 5001 |
> | `mrr-health-check` | Takes a role; per-role strike files at `/run/mrr/health.{role}.strikes` |
> | `mrr-health.service` | Two `ExecStart=` lines, one probe per host |
> | `mrrctl` | `config`/`editor` shorthands; bare verbs address the **game host**; `all` for both; per-role `deploy`/`rollback` |
> | `mrr.env` | Role-scoped `MRR_GAME_*` / `MRR_CONFIG_*`, with unprefixed aliases kept for now |
> | `install.sh` / `uninstall.sh` | Install, enable and remove the second unit; per-role deploy directories |
>
> Verified here: all shell scripts pass `bash -n`, all units pass `systemd-analyze verify`,
> and the SPI split was tested directly — with `/dev/spidev0.0` absent, `preflight game`
> fails and `preflight config` still passes.
>
> Deploy layout changed: `/srv/mrr/app` becomes `/srv/mrr/game` and `/srv/mrr/config`, each
> with its own `.previous`. **A machine with the old layout installed needs a re-run of
> `install.sh`**, or the units will look for a DLL that is not there.

[API_DECOMPOSITION_DESIGN.md](../documents/API_DECOMPOSITION_DESIGN.md) splits the app into seven API
contracts across **two** processes. This section is the supervision half of that plan.

```
mrr.target
├── mrr-server.service      game host        :5000   Master, Rules, Executor,
│     PartOf=mrr.target                              Device Gateway, Presentation, Admin
│     Requires mariadb, Wants mrr-spi
├── mrr-config.service      authoring host   :5001   Configuration & Authoring
│     NOT PartOf=mrr.target                          boards, GameData, operators
│     Requires mariadb, no SPI
├── mrr-health.timer   → probes both units
└── mrr-recover.timer  → un-latches both
```

`mrr-server.service` keeps its name and becomes the game host. Renaming it to `mrr-game`
would mean touching the sudoers drop-in, `install.sh`, `mrrctl`, the health script, and
every runbook line in this document for no functional gain.

### Restart isolation — why `mrr-config` omits `PartOf=`

The entire reason Configuration is its own process is that **editing a board must not be
able to disturb a live game**. `PartOf=mrr.target` would defeat that: `systemctl restart
mrr.target` propagates to every member, so a routine group restart would bounce the board
editor along with the game — and worse, `mrrctl restart` (which today addresses the target)
would too.

So `mrr-config.service` declares `WantedBy=mrr.target` (starts at boot with the group) but
**not** `PartOf=mrr.target` (a target stop/restart leaves it alone). Because target
propagation no longer covers it, `mrrctl` must enumerate units explicitly for a genuine
full stop — see the verb table below.

### `mrrctl` changes

| Concern | Change |
|---|---|
| **Default target of destructive verbs** | Bare `start` / `stop` / `restart` / `pause` currently resolve to `mrr.target`. They should resolve to **`mrr-server.service`**. Restarting the editor because you restarted the game is harmless; restarting the *game* because you wanted to reload the editor loses a turn. |
| **New group verb** | `mrrctl <verb> all` acts on both units, enumerated (not via target propagation, since `mrr-config` is no longer `PartOf`). |
| **Shorthand** | `resolve()` gains `config` → `mrr-config.service`. `server`/`game` → `mrr-server.service`. |
| **`BOOT_UNITS`** | Add `mrr-config.service` so `mrrctl enable`/`disable` covers it. |
| **`deploy` / `rollback`** | Two publish outputs — `/srv/mrr/game` and `/srv/mrr/config`, each with its own `previous/` copy. `mrrctl deploy [game\|config\|all]`, defaulting to `all`. Independent rollback is the point: a bad editor deploy must not force a game rollback. |
| **`status`** | Report both units, and both deployed-build timestamps. |

### `mrr-preflight` changes

The script currently hardcodes one app dir, one port, and an unconditional SPI check. It
should take a role argument — `mrr-preflight game` / `mrr-preflight config` — and:

| Check | `game` | `config` |
|---|---|---|
| App DLL present | `$MRR_GAME_APP_DIR/MRR.dll` | `$MRR_CONFIG_APP_DIR/MRR.Config.dll` |
| MariaDB reachable | yes | yes — it writes board tables |
| Port free | 5000 | 5001 |
| `/dev/spidev0.0` present and readable | **yes** | **no — skip** |

The SPI check must not run for `config`. Only the game host constructs `LEDs`
(`Communication` → `Ws2812b.Update()`), so gating the authoring host on SPI would make board
editing unavailable on a Pi with no LED hardware attached — and would crash-loop it for a
reason that has nothing to do with its job.

### `mrr-health-check` changes

Currently hardcodes `UNIT=mrr-server.service`, one URL, and one strike file
(`/run/mrr/health.strikes`). Parameterize all three and invoke per unit:

```
mrr-health-check game     → mrr-server.service, $MRR_GAME_HEALTH_URL,   /run/mrr/health.game.strikes
mrr-health-check config   → mrr-config.service, $MRR_CONFIG_HEALTH_URL, /run/mrr/health.config.strikes
```

Separate strike files matter: a shared counter would let editor failures accumulate strikes
that then restart the game server. `mrr-health.service` runs both (`ExecStart=` twice, or a
loop); the existing active/frozen/grace guards apply per unit unchanged.

Standardize both probes on **`/api/health`** — the endpoint this document's §6 already
recommends adding, which removes the dependency on `/index.html` being the probe target.

### `mrr.env` additions

Role-scope the per-process tunables; keep the DB and shared settings flat.

```sh
# --- game host --------------------------------------------------------------
MRR_GAME_APP_DIR=/srv/mrr/game
MRR_GAME_PORT=5000
MRR_GAME_HEALTH_URL=http://127.0.0.1:5000/api/health

# --- authoring host ---------------------------------------------------------
MRR_CONFIG_APP_DIR=/srv/mrr/config
MRR_CONFIG_PORT=5001
MRR_CONFIG_HEALTH_URL=http://127.0.0.1:5001/api/health
```

`MRR_APP_DIR`, `MRR_PORT`, and `MRR_HEALTH_URL` should stay as aliases for the game host
during the transition so an un-migrated script keeps working, then be removed.

### Binding and reachability

No reverse proxy. Everything phone-facing — the player UI, the GM panel, and the SignalR hub
— is in the game host, so it is already a single origin. Config is a desktop browser tool
reached directly at `http://mrobopi:5001`, and the one cross-origin call (the GM panel's
pre-game setup sections hitting Config's API) needs one CORS entry, not a proxy.

The Admin API's SQL routes must be **loopback-only** even though they live in the game host
(`API_DECOMPOSITION_DESIGN.md` §5.7). Kestrel can express that with a second listen
endpoint; the alternative is gating the routes on a loopback check. Either way, arbitrary
SQL must not be reachable from the game WiFi — which is a change from today, where
`/api/table/{tablename}/{filter}/{setvalue}` executes caller-supplied SQL on `http://*:5000`.

### Pause, with the Executor in the picture

§5 warns that `mrrctl pause` freezes the process. That warning gets sharper once turn
execution is its own contract: **freezing the game host mid-turn does not stop the robots.**
The cgroup freeze suspends the dispatch loop while any already-sent AIM command keeps
running to completion, so the physical board drifts out of sync with the game state that
the frozen process will resume believing.

Once `POST /api/execution/abort` exists (`API_DECOMPOSITION_DESIGN.md` §5.4), `mrrctl pause`
should refuse — or at minimum warn loudly — while `/api/execution/status` reports a turn in
progress, and point at the game-level abort instead. Freezing between turns remains safe and
is the case the runbook's "robot fell off the board" entry actually describes.

### Boot ordering and independence

- Both units `Requires=mariadb.service` + `After=`. Ordering only, not readiness — preflight
  still does the TCP check.
- `mrr-config` must **not** declare `After=mrr-server.service` or `BindsTo=`. It has to come
  up whether or not the game host is healthy; that independence is the deliverable.
- `mrr-config` does **not** `Wants=mrr-spi.service`.
- Start-limit and recover-timer behaviour is per-unit and unchanged: a config crash loop
  latches `mrr-config` only, and `mrr-recover` un-latches whichever units are `failed`.

### Acceptance

- `mrrctl restart` restarts the game host and leaves `mrr-config` untouched (verify by PID).
- `mrrctl restart config` leaves the game host untouched, mid-game.
- `mrrctl stop all` stops both; `mrrctl start` brings back only the game host.
- Killing `mrr-config` repeatedly latches it `failed` without affecting the game host, and
  `mrr-recover` clears it within 2 minutes.
- A cold boot brings up both units with MariaDB satisfied and SPI loaded for the game host
  only.
- `mrr-preflight config` passes on a Pi with no `/dev/spidev0.0`.

---

## 11. Deliberately out of scope

- **Web control panel.** A "System" panel on `gmindex.html` calling
  `POST /api/system/{restart|stop|pause}` that shells out to `mrrctl` is a natural
  follow-on (restart-from-inside works precisely because systemd brings the process
  back). Left out here: it needs an auth story before an endpoint that can stop the
  game server is exposed to six phones on the game WiFi.
- **Game-level pause** (`GameController` holds command dispatch while still serving
  phones). Different concern from process supervision; belongs with the state machine.
- **Remote alerting** on repeated restarts. The data is already in the journal;
  no notification path exists on this host yet.
- **Managing MariaDB.** It has its own well-tested unit and is enabled at boot;
  MRR declares a dependency on it rather than supervising it.

---

## 12. Uninstall

```bash
sudo /home/mrr/Mega-Robo-Rally-SignalR/install/service/uninstall.sh          # keep /srv/mrr + config
sudo /home/mrr/Mega-Robo-Rally-SignalR/install/service/uninstall.sh --purge  # remove them too
```
