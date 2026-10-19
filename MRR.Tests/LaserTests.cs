namespace MRR.Tests;

/// <summary>
/// Robot lasers (CreateCommands.CreatePhase's damage step): a robot fires along its facing and
/// hits the first robot in line, wherever on the board it stands -- including the outer rows and
/// columns -- and a shut-down robot doesn't fire.
/// </summary>
public class LaserTests
{
    private const int ShooterId = 1;
    private const int TargetId = 2;

    /// <summary>Plans a one-phase turn on an empty 12x12 board; returns the shooter's laser hits.</summary>
    private static int ShooterHits(int shooterX, int shooterY, Direction facing, int targetX, int targetY,
        tPlayerStatus shooterStatus = tPlayerStatus.ReadyToRun)
    {
        var shooter = new PlayerState
        {
            ID = ShooterId,
            PlayerStatus = shooterStatus,
            Priority = 1,
            CurrentPos = new RobotLocation(facing, shooterX, shooterY),
        };
        shooter.NextPos = new RobotLocation(shooter.CurrentPos);

        var target = new PlayerState
        {
            ID = TargetId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 2,
            CurrentPos = new RobotLocation(Direction.Up, targetX, targetY),
        };
        target.NextPos = new RobotLocation(target.CurrentPos);

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 1,
            GameState = 6,
            LaserDamage = 1,
            Board = new BoardElementCollection(12, 12),
            Players = [shooter, target],
            GameCards = new CardList(),
        };

        var plan = new CreateCommands(request).CreateTurn();
        Assert.True(plan.Planned, plan.Summary);

        return plan.Commands.Count(c =>
            c.CommandType == SquareAction.FireCannon && c.RobotID == ShooterId && c.Value == TargetId);
    }

    [Theory]
    [InlineData(5, 5, Direction.Right, 8, 5)]   // middle of the board
    [InlineData(3, 5, Direction.Right, 11, 5)]  // target on the far edge
    [InlineData(0, 5, Direction.Right, 3, 5)]   // shooter on the left edge
    [InlineData(11, 5, Direction.Left, 8, 5)]   // shooter on the right edge
    [InlineData(5, 0, Direction.Down, 5, 3)]    // shooter on the top edge
    [InlineData(5, 11, Direction.Up, 5, 8)]     // shooter on the bottom edge
    [InlineData(0, 0, Direction.Right, 11, 0)]  // corner to corner along the edge
    public void Laser_HitsRobotInLine(int sx, int sy, Direction facing, int tx, int ty)
    {
        Assert.Equal(1, ShooterHits(sx, sy, facing, tx, ty));
    }

    [Fact]
    public void Laser_FacingOffTheBoard_HitsNothing()
    {
        // Shooter on the left edge facing out; the target is behind it.
        Assert.Equal(0, ShooterHits(0, 5, Direction.Left, 3, 5));
    }

    [Fact]
    public void ShutDownRobot_DoesNotFire()
    {
        Assert.Equal(0, ShooterHits(5, 5, Direction.Right, 8, 5, tPlayerStatus.ShutDown));
    }
}
