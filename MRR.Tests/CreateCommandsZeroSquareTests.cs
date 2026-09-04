namespace MRR.Tests;

/// <summary>
/// End-to-end regression test for the CommandItem.EndPos bug: plans a real turn where a
/// robot moves onto square (0,0) and checks the planned Move command's EndPos -- the same
/// value CommandProcess/DataService.ProcessDbCommand writes into Robots.CurrentPosCol/
/// CurrentPosRow when the command completes. See RobotLocationTests and
/// CommandItemPositionTests for the underlying unit-level bug this drives through.
/// </summary>
public class CreateCommandsZeroSquareTests
{
    [Fact]
    public void MovingOntoSquareZeroZero_PlansTheRealDestination()
    {
        const int robotId = 1;

        var robot = new PlayerState
        {
            ID = robotId,
            Active = true,
            Priority = 1,
            CurrentPos = new RobotLocation(Direction.Left, 1, 0),
        };
        // GetPlayerStatesFromDB does this at load time; reproduce it here since we are
        // building the PlayerState by hand instead of loading it from the database.
        robot.NextPos = new RobotLocation(robot.CurrentPos);

        var card = new MoveCard(1, MoveCard.tCardType.Forward1)
        {
            Owner = robotId,
            PhasePlayed = 1,
        };

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 1,
            GameState = 6, // CreateCommands.ExecuteTurn refuses to plan in any other state
            Board = new BoardElementCollection(5, 5),
            Players = [robot],
            GameCards = [card],
        };

        var plan = new CreateCommands(request).ExecuteTurn();

        Assert.True(plan.Planned, plan.Summary);

        var moveCommand = plan.Commands.Single(c => c.RobotID == robotId && c.CommandType == SquareAction.Move);

        Assert.Equal(0, moveCommand.EndPos.X);
        Assert.Equal(0, moveCommand.EndPos.Y);
    }
}
