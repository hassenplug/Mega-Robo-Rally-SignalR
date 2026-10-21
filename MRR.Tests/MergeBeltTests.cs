namespace MRR.Tests;

/// <summary>
/// Merging conveyor belts (SquareType 13-16, install/todo.md Section 1). The merge square's
/// SquareAction.OptionalRotate runs at sequence 4 alongside the other belt Rotates and turns a
/// robot only if a conveyor carried it in from the side: from the left of the arrow it turns
/// left, from the right it turns right, from behind not at all. The side is read from
/// PlayerState.PreviousPos, set by CreateCommands.MoveRobot and cleared at the start of board
/// effects.
/// </summary>
public class MergeBeltTests
{
    private const int RobotId = 1;

    private static BoardActionsCollection MergeActions() =>
    [
        new BoardAction(SquareAction.Move, 1, 3, 31),
        new BoardAction(SquareAction.OptionalRotate, 1, 4, 31),
    ];

    private static BoardActionsCollection BeltActions() =>
    [
        new BoardAction(SquareAction.Move, 1, 3, 31),
    ];

    private static TurnPlan PlanOnePhase(BoardElementCollection board, RobotLocation start, CardList? cards = null)
    {
        var robot = new PlayerState
        {
            ID = RobotId,
            PlayerStatus = tPlayerStatus.ReadyToRun,
            Priority = 1,
            CurrentPos = start,
        };
        robot.NextPos = new RobotLocation(robot.CurrentPos);

        var request = new TurnRequest
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = 1,
            GameState = 6,
            Board = board,
            Players = [robot],
            GameCards = cards ?? [],
        };

        var plan = new CreateCommands(request).CreateTurn();
        Assert.True(plan.Planned, plan.Summary);
        return plan;
    }

    // 5x5 board: merge square at (2,2) with its arrow Up, and one ordinary belt feeding it
    // from the given side.
    private static BoardElementCollection MergeBoardFedFrom(int feedX, int feedY, Direction feedArrow)
    {
        var board = new BoardElementCollection(5, 5);
        board.SetSquare(2, 2, SquareType.NormalMergeAll, Direction.Up, MergeActions());
        board.SetSquare(feedX, feedY, SquareType.NormalBelt, feedArrow, BeltActions());
        return board;
    }

    private static List<CommandItem> MergeTurns(TurnPlan plan) =>
        plan.Commands.Where(c => c.RobotID == RobotId && c.CommandType == SquareAction.Rotate).ToList();

    [Fact]
    public void CarriedInFromTheLeft_TurnsLeft()
    {
        var plan = PlanOnePhase(MergeBoardFedFrom(1, 2, Direction.Right), new RobotLocation(Direction.Up, 1, 2));

        var turn = Assert.Single(MergeTurns(plan));
        Assert.Equal(-1, turn.Value);
        Assert.Equal(Direction.Left, turn.EndPos.Direction);
    }

    [Fact]
    public void CarriedInFromTheRight_TurnsRight()
    {
        var plan = PlanOnePhase(MergeBoardFedFrom(3, 2, Direction.Left), new RobotLocation(Direction.Up, 3, 2));

        var turn = Assert.Single(MergeTurns(plan));
        Assert.Equal(1, turn.Value);
        Assert.Equal(Direction.Right, turn.EndPos.Direction);
    }

    [Fact]
    public void CarriedInFromBehind_DoesNotTurn()
    {
        var plan = PlanOnePhase(MergeBoardFedFrom(2, 3, Direction.Up), new RobotLocation(Direction.Up, 2, 3));

        Assert.Empty(MergeTurns(plan));
    }

    [Fact]
    public void DroveOntoMergeWithItsOwnCard_DoesNotTurn()
    {
        // Merge at the top edge, so its own belt move is off the board and skipped: the robot
        // is still on the merge at sequence 4. It came from the left -- but under its own
        // power, which board effects must not count.
        var board = new BoardElementCollection(5, 5);
        board.SetSquare(2, 0, SquareType.NormalMergeAll, Direction.Up, MergeActions());
        var cards = new CardList
        {
            new MoveCard(1, MoveCard.tCardType.Forward1) { Owner = RobotId, PhasePlayed = 1 },
        };

        var plan = PlanOnePhase(board, new RobotLocation(Direction.Right, 1, 0), cards);

        Assert.Empty(MergeTurns(plan));
    }

    private static BoardActionsCollection FastMergeActions() =>
    [
        new BoardAction(SquareAction.Move, 1, 1, 31),
        new BoardAction(SquareAction.OptionalRotate, 1, 2, 31),
        new BoardAction(SquareAction.Move, 1, 3, 31),
        new BoardAction(SquareAction.OptionalRotate, 1, 4, 31),
    ];

    private static BoardActionsCollection FastBeltActions() =>
    [
        new BoardAction(SquareAction.Move, 1, 1, 31),
        new BoardAction(SquareAction.Move, 1, 3, 31),
    ];

    [Fact]
    public void FastMerge_CarriedInFromTheLeftByExpressBelt_TurnsLeft()
    {
        var board = new BoardElementCollection(5, 5);
        board.SetSquare(2, 2, SquareType.FastMergeAll, Direction.Up, FastMergeActions());
        board.SetSquare(1, 2, SquareType.FastBelt, Direction.Right, FastBeltActions());

        var plan = PlanOnePhase(board, new RobotLocation(Direction.Up, 1, 2));

        var turn = Assert.Single(MergeTurns(plan));
        Assert.Equal(-1, turn.Value);
        Assert.Equal(Direction.Left, turn.EndPos.Direction);
    }

    [Fact]
    public void FastMerge_BlockedAfterTurning_DoesNotTurnAgainAtSequence4()
    {
        // Merge at the top edge: the robot is carried on at seq 1, turns at seq 2, its seq 3
        // move off the board is skipped, and it is still on the merge for the seq 4 check.
        var board = new BoardElementCollection(5, 5);
        board.SetSquare(2, 0, SquareType.FastMergeAll, Direction.Up, FastMergeActions());
        board.SetSquare(3, 0, SquareType.FastBelt, Direction.Left, FastBeltActions());

        var plan = PlanOnePhase(board, new RobotLocation(Direction.Up, 3, 0));

        var turn = Assert.Single(MergeTurns(plan));
        Assert.Equal(1, turn.Value);
    }

    [Theory]
    [InlineData(Direction.Up, 1, 2, -1)]    // arrow Up: left side is (1,2)
    [InlineData(Direction.Up, 3, 2, 1)]     // arrow Up: right side is (3,2)
    [InlineData(Direction.Up, 2, 3, 0)]     // arrow Up: behind
    [InlineData(Direction.Right, 2, 1, -1)] // arrow Right: left side is above
    [InlineData(Direction.Right, 2, 3, 1)]  // arrow Right: right side is below
    [InlineData(Direction.Left, 2, 1, 1)]   // arrow Left: right side is above
    [InlineData(Direction.Down, 3, 2, -1)]  // arrow Down: left side is (3,2)
    public void MergeRotation_ReadsSideRelativeToTheArrow(Direction arrow, int prevX, int prevY, int expected)
    {
        var robot = new PlayerState { CurrentPos = new RobotLocation(Direction.Up, 2, 2) };
        robot.PreviousPos = new RobotLocation(Direction.Up, prevX, prevY);

        Assert.Equal(expected, CreateCommands.MergeRotation(robot, arrow));
    }

    [Fact]
    public void PreviousPos_DefaultsToCurrentPos()
    {
        var robot = new PlayerState { CurrentPos = new RobotLocation(Direction.Up, 2, 2) };
        Assert.Same(robot.CurrentPos, robot.PreviousPos);

        robot.PreviousPos = new RobotLocation(Direction.Up, 1, 2);
        robot.ClearPreviousPos();
        Assert.Same(robot.CurrentPos, robot.PreviousPos);
        Assert.Equal(0, CreateCommands.MergeRotation(robot, Direction.Up));
    }
}
