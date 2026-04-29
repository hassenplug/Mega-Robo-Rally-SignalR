# Mega Robo Rally — Project TODO

**Last updated:** 2026-04-29
**Legend:** `[x]` Done &nbsp; `[-]` Partial / In Progress &nbsp; `[ ]` Not started

---

## Section 1 — Critical Path
*Game cannot start or run without these.*

- [ ] Convert `procResetPlayers` to C#
  - Still called as raw SQL in `GameController.cs:259`
  - Advances shutdown states, respawns dead robots, resets option play records
  - Blocks every turn start

- [ ] Convert `procMoveCardsShuffleAndDeal` to C#
  - Shuffles and deals 9 cards to each player at turn start
  - Blocks every turn start

- [ ] Convert `procUpdateCardPlayed` to C#
  - Called every time a phone submits a card into a register
  - Blocks phone card programming

- [ ] Convert `procUpdateRobotCards` + `procMoveCardsCheckProgrammed` to C#
  - Rebuilds CardsDealt/CardsPlayed strings; checks when all players are ready
  - Blocks programming phase completion

- [ ] Convert `funcProcessCommand` (DB-category commands) to C#
  - Partial: C# handles robot/user categories; MySQL still handles DB-category
  - Every DB-side command during execution calls back into MySQL
  - See `sql-to-csharp-conversion-list.md` for details

- [ ] Convert `funcGetNextGameState` to C#
  - Partial: DataHub still calls it directly for phone-triggered transitions
  - State machine must be fully in C# to run without database logic

- [ ] Replicate `Robots_BEFORE_UPDATE` trigger in C#
  - Damage cap → death; ShutDown=4 → clear damage; ShutDown=2 → status=9
  - Silent data bug in every place that writes Robots without this

- [ ] Convert `funcDealSpamToPlayer` to C#
  - Needed every time a robot takes damage
  - Inserts a new Spam card into the player's discard pile

- [ ] Set initial starting positions for robots before creating the command list
  - Robots need a valid board position + direction before ExecuteTurn runs
  - Related: `procVerifyPosition`, `procSetRobotDirection` (see Section 5)

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

- [ ] LED animation during movement
  - Add `StartMoveAsync()` / `StopMoveAsync()` commands to `Player`
  - Animate LEDs (e.g. chase pattern or pulsing) when a move begins
  - Restore robot color LEDs when movement completes

- [ ] Confirm `isMoving` is set and unset correctly (`Players.cs`)
  - Set to `true` when a move command is sent
  - Set to `false` on `motion_complete` event and `is_moving=false` status event
  - Verify `CommandProcess.cs` polls it correctly for category-1 commands

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
*WiFi router → Raspberry Pi → robots → player phones.*

- [ ] Configure WiFi router
  - Set SSID and password for the game network
  - Use 2.4 GHz band (confirm AIM robots support 5 GHz before switching)
  - Consider a dedicated router / isolated SSID to keep game traffic off the internet

- [ ] Connect Raspberry Pi via Ethernet
  - Wire Pi 5 to router with ethernet cable
  - Assign Pi a static IP or create a DHCP reservation by MAC address
  - Update connection strings / launch URLs in `appsettings.json` and `CLAUDE.md` if IP changes from `mrobopi3`

- [ ] Connect all 6 AIM robots to the WiFi network
  - Use the VEX AIM app or built-in setup to join the game SSID
  - Note each robot's MAC address for DHCP reservation

- [ ] Assign static IPs to all 6 robots
  - Configure DHCP reservations on the router by MAC address so IPs never change
  - Suggested scheme: e.g. 192.168.x.101–106 for robots 1–6

- [ ] Update robot IP addresses in the database
  - Enter confirmed IPs into the `Robots` table (`IPAddress` column)
  - `AIMRobot.cs` reads IP from DB at connection time — must match actual robot IPs

- [ ] Connect player phones to the game WiFi
  - All 6 player phones join the same SSID
  - Phones open the player UI at `http://{Pi-IP}:{port}/` in the browser

- [ ] Verify Pi → robot connectivity
  - From the Pi, confirm WebSocket reachability: `ws://{robot-ip}:80/ws_cmd` for each robot
  - Quick check: `curl http://{robot-ip}:80/` or ping each robot IP

- [ ] Verify phone → Pi SignalR connectivity
  - Open player UI from a phone browser; confirm SignalR hub connects and hand is displayed
  - Check Pi server port is reachable from the phone (default ASP.NET Core port 5000 or 80)

- [ ] Document final IP address assignments
  - Record Pi IP, each robot IP, and router IP in `install/network.md` (or a label on the hardware)

---

## Section 7 — Infrastructure / Setup

- [ ] Entity Framework for game setup / initialization
  - Use EF (`MRRDbContext` already exists) for initial game setup steps
  - Currently using raw SQL for `procGameNew` / `procResetGame`

- [ ] Convert remaining SQL stored procedures to C# (see `sql-to-csharp-conversion-list.md`)

  **High priority (beyond Section 1):**
  - [ ] `procGameFillPrograms` — auto-fill empty registers (classic rules damage > 4)
  - [ ] `procCurrentPosSave` / `procCurrentPosLoad` — state snapshot for state 16
  - [ ] `procDealOptionToRobot` — deal option cards to robots
  - [ ] `procUpdatePlayerPriority` — round-robin priority rotation
  - [ ] `procVerifyPosition` — validate robot position (no collision, non-zero)
  - [ ] `funcGetNextCard` — draw next card; reshuffle discard if deck empty
  - [ ] `funcGetProgramReadyState` — returns programming readiness state

  **Medium / low priority:**
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
