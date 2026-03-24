# Rally Database Schema Reference

**Database name:** `rally`
**Server:** `mrobopi3`
**User:** `mrr` / password `rallypass`
**Engine:** MySQL / MariaDB (InnoDB)
**Install files:** `install/SRRDatabase20251001.sql` (authoritative), `userMRR.sql` (user creation)

---

## 1. Core Tables

### Robots
The live player/robot state table. Populated fresh each game from `OperatorData`.

| Column | Type | Default | Notes |
|---|---|---|---|
| RobotID | INT PK | 0 | 1-based player number |
| OperatorName | VARCHAR(20) | | Player name |
| RobotBaseID | INT FK→RobotBases | 0 | Physical hardware |
| RobotBodyID | INT FK→RobotBodies | 0 | Visual skin/name |
| CurrentFlag | INT | 0 | Last flag touched in order |
| Lives | INT | 3 | Remaining lives |
| Damage | INT | 0 | Damage tokens (0-10) |
| ShutDown | INT FK→RobotShutDown | 0 | Shutdown state enum |
| PositionValid | INT | 0 | 1 = position verified |
| Computer | INT | 0 | AI-controlled flag |
| Score | INT | 0 | Game score |
| Status | INT FK→RobotStatus | 0 | Current status enum |
| CurrentPosRow | INT | 0 | Current Y position |
| CurrentPosCol | INT | 0 | Current X position |
| CurrentPosDir | INT | 0 | Direction (0=None, 1=Up, 2=Right, 3=Down, 4=Left) |
| ArchivePosRow | INT | 0 | Respawn Y |
| ArchivePosCol | INT | 0 | Respawn X |
| ArchivePosDir | INT | 0 | Respawn direction |
| IsConnected | INT | 1 | Physical robot connection |
| RobotBatteries | INT | 0 | |
| PhoneBatteries | INT | 0 | |
| Priority | INT | 0 | Turn order (1=first) |
| Password | VARCHAR(10) | | Phone login |
| PlayerSeat | INT | 0 | Physical seat position (1-8) |
| Energy | INT | 3 | Energy tokens |
| CardsDealt | VARCHAR(30) | | CSV of CardTypeIDs in hand (for phone display) |
| CardsPlayed | VARCHAR(20) | | CSV of CardTypeIDs in registers (for phone display) |
| MessageCommandID | INT | | FK→CommandList.CommandID for display message |

**Triggers on Robots:**
- `Robots_BEFORE_UPDATE`: If Damage > 9 → Status=11 (Dead), ShutDown=0. If ShutDown=4 → Damage=0, ShutDown=2. If ShutDown=2 → Status=9 (Shut Down).
- `Robots_AFTER_UPDATE`: Calls `procSetStatus()` to update StatusLEDs.

**Key:** The old schema (rally2old.sql) had `CardsDealt` and `CardsPlayed` missing, and used `MessageID` instead of `MessageCommandID`.

---

### MoveCards
Live deck of cards for the current game. Rebuilt each game by `procGameNewAddCards`.

| Column | Type | Default | Notes |
|---|---|---|---|
| CardID | INT PK | 0 | Card identifier |
| Owner | INT PK | -1 | RobotID owner; -1 = undealt; -2 = temp (recompile) |
| CardTypeID | INT FK→MoveCardTypes | -1 | Type of card |
| PhasePlayed | INT | 0 | Register slot (1-5); -1 = in hand/unplayed; 0 = unset |
| Locked | INT | 0 | 1 = register is locked (cannot change) |
| Random | INT | 0 | Used during shuffle ordering |
| CurrentOrder | INT | 0 | Shuffle sort key |
| Executed | INT | 0 | 1 = already executed this turn |
| CardLocation | INT | 0 | Location enum (see MoveCardLocations) |

**CardLocation values:**
- 0 = Deck
- 1 = Hand
- 2 = Played (in a register)
- 3 = Discard
- 4 = Locked (locked register)
- 5 = Played Spam (spam/haywire played and discarded)

---

### MoveCardTypes (static lookup)

| CardTypeID | Description | ShortDescription | Value | FileName |
|---|---|---|---|---|
| 0 | Unknown | - | 0 | Blank |
| 1 | U-Turn | U | 2 | UTurn |
| 2 | Right Turn | R | 1 | RTurn |
| 3 | Left Turn | L | -1 | LTurn |
| 4 | Backward 1 | B | -1 | Back1 |
| 5 | Forward 1 | 1 | 1 | Forward1 |
| 6 | Forward 2 | 2 | 2 | Forward2 |
| 7 | Forward 3 | 3 | 3 | Forward3 |
| 8 | Again | A | 0 | Again |
| 9 | Power Up | P | 0 | PowerUp |
| 10 | Spam | S | 0 | Spam |
| 11 | Haywire | H | 0 | Haywire |

---

### MoveCardsCompleteList (static deck templates)

Defines which cards belong in each deck variant (SetID).

| SetID | Description | Size | Notes |
|---|---|---|---|
| 1 | Standard 6-8 player | 84 cards | 6xU-Turn, 18xR+L, 6xBack1, 18xFwd1, 12xFwd2, 6xFwd3 |
| 2 | Standard 9+ player | 112 cards | Larger deck, same types |
| 3 | Single-phase mode (one card/turn) | 77 cards | Each of all 7 types repeating evenly |
| 4 | Renegade rules (RulesVersion=1) | 20 cards | 1xU, 4xR, 4xL, 1xBack1, 4xFwd1, 3xFwd2, 1xFwd3, 1xAgain, 1xPowerUp |

SetID is selected in `procGameNewAddCards` based on player count and PhaseCount:
- `> 8 players` → SetID=2
- `PhaseCount=1` → SetID=3
- `RulesVersion=1` → SetID=4

In Renegade mode (SetID=4), each robot gets their own copy of the deck (Owner is set at INSERT time); in classic mode the deck is shared.

---

### CommandList
The execution queue. `CreateCommands.cs` writes rows; `CommandProcess.cs` reads and executes them.

| Column | Type | Notes |
|---|---|---|
| CommandID | INT PK AUTO_INCREMENT | Auto-assigned by trigger (max+1) |
| Turn | INT | Game turn number |
| Phase | INT | Phase within turn (1-5) |
| CommandTypeID | INT | What to do (see CommandLookup) |
| Parameter | INT | Primary parameter |
| ParameterB | INT | Secondary parameter (default 0) |
| RobotID | INT | Target robot |
| CommandSequence | INT | Execution batch number (all same seq execute together) |
| CommandSubSequence | INT | Sub-ordering within a sequence |
| StatusID | INT | Current status (1-7, see CommandStatusLookup) |
| BTCommand | VARCHAR(10) | Legacy Bluetooth command string |
| Description | VARCHAR(50) | Human-readable label |
| PositionRow | INT | Target row for position updates |
| PositionCol | INT | Target column for position updates |
| PositionDir | INT | Target direction for position updates |
| CommandCatID | INT FK→CommandCategories | Category |

**Trigger:** `CommandList_BEFORE_INSERT` assigns CommandID = max(CommandID)+1.

---

### CommandStatusLookup (static)

| StatusID | Description | Color |
|---|---|---|
| 0 | Unknown | ffaaaa |
| 1 | Waiting | ffaaff |
| 2 | Ready (execute now) | 00ff00 |
| 3 | Script Command (Python executing) | aaffaa |
| 4 | In Progress | ffff00 |
| 5 | Script Complete (update position) | ffffaa |
| 6 | Complete | aaaaaa |
| 7 | Connecting | ff0000 |

---

### CommandLookup (static — complete list of CommandTypeIDs)

| ID | Description | Enabled |
|---|---|---|
| 3 | Player Location (move robot in DB) | 1 |
| 12 | Move (physical robot move command) | 1 |
| 13 | Rotate (physical robot turn command) | 1 |
| 14 | Damage (set robot damage) | 1 |
| 15 | Archive (set archive/respawn position) | 1 |
| 16 | Flag (set robot's current flag) | 1 |
| 17 | Deal Option | 1 |
| 20 | Dead | 1 |
| 22 | Set Lives | 1 |
| 24 | Deal Move Card | 1 |
| 30 | Phase Start | 0 |
| 40 | Log data | 1 |
| 41 | Game Winner | 1 |
| 42 | Play Card (mark card executed) | 1 |
| 43 | Play Option Card | 1 |
| 49 | Begin Board Effects | 0 |
| 57 | Start Bot Move | 1 |
| 58 | Stop Bot Move | 1 |
| 60 | Fire Cannon | 0 |
| 63 | Set Player Status | 1 |
| 64 | Damage Points | 1 |
| 65 | Deal Option | 1 |
| 66 | Destroy Option | 1 |
| 67 | Set Option Count | 1 |
| 68 | Set Max Damage | 1 |
| 69 | Set Energy | 1 |
| 70 | BT/WS Connect | 1 |
| 71 | BT/WS Disconnect | 1 |
| 73 | Deal Spam Card | 1 |
| 82 | SetShutdownMode | 1 |
| 83 | Touch Flag | 1 |
| 91 | Set Current Game Data | 1 |
| 92 | Set Button Text | 1 |
| 95 | End Of Game | 1 |
| 96 | Delete Robot | 1 |
| 97 | Set Game State | 1 |
| 98 | Shut Down Game | 1 |

---

### CommandCategories (static)

| CommandCatID | Description | RobotCommand | DBCommand | PiCommand |
|---|---|---|---|---|
| 1 | Robot wReply (waits for ack) | 1 | 0 | 0 |
| 2 | Robot No Reply | 1 | 0 | 0 |
| 3 | DB (database-only update) | 0 | 1 | 0 |
| 4 | PI (Raspberry Pi command) | 0 | 0 | 1 |
| 5 | Node | 0 | 0 | 0 |
| 6 | User Input (pause for user) | 0 | 0 | 0 |
| 7 | Connection | 1 | 0 | 0 |

---

### CurrentGameData
Key-value store for live game state. Dual-keyed: `sKey` (string) and `iKey` (integer).

| sKey | iKey | Default | Notes |
|---|---|---|---|
| GameType | 1 | 0 | Active game type |
| Turn | 2 | 0 | Current turn number |
| Phase | 3 | 0 | Current phase |
| Command | 4 | 0 | (internal) |
| SubCommand | 5 | 0 | (internal) |
| LaserDamage | 6 | 0 | Laser damage amount |
| TotalFlags | 7 | 0 | Flags needed to win |
| RobotsActive | 8 | 0 | Toggle |
| IsRunning | 9 | 0 | Toggle |
| GameState | 10 | 0 | Current state machine state |
| ProgramsReady | 11 | 0 | |
| RobotsReady | 12 | 0 | |
| CommandParameter | 13 | 0 | Secondary state parameter (e.g., winner RobotID) |
| LastUpdateTime | 14 | | |
| PhaseCount | 16 | 5 | Registers per turn |
| MaxDamage | 17 | 10 | Damage to destroy robot |
| BoardCols | 18 | 1 | Board X size |
| BoardRows | 19 | 1 | Board Y size |
| BoardID | 20 | 1 | Active board |
| OptionCount | 22 | -1 | Options per player (-1 = unlimited) |
| Players | 23 | 6 | Player count |
| PlayerListID | 25 | 1 | Active player list |
| GameDataID | 26 | 1 | Active GameData row |
| RulesVersion | 27 | 2 | 0=classic, 1=Renegade, 2=MRR |
| Message | 28 | | Status message string |

**Trigger:** `CurrentGameData_BEFORE_UPDATE` auto-sets `sValue` from lookup tables when GameState, GameType, or BoardID changes.

---

### GameState (static — state machine definition)

| GameStateID | Description | WaitForUser | AutoRefresh | ButtonText |
|---|---|---|---|---|
| 0 | New Game | 1 | 0 | Start Game |
| 2 | Next Turn | 1 | 0 | Next Turn |
| 3 | Verify Positions | 1 | 0 | Verify Positions |
| 4 | Program Robots | 1 | 1 | [wait for programs] |
| 5 | Execute Turn | 1 | 0 | Execute Turn |
| 6 | Executing... | 0 | 1 | [wait for execute] |
| 7 | Run Phase | 1 | 0 | Run Phase:[phase] |
| 8 | Running... | 0 | 1 | [running] |
| 9 | Continue Running | 0 | 0 | Continue |
| 10 | Remove Robot | 1 | 0 | Remove [robotID] |
| 11 | Game Winner | 1 | 0 | Winner [robotID] |
| 12 | End of game | 1 | 0 | End of Game |
| 13 | Exit Game | 1 | 0 | Shut Down Robots |
| 14 | [run exit] | 0 | 0 | [run exit] |
| 15 | Create Programs | 0 | 1 | Generate Programs |
| 16 | Restore Positions | 1 | 0 | Restore |
| 17 | C# Failed | 1 | 0 | System Crashed |
| 21 | Load XML Boards | 1 | 0 | Load XML Boards |
| 22 | Test Board Save | 1 | 0 | Test Load Save |
| 23 | Reset Board | 1 | 0 | Reset Board |
| 24 | Test Run PTO | 0 | 1 | Test PTO |

---

### RobotStatus (static — player status enum)

| StatusID | Description | ShortDescription | Active | Programming | StatusColor | LEDColor |
|---|---|---|---|---|---|---|
| 0 | Unknown | Unknown | 0 | 0 | FFFFFF | FFFFFF |
| 1 | Waiting For Cards | Wait | 1 | 1 | FFFFFF | FFFFFF |
| 2 | Ready to Program | Program | 1 | 1 | CCFFCC | 003333 |
| 3 | Programming | Program | 1 | 1 | AAFFAA | 008888 |
| 4 | Ready to Run | Ready | 1 | 1 | 00FF00 | 00FF00 |
| 5 | Move In Progress | Moving | 1 | 0 | 0000FF | 0000FF |
| 6 | Moving | Moving | 1 | 0 | 0000FF | 0000FF |
| 7 | Connection Failing | Connect | 1 | 0 | FFA500 | FFA500 |
| 8 | Connected | Connect | 1 | 0 | AAAAFF | 000088 |
| 9 | Shut Down | Shut Down | 0 | 0 | FFFF00 | FFFF00 |
| 10 | Not Active | Inactive | 0 | 0 | FF0000 | FF0000 |
| 11 | Dead | Dead | 0 | 0 | FF0000 | FF0000 |
| 12 | Move Complete | Done | 1 | 0 | FF00FF | 880088 |
| 13 | Program Locked | Locked In | 1 | 0 | 55FF55 | 55FF55 |

**`Programming=1`** means the status counts as "active and able to program". The cursor in `procMoveCardsShuffleAndDeal` selects robots WHERE `RobotStatus.Programming = 1`.

---

### RobotShutDown (static — shutdown state machine)

| ShutDownID | Description | NextState | RobotActiveState |
|---|---|---|---|
| 0 | None | 0 | 1 |
| 1 | Next Turn (will shut down) | 4 | 1 |
| 2 | Currently Shut Down | 0 | 9 |
| 3 | Reset | 2 | 1 |
| 4 | Clear & Currently | 2 | 1 |

In `procResetPlayers`, ShutDown is advanced to NextState each turn. ShutDown=4 sets Damage=0 first (circuit breaker).

---

### RobotBodies (static — robot visual identities)

| RobotBodyID | Name | Color (hex) | ColorFG |
|---|---|---|---|
| 1 | Hammerbot | 7338B0 (purple) | FFFFFF |
| 2 | Hulk X90 | FE0000 (red) | FFFFFF |
| 3 | Smashbot | FFE733 (yellow) | 000000 |
| 4 | Spinbot | 0000FF (blue) | FFFFFF |
| 5 | Trundlebot | B76DBB (lavender) | FFFFFF |
| 6 | Twitch | BE9371 (tan) | FFFFFF |
| 7 | Twonky | EB9C1B (orange) | 000000 |
| 8 | Zoombot | 2A611E (dark green) | FFFFFF |

---

### RobotBases (static — physical hardware mapping)

10 entries (IDs 1-10) with MAC addresses for Bluetooth pairing. Each maps to a `DefaultBody`.

| RobotBaseID | MACID |
|---|---|
| 1 | 00:16:53:08:BE:AA |
| 2 | 00:16:53:0A:76:DD |
| 3 | 00:16:53:0A:76:11 |
| 4 | 00:16:53:08:B6:A7 |
| 5 | 00:16:53:0A:7D:86 |
| 6 | 00:16:53:0A:82:8D |
| 7 | 00:16:53:08:BE:77 |
| 8 | 00:16:53:0A:37:26 |
| 9 | 00:16:53:0A:36:D5 |
| 10 | 00:16:53:0A:36:67 |

---

### RobotDirections (static)

| DirID | Description | ShortDirDesc | NextDirection (CW) |
|---|---|---|---|
| 0 | None | - | 1 |
| 1 | Up | ^ | 2 |
| 2 | Right | > | 3 |
| 3 | Down | V | 4 |
| 4 | Left | < | 1 |

---

### OperatorData
Player list templates. Multiple lists (OperatorListID) allow different player configurations.

| Column | Notes |
|---|---|
| OperatorListID | List group (1=default, 2=6-player MRR) |
| RobotID | Robot/player number |
| OperatorName | Display name |
| Paid | Payment flag |
| RobotBodyID | Which skin to use |
| IsActive | 1=in this list |
| Password | Phone login password |
| PlayerSeat | Physical seat (1-8) |
| StartPosition | BoardItemActions Parameter for start square |

**List 1** (10 players, no StartPosition): Generic P1-P10, seats 1-6 (P7-P10 have no seat).
**List 2** (6 players, with StartPosition): P1-P6 with StartPosition 1-6 mapped to `SquareAction=19` board squares.

---

### GameData
Game configuration presets (one row per named game setup).

| Column | Notes |
|---|---|
| GameDataID | 1-10 |
| GameType | References GameTypes |
| TotalFlags | Flags needed to win |
| LaserDamage | Damage per laser hit |
| BoardName | Path to board XML file |
| Description | Human label |
| GameCode | Short code |
| PhaseCount | Registers per turn (usually 5, can be 1) |
| BoardCols | Board width segments |
| BoardRows | Board height segments |
| OptionCount | Options to deal (-1 = none) |
| BoardID | FK→Boards |
| PlayerListID | FK→OperatorData list |
| RulesVersion | 0=classic, 1=Renegade |

**Trigger:** `GameData_BEFORE_UPDATE` — when BoardID changes, auto-copies `LaserDamage`, `GameType`, `PhaseCount`, `TotalFlags`, `X`, `Y` from the `Boards` table.

Active game = GameData row referenced by `CurrentGameData.GameDataID` (iKey=26).

---

### GameTypes (static)

| GameType | Description | LaserDamage | PhaseCount | RuleVersion |
|---|---|---|---|---|
| 0 | Standard | 1 | 5 | 0 |
| 1 | King of the Hill | 0 | 5 | 0 |
| 2 | 10 Turn | 0 | 1 | 0 |
| 3 | Standard 23 | 1 | 5 | 1 |
| 4 | Capture the Flag | - | - | - |
| 5 | Musical Chairs | - | - | - |
| 6 | Standard V2 | - | - | - |

---

### Boards
Board registry. One row per known board.

| Column | Notes |
|---|---|
| BoardID | Primary key |
| BoardName | Path to board XML file |
| X | Width |
| Y | Height |
| GameType | Default game type |
| Players | Player count |
| TotalFlags | Flag count |
| LaserDamage | Default laser damage |
| PhaseCount | Phases per turn |
| RulesVersion | Rules version |

Only BoardID=1 (`../Boards/TST-9x9-2p.srx`) is seeded by default. Other boards are loaded via state 21 ("Load XML Boards").

---

### BoardItems
Physical squares on a board. One row per (BoardID, X, Y).

| Column | Notes |
|---|---|
| BoardID | FK→Boards |
| X | Column (0-based) |
| Y | Row (0-based) |
| SquareType | FK→BoardSquares.ID |
| Rotation | Visual rotation of the tile |

---

### BoardItemActions
Effect definitions for board squares. Multiple rows per square (one per action).

| Column | Notes |
|---|---|
| BoardID | FK→Boards |
| X | Column |
| Y | Row |
| SquareAction | CommandTypeID of the action |
| ActionSequence | Execution order |
| Phase | Which phases trigger this (bitmask or phase number) |
| Parameter | Action-specific parameter |

**SquareAction=19** is "Player Start" — used to locate starting positions.
**SquareAction=100** appears on Flag squares (parameter = flag number).

---

### BoardSquares (static — square type lookup)

| ID | Name | Notes |
|---|---|---|
| 0 | Blank | Normal empty floor |
| 10 | Normal Belt | Single conveyor belt |
| 11 | Normal Turn CW | Belt turning clockwise |
| 12 | Normal Turn CCW | Belt turning counter-clockwise |
| 20 | Fast Belt | Express conveyor belt (moves 2) |
| 21 | Fast Turn CW | |
| 22 | Fast Turn CCW | |
| 31 | Gear CW | Rotates robots CW |
| 32 | Gear CCW | Rotates robots CCW |
| 40 | Pit | Robot destroyed on entry |
| 41 | Trap Door | |
| 42 | Edge | |
| 43 | Corner Edge | |
| 50 | Pusher | Pushes robot on activation phases |
| 55 | Water | |
| 60 | Cannon | Board laser |
| 61 | Randomizer | |
| 70 | Crusher | |
| 80 | Flamer | |
| 90 | Wrench | Repair site (-1 damage at end of turn) |
| 91 | Wrench Hammer | Double repair (-2 damage) |
| 100 | Flag | Checkpoint |
| 105 | King | King of the Hill flag |
| 110 | Start Square | Starting position |
| 200 | Blank Wall | Wall marker |

---

### BoardSegmentList
Maps board segments (XML-based modular boards) to board positions.

| Column | Notes |
|---|---|
| BoardID PK | |
| X | Segment column |
| Y | Segment row |
| BoardSegmentID | Segment identifier |
| Rotation | Segment rotation |

---

### Options (static — upgrade card definitions)

59 option cards defined. Key fields:

| Column | Notes |
|---|---|
| OptionID | Primary key |
| Name | Display name |
| Text | Full rules text |
| SRR_Text | Short implementation note |
| EditorType | UI editor type |
| Quantity | Uses/charges (-1=unlimited, -2=permanent passive) |
| Damage | Damage dealt when used |
| ActionSequence | When it activates |
| CurrentOrder | Shuffled deal order |
| OptType | Category |
| Functional | Implementation status (>7 = implemented) |

**Key implemented options (Functional > 7):**
- 1: Ablative Coat (absorbs 3 damage, then discarded)
- 6: Brakes (Move 1 can stop short)
- 9: Circuit Breaker (auto-shutdown at 3+ damage)
- 12: Crab Legs (Move 1 sideways)
- 13: Double Barrel Laser (2 laser shots)
- 16: Extra Memory (+1 card dealt per turn)
- 18: Flywheel (save one card between turns)
- 19: Fourth Gear (Move 3 becomes Move 4)
- 22: Gyroscopic Stabilizer (ignore belt/gear rotation)
- 23: High Power Laser (shoot through walls/robots)
- 33: Power Down Shield
- 37: Ramming Gear (pushing deals damage)
- 38: Rear Laser (fires backward)
- 39: Recompile (redraw hand, takes 1 damage)
- 41: Reflector (reflects lasers)
- 43: Reverse Gears (Back Up moves 2 squares)
- 48: Shield (directional protection)
- 49: Superior Archive Copy (respawn with 0 damage)
- 52: Turret (rotatable laser direction)
- 53: Explosive Laser
- 55: Point Sucker
- 56: EMP
- 57: Damage Eraser
- 58: Reboot (immediate shutdown + full repair)
- 59: Additional Laser

**Special option IDs referenced in procedures:**
- 9 = Circuit Breaker (auto-shutdown trigger in `procResetPlayers`)
- 16 = Extra Memory (+1 card in `procMoveCardsShuffleAndDeal`)
- 39 = Recompile (hand swap in `procProcessOption`)
- 49 = Superior Archive (respawn with 0 damage in `procResetPlayers`)
- 58 = Reboot (shutdown+repair in `procProcessOption`)

---

### RobotOptions
Options currently held by robots during a game.

| Column | Notes |
|---|---|
| RobotID PK | |
| OptionID PK | |
| DestroyWhenDamaged | |
| Quantity | Remaining uses |
| IsActive | Currently active |
| PhasePlayed | Which phase it was played |
| DataValue | Direction or other per-option value |

---

### StatusLEDs
Drives the physical LED display (Sense HAT or similar).

| Column | Notes |
|---|---|
| LEDID | Maps to RobotID |
| R, G, B | RGB values (0-255) |
| Sort | Display order |
| Brightness | 0-100 |
| Color | Hex color string (source of truth) |

**Trigger:** `StatusLEDs_BEFORE_UPDATE` — converts `Color` hex string to R/G/B integers automatically.

`procSetStatus()` updates StatusLEDs from `viewRobots.LEDColor`. Override rules:
- PositionValid=0 → Red (FF0000)
- CommandTypeID=70 (connecting) with StatusID=7 → Orange (FF8800)

---

### History Tables
Save snapshots of game state per turn for replay/restore.

- **HistoryRobots** (GameID, Turn, RobotID PK): Full robot state snapshot
- **HistoryMoveCards** (GameID, Turn, CardID, Owner PK): Card assignments
- **HistoryRobotOptions** (GameID, Turn, RobotID, OptionID PK): Option assignments

Saved by `procCurrentPosSave()` at state 5 (Execute Turn), loaded by `procCurrentPosLoad()` at state 16.

---

### PhaseCounter
Simple lookup table with IDs 1-5, used in cursor joins to iterate phase slots.

---

### MoveCardLocations (static)

| LocationID | Description | DealPriority |
|---|---|---|
| 0 | Deck | 3 |
| 1 | Hand | 2 |
| 2 | Played | 5 |
| 3 | Discard | 4 |
| 4 | Locked | 1 |
| 5 | Played Spam | 5 |

DealPriority is used during shuffle (Locked cards stay in place, Deck drawn first).

---

### SeatOrientation
Maps physical seat numbers to board viewing directions.

| SeatID | Direction |
|---|---|
| 1, 2, 3 | 1 (Up) |
| 4, 5 | 2 (Right) |
| 6, 7, 8 | 3 (Down) |

---

### RobotCommands (static — physical command parameter lookup)

| CommandType | Value | Description |
|---|---|---|
| 1 (Move) | 0 | Move Back |
| 1 | 1 | Move 0 |
| 1 | 2 | Move 1 |
| 1 | 3 | Move 2 |
| 1 | 4 | Move 3 |
| 2 (Turn) | 0 | Turn Left |
| 2 | 1 | Turn 0 |
| 2 | 2 | Turn Right |
| 2 | 3 | U-Turn |
| 3 (LED/PTO) | 0 | PTO Off |
| 3 | 1 | PTO On |
| 3 | 2 | LED Laser |
| 3 | 4 | Damaged |
| 3 | 5 | Flag |
| 3 | 6 | Active Option |
| 3 | 7 | Game Winner |
| 3 | 8 | Dead |
| 3 | 9 | Set Energy |
| 4 (Shutdown) | 0 | Set Shut Down |

---

### BluetoothDongles (static)

Two Bluetooth dongles: `00:0C:78:33:50:8E` and `00:0C:78:33:DE:E6`.

---

### Other Small Tables

- **GameCommandList**: Script of commands to run at game events (start, each turn, end)
- **GameCommandTiming**: Timing categories (connection, start of game, each turn, each phase, end of game)
- **RobotMessages**: Message strings (0=null, 1=Validate Position, 2=Remove Robot, 3=Next Phase, 4=Robot Direction)

---

## 2. Views

### viewRobots (primary query for all clients)
Joins: Robots + RobotBodies + RobotStatus + RobotDirections + SeatOrientation + MoveCards(played) + CommandList(message)

Key computed columns:
- `X` = CurrentPosCol, `Y` = CurrentPosRow
- `AX` = ArchivePosCol, `AY` = ArchivePosRow
- `sDir` = ShortDirDesc (^, >, V, <)
- `FlagEnergy` = "CurrentFlag/Energy" string
- `PlayerViewDirection` = seat-based direction adjustment
- `StatusToShow` = cards played string (e.g., "1R2L3") or status text if inactive
- `msg` = CommandList.Description for MessageCommandID
- Ordered by Priority

### viewRobotsMicro
Same as viewRobots but without the old `dealt` sub-query; uses Robots.CardsDealt/CardsPlayed directly.

### viewRobotsOld
Legacy version with separate CardsDealt subquery.

### viewMoveCards
MoveCards + MoveCardTypes + MoveCardLocations. Shows CardID, type description, owner, phase played, executed, locked, location name.

### viewCommandList
CommandList + CommandCategories + CommandStatusLookup. Full annotated command view.

### viewCommandListActive
`SELECT * FROM CommandList WHERE StatusID >= 2 AND StatusID <= 4` — commands currently being processed.

### viewCurrentGame
Simple pass-through: `SELECT sKey, iValue, sValue, Category FROM CurrentGameData`

### viewBoard
`SELECT BoardID, BoardName, MAX(X) AS MaxX, MAX(Y) AS MaxY FROM Boards JOIN BoardItems GROUP BY BoardID`

### viewOptions
`SELECT OptionID, Name, SRR_Text, EditorType, Quantity, Damage FROM Options WHERE Functional > 7`

### viewRobotOptions
RobotOptions joined to viewOptions ordered by Name.

---

## 3. Stored Procedures

### procGameStart(p_GameDataID INT)
Entry point to start a new game. Sets GameState=0 and GameDataID, then calls `funcGetNextGameState()`.

### funcGetNextGameState() RETURNS INT
The database-side state machine. Runs a REPEAT loop advancing the state until it stabilizes at a wait state. See state table above. Key state transitions:
- 0 → calls `procGameNew()` → 2
- 2 → calls `procResetPlayers()` + `procMoveCardsShuffleAndDeal()` → 3
- 3 → checks PositionValid; if all valid → 4
- 4 → checks all robots Status=4; if all ready → 5
- 5 → `procCurrentPosSave()` → 6
- 16 → `procCurrentPosLoad()` → 3

### procGameNew()
Initializes a new game:
1. Calls `procResetGame()`
2. Inserts Robots from OperatorData, setting start positions from BoardItemActions (SquareAction=19)
3. Counts flags on board, sets TotalFlags
4. Initializes StatusLEDs
5. Shuffles Options deck

### procResetGame()
Clears live data (MoveCards, CommandList, RobotOptions, StatusLEDs, Robots) and copies settings from GameData into CurrentGameData.

### procMoveCardsShuffleAndDeal()
The main card dealing procedure. Called at state 2 (start of each turn).

**Renegade rules (RulesVersion=1):**
1. Discard played Spam cards (CardLocation=2 with CardTypeID=10)
2. Move all hand/played cards to discard (CardLocation=3)
3. Shuffle using `RAND()` + `DealPriority` weighting (locked cards stay)
4. If any robot has < 9 cards, move their discards back to deck
5. Deal 9 cards (CardLocation=1) to each robot
6. Update Robots.CardsDealt and CardsPlayed strings

**Classic rules (RulesVersion=0):**
- Uses cursor over active robots
- Deals (9 - Damage) cards per robot
- Locked cards handled per damage level
- Calls `procMoveCardsCheckProgrammed()`

**Note:** If Option 16 (Extra Memory) is held, `lOptionCards` adds 1 to card count.

### procGameNewAddCards()
Creates the MoveCards table for a new game. Selects the appropriate SetID and inserts from MoveCardsCompleteList.

### procMoveCardsCheckProgrammed()
Checks each active robot's programming status and updates Robot.Status:
- < 5 cards in hand → Status=1 (Waiting for Cards)
- Programmed count = PhaseCount → Status=4 (Ready to Run)
- Some programmed > locked → Status=3 (Programming)
- else → Status=2 (Ready to Program)

### procMoveCardsCheckOne(p_Player INT)
Same as above but for a single player. Also calls `procGameNextState()` if game is in state 3 or 4.

### procGameFillPrograms()
Auto-programs any robot that has unfilled registers (for shutdown scenarios). Uses PhaseCounter cross-join to find empty slots, fills them from the robot's hand.

### procResetPlayers()
Called at start of each turn (state 2):
1. Advances ShutDown state machine
2. Applies Circuit Breaker option (OptionID=9)
3. Sets Status=2 for non-shutdown robots
4. Marks robots with Damage > 9 as Dead (Status=11)
5. Applies Superior Archive (OptionID=49) for dead robots with lives > 0
6. Respawns dead robots at ArchivePos with `useDamage` (LaserDamage*2) damage
7. Resets RobotOptions.PhasePlayed

### procVerifyPosition(p_Robot INT)
Checks if a robot's position is valid:
- Direction must not be 0 (None)
- Row and Col must not be 0
- No other robot at same position
Sets Robots.PositionValid accordingly.

### procCurrentPosSave()
Saves current turn state to HistoryRobots, HistoryMoveCards, HistoryRobotOptions.

### procCurrentPosLoad()
Restores state from history (state 16). Calls `procResetGame()` then reloads from History tables.

### procUpdateCardPlayed(p_Player INT, p_CardTypeID INT, p_PhasePlayed INT)
Phone client card programming endpoint:
- Validates robot is in Programming status
- If p_PhasePlayed=-1, finds first empty phase slot
- If p_CardTypeID > 0, finds the card by type
- Moves card from Hand (CardLocation=1) to Played (CardLocation=2)
- Removes any card already in that slot back to hand
- Updates Robot.Status (3=programming, 4=ready)
- Calls `procUpdateRobotCards()` to refresh CardsDealt/CardsPlayed strings

### procCardPlayed(p_Card VARCHAR(1), p_Player INT)
Alternative card programming endpoint using ShortDescription letter (e.g., 'R', 'L', '1').

### procUpdateRobotCards(p_Player INT)
Rebuilds Robots.CardsDealt and Robots.CardsPlayed CSV strings for the phone UI.

### funcProcessCommand(p_CommandID INT, p_NewStatus INT) RETURNS INT
Main command execution handler. Called from C# when a command completes. Processes side effects based on CommandTypeID:
- 3: Player Location (update Robots.CurrentPos from command's Position fields)
- 14: Set Damage
- 15: Set Archive position
- 16: Set Current Flag
- 22: Set Lives
- 24: Deal card to player
- 41: Game Winner (update CurrentGameData GameState=11)
- 42: Mark card as executed
- 63: Set robot Status
- 66: Destroy option
- 67: Set option quantity
- 68: Set max damage
- 73: Deal spam card (calls `funcDealSpamToPlayer`)
- 82: Set ShutDown
- 91: Set CurrentGameData iValue by iKey
- 95: End of game (GameState=12)
- 96: Delete robot
- 97: Set GameState
- Status=5: Updates robot position then marks Complete (6)

### funcGetNextCard(p_player INT, p_usedSpam INT) RETURNS INT
Draws one card for a player from their deck. If deck empty, shuffles discard back. Marks the used spam card as CardLocation=5.

### funcDealSpamToPlayer(p_RobotID INT) RETURNS INT
Adds a new Spam card (CardTypeID=10) to a robot's discard pile. Returns new CardID. Called by funcProcessCommand type 73.

### funcMarkCommandsReady() RETURNS INT
Advances the command queue: finds the minimum CommandSequence with StatusID=1 (Waiting) and sets it to 2 (Ready). Returns count of active commands.

### procGetReadyCommands()
Procedure version of above; also checks GameState=8. Returns `viewCommandListActive`.

### procDealOptionToRobot(p_RobotID INT)
Deals the next available option card from the shuffled Options deck to a robot. Updates Options.CurrentOrder to advance the shuffle pointer.

### funcGetNextOption(p_RobotID INT) RETURNS INT
Returns the next available OptionID for a robot (not already owned, Functional>7).

### procProcessOption(p_OptionID INT, p_RobotID INT)
Handles immediate option card effects:
- 58 (Reboot): ShutDown=2, Damage=0
- 39 (Recompile): Swaps robot's hand cards with undealt cards, deals 1 damage

### procSetStatus()
Updates StatusLEDs from viewRobots.LEDColor. Overrides with Red for invalid positions, Orange for connecting status.

### procRobotConnectionStatus(p_Robot INT, p_connection INT)
Ensures a connect (70) or disconnect (71) command exists in CommandList for the robot and sets it to Ready (StatusID=2).

### procTestActiveRobots()
Calls `procRobotConnectionStatus` for all robots (used for testing connections). Includes a `SLEEP(2)`.

### procUpdatePlayerPriority()
Rotates robot priorities: each Priority -= 1, then the robot that hits 0 gets Priority = count. Used in single-phase (10-Turn) mode.

### procKickstart()
Sets GameState=8 and calls `procCommandUpdateStatus(-1, 0)`.

### procSetRobotDirection(p_RobotID INT, p_Direction INT)
Updates CurrentPosDir and sets PositionValid=1.

### funcGetProgramReadyState() RETURNS INT
Returns 3 (verify), 4 (wait), or 5 (programmed) based on robot states.

---

## 4. Triggers Summary

| Table | Trigger | When | Effect |
|---|---|---|---|
| Robots | Robots_BEFORE_UPDATE | Before update | Damage>9 → Status=11; ShutDown=4 → Damage=0,ShutDown=2; ShutDown=2 → Status=9 |
| Robots | Robots_AFTER_UPDATE | After update | Calls procSetStatus() |
| CurrentGameData | CurrentGameData_BEFORE_UPDATE | Before update | Sets sValue from GameState/GameType/Board lookups |
| CommandList | CommandList_BEFORE_INSERT | Before insert | Assigns CommandID = max+1 |
| GameData | GameData_BEFORE_UPDATE | Before update | When BoardID changes, copies board settings to GameData columns |
| StatusLEDs | StatusLEDs_BEFORE_UPDATE | Before update | Converts Color hex string to R/G/B integers |

---

## 5. Schema Evolution Across Versions

### rally2old.sql (MariaDB dump — earliest version, from running Pi)
- `Robots` table: Missing `CardsDealt`, `CardsPlayed`, `MessageCommandID` columns
- `Robots` data shows 8 players active (Player1-Player8) with real board positions
- No `GameCommandList`, `GameCommandTiming`, `RobotMessages` tables found
- No stored procedures (dump only includes table data)

### SRRDatabase20240101.sql (January 2024)
- `Robots` has `MessageID INT` (replaced by `MessageCommandID` in 2025 version)
- `MoveCards` already has `CardLocation` column (same as current)
- `CommandList` already has `CommandCatID`
- `MoveCardLocations` table exists
- `BoardSegmentList` table exists
- Same stored procedures as current but `procCommandUpdateStatus` called internally (not defined in this file — likely existed as a separate procedure)
- `RulesVersion` not yet in `GameData` or `Boards`

### SRRDatabase202404.sql (April 2024)
- `Robots` still has `MessageID INT` (not yet `MessageCommandID`)
- `RulesVersion` added to `GameData` and `Boards` tables
- `procMoveCardsShuffleAndDeal` has `lRulesVersion` branching (RulesVersion=1 path)
- `procResetGame` now copies RulesVersion from GameData
- Essentially the same as the Jan 2024 version with RulesVersion additions

### SRRDatabase20251001.sql (October 2025 — CURRENT/AUTHORITATIVE)
- `Robots.MessageID` renamed to `Robots.MessageCommandID` (now FK→CommandList.CommandID)
- `viewRobots` updated: joins CommandList on `Robots.MessageCommandID = cl.CommandID` to show `cl.Description as msg`
- `viewRobotsMicro` added (same as viewRobots but alternate structure)
- `funcDealSpamToPlayer` and `procGetReadyCommands` procedures added
- `SetID=4` (Renegade deck) added to `MoveCardsCompleteList`
- `CurrentGameData.RulesVersion` default changed to 2 (MRR rules)
- `GameState` rows 21-24 added (Load XML Boards, Test Board Save, Reset Board, Test Run PTO)
- `CommandLookup` entries 73 (Deal Spam Card), 91-92 added
- Option cards 53-59 added (custom SRR options)

---

## 6. Database User

From `userMRR.sql`:
```sql
CREATE USER 'mrr' IDENTIFIED BY 'rallypass';
GRANT ALL ON `rally`.* TO 'mrr';
```

Connection string pattern: `Server=mrobopi3;Database=rally;User=mrr;Password=rallypass;`

---

## 7. Key Query Patterns Used by C#

**Get all player state:** `SELECT * FROM viewRobots`
or `SELECT * FROM viewRobotsMicro`

**Get current game state:** `SELECT * FROM viewCurrentGame`

**Get active commands to execute:** `CALL procGetReadyCommands()`
or `SELECT * FROM viewCommandListActive`

**Advance game state:** `SELECT funcGetNextGameState()`

**Update a command's status:** `SELECT funcProcessCommand(@commandID, @newStatus)`

**Program a card:** `CALL procUpdateCardPlayed(@robotID, @cardTypeID, @phase)`

**Check if all programmed:** `CALL procMoveCardsCheckProgrammed()`

**Start a game:** `CALL procGameStart(@gameDataID)`
