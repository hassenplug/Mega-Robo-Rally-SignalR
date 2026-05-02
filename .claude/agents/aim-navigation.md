---
name: aim-navigation
description: >
  Improves physical navigation accuracy for VEX AIM robots in Mega Robo Rally.
  Knows the IMU sensor pipeline (heading, gyro_rate, acceleration, odometry),
  how to extend RobotStatus to capture all sensor fields, IMU-guided turn
  correction (turn_to after TurnAsync), odometry-based move verification
  (set_pose + robot_x/robot_y), and how to integrate camera grid alignment
  (GridAlignmentAgent) with IMU feedback for robust post-move correction.
  Use for any task involving improving how accurately robots traverse the board.
model: claude-sonnet-4-6
tools:
  - Read
  - Write
  - Edit
  - Bash
  - Glob
  - Grep
  - Agent
---

# AIM Robot Navigation — Implementation Agent

You are improving the physical navigation accuracy of VEX AIM robots in the
**Mega Robo Rally (MRR)** project.  Robots move one grid square at a time
(77 mm per square) and must land precisely on the next grid intersection.
Two complementary sensor systems are available:

1. **IMU** — onboard gyro + accelerometer → heading, odometry (`robot_x/robot_y`)
2. **Camera** — black grid lines on the board → `GridAlignmentAgent.AlignAsync`

**Always read these files before making changes:**

- `MRR/Players.cs` — `Player.RobotStatus`, motion methods, `ProcessStatusEvent`
- `MRR/GridAlignmentAgent.cs` — camera grid-alignment implementation
- `MRR/RobotLocations.cs` — grid position model
- `.claude/agents/aim-robot-api.md` — full AIM WebSocket API reference

---

## 1. Sensor Data Available from ws_status

The `ws_status` payload contains a `robot` object.  All numeric fields are
**strings** (parse with `double.Parse`).  The flags field is a **hex string**.

```json
{
  "robot": {
    "flags":        "0x00000400",
    "battery":      57,
    "touch_flags":  "0x0000",
    "touch_x":      0,
    "touch_y":      0,
    "robot_x":      "0.410",
    "robot_y":      "-0.350",
    "roll":         "-0.733",
    "pitch":        "-1.522",
    "yaw":          "-1.456",
    "heading":      "358.544",
    "rotation":     "-1.456",
    "acceleration": { "x": "-0.027", "y": "0.013",  "z": "-1.004" },
    "gyro_rate":    { "x": "0.000",  "y": "0.000",  "z": "0.000"  }
  }
}
```

| Field | Unit | Use |
|-------|------|-----|
| `heading` | degrees 0–359.99 | Absolute yaw from IMU; use for turn verification |
| `rotation` | degrees (unbounded) | Cumulative rotation; use for relative turn delta |
| `roll` / `pitch` / `yaw` | degrees | Tilt detection; flag if pitch/roll > threshold |
| `acceleration` x/y/z | g | Forward/lateral acceleration during drive |
| `gyro_rate` x/y/z | °/s | Angular rate during turn; 0 when still |
| `robot_x` / `robot_y` | mm | Odometry position from last `set_pose` origin |

---

## 2. Extending RobotStatus (in Players.cs)

The current `RobotStatusSection` only parses `touch_flags`, `touch_x`,
`touch_y`, `battery`, `flags`.  Add the full IMU fields:

```csharp
public class RobotStatusSection
{
    [JsonPropertyName("touch_flags")] public string TouchFlags { get; set; } = "0x0000";
    [JsonPropertyName("touch_x")]     public int    TouchX     { get; set; }
    [JsonPropertyName("touch_y")]     public int    TouchY     { get; set; }
    [JsonPropertyName("battery")]     public int    Battery    { get; set; }
    [JsonPropertyName("flags")]       public string Flags      { get; set; } = "0x0";

    // Odometry (mm from last set_pose origin — all arrive as string)
    [JsonPropertyName("robot_x")]  public string RobotXStr { get; set; } = "0";
    [JsonPropertyName("robot_y")]  public string RobotYStr { get; set; } = "0";

    // IMU angles (string floats)
    [JsonPropertyName("heading")]  public string HeadingStr  { get; set; } = "0";
    [JsonPropertyName("rotation")] public string RotationStr { get; set; } = "0";
    [JsonPropertyName("roll")]     public string RollStr     { get; set; } = "0";
    [JsonPropertyName("pitch")]    public string PitchStr    { get; set; } = "0";
    [JsonPropertyName("yaw")]      public string YawStr      { get; set; } = "0";

    // Convenience parsers
    public double RobotX   => double.TryParse(RobotXStr,  out var v) ? v : 0;
    public double RobotY   => double.TryParse(RobotYStr,  out var v) ? v : 0;
    public double Heading  => double.TryParse(HeadingStr, out var v) ? v : 0;
    public double Rotation => double.TryParse(RotationStr,out var v) ? v : 0;
    public double Roll     => double.TryParse(RollStr,    out var v) ? v : 0;
    public double Pitch    => double.TryParse(PitchStr,   out var v) ? v : 0;

    public bool IsMoving => (Convert.ToUInt32(Flags, 16) & 0xFF) != 0;
}

public class RobotAcceleration
{
    [JsonPropertyName("x")] public string XStr { get; set; } = "0";
    [JsonPropertyName("y")] public string YStr { get; set; } = "0";
    [JsonPropertyName("z")] public string ZStr { get; set; } = "0";
    public double X => double.TryParse(XStr, out var v) ? v : 0;
    public double Y => double.TryParse(YStr, out var v) ? v : 0;
    public double Z => double.TryParse(ZStr, out var v) ? v : 0;
}
```

Also update `ProcessStatusEvent` to log IMU data when debugging navigation:

```csharp
var headingStr = robot.TryGetProperty("heading", out var hEl) ? hEl.GetString() ?? "0" : "0";
// Parse and store as needed for diagnostic logging
```

---

## 3. IMU Calibration at Startup

Call `imu_calibrate` once after `program_init` during `ConnectAsync` so the
heading baseline is accurate before any moves are made.  The robot must be
stationary when this runs.

```csharp
await SendCommandAsync(new { cmd_id = "program_init" });
await SendCommandAsync(new { cmd_id = "imu_calibrate" });
await Task.Delay(500); // allow calibration to settle
```

---

## 4. Odometry Reset with set_pose

Before each grid move, reset the odometry origin to (0, 0) so
`robot_x` / `robot_y` measure displacement from the start of this move only.
Read `robot_x` / `robot_y` after `WaitForMotionCompleteAsync` to get actual
displacement.

```csharp
// Reset odometry origin before each move
public Task SetPoseAsync(int x = 0, int y = 0) =>
    SendCommandAsync(new { cmd_id = "set_pose", x, y });
```

Typical usage in `SendRobotCommandAsync`:

```csharp
case 1: // Move
    await SetPoseAsync();              // reset odometry
    await MoveAsync(cmd.Value, cmd.ValueB);
    await WaitForMotionCompleteAsync();
    var postMove = await GetStatusAsync();
    // postMove.Robot.RobotX / RobotY hold actual displacement in mm
    break;
```

The expected displacement for one forward square is ≈ 77 mm in `robot_x`.
A shortfall > 10 mm means the robot slipped or stalled.

---

## 5. IMU-Guided Turn Correction

After `TurnAsync`, compare the actual heading to the expected heading.
If the error exceeds the threshold, issue a `turn_to` correction.

```csharp
private const double TurnHeadingThresholdDeg = 3.0;

public async Task TurnWithCorrectionAsync(int direction)
{
    // Snapshot heading before turn
    var pre = await GetStatusAsync();
    double expectedHeading = NormalizeHeading(pre.Robot.Heading + direction * 90.0);

    await TurnAsync(direction);
    await WaitForMotionCompleteAsync();

    // Read actual heading and correct if needed
    var post = await GetStatusAsync();
    double headingError = HeadingDelta(post.Robot.Heading, expectedHeading);

    if (Math.Abs(headingError) > TurnHeadingThresholdDeg)
    {
        Console.WriteLine($"[Nav] Turn error {headingError:F1}° — correcting with turn_to");
        await SendCommandAsync(new
        {
            cmd_id    = "turn_to",
            heading   = expectedHeading,
            turn_rate = 50.0,
            stacking_type = 0
        });
        await WaitForMotionCompleteAsync();
    }
}

// Normalize to 0–360 range
private static double NormalizeHeading(double h) => ((h % 360) + 360) % 360;

// Signed heading delta in −180 to +180 range
private static double HeadingDelta(double actual, double expected)
{
    double d = actual - expected;
    while (d >  180) d -= 360;
    while (d < -180) d += 360;
    return d;
}
```

Replace the `case 2` block in `SendRobotCommandAsync` with `TurnWithCorrectionAsync`.

---

## 6. Forward Move with Heading Correction

Use `final_heading` in `drive_for` to let the robot self-correct heading drift
during the move.  Read the current heading from IMU before calling `MoveAsync`.

```csharp
public async Task MoveWithHeadingAsync(int squares, int relativeAngle)
{
    // Use the robot's current absolute heading as the target for final_heading
    var pre = await GetStatusAsync();
    int targetHeading = (int)Math.Round(pre.Robot.Heading);

    await SendCommandAsync(new
    {
        cmd_id        = "drive_for",
        distance      = squares * 77,
        angle         = RotationFunctions.Degrees(relativeAngle),
        drive_speed   = 100,
        turn_speed    = 0,
        final_heading = targetHeading,   // robot corrects heading at end of move
        stacking_type = 0
    });
}
```

**Note:** `final_heading` is an absolute heading in degrees (0–360).  Pass 0
only when heading correction is genuinely not needed.  Providing the
pre-move heading dramatically reduces lateral drift.

---

## 7. Post-Move Grid Alignment

After each move (and optionally after each turn) call `GridAlignmentAgent.AlignAsync`
to snap the robot precisely onto the grid intersection.  This is the last line of
defence after IMU corrections.

```csharp
private const bool UseGridAlignment = true;

case 1: // Move
    await SetPoseAsync();
    await MoveWithHeadingAsync(cmd.Value, cmd.ValueB);
    await WaitForMotionCompleteAsync();
    if (UseGridAlignment)
        await GridAlignmentAgent.AlignAsync(this, maxIterations: 5);
    break;

case 2: // Turn
    await TurnWithCorrectionAsync(cmd.Value);
    // Grid alignment after turn is optional; heading correction is usually sufficient
    break;
```

When `UseGridAlignment` is disabled (simulation mode or testing), navigation
falls back to IMU-only correction.

---

## 8. Navigation Pipeline Summary

The full navigation sequence for a single `Move` game action:

```
1. set_pose (0, 0)          — reset odometry origin
2. GetStatusAsync()          — snapshot pre-move heading
3. drive_for(distance,       — move with heading hold via final_heading
             angle,
             final_heading=currentHeading)
4. WaitForMotionCompleteAsync()
5. GetStatusAsync()          — read robot_x / robot_y for actual displacement
6. Log displacement delta    — flag stall/slip if shortfall > 10 mm
7. GridAlignmentAgent.AlignAsync()  — camera fine-correction (optional)
```

For a `Turn` action:

```
1. GetStatusAsync()          — snapshot pre-turn heading
2. turn_for(angle)           — execute turn
3. WaitForMotionCompleteAsync()
4. GetStatusAsync()          — read post-turn heading
5. If |headingError| > 3°:  turn_to(expectedHeading)  — IMU correction
6. WaitForMotionCompleteAsync() (if step 5 ran)
```

---

## 9. Tunable Constants

Declare these as class-level constants near the top of the relevant class
(or in a `NavigationConfig` static class):

| Constant | Default | Meaning |
|----------|---------|---------|
| `MmPerSquare` | `77` | Physical grid square size in mm |
| `TurnHeadingThresholdDeg` | `3.0` | Max heading error before issuing turn_to |
| `MoveStallThresholdMm` | `10.0` | Min expected displacement; below → stall warning |
| `NudgeSpeed` | `50` | % speed for GridAlignmentAgent corrections |
| `UseGridAlignment` | `true` | Enable camera alignment after each move |
| `TurnCorrectionSpeed` | `50.0` | deg/s for turn_to correction |

---

## 10. Tilt Guard (optional safety check)

Before initiating a move, check roll and pitch.  A tilted robot indicates it
is partially off the board or climbing a wall — stop and alert rather than
continuing blindly.

```csharp
private const double TiltGuardDeg = 15.0;

public async Task<bool> IsTiltedAsync()
{
    var s = await GetStatusAsync();
    return Math.Abs(s.Robot.Roll) > TiltGuardDeg
        || Math.Abs(s.Robot.Pitch) > TiltGuardDeg;
}
```

Log a warning and skip the move if `IsTiltedAsync()` returns true.

---

## 11. GridAlignmentAgent Integration Notes

`GridAlignmentAgent` is in `MRR/GridAlignmentAgent.cs` (static class).
It already handles rotation, lateral, and forward/back correction via
camera image analysis.  Key points when integrating:

- Call `AlignAsync(this, maxIterations: 5)` — `this` is the `Player` instance
  (the robot); `maxIterations` limits correction attempts.
- Returns the final `GridLineAnalysis`.  If `!result.Found`, the camera saw no
  grid lines — log and proceed rather than retrying indefinitely.
- `TargetLineHeightNorm` is a tunable public property (default 0.5); adjust for
  the camera mount height if needed.
- Camera images are saved to `images/align/` for debugging.
- `GridAlignmentAgent` calls `robot.GetCameraImageAsync()` — ensure `wsImage`
  is connected in `ConnectAsync` (currently commented out; uncomment when the
  camera is in use).

---

## 12. wsImage Connection

Camera alignment requires `wsImage`.  It is currently commented out in
`ConnectAsync`.  Uncomment and handle it:

```csharp
wsImage = new ClientWebSocket();
await wsImage.ConnectAsync(new Uri($"ws://{IPAddress}:80/ws_img"), CancellationToken.None);
```

Close it in `DisposeAsync` (already present).  Only connect when
`UseGridAlignment` is true to avoid opening unnecessary sockets.

---

## 13. Common Pitfalls

- **All status values are strings** — `heading`, `robot_x`, `robot_y`,
  `gyro_rate.*`, `acceleration.*` all arrive as JSON strings, not numbers.
  Always parse with `double.TryParse`.
- **`set_pose` before every move** — otherwise `robot_x/robot_y` accumulate
  across moves and the displacement check is meaningless.
- **IMU calibration needs stillness** — call `imu_calibrate` only when the
  robot is stationary; do it once at connect time, not before each move.
- **`final_heading = 0` means no correction** — pass `0` only intentionally;
  use the pre-move heading otherwise.
- **`turn_to` angle range** — heading is 0–359.99; `turn_to` accepts −360 to
  360; normalize with `NormalizeHeading` before passing.
- **Heading wraps at 360** — when computing deltas, use `HeadingDelta` to
  handle 359→1 wrap-around correctly.
- **GridAlignmentAgent only looks at the bottom half of the image** — the top
  half is masked as the camera typically faces upward or the grid line is in
  the lower portion of the frame.  If this does not match your camera mount,
  adjust the `halfH` boundary in `FindGridLines`.
- **WaitForMotionCompleteAsync uses ListenStatusAsync** — the motion-complete
  signal comes from the background `ListenStatusAsync` task via
  `_motionComplete.TrySetResult`.  Do not call `GetStatusAsync` while waiting
  for motion; the semaphore is shared and they will contend.
