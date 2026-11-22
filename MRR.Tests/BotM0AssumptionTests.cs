namespace MRR.Tests;

/// <summary>
/// Computer-player M0 (documents/COMPUTER_PLAYER_DESIGN.md §6, §9): checks the things the plan
/// *assumes* about the turn planner before any bot code is written. If one of these fails, the
/// plan's search design has to change, which is the whole point of running them first.
///
/// Two of the tests are "characterization" tests: they pin down planner behaviour the plan
/// works AROUND (it overwrites card priorities and consumes draw piles), so a bot must copy
/// those inputs. If the planner is later made side-effect free, delete them -- they do not
/// describe a rule, only a fact about today's code.
/// </summary>
public class BotM0AssumptionTests
{
    // ---- fixture sanity ----------------------------------------------------------------------

    [Fact]
    public void Fixture_BoardIsBusyEnoughToBeWorthMeasuring()
    {
        var board = BotFixtures.Board();

        Assert.Equal(BotFixtures.Size * BotFixtures.Size, board.BoardElements.Count);
        Assert.Equal(BotFixtures.TotalFlags, board.CalcTotalFlags());
        Assert.Equal(3, board.BoardElements.Count(e => e.Type == SquareType.Pit));
        Assert.True(board.BoardElements.Count(e => e.ActionList.Any(a => a.SquareAction == SquareAction.Move)) >= 20);
    }

    [Fact]
    public void Fixture_EveryRandomProgramPlans()
    {
        var board = BotFixtures.Board();
        var players = BotFixtures.Players();
        for (int seed = 0; seed < 20; seed++)
        {
            var plan = BotFixtures.PlanSolo(board, players, 1, BotFixtures.RandomProgram(seed));
            Assert.True(plan.Planned, plan.Summary);
        }
    }

    // ---- H1: same input, same plan -----------------------------------------------------------

    [Fact]
    public void Plan_IsDeterministic_ForTheSameInputs()
    {
        var board = BotFixtures.Board();
        var players = BotFixtures.Players();

        for (int seed = 0; seed < 25; seed++)
        {
            var program = BotFixtures.RandomProgram(seed);
            string first = BotFixtures.Signature(BotFixtures.PlanSolo(board, players, 1, program));
            string second = BotFixtures.Signature(BotFixtures.PlanSolo(board, players, 1, program));
            Assert.Equal(first, second);
        }

        // The same with every robot programmed (Expert's opponent worlds), where card priority
        // between robots decides who moves first.
        for (int seed = 0; seed < 15; seed++)
        {
            string Plan()
            {
                var cards = new CardList();
                for (int robot = 1; robot <= 6; robot++)
                    BotFixtures.AddProgram(cards, robot, BotFixtures.RandomProgram(seed * 10 + robot));
                return BotFixtures.Signature(new CreateCommands(BotFixtures.Request(board, players, cards, 5)).CreateTurn());
            }
            Assert.Equal(Plan(), Plan());
        }

        // Negative control: the signature must be able to tell plans apart, or the equality
        // above proves nothing.
        var distinct = Enumerable.Range(0, 25)
            .Select(seed => BotFixtures.Signature(BotFixtures.PlanSolo(board, players, 1, BotFixtures.RandomProgram(seed))))
            .Distinct().Count();
        Assert.True(distinct >= 10, $"only {distinct} distinct plans from 25 different programs");
    }

    // ---- H2: the board and the robots are read-only to the planner ---------------------------

    [Fact]
    public void Plan_DoesNotChangeTheBoardOrThePlayers_WhenNoOptionCardsAreInPlay()
    {
        var board = BotFixtures.Board();
        var players = BotFixtures.Players();
        string boardBefore = BotFixtures.BoardSignature(board);
        string playersBefore = BotFixtures.PlayersSignature(players);

        for (int seed = 0; seed < 15; seed++)
        {
            // All six robots programmed, so pushes, belts, lasers and deaths all get a chance to
            // touch shared state.
            var cards = new CardList();
            for (int robot = 1; robot <= 6; robot++)
                BotFixtures.AddProgram(cards, robot, BotFixtures.RandomProgram(seed * 10 + robot));
            var plan = new CreateCommands(BotFixtures.Request(board, players, cards, 5)).CreateTurn();
            Assert.True(plan.Planned, plan.Summary);
        }

        Assert.Equal(boardBefore, BotFixtures.BoardSignature(board));
        Assert.Equal(playersBefore, BotFixtures.PlayersSignature(players));
    }

    // ---- H3: a cardless robot is an inert obstacle whose laser is still live -----------------

    private static TurnPlan PlanShootout(MoveCard.tCardType myCard)
    {
        // Robot 1 at (4,4) facing Down. Robot 2 is cardless at (1,5) facing Right, so it fires
        // along row 5. Whether robot 1 ends in row 5 is up to its one card.
        var me = new PlayerState
        {
            ID = 1, PlayerStatus = tPlayerStatus.ReadyToRun, Priority = 1,
            CurrentPos = new RobotLocation(Direction.Down, 4, 4),
        };
        me.NextPos = new RobotLocation(me.CurrentPos);
        var shooter = new PlayerState
        {
            ID = 2, PlayerStatus = tPlayerStatus.ReadyToRun, Priority = 2,
            CurrentPos = new RobotLocation(Direction.Right, 1, 5),
        };
        shooter.NextPos = new RobotLocation(shooter.CurrentPos);

        var cards = new CardList();
        BotFixtures.AddProgram(cards, 1, [myCard]);
        var request = BotFixtures.Request(new BoardElementCollection(12, 12), [me, shooter], cards, 1);

        var plan = new CreateCommands(request).CreateTurn();
        Assert.True(plan.Planned, plan.Summary);
        return plan;
    }

    private static bool ShooterHitRobot1(TurnPlan plan) =>
        plan.Commands.Any(c => c.CommandType == SquareAction.FireCannon && c.RobotID == 2 && c.Value == 1);

    [Fact]
    public void CardlessRobot_FiresAtMeWhenIEndInItsLine()
    {
        var plan = PlanShootout(MoveCard.tCardType.Forward1); // (4,4) -> (4,5): into row 5

        Assert.True(ShooterHitRobot1(plan));
        Assert.Contains(plan.Commands, c => c.RobotID == 1 && c.CommandType == SquareAction.DealSpamCard);
    }

    [Fact]
    public void CardlessRobot_DoesNotHitMeWhenIStayOutOfItsLine()
    {
        var plan = PlanShootout(MoveCard.tCardType.RTurn); // stays at (4,4)

        Assert.False(ShooterHitRobot1(plan));
        Assert.DoesNotContain(plan.Commands, c => c.RobotID == 1 && c.CommandType == SquareAction.DealSpamCard);
    }

    [Fact]
    public void CardlessRobot_NeverMovesOnItsOwn()
    {
        var plan = PlanShootout(MoveCard.tCardType.Forward1);

        Assert.DoesNotContain(plan.Commands, c => c.RobotID == 2 &&
            (c.CommandType == SquareAction.Move || c.CommandType == SquareAction.Rotate));
    }

    // ---- H4: a prefix plan is exactly the first k phases of the full plan --------------------

    private static string PhaseCommands(TurnPlan plan, int upToPhase) =>
        string.Join("\n", plan.Commands
            .Where(c => c.Phase <= upToPhase && c.CommandType != SquareAction.SetCurrentGameData)
            .Select(BotFixtures.CommandKey));

    [Theory]
    [InlineData(false)] // only robot 1 holds cards (Levels 2-4's model)
    [InlineData(true)]  // all six hold cards (Expert's opponent worlds)
    public void PrefixPlan_MatchesTheFirstPhasesOfTheFullPlan(bool allRobotsProgrammed)
    {
        var board = BotFixtures.Board();
        var players = BotFixtures.Players();

        TurnPlan Plan(int seed, int phases)
        {
            var cards = new CardList();
            for (int robot = 1; robot <= (allRobotsProgrammed ? 6 : 1); robot++)
                BotFixtures.AddProgram(cards, robot, BotFixtures.RandomProgram(seed * 10 + robot).Take(phases).ToArray());
            return new CreateCommands(BotFixtures.Request(board, players, cards, phases)).CreateTurn();
        }

        int moves = 0, boardMoves = 0, damage = 0, commands = 0;
        for (int seed = 0; seed < 30; seed++)
        {
            var full = Plan(seed, 5);
            for (int k = 1; k <= 4; k++)
            {
                var prefix = Plan(seed, k);
                Assert.True(prefix.Planned, prefix.Summary);
                string expected = PhaseCommands(full, k);
                Assert.Equal(expected, PhaseCommands(prefix, k));

                var compared = prefix.Commands.Where(c => c.Phase <= k && c.CommandType != SquareAction.SetCurrentGameData).ToList();
                commands += compared.Count;
                moves += compared.Count(c => c.CommandType == SquareAction.Move);
                boardMoves += compared.Count(c => c.CommandType == SquareAction.BoardMove);
                damage += compared.Count(c => c.CommandType == SquareAction.DealSpamCard);
            }
        }

        // Guard against a vacuous pass: the plans being compared must really contain robots
        // driving, belts carrying them and lasers hitting them.
        Assert.True(commands > 1000, $"only {commands} commands compared");
        Assert.True(moves > 50, $"only {moves} card moves compared");
        Assert.True(boardMoves > 0, "no belt moves compared -- the fixture board's belts are not being used");
        if (allRobotsProgrammed) Assert.True(damage > 0, "no laser damage compared");
    }

    // ---- H4b: King of the Hill's end-of-game commands are not about the bot's robot ----------

    [Fact]
    public void KingOfTheHillTurnLimit_AddsNoCommandsForTheBotsRobot()
    {
        // CreateTurn adds game-ending commands after Turn 9 (1-phase) or 2 (5-phase) in a King of
        // the Hill game. A one-phase prefix plan in a late turn takes that branch, so check the
        // robot's own commands are the same with or without it.
        var board = BotFixtures.Board();
        var players = BotFixtures.Players();
        var program = BotFixtures.RandomProgram(3).Take(1).ToArray();

        TurnPlan Plan(GameTypes type)
        {
            var cards = new CardList();
            BotFixtures.AddProgram(cards, 1, program);
            var request = BotFixtures.Request(board, players, cards, 1);
            request.GameType = type;
            request.Turn = 20;
            return new CreateCommands(request).CreateTurn();
        }

        var normal = Plan(default);
        var koth = Plan(GameTypes.KingOfTheHill);

        Assert.True(koth.Commands.Count > normal.Commands.Count, "the King of the Hill branch was not taken");
        Assert.Equal(
            string.Join("\n", normal.Commands.Where(c => c.RobotID == 1).Select(BotFixtures.CommandKey)),
            string.Join("\n", koth.Commands.Where(c => c.RobotID == 1).Select(BotFixtures.CommandKey)));
    }

    // ---- known side effects the bot must copy around (characterization) ----------------------

    [Fact]
    public void Characterization_PlannerOverwritesCardPriority_SoABotMustNotShareCards()
    {
        var board = BotFixtures.Board();
        var players = BotFixtures.Players(); // robot 3 has Priority 3

        var cards = new CardList();
        BotFixtures.AddProgram(cards, 3, BotFixtures.RandomProgram(1));
        foreach (var c in cards) c.Priority = 12345;

        var plan = new CreateCommands(BotFixtures.Request(board, players, cards, 5)).CreateTurn();
        Assert.True(plan.Planned, plan.Summary);

        Assert.All(cards, c => Assert.Equal(3, c.Priority)); // overwritten with the owner's priority
    }

    [Fact]
    public void Characterization_PlannerConsumesTheDrawPile_SoABotMustCopyIt()
    {
        var me = new PlayerState
        {
            ID = 1, PlayerStatus = tPlayerStatus.ReadyToRun, Priority = 1,
            CurrentPos = new RobotLocation(Direction.Down, 4, 4),
        };
        me.NextPos = new RobotLocation(me.CurrentPos);

        var cards = new CardList { new MoveCard(10, MoveCard.tCardType.Spam) { Owner = 1, PhasePlayed = 1 } };
        var request = BotFixtures.Request(new BoardElementCollection(12, 12), [me], cards, 1);
        request.DrawPiles[1] = [new MoveCard(50, MoveCard.tCardType.Forward1) { Owner = 1 }];

        var plan = new CreateCommands(request).CreateTurn();
        Assert.True(plan.Planned, plan.Summary);

        Assert.Empty(request.DrawPiles[1]);                                      // the pile was drawn from, in place
        Assert.Contains(plan.SpamConsumed, s => s.RobotID == 1 && s.CardID == 10); // the Spam was spent
        var end = BotFixtures.FinalPosition(plan, me);
        Assert.Equal((4, 5), (end.X, end.Y));                                    // and the drawn Forward1 ran instead
    }

    // ---- H5: the planner can run on several threads over a shared board ----------------------

    [Fact]
    public void Plan_IsSafeToRunInParallel_OverASharedBoardAndPlayers()
    {
        var board = BotFixtures.Board();
        var players = BotFixtures.Players();
        const int Runs = 60;

        var sequential = new string[Runs];
        for (int i = 0; i < Runs; i++)
            sequential[i] = BotFixtures.Signature(BotFixtures.PlanSolo(board, players, 1 + i % 6, BotFixtures.RandomProgram(i)));

        var parallel = new string[Runs];
        Parallel.For(0, Runs, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            parallel[i] = BotFixtures.Signature(BotFixtures.PlanSolo(board, players, 1 + i % 6, BotFixtures.RandomProgram(i))));

        for (int i = 0; i < Runs; i++)
            Assert.Equal(sequential[i], parallel[i]);
        Assert.Equal(BotFixtures.BoardSignature(BotFixtures.Board()), BotFixtures.BoardSignature(board));
    }
}
