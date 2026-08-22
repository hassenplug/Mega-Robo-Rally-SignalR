using System;
using System.Data;
using System.Text;
using System.IO;
using MySqlConnector;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using MRR.Data;
using MRR.Data.Entities;
using System.Xml.Serialization;

namespace MRR.Services
{
    public class DataService
    {
        private readonly string _connectionString;
        private readonly string DatabaseName;

        public DataService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("Rally")
                ?? throw new InvalidOperationException("Connection string 'Rally' not found in configuration.");
            var builder = new MySqlConnector.MySqlConnectionStringBuilder(_connectionString);
            DatabaseName = builder.Database;
        }

        public string ConnectionString { get { return _connectionString; } }

        /// <summary>
        /// Creates a new MRRDbContext instance using the configured connection string.
        /// Caller is responsible for disposing.
        /// </summary>
        public MRRDbContext CreateDbContext()
        {
            var optionsBuilder = new DbContextOptionsBuilder<MRRDbContext>();
            optionsBuilder.UseMySql(_connectionString, new MySqlServerVersion(new Version(8, 0, 0)));
            return new MRRDbContext(optionsBuilder.Options);
        }

        ///////////////////////////////////////////////////////////////////////////
        // Retrieve all relevant data from the database to send to clients
        ///////////////////////////////////////////////////////////////////////////

        // Lazily-loaded players collection. First access will load from the database.
        private Players? _allPlayers;
        public Players AllPlayers
        {
            get
            {
                if (_allPlayers == null)
                {
                    _allPlayers = GetAllPlayers();
                }
                return _allPlayers;
            }
            set
            {
                _allPlayers = value;
            }
        }

        public int RobotsActive { get; set; }

        public string BoardFileName { get; set; } = string.Empty;

        public int BoardID { get; set; }

        private int _gameState;
        public int GameState
        {
            get => _gameState;
            set
            {
                _gameState = value;
                using var ctx = CreateDbContext();
                var row = ctx.CurrentGameData.Find(10);
                if (row != null)
                {
                    row.IValue = value;
                    ctx.SaveChanges();
                }
            }
        }

        public int PhaseCount { get; set; }

        public CommandList ListOfCommands { get; set; } = new CommandList();

        public CardList GameCards { get; set; } = new CardList();

        public OptionCardList OptionCards { get; set; } = new OptionCardList();

        public Dictionary<int, string> OptionCardNames = new Dictionary<int, string>();

        public BoardElementCollection g_BoardElements { get; set; } = new BoardElementCollection();

        public int Turn { get; set; } = 0;
        public int Phase { get; set; } = 0;

        public GameTypes GameType { get; set; }

        public int OptionsOnStartup { get; set; } = -1;

        public int LaserDamage { get; set; } = 1;

        private int _totalFlags = 5;
        /// <summary>
        /// Number of flags in the current game — one value for the whole game, not per player.
        /// CurrentGameData (iKey 7) is the source of truth: set from the board in
        /// GameController.StartGame(), reloaded by UpdateGameState(), and written through here
        /// so a mid-game restart restores it.
        /// </summary>
        public int TotalFlags
        {
            get => _totalFlags;
            set
            {
                _totalFlags = value;
                using var ctx = CreateDbContext();
                var row = ctx.CurrentGameData.Find(7);
                if (row != null)
                {
                    row.IValue = value;
                    ctx.SaveChanges();
                }
            }
        }

        public AllDataPayload AllData { get; set; } = new AllDataPayload();

        public bool IsOptionsEnabled
        {
            get
            {
                return (OptionsOnStartup > -1);
            }
            set
            {
                if (value)
                {
                    OptionsOnStartup = 1;
                }
                else
                {
                    OptionsOnStartup = -1;
                }
            }
        }


        ///////////////////////////////////////////////////////////////////////////
        // 
        ///////////////////////////////////////////////////////////////////////////

        // Return the results of any query as a JSON string (uses DataTable -> JSON)
        public string GetQueryResultsJson(string query, string name = "data")
        {
            var dt = GetQueryResults(query);
            // Serialize the DataTable rows as an array of objects under a dynamic property name
            var payload = new Dictionary<string, object> { { name, dt } };
            return JsonConvert.SerializeObject(payload);
        }

        public string GetAllDataJson() => JsonConvert.SerializeObject(GetAllDataFromPlayers());

        public AllDataPayload GetAllDataFromPlayers()
        {
            string titlemessage = "Turn " + Turn;
            if (Turn == 0) titlemessage = "Game Setup";
            if (Phase > 0) titlemessage += " Phase " + Phase;
            //foreach (var player in AllPlayers)
            //{
            //    Console.WriteLine(player.ToRobotData().ToString());
            //}

            return new AllDataPayload
            {
                titlemsg  = titlemessage,
                gamestate = GameState,
                robots    = [.. AllPlayers.OrderBy(p => p.Priority).Select(p => p.ToRobotData())],
            };
        }

        public int GetIntFromDB(string strSQL)
        {
            var dt = GetQueryResults(strSQL);
            var returnval = 0;
            if (dt != null && dt.Rows.Count > 0)
            {
                var val = dt.Rows[0][0];
                if (val != DBNull.Value)
                {
                    returnval = Convert.ToInt32(val);
                }
            }

            return returnval;
        }

        public int[] GetIntList(string strSQL)
        {
            List<int> returnvalset = new List<int>();
            var dt = GetQueryResults(strSQL);
            if (dt != null && dt.Rows.Count > 0)
            {
                foreach (DataRow row in dt.Rows)
                {
                    var returnval = 0;
                    var val = row[0];
                    if (val != DBNull.Value)
                    {
                        returnval = Convert.ToInt32(val);
                    }
                    returnvalset.Add(returnval);
                }
            }

            return returnvalset.ToArray();
        }


        ///////////////////////////////////////////////////////////////////////////
        // Execute a command that does not return results (e.g., INSERT, UPDATE, DELETE)
        // Returns the number of affected rows or 0 if an error occurs
        ///////////////////////////////////////////////////////////////////////////        

        // use 
        // _dataService.ExecuteSQL( 
        // instead of 
        // DBConn.Command(


        public int ExecuteSQL(string query)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();

                    using (var command = new MySqlCommand(query, connection))
                    {
                        return command.ExecuteNonQuery();
                    }
                }
                catch (MySqlException ex)
                {
                    // Log or handle the exception appropriately
                    Console.WriteLine($"DB Error ({ex.Number}): {ex.Message}");
                    Console.WriteLine($"sql: ({query})");
                    return 0;
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        // Execute a query that returns results (e.g., SELECT)
        // Returns a list of dictionaries representing rows or an error message
        ///////////////////////////////////////////////////////////////////////////

        public DataTable GetQueryResults(string query)
        {
            var dt = new DataTable();
            using (var connection = new MySqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();
                    using (var command = new MySqlCommand(query, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        dt.Load(reader); // loads schema + rows
                        return dt;
                    }
                }
                catch (MySqlException ex)
                {
                    // Log/throw as appropriate; returning empty table could also be chosen
                    Console.WriteLine($"DB Error ({ex.Number}): {ex.Message}");
                    Console.WriteLine($"sql: ({query})");

                    return dt; // empty table lets callers iterate without null checks
                }
            }
        }

        ////////////////////////////////////////////////////////////////////////////
        // Load board data from the database into a BoardElementCollection
        ////////////////////////////////////////////////////////////////////////////    
        public BoardElementCollection BoardLoadFromDB(int sourceID)
        {
            BoardElementCollection l_BoardElements = new BoardElementCollection();
            string strSQL;
            BoardActionsCollection squareActions = new BoardActionsCollection();

            strSQL = "Select X,Y,SquareAction,ActionSequence,Phase,Parameter from BoardItemActions where BoardID=" + sourceID + ";";
            var actionTable = GetQueryResults(strSQL);
            foreach (DataRow actionRow in actionTable.Rows)
            {
                BoardAction oneAction = new BoardAction((SquareAction)actionRow["SquareAction"], Convert.ToInt32(actionRow["Parameter"]), Convert.ToInt32(actionRow["ActionSequence"]), Convert.ToInt32(actionRow["Phase"]));
                oneAction.SquareX = Convert.ToInt32(actionRow["X"]);
                oneAction.SquareY = Convert.ToInt32(actionRow["Y"]);
                squareActions.Add(oneAction);
            }


            l_BoardElements = new BoardElementCollection();
            strSQL = "Select X,Y,SquareType,Rotation from BoardItems where BoardID=" + sourceID + ";";
            var readerTable = GetQueryResults(strSQL);
            foreach (DataRow row in readerTable.Rows)
            {
                int boardX = Convert.ToInt32(row["X"]);
                int boardY = Convert.ToInt32(row["Y"]);
                if (boardX + 1 > l_BoardElements.BoardCols) l_BoardElements.BoardCols = boardX + 1;
                if (boardY + 1 > l_BoardElements.BoardRows) l_BoardElements.BoardRows = boardY + 1;

                BoardActionsCollection boardSquareActions = new BoardActionsCollection();

                foreach (BoardAction thisaction in squareActions.Where(sa => sa.SquareX == boardX && sa.SquareY == boardY))
                {
                    boardSquareActions.Add(thisaction);
                }

                l_BoardElements.SetSquare(boardX, boardY, (SquareType)row["SquareType"], (Direction)row["Rotation"], boardSquareActions);
            }
            return l_BoardElements;
        }

        // --- Legacy-style helpers (ported from Database.cs) ---
        // Provide backwards-compatible methods so existing code that used Database
        // can call similar APIs on DataService during the migration.


        public string GetHTMLfromQuery(string strSQL)
        {
            var dt = GetQueryResults(strSQL);
            var sb = new System.Text.StringBuilder();
            sb.Append("<table width='100%'>");
            // header row
            sb.Append("<tr>");
            foreach (DataColumn col in dt.Columns)
            {
                sb.Append("<td style='background-color:#cccccc;'>").Append(col.ColumnName).Append("</td>");
            }
            sb.Append("</tr>");
            // data rows
            foreach (DataRow row in dt.Rows)
            {
                sb.Append("<tr>");
                foreach (DataColumn col in dt.Columns)
                {
                    var val = row[col];
                    var sval = val == DBNull.Value ? "" : System.Net.WebUtility.HtmlEncode(val.ToString());
                    sb.Append("<td style='background-color:#eeeeee;'>").Append(sval).Append("</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</table>");
            return sb.ToString();
        }

        public string GetTableDataAsHTML(string readdata)
        {
            var tablesin = readdata.Split('/');
//            var newQuery = sout[sout.Length - 1];
            string output = "<html><head>";
            output += "<script src='/jscode.js' type='text/javascript' charset='utf-8'></script>";
            output += "</head><body>";
            foreach (var eachtable in tablesin)
            {
                var newQuery = "Select * from " + eachtable;
                output += GetHTMLfromQuery(newQuery);
            }
            output += "</body></html>";
            return output;
        }

        public void ResetGameState()
        {
            // retained for compatibility; original implementation was empty
            // but higher-level initialization should call appropriate procedures
        }


        /*

                select Robots.RobotID, 
        RobotBodies.`Name` as RobotName, 
        RobotBodies.Color as RobotColor, 
        RobotBodies.ColorFG as RobotColorFG,
        Robots.CurrentFlag, 
        RobotStatus.StatusColor as StatusColor,
        RobotStatus.LEDColor as LEDColor,
        RobotStatus.ShortDescription as PlayerStatus,
        Robots.Status as StatusID,
        CurrentPosCol as `X`,
        CurrentPosRow as `Y`,
        CurrentPosDir as Dir,
        ShortDirDesc as sDir,
        ArchivePosCol as `AX`,
        ArchivePosRow as `AY`,
        Robots.Score as Score,
        OperatorName,
        PositionValid,
        Priority,
        `ShutDown`,
        `Password`,
        PlayerSeat,
        Energy,
        Concat(CurrentFlag,"/",Energy) FlagEnergy,
        so.Direction as PlayerViewDirection,
        so.Direction as DirectionAdjustment,
        Robots.CardsDealt,
        Robots.CardsPlayed,
        if(isnull(ShowCardsPlayed) || RobotStatus.Active=0,RobotStatus.ShortDescription,ShowCardsPlayed) as StatusToShow,
        cl.Description msg


        from (Robots inner join RobotBodies on Robots.RobotBodyID = RobotBodies.RobotBodyID)
         inner join RobotStatus on if(Robots.IsConnected=1,Robots.`Status`,10) = RobotStatus.RobotStatusID
         inner join RobotDirections on Robots.CurrentPosDir = RobotDirections.DirID
         inner join SeatOrientation so on PlayerSeat = so.SeatID

         left join (
         #show cards played
        select Owner, 
        GROUP_CONCAT(if(isnull(mc.CardID),"-",if(mc.Executed,mct.ShortDescription,"X")) order by PhasePlayed ) ShowCardsPlayed
        from MoveCards mc inner join MoveCardTypes mct on mc.CardTypeID = mct.CardTypeID 
        where mc.PhasePlayed>0 group by owner order by Owner) played
        on Robots.RobotID = played.Owner
        left join CommandList cl on Robots.MessageCommandID = cl.CommandID
        */

        public Players GetAllPlayers(bool forceRefresh = false)
        {
            if (_allPlayers == null || forceRefresh)
            {
                var players = new Players();

                string strSQL = @"SELECT r.RobotID, rb.Name AS RobotName, rb.Color AS RobotColor, rb.ColorFG AS RobotColorFG,
                       r.OperatorName, r.Password, r.PlayerSeat, rbase.IPAddress,
                       so.Direction AS PlayerViewDirection
                FROM Robots r
                JOIN RobotBodies rb ON r.RobotBodyID = rb.RobotBodyID
                JOIN RobotDirections rd ON r.CurrentPosDir = rd.DirID
                JOIN SeatOrientation so ON r.PlayerSeat = so.SeatID
                JOIN RobotBases rbase ON r.RobotBaseID = rbase.RobotBaseID
                ORDER BY r.RobotID";

                var loadplayers = this.GetQueryResults(strSQL);
                foreach (DataRow row in loadplayers.Rows)
                {
                    players.Add(new Player()
                    {
                        ID                  = (int)row["RobotID"],
                        PlayerSeat          = (int)row["PlayerSeat"],
                        Name                = row["RobotName"].ToString() ?? "",
                        Color               = row["RobotColor"].ToString() ?? "FFFFFF",
                        ForeColor           = row["RobotColorFG"].ToString() ?? "000000",
                        Password            = row["Password"]?.ToString() ?? "",
                        IPAddress           = row["IPAddress"].ToString(),
                        PlayerViewDirection = Convert.ToInt32(row["PlayerViewDirection"]),
                        AllGameCards        = GameCards
                    });
                    //Console.WriteLine("Loaded player ID:" + row["RobotID"].ToString() + " Name:" + row["RobotName"].ToString() + " IP:" + IPAddress);
                }
                _allPlayers = players;

            }

            RefreshAllPlayers();
            
            return _allPlayers;
        }

        public void RefreshAllPlayers()
        {
            string strSQL = @"SELECT r.RobotID, r.CurrentFlag, rs.StatusColor, rs.LEDColor, rs.ShortDescription AS PlayerStatus,
                   r.Status AS StatusID, r.CurrentPosCol AS X, r.CurrentPosRow AS Y, r.CurrentPosDir AS Dir,
                   rd.ShortDirDesc AS sDir, r.ArchivePosCol AS AX, r.ArchivePosRow AS AY, r.Score,
                   r.PositionValid, r.Priority, r.ShutDown, r.Energy,
                   CONCAT(r.CurrentFlag,'/',r.Energy) AS FlagEnergy,
                   r.CardsDealt, r.CardsPlayed,
                   IF(played.ShowCardsPlayed IS NULL OR rs.Active = 0, rs.ShortDescription, played.ShowCardsPlayed) AS StatusToShow,
                   cl.Description AS msg
            FROM Robots r
            JOIN RobotStatus rs ON IF(r.IsConnected = 1, r.Status, 10) = rs.RobotStatusID
            JOIN RobotDirections rd ON r.CurrentPosDir = rd.DirID
            LEFT JOIN (
                SELECT mc.Owner,
                       GROUP_CONCAT(IF(mc.CardID IS NULL, '-', IF(mc.Executed, mct.ShortDescription, 'X'))
                                    ORDER BY mc.PhasePlayed) AS ShowCardsPlayed
                FROM MoveCards mc
                JOIN MoveCardTypes mct ON mc.CardTypeID = mct.CardTypeID
                WHERE mc.PhasePlayed > 0
                GROUP BY mc.Owner ORDER BY mc.Owner
            ) played ON r.RobotID = played.Owner
            LEFT JOIN CommandList cl ON r.MessageCommandID = cl.CommandID
            ORDER BY r.Priority";

            var loadplayers = this.GetQueryResults(strSQL);
            foreach (DataRow row in loadplayers.Rows)
            {
                var existingPlayer = _allPlayers?.FirstOrDefault(p => p.ID == (int)row["RobotID"]);
                if (existingPlayer != null)
                {
                    existingPlayer.LastFlag          = (int)row["CurrentFlag"];
                    existingPlayer.ShutDown          = (tShutDown)(int)row["ShutDown"];
                    existingPlayer.PlayerStatus      = (tPlayerStatus)(int)row["StatusID"];
                    existingPlayer.CurrentPos        = new RobotLocation((Direction)(int)row["Dir"], (int)row["X"], (int)row["Y"]);
                    existingPlayer.ArchivePosCol     = (int)row["AX"];
                    existingPlayer.ArchivePosRow     = (int)row["AY"];
                    existingPlayer.Priority          = (int)row["Priority"];
                    existingPlayer.Energy            = (int)row["Energy"];
                    existingPlayer.Score             = (int)row["Score"];
                    existingPlayer.PositionValid     = (int)row["PositionValid"] != 0;
                    existingPlayer.Active            = (int)row["StatusID"] != 10;
                    //existingPlayer.PlayerMsg         = row["msg"]?.ToString()          ?? "";
                };
            }

        }

        /// <summary>
        /// Reloads MoveCard state for a single player from the DB into in-memory GameCards.
        /// CardsDealtStr and CardsPlayedStr are computed from GameCards, so this keeps them fresh.
        /// </summary>
        public void RefreshPlayerCards(int robotID)
        {
            return;
            var dt = GetQueryResults(
                $"SELECT CardID, PhasePlayed, CardLocation, Executed FROM MoveCards WHERE Owner = {robotID};");
            foreach (DataRow row in dt.Rows)
            {
                var card = GameCards.FirstOrDefault(c => c.Owner == robotID && c.ID == (int)row["CardID"]);
                if (card == null) continue;
                card.PhasePlayed  = (int)row["PhasePlayed"];
                card.CardLocation = (int)row["CardLocation"];
                card.Executed     = (int)row["Executed"] == 1;
            }
        }

        public void LoadGameCardsFromDatabase()
        {
            GameCards.Clear();

            string strSQL = "Select CardID, CardTypeID, Owner, PhasePlayed, CardLocation, Executed from MoveCards;";
            var reader = GetQueryResults(strSQL);

            foreach (DataRow row in reader.Rows)
            {
                MoveCard newCard = new((int)row["CardID"], (int)row["CardTypeID"])
                {
                    Owner = (int)row["Owner"],
                    PhasePlayed = (int)row["PhasePlayed"],
                    CardLocation = (int)row["CardLocation"],
                    Executed = (int)row["Executed"] == 1
                };

                GameCards.Add(newCard);
            }
        }

        public void LoadOptionCardsFromDatabase()
        {
            OptionCards.Clear();
            string strSQL =
                "SELECT ro.RobotID, ro.OptionID, ro.DestroyWhenDamaged, ro.Quantity, ro.IsActive, " +
                "ro.PhasePlayed, ro.DataValue, o.Damage, o.Name, o.EditorType " +
                "FROM RobotOptions ro " +
                "JOIN Options o ON ro.OptionID = o.OptionID " +
                "WHERE o.Functional > 7 " +
                "ORDER BY o.Name;";
            var reader = GetQueryResults(strSQL);
            foreach (DataRow row in reader.Rows)
            {
                OptionCard newCard = new OptionCard()
                {
                    Owner = (int)row["RobotID"],
                    ID = (int)row["OptionID"],
                    DestroyWhenDamaged = ((int)row["DestroyWhenDamaged"] == 1),
                    Quantity = (int)row["Quantity"],
                    PhasePlayed = (int)row["PhasePlayed"],
                    DataValue = (int)row["DataValue"],
                    Damage = (int)row["Damage"],
                    Name = (string)row["Name"],
                    EditorType = (tOptionEditorType)row["EditorType"]
                };

                OptionCards.Add(newCard);

                if (!OptionCardNames.ContainsKey(newCard.ID))
                {
                    OptionCardNames.Add(newCard.ID, newCard.Name);
                }
            }

        }        

        public void ReloadAllData()
        {
            UpdateGameState(); // ensure C# state reflects any DB changes from UpdateGameState logic
            GetAllPlayers(forceRefresh: true);
            LoadGameCardsFromDatabase();
            LoadOptionCardsFromDatabase();
        }

        /// <summary>
        /// C# equivalent of procUpdateCardPlayed + procUpdateRobotCards.
        /// Phone/robot UI programming endpoint: places a card from hand into a register,
        /// or removes a card from a register back to hand.
        ///
        /// Parameters match the stored procedure signature:
        ///   p_Player      — RobotID of the player programming
        ///   p_CardTypeID  — CardTypeID to play (>0 = play that type; -1 = only clear the slot)
        ///   p_PhasePlayed — Register slot 1-5 to target (-1 = first empty slot)
        ///
        /// Behaviour:
        ///   1. Does nothing if the robot is not in a Programming-eligible status.
        ///   2. If p_PhasePlayed=-1, finds the lowest empty register (up to PhaseCount).
        ///      If none exists, clears both parameters and exits without moving any card.
        ///   3. If p_CardTypeID>0, selects the lowest CardID in the player's hand (CardLocation=1)
        ///      with that type.
        ///   4. Moves any card currently in the target slot back to hand (CardLocation=1).
        ///   5. Moves the selected card from hand to the target slot (CardLocation=2).
        ///   6. Sets robot Status=4 (Ready) if all PhaseCount slots are filled, else Status=3 (Programming).
        ///   7. Rebuilds Robots.CardsDealt and Robots.CardsPlayed CSV strings (procUpdateRobotCards).
        ///   8. Updates in-memory player state to match.
        /// </summary>
        public void UpdateCardPlayed(int p_Player, int p_CardTypeID, int p_PhasePlayed)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            Player? player = AllPlayers.GetPlayer(p => p.ID == p_Player);

            // 1. Check that the robot is in a programming-eligible status.
            int inProgramming;
            using (var cmd = new MySqlCommand(
                "SELECT rs.Programming " +
                "FROM Robots r " +
                "INNER JOIN RobotStatus rs ON r.`Status` = rs.RobotStatusID " +
                "WHERE r.RobotID = @player",
                connection))
            {
                cmd.Parameters.AddWithValue("@player", p_Player);
                var result = cmd.ExecuteScalar();
                inProgramming = (result == null || result == DBNull.Value) ? 0 : Convert.ToInt32(result);
            }

            if (inProgramming != 1) return;

            int vCardID = -1;
            int phaseCount = PhaseCount;

            // 2. If no target slot specified, find the first empty register.
            if (p_PhasePlayed == -1)
            {
                using var cmd = new MySqlCommand(
                    "SELECT MIN(pc.ID) " +
                    "FROM PhaseCounter pc " +
                    "LEFT JOIN MoveCards mc ON pc.ID = mc.PhasePlayed AND mc.`Owner` = @player " +
                    "WHERE mc.CardTypeID IS NULL",
                    connection);
                cmd.Parameters.AddWithValue("@player", p_Player);
                var result = cmd.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                {
                    // All slots are full — nothing to do.
                    return;
                }
                p_PhasePlayed = Convert.ToInt32(result);
                if (p_PhasePlayed > phaseCount)
                {
                    // No valid empty slot within the current phase count.
                    return;
                }
            }

            // 3. If a card type is requested, find the lowest CardID of that type in hand.
            if (p_CardTypeID > 0)
            {
                using var cmd = new MySqlCommand(
                    "SELECT MIN(CardID) FROM MoveCards " +
                    "WHERE `Owner` = @player AND CardLocation = 1 AND CardTypeID = @typeId",
                    connection);
                cmd.Parameters.AddWithValue("@player", p_Player);
                cmd.Parameters.AddWithValue("@typeId", p_CardTypeID);
                var result = cmd.ExecuteScalar();
                vCardID = (result == null || result == DBNull.Value) ? -1 : Convert.ToInt32(result);
            }

            // 4. Return any card already in the target slot back to hand.
            using (var cmd = new MySqlCommand(
                "UPDATE MoveCards SET PhasePlayed = -1, CardLocation = 1 " +
                "WHERE `Owner` = @player AND PhasePlayed = @phase AND CardLocation = 2",
                connection))
            {
                cmd.Parameters.AddWithValue("@player", p_Player);
                cmd.Parameters.AddWithValue("@phase", p_PhasePlayed);
                cmd.ExecuteNonQuery();
            }

            // 5. Move the selected card from hand into the target slot.
            if (vCardID >= 0)
            {
                using var cmd = new MySqlCommand(
                    "UPDATE MoveCards SET PhasePlayed = @phase, CardLocation = 2 " +
                    "WHERE `Owner` = @player AND CardID = @cardId AND CardLocation = 1",
                    connection);
                cmd.Parameters.AddWithValue("@phase", p_PhasePlayed);
                cmd.Parameters.AddWithValue("@player", p_Player);
                cmd.Parameters.AddWithValue("@cardId", vCardID);
                cmd.ExecuteNonQuery();
            }

            // 6. Determine new robot status: 4=Ready if all registers filled, else 3=Programming.
            int programCount;
            using (var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM MoveCards WHERE `Owner` = @player AND CardLocation = 2",
                connection))
            {
                cmd.Parameters.AddWithValue("@player", p_Player);
                programCount = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }
            int newStatus = (programCount == phaseCount) ? 4 : 3;

            // 7. Rebuild CardsDealt and CardsPlayed CSV strings (procUpdateRobotCards).
            string? cardsDealt;
            using (var cmd = new MySqlCommand(
                "SELECT GROUP_CONCAT(CardTypeID ORDER BY CardTypeID DESC) " +
                "FROM MoveCards WHERE CardLocation = 1 AND `Owner` = @player " +
                "GROUP BY `Owner`",
                connection))
            {
                cmd.Parameters.AddWithValue("@player", p_Player);
                var result = cmd.ExecuteScalar();
                cardsDealt = (result == null || result == DBNull.Value) ? null : result.ToString();
            }

            string? cardsPlayed;
            using (var cmd = new MySqlCommand(
                "SELECT GROUP_CONCAT(IFNULL(mc.CardTypeID, 0) ORDER BY pc.ID) " +
                "FROM PhaseCounter pc " +
                "LEFT JOIN MoveCards mc ON pc.ID = mc.PhasePlayed AND mc.`Owner` = @player",
                connection))
            {
                cmd.Parameters.AddWithValue("@player", p_Player);
                var result = cmd.ExecuteScalar();
                cardsPlayed = (result == null || result == DBNull.Value) ? null : result.ToString();
            }

            using (var cmd = new MySqlCommand(
                "UPDATE Robots SET CardsDealt = @dealt, CardsPlayed = @played WHERE RobotID = @player",
                connection))
            {
                cmd.Parameters.AddWithValue("@dealt", (object?)cardsDealt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@played", (object?)cardsPlayed ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@player", p_Player);
                cmd.ExecuteNonQuery();
            }

            // Update robot Status.
            using (var cmd = new MySqlCommand(
                "UPDATE Robots SET `Status` = @status WHERE RobotID = @player",
                connection))
            {
                cmd.Parameters.AddWithValue("@status", newStatus);
                cmd.Parameters.AddWithValue("@player", p_Player);
                cmd.ExecuteNonQuery();
            }

            player.PlayerStatus = (tPlayerStatus)newStatus;

            // 8. Sync in-memory GameCards to match the DB moves above.
            var returnedCard = GameCards.FirstOrDefault(c => c.Owner == p_Player && c.PhasePlayed == p_PhasePlayed && c.CardLocation == 2);
            if (returnedCard != null)
            {
                returnedCard.PhasePlayed = -1;
                returnedCard.CardLocation = 1;
            }
            if (vCardID >= 0)
            {
                var movedCard = GameCards.FirstOrDefault(c => c.Owner == p_Player && c.ID == vCardID && c.CardLocation == 1);
                if (movedCard != null)
                {
                    movedCard.PhasePlayed = p_PhasePlayed;
                    movedCard.CardLocation = 2;
                }
            }
        }

        public int UpdateGameState()
        {
            // Query current game data
            string strSQL = "Select iKey, sKey, iValue, sValue from CurrentGameData;";
            var dt = GetQueryResults(strSQL);
            if (dt != null && dt.Rows.Count > 0)
            {
                foreach (System.Data.DataRow row in dt.Rows)
                {
                    var key = Convert.ToInt32(row[0]);
                    var value = Convert.ToInt32(row[2]);
//                    Console.WriteLine("GameState Key:" + key.ToString() + " Value:" + value.ToString());
                    switch (key)
                    {
                        case 1: GameType = (GameTypes)value; break;
                        case 2: Turn = value; break;
                        case 3: Phase = value; break;
                        case 6: LaserDamage = value; break;
                        // Backing field, not the property: this is a read from CurrentGameData,
                        // so writing back through the setter would be a redundant round trip.
                        case 7: _totalFlags = value; break;
                        case 8: RobotsActive = value; break;
                        case 10: _gameState = value; break;
                        case 16: PhaseCount = value; break;
                        case 20:
                            BoardID = value;
                            if (row[3] != System.DBNull.Value) BoardFileName = row[3].ToString() ?? "";
                            break;
                        case 22: OptionsOnStartup = value; break;
                    }
                }
            }
            return GameState;
        }

        ///////////////////////////////////////////////////////////////////////////
        // Datagrid editor API methods
        ///////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Get list of all tables in the database
        /// </summary>
        public List<string> GetTableList()
        {
            var tableNames = new List<string>();
            string strSQL = $"SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = '{DatabaseName}' ORDER BY TABLE_NAME;";
            
            var dt = GetQueryResults(strSQL);
            foreach (DataRow row in dt.Rows)
            {
                var name = row[0]?.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    tableNames.Add(name);
                }
            }
            
            return tableNames;
        }

        /// <summary>
        /// Get table data as JSON with columns and rows
        /// </summary>
        public string GetTableDataAsJson(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("Table name cannot be empty", nameof(tableName));

            // Validate table name to prevent SQL injection
            if (!IsValidTableName(tableName))
                throw new ArgumentException($"Invalid table name: {tableName}", nameof(tableName));

            var dt = GetQueryResults($"SELECT * FROM `{tableName}` LIMIT 1000;");
            var rows = new List<Dictionary<string, object>>();
            var columns = new List<string>();

            // Get column names
            foreach (DataColumn col in dt.Columns)
            {
                columns.Add(col.ColumnName);
            }

            // Convert rows to dictionaries
            foreach (DataRow row in dt.Rows)
            {
                var rowDict = new Dictionary<string, object>();
                foreach (DataColumn col in dt.Columns)
                {
                    rowDict[col.ColumnName] = row[col] ?? DBNull.Value;
                }
                rows.Add(rowDict);
            }

            var result = new { columns, rows };
            return JsonConvert.SerializeObject(result);
        }

        /// <summary>
        /// Save table data from JSON format
        /// </summary>
        public object SaveTableData(string tableName, string jsonData)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("Table name cannot be empty", nameof(tableName));

            if (!IsValidTableName(tableName))
                throw new ArgumentException($"Invalid table name: {tableName}", nameof(tableName));

            try
            {
                var data = JsonConvert.DeserializeObject<dynamic>(jsonData);
                
                if (data == null)
                    throw new ArgumentException("Invalid JSON format.");

                var rows = data["rows"];
                if (rows == null)
                    throw new ArgumentException("Invalid JSON format. Expected 'rows' array.");

                // For this simple implementation, we'll just return a success message
                // A full implementation would track changes, perform updates, inserts, deletes
                var rowCount = ((Newtonsoft.Json.Linq.JArray)rows).Count;
                // find table key
                // for each row
                // find the record with the key
                // if none, add record
                // else
                // update values listed
                
                return new 
                { 
                    success = true, 
                    message = $"Data received for table '{tableName}' with {rowCount} rows. (Full save not yet implemented)", 
                    rowCount 
                };
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Invalid JSON format: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Validate table name to prevent SQL injection
        /// </summary>
        private bool IsValidTableName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return false;

            // Only allow alphanumeric characters and underscores
            return System.Text.RegularExpressions.Regex.IsMatch(tableName, @"^[a-zA-Z0-9_]+$");
        }

        /// <summary>
        /// Stores a planned turn's commands, replacing whatever was there for that turn.
        /// Master's job, not the planner's: CommandList is a Master-owned table.
        ///
        /// One DbContext and one SaveChanges for the whole batch. The previous version
        /// created a DbContext inside the per-command loop and saved each command
        /// individually -- roughly 130 contexts and 130 round trips per turn.
        /// </summary>
        public int PersistCommands(CommandList commands, int turn)
        {
            using var ctx = CreateDbContext();
            using var transaction = ctx.Database.BeginTransaction();

            // Delete and insert in ONE transaction. The turn's commands are the durable
            // record used to resume after a restart, so a half-written CommandList is worse
            // than no change at all: replacing the rows must be all-or-nothing.
            ctx.Database.ExecuteSqlRaw(
                "DELETE FROM CommandList WHERE Turn = {0} AND Phase > 0;", turn);

            foreach (var command in commands)
            {
                command.Turn = turn;
                ctx.CommandItems.Add(command);
            }
            ctx.SaveChanges();

            transaction.Commit();
            return commands.Count;
        }

        public int ProcessDbCommand(int p_CommandID, int p_NewStatus)
        {
            var command = ListOfCommands.FirstOrDefault(c => c.CommandID == p_CommandID);
            if (command == null)
                return -1; // or throw an exception if preferred

            var statusid = ProcessDbCommand(command, p_NewStatus);
            command.StatusID = statusid; // update in-memory status to match DB changes
            return statusid;
        }
        /// <summary>
        /// C# equivalent of funcProcessCommand(p_CommandID, p_NewStatus).
        /// Loads the command row, performs any DB-side effect based on CommandTypeID,
        /// applies position updates when p_NewStatus==5, then writes the final StatusID
        /// back to CommandList. Returns the resulting StatusID.
        /// Pass p_NewStatus=-1 to auto-complete (equivalent to SQL default).
        /// </summary>
        public int ProcessDbCommand(CommandItem p_Command, int p_NewStatus)
        {
            //int cType       = (int)p_Command.CommandType;
            int cRobotID    = p_Command.RobotID;
            int cParameter  = p_Command.Value;
            int cParameterB = p_Command.ValueB;
            int cRow        = p_Command.PositionRow;
            int cCol        = p_Command.PositionCol;
            int cDir        = p_Command.PositionDir;
            // Resolve explicitly rather than relying on CommandItem.Robot having been
            // attached by whoever loaded this command. PlayerState, not Player: this method
            // only ever reads and writes game state (damage, flags, position, status), never
            // the robot transport.
            PlayerState? robot = p_Command.Robot ?? AllPlayers.GetPlayer(p => p.ID == cRobotID);

            if (p_NewStatus == -1)
                p_NewStatus = 6; // command complete

            using var db = CreateDbContext();

            // Process side-effects by CommandTypeID
            switch (p_Command.CommandType)
            {
                case SquareAction.PlayerLocation: // Player Location — handled below in the status==5 block
                    p_NewStatus = 5;
                    break;

                case SquareAction.Damage: // Set Damage
                    if (robot != null)
                        robot.Damage = cParameter;
                    db.Robots.Where(r => r.ID == cRobotID)
                        .ExecuteUpdate(s => s.SetProperty(r => r.Damage, cParameter));
                    break;

                case SquareAction.Archive: // Set Archive position
                    if (robot != null)
                        robot.ArchivePos = new RobotLocation((Direction)cDir, cCol, cRow);
                    db.Robots.Where(r => r.ID == cRobotID)
                        .ExecuteUpdate(s => s
                            .SetProperty(r => r.ArchivePosRow, cRow)
                            .SetProperty(r => r.ArchivePosCol, cCol)
                            .SetProperty(r => r.ArchivePosDir, cDir));
                    break;

                case SquareAction.Flag: // Set Current Flag
                    if (robot != null)
                        robot.LastFlag = cParameter;
                    db.Robots.Where(r => r.ID == cRobotID)
                        .ExecuteUpdate(s => s.SetProperty(r => r.LastFlag, cParameter));
                    break;

                case SquareAction.Option: // Deal option card to robot
                {
                    int optionID = cParameter;
                    if (optionID == 0)
                        optionID = GetNextOption(cRobotID);
                    ExecuteSQL(
                        $"INSERT INTO RobotOptions (RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue) " +
                        $"SELECT {cRobotID}, OptionID, false, Quantity, false, 0, 0 " +
                        $"FROM `Options` WHERE OptionID = {optionID}");
                    break;
                }

                case SquareAction.LostLife: // Set Lives
                    if (robot != null) robot.Lives = cParameter;
                    db.Robots.Where(r => r.ID == cRobotID)
                        .ExecuteUpdate(s => s.SetProperty(r => r.Lives, cParameter));
                    break;

                case SquareAction.DealCard: // Deal card to player (assign card owner)
                    ExecuteSQL(
                        $"UPDATE MoveCards SET Owner = {cRobotID} WHERE CardID = {cParameter}");
                    break;

                case SquareAction.GameWinner: // Game Winner
                    GameState = 11;
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET iValue = {cRobotID} WHERE iKey = 13");
                    break;

                case SquareAction.Card: // Mark card as executed
                    ExecuteSQL(
                        $"UPDATE MoveCards SET Executed = 1 WHERE CardID = {cParameter} AND Owner = {cRobotID}");
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET sValue = 'Played Card' WHERE iKey = 21");
                    break;

                case SquareAction.SetPlayerStatus: // Set robot Status
                    if (robot != null) robot.PlayerStatus = (tPlayerStatus)cParameter;
                    db.Robots.Where(r => r.ID == cRobotID)
                        .ExecuteUpdate(s => s.SetProperty(r => r.PlayerStatus, (tPlayerStatus)cParameter));
                    break;

                case SquareAction.DeathPoints: // Set DamagePoints — column does not exist in schema; log and skip
                    Console.WriteLine($"ProcessDbCommand: DeathPoints is not supported — skipped.");
                    break;

                case SquareAction.DealOptionCard: // no-op in original SQL
                    break;

                case SquareAction.DestroyOptionCard: // Delete Option from player
                    ExecuteSQL(
                        $"DELETE FROM RobotOptions WHERE RobotID = {cRobotID} AND OptionID = {cParameter}");
                    break;

                case SquareAction.OptionCountSet: // Set option quantity
                    ExecuteSQL(
                        $"UPDATE RobotOptions SET Quantity = {cParameterB} WHERE RobotID = {cRobotID} AND OptionID = {cParameter}");
                    break;

                case SquareAction.SetDamagePointTotal: // Set MaxDamage
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET iValue = {cParameter} WHERE iKey = 17");
                    break;

                case SquareAction.DealSpamCard: // Deal Spam card to player
                    DealSpamToPlayer(cRobotID);
                    break;

                case SquareAction.SetShutDownMode: // Set ShutDown
                    if (robot != null) robot.ShutDown = (tShutDown)cParameter;
                    db.Robots.Where(r => r.ID == cRobotID)
                        .ExecuteUpdate(s => s.SetProperty(r => r.ShutDown, (tShutDown)cParameter));
                    break;

                case SquareAction.SetCurrentGameData: // Set CurrentGameData iValue by iKey
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET iValue = {cParameterB} WHERE iKey = {cParameter}");
                    break;

                case SquareAction.EndOfGame: // End of game
                    GameState = 12;
                    break;

                case SquareAction.DeleteRobot: // Delete robot
                    db.Robots.Where(r => r.ID == cRobotID).ExecuteDelete();
                    break;

                case SquareAction.SetGameState: // Set GameState
                    GameState = cParameter;
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET iValue = {cRobotID} WHERE iKey = 13");
                    break;

                // The following are no-ops in the SQL original
                case SquareAction.BlockDirection:
                case SquareAction.RobotPush:
                case SquareAction.PhaseStart:
                case SquareAction.PlayOptionCard:
                case SquareAction.BeginBoardEffects:
                case SquareAction.Water:
                case SquareAction.DeletedMove:
                case SquareAction.FireCannon:
                    break;
                case SquareAction.SetButtonText:
//                            onecommand.StatusID = _dataService.ProcessDbCommand(onecommand, -1);
//                            Db.SaveChanges();
                    if (robot != null) robot.PlayerMsg = "";
                    db.Robots.Where(r => r.ID == cRobotID)
                        .ExecuteUpdate(s => s.SetProperty(r => r.PlayerMsg, ""));
                    break;

                case SquareAction.SetEnergy:
                    if (robot != null) robot.Energy = cParameter;
                    db.Robots.Where(r => r.ID == cRobotID)
                        .ExecuteUpdate(s => s.SetProperty(r => r.Energy, cParameter));
                    break;

                default:
                    // Unknown type — no side-effect, fall through to status update
                    break;
            }

            // Status 5 means "move complete — update robot position then mark done"
            if (p_NewStatus == 5)
            {
                if (cCol >= 0 && cRow >= 0)
                {
                    if (robot != null)
                    {
                        robot.CurrentPos.X = cCol;
                        robot.CurrentPos.Y = cRow;
                        robot.CurrentPos.Direction = (Direction)cDir;
                        robot.Score = cParameterB;
                    }
                    db.Robots.Where(r => r.ID == cRobotID)
                        .ExecuteUpdate(s => s
                            .SetProperty(r => r.CurrentPosRow, cRow)
                            .SetProperty(r => r.CurrentPosCol, cCol)
                            .SetProperty(r => r.CurrentPosDir, cDir)
                            .SetProperty(r => r.Score, cParameterB));
                }
                p_NewStatus = 6; // command complete
            }

            // Write final status back to CommandList
            if (p_Command.CommandID > 0)
                ExecuteSQL($"UPDATE CommandList SET StatusID = {p_NewStatus} WHERE CommandID = {p_Command.CommandID}");

            return p_NewStatus;
        }

        /// <summary>
        /// C# equivalent of funcDealSpamToPlayer.
        /// Inserts a new Spam card (CardTypeID=10) into the robot's discard pile.
        /// Returns the new CardID.
        /// </summary>
        public int DealSpamToPlayer(int robotID)
        {
            int maxId = GetIntFromDB(
                $"SELECT COALESCE(MAX(CardID), 0) + 1 FROM MoveCards WHERE Owner = {robotID}");
            ExecuteSQL(
                $"INSERT INTO MoveCards (CardID, CardTypeID, Owner, CardLocation) " +
                $"VALUES ({maxId}, 10, {robotID}, 3)");
            return maxId;
        }

        /// <summary>
        /// C# equivalent of funcGetNextOption.
        /// Returns the next available OptionID for a robot (not already owned, Functional > 7),
        /// ordered by CurrentOrder. Advances the shuffle pointer by adding 100 to CurrentOrder.
        /// </summary>
        public int GetNextOption(int robotID)
        {
            int optionID = GetIntFromDB(
                $"SELECT o.OptionID FROM `Options` o " +
                $"LEFT JOIN (SELECT OptionID FROM RobotOptions WHERE RobotID = {robotID}) AS ro " +
                $"ON o.OptionID = ro.OptionID " +
                $"WHERE ro.OptionID IS NULL AND o.Functional > 7 " +
                $"ORDER BY o.CurrentOrder LIMIT 1");
            if (optionID > 0)
            {
                ExecuteSQL(
                    $"UPDATE `Options` SET CurrentOrder = CurrentOrder + 100 WHERE OptionID = {optionID}");
            }
            return optionID;
        }

        /// <summary>
        /// C# equivalent of procMoveCardsShuffleAndDeal.
        /// Shuffles and deals move cards to each active player at the start of a turn.
        /// Behaviour varies by PhaseCount:
        ///   PhaseCount=1 — single-phase (10-Turn) mode: rotate priorities then assign
        ///                  cards to players by priority slot.
        ///   otherwise    — Renegade rules: discard played Spam cards, move hand/played
        ///                  cards to discard, shuffle with DealPriority weighting, refill
        ///                  deck from discard if a player has fewer than 9 cards, deal 9
        ///                  cards and update Robots.CardsDealt / CardsPlayed strings.
        /// </summary>
        public void MoveCardsShuffleAndDeal()
        {
            int phaseCount = GetIntFromDB(
                "SELECT iValue FROM CurrentGameData WHERE sKey = 'PhaseCount'");

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            // Unlock all non-locked cards; reset Executed and Random flags.
            // If no rows exist (empty deck) call procGameNewAddCards to create the deck.
            using (var cmd = new MySqlCommand(
                "UPDATE MoveCards SET `Locked` = IF(CardLocation = 4, 1, 0), Executed = 0, Random = 0",
                connection))
            {
                int affected = cmd.ExecuteNonQuery();
                if (affected == 0)
                {
                    // No cards exist yet — create the deck.
                    GameNewAddCards();
                }
            }

            if (phaseCount == 1)
            {
                // Single-phase (10-Turn) mode.
                // Rotate player priorities, then assign cards to each robot by their
                // priority slot (10 - floor((CardID-1)/7)).
                UpdatePlayerPriority(connection);

                using (var cmd = new MySqlCommand(
                    "UPDATE MoveCards SET PhasePlayed = -1",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new MySqlCommand(
                    "UPDATE MoveCards, Robots SET Owner = RobotID " +
                    "WHERE 10 - FLOOR((CardID - 1) / 7) = Priority",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }
            }
            else
            {
                // Renegade rules.

                // 1. Discard played Spam cards (CardLocation=2 with CardTypeID=10).
                using (var cmd = new MySqlCommand(
                    "DELETE FROM MoveCards WHERE CardLocation = 5 AND CardTypeID = 10",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // 2. Move all remaining hand (1) and played (2) cards to discard (3).
                using (var cmd = new MySqlCommand(
                    "UPDATE MoveCards SET CardLocation = 3, PhasePlayed = 0 " +
                    "WHERE CardLocation = 1 OR CardLocation = 2",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // 3. Shuffle: assign a random value weighted by DealPriority, reset CurrentOrder.
                using (var cmd = new MySqlCommand(
                    "UPDATE MoveCards mc " +
                    "INNER JOIN MoveCardLocations mcl ON mc.CardLocation = mcl.LocationID " +
                    "SET mc.Random = ROUND(500.0 * RAND()) + mcl.DealPriority * 500, mc.CurrentOrder = 0",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // 4. Rank cards within each owner by their Random value using the
                //    self-join count pattern, storing rank in CurrentOrder.
                using (var cmd = new MySqlCommand(
                    "UPDATE MoveCards m1 " +
                    "INNER JOIN (" +
                    "  SELECT mc.CardID, mc.Owner, COUNT(mc.CardID) AS cnt, mc.CardLocation " +
                    "  FROM MoveCards mc " +
                    "  INNER JOIN MoveCards mc2 " +
                    "    ON mc.Owner = mc2.Owner " +
                    "    AND (mc.Random > mc2.Random OR (mc.Random = mc2.Random AND mc.CardID >= mc2.CardID)) " +
                    "  GROUP BY mc.CardID, mc.Owner, mc.CardLocation " +
                    "  ORDER BY mc.Owner, cnt" +
                    ") ij ON m1.Owner = ij.Owner AND m1.CardID = ij.CardID " +
                    "SET m1.CurrentOrder = ij.cnt",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // 5. If any player has fewer than 9 cards in their deck (CardLocation=0),
                //    move their discards (CardLocation=3) back into their deck.
                using (var cmd = new MySqlCommand(
                    "UPDATE MoveCards m0 " +
                    "INNER JOIN (" +
                    "  SELECT Owner FROM MoveCards WHERE CardLocation = 0 " +
                    "  GROUP BY Owner HAVING COUNT(CardID) < 9" +
                    ") lt9 ON m0.Owner = lt9.Owner " +
                    "SET CardLocation = 0 " +
                    "WHERE CardLocation = 3",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // 6. Deal 9 cards per player: promote the 9 lowest-ordered cards to hand.
                using (var cmd = new MySqlCommand(
                    "UPDATE MoveCards SET CardLocation = 1 WHERE CurrentOrder <= 9",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // 7. Rebuild Robots.CardsDealt (sorted by CardTypeID desc) and
                //    reset CardsPlayed to "0,0,0,0,0".
                using (var cmd = new MySqlCommand(
                    "UPDATE Robots rb " +
                    "INNER JOIN (" +
                    "  SELECT mc.Owner, GROUP_CONCAT(mc.CardTypeID ORDER BY mc.CardTypeID DESC) AS gctl " +
                    "  FROM MoveCards mc " +
                    "  WHERE mc.CardLocation = 1 " +
                    "  GROUP BY mc.Owner" +
                    ") ctl ON rb.RobotID = ctl.Owner " +
                    "SET CardsDealt = ctl.gctl, CardsPlayed = '0,0,0,0,0'",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }

                UpdatePlayerPriority(connection);
            }
        }

        /// <summary>
        /// C# equivalent of funcGetNextCard.
        /// Draws the next card from the player's deck for use when a Spam card is played.
        /// Marks the previously-played spam card as CardLocation=5 (Played Spam).
        /// If the player has no cards in the deck (CardLocation=0), shuffles the discard
        /// pile (CardLocation=3) back into the deck before drawing.
        /// Returns the CardID of the drawn card, or 0 if none is available.
        /// </summary>
        public int GetNextCard(int player, int usedSpamCardID)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            // Mark the used spam card as Played Spam (CardLocation=5)
            using (var cmd = new MySqlCommand(
                "UPDATE MoveCards SET CardLocation = 5 WHERE Owner = @player AND CardID = @usedSpam",
                connection))
            {
                cmd.Parameters.AddWithValue("@player", player);
                cmd.Parameters.AddWithValue("@usedSpam", usedSpamCardID);
                cmd.ExecuteNonQuery();
            }

            // Try to get the first card in deck (0) or discard (3), ordered by CurrentOrder
            int cCardID = 0;
            int cCardLoc = -1;
            using (var cmd = new MySqlCommand(
                "SELECT CardID, CardLocation FROM MoveCards " +
                "WHERE Owner = @player AND (CardLocation = 0 OR CardLocation = 3) " +
                "ORDER BY CurrentOrder LIMIT 1",
                connection))
            {
                cmd.Parameters.AddWithValue("@player", player);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    cCardID = reader.GetInt32(0);
                    cCardLoc = reader.GetInt32(1);
                }
            }

            if (cCardID == 0)
            {
                // No cards available at all
                return 0;
            }

            // If the top card was not already in the deck, reshuffle discards into deck
            if (cCardLoc != 0)
            {
                // Move all discards back to deck
                using (var cmd = new MySqlCommand(
                    "UPDATE MoveCards SET CardLocation = 0 WHERE CardLocation = 3",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // Assign random order weighted by DealPriority
                using (var cmd = new MySqlCommand(
                    "UPDATE MoveCards mc " +
                    "INNER JOIN MoveCardLocations mcl ON mc.CardLocation = mcl.LocationID " +
                    "SET mc.Random = ROUND(500.0 * RAND()) + mcl.DealPriority * 500, mc.CurrentOrder = 0",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // Rank cards by Random within each owner (self-join count pattern)
                using (var cmd = new MySqlCommand(
                    "UPDATE MoveCards m1 " +
                    "INNER JOIN (" +
                    "  SELECT mc.CardID, mc.Owner, COUNT(mc.CardID) AS cnt " +
                    "  FROM MoveCards mc " +
                    "  INNER JOIN MoveCards mc2 ON mc.Owner = mc2.Owner AND mc.Random >= mc2.Random " +
                    "  GROUP BY mc.CardID, mc.Owner, mc.CardLocation " +
                    "  ORDER BY mc.Owner, cnt" +
                    ") ij ON m1.Owner = ij.Owner AND m1.CardID = ij.CardID " +
                    "SET m1.CurrentOrder = ij.cnt",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // Re-fetch top card now that deck is rebuilt
                cCardID = 0;
                using (var cmd = new MySqlCommand(
                    "SELECT CardID FROM MoveCards " +
                    "WHERE Owner = @player AND CardLocation = 0 " +
                    "ORDER BY CurrentOrder LIMIT 1",
                    connection))
                {
                    cmd.Parameters.AddWithValue("@player", player);
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        cCardID = reader.GetInt32(0);
                    }
                }
            }

            if (cCardID == 0)
            {
                return 0;
            }

            // Mark the drawn card as Hand (CardLocation=1)
            using (var cmd = new MySqlCommand(
                "UPDATE MoveCards SET CardLocation = 1 WHERE Owner = @player AND CardID = @cardID",
                connection))
            {
                cmd.Parameters.AddWithValue("@player", player);
                cmd.Parameters.AddWithValue("@cardID", cCardID);
                cmd.ExecuteNonQuery();
            }

            return cCardID;
        }

        /// <summary>
        /// C# equivalent of procUpdatePlayerPriority.
        /// Rotates robot turn-order priorities round-robin for 10-Turn (single-phase) mode:
        ///   1. Decrement every robot's Priority by 1.
        ///   2. Count the total number of robots.
        ///   3. The robot whose Priority wrapped to 0 is assigned the highest priority
        ///      (robotCount), so turn order cycles through all players evenly.
        /// Accepts an optional open connection so it can be called within the same
        /// connection context as MoveCardsShuffleAndDeal without opening a second one.
        /// </summary>
        public void UpdatePlayerPriority(MySqlConnection? connection = null, int change = -1)
        {
            bool ownConnection = connection == null;
            if (ownConnection)
            {
                connection = new MySqlConnection(_connectionString);
                connection.Open();
            }

            try
            {
                // Step 1: Count robots first so the mod range is known.
                int robotCount;
                using (var cmd = new MySqlCommand("SELECT COUNT(RobotID) FROM Robots", connection!))
                {
                    var result = cmd.ExecuteScalar();
                    robotCount = result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
                }

                if (robotCount == 0) return;

                // Step 2: Add change and wrap with mod (priorities are 1-based: 1..robotCount).
                // Double-mod keeps the result positive for any change value.
                using var update = new MySqlCommand(
                    "UPDATE Robots SET Priority = MOD(MOD(Priority - 1 + @change, @n) + @n, @n) + 1",
                    connection!);
                update.Parameters.AddWithValue("@change", change);
                update.Parameters.AddWithValue("@n", robotCount);
                update.ExecuteNonQuery();
            }
            finally
            {
                if (ownConnection)
                    connection!.Dispose();
            }
        }

        // =====================================================================
        // procGameNewAddCards — C# equivalent
        // Creates the MoveCards deck for a new game. Renegade always uses deck set 4,
        // with each robot getting its own copy; player count and PhaseCount no longer
        // select a set (they only did so under the removed Classic rules).
        // =====================================================================
        public void GameNewAddCards()
        {
            ExecuteSQL("DELETE FROM MoveCards");

            // Each robot gets its own copy of the deck (Owner = RobotID)
            ExecuteSQL(
                "INSERT INTO MoveCards (CardID, CardTypeID, `Owner`, CardLocation) " +
                "SELECT CardID, CardTypeID, Robots.RobotID, 0 " +
                "FROM MoveCardsCompleteList, Robots WHERE SetID = 4");
        }

        // =====================================================================
        // procDealOptionToRobot — C# equivalent
        // Deals the next option from the shuffled Options deck to a robot.
        // =====================================================================
        public void DealOptionToRobot(int robotID)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            // Find first option not already owned by this robot, Functional > 7
            int optionID = 0;
            using (var cmd = new MySqlCommand(
                "SELECT o.OptionID FROM `Options` o " +
                "LEFT JOIN (SELECT OptionID FROM RobotOptions WHERE RobotID = @robot) AS ro " +
                "ON o.OptionID = ro.OptionID " +
                "WHERE ro.OptionID IS NULL AND o.Functional > 7 " +
                "ORDER BY o.CurrentOrder LIMIT 1",
                connection))
            {
                cmd.Parameters.AddWithValue("@robot", robotID);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value)
                    optionID = Convert.ToInt32(res);
            }

            if (optionID > 0)
            {
                // Insert option using column values from Options table
                using var cmd = new MySqlCommand(
                    "INSERT INTO RobotOptions (RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue) " +
                    "SELECT @robot, OptionID, false, Quantity, false, 0, IF(EditorType=6, 1, 0) " +
                    "FROM `Options` WHERE OptionID = @opt",
                    connection);
                cmd.Parameters.AddWithValue("@robot", robotID);
                cmd.Parameters.AddWithValue("@opt", optionID);
                cmd.ExecuteNonQuery();
            }
            else
            {
                // No unique option left — find any option with Quantity > -1 and add quantity
                int fallbackID = 0;
                int fallbackQty = 0;
                using (var cmd = new MySqlCommand(
                    "SELECT OptionID, Quantity FROM `Options` " +
                    "WHERE Quantity > -1 AND Functional > 7 ORDER BY CurrentOrder LIMIT 1",
                    connection))
                {
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        fallbackID  = reader.GetInt32(0);
                        fallbackQty = reader.GetInt32(1);
                    }
                }

                if (fallbackID > 0)
                {
                    if (fallbackQty > 0)
                    {
                        using var cmd = new MySqlCommand(
                            "UPDATE RobotOptions SET Quantity = Quantity + @qty " +
                            "WHERE RobotID = @robot AND OptionID = @opt",
                            connection);
                        cmd.Parameters.AddWithValue("@qty", fallbackQty);
                        cmd.Parameters.AddWithValue("@robot", robotID);
                        cmd.Parameters.AddWithValue("@opt", fallbackID);
                        cmd.ExecuteNonQuery();
                    }
                    else
                    {
                        using var cmd = new MySqlCommand(
                            "INSERT INTO RobotOptions (RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue) " +
                            "SELECT @robot, OptionID, false, Quantity, false, 0, IF(EditorType=6, 1, 0) " +
                            "FROM `Options` WHERE OptionID = @opt",
                            connection);
                        cmd.Parameters.AddWithValue("@robot", robotID);
                        cmd.Parameters.AddWithValue("@opt", fallbackID);
                        cmd.ExecuteNonQuery();
                    }
                    optionID = fallbackID;
                }
            }

            if (optionID > 0)
            {
                using var cmd = new MySqlCommand(
                    "UPDATE `Options` SET CurrentOrder = CurrentOrder + 100 WHERE OptionID = @opt",
                    connection);
                cmd.Parameters.AddWithValue("@opt", optionID);
                cmd.ExecuteNonQuery();
            }
        }

        // =====================================================================
        // procSetStatus — C# equivalent
        // Syncs StatusLEDs from Robots joined to RobotStatus (LEDColor).
        // Also applies the trigger logic for StatusLEDs_BEFORE_UPDATE:
        //   converts the hex Color string to R/G/B integers in the same UPDATE.
        // =====================================================================
        public void SetStatus()
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            // Step 1: set Color from LEDColor via Robots+RobotStatus join
            using (var cmd = new MySqlCommand(
                "UPDATE StatusLEDs " +
                "INNER JOIN Robots vr ON StatusLEDs.LEDID = vr.RobotID " +
                "INNER JOIN RobotStatus rs ON IF(vr.IsConnected=1, vr.Status, 10) = rs.RobotStatusID " +
                "SET StatusLEDs.Color = rs.LEDColor, " +
                "    StatusLEDs.R = CONV(SUBSTRING(rs.LEDColor,1,2),16,10), " +
                "    StatusLEDs.G = CONV(SUBSTRING(rs.LEDColor,3,2),16,10), " +
                "    StatusLEDs.B = CONV(SUBSTRING(rs.LEDColor,5,2),16,10)",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Step 2: override with red for robots with invalid position
            using (var cmd = new MySqlCommand(
                "UPDATE StatusLEDs " +
                "INNER JOIN Robots vr ON StatusLEDs.LEDID = vr.RobotID " +
                "SET StatusLEDs.Color = 'FF0000', " +
                "    StatusLEDs.R = 255, StatusLEDs.G = 0, StatusLEDs.B = 0 " +
                "WHERE vr.PositionValid = 0",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Step 3: override with orange for robots with an active BT-connect command (CommandTypeID=70, StatusID=7)
            using (var cmd = new MySqlCommand(
                "UPDATE StatusLEDs " +
                "INNER JOIN CommandList cl ON StatusLEDs.LEDID = cl.RobotID " +
                "SET StatusLEDs.Color = 'FF8800', " +
                "    StatusLEDs.R = 255, StatusLEDs.G = 136, StatusLEDs.B = 0 " +
                "WHERE cl.CommandTypeID = 70 AND cl.StatusID = 7",
                connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        // =====================================================================
        // procResetPlayers — C# equivalent
        // Called at the start of each turn. Advances ShutDown state machine,
        // applies Circuit Breaker, resets Status, handles death/respawn.
        // =====================================================================
        public void ResetPlayers()
        {
            // Read LaserDamage (respawn damage = LaserDamage * 2)
            int laserDamage  = GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE sKey='LaserDamage'");
            int useDamage    = laserDamage * 2;

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            // 1. Advance ShutDown state machine for all robots with ShutDown > 0
            //    (join RobotShutDown to get NextState)
            using (var cmd = new MySqlCommand(
                "UPDATE Robots " +
                "INNER JOIN RobotShutDown ON Robots.`ShutDown` = RobotShutDown.ShutDownID " +
                "SET `ShutDown` = NextState " +
                "WHERE Robots.`ShutDown` > 0",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // 2. Circuit Breaker (OptionID=9): auto-shutdown at Damage >= 3
            using (var cmd = new MySqlCommand(
                "UPDATE Robots " +
                "INNER JOIN RobotOptions ON Robots.RobotID = RobotOptions.RobotID AND RobotOptions.OptionID = 9 " +
                "SET ShutDown = 4 " +
                "WHERE Damage >= 3",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // 3. Set Status=2 (Ready to Program) for non-shutdown robots
            //    Robots_BEFORE_UPDATE trigger logic: ShutDown=4 → Damage=0, ShutDown=2; ShutDown=2 → Status=9
            //    We apply the ShutDown=4 transition inline here.
            using (var cmd = new MySqlCommand(
                "UPDATE Robots SET Status = 2 WHERE ShutDown = 0",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Apply trigger logic for ShutDown state transitions before writing
            // ShutDown=4 → Damage=0, ShutDown=2; ShutDown=2 → Status=9
            // We do this inline since triggers are being removed.
            using (var cmd = new MySqlCommand(
                "UPDATE Robots SET Damage = 0, ShutDown = 2 WHERE ShutDown = 4",
                connection))
            {
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new MySqlCommand(
                "UPDATE Robots SET `Status` = 9 WHERE ShutDown = 2",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // 4. Mark robots with Damage > 9 or already Dead as Dead (Status=11)
            using (var cmd = new MySqlCommand(
                "UPDATE Robots SET Damage = 10, ShutDown = 0, Lives = Lives - 1, Status = 11 " +
                "WHERE Damage > 9 OR Status = 11",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Discard played cards for dead/shutdown robots
            using (var cmd = new MySqlCommand(
                "UPDATE MoveCards " +
                "INNER JOIN Robots ON MoveCards.Owner = Robots.RobotID " +
                "SET PhasePlayed = 0, Owner = -1 " +
                "WHERE PhasePlayed > 5 AND (Robots.Status = 11 OR Robots.ShutDown > 0)",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // 5. Superior Archive Copy (OptionID=49): dead robots with lives > 0 respawn undamaged
            using (var cmd = new MySqlCommand(
                "UPDATE Robots " +
                "INNER JOIN RobotOptions ON Robots.RobotID = RobotOptions.RobotID AND RobotOptions.OptionID = 49 " +
                "SET Damage = 0, ShutDown = 0, " +
                "    CurrentPosRow = ArchivePosRow, CurrentPosCol = ArchivePosCol, CurrentPosDir = ArchivePosDir, " +
                "    Status = 1, PositionValid = 0 " +
                "WHERE Status = 11 AND Lives > 0",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // 6. Standard respawn: dead robots with lives > 0 respawn with laser damage penalty
            using (var cmd = new MySqlCommand(
                $"UPDATE Robots " +
                $"SET Damage = {useDamage}, ShutDown = 0, " +
                $"    CurrentPosRow = ArchivePosRow, CurrentPosCol = ArchivePosCol, CurrentPosDir = ArchivePosDir, " +
                $"    Status = 1, PositionValid = 0 " +
                $"WHERE Status = 11 AND Lives > 0",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // 7. Reset RobotOptions.PhasePlayed
            using (var cmd = new MySqlCommand(
                "UPDATE RobotOptions SET PhasePlayed = 0",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Sync status LEDs
            SetStatus();
        }

        // =====================================================================
        // procCurrentPosSave — C# equivalent
        // Snapshots current Robots, MoveCards, and RobotOptions into History tables.
        // =====================================================================
        public void CurrentPosSave()
        {
            int gameID = GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE sKey='GameDataID'");
            int turn   = GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE sKey='Turn'");

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();
            try
            {
                // HistoryRobots
                using (var cmd = new MySqlCommand(
                    $"DELETE FROM HistoryRobots WHERE GameID = {gameID} AND Turn = {turn}",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new MySqlCommand(
                    $"INSERT INTO HistoryRobots " +
                    $"(GameID, Turn, RobotID, OperatorName, RobotBaseID, RobotBodyID, " +
                    $" CurrentFlag, Lives, Damage, ShutDown, Computer, Score, Status, " +
                    $" CurrentPosRow, CurrentPosCol, CurrentPosDir, " +
                    $" ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority) " +
                    $"SELECT {gameID}, {turn}, RobotID, OperatorName, RobotBaseID, RobotBodyID, " +
                    $"       CurrentFlag, Lives, Damage, ShutDown, Computer, Score, Status, " +
                    $"       CurrentPosRow, CurrentPosCol, CurrentPosDir, " +
                    $"       ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority " +
                    $"FROM Robots",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                // HistoryMoveCards
                using (var cmd = new MySqlCommand(
                    $"DELETE FROM HistoryMoveCards WHERE GameID = {gameID} AND Turn = {turn}",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new MySqlCommand(
                    $"INSERT INTO HistoryMoveCards (GameID, Turn, CardID, Owner, PhasePlayed, Locked) " +
                    $"SELECT {gameID}, {turn}, CardID, Owner, PhasePlayed, Locked " +
                    $"FROM MoveCards WHERE Owner > 0",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                // HistoryRobotOptions
                using (var cmd = new MySqlCommand(
                    $"DELETE FROM HistoryRobotOptions WHERE GameID = {gameID} AND Turn = {turn}",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new MySqlCommand(
                    $"INSERT INTO HistoryRobotOptions " +
                    $"(GameID, Turn, RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue) " +
                    $"SELECT {gameID}, {turn}, RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue " +
                    $"FROM RobotOptions",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        // =====================================================================
        // procCurrentPosLoad — C# equivalent
        // Restores Robots, MoveCards, and RobotOptions from History tables.
        // =====================================================================
        public void CurrentPosLoad()
        {
            int gameID = GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE sKey='GameDataID'");
            int turn   = GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE sKey='Turn'");

            // Clear live tables (mirrors procResetGame minus CurrentGameData copy)
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();
            try
            {
                foreach (var tbl in new[] { "MoveCards", "CommandList", "RobotOptions", "StatusLEDs", "Robots" })
                {
                    using var del = new MySqlCommand($"DELETE FROM {tbl}", connection, tx);
                    del.ExecuteNonQuery();
                }

                // Restore Robots
                using (var cmd = new MySqlCommand(
                    $"INSERT INTO Robots " +
                    $"(RobotID, OperatorName, RobotBaseID, RobotBodyID, " +
                    $" CurrentFlag, Lives, Damage, ShutDown, Computer, Score, Status, " +
                    $" CurrentPosRow, CurrentPosCol, CurrentPosDir, " +
                    $" ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority, PositionValid) " +
                    $"SELECT RobotID, OperatorName, RobotBaseID, RobotBodyID, " +
                    $"       CurrentFlag, Lives, Damage, ShutDown, Computer, Score, Status, " +
                    $"       CurrentPosRow, CurrentPosCol, CurrentPosDir, " +
                    $"       ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority, 0 " +
                    $"FROM HistoryRobots WHERE GameID = {gameID} AND Turn = {turn}",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            // Rebuild card deck and restore card state
            GameNewAddCards();

            using var connection2 = new MySqlConnection(_connectionString);
            connection2.Open();
            using var tx2 = connection2.BeginTransaction();
            try
            {
                using (var cmd = new MySqlCommand(
                    $"UPDATE MoveCards " +
                    $"INNER JOIN HistoryMoveCards ON MoveCards.CardID = HistoryMoveCards.CardID " +
                    $"SET MoveCards.Owner = HistoryMoveCards.Owner, " +
                    $"    MoveCards.PhasePlayed = HistoryMoveCards.PhasePlayed, " +
                    $"    MoveCards.Locked = HistoryMoveCards.Locked " +
                    $"WHERE HistoryMoveCards.GameID = {gameID} AND HistoryMoveCards.Turn = {turn}",
                    connection2, tx2))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new MySqlCommand(
                    $"INSERT INTO RobotOptions " +
                    $"(RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue) " +
                    $"SELECT RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue " +
                    $"FROM HistoryRobotOptions WHERE GameID = {gameID} AND Turn = {turn}",
                    connection2, tx2))
                {
                    cmd.ExecuteNonQuery();
                }

                tx2.Commit();
            }
            catch
            {
                tx2.Rollback();
                throw;
            }
        }

        // =====================================================================
        // procVerifyPosition — C# equivalent
        // Sets PositionValid=1 if direction != 0, row != 0, col != 0,
        // and no duplicate robot positions exist.
        // =====================================================================
        public void VerifyPosition(int robotID)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            int posRow = 0, posCol = 0, posDir = 0;
            using (var cmd = new MySqlCommand(
                "SELECT CurrentPosRow, CurrentPosCol, CurrentPosDir FROM Robots WHERE RobotID = @id",
                connection))
            {
                cmd.Parameters.AddWithValue("@id", robotID);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    posRow = reader.GetInt32(0);
                    posCol = reader.GetInt32(1);
                    posDir = reader.GetInt32(2);
                }
            }

            int duplicates = 0;
            using (var cmd = new MySqlCommand(
                "SELECT COUNT(RobotID) FROM Robots WHERE CurrentPosRow = @row AND CurrentPosCol = @col",
                connection))
            {
                cmd.Parameters.AddWithValue("@row", posRow);
                cmd.Parameters.AddWithValue("@col", posCol);
                duplicates = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }

            int passed = (posDir == 0 || posRow == 0 || posCol == 0 || duplicates > 1) ? 0 : 1;

            using (var cmd = new MySqlCommand(
                "UPDATE Robots SET PositionValid = @passed WHERE RobotID = @id",
                connection))
            {
                cmd.Parameters.AddWithValue("@passed", passed);
                cmd.Parameters.AddWithValue("@id", robotID);
                cmd.ExecuteNonQuery();
            }
        }




    }
}