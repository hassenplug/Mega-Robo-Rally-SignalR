using System;
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

        public object GetAllData()
        {
            string strSQL = "select * from viewRobots;";
            object robotdata = GetQueryResults(strSQL);
            var dataout = new { Robots = robotdata, ServerTime = DateTime.Now.ToLongTimeString() };
            //Clients.All.SendAsync("AllDataUpdate", dataout);
            return dataout;
        }

        public void UpdatePlayer(int command, int playerId = 0, int data1 = 0, int data2 = 0)
        {
            //Console.WriteLine($"UpdatePlayer called with playerId={playerId}, command={command}, data1={data1}, data2={data2}");

            //string strSQL = $"call updatePlayer({playerId},{command},{data1},{data2});";
            //Console.WriteLine("Update: " + request);
            // update/player/card/removefrom/

            //string[] requestSplit = request.Split('/');
            //string commandID = requestSplit[2];
            //string playerid = requestSplit[3];
            switch (command)
            {
                case 1:
                    //string cardid = requestSplit[4];
                    //string position = requestSplit[5];
                    //DBConn.Command("call procUpdateCardPlayed(" + playerid + "," + cardid + "," + position + ");");
                    //Console.WriteLine($"UpdatePlayer called with playerId={playerId}, command={command}, data1={data1}, data2={data2}");
                    ExecuteQuery("call procUpdateCardPlayed(" + playerId + "," + data1 + "," + data2 + ");");
                    //Console.WriteLine($"UpdatePlayer called with playerId={playerId}, command={command}, data1={data1}, data2={data2}");
                    // check to see if we an go to next state
                    break;
                case 2:
                    //string positionValid = requestSplit[4];
                    // clear message
                    //DBConn.Command("update Robots set PositionValid=" + positionValid + " where RobotID=" + playerid + ";");
                    break;
                case 3:
                    //int markcommand = DBConn.GetIntFromDB("Select MessageCommandID from Robots where RobotID=" + playerid);
                    //DBConn.Command("update Robots set MessageCommandID=null where RobotID=" + playerid + ";");
                    //DBConn.Command("update CommandList set StatusID=6 where CommandID=" + markcommand + ";");
                    break;

            }
            // check to see if we an go to next state
            //select funcGetNextGameState();
            
            //var gamestate = rDBConn.Exec("select funcGetNextGameState();"); //going to next state?
//            var gamestate = DBConn.Command("select funcGetNextGameState();"); //going to next state?
            
            //if (createCommands.UpdateGameState() == 6)
//            if (gamestate == 6)
//            {
//                createCommands.ExecuteTurn();
//            }
//            return MakeRobotsJson(request);
        }

        public bool ExecuteQuery(string query)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.ExecuteNonQuery();
                        return true;
                    }
                }
                catch (MySqlException ex)
                {
                    // Log or handle the exception appropriately
                    Console.WriteLine($"DB Error ({ex.Number}): {ex.Message}");
                    return false;
                }
            }
        }

        public object GetQueryResults(string query)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();

                    using (var command = new MySqlCommand(query, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        var results = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();

                        while (reader.Read())
                        {
                            var row = new System.Collections.Generic.Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                row[reader.GetName(i)] = reader.GetValue(i);
                            }
                            results.Add(row);
                        }

                        return results;
                    }
                }
                catch (MySqlException ex)
                {
                    // Log or handle the exception appropriately
                    return $"DB Error ({ex.Number}): {ex.Message}";
                }
            }
        }
    }
}