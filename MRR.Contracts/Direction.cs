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
}
