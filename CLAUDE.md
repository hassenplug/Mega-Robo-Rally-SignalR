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
AIMRobot.cs          WebSocket client for each VEX AIM robot
Players.cs           Player / Robot data model
CardList.cs          MoveCard types + deck management
OptionCards.cs       Upgrade card definitions
BoardElement.cs      Board square / tile model
RobotLocations.cs    Position + direction tracking
RotationFunctions.cs Direction math helpers
PhaseFunctions.cs    Per-phase helpers
Data/                Entity Framework models + DbContext
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

## Coding Conventions
- All new code in C#, .NET 9, nullable-enabled
- New files go in `MRR/` or an appropriate subdirectory (`MRR/Services/`, `MRR/Sensors/`, etc.)
- Agents and agent-related files go in `.claude/agents/`
- Follow existing patterns (partial classes, singleton services, async/await throughout)
- No breaking changes to existing REST API contracts without discussion
- Server hostname: `mrobopi3` — used in connection strings and launch URLs

## Active Agent
Use the **`robo-rally-dev`** sub-agent (`.claude/agents/robo-rally-dev.md`) for all game development tasks. It contains the full Robo Rally Renegade rule set, VEX AIM robot command reference, and implementation guidance.
