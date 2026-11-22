namespace MRR.Bot
{
    /// <summary>A complete program and its score.</summary>
    public sealed record ScoredProgram(IReadOnlyList<int> Program, double Score);

    /// <summary>
    /// One search stage: a beam search of width B over card-type sequences
    /// (COMPUTER_PLAYER_DESIGN.md §5.2). For k = 1..PhaseCount it extends each of the best B
    /// prefixes by every card type still in hand, plans the first k phases, and keeps the best B.
    /// Prefix plans are exact because the planner resolves phases in order (M0).
    ///
    /// A stage only ever produces a complete program. If <c>shouldStop</c> turns true mid-stage it
    /// returns null at once (after at most one more planner call), and the caller keeps the last
    /// finished stage.
    /// </summary>
    public static class BeamSearch
    {
        /// <returns>The final layer, best first, or null when stopped.</returns>
        public static List<ScoredProgram>? RunStage(ProgramEvaluator evaluator, int robotId,
            IReadOnlyList<int> hand, int phaseCount, int width, Func<bool> shouldStop)
        {
            int length = Math.Min(phaseCount, hand.Count);
            if (length <= 0 || width <= 0) return new List<ScoredProgram>();

            var available = hand.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
            var types = available.Keys.OrderBy(t => t).ToList(); // fixed order: deterministic ties

            var beam = new List<ScoredProgram> { new(Array.Empty<int>(), 0) };
            for (int k = 1; k <= length; k++)
            {
                var layer = new List<ScoredProgram>();
                foreach (var prefix in beam)
                {
                    foreach (int type in types)
                    {
                        if (prefix.Program.Count(t => t == type) >= available[type]) continue;
                        if (shouldStop()) return null;

                        var program = prefix.Program.Append(type).ToArray();
                        layer.Add(new ScoredProgram(program, evaluator.Evaluate(robotId, program)));
                    }
                }
                // Stable sort: equal scores keep the fixed type order.
                beam = layer.OrderByDescending(p => p.Score).Take(k == length ? layer.Count : width).ToList();
            }
            return beam;
        }

        /// <summary>Picks from a ranked final layer the way the profile says (§5.0).</summary>
        public static ScoredProgram? Choose(IReadOnlyList<ScoredProgram> ranked, BotProfile profile, Random rng)
        {
            if (ranked.Count == 0) return null;
            switch (profile.Choice)
            {
                case BotChoice.RandomTopN:
                    return ranked[rng.Next(Math.Min(Math.Max(profile.ChoiceCount, 1), ranked.Count))];
                case BotChoice.WithinMargin:
                    double floor = ranked[0].Score - profile.ChoiceMargin;
                    var close = ranked.TakeWhile(p => p.Score >= floor).ToList();
                    return close[rng.Next(close.Count)];
                default:
                    return ranked[0];
            }
        }
    }
}
