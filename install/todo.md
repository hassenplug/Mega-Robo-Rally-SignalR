# Mega Robo Rally — Project TODO

**Last updated:** 2026-09-04
**Legend:** `[x]` Done &nbsp; `[-]` Partial / In Progress &nbsp; `[ ]` Not started

---

## Section 1 — Game Mechanics
*Renegade rules completeness.*

- [x] ~~Damage does not carry across turns into the rules engine~~ — investigated 2026-08-27
  while implementing `documents/ALLPLAYERS_REMOVAL_DESIGN.md`; resolved same day, not a bug.
  `Damage`/`Lives` reset to 0 at the start of every turn's planning input by design under this
  rules version: `Lives` isn't tracked at all, ordinary damage converts to a dealt Spam card
  instead of accumulating, and only a single hit big enough to kill in one shot (a pit) needs
  `Damage` to reflect it — fully decided within one turn's simulation. Circuit Breaker (the
  other would-be reader of cross-turn `Damage`) isn't used in this rules version either. See
  `documents/ALLPLAYERS_REMOVAL_DESIGN.md` §11.

- [ ] Shutdown mechanic (`GameController.cs` + phone UI)
  - Player announces shutdown during programming phase
  - Shut-down robot: takes no laser damage, cannot move, may clear damage cards

- [ ] Reboot mechanic
  - Triggered when robot moves into a pit or off the board
  - Robot placed at chosen reboot token; receives 2 Spam cards; continues this turn
  - Player must choose the direction the robot faces when placed at the reboot token
  - Needs: pit/edge detection in `CreateCommands` + `DataService` respawn logic; direction picker on phone UI; `procSetRobotDirection` equivalent (see Section 6)

### Board Element Activation

- [-] Conveyor belts (`CreateCommands.cs`)
  - [x] Express belts move first (2 squares), then all belts (1 square)
  - [x] Chained movement: robot landing on a second belt also moves
  - [ ] Merge conveyor belts (splitting paths converge)

- [ ] Pushers (`CreateCommands.cs`)
  - Activate only on specific phases (odd or even, marked per pusher)
  - Push robot one square; chain-pushes if another robot is in path

- [x] Gears (`CreateCommands.cs`)
  - CW gear: rotate robot 90° right; CCW gear: rotate robot 90° left

- [x] Board lasers (`CreateCommands.cs`)
  - Fire each phase; damage any robot in line of sight
  - Wall-blocked: walls on the far side of source/target stop the laser

- [x] Robot lasers (`CreateCommands.cs`)
  - Each robot fires 1 laser forward; damages first robot in path
  - Option cards: RearLaser (fires backward too), HighPowerLaser (2 damage)

---

- [x] Flag / checkpoint detection (`CreateCommands.cs` + `GameController.cs`)
  - End of each phase: robot on flag N (where N == LastFlag+1) touches it

- [-] Win condition (`CreateCommands.AddFlag`)
  - [x] Flag comparison fixed 2026-08-22. Was comparing against a hardcoded 5
    (`Player.TotalFlags` was `get => 5; set {}`), so any board without exactly 5 flags
    scored wrong. Now one game-wide `TotalFlags` in `CurrentGameData` (iKey 7), taken from
    the board at game start; `AddFlag` returns true on `LastFlag >= TotalFlags`.
  - [ ] Still only *announces* the winner — [CreateCommands.cs:1346](../MRR.Rules/CreateCommands.cs)
    adds a `"Game Winner:"` text command, with `SquareAction.GameWinner` commented out. The
    game does not actually end. Issue the `GameWinner` command so `ProcessDbCommand` handles it.
    Related: even once issued, `ProcessDbCommand`'s `GameWinner` case only writes
    `CurrentGameData` — it never calls `UpdateGameState()`, so C# state stays stale until the
    next unrelated refresh (`documents/DB_SYNC_ISSUES.md` #12).

- [ ] Board data cleanup (found via `documents/API_DECOMPOSITION_DESIGN.md` §7 /
  `PROJECT_STATUS.md` §4.2, still open)
  - 6 boards have flag-numbering gaps and 16 have a stale `Boards.TotalFlags` value
  - 6 boards have duplicate player start positions (board IDs 20, 40, 41, 59, 67, 71)

- [ ] Damage card draw mechanic
  - When a robot takes damage, draw top card from damage stack → add to discard
  - Spam execution: play top card from deck without choice
  - Haywire execution: play 5 random cards from deck
  - Trojan Horse execution: all other robots take 1 damage

- [-] Option card effects wired into phase processing (`CreateCommands.cs`)
  - Partial: ReverseGears, FourthGear, RammingGear referenced
  - Missing: Brakes, CrabLegs, Recompile, many others
  - Circuit Breaker confirmed **not used** in this rules version (2026-08-27) — do not
    implement it; the existing check in `CreateCommands.cs` (line ~604) is dead in practice

---

## Section 2 — Robot Hardware
*VEX AIM physical integration.*

- [ ] Calibrate `drive_for` distance for one board square
  - Currently: `distance = value * 77mm` (estimated, not measured)
  - Must be measured empirically on the actual printed board
  - See `robo-rally-dev.md §4.6`

- [-] Confirm ws_img wire format against live robot
  - [x] Image is downloaded and saved to `images/align/` (`Players.cs` `GetCameraImageAsync` + `SaveAlignImage`)
  - [x] First 4 bytes are logged in hex on every capture (verify `0xFF 0xD8 0xFF` = JPEG magic)
  - [ ] Confirm `GridAlignmentAgent.ExtractImageBytes()` succeeds and `AnalyzeImage` returns `HasLines=true` on real board
  - [ ] Does `ws_img` stream frames continuously once connected, or only after a trigger command?
  - [ ] Is the frame rate controllable?
  - [ ] Does `ws_img` require `program_init` first, the way `ws_cmd` does?
  - See `tools/ai_agent/ws_img_format.md` for what to update if format differs from raw JPEG
  - Same open questions echoed in `tools/ai_agent/grid_alignment_agent.md` ("Known Unknowns")
    and `tools/ai_agent/image_processing.md` ("pending hardware validation") — one hardware
    session should close all three docs' unknowns at once

- [-] Grid alignment agent calibration (`GridAlignmentAgent.cs`)
  - Code complete; constants need tuning against real board + lighting
  - `BlackLuminanceThreshold`, `MinBlackPixels`, `AlignedThreshold`, `NudgeDistanceMm`

- [ ] Robot LCD display at game start
  - Show robot name and player name on each AIM robot's screen
  - Call `PrintAsync` / `lcd_print_at` in `GameController` state 0
  - Draw an arrow on the LCD indicating the robot's current facing direction
    - Update from drawing an arrow to loading an arrow image

- [ ] LED state machine across game phases (`Players.cs`, `CommandProcess.cs`, `GameController.cs`)
  - **Connected / waiting for program** → LEDs ON (robot color) — `SendColorStatus()` already does this at connect time
  - **Program complete** (all 5 registers filled / programs locked, state 5) → LEDs OFF
  - **Executing move** (between `SendRobotCommandAsync` start and `isMoving` → false) → LEDs ON
  - **Move complete, waiting for next program** (state 12 → 2) → LEDs ON again
  - Implementation: call `SetLedAsync` at each transition point in `CommandProcess.ProcessCommand` (before/after move) and in `GameController` state transitions (state 5 = off, state 2 = on)

- [x] Update isMoving to multiple states
  - 0 when not moving
  - 1 when starting a move (sending move command)
  - 2 when robot comfirms move is in progress
  - back to 0 when move is complete (or move to state 3 when move complete but unchecked)
  - Confirm `isMoving` is set and unset correctly (`Players.cs`)


- [ ] Remove old/unused communication code
  - Audit `Players.cs` and `Program.cs` for any leftover WebSocket stubs or dead paths
  - Remove all bluetooth communication and support
  - Make sure to keep a place to store the robot's IP address

- [ ] Robot 6's `RobotBases` row has a placeholder IP/AIMName (`PROJECT_STATUS.md` §4.6) —
  that seat cannot use a physical robot until a real base is assigned

- [ ] Failed robot send still applies the move as if it succeeded (`CommandProcess.cs`,
  `DataService.Commands.cs` `ProcessDbCommand`) — `SendRobotCommandAsync`'s fault handler
  already stops the loop from hanging (sets `isConnected=false`, forces `StatusID=4`), but the
  next poll still calls `ProcessDbCommand(command, 5)`, which applies the move's position
  effect unconditionally. A robot whose send failed ends up with its DB position updated as
  though it moved. Needs a path that skips the position effect when the send is known to have
  failed. (`documents/API_DECOMPOSITION_DESIGN.md` §7, listed as High — the hang half of it is
  already fixed, the false-success half is not)

- [ ] Implement ws_audio upload if server-side audio needed (`ws://{ip}:80/ws_audio`)
  - Wire format is documented (AIM WebSocket Library v1.0.1):
    - Byte 0: format (`0`=WAV, `1`=MP3)
    - Byte 1: volume (0–100)
    - Bytes 4–7: data length (little-endian uint32)
    - Bytes 32–63: filename (null-padded, 32 chars max)
    - Followed by: raw audio data (max 255 KB)
  - `play_file` cmd_id (`ws_cmd`) plays a file uploaded via `ws_audio`
  - Only needed if server wants to push custom audio to robots; built-in sounds via `play_sound` need no upload

---

## Section 3 — UI
*`wwwroot/`*

### Player Programming UI (`index.html`)

- [x] Card programming interface — players see hand and place cards into registers
- [x] Show player hand and register state in real time via SignalR

- [ ] Allow player to set facing direction after reboot
  - When a robot reboots, the player must choose which direction it faces
  - Need a direction-picker UI on the player's phone (`index.html`)
  - Ties into the reboot mechanic (Section 1)

- [ ] Shutdown toggle on phone UI
  - Player can choose to shut down during programming phase

- [ ] Display robot status on phone (damage, energy, position) — not lives; this rules
  version doesn't track lives (confirmed 2026-08-27, see Section 1 note above)

- [ ] Show deck size on player UI
  - Display total cards in the player's personal deck (all MoveCards owned by that robot across all locations except Played Spam / CardLocation=5)
  - Includes accumulated Spam cards so players can see how damage bloats their deck
  - Source: `COUNT(*) FROM MoveCards WHERE Owner=robotID AND CardLocation != 5`
  - Expose via a new column in `viewRobots` or `viewRobotsMicro`, then surface in `CardsDealt`/`AllDataUpdate` JSON

- [ ] Handle Haywire / Spam / option card notifications on phone

- [ ] Every phone still receives every player's hand in the broadcast payload, not just its
  own (`documents/API_DECOMPOSITION_DESIGN.md` §7, Medium; the password leak this item used to
  also cover is already fixed). Needs per-seat SignalR groups or payload filtering.

### GM Control Page *(new page needed)*

- [ ] Game selection — dropdown/list from GameData table; button to load selected game

- [ ] Game controls — Start, Restart, Reboot buttons (mapped to game state transitions)

- [ ] Dynamic button area — display any buttons/actions that appear based on game state
  - e.g. "Advance Phase", "Skip Robot", admin overrides

- [ ] Link to board viewer showing current robot positions

- [ ] Controls to manually set a robot's facing direction
  - Needed at game start when robots are placed on the board
  - Calls `procSetRobotDirection` equivalent in C#

- [ ] Show deck size per player on GM UI
  - Same data as the player UI item above — total cards in each player's deck including Spam
  - Display alongside Damage in each robot's status panel so GM can see deck health at a glance

- [ ] Show AIM robot battery level on GM UI
  - `ws_status` already delivers `battery` (0–100%); `ProcessStatusEvent` reads it but discards it
  - Server: add `Player.Battery` property; store in `ProcessStatusEvent`; merge into `GetAllDataJson` robots payload
  - GM UI: show in each robot status panel when `IsConnected == 1`; color-code ≥50% green, 20–49% yellow, <20% red
  - Design spec in `.claude/agents/gm-ui.md` § Robot Status Panel

- [ ] Web control panel for `mrrctl` (start/stop/restart/status via REST, called from a
  "System" panel in `gmindex.html`) — designed but explicitly deferred pending an auth story;
  `mrrctl` itself is CLI-only today (`install/PROCESS_MANAGER.md` §11)

---

## Section 4 — Raspberry Pi Hardware
*Sense HAT.*

- [ ] Create `MRR/Sensors/SenseHatService.cs`
  - Add `Iot.Device.Bindings` NuGet package to `MRR.csproj`
  - Register as singleton in `Program.cs`
  - 8×8 LED matrix: show current game state, active robots (by color), turn/phase

- [ ] Joystick input from Sense HAT
  - Read joystick direction in `SenseHatService`
  - Map to game actions (advance state, navigate menus, etc.)

- [ ] Create SD card setup / install script
  - Script to run on a fresh Raspberry Pi OS image to install all dependencies
  - Should cover: .NET 9 runtime, MySQL server, project files, `systemd` service for auto-start
  - Store in `install/` directory alongside this file

- [ ] **Install the systemd process manager on `mrobopi`.** Design + reference implementation
  are code-complete in the repo (`install/PROCESS_MANAGER.md`; every file it describes exists
  in `install/service/` — confirmed 2026-08-30: `mrr.target`, `mrr-server.service`,
  `mrr-config.service`, `mrr-spi.service`, `mrr-health.{service,timer}`,
  `mrr-recover.{service,timer}`, `mrrctl`, `mrr-preflight`, `mrr-health-check`, `mrr-recover`,
  `mrr.env`, `install.sh`, `uninstall.sh`). What it does once installed:
  - **Boot & restart**: `mrr.target` groups the units; `Restart=always` brings the game host
    back after both a crash *and* a clean exit; a `mrr-preflight` gate blocks start until
    MariaDB answers and (game host only) `/dev/spidev0.0` exists
  - **Two-process layout** (implemented 2026-08-22, per §10.1): `mrr-server.service` (game
    host, :5000) and `mrr-config.service` (board-authoring host, :5001) are supervised
    independently — `mrr-config` deliberately omits `PartOf=mrr.target` so restarting/editing
    the board editor can never bounce a live game
  - **Health / recovery**: `mrr-health.timer` probes `GET /api/health` on both hosts every 30s
    (confirmed live in code: `MRR/Program.cs` and `MRR.Config/Program.cs` both map it) and
    restarts a host that fails 3 times in a row; `mrr-recover.timer` un-latches a crash-looped
    unit every 2 minutes without touching an operator-initiated `stop`
  - **Operator CLI (`mrrctl`)**: `status` / `start` / `stop` / `restart` / `pause` (cgroup
    freeze — see doc §5 for why that's only right for short interruptions) / `resume` / `logs`
    / `enable` / `disable` / `deploy [role]` / `update` / `rollback [role]` / `list`, each
    addressable per-role (`game`, `config`, `all`)
  - **Deploy safety**: the app runs from `/srv/mrr/{game,config}` (a `dotnet publish` output),
    never straight from the repo build folder, specifically so editing code on the same Pi that
    hosts the live game can't swap binaries under it mid-game; `.previous` copies back
    `mrrctl rollback`
  - **`documents/PROCESS_MANAGER_DESIGN.md` is an early draft, explicitly superseded — the doc
    itself says so. `install/PROCESS_MANAGER.md` is the one to read/update.**

  What's actually **not done**: installation on the physical host. Per `PROJECT_STATUS.md`,
  as of its last update `mrrctl` isn't on `mrobopi`'s `PATH`, no `mrr-*` units are registered,
  and the game is started by hand (`dotnet run` in a terminal — "Mode A"). Verify current state
  before assuming otherwise, then: stop the hand-started server, run
  `sudo install/service/install.sh`, and work through §8.4/§8.5's verification + restart-policy
  checks in `install/PROCESS_MANAGER.md`.
  - If any machine still has the old deploy layout (`/srv/mrr/app`), re-run `install.sh` to
    move it to `/srv/mrr/game` + `/srv/mrr/config`
  - Deliberately out of scope per the doc's own §11 (also listed in Section 3/6 above): a
    web control panel calling `mrrctl` from `gmindex.html`, game-level pause, remote alerting

---

## Section 5 — Network Setup
*Topology: Home Router → Game Router (WAN) → Pi + Robots + Phones*

```
Home Router (192.168.1.x)
    └── Game Router WAN port  (gets DHCP lease from home router)
            Game Router LAN (e.g. 192.168.4.x)
                ├── Raspberry Pi  (Ethernet, static IP)
                ├── AIM Robots ×6 (WiFi, DHCP reservations)
                └── Player Phones ×6 (WiFi)
```

### Game Router Setup

- [ ] Configure game router
  - Set a dedicated SSID and password (e.g. `MRR-Game`)
  - Use 2.4 GHz band (confirm AIM robots support 5 GHz before switching)
  - Set game router LAN subnet (e.g. `192.168.4.0/24`) — must not overlap home router subnet
  - Connect game router WAN port to home router via Ethernet cable

- [ ] Enable access from home network to Pi
  - On the game router: add a port-forward rule — external port 5000 (or 80) → Pi's game-network IP
  - This lets dev machines on the home network reach the Pi at `http://{home-router-assigned-IP}:{port}/`
  - Alternatively, connect your dev machine directly to the game router WiFi during testing

### Raspberry Pi

- [ ] Connect Pi via Ethernet to game router LAN
  - Wire Pi 5 to a LAN port on the game router (not the WAN port)
  - Assign Pi a static IP on the game subnet (e.g. `192.168.4.10`) or DHCP reservation by MAC
  - Set hostname `mrobopi` to resolve to this IP on the game network (via router DNS or `/etc/hosts`)
  - Update connection strings / launch URLs in `appsettings.json` if IP differs from current config

### Robots

- [ ] Connect all 6 AIM robots to the game WiFi (`MRR-Game` SSID)
  - Use the VEX AIM app or built-in setup to join the game SSID
  - Note each robot's MAC address for DHCP reservation

- [ ] Assign static IPs to all 6 robots and update the database
  - Configure DHCP reservations on the game router by MAC address
  - Suggested scheme: `192.168.4.101`–`192.168.4.106` for robots 1–6
  - Enter confirmed IPs into the `RobotBases` table (`IPAddress` column; renamed from `MACID` 2026-08-22)
  - `RobotBases` also holds `DefaultBody` — verify each base is mapped to the correct robot body
  - `DataService.GetAllPlayers()` reads `IPAddress` into `Player.IPAddress`; `Player.Connect()` in `Players.cs` uses it to open the WebSocket

### Player Phones

- [ ] Connect player phones to the game WiFi (`MRR-Game` SSID)
  - All 6 player phones join the same SSID as the robots
  - Phones open the player UI at `http://192.168.4.10:{port}/` (or `http://mrobopi:{port}/`)

### Verification

- [ ] Verify Pi → robot connectivity
  - From the Pi, confirm WebSocket reachability: `ws://{robot-ip}:80/ws_cmd` for each robot
  - Quick check: `curl http://{robot-ip}:80/` or `ping {robot-ip}`

- [ ] Verify phone → Pi SignalR connectivity
  - Open player UI from a phone on the game WiFi; confirm SignalR hub connects and hand is displayed

- [ ] Verify home-network → Pi connectivity
  - From a dev machine on the home network, reach the Pi via the port-forward rule
  - Confirm GM page and player UI load correctly over the home → game router path

- [ ] Document final IP address assignments
  - Record all IPs (Pi, each robot, game router LAN/WAN, home router) in `install/network.md`

- [ ] `Urls: http://*:5000` in `appsettings.json` binds every network interface, not just the
  game LAN (`documents/API_DECOMPOSITION_DESIGN.md` §7, Low) — fine on an isolated game router,
  worth narrowing once the home-network port-forward above is in place

---

## Section 6 — Infrastructure / Setup

- [ ] Entity Framework for game setup / initialization
  - Use EF (`MRRDbContext` already exists) for initial game setup steps
  - `GameController.StartGame()` / `LoadGameData()` still use raw SQL string building

- [ ] **Security: DB password committed in tracked `appsettings.json`** (`ConnectionStrings:Rally`,
  `pwd=rallypass`) in both `MRR/appsettings.json` and `MRR.Config/appsettings.json`
  (`PROJECT_STATUS.md` §4.7). Move to an untracked `appsettings.Production.json` or an
  environment variable before this repo is ever made public. Flagging this one as worth
  verifying and fixing promptly rather than leaving it queued.

- [ ] `SqlGateway.ExecuteSQL`/`GetQueryResults` silently swallow database errors and return
  empty results instead of surfacing them (`PROJECT_STATUS.md` §6.6) — a bad connection string
  or a bad query currently shows up later as an unrelated NullReferenceException instead of a
  clear DB error at the source

- [ ] Add the new `FieldEnclosed` `CurrentGameData` row (iKey 21) to the test MySQL copy —
  `install/MRRDatabase.sql` only seeds a fresh install, so the running `rally` database on
  `mrobopi` (or wherever the test copy lives) needs it inserted by hand:
  ```sql
  INSERT INTO CurrentGameData (sKey, iValue, sValue, Category, iKey) VALUES ('FieldEnclosed', 0, NULL, 'Game', 21);
  ```
  Backs the "field walled in on all sides" boundary check added to
  `CreateCommands.CalcMoveDistance` (`GameStateStore.cs`, `DataService.cs`,
  `TurnRequest.cs`) — until this row exists, toggling it via the DB grid editor won't work.

- [ ] Board editor `PUT` does its replace as a DELETE+INSERT with no surrounding transaction,
  and builds SQL by string concatenation rather than parameters (`Program.cs:453`,
  `documents/API_DECOMPOSITION_DESIGN.md` §7, Medium) — a failure mid-update can leave a board
  half-written, and the concatenation is worth checking for injection risk

- [ ] Split `DataService` further (`documents/API_DECOMPOSITION_DESIGN.md` §9 step 4, called out
  in `PROJECT_STATUS.md` as "the largest remaining piece"): extract a `RuleEffects` layer and a
  repository layer out of the current partial-class split. Sits on the turn-execution hot path
  (`ProcessDbCommand`, `CreateCommands`) — wants careful review, not a quick pass.

- [ ] Presentation-layer decomposition (`documents/API_DECOMPOSITION_DESIGN.md` §9 step 7 /
  §4 "landmine"): `RobotScreenUI` still writes game state, calls the DB, and broadcasts SignalR
  all from one method (`UpdateCardPlayed` then `SendAsync`) instead of separating render from
  report; per-seat SignalR groups (see the phone-hand-visibility item in Section 3) are part of
  the same step

- [ ] Game-level turn pause, distinct from process-level pause (`install/PROCESS_MANAGER.md`
  §11): `GameController` holding the dispatch loop while continuing to serve phones, vs.
  `mrrctl pause` suspending the whole process. Explicitly out of scope when the process manager
  was built; still wanted.

- [ ] Remote alerting on repeated `mrr-server` restarts — no notification path exists yet
  (`install/PROCESS_MANAGER.md` §11)

### In-memory / DB sync bugs (`documents/DB_SYNC_ISSUES.md`)

Same class of bug as the two fixed 2026-08-30 (see Done below): a DB write lands, but a
matching in-memory collection is never updated, so the next unrelated write from that stale
copy silently reverts it, or a broadcast reads stale data. Numbering below matches the doc
(items 1/2/3/5 were the `AllPlayers` mirror, already resolved by its removal).

- [ ] #4 — Turn counter incremented in DB (`CurrentGameData` iKey=2) but not `_dataService.Turn`
  (`GameController.NextState()`)
- [ ] #6 — Bulk `CommandList` `StatusID` update not reflected in `DataService.ListOfCommands`
  (`GameController.NextState()`) — note: `ListOfCommands` itself was removed 2026-08-30 as dead
  code (see Done below); re-check whether this item still applies to whatever now holds that
  bulk-updated set, or is moot
- [ ] #7 — `CreateCommands.ExecuteTurn()` writes `GameState` directly via raw SQL, bypassing the
  `GameState` property setter
- [ ] #8 — `CommandList` phase rows deleted in DB but the matching in-memory list not cleared
  (`CreateCommands.ExecuteTurn()`)
- [ ] #9 — `MoveCards` table cleared in DB but the `GameCards` collection not cleared
  (`DataService.GameNewAddCards()`)
- [ ] #10 — `ProcessDbCommand`'s `Option.Option` case inserts into `RobotOptions` but doesn't
  update the in-memory `OptionCards` collection
- [ ] #11 — `ProcessDbCommand`'s `DealCard` case updates a `MoveCard`'s `Owner` in the DB but
  leaves the matching `GameCards` entry stale
- [ ] #13 — `ProcessDbCommand`'s `SetCurrentGameData` case doesn't refresh `PhaseCount`/
  `LaserDamage` in memory after writing them to the DB
- [ ] #14 — `UpdateCardPlayed()` leaves `Player.CardsDealt`/`CardsPlayed` stale after its DB
  update

### Doc housekeeping (found while auditing docs 2026-08-30, not yet independently re-verified in code)

- [ ] `documents/API_DECOMPOSITION_DESIGN.md` §7's defects table has several rows that read as
  stale against `PROJECT_STATUS.md`: "no abort path" (an abort endpoint now exists), "`/api/table`
  mutating GET" (replaced by `MRR.Admin` per that doc's own §9 step 5), and "phone receives
  password" (fixed — only the hand-visibility half above is still open). Worth a pass to confirm
  and update the table rather than trust either doc blindly.
- [x] **Confirmed fixed 2026-08-30**: "`/` returns 404" — `MRR/Program.cs` now calls
  `UseDefaultFiles()` before `UseStaticFiles()` (with a comment noting exactly this history), so
  `/` does serve `index.html`. `install/PROCESS_MANAGER.md` §6 still described this as an open
  fix to make; corrected there too. One leftover: the `/api/health` comment a few lines below it
  in `Program.cs` still says "UseStaticFiles is registered before UseDefaultFiles above" — now
  false, harmless, but worth a one-line fix next time that function is touched.
- [ ] Same doc's §9 step 6 (Device Gateway) says "Not started"; `PROJECT_STATUS.md` §5.1 (same
  date) says "Partial — dispatch bugs fixed; `IRobotTransport` remains." Reconcile.
- [ ] Same doc's §5.4 still lists "busy-wait, no timeout" as open; `PROJECT_STATUS.md` §6.6 says
  commands now time out after 30s. The timeout half looks done; confirm whether the busy-wait/
  poll-interval half is still a real concern.
- [ ] Git branch `pre-decomposition-cleanup` was well ahead of `origin` as of `PROJECT_STATUS.md`
  §5.3 — confirm it's been pushed/merged/renamed since

- [x] **Convert SQL stored procedures to C# — COMPLETE (verified 2026-08-22).**

  The `rally` schema now contains **37 base tables and nothing else**: zero stored
  procedures, zero functions, zero triggers, zero views. `install/MRRDatabase.sql` matches,
  and no C# code calls a `proc*`/`func*` — every remaining mention is a comment or
  commented-out code.

  Ported to `DataService`: `procResetPlayers` → `ResetPlayers()`,
  `procMoveCardsShuffleAndDeal` → `MoveCardsShuffleAndDeal()`, `procUpdateCardPlayed` →
  `UpdateCardPlayed()`, `funcProcessCommand` → `ProcessDbCommand()`, `funcDealSpamToPlayer`
  → `DealSpamToPlayer()`, `procCurrentPosSave`/`Load` → `CurrentPosSave()`/`CurrentPosLoad()`,
  `procDealOptionToRobot` → `DealOptionToRobot()`, `procVerifyPosition` → `VerifyPosition()`,
  `funcGetNextCard` → `GetNextCard()`, `procUpdatePlayerPriority` → `UpdatePlayerPriority()`,
  `procSetStatus` → `SetStatus()`, `funcGetNextOption` → `GetNextOption()`.
  `funcGetNextGameState` → `GameController.NextState()`.

  **Deliberately dropped rather than ported:**
  - `Robots_BEFORE_UPDATE` — the damage-cap → death rule. Robots do not normally die from
    taking damage, so the rule is not wanted. The `ApplyRobotBeforeUpdateRules` helper written
    for it was never called and was deleted 2026-08-22. `ResetPlayers()` still applies the
    ShutDown transitions it also covered.
  - `procGameFillPrograms` — Classic-rules only; deleted with `RulesVersion` 2026-08-22.
  - `procMoveCardsCheckProgrammed` — its only callers were the Classic paths; deleted 2026-08-22.
  - `funcGetProgramReadyState`, `procMoveCardsCheckOne`, `procProcessOption`,
    `procKickstart`, `procRobotConnectionStatus` — no C# equivalent and no callers.
  - `Robots_AFTER_UPDATE`, `CurrentGameData_BEFORE_UPDATE`, `GameData_BEFORE_UPDATE` —
    convenience triggers (LED sync, `sValue` lookups, BoardID cascade). The application now
    writes those fields explicitly. `GameData_BEFORE_UPDATE`'s BoardID cascade is done by
    hand in the board-editor `PUT` ([Program.cs](../MRR/Program.cs)).

---

## Section 7 — Dead Code Removal

- [ ] `RefreshPlayerCards` (`DataService.Cards.cs`) — **does nothing**, but has seven callers
  in `Program.cs`, `GameController.cs` and `RobotScreenUI.cs` that read as though it refreshes
  card state. Its body is disabled by an early `return`, apparently because
  `UpdateCardPlayed` step 8 now syncs the moved cards in memory directly. Decide: delete it
  and the seven calls, or restore it. It should not stay a silent no-op.
- [ ] `SetArchiveToCurrent` (`Players.cs:87`) — no callers; updates archive pos from current pos
- [ ] `HasOptionCard` (`Players.cs`) — no callers; stub that always returns false
- [ ] `MoveUnlimitedAsync` (`Players.cs`) — no callers; sends continuous drive command
- [ ] `ShowAIAsync` (`Players.cs`) — no callers; triggers AI vision overlay on robot LCD
- [ ] Dead commented-out line at `CommandList.cs:297` (found during `ALLPLAYERS_REMOVAL_DESIGN.md` review)
- [ ] Dead commented-out line at `Program.cs:228` (same review)
- [ ] `AdminApi.cs:155` computes `players = data.AllPlayers.Count` — cosmetic only, could become
  a `COUNT(*)` now that `AllPlayers` isn't the source of truth elsewhere; no correctness need

---
## Section 8 — Game Screen

- GM screen needs a way to end the current game.
- `CurrentGameData` should have a flag to determine:
  - Whether a game is currently in progress (and whether we need to connect to the robots)
  - What should be displayed on the player interface (e.g. "Game setup in progress")


  Using the IsRunning flag in CurrentGameData
  When the pi boots, or app starts, if IsRunning, connect to the robots, and store that robots are connected.
  When IsRunning is turned off, disconnect from robots
  When a game is started, IsRunning should be turned on
  
- [x] Removing AllPlayers from main code — design doc written and rollout implemented
  2026-08-27/30, see `documents/ALLPLAYERS_REMOVAL_DESIGN.md`. `Robots` is now read fresh from
  the DB per broadcast; `AllPlayers` remains only where the doc identifies it's still needed
  (command creation, connection registry).

- [ ] Manual verification pass for the AllPlayers removal (`ALLPLAYERS_REMOVAL_DESIGN.md` §10 —
  written but unchecked):
  - [ ] Phones' displayed position/damage/status/cards update every broadcast
  - [ ] `CommandList` descriptions ("played card: X") still render correctly
  - [ ] `/api/admin/diagnostics` still reports `robotsConnected`
  - [ ] Robot disconnect/reconnect mid-game still works
  - [ ] Re-check `UpdateCardPlayed` specifically through a full programming→lock→execute cycle
  - [ ] Play a multi-turn game confirming pit-death/Damage-threshold behavior holds turn after turn

---
## Section 9 - Robot Connection Screen

- Update the IsConnected flag in Robots to ConnectStatus
  - Add the needed statuses to the RobotStatus table
  - Link ConnectStatus to the RobotStatus table
  - Update all references to IsConnected

Create a small form.  Data should be pulled using the same subscription as index.html
- [x] Header buttons
  - [x] Connect All
  - [x] Disconnect All
  - [x] Search (search all IP addresses for matching Mac addresses)

  - [x] Update IP (Allow user to enter the IP address into the box where the name was)
- [x] Show rows for all robots
  - [x] Colored Button next to a box with a Robot Name and colored background (colors will match the robot) - Button will toggle connection (try to connect/dsiconnect)
    - [x] Red (not connected)
    - [x] Yellow (Connecting)
    - [x] Green (Connected)
    - [x] Purple (Searching)
    - [x] Unknown (0)

---
## Section 10 - Update index to create a GM screen

 - [ ] copy "connections" functionality into the index page
   - [ ] set the status field to be a button that the GM can use to connect when a robot is not connected.
   - [ ] background of Status should be red when not connected, but only on the gm screen
 - [ ] Gm screen will have a "Next" button at the bottom of the program commands table
 - [ ] GM screen will show all buttons players see
 - [ ] Tap on the game message (like "Turn 2") will toggle between the player view and the GM view (only when GM mode is enabled)
 - [ ] Players will have to log in and the browser will hold a cookie of the player login

---

## Done *(reference)*

- [x] CommandProcess background execution thread
- [x] Command pipeline: `CreateCommands` → `CommandList` → `CommandProcess` → WebSocket
- [x] WebSocket dual-channel robot communication (`ws_cmd` + `ws_status`)
- [x] SignalR DataHub broadcasts to phones
- [x] State machine `GameController.NextState()` (states 0–16)
- [x] Board editor API + HTML
- [x] EF Core DbContext (`MRRDbContext`) for `CommandItems` / `Robots` / `CurrentGameData`
- [x] `funcMarkCommandsReady` → `PendingCommands.MarkCommandsReady()` (C# done)
- [x] `procGetReadyCommands` → `CommandProcess.GetActiveCommandList()` (C# done)
- [x] `procUpdatePlayerPriority` (10-Turn mode path) → `DataService.UpdatePlayerPriority()`
- [x] `viewRobots`, `viewRobotOptions` (active views used)
- [x] `viewRobotsInit`, `viewRobotsRefresh` replaced with inline SQL in `DataService.cs` — views removed from schema
- [x] `CommandList_BEFORE_INSERT` trigger → MySQL AUTO_INCREMENT
- [x] Camera image capture: `Player.GetCameraImageAsync()` via ws_img
- [x] Grid alignment agent: `GridAlignmentAgent.cs` + `GET /api/robot/align/{robotId}`
- [x] SixLabors.ImageSharp 3.1.12 added for image processing (Pi-compatible)
- [x] `RulesVersion` removed 2026-08-22 — Renegade only. Deleted the Classic branch of
      `MoveCardsShuffleAndDeal` and `GameFillPrograms` (148 lines), the field, the
      `GameData` column, and the `CurrentGameData` iKey 27 row
- [x] One game-wide `TotalFlags` 2026-08-22 — `CurrentGameData` iKey 7, set from the board
      at game start; removed the hardcoded `Player.TotalFlags`
- [x] Dead code removed 2026-08-22: `ApplyRobotBeforeUpdateRules`, `MoveCardsCheckProgrammed`
- [x] Fixed 2026-08-30: `clearpause` GM action (`Program.cs`) used raw SQL that a live
  `PendingCommands` loop never saw, so its next `SaveChanges()` on the stale in-memory copy
  silently reverted the fix. Added `GameController.ClearPausedCommands()` /
  `PendingCommands.ClearStuckCommands()`, routed through the live loop when one exists —
  same bug class as `documents/DB_SYNC_ISSUES.md`.
- [x] Fixed 2026-08-30: `DataService.ProcessDbCommand(int, int)` — the player's "continue"
  button REST path (`/api/player/3/...`) — read from `DataService.ListOfCommands`, a public
  property nothing ever populated, so it always returned -1 and never completed the command.
  Added `GameController.ProcessDbCommand(int, int)` / `PendingCommands.ProcessDbCommand(int, int)`
  to look the command up in the live turn's in-memory list instead; removed the dead
  `ListOfCommands` property.
