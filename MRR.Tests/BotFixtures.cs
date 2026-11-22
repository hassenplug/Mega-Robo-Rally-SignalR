using System.Text;

namespace MRR.Tests;

/// <summary>
/// Shared inputs for the computer-player M0 measurements (documents/COMPUTER_PLAYER_DESIGN.md §6).
///
/// The board is synthetic but deliberately busy: a belt ring (normal and fast, with CW corners),
/// two gears, three pits, walls and five flags on a 12x12 field, so a plan exercises belts, turns,
/// walls, pits, lasers and flags rather than an empty floor. It is a stand-in for a real board
/// (the real library is in the database, not the repo), so the timings it produces are an
/// order-of-magnitude guide, not a promise.
/// </summary>
internal static class BotFixtures
{
    public const int Size = 12;
    public const int TotalFlags = 5;

    /// <summary>Flag n sits at Flags[n - 1].</summary>
    public static readonly (int X, int Y)[] Flags = [(8, 5), (2, 9), (10, 2), (5, 10), (1, 1)];

    /// <summary>Start squares for robots 1..6 (index 0 = robot 1).</summary>
    private static readonly (int X, int Y, Direction Dir)[] Starts =
    [
        (0, 5, Direction.Right), (11, 5, Direction.Left), (5, 0, Direction.Down),
        (5, 11, Direction.Up), (2, 10, Direction.Up), (9, 1, Direction.Down),
    ];

    /// <summary>A representative nine-card hand: eight distinct types, one duplicate.</summary>
    public static readonly MoveCard.tCardType[] Hand =
    [
        MoveCard.tCardType.Forward1, MoveCard.tCardType.Forward1, MoveCard.tCardType.Forward2,
        MoveCard.tCardType.Forward3, MoveCard.tCardType.RTurn, MoveCard.tCardType.LTurn,
        MoveCard.tCardType.UTurn, MoveCard.tCardType.Back1, MoveCard.tCardType.Again,
    ];

    // Board actions, written the way the real board data and the existing tests write them
    // (Move/Rotate/Flag with sequence and phase mask; Move's parameter is a Direction that the
    // square's Rotation turns).
    private static BoardActionsCollection Belt() => [new BoardAction(SquareAction.Move, 1, 3, 31)];
    private static BoardActionsCollection FastBelt() =>
        [new BoardAction(SquareAction.Move, 1, 1, 31), new BoardAction(SquareAction.Move, 1, 3, 31)];
    private static BoardActionsCollection TurnCw() =>
        [new BoardAction(SquareAction.Move, 1, 3, 31), new BoardAction(SquareAction.Rotate, 1, 4, 31)];

    public static BoardElementCollection Board()
    {
        var board = new BoardElementCollection(Size, Size);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                board.SetSquare(x, y, SquareType.Blank, Direction.None, new BoardActionsCollection());

        // Belt ring, clockwise: top edge Right, right edge Down (fast), bottom edge Left, left edge Up.
        for (int x = 4; x <= 8; x++) board.SetSquare(x, 3, SquareType.NormalBelt, Direction.Right, Belt());
        for (int y = 4; y <= 7; y++) board.SetSquare(9, y, SquareType.FastBelt, Direction.Down, FastBelt());
        for (int x = 4; x <= 8; x++) board.SetSquare(x, 8, SquareType.NormalBelt, Direction.Left, Belt());
        for (int y = 4; y <= 7; y++) board.SetSquare(3, y, SquareType.NormalBelt, Direction.Up, Belt());
        board.SetSquare(3, 3, SquareType.NormalTurnCW, Direction.Right, TurnCw());
        board.SetSquare(9, 3, SquareType.NormalTurnCW, Direction.Down, TurnCw());
        board.SetSquare(9, 8, SquareType.NormalTurnCW, Direction.Left, TurnCw());
        board.SetSquare(3, 8, SquareType.NormalTurnCW, Direction.Up, TurnCw());

        board.SetSquare(5, 5, SquareType.GearCW, Direction.None, [new BoardAction(SquareAction.Rotate, 1, 4, 31)]);
        board.SetSquare(6, 5, SquareType.GearCCW, Direction.None, [new BoardAction(SquareAction.Rotate, 2, 4, 31)]);

        foreach (var (x, y) in new[] { (6, 6), (1, 10), (10, 1) })
            board.SetSquare(x, y, SquareType.Pit, Direction.None, new BoardActionsCollection());

        board.GetSquare(4, 5)!.AddWall(Direction.Down);
        board.GetSquare(7, 4)!.AddWall(Direction.Right);
        board.GetSquare(2, 6)!.AddWall(Direction.Up);
        board.GetSquare(8, 10)!.AddWall(Direction.Left);
        board.GetSquare(10, 6)!.AddWall(Direction.Down);

        for (int n = 1; n <= TotalFlags; n++)
        {
            var (fx, fy) = Flags[n - 1];
            board.GetSquare(fx, fy)!.AddAction(new BoardAction(SquareAction.Flag, n, 10, 31));
        }

        return board;
    }

    /// <summary>Six robots, IDs 1..6, priority = ID, all ready to run, none holding a flag yet.</summary>
    public static PlayerStates Players()
    {
        var players = new PlayerStates();
        for (int i = 0; i < Starts.Length; i++)
        {
            var (x, y, dir) = Starts[i];
            var p = new PlayerState
            {
                ID = i + 1,
                Name = "R" + (i + 1),
                PlayerStatus = tPlayerStatus.ReadyToRun,
                Priority = i + 1,
                Energy = 3,
                CurrentPos = new RobotLocation(dir, x, y),
            };
            p.NextPos = new RobotLocation(p.CurrentPos);
            players.Add(p);
        }
        return players;
    }

    /// <summary>The cards for one robot's program: type i goes into register i + 1.</summary>
    public static void AddProgram(CardList cards, int owner, IReadOnlyList<MoveCard.tCardType> types)
    {
        for (int i = 0; i < types.Count; i++)
            cards.Add(new MoveCard(owner * 100 + i, types[i]) { Owner = owner, PhasePlayed = i + 1 });
    }

    public static TurnRequest Request(BoardElementCollection board, PlayerStates players, CardList cards, int phaseCount) =>
        new()
        {
            Turn = 1,
            Phase = 0,
            PhaseCount = phaseCount,
            GameState = 6, // the planner refuses to plan in any other state
            TotalFlags = TotalFlags,
            LaserDamage = 1,
            Board = board,
            Players = players,
            GameCards = cards,
        };

    /// <summary>
    /// Plans a turn in which only <paramref name="me"/> holds cards (the "other robots are
    /// stationary and cardless" model the plan uses for Levels 2-4).
    /// </summary>
    public static TurnPlan PlanSolo(BoardElementCollection board, PlayerStates players, int me,
        IReadOnlyList<MoveCard.tCardType> program, int? phaseCount = null)
    {
        var cards = new CardList();
        AddProgram(cards, me, program);
        var request = Request(board, players, cards, phaseCount ?? program.Count);
        return new CreateCommands(request).CreateTurn();
    }

    private static readonly SquareAction[] PositionCommands =
    [
        SquareAction.Move, SquareAction.PushedMove, SquareAction.BoardMove, SquareAction.Rotate,
        SquareAction.BoardRotate, SquareAction.BoardMoveRotate, SquareAction.PushedMoveRotate,
    ];

    /// <summary>Where a robot ends the plan: its last positional command's end, else where it started.</summary>
    public static RobotLocation FinalPosition(TurnPlan plan, PlayerState start)
    {
        var last = plan.Commands.LastOrDefault(c => c.RobotID == start.ID && PositionCommands.Contains(c.CommandType));
        return last == null ? new RobotLocation(start.CurrentPos) : new RobotLocation(last.EndPos);
    }

    /// <summary>
    /// A stand-in for the real score, with the same shape: flags up, deaths and damage down,
    /// distance to the next flag down. Only used to make the benchmark's searches choose
    /// something; the real weights are M2's job.
    /// </summary>
    public static int TrivialScore(TurnPlan plan, PlayerState start)
    {
        int me = start.ID;
        int flags = plan.Commands.Count(c => c.RobotID == me && c.CommandType == SquareAction.Flag && c.Value > 0);
        bool dead = plan.Commands.Any(c => c.RobotID == me && c.CommandType == SquareAction.Dead);
        int damage = plan.Commands.Count(c => c.RobotID == me && c.CommandType == SquareAction.DealSpamCard);
        var end = FinalPosition(plan, start);
        var (fx, fy) = Flags[Math.Min(start.LastFlag + flags, TotalFlags - 1)];
        int distance = Math.Abs(end.X - fx) + Math.Abs(end.Y - fy);
        return flags * 1000 - (dead ? 400 : 0) - damage * 80 - distance * 30;
    }

    // ---- signatures: equal strings mean "the same thing" -------------------------------------

    /// <summary>Everything observable about a plan except database ids and sequencing numbers.</summary>
    public static string Signature(TurnPlan plan, Func<CommandItem, bool>? filter = null)
    {
        var sb = new StringBuilder();
        sb.Append(plan.Planned).Append('|').Append(plan.NextGameState).Append('|').Append(plan.Summary).Append('\n');
        foreach (var c in plan.Commands.Where(c => filter == null || filter(c)))
            sb.Append(CommandKey(c)).Append('\n');
        foreach (var s in plan.SpamConsumed) sb.Append("spam:").Append(s.RobotID).Append(',').Append(s.CardID).Append('\n');
        foreach (var s in plan.SpamReplacements) sb.Append("repl:").Append(s.RobotID).Append(',').Append(s.Phase).Append(',').Append(s.CardTypeID).Append('\n');
        foreach (var w in plan.Warnings) sb.Append("warn:").Append(w).Append('\n');
        return sb.ToString();
    }

    public static string CommandKey(CommandItem c) =>
        $"{c.Phase}/{c.PhaseStep} {c.CommandType} r{c.RobotID} v{c.Value}/{c.ValueB} " +
        $"({c.StartPos.X},{c.StartPos.Y},{c.StartPos.Direction})->({c.EndPos.X},{c.EndPos.Y},{c.EndPos.Direction}) '{c.text}'";

    public static string BoardSignature(BoardElementCollection board)
    {
        var sb = new StringBuilder();
        sb.Append(board.BoardCols).Append('x').Append(board.BoardRows).Append('\n');
        foreach (var e in board.BoardElements.OrderBy(e => e.BoardRow).ThenBy(e => e.BoardCol))
        {
            sb.Append(e.BoardCol).Append(',').Append(e.BoardRow).Append(',').Append(e.Type).Append(',').Append(e.Rotation).Append(':');
            foreach (var a in e.ActionList)
                sb.Append(a.SquareAction).Append('/').Append(a.Parameter).Append('/').Append(a.ActionSequence).Append('/').Append(a.Phase).Append(';');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static string PlayersSignature(PlayerStates players)
    {
        var sb = new StringBuilder();
        foreach (var p in players)
            sb.Append($"{p.ID} {p.PlayerStatus} {p.ShutDown} pos({p.CurrentPos.X},{p.CurrentPos.Y},{p.CurrentPos.Direction}) " +
                      $"next({p.NextPos.X},{p.NextPos.Y},{p.NextPos.Direction}) flag{p.LastFlag} e{p.Energy} " +
                      $"prio{p.Priority} resp{p.RespawnID} dmg{p.DamagePoints}\n");
        return sb.ToString();
    }

    // ---- hands and programs ------------------------------------------------------------------

    /// <summary>A legal program: <paramref name="count"/> distinct cards drawn from <see cref="Hand"/>, seeded.</summary>
    public static MoveCard.tCardType[] RandomProgram(int seed, int count = 5)
    {
        var rng = new Random(seed);
        return Hand.OrderBy(_ => rng.Next()).Take(count).ToArray();
    }

    /// <summary>How many distinct type sequences of this length the hand can produce.</summary>
    public static int DistinctSequences(IReadOnlyList<MoveCard.tCardType> hand, int length)
    {
        var counts = hand.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        int Count(int remaining)
        {
            if (remaining == 0) return 1;
            int total = 0;
            foreach (var t in counts.Keys.ToList())
            {
                if (counts[t] == 0) continue;
                counts[t]--;
                total += Count(remaining - 1);
                counts[t]++;
            }
            return total;
        }
        return Count(length);
    }
}
