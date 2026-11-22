namespace MRR.Bot
{
    /// <summary>
    /// Improves every computer's program while the humans program (COMPUTER_PLAYER_DESIGN.md §5.7
    /// step 2). Breadth-first: every computer finishes stage s before any starts stage s + 1, so
    /// when the humans finish early every computer has had the same depth of thought.
    ///
    /// Driven by callbacks so it needs no database and no clock: <c>shouldStop</c> is checked
    /// between planner calls, and <c>onImproved</c> receives each finished stage's choice when it
    /// differs from the program currently applied. A stage cut off part-way is discarded.
    /// </summary>
    public static class BotScheduler
    {
        /// <param name="seats">The computers to improve. Level 1 seats (no stages) are skipped.</param>
        /// <param name="current">Each robot's applied program (the placeholder), by RobotID; updated as programs improve.</param>
        /// <param name="onImproved">Apply a better program. Return false if it was refused (e.g. the GM sat that robot out); that robot is then dropped from the run.</param>
        /// <returns>True when every stage of every seat finished; false when stopped early.</returns>
        public static bool Run(BotSnapshot snapshot, IReadOnlyList<BotSeat> seats,
            IDictionary<int, IReadOnlyList<int>> current, ProgramEvaluator evaluator, Random rng,
            Func<bool> shouldStop, Func<BotSeat, ScoredProgram, int, bool> onImproved)
        {
            int stages = seats.Count == 0 ? 0 : seats.Max(s => s.Profile.Stages.Length);
            var dropped = new HashSet<int>();
            var appliedScore = new Dictionary<int, double>(); // the placeholder's is unknown: anything beats it
            for (int stage = 0; stage < stages; stage++)
            {
                foreach (var seat in seats)
                {
                    var widths = seat.Profile.Stages;
                    if (stage >= widths.Length || dropped.Contains(seat.RobotID)) continue;

                    var ranked = BeamSearch.RunStage(evaluator, seat.RobotID, seat.Hand, snapshot.PhaseCount, widths[stage], shouldStop);
                    if (ranked == null) return false; // stopped mid-stage: keep the last finished one

                    // Only the last stage applies the level's deliberate imperfection; earlier
                    // stages hand over their best. A wider beam is not guaranteed to beat a
                    // narrower one, so a best-choice result replaces the program only if it
                    // scores higher.
                    bool noisy = stage == widths.Length - 1 && seat.Profile.Choice != BotChoice.Best;
                    var chosen = BeamSearch.Choose(ranked, noisy ? seat.Profile : seat.Profile with { Choice = BotChoice.Best }, rng);
                    if (chosen == null) continue;
                    if (!noisy && appliedScore.TryGetValue(seat.RobotID, out var best) && chosen.Score <= best) continue;

                    if (current.TryGetValue(seat.RobotID, out var applied) && applied.SequenceEqual(chosen.Program))
                    {
                        appliedScore[seat.RobotID] = chosen.Score;
                        continue;
                    }
                    // A finished stage is applied even if a stop came in meanwhile: it is complete,
                    // and the host's apply is refused safely once the turn is locked.
                    if (onImproved(seat, chosen, stage + 1))
                    {
                        current[seat.RobotID] = chosen.Program;
                        appliedScore[seat.RobotID] = chosen.Score;
                    }
                    else dropped.Add(seat.RobotID);
                }
            }
            return true;
        }
    }
}
