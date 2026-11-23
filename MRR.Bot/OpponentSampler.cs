namespace MRR.Bot
{
    /// <summary>
    /// One sampled "world" for Expert's re-rank (§5.2): a plausible program for every robot that
    /// will act this turn, from public information only -- positions, facings, flags taken and
    /// shut-down state, never anyone's cards. Each robot is dealt nine random cards from the deck
    /// mix and programmed by a greedy search (stage width 1) toward its own next flag, with the
    /// others standing still.
    ///
    /// Greedy rather than the plan's Level 2: Level 2 costs about 0.5 s per robot on the Pi 4,
    /// which would make one world take ~3 s; greedy is a third of that. One world is shared by
    /// every Expert at the table, each ignoring its own entry.
    /// </summary>
    public static class OpponentSampler
    {
        public const int HandSize = 9;

        /// <returns>RobotID → program, or null if <paramref name="shouldStop"/> turned true.</returns>
        public static Dictionary<int, IReadOnlyList<int>>? Sample(ProgramEvaluator evaluator, BotSnapshot snapshot,
                                                                   int seed, Func<bool> shouldStop)
        {
            var deck = snapshot.DeckMix.Count > 0 ? snapshot.DeckMix : BotSnapshot.StandardDeck;
            var world = new Dictionary<int, IReadOnlyList<int>>();
            foreach (var robot in snapshot.Players)
            {
                if (robot.PlayerStatus == tPlayerStatus.Dead || robot.PlayerStatus == tPlayerStatus.ShutDown) continue;

                var rng = new Random(unchecked(seed * 7919 + robot.ID * 104729));
                var hand = deck.OrderBy(_ => rng.Next()).Take(HandSize).ToList();
                var ranked = BeamSearch.RunStage(evaluator, robot.ID, hand, snapshot.PhaseCount, 1, shouldStop);
                if (ranked == null) return null;
                if (ranked.Count > 0) world[robot.ID] = ranked[0].Program;
            }
            return world;
        }
    }
}
