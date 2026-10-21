namespace MRR
{
    /// <summary>
    /// Board facing. Values are persisted (Robots.CurrentPosDir, BoardItems.Rotation),
    /// so the numbers are part of the schema -- do not renumber.
    /// Rotation maths lives in RotationFunctions, which stays in the host because one of
    /// its helpers still takes a Player; it moves here once Player splits into PlayerState.
    /// </summary>
    public enum Direction
    {
        None = 0,
        Up = 1,
        Right = 2,
        Down = 3,
        Left = 4
    }

    public static class DirectionExtensions
    {
        /// <summary>The direction as an arrow character ("?" for None or anything unknown).</summary>
        public static string Arrow(this Direction direction) => direction switch
        {
            Direction.Up => "↑",
            Direction.Right => "→",
            Direction.Down => "↓",
            Direction.Left => "←",
            _ => "?",
        };

        /// <summary>
        /// Seat-arrow characters, indexed by quarter-turns clockwise from the robot's facing:
        /// up, right, down, left. Plain ASCII because the robot screen fonts have no arrow glyphs
        /// (see MRR.Tools.FontTest). DataService.SeatArrowSql builds Robots.SeatArrow from these too.
        /// </summary>
        public const string SeatArrowChars = "^>v<";

        /// <summary>
        /// The arrow shown on the robot's screen pointing toward its player, as
        /// (6 - adjustment - facing) mod 4 quarter-turns clockwise from the top of the screen.
        /// DirectionAdjustment is SeatOrientation.Direction -- the same value the phone adds to a
        /// facing to show it from the player's side (js/loadrobots.js renderDirectionArrow) -- not
        /// simply the board direction the seat looks in; the formula was checked on the live
        /// table for seats with DirectionAdjustment 2 and 3. Empty until both are known.
        /// </summary>
        public static string SeatArrow(this Direction facing, int directionAdjustment)
        {
            if (facing < Direction.Up || facing > Direction.Left || directionAdjustment < 1 || directionAdjustment > 4) return "";
            return SeatArrowChars[(10 - directionAdjustment - (int)facing) % 4].ToString();
        }
    }
}
