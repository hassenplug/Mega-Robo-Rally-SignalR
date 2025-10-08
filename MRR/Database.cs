using System.Data;
using MySqlConnector;

namespace MRR_CLG
{
    public class Database
    {
        public MySqlConnection Conn;

        public Database()
        {
            Connect();
        }

        public MySqlConnection Connect()
        {
            string myConnectionString = "server=mrobopi3;uid=mrr;pwd=rallypass;database=rally;";

            try
            {
                Conn = new MySqlConnection();
                Conn.ConnectionString = myConnectionString;
                //Conn.ConnectionString = ConfigurationManager.AppSettings;
                Conn.Open();
            }
            catch (MySqlConnector.MySqlException ex)
            //catch ()
            {
                //MessageBox.Show(ex.Message);
                Console.WriteLine(ex.Message);
            }

            return Conn;
        }

        public void Close()
        {
            Conn.Close();
        }

        public MySqlDataReader Exec(string strSQL)
        {
            if (Conn.State == System.Data.ConnectionState.Open)
            {
                try
                {
                
                    MySqlCommand cmd = new MySqlCommand();
                    cmd.CommandText = strSQL;
                    cmd.Connection = Conn;
                    cmd.CommandType = System.Data.CommandType.Text;
                    return cmd.ExecuteReader();
                }
                catch(Exception e)
                {
                    Console.WriteLine("DBError:{0}" , e);

                }
            }

            return null;
        }

        public int Command(string strSQL)
        {
            if (Conn.State == System.Data.ConnectionState.Open)
            {
                // set next game state...
                //Console.WriteLine(strSQL);
                MySqlConnector.MySqlCommand update = Conn.CreateCommand();  // Game.DBConn.Exec(strSQL);
                update.CommandText = strSQL;
                Console.WriteLine(strSQL);
                return update.ExecuteNonQuery();
                
            }
            return 0;
        }

        public int GetIntFromDB(string strSQL)
        {
            var returnset = Exec(strSQL);
            var returnval = 0;
            if (returnset.Read())
            {
                //Console.WriteLine(returnset[0]);
                if (returnset[0] == System.DBNull.Value)
                {
                    returnval = 0;
                }
                else
                {
                    returnval = (int)(long)Convert.ToInt64(returnset[0]) ;
                }
                //var returnval1 = returnset[0];
                //int.TryParse(returnset[0],returnval);
                //var returnval2 = (long)returnset[0];
                //returnval = (int)returnval2;

            }
            returnset.Close();
            return returnval;
        }

        public int[] GetIntList(string strSQL)
        {
            var returnset = Exec(strSQL);
            List<int> returnvalset = new List<int>();

            if (returnset.Read())
            {
                for(int f=0;f<returnset.FieldCount;f++)
                {
                    //Console.WriteLine(returnset[0]);
                    var returnval = 0;
                    if (returnset[f] != System.DBNull.Value)
                    {
                        returnval = (int)(long)Convert.ToInt64(returnset[f]) ;
                    }
                    returnvalset.Add(returnval);
                }
            }
            returnset.Close();
            return returnvalset.ToArray();
        }


        public string jsonFromQuery(string strSQL)
        {
            string output = "";
            string comma = "";

            MySqlConnector.MySqlDataReader reader = Exec(strSQL);
            while (reader.Read())
            {
                string commain = "";
                string localoutput = "";
                for(int c=0;c<reader.FieldCount;c++)
                {
                    localoutput += commain + "\"" + reader.GetName(c) + "\":\"" + reader.GetValue(c) + "\"";
                    commain = ",";
                }
                output += comma + "{" + localoutput + "}";
                comma = ",";
            }

            output = "[" + output + "]";
            reader.Close();

            //Console.WriteLine("output:" + output);

            return output;
        }

        public string GetHTMLfromQuery(string strSQL)
        {
            string output = "";
            string fields = "";

            MySqlConnector.MySqlDataReader reader = Exec(strSQL);
            while (reader.Read())
            {
                fields = "";
                output += "<tr>";

                //GetFieldType(Int32)

                for(int c=0;c<reader.FieldCount;c++)
                {
                    fields += "<td style='background-color:#cccccc;'>" + reader.GetName(c) + "</td>" ;
                    //fields += "<td bgcolor=#080808>" + reader.GetName(c) + "</td>" ;
                    output += "<td style='background-color:#eeeeee;'>" + reader.GetValue(c) + "</td>" ;
                }

                output += "</tr>";
            }

            output = "<table width='100%'><tr>" + fields + "</tr>" + output + "</table>";
            reader.Close();

            //Console.WriteLine("output:" + output);

            return output;
        }

        public string GetTableNames(string usetable)
        {
            string strSQL = "select TABLE_NAME  from information_schema.TABLES t where TABLE_SCHEMA ='rally' order by TABLE_TYPE , TABLE_NAME  ;";
            string output = "";

            MySqlDataReader reader = Exec(strSQL);
            while (reader.Read())
            {
                output += "<option value='" +  reader[0] + "'";
                if ((string)reader[0] == usetable) output += " selected ";
                output += ">" +  reader[0] + "</option>";
            }

            output = "<select id='tables' onchange='changeToTable();'>" + output + "</select>";
            reader.Close();
            return output;
        }

        public string GetEditor(string readdata)
        {

            var sout = readdata.Split("/");
            var newQuery =  sout[sout.Length-1] ;
//            return rDBConn.GetHTMLfromQuery(newQuery);

            string output = "<html><head>";
            output += "<script src='/jscode.js' type='text/javascript' charset='utf-8'></script>";
            output += "</head><body>";
//            output += "<h1>Database Editor</h1>";
            output +=  GetTableNames(newQuery);
            newQuery = "Select * from " + newQuery;
            output += GetHTMLfromQuery(newQuery);
            output += "</body></html>";

            //Console.WriteLine("output:" + output);

            return output;
        }


        public BoardElementCollection BoardLoadFromDB(int sourceID)
        {
            BoardElementCollection l_BoardElements = new BoardElementCollection();
            string strSQL;
            BoardActionsCollection squareActions = new BoardActionsCollection();

            strSQL = "Select X,Y,SquareAction,ActionSequence,Phase,Parameter from BoardItemActions where BoardID=" + sourceID + ";";
            MySqlConnector.MySqlDataReader actionReader = Exec(strSQL);
            while (actionReader.Read())
            {
                BoardAction oneAction = new BoardAction((SquareAction)actionReader["SquareAction"], (int)actionReader["Parameter"], (int)actionReader["ActionSequence"], (int)actionReader["Phase"]);
                oneAction.SquareX = (int)actionReader["X"];
                oneAction.SquareY = (int)actionReader["Y"];
                squareActions.Add(oneAction);

            }

            actionReader.Close();


            l_BoardElements = new BoardElementCollection();
            strSQL = "Select X,Y,SquareType,Rotation from BoardItems where BoardID=" + sourceID + ";";
            MySqlConnector.MySqlDataReader reader = Exec(strSQL);
            while (reader.Read())
            {
                int boardX = (int)reader["X"];
                int boardY = (int)reader["Y"];
                if (boardX + 1 > l_BoardElements.BoardCols) l_BoardElements.BoardCols = boardX + 1;
                if (boardY + 1 > l_BoardElements.BoardRows) l_BoardElements.BoardRows = boardY + 1;

                BoardActionsCollection boardSquareActions = new BoardActionsCollection();

                foreach(BoardAction thisaction in squareActions.Where(sa => sa.SquareX == boardX && sa.SquareY == boardY))
                {
                    boardSquareActions.Add(thisaction);
                }

                l_BoardElements.SetSquare(boardX, boardY,(SquareType)reader["SquareType"],(Direction)reader["Rotation"],boardSquareActions);
            }

            reader.Close();
            return l_BoardElements;
        }

        public void BoardSaveToDB(int destinationID, BoardElementCollection l_BoardElements)
        {
            Command("Delete from BoardItems where BoardID=" + destinationID + ";");
            Command("Delete from BoardItemActions where BoardID=" + destinationID + ";");
            //  loop through cells
            //  loop through actions
            foreach(BoardElement thisSquare in l_BoardElements.BoardElements)
            {
                string strSQL = "insert into BoardItems " +
                    "(BoardID, X, Y, SquareType, Rotation) " +
                    " values (" + destinationID + "," + thisSquare.BoardCol + "," + thisSquare.BoardRow + "," + (int)thisSquare.Type + "," + (int)thisSquare.Rotation + ")";

                Command(strSQL);
                
                foreach (BoardAction thisAction in thisSquare.ActionList)
                {
                    //ActionList.Add(thisaction);
                    //DBConn.Command("Update CurrentGameData set GameState=0, Message='loaded " + boardcount + "boards'; ");
                    strSQL = "insert into BoardItemActions " +
                        "(BoardID, X, Y, SquareAction, ActionSequence, Phase, Parameter) " +
                        " values (" + destinationID + "," + thisSquare.BoardCol + "," + thisSquare.BoardRow +
                        "," + (int)thisAction.SquareAction + "," + thisAction.ActionSequence + "," + thisAction.Phase + "," + thisAction.Parameter + ")";
                    Command(strSQL);

                    strSQL = "Update Boards set " +
                        " X=" + l_BoardElements.BoardCols.ToString() +
                        ", Y=" + l_BoardElements.BoardRows.ToString() +
                        ", GameType=" + (int)l_BoardElements.GameType +
                        ", TotalFlags=" + l_BoardElements.TotalFlags.ToString() +
                        ", LaserDamage=" + l_BoardElements.LaserDamage.ToString() +
                        " where BoardID=" + destinationID.ToString();
                    Command(strSQL);
                }
            }
        }
        
        public bool SendGameMessage(int NewState, string NewMessage)
        {
            if (Conn.State == System.Data.ConnectionState.Open)
            {
                Command("Update CurrentGameData set iValue=" + NewState + " where iKey=10;");
                Command("Update CurrentGameData set sValue='" + NewMessage + "' where iKey=16;");
            }
            return true;

        }

        public void ResetGameState()
        {
            // clear connections
            // set active commands to ready
            // set game data as needed
        }

    }

}