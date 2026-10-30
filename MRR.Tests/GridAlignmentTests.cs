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
