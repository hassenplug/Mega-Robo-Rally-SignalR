# Database-to-Memory Sync Issues Found

## Critical Issues

### 1. **GameController.StartGame()** - Lines 167-174
**Location**: MRR/GameController.cs, lines 167-174
**Issue**: Multiple database deletions without clearing in-memory collections:
- `DELETE FROM MoveCards` - but `_dataService.GameCards` is NOT cleared
- `DELETE FROM CommandList` - but `_dataService.ListOfCommands` is NOT cleared  
- `DELETE FROM RobotOptions` - but `_dataService.OptionCards` is NOT cleared
- `DELETE FROM Robots` - but `_dataService.AllPlayers` is NOT cleared

**Result**: In-memory objects contain stale data from previous game.

---

### 2. **GameController.StartGame()** - Line 205
**Location**: MRR/GameController.cs, line 205
**Issue**: Robot positions updated in DB but in-memory `Player` objects are NOT updated:
```sql
Update Robots set CurrentPosRow=X, CurrentPosCol=Y, CurrentPosDir=Z where RobotID=N
```
**Missing**: Update the in-memory `Player.CurrentPos` / `Player.ArchivePos` objects.

---

### 3. **GameController.StartGame()** - Line 226
**Location**: MRR/GameController.cs, line 226
**Issue**: Robot deleted from database but NOT removed from `AllPlayers` collection:
```sql
delete from Robots where RobotID=N
```
**Result**: Dead robot still exists in memory; can cause crashes in iteration loops.

---

### 4. **GameController.NextState()** - Line 273
**Location**: MRR/GameController.cs, line 273
**Issue**: Turn counter incremented in DB but not in `_dataService.Turn`:
```sql
update CurrentGameData set iValue=iValue+1 where iKey=2
```
**Missing**: After this write, should call `UpdateGameState()` or manually set `_dataService.Turn++`.

---

### 5. **GameController.NextState()** - Line 295
**Location**: MRR/GameController.cs, line 295
**Issue**: Robot status changed in DB but not in in-memory `Player` objects:
```sql
Update Robots set `Status` = 13
```
**Missing**: Update `Player.PlayerStatus` for all players in `AllPlayers`.

---

### 6. **GameController.NextState()** - Line 378
**Location**: MRR/GameController.cs, line 378
**Issue**: CommandList status bulk-updated in DB but in-memory `ListOfCommands` NOT updated:
```sql
Update CommandList set StatusID = 2 where StatusID=4 or StatusID=3
```
**Result**: In-memory commands still show old status; can cause re-execution of commands.

---

### 7. **CreateCommands.ExecuteTurn()** - Line 624
**Location**: MRR/CreateCommands.cs, line 624
**Issue**: GameState updated in DB but `_dataService.GameState` NOT updated:
```sql
Update CurrentGameData set iValue = 7 where iKey = 10
```
**Note**: This directly writes to DB, bypassing the `GameState` property setter which would sync it.

---

### 8. **CreateCommands.ExecuteTurn()** - Line 641
**Location**: MRR/CreateCommands.cs, line 641
**Issue**: CommandList entries deleted in DB but `_dataService.ListOfCommands` NOT cleared:
```sql
Delete from CommandList where Turn=X and Phase>0
```
**Result**: In-memory list still contains old commands from previous turn phases.

---

### 9. **DataService.GameNewAddCards()** - Line 1782
**Location**: MRR/DataService.cs, line 1782
**Issue**: MoveCards table cleared but `_dataService.GameCards` NOT cleared:
```sql
DELETE FROM MoveCards
```
**Then**: New cards inserted into DB, but in-memory `GameCards` collection is not reloaded.

---

### 10. **DataService.ProcessDbCommand()** - Line 1117 (Option.Option case)
**Location**: MRR/DataService.cs, line 1117
**Issue**: RobotOptions inserted in DB but `_dataService.OptionCards` NOT updated with the new option.

---

### 11. **DataService.ProcessDbCommand()** - Line 1127 (DealCard case)
**Location**: MRR/DataService.cs, line 1127
**Issue**: MoveCard Owner updated but in-memory `GameCards` entry NOT updated:
```sql
UPDATE MoveCards SET Owner = X WHERE CardID = Y
```

---

### 12. **DataService.ProcessDbCommand()** - Line 1141 (GameWinner case)
**Location**: MRR/DataService.cs, line 1141
**Issue**: CurrentGameData written to DB but `_dataService` properties NOT updated:
```sql
UPDATE CurrentGameData SET iValue = X WHERE iKey = 13
```
**Missing**: `UpdateGameState()` call to refresh in-memory state.

---

### 13. **DataService.ProcessDbCommand()** - Line 1158 (SetCurrentGameData case)
**Location**: MRR/DataService.cs, line 1158
**Issue**: CurrentGameData written but corresponding `_dataService` properties NOT updated (e.g., `PhaseCount`, `LaserDamage`).

---

### 14. **DataService.UpdateCardPlayed()** - Lines 717-748
**Location**: MRR/DataService.cs, lines 717-748
**Issue**: Database is updated but in-memory `Player` card lists are NOT updated:
- MoveCards Owner/Location/PhasePlayed are changed in DB
- But `Player.CardsDealt` and `Player.CardsPlayed` lists in memory are stale
- `Player.PlayerStatus` IS updated (line 738) but card collections are not re-synced

---

## Pattern Summary

**Most common issue**: Database writes via `ExecuteSQL()` that directly mutate tables without:
1. Calling `UpdateGameState()` to refresh CurrentGameData-derived fields
2. Reloading the affected entity collections (e.g., `AllPlayers`, `GameCards`, `ListOfCommands`)
3. Updating individual in-memory entity properties

**Affected tables and their in-memory counterparts**:
- `CurrentGameData` ↔ `DataService.GameState`, `.Turn`, `.Phase`, `.BoardID`, etc.
- `Robots` ↔ `DataService.AllPlayers` collection
- `MoveCards` ↔ `DataService.GameCards` collection
- `CommandList` ↔ `DataService.ListOfCommands` collection
- `RobotOptions` ↔ `DataService.OptionCards` collection & `Player.Options`
