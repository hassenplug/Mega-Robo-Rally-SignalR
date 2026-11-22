namespace MRR.Bot
{
    /// <summary>Score weights (COMPUTER_PLAYER_DESIGN.md §5.3). Starting guesses to tune.</summary>
    public sealed record ScoreWeights(
        double Flag = 1000,
        double Died = -400,
        double Damage = -80,
        double PerSquare = -30,
        double FacingPath = 10);

    /// <summary>
    /// Runs the real turn planner on one candidate program for one robot and scores the result.
    /// Only that robot holds cards; every other robot is a stationary obstacle that can still be
    /// pushed and shoot. Each call builds its own cards and (empty) draw piles, because the
    /// planner overwrites card priorities and consumes draw piles (M0 characterization tests).
    ///
    /// Spam: with an empty draw pile a Spam register moves nothing. That makes Spam a
    /// "do nothing" card for the search; sampling the real replacement arrives in M3.
    ///
    /// Thread-safe: shares only the snapshot, which the planner does not modify.
    /// </summary>
    public sealed class ProgramEvaluator
    {
        private static readonly SquareAction[] PositionCommands =
        [
            SquareAction.Move, SquareAction.PushedMove, SquareAction.BoardMove, SquareAction.Rotate,
            SquareAction.BoardRotate, SquareAction.BoardMoveRotate, SquareAction.PushedMoveRotate,
        ];

        private readonly BotSnapshot _snapshot;
        private readonly FlagDistances _distances;
        private readonly ScoreWeights _weights;
        private long _plans;

        public ProgramEvaluator(BotSnapshot snapshot, FlagDistances distances, ScoreWeights? weights = null)
        {
            _snapshot = snapshot;
            _distances = distances;
            _weights = weights ?? new ScoreWeights();
        }

        /// <summary>Planner calls made so far (for work caps and logging).</summary>
        public long PlansRun => Interlocked.Read(ref _plans);

        /// <summary>Plans the first <c>program.Count</c> phases and scores the state after them.</summary>
        public double Evaluate(int robotId, IReadOnlyList<int> program)
        {
            Interlocked.Increment(ref _plans);
            var start = _snapshot.Players.GetPlayer(robotId);
            if (start == null || program.Count == 0) return double.MinValue;

            TurnPlan plan;
            try
            {
                plan = Plan(robotId, program);
            }
            catch (Exception)
            {
                return double.MinValue; // a program the planner cannot handle is never chosen
            }
            return plan.Planned ? Score(plan, start) : double.MinValue;
        }

        /// <summary>The planner's output for this program (phases 1..program.Count).</summary>
        public TurnPlan Plan(int robotId, IReadOnlyList<int> program)
        {
            var cards = new CardList();
            for (int i = 0; i < program.Count; i++)
                cards.Add(new MoveCard(900_000 + i, (MoveCard.tCardType)program[i]) { Owner = robotId, PhasePlayed = i + 1 });

            var request = new TurnRequest
            {
                Turn = 1,
                PhaseCount = program.Count,
                GameState = 6, // the planner refuses any other state
                TotalFlags = _snapshot.TotalFlags,
                FieldEnclosed = _snapshot.FieldEnclosed,
                LaserDamage = _snapshot.LaserDamage,
                GameType = _snapshot.GameType,
                Board = _snapshot.Board,
                Players = _snapshot.Players,
                GameCards = cards,
                OptionCards = [],
                DrawPiles = new Dictionary<int, List<MoveCard>>(),
            };
            return new CreateCommands(request).CreateTurn();
        }

        /// <summary>The §5.3 score of a plan for the robot that started as <paramref name="start"/>.</summary>
        public double Score(TurnPlan plan, PlayerState start)
        {
            int me = start.ID;
            int flags = 0, damage = 0;
            bool dead = false;
            CommandItem? last = null;
            foreach (var c in plan.Commands)
            {
                if (c.RobotID != me) continue;
                if (c.CommandType == SquareAction.Flag && c.Value > 0) flags++;
                else if (c.CommandType == SquareAction.Dead) dead = true;
                else if (c.CommandType == SquareAction.DealSpamCard) damage++;
                if (Array.IndexOf(PositionCommands, c.CommandType) >= 0) last = c;
            }

            double score = flags * _weights.Flag + damage * _weights.Damage;
            if (dead) return score + _weights.Died;

            int nextFlag = start.LastFlag + flags + 1;
            if (nextFlag > _snapshot.TotalFlags) return score; // every flag taken

            var end = last == null ? start.CurrentPos : last.EndPos;
            int distance = _distances.Distance(nextFlag, end.X, end.Y);
            if (distance == FlagDistances.Unreachable)
                distance = _snapshot.Board.BoardCols + _snapshot.Board.BoardRows; // worse than anywhere reachable
            score += distance * _weights.PerSquare;
            if (_distances.FacesTowards(nextFlag, end.X, end.Y, end.Direction)) score += _weights.FacingPath;
            return score;
        }
    }
}
