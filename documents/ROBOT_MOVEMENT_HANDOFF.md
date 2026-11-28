# Robot movement — session handoff

**Written 2026-11-28.** The short "where am I, what next" version of
[ROBOT_MOVEMENT_DESIGN.md](ROBOT_MOVEMENT_DESIGN.md), so a session on the Raspberry Pi (next to the robots) can start
in a few minutes. It is draft guidance written by an AI assistant from reading the code. **No robot was run to
write it** — verify against the code and the hardware before building from it.

## Start a session on the Pi with

> Read `documents/ROBOT_MOVEMENT_HANDOFF.md` and `documents/ROBOT_MOVEMENT_DESIGN.md`, then continue from "Next steps".

## Getting these files to the Pi

Both docs are **uncommitted** on the dev machine, and so is the one-line addition to `CLAUDE.md`. The working copy
also has changes that are not part of this plan: `documents/COMPUTER_PLAYER_DESIGN.md`,
`documents/COMPUTER_PLAYER_HANDOFF.md`, and the "30-game arena" text in `CLAUDE.md`'s `COMPUTER_PLAYER_DESIGN` row.
Commit only what you mean to:

```bash
# on the dev machine
git add documents/ROBOT_MOVEMENT_DESIGN.md documents/ROBOT_MOVEMENT_HANDOFF.md CLAUDE.md   # CLAUDE.md carries the unrelated row-63 edit too
git commit -m "Robot movement plan and handoff"
git push

# on the Pi
cd ~/Mega-Robo-Rally-SignalR && git pull
```

No git? Copy just the two `documents/ROBOT_MOVEMENT_*.md` files with `scp`; the `CLAUDE.md` row is only an index entry.

## The problem in four lines

1. Camera alignment only runs from `GET /api/robot/align*`. **The game's move path never calls it.**
2. `RobotConnection.MoveAndWaitAsync` ignores the IMU and odometry: `final_heading = 0`, `preHeading` forced to 0, and
   the `correctionAngle` it computes is discarded. `TurnAndWaitAsync` never reads the heading back.
3. So every move is dead reckoning from wherever the last one ended, and errors pile up across a turn's registers.
4. The camera can't correct *during* a drive (new websocket per frame, about 0.4 s per square). Only the IMU and
   odometry on `ws_status` can.

## The plan in one table

| Phase | What | Status |
|---|---|---|
| 0 | **Measure.** No error data exists. Standalone experiments, then in-game per-command logging | **In progress:** `MRR.Tools.MoveTest` built 2026-11-28, not yet run on a robot |
| 0+ | **Applied 2026-11-28 by owner's decision, before the tape check:** game `drive_speed` 200 → 120 and `turn_rate` 200 → 180 in `RobotConnection.MoveAsync` / `TurnAsync`, from Phase 0 #6 and #7 | built, not deployed or played |
| 1 | IMU heading hold: target heading per robot, `turn_to` fix after each turn, real `final_heading` | not started |
| 2 | Absolute-target moves: aim at the destination square's center, not "75 mm further"; small corrective drive | not started |
| 3 | Bounded camera alignment at the end of a move, with a sanity limit; re-zero odometry when it succeeds | not started |
| 4 | Streaming `drive_with_vector` control during a drive | only if 1–3 leave a gap |

## Code map

Line numbers drift; search by name.

| Where | What |
|---|---|
| `MRR/Devices/RobotConnection.cs` — `MoveAndWaitAsync`, `TurnAndWaitAsync`, `MoveAsync`, `TurnAsync`, `WaitForStopAsync`, `SendCommandAsync` (`_cmdLock`), `GetCameraImageAsync` | Everything the game sends to a robot's movement |
| `MRR/Players.cs` — `SendRobotCommandAsync` | Dispatches `CommandMoveType` 1 (move) and 2 (turn) to the methods above |
| `MRR/CommandProcess.cs` — `ProcessCommand`, `CommandDeadline` (10 s) | Runs commands in order; gives up on one after 10 s |
| `MRR.Rules/CreateCommands.cs` — `TurnRobot` | Makes moves robot-relative (`ValueB` 1 ahead, 2 right, 3 back, 4 left) |
| `MRR/GridAlignmentAgent.cs` — `AlignAsync`, `PlanMove`, `FindPluses` | Camera alignment; thresholds and scales are constants at the top |
| `MRR/Program.cs` — `/api/robot/alignthis/{robotId}`, `/api/timing` | Manual alignment endpoint; ack and status round-trip stats |
| `MRR.Tools.FirmwareCompare/AimTestClient.cs` | Standalone ws_cmd + ws_status client; the model for the Phase 0 tool |
| `tools/ai_agent/grid_alignment_agent.md`, `.claude/agents/aim-navigation.md`, `.claude/agents/aim-robot-api.md` | Camera notes, IMU recipes (draft), wire format |

## On the Pi

From [PROJECT_STATUS.md](../PROJECT_STATUS.md) as of its last update — confirm each on the Pi.

| | |
|---|---|
| Repo | `~/Mega-Robo-Rally-SignalR` |
| .NET | `~/.dotnet/dotnet` (a user install; not on the system path) |
| Game host | port 5000. The `mrr-*` services were **not installed** on 2026-08-22, so run by hand: `~/.dotnet/dotnet run --project MRR/MRR.csproj`. Check with `command -v mrrctl` |
| Stop a hand-started host | `kill $(ss -ltnp \| grep ':5000' \| sed -E 's/.*pid=([0-9]+).*/\1/')`. **Never `pkill -f`**: it kills your own shell and has corrupted the git database before |
| Alignment images | `/home/mrr/images/align/` (`GridAlignment:ImageDirectory` in `appsettings.json`) |
| Build | `~/.dotnet/dotnet build Mega-Robo-Rally-SignalR.sln` (expect 0 errors, 0 warnings) |
| Robot ids | `alignthis/{id}` takes the game's `RobotID`, not the AIM number. `GET /api/robot/info` lists them |

## Ground rules for a session with real robots

- **Never while a game is running.** Keep the table clear, one robot at a time, a hand near the robot, the lowest
  speed that still moves it on the first try.
- Stop a runaway robot with `drive` speed 0, or `POST /api/execution/abort` if the game host is running.
- **Don't change the game's move or turn code before the Phase 0 numbers are in.** The aim is to know what is wrong first.
- Don't `mrrctl pause` mid-turn.
- Treat numbers in the design doc (3°, 3 mm, 25 mm, 2 iterations) as proposals, not measurements.

## Next steps, in order

### 1. Phase 0a — standalone experiments (no change to the game code)

No existing endpoint reads a robot's live status or sends a raw command (checked in `Program.cs`), and the open
questions are about the firmware, not the game. So build a small console tool, **`MRR.Tools.MoveTest`**, modeled on
`MRR.Tools.FirmwareCompare/AimTestClient.cs` (it already does `program_init`, status poll, wait-until-stopped).
Run it with the **game host stopped**, so the server isn't holding the robot's sockets. Whether an AIM accepts two
controllers at once has not been tested, so don't rely on it.

Put the results in a "Phase 0 results" section of the design doc.

**Built 2026-11-28, never run on a robot.** `MRR.Tools.MoveTest` (in the solution, builds with 0 warnings) has one
experiment per row below except #8 (read `/api/timing` instead) and #9 (not built, on purpose), plus `moves`
(Phase 0 step 2) and `loop` (the 16-move loop). Checked so far: it refuses to start while anything listens on port
5000, and it exits cleanly when the robot can't be reached. The AIM hostnames did not resolve from the Pi on
2026-11-28 (no robot was on); use an IP if that persists.

```bash
~/.dotnet/dotnet run --project MRR.Tools.MoveTest -- --help
~/.dotnet/dotnet run --project MRR.Tools.MoveTest -- --robot 4 status         # no movement; try first
~/.dotnet/dotnet run --project MRR.Tools.MoveTest -- --robot 4 moves --measure
```

Logs go to `~/images/movetest/<host>-<experiment>-<time>.jsonl`, one line per command (pose before/after, ack,
elapsed ms, typed measurements). Moves copy the game's (75 mm per square, speed 200, `final_heading` 0, turn rate
200), except that the tool resets odometry with `set_pose 0,0` where it wants a clean displacement rather than the
game's `set_pose(-target)`. It waits 2 s after `imu_calibrate`, which is a guess. Ctrl+C, or a robot still moving
after 10 s, sends `drive` speed 0.

| # | Question | Experiment | Record |
|---|---|---|---|
| 1 | Does `final_heading` hold a heading? Absolute, relative to what? | Calibrate, turn by hand ~20° off, `drive_for` 150 mm with `final_heading` = 0, then 90, then the current heading | Heading before and after each; whether 0 behaves differently |
| 2 | Which way do `robot_x/y` point after a turn? | `set_pose` 0,0; `turn_for` 90; `drive_for` 75 forward; read the status. Repeat after 180 and 270 | Which axis moved, and its sign |
| 3 | Does `set_pose` accept a heading? | Send `set_pose` with a `heading` or `rotation` field, read the status | Accepted, rejected, or ignored |
| 4 | Is odometry accurate? | 16 × 75 mm drives (out and back) with a tape measure | Odometry vs. tape, per drive and total |
| 5 | Does the heading zero survive moving the robot by hand? | `imu_calibrate`, rotate the robot by hand 90°, read `heading` | The reading; decides how a per-robot zero offset is captured |
| 6 | Is `turn_rate = 200` clamped (docs say max 180)? | `turn_for` 90 at 100 / 180 / 200, timed, with heading read after | Duration and final heading for each |
| 7 | Speed vs. accuracy | `drive_for` 75 mm × 10 at 200 / 120 / 80 mm/s | Mean and spread of the end position and heading error |
| 8 | Real round-trip times | After any game: `curl -s http://127.0.0.1:5000/api/timing` | Ack and status avg/max ms |
| 9 | Does the robot stop by itself if commands stop? | **Only before Phase 4.** Low speed, robot held or blocked, kill the client mid-`drive` | Stops or keeps driving |

### 2. Phase 0b — in-game logging

In `MoveAndWaitAsync` and `TurnAndWaitAsync`, write one JSONL line per command (command, pre/post `robot_x`,
`robot_y`, `heading`, `rotation`, elapsed ms) to a file under the image directory's parent. Same pattern as
`SaveBattery`: off the move's path, never throws. Then play a practice game and a hand-measured loop, and fill in the
error table per move type and robot.

### 3. Pick the first fix from the data

Which of yaw, distance or turn error dominates decides the order of Phases 1–3. If it is turn error, start at
Phase 1; if it is distance and accumulation, start at Phase 2.

## Decisions still open (defaults in the design doc)

Time budget per move; camera alignment in games on by default with a GM off-switch; global constants before
per-robot calibration; the accuracy target.

## When results come in

- Update `tools/ai_agent/grid_alignment_agent.md` and `install/todo.md`: both say the correction loop "has never
  moved a robot", yet the doc records AIM-04 and AIM-07 correction runs from 2026-11-01.
- `install/todo.md` has no item for this plan yet; add one pointing at the design doc.
- Correct any command or number in `.claude/agents/aim-navigation.md` and `aim-robot-api.md` that Phase 0 shows
  to be wrong. Their recipes are drafts that have not been run.

## Not done

Only `MRR.Tools.MoveTest` exists (uncommitted). No game-code changes, no in-game logging (Phase 0b), no tests, no
`todo.md` entry, and nothing run on a robot.
