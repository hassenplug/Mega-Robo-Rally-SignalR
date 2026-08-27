# Removing AllPlayers as an In-Memory Data Cache

**Status:** Proposed — not yet implemented
**Date:** 2026-08-27
**Related:** [API_DECOMPOSITION_DESIGN.md](API_DECOMPOSITION_DESIGN.md) (§4, §5.5, step 6 — `Player`/`IRobotTransport` split), [ALLPLAYERS_REFACTOR_PLAN.md](ALLPLAYERS_REFACTOR_PLAN.md) (superseded, see §8), [DB_SYNC_ISSUES.md](DB_SYNC_ISSUES.md) (mostly moot, see §8), `.claude/agents/move-to-memory.md` (conflicts, see §8)

## 1. Trigger

[`GetRobotsFromTable()`](../MRR/DataService.Players.cs#L153) now builds `AllDataPayload.robots` — what every SignalR broadcast sends to the phones — by reading the `Robots` table directly, every time. Its own comment says so: "replacing the old path that built `RobotData` from the in-memory Players/GameCards collections." That was the one job `AllPlayers`, as an in-memory mirror of the `Robots` table, existed to do fast. It no longer does that job, so most of what still writes to it and reads from it is upkeep for a cache nothing consumes.

**Goal:** remove the in-memory robot-*data* mirror everywhere it isn't needed, and confirm the one place it should stay — the snapshot built while creating commands — is already correctly isolated from this cache.

## 2. Two things are both called "AllPlayers"

| Concern | Type | Lives where | Needed in memory? |
|---|---|---|---|
| Live robot connection registry — WebSocket, `ScreenUI`, `isConnected`, `IPAddress` | `Players : List<Player>` | `DataService.AllPlayers`, forwarded by `GameController.AllPlayers` | **Yes.** A socket cannot be reconstructed from a SQL row. |
| Mirrored robot *data* — position, damage, energy, status, score, cards, message | Same objects (`Player : PlayerState`) | Same collection, freshened by `RefreshAllPlayers()` / `GetAllPlayers()` | **No, not anymore.** `GetRobotsFromTable()` reads this straight from `Robots` on every broadcast, and no other reader depends on the mirrored value — see §3. |
| Turn-planning snapshot | `PlayerStates : List<PlayerState>` | `TurnRequest.Players` → `CreateCommands.AllPlayers` | **Yes — this is the named exception.** Already pure: `MRR.Rules` cannot reference `MySqlConnector`, enforced by the build. |

This document's job is to strip row 2 out of row 1's collection, and confirm row 3 is already correctly separate.

## 3. Evidence the mirrored data is dead weight

- **`GetRobotsFromTable()` already bypasses it** for the one consumer (broadcast) that used to justify it.
- **`ProcessDbCommand`** ([DataService.Commands.cs:104](../MRR/DataService.Commands.cs#L104)) does a dual write in nearly every `SquareAction` case: `robot.Damage = cParameter` (in-memory) *and* `db.Robots...ExecuteUpdate(...)` (DB). Nothing downstream reads the in-memory value before the next full reload discards it: `BuildTurnRequest()` calls `ReloadAllData()` → `GetAllPlayers(forceRefresh: true)` → a fresh `Robots` read, every turn. The in-memory writes execute, cost cycles, and are read by no one.
- **`UpdateCardPlayed`** ([DataService.Cards.cs:307](../MRR/DataService.Cards.cs#L307)) does the same thing once, then its own comment admits it: *"Guarded rather than assumed... this is not reachable — but that is incidental protection, not a guard."* Nobody reads `player.PlayerStatus` after that line.
- **`CommandProcess.cs:347-350`** sets `robotPlayer.MessageCommandID = ...` then calls `_dataService.RefreshAllPlayers()` — a full re-sync purely to keep a cache current that has no reader.
- **`DB_SYNC_ISSUES.md`'s 14 open items** are all instances of this same cache going stale relative to the DB. Once the cache is gone, the `AllPlayers`-related items are moot by construction, not "fixed."

## 4. The one legitimate exception (confirmed, unchanged)

`MRR.Rules/CreateCommands.cs`'s `AllPlayers` ([CreateCommands.cs:59](../MRR.Rules/CreateCommands.cs#L59), type `PlayerStates`) is exactly "except during the process of creating commands":

- Sourced from `TurnRequest.Players`, built once per turn.
- Deep-copied into `workingPlayers` for simulation ([CreateCommands.cs:490](../MRR.Rules/CreateCommands.cs#L490)); the working copy is explicitly never saved back ("Note: Don't refresh real AllPlayers; we're working on copy only").
- `MRR.Rules` has no project reference to `MySqlConnector` or ASP.NET — it *cannot* reach the database, enforced by the build, per API_DECOMPOSITION_DESIGN.md §5.3.

**Recommendation: leave this exactly as is.**

One seam still ties this snapshot to the cache being removed: `BuildTurnRequest()` builds it via `Players = new PlayerStates(AllPlayers)` ([DataService.Commands.cs:54](../MRR/DataService.Commands.cs#L54)) — i.e. it copies out of the connection-registry collection. Once that collection stops being kept data-fresh (§6), this line would be reading whatever the registry happened to have cached, not a guaranteed-current value. Fix: add `DataService.GetPlayerStatesFromDB()` — a direct `Robots` query into bare `PlayerState` objects (no sockets, no registry dependency) — and use it here instead. This is the one behavior change this document requires outside of deleting dead writes.

## 5. Per-callsite audit

| File | Line(s) | What it does | Verdict |
|---|---|---|---|
| `MRR/DataService.cs` | 44-58 | `AllPlayers` property, lazy-loads via `GetAllPlayers()` | **Keep**, redefine contract to registry-only (§6) |
| `MRR/DataService.cs` | 185 | `ReloadAllData()` → `GetAllPlayers(forceRefresh: true)` | Keep — reloads roster/connections, not data, after this change |
| `MRR/DataService.Players.cs` | 75-115 | `GetAllPlayers()` — loads roster, then calls `RefreshAllPlayers()` | **Change** — drop the trailing `RefreshAllPlayers()` call (§6) |
| `MRR/DataService.Players.cs` | 199-232 | `RefreshAllPlayers()` — re-syncs 15 runtime fields from `Robots` | **Delete**, once both callers above are gone |
| `MRR/DataService.Commands.cs` | 54 | `Players = new PlayerStates(AllPlayers)` in `BuildTurnRequest()` | **Replace** with `GetPlayerStatesFromDB()` (§4) |
| `MRR/DataService.Commands.cs` | 117, and every `robot.<field> = ...` line in the `ProcessDbCommand` switch (132, 139, 149, 168, 192, 224, 261, 267, 282-287) | Resolve `robot`, mirror each DB write into it | **Remove** — keep only the `db.Robots...ExecuteUpdate` calls |
| `MRR/DataService.Cards.cs` | 196 | `Player? player = AllPlayers.GetPlayer(...)` in `UpdateCardPlayed` | **Remove**, along with the `player.PlayerStatus = newStatus` write at line 307 |
| `MRR/CommandProcess.cs` | 46-50 | Attach `command.Robot` from `AllPlayers` at construction | **Keep** — feeds `CommandItem.Description`'s `Robot?.CardsPlayer` (needs the roster's `GameCards` wiring), not runtime data |
| `MRR/CommandProcess.cs` | 245, 337 | `_dataService.AllPlayers.GetPlayer(...)` to get the connected robot before sending a WS command | **Keep** — needs the live socket, cannot come from the DB |
| `MRR/CommandProcess.cs` | 347-350 | `robotPlayer.MessageCommandID = ...;` then `_dataService.RefreshAllPlayers();` | **Remove both** — the `ExecuteUpdate` two lines above is what actually matters |
| `MRR/GameController.cs` | 43 | `AllPlayers => _dataService.AllPlayers` passthrough | **Keep** |
| `MRR/GameController.cs` | 319, 429, 491, 526, 548, 571, 587, 603, 612 | Iterate/lookup for `.Connect()`, `.isConnected`, `.ScreenUI`, `.UpdateStatusLEDs()`, `.DisposeAsync()` | **Keep** — all connection/transport operations; `UpdateStatusLEDs()` reads only `CardsPlayedStr` (derived from `GameCards`), not mirrored data |
| `MRR/GameController.cs` | 281 | `_dataService.GetAllPlayers(true)` after `StartGame()` rebuilds `Robots` | **Keep** — this reload is about roster membership (new game's robot list), not data freshness |
| `MRR/Admin/AdminApi.cs` | 155 | `players = data.AllPlayers.Count` | Cosmetic — could become `COUNT(*)`, no correctness reason to change |
| `MRR/Admin/AdminApi.cs` | 163 | `robotsConnected = data.AllPlayers.Count(p => p.isConnected)` | **Keep** — genuinely live socket state, the point of a diagnostics endpoint |
| `MRR/Program.cs` | 174 | `dataService.AllPlayers.GetPlayer(pid)?.UpdateStatusLEDs()` | **Keep** — connection use |
| `MRR/Program.cs` | 228 | Commented-out dead line | No action (or delete as cosmetic cleanup) |
| `MRR.Contracts/CommandList.cs` | 218, 297 | Historical comment + dead commented-out line about a removed `static AllPlayers` | No action (or delete line 297 as cosmetic cleanup) |
| `MRR.Contracts/PlayerStates.cs` | 8 | Doc comment: "Master builds one of these from AllPlayers" | Update wording after §4's change lands (cosmetic) |
| `MRR.Rules/CreateCommands.cs` | all refs | Turn-planning snapshot | **Keep, unchanged** — this is the named exception (§4) |

## 6. `DataService` surface after the change

- `AllPlayers` stays as a property and stays named `AllPlayers`, but its contract narrows: it is the **connection registry**, not a data mirror. `GetAllPlayers()`'s roster load keeps `ID`, `Name`, `Color`, `ForeColor`, `Password`, `IPAddress`, `PlayerSeat`, `PlayerViewDirection`, and the `AllGameCards` wiring — nothing else — and no longer calls `RefreshAllPlayers()`.
- `RefreshAllPlayers()` is deleted once its two callers (`GetAllPlayers()`, `CommandProcess.cs:350`) are gone. `RefreshRobotDenormalizedFields()` stays — `GetRobotsFromTable()` and `SetStatus()` still need it.
- `ProcessDbCommand` loses every `if (robot != null) robot.<field> = ...` line; only the `db.Robots...ExecuteUpdate(...)` calls remain. This also lets `robot` itself be dropped from the method except where `SquareAction.PlayerLocation`'s status-5 block needs nothing from it either (its `robot.CurrentPos...` / `robot.Score` assignments are the same pattern — remove them too).
- `UpdateCardPlayed` drops the `AllPlayers.GetPlayer` lookup and the trailing `player.PlayerStatus = ...` write.
- `BuildTurnRequest()` calls a new `GetPlayerStatesFromDB()` instead of wrapping `AllPlayers`.

## 7. What stays untouched

- `GameController.cs`'s iteration over `AllPlayers` for `.Connect()`, `.isConnected`, `.ScreenUI`, `.UpdateStatusLEDs()`, `.DisposeAsync()`.
- `CommandProcess.cs`'s `GetPlayer(...)` lookups that fetch the live socket to send a command, and the constructor's `command.Robot` attach (needed for `Description` text).
- `Admin/AdminApi.cs`'s `robotsConnected` diagnostic.
- `Program.cs`'s `UpdateStatusLEDs()` call.
- `MRR.Rules/CreateCommands.cs`'s `AllPlayers` (`PlayerStates`) — the explicit exception.

## 8. Conflicts with existing documents — decided 2026-08-27

- **`ALLPLAYERS_REFACTOR_PLAN.md`'s premise was the opposite move** — wrapping every write so `AllPlayers` never goes stale, instead of removing the thing being kept fresh. **Superseded**; its banner now points here.
- **`.claude/agents/move-to-memory.md` described the same opposite direction at a larger scope** — write-through setters on `Player`, a `SuppressDbWrites` flag, `ReloadPlayerRuntimeFields()` between states 6→7, per-command sync inside `ProcessDbCommand` (its Phase 7 is, almost verbatim, what §6 above says to delete instead of build). **Retired.** Its Phase 3 (an in-memory `GameCards` collection) is unrelated to this document and already partially done elsewhere — untouched by the retirement.
  - One thing from that agent's design is explicitly *not* being undone: `AllPlayers` as a WebSocket connection registry. Confirmed by tracing the code — `ConnectAsync()` opens `wsCmd`/`wsStatus` once per `Player` and every subsequent command for that robot reuses the same open socket (`SendCommandAsync`, `GetStatusAsync`), and `CommandProcess.ProcessCommand` reads `robot.isConnected` as a live branch condition on every poll iteration. All robot communication runs through this registry, so it isn't a casualty of retiring the agent — only the *data*-mirroring parts of its design (and of `AllPlayers`) are.
- **`DB_SYNC_ISSUES.md` items 1, 2, 3, and 5** are all `AllPlayers`-drift specifically; marked individually as moot once this document is implemented — nothing left to drift. Items 4, 6-14 (concerning `GameCards`, `ListOfCommands`, `OptionCards`, `CurrentGameData`) are unaffected and stay open.

## 9. Rollout order

1. Add `GetPlayerStatesFromDB()`; switch `BuildTurnRequest()` to it. Do this first — everything else assumes it's safe to stop keeping data fields fresh, and this is the one call that actually depended on that.
2. Strip the field-mirroring writes from `ProcessDbCommand` (§6).
3. Strip the field-mirroring write from `UpdateCardPlayed` (§6).
4. Delete `CommandProcess.cs:347-350`'s `robotPlayer.MessageCommandID = ...` and `RefreshAllPlayers()` call.
5. Remove `RefreshAllPlayers()`'s call inside `GetAllPlayers()`; then delete the method (confirm zero remaining callers first: `grep -rn "RefreshAllPlayers\b"`).
6. Leave everything in §7 untouched.
7. Optional cosmetic follow-ups: reword the `PlayerStates.cs` doc comment; delete the two dead commented-out lines (`CommandList.cs:297`, `Program.cs:228`).

## 10. Verification

- After steps 1-5, `grep -rn "RefreshAllPlayers\b"` returns nothing.
- Play a full turn; confirm phones' displayed position/damage/status/cards still update every broadcast — they come from `GetRobotsFromTable()`, untouched by this change.
- Confirm `CommandList` descriptions ("played card: X") still render — depends on `command.Robot.CardsPlayer`, untouched.
- Confirm `/api/admin/diagnostics` still reports a `robotsConnected` count.
- Confirm a robot disconnect/reconnect mid-game still works (`ConnectToAllRobots`, `DisconnectAllRobots`, `SetRobotConnected`) — none of this touches the removed data-mirroring code.
