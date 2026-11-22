namespace MRR.Bot
{
    /// <summary>
    /// Shortest walking distance (in squares) from every square to each flag, by BFS over the
    /// board (COMPUTER_PLAYER_DESIGN.md §5.3). Walls follow the planner's rule
    /// (CreateCommands.CalcMoveDistance): leaving a square in direction D is blocked by a wall on
    /// it facing the opposite way, entering one by a wall on it facing D. Pits, trap doors and
    /// Block squares are impassable. Belts are ignored: the planner's own belt handling already
    /// shows in where a plan ends, so this only has to rank end squares.
    ///
    /// Thread-safe once built; build one per snapshot and share it.
    /// </summary>
    public sealed class FlagDistances
    {
        public const int Unreachable = int.MaxValue;

        private readonly int _cols, _rows;
        private readonly Dictionary<int, int[,]> _byFlag = new();

        public FlagDistances(BoardElementCollection board, int totalFlags)
        {
            _cols = board.BoardCols;
            _rows = board.BoardRows;

            var grid = new BoardElement?[_cols, _rows];
            foreach (var square in board.BoardElements)
                if (square.BoardCol >= 0 && square.BoardCol < _cols && square.BoardRow >= 0 && square.BoardRow < _rows)
                    grid[square.BoardCol, square.BoardRow] = square;

            for (int flag = 1; flag <= totalFlags; flag++)
            {
                var flagSquare = board.GetFlagSquare(flag);
                if (flagSquare != null) _byFlag[flag] = Bfs(grid, flagSquare.BoardCol, flagSquare.BoardRow);
            }
        }

        /// <summary>Squares from (x, y) to flag <paramref name="flag"/>; <see cref="Unreachable"/> if there is no path or no such flag.</summary>
        public int Distance(int flag, int x, int y)
        {
            if (!_byFlag.TryGetValue(flag, out var d) || x < 0 || y < 0 || x >= _cols || y >= _rows) return Unreachable;
            return d[x, y];
        }

        /// <summary>
        /// True when facing <paramref name="dir"/> from (x, y) points along a shortest path to the
        /// flag: the neighbour that way is one step closer.
        /// </summary>
        public bool FacesTowards(int flag, int x, int y, Direction dir)
        {
            int here = Distance(flag, x, y);
            if (here == Unreachable || here == 0) return false;
            var (dx, dy) = RotationFunctions.MovementOffset(dir);
            return Distance(flag, x + dx, y + dy) == here - 1;
        }

        private static Direction Opposite(Direction d) => RotationFunctions.Rotate(2, d);

        private static bool Impassable(BoardElement? s) =>
            s != null && (s.Type == SquareType.Pit || s.Type == SquareType.TrapDoor || s.Type == SquareType.Block);

        private int[,] Bfs(BoardElement?[,] grid, int fx, int fy)
        {
            var dist = new int[_cols, _rows];
            for (int x = 0; x < _cols; x++)
                for (int y = 0; y < _rows; y++)
                    dist[x, y] = Unreachable;

            dist[fx, fy] = 0;
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue((fx, fy));
            var dirs = new[] { Direction.Up, Direction.Right, Direction.Down, Direction.Left };

            // Searching outward from the flag: a robot at the neighbour (nx, ny) would walk in
            // direction Opposite(d) to reach (x, y).
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                foreach (var d in dirs)
                {
                    var (dx, dy) = RotationFunctions.MovementOffset(d);
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= _cols || ny >= _rows || dist[nx, ny] != Unreachable) continue;

                    var from = grid[nx, ny];
                    var to = grid[x, y];
                    if (Impassable(from)) continue;
                    var walk = Opposite(d);
                    if (from?.HasWall(Opposite(walk)) == true) continue; // can't leave that way
                    if (to?.HasWall(walk) == true) continue;             // can't enter that way

                    dist[nx, ny] = dist[x, y] + 1;
                    queue.Enqueue((nx, ny));
                }
            }
            return dist;
        }
    }
}
