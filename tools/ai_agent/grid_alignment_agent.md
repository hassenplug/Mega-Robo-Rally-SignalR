# Grid Alignment Agent

**File:** `MRR/GridAlignmentAgent.cs`
**Implemented:** 2026-04-28 (line method); **replaced 2026-10-28** with the two-plus method below.
Tested only on synthetic images — never on a real robot camera frame.

## Purpose

After each game move, a robot may drift slightly off the center of its board square. The agent captures a camera frame from the robot, finds the two "plus" intersections of the black grid lines at the **front-left and front-right corners** of the robot's square, and nudges the robot until those pluses sit where they should.

## Architecture

```
GET /api/robot/alignthis/{robotId}   (also GET /api/robot/align/{robotId})
        ↓
GridAlignmentAgent.AlignAsync(RobotConnection)  ← main loop, up to 10 iterations
        ↓
RobotConnection.GetCameraImageAsync()           ← ws://{Hostname}:80/ws_img (one frame)
        ↓
GridAlignmentAgent.FindPluses(byte[])           → PlusAnalysis
        ↓
turn_for / drive_for nudge + WaitForStopAsync
```

## How Detection Works

Only the bottom half of the frame is searched (`SearchTopNorm`).

1. **Rough lines.** Row and column dark-pixel histograms, each summed over a ±2% band (`LineBandNorm`) so a few degrees of rotation doesn't smear a line below threshold. The strongest row is the front line; the strongest column in each half is a side line.
2. **Plus center.** Around each rough crossing, a local window (`PlusWindowNorm`) gives the exact center: the middle of the strongest row and column in that window.
3. **Plus check.** The center must have dark arms in all four directions (`ArmGapNorm`..`ArmLengthNorm`, `MinArmFraction`). A T or L corner is rejected. Both pluses must be found, or nothing moves.

## Target: symmetric about center

No calibration. The robot is aligned when:

| Correction (in this order) | Measured as | Target |
|---|---|---|
| 1. Rotate (`turn_for`) | `TiltDeg` = angle of the line from left plus to right plus | 0° |
| 2. Strafe (`drive_for` ±90°) | `LateralOffsetNorm` = plus midpoint X − 0.5 | 0 |
| 3. Forward/back (`drive_for` 0°/180°) | `MidY` = plus midpoint Y | `TargetPlusHeightNorm` (0.75) |

This assumes the camera is mounted dead-center and straight. If it isn't, the robot will end up consistently off by the camera's offset.

## Tunable Constants

| Constant | Default | What to adjust |
|---|---|---|
| `BlackLuminanceThreshold` | 60 | Raise if lines aren't detected; lower if shadows count as lines |
| `SearchTopNorm` | 0.5 | Lower if the pluses appear in the top half of the frame |
| `TargetPlusHeightNorm` | 0.75 | Where the pluses sit when the robot is centered — **set from a real frame** |
| `ArmHalfWidthPx` | 4 | Raise to tolerate more rotation (fails beyond ~5° at 4) |
| `ArmLengthNorm` / `MinArmFraction` | 0.07 / 0.6 | Lower if real pluses are rejected as T/L |
| `LateralScaleMm` / `ForwardScaleMm` | 60 | mm moved per unit of normalized offset |

## Synthetic Test Results (640×480, 2026-10-28)

Unit tests: `MRR.Tests/GridAlignmentTests.cs` (`dotnet test MRR.Tests --filter GridAlignmentTests`). They cover every row below except the ±10°/15° limitation.

| Case | Result |
|---|---|
| Centered | Found, exact (0.25/0.75, 0.75/0.75) |
| Shifted | Found, exact |
| Rotated ±2°, ±5° | Found, tilt measured to within 0.05° |
| Rotated ±10°, 15° | **Not found** (arm check; see `ArmHalfWidthPx`) |
| T junctions only | Correctly rejected |
| Noise | Found, exact |
| No lines | Correctly not found |

## Debug Images

Each iteration saves `images/align/align_{host}_{ts}_orig.jpg` and `..._pluses.jpg` (dark pixels red, ignored region grayed, found pluses drawn green). On the deployed server this is `/srv/mrr/game/images/align/`.

## Known Unknowns

- Camera orientation relative to robot forward axis is assumed but not confirmed. If corrections move in the wrong direction, negate the sign of the affected angle.
- Large dark areas (board edge, robot shadow) in the bottom half can outweigh the lines.
- Perspective: a downward-angled camera makes the side lines converge. The window/arm check tolerates some of this; real frames will tell.
- The `ws_img` wire format is unconfirmed. See `ws_img_format.md`.
