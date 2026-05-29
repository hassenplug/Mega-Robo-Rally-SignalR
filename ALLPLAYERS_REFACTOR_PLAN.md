# Refactoring Plan: AllPlayers Direct Database Connection

## Goal
Eliminate the `DbSet<Player> Robots` Entity Framework mapping, connect `AllPlayers` directly to the database, and ensure all database writes automatically sync to the in-memory collection. Make `AllPlayers` read-only in `CreateCommands`.

---

## Current State Analysis

### Problems
1. **Lazy loading once**: `AllPlayers` loads on first access and caches indefinitely
2. **Manual sync required**: 60+ `ExecuteSQL()` calls bypass EF Core and don't update `AllPlayers`
3. **Stale in-memory data**: Game controller can see old positions, statuses, and damage values
4. **DbSet unused**: `DbSet<Player> Robots` defined in `MRRDbContext` but code uses raw SQL instead

### Current Flow
```
DataService.AllPlayers (lazy-loaded, cached)
    ↓
GetAllPlayers() + RefreshAllPlayers() (raw SQL queries)
    ↓
ExecuteSQL() writes to DB, but NO sync back to AllPlayers
    ↓
Stale in-memory data ❌
```

---

## Proposed Solution: Three-Phase Approach

### Phase 1: Sync Wrapper Layer (Immediate Fix)
**Goal**: Wrap all ExecuteSQL calls that modify player data with sync logic.

#### 1a. Create Database Write Interceptors in DataService
Wrap high-impact writes with automatic refresh:

```csharp
// Instead of raw ExecuteSQL:
_dataService.ExecuteSQL("Update Robots set Status = 13");

// Use wrapped method:
_dataService.UpdateRobotStatusAll(13);  // syncs AllPlayers automatically
```

**Methods to create** (in `DataService`):
- `UpdateRobotStatus(robotId, newStatus)` → syncs single player
- `UpdateRobotPosition(robotId, x, y, dir)` → syncs position
- `UpdateRobotPositionBatch(updates)` → syncs multiple players
- `UpdateRobotDamage(robotId, damage)` → syncs damage
- `SetRobotEnergy(robotId, energy)` → syncs energy
- `DeleteRobot(robotId)` → removes from AllPlayers collection
- `InsertRobot(playerData)` → adds to AllPlayers collection

**Implementation Pattern**:
```csharp
public void UpdateRobotStatus(int robotId, int newStatus)
{
    // 1. Write to DB
    ExecuteSQL($"UPDATE Robots SET Status = {newStatus} WHERE RobotID = {robotId}");
    
    // 2. Sync to memory
    var player = _allPlayers?.FirstOrDefault(p => p.ID == robotId);
    if (player != null)
    {
        player.PlayerStatus = (tPlayerStatus)newStatus;
    }
}
```

#### 1b. Find & Replace ExecuteSQL Calls
Search for 60+ raw ExecuteSQL calls and replace with wrapper methods:
- **GameController.cs**: 20+ calls
- **CreateCommands.cs**: 5+ calls  
- **DataService.cs**: 30+ calls

**Priority order**:
1. Robots table writes (Status, Position, Damage, Energy)
2. CurrentGameData writes (GameState, Turn, Phase)
3. MoveCards writes (Owner, Location, Phase)
4. CommandList writes (StatusID)

---

### Phase 2: Entity Framework Re-Integration (Medium Term)
**Goal**: Use EF Core's DbSet + change tracking instead of raw SQL.

#### 2a. Restore `DbSet<Player> Robots` Properly
```csharp
public class MRRDbContext : DbContext
{
    public DbSet<CommandItem> CommandItems { get; set; } = null!;
    public DbSet<Player> Robots { get; set; } = null!;  // ← Keep this
    public DbSet<CurrentGameDataEntity> CurrentGameData { get; set; } = null!;
}
```

#### 2b. Create High-Level Operations in DataService
Replace ExecuteSQL patterns with EF Core operations:

```csharp
public void UpdateRobotStatus(int robotId, int newStatus)
{
    using var ctx = CreateDbContext();
    ctx.Robots.Where(r => r.ID == robotId)
        .ExecuteUpdate(s => s.SetProperty(r => r.PlayerStatus, (tPlayerStatus)newStatus));
    
    // Sync to memory
    var player = _allPlayers?.FirstOrDefault(p => p.ID == robotId);
    if (player != null)
        player.PlayerStatus = (tPlayerStatus)newStatus;
}

public void UpdateGameState(int newState)
{
    using var ctx = CreateDbContext();
    ctx.CurrentGameData
        .Where(cgd => cgd.IKey == 10)
        .ExecuteUpdate(s => s.SetProperty(c => c.IValue, newState));
    
    _gameState = newState;
}
```

#### 2c. Batch Operations
For bulk updates (common in turn execution), use transactions:

```csharp
public void BulkUpdateRobotPositions(Dictionary<int, (int x, int y, int dir)> updates)
{
    using var ctx = CreateDbContext();
    using var transaction = ctx.Database.BeginTransaction();
    try
    {
        foreach (var (robotId, (x, y, dir)) in updates)
        {
            ctx.Robots.Where(r => r.ID == robotId)
                .ExecuteUpdate(s => s
                    .SetProperty(r => r.CurrentPosCol, x)
                    .SetProperty(r => r.CurrentPosRow, y)
                    .SetProperty(r => r.CurrentPosDir, dir));
            
            var player = _allPlayers?.FirstOrDefault(p => p.ID == robotId);
            if (player != null)
            {
                player.CurrentPos = new RobotLocation((Direction)dir, x, y);
            }
        }
        transaction.Commit();
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}
```

---

### Phase 3: Live Memory View (Future Optimization)
**Goal**: Eliminate stale data by making AllPlayers a live snapshot.

#### 3a. Query-on-Demand Pattern
```csharp
public Players AllPlayers
{
    get
    {
        // Always fresh from DB (or cached with TTL)
        return GetAllPlayers(forceRefresh: true);
    }
}
```

**Pros**: Zero stale data  
**Cons**: More queries, potential performance impact

#### 3b. Selective Refresh
```csharp
// After ExecuteTurn, refresh just what changed
private void SyncPlayerAfterMove(int robotId)
{
    var row = GetQueryResults($"SELECT ... FROM Robots WHERE RobotID = {robotId}");
    var player = _allPlayers?.FirstOrDefault(p => p.ID == robotId);
    if (player != null && row.Rows.Count > 0)
    {
        // Update player from row
    }
}
```

---

### Phase 4: Make AllPlayers Read-Only in CreateCommands
**Goal**: Prevent accidental direct mutations in command execution.

#### 4a. Change CreateCommands Property
```csharp
// In CreateCommands.cs
public IReadOnlyList<Player> AllPlayers => _dataService.AllPlayers.AsReadOnly();
```

**Result**: Compile error if code tries `AllPlayers[0].Status = X`  
→ Must use `_dataService.UpdateRobotStatus()` wrapper instead

#### 4b. Document the Pattern
```csharp
// BAD (won't compile):
AllPlayers.First().PlayerStatus = tPlayerStatus.Dead;

// GOOD (required):
_dataService.UpdateRobotStatus(playerId, (int)tPlayerStatus.Dead);
```

---

## Implementation Roadmap

### Step 1: Audit & Categorize (Phase 1 Prep)
- [ ] Count all `ExecuteSQL()` calls by affected table
- [ ] Group by business logic (status updates, position moves, damage, etc.)
- [ ] Identify critical sync points (StartGame, NextState, ExecuteTurn)

### Step 2: Create Wrapper Methods (Phase 1)
- [ ] DataService: `UpdateRobotStatus()`
- [ ] DataService: `UpdateRobotPosition()`
- [ ] DataService: `UpdateRobotDamage()`
- [ ] DataService: `SetGameState()`
- [ ] DataService: `SetTurn()`
- [ ] DataService: `DeleteRobotFromGame()`
- [ ] ... (others as needed)

### Step 3: Replace Raw SQL (Phase 1)
- [ ] GameController.StartGame()
- [ ] GameController.NextState()
- [ ] CreateCommands.ExecuteTurn()
- [ ] CreateCommands.AddCommandsToDatabase()
- [ ] DataService.ProcessDbCommand()

### Step 4: Convert to EF Core Batch Operations (Phase 2)
- [ ] Replace ExecuteSQL patterns with ExecuteUpdate/ExecuteDelete
- [ ] Test transaction handling with multiple updates
- [ ] Performance profile before/after

### Step 5: Make AllPlayers Read-Only (Phase 4)
- [ ] Change property to `IReadOnlyList<Player>`
- [ ] Update CreateCommands to use data service methods
- [ ] Run full test suite

### Step 6: Optional - Query-on-Demand (Phase 3)
- [ ] Evaluate performance impact
- [ ] Add refresh TTL caching if needed
- [ ] Measure database load increase

---

## Files to Modify

| File | Scope |
|------|-------|
| **DataService.cs** | Add ~15 wrapper methods; remove raw SQL calls |
| **GameController.cs** | Replace 20+ ExecuteSQL with wrapper calls |
| **CreateCommands.cs** | Replace 5+ ExecuteSQL; make AllPlayers read-only |
| **MRRDbContext.cs** | Keep `DbSet<Player> Robots` (already present) |
| **Players.cs** | No changes (model itself is fine) |

---

## Success Criteria

- ✅ All database writes have corresponding in-memory sync
- ✅ No stale `AllPlayers` data after game state changes
- ✅ CreateCommands cannot accidentally mutate AllPlayers directly
- ✅ Test suite passes (especially game flow tests)
- ✅ Performance: no significant DB query increase
- ✅ All 14 sync issues from DB_SYNC_ISSUES.md resolved

---

## Risk Assessment

| Risk | Mitigation |
|------|-----------|
| Breaking existing code | Phase 1 wrappers are additive; existing code works until replaced |
| Performance regression | Batch methods + profiling; EF Core is optimized for bulk ops |
| Transaction complexity | Use EF Core's built-in transaction support; test edge cases |
| Incomplete coverage | Audit must identify ALL SQL writes first |

---

## Timeline Estimate

- **Phase 1** (Sync Wrappers): 4–6 hours
- **Phase 2** (EF Core): 3–4 hours  
- **Phase 3** (Live View): 2–3 hours (optional)
- **Phase 4** (Read-Only): 1–2 hours
- **Testing & Debugging**: 2–3 hours

**Total: ~14–20 hours**

---

## Notes

- The `DbSet<Player> Robots` mapping already exists; use it
- Don't delete raw SQL methods; keep them private and only use via wrappers
- Use `IReadOnlyList` + wrapper methods to enforce single source of truth
- Consider adding unit tests for each wrapper method (especially batch operations)
