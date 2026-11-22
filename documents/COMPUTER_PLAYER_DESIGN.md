# Computer Player — Design

**M0 closed; M1 built and first played 2026-11-22 (all-computer table, 4 turns, no errors) — a few
M1 checks remain, see §6.2 (written 2026-11-21; decisions recorded 2026-11-22).** Draft guidance from a read of the
code. Items marked *starting guess* are placeholders to tune, not measurements — except where §6.1 now
gives a measured number — and §9 lists what was **not** checked. Verify against the code before
building from it.

## 1. Decisions made, and assumptions

A **computer player** is a robot seat with `Robots.Computer > 0` that chooses its own registers,
facing and shut-down with no phone or robot-screen user, and then goes through the same state machine
as everyone else.

| Question | Decision (2026-11-22) |
|---|---|
| Purpose | **Fill empty seats** when there are fewer humans than robots. |
| How computers get seats | **One GM button** that turns every still-unselected seat into a computer — the same shape as *End Selection*, which removes them instead (§4). |
| Prompts on a computer's robot | **The GM keeps seeing and confirming them.** No auto-confirm, no re-targeting (§3, gaps 3–4). |
| Strength | **Selectable level, up to Expert**, chosen by the GM when pressing the button (§5.0). |
| Where the code lives | **New `MRR.Bot` project**, referencing `MRR.Contracts` and `MRR.Rules` only. |
| Unplayed Spam | **Stays in the hand** — confirmed intended. The AI's valuation uses it (§5.4). |
| **Timing** | **Levels 1–4 must never take longer than the players:** they search while the humans program, and when the last human finishes they are finished too. **Expert may take up to 5 s after the humans are done as its normal allowance, with 10 s as a hard ceiling** — confirmed 2026-11-22 (§5.7). |

Assumptions that remain:

- It drives a **real AIM robot or a simulated one**. Movement is unchanged; only *who programs it*
  changes.
- **Plays fair at every level:** never reads another robot's registers or hand, and never reads its
  own true draw order.
- **Out of scope:** option/upgrade cards (most are unimplemented, `PROJECT_STATUS.md` §5.2), Haywire
  ([HAYWIRE_DESIGN.md](HAYWIRE_DESIGN.md)), a robot-screen or LED "CPU" indicator
  ([ROBOT_EFFECTS_DESIGN.md](ROBOT_EFFECTS_DESIGN.md)), changing a computer's level after it is set,
  and making a computer seat human again mid-game.

## 2. What already exists

| Piece | Where | Why it matters |
|---|---|---|
| `Robots.Computer` column, commented "AI-controlled flag" | [MRRDatabase.sql:352](../install/MRRDatabase.sql), `.claude/agents/mrr-database.md:114` | **No schema change.** It is an `int` defaulting to 0, so it can hold the level (0 = human, 1–5 = computer level). Today it is only copied into the history tables ([DataService.Players.cs:968-1054](../MRR/DataService.Players.cs)); nothing reads it. |
| Programming is four plain `DataService` calls | `UpdateCardPlayed` ([Cards.cs:210](../MRR/DataService.Cards.cs)), `SetRobotDirection`, `ConfirmRobotDirection`, `ToggleShutDown` ([Players.cs:831-1303](../MRR/DataService.Players.cs)) | Phones use these. A computer needs one **atomic** variant (gap 11), but the rules it follows are theirs. `UpdateCardPlayed` picks the lowest `CardID` of the requested **type** from the hand, so a decision is just card *types* per register. |
| The state-4 gate | [GameController.cs:401-413](../MRR/GameController.cs) | Advances when no robot has `Status <> 4 AND Status < 9` and every `PositionValid <> 0`. This is the "are the players done" test; the computer reuses its exact shape (gap 12). |
| A **pure, deterministic turn planner** | `MRR.Rules` (references `MRR.Contracts` only), `TurnRequest` in, `TurnPlan` out | The expensive part of an AI — knowing what a program *does* on this board (belts, pits, walls, lasers, pushing, Spam) — already exists and is tested. [API_DECOMPOSITION_DESIGN.md](API_DECOMPOSITION_DESIGN.md) §5.3 already lists dry-runs as something its purity unlocks. |
| What a plan reports | `Flag` (Value = new flag count), `Dead`, `Damage`, `DealSpamCard`, and each command's `EndPos` ([CreateCommands.cs:1925-2060](../MRR.Rules/CreateCommands.cs)) | Enough to score an outcome without re-implementing any rule. |
| A request can be built in a few lines | [ShutDownTests.cs:55-66](../MRR.Tests/ShutDownTests.cs) | Fixtures for the brain are cheap. `PhaseCount` is a request field, so a plan can cover just the first *k* phases. |
| The `endselection` action | [Program.cs:185-201](../MRR/Program.cs), [gmindex.html:592](../MRR/wwwroot/gmindex.html) | The template for the new button: a `/api/state/{action}/{parameter1}` case guarded by `GameState == 1`, then `GetAllPlayers(true)` and `NextState()`. `parameter1` arrives as an int, which is where the level goes. |

## 3. Gaps — what stops a computer from playing today

| # | Gap | Evidence | Resolution |
|---|---|---|---|
| 1 | **Nothing triggers programming** for a robot with no client. | State 3 → 4 and 15 → 4 only set the state and the LEDs ([GameController.cs:396-399, 450-453](../MRR/GameController.cs)). | Hook after both `SetGameState(4)` calls; fire-and-forget, the same pattern as `ScreenUiLoadHand`. A new state-4 entry cancels any run still going. |
| 2 | **Seat claim is human-driven and sequential.** | `PlayerToSelect = claimed + 1` ([Players.cs:349](../MRR/DataService.Players.cs)); a phone waits until `PlayerToSelect` equals its seat ([loadrobots.js:827](../MRR/wwwroot/js/loadrobots.js)). `SelectSeat` never sets `Computer`. | New `fillcomputers` action (§4). It claims **every** unclaimed seat at once, so — exactly like *End Selection* — the GM presses it after the last human has picked; a human who has not picked yet can no longer join. |
| 3 | **The "Run Phase N" prompt goes to the lowest-`Priority` player** ([CreateCommands.cs:947-949](../MRR.Rules/CreateCommands.cs)); `CommandProcess` waits for a click ([CommandProcess.cs:446-493](../MRR/CommandProcess.cs)). | If that player is a computer, a human must confirm it. | **Decided: no change.** The GM sees and confirms it (the GM page shows a button for any robot's pending message, `PROJECT_STATUS.md` §1.8). Consequence: with computers early in priority order the GM clicks Run Phase up to 5× a turn. |
| 4 | **Physical-world prompts** ("Remove Robot", reboot placement) are addressed to the dead robot's owner. | [CreateCommands.cs:1258, 1862, 1995, 2055](../MRR.Rules/CreateCommands.cs) | **Decided: GM confirms.** Nothing to build. |
| 5 | **`BuildTurnRequest()` is unsafe mid-programming.** | It calls `ReloadAllData()` ([DataService.cs:214](../MRR/DataService.cs)), which clears and refills the shared `GameCards`; `UpdateCardPlayed` edits that same list in memory ([Cards.cs:299-314](../MRR/DataService.Cards.cs)). A phone tap during an AI snapshot could throw or corrupt it. | New **read-only** `BuildBotSnapshot` (§4) that reads the DB into fresh objects and never touches the shared collections. |
| 6 | **The planner mutates its inputs.** | Overwrites `Priority` on the shared `GameCards` ([CreateCommands.cs:594-597](../MRR.Rules/CreateCommands.cs)); consumes `DrawPiles`; option cards `Use()`; board-changing options. `BuildDrawPile` returns the **shared** `MoveCard` instances ([Cards.cs:410](../MRR/DataService.Cards.cs)). | Every simulation gets its own copies; v1 passes an empty `OptionCardList`. A test asserts inputs are unchanged after a search. |
| 7 | **`Computer` is not broadcast.** | `RobotData` ([AllDataPayload.cs:35](../MRR.Contracts/AllDataPayload.cs)) omits it. | Add it to `RobotData` and `GetRobotsFromTable`. **No `PlayerState` change**: with gaps 3–4 decided, the planner does not need to know. The ESP32 firmware filters the JSON ([.ino:167](../esp32-player-station/esp32-player-station.ino)), so an extra field is ignored. |
| 8 | **Robot-screen UI is created for every connected robot.** | `ScreenUiLoadHand`, `InitScreenUI` ([GameController.cs:617-647](../MRR/GameController.cs)) | Skip computer seats. |
| 9 | **The planner logs a line per call.** | `Console.WriteLine("Planned …")` ([CreateCommands.cs:748](../MRR.Rules/CreateCommands.cs)); `GameController` already logs the same summary. | Delete the planner's copy — the plan's only `MRR.Rules` edit. A search would otherwise print thousands of lines. (M0 measured the line's cost in speed as nothing, §6.1, so this is about log noise only and is low priority.) |
| 10 | **`NextState()` is single-flight.** A call made while another runs is ignored. | [GameController.cs:335-339](../MRR/GameController.cs) | After applying a decision, call `NextState()`, then re-check the state and retry briefly. Phones have the same latent race; the AI just makes it likelier. |
| 11 | **A program can only be changed one card at a time, with the gate able to fire in between.** | `UpdateCardPlayed` moves one card per call and re-derives Ready/Programming each time ([Cards.cs:210-363](../MRR/DataService.Cards.cs)). Replacing a 5-card program that way passes through half-old, half-new states, and it needs a card that may currently sit in another register. | New **atomic** `SetRobotProgram` (§5.8): all five registers (and the facing) in one transaction, refused once the turn is locked. This replaces my earlier "reuse `UpdateCardPlayed` ×5". |
| 12 | **No cheap "the humans are done" signal for the AI.** | The gate's test is inline SQL in `NextState()` ([GameController.cs:402](../MRR/GameController.cs)); programming happens through three paths (phone, robot screen, GM Sit Out). | A small poll of the same test restricted to `Computer = 0` (§5.7). Polling sees every path without hooking any of them. |
| 13 | **The gate cannot wait for a computer.** | State 4 advances the moment every robot is Ready ([GameController.cs:402-407](../MRR/GameController.cs)). A computer that is Ready with a placeholder would be locked in immediately, so Expert's extra seconds could never happen. | One new condition in the gate: `ComputerPlayers.MayLockIn()` (§5.7). It is true at once when no computer is entitled to extra time, so Levels 1–4 gain no latency. |
| 14 | **Nothing tells the players why the turn has not started** while Expert is still thinking. | The only message is `titlemsg`, built from Turn and Phase alone ([DataService.cs:127-129](../MRR/DataService.cs)); it shows on the GM page ([gmindex.html:715](../MRR/wwwroot/gmindex.html)) and the phones ([loadrobots.js:686](../MRR/wwwroot/js/loadrobots.js)). | Append a note while the gate is holding, through a small settable property on `DataService`. (The GM-UI agent notes mention a `CurrentGameData.Message`; I found no such message being set in `MRR/*.cs`, so the plan does not use it.) |

## 4. Architecture

```
GM presses "Fill with Computers" (level L)  ──►  DataService.ClaimUnselectedAsComputers(L)
                                                   └─ SelectSeat per open seat, then Computer = L

GameController.NextState() ── enters state 4 ──►  ComputerPlayers.Run()   (fire-and-forget, cancelled by the next entry)
   │
   ├─ t0:  every computer gets a legal PLACEHOLDER program  ──► SetRobotProgram   (Ready at once)
   │
   ├─ worker thread(s), below-normal priority:
   │      MRR.Bot.Scheduler: widen the search for every computer in turn  (breadth-first, §5.7)
   │         ├─ DataService.BuildBotSnapshot(robotId)       read-only
   │         ├─ MRR.Bot.Brain  ── uses MRR.Rules.CreateCommands as the simulator
   │         └─ each improvement ──► SetRobotProgram        (atomic; refused once locked)
   │
   ├─ poll every ~100 ms:  GameState != 4  or  no human still programming  ──►  stamp "humans done"
   │                         Levels 1–4 stop searching now;  Expert keeps going for 5–10 s more
   │
   └─ state-4 gate (NextState, case 4):  every robot Ready  AND  ComputerPlayers.MayLockIn()
          MayLockIn is true at once for Levels 1–4;  for Expert it is false until its search
          finishes or the 10 s ceiling passes, then the orchestrator calls NextState()
```

**New or changed:**

| What | Where |
|---|---|
| `MRR.Bot` class library — `Brain`, `BotProfile` (one preset per level), `Decision`, the **scheduler** (stages, breadth-first order, stop handling), scoring, board-distance helper, opponent sampler. References **`MRR.Contracts` and `MRR.Rules` only**, so it cannot reach a database or a socket. It is driven through callbacks (`shouldStop`, `onImproved`), which is also what makes the timing testable without a database or a real clock. | new project, added to the `.sln` and referenced by `MRR` and `MRR.Tests` |
| Orchestration: find robots with `Computer > 0` in a programmable status, apply placeholders, run the scheduler, poll the human clock, own `HumansDoneAt` and `MayLockIn()`, advance the state | `MRR/GameController.ComputerPlayers.cs` (partial, like the existing ScreenUi helpers) |
| The gate: one added condition, `&& MayLockIn()`, in `NextState()` case 4 (gap 13) | `MRR/GameController.cs:402-407` |
| A settable note appended to `titlemsg` while the gate holds (gap 14) | `MRR/DataService.cs:127-129` |
| `ClaimUnselectedAsComputers(level)`, `BuildBotSnapshot`, `GetComputerRobots`, `HumansStillProgramming()`, **`SetRobotProgram`** | `MRR/DataService.ComputerPlayers.cs` (partial; DataService is already split by concern) |
| `Computer` in `RobotData` / `GetRobotsFromTable` | Contracts + DataService |
| `case "fillcomputers"` in `/api/state/{newstate}/{parameter1}`, mirroring `endselection`: ignored unless `GameState == 1`; clamps the level to 1–5; claims every open seat; then `GetAllPlayers(true)` and `NextState()` (all seats are claimed, so state 1 → 2). Afterwards pushes each new robot's name and colour to its screen and LEDs, as `/api/setup/select` does ([Program.cs:322](../MRR/Program.cs)). Additive — no existing contract changes. | `Program.cs` |
| GM page: a level selector and a *Fill with Computers* button beside *End Selection*, confirming with the count taken from `GameConfig.AvailableStartPositions` (as `endSelection()` does); a "CPU" label for computer seats on the phone page | `wwwroot/` |
| Planner: delete the one console line (gap 9) | `MRR.Rules/CreateCommands.cs` |
| `ComputerPlayer` config section: `MaxParallel`, `SoloBudgetMs`, `StopPollMs`, `GraceSoftMs` and `GraceHardMs` (Expert's extra time; per level, 0 for Levels 1–4), per-level overrides of the `BotProfile` presets | `MRR/appsettings.json` (per `CLAUDE.md`, config lives there) |

**`ClaimUnselectedAsComputers(level)`:** for each `Robots` row with `Status <> 1`, in `RobotID` order:
take the next seat number (claimed + 1), the lowest-numbered unclaimed active body, call the existing
`SelectSeat` ([Players.cs:381](../MRR/DataService.Players.cs)), then
`UPDATE Robots SET Computer = @level, OperatorName = 'CPU n Level'` (the column is `varchar(20)`; "CPU 3 Beginner"
fits). Returns how many it claimed. Zero open seats is a no-op, so the button is safe to press twice.
`SelectSeat` has no "already claimed" check (it is commented out), so a human claiming at the same
instant could be overwritten; the same is true of *End Selection*, and the answer is the same — press it
when the humans are done.

**Rejected alternatives:** an external client process polling `/api/state` like the ESP32 station
(the payload lacks `ArchivePos`, deck/discard contents, `LaserDamage` and `FieldEnclosed`, and the seat
step needs server changes anyway; the DB-free `MRR.Bot` keeps that door open); reusing **Sit Out** as a
stand-in (it works today but the seat does nothing — useful as the fallback if the brain is disabled).

## 5. The decision procedure

### 5.0 Levels

The GM picks one level when pressing the button; it is stored per robot in `Robots.Computer`. A level
is a **`BotProfile` preset** over one shared engine, so five levels are not five programs. A level is
defined by **how much work its search is allowed to do and how it chooses**, never by wall-clock time —
time is set by the humans (§5.7). Level names and every number below are *starting guesses*.

| Level | Name | What it does | Mechanism (stages = beam widths, §5.2) |
|---|---|---|---|
| 1 | Beginner | Random legal program. No planning. | No search. The placeholder **is** the final program. Also the fail-safe brain every higher level falls back to. |
| 2 | Easy | Plans, but makes human-sized mistakes. | Stages 1, 3. The last stage **picks at random among its top 5**; Spam treated as plain bad. |
| 3 | Normal | Reaches flags steadily; occasional slips. | Stages 1, 3, 10, 20. Last stage picks among candidates within a small margin of the best; Spam sampling; chooses its facing after a reboot; simple shut-down rule. |
| 4 | Hard | Best program it can find against a static board. | Stages 1, 3, 10, 30, 60 (or exhaustive if M0 says it fits); **no noise**; more Spam samples. |
| 5 | Expert | Hard, plus it allows for opponents that move. | Hard's stages, then **re-ranks the top ~12 candidates across sampled opponent worlds** in rounds (§5.2). The only level allowed to run **5–10 s past the humans** (§5.7). |

Lower levels are deliberately imperfect by *choosing worse programs*, so a level's behaviour does not
depend on how fast the Pi is that day. The honest exception is Hard: it keeps widening until the humans
finish, so a table that programs very quickly gets a weaker Hard move. Expert has the same dependence
but gets its extra seconds after the humans to make up for it (§8).

### 5.1 Inputs
Board; every robot's position, facing, status and last flag; this robot's hand (`MoveCards`,
`CardLocation = 1`, grouped by type) and its deck + discard **contents** (not order); `PhaseCount`,
`TotalFlags`, `LaserDamage`, `FieldEnclosed`; its level. Levels 2–4 model the other robots as
**stationary, cardless, still on the board**: they can be pushed and can shoot, but do not move.

### 5.2 Search: widen, never block
Cards of one type are interchangeable, so search over **type sequences** of length `PhaseCount` drawn
from the hand multiset (at most 9·8·7·6·5 = 15,120 ordered picks; far fewer distinct ones). Each
candidate is scored by running the **real planner** on a snapshot where only this robot has cards. No
movement rule is re-implemented, so the brain cannot disagree with the game about what a card does.

Because the humans decide when the search ends, the search is built as **iterative widening**, so a
complete, sensible answer exists very early and only gets better:

- A **stage** is one complete beam search of width *B*. For `k = 1…PhaseCount`, extend each of the best
  *B* prefixes by each remaining card type, plan with `PhaseCount = k`, score the state after phase *k*,
  keep the top *B*. Prefix plans are exact for those phases because the planner resolves phases in
  order. Stage widths come from the level (§5.0); width 1 is plain greedy, about 35 short plans.
- A stage only ever produces a **complete** program, and its result replaces the current one only when
  the stage **finishes**. A search cut off mid-stage simply keeps the last finished stage — there is no
  half-built answer to repair.
- **Exhaustive** replaces the widest stage when M0 shows it fits.
- **Expert re-rank, also in rounds.** Take the top *N* candidates (starting guess 12). Round *r* scores
  *all N* on sampled world *r*: every other robot gets a plausible program — a random 9-card hand dealt
  from the standard deck's type mix, programmed by the **Level 2** search toward that robot's own next
  flag — and the planner runs with all robots moving (card priority now matters). After each full
  round, the candidate with the best running average becomes the current program. Opponent programs use
  **public information only** (positions, facings, last flags, shut-down state), never their cards.
  Cost is about *N × K* full plans for *K* rounds (starting guess 8). This re-rank is the only work
  that may run past the humans (§5.7), and it is built for that: it applies a result only after a
  *full* round, so being cut off at the ceiling never leaves a half-judged candidate in charge.

### 5.3 Score
Computed from the plan's commands for this robot. Weights are part of `BotProfile`, *starting guesses*:

| Term | Sign | Starting guess |
|---|---|---|
| Flags touched (`Flag` commands) | + | 1000 each |
| Died (`Dead` / `SetPlayerStatus 11`) | − | −400 (also costs two Spam, [CreateCommands.cs:1950](../MRR.Rules/CreateCommands.cs), and the lost position) |
| Damage taken (`DealSpamCard`) | − | −80 each |
| Progress: path distance to flag `LastFlag + 1` before minus after | ± | 30 per square |
| Ends facing along the next step of that path | + | 10 |

The path distance is a BFS over the board that treats walls (`HasWall`), `Block` squares and pits as
impassable and **ignores belts**; the planner's own belt handling already shows up in the end position,
so BFS only has to rank end squares. The flag rule (a robot advances only on `LastFlag + 1`) is in
`PROJECT_STATUS.md` §4.2.

### 5.4 Spam
Two code facts, both confirmed as intended by the user (2026-11-22):

- A played Spam is **removed** — `TurnPlan.SpamConsumed` → `RetireSpamCards` → location 5, deleted at
  the next deal ([Cards.cs:591-598](../MRR/DataService.Cards.cs)).
- An **unplayed Spam in hand stays in hand**: the hand→discard move exempts type 10
  ([Cards.cs:600-604](../MRR/DataService.Cards.cs)) and hand ranks first in the shuffle. So Spam
  clogs the hand — with *k* Spam the robot chooses from only `9 − k` real cards — until it is played or
  the robot shuts down.

So playing a Spam is both a cleanup and a random move. At levels 3+, score a sequence containing *s*
Spam by averaging *m* plans with the replacement pile re-sampled from the robot's unseen cards (*m* =
3 at Normal, more above; starting guess). **Never use the true pile order** (`BuildDrawPile`): that
would be peeking. Sequences without Spam are planned once.

### 5.5 Facing after a reboot
Only when `PositionValid == 0`. Level 1: keep the current facing (the placeholder confirms it). Level 2:
face the first step of the BFS path to the next flag. Level 3+: the stage search runs once per facing
(4×, work split) and the best wins. The facing travels **with the program** through `SetRobotProgram`,
so a program is never applied with a facing it was not evaluated for. Use the raw board `Direction`;
`DirectionAdjustment` is display-only.

### 5.6 Shut-down
Level 3+ only, rule-based and tunable: shut down when the robot's Spam count is at or above a threshold
*and* the current program's score is below a floor. Shut-down discards the hand and every damage card
([Cards.cs:440-478](../MRR/DataService.Cards.cs)) but the robot is still shot and pushed, so refuse it
when the robot stands in an obvious laser line (Hard and above). Only this brain writes a computer's
shut-down flag, so it reads the flag and calls `ToggleShutDown` only when the answer differs. If chosen,
`ToggleShutDown` fills any empty registers itself ([Players.cs:853-854](../MRR/DataService.Players.cs)).
The rule is re-evaluated each time the program improves, so it can be switched off again.

### 5.7 Timing: never later than the humans (Expert excepted)

The rule (user, 2026-11-22): **Levels 1–4 must not take longer than the players. Expert may take 5–10
seconds after the humans are done.** For Levels 1–4 the way to guarantee it, rather than hope for it, is
for the computer **never to be what the gate is waiting on**. Expert is the one level the gate is
allowed to wait for, and only for a bounded time.

1. **t0 — placeholder.** On entering state 4, every computer immediately gets a legal Level 1 program
   (random, `PhaseCount` distinct non-Spam hand cards; current facing confirmed) through
   `SetRobotProgram`. Each is Ready within a few milliseconds, so from then on the gate waits on the
   humans alone — and if anything later fails, a legal program is already in place.
2. **Search while the humans program.** A worker (below-normal priority, `MaxParallel` threads) widens
   the search for **every computer in turn, breadth-first**: all computers finish stage 1, then all
   finish stage 2, and so on — never one computer to the end before the next begins. Each finished
   stage that improves a program is applied at once, atomically (§5.8).
3. **Humans done.** `HumansDoneAt` is stamped by whichever notices first: a poll every `StopPollMs`
   (starting guess 100), or the gate itself (step 5). "Done" is the gate's own test restricted to
   `Computer = 0` — `Status <> 4 AND Status < 9` — so shut-down and sat-out humans do not hold the
   search open. A `GameState` other than 4 also ends the run. **Levels 1–4 stop searching at once**:
   the brain checks `shouldStop` between planner calls, so it overruns by at most one plan, and keeps
   the last *finished* stage.
4. **Expert's extra time.** After `HumansDoneAt`, Expert keeps going. Until `GraceSoftMs` (5 s,
   confirmed) it carries on as normal. Between `GraceSoftMs` and `GraceHardMs` (10 s, confirmed) it
   only **finishes the re-rank round already in progress** and starts no new one. At `GraceHardMs` it
   stops and throws away any unfinished round; the program stays at the best *full* round so far. It
   also stops the moment its re-rank is complete, so it does not wait out the clock for nothing. The
   5 s / 10 s split was confirmed by the user on 2026-11-22; both numbers remain config values.
5. **The gate.** State 4 advances only when every robot is Ready (existing test) **and**
   `ComputerPlayers.MayLockIn()` is true (gap 13). When every robot is Ready the gate calls it; the
   first call stamps `HumansDoneAt` if nothing has yet. It returns true **at once** when no active
   computer is entitled to extra time — every computer at Levels 1–4 — so those levels gain no
   latency. For Expert it returns false until its search is finished or `GraceHardMs` has passed. The
   deadline is an absolute timestamp the gate checks itself, so a dead worker thread cannot hold the
   game past it. When the hold ends, the orchestrator calls `NextState()` (the humans are done and will
   not tap again), with the retry from gap 10; the GM's Next State button also works.
6. **Players are told.** While the gate holds, `titlemsg` carries a note ("… waiting for computer
   players", gap 14); it is cleared on release.

**No human clock.** If no human is still programming when state 4 begins (an all-computer table, or
every human already shut down or sitting out), there is nothing to race: `HumansDoneAt` is t0. The extra
time every computer above Level 1 is entitled to is then `SoloBudgetMs` (starting guess 5 s; Expert
keeps its own grace if that is larger). The placeholder stays applied, the searches run, and the gate
holds until they finish or the time is up. One mechanism covers this case and the normal one; it
replaces my earlier "skip the placeholder and apply once" path.

Consequences, accepted: computer seats show **Ready from the start** of programming, and their
registers may be replaced a few times (the humans' phones show that); Hard's quality depends on how long
the humans take (§8); and at Expert the table may wait up to 10 s after the last human taps. A **new
state-4 entry** (GM *Create Program*, or *Reload Position*) cancels the run; robots already Ready keep
their program, which keeps re-entry idempotent.

### 5.8 Applying a decision, and failing safe

`SetRobotProgram(robotId, cardTypeIds, facing?)` — one transaction:

1. Lock the robot's row. Refuse (return false) unless `GameState == 4`, the robot's status has
   `RobotStatus.Programming = 1`, and `PositionValid < 2` (the lock-in sets it to 2,
   `LockAllRobotDirections`, [Players.cs:1412](../MRR/DataService.Players.cs)). The lock-in's own
   `UPDATE Robots SET Status = 13` queues behind this row lock, so the two cannot interleave.
2. Return every card in the robot's registers to its hand.
3. For each phase, take the lowest `CardID` of the requested type from the hand into that register.
   If any type is missing, **roll back** and return false — a program is applied whole or not at all.
4. If a facing is given, set `CurrentPosDir`/`SeatArrow` and `PositionValid = 1`.
5. Commit; then recompute status and the `CardsDealt`/`CardsPlayed` summary with the existing
   `UpdateProgrammingStatus`, mirror the change into the in-memory `GameCards` as `UpdateCardPlayed`
   step 8 does, and send the LED command **only if the status changed** (it is a robot round trip and
   must not run inside the transaction).

Then `UpdateGameState()` to broadcast and `NextState()` with a retry (gap 10).

**A computer player must never be the reason a game stalls.** Wrap the scheduler and every apply in
try/catch; on any failure log it and leave the robot on its last applied program, falling back to the
Level 1 placeholder if it has none. Robots already at Ready when a run starts (state 15/16 re-entry) are
skipped.

### 5.9 Resource limits
The Pi also runs MariaDB, the server and a Chromium kiosk, and a phone tap must not feel slower because
a brain is thinking. Brains run on dedicated **below-normal-priority** threads, at most `MaxParallel`
(starting guess 1), never on request threads. The human-clock poll is a single cheap indexed count every
~100 ms.

## 6. Milestones

Sizes are my estimate, not a commitment.

| # | Milestone | Size | Done when |
|---|---|---|---|
| **M0** | **Measure; no behaviour change. Done 2026-11-22 on the development machine and on the Raspberry Pi 4 (§6.1). Closed.** A benchmark test (`Category=Benchmark`, run by hand with `dotnet test --filter`, not a pass/fail gate) timing `CreateTurn` on a 12×12 board with 6 robots, for 1- to 5-phase plans, and the **time to finish stage 1 for 5 computers**. Also tests: planner output is identical on repeat; the board is unchanged after a plan; a cardless robot is an inert obstacle that still shoots. | S | Timings **from the dev machine and from the Pi** are recorded in this doc, and the stage widths in §5.0 are set so stage 1 for all computers is fast (target ≤ 1 s on the Pi, *starting guess*). **Dev machine: target met by a wide margin. Pi 4: stage 1 for five computers 0.64 s — target met (~1.5× margin); stage widths unchanged; Expert re-rank budget flagged for M4 (§6.1).** |
| **M1** | **Plumbing, Level 1, and the timing guarantee.** Gaps 1, 2, 5, 7, 8, 9, 10, 11, 12; `MRR.Bot` project with `Decision`, `BotProfile`, the random brain and the scheduler skeleton; `ClaimUnselectedAsComputers`, `fillcomputers`, the GM selector and button, `Computer` in the payload, the "CPU" label; `SetRobotProgram`; the human-clock poll; placeholder-at-t0. | M–L | The button turns every open seat into a computer at the chosen level and the game moves to state 2; a table with ≥1 computer runs **10 consecutive turns** with no stall other than prompts the GM confirms; **every computer is Ready within ~100 ms of state 4** (log timestamps); a phone tapping during a snapshot causes no exception; a `SetRobotProgram` refused after lock-in leaves the program untouched; *End Selection* still works. |
| **M2** | **Brain core, Levels 2–4.** Planner-based staged search, score, BFS distance, noise, breadth-first scheduling, refine-and-apply, stop handling, fallback; **the gate condition `MayLockIn()` and the title note** (gaps 13–14), needed here for the no-human-clock case. | M–L | Fixture and scheduler tests pass (§7); inputs unchanged after a search; **with 5 computers still searching, lock-in follows the last human's tap by no more than ~250 ms on the Pi** (*starting guess*, from log timestamps); an all-computer table is held for at most `SoloBudgetMs` and then locks in; the **regret test** orders Levels 1 > 2 > 3 > 4. |
| **M3** | Reboot facing, Spam sampling, shut-down rule (Levels 3+). | S–M | A fixture per feature; a manual run through death → reboot → programming. |
| **M4** | **Expert, and the arena that validates the ladder.** Opponent sampler and round-based re-rank; **Expert's extra time** (soft/hard deadlines, early release, §5.7); an *approximate* in-memory applier (final positions, flag count, Spam per `DealSpamCard`) so several brains can play many turns with no database. | M | In the arena, over a fixed turn count and fixed boards, **each level beats the one below** — margin agreed beforehand, not chosen afterwards. On the Pi, with Expert computers searching, **lock-in follows the last human's tap by no more than `GraceHardMs` + ~250 ms, and usually sooner** (log timestamps). See §8 for why the applier is approximate. |

### 6.1 M0 results (2026-11-22, development machine and Raspberry Pi)

**Setup.** Intel i7-1360P (16 logical threads), Windows 11, .NET 9 **Release**; a synthetic 12×12 board
(belts, fast belts, gears, pits, walls, lasers, 5 flags — the real board library is in the database, not
the repo), 6 robots, and a nine-card hand with 8 distinct types. Code: `MRR.Tests/BotFixtures.cs`,
`BotM0AssumptionTests.cs` (14 tests, run with the normal suite — 122/122 pass) and `BotM0Benchmark.cs`
(opt-in).

**Pi setup (run 2026-11-22 14:50).** The game server is a **Raspberry Pi 4 Model B Rev 1.5** (4 × Cortex-A72,
1.8 GHz, 8 GB) — *not* the Pi 5 that `CLAUDE.md` describes. Debian 13 (trixie), .NET 9.0.21 Arm64, Release.
Not throttled (`get_throttled=0x0`, 58 °C). **The game server was left running** — a game was live in
state 4 (turn 57) — as were the VS Code server and Copilot; the CPU was ~88 % idle at the start. So the Pi
numbers are slightly pessimistic for a quiet Pi, but realistic for a Pi that is also hosting a game.
The normal suite (including the 14 M0 assumption tests) also passes on the Pi: 122/122, Debug.

**Assumptions the plan relies on — all held:**

| Assumption | Result |
|---|---|
| The same input gives the same plan | ✔ 25 programs solo and 15 with all six robots programmed; a negative control confirms the comparison can tell plans apart |
| The planner does not change the board or the robots when no option cards are in play | ✔ 15 six-robot plans |
| A cardless robot never moves, and its laser hits me exactly when I end in its line | ✔ so "stationary opponents" (Levels 2–4) is a meaningful model |
| A plan with `PhaseCount = k` is exactly the first *k* phases of the full plan | ✔ k = 1–4, 30 programs, solo and all six; guarded against a vacuous pass. **The beam search in §5.2 is therefore exact, not an approximation.** |
| Several threads sharing one board and one player list give the same plans as one thread | ✔ 60 plans on 8 threads. **One board can be cached per game; `MaxParallel > 1` is allowed.** |
| King of the Hill's end-of-game commands do not touch the bot's robot (matters for 1-phase prefix plans) | ✔ |
| Gaps 5–6: the planner overwrites card `Priority` and consumes `DrawPiles` | ✔ pinned by two characterization tests — the bot must copy cards and draw piles |

**Timings (mean unless noted).**

| Measure | Dev machine | Pi 4 | Pi ÷ dev |
|---|---|---|---|
| One plan, only robot 1 holds cards, 1 / 2 / 3 / 4 / 5 phases | 0.07 / 0.19 / 0.33 / 0.51 / **0.71 ms** | 0.79 / 1.89 / 3.36 / 5.48 / **8.42 ms** | ×11.9 (5 ph.) |
| Memory allocated per solo plan | 0.13 MB (1 phase) to **1.3 MB** (5 phases) | same | — |
| One 5-phase plan with all six robots holding cards (an Expert opponent world) | **3.2 ms**, 5.3 MB | **36.7 ms** | ×11.5 |
| Search stage for one computer, width 1 / 3 / 10 / 30 / 60 (plans · time; median of 3) | 32 · 12 ms / 79 · 32 ms / 230 · 98 ms / 549 · 290 ms / 1016 · **533 ms** | 148 ms / 369 ms / 1.08 s / 2.96 s / **5.86 s** | ×11.0 (w 60) |
| All five stages for one computer (sum of the row above) | ≈ 1.0 s | **≈ 10.4 s** | |
| **Stage 1 for five computers, one after another** (the M0 target; median of 5) | **58 ms** | **642 ms** (target ≤ 1 s — **met**) | ×11.1 |
| Exhaustive search for this hand | 8,820 sequences × 1.0 ms ≈ **8.9 s** per computer | 8,820 × 10.6 ms ≈ **93 s** | |
| Expert re-rank at the planned size (12 candidates × 8 worlds = 96 six-robot plans), **per Expert computer** | ≈ **0.31 s** | ≈ **3.5 s** | ×11.4 |
| Throughput of 5-phase plans on 1 / 2 / 4 threads | 974 / 1575 / 2263 plans/s (×1.0 / 1.6 / 2.3) | 95 / 157 / 192 plans/s (×1.0 / 1.7 / 2.0) | |
| The planner's own console line (gap 9) | no measurable cost (×1.0) | ×1.0 | |
| One `BoardElementCollection.GetSquare` lookup | 0.13 µs | 1.14 µs | ×8.8 |

**The Pi 4 is about 11–12× slower than the dev machine**, consistently across measures. That is under
every break-even factor below, so the plan holds — but with far less headroom than on the dev machine.

**What this means for the plan, on the Pi 4** (added after the Pi run):

- **M0's target is met: stage 1 for five computers in 0.64 s** (≤ 1 s). Margin is only ~1.5×, so keep
  stage 1 at width 1 and do not add work to it.
- **Hard rarely reaches its widest stage when the table is full of computers.** Breadth-first over five
  computers, stages 1–3 (widths 1, 3, 10) take ≈ 8 s; width 30 for all five adds ≈ 15 s; width 60 adds
  ≈ 29 s. One Hard computer gets all five stages in ≈ 10.4 s. Since Hard keeps whatever stage it
  finished when the humans tap, this is acceptable by design (§5.0, §8), but stage widths 1, 3, 10, 30, 60
  are effectively "1, 3, 10 and sometimes 30" with five computers. **Not changed yet** — M2's regret test
  should say whether width 10 is already close enough to best, which would make the wider stages a bonus
  rather than a need.
- **Exhaustive search is out** (≈ 93 s per computer). Exhaustive references for M2's regret test should
  be computed on the dev machine, not the Pi.
- **Expert's re-rank budget is per computer, and that matters.** 3.5 s for one Expert fits the 5 s soft
  allowance; **five Expert computers at N = 12, K = 8 would need ≈ 17.6 s sequentially**, past the 10 s
  hard ceiling. (The dev-machine notes below did not account for several Expert seats.) The round-based
  re-rank still behaves correctly — it applies only full rounds and stops at the ceiling — but each
  Expert would get only 2–3 of its 8 worlds. Options for M4, not decided: start the re-rank during the
  humans' time as soon as Hard's stage 3 is done; lower *N*/*K* per computer as the number of Expert
  seats grows; or allow `MaxParallel = 2` for the re-rank only (×1.7 measured).
- **Parallelism: ×2.0 on 4 threads, ×1.7 on 2.** Keep `MaxParallel = 1` as planned; the Pi also runs
  MariaDB, the server and Chromium, and this run did not measure the effect on phone latency.
- **If the server moves to a Pi 5**, expect these times to drop roughly 2–3× (an estimate from published
  CPU comparisons, not measured here). Re-run the benchmark on the target hardware before tuning numbers.

**What this meant for the plan on the dev machine** (written before the Pi run; kept for the record):

- **Stage widths stay 1, 3, 10, 30, 60.** Nothing here argues for changing them. Running all five stages
  for one computer costs about 1 s; for five computers, about 5 s.
- **Exhaustive search is not the default.** About 9 s per computer even here, against 0.5 s for the
  widest beam. Whether a beam of 60 finds near-best programs is M2's regret test, and an exhaustive
  reference for each fixture is affordable (≈ 9 s each).
- **Expert's re-rank is cheap next to its 5 s soft allowance** (0.31 s here), so there is room to raise
  the candidates (*N*) and worlds (*K*) once the Pi's speed is known.
- **Parallelism pays, sublinearly.** 2.3× on 4 threads. Allocation (1.3 MB a plan) is the likely limit —
  an inference I did not measure. A server that allocates about 1 GB/s while a brain runs may also delay
  GM and phone traffic on the Pi, so **keep `MaxParallel = 1` until the Pi shows otherwise**.
- **The planner's console line is a log-noise problem, not a speed problem** (gap 9, now low priority).
- **Break-even for the Pi.** These hold unless the Pi is slower than this machine by at least:
  17× for "stage 1 for five computers ≤ 1 s" (58 ms → 1 s); 19× for the widest stage to fit inside a
  10 s window (my stand-in for how long players take to program — I have no data on that); 16× for
  the Expert re-rank to fit inside the 5 s soft allowance. I have not
  measured the real factor and am not going to guess it. *(Measured afterwards: ×11–12 on the Pi 4 —
  see above.)*

**Re-running the benchmark** (e.g. on a Pi 5, or with the game host stopped for a quiet-machine number):

```bash
cd ~/Mega-Robo-Rally-SignalR
MRR_BENCH=1 ~/.dotnet/dotnet test MRR.Tests/MRR.Tests.csproj -c Release \
  --filter "Category=Benchmark" --logger "console;verbosity=detailed"
```

The report is printed and also saved as `bot-m0-results.txt` beside the test assembly
(`MRR.Tests/bin/Release/net9.0/`). On the Pi 4 the run takes about 1.5 minutes after the build. On
Windows, set `$env:MRR_BENCH = "1"` in PowerShell instead.

### 6.2 M1 as built (2026-11-22) — and where it differs from the plan above

Built on the recommended answers to §9's questions (Ready from the start; five level names; one level
per press; level in the seat name). **Builds; 132/132 unit tests pass on the Pi.**

**First game, 2026-11-22 (deployed to the Pi by the user, who reports it worked; server log checked):**
*Fill with Computers* claimed 5 seats at level 3; four turns were programmed (15:16–15:21), every
computer's program applied in the same second as state 4, none refused, no errors. One computer was
rebooted and confirmed its facing on the next turn. Turns 2–4 succeeding confirms the `PositionValid`
finding below.

| Piece | Where |
|---|---|
| `MRR.Bot` project: `BotProfile` (levels 1–5 and names), `Decision`, `RandomBrain` (Level 1) | `MRR.Bot/` |
| `ClaimUnselectedAsComputers`, `GetComputersToProgram`, `SetRobotProgram` (atomic, one transaction including the status write) | `MRR/DataService.ComputerPlayers.cs` |
| `ProgramComputerPlayers()` — placeholder for every computer on entering state 4 | `MRR/GameController.ComputerPlayers.cs`, called from `NextState()` cases 3 and 15 |
| `fillcomputers` action | `MRR/Program.cs` |
| `Computer` in `RobotData` and on `Player` | `MRR.Contracts/AllDataPayload.cs`, `MRR/Players.cs`, `GetRobotsFromTable`, `GetAllPlayers` |
| GM level selector + *Fill with Computers* | `MRR/wwwroot/gmindex.html` |
| Phone: "(CPU)" after the robot name; no card play or shut-down for a computer seat (GM excepted) | `MRR/wwwroot/js/loadrobots.js` |
| Robot-screen UI and the programming lights-on skip computer seats (gap 8) | `GameController.cs` |
| Planner's console line removed (gap 9) | `MRR.Rules/CreateCommands.cs` |
| Tests: legality over 2,000 random hands, Spam/Again avoidance, facing, determinism, level clamp | `MRR.Tests/BotRandomBrainTests.cs` |

**Differences from the plan, and why:**

- **The placeholder is applied synchronously inside `NextState()`**, right after `SetGameState(4)` and
  before the same loop reaches case 4's gate — not fire-and-forget as gap 1 suggested. It is a few
  DB writes per computer, the gate sees every computer Ready on its first check, and no `NextState()`
  retry is needed (gap 10 does not arise in M1).
- **`SetRobotProgram` does not check `PositionValid < 2`** (§5.8 step 1). That check was wrong:
  `LockAllRobotDirections` sets every robot to 2 at lock-in and nothing resets it to 1, so at the start
  of every turn after the first, robots sit at `PositionValid = 2` (confirmed in the live DB). The
  guard is `GameState == 4` and `RobotStatus.Programming = 1` under the row lock; lock-in's
  `Status = 13` has `Programming = 0`, so that is enough.
- **The status write is inside the transaction** (§5.8 had it after commit), so a later refinement
  (M2) cannot overwrite lock-in's `Status = 13` with 4. `RebuildRobotCardsSummary` gained an optional
  transaction parameter for this.
- **No LED command from `SetRobotProgram`.** Computer robots are skipped by the programming
  lights-on, and `ProgramComputerPlayers` turns their lights off (fire-and-forget) once — which avoids
  both a robot round trip inside the transaction and a race with the lights-on task.
- **Moved to M2, where they are first used:** the human-clock poll, the scheduler skeleton, the
  `NextState()` retry (gap 10) and the `ComputerPlayer` config section. In M1 nothing would read them.
- **The random brain avoids Spam** unless the hand has too few other cards, **and Again in register 1**
  (the planner treats it as Spam there). Still random, still legal.
- **The decision's hand is hand + registers** (`CardLocation IN (1, 2)`), since `SetRobotProgram`
  returns register cards to the hand first.

**Known limits:** a robot-screen UI created for a seat *before* it became a computer keeps its polling
task running (a pre-existing behaviour of `GetAllPlayers(true)`, which drops `Player.ScreenUI`
without cancelling the task); only matters with *Use Robot Screen* on.

**To close M1** — from the §6 "done when" list. **Done:** fill at state 1 → state 2; every computer
Ready right after state 4 (log). **Still open:** 10 consecutive turns (4 so far, all-computer); a
table mixing humans and computers; a phone tap during programming causes no error; *End Selection*
still works; GM *Create Program* / *Reload Position* with computers (§7 manual test 9).

## 7. Testing

**Automated (xUnit in `MRR.Tests`, no database)** — fixtures built like
[ShutDownTests.cs](../MRR.Tests/ShutDownTests.cs). Tests stop the search by **plan count**, not wall
clock, so results are deterministic:

- **Legality** (property test, every level, random boards and hands): exactly `PhaseCount` registers;
  every type is in the hand; no more of a type than the hand holds.
- **Behaviour fixtures** (Levels 3–5): flag 1–3 squares ahead → touches it this turn; a pit in front
  and a safe alternative → avoids the pit; a hand of only turns → still a legal program; a respawned
  robot → picks the facing toward its flag.
- **Regret test:** over a fixed set of fixtures and seeds, regret = (best exhaustive score − chosen
  score). Mean regret must fall from Level 1 to Level 4. This checks the ladder without an executor.
- **Scheduler timing** (callbacks, fake stop signal):
  - `shouldStop` set mid-stage → returns within one plan, keeping the last **finished** stage;
  - breadth-first: with *n* computers, every one finishes stage *s* before any starts stage *s + 1*;
  - a program is only replaced by a later stage's result, never by a partial one;
  - with no human clock, the work cap ends the search and a program is still produced;
  - **Expert's extra time** (an injected `TimeProvider` with a hand-written fake, so no test package is
    needed): after humans-done, no new round starts past the soft deadline; a round in progress may
    finish until the hard deadline; at the hard deadline the unfinished round is discarded and the last
    **full** round's program stays; and it stops early once its re-rank is complete;
  - **`MayLockIn()`**: true at once when every computer is Level 1–4; false for Expert until it is
    finished or the hard deadline has passed; the deadline holds even if the worker never reports back
    (the gate uses its own clock).
- **Purity:** board, cards, draw piles and option cards compare equal before and after `Decide`.
- **Determinism:** the same snapshot, profile, seed and plan-count cap give the same `Decision`.

**Manual (database-bound, like the other `DataService` work — verified by playing,
`PROJECT_STATUS.md` §4.7):**

1. Setup: humans claim seats; the GM picks a level and presses *Fill with Computers*; every open seat is
   claimed as `CPU n <Level>` with a distinct body and a sensible seat number; the game moves to state
   2; the robots show their name and colour. Pressing it again does nothing. With no open seats it does
   nothing. *End Selection* still works.
2. **Timing, Levels 1–4:** start a turn with two Hard computers and one human. The computers are Ready
   almost immediately; the human finishes; lock-in follows at once, with **no wait on a computer**.
   Repeat with the human finishing within a second or two.
2b. **Timing, Expert:** the same with two Expert computers. After the human finishes the table may wait,
   the title shows the "waiting for computer players" note, and lock-in follows when the computers finish
   or at 10 s, whichever is first; the note clears. Kill the brain mid-wait (or disable it): the game
   still locks in at the ceiling with the last applied program.
3. Programming: phones show "CPU"; no programming controls; a computer's registers may change while the
   humans program, and the final ones are legal.
4. **Race:** a human finishes while a refinement is in flight — no error, no half-old/half-new program,
   and the executed turn matches the program shown at lock-in.
5. An all-computer table (or every human shut down) takes the no-human-clock path: the gate holds for
   up to `SoloBudgetMs` (Expert: its grace), then locks in; the placeholder stands if anything fails.
6. The GM sees and confirms the "Run Phase" prompt when a computer is first in priority order.
7. A computer robot dies: "Remove Robot" appears on the GM panel; it reboots and chooses a facing.
8. A phone taps registers while the AI computes: no errors in the log.
9. GM *Create Program* (state 15) and *Reload Position* (16): the run is cancelled and restarted;
   computers are not double-programmed.
10. Disable the brain (config): computers keep the Level 1 placeholder.

## 8. Risks

- **Quality depends on human speed.** Hard keeps improving only while humans are still programming; a
  table that finishes in seconds gets its stage-1/2 answer. That is the direct price of "never take
  longer than the players". Expert has the same dependence, softened by up to 10 s of extra time — but
  that extra time only helps its re-rank rounds, not the earlier stages. Mitigation: stages are ordered
  so most of the value arrives early, and M0 sets stage 1 to finish fast. The arena (M4) should be run
  at several fixed work caps to show how much a level loses when starved.
- **The gate hold is a new way for a turn to stall.** Until now only a human could hold state 4 open.
  Mitigations: the hold ends on an absolute timestamp the gate checks itself (a dead worker cannot
  extend it), it only applies to Expert and the no-human-clock case, the Level 1 placeholder is always
  in place, and the GM's Next State button still works. Players see up to 10 s of "everyone Ready, nothing
  happening" at Expert, softened by the title note.
- **Refine-versus-lock-in race.** The only way a computer could still delay or corrupt a turn is a
  refinement landing around lock-in. `SetRobotProgram` is atomic and refuses once the turn is locked
  (§5.8), but this is the part most worth a deliberate test on the real database.
- **Simulation ≠ reality below Expert.** Levels 2–4 are blind to being pushed, blocked or shot by robots
  that move; Expert only samples them. Opponent modelling is the biggest quality lever left after this
  plan.
- **Pi CPU.** Unmeasured until M0. Mitigations: below-normal priority, `MaxParallel`, level-defined work
  caps, fallback to the placeholder.
- **Visible side effects.** Computers show Ready from t0 and their registers change; a computer robot's
  LEDs go off early (`UpdateProgrammingStatus` turns them off at Ready).
- **The arena is approximate.** There is no headless executor — applying a plan's effects lives in
  `ProcessDbCommand`, whose extraction (`RuleEffects`) is the largest remaining architecture item
  ([PROJECT_STATUS.md](../PROJECT_STATUS.md) §5.1). The arena can rank levels but not prove rules-exact
  outcomes. The game also does not end on a win (§4.4 there), so runs use a fixed turn count.
- **GM workload.** Because the GM confirms every prompt, a table with several computers means more
  clicking (Run Phase up to 5× a turn when a computer is first). Decided; revisit if it is tiresome.
- **Honor-system seats.** Anyone can type a computer's seat number on a phone and program it; the game
  has no seat authentication ([PHONE_LOGIN_DESIGN.md](PHONE_LOGIN_DESIGN.md) was not built). The phone
  page hides the controls for computer seats; the server does not enforce it.
- **Level tuning.** Level names, stage widths, caps and score weights are all guesses until M2–M4; the
  ladder is only as good as the arena that checks it.
- **Rules drift.** The brain follows rule changes automatically only because it plans with the real
  planner. The score weights and the shut-down rule do not.
- **Haywire / options later.** Both change what a hand means; the brain needs a revisit when either lands.

## 9. Not yet checked, and remaining questions

**Not yet checked** — the plan leans on these, so confirm them. (M0 settled the planner-behaviour
assumptions on 2026-11-22 — cardless robots, board and robot immutability, plans with several programmed
robots, King of the Hill prefix plans — see §6.1.)

- Planner **speed on the Raspberry Pi**. Measured on the development machine only (§6.1); the Pi's
  slowdown factor decides whether the break-even points there hold.
- That row locking in `SetRobotProgram` serializes against the lock-in write as §5.8 assumes: I did not
  read how `SqlGateway`/`ExecuteSQL` open connections and transactions, so the real isolation behaviour
  needs a test against MariaDB.
- Whether `BoardElement.HasWall` plus `Block`/pit squares is all the BFS needs (I read
  `CalcMoveDistance`, not the rest of the board model).
- Whether every way a human can finish programming (phone, robot screen, GM Sit Out) reaches
  `NextState()`. The `/api/player` endpoint does ([Program.cs:293](../MRR/Program.cs)); I did not read
  the robot-screen path. If one does not, the gate cannot stamp "humans done" for it and the ~100 ms poll
  covers it instead, so Levels 1–4 would stop searching up to one poll late — not a correctness problem.
- What *Reload Position* (state 16) does to cards: positions are restored, but I did not check whether
  registers are, so a computer's program could be stale after it.
- The standard deck's type mix, for the Expert opponent sampler (the deck templates are seeded in
  `MoveCardsCompleteList`; I did not decode them).
- The GM menu structure and the phone page's seat rendering (`gmindex.html` and `loadrobots.js` were read
  only around the seat-claim code); how `RobotScreenUI` behaves for a seat nobody sits at.
- `UpdatePlayerPriority` assumes priorities `1..N`. A fill claims seats `claimed + 1…N` in order, which
  keeps them contiguous, but I did not test it.
- `SelectSeat`'s uniqueness checks are commented out, so the fill relies on picking free bodies and
  seats itself.

**Questions** (a recommendation is given for each; none blocks M1, which can proceed on the
recommended answers unless you say otherwise):

1. **Is it acceptable that computer seats show Ready from the start of programming** (and their
   registers can change while the humans program)? It is what makes "never later than the humans" a
   guarantee for Levels 1–4, and it keeps a legal program in place while Expert is given extra time. The alternative — hold each computer back and apply once when the humans finish — keeps
   them looking "still thinking" but adds the detect-and-apply time (probably 100–300 ms per computer,
   unmeasured) to every turn. *Recommended: Ready from the start.*
2. **Are five levels and these names right?** Beginner, Easy, Normal, Hard, Expert. *Recommended: yes;
   rename freely, they are only labels in the `BotProfile` presets.*
3. **One level for every seat filled by a press** is what is planned. If you want mixed levels in one
   game, the cheapest route is a per-robot setter on the GM page afterwards (the level already lives
   per robot). *Recommended: leave out of v1.*
4. **Show the level to players** in the seat's name (`CPU 3 Expert`)? *Recommended: yes — it costs
   nothing and humans will want to know.*
5. **Is "Expert = Hard + sampling opponents' likely programs" the right idea of expert?** It is fair
   (public information only) and measurable in the arena, but it is also the most expensive level. If
   you meant something else — for example "knows the other players' habits" — that is a different design.
*Settled, no longer a question:* Expert's extra time is 5 s soft / 10 s hard, Expert only (2026-11-22,
§1). Should Hard ever want a little extra time too, it is one config value per level.
