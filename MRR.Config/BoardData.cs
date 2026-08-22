using System.Data;
using System.Text.Json;
using System.Xml.Serialization;
using MySqlConnector;

namespace MRR.Config
{
    /// <summary>
    /// Data access for the authoring host. Covers only what Configuration & Authoring owns:
    /// Boards, BoardItems, BoardItemActions, GameData, and the operator/hardware tables.
    ///
    /// This duplicates a little of the host's DataService plumbing on purpose. Sharing a
    /// persistence assembly between the two processes would re-couple them, and the reason
    /// Config is a separate process is that it must not be able to disturb a running game.
    /// It never touches CurrentGameData, Robots, MoveCards or CommandList.
    /// </summary>
    public class BoardData
    {
        private readonly string _connectionString;

        public BoardData(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("Rally")
                ?? throw new InvalidOperationException("Connection string 'Rally' not found in configuration.");
        }

        // ── plumbing ────────────────────────────────────────────────────────────

        /// <summary>
        /// DataRow value to int, tolerating NULL. Boards.LaserDamage, PhaseCount and GameType
        /// are all nullable, and 81 of the 89 boards in the live database have a NULL
        /// PhaseCount -- a bare Convert.ToInt32 throws InvalidCastException on those.
        /// </summary>
        public static int AsInt(object? value, int fallback = 0)
            => value is null || value == DBNull.Value ? fallback : Convert.ToInt32(value);


        public DataTable GetQueryResults(string query, params (string Name, object? Value)[] parameters)
        {
            var table = new DataTable();
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = new MySqlCommand(query, connection);
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            using var adapter = new MySqlDataAdapter(command);
            adapter.Fill(table);
            return table;
        }

        public int ExecuteSQL(string query, params (string Name, object? Value)[] parameters)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = new MySqlCommand(query, connection);
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return command.ExecuteNonQuery();
        }

        /// <summary>
        /// Opens a connection and transaction for a multi-statement write. Used by the board
        /// PUT, which deletes and re-inserts every square: without a transaction a malformed
        /// request leaves the board empty.
        /// </summary>
        public MySqlConnection OpenConnection()
        {
            var connection = new MySqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// Serializes a query's rows as {"name":[{column:value,...},...]}, matching the shape
        /// the board editor UI already expects from the host's old endpoints.
        /// </summary>
        public string GetQueryResultsJson(string query, string name, params (string Name, object? Value)[] parameters)
        {
            var table = GetQueryResults(query, parameters);
            var rows = new List<Dictionary<string, object?>>(table.Rows.Count);
            foreach (DataRow row in table.Rows)
            {
                var dict = new Dictionary<string, object?>(table.Columns.Count);
                foreach (DataColumn column in table.Columns)
                {
                    var value = row[column];
                    dict[column.ColumnName] = value == DBNull.Value ? null : value;
                }
                rows.Add(dict);
            }
            return JsonSerializer.Serialize(new Dictionary<string, object> { { name, rows } });
        }

        public int GetIntFromDB(string query, params (string Name, object? Value)[] parameters)
        {
            var table = GetQueryResults(query, parameters);
            if (table.Rows.Count == 0) return 0;
            var value = table.Rows[0][0];
            return value == DBNull.Value ? 0 : Convert.ToInt32(value);
        }

        // ── board load / save ───────────────────────────────────────────────────

        /// <summary>
        /// Loads a board into a BoardElementCollection. The host has its own copy of this for
        /// reading the board being played; this one serves the editor and .srx export.
        /// </summary>
        public BoardElementCollection BoardLoadFromDB(int sourceID)
        {
            var squareActions = new BoardActionsCollection();
            var actionTable = GetQueryResults(
                "SELECT X,Y,SquareAction,ActionSequence,Phase,Parameter FROM BoardItemActions WHERE BoardID=@id;",
                ("@id", sourceID));
            foreach (DataRow actionRow in actionTable.Rows)
            {
                var oneAction = new BoardAction(
                    (SquareAction)Convert.ToInt32(actionRow["SquareAction"]),
                    Convert.ToInt32(actionRow["Parameter"]),
                    Convert.ToInt32(actionRow["ActionSequence"]),
                    Convert.ToInt32(actionRow["Phase"]))
                {
                    SquareX = Convert.ToInt32(actionRow["X"]),
                    SquareY = Convert.ToInt32(actionRow["Y"]),
                };
                squareActions.Add(oneAction);
            }

            var board = new BoardElementCollection();
            var itemTable = GetQueryResults(
                "SELECT X,Y,SquareType,Rotation FROM BoardItems WHERE BoardID=@id;", ("@id", sourceID));
            foreach (DataRow row in itemTable.Rows)
            {
                int boardX = Convert.ToInt32(row["X"]);
                int boardY = Convert.ToInt32(row["Y"]);
                if (boardX + 1 > board.BoardCols) board.BoardCols = boardX + 1;
                if (boardY + 1 > board.BoardRows) board.BoardRows = boardY + 1;

                var forThisSquare = new BoardActionsCollection();
                foreach (BoardAction action in squareActions.Where(sa => sa.SquareX == boardX && sa.SquareY == boardY))
                    forThisSquare.Add(action);

                board.SetSquare(boardX, boardY,
                    (SquareType)Convert.ToInt32(row["SquareType"]),
                    (Direction)Convert.ToInt32(row["Rotation"]),
                    forThisSquare);
            }

            var header = GetQueryResults(
                "SELECT BoardName, LaserDamage, GameType FROM Boards WHERE BoardID=@id;", ("@id", sourceID));
            if (header.Rows.Count > 0)
            {
                board.BoardName   = header.Rows[0]["BoardName"]?.ToString();
                board.LaserDamage = AsInt(header.Rows[0]["LaserDamage"], 1);
                board.GameType    = AsInt(header.Rows[0]["GameType"]);
            }
            board.TotalFlags = board.CalcTotalFlags();
            return board;
        }

        /// <summary>
        /// Replaces a board's squares and actions from a BoardElementCollection, in one
        /// transaction. Used by .srx import.
        /// </summary>
        public void BoardSaveToDB(int destinationID, BoardElementCollection board)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            void Run(string sql)
            {
                using var command = new MySqlCommand(sql, connection, transaction);
                command.ExecuteNonQuery();
            }

            Run($"DELETE FROM BoardItemActions WHERE BoardID={destinationID};");
            Run($"DELETE FROM BoardItems WHERE BoardID={destinationID};");

            foreach (var square in board.BoardElements)
            {
                Run("INSERT INTO BoardItems (BoardID, X, Y, SquareType, Rotation) VALUES (" +
                    $"{destinationID},{square.BoardCol},{square.BoardRow},{(int)square.Type},{(int)square.Rotation});");

                foreach (var action in square.ActionList)
                {
                    Run("INSERT INTO BoardItemActions (BoardID, X, Y, SquareAction, ActionSequence, Phase, Parameter) VALUES (" +
                        $"{destinationID},{square.BoardCol},{square.BoardRow},{(int)action.SquareAction}," +
                        $"{action.ActionSequence},{action.Phase},{action.Parameter});");
                }
            }

            using (var header = new MySqlCommand(
                "UPDATE Boards SET BoardName=@name, X=@cols, Y=@rows, GameType=@gameType, " +
                "TotalFlags=@totalFlags, LaserDamage=@laserDamage WHERE BoardID=@id;", connection, transaction))
            {
                header.Parameters.AddWithValue("@name", board.BoardName ?? "Imported");
                header.Parameters.AddWithValue("@cols", board.BoardCols);
                header.Parameters.AddWithValue("@rows", board.BoardRows);
                header.Parameters.AddWithValue("@gameType", board.GameType);
                header.Parameters.AddWithValue("@totalFlags", board.CalcTotalFlags());
                header.Parameters.AddWithValue("@laserDamage", board.LaserDamage);
                header.Parameters.AddWithValue("@id", destinationID);
                header.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        /// <summary>Deserializes a .srx board file. Returns null if the file is missing or malformed.</summary>
        public static BoardElementCollection? LoadBoardFile(string fileName)
        {
            if (!File.Exists(fileName)) return null;
            try
            {
                var serializer = new XmlSerializer(typeof(BoardElementCollection));
                using var reader = new StreamReader(fileName);
                return serializer.Deserialize(reader) as BoardElementCollection;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Failed to parse {fileName}: {ex.Message}");
                return null;
            }
        }
    }
}
