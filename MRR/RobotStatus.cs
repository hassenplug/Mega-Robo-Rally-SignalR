using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace MRR
{
    public class RobotStatus
    {
        [JsonPropertyName("controller")]
        public ControllerSection Controller { get; set; } = new();

        [JsonPropertyName("robot")]
        public RobotSection Robot { get; set; } = new();

        [JsonPropertyName("aivision")]
        public AiVisionSection AiVision { get; set; } = new();

        // ── Controller ───────────────────────────────────────────────────────

        public class ControllerSection
        {
            [JsonPropertyName("flags")]
            public string Flags { get; set; } = "0x0000";

            [JsonPropertyName("stick_x")]
            public int StickX { get; set; }

            [JsonPropertyName("stick_y")]
            public int StickY { get; set; }

            [JsonPropertyName("battery")]
            public int Battery { get; set; }
        }

        // ── Robot ────────────────────────────────────────────────────────────

        public class RobotSection
        {
            [JsonPropertyName("flags")]
            public string Flags { get; set; } = "0x0";

            [JsonPropertyName("battery")]
            public int Battery { get; set; }

            [JsonPropertyName("touch_flags")]
            public string TouchFlags { get; set; } = "0x0000";

            [JsonPropertyName("touch_x")]
            public int TouchX { get; set; }

            [JsonPropertyName("touch_y")]
            public int TouchY { get; set; }

            [JsonPropertyName("robot_x")]
            public string RobotXStr { get; set; } = "0";

            [JsonPropertyName("robot_y")]
            public string RobotYStr { get; set; } = "0";

            [JsonPropertyName("roll")]
            public string RollStr { get; set; } = "0";

            [JsonPropertyName("pitch")]
            public string PitchStr { get; set; } = "0";

            [JsonPropertyName("yaw")]
            public string YawStr { get; set; } = "0";

            [JsonPropertyName("heading")]
            public string HeadingStr { get; set; } = "0";

            [JsonPropertyName("rotation")]
            public string RotationStr { get; set; } = "0";

            [JsonPropertyName("acceleration")]
            public Vector3Section Acceleration { get; set; } = new();

            [JsonPropertyName("gyro_rate")]
            public Vector3Section GyroRate { get; set; } = new();

            [JsonPropertyName("screen")]
            public ScreenSection Screen { get; set; } = new();

            private static double Parse(string s) =>
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

            public double RobotX   => Parse(RobotXStr);
            public double RobotY   => Parse(RobotYStr);
            public double Roll     => Parse(RollStr);
            public double Pitch    => Parse(PitchStr);
            public double Yaw      => Parse(YawStr);
            public double Heading  => Parse(HeadingStr);
            public double Rotation => Parse(RotationStr);

            public double DistToOrigin => Math.Sqrt(RobotX * RobotX + RobotY * RobotY);
            public double DirToOrigin  => Math.Atan2(-RobotY, -RobotX) * 180.0 / Math.PI;
            public bool   isMoving     => (Convert.ToUInt32(Flags, 16) & 0xFF) != 0;
        }

        // ── Shared sub-sections ──────────────────────────────────────────────

        public class Vector3Section
        {
            [JsonPropertyName("x")]
            public string XStr { get; set; } = "0";

            [JsonPropertyName("y")]
            public string YStr { get; set; } = "0";

            [JsonPropertyName("z")]
            public string ZStr { get; set; } = "0";

            private static double Parse(string s) =>
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

            public double X => Parse(XStr);
            public double Y => Parse(YStr);
            public double Z => Parse(ZStr);
        }

        public class ScreenSection
        {
            [JsonPropertyName("row")]
            public string RowStr { get; set; } = "0";

            [JsonPropertyName("column")]
            public string ColumnStr { get; set; } = "0";

            public int Row    => int.TryParse(RowStr,    out var v) ? v : 0;
            public int Column => int.TryParse(ColumnStr, out var v) ? v : 0;
        }

        // ── AI Vision ────────────────────────────────────────────────────────

        public class AiVisionSection
        {
            [JsonPropertyName("classnames")]
            public NamedList<ClassNameItem> Classnames { get; set; } = new();

            [JsonPropertyName("objects")]
            public NamedList<VisionObject> Objects { get; set; } = new();
        }

        public class NamedList<T>
        {
            [JsonPropertyName("count")]
            public int Count { get; set; }

            [JsonPropertyName("items")]
            public List<T> Items { get; set; } = new();
        }

        public class ClassNameItem
        {
            [JsonPropertyName("index")]
            public int Index { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; } = "";
        }

        public class VisionObject
        {
            [JsonPropertyName("index")]
            public int Index { get; set; }

            [JsonPropertyName("classname")]
            public string Classname { get; set; } = "";

            [JsonPropertyName("x")]
            public int X { get; set; }

            [JsonPropertyName("y")]
            public int Y { get; set; }

            [JsonPropertyName("width")]
            public int Width { get; set; }

            [JsonPropertyName("height")]
            public int Height { get; set; }
        }
    }
}
