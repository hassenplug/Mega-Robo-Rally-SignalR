# Robot Movement Accuracy — Plan

**Status: plan only, nothing implemented (2026-11-28).** Written from reading the code; no robot was run. Everything under "What could not be verified" has to be settled on the board before the later phases are built.

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

## Decisions for the owner (defaults in bold)

- **Time budget per move.** How many extra seconds per register are acceptable for correction? Default: measure in Phase 0, then decide. `CommandProcess` gives up on a command after 10 s.
- **Camera in games:** **on by default with a GM off-switch**, or only on demand?
- **Per-robot calibration** (mm per square, heading zero, camera offset): **start with global constants**; add per-robot values (appsettings by hostname, or a `RobotBases` column through the `mrr-database` agent) only if Phase 0 shows robots differ.
- **Accuracy target.** The numbers in this plan (3°, 3 mm, 25 mm) are proposals, not requirements.

## Files this will touch

`MRR/Devices/RobotConnection.cs` (move/turn methods, the log), `MRR/Players.cs` (expected pose / target heading), `MRR/GridAlignmentAgent.cs` (bounded call, sanity bound), a new `MRR.Tools.MoveTest` project (Phase 0), and tests in `MRR.Tests/`. No database changes in Phases 0–3 unless per-robot calibration is chosen.
