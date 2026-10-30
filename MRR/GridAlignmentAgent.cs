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

    // Pixels with (R+G+B)/3 below this fraction of the search region's median are treated as
    // black. Relative, because camera exposure varies: on AIM-07 (2026-10-29) the floor's median
    // was 103 with lines at 27-43 in one frame, and 161 with lines at 31-70 in a brighter one.
    private const double DarkFractionOfMedian = 0.7;

    // Only rows below this fraction of the height are searched (0 = whole frame). The AIM
    // camera looks nearly straight ahead, so the front line sits just below the middle.
    private const double SearchTopNorm = 0.4;

    // The front line must have at least this many fully-dark rows' worth of dark pixels in
    // each third of the image width (summed over ±LineBandNorm). Requiring all three thirds
    // keeps a dark blob or a slanted side line in one part of the frame from counting.
    private const double MinLineRowsPerThird = 3.0;

    // Rows within ±this fraction of the height are summed together when looking for the
    // front line, so it still scores as one line if not perfectly straight.
    private const double LineBandNorm = 0.02;

    // Largest front-line tilt (robot rotation, roughly) searched for. Beyond this the
    // pluses are not found and alignment does nothing; run IMU heading correction first.
    private const double MaxTiltDeg = 10.0;

    // Side lines are found as dark runs crossing a curve ArmGapNorm (fraction of width) above
    // and below the front line. Runs wider than MaxSideLineWidthNorm are blobs, not lines.
    // Above and below runs pair up if they are within MaxSideLineSlope px across per px of
    // gap -- perspective makes side lines slant, about 1 px/px at the frame edges.
    private const double ArmGapNorm           = 0.015;
    private const double MaxSideLineWidthNorm = 0.08;
    private const double MaxSideLineSlope     = 1.5;

    // Each side-line arm is followed for ArmLengthNorm (fraction of width) away from the front
    // line, re-centering on the dark run each row within ±TrackWindowPx so slanted lines can be
    // followed. At least MinArmFraction of the rows must have a dark pixel. Arms above the
    // line are farther away and fade quickly, so this stays short.
    private const double ArmLengthNorm  = 0.025;
    private const int    TrackWindowPx  = 4;
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
    // 0.575 is where they were in the first real AIM-07 frame (2026-10-29), with the robot
    // placed by hand but not confirmed centered -- re-measure from a hand-centered robot.
    public static double TargetPlusHeightNorm { get; set; } = 0.575;

    // Where camera frames and debug images are saved. Set at startup from
    // GridAlignment:ImageDirectory in appsettings.json.
    public static string ImageDirectory { get; set; } = Path.Combine("images", "align");

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
                // Turn by the tilt itself. Negating it turned the wrong way on AIM-07 (2026-10-29):
                // tilt -8.2° -> turn_for +8.2 -> tilt -10.3°, then the pluses left the frame.
                await robot.SendCommandAsync(new
                {
                    cmd_id    = "turn_for",
                    angle     = last.TiltDeg,
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
    // The AIM camera looks nearly straight ahead, so in the frame the front line is close to
    // horizontal but the side lines slant toward the horizon (perspective).
    //
    // Pass 1 — the front line is the lowest row band that is dark across all three thirds of
    //          the width; a least-squares fit through it gives its tilt.
    // Pass 2 — side lines are dark runs crossing curves just above and just below the front
    //          line. A plus is an above run and a below run that line up (a T or L has only one).
    //          Each run is followed away from the front line to confirm it is a line.
    // Pass 3 — the two pluses nearest the image center on each side are the front corners.
    public static PlusAnalysis FindPluses(byte[] imageData)
    {
        try
        {
            using var img = Image.Load<Rgb24>(imageData);
            int w = img.Width, h = img.Height;
            int top = (int)(h * SearchTopNorm);
            int third = w / 3;

            var dark = new bool[w * h];
            var thirdCounts = new int[3][] { new int[h], new int[h], new int[h] };
            int darkBelow = DarkThreshold(img, top);

            img.ProcessPixelRows(accessor =>
            {
                for (int y = top; y < h; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < w; x++)
                    {
                        var p = row[x];
                        if ((p.R + p.G + p.B) / 3 < darkBelow)
                        {
                            dark[y * w + x] = true;
                            thirdCounts[Math.Min(x / third, 2)][y]++;
                        }
                    }
                }
            });

            // Pass 1: front line. Score each row by its weakest third, trying tilts up to
            // MaxTiltDeg (each third is looked up at the row the tilted line would cross it)
            // and keeping the best.
            int band = Math.Max(1, (int)(h * LineBandNorm));
            var bands = thirdCounts.Select(c => BandSums(c, band)).ToArray();
            double maxSlope = Math.Tan(MaxTiltDeg * Math.PI / 180);
            double slopeStep = (double)band / third;
            var score = new int[h];
            var bestSlope = new double[h];
            for (int y = top; y < h; y++)
                for (double slope = -maxSlope; slope <= maxSlope + 1e-9; slope += slopeStep)
                {
                    int weakest = int.MaxValue;
                    for (int t = 0; t < 3; t++)
                    {
                        int ty = y + (int)Math.Round(slope * ((t + 0.5) * third - w / 2.0));
                        weakest = Math.Min(weakest, ty >= 0 && ty < h ? bands[t][ty] : 0);
                    }
                    if (weakest > score[y]) { score[y] = weakest; bestSlope[y] = slope; }
                }

            int minScore = (int)(third * MinLineRowsPerThird);
            int lowest = -1;
            for (int y = h - 1; y >= top && lowest < 0; y--)
                if (score[y] >= minScore) lowest = y;
            if (lowest < 0)
                return PlusAnalysis.NotFound;

            int hLineRow = PeakCenter(score, Math.Max(top, lowest - 4 * band), lowest + 1);
            // Fit twice, starting from the tilt that scored best, so the ends of a tilted line
            // aren't cut off by a band that doesn't follow it.
            double guessSlope = bestSlope[hLineRow];
            var (lineSlope, lineAt0) = FitLine(dark, w, h, top, guessSlope, hLineRow - guessSlope * w / 2.0, 2 * band);
            (lineSlope, lineAt0) = FitLine(dark, w, h, top, lineSlope, lineAt0, 2 * band);
            double LineY(double x) => lineAt0 + lineSlope * x;

            // Pass 2: side-line crossings.
            int gap = Math.Max(2, (int)(w * ArmGapNorm));
            int len = Math.Max(2, (int)(w * ArmLengthNorm));
            int maxRun = (int)(w * MaxSideLineWidthNorm);

            var below = SideLineRuns(dark, w, h, top, x => LineY(x) + gap, maxRun)
                .Where(x => ArmIsLine(dark, w, h, top, x, (int)Math.Round(LineY(x) + gap), +1, len))
                .ToList();
            var above = SideLineRuns(dark, w, h, top, x => LineY(x) - gap, maxRun)
                .Where(x => ArmIsLine(dark, w, h, top, x, (int)Math.Round(LineY(x) - gap), -1, len))
                .ToList();

            var pluses = new List<double>();
            foreach (var xb in below)
            {
                var match = above.Where(xa => Math.Abs(xa - xb) <= 2 * gap * MaxSideLineSlope)
                                 .OrderBy(xa => Math.Abs(xa - xb))
                                 .Select(xa => (double?)xa)
                                 .FirstOrDefault();
                // A straight line through the plus crosses the two curves equally far either
                // side of where it crosses the front line.
                if (match != null) pluses.Add((match.Value + xb) / 2.0);
            }

            // Pass 3: nearest plus on each side of center.
            double center = w / 2.0;
            var leftX  = pluses.Where(x => x <  center).Select(x => (double?)x).Max();
            var rightX = pluses.Where(x => x >= center).Select(x => (double?)x).Min();
            if (leftX == null || rightX == null)
                return PlusAnalysis.NotFound;

            double lx = leftX.Value,  ly = LineY(lx);
            double rx = rightX.Value, ry = LineY(rx);
            double tiltDeg = Math.Atan2(ry - ly, rx - lx) * 180.0 / Math.PI;

            return new PlusAnalysis(
                Found:      true,
                LeftPlusX:  lx / w,
                LeftPlusY:  ly / h,
                RightPlusX: rx / w,
                RightPlusY: ry / h,
                TiltDeg:    tiltDeg);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GridAlign] Plus analysis error: {ex.Message}");
            return PlusAnalysis.NotFound;
        }
    }

    // Least-squares fit y = at0 + slope·x through the dark-pixel centroid of each of 16
    // vertical slices, within ±radius rows of the guess line y = guessAt0 + guessSlope·x.
    // Returns the guess unchanged if fewer than 3 slices have data.
    private static (double Slope, double At0) FitLine(bool[] dark, int w, int h, int top, double guessSlope, double guessAt0, int radius)
    {
        const int numSlices = 16;
        int sliceW = Math.Max(1, w / numSlices);

        double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
        int n = 0;
        for (int s = 0; s < numSlices; s++)
        {
            double sy = 0; int count = 0;
            int guessY = (int)Math.Round(guessAt0 + guessSlope * (s + 0.5) * sliceW);
            int yMin = Math.Max(top, guessY - radius), yMax = Math.Min(h - 1, guessY + radius);
            for (int y = yMin; y <= yMax; y++)
                for (int x = s * sliceW; x < Math.Min(w, (s + 1) * sliceW); x++)
                    if (dark[y * w + x]) { sy += y; count++; }
            // A slice crossed only by a side line has a few dark pixels per row; the front
            // line contributes a full slice width. Skip slices without at least one row's worth.
            if (count < sliceW) continue;
            double cx = (s + 0.5) * sliceW, cy = sy / count;
            sumX += cx; sumY += cy; sumXX += cx * cx; sumXY += cx * cy;
            n++;
        }
        if (n < 3) return (guessSlope, guessAt0);
        double denom = n * sumXX - sumX * sumX;
        if (Math.Abs(denom) < 1e-10) return (guessSlope, guessAt0);
        double slope = (n * sumXY - sumX * sumY) / denom;
        return (slope, (sumY - slope * sumX) / n);
    }

    // Centers of the dark runs, at most maxRun wide, along the curve y = curveY(x).
    private static List<int> SideLineRuns(bool[] dark, int w, int h, int top, Func<double, double> curveY, int maxRun)
    {
        var centers = new List<int>();
        int start = -1;
        for (int x = 0; x <= w; x++)
        {
            int y = x < w ? (int)Math.Round(curveY(x)) : -1;
            bool isDark = x < w && y >= top && y < h && dark[y * w + x];
            if (isDark && start < 0) start = x;
            else if (!isDark && start >= 0)
            {
                if (x - start <= maxRun) centers.Add((start + x - 1) / 2);
                start = -1;
            }
        }
        return centers;
    }

    // Follow a side line from (x, y) for len rows in direction dy (+1 down, -1 up), re-centering
    // on the dark pixels within ±TrackWindowPx each row so a slanted line can be followed.
    // True if at least MinArmFraction of the rows had a dark pixel.
    private static bool ArmIsLine(bool[] dark, int w, int h, int top, int x, int y, int dy, int len)
    {
        int hits = 0;
        double cx = x;
        for (int step = 1; step <= len; step++)
        {
            int row = y + dy * step;
            if (row < top || row >= h) break;
            int sum = 0, count = 0;
            for (int px = (int)cx - TrackWindowPx; px <= (int)cx + TrackWindowPx; px++)
                if (px >= 0 && px < w && dark[row * w + px]) { sum += px; count++; }
            if (count == 0) continue;
            cx = (double)sum / count;
            hits++;
        }
        return hits >= len * MinArmFraction;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Save original image and an annotated copy (black pixels → red, found pluses → green)
    // to ImageDirectory. Both files share the same timestamp so they pair up in the listing.
    private static void SaveDebugImages(byte[] imageData, string hostname, PlusAnalysis analysis)
    {
        try
        {
            var dir = ImageDirectory;
            Directory.CreateDirectory(dir);
            var safe = hostname.Replace('.', '_');
            var ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

            //File.WriteAllBytes(Path.Combine(dir, $"align_{safe}_{ts}_orig.jpg"), imageData);

            using var img = Image.Load<Rgb24>(imageData);
            int w = img.Width, h = img.Height;
            int top = (int)(h * SearchTopNorm);
            int darkBelow = DarkThreshold(img, top);
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
                        else if ((row[x].R + row[x].G + row[x].B) / 3 < darkBelow)
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

    // Brightness below which a pixel counts as black: DarkFractionOfMedian of the median
    // (R+G+B)/3 over rows top..bottom.
    private static int DarkThreshold(Image<Rgb24> img, int top)
    {
        var histogram = new int[256];
        int total = 0;
        img.ProcessPixelRows(accessor =>
        {
            for (int y = top; y < accessor.Height; y++)
                foreach (var p in accessor.GetRowSpan(y))
                {
                    histogram[(p.R + p.G + p.B) / 3]++;
                    total++;
                }
        });
        int seen = 0, median = 0;
        while (median < 255 && seen + histogram[median] < (total + 1) / 2)
            seen += histogram[median++];
        return (int)(median * DarkFractionOfMedian);
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
