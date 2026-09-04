namespace MRR.Tests;

/// <summary>
/// CommandItem's constructor used to treat "NextPos.X == 0 &amp;&amp; NextPos.Y == 0" as meaning
/// "no move planned yet, fall back to CurrentPos" -- but (0,0) is also a real board square,
/// so any command whose robot was genuinely moving to/rotating on square (0,0) got EndPos
/// set to the robot's OLD position instead. CommandProcess/DataService.ProcessDbCommand
/// writes EndPos straight into Robots.CurrentPosCol/CurrentPosRow when the command
/// completes, so this was the direct cause of the wrong X/Y values landing in the database.
/// </summary>
public class CommandItemPositionTests
{
    [Fact]
    public void EndPos_UsesNextPos_EvenWhenDestinationIsSquareZeroZero()
    {
        var robot = new PlayerState
        {
            ID = 1,
            CurrentPos = new RobotLocation(Direction.Left, 5, 3),
            NextPos = new RobotLocation(Direction.Left, 0, 0),
        };

        var command = new CommandItem(1, 10, robot, 1, 1, Direction.Left, SquareAction.Move);

        Assert.Equal(0, command.EndPos.X);
        Assert.Equal(0, command.EndPos.Y);
    }

    [Fact]
    public void EndPos_UsesCurrentPosition_WhenNoMoveIsInFlight()
    {
        // Mirrors what DataService.GetPlayerStatesFromDB now does: NextPos starts out equal
        // to CurrentPos, not the RobotLocation() default, so "no move planned yet" is
        // represented truthfully instead of via a magic-value guess.
        var robot = new PlayerState
        {
            ID = 1,
            CurrentPos = new RobotLocation(Direction.Up, 2, 2),
        };
        robot.NextPos = new RobotLocation(robot.CurrentPos);

        var command = new CommandItem(1, 10, robot, 0, 0, Direction.None, SquareAction.StartBotMove);

        Assert.Equal(2, command.EndPos.X);
        Assert.Equal(2, command.EndPos.Y);
    }
}
