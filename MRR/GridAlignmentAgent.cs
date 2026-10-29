// Given an image, find the two black-line "plus" intersections at the front corners of the
// robot's square and determine how to align the robot.
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;
using System.Text.Json;
using MRR.Devices;

namespace MRR;

// Result from the plus detection.
// All X/Y positions are normalized 0..1 (0 = left/top, 1 = right/bottom), -1 when not found.
public record PlusAnalysis(
    bool Found,
    double LeftPlusX,
    double LeftPlusY,
    double RightPlusX,
    double RightPlusY,
    double TiltDeg)                 // positive = right plus is lower (CW tilt)
{
    public static readonly PlusAnalysis NotFound = new(false, -1, -1, -1, -1, 0);

    // Normalized offset of the plus midpoint from image center.
    // Positive = midpoint is right of center = robot shifted left = strafe right.
    // Negative = midpoint is left of center = robot shifted right = strafe left.
    public double LateralOffsetNorm => Found ? (LeftPlusX + RightPlusX) / 2.0 - 0.5 : 0;

    // Normalized Y of the plus midpoint.
    public double MidY => Found ? (LeftPlusY + RightPlusY) / 2.0 : 0;
}

// Captures a camera frame via ws_img, finds the two "plus" intersections of the black grid
// lines at the front-left and front-right corners of the robot's square, and nudges the robot
// until the pluses sit symmetrically about the image center:
//   1. Rotate until the two pluses are level.
//   2. Strafe until their midpoint is horizontally centered.
//   3. Drive forward/back until they are at TargetPlusHeightNorm.
public static class GridAlignmentAgent
{
    // ── Tunable constants ─────────────────────────────────────────────────────

    // Pixels with (R+G+B)/3 below this are treated as black.
    private const int BlackLuminanceThreshold = 60;

    // Only rows below this fraction of the height are searched (0 = whole frame).
    private const double SearchTopNorm = 0.5;

    // Minimum fraction of a row/column (within the search region) that must be dark to
    // count as a grid line.
    private const double MinHorizontalLineFraction = 0.15;
    private const double MinVerticalLineFraction   = 0.15;

    // Rows/columns within ±this fraction of the image size are summed together when looking
    // for lines, so a few degrees of robot rotation doesn't smear a line below the threshold.
    private const double LineBandNorm = 0.02;

    // Half-size of the window around a rough line crossing used to locate the plus center,
    // as a fraction of image width.
    private const double PlusWindowNorm = 0.08;

    // Plus verification: each of the 4 arms is sampled from ArmGapNorm to ArmLengthNorm
    // (fractions of image width) away from the center, allowing ±ArmHalfWidthPx of drift
    // across the arm. At least MinArmFraction of the samples must be dark.
    private const double ArmGapNorm     = 0.02;
    private const double ArmLengthNorm  = 0.07;
    private const int    ArmHalfWidthPx = 4;
    private const double MinArmFraction = 0.6;

    // Correction thresholds — don't move if already within these limits.
    private const double RotationThresholdDeg  = 1.0;   // degrees
    private const double LateralThresholdNorm  = 0.04;  // fraction of image width
    private const double ForwardThresholdNorm  = 0.04;  // fraction of image height

    // Nudge distance per unit of normalized offset (mm).
    private const double LateralScaleMm = 60.0;
    private const double ForwardScaleMm = 60.0;

    // Speed used for nudge corrections (percent).
    private const int NudgeSpeed = 50;

    // Where we want the two pluses to sit vertically in the frame (0=top, 1=bottom).
    // Tune this based on camera mount position and desired centering point.
    public static double TargetPlusHeightNorm { get; set; } = 0.75;

    // ── Main entry point ─────────────────────────────────────────────────────

    // Capture image → find pluses → correct in order: rotation, lateral, forward/back.
    // Loops until aligned or maxIterations exhausted.
    public static async Task<PlusAnalysis> AlignAsync(RobotConnection robot, int maxIterations = 10)
    {
        var last = PlusAnalysis.NotFound;

        for (int i = 0; i < maxIterations; i++)
        {
            var bytes = await robot.GetCameraImageAsync();
            if (bytes == null)
            {
                Console.WriteLine($"[GridAlign] No image from {robot.Hostname}");
                return last;
            }

            var imageData = ExtractImageBytes(bytes);
            last = FindPluses(imageData);
            SaveDebugImages(imageData, robot.Hostname ?? "unknown", last);
            Console.WriteLine(
                $"[GridAlign] iter={i} found={last.Found} " +
                $"left=({last.LeftPlusX:F3},{last.LeftPlusY:F3}) " +
                $"right=({last.RightPlusX:F3},{last.RightPlusY:F3}) " +
                $"tilt={last.TiltDeg:F2}° lateral={last.LateralOffsetNorm:F3} midY={last.MidY:F3}");

            if (!last.Found) break;

            bool needsRotation = Math.Abs(last.TiltDeg) > RotationThresholdDeg;
            bool needsLateral  = Math.Abs(last.LateralOffsetNorm) > LateralThresholdNorm;
            bool needsForward  = Math.Abs(last.MidY - TargetPlusHeightNorm) > ForwardThresholdNorm;

            if (!needsRotation && !needsLateral && !needsForward) break;

            // Phase 1: resolve rotation before lateral/forward corrections are meaningful.
            if (needsRotation)
            {
                // tiltDeg > 0 = right plus lower = robot rotated CW = turn CCW (negative angle).
                await robot.SendCommandAsync(new
                {
                    cmd_id    = "turn_for",
                    angle     = -last.TiltDeg,
                    turn_rate = 50,
                    stacking_type = 0
                });
                await robot.WaitForStopAsync();
                continue;
            }

            // Phase 2: strafe until the plus midpoint is centered.
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
                await robot.WaitForStopAsync();
                continue;
            }

            // Phase 3: drive forward/back until the pluses are at target height.
            if (needsForward)
            {
                double forwardOffset = last.MidY - TargetPlusHeightNorm;
                // forwardOffset > 0 = pluses below target = robot is too far forward = move back (180°).
                // forwardOffset < 0 = pluses above target = robot hasn't reached them = move forward (0°).
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
                await robot.WaitForStopAsync();
            }
        }

        return last;
    }

    // ── Plus detection ────────────────────────────────────────────────────────

    // Find the front-left and front-right plus intersections.
    //
    // Pass 1 — row/column histograms over the search region give the rough front line
    //          (strongest row) and the two side lines (strongest column in each half).
    // Pass 2 — around each rough crossing, local row/column histograms give the plus center.
    // Pass 3 — each center must have dark arms in all four directions, or it is a T/L, not a plus.
    public static PlusAnalysis FindPluses(byte[] imageData)
    {
        try
        {
            using var img = Image.Load<Rgb24>(imageData);
            int w = img.Width, h = img.Height;
            int top = (int)(h * SearchTopNorm);

            var dark = new bool[w * h];
            var rowCounts = new int[h];
            var colCounts = new int[w];

            img.ProcessPixelRows(accessor =>
            {
                for (int y = top; y < h; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < w; x++)
                    {
                        var p = row[x];
                        if ((p.R + p.G + p.B) / 3 < BlackLuminanceThreshold)
                        {
                            dark[y * w + x] = true;
                            rowCounts[y]++;
                            colCounts[x]++;
                        }
                    }
                }
            });

            // Pass 1: rough front line and side lines, from histograms summed over a band
            // so a tilted robot's lines still peak.
            var rowBands = BandSums(rowCounts, (int)(h * LineBandNorm));
            var colBands = BandSums(colCounts, (int)(w * LineBandNorm));

            int hLineRow = PeakCenter(rowBands, top, h);
            if (rowBands[hLineRow] < w * MinHorizontalLineFraction)
                return PlusAnalysis.NotFound;

            int leftLineCol  = PeakCenter(colBands, 0,     w / 2);
            int rightLineCol = PeakCenter(colBands, w / 2, w);
            int minVertical  = (int)((h - top) * MinVerticalLineFraction);
            if (colBands[leftLineCol] < minVertical || colBands[rightLineCol] < minVertical)
                return PlusAnalysis.NotFound;

            // Passes 2 and 3: refine and verify each crossing.
            var left  = LocatePlus(dark, w, h, top, leftLineCol,  hLineRow);
            var right = LocatePlus(dark, w, h, top, rightLineCol, hLineRow);
            if (left == null || right == null)
                return PlusAnalysis.NotFound;

            var (lx, ly) = left.Value;
            var (rx, ry) = right.Value;
            double tiltDeg = Math.Atan2(ry - ly, rx - lx) * 180.0 / Math.PI;

            return new PlusAnalysis(
                Found:      true,
                LeftPlusX:  (double)lx / w,
                LeftPlusY:  (double)ly / h,
                RightPlusX: (double)rx / w,
                RightPlusY: (double)ry / h,
                TiltDeg:    tiltDeg);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GridAlign] Plus analysis error: {ex.Message}");
            return PlusAnalysis.NotFound;
        }
    }

    // Find the plus center near (roughX, roughY) and confirm it has four dark arms.
    // Returns pixel coordinates, or null if no plus is there.
    private static (int X, int Y)? LocatePlus(bool[] dark, int w, int h, int top, int roughX, int roughY)
    {
        int r  = Math.Max(4, (int)(w * PlusWindowNorm));
        int x0 = Math.Max(0, roughX - r), x1 = Math.Min(w, roughX + r + 1);
        int y0 = Math.Max(top, roughY - r), y1 = Math.Min(h, roughY + r + 1);

        var rowCounts = new int[y1 - y0];
        var colCounts = new int[x1 - x0];
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (dark[y * w + x])
                {
                    rowCounts[y - y0]++;
                    colCounts[x - x0]++;
                }

        int cy = y0 + PeakCenter(rowCounts, 0, rowCounts.Length);
        int cx = x0 + PeakCenter(colCounts, 0, colCounts.Length);

        int gap = Math.Max(1, (int)(w * ArmGapNorm));
        int len = Math.Max(gap + 1, (int)(w * ArmLengthNorm));
        bool isPlus =
            ArmIsDark(dark, w, h, top, cx, cy,  1,  0, gap, len) &&
            ArmIsDark(dark, w, h, top, cx, cy, -1,  0, gap, len) &&
            ArmIsDark(dark, w, h, top, cx, cy,  0,  1, gap, len) &&
            ArmIsDark(dark, w, h, top, cx, cy,  0, -1, gap, len);

        return isPlus ? (cx, cy) : null;
    }

    // True if enough steps along (dx, dy) from (cx, cy) have a dark pixel within
    // ±ArmHalfWidthPx across the arm. Steps outside the search region count as not dark.
    private static bool ArmIsDark(bool[] dark, int w, int h, int top, int cx, int cy, int dx, int dy, int gap, int len)
    {
        int hits = 0;
        for (int step = gap; step <= len; step++)
        {
            for (int across = -ArmHalfWidthPx; across <= ArmHalfWidthPx; across++)
            {
                int x = cx + dx * step + dy * across;
                int y = cy + dy * step + dx * across;
                if (x < 0 || x >= w || y < top || y >= h) continue;
                if (dark[y * w + x]) { hits++; break; }
            }
        }
        return hits >= (len - gap + 1) * MinArmFraction;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Save original image and an annotated copy (black pixels → red, found pluses → green)
    // to images/align/. Both files share the same timestamp so they pair up in the listing.
    private static void SaveDebugImages(byte[] imageData, string hostname, PlusAnalysis analysis)
    {
        try
        {
            var dir = Path.Combine("images", "align");
            Directory.CreateDirectory(dir);
            var safe = hostname.Replace('.', '_');
            var ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

            File.WriteAllBytes(Path.Combine(dir, $"align_{safe}_{ts}_orig.jpg"), imageData);

            using var img = Image.Load<Rgb24>(imageData);
            int w = img.Width, h = img.Height;
            int top = (int)(h * SearchTopNorm);
            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < h; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < w; x++)
                    {
                        if (y < top)
                        {
                            // Gray out ignored region so it's visually distinct.
                            var p = row[x];
                            row[x] = new Rgb24((byte)(p.R / 2), (byte)(p.G / 2), (byte)(p.B / 2));
                        }
                        else if ((row[x].R + row[x].G + row[x].B) / 3 < BlackLuminanceThreshold)
                        {
                            row[x] = new Rgb24(255, 0, 0);
                        }
                    }
                }

                if (!analysis.Found) return;
                int arm = Math.Max(4, (int)(w * ArmLengthNorm));
                foreach (var (nx, ny) in new[] { (analysis.LeftPlusX, analysis.LeftPlusY), (analysis.RightPlusX, analysis.RightPlusY) })
                {
                    int px = (int)(nx * w), py = (int)(ny * h);
                    for (int d = -arm; d <= arm; d++)
                        for (int t = -1; t <= 1; t++)
                        {
                            if (px + d >= 0 && px + d < w && py + t >= 0 && py + t < h)
                                accessor.GetRowSpan(py + t)[px + d] = new Rgb24(0, 255, 0);
                            if (px + t >= 0 && px + t < w && py + d >= 0 && py + d < h)
                                accessor.GetRowSpan(py + d)[px + t] = new Rgb24(0, 255, 0);
                        }
                }
            });
            img.Save(Path.Combine(dir, $"align_{safe}_{ts}_pluses.jpg"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GridAlign] SaveDebugImages error: {ex.Message}");
        }
    }

    // Index of the maximum value in arr[start..end).
    private static int PeakIndex(int[] arr, int start, int end)
    {
        int best = start;
        for (int i = start + 1; i < end; i++)
            if (arr[i] > arr[best]) best = i;
        return best;
    }

    // Middle of the run of values >= half the maximum around the peak in arr[start..end),
    // so a thick line reports its center instead of its first row/column.
    private static int PeakCenter(int[] arr, int start, int end)
    {
        int best = PeakIndex(arr, start, end);
        int half = arr[best] / 2;
        int lo = best, hi = best;
        while (lo > start && arr[lo - 1] >= half) lo--;
        while (hi < end - 1 && arr[hi + 1] >= half) hi++;
        return (lo + hi) / 2;
    }

    // Each entry becomes the sum of arr over ±radius, so a slightly tilted line, whose
    // dark pixels spread over several rows/columns, still forms one strong peak.
    private static int[] BandSums(int[] arr, int radius)
    {
        var sums = new int[arr.Length];
        int running = 0;
        for (int i = 0; i < arr.Length + radius; i++)
        {
            if (i < arr.Length) running += arr[i];
            if (i - 2 * radius - 1 >= 0) running -= arr[i - 2 * radius - 1];
            int center = i - radius;
            if (center >= 0 && center < arr.Length) sums[center] = running;
        }
        return sums;
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
