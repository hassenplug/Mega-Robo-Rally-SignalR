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
| **M0 — measure** | **Closed 2026-11-22.** Dev machine committed in `03d2f75`; Pi 4 run done the same afternoon. Results: design §6.1. |
| **M1 — plumbing + Level 1** | **Built and played 2026-11-22**: 5 computers + human(s), 4 turns, no errors. A few checks remain (10 turns, phone tap, End Selection, states 15/16) — design §6.2. |
| **M2 — search, Levels 2–4** | **Written 2026-11-22; 144/144 tests; regret ladder 449 / 107 / 14 / 0. Not yet played.** Design §6.3. |
| **M3 — Spam, reboot facing, shut-down** | **Written 2026-11-22**, not yet played — design §5.4–5.6. |
| **M4 — Expert + arena** | **Written 2026-11-22**, 181/181 tests; arena passed (Beginner −1380, Easy 5040, Normal 7830, Hard 9190, Expert 9710). Not yet played — design §6.4. |

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
computer (so not the default); Expert re-rank ≈ 0.3 s.

**Pi numbers (2026-11-22).** The game server is a **Raspberry Pi 4 Model B (1.8 GHz)**, not a Pi 5. It is
about **11–12× slower** than the dev machine: solo 5-phase plan 8.4 ms; **stage 1 for five computers
0.64 s (target ≤ 1 s met)**; width-60 stage 5.9 s per computer; exhaustive ≈ 93 s (out); Expert re-rank
≈ 3.5 s **per Expert computer** — so five Expert seats would overrun the 10 s ceiling at N = 12, K = 8
(an M4 problem; options listed in design §6.1). Measured with the game server running a live game.

## Next steps, in order

1. ~~Run the benchmark on the Pi~~ — done 2026-11-22, see above. (Re-run command is in design §6.1 if
   the server moves to a Pi 5.)
2. ~~Answer the open questions~~ — the user said (2026-11-22) to proceed on the recommendations.
3. **M1/M2 — play them.** M1's remaining checks are in design §6.2; M2's in §6.3 ("To close M2").
   Deploy and play a few turns with a human and Normal/Hard computers, then one all-computer turn.
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

*Updated 2026-11-22 (M1 session):* M0 Pi results committed (`519c05f`). M1 code written; test suite
132 passing on the Pi (Debug). M1 committed after the first game.
