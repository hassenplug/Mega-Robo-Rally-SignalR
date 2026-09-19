namespace MRR.Tests;

/// <summary>
/// Spec test for the not-yet-implemented Pit/reboot mechanic (install/todo.md Section 1,
/// "Reboot mechanic" -- pit/off-board entry). CreateCommands.cs has no code today that reacts
/// to SquareType.Pit at all, so this is written against the requirement, not the current
/// behavior, and is expected to fail until that logic exists -- see the Skip reason on the
/// Fact below. Delete the Skip (and adjust the asserts if the real implementation differs in
/// a way that's still faithful to the requirement) once the mechanic lands.
///
/// This covers only the slice of the requirement CreateCommands' pure turn-planning layer can
/// exercise in isolation: a robot dies the instant it lands on a Pit square and stops
/// receiving further commands for the rest of that same turn. The remaining steps in the
/// requirement -- waiting for the "remove robot from board" tap, placing the robot at its
/// respawn square, picking a facing direction next turn, and the reboot-square push-on-entry
/// behavior -- span GameController/DataService and the phone UI across two turns, so they
/// aren't testable at this level; they belong in integration coverage once the mechanic exists.
///
/// Modeled on the existing damage-death path CreateCommands.AddDamage() already implements
/// (CreateCommands.cs ~1778-1857): reaching fatal damage adds a SquareAction.SetPlayerStatus
/// command with tPlayerStatus.Dead (11), then a SquareAction.SetButtonText "Remove Robot: "
/// command (a blocking User Input prompt -- CommandCategories.UserInput, CommandList.cs:418)
/// telling the human to physically take the robot off the table. A Pit death should reuse (or
/// exactly mirror) that same pair of commands rather than inventing a new signal, and the
/// existing `workingPlayers.Where(ap => ap.Active && ...)` gate most phase-processing loops
/// already use (e.g. CreateCommands.cs:930) should then skip the now-dead robot for any
/// remaining phase of the turn -- so this only needs a Damage-threshold-style kill triggered by
/// landing on SquareType.Pit, not a new suppression mechanism.
/// </summary>
public class PitRebootTests
{
    private const int RobotId = 1;

    [Fact]
    public void RobotMovesOntoPitSquare_DiesImmediatelyAndStopsReceivingCommandsThisTurn()
    {
        // Arrange: 5x5 board with a Pit at (2,2). Robot starts at (1,2) facing Right, one
        // square short of the pit, with two Forward1 cards programmed back to back.
        var board = new BoardElementCollection(5, 5);
        board.SetSquare(2, 2, SquareType.Pit, Direction.None, new BoardActionsCollection());

        var robot = new PlayerState
        {
            ID = RobotId,
            Active = true,
            Priority = 1,
            CurrentPos = new RobotLocation(Direction.Right, 1, 2),
        };
        robot.NextPos = new RobotLocation(robot.CurrentPos);

        var cards = new CardList
        {
            new MoveCard(1, MoveCard.tCardType.Forward1) { Owner = RobotId, PhasePlayed = 1 }, // (1,2) -> (2,2): the pit
            new MoveCard(2, MoveCard.tCardType.Forward1) { Owner = RobotId, PhasePlayed = 2 }, // would be (2,2) -> (3,2) if still alive
        };

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 2,
            GameState = 6, // CreateCommands.ExecuteTurn refuses to plan in any other state
            Board = board,
            Players = [robot],
            GameCards = cards,
        };

        // Act
        var plan = new CreateCommands(request).ExecuteTurn();

        // Assert
        Assert.True(plan.Planned, plan.Summary);

        // Sanity: phase 1 still carries the robot onto the pit square itself.
        var phase1Move = plan.Commands.Single(c =>
            c.RobotID == RobotId && c.Phase == 1 && c.CommandType == SquareAction.Move);
        Assert.Equal(2, phase1Move.EndPos.X);
        Assert.Equal(2, phase1Move.EndPos.Y);

        // Landing on the pit kills the robot immediately -- same status the damage-death path
        // already sets (tPlayerStatus.Dead = 11).
        Assert.Contains(plan.Commands, c =>
            c.RobotID == RobotId && c.CommandType == SquareAction.SetPlayerStatus && c.Value == 11);

        // The human is told to physically remove the robot from the table -- same
        // SetButtonText/"Remove Robot: " User Input prompt the damage-death path uses.
        Assert.Contains(plan.Commands, c =>
            c.RobotID == RobotId && c.CommandType == SquareAction.SetButtonText &&
            c.text.StartsWith("Remove Robot", StringComparison.Ordinal));

        // Dead for the rest of the turn: no phase-2 movement command for this robot at all,
        // even though it had a second Forward1 card programmed.
        Assert.DoesNotContain(plan.Commands, c =>
            c.RobotID == RobotId && c.Phase == 2 &&
            (c.CommandType == SquareAction.Move || c.CommandType == SquareAction.PushedMove ||
             c.CommandType == SquareAction.BoardMove));
    }
}
