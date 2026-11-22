# Computer Player — session handoff

**Written 2026-11-22 at the end of the planning + M0 session.** Everything here is also in
[COMPUTER_PLAYER_DESIGN.md](COMPUTER_PLAYER_DESIGN.md); this note is the short "where am I, what next"
version so a new session (or you) can pick up in a few minutes. It is draft guidance written by an AI
assistant from reading the code — verify against the code before building from it.

## Start a new session with

> Read `documents/COMPUTER_PLAYER_HANDOFF.md` and `documents/COMPUTER_PLAYER_DESIGN.md`, then continue
> from "Next steps".

## What the feature is

A **computer player**: a robot seat with `Robots.Computer > 0` (the column already exists; it holds the
level, 1–5) that programs its own registers, facing and shut-down. Purpose: **fill empty seats**. A GM
button turns every still-unselected seat into a computer at a chosen level (Beginner → Expert). The GM
keeps confirming the usual prompts. New project `MRR.Bot` (references `MRR.Contracts` and `MRR.Rules`
only) holds the brain, which scores candidate programs by running the real turn planner.

**Timing rule:** Levels 1–4 must never take longer than the human players; **Expert may take up to 5 s
(normal) / 10 s (hard ceiling) after the humans finish.** Design: every computer gets a legal placeholder
program the moment programming starts, a background search keeps improving it atomically, and the
state-4 gate waits only for Expert (design §5.7).

## Where things stand

| | Status |
|---|---|
| Plan | Written and agreed in a back-and-forth with the user: [COMPUTER_PLAYER_DESIGN.md](COMPUTER_PLAYER_DESIGN.md). Committed (`aa82572`). |
| **M0 — measure** | **Done on the development machine; the Raspberry Pi run is still to do.** Committed (`03d2f75`). Results: design §6.1. |
| M1 and later | **Not started. No product code exists** — only test code. |

Decisions already made by the user (design §1): fill empty seats; one GM button; GM sees prompts;
selectable level up to Expert; `MRR.Bot` project; unplayed Spam staying in hand is intended;
Expert's 5 s soft / 10 s hard extra time.

## What M0 added (all under `MRR.Tests/`)

| File | What it is |
|---|---|
| `BotFixtures.cs` | A synthetic busy 12×12 board, 6 robots, a 9-card hand, plan/score/signature helpers. Reusable for M2's tests. |
| `BotM0AssumptionTests.cs` | 14 tests, run with the normal suite. They confirm what the plan assumed about the planner (deterministic, board untouched, cardless robots inert but still shooting, prefix plans exact, thread-safe over a shared board, King of the Hill harmless) and pin two side effects a bot must copy around (card `Priority` is overwritten, `DrawPiles` is consumed). |
| `BotM0Benchmark.cs` | The timings. **Opt-in** (`MRR_BENCH=1`), prints a report and saves `bot-m0-results.txt`. |

Headline numbers (dev machine, Release): one solo 5-phase plan **0.71 ms**; first search pass for five
computers **58 ms**; widest planned pass (width 60) **0.53 s** per computer; exhaustive search ≈ 9 s per
computer (so not the default); Expert re-rank ≈ 0.3 s. **No Pi numbers yet.**

## Next steps, in order

1. **Run the benchmark on the Pi** (stop the game host first so its CPU doesn't inflate the numbers):

   ```bash
   cd ~/Mega-Robo-Rally-SignalR
   MRR_BENCH=1 ~/.dotnet/dotnet test MRR.Tests/MRR.Tests.csproj -c Release \
     --filter "Category=Benchmark" --logger "console;verbosity=detailed"
   ```

   The report is saved as `MRR.Tests/bin/Release/net9.0/bot-m0-results.txt`. Paste it into design §6.1,
   add a Pi column, re-check the break-even factors, and mark M0 closed. (Windows PowerShell:
   `$env:MRR_BENCH = "1"` then the same `dotnet test` line.)
2. **Answer the open questions** (below), or tell the next session to proceed on the recommendations.
3. **M1 — plumbing with the Level 1 (random) brain and the timing guarantee.** Scope and "done when" are
   in design §6. In short: gaps 1, 2, 5, 7, 8, 9, 10, 11, 12 (design §3); the `MRR.Bot` project;
   `ClaimUnselectedAsComputers` + a `fillcomputers` case in `/api/state/{newstate}/{parameter1}`; the GM
   level selector and *Fill with Computers* button beside *End Selection*; `Computer` added to
   `RobotData`; the atomic `SetRobotProgram`; the human-clock poll; the placeholder at the start of state 4.
4. M2 (Levels 2–4 brain, the `MayLockIn()` gate hold), M3 (reboot facing, Spam sampling, shut-down
   rule), M4 (Expert and the arena) — design §6.

## Open questions (none blocks M1; recommendations are in design §9)

1. **Computers show Ready from the start of programming, and their registers can change while humans
   program** — acceptable? It is what makes "never later than the humans" a guarantee. *Recommended: yes.*
   M1 is built around it.
2. Five level names (Beginner, Easy, Normal, Hard, Expert) — right?
3. One level for every seat filled by a single button press (no mixed levels in v1) — OK?
4. Show the level in the seat name (`CPU 3 Expert`)?
5. Is "Expert = Hard + sampling opponents' likely programs" what you mean by expert?

## Things worth knowing before you touch code

- **Do not call `BuildTurnRequest()` while players are programming.** It reloads the shared `GameCards`
  and can race with phone taps. The plan uses a new read-only `BuildBotSnapshot` instead (design gap 5).
- **The planner mutates its inputs** (card priorities, draw piles). A bot must copy them for every
  simulation; the tests pin this (design gap 6).
- **`NextState()` is single-flight**: a call made while one is running is ignored, so the AI must retry
  after applying a program (design gap 10).
- `PlayerState`'s copy constructor does not copy `PlayerStatus`; the planner already works around it.
- Nothing in the existing REST contract changes; the new endpoint is additive (`CLAUDE.md` rule).
- The database is tables only — no stored procedures. `SetRobotProgram` is C#, not SQL logic in the DB.
- Not yet verified (design §9): how `SqlGateway`/`ExecuteSQL` handle connections and transactions (the
  row-locking in `SetRobotProgram` assumes MariaDB serializes against the lock-in write); what *Reload
  Position* does to registers; whether the robot-screen programming path reaches `NextState()`; the
  deck's card-type mix (needed for Expert's opponent sampler).
- `dotnet test` prints NU1902/NU1903 vulnerability warnings for `SixLabors.ImageSharp` 3.1.12 and
  `Microsoft.AspNetCore.SignalR.Core` 1.2.0. They are unrelated to this work and were left alone.

## Repository state at handoff

Working tree clean; the plan and M0 are committed on `main`. Test suite: 122 passing (Debug). No product
code has been written.
