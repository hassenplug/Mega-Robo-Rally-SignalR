# Mega Robo Rally (MRR) — Project Context

## What This Is
A computerized Robo Rally (Renegade edition) game engine built in C# / ASP.NET Core 9.
Physical playing pieces are 6 VEX AIM robots controlled via WebSocket.
A Raspberry Pi 5 with Sense HAT runs the server and displays minimal game status on its 8×8 LED matrix.
Six phones connect to the Pi via SignalR and show each player's hand / programming UI.

## Hardware
| Device | Role |
|---|---|
| Raspberry Pi 5 + Sense HAT | Game server, 8×8 LED display, joystick input |
| 6 × VEX AIM robots | Physical playing pieces on the board |
| 6 × phones (browser) | Player UI — show hand cards, accept programming input |

## Project Layout (MRR/)
```
Program.cs           REST API endpoints + startup
GameController.cs    State machine (states 0–16), orchestrates turns
DataService.cs       MySQL data layer (server: mrobopi3, db: rally)
DataHub.cs           SignalR hub for real-time phone/client updates
CreateCommands.cs    Converts move cards → PendingCommand rows
CommandProcess.cs    Background thread: executes PendingCommands in order
Players.cs           Player / Robot data model + AIM robot WebSocket methods
CardList.cs          MoveCard types + deck management
OptionCards.cs       Upgrade card definitions
BoardElement.cs      Board square / tile model
RobotLocations.cs    Position + direction tracking
RotationFunctions.cs Direction math helpers
PhaseFunctions.cs    Per-phase helpers
RobotScreenUI.cs     AIM robot touchscreen programming UI
GridAlignmentAgent.cs Camera-based grid alignment for navigation
Data/
  MRRDbContext.cs    Entity Framework DbContext
Sensors/             (empty — future Sense HAT integration)
Services/            (empty — future service classes)
wwwroot/             Static web assets for phone UI
```

## Key Architecture Patterns
- **State machine** in `GameController.NextState()` (states 0–16) — do not bypass it
- **Command pipeline**: CreateCommands writes rows → CommandProcess reads and executes them sequentially
- **Robot communication**: dual WebSocket per robot (ws_cmd + ws_status) via `AIMRobot`
- **Real-time**: SignalR `DataHub` broadcasts to all phones after every state change
- **Database**: MySQL with stored procedures (`procResetPlayers`, `procMoveCardsShuffleAndDeal`, etc.)
- **Thread safety**: `Interlocked` flags guard `NextState()` and `ExecuteTurn()`

## Game State Reference
| State | Meaning |
|---|---|
| 0 | StartGame (init) |
| 2 | Reset / shuffle / deal cards |
| 3 | Verify positions |
| 4 | Wait for player programming |
| 5 | Lock programs |
| 6 | ExecuteTurn (build command list) |
| 7 | Run phase — wait |
| 8 | Run phase — in progress |
| 9–11 | Sub-states of run phase |
| 12 | Next turn → back to state 2 |
| 13–14 | Exit / reset → state 0 |
| 15 | Recreate program → state 4 |
| 16 | Reload positions → state 3 |

## Working Principles

- Don't assume. Don't hide confusion. Surface tradeoffs.
- Minimum code that solves the problem. Nothing speculative.
- Touch only what you must. Clean up only your own mess.
- Define success criteria. Loop until verified.

## Coding Conventions
- All new code in C#, .NET 9, nullable-enabled
- New files go in `MRR/` or an appropriate subdirectory (`MRR/Services/`, `MRR/Sensors/`, etc.)
- Agents and agent-related files go in `.claude/agents/`
- Follow existing patterns (partial classes, singleton services, async/await throughout)
- No breaking changes to existing REST API contracts without discussion
- Server hostname: `mrobopi3` — used in connection strings and launch URLs

## Project TODO
See [install/todo.md](install/todo.md) for the active task list.

## Active Agents

| Agent | File | When to use |
|---|---|---|
| `robo-rally-dev` | [.claude/agents/robo-rally-dev.md](.claude/agents/robo-rally-dev.md) | Game logic, robot movement, board simulation, player UI, hardware integration. Contains the full Robo Rally Renegade rule set and implementation guidance. |
| `sql-to-csharp` | [.claude/agents/sql-to-csharp.md](.claude/agents/sql-to-csharp.md) | Converting MySQL stored procedures, triggers, and functions into C# methods in `DataService.cs`. Contains the full rally DB schema, all procedure logic, and trigger behavior. |
| `aim-robot-api` | [.claude/agents/aim-robot-api.md](.claude/agents/aim-robot-api.md) | Any VEX AIM robot command from C#. Documents every WebSocket command (drive, turn, LCD, LED, sound, vision, IMU, kicker), the JSON wire format, and `AIMRobot.cs` patterns. |
| `aim-screen-ui` | [.claude/agents/aim-screen-ui.md](.claude/agents/aim-screen-ui.md) | Robot touchscreen programming UI (`RobotScreenUI.cs`). Knows the 240×240 circular LCD layout, touch polling, the 9-card ring + 5 horizontal slot design, and GameController integration (states 4–5). |
| `gm-ui` | [.claude/agents/gm-ui.md](.claude/agents/gm-ui.md) | GM control panel (`gmindex.html`). Knows the full REST API surface, exact AllDataUpdate payload shape, state-by-state button logic, robot status panels, game message bar (titlemsg + CurrentGameData.Message), game selection, direction setter, Use Robots toggle (simulation vs. physical), pre-game player setup (robot body / base / seat assignment), and wwwroot/ conventions. |
| `aim-navigation` | [.claude/agents/aim-navigation.md](.claude/agents/aim-navigation.md) | Improving physical robot navigation accuracy. Knows the full IMU sensor pipeline (heading, gyro_rate, odometry via robot_x/robot_y), extending RobotStatus, IMU-guided turn correction (turn_to), odometry-based move verification (set_pose), and integrating camera grid alignment (GridAlignmentAgent) for post-move correction. |
| `mrr-database` | [.claude/agents/mrr-database.md](.claude/agents/mrr-database.md) | Maintains `install/MRRDatabase.sql` as the single source of truth for the entire rally DB schema. Knows all 37 tables, 12 views, 7 functions, 24 stored procedures, 6 triggers, and seed data. Use when adding/modifying DB schema, writing new queries, or updating the install script. |
| `move-to-memory` | [.claude/agents/move-to-memory.md](.claude/agents/move-to-memory.md) | Refactors the data layer so live game data (Players, MoveCards, CurrentGameData) lives in memory with write-through property setters. Manages the CreateCommands write-suppression window, targeted AllPlayers reload at state 6→7, and in-turn AllPlayers sync inside ProcessDbCommand. |
