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
    /// Spam: without a draw pool a Spam register moves nothing; with one (Normal and up) its
    /// replacement is sampled from the robot's unseen cards (see Evaluate).
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

        /// <summary>
        /// Plans the first <c>program.Count</c> phases and scores the state after them.
        ///
        /// <paramref name="facing"/>: plan as if the robot faced this way (a reboot facing still to
        /// choose, §5.5); null = its facing in the snapshot.
        ///
        /// <paramref name="drawPool"/> and <paramref name="spamSamples"/> (§5.4): a program holding
        /// a Spam is scored as the mean of <paramref name="spamSamples"/> plans, each with the
        /// replacement pile drawn in a random order from <paramref name="drawPool"/> -- the card
        /// types in the robot's deck and discard pile, never their real order. The samples are
        /// seeded from the robot, facing and program, so a program scores the same every time it is
        /// met and programs are compared on the same draws. Without a pool or samples, or without a
        /// Spam, one plan with an empty pile: Spam then moves nothing.
        /// </summary>
        public double Evaluate(int robotId, IReadOnlyList<int> program, Direction? facing = null,
                               IReadOnlyList<int>? drawPool = null, int spamSamples = 0)
        {
            var start = PlayersFor(robotId, facing).GetPlayer(robotId);
            if (start == null || program.Count == 0) { Interlocked.Increment(ref _plans); return double.MinValue; }

            // An Again in register 1 draws like a Spam too (CreateCommands).
            bool draws = program.Contains((int)MoveCard.tCardType.Spam) || program[0] == (int)MoveCard.tCardType.Again;
            bool sample = spamSamples > 0 && drawPool is { Count: > 0 } && draws;
            int runs = sample ? spamSamples : 1;
            double total = 0;
            for (int i = 0; i < runs; i++)
            {
                Interlocked.Increment(ref _plans);
                var pile = sample ? SamplePile(robotId, facing, program, i, drawPool!) : null;
                TurnPlan plan;
                try
                {
                    plan = Plan(robotId, program, facing, pile);
                }
                catch (Exception)
                {
                    return double.MinValue; // a program the planner cannot handle is never chosen
                }
                if (!plan.Planned) return double.MinValue;
                total += Score(plan, start);
            }
            return total / runs;
        }

        /// <summary>The planner's output for this program (phases 1..program.Count).</summary>
        public TurnPlan Plan(int robotId, IReadOnlyList<int> program, Direction? facing = null, List<MoveCard>? drawPile = null)
        {
            var cards = new CardList();
            for (int i = 0; i < program.Count; i++)
                cards.Add(new MoveCard(900_000 + i, (MoveCard.tCardType)program[i]) { Owner = robotId, PhasePlayed = i + 1 });

            var piles = new Dictionary<int, List<MoveCard>>();
            if (drawPile != null) piles[robotId] = drawPile;

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
                Players = PlayersFor(robotId, facing),
                GameCards = cards,
                OptionCards = [],
                DrawPiles = piles,
            };
            return new CreateCommands(request).CreateTurn();
        }

        /// <summary>
        /// Expert's re-rank (§5.2): this robot's program scored in one sampled world, where every
        /// other robot in <paramref name="world"/> plays its sampled program and all move together,
        /// so card priority matters. Spam in the candidate moves nothing here (no draw pile).
        /// </summary>
        public double EvaluateInWorld(int robotId, IReadOnlyList<int> program, Direction? facing,
                                      IReadOnlyDictionary<int, IReadOnlyList<int>> world)
        {
            Interlocked.Increment(ref _plans);
            var players = PlayersFor(robotId, facing);
            var start = players.GetPlayer(robotId);
            if (start == null || program.Count == 0) return double.MinValue;

            var cards = new CardList();
            for (int i = 0; i < program.Count; i++)
                cards.Add(new MoveCard(900_000 + i, (MoveCard.tCardType)program[i]) { Owner = robotId, PhasePlayed = i + 1 });
            foreach (var (other, otherProgram) in world)
            {
                if (other == robotId) continue;
                for (int i = 0; i < otherProgram.Count && i < program.Count; i++)
                    cards.Add(new MoveCard(700_000 + other * 10 + i, (MoveCard.tCardType)otherProgram[i]) { Owner = other, PhasePlayed = i + 1 });
            }

            var request = new TurnRequest
            {
                Turn = 1,
                PhaseCount = program.Count,
                GameState = 6,
                TotalFlags = _snapshot.TotalFlags,
                FieldEnclosed = _snapshot.FieldEnclosed,
                LaserDamage = _snapshot.LaserDamage,
                GameType = _snapshot.GameType,
                Board = _snapshot.Board,
                Players = players,
                GameCards = cards,
                OptionCards = [],
                DrawPiles = new Dictionary<int, List<MoveCard>>(),
            };
            try
            {
                var plan = new CreateCommands(request).CreateTurn();
                return plan.Planned ? Score(plan, start) : double.MinValue;
            }
            catch (Exception)
            {
                return double.MinValue;
            }
        }

        /// <summary>
        /// The snapshot's players, or -- with a facing -- a cached copy in which only this robot
        /// faces that way. Copies keep PlayerStatus, which PlayerState's copy constructor drops.
        /// </summary>
        public PlayerStates PlayersFor(int robotId, Direction? facing)
        {
            if (facing == null) return _snapshot.Players;
            return _facingVariants.GetOrAdd((robotId, facing.Value), key =>
            {
                var players = new PlayerStates();
                foreach (var p in _snapshot.Players)
                {
                    var copy = new PlayerState(p) { PlayerStatus = p.PlayerStatus, PositionValid = p.PositionValid };
                    if (p.ID == key.Item1)
                    {
                        copy.CurrentPos = new RobotLocation(key.Item2, p.CurrentPos.X, p.CurrentPos.Y);
                        copy.NextPos = new RobotLocation(copy.CurrentPos);
                    }
                    players.Add(copy);
                }
                return players;
            });
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<(int, Direction), PlayerStates> _facingVariants = new();

        private static List<MoveCard> SamplePile(int robotId, Direction? facing, IReadOnlyList<int> program, int sample, IReadOnlyList<int> pool)
        {
            unchecked
            {
                int seed = 17 + robotId * 31 + (int)(facing ?? Direction.None) * 131 + sample * 7919;
                foreach (int t in program) seed = seed * 31 + t;
                var rng = new Random(seed);
                return pool.OrderBy(_ => rng.Next())
                    .Select((t, i) => new MoveCard(800_000 + i, (MoveCard.tCardType)t) { Owner = robotId })
                    .ToList();
            }
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

        /// <summary>
        /// Level 2's reboot facing (§5.5): a direction that points along a shortest path to the
        /// robot's next flag, or null if none does (it then keeps its facing).
        /// </summary>
        public Direction? FacingTowardsFlag(int robotId)
        {
            var start = _snapshot.Players.GetPlayer(robotId);
            if (start == null) return null;
            int flag = start.LastFlag + 1;
            foreach (var d in new[] { Direction.Up, Direction.Right, Direction.Down, Direction.Left })
                if (_distances.FacesTowards(flag, start.CurrentPos.X, start.CurrentPos.Y, d)) return d;
            return null;
        }

        /// <summary>The robot's starting state in the snapshot (turned to <paramref name="facing"/> if given), or null.</summary>
        public PlayerState? Start(int robotId, Direction? facing = null) => PlayersFor(robotId, facing).GetPlayer(robotId);

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
