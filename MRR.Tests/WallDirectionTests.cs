namespace MRR.Tests;

/// <summary>
/// Walls (SquareAction.BlockDirection): Parameter &gt; 0 is the blocked direction; Parameter 0
/// takes it from the square's Rotation, matching the wall image (bar on the top edge at
/// Rotation Up). See BoardElement.WallDirection.
/// </summary>
public class WallDirectionTests
{
    private const int RobotId = 1;
    private const int TargetId = 2;

    private static BoardElementCollection BoardWithWall(int x, int y, Direction rotation, int parameter)
    {
        var board = new BoardElementCollection(10, 10);
        board.SetSquare(x, y, SquareType.Walls, rotation,
            [new BoardAction(SquareAction.BlockDirection, parameter, 0, 31)]);
        return board;
    }

    /// <summary>Robot at (2,2) facing Up plays Forward1 on <paramref name="board"/>; true if a wall stopped it.</summary>
    private static bool ForwardIsBlocked(BoardElementCollection board)
    {
        var robot = new PlayerState
        {
            ID = RobotId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 1,
            CurrentPos = new RobotLocation(Direction.Up, 2, 2),
        };
        robot.NextPos = new RobotLocation(robot.CurrentPos);

        var hand = new CardList();
        hand.Add(new MoveCard(1, MoveCard.tCardType.Forward1) { Owner = RobotId, PhasePlayed = 1 });

        var plan = new CreateCommands(new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 1,
            GameState = 6,
            Board = board,
            Players = [robot],
            GameCards = hand,
        }).CreateTurn();
        Assert.True(plan.Planned, plan.Summary);

        return plan.Commands.Any(c => c.RobotID == RobotId && c.CommandType == SquareAction.BlockDirection);
    }

    [Theory]
    [InlineData(Direction.Up, true)]     // bar on the top edge of (2,2): blocks leaving upward
    [InlineData(Direction.Down, false)]  // bottom edge
    [InlineData(Direction.Right, false)]
    [InlineData(Direction.Left, false)]
    public void ParameterZero_WallOnOwnSquare_UsesRotation(Direction rotation, bool blocked)
    {
        Assert.Equal(blocked, ForwardIsBlocked(BoardWithWall(2, 2, rotation, 0)));
    }

    [Theory]
    [InlineData(Direction.Down, true)]   // bar on the bottom edge of (2,1): blocks entering from below
    [InlineData(Direction.Up, false)]
    [InlineData(Direction.Right, false)]
    [InlineData(Direction.Left, false)]
    public void ParameterZero_WallOnTargetSquare_UsesRotation(Direction rotation, bool blocked)
    {
        Assert.Equal(blocked, ForwardIsBlocked(BoardWithWall(2, 1, rotation, 0)));
    }

    [Theory]
    [InlineData(Direction.Up, (int)Direction.Down, true)]   // Parameter Down = top edge, whatever the rotation
    [InlineData(Direction.Left, (int)Direction.Down, true)]
    [InlineData(Direction.Up, (int)Direction.Up, false)]    // rotation alone would block; the Parameter wins
    public void ParameterSet_OverridesRotation(Direction rotation, int parameter, bool blocked)
    {
        Assert.Equal(blocked, ForwardIsBlocked(BoardWithWall(2, 2, rotation, parameter)));
    }

    [Fact]
    public void ParameterZero_RotationNone_BlocksNothing()
    {
        Assert.False(ForwardIsBlocked(BoardWithWall(2, 2, Direction.None, 0)));
    }

    [Theory]
    [InlineData(Direction.Up, true)]     // bar on the top edge of (2,4): beam stopped entering
    [InlineData(Direction.Down, true)]   // bottom edge: beam stopped leaving
    [InlineData(Direction.Left, false)]  // side edge: beam passes
    public void ParameterZero_WallStopsLaser(Direction rotation, bool blocked)
    {
        // Shooter at (2,2) facing Down, target at (2,6); wall square at (2,4).
        var shooter = new PlayerState
        {
            ID = RobotId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 1,
            CurrentPos = new RobotLocation(Direction.Down, 2, 2),
        };
        shooter.NextPos = new RobotLocation(shooter.CurrentPos);
        var target = new PlayerState
        {
            ID = TargetId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 2,
            CurrentPos = new RobotLocation(Direction.Up, 2, 6),
        };
        target.NextPos = new RobotLocation(target.CurrentPos);

        var plan = new CreateCommands(new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 1,
            GameState = 6,
            LaserDamage = 1,
            Board = BoardWithWall(2, 4, rotation, 0),
            Players = [shooter, target],
            GameCards = new CardList(),
        }).CreateTurn();
        Assert.True(plan.Planned, plan.Summary);

        bool hit = plan.Commands.Any(c =>
            c.CommandType == SquareAction.FireCannon && c.RobotID == RobotId && c.Value == TargetId);
        Assert.Equal(!blocked, hit);
    }
}
