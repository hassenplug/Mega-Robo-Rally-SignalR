using MRR.Services;
using MySqlConnector;
using Xunit.Abstractions;

namespace MRR.Tests;

/// <summary>
/// DataService.RefillShortDecksFromDiscard (step 5 of MoveCardsShuffleAndDeal): a robot with
/// fewer than 9 cards in its deck gets its discard pile back -- including a robot with an
/// EMPTY deck, which the old query missed (turn 29, 2026-11-22: Twitch and Spinbot stuck at
/// 0 deck cards, so "Show My Deck" showed nothing).
///
/// Needs MySQL/MariaDB. Set MRR_TEST_DB to a connection string for a scratch database the
/// test may create and drop a MoveCards table in; without it the test only reports that it
/// was skipped. It refuses to run against the game database (rally).
/// </summary>
public class MoveCardsRefillTests
{
    private const int DeckLocation = 0;
    private const int HandLocation = 1;
    private const int DiscardLocation = 3;

    private readonly ITestOutputHelper _output;

    public MoveCardsRefillTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void RefillsEmptyAndShortDecks_LeavesFullDecks()
    {
        var connectionString = Environment.GetEnvironmentVariable("MRR_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _output.WriteLine("Skipped. Set MRR_TEST_DB to a scratch database's connection string.");
            return;
        }

        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        Assert.False(string.Equals(connection.Database, "rally", StringComparison.OrdinalIgnoreCase),
            "MRR_TEST_DB points at the game database; use a scratch database.");

        Execute(connection, "DROP TABLE IF EXISTS MoveCards");
        Execute(connection,
            "CREATE TABLE MoveCards (CardID int NOT NULL PRIMARY KEY, Owner int, CardLocation int)");
        try
        {
            int cardId = 0;
            void Add(int owner, int location, int count)
            {
                for (int i = 0; i < count; i++)
                    Execute(connection,
                        $"INSERT INTO MoveCards VALUES ({++cardId}, {owner}, {location})");
            }

            // Robot 1: empty deck -- the bug.  Robot 2: short deck.  Robot 3: full deck.
            Add(1, HandLocation, 9); Add(1, DiscardLocation, 13);
            Add(2, DeckLocation, 2); Add(2, HandLocation, 9); Add(2, DiscardLocation, 9);
            Add(3, DeckLocation, 9); Add(3, DiscardLocation, 11);

            DataService.RefillShortDecksFromDiscard(connection);

            Assert.Equal((13, 0, 9), Piles(connection, 1));
            Assert.Equal((11, 0, 9), Piles(connection, 2));
            Assert.Equal((9, 11, 0), Piles(connection, 3));
        }
        finally
        {
            Execute(connection, "DROP TABLE IF EXISTS MoveCards");
        }
    }

    private static (long deck, long discard, long hand) Piles(MySqlConnection connection, int owner)
    {
        using var cmd = new MySqlCommand(
            "SELECT COALESCE(SUM(CardLocation = 0), 0), COALESCE(SUM(CardLocation = 3), 0), " +
            $"COALESCE(SUM(CardLocation = 1), 0) FROM MoveCards WHERE Owner = {owner}",
            connection);
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return (Convert.ToInt64(reader[0]), Convert.ToInt64(reader[1]), Convert.ToInt64(reader[2]));
    }

    private static void Execute(MySqlConnection connection, string sql)
    {
        using var cmd = new MySqlCommand(sql, connection);
        cmd.ExecuteNonQuery();
    }
}
