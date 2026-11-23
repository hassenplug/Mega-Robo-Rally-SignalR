using MRR.Bot;
using T = MRR.MoveCard.tCardType;

namespace MRR.Tests
{
    /// <summary>
    /// M3: facing after a reboot (§5.5) and sampling a Spam's replacement (§5.4).
    /// </summary>
    public class BotRebootAndSpamTests
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

        private static PlayerStates OneRobot(int x, int y, Direction dir)
        {
            var p = new PlayerState { ID = 1, Name = "R1", PlayerStatus = tPlayerStatus.ReadyToRun, Priority = 1, Energy = 3, CurrentPos = new RobotLocation(dir, x, y) };
            p.NextPos = new RobotLocation(p.CurrentPos);
            return [p];
        }

        private static List<int> Hand(params T[] types) => types.Select(t => (int)t).ToList();

        private static (BotSnapshot, ProgramEvaluator) Setup(BoardElementCollection board, PlayerStates players, int phases = 5)
        {
            var s = new BotSnapshot { Board = board, Players = players, PhaseCount = phases, TotalFlags = 1, LaserDamage = 1 };
            return (s, new ProgramEvaluator(s, new FlagDistances(board, 1)));
        }

        private static ScoredProgram? Search(BotSnapshot s, ProgramEvaluator ev, BotSeat seat)
        {
            ScoredProgram? last = null;
            BotScheduler.Run(s, [seat], new Dictionary<int, IReadOnlyList<int>>(), ev, new Random(1),
                shouldStop: () => false, onImproved: (_, p, _) => { last = p; return true; });
            return last;
        }

        // No turns in hand: only the reboot facing can point it at the flag.
        private static readonly List<int> NoTurns =
            Hand(T.Forward1, T.Forward2, T.Forward3, T.Back1, T.Forward1, T.Again, T.Forward2, T.Back1, T.Forward1);

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        public void Reboot_NormalAndUp_ChooseTheFacingThatReachesTheFlag(int level)
        {
            var (s, ev) = Setup(EmptyBoard(8, (4, 4)), OneRobot(1, 4, Direction.Down));
            var best = Search(s, ev, new BotSeat(1, level, NoTurns, ChooseFacing: true))!;

            Assert.Equal(Direction.Right, best.Facing);
            Assert.Contains(ev.Plan(1, best.Program, best.Facing).Commands, c => c.RobotID == 1 && c.CommandType == SquareAction.Flag && c.Value > 0);
        }

        [Fact]
        public void Reboot_Easy_FacesAlongThePath()
        {
            var (s, ev) = Setup(EmptyBoard(8, (4, 4)), OneRobot(1, 4, Direction.Down));
            Assert.Equal(Direction.Right, ev.FacingTowardsFlag(1));
            Assert.Equal(Direction.Right, Search(s, ev, new BotSeat(1, 2, NoTurns, ChooseFacing: true))!.Facing);
        }

        [Fact]
        public void NoReboot_KeepsItsFacing()
        {
            var (s, ev) = Setup(EmptyBoard(8, (4, 4)), OneRobot(1, 4, Direction.Down));
            Assert.Null(Search(s, ev, new BotSeat(1, 4, NoTurns))!.Facing);
        }

        [Fact]
        public void FacingVariants_LeaveSnapshotUnchanged()
        {
            var (s, ev) = Setup(EmptyBoard(8, (4, 4)), OneRobot(1, 4, Direction.Down));
            ev.Evaluate(1, Hand(T.Forward3), Direction.Right);
            Assert.Equal(Direction.Down, s.Players.GetPlayer(1)!.CurrentPos.Direction);
            Assert.Equal(Direction.Right, ev.PlayersFor(1, Direction.Right).GetPlayer(1)!.CurrentPos.Direction);
            Assert.Equal(tPlayerStatus.ReadyToRun, ev.PlayersFor(1, Direction.Right).GetPlayer(1)!.PlayerStatus);
        }

        [Fact]
        public void Spam_IsScoredFromTheDrawPool()
        {
            // Flag three squares ahead; every card it could draw is a Forward 3.
            var (_, ev) = Setup(EmptyBoard(8, (4, 4)), OneRobot(1, 4, Direction.Right), phases: 1);
            var spam = Hand(T.Spam);
            var pool = Hand(T.Forward3, T.Forward3, T.Forward3, T.Forward3);

            double unsampled = ev.Evaluate(1, spam);
            double sampled = ev.Evaluate(1, spam, drawPool: pool, spamSamples: 3);
            Assert.True(sampled >= 1000, $"sampled {sampled}");   // touches the flag
            Assert.True(unsampled < 0, $"unsampled {unsampled}"); // moves nothing
        }

        [Fact]
        public void SpamSamples_AreRepeatable_AndAverage()
        {
            // Half the pool reaches the flag, half turns away: the mean lies between the two.
            var (_, ev) = Setup(EmptyBoard(8, (4, 4)), OneRobot(1, 4, Direction.Right), phases: 1);
            var spam = Hand(T.Spam);
            var pool = Hand(T.Forward3, T.Forward3, T.UTurn, T.UTurn);

            double a = ev.Evaluate(1, spam, drawPool: pool, spamSamples: 8);
            double b = ev.Evaluate(1, spam, drawPool: pool, spamSamples: 8);
            Assert.Equal(a, b);
            Assert.True(a > ev.Evaluate(1, Hand(T.UTurn)) && a < ev.Evaluate(1, Hand(T.Forward3)), $"mean {a}");
        }

        [Fact]
        public void ProgramsWithoutSpam_ArePlannedOnce()
        {
            var (_, ev) = Setup(EmptyBoard(8, (4, 4)), OneRobot(1, 4, Direction.Right), phases: 2);
            ev.Evaluate(1, Hand(T.Forward1, T.RTurn), drawPool: Hand(T.Forward3), spamSamples: 5);
            Assert.Equal(1, ev.PlansRun);
            ev.Evaluate(1, Hand(T.Spam, T.RTurn), drawPool: Hand(T.Forward3), spamSamples: 5);
            Assert.Equal(6, ev.PlansRun);
        }
    }
}
