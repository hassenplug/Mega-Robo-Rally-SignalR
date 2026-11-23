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
    ///
    /// Shut-down (§5.6): for levels that may shut down, each time the robot's program changes the
    /// ShutDownAdvisor is asked again, and <c>onShutDown</c> is called whenever its answer differs
    /// from the robot's current state -- so a later, better program can switch it off again.
    /// Computers start each turn not shut down (ResetPlayers clears it).
    /// </summary>
    public static class BotScheduler
    {
        /// <summary>One stop signal for everyone (tests, and callers with no Expert).</summary>
        public static bool Run(BotSnapshot snapshot, IReadOnlyList<BotSeat> seats,
            IDictionary<int, IReadOnlyList<int>> current, ProgramEvaluator evaluator, Random rng,
            Func<bool> shouldStop, Func<BotSeat, ScoredProgram, int, bool> onImproved,
            Func<BotSeat, ShutDownVerdict, bool>? onShutDown = null, ShutDownRules? shutDownRules = null,
            Func<bool>? mayStartRound = null, ReRankSettings? reRank = null) =>
            Run(snapshot, seats, current, evaluator, rng, _ => shouldStop(), onImproved, onShutDown, shutDownRules, mayStartRound, reRank);

        /// <param name="seats">The computers to improve. Level 1 seats (no stages) are skipped.</param>
        /// <param name="current">Each robot's applied program (the placeholder), by RobotID; updated as programs improve.</param>
        /// <param name="shouldStop">Per seat: must this computer stop now? (Expert stops later than the rest, BotTiming.StopFor.)</param>
        /// <param name="onImproved">Apply a better program; the int is the stage (re-rank rounds count on after the stages). Return false if it was refused (e.g. the GM sat that robot out); that robot is then dropped from the run.</param>
        /// <param name="onShutDown">Set the robot's shut-down state to the verdict's. Return false if refused. Null: never shut down.</param>
        /// <param name="mayStartRound">Whether an Expert may start another re-rank round (false after the soft deadline).</param>
        /// <returns>True when every stage and round of every seat finished; false when stopped early.</returns>
        public static bool Run(BotSnapshot snapshot, IReadOnlyList<BotSeat> seats,
            IDictionary<int, IReadOnlyList<int>> current, ProgramEvaluator evaluator, Random rng,
            Func<BotSeat, bool> shouldStop, Func<BotSeat, ScoredProgram, int, bool> onImproved,
            Func<BotSeat, ShutDownVerdict, bool>? onShutDown = null, ShutDownRules? shutDownRules = null,
            Func<bool>? mayStartRound = null, ReRankSettings? reRank = null)
        {
            reRank ??= new ReRankSettings();
            mayStartRound ??= () => true;
            int StagesOf(BotSeat seat) => seat.Profile.Stages.Length + (seat.Profile.ReRank ? reRank.Rounds : 0);
            int stages = seats.Count == 0 ? 0 : seats.Max(StagesOf);
            int candidatesPerExpert = reRank.CandidatesFor(seats.Count(x => x.Profile.ReRank));
            int worldSeed = rng.Next();
            bool complete = true;

            var dropped = new HashSet<int>();   // refused an apply
            var stopped = new HashSet<int>();   // out of time, or no more rounds allowed
            var best = new Dictionary<int, ScoredProgram>();   // the applied program, once its score is known
            var shutDown = new Dictionary<int, bool>();
            var standStill = new Dictionary<int, PlanOutcome>();
            var appliedFacing = new Dictionary<int, Direction?>(); // the placeholder kept the robot's facing (null)
            var pathFacing = new Dictionary<int, Direction?>();    // Easy's reboot facing, worked out once
            var lastRanked = new Dictionary<int, List<ScoredProgram>>(); // Expert: its last stage's ranking
            var candidates = new Dictionary<int, List<ScoredProgram>>();
            var sums = new Dictionary<int, double[]>();
            var worlds = new List<Dictionary<int, IReadOnlyList<int>>?>();

            for (int stage = 0; stage < stages; stage++)
            {
                foreach (var seat in seats)
                {
                    var widths = seat.Profile.Stages;
                    if (stage >= StagesOf(seat) || dropped.Contains(seat.RobotID) || stopped.Contains(seat.RobotID)) continue;

                    if (stage >= widths.Length)
                    {
                        // Expert re-rank round (stage - widths.Length).
                        var outcome = ReRankRound(seat, stage - widths.Length);
                        if (outcome == false) complete = false;
                        continue;
                    }

                    var ranked = RunSeatStage(evaluator, seat, snapshot.PhaseCount, widths[stage], () => shouldStop(seat), pathFacing);
                    if (ranked == null)
                    {
                        // Stopped mid-stage: keep the last finished one.
                        stopped.Add(seat.RobotID);
                        complete = false;
                        continue;
                    }
                    if (seat.Profile.ReRank && stage == widths.Length - 1) lastRanked[seat.RobotID] = ranked;

                    // Only the last stage applies the level's deliberate imperfection; earlier
                    // stages hand over their best. A wider beam is not guaranteed to beat a
                    // narrower one, so a best-choice result replaces the program only if it
                    // scores higher. (The placeholder's score is unknown: anything beats it.)
                    bool noisy = stage == widths.Length - 1 && seat.Profile.Choice != BotChoice.Best;
                    var chosen = BeamSearch.Choose(ranked, noisy ? seat.Profile : seat.Profile with { Choice = BotChoice.Best }, rng);
                    if (chosen == null) continue;
                    if (!noisy && best.TryGetValue(seat.RobotID, out var held) && chosen.Score <= held.Score) continue;

                    if (current.TryGetValue(seat.RobotID, out var applied) && applied.SequenceEqual(chosen.Program)
                        && appliedFacing.GetValueOrDefault(seat.RobotID) == chosen.Facing)
                    {
                        best[seat.RobotID] = chosen;
                    }
                    // A finished stage is applied even if a stop came in meanwhile: it is complete,
                    // and the host's apply is refused safely once the turn is locked.
                    else if (onImproved(seat, chosen, stage + 1))
                    {
                        current[seat.RobotID] = chosen.Program;
                        appliedFacing[seat.RobotID] = chosen.Facing;
                        best[seat.RobotID] = chosen;
                    }
                    else
                    {
                        dropped.Add(seat.RobotID);
                        continue;
                    }

                    if (onShutDown == null || !seat.Profile.MayShutDown) continue;
                    var start = evaluator.Start(seat.RobotID);
                    if (start == null) continue;
                    if (!standStill.TryGetValue(seat.RobotID, out var still))
                        standStill[seat.RobotID] = still = evaluator.Outcome(evaluator.PlanShutDown(seat.RobotID), start);

                    var verdict = ShutDownAdvisor.Decide(evaluator, seat, chosen, still, snapshot.TotalFlags, shutDownRules);
                    if (verdict.ShutDown != shutDown.GetValueOrDefault(seat.RobotID) && onShutDown(seat, verdict))
                        shutDown[seat.RobotID] = verdict.ShutDown;
                }
            }
            return complete;

            // One re-rank round for one Expert (§5.2): score each candidate in sampled world
            // `round` and, if the round finishes, apply the candidate with the best running
            // average. A round cut off part-way is discarded. Returns false when it stopped early.
            bool? ReRankRound(BotSeat seat, int round)
            {
                if (!lastRanked.TryGetValue(seat.RobotID, out var ranking) || ranking.Count == 0)
                {
                    stopped.Add(seat.RobotID);
                    return null;
                }
                if (!mayStartRound())
                {
                    stopped.Add(seat.RobotID);
                    return false;
                }

                while (worlds.Count <= round) worlds.Add(null);
                worlds[round] ??= OpponentSampler.Sample(evaluator, snapshot, worldSeed + round, () => shouldStop(seat));
                var world = worlds[round];
                if (world == null)
                {
                    stopped.Add(seat.RobotID);
                    return false;
                }

                if (!candidates.TryGetValue(seat.RobotID, out var cands))
                    candidates[seat.RobotID] = cands = ranking.Take(candidatesPerExpert).ToList();
                if (!sums.TryGetValue(seat.RobotID, out var sum))
                    sums[seat.RobotID] = sum = new double[cands.Count];

                var scores = new double[cands.Count];
                for (int i = 0; i < cands.Count; i++)
                {
                    if (shouldStop(seat))
                    {
                        stopped.Add(seat.RobotID);
                        return false;
                    }
                    scores[i] = evaluator.EvaluateInWorld(seat.RobotID, cands[i].Program, cands[i].Facing, world);
                }
                for (int i = 0; i < cands.Count; i++) sum[i] += scores[i];

                int bestIndex = 0;
                for (int i = 1; i < cands.Count; i++) if (sum[i] > sum[bestIndex]) bestIndex = i;
                var chosen = cands[bestIndex] with { Score = sum[bestIndex] / (round + 1) };

                if (current.TryGetValue(seat.RobotID, out var applied) && applied.SequenceEqual(chosen.Program)
                    && appliedFacing.GetValueOrDefault(seat.RobotID) == chosen.Facing)
                    return true;
                if (onImproved(seat, chosen, seat.Profile.Stages.Length + round + 1))
                {
                    current[seat.RobotID] = chosen.Program;
                    appliedFacing[seat.RobotID] = chosen.Facing;
                }
                else dropped.Add(seat.RobotID);
                return true;
            }
        }

        private static readonly Direction[] Facings = [Direction.Up, Direction.Right, Direction.Down, Direction.Left];

        /// <summary>
        /// One stage for one seat. After a reboot (§5.5) Normal and up run it once per facing and
        /// rank all the results together; Easy faces along the path to its next flag; otherwise the
        /// robot keeps its facing.
        /// </summary>
        private static List<ScoredProgram>? RunSeatStage(ProgramEvaluator evaluator, BotSeat seat, int phaseCount,
            int width, Func<bool> shouldStop, Dictionary<int, Direction?> pathFacing)
        {
            if (!seat.ChooseFacing)
                return BeamSearch.RunStage(evaluator, seat, phaseCount, width, shouldStop);

            if (seat.Level <= 2)
            {
                if (!pathFacing.TryGetValue(seat.RobotID, out var facing))
                    pathFacing[seat.RobotID] = facing = evaluator.FacingTowardsFlag(seat.RobotID);
                return BeamSearch.RunStage(evaluator, seat, phaseCount, width, shouldStop, facing);
            }

            var all = new List<ScoredProgram>();
            foreach (var facing in Facings)
            {
                var ranked = BeamSearch.RunStage(evaluator, seat, phaseCount, width, shouldStop, facing);
                if (ranked == null) return null;
                all.AddRange(ranked);
            }
            return all.OrderByDescending(p => p.Score).ToList(); // stable: ties keep facing order
        }
    }
}
