# Robot Movement Accuracy — Plan

**Status (2026-11-28): Phase 0 partly measured on AIM-04 and AIM-07; speed/turn-rate change and Phase 1 heading hold built, not yet played.** Written from reading the code; no robot was run. Everything under "What could not be verified" has to be settled on the board before the later phases are built.

## The problem

Camera alignment ([grid_alignment_agent.md](../tools/ai_agent/grid_alignment_agent.md)) works some, but it only ever fixes a robot **after** it has stopped, and in a real game it is **never called at all**. Nothing corrects a robot during or between game moves, so small errors pile up over a turn's five registers.

## What the code does today

| Step | Where | What happens |
|---|---|---|
| Card → move | `CreateCommands.TurnRobot` ([MRR.Rules/CreateCommands.cs:877](../MRR.Rules/CreateCommands.cs#L877)) | Moves are **robot-relative** (`ValueB` 1=ahead, 2=right, 3=back, 4=left). The AIM is holonomic, so a robot strafes or reverses without turning. Only `Rotate` commands change its facing. |
| Move | `RobotConnection.MoveAndWaitAsync` ([MRR/Devices/RobotConnection.cs:538](../MRR/Devices/RobotConnection.cs#L538)) | `set_pose` to the *negative* of the expected displacement, then `drive_for` of `squares × 75 mm` (deliberately 1 mm short per square) at 200 mm/s, `final_heading = 0`. Then `WaitForStopAsync` (polls `ws_status` every 50 ms). It then computes `correctionAngle` from `DirToOrigin` and **throws it away**. The odometry check was started and never finished. |
| Heading | same method, line 547 | `preHeading = 0;` with the comment "ignore heading for now, since the robot's odometry is not accurate enough". |
| Turn | `TurnAndWaitAsync` ([:567](../MRR/Devices/RobotConnection.cs#L567)) | `turn_for` of `direction × 90°`, `turn_rate = 200`. The resulting heading is never read back. |
| Camera alignment | `GridAlignmentAgent.AlignAsync` | Called only from `GET /api/robot/align*` ([Program.cs:398](../MRR/Program.cs#L398), [:421](../MRR/Program.cs#L421)). Not from `MoveAndWaitAsync`, `TurnAndWaitAsync` or `CommandProcess`. |

So today each move is dead reckoning from wherever the previous one ended, with the IMU and odometry both ignored.

## Why camera-only "adjust while moving" is the wrong target

- One camera frame costs a **new websocket per frame** ([GetCameraImageAsync](../MRR/Devices/RobotConnection.cs#L810)), a JPEG decode and plus detection. A single square takes about 0.4 s at 200 mm/s. The camera cannot be in a control loop during a drive without a persistent frame stream, and whether `ws_img` streams is still an open question ([todo.md](../install/todo.md) Section "Robot grid alignment").
- When the robot reverses, the front pluses recede out of view. When it strafes they slide sideways. The camera only reliably sees a plus at rest, facing the front line.
- The sensors that *are* readable during motion are the IMU (`heading`, `gyro_rate`) and odometry (`robot_x/y`), both on `ws_status`.

## Approach: three layers, each fixing what the one below can't

| Layer | Sensor | When | Fixes |
|---|---|---|---|
| 1. Heading hold | IMU `heading` | during and after every move and turn | yaw error from turns; the sideways drift a yaw error causes during a drive |
| 2. Absolute-target moves | odometry | every move | undershoot/overshoot, and **stops errors accumulating** across moves |
| 3. Camera anchor | grid pluses | at rest, after a move | odometry drift itself; re-zeroes the pose to the true square center |

The key idea is in layer 2. Right now every move targets "75 mm further than wherever I am". Instead the server should keep each robot's **expected pose** (center of its game square, in mm) and aim every move at the *absolute* center of the destination square. A 2 mm miss on move 1 is then corrected on move 2, not carried forward. The camera (layer 3) re-zeroes odometry when it can see the pluses, so odometry never has to be trusted for long.

## Phases

### Phase 0 — Measure first (no behavior change)

There is no error budget in the repo: no data on how far off a robot ends after a move or a turn, or which kind of error dominates. Building correction without it is guesswork.

1. **Per-command log**: for every move and turn, write one JSONL line with the command, pre/post `robot_x`, `robot_y`, `heading`, `rotation`, and elapsed ms. Server-side only, off the move's path (same pattern as `SaveBattery`).
2. **Scripted test run** per robot, from a standalone console tool (`MRR.Tools.MoveTest`, modeled on `MRR.Tools.FirmwareCompare/AimTestClient.cs`). No existing endpoint reads live robot status or sends raw commands, and the questions below are about firmware, not the game, so the tool runs with the game host stopped and touches no game code. N × each of forward 1, forward 3, back 1, strafe 1, turn ±90, U-turn, then a 16-move loop that should return to the start. Record the hand-measured final error (mm and degrees) next to the log. The experiment table is in [ROBOT_MOVEMENT_HANDOFF.md](ROBOT_MOVEMENT_HANDOFF.md).
3. **Speed sweep**: repeat forward 1 at 200 / 120 / 80 mm/s. Full speed is the likeliest cause of overshoot and yaw wobble, and slowing down is the cheapest fix if the time cost is acceptable.
4. **Close the open questions below** with short experiments.

**Output:** a table of error per move type per robot, and which of yaw, distance or turn error dominates. Phase 1–3 priorities follow from it.

### Phase 1 — Heading hold (layer 1)

**Built 2026-11-28, not yet played:** `RobotConnection.TurnAndWaitAsync` reads the IMU heading before each turn and
sends `turn_for` (nominal turn + gap to the heading the robot should have), via `PlanHeadingTurn` (tests in
`MRR.Tests/HeadingTurnTests.cs`). The reference is taken at the robot's first turn on a connection and re-taken
whenever the gap is over 10° (taken to mean the robot was moved by hand). This replaces items 1–3 below: Phase 0
showed `final_heading` is ignored (#1) and `turn_to` can't refine sub-2° errors (turn_to row), while absolute
`turn_for` stops build-up at no extra time (abs-turn row). Not done: correcting heading after drives (they add
≤1.4° each; the next turn takes it out), and a reference tied to the board instead of the first turn.

1. Keep a **target heading** per robot: its game facing × 90 + a per-robot zero offset captured once, when the robot is placed on its start square.
2. After each `turn_for`, read `heading`. If the error is more than about 2°, send `turn_to` (the correction in [aim-navigation.md](../.claude/agents/aim-navigation.md) §5). One 1° yaw error on a 3-square drive is about 4 mm sideways, and turn error otherwise carries into every later move.
3. Give `drive_for` a real `final_heading` instead of 0. `0` means "don't correct", so a target of exactly 0° needs a workaround; settle it in Phase 0.

**Done when:** over the 16-move loop, heading error at the end is within 3° (a proposed target; confirm it).

### Phase 2 — Absolute-target moves (layer 2)

1. Fix the odometry frame, which `preHeading = 0` currently papers over (see unknown 2).
2. Track expected pose per robot. Set the `drive_for` distance to the *remaining* distance to the destination square's center, not `squares × 75`. Drop the "1 mm short" hack once the stop is accurate.
3. After the stop, if the residual is more than about 3 mm and less than about 30 mm, send one short corrective `drive_for` in the robot's frame. Anything larger is logged and left alone, since it probably means a push or a stall.
4. **Moves over several squares:** try splitting into one-square legs with a status check between them (uses only commands already proven on the board). Keep it only if Phase 0 shows long moves drift more than short ones.

### Phase 3 — Camera anchor in the game path (layer 3)

Only after Phases 1–2, since they decide how much is left for the camera to fix.

1. Call a **bounded** `AlignAsync` (at most 2 iterations) at the end of a move, when the robot is facing a front line it can see. Skip for reverse moves and whenever `!Found`; that is already non-fatal.
2. **Sanity bound:** ignore a correction larger than about 25 mm or 10°. A false detection must never yank a robot across the square.
3. On a successful alignment, `set_pose` to the square center, which resets odometry drift.
4. Measure the added time per move on the Pi 5 *before* switching it on for games. Offer a GM setting to turn it off.
5. Prerequisites already in [todo.md](../install/todo.md): re-measure `TargetPlusHeightNorm` from a hand-centered robot, tune `LateralScaleMm` / `ForwardScaleMm`, and settle `ws_img` streaming. Per-robot camera offsets may be needed.

### Phase 4 — True in-motion control (only if Phases 1–3 leave a gap)

Stream `drive_with_vector` (`x`, `t`, `r`) at 10–20 Hz with a small controller: `r` from heading error, `x` from cross-track error, `t` ramping down to the target. This is the only way to correct *during* a drive, and it is the riskiest: the vector units are undocumented here, `SendCommandAsync` serializes every command behind `_cmdLock` (and the LED effects and flash loop share it), and a dropped link mid-drive could leave the robot driving. Don't start it until Phase 0 shows the remaining error is mid-leg slip that one-square legs don't catch. It would need a watchdog that sends `drive` speed 0 in a `finally`, a hard time limit, and effects paused during the move.

## What could not be verified (answer in Phase 0)

1. **Is `final_heading` absolute, and relative to what?** The docs say "absolute heading, 0 = don't correct". Does the robot actually hold or correct it?
2. **Which way do `robot_x` / `robot_y` point after the robot turns?** They look like a fixed world frame set by `set_pose` and the IMU zero, not the robot's current facing. If so, the `set_pose(-newX, -newY)` target in `MoveAndWaitAsync` is only right while heading is 0, which would explain the "ignore heading" comment. Does `set_pose` take a heading? The documented fields are only `x`, `y`.
3. **How accurate is odometry** over one square and over a 16-move loop (wheel slip on this surface)?
4. **Does a heading zero survive** `imu_calibrate` at connect (done in `ConnectAsync`) if the robot is later moved by hand onto its start square? This decides how the per-robot zero offset is captured.
5. **`turn_rate = 200`** is above the 180 deg/s maximum in [aim-robot-api.md](../.claude/agents/aim-robot-api.md). Is it clamped silently? The same file calls speed "mm/s" while `GridAlignmentAgent` calls its `NudgeSpeed = 50` "percent"; one of them is wrong.
6. **Round-trip times:** `CommandTiming` already records ack and status times for the GM Timing panel. Read them from a real game before choosing any control-loop rate.
7. **Does the robot stop by itself** if commands stop arriving mid-drive? Required before Phase 4.
8. **Housekeeping:** [grid_alignment_agent.md](../tools/ai_agent/grid_alignment_agent.md) and [todo.md](../install/todo.md) say the correction loop "has never moved a robot", but the same doc records measured AIM-04 and AIM-07 correction runs from 2026-11-01. Reconcile before relying on either.

## Phase 0 results

Measured with `MRR.Tools.MoveTest` on the board. Logs in `~/images/movetest/` on the Pi. One robot and one run each
so far: treat these as first readings, not error rates.

| # | Date, robot | Result |
|---|---|---|
| 3 | 2026-11-28, AIM-07 | **`set_pose` does not take a heading.** `heading`, `rotation` and `theta` fields are all acked `complete` and silently ignored (heading stayed 0.0). A per-robot heading zero must be an offset kept by the server. |
| 2 | 2026-11-28, AIM-07 | **`robot_x/y` are a fixed world frame**, not robot-relative: `+y` = the facing at `set_pose`, `+x` = its right. Forward 75 mm after turning 0/90/180/270 read `y +76`, `x +82`, `y −77`, `x −79`. So `MoveAndWaitAsync`'s target math is only right at heading 0 (unknown 2). Same run: odometry read 74–82 mm per 75 mm command (sideways-facing legs 79–82, forward/back legs 74–77, but #7 shows forward legs at speed 200 also reach 82, so this is probably speed-200 scatter, not a direction effect; not yet checked against tape); turns ended within ~1.5° (90→89.6, 180→181.5, 270→271.2); turns shift the robot's center up to ~5 mm in odometry; ≤1.5 mm sideways and ≤1.4° heading change per drive. After 16 commands (out-and-back pairs plus turns) the heading read −0.6° and the robot was back on its start mark by eye. Times: 0.73–0.82 s per square; 1.1 / 1.6 / 2.2 s for 90 / 180 / 270° turns, with ~300 ms ping to the robot. |

| 6 | 2026-11-28, AIM-07, 5 reps (10 turns per rate) | `turn_for` ±90, overshoot past 90° by IMU heading (mean ± sd, range) and time: rate 100 → **1.6 ± 0.6°** (0.9–2.8), 1.51 s; rate 180 → **0.5 ± 0.35°** (0.0–1.0), 1.17 s; rate 200 (the game's) → **0.8 ± 0.4°** (0.2–1.2), 1.17 s. **Turns overshoot, never undershoot** (one of 30 was 0.0°), and the same in both directions, so right turns in a row add up (4 × 0.8° ≈ 3°). **The turn speed is very likely capped at 180:** 180 and 200 take the same time to within 4 ms on average (but see AIM-04 below: 200 still overshoots more than 180). Slower turns are *less* accurate. Odometry moved ≤1.3 mm per turn. A 1-rep run just before gave the same picture. These are IMU readings; whether the IMU's 90° is a true 90° is not checked. |
| 1 | 2026-11-28, AIM-07, `turn_speed` 0 (as the game sends) | **`final_heading` did nothing.** Robot twisted ~22° by hand, then `drive_for` 150 mm: with `final_heading` 0 it ended at 21.7°, with 90 at 22.3° (did not turn), with 23 at 22.4°. Heading held within 0.7° through each drive by itself. Each drive went 155 mm along 22.4° in the world frame, so `drive_for` angle 0 is "the way the robot faces" and odometry stays world-frame (confirms #2). Re-run with `turn_speed` 45, speed 120, robot twisted ~31°: **still no effect**. `final_heading` 0 → 31.4°, 90 → 31.6°, 33 → 31.4°, and no spin during the drive either. So **`final_heading` (and `turn_speed`) appear to be ignored by this firmware**; Phase 1 must correct heading with a separate `turn_to` (or `turn_for`) after turns. Same run, speed 120: 150 mm drives went 151.8 mm, vs 155 at 200 (fits #7). |
| turn_to | 2026-11-28, AIM-07 (battery 73%), 16 × `turn_for` ±90 at rate 180, each followed by `turn_to` the exact target at rate 45 | **`turn_to` does not refine a sub-2° error.** It has a dead band of about 0.5°: at errors ≤0.51° it did nothing (≈85 ms). At 0.52–1.6° it always moved, but by a roughly fixed 1–2.7° whatever the error, usually overshooting to the other side (e.g. +1.1 → −1.4, −0.6 → +1.9). Mean \|error\| was 0.58° after the turns and 0.64° after the corrections; on the turns it actually moved, 0.94° → 1.06°. Each correction that moves costs ~0.3 s. What does work: it took the short way across 0°/360°, and because each correction targets an absolute heading, error did **not** build up over 4 right turns in a row (stayed within ±1.9°). Untested alternatives: `turn_to` at a much lower rate, or making each `turn_for` angle = target − current heading (absolute turns at no extra command). |
| abs-turn | 2026-11-28, AIM-07 (battery 72%), 16 turns at rate 180 | Each `turn_for` angle = target − current IMU heading, to 0.1° (sent 88.2 … 91.5). Fractional angles were acked and the turns tracked them (e.g. −88.2 → −87.7, −87.7 → −87.6), but whether the firmware rounds to whole degrees can't be told at this noise level. Final error vs target: mean \|error\| **0.72°**, max 2.1°, **no build-up** over 4 turns in a row, and no extra command or time. That is about the same as `turn_to` (0.64°, max 1.9°) without its ~0.3 s. Per-turn scatter was larger than in #6 (±1°, some undershoots, mean overshoot only 0.36°), possibly the lower battery or chance. **Takeaway: either method stops heading error accumulating; neither gets a single turn better than about ±1–2°.** Absolute `turn_for` is the cheaper Phase 1 design. |
| 5 | 2026-11-28, AIM-07 | **The heading zero survives being moved by hand, including being lifted.** After `imu_calibrate` (0.0): slid by hand ~90° clockwise → 91.0 (within hand accuracy); lifted, turned back and set down → 0.1, about 35 s after calibrating. Odometry moved ≤0.5 mm through all of it, so **odometry does not follow hand moves**: `set_pose` is still needed after a robot is placed. Consequence for Phase 1: calibrate once at connect as now, and capture each robot's zero offset (IMU heading − game facing × 90) when it is on its start square, e.g. at Verify positions (state 3). |
| 6 | 2026-11-28, AIM-04, 5 reps | rate 100 → **0.2 ± 0.4°** (−0.4 to 0.7), 1.36 s; 180 → **0.35 ± 0.5°** (−0.5 to 0.9), 1.11 s; 200 → **0.7 ± 0.3°** (0.4 to 1.2), 1.10 s. Same both directions; odometry moved ≤0.9 mm per turn. **Across both robots:** rate 200 overshoots ~0.75° on both, with no undershoot. 180 overshoots less on both (0.5 and 0.35°) for the same time, so **the 200 is not simply ignored** (a pure cap at 180 would give the same overshoot); switching the game to 180 looks like a free improvement. Rate 100 differs by robot (AIM-07 1.6°, AIM-04 0.2°), so slow turns are not a reliable fix. |

| 7 | 2026-11-28, AIM-07, 10 drives per speed | `drive_for` 75 mm, alternating forward/back, odometry distance (mean ± sd, range) and time: speed 200 (the game's) → **78.3 ± 2.1 mm** (75.5–82.1), 0.79 s; 120 → **75.5 ± 1.2** (73.5–77.2), 0.94 s; 80 → **75.2 ± 0.8** (73.8–76.4), 1.22 s. **At 200 the robot overshoots ~3 mm with the most scatter; at 120 both mostly go away for +0.15 s per square.** No forward/back difference at any speed. Sideways drift ≤1.7 mm and heading change ≤1.3° per drive at every speed, so speed doesn't affect yaw. Odometry only: if the wheels slip, real overshoot is larger than this, so confirm with tape (`odometry --speed 200` vs `--speed 120`) before changing the game. |
| 7 | 2026-11-28, AIM-04 (battery 100%; AIM-07 was 83%), 10 drives per speed | speed 200 → **76.3 ± 1.9 mm** (73.8–79.7), 0.76 s; 120 → **75.3 ± 1.1** (74.2–76.9), 0.93 s; 80 → **75.2 ± 0.7** (73.8–76.2), 1.21 s. **At 200 the two robots differ** (overshoot 1.3 vs 3.3 mm, scatter about the same); **at 120 and 80 they match** to 0.2 mm. So a slower speed would also make robots behave alike, without per-robot distance constants. AIM-04 drifted less sideways (≤0.6 mm) than AIM-07 (≤1.7 mm); heading change ≤1.0° per drive. |

**Applied 2026-11-28 (owner's decision, before a tape check):** the game's `drive_speed` is now 120 (was 200) and
`turn_rate` 180 (was 200), in `RobotConnection.MoveAsync` and `TurnAsync`. If the tape check later shows the odometry
misleads, revisit both.

## Decisions for the owner (defaults in bold)

- **Time budget per move.** How many extra seconds per register are acceptable for correction? Default: measure in Phase 0, then decide. `CommandProcess` gives up on a command after 10 s.
- **Camera in games:** **on by default with a GM off-switch**, or only on demand?
- **Per-robot calibration** (mm per square, heading zero, camera offset): **start with global constants**; add per-robot values (appsettings by hostname, or a `RobotBases` column through the `mrr-database` agent) only if Phase 0 shows robots differ.
- **Accuracy target.** The numbers in this plan (3°, 3 mm, 25 mm) are proposals, not requirements.

## Files this will touch

`MRR/Devices/RobotConnection.cs` (move/turn methods, the log), `MRR/Players.cs` (expected pose / target heading), `MRR/GridAlignmentAgent.cs` (bounded call, sanity bound), a new `MRR.Tools.MoveTest` project (Phase 0), and tests in `MRR.Tests/`. No database changes in Phases 0–3 unless per-robot calibration is chosen.
