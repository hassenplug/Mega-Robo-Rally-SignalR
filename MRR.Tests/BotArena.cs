using System.Text;
using MRR.Bot;
using Xunit.Abstractions;

namespace MRR.Tests
{
    /// <summary>
    /// M4's arena (COMPUTER_PLAYER_DESIGN.md §6): one computer of each level, 1-5, plays whole
    /// games on the busy fixture board with no database, to check that each level beats the one
    /// below.
    ///
    /// The applier is APPROXIMATE (design §8): after the real planner plans the turn with every
    /// robot's program, it takes each robot's final position, counts its flags, adds one Spam to
    /// its damage pile per DealSpamCard, and respawns a dead robot on its start square. Hands are
    /// dealt from the deck mix plus the damage pile; unplayed Spam stays in hand; shutting down
    /// clears hand Spam and the damage pile. Start squares rotate between games so no level keeps a
    /// lucky start.
    ///
    /// Opt-in and slow (minutes on the Pi): MRR_ARENA=1, optional MRR_ARENA_GAMES (5) and
    /// MRR_ARENA_TURNS (6). Pass/fail margin, set before running (2026-11-22): each level's total
    /// score must be at least the level below's. Score = 1000 per flag − 30 per square from the
    /// next flag at the end.
    /// </summary>
    public class BotArena(ITestOutputHelper output)
    {
        private sealed class Robot
        {
            public int Level;
            public PlayerState State = null!;
            public (int X, int Y, Direction Dir) Start;
            public List<int> Hand = [];
            public int DamagePile;
            public int Flags, Deaths, Damage, ShutDowns;
        }

        [Fact]
        [Trait("Category", "Arena")]
        public void LevelsBeatTheLevelBelow()
        {
            if (Environment.GetEnvironmentVariable("MRR_ARENA") != "1") return; // opt-in
            int games = int.TryParse(Environment.GetEnvironmentVariable("MRR_ARENA_GAMES"), out var g) ? g : 5;
            int turns = int.TryParse(Environment.GetEnvironmentVariable("MRR_ARENA_TURNS"), out var t) ? t : 6;

            var board = BotFixtures.Board();
            var distances = new FlagDistances(board, BotFixtures.TotalFlags);
            var starts = BotFixtures.Players().Take(5).Select(p => (p.CurrentPos.X, p.CurrentPos.Y, p.CurrentPos.Direction)).ToArray();
            var total = new double[6];
            var flags = new int[6]; var deaths = new int[6]; var damage = new int[6]; var shut = new int[6];
            var clock = System.Diagnostics.Stopwatch.StartNew();

            for (int game = 0; game < games; game++)
            {
                var rng = new Random(1000 + game);
                var robots = Enumerable.Range(1, 5).Select(level =>
                {
                    var start = starts[(level - 1 + game) % starts.Length];
                    var state = new PlayerState { ID = level, Name = "L" + level, PlayerStatus = tPlayerStatus.ReadyToRun,
                                                  Priority = level, Energy = 3, CurrentPos = new RobotLocation(start.Item3, start.Item1, start.Item2) };
                    state.NextPos = new RobotLocation(state.CurrentPos);
                    return new Robot { Level = level, State = state, Start = start };
                }).ToList();

                for (int turn = 0; turn < turns; turn++)
                {
                    foreach (var r in robots) Deal(r, rng);
                    var players = new PlayerStates();
                    foreach (var r in robots) players.Add(Copy(r.State));
                    var snapshot = new BotSnapshot { Board = board, Players = players, PhaseCount = 5, TotalFlags = BotFixtures.TotalFlags, LaserDamage = 1 };

                    var programs = new Dictionary<int, IReadOnlyList<int>>();
                    var shuttingDown = new HashSet<int>();
                    foreach (var r in robots)
                    {
                        var (program, down) = Decide(snapshot, distances, r, rng);
                        programs[r.Level] = program;
                        if (down) shuttingDown.Add(r.Level);
                    }
                    Execute(board, robots, programs, shuttingDown, rng);
                    // Priority rotates each turn, as in the game.
                    foreach (var r in robots) r.State.Priority = r.State.Priority % robots.Count + 1;
                }

                foreach (var r in robots)
                {
                    int next = r.State.LastFlag + 1;
                    int d = next > BotFixtures.TotalFlags ? 0 : Math.Min(distances.Distance(next, r.State.CurrentPos.X, r.State.CurrentPos.Y), 24);
                    total[r.Level] += r.Flags * 1000 - d * 30;
                    flags[r.Level] += r.Flags; deaths[r.Level] += r.Deaths; damage[r.Level] += r.Damage; shut[r.Level] += r.ShutDowns;
                }
            }

            var report = new StringBuilder($"Arena: {games} games x {turns} turns, {clock.Elapsed.TotalSeconds:0} s\n");
            report.AppendLine("level    score  flags  deaths  spam  shutdowns");
            for (int level = 1; level <= 5; level++)
                report.AppendLine($"{BotProfile.ForLevel(level).Name,-8} {total[level],6:0} {flags[level],6} {deaths[level],7} {damage[level],5} {shut[level],10}");
            output.WriteLine(report.ToString());
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "bot-arena-results.txt"), report.ToString());

            for (int level = 2; level <= 5; level++)
                Assert.True(total[level] >= total[level - 1], report.ToString());
        }

        private static PlayerState Copy(PlayerState p) =>
            new(p) { PlayerStatus = p.PlayerStatus, PositionValid = true };

        /// <summary>Unplayed Spam stays; the rest of the nine come from the deck mix plus the damage pile.</summary>
        private static void Deal(Robot r, Random rng)
        {
            const int spam = (int)MoveCard.tCardType.Spam;
            r.Hand = r.Hand.Where(c => c == spam).ToList();
            var pool = BotSnapshot.StandardDeck.Concat(Enumerable.Repeat(spam, r.DamagePile)).OrderBy(_ => rng.Next()).ToList();
            foreach (int card in pool.Take(Math.Max(0, 9 - r.Hand.Count)))
            {
                r.Hand.Add(card);
                if (card == spam) r.DamagePile--;
            }
        }

        private static (IReadOnlyList<int> Program, bool ShutDown) Decide(BotSnapshot snapshot, FlagDistances distances, Robot r, Random rng)
        {
            var placeholder = RandomBrain.Decide(r.Hand, snapshot.PhaseCount, 1, false, rng).CardTypes;
            if (r.Level == 1) return (placeholder, false);

            var evaluator = new ProgramEvaluator(snapshot, distances);
            var pool = BotSnapshot.StandardDeck.Concat(Enumerable.Repeat((int)MoveCard.tCardType.Spam, r.DamagePile)).ToList();
            var seat = new BotSeat(r.Level, r.Level, r.Hand, r.DamagePile, pool);
            IReadOnlyList<int> program = placeholder;
            bool down = false;
            BotScheduler.Run(snapshot, [seat], new Dictionary<int, IReadOnlyList<int>> { [r.Level] = placeholder }, evaluator, new Random(rng.Next()),
                shouldStop: () => false,
                onImproved: (_, p, _) => { program = p.Program; return true; },
                onShutDown: (_, v) => { down = v.ShutDown; return true; });
            return (program, down);
        }

        /// <summary>Plans the turn with every program and applies the approximate result.</summary>
        private static void Execute(BoardElementCollection board, List<Robot> robots, Dictionary<int, IReadOnlyList<int>> programs,
                                    HashSet<int> shuttingDown, Random rng)
        {
            const int spam = (int)MoveCard.tCardType.Spam;
            var players = new PlayerStates();
            var cards = new CardList();
            var piles = new Dictionary<int, List<MoveCard>>();
            foreach (var r in robots)
            {
                var p = Copy(r.State);
                if (shuttingDown.Contains(r.Level)) { p.PlayerStatus = tPlayerStatus.ShutDown; p.ShutDown = tShutDown.Currently; }
                players.Add(p);
                if (shuttingDown.Contains(r.Level)) continue;
                var program = programs[r.Level];
                for (int i = 0; i < program.Count; i++)
                    cards.Add(new MoveCard(r.Level * 100 + i, (MoveCard.tCardType)program[i]) { Owner = r.Level, PhasePlayed = i + 1 });
                piles[r.Level] = BotSnapshot.StandardDeck.OrderBy(_ => rng.Next())
                    .Select((type, i) => new MoveCard(r.Level * 1000 + i, (MoveCard.tCardType)type) { Owner = r.Level }).ToList();
            }

            var plan = new CreateCommands(new TurnRequest
            {
                Turn = 1, PhaseCount = 5, GameState = 6, TotalFlags = BotFixtures.TotalFlags, LaserDamage = 1,
                Board = board, Players = players, GameCards = cards, DrawPiles = piles,
            }).CreateTurn();

            foreach (var r in robots)
            {
                var mine = plan.Commands.Where(c => c.RobotID == r.Level).ToList();
                int newFlags = mine.Count(c => c.CommandType == SquareAction.Flag && c.Value > 0);
                int hits = mine.Count(c => c.CommandType == SquareAction.DealSpamCard);
                bool dead = mine.Any(c => c.CommandType == SquareAction.Dead);

                r.Flags += newFlags; r.State.LastFlag += newFlags; r.Damage += hits;

                if (shuttingDown.Contains(r.Level))
                {
                    r.ShutDowns++;
                    r.Hand.RemoveAll(c => c == spam);
                    r.DamagePile = 0;
                }
                else
                {
                    // Played Spam leaves the game; the rest of the program is discarded.
                    var program = programs[r.Level];
                    foreach (int card in program) r.Hand.Remove(card);
                }
                r.DamagePile += hits;

                if (dead)
                {
                    r.Deaths++;
                    r.DamagePile += 2;
                    r.State.CurrentPos = new RobotLocation(r.Start.Dir, r.Start.X, r.Start.Y);
                }
                else
                {
                    var last = mine.LastOrDefault(c => c.CommandType is SquareAction.Move or SquareAction.PushedMove or SquareAction.BoardMove
                        or SquareAction.Rotate or SquareAction.BoardRotate or SquareAction.BoardMoveRotate or SquareAction.PushedMoveRotate);
                    if (last != null) r.State.CurrentPos = new RobotLocation(last.EndPos);
                }
                r.State.NextPos = new RobotLocation(r.State.CurrentPos);
            }
        }
    }
}
