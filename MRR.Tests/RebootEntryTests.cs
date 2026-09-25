namespace MRR.Tests;

/// <summary>
/// Spec test for the reboot-entry step of the Reboot mechanic (install/todo.md Section 1,
/// step 4): the per-card loop in CreateCommands.CreatePhase() (CreateCommands.cs ~1071-1092)
/// checks any player with a played card this phase for `RespawnID > 0` -- the marker
/// DataService.Players.cs's RespawnRobotAtRebootToken() sets (and PlayerState.IsRunning
/// requires `RespawnID == 0`, so a robot with one pending never gets programmed normally
/// until this runs and clears it).
///
/// Rewritten 2026-09-23: the original version of this test (added 2026-09-18) drove the
/// scenario by placing a robot on a SquareType.RebootToken square with no RespawnID set --
/// that was exactly right for the phase-1-only block CreateCommands.cs had at the time, but a
/// 2026-09-20/21 rework replaced that block with the RespawnID-gated one this file now tests,
/// and left the old block commented out (since deleted) rather than updating this test to
/// match, so both cases here were failing against dead code. The reboot mechanic itself has
/// been confirmed working on the live table since (install/todo.md Section 1) -- this was a
/// stale test, not a real regression. Re-synced again same day after the placement message and
/// facing-direction source changed underneath this test a second time (now "Place: {Name} on
/// {RespawnID} facing {the robot's own CurrentPos.Direction}", not the respawn square's own
/// printed Rotation) -- see the exact string asserted below rather than trusting this comment
/// if it drifts again.
/// </summary>
public class RebootEntryTests
{
    private const int EnteringId = 1;
    private const int OccupantId = 2;
    private const int RespawnTokenId = 1; // matches SquareAction.Respawn's Parameter below

    [Fact]
    public void RobotWithPendingRespawn_PushesOccupantAndPromptsForPlacement()
    {
        // A Respawn square (letter "A" = Parameter 1) at (2,2), facing Right -- the direction a
        // push and the placement prompt should both use. The entering robot is already sitting
        // there (as RespawnRobotAtRebootToken would have left it) with RespawnID=1 still
        // pending, and another robot is still occupying the same square -- the collision this
        // step must resolve.
        var board = new BoardElementCollection(5, 5);
        var respawnActions = new BoardActionsCollection
        {
            new BoardAction { SquareAction = SquareAction.Respawn, Parameter = RespawnTokenId },
        };
        board.SetSquare(2, 2, SquareType.RebootToken, Direction.Right, respawnActions);

        // PowerUp doesn't move the robot -- keeps this test's asserts about the push/placement
        // isolated from the entering robot's own (separately-tested-elsewhere) movement once
        // RespawnID clears and it rejoins normal phase processing later in this same loop.
        var entering = new PlayerState
        {
            ID = EnteringId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 1,
            RespawnID = RespawnTokenId,
            CurrentPos = new RobotLocation(Direction.Right, 2, 2),
        };
        entering.NextPos = new RobotLocation(entering.CurrentPos);

        var occupant = new PlayerState
        {
            ID = OccupantId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 2,
            CurrentPos = new RobotLocation(Direction.Up, 2, 2),
        };
        occupant.NextPos = new RobotLocation(occupant.CurrentPos);

        var cards = new CardList
        {
            new MoveCard(1, MoveCard.tCardType.PowerUp) { Owner = EnteringId, PhasePlayed = 1 },
        };

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 1,
            GameState = 6, // CreateCommands.CreateTurn refuses to plan in any other state
            Board = board,
            Players = [entering, occupant],
            GameCards = cards,
        };

        var plan = new CreateCommands(request).CreateTurn();

        Assert.True(plan.Planned, plan.Summary);

        // The occupant gets pushed one square in the respawn square's own facing (Right):
        // (2,2) -> (3,2).
        var pushedMove = plan.Commands.Single(c =>
            c.RobotID == OccupantId && c.CommandType == SquareAction.PushedMove);
        Assert.Equal(3, pushedMove.EndPos.X);
        Assert.Equal(2, pushedMove.EndPos.Y);

        // The entering robot is prompted to physically place the real robot, with the facing
        // direction actually filled in (this used to be a hardcoded, unfinished "facing..."
        // string -- see install/todo.md Section 1). The direction shown is the robot's own
        // CurrentPos.Direction (what the player picked via the direction picker), not the
        // respawn square's printed Rotation -- both happen to be Right in this test.
        Assert.Contains(plan.Commands, c =>
            c.RobotID == EnteringId && c.CommandType == SquareAction.SetButtonText &&
            c.text == $"Place: {entering.Name} on {RespawnTokenId} facing {Direction.Right}");

        // RespawnID is cleared in the DB too (not just in-memory), via a persisted command --
        // otherwise a reload before the next confirm would see it still pending.
        Assert.Contains(plan.Commands, c =>
            c.RobotID == EnteringId && c.CommandType == SquareAction.Respawn && c.Value == 0);
    }

    [Fact]
    public void RobotWithPendingRespawn_NoOccupant_PromptsForPlacementWithNoPush()
    {
        var board = new BoardElementCollection(5, 5);
        var respawnActions = new BoardActionsCollection
        {
            new BoardAction { SquareAction = SquareAction.Respawn, Parameter = RespawnTokenId },
        };
        board.SetSquare(2, 2, SquareType.RebootToken, Direction.Right, respawnActions);

        var entering = new PlayerState
        {
            ID = EnteringId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 1,
            RespawnID = RespawnTokenId,
            CurrentPos = new RobotLocation(Direction.Right, 2, 2),
        };
        entering.NextPos = new RobotLocation(entering.CurrentPos);

        var cards = new CardList
        {
            new MoveCard(1, MoveCard.tCardType.PowerUp) { Owner = EnteringId, PhasePlayed = 1 },
        };

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 1,
            GameState = 6,
            Board = board,
            Players = [entering],
            GameCards = cards,
        };

        var plan = new CreateCommands(request).CreateTurn();

        Assert.True(plan.Planned, plan.Summary);

        Assert.Contains(plan.Commands, c =>
            c.RobotID == EnteringId && c.CommandType == SquareAction.SetButtonText &&
            c.text == $"Place: {entering.Name} on {RespawnTokenId} facing {Direction.Right}");
        Assert.DoesNotContain(plan.Commands, c => c.CommandType == SquareAction.PushedMove);
    }
}
