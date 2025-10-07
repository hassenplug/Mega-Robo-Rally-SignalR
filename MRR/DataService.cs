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
            //hubContext.Clients.All.SendAsync("AllDataUpdate", dataout);
            return dataout;
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