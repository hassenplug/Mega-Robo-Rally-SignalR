namespace MRR.Bot
{
    /// <summary>
    /// Level 1 (Beginner): a random legal program, no planning. Also the placeholder every
    /// computer gets the moment programming starts, and the fallback if anything better fails
    /// (COMPUTER_PLAYER_DESIGN.md §5.0, §5.7 step 1).
    ///
    /// Legal means: exactly PhaseCount cards, each taken from the hand, never more of a type
    /// than the hand holds. Within that it avoids two choices that only waste a register:
    /// Spam (played only when the hand has too few other cards), and Again in register 1
    /// (the planner treats it as Spam there, CreateCommands.cs).
    /// </summary>
    public static class RandomBrain
    {
        /// <param name="handCardTypes">Card type IDs in the robot's hand (CardLocation 1), one entry per card.</param>
        /// <param name="phaseCount">Registers to fill (CurrentGameData PhaseCount).</param>
        /// <param name="currentFacing">The robot's facing now; confirmed as is when <paramref name="facingNeeded"/>.</param>
        /// <param name="facingNeeded">True when PositionValid == 0, so the facing must be confirmed for the robot to be Ready.</param>
        /// <param name="rng">Source of randomness; pass a seeded one for repeatable results.</param>
        /// <returns>
        /// A Decision with min(phaseCount, hand size) card types. Fewer than phaseCount only when
        /// the hand is short, which the host logs; the robot then stays not-Ready, as a human
        /// with the same hand would.
        /// </returns>
        public static Decision Decide(IReadOnlyList<int> handCardTypes, int phaseCount,
                                      int currentFacing, bool facingNeeded, Random rng)
        {
            const int spam  = (int)MoveCard.tCardType.Spam;
            const int again = (int)MoveCard.tCardType.Again;

            // Shuffle the non-Spam cards and put the Spam cards last, so Spam is only reached
            // when there is nothing else.
            var cards = handCardTypes.Where(t => t != spam).OrderBy(_ => rng.Next())
                .Concat(handCardTypes.Where(t => t == spam))
                .ToList();

            int count = Math.Min(Math.Max(phaseCount, 0), cards.Count);
            var program = cards.Take(count).ToList();

            // Again in register 1: swap it with the first later register (or unused card)
            // that is not an Again, if there is one.
            if (program.Count > 0 && program[0] == again)
            {
                int swap = cards.FindIndex(1, t => t != again);
                if (swap >= 0)
                {
                    if (swap < program.Count) (program[0], program[swap]) = (program[swap], program[0]);
                    else program[0] = cards[swap];
                }
            }

            return new Decision(program, facingNeeded ? currentFacing : null);
        }
    }
}
