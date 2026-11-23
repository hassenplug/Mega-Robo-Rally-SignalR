using MRR.Bot;
using T = MRR.MoveCard.tCardType;

namespace MRR.Tests
{
    /// <summary>
    /// M3: when a computer shuts down (ShutDownAdvisor, COMPUTER_PLAYER_DESIGN.md §5.6).
    /// </summary>
    public class BotShutDownTests
    {
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

        private static PlayerStates OneRobot(int x, int y, Direction dir, int lastFlag = 0)
        {
            var p = new PlayerState { ID = 1, Name = "R1", PlayerStatus = tPlayerStatus.ReadyToRun, Priority = 1, Energy = 3,
                                      CurrentPos = new RobotLocation(dir, x, y), LastFlag = lastFlag };
            p.NextPos = new RobotLocation(p.CurrentPos);
            return [p];
        }

        private static List<int> Hand(params T[] types) => types.Select(t => (int)t).ToList();

        /// <summary>Runs the robot's whole search as <paramref name="level"/> and returns every shut-down call made.</summary>
        private static List<ShutDownVerdict> Run(BoardElementCollection board, PlayerStates players, int flags,
                                                 List<int> hand, int level, int damageInDiscard = 0)
        {
            var s = new BotSnapshot { Board = board, Players = players, PhaseCount = 5, TotalFlags = flags, LaserDamage = 1 };
            var ev = new ProgramEvaluator(s, new FlagDistances(board, flags));
            var calls = new List<ShutDownVerdict>();
            BotScheduler.Run(s, [new BotSeat(1, level, hand, damageInDiscard)], new Dictionary<int, IReadOnlyList<int>>(), ev, new Random(1),
                shouldStop: () => false, onImproved: (_, _, _) => true,
                onShutDown: (_, v) => { calls.Add(v); return true; });
            return calls;
        }

        // Five Spam: whatever it programs, at most four real cards, all turns here.
        private static readonly List<int> ClogOfTurns =
            Hand(T.Spam, T.Spam, T.Spam, T.Spam, T.Spam, T.RTurn, T.LTurn, T.UTurn, T.Again);

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void Clogged_NoGoodMove_Safe_ShutsDown(int level)
        {
            var calls = Run(EmptyBoard(8, (6, 6)), OneRobot(1, 1, Direction.Up), 1, ClogOfTurns, level);
            Assert.True(calls.Count == 1 && calls[0].ShutDown, string.Join(" | ", calls.Select(c => c.Reason)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void BeginnerAndEasy_NeverShutDown(int level)
        {
            Assert.Empty(Run(EmptyBoard(8, (6, 6)), OneRobot(1, 1, Direction.Up), 1, ClogOfTurns, level));
        }

        [Fact]
        public void FlagInReach_DoesNotShutDown()
        {
            var hand = Hand(T.Spam, T.Spam, T.Spam, T.Spam, T.Forward2, T.RTurn, T.LTurn, T.UTurn, T.Again);
            var calls = Run(EmptyBoard(8, (3, 4), (7, 7)), OneRobot(1, 4, Direction.Right), 2, hand, 4);
            Assert.DoesNotContain(calls, c => c.ShutDown);
        }

        [Fact]
        public void LightClog_DoesNotShutDown()
        {
            // Two in hand and one in the discard pile: under both thresholds (3 in hand, 4 in all).
            var hand = Hand(T.Spam, T.Spam, T.RTurn, T.LTurn, T.UTurn, T.Again, T.RTurn, T.LTurn, T.UTurn);
            Assert.DoesNotContain(Run(EmptyBoard(8, (6, 6)), OneRobot(1, 1, Direction.Up), 1, hand, 4, damageInDiscard: 1), c => c.ShutDown);
        }

        [Fact]
        public void DiscardPileCounts_TowardTheClog()
        {
            // Two in hand plus two in the discard pile reaches the 4-in-all threshold.
            var hand = Hand(T.Spam, T.Spam, T.RTurn, T.LTurn, T.UTurn, T.Again, T.RTurn, T.LTurn, T.UTurn);
            var calls = Run(EmptyBoard(8, (6, 6)), OneRobot(1, 1, Direction.Up), 1, hand, 4, damageInDiscard: 2);
            Assert.Contains(calls, c => c.ShutDown);
        }

        [Fact]
        public void LastFlagClose_DoesNotShutDown()
        {
            // Holding flag 1 of 2; flag 2 is three squares away (behind it, out of reach this turn).
            var calls = Run(EmptyBoard(8, (7, 7), (1, 4)), OneRobot(1, 1, Direction.Up, lastFlag: 1), 2, ClogOfTurns, 4);
            Assert.DoesNotContain(calls, c => c.ShutDown);
        }

        [Fact]
        public void StandingStillDies_DoesNotShutDown()
        {
            // On a belt that carries it into a pit: shutting down would not save it.
            var board = EmptyBoard(8, (6, 6));
            board.SetSquare(1, 4, SquareType.NormalBelt, Direction.Right, [new BoardAction(SquareAction.Move, 1, 3, 31)]);
            board.GetSquare(2, 4)!.Type = SquareType.Pit;
            var players = OneRobot(1, 4, Direction.Up);

            var s = new BotSnapshot { Board = board, Players = players, PhaseCount = 5, TotalFlags = 1, LaserDamage = 1 };
            var ev = new ProgramEvaluator(s, new FlagDistances(board, 1));
            var still = ev.Outcome(ev.PlanShutDown(1), players.GetPlayer(1)!);
            Assert.True(still.Dead, "the belt should carry a shut-down robot into the pit");

            Assert.DoesNotContain(Run(board, OneRobot(1, 4, Direction.Up), 1, ClogOfTurns, 4), c => c.ShutDown);
        }

        [Fact]
        public void ShutDownPlan_LeavesSnapshotUnchanged()
        {
            var s = new BotSnapshot { Board = BotFixtures.Board(), Players = BotFixtures.Players(), PhaseCount = 5, TotalFlags = BotFixtures.TotalFlags };
            string players = BotFixtures.PlayersSignature(s.Players);
            new ProgramEvaluator(s, new FlagDistances(s.Board, s.TotalFlags)).PlanShutDown(2);
            Assert.Equal(players, BotFixtures.PlayersSignature(s.Players));
        }
    }
}
