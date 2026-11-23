namespace MRR.Bot
{
    /// <summary>
    /// When to shut down (COMPUTER_PLAYER_DESIGN.md §5.6). Starting guesses agreed 2026-11-22.
    /// </summary>
    public sealed record ShutDownRules(
        int MinDamageInHand = 3,       // a real clog: at least this many in hand and registers...
        int MinDamageTotal = 4,        // ...or at least this many counting the discard pile
        int MinProgress = 2,           // squares closer to the next flag that make a turn worth playing
        int LastFlagNear = 3,          // never shut down when the last flag is this close
        double PerDamageInHand = 120,  // value of clearing one damage card from hand/registers
        double PerDamageInDiscard = 60,// ...and from the discard pile
        double Margin = 50);           // shutting down must beat the best program by this much

    /// <summary>The decision and why, for the log.</summary>
    public sealed record ShutDownVerdict(bool ShutDown, string Reason);

    /// <summary>
    /// Decides whether a computer should shut down this turn. Shutting down (DataService
    /// ApplyShutDownAtLockIn) clears every damage card in hand, registers and discard pile, and
    /// costs the turn's movement; the robot is still shot, pushed and carried by belts.
    ///
    /// Shut down only when all of these hold:
    ///   1. a real clog: MinDamageInHand in hand, or MinDamageTotal counting the discard pile;
    ///   2. this turn does not matter much: the best program touches no flag and gets less than
    ///      MinProgress squares closer -- or it dies;
    ///   3. standing still (planned with the real planner) does not kill the robot and costs no
    ///      more damage than shutting down clears;
    ///   4. not when only the last flag is left and it is within LastFlagNear squares;
    ///   5. the shut-down score -- the stand-still plan's score plus PerDamageInHand and
    ///      PerDamageInDiscard per card cleared -- beats the best program by Margin.
    /// </summary>
    public static class ShutDownAdvisor
    {
        /// <param name="standStill">The robot's outcome from <see cref="ProgramEvaluator.PlanShutDown"/>; computed once per run by the caller.</param>
        public static ShutDownVerdict Decide(ProgramEvaluator evaluator, BotSeat seat, ScoredProgram best,
                                             PlanOutcome standStill, int totalFlags, ShutDownRules? rules = null)
        {
            rules ??= new ShutDownRules();
            int inHand = seat.DamageInHand, inDiscard = seat.DamageInDiscard;

            if (inHand < rules.MinDamageInHand && inHand + inDiscard < rules.MinDamageTotal)
                return new(false, $"only {inHand} damage in hand, {inDiscard} in discard");

            var start = evaluator.Start(seat.RobotID);
            if (start == null) return new(false, "robot not in snapshot");

            if (start.LastFlag + 1 == totalFlags)
            {
                int toLast = evaluator.Outcome(new TurnPlan(), start).StartDistance;
                if (toLast <= rules.LastFlagNear) return new(false, $"last flag {toLast} squares away");
            }

            var bestOutcome = evaluator.Outcome(evaluator.Plan(seat.RobotID, best.Program), start);
            if (!bestOutcome.Dead)
            {
                if (bestOutcome.Flags > 0) return new(false, "best program touches a flag");
                int progress = bestOutcome.StartDistance == FlagDistances.Unreachable || bestOutcome.EndDistance == FlagDistances.Unreachable
                    ? 0
                    : bestOutcome.StartDistance - bestOutcome.EndDistance;
                if (progress >= rules.MinProgress) return new(false, $"best program gains {progress} squares");
            }

            if (standStill.Dead) return new(false, "standing still dies");
            if (standStill.Damage > inHand + inDiscard)
                return new(false, $"standing still takes {standStill.Damage} damage, clears {inHand + inDiscard}");

            double shutDownScore = evaluator.Score(standStill)
                                   + inHand * rules.PerDamageInHand + inDiscard * rules.PerDamageInDiscard;
            if (shutDownScore < best.Score + rules.Margin)
                return new(false, $"shut-down score {shutDownScore:0} vs program {best.Score:0}");

            return new(true, $"clears {inHand}+{inDiscard} damage; shut-down score {shutDownScore:0} vs program {best.Score:0}");
        }
    }
}
