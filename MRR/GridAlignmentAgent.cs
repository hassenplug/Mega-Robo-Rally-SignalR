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

    // Vanishing point of the two side lines (normalized, -1 if they could not be fitted). The
    // side lines are parallel on the board, so this point moves sideways only when the robot
    // turns, not when it slides: it is the rotation measure, and positions are taken relative
    // to it so they don't include rotation.
    public double VanishingX { get; init; } = -1;
    public double VanishingY { get; init; } = -1;
    public bool HasVanishingPoint => Found && VanishingX >= 0;

    // Rotation: vanishing point's offset from image center. Positive = robot turned left.
    public double YawOffsetNorm => HasVanishingPoint ? VanishingX - 0.5 : 0;

    // Sideways position: plus midpoint relative to the vanishing point, so turning (which moves
    // both equally) doesn't change it. Positive = robot shifted left.
    public double PositionOffsetNorm => HasVanishingPoint ? (LeftPlusX + RightPlusX) / 2.0 - VanishingX : 0;

    // File name (in GridAlignmentAgent.ImageDirectory) of the marked-up image this result came
    // from, or null if none was saved. Served by GET /api/robot/alignimage/{file}.
    public string? ImageFile { get; init; }
}

// One correction step, planned from one camera frame: a single drive_for in any direction
// (sideways and forward/back combined), then a turn_for. DriveMm 0 = no drive, TurnDeg 0 = no turn.
public record AlignmentMove(int DriveMm, int DriveAngleDeg, double TurnDeg)
{
    public static readonly AlignmentMove None = new(0, 0, 0);
    public bool IsNone => DriveMm == 0 && TurnDeg == 0;
}

// Captures a camera frame via ws_img, finds the two "plus" intersections of the black grid
// lines at the front-left and front-right corners of the robot's square, and nudges the robot
// until the pluses sit symmetrically about the image center: level, midpoint horizontally
// centered, and at TargetPlusHeightNorm. Each step corrects all three from one frame (see
// PlanMove), so the robot never has to see the pluses between a move and a turn.
public static class GridAlignmentAgent
{
    // ── Tunable constants ─────────────────────────────────────────────────────

    // Pixels with (R+G+B)/3 below this fraction of the search region's median are treated as
    // black. Relative, because camera exposure varies: on AIM-07 (2026-10-29) the floor's median
    // was 103 with lines at 27-43 in one frame, and 161 with lines at 31-70 in a brighter one.
    private const double DarkFractionOfMedian = 0.7;

    // Only rows below this fraction of the height are searched (0 = whole frame). The AIM
    // camera looks nearly straight ahead, so the front line sits just below the middle.
    private const double SearchTopNorm = 0.5;

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

    // Side lines are also followed down from each plus for SideLineFitNorm of the image height
    // and fitted with a straight line, to find their vanishing point. Each row re-centers on
    // the dark pixels within ±SideLineWindowPx; rows with more than SideLineMaxDarkPx of them
    // (a blob) or none are skipped. At least MinSideLineFraction of the rows must be usable.
    private const double SideLineFitNorm     = 0.23;
    private const int    SideLineWindowPx    = 6;
    private const int    SideLineMaxDarkPx   = 12;
    private const double MinSideLineFraction = 0.3;

    // Correction thresholds — don't move if already within these limits. In mm on AIM-04:
    // lateral 0.02 ≈ 1.9 mm, forward 0.015 ≈ 2.6 mm (see the scales below). At 0.04 each, runs
    // backing up from too close stopped at the near edge of the band, ~6 mm short of center.
    private const double RotationThresholdDeg  = 1.0;   // degrees
    private const double LateralThresholdNorm  = 0.02;  // fraction of image width
    private const double ForwardThresholdNorm  = 0.015; // fraction of image height

    // Nudge distance per unit of normalized offset (mm), measured on AIM-04 (2026-11-01).
    // LateralScaleMm is per unit of PositionOffsetNorm: eight 2-4 mm sideways drives moved it
    // 0.0105/mm (67-114 mm per unit, rotation removed), so 95. ForwardScaleMm is per unit of
    // MidY: thirteen 3-7 mm backward drives moved it 0.0058/mm (52 mm, 0.303), so 172. Forward
    // drives were not measured.
    private const double LateralScaleMm = 95.0;
    private const double ForwardScaleMm = 172.0;

    // Speed used for nudge corrections (percent).
    private const int NudgeSpeed = 50;

    // How far the vanishing point moves per degree of turn_for, as a fraction of image width.
    // Nine turn-only steps on AIM-07 (2026-11-01): 4-24.5 px per degree on a 640 px frame --
    // small turns are imprecise -- median 18.6 px, so -0.029. Negative: turn_for +1° moves it left.
    private const double VanishingShiftPerTurnDeg = -0.029;

    // Fallback only (no vanishing point): LateralShiftPerTurnDeg is how far a turn slides the
    // pluses sideways in the frame, per degree of turn_for: the camera
    // looks straight ahead, so turning pans the view. Measured on AIM-07 (2026-11-01) from four
    // rotate-only steps: -0.015 to -0.017 each (e.g. turn -6.94° -> lateral 0.003 -> 0.115).
    // PlanMove adds this to the sideways correction so the pluses end up centered after the turn.
    private const double LateralShiftPerTurnDeg = -0.016;

    // Where we want the two pluses to sit vertically in the frame (0=top, 1=bottom).
    // 0.575 is where they were in the first real AIM-07 frame (2026-10-29), with the robot
    // placed by hand but not confirmed centered -- re-measure from a hand-centered robot.
    public static double TargetPlusHeightNorm { get; set; } = 0.575;

    // Where camera frames and debug images are saved. Set at startup from
    // GridAlignment:ImageDirectory in appsettings.json.
    public static string ImageDirectory { get; set; } = Path.Combine("images", "align");

    // ── Main entry point ─────────────────────────────────────────────────────

    // Capture image → find pluses → one combined drive + turn (PlanMove) → repeat.
    // Loops until aligned, the pluses are lost, or maxIterations is used up.
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
            last = last with { ImageFile = SaveDebugImages(imageData, robot.Hostname ?? "unknown", last) };
            Console.WriteLine(
                $"[GridAlign] iter={i} found={last.Found} " +
                $"left=({last.LeftPlusX:F3},{last.LeftPlusY:F3}) " +
                $"right=({last.RightPlusX:F3},{last.RightPlusY:F3}) " +
                $"tilt={last.TiltDeg:F2}° lateral={last.LateralOffsetNorm:F3} midY={last.MidY:F3} " +
                $"yaw={last.YawOffsetNorm:F3} pos={last.PositionOffsetNorm:F3} vp={last.HasVanishingPoint}");

            if (!last.Found) break;

            var move = PlanMove(last);
            if (move.IsNone) break;
            Console.WriteLine($"[GridAlign] iter={i} move: drive {move.DriveMm} mm at {move.DriveAngleDeg}°, turn {move.TurnDeg:F2}°");

            // Drive first: when the robot is too close or off to one side, turning first can
            // swing a plus out of view. The next frame is only taken after both.
            if (move.DriveMm > 0)
            {
                await robot.SendCommandAsync(new
                {
                    cmd_id        = "drive_for",
                    distance      = move.DriveMm,
                    angle         = move.DriveAngleDeg,
                    drive_speed   = NudgeSpeed,
                    turn_speed    = 0,
                    final_heading = 0,
                    stacking_type = 0
                });
                await robot.WaitForStopAsync();
            }

            if (move.TurnDeg != 0)
            {
                // Turn by the tilt itself. Negating it turned the wrong way on AIM-07 (2026-10-29):
                // tilt -8.2° -> turn_for +8.2 -> tilt -10.3°, then the pluses left the frame.
                await robot.SendCommandAsync(new
                {
                    cmd_id    = "turn_for",
                    angle     = move.TurnDeg,
                    turn_rate = 50,
                    stacking_type = 0
                });
                await robot.WaitForStopAsync();
            }
        }

        return last;
    }

    // The correction for one frame. Each of the three errors is corrected only if it is outside
    // its threshold. Sideways and forward/back combine into one drive: DriveAngleDeg is the
    // direction to travel relative to the robot (0 = forward, 90 = right, 180 = back,
    // -90 = left), the same convention as drive_for's angle.
    //
    // The turn also slides the view sideways (LateralShiftPerTurnDeg), so the sideways
    // correction is planned against where the pluses will be after the turn. Without this, a
    // robot that is close and needs a big turn can lose a plus off the edge of the frame.
    //
    // With a vanishing point (the normal case on the real camera), rotation and position are
    // measured separately: turn until the vanishing point is centered, and drive sideways by the
    // plus midpoint's offset from the vanishing point. The front line's tilt is not used: on
    // AIM-07 it read level with the robot ~1.4° turned left (vanishing point +25 px), which left
    // the robot turned left and shifted right. Without one, falls back to tilt and the predicted
    // sideways slide.
    public static AlignmentMove PlanMove(PlusAnalysis analysis)
    {
        if (!analysis.Found) return AlignmentMove.None;

        double rotationDeg = analysis.HasVanishingPoint
            ? -analysis.YawOffsetNorm / VanishingShiftPerTurnDeg
            : analysis.TiltDeg;
        double turnDeg = Math.Abs(rotationDeg) > RotationThresholdDeg ? rotationDeg : 0;
        double lateralAfterTurn = analysis.HasVanishingPoint
            ? analysis.PositionOffsetNorm
            : analysis.LateralOffsetNorm + LateralShiftPerTurnDeg * turnDeg;
        double forwardOffset = analysis.MidY - TargetPlusHeightNorm;

        // Positive = right. Midpoint right of center = robot shifted left = move right.
        double rightMm = Math.Abs(lateralAfterTurn) > LateralThresholdNorm
            ? lateralAfterTurn * LateralScaleMm : 0;
        // Positive = forward. forwardOffset > 0 = pluses below target = robot too far forward
        // = move back.
        double forwardMm = Math.Abs(forwardOffset) > ForwardThresholdNorm
            ? -forwardOffset * ForwardScaleMm : 0;

        // drive_for with distance 0 drives without stopping, so a drive that rounds to 0 mm is
        // dropped rather than sent.
        int driveMm = (int)Math.Round(Math.Sqrt(rightMm * rightMm + forwardMm * forwardMm));
        int angleDeg = driveMm > 0 ? (int)Math.Round(Math.Atan2(rightMm, forwardMm) * 180.0 / Math.PI) : 0;

        return new AlignmentMove(driveMm, angleDeg, turnDeg);
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

            var result = new PlusAnalysis(
                Found:      true,
                LeftPlusX:  lx / w,
                LeftPlusY:  ly / h,
                RightPlusX: rx / w,
                RightPlusY: ry / h,
                TiltDeg:    tiltDeg);

            // Vanishing point: where the two side lines, followed down from the pluses, meet.
            var leftLine  = FitSideLine(dark, w, h, lx, ly, gap);
            var rightLine = FitSideLine(dark, w, h, rx, ry, gap);
            if (leftLine == null || rightLine == null) return result;
            var (la, lb) = leftLine.Value;
            var (ra, rb) = rightLine.Value;
            if (Math.Abs(lb - rb) < 1e-3) return result;   // parallel in the image: no perspective
            double vy = (ra - la) / (lb - rb);
            double vx = la + lb * vy;
            if (vx < 0 || vx >= w || vy >= Math.Min(ly, ry)) return result;   // not ahead of the pluses
            return result with { VanishingX = vx / w, VanishingY = vy / h };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GridAlign] Plus analysis error: {ex.Message}");
            return PlusAnalysis.NotFound;
        }
    }

    // Follow a side line down from its plus at (px, py) and fit x = a + b·y through the dark-run
    // centers. Returns null if too few rows were usable.
    private static (double A, double B)? FitSideLine(bool[] dark, int w, int h, double px, double py, int gap)
    {
        int y0 = (int)py + gap, y1 = Math.Min(h - 1, (int)py + (int)(h * SideLineFitNorm));
        var ys = new List<double>();
        var xs = new List<double>();
        double cx = px;
        for (int y = y0; y <= y1; y++)
        {
            int sum = 0, count = 0;
            for (int x = (int)cx - SideLineWindowPx; x <= (int)cx + SideLineWindowPx; x++)
                if (x >= 0 && x < w && dark[y * w + x]) { sum += x; count++; }
            if (count == 0 || count > SideLineMaxDarkPx) continue;
            cx = (double)sum / count;
            xs.Add(cx);
            ys.Add(y);
        }
        if (xs.Count < (y1 - y0 + 1) * MinSideLineFraction || xs.Count < 3) return null;

        double meanY = ys.Average(), meanX = xs.Average();
        double syy = 0, sxy = 0;
        for (int i = 0; i < xs.Count; i++)
        {
            syy += (ys[i] - meanY) * (ys[i] - meanY);
            sxy += (ys[i] - meanY) * (xs[i] - meanX);
        }
        if (syy < 1e-9) return null;
        double b = sxy / syy;
        return (meanX - b * meanY, b);
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
    // Returns the annotated copy's file name, or null if saving failed.
    private static string? SaveDebugImages(byte[] imageData, string hostname, PlusAnalysis analysis)
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
                if (analysis.HasVanishingPoint)
                {
                    // Fitted side lines, drawn from each plus up to the vanishing point.
                    double vx = analysis.VanishingX * w, vy = analysis.VanishingY * h;
                    foreach (var (nx, ny) in new[] { (analysis.LeftPlusX, analysis.LeftPlusY), (analysis.RightPlusX, analysis.RightPlusY) })
                    {
                        double px = nx * w, py = ny * h;
                        int steps = (int)Math.Max(Math.Abs(px - vx), Math.Abs(py - vy));
                        for (int k = 0; k <= steps; k++)
                        {
                            int x = (int)Math.Round(px + (vx - px) * k / Math.Max(1, steps));
                            int y = (int)Math.Round(py + (vy - py) * k / Math.Max(1, steps));
                            if (x >= 0 && x < w && y >= 0 && y < h) accessor.GetRowSpan(y)[x] = new Rgb24(255, 255, 0);
                        }
                    }
                }
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
            var file = $"align_{safe}_{ts}_pluses.jpg";
            img.Save(Path.Combine(dir, file));
            return file;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GridAlign] SaveDebugImages error: {ex.Message}");
            return null;
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
