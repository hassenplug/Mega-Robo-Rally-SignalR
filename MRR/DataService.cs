using System;
using System.Data;
using System.Text;
using System.IO;
using MySqlConnector;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using MRR.Data;
using System.Xml.Serialization;

namespace MRR.Services
{
    public class DataService
    {
        private const string DbServerIp = "mrobopi3"; // e.g., "
        private const string DatabaseName = "rally";
        private const string UserId = "mrr";
        private const string Password = "rallypass";

        private readonly string _connectionString =
            $"server={DbServerIp};database={DatabaseName};uid={UserId};pwd={Password}";

        public DataService()
        {
            // Deferred initialization of players; loaded on first access via AllPlayers getter
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
            var ctx = new MRRDbContext(optionsBuilder.Options);

            // Populate PendingCommandEntity.RobotPlayer when entities are materialized/tracked
            ctx.ChangeTracker.Tracked += (sender, e) =>
            {
                try
                {
                    if (e.Entry.Entity is MRR.Data.Entities.PendingCommandEntity pc)
                    {
                        // Only populate when coming from a query (materialized)
                        if (e.FromQuery)
                        {
                            var players = this.AllPlayers;
                            if (players != null)
                            {
                                pc.RobotPlayer = players.GetPlayer(p => p.ID == pc.RobotID);
                            }
                        }
                    }
                }
                catch
                {
                    // swallow any errors to avoid breaking tracking
                }
            };

            return ctx;
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
            set => _allPlayers = value;
        }

        public int RobotsActive { get; set; }

        public string BoardFileName { get; set; } = string.Empty;

        public int BoardID { get; set; }

        public int GameState { get; set; }

        public int RulesVersion { get; set; }

        public int PhaseCount { get; set; }

        public CommandList ListOfCommands { get; set; } = new CommandList();

        public CardList GameCards { get; set; } = new CardList();

        public OptionCardList OptionCards { get; set; } = new OptionCardList();

        public Dictionary<int, string> OptionCardNames = new Dictionary<int, string>();

        public BoardElementCollection g_BoardElements { get; set; } = new BoardElementCollection();

        public int CurrentTurn { get; set; } = 0;
        public int CurrentPhase { get; set; } = 0;

        public GameTypes GameType { get; set; }

        public int OptionsOnStartup { get; set; } = -1;

        public int LaserDamage { get; set; } = 1;

        public int TotalFlags { get; set; } = 4;

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

        // Convenience: return the same payload as GetAllData but as a JSON string
        public string GetAllDataJson()
        {
            UpdateGameState();
            //string strSQLcgd = "Select iKey, sKey, iValue, sValue from CurrentGameData;";

            string strSQL = "select * from viewRobots;";
            //string titlemessage = "Turn " + GetIntFromDB("Select iValue from CurrentGameData where iKey=2;");
            string titlemessage = "Turn " + CurrentTurn;
            if (CurrentPhase > 0)
            {
                titlemessage += " Phase " + CurrentPhase;
            }
            //            var payload = new { robots = GetQueryResults(strSQL), currentgamedata = GetQueryResults(strSQLcgd), ServerTime = DateTime.Now.ToLongTimeString() };
            var payload = new { titlemsg = titlemessage, gamestate = GameState, robots = GetQueryResults(strSQL) };
            return JsonConvert.SerializeObject(payload);
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


        public void BoardSaveToDB(int destinationID, BoardElementCollection l_BoardElements)
        {
            ExecuteSQL("Delete from BoardItems where BoardID=" + destinationID + ";");
            ExecuteSQL("Delete from BoardItemActions where BoardID=" + destinationID + ";");
            //  loop through cells
            //  loop through actions
            foreach (BoardElement thisSquare in l_BoardElements.BoardElements)
            {
                string strSQL = "insert into BoardItems " +
                    "(BoardID, X, Y, SquareType, Rotation) " +
                    " values (" + destinationID + "," + thisSquare.BoardCol + "," + thisSquare.BoardRow + "," + (int)thisSquare.Type + "," + (int)thisSquare.Rotation + ")";

                ExecuteSQL(strSQL);

                foreach (BoardAction thisAction in thisSquare.ActionList)
                {

                    strSQL = "insert into BoardItemActions " +
                        "(BoardID, X, Y, SquareAction, ActionSequence, Phase, Parameter) " +
                        " values (" + destinationID + "," + thisSquare.BoardCol + "," + thisSquare.BoardRow +
                        "," + (int)thisAction.SquareAction + "," + thisAction.ActionSequence + "," + thisAction.Phase + "," + thisAction.Parameter + ")";
                    ExecuteSQL(strSQL);

                    strSQL = "Update Boards set " +
                        " X=" + l_BoardElements.BoardCols.ToString() +
                        ", Y=" + l_BoardElements.BoardRows.ToString() +
                        ", GameType=" + (int)l_BoardElements.GameType +
                        ", TotalFlags=" + l_BoardElements.TotalFlags.ToString() +
                        ", LaserDamage=" + l_BoardElements.LaserDamage.ToString() +
                        " where BoardID=" + destinationID.ToString();
                    ExecuteSQL(strSQL);
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

                string strSQL = "Select * from viewRobotsInit;";

                var loadplayers = this.GetQueryResults(strSQL);
                foreach (DataRow row in loadplayers.Rows)
                {
                    players.Add(new Player()
                    {
                        ID = (int)row["RobotID"],
                        PlayerSeat = (int)row["PlayerSeat"],
                        Name = row["RobotName"].ToString() ?? "",
                        Color = row["RobotColor"].ToString() ?? "FFFFFF", // default white
                        IPAddress = row["MACID"].ToString(),

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
            string strSQL = "Select * from viewRobotsRefresh;";

            var loadplayers = this.GetQueryResults(strSQL);
            foreach (DataRow row in loadplayers.Rows)
            {
                var existingPlayer = _allPlayers?.FirstOrDefault(p => p.ID == (int)row["RobotID"]);
                if (existingPlayer != null)
                {
                    existingPlayer.LastFlag = (int)row["CurrentFlag"];
                    //existingPlayer.Lives = (int)row["Lives"];
                    //existingPlayer.Damage = (int)row["Damage"];
                    existingPlayer.ShutDown = (tShutDown)((int)row["ShutDown"]);
                    existingPlayer.PlayerStatus = (tPlayerStatus)((int)row["StatusID"]);
                    existingPlayer.CurrentPos = new RobotLocation((Direction)(int)row["Dir"], (int)row["X"], (int)row["Y"]);
                    existingPlayer.Priority = (int)row["Priority"];
                    existingPlayer.Energy = (int)row["Energy"];
                    existingPlayer.Active = ((int)row["StatusID"] != 10);
                };
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
                        case 2: CurrentTurn = value; break;
                        case 3: CurrentPhase = value; break;
                        case 6: LaserDamage = value; break;
                        case 8: RobotsActive = value; break;
                        case 10: GameState = value; break;
                        case 16: PhaseCount = value; break;
                        case 20:
                            BoardID = value;
                            if (row[3] != System.DBNull.Value) BoardFileName = row[3].ToString() ?? "";
                            break;
                        case 22: OptionsOnStartup = value; break;
                        case 27: RulesVersion = value; break;
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
        /// C# equivalent of funcProcessCommand(p_CommandID, p_NewStatus).
        /// Loads the command row, performs any DB-side effect based on CommandTypeID,
        /// applies position updates when p_NewStatus==5, then writes the final StatusID
        /// back to CommandList. Returns the resulting StatusID.
        /// Pass p_NewStatus=-1 to auto-complete (equivalent to SQL default).
        /// </summary>
        public int ProcessDbCommand(int p_CommandID, int p_NewStatus)
        {
            // Load command row
            int cType = 0, cRobotID = 0, cParameter = 0, cParameterB = 0;
            int cRow = 0, cCol = 0, cDir = 0;

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using var loadCmd = new MySqlCommand(
                    "SELECT CommandTypeID, RobotID, Parameter, ParameterB, PositionRow, PositionCol, PositionDir " +
                    "FROM CommandList WHERE CommandID = @id", connection);
                loadCmd.Parameters.AddWithValue("@id", p_CommandID);
                using var reader = loadCmd.ExecuteReader();
                if (!reader.Read())
                {
                    // Command not found — nothing to do
                    return p_NewStatus == -1 ? 6 : p_NewStatus;
                }
                cType       = reader.GetInt32(0);
                cRobotID    = reader.GetInt32(1);
                cParameter  = reader.GetInt32(2);
                cParameterB = reader.GetInt32(3);
                cRow        = reader.GetInt32(4);
                cCol        = reader.GetInt32(5);
                cDir        = reader.GetInt32(6);
            }

            if (p_NewStatus == -1)
                p_NewStatus = 6; // command complete

            // Process side-effects by CommandTypeID
            switch ((SquareAction)cType)
            {
                case SquareAction.PlayerLocation: // Player Location — handled below in the status==5 block
                    p_NewStatus = 5;
                    break;

                case SquareAction.Damage: // Set Damage
                    ExecuteSQL(
                        $"UPDATE Robots SET Damage = {cParameter} WHERE RobotID = {cRobotID}");
                    break;

                case SquareAction.Archive: // Set Archive position
                    ExecuteSQL(
                        $"UPDATE Robots SET ArchivePosRow = {cRow}, ArchivePosCol = {cCol}, ArchivePosDir = 0 " +
                        $"WHERE RobotID = {cRobotID}");
                    break;

                case SquareAction.Flag: // Set Current Flag
                    ExecuteSQL(
                        $"UPDATE Robots SET CurrentFlag = {cParameter} WHERE RobotID = {cRobotID}");
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
                    ExecuteSQL(
                        $"UPDATE Robots SET Lives = {cParameter} WHERE RobotID = {cRobotID}");
                    break;

                case SquareAction.DealCard: // Deal card to player (assign card owner)
                    ExecuteSQL(
                        $"UPDATE MoveCards SET Owner = {cRobotID} WHERE CardID = {cParameter}");
                    break;

                case SquareAction.GameWinner: // Game Winner
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET iValue = 11 WHERE iKey = 10");
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
                    ExecuteSQL(
                        $"UPDATE Robots SET Status = {cParameter} WHERE RobotID = {cRobotID}");
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
                    ExecuteSQL(
                        $"UPDATE Robots SET ShutDown = {cParameter} WHERE RobotID = {cRobotID}");
                    break;

                case SquareAction.SetCurrentGameData: // Set CurrentGameData iValue by iKey
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET iValue = {cParameterB} WHERE iKey = {cParameter}");
                    break;

                case SquareAction.EndOfGame: // End of game
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET iValue = 12 WHERE iKey = 10");
                    break;

                case SquareAction.DeleteRobot: // Delete robot
                    ExecuteSQL(
                        $"DELETE FROM Robots WHERE RobotID = {cRobotID}");
                    break;

                case SquareAction.SetGameState: // Set GameState
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET iValue = {cParameter} WHERE iKey = 10");
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
                case SquareAction.SetButtonText:
                    break;
                case SquareAction.SetEnergy:
                    ExecuteSQL(
                        $"UPDATE Robots SET Energy = {cParameter} WHERE RobotID = {cRobotID}");
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
                    ExecuteSQL(
                        $"UPDATE Robots SET CurrentPosRow = {cRow}, CurrentPosCol = {cCol}, " +
                        $"CurrentPosDir = {cDir}, Score = {cParameterB} WHERE RobotID = {cRobotID}");
                }
                p_NewStatus = 6; // command complete
            }

            // Write final status back to CommandList
            if (p_CommandID > 0)
            {
                ExecuteSQL(
                    $"UPDATE CommandList SET StatusID = {p_NewStatus} WHERE CommandID = {p_CommandID}");
            }

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
        /// Behaviour varies by RulesVersion and PhaseCount:
        ///   PhaseCount=1  — single-phase (10-Turn) mode: rotate priorities then assign
        ///                   cards to players by priority slot.
        ///   RulesVersion=1 — Renegade rules: discard played Spam cards, move hand/played
        ///                   cards to discard, shuffle with DealPriority weighting, refill
        ///                   deck from discard if a player has fewer than 9 cards, deal 9
        ///                   cards and update Robots.CardsDealt / CardsPlayed strings.
        ///   RulesVersion=0 — Classic rules: deal (9 - Damage + ExtraMemoryOption) cards
        ///                   per robot, respecting locked registers, then call
        ///                   MoveCardsCheckProgrammed to update robot Status values.
        /// </summary>
        public void MoveCardsShuffleAndDeal()
        {
            int rulesVersion = GetIntFromDB(
                "SELECT iValue FROM CurrentGameData WHERE sKey = 'RulesVersion'");
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
                    // No cards exist yet — create the deck via the stored procedure.
                    using var addCards = new MySqlCommand("CALL procGameNewAddCards()", connection);
                    addCards.ExecuteNonQuery();
                }
            }

            if (phaseCount == 1)
            {
                // Single-phase (10-Turn) mode.
                // Rotate player priorities via the stored procedure, then assign
                // cards to each robot by their priority slot (10 - floor((CardID-1)/7)).
                using (var cmd = new MySqlCommand("CALL procUpdatePlayerPriority()", connection))
                {
                    cmd.ExecuteNonQuery();
                }

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
            else if (rulesVersion == 1)
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
            }
            else
            {
                // Classic rules (RulesVersion=0).

                // Load all active robots (Programming=1) with their Damage values.
                var activeRobots = new List<(int robotID, int damage)>();
                using (var cmd = new MySqlCommand(
                    "SELECT Robots.Damage, Robots.RobotID " +
                    "FROM Robots " +
                    "INNER JOIN RobotStatus ON Robots.`Status` = RobotStatus.RobotStatusID " +
                    "WHERE RobotStatus.Programming = 1",
                    connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        activeRobots.Add((reader.GetInt32(1), reader.GetInt32(0)));
                    }
                }

                // First pass: handle locked registers for robots with damage > 4.
                foreach (var (robotID, damage) in activeRobots)
                {
                    int lastLockedCard;
                    if (damage > 4)
                    {
                        lastLockedCard = 10 - damage;

                        // Check that the robot has enough cards; fill from unowned pool if short.
                        int cardCount = GetIntFromDB(
                            $"SELECT COUNT(CardID) FROM MoveCards WHERE `Owner` = {robotID}");
                        if (cardCount < 5)
                        {
                            using var fillCmd = new MySqlCommand(
                                $"UPDATE MoveCards SET `Owner` = {robotID}, Random = 1 " +
                                $"WHERE `Owner` = -1 ORDER BY CurrentOrder, CardID LIMIT 5",
                                connection);
                            fillCmd.ExecuteNonQuery();

                            // Auto-fill empty program registers.
                            using var fillProgs = new MySqlCommand("CALL procGameFillPrograms()", connection);
                            fillProgs.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        lastLockedCard = 6;
                    }

                    // Lock any cards that were played in registers >= lastLockedCard.
                    using var lockCmd = new MySqlCommand(
                        $"UPDATE MoveCards SET `Locked` = 1 " +
                        $"WHERE `Owner` = {robotID} AND PhasePlayed >= {lastLockedCard} AND PhasePlayed < 6",
                        connection);
                    lockCmd.ExecuteNonQuery();
                }

                // Return all unlocked, non-programmed cards to the unowned pool
                // with a fresh random shuffle order.
                using (var cmd = new MySqlCommand(
                    "UPDATE MoveCards " +
                    "SET CurrentOrder = ROUND(500.0 * RAND()), `Owner` = -1, PhasePlayed = -1, Random = 0 " +
                    "WHERE `Locked` = 0 AND PhasePlayed < 6",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // Second pass: deal cards to each active robot.
                foreach (var (robotID, damage) in activeRobots)
                {
                    // Extra Memory option (OptionID=16) adds 1 to card count.
                    int optionCards = GetIntFromDB(
                        $"SELECT IFNULL(SUM(Quantity), 0) FROM RobotOptions " +
                        $"WHERE RobotID = {robotID} AND OptionID = 16");

                    int newCardCount = 9 - damage + optionCards;

                    while (newCardCount > 0)
                    {
                        using var dealCmd = new MySqlCommand(
                            $"UPDATE MoveCards SET `Owner` = {robotID} " +
                            $"WHERE `Owner` = -1 ORDER BY CurrentOrder, CardID LIMIT 1",
                            connection);
                        dealCmd.ExecuteNonQuery();
                        newCardCount--;
                    }
                }

                // Update robot Status values based on programming state.
                MoveCardsCheckProgrammed(connection);
            }
        }

        /// <summary>
        /// C# equivalent of procMoveCardsCheckProgrammed.
        /// Reads each active robot's card counts and updates their Status:
        ///   1 = Waiting for Cards (fewer than 5 cards)
        ///   4 = Ready to Run (all PhaseCount registers filled)
        ///   3 = Programming (some registers filled beyond locked count)
        ///   2 = Ready to Program (no progress yet)
        /// Uses an existing open connection so it can be called within a transaction
        /// or alongside other commands without opening a second connection.
        /// </summary>
        public void MoveCardsCheckProgrammed(MySqlConnection? connection = null)
        {
            bool ownConnection = connection == null;
            if (ownConnection)
            {
                connection = new MySqlConnection(_connectionString);
                connection.Open();
            }

            try
            {
                int requiredCards = GetIntFromDB(
                    "SELECT iValue FROM CurrentGameData WHERE sKey = 'PhaseCount'");

                // Collect per-robot card statistics for all actively-programming robots.
                var robotStats = new List<(int cards, int programmed, int locked, int robotID, int currentStatus)>();
                using (var cmd = new MySqlCommand(
                    "SELECT COUNT(CardID) AS countCards, " +
                    "  SUM(IF(MoveCards.PhasePlayed > 0 AND MoveCards.PhasePlayed < 6, 1, 0)) AS countProgrammed, " +
                    "  SUM(IF(MoveCards.Locked > 0, 1, 0)) AS countLocked, " +
                    "  Robots.RobotID AS rID, " +
                    "  Robots.`Status` AS CurrentStatus " +
                    "FROM Robots " +
                    "INNER JOIN RobotStatus ON Robots.`Status` = RobotStatus.RobotStatusID " +
                    "INNER JOIN MoveCards ON Robots.RobotID = MoveCards.`Owner` " +
                    "WHERE RobotStatus.Programming = 1 " +
                    "GROUP BY rID, CurrentStatus",
                    connection!))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        robotStats.Add((
                            reader.GetInt32(0),
                            reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                            reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            reader.GetInt32(3),
                            reader.GetInt32(4)
                        ));
                    }
                }

                foreach (var (cards, programmed, locked, robotID, currentStatus) in robotStats)
                {
                    int newStatus;
                    if (cards < 5)
                        newStatus = 1;
                    else if (programmed == requiredCards)
                        newStatus = 4;
                    else if (programmed > locked)
                        newStatus = 3;
                    else
                        newStatus = 2;

                    if (newStatus != currentStatus)
                    {
                        using var updateCmd = new MySqlCommand(
                            $"UPDATE Robots SET `Status` = {newStatus} WHERE RobotID = {robotID}",
                            connection!);
                        updateCmd.ExecuteNonQuery();
                    }
                }
            }
            finally
            {
                if (ownConnection)
                {
                    connection!.Dispose();
                }
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

        public void BoardFileRead(string p_Filename)
        {

            if (p_Filename.Contains(".jpg")) p_Filename = p_Filename.Replace(".jpg", ".srx");
            if (p_Filename.Contains(".srx"))
            {
                g_BoardElements = LoadFile(typeof(BoardElementCollection), p_Filename) as BoardElementCollection ?? new BoardElementCollection();
            }

            if (g_BoardElements != null)
            {
                TotalFlags = g_BoardElements.BoardElements.Count(be => be.ActionList.Count(al => al.SquareAction == SquareAction.Flag) > 0);
                LaserDamage = g_BoardElements.LaserDamage;
                //GameType = g_BoardElements.BoardType;
            }
            else
            {
                Console.WriteLine("Load Board Failed:" + p_Filename);
            }
        }

        public Object? LoadFile(Type FileType, string FileName)
        {
            if (!File.Exists(FileName))
            {
                return null;
            }
            //XmlDeserializationEvents
            DateTime starttime = DateTime.Now;
            XmlSerializer serialPlay = new XmlSerializer(FileType);
            System.IO.StreamReader csvfile = new System.IO.StreamReader(FileName);
            Object? localfile = serialPlay.Deserialize(csvfile);
            csvfile.Close();
            //Console.WriteLine("Load " + FileType.ToString() + " ET:" + (DateTime.Now - starttime).ToString());

            return localfile;
        }




    }
}