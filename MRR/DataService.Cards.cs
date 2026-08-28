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
    /// <summary>
    /// DataService — MoveCards and Options: dealing, shuffling, drawing, playing.
    ///
    /// Part of the step 4 split (API_DECOMPOSITION_DESIGN.md section 4): DataService is
    /// one class doing several jobs, so it is first separated by concern into partials.
    /// Splitting the file changes nothing semantically, but it makes each concern's real
    /// dependencies visible, which is what the repository extraction needs.
    /// </summary>
    public partial class DataService
    {

        /// <summary>
        /// DOES NOTHING. Retained only so its seven call sites keep compiling.
        ///
        /// It was meant to reload a player's MoveCards from the database into the in-memory
        /// GameCards. UpdateCardPlayed now does that inline (step 8 below), for exactly the
        /// two cards it moved, so re-reading the player's whole hand on every keypress became
        /// redundant and someone disabled this with an early return.
        ///
        /// The problem is that it is silent: Program.cs, GameController and RobotScreenUI all
        /// call it as though it refreshes something. Either delete it and its callers, or
        /// restore it -- but it should not stay a no-op that reads like a refresh. Tracked in
        /// install/todo.md, Section 7.
        /// </summary>
        public void RefreshPlayerCards(int robotID)
        {
            return;   // intentional: see remarks above. The body below is unreachable.
#pragma warning disable CS0162
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
#pragma warning restore CS0162
        }

        /// <summary>
        /// Rebuilds Robots.CardsDealt (CSV of hand CardTypeIDs, desc) and Robots.CardsPlayed
        /// (CSV of each register's CardTypeID by PhaseCounter slot, 0 = empty) from MoveCards.
        /// Pass playerId to rebuild a single robot (e.g. after UpdateCardPlayed); omit to
        /// rebuild every robot at once (e.g. after MoveCardsShuffleAndDeal).
        /// </summary>
        private void RebuildRobotCardsSummary(MySqlConnection connection, int? playerId = null)
        {
            string dealtFilter  = playerId.HasValue ? "AND mc.Owner = @player" : "";
            string playedFilter = playerId.HasValue ? "WHERE r2.RobotID = @player" : "";
            string whereClause  = playerId.HasValue ? "WHERE rb.RobotID = @player" : "";

            string sql =
                "UPDATE Robots rb " +
                "LEFT JOIN (" +
                "  SELECT mc.Owner, GROUP_CONCAT(mc.CardTypeID ORDER BY mc.CardTypeID DESC) AS gctl " +
                $"  FROM MoveCards mc WHERE mc.CardLocation = 1 {dealtFilter} " +
                "  GROUP BY mc.Owner" +
                ") dealt ON rb.RobotID = dealt.Owner " +
                "LEFT JOIN (" +
                "  SELECT r2.RobotID AS Owner, GROUP_CONCAT(IFNULL(mc.CardTypeID, 0) ORDER BY pc.ID) AS gctp " +
                "  FROM Robots r2 CROSS JOIN PhaseCounter pc " +
                "  LEFT JOIN MoveCards mc ON pc.ID = mc.PhasePlayed AND mc.Owner = r2.RobotID " +
                $"  {playedFilter} " +
                "  GROUP BY r2.RobotID" +
                ") played ON rb.RobotID = played.Owner " +
                $"SET rb.CardsDealt = dealt.gctl, rb.CardsPlayed = played.gctp {whereClause}";

            using var cmd = new MySqlCommand(sql, connection);
            if (playerId.HasValue)
                cmd.Parameters.AddWithValue("@player", playerId.Value);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Recomputes Robots.CardCount (every MoveCards row owned by the robot, regardless of
        /// CardLocation) from MoveCards. One UPDATE shared by every place that changes how many
        /// cards a robot owns: dealing and expired-Spam-card deletion in
        /// MoveCardsShuffleAndDeal (all robots), and a new Spam card being dealt to one robot
        /// in DealSpamToPlayer. Pass playerId to refresh a single robot; omit to refresh all.
        /// </summary>
        private void RefreshCardCount(MySqlConnection connection, int? playerId = null)
        {
            string whereClause = playerId.HasValue ? "WHERE rb.RobotID = @player" : "";

            string sql =
                "UPDATE Robots rb " +
                "LEFT JOIN (" +
                "  SELECT Owner, COUNT(*) AS cnt FROM MoveCards GROUP BY Owner" +
                ") mc ON rb.RobotID = mc.Owner " +
                $"SET rb.CardCount = IFNULL(mc.cnt, 0) {whereClause}";

            using var cmd = new MySqlCommand(sql, connection);
            if (playerId.HasValue)
                cmd.Parameters.AddWithValue("@player", playerId.Value);
            cmd.ExecuteNonQuery();
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
        /// </summary>
        /// <remarks>
        /// NEEDS RE-CHECK once documents/ALLPLAYERS_REMOVAL_DESIGN.md's removal is fully in:
        /// this method used to look up the robot's cached Player object via AllPlayers and set
        /// its PlayerStatus after the DB write below. That write-through was confirmed dead
        /// (nothing reads it before the next full reload) and has been removed, but this was
        /// the one call site flagged for a second look rather than assumed safe outright --
        /// verify the phone/robot-screen programming flow still shows the right status after
        /// this change.
        /// </remarks>
        public void UpdateCardPlayed(int p_Player, int p_CardTypeID, int p_PhasePlayed)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

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
            RebuildRobotCardsSummary(connection, p_Player);

            // Update robot Status.
            using (var cmd = new MySqlCommand(
                "UPDATE Robots SET `Status` = @status WHERE RobotID = @player",
                connection))
            {
                cmd.Parameters.AddWithValue("@status", newStatus);
                cmd.Parameters.AddWithValue("@player", p_Player);
                cmd.ExecuteNonQuery();
            }

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

        /// <summary>
        /// The cards a robot would draw, in order, when Spam forces a replacement: its deck
        /// (CardLocation 0) first, then its discard pile (3), each by CurrentOrder.
        ///
        /// This is the read half of what funcGetNextCard did. Drawing up front is what makes
        /// a turn reproducible: the old code drew inside the simulation loop and reshuffled
        /// the discard pile as a side effect, so replanning the same turn could deal
        /// differently. Read-only -- retiring the spent Spam cards happens after planning,
        /// from TurnPlan.SpamConsumed.
        /// </summary>
        public List<MoveCard> BuildDrawPile(int robotID)
        {
            var pile = new List<MoveCard>();
            var table = GetQueryResults(
                "SELECT CardID FROM MoveCards " +
                $"WHERE Owner = {robotID} AND (CardLocation = 0 OR CardLocation = 3) " +
                "ORDER BY CardLocation, CurrentOrder;");

            foreach (System.Data.DataRow row in table.Rows)
            {
                int cardID = Convert.ToInt32(row["CardID"]);
                var card = GameCards.FirstOrDefault(c => c.ID == cardID && c.Owner == robotID);
                if (card != null) pile.Add(card);
            }
            return pile;
        }

        /// <summary>
        /// Marks the Spam cards a planned turn consumed as played (CardLocation 5), which
        /// funcGetNextCard used to do while drawing.
        /// </summary>
        public void RetireSpamCards(IEnumerable<SpamCardUse> consumed)
        {
            foreach (var use in consumed)
            {
                ExecuteSQL(
                    $"UPDATE MoveCards SET CardLocation = 5 WHERE Owner = {use.RobotID} AND CardID = {use.CardID};");
            }
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

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            using (var cmd = new MySqlCommand(
                "INSERT INTO MoveCards (CardID, CardTypeID, Owner, CardLocation) " +
                "VALUES (@cardId, 10, @robotId, 3)",
                connection))
            {
                cmd.Parameters.AddWithValue("@cardId", maxId);
                cmd.Parameters.AddWithValue("@robotId", robotID);
                cmd.ExecuteNonQuery();
            }

            // A new Spam card just entered this robot's collection; keep CardCount in sync.
            RefreshCardCount(connection, robotID);

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

                // 7. Rebuild Robots.CardsDealt/CardsPlayed for every robot (no one has
                //    played into a register yet, so CardsPlayed naturally comes out "0,0,0,0,0").
                RebuildRobotCardsSummary(connection);

                // Refresh CardCount for everyone: step 1 above deleted expired Spam cards and
                // step 6 dealt cards, both of which can change how many cards a robot owns.
                RefreshCardCount(connection);

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
    }
}
