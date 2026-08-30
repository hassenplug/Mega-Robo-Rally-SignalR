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
DataService.cs       MySQL data layer (db: rally; conn string from appsettings.json)
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
  CurrentGameData.cs CurrentGameDataEntity (keyed on iKey)
wwwroot/             Static web assets for phone UI
```

## Design Documents
| Document | Covers |
|---|---|
| [API_DECOMPOSITION_DESIGN.md](documents/API_DECOMPOSITION_DESIGN.md) | Splitting the app into seven API contracts across two processes; migration order; open defects |
| [install/PROCESS_MANAGER.md](install/PROCESS_MANAGER.md) | systemd supervision, `mrrctl`, deploy/rollback. Implementation in [install/service/](install/service/) |
| [DB_SYNC_ISSUES.md](documents/DB_SYNC_ISSUES.md) | Open: DB deletes that don't clear the matching in-memory collections |
| ~~[ALLPLAYERS_REFACTOR_PLAN.md](documents/ALLPLAYERS_REFACTOR_PLAN.md)~~ | **Superseded** by ALLPLAYERS_REMOVAL_DESIGN.md — decided 2026-08-27 to remove the in-memory mirror rather than keep it synced. Kept for its inventory of the sync problems |
| [ALLPLAYERS_REMOVAL_DESIGN.md](documents/ALLPLAYERS_REMOVAL_DESIGN.md) | **Implemented** 2026-08-27 — all of §9's rollout steps done and build-verified. The §11 `Damage`/`Lives` question was resolved as correct behavior, not a bug (see [install/todo.md](install/todo.md) Section 1). §10's manual verification checklist is still open — see todo.md Section 8 |

## Key Architecture Patterns
- **State machine** in `GameController.NextState()` (states 0–16) — do not bypass it
- **Command pipeline**: CreateCommands writes rows → CommandProcess reads and executes them sequentially
- **Robot communication**: dual WebSocket per robot (ws_cmd + ws_status) via `AIMRobot`
- **Real-time**: SignalR `DataHub` broadcasts to all phones after every state change
- **Database**: MySQL (`rally`), **tables only** — 37 base tables, and zero stored
  procedures, functions, triggers, or views. All that logic now lives in C#, mostly in
  `DataService` (e.g. `ResetPlayers()`, `MoveCardsShuffleAndDeal()`, `ProcessDbCommand()`).
  Do not add database-side logic, and do not call `proc*`/`func*` — they do not exist.
- **Renegade rules only.** `RulesVersion` was removed 2026-08-22; there is no Classic path.
- **One `TotalFlags` per game**, in `CurrentGameData` (iKey 7), taken from the board at game
  start. It is not a per-player value.
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
- Server hostname: `mrobopi`. Never hardcode it — the DB connection string and the
  HTTP listen URL both come from [MRR/appsettings.json](MRR/appsettings.json)
  (`ConnectionStrings:Rally` and `Urls`), so the app can run on a host separate from the DB.

## Project Status & Operations

[PROJECT_STATUS.md](PROJECT_STATUS.md) — how to rebuild the SD card, run each part, run a
game, plus known issues and what remains. Start there for anything operational.

## Project TODO
See [install/todo.md](install/todo.md) for the active task list.

## Active Agents

| Agent | File | When to use |
|---|---|---|
| `robo-rally-dev` | [.claude/agents/robo-rally-dev.md](.claude/agents/robo-rally-dev.md) | Game logic, robot movement, board simulation, player UI, hardware integration. Contains the full Robo Rally Renegade rule set and implementation guidance. |
| ~~`sql-to-csharp`~~ | [.claude/agents/sql-to-csharp.md](.claude/agents/sql-to-csharp.md) | **Retired — migration complete.** The DB has no procedures, functions, or triggers left to convert. Kept only as a record of what the original SQL did. |
| `aim-robot-api` | [.claude/agents/aim-robot-api.md](.claude/agents/aim-robot-api.md) | Any VEX AIM robot command from C#. Documents every WebSocket command (drive, turn, LCD, LED, sound, vision, IMU, kicker), the JSON wire format, and `AIMRobot.cs` patterns. |
| `aim-screen-ui` | [.claude/agents/aim-screen-ui.md](.claude/agents/aim-screen-ui.md) | Robot touchscreen programming UI (`RobotScreenUI.cs`). Knows the 240×240 circular LCD layout, touch polling, the 9-card ring + 5 horizontal slot design, and GameController integration (states 4–5). |
| `gm-ui` | [.claude/agents/gm-ui.md](.claude/agents/gm-ui.md) | GM control panel (`gmindex.html`). Knows the full REST API surface, exact AllDataUpdate payload shape, state-by-state button logic, robot status panels, game message bar (titlemsg + CurrentGameData.Message), game selection, direction setter, Use Robots toggle (simulation vs. physical), pre-game player setup (robot body / base / seat assignment), and wwwroot/ conventions. |
| `aim-navigation` | [.claude/agents/aim-navigation.md](.claude/agents/aim-navigation.md) | Improving physical robot navigation accuracy. Knows the full IMU sensor pipeline (heading, gyro_rate, odometry via robot_x/robot_y), extending RobotStatus, IMU-guided turn correction (turn_to), odometry-based move verification (set_pose), and integrating camera grid alignment (GridAlignmentAgent) for post-move correction. |
| `mrr-database` | [.claude/agents/mrr-database.md](.claude/agents/mrr-database.md) | Maintains `install/MRRDatabase.sql` as the single source of truth for the rally schema — 37 tables and seed data. Use when adding/modifying DB schema, writing new queries, or updating the install script. Its procedure/function/trigger/view reference sections are **historical only**; none of those objects exist in the database. |
| ~~`move-to-memory`~~ | [.claude/agents/move-to-memory.md](.claude/agents/move-to-memory.md) | **Retired — direction reversed.** The project moved to reading `Robots` fresh from the DB per broadcast instead of caching it in memory; see [ALLPLAYERS_REMOVAL_DESIGN.md](documents/ALLPLAYERS_REMOVAL_DESIGN.md). Kept only as a historical record. |
