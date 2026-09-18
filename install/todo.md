# Mega Robo Rally — Project TODO

**Last updated:** 2026-09-18
**Legend:** `[x]` Done &nbsp; `[-]` Partial / In Progress &nbsp; `[ ]` Not started

Resolved items are removed from this file once done rather than kept as a checked-off log —
see git history / commit messages for what happened and when.

---

## Current Priorities

Sections below are organized by feature area, not urgency. Ranked pull of what actually
matters for a real game, re-derived 2026-09-16. Items 1–6 are genuine open gaps; 7–8 stay on
the list but rank last because this is a closed system with no public exposure, per user
2026-09-16 — see each item's note. Re-check before trusting this if much time has passed.

1. **Failed robot send silently applies the move anyway** (Section 2). A send that fails still
   advances the robot's DB position as if it succeeded — the game state and the physical board
   quietly diverge, and nothing about it is visible to the GM. Tagged High in
   `API_DECOMPOSITION_DESIGN.md` §7.
2. **Reboot mechanic** (Section 1). Core Renegade rule for a robot that falls in a pit or off
   the board. The direction-picker half is done; pit/edge detection and the respawn itself are
   not — any board with pits can't be played correctly yet.
3. **Shutdown mechanic** (Section 1). Not started. Also a core Renegade rule.
4. **Damage card draw mechanic** (Section 1). Not started: drawing from the damage stack, Spam/
   Haywire/Trojan Horse execution. Beyond the basic damage → dealt-Spam-card conversion that
   already works, none of the special-card executions are implemented.
5. **Pushers** (Section 1). Board element type not implemented at all — activate on specific
   phases (odd/even), push a robot one square, chain-push if another robot is in the way.
6. **Board data cleanup** (Section 1). 6 boards have flag-numbering gaps and are unwinnable;
   16 have a stale `Boards.TotalFlags` value.
7. **DB password committed in tracked `appsettings.json`** (Section 6) — lower priority: closed
   system, no public exposure, per user 2026-09-16.
8. **Every phone receives every player's hand** (Section 3) — lower priority, same reasoning;
   the cookie-login item already decided not to worry about this for the raw broadcast payload.

Everything else in the sections below is real but lower-stakes: UI polish, dead-code removal,
doc reconciliation, and the network-setup checklist (Section 5 — unverified whether it's still
literally all open, or just not updated after being done by hand).

---

## Section 1 — Game Mechanics
*Renegade rules completeness.*

- [ ] Shutdown mechanic (`GameController.cs` + phone UI)
  - Player announces shutdown during programming phase
  - Shut-down robot: takes no laser damage, cannot move, may clear damage cards

- [ ] Reboot mechanic
  - Triggered when robot moves into a pit or off the board
  - Robot placed at chosen reboot token; receives 2 Spam cards; continues this turn
  - Player must choose the direction the robot faces when placed at the reboot token — the
    picker itself is done (2026-09-15). Once respawn logic exists and sets `PositionValid = 0`
    the same way game start already does, the phone UI will show the direction picker with no
    further wiring needed here.
  - Needs: pit/edge detection in `CreateCommands` + `DataService` respawn logic; ~~direction
    picker on phone UI~~ (done); ~~`procSetRobotDirection` equivalent~~ (done)

### Board Element Activation

- [-] Conveyor belts (`CreateCommands.cs`)
  - [x] Express belts move first (2 squares), then all belts (1 square)
  - [x] Chained movement: robot landing on a second belt also moves
  - [ ] Merge conveyor belts (splitting paths converge)
  - [-] Conveyor belts can push a robot off the board (into a pit/off the edge) and through
    walls — off-board half fixed 2026-09-10: `InValidPos()` now guards the `SquareAction.Move`
    case in `CreateCommands.cs`, so a belt push toward an out-of-range square is skipped instead
    of writing a negative/out-of-bounds position. Still open: belt movement passing through a
    wall — it doesn't appear to run the same wall check as normal moves (see
    `CalcMoveDistance`'s wall check, Section 6).

- [ ] Pushers (`CreateCommands.cs`)
  - Activate only on specific phases (odd or even, marked per pusher)
  - Push robot one square; chain-pushes if another robot is in path

---

- [ ] `MoveCards.Executed` is never actually set, so the currently-executing card's short
  description never displays — found 2026-09-16 in
  [`DataService.RebuildRobotCardsSummary`](../MRR/DataService.Cards.cs#L52): the
  `ShowCardsPlayed` GROUP_CONCAT reads `IF(mc.Executed, mct.ShortDescription, 'X')`, but cards
  are not marked as executed in the database, so it always falls through to `'X'` instead of
  showing e.g. "M1"/"TR" for the card currently running. The write side exists —
  `DataService.Commands.cs`'s `SquareAction.Card` case sets `Executed = 1`, and `CreateCommands`
  does add that command per played card (`CreateCommands.cs:117`, `:1099`, `:1110`) — so this
  needs tracing why it isn't landing rather than being built from scratch: check
  `SqlGateway.ExecuteSQL`'s known silent-error-swallowing (Section 6) first, then whether the
  `CardID`/`Owner` pair in the `WHERE` clause still matches by the time this command runs.
  Same duplicated GROUP_CONCAT logic also appears in `DataService.Players.cs:71` and `:213` —
  check whether it has the same problem or was already fixed independently there.

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

**Note (2026-09-17, user instruction):** Option cards and Haywire cards are not being worked
on yet. Do not remove "dead" code related to either (unwired `tOptionCardCommandType` entries,
`OptionCard`/`OptionCardList`, Haywire handling in `CardList.cs`, etc.) until this is picked up.

---

## Section 2 — Robot Hardware
*VEX AIM physical integration.*

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

- [ ] Shutdown toggle on phone UI
  - Player can choose to shut down during programming phase

- [ ] Show what cards were played last turn (phone/GM UI) — right now only the *current*
  turn's registers are visible (`CardsPlayed`/`CardsPlayedStr` on `PlayerState`, computed live
  from `GameCards`); once the next turn starts (`MoveCardsShuffleAndDeal()` at state 2), that's
  gone. The data already exists: `DataService.Players.cs`'s `CurrentPosSave()` (called at state
  5, just before locking programs and executing) snapshots that turn's `MoveCards` into
  `HistoryMoveCards` keyed by `GameID`/`Turn`/`CardID`/`Owner`/`PhasePlayed`/`Locked` — so this
  is a display gap, not new tracking. Needs: a query by `GameID`+`Turn`-1 (or whatever turn was
  last locked) per robot, and somewhere to show it — e.g. a small "last turn" strip under the
  current registers, shown on tap like the F/E/C header toggle.

- [ ] Handle Haywire / Spam / option card notifications on phone

- [ ] Every phone still receives every player's hand in the broadcast payload, not just its
  own (`documents/API_DECOMPOSITION_DESIGN.md` §7, Medium; the password leak this item used to
  also cover is already fixed). Needs per-seat SignalR groups or payload filtering. **Lowered to
  low priority 2026-09-16** — closed system, consistent with the cookie-login item deciding "do
  not worry about sending all cards to all robots." Client-side login already hides it from a
  casual player; this item is only about the raw broadcast payload still carrying it.

### GM Control Page *(in program section of index.html)*

- [-] Game controls — Start, Restart, Reboot buttons (mapped to game state transitions) on a menu that appears when clicking on "Robots" — **partial, 2026-09-15.** The "Robot" header
  is now tappable (GM view only, see below) and opens a menu (`#gmMenu` in `index.html`,
  `toggleGmMenu()`/`gmAction()` in `js/loadrobots.js`) hitting the same `/api/state/{action}`
  routes `gmindex.html`'s links already use. Built with the game-state actions that actually
  exist today — Start Game, Next State, End Game — rather than literal "Restart"/"Reboot"
  buttons, since neither of those is a real distinct action in the codebase (no "restart game"
  endpoint, and "Reboot" is the still-unbuilt per-robot mechanic in Section 1, not a game-level
  control).

- [ ] Dynamic button area — display any buttons/actions that appear based on game state
  - e.g. "Advance Phase", "Skip Robot", admin overrides

- [ ] Link to board viewer showing current robot positions

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

- [ ] **Security: DB password committed in tracked `appsettings.json`** (`ConnectionStrings:Rally`,
  `pwd=rallypass`) in both `MRR/appsettings.json` and `MRR.Config/appsettings.json`
  (`PROJECT_STATUS.md` §4.7). Move to an untracked `appsettings.Production.json` or an
  environment variable before this repo is ever made public. **Lowered to low priority
  2026-09-16** — this is a totally closed system (isolated game network, no public exposure),
  so the "before it's ever public" condition isn't imminent. Still worth doing eventually,
  just not urgent.

- [ ] `SqlGateway.ExecuteSQL`/`GetQueryResults` silently swallow database errors and return
  empty results instead of surfacing them (`PROJECT_STATUS.md` §6.6) — a bad connection string
  or a bad query currently shows up later as an unrelated NullReferenceException instead of a
  clear DB error at the source

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

- [ ] `Robots.Password`'s source is still undecided (found 2026-09-17 while designing the
  `GameState=0`/`1` seat-claim flow, "Operator Data Setup"): it's a real, live column (used for
  phone login), but nothing in the seat-claim save path (`DataService.SelectSeat`) sets it.
  Needs a decision on whether/how it gets populated now that seats are claimed directly into
  `Robots` instead of via `OperatorData`.

---

## Section 7 — Dead Code Removal

- [ ] `HasOptionCard` (`Players.cs`) — no callers; stub that always returns false. **Do not
  remove yet (2026-09-17)** — Option cards aren't implemented yet; leave this in place.
- [ ] `AdminApi.cs:155` computes `players = data.AllPlayers.Count` — cosmetic only, could become
  a `COUNT(*)` now that `AllPlayers` isn't the source of truth elsewhere; no correctness need

---
## Section 8 — Game Screen / GM UI

*Combined 2026-09-11 from the former Section 8 (Game Screen), Section 9 (Robot Connection
Screen), and Section 10 (Update index to create a GM screen) — all three described the same
evolving GM screen.*

### Merge Connections Into the Index / GM Screen

 - [-] copy "connections" functionality into the index page — **partial, 2026-09-15.** Connect/
   disconnect is in (`js/loadrobots.js`'s `showall()`, hitting the existing
   `/api/robot/connect|disconnect/{id}` routes), including a color-coded connect button in the
   Status column (GM view only) driven by `RobotStatus.StatusColor`, not a hardcoded color.
   `connectscreen.js`'s Search (scan IPs for MAC matches) and Update IP are not copied over —
   `connectscreen.html` is still the only place for those.

---

## Section 9 - Testing

- [ ] Add test coverage for other `CreateCommands` rules as they're touched (conveyor belts,
  pushers, lasers, option cards) — none of that is covered yet; `MRR.Tests` only has the
  position-bug regression tests so far. Run all tests from the repo root: `dotnet test MRR.Tests`.
