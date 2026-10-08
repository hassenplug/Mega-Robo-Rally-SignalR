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
    }
}
