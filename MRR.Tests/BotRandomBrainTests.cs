using MRR.Bot;
using T = MRR.MoveCard.tCardType;

namespace MRR.Tests
{
    /// <summary>
    /// Level 1 (random) brain: always legal, avoids wasted registers when it can.
    /// COMPUTER_PLAYER_DESIGN.md §7 "Legality".
    /// </summary>
    public class BotRandomBrainTests
    {
        private static List<int> Hand(params T[] types) => types.Select(t => (int)t).ToList();

        private static void AssertLegal(IReadOnlyList<int> hand, int phaseCount, Decision d)
        {
            Assert.Equal(Math.Min(phaseCount, hand.Count), d.CardTypes.Count);
            foreach (var group in d.CardTypes.GroupBy(t => t))
                Assert.True(hand.Count(t => t == group.Key) >= group.Count(),
                    $"type {group.Key} used {group.Count()}x but hand holds {hand.Count(t => t == group.Key)}");
        }

        [Fact]
        public void RandomHands_AlwaysLegal()
        {
            var rng = new Random(1234);
            var deckTypes = new[] { T.UTurn, T.RTurn, T.LTurn, T.Back1, T.Forward1, T.Forward2, T.Forward3, T.Again, T.PowerUp, T.Spam };

            for (int i = 0; i < 2000; i++)
            {
                int handSize = rng.Next(0, 10);
                var hand = Enumerable.Range(0, handSize).Select(_ => (int)deckTypes[rng.Next(deckTypes.Length)]).ToList();
                int phaseCount = rng.Next(1, 6);

                var d = RandomBrain.Decide(hand, phaseCount, currentFacing: 1, facingNeeded: false, rng);
                AssertLegal(hand, phaseCount, d);
            }
        }

        [Fact]
        public void Spam_OnlyWhenTooFewOtherCards()
        {
            var rng = new Random(7);
            var hand = Hand(T.Spam, T.Spam, T.Forward1, T.Forward2, T.RTurn, T.LTurn, T.UTurn, T.Back1, T.Forward3);
            for (int i = 0; i < 200; i++)
                Assert.DoesNotContain((int)T.Spam, RandomBrain.Decide(hand, 5, 1, false, rng).CardTypes);

            // Only four non-Spam cards for five registers: exactly one Spam must be played.
            var clogged = Hand(T.Spam, T.Spam, T.Spam, T.Spam, T.Spam, T.Forward1, T.Forward2, T.RTurn, T.LTurn);
            var d = RandomBrain.Decide(clogged, 5, 1, false, rng);
            AssertLegal(clogged, 5, d);
            Assert.Equal(1, d.CardTypes.Count(t => t == (int)T.Spam));
        }

        [Fact]
        public void Again_NotInRegisterOne_WhenAvoidable()
        {
            var rng = new Random(99);
            var hand = Hand(T.Again, T.Again, T.Forward1, T.RTurn, T.LTurn, T.UTurn, T.Back1, T.Forward2, T.Forward3);
            for (int i = 0; i < 500; i++)
            {
                var d = RandomBrain.Decide(hand, 5, 1, false, rng);
                AssertLegal(hand, 5, d);
                Assert.NotEqual((int)T.Again, d.CardTypes[0]);
            }

            // A hand of nothing but Again: still a legal program.
            var allAgain = Hand(T.Again, T.Again, T.Again, T.Again, T.Again);
            AssertLegal(allAgain, 5, RandomBrain.Decide(allAgain, 5, 1, false, rng));
        }

        [Fact]
        public void Facing_ConfirmedOnlyWhenNeeded()
        {
            var hand = Hand(T.Forward1, T.Forward2, T.Forward3, T.RTurn, T.LTurn);
            Assert.Null(RandomBrain.Decide(hand, 5, 3, facingNeeded: false, new Random(1)).Facing);
            Assert.Equal(3, RandomBrain.Decide(hand, 5, 3, facingNeeded: true, new Random(1)).Facing);
        }

        [Fact]
        public void SameSeed_SameProgram()
        {
            var hand = Hand(T.Forward1, T.Forward2, T.Forward3, T.RTurn, T.LTurn, T.UTurn, T.Back1, T.Again, T.Spam);
            var a = RandomBrain.Decide(hand, 5, 1, false, new Random(42));
            var b = RandomBrain.Decide(hand, 5, 1, false, new Random(42));
            Assert.Equal(a.CardTypes, b.CardTypes);
        }

        [Theory]
        [InlineData(-3, 1, "Beginner")]
        [InlineData(1, 1, "Beginner")]
        [InlineData(3, 3, "Normal")]
        [InlineData(5, 5, "Expert")]
        [InlineData(99, 5, "Expert")]
        public void Profile_ClampsLevel(int asked, int level, string name)
        {
            var p = BotProfile.ForLevel(asked);
            Assert.Equal(level, p.Level);
            Assert.Equal(name, p.Name);
            Assert.True($"CPU 8 {p.Name}".Length <= 20, "OperatorName is varchar(20)");
        }
    }
}
