namespace MRR.Bot
{
    /// <summary>
    /// What one plan did to one robot: flags touched, death, damage taken, the flag it is now
    /// heading for, and its walking distance to that flag before and after (Unreachable if none).
    /// </summary>
    public sealed record PlanOutcome(int Flags, bool Dead, int Damage, int NextFlag,
                                     int StartDistance, int EndDistance, bool FacesPath);

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
        public double Score(TurnPlan plan, PlayerState start) => Score(Outcome(plan, start));

        public double Score(PlanOutcome o)
        {
            double score = o.Flags * _weights.Flag + o.Damage * _weights.Damage;
            if (o.Dead) return score + _weights.Died;
            if (o.NextFlag > _snapshot.TotalFlags) return score; // every flag taken

            int distance = o.EndDistance == FlagDistances.Unreachable
                ? _snapshot.Board.BoardCols + _snapshot.Board.BoardRows // worse than anywhere reachable
                : o.EndDistance;
            score += distance * _weights.PerSquare;
            if (o.FacesPath) score += _weights.FacingPath;
            return score;
        }

        /// <summary>What a plan did to the robot that started as <paramref name="start"/>.</summary>
        public PlanOutcome Outcome(TurnPlan plan, PlayerState start)
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

            int nextFlag = start.LastFlag + flags + 1;
            var end = last == null ? start.CurrentPos : last.EndPos;
            return new PlanOutcome(
                flags, dead, damage, nextFlag,
                StartDistance: _distances.Distance(start.LastFlag + 1, start.CurrentPos.X, start.CurrentPos.Y),
                EndDistance: _distances.Distance(nextFlag, end.X, end.Y),
                FacesPath: _distances.FacesTowards(nextFlag, end.X, end.Y, end.Direction));
        }

        /// <summary>The robot's starting state in the snapshot, or null.</summary>
        public PlayerState? Start(int robotId) => _snapshot.Players.GetPlayer(robotId);

        /// <summary>
        /// The turn if this robot shuts down: it plays no cards and fires no laser, but is still
        /// pushed, carried by belts and shot (the planner decides who fires from the request's
        /// players, so only this robot's status changes, on a copy of the list).
        /// </summary>
        public TurnPlan PlanShutDown(int robotId)
        {
            Interlocked.Increment(ref _plans);
            var players = new PlayerStates();
            foreach (var p in _snapshot.Players)
            {
                // The copy constructor does not copy PlayerStatus; keep everyone's.
                var copy = new PlayerState(p) { PlayerStatus = p.PlayerStatus, PositionValid = p.PositionValid };
                if (p.ID == robotId)
                {
                    copy.PlayerStatus = tPlayerStatus.ShutDown;
                    copy.ShutDown = tShutDown.Currently;
                }
                players.Add(copy);
            }

            var request = new TurnRequest
            {
                Turn = 1,
                PhaseCount = _snapshot.PhaseCount,
                GameState = 6,
                TotalFlags = _snapshot.TotalFlags,
                FieldEnclosed = _snapshot.FieldEnclosed,
                LaserDamage = _snapshot.LaserDamage,
                GameType = _snapshot.GameType,
                Board = _snapshot.Board,
                Players = players,
                GameCards = [],
                OptionCards = [],
                DrawPiles = new Dictionary<int, List<MoveCard>>(),
            };
            return new CreateCommands(request).CreateTurn();
        }
    }
}
