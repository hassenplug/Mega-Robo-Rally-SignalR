namespace MRR.Tests;

/// <summary>
/// Spec test for the reboot-entry step of the Reboot mechanic (install/todo.md Section 1,
/// steps 5-6): the phase-1 pre-processing CreateCommands.ExecutePhase() runs for any robot
/// whose current square is a RebootToken -- DataService.Players.cs's
/// RespawnRobotAtRebootToken() is the only code that ever places a robot on one, always with
/// PositionValid=0, so a robot only ever starts a phase 1 sitting there on the one turn it
/// just rebooted.
///
/// UNVERIFIED AGAINST A LIVE GAME/PHYSICAL ROBOT (see install/todo.md Section 1) -- this only
/// confirms CreateCommands' planned commands are correct, not that the physical dispatch
/// sequencing (the "Place robot" prompt blocking before the entering robot's first move sends)
/// behaves correctly against real hardware.
/// </summary>
public class RebootEntryTests
{
    private const int EnteringId = 1;
    private const int OccupantId = 2;

    [Fact]
    public void RobotEntersOccupiedRebootToken_PushesOccupantAndPromptsForPlacement()
    {
        // A RebootToken at (2,2). The entering robot is already sitting there (as
        // RespawnRobotAtRebootToken would have left it, facing Right), and another robot is
        // still occupying the same square -- the collision this step must resolve.
        var board = new BoardElementCollection(5, 5);
        board.SetSquare(2, 2, SquareType.RebootToken, Direction.None, new BoardActionsCollection());

        var entering = new PlayerState
        {
            ID = EnteringId,
            Active = true,
            Priority = 1,
            CurrentPos = new RobotLocation(Direction.Right, 2, 2),
        };
        entering.NextPos = new RobotLocation(entering.CurrentPos);

        var occupant = new PlayerState
        {
            ID = OccupantId,
            Active = true,
            Priority = 2,
            CurrentPos = new RobotLocation(Direction.Up, 2, 2),
        };
        occupant.NextPos = new RobotLocation(occupant.CurrentPos);

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 1,
            GameState = 6, // CreateCommands.ExecuteTurn refuses to plan in any other state
            Board = board,
            Players = [entering, occupant],
            GameCards = new CardList(), // neither robot has a programmed card this test cares about
        };

        var plan = new CreateCommands(request).ExecuteTurn();

        Assert.True(plan.Planned, plan.Summary);

        // The occupant gets pushed one square in the entering robot's facing (Right):
        // (2,2) -> (3,2).
        var pushedMove = plan.Commands.Single(c =>
            c.RobotID == OccupantId && c.CommandType == SquareAction.PushedMove);
        Assert.Equal(3, pushedMove.EndPos.X);
        Assert.Equal(2, pushedMove.EndPos.Y);

        // The entering robot is prompted to physically place the real robot before anything
        // else happens to it this turn.
        Assert.Contains(plan.Commands, c =>
            c.RobotID == EnteringId && c.CommandType == SquareAction.SetButtonText &&
            c.text.StartsWith("Place ", StringComparison.Ordinal));
    }

    [Fact]
    public void RobotEntersUnoccupiedRebootToken_PromptsForPlacementWithNoPush()
    {
        var board = new BoardElementCollection(5, 5);
        board.SetSquare(2, 2, SquareType.RebootToken, Direction.None, new BoardActionsCollection());

        var entering = new PlayerState
        {
            ID = EnteringId,
            Active = true,
            Priority = 1,
            CurrentPos = new RobotLocation(Direction.Right, 2, 2),
        };
        entering.NextPos = new RobotLocation(entering.CurrentPos);

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 1,
            GameState = 6,
            Board = board,
            Players = [entering],
            GameCards = new CardList(),
        };

        var plan = new CreateCommands(request).ExecuteTurn();

        Assert.True(plan.Planned, plan.Summary);

        Assert.Contains(plan.Commands, c =>
            c.RobotID == EnteringId && c.CommandType == SquareAction.SetButtonText &&
            c.text.StartsWith("Place ", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Commands, c => c.CommandType == SquareAction.PushedMove);
    }
}
