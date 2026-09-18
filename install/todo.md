# Mega Robo Rally — Project TODO

**Last updated:** 2026-09-16
**Legend:** `[x]` Done &nbsp; `[-]` Partial / In Progress &nbsp; `[ ]` Not started

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

Also resolved since the last pass, no longer tracked as open:
- Win condition only announcing the winner and continuing play (Section 1) — confirmed
  *intended* 2026-09-16, not a gap; `SquareAction.GameWinner` stays commented out on purpose.
- `drive_for` distance calibration, the systemd install on `mrobopi`, and the `AllPlayers`
  manual verification pass (Sections 2, 4, 8) — all validated done by user 2026-09-16.

Everything else in the sections below is real but lower-stakes: UI polish, dead-code removal,
doc reconciliation, and the network-setup checklist (Section 5 — unverified whether it's still
literally all open, or just not updated after being done by hand).

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
  - Player must choose the direction the robot faces when placed at the reboot token —
    **the picker itself is done (2026-09-15), see Section 3.** Once respawn logic exists and
    sets `PositionValid = 0` the same way game start already does, the phone UI will show the
    direction picker with no further wiring needed here.
  - Needs: pit/edge detection in `CreateCommands` + `DataService` respawn logic; ~~direction
    picker on phone UI~~ (done); ~~`procSetRobotDirection` equivalent~~ (done, see Section 6)

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

- [x] Win condition (`CreateCommands.AddFlag`)
  - [x] Flag comparison fixed 2026-08-22. Was comparing against a hardcoded 5
    (`Player.TotalFlags` was `get => 5; set {}`), so any board without exactly 5 flags
    scored wrong. Now one game-wide `TotalFlags` in `CurrentGameData` (iKey 7), taken from
    the board at game start; `AddFlag` returns true on `LastFlag >= TotalFlags`.
  - [x] Only *announces* the winner, does not end the game — **confirmed intended 2026-09-16,
    not a bug**: [CreateCommands.cs:1346](../MRR.Rules/CreateCommands.cs) adds a
    `"Game Winner:"` text command with `SquareAction.GameWinner` left commented out, on purpose.
    User wants play to continue for the fully allotted time after the win is announced, not have
    the game cut short — the current behavior (message only, keep playing) is exactly that.
    `ProcessDbCommand`'s unreachable `GameWinner` case (`DataService.Commands.cs:180-184`, sets
    `GameState = 11`) stays dead code deliberately; do not wire it up. `documents/DB_SYNC_ISSUES.md`
    #12 (about that same dead case) is likewise moot, not something to fix.

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

- [x] Calibrate `drive_for` distance for one board square — **validated by user 2026-09-16**
  against the physical board. (`distance = value * 77mm`, `robo-rally-dev.md §4.6`.)

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

- [x] Allow player to set facing direction after reboot — **done 2026-09-15, revised same
  day (twice).** The `Direction1.png` arrow (recolored purple) loops through the 4 facing
  directions for whichever robot's hand is showing (`js/loadrobots.js`: `cycleDirection`/
  `renderDirectionArrow`/`updateDirectionPicker`), drawn rotated relative to that seat's own
  orientation (`DirectionAdjustment`/`PlayerViewDirection`, from `SeatOrientation`) rather than
  raw board-absolute Up/Right/Down/Left, so "arrow points up" always means "facing the way I
  am" regardless of which side of the table the phone is on. Cycling writes `CurrentPosDir`
  immediately (`/api/player/4/...` → `DataService.SetRobotDirection`, the fixed C# port of the
  buggy `procSetRobotDirection`) but does **not** mark the position valid — a separate "Set
  Direction" button (`/api/player/5/...` → `DataService.ConfirmRobotDirection`) does that, so a
  player can freely cycle before committing.

  `Robots.PositionValid` (now surfaced on `AllDataPayload`, previously commented out) is a
  3-state flag, not a bool: 0=not set, 1=user set, 2=locked. A normal player sees the picker
  only at 0; GM (once tapped into GM view, `isGmModeActive()` — logged in as GM alone isn't
  enough) sees it at 0 or 1 and the same button toggles 0↔1, but never at 2 either way — see
  `DataService.Players.cs`'s comment above `ConfirmRobotDirection` for the full state table.
  `GameController.NextState()`'s state 4→5 transition ("still programming" → "ready to
  execute") now refuses to advance while any robot is still at 0
  (`DataService.AllRobotDirectionsChosen`), then locks every robot to 2
  (`DataService.LockAllRobotDirections`) the moment it does advance — so a turn can never
  start executing with a robot still at its unconfirmed default facing, and the picker
  disappears everywhere once that turn is running.

  `GameController.StartGame()` explicitly inserts every robot with `PositionValid=1` (revised
  2026-09-16, was left at the schema default of 0): the board's `PlayerStart` rotation is
  accepted as a reasonable starting facing without forcing every player through the picker
  before the game can begin — `PositionValid=0` (needs attention) is reserved for a mid-game
  respawn (`ResetPlayers()`) or the GM's "Reload Position" action (`CurrentPosLoad()`), both of
  which restore a robot's facing with no player choice behind it. Not wired to an actual
  reboot yet since the reboot mechanic itself doesn't exist (Section 1) — nothing further to do
  here once it lands, since `ResetPlayers()`'s respawn path already sets `PositionValid=0`.

- [ ] Shutdown toggle on phone UI
  - Player can choose to shut down during programming phase

- [x] Display robot status on phone (damage, energy, position) — not lives; this rules
  version doesn't track lives (confirmed 2026-08-27, see Section 1 note above)

- [x] Show deck size on player UI — mostly done 2026-09-10 ("Add Card Count" / "count spam
  cards"): `Robots.CardCount` is now a real column kept current by `RefreshCardCount`/
  `RefreshFlagEnergyCards` (`MRR/DataService.Cards.cs`) on every path that changes a robot's
  cards (deal, shuffle, expire, `DealSpamToPlayer`), surfaced as `FlagEnergyCards`
  ("LastFlag/Energy/CardCount") on `AllDataPayload` and rendered by `loadrobots.js` on
  `index.html`.
  - Display total cards in the player's personal deck (all MoveCards owned by that robot across all locations except Played Spam / CardLocation=5)
  - Includes accumulated Spam cards so players can see how damage bloats their deck
  - Gap: `RefreshCardCount`'s query (`DataService.Cards.cs:77-92`) counts every `MoveCards` row
    for the robot regardless of `CardLocation` — it does not exclude `CardLocation=5` (played/
    discarded Spam) as this item originally specced. Confirm whether that still matters before
    closing this out.
  - Source: `COUNT(*) FROM MoveCards WHERE Owner=robotID AND CardLocation != 5`
  - Landed on `Robots.CardCount` directly rather than a `viewRobots`/`viewRobotsMicro` column
    (no views exist in this schema per project conventions), surfaced via `AllDataPayload`
    same as planned.

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
  low priority 2026-09-16** — closed system, consistent with the cookie-login item below already
  deciding "do not worry about sending all cards to all robots." Client-side login already hides
  it from a casual player; this item is only about the raw broadcast payload still carrying it.
- [x] Add a cookie to each phone. The cookie is either the RobotID (from the json data file) or "0555" which is the GM login.
     When index.html isloaded, match to the cookie.  If it doesn't mach the GM login on one of the current RobotIDs, request a new login.If it nmatches a RobotID, only allow the player to see cards for that ID.  This is a closed system.  Do not worry about sending all cards to all robots
     — done 2026-09-14, same item as Section 8's "Players will have to log in..." line below; see
     that entry for the implementation writeup.



### GM Control Page *(in program section of index.html)*

- [x] Game selection — dropdown/list from GameData table; button to load selected game.
  Dropdown will show description — **done 2026-09-16.** `#gmtable` (`index.html`, inside the
  shared program area) shows a `GameData.Description` dropdown + Start button, but only in GM
  mode while `IsRunning` is falsy (no game currently running) — no point offering to start a
  new game over a live one. New phone-reachable `GET /api/gamedata` (`Program.cs`) returns
  `{GameDataID, Description}` for every row (deliberately not `/api/admin/tables/GameData`,
  which is loopback-only and would refuse every phone on the game WiFi). "Start" calls the
  existing `/api/state/startgame/{GameDataID}` route (`GameController.LoadGameData`) with the
  selected row's ID — the same route the GM menu's plain "Start Game" button already hits
  with no ID.

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

- [x] Controls to manually set a robot's facing direction — **done 2026-09-15**, same
  direction-picker button as the player UI item above (Section 3 top): GM isn't restricted
  from viewing/acting as any robot, so it's the same control, not a separate GM-only one.
  `DataService.SetRobotDirection` is the `procSetRobotDirection` equivalent.
  - Needed at game start when robots are placed on the board
  - Calls `procSetRobotDirection` equivalent in C#

- [x] Show deck size per player on GM UI
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

- [x] **Install the systemd process manager on `mrobopi`.** Design + reference implementation
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

  Installation on the physical host — **validated by user 2026-09-16**. `PROJECT_STATUS.md`'s
  "`mrrctl` isn't on `mrobopi`'s PATH, started by hand" description is now stale; update it
  next time that doc is touched.
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

- [x] Entity Framework for game setup / initialization
  - Use EF (`MRRDbContext` already exists) for initial game setup steps
  - `GameController.StartGame()` / `LoadGameData()` still use raw SQL string building

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

- [x] Add the new `FieldEnclosed` `CurrentGameData` row (iKey 21) to the test MySQL copy —
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

All items in this list are now closed — fixed, found moot by an earlier refactor, or confirmed
not to be a real bug. See `documents/DB_SYNC_ISSUES.md` for the full writeup of each. The pattern
keeps recurring in new code though (#15 below was found three weeks after this list was first
written) — don't treat "all closed" as "can't happen again."

- [x] #4 — Turn counter incremented in DB (`CurrentGameData` iKey=2) but not `_dataService.Turn`
  (`GameController.NextState()`) — **fixed 2026-09-13**. Real and player-visible: the broadcast
  right after this write showed the previous turn's number for the whole programming phase (states
  3/4), since nothing reloaded `_dataService.Turn` before it. Added `_dataService.UpdateGameState()`
  (the `DataService`-level DB reload) right after the raw SQL.
- [x] #6 — Bulk `CommandList` `StatusID` update not reflected in `DataService.ListOfCommands`
  (`GameController.NextState()`) — **fixed 2026-09-12**. `ListOfCommands` was already gone
  (removed 2026-08-30); the live in-memory set is `PendingCommands._commandList`, rebuilt fresh
  from the DB per turn, so the original staleness risk was mostly closed already. The remaining
  gap: `GameController.LoadCurrentGame()`'s raw-SQL reset of stuck commands ran even while a
  `PendingCommands` loop was live (e.g. a GM reset mid-turn), bypassing its EF tracking the same
  way the `ClearPausedCommands`/"clearpause" bug did. Added `PendingCommands.ResetStuckCommands()`
  and routed `LoadCurrentGame()` through it when `_pendingCommands != null`, same pattern as
  `ClearPausedCommands`. See `documents/DB_SYNC_ISSUES.md` #6.
- [x] #7 — `CreateCommands.ExecuteTurn()` writes `GameState` directly via raw SQL, bypassing the
  `GameState` property setter — **moot, checked 2026-09-13**. Already resolved by the Master/
  planner split: the planner returns `TurnPlan.NextGameState` instead of writing DB itself, and
  `GameController.ExecuteTurn()` applies it via the write-through `GameState` property.
- [x] #8 — `CommandList` phase rows deleted in DB but the matching in-memory list not cleared
  (`CreateCommands.ExecuteTurn()`) — **moot, checked 2026-09-13**. Same Master/planner split moved
  this delete into `DataService.PersistCommands()`, always called before that turn's
  `PendingCommands` exists, so there's no live in-memory list for it to leave stale under normal
  state-machine timing. See `documents/DB_SYNC_ISSUES.md` #8 for the (very narrow) case this
  doesn't cover.
- [x] #9 — `MoveCards` table cleared in DB but the `GameCards` collection not cleared
  (`DataService.GameNewAddCards()`) — **fixed 2026-09-13**. `GameController.StartGame()`'s call
  site was already safe (`LoadCurrentGame()` reloads right after). `CurrentPosLoad()`'s call site
  (the "Reload Position" GM action) was not — added `ReloadAllData()` at the end of that method.
- [x] #10 — `ProcessDbCommand`'s `Option.Option` case inserts into `RobotOptions` but doesn't
  update the in-memory `OptionCards` collection — **fixed 2026-09-13**. Added
  `LoadOptionCardsFromDatabase()` right after the insert.
- [x] #11 — `ProcessDbCommand`'s `DealCard` case updates a `MoveCard`'s `Owner` in the DB but
  leaves the matching `GameCards` entry stale — **fixed 2026-09-13**. Added a targeted
  `GameCards` field update, same idiom `UpdateCardPlayed()` already uses.
- [x] #13 — `ProcessDbCommand`'s `SetCurrentGameData` case doesn't refresh `PhaseCount`/
  `LaserDamage` in memory after writing them to the DB — **fixed 2026-09-13**. The iKey is
  caller-chosen so no single field can be targeted; added `UpdateGameState()` (full
  `GameStateStore` reload) after the write.
- [x] #14 — `UpdateCardPlayed()` leaves `Player.CardsDealt`/`CardsPlayed` stale after its DB
  update — **already fixed, undated; confirmed 2026-09-13**. `UpdateCardPlayed` already has an
  explicit in-memory `GameCards` sync step, and `Player.CardsDealt`/`CardsPlayed` no longer exist
  as stored fields — they're computed live off the shared `GameCards` reference.
- [x] #15 — `GameController.LoadGameData()` writes the new `BoardID`/`OptionCount`/`PhaseCount`/
  etc. into `CurrentGameData` via raw SQL but never refreshed `GameStateStore` — **fixed
  2026-09-16**. Found via a real bug report: starting a new game with a different player count
  left the `Robots` table wrong until Start Game was clicked a second time. `LoadGameData()` runs
  immediately before `StartGame()` (`Program.cs` → `SetGameState(0)` → `NextState()` case 0), and
  `StartGame()` reads `_dataService.BoardID` to find the board's `PlayerStart` squares — with the
  stale cache, the first call built the robot table against the *previous* board. Added
  `_dataService.UpdateGameState()` at the end of `LoadGameData()`, same fix as #4. See
  `documents/DB_SYNC_ISSUES.md` #15.

### Doc housekeeping (found while auditing docs 2026-08-30; re-verified in code 2026-09-16)

- [x] `documents/API_DECOMPOSITION_DESIGN.md` §7's defects table had rows that read as stale
  against `PROJECT_STATUS.md` — **confirmed stale 2026-09-16, table needs a pass**: grepped
  `Program.cs` and confirmed `/api/execution/abort` exists (abort path row is stale) and no
  `/api/table` route remains, only a comment noting it was replaced by `MRR.Admin` (that row is
  stale too). "Phone receives password" was already correctly noted as fixed, only the
  hand-visibility half open (see Section 3). Table itself not yet edited — still an actual
  editing pass to do in `API_DECOMPOSITION_DESIGN.md`, just no longer an open question.
- [x] **Confirmed fixed 2026-08-30**: "`/` returns 404" — `MRR/Program.cs` now calls
  `UseDefaultFiles()` before `UseStaticFiles()` (with a comment noting exactly this history), so
  `/` does serve `index.html`. `install/PROCESS_MANAGER.md` §6 still described this as an open
  fix to make; corrected there too. One leftover: the `/api/health` comment a few lines below it
  in `Program.cs` still says "UseStaticFiles is registered before UseDefaultFiles above" — now
  false, harmless, but worth a one-line fix next time that function is touched.
- [x] Same doc's §9 step 6 (Device Gateway) says "Not started"; `PROJECT_STATUS.md` §5.1 (same
  date) says "Partial — dispatch bugs fixed; `IRobotTransport` remains." **Reconciled
  2026-09-16, not actually a conflict**: `grep -rn IRobotTransport MRR/` finds zero matches — the
  type genuinely doesn't exist in code, so "Not started" is correct for the type extraction
  itself. "Dispatch bugs fixed" refers to something narrower and separate: the command-hang fix
  (`CommandProcess.CommandDeadline`, see next item), not the failed-send-applies-anyway bug,
  which Section 2 above still lists open. Both docs are correct read precisely; worth rewording
  PROJECT_STATUS.md's line so it doesn't imply more progress than "Not started" next time someone
  skims just one of the two docs.
- [x] Same doc's §5.4 still lists "busy-wait, no timeout" as open; `PROJECT_STATUS.md` §6.6 says
  commands now time out. **Resolved 2026-09-16, no longer a real concern**: confirmed in
  `CommandProcess.cs` — `CommandDeadline = TimeSpan.FromSeconds(10)`, matching what
  `PROJECT_STATUS.md` §6.6 already says (this todo item's own paraphrase had drifted to "30s" —
  that was the error, not the doc), enforces the timeout, and
  `PollInterval = TimeSpan.FromMilliseconds(20)` means the loop sleeps between checks rather than
  spinning. Both halves of the original complaint are done; §5.4 can be marked resolved.
- [x] Git branch `pre-decomposition-cleanup` was well ahead of `origin` as of `PROJECT_STATUS.md`
  §5.3 — **confirmed merged 2026-09-16**: `git merge-base --is-ancestor origin/pre-decomposition-cleanup
  origin/main` succeeds and the branch is 0 commits ahead of `origin/main`. Safe to delete the
  branch and drop this line from `PROJECT_STATUS.md` §5.3.

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

- [x] `RefreshPlayerCards` (`DataService.Cards.cs`) — deleted, along with its seven call sites
  in `Program.cs`, `GameController.cs` and `RobotScreenUI.cs`. It was a no-op; `UpdateCardPlayed`
  step 8 already syncs the moved cards in memory directly.
- [ ] `SetArchiveToCurrent` (`Players.cs:87`) — no callers; updates archive pos from current pos - This is no longer required
- [ ] `HasOptionCard` (`Players.cs`) — no callers; stub that always returns false. **Do not
  remove yet (2026-09-17)** — Option cards aren't implemented yet; leave this in place.
- [ ] `MoveUnlimitedAsync` (`Players.cs`) — no callers; sends continuous drive command
- [ ] `ShowAIAsync` (`Players.cs`) — no callers; triggers AI vision overlay on robot LCD
- [ ] Dead commented-out line at `CommandList.cs:297` (found during `ALLPLAYERS_REMOVAL_DESIGN.md` review)
- [ ] Dead commented-out line at `Program.cs:228` (same review)
- [ ] `AdminApi.cs:155` computes `players = data.AllPlayers.Count` — cosmetic only, could become
  a `COUNT(*)` now that `AllPlayers` isn't the source of truth elsewhere; no correctness need

---
## Section 8 — Game Screen / GM UI

*Combined 2026-09-11 from the former Section 8 (Game Screen), Section 9 (Robot Connection
Screen), and Section 10 (Update index to create a GM screen) — all three described the same
evolving GM screen.*

### Game Lifecycle

- GM screen needs a way to end the current game.
- `CurrentGameData` should have a flag to determine:
  - Whether a game is currently in progress (and whether we need to connect to the robots)
  - What should be displayed on the player interface (e.g. "Game setup in progress")

  Using GameState in CurrentGameData (GameState==25 means "no game running" — was a separate
  IsRunning flag until 2026-09-17, see the checked item below):
  When the pi boots, or app starts, if GameState!=25, connect to the robots, and store that robots are connected.
  When GameState is set to 25, disconnect from robots.
  When a game is started, GameState moves off 25 on its own via the normal state machine.

- [x] Removing AllPlayers from main code — design doc written and rollout implemented
  2026-08-27/30, see `documents/ALLPLAYERS_REMOVAL_DESIGN.md`. `Robots` is now read fresh from
  the DB per broadcast; `AllPlayers` remains only where the doc identifies it's still needed
  (command creation, connection registry).

- [x] Manual verification pass for the AllPlayers removal (`ALLPLAYERS_REMOVAL_DESIGN.md` §10) —
  **validated by user 2026-09-16**:
  - [x] Phones' displayed status/cards update every broadcast
  - [x] `CommandList` descriptions ("played card: X") still render correctly
  - [x] `/api/admin/diagnostics` still reports `robotsConnected`
  - [x] Robot disconnect/reconnect mid-game still works
  - [x] Re-check `UpdateCardPlayed` specifically through a full programming→lock→execute cycle
  - [x] Play a multi-turn game confirming pit-death/Damage-threshold behavior holds turn after turn

### Robot Connection Screen

- Update the IsConnected flag in Robots to ConnectStatus
  - Add the needed statuses to the RobotStatus table
  - Link ConnectStatus to the RobotStatus table
  - Update all references to IsConnected

Create a small form. Data should be pulled using the same subscription as index.html
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

- [x] Connection screen kept showing a robot as green/Connected after it silently dropped
  mid-turn — **fixed 2026-09-16**. `Robots.ConnectStatusID`/`ConnectStatusColor` only ever got
  written by an explicit Connect/Disconnect action (`GameController.SetRobotConnectStatus`);
  `RobotConnection.IsConnected` going false on its own (inside `SendCommandAsync`'s catch block,
  Device Gateway code with no DB access) never told the DB. Extracted the write into
  `DataService.SetRobotConnectStatus` (`DataService.Players.cs`) and call it from
  `CommandProcess.ProcessCommand` (`CommandProcess.cs`) the moment it finds `!robot.isConnected`
  mid-turn -- the existing `PublishSnapshot()` a few lines later in that same polling loop
  broadcasts it, no new broadcast needed. Deliberately still only touches
  `ConnectStatusID`/`ConnectStatusColor`/`ConnectStatusDesc`, same as before -- never
  `Robots.Status`/`StatusColor` (the gameplay columns); a dropped connection must not look like
  a gameplay status change.

### Merge Connections Into the Index / GM Screen

 - [-] copy "connections" functionality into the index page — **partial, 2026-09-15.** Connect/
   disconnect is in (`js/loadrobots.js`'s `showall()`, hitting the existing
   `/api/robot/connect|disconnect/{id}` routes). `connectscreen.js`'s Search (scan IPs for MAC
   matches) and Update IP are not copied over — `connectscreen.html` is still the only place
   for those.
   - [x] set the status field to be a button that the GM can use to connect when a robot is not connected.
   - [x] background of Status should be red when not connected, but only on the gm screen —
     driven by `RobotData.ConnectStatusColor` (the DB's own `RobotStatus.StatusColor` for
     `NotConnected`/20), not a hardcoded color, so it stays in sync with the Robot Connection
     Screen's own red/yellow/green/purple scheme.
 - [ ] ~~Gm screen will have a "Next" button at the bottom of the program commands table~~
 - [x] GM screen will show all buttons players see — GM view is additive (`gmViewActive` in
   `js/loadrobots.js`): it reveals the Robot-header menu and the Connect status column on top
   of the same card-programming UI every player already sees, never hides or replaces it.
 - [x] Tap on the game message (like "Turn 2") will toggle between the player view and the GM
   view (only when GM mode is enabled) — done 2026-09-15, `toggleGmView()` in
   `js/loadrobots.js`, gated on `IsGM` (a no-op tap for anyone not logged in as GM).
 - [x] Players will have to log in and the browser will hold a cookie of the player login —
   done 2026-09-14, `index.html`/`js/loadrobots.js` only (no server changes): the cookie itself
   is the identity (a RobotID, or GM code `0555`), matched client-side against
   `datapacket.robots` on every broadcast; no match shows a login modal (tap your robot, or
   type the GM code). A logged-in player's `showplayerprogram()` call refuses to switch to
   another robot's row; GM is unrestricted. Deliberately simpler than
   `documents/PHONE_LOGIN_DESIGN.md`'s design (no PIN/password check, no server-side session
   registry, no per-seat broadcast filtering) — explicitly out of scope per this task ("closed
   system... do not worry about sending all cards to all robots"). That doc's tracking/
   enforcement pieces (`PhoneConnected`, `whoami`, SignalR connect tracking) are still undone
   if wanted later.

### Operator Data Setup (new, 2026-09-17)

**Status: requirements being gathered — not ready to build yet.** User will keep updating
requirements here before implementation starts.

**Implemented 2026-09-18** (the `GameState=0`/`GameState=1` flow below, end to end): 
`GameController.StartGame()` now builds one placeholder `Robots` row per `RobotBases` row
(`DataService.InsertPlaceholderRobot`) and connects to all of them; `NextState()`'s `case 1`
waits (no `SetGameState` call, same pattern as `case 4`) until every row has `Status=1`, then
does what the removed `LoadPlayersIntoGame()` used to do once its bulk insert finished (deal
starting options, deal the deck, set initial `Priority`) and advances to state 2.
`DataService.SelectSeat` is the one atomic claim (turn order + both uniqueness checks in a
single `WHERE`), exposed as `GET /api/setup/select/{seat}/{startPosition}/{robotBodyId}/
{operatorName}` (`Program.cs`); `DataService.BuildGameConfig` feeds `AllDataPayload.GameConfig`
(`PlayerToSelect`/`AvailableRobots`/`AvailableStartPositions`, only present while
`GameState==1` — `NullValueHandling.Ignore`, not a schema change to `MRR.Contracts`). Phone side:
`js/loadrobots.js` gates the *entire* normal render path (`applyLogin`/`showall`/
`showplayerprogram`) out of `GameState==1` and shows a separate `#setupScreen` instead.

**Updated 2026-09-18, same day:** robot-based login removed entirely per the user ("I only need
to log in for the seat now") — there's no more "tap your robot" list in `#loginModal` (GM code
only now) and no more separate setup-only cookie. `LOGIN_COOKIE` (`mrr_seat`) is claimed once via
`chooseSetupSeat()` during `GameState==1` and stays a player's identity for the rest of the game;
`applyLogin()` now matches it against whichever robot currently has that `PlayerSeat`, instead of
matching a fixed `RobotID`. The visible "Log Out" link/page from earlier the same day was
replaced with a hidden gesture (5 taps on the "Robot" header within 10 seconds,
`handleRobotHeaderTap()`) that opens `logout.html` — there's no other per-player action to hang a
visible logout control off of now that login has no button of its own. Known gap: a player whose
seat cookie doesn't match anything mid-game (cleared, or a genuinely new phone joining after
setup already finished) has no self-service way back in from the main page — the modal left there
is GM-only. Not treated as a regression to fix now, since it wasn't handled under the old design
either (a robot-based cookie could at least always re-pick from the same modal).

**Updated 2026-09-18, closing the gap above:** `#loginModal` is no longer GM-only — it's one
text box (`attemptSeatLogin()`) that takes either a seat number or the GM code, so a player
whose cookie stops matching (cleared, or a new phone) can now type their seat back in from the
main page instead of being stuck at a GM-only prompt. The `GameState==1` setup screen's old
button-per-seat `renderSeatPicker()`/`chooseSetupSeat()` was removed in favor of reusing this
same box (`showSetupScreen()` calls `showLogin()` when the cookie doesn't hold a valid seat for
the current game) — also fixes a latent bug where a GM-logged-in phone landing on the setup
screen was asked to pick a seat every time (it now just shows a waiting message instead).
`setupNameInput` (operator name entry) was removed too — no name is collected anywhere now;
`DataService.SelectSeat` no longer takes an `operatorName` parameter and just names the claimed
row `"Seat {seat}"`. `GET /api/setup/select/{seat}/{startPosition}/{robotBodyId}` dropped its
trailing `{operatorName}` segment accordingly (`Program.cs`).

Two pre-existing bugs surfaced and fixed along the way, both the same class: an `INNER JOIN
RobotBodies`/`RobotBases` that silently excluded a placeholder row (`RobotBodyID` is `NULL` until
claimed) — `DataService.GetAllPlayers()` and `RobotConnection.LoadFromDatabase()` both used to
join fresh instead of reading `Robots`' own already-denormalized `RobotName`/`RobotColor`/
`RobotColorFG`/`IPAddress` columns; without the fix, `ConnectToAllRobots()` would have silently
connected to zero robots during setup. `RobotScreenUI` (the physical touchscreen programming UI,
`UseRobotScreen`, off by default) was not re-checked against placeholder rows — untested.

**Not done:** no live game has actually been run through this end-to-end yet — build + the
existing `MRR.Tests` suite pass, but verifying against a real game means wiping `mrobopi`'s live
`Robots` table (`StartGame()` still starts with `Delete from Robots;`), so that needs the user to
run it, not this session. Also known but not fixed: re-rendering `#setupScreen` mid-typing (e.g.
another phone's broadcast landing while you're typing your name) will reset the name `<input>` —
low-probability given selection is turn-gated, not fixed here.

- [x] I am totally bypassing the Operator Data table. — confirmed 2026-09-17. This supersedes
  most of "Identify all existing relationships" and "Other needed changes" further down (marked
  there); the new design writes straight into `Robots` at `GameState=0`/`1` instead of building
  or joining `OperatorData` at all.

- [x] Replace "IsRunning" with GameState=25.  Any place IsRunning is set to false, set GameState=25;  Any place we check IsRunning, Check for GameState!=25
  — **done 2026-09-17.** Removed `IsRunning` entirely (`GameStateStore`/`DataService`/
  `GameController`/`AllDataPayload`) rather than keeping it as a computed alias — every read
  site now compares `GameState`/`datapacket.gamestate` to 25 directly (`GameController.cs`'s
  `LoadCurrentGame()`, `js/loadrobots.js`'s `showall()`). `EndGame()` now sets `GameState=25`
  directly. `StartGame()`'s old `IsRunning=true` line was removed outright rather than
  replaced with anything: `GameState` already moves off 25 through the normal state machine
  (the raw SQL a few lines below it, then case 0's `SetGameState(2)` in `NextState()`) by the
  time that state finishes, so nothing else was needed. This only touches the game-level
  "is a game running" flag — `PlayerState.IsRunning` (a per-robot "alive and not shut down"
  property, `Active && ShutDown != Currently`) is a completely different, unrelated concept
  and was left alone.

  The `CurrentGameData` row at iKey 9 (`IsRunning`) is now orphaned — nothing reads or writes
  it — but wasn't deleted from the schema; see `.claude/agents/mrr-database.md`'s reference
  table. Also worth knowing: `GameState`'s seed default in `install/MRRDatabase.sql` is still
  `2`, not `25` — a truly fresh install (before any game has ever started or ended) will
  briefly report as "running" in state 2 until a game actually starts or `EndGame()` runs
  once. Didn't change the seed default since that's a behavior decision, not part of the
  literal ask; flagging it here rather than deciding it silently.

  The RobotID in the robot table is tied to the starting position selected by the player.
  When a player selects a starting position and robotBodyID, those values are updated in the Robots table

- [x] During StartGame (GameState=0), load the Robot Table with the max number of entries, 
  - [x] Set 
    - [x] the Color to black
    - [x] FG color to white
    - [x] Name to the number of the starting position ("Start X") (RobotBaseID)
    - [x] OperatorName to "Seat ?"
    - [x] Set Start X,Y,Dir
    - [x] Status to 0
    - [x] IPAddress to match the robotbase
    - [x] RobotID will match the robotbase.RobotBaseID
  - [x] Connect to robots
  - [x] Set GameState to 1
- [x] When GameState=1 
  - [x] ~~Player may enter "Operator Name"~~ — **reversed 2026-09-18:** decided not to require a
    name after all; the input was removed and `OperatorName` is just `"Seat {seat}"` now (see
    the "closing the gap" note above)
  - [x] send out a modified json
  - [x] In json, have a section: "GameConfig" (this should not be part of the json file the rest of the time)
    - [x] PlayerToSelect - in seat order, the first player who does not yet have a robot row with
      `Status=1` (clarified 2026-09-17)
    - [x] Available (unselected) Robots (include name & colors) array (pull from Robot Bodies table)
    - [x] Available (unselected) Start Positions array  (numbers that are left to select)
    - [x] Once a robot and start position are selected, they are stored automatically — no
      separate Save button (clarified 2026-09-17)
      - [x] store
        - [x] Seat, from device cookie -> Priority & PlayerSeat
        - [x] RobotBodyID
        - [x] StartPosition (RobotID)
        - [x] Operator Name
      - [x] Load name & color from bodies table
      - [x] Update "Position Valid" to 1
      - [x] Update "Status" to 1 
    - [x] When Position Valid ==1 for all entries
    - [x] set GameState=2 and NextState()
  
  
  
- [x] Identify all existing relationships — **2026-09-17 findings.** Kept below as historical
  documentation of the system being replaced, not as input to an `OperatorData` redesign — see
  "I am totally bypassing the Operator Data table" above. None of these relationships need to
  survive into the new design, which writes straight into `Robots` instead.
  - `OperatorData` has **no FK constraints at all** in `install/MRRDatabase.sql` (PK is just
    the composite `(OperatorListID, RobotID)`). Every link below is enforced only by the SQL
    in `GameController.StartGame()` (`MRR/GameController.cs:275-289`), not by the schema.
  - `OperatorListID` ↔ `CurrentGameData` row where `sKey='PlayerListID'` — picks which list is
    active (List 1 = 10 generic players, List 2 = 6-player MRR w/ `StartPosition` set).
  - `RobotBodyID` ↔ `RobotBodies.RobotBodyID` — pulls `Name`/`Color`/`ColorFG`.
  - `RobotID` ↔ `RobotBases.RobotBaseID` — joined as if `RobotID` *is* a `RobotBaseID`, to pull
    `IPAddress`. This is a naming convention (the two ID ranges are expected to line up 1:1),
    not a declared relationship — worth deciding explicitly if it's redesigned.
  - `PlayerSeat` ↔ `SeatOrientation.SeatID` — pulls `Direction`.
  - `RobotID`, `OperatorName`, `Password`, `PlayerSeat` are copied straight across into the new
    `Robots` row at game start (`Robots.RobotID`/`OperatorName`/`Password`/`PlayerSeat`).
  - `StartPosition` is documented (`mrr-database.md`) as "BoardItemActions Parameter for start
    square" but is **not referenced anywhere in current C# code** — confirmed via repo-wide
    search, only appears in docs/todo/SQL files. Not joined in `StartGame()` today; wiring it
    in is part of what this new form/redesign would need to add.
  - `Paid`/`IsActive` have no joins; `IsActive` is only a `WHERE od.IsActive > 0` filter.
  - **Discrepancy to resolve:** the requirements sketch above (line ~811, "RobotBaseID from
    Operator.StartPos") assumes `RobotBaseID` will come from `StartPosition`, but the *current*
    code derives it from `RobotID` instead (`rbase on od.RobotID = rbase.RobotBaseID`). Decide
    which becomes the real link when this is redesigned.


#### Other needed changes found while reviewing this draft (2026-09-17, updated 2026-09-17)

These started as consequences of the `OperatorData`-redesign draft above. **Most are now
superseded** by the "I am totally bypassing the Operator Data table" decision (confirmed
2026-09-17): the new design writes straight into `Robots` at `GameState=0`/`1` instead of
building or joining `OperatorData` at all. Kept below for the still-open items and as a record
of what the bypass resolved.

- [ ] **GameState=1 doesn't exist yet.** States today are 0, 2-16 (CLAUDE.md's table) — there is
  no `case 1` in `GameController.NextState()`. Adding this setup phase means deciding exactly
  where it sits in the state machine (right after 0/`StartGame()`, before 2?) and adding the
  transition + broadcast logic for it. Still open — unaffected by the `OperatorData` bypass.
- [ ] **No robot identity exists yet during this phase.** Updated for the bypass: `Robots` is no
  longer empty going into this phase — `GameState=0` pre-populates one placeholder row per start
  position (`OperatorName="Seat ?"`, black/white colors, `RobotID`=`RobotBaseID`, `Status=0`).
  But those placeholder rows aren't yet tied to any phone. The phone login cookie built earlier
  this session (`mrr_player`) matches against `datapacket.robots` by `RobotID` — so "Seat, from
  device cookie" (line 851) still implies a seat-keyed identity separate from that placeholder
  `RobotID`, for this phase specifically. Still open.
- [x] **`SeatOrientation.Direction` would become player-set, not fixed seed data — superseded by
  the bypass.** The original idea (a new `OperatorData.SeatDirection` column) is moot now that
  there's no `OperatorData` row to hold it. What's left open: `GameState=0`'s "Set Start X,Y,Dir"
  sets direction once from the board's start-position data, and nothing in the current
  `GameState=1` draft (lines 830-859) lets the player adjust it interactively the way the old
  "Direction to GM, click arrow until correct" note described. Not decided whether that
  interactive step still exists, and if so, whether it writes directly to
  `Robots.DirectionAdjustment`.
- [ ] **Uniqueness needs server-side enforcement, not just UI hints.** Updated for the bypass:
  `RobotBodyID` and `StartPosition` ("can not duplicate") need the store step to check against the
  other placeholder rows already claimed in `Robots` (not `OperatorData` rows), and reject a
  duplicate. Initial selection is turn-gated (only `PlayerToSelect` can act), so a race there is
  unlikely rather than impossible. Still open.
- [x] **Two different sources were given for `Robots.Priority` — superseded by the bypass.** The
  `OperatorData`-era ambiguity (`StartPosition` vs. `PlayerSeat` vs. `SeatID`) no longer applies:
  the current `GameState=1` draft sets `Priority` directly from the seat, in one place (line 851),
  with no second source in play.
- [x] **`OperatorData.RobotID`'s changing role, including the primary key — moot.** There's no
  `OperatorData` table left to have a primary key.
- [ ] **`Password`'s source is still undecided — reframed, not resolved, by the bypass.** The old
  `OperatorData` draft's "(robotid) (DNE)" annotation is moot, but the underlying question isn't:
  `Robots.Password` is a real, live column (used for phone login), and the current `GameState=1`
  save list (line 851) doesn't set it at all. Needs a decision on whether/how it gets populated
  under the new direct-to-`Robots` flow.
- [x] **`LoadPlayersIntoGame()` needs a rewrite — superseded 2026-09-17: it shouldn't be its own
  function.** Confirmed by the user. Under the bypass, `Robots` is built up directly across
  `GameState=0` (placeholder rows) and `GameState=1` (per-seat saves), so there's no single
  bulk-INSERT-from-`OperatorData` moment left to house in a dedicated function the way
  `LoadPlayersIntoGame()` does today — that function goes away rather than getting rewritten in
  place.
- [x] **Setup needs to be turn-gated ("Players take a seat and can select items in Seat
  Order") — answered, 2026-09-17.** `PlayerToSelect` is, in seat order, the first player who does
  not yet have a robot row with `Status=1`; each phone gates its own selection UI on whether its
  seat matches it. Selection auto-stores as soon as both `RobotBodyID` and `StartPosition` are
  chosen — there's no separate Save button to gate.

---

## Section 9 - Testing

- [x] `MRR.Tests` (xUnit) added 2026-09-04 — references `MRR.Contracts` and `MRR.Rules` only
  (the pure turn-planning libraries, no DB/ASP.NET), so tests run without a database or robots.
  Run all tests from the repo root:
  ```
  dotnet test MRR.Tests
  ```
  Or run the whole solution's tests (currently just this project):
  ```
  dotnet test Mega-Robo-Rally-SignalR.sln
  ```
  Current coverage: the `CommandItem.EndPos`/`RobotLocation` position bug fixed 2026-09-04
  (`RobotLocationTests.cs`, `CommandItemPositionTests.cs`, `CreateCommandsZeroSquareTests.cs`
  — the last one plans a full turn through `CreateCommands.ExecuteTurn()` and checks the
  planned `Move` command's `EndPos`, the value `ProcessDbCommand` writes into
  `Robots.CurrentPosCol/CurrentPosRow`).

- [ ] Add test coverage for other `CreateCommands` rules as they're touched (conveyor belts,
  pushers, lasers, option cards) — none of that is covered yet; `MRR.Tests` only has the
  position-bug regression tests above so far.

- [x] Test-game DB snapshot added 2026-09-07 — `install/testgame-snapshot.sql` holds a data-only
  dump of `Robots`, `MoveCards`, `CurrentGameData` (schema must already exist, from
  `install/MRRDatabase.sql`) for quickly resetting the live `rally` database to a known
  mid-game state instead of playing a game by hand each time. Reimport is idempotent (each
  table's section starts with `DELETE FROM`, so re-running it re-applies the same snapshot
  cleanly):
  ```
  mysql -h localhost -u mrr -prallypass rally < install/testgame-snapshot.sql
  ```
  Regenerate it from the live database when you want to capture a new test state:
  ```
  mysqldump -h localhost -u mrr -prallypass \
    --no-create-info --skip-triggers --complete-insert --skip-extended-insert \
    rally Robots MoveCards CurrentGameData > install/testgame-snapshot.sql
  ```
  then re-add the three `DELETE FROM <table>;` lines (one before each table's `LOCK TABLES`)
  that mysqldump itself doesn't emit, so a re-import doesn't hit duplicate-key errors against
  whatever is already in those tables.

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
- [x] Fixed 2026-09-14: A "User Input" command (e.g. a continue-button prompt) left StatusID
  stuck at 4 until the player clicks it, and `PendingCommands.ProcessCommands()` treated that
  exactly like "nothing left to do" — it exited, and `StartProcessCommandsThread`'s background
  thread disposed that instance and called `NextState()` to try again, which recreated a brand
  new `PendingCommands` (fresh EF DbContext + DB query) and a brand new `Thread` roughly every
  `PollInterval` (20ms) for as long as the human took to click. Symptom: clicking the button
  often logged "ProcessCommands thread is already running." and needed a manual "Next State" to
  actually unstick — the click's write could land in the gap between one incarnation disposing
  and the next constructing itself, falling through to the DB-only fallback path
  (`GameController.ProcessDbCommand` only syncs a *live* instance's in-memory copy), and the next
  incarnation's fresh query could still lose that race and spin again. Reworked
  `PendingCommands.ProcessCommands()`'s loop to end purely on `active.Count` (removed the
  `stillRunning` no-progress bailout) so one instance just keeps polling in place while blocked
  — `GameController.ProcessDbCommand` then always finds it non-null and the click lands directly
  on the in-memory `CommandItem` this same loop is already checking, no gap, no respawn.
