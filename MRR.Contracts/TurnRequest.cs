namespace MRR
{
    /// <summary>
    /// Everything the planner needs to plan one turn. Master assembles this from the live
    /// game; the planner reads it and nothing else, which is what makes a plan reproducible
    /// from its input alone. See API_DECOMPOSITION_DESIGN.md section 5.3.
    /// </summary>
    public class TurnRequest
    {
        public int Turn { get; set; }
        public int Phase { get; set; }
        public int PhaseCount { get; set; } = 5;
        public int GameState { get; set; }
        public int BoardID { get; set; }
        public string BoardFileName { get; set; } = "";

        /// <summary>Game-wide flag count, from the board (CurrentGameData iKey 7).</summary>
        public int TotalFlags { get; set; }

        /// <summary>
        /// Whether the whole field is walled in (CurrentGameData iKey 21). When true, no
        /// robot can move below 0 or above BoardCols-1/BoardRows-1 -- see
        /// CreateCommands.CalcMoveDistance.
        /// </summary>
        public bool FieldEnclosed { get; set; }
        public int LaserDamage { get; set; } = 1;
        public GameTypes GameType { get; set; }

        /// <summary>-1 disables option cards; see IsOptionsEnabled.</summary>
        public int OptionsOnStartup { get; set; } = -1;
        public bool IsOptionsEnabled => OptionsOnStartup > -1;

        /// <summary>The board being played. Loaded by Master, not by the planner.</summary>
        public BoardElementCollection Board { get; set; } = new();

        /// <summary>
        /// Current robot states. The planner deep-copies these and simulates on the copy;
        /// the originals are never mutated. Real player state changes happen later, when the
        /// executor runs the stored commands.
        /// </summary>
        public PlayerStates Players { get; set; } = [];

        /// <summary>Every card in play, with Owner and PhasePlayed set.</summary>
        public CardList GameCards { get; set; } = [];

        /// <summary>Option cards held by robots.</summary>
        public OptionCardList OptionCards { get; set; } = [];

        /// <summary>
        /// Pre-drawn replacement cards per robot, in draw order, for resolving Spam.
        ///
        /// Spam resolution used to call DataService.GetNextCard in a loop, which both hit
        /// the database and mutated it mid-simulation -- and it reshuffles the discard pile,
        /// so the same request would not replan the same way. Master draws the sequence up
        /// front instead; the planner just consumes it. Keyed by RobotID.
        /// </summary>
        public Dictionary<int, List<MoveCard>> DrawPiles { get; set; } = [];
    }
}
