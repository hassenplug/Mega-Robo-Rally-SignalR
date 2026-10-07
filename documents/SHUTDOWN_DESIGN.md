# Shutdown (power-down) — Design

**Status: implemented 2026-10-06; build and `MRR.Tests/ShutDownTests.cs` pass; confirmed working by the project owner 2026-10-07.** Decided 2026-10-05. The sections below are the original plan and are kept as written. Covers the two open items in
[install/todo.md](../install/todo.md): Section 1 "Shutdown mechanic" and Section 3 "Shutdown
toggle on phone UI".

## Rules as decided for this project

These are the project owner's decisions for this version, not a transcription of the rulebook.
Verify against the Renegade rulebook before treating them as canonical.

| Question | Decision |
|---|---|
| When can a player shut down? | Only during programming (`GameState == 4`), via a toggle on the phone. The toggle can be switched on and off until the program is locked. |
| Is there a "next turn" delay? | **No.** There is no `NextTurn` state. Shutdown takes effect for the turn in which it is chosen. |
| What happens to the robot's cards? | At lock-in: every damage card (Spam **and** Haywire) in its registers, hand and discard pile goes to the **damage discard pile** (`CardLocation` 5); every regular programming card in its registers and hand goes to its **discard pile** (`CardLocation` 3). Implemented by `DataService.ApplyShutDownAtLockIn()`; the next deal empties the damage discard pile. |
| Does the robot move? | It plays no registers (no cards, so nothing is executed), but it **can be pushed**, and conveyors and other board moves still apply. |
| Does it take laser damage? | **Yes**, as Spam cards from the damage stack, like any robot. |
| Does it fire its own laser? | **No.** |
| When does shutdown end? | At the start of the next turn (`ResetPlayers()`). No state advancement is needed. |

## Simplified state model

`Robots.ShutDown` uses only two values of the existing `tShutDown` enum
([PlayerState.cs](../MRR.Contracts/PlayerState.cs)):

- `None (0)` — normal
- `Currently (2)` — shut down this turn

`NextTurn`, `WithoutReset` and `ClearDamage` are not used by this mechanic. They remain in the
enum and in the `RobotShutDown` seed table (see [install/MRRDatabase.sql](../install/MRRDatabase.sql));
the `NextState` column of that table is not used.

`Robots.Status` becomes `tPlayerStatus.ShutDown (9)` at lock-in.

## What already exists

- `tPlayerStatus.ShutDown = 9` and `tShutDown` enum.
- `Robots.ShutDown` column and `RobotShutDown` lookup table.
- `PlayerState.IsShutDown` and `PlayerState.IsRunning` (= not Dead, not ShutDown, no pending
  `RespawnID`) in [PlayerState.cs](../MRR.Contracts/PlayerState.cs).
- `AllDataPayload.ShutDown` is already sent to the phones.
- `SquareAction.SetShutDownMode` (82), handled in
  [DataService.Commands.cs](../MRR/DataService.Commands.cs) (`UPDATE Robots SET ShutDown = ...`).
- The **EMP** option card is the only current writer of `ShutDown`
  ([CreateCommands.cs](../MRR.Rules/CreateCommands.cs), ~lines 975-1004).

## What is missing

- No player input to toggle shutdown.
- Nothing discards cards for a shut-down robot.
- `ResetPlayers()` ([DataService.Players.cs](../MRR/DataService.Players.cs)) no longer advances
  or clears `ShutDown` (the old `procResetPlayers` logic was removed). Its blanket
  `UPDATE Robots SET Status = ReadyToProgram` clears the `ShutDown` *status* but not the column.
- `IsRunning` conflates "doesn't act" with "isn't on the board" (see Execution below).

## Implementation plan

### 1. Toggle endpoint
Add a case to `/api/player/{command}/{playerId}/...` in [Program.cs](../MRR/Program.cs).
- Valid only when `GameState == 4` and the robot is alive; otherwise rejected.
- Flips `Robots.ShutDown` between 0 and 2.
- Logic lives in a `DataService` method; the REST handler stays thin. Follow how the existing
  cases pass the player ID and data.
- Do not change existing REST contracts; this is an additive command number.

### 2. Lock-in (state 5, `GameController.cs`)
For each robot with `ShutDown == Currently`:
- set `PlayerStatus = ShutDown (9)`;
- move its damage cards (Spam, Haywire; registers/hand/discard) to the damage discard pile and
  its programming cards (registers/hand) to the discard pile — see the rules table above.

A robot that chose shutdown must still complete its program (and direction) like any other:
state 4 waits on it the same way, and its cards are only discarded here at lock-in.

Do this at lock-in, not at toggle time, so toggling off loses nothing.
**To verify before writing:** how Spam and programmed cards are stored (`MoveCards.Owner`,
`PhasePlayed`, `Locked`) and how the damage stack is represented.

### 3. Execution filters ([CreateCommands.cs](../MRR.Rules/CreateCommands.cs))
`IsRunning` is used at ~8 call sites (lines ~946, 1044, 1096, 1115, 1191, 1259, 1591-1592) plus
`GetPlayerAtSquare` in [PlayerStates.cs](../MRR.Contracts/PlayerStates.cs) and
[Players.cs](../MRR/Players.cs). Today it excludes shut-down robots from everything. Required:

| Behaviour | Shut-down robot |
|---|---|
| Plays registers | No (it has no cards) |
| Fires its own laser | **No** |
| Is hit by lasers (draws Spam) | **Yes** |
| Is pushed / blocks squares / is found by `GetPlayerAtSquare` | **Yes** |
| Board moves (conveyors, pits, etc.) | **Yes** |

Approach: add an `IsOnBoard` property (not Dead, no pending `RespawnID`) and review each
`IsRunning` call site, choosing `IsRunning` (acts) or `IsOnBoard` (is physically present).
Do not loosen `IsRunning` itself.

### 4. Clear at next turn
In `ResetPlayers()` add `UPDATE Robots SET ShutDown = 0`. The existing reset to `ReadyToProgram`
already clears the status, and the robot is dealt a normal hand at state 2.

### 5. Phone UI
[index.html](../MRR/wwwroot/index.html) and [loadrobots.js](../MRR/wwwroot/js/loadrobots.js):
- Toggle shown only in state 4 and hidden once the program is locked.
- State driven by `ShutDown` in the existing payload.
- "Shut down" indicator during the run phases.
- Optional: show shutdown in the GM robot status panel; `RobotEffects.cs` already has a
  `SetShutDownMode` case that could set a robot LED.

### 6. Tests (`MRR.Tests`, modelled on `PitRebootTests.cs`)
- Toggle works in state 4 and is rejected in other states.
- Lock-in moves damage cards to the damage discard pile, programming cards to the discard
  pile, and sets status 9. (Needs the DB — not covered by the unit tests; manual check.)
- Shut-down robot: takes laser damage as Spam, does not fire, can be pushed.
- `ResetPlayers()` clears the shutdown.
- Manual check on the table with simulated robots (see todo.md Section 8 for how other
  mechanics were verified).

### 7. Docs
Tick the two todo.md items and add a line to PROJECT_STATUS.md. (robo-rally-dev.md §1.13 is already corrected.)

## Open items

- **EMP interaction.** EMP sets `ShutDown = Currently` on all other robots and `WithoutReset` on
  its owner. The new next-turn clear will wipe both. Default: leave EMP's code alone and note the
  interaction; revisit if EMP needs "shut down without repair" semantics.
- Whether a shut-down robot's physical AIM robot needs any visible indication (LED).
- Spam discard storage details (see step 2).
