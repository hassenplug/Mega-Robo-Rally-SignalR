# Database-to-Memory Sync Issues Found

> ## 🔶 PARTIALLY MOOT — re-checked 2026-08-27
>
> Items 1, 2, 3, and 5 below are all instances of the `Robots` ↔ `AllPlayers` player-*data*
> mirror going stale. [ALLPLAYERS_REMOVAL_DESIGN.md](ALLPLAYERS_REMOVAL_DESIGN.md) removes
> that mirror instead of syncing it — once implemented, those four items close by
> construction (there is nothing left to drift). Marked individually below; left in place
> since the removal is not yet implemented.
>
> Items 4, 6–14 concern `GameCards`, `ListOfCommands`, `OptionCards`, and `CurrentGameData` —
> unrelated collections, unaffected by that document. All of 4, 6–11, 13 are now fixed or found
> moot (see each item, resolved 2026-09-12/13); 14 was already fixed by an earlier, undated pass
> (`UpdateCardPlayed`'s step 8). 12 was never tracked in `install/todo.md`'s list -- its
> `GameState = 11` already goes through the write-through property; only the winner-robot-ID
> write (iKey 13) is raw SQL, and nothing in `GameStateStore` mirrors that field, so there is
> nothing to desync.
>
> The line numbers below have drifted; the code they describe has not.


## Critical Issues

### 1. **GameController.StartGame()** - Lines 167-174 — *moot once ALLPLAYERS_REMOVAL_DESIGN.md lands*
**Location**: MRR/GameController.cs, lines 167-174
**Issue**: Multiple database deletions without clearing in-memory collections:
- `DELETE FROM MoveCards` - but `_dataService.GameCards` is NOT cleared
- `DELETE FROM CommandList` - but `_dataService.ListOfCommands` is NOT cleared  
- `DELETE FROM RobotOptions` - but `_dataService.OptionCards` is NOT cleared
- `DELETE FROM Robots` - but `_dataService.AllPlayers` is NOT cleared

**Result**: In-memory objects contain stale data from previous game.

---

### 2. **GameController.StartGame()** - Line 205 — *moot once ALLPLAYERS_REMOVAL_DESIGN.md lands*
**Location**: MRR/GameController.cs, line 205
**Issue**: Robot positions updated in DB but in-memory `Player` objects are NOT updated:
```sql
Update Robots set CurrentPosRow=X, CurrentPosCol=Y, CurrentPosDir=Z where RobotID=N
```
**Missing**: Update the in-memory `Player.CurrentPos` / `Player.ArchivePos` objects.

---

### 3. **GameController.StartGame()** - Line 226 — *moot once ALLPLAYERS_REMOVAL_DESIGN.md lands*
**Location**: MRR/GameController.cs, line 226
**Issue**: Robot deleted from database but NOT removed from `AllPlayers` collection:
```sql
delete from Robots where RobotID=N
```
**Result**: Dead robot still exists in memory; can cause crashes in iteration loops.

---

### 4. **GameController.NextState()** - Line 273 — *FIXED 2026-09-13*
**Location**: MRR/GameController.cs, case 2 ("Next Turn")
**Issue**: Turn counter incremented in DB but not in `_dataService.Turn`:
```sql
update CurrentGameData set iValue=iValue+1 where iKey=2
```
**Missing**: After this write, should call `UpdateGameState()` or manually set `_dataService.Turn++`.

**Resolution**: Confirmed real and player-visible: `Turn` is a plain `GameStateStore` field (not
write-through like `GameState`/`TotalFlags`/`IsRunning`/`FieldEnclosed`), and nothing reloaded it
between this write and the broadcast at the end of the same `NextState()` iteration -- every
phone showed the previous turn number in the title (`"Turn " + Turn`) for the entire programming
phase (states 3/4), until `BuildTurnRequest()`'s reload at state 6 finally caught it up. Added
`_dataService.UpdateGameState()` (the `DataService`-level reload, i.e. `GameStateStore.Reload()`
-- not `GameController.UpdateGameState()`, which only broadcasts already-cached data) right after
the raw SQL.

---

### 5. **GameController.NextState()** - Line 295 — *moot once ALLPLAYERS_REMOVAL_DESIGN.md lands*
**Location**: MRR/GameController.cs, line 295
**Issue**: Robot status changed in DB but not in in-memory `Player` objects:
```sql
Update Robots set `Status` = 13
```
**Missing**: Update `Player.PlayerStatus` for all players in `AllPlayers`.

---

### 6. **GameController.NextState()** - Line 378 — *FIXED 2026-09-12*
**Location**: MRR/GameController.cs, `LoadCurrentGame()` (the code moved; the bug describes the
same statement `NextState()` used to call directly)
**Issue**: CommandList status bulk-updated in DB but in-memory `ListOfCommands` NOT updated:
```sql
Update CommandList set StatusID = 2 where StatusID=4 or StatusID=3
```
**Result**: In-memory commands still show old status; can cause re-execution of commands.

**Resolution**: `ListOfCommands` itself is gone (removed 2026-08-30 as dead code); the live
in-memory set is now `PendingCommands._commandList`, rebuilt fresh from the DB each time a turn
starts executing and disposed when it finishes (see `GameController._pendingCommands`). The
residual gap was narrower than the original bug but still real: if `LoadCurrentGame()` runs
while a `PendingCommands` instance is alive (e.g. a GM-triggered game reset mid-turn), the raw
SQL bypassed its EF change tracking, so the live loop kept iterating stale `StatusID` values and
its next `SaveChanges()` could revert the reset. Fixed the same way `ClearPausedCommands` already
handled this bug class: added `PendingCommands.ResetStuckCommands()` (DB `ExecuteUpdate` +
in-memory sync) and routed `LoadCurrentGame()` through it when `_pendingCommands != null`,
falling back to the direct SQL update when no loop is running.

---

### 7. **CreateCommands.ExecuteTurn()** - Line 624 — *moot, already fixed by an earlier refactor*
**Location**: MRR/CreateCommands.cs, line 624
**Issue**: GameState updated in DB but `_dataService.GameState` NOT updated:
```sql
Update CurrentGameData set iValue = 7 where iKey = 10
```
**Note**: This directly writes to DB, bypassing the `GameState` property setter which would sync it.

**Resolution**: `CreateCommands.ExecuteTurn()` (the planner) no longer touches the database at
all -- see its own comment: "The caller stores the commands and applies the state change...
Both are now results, not side effects." It returns `TurnPlan.NextGameState` instead, and
`GameController.ExecuteTurn()` applies it via `_dataService.GameState = plan.NextGameState;`,
which *is* the write-through property. Nothing left to fix here.

---

### 8. **CreateCommands.ExecuteTurn()** - Line 641 — *moot at this location; see #9 for a live analog*
**Location**: MRR/CreateCommands.cs, line 641
**Issue**: CommandList entries deleted in DB but `_dataService.ListOfCommands` NOT cleared:
```sql
Delete from CommandList where Turn=X and Phase>0
```
**Result**: In-memory list still contains old commands from previous turn phases.

**Resolution**: Same Master/planner split as #7 moved this delete out of `CreateCommands`
entirely -- it's now `DataService.PersistCommands()` (`DELETE ... WHERE Turn = {0} AND Phase > 0`
in one transaction with the insert), called once per turn from `GameController.ExecuteTurn()` at
state 6, always before `StartProcessCommandsThread()` creates that turn's `PendingCommands`. Under
normal state-machine timing there is no live `PendingCommands` instance at the moment this runs
(the previous turn's was disposed when its `ProcessCommands()` loop returned, back at state
8→9/10/11→12→2), so there is no stale in-memory list for this delete to leave behind. Unlike #6,
there's no GM action that can reach this delete while a loop is live -- `AbortTurn()` leaves
`GameState` wherever it was, and reaching state 6 again requires a full state 3→4→5 replay, by
which point the aborted loop's background thread has long finished disposing. Not fixed further.

---

### 9. **DataService.GameNewAddCards()** - Line 1782 — *FIXED 2026-09-13*
**Location**: MRR/DataService.cs, line 1782
**Issue**: MoveCards table cleared but `_dataService.GameCards` NOT cleared:
```sql
DELETE FROM MoveCards
```
**Then**: New cards inserted into DB, but in-memory `GameCards` collection is not reloaded.

**Resolution**: Two call sites, one already safe, one genuinely stale. `GameController.StartGame()`
calls `GameNewAddCards()` then `LoadCurrentGame()` a few lines later, which reloads everything --
already fine. `DataService.Players.cs`'s `CurrentPosLoad()` (the "Reload Position" GM action,
state 16) also calls it, then patches `MoveCards`/`RobotOptions` further from `HistoryMoveCards`/
`HistoryRobotOptions` via raw SQL, and used to return with no reload at all -- `GameCards` (and
`OptionCards`) stayed stale until whatever next happened to trigger a full reload. Since
`Player.CardsPlayer`/`CardsPlayed` are computed live off the shared `GameCards` reference (see
#14), this meant a restored turn could show the wrong hand until that next unrelated reload.
Added `ReloadAllData()` at the end of `CurrentPosLoad()`.

---

### 10. **DataService.ProcessDbCommand()** - Line 1117 (Option.Option case) — *FIXED 2026-09-13*
**Location**: MRR/DataService.cs, line 1117
**Issue**: RobotOptions inserted in DB but `_dataService.OptionCards` NOT updated with the new option.

**Resolution**: This runs from the live turn-execution path (`PendingCommands.ProcessCommand()`
calls straight into `DataService.ProcessDbCommand`), so `OptionCards` could sit stale for the rest
of that phase. Bounded in practice -- `OptionCards` is read only once per turn, at the top of the
*next* `BuildTurnRequest()`, and `CommandProcess.ProcessCommands()`'s own per-phase
`ReloadAllData()` would catch it up before then -- but added `LoadOptionCardsFromDatabase()` right
after the INSERT so the option is visible immediately rather than relying on that self-healing.

---

### 11. **DataService.ProcessDbCommand()** - Line 1127 (DealCard case) — *FIXED 2026-09-13*
**Location**: MRR/DataService.cs, line 1127
**Issue**: MoveCard Owner updated but in-memory `GameCards` entry NOT updated:
```sql
UPDATE MoveCards SET Owner = X WHERE CardID = Y
```

**Resolution**: Same live-path/bounded-staleness reasoning as #10, but fixed with a targeted
field update (`GameCards.FirstOrDefault(c => c.ID == cParameter).Owner = cRobotID`) rather than a
full reload, matching the idiom `UpdateCardPlayed()` already uses (see #14) since only one card's
owner actually changed.

---

### 12. **DataService.ProcessDbCommand()** - Line 1141 (GameWinner case) — *not a bug, not tracked*
**Location**: MRR/DataService.cs, line 1141
**Issue**: CurrentGameData written to DB but `_dataService` properties NOT updated:
```sql
UPDATE CurrentGameData SET iValue = X WHERE iKey = 13
```
**Missing**: `UpdateGameState()` call to refresh in-memory state.

**Resolution**: Re-checked 2026-09-13 (this item was never carried into `install/todo.md`'s
list, unlike 4/6-11/13/14). The `GameState = 11` half of this case already goes through the
write-through property. The remaining raw SQL only writes the winning robot's ID to iKey 13, and
no `GameStateStore` field mirrors that value -- there is nothing in memory to go stale. Left as is.

---

### 13. **DataService.ProcessDbCommand()** - Line 1158 (SetCurrentGameData case) — *FIXED 2026-09-13*
**Location**: MRR/DataService.cs, line 1158
**Issue**: CurrentGameData written but corresponding `_dataService` properties NOT updated (e.g., `PhaseCount`, `LaserDamage`).

**Resolution**: The iKey this writes is caller-chosen (`BoardAction.Parameter`), so no single
field can be targeted inline. Added `UpdateGameState()` (the `DataService` reload, refreshing
every `GameStateStore` scalar) right after the write, same fix shape as #4.

---

### 14. **DataService.UpdateCardPlayed()** - Lines 717-748 — *already fixed, undated*
**Location**: MRR/DataService.cs, lines 717-748
**Issue**: Database is updated but in-memory `Player` card lists are NOT updated:
- MoveCards Owner/Location/PhasePlayed are changed in DB
- But `Player.CardsDealt` and `Player.CardsPlayed` lists in memory are stale
- `Player.PlayerStatus` IS updated (line 738) but card collections are not re-synced

**Resolution**: Re-checked 2026-09-13, already fixed by an earlier undated pass. `UpdateCardPlayed`
(now `DataService.Cards.cs`) has an explicit step 8, "Sync in-memory GameCards to match the DB
moves above," that patches the moved cards' `PhasePlayed`/`CardLocation`/`Owner` in place.
Separately, `Player.CardsDealt`/`CardsPlayed` no longer exist as stored fields at all --
`PlayerState.CardsPlayed`/`CardsDealtStr`/`CardsPlayedStr` are computed live from
`CardsPlayer`, itself `AllGameCards.Where(c => c.Owner == ID)` against the shared `GameCards`
reference. There is no separate list left to desync.

---

### 15. **GameController.LoadGameData()** — *FIXED 2026-09-16*
**Location**: MRR/GameController.cs
**Issue**: Copies a `GameData` row (`BoardID`, `OptionCount`/`OptionsOnStartup`, `PhaseCount`,
`GameType`, `LaserDamage`) into `CurrentGameData` via raw SQL, but never refreshed
`GameStateStore`'s cached copies of those same fields.

**Missing**: After this write, should call `UpdateGameState()` (`GameStateStore.Reload()`).

**Resolution**: Confirmed real and player-visible, found while investigating "starting a new game
with a different player count doesn't update the Robots table until Start Game is clicked twice."
`LoadGameData()` is always followed immediately by `StartGame()` (`Program.cs` →
`SetGameState(0)` → `NextState()`'s case 0), and `StartGame()` reads `_dataService.BoardID` to
load the board's `PlayerStart` squares. With the stale cache, the first `StartGame()` call built
the `Robots` table against the *previous* game's board, mismatching the newly-selected player
list; `StartGame()`'s own trailing `LoadCurrentGame() → ReloadAllData() → UpdateGameState()`
refreshed the cache just in time for a second call to work. Added
`_dataService.UpdateGameState()` at the end of `LoadGameData()`, same fix as #4.

---

## Pattern Summary

**Most common issue**: Database writes via `ExecuteSQL()` that directly mutate tables without:
1. Calling `UpdateGameState()` to refresh CurrentGameData-derived fields (`DataService`'s own
   `UpdateGameState()` = `GameStateStore.Reload()`, not `GameController.UpdateGameState()`,
   which only broadcasts whatever is already cached)
2. Reloading the affected entity collections (e.g., `GameCards`, `OptionCards`,
   `PendingCommands._commandList`)
3. Updating individual in-memory entity properties

**Status as of 2026-09-16**: every item above is fixed, found moot by an earlier refactor, or
confirmed not to need a fix (1/2/3/5 by `ALLPLAYERS_REMOVAL_DESIGN.md`; 4/9/10/11/13 fixed
2026-09-13; 6 fixed 2026-09-12; 7/8 moot; 12 never a real bug; 14 already fixed, undated; 15
fixed 2026-09-16). The pattern keeps recurring in new code, though -- #15 was found nearly three
weeks after this list was first written, in a method none of the other 14 touch. Worth grepping
for `_dataService.ExecuteSQL(` / `ExecuteSQL(` call sites that write `CurrentGameData` without a
following `UpdateGameState()` next time this class of bug is suspected, rather than assuming the
list above is exhaustive.

**Affected tables and their in-memory counterparts**:
- `CurrentGameData` ↔ `DataService.GameState`, `.Turn`, `.Phase`, `.BoardID`, etc. (via
  `GameStateStore` -- `GameState`/`TotalFlags`/`FieldEnclosed` write through on set (`IsRunning`
  was a fourth one here until 2026-09-17, when it was folded into `GameState==25`); everything
  else needs an explicit `UpdateGameState()`/`Reload()` after a raw write)
- `Robots` ↔ `DataService.AllPlayers` -- no longer a data mirror; see `ALLPLAYERS_REMOVAL_DESIGN.md`
- `MoveCards` ↔ `DataService.GameCards` collection (shared by reference onto every
  `Player`/`PlayerState.AllGameCards`, so an in-place `Clear()`+repopulate or targeted field edit
  is enough -- no need to re-attach it anywhere)
- `CommandList` ↔ `PendingCommands._commandList` -- rebuilt fresh from the DB whenever a turn
  starts executing, not a long-lived collection; a raw write only needs syncing if it can land
  while that instance is alive (`ClearStuckCommands`/`ResetStuckCommands` are the existing examples)
- `RobotOptions` ↔ `DataService.OptionCards` collection
