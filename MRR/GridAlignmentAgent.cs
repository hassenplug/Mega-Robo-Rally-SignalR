using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;

namespace MRR;

// Result from the line-detection analysis.
// All X/Y positions are normalized 0..1 (0 = left/top, 1 = right/bottom).
// LeftLineX / RightLineX are -1 when that line was not found.
public record GridLineAnalysis(
    bool Found,
    double HorizontalLineY,         // normalized Y of the horizontal grid line
    double HorizontalLineTiltDeg,   // positive = right side of line is lower (CW tilt)
    double LeftLineX,               // normalized X of left vertical line, or -1
    double RightLineX,              // normalized X of right vertical line, or -1
    bool HasBothVerticalLines)
{
    // Normalized offset of the vertical-line midpoint from image center.
    // Positive = midpoint is right of center = robot shifted left = strafe right.
    // Negative = midpoint is left of center = robot shifted right = strafe left.
    public double LateralOffsetNorm =>
        HasBothVerticalLines ? (LeftLineX + RightLineX) / 2.0 - 0.5 : 0;
}

// Legacy result from the quadrant-counting method (preserved for reference).
public record GridAlignmentResult(
    bool HasLines,
    double OffsetX,
    double OffsetY,
    int BlackPixelCount,
    bool IsAligned
);

// Captures a camera frame via ws_img, detects the black grid lines between board
// squares, and nudges the robot into alignment in three phases:
//   1. Rotate until the horizontal line is level.
//   2. Strafe until the two vertical lines are equidistant from center.
//   3. Drive forward/back until the horizontal line is at TargetLineHeightNorm.
public static class GridAlignmentAgent
{
    // ── Tunable constants ─────────────────────────────────────────────────────

    // Pixels with (R+G+B)/3 below this are treated as black.
    private const int BlackLuminanceThreshold = 60;

    // Minimum fraction of a row/column that must be dark to count as a grid line.
    private const double MinHorizontalLineFraction = 0.15;
    private const double MinVerticalLineFraction   = 0.15;

    // Correction thresholds — don't move if already within these limits.
    private const double RotationThresholdDeg  = 1.0;   // degrees
    private const double LateralThresholdNorm  = 0.04;  // fraction of image width
    private const double ForwardThresholdNorm  = 0.04;  // fraction of image height

    // Nudge distance per unit of normalized offset (mm).
    private const double LateralScaleMm = 60.0;
    private const double ForwardScaleMm = 60.0;

    // Speed used for nudge corrections (percent).
    private const int NudgeSpeed = 50;

    // Max ms to wait for each nudge move to complete.
    private const int MotionTimeoutMs = 3000;

    // Where we want the horizontal line to sit vertically in the frame (0=top, 1=bottom).
    // Tune this based on camera mount position and desired centering point.
    public static double TargetLineHeightNorm { get; set; } = 0.5;

    // ── Main entry point ─────────────────────────────────────────────────────

    // Capture image → detect lines → correct in order: rotation, lateral, forward/back.
    // Loops until aligned or maxIterations exhausted.
    public static async Task<GridLineAnalysis> AlignAsync(Player robot, int maxIterations = 10)
    {
        var last = new GridLineAnalysis(false, 0, 0, -1, -1, false);

        for (int i = 0; i < maxIterations; i++)
        {
            var bytes = await robot.GetCameraImageAsync();
            if (bytes == null)
            {
                Console.WriteLine($"[GridAlign] No image from {robot.IPAddress}");
                return last;
            }

            last = FindGridLines(ExtractImageBytes(bytes));
            Console.WriteLine(
                $"[GridAlign] iter={i} found={last.Found} " +
                $"hY={last.HorizontalLineY:F3} tilt={last.HorizontalLineTiltDeg:F2}° " +
                $"lx={last.LeftLineX:F3} rx={last.RightLineX:F3} " +
                $"lateral={last.LateralOffsetNorm:F3}");

            if (!last.Found) break;

            bool needsRotation = Math.Abs(last.HorizontalLineTiltDeg) > RotationThresholdDeg;
            bool needsLateral  = last.HasBothVerticalLines
                                 && Math.Abs(last.LateralOffsetNorm) > LateralThresholdNorm;
            bool needsForward  = Math.Abs(last.HorizontalLineY - TargetLineHeightNorm) > ForwardThresholdNorm;

            if (!needsRotation && !needsLateral && !needsForward) break;

            // Phase 1: resolve rotation before lateral/forward corrections are meaningful.
            if (needsRotation)
            {
                // tiltDeg > 0 = line tilts CW = robot rotated CW = turn CCW (negative angle).
                await robot.SendCommandAsync(new
                {
                    cmd_id    = "turn_for",
                    angle     = -last.HorizontalLineTiltDeg,
                    turn_rate = 50,
                    stacking_type = 0
                });
                await robot.WaitForMotionCompleteAsync(MotionTimeoutMs);
                continue;
            }

            // Phase 2: strafe until vertical lines are centered.
            if (needsLateral)
            {
                double nudgeMm = last.LateralOffsetNorm * LateralScaleMm;
                // LateralOffsetNorm > 0 = midpoint right of center = robot shifted left = strafe right (+90).
                // LateralOffsetNorm < 0 = midpoint left of center  = robot shifted right = strafe left (-90).
                await robot.SendCommandAsync(new
                {
                    cmd_id        = "drive_for",
                    distance      = (int)Math.Abs(nudgeMm),
                    angle         = nudgeMm > 0 ? 90 : -90,
                    drive_speed   = NudgeSpeed,
                    turn_speed    = 0,
                    final_heading = 0,
                    stacking_type = 0
                });
                await robot.WaitForMotionCompleteAsync(MotionTimeoutMs);
                continue;
            }

            // Phase 3: drive forward/back until horizontal line is at target height.
            if (needsForward)
            {
                double forwardOffset = last.HorizontalLineY - TargetLineHeightNorm;
                // forwardOffset > 0 = line is below target = robot is past the line = move back (180°).
                // forwardOffset < 0 = line is above target = robot hasn't reached the line = move forward (0°).
                double nudgeMm = Math.Abs(forwardOffset) * ForwardScaleMm;
                await robot.SendCommandAsync(new
                {
                    cmd_id        = "drive_for",
                    distance      = (int)nudgeMm,
                    angle         = forwardOffset > 0 ? 180 : 0,
                    drive_speed   = NudgeSpeed,
                    turn_speed    = 0,
                    final_heading = 0,
                    stacking_type = 0
                });
                await robot.WaitForMotionCompleteAsync(MotionTimeoutMs);
            }
        }

        return last;
    }

    // ── Line detection ────────────────────────────────────────────────────────

    // Detect the horizontal grid line and the vertical grid lines on each side.
    //
    // Single pixel pass builds four histograms simultaneously:
    //   rowCounts     — dark pixels per row (full width)   → horizontal line position
    //   leftBandRows  — dark pixels per row in left third  ┐ → horizontal line tilt
    //   rightBandRows — dark pixels per row in right third ┘
    //   colCounts     — dark pixels per column             → vertical line positions
    public static GridLineAnalysis FindGridLines(byte[] imageData)
    {
        try
        {
            using var img = Image.Load<Rgb24>(imageData);
            int w = img.Width, h = img.Height;
            int bandW = w / 3;

            var rowCounts     = new int[h];
            var leftBandRows  = new int[h];
            var rightBandRows = new int[h];
            var colCounts     = new int[w];

            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < h; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < w; x++)
                    {
                        var p = row[x];
                        if ((p.R + p.G + p.B) / 3 < BlackLuminanceThreshold)
                        {
                            rowCounts[y]++;
                            colCounts[x]++;
                            if (x < bandW)          leftBandRows[y]++;
                            else if (x >= w - bandW) rightBandRows[y]++;
                        }
                    }
                }
            });

            // Horizontal line: row with the most dark pixels across the full width.
            int hLineRow = PeakIndex(rowCounts, 0, h);
            if (rowCounts[hLineRow] < w * MinHorizontalLineFraction)
                return new GridLineAnalysis(false, 0, 0, -1, -1, false);

            // Tilt: compare the peak row of the left third vs the right third.
            // Positive = right peak is lower = line tilts CW.
            int leftPeakRow  = PeakIndex(leftBandRows,  0, h);
            int rightPeakRow = PeakIndex(rightBandRows, 0, h);
            double tiltDeg = Math.Atan2(rightPeakRow - leftPeakRow, 2.0 * bandW)
                             * 180.0 / Math.PI;

            // Vertical lines: peak column in each half of the image.
            int leftLineCol  = PeakIndex(colCounts, 0,     w / 2);
            int rightLineCol = PeakIndex(colCounts, w / 2, w);
            bool hasLeftLine  = colCounts[leftLineCol]  >= h * MinVerticalLineFraction;
            bool hasRightLine = colCounts[rightLineCol] >= h * MinVerticalLineFraction;

            return new GridLineAnalysis(
                Found:               true,
                HorizontalLineY:     (double)hLineRow    / h,
                HorizontalLineTiltDeg: tiltDeg,
                LeftLineX:           hasLeftLine  ? (double)leftLineCol  / w : -1,
                RightLineX:          hasRightLine ? (double)rightLineCol / w : -1,
                HasBothVerticalLines: hasLeftLine && hasRightLine
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GridAlign] Line analysis error: {ex.Message}");
            return new GridLineAnalysis(false, 0, 0, -1, -1, false);
        }
    }

    // ── Legacy quadrant method (preserved) ───────────────────────────────────

    // Original approach: counts dark pixels in each image quadrant and returns
    // normalized imbalances. Kept for comparison / fallback.
    // OffsetX > 0 = more dark pixels on right = robot shifted right.
    // OffsetY > 0 = more dark pixels on top   = robot shifted forward.
    public static GridAlignmentResult AnalyzeImageByQuadrant(byte[] imageData)
    {
        try
        {
            using var img = Image.Load<Rgb24>(imageData);
            int midX = img.Width / 2, midY = img.Height / 2;
            long leftBlack = 0, rightBlack = 0, topBlack = 0, bottomBlack = 0;

            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < img.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < img.Width; x++)
                    {
                        var p = row[x];
                        if ((p.R + p.G + p.B) / 3 < BlackLuminanceThreshold)
                        {
                            if (x < midX) leftBlack++; else rightBlack++;
                            if (y < midY) topBlack++; else bottomBlack++;
                        }
                    }
                }
            });

            int total = (int)(leftBlack + rightBlack);
            if (total < 100)
                return new GridAlignmentResult(false, 0, 0, total, false);

            double dx = (double)(rightBlack - leftBlack) / total;
            double dy = (double)(topBlack - bottomBlack) / total;
            bool aligned = Math.Abs(dx) < 0.05 && Math.Abs(dy) < 0.05;
            return new GridAlignmentResult(true, dx, dy, total, aligned);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GridAlign] Quadrant analysis error: {ex.Message}");
            return new GridAlignmentResult(false, 0, 0, 0, false);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Index of the maximum value in arr[start..end).
    private static int PeakIndex(int[] arr, int start, int end)
    {
        int best = start;
        for (int i = start + 1; i < end; i++)
            if (arr[i] > arr[best]) best = i;
        return best;
    }

    // If the payload is a JSON object with a base64 image field, extract and decode it.
    // Otherwise return the bytes unchanged (assumed raw JPEG).
    private static byte[] ExtractImageBytes(byte[] data)
    {
        if (data.Length > 0 && data[0] == '{')
        {
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                foreach (var key in new[] { "image", "data", "frame", "jpeg" })
                    if (root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
                    {
                        var b64 = el.GetString();
                        if (b64 != null) return Convert.FromBase64String(b64);
                    }
            }
            catch { }
        }
        return data;
    }
}
