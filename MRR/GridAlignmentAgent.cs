using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;

namespace MRR;

// Result of one image analysis pass.
// OffsetX: positive = more black on right  = robot shifted right  = needs to strafe left
// OffsetY: positive = more black on top    = robot shifted forward = needs to move back
public record GridAlignmentResult(
    bool HasLines,
    double OffsetX,
    double OffsetY,
    int BlackPixelCount,
    bool IsAligned
);

// Captures a camera frame from the AIM robot's ws_img channel, detects the black
// grid lines printed between board squares, and nudges the robot until it is centered.
public static class GridAlignmentAgent
{
    // Pixels with (R+G+B)/3 below this are counted as "black"
    private const int BlackLuminanceThreshold = 60;

    // Normalised offset below this in both axes = aligned
    private const double AlignedThreshold = 0.05;

    // At least this many black pixels must be present to consider lines detected
    private const int MinBlackPixels = 100;

    // How far to nudge per correction iteration (mm)
    private const int NudgeDistanceMm = 10;

    // Speed used for nudge corrections (percent)
    private const int NudgeSpeed = 50;

    // Max ms to wait for a nudge move to complete
    private const int MotionTimeoutMs = 3000;

    // Capture image, analyze, correct, repeat until aligned or iterations exhausted.
    public static async Task<GridAlignmentResult> AlignAsync(Player robot, int maxIterations = 5)
    {
        var last = new GridAlignmentResult(false, 0, 0, 0, false);

        for (int i = 0; i < maxIterations; i++)
        {
            var bytes = await robot.GetCameraImageAsync();
            if (bytes == null)
            {
                Console.WriteLine($"[GridAlign] No image received from {robot.IPAddress}");
                return last;
            }

            last = AnalyzeImage(bytes);
            Console.WriteLine($"[GridAlign] iter={i} px={last.BlackPixelCount} dx={last.OffsetX:F3} dy={last.OffsetY:F3} aligned={last.IsAligned}");

            if (!last.HasLines || last.IsAligned)
                break;

            await ApplyCorrectionAsync(robot, last);
        }

        return last;
    }

    // Decode the image bytes and count black pixels in each quadrant to compute offsets.
    // Handles raw JPEG bytes as well as JSON payloads with a base64-encoded image field.
    public static GridAlignmentResult AnalyzeImage(byte[] imageData)
    {
        var jpegBytes = ExtractImageBytes(imageData);
        try
        {
            using var img = Image.Load<Rgb24>(jpegBytes);
            int midX = img.Width / 2;
            int midY = img.Height / 2;
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
            if (total < MinBlackPixels)
                return new GridAlignmentResult(false, 0, 0, total, false);

            double dx = (double)(rightBlack - leftBlack) / total;
            double dy = (double)(topBlack - bottomBlack) / total;
            bool aligned = Math.Abs(dx) < AlignedThreshold && Math.Abs(dy) < AlignedThreshold;

            return new GridAlignmentResult(true, dx, dy, total, aligned);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GridAlign] Image parse error: {ex.Message}");
            return new GridAlignmentResult(false, 0, 0, 0, false);
        }
    }

    // If the payload is a JSON object with a base64 image field, extract and decode it.
    // Otherwise return the bytes unchanged (assumed to be raw JPEG).
    private static byte[] ExtractImageBytes(byte[] data)
    {
        if (data.Length > 0 && data[0] == '{')
        {
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                foreach (var key in new[] { "image", "data", "frame", "jpeg" })
                {
                    if (root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
                    {
                        var b64 = el.GetString();
                        if (b64 != null) return Convert.FromBase64String(b64);
                    }
                }
            }
            catch { }
        }
        return data;
    }

    // Send small drive_for corrections to move the robot toward center.
    private static async Task ApplyCorrectionAsync(Player robot, GridAlignmentResult r)
    {
        if (Math.Abs(r.OffsetX) >= AlignedThreshold)
        {
            // More black on right → robot shifted right → strafe left (angle −90)
            // More black on left  → robot shifted left  → strafe right (angle +90)
            int angle = r.OffsetX > 0 ? -90 : 90;
            await robot.SendCommandAsync(new
            {
                cmd_id = "drive_for",
                distance = NudgeDistanceMm,
                angle,
                drive_speed = NudgeSpeed,
                turn_speed = 0,
                final_heading = 0,
                stacking_type = 0
            });
            await robot.WaitForMotionCompleteAsync(MotionTimeoutMs);
        }

        if (Math.Abs(r.OffsetY) >= AlignedThreshold)
        {
            // More black on top    → robot shifted forward → move backward (angle 180)
            // More black on bottom → robot shifted back    → move forward  (angle   0)
            int angle = r.OffsetY > 0 ? 180 : 0;
            await robot.SendCommandAsync(new
            {
                cmd_id = "drive_for",
                distance = NudgeDistanceMm,
                angle,
                drive_speed = NudgeSpeed,
                turn_speed = 0,
                final_heading = 0,
                stacking_type = 0
            });
            await robot.WaitForMotionCompleteAsync(MotionTimeoutMs);
        }
    }
}
