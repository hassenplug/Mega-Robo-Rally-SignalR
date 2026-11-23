using MRR.Bot;

namespace MRR.Tests
{
    /// <summary>
    /// M4: Expert's timing (BotTiming, design §5.7) and its re-rank against sampled opponents (§5.2).
    /// The timing tests use a hand-written fake clock, so no test package or real time is needed.
    /// </summary>
    public class BotExpertTests
    {
        private sealed class FakeClock { public long Now; }

        private static BotTiming Timing(FakeClock clock, bool humanClock, bool hasExpert, long solo = 5000) =>
            new(humanClock, hasExpert, solo, graceSoftMs: 5000, graceHardMs: 10000, () => clock.Now);

        // ---- timing -------------------------------------------------------------------------

        [Fact]
        public void Levels1To4_StopWhenTheHumansAreDone_AndNeverHoldTheGate()
        {
            var clock = new FakeClock();
            var t = Timing(clock, humanClock: true, hasExpert: false);
            Assert.False(t.StopFor(4));

            clock.Now = 2500;
            Assert.True(t.MayLockIn());        // the gate's call means the humans are done...
            Assert.True(t.HumansDone);         // ...so it stamps that
            Assert.Equal(2500, t.HumansDoneAtMs);
            Assert.True(t.StopFor(3));
        }

        [Fact]
        public void Expert_SoftThenHardDeadline_AfterTheHumans()
        {
            var clock = new FakeClock { Now = 1000 };
            var t = Timing(clock, humanClock: true, hasExpert: true);
            t.StampHumansDone();
            Assert.True(t.StopFor(4));         // Hard stops at once
            Assert.False(t.StopFor(5));        // Expert keeps going

            clock.Now = 5999; Assert.True(t.MayStartRound); Assert.False(t.MayLockIn());
            clock.Now = 6000; Assert.False(t.MayStartRound); Assert.False(t.StopFor(5)); // finish the round in hand
            clock.Now = 10999; Assert.False(t.MayLockIn());
            clock.Now = 11000; Assert.True(t.StopFor(5)); Assert.True(t.MayLockIn());
        }

        [Fact]
        public void Expert_GateDeadline_HoldsWithoutAnyWorker()
        {
            // Nothing but the clock moves: the gate still opens at the hard deadline.
            var clock = new FakeClock();
            var t = Timing(clock, humanClock: true, hasExpert: true);
            Assert.False(t.MayLockIn());       // stamps humans done at 0
            clock.Now = 9999; Assert.False(t.MayLockIn());
            clock.Now = 10000; Assert.True(t.MayLockIn());
        }

        [Fact]
        public void NoHumanClock_BudgetForLevels2To4_GraceForExpert()
        {
            var clock = new FakeClock();
            var plain = Timing(clock, humanClock: false, hasExpert: false, solo: 3000);
            var expert = Timing(clock, humanClock: false, hasExpert: true, solo: 3000);

            clock.Now = 2999; Assert.False(plain.MayLockIn()); Assert.False(plain.StopFor(4));
            clock.Now = 3000; Assert.True(plain.MayLockIn()); Assert.True(plain.StopFor(4));
            Assert.False(expert.MayLockIn());  // Expert's grace (10 s) is larger than the budget
            clock.Now = 10000; Assert.True(expert.MayLockIn()); Assert.True(expert.StopFor(5));
        }

        [Theory]
        [InlineData(1, 12)]
        [InlineData(2, 12)]
        [InlineData(3, 8)]
        [InlineData(4, 6)]
        [InlineData(5, 4)]
        public void CandidatesShrink_AsExpertSeatsGrow(int experts, int candidates) =>
            Assert.Equal(candidates, new ReRankSettings().CandidatesFor(experts));

        // ---- opponent sampler ---------------------------------------------------------------

        private static (BotSnapshot, ProgramEvaluator) Fixture(int phases = 3)
        {
            var s = new BotSnapshot { Board = BotFixtures.Board(), Players = BotFixtures.Players(), PhaseCount = phases, TotalFlags = BotFixtures.TotalFlags, LaserDamage = 1 };
            return (s, new ProgramEvaluator(s, new FlagDistances(s.Board, s.TotalFlags)));
        }

        [Fact]
        public void OpponentWorld_IsRepeatable_LegalFromTheDeck_AndSkipsInactiveRobots()
        {
            var (s, ev) = Fixture();
            s.Players.GetPlayer(3)!.PlayerStatus = tPlayerStatus.ShutDown;
            s.Players.GetPlayer(4)!.PlayerStatus = tPlayerStatus.Dead;

            var a = OpponentSampler.Sample(ev, s, seed: 7, () => false)!;
            var b = OpponentSampler.Sample(ev, s, seed: 7, () => false)!;

            Assert.Equal([1, 2, 5, 6], a.Keys.OrderBy(k => k));
            foreach (var (robot, program) in a)
            {
                Assert.Equal(b[robot], program);
                Assert.Equal(3, program.Count);
                foreach (var g in program.GroupBy(t => t))
                    Assert.True(BotSnapshot.StandardDeck.Count(t => t == g.Key) >= g.Count());
            }
        }

        [Fact]
        public void WorldEvaluation_LeavesSnapshotUnchanged()
        {
            var (s, ev) = Fixture();
            string players = BotFixtures.PlayersSignature(s.Players), board = BotFixtures.BoardSignature(s.Board);
            var world = OpponentSampler.Sample(ev, s, 1, () => false)!;
            ev.EvaluateInWorld(1, world[1], Direction.Down, world);
            Assert.Equal(players, BotFixtures.PlayersSignature(s.Players));
            Assert.Equal(board, BotFixtures.BoardSignature(s.Board));
        }

        // ---- re-rank in the scheduler -------------------------------------------------------

        private static readonly IReadOnlyList<int> Hand = BotFixtures.Hand.Select(t => (int)t).ToList();

        [Fact]
        public void Expert_RunsEveryRound_WhenAllowed()
        {
            var (s, ev) = Fixture();
            int rounds = 0;
            bool complete = BotScheduler.Run(s, [new BotSeat(1, 5, Hand)], new Dictionary<int, IReadOnlyList<int>>(), ev, new Random(1),
                shouldStop: () => false, onImproved: (_, _, _) => true,
                mayStartRound: () => { rounds++; return true; }, reRank: new ReRankSettings(Rounds: 3));
            Assert.True(complete);
            Assert.Equal(3, rounds);
        }

        [Fact]
        public void Expert_StartsNoRound_AfterTheSoftDeadline()
        {
            var (s, ev) = Fixture();
            var stages = new List<int>();
            bool complete = BotScheduler.Run(s, [new BotSeat(1, 5, Hand)], new Dictionary<int, IReadOnlyList<int>>(), ev, new Random(1),
                shouldStop: () => false, onImproved: (_, _, stage) => { stages.Add(stage); return true; },
                mayStartRound: () => false, reRank: new ReRankSettings(Rounds: 3));
            Assert.False(complete);
            Assert.All(stages, st => Assert.True(st <= 3, $"stage {st} is a re-rank round"));
        }

        [Fact]
        public void Expert_ReRank_StartsWhileHardIsStillSearching()
        {
            // Breadth-first (option 1): Expert's first round comes before Hard's last stage ends.
            var (s, ev) = Fixture();
            long calls = 0, firstRound = -1, lastHard = -1;
            var seats = new[] { new BotSeat(1, 4, Hand), new BotSeat(2, 5, Hand) };
            BotScheduler.Run(s, seats, new Dictionary<int, IReadOnlyList<int>>(), ev, new Random(1),
                shouldStop: seat => { calls++; if (seat.Level == 4) lastHard = calls; return false; },
                onImproved: (_, _, _) => true,
                mayStartRound: () => { if (firstRound < 0) firstRound = calls; return true; },
                reRank: new ReRankSettings(Rounds: 2));
            Assert.True(firstRound > 0 && firstRound < lastHard, $"first round at {firstRound}, Hard's last check at {lastHard}");
        }

        [Fact]
        public void Expert_KeepsSearching_AfterTheOthersStop()
        {
            // Hard is told to stop at once; Expert is not, and finishes its rounds.
            var (s, ev) = Fixture();
            int rounds = 0;
            var applied = new List<int>();
            var seats = new[] { new BotSeat(1, 4, Hand), new BotSeat(2, 5, Hand) };
            BotScheduler.Run(s, seats, new Dictionary<int, IReadOnlyList<int>>(), ev, new Random(1),
                shouldStop: seat => seat.Level == 4,
                onImproved: (seat, _, _) => { applied.Add(seat.RobotID); return true; },
                mayStartRound: () => { rounds++; return true; }, reRank: new ReRankSettings(Rounds: 2));
            Assert.DoesNotContain(1, applied);
            Assert.Equal(2, rounds);
        }

        [Fact]
        public void Expert_CutMidRound_DiscardsThatRound()
        {
            var (s, ev) = Fixture();
            int rounds = 0;
            long cutAt = long.MaxValue;
            var stages = new List<int>();
            bool complete = BotScheduler.Run(s, [new BotSeat(1, 5, Hand)], new Dictionary<int, IReadOnlyList<int>>(), ev, new Random(1),
                shouldStop: () => ev.PlansRun >= cutAt,
                onImproved: (_, _, stage) => { stages.Add(stage); return true; },
                mayStartRound: () =>
                {
                    // Cut a few plans into the second round (after its world is sampled).
                    if (++rounds == 2) cutAt = ev.PlansRun + 25;
                    return true;
                },
                reRank: new ReRankSettings(Rounds: 3));
            Assert.False(complete);
            Assert.All(stages, st => Assert.True(st <= 4, $"stage {st}: only stages 1-3 and round 1 finished"));
        }
    }
}
