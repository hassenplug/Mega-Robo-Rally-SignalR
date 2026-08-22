# Rally Database — SQL-to-C# Conversion List

> ## ⚠️ HISTORICAL SNAPSHOT — the migration is COMPLETE
>
> **Every per-row status below is out of date.** This was generated 2026-03-24, when the
> migration was in progress. As of 2026-08-22 the `rally` schema holds **37 base tables and
> nothing else**: zero stored procedures, zero functions, zero triggers, zero views, and no C#
> code calls a `proc*`/`func*`.
>
> The 41 rows marked "Not Started" are all either ported to C# or deliberately dropped. The
> per-item breakdown of which is which lives in
> **[install/todo.md](../../install/todo.md)** (Section 6) — use that, not this file.
>
> Kept because the *descriptions* are a useful record of what each original SQL object did.


Generated: 2026-03-24

---

## Stored Procedures

| Name | Status | Priority | Description |
|---|---|---|---|
| `procResetPlayers` | Not Started | **High** | Advances shutdown states, respawns dead robots, resets option play records each turn |
| `procMoveCardsShuffleAndDeal` | Not Started | **High** | Shuffles and deals cards to each player at turn start |
| `procUpdateCardPlayed` | Not Started | **High** | Places a card from hand into a register slot (called on every phone card submission) |
| `procMoveCardsCheckProgrammed` | Not Started | **High** | Sets robot Status (1–4) based on how many registers are filled |
| `procMoveCardsCheckOne` | Not Started | **High** | Single-player version of above; also advances game state |
| `procUpdateRobotCards` | Not Started | **High** | Rebuilds `CardsDealt`/`CardsPlayed` strings on Robots row |
| `procCommandUpdateStatus` | Not Started | **High** | Updates `StatusID` on a CommandList row *(missing from SQL file — implied)* |
| `procRobotConnectionStatus` | Not Started | **Medium** | Inserts/resets a connect/disconnect command in CommandList |
| `procGameFillPrograms` | Not Started | **Medium** | Auto-fills empty registers for robots with too few cards |
| `procCurrentPosSave` | Not Started | **Medium** | Snapshots Robots/MoveCards/RobotOptions to History tables at state 5 |
| `procCurrentPosLoad` | Not Started | **Medium** | Restores state from History tables at state 16 |
| `procDealOptionToRobot` | Not Started | **Medium** | Deals next option card from shuffled deck to a robot |
| `procUpdatePlayerPriority` | Not Started | **Medium** | Rotates player turn-order priorities round-robin |
| `procVerifyPosition` | Not Started | **Medium** | Validates robot position (non-zero, no collision) |
| `procCardPlayed` | Not Started | **Medium** | Older card-play entry point using short description letter |
| `procProcessOption` | Not Started | **Medium** | Executes option card effects (Reboot=58, Recompile=39) |
| `procSetStatus` | Not Started | **Medium** | Updates StatusLEDs from robot LED colors (Sense HAT display) |
| `procGameNew` | Not Started | **Low** | Full game init: inserts Robots, flags, LEDs, shuffles options |
| `procResetGame` | Not Started | **Low** | Clears live tables and copies GameData config into CurrentGameData |
| `procGameStart` | Not Started | **Low** | Entry point: sets state=0, calls funcGetNextGameState |
| `procGameNewAddCards` | Not Started | **Low** | Populates MoveCards from template (SetID chosen by rules version) |
| `procKickstart` | Not Started | **Low** | Recovery helper: resets in-progress commands |
| `procSetRobotDirection` | Not Started | **Low** | Admin: sets robot direction and marks position valid |
| `procTestActiveRobots` | Not Started | **Low** | Test/diagnostic: queues connect commands for all robots |
| `procGetReadyCommands` | **Done** | Low | Fully replaced by `CommandProcess.GetActiveCommandList()` |

---

## Functions

| Name | Status | Priority | Description |
|---|---|---|---|
| `funcProcessCommand` | **Partial** | **High** | Command dispatcher — C# handles robot/user categories; MySQL still handles DB-category commands |
| `funcGetNextGameState` | **Partial** | **High** | SQL state machine — still called directly from DataHub for phone-triggered transitions |
| `funcDealSpamToPlayer` | Not Started | **High** | Inserts a new Spam card into a player's discard pile |
| `funcMarkCommandsReady` | **Done** | High | Fully replaced by `PendingCommands.MarkCommandsReady()` |
| `funcGetNextCard` | Not Started | **Medium** | Draws the next card for a player; reshuffles discard if deck empty |
| `funcGetNextOption` | Not Started | **Medium** | Returns next unassigned option card and advances shuffle pointer |
| `funcGetProgramReadyState` | Not Started | **Medium** | Returns programming readiness state (3/4/5) |

---

## Triggers

| Name | Table | Status | Priority | Description |
|---|---|---|---|---|
| `Robots_BEFORE_UPDATE` | Robots | Not Started | **High** | Damage cap → death; ShutDown=4 → clear damage; ShutDown=2 → status=9. Must be replicated in C# wherever Robots is written. |
| `Robots_AFTER_UPDATE` | Robots | Not Started | **Medium** | Calls `procSetStatus()` to sync StatusLEDs after every Robots update |
| `CommandList_BEFORE_INSERT` | CommandList | **Done** | High | Auto-increment CommandID — now handled by MySQL AUTO_INCREMENT |
| `CurrentGameData_BEFORE_UPDATE` | CurrentGameData | Not Started | **Low** | Copies display label strings into `sValue` when state/type/board changes |
| `GameData_BEFORE_UPDATE` | GameData | Not Started | **Low** | Copies board metadata into GameData row when BoardID changes |
| `StatusLEDs_BEFORE_UPDATE` | StatusLEDs | Not Started | **Low** | Converts hex `Color` field to R/G/B integers |

---

## Views (queried from C#)

| Name | Status | Priority | Description |
|---|---|---|---|
| `viewRobots` | **Done** | High | Inlined into `GetAllDataJson()` — view kept in schema (still used by stored procedures) |
| `viewRobotsInit` | **Done** | High | Replaced with inline SQL in `DataService.GetAllPlayers()` — view removed from schema |
| `viewRobotsRefresh` | **Done** | High | Replaced with inline SQL in `DataService.RefreshAllPlayers()` — view removed from schema |
| `viewRobotOptions` | **Done** | High | Robot option cards — used by `LoadOptionCardsFromDatabase()` |
| `viewMoveCards` | Not Started | **Medium** | Human-readable card view — not yet queried from C# |
| `viewCommandList` | Not Started | Low | Admin/debug annotated command view |
| `viewOptions` | Not Started | Low | Filters Options where Functional > 7 (playable cards only) |
| `viewCurrentGame` | Not Started | Low | Simple alias for CurrentGameData |
| `viewBoard` | Not Started | Low | Joins Boards with BoardItems to compute MaxX/MaxY |
| `viewRobotsOld` | Not Started | Low | Legacy viewRobots variant; superseded |
| `viewRobotsMicro` | Not Started | Low | Duplicate of viewRobots kept for backward compatibility |

---

## Critical Path — CLEARED

All six blockers are resolved; the game runs with no MySQL-side logic at all.

1. `Robots_BEFORE_UPDATE` — **dropped, not ported.** Robots do not normally die from taking
   damage, so the damage-cap → death rule is not wanted. `ResetPlayers()` still applies the
   ShutDown transitions the trigger also covered.
2. `funcProcessCommand` → `DataService.ProcessDbCommand()` — handles all categories.
3. `procResetPlayers` → `ResetPlayers()`; `procMoveCardsShuffleAndDeal` → `MoveCardsShuffleAndDeal()`.
4. `procUpdateCardPlayed` → `UpdateCardPlayed()` (rebuilds the CardsDealt/CardsPlayed strings itself).
5. `funcGetNextGameState` → `GameController.NextState()`. `DataHub` is now broadcast-only;
   inbound actions go through the REST API.
6. `procCommandUpdateStatus` / `procRobotConnectionStatus` — dropped; no C# equivalent, no callers.

---

## Summary

As of 2026-08-22 — verified against the live schema and `install/MRRDatabase.sql`:

| Category | Objects remaining in the database |
|---|---|
| Stored Procedures | **0** |
| Functions | **0** |
| Triggers | **0** |
| Views | **0** |
| Base tables | 37 |

Each original object was either ported to C# or deliberately dropped; see
[install/todo.md](../../install/todo.md) Section 6 for the split. The table below is the
2026-03-24 snapshot, retained for its descriptions only.

<details><summary>Original 2026-03-24 counts (obsolete)</summary>

| Category | Total | Done | Partial | Not Started |
|---|---|---|---|---|
| Stored Procedures | 25 | 1 | 0 | 24 |
| Functions | 7 | 1 | 2 | 4 |
| Triggers | 6 | 1 | 0 | 5 |
| Views (active) | 11 | 4 | 0 | 7 |
| **Total** | **49** | **7** | **2** | **40** |

</details>
