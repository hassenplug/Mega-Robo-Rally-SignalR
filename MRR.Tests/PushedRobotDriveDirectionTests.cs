namespace MRR.Tests;

/// <summary>
/// Regression test for a "robot drives the wrong physical direction" bug in TurnRobot
/// (CreateCommands.cs). CommandItem.ValueB is the drive angle sent to the real robot,
/// relative to its own current heading (1=straight ahead, 3=straight reverse, 2/4=90 skew --
/// see RotationFunctions.Degrees). For a robot's own move, MoveRobot sets ValueB from its own
/// distance sign, which is always consistent with its own facing. But when a robot is pushed
/// (SquareAction.PushedMove), MoveRobot sets ValueB from the *pusher's* distance sign -- e.g.
/// backing into another robot builds the pushed robot's move as ValueB=3 (reverse), with no
/// relation to which way the pushed robot itself is facing.
///
/// TurnRobot is supposed to reconcile ValueB against the pushed robot's actual facing before
/// the command is sent to hardware. It did so correctly whenever a rotation was needed
/// (RotationDifference != 0), but left ValueB untouched when the pushed robot's facing already
/// matched the absolute travel direction (RotationDifference == 0) -- so a robot already
/// facing the direction it gets shoved kept the stale ValueB=3 (reverse) and would physically
/// drive backward instead of forward.
/// </summary>
public class PushedRobotDriveDirectionTests
{
    private const int PusherId = 1;
    private const int PushedId = 2;

    [Fact]
    public void RobotPushedInTheDirectionItAlreadyFaces_DrivesForwardNotReverse()
    {
        // Pusher backs up (Back1) from (2,2) facing Right -- travels Left, into (1,2).
        var pusher = new PlayerState
        {
            ID = PusherId,
            Active = true,
            Priority = 1,
            CurrentPos = new RobotLocation(Direction.Right, 2, 2),
        };
        pusher.NextPos = new RobotLocation(pusher.CurrentPos);

        // Pushed robot sits at (1,2), already facing Left -- the same direction it is about
        // to be shoved (toward (0,2)). No rotation is needed for its own facing to line up
        // with the push's travel direction.
        var pushed = new PlayerState
        {
            ID = PushedId,
            Active = true,
            Priority = 2,
            CurrentPos = new RobotLocation(Direction.Left, 1, 2),
        };
        pushed.NextPos = new RobotLocation(pushed.CurrentPos);

        var card = new MoveCard(1, MoveCard.tCardType.Back1) { Owner = PusherId, PhasePlayed = 1 };

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 1,
            GameState = 6, // CreateCommands.ExecuteTurn refuses to plan in any other state
            Board = new BoardElementCollection(5, 5),
            Players = [pusher, pushed],
            GameCards = [card],
        };

        var plan = new CreateCommands(request).ExecuteTurn();

        Assert.True(plan.Planned, plan.Summary);

        var pushedMove = plan.Commands.Single(c => c.RobotID == PushedId && c.CommandType == SquareAction.PushedMove);

        // Sanity: the pushed robot really did end up one square further in the direction it
        // already faced.
        Assert.Equal(0, pushedMove.EndPos.X);
        Assert.Equal(2, pushedMove.EndPos.Y);

        // The bug: ValueB was left at 3 (reverse) even though the pushed robot's facing
        // matches the travel direction, so it should drive straight ahead (1).
        Assert.Equal(1, pushedMove.ValueB);
    }
}
