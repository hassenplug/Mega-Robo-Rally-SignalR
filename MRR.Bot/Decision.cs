namespace MRR.Bot
{
    /// <summary>
    /// What a computer player decided: one card type per register (register 1 first) and,
    /// when the robot's facing still needs choosing (Robots.PositionValid == 0, after a reboot),
    /// the facing to confirm. The host applies it whole or not at all
    /// (DataService.SetRobotProgram).
    /// </summary>
    public sealed record Decision(IReadOnlyList<int> CardTypes, int? Facing);
}
