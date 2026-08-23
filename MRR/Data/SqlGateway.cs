using System.Data;
using Microsoft.EntityFrameworkCore;
using MRR.Data;
using MySqlConnector;

namespace MRR.Services
{
    /// <summary>
    /// Raw database access for the game host: connection string, ad-hoc SQL, and the EF
    /// context factory. No game logic and no in-memory state — it does not know what a
    /// robot or a turn is.
    ///
    /// First slice of the DataService split (design section 4). DataService still exposes
    /// ExecuteSQL/GetQueryResults and delegates here, so every existing call site is
    /// unchanged; the point is that the plumbing now has one home instead of being tangled
    /// with the state cache and the rules.
    /// </summary>
    public class SqlGateway
    {
        private readonly string _connectionString;

        public SqlGateway(string connectionString)
        {
            _connectionString = connectionString;
            DatabaseName = new MySqlConnectionStringBuilder(connectionString).Database;
        }

        public string ConnectionString => _connectionString;
        public string DatabaseName { get; }

        /// <summary>New MRRDbContext on this connection. Caller disposes.</summary>
        public MRRDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<MRRDbContext>()
                .UseMySql(_connectionString, new MySqlServerVersion(new Version(8, 0, 0)))
                .Options;
            return new MRRDbContext(options);
        }

        public MySqlConnection OpenConnection()
        {
            var connection = new MySqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// Runs a non-query. Returns 0 on a database error rather than throwing.
        ///
        /// That swallow is long-standing behaviour and is kept deliberately, but it is worth
        /// knowing about: a bad connection string does not surface here, it surfaces later as
        /// a NullReferenceException somewhere that expected data. If startup fails oddly,
        /// check the database connection first. Centralising it here at least means there is
        /// one place to change that decision.
        /// </summary>
        public int ExecuteSQL(string query)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = new MySqlCommand(query, connection);
                return command.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"DB Error ({ex.Number}): {ex.Message}");
                Console.WriteLine($"sql: ({query})");
                return 0;
            }
        }

        /// <summary>Runs a query. Returns an empty table on error, so callers can iterate.</summary>
        public DataTable GetQueryResults(string query)
        {
            var table = new DataTable();
            try
            {
                using var connection = OpenConnection();
                using var command = new MySqlCommand(query, connection);
                using var reader = command.ExecuteReader();
                table.Load(reader);
            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"DB Error ({ex.Number}): {ex.Message}");
                Console.WriteLine($"sql: ({query})");
            }
            return table;
        }

        /// <summary>First column of the first row as an int; 0 if there is no row.</summary>
        public int GetIntFromDB(string query)
        {
            var table = GetQueryResults(query);
            if (table.Rows.Count == 0) return 0;
            var value = table.Rows[0][0];
            return value == DBNull.Value ? 0 : Convert.ToInt32(value);
        }

        /// <summary>
        /// First column of every row as ints. A NULL yields 0 and still occupies a slot, so
        /// the result stays positionally aligned with the rows — matching the behaviour
        /// callers already rely on.
        /// </summary>
        public int[] GetIntList(string query)
        {
            var table = GetQueryResults(query);
            var values = new List<int>(table.Rows.Count);
            foreach (DataRow row in table.Rows)
            {
                var value = row[0];
                values.Add(value == DBNull.Value ? 0 : Convert.ToInt32(value));
            }
            return [.. values];
        }
    }
}
