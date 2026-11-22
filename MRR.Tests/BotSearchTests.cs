using Xunit.Abstractions;
using MRR.Bot;
using T = MRR.MoveCard.tCardType;

namespace MRR.Tests
{
    /// <summary>
    /// M2: the planner-based search for Levels 2-4 (COMPUTER_PLAYER_DESIGN.md §5.2-5.3, §7).
    /// Searches are stopped by plan count, never by the clock, so results are deterministic.
    /// </summary>
    public class BotSearchTests(ITestOutputHelper output)
    {
        // ---- helpers ------------------------------------------------------------------------

        private static BoardElementCollection EmptyBoard(int size, params (int X, int Y)[] flags)
        {
            var board = new BoardElementCollection(size, size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    board.SetSquare(x, y, SquareType.Blank, Direction.None, new BoardActionsCollection());
            for (int n = 1; n <= flags.Length; n++)
                board.GetSquare(flags[n - 1].X, flags[n - 1].Y)!.AddAction(new BoardAction(SquareAction.Flag, n, 10, 31));
            return board;
        }

        private static PlayerStates OneRobot(int x, int y, Direction dir)
        {
            var p = new PlayerState { ID = 1, Name = "R1", PlayerStatus = tPlayerStatus.ReadyToRun, Priority = 1, Energy = 3, CurrentPos = new RobotLocation(dir, x, y) };
            p.NextPos = new RobotLocation(p.CurrentPos);
            return [p];
        }

        private static BotSnapshot Snap(BoardElementCollection board, PlayerStates players, int phases, int flags) =>
            new() { Board = board, Players = players, PhaseCount = phases, TotalFlags = flags, LaserDamage = 1 };

        private static ProgramEvaluator Evaluator(BotSnapshot s) => new(s, new FlagDistances(s.Board, s.TotalFlags));

        private static List<int> Hand(params T[] types) => types.Select(t => (int)t).ToList();

        /// <summary>Runs a level's whole search for one robot (no stop) and returns its final program.</summary>
        private static ScoredProgram? SearchToEnd(BotSnapshot s, ProgramEvaluator ev, int robot, int level, IReadOnlyList<int> hand, int seed)
        {
            ScoredProgram? last = null;
            var seat = new BotSeat(robot, level, hand);
            BotScheduler.Run(s, [seat], new Dictionary<int, IReadOnlyList<int>>(), ev, new Random(seed),
                shouldStop: () => false, onImproved: (_, p, _) => { last = p; return true; });
            return last;
        }

        private static bool Touches(TurnPlan plan, int robot, SquareAction action) =>
            plan.Commands.Any(c => c.RobotID == robot && c.CommandType == action && (action != SquareAction.Flag || c.Value > 0));

        private static void AssertLegal(IReadOnlyList<int> hand, int phases, IReadOnlyList<int> program)
        {
            Assert.Equal(Math.Min(phases, hand.Count), program.Count);
            foreach (var g in program.GroupBy(t => t))
                Assert.True(hand.Count(t => t == g.Key) >= g.Count());
        }

        private static readonly List<int> MixedHand =
            Hand(T.Forward1, T.Forward2, T.Forward3, T.RTurn, T.LTurn, T.UTurn, T.Back1, T.Again, T.RTurn);

        // ---- flag distances -----------------------------------------------------------------

        [Fact]
        public void FlagDistances_RespectWallsAndPits()
        {
            var board = EmptyBoard(5, (2, 2));
            Assert.Equal(2, new FlagDistances(board, 1).Distance(1, 0, 2));

            board.GetSquare(1, 2)!.Type = SquareType.Pit;            // straight line now blocked
            Assert.Equal(4, new FlagDistances(board, 1).Distance(1, 0, 2));

            var walled = EmptyBoard(5, (2, 2));
            walled.GetSquare(2, 2)!.AddWall(Direction.Right);         // can't enter (2,2) moving Right
            var d = new FlagDistances(walled, 1);
            Assert.Equal(1, d.Distance(1, 2, 1));                     // from above: unaffected
            Assert.Equal(3, d.Distance(1, 1, 2));                     // from the left: must go round
            Assert.True(d.FacesTowards(1, 2, 0, Direction.Down));
            Assert.False(d.FacesTowards(1, 2, 0, Direction.Up));
        }

        // ---- behaviour fixtures (Levels 3-4) ------------------------------------------------

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        public void FlagAhead_IsTouchedThisTurn(int level)
        {
            var s = Snap(EmptyBoard(8, (4, 4)), OneRobot(1, 4, Direction.Right), phases: 5, flags: 1);
            var ev = Evaluator(s);
            var best = SearchToEnd(s, ev, 1, level, MixedHand, seed: 1)!;
            AssertLegal(MixedHand, 5, best.Program);
            Assert.True(Touches(ev.Plan(1, best.Program), 1, SquareAction.Flag), $"program [{string.Join(",", best.Program)}]");
        }

        [Fact]
        public void PitAhead_IsAvoided()
        {
            var board = EmptyBoard(8, (1, 1));
            board.GetSquare(2, 4)!.Type = SquareType.Pit;
            board.GetSquare(3, 4)!.Type = SquareType.Pit;
            var s = Snap(board, OneRobot(1, 4, Direction.Right), phases: 5, flags: 1);
            var ev = Evaluator(s);
            var hand = Hand(T.Forward3, T.Forward2, T.Forward1, T.Forward1, T.LTurn, T.RTurn, T.UTurn, T.Back1, T.Forward2);

            var best = SearchToEnd(s, ev, 1, 4, hand, seed: 1)!;
            var plan = ev.Plan(1, best.Program);
            Assert.False(Touches(plan, 1, SquareAction.Dead), $"program [{string.Join(",", best.Program)}] dies");
            Assert.True(Touches(plan, 1, SquareAction.Flag), "the flag is 3 squares up: LTurn then forward reaches it");
        }

        [Fact]
        public void OnlyTurns_StillLegal()
        {
            var s = Snap(EmptyBoard(8, (6, 6)), OneRobot(1, 1, Direction.Right), phases: 5, flags: 1);
            var hand = Hand(T.RTurn, T.RTurn, T.RTurn, T.LTurn, T.LTurn, T.LTurn, T.UTurn, T.UTurn, T.UTurn);
            var best = SearchToEnd(s, Evaluator(s), 1, 4, hand, seed: 1)!;
            AssertLegal(hand, 5, best.Program);
        }

        [Fact]
        public void ShortHand_ProgramsWhatItHas()
        {
            var s = Snap(EmptyBoard(8, (6, 6)), OneRobot(1, 1, Direction.Right), phases: 5, flags: 1);
            var hand = Hand(T.Forward1, T.RTurn, T.Forward2);
            var best = SearchToEnd(s, Evaluator(s), 1, 4, hand, seed: 1)!;
            AssertLegal(hand, 5, best.Program);
        }

        // ---- purity and determinism ---------------------------------------------------------

        [Fact]
        public void Search_LeavesSnapshotUnchanged()
        {
            var s = Snap(BotFixtures.Board(), BotFixtures.Players(), phases: 5, flags: BotFixtures.TotalFlags);
            string board = BotFixtures.BoardSignature(s.Board), players = BotFixtures.PlayersSignature(s.Players);
            var hand = BotFixtures.Hand.Select(t => (int)t).ToList();

            SearchToEnd(s, Evaluator(s), 1, 3, hand, seed: 5);

            Assert.Equal(board, BotFixtures.BoardSignature(s.Board));
            Assert.Equal(players, BotFixtures.PlayersSignature(s.Players));
        }

        [Fact]
        public void SameSeed_SameDecision()
        {
            var s = Snap(BotFixtures.Board(), BotFixtures.Players(), phases: 3, flags: BotFixtures.TotalFlags);
            var hand = BotFixtures.Hand.Select(t => (int)t).ToList();
            var a = SearchToEnd(s, Evaluator(s), 2, 3, hand, seed: 11)!;
            var b = SearchToEnd(s, Evaluator(s), 2, 3, hand, seed: 11)!;
            Assert.Equal(a.Program, b.Program);
        }

        // ---- scheduler ----------------------------------------------------------------------

        [Fact]
        public void Stop_MidStage_KeepsLastFinishedStage_AndOverrunsByNoPlan()
        {
            var s = Snap(BotFixtures.Board(), BotFixtures.Players(), phases: 5, flags: BotFixtures.TotalFlags);
            var hand = BotFixtures.Hand.Select(t => (int)t).ToList();
            var ev = Evaluator(s);
            const int cap = 50; // stage 1 (greedy) needs ~32 plans, stage 2 ~79

            var applied = new List<int>();
            bool complete = BotScheduler.Run(s, [new BotSeat(1, 4, hand)], new Dictionary<int, IReadOnlyList<int>>(), ev, new Random(1),
                shouldStop: () => ev.PlansRun >= cap, onImproved: (_, _, stage) => { applied.Add(stage); return true; });

            Assert.False(complete);
            Assert.Equal([1], applied);        // stage 1 applied; the cut-off stage 2 never is
            Assert.Equal(cap, ev.PlansRun);    // checked before every planner call
        }

        [Fact]
        public void BreadthFirst_EveryComputerFinishesAStageBeforeAnyStartsTheNext()
        {
            var s = Snap(BotFixtures.Board(), BotFixtures.Players(), phases: 5, flags: BotFixtures.TotalFlags);
            var hand = BotFixtures.Hand.Select(t => (int)t).ToList();

            // How many plans stage 1 costs for each robot on its own.
            long stage1 = 0;
            foreach (int robot in new[] { 1, 2, 3 })
            {
                var one = Evaluator(s);
                BeamSearch.RunStage(one, robot, hand, 5, width: 1, () => false);
                stage1 += one.PlansRun;
            }

            var ev = Evaluator(s);
            var applied = new List<(int Robot, int Stage)>();
            var seats = new[] { 1, 2, 3 }.Select(r => new BotSeat(r, 4, hand)).ToList();
            BotScheduler.Run(s, seats, new Dictionary<int, IReadOnlyList<int>>(), ev, new Random(1),
                shouldStop: () => ev.PlansRun >= stage1, onImproved: (seat, _, stage) => { applied.Add((seat.RobotID, stage)); return true; });

            Assert.Equal([(1, 1), (2, 1), (3, 1)], applied);
        }

        [Fact]
        public void RefusedApply_DropsOnlyThatRobot()
        {
            var s = Snap(BotFixtures.Board(), BotFixtures.Players(), phases: 3, flags: BotFixtures.TotalFlags);
            var hand = BotFixtures.Hand.Select(t => (int)t).ToList();
            var applied = new List<(int Robot, int Stage)>();
            var seats = new[] { new BotSeat(1, 2, hand), new BotSeat(2, 2, hand) };

            bool complete = BotScheduler.Run(s, seats, new Dictionary<int, IReadOnlyList<int>>(), Evaluator(s), new Random(1),
                shouldStop: () => false,
                onImproved: (seat, _, stage) => { applied.Add((seat.RobotID, stage)); return seat.RobotID != 1; });

            Assert.True(complete);
            Assert.Equal(1, applied.Count(a => a.Robot == 1));   // refused once, then left alone
            Assert.Contains(applied, a => a.Robot == 2 && a.Stage == 2);
        }

        // ---- the ladder ---------------------------------------------------------------------

        /// <summary>
        /// Regret = best possible score (exhaustive) minus the chosen program's score. Over a set
        /// of robots and hands on the busy fixture board, mean regret must fall from Level 1 to
        /// Level 4 (design §7). Three phases keep the exhaustive reference cheap on the Pi.
        /// </summary>
        [Fact]
        public void Regret_FallsWithLevel()
        {
            const int phases = 3;
            var s = Snap(BotFixtures.Board(), BotFixtures.Players(), phases, BotFixtures.TotalFlags);
            var ev = Evaluator(s);
            var deck = new[] { T.Forward1, T.Forward1, T.Forward2, T.Forward3, T.RTurn, T.RTurn, T.LTurn, T.LTurn, T.UTurn, T.Back1, T.Again };
            var regret = new double[5];
            int cases = 0;

            for (int robot = 1; robot <= 6; robot++)
            {
                var rng = new Random(robot * 97);
                var hand = deck.OrderBy(_ => rng.Next()).Take(9).Select(t => (int)t).ToList();
                double best = Exhaustive(ev, robot, hand, phases);
                cases++;

                for (int seed = 0; seed < 2; seed++)
                {
                    var random = RandomBrain.Decide(hand, phases, 1, false, new Random(seed + robot * 13));
                    regret[1] += (best - ev.Evaluate(robot, random.CardTypes)) / 2;
                    for (int level = 2; level <= 4; level++)
                        regret[level] += (best - SearchToEnd(s, ev, robot, level, hand, seed)!.Score) / 2;
                }
            }

            for (int level = 1; level <= 4; level++) regret[level] /= cases;
            string report = string.Join(", ", Enumerable.Range(1, 4).Select(l => $"L{l} {regret[l]:0.0}"));
            output.WriteLine("mean regret: " + report);

            Assert.True(regret[1] > regret[2], report);
            Assert.True(regret[2] > regret[3], report);
            Assert.True(regret[3] >= regret[4], report);
            Assert.True(regret[4] < 15, report); // Hard is close to the best possible
        }

        private static double Exhaustive(ProgramEvaluator ev, int robot, IReadOnlyList<int> hand, int length)
        {
            var counts = hand.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
            var program = new List<int>();
            double best = double.MinValue;
            void Recurse()
            {
                if (program.Count == length) { best = Math.Max(best, ev.Evaluate(robot, program.ToArray())); return; }
                foreach (int t in counts.Keys.ToList())
                {
                    if (counts[t] == 0) continue;
                    counts[t]--; program.Add(t);
                    Recurse();
                    program.RemoveAt(program.Count - 1); counts[t]++;
                }
            }
            Recurse();
            return best;
        }
    }
}
