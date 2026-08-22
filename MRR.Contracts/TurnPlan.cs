namespace MRR
{
    /// <summary>
    /// The result of planning one turn: the ordered commands to run, and the game state the
    /// caller should move to. Returned by the planner instead of the planner writing either
    /// of them itself — see API_DECOMPOSITION_DESIGN.md section 5.3.
    ///
    /// Persisting Commands and applying NextGameState are Master's job: CommandList and
    /// CurrentGameData are Master-owned tables (section 6), and a planner that writes them
    /// cannot be run twice on the same input to check it is deterministic.
    /// </summary>
    public class TurnPlan
    {
        /// <summary>Ordered, sequenced commands. Empty if the turn could not be planned.</summary>
        public CommandList Commands { get; set; } = [];

        /// <summary>Game state the caller should move to once the commands are stored.</summary>
        public int NextGameState { get; set; }

        /// <summary>True when a plan was produced; false means Summary says why not.</summary>
        public bool Planned { get; set; }

        /// <summary>Human-readable outcome, for the GM panel and the log.</summary>
        public string Summary { get; set; } = "";

        /// <summary>Non-fatal problems noticed while planning.</summary>
        public List<string> Warnings { get; set; } = [];

        /// <summary>
        /// Spam cards consumed while resolving the turn. The planner cannot retire them
        /// itself -- MoveCards is Master's table -- so it reports them and Master marks them
        /// played. This is what DataService.GetNextCard used to do as a side effect of
        /// drawing.
        /// </summary>
        public List<SpamCardUse> SpamConsumed { get; set; } = [];

        public override string ToString() => Summary;
    }

    /// <summary>One Spam card played by one robot while the turn was planned.</summary>
    public readonly record struct SpamCardUse(int RobotID, int CardID);
}
