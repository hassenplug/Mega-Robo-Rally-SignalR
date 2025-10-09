using System;
using System.Data;
using System.Text;
using System.IO;
using MySqlConnector;
using Newtonsoft.Json;

namespace MRR.Services
{
    public class DataService
    {
        // ⚠️ IMPORTANT: Replace with your actual database credentials
        private const string DbServerIp = "mrobopi3"; // e.g., "
        private const string DatabaseName = "rally";
        private const string UserId = "mrr";
        private const string Password = "rallypass";

        private readonly string _connectionString =
            $"server={DbServerIp};database={DatabaseName};uid={UserId};pwd={Password}";

        ///////////////////////////////////////////////////////////////////////////
        // Retrieve all relevant data from the database to send to clients
        ///////////////////////////////////////////////////////////////////////////


        // Return the results of any query as a JSON string (uses DataTable -> JSON)
        public string GetQueryResultsJson(string query)
        {
            var dt = GetQueryResults(query);
            // Serialize the DataTable rows as an array of objects
            return JsonConvert.SerializeObject(dt);
        }

        // Convenience: return the same payload as GetAllData but as a JSON string
        public string GetAllDataJson()
        {
            string strSQL = "select * from viewRobots;";
            var payload = new { robots = GetQueryResults(strSQL), ServerTime = DateTime.Now.ToLongTimeString() };
            return JsonConvert.SerializeObject(payload);
            //return payload.toString();
        }

        // Return the JSON payload as UTF8 bytes
        public byte[] GetAllDataJsonBytes()
        {
            return Encoding.UTF8.GetBytes(GetAllDataJson());
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
                    var sval = val == DBNull.Value ? "" : val.ToString();
                    // escape quotes
                    sval = sval.Replace("\"", "\\\"");
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


    }
}