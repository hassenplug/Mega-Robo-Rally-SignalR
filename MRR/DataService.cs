using System;
using MySqlConnector;

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

        public string GetUserCount()
        {
            string query = "SELECT COUNT(*) FROM Robots;"; 
            int userCount = 0;

            using (var connection = new MySqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();
                    
                    using (var command = new MySqlCommand(query, connection))
                    {
                        object result = command.ExecuteScalar();
                        userCount = Convert.ToInt32(result);
                    }
                    return $"Database Connected: Found {userCount} robots.";
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