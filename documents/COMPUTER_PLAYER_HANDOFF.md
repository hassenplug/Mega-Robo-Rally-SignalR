# Computer Player — session handoff

**Updated 2026-11-28** (first written 2026-11-22). Everything here is also in
[COMPUTER_PLAYER_DESIGN.md](COMPUTER_PLAYER_DESIGN.md); this note is the short "where am I, what next"
version so a new session (or you) can pick up in a few minutes. It is draft guidance written by an AI
assistant from reading the code — verify against the code before building from it.

## Start a new session with

> Read `documents/COMPUTER_PLAYER_HANDOFF.md` and `documents/COMPUTER_PLAYER_DESIGN.md`, then continue
> from "Next steps".

## What the feature is

A **computer player**: a robot seat with `Robots.Computer > 0` (the existing column; it holds the level,
1–5 = Beginner, Easy, Normal, Hard, Expert) that programs its own registers, facing and shut-down.
Purpose: **fill empty seats**. At seat selection the GM picks **how many** computers and **which level**
and presses *Start*: that many of the unpicked robots become computers (lowest RobotID first), the rest of
the unpicked robots are removed, and the game starts (0 computers = the old *End Selection*). The GM keeps
confirming the usual prompts. The brain is the DB-free `MRR.Bot` project; it scores candidate programs by
running the real turn planner.

**Timing rule:** Levels 1–4 never take longer than the human players; **Expert may take up to 5 s
(normal) / 10 s (hard ceiling) after the humans finish.** Every computer gets a legal placeholder program
the moment programming starts; a background search keeps improving it atomically; the state-4 gate waits
only for Expert, or when no human is programming (design §5.7).

## Where things stand (2026-11-28)

| | Status |
|---|---|
| Plan | Agreed with the user 2026-11-22; the five open questions were answered "proceed on the recommendations". |
| **M0 — measure** | **Closed 2026-11-22.** Pi 4 ≈ 11–12× slower than the dev machine; stage-1 target met. Design §6.1. |
| **M1 — plumbing + Level 1** | **Built and played 2026-11-22** (5 computers + human, 4 turns, no errors). Still to check: 10 consecutive turns, a phone tap during programming, GM Create Program / Reload Position with computers. Design §6.2. |
| **M2 — search, Levels 2–4** | **Written; unit-tested; never played.** Regret ladder 449 / 107 / 14 / 0. Design §6.3. |
| **M3 — Spam sampling, reboot facing, shut-down** | **Written; unit-tested; never played.** Documented inline, design §5.4–5.6. |
| **M4 — Expert + arena** | **Written; unit-tested; never played.** Arena passed on the Pi (design §6.4); a larger arena was run 2026-11-28 (§6.5). |
| Computer-count drop-down (2026-11-27) | **Built; never tried** on the Pi. Design §6.2 ("Changed 2026-11-27"). |

**Tests:** `dotnet test Mega-Robo-Rally-SignalR.sln` — **181 passing**, build clean (no compiler warnings),
checked on the dev machine 2026-11-28. Opt-in extras: `MRR_BENCH=1` (planner timings), `MRR_ARENA=1`
(arena; also `MRR_ARENA_GAMES`, `MRR_ARENA_TURNS`).

**The one piece of work still open is playing it.** Nothing known is broken; nothing past M1 has run
against the real database, robots or phones.

## Code map

| Where | What |
|---|---|
| `MRR.Bot/` | The brain: `BotProfile` (levels), `RandomBrain` (Level 1), `BeamSearch` + `BotScheduler` (stages, breadth-first over computers), `ProgramEvaluator` (one real planner call per candidate), `FlagDistances` (BFS), `ShutDownAdvisor`, `OpponentSampler` (Expert), `BotTiming` (all deadline rules, on an injectable clock). References only `MRR.Contracts` and `MRR.Rules`. |
| `MRR/DataService.ComputerPlayers.cs` | `ClaimUnselectedAsComputers`, `GetComputersToProgram`, `BuildBotSnapshot` (read-only), `HumansStillProgramming`, `TitleNote`, and the atomic `SetRobotProgram`. |
| `MRR/GameController.ComputerPlayers.cs` | `ProgramComputerPlayers` (placeholders at state-4 entry), the background run, the human-clock poll, `MayLockIn()` (the gate's extra condition), `SetComputerLevel` (GM Shut Down view). Called from `NextState()` cases 3, 4 and 15. |
| `MRR/Program.cs` | `fillcomputers` case in `/api/state/{newstate}/{parameter1}?count=N`. |
| `MRR/wwwroot/gmindex.html`, `js/loadrobots.js` | Count + level selectors and *Start*; "(CPU)" label; no card play for a computer seat (GM excepted). |
| `MRR.Tests/Bot*.cs` | Tests, the M0 benchmark and the arena. |

Appsettings section `ComputerPlayer` (all optional): `SearchEnabled` (true; false = every computer keeps
its Level 1 program), `SoloBudgetMs` 5000, `GraceSoftMs` 5000, `GraceHardMs` 10000, `StopPollMs` 100.

## Next steps, in order

### 1. Play-test session on the Pi

Deploy as usual (`mrrctl deploy game` if the services are installed; otherwise rebuild and restart by hand —
`PROJECT_STATUS.md` §2). Watch the log for the lines in the second column. **Stop and tell the assistant if
a turn stalls, an error appears, or a phone feels slow while computers think.**

| # | Do this | Expect |
|---|---|---|
| 1 | **Setup, count 2:** one human picks a robot; choose Computers **2**, Level **Hard**, *Start* | `Fill with Computers: 2 of 2 computer(s) at level 4; N unpicked robot(s) removed.` Seats named `CPU n Hard`, distinct bodies, lowest RobotIDs; game goes to state 2. |
| 2 | **Hard timing, 2 Hard + 1 human, 3+ turns** (once finish *fast*, once slowly) | Each turn: `[CPU n] Level 4 program …` at state 4, then `Hard stage s program … at X ms` while you program, then `search stopped … humans done at B ms` and `lock-in allowed at A ms (humans done at B ms)` with **A − B ≈ 0 (≤ ~250 ms)**. No wait on a computer. Phones show "(CPU)". |
| 3 | **Phone/touch while thinking** | Taps feel normal; no errors in the log. Watch especially with 4–5 computers. |
| 4 | **Expert, 2 Expert + 1 human** | After you finish: the title shows "- waiting for computer players", `lock-in held …`, then `releasing lock-in at X ms` ≤ your finish + 10 s (usually sooner), and the note clears. |
| 5 | **All-computer table** (choose all seats computers, or sit the human out) | Title note; `lock-in allowed at A ms (no human clock)` with A ≤ about 5 s (Expert: up to 10 s). |
| 6 | **A computer dies** (pit or damage) | GM confirms "Remove Robot"; next turn the log shows a `facing` on its program and it plays normally. |
| 7 | **Shut-down and Spam** over ~10 turns | `[CPU n] shutting down: …` only when badly clogged; watch whether any computer hoards Spam in its hand (design §5.4: playing a Spam isn't scored). |
| 8 | **Count 0 and count "all"** at another game start | 0 behaves as the old *End Selection* (needs ≥ 1 human picked; with none, nothing happens); "all" fills every open seat. |
| 9 | **GM Create Program (15) / Reload Position (16)** mid-programming | Run is cancelled and restarted; computers are not double-programmed. **Look at computers' status after Reload Position** — it rebuilds `Robots` from history (design §9). |
| 10 | **Ten consecutive turns** with computers, no stall | Closes M1. |

Useful: `mrrctl logs -f | grep -E "CPU|Computer players|Fill with"` (or the server terminal).

### 2. After playing

Record what happened in design §6.2–6.4 ("To close M1 / M2"), mark each milestone closed or list the
failures, and commit. Then the known follow-ups (none urgent):

- **Spam cleanup is not scored** — if computers hoard Spam, add a bonus per Spam played (design §5.4).
- **Hard rarely reaches its widest stages on the Pi** with five computers (design §6.1). The regret test
  said Hard matched the best in every case; check in play whether width 10 is already enough.
- **Expert's lead over Hard** — see design §6.5 for the larger arena; more boards would firm it further.
- **Pi 5?** If the server moves, re-run the benchmark (command in design §6.1) before tuning.

## Open questions

All five original questions were answered "proceed on the recommendations" (2026-11-22). Still open:

1. **Shut-down and the draw deck:** shutting down clears damage cards in hand, registers and discard
   pile, but **not** ones already shuffled into the draw deck (`ApplyShutDownAtLockIn`). Intended? The
   computers' shut-down rule assumes what the code does today (design §5.6).
2. **What does *Reload Position* do to statuses?** It restores `Robots.Status` as saved at lock-in
   (13, "Program Locked"?). Not computer-specific, but it decides whether computers are re-programmed
   afterwards. Look at it in play (test 9 above).

## Things worth knowing before you touch code

- **Do not call `BuildTurnRequest()` while players are programming.** It reloads the shared `GameCards`
  and can race with phone taps; computers use the read-only `BuildBotSnapshot`.
- **The planner mutates its inputs** (card priorities, draw piles); the brain copies them (tests pin this).
- **`NextState()` is single-flight**; the run retries after a hold (`AdvanceFromProgramming`).
- `PlayerState`'s copy constructor does not copy `PlayerStatus`; the planner already works around it.
- **Robot-screen programming never calls `NextState()`** (`RobotScreenUI` calls `UpdateCardPlayed`
  directly), so for that path only the 100 ms poll notices "humans done". Not a correctness problem.
- `SetRobotProgram` has one small known looseness: it updates the shared in-memory `GameCards` *after* the
  commit, so a refinement landing at the exact instant of lock-in's reload could throw "collection was
  modified" — it is caught, the database write stands, and that robot just stops being refined. Harmless.
- A refinement carrying a facing can land between the gate's direction lock and `SetGameState(5)`. It is
  still applied with its own program, so the pair stays consistent; it just resets that robot's
  `PositionValid` from 2 to 1. Harmless.
- The server is a **Raspberry Pi 4**, not a Pi 5 as `CLAUDE.md`'s hardware table says.
- The database is tables only — no stored procedures; `SetRobotProgram` is C#, not DB-side logic.
- `dotnet test` prints NU1902/NU1903 vulnerability warnings for `SixLabors.ImageSharp` 3.1.12 and
  `Microsoft.AspNetCore.SignalR.Core` 1.2.0. Unrelated; left alone.

## Repository state at handoff

Everything through the computer-count drop-down and the GM-screen clean-ups is committed on `main`
(last computer-player commit `83dbceb`, 2026-11-27). **Uncommitted, 2026-11-28:** this note, design
doc edits (§6.5, §7, §9), and one log line in `GameController.ComputerPlayers.cs` — `MayLockIn()` now
logs `lock-in allowed at A ms (humans done at B ms)` so the timing checks above can be read straight from
the log.
