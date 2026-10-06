namespace MRR.Tests;

/// <summary>
/// Spec test for the shutdown mechanic's turn-planning half (documents/SHUTDOWN_DESIGN.md
/// section 3): a shut-down robot plays no registers and fires no laser, but is still physically
/// on the board, so another robot driving into it pushes it. The lock-in card moves and the
/// toggle live in DataService (MySQL) and are checked manually on the table.
/// </summary>
public class ShutDownTests
{
    private const int MoverId = 1;
    private const int ShutDownId = 2;

    [Fact]
    public void ShutDownRobot_IsOnBoardButNotRunning()
    {
        var robot = new PlayerState { ID = ShutDownId, PlayerStatus = tPlayerStatus.ShutDown };

        Assert.True(robot.IsOnBoard);
        Assert.False(robot.IsRunning);
        Assert.False(new PlayerState { PlayerStatus = tPlayerStatus.Dead }.IsOnBoard);
    }

    [Fact]
    public void ShutDownRobot_CanBePushed()
    {
        // 5x5 empty board. Mover at (1,2) facing Right drives Forward1 into the shut-down
        // robot at (2,2), which has no cards and must be pushed on to (3,2).
        var board = new BoardElementCollection(5, 5);

        var mover = new PlayerState
        {
            ID = MoverId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 1,
            CurrentPos = new RobotLocation(Direction.Right, 1, 2),
        };
        mover.NextPos = new RobotLocation(mover.CurrentPos);

        var shutDown = new PlayerState
        {
            ID = ShutDownId,
            PlayerStatus = tPlayerStatus.ShutDown,
            ShutDown = tShutDown.Currently,
            Priority = 2,
            CurrentPos = new RobotLocation(Direction.Up, 2, 2),
        };
        shutDown.NextPos = new RobotLocation(shutDown.CurrentPos);

        var cards = new CardList
        {
            new MoveCard(1, MoveCard.tCardType.Forward1) { Owner = MoverId, PhasePlayed = 1 },
        };

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 1,
            GameState = 6,
            Board = board,
            Players = [mover, shutDown],
            GameCards = cards,
        };

        var plan = new CreateCommands(request).CreateTurn();

        Assert.True(plan.Planned, plan.Summary);

        var push = plan.Commands.Single(c =>
            c.RobotID == ShutDownId && c.Phase == 1 && c.CommandType == SquareAction.PushedMove);
        Assert.Equal(3, push.EndPos.X);
        Assert.Equal(2, push.EndPos.Y);

        // It still plays nothing of its own.
        Assert.DoesNotContain(plan.Commands, c =>
            c.RobotID == ShutDownId && c.CommandType == SquareAction.Move);
    }
}
