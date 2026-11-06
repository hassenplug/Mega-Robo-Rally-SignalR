namespace MRR.Tests;

/// <summary>
/// A Spam register runs the next card drawn instead, chaining past further Spam. The planner
/// reports that card in TurnPlan.SpamReplacements so Master can save what actually ran
/// (HistoryRobotTurns.ExecutedCards, index.html's "Last Hand").
/// </summary>
public class SpamReplacementTests
{
    private const int RobotId = 1;

    [Fact]
    public void SpamRegister_ReportsTheFirstNonSpamCardDrawn()
    {
        var robot = new PlayerState
        {
            ID = RobotId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 1,
            CurrentPos = new RobotLocation(Direction.Right, 2, 2),
        };
        robot.NextPos = new RobotLocation(robot.CurrentPos);

        var hand = new CardList();
        hand.AddRange(new[]
        {
            new MoveCard(1, MoveCard.tCardType.Spam)     { Owner = RobotId, PhasePlayed = 1 },
            new MoveCard(2, MoveCard.tCardType.Forward1) { Owner = RobotId, PhasePlayed = 2 },
        });

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 5,
            GameState = 6,
            Board = new BoardElementCollection(10, 10),
            Players = [robot],
            GameCards = hand,
            DrawPiles =
            {
                [RobotId] =
                [
                    new MoveCard(10, MoveCard.tCardType.Spam)     { Owner = RobotId },
                    new MoveCard(11, MoveCard.tCardType.Forward2) { Owner = RobotId },
                ],
            },
        };

        var plan = new CreateCommands(request).CreateTurn();

        Assert.True(plan.Planned, plan.Summary);
        var replacement = Assert.Single(plan.SpamReplacements);
        Assert.Equal(new SpamReplacement(RobotId, 1, (int)MoveCard.tCardType.Forward2), replacement);
        Assert.Equal(2, plan.SpamConsumed.Count); // the programmed Spam and the drawn one
    }

    [Fact]
    public void AgainInRegisterOne_CountsAsSpamButOnlyRealCardsAreCommandedOrRetired()
    {
        var plan = PlanWithFirstRegister(MoveCard.tCardType.Again, drawPile: MoveCard.tCardType.Forward2);

        Assert.True(plan.Planned, plan.Summary);
        Assert.Equal(new SpamReplacement(RobotId, 1, (int)MoveCard.tCardType.Forward2), Assert.Single(plan.SpamReplacements));
        // The Again card is discarded as usual, not retired like Spam.
        Assert.Empty(plan.SpamConsumed);
        // Card commands in phase 1: the Again card and the drawn Forward 2 -- not the stand-in.
        var cardIds = plan.Commands.Where(c => c.Phase == 1 && c.CommandType == SquareAction.Card).Select(c => c.Value).ToList();
        Assert.Equal(new[] { 1, 11 }, cardIds);
    }

    [Fact]
    public void AgainAfterSpam_RepeatsTheCardDrawnForTheSpam()
    {
        // Turn 12, robot 6: Forward 1, Spam (drew Forward 2), Again -- the Again must move 2.
        var plan = PlanRegisters(
            [MoveCard.tCardType.Forward1, MoveCard.tCardType.Spam, MoveCard.tCardType.Again],
            drawPile: [MoveCard.tCardType.Forward2]);

        Assert.True(plan.Planned, plan.Summary);
        Assert.Contains(new SpamReplacement(RobotId, 2, (int)MoveCard.tCardType.Forward2), plan.SpamReplacements);
        Assert.Contains(new SpamReplacement(RobotId, 3, (int)MoveCard.tCardType.Forward2), plan.SpamReplacements);
        Assert.Equal(2, MovesInPhase(plan, 3));
        // The Again card itself (ID 3) is the card commanded in phase 3, not the Spam (ID 2).
        Assert.Equal(new[] { 3 }, CardIdsInPhase(plan, 3));
    }

    [Fact]
    public void AgainAfterAgain_RepeatsTheSameCardAgain()
    {
        var plan = PlanRegisters(
            [MoveCard.tCardType.Spam, MoveCard.tCardType.Again, MoveCard.tCardType.Again],
            drawPile: [MoveCard.tCardType.Forward1]);

        Assert.True(plan.Planned, plan.Summary);
        Assert.Contains(new SpamReplacement(RobotId, 3, (int)MoveCard.tCardType.Forward1), plan.SpamReplacements);
        Assert.Equal(1, MovesInPhase(plan, 2));
        Assert.Equal(1, MovesInPhase(plan, 3));
    }

    [Fact]
    public void AgainAfterNormalCard_RepeatsIt_AndIsReportedForHistory()
    {
        var plan = PlanRegisters(
            [MoveCard.tCardType.Forward1, MoveCard.tCardType.Again],
            drawPile: []);

        Assert.True(plan.Planned, plan.Summary);
        Assert.Equal(new SpamReplacement(RobotId, 2, (int)MoveCard.tCardType.Forward1), Assert.Single(plan.SpamReplacements));
        Assert.Equal(1, MovesInPhase(plan, 2));
        Assert.Equal(new[] { 2 }, CardIdsInPhase(plan, 2));
    }

    [Fact]
    public void SpamThatDrawsAgain_RepeatsThePreviousRegister()
    {
        // Turn 20, robot 4: Back 1, Forward 3, Left, Forward 1, Spam (drew Again) -- the
        // drawn Again must repeat the Forward 1, not do nothing.
        var plan = PlanRegisters(
            [MoveCard.tCardType.Back1, MoveCard.tCardType.Forward3, MoveCard.tCardType.LTurn,
             MoveCard.tCardType.Forward1, MoveCard.tCardType.Spam],
            drawPile: [MoveCard.tCardType.Again]);

        Assert.True(plan.Planned, plan.Summary);
        Assert.Contains(new SpamReplacement(RobotId, 5, (int)MoveCard.tCardType.Forward1), plan.SpamReplacements);
        Assert.Equal(1, MovesInPhase(plan, 5));
        // The Spam (ID 5) is retired; the drawn Again (ID 11) is commanded but not retired.
        Assert.Equal(new[] { 5 }, plan.SpamConsumed.Select(s => s.CardID));
        Assert.Equal(new[] { 5, 11 }, CardIdsInPhase(plan, 5));
    }

    [Fact]
    public void SpamInRegisterOne_ThatDrawsAgain_KeepsDrawing()
    {
        var plan = PlanRegisters(
            [MoveCard.tCardType.Spam],
            drawPile: [MoveCard.tCardType.Again, MoveCard.tCardType.Forward2]);

        Assert.True(plan.Planned, plan.Summary);
        Assert.Equal(new SpamReplacement(RobotId, 1, (int)MoveCard.tCardType.Forward2), Assert.Single(plan.SpamReplacements));
        Assert.Equal(2, MovesInPhase(plan, 1));
        Assert.Equal(new[] { 1 }, plan.SpamConsumed.Select(s => s.CardID));
        Assert.Equal(new[] { 1, 11, 12 }, CardIdsInPhase(plan, 1));
    }

    private static int MovesInPhase(TurnPlan plan, int phase) =>
        plan.Commands.Count(c => c.Phase == phase && c.RobotID == RobotId && c.CommandType == SquareAction.Move);

    private static List<int> CardIdsInPhase(TurnPlan plan, int phase) =>
        plan.Commands.Where(c => c.Phase == phase && c.RobotID == RobotId && c.CommandType == SquareAction.Card)
                     .Select(c => c.Value).ToList();

    /// <summary>Registers get card IDs 1, 2, 3...; draw-pile cards get 11, 12...</summary>
    private static TurnPlan PlanRegisters(MoveCard.tCardType[] registers, MoveCard.tCardType[] drawPile)
    {
        var robot = new PlayerState
        {
            ID = RobotId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 1,
            CurrentPos = new RobotLocation(Direction.Right, 1, 1),
        };
        robot.NextPos = new RobotLocation(robot.CurrentPos);

        var hand = new CardList();
        for (int i = 0; i < registers.Length; i++)
            hand.Add(new MoveCard(i + 1, registers[i]) { Owner = RobotId, PhasePlayed = i + 1 });

        return new CreateCommands(new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 5,
            GameState = 6,
            Board = new BoardElementCollection(10, 10),
            Players = [robot],
            GameCards = hand,
            DrawPiles = { [RobotId] = drawPile.Select((t, i) => new MoveCard(11 + i, t) { Owner = RobotId }).ToList() },
        }).CreateTurn();
    }

    private static TurnPlan PlanWithFirstRegister(MoveCard.tCardType first, MoveCard.tCardType drawPile)
    {
        var robot = new PlayerState
        {
            ID = RobotId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 1,
            CurrentPos = new RobotLocation(Direction.Right, 2, 2),
        };
        robot.NextPos = new RobotLocation(robot.CurrentPos);

        var hand = new CardList();
        hand.Add(new MoveCard(1, first) { Owner = RobotId, PhasePlayed = 1 });

        return new CreateCommands(new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 5,
            GameState = 6,
            Board = new BoardElementCollection(10, 10),
            Players = [robot],
            GameCards = hand,
            DrawPiles = { [RobotId] = [new MoveCard(11, drawPile) { Owner = RobotId }] },
        }).CreateTurn();
    }
}
