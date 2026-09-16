namespace MRR.Tools.FirmwareCompare;

public sealed record TestCommand(string Name, object Payload, bool IsMotion);

/// <summary>
/// The command sequence sent identically to both robots. Command shapes follow
/// .claude/agents/aim-robot-api.md exactly (field names, defaults, the @string-as-`string`
/// quirk, the dynamic LED key, etc).
/// </summary>
public static class TestCommands
{
    public static List<TestCommand> Build(bool includeMotion)
    {
        var commands = new List<TestCommand>
        {
            new("imu_calibrate", new { cmd_id = "imu_calibrate" }, false),
            new("set_pose", new { cmd_id = "set_pose", x = 0, y = 0 }, false),
            new("lcd_clear_screen", new { cmd_id = "lcd_clear_screen", r = 0, g = 0, b = 80 }, false),
            new("lcd_set_font", new { cmd_id = "lcd_set_font", fontname = "mono20" }, false),
            new("lcd_set_pen_color", new { cmd_id = "lcd_set_pen_color", r = 255, g = 255, b = 255 }, false),
            new("lcd_set_fill_color", new { cmd_id = "lcd_set_fill_color", r = 0, g = 0, b = 80, transparent = false }, false),
            new("lcd_print_at", new { cmd_id = "lcd_print_at", @string = "FW TEST", x = 20, y = 100, b_opaque = true }, false),
            new("light_set_green", Led("all", 0, 255, 0), false),
            new("light_set_off", Led("all", 0, 0, 0), false),
            new("play_sound", new { cmd_id = "play_sound", name = "doorbell", volume = 40 }, false),
            new("color_detection_on", new { cmd_id = "color_detection", enable = true, merge = false }, false),
            new("tag_detection_on", new { cmd_id = "tag_detection", enable = true }, false),
            new("model_detection_off", new { cmd_id = "model_detection", enable = false }, false),
            new("imu_set_crash_threshold", new { cmd_id = "imu_set_crash_threshold", sensitivity = 1 }, false),
        };

        if (includeMotion)
        {
            // Physically moves the robot -- opt-in only (--move). Note the drive_for
            // pitfall from aim-robot-api.md: distance=0 means "drive indefinitely", not
            // "don't move", so these use a real (small) distance/angle and a matching
            // return trip rather than a supposedly-safe zero value.
            commands.Add(new("turn_for_right_90", new { cmd_id = "turn_for", angle = 90, turn_rate = 120, stacking_type = 0 }, true));
            commands.Add(new("turn_for_left_90", new { cmd_id = "turn_for", angle = -90, turn_rate = 120, stacking_type = 0 }, true));
            commands.Add(new("drive_for_forward_1sq", new { cmd_id = "drive_for", distance = 77, angle = 0, drive_speed = 100, turn_speed = 0, final_heading = 0, stacking_type = 0 }, true));
            commands.Add(new("drive_for_back_1sq", new { cmd_id = "drive_for", distance = 77, angle = 180, drive_speed = 100, turn_speed = 0, final_heading = 0, stacking_type = 0 }, true));
            commands.Add(new("kick_soft", new { cmd_id = "kick_soft" }, false));
        }

        return commands;
    }

    private static Dictionary<string, object> Led(string led, int r, int g, int b) =>
        new()
        {
            ["cmd_id"] = "light_set",
            [led] = new { r, g, b },
        };
}
