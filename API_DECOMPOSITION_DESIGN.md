# MRR API Decomposition Design

**Status:** In progress — steps 0 and 1 implemented (see §9)
**Date:** 2026-08-22 (decisions 1–8 resolved)
**Related:** [install/PROCESS_MANAGER.md](install/PROCESS_MANAGER.md) — supervision, and the
implemented units in [install/service/](install/service/). §9 of this document specifies the
changes that decomposition requires there.

> The root-level `PROCESS_MANAGER_DESIGN.md` is an earlier draft, superseded by
> `install/PROCESS_MANAGER.md`, which documents a working implementation. Only the
> `install/` one should be maintained.

---

## 1. Goal

Break the single `MRR` ASP.NET Core app into independently-specified APIs with enforced
contracts, so the rules engine becomes deterministic and independently exercisable, board
authoring stops sharing a failure domain with live games, and each piece can be worked on
without breaking the others.

**Seven contracts. Two processes.** Reasoning in §3.

---

## 2. The design driver: tempo

The candidate services run at four different rates. That, not their function, determines
whether a boundary can afford to be a network hop.

| Tempo | Work | Latency budget | Network hop affordable? |
|---|---|---|---|
| **Between games** | Board authoring, game setup, seat + robot assignment | seconds | Yes, easily |
| **Per turn** (~1×) | Rules simulation | ~100 ms | Yes |
| **Per command** (~100×/turn) | Command execution, robot I/O | ~10 ms, during physical motion | **No** |
| **Continuous** | Phone/screen push | debounce-able to ~100 ms | Yes |

Two consequences that shape the design:

**Presentation and Device Gateway are not peers.** Presentation is a *lossy one-way view* —
drop a frame and nobody notices. Device Gateway is a *reliable control plane* — drop a
command and a robot is physically in the wrong square. Opposite reliability requirements;
treating them as two halves of "Communication" is what let
[`RobotScreenUI`](MRR/RobotScreenUI.cs) end up doing both jobs plus database writes.

**Only the between-games tempo justifies its own process.** Everything at per-command tempo
must share an address space; everything else is called only by Master and gains nothing
from a hop.

---

## 3. Architecture

```
  phones (6)      robot screens (6)     GM panel            authoring UI (desktop)
       |                  |                 |                       |
       +--- SignalR --+   +---- ws ----+    |                       |
                      |                |    |                       |
 ┌────────────────────┴────────────────┴────┴──────┐   ┌────────────┴─────────┐
 │  mrr-server.service  :5000                      │   │ mrr-config.service   │
 │                                                 │   │            :5001     │
 │   1 MASTER      state machine 0-16              │   │                      │
 │                 owns CurrentGameData / Robots   │   │  2 CONFIGURATION     │
 │                        │                        │   │    & AUTHORING       │
 │   3 RULES  ────────────┼──────── 4 EXECUTOR     │   │                      │
 │   pure planner         │         command runner │   │  boards, items,      │
 │   no I/O               │              │         │   │  actions, .srx       │
 │                        │              │         │   │  GameData            │
 │   6 PRESENTATION   5 DEVICE GATEWAY   │         │   │  OperatorData        │
 │   phones + screens AIM ws, transport  │         │   │  seats/bodies/bases  │
 │   + GM panel                          │         │   │                      │
 │                                                 │   └──────────┬───────────┘
 │   7 ADMIN   tables + direct SQL, localhost only │              │
 └──────────────────────┬──────────────────────────┘              │
                        └───────── rally (MySQL) ─────────────────┘
```

| # | Contract | Lives in | Process | Tempo |
|---|---|---|---|---|
| 1 | Master | `MRR.Host/Master/` | `mrr-server` | per turn |
| 2 | Configuration & Authoring | **`MRR.Config`** (project) | `mrr-config` | between games |
| 3 | Rules / Turn Planner | **`MRR.Rules`** (project) | `mrr-server` | per turn |
| 4 | Turn Executor | `MRR.Host/Executor/` | `mrr-server` | per command |
| 5 | Device Gateway | `MRR.Host/Devices/` | `mrr-server` | per command |
| 6 | Presentation | `MRR.Host/Presentation/` | `mrr-server` | continuous |
| 7 | Admin & Diagnostics | `MRR.Host/Admin/` | `mrr-server` | on demand |

Two of the seven are their own projects; the rest are folders in `MRR.Host` — see §3.2 for
why, and what it would take to promote one later.

Unit naming keeps the existing `mrr-server.service` for the game host rather than renaming
it to `mrr-game` — it is already installed, and `mrrctl`, the sudoers drop-in, and
`install.sh` all reference it. Only `mrr-config.service` is new.

### 3.1 Why one process for six of them

The benefits sought — testable rules, isolated work, clear ownership, no more 2350-line
`DataService` — come from **the contracts**. Processes additionally buy independent restart,
failure isolation, and independent deploy. On one Pi, one MySQL instance, one hard real-time
robot loop, only Configuration benefits from those.

Concretely: [`ProcessCommands`](MRR/CommandProcess.cs#L69) touches every active command on
every pass and currently broadcasts a full snapshot each iteration. A hop on the per-command
dispatch path costs a round trip per command per pass, on a Pi 5, while robots are moving.
Rules, Presentation, and Admin are called only from inside the host, so a hop buys nothing.

Admin specifically **must** be in-process: it modifies game state by direct SQL, and Master
holds that state in memory. A separate process could not invalidate Master's cache, so it
would leave the running game serving stale data. See §5.7.

"Master controls all the others" survives intact — interface calls instead of HTTP, and
Master is equally unbypassable either way.

**What would justify splitting further:** authoring or the planner in a different language;
running the planner on a laptop against a recorded game; two people deploying
independently; or measurement showing the per-command hop is cheaper than estimated.

### 3.2 Enforcing the boundaries without processes

**Decided: four projects, not one per contract.** Nine projects would compiler-check every
boundary, but on a Pi 5 where a build already takes 40s–2min that cost is paid on every
change, for a one-developer project. Four buys the guarantee that matters:

```
MRR.Contracts    ← no dependencies. Models + interfaces only.
MRR.Rules        → Contracts        [PURE — enforced by the build]
MRR.Host         → Contracts, Rules  Master / Executor / Devices /
                                     Presentation / Admin as folders
MRR.Config       → Contracts         separate host
```

`MRR.Rules` must not reference `MySqlConnector` or `Microsoft.AspNetCore.*`, and with no
project reference to either it cannot. That one constraint is what makes the planner
deterministic and independently exercisable.

The other five boundaries are conventions, not compiler rules: one folder per contract under
`MRR.Host`, one namespace each, and the §5 invariants (only Presentation holds an
`IHubContext`; only Devices opens a robot socket) enforced by review. If a boundary starts
leaking in practice, promoting that one folder to its own project is a mechanical change —
the namespaces already match.

---

## 4. Prerequisite: what must happen before any split

`DataService` is the whole application — one 2350-line singleton doing three unrelated jobs:

1. **Data access** — raw SQL, EF context, table CRUD, board load/save
2. **Live game state** — `AllPlayers`, `GameCards`, `OptionCards`, `g_BoardElements`,
   `GameState`, `Turn`, `Phase`, `BoardID` in memory
3. **Game rules** — [`ProcessDbCommand`](MRR/DataService.cs#L963), a ~220-line
   `SquareAction` switch that *applies* damage, flags, options, lives, and win conditions

It becomes:

- `IGameStateStore` — the in-memory current-game cache (Master only)
- `IBoardRepository`, `IPlayerRepository`, `ICommandRepository`, `ICardRepository`
- `RuleEffects` — the `ProcessDbCommand` switch, into **`MRR.Rules`**, shared by the planner
  and the executor so the two cannot disagree
- `TableAdminService` — the generic table CRUD and SQL execution, into **`MRR.Admin`** (§5.7)

**Structural landmines to clear first:**

| Landmine | Location | Why it blocks a split |
|---|---|---|
| `static Players? AllPlayers` on `CommandItem` | [CommandList.cs:213](MRR/CommandList.cs#L213) | A static back-reference from the command model into live game state. Any serialization silently loses `Robot`. |
| `Player` is both domain model and WebSocket client | [Players.cs:527](MRR/Players.cs#L527) — `ConnectAsync`, `SendCommandAsync`, `GetStatusAsync`, `AlignAsync` | Everyone needs `Player`; only Device Gateway should own a socket. Split into `PlayerState` + `IRobotTransport`. |
| `RobotScreenUI` writes game state and broadcasts | [RobotScreenUI.cs:355](MRR/RobotScreenUI.cs#L355) — `UpdateCardPlayed` then `SendAsync` | One method spans three contracts. |
| Board import lives inside the planner | [CreateCommands.cs:1864](MRR/CreateCommands.cs#L1864) — `LoadXMLBoards`, `BoardSaveToDB` | Between-games authoring code inside the per-turn hot path. Moves to Config. |

---

## 5. Contract specifications

### 5.1 Master — state machine and game state

**Owns** the 0–16 state machine and authoritative current-game state. Nothing else writes
`CurrentGameData`, `Robots`, `MoveCards`, `RobotOptions`, or `StatusLEDs` — except Admin,
which does so deliberately and then forces a reload (§5.7).

**From:** [GameController.cs](MRR/GameController.cs) entire, plus `/api/state`,
`/api/player`, `/api/settings`, `/api/alldata` at [Program.cs:90-205](MRR/Program.cs#L90-L205).

| Method | Route | Notes |
|---|---|---|
| `GET` | `/api/game/state` | State, turn, phase, board id |
| `POST` | `/api/game/advance` | Replaces `/api/state/nextstate` |
| `POST` | `/api/game/start` | `{gameDataId}` |
| `POST` | `/api/game/goto/{state}` | GM recovery (13/14/15/16) |
| `POST` | `/api/game/turn/execute` | Plan via Rules → hand to Executor |
| `POST` | `/api/players/{id}/card` | Card played — from phone **or** robot screen |
| `POST` | `/api/players/{id}/ack` | Prompt acknowledged (today's `clearpause`) |
| `GET` | `/api/snapshot` | The `AllDataPayload` clients read |
| `GET` | `/api/health` | Liveness. Path chosen to match what `mrr.env` already anticipates |

Master is produced by subtraction — what remains after the others leave. That is why
widening Config (§5.2) and extracting Admin (§5.7) matter: without them, Master reabsorbs
everything.

---

### 5.2 Configuration & Authoring — `mrr-config.service` (:5001)

**Widened from "Board Editor."** The original list had no home for pre-game setup, so it
would have defaulted into Master. Seat assignment, robot body/base mapping, `OperatorData`
/ player lists, and game selection share tempo and failure domain with board editing:
between games, nothing real-time, a crash is harmless.

**Owns:** `Boards`, `BoardItems`, `BoardItemActions`, `BoardSegmentList`, `BoardSquares`,
`GameData`, `OperatorData`, `RobotBases`, `RobotBodies`, `SeatOrientation`.

**From:** [Program.cs:282-599](MRR/Program.cs#L282-L599),
[BoardElement.cs](MRR/BoardElement.cs), `BoardLoadFromDB` / `BoardSaveToDB` /
`BoardFileRead`, `SeedBoardTemplate` at [Program.cs:610](MRR/Program.cs#L610),
`LoadXMLBoards` at [CreateCommands.cs:1864](MRR/CreateCommands.cs#L1864), the setup half of
[`StartGame()`](MRR/GameController.cs#L176), `board-editor.html`, `board-viewer.html`,
`datagrid-editor.html`.

| Method | Route |
|---|---|
| `GET` | `/api/boards`, `/api/boards/{id}`, `/api/boards/types` |
| `POST` `PUT` | `/api/boards`, `/api/boards/{id}` |
| `POST` `GET` | `/api/boards/{id}/import`, `/api/boards/{id}/export` (`.srx`) |
| `GET` | `/api/boards/{id}/validate` | **new** |
| `GET` `PUT` | `/api/gamedata`, `/api/gamedata/{id}` |
| `GET` `PUT` | `/api/operators`, `/api/operators/{id}` — name, seat, body, base, password |
| `GET` | `/api/hardware/bases`, `/api/hardware/bodies`, `/api/seats` |
| `GET` | `/api/health` |

**Extract first.** Lowest risk, clean existing HTTP surface, nothing in the game loop
depends on it, and it rehearses the pattern.

**Fix during the move:** the board `PUT` at
[Program.cs:453-547](MRR/Program.cs#L453-L547) does `DELETE` then bulk `INSERT` with no
transaction — a malformed request leaves the board empty — and builds SQL by string
concatenation with `Replace("'", "''")` as the only escaping. Wrap in a transaction, use
parameters.

**`/validate` has real work to do.** Verified against the live DB: 6 boards have gaps in
their flag numbering (board 3 is `1,4`; board 42 is `1,2,4`), which makes them unwinnable
because [CreateCommands.cs:1339](MRR/CreateCommands.cs#L1339) only advances on
`LastFlag + 1 == Parameter`. 16 boards have a stale `Boards.TotalFlags` column. Validation
should cover: contiguous flag numbering from 1, unique start positions, conveyors not
pointing off-board, and `Boards.TotalFlags` agreeing with the flag squares.

---

### 5.3 Rules / Turn Planner — pure, `MRR.Rules`

**The highest-value contract.** Not because it is separately deployable, but because it is
separately *testable*.

**From:** [CreateCommands.cs](MRR/CreateCommands.cs) minus its authoring code,
[PhaseFunctions.cs](MRR/PhaseFunctions.cs), [RotationFunctions.cs](MRR/RotationFunctions.cs),
[CardList.cs](MRR/CardList.cs), [OptionCards.cs](MRR/OptionCards.cs), plus `RuleEffects`
lifted from `ProcessDbCommand`.

```
TurnPlan Plan(TurnRequest request)

  request:  { turn, phaseCount, gameType, laserDamage, totalFlags,
              board:    BoardElementCollection,
              players:  PlayerState[],
              programs: MoveCard[],
              options:  OptionCard[],
              deck:     MoveCard[] }      // pre-drawn — decided, see below
  returns:  { commands: CommandItem[],    // ordered, sequenced
              projectedPlayers: PlayerState[],
              nextGameState: int,
              warnings: string[] }
```

No database, no SignalR, no robots. Same request → byte-identical command list.

**Four writes to sever.** The planner is not read-only today:

| Line | What it does | Resolution |
|---|---|---|
| [651](MRR/CreateCommands.cs#L651) | `AddCommandsToDatabase()` writes `CommandList` | Return the list; Master persists it |
| [634](MRR/CreateCommands.cs#L634) | `Update CurrentGameData ... iKey = 10` — sets game state to 7 | Returns as `TurnPlan.nextGameState`; Master applies it |
| [1638](MRR/CreateCommands.cs#L1638) | *(fixed 2026-08-22)* wrote a player's flag count into game-wide `TotalFlags` | Already removed — see §7 |
| [1027](MRR/CreateCommands.cs#L1027) | `GetNextCard()` draws a replacement card mid-simulation, chaining for Spam | **Pre-draw — decided** |

**Deck pre-draw (decided).** Master reads enough of each player's deck up front and passes
it as `request.deck`; the planner consumes from that list instead of calling the DB. Fully
deterministic and trivially fixture-able. Chained Spam cannot exceed the deck, so a bounded
worst case is computable — `Plan()` asserts it and reports a `warnings` entry rather than
throwing mid-simulation. `DataService.GetNextCard` stays (it is how Master fills the deck);
what goes away is the planner calling it.

**What purity unlocks:**

- **Regression fixtures.** Every open item in [install/todo.md](install/todo.md) — pushers,
  merge conveyors, reboot, shutdown, win condition, damage-card draw — becomes a board +
  program + expected command list. Today, testing a conveyor chain means a physical game.
- **Board validation.** Config's `/validate` can dry-run a board before robots are placed.

---

### 5.4 Turn Executor — `MRR.Executor`

**Owns** running a command list to completion. The `CommandList.StatusID` lifecycle
(1 waiting → 2 ready → 3 sent → 4 in progress → 5 apply → 6 complete) is private to it.

**From:** [CommandProcess.cs](MRR/CommandProcess.cs), calling into `RuleEffects`.

| Method | Route | Notes |
|---|---|---|
| `POST` | `/api/execution/run` | `{turn, commands[]}` — starts the runner |
| `GET` | `/api/execution/status` | Turn, phase, sequence, pending count, blocked-on |
| `POST` | `/api/execution/resume` | Clears a `CommandCatID=6` user-input wait |
| `POST` | `/api/execution/abort` | **new** — no way to stop a bad turn today |

**Fixes during the move:**

- **Fire-and-forget sends.** [CommandProcess.cs:185](MRR/CommandProcess.cs#L185) does
  `_ = robot.SendRobotCommandAsync(onecommand)` and immediately marks status 3/4. A send
  failure is invisible and the turn proceeds as if the robot moved. The `IRobotTransport`
  boundary forces a real awaited call with an error status.
- **Busy-wait.** `ProcessCommands` spins with no delay and no timeout. Add a poll interval
  and a per-command deadline that surfaces as a GM-visible stall.
- **No abort path.**

`/api/execution/abort` is also what makes a *game-level* pause possible — the thing
`install/PROCESS_MANAGER.md` §11 lists as out of scope for the process manager, and the
correct alternative to freezing the process mid-turn (§9.4).

---

### 5.5 Device Gateway — `MRR.Devices`

**Owns** all six AIM WebSocket pairs and is the only code that knows the AIM wire format.
**Transport only — no game rules, no database.**

**From:** the socket half of [Players.cs:470-700](MRR/Players.cs#L470-L700),
[RobotStatus.cs](MRR/RobotStatus.cs), [GridAlignmentAgent.cs](MRR/GridAlignmentAgent.cs),
the `/api/robot/*` endpoints at [Program.cs:210-280](MRR/Program.cs#L210-L280), and the
*rendering* half of `RobotScreenUI`.

| Method | Route | Notes |
|---|---|---|
| `GET` | `/api/robots` | Connection state, IP, last status, last-heard-from |
| `POST` | `/api/robots/connect` | `{robotId?}` — all if omitted |
| `POST` | `/api/robots/{id}/disconnect` | Currently unimplemented ([Program.cs:262](MRR/Program.cs#L262)) |
| `POST` | `/api/robots/{id}/command` | `RobotCommandRequest`, `awaitReply` flag |
| `GET` | `/api/robots/{id}/status` | IMU heading, odometry, motion state |
| `POST` | `/api/robots/{id}/align` | Camera grid alignment |
| `POST` | `/api/robots/{id}/screen` | Render a frame supplied by Presentation |
| `GET` | `/api/robots/{id}/screen/input` | Poll touch events → forwarded to Master |

`IRobotTransport` also gives simulation mode ("Use Robots" off) a real home: a null
transport, rather than scattered `isConnected` checks.

---

### 5.6 Presentation — `MRR.Presentation`

**Covers both player surfaces and the GM panel.** The AIM touchscreen is a second player
input surface, peer to the phones — same hand display, same card taps. Only its *transport*
is the robot socket. In the original list it fell under "Robots," which is exactly the
tangle in `RobotScreenUI`: it decides *what to draw*, writes cards to the DB, and broadcasts
SignalR. Split it — **Presentation decides what to render and where; Device Gateway carries
the bytes; Master owns the resulting state change.**

**The GM panel lives here, on the game host.** It is used *during* play, so it shares tempo
with Presentation, not with authoring. Its pre-game setup sections (robot body / base / seat
assignment) call Config's API cross-origin — one GM panel, so the GM never needs two tabs,
while Config keeps ownership of the data. Config enables CORS for the game-host origin.

**Owns** every push to a human. The only holder of an `IHubContext`.

**From:** [DataHub.cs](MRR/DataHub.cs), [AllDataPayload.cs](MRR/AllDataPayload.cs), the
layout/decision half of [RobotScreenUI.cs](MRR/RobotScreenUI.cs), and the ~8 scattered
`_hubContext.Clients.All.SendAsync("AllDataUpdate", ...)` call sites across
`GameController`, `PendingCommands`, `RobotScreenUI`, and `Program.cs`.

| Direction | Contract |
|---|---|
| In | `Publish(AllDataPayload)` — fan out per seat |
| In | `PublishPlayer(int seat, ...)` — targeted |
| Out | SignalR `/datahub` → `AllDataUpdate`, plus new per-seat `PlayerUpdate` |
| Out | `POST` to Gateway `/api/robots/{id}/screen` — same projection, different transport |
| Out | Serves `index.html`, `playonline.html`, `gmindex.html`, `buttonpage.html` |

**Fixes during the move:**

- **Every phone receives every player's hand and password.** `RobotData.Password` is in the
  broadcast payload and the hub sends `Clients.All`. In a 6-player game that is an
  information leak. Use SignalR groups keyed by seat.
- **Broadcast storm.** [CommandProcess.cs:88-90](MRR/CommandProcess.cs#L88-L90) rebuilds and
  re-serializes the full snapshot every loop iteration. Debounce to ~100 ms.
- **`/` returns 404.** [Program.cs:39-40](MRR/Program.cs#L39-L40) calls `UseStaticFiles()`
  before `UseDefaultFiles()`, so `/` is never rewritten to `index.html` and phones must be
  pointed at the explicit filename. Swapping those two lines is the fix, and it also lets
  the health probe stop depending on a static file path (`mrr.env` documents this).

---

### 5.7 Admin & Diagnostics — `MRR.Admin`

Replaces `/api/table`. The old endpoint does not survive, but the capability it provided —
edit any table, run direct SQL to fix game state mid-session — is a hard requirement, so it
gets a proper contract instead of an accident.

**Why in-process with Master, not in Config:** a direct `UPDATE Robots ...` changes state
that Master holds *in memory* (`AllPlayers`, `GameState`, `GameCards`). A separate process
cannot invalidate that cache, so the game would keep serving stale data and then overwrite
the manual fix on its next write-through. Every mutating Admin call must therefore run
`IGameStateStore.ReloadAllData()` and publish a fresh snapshot before returning.

| Method | Route | Notes |
|---|---|---|
| `GET` | `/api/admin/tables` | Table list |
| `GET` | `/api/admin/tables/{name}` | Rows, paged, with column metadata |
| `POST` | `/api/admin/tables/{name}` | Upsert rows — today's `SaveTableData` path |
| `POST` | `/api/admin/sql` | Arbitrary SQL. `{ sql }` → result set, or rows affected |
| `GET` | `/api/admin/sql/history` | Audit trail (below) |
| `GET` | `/api/admin/diagnostics` | State store vs. DB drift, socket states, thread flags |

**Requirements on it:**

1. **Reload after every write.** Any non-`SELECT` statement triggers `ReloadAllData()` plus
   a Presentation publish. Without this the tool actively corrupts a running game — which is
   the strongest argument for the contract existing rather than being ad hoc.
2. **Audit log.** Every statement, with timestamp, turn, phase, and rows affected, appended
   to a log. This is how "why did the game state go strange in round 4" becomes answerable
   after a tournament. Cheap, and the single most useful thing the old endpoint lacked.
3. **Localhost binding.** Kestrel listens for Admin routes on `127.0.0.1` only, or the routes
   are gated to a loopback/authenticated caller. Arbitrary SQL must never be reachable from
   the phone WiFi. This is a real change from today: `/api/table/{tablename}/{filter}/{setvalue}`
   ([Program.cs:52-69](MRR/Program.cs#L52-L69)) is a `GET` that executes caller-supplied SQL
   fragments and is currently exposed on `http://*:5000`.
4. **`GET` is read-only.** Mutations are `POST`. Today's mutating `GET` is crawler- and
   prefetch-hostile.
5. **`SELECT` statements skip the reload** — no point paying for it on a read.

Config gets a narrower version of the same table surface for its own tables
(`/api/admin/tables` scoped to boards and setup), since it has no in-memory game state to
invalidate.

---

## 6. Data ownership

One `rally` schema, exactly one writer per table. This keeps the split honest without a
database-per-service migration.

| Tables | Writer | Readers |
|---|---|---|
| `Boards`, `BoardItems`, `BoardItemActions`, `BoardSegmentList`, `BoardSquares` | **Config** | Master, Rules |
| `GameData` | **Config** | Master |
| `OperatorData`, `RobotBases`, `RobotBodies`, `SeatOrientation` | **Config** | Master, Gateway (IP lookup) |
| `CurrentGameData`, `GameState`, `PhaseCounter` | **Master** | all |
| `Robots`, `MoveCards`, `RobotOptions`, `StatusLEDs`, `MoveCardLocations` | **Master** | Rules (read), Executor (via `RuleEffects`) |
| `CommandList` | **Master** (insert) / **Executor** (status) | Presentation |
| `History*` | **Master** | — |
| Lookups (`CommandLookup`, `MoveCardTypes`, `Options`, `RobotStatus`, …) | migration scripts only | all |

**Admin is the deliberate exception** — it can write anything, which is its purpose. That is
exactly why it owes a reload and an audit entry on every write (§5.7).

**Readers read the database directly — they do not call the owner's API.** Master and Rules
query `Boards` / `BoardItems` / `BoardItemActions` themselves at game start; Config is simply
the only component that *writes* them. This is what makes "Config can be stopped mid-game with
no effect on play" true rather than aspirational — routing board loads through Config's HTTP
API would make it a runtime dependency and a single point of failure for starting a game.
Ownership here means write-ownership plus schema-ownership, not gatekeeping reads.

---

## 7. Defects the boundaries expose

| Severity | Defect | Status |
|---|---|---|
| **High** | Win condition hardcoded to 5 flags: `Player.TotalFlags` was `get => 5; set {}`, so `AddFlag` ignored the board's real flag count and its `> 5` branch wrote a player's progress into game-wide `TotalFlags`. | **Fixed 2026-08-22.** One game-wide `TotalFlags` in `CurrentGameData` iKey 7, taken from the board at game start. |
| **High** | Robot sends are fire-and-forget; a failed send advances the turn as if the robot moved ([CommandProcess.cs:185](MRR/CommandProcess.cs#L185)). | Open — §5.4 |
| **Medium** | Board `PUT` is `DELETE`+`INSERT` with no transaction ([Program.cs:453](MRR/Program.cs#L453)). | Open — §5.2 |
| **Medium** | Every phone receives every player's hand and password. | Open — §5.6 |
| **Medium** | No abort path once a turn starts. | Open — §5.4 |
| **Medium** | 6 boards have gaps in flag numbering and are unwinnable; 16 have a stale `Boards.TotalFlags`. | Open — board data; §5.2 `/validate` |
| **Medium** | `/api/table` is a mutating `GET` executing caller-supplied SQL on `http://*:5000`. | Open — §5.7 |
| **Low** | `/` returns 404; phones need the explicit `/index.html`. | Open — §5.6 |
| **Low** | `Urls: http://*:5000` binds every interface. | Open |

---

## 8. Decided: remove `RulesVersion`

Renegade only; the engine is never going backwards. **This is not a field removal — it is
dead-branch elimination.** `RulesVersion` is load-bearing today:

| Site | Action |
|---|---|
| [DataService.cs:1255](MRR/DataService.cs#L1255) `MoveCardsShuffleAndDeal` | Reads it, then branches. **Delete the `RulesVersion=0` Classic `else` branch** at [1389](MRR/DataService.cs#L1389); keep the Renegade path unconditionally |
| [DataService.cs:1487](MRR/DataService.cs#L1487) `GameFillPrograms` | Classic-only (`procGameFillPrograms`). Delete |
| [DataService.cs:1798](MRR/DataService.cs#L1798) `GameNewAddCards` | Reads it. Drop the read and the branch |
| [DataService.cs:87](MRR/DataService.cs#L87), [861](MRR/DataService.cs#L861) | Property and `UpdateGameState` `case 27`. Delete |
| [CreateCommands.cs:60](MRR/CreateCommands.cs#L60) | Passthrough. Delete |
| [GameController.cs:157](MRR/GameController.cs#L157) | `LoadGameData` CASE arm. Delete |
| [Program.cs:555](MRR/Program.cs#L555), [590](MRR/Program.cs#L590) | GameData `SELECT` column lists. Delete |
| [board-editor.html:1226](MRR/wwwroot/board-editor.html#L1226), [1245](MRR/wwwroot/board-editor.html#L1245) | Editor field lists. Delete |
| `install/MRRDatabase.sql:286`, `:704` | Drop `GameData.RulesVersion`; drop the `CurrentGameData` iKey 27 row |
| `install/gameconfig.sql` + copy | Drop the `RulesVersion` update line |

`DataService.GetNextCard` is **not** classic-only despite the neighbouring comment — it is
the Spam draw called from the planner, and it stays (§5.3).

Doing this before step 2 of §9 shrinks `MoveCardsShuffleAndDeal` and removes a branch the
new fixtures would otherwise have to cover twice.

---

## 9. Migration plan

Ordered by value-over-risk. Each step ends with a playable game; never more than one seam
open at a time.

| Step | Work | Status |
|---|---|---|
| **0. Contracts** | `MRR.Contracts` (4-project layout, §3.2); move the `Player`-free models; delete `static CommandItem.AllPlayers`; add `/api/health`. Remove `RulesVersion` (§8) | **Done** — `4560eb3`, `6e7969d` |
| **1. Split `Player`** | `PlayerState` (Contracts) + `Player : PlayerState` (host transport). Unblocks `CommandItem`, `CommandList`, `OptionCardList` into Contracts | **Done** — `0bba5bb`. Moved up from step 5; see below |
| **2. Config out** | Move board / gamedata / operator / hardware routes, `BoardElement`, load/save, `LoadXMLBoards` into `mrr-config`; transaction + `/validate` fixes; new systemd unit (§10) | **Two processes.** Authoring cannot disturb a live game |
| **3. Purify Rules** | Retarget `CreateCommands` from `Players`/`Player` to `PlayerState`; sever the writes in §5.3; pre-drawn deck; `Plan()` returns a `TurnPlan`; Master persists. Extract `MRR.Rules` | Deterministic planner; `CreateCommands` no longer references `DataService` |
| **4. Split `DataService`** | Extract `RuleEffects`, repositories, `IGameStateStore`. **Riskiest step — do it alone, on a branch** | Internal seams exist |
| **5. Admin** | Replace `/api/table` with `MRR.Admin`: reload-after-write, audit log, loopback binding, `POST` for mutations | A safe way to hand-edit game state mid-session |
| **6. Device Gateway** | `IRobotTransport` over the transport half of `Player`; awaited sends, timeouts, `/abort` | Robot failures visible; real simulation mode |
| **7. Presentation** | Invert `RobotScreenUI` to render-and-report; centralize pushes; per-seat groups; debounce; fix `/` → 404 | Password leak and broadcast storm fixed |

### Why the `Player` split moved to step 1

Discovered while implementing step 0. `MRR.Rules` *is* `CreateCommands`, and
`CreateCommands` had 51 references to `Player`/`Players` — `ProcessMove(Player)`,
`MoveRobot(Player…)`, `AddDamage(Player…)`, `workingPlayers`. `Player` owned three
WebSockets and a `RobotScreenUI`, so it could not live in Contracts, so neither could
anything that referenced it: `CommandList.cs` (13 references) and `OptionCardList`
(3 methods) were stuck in the host for the same reason.

Purifying Rules was therefore blocked on splitting `Player`, not the other way round.
Splitting it first also made three files move to Contracts with no signature changes at
all, because `Player : PlayerState` means every existing call site upcasts implicitly.

The remaining `Player` work — putting the transport behind `IRobotTransport` — is genuinely
independent and stays late, at step 6.

Steps 1 and 2 carry most of the value and are independently shippable. Step 4 can be pulled
earlier if hand-editing game state is needed before the rest lands — it only depends on
step 0.

### Why Rules comes before `DataService`

All twelve of the planner's `_dataService` property passthroughs
([CreateCommands.cs:54-83](MRR/CreateCommands.cs#L54-L83)) are reads that can be built into a
`TurnRequest` at the call site in `ExecuteTurn()`, and the four genuine writes are severable
independently (§5.3). Nothing about step 2 requires step 3.

**Decided: no test project** — verification is playing a game. That removes the original
argument for this order (build a regression suite before the risky surgery) but not the
order itself, for two reasons:

1. **It shrinks step 3.** After step 2, `CreateCommands` — 1917 lines, `DataService`'s single
   largest consumer — no longer references `DataService` at all. Cracking open the untested
   2350-line singleton with that dependency already severed is a materially smaller change
   than doing it with `CreateCommands` still wired in.
2. **Mechanical before structural.** Step 2 threads parameters and moves four writes to the
   caller. Step 3 restructures the class every component depends on. With no tests in either
   case, doing the mechanical one first is the lower-variance sequence.

---

## 10. Process manager changes

The existing supervision design ([install/PROCESS_MANAGER.md](install/PROCESS_MANAGER.md))
already anticipates a second process — its §10 is "Adding a second managed process," and
`mrr.target` + `PartOf=` exist for exactly this. What decomposition needs is spelled out
there in a new section; summarised here:

| Area | Change |
|---|---|
| Units | Add `mrr-config.service`. `mrr-server.service` keeps its name and becomes the game host |
| Isolation | `mrr-config` must **not** be `PartOf=mrr.target`, or a group restart bounces the board editor with the game — and the point of splitting it out was that it cannot be affected |
| `mrrctl` default | Bare `start`/`stop`/`restart`/`pause` should address the **game host**, not the target; add `all` for the group. Restarting the editor because you restarted the game is harmless; the reverse is not |
| `mrr-preflight` | Takes a role argument. Config's gate checks app + DB + port 5001, and **skips the SPI check** — only the game host touches `/dev/spidev0.0` |
| `mrr-health-check` | Parameterize `UNIT`, URL, and strike file; probe both units |
| `mrr.env` | Role-scoped `MRR_GAME_*` / `MRR_CONFIG_*` for port, app dir, health URL |
| `mrrctl deploy` | Publish two projects to `/srv/mrr/game` and `/srv/mrr/config`, with independent rollback |
| Health path | Standardize on `/api/health` in both hosts — what `mrr.env` already says it wants |

**No reverse proxy.** Earlier drafts of this document put one on :5000 to give six services a
single origin. At two processes it is unnecessary: everything phone-facing (UI + SignalR hub)
is already in the game host on one origin, and Config is a desktop tool reached directly at
`:5001`. The only cross-origin call is the GM panel's setup sections hitting Config, which
one CORS entry covers. Dropping the proxy removes a process, a config file, and a failure
mode.

---

## 11. Acceptance criteria

- A full 6-player, 5-phase turn completes with physical robots after each step.
- `Plan()` is deterministic: identical `TurnRequest` → byte-identical command list. With no
  test project, verify by calling it twice on the same request and diffing the two command
  lists at runtime (a temporary assert in `ExecuteTurn` is enough).
- `MRR.Rules` references neither `MySqlConnector` nor `Microsoft.AspNetCore.*`, enforced by
  the build.
- `CreateCommands` contains no `_dataService` reference after step 2.
- Config can be stopped mid-game with no effect on play, and `mrrctl restart config` does
  not touch the game host.
- A direct `UPDATE Robots ...` through Admin is reflected in the phones' next update without
  a game restart, and appears in the audit log.
- Arbitrary SQL is not reachable from the phone network.
- Each phone receives only its own hand and password.
- A robot that stops responding surfaces as a GM-visible stall, not a silently advanced turn.
- Exactly one type holds an `IHubContext`; exactly one opens a robot WebSocket.
- `mrrctl status` reports both units; both survive a cold Pi boot.

---

## 12. Resolved decisions

| # | Decision | Resolution |
|---|---|---|
| 1 | Spam deck: pre-draw vs. `ICardSource` | **Pre-draw.** `TurnRequest.deck` (§5.3) |
| 2 | Does `/api/table` survive? | **No** — replaced by `MRR.Admin` (§5.7), which keeps table editing and direct SQL but adds reload-after-write, an audit log, and loopback binding |
| 3 | Is `RulesVersion` enforced? | **Removed.** Renegade only; Classic branches deleted (§8) |
| 4 | Where does the GM panel live? | **Game host**, with Presentation. Pre-game setup sections call Config cross-origin (§5.6) |
| 5 | Process manager structure | Two units, isolation via omitted `PartOf`, role-scoped preflight and health (§10 and `install/PROCESS_MANAGER.md`) |
| 6 | How many projects? | **Four** — Contracts, Rules, Host, Config (§3.2). Build cost on the Pi outweighs compiler-checking boundaries a single developer can hold in review |
| 7 | Test project? | **No.** Verification is playing a game. Step 2/3 order still stands — see §9 |
| 8 | Board reads: direct DB or via Config? | **Direct, read-only** (§6). Keeps Config off the runtime path so it can be stopped mid-game |
