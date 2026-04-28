# Grid Alignment Agent

**File:** `MRR/GridAlignmentAgent.cs`
**Implemented:** 2026-04-28

## Purpose

After each game move, a robot may drift slightly off the center of its board square. The agent captures a camera frame from the robot, measures how asymmetrically the black grid lines appear, and nudges the robot with small `drive_for` commands until it is centered.

## Architecture

```
GET /api/robot/align/{robotId}
        ↓
GridAlignmentAgent.AlignAsync(Player robot)   ← main loop, up to 5 iterations
        ↓
Player.GetCameraImageAsync()                   ← ws://{ip}:80/ws_img (one frame)
        ↓
GridAlignmentAgent.AnalyzeImage(byte[])        ← count black pixels per quadrant
        ↓
GridAlignmentAgent.ApplyCorrectionAsync()      ← drive_for nudge + WaitForMotionComplete
```

## How Detection Works

Black grid lines between board squares appear in the camera image. When the robot is centered, dark pixels are roughly symmetric across both axes. When off-center:

- More black on the **right** → robot shifted right → strafe **left** (angle = −90°)
- More black on the **left** → robot shifted left → strafe **right** (angle = +90°)
- More black on the **top** → robot shifted forward → move **backward** (angle = 180°)
- More black on the **bottom** → robot shifted backward → move **forward** (angle = 0°)

Normalized offsets: `OffsetX = (rightBlack − leftBlack) / totalBlack`, same for Y.

## Calibration Constants (tune empirically)

| Constant | Default | What to adjust |
|---|---|---|
| `BlackLuminanceThreshold` | 60 | Raise if floor shadows cause false positives; lower if lines aren't detected |
| `MinBlackPixels` | 100 | Raise if getting false "no lines" on a real board |
| `AlignedThreshold` | 0.05 | Tighten for more precision; loosen for fewer correction iterations |
| `NudgeDistanceMm` | 10 mm | Reduce if robot overshoots; raise if corrections are too small |
| `NudgeSpeed` | 50% | Reduce for smoother corrections |

## Return Value (JSON)

```json
{
  "hasLines": true,
  "offsetX": 0.02,
  "offsetY": -0.01,
  "blackPixelCount": 840,
  "isAligned": true
}
```

## Known Unknowns

- Camera orientation relative to robot forward axis is assumed but not confirmed — if corrections move in the wrong direction, negate the sign of the affected angle.
- The `ws_img` wire format is unconfirmed. See `ws_img_format.md`.
