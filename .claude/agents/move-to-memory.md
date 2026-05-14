---
name: move-to-memory
description: >
  Refactors the MRR data layer so all live game data lives in memory
  (loaded from DB at startup, written back via property setters) instead
  of being re-queried from MySQL on every operation. Manages the
  CreateCommands write-suppression window, the targeted AllPlayers reload
  between states 6 and 7, and in-turn AllPlayers sync after each
  ProcessDbCommand call.
model: sonnet
tools:
  - Read
  - Write
  - Edit
  - Bash
  - Glob
  - Grep
  - Agent
---

# Move-to-Memory Refactor Agent

## Goal

Shift the source of truth for all live game data from the MySQL database to
in-memory objects inside `DataService`.  The database becomes a persistence
backing store rather than the primary query target.

---

## Design Decisions (confirmed by product owner Q&A)

### 1. Data loaded into memory at startup
| Data | DB source | In-memory holder |
|---|---|---|
| Player/robot state | `Robots` + joined tables | `DataService._allPlayers` (already exists, needs write-through) |
| Move card deck / hands | `MoveCards` | New `DataService._allCards` collection |
| Game metadata | `CurrentGameData` | New `DataService._gameData` object |
| Board layout | `BoardItems` / `BoardItemActions` | Load on demand at start of `CreateCommands`, discard after |
| Option cards | `Options` / `RobotOptions` | **Out of scope — leave as direct DB queries** |

### 2. Write-through via property setters
Every meaningful property on `Player` that maps to a DB column should auto-save
when assigned.  Pattern:

```csharp
// In Player.cs
private DataService? _ds;  // injected when player is added to collection

public int Energy
{
    get => _energy;
    set
    {
        _energy = value;
        if (!(_ds?.SuppressDbWrites ?? true))
            _ds!.ExecuteSQL($"UPDATE Robots SET Energy={_energy} WHERE RobotID={ID}");
    }
}
```

The same pattern applies to all state-carrying properties:
`Energy`, `ShutDown`, `Priority`, `PlayerStatus`, `Score`, `LastFlag`,
`MessageCommandID`, `CurrentPos` (col/row/dir), `ArchivePos`, `Damage`,
`CardsDealtStr`, `CardsPlayedStr`.

### 3. Write-suppression flag
`DataService` exposes a `bool SuppressDbWrites` property (default `false`).
During `CreateCommands` execution (the command-list generation phase) this is
set to `true` so that none of the intermediate player-state changes write
through to the DB.

```csharp
// DataService.cs
public bool SuppressDbWrites { get; set; } = false;
```

`CreateCommands` must:
1. Set `_ds.SuppressDbWrites = true` at entry.
2. Do all its work (load board, generate command list).
3. Save **only** the `CommandList` table to DB.
4. Set `_ds.SuppressDbWrites = false` when done.

### 4. Targeted AllPlayers reload (between state 6 → 7)
After `CreateCommands` finishes (and before `CommandProcess` starts), reload only
the fields that could change during a turn:
- `CurrentPosCol`, `CurrentPosRow`, `CurrentPosDir`
- `ArchivePosCol`, `ArchivePosRow`, `ArchivePosDir`
- `Energy`, `Damage`, `Status`, `ShutDown`, `Priority`, `Score`, `LastFlag`
- `CardsDealtStr`, `CardsPlayedStr`
- `MessageCommandID`

Do **not** reload static fields (name, color, IP address, seat, etc.) since
those cannot change mid-game.  This reload must be done with
`SuppressDbWrites = true` so that the reload itself does not trigger write-
through fires back to the DB.

Add method to `DataService`:
```csharp
public void ReloadPlayerRuntimeFields()
// Reads the DB, updates each Player in _allPlayers in-place, does NOT fire setters
```

The reload is triggered from `GameController.NextState()` in the transition
from state 6 to state 7.

### 5. AllPlayers in-memory sync during CommandProcess
`ProcessDbCommand()` in `DataService` currently writes to DB (Robots, MoveCards,
etc.) but does **not** update the in-memory `_allPlayers`.

After each DB write inside `ProcessDbCommand`, the corresponding `Player`
object in `_allPlayers` must be updated **directly** (bypassing setters, to
avoid a double-write) using a private helper or field-assignment approach:

```csharp
// After writing position to DB in ProcessDbCommand:
var player = _allPlayers.GetByID(robotID);
if (player != null)
{
    player.SetFieldsDirect(col: newCol, row: newRow, dir: newDir);
    // SetFieldsDirect assigns backing fields directly, bypassing setters
}
```

---

## Files to Touch

| File | Changes |
|---|---|
| `MRR/Players.cs` | Add `_ds` field + injection; add `SetFieldsDirect()`; convert state properties to write-through setters |
| `MRR/DataService.cs` | Add `SuppressDbWrites` flag; add `_allCards` collection; add `_gameData` singleton; update `RefreshAllPlayers` / `GetAllPlayers` to inject `_ds` into each player; add `ReloadPlayerRuntimeFields()`; update `ProcessDbCommand` to call `SetFieldsDirect` after each DB write |
| `MRR/GameController.cs` | Call `ReloadPlayerRuntimeFields()` in state 6→7 transition; remove any redundant `GetAllPlayers(true)` force-refresh calls that are now covered by the reload |
| `MRR/CreateCommands.cs` | Wrap entry/exit with `SuppressDbWrites = true/false`; remove direct `ExecuteSQL` calls that wrote player state during command generation (those writes are now suppressed); keep only the final `CommandList` save |

---

## Implementation Plan

### Phase 1 — Add infrastructure (no behavior change yet)

1. **`DataService.SuppressDbWrites`** — add the flag property.
2. **`Player._ds` injection** — add the field and a new `InjectDataService(DataService ds)` method; call it inside `GetAllPlayers()` when building the player list.
3. **`Player.SetFieldsDirect()`** — add a method that assigns all runtime backing fields without triggering setters.  Signature:
   ```csharp
   internal void SetFieldsDirect(
       int? col = null, int? row = null, string? dir = null,
       int? archiveCol = null, int? archiveRow = null, string? archiveDir = null,
       int? energy = null, int? damage = null, int? status = null,
       bool? shutDown = null, int? priority = null, int? score = null,
       int? lastFlag = null, int? msgCmdID = null,
       string? cardsDealt = null, string? cardsPlayed = null);
   ```

### Phase 2 — Convert Player properties to write-through setters

Convert each state-carrying property one at a time.  Verify the game still
compiles and runs after each batch:

Batch A: `Energy`, `ShutDown`, `Priority`, `Score`
Batch B: `PlayerStatus`, `LastFlag`, `MessageCommandID`, `Damage`
Batch C: `CurrentPos` (wrap the RobotLocation setter or expose col/row/dir
         individually), `ArchivePos`
Batch D: ~~`CardsDealtStr`, `CardsPlayedStr`~~ — **already done differently**: both
are now computed read-only properties on `Player` derived from in-memory `GameCards`
(`AllGameCards.Where(c => c.Owner == ID)`). No write-through setter is needed.
`StatusToShow` is also now a computed property (no setter).

Write-through SQL for each property must match exactly what the current
`ExecuteSQL` calls in `GameController` and `CreateCommands` write (same column
names, same WHERE clause).

### Phase 3 — Add MoveCards in-memory collection — **PARTIALLY DONE**

The `MoveCard` class already has all DB columns as properties: `ID`, `Type`,
`Owner`, `PhasePlayed`, `CardLocation`, `Executed`, `Locked`, `CurrentOrder`.

`DataService.GameCards` (`CardList`, a `List<MoveCard>`) is the in-memory collection.
It is populated by `DataService.LoadGameCardsFromDatabase()` which selects all
`MoveCards` columns including `CardLocation` and `Executed`.

`CardsPlayer` on each `Player` is a computed property:
`[.. (AllGameCards ?? []).Where(c => c.Owner == ID)]`
where `AllGameCards` is a reference to `DataService.GameCards`.

**Still needed:**
- Ensure `AllGameCards` is wired onto each `Player` when `GetAllPlayers()` builds
  the player list (so `CardsPlayer` resolves against the shared collection).
- Replace remaining direct `GetQueryResults("SELECT ... FROM MoveCards")` calls in
  hot paths with in-memory `GameCards` lookups.
- `UpdateCardPlayed` already syncs `GameCards` in-memory after each DB write.
- `RefreshPlayerCards(robotID)` reloads that player's card fields from DB into
  `GameCards` (PhasePlayed, CardLocation, Executed).

### Phase 4 — Add CurrentGameData in-memory object

1. Define `GameDataRecord` (or reuse `CurrentGameData` entity) for the
   `CurrentGameData` table.
2. Add `GameDataRecord _gameData` to `DataService`; load from DB at startup.
3. The existing `GameState` property setter already writes to DB — wire it
   through the new object with `SuppressDbWrites` awareness.
4. Replace all `GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE iKey=...")` 
   calls with in-memory lookups.

### Phase 5 — CreateCommands write-suppression

1. At the top of the `CreateCommands` entry point, set
   `_ds.SuppressDbWrites = true`.
2. At the bottom (before returning), save the `CommandList` to DB via the
   existing EF / `ExecuteSQL` path (this write is intentional and must bypass
   suppression — use `ExecuteSQL` directly, not a setter).
3. Reset `_ds.SuppressDbWrites = false`.
4. Remove any now-redundant `ExecuteSQL` calls inside `CreateCommands` that
   were writing player state — those side-effects are now intentionally dropped.

### Phase 6 — Targeted reload between states 6 and 7

1. Implement `DataService.ReloadPlayerRuntimeFields()`:
   ```
   SuppressDbWrites = true
   SELECT the runtime columns for all robots
   foreach player in _allPlayers: call SetFieldsDirect with new values
   SuppressDbWrites = false
   ```
2. In `GameController.NextState()`, insert the call at the state 6→7 boundary
   (after `CreateCommands` completes and before the state advances to 7).

### Phase 7 — AllPlayers sync inside ProcessDbCommand

For each `SquareAction` case in `ProcessDbCommand` that writes to `Robots`,
add a `SetFieldsDirect` call on the matching player immediately after the DB
write.  Cases to cover:

- Any case that updates `CurrentPosCol/Row/Dir`
- Any case that updates `ArchivePosCol/Row/Dir`
- Any case that updates `Energy`
- Any case that updates `Damage` (or draws a spam card)
- Any case that updates `Status` or `ShutDown`
- Any case that updates `LastFlag` / `Score`
- Any case that updates `CardsDealt` / `CardsPlayed` (spam card swaps)

Do **not** remove the existing DB writes — they still happen.  This step
adds in-memory sync alongside each existing write.

---

## Verification Checklist

After all phases, confirm:

- [ ] Server starts and all players load from DB without errors.
- [ ] `GetAllDataJson()` returns correct data using only the in-memory copy.
- [ ] A full turn (state 2 → 12) completes without DB errors.
- [ ] AllPlayers state after turn execution matches DB state (run a spot-check
      query against `Robots` table vs. in-memory player values).
- [ ] `CreateCommands` generates the same `CommandList` rows as before.
- [ ] No player state is written to DB during `CreateCommands`
      (can verify by temporarily logging all `ExecuteSQL` calls with a filter).
- [ ] `ReloadPlayerRuntimeFields()` is called exactly once per turn, at state 6→7.
- [ ] Force-refresh calls that are now redundant have been removed.

---

## Out of Scope

- Option cards (`Options` / `RobotOptions`) — leave as direct DB queries.
- Board layout persistence — load on demand, discard after `CreateCommands`.
- API endpoints in `Program.cs` that do ad-hoc `GetQueryResults` / `GetQueryResultsJson`
  calls for admin/GM views — these can remain as direct DB queries.
- `BoardSaveToDB` — no change needed; board editing is an admin path.
- Any `DataHub.cs` direct `ExecuteSQL` calls that operate on non-player tables.

---

## Risks & Gotchas

1. **Thread safety**: `_allPlayers`, `_allCards`, and `_gameData` are shared
   singletons.  `SuppressDbWrites` must be a non-static instance field to
   avoid race conditions if multiple SignalR calls overlap.  If `CreateCommands`
   is always single-threaded (it currently is, guarded by `Interlocked`), a
   simple bool is sufficient.

2. **`DataHub` SignalR calls**: Several `DataHub` methods call `ExecuteSQL`
   directly for player card updates.  After Phase 2, some of these writes will
   be redundant (setter already wrote to DB).  Audit `DataHub.cs` for double-
   writes after Phase 2 completes.

3. **EF DbContext vs. direct SQL**: `DataService.CreateDbContext()` returns an
   EF context used in `CommandProcess`.  After Phase 7, EF-based reads of
   `Robots` should still work because the DB is kept in sync by the write-
   through setters; no EF changes required.

4. **`RefreshAllPlayers()` callers**: After all phases are done, `RefreshAllPlayers()`
   is replaced by `ReloadPlayerRuntimeFields()` (targeted reload).  Remove or
   mark `RefreshAllPlayers()` as obsolete once all call sites are migrated.
