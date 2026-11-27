# Game Results & Player Instructions — Design

**Status: step 1 built 2026-11-27, not yet tried in a game** (`/api/results`, GM Results view, phone
ranking; see §6). Phone instruction cards built too (§3); the GM briefing and the rest of the timing are not.

Two screens around a game:

1. **Instructions** while players claim seats, pick robots and pick starting positions (GameState 1).
2. **Results** after the GM presses **End Game** (GameState 25): a full stats view on the GM screen
   and a simple ranking on the player phones.

Both are mostly front-end work. Most stats can be counted from `CommandList`, which already
holds every command of the current game (§4). The only new recording is **timing**: when
commands ran, when the game started and ended, and when each player locked in.

*Revised 2026-11-27: the first draft proposed a new `GameEvents` table. Dropped after confirming
that `CommandList` keeps the whole game.*

---

## 1. Decisions made

| # | Decision | Source |
|---|---|---|
| D1 | Reaching the last flag does **not** end the game. The robot's flag count resets to 0 and it keeps playing, which also allows "everyone touches the last flag" games. This is the current behaviour (`CreateCommands.cs` ~1576-1586) and stays. | User, 2026-11-26 |
| D2 | The game ends **only** when the GM presses End Game, at whatever point the group stops. There is no automatic end. | User, 2026-11-27 |
| D3 | Full results on the GM screen; a simplified ranking on the player screens. | User, 2026-11-27 |
| D4 | Results only need to last until the next game starts. No history across games, no leaderboard. | User, 2026-11-27 |
| D5 | Players get instructions on their screens during seat, robot and start-position selection. | User, 2026-11-27 |
| D6 | A turn cannot be re-planned once submitted, so executed `CommandList` rows are never replaced. | User, 2026-11-27 |
| D7 | `HistoryRobotTurns` is cleared when a game starts, and records when each player submitted their program. **Done 2026-11-27** (§4.2). | User, 2026-11-27 |

Consequences:
- The commented-out `SquareAction.GameWinner` command stays unused. A finish is the executed
  `Flag` command whose value reaches `TotalFlags` (§4), and the existing "Game Winner:" message
  to the player stays.
- Because of D4, results can be read from tables that `GameController.StartGame()` already
  clears (`GameController.cs` ~269-276), chiefly `CommandList`.

---

## 2. When each screen shows

| GameState | Phones (`index.html`) | GM (`gmindex.html`) |
|---|---|---|
| 1 (seat selection) | Instructions + the existing seat/robot/start picker | Existing `robotselect` view, plus a GM briefing checklist (§3.3) |
| 2–16 (playing) | Unchanged | Unchanged |
| 25, **after a game** | Ranking (§5.2) | New `results` view (§5.1), opened automatically when End Game is pressed |
| 25, no game played | Unchanged | Unchanged (`gamecontrol` view) |

"After a game" means the previous game reached at least one turn and its `Robots` rows still
exist. GameState 25 is also the state after a server start with no game running, so the screen
must check this rather than just `gamestate === 25`. Because the data lives in the database, the
results come back after a Pi restart until the next game starts. That seems useful rather than a
problem.

`EndGame()` (`GameController.cs` ~742) sets state 25 and disconnects the robots. It does not
touch `Robots`, so the final positions and flags are still there to read.

---

## 3. Start-of-game instructions

**Phone cards built 2026-11-27** (`INSTRUCTION_CARDS` in `js/loadrobots.js`; 7 cards, wording below
adjusted to the rules as implemented: reboot gives 2 Spam, shut down discards all Spam). Text only, no
pictures yet. The GM briefing (§3.3) is not built.

### 3.1 Where they go

`renderSetupStatus()` in `js/loadrobots.js` (~834) draws the GameState 1 phone screen in two forms:
- **Waiting** ("Waiting for Seat N to finish choosing…"): most players see this most of the time.
  This is the main place for instructions.
- **Your turn**: the robot and start-position buttons. Keep instructions short here, so they don't
  push the buttons off the screen. A one-line hint is enough (for example, "Start squares are
  numbered on the board").

Proposal: an instructions panel below the waiting message, made of short cards the player can page
through (Next / Back), and collapsed to a "How to play" button during their own turn to pick.

### 3.2 Content (draft, from `Game_Instructions.md`)

1. **Goal.** Touch the flags in order, 1 to N (N comes from `datapacket.totalflags`). When you
   reach the last flag, you start again at flag 1 and keep playing. The game ends when the GM
   calls it.
2. **Each turn.** Pick 5 cards in the order you want them to run. When every robot is
   programmed, the robots run one card each, taking turns in the order shown on the screen.
   That order rotates so everyone gets to go first.
3. **Your phone.** Hold the phone lined up with the board; arrows are from your robot's point of
   view. If the screen goes to sleep, pull down to refresh.
4. **The board.** Belts, double belts, gears and pits. A picture and one line for each.
5. **Lasers & Spam.** Getting shot gives you Spam cards, which play a random card in their place.
6. **Reboot.** Falling in a pit or off the board puts your robot back on the reboot square.
7. **Shut down.** You can shut down for a turn to clear damage (behaviour as in
   `Archive/SHUTDOWN_DESIGN.md`).

Wording should be checked against the rules actually implemented (Renegade, `CARD_KNOWLEDGE_BASE.md`)
before release.

**Option, not required:** show only the board elements on the current board. This needs the
board's square types in the payload or a small endpoint. Leave it for later unless it's cheap.

### 3.3 GM briefing

`Game_Instructions.md` "Starting the Game with Players" is already a checklist for the GM's
spoken introduction. Showing it as a tick-list beside the `robotselect` view keeps the GM and
the phone cards in step. Ticks are kept in the browser only (`localStorage`) and nothing is saved
on the server.

### 3.4 Not covered

The ESP32 player station's screen is too small for this text (`ESP32_PLAYER_STATION_DESIGN.md`).
It could show "See the GM for rules" or nothing. This is an open question (§8).

---

## 4. Recording stats

### 4.1 Counts come from `CommandList`

`CommandList` holds every command of the current game. Its rows are:
- written per turn by `PersistCommands()` (`DataService.Commands.cs` ~67), which replaces only
  that turn's rows when the turn is re-planned;
- cleared only by `StartGame()` (`GameController.cs` ~274);
- marked `StatusID = 6` when executed, by the one `UPDATE` at the end of
  `ProcessDbCommand()` (~362).

Only the master writes it, never the planner, so computer players' trial runs in `MRR.Bot` don't
appear in it. **Count only `StatusID = 6`**, so commands planned but never run (End Game
mid-turn) are left out. Each row has `Turn`, `Phase`, `CommandSequence`, `RobotID`,
`CommandTypeID` and `Parameter`.

| Stat | Query on executed `CommandList` rows |
|---|---|
| Turns played | `MAX(Turn)` |
| Flags touched | `Flag` (16) with `Parameter > 0` |
| Laps (finishes) | `Flag` with `Parameter >= TotalFlags` (the reset that follows writes `Flag 0`, which is not counted) |
| Finish order | those rows ordered by `Turn, Phase, CommandSequence` |
| Deaths | `Dead` (20) |
| Spam received | `DealSpamCard` (73) |
| Hits on other robots | `SetPlayerStatus` (63) with `Parameter = 14` (`LaserFired`), written for the robot causing damage in `AddDamage`: lasers, ramming gear and exploding options, not only lasers |
| Pushes received / given | `RobotPush` (21); `RobotID` is the pushed robot, `Parameter` the pusher |
| Cards played | `Card` (42) |
| Shut-down turns, energy, options | `SetShutDownMode` (82), `SetEnergy` (69), `Option` (17) / `DealOptionCard` (65) |

Checked while building (2026-11-27):
- `Flag` commands with a value > 0 come only from `AddFlag` (normal flags, King of the Hill and
  Last Man); the resets write `Flag 0`. On King of the Hill boards the value is a running total,
  so "laps" there means reaching `TotalFlags` points, not a lap.
- `SetPlayerStatus 14` is sent for any damage one robot causes another (see the table).
- **Aborted turns can over-count.** `POST /api/execution/abort` (`CommandProcess.AbandonTurn`) sets
  `StatusID = 6` on commands that never ran. There is no button for it, and re-running the turn
  replaces those rows, so this is rare. `ExecutedAt` (§4.2), set only when a command really runs,
  would make it exact.
- Cause of death (pit vs. damage) is not on the `Dead` command. Not needed for version 1.

### 4.2 Timing: the only new recording

`CommandList` has no time column, and some times aren't commands at all.

| Need | Proposal |
|---|---|
| When each command ran (finish time, run time per turn) | Add `ExecutedAt datetime(3) NULL` to `CommandList`, set in the same `UPDATE` that writes `StatusID = 6` |
| Game start / end | Two new `CurrentGameData` keys, set when GameState goes 1 → 2 and in `EndGame()` |
| When each turn's cards were dealt | One `CurrentGameData` key, set at GameState 2, copied into `HistoryRobotTurns` by `SaveToHistory()` as `DealtAt` |
| When each player submitted | **Done 2026-11-27.** `Robots.SubmittedAt`, set by `RebuildRobotCardsSummary()` whenever it writes `Status = 4` (phone, robot screen or computer player; changing a card in a full program sets it again; going back to programming clears it). `SaveToHistory()` copies it into `HistoryRobotTurns.SavedAt` (which used to hold the time the row was written; NULL now if the robot never submitted). Existing databases: `install/add_submitted_at.sql` |

Programming time per player per turn = `SavedAt − DealtAt` from `HistoryRobotTurns`.

**Done 2026-11-27:** `StartGame()` now clears `HistoryRobotTurns`, which has no `GameID`, so it
only holds the current or most recent game.

Schema changes go into `install/MRRDatabase.sql` (the `mrr-database` agent keeps that file). No
database-side logic, in line with CLAUDE.md.

---

## 5. The screens

### 5.1 GM results (`gmindex.html`, new `results` view)

**Game summary**
- Board name, players (computer players marked with their level)
- Total game time (game start → End Game, §4.2), total turns
- Average turn time, split into programming (`DealtAt` → last `SavedAt`) and running (first →
  last `ExecutedAt` in the turn)
- Finish order: who, turn, game time

**Per-player table**, one row per robot, in ranking order. "Executed" means `CommandList` rows
with `StatusID = 6` (§4.1).

| Column | From |
|---|---|
| Place | §5.3 |
| Robot / player | `Robots` |
| Flags touched (total) | executed `Flag` with `Parameter > 0` |
| First finish (turn / time) | first such finish row; time from `ExecutedAt` |
| Avg / slowest programming time | `HistoryRobotTurns.SavedAt − DealtAt` |
| Times last to lock in | per turn, the robot with the latest `SavedAt` |
| Deaths | executed `Dead` |
| Spam received | executed `DealSpamCard` |
| Hits | executed `SetPlayerStatus 14` |
| Pushes given / received | executed `RobotPush` (`Parameter` / `RobotID`) |
| Turns shut down | executed `SetShutDownMode` (check the value used to switch it on) |

Computer players' programming time is shown but left out of "slowest" and "last to lock in"
(their timing is controlled by `BotTiming`).

**Later, if wanted:** a chart of each robot's flag progress against turns (from the `Flag` rows);
highlights such as "most deaths" or "fastest programmer".

### 5.2 Player ranking (`index.html`)

One short list for everyone: place and robot colour + name. The player's own row
is highlighted (from the `mrr_seat` cookie). Optionally one personal line underneath: "You:
2 deaths, 5 Spam, avg 34 s to program."

### 5.3 Ranking rule

1. Most flags touched in all. On a normal board this is the same as laps then current flag,
   because a finish resets the robot to flag 0 and it carries on counting.
2. Then whoever touched their last flag first: their last executed `Flag` row, ordered by `Turn,
   Phase, CommandSequence`. This needs no timestamp.

Decided 2026-11-27. Laps and current flag are no longer shown on either screen.

### 5.4 Endpoint

`GET /api/results` returns the summary and per-player rows as JSON, worked out on request from
`CommandList`, `HistoryRobotTurns`, `CurrentGameData` and `Robots`. A separate endpoint, not part
of the SignalR `AllDataUpdate` broadcast, so the broadcast doesn't grow. Both pages fetch it when
they see GameState 25 and the payload says a game was played. It returns an empty result when
there is nothing to show. It is a new endpoint, so no existing API contract changes.

---

## 6. Suggested build order

1. **Built 2026-11-27.** `/api/results` from `CommandList` alone: turns, laps, flags, place, deaths,
   Spam, hits, pushes, plus submit timing from `HistoryRobotTurns.SavedAt` (average turn time, each
   human's average time behind the first to submit, times last). GM `results` view and phone
   ranking. No schema change. Code: `MRR/DataService.Results.cs`, `Program.cs` (`/api/results`),
   `gmindex.html` (Results view), `js/loadrobots.js` + `index.html` (phone ranking).
2. Phone instructions panel in GameState 1 (§3), independent of 1.
3. Rest of the timing (§4.2): `CommandList.ExecutedAt`, game start/end keys, `DealtAt` in
   `HistoryRobotTurns` (submit time in `SavedAt` and the clearing are already done). Then the time columns.
4. GM briefing checklist; optional flag-progress chart.

Success check for step 1: in a simulated game (Use Robots off) where one robot finishes twice and
another once before End Game, the GM and phone screens show the right laps and places, and
starting a new game clears them.

---

## 7. Related work

- `ROBOT_EFFECTS_DESIGN.md` §7 waits on how a win is handled. Its win effect can play on the
  executed `Flag` command that reaches `TotalFlags`, which settles that question.
- `PROJECT_STATUS.md` §4.4 ("Winning does not end the game") should be updated to say this is
  now intended (D1/D2), once this design is agreed.

---

## 8. Open questions

1. ~~Ranking rule~~ decided 2026-11-27: total flags touched (§5.3).
2. **ESP32 player stations:** show the ranking (it is small) or nothing?
3. **Instructions:** are the cards in §3.2 the right list and order? Should they be shown again
   during the game (a "Rules" button)?
4. **Does End Game in the middle of a phase count that turn?** Proposal: count a turn once any of
   its commands has run, and count only executed commands.
5. **Cause of death** (pit vs. damage): wanted in version 1?
