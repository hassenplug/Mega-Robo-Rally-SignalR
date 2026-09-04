namespace MRR.Tests;

/// <summary>
/// Full-turn scenario: one robot on the table, dealt a 9-card hand, 5 of them programmed
/// into registers (phases), then a turn calculated end to end through
/// CreateCommands.ExecuteTurn(). Confirms the destination recorded for every phase -- not
/// just the final square -- matches hand-computed positions.
///
/// MRR.Rules/MRR.Contracts have no database access by design (API_DECOMPOSITION_DESIGN.md
/// section 3.2), so "start a new game" / "reset the robot table to one robot" is modeled by
/// building the single-robot TurnRequest directly, the same shape DataService.
/// BuildTurnRequest() would hand the planner after DataService.ResetPlayers() and
/// MoveCardsShuffleAndDeal() ran against a table holding one robot.
/// </summary>
public class SingleRobotTurnTests
{
    private const int RobotId = 1;

    [Fact]
    public void CalculatedTurn_RecordsTheCorrectDestinationForEveryPhase()
    {
        // Arrange: one robot on the table, starting at (2,2) facing Right.
        var robot = new PlayerState
        {
            ID = RobotId,
            Active = true,
            Priority = 1,
            CurrentPos = new RobotLocation(Direction.Right, 2, 2),
        };
        robot.NextPos = new RobotLocation(robot.CurrentPos);

        // Deal a 9-card hand; only 5 are programmed into registers (PhasePlayed 1-5), the
        // other 4 stay in hand (PhasePlayed -1, MoveCard's default -- "unplayed").
        var hand = new CardList();
        var played = new[]
        {
            new MoveCard(1, MoveCard.tCardType.Forward1) { Owner = RobotId, PhasePlayed = 1 }, // (2,2) -> (3,2)
            new MoveCard(2, MoveCard.tCardType.RTurn)    { Owner = RobotId, PhasePlayed = 2 }, // Right -> Down
            new MoveCard(3, MoveCard.tCardType.Forward1) { Owner = RobotId, PhasePlayed = 3 }, // (3,2) -> (3,3)
            new MoveCard(4, MoveCard.tCardType.LTurn)    { Owner = RobotId, PhasePlayed = 4 }, // Down -> Right
            new MoveCard(5, MoveCard.tCardType.Forward1) { Owner = RobotId, PhasePlayed = 5 }, // (3,3) -> (4,3)
        };
        hand.AddRange(played);
        hand.AddRange(new[]
        {
            new MoveCard(6, MoveCard.tCardType.Forward2) { Owner = RobotId },
            new MoveCard(7, MoveCard.tCardType.UTurn)    { Owner = RobotId },
            new MoveCard(8, MoveCard.tCardType.Back1)    { Owner = RobotId },
            new MoveCard(9, MoveCard.tCardType.PowerUp)  { Owner = RobotId },
        });

        Assert.Equal(9, hand.Count(c => c.Owner == RobotId));
        Assert.Equal(5, hand.Count(c => c.Owner == RobotId && c.PhasePlayed > 0));

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 5,
            GameState = 6, // CreateCommands.ExecuteTurn refuses to plan in any other state
            Board = new BoardElementCollection(10, 10),
            Players = [robot],
            GameCards = hand,
        };

        // Act
        var plan = new CreateCommands(request).ExecuteTurn();

        // Assert
        Assert.True(plan.Planned, plan.Summary);

        AssertMove(plan, phase: 1, expectedX: 3, expectedY: 2, expectedDir: Direction.Right);
        AssertRotate(plan, phase: 2, expectedX: 3, expectedY: 2, expectedDir: Direction.Down);
        AssertMove(plan, phase: 3, expectedX: 3, expectedY: 3, expectedDir: Direction.Down);
        AssertRotate(plan, phase: 4, expectedX: 3, expectedY: 3, expectedDir: Direction.Right);
        AssertMove(plan, phase: 5, expectedX: 4, expectedY: 3, expectedDir: Direction.Right);
    }

    private static void AssertMove(TurnPlan plan, int phase, int expectedX, int expectedY, Direction expectedDir)
        => AssertDestination(plan, phase, SquareAction.Move, expectedX, expectedY, expectedDir);

    private static void AssertRotate(TurnPlan plan, int phase, int expectedX, int expectedY, Direction expectedDir)
        => AssertDestination(plan, phase, SquareAction.Rotate, expectedX, expectedY, expectedDir);

    private static void AssertDestination(
        TurnPlan plan, int phase, SquareAction commandType, int expectedX, int expectedY, Direction expectedDir)
    {
        var command = plan.Commands.Single(c =>
            c.RobotID == RobotId && c.Phase == phase && c.CommandType == commandType);

        // This is exactly the value CommandProcess/DataService.ProcessDbCommand writes into
        // Robots.CurrentPosCol/CurrentPosRow/CurrentPosDir once the command completes.
        Assert.Equal(expectedX, command.EndPos.X);
        Assert.Equal(expectedY, command.EndPos.Y);
        Assert.Equal(expectedDir, command.EndPos.Direction);
    }
}
