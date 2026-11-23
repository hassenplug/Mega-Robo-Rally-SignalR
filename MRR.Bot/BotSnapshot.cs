namespace MRR.Bot
{
    /// <summary>
    /// The game as the brain sees it, taken once when programming starts. Read-only for the
    /// brain: the planner deep-copies Players itself and never changes the board (M0 tests,
    /// BotM0AssumptionTests), and every plan gets its own cards and draw piles.
    ///
    /// Players holds every robot, with no cards: Levels 2-4 model the other robots as
    /// stationary obstacles that can be pushed and can shoot (§5.1).
    /// </summary>
    public sealed class BotSnapshot
    {
        public required BoardElementCollection Board { get; init; }
        public required PlayerStates Players { get; init; }
        public int PhaseCount { get; init; } = 5;
        public int TotalFlags { get; init; }
        public int LaserDamage { get; init; } = 1;
        public bool FieldEnclosed { get; init; }
        public GameTypes GameType { get; init; }
    }

    /// <summary>
    /// One computer robot to program: its level, the card types it may use (hand plus
    /// registers), and how many damage cards (Spam, Haywire) sit in its discard pile -- which
    /// shutting down would also clear.
    /// </summary>
    public sealed record BotSeat(int RobotID, int Level, IReadOnlyList<int> Hand, int DamageInDiscard = 0)
    {
        public BotProfile Profile => BotProfile.ForLevel(Level);

        /// <summary>Damage cards (Spam, Haywire) in the hand and registers.</summary>
        public int DamageInHand => Hand.Count(t => t == (int)MoveCard.tCardType.Spam || t == (int)MoveCard.tCardType.Haywire);
    }
}
