# AllPlayers Isolation Pattern: Working Copy During Turn Execution

> ## ⚠️ SUPERSEDED — 2026-08-22
>
> This describes a copy-then-commit pattern that was reconsidered. The approach actually
> adopted is in **[ALLPLAYERS_WORKING_COPY_SIMULATION_ONLY.md](ALLPLAYERS_WORKING_COPY_SIMULATION_ONLY.md)**
> — simulate on a discarded copy, record the outcome as commands. Kept for history only.


## Revised Understanding

**Current Problem**:
- CreateCommands mutates AllPlayers directly during ExecuteTurn
- Those mutations are scattered throughout phase execution
- No single point to validate or rollback the turn
- Risk of partial state if turn fails mid-execution

**Desired Pattern**:
```
START ExecuteTurn
  ↓
Make COPY of AllPlayers (working copy)
  ↓
Execute phases on COPY (move robots, apply damage, etc.)
  - NO database writes during execution
  - Changes isolated to working copy only
  ↓
END ExecuteTurn
  ↓
Write final state back to DB in one transaction
  ↓
Update DataService.AllPlayers with final state
```

**Benefits**:
- ✅ Database consistency: Either full turn succeeds or none
- ✅ Isolation: Other code sees AllPlayers unchanged during turn
- ✅ Rollback capability: If turn fails, copy is discarded
- ✅ Single write point: One transaction at end instead of scattered ExecuteSQL calls
- ✅ Easier to debug: Can inspect working copy vs. actual state

---

## Implementation Strategy

### Step 1: Create AllPlayers Copy Constructor
**File**: `MRR/Players.cs`

The `Players` class (which extends `List<Player>`) needs a copy method:

```csharp
/// <summary>
/// Creates a deep copy of all players for turn execution isolation.
/// Changes to the copy do NOT affect the original.
/// </summary>
public Players DeepCopy()
{
    var copy = new Players();
    foreach (var player in this)
    {
        copy.Add(new Player(player));  // Player already has copy constructor
    }
    return copy;
}
```

✅ Player already has copy constructor (seen in Players.cs line ~95)

---

### Step 2: Update CreateCommands Initialization

**File**: `MRR/CreateCommands.cs`

Change ExecuteTurn to work with a copy:

```csharp
public string ExecuteTurn()
{
    if (GameState != 6)
        return ("Wrong State:" + GameState.ToString());

    // Load board and game state
    g_BoardElements = _dataService.BoardLoadFromDB(BoardID);
    _dataService.ReloadAllData();

    // ✅ NEW: Create a working copy of AllPlayers
    Players workingPlayers = _dataService.AllPlayers.DeepCopy();
    
    ListOfCommands.Clear();

    // ... rest of turn execution using workingPlayers instead of AllPlayers
    
    // At end of ExecuteTurn:
    _dataService.AllPlayers = workingPlayers;  // Update persistent state
    _dataService.SavePlayerState(workingPlayers);  // Write to DB
}
```

---

### Step 3: Update All References in CreateCommands

**Pattern**: Replace `AllPlayers` with `workingPlayers`

- **ProcessMove**: Uses `AllPlayers.GetPlayer()` → use `workingPlayers.GetPlayer()`
- **CalcMoveDistance**: Uses `AllPlayers` → use `workingPlayers`
- **ExecutePhase**: Iterates `AllPlayers` → iterate `workingPlayers`
- **Damage/death checks**: Uses `AllPlayers` → use `workingPlayers`

**Remaining reads**: `_dataService.AllPlayers` for game cards priority (that's OK, those are read-only)

---

### Step 4: Create SavePlayerState Method

**File**: `MRR/DataService.cs`

Single transaction that writes all final player state:

```csharp
public void SavePlayerState(Players finalState)
{
    using var ctx = CreateDbContext();
    using var transaction = ctx.Database.BeginTransaction();
    try
    {
        foreach (var player in finalState)
        {
            ctx.Robots.Where(r => r.ID == player.ID)
                .ExecuteUpdate(s => s
                    .SetProperty(r => r.CurrentPosCol, player.CurrentPos.X)
                    .SetProperty(r => r.CurrentPosRow, player.CurrentPos.Y)
                    .SetProperty(r => r.CurrentPosDir, (int)player.CurrentPos.Direction)
                    .SetProperty(r => r.Damage, player.Damage)
                    .SetProperty(r => r.Energy, player.Energy)
                    .SetProperty(r => r.LastFlag, player.LastFlag)
                    .SetProperty(r => r.Status, (int)player.PlayerStatus)
                    .SetProperty(r => r.ShutDown, (int)player.ShutDown)
                    .SetProperty(r => r.Score, player.Score)
                );
        }
        transaction.Commit();
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        Console.WriteLine($"Failed to save player state: {ex.Message}");
        throw;
    }
}
```

---

### Step 5: Implications for AllPlayers Property

**In DataService**:

```csharp
private Players? _allPlayers;

public Players AllPlayers
{
    get
    {
        if (_allPlayers == null)
        {
            _allPlayers = GetAllPlayers();
            CommandItem.AllPlayers = _allPlayers;
        }
        return _allPlayers;
    }
    set
    {
        _allPlayers = value;
        CommandItem.AllPlayers = value;
    }
}
```

**Now makes sense**:
- `set` is used to update persistent state after turn completes
- Callers make a copy, work on it, then set it back
- Rest of code only ever reads AllPlayers (gets fresh/latest state)

---

### Step 6: Read-Only in CreateCommands

**In CreateCommands.cs**:

```csharp
// Property moved from DataService reference:
private Players? _workingPlayers;

public Players WorkingPlayers
{
    get => _workingPlayers ?? throw new InvalidOperationException("ExecuteTurn not started");
    set => _workingPlayers = value;
}

// DO NOT access _dataService.AllPlayers during ExecuteTurn
// Use WorkingPlayers instead
```

**Result**: Compile error if code accidentally references `AllPlayers` during turn

---

## Files to Modify

| File | Change | Scope |
|------|--------|-------|
| **Players.cs** | Add `DeepCopy()` method | ~5 lines |
| **CreateCommands.cs** | Create working copy at start; replace AllPlayers refs | ~10 locations |
| **DataService.cs** | Add `SavePlayerState(players)` method | ~20 lines |
| **GameController.cs** | No changes needed (already writes game state separately) | — |

---

## Execution Flow

```
GameController.NextState() — state 6
    ↓
CreateCommands.ExecuteTurn()
    ↓
workingPlayers = DataService.AllPlayers.DeepCopy()
    ↓
for (RunningPhase = 1 to PhaseCount)
    ExecutePhase(workingPlayers)
        ↓
        for (each square action this phase)
            ProcessDbCommand(action, workingPlayers)
                ↓
                Update workingPlayers only
                ✅ NO database writes here
    ↓
ProcessMove(workingPlayers)
    ↓
CalcMoveDistance(workingPlayers)
    ↓
MoveRobot(workingPlayers) — updates workingPlayer.CurrentPos
    ↓
AddDamage(workingPlayers) — updates workingPlayer.Damage
    ↓
All phase execution complete
    ↓
DataService.AllPlayers = workingPlayers  // Update persistent copy
DataService.SavePlayerState(workingPlayers)  // Single DB transaction
    ↓
Turn complete ✅
```

---

## Key Differences from Original Plan

| Aspect | Original Plan | Revised Pattern |
|--------|---|---|
| **AllPlayers mutations** | Sync immediately to DB | Collect in working copy only |
| **Database writes** | Scattered throughout execution | Single write at end |
| **Isolation** | Other code affected during turn | Other code unaffected |
| **Transaction safety** | Multiple transactions (risky) | One transaction (safe) |
| **Read-only in CreateCommands** | Property protection | Actual data isolation |
| **Rollback capability** | Complex (need to track changes) | Simple (discard working copy) |

---

## Benefits

1. **Database Consistency**: All player state updates happen in one transaction
2. **Isolation**: GameController and other threads see consistent AllPlayers until turn completes
3. **Testability**: Can verify turn logic without touching database
4. **Debugging**: Can inspect working copy after failed turn
5. **Performance**: Fewer total database writes
6. **Safety**: No partial game state if turn crashes mid-execution

---

## Potential Issues & Solutions

| Issue | Solution |
|-------|----------|
| Copy is expensive | Players collection is typically 6 objects; negligible |
| Need to reference original AllPlayers occasionally | Use `_dataService.AllPlayers` explicitly (rare) |
| Other methods expect AllPlayers to change | Call SavePlayerState at turn end before other code runs |
| Card priorities based on old player order | Already loaded before copy; use for sorting |
| Legacy code references AllPlayers | Gradually migrate to WorkingPlayers during ExecuteTurn |

---

## Implementation Order

1. ✅ Add `DeepCopy()` to Players.cs
2. ✅ Add `SavePlayerState()` to DataService.cs  
3. ✅ Update `ExecuteTurn()` to create and use working copy
4. ✅ Replace all `AllPlayers` refs in ExecuteTurn with `workingPlayers`
5. ✅ Remove scattered ExecuteSQL calls from turn execution
6. ✅ Test game flow (start game → program → execute → repeat)
