# Mega Robo Rally — Project TODO

**Last updated:** 2026-05-05
**Legend:** `[x]` Done &nbsp; `[-]` Partial / In Progress &nbsp; `[ ]` Not started

---

## Section 1 — Critical Path
*Game cannot start or run without these.*

- [ ] Set initial starting positions for robots before creating the command list
  - Robots need a valid board position + direction before ExecuteTurn runs
  - Related: `procVerifyPosition`, `procSetRobotDirection` (see Section 7)

---

## Section 2 — Game Mechanics
*Renegade rules completeness.*

- [ ] Reboot mechanic
  - Triggered when robot moves into a pit or off the board
  - Robot placed at chosen reboot token; receives 2 Spam cards; continues this turn
  - Needs: pit/edge detection in `CreateCommands` + `DataService` respawn logic

- [-] Board element activation — conveyor belts (`CreateCommands.cs`)
  - [x] Express belts move first (2 squares), then all belts (1 square)
  - [x] Chained movement: robot landing on a second belt also moves
  - [ ] Merge conveyor belts (splitting paths converge)

- [ ] Board element activation — pushers (`CreateCommands.cs`)
  - Activate only on specific phases (odd or even, marked per pusher)
  - Push robot one square; chain-pushes if another robot is in path

- [x] Board element activation — gears (`CreateCommands.cs`)
  - CW gear: rotate robot 90° right; CCW gear: rotate robot 90° left

- [x] Board element activation — board lasers (`CreateCommands.cs`)
  - Fire each phase; damage any robot in line of sight
  - Wall-blocked: walls on the far side of source/target stop the laser

- [x] Board element activation — robot lasers (`CreateCommands.cs`)
  - Each robot fires 1 laser forward; damages first robot in path
  - Option cards: RearLaser (fires backward too), HighPowerLaser (2 damage)

- [x] Flag / checkpoint detection (`CreateCommands.cs` + `GameController.cs`)
  - End of each phase: robot on flag N (where N == LastFlag+1) touches it

- [ ] Win condition (`GameController.cs`)
  - After flag check: if `robot.LastFlag == TotalFlags` → that robot wins
  - End the game and display winner

- [ ] Shutdown mechanic (`GameController.cs` + phone UI)
  - Player announces shutdown during programming phase
  - Shut-down robot: takes no laser damage, cannot move, may clear damage cards

- [ ] Damage card draw mechanic
  - When a robot takes damage, draw top card from damage stack → add to discard
  - Spam execution: play top card from deck without choice
  - Haywire execution: play 5 random cards from deck
  - Trojan Horse execution: all other robots take 1 damage

- [-] Option card effects wired into phase processing (`CreateCommands.cs`)
  - Partial: ReverseGears, FourthGear, RammingGear referenced
  - Missing: Brakes, CrabLegs, Recompile, many others

---

## Section 3 — Robot Hardware
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
  - Update arrow any time the robot's direction changes

- [ ] LED state machine across game phases (`Players.cs`, `CommandProcess.cs`, `GameController.cs`)
  - **Connected / waiting for program** → LEDs ON (robot color) — `SendColorStatus()` already does this at connect time
  - **Program complete** (all 5 registers filled / programs locked, state 5) → LEDs OFF
  - **Executing move** (between `SendRobotCommandAsync` start and `isMoving` → false) → LEDs ON
  - **Move complete, waiting for next program** (state 12 → 2) → LEDs ON again
  - Implementation: call `SetLedAsync` at each transition point in `CommandProcess.ProcessCommand` (before/after move) and in `GameController` state transitions (state 5 = off, state 2 = on)

- [-] Confirm `isMoving` is set and unset correctly (`Players.cs`)
  - Set to `true` when a move command is sent (ack response `status == "in_progress"`)
  - Set to `false` by `ListenStatusAsync` → `ProcessStatusEvent` when motion flags clear
  - [x] `CommandProcess.cs` polling wired: `SendRobotCommandAsync` no longer blocks on `WaitForMotionCompleteAsync`; StatusID 2→3→4 flow now polls `isMoving` live
  - [ ] Verify on hardware: confirm `isMoving` goes `true` on ack and `false` on motion complete under real robot conditions

- [ ] Remove old/unused communication code
  - Audit `Players.cs` and `Program.cs` for any leftover WebSocket stubs or dead paths

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

## Section 4 — UI
*`wwwroot/`*

### Player Programming UI (`index.html`)

- [x] Card programming interface — players see hand and place cards into registers
- [x] Show player hand and register state in real time via SignalR

- [ ] Allow player to set facing direction after reboot
  - When a robot reboots, the player must choose which direction it faces
  - Need a direction-picker UI on the player's phone (`index.html`)
  - Ties into the reboot mechanic (Section 2)

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

## Section 5 — Raspberry Pi Hardware
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

## Section 6 — Network Setup
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
  - Set hostname `mrobopi3` to resolve to this IP on the game network (via router DNS or `/etc/hosts`)
  - Update connection strings / launch URLs in `appsettings.json` if IP differs from current config

### Robots

- [ ] Connect all 6 AIM robots to the game WiFi (`MRR-Game` SSID)
  - Use the VEX AIM app or built-in setup to join the game SSID
  - Note each robot's MAC address for DHCP reservation

- [ ] Assign static IPs to all 6 robots
  - Configure DHCP reservations on the game router by MAC address
  - Suggested scheme: `192.168.4.101`–`192.168.4.106` for robots 1–6

- [ ] Update robot IP addresses in the database
  - Enter confirmed IPs into the `Robots` table (`IPAddress` column)
  - `AIMRobot.cs` reads IP from DB at connection time — must match actual robot IPs

### Player Phones

- [ ] Connect player phones to the game WiFi (`MRR-Game` SSID)
  - All 6 player phones join the same SSID as the robots
  - Phones open the player UI at `http://192.168.4.10:{port}/` (or `http://mrobopi3:{port}/`)

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

## Section 7 — Infrastructure / Setup

- [ ] Entity Framework for game setup / initialization
  - Use EF (`MRRDbContext` already exists) for initial game setup steps
  - Currently using raw SQL for `procGameNew` / `procResetGame`

- [ ] Convert remaining SQL stored procedures to C# (see `sql-to-csharp-conversion-list.md`)

  **High priority:**
  - [ ] `procResetPlayers` — advances shutdown states, respawns dead robots, resets option play records; called each turn start (`GameController.cs:259`)
  - [ ] `procMoveCardsShuffleAndDeal` — shuffles and deals 9 cards to each player at turn start
  - [ ] `procUpdateCardPlayed` — called every time a phone submits a card into a register
  - [ ] `procUpdateRobotCards` + `procMoveCardsCheckProgrammed` — rebuilds CardsDealt/CardsPlayed strings; checks when all players are ready
  - [ ] `funcProcessCommand` (DB-category commands) — C# handles robot/user categories; MySQL still handles DB-category; see `sql-to-csharp-conversion-list.md`
  - [ ] `funcGetNextGameState` — DataHub still calls it directly for phone-triggered transitions
  - [ ] `Robots_BEFORE_UPDATE` trigger — damage cap → death; ShutDown=4 → clear damage; ShutDown=2 → status=9; silent data bug without this
  - [ ] `funcDealSpamToPlayer` — inserts a Spam card into the player's discard pile when a robot takes damage
  - [ ] `procGameFillPrograms` — auto-fill empty registers (classic rules damage > 4)
  - [ ] `procCurrentPosSave` / `procCurrentPosLoad` — state snapshot for state 16
  - [ ] `procDealOptionToRobot` — deal option cards to robots
  - [ ] `procVerifyPosition` — validate robot position (no collision, non-zero)
  - [ ] `funcGetNextCard` — draw next card; reshuffle discard if deck empty
  - [ ] `funcGetProgramReadyState` — returns programming readiness state

  **Medium / low priority:**
  - [ ] `procUpdatePlayerPriority` — round-robin priority rotation
  - [ ] `procRobotConnectionStatus`, `procKickstart`, `procSetStatus`, `procProcessOption`
  - [ ] `Robots_AFTER_UPDATE` trigger (sync StatusLEDs via `procSetStatus` equivalent)
  - [ ] `CurrentGameData_BEFORE_UPDATE`, `GameData_BEFORE_UPDATE` triggers

- [ ] Recover / define missing view definitions
  - `viewRobotsInit` and `viewRobotsRefresh` definitions are missing from SQL files
  - Currently queried from C# but definition source is unknown

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
- [x] `viewRobots`, `viewRobotsInit`, `viewRobotsRefresh`, `viewRobotOptions` (active views used)
- [x] `CommandList_BEFORE_INSERT` trigger → MySQL AUTO_INCREMENT
- [x] Camera image capture: `Player.GetCameraImageAsync()` via ws_img
- [x] Grid alignment agent: `GridAlignmentAgent.cs` + `GET /api/robot/align/{robotId}`
- [x] SixLabors.ImageSharp 3.1.12 added for image processing (Pi-compatible)
