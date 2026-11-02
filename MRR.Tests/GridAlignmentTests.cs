using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MRR.Tests;

/// <summary>
/// Plus detection in GridAlignmentAgent (tools/ai_agent/grid_alignment_agent.md). Everything here
/// is synthetic: 640×480 frames with 9 px black grid lines drawn on a light floor. That proves the
/// geometry, not that a real robot camera frame looks like this -- that needs a live robot.
/// </summary>
public class GridAlignmentTests
{
    private const int Width = 640, Height = 480;

    // A front line at row hY and side lines at columns lX and rX. With pluses = false the side
    // lines stop at the front line, making T junctions. The whole grid is rotated by rotateDeg
    // (positive = clockwise in the image) about the frame center.
    private static byte[] MakeFrame(int hY, int lX, int rX, bool pluses = true, double rotateDeg = 0, int noise = 0)
    {
        using var img = new Image<Rgb24>(Width, Height, new Rgb24(200, 200, 190));
        var rnd = new Random(1);
        double c = Math.Cos(rotateDeg * Math.PI / 180), s = Math.Sin(rotateDeg * Math.PI / 180);
        img.ProcessPixelRows(accessor =>
        {
            for (int py = 0; py < Height; py++)
            {
                var row = accessor.GetRowSpan(py);
                for (int px = 0; px < Width; px++)
                {
                    double x = Width / 2 + (px - Width / 2) * c + (py - Height / 2) * s;
                    double y = Height / 2 - (px - Width / 2) * s + (py - Height / 2) * c;
                    bool line = Math.Abs(y - hY) <= 4
                        || ((Math.Abs(x - lX) <= 4 || Math.Abs(x - rX) <= 4) && (pluses || y >= hY - 4));
                    if (line) row[px] = new Rgb24(20, 20, 20);
                    else if (noise > 0)
                    {
                        int n = rnd.Next(-noise, noise) / 4;
                        row[px] = new Rgb24((byte)(200 + n), (byte)(200 + n), (byte)(190 + n));
                    }
                }
            }
        });
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Centered_FindsBothPlusesAtTheirCenters()
    {
        var result = GridAlignmentAgent.FindPluses(MakeFrame(360, 160, 480));

        Assert.True(result.Found);
        Assert.Equal(0.25, result.LeftPlusX, 2);
        Assert.Equal(0.75, result.LeftPlusY, 2);
        Assert.Equal(0.75, result.RightPlusX, 2);
        Assert.Equal(0.75, result.RightPlusY, 2);
        Assert.Equal(0, result.TiltDeg, 1);
        Assert.Equal(0, result.LateralOffsetNorm, 2);
    }

    [Fact]
    public void Shifted_ReportsLateralAndHeightOffsets()
    {
        // Midpoint 40 px right of center, pluses 30 px higher.
        var result = GridAlignmentAgent.FindPluses(MakeFrame(330, 200, 520));

        Assert.True(result.Found);
        Assert.Equal(40.0 / Width, result.LateralOffsetNorm, 2);
        Assert.Equal(330.0 / Height, result.MidY, 2);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-2)]
    [InlineData(5)]
    [InlineData(-5)]
    [InlineData(10)]
    [InlineData(-10)]
    public void Rotated_MeasuresTilt(double rotateDeg)
    {
        var result = GridAlignmentAgent.FindPluses(MakeFrame(360, 160, 480, rotateDeg: rotateDeg));

        Assert.True(result.Found);
        Assert.Equal(rotateDeg, result.TiltDeg, 0);
    }

    [Fact]
    public void Noisy_StillFindsPluses()
    {
        var result = GridAlignmentAgent.FindPluses(MakeFrame(360, 160, 480, noise: 60));

        Assert.True(result.Found);
        Assert.Equal(0, result.LateralOffsetNorm, 2);
    }

    [Fact]
    public void TJunctions_AreNotPluses()
    {
        Assert.False(GridAlignmentAgent.FindPluses(MakeFrame(360, 160, 480, pluses: false)).Found);
    }

    [Fact]
    public void NoLines_NotFound()
    {
        Assert.False(GridAlignmentAgent.FindPluses(MakeFrame(-100, -100, -100)).Found);
    }

    // First real frame from AIM-07's camera (2026-10-29), robot sitting on a board square.
    // Expected positions were measured by hand from the frame's dark pixels: front line at
    // row ~276, side lines crossing it at x ~168 and ~461, both slanting ~1 px/row outward
    // going down. The bottom-right corner has a large dark area that is not a line.
    [Fact]
    public void RealFrame_Aim07_FindsFrontCornerPluses()
    {
        var frame = File.ReadAllBytes(FindTestData("grid_align_aim07_20261029.jpg"));

        var result = GridAlignmentAgent.FindPluses(frame);

        Assert.True(result.Found);
        Assert.Equal(168.0 / Width, result.LeftPlusX, 0.015);
        Assert.Equal(461.0 / Width, result.RightPlusX, 0.015);
        Assert.Equal(276.0 / Height, result.LeftPlusY, 0.015);
        Assert.Equal(276.0 / Height, result.RightPlusY, 0.015);
        Assert.InRange(result.TiltDeg, -2, 2);
        // Side lines by hand: left through (180,262),(118,330), right through (448,262),(504,330);
        // they meet at about (321, 106).
        Assert.True(result.HasVanishingPoint);
        Assert.Equal(321.0 / Width, result.VanishingX, 0.03);
        Assert.Equal(106.0 / Height, result.VanishingY, 0.04);
    }

    // Same robot, brighter exposure (2026-10-29 17:08, facing a lit desk): floor median ~161 and
    // parts of the front line as bright as ~70, which a fixed "black below 60" cutoff missed.
    // Hand-measured: front line at row ~285, side lines crossing it at x ~197 and ~506.
    [Fact]
    public void RealFrame_Aim07Bright_FindsFrontCornerPluses()
    {
        var frame = File.ReadAllBytes(FindTestData("grid_align_aim07_bright_20261029.jpg"));

        var result = GridAlignmentAgent.FindPluses(frame);

        Assert.True(result.Found);
        Assert.Equal(197.0 / Width, result.LeftPlusX, 0.015);
        Assert.Equal(506.0 / Width, result.RightPlusX, 0.015);
        Assert.Equal(285.0 / Height, result.LeftPlusY, 0.015);
        Assert.Equal(285.0 / Height, result.RightPlusY, 0.015);
        Assert.InRange(result.TiltDeg, -2, 2);
        // Side lines by hand: left through (200,280),(173,320), right through (500,280),(543,320);
        // they meet at about (316, 109).
        Assert.True(result.HasVanishingPoint);
        Assert.Equal(316.0 / Width, result.VanishingX, 0.03);
        Assert.Equal(109.0 / Height, result.VanishingY, 0.04);
    }

    // ── PlanMove ──────────────────────────────────────────────────────────────

    // Two pluses with their midpoint at (0.5 + lateral, Target + below) and the given tilt.
    private static PlusAnalysis Pluses(double lateral = 0, double below = 0, double tilt = 0)
    {
        double y = GridAlignmentAgent.TargetPlusHeightNorm + below;
        return new PlusAnalysis(true, 0.25 + lateral, y, 0.75 + lateral, y, tilt);
    }

    [Fact]
    public void PlanMove_WithinThresholds_NoMove()
    {
        Assert.True(GridAlignmentAgent.PlanMove(Pluses(lateral: 0.01, below: -0.01, tilt: 0.5)).IsNone);
    }

    [Fact]
    public void PlanMove_NotFound_NoMove()
    {
        Assert.True(GridAlignmentAgent.PlanMove(PlusAnalysis.NotFound).IsNone);
    }

    [Theory]
    [InlineData( 0.1,  0.0,   90)]   // midpoint right of center -> move right
    [InlineData(-0.1,  0.0,  -90)]   // midpoint left of center  -> move left
    [InlineData( 0.0,  0.1,  180)]   // pluses below target (too close) -> move back
    [InlineData( 0.0, -0.1,    0)]   // pluses above target (too far)   -> move forward
    public void PlanMove_SingleDirection(double lateral, double below, int expectedAngle)
    {
        var move = GridAlignmentAgent.PlanMove(Pluses(lateral, below));

        Assert.Equal(expectedAngle, move.DriveAngleDeg);
        Assert.True(move.DriveMm > 0);
        Assert.Equal(0, move.TurnDeg);
    }

    [Theory]
    [InlineData( 0.1,  0.1,   91,  179)]   // too close and shifted left -> back and right in one drive
    [InlineData(-0.1, -0.1,  -89,   -1)]   // too far and shifted right  -> forward and left in one drive
    public void PlanMove_CombinesSidewaysAndForwardIntoOneDrive(double lateral, double below, int minAngle, int maxAngle)
    {
        var move = GridAlignmentAgent.PlanMove(Pluses(lateral, below));

        Assert.InRange(move.DriveAngleDeg, minAngle, maxAngle);
        Assert.Equal(0, move.TurnDeg);
    }

    [Fact]
    public void PlanMove_DiagonalDistanceIsCombinedLength()
    {
        int sideways = GridAlignmentAgent.PlanMove(Pluses(0.1, 0)).DriveMm;
        int back = GridAlignmentAgent.PlanMove(Pluses(0, 0.1)).DriveMm;

        int diagonal = GridAlignmentAgent.PlanMove(Pluses(0.1, 0.1)).DriveMm;

        Assert.InRange(diagonal, (int)Math.Sqrt(sideways * sideways + back * back) - 1,
                                 (int)Math.Sqrt(sideways * sideways + back * back) + 1);
    }

    [Fact]
    public void PlanMove_DriveAndTurnFromTheSameFrame()
    {
        var move = GridAlignmentAgent.PlanMove(Pluses(lateral: 0.1, below: 0.1, tilt: -5));

        Assert.True(move.DriveMm > 0);
        Assert.InRange(move.DriveAngleDeg, 91, 179);   // back and right
        Assert.Equal(-5, move.TurnDeg);
    }

    // Real AIM-07 step (2026-11-01 21:30:21): close to the line, centered, tilted -6.94°. Turning
    // alone slid the pluses to lateral 0.115, then 0.142, then out of view. The plan has to move
    // right as well as back, ahead of the slide the turn will cause.
    [Fact]
    public void PlanMove_CenteredButTilted_MovesAheadOfTheTurnsSidewaysSlide()
    {
        var move = GridAlignmentAgent.PlanMove(new PlusAnalysis(true, 0.25, 0.673, 0.756, 0.673, -6.94));

        Assert.Equal(-6.94, move.TurnDeg, 2);
        Assert.InRange(move.DriveAngleDeg, 91, 179);   // back and right
    }

    [Fact]
    public void PlanMove_TurnOnly_DoesNotDrive()
    {
        // drive_for with distance 0 drives without stopping, so no drive must be planned. A 1.2°
        // turn's predicted sideways slide (0.019) stays inside the 0.02 threshold.
        var move = GridAlignmentAgent.PlanMove(Pluses(tilt: 1.2));

        Assert.Equal(0, move.DriveMm);
        Assert.Equal(1.2, move.TurnDeg);
    }

    // Pluses as above, plus a vanishing point at x = 0.5 + vanishing.
    private static PlusAnalysis PlusesWithVanishing(double lateral, double vanishing, double tilt = 0) =>
        Pluses(lateral, 0, tilt) with { VanishingX = 0.5 + vanishing, VanishingY = 0.2 };

    [Fact]
    public void PlanMove_RotationOnly_TurnsWithoutDrivingSideways()
    {
        // Turning moves the vanishing point and the pluses together: the pluses are 0.06 right
        // of center only because the robot is turned, so it must turn back and not drive.
        var move = GridAlignmentAgent.PlanMove(PlusesWithVanishing(lateral: 0.06, vanishing: 0.06));

        Assert.Equal(0, move.DriveMm);
        Assert.True(move.TurnDeg > 1);   // vanishing point right of center = turned left -> turn right
    }

    [Fact]
    public void PlanMove_ShiftedButStraight_DrivesWithoutTurning()
    {
        // Vanishing point centered, pluses right of it: robot shifted left, facing straight.
        var move = GridAlignmentAgent.PlanMove(PlusesWithVanishing(lateral: 0.08, vanishing: 0, tilt: 3));

        Assert.Equal(90, move.DriveAngleDeg);
        Assert.Equal(0, move.TurnDeg);   // the front line's tilt is ignored when there is a vanishing point
    }

    // Final frame of an AIM-04 run (2026-11-01 21:54:32) that the tilt-based plan called aligned:
    // tilt -0.72°, pluses centered, but vanishing point 25 px right of center and the pluses left
    // of it. On the board the robot was turned left and shifted right.
    [Fact]
    public void PlanMove_LevelLookingButTurnedLeft_TurnsRight()
    {
        var move = GridAlignmentAgent.PlanMove(PlusesWithVanishing(lateral: 0.025, vanishing: 25.0 / 640, tilt: -0.72));

        Assert.InRange(move.TurnDeg, 1.0, 2.0);
    }

    private static string FindTestData(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "MRR.Tests", "TestData", fileName);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"MRR.Tests/TestData/{fileName} not found above the test output directory.");
    }

    [Fact]
    public void NotAnImage_NotFound()
    {
        Assert.False(GridAlignmentAgent.FindPluses([1, 2, 3]).Found);
    }
}
