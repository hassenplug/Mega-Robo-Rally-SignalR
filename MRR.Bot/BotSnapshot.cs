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

        /// <summary>
        /// The card types of one robot's full programming deck (MoveCardsCompleteList set 4), one
        /// entry per card. Expert deals opponents random hands from it (§5.2); empty = StandardDeck.
        /// </summary>
        public IReadOnlyList<int> DeckMix { get; init; } = StandardDeck;

        /// <summary>Deck set 4 as seeded on 2026-11-22: 20 cards.</summary>
        public static readonly IReadOnlyList<int> StandardDeck =
        [
            1,             // U-Turn
            2, 2, 2, 2,    // Right Turn
            3, 3, 3, 3,    // Left Turn
            4,             // Back 1
            5, 5, 5, 5,    // Forward 1
            6, 6, 6,       // Forward 2
            7,             // Forward 3
            8,             // Again
            9,             // Power Up
        ];
    }

    /// <summary>
    /// One computer robot to program: its level, the card types it may use (hand plus
    /// registers), and how many damage cards (Spam, Haywire) sit in its discard pile -- which
    /// shutting down would also clear. DrawPool: the card types in its deck and discard pile,
    /// which a Spam's replacement is drawn from (contents only, never their order). ChooseFacing:
    /// it rebooted and its facing is still to choose (Robots.PositionValid was 0).
    /// </summary>
    public sealed record BotSeat(int RobotID, int Level, IReadOnlyList<int> Hand, int DamageInDiscard = 0,
                                 IReadOnlyList<int>? DrawPool = null, bool ChooseFacing = false)
    {
        public BotProfile Profile => BotProfile.ForLevel(Level);

        /// <summary>Damage cards (Spam, Haywire) in the hand and registers.</summary>
        public int DamageInHand => Hand.Count(t => t == (int)MoveCard.tCardType.Spam || t == (int)MoveCard.tCardType.Haywire);
    }
}
