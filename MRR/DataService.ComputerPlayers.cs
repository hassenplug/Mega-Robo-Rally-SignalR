using System.Data;
using MySqlConnector;
using MRR.Bot;

namespace MRR.Services
{
    /// <summary>
    /// A computer seat that needs a program this turn: in a programming status, not yet Ready.
    /// Hand holds one card type per card the program may use -- the hand plus whatever is
    /// already in its registers, since SetRobotProgram returns those to the hand first.
    /// </summary>
    public sealed record ComputerSeat(int RobotID, int Level, int PositionValid, int Facing, List<int> Hand, int DamageInDiscard,
                                      List<int> DrawPool);

    /// <summary>
    /// DataService — computer players (documents/COMPUTER_PLAYER_DESIGN.md §4, §5.8).
    /// Database reads and writes only; the decisions come from MRR.Bot.
    /// </summary>
    public partial class DataService
    {
        /// <summary>
        /// GM "Fill with Computers" (setup phase, GameState == 1): claims every seat no player
        /// has picked yet as a computer at <paramref name="level"/>, using the same SelectSeat
        /// a phone uses. Each gets the lowest seat number not yet taken, the lowest-numbered
        /// active robot body not yet taken, Robots.Computer = level, and the name
        /// "CPU {seat} {level name}". Returns how many seats were claimed; 0 when none were
        /// open, so pressing it twice is harmless.
        ///
        /// Like End Selection, press it after the last human has picked: a human claiming at the
        /// same instant could collide, because SelectSeat's own uniqueness checks are disabled.
        /// </summary>
        public int ClaimUnselectedAsComputers(int level, int? maxCount = null)
        {
            var profile = BotProfile.ForLevel(level);
            var open = GetIntList("SELECT RobotID FROM Robots WHERE Status <> 1 ORDER BY RobotID");
            int claimed = 0;

            // maxCount: claim only the first that many open seats (lowest RobotID first); the
            // caller removes the rest (GM "Fill with Computers" with a count).
            foreach (int robotId in open.Take(Math.Max(0, maxCount ?? open.Length)))
            {
                var usedSeats = GetIntList("SELECT PlayerSeat FROM Robots WHERE Status = 1").ToHashSet();
                int seat = 1;
                while (usedSeats.Contains(seat)) seat++;

                int bodyId = GetIntFromDB(
                    "SELECT COALESCE(MIN(RobotBodyID), 0) FROM RobotBodies WHERE BodyActive > 0 " +
                    "AND RobotBodyID NOT IN (SELECT RobotBodyID FROM Robots WHERE Status = 1 AND RobotBodyID IS NOT NULL)");
                if (bodyId == 0)
                {
                    Console.WriteLine($"Fill with Computers: no free robot body for start {robotId}; stopped after {claimed}.");
                    break;
                }

                // SelectSeat joins SeatOrientation on the seat number, so a seat with no row
                // there is not claimed.
                if (!SelectSeat(seat, robotId, bodyId))
                {
                    Console.WriteLine($"Fill with Computers: could not claim start {robotId} as seat {seat}; stopped after {claimed}.");
                    break;
                }

                using var connection = new MySqlConnection(_connectionString);
                connection.Open();
                using var update = new MySqlCommand(
                    "UPDATE Robots SET Computer = @level, OperatorName = @name WHERE RobotID = @id", connection);
                update.Parameters.AddWithValue("@level", profile.Level);
                update.Parameters.AddWithValue("@name", $"CPU {seat} {profile.Name}");
                update.Parameters.AddWithValue("@id", robotId);
                update.ExecuteNonQuery();
                claimed++;
            }

            return claimed;
        }

        /// <summary>
        /// Computer seats that still need a program: Robots.Computer &gt; 0, a status that
        /// allows programming (RobotStatus.Programming = 1), and not already Ready (4) -- a robot
        /// already Ready when programming is re-entered (GM Create Program / Reload Position)
        /// keeps its program. Shut-down, sat-out and dead robots are not in a programming status.
        /// </summary>
        public List<ComputerSeat> GetComputersToProgram()
        {
            var seats = new List<ComputerSeat>();
            var robots = GetQueryResults(
                "SELECT r.RobotID, r.Computer, r.PositionValid, r.CurrentPosDir FROM Robots r " +
                "JOIN RobotStatus rs ON rs.RobotStatusID = r.`Status` " +
                $"WHERE IFNULL(r.Computer, 0) > 0 AND rs.Programming = 1 AND r.`Status` <> {(int)tPlayerStatus.ReadyToRun} " +
                "ORDER BY r.RobotID");

            foreach (DataRow row in robots.Rows)
            {
                int robotId = Convert.ToInt32(row["RobotID"]);
                var hand = GetIntList(
                    $"SELECT CardTypeID FROM MoveCards WHERE Owner = {robotId} AND CardLocation IN (1, 2) ORDER BY CardID");
                seats.Add(new ComputerSeat(
                    robotId,
                    Convert.ToInt32(row["Computer"]),
                    Convert.ToInt32(row["PositionValid"]),
                    Convert.ToInt32(row["CurrentPosDir"]),
                    hand.ToList(),
                    // Spam/Haywire in the discard pile: shutting down clears these too.
                    GetIntFromDB($"SELECT COUNT(*) FROM MoveCards WHERE Owner = {robotId} AND CardLocation = 3 AND CardTypeID IN (10, 11)"),
                    // Deck and discard contents -- what a Spam's replacement comes from. Sorted by
                    // type so the brain cannot see the real draw order (design §5.4).
                    GetIntList($"SELECT CardTypeID FROM MoveCards WHERE Owner = {robotId} AND CardLocation IN (0, 3) ORDER BY CardTypeID").ToList()));
            }
            return seats;
        }

        /// <summary>
        /// Sets a computer's "shut down this turn" choice (design §5.6) through the phone's own
        /// ToggleShutDown, so turning it on also fills any empty registers. Only toggles between
        /// None and Currently: a robot the GM is sitting out is left alone (returns false), as is
        /// one already in the requested state (returns true). ToggleShutDown itself refuses
        /// outside programming (GameState 4) and for a dead robot.
        /// </summary>
        public bool SetComputerShutDown(int robotId, bool on)
        {
            var row = GetQueryResults($"SELECT ShutDown FROM Robots WHERE RobotID = {robotId}");
            if (row.Rows.Count == 0) return false;
            int current = Convert.ToInt32(row.Rows[0]["ShutDown"]);
            if (current == (int)tShutDown.SitOut) return false;
            if ((current == (int)tShutDown.Currently) == on) return true;
            return ToggleShutDown(robotId);
        }

        /// <summary>
        /// The game as the computer players' brain sees it (design gap 5). Read-only: it reads
        /// the database into fresh objects and never touches the shared GameCards, which
        /// BuildTurnRequest() reloads (and a phone tap edits) -- so it is safe while players
        /// program. The robots carry no cards.
        /// </summary>
        public BotSnapshot BuildBotSnapshot()
        {
            var players = GetPlayerStatesFromDB();
            foreach (var p in players) p.AllGameCards = null; // not the shared GameCards

            return new BotSnapshot
            {
                Board         = BoardLoadFromDB(BoardID),
                Players       = players,
                PhaseCount    = PhaseCount,
                TotalFlags    = TotalFlags,
                LaserDamage   = LaserDamage,
                FieldEnclosed = FieldEnclosed,
                GameType      = GameType,
                // The deck every robot is dealt (GameNewAddCards uses set 4): Expert's opponent sampler.
                DeckMix       = GetIntList("SELECT CardTypeID FROM MoveCardsCompleteList WHERE SetID = 4 ORDER BY CardTypeID"),
            };
        }

        /// <summary>Sets Robots.Computer (0 = a human plays it). False if there is no such robot.</summary>
        public bool SetRobotComputer(int robotId, int level)
        {
            if (GetIntFromDB($"SELECT COUNT(*) FROM Robots WHERE RobotID = {robotId}") == 0) return false;
            ExecuteSQL($"UPDATE Robots SET Computer = {level} WHERE RobotID = {robotId}");
            return true;
        }

        public bool IsComputer(int robotId) =>
            GetIntFromDB($"SELECT IFNULL(Computer, 0) FROM Robots WHERE RobotID = {robotId}") > 0;

        /// <summary>
        /// Human robots the state-4 gate is still waiting on: the gate's own test
        /// (Status &lt;&gt; 4 AND Status &lt; 9) restricted to Computer = 0, so shut-down and
        /// sat-out humans do not count (design §5.7 step 3).
        /// </summary>
        public int HumansStillProgramming() =>
            GetIntFromDB("SELECT COUNT(*) FROM Robots WHERE IFNULL(Computer, 0) = 0 AND `Status` <> 4 AND `Status` < 9");

        /// <summary>
        /// Appended to the title every client shows (titlemsg) while the state-4 gate waits for
        /// computer players (design gap 14). Null when there is nothing to say.
        /// </summary>
        public string? TitleNote { get; set; }

        /// <summary>
        /// Applies a whole program to one robot in a single transaction (design §5.8, gap 11):
        /// every register and, if given, the facing -- or nothing at all.
        ///
        /// Refused (returns false, nothing changed) unless GameState is 4 and the robot is in a
        /// programming status; the robot's row is locked first, so lock-in's
        /// "UPDATE Robots SET Status = 13" (GameController.NextState, state 5) waits for this
        /// to finish and cannot interleave with it. Also refused when the hand lacks a requested
        /// type, or more types than PhaseCount are given.
        ///
        /// Cards are picked like UpdateCardPlayed picks them: the lowest CardID of the type in
        /// hand. Status becomes Ready (4) when every register up to PhaseCount is filled and the
        /// facing is chosen, else Programming (3) -- the same rule as UpdateProgrammingStatus.
        ///
        /// Unlike UpdateCardPlayed this does not touch the robot's LEDs: computer robots'
        /// lights are not switched on for programming (GameController.ComputerPlayers.cs), and
        /// a robot round trip must not run inside the transaction.
        /// </summary>
        public bool SetRobotProgram(int robotId, IReadOnlyList<int> cardTypes, int? facing)
        {
            int phaseCount = PhaseCount;
            if (cardTypes.Count > phaseCount) return false;

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();

            MySqlCommand Cmd(string sql)
            {
                var cmd = new MySqlCommand(sql, connection, tx);
                cmd.Parameters.AddWithValue("@id", robotId);
                return cmd;
            }

            // 1. Lock the robot's row and check it may still be programmed.
            int programming, positionValid;
            using (var cmd = Cmd(
                "SELECT rs.Programming, r.PositionValid FROM Robots r " +
                "JOIN RobotStatus rs ON rs.RobotStatusID = r.`Status` " +
                "WHERE r.RobotID = @id FOR UPDATE"))
            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read()) return false;
                programming = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                positionValid = reader.GetInt32(1);
            }

            int gameState;
            using (var cmd = Cmd("SELECT iValue FROM CurrentGameData WHERE sKey = 'GameState'"))
                gameState = Convert.ToInt32(cmd.ExecuteScalar() ?? -1);

            if (gameState != 4 || programming != 1) return false; // disposing tx rolls back

            // 2. Every card in a register goes back to the hand.
            using (var cmd = Cmd("UPDATE MoveCards SET PhasePlayed = -1, CardLocation = 1 WHERE Owner = @id AND CardLocation = 2"))
                cmd.ExecuteNonQuery();

            // 3. Register by register, the lowest CardID of the requested type.
            var placed = new List<(int CardID, int Phase)>();
            for (int i = 0; i < cardTypes.Count; i++)
            {
                int cardId;
                using (var cmd = Cmd("SELECT MIN(CardID) FROM MoveCards WHERE Owner = @id AND CardLocation = 1 AND CardTypeID = @type"))
                {
                    cmd.Parameters.AddWithValue("@type", cardTypes[i]);
                    var result = cmd.ExecuteScalar();
                    if (result == null || result == DBNull.Value) return false; // missing type: roll back
                    cardId = Convert.ToInt32(result);
                }

                using (var cmd = Cmd("UPDATE MoveCards SET PhasePlayed = @phase, CardLocation = 2 WHERE CardID = @card AND Owner = @id"))
                {
                    cmd.Parameters.AddWithValue("@phase", i + 1);
                    cmd.Parameters.AddWithValue("@card", cardId);
                    cmd.ExecuteNonQuery();
                }
                placed.Add((cardId, i + 1));
            }

            // 4. The facing travels with the program.
            if (facing.HasValue)
            {
                int dir = facing.Value;
                using var cmd = Cmd(
                    $"UPDATE Robots SET CurrentPosDir = {dir}, SeatArrow = {SeatArrowSql(dir.ToString())}, " +
                    "PositionValid = 1 WHERE RobotID = @id");
                cmd.ExecuteNonQuery();
                positionValid = 1;
            }

            // 5. Status and the CardsDealt/CardsPlayed summary, still under the row lock.
            int newStatus = (placed.Count == phaseCount && positionValid > 0)
                ? (int)tPlayerStatus.ReadyToRun
                : (int)tPlayerStatus.Programming;
            RebuildRobotCardsSummary(connection, robotId, newStatus, tx);

            tx.Commit();

            // Mirror the moves into the in-memory GameCards, as UpdateCardPlayed step 8 does.
            foreach (var card in GameCards.Where(c => c.Owner == robotId && c.CardLocation == 2))
            {
                card.CardLocation = 1;
                card.PhasePlayed = -1;
            }
            foreach (var (cardId, phase) in placed)
            {
                var card = GameCards.FirstOrDefault(c => c.ID == cardId && c.Owner == robotId);
                if (card == null) continue;
                card.CardLocation = 2;
                card.PhasePlayed = phase;
            }

            return true;
        }
    }
}
