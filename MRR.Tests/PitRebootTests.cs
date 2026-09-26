namespace MRR.Tests;

/// <summary>
/// Spec test for the Pit/reboot mechanic's death half (install/todo.md Section 1, "Reboot
/// mechanic" -- pit/off-board entry). CreateCommands.MoveRobot() checks the landed square's
/// Type for SquareType.Pit and, if it's a pit, calls KillRobot() (CreateCommands.cs ~1743),
/// which adds two SquareAction.DealSpamCard commands, a SquareAction.SetPlayerStatus command
/// with tPlayerStatus.Dead (11), and queues the "Remove Robot: {Name}" User Input prompt
/// (CommandCategories.UserInput, CommandList.cs:418) -- a blocking confirmation telling the
/// human to physically take the robot off the table, flushed once the whole phase's per-card
/// movement loop finishes (see CreatePhase() and the comment on _pendingRemovalMessages) rather
/// than emitted inline, specifically so it can't stall a later-priority robot's still-pending
/// move in the same phase (RobotPushedIntoPit_DoesNotStallALaterRobotsMoveInTheSamePhase below).
/// PlayerState.IsRunning (PlayerStatus != Dead && != ShutDown) then makes the phase-processing
/// loop skip the now-dead robot for the rest of the turn with no extra suppression code needed.
///
/// This covers only the slice of the mechanic CreateCommands' pure turn-planning layer can
/// exercise in isolation. Respawn next turn (RespawnRobotAtRebootToken, the direction picker,
/// the reboot-square push-on-entry) spans DataService/GameController and the phone UI across
/// two turns -- see MRR.Tests/RebootEntryTests.cs for that half.
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
            PlayerStatus = tPlayerStatus.ReadyToRun,
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
            GameState = 6, // CreateCommands.CreateTurn refuses to plan in any other state
            Board = board,
            Players = [robot],
            GameCards = cards,
        };

        // Act
        var plan = new CreateCommands(request).CreateTurn();

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

    private const int PusherId = 1;
    private const int PushedId = 2;
    private const int LaterRobotId = 3;

    [Fact]
    public void RobotPushedIntoPit_DoesNotStallALaterRobotsMoveInTheSamePhase()
    {
        // Regression test for the exact bug reported 2026-09-25: pusher (priority 1) moves onto
        // the pushed robot's square, shoving it into a pit at (3,2) -- this happens while
        // CreatePhase() is still resolving the *pusher's* own card, before the per-card loop
        // ever reaches laterRobot's (priority 2) card. Before the fix, KillRobot() called
        // ShowMessageToPlayer() (a blocking User Input command) inline at that exact moment, so
        // its commands landed in ListOfCommands ahead of laterRobot's still-to-come move --
        // CommandProcess batches strictly by list order (SequenceCommands()), so laterRobot's
        // move would not even be marked ready until a human confirmed the pushed robot's
        // removal. The fix defers that prompt to after the whole phase's per-card loop.
        var board = new BoardElementCollection(5, 5);
        board.SetSquare(3, 2, SquareType.Pit, Direction.None, new BoardActionsCollection());

        var pusher = new PlayerState
        {
            ID = PusherId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 1,
            CurrentPos = new RobotLocation(Direction.Right, 1, 2),
        };
        pusher.NextPos = new RobotLocation(pusher.CurrentPos);

        var pushed = new PlayerState
        {
            ID = PushedId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 2,
            CurrentPos = new RobotLocation(Direction.Right, 2, 2),
        };
        pushed.NextPos = new RobotLocation(pushed.CurrentPos);

        var laterRobot = new PlayerState
        {
            ID = LaterRobotId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 3,
            CurrentPos = new RobotLocation(Direction.Right, 0, 0),
        };
        laterRobot.NextPos = new RobotLocation(laterRobot.CurrentPos);

        var cards = new CardList
        {
            // (1,2) -> (2,2), where `pushed` sits -> pushes it to (3,2), the pit.
            new MoveCard(1, MoveCard.tCardType.Forward1) { Owner = PusherId, PhasePlayed = 1 },
            new MoveCard(2, MoveCard.tCardType.Forward1) { Owner = LaterRobotId, PhasePlayed = 1 },
        };

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 1,
            GameState = 6,
            Board = board,
            Players = [pusher, pushed, laterRobot],
            GameCards = cards,
        };

        var plan = new CreateCommands(request).CreateTurn();

        Assert.True(plan.Planned, plan.Summary);

        // Sanity: the push into the pit actually happened.
        Assert.Contains(plan.Commands, c =>
            c.RobotID == PushedId && c.CommandType == SquareAction.PushedMove &&
            c.EndPos.X == 3 && c.EndPos.Y == 2);

        int laterRobotMoveIndex = plan.Commands.FindIndex(c =>
            c.RobotID == LaterRobotId && c.CommandType == SquareAction.Move);
        int removalPromptIndex = plan.Commands.FindIndex(c =>
            c.RobotID == PushedId && c.CommandType == SquareAction.SetButtonText &&
            c.text.StartsWith("Remove Robot", StringComparison.Ordinal));

        Assert.True(laterRobotMoveIndex >= 0, "laterRobot's move command was not planned at all.");
        Assert.True(removalPromptIndex >= 0, "The pushed robot's removal prompt was not planned.");

        // The whole point of the fix: laterRobot's move must be list-ordered *before* the
        // removal prompt, so CommandProcess's list-order-batched dispatch (SequenceCommands())
        // never groups it behind a blocking confirmation that has nothing to do with it.
        Assert.True(laterRobotMoveIndex < removalPromptIndex,
            $"laterRobot's move (index {laterRobotMoveIndex}) should be planned before the " +
            $"pushed robot's removal prompt (index {removalPromptIndex}), not after.");
    }

    private const int AId = 1;
    private const int BId = 2;
    private const int CId = 3;

    [Fact]
    public void OneMultiSquareMovePushesTwoRobotsIntoTheSamePitSquareInTurn_OrdersRemovalsBeforeReuse()
    {
        // Regression test for the follow-up bug reported 2026-09-25: the end-of-phase-only
        // flush (the fix above) isn't enough when a *single* multi-square move reuses a square
        // a robot just died on, within the same move, before the phase even ends.
        //
        // A(0,2), B(1,2), C(2,2), Pit(3,2), all facing Right. A plays Forward3 (distance 3):
        //   square 1: A -> (1,2), pushing B -> (2,2), pushing C -> (3,2) = the pit. C dies.
        //   square 2: A -> (2,2), pushing B -> (3,2) -- the same square C just died in. B must
        //     not be sent there before C's removal is confirmed.
        //   square 3: A itself -> (3,2) -- the same square B then dies in. A must not be sent
        //     there before B's removal is confirmed either.
        var board = new BoardElementCollection(6, 5);
        board.SetSquare(3, 2, SquareType.Pit, Direction.None, new BoardActionsCollection());

        var a = new PlayerState
        {
            ID = AId, PlayerStatus = tPlayerStatus.ReadyToRun, Priority = 1,
            CurrentPos = new RobotLocation(Direction.Right, 0, 2),
        };
        a.NextPos = new RobotLocation(a.CurrentPos);

        var b = new PlayerState
        {
            ID = BId, PlayerStatus = tPlayerStatus.ReadyToRun, Priority = 2,
            CurrentPos = new RobotLocation(Direction.Right, 1, 2),
        };
        b.NextPos = new RobotLocation(b.CurrentPos);

        var c = new PlayerState
        {
            ID = CId, PlayerStatus = tPlayerStatus.ReadyToRun, Priority = 3,
            CurrentPos = new RobotLocation(Direction.Right, 2, 2),
        };
        c.NextPos = new RobotLocation(c.CurrentPos);

        var cards = new CardList
        {
            new MoveCard(1, MoveCard.tCardType.Forward3) { Owner = AId, PhasePlayed = 1 },
        };

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 1,
            GameState = 6,
            Board = board,
            Players = [a, b, c],
            GameCards = cards,
        };

        var plan = new CreateCommands(request).CreateTurn();

        Assert.True(plan.Planned, plan.Summary);

        // C dies on the first square of A's move.
        Assert.Contains(plan.Commands, cmd =>
            cmd.RobotID == CId && cmd.CommandType == SquareAction.PushedMove &&
            cmd.EndPos.X == 3 && cmd.EndPos.Y == 2);

        int cRemovalIndex = plan.Commands.FindIndex(cmd =>
            cmd.RobotID == CId && cmd.CommandType == SquareAction.SetButtonText &&
            cmd.text.StartsWith("Remove Robot", StringComparison.Ordinal));
        Assert.True(cRemovalIndex >= 0, "C's removal prompt was not planned.");

        // B gets pushed onto that same square on A's second square of movement.
        int bMoveIntoSquareIndex = plan.Commands.FindIndex(cmd =>
            cmd.RobotID == BId && cmd.CommandType == SquareAction.PushedMove &&
            cmd.EndPos.X == 3 && cmd.EndPos.Y == 2);
        Assert.True(bMoveIntoSquareIndex >= 0, "B's move into the pit square was not planned.");

        // The whole point of this test: C's removal must be confirmed before B is sent onto
        // the same square, even though both happen inside the same phase (indeed the same
        // multi-square move) that the end-of-phase-only flush would otherwise let run through
        // uninterrupted.
        Assert.True(cRemovalIndex < bMoveIntoSquareIndex,
            $"C's removal prompt (index {cRemovalIndex}) must come before B's move onto the " +
            $"same square (index {bMoveIntoSquareIndex}), not after.");

        int bRemovalIndex = plan.Commands.FindIndex(cmd =>
            cmd.RobotID == BId && cmd.CommandType == SquareAction.SetButtonText &&
            cmd.text.StartsWith("Remove Robot", StringComparison.Ordinal));
        Assert.True(bRemovalIndex >= 0, "B's removal prompt was not planned.");
        Assert.True(bMoveIntoSquareIndex < bRemovalIndex,
            "B's removal prompt should come after its own move onto the square, not before.");

        // A itself ends its 3-square move on the same square, since it started immediately
        // behind B and C -- B's removal must be confirmed before A is sent there too.
        int aMoveIntoSquareIndex = plan.Commands.FindIndex(cmd =>
            cmd.RobotID == AId && cmd.CommandType == SquareAction.Move &&
            cmd.EndPos.X == 3 && cmd.EndPos.Y == 2);
        Assert.True(aMoveIntoSquareIndex >= 0, "A's own move onto the same square was not planned.");
        Assert.True(bRemovalIndex < aMoveIntoSquareIndex,
            $"B's removal prompt (index {bRemovalIndex}) must come before A's own move onto " +
            $"the same square (index {aMoveIntoSquareIndex}), not after.");
    }
}
