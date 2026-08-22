# Mega Robo Rally — Project TODO

**Last updated:** 2026-08-22
**Legend:** `[x]` Done &nbsp; `[-]` Partial / In Progress &nbsp; `[ ]` Not started

---

## Section 1 — Game Mechanics
*Renegade rules completeness.*

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
  - [ ] Still only *announces* the winner — [CreateCommands.cs:1346](../MRR/CreateCommands.cs#L1346)
    adds a `"Game Winner:"` text command, with `SquareAction.GameWinner` commented out. The
    game does not actually end. Issue the `GameWinner` command so `ProcessDbCommand` handles it.

- [ ] Damage card draw mechanic
  - When a robot takes damage, draw top card from damage stack → add to discard
  - Spam execution: play top card from deck without choice
  - Haywire execution: play 5 random cards from deck
  - Trojan Horse execution: all other robots take 1 damage

- [-] Option card effects wired into phase processing (`CreateCommands.cs`)
  - Partial: ReverseGears, FourthGear, RammingGear referenced
  - Missing: Brakes, CrabLegs, Recompile, many others

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
  - See `tools/ai_agent/ws_img_format.md` for what to update if format differs from raw JPEG

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

- [ ] Display robot status on phone (damage, lives, energy, position)

- [ ] Show deck size on player UI
  - Display total cards in the player's personal deck (all MoveCards owned by that robot across all locations except Played Spam / CardLocation=5)
  - Includes accumulated Spam cards so players can see how damage bloats their deck
  - Source: `COUNT(*) FROM MoveCards WHERE Owner=robotID AND CardLocation != 5`
  - Expose via a new column in `viewRobots` or `viewRobotsMicro`, then surface in `CardsDealt`/`AllDataUpdate` JSON

- [ ] Handle Haywire / Spam / option card notifications on phone

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

---

## Section 6 — Infrastructure / Setup

- [ ] Entity Framework for game setup / initialization
  - Use EF (`MRRDbContext` already exists) for initial game setup steps
  - `GameController.StartGame()` / `LoadGameData()` still use raw SQL string building

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
    hand in the board-editor `PUT` ([Program.cs](../MRR/Program.cs#L578)).

---

## Section 7 — Dead Code Removal

- [ ] `SetArchiveToCurrent` (`Players.cs:87`) — no callers; updates archive pos from current pos
- [ ] `HasOptionCard` (`Players.cs`) — no callers; stub that always returns false
- [ ] `MoveUnlimitedAsync` (`Players.cs`) — no callers; sends continuous drive command
- [ ] `ShowAIAsync` (`Players.cs`) — no callers; triggers AI vision overlay on robot LCD

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
