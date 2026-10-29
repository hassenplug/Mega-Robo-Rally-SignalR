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
