using System;
using System.Data;
using System.Text;
using System.IO;
using MySqlConnector;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using MRR.Data;

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
            string strSQLcgd = "Select iKey, sKey, iValue, sValue from CurrentGameData;";

            string strSQL = "select * from viewRobots;";
            string titlemessage = "Turn " + GetIntFromDB("Select iValue from CurrentGameData where iKey=2;");
            //            var payload = new { robots = GetQueryResults(strSQL), currentgamedata = GetQueryResults(strSQLcgd), ServerTime = DateTime.Now.ToLongTimeString() };
            var payload = new { titlemsg = titlemessage, robots = GetQueryResults(strSQL) };
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

        public string JsonFromQuery(string strSQL)
        {
            // Build a simple JSON array string from the query results (field values are escaped naively)
            var dt = GetQueryResults(strSQL);
            var sb = new System.Text.StringBuilder();
            sb.Append('[');
            var firstRow = true;
            foreach (DataRow row in dt.Rows)
            {
                if (!firstRow) sb.Append(',');
                firstRow = false;
                sb.Append('{');
                var firstCol = true;
                foreach (DataColumn col in dt.Columns)
                {
                    if (!firstCol) sb.Append(',');
                    firstCol = false;
                    var val = row[col];
                    var sval = val == DBNull.Value ? "" : val?.ToString();
                    // escape quotes
                    sval = (sval ?? "").Replace("\"", "\\\"");
                    sb.Append('"').Append(col.ColumnName).Append("\":\"").Append(sval).Append('"');
                }
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

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

        public string GetTableNames(string useTable)
        {
            string strSQL = "select TABLE_NAME  from information_schema.TABLES t where TABLE_SCHEMA ='" + DatabaseName + "' order by TABLE_TYPE , TABLE_NAME  ;";
            var dt = GetQueryResults(strSQL);
            var sb = new System.Text.StringBuilder();
            foreach (DataRow row in dt.Rows)
            {
                var name = row[0].ToString();
                sb.Append("<option value='").Append(name).Append("'");
                if (name == useTable) sb.Append(" selected");
                sb.Append('>').Append(name).Append("</option>");
            }
            return "<select id='tables' onchange='changeToTable();'>" + sb.ToString() + "</select>";
        }

        public string GetEditor(string readdata)
        {
            var sout = readdata.Split('/');
            var newQuery = sout[sout.Length - 1];
            string output = "<html><head>";
            output += "<script src='/jscode.js' type='text/javascript' charset='utf-8'></script>";
            output += "</head><body>";
            output += GetTableNames(newQuery);
            newQuery = "Select * from " + newQuery;
            output += GetHTMLfromQuery(newQuery);
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

        public Players GetAllPlayers()
        {
            if (_allPlayers != null)
            {
                return _allPlayers;
            }
            var players = new Players();

            string strSQL = "Select * from viewRobotsInit;";

            var loadplayers = this.GetQueryResults(strSQL);
            foreach (DataRow row in loadplayers.Rows)
            {
                players.Add(new Player()
                {
                    ID = (int)row["RobotID"],
                    PlayerSeat = (int)row["PlayerSeat"],
                    Name = row["RobotName"].ToString(),
                    Color = row["RobotColor"].ToString() ?? "FFFFFF", // default white
                    IPAddress = row["MACID"].ToString(),

                });
                //Console.WriteLine("Loaded player ID:" + row["RobotID"].ToString() + " Name:" + row["RobotName"].ToString() + " IP:" + IPAddress);
            }

            RefreshAllPlayers();
            
            _allPlayers = players;
            return players;
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
                    existingPlayer.Lives = (int)row["Lives"];
                    existingPlayer.Damage = (int)row["Damage"];
                    existingPlayer.ShutDown = (tShutDown)((int)row["ShutDown"]);
                    existingPlayer.PlayerStatus = (tPlayerStatus)((int)row["Status"]);
                    existingPlayer.CurrentPos = new RobotLocation((Direction)(int)row["CurrentPosDir"], (int)row["CurrentPosCol"], (int)row["CurrentPosRow"]);
                    existingPlayer.Priority = (int)row["Priority"];
                    existingPlayer.Energy = (int)row["Energy"];
                    existingPlayer.Active = ((int)row["Status"] != 10);
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
                            if (row[3] != System.DBNull.Value) BoardFileName = row[3].ToString();
                            break;
                        case 22: OptionsOnStartup = value; break;
                        case 27: RulesVersion = value; break;
                    }
                }
            }
            return GameState;
        }




    }
}